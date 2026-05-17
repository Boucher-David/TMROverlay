using TmrOverlay.App.Overlays.SimpleTelemetry;
using TmrOverlay.Core.Fuel;
using TmrOverlay.Core.History;
using TmrOverlay.Core.Overlays;

namespace TmrOverlay.App.Overlays.FuelCalculator;

internal sealed record FuelCalculatorViewModel(
    string Status,
    string Overview,
    string Source,
    IReadOnlyList<FuelDisplayRow> Rows,
    IReadOnlyList<SimpleTelemetryMetricSectionViewModel> MetricSections)
{
    public static FuelCalculatorViewModel Waiting(string status)
    {
        return new FuelCalculatorViewModel(
            Status: status,
            Overview: "--",
            Source: "source: waiting",
            Rows: [],
            MetricSections: []);
    }

    public static FuelCalculatorViewModel From(
        FuelStrategySnapshot strategy,
        SessionHistoryLookupResult history,
        bool showAdvice,
        string unitSystem,
        int maximumRows)
    {
        return new FuelCalculatorViewModel(
            Status: strategy.Status,
            Overview: BuildOverview(strategy, unitSystem),
            Source: BuildSourceText(strategy, history, unitSystem),
            Rows: BuildDisplayRows(strategy, showAdvice, unitSystem, maximumRows),
            MetricSections: BuildMetricSections(strategy, showAdvice, unitSystem, maximumRows));
    }

    public static FuelCalculatorViewModel From(
        LiveFuelStrategyModel model,
        bool showAdvice,
        string unitSystem,
        int maximumRows)
    {
        return model.Strategy is { } strategy
            ? From(strategy, model.History, showAdvice, unitSystem, maximumRows)
            : Waiting(model.Status);
    }

    private static string BuildOverview(FuelStrategySnapshot strategy, string unitSystem)
    {
        if (strategy.PlannedRaceLaps is { } plannedLaps
            && strategy.PlannedStintCount is { } stintCount
            && strategy.FinalStintTargetLaps is { } finalStintLaps)
        {
            return stintCount <= 1
                ? $"{plannedLaps} laps | no stop"
                : $"{plannedLaps} laps | {stintCount} stints | final {finalStintLaps}";
        }

        var fuel = FormatFuelVolume(strategy.CurrentFuelLiters, unitSystem);
        var remaining = FuelStrategyCalculator.FormatNumber(strategy.RaceLapsRemaining, " laps");
        var needed = strategy.AdditionalFuelNeededLiters is > 0.1d
            ? $"+{FormatFuelVolume(strategy.AdditionalFuelNeededLiters, unitSystem)}"
            : "covered";
        return $"{fuel} | {remaining} | {needed}";
    }

    private static IReadOnlyList<FuelDisplayRow> BuildDisplayRows(
        FuelStrategySnapshot strategy,
        bool showAdvice,
        string unitSystem,
        int maximumRows)
    {
        var includeAdvice = false;
        var rows = new List<FuelDisplayRow>(maximumRows);

        foreach (var row in BuildUsageDisplayRows(strategy, unitSystem)
            .Take(Math.Max(0, maximumRows - rows.Count)))
        {
            rows.Add(row);
        }

        foreach (var stint in strategy.Stints
            .Where(ShouldDisplayStint)
            .Take(Math.Max(0, maximumRows - rows.Count)))
        {
            rows.Add(new FuelDisplayRow(
                $"Stint {stint.Number}",
                BuildStintText(stint, unitSystem),
                includeAdvice ? FormatTireAdvice(stint.TireAdvice, unitSystem) : string.Empty));
        }

        return rows;
    }

    private static IReadOnlyList<SimpleTelemetryMetricSectionViewModel> BuildMetricSections(
        FuelStrategySnapshot strategy,
        bool showAdvice,
        string unitSystem,
        int maximumRows)
    {
        var includeAdvice = false;
        var rowBudget = Math.Max(1, maximumRows);
        var raceRows = new List<SimpleTelemetryRowViewModel>
        {
            new("Plan", BuildPlanText(strategy), StrategyTone(strategy))
            {
                Segments = PlanSegments(strategy, unitSystem)
            }
        };
        if (rowBudget > 1)
        {
            raceRows.Add(new SimpleTelemetryRowViewModel("Fuel", BuildFuelText(strategy, unitSystem), FuelNeedTone(strategy))
            {
                Segments = FuelSegments(strategy, unitSystem)
            });
        }

        var sections = new List<SimpleTelemetryMetricSectionViewModel>
        {
            new("Race Information", raceRows)
        };

        var rowsUsed = raceRows.Count;
        var usageRows = BuildUsageMetricRows(strategy, unitSystem)
            .Take(Math.Max(0, rowBudget - rowsUsed))
            .ToArray();
        if (usageRows.Length > 0)
        {
            sections.Add(new SimpleTelemetryMetricSectionViewModel("Fuel Usage", usageRows));
            rowsUsed += usageRows.Length;
        }

        var stintRowBudget = Math.Max(0, rowBudget - rowsUsed - (usageRows.Length > 0 ? 1 : 0));
        var stintRows = strategy.Stints
            .Where(ShouldDisplayStint)
            .Take(stintRowBudget)
            .Select(stint => new SimpleTelemetryRowViewModel(
                $"Stint {stint.Number}",
                BuildStintText(stint, unitSystem),
                StintTone(stint))
            {
                Segments = StintSegments(stint, includeAdvice, unitSystem)
            })
            .ToArray();
        if (stintRows.Length > 0)
        {
            sections.Add(new SimpleTelemetryMetricSectionViewModel("Stint Targets", stintRows));
        }

        return sections;
    }

    private static IReadOnlyList<FuelDisplayRow> BuildUsageDisplayRows(
        FuelStrategySnapshot strategy,
        string unitSystem)
    {
        if (UsageLabel(strategy.SessionKind) is not { } label)
        {
            return [];
        }

        return
        [
            new FuelDisplayRow(
                label,
                strategy.MeasuredFuelPerLapSampleCount > 0 && strategy.MeasuredFuelPerLapAverageLiters is not null
                    ? BuildUsageText(strategy, unitSystem)
                    : "waiting for completed lap",
                string.Empty)
        ];
    }

    private static IReadOnlyList<SimpleTelemetryRowViewModel> BuildUsageMetricRows(
        FuelStrategySnapshot strategy,
        string unitSystem)
    {
        if (UsageLabel(strategy.SessionKind) is not { } label)
        {
            return [];
        }

        var hasMeasuredUsage = strategy.MeasuredFuelPerLapSampleCount > 0
            && strategy.MeasuredFuelPerLapAverageLiters is not null;
        return
        [
            new SimpleTelemetryRowViewModel(
                label,
                hasMeasuredUsage ? BuildUsageText(strategy, unitSystem) : "waiting for completed lap",
                hasMeasuredUsage ? SimpleTelemetryTone.Info : SimpleTelemetryTone.Waiting)
            {
                Segments = UsageSegments(strategy, unitSystem, hasMeasuredUsage)
            }
        ];
    }

    private static string? UsageLabel(OverlaySessionKind? sessionKind)
    {
        return OverlayAvailabilityEvaluator.NormalizeSessionKind(sessionKind) switch
        {
            OverlaySessionKind.Practice => "Practice Usage",
            OverlaySessionKind.Qualifying => "Quali Usage",
            _ => null
        };
    }

    private static string BuildUsageText(FuelStrategySnapshot strategy, string unitSystem)
    {
        return $"min {FormatFuelPerLap(strategy.MeasuredFuelPerLapMinimumLiters, unitSystem)} | avg {FormatFuelPerLap(strategy.MeasuredFuelPerLapAverageLiters, unitSystem)} | max {FormatFuelPerLap(strategy.MeasuredFuelPerLapMaximumLiters, unitSystem)}";
    }

    private static IReadOnlyList<SimpleTelemetryMetricSegmentViewModel> UsageSegments(
        FuelStrategySnapshot strategy,
        string unitSystem,
        bool hasMeasuredUsage)
    {
        var tone = hasMeasuredUsage ? SimpleTelemetryTone.Info : SimpleTelemetryTone.Waiting;
        return
        [
            Segment("Min", FormatFuelPerLap(strategy.MeasuredFuelPerLapMinimumLiters, unitSystem), tone),
            Segment("Avg", FormatFuelPerLap(strategy.MeasuredFuelPerLapAverageLiters, unitSystem), tone),
            Segment("Max", FormatFuelPerLap(strategy.MeasuredFuelPerLapMaximumLiters, unitSystem), tone),
            Segment("Laps", FormatSampleCount(strategy.MeasuredFuelPerLapSampleCount), tone)
        ];
    }

    private static bool ShouldDisplayStint(FuelStintEstimate stint)
    {
        return stint.LengthLaps > 0.05d
            || string.Equals(stint.Source, "finish", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildStintText(FuelStintEstimate stint, string unitSystem)
    {
        if (string.Equals(stint.Source, "finish", StringComparison.OrdinalIgnoreCase))
        {
            return "no fuel stop needed";
        }

        if (stint.TargetLaps is { } targetLaps)
        {
            var target = FormatFuelPerLap(stint.TargetFuelPerLapLiters, unitSystem);
            var suffix = stint.Source == "final" ? " final" : string.Empty;
            return $"{targetLaps} laps{suffix} | target {target}";
        }

        return $"{stint.LengthLaps:0.0} laps";
    }

    private static string BuildPlanText(FuelStrategySnapshot strategy)
    {
        var laps = strategy.PlannedRaceLaps is { } plannedLaps
            ? $"{plannedLaps} laps"
            : FuelStrategyCalculator.FormatNumber(strategy.RaceLapsRemaining, " laps");
        var stints = strategy.PlannedStintCount is { } stintCount
            ? stintCount <= 1 ? "no stop" : $"{stintCount} stints"
            : "--";
        var stops = strategy.PlannedStopCount is { } stopCount
            ? $"{stopCount} {Pluralize("stop", stopCount)}"
            : "--";
        return $"{laps} | {stints} | {stops}";
    }

    private static string BuildFuelText(FuelStrategySnapshot strategy, string unitSystem)
    {
        var current = FormatFuelVolume(strategy.CurrentFuelLiters, unitSystem);
        var burn = FormatFuelPerLap(strategy.FuelPerLapLiters, unitSystem);
        var need = strategy.AdditionalFuelNeededLiters is > 0.1d
            ? $"+{FormatFuelVolume(strategy.AdditionalFuelNeededLiters, unitSystem)}"
            : "covered";
        return $"{current} | {burn} | {need}";
    }

    private static IReadOnlyList<SimpleTelemetryMetricSegmentViewModel> PlanSegments(
        FuelStrategySnapshot strategy,
        string unitSystem)
    {
        return
        [
            Segment("Race", FormatLapCount(strategy.PlannedRaceLaps), strategy.PlannedRaceLaps is null ? SimpleTelemetryTone.Waiting : SimpleTelemetryTone.Info),
            Segment("Remain", FormatLaps(strategy.RaceLapsRemaining), strategy.RaceLapsRemaining is null ? SimpleTelemetryTone.Waiting : SimpleTelemetryTone.Info),
            Segment("Stints", FormatCount(strategy.PlannedStintCount), strategy.PlannedStintCount is null ? SimpleTelemetryTone.Waiting : StrategyCountTone(strategy.PlannedStintCount)),
            Segment("Stops", FormatCount(strategy.PlannedStopCount), strategy.PlannedStopCount is null ? SimpleTelemetryTone.Waiting : SimpleTelemetryTone.Info),
            Segment("Save", FormatSaving(strategy.RequiredFuelSavingLitersPerLap, unitSystem), SavingTone(strategy.RequiredFuelSavingLitersPerLap))
        ];
    }

    private static IReadOnlyList<SimpleTelemetryMetricSegmentViewModel> FuelSegments(
        FuelStrategySnapshot strategy,
        string unitSystem)
    {
        return
        [
            Segment("Current", FormatFuelVolume(strategy.CurrentFuelLiters, unitSystem), strategy.CurrentFuelLiters is null ? SimpleTelemetryTone.Waiting : SimpleTelemetryTone.Info),
            Segment("Burn", FormatFuelPerLap(strategy.FuelPerLapLiters, unitSystem), BurnTone(strategy.FuelPerLapLiters)),
            Segment("Tank", FuelStrategyCalculator.FormatNumber(strategy.FullTankStintLaps, " laps"), strategy.FullTankStintLaps is null ? SimpleTelemetryTone.Waiting : SimpleTelemetryTone.Info),
            Segment("Need", FormatFuelNeed(strategy.AdditionalFuelNeededLiters, unitSystem), FuelNeedTone(strategy))
        ];
    }

    private static IReadOnlyList<SimpleTelemetryMetricSegmentViewModel> StintSegments(
        FuelStintEstimate stint,
        bool showAdvice,
        string unitSystem)
    {
        var segments = new List<SimpleTelemetryMetricSegmentViewModel>
        {
            Segment("Laps", FormatStintLaps(stint), SimpleTelemetryTone.Info),
            Segment("Target", FormatStintTarget(stint, unitSystem), StintTargetTone(stint)),
            Segment("Save", FormatSaving(stint.RequiredFuelSavingLitersPerLap, unitSystem), SavingTone(stint.RequiredFuelSavingLitersPerLap))
        };
        if (showAdvice)
        {
            segments.Add(Segment(
                "Tires",
                FormatTireAdvice(stint.TireAdvice, unitSystem),
                TireAdviceTone(stint.TireAdvice)));
        }

        return segments;
    }

    private static SimpleTelemetryMetricSegmentViewModel Segment(
        string label,
        string value,
        SimpleTelemetryTone tone)
    {
        return new SimpleTelemetryMetricSegmentViewModel(label, value, tone);
    }

    private static string FormatLapCount(int? laps)
    {
        return laps is { } value ? $"{value} laps" : "--";
    }

    private static string FormatCount(int? value)
    {
        return value is { } number ? number.ToString(System.Globalization.CultureInfo.InvariantCulture) : "--";
    }

    private static string FormatSampleCount(int count)
    {
        return count > 0 ? $"{count} {Pluralize("lap", count)}" : "--";
    }

    private static string FormatLaps(double? laps)
    {
        return FuelStrategyCalculator.FormatNumber(laps, " laps");
    }

    private static string FormatStintLaps(FuelStintEstimate stint)
    {
        return stint.TargetLaps is { } targetLaps
            ? $"{targetLaps} laps"
            : FormatLaps(stint.LengthLaps);
    }

    private static string FormatStintTarget(FuelStintEstimate stint, string unitSystem)
    {
        if (string.Equals(stint.Source, "finish", StringComparison.OrdinalIgnoreCase))
        {
            return "Finish";
        }

        return FormatFuelPerLap(stint.TargetFuelPerLapLiters, unitSystem);
    }

    private static string FormatSaving(double? liters, string unitSystem)
    {
        return liters is > 0.01d ? FormatFuelPerLap(liters, unitSystem) : "None";
    }

    private static string FormatFuelNeed(double? liters, string unitSystem)
    {
        return liters is > 0.1d ? $"+{FormatFuelVolume(liters, unitSystem)}" : "Covered";
    }

    private static SimpleTelemetryTone StrategyTone(FuelStrategySnapshot strategy)
    {
        return strategy.RequiredFuelSavingLitersPerLap is > 0.01d
            ? SimpleTelemetryTone.Warning
            : SimpleTelemetryTone.Info;
    }

    private static SimpleTelemetryTone StrategyCountTone(int? stints)
    {
        return stints is <= 1 ? SimpleTelemetryTone.Success : SimpleTelemetryTone.Info;
    }

    private static SimpleTelemetryTone FuelNeedTone(FuelStrategySnapshot strategy)
    {
        if (strategy.AdditionalFuelNeededLiters is null)
        {
            return SimpleTelemetryTone.Waiting;
        }

        return strategy.AdditionalFuelNeededLiters > 0.1d
            ? SimpleTelemetryTone.Warning
            : SimpleTelemetryTone.Success;
    }

    private static SimpleTelemetryTone BurnTone(double? liters)
    {
        if (liters is null)
        {
            return SimpleTelemetryTone.Waiting;
        }

        return SimpleTelemetryTone.Info;
    }

    private static SimpleTelemetryTone StintTone(FuelStintEstimate stint)
    {
        return stint.RequiredFuelSavingLitersPerLap is > 0.01d
            ? SimpleTelemetryTone.Warning
            : SimpleTelemetryTone.Info;
    }

    private static SimpleTelemetryTone StintTargetTone(FuelStintEstimate stint)
    {
        if (string.Equals(stint.Source, "finish", StringComparison.OrdinalIgnoreCase))
        {
            return SimpleTelemetryTone.Success;
        }

        return stint.TargetFuelPerLapLiters is null
            ? SimpleTelemetryTone.Waiting
            : StintTone(stint);
    }

    private static SimpleTelemetryTone SavingTone(double? liters)
    {
        return liters is > 0.01d ? SimpleTelemetryTone.Warning : SimpleTelemetryTone.Success;
    }

    private static SimpleTelemetryTone TireAdviceTone(TireChangeAdvice? advice)
    {
        if (advice is null || advice == TireChangeAdvice.Pending)
        {
            return SimpleTelemetryTone.Waiting;
        }

        return advice.TimeLossSeconds is > 1d ? SimpleTelemetryTone.Warning : SimpleTelemetryTone.Success;
    }

    private static string BuildRhythmText(FuelRhythmComparison comparison)
    {
        return $"{comparison.LongTargetLaps}-lap rhythm avoids +{comparison.AdditionalStopCount} {Pluralize("stop", comparison.AdditionalStopCount)}";
    }

    private static string BuildRhythmAdvice(FuelRhythmComparison comparison, string unitSystem)
    {
        var time = comparison.EstimatedTimeLossSeconds is { } seconds && seconds > 0d
            ? $"~{seconds:0}s"
            : "--";
        return comparison.RequiredSavingLitersPerLap > 0.01d
            ? $"{time} | save {FormatFuelPerLap(comparison.RequiredSavingLitersPerLap, unitSystem)}"
            : time;
    }

    private static string BuildSourceText(
        FuelStrategySnapshot strategy,
        SessionHistoryLookupResult history,
        string unitSystem)
    {
        var fuelPerLap = FormatFuelPerLap(strategy.FuelPerLapLiters, unitSystem);
        var fullTank = FuelStrategyCalculator.FormatNumber(strategy.FullTankStintLaps, " laps/tank");
        var historySource = history.UserAggregate is not null
            ? "user"
            : history.BaselineAggregate is not null
                ? "baseline"
                : "none";
        var historicalRange = strategy.FuelPerLapMinimumLiters is not null || strategy.FuelPerLapMaximumLiters is not null
            ? $" | min/avg/max {FormatFuelNumber(strategy.FuelPerLapMinimumLiters, unitSystem)}/{FormatFuelNumber(strategy.FuelPerLapLiters, unitSystem)}/{FormatFuelNumber(strategy.FuelPerLapMaximumLiters, unitSystem)} {FuelPerLapSuffix(unitSystem)}"
            : string.Empty;
        var gaps = strategy.OverallLeaderGapLaps is not null || strategy.ClassLeaderGapLaps is not null
            ? $" | gap O{FormatPlain(strategy.OverallLeaderGapLaps)} C{FormatPlain(strategy.ClassLeaderGapLaps)}"
            : string.Empty;
        var tireModel = string.Empty;
        return $"burn {fuelPerLap} ({strategy.FuelPerLapSource}) | {fullTank} | history {historySource}{historicalRange}{tireModel}{gaps}";
    }

    private static string FormatTireAdvice(TireChangeAdvice? advice, string unitSystem)
    {
        if (advice is null)
        {
            return "--";
        }

        if (advice == TireChangeAdvice.Pending && advice.FuelToAddLiters is { } pendingFuel)
        {
            return $"tire data pending ({FormatFuelVolume(pendingFuel, unitSystem)})";
        }

        if (advice.FuelToAddLiters is { } fuelToAdd
            && advice.TimeLossSeconds is { } timeLoss
            && timeLoss <= 1d)
        {
            return $"tires free ({FormatFuelVolume(fuelToAdd, unitSystem)})";
        }

        return advice.Text.Replace(" L", $" {FuelVolumeSuffix(unitSystem)}", StringComparison.Ordinal);
    }

    private static string FormatFuelVolume(double? liters, string unitSystem)
    {
        return $"{FormatFuelNumber(liters, unitSystem)} {FuelVolumeSuffix(unitSystem)}";
    }

    private static string FormatFuelPerLap(double? liters, string unitSystem)
    {
        return $"{FormatFuelNumber(liters, unitSystem)} {FuelPerLapSuffix(unitSystem)}";
    }

    private static string FormatFuelNumber(double? liters, string unitSystem)
    {
        if (liters is null || double.IsNaN(liters.Value) || double.IsInfinity(liters.Value))
        {
            return "--";
        }

        var value = string.Equals(unitSystem, "Imperial", StringComparison.OrdinalIgnoreCase)
            ? liters.Value * 0.264172052d
            : liters.Value;
        return FormattableString.Invariant($"{value:0.0}");
    }

    private static string FuelVolumeSuffix(string unitSystem)
    {
        return string.Equals(unitSystem, "Imperial", StringComparison.OrdinalIgnoreCase) ? "gal" : "L";
    }

    private static string FuelPerLapSuffix(string unitSystem)
    {
        return string.Equals(unitSystem, "Imperial", StringComparison.OrdinalIgnoreCase) ? "gal/lap" : "L/lap";
    }

    private static string FormatPlain(double? value)
    {
        return value is null || double.IsNaN(value.Value) || double.IsInfinity(value.Value)
            ? "--"
            : FormattableString.Invariant($"{value.Value:0.0}");
    }

    private static string Pluralize(string singular, int count)
    {
        return count == 1 ? singular : $"{singular}s";
    }
}

internal sealed record FuelDisplayRow(string Label, string Value, string Advice);
