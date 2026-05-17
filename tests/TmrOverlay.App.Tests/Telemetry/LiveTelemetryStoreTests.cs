using TmrOverlay.App.Overlays.Standings;
using TmrOverlay.Core.History;
using TmrOverlay.Core.Telemetry.Live;
using Xunit;

namespace TmrOverlay.App.Tests.Telemetry;

public sealed class LiveTelemetryStoreTests
{
    [Fact]
    public void LiveFuelSnapshot_FromKeepsFuelUseDiagnosticUntilMeasuredLap()
    {
        var context = new HistoricalSessionContext
        {
            Car = new HistoricalCarIdentity
            {
                DriverCarFuelKgPerLiter = 0.75d,
                DriverCarEstLapTimeSeconds = 90d
            },
            Track = new HistoricalTrackIdentity(),
            Session = new HistoricalSessionIdentity(),
            Conditions = new HistoricalSessionInfoConditions()
        };
        var sample = CreateSample(
            fuelLevelLiters: 50d,
            fuelLevelPercent: 0.5d,
            fuelUsePerHourKg: 75d);

        var fuel = LiveFuelSnapshot.From(context, sample);

        Assert.True(fuel.HasValidFuel);
        Assert.Equal("local-driver-scalar", fuel.Source);
        Assert.Equal(100d, fuel.FuelUsePerHourLiters);
        Assert.Null(fuel.FuelPerLapLiters);
        Assert.Equal(90d, fuel.LapTimeSeconds);
        Assert.Equal("player-last-lap", fuel.LapTimeSource);
        Assert.Equal(30d, fuel.EstimatedMinutesRemaining);
        Assert.Null(fuel.EstimatedLapsRemaining);
        Assert.Equal("level-only", fuel.Confidence);
    }

    [Fact]
    public void RecordFrame_PublishesMeasuredFuelBurnAfterCompletedGreenLap()
    {
        var store = new LiveTelemetryStore();
        var startedAtUtc = DateTimeOffset.Parse("2026-05-17T12:00:00Z");

        store.RecordFrame(CreateSample(
            capturedAtUtc: startedAtUtc,
            sessionTime: 0d,
            sessionState: 4,
            playerCarIdx: 10,
            teamLapCompleted: 0,
            teamLapDistPct: 0.25d,
            fuelLevelLiters: 50d));
        store.RecordFrame(CreateSample(
            capturedAtUtc: startedAtUtc.AddSeconds(90),
            sessionTime: 90d,
            sessionState: 4,
            playerCarIdx: 10,
            teamLapCompleted: 1,
            teamLapDistPct: 0.25d,
            fuelLevelLiters: 47.5d));

        var snapshot = store.Snapshot();

        Assert.Equal(2.5d, snapshot.Fuel.FuelPerLapLiters!.Value, precision: 3);
        Assert.Equal(2.5d, snapshot.Fuel.MeasuredFuelPerLapMinimumLiters!.Value, precision: 3);
        Assert.Equal(2.5d, snapshot.Fuel.MeasuredFuelPerLapAverageLiters!.Value, precision: 3);
        Assert.Equal(2.5d, snapshot.Fuel.MeasuredFuelPerLapMaximumLiters!.Value, precision: 3);
        Assert.Equal(1, snapshot.Fuel.MeasuredFuelPerLapSampleCount);
        Assert.Equal(19d, snapshot.Fuel.EstimatedLapsRemaining!.Value, precision: 3);
        Assert.Equal("measured-green-lap", snapshot.Fuel.Confidence);
        Assert.True(snapshot.Models.FuelPit.MeasuredBurnEvidence.IsUsable);
        Assert.Equal("rolling-local-fuel-delta", snapshot.Models.FuelPit.MeasuredBurnEvidence.Source);
    }

    [Fact]
    public void RecordFrame_DoesNotCountPreGreenToGreenFuelDropAsLapBurn()
    {
        var store = new LiveTelemetryStore();
        var startedAtUtc = DateTimeOffset.Parse("2026-05-17T12:00:00Z");

        store.RecordFrame(CreateSample(
            capturedAtUtc: startedAtUtc,
            sessionTime: 0d,
            sessionState: 3,
            playerCarIdx: 10,
            teamLapCompleted: 0,
            teamLapDistPct: 0.25d,
            fuelLevelLiters: 50d));
        store.RecordFrame(CreateSample(
            capturedAtUtc: startedAtUtc.AddSeconds(90),
            sessionTime: 90d,
            sessionState: 4,
            playerCarIdx: 10,
            teamLapCompleted: 1,
            teamLapDistPct: 0.25d,
            fuelLevelLiters: 47.5d));

        var snapshot = store.Snapshot();

        Assert.Null(snapshot.Fuel.FuelPerLapLiters);
        Assert.Null(snapshot.Fuel.EstimatedLapsRemaining);
        Assert.Equal("level-only", snapshot.Fuel.Confidence);
        Assert.False(snapshot.Models.FuelPit.MeasuredBurnEvidence.IsUsable);
        Assert.Equal("requires_completed_green_lap_delta", snapshot.Models.FuelPit.MeasuredBurnEvidence.MissingReason);
    }

    [Fact]
    public void RecordFrame_KeepsMeasuredBurnWhilePitFuelLevelChanges()
    {
        var store = new LiveTelemetryStore();
        var startedAtUtc = DateTimeOffset.Parse("2026-05-17T12:00:00Z");

        store.RecordFrame(CreateSample(
            capturedAtUtc: startedAtUtc,
            sessionTime: 0d,
            sessionState: 4,
            playerCarIdx: 10,
            teamLapCompleted: 0,
            teamLapDistPct: 0.25d,
            fuelLevelLiters: 50d));
        store.RecordFrame(CreateSample(
            capturedAtUtc: startedAtUtc.AddSeconds(90),
            sessionTime: 90d,
            sessionState: 4,
            playerCarIdx: 10,
            teamLapCompleted: 1,
            teamLapDistPct: 0.25d,
            fuelLevelLiters: 47.5d));
        store.RecordFrame(CreateSample(
            capturedAtUtc: startedAtUtc.AddSeconds(120),
            sessionTime: 120d,
            sessionState: 4,
            playerCarIdx: 10,
            teamLapCompleted: 1,
            teamLapDistPct: 0.30d,
            fuelLevelLiters: 80d,
            onPitRoad: true,
            teamOnPitRoad: true,
            playerTrackSurface: 1));

        var snapshot = store.Snapshot();

        Assert.Equal(80d, snapshot.Fuel.FuelLevelLiters);
        Assert.Equal(2.5d, snapshot.Fuel.FuelPerLapLiters!.Value, precision: 3);
        Assert.Equal(32d, snapshot.Fuel.EstimatedLapsRemaining!.Value, precision: 3);
    }

    [Fact]
    public void RecordFrame_PublishesLatestSampleAndFuel()
    {
        var store = new LiveTelemetryStore();
        var startedAtUtc = DateTimeOffset.UtcNow.AddSeconds(-5);
        var capturedAtUtc = DateTimeOffset.UtcNow;

        store.MarkConnected();
        store.MarkCollectionStarted("session-test", startedAtUtc);
        store.RecordFrame(CreateSample(capturedAtUtc: capturedAtUtc));

        var snapshot = store.Snapshot();

        Assert.True(snapshot.IsConnected);
        Assert.True(snapshot.IsCollecting);
        Assert.Equal("session-test", snapshot.SourceId);
        Assert.Equal(startedAtUtc, snapshot.StartedAtUtc);
        Assert.Equal(capturedAtUtc, snapshot.LastUpdatedAtUtc);
        Assert.NotNull(snapshot.LatestSample);
        Assert.True(snapshot.Fuel.HasValidFuel);
    }

    [Fact]
    public void RecordFrame_PublishesGarageVisibleRaceEvent()
    {
        var store = new LiveTelemetryStore();

        store.RecordFrame(CreateSample(isGarageVisible: true));

        var snapshot = store.Snapshot();

        Assert.True(snapshot.Models.RaceEvents.IsGarageVisible);
    }

    [Fact]
    public void RecordFrame_PublishesTrackMapSectorHighlightsInModelV2()
    {
        var store = new LiveTelemetryStore();
        ApplyThreeSectorSession(store);
        var startedAtUtc = DateTimeOffset.Parse("2026-05-05T12:00:00Z");

        var progress = new[] { 0.01d, 0.10d, 0.20d, 0.30d, 0.40d, 0.51d };
        for (var index = 0; index < progress.Length; index++)
        {
            store.RecordFrame(CreateSample(
                capturedAtUtc: startedAtUtc.AddSeconds(index),
                sessionTime: index,
                playerCarIdx: 10,
                teamLapCompleted: 0,
                teamLapDistPct: progress[index]));
        }

        var trackMap = store.Snapshot().Models.TrackMap;

        Assert.True(trackMap.HasSectors);
        Assert.True(trackMap.HasLiveTiming);
        Assert.Contains(trackMap.Sectors, sector =>
            sector.SectorNum == 0
            && sector.Highlight == LiveTrackSectorHighlights.PersonalBest
            && sector.BoundaryHighlight == LiveTrackSectorHighlights.PersonalBest);
    }

    [Fact]
    public void RecordFrame_PublishesRaceProgressModel()
    {
        var store = new LiveTelemetryStore();

        store.RecordFrame(CreateSample(
            teamLapCompleted: 4,
            teamLapDistPct: 0.2d,
            teamPosition: 2,
            teamClassPosition: 1,
            leaderLapCompleted: 4,
            leaderLapDistPct: 0.5d,
            sessionTimeRemain: 250d,
            teamLastLapTimeSeconds: 100d));

        var progress = store.Snapshot().Models.RaceProgress;

        Assert.True(progress.HasData);
        Assert.Equal(4.2d, progress.StrategyCarProgressLaps!.Value, precision: 3);
        Assert.Equal(0.3d, progress.StrategyOverallLeaderGapLaps!.Value, precision: 3);
        Assert.Equal(2.8d, progress.RaceLapsRemaining!.Value, precision: 3);
        Assert.Equal("timed race by team last lap", progress.RaceLapsRemainingSource);
        Assert.Equal(2, progress.StrategyOverallPosition);
        Assert.Equal(1, progress.StrategyClassPosition);
    }

    [Fact]
    public void RecordFrame_DoesNotTreatRacePreGreenCountdownAsRaceTimeRemaining()
    {
        var store = new LiveTelemetryStore();
        store.ApplySessionInfo("""
WeekendInfo:
 EventType: Race
SessionInfo:
 CurrentSessionNum: 2
 Sessions:
 - SessionNum: 2
   SessionType: Race
   SessionName: RACE
   SessionTime: 14400 sec
   SessionLaps: unlimited
DriverInfo:
 DriverCarIdx: 10
""");

        store.RecordFrame(CreateSample(
            playerCarIdx: 10,
            teamLapCompleted: 0,
            teamLapDistPct: 0.0d,
            sessionState: 1,
            sessionTimeRemain: 109d,
            teamLastLapTimeSeconds: 100d));

        var progress = store.Snapshot().Models.RaceProgress;

        Assert.Equal(144d, progress.RaceLapsRemaining);
        Assert.Equal("scheduled time", progress.RaceLapsRemainingSource);
    }

    [Fact]
    public void RecordFrame_PublishesRollingRaceProjectionAfterCleanLeaderWindow()
    {
        var store = new LiveTelemetryStore();
        var startedAtUtc = DateTimeOffset.Parse("2026-05-07T12:00:00Z");
        var leaderFrames = new[]
        {
            (SessionTime: 0d, LapCompleted: 0, LapDistPct: 0.90d, LastLap: (double?)null),
            (SessionTime: 5d, LapCompleted: 1, LapDistPct: 0.02d, LastLap: (double?)91d),
            (SessionTime: 90d, LapCompleted: 1, LapDistPct: 0.90d, LastLap: (double?)91d),
            (SessionTime: 96d, LapCompleted: 2, LapDistPct: 0.02d, LastLap: (double?)92d),
            (SessionTime: 181d, LapCompleted: 2, LapDistPct: 0.90d, LastLap: (double?)92d),
            (SessionTime: 188d, LapCompleted: 3, LapDistPct: 0.02d, LastLap: (double?)93d)
        };

        foreach (var frame in leaderFrames)
        {
            store.RecordFrame(CreateSample(
                capturedAtUtc: startedAtUtc.AddSeconds(frame.SessionTime),
                sessionTime: frame.SessionTime,
                sessionState: 4,
                playerCarIdx: 10,
                teamLapCompleted: 2,
                teamLapDistPct: 0.50d,
                leaderCarIdx: 11,
                leaderLapCompleted: frame.LapCompleted,
                leaderLapDistPct: frame.LapDistPct,
                leaderLastLapTimeSeconds: frame.LastLap,
                sessionTimeRemain: 360d));
        }

        var models = store.Snapshot().Models;
        var projection = models.RaceProjection;

        Assert.True(projection.HasData);
        Assert.Equal(92d, projection.OverallLeaderPaceSeconds!.Value, precision: 3);
        Assert.Contains("rolling overall leader pace", projection.OverallLeaderPaceSource);
        Assert.Equal(4.5d, projection.EstimatedTeamLapsRemaining!.Value, precision: 3);
        Assert.Equal(projection.OverallLeaderPaceSeconds, models.RaceProgress.RacePaceSeconds);
        Assert.Equal(projection.EstimatedTeamLapsRemaining, models.RaceProgress.RaceLapsRemaining);
    }

