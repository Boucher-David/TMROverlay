using TmrOverlay.Core.History;
using TmrOverlay.Core.Fuel;
using TmrOverlay.Core.Telemetry.Live;
using Xunit;

namespace TmrOverlay.App.Tests.Overlays;

public sealed class FuelStrategyCalculatorTests
{
    [Fact]
    public void From_UsesLeaderProgressForTimedRaceLapsRemaining()
    {
        var live = CreateLiveSnapshot(
            fuelLevelLiters: 100d,
            fuelUsePerHourKg: 75d,
            teamLapCompleted: 4,
            teamLapDistPct: 0.2d,
            leaderLapCompleted: 4,
            leaderLapDistPct: 0.5d,
            sessionTimeRemain: 250d,
            teamLastLapTimeSeconds: 100d,
            measuredFuelPerLapLiters: 2.778d);

        var strategy = FuelStrategyCalculator.From(live, EmptyHistory(live.Combo));

        Assert.NotNull(strategy.RaceLapsRemaining);
        Assert.Equal(2.8d, strategy.RaceLapsRemaining.Value, precision: 3);
        Assert.Equal(2.778d, strategy.FuelPerLapLiters!.Value, precision: 3);
        Assert.Equal("fuel covers finish", strategy.Status);
        Assert.Equal(2.8d, Assert.Single(strategy.Stints).LengthLaps, precision: 3);
        Assert.Equal("finish", strategy.Stints[0].Source);
        Assert.Null(strategy.Stints[0].TireAdvice);
    }

    [Fact]
    public void LiveFuelStrategyModel_UsesExistingStrategyWhenLocalContextIsAvailable()
    {
        var live = CreateLiveSnapshot(
            fuelLevelLiters: 100d,
            fuelUsePerHourKg: 75d,
            teamLapCompleted: 4,
            teamLapDistPct: 0.2d,
            leaderLapCompleted: 4,
            leaderLapDistPct: 0.5d,
            sessionTimeRemain: 250d,
            teamLastLapTimeSeconds: 100d,
            measuredFuelPerLapLiters: 2.778d);
        var now = live.LastUpdatedAtUtc ?? DateTimeOffset.UtcNow;

        var model = LiveFuelStrategyModel.From(live, now, combo => EmptyHistory(combo));

        Assert.True(model.IsAvailable);
        Assert.Equal("fuel covers finish", model.Status);
        Assert.NotNull(model.Strategy);
        Assert.Equal(2.8d, model.Strategy.RaceLapsRemaining!.Value, precision: 3);
    }

    [Fact]
    public void LiveFuelStrategyModel_DoesNotLookupHistoryWhenLocalContextIsUnavailable()
    {
        var live = CreateLiveSnapshot(
            fuelLevelLiters: 100d,
            fuelUsePerHourKg: 75d,
            teamLapCompleted: 4,
            teamLapDistPct: 0.2d,
            leaderLapCompleted: 4,
            leaderLapDistPct: 0.5d,
            sessionTimeRemain: 250d,
            teamLastLapTimeSeconds: 100d) with
        {
            IsConnected = false,
            Models = LiveRaceModels.Empty
        };
        var now = live.LastUpdatedAtUtc ?? DateTimeOffset.UtcNow;
        var lookupCount = 0;

        var model = LiveFuelStrategyModel.From(live, now, combo =>
        {
            lookupCount++;
            return EmptyHistory(combo);
        });

        Assert.False(model.IsAvailable);
        Assert.Null(model.Strategy);
        Assert.Equal(0, lookupCount);
    }

    [Fact]
    public void From_UsesOverallLeaderPaceForTimedRaceProjection()
    {
        var live = CreateLiveSnapshot(
            fuelLevelLiters: 100d,
            fuelUsePerHourKg: 75d,
            teamLapCompleted: 4,
            teamLapDistPct: 0.2d,
            leaderLapCompleted: 4,
            leaderLapDistPct: 0.5d,
            sessionTimeRemain: 250d,
            teamLastLapTimeSeconds: 100d,
            leaderLastLapTimeSeconds: 80d,
            measuredFuelPerLapLiters: 2.778d);

        var strategy = FuelStrategyCalculator.From(live, EmptyHistory(live.Combo));

        Assert.Equal(3.8d, strategy.RaceLapsRemaining!.Value, precision: 3);
        Assert.Equal("overall leader last lap", strategy.RacePaceSource);
        Assert.Equal("timed race by overall leader last lap", strategy.RaceLapEstimateSource);
        Assert.Equal(0.3d, strategy.OverallLeaderGapLaps!.Value, precision: 3);
    }

