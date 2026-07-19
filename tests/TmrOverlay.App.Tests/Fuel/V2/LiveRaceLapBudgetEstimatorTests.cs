using TmrOverlay.App.Overlays.FuelCalculator;
using TmrOverlay.Core.Fuel.V2;
using TmrOverlay.Core.History;
using TmrOverlay.Core.Telemetry.Live;
using Xunit;

namespace TmrOverlay.App.Tests.Fuel.V2;

public sealed class LiveRaceLapBudgetEstimatorTests
{
    [Fact]
    public void TimedRace_PreservesDecimalProjectionAndSeparateConservativeBudget()
    {
        var budget = LiveRaceLapBudgetEstimator.Estimate(
            RaceContext("unlimited", "3600 sec"),
            RaceSession(sessionState: 4, timeRemainingSeconds: 604d),
            strategyCarProgressLaps: 0d,
            overallLeaderProgressLaps: 0d,
            classLeaderProgressLaps: null,
            racePaceSeconds: 100d,
            racePaceSource: "rolling clean pace");

        Assert.Equal(7, budget.PrimaryLapsRemaining);
        Assert.Equal(6.04d, Assert.IsType<double>(budget.PossibleLapsRemaining), precision: 6);
        Assert.Equal(6.04d, Assert.IsType<double>(budget.EstimatedFinishLap), precision: 6);
        Assert.Contains(LiveRaceLapBudgetStateFlag.BoundaryRisk, budget.StateFlags);

        var staged = FuelV2LapBudgetStaging.FromBudget(budget);
        Assert.Equal(7, staged.PrimaryLapsRemaining);
        Assert.Equal(6.04d, Assert.IsType<double>(staged.PossibleLapsRemaining), precision: 6);
        Assert.Equal(6.04d, Assert.IsType<double>(staged.EstimatedFinishLap), precision: 6);
        Assert.Equal(LiveRaceLapBudgetSource.TimedLiveClock, staged.ProjectionSource);
        Assert.Equal(LiveRaceLapBudgetSource.TimedLiveClock, staged.ActionableSource);
        Assert.Equal(LiveRaceLapBudgetConfidence.Medium, staged.Confidence);
        Assert.False(staged.CanDriveFuelAdvice);
        Assert.Contains(LiveRaceLapBudgetStateFlag.BoundaryRisk, staged.StateFlags);
    }

    [Fact]
    public void TimedRace_ContaminatedPaceCanRemainVisibleWithoutLoweringActionableBudget()
    {
        var budget = LiveRaceLapBudgetEstimator.Estimate(
            RaceContext("unlimited", "3600 sec"),
            RaceSession(sessionState: 4, timeRemainingSeconds: 500d),
            strategyCarProgressLaps: 100d,
            overallLeaderProgressLaps: 100d,
            classLeaderProgressLaps: null,
            racePaceSeconds: 100d,
            racePaceSource: "rolling contaminated pace",
            options: new LiveRaceLapBudgetOptions(
                PaceContaminated: true,
                CleanRacePaceSeconds: 80d));

        Assert.Equal(7, budget.PrimaryLapsRemaining);
        Assert.Equal(5d, Assert.IsType<double>(budget.PossibleLapsRemaining), precision: 6);
        Assert.Equal(105d, Assert.IsType<double>(budget.EstimatedFinishLap), precision: 6);
        Assert.Equal(LiveRaceLapBudgetSource.TimedLiveClockHeldCleanPace, budget.Source);
        Assert.Equal(LiveRaceLapBudgetSource.TimedLiveClock, budget.ProjectionSource);
        Assert.False(budget.CanDriveFuelAdvice);

        var staged = FuelV2LapBudgetStaging.FromBudget(budget);
        Assert.Equal(7, staged.PrimaryLapsRemaining);
        Assert.Equal(5d, Assert.IsType<double>(staged.PossibleLapsRemaining), precision: 6);
        Assert.Equal(LiveRaceLapBudgetSource.TimedLiveClock, staged.ProjectionSource);
        Assert.Equal(LiveRaceLapBudgetSource.TimedLiveClockHeldCleanPace, staged.ActionableSource);
        Assert.False(staged.CanDriveFuelAdvice);
    }

    [Fact]
    public void StagingPreservesHighConfidencePossibleAndConservativeLapBudgets()
    {
        var budget = LiveRaceLapBudgetEstimator.Estimate(
            RaceContext("unlimited", "3600 sec"),
            RaceSession(sessionState: 4, timeRemainingSeconds: 550d),
            strategyCarProgressLaps: 100d,
            overallLeaderProgressLaps: 100d,
            classLeaderProgressLaps: null,
            racePaceSeconds: 100d,
            racePaceSource: "rolling clean pace");

        var staged = FuelV2LapBudgetStaging.FromBudget(budget);

        Assert.Equal(6, staged.PrimaryLapsRemaining);
        Assert.Equal(5.5d, Assert.IsType<double>(staged.PossibleLapsRemaining), precision: 6);
        Assert.Equal(105.5d, Assert.IsType<double>(staged.EstimatedFinishLap), precision: 6);
        Assert.Equal(LiveRaceLapBudgetSource.TimedLiveClock, staged.ProjectionSource);
        Assert.Equal(LiveRaceLapBudgetSource.TimedLiveClock, staged.ActionableSource);
        Assert.Equal(LiveRaceLapBudgetConfidence.High, staged.Confidence);
        Assert.True(staged.CanDriveFuelAdvice);
    }

