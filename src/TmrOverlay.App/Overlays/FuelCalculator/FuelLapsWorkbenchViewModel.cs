using System.Globalization;
using TmrOverlay.App.Overlays.SimpleTelemetry;
using TmrOverlay.Core.Fuel;
using TmrOverlay.Core.History;
using TmrOverlay.Core.Telemetry.Live;

namespace TmrOverlay.App.Overlays.FuelCalculator;

internal static class FuelLapsWorkbenchViewModel
{
    private const int UnlimitedLapsSentinel = 32000;
    private const int MaxPlausibleLiveLapCount = 1000;

    public static bool Enabled => true;

    public static FuelCalculatorViewModel Create()
    {
        var materializedRows = Rows
            .Select(Materialize)
            .ToArray();

        var metricRows = materializedRows
            .Select(row => new SimpleTelemetryRowViewModel(row.Label, row.Summary, RowTone(row))
            {
                Segments =
                [
                    Segment("Start", row.StartOfRace),
                    Segment("Mid S1", row.MiddleOfStint1),
                    Segment("Stop 1", row.AfterFirstStop),
                    Segment("Half", row.Halfway),
                    Segment("Actual", row.Actual)
                ]
            })
            .ToArray();

        var legacyRows = materializedRows
            .Select(row => new FuelDisplayRow(
                row.Label,
                LegacyValue(row),
                row.Actual.Value))
            .ToArray();

        return new FuelCalculatorViewModel(
            Status: "laps workbench",
            Overview: "current lap counter baseline",
            Source: "source: Fuel V2 laps workbench; current V1 lap logic from raw capture checkpoints",
            Rows: legacyRows,
            MetricSections:
            [
                new SimpleTelemetryMetricSectionViewModel("Laps Workbench", metricRows)
            ]);
    }

    private static MaterializedWorkbenchRow Materialize(WorkbenchRow row)
    {
        return new MaterializedWorkbenchRow(
            row.Label,
            row.Summary,
            Cell(row.StartOfRace, row.ActualLaps),
            Cell(row.MiddleOfStint1, row.ActualLaps),
            Cell(row.AfterFirstStop, row.ActualLaps),
            Cell(row.Halfway, row.ActualLaps),
            new WorkbenchCell(row.ActualLabel, SimpleTelemetryTone.Modeled));
    }

    private static WorkbenchCell Cell(CheckpointProbe? probe, double? actualLaps)
    {
        if (probe is null)
        {
            return new WorkbenchCell("--", SimpleTelemetryTone.Waiting);
        }

        var context = BuildContext(probe);
        var session = BuildSession(context, probe);
        var estimate = LiveRaceProgressProjector.EstimateLapsRemaining(
            context,
            session,
            probe.StrategyProgressLaps,
            probe.OverallLeaderProgressLaps,
            probe.ClassLeaderProgressLaps,
            probe.RacePaceSeconds,
            probe.RacePaceSource);
        var finishLaps = EstimateFinishLaps(probe, estimate);
        if (finishLaps is null)
        {
            return new WorkbenchCell(SourceToken(estimate.Source, probe), SimpleTelemetryTone.Waiting);
        }

        return new WorkbenchCell(
            $"{FormatLaps(finishLaps.Value)} {SourceToken(estimate.Source, probe)}",
            CellTone(finishLaps.Value, actualLaps, estimate.Source));
    }

    private static HistoricalSessionContext BuildContext(CheckpointProbe probe)
    {
        return new HistoricalSessionContext
        {
            Car = new HistoricalCarIdentity(),
            Track = new HistoricalTrackIdentity(),
            Session = new HistoricalSessionIdentity
            {
                SessionType = probe.SessionType,
                SessionName = probe.SessionName,
                EventType = probe.EventType,
                TeamRacing = probe.TeamRacing,
                SessionTime = probe.ScheduledSessionTime,
                SessionLaps = probe.ScheduledSessionLaps
            },
            Conditions = new HistoricalSessionInfoConditions()
        };
    }

    private static LiveSessionModel BuildSession(HistoricalSessionContext context, CheckpointProbe probe)
    {
        return new LiveSessionModel(
            HasData: true,
            Quality: LiveModelQuality.Reliable,
            Combo: HistoricalComboIdentity.From(context),
            SessionType: probe.SessionType,
            SessionName: probe.SessionName,
            EventType: probe.EventType,
            TeamRacing: probe.TeamRacing,
            SessionTimeSeconds: ValidNonNegative(probe.SessionTimeSeconds),
            SessionTimeRemainSeconds: ValidNonNegative(probe.SessionTimeRemainSeconds),
            SessionTimeTotalSeconds: ValidPositive(probe.SessionTimeTotalSeconds),
            SessionLapsRemain: probe.SessionLapsRemain,
            SessionLapsTotal: probe.SessionLapsTotal,
            RaceLaps: probe.RaceLaps,
            SessionState: probe.SessionState,
            SessionFlags: probe.SessionFlags,
            TrackDisplayName: null,
            TrackLengthKm: null,
            CarDisplayName: null,
            MissingSignals: []);
    }