    [Fact]
    public void From_FallsBackToHistoricalFuelPerLapWhenLiveBurnUnavailable()
    {
        var live = CreateLiveSnapshot(
            fuelLevelLiters: 50d,
            fuelUsePerHourKg: 75d,
            teamLastLapTimeSeconds: 100d,
            sessionTimeRemain: 1_000d);
        var aggregate = new HistoricalSessionAggregate
        {
            BaselineSessionCount = 1,
            FuelPerLapLiters = new RunningHistoricalMetric
            {
                SampleCount = 1,
                Mean = 10d,
                Minimum = 10d,
                Maximum = 10d
            }
        };

        var strategy = FuelStrategyCalculator.From(live, new SessionHistoryLookupResult(live.Combo, null, aggregate));

        Assert.Equal(10d, strategy.FuelPerLapLiters);
        Assert.Equal("baseline history", strategy.FuelPerLapSource);
        Assert.Equal(50d, strategy.AdditionalFuelNeededLiters);
        Assert.Equal(2, strategy.PlannedStintCount);
        Assert.Equal(1, strategy.PlannedStopCount);
        Assert.Equal("2 stints / 1 stop", strategy.Status);
    }

    [Fact]
    public void From_DoesNotUseInstantaneousFuelUseAsStrategyBurn()
    {
        var live = CreateLiveSnapshot(
            fuelLevelLiters: 50d,
            fuelUsePerHourKg: 75d,
            teamLastLapTimeSeconds: 100d,
            sessionTimeRemain: 1_000d);

        var strategy = FuelStrategyCalculator.From(live, EmptyHistory(live.Combo));

        Assert.Equal(50d, strategy.CurrentFuelLiters);
        Assert.Equal(100d, strategy.FuelPerHourLiters);
        Assert.Null(strategy.FuelPerLapLiters);
        Assert.Equal("unavailable", strategy.FuelPerLapSource);
        Assert.Equal("waiting for burn", strategy.Status);
        Assert.Empty(strategy.Stints);
    }

    [Fact]
    public void From_UsesModelV2FuelAndRaceProgressWhenLegacySlicesAreUnavailable()
    {
        var live = CreateLiveSnapshot(
            fuelLevelLiters: 80d,
            fuelUsePerHourKg: 75d,
            teamLapCompleted: 4,
            teamLapDistPct: 0.2d,
            leaderLapCompleted: 4,
            leaderLapDistPct: 0.5d,
            sessionTimeRemain: 250d,
            teamLastLapTimeSeconds: 100d) with
        {
            Fuel = LiveFuelSnapshot.Unavailable
        };

        var strategy = FuelStrategyCalculator.From(live, EmptyHistory(live.Combo));

        Assert.Equal(80d, strategy.CurrentFuelLiters);
        Assert.Equal(2.8d, strategy.RaceLapsRemaining!.Value, precision: 3);
        Assert.Equal(0.3d, strategy.OverallLeaderGapLaps!.Value, precision: 3);
        Assert.Equal(2, strategy.TeamOverallPosition);
    }

    [Fact]
    public void From_CompletesV2FuelPitAndRaceProgressFromLatestSampleWhenModelsAreMissing()
    {
        var live = CreateLiveSnapshot(
            fuelLevelLiters: 72d,
            fuelUsePerHourKg: 75d,
            teamLapCompleted: 4,
            teamLapDistPct: 0.2d,
            leaderLapCompleted: 4,
            leaderLapDistPct: 0.5d,
            sessionTimeRemain: 250d,
            teamLastLapTimeSeconds: 100d) with
        {
            Models = LiveRaceModels.Empty
        };

        var strategy = FuelStrategyCalculator.From(live, EmptyHistory(live.Combo));

        Assert.Equal(72d, strategy.CurrentFuelLiters);
        Assert.Equal("unavailable", strategy.FuelPerLapSource);
        Assert.Null(strategy.FuelPerLapLiters);
        Assert.Equal(2.8d, strategy.RaceLapsRemaining!.Value, precision: 3);
        Assert.Equal(0.3d, strategy.OverallLeaderGapLaps!.Value, precision: 3);
        Assert.Equal(2, strategy.TeamOverallPosition);
    }

