namespace TmrOverlay.Core.Fuel.V2;

// This is the first strategy-policy owner above the factual burn buckets. It
// deliberately selects a normal race-burn baseline only; Max/Min/Quali remain
// factual comparison evidence and are never silently promoted into a normal
// stint profile. The caller owns lifecycle reset when car/layout/session scope
// changes, so a prior selection cannot leak into a different race.
internal static class FuelV2RaceBurnEvidenceSelector
{
    public static FuelV2RaceBurnEvidenceSelection From(
        FuelV2FuelPerLapWindows windows,
        FuelV2RaceBurnEvidenceSelection? previous = null,
        FuelV2RaceBurnEvidenceSelectionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(windows);

        var safeOptions = options ?? FuelV2RaceBurnEvidenceSelectionOptions.Default;
        var history = Candidate(
            windows.HistoricalNormal,
            FuelV2BurnBucketId.HistoricalNormal,
            safeOptions.IncludeDisplayOnlyEvidenceForShadowCapture);
        var liveTen = Candidate(
            windows.TenLapAverage,
            FuelV2BurnBucketId.TenLapAverage,
            safeOptions.IncludeDisplayOnlyEvidenceForShadowCapture);
        var liveFive = Candidate(
            windows.FiveLapAverage,
            FuelV2BurnBucketId.FiveLapAverage,
            safeOptions.IncludeDisplayOnlyEvidenceForShadowCapture);
        var liveLast = Candidate(
            windows.Last,
            FuelV2BurnBucketId.Last,
            safeOptions.IncludeDisplayOnlyEvidenceForShadowCapture);
        // Never let a longer, lower average hide a recent accepted increase.
        // Both windows are useful evidence, but fuel safety chooses the higher
        // confirmed live burn; only the lower side of a change is gated by the
        // ten-sample policy below.
        var confirmedLive = MostConservative(liveTen, liveFive);
        var live = confirmedLive ?? liveLast;

        // History is the best initial normal-race baseline. A lone live lap is
        // useful when history is absent, but it must not rewrite an exact
        // history baseline. Five clean laps may confirm a higher burn
        // immediately; a lower burn waits for the longer live window so the
        // strategy cannot drop fuel or delete a stop on a tiny sample.
        var candidate = confirmedLive ?? history ?? liveLast;
        if (candidate is null)
        {
            return FuelV2RaceBurnEvidenceSelection.Unavailable("no normal race burn evidence");
        }

        var prior = PriorUsable(previous);
        // A missing current history read is not a license to carry an old
        // history value forward. The lifecycle owner still resets on a scope
        // change; this additional guard also prevents a transient/missing
        // reader result from masquerading as fresh exact-layout evidence.
        var conservativeBaseline = prior?.BucketId == FuelV2BurnBucketId.HistoricalNormal && history is null
            ? null
            : prior ?? history;
        if (ShouldHoldLowerLiveCandidate(candidate, conservativeBaseline, safeOptions))
        {
            return Held(conservativeBaseline!, candidate);
        }

        return Selected(candidate, live ?? candidate, history is null);
    }

    private static FuelV2RaceBurnEvidenceSelection Held(
        FuelV2RaceBurnCandidate baseline,
        FuelV2RaceBurnCandidate candidate)
    {
        return new FuelV2RaceBurnEvidenceSelection(
            Burn: baseline.Burn,
            BurnBucketId: baseline.BucketId,
            CandidateBurn: candidate.Burn,
            CandidateBucketId: candidate.BucketId,
            State: FuelV2RaceBurnEvidenceSelectionState.HeldConservative,
            CanSeedPlan: baseline.Burn.StrategyEligible,
            CanDriveAdvice: baseline.Burn.StrategyEligible,
            StateFlags:
            [
                FuelV2RaceBurnEvidenceSelectionFlag.LiveRaceEvidence,
                FuelV2RaceBurnEvidenceSelectionFlag.ConservativeDecreaseHeld
            ],
            Reason: $"held {FuelV2BurnBucketCatalog.Label(baseline.BucketId)} until lower live burn has a full 10L window");
    }

