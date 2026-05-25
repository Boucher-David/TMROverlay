using System.Globalization;
using TmrOverlay.App.Overlays.SimpleTelemetry;
using TmrOverlay.Core.Telemetry.Live;

namespace TmrOverlay.App.Overlays.Flags;

internal static class FlagsOverlayViewModel
{
    private const int CheckeredFlag = 0x00000001;
    private const int WhiteFlag = 0x00000002;
    private const int GreenFlag = 0x00000004;
    private const int YellowFlag = 0x00000008;
    private const int RedFlag = 0x00000010;
    private const int BlueFlag = 0x00000020;
    private const int DebrisFlag = 0x00000040;
    private const int CrossedFlag = 0x00000080;
    private const int WavingYellowFlag = 0x00000100;
    private const int OneToGreenFlag = 0x00000200;
    private const int GreenHeldFlag = 0x00000400;
    private const int TenToGoFlag = 0x00000800;
    private const int FiveToGoFlag = 0x00001000;
    private const int RandomWavingFlag = 0x00002000;
    private const int CautionFlag = 0x00004000;
    private const int WavingCautionFlag = 0x00008000;
    private const int BlackFlag = 0x00010000;
    private const int DisqualifyFlag = 0x00020000;
    private const int ServiceableFlag = 0x00040000;
    private const int FurledFlag = 0x00080000;
    private const int RepairFlag = 0x00100000;
    private const int ScoringInvalidFlag = 0x00200000;
    private const int UnknownDriverFlag = 0x00400000;
    private const int StartHiddenFlag = 0x10000000;
    private const int StartReadyFlag = 0x20000000;
    private const int StartSetFlag = 0x40000000;
    private const int StartGoFlag = unchecked((int)0x80000000);
    private const int MaxDisplayLapCount = 1000;

    private static readonly FlagLabel[] FlagLabels =
    [
        new(CheckeredFlag, "checkered"),
        new(WhiteFlag, "white"),
        new(GreenFlag, "green"),
        new(YellowFlag, "yellow"),
        new(RedFlag, "red"),
        new(BlueFlag, "blue"),
        new(DebrisFlag, "debris"),
        new(CrossedFlag, "crossed"),
        new(WavingYellowFlag, "waving yellow"),
        new(OneToGreenFlag, "one to green"),
        new(GreenHeldFlag, "green held"),
        new(TenToGoFlag, "ten to go"),
        new(FiveToGoFlag, "five to go"),
        new(RandomWavingFlag, "random waving"),
        new(CautionFlag, "caution"),
        new(WavingCautionFlag, "waving caution"),
        new(BlackFlag, "black"),
        new(DisqualifyFlag, "disqualify"),
        new(ServiceableFlag, "service"),
        new(FurledFlag, "furled"),
        new(RepairFlag, "repair"),
        new(ScoringInvalidFlag, "scoring invalid"),
        new(UnknownDriverFlag, "unknown driver flag"),
        new(StartHiddenFlag, "start hidden"),
        new(StartReadyFlag, "start ready"),
        new(StartSetFlag, "start set"),
        new(StartGoFlag, "start go")
    ];

    public static FlagOverlayDisplayViewModel ForDisplay(
        LiveTelemetrySnapshot snapshot,
        DateTimeOffset now)
    {
        if (!SimpleTelemetryOverlayViewModel.IsFresh(snapshot, now, out var waitingStatus))
        {
            return FlagOverlayDisplayViewModel.Waiting(waitingStatus);
        }

        var session = snapshot.Models.Session;
        if (!session.HasData && session.SessionFlags is null && session.SessionState is null)
        {
            return FlagOverlayDisplayViewModel.Waiting("waiting for session state");
        }

        var flags = session.SessionFlags;
        var localDriverFlags = LocalDriverSessionFlags(snapshot);
        var displayFlags = BuildDisplayFlags(flags, session, localDriverFlags);
        return new FlagOverlayDisplayViewModel(
            IsWaiting: false,
            Status: displayFlags.Count == 0 ? "none" : string.Join(" + ", displayFlags.Select(flag => flag.Label)),
            Tone: displayFlags.Count == 0 ? SimpleTelemetryTone.Info : StrongestTone(displayFlags),
            Flags: displayFlags,
            RawFlags: flags);
    }

