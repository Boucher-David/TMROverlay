using TmrOverlay.Core.Fuel.V2;
using TmrOverlay.Core.History;
using TmrOverlay.Core.PitService;

namespace TmrOverlay.App.History;

// Test/Practice-only projection over immutable, exact car/layout Fuel V2
// summaries. This deliberately reports collection facts, not strategy
// readiness: DCRuleSet remains raw provenance and service ordering/timing stay
// unverified even after every collection goal below is satisfied.
internal sealed class FuelV2ModelReadinessQueryService
{
    private const int RequiredSingleTireShapes = 4;
    private const double SmallFillMaximumCapacityFraction = 0.35d;
    private const double LargeFillMinimumCapacityFraction = 0.65d;
    private readonly FuelV2HistoryOptions _options;
    private readonly FuelV2HistoryStore _store;
    private readonly Dictionary<string, CachedReadiness> _cache = new(StringComparer.Ordinal);
    private readonly object _cacheSync = new();

    public FuelV2ModelReadinessQueryService(
        FuelV2HistoryOptions options,
        FuelV2HistoryStore store)
    {
        _options = options;
        _store = store;
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
        var revision = _store.Revision;
        lock (_cacheSync)
        {
            if (_cache.TryGetValue(cacheKey, out var cached) && cached.Revision == revision)
            {
                return cached.Readiness;
            }
        }

        var readiness = Build(car.Key, layout, candidateFamilies);
        lock (_cacheSync)
        {
            if (_store.Revision == revision)
            {
                _cache[cacheKey] = new CachedReadiness(revision, readiness);
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
        IReadOnlyList<string> candidateFamilies)
    {
        var summaries = new List<FuelV2HistorySummary>();
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

            summaries.AddRange(read.Summaries);
            familiesWithEvidence.Add(family);
        }

        var routeObservations = summaries.SelectMany(summary => summary.PitRouteObservations).ToArray();
        var stationary = summaries.SelectMany(summary => summary.StationaryServiceObservations).ToArray();
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

        var qualifiedRefuels = summaries
            .SelectMany(summary => summary.StationaryServiceObservations
                .Where(IsQualifiedRefuelObservation)
                .Select(observation => new QualifiedRefuel(summary, observation)))
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
        var rows = new[]
        {
            new FuelV2ModelReadinessRow(
                "Pit route",
                "Local route checkpoints",
                [
                    Cell("To box", routeHasEntryLeg, routeHasEntryLeg ? "observed" : "need route"),
                    Cell("From box", routeHasExitLeg, routeHasExitLeg ? "observed" : "need exit"),
                    Cell("Full stop", completeRouteCount > 0, completeRouteCount > 0 ? $"{completeRouteCount} qualified" : "need 1"),
                    Cell("Pit box", assignedStallCount > 0, assignedStallCount > 0 ? "detected" : "not reported"),
                    OptionalCell("Pit lane pass", pitLanePassCount > 0, pitLanePassCount > 0 ? $"{pitLanePassCount} observed" : "optional")
                ]),
            new FuelV2ModelReadinessRow(
                "Refuel",
                "Stationary local service",
                [
                    Cell("Small fill", smallRefuelCount > 0, smallRefuelCount > 0 ? "measured" : "need low fill"),
                    Cell("Large fill", largeRefuelCount > 0, largeRefuelCount > 0 ? "measured" : "need high fill"),
                    Cell("Fuel flow", fuelFlowCount > 0, fuelFlowCount > 0 ? "timed" : "need timing"),
                    Cell("Fuel only", fuelOnlyCount > 0, fuelOnlyCount > 0 ? "confirmed" : "need service"),
                    Cell("Fuel + tires", fuelWithTiresCount > 0, fuelWithTiresCount > 0 ? "confirmed" : "need service")
                ]),
            new FuelV2ModelReadinessRow(
                "Tires",
                "Counter-proven outcomes",
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
            Detail: summaries.Count == 0
                ? "no exact Test/Practice history"
                : $"exact {string.Join(" + ", familiesWithEvidence.Distinct(StringComparer.OrdinalIgnoreCase))} history",
            Rows: rows);
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
        var capacity = refuel.Summary.FuelCapacity.EffectiveSessionCapacityLiters;
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

    private sealed record QualifiedRefuel(
        FuelV2HistorySummary Summary,
        PitServiceStationaryServiceObservation Observation);

    private sealed record CachedReadiness(long Revision, FuelV2ModelReadiness Readiness);
}

internal enum FuelV2ModelReadinessState
{
    Missing = 0,
    Confirmed = 1
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
    IReadOnlyList<FuelV2ModelReadinessRow> Rows)
{
    public static FuelV2ModelReadiness Hidden(string detail) => new(
        IsVisible: false,
        IsCollectionComplete: false,
        SourceFamilies: [],
        Detail: detail,
        Rows: []);
}