    [Fact]
    public void From_UsesFourHourHistoryToPlanEightLapTargets()
    {
        var live = CreateLiveSnapshot(
            fuelLevelLiters: 106d,
            fuelUsePerHourKg: 0d,
            sessionTimeRemain: 14_400d,
            teamLastLapTimeSeconds: 482.092804d,
            fuelMaxLiters: 106d,
            estimatedLapSeconds: 465.166d);
        var aggregate = new HistoricalSessionAggregate
        {
            BaselineSessionCount = 1,
            Car = live.Context.Car,
            FuelPerLapLiters = new RunningHistoricalMetric
            {
                SampleCount = 1,
                Mean = 13.36344d,
                Minimum = 13.36344d,
                Maximum = 13.36344d
            },
            MedianLapSeconds = new RunningHistoricalMetric
            {
                SampleCount = 1,
                Mean = 482.092804d,
                Minimum = 482.092804d,
                Maximum = 482.092804d
            },
            ObservedFuelFillRateLitersPerSecond = new RunningHistoricalMetric
            {
                SampleCount = 1,
                Mean = 2.68d,
                Minimum = 2.68d,
                Maximum = 2.68d
            },
            AveragePitLaneSeconds = new RunningHistoricalMetric
            {
                SampleCount = 1,
                Mean = 64.822333d,
                Minimum = 64.822333d,
                Maximum = 64.822333d
            },
            AverageTireChangePitServiceSeconds = new RunningHistoricalMetric
            {
                SampleCount = 1,
                Mean = 39.2d,
                Minimum = 39.2d,
                Maximum = 39.2d
            }
        };

        var strategy = FuelStrategyCalculator.From(live, new SessionHistoryLookupResult(live.Combo, null, aggregate));

        Assert.Equal(30, strategy.PlannedRaceLaps);
        Assert.Equal(4, strategy.PlannedStintCount);
        Assert.Equal(3, strategy.PlannedStopCount);
        Assert.Equal(7, strategy.FinalStintTargetLaps);
        Assert.Equal("4 stints / 3 stops", strategy.Status);
        Assert.Equal(new int?[] { 8, 8, 7, 7 }, strategy.Stints.Select(stint => stint.TargetLaps).ToArray());
        Assert.Equal(13.25d, strategy.Stints[0].TargetFuelPerLapLiters!.Value, precision: 3);
        Assert.Equal(13.36344d, strategy.Stints[0].CurrentFuelPerLapLiters!.Value, precision: 5);
        Assert.Equal("baseline history", strategy.Stints[0].CurrentFuelPerLapSource);
        Assert.Null(strategy.Stints[0].TireAdvice);
        Assert.Null(strategy.RhythmComparison);
        Assert.Equal(0.113d, strategy.RequiredFuelSavingLitersPerLap!.Value, precision: 3);
        Assert.Null(strategy.StopOptimization);
    }

    [Fact]
    public void From_UsesTeammateEightLapHistoryForAlternatingTeamPlan()
    {
        var live = CreateLiveSnapshot(
            fuelLevelLiters: 106d,
            fuelUsePerHourKg: 0d,
            sessionTimeRemain: 14_400d,
            teamLastLapTimeSeconds: 482.092804d,
            fuelMaxLiters: 106d,
            estimatedLapSeconds: 465.166d);
        var aggregate = new HistoricalSessionAggregate
        {
            BaselineSessionCount = 1,
            Car = live.Context.Car,
            FuelPerLapLiters = new RunningHistoricalMetric
            {
                SampleCount = 1,
                Mean = 13.36344d,
                Minimum = 13.36344d,
                Maximum = 13.36344d
            },
            MedianLapSeconds = new RunningHistoricalMetric
            {
                SampleCount = 1,
                Mean = 482.092804d,
                Minimum = 482.092804d,
                Maximum = 482.092804d
            },
            TeammateDriverStintLaps = new RunningHistoricalMetric
            {
                SampleCount = 2,
                Mean = 8d,
                Minimum = 8d,
                Maximum = 8d
            },
            ObservedFuelFillRateLitersPerSecond = new RunningHistoricalMetric
            {
                SampleCount = 1,
                Mean = 2.68d,
                Minimum = 2.68d,
                Maximum = 2.68d
            },
            AveragePitLaneSeconds = new RunningHistoricalMetric
            {
                SampleCount = 1,
                Mean = 64.822333d,
                Minimum = 64.822333d,
                Maximum = 64.822333d
            },
            AverageTireChangePitServiceSeconds = new RunningHistoricalMetric
            {
                SampleCount = 1,
                Mean = 39.2d,
                Minimum = 39.2d,
                Maximum = 39.2d
            }
        };

        var strategy = FuelStrategyCalculator.From(live, new SessionHistoryLookupResult(live.Combo, null, aggregate));

        Assert.Equal(new int?[] { 7, 8, 7, 8 }, strategy.Stints.Select(stint => stint.TargetLaps).ToArray());
        Assert.Equal(8, strategy.TeammateStintTargetLaps);
        Assert.Equal("baseline teammate stints", strategy.TeammateStintTargetSource);
        Assert.Equal(8, strategy.FinalStintTargetLaps);
        Assert.All(strategy.Stints, stint => Assert.Null(stint.TireAdvice));
        Assert.Equal("4 stints / 3 stops", strategy.Status);
        Assert.Null(strategy.RhythmComparison);
        Assert.Equal(0.113d, strategy.RequiredFuelSavingLitersPerLap!.Value, precision: 3);
    }

