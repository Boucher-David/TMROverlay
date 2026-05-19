using TmrOverlay.App.Overlays.GapToLeader;
using TmrOverlay.Core.History;
using TmrOverlay.Core.Telemetry.Live;
using Xunit;

namespace TmrOverlay.App.Tests.Overlays;

public sealed class GapToLeaderLiveModelAdapterTests
{
    [Fact]
    public void Select_PrefersModelTimingGapOverLegacyGap()
    {
        var focus = TimingRow(
            carIdx: 10,
            isFocus: true,
            classPosition: 2,
            gapSeconds: 12.5d,
            deltaSeconds: 0d,
            gapEvidence: LiveSignalEvidence.Reliable("CarIdxF2Time"));
        var leader = TimingRow(
            carIdx: 2,
            isClassLeader: true,
            classPosition: 1,
            gapSeconds: 0d,
            deltaSeconds: -12.5d,
            gapEvidence: LiveSignalEvidence.Reliable("class-leader-row"));
        var snapshot = SnapshotWithModels(
            legacyGapSeconds: 999d,
            timing: LiveTimingModel.Empty with
            {
                HasData = true,
                Quality = LiveModelQuality.Reliable,
                FocusCarIdx = 10,
                ClassLeaderCarIdx = 2,
                FocusRow = focus,
                ClassRows = [leader, focus],
                ClassLeaderGapEvidence = LiveSignalEvidence.Reliable("CarIdxF2Time")
            },
            progress: LiveRaceProgressModel.Empty with
            {
                HasData = true,
                ReferenceClassPosition = 2,
                ReferenceClassLeaderGapLaps = 0.1d
            });

        var gap = GapToLeaderLiveModelAdapter.Select(snapshot);

        Assert.Equal(12.5d, gap.ClassLeaderGap.Seconds);
        Assert.Equal("CarIdxF2Time", gap.ClassLeaderGap.Source);
        Assert.Contains(gap.ClassCars, car =>
            car.CarIdx == 10
            && car.IsReferenceCar
            && car.GapSecondsToClassLeader == 12.5d);
    }

    [Fact]
    public void Select_CompletesV2TimingFromLegacySampleWhenModelsAreUnavailable()
    {
        var snapshot = SnapshotWithModels(
            legacyGapSeconds: 42.5d,
            timing: LiveTimingModel.Empty,
            progress: LiveRaceProgressModel.Empty) with
        {
            IsConnected = true,
            IsCollecting = true,
            LastUpdatedAtUtc = DateTimeOffset.UtcNow,
            LatestSample = SampleWithReferenceFocus()
        };

        var gap = GapToLeaderLiveModelAdapter.Select(snapshot);

        Assert.True(gap.HasData);
        Assert.Equal(42.5d, gap.ClassLeaderGap.Seconds);
        Assert.Equal("legacy", gap.ClassLeaderGap.Source);
        Assert.Contains(gap.ClassCars, car =>
            car.CarIdx == 10
            && car.IsReferenceCar
            && car.GapSecondsToClassLeader == 42.5d);
    }

    [Fact]
    public void Select_UsesRaceProgressLapGapWhenTimingGapEvidenceIsPartial()
    {
        var partialEvidence = LiveSignalEvidence.Partial("CarIdxF2Time", "leader_f2_time_missing");
        var focus = TimingRow(
            carIdx: 10,
            isFocus: true,
            classPosition: 2,
            gapSeconds: 999d,
            deltaSeconds: 0d,
            gapEvidence: partialEvidence);
        var leader = TimingRow(
            carIdx: 2,
            isClassLeader: true,
            classPosition: 1,
            gapSeconds: 0d,
            deltaSeconds: null,
            gapEvidence: LiveSignalEvidence.Reliable("class-leader-row"));
        var snapshot = SnapshotWithModels(
            legacyGapSeconds: 999d,
            timing: LiveTimingModel.Empty with
            {
                HasData = true,
                Quality = LiveModelQuality.Partial,
                FocusCarIdx = 10,
                ClassLeaderCarIdx = 2,
                FocusRow = focus,
                ClassRows = [leader, focus],
                ClassLeaderGapEvidence = partialEvidence
            },
            progress: LiveRaceProgressModel.Empty with
            {
                HasData = true,
                ReferenceClassPosition = 2,
                ReferenceClassLeaderGapLaps = 0.42d
            });

        var gap = GapToLeaderLiveModelAdapter.Select(snapshot);

        Assert.Null(gap.ClassLeaderGap.Seconds);
        Assert.Equal(0.42d, gap.ClassLeaderGap.Laps);
        Assert.Equal("LiveRaceProgress", gap.ClassLeaderGap.Source);
        var focusCar = Assert.Single(gap.ClassCars.Where(car => car.CarIdx == 10));
        Assert.Null(focusCar.GapSecondsToClassLeader);
        Assert.Equal(0.42d, focusCar.GapLapsToClassLeader);
    }

