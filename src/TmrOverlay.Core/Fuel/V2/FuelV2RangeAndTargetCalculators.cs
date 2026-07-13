namespace TmrOverlay.Core.Fuel.V2;

internal static class FuelV2RangeCalculator
{
    public static FuelV2RangeSnapshot From(
        double? currentFuelLiters,
        FuelV2FuelPerLapWindows windows)
    {
        var checkpoints = FuelV2FuelCheckpointCalculator.From(
            FuelV2EffectiveCapacityResolver.From(null, null, null),
            new FuelV2FuelCheckpointInputs(CurrentFuelLiters: currentFuelLiters));
        var boundary = FuelV2BoundaryFeasibilityCalculator.From(checkpoints, windows);

        return From(boundary);
    }

    internal static FuelV2RangeSnapshot From(FuelV2BoundaryFeasibilitySnapshot boundary)
    {
        return new FuelV2RangeSnapshot(
            CurrentFuelLiters: boundary.RangeCheckpoint?.HasValue == true
                ? boundary.RangeCheckpoint.Liters
                : null,
            Last: Range(boundary, FuelV2BurnBucketId.Last),
            FiveLapAverage: Range(boundary, FuelV2BurnBucketId.FiveLapAverage),
            TenLapAverage: Range(boundary, FuelV2BurnBucketId.TenLapAverage),
            Max: Range(boundary, FuelV2BurnBucketId.Maximum));
    }

    private static FuelV2Scalar? Range(
        FuelV2BoundaryFeasibilitySnapshot snapshot,
        FuelV2BurnBucketId bucketId) => snapshot.Bucket(bucketId).FractionalRangeLaps;

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
        var normalizedReferenceBurn = TypedReferenceBurn(referenceBurn);
        var targets = targetLaps
            .Where(laps => laps > 0)
            .Distinct()
            .Order()
            .Select(laps => TargetCell(fuelBudgetLiters, normalizedReferenceBurn, laps))
            .ToArray();

        return new FuelV2TargetUsageSnapshot(
            FuelBudgetLiters: IsPositiveFinite(fuelBudgetLiters) ? fuelBudgetLiters : null,
            BudgetSource: string.IsNullOrWhiteSpace(budgetSource) ? "fuel budget" : budgetSource,
            ReferenceBurn: normalizedReferenceBurn,
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
            ReferenceBurn: referenceBurn,
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

    private static FuelV2Scalar? TypedReferenceBurn(FuelV2Scalar? referenceBurn)
    {
        return referenceBurn?.HasTypedBurnEvidence == true ? referenceBurn : null;
    }

    private static bool IsPositiveFinite(double? value)
    {
        return value is { } scalar && scalar > 0d && !double.IsNaN(scalar) && !double.IsInfinity(scalar);
    }
}
