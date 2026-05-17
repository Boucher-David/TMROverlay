using System.Globalization;
using TmrOverlay.App.Overlays.Abstractions;
using TmrOverlay.App.Overlays.Content;
using TmrOverlay.App.Overlays.SimpleTelemetry;
using TmrOverlay.Core.Overlays;
using TmrOverlay.Core.Settings;
using TmrOverlay.Core.Telemetry.Live;

namespace TmrOverlay.App.Overlays.PitService;

internal static class PitServiceOverlayViewModel
{
    private static readonly TimeSpan ChangeHighlightDuration = TimeSpan.FromSeconds(30);
    private const int MaxDisplayLapCount = 1000;
    private const string ErrorAccentHex = "#FF6274";
    private const string WarningAccentHex = "#FFD15B";
    private const string SuccessAccentHex = "#62FF9F";
    private const string InfoAccentHex = "#00E8FF";

    public static StatefulBuilder CreateStatefulBuilder()
    {
        return new StatefulBuilder();
    }

    public static Func<LiveTelemetrySnapshot, DateTimeOffset, string, SimpleTelemetryOverlayViewModel> CreateBuilder(
        OverlaySettings? settings = null)
    {
        var builder = CreateStatefulBuilder();
        return (snapshot, now, unitSystem) => builder.Build(snapshot, now, unitSystem, settings);
    }

    public static SimpleTelemetryOverlayViewModel From(
        LiveTelemetrySnapshot snapshot,
        DateTimeOffset now,
        string unitSystem,
        OverlaySettings? settings = null)
    {
        return From(snapshot, now, unitSystem, changeTracker: null, settings);
    }

    private static SimpleTelemetryOverlayViewModel From(
        LiveTelemetrySnapshot snapshot,
        DateTimeOffset now,
        string unitSystem,
        ChangeTracker? changeTracker,
        OverlaySettings? settings)
    {
        if (!SimpleTelemetryOverlayViewModel.IsFresh(snapshot, now, out var waitingStatus))
        {
            changeTracker?.Reset();
            return SimpleTelemetryOverlayViewModel.Waiting("Pit Service", waitingStatus);
        }

        snapshot = snapshot with { Models = snapshot.CompleteModels() };
        var localContext = LiveLocalStrategyContext.ForPitService(snapshot, now);
        if (!localContext.IsAvailable)
        {
            changeTracker?.Reset();
            return SimpleTelemetryOverlayViewModel.Waiting("Pit Service", localContext.StatusText);
        }

        var pit = snapshot.Models.PitService;
        if (!pit.HasData)
        {
            changeTracker?.Reset();
            return SimpleTelemetryOverlayViewModel.Waiting("Pit Service", "waiting for pit telemetry");
        }

        var release = BuildReleaseState(pit);
        var status = BuildStatus(pit, release);
        var tone = ToneFor(pit, release);
        var fuelRequest = FormatFuelRequest(pit, unitSystem);
        var tearoff = FormatTearoff(pit);
        var repair = FormatRepair(pit);
        var fastRepair = FormatFastRepair(pit);
        var raceContext = BuildRaceContextRow(snapshot);
        var fuelRequestChanged = IsChanged(changeTracker, "fuel-request", fuelRequest, now);
        var tearoffChanged = IsChanged(changeTracker, "tearoff", tearoff, now);
        var repairChanged = IsChanged(changeTracker, "repair", repair, now);
        var fastRepairChanged = IsChanged(changeTracker, "fast-repair", fastRepair, now);
        var releaseRow = PitSignalRow("Release", release.Value, release.Tone, OverlayContentColumnSettings.PitServiceReleaseBlockId);
        var pitStatusRow = PitSignalRow("Pit status", PitServiceStatusFormatter.Format(pit.Status), tone, OverlayContentColumnSettings.PitServicePitStatusBlockId);
        var fuelRequestRow = new SimpleTelemetryRowViewModel("Fuel request", fuelRequest, HighlightTone(SimpleTelemetryTone.Normal, fuelRequestChanged))
        {
            Segments = FuelRequestSegments(pit, unitSystem)
        };
        var tearoffRow = new SimpleTelemetryRowViewModel("Tearoff", tearoff, HighlightTone(SimpleTelemetryTone.Normal, tearoffChanged))
        {
            Segments = TearoffSegments(pit)
        };
        var repairRow = new SimpleTelemetryRowViewModel("Repair", repair, HighlightTone(RepairTone(pit), repairChanged))
        {
            Segments = RepairSegments(pit)
        };
        var fastRepairRow = new SimpleTelemetryRowViewModel("Fast repair", fastRepair, HighlightTone(SimpleTelemetryTone.Normal, fastRepairChanged))
        {
            Segments = FastRepairSegments(pit)
        };
        var metricSections = new List<SimpleTelemetryMetricSectionViewModel>
        {
            new("Pit Signal", new[] { releaseRow, pitStatusRow }),
            new("Service Request", new[] { fuelRequestRow, tearoffRow, repairRow, fastRepairRow })
        };
        if (raceContext is not null)
        {
            metricSections.Insert(0, new SimpleTelemetryMetricSectionViewModel(
                "Session",
                new[] { raceContext }));
        }

        var sessionKind = OverlayAvailabilityEvaluator.CurrentSessionKind(snapshot);
        var contentOptions = PitServiceContentOptions.From(settings, sessionKind);
        var tireAnalysisRows = BuildTireAnalysisRows(pit, snapshot.Models.TireCondition, unitSystem, contentOptions);
        IReadOnlyList<SimpleTelemetryGridSectionViewModel> sections = tireAnalysisRows.Count == 0
            ? Array.Empty<SimpleTelemetryGridSectionViewModel>()
            : new[]
            {
                new SimpleTelemetryGridSectionViewModel(
                    "Tire Analysis",
                    new[] { "Info", "FL", "FR", "RL", "RR" },
                    tireAnalysisRows)
            };

        var model = new SimpleTelemetryOverlayViewModel(
            Title: "Pit Service",
            Status: status,
            Source: BuildSource(),
            Tone: tone,
            Rows: metricSections.SelectMany(section => section.Rows).ToArray(),
            MetricSections: metricSections,
            Sections: sections);
        return SimpleTelemetryOverlayViewModel.ApplyContentSettings(
            model,
            settings,
            OverlayContentColumnSettings.PitService,
            sessionKind);
    }