    [Fact]
    public void RecordFrame_ClearsFullLapTrackMapHighlightWhenNextFirstSectorCompletes()
    {
        var store = new LiveTelemetryStore();
        ApplyThreeSectorSession(store);
        var startedAtUtc = DateTimeOffset.Parse("2026-05-05T12:00:00Z");

        var firstLapProgress = new[]
        {
            (SessionTime: 0d, LapCompleted: 0, LapDistPct: 0.01d),
            (SessionTime: 2d, LapCompleted: 0, LapDistPct: 0.10d),
            (SessionTime: 4d, LapCompleted: 0, LapDistPct: 0.20d),
            (SessionTime: 6d, LapCompleted: 0, LapDistPct: 0.30d),
            (SessionTime: 8d, LapCompleted: 0, LapDistPct: 0.40d),
            (SessionTime: 10d, LapCompleted: 0, LapDistPct: 0.51d),
            (SessionTime: 12d, LapCompleted: 0, LapDistPct: 0.62d),
            (SessionTime: 14d, LapCompleted: 0, LapDistPct: 0.72d),
            (SessionTime: 16d, LapCompleted: 0, LapDistPct: 0.76d),
            (SessionTime: 18d, LapCompleted: 0, LapDistPct: 0.86d),
            (SessionTime: 20d, LapCompleted: 0, LapDistPct: 0.96d),
            (SessionTime: 22d, LapCompleted: 1, LapDistPct: 0.01d)
        };
        foreach (var frame in firstLapProgress)
        {
            store.RecordFrame(CreateSample(
                capturedAtUtc: startedAtUtc.AddSeconds(frame.SessionTime),
                sessionTime: frame.SessionTime,
                playerCarIdx: 10,
                teamLapCompleted: frame.LapCompleted,
                teamLapDistPct: frame.LapDistPct,
                lapDeltaToSessionBestLapSeconds: frame.LapCompleted == 1 ? 0d : null,
                lapDeltaToSessionBestLapOk: frame.LapCompleted == 1 ? true : null));
        }

        Assert.All(
            store.Snapshot().Models.TrackMap.Sectors,
            sector => Assert.Equal(LiveTrackSectorHighlights.BestLap, sector.Highlight));
        Assert.DoesNotContain(
            store.Snapshot().Models.TrackMap.Sectors,
            sector => sector.BoundaryHighlight == LiveTrackSectorHighlights.BestLap);

        var nextLapProgress = new[]
        {
            (SessionTime: 23d, LapDistPct: 0.10d),
            (SessionTime: 24d, LapDistPct: 0.20d),
            (SessionTime: 25d, LapDistPct: 0.30d),
            (SessionTime: 26d, LapDistPct: 0.40d),
            (SessionTime: 27d, LapDistPct: 0.51d)
        };
        foreach (var frame in nextLapProgress)
        {
            store.RecordFrame(CreateSample(
                capturedAtUtc: startedAtUtc.AddSeconds(frame.SessionTime),
                sessionTime: frame.SessionTime,
                playerCarIdx: 10,
                teamLapCompleted: 1,
                teamLapDistPct: frame.LapDistPct));
        }

        var sectors = store.Snapshot().Models.TrackMap.Sectors;
        Assert.Contains(sectors, sector =>
            sector.SectorNum == 0
            && sector.Highlight == LiveTrackSectorHighlights.PersonalBest);
        Assert.DoesNotContain(sectors, sector =>
            sector.SectorNum != 0
            && sector.Highlight != LiveTrackSectorHighlights.None);
    }

    [Fact]
    public void RecordFrame_PreservesFreshGreenSectorWhenClearingGreenFullLapTrackMapHighlight()
    {
        var store = new LiveTelemetryStore();
        ApplyThreeSectorSession(store);
        var startedAtUtc = DateTimeOffset.Parse("2026-05-05T12:00:00Z");

        var firstLapProgress = new[]
        {
            (SessionTime: 0d, LapCompleted: 0, LapDistPct: 0.01d),
            (SessionTime: 2d, LapCompleted: 0, LapDistPct: 0.10d),
            (SessionTime: 4d, LapCompleted: 0, LapDistPct: 0.20d),
            (SessionTime: 6d, LapCompleted: 0, LapDistPct: 0.30d),
            (SessionTime: 8d, LapCompleted: 0, LapDistPct: 0.40d),
            (SessionTime: 10d, LapCompleted: 0, LapDistPct: 0.51d),
            (SessionTime: 12d, LapCompleted: 0, LapDistPct: 0.62d),
            (SessionTime: 14d, LapCompleted: 0, LapDistPct: 0.72d),
            (SessionTime: 16d, LapCompleted: 0, LapDistPct: 0.76d),
            (SessionTime: 18d, LapCompleted: 0, LapDistPct: 0.86d),
            (SessionTime: 20d, LapCompleted: 0, LapDistPct: 0.96d),
            (SessionTime: 22d, LapCompleted: 1, LapDistPct: 0.01d)
        };
        foreach (var frame in firstLapProgress)
        {
            store.RecordFrame(CreateSample(
                capturedAtUtc: startedAtUtc.AddSeconds(frame.SessionTime),
                sessionTime: frame.SessionTime,
                playerCarIdx: 10,
                teamLapCompleted: frame.LapCompleted,
                teamLapDistPct: frame.LapDistPct,
                lapDeltaToSessionBestLapSeconds: frame.LapCompleted == 1 ? 1.5d : null,
                lapDeltaToSessionBestLapOk: frame.LapCompleted == 1 ? true : null));
        }

        Assert.All(
            store.Snapshot().Models.TrackMap.Sectors,
            sector => Assert.Equal(LiveTrackSectorHighlights.PersonalBest, sector.Highlight));

        var nextLapProgress = new[]
        {
            (SessionTime: 23d, LapDistPct: 0.10d),
            (SessionTime: 24d, LapDistPct: 0.20d),
            (SessionTime: 25d, LapDistPct: 0.30d),
            (SessionTime: 26d, LapDistPct: 0.40d),
            (SessionTime: 27d, LapDistPct: 0.51d)
        };
        foreach (var frame in nextLapProgress)
        {
            store.RecordFrame(CreateSample(
                capturedAtUtc: startedAtUtc.AddSeconds(frame.SessionTime),
                sessionTime: frame.SessionTime,
                playerCarIdx: 10,
                teamLapCompleted: 1,
                teamLapDistPct: frame.LapDistPct));
        }

        var sectors = store.Snapshot().Models.TrackMap.Sectors;
        Assert.Contains(sectors, sector =>
            sector.SectorNum == 0
            && sector.Highlight == LiveTrackSectorHighlights.PersonalBest);
        Assert.DoesNotContain(sectors, sector =>
            sector.SectorNum != 0
            && sector.Highlight != LiveTrackSectorHighlights.None);
    }

    [Fact]
    public void RecordFrame_UsesLapDistanceForTrackMapSectorsWhenLapCountersAreInvalid()
    {
        var store = new LiveTelemetryStore();
        ApplyThreeSectorSession(store);
        var startedAtUtc = DateTimeOffset.Parse("2026-05-05T12:00:00Z");

        var progress = new[] { 0.99d, 0.01d, 0.10d, 0.20d, 0.30d, 0.40d, 0.51d };
        for (var index = 0; index < progress.Length; index++)
        {
            store.RecordFrame(CreateSample(
                capturedAtUtc: startedAtUtc.AddSeconds(index),
                sessionTime: index,
                lapCompleted: -1,
                playerCarIdx: 10,
                teamLapCompleted: -1,
                teamLapDistPct: progress[index]));
        }

        var trackMap = store.Snapshot().Models.TrackMap;

        Assert.True(trackMap.HasSectors);
        Assert.True(trackMap.HasLiveTiming);
        Assert.Contains(trackMap.Sectors, sector =>
            sector.SectorNum == 0
            && sector.Highlight == LiveTrackSectorHighlights.PersonalBest);
    }

    [Fact]
    public void RecordFrame_SeedsSectorTimingAfterActiveResetStyleProgressJump()
    {
        var store = new LiveTelemetryStore();
        ApplyThreeSectorSession(store);
        var startedAtUtc = DateTimeOffset.Parse("2026-05-05T12:00:00Z");
        var progress = new[] { 0.10d, 0.50d, 0.56d, 0.62d, 0.68d, 0.74d, 0.76d };

        for (var index = 0; index < progress.Length; index++)
        {
            store.RecordFrame(CreateSample(
                capturedAtUtc: startedAtUtc.AddSeconds(index),
                sessionTime: index,
                lapCompleted: -1,
                playerCarIdx: 10,
                teamLapCompleted: -1,
                teamLapDistPct: progress[index]));
        }

        var sectors = store.Snapshot().Models.TrackMap.Sectors;

        Assert.Contains(sectors, sector =>
            sector.SectorNum == 1
            && sector.Highlight == LiveTrackSectorHighlights.PersonalBest);
        Assert.Contains(sectors, sector =>
            sector.SectorNum == 0
            && sector.Highlight == LiveTrackSectorHighlights.None);
    }

    [Fact]
    public void RecordFrame_CarriesSessionInfoClassColorToProximityCars()
    {
        var store = new LiveTelemetryStore();
        store.ApplySessionInfo("""
DriverInfo:
 DriverCarIdx: 10
 Drivers:
 - CarIdx: 10
   CarClassColor: 0xffda59
 - CarIdx: 51
   CarClassColor: 0x33ceff
""");

        store.RecordFrame(CreateSample(
            playerCarIdx: 10,
            teamLapDistPct: 0.50d,
            teamEstimatedTimeSeconds: 50d,
            nearbyCars:
            [
                new HistoricalCarProximity(
                    CarIdx: 51,
                    LapCompleted: 2,
                    LapDistPct: 0.51d,
                    F2TimeSeconds: null,
                    EstimatedTimeSeconds: 51d,
                    Position: 3,
                    ClassPosition: 1,
                    CarClass: 4099,
                    TrackSurface: 3,
                    OnPitRoad: false)
            ]));

        var snapshot = store.Snapshot();
        var car = Assert.Single(snapshot.Proximity.NearbyCars);
        Assert.Equal("#33CEFF", car.CarClassColorHex);
        Assert.Equal("#FFDA59", snapshot.Models.Spatial.ReferenceCarClassColorHex);
    }

    [Fact]
    public void ApplySessionInfo_CarriesSplitTimeSectorsIntoContext()
    {
        var store = new LiveTelemetryStore();
        store.ApplySessionInfo("""
WeekendInfo:
 TrackName: nurburgringcombined
DriverInfo:
 DriverCarIdx: 10
 Drivers:
 - CarIdx: 10
   UserName: Player
SplitTimeInfo:
 Sectors:
 - SectorNum: 0
   SectorStartPct: 0.000000
 - SectorNum: 1
   SectorStartPct: 0.059239
 - SectorNum: 2
   SectorStartPct: 0.114229
""");

        var sectors = store.Snapshot().Context.Sectors;

        Assert.Collection(
            sectors,
            sector =>
            {
                Assert.Equal(0, sector.SectorNum);
                Assert.Equal(0d, sector.SectorStartPct);
            },
            sector =>
            {
                Assert.Equal(1, sector.SectorNum);
                Assert.Equal(0.059239d, sector.SectorStartPct);
            },
            sector =>
            {
                Assert.Equal(2, sector.SectorNum);
                Assert.Equal(0.114229d, sector.SectorStartPct);
            });
    }

    private static void ApplyThreeSectorSession(LiveTelemetryStore store)
    {
        store.ApplySessionInfo("""
WeekendInfo:
 TrackName: synthetic
DriverInfo:
 DriverCarIdx: 10
 Drivers:
 - CarIdx: 10
   UserName: Player
SplitTimeInfo:
 Sectors:
 - SectorNum: 0
   SectorStartPct: 0.000000
 - SectorNum: 1
   SectorStartPct: 0.500000
 - SectorNum: 2
   SectorStartPct: 0.750000
""");
    }

    private static void ApplyMulticlassClassOrderSession(LiveTelemetryStore store)
    {
        store.ApplySessionInfo("""
DriverInfo:
 DriverCarIdx: 10
 Drivers:
 - CarIdx: 10
   UserName: GT3 Driver
   CarClassID: 4098
   CarClassShortName: GT3
   CarClassRelSpeed: 50
   CarClassEstLapTime: 90.0
 - CarIdx: 51
   UserName: GTP Driver
   CarClassID: 4100
   CarClassShortName: GTP
   CarClassRelSpeed: 90
   CarClassEstLapTime: 72.0
 - CarIdx: 52
   UserName: LMP2 Driver
   CarClassID: 4099
   CarClassShortName: LMP2
   CarClassRelSpeed: 75
   CarClassEstLapTime: 80.0
 - CarIdx: 53
   UserName: Slower Driver
   CarClassID: 4097
   CarClassShortName: PCC
   CarClassRelSpeed: 40
   CarClassEstLapTime: 96.0
""");
    }

    [Fact]
    public void RecordFrame_SurfacesMulticlassApproachOutsideCloseRadarRange()
    {
        var store = new LiveTelemetryStore();
        ApplyMulticlassClassOrderSession(store);

        store.RecordFrame(CreateSample(
            playerCarIdx: 10,
            teamLapDistPct: 0.50d,
            teamEstimatedTimeSeconds: 50d,
            teamCarClass: 4098,
            nearbyCars:
            [
                new HistoricalCarProximity(
                    CarIdx: 51,
                    LapCompleted: 2,
                    LapDistPct: 0.455d,
                    F2TimeSeconds: 0d,
                    EstimatedTimeSeconds: 45.5d,
                    Position: 3,
                    ClassPosition: 1,
                    CarClass: 4099,
                    TrackSurface: 3,
                    OnPitRoad: false)
            ]));

        var snapshot = store.Snapshot();

        var approach = Assert.Single(snapshot.Proximity.MulticlassApproaches);
        Assert.Equal(51, approach.CarIdx);
        Assert.Equal(4099, approach.CarClass);
        Assert.Equal(-4.5d, approach.RelativeSeconds!.Value, precision: 6);
        Assert.Equal(approach, snapshot.Proximity.StrongestMulticlassApproach);
        Assert.Equal(approach, Assert.Single(snapshot.Models.Spatial.MulticlassApproaches));
        Assert.Equal(approach, snapshot.Models.Spatial.StrongestMulticlassApproach);
    }