    public static SimpleTelemetryOverlayViewModel From(
        LiveTelemetrySnapshot snapshot,
        DateTimeOffset now,
        string unitSystem)
    {
        if (!SimpleTelemetryOverlayViewModel.IsFresh(snapshot, now, out var waitingStatus))
        {
            return SimpleTelemetryOverlayViewModel.Waiting("Flags", waitingStatus);
        }

        var session = snapshot.Models.Session;
        if (!session.HasData && session.SessionFlags is null && session.SessionState is null)
        {
            return SimpleTelemetryOverlayViewModel.Waiting("Flags", "waiting for session state");
        }

        var flags = session.SessionFlags;
        var progress = snapshot.Models.RaceProgress;
        var projection = snapshot.Models.RaceProjection;
        var activeFlags = FormatFlags(flags);
        var sessionState = FormatSessionState(session);
        var tone = ToneFor(flags, session.SessionState);
        var status = flags is null
            ? sessionState
            : PrimaryFlagLabel(flags.Value) ?? "none";
        var timeLabel = IsRacePreGreen(session) ? "Countdown" : "Time left";
        var rows = new[]
        {
            new SimpleTelemetryRowViewModel("State", sessionState, tone),
            new SimpleTelemetryRowViewModel("Flags", activeFlags, tone),
            new SimpleTelemetryRowViewModel("Raw", FormatRawFlags(flags)),
            new SimpleTelemetryRowViewModel(timeLabel, SimpleTelemetryOverlayViewModel.FormatDuration(session.SessionTimeRemainSeconds, compact: true)),
            new SimpleTelemetryRowViewModel("Laps left", FormatLaps(session, progress, projection))
        };

        return new SimpleTelemetryOverlayViewModel(
            Title: "Flags",
            Status: status,
            Source: "source: session flags telemetry",
            Tone: tone,
            Rows: rows);
    }

    private static string FormatSessionState(LiveSessionModel session)
    {
        if (IsRaceSession(session))
        {
            return session.SessionState switch
            {
                1 => "race grid (1)",
                2 => "grid countdown (2)",
                3 => "parade laps (3)",
                4 => "racing (4)",
                5 => "checkered (5)",
                6 => "cool down (6)",
                null => "--",
                _ => $"state {session.SessionState.Value.ToString(CultureInfo.InvariantCulture)}"
            };
        }

        return session.SessionState switch
        {
            0 => "invalid (0)",
            1 => "get in car (1)",
            2 => "warmup (2)",
            3 => "parade laps (3)",
            4 => "racing (4)",
            5 => "checkered (5)",
            6 => "cool down (6)",
            null => "--",
            _ => $"state {session.SessionState.Value.ToString(CultureInfo.InvariantCulture)}"
        };
    }

    private static bool IsRacePreGreen(LiveSessionModel session)
    {
        return session.SessionState is >= 1 and <= 3 && IsRaceSession(session);
    }

    private static bool IsRaceSession(LiveSessionModel session)
    {
        return ContainsRace(session.SessionType)
            || ContainsRace(session.SessionName)
            || ContainsRace(session.EventType);
    }