    [Fact]
    public void Select_WaitsWhenOnlyReferenceAndLeaderRowsExistWithoutGapEvidence()
    {
        var partialEvidence = LiveSignalEvidence.Partial("CarIdxF2Time", "leader_f2_time_missing");
        var focus = TimingRow(
            carIdx: 10,
            isFocus: true,
            classPosition: 2,
            gapSeconds: null,
            deltaSeconds: null,
            gapEvidence: partialEvidence);
        var leader = TimingRow(
            carIdx: 2,
            isClassLeader: true,
            classPosition: 1,
            gapSeconds: 0d,
            deltaSeconds: null,
            gapEvidence: LiveSignalEvidence.Reliable("class-leader-row"));
        var snapshot = SnapshotWithModels(
            legacyGapSeconds: 999d,
            timing: LiveTimingModel.Empty with
            {
                HasData = true,
                Quality = LiveModelQuality.Partial,
                FocusCarIdx = 10,
                ClassLeaderCarIdx = 2,
                FocusRow = focus,
                ClassRows = [leader, focus],
                ClassLeaderGapEvidence = partialEvidence
            },
            progress: LiveRaceProgressModel.Empty with
            {
                HasData = true,
                ReferenceClassPosition = 2
            });

        var gap = GapToLeaderLiveModelAdapter.Select(snapshot);

        Assert.False(gap.HasData);
        Assert.False(gap.ClassLeaderGap.HasData);
        Assert.Null(GapToLeaderLiveModelAdapter.SelectFocusedTrendPointSeconds(snapshot, gap));
        Assert.Contains(gap.ClassCars, car => car.CarIdx == 10 && car.IsReferenceCar);
    }

    [Fact]
    public void SelectFocusedTrendPointSeconds_ConvertsLivePlacementLapGapWithLapReference()
    {
        var partialEvidence = LiveSignalEvidence.Partial("CarIdxF2Time", "leader_f2_time_missing");
        var focus = TimingRow(
            carIdx: 10,
            isFocus: true,
            classPosition: 2,
            gapSeconds: 999d,
            deltaSeconds: 0d,
            gapEvidence: partialEvidence);
        var leader = TimingRow(
            carIdx: 2,
            isClassLeader: true,
            classPosition: 1,
            gapSeconds: 0d,
            deltaSeconds: null,
            gapEvidence: LiveSignalEvidence.Reliable("class-leader-row"));
        var snapshot = SnapshotWithModels(
            legacyGapSeconds: 999d,
            timing: LiveTimingModel.Empty with
            {
                HasData = true,
                Quality = LiveModelQuality.Partial,
                FocusCarIdx = 10,
                ClassLeaderCarIdx = 2,
                FocusRow = focus,
                ClassRows = [leader, focus],
                ClassLeaderGapEvidence = partialEvidence
            },
            progress: LiveRaceProgressModel.Empty with
            {
                HasData = true,
                ReferenceClassPosition = 2,
                ReferenceClassLeaderGapLaps = 0.42d,
                StrategyLapTimeSeconds = 91.5d
            });
        var gap = GapToLeaderLiveModelAdapter.Select(snapshot);

        var trendSeconds = GapToLeaderLiveModelAdapter.SelectFocusedTrendPointSeconds(snapshot, gap);

        Assert.Equal(38.43d, trendSeconds!.Value, precision: 6);
    }