    [Fact]
    public void RecordFrame_SurfacesNearestFasterClassApproachForCountdown()
    {
        var store = new LiveTelemetryStore();
        ApplyMulticlassClassOrderSession(store);

        store.RecordFrame(CreateSample(
            playerCarIdx: 10,
            teamLapDistPct: 0.50d,
            teamEstimatedTimeSeconds: 50d,
            teamCarClass: 4098,
            nearbyCars:
            [
                new HistoricalCarProximity(
                    CarIdx: 51,
                    LapCompleted: 2,
                    LapDistPct: 0.455d,
                    F2TimeSeconds: 0d,
                    EstimatedTimeSeconds: 45.5d,
                    Position: 2,
                    ClassPosition: 1,
                    CarClass: 4100,
                    TrackSurface: 3,
                    OnPitRoad: false),
                new HistoricalCarProximity(
                    CarIdx: 52,
                    LapCompleted: 2,
                    LapDistPct: 0.465d,
                    F2TimeSeconds: 0d,
                    EstimatedTimeSeconds: 46.5d,
                    Position: 3,
                    ClassPosition: 1,
                    CarClass: 4099,
                    TrackSurface: 3,
                    OnPitRoad: false)
            ]));

        var snapshot = store.Snapshot();

        Assert.Equal(new[] { 52, 51 }, snapshot.Proximity.MulticlassApproaches.Select(approach => approach.CarIdx).ToArray());
        Assert.Equal(52, snapshot.Proximity.StrongestMulticlassApproach?.CarIdx);
        Assert.Equal(-3.5d, snapshot.Proximity.StrongestMulticlassApproach!.RelativeSeconds!.Value, precision: 6);
    }

    [Fact]
    public void RecordFrame_DoesNotSurfaceSlowerClassAsFasterClassApproach()
    {
        var store = new LiveTelemetryStore();
        ApplyMulticlassClassOrderSession(store);

        store.RecordFrame(CreateSample(
            playerCarIdx: 10,
            teamLapDistPct: 0.50d,
            teamEstimatedTimeSeconds: 50d,
            teamCarClass: 4099,
            nearbyCars:
            [
                new HistoricalCarProximity(
                    CarIdx: 51,
                    LapCompleted: 2,
                    LapDistPct: 0.455d,
                    F2TimeSeconds: 0d,
                    EstimatedTimeSeconds: 45.5d,
                    Position: 3,
                    ClassPosition: 1,
                    CarClass: 4098,
                    TrackSurface: 3,
                    OnPitRoad: false)
            ]));

        var snapshot = store.Snapshot();

        Assert.Empty(snapshot.Proximity.MulticlassApproaches);
        Assert.Null(snapshot.Proximity.StrongestMulticlassApproach);
    }

    [Fact]
    public void RecordFrame_DoesNotSurfaceApproachesWhenLocalClassIsFastest()
    {
        var store = new LiveTelemetryStore();
        ApplyMulticlassClassOrderSession(store);

        store.RecordFrame(CreateSample(
            playerCarIdx: 10,
            teamLapDistPct: 0.50d,
            teamEstimatedTimeSeconds: 50d,
            teamCarClass: 4100,
            nearbyCars:
            [
                new HistoricalCarProximity(
                    CarIdx: 51,
                    LapCompleted: 2,
                    LapDistPct: 0.455d,
                    F2TimeSeconds: 0d,
                    EstimatedTimeSeconds: 45.5d,
                    Position: 3,
                    ClassPosition: 1,
                    CarClass: 4099,
                    TrackSurface: 3,
                    OnPitRoad: false)
            ]));

        var snapshot = store.Snapshot();

        Assert.Empty(snapshot.Proximity.MulticlassApproaches);
        Assert.Null(snapshot.Proximity.StrongestMulticlassApproach);
    }

    [Fact]
    public void RecordFrame_DoesNotSurfaceMulticlassApproachInsideCloseRadarRange()
    {
        var store = new LiveTelemetryStore();
        ApplyMulticlassClassOrderSession(store);

        store.RecordFrame(CreateSample(
            playerCarIdx: 10,
            teamLapDistPct: 0.50d,
            teamEstimatedTimeSeconds: 50d,
            teamCarClass: 4098,
            nearbyCars:
            [
                new HistoricalCarProximity(
                    CarIdx: 51,
                    LapCompleted: 2,
                    LapDistPct: 0.485d,
                    F2TimeSeconds: 0d,
                    EstimatedTimeSeconds: 48.5d,
                    Position: 3,
                    ClassPosition: 1,
                    CarClass: 4099,
                    TrackSurface: 3,
                    OnPitRoad: false)
            ]));

        var snapshot = store.Snapshot();

        Assert.Empty(snapshot.Proximity.MulticlassApproaches);
        Assert.Null(snapshot.Proximity.StrongestMulticlassApproach);
    }

    [Fact]
    public void RecordFrame_SuppressesRadarWhenCameraFocusLeavesLocalPlayer()
    {
        var store = new LiveTelemetryStore();
        var startedAtUtc = DateTimeOffset.UtcNow;

        store.RecordFrame(CreateSample(
            capturedAtUtc: startedAtUtc,
            playerCarIdx: 10,
            teamLapDistPct: 0.50d,
            teamEstimatedTimeSeconds: 50d,
            teamCarClass: 4098,
            nearbyCars:
            [
                new HistoricalCarProximity(
                    CarIdx: 51,
                    LapCompleted: 2,
                    LapDistPct: 0.455d,
                    F2TimeSeconds: 0d,
                    EstimatedTimeSeconds: 45.5d,
                    Position: 3,
                    ClassPosition: 1,
                    CarClass: 4099,
                    TrackSurface: 3,
                    OnPitRoad: false)
            ]));
        store.RecordFrame(CreateSample(
            capturedAtUtc: startedAtUtc.AddSeconds(1),
            playerCarIdx: 10,
            teamCarClass: 4098,
            focusCarIdx: 30,
            focusLapDistPct: 0.50d,
            focusEstimatedTimeSeconds: 50d,
            focusCarClass: 4098,
            nearbyCars:
            [
                new HistoricalCarProximity(
                    CarIdx: 51,
                    LapCompleted: 2,
                    LapDistPct: 0.465d,
                    F2TimeSeconds: 0d,
                    EstimatedTimeSeconds: 46.5d,
                    Position: 3,
                    ClassPosition: 1,
                    CarClass: 4099,
                    TrackSurface: 3,
                    OnPitRoad: false)
            ]));

        var snapshot = store.Snapshot();

        Assert.False(snapshot.Proximity.HasData);
        Assert.Empty(snapshot.Proximity.NearbyCars);
        Assert.Empty(snapshot.Proximity.MulticlassApproaches);
        Assert.Null(snapshot.Proximity.StrongestMulticlassApproach);
        Assert.False(snapshot.Models.Spatial.HasData);
        Assert.Null(snapshot.Models.Spatial.ReferenceCarIdx);
        Assert.Null(snapshot.Models.Spatial.ReferenceLapDistPct);
        Assert.Empty(snapshot.Models.Spatial.Cars);
        Assert.Empty(snapshot.Models.Spatial.MulticlassApproaches);
    }

    [Fact]
    public void RecordFrame_BuildsSharedRaceModelsFromCurrentSnapshot()
    {
        var store = new LiveTelemetryStore();
        store.ApplySessionInfo("""
WeekendInfo:
 TrackDisplayName: Test Circuit
 TrackLength: 5.100
 EventType: Race
 TeamRacing: 1
 TrackWeatherType: Static
 TrackSkies: Partly Cloudy
 TrackPrecipitation: 12
SessionInfo:
 CurrentSessionNum: 0
 Sessions:
 - SessionNum: 0
   SessionType: Race
   SessionName: Race
   SessionTrackRubberState: Moderate Usage
   ResultsPositions:
   - Position: 1
     ClassPosition: 0
     CarIdx: 11
     Lap: 2
     FastestTime: 89.1000
     LastTime: 91.2000
     LapsComplete: 2
     ReasonOutStr: Running
   - Position: 2
     ClassPosition: 1
     CarIdx: 10
     Lap: 2
     FastestTime: 89.5000
     LastTime: 92.3000
     LapsComplete: 2
     ReasonOutStr: Running
   - Position: 3
     ClassPosition: 2
     CarIdx: 12
     Lap: 2
     FastestTime: 90.0000
     LastTime: 93.4000
     LapsComplete: 2
     ReasonOutStr: Running
DriverInfo:
 DriverCarIdx: 10
 DriverCarFuelKgPerLtr: 0.75
 Drivers:
 - CarIdx: 10
   UserName: Driver One
   TeamName: Team One
   CarNumber: 71
   CarClassID: 4098
   CarClassShortName: GT3
   CarClassColor: 0xffda59
 - CarIdx: 11
   UserName: Leader One
   TeamName: Leader Team
   CarNumber: 1
   CarClassID: 4098
   CarClassShortName: GT3
   CarClassColor: 0xffda59
 - CarIdx: 12
   UserName: Chaser One
   TeamName: Chaser Team
   CarNumber: 12
   CarClassID: 4098
   CarClassShortName: GT3
   CarClassColor: 0xffda59
""");

        store.RecordFrame(CreateSample(
            playerCarIdx: 10,
            carLeftRight: 4,
            precipitationPercent: 0.12d,
            relativeHumidityPercent: 0.95d,
            fogLevelPercent: 0.05d,
            teamLapDistPct: 0.50d,
            teamEstimatedTimeSeconds: 50d,
            teamCarClass: 4098,
            teamPosition: 7,
            teamClassPosition: 3,
            classLeaderCarIdx: 11,
            classLeaderLapDistPct: 0.53d,
            classLeaderF2TimeSeconds: 0d,
            focusClassCars:
            [
                new HistoricalCarProximity(
                    CarIdx: 11,
                    LapCompleted: 2,
                    LapDistPct: 0.53d,
                    F2TimeSeconds: 0d,
                    EstimatedTimeSeconds: 53d,
                    Position: 1,
                    ClassPosition: 1,
                    CarClass: 4098,
                    TrackSurface: 3,
                    OnPitRoad: false),
                new HistoricalCarProximity(
                    CarIdx: 12,
                    LapCompleted: 2,
                    LapDistPct: 0.49d,
                    F2TimeSeconds: 4d,
                    EstimatedTimeSeconds: 49d,
                    Position: 8,
                    ClassPosition: 4,
                    CarClass: 4098,
                    TrackSurface: 3,
                    OnPitRoad: false)
            ],
            nearbyCars:
            [
                new HistoricalCarProximity(
                    CarIdx: 12,
                    LapCompleted: 2,
                    LapDistPct: 0.49d,
                    F2TimeSeconds: 4d,
                    EstimatedTimeSeconds: 49d,
                    Position: 8,
                    ClassPosition: 4,
                    CarClass: 4098,
                    TrackSurface: 3,
                    OnPitRoad: false)
            ]));

        var models = store.Snapshot().Models;

        Assert.True(models.Session.HasData);
        Assert.Equal("Race", models.Session.SessionType);
        Assert.Equal(5100d, models.Spatial.TrackLengthMeters!.Value, precision: 6);
        Assert.Equal(10, models.Spatial.ReferenceCarIdx);
        Assert.Equal(4098, models.Spatial.ReferenceCarClass);
        Assert.Equal(4, models.Spatial.CarLeftRight);
        Assert.Equal("both sides", models.Spatial.SideStatus);
        Assert.True(models.Spatial.HasCarLeft);
        Assert.True(models.Spatial.HasCarRight);
        var spatialCar = Assert.Single(models.Spatial.Cars);
        Assert.Equal(12, spatialCar.CarIdx);
        Assert.Equal(-1d, spatialCar.RelativeSeconds!.Value, precision: 6);
        Assert.True(spatialCar.HasReliableRelativeSeconds);
        Assert.Equal(10, models.DriverDirectory.PlayerCarIdx);
        Assert.Equal("Driver One", models.DriverDirectory.PlayerDriver?.DriverName);
        Assert.Equal(10, models.Timing.FocusRow?.CarIdx);
        Assert.Contains(models.Timing.ClassRows, row => row.CarIdx == 12 && row.DriverName == "Chaser One");
        Assert.Contains(models.Relative.Rows, row => row.CarIdx == 12 && row.IsBehind);
        Assert.True(models.Scoring.HasData);
        Assert.Equal(3, models.Scoring.Rows.Count);
        Assert.Collection(
            models.Scoring.Rows,
            row =>
            {
                Assert.Equal(11, row.CarIdx);
                Assert.Equal(1, row.ClassPosition);
            },
            row =>
            {
                Assert.Equal(10, row.CarIdx);
                Assert.Equal(2, row.ClassPosition);
            },
            row =>
            {
                Assert.Equal(12, row.CarIdx);
                Assert.Equal(3, row.ClassPosition);
            });
        Assert.Equal(3, models.Coverage.ResultRowCount);
        Assert.Equal(3, models.Coverage.LiveScoringRowCount);
        Assert.Equal(3, models.Coverage.LiveTimingRowCount);
        Assert.Equal(3, models.Coverage.LiveSpatialRowCount);
        Assert.Equal(1, models.Coverage.LiveProximityRowCount);
        Assert.Equal("Partly Cloudy", models.Weather.SkiesLabel);
        Assert.Equal(12d, models.Weather.PrecipitationPercent!.Value, precision: 6);
        Assert.Equal(95d, models.Weather.RelativeHumidityPercent!.Value, precision: 6);
        Assert.Equal(5d, models.Weather.FogLevelPercent!.Value, precision: 6);
        Assert.True(models.FuelPit.Fuel.HasValidFuel);
        Assert.True(models.Timing.ClassLeaderGapEvidence.IsUsable);
        Assert.True(models.Timing.FocusRow!.CanUseForRadarPlacement);
        Assert.Equal("requires_previous_green_distance_sample", models.FuelPit.BaselineEligibilityEvidence.MissingReason);
    }