    private static FuelV2RaceBurnEvidenceSelection Selected(
        FuelV2RaceBurnCandidate candidate,
        FuelV2RaceBurnCandidate observedCandidate,
        bool historyUnavailable)
    {
        var isLive = candidate.BucketId is FuelV2BurnBucketId.Last
            or FuelV2BurnBucketId.FiveLapAverage
            or FuelV2BurnBucketId.TenLapAverage;
        var isConfirmedLive = candidate.BucketId is FuelV2BurnBucketId.FiveLapAverage
            or FuelV2BurnBucketId.TenLapAverage;
        var state = candidate.BucketId switch
        {
            FuelV2BurnBucketId.HistoricalNormal => FuelV2RaceBurnEvidenceSelectionState.HistoricalSeed,
            FuelV2BurnBucketId.Last => FuelV2RaceBurnEvidenceSelectionState.LiveProvisional,
            _ => FuelV2RaceBurnEvidenceSelectionState.LiveConfirmed
        };
        var flags = new List<FuelV2RaceBurnEvidenceSelectionFlag>();
        if (isLive)
        {
            flags.Add(FuelV2RaceBurnEvidenceSelectionFlag.LiveRaceEvidence);
        }
        else
        {
            flags.Add(FuelV2RaceBurnEvidenceSelectionFlag.HistoricalNormalEvidence);
        }

        if (historyUnavailable && isLive)
        {
            flags.Add(FuelV2RaceBurnEvidenceSelectionFlag.NoHistoricalBaseline);
        }

        var canSeedPlan = candidate.Burn.StrategyEligible;
        return new FuelV2RaceBurnEvidenceSelection(
            Burn: candidate.Burn,
            BurnBucketId: candidate.BucketId,
            CandidateBurn: observedCandidate.Burn,
            CandidateBucketId: observedCandidate.BucketId,
            State: state,
            CanSeedPlan: canSeedPlan,
            CanDriveAdvice: canSeedPlan
                && (isConfirmedLive || candidate.BucketId == FuelV2BurnBucketId.HistoricalNormal),
            StateFlags: flags,
            Reason: state switch
            {
                FuelV2RaceBurnEvidenceSelectionState.HistoricalSeed => "exact classified history normal",
                FuelV2RaceBurnEvidenceSelectionState.LiveProvisional => "single accepted live race burn span",
                _ => $"accepted live {FuelV2BurnBucketCatalog.Label(candidate.BucketId)} race window"
            });
    }

    private static bool ShouldHoldLowerLiveCandidate(
        FuelV2RaceBurnCandidate candidate,
        FuelV2RaceBurnCandidate? conservativeBaseline,
        FuelV2RaceBurnEvidenceSelectionOptions options)
    {
        if (conservativeBaseline is null
            || candidate.BucketId is not (FuelV2BurnBucketId.FiveLapAverage or FuelV2BurnBucketId.TenLapAverage)
            || candidate.Burn.Value is not { } candidateValue
            || conservativeBaseline.Burn.Value is not { } baselineValue
            || candidateValue >= baselineValue)
        {
            return false;
        }

        var minimumSamples = Math.Max(10, options.MinimumLiveSamplesToLowerBurn);
        return candidate.Burn.SampleCount is not { } samples
            || samples < minimumSamples;
    }

    private static FuelV2RaceBurnCandidate? MostConservative(
        FuelV2RaceBurnCandidate? first,
        FuelV2RaceBurnCandidate? second)
    {
        if (first is null)
        {
            return second;
        }

        if (second is null)
        {
            return first;
        }

        return first.Burn.Value >= second.Burn.Value ? first : second;
    }

    private static FuelV2RaceBurnCandidate? PriorUsable(FuelV2RaceBurnEvidenceSelection? selection)
    {
        return selection is { IsAvailable: true, Burn: { } burn, BurnBucketId: { } bucketId }
            ? Candidate(burn, bucketId)
            : null;
    }

    private static FuelV2RaceBurnCandidate? Candidate(
        FuelV2Scalar? burn,
        FuelV2BurnBucketId bucketId,
        bool includeDisplayOnlyEvidence)
    {
        return burn is { HasValue: true, HasTypedBurnEvidence: true }
            && (burn.StrategyEligible || includeDisplayOnlyEvidence)
            && burn.BurnBucketId == bucketId
            && HasExpectedSource(burn, bucketId)
            && burn.Value is > 0d
            ? new FuelV2RaceBurnCandidate(bucketId, burn)
            : null;
    }

