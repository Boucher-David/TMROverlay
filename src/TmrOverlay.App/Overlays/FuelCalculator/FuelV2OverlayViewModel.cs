using System.Globalization;
using TmrOverlay.App.History;
using TmrOverlay.App.Overlays.SimpleTelemetry;
using TmrOverlay.Core.Fuel.V2;
using TmrOverlay.Core.Overlays;
using TmrOverlay.Core.Settings;
using TmrOverlay.Core.Telemetry.Live;

namespace TmrOverlay.App.Overlays.FuelCalculator;

// Presentation only. The Core composer remains the single owner of factual
// capacity/checkpoint, burn-window, range, and lap-budget calculations. This
// first production-shaped V2 slice deliberately presents no inferred strategy
// selection, target, refuel request, plan, or lower-half stint schedule.
internal sealed record FuelV2OverlayViewModel(
    SimpleTelemetryOverlayViewModel Overlay,
    FuelV2TireHistoryCellViewModel TireHistory)
{
    public static FuelV2OverlayViewModel From(
        LiveTelemetrySnapshot snapshot,
        string unitSystem,
        DateTimeOffset now,
        OverlaySettings? contentSettings = null,
        FuelV2TireServiceHistorySelection? tireHistory = null,
        FuelV2HistoryNormalBurnSelection? normalHistory = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var localContext = LiveLocalStrategyContext.ForFuelV2FactualDisplay(snapshot, now);
        if (!localContext.IsAvailable)
        {
            return Waiting(localContext.StatusText);
        }

        if (!snapshot.HasFrameForCurrentContext
            || !snapshot.HasSessionInfoForCurrentCollection)
        {
            return Waiting("waiting for current fuel telemetry");
        }

        // Exact classified history is displayed as its own explicitly named
        // factual bucket. It is not selected here, cannot create a target or
        // pit request, and does not promote V1 or strategy advice.
        var composed = FuelV2LiveSnapshotComposer.From(
            snapshot,
            new FuelV2LiveSnapshotOptions(
                HistoricalNormalSeed: normalHistory?.IsAvailable == true
                    ? normalHistory.Burn
                    : null));
        var currentFuel = composed.FuelCheckpoints.Current?.Liters;
        if (currentFuel is null)
        {
            return Waiting("waiting for fuel level");
        }

        var content = FuelContentPolicy.From(
            contentSettings,
            OverlayAvailabilityEvaluator.CurrentSessionKind(snapshot));
        var isRace = OverlayAvailabilityEvaluator.NormalizeSessionKind(
            OverlayAvailabilityEvaluator.CurrentSessionKind(snapshot)) == OverlaySessionKind.Race;
        var isFactualFallback = string.Equals(
            localContext.Reason,
            "session_driver_camera_identity_fallback",
            StringComparison.Ordinal);
        var sections = new List<SimpleTelemetryMetricSectionViewModel>();

        if (!isFactualFallback && isRace && content.ShowRacePlan && HasLapContext(composed.LapBudget))
        {
            sections.Add(new SimpleTelemetryMetricSectionViewModel(
                "Race Information",
                [new SimpleTelemetryRowViewModel(
                    "Laps",
                    FormatLapContext(composed.LapBudget),
                    LapTone(composed.LapBudget))
                {
                    Segments =
                    [
                        Segment("Remain", FormatWholeLaps(composed.LapBudget.PrimaryLapsRemaining), LapTone(composed.LapBudget)),
                        Segment("Finish", FormatDecimalLaps(composed.LapBudget.EstimatedFinishLap), LapTone(composed.LapBudget)),
                        Segment("State", LapState(composed.LapBudget), LapTone(composed.LapBudget))
                    ]
                }]));
        }

        // Fuel state is factual live telemetry, not race strategy. Race uses
        // the existing Fuel content block; Test/Practice/Qualifying use the
        // existing Fuel Range block. That makes the V2 surface available in
        // every session without silently changing a user's content choices.
        var showFuelState = content.ShowFuelState(
            OverlayAvailabilityEvaluator.CurrentSessionKind(snapshot));
        if (showFuelState)
        {
            var capacity = composed.Capacity.EffectiveCapacityLiters;
            sections.Add(new SimpleTelemetryMetricSectionViewModel(
                "Fuel State",
                [new SimpleTelemetryRowViewModel(
                    "Fuel",
                    FormatFuelState(currentFuel, capacity, unitSystem),
                    SimpleTelemetryTone.Info)
                {
                    Segments =
                    [
                        Segment("Current", FormatFuel(currentFuel, unitSystem), SimpleTelemetryTone.Info),
                        Segment("Max", FormatFuel(capacity, unitSystem), CapacityTone(composed)),
                        Segment("Cap", CapacityLabel(composed), CapacityTone(composed))
                    ]
                }]));
        }

        if (!isFactualFallback && content.ShowFuelUsage && composed.BurnWindows.AvailableBuckets.Count > 0)
        {
            sections.Add(new SimpleTelemetryMetricSectionViewModel(
                "Fuel Usage",
                [new SimpleTelemetryRowViewModel(
                    "Fuel/Lap",
                    FormatBurnSummary(composed.BurnWindows, unitSystem),
                    SimpleTelemetryTone.Info)
                {
                    Segments = BurnSegments(composed.BurnWindows, unitSystem)
                }]));
        }

        if (!isFactualFallback && content.ShowFuelRange && HasRange(composed))
        {
            sections.Add(new SimpleTelemetryMetricSectionViewModel(
                "Fuel Range",
                [new SimpleTelemetryRowViewModel(
                    "Range",
                    FormatRangeSummary(composed),
                    SimpleTelemetryTone.Info)
                {
                    Segments = RangeSegments(composed)
                }]));
        }

        var rows = sections.SelectMany(section => section.Rows).ToArray();
        if (rows.Length == 0)
        {
            return Waiting("waiting for clean fuel evidence");
        }

        return new FuelV2OverlayViewModel(
            Overlay: new SimpleTelemetryOverlayViewModel(
                Title: "Fuel Calculator",
                Status: Status(composed),
                Source: "source: Fuel V2 factual live composition",
                Tone: SimpleTelemetryTone.Info,
                Rows: rows,
                MetricSections: sections,
                Sections: []),
            TireHistory: FuelV2TireHistoryCellViewModel.From(tireHistory));
    }

    private static FuelV2OverlayViewModel Waiting(string status)
    {
        return new FuelV2OverlayViewModel(
            Overlay: SimpleTelemetryOverlayViewModel.Waiting("Fuel Calculator", status) with
            {
                Source = "source: Fuel V2 waiting for current live context"
            },
            TireHistory: FuelV2TireHistoryCellViewModel.Hidden);
    }

    private static bool HasLapContext(FuelV2LapBudgetProjection budget)
    {
        return budget.PrimaryLapsRemaining is not null
            || budget.PossibleLapsRemaining is not null
            || budget.EstimatedFinishLap is not null;
    }

    private static bool HasRange(FuelV2ComposedSnapshot composed)
    {
        return FuelV2BurnBucketCatalog.Ordered
            .Select(composed.Range.Bucket)
            .Any(range => range?.HasValue == true);
    }

    private static string Status(FuelV2ComposedSnapshot composed)
    {
        return HasLiveRangeEvidence(composed.BurnWindows)
            ? "fuel range"
            : composed.BurnWindows.HistoricalNormal?.HasValue == true
                ? "fuel history"
            : "fuel level";
    }

    private static string FormatLapContext(FuelV2LapBudgetProjection budget)
    {
        return budget.PrimaryLapsRemaining is { } remaining
            ? $"{remaining} laps remaining"
            : budget.EstimatedFinishLap is { } finish
                ? $"finish {finish.ToString("0.00", CultureInfo.InvariantCulture)}"
                : "--";
    }

    private static SimpleTelemetryTone LapTone(FuelV2LapBudgetProjection budget)
    {
        return budget.CanDriveFuelAdvice
            ? SimpleTelemetryTone.Info
            : SimpleTelemetryTone.Waiting;
    }

    private static string LapState(FuelV2LapBudgetProjection budget)
    {
        return budget.CanDriveFuelAdvice
            ? "live"
            : "learning";
    }

    private static string FormatFuelState(double? currentFuel, double? capacity, string unitSystem)
    {
        return capacity is { } maximum
            ? $"{FormatFuel(currentFuel, unitSystem)} / {FormatFuel(maximum, unitSystem)}"
            : FormatFuel(currentFuel, unitSystem);
    }

    private static string CapacityLabel(FuelV2ComposedSnapshot composed)
    {
        return composed.Capacity.CanDriveFuelAdvice
            ? "verified"
            : "learning";
    }

    private static SimpleTelemetryTone CapacityTone(FuelV2ComposedSnapshot composed)
    {
        return composed.Capacity.CanDriveFuelAdvice
            ? SimpleTelemetryTone.Info
            : SimpleTelemetryTone.Waiting;
    }

    private static string FormatBurnSummary(FuelV2FuelPerLapWindows windows, string unitSystem)
    {
        var primary = FirstAvailableBurn(windows);
        return primary?.HasValue == true
            ? FormatFuelPerLap(primary.Value, unitSystem)
            : "--";
    }

    private static IReadOnlyList<SimpleTelemetryMetricSegmentViewModel> BurnSegments(
        FuelV2FuelPerLapWindows windows,
        string unitSystem)
    {
        return FuelV2BurnBucketCatalog.Ordered
            .Select(bucketId => BurnSegment(
                FuelV2BurnBucketCatalog.Label(bucketId),
                windows.Bucket(bucketId),
                unitSystem))
            .ToArray();
    }

    private static SimpleTelemetryMetricSegmentViewModel BurnSegment(
        string label,
        FuelV2Scalar? scalar,
        string unitSystem)
    {
        return Segment(
            label,
            scalar?.HasValue == true ? FormatFuelPerLap(scalar.Value, unitSystem) : "--",
            ScalarTone(scalar));
    }

    private static string FormatRangeSummary(FuelV2ComposedSnapshot composed)
    {
        return FirstAvailableRange(composed.Range)?.Value is { } range
            ? $"{range.ToString("0.00", CultureInfo.InvariantCulture)} laps"
            : "--";
    }

    private static IReadOnlyList<SimpleTelemetryMetricSegmentViewModel> RangeSegments(FuelV2ComposedSnapshot composed)
    {
        return FuelV2BurnBucketCatalog.Ordered
            .Select(bucketId => RangeSegment(
                FuelV2BurnBucketCatalog.Label(bucketId),
                composed.Range.Bucket(bucketId)))
            .ToArray();
    }

    private static SimpleTelemetryMetricSegmentViewModel RangeSegment(string label, FuelV2Scalar? scalar)
    {
        return Segment(
            label,
            scalar?.Value is { } range
                ? $"{range.ToString("0.00", CultureInfo.InvariantCulture)} laps"
                : "--",
            ScalarTone(scalar));
    }

    private static bool HasLiveRangeEvidence(FuelV2FuelPerLapWindows windows)
    {
        return windows.Last?.HasValue == true
            || windows.FiveLapAverage?.HasValue == true
            || windows.TenLapAverage?.HasValue == true;
    }

    private static FuelV2Scalar? FirstAvailableBurn(FuelV2FuelPerLapWindows windows)
    {
        return FuelV2BurnBucketCatalog.Ordered
            .Select(windows.Bucket)
            .FirstOrDefault(bucket => bucket?.HasValue == true);
    }

    private static FuelV2Scalar? FirstAvailableRange(FuelV2RangeSnapshot range)
    {
        return FuelV2BurnBucketCatalog.Ordered
            .Select(range.Bucket)
            .FirstOrDefault(bucket => bucket?.HasValue == true);
    }

    private static SimpleTelemetryTone ScalarTone(FuelV2Scalar? scalar)
    {
        if (scalar?.HasValue != true)
        {
            return SimpleTelemetryTone.Waiting;
        }

        return scalar.Confidence == FuelV2Confidence.Seeded
            ? SimpleTelemetryTone.Modeled
            : SimpleTelemetryTone.Info;
    }

    private static SimpleTelemetryMetricSegmentViewModel Segment(
        string label,
        string value,
        SimpleTelemetryTone tone)
    {
        return new SimpleTelemetryMetricSegmentViewModel(label, value, tone);
    }

    private static string FormatWholeLaps(int? laps)
    {
        return laps is { } value ? $"{value} laps" : "--";
    }

    private static string FormatDecimalLaps(double? laps)
    {
        return laps is { } value
            ? value.ToString("0.00", CultureInfo.InvariantCulture)
            : "--";
    }

    private static string FormatFuel(double? liters, string unitSystem)
    {
        return SimpleTelemetryOverlayViewModel.FormatFuelVolume(liters, unitSystem);
    }

    private static string FormatFuelPerLap(double? liters, string unitSystem)
    {
        var volume = FormatFuel(liters, unitSystem);
        return volume == "--"
            ? volume
            : string.Equals(unitSystem, "Imperial", StringComparison.OrdinalIgnoreCase)
                ? volume.Replace(" gal", " gal/lap", StringComparison.Ordinal)
                : volume.Replace(" L", " L/lap", StringComparison.Ordinal);
    }
}