    [Fact]
    public void RecordFrame_MapsLiveTireCompoundTelemetryThroughSharedModel()
    {
        var store = new LiveTelemetryStore();
        store.ApplySessionInfo("""
SessionInfo:
 CurrentSessionNum: 0
 Sessions:
 - SessionNum: 0
   SessionType: Race
DriverInfo:
 DriverCarIdx: 10
 DriverTires:
 - TireIndex: 0
   TireCompoundType: "Hard"
 - TireIndex: 1
   TireCompoundType: "Wet"
 Drivers:
 - CarIdx: 10
   UserName: Reference Driver
 - CarIdx: 12
   UserName: Threat Driver
""");

        store.RecordFrame(CreateSample(
            playerCarIdx: 10,
            playerTireCompound: 0,
            teamTireCompound: 0,
            allCars:
            [
                new HistoricalCarProximity(
                    CarIdx: 12,
                    LapCompleted: 2,
                    LapDistPct: 0.49d,
                    F2TimeSeconds: 4d,
                    EstimatedTimeSeconds: 49d,
                    Position: 2,
                    ClassPosition: 2,
                    CarClass: 4098,
                    TrackSurface: 3,
                    OnPitRoad: false,
                    TireCompound: 1)
            ]));

        var tires = store.Snapshot().Models.TireCompounds;

        Assert.True(tires.HasData);
        Assert.Equal(LiveModelQuality.Reliable, tires.Quality);
        Assert.Collection(
            tires.Definitions,
            tire =>
            {
                Assert.Equal(0, tire.Index);
                Assert.Equal("Hard", tire.Label);
                Assert.Equal("H", tire.ShortLabel);
                Assert.False(tire.IsWet);
            },
            tire =>
            {
                Assert.Equal(1, tire.Index);
                Assert.Equal("Wet", tire.Label);
                Assert.Equal("W", tire.ShortLabel);
                Assert.True(tire.IsWet);
            });
        Assert.Equal("Hard", tires.PlayerCar?.Label);
        var threat = Assert.Single(tires.Cars, car => car.CarIdx == 12);
        Assert.Equal("Wet", threat.Label);
        Assert.True(threat.IsWet);
        Assert.Equal("CarIdxTireCompound", threat.Evidence.Source);
    }

    [Fact]
    public void RecordFrame_TreatsNegativeTireCompoundAsUnavailableNotWet()
    {
        var store = new LiveTelemetryStore();
        store.ApplySessionInfo("""
SessionInfo:
 CurrentSessionNum: 0
 Sessions:
 - SessionNum: 0
   SessionType: Race
DriverInfo:
 DriverCarIdx: 10
 DriverTires:
 - TireIndex: 0
   TireCompoundType: "Hard"
 - TireIndex: 1
   TireCompoundType: "Wet"
 Drivers:
 - CarIdx: 10
   UserName: Reference Driver
 - CarIdx: 12
   UserName: Threat Driver
""");

        store.RecordFrame(CreateSample(
            playerCarIdx: 10,
            playerTireCompound: -1,
            teamTireCompound: -1,
            allCars:
            [
                new HistoricalCarProximity(
                    CarIdx: 12,
                    LapCompleted: 2,
                    LapDistPct: 0.49d,
                    F2TimeSeconds: 4d,
                    EstimatedTimeSeconds: 49d,
                    Position: 2,
                    ClassPosition: 2,
                    CarClass: 4098,
                    TrackSurface: 3,
                    OnPitRoad: false,
                    TireCompound: -1)
            ]));

        var tires = store.Snapshot().Models.TireCompounds;

        Assert.True(tires.HasData);
        Assert.Null(tires.PlayerCar);
        Assert.Empty(tires.Cars);
        Assert.Contains(tires.Definitions, tire => tire.Index == 1 && tire.Label == "Wet");
    }

    [Fact]
    public void RecordFrame_HoldsRaceStartingGridUntilLiveScoringCoverageIsMeaningful()
    {
        var store = new LiveTelemetryStore();
        store.ApplySessionInfo("""
WeekendInfo:
 TrackDisplayName: Test Circuit
 TrackLength: 5.100
 EventType: Race
SessionInfo:
 CurrentSessionNum: 0
 Sessions:
 - SessionNum: 0
   SessionType: Race
   SessionName: Race
DriverInfo:
 DriverCarIdx: 10
 Drivers:
 - CarIdx: 10
   UserName: Reference Driver
   CarNumber: 10
   CarClassID: 4098
   CarClassShortName: GT3
 - CarIdx: 11
   UserName: Grid Leader
   CarNumber: 11
   CarClassID: 4098
   CarClassShortName: GT3
 - CarIdx: 12
   UserName: Live Leader
   CarNumber: 12
   CarClassID: 4098
   CarClassShortName: GT3
QualifyResultsInfo:
 Results:
 - Position: 0
   ClassPosition: 0
   CarIdx: 11
 - Position: 1
   ClassPosition: 1
   CarIdx: 10
 - Position: 2
   ClassPosition: 2
   CarIdx: 12
""");
        var now = DateTimeOffset.UtcNow;

        store.RecordFrame(CreateSample(
            capturedAtUtc: now,
            sessionTime: 56.9d,
            sessionState: 4,
            playerCarIdx: 10,
            focusCarIdx: 10,
            allCars:
            [
                Car(11, position: 0, classPosition: 0, lapDistPct: 0.10d, f2TimeSeconds: 0d),
                Car(10, position: 0, classPosition: 0, lapDistPct: 0.09d, f2TimeSeconds: 0d),
                Car(12, position: 0, classPosition: 0, lapDistPct: 0.11d, f2TimeSeconds: 0d)
            ]));

        var gridSnapshot = store.Snapshot();
        Assert.True(gridSnapshot.Models.Scoring.HasData);
        Assert.Equal(LiveScoringSource.StartingGrid, gridSnapshot.Models.Scoring.Source);
        Assert.Equal(new[] { 11, 10, 12 }, gridSnapshot.Models.Scoring.Rows.Select(row => row.CarIdx));
        Assert.Equal("source: starting grid", StandingsOverlayViewModel.From(gridSnapshot, now).Source);

        store.RecordFrame(CreateSample(
            capturedAtUtc: now.AddSeconds(2),
            sessionTime: 58.7d,
            sessionState: 4,
            playerCarIdx: 10,
            focusCarIdx: 10,
            allCars:
            [
                Car(12, position: 1, classPosition: 1, lapDistPct: 0.14d, f2TimeSeconds: 0d),
                Car(10, position: 2, classPosition: 2, lapDistPct: 0.13d, f2TimeSeconds: 1.7d),
                Car(11, position: 3, classPosition: 3, lapDistPct: 0.12d, f2TimeSeconds: 2.8d)
            ]));

        var liveSnapshot = store.Snapshot();
        Assert.False(liveSnapshot.Models.Scoring.HasData);
        Assert.Equal(LiveScoringSource.None, liveSnapshot.Models.Scoring.Source);

        var liveViewModel = StandingsOverlayViewModel.From(liveSnapshot, now.AddSeconds(2), maximumRows: 3);
        Assert.Equal("source: live timing telemetry", liveViewModel.Source);
        Assert.Equal(new[] { "#12", "#10", "#11" }, liveViewModel.Rows.Select(row => row.CarNumber));
    }

    [Fact]
    public void RecordFrame_UsesEstimatedTimingForRacePreGreenRelativeWithoutReorderingStandingsGrid()
    {
        var store = new LiveTelemetryStore();
        store.ApplySessionInfo("""
WeekendInfo:
 TrackDisplayName: Test Circuit
 TrackLength: 5.100
 EventType: Race
SessionInfo:
 CurrentSessionNum: 0
 Sessions:
 - SessionNum: 0
   SessionType: Race
   SessionName: Race
DriverInfo:
 DriverCarIdx: 10
 Drivers:
 - CarIdx: 10
   UserName: Reference Driver
   CarNumber: 10
   CarClassID: 4098
   CarClassShortName: GT3
 - CarIdx: 11
   UserName: Grid Leader
   CarNumber: 11
   CarClassID: 4098
   CarClassShortName: GT3
 - CarIdx: 12
   UserName: Grid Chase
   CarNumber: 12
   CarClassID: 4098
   CarClassShortName: GT3
QualifyResultsInfo:
 Results:
 - Position: 0
   ClassPosition: 0
   CarIdx: 11
 - Position: 1
   ClassPosition: 1
   CarIdx: 10
 - Position: 2
   ClassPosition: 2
   CarIdx: 12
""");
        var now = DateTimeOffset.UtcNow;

        store.RecordFrame(CreateSample(
            capturedAtUtc: now,
            sessionTime: 118.4d,
            sessionState: 3,
            playerCarIdx: 10,
            focusCarIdx: 10,
            teamCarClass: 4098,
            teamLapDistPct: 0.50d,
            teamEstimatedTimeSeconds: 50d,
            allCars:
            [
                Car(11, position: 0, classPosition: 0, lapDistPct: 0.53d, f2TimeSeconds: 0d, estimatedTimeSeconds: 53d),
                Car(10, position: 0, classPosition: 0, lapDistPct: 0.50d, f2TimeSeconds: 0d, estimatedTimeSeconds: 50d),
                Car(12, position: 0, classPosition: 0, lapDistPct: 0.48d, f2TimeSeconds: 0d, estimatedTimeSeconds: 48d)
            ]));

        var snapshot = store.Snapshot();
        var models = snapshot.Models;

        Assert.Equal(LiveScoringSource.StartingGrid, models.Scoring.Source);
        Assert.Equal(new[] { 11, 10, 12 }, models.Scoring.Rows.Select(row => row.CarIdx));

        var leader = Assert.Single(models.Timing.OverallRows, row => row.CarIdx == 11);
        var reference = Assert.Single(models.Timing.OverallRows, row => row.CarIdx == 10);
        var trailing = Assert.Single(models.Timing.OverallRows, row => row.CarIdx == 12);
        Assert.Null(leader.GapSecondsToClassLeader);
        Assert.Null(leader.DeltaSecondsToFocus);
        Assert.Null(reference.GapSecondsToClassLeader);
        Assert.Equal(0d, reference.DeltaSecondsToFocus);
        Assert.Null(trailing.GapSecondsToClassLeader);
        Assert.Null(trailing.DeltaSecondsToFocus);

        var standings = StandingsOverlayViewModel.From(snapshot, now, maximumRows: 3);
        Assert.Equal("source: starting grid", standings.Source);
        Assert.Equal(new[] { "#11", "#10", "#12" }, standings.Rows.Select(row => row.CarNumber));
        Assert.Equal(new[] { "--", "--", "--" }, standings.Rows.Select(row => row.Interval));
        Assert.Equal(new[] { "Leader", "--", "--" }, standings.Rows.Select(row => row.Gap));

        Assert.Contains(models.Relative.Rows, row =>
            row.CarIdx == 11
            && row.IsAhead
            && row.RelativeSeconds == 3d
            && row.TimingEvidence.IsUsable);
        Assert.Contains(models.Relative.Rows, row =>
            row.CarIdx == 12
            && row.IsBehind
            && row.RelativeSeconds == -2d
            && row.TimingEvidence.IsUsable);
    }

    [Fact]
    public void RecordFrame_MergesEstimatedRelativeRowsWhenProximityOnlyHasPartialField()
    {
        var store = new LiveTelemetryStore();
        store.ApplySessionInfo("""
WeekendInfo:
 EventType: Race
SessionInfo:
 CurrentSessionNum: 0
 Sessions:
 - SessionNum: 0
   SessionType: Race
   SessionName: Race
DriverInfo:
 DriverCarIdx: 10
 Drivers:
 - CarIdx: 10
   UserName: Reference Driver
   CarNumber: 10
   CarClassID: 4098
 - CarIdx: 11
   UserName: Near Proximity
   CarNumber: 11
   CarClassID: 4098
 - CarIdx: 12
   UserName: Estimated Only
   CarNumber: 12
   CarClassID: 4098
""");

        store.RecordFrame(CreateSample(
            sessionState: 3,
            playerCarIdx: 10,
            focusCarIdx: 10,
            teamCarClass: 4098,
            teamLapDistPct: 0.50d,
            teamEstimatedTimeSeconds: 50d,
            nearbyCars:
            [
                Car(11, position: 0, classPosition: 0, lapDistPct: 0.53d, f2TimeSeconds: 0d, estimatedTimeSeconds: 53d)
            ],
            allCars:
            [
                Car(11, position: 0, classPosition: 0, lapDistPct: 0.53d, f2TimeSeconds: 0d, estimatedTimeSeconds: 53d),
                Car(10, position: 0, classPosition: 0, lapDistPct: 0.50d, f2TimeSeconds: 0d, estimatedTimeSeconds: 50d),
                Car(12, position: 0, classPosition: 0, lapDistPct: 0.48d, f2TimeSeconds: 0d, estimatedTimeSeconds: 48d)
            ]));

        var rows = store.Snapshot().Models.Relative.Rows;

        Assert.Contains(rows, row =>
            row.CarIdx == 11
            && row.Source == "proximity"
            && row.RelativeSeconds == 3d);
        Assert.Contains(rows, row =>
            row.CarIdx == 12
            && row.Source == "estimated-relative"
            && row.RelativeSeconds == -2d);
    }

    [Fact]
    public void RecordFrame_KeepsRelativeTimingAvailableWhenPlayerIsInPitStall()
    {
        var store = new LiveTelemetryStore();
        store.ApplySessionInfo("""
WeekendInfo:
 EventType: Race
SessionInfo:
 CurrentSessionNum: 0
 Sessions:
 - SessionNum: 0
   SessionType: Race
   SessionName: Race
DriverInfo:
 DriverCarIdx: 10
 Drivers:
 - CarIdx: 10
   UserName: Reference Driver
   CarNumber: 10
   CarClassID: 4098
 - CarIdx: 11
   UserName: Leader
   CarNumber: 11
   CarClassID: 4098
 - CarIdx: 12
   UserName: Chase
   CarNumber: 12
   CarClassID: 4098
""");

        store.RecordFrame(CreateSample(
            sessionState: 4,
            playerCarIdx: 10,
            focusCarIdx: 10,
            teamCarClass: 4098,
            teamLapDistPct: 0.50d,
            teamEstimatedTimeSeconds: 50d,
            onPitRoad: true,
            playerCarInPitStall: true,
            teamOnPitRoad: true,
            allCars:
            [
                Car(11, position: 26, classPosition: 14, lapDistPct: 0.53d, f2TimeSeconds: 0d, estimatedTimeSeconds: 53d),
                Car(10, position: 27, classPosition: 15, lapDistPct: 0.50d, f2TimeSeconds: 0d, estimatedTimeSeconds: 50d, onPitRoad: true),
                Car(12, position: 28, classPosition: 16, lapDistPct: 0.48d, f2TimeSeconds: 0d, estimatedTimeSeconds: 48d)
            ]));

        var snapshot = store.Snapshot();

        Assert.Empty(snapshot.Models.Spatial.Cars);
        Assert.Contains(snapshot.Models.Relative.Rows, row =>
            row.CarIdx == 11
            && row.IsAhead
            && row.RelativeSeconds == 3d);
        Assert.Contains(snapshot.Models.Relative.Rows, row =>
            row.CarIdx == 12
            && row.IsBehind
            && row.RelativeSeconds == -2d);
    }

