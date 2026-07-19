using TmrOverlay.Core.Telemetry.Live;

namespace TmrOverlay.Core.Fuel.V2;

internal static class FuelV2SnapshotComposer
{
    public static FuelV2ComposedSnapshot From(FuelV2SnapshotCompositionInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(inputs.LapBudget);
        ArgumentNullException.ThrowIfNull(inputs.FuelCheckpoints);
        ArgumentNullException.ThrowIfNull(inputs.BurnWindows);
        ArgumentNullException.ThrowIfNull(inputs.Boundary);
        ArgumentNullException.ThrowIfNull(inputs.TargetUsage);
        ArgumentNullException.ThrowIfNull(inputs.TargetUsage.TargetLaps);

        var targetComposition = inputs.TargetUsage with
        {
            TargetLaps = inputs.TargetUsage.TargetLaps.ToArray()
        };
        var planComposition = NormalizePlanComposition(inputs.Plan);

        var boundary = FuelV2BoundaryFeasibilityCalculator.From(
            inputs.FuelCheckpoints,
            inputs.BurnWindows,
            inputs.Boundary.TargetLaps,
            inputs.Boundary.ReserveLiters,
            inputs.Boundary.PitLaneFuelLiters);
        var range = FuelV2RangeCalculator.From(boundary);
        var pitRequest = boundary.TargetLaps.HasValue
            ? FuelV2PitRequestCalculator.FromBoundary(inputs.FuelCheckpoints, boundary)
            : null;

        var targetBudget = inputs.FuelCheckpoints.Select(targetComposition.FuelBudgetCheckpointKind);
        var targetReferenceBurn = targetComposition.ReferenceBurnBucketId is { } targetReferenceId
            ? inputs.BurnWindows.Bucket(targetReferenceId)
            : null;
        var targetUsage = FuelV2TargetUsageCalculator.From(
            targetBudget.CalculationLiters,
            targetBudget.SourceLabel,
            targetReferenceBurn,
            targetComposition.TargetLaps);
        var plan = ComposePlan(planComposition, inputs.LapBudget, inputs.FuelCheckpoints, inputs.BurnWindows);

        return new FuelV2ComposedSnapshot(
            LapBudget: inputs.LapBudget,
            Capacity: inputs.FuelCheckpoints.Capacity,
            FuelCheckpoints: inputs.FuelCheckpoints,
            BurnWindows: inputs.BurnWindows,
            BoundaryFeasibility: boundary,
            Range: range,
            TargetUsage: targetUsage,
            TargetUsageComposition: targetComposition,
            TargetUsageFuelBudget: targetBudget,
            PitRequest: pitRequest,
            Plan: plan.Snapshot,
            PlanComposition: planComposition,
            PlanDependencies: plan.Dependencies);
    }

    private static FuelV2ComposedPlan ComposePlan(
        FuelV2PlanComposition? composition,
        FuelV2LapBudgetProjection lapBudget,
        FuelV2FuelCheckpointSnapshot checkpoints,
        FuelV2FuelPerLapWindows burnWindows)
    {
        if (composition is null)
        {
            return new FuelV2ComposedPlan(null, null);
        }

        var compositionOptions = composition switch
        {
            FuelV2FullRacePlanComposition fullRace => fullRace.Options,
            FuelV2CurrentCheckpointPlanComposition current => current.Options,
            _ => throw new ArgumentOutOfRangeException(nameof(composition), composition, "Unknown Fuel V2 plan composition.")
        };
        var options = PlanOptionsWithLapBudgetContext(compositionOptions, lapBudget);
        return composition switch
        {
            FuelV2FullRacePlanComposition fullRace => ComposeFullRacePlan(
                fullRace,
                lapBudget,
                checkpoints,
                burnWindows,
                options),
            FuelV2CurrentCheckpointPlanComposition current => ComposeCurrentCheckpointPlan(
                current,
                lapBudget,
                checkpoints,
                burnWindows,
                options),
            _ => throw new ArgumentOutOfRangeException(nameof(composition), composition, "Unknown Fuel V2 plan composition.")
        };
    }

    private static FuelV2PlanComposition? NormalizePlanComposition(FuelV2PlanComposition? composition)
    {
        FuelV2PlanOptions? CopyOptions(FuelV2PlanOptions? options) => options is null
            ? null
            : options with { StateFlags = options.StateFlags?.ToArray() };

        return composition switch
        {
            null => null,
            FuelV2FullRacePlanComposition fullRace => fullRace with { Options = CopyOptions(fullRace.Options) },
            FuelV2CurrentCheckpointPlanComposition current => current with { Options = CopyOptions(current.Options) },
            _ => throw new ArgumentOutOfRangeException(nameof(composition), composition, "Unknown Fuel V2 plan composition.")
        };
    }