    [Fact]
    public void From_HidesEnduranceRhythmOptimizationForV1()
    {
        var live = CreateLiveSnapshot(
            fuelLevelLiters: 106d,
            fuelUsePerHourKg: 0d,
            sessionTimeRemain: 86_400d,
            teamLastLapTimeSeconds: 482.092804d,
            fuelMaxLiters: 106d,
            estimatedLapSeconds: 465.166d);
        var aggregate = new HistoricalSessionAggregate
        {
            BaselineSessionCount = 1,
            Car = live.Context.Car,
            FuelPerLapLiters = new RunningHistoricalMetric
            {
                SampleCount = 1,
                Mean = 13.36344d,
                Minimum = 13.36344d,
                Maximum = 13.36344d
            },
            MedianLapSeconds = new RunningHistoricalMetric
            {
                SampleCount = 1,
                Mean = 482.092804d,
                Minimum = 482.092804d,
                Maximum = 482.092804d
            },
            TeammateDriverStintLaps = new RunningHistoricalMetric
            {
                SampleCount = 2,
                Mean = 8d,
                Minimum = 8d,
                Maximum = 8d
            },
            AveragePitLaneSeconds = new RunningHistoricalMetric
            {
                SampleCount = 1,
                Mean = 64.822333d,
                Minimum = 64.822333d,
                Maximum = 64.822333d
            }
        };

        var strategy = FuelStrategyCalculator.From(live, new SessionHistoryLookupResult(live.Combo, null, aggregate));

        Assert.Equal(180, strategy.PlannedRaceLaps);
        Assert.Equal(23, strategy.PlannedStintCount);
        Assert.Equal(22, strategy.PlannedStopCount);
        Assert.Equal("23 stints / 22 stops", strategy.Status);
        Assert.Null(strategy.RhythmComparison);
        Assert.Null(strategy.StopOptimization);
    }

    [Fact]
    public void From_RollsFutureStintRowsForwardAfterCompletedStints()
    {
        var live = CreateLiveSnapshot(
            fuelLevelLiters: 106d,
            fuelUsePerHourKg: 0d,
            teamLapCompleted: 32,
            leaderLapCompleted: 32,
            sessionTimeRemain: 86_400d - 32d * 482.092804d,
            teamLastLapTimeSeconds: 482.092804d,
            fuelMaxLiters: 106d,
            estimatedLapSeconds: 465.166d) with
        {
            CompletedStintCount = 4
        };
        var aggregate = new HistoricalSessionAggregate
        {
            BaselineSessionCount = 1,
            Car = live.Context.Car,
            FuelPerLapLiters = new RunningHistoricalMetric
            {
                SampleCount = 1,
                Mean = 13.36344d,
                Minimum = 13.36344d,
                Maximum = 13.36344d
            },
            MedianLapSeconds = new RunningHistoricalMetric
            {
                SampleCount = 1,
                Mean = 482.092804d,
                Minimum = 482.092804d,
                Maximum = 482.092804d
            },
            TeammateDriverStintLaps = new RunningHistoricalMetric
            {
                SampleCount = 2,
                Mean = 8d,
                Minimum = 8d,
                Maximum = 8d
            }
        };

        var strategy = FuelStrategyCalculator.From(live, new SessionHistoryLookupResult(live.Combo, null, aggregate));

        Assert.Equal(5, strategy.Stints[0].Number);
        Assert.Equal(6, strategy.Stints[1].Number);
        Assert.Equal(7, strategy.Stints[2].Number);
    }