    private static string BuildSource()
    {
        return "source: player/team pit service telemetry";
    }

    internal static string HeaderStatus(string? status)
    {
        return string.Equals(status?.Trim(), "hold", StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : status ?? string.Empty;
    }

    private static string BuildStatus(LivePitServiceModel pit, PitReleaseState release)
    {
        if (PitServiceStatusFormatter.IsError(pit.Status))
        {
            return "pit stall error";
        }

        if (release.Kind == PitReleaseKind.Go)
        {
            return "release ready";
        }

        if (release.Kind == PitReleaseKind.Advisory)
        {
            return "optional repair";
        }

        if (release.Kind == PitReleaseKind.Hold)
        {
            return "hold";
        }

        if (pit.PlayerCarInPitStall)
        {
            return "in pit stall";
        }

        if (pit.PitstopActive)
        {
            return "service active";
        }

        if (pit.OnPitRoad || pit.TeamOnPitRoad == true)
        {
            return "on pit road";
        }

        return HasRequestedService(pit) ? "service requested" : "pit ready";
    }

    private static string FormatFuelRequest(LivePitServiceModel pit, string unitSystem)
    {
        var requested = pit.Request.Fuel || pit.Request.FuelLiters is > 0d
            ? "requested"
            : null;
        var amount = pit.Request.FuelLiters is > 0d
            ? SimpleTelemetryOverlayViewModel.FormatFuelVolume(pit.Request.FuelLiters, unitSystem)
            : null;
        var value = SimpleTelemetryOverlayViewModel.JoinAvailable(requested, amount);
        if (value != "--")
        {
            return value;
        }

        return pit.Flags is null ? "--" : "none";
    }

    private static string FormatTearoff(LivePitServiceModel pit)
    {
        if (pit.Request.Tearoff)
        {
            return "requested";
        }

        return pit.Flags is null ? "--" : "none";
    }

    private static string FormatRepair(LivePitServiceModel pit)
    {
        string? required = pit.Repair.RequiredSeconds is { } requiredSeconds && requiredSeconds > 0d
            ? $"{requiredSeconds.ToString("0", CultureInfo.InvariantCulture)}s required"
            : null;
        string? optional = pit.Repair.OptionalSeconds is { } optionalSeconds && optionalSeconds > 0d
            ? $"{optionalSeconds.ToString("0", CultureInfo.InvariantCulture)}s optional"
            : null;
        return SimpleTelemetryOverlayViewModel.JoinAvailable(required, optional);
    }

    private static string FormatFastRepair(LivePitServiceModel pit)
    {
        var selected = pit.FastRepair.Selected
            ? "selected"
            : null;
        var available = pit.FastRepair.LocalAvailable is { } availableCount && availableCount >= 0
            ? $"available {availableCount.ToString(CultureInfo.InvariantCulture)}"
            : null;
        return SimpleTelemetryOverlayViewModel.JoinAvailable(selected, available);
    }

    private static SimpleTelemetryRowViewModel PitSignalRow(
        string label,
        string value,
        SimpleTelemetryTone tone,
        string segmentKey)
    {
        return new SimpleTelemetryRowViewModel(label, value, tone)
        {
            Segments = [Segment(label, string.IsNullOrWhiteSpace(value) ? "--" : value, tone, segmentKey)],
            RowColorHex = AccentFor(tone)
        };
    }

    private static IReadOnlyList<SimpleTelemetryMetricSegmentViewModel> FuelRequestSegments(
        LivePitServiceModel pit,
        string unitSystem)
    {
        var requested = pit.Request.Fuel || pit.Request.FuelLiters is > 0d;
        double? selectedLiters = pit.Request.FuelLiters is > 0d ? pit.Request.FuelLiters : null;
        return
        [
            Segment(
                "Requested",
                RequestStateValue(requested, pit.Flags is not null || selectedLiters is not null),
                RequestStateTone(requested, pit.Flags is not null || selectedLiters is not null),
                OverlayContentColumnSettings.PitServiceFuelRequestedBlockId),
            Segment(
                "Selected",
                selectedLiters is { } liters
                    ? SimpleTelemetryOverlayViewModel.FormatFuelVolume(liters, unitSystem)
                    : "--",
                selectedLiters is null ? SimpleTelemetryTone.Waiting : SimpleTelemetryTone.Info,
                OverlayContentColumnSettings.PitServiceFuelSelectedBlockId)
        ];
    }

    private static IReadOnlyList<SimpleTelemetryMetricSegmentViewModel> TearoffSegments(LivePitServiceModel pit)
    {
        var known = pit.Flags is not null;
        return
        [
            Segment(
                "Requested",
                RequestStateValue(pit.Request.Tearoff, known),
                RequestStateTone(pit.Request.Tearoff, known),
                OverlayContentColumnSettings.PitServiceTearoffRequestedBlockId)
        ];
    }

    private static IReadOnlyList<SimpleTelemetryMetricSegmentViewModel> RepairSegments(LivePitServiceModel pit)
    {
        var required = FormatRepairSeconds(pit.Repair.RequiredSeconds);
        var optional = FormatRepairSeconds(pit.Repair.OptionalSeconds);
        return
        [
            Segment("Required", required ?? "--", required is null ? SimpleTelemetryTone.Success : SimpleTelemetryTone.Error, OverlayContentColumnSettings.PitServiceRepairRequiredBlockId),
            Segment("Optional", optional ?? "--", optional is null ? SimpleTelemetryTone.Success : SimpleTelemetryTone.Warning, OverlayContentColumnSettings.PitServiceRepairOptionalBlockId)
        ];
    }

    private static IReadOnlyList<SimpleTelemetryMetricSegmentViewModel> FastRepairSegments(LivePitServiceModel pit)
    {
        var known = pit.Flags is not null;
        var available = pit.FastRepair.LocalAvailable is { } value && value >= 0
            ? value.ToString(CultureInfo.InvariantCulture)
            : null;
        return
        [
            Segment(
                "Selected",
                RequestStateValue(pit.FastRepair.Selected, known),
                RequestStateTone(pit.FastRepair.Selected, known),
                OverlayContentColumnSettings.PitServiceFastRepairSelectedBlockId),
            Segment(
                "Available",
                available ?? "--",
                available is null
                    ? SimpleTelemetryTone.Waiting
                    : pit.FastRepair.LocalAvailable is > 0
                        ? SimpleTelemetryTone.Success
                        : SimpleTelemetryTone.Error,
                OverlayContentColumnSettings.PitServiceFastRepairAvailableBlockId)
        ];
    }

    private static SimpleTelemetryMetricSegmentViewModel Segment(
        string label,
        string value,
        SimpleTelemetryTone tone,
        string? key = null)
    {
        return new SimpleTelemetryMetricSegmentViewModel(label, value, tone, Key: key);
    }

    private static string RequestStateValue(bool requested, bool known)
    {
        if (!known)
        {
            return "--";
        }

        return requested ? "Yes" : "No";
    }

    private static SimpleTelemetryTone RequestStateTone(bool requested, bool known)
    {
        if (!known)
        {
            return SimpleTelemetryTone.Waiting;
        }

        return requested ? SimpleTelemetryTone.Success : SimpleTelemetryTone.Info;
    }

    private static string? FormatRepairSeconds(double? seconds)
    {
        return seconds is { } value && value > 0d
            ? $"{value.ToString("0", CultureInfo.InvariantCulture)}s"
            : null;
    }

    private static SimpleTelemetryRowViewModel? BuildRaceContextRow(LiveTelemetrySnapshot snapshot)
    {
        var session = snapshot.Models.Session;
        var timeRemaining = OverlayHeaderTimeFormatter.FormatCompactTimeRemaining(
            session.SessionTimeRemainSeconds,
            session.SessionState,
            OverlayAvailabilityEvaluator.CurrentSessionKind(snapshot));
        var time = string.IsNullOrWhiteSpace(timeRemaining)
            ? null
            : timeRemaining;
        var laps = FormatRaceLaps(session, snapshot.Models.RaceProgress, snapshot.Models.RaceProjection);
        var value = SimpleTelemetryOverlayViewModel.JoinAvailable(
            time,
            laps == "--" ? null : laps);
        return value == "--"
            ? null
            : new SimpleTelemetryRowViewModel("Time / Laps", value)
            {
                Segments =
                [
                    Segment("Time", time ?? "--", SimpleTelemetryTone.Normal, OverlayContentColumnSettings.PitServiceSessionTimeBlockId),
                    Segment("Laps", laps, SimpleTelemetryTone.Normal, OverlayContentColumnSettings.PitServiceSessionLapsBlockId)
                ]
            };
    }

    private static string FormatRaceLaps(
        LiveSessionModel session,
        LiveRaceProgressModel raceProgress,
        LiveRaceProjectionModel raceProjection)
    {
        var remain = FormatRemainingLapCount(session.SessionLapsRemain)
            ?? FormatEstimatedLapCount(raceProjection.EstimatedTeamLapsRemaining)
            ?? FormatEstimatedLapCount(raceProgress.RaceLapsRemaining);
        var total = FormatTotalLapCount(session.SessionLapsTotal)
            ?? FormatEstimatedTotalLaps(raceProjection)
            ?? FormatEstimatedTotalLaps(raceProgress)
            ?? FormatTotalLapCount(session.RaceLaps);
        return remain is null && total is null ? "--" : $"{remain ?? "--"}/{total ?? "--"} laps";
    }

    private static string? FormatEstimatedLapCount(double? laps)
    {
        return laps is { } value && SimpleTelemetryOverlayViewModel.IsFinite(value) && value >= 0d && value <= MaxDisplayLapCount
            ? $"{value.ToString("0.#", CultureInfo.InvariantCulture)} est"
            : null;
    }

    private static string? FormatEstimatedTotalLaps(LiveRaceProgressModel raceProgress)
    {
        if (raceProgress.RaceLapsRemaining is not { } remaining
            || !SimpleTelemetryOverlayViewModel.IsFinite(remaining)
            || remaining < 0d
            || remaining > MaxDisplayLapCount)
        {
            return null;
        }

        var progress = raceProgress.OverallLeaderProgressLaps
            ?? raceProgress.ClassLeaderProgressLaps
            ?? raceProgress.StrategyCarProgressLaps;
        return progress is { } value && SimpleTelemetryOverlayViewModel.IsFinite(value) && value >= 0d
            ? $"{Math.Ceiling(value + remaining).ToString(CultureInfo.InvariantCulture)} est"
            : null;
    }

    private static string? FormatEstimatedTotalLaps(LiveRaceProjectionModel raceProjection)
    {
        return raceProjection.EstimatedFinishLap is { } value
            && SimpleTelemetryOverlayViewModel.IsFinite(value)
            && value >= 0d
            && value <= MaxDisplayLapCount
                ? $"{Math.Ceiling(value).ToString(CultureInfo.InvariantCulture)} est"
                : null;
    }

    private static string? FormatRemainingLapCount(int? laps)
    {
        return laps is { } value && value is >= 0 and <= MaxDisplayLapCount
            ? value.ToString(CultureInfo.InvariantCulture)
            : null;
    }

    private static string? FormatTotalLapCount(int? laps)
    {
        return laps is { } value && value is > 0 and <= MaxDisplayLapCount
            ? value.ToString(CultureInfo.InvariantCulture)
            : null;
    }

    private static IReadOnlyList<SimpleTelemetryGridRowViewModel> BuildTireAnalysisRows(
        LivePitServiceModel pit,
        LiveTireConditionModel tireCondition,
        string unitSystem,
        PitServiceContentOptions options)
    {
        var rows = new List<SimpleTelemetryGridRowViewModel>();
        AddIfPresent(rows, BuildCompoundRow(pit, options));
        AddIfPresent(rows, BuildChangeRow(pit, tireCondition, options));
        AddIfPresent(rows, BuildSetLimitRow(pit, options));
        AddIfPresent(rows, BuildSetsAvailableRow(pit, options));
        AddIfPresent(rows, BuildSetsUsedRow(pit, options));
        AddIfPresent(rows, BuildPressureRow(tireCondition, unitSystem, options));
        AddIfPresent(rows, BuildTemperatureRow(tireCondition, unitSystem, options));
        AddIfPresent(rows, BuildWearRow(tireCondition, options));
        AddIfPresent(rows, BuildDistanceRow(tireCondition, unitSystem, options));
        return rows;
    }

    private static void AddIfPresent(List<SimpleTelemetryGridRowViewModel> rows, SimpleTelemetryGridRowViewModel? row)
    {
        if (row is not null)
        {
            rows.Add(row);
        }
    }

    private static SimpleTelemetryGridRowViewModel? BuildCompoundRow(
        LivePitServiceModel pit,
        PitServiceContentOptions options)
    {
        if (!options.ShowTireCompound)
        {
            return null;
        }

        var current = FirstAvailable(pit.Tires.CurrentCompoundShortLabel, pit.Tires.CurrentCompoundLabel);
        var requested = FirstAvailable(pit.Tires.RequestedCompoundShortLabel, pit.Tires.RequestedCompoundLabel);
        var changing = requested is not null
            && (current is null || !string.Equals(current, requested, StringComparison.OrdinalIgnoreCase));
        var value = changing
            ? requested
            : FirstAvailable(current, requested);
        if (value is null)
        {
            return null;
        }

        var tone = changing ? SimpleTelemetryTone.Success : SimpleTelemetryTone.Info;
        return Row("Compound", Cell(value, tone), Cell(value, tone), Cell(value, tone), Cell(value, tone));
    }

    private static SimpleTelemetryGridRowViewModel? BuildChangeRow(
        LivePitServiceModel pit,
        LiveTireConditionModel tireCondition,
        PitServiceContentOptions options)
    {
        if (!options.ShowTireChange)
        {
            return null;
        }

        var flagsAvailable = pit.Flags is not null;
        var leftFront = tireCondition.LeftFront.ChangeRequested ?? (flagsAvailable ? pit.Tires.LeftFrontChangeRequested : null);
        var rightFront = tireCondition.RightFront.ChangeRequested ?? (flagsAvailable ? pit.Tires.RightFrontChangeRequested : null);
        var leftRear = tireCondition.LeftRear.ChangeRequested ?? (flagsAvailable ? pit.Tires.LeftRearChangeRequested : null);
        var rightRear = tireCondition.RightRear.ChangeRequested ?? (flagsAvailable ? pit.Tires.RightRearChangeRequested : null);
        if (!flagsAvailable && leftFront is null && rightFront is null && leftRear is null && rightRear is null)
        {
            return null;
        }

        return Row(
            "Change",
            ChangeCell(leftFront),
            ChangeCell(rightFront),
            ChangeCell(leftRear),
            ChangeCell(rightRear));
    }

    private static SimpleTelemetryGridRowViewModel? BuildSetLimitRow(
        LivePitServiceModel pit,
        PitServiceContentOptions options)
    {
        if (!options.ShowTireSetLimit)
        {
            return null;
        }

        var limit = RepresentativeLimit(pit);
        if (limit is null)
        {
            return null;
        }

        return Row("Set limit", limit, limit, limit, limit);
    }

    private static SimpleTelemetryGridRowViewModel? BuildSetsAvailableRow(
        LivePitServiceModel pit,
        PitServiceContentOptions options)
    {
        if (!options.ShowTireSetsAvailable || !HasLimitedTireInventory(pit))
        {
            return null;
        }

        var available = FormatLimitedAvailableInventory(pit);
        return available is not null
            ? Row(
                "Available",
                AvailableCell(available),
                AvailableCell(available),
                AvailableCell(available),
                AvailableCell(available))
            : null;
    }

    private static SimpleTelemetryGridRowViewModel? BuildSetsUsedRow(
        LivePitServiceModel pit,
        PitServiceContentOptions options)
    {
        if (!options.ShowTireSetsUsed)
        {
            return null;
        }

        var leftFront = FormatUsedForCorner(
            pit.Tires.LeftFrontTiresUsed,
            pit.Tires.LeftTireSetsUsed,
            "L",
            pit.Tires.FrontTireSetsUsed,
            "F",
            pit.Tires.TireSetsUsed);
        var rightFront = FormatUsedForCorner(
            pit.Tires.RightFrontTiresUsed,
            pit.Tires.RightTireSetsUsed,
            "R",
            pit.Tires.FrontTireSetsUsed,
            "F",
            pit.Tires.TireSetsUsed);
        var leftRear = FormatUsedForCorner(
            pit.Tires.LeftRearTiresUsed,
            pit.Tires.LeftTireSetsUsed,
            "L",
            pit.Tires.RearTireSetsUsed,
            "Rr",
            pit.Tires.TireSetsUsed);
        var rightRear = FormatUsedForCorner(
            pit.Tires.RightRearTiresUsed,
            pit.Tires.RightTireSetsUsed,
            "R",
            pit.Tires.RearTireSetsUsed,
            "Rr",
            pit.Tires.TireSetsUsed);
        return AnyCellHasValue(leftFront, rightFront, leftRear, rightRear)
            ? Row("Used", leftFront, rightFront, leftRear, rightRear)
            : null;
    }

    private static SimpleTelemetryGridRowViewModel? BuildPressureRow(
        LiveTireConditionModel tireCondition,
        string unitSystem,
        PitServiceContentOptions options)
    {
        if (!options.ShowTirePressure)
        {
            return null;
        }

        return BuildTireConditionRow(
            "Pressure",
            tireCondition,
            corner =>
            {
                var pressureKpa = FirstRepresentative(
                    corner.PitServicePressureKpa,
                    corner.ColdPressureKpa,
                    corner.BlackBoxColdPressurePa / 1000d);
                return pressureKpa is { } value
                    ? SimpleTelemetryOverlayViewModel.FormatPressure(value / 100d, unitSystem)
                    : "--";
            });
    }

    private static SimpleTelemetryGridRowViewModel? BuildTemperatureRow(
        LiveTireConditionModel tireCondition,
        string unitSystem,
        PitServiceContentOptions options)
    {
        return options.ShowTireTemperature
            ? BuildTireConditionRow("Temp", tireCondition, corner => FormatTemperatureAcross(corner.TemperatureC, unitSystem))
            : null;
    }

    private static SimpleTelemetryGridRowViewModel? BuildWearRow(
        LiveTireConditionModel tireCondition,
        PitServiceContentOptions options)
    {
        return options.ShowTireWear
            ? BuildTireConditionRow("Wear", tireCondition, corner => FormatWearAcross(corner.Wear))
            : null;
    }

    private static SimpleTelemetryGridRowViewModel? BuildDistanceRow(
        LiveTireConditionModel tireCondition,
        string unitSystem,
        PitServiceContentOptions options)
    {
        return options.ShowTireDistance
            ? BuildTireConditionRow("Distance", tireCondition, corner => FormatDistance(corner.OdometerMeters, unitSystem))
            : null;
    }

    private static SimpleTelemetryGridRowViewModel? BuildTireConditionRow(
        string label,
        LiveTireConditionModel tireCondition,
        Func<LiveTireCornerCondition, string> format)
    {
        var leftFront = format(tireCondition.LeftFront);
        var rightFront = format(tireCondition.RightFront);
        var leftRear = format(tireCondition.LeftRear);
        var rightRear = format(tireCondition.RightRear);
        return AnyCellHasValue(leftFront, rightFront, leftRear, rightRear)
            ? Row(label, leftFront, rightFront, leftRear, rightRear)
            : null;
    }

    private static SimpleTelemetryGridRowViewModel Row(
        string label,
        string leftFront,
        string rightFront,
        string leftRear,
        string rightRear,
        SimpleTelemetryTone tone = SimpleTelemetryTone.Normal)
    {
        return new SimpleTelemetryGridRowViewModel(
            label,
            [
                Cell(leftFront, tone),
                Cell(rightFront, tone),
                Cell(leftRear, tone),
                Cell(rightRear, tone)
            ],
            tone);
    }

    private static SimpleTelemetryGridRowViewModel Row(
        string label,
        SimpleTelemetryGridCellViewModel leftFront,
        SimpleTelemetryGridCellViewModel rightFront,
        SimpleTelemetryGridCellViewModel leftRear,
        SimpleTelemetryGridCellViewModel rightRear,
        SimpleTelemetryTone tone = SimpleTelemetryTone.Normal)
    {
        return new SimpleTelemetryGridRowViewModel(
            label,
            [leftFront, rightFront, leftRear, rightRear],
            tone);
    }

    private static SimpleTelemetryGridCellViewModel Cell(string value, SimpleTelemetryTone tone = SimpleTelemetryTone.Normal)
    {
        return new SimpleTelemetryGridCellViewModel(value, tone);
    }

    private static SimpleTelemetryGridCellViewModel ChangeCell(bool? requested)
    {
        if (requested is null)
        {
            return Cell("--", SimpleTelemetryTone.Waiting);
        }

        return requested.Value
            ? Cell("Change", SimpleTelemetryTone.Success)
            : Cell("Keep", SimpleTelemetryTone.Info);
    }

    private static SimpleTelemetryGridCellViewModel AvailableCell(string value)
    {
        return Cell(value, IsZeroAvailableLabel(value)
            ? SimpleTelemetryTone.Error
            : SimpleTelemetryTone.Normal);
    }

    private static bool IsZeroAvailableLabel(string value)
    {
        return value
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(part => string.Equals(part, "0", StringComparison.Ordinal)
                || part.EndsWith(" 0", StringComparison.Ordinal));
    }

    private static string? RepresentativeLimit(LivePitServiceModel pit)
    {
        if (pit.Tires.DryTireSetLimit is > 0)
        {
            return $"{pit.Tires.DryTireSetLimit.Value.ToString(CultureInfo.InvariantCulture)} sets";
        }

        return HasUnlimitedAvailability(pit)
            ? "unlim"
            : null;
    }

    private static bool HasUnlimitedAvailability(LivePitServiceModel pit)
    {
        return pit.Tires.TireSetsAvailable == 255
            || pit.Tires.LeftTireSetsAvailable == 255
            || pit.Tires.RightTireSetsAvailable == 255
            || pit.Tires.FrontTireSetsAvailable == 255
            || pit.Tires.RearTireSetsAvailable == 255
            || pit.Tires.LeftFrontTiresAvailable == 255
            || pit.Tires.RightFrontTiresAvailable == 255
            || pit.Tires.LeftRearTiresAvailable == 255
            || pit.Tires.RightRearTiresAvailable == 255;
    }

    private static bool HasLimitedTireInventory(LivePitServiceModel pit)
    {
        return pit.Tires.DryTireSetLimit is > 0;
    }

    private static string? FormatLimitedAvailableInventory(LivePitServiceModel pit)
    {
        if (IsRepresentativeLimitedAvailable(pit.Tires.TireSetsAvailable, pit))
        {
            return FormatTireCounter(pit.Tires.TireSetsAvailable);
        }

        var matchingCornerValue = MatchingLimitedAvailable(
            pit,
            pit.Tires.LeftFrontTiresAvailable,
            pit.Tires.RightFrontTiresAvailable,
            pit.Tires.LeftRearTiresAvailable,
            pit.Tires.RightRearTiresAvailable);
        if (matchingCornerValue is not null)
        {
            return FormatTireCounter(matchingCornerValue);
        }

        var matchingSetValue = MatchingLimitedAvailable(
            pit,
            pit.Tires.LeftTireSetsAvailable,
            pit.Tires.RightTireSetsAvailable,
            pit.Tires.FrontTireSetsAvailable,
            pit.Tires.RearTireSetsAvailable);
        return matchingSetValue is not null
            ? FormatTireCounter(matchingSetValue)
            : null;
    }

    private static int? MatchingLimitedAvailable(LivePitServiceModel pit, params int?[] values)
    {
        if (values.Any(value => !IsRepresentativeLimitedAvailable(value, pit)))
        {
            return null;
        }

        var first = values[0]!.Value;
        return values.All(value => value == first) ? first : null;
    }

    private static bool IsRepresentativeLimitedAvailable(int? value, LivePitServiceModel pit)
    {
        if (value is null || value == 255)
        {
            return false;
        }

        if (value > 0)
        {
            return true;
        }

        return value == 0 && HasUsedEntireDryTireLimit(pit);
    }

    private static bool HasUsedEntireDryTireLimit(LivePitServiceModel pit)
    {
        if (pit.Tires.DryTireSetLimit is not { } limit || limit <= 0)
        {
            return false;
        }

        return pit.Tires.TireSetsUsed >= limit
            || pit.Tires.LeftFrontTiresUsed >= limit
            || pit.Tires.RightFrontTiresUsed >= limit
            || pit.Tires.LeftRearTiresUsed >= limit
            || pit.Tires.RightRearTiresUsed >= limit;
    }

    private static string FormatUsedForCorner(
        int? perCorner,
        int? side,
        string sideLabel,
        int? axle,
        string axleLabel,
        int? global)
    {
        if (IsRepresentativeUsed(perCorner))
        {
            return FormatTireCounter(perCorner);
        }

        var parts = new List<string>();
        if (IsRepresentativeUsed(side))
        {
            parts.Add($"{sideLabel} {FormatTireCounter(side)}");
        }

        if (IsRepresentativeUsed(axle) && axle != side)
        {
            parts.Add($"{axleLabel} {FormatTireCounter(axle)}");
        }

        if (parts.Count > 0)
        {
            return string.Join("/", parts);
        }

        return IsRepresentativeUsed(global) ? FormatTireCounter(global) : "--";
    }

    private static string FormatTireCounter(int? value)
    {
        return value == 255
            ? "unlim"
            : value?.ToString(CultureInfo.InvariantCulture) ?? "--";
    }

    private static string FormatTemperatureAcross(LiveTireAcrossTreadValues values, string unitSystem)
    {
        var raw = new[] { values.Left, values.Middle, values.Right }
            .Where(IsRepresentativeTemperature)
            .Select(value => SimpleTelemetryOverlayViewModel.IsImperial(unitSystem)
                ? value!.Value * 9d / 5d + 32d
                : value!.Value)
            .ToArray();
        return FormatAcrossValues(
            raw,
            "0",
            SimpleTelemetryOverlayViewModel.IsImperial(unitSystem) ? " F" : " C");
    }

    private static string FormatWearAcross(LiveTireAcrossTreadValues values)
    {
        var raw = new[] { values.Left, values.Middle, values.Right }
            .Where(IsRepresentativeWear)
            .Select(value => value!.Value * 100d)
            .ToArray();
        return FormatAcrossValues(raw, "0", "%");
    }

    private static string FormatAcrossValues(IReadOnlyList<double> values, string format, string suffix)
    {
        if (values.Count == 0)
        {
            return "--";
        }

        var formatted = values
            .Select(value => value.ToString(format, CultureInfo.InvariantCulture))
            .ToArray();
        var distinct = formatted.Distinct(StringComparer.Ordinal).ToArray();
        return distinct.Length == 1
            ? $"{distinct[0]}{suffix}"
            : $"{string.Join("/", formatted)}{suffix}";
    }

    private static string FormatDistance(double? meters, string unitSystem)
    {
        if (meters is not { } value || !SimpleTelemetryOverlayViewModel.IsFinite(value) || value <= 0d)
        {
            return "--";
        }

        if (SimpleTelemetryOverlayViewModel.IsImperial(unitSystem))
        {
            return $"{(value / 1609.344d).ToString("0.0", CultureInfo.InvariantCulture)} mi";
        }

        return $"{(value / 1000d).ToString("0.0", CultureInfo.InvariantCulture)} km";
    }

    private static double? FirstRepresentative(params double?[] values)
    {
        foreach (var value in values)
        {
            if (value is { } number && number > 0d && SimpleTelemetryOverlayViewModel.IsFinite(number))
            {
                return number;
            }
        }

        return null;
    }

    private static string? FirstAvailable(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
    }

    private static bool AnyCellHasValue(params string[] values)
    {
        return values.Any(value => !string.IsNullOrWhiteSpace(value) && value != "--");
    }

    private static bool IsRepresentativeUsed(int? value)
    {
        return value is > 0;
    }

    private static bool IsRepresentativeWear(double? value)
    {
        return value is > 0d and <= 1d;
    }

    private static bool IsRepresentativeTemperature(double? value)
    {
        return value is > 0d and < 250d;
    }

    private static SimpleTelemetryTone ToneFor(LivePitServiceModel pit, PitReleaseState release)
    {
        if (release.Tone is SimpleTelemetryTone.Info or SimpleTelemetryTone.Success or SimpleTelemetryTone.Warning or SimpleTelemetryTone.Error)
        {
            return release.Tone;
        }

        if (HasRequiredRepair(pit) || HasOptionalRepair(pit))
        {
            return SimpleTelemetryTone.Warning;
        }

        if (pit.PlayerCarInPitStall || pit.PitstopActive || pit.OnPitRoad || pit.TeamOnPitRoad == true)
        {
            return SimpleTelemetryTone.Info;
        }

        return HasRequestedService(pit) ? SimpleTelemetryTone.Success : SimpleTelemetryTone.Normal;
    }

    private static SimpleTelemetryTone RepairTone(LivePitServiceModel pit)
    {
        if (HasRequiredRepair(pit))
        {
            return SimpleTelemetryTone.Error;
        }

        return HasOptionalRepair(pit) ? SimpleTelemetryTone.Warning : SimpleTelemetryTone.Normal;
    }

    private static string? AccentFor(SimpleTelemetryTone tone)
    {
        return tone switch
        {
            SimpleTelemetryTone.Error => ErrorAccentHex,
            SimpleTelemetryTone.Warning => WarningAccentHex,
            SimpleTelemetryTone.Success => SuccessAccentHex,
            SimpleTelemetryTone.Info => InfoAccentHex,
            _ => null
        };
    }

    private static bool HasRequestedService(LivePitServiceModel pit)
    {
        return pit.Request.HasAnyRequest || (pit.Flags is { } flags && flags != 0);
    }

    private static PitReleaseState BuildReleaseState(LivePitServiceModel pit)
    {
        if (PitServiceStatusFormatter.IsError(pit.Status))
        {
            return new PitReleaseState(
                PitReleaseKind.Hold,
                $"RED - {PitServiceStatusFormatter.Format(pit.Status)}",
                SimpleTelemetryTone.Error);
        }

        if (PitServiceStatusFormatter.IsComplete(pit.Status))
        {
            return new PitReleaseState(
                PitReleaseKind.Go,
                "GREEN - go",
                SimpleTelemetryTone.Success);
        }

        if (IsServiceActive(pit))
        {
            return new PitReleaseState(
                PitReleaseKind.Hold,
                "RED - service active",
                SimpleTelemetryTone.Error);
        }

        if (HasRequiredRepair(pit))
        {
            return new PitReleaseState(
                PitReleaseKind.Hold,
                "RED - repair active",
                SimpleTelemetryTone.Error);
        }

        if (HasOptionalRepair(pit))
        {
            return new PitReleaseState(
                PitReleaseKind.Advisory,
                "YELLOW - optional repair",
                SimpleTelemetryTone.Warning);
        }

        if (pit.PlayerCarInPitStall)
        {
            return new PitReleaseState(
                PitReleaseKind.Go,
                pit.Status is null ? "GREEN - go (inferred)" : "GREEN - go",
                SimpleTelemetryTone.Success);
        }

        if (pit.OnPitRoad || pit.TeamOnPitRoad == true)
        {
            return new PitReleaseState(
                PitReleaseKind.Pending,
                "pit road",
                SimpleTelemetryTone.Info);
        }

        return HasRequestedService(pit)
            ? new PitReleaseState(
                PitReleaseKind.Pending,
                "armed",
                SimpleTelemetryTone.Info)
            : new PitReleaseState(
                PitReleaseKind.Pending,
                "--",
                SimpleTelemetryTone.Normal);
    }

    private static bool IsServiceActive(LivePitServiceModel pit)
    {
        return PitServiceStatusFormatter.IsInProgress(pit.Status) || pit.PitstopActive;
    }

    private static bool HasRequiredRepair(LivePitServiceModel pit)
    {
        return pit.Repair.RequiredSeconds is > 0d;
    }

    private static bool HasOptionalRepair(LivePitServiceModel pit)
    {
        return pit.Repair.OptionalSeconds is > 0d;
    }

    private static bool IsChanged(ChangeTracker? tracker, string key, string value, DateTimeOffset now)
    {
        return tracker?.IsHighlighted(key, value, now) == true;
    }

    private static SimpleTelemetryTone HighlightTone(SimpleTelemetryTone baseTone, bool changed)
    {
        return StrongestTone(baseTone, changed ? SimpleTelemetryTone.Info : SimpleTelemetryTone.Normal);
    }

    private static SimpleTelemetryTone StrongestTone(SimpleTelemetryTone left, SimpleTelemetryTone right)
    {
        return Weight(left) >= Weight(right) ? left : right;
    }

    private static int Weight(SimpleTelemetryTone tone)
    {
        return tone switch
        {
            SimpleTelemetryTone.Error => 50,
            SimpleTelemetryTone.Warning => 40,
            SimpleTelemetryTone.Info => 30,
            SimpleTelemetryTone.Modeled => 30,
            SimpleTelemetryTone.Success => 20,
            SimpleTelemetryTone.Waiting => 10,
            _ => 0
        };
    }

    private enum PitReleaseKind
    {
        Pending,
        Advisory,
        Hold,
        Go
    }

    private sealed record PitReleaseState(
        PitReleaseKind Kind,
        string Value,
        SimpleTelemetryTone Tone);

    internal sealed class StatefulBuilder
    {
        private readonly ChangeTracker _changeTracker = new(ChangeHighlightDuration);

        public SimpleTelemetryOverlayViewModel Build(
            LiveTelemetrySnapshot snapshot,
            DateTimeOffset now,
            string unitSystem,
            OverlaySettings? settings = null)
        {
            return From(snapshot, now, unitSystem, _changeTracker, settings);
        }
    }

    private sealed record PitServiceContentOptions(
        bool ShowTireCompound,
        bool ShowTireChange,
        bool ShowTireSetLimit,
        bool ShowTireSetsAvailable,
        bool ShowTireSetsUsed,
        bool ShowTirePressure,
        bool ShowTireTemperature,
        bool ShowTireWear,
        bool ShowTireDistance)
    {
        public static PitServiceContentOptions From(
            OverlaySettings? settings,
            OverlaySessionKind? sessionKind)
        {
            return new PitServiceContentOptions(
                ShowTireCompound: ContentEnabled(settings, OverlayOptionKeys.PitServiceShowTireCompound, sessionKind),
                ShowTireChange: ContentEnabled(settings, OverlayOptionKeys.PitServiceShowTireChange, sessionKind),
                ShowTireSetLimit: ContentEnabled(settings, OverlayOptionKeys.PitServiceShowTireSetLimit, sessionKind),
                ShowTireSetsAvailable: ContentEnabled(settings, OverlayOptionKeys.PitServiceShowTireSetsAvailable, sessionKind),
                ShowTireSetsUsed: ContentEnabled(settings, OverlayOptionKeys.PitServiceShowTireSetsUsed, sessionKind),
                ShowTirePressure: ContentEnabled(settings, OverlayOptionKeys.PitServiceShowTirePressure, sessionKind),
                ShowTireTemperature: ContentEnabled(settings, OverlayOptionKeys.PitServiceShowTireTemperature, sessionKind),
                ShowTireWear: ContentEnabled(settings, OverlayOptionKeys.PitServiceShowTireWear, sessionKind),
                ShowTireDistance: ContentEnabled(settings, OverlayOptionKeys.PitServiceShowTireDistance, sessionKind));
        }

        private static bool ContentEnabled(
            OverlaySettings? settings,
            string optionKey,
            OverlaySessionKind? sessionKind)
        {
            return settings is null
                || OverlayContentColumnSettings.ContentEnabledForSession(
                    settings,
                    optionKey,
                    defaultEnabled: true,
                    sessionKind);
        }
    }

    private sealed class ChangeTracker
    {
        private readonly TimeSpan _duration;
        private readonly Dictionary<string, string> _lastValues = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DateTimeOffset> _highlightUntil = new(StringComparer.OrdinalIgnoreCase);

        public ChangeTracker(TimeSpan duration)
        {
            _duration = duration;
        }

        public bool IsHighlighted(string key, string value, DateTimeOffset now)
        {
            var normalized = value.Trim();
            if (_lastValues.TryGetValue(key, out var previous)
                && !string.Equals(previous, normalized, StringComparison.Ordinal))
            {
                _highlightUntil[key] = now.Add(_duration);
            }

            _lastValues[key] = normalized;
            return _highlightUntil.TryGetValue(key, out var until) && until >= now;
        }

        public void Reset()
        {
            _lastValues.Clear();
            _highlightUntil.Clear();
        }
    }
}
