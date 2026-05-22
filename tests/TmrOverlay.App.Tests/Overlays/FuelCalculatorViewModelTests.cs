using TmrOverlay.App.Overlays.Content;
using TmrOverlay.App.Overlays.FuelCalculator;
using TmrOverlay.App.Overlays.SimpleTelemetry;
using TmrOverlay.Core.Fuel;
using TmrOverlay.Core.History;
using TmrOverlay.Core.Overlays;
using TmrOverlay.Core.Settings;
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
        Assert.DoesNotContain(practice.MetricSections, section => section.Title == "Race Information");
        Assert.DoesNotContain(practice.MetricSections, section => section.Title == "Stint Targets");
        Assert.Contains(practice.MetricSections, section => section.Title == "Fuel Range");
        Assert.Contains(practice.MetricSections.Single(section => section.Title == "Fuel Range").Rows,
            row => row.Segments.Select(segment => segment.Label).SequenceEqual(new[] { "Level", "Usage", "Range", "Tank" }));
        Assert.Equal("fuel range", practice.Status);

        var qualiUsage = Assert.Single(qualifying.MetricSections.Single(section => section.Title == "Fuel Usage").Rows);
        Assert.Equal("Quali Usage", qualiUsage.Label);
        Assert.Contains(qualiUsage.Segments, segment => segment.Label == "Avg" && segment.Value == "5.2 L/lap");
    }

    [Fact]
    public void From_WhenRaceStrategyIsUntrusted_UsesCalculatingCopyAndHidesStints()
    {
        var strategy = RaceStrategy() with
        {
            FuelPerLapSource = "history",
            AdditionalFuelNeededLiters = null
        };

        var viewModel = FuelCalculatorViewModel.From(
            strategy,
            EmptyHistory(),
            showAdvice: false,
            unitSystem: "Metric",
            maximumRows: 6);

        Assert.Equal("calculating strategy", viewModel.Status);
        Assert.Collection(viewModel.MetricSections, section => Assert.Equal("Race Information", section.Title));
        Assert.DoesNotContain(viewModel.MetricSections, section => section.Title == "Stint Targets");

        var values = string.Join(
            " ",
            viewModel.MetricSections
                .SelectMany(section => section.Rows)
                .SelectMany(row => row.Segments)
                .Select(segment => segment.Value));
        Assert.Contains("Calculating", values, StringComparison.Ordinal);
        Assert.DoesNotContain("Covered", values, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("None", values, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(viewModel.MetricSections[0].Rows[0].Segments, segment => segment.Label == "Save" && segment.Tone == SimpleTelemetryTone.Waiting);
        Assert.Contains(viewModel.MetricSections[0].Rows[1].Segments, segment => segment.Label == "Need" && segment.Tone == SimpleTelemetryTone.Waiting);
    }

    [Fact]
    public void From_WhenMeasuredFuelNeedIsZero_AllowsCoveredAndNoneSuccessCopy()
    {
        var strategy = RaceStrategy() with
        {
            AdditionalFuelNeededLiters = 0d,
            RequiredFuelSavingLitersPerLap = 0d,
            PlannedStintCount = 1,
            PlannedStopCount = 0,
            Stints =
            [
                new FuelStintEstimate(
                    Number: 1,
                    LengthLaps: 12d,
                    Source: "finish",
                    TargetLaps: 12,
                    TargetFuelPerLapLiters: 3.1d,
                    CurrentFuelPerLapLiters: 3.1d,
                    CurrentFuelPerLapSource: "measured green lap",
                    RequiredFuelSavingLitersPerLap: 0d,
                    RequiredFuelSavingPercent: null,
                    TireAdvice: TireChangeAdvice.NoStop)
            ]
        };

        var viewModel = FuelCalculatorViewModel.From(
            strategy,
            EmptyHistory(),
            showAdvice: false,
            unitSystem: "Metric",
            maximumRows: 6);
        var text = string.Join(
            " ",
            viewModel.MetricSections
                .SelectMany(section => section.Rows)
                .SelectMany(row => row.Segments)
                .Select(segment => segment.Value));

        Assert.Contains("Covered", text, StringComparison.Ordinal);
        Assert.Contains("None", text, StringComparison.Ordinal);
    }

    [Fact]
    public void From_AppliesFuelContentSettingsToRowsAndSections()
    {
        var settings = FuelSettings();
        SetFuelBlock(settings, OverlayContentColumnSettings.FuelCalculatorStintTargetsBlockId, enabled: false);

        var stintsOff = FuelCalculatorViewModel.From(
            RaceStrategy(),
            EmptyHistory(),
            showAdvice: false,
            unitSystem: "Metric",
            maximumRows: 6,
            contentSettings: settings);

        Assert.Collection(stintsOff.MetricSections, section => Assert.Equal("Race Information", section.Title));
        Assert.DoesNotContain(stintsOff.MetricSections, section => section.Title == "Stint Targets");

        SetFuelBlock(settings, OverlayContentColumnSettings.FuelCalculatorRacePlanBlockId, enabled: false);
        SetFuelBlock(settings, OverlayContentColumnSettings.FuelCalculatorRaceFuelBlockId, enabled: false);
        SetFuelBlock(settings, OverlayContentColumnSettings.FuelCalculatorStintTargetsBlockId, enabled: true);

        var raceInfoOff = FuelCalculatorViewModel.From(
            RaceStrategy(),
            EmptyHistory(),
            showAdvice: false,
            unitSystem: "Metric",
            maximumRows: 6,
            contentSettings: settings);

        Assert.Collection(raceInfoOff.MetricSections, section => Assert.Equal("Stint Targets", section.Title));
    }

    private static FuelStrategySnapshot RaceStrategy()
    {
        return new FuelStrategySnapshot(
            HasData: true,
            Status: "3 stints / 2 stops",
            CurrentFuelLiters: 74d,
            FuelPercent: 0.7d,
            FuelPerLapLiters: 3.1d,
            FuelPerLapSource: "measured green lap",
            FuelPerLapMinimumLiters: 3.0d,
            FuelPerLapMaximumLiters: 3.2d,
            MeasuredFuelPerLapMinimumLiters: 3.0d,
            MeasuredFuelPerLapAverageLiters: 3.1d,
            MeasuredFuelPerLapMaximumLiters: 3.2d,
            MeasuredFuelPerLapSampleCount: 3,
            FuelPerHourLiters: null,
            LapTimeSeconds: 100d,
            LapTimeSource: "live",
            RacePaceSeconds: 100d,
            RacePaceSource: "live",
            RaceLapsRemaining: 30.4d,
            RaceLapEstimateSource: "timed race",
            OverallLeaderGapLaps: 0.18d,
            ClassLeaderGapLaps: 0.04d,
            TeamOverallPosition: 12,
            TeamClassPosition: 4,
            PlannedRaceLaps: 31,
            FuelToFinishLiters: 94.2d,
            AdditionalFuelNeededLiters: 0d,
            FullTankStintLaps: 34.2d,
            PlannedStintCount: 3,
            PlannedStopCount: 2,
            FinalStintTargetLaps: 7,
            RequiredFuelSavingLitersPerLap: 0.2d,
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
                new FuelStintEstimate(1, 12d, "target", 12, 3.1d, 3.1d, "measured green lap", 0.2d, null, TireChangeAdvice.NoStop),
                new FuelStintEstimate(2, 12d, "target", 12, 3.1d, 3.1d, "measured green lap", 0d, null, TireChangeAdvice.NoStop),
                new FuelStintEstimate(3, 7d, "final", 7, 3.1d, 3.1d, "measured green lap", 0d, null, TireChangeAdvice.NoStop)
            ],
            SessionKind: OverlaySessionKind.Race);
    }

    private static SessionHistoryLookupResult EmptyHistory()
    {
        return SessionHistoryLookupResult.Empty(new HistoricalComboIdentity
        {
            CarKey = "car-test",
            TrackKey = "track-test",
            SessionKey = "race"
        });
    }

    private static OverlaySettings FuelSettings()
    {
        return new ApplicationSettings().GetOrAddOverlay(
            FuelCalculatorOverlayDefinition.Definition.Id,
            FuelCalculatorOverlayDefinition.Definition.DefaultWidth,
            FuelCalculatorOverlayDefinition.Definition.DefaultHeight);
    }

    private static void SetFuelBlock(OverlaySettings settings, string blockId, bool enabled)
    {
        Assert.NotNull(OverlayContentColumnSettings.FuelCalculator.Blocks);
        var block = OverlayContentColumnSettings.FuelCalculator.Blocks!.Single(block => block.Id == blockId);
        settings.SetBooleanOption(block.EnabledOptionKey, enabled);
    }
}