    [Fact]
    public void TimedPreGreen_PreservesDecimalProjectionButCannotDriveAdvice()
    {
        var budget = LiveRaceLapBudgetEstimator.Estimate(
            RaceContext("unlimited", "604 sec"),
            RaceSession(sessionState: 3, timeRemainingSeconds: -1d),
            strategyCarProgressLaps: 0d,
            overallLeaderProgressLaps: 0d,
            classLeaderProgressLaps: null,
            racePaceSeconds: 100d,
            racePaceSource: "scheduled estimate");

        Assert.Equal(7, budget.PrimaryLapsRemaining);
        Assert.Equal(6.04d, Assert.IsType<double>(budget.PossibleLapsRemaining), precision: 6);
        Assert.Equal(6.04d, Assert.IsType<double>(budget.EstimatedFinishLap), precision: 6);
        Assert.False(budget.CanDriveFuelAdvice);
    }

    [Fact]
    public void MissingActiveClockAndPaceRemainExplicitlyUnavailableAfterStaging()
    {
        var budget = LiveRaceLapBudgetEstimator.Estimate(
            RaceContext("unlimited", "3600 sec"),
            RaceSession(sessionState: 4, timeRemainingSeconds: -1d),
            strategyCarProgressLaps: null,
            overallLeaderProgressLaps: null,
            classLeaderProgressLaps: null,
            racePaceSeconds: null,
            racePaceSource: "unavailable");

        var staged = FuelV2LapBudgetStaging.FromBudget(budget);

        Assert.Null(staged.PrimaryLapsRemaining);
        Assert.Null(staged.PossibleLapsRemaining);
        Assert.Null(staged.EstimatedFinishLap);
        Assert.Equal(LiveRaceLapBudgetSource.MissingActiveClock, staged.ProjectionSource);
        Assert.Equal(LiveRaceLapBudgetSource.MissingActiveClock, staged.ActionableSource);
        Assert.Equal(LiveRaceLapBudgetConfidence.Blocked, staged.Confidence);
        Assert.False(staged.CanDriveFuelAdvice);
        Assert.Contains(LiveRaceLapBudgetStateFlag.ClockMissing, staged.StateFlags);
    }

    [Fact]
    public void NativeWorkbench_PreservesTwoDecimalLapProjection()
    {
        var model = FuelLapsWorkbenchViewModel.Create();
        var section = Assert.Single(model.MetricSections, section => section.Title == "Laps Workbench");
        var row = Assert.Single(section.Rows, row => row.Label == "Dallara 45m");
        var middle = Assert.Single(row.Segments, segment => segment.Label == "Mid S1");

        Assert.StartsWith("6.04 ", middle.Value);
    }

    [Fact]
    public void NativeWorkbench_DisplaysRawProjectionWithoutReplacingItWithHeldBudget()
    {
        var budget = LiveRaceLapBudgetEstimator.Estimate(
            RaceContext("unlimited", "86400 sec"),
            RaceSession(sessionState: 4, timeRemainingSeconds: 26589.981d),
            strategyCarProgressLaps: 119.3791d,
            overallLeaderProgressLaps: 119.3791d,
            classLeaderProgressLaps: null,
            racePaceSeconds: 570.265d,
            racePaceSource: "rolling contaminated pace",
            options: new LiveRaceLapBudgetOptions(
                PaceContaminated: true,
                FrontPackPaceDisagreement: true,
                CleanRacePaceSeconds: 491.778d,
                PreviousCleanEstimatedFinishLap: 174d));

        var display = FuelLapsWorkbenchViewModel.FormatBudgetProjection(
            Assert.IsType<double>(budget.EstimatedFinishLap),
            "leader",
            budget);

        Assert.Equal("166.01 leader held degraded", display);
    }

    private static HistoricalSessionContext RaceContext(string sessionLaps, string sessionTime)
    {
        return new HistoricalSessionContext
        {
            Car = new HistoricalCarIdentity(),
            Track = new HistoricalTrackIdentity(),
            Session = new HistoricalSessionIdentity
            {
                SessionType = "Race",
                SessionLaps = sessionLaps,
                SessionTime = sessionTime
            },
            Conditions = new HistoricalSessionInfoConditions()
        };
    }

    private static LiveSessionModel RaceSession(int sessionState, double timeRemainingSeconds)
    {
        return LiveSessionModel.Empty with
        {
            HasData = true,
            SessionType = "Race",
            SessionState = sessionState,
            SessionTimeRemainSeconds = timeRemainingSeconds
        };
    }
}