    [Fact]
    public void RecordFrame_DoesNotUseEstimatedTimingForRacePreGreenStateTwo()
    {
        var store = new LiveTelemetryStore();
        store.ApplySessionInfo("""
WeekendInfo:
 EventType: Race
SessionInfo:
 CurrentSessionNum: 0
 Sessions:
 - SessionNum: 0
   SessionType: Race
   SessionName: Race
DriverInfo:
 DriverCarIdx: 10
 Drivers:
 - CarIdx: 10
   UserName: Reference Driver
   CarNumber: 10
   CarClassID: 4098
 - CarIdx: 11
   UserName: Grid Leader
   CarNumber: 11
   CarClassID: 4098
QualifyResultsInfo:
 Results:
 - Position: 0
   ClassPosition: 0
   CarIdx: 11
 - Position: 1
   ClassPosition: 1
   CarIdx: 10
""");

        store.RecordFrame(CreateSample(
            sessionState: 2,
            playerCarIdx: 10,
            focusCarIdx: 10,
            teamCarClass: 4098,
            teamLapDistPct: 0.50d,
            teamEstimatedTimeSeconds: 50d,
            allCars:
            [
                Car(11, position: 0, classPosition: 0, lapDistPct: 0.53d, f2TimeSeconds: 0d, estimatedTimeSeconds: 53d),
                Car(10, position: 0, classPosition: 0, lapDistPct: 0.50d, f2TimeSeconds: 0d, estimatedTimeSeconds: 50d)
            ]));

        var models = store.Snapshot().Models;

        Assert.Null(models.Timing.FocusRow?.GapSecondsToClassLeader);
        var focusDelta = models.Timing.FocusRow?.DeltaSecondsToFocus;
        Assert.NotNull(focusDelta);
        Assert.Equal(0d, focusDelta.Value, precision: 6);
        Assert.Empty(models.Relative.Rows);
    }

    [Fact]
    public void RecordFrame_KeepsSpatialProgressWhenLapDistanceExistsBeforeCompletedLapCount()
    {
        var store = new LiveTelemetryStore();
        store.ApplySessionInfo("""
WeekendInfo:
 EventType: Race
 TrackDisplayName: Test Circuit
 TrackLength: 5.100
SessionInfo:
 CurrentSessionNum: 0
 Sessions:
 - SessionNum: 0
   SessionType: Race
   SessionName: Race
DriverInfo:
 DriverCarIdx: 10
 Drivers:
 - CarIdx: 10
   UserName: Reference Driver
   CarNumber: 10
   CarClassID: 4098
 - CarIdx: 11
   UserName: Grid Car
   CarNumber: 11
   CarClassID: 4098
""");

        store.RecordFrame(CreateSample(
            sessionState: 3,
            playerCarIdx: 10,
            focusCarIdx: 10,
            teamCarClass: 4098,
            teamLapCompleted: -1,
            teamLapDistPct: 0.50d,
            teamEstimatedTimeSeconds: 50d,
            allCars:
            [
                Car(11, position: 0, classPosition: 0, lapCompleted: -1, lapDistPct: 0.53d, f2TimeSeconds: 0d, estimatedTimeSeconds: 53d, trackSurface: 3)
            ]));

        var models = store.Snapshot().Models;
        var player = Assert.Single(models.Timing.OverallRows, row => row.CarIdx == 10);
        var other = Assert.Single(models.Timing.OverallRows, row => row.CarIdx == 11);

        Assert.True(player.HasSpatialProgress);
        Assert.True(other.HasSpatialProgress);
        Assert.True(other.CanUseForRadarPlacement);
        Assert.Null(other.ProgressLaps);
        Assert.Equal(0.53d, other.LapDistPct);
    }

    [Fact]
    public void RecordFrame_RendersPreGridRadarFromLapDistanceWhenOfficialPositionsAreZero()
    {
        var store = new LiveTelemetryStore();
        store.ApplySessionInfo("""
WeekendInfo:
 EventType: Race
 TrackDisplayName: Test Circuit
 TrackLength: 5.100
SessionInfo:
 CurrentSessionNum: 0
 Sessions:
 - SessionNum: 0
   SessionType: Race
   SessionName: Race
DriverInfo:
 DriverCarIdx: 10
 Drivers:
 - CarIdx: 10
   UserName: Reference Driver
   CarNumber: 10
   CarClassID: 4098
 - CarIdx: 11
   UserName: Grid Car
   CarNumber: 11
   CarClassID: 4098
""");

        store.RecordFrame(CreateSample(
            sessionState: 3,
            playerCarIdx: 10,
            focusCarIdx: 10,
            carLeftRight: 1,
            teamCarClass: 4098,
            teamLapCompleted: -1,
            teamLapDistPct: 0.500d,
            teamEstimatedTimeSeconds: 50d,
            nearbyCars:
            [
                Car(
                    11,
                    position: 0,
                    classPosition: 0,
                    lapCompleted: -1,
                    lapDistPct: 0.50156862745d,
                    f2TimeSeconds: 0d,
                    estimatedTimeSeconds: 50.1d,
                    trackSurface: 3,
                    onPitRoad: false)
            ]));

        var models = store.Snapshot().Models;
        var spatialCar = Assert.Single(models.Spatial.Cars);

        Assert.True(models.Spatial.HasData);
        Assert.Equal(3, models.Session.SessionState);
        Assert.Equal(1, models.Spatial.CarLeftRight);
        Assert.False(models.Spatial.HasCarLeft);
        Assert.False(models.Spatial.HasCarRight);
        Assert.Equal(11, spatialCar.CarIdx);
        Assert.Equal(8d, spatialCar.RelativeMeters!.Value, precision: 6);
        Assert.Null(spatialCar.OverallPosition);
        Assert.Null(spatialCar.ClassPosition);
    }

    [Fact]
    public void RecordFrame_UsesEstimatedStandingsGapAndIntervalWhenRaceF2IsPlaceholderAfterGreen()
    {
        var store = new LiveTelemetryStore();
        store.ApplySessionInfo("""
WeekendInfo:
 EventType: Race
 TrackDisplayName: Test Circuit
 TrackLength: 5.100
SessionInfo:
 CurrentSessionNum: 0
 Sessions:
 - SessionNum: 0
   SessionType: Race
   SessionName: Race
DriverInfo:
 DriverCarIdx: 10
 Drivers:
 - CarIdx: 10
   UserName: Reference Driver
   CarNumber: 10
   CarClassID: 4098
 - CarIdx: 11
   UserName: Class Leader
   CarNumber: 11
   CarClassID: 4098
 - CarIdx: 12
   UserName: Chase Driver
   CarNumber: 12
   CarClassID: 4098
""");

        store.RecordFrame(CreateSample(
            sessionState: 4,
            playerCarIdx: 10,
            focusCarIdx: 10,
            teamPosition: 2,
            teamClassPosition: 2,
            teamCarClass: 4098,
            teamLapDistPct: 0.50d,
            teamF2TimeSeconds: 0.001d,
            teamEstimatedTimeSeconds: 50d,
            teamLastLapTimeSeconds: 90d,
            classLeaderCarIdx: 11,
            classLeaderLapDistPct: 0.53d,
            classLeaderF2TimeSeconds: 0d,
            allCars:
            [
                Car(11, position: 1, classPosition: 1, lapDistPct: 0.53d, f2TimeSeconds: 0d, lapCompleted: 2, estimatedTimeSeconds: 53d),
                Car(10, position: 2, classPosition: 2, lapDistPct: 0.50d, f2TimeSeconds: 0.001d, lapCompleted: 2, estimatedTimeSeconds: 50d),
                Car(12, position: 3, classPosition: 3, lapDistPct: 0.48d, f2TimeSeconds: 0.002d, lapCompleted: 2, estimatedTimeSeconds: 48d)
            ]));

        var models = store.Snapshot().Models;
        var leader = Assert.Single(models.Timing.OverallRows, row => row.CarIdx == 11);
        var reference = Assert.Single(models.Timing.OverallRows, row => row.CarIdx == 10);
        var chase = Assert.Single(models.Timing.OverallRows, row => row.CarIdx == 12);

        Assert.Equal(0d, leader.GapSecondsToClassLeader);
        Assert.Null(leader.IntervalSecondsToPreviousClassRow);
        Assert.Equal(3d, reference.GapSecondsToClassLeader);
        Assert.Equal(3d, reference.IntervalSecondsToPreviousClassRow);
        Assert.Equal(5d, chase.GapSecondsToClassLeader);
        Assert.Equal(2d, chase.IntervalSecondsToPreviousClassRow);

        var standings = StandingsOverlayViewModel.From(store.Snapshot(), DateTimeOffset.UtcNow, maximumRows: 3);
        Assert.Equal(new[] { "Lap 3", "+3.0", "+5.0" }, standings.Rows.Select(row => row.Gap));
        Assert.Equal(new[] { "0.0", "+3.0", "+2.0" }, standings.Rows.Select(row => row.Interval));
    }

    [Fact]
    public void RecordFrame_UsesWrapAwareEstimatedStandingsGapAndIntervalAcrossStartFinishAfterGreen()
    {
        var store = new LiveTelemetryStore();
        store.ApplySessionInfo("""
WeekendInfo:
 EventType: Race
 TrackDisplayName: Test Circuit
 TrackLength: 5.100
SessionInfo:
 CurrentSessionNum: 0
 Sessions:
 - SessionNum: 0
   SessionType: Race
   SessionName: Race
DriverInfo:
 DriverCarIdx: 10
 Drivers:
 - CarIdx: 10
   UserName: Reference Driver
   CarNumber: 10
   CarClassID: 4098
 - CarIdx: 11
   UserName: Class Leader
   CarNumber: 11
   CarClassID: 4098
 - CarIdx: 12
   UserName: Chase Driver
   CarNumber: 12
   CarClassID: 4098
""");

        store.RecordFrame(CreateSample(
            sessionState: 4,
            playerCarIdx: 10,
            focusCarIdx: 10,
            teamPosition: 2,
            teamClassPosition: 2,
            teamCarClass: 4098,
            teamLapDistPct: 0.99d,
            teamF2TimeSeconds: 0.001d,
            teamEstimatedTimeSeconds: 89d,
            teamLastLapTimeSeconds: 90d,
            classLeaderCarIdx: 11,
            classLeaderLapDistPct: 0.01d,
            classLeaderF2TimeSeconds: 0d,
            allCars:
            [
                Car(11, position: 1, classPosition: 1, lapDistPct: 0.01d, f2TimeSeconds: 0d, lapCompleted: 2, estimatedTimeSeconds: 4d),
                Car(10, position: 2, classPosition: 2, lapDistPct: 0.99d, f2TimeSeconds: 0.001d, lapCompleted: 2, estimatedTimeSeconds: 89d),
                Car(12, position: 3, classPosition: 3, lapDistPct: 0.97d, f2TimeSeconds: 0.002d, lapCompleted: 2, estimatedTimeSeconds: 87d)
            ]));

        var reference = Assert.Single(store.Snapshot().Models.Timing.OverallRows, row => row.CarIdx == 10);
        var chase = Assert.Single(store.Snapshot().Models.Timing.OverallRows, row => row.CarIdx == 12);

        Assert.Equal(5d, reference.GapSecondsToClassLeader);
        Assert.Equal(5d, reference.IntervalSecondsToPreviousClassRow);
        Assert.Equal(7d, chase.GapSecondsToClassLeader);
        Assert.Equal(2d, chase.IntervalSecondsToPreviousClassRow);
    }

    [Fact]
    public void RecordFrame_UsesLapGapForDifferentLapStandingsComparisonsAfterGreen()
    {
        var store = new LiveTelemetryStore();
        store.ApplySessionInfo("""
WeekendInfo:
 EventType: Race
 TrackDisplayName: Test Circuit
 TrackLength: 5.100
SessionInfo:
 CurrentSessionNum: 0
 Sessions:
 - SessionNum: 0
   SessionType: Race
   SessionName: Race
DriverInfo:
 DriverCarIdx: 10
 Drivers:
 - CarIdx: 10
   UserName: Reference Driver
   CarNumber: 10
   CarClassID: 4098
 - CarIdx: 11
   UserName: Class Leader
   CarNumber: 11
   CarClassID: 4098
""");

        store.RecordFrame(CreateSample(
            sessionState: 4,
            playerCarIdx: 10,
            focusCarIdx: 10,
            teamPosition: 2,
            teamClassPosition: 2,
            teamCarClass: 4098,
            teamLapCompleted: 1,
            teamLapDistPct: 0.50d,
            teamF2TimeSeconds: 0.001d,
            teamEstimatedTimeSeconds: 50d,
            teamLastLapTimeSeconds: 90d,
            classLeaderCarIdx: 11,
            classLeaderLapDistPct: 0.53d,
            classLeaderF2TimeSeconds: 0d,
            allCars:
            [
                Car(11, position: 1, classPosition: 1, lapDistPct: 0.53d, f2TimeSeconds: 0d, lapCompleted: 2, estimatedTimeSeconds: 53d),
                Car(10, position: 2, classPosition: 2, lapDistPct: 0.50d, f2TimeSeconds: 0.001d, lapCompleted: 1, estimatedTimeSeconds: 50d)
            ]));

        var reference = Assert.Single(store.Snapshot().Models.Timing.OverallRows, row => row.CarIdx == 10);

        Assert.Null(reference.GapSecondsToClassLeader);
        Assert.Equal(1d, reference.GapLapsToClassLeader);
        Assert.Null(reference.IntervalSecondsToPreviousClassRow);
        Assert.Equal(1d, reference.IntervalLapsToPreviousClassRow);

        var standings = StandingsOverlayViewModel.From(store.Snapshot(), DateTimeOffset.UtcNow, maximumRows: 2);
        Assert.Equal(new[] { "Lap 3", "+1L" }, standings.Rows.Select(row => row.Gap));
        Assert.Equal(new[] { "0.0", "+1L" }, standings.Rows.Select(row => row.Interval));
    }

