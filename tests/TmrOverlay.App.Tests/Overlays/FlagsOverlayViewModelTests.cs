using TmrOverlay.App.Overlays.Flags;
using TmrOverlay.App.Overlays.SimpleTelemetry;
using TmrOverlay.Core.History;
using TmrOverlay.Core.Telemetry.Live;
using Xunit;

namespace TmrOverlay.App.Tests.Overlays;

public sealed class FlagsOverlayViewModelTests
{
    private const int YellowFlag = 0x00000008;
    private const int DebrisFlag = 0x00000040;
    private const int OneToGreenFlag = 0x00000200;
    private const int CautionFlag = 0x00004000;
    private const int WavingCautionFlag = 0x00008000;
    private const int BlackFlag = 0x00010000;
    private const int RepairFlag = 0x00100000;
    private const int StartReadyFlag = 0x20000000;
    private const int StartGoFlag = unchecked((int)0x80000000);

    [Theory]
    [InlineData(OneToGreenFlag, FlagDisplayKind.Yellow, "One to green")]
    [InlineData(StartReadyFlag, FlagDisplayKind.Green, "Ready")]
    [InlineData(StartGoFlag, FlagDisplayKind.Green, "Start")]
    public void ForDisplay_RaceStartWithRaceEventMetadata_ShowsRaceStartPseudoFlags(
        int sessionFlags,
        FlagDisplayKind expectedKind,
        string expectedLabel)
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = Snapshot(
            now,
            sessionType: "Warmup",
            eventType: "Race",
            sessionState: 2,
            sessionFlags: sessionFlags);

        var viewModel = FlagsOverlayViewModel.ForDisplay(snapshot, now);

