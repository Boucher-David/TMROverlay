using TmrOverlay.Core.Fuel.V2;
using TmrOverlay.Core.History;
using TmrOverlay.Core.PitService;
using TmrOverlay.Core.Telemetry.Live;

namespace TmrOverlay.App.History;

// V2-only reader for an eventual Stint-row Tire cell. It reads immutable
// stationary-service observations and returns exact selected/confirmed shape
// evidence. Timing and "these tires fit inside this refuel" advice remain
// owned by a later rule-qualified learned-service model.
internal sealed class FuelV2PitServiceTireHistoryQueryService
{
    private readonly FuelV2HistoryOptions _options;
    private readonly FuelV2HistoryStore _store;
    private readonly Dictionary<string, CachedSelection> _cache = new(StringComparer.Ordinal);
    private readonly object _cacheSync = new();

    public FuelV2PitServiceTireHistoryQueryService(
        FuelV2HistoryOptions options,
        FuelV2HistoryStore store)
    {
        _options = options;
        _store = store;
    }

    public FuelV2TireServiceHistorySelection Lookup(
        HistoricalSessionContext context,
        LivePitServiceRequest currentRequest)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(currentRequest);

        var shape = PitServiceTireShape.From(currentRequest);
        var ruleScope = PitServiceRuleScopeCatalog.FromDCRuleSet(context.Session.DCRuleSet);
        if (shape.RequestedTireCount == 0)
        {
            return Unavailable(
                FuelV2TireServiceHistorySelectionStatus.NoTiresSelected,
                shape,
                ruleScope,
                "No tires are currently selected for the next pit request.");
        }

        if (!_options.Enabled)
        {
            return Unavailable(
                FuelV2TireServiceHistorySelectionStatus.Disabled,
                shape,
                ruleScope,
                "Fuel V2 history is disabled.");
        }

        var car = FuelV2HistoryIdentity.Car(context.Car.CarId, context.Car.CarPath);
        var layout = FuelV2HistoryIdentity.TrackLayout(
            context.Track.TrackId,
            context.Track.TrackName,
            context.Track.TrackDisplayName,
            context.Track.TrackConfigName);
        if (!car.IsExact || !layout.IsExact)
        {
            return Unavailable(
                FuelV2TireServiceHistorySelectionStatus.InexactIdentity,
                shape,
                ruleScope,
                "Exact car and track-layout identity are required for tire-service history.");
        }

        var requestedFamily = FuelV2HistoryIdentity.SessionFamily(
            context.Session.SessionType,
            context.Session.SessionName,
            context.Session.EventType);
        var candidateFamilies = requestedFamily switch
        {
            // Offline Testing has practice-equivalent collection quality but
            // remains a separately labeled provenance family.
            "race" => new[] { "race", "practice", "test" },
            "practice" => new[] { "practice", "test" },
            "test" => new[] { "test", "practice" },
            _ => Array.Empty<string>()
        };
        if (candidateFamilies.Length == 0)
        {
            return Unavailable(
                FuelV2TireServiceHistorySelectionStatus.UnsupportedSessionFamily,
                shape,
                ruleScope,
                "Only race, practice, and Offline Testing sessions can read tire-service history.");
        }

        var cacheKey = CacheKey(car.Key, layout, requestedFamily, shape, ruleScope);
        var revision = _store.Revision;
        lock (_cacheSync)
        {
            if (_cache.TryGetValue(cacheKey, out var cached) && cached.Revision == revision)
            {
                return cached.Selection;
            }
        }