    private static LiveTelemetrySnapshot SnapshotWithModels(
        double legacyGapSeconds,
        LiveTimingModel timing,
        LiveRaceProgressModel progress)
    {
        var context = RaceContext();
        return LiveTelemetrySnapshot.Empty with
        {
            Context = context,
            Combo = HistoricalComboIdentity.From(context),
            LeaderGap = new LiveLeaderGapSnapshot(
                HasData: true,
                ReferenceOverallPosition: 2,
                ReferenceClassPosition: 2,
                OverallLeaderCarIdx: 1,
                ClassLeaderCarIdx: 2,
                OverallLeaderGap: new LiveGapValue(true, false, legacyGapSeconds, null, "legacy"),
                ClassLeaderGap: new LiveGapValue(true, false, legacyGapSeconds, null, "legacy"),
                ClassCars:
                [
                    new LiveClassGapCar(2, false, true, 1, 0d, 0d, -legacyGapSeconds),
                    new LiveClassGapCar(10, true, false, 2, legacyGapSeconds, null, 0d)
                ]),
            Models = LiveRaceModels.Empty with
            {
                Timing = timing,
                RaceProgress = progress
            }
        };
    }

    private static HistoricalSessionContext RaceContext()
    {
        return new HistoricalSessionContext
        {
            Car = new HistoricalCarIdentity(),
            Track = new HistoricalTrackIdentity(),
            Session = new HistoricalSessionIdentity
            {
                SessionType = "Race",
                EventType = "Race"
            },
            Conditions = new HistoricalSessionInfoConditions()
        };
    }

    private static HistoricalTelemetrySample SampleWithReferenceFocus()
    {
        return new HistoricalTelemetrySample(
            CapturedAtUtc: DateTimeOffset.UtcNow,
            SessionTime: 10d,
            SessionTick: 1,
            SessionInfoUpdate: 1,
            IsOnTrack: true,
            IsInGarage: false,
            OnPitRoad: false,
            PitstopActive: false,
            PlayerCarInPitStall: false,
            FuelLevelLiters: 25d,
            FuelLevelPercent: 0.25d,
            FuelUsePerHourKg: 0d,
            SpeedMetersPerSecond: 42d,
            Lap: 4,
            LapCompleted: 4,
            LapDistPct: 0.5d,
            LapLastLapTimeSeconds: null,
            LapBestLapTimeSeconds: null,
            AirTempC: 20d,
            TrackTempCrewC: 24d,
            TrackWetness: 1,
            WeatherDeclaredWet: false,
            PlayerTireCompound: 0,
            PlayerCarIdx: 10,
            FocusCarIdx: 10,
            FocusLapCompleted: 4,
            FocusLapDistPct: 0.5d,
            FocusClassPosition: 2,
            TeamLapCompleted: 4,
            TeamLapDistPct: 0.5d,
            TeamClassPosition: 2,
            ClassLeaderCarIdx: 2,
            FocusClassLeaderCarIdx: 2);
    }

    private static LiveTimingRow TimingRow(
        int carIdx,
        bool isFocus = false,
        bool isClassLeader = false,
        int? classPosition = null,
        double? gapSeconds = null,
        double? deltaSeconds = null,
        LiveSignalEvidence? gapEvidence = null)
    {
        return new LiveTimingRow(
            CarIdx: carIdx,
            Quality: LiveModelQuality.Reliable,
            Source: "test",
            IsPlayer: isFocus,
            IsFocus: isFocus,
            IsOverallLeader: false,
            IsClassLeader: isClassLeader,
            HasTiming: true,
            HasSpatialProgress: true,
            CanUseForRadarPlacement: false,
            TimingEvidence: LiveSignalEvidence.Reliable("test"),
            SpatialEvidence: LiveSignalEvidence.Reliable("test"),
            RadarPlacementEvidence: LiveSignalEvidence.Unavailable("test", "not_applicable"),
            GapEvidence: gapEvidence ?? LiveSignalEvidence.Unavailable("class-gap", "gap_signals_missing"),
            DriverName: null,
            TeamName: null,
            CarNumber: carIdx.ToString(System.Globalization.CultureInfo.InvariantCulture),
            CarClassName: "GT3",
            CarClassColorHex: null,
            OverallPosition: null,
            ClassPosition: classPosition,
            CarClass: 1,
            LapCompleted: 4,
            LapDistPct: 0.5d,
            ProgressLaps: 4.5d,
            F2TimeSeconds: null,
            EstimatedTimeSeconds: null,
            LastLapTimeSeconds: null,
            BestLapTimeSeconds: null,
            GapSecondsToClassLeader: gapSeconds,
            GapLapsToClassLeader: null,
            IntervalSecondsToPreviousClassRow: null,
            IntervalLapsToPreviousClassRow: null,
            DeltaSecondsToFocus: deltaSeconds,
            TrackSurface: null,
            OnPitRoad: false);
    }
}