    private static FuelV2ComposedPlan ComposeFullRacePlan(
        FuelV2FullRacePlanComposition composition,
        FuelV2LapBudgetProjection lapBudget,
        FuelV2FuelCheckpointSnapshot checkpoints,
        FuelV2FuelPerLapWindows burnWindows,
        FuelV2PlanOptions options)
    {
        var plannedRaceLaps = LapValue(lapBudget, composition.PlannedRaceLapsSource);
        var raceLapsRemaining = LapValue(lapBudget, composition.RaceLapsRemainingSource);
        var fuelBudget = checkpoints.Select(composition.FuelBudgetCheckpointKind);
        var burn = burnWindows.Bucket(composition.BurnBucketId);
        var snapshot = FuelV2PlanCalculator.FromFuelBudget(
            plannedRaceLaps,
            raceLapsRemaining,
            fuelBudget.CalculationLiters,
            burn,
            options);
        var dependencies = new FuelV2PlanDependencies(
            Mode: FuelV2PlanCompositionMode.FullRace,
            PlannedRaceLapsSource: composition.PlannedRaceLapsSource,
            PlannedRaceLaps: plannedRaceLaps,
            RaceLapsRemainingSource: composition.RaceLapsRemainingSource,
            RaceLapsRemaining: raceLapsRemaining,
            FuelBudget: fuelBudget,
            BurnBucketId: composition.BurnBucketId,
            Burn: burn,
            FutureFuelBudget: null,
            FutureBurnBucketId: null,
            FutureBurn: null);
        return new FuelV2ComposedPlan(snapshot, dependencies);
    }

    private static FuelV2ComposedPlan ComposeCurrentCheckpointPlan(
        FuelV2CurrentCheckpointPlanComposition composition,
        FuelV2LapBudgetProjection lapBudget,
        FuelV2FuelCheckpointSnapshot checkpoints,
        FuelV2FuelPerLapWindows burnWindows,
        FuelV2PlanOptions options)
    {
        var plannedRaceLaps = LapValue(lapBudget, composition.PlannedRaceLapsSource);
        var raceLapsRemaining = LapValue(lapBudget, composition.RaceLapsRemainingSource);
        var currentFuel = checkpoints.Select(composition.CurrentFuelCheckpointKind);
        var currentBurn = burnWindows.Bucket(composition.CurrentBurnBucketId);
        var futureFuel = checkpoints.Select(composition.FutureFuelCheckpointKind);
        var futureBurn = burnWindows.Bucket(composition.FutureBurnBucketId);
        var snapshot = FuelV2PlanCalculator.FromCurrentCheckpointFuelBudget(
            plannedRaceLaps,
            raceLapsRemaining,
            currentFuel.CalculationLiters,
            currentBurn,
            futureFuel.CalculationLiters,
            futureBurn,
            options);
        var dependencies = new FuelV2PlanDependencies(
            Mode: FuelV2PlanCompositionMode.CurrentCheckpoint,
            PlannedRaceLapsSource: composition.PlannedRaceLapsSource,
            PlannedRaceLaps: plannedRaceLaps,
            RaceLapsRemainingSource: composition.RaceLapsRemainingSource,
            RaceLapsRemaining: raceLapsRemaining,
            FuelBudget: currentFuel,
            BurnBucketId: composition.CurrentBurnBucketId,
            Burn: currentBurn,
            FutureFuelBudget: futureFuel,
            FutureBurnBucketId: composition.FutureBurnBucketId,
            FutureBurn: futureBurn);
        return new FuelV2ComposedPlan(snapshot, dependencies);
    }

    private static FuelV2PlanOptions PlanOptionsWithLapBudgetContext(
        FuelV2PlanOptions? options,
        FuelV2LapBudgetProjection lapBudget)
    {
        var safeOptions = options ?? FuelV2PlanOptions.Default;
        var stateFlags = (safeOptions.StateFlags ?? []).ToList();
        if (lapBudget.ActionableSource == LiveRaceLapBudgetSource.TimedLiveClockHeldCleanPace)
        {
            stateFlags.Add(FuelV2PlanStateFlag.HeldLapBudget);
        }

        if (!lapBudget.CanDriveFuelAdvice)
        {
            stateFlags.Add(FuelV2PlanStateFlag.DegradedLapBudget);
        }

        return safeOptions with
        {
            StateFlags = stateFlags.Distinct().OrderBy(flag => flag).ToArray()
        };
    }

