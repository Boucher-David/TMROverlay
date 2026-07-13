namespace TmrOverlay.Core.Fuel.V2;

internal static class FuelV2RangeCalculator
{
    public static FuelV2RangeSnapshot From(
        double? currentFuelLiters,
        FuelV2FuelPerLapWindows windows)
    {
        return new FuelV2RangeSnapshot(
            CurrentFuelLiters: IsNonNegativeFinite(currentFuelLiters) ? currentFuelLiters : null,
            Last: RangeFrom(currentFuelLiters, windows.Last, "range from Last"),
            FiveLapAverage: RangeFrom(currentFuelLiters, windows.FiveLapAverage, "range from 5L"),
            TenLapAverage: RangeFrom(currentFuelLiters, windows.TenLapAverage, "range from 10L"),
            Max: RangeFrom(currentFuelLiters, windows.Max, "range from Max"));
    }

    private static FuelV2Scalar? RangeFrom(double? currentFuelLiters, FuelV2Scalar? burn, string source)
    {
        if (!IsNonNegativeFinite(currentFuelLiters) || burn?.HasValue != true || burn.Value <= 0d)
        {
            return null;
        }

        return FuelV2Scalar.From(
            currentFuelLiters!.Value / burn.Value!.Value,
            source,
            burn.Confidence,
            burn.ContextFlags,
            displayEligible: burn.DisplayEligible,
            cleanBaselineEligible: false);
    }

    private static bool IsNonNegativeFinite(double? value)
    {
        return value is { } scalar && scalar >= 0d && !double.IsNaN(scalar) && !double.IsInfinity(scalar);
    }
}
internal static class FuelV2TargetUsageCalculator
{
    private const double MaxPlausibleProjectedTargetLaps = 1000d;

    public static FuelV2TargetUsageSnapshot From(
        double? fuelBudgetLiters,
        string budgetSource,
        FuelV2Scalar? referenceBurn,
        IReadOnlyList<int> targetLaps)
    {
        var targets = targetLaps
            .Where(laps => laps > 0)
            .Distinct()
            .Order()
            .Select(laps => TargetCell(fuelBudgetLiters, referenceBurn, laps))
            .ToArray();

        return new FuelV2TargetUsageSnapshot(
            FuelBudgetLiters: IsPositiveFinite(fuelBudgetLiters) ? fuelBudgetLiters : null,
            BudgetSource: string.IsNullOrWhiteSpace(budgetSource) ? "fuel budget" : budgetSource,
            ReferenceBurn: referenceBurn,
            Targets: targets);
    }

    public static IReadOnlyList<int> AroundProjectedTarget(double? projectedTargetLaps)
    {
        if (projectedTargetLaps is not { } target
            || target <= 0d
            || target > MaxPlausibleProjectedTargetLaps
            || double.IsNaN(target)
            || double.IsInfinity(target))
        {
            return [];
        }

        var wholeTarget = Math.Max(1, (int)Math.Round(target, MidpointRounding.AwayFromZero));
        var candidates = wholeTarget == 1
            ? new[] { 1, 2, 3 }
            : new[] { wholeTarget - 1, wholeTarget, wholeTarget + 1 };
        return candidates.Where(laps => laps > 0).Distinct().ToArray();
    }

    private static FuelV2TargetUsageCell TargetCell(double? fuelBudgetLiters, FuelV2Scalar? referenceBurn, int targetLaps)
    {
        var required = IsPositiveFinite(fuelBudgetLiters)
            ? FuelV2Scalar.From(
                fuelBudgetLiters!.Value / targetLaps,
                $"target {targetLaps} laps",
                FuelV2Confidence.Live,
                displayEligible: true,
                cleanBaselineEligible: false)
            : FuelV2Scalar.Unavailable($"target {targetLaps} laps");

        return new FuelV2TargetUsageCell(
            TargetLaps: targetLaps,
            RequiredFuelPerLap: required,
            Tone: TargetTone(required, referenceBurn));
    }

    private static FuelV2WorkbenchTone TargetTone(FuelV2Scalar required, FuelV2Scalar? referenceBurn)
    {
        if (!required.HasValue || referenceBurn?.HasValue != true || referenceBurn.Value is not > 0d)
        {
            return required.HasValue ? FuelV2WorkbenchTone.Info : FuelV2WorkbenchTone.Waiting;
        }

        var ratio = required.Value!.Value / referenceBurn.Value!.Value;
        if (ratio >= 1d)
        {
            return FuelV2WorkbenchTone.Success;
        }

        return ratio >= 0.95d
            ? FuelV2WorkbenchTone.Warning
            : FuelV2WorkbenchTone.Error;
    }

    private static bool IsPositiveFinite(double? value)
    {
        return value is { } scalar && scalar > 0d && !double.IsNaN(scalar) && !double.IsInfinity(scalar);
    }
}