        var selection = LookupCore(car.Key, layout, candidateFamilies, shape, ruleScope);
        lock (_cacheSync)
        {
            // The store may have imported while this bounded read was running.
            // Do not cache a result if its underlying files could have
            // changed; the next refresh will recompute against the new
            // revision.
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

    private FuelV2TireServiceHistorySelection LookupCore(
        string carKey,
        FuelV2HistoryLayoutIdentity layout,
        IReadOnlyList<string> candidateFamilies,
        PitServiceTireShape shape,
        PitServiceRuleScope requestedRuleScope)
    {
        FuelV2HistorySummaryReadResult? lastFailure = null;
        FuelV2TireServiceHistorySelection? requestedOnly = null;
        var sawRuleScopeMismatch = false;
        foreach (var family in candidateFamilies)
        {
            var combo = new FuelV2HistoryComboIdentity
            {
                CarKey = carKey,
                TrackKey = layout.Key,
                TrackLayoutKey = layout.Key,
                TrackLayoutIdentitySource = layout.Source,
                SessionKey = family
            };
            var read = _store.ReadExactSummaries(combo);
            if (!read.IsAvailable)
            {
                lastFailure = read;
                continue;
            }

            var matchingRuleScopeSummaries = read.Summaries
                .Where(summary => SameRuleScope(
                    PitServiceRuleScopeCatalog.FromDCRuleSet(summary.Scope.Session.DCRuleSet),
                    requestedRuleScope))
                .ToArray();
            if (matchingRuleScopeSummaries.Length == 0)
            {
                sawRuleScopeMismatch = true;
                continue;
            }

            var built = BuildProfile(matchingRuleScopeSummaries, shape, requestedRuleScope);
            if (built.Profile.HasConfirmedOutcome)
            {
                return new FuelV2TireServiceHistorySelection(
                    FuelV2TireServiceHistorySelectionStatus.Selected,
                    shape,
                    requestedRuleScope,
                    built.Profile,
                    family,
                    Detail("confirmed", family, built.Profile, requestedRuleScope),
                    built.IgnoredDuplicateObservationCount);
            }

            if (built.Profile.HasOnlyRequestedEvidence)
            {
                requestedOnly ??= new FuelV2TireServiceHistorySelection(
                    FuelV2TireServiceHistorySelectionStatus.RequestedOnly,
                    shape,
                    requestedRuleScope,
                    built.Profile,
                    family,
                    Detail("requested but unproven", family, built.Profile, requestedRuleScope),
                    built.IgnoredDuplicateObservationCount);
            }
        }

        if (requestedOnly is not null)
        {
            return requestedOnly;
        }

        if (sawRuleScopeMismatch)
        {
            return Unavailable(
                FuelV2TireServiceHistorySelectionStatus.RuleScopeMismatch,
                shape,
                requestedRuleScope,
                "Exact tire history exists, but not for the same raw DCRuleSet provenance.");
        }

        var status = ToSelectionStatus(lastFailure?.Status ?? FuelV2HistorySummaryReadStatus.Missing);
        return Unavailable(
            status,
            shape,
            requestedRuleScope,
            $"No exact {shape.DisplayLabel} tire-service history ({status}).");
    }

    private static BuiltProfile BuildProfile(
        IReadOnlyList<FuelV2HistorySummary> summaries,
        PitServiceTireShape shape,
        PitServiceRuleScope ruleScope)
    {
        var builder = new PitServiceTireHistoryProfileBuilder(shape);
        var groupedObservations = summaries
            .SelectMany(summary => summary.StationaryServiceObservations
                .Select(observation => new HistoryObservation(
                    summary,
                    observation,
                    PitServiceTireChangeClassifier.Classify(observation))))
            .GroupBy(item => ObservationFingerprint(item.Summary, item.Observation), StringComparer.Ordinal)
            .ToArray();
        var duplicateCount = groupedObservations.Sum(group => group.Count() - 1);
        foreach (var group in groupedObservations)
        {
            // A reconnect can retain the same stop twice with a different
            // capture boundary. Never let lexicographic file order choose the
            // evidence: keep the strongest execution state for display, carry
            // every qualification flag forward, and make the group ineligible
            // for timing if any duplicate is explicitly contaminated.
            var candidates = group.ToArray();
            var representative = candidates
                .OrderByDescending(item => ExecutionEvidenceRank(item.Assessment.ExecutionState))
                .ThenByDescending(item => item.Assessment.IsCleanForTireTimingLearning)
                .ThenByDescending(item => item.Observation.SampleCount)
                .ThenByDescending(item => item.Observation.EndedAtUtc)
                .ThenBy(item => item.Summary.SummaryId ?? item.Summary.SourceId, StringComparer.OrdinalIgnoreCase)
                .First();
            var mergedFlags = candidates
                .SelectMany(item => item.Assessment.QualificationFlags)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(flag => flag, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var hasTimingContamination = candidates.Any(HasTimingContamination);
            builder.Add(
                representative.Observation,
                mergedFlags,
                disqualifyForTireTimingLearning: hasTimingContamination);
        }

        var profile = builder.Build();
        // Exact executed tire shape is useful evidence today. Raw DCRuleSet is
        // deliberately not proof of service order/overlap, so no profile may
        // become a duration-calibration input until a verified rule contract
        // and comparable component-shape model exist.
        return new BuiltProfile(
            profile with { TimingEligibility = PitServiceTireHistoryTimingEligibility.BlockedRulesUnverified },
            duplicateCount);
    }

    private static string ObservationFingerprint(
        FuelV2HistorySummary summary,
        PitServiceStationaryServiceObservation observation)
    {
        // Reconnect sidecars may contain the same stationary boundary. We can
        // only merge when the occurrence itself was verified; otherwise keep
        // the immutable summary identity in the fingerprint so distinct real
        // sessions are never silently collapsed.
        var occurrence = summary.SessionIntegrity.SessionOccurrenceSupportsReconnectDeduplication
            ? summary.SessionIntegrity.SessionOccurrenceKey
            : summary.SummaryId ?? summary.SourceId;
        return string.Join(
            "|",
            occurrence,
            observation.StartedAtUtc.UtcDateTime.Ticks,
            observation.EndedAtUtc.UtcDateTime.Ticks,
            observation.EntryRequest.LeftFrontTire,
            observation.EntryRequest.RightFrontTire,
            observation.EntryRequest.LeftRearTire,
            observation.EntryRequest.RightRearTire);
    }

    private static int ExecutionEvidenceRank(PitServiceTireExecutionState state)
    {
        return state switch
        {
            PitServiceTireExecutionState.Confirmed => 4,
            PitServiceTireExecutionState.RequestedOnly => 3,
            PitServiceTireExecutionState.Mismatch => 2,
            PitServiceTireExecutionState.Ambiguous => 1,
            _ => 0
        };
    }

    private static bool HasTimingContamination(HistoryObservation item)
    {
        if (item.Assessment.ExecutionState == PitServiceTireExecutionState.Ambiguous)
        {
            return true;
        }

        return item.Assessment.QualificationFlags.Any(flag => string.Equals(
            flag,
            "repair-active",
            StringComparison.OrdinalIgnoreCase)
            || string.Equals(
                flag,
                "telemetry-interrupted",
                StringComparison.OrdinalIgnoreCase));
    }

    private static bool SameRuleScope(PitServiceRuleScope left, PitServiceRuleScope right)
    {
        return string.Equals(left.RuleSetIdentity, right.RuleSetIdentity, StringComparison.OrdinalIgnoreCase);
    }

    private static string Detail(
        string evidence,
        string family,
        PitServiceTireHistoryProfile profile,
        PitServiceRuleScope ruleScope)
    {
        return $"{evidence} {profile.RequestedShape.DisplayLabel} in {family} history; "
            + $"{profile.ConfirmedOutcomeCount} confirmed, {profile.CleanConfirmedOutcomeCount} clean; "
            + $"raw DCRuleSet {ruleScope.RuleSetIdentity} (execution mode unverified)";
    }

    private static FuelV2TireServiceHistorySelectionStatus ToSelectionStatus(
        FuelV2HistorySummaryReadStatus status)
    {
        return status switch
        {
            FuelV2HistorySummaryReadStatus.Unreadable => FuelV2TireServiceHistorySelectionStatus.Unreadable,
            FuelV2HistorySummaryReadStatus.LegacyVersion => FuelV2TireServiceHistorySelectionStatus.LegacyVersion,
            FuelV2HistorySummaryReadStatus.FutureVersion => FuelV2TireServiceHistorySelectionStatus.FutureVersion,
            FuelV2HistorySummaryReadStatus.ScopeMismatch => FuelV2TireServiceHistorySelectionStatus.ScopeMismatch,
            FuelV2HistorySummaryReadStatus.Unclassified => FuelV2TireServiceHistorySelectionStatus.Unclassified,
            _ => FuelV2TireServiceHistorySelectionStatus.Missing
        };
    }

    private static FuelV2TireServiceHistorySelection Unavailable(
        FuelV2TireServiceHistorySelectionStatus status,
        PitServiceTireShape? shape,
        PitServiceRuleScope ruleScope,
        string detail)
    {
        return new FuelV2TireServiceHistorySelection(
            status,
            shape,
            ruleScope,
            Profile: null,
            SelectedSessionFamily: null,
            Detail: detail,
            IgnoredDuplicateObservationCount: 0);
    }

    private static string CacheKey(
        string carKey,
        FuelV2HistoryLayoutIdentity layout,
        string requestedFamily,
        PitServiceTireShape shape,
        PitServiceRuleScope ruleScope)
    {
        return string.Join(
            "|",
            carKey,
            layout.Key,
            layout.Source,
            requestedFamily,
            shape.LeftFront,
            shape.RightFront,
            shape.LeftRear,
            shape.RightRear,
            ruleScope.RuleSetIdentity,
            ruleScope.ExecutionMode,
            ruleScope.Verification);
    }

    private sealed record CachedSelection(long Revision, FuelV2TireServiceHistorySelection Selection);

    private sealed record HistoryObservation(
        FuelV2HistorySummary Summary,
        PitServiceStationaryServiceObservation Observation,
        PitServiceTireChangeAssessment Assessment);

    private sealed record BuiltProfile(
        PitServiceTireHistoryProfile Profile,
        int IgnoredDuplicateObservationCount);
}