    [Fact]
    public void RecordFrame_UsesEstimatedTimingAcrossStartFinishLapCounterSkewAfterGreen()
    {
        var store = new LiveTelemetryStore();
        store.ApplySessionInfo("""
WeekendInfo:
 EventType: Race
 TrackDisplayName: Test Circuit
 TrackLength: 5.100
SessionInfo:
 CurrentSessionNum: 0
 Sessions:
 - SessionNum: 0
   SessionType: Race
   SessionName: Race
DriverInfo:
 DriverCarIdx: 10
 Drivers:
 - CarIdx: 10
   UserName: Reference Driver
   CarNumber: 10
   CarClassID: 4098
 - CarIdx: 11
   UserName: Class Leader
   CarNumber: 11
   CarClassID: 4098
""");

        store.RecordFrame(CreateSample(
            sessionState: 4,
            playerCarIdx: 10,
            focusCarIdx: 10,
            teamPosition: 2,
            teamClassPosition: 2,
            teamCarClass: 4098,
            teamLapCompleted: 1,
            teamLapDistPct: 0.99d,
            teamF2TimeSeconds: 0.001d,
            teamEstimatedTimeSeconds: 89d,
            teamLastLapTimeSeconds: 90d,
            classLeaderCarIdx: 11,
            classLeaderLapDistPct: 0.01d,
            classLeaderF2TimeSeconds: 0d,
            allCars:
            [
                Car(11, position: 1, classPosition: 1, lapDistPct: 0.01d, f2TimeSeconds: 0d, lapCompleted: 2, estimatedTimeSeconds: 1d),
                Car(10, position: 2, classPosition: 2, lapDistPct: 0.99d, f2TimeSeconds: 0.001d, lapCompleted: 1, estimatedTimeSeconds: 89d)
            ]));

        var reference = Assert.Single(store.Snapshot().Models.Timing.OverallRows, row => row.CarIdx == 10);

        Assert.Equal(2d, reference.GapSecondsToClassLeader);
        Assert.Null(reference.GapLapsToClassLeader);
        Assert.Equal(2d, reference.IntervalSecondsToPreviousClassRow);
        Assert.Null(reference.IntervalLapsToPreviousClassRow);
    }

    [Fact]
    public void RecordFrame_InfersLapGapFromF2AndEstimatedTimingWhenLapCompletedIsMissingAfterGreen()
    {
        var store = new LiveTelemetryStore();
        store.ApplySessionInfo("""
WeekendInfo:
 EventType: Race
 TrackDisplayName: Test Circuit
 TrackLength: 5.100
SessionInfo:
 CurrentSessionNum: 0
 Sessions:
 - SessionNum: 0
   SessionType: Race
   SessionName: Race
DriverInfo:
 DriverCarIdx: 10
 Drivers:
 - CarIdx: 10
   UserName: Reference Driver
   CarNumber: 10
   CarClassID: 4098
 - CarIdx: 11
   UserName: Class Leader
   CarNumber: 11
   CarClassID: 4098
""");

        store.RecordFrame(CreateSample(
            sessionState: 4,
            playerCarIdx: 10,
            focusCarIdx: 10,
            teamPosition: 2,
            teamClassPosition: 2,
            teamCarClass: 4098,
            teamLapCompleted: null,
            teamLapDistPct: 0.45d,
            teamF2TimeSeconds: 95d,
            teamEstimatedTimeSeconds: 45d,
            teamLastLapTimeSeconds: 90d,
            classLeaderCarIdx: 11,
            classLeaderLapDistPct: null,
            classLeaderF2TimeSeconds: 0d,
            allCars:
            [
                Car(11, position: 1, classPosition: 1, lapDistPct: 0.50d, f2TimeSeconds: 0d, lapCompleted: -1, estimatedTimeSeconds: 50d),
                Car(10, position: 2, classPosition: 2, lapDistPct: 0.45d, f2TimeSeconds: 95d, lapCompleted: -1, estimatedTimeSeconds: 45d)
            ]));

        var reference = Assert.Single(store.Snapshot().Models.Timing.OverallRows, row => row.CarIdx == 10);

        Assert.Null(reference.GapSecondsToClassLeader);
        Assert.Equal(1d, reference.GapLapsToClassLeader);
        Assert.Null(reference.IntervalSecondsToPreviousClassRow);
        Assert.Equal(1d, reference.IntervalLapsToPreviousClassRow);

        var standings = StandingsOverlayViewModel.From(store.Snapshot(), DateTimeOffset.UtcNow, maximumRows: 2);
        Assert.Equal(new[] { "Leader", "+1L" }, standings.Rows.Select(row => row.Gap));
        Assert.Equal(new[] { "0.0", "+1L" }, standings.Rows.Select(row => row.Interval));
    }

    [Fact]
    public void RecordFrame_UsesEstimatedGridTimingUntilRaceF2PlaceholdersBecomeUsableAfterGreen()
    {
        var store = new LiveTelemetryStore();
        store.ApplySessionInfo("""
WeekendInfo:
 EventType: Race
 TrackDisplayName: Test Circuit
 TrackLength: 5.100
SessionInfo:
 CurrentSessionNum: 0
 Sessions:
 - SessionNum: 0
   SessionType: Race
   SessionName: Race
DriverInfo:
 DriverCarIdx: 10
 Drivers:
 - CarIdx: 10
   UserName: Reference Driver
   CarNumber: 10
   CarClassID: 4098
 - CarIdx: 11
   UserName: Class Leader
   CarNumber: 11
   CarClassID: 4098
 - CarIdx: 12
   UserName: Chase Driver
   CarNumber: 12
   CarClassID: 4098
""");

        store.RecordFrame(CreateSample(
            sessionState: 3,
            playerCarIdx: 10,
            focusCarIdx: 10,
            teamPosition: 2,
            teamClassPosition: 2,
            teamCarClass: 4098,
            teamLapCompleted: -1,
            teamLapDistPct: -1d,
            teamF2TimeSeconds: 0.001d,
            teamEstimatedTimeSeconds: 88d,
            teamLastLapTimeSeconds: 90d,
            classLeaderCarIdx: 11,
            classLeaderLapDistPct: -1d,
            classLeaderF2TimeSeconds: 0d,
            playerTrackSurface: 3,
            allCars:
            [
                Car(11, position: 1, classPosition: 1, lapDistPct: -1d, f2TimeSeconds: 0d, lapCompleted: -1, estimatedTimeSeconds: 90d),
                Car(10, position: 2, classPosition: 2, lapDistPct: -1d, f2TimeSeconds: 0.001d, lapCompleted: -1, estimatedTimeSeconds: 88d),
                Car(12, position: 3, classPosition: 3, lapDistPct: -1d, f2TimeSeconds: 0.002d, lapCompleted: -1, estimatedTimeSeconds: 86d)
            ]));

        store.RecordFrame(CreateSample(
            sessionState: 4,
            playerCarIdx: 10,
            focusCarIdx: 10,
            teamPosition: 2,
            teamClassPosition: 2,
            teamCarClass: 4098,
            teamLapCompleted: -1,
            teamLapDistPct: -1d,
            teamF2TimeSeconds: 0.001d,
            teamEstimatedTimeSeconds: 88d,
            teamLastLapTimeSeconds: 90d,
            classLeaderCarIdx: 11,
            classLeaderLapDistPct: -1d,
            classLeaderF2TimeSeconds: 0d,
            allCars:
            [
                Car(11, position: 1, classPosition: 1, lapDistPct: -1d, f2TimeSeconds: 0d, lapCompleted: -1, estimatedTimeSeconds: 90d, trackSurface: null),
                Car(10, position: 2, classPosition: 2, lapDistPct: -1d, f2TimeSeconds: 0.001d, lapCompleted: -1, estimatedTimeSeconds: 88d, trackSurface: null),
                Car(12, position: 3, classPosition: 3, lapDistPct: -1d, f2TimeSeconds: 0.002d, lapCompleted: -1, estimatedTimeSeconds: 86d, trackSurface: null)
            ]));

        var reference = Assert.Single(store.Snapshot().Models.Timing.OverallRows, row => row.CarIdx == 10);
        var chase = Assert.Single(store.Snapshot().Models.Timing.OverallRows, row => row.CarIdx == 12);

        Assert.Equal(2d, reference.GapSecondsToClassLeader);
        Assert.Equal("CarIdxEstTime+CarIdxPosition", reference.GapEvidence.Source);
        Assert.Equal(2d, reference.IntervalSecondsToPreviousClassRow);
        Assert.Equal(4d, chase.GapSecondsToClassLeader);
        Assert.Equal(2d, chase.IntervalSecondsToPreviousClassRow);
    }

    [Fact]
    public void RecordFrame_KeepsEstimatedRelativeRowsWhenPlayerTowedDuringRacePreGreenStateThree()
    {
        var store = new LiveTelemetryStore();
        store.ApplySessionInfo("""
WeekendInfo:
 EventType: Race
SessionInfo:
 CurrentSessionNum: 0
 Sessions:
 - SessionNum: 0
   SessionType: Race
   SessionName: Race
DriverInfo:
 DriverCarIdx: 10
 Drivers:
 - CarIdx: 10
   UserName: Reference Driver
   CarNumber: 10
   CarClassID: 4098
 - CarIdx: 11
   UserName: Grid Leader
   CarNumber: 11
   CarClassID: 4098
QualifyResultsInfo:
 Results:
 - Position: 0
   ClassPosition: 0
   CarIdx: 11
 - Position: 1
   ClassPosition: 1
   CarIdx: 10
""");

        store.RecordFrame(CreateSample(
            sessionState: 3,
            playerCarIdx: 10,
            focusCarIdx: 10,
            focusLapDistPct: 0.50d,
            focusEstimatedTimeSeconds: 50d,
            teamCarClass: 4098,
            teamLapCompleted: -1,
            teamLapDistPct: 0.50d,
            teamEstimatedTimeSeconds: 50d,
            allCars:
            [
                Car(11, position: 0, classPosition: 0, lapDistPct: 0.53d, f2TimeSeconds: 0d, estimatedTimeSeconds: 53d),
                Car(10, position: 0, classPosition: 0, lapDistPct: 0.50d, f2TimeSeconds: 0d, estimatedTimeSeconds: 50d)
            ],
            onPitRoad: false,
            playerCarInPitStall: false,
            teamOnPitRoad: false,
            playerTrackSurface: 3));

        store.RecordFrame(CreateSample(
            sessionState: 3,
            playerCarIdx: 10,
            focusCarIdx: 10,
            focusLapDistPct: 0.012d,
            focusEstimatedTimeSeconds: 4.6d,
            teamCarClass: 4098,
            teamLapCompleted: -1,
            teamLapDistPct: 0.012d,
            teamEstimatedTimeSeconds: 4.6d,
            allCars:
            [
                Car(11, position: 0, classPosition: 0, lapDistPct: 0.53d, f2TimeSeconds: 0d, estimatedTimeSeconds: 53d),
                Car(10, position: 0, classPosition: 0, lapDistPct: 0.012d, f2TimeSeconds: 0d, estimatedTimeSeconds: 4.6d)
            ],
            onPitRoad: true,
            playerCarInPitStall: true,
            teamOnPitRoad: true,
            playerTrackSurface: 1));

        var snapshot = store.Snapshot();
        var models = snapshot.Models;

        Assert.Equal(LiveScoringSource.StartingGrid, models.Scoring.Source);
        Assert.Null(models.Timing.FocusRow?.GapSecondsToClassLeader);
        Assert.False(models.Timing.FocusRow?.HasTakenGrid == true);
        var focusDelta = models.Timing.FocusRow?.DeltaSecondsToFocus;
        Assert.NotNull(focusDelta);
        Assert.Equal(0d, focusDelta.Value, precision: 6);
        var relativeRow = Assert.Single(models.Relative.Rows);
        Assert.Equal(11, relativeRow.CarIdx);
        Assert.True(relativeRow.IsBehind);
        Assert.Equal(-41.6d, relativeRow.RelativeSeconds!.Value, precision: 3);
        var standings = StandingsOverlayViewModel.From(snapshot, DateTimeOffset.UtcNow);
        Assert.Equal("source: starting grid", standings.Source);
        var playerRow = Assert.Single(standings.Rows, row => row.IsReference);
        Assert.Equal("IN", playerRow.Pit);
        Assert.True(playerRow.IsPendingGrid);
    }