    private static bool HasExpectedSource(FuelV2Scalar burn, FuelV2BurnBucketId bucketId)
    {
        return (bucketId, burn.BurnSource) switch
        {
            (FuelV2BurnBucketId.Last, FuelV2BurnSource.LiveLastLap) => true,
            (FuelV2BurnBucketId.FiveLapAverage, FuelV2BurnSource.LiveFiveLapAverage) => true,
            (FuelV2BurnBucketId.TenLapAverage, FuelV2BurnSource.LiveTenLapAverage) => true,
            (FuelV2BurnBucketId.HistoricalNormal, FuelV2BurnSource.HistoricalNormal) => true,
            _ => false
        };
    }

    private sealed record FuelV2RaceBurnCandidate(
        FuelV2BurnBucketId BucketId,
        FuelV2Scalar Burn);
}

// Stateful use is intentionally opt-in. The live telemetry store must remain a
// factual collector; a future Fuel V2 strategy/lifecycle service owns one of
// these trackers and resets it at its own session/condition boundaries.
internal sealed class FuelV2RaceBurnEvidenceSelectionTracker
{
    private FuelV2RaceBurnEvidenceSelection? _previous;

    public FuelV2RaceBurnEvidenceSelection Select(
        FuelV2FuelPerLapWindows windows,
        FuelV2RaceBurnEvidenceSelectionOptions? options = null)
    {
        var selection = FuelV2RaceBurnEvidenceSelector.From(windows, _previous, options);
        _previous = selection;
        return selection;
    }

    public void Reset()
    {
        _previous = null;
    }
}

internal sealed record FuelV2RaceBurnEvidenceSelectionOptions(
    int MinimumLiveSamplesToLowerBurn = 10,
    // Shadow capture may evaluate the same display-only historical bucket
    // shown by the factual V2 overlay. It must remain explicit and opt-in:
    // ordinary selector callers still require strategy-eligible evidence.
    bool IncludeDisplayOnlyEvidenceForShadowCapture = false)
{
    public static FuelV2RaceBurnEvidenceSelectionOptions Default { get; } = new();
}

internal sealed record FuelV2RaceBurnEvidenceSelection(
    FuelV2Scalar? Burn,
    FuelV2BurnBucketId? BurnBucketId,
    FuelV2Scalar? CandidateBurn,
    FuelV2BurnBucketId? CandidateBucketId,
    FuelV2RaceBurnEvidenceSelectionState State,
    bool CanSeedPlan,
    bool CanDriveAdvice,
    IReadOnlyList<FuelV2RaceBurnEvidenceSelectionFlag> StateFlags,
    string Reason)
{
    public bool IsAvailable
    {
        get
        {
            var burn = Burn;
            var evidenceBucket = burn?.BurnBucketId;
            return burn is not null
                && burn.HasValue
                && burn.HasTypedBurnEvidence
                && evidenceBucket is not null
                && BurnBucketId == evidenceBucket;
        }
    }

    public static FuelV2RaceBurnEvidenceSelection Unavailable(string reason)
    {
        return new FuelV2RaceBurnEvidenceSelection(
            Burn: null,
            BurnBucketId: null,
            CandidateBurn: null,
            CandidateBucketId: null,
            State: FuelV2RaceBurnEvidenceSelectionState.Unavailable,
            CanSeedPlan: false,
            CanDriveAdvice: false,
            StateFlags: [],
            Reason: string.IsNullOrWhiteSpace(reason) ? "normal race burn unavailable" : reason);
    }
}

internal enum FuelV2RaceBurnEvidenceSelectionState
{
    Unavailable = 0,
    HistoricalSeed = 1,
    LiveProvisional = 2,
    LiveConfirmed = 3,
    HeldConservative = 4
}

internal enum FuelV2RaceBurnEvidenceSelectionFlag
{
    HistoricalNormalEvidence = 0,
    LiveRaceEvidence = 1,
    NoHistoricalBaseline = 2,
    ConservativeDecreaseHeld = 3
}