    private static bool ContainsRace(string? value)
    {
        return value?.IndexOf("race", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string FormatFlags(int? flags)
    {
        if (flags is null)
        {
            return "--";
        }

        var active = FlagLabels
            .Where(label => HasFlag(flags.Value, label.Bit))
            .Select(label => label.Label)
            .ToArray();
        return active.Length == 0 ? "none" : string.Join(", ", active.Take(3)) + (active.Length > 3 ? $" +{active.Length - 3}" : string.Empty);
    }

    private static string FormatRawFlags(int? flags)
    {
        return flags is { } value
            ? $"0x{unchecked((uint)value).ToString("X8", CultureInfo.InvariantCulture)}"
            : "--";
    }

    private static string FormatLaps(
        LiveSessionModel session,
        LiveRaceProgressModel progress,
        LiveRaceProjectionModel projection)
    {
        var remain = FormatLapCount(session.SessionLapsRemain)
            ?? FormatEstimatedLapCount(projection.EstimatedTeamLapsRemaining)
            ?? FormatEstimatedLapCount(progress.RaceLapsRemaining);
        var total = FormatLapCount(session.RaceLaps)
            ?? FormatLapCount(session.SessionLapsTotal);
        if (remain is null && total is null)
        {
            return "--";
        }

        return $"{remain ?? "--"} / {total ?? "--"}";
    }

    private static string? FormatEstimatedLapCount(double? laps)
    {
        return laps is { } value && SimpleTelemetryOverlayViewModel.IsFinite(value) && value >= 0d && value <= MaxDisplayLapCount
            ? $"{value.ToString("0.#", CultureInfo.InvariantCulture)} est"
            : null;
    }

    private static string? FormatLapCount(int? laps)
    {
        return laps is { } value && value is > 0 and <= MaxDisplayLapCount
            ? value.ToString(CultureInfo.InvariantCulture)
            : null;
    }

    private static string? PrimaryFlagLabel(int flags)
    {
        if (HasFlag(flags, RepairFlag))
        {
            return "repair flag";
        }

        if (HasFlag(flags, BlackFlag))
        {
            return "black flag";
        }

        if (HasFlag(flags, DisqualifyFlag))
        {
            return "disqualified";
        }

        if (HasFlag(flags, ScoringInvalidFlag) || HasFlag(flags, UnknownDriverFlag))
        {
            return "driver flag";
        }

        if (HasFlag(flags, RedFlag))
        {
            return "red flag";
        }

        if (HasFlag(flags, FurledFlag))
        {
            return "black flag warning";
        }

        if (HasFlag(flags, WavingCautionFlag)
            || HasFlag(flags, CautionFlag)
            || HasFlag(flags, RandomWavingFlag)
            || HasFlag(flags, OneToGreenFlag)
            || HasFlag(flags, WavingYellowFlag)
            || HasFlag(flags, DebrisFlag)
            || HasFlag(flags, YellowFlag))
        {
            return "yellow/caution";
        }

        if (HasFlag(flags, BlueFlag))
        {
            return "blue flag";
        }

        if (HasFlag(flags, CheckeredFlag))
        {
            return "checkered";
        }

        if (HasFlag(flags, WhiteFlag))
        {
            return "white flag";
        }

        if (HasFlag(flags, FiveToGoFlag) || HasFlag(flags, TenToGoFlag) || HasFlag(flags, CrossedFlag))
        {
            return "race countdown";
        }

        return HasFlag(flags, GreenHeldFlag) || HasFlag(flags, GreenFlag) || HasFlag(flags, StartGoFlag)
            ? "green"
            : null;
    }

    private static SimpleTelemetryTone ToneFor(int? flags, int? sessionState)
    {
        if (flags is { } value)
        {
            if (HasFlag(value, BlackFlag)
                || HasFlag(value, RepairFlag)
                || HasFlag(value, UnknownDriverFlag)
                || HasFlag(value, ScoringInvalidFlag)
                || HasFlag(value, FurledFlag)
                || HasFlag(value, DisqualifyFlag)
                || HasFlag(value, RedFlag))
            {
                return SimpleTelemetryTone.Error;
            }

            if (HasFlag(value, WavingCautionFlag)
                || HasFlag(value, CautionFlag)
                || HasFlag(value, RandomWavingFlag)
                || HasFlag(value, OneToGreenFlag)
                || HasFlag(value, WavingYellowFlag)
                || HasFlag(value, DebrisFlag)
                || HasFlag(value, YellowFlag))
            {
                return SimpleTelemetryTone.Warning;
            }

            if (HasFlag(value, FiveToGoFlag)
                || HasFlag(value, TenToGoFlag)
                || HasFlag(value, CrossedFlag)
                || HasFlag(value, WhiteFlag)
                || HasFlag(value, CheckeredFlag))
            {
                return SimpleTelemetryTone.Info;
            }

            if (HasFlag(value, GreenHeldFlag) || HasFlag(value, GreenFlag) || HasFlag(value, StartGoFlag))
            {
                return SimpleTelemetryTone.Success;
            }
        }

        return sessionState == 4 ? SimpleTelemetryTone.Success : SimpleTelemetryTone.Info;
    }

    private static bool HasFlag(int flags, int bit)
    {
        return (flags & bit) != 0;
    }

    private static SimpleTelemetryTone StrongestTone(IReadOnlyList<FlagOverlayDisplayItem> flags)
    {
        if (flags.Any(flag => flag.Tone == SimpleTelemetryTone.Error))
        {
            return SimpleTelemetryTone.Error;
        }

        if (flags.Any(flag => flag.Tone == SimpleTelemetryTone.Warning))
        {
            return SimpleTelemetryTone.Warning;
        }

        if (flags.Any(flag => flag.Tone == SimpleTelemetryTone.Success))
        {
            return SimpleTelemetryTone.Success;
        }

        return flags.Any(flag => flag.Tone == SimpleTelemetryTone.Info)
            ? SimpleTelemetryTone.Info
            : SimpleTelemetryTone.Normal;
    }

    private static IReadOnlyList<FlagOverlayDisplayItem> BuildDisplayFlags(
        int? flags,
        LiveSessionModel session,
        int? localDriverFlags = null)
    {
        var items = new List<PrioritizedFlagDisplayItem>();
        var isRaceSession = IsRaceSession(session);
        var suppressGlobalYellowFamily = IsPracticeOrQualifyingSession(session)
            && !HasLocalYellowFamilyEvidence(localDriverFlags);
        if (flags is { } value)
        {
            AddCriticalFlags(value, localDriverFlags, items);
            AddYellowFlags(
                value,
                items,
                allowRaceStartPseudoFlags: isRaceSession,
                allowGlobalYellowFamily: !suppressGlobalYellowFamily);
            AddIf(
                value,
                BlueFlag,
                items,
                order: 50,
                new FlagOverlayDisplayItem(
                    FlagDisplayKind.Blue,
                    FlagDisplayCategory.Blue,
                    "Blue",
                    null,
                    SimpleTelemetryTone.Info));
            AddFinishFlags(value, items);
            AddGreenFlags(value, items, allowRaceStartPseudoFlags: isRaceSession);
        }

        if (session.SessionState == 5 && !items.Any(item => item.Item.Kind == FlagDisplayKind.Checkered))
        {
            items.Add(new PrioritizedFlagDisplayItem(
                60,
                new FlagOverlayDisplayItem(
                    FlagDisplayKind.Checkered,
                    FlagDisplayCategory.Finish,
                    "Checkered",
                    "session complete",
                    SimpleTelemetryTone.Info)));
        }

        return items
            .OrderBy(item => item.Order)
            .Select(item => item.Item)
            .ToArray();
    }

    private static bool IsPracticeOrQualifyingSession(LiveSessionModel session)
    {
        return ContainsAny(session.SessionType, "practice", "qualify", "qualifying")
            || ContainsAny(session.SessionName, "practice", "qualify", "qualifying")
            || ContainsAny(session.EventType, "practice", "qualify", "qualifying");
    }

    private static bool ContainsAny(string? value, params string[] needles)
    {
        return !string.IsNullOrWhiteSpace(value)
            && needles.Any(needle => value.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static bool HasLocalYellowFamilyEvidence(int? localDriverFlags)
    {
        return localDriverFlags is { } flags
            && (HasFlag(flags, YellowFlag)
                || HasFlag(flags, WavingYellowFlag)
                || HasFlag(flags, OneToGreenFlag)
                || HasFlag(flags, DebrisFlag)
                || HasFlag(flags, RandomWavingFlag)
                || HasFlag(flags, CautionFlag)
                || HasFlag(flags, WavingCautionFlag));
    }

    private static void AddCriticalFlags(
        int globalFlags,
        int? localDriverFlags,
        List<PrioritizedFlagDisplayItem> items)
    {
        AddIf(
            globalFlags,
            RedFlag,
            items,
            order: 10,
            new FlagOverlayDisplayItem(
                FlagDisplayKind.Red,
                FlagDisplayCategory.Critical,
                "Red",
                null,
                SimpleTelemetryTone.Error));

        if (localDriverFlags is not { } flags)
        {
            return;
        }

        AddIf(
            flags,
            RepairFlag,
            items,
            order: 20,
            new FlagOverlayDisplayItem(
                FlagDisplayKind.Meatball,
                FlagDisplayCategory.Critical,
                "Repair",
                null,
                SimpleTelemetryTone.Error));

        var blackLabels = new List<string>();
        AddBlackLabelIf(flags, BlackFlag, blackLabels, "Black");
        AddBlackLabelIf(flags, DisqualifyFlag, blackLabels, "DQ");
        AddBlackLabelIf(flags, ScoringInvalidFlag, blackLabels, "Scoring");
        AddBlackLabelIf(flags, UnknownDriverFlag, blackLabels, "Driver");
        AddBlackLabelIf(flags, FurledFlag, blackLabels, "Furled");
        if (blackLabels.Count == 0)
        {
            return;
        }

        items.Add(new PrioritizedFlagDisplayItem(
            30,
            new FlagOverlayDisplayItem(
                FlagDisplayKind.Black,
                FlagDisplayCategory.Critical,
                blackLabels[0],
                blackLabels.Count > 1 ? string.Join(" / ", blackLabels.Skip(1)) : null,
                SimpleTelemetryTone.Error)));
    }

    private static int? LocalDriverSessionFlags(LiveTelemetrySnapshot snapshot)
    {
        var incidentPressure = snapshot.Models.IncidentPressure;
        if (!incidentPressure.HasData)
        {
            return null;
        }

        var playerCarIdx = incidentPressure.PlayerCarIdx
            ?? snapshot.Models.DriverDirectory.PlayerCarIdx
            ?? snapshot.Models.Reference.PlayerCarIdx
            ?? snapshot.Models.Timing.PlayerCarIdx;

        return incidentPressure.Cars.FirstOrDefault(car =>
                car.IsPlayer || (playerCarIdx is { } carIdx && car.CarIdx == carIdx))
            ?.SessionFlags;
    }

    private static void AddYellowFlags(
        int flags,
        List<PrioritizedFlagDisplayItem> items,
        bool allowRaceStartPseudoFlags,
        bool allowGlobalYellowFamily)
    {
        if (!allowGlobalYellowFamily)
        {
            return;
        }

        if (HasFlag(flags, WavingCautionFlag) || HasFlag(flags, CautionFlag))
        {
            items.Add(new PrioritizedFlagDisplayItem(
                40,
                new FlagOverlayDisplayItem(
                    FlagDisplayKind.Caution,
                    FlagDisplayCategory.Yellow,
                    "Caution",
                    HasFlag(flags, WavingCautionFlag) ? "waving" : null,
                    SimpleTelemetryTone.Warning)));
            return;
        }

        var label = "Yellow";
        string? detail = null;
        if (allowRaceStartPseudoFlags && HasFlag(flags, OneToGreenFlag))
        {
            label = "One to green";
        }
        else if (HasFlag(flags, DebrisFlag))
        {
            label = "Debris";
            items.Add(new PrioritizedFlagDisplayItem(
                42,
                new FlagOverlayDisplayItem(
                    FlagDisplayKind.Debris,
                    FlagDisplayCategory.Yellow,
                    label,
                    detail,
                    SimpleTelemetryTone.Warning)));
            return;
        }
        else if (HasFlag(flags, WavingYellowFlag) || HasFlag(flags, RandomWavingFlag))
        {
            detail = "waving";
        }

        if (HasFlag(flags, YellowFlag)
            || HasFlag(flags, WavingYellowFlag)
            || (allowRaceStartPseudoFlags && HasFlag(flags, OneToGreenFlag))
            || HasFlag(flags, DebrisFlag)
            || HasFlag(flags, RandomWavingFlag))
        {
            items.Add(new PrioritizedFlagDisplayItem(
                42,
                new FlagOverlayDisplayItem(
                    FlagDisplayKind.Yellow,
                    FlagDisplayCategory.Yellow,
                    label,
                    detail,
                    SimpleTelemetryTone.Warning)));
        }
    }

    private static void AddFinishFlags(int flags, List<PrioritizedFlagDisplayItem> items)
    {
        AddIf(
            flags,
            CheckeredFlag,
            items,
            order: 60,
            new FlagOverlayDisplayItem(
                FlagDisplayKind.Checkered,
                FlagDisplayCategory.Finish,
                "Checkered",
                null,
                SimpleTelemetryTone.Info));

        if (HasFlag(flags, WhiteFlag)
            || HasFlag(flags, TenToGoFlag)
            || HasFlag(flags, FiveToGoFlag)
            || HasFlag(flags, CrossedFlag))
        {
            var label = HasFlag(flags, WhiteFlag)
                ? "White"
                : HasFlag(flags, FiveToGoFlag)
                    ? "Five to go"
                    : HasFlag(flags, TenToGoFlag)
                        ? "Ten to go"
                        : "Crossed";
            items.Add(new PrioritizedFlagDisplayItem(
                70,
                new FlagOverlayDisplayItem(
                    FlagDisplayKind.White,
                    FlagDisplayCategory.Finish,
                    label,
                    null,
                    SimpleTelemetryTone.Info)));
        }
    }

    private static void AddGreenFlags(
        int flags,
        List<PrioritizedFlagDisplayItem> items,
        bool allowRaceStartPseudoFlags)
    {
        if (!HasFlag(flags, GreenHeldFlag)
            && !(allowRaceStartPseudoFlags
                && (HasFlag(flags, StartReadyFlag)
                    || HasFlag(flags, StartSetFlag)
                    || HasFlag(flags, StartGoFlag))))
        {
            return;
        }

        var label = allowRaceStartPseudoFlags && HasFlag(flags, StartGoFlag)
            ? "Start"
            : allowRaceStartPseudoFlags && HasFlag(flags, StartSetFlag)
                ? "Set"
                : allowRaceStartPseudoFlags && HasFlag(flags, StartReadyFlag)
                    ? "Ready"
                    : "Green";
        items.Add(new PrioritizedFlagDisplayItem(
            80,
            new FlagOverlayDisplayItem(
                FlagDisplayKind.Green,
                FlagDisplayCategory.Green,
                label,
                HasFlag(flags, GreenHeldFlag) ? "held" : null,
                SimpleTelemetryTone.Success)));
    }

    private static void AddIf(
        int flags,
        int bit,
        List<PrioritizedFlagDisplayItem> items,
        int order,
        FlagOverlayDisplayItem item)
    {
        if (HasFlag(flags, bit))
        {
            items.Add(new PrioritizedFlagDisplayItem(order, item));
        }
    }

    private static void AddBlackLabelIf(int flags, int bit, List<string> labels, string label)
    {
        if (HasFlag(flags, bit))
        {
            labels.Add(label);
        }
    }

    private sealed record FlagLabel(int Bit, string Label);

    private sealed record PrioritizedFlagDisplayItem(int Order, FlagOverlayDisplayItem Item);
}

internal sealed record FlagOverlayDisplayViewModel(
    bool IsWaiting,
    string Status,
    SimpleTelemetryTone Tone,
    IReadOnlyList<FlagOverlayDisplayItem> Flags,
    int? RawFlags)
{
    public static FlagOverlayDisplayViewModel Empty { get; } = new(
        IsWaiting: false,
        Status: "none",
        Tone: SimpleTelemetryTone.Info,
        Flags: [],
        RawFlags: null);

    public bool HasDisplayFlags => !IsWaiting && Flags.Count > 0;

    public static FlagOverlayDisplayViewModel Waiting(string status)
    {
        return new FlagOverlayDisplayViewModel(
            IsWaiting: true,
            Status: status,
            Tone: SimpleTelemetryTone.Waiting,
            Flags: [],
            RawFlags: null);
    }
}

internal sealed record FlagOverlayDisplayItem(
    FlagDisplayKind Kind,
    FlagDisplayCategory Category,
    string Label,
    string? Detail,
    SimpleTelemetryTone Tone);

internal enum FlagDisplayKind
{
    Green,
    Blue,
    Yellow,
    Debris,
    Caution,
    Red,
    Black,
    Meatball,
    White,
    Checkered
}

internal enum FlagDisplayCategory
{
    Green,
    Blue,
    Yellow,
    Critical,
    Finish
}