    private static double? LapValue(
        FuelV2LapBudgetProjection lapBudget,
        FuelV2LapBudgetValueKind valueKind)
    {
        double? value = valueKind switch
        {
            FuelV2LapBudgetValueKind.PrimaryLapsRemaining => lapBudget.PrimaryLapsRemaining is { } primary
                ? primary
                : null,
            FuelV2LapBudgetValueKind.PossibleLapsRemaining => lapBudget.PossibleLapsRemaining,
            FuelV2LapBudgetValueKind.EstimatedFinishLap => lapBudget.EstimatedFinishLap,
            _ => null
        };
        return value is { } scalar && scalar >= 0d && !double.IsNaN(scalar) && !double.IsInfinity(scalar)
            ? scalar
            : null;
    }

    private sealed record FuelV2ComposedPlan(
        FuelV2PlanSnapshot? Snapshot,
        FuelV2PlanDependencies? Dependencies);
}

internal sealed record FuelV2SnapshotCompositionInputs(
    FuelV2LapBudgetProjection LapBudget,
    FuelV2FuelCheckpointSnapshot FuelCheckpoints,
    FuelV2FuelPerLapWindows BurnWindows,
    FuelV2BoundaryComposition Boundary,
    FuelV2TargetUsageComposition TargetUsage,
    FuelV2PlanComposition? Plan);

internal sealed record FuelV2BoundaryComposition(
    int? TargetLaps,
    double ReserveLiters,
    double PitLaneFuelLiters);

internal sealed record FuelV2TargetUsageComposition(
    FuelV2FuelCheckpointKind FuelBudgetCheckpointKind,
    FuelV2BurnBucketId? ReferenceBurnBucketId,
    IReadOnlyList<int> TargetLaps);

internal abstract record FuelV2PlanComposition;

internal sealed record FuelV2FullRacePlanComposition(
    FuelV2LapBudgetValueKind PlannedRaceLapsSource,
    FuelV2LapBudgetValueKind RaceLapsRemainingSource,
    FuelV2FuelCheckpointKind FuelBudgetCheckpointKind,
    FuelV2BurnBucketId BurnBucketId,
    FuelV2PlanOptions? Options) : FuelV2PlanComposition;

internal sealed record FuelV2CurrentCheckpointPlanComposition(
    FuelV2LapBudgetValueKind PlannedRaceLapsSource,
    FuelV2LapBudgetValueKind RaceLapsRemainingSource,
    FuelV2FuelCheckpointKind CurrentFuelCheckpointKind,
    FuelV2BurnBucketId CurrentBurnBucketId,
    FuelV2FuelCheckpointKind FutureFuelCheckpointKind,
    FuelV2BurnBucketId FutureBurnBucketId,
    FuelV2PlanOptions? Options) : FuelV2PlanComposition;

internal enum FuelV2LapBudgetValueKind
{
    PrimaryLapsRemaining,
    PossibleLapsRemaining,
    EstimatedFinishLap
}

internal enum FuelV2PlanCompositionMode
{
    FullRace,
    CurrentCheckpoint
}

internal sealed record FuelV2PlanDependencies(
    FuelV2PlanCompositionMode Mode,
    FuelV2LapBudgetValueKind PlannedRaceLapsSource,
    double? PlannedRaceLaps,
    FuelV2LapBudgetValueKind RaceLapsRemainingSource,
    double? RaceLapsRemaining,
    FuelV2FuelCheckpointSelection FuelBudget,
    FuelV2BurnBucketId BurnBucketId,
    FuelV2Scalar? Burn,
    FuelV2FuelCheckpointSelection? FutureFuelBudget,
    FuelV2BurnBucketId? FutureBurnBucketId,
    FuelV2Scalar? FutureBurn);

internal sealed record FuelV2ComposedSnapshot(
    FuelV2LapBudgetProjection LapBudget,
    FuelV2EffectiveCapacitySnapshot Capacity,
    FuelV2FuelCheckpointSnapshot FuelCheckpoints,
    FuelV2FuelPerLapWindows BurnWindows,
    FuelV2BoundaryFeasibilitySnapshot BoundaryFeasibility,
    FuelV2RangeSnapshot Range,
    FuelV2TargetUsageSnapshot TargetUsage,
    FuelV2TargetUsageComposition TargetUsageComposition,
    FuelV2FuelCheckpointSelection TargetUsageFuelBudget,
    FuelV2PitRequestSnapshot? PitRequest,
    FuelV2PlanSnapshot? Plan,
    FuelV2PlanComposition? PlanComposition,
    FuelV2PlanDependencies? PlanDependencies);