    private static double? EstimateFinishLaps(CheckpointProbe probe, LiveRaceLapEstimate estimate)
    {
        if (estimate.LapsRemaining is null)
        {
            return null;
        }

        if (probe.SessionState is >= 5)
        {
            return probe.OverallLeaderProgressLaps
                ?? probe.ClassLeaderProgressLaps
                ?? probe.StrategyProgressLaps
                ?? 0d;
        }

        if (ValidLapCount(probe.SessionLapsTotal) is { } lapTotal)
        {
            return lapTotal;
        }

        if (!IsRacePreGreen(probe)
            && probe.OverallLeaderProgressLaps is { } leaderProgress
            && ValidPositive(probe.SessionTimeRemainSeconds) is { } remainingSeconds
            && LiveRaceProgressProjector.ValidLapTime(probe.RacePaceSeconds) is { } racePace)
        {
            return Math.Ceiling(leaderProgress + remainingSeconds / racePace);
        }

        return probe.StrategyProgressLaps is { } strategyProgress
            ? strategyProgress + estimate.LapsRemaining.Value
            : estimate.LapsRemaining.Value;
    }

    private static bool IsRacePreGreen(CheckpointProbe probe)
    {
        return probe.SessionState is >= 1 and <= 3
            && (ContainsRace(probe.SessionType)
                || ContainsRace(probe.SessionName)
                || ContainsRace(probe.EventType));
    }

    private static SimpleTelemetryTone CellTone(double finishLaps, double? actualLaps, string source)
    {
        if (string.Equals(source, "non-race session", StringComparison.OrdinalIgnoreCase))
        {
            return SimpleTelemetryTone.Waiting;
        }

        if (actualLaps is null)
        {
            return SimpleTelemetryTone.Info;
        }

        return Math.Abs(finishLaps - actualLaps.Value) <= 0.25d
            ? SimpleTelemetryTone.Success
            : SimpleTelemetryTone.Warning;
    }

    private static SimpleTelemetryTone RowTone(MaterializedWorkbenchRow row)
    {
        var cells = new[] { row.StartOfRace, row.MiddleOfStint1, row.AfterFirstStop, row.Halfway };
        if (cells.Any(cell => cell.Tone == SimpleTelemetryTone.Warning))
        {
            return SimpleTelemetryTone.Warning;
        }

        return cells.Any(cell => cell.Tone is SimpleTelemetryTone.Success or SimpleTelemetryTone.Info)
            ? SimpleTelemetryTone.Info
            : SimpleTelemetryTone.Waiting;
    }

    private static string SourceToken(string source, CheckpointProbe probe)
    {
        if (string.Equals(source, "non-race session", StringComparison.OrdinalIgnoreCase))
        {
            return "non-race";
        }

        if (string.Equals(source, "session ended", StringComparison.OrdinalIgnoreCase))
        {
            return "ended";
        }

        if (ValidLapCount(probe.SessionLapsTotal) is not null
            || source.Contains("session laps", StringComparison.OrdinalIgnoreCase)
            || source.Contains("session lap total", StringComparison.OrdinalIgnoreCase))
        {
            return "SDK";
        }

        if (source.Contains("leader", StringComparison.OrdinalIgnoreCase))
        {
            return "leader";
        }

        if (source.Contains("team", StringComparison.OrdinalIgnoreCase))
        {
            return "team";
        }

        if (source.Contains("scheduled", StringComparison.OrdinalIgnoreCase)
            || source.Contains("estimate", StringComparison.OrdinalIgnoreCase))
        {
            return "seed";
        }

        return "--";
    }

    private static string LegacyValue(MaterializedWorkbenchRow row)
    {
        return string.Join(
            " | ",
            new[]
            {
                $"start {row.StartOfRace.Value}",
                $"mid {row.MiddleOfStint1.Value}",
                $"stop {row.AfterFirstStop.Value}",
                $"half {row.Halfway.Value}"
            });
    }

    private static SimpleTelemetryMetricSegmentViewModel Segment(string label, WorkbenchCell cell)
    {
        return new SimpleTelemetryMetricSegmentViewModel(label, cell.Value, cell.Tone);
    }

    private static string FormatLaps(double laps)
    {
        var rounded = Math.Round(laps);
        return Math.Abs(laps - rounded) <= 0.05d
            ? rounded.ToString("0", CultureInfo.InvariantCulture)
            : laps.ToString("0.0", CultureInfo.InvariantCulture);
    }

    private static double? ValidLapCount(int? laps)
    {
        return laps is { } lapCount
            && lapCount > 0
            && lapCount < UnlimitedLapsSentinel
            && lapCount <= MaxPlausibleLiveLapCount
            ? lapCount
            : null;
    }

    private static double? ValidPositive(double? value)
    {
        return value is { } positiveValue && positiveValue > 0d && IsFinite(positiveValue)
            ? positiveValue
            : null;
    }

