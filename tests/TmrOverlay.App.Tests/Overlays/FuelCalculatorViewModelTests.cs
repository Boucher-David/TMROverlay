using TmrOverlay.App.Overlays.FuelCalculator;
using TmrOverlay.App.Overlays.SimpleTelemetry;
using TmrOverlay.Core.Fuel;
using TmrOverlay.Core.History;
using TmrOverlay.Core.Overlays;
using TmrOverlay.Core.Telemetry.Live;
using Xunit;

namespace TmrOverlay.App.Tests.Overlays;

public sealed class FuelCalculatorViewModelTests
{
    [Fact]
    public void From_WhenFuelTelemetryAndHistoryAreUnavailable_HasNoStintRows()
    {
        var live = LiveTelemetrySnapshot.Empty;
        var history = SessionHistoryLookupResult.Empty(live.Combo);
        var strategy = FuelStrategyCalculator.From(live, history);

        var viewModel = FuelCalculatorViewModel.From(
            strategy,
            history,
            showAdvice: true,
            unitSystem: "Metric",
            maximumRows: 6);

        Assert.False(strategy.HasData);
        Assert.Equal("waiting for fuel", viewModel.Status);
        Assert.Empty(viewModel.Rows);
        Assert.Contains("history none", viewModel.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void From_BuildsSegmentedRaceAndStintSections()
    {
        var strategy = new FuelStrategySnapshot(
            HasData: true,
            Status: "2 stints / 1 stop",
            CurrentFuelLiters: 50d,
            FuelPercent: 0.5d,
            FuelPerLapLiters: 10d,
            FuelPerLapSource: "measured green lap",
            FuelPerLapMinimumLiters: 9.8d,
            FuelPerLapMaximumLiters: 10.2d,
            MeasuredFuelPerLapMinimumLiters: 9.8d,
            MeasuredFuelPerLapAverageLiters: 10d,
            MeasuredFuelPerLapMaximumLiters: 10.2d,
            MeasuredFuelPerLapSampleCount: 3,
            FuelPerHourLiters: null,
            LapTimeSeconds: 100d,
            LapTimeSource: "live",
            RacePaceSeconds: 100d,
            RacePaceSource: "live",
            RaceLapsRemaining: 12d,
            RaceLapEstimateSource: "timed race",
            OverallLeaderGapLaps: null,
            ClassLeaderGapLaps: null,
            TeamOverallPosition: 4,
            TeamClassPosition: 2,
            PlannedRaceLaps: 20,
            FuelToFinishLiters: 120d,
            AdditionalFuelNeededLiters: 70d,
            FullTankStintLaps: 10d,
            PlannedStintCount: 2,
            PlannedStopCount: 1,
            FinalStintTargetLaps: 2,
            RequiredFuelSavingLitersPerLap: null,
            RequiredFuelSavingPercent: null,
            StopOptimization: null,
            RhythmComparison: null,
            TeammateStintTargetLaps: null,
            TeammateStintTargetSource: null,
            TireModelSource: "history",
            FuelFillRateLitersPerSecond: null,
            TireChangeServiceSeconds: null,
            Stints:
            [
                new FuelStintEstimate(
                    Number: 1,
                    LengthLaps: 5d,
                    Source: "current",
                    TargetLaps: 5,
                    TargetFuelPerLapLiters: 10d,
                    CurrentFuelPerLapLiters: 10d,
                    CurrentFuelPerLapSource: "measured green lap",
                    RequiredFuelSavingLitersPerLap: null,
                    RequiredFuelSavingPercent: null,
                    TireAdvice: new TireChangeAdvice("tires free (50 L)", FuelToAddLiters: 50d, TimeLossSeconds: 0d)),
                new FuelStintEstimate(
                    Number: 2,
                    LengthLaps: 7d,
                    Source: "final",
                    TargetLaps: 7,
                    TargetFuelPerLapLiters: 10d,
                    CurrentFuelPerLapLiters: 10d,
                    CurrentFuelPerLapSource: "measured green lap",
                    RequiredFuelSavingLitersPerLap: null,
                    RequiredFuelSavingPercent: null,
                    TireAdvice: TireChangeAdvice.NoStop)
            ],
            SessionKind: OverlaySessionKind.Race);

        var viewModel = FuelCalculatorViewModel.From(
            strategy,
            SessionHistoryLookupResult.Empty(new HistoricalComboIdentity
            {
                CarKey = "car-test",
                TrackKey = "track-test",
                SessionKey = "race"
            }),
            showAdvice: true,
            unitSystem: "Metric",
            maximumRows: 6);

        Assert.Collection(
            viewModel.MetricSections.Select(section => section.Title),
            title => Assert.Equal("Race Information", title),
            title => Assert.Equal("Stint Targets", title));
        var plan = Assert.Single(viewModel.MetricSections[0].Rows, row => row.Label == "Plan");
        Assert.Equal(SimpleTelemetryTone.Info, plan.Tone);
        Assert.Collection(
            plan.Segments,
            segment =>
            {
                Assert.Equal("Race", segment.Label);
                Assert.Equal(SimpleTelemetryTone.Info, segment.Tone);
            },
            segment =>
            {
                Assert.Equal("Remain", segment.Label);
                Assert.Equal(SimpleTelemetryTone.Info, segment.Tone);
            },
            segment =>
            {
                Assert.Equal("Stints", segment.Label);
                Assert.Equal(SimpleTelemetryTone.Info, segment.Tone);
            },
            segment =>
            {
                Assert.Equal("Stops", segment.Label);
                Assert.Equal(SimpleTelemetryTone.Info, segment.Tone);
            },
            segment => Assert.Equal("Save", segment.Label));
        var fuel = Assert.Single(viewModel.MetricSections[0].Rows, row => row.Label == "Fuel");
        Assert.Contains(fuel.Segments, segment => segment.Label == "Tank" && segment.Tone == SimpleTelemetryTone.Info);
        Assert.Contains(fuel.Segments, segment => segment.Label == "Need" && segment.Value == "+70.0 L" && segment.Tone == SimpleTelemetryTone.Warning);
        var stint = Assert.Single(viewModel.MetricSections[1].Rows, row => row.Label == "Stint 1");
        Assert.Equal(SimpleTelemetryTone.Info, stint.Tone);
        Assert.Contains(stint.Segments, segment => segment.Label == "Laps" && segment.Tone == SimpleTelemetryTone.Info);
        Assert.Contains(stint.Segments, segment => segment.Label == "Target" && segment.Tone == SimpleTelemetryTone.Info);
        Assert.DoesNotContain(stint.Segments, segment => segment.Label == "Tires");
        Assert.DoesNotContain(viewModel.Rows, row => row.Advice.Contains("tires", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            viewModel.MetricSections.SelectMany(section => section.Rows).SelectMany(row => row.Segments),
            segment => segment.Tone == SimpleTelemetryTone.Modeled);
    }

    [Fact]
    public void From_ShowsMeasuredUsageRowsForPracticeAndQualifying()
    {
        var strategy = new FuelStrategySnapshot(
            HasData: true,
            Status: "stint estimate",
            CurrentFuelLiters: 40d,
            FuelPercent: 0.4d,
            FuelPerLapLiters: 5.2d,
            FuelPerLapSource: "measured green lap",
            FuelPerLapMinimumLiters: null,
            FuelPerLapMaximumLiters: null,
            MeasuredFuelPerLapMinimumLiters: 5.0d,
            MeasuredFuelPerLapAverageLiters: 5.2d,
            MeasuredFuelPerLapMaximumLiters: 5.4d,
            MeasuredFuelPerLapSampleCount: 3,
            FuelPerHourLiters: null,
            LapTimeSeconds: 100d,
            LapTimeSource: "live",
            RacePaceSeconds: 100d,
            RacePaceSource: "live",
            RaceLapsRemaining: null,
            RaceLapEstimateSource: "unavailable",
            OverallLeaderGapLaps: null,
            ClassLeaderGapLaps: null,
            TeamOverallPosition: null,
            TeamClassPosition: null,
            PlannedRaceLaps: null,
            FuelToFinishLiters: null,
            AdditionalFuelNeededLiters: null,
            FullTankStintLaps: 19.2d,
            PlannedStintCount: null,
            PlannedStopCount: null,
            FinalStintTargetLaps: null,
            RequiredFuelSavingLitersPerLap: null,
            RequiredFuelSavingPercent: null,
            StopOptimization: null,
            RhythmComparison: null,
            TeammateStintTargetLaps: null,
            TeammateStintTargetSource: null,
            TireModelSource: "none",
            FuelFillRateLitersPerSecond: null,
            TireChangeServiceSeconds: null,
            Stints: [],
            SessionKind: OverlaySessionKind.Practice);

        var practice = FuelCalculatorViewModel.From(
            strategy,
            SessionHistoryLookupResult.Empty(new HistoricalComboIdentity { CarKey = "car", TrackKey = "track", SessionKey = "practice" }),
            showAdvice: false,
            unitSystem: "Metric",
            maximumRows: 4);
        var qualifying = FuelCalculatorViewModel.From(
            strategy with { SessionKind = OverlaySessionKind.Qualifying },
            SessionHistoryLookupResult.Empty(new HistoricalComboIdentity { CarKey = "car", TrackKey = "track", SessionKey = "qualifying" }),
            showAdvice: false,
            unitSystem: "Metric",
            maximumRows: 4);

        var practiceUsage = Assert.Single(practice.MetricSections.Single(section => section.Title == "Fuel Usage").Rows);
        Assert.Equal("Practice Usage", practiceUsage.Label);
        Assert.Contains(practiceUsage.Segments, segment => segment.Label == "Min" && segment.Value == "5.0 L/lap");
        Assert.Contains(practiceUsage.Segments, segment => segment.Label == "Avg" && segment.Value == "5.2 L/lap");
        Assert.Contains(practiceUsage.Segments, segment => segment.Label == "Max" && segment.Value == "5.4 L/lap");
        Assert.Contains(practiceUsage.Segments, segment => segment.Label == "Laps" && segment.Value == "3 laps");

        var qualiUsage = Assert.Single(qualifying.MetricSections.Single(section => section.Title == "Fuel Usage").Rows);
        Assert.Equal("Quali Usage", qualiUsage.Label);
        Assert.Contains(qualiUsage.Segments, segment => segment.Label == "Avg" && segment.Value == "5.2 L/lap");
    }
}