    [Fact]
    public void RecordFrame_UsesNumericClassLabelsWhenSdkClassNamesAreBlank()
    {
        var store = new LiveTelemetryStore();
        store.ApplySessionInfo("""
WeekendInfo:
 TrackDisplayName: Test Circuit
 TrackLength: 5.100
 EventType: Race
SessionInfo:
 CurrentSessionNum: 0
 Sessions:
 - SessionNum: 0
   SessionType: Race
   SessionName: Race
DriverInfo:
 DriverCarIdx: 10
 Drivers:
 - CarIdx: 10
   UserName: GT3 Reference
   CarNumber: 10
   CarClassID: 4098
   CarClassShortName:
   CarClassColor: 0xffda59
   CarScreenNameShort: BMW M4 GT3
   CarScreenName: BMW M4 GT3
   CarPath: bmwm4gt3
 - CarIdx: 11
   UserName: GT3 Other
   CarNumber: 11
   CarClassID: 4098
   CarClassShortName:
   CarClassColor: 0xffda59
   CarScreenNameShort: Ferrari 296 GT3
   CarScreenName: Ferrari 296 GT3
   CarPath: ferrari296gt3
 - CarIdx: 20
   UserName: TCR Reference
   CarNumber: 20
   CarClassID: 4101
   CarClassShortName:
   CarClassColor: 0xae6bff
   CarScreenNameShort: Honda Civic Type R TCR
   CarScreenName: Honda Civic Type R TCR
   CarPath: hondacivictyper
 - CarIdx: 21
   UserName: TCR Other
   CarNumber: 21
   CarClassID: 4101
   CarClassShortName:
   CarClassColor: 0xae6bff
   CarScreenNameShort: Hyundai Elantra N TCR
   CarScreenName: Hyundai Elantra N TCR
   CarPath: hyundaielantrantcr
 - CarIdx: 30
   UserName: M2 Single
   CarNumber: 30
   CarClassID: 4102
   CarClassShortName:
   CarClassColor: 0x53ff77
   CarScreenNameShort: BMW M2 CS Racing
   CarScreenName: BMW M2 CS Racing
   CarPath: bmwm2csracing
QualifyResultsInfo:
 Results:
 - Position: 0
   ClassPosition: 0
   CarIdx: 10
 - Position: 1
   ClassPosition: 1
   CarIdx: 11
 - Position: 2
   ClassPosition: 0
   CarIdx: 20
 - Position: 3
   ClassPosition: 1
   CarIdx: 21
 - Position: 4
   ClassPosition: 0
   CarIdx: 30
""");

        store.RecordFrame(CreateSample(
            sessionState: 3,
            playerCarIdx: 10,
            focusCarIdx: 10,
            focusCarClass: 4098,
            teamCarClass: 4098));

        var models = store.Snapshot().Models;

        Assert.Null(models.DriverDirectory.Drivers.Single(driver => driver.CarIdx == 10).CarClassName);
        Assert.Null(models.DriverDirectory.Drivers.Single(driver => driver.CarIdx == 20).CarClassName);
        Assert.Null(models.DriverDirectory.Drivers.Single(driver => driver.CarIdx == 30).CarClassName);
        Assert.Contains(models.Scoring.ClassGroups, group => group.CarClass == 4098 && group.ClassName == "Class 4098");
        Assert.Contains(models.Scoring.ClassGroups, group => group.CarClass == 4101 && group.ClassName == "Class 4101");
        Assert.Contains(models.Scoring.ClassGroups, group => group.CarClass == 4102 && group.ClassName == "Class 4102");
    }

    [Fact]
    public void RecordFrame_DoesNotTreatAllZeroF2TimingAsReliableRaceGap()
    {
        var store = new LiveTelemetryStore();

        store.RecordFrame(CreateSample(
            sessionState: 4,
            playerCarIdx: 10,
            focusCarIdx: 10,
            focusLapDistPct: null,
            focusEstimatedTimeSeconds: 0d,
            teamEstimatedTimeSeconds: 0d,
            teamPosition: 2,
            teamClassPosition: 2,
            classLeaderCarIdx: 11,
            classLeaderF2TimeSeconds: 0d,
            focusClassCars:
            [
                Car(11, position: 1, classPosition: 1, lapDistPct: 0.20d, f2TimeSeconds: 0d),
                Car(10, position: 2, classPosition: 2, lapDistPct: 0.19d, f2TimeSeconds: 0d)
            ]));

        var models = store.Snapshot().Models;

        Assert.False(models.Timing.ClassLeaderGapEvidence.IsUsable);
        Assert.Equal("gap_signals_missing", models.Timing.ClassLeaderGapEvidence.MissingReason);
        Assert.Null(models.Timing.FocusRow?.GapSecondsToClassLeader);
    }

    [Fact]
    public void RecordFrame_MarksTimingOnlyRowsAsUnavailableForSpatialAndRadarPlacement()
    {
        var store = new LiveTelemetryStore();

        store.RecordFrame(CreateSample(
            playerCarIdx: 10,
            teamLapDistPct: 0.50d,
            teamEstimatedTimeSeconds: 50d,
            teamCarClass: 4098,
            teamClassPosition: 3,
            classLeaderCarIdx: 11,
            classLeaderLapDistPct: 0.53d,
            classLeaderF2TimeSeconds: 0d,
            focusClassCars:
            [
                new HistoricalCarProximity(
                    CarIdx: 23,
                    LapCompleted: -1,
                    LapDistPct: -1d,
                    F2TimeSeconds: 62.75d,
                    EstimatedTimeSeconds: null,
                    Position: 9,
                    ClassPosition: 7,
                    CarClass: 4098,
                    TrackSurface: null,
                    OnPitRoad: null)
            ]));

        var row = Assert.Single(store.Snapshot().Models.Timing.ClassRows, row => row.CarIdx == 23);

        Assert.True(row.HasTiming);
        Assert.True(row.TimingEvidence.IsUsable);
        Assert.False(row.HasSpatialProgress);
        Assert.False(row.SpatialEvidence.IsUsable);
        Assert.Equal("lap_progress_missing", row.SpatialEvidence.MissingReason);
        Assert.False(row.CanUseForRadarPlacement);
        Assert.False(row.RadarPlacementEvidence.IsUsable);
    }

    [Fact]
    public void RecordFrame_InferRelativeOverlaySecondsWithoutChangingRadarTiming()
    {
        var store = new LiveTelemetryStore();

        store.RecordFrame(CreateSample(
            playerCarIdx: 10,
            teamLapDistPct: 0.50d,
            teamCarClass: 4098,
            nearbyCars:
            [
                new HistoricalCarProximity(
                    CarIdx: 12,
                    LapCompleted: 2,
                    LapDistPct: 0.53d,
                    F2TimeSeconds: null,
                    EstimatedTimeSeconds: null,
                    Position: 8,
                    ClassPosition: 4,
                    CarClass: 4098,
                    TrackSurface: 3,
                    OnPitRoad: false)
            ]));

        var snapshot = store.Snapshot();

        var proximityCar = Assert.Single(snapshot.Proximity.NearbyCars);
        Assert.Null(proximityCar.RelativeSeconds);

        var relativeRow = Assert.Single(snapshot.Models.Relative.Rows);
        Assert.Equal(2.7d, relativeRow.RelativeSeconds!.Value, precision: 6);
        Assert.Equal("CarIdxLapDistPct+lap-time", relativeRow.TimingEvidence.Source);

        Assert.Empty(snapshot.Models.Spatial.Cars);

        var parity = LiveModelParityAnalyzer.Analyze(snapshot);
        Assert.False(parity.HasMismatch);
    }

    [Fact]
    public void RecordFrame_KeepsPitRoadCarsInRelativeButOutOfSpatial()
    {
        var store = new LiveTelemetryStore();

        store.RecordFrame(CreateSample(
            playerCarIdx: 10,
            teamLapCompleted: 2,
            teamLapDistPct: 0.50d,
            teamCarClass: 4098,
            nearbyCars:
            [
                new HistoricalCarProximity(
                    CarIdx: 12,
                    LapCompleted: 1,
                    LapDistPct: 0.49d,
                    F2TimeSeconds: null,
                    EstimatedTimeSeconds: null,
                    Position: 8,
                    ClassPosition: 4,
                    CarClass: 4098,
                    TrackSurface: 1,
                    OnPitRoad: true)
            ]));

        var snapshot = store.Snapshot();

        Assert.Empty(snapshot.Proximity.NearbyCars);
        Assert.Empty(snapshot.Models.Spatial.Cars);
        var relativeRow = Assert.Single(snapshot.Models.Relative.Rows);
        Assert.Equal(12, relativeRow.CarIdx);
        Assert.True(relativeRow.OnPitRoad);
    }

    [Fact]
    public void RecordFrame_FlagsFuelUseWithoutFuelLevelAsDiagnosticOnly()
    {
        var store = new LiveTelemetryStore();

        store.RecordFrame(CreateSample(
            fuelLevelLiters: 0d,
            fuelLevelPercent: 0d,
            fuelUsePerHourKg: 60d));

        var fuelPit = store.Snapshot().Models.FuelPit;

        Assert.False(fuelPit.Fuel.HasValidFuel);
        Assert.False(fuelPit.FuelLevelEvidence.IsUsable);
        Assert.Equal("missing_or_zero_fuel_level", fuelPit.FuelLevelEvidence.MissingReason);
        Assert.False(fuelPit.InstantaneousBurnEvidence.IsUsable);
        Assert.Equal("fuel_level_invalid", fuelPit.InstantaneousBurnEvidence.MissingReason);
        Assert.False(fuelPit.MeasuredBurnEvidence.IsUsable);
    }

    [Fact]
    public void RecordFrame_MarksClassGapPartialWhenLeaderF2IsMissing()
    {
        var store = new LiveTelemetryStore();

        store.RecordFrame(CreateSample(
            playerCarIdx: 10,
            teamLapDistPct: 0.50d,
            focusF2TimeSeconds: 125d,
            teamCarClass: 4098,
            teamClassPosition: 7,
            classLeaderCarIdx: 11,
            classLeaderF2TimeSeconds: null));

        var models = store.Snapshot().Models;

        Assert.False(models.Timing.ClassLeaderGapEvidence.IsUsable);
        Assert.Equal("CarIdxF2Time", models.Timing.ClassLeaderGapEvidence.Source);
        Assert.Equal("leader_f2_time_missing", models.Timing.ClassLeaderGapEvidence.MissingReason);
        Assert.Equal("leader_f2_time_missing", models.Timing.FocusRow!.GapEvidence.MissingReason);
    }

    [Fact]
    public void RecordFrame_InvertsClutchRawForControlInput()
    {
        var store = new LiveTelemetryStore();

        store.RecordFrame(CreateSample(
            playerCarIdx: 10,
            brake: 1d,
            clutch: 0d,
            clutchRaw: 0.82d));

        var inputs = store.Snapshot().Models.Inputs;

        Assert.Equal(1d, inputs.Brake);
        Assert.NotNull(inputs.Clutch);
        Assert.Equal(0.18d, inputs.Clutch.Value, precision: 6);
    }

    [Fact]
    public void RecordFrame_InvertsNormalizedClutchWhenRawIsMissing()
    {
        var store = new LiveTelemetryStore();

        store.RecordFrame(CreateSample(
            playerCarIdx: 10,
            clutch: 1d,
            clutchRaw: null));

        var inputs = store.Snapshot().Models.Inputs;

        Assert.Equal(0d, inputs.Clutch);
    }

    [Fact]
    public void LiveModelParityAnalyzer_HasNoMismatchesForCurrentOverlayInputs()
    {
        var store = new LiveTelemetryStore();

        store.RecordFrame(CreateSample(
            playerCarIdx: 10,
            teamLapDistPct: 0.50d,
            teamEstimatedTimeSeconds: 50d,
            teamCarClass: 4098,
            teamPosition: 7,
            teamClassPosition: 3,
            classLeaderCarIdx: 11,
            classLeaderLapDistPct: 0.53d,
            classLeaderF2TimeSeconds: 0d,
            focusClassCars:
            [
                new HistoricalCarProximity(
                    CarIdx: 11,
                    LapCompleted: 2,
                    LapDistPct: 0.53d,
                    F2TimeSeconds: 0d,
                    EstimatedTimeSeconds: 53d,
                    Position: 1,
                    ClassPosition: 1,
                    CarClass: 4098,
                    TrackSurface: 3,
                    OnPitRoad: false),
                new HistoricalCarProximity(
                    CarIdx: 12,
                    LapCompleted: 2,
                    LapDistPct: 0.49d,
                    F2TimeSeconds: 4d,
                    EstimatedTimeSeconds: 49d,
                    Position: 8,
                    ClassPosition: 4,
                    CarClass: 4098,
                    TrackSurface: 3,
                    OnPitRoad: false)
            ],
            nearbyCars:
            [
                new HistoricalCarProximity(
                    CarIdx: 12,
                    LapCompleted: 2,
                    LapDistPct: 0.49d,
                    F2TimeSeconds: 4d,
                    EstimatedTimeSeconds: 49d,
                    Position: 8,
                    ClassPosition: 4,
                    CarClass: 4098,
                    TrackSurface: 3,
                    OnPitRoad: false)
            ]));

        var parity = LiveModelParityAnalyzer.Analyze(store.Snapshot());

        Assert.False(parity.HasMismatch);
        Assert.Empty(parity.Observations);
        Assert.True(parity.Coverage.HasLegacyFuel);
        Assert.True(parity.Coverage.HasModelTiming);
    }

