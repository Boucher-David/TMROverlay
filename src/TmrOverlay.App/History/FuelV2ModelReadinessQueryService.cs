using TmrOverlay.App.Telemetry;
using TmrOverlay.Core.Fuel.V2;
using TmrOverlay.Core.History;
using TmrOverlay.Core.PitService;

namespace TmrOverlay.App.History;

// Test/Practice-only projection over exact car/layout Fuel V2 history plus
// closed observations from the same verified active session. This deliberately
// reports collection facts, not strategy readiness: DCRuleSet remains raw
// provenance and service ordering/timing stay unverified even after every
// collection goal below is satisfied.
internal sealed class FuelV2ModelReadinessQueryService
{
    private const int RequiredSingleTireShapes = 4;
    private const double SmallFillMaximumCapacityFraction = 0.35d;
    private const double LargeFillMinimumCapacityFraction = 0.65d;
    private readonly FuelV2HistoryOptions _options;
    private readonly FuelV2HistoryStore _store;
    private readonly IFuelV2CurrentSessionEvidenceSource? _currentSessionEvidenceSource;
    private readonly Dictionary<string, CachedReadiness> _cache = new(StringComparer.Ordinal);
    private readonly object _cacheSync = new();

    public FuelV2ModelReadinessQueryService(
        FuelV2HistoryOptions options,
        FuelV2HistoryStore store,
        IFuelV2CurrentSessionEvidenceSource? currentSessionEvidenceSource = null)
    {
        _options = options;
        _store = store;
        _currentSessionEvidenceSource = currentSessionEvidenceSource;
    }

    public FuelV2ModelReadiness Lookup(HistoricalSessionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!_options.Enabled)
        {
            return FuelV2ModelReadiness.Hidden("Fuel V2 history is disabled.");
        }

        var car = FuelV2HistoryIdentity.Car(context.Car.CarId, context.Car.CarPath);
        var layout = FuelV2HistoryIdentity.TrackLayout(
            context.Track.TrackId,
            context.Track.TrackName,
            context.Track.TrackDisplayName,
            context.Track.TrackConfigName);
        if (!car.IsExact || !layout.IsExact)
        {
            return FuelV2ModelReadiness.Hidden("Exact car and track-layout identity are required.");
        }

        var requestedFamily = FuelV2HistoryIdentity.SessionFamily(
            context.Session.SessionType,
            context.Session.SessionName,
            context.Session.EventType);
        var candidateFamilies = requestedFamily switch
        {
            "test" => new[] { "test", "practice" },
            "practice" => new[] { "practice", "test" },
            _ => Array.Empty<string>()
        };
        if (candidateFamilies.Length == 0)
        {
            return FuelV2ModelReadiness.Hidden("Model readiness is shown only in Test and Practice.");
        }

        var cacheKey = string.Join("|", car.Key, layout.Key, layout.Source, requestedFamily);
        var historyRevision = _store.Revision;
        var currentSession = _currentSessionEvidenceSource?.GetCurrentSessionEvidenceSnapshot();
        var currentSessionRevision = currentSession?.Revision ?? 0L;
        lock (_cacheSync)
        {
            if (_cache.TryGetValue(cacheKey, out var cached)
                && cached.HistoryRevision == historyRevision
                && cached.CurrentSessionRevision == currentSessionRevision)
            {
                return cached.Readiness;
            }
        }

        var readiness = Build(car.Key, layout, candidateFamilies, context, currentSession?.Evidence);
        lock (_cacheSync)
        {
            if (_store.Revision == historyRevision
                && (_currentSessionEvidenceSource?.GetCurrentSessionEvidenceSnapshot().Revision ?? 0L) == currentSessionRevision)
            {
                _cache[cacheKey] = new CachedReadiness(historyRevision, currentSessionRevision, readiness);
            }
            else
            {
                _cache.Remove(cacheKey);
            }
        }