    private static LiveTelemetrySnapshot CreateLiveSnapshot(
        double fuelLevelLiters,
        double fuelUsePerHourKg,
        int teamLapCompleted = 0,
        double teamLapDistPct = 0d,
        int leaderLapCompleted = 0,
        double leaderLapDistPct = 0d,
        double sessionTimeRemain = 600d,
        double teamLastLapTimeSeconds = 100d,
        double? leaderLastLapTimeSeconds = null,
        double fuelMaxLiters = 100d,
        double estimatedLapSeconds = 100d,
        double? measuredFuelPerLapLiters = null)
    {
        var context = new HistoricalSessionContext
        {
            Car = new HistoricalCarIdentity
            {
                DriverCarFuelMaxLiters = fuelMaxLiters,
                DriverCarFuelKgPerLiter = 0.75d,
                DriverCarEstLapTimeSeconds = estimatedLapSeconds
            },
            Track = new HistoricalTrackIdentity(),
            Session = new HistoricalSessionIdentity
            {
                SessionType = "Race",
                SessionTime = "3600 sec",
                SessionLaps = "unlimited"
            },
            Conditions = new HistoricalSessionInfoConditions()
        };
        var combo = HistoricalComboIdentity.From(context);
        var sample = new HistoricalTelemetrySample(
            CapturedAtUtc: DateTimeOffset.UtcNow,
            SessionTime: 120d,
            SessionTick: 100,
            SessionInfoUpdate: 1,
            IsOnTrack: true,
            IsInGarage: false,
            OnPitRoad: false,
            PitstopActive: false,
            PlayerCarInPitStall: false,
            FuelLevelLiters: fuelLevelLiters,
            FuelLevelPercent: fuelLevelLiters / 100d,
            FuelUsePerHourKg: fuelUsePerHourKg,
            SpeedMetersPerSecond: 50d,
            Lap: teamLapCompleted,
            LapCompleted: teamLapCompleted,
            LapDistPct: teamLapDistPct,
            LapLastLapTimeSeconds: teamLastLapTimeSeconds,
            LapBestLapTimeSeconds: teamLastLapTimeSeconds,
            AirTempC: 20d,
            TrackTempCrewC: 25d,
            TrackWetness: 1,
            WeatherDeclaredWet: false,
            PlayerTireCompound: 0,
            SessionTimeRemain: sessionTimeRemain,
            SessionTimeTotal: 3_600d,
            SessionLapsRemainEx: 32_767,
            SessionLapsTotal: 32_767,
            SessionState: 4,
            RaceLaps: teamLapCompleted,
            PlayerCarIdx: 15,
            FocusCarIdx: 15,
            TeamLapCompleted: teamLapCompleted,
            TeamLapDistPct: teamLapDistPct,
            TeamLastLapTimeSeconds: teamLastLapTimeSeconds,
            TeamBestLapTimeSeconds: teamLastLapTimeSeconds,
            TeamPosition: 2,
            TeamClassPosition: 2,
            LeaderCarIdx: 1,
            LeaderLapCompleted: leaderLapCompleted,
            LeaderLapDistPct: leaderLapDistPct,
            LeaderLastLapTimeSeconds: leaderLastLapTimeSeconds);

        var fuel = LiveFuelSnapshot.From(context, sample);
        if (measuredFuelPerLapLiters is { } measured)
        {
            fuel = fuel.WithMeasuredFuelPerLap(measured);
        }
        var proximity = LiveProximitySnapshot.From(context, sample);
        var leaderGap = LiveLeaderGapSnapshot.From(sample);
        var models = LiveRaceModelBuilder.From(context, sample, fuel, proximity, leaderGap);

        return new LiveTelemetrySnapshot(
            IsConnected: true,
            IsCollecting: true,
            SourceId: "test",
            StartedAtUtc: DateTimeOffset.UtcNow.AddMinutes(-2),
            LastUpdatedAtUtc: sample.CapturedAtUtc,
            Sequence: 1,
            Context: context,
            Combo: combo,
            LatestSample: sample,
            Fuel: fuel,
            Proximity: proximity,
            LeaderGap: leaderGap)
        {
            Models = models
        };
    }

    private static SessionHistoryLookupResult EmptyHistory(HistoricalComboIdentity combo)
    {
        return new SessionHistoryLookupResult(combo, UserAggregate: null, BaselineAggregate: null);
    }
}