    private static double? ValidNonNegative(double? value)
    {
        return value is { } positiveValue && positiveValue >= 0d && IsFinite(positiveValue)
            ? positiveValue
            : null;
    }

    private static bool ContainsRace(string? value)
    {
        return value?.IndexOf("race", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }

    private static readonly WorkbenchRow[] Rows =
    [
        new(
            Label: "Dallara 45m",
            Summary: "timed race / full proof",
            CaptureId: "capture-20260522-204847-774",
            ActualLaps: 6,
            ActualLabel: "6 actual",
            StartOfRace: new(
            FrameIndex: 19086,
            SessionInfoUpdate: 103,
            SessionType: "Race",
            SessionName: "RACE",
            EventType: "Race",
            TeamRacing: false,
            ScheduledSessionTime: "2700.0000 sec",
            ScheduledSessionLaps: "unlimited",
            SessionTimeSeconds: 0.233333d,
            SessionTimeRemainSeconds: -1d,
            SessionTimeTotalSeconds: 2700d,
            SessionLapsRemain: 32767,
            SessionLapsTotal: 32767,
            RaceLaps: 0,
            SessionState: 1,
            SessionFlags: 268435456,
            StrategyProgressLaps: 0d,
            OverallLeaderProgressLaps: null,
            ClassLeaderProgressLaps: null,
            RacePaceSeconds: 450.9073d,
            RacePaceSource: "driver estimate"),
            MiddleOfStint1: new(
            FrameIndex: 89314,
            SessionInfoUpdate: 182,
            SessionType: "Race",
            SessionName: "RACE",
            EventType: "Race",
            TeamRacing: false,
            ScheduledSessionTime: "2700.0000 sec",
            ScheduledSessionLaps: "unlimited",
            SessionTimeSeconds: 1171.15d,
            SessionTimeRemainSeconds: 1732.05d,
            SessionTimeTotalSeconds: 2700d,
            SessionLapsRemain: 32767,
            SessionLapsTotal: 32767,
            RaceLaps: 3,
            SessionState: 4,
            SessionFlags: 268697600,
            StrategyProgressLaps: 1.999304d,
            OverallLeaderProgressLaps: 2.143373d,
            ClassLeaderProgressLaps: 2.029878d,
            RacePaceSeconds: 444.028412d,
            RacePaceSource: "overall leader last lap"),
            AfterFirstStop: new(
            FrameIndex: 148916,
            SessionInfoUpdate: 269,
            SessionType: "Race",
            SessionName: "RACE",
            EventType: "Race",
            TeamRacing: false,
            ScheduledSessionTime: "2700.0000 sec",
            ScheduledSessionLaps: "unlimited",
            SessionTimeSeconds: 2164.783333d,
            SessionTimeRemainSeconds: 738.416667d,
            SessionTimeTotalSeconds: 2700d,
            SessionLapsRemain: 32767,
            SessionLapsTotal: 32767,
            RaceLaps: 5,
            SessionState: 4,
            SessionFlags: 268697600,
            StrategyProgressLaps: 4.018543d,
            OverallLeaderProgressLaps: 4.285448d,
            ClassLeaderProgressLaps: 4.063562d,
            RacePaceSeconds: 448.7883d,
            RacePaceSource: "overall leader last lap"),
            Halfway: new(
            FrameIndex: 112599,
            SessionInfoUpdate: 215,
            SessionType: "Race",
            SessionName: "RACE",
            EventType: "Race",
            TeamRacing: false,
            ScheduledSessionTime: "2700.0000 sec",
            ScheduledSessionLaps: "unlimited",
            SessionTimeSeconds: 1559.366667d,
            SessionTimeRemainSeconds: 1343.833333d,
            SessionTimeTotalSeconds: 2700d,
            SessionLapsRemain: 32767,
            SessionLapsTotal: 32767,
            RaceLaps: 4,
            SessionState: 4,
            SessionFlags: 268697856,
            StrategyProgressLaps: 2.784916d,
            OverallLeaderProgressLaps: 3.040308d,
            ClassLeaderProgressLaps: 2.836796d,
            RacePaceSeconds: 444.454193d,
            RacePaceSource: "overall leader last lap")),
        new(
            Label: "Dallara 4L full",
            Summary: "fixed-lap race / full proof",
            CaptureId: "capture-20260523-034827-919",
            ActualLaps: 4,
            ActualLabel: "4 fixed",
            StartOfRace: new(
            FrameIndex: 18822,
            SessionInfoUpdate: 88,
            SessionType: "Race",
            SessionName: "RACE",
            EventType: "Race",
            TeamRacing: false,
            ScheduledSessionTime: "2100.0000 sec",
            ScheduledSessionLaps: "4",
            SessionTimeSeconds: 1d,
            SessionTimeRemainSeconds: 58.016667d,
            SessionTimeTotalSeconds: 2100d,
            SessionLapsRemain: 4,
            SessionLapsTotal: 4,
            RaceLaps: 0,
            SessionState: 1,
            SessionFlags: 268435968,
            StrategyProgressLaps: 0d,
            OverallLeaderProgressLaps: null,
            ClassLeaderProgressLaps: null,
            RacePaceSeconds: 450.9073d,
            RacePaceSource: "driver estimate"),
            MiddleOfStint1: new(
            FrameIndex: 74599,
            SessionInfoUpdate: 140,
            SessionType: "Race",
            SessionName: "RACE",
            EventType: "Race",
            TeamRacing: false,
            ScheduledSessionTime: "2100.0000 sec",
            ScheduledSessionLaps: "4",
            SessionTimeSeconds: 930.833333d,
            SessionTimeRemainSeconds: 1367.683333d,
            SessionTimeTotalSeconds: 2100d,
            SessionLapsRemain: 3,
            SessionLapsTotal: 4,
            RaceLaps: 2,
            SessionState: 4,
            SessionFlags: 268697600,
            StrategyProgressLaps: 1.472624d,
            OverallLeaderProgressLaps: 1.616234d,
            ClassLeaderProgressLaps: 1.490846d,
            RacePaceSeconds: 449.105408d,
            RacePaceSource: "overall leader last lap"),
            AfterFirstStop: new(
            FrameIndex: 119461,
            SessionInfoUpdate: 197,
            SessionType: "Race",
            SessionName: "RACE",
            EventType: "Race",
            TeamRacing: false,
            ScheduledSessionTime: "2100.0000 sec",
            ScheduledSessionLaps: "4",
            SessionTimeSeconds: 1678.65d,
            SessionTimeRemainSeconds: 619.866667d,
            SessionTimeTotalSeconds: 2100d,
            SessionLapsRemain: 1,
            SessionLapsTotal: 4,
            RaceLaps: 4,
            SessionState: 4,
            SessionFlags: 268697602,
            StrategyProgressLaps: 3.018616d,
            OverallLeaderProgressLaps: 3.190723d,
            ClassLeaderProgressLaps: 3.021613d,
            RacePaceSeconds: 451.629486d,
            RacePaceSource: "overall leader last lap"),
            Halfway: new(
            FrameIndex: 85067,
            SessionInfoUpdate: 147,
            SessionType: "Race",
            SessionName: "RACE",
            EventType: "Race",
            TeamRacing: false,
            ScheduledSessionTime: "2100.0000 sec",
            ScheduledSessionLaps: "4",
            SessionTimeSeconds: 1105.316667d,
            SessionTimeRemainSeconds: 1193.2d,
            SessionTimeTotalSeconds: 2100d,
            SessionLapsRemain: 2,
            SessionLapsTotal: 4,
            RaceLaps: 3,
            SessionState: 4,
            SessionFlags: 268697600,
            StrategyProgressLaps: 1.842515d,
            OverallLeaderProgressLaps: 2.025932d,
            ClassLeaderProgressLaps: 1.864972d,
            RacePaceSeconds: 445.887085d,
            RacePaceSource: "overall leader last lap")),
        new(
            Label: "Dallara 4L blip",
            Summary: "fixed-lap race / SDK blip",
            CaptureId: "capture-20260522-194832-318",
            ActualLaps: 4,
            ActualLabel: "4 fixed",
            StartOfRace: new(
            FrameIndex: 19118,
            SessionInfoUpdate: 85,
            SessionType: "Race",
            SessionName: "RACE",
            EventType: "Race",
            TeamRacing: false,
            ScheduledSessionTime: "2100.0000 sec",
            ScheduledSessionLaps: "4",
            SessionTimeSeconds: 1.3d,
            SessionTimeRemainSeconds: -1d,
            SessionTimeTotalSeconds: 2100d,
            SessionLapsRemain: 4,
            SessionLapsTotal: 4,
            RaceLaps: 0,
            SessionState: 1,
            SessionFlags: 268435456,
            StrategyProgressLaps: 0d,
            OverallLeaderProgressLaps: null,
            ClassLeaderProgressLaps: null,
            RacePaceSeconds: 450.9073d,
            RacePaceSource: "driver estimate"),
            MiddleOfStint1: new(
            FrameIndex: 75264,
            SessionInfoUpdate: 149,
            SessionType: "Race",
            SessionName: "RACE",
            EventType: "Race",
            TeamRacing: false,
            ScheduledSessionTime: "2100.0000 sec",
            ScheduledSessionLaps: "4",
            SessionTimeSeconds: 937.083333d,
            SessionTimeRemainSeconds: 1367.8d,
            SessionTimeTotalSeconds: 2100d,
            SessionLapsRemain: 3,
            SessionLapsTotal: 4,
            RaceLaps: 2,
            SessionState: 4,
            SessionFlags: 269746176,
            StrategyProgressLaps: 1.471666d,
            OverallLeaderProgressLaps: 1.644483d,
            ClassLeaderProgressLaps: 1.496287d,
            RacePaceSeconds: 439.602386d,
            RacePaceSource: "overall leader last lap"),
            AfterFirstStop: new(
            FrameIndex: 122998,
            SessionInfoUpdate: 221,
            SessionType: "Race",
            SessionName: "RACE",
            EventType: "Race",
            TeamRacing: false,
            ScheduledSessionTime: "2100.0000 sec",
            ScheduledSessionLaps: "4",
            SessionTimeSeconds: 1732.7d,
            SessionTimeRemainSeconds: 572.183333d,
            SessionTimeTotalSeconds: 2100d,
            SessionLapsRemain: 1,
            SessionLapsTotal: 4,
            RaceLaps: 4,
            SessionState: 4,
            SessionFlags: 269746178,
            StrategyProgressLaps: 3.007212d,
            OverallLeaderProgressLaps: 3.386943d,
            ClassLeaderProgressLaps: 3.141442d,
            RacePaceSeconds: 443.5383d,
            RacePaceSource: "overall leader last lap"),
            Halfway: new(
            FrameIndex: 71060,
            SessionInfoUpdate: 144,
            SessionType: "Race",
            SessionName: "RACE",
            EventType: "Race",
            TeamRacing: false,
            ScheduledSessionTime: "2100.0000 sec",
            ScheduledSessionLaps: "4",
            SessionTimeSeconds: 867d,
            SessionTimeRemainSeconds: 1437.883333d,
            SessionTimeTotalSeconds: 2100d,
            SessionLapsRemain: 3,
            SessionLapsTotal: 4,
            RaceLaps: 2,
            SessionState: 4,
            SessionFlags: 269746176,
            StrategyProgressLaps: 1.327493d,
            OverallLeaderProgressLaps: 1.474047d,
            ClassLeaderProgressLaps: 1.354814d,
            RacePaceSeconds: 439.602386d,
            RacePaceSource: "overall leader last lap")),
        new(
            Label: "Dallara 4L early",
            Summary: "fixed-lap race / start only",
            CaptureId: "capture-20260523-194833-742",
            ActualLaps: null,
            ActualLabel: "no finish",
            StartOfRace: new(
            FrameIndex: 19473,
            SessionInfoUpdate: 112,
            SessionType: "Race",
            SessionName: "RACE",
            EventType: "Race",
            TeamRacing: false,
            ScheduledSessionTime: "2100.0000 sec",
            ScheduledSessionLaps: "4",
            SessionTimeSeconds: 0.166667d,
            SessionTimeRemainSeconds: -1d,
            SessionTimeTotalSeconds: 2100d,
            SessionLapsRemain: 4,
            SessionLapsTotal: 4,
            RaceLaps: 0,
            SessionState: 1,
            SessionFlags: 268435456,
            StrategyProgressLaps: 0d,
            OverallLeaderProgressLaps: null,
            ClassLeaderProgressLaps: null,
            RacePaceSeconds: 450.9073d,
            RacePaceSource: "driver estimate"),
            MiddleOfStint1: null,
            AfterFirstStop: null,
            Halfway: null),
        new(
            Label: "GR86 3L start",
            Summary: "fixed-lap race / clock sentinel",
            CaptureId: "capture-20260523-200213-824",
            ActualLaps: 3,
            ActualLabel: "3 fixed",
            StartOfRace: new(
            FrameIndex: 21816,
            SessionInfoUpdate: 38,
            SessionType: "Race",
            SessionName: "RACE",
            EventType: "Race",
            TeamRacing: false,
            ScheduledSessionTime: "unlimited",
            ScheduledSessionLaps: "3",
            SessionTimeSeconds: 0.183333d,
            SessionTimeRemainSeconds: 604800d,
            SessionTimeTotalSeconds: 604800d,
            SessionLapsRemain: 3,
            SessionLapsTotal: 3,
            RaceLaps: 0,
            SessionState: 1,
            SessionFlags: 268435456,
            StrategyProgressLaps: 0d,
            OverallLeaderProgressLaps: null,
            ClassLeaderProgressLaps: null,
            RacePaceSeconds: 459.5304d,
            RacePaceSource: "driver estimate"),
            MiddleOfStint1: null,
            AfterFirstStop: null,
            Halfway: null),
        new(
            Label: "VLN 4h team",
            Summary: "timed endurance / team proof",
            CaptureId: "capture-20260426-130334-932",
            ActualLaps: 30,
            ActualLabel: "30 actual",
            StartOfRace: new(
            FrameIndex: 139460,
            SessionInfoUpdate: 340,
            SessionType: "Race",
            SessionName: "RACE",
            EventType: "Race",
            TeamRacing: true,
            ScheduledSessionTime: "14400.0000 sec",
            ScheduledSessionLaps: "unlimited",
            SessionTimeSeconds: 0.5d,
            SessionTimeRemainSeconds: -1d,
            SessionTimeTotalSeconds: 14400d,
            SessionLapsRemain: 32767,
            SessionLapsTotal: 32767,
            RaceLaps: 0,
            SessionState: 1,
            SessionFlags: 268435456,
            StrategyProgressLaps: 0d,
            OverallLeaderProgressLaps: null,
            ClassLeaderProgressLaps: null,
            RacePaceSeconds: 465.166d,
            RacePaceSource: "driver estimate"),
            MiddleOfStint1: new(
            FrameIndex: 248717,
            SessionInfoUpdate: 473,
            SessionType: "Race",
            SessionName: "RACE",
            EventType: "Race",
            TeamRacing: true,
            ScheduledSessionTime: "14400.0000 sec",
            ScheduledSessionLaps: "unlimited",
            SessionTimeSeconds: 1821.533333d,
            SessionTimeRemainSeconds: 12844.266667d,
            SessionTimeTotalSeconds: 14400d,
            SessionLapsRemain: 32767,
            SessionLapsTotal: 32767,
            RaceLaps: 4,
            SessionState: 4,
            SessionFlags: 268697600,
            StrategyProgressLaps: 3.172168d,
            OverallLeaderProgressLaps: 3.243565d,
            ClassLeaderProgressLaps: 3.243565d,
            RacePaceSeconds: 473.066101d,
            RacePaceSource: "overall leader last lap"),
            AfterFirstStop: null,
            Halfway: new(
            FrameIndex: 587741,
            SessionInfoUpdate: 1361,
            SessionType: "Race",
            SessionName: "RACE",
            EventType: "Race",
            TeamRacing: true,
            ScheduledSessionTime: "14400.0000 sec",
            ScheduledSessionLaps: "unlimited",
            SessionTimeSeconds: 7471.933333d,
            SessionTimeRemainSeconds: 7193.866667d,
            SessionTimeTotalSeconds: 14400d,
            SessionLapsRemain: 32767,
            SessionLapsTotal: 32767,
            RaceLaps: 15,
            SessionState: 4,
            SessionFlags: 268697600,
            StrategyProgressLaps: 14.756831d,
            OverallLeaderProgressLaps: null,
            ClassLeaderProgressLaps: null,
            RacePaceSeconds: 486.336487d,
            RacePaceSource: "team last lap")),
        new(
            Label: "24h rejoin",
            Summary: "timed endurance / rejoin",
            CaptureId: "capture-20260502-143722-571",
            ActualLaps: 173d,
            ActualLabel: "173 actual",
            StartOfRace: null,
            MiddleOfStint1: null,
            AfterFirstStop: null,
            Halfway: new(
            FrameIndex: 138838,
            SessionInfoUpdate: 370,
            SessionType: "Race",
            SessionName: "RACE",
            EventType: "Race",
            TeamRacing: true,
            ScheduledSessionTime: "86400.0000 sec",
            ScheduledSessionLaps: "unlimited",
            SessionTimeSeconds: 59324.102605d,
            SessionTimeRemainSeconds: 27326.030729d,
            SessionTimeTotalSeconds: 86400d,
            SessionLapsRemain: 32767,
            SessionLapsTotal: 32767,
            RaceLaps: 119,
            SessionState: 4,
            SessionFlags: 268697600,
            StrategyProgressLaps: 113.623355d,
            OverallLeaderProgressLaps: 118.011051d,
            ClassLeaderProgressLaps: 118.011051d,
            RacePaceSeconds: 493.780304d,
            RacePaceSource: "overall leader last lap")),
        new(
            Label: "Dallara timed mid",
            Summary: "timed race / mid capture",
            CaptureId: "capture-20260522-185231-444",
            ActualLaps: null,
            ActualLabel: "unknown",
            StartOfRace: null,
            MiddleOfStint1: new(
            FrameIndex: 32444,
            SessionInfoUpdate: 10,
            SessionType: "Race",
            SessionName: "RACE",
            EventType: "Race",
            TeamRacing: false,
            ScheduledSessionTime: "2700.0000 sec",
            ScheduledSessionLaps: "unlimited",
            SessionTimeSeconds: 465.733333d,
            SessionTimeRemainSeconds: 2438.45d,
            SessionTimeTotalSeconds: 2700d,
            SessionLapsRemain: 32767,
            SessionLapsTotal: 32767,
            RaceLaps: 1,
            SessionState: 4,
            SessionFlags: 268435460,
            StrategyProgressLaps: 0d,
            OverallLeaderProgressLaps: 0.547272d,
            ClassLeaderProgressLaps: 0.47894d,
            RacePaceSeconds: 450.9073d,
            RacePaceSource: "driver estimate"),
            AfterFirstStop: null,
            Halfway: null),
        new(
            Label: "BMW 45m early",
            Summary: "timed race / early capture",
            CaptureId: "latest-capture",
            ActualLaps: null,
            ActualLabel: "unknown",
            StartOfRace: null,
            MiddleOfStint1: null,
            AfterFirstStop: null,
            Halfway: null),
        new(
            Label: "Dallara practice",
            Summary: "practice control",
            CaptureId: "capture-20260522-192333-050",
            ActualLaps: null,
            ActualLabel: "non-race",
            StartOfRace: new(
            FrameIndex: 1,
            SessionInfoUpdate: 1,
            SessionType: "Practice",
            SessionName: "PRACTICE",
            EventType: "Practice",
            TeamRacing: false,
            ScheduledSessionTime: "7200.0000 sec",
            ScheduledSessionLaps: "unlimited",
            SessionTimeSeconds: 2162.466732d,
            SessionTimeRemainSeconds: -1d,
            SessionTimeTotalSeconds: 7200d,
            SessionLapsRemain: 32767,
            SessionLapsTotal: 32767,
            RaceLaps: 0,
            SessionState: 4,
            SessionFlags: 268435456,
            StrategyProgressLaps: 0d,
            OverallLeaderProgressLaps: null,
            ClassLeaderProgressLaps: null,
            RacePaceSeconds: 450.9073d,
            RacePaceSource: "driver estimate"),
            MiddleOfStint1: new(
            FrameIndex: 47999,
            SessionInfoUpdate: 44,
            SessionType: "Practice",
            SessionName: "PRACTICE",
            EventType: "Practice",
            TeamRacing: false,
            ScheduledSessionTime: "7200.0000 sec",
            ScheduledSessionLaps: "unlimited",
            SessionTimeSeconds: 2962.466732d,
            SessionTimeRemainSeconds: 4237.549935d,
            SessionTimeTotalSeconds: 7200d,
            SessionLapsRemain: 32767,
            SessionLapsTotal: 32767,
            RaceLaps: 1,
            SessionState: 4,
            SessionFlags: 268697600,
            StrategyProgressLaps: 0.63291d,
            OverallLeaderProgressLaps: 0.63291d,
            ClassLeaderProgressLaps: 0.63291d,
            RacePaceSeconds: 450.9073d,
            RacePaceSource: "driver estimate"),
            AfterFirstStop: null,
            Halfway: new(
            FrameIndex: 41727,
            SessionInfoUpdate: 36,
            SessionType: "Practice",
            SessionName: "PRACTICE",
            EventType: "Practice",
            TeamRacing: false,
            ScheduledSessionTime: "7200.0000 sec",
            ScheduledSessionLaps: "unlimited",
            SessionTimeSeconds: 2857.933398d,
            SessionTimeRemainSeconds: 4342.083268d,
            SessionTimeTotalSeconds: 7200d,
            SessionLapsRemain: 32767,
            SessionLapsTotal: 32767,
            RaceLaps: 1,
            SessionState: 4,
            SessionFlags: 268697600,
            StrategyProgressLaps: 0.404729d,
            OverallLeaderProgressLaps: 0.404729d,
            ClassLeaderProgressLaps: 0.404729d,
            RacePaceSeconds: 450.9073d,
            RacePaceSource: "driver estimate")),
        new(
            Label: "Dallara quali",
            Summary: "qualifying control",
            CaptureId: "capture-20260523-031807-356",
            ActualLaps: null,
            ActualLabel: "non-race",
            StartOfRace: new(
            FrameIndex: 1,
            SessionInfoUpdate: 1,
            SessionType: "Lone Qualify",
            SessionName: "QUALIFY",
            EventType: "Qualify",
            TeamRacing: false,
            ScheduledSessionTime: "1200.0000 sec",
            ScheduledSessionLaps: "1",
            SessionTimeSeconds: 12.966667d,
            SessionTimeRemainSeconds: -1d,
            SessionTimeTotalSeconds: 1200d,
            SessionLapsRemain: 1,
            SessionLapsTotal: 1,
            RaceLaps: 0,
            SessionState: 4,
            SessionFlags: 268435456,
            StrategyProgressLaps: 0d,
            OverallLeaderProgressLaps: null,
            ClassLeaderProgressLaps: null,
            RacePaceSeconds: 450.9073d,
            RacePaceSource: "driver estimate"),
            MiddleOfStint1: new(
            FrameIndex: 12497,
            SessionInfoUpdate: 20,
            SessionType: "Lone Qualify",
            SessionName: "QUALIFY",
            EventType: "Qualify",
            TeamRacing: false,
            ScheduledSessionTime: "1200.0000 sec",
            ScheduledSessionLaps: "1",
            SessionTimeSeconds: 309.983331d,
            SessionTimeRemainSeconds: 890.033336d,
            SessionTimeTotalSeconds: 1200d,
            SessionLapsRemain: 1,
            SessionLapsTotal: 1,
            RaceLaps: 2,
            SessionState: 4,
            SessionFlags: 268697600,
            StrategyProgressLaps: 0.218499d,
            OverallLeaderProgressLaps: 0.982794d,
            ClassLeaderProgressLaps: 0.218499d,
            RacePaceSeconds: 450.9073d,
            RacePaceSource: "driver estimate"),
            AfterFirstStop: null,
            Halfway: new(
            FrameIndex: 7366,
            SessionInfoUpdate: 18,
            SessionType: "Lone Qualify",
            SessionName: "QUALIFY",
            EventType: "Qualify",
            TeamRacing: false,
            ScheduledSessionTime: "1200.0000 sec",
            ScheduledSessionLaps: "1",
            SessionTimeSeconds: 224.466664d,
            SessionTimeRemainSeconds: 975.550003d,
            SessionTimeTotalSeconds: 1200d,
            SessionLapsRemain: 1,
            SessionLapsTotal: 1,
            RaceLaps: 1,
            SessionState: 4,
            SessionFlags: 268697600,
            StrategyProgressLaps: 0.054726d,
            OverallLeaderProgressLaps: 0.147912d,
            ClassLeaderProgressLaps: 0.054726d,
            RacePaceSeconds: 450.9073d,
            RacePaceSource: "driver estimate")),
        new(
            Label: "Daytona offline",
            Summary: "offline test control",
            CaptureId: "capture-20260525-182213-419",
            ActualLaps: null,
            ActualLabel: "unknown",
            StartOfRace: new(
            FrameIndex: 1,
            SessionInfoUpdate: 1,
            SessionType: "Offline Testing",
            SessionName: "TESTING",
            EventType: "Test",
            TeamRacing: false,
            ScheduledSessionTime: "unlimited",
            ScheduledSessionLaps: "unlimited",
            SessionTimeSeconds: 28.733333d,
            SessionTimeRemainSeconds: 604800d,
            SessionTimeTotalSeconds: 604800d,
            SessionLapsRemain: 32767,
            SessionLapsTotal: 32767,
            RaceLaps: 0,
            SessionState: 4,
            SessionFlags: 268435456,
            StrategyProgressLaps: 0d,
            OverallLeaderProgressLaps: null,
            ClassLeaderProgressLaps: null,
            RacePaceSeconds: 47.7706d,
            RacePaceSource: "driver estimate"),
            MiddleOfStint1: new(
            FrameIndex: 53288,
            SessionInfoUpdate: 29,
            SessionType: "Offline Testing",
            SessionName: "TESTING",
            EventType: "Test",
            TeamRacing: false,
            ScheduledSessionTime: "unlimited",
            ScheduledSessionLaps: "unlimited",
            SessionTimeSeconds: 917.05d,
            SessionTimeRemainSeconds: 604800d,
            SessionTimeTotalSeconds: 604800d,
            SessionLapsRemain: 32767,
            SessionLapsTotal: 32767,
            RaceLaps: 8,
            SessionState: 4,
            SessionFlags: 268698112,
            StrategyProgressLaps: 7.416525d,
            OverallLeaderProgressLaps: 7.416525d,
            ClassLeaderProgressLaps: 7.416525d,
            RacePaceSeconds: 103.976997d,
            RacePaceSource: "overall leader last lap"),
            AfterFirstStop: null,
            Halfway: new(
            FrameIndex: 47874,
            SessionInfoUpdate: 24,
            SessionType: "Offline Testing",
            SessionName: "TESTING",
            EventType: "Test",
            TeamRacing: false,
            ScheduledSessionTime: "unlimited",
            ScheduledSessionLaps: "unlimited",
            SessionTimeSeconds: 826.816667d,
            SessionTimeRemainSeconds: 604800d,
            SessionTimeTotalSeconds: 604800d,
            SessionLapsRemain: 32767,
            SessionLapsTotal: 32767,
            RaceLaps: 7,
            SessionState: 4,
            SessionFlags: 268698112,
            StrategyProgressLaps: 6.312331d,
            OverallLeaderProgressLaps: 6.312331d,
            ClassLeaderProgressLaps: 6.312331d,
            RacePaceSeconds: 77.146896d,
            RacePaceSource: "overall leader last lap")),
    ];

    private sealed record MaterializedWorkbenchRow(
        string Label,
        string Summary,
        WorkbenchCell StartOfRace,
        WorkbenchCell MiddleOfStint1,
        WorkbenchCell AfterFirstStop,
        WorkbenchCell Halfway,
        WorkbenchCell Actual);

    private sealed record WorkbenchRow(
        string Label,
        string Summary,
        string CaptureId,
        double? ActualLaps,
        string ActualLabel,
        CheckpointProbe? StartOfRace,
        CheckpointProbe? MiddleOfStint1,
        CheckpointProbe? AfterFirstStop,
        CheckpointProbe? Halfway);

    private sealed record CheckpointProbe(
        int FrameIndex,
        int SessionInfoUpdate,
        string? SessionType,
        string? SessionName,
        string? EventType,
        bool? TeamRacing,
        string? ScheduledSessionTime,
        string? ScheduledSessionLaps,
        double? SessionTimeSeconds,
        double? SessionTimeRemainSeconds,
        double? SessionTimeTotalSeconds,
        int? SessionLapsRemain,
        int? SessionLapsTotal,
        int? RaceLaps,
        int? SessionState,
        int? SessionFlags,
        double? StrategyProgressLaps,
        double? OverallLeaderProgressLaps,
        double? ClassLeaderProgressLaps,
        double? RacePaceSeconds,
        string RacePaceSource);

    private sealed record WorkbenchCell(string Value, SimpleTelemetryTone Tone);
}