        return readiness;
    }

    private FuelV2ModelReadiness Build(
        string carKey,
        FuelV2HistoryLayoutIdentity layout,
        IReadOnlyList<string> candidateFamilies,
        HistoricalSessionContext context,
        FuelV2CurrentSessionEvidence? currentSessionEvidence)
    {
        var evidence = new List<ReadinessEvidence>();
        var familiesWithEvidence = new List<string>();
        foreach (var family in candidateFamilies)
        {
            var read = _store.ReadExactSummaries(new FuelV2HistoryComboIdentity
            {
                CarKey = carKey,
                TrackKey = layout.Key,
                TrackLayoutKey = layout.Key,
                TrackLayoutIdentitySource = layout.Source,
                SessionKey = family
            });
            if (!read.IsAvailable)
            {
                continue;
            }

            evidence.AddRange(read.Summaries.Select(summary => new ReadinessEvidence(
                Source: FuelV2ModelReadinessEvidenceSource.DurableHistory,
                EffectiveCapacityLiters: summary.FuelCapacity.EffectiveSessionCapacityLiters,
                StationaryServiceObservations: summary.StationaryServiceObservations,
                PitRouteObservations: summary.PitRouteObservations)));
            familiesWithEvidence.Add(family);
        }

        var currentSessionIncluded = currentSessionEvidence is not null
            && MatchesCurrentSessionEvidence(context, carKey, layout, candidateFamilies, currentSessionEvidence);
        if (currentSessionIncluded)
        {
            evidence.Add(new ReadinessEvidence(
                Source: FuelV2ModelReadinessEvidenceSource.CurrentSession,
                EffectiveCapacityLiters: currentSessionEvidence!.Scope.FuelCapacity.EffectiveSessionCapacityLiters,
                StationaryServiceObservations: currentSessionEvidence.StationaryServiceObservations,
                PitRouteObservations: currentSessionEvidence.PitRouteObservations));
            familiesWithEvidence.Add(currentSessionEvidence.SessionIdentity.SessionFamily);
        }

        var routeObservations = evidence.SelectMany(item => item.PitRouteObservations).ToArray();
        var stationary = evidence.SelectMany(item => item.StationaryServiceObservations).ToArray();
        var confirmedShapes = stationary
            .Select(PitServiceTireChangeClassifier.Classify)
            .Where(assessment => assessment.ExecutionState == PitServiceTireExecutionState.Confirmed)
            .Select(assessment => assessment.RequestedShape)
            .ToArray();

        var routeHasEntryLeg = routeObservations.Any(route => route.HasQualifiedEntryToBoxLeg);
        var routeHasExitLeg = routeObservations.Any(route => route.HasQualifiedBoxToExitLeg);
        var completeRouteCount = routeObservations.Count(route => route.HasCompleteRoute);
        var assignedStallCount = routeObservations.Count(route => route.Assignment.PitBoxIdentity is not null);
        var pitLanePassCount = routeObservations.Count(route => route.HasCompletePitLanePass);

        var qualifiedRefuels = evidence
            .SelectMany(item => item.StationaryServiceObservations
                .Where(IsQualifiedRefuelObservation)
                .Select(observation => new QualifiedRefuel(item.EffectiveCapacityLiters, observation)))
            .ToArray();
        var smallRefuelCount = qualifiedRefuels.Count(IsSmallFill);
        var largeRefuelCount = qualifiedRefuels.Count(IsLargeFill);
        var fuelFlowCount = qualifiedRefuels.Length;
        var fuelOnlyCount = qualifiedRefuels.Count(IsFuelOnly);
        var fuelWithTiresCount = qualifiedRefuels.Count(IsFuelWithConfirmedFourTires);

        var confirmedSingles = confirmedShapes.Where(IsSingle).Distinct().Count();
        var confirmedFronts = confirmedShapes.Any(shape => shape.IsFrontPair);
        var confirmedRears = confirmedShapes.Any(shape => shape.IsRearPair);
        var confirmedLeft = confirmedShapes.Any(shape => shape.IsLeftPair);
        var confirmedRight = confirmedShapes.Any(shape => shape.IsRightPair);
        var confirmedAllFour = confirmedShapes.Count(shape => shape.IsAllFour);
        var sourceDetail = EvidenceDetail(evidence);
        var rows = new[]
        {
            new FuelV2ModelReadinessRow(
                "Pit route",
                $"Local route checkpoints · {sourceDetail}",
                [
                    Cell("To box", routeHasEntryLeg, routeHasEntryLeg ? "observed" : "need route"),
                    Cell("From box", routeHasExitLeg, routeHasExitLeg ? "observed" : "need exit"),
                    Cell("Full stop", completeRouteCount > 0, completeRouteCount > 0 ? $"{completeRouteCount} qualified" : "need 1"),
                    Cell("Pit box", assignedStallCount > 0, assignedStallCount > 0 ? "detected" : "not reported"),
                    OptionalCell("Pit lane pass", pitLanePassCount > 0, pitLanePassCount > 0 ? $"{pitLanePassCount} observed" : "optional")
                ]),
            new FuelV2ModelReadinessRow(
                "Refuel",
                $"Stationary local service · {sourceDetail}",
                [
                    Cell("Small fill", smallRefuelCount > 0, smallRefuelCount > 0 ? "measured" : "need low fill"),
                    Cell("Large fill", largeRefuelCount > 0, largeRefuelCount > 0 ? "measured" : "need high fill"),
                    Cell("Fuel flow", fuelFlowCount > 0, fuelFlowCount > 0 ? "timed" : "need timing"),
                    Cell("Fuel only", fuelOnlyCount > 0, fuelOnlyCount > 0 ? "confirmed" : "need service"),
                    Cell("Fuel + tires", fuelWithTiresCount > 0, fuelWithTiresCount > 0 ? "confirmed" : "need service")
                ]),
            new FuelV2ModelReadinessRow(
                "Tires",
                $"Counter-proven outcomes · {sourceDetail}",
                [
                    Cell("1 tire", confirmedSingles >= RequiredSingleTireShapes, $"{Math.Min(confirmedSingles, RequiredSingleTireShapes)}/{RequiredSingleTireShapes} corners"),
                    Cell("Fronts", confirmedFronts, confirmedFronts ? "confirmed" : "need front pair"),
                    Cell("Rears", confirmedRears, confirmedRears ? "confirmed" : "need rear pair"),
                    Cell("Left", confirmedLeft, confirmedLeft ? "confirmed" : "need left pair"),
                    Cell("Right", confirmedRight, confirmedRight ? "confirmed" : "need right pair"),
                    Cell("4 tires", confirmedAllFour > 0, confirmedAllFour > 0 ? "confirmed" : "need service")
                ])
        };

        // Required means *collectable factual evidence*. Raw requests and
        // DCRuleSet remain preserved provenance, never green readiness. The
        // optional pit-lane pass can calibrate travel separately, but a clean
        // stopped route is sufficient for this initial checklist to retire.
        var collectionComplete = completeRouteCount > 0
            && assignedStallCount > 0
            && smallRefuelCount > 0
            && largeRefuelCount > 0
            && fuelOnlyCount > 0
            && fuelWithTiresCount > 0
            && confirmedSingles >= RequiredSingleTireShapes
            && confirmedFronts
            && confirmedRears
            && confirmedLeft
            && confirmedRight
            && confirmedAllFour > 0;
        return new FuelV2ModelReadiness(
            IsVisible: !collectionComplete,
            IsCollectionComplete: collectionComplete,
            SourceFamilies: familiesWithEvidence.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            Detail: evidence.Count == 0
                ? "no exact Test/Practice history or closed current-session evidence"
                : sourceDetail,
            Rows: rows,
            EvidenceSources: evidence
                .Select(item => item.Source)
                .Distinct()
                .ToArray(),
            CurrentSessionEvidenceUpdatedAtUtc: currentSessionIncluded
                ? currentSessionEvidence!.UpdatedAtUtc
                : null);
    }

    private static bool MatchesCurrentSessionEvidence(
        HistoricalSessionContext context,
        string carKey,
        FuelV2HistoryLayoutIdentity layout,
        IReadOnlyList<string> candidateFamilies,
        FuelV2CurrentSessionEvidence currentSessionEvidence)
    {
        var identity = currentSessionEvidence.SessionIdentity;
        if (!identity.IsReadyToRecord
            || !string.Equals(identity.CarKey, carKey, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(identity.TrackLayout.Key, layout.Key, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(identity.TrackLayout.Source, layout.Source, StringComparison.OrdinalIgnoreCase)
            || !candidateFamilies.Contains(identity.SessionFamily, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        // The recorder's session boundary uses these same verified occurrence
        // values. Require the current presentation context to agree before a
        // completed event can affect this session's factual matrix.
        var occurrenceMatches = (identity.CurrentSessionNum is { } currentSessionNum
                && context.Session.CurrentSessionNum == currentSessionNum)
            || (identity.SessionNum is { } sessionNum
                && context.Session.SessionNum == sessionNum);
        return occurrenceMatches
            && MatchesKnown(identity.SessionId, context.Session.SessionId)
            && MatchesKnown(identity.SubSessionId, context.Session.SubSessionId);
    }

    private static bool MatchesKnown(int? recorded, int? current)
    {
        // A recorded verifier is never allowed to become a wildcard just
        // because the presenter has lost that part of the current context.
        // It is safer to withhold the in-memory evidence in that case.
        return recorded is null || current == recorded;
    }

    private static string EvidenceDetail(IReadOnlyList<ReadinessEvidence> evidence)
    {
        var durableFamilies = evidence
            .Where(item => item.Source == FuelV2ModelReadinessEvidenceSource.DurableHistory)
            .Select(_ => "exact Test/Practice history")
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var hasCurrentSession = evidence.Any(item => item.Source == FuelV2ModelReadinessEvidenceSource.CurrentSession);
        return hasCurrentSession
            ? durableFamilies.Length > 0
                ? "exact history + current session"
                : "current session"
            : durableFamilies.Length > 0
                ? durableFamilies[0]
                : "no collection evidence";
    }

    private static FuelV2ModelReadinessCell Cell(
        string label,
        bool observed,
        string value,
        bool required = true)
    {
        return new FuelV2ModelReadinessCell(
            label,
            value,
            observed ? FuelV2ModelReadinessState.Confirmed : FuelV2ModelReadinessState.Missing,
            required);
    }

    private static FuelV2ModelReadinessCell OptionalCell(
        string label,
        bool observed,
        string value)
    {
        return new FuelV2ModelReadinessCell(
            label,
            value,
            observed ? FuelV2ModelReadinessState.Confirmed : FuelV2ModelReadinessState.Missing,
            IsRequired: false);
    }

    private static bool IsQualifiedRefuelObservation(PitServiceStationaryServiceObservation observation)
    {
        return observation.SawPitStall
            && observation.EntryRequest.Fuel
            && observation.PositiveFuelAddedLiters is > 0d
            && observation.FuelFlowDurationSeconds is > 0d
            && observation.DurationSeconds > 0d
            && !observation.RequestChangedDuringService
            && !observation.SawRepair
            && !observation.QualificationFlags.Contains("telemetry-interrupted", StringComparer.OrdinalIgnoreCase)
            && !observation.QualificationFlags.Contains("fuel-nonmonotonic", StringComparer.OrdinalIgnoreCase)
            && !observation.QualificationFlags.Contains("local-fuel-incomplete", StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsSmallFill(QualifiedRefuel refuel)
    {
        return FillFraction(refuel) is { } fraction
            && fraction <= SmallFillMaximumCapacityFraction;
    }

    private static bool IsLargeFill(QualifiedRefuel refuel)
    {
        return FillFraction(refuel) is { } fraction
            && fraction >= LargeFillMinimumCapacityFraction;
    }

    private static double? FillFraction(QualifiedRefuel refuel)
    {
        var capacity = refuel.EffectiveCapacityLiters;
        return capacity is > 0d && refuel.Observation.PositiveFuelAddedLiters is { } added && added > 0d
            ? added / capacity
            : null;
    }

    private static bool IsFuelOnly(QualifiedRefuel refuel)
    {
        var assessment = PitServiceTireChangeClassifier.Classify(refuel.Observation);
        return assessment.ExecutionState == PitServiceTireExecutionState.NoTiresRequested
            && assessment.ObservedCounterDelta?.HasExactCornerUsedDeltas == true;
    }

    private static bool IsFuelWithConfirmedFourTires(QualifiedRefuel refuel)
    {
        var assessment = PitServiceTireChangeClassifier.Classify(refuel.Observation);
        return assessment.ExecutionState == PitServiceTireExecutionState.Confirmed
            && assessment.RequestedShape.IsAllFour
            && assessment.IsCleanForTireTimingLearning;
    }

    private static bool IsSingle(PitServiceTireShape shape) => shape.RequestedTireCount == 1;

    private sealed record ReadinessEvidence(
        FuelV2ModelReadinessEvidenceSource Source,
        double? EffectiveCapacityLiters,
        IReadOnlyList<PitServiceStationaryServiceObservation> StationaryServiceObservations,
        IReadOnlyList<PitServiceRouteObservation> PitRouteObservations);

    private sealed record QualifiedRefuel(
        double? EffectiveCapacityLiters,
        PitServiceStationaryServiceObservation Observation);

    private sealed record CachedReadiness(
        long HistoryRevision,
        long CurrentSessionRevision,
        FuelV2ModelReadiness Readiness);
}

internal enum FuelV2ModelReadinessState
{
    Missing = 0,
    Confirmed = 1
}

internal enum FuelV2ModelReadinessEvidenceSource
{
    DurableHistory = 0,
    CurrentSession = 1
}

internal sealed record FuelV2ModelReadinessCell(
    string Label,
    string Value,
    FuelV2ModelReadinessState State,
    bool IsRequired);

internal sealed record FuelV2ModelReadinessRow(
    string Label,
    string Detail,
    IReadOnlyList<FuelV2ModelReadinessCell> Cells);

internal sealed record FuelV2ModelReadiness(
    bool IsVisible,
    bool IsCollectionComplete,
    IReadOnlyList<string> SourceFamilies,
    string Detail,
    IReadOnlyList<FuelV2ModelReadinessRow> Rows,
    IReadOnlyList<FuelV2ModelReadinessEvidenceSource>? EvidenceSources = null,
    DateTimeOffset? CurrentSessionEvidenceUpdatedAtUtc = null)
{
    public static FuelV2ModelReadiness Hidden(string detail) => new(
        IsVisible: false,
        IsCollectionComplete: false,
        SourceFamilies: [],
        Detail: detail,
        Rows: [],
        EvidenceSources: []);
}