        var flag = Assert.Single(viewModel.Flags);
        Assert.Equal(expectedKind, flag.Kind);
        Assert.Equal(expectedLabel, flag.Label);
    }

    [Theory]
    [InlineData(OneToGreenFlag)]
    [InlineData(StartReadyFlag)]
    [InlineData(StartGoFlag)]
    public void ForDisplay_OfflineTestingSuppressesRaceStartPseudoFlags(int sessionFlags)
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = Snapshot(
            now,
            sessionType: "Offline Testing",
            eventType: "Test",
            sessionState: 4,
            sessionFlags: sessionFlags);

        var viewModel = FlagsOverlayViewModel.ForDisplay(snapshot, now);

        Assert.False(viewModel.HasDisplayFlags);
        Assert.Empty(viewModel.Flags);
        Assert.Equal("none", viewModel.Status);
    }

    [Theory]
    [InlineData("Practice", YellowFlag)]
    [InlineData("Practice", DebrisFlag)]
    [InlineData("Practice", CautionFlag)]
    [InlineData("Practice", WavingCautionFlag)]
    [InlineData("Qualify", YellowFlag)]
    [InlineData("Qualify", DebrisFlag)]
    [InlineData("Qualify", CautionFlag)]
    [InlineData("Qualify", WavingCautionFlag)]
    public void ForDisplay_PracticeAndQualifyingSuppressGlobalYellowFamilyWithoutLocalEvidence(
        string sessionType,
        int sessionFlags)
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = Snapshot(
            now,
            sessionType: sessionType,
            eventType: sessionType,
            sessionState: 4,
            sessionFlags: sessionFlags);

        var viewModel = FlagsOverlayViewModel.ForDisplay(snapshot, now);

        Assert.False(viewModel.HasDisplayFlags);
        Assert.Empty(viewModel.Flags);
        Assert.Equal("none", viewModel.Status);
    }

    [Theory]
    [InlineData("Practice")]
    [InlineData("Qualify")]
    public void ForDisplay_PracticeAndQualifyingKeepGlobalYellowFamilyWithLocalYellowEvidence(string sessionType)
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = Snapshot(
            now,
            sessionType: sessionType,
            eventType: sessionType,
            sessionState: 4,
            sessionFlags: DebrisFlag,
            localDriverFlags: YellowFlag);

        var viewModel = FlagsOverlayViewModel.ForDisplay(snapshot, now);

        var flag = Assert.Single(viewModel.Flags);
        Assert.Equal(FlagDisplayKind.Debris, flag.Kind);
        Assert.Equal("Debris", flag.Label);
    }

    [Theory]
    [InlineData("Practice")]
    [InlineData("Qualify")]
    [InlineData("Offline Testing")]
    public void ForDisplay_NonRaceSessionsKeepLocalActionableCriticalFlags(string sessionType)
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = Snapshot(
            now,
            sessionType: sessionType,
            eventType: sessionType == "Offline Testing" ? "Test" : sessionType,
            sessionState: 4,
            sessionFlags: 0,
            localDriverFlags: RepairFlag | BlackFlag);

        var viewModel = FlagsOverlayViewModel.ForDisplay(snapshot, now);

        Assert.Equal(
            new[] { FlagDisplayKind.Meatball, FlagDisplayKind.Black },
            viewModel.Flags.Select(flag => flag.Kind).ToArray());
        Assert.Equal("Repair + Black", viewModel.Status);
    }

    [Fact]
    public void From_RaceStartWithRaceEventMetadata_UsesCountdownEvenWhenSessionTypeIsStale()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = Snapshot(
            now,
            sessionType: "Warmup",
            eventType: "Race",
            sessionState: 2,
            sessionFlags: OneToGreenFlag,
            sessionTimeRemainSeconds: 75d);

        var viewModel = FlagsOverlayViewModel.From(snapshot, now, "Metric");

        Assert.Contains(viewModel.Rows, row => row.Label == "State" && row.Value == "grid countdown (2)");
        Assert.Contains(viewModel.Rows, row => row.Label == "Countdown" && row.Value == "1:15");
        Assert.DoesNotContain(viewModel.Rows, row => row.Label == "Time left");
    }

    private static LiveTelemetrySnapshot Snapshot(
        DateTimeOffset now,
        string sessionType,
        string eventType,
        int sessionState,
        int sessionFlags,
        int? localDriverFlags = null,
        double? sessionTimeRemainSeconds = null)
    {
        var context = new HistoricalSessionContext
        {
            Car = new HistoricalCarIdentity(),
            Track = new HistoricalTrackIdentity(),
            Session = new HistoricalSessionIdentity
            {
                SessionType = sessionType,
                EventType = eventType
            },
            Conditions = new HistoricalSessionInfoConditions()
        };
        var models = LiveRaceModels.Empty with
        {
            Session = LiveSessionModel.Empty with
            {
                HasData = true,
                Quality = LiveModelQuality.Reliable,
                SessionType = sessionType,
                EventType = eventType,
                SessionState = sessionState,
                SessionFlags = sessionFlags,
                SessionTimeRemainSeconds = sessionTimeRemainSeconds
            },
            DriverDirectory = LiveDriverDirectoryModel.Empty with
            {
                HasData = true,
                Quality = LiveModelQuality.Reliable,
                PlayerCarIdx = 10,
                FocusCarIdx = 10
            },
            IncidentPressure = localDriverFlags is { } flags
                ? IncidentPressureWithPlayerFlags(10, flags)
                : LiveIncidentPressureModel.Empty
        };

        return LiveTelemetrySnapshot.Empty with
        {
            IsConnected = true,
            IsCollecting = true,
            LastUpdatedAtUtc = now,
            Sequence = 1,
            Context = context,
            Combo = HistoricalComboIdentity.From(context),
            Models = models
        };
    }

    private static LiveIncidentPressureModel IncidentPressureWithPlayerFlags(int playerCarIdx, int sessionFlags)
    {
        return LiveIncidentPressureModel.Empty with
        {
            HasData = true,
            Quality = LiveModelQuality.Reliable,
            Evidence = LiveSignalEvidence.Reliable("CarIdxSessionFlags"),
            PlayerCarIdx = playerCarIdx,
            FocusCarIdx = playerCarIdx,
            CurrentFlaggedCarCount = 1,
            Cars =
            [
                new LiveIncidentPressureCar(
                    CarIdx: playerCarIdx,
                    DriverName: null,
                    TeamName: null,
                    CarNumber: null,
                    CarClass: null,
                    IsPlayer: true,
                    IsFocus: true,
                    SessionFlags: sessionFlags,
                    TrackSurface: 3,
                    OnPitRoad: false,
                    HasBlackFlag: HasFlag(sessionFlags, BlackFlag),
                    HasDisqualifyFlag: false,
                    HasRepairFlag: HasFlag(sessionFlags, RepairFlag),
                    HasFurledFlag: false,
                    IsCurrentlyOffTrack: false,
                    ObservedOffTrackTransitions: 0,
                    PressureScore: 0d,
                    PressureLevel: "normal",
                    Evidence: LiveSignalEvidence.Reliable("CarIdxSessionFlags"))
            ]
        };
    }

    private static bool HasFlag(int flags, int mask) => (flags & mask) == mask;
}