    [Fact]
    public void LiveModelParityAnalyzer_IgnoresLegacyMissingTimingZeroGaps()
    {
        var sample = CreateSample();
        var classLeader = new LiveTimingRow(
            CarIdx: 11,
            Quality: LiveModelQuality.Reliable,
            Source: "test",
            IsPlayer: false,
            IsFocus: false,
            IsOverallLeader: false,
            IsClassLeader: true,
            HasTiming: true,
            HasSpatialProgress: true,
            CanUseForRadarPlacement: true,
            TimingEvidence: LiveSignalEvidence.Reliable("test"),
            SpatialEvidence: LiveSignalEvidence.Reliable("test"),
            RadarPlacementEvidence: LiveSignalEvidence.Reliable("test"),
            GapEvidence: LiveSignalEvidence.Inferred("all-cars-f2"),
            DriverName: "Class Leader",
            TeamName: null,
            CarNumber: "11",
            CarClassName: "GT3",
            CarClassColorHex: "#FFDA59",
            OverallPosition: 1,
            ClassPosition: 1,
            CarClass: 4098,
            LapCompleted: 2,
            LapDistPct: 0.5d,
            ProgressLaps: 2.5d,
            F2TimeSeconds: 0d,
            EstimatedTimeSeconds: null,
            LastLapTimeSeconds: null,
            BestLapTimeSeconds: null,
            GapSecondsToClassLeader: 0d,
            GapLapsToClassLeader: null,
            IntervalSecondsToPreviousClassRow: null,
            IntervalLapsToPreviousClassRow: null,
            DeltaSecondsToFocus: 0d,
            TrackSurface: 3,
            OnPitRoad: false);
        var snapshot = LiveTelemetrySnapshot.Empty with
        {
            IsConnected = true,
            IsCollecting = true,
            LastUpdatedAtUtc = sample.CapturedAtUtc,
            LatestSample = sample,
            LeaderGap = new LiveLeaderGapSnapshot(
                HasData: true,
                ReferenceOverallPosition: 1,
                ReferenceClassPosition: 1,
                OverallLeaderCarIdx: 11,
                ClassLeaderCarIdx: 11,
                OverallLeaderGap: new LiveGapValue(true, true, null, null, "legacy"),
                ClassLeaderGap: new LiveGapValue(true, true, null, null, "legacy"),
                ClassCars:
                [
                    new LiveClassGapCar(
                        CarIdx: 11,
                        IsReferenceCar: true,
                        IsClassLeader: true,
                        ClassPosition: 1,
                        GapSecondsToClassLeader: null,
                        GapLapsToClassLeader: null,
                        DeltaSecondsToReference: null)
                ]),
            Models = LiveRaceModels.Empty with
            {
                Timing = LiveTimingModel.Empty with
                {
                    HasData = true,
                    Quality = LiveModelQuality.Reliable,
                    FocusCarIdx = 11,
                    OverallLeaderCarIdx = 11,
                    ClassLeaderCarIdx = 11,
                    FocusRow = classLeader,
                    OverallRows = [classLeader],
                    ClassRows = [classLeader]
                }
            }
        };

        var parity = LiveModelParityAnalyzer.Analyze(snapshot);

        Assert.DoesNotContain(parity.Observations, observation =>
            observation.Family == "timing"
            && (observation.Key.StartsWith("gap-seconds-", StringComparison.Ordinal)
                || observation.Key.StartsWith("delta-to-focus-", StringComparison.Ordinal)));
    }

    [Fact]
    public void LiveModelParityAnalyzer_ReportsModelMismatch()
    {
        var store = new LiveTelemetryStore();
        store.RecordFrame(CreateSample());
        var snapshot = store.Snapshot();
        var mismatched = snapshot with
        {
            Models = snapshot.Models with
            {
                Weather = snapshot.Models.Weather with
                {
                    TrackWetness = 6
                }
            }
        };

        var parity = LiveModelParityAnalyzer.Analyze(mismatched);

        Assert.True(parity.HasMismatch);
        Assert.Contains(parity.Observations, observation =>
            observation.Family == "weather"
            && observation.Key == "track-wetness"
            && observation.LegacyValue == "1"
            && observation.ModelValue == "6");
    }

    [Fact]
    public void RecordFrame_KeepsModelsBackwardCompatibleWhenTimingIsUnavailable()
    {
        var store = new LiveTelemetryStore();

        store.RecordFrame(CreateSample(
            fuelLevelLiters: 0d,
            playerCarIdx: null,
            teamLapDistPct: null,
            teamEstimatedTimeSeconds: null,
            nearbyCars: null));

        var models = store.Snapshot().Models;

        Assert.False(models.Timing.HasData);
        Assert.Equal(LiveModelQuality.Unavailable, models.Timing.Quality);
        Assert.False(models.Relative.HasData);
        Assert.False(models.Spatial.HasData);
        Assert.Null(models.Spatial.ReferenceCarIdx);
        Assert.Equal(LiveModelQuality.Unavailable, models.FuelPit.Quality);
        Assert.True(models.Session.HasData);
    }

    [Fact]
    public void RecordFrame_DoesNotPromotePlayerToVisualFocusWhenCameraFocusIsUnavailable()
    {
        var store = new LiveTelemetryStore();

        store.RecordFrame(CreateSample(
            playerCarIdx: 10,
            teamLapDistPct: 0.50d,
            teamEstimatedTimeSeconds: 50d,
            teamCarClass: 4098,
            teamPosition: 7,
            teamClassPosition: 3,
            focusUnavailable: true,
            nearbyCars:
            [
                new HistoricalCarProximity(
                    CarIdx: 12,
                    LapCompleted: 2,
                    LapDistPct: 0.49d,
                    F2TimeSeconds: 4d,
                    EstimatedTimeSeconds: 49d,
                    Position: 8,
                    ClassPosition: 4,
                    CarClass: 4098,
                    TrackSurface: 3,
                    OnPitRoad: false)
            ]));

        var models = store.Snapshot().Models;

        Assert.Equal(10, models.DriverDirectory.PlayerCarIdx);
        Assert.Null(models.DriverDirectory.FocusCarIdx);
        Assert.NotNull(models.Timing.PlayerRow);
        Assert.Null(models.Timing.FocusRow);
        Assert.Null(models.Relative.ReferenceCarIdx);
        Assert.Equal(10, models.Spatial.ReferenceCarIdx);
    }

    [Fact]
    public void TimingColumnRegistry_ProvidesReusableDefaultColumns()
    {
        var keys = TimingColumnRegistry.All.Select(column => column.Key).ToArray();

        Assert.Equal(keys.Length, keys.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains(TimingColumnRegistry.Driver, keys);
        Assert.Contains(TimingColumnRegistry.Gap, keys);
        Assert.Contains(TimingColumnRegistry.Interval, keys);
        Assert.Contains(TimingColumnRegistry.Pit, keys);

        var gapColumn = Assert.Single(TimingColumnRegistry.All, column => column.Key == TimingColumnRegistry.Gap);
        var row = new LiveTimingRow(
            CarIdx: 10,
            Quality: LiveModelQuality.Reliable,
            Source: "test",
            IsPlayer: true,
            IsFocus: true,
            IsOverallLeader: false,
            IsClassLeader: false,
            HasTiming: true,
            HasSpatialProgress: true,
            CanUseForRadarPlacement: true,
            TimingEvidence: LiveSignalEvidence.Reliable("test"),
            SpatialEvidence: LiveSignalEvidence.Reliable("test"),
            RadarPlacementEvidence: LiveSignalEvidence.Reliable("test"),
            GapEvidence: LiveSignalEvidence.Reliable("test"),
            DriverName: "Driver One",
            TeamName: "Team One",
            CarNumber: "71",
            CarClassName: "GT3",
            CarClassColorHex: "#FFDA59",
            OverallPosition: 7,
            ClassPosition: 3,
            CarClass: 4098,
            LapCompleted: 2,
            LapDistPct: 0.5d,
            ProgressLaps: 2.5d,
            F2TimeSeconds: 5d,
            EstimatedTimeSeconds: 50d,
            LastLapTimeSeconds: 90d,
            BestLapTimeSeconds: 89d,
            GapSecondsToClassLeader: 5.1234d,
            GapLapsToClassLeader: null,
            IntervalSecondsToPreviousClassRow: null,
            IntervalLapsToPreviousClassRow: null,
            DeltaSecondsToFocus: 0d,
            TrackSurface: 3,
            OnPitRoad: false);

        Assert.Equal("5.123", gapColumn.FormatValue(row));
    }

    private static HistoricalTelemetrySample CreateSample(
        DateTimeOffset? capturedAtUtc = null,
        double sessionTime = 123d,
        int lapCompleted = 2,
        double fuelLevelLiters = 42d,
        double fuelLevelPercent = 0.4d,
        double fuelUsePerHourKg = 60d,
        int playerTireCompound = 0,
        int? playerCarIdx = null,
        double? teamLapDistPct = null,
        int? teamLapCompleted = null,
        double? focusF2TimeSeconds = null,
        double? teamEstimatedTimeSeconds = null,
        double? teamF2TimeSeconds = null,
        int? teamCarClass = null,
        int? teamPosition = null,
        int? teamClassPosition = null,
        int? teamTireCompound = null,
        double? teamLastLapTimeSeconds = null,
        int? focusCarIdx = null,
        double? focusLapDistPct = null,
        double? focusEstimatedTimeSeconds = null,
        int? focusCarClass = null,
        int? focusTireCompound = null,
        int? classLeaderCarIdx = null,
        double? classLeaderLapDistPct = null,
        double? classLeaderF2TimeSeconds = null,
        double? classLeaderLastLapTimeSeconds = null,
        int? classLeaderTireCompound = null,
        int? leaderCarIdx = null,
        int? leaderLapCompleted = null,
        double? leaderLapDistPct = null,
        double? leaderLastLapTimeSeconds = null,
        int? leaderTireCompound = null,
        double? sessionTimeRemain = null,
        int? sessionState = null,
        IReadOnlyList<HistoricalCarProximity>? focusClassCars = null,
        IReadOnlyList<HistoricalCarProximity>? nearbyCars = null,
        IReadOnlyList<HistoricalCarProximity>? allCars = null,
        int? carLeftRight = null,
        bool? isGarageVisible = null,
        double? lapDeltaToSessionBestLapSeconds = null,
        bool? lapDeltaToSessionBestLapOk = null,
        bool onPitRoad = false,
        bool playerCarInPitStall = false,
        bool? teamOnPitRoad = null,
        int? playerTrackSurface = null,
        double? precipitationPercent = null,
        double? relativeHumidityPercent = null,
        double? fogLevelPercent = null,
        double? brake = null,
        double? clutch = null,
        double? clutchRaw = null,
        bool focusUnavailable = false)
    {
        var resolvedFocusCarIdx = focusUnavailable ? null : focusCarIdx ?? playerCarIdx;
        return new HistoricalTelemetrySample(
            CapturedAtUtc: capturedAtUtc ?? DateTimeOffset.UtcNow,
            SessionTime: sessionTime,
            SessionTick: 100,
            SessionInfoUpdate: 1,
            IsOnTrack: true,
            IsInGarage: false,
            OnPitRoad: onPitRoad,
            PitstopActive: false,
            PlayerCarInPitStall: playerCarInPitStall,
            FuelLevelLiters: fuelLevelLiters,
            FuelLevelPercent: fuelLevelPercent,
            FuelUsePerHourKg: fuelUsePerHourKg,
            SpeedMetersPerSecond: 50d,
            Lap: 3,
            LapCompleted: lapCompleted,
            LapDistPct: 0.5d,
            LapLastLapTimeSeconds: 90d,
            LapBestLapTimeSeconds: 89d,
            AirTempC: 20d,
            TrackTempCrewC: 24d,
            TrackWetness: 1,
            WeatherDeclaredWet: false,
            PlayerTireCompound: playerTireCompound,
            PrecipitationPercent: precipitationPercent,
            RelativeHumidityPercent: relativeHumidityPercent,
            FogLevelPercent: fogLevelPercent,
            SessionTimeRemain: sessionTimeRemain,
            SessionState: sessionState,
            IsGarageVisible: isGarageVisible,
            PlayerCarIdx: playerCarIdx,
            FocusCarIdx: resolvedFocusCarIdx,
            FocusUnavailableReason: focusUnavailable ? "cam_car_idx_missing" : null,
            FocusLapCompleted: focusLapDistPct is null ? null : 2,
            FocusLapDistPct: focusLapDistPct,
            FocusF2TimeSeconds: focusF2TimeSeconds,
            FocusEstimatedTimeSeconds: focusEstimatedTimeSeconds,
            FocusCarClass: focusCarClass,
            FocusTireCompound: focusTireCompound,
            CarLeftRight: carLeftRight,
            TeamLapCompleted: teamLapCompleted ?? (teamLapDistPct is null ? null : 2),
            TeamLapDistPct: teamLapDistPct,
            TeamEstimatedTimeSeconds: teamEstimatedTimeSeconds,
            TeamF2TimeSeconds: teamF2TimeSeconds ?? teamEstimatedTimeSeconds,
            TeamPosition: teamPosition,
            TeamClassPosition: teamClassPosition,
            TeamCarClass: teamCarClass,
            TeamTireCompound: teamTireCompound,
            TeamLastLapTimeSeconds: teamLastLapTimeSeconds,
            FocusClassLeaderCarIdx: classLeaderCarIdx,
            FocusClassLeaderLapCompleted: classLeaderLapDistPct is null ? null : 2,
            FocusClassLeaderLapDistPct: classLeaderLapDistPct,
            FocusClassLeaderF2TimeSeconds: classLeaderF2TimeSeconds,
            FocusClassLeaderLastLapTimeSeconds: classLeaderLastLapTimeSeconds,
            FocusClassLeaderTireCompound: classLeaderTireCompound,
            TeamOnPitRoad: teamOnPitRoad,
            PlayerTrackSurface: playerTrackSurface,
            LeaderCarIdx: leaderCarIdx,
            LeaderLapCompleted: leaderLapCompleted,
            LeaderLapDistPct: leaderLapDistPct,
            LeaderLastLapTimeSeconds: leaderLastLapTimeSeconds,
            LeaderTireCompound: leaderTireCompound,
            FocusClassCars: focusClassCars,
            NearbyCars: nearbyCars,
            AllCars: allCars,
            Brake: brake,
            Clutch: clutch,
            ClutchRaw: clutchRaw,
            LapDeltaToSessionBestLapSeconds: lapDeltaToSessionBestLapSeconds,
            LapDeltaToSessionBestLapOk: lapDeltaToSessionBestLapOk);
    }

    private static HistoricalCarProximity Car(
        int carIdx,
        int position,
        int classPosition,
        double lapDistPct,
        double f2TimeSeconds,
        int lapCompleted = 0,
        double? estimatedTimeSeconds = null,
        int? trackSurface = 3,
        bool? onPitRoad = false)
    {
        return new HistoricalCarProximity(
            CarIdx: carIdx,
            LapCompleted: lapCompleted,
            LapDistPct: lapDistPct,
            F2TimeSeconds: f2TimeSeconds,
            EstimatedTimeSeconds: estimatedTimeSeconds ?? f2TimeSeconds,
            Position: position,
            ClassPosition: classPosition,
            CarClass: 4098,
            TrackSurface: trackSurface,
            OnPitRoad: onPitRoad);
    }
}
