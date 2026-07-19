using TmrOverlay.Core.Fuel.V2;
using TmrOverlay.Core.History;

namespace TmrOverlay.App.History;

// Staged V2-only reader. V1 never receives this result. The factual V2
// presenter may display it only as the explicitly labeled HistoricalNormal
// comparison bucket; strategy callers must opt in with an explicit purpose.
internal sealed class FuelV2HistoryNormalBurnQueryService
{
    private readonly FuelV2HistoryOptions _options;
    private readonly FuelV2HistoryStore _store;
    private readonly Dictionary<string, CachedSelection> _cache = new(StringComparer.Ordinal);
    private readonly object _cacheSync = new();

    public FuelV2HistoryNormalBurnQueryService(
        FuelV2HistoryOptions options,
        FuelV2HistoryStore store)
    {
        _options = options;
        _store = store;
    }

    public FuelV2HistoryNormalBurnSelection Lookup(
        HistoricalSessionContext context,
        FuelV2HistoryLookupPurpose purpose = FuelV2HistoryLookupPurpose.Workbench)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!_options.Enabled)
        {
            return FuelV2HistoryNormalBurnSelection.Unavailable(
                FuelV2HistoryNormalBurnSelectionStatus.Disabled,
                "Fuel V2 history is disabled.");
        }

        if (purpose == FuelV2HistoryLookupPurpose.Strategy && !_options.UseForStrategy)
        {
            return FuelV2HistoryNormalBurnSelection.Unavailable(
                FuelV2HistoryNormalBurnSelectionStatus.StrategyPromotionDisabled,
                "Fuel V2 strategy promotion is disabled.");
        }

        var car = FuelV2HistoryIdentity.Car(context.Car.CarId, context.Car.CarPath);
        var layout = FuelV2HistoryIdentity.TrackLayout(
            context.Track.TrackId,
            context.Track.TrackName,
            context.Track.TrackDisplayName,
            context.Track.TrackConfigName);
        if (!car.IsExact || !layout.IsExact)
        {
            return FuelV2HistoryNormalBurnSelection.Unavailable(
                FuelV2HistoryNormalBurnSelectionStatus.InexactIdentity,
                "Exact car and track-layout identity are required for Fuel V2 history.");
        }

        var requestedFamily = FuelV2HistoryIdentity.SessionFamily(
            context.Session.SessionType,
            context.Session.SessionName,
            context.Session.EventType);
        var candidateFamilies = requestedFamily switch
        {
            // Test is deliberately a separate evidence family. It is useful
            // practice-like fuel evidence, never relabeled as race evidence,
            // and comes after the more directly matching family.
            "race" => new[] { "race", "practice", "test" },
            "practice" => new[] { "practice", "test" },
            "test" => new[] { "test", "practice" },
            _ => Array.Empty<string>()
        };
        if (candidateFamilies.Length == 0)
        {
            return FuelV2HistoryNormalBurnSelection.Unavailable(
                FuelV2HistoryNormalBurnSelectionStatus.UnsupportedSessionFamily,
                "Only race, practice, and Offline Testing sessions can select normal Fuel V2 history.");
        }

        var cacheKey = CacheKey(car.Key, layout, requestedFamily, purpose);
        var revision = _store.Revision;
        lock (_cacheSync)
        {
            if (_cache.TryGetValue(cacheKey, out var cached) && cached.Revision == revision)
            {
                return cached.Selection;
            }
        }

        var selection = LookupCore(car.Key, layout, candidateFamilies, purpose);
        lock (_cacheSync)
        {
            // Do not retain an answer produced while a sidecar import or
            // maintenance rebuild changed the exact aggregate beneath us.
            var currentRevision = _store.Revision;
            if (currentRevision == revision)
            {
                _cache[cacheKey] = new CachedSelection(currentRevision, selection);
            }
            else
            {
                _cache.Remove(cacheKey);
            }
        }

        return selection;
    }

    private FuelV2HistoryNormalBurnSelection LookupCore(
        string carKey,
        FuelV2HistoryLayoutIdentity layout,
        IReadOnlyList<string> candidateFamilies,
        FuelV2HistoryLookupPurpose purpose)
    {
        FuelV2HistoryAggregateReadResult? lastFailure = null;
        FuelV2HistoryAggregateReadResult? lastNonMissingFailure = null;
        foreach (var sessionFamily in candidateFamilies)
        {
            var combo = new FuelV2HistoryComboIdentity
            {
                CarKey = carKey,
                // Aggregate paths use TrackLayoutKey. TrackKey is retained by
                // persisted summaries for diagnostics but is not an exact
                // layout proof and is never used as a selector fallback.
                TrackKey = layout.Key,
                TrackLayoutKey = layout.Key,
                TrackLayoutIdentitySource = layout.Source,
                SessionKey = sessionFamily
            };
            var read = _store.ReadExactAggregate(combo);
            if (!read.IsAvailable)
            {
                lastFailure = read;
                if (read.Status != FuelV2HistoryAggregateReadStatus.Missing)
                {
                    lastNonMissingFailure = read;
                }
                continue;
            }

            var aggregate = read.Aggregate!;
            var sampleCount = aggregate.AcceptedLapFuelPerLapLiters.SampleCount;
            var detail = $"classified {sessionFamily} history; {sampleCount} accepted lap windows; "
                + $"{aggregate.LearningEligibleSessionCount} learning-eligible sessions";
            var canDriveAdvice = purpose == FuelV2HistoryLookupPurpose.Strategy;
            var burn = FuelV2Scalar.From(
                aggregate.AcceptedLapFuelPerLapLiters.Mean,
                detail,
                FuelV2Confidence.Seeded,
                displayEligible: true,
                cleanBaselineEligible: false,
                burnBucketId: FuelV2BurnBucketId.HistoricalNormal,
                burnSource: FuelV2BurnSource.HistoricalNormal,
                sampleCount: sampleCount,
                strategyEligible: canDriveAdvice);
            return new FuelV2HistoryNormalBurnSelection(
                FuelV2HistoryNormalBurnSelectionStatus.Selected,
                burn,
                sessionFamily,
                detail,
                aggregate.ClassifiedSessionCount,
                aggregate.LearningEligibleSessionCount,
                sampleCount,
                aggregate.UpdatedAtUtc,
                CanSeedPlan: true,
                CanDriveAdvice: canDriveAdvice);
        }

        var status = ToSelectionStatus(
            (lastNonMissingFailure ?? lastFailure)?.Status ?? FuelV2HistoryAggregateReadStatus.Missing);
        return FuelV2HistoryNormalBurnSelection.Unavailable(
            status,
            $"No usable exact race/practice/test Fuel V2 history ({status}).");
    }

    private static string CacheKey(
        string carKey,
        FuelV2HistoryLayoutIdentity layout,
        string requestedFamily,
        FuelV2HistoryLookupPurpose purpose)
    {
        return string.Join(
            "|",
            carKey,
            layout.Key,
            layout.Source,
            requestedFamily,
            purpose.ToString());
    }

    private sealed record CachedSelection(long Revision, FuelV2HistoryNormalBurnSelection Selection);

    private static FuelV2HistoryNormalBurnSelectionStatus ToSelectionStatus(FuelV2HistoryAggregateReadStatus status)
    {
        return status switch
        {
            FuelV2HistoryAggregateReadStatus.Unreadable => FuelV2HistoryNormalBurnSelectionStatus.Unreadable,
            FuelV2HistoryAggregateReadStatus.LegacyVersion => FuelV2HistoryNormalBurnSelectionStatus.LegacyVersion,
            FuelV2HistoryAggregateReadStatus.FutureVersion => FuelV2HistoryNormalBurnSelectionStatus.FutureVersion,
            FuelV2HistoryAggregateReadStatus.ScopeMismatch => FuelV2HistoryNormalBurnSelectionStatus.ScopeMismatch,
            FuelV2HistoryAggregateReadStatus.Unclassified => FuelV2HistoryNormalBurnSelectionStatus.Unclassified,
            FuelV2HistoryAggregateReadStatus.NotLearningEligible => FuelV2HistoryNormalBurnSelectionStatus.NotLearningEligible,
            FuelV2HistoryAggregateReadStatus.MetricUnavailable => FuelV2HistoryNormalBurnSelectionStatus.MetricUnavailable,
            _ => FuelV2HistoryNormalBurnSelectionStatus.Missing
        };
    }
}
