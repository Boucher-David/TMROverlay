using TmrOverlay.Core.Fuel.V2;
using TmrOverlay.Core.Telemetry.Live;
using Xunit;

namespace TmrOverlay.App.Tests.Fuel.V2;

public sealed class FuelV2SnapshotComposerTests
{
    [Fact]
    public void Composer_RetainsAcceptedOwnersAndProjectsExplicitCellDependencies()
    {
        var lapBudget = LapBudget();
        var capacity = FuelV2EffectiveCapacityResolver.From(100d, 0.60d, 0.60d);
        var checkpoints = FuelV2FuelCheckpointCalculator.From(
            capacity,
            new FuelV2FuelCheckpointInputs(
                MeasuredFirstGreenFuelLiters: 58d,
                CurrentFuelLiters: 40d,
                MeasuredAtBoxFuelLiters: 10d,
                MeasuredServiceCompleteFuelLiters: 50d,
                MeasuredPitExitFuelLiters: 49d));
        var windows = FuelV2FuelPerLapCalculator.FromAcceptedLaps(
            [10d, 11d, 9d, 10d, 10d, 10d, 10d, 10d, 10d, 10d]);

        var snapshot = FuelV2SnapshotComposer.From(new FuelV2SnapshotCompositionInputs(
            LapBudget: lapBudget,
            FuelCheckpoints: checkpoints,
            BurnWindows: windows,
            Boundary: new FuelV2BoundaryComposition(
                TargetLaps: 2,
                ReserveLiters: 1d,
                PitLaneFuelLiters: 0.5d),
            TargetUsage: new FuelV2TargetUsageComposition(
                FuelBudgetCheckpointKind: FuelV2FuelCheckpointKind.FirstGreen,
                ReferenceBurnBucketId: FuelV2BurnBucketId.FiveLapAverage,
                TargetLaps: [4, 5, 6]),
            Plan: new FuelV2CurrentCheckpointPlanComposition(
                PlannedRaceLapsSource: FuelV2LapBudgetValueKind.PrimaryLapsRemaining,
                RaceLapsRemainingSource: FuelV2LapBudgetValueKind.PrimaryLapsRemaining,
                CurrentFuelCheckpointKind: FuelV2FuelCheckpointKind.Current,
                CurrentBurnBucketId: FuelV2BurnBucketId.Last,
                FutureFuelCheckpointKind: FuelV2FuelCheckpointKind.ServiceComplete,
                FutureBurnBucketId: FuelV2BurnBucketId.FiveLapAverage,
                Options: null)));

        Assert.Same(lapBudget, snapshot.LapBudget);
        Assert.Same(capacity, snapshot.Capacity);
        Assert.Same(checkpoints, snapshot.FuelCheckpoints);
        Assert.Same(windows, snapshot.BurnWindows);
        Assert.Same(capacity, snapshot.BoundaryFeasibility.Capacity);

        var boundary = snapshot.BoundaryFeasibility.Bucket(FuelV2BurnBucketId.Last);
        Assert.Equal(4d, Value(boundary.FractionalRangeLaps), precision: 6);
        Assert.Equal(21.5d, Value(boundary.DesiredFuelLiters), precision: 6);
        Assert.Equal(11.5d, Value(boundary.DesiredAddLiters), precision: 6);
        Assert.Equal(FuelV2TargetFeasibilityState.Feasible, boundary.FeasibilityState);

        Assert.Equal(40d, snapshot.Range.CurrentFuelLiters);
        Assert.Equal(Value(boundary.FractionalRangeLaps), Value(snapshot.Range.Last), precision: 9);

        var pitLast = Assert.IsType<FuelV2PitRequestCell>(snapshot.PitRequest?.Last);
        Assert.Equal(Value(boundary.DesiredAddLiters), Value(pitLast.DesiredAddLiters), precision: 9);
        Assert.Equal(Value(boundary.ClampedAddLiters), Value(pitLast.FuelToAddLiters), precision: 9);
        Assert.Equal(boundary.FeasibilityState, pitLast.FeasibilityState);

        Assert.Equal(58d, snapshot.TargetUsage.FuelBudgetLiters);
        Assert.Equal("measured first-green telemetry", snapshot.TargetUsage.BudgetSource);
        Assert.Equal(FuelV2FuelCheckpointSelectionState.Available, snapshot.TargetUsageFuelBudget.State);
        Assert.Equal(FuelV2FuelCheckpointKind.FirstGreen, snapshot.TargetUsageComposition.FuelBudgetCheckpointKind);
        Assert.Same(windows.FiveLapAverage, snapshot.TargetUsage.ReferenceBurn);
        Assert.Equal([4, 5, 6], snapshot.TargetUsage.Targets.Select(target => target.TargetLaps));

        var plan = Assert.IsType<FuelV2PlanSnapshot>(snapshot.Plan);
        Assert.Equal(4d, plan.CurrentStintCapacityLaps);
        Assert.Equal(5d, plan.FutureStintCapacityLaps);
        Assert.Equal(2, plan.PlannedStopCount);
        Assert.Equal(3d, plan.FinalStintLaps);
        var planDependencies = Assert.IsType<FuelV2PlanDependencies>(snapshot.PlanDependencies);
        Assert.Equal(FuelV2PlanCompositionMode.CurrentCheckpoint, planDependencies.Mode);
        Assert.Equal(FuelV2FuelCheckpointKind.Current, planDependencies.FuelBudget.Kind);
        Assert.Same(windows.Last, planDependencies.Burn);
        Assert.Equal(FuelV2FuelCheckpointKind.ServiceComplete, planDependencies.FutureFuelBudget?.Kind);
        Assert.Same(windows.FiveLapAverage, planDependencies.FutureBurn);
    }

    [Fact]
    public void Composer_MissingExplicitSelectionsDoNotFallBackToAvailableFacts()
    {
        var capacity = FuelV2EffectiveCapacityResolver.From(60d, 1d, 1d);
        var checkpoints = FuelV2FuelCheckpointCalculator.From(
            capacity,
            new FuelV2FuelCheckpointInputs(CurrentFuelLiters: 40d));
        var windows = FuelV2FuelPerLapCalculator.FromAcceptedLaps([10d]);

        var snapshot = FuelV2SnapshotComposer.From(new FuelV2SnapshotCompositionInputs(
            LapBudget: LapBudget(),
            FuelCheckpoints: checkpoints,
            BurnWindows: windows,
            Boundary: new FuelV2BoundaryComposition(
                TargetLaps: null,
                ReserveLiters: 0d,
                PitLaneFuelLiters: 0d),
            TargetUsage: new FuelV2TargetUsageComposition(
                FuelBudgetCheckpointKind: FuelV2FuelCheckpointKind.FirstGreen,
                ReferenceBurnBucketId: null,
                TargetLaps: [4]),
            Plan: new FuelV2FullRacePlanComposition(
                PlannedRaceLapsSource: FuelV2LapBudgetValueKind.PrimaryLapsRemaining,
                RaceLapsRemainingSource: FuelV2LapBudgetValueKind.PrimaryLapsRemaining,
                FuelBudgetCheckpointKind: FuelV2FuelCheckpointKind.EffectiveCapacity,
                BurnBucketId: FuelV2BurnBucketId.FiveLapAverage,
                Options: null)));

        Assert.Null(snapshot.PitRequest);
        Assert.Equal(4d, Value(snapshot.Range.Last), precision: 6);

        Assert.Null(snapshot.TargetUsage.FuelBudgetLiters);
        Assert.Null(snapshot.TargetUsage.ReferenceBurn);
        Assert.False(Assert.Single(snapshot.TargetUsage.Targets).RequiredFuelPerLap.HasValue);

        var plan = Assert.IsType<FuelV2PlanSnapshot>(snapshot.Plan);
        Assert.Equal(60d, plan.UsableStintFuelLiters);
        Assert.Null(plan.StintBurn);
        Assert.Null(plan.StintCapacityLaps);
        Assert.Null(plan.PlannedStopCount);
    }

    [Fact]
    public void Composer_FullRacePlanUsesTheNamedCheckpointAndBucket()
    {
        var capacity = FuelV2EffectiveCapacityResolver.From(60d, 1d, 1d);
        var checkpoints = FuelV2FuelCheckpointCalculator.From(
            capacity,
            new FuelV2FuelCheckpointInputs(
                MeasuredFirstGreenFuelLiters: 50d,
                CurrentFuelLiters: 20d,
                MeasuredAtBoxFuelLiters: 10d));
        var maxSeed = FuelV2Scalar.From(
            12d,
            "historical maximum",
            FuelV2Confidence.Seeded,
            burnSource: FuelV2BurnSource.HistoricalSeed,
            sampleCount: 12,
            strategyEligible: true);
        var windows = FuelV2FuelPerLapCalculator.FromAcceptedLaps(
            [10d],
            new FuelV2FuelPerLapWindowOptions(MaxSeed: maxSeed));

        var snapshot = FuelV2SnapshotComposer.From(new FuelV2SnapshotCompositionInputs(
            LapBudget: LapBudget(),
            FuelCheckpoints: checkpoints,
            BurnWindows: windows,
            Boundary: new FuelV2BoundaryComposition(2, 0d, 0d),
            TargetUsage: new FuelV2TargetUsageComposition(
                FuelV2FuelCheckpointKind.Current,
                FuelV2BurnBucketId.Last,
                [2]),
            Plan: new FuelV2FullRacePlanComposition(
                PlannedRaceLapsSource: FuelV2LapBudgetValueKind.PrimaryLapsRemaining,
                RaceLapsRemainingSource: FuelV2LapBudgetValueKind.PrimaryLapsRemaining,
                FuelBudgetCheckpointKind: FuelV2FuelCheckpointKind.FirstGreen,
                BurnBucketId: FuelV2BurnBucketId.Maximum,
                Options: null)));

        var plan = Assert.IsType<FuelV2PlanSnapshot>(snapshot.Plan);
        Assert.Equal(50d, plan.UsableStintFuelLiters);
        Assert.Same(windows.Max, plan.StintBurn);
        Assert.Equal(FuelV2BurnBucketId.Maximum, plan.StintBurn?.BurnBucketId);
        Assert.Equal(4d, plan.StintCapacityLaps);
        Assert.Equal(2, plan.PlannedStopCount);
    }

    [Fact]
    public void Composer_HistoricalNormalDrivesOnlyTheExplicitTargetAndPlanSelections()
    {
        var capacity = FuelV2EffectiveCapacityResolver.From(75d, 0.68d, 0.68d);
        var checkpoints = FuelV2FuelCheckpointCalculator.From(
            capacity,
            new FuelV2FuelCheckpointInputs(
                MeasuredFirstGreenFuelLiters: 49.6766d,
                CurrentFuelLiters: 49.6766d));
        var history = FuelV2Scalar.From(
            13.5d,
            "classified race history; 1 accepted lap windows; 1 learning-eligible sessions",
            FuelV2Confidence.Seeded,
            burnBucketId: FuelV2BurnBucketId.HistoricalNormal,
            burnSource: FuelV2BurnSource.HistoricalNormal,
            sampleCount: 1);
        var windows = FuelV2FuelPerLapCalculator.FromAcceptedLaps(
            [12.63d],
            new FuelV2FuelPerLapWindowOptions(HistoricalNormalSeed: history));

        var snapshot = FuelV2SnapshotComposer.From(new FuelV2SnapshotCompositionInputs(
            LapBudget: LapBudget() with
            {
                PrimaryLapsRemaining = 4,
                PossibleLapsRemaining = 4d,
                EstimatedFinishLap = 4d
            },
            FuelCheckpoints: checkpoints,
            BurnWindows: windows,
            Boundary: new FuelV2BoundaryComposition(3, 0d, 0d),
            TargetUsage: new FuelV2TargetUsageComposition(
                FuelV2FuelCheckpointKind.FirstGreen,
                FuelV2BurnBucketId.HistoricalNormal,
                [3, 4]),
            Plan: new FuelV2FullRacePlanComposition(
                FuelV2LapBudgetValueKind.PrimaryLapsRemaining,
                FuelV2LapBudgetValueKind.PossibleLapsRemaining,
                FuelV2FuelCheckpointKind.FirstGreen,
                FuelV2BurnBucketId.HistoricalNormal,
                Options: null)));

        Assert.Same(windows.HistoricalNormal, snapshot.TargetUsage.ReferenceBurn);
        Assert.Equal(12.41915d, Value(snapshot.TargetUsage.Targets.Single(target => target.TargetLaps == 4).RequiredFuelPerLap), precision: 5);
        Assert.Same(windows.HistoricalNormal, snapshot.PlanDependencies?.Burn);
        Assert.Equal(FuelV2BurnBucketId.HistoricalNormal, snapshot.PlanDependencies?.BurnBucketId);
        Assert.Equal(3d, snapshot.Plan?.StintCapacityLaps);
        Assert.NotSame(windows.Last, snapshot.PlanDependencies?.Burn);
    }

    [Fact]
    public void Composer_ConflictedCapacityCannotEscapeIntoTargetUsageOrPlan()
    {
        var capacity = FuelV2EffectiveCapacityResolver.From(75d, 0.8d, 0.68d);
        var checkpoints = FuelV2FuelCheckpointCalculator.From(
            capacity,
            new FuelV2FuelCheckpointInputs(CurrentFuelLiters: 20d, MeasuredAtBoxFuelLiters: 10d));
        var windows = FuelV2FuelPerLapCalculator.FromAcceptedLaps([10d]);

        var snapshot = FuelV2SnapshotComposer.From(new FuelV2SnapshotCompositionInputs(
            LapBudget: LapBudget(),
            FuelCheckpoints: checkpoints,
            BurnWindows: windows,
            Boundary: new FuelV2BoundaryComposition(2, 0d, 0d),
            TargetUsage: new FuelV2TargetUsageComposition(
                FuelV2FuelCheckpointKind.EffectiveCapacity,
                FuelV2BurnBucketId.Last,
                [4]),
            Plan: new FuelV2FullRacePlanComposition(
                FuelV2LapBudgetValueKind.PrimaryLapsRemaining,
                FuelV2LapBudgetValueKind.PrimaryLapsRemaining,
                FuelV2FuelCheckpointKind.EffectiveCapacity,
                FuelV2BurnBucketId.Last,
                Options: null)));

        Assert.Equal(FuelV2FuelCheckpointSelectionState.Conflicted, snapshot.TargetUsageFuelBudget.State);
        Assert.Null(snapshot.TargetUsageFuelBudget.CalculationLiters);
        Assert.Contains("conflicted", snapshot.TargetUsage.BudgetSource);
        Assert.Null(snapshot.TargetUsage.FuelBudgetLiters);
        Assert.False(Assert.Single(snapshot.TargetUsage.Targets).RequiredFuelPerLap.HasValue);
        Assert.Equal(FuelV2TargetFeasibilityState.CapacityConflicted,
            snapshot.BoundaryFeasibility.Bucket(FuelV2BurnBucketId.Last).FeasibilityState);

        var plan = Assert.IsType<FuelV2PlanSnapshot>(snapshot.Plan);
        Assert.Null(plan.UsableStintFuelLiters);
        Assert.Null(plan.StintCapacityLaps);
        Assert.Null(plan.PlannedStopCount);
        Assert.Equal(FuelV2FuelCheckpointSelectionState.Conflicted, snapshot.PlanDependencies?.FuelBudget.State);
    }

    [Fact]
    public void Composer_InvalidAtBoxFallbackCannotDriveDerivedCells()
    {
        var capacity = FuelV2EffectiveCapacityResolver.From(60d, 1d, 1d);
        var checkpoints = FuelV2FuelCheckpointCalculator.From(
            capacity,
            new FuelV2FuelCheckpointInputs(
                CurrentFuelLiters: 20d,
                MeasuredAtBoxFuelLiters: -1d,
                ExpectedFuelToBoxLiters: 1d));
        var windows = FuelV2FuelPerLapCalculator.FromAcceptedLaps([10d]);

        var snapshot = FuelV2SnapshotComposer.From(new FuelV2SnapshotCompositionInputs(
            LapBudget: LapBudget(),
            FuelCheckpoints: checkpoints,
            BurnWindows: windows,
            Boundary: new FuelV2BoundaryComposition(2, 0d, 0d),
            TargetUsage: new FuelV2TargetUsageComposition(
                FuelV2FuelCheckpointKind.ExpectedAtBox,
                FuelV2BurnBucketId.Last,
                [2]),
            Plan: new FuelV2CurrentCheckpointPlanComposition(
                FuelV2LapBudgetValueKind.PrimaryLapsRemaining,
                FuelV2LapBudgetValueKind.PrimaryLapsRemaining,
                FuelV2FuelCheckpointKind.Current,
                FuelV2BurnBucketId.Last,
                FuelV2FuelCheckpointKind.ExpectedAtBox,
                FuelV2BurnBucketId.Last,
                Options: null)));

        Assert.Equal(19d, checkpoints.ExpectedAtBox?.Liters);
        Assert.Equal(FuelV2FuelCheckpointSelectionState.Invalid, snapshot.TargetUsageFuelBudget.State);
        Assert.Null(snapshot.TargetUsage.FuelBudgetLiters);
        Assert.Equal(FuelV2TargetFeasibilityState.Invalid,
            snapshot.BoundaryFeasibility.Bucket(FuelV2BurnBucketId.Last).FeasibilityState);
        Assert.Null(snapshot.PitRequest?.Last);

        var plan = Assert.IsType<FuelV2PlanSnapshot>(snapshot.Plan);
        Assert.Equal(2d, plan.CurrentStintCapacityLaps);
        Assert.Null(plan.FutureStintCapacityLaps);
        Assert.Null(plan.PlannedStopCount);
        Assert.Equal(FuelV2FuelCheckpointSelectionState.Invalid, snapshot.PlanDependencies?.FutureFuelBudget?.State);
    }

    [Fact]
    public void Composer_ConflictedProjectionAndCleanZeroRemainDifferentSelections()
    {
        var capacity = FuelV2EffectiveCapacityResolver.From(60d, 1d, 1d);
        var windows = FuelV2FuelPerLapCalculator.FromAcceptedLaps([10d]);
        var conflictedCheckpoints = FuelV2FuelCheckpointCalculator.From(
            capacity,
            new FuelV2FuelCheckpointInputs(CurrentFuelLiters: 1d, ExpectedFuelToBoxLiters: 2d));
        var cleanZeroCheckpoints = FuelV2FuelCheckpointCalculator.From(
            capacity,
            new FuelV2FuelCheckpointInputs(CurrentFuelLiters: 0d, MeasuredAtBoxFuelLiters: 0d));

        var conflicted = ComposeWithBudget(
            conflictedCheckpoints,
            windows,
            FuelV2FuelCheckpointKind.ExpectedAtBox);
        var cleanZero = ComposeWithBudget(
            cleanZeroCheckpoints,
            windows,
            FuelV2FuelCheckpointKind.Current);

        Assert.Equal(0d, conflictedCheckpoints.ExpectedAtBox?.Liters);
        Assert.Equal(FuelV2FuelCheckpointSelectionState.Conflicted, conflicted.TargetUsageFuelBudget.State);
        Assert.Null(conflicted.TargetUsageFuelBudget.CalculationLiters);

        Assert.Equal(FuelV2FuelCheckpointSelectionState.Available, cleanZero.TargetUsageFuelBudget.State);
        Assert.Equal(0d, cleanZero.TargetUsageFuelBudget.CalculationLiters);
        Assert.Null(cleanZero.TargetUsage.FuelBudgetLiters);
        var zeroPlan = Assert.IsType<FuelV2PlanSnapshot>(cleanZero.Plan);
        Assert.Equal(0d, zeroPlan.UsableStintFuelLiters);
        Assert.Equal(0d, zeroPlan.StintCapacityLaps);
        Assert.Null(zeroPlan.PlannedStopCount);
    }

    [Fact]
    public void CheckpointSelection_PropagatesOnlyProvidedDescendantDependencies()
    {
        var capacity = FuelV2EffectiveCapacityResolver.From(60d, 1d, 1d);
        var invalidChain = FuelV2FuelCheckpointCalculator.From(
            capacity,
            new FuelV2FuelCheckpointInputs(
                CurrentFuelLiters: -1d,
                ExpectedFuelToBoxLiters: 1d,
                PlannedServiceAddLiters: 10d,
                ExpectedBoxToPitExitFuelLiters: 0.5d));
        var conflictedChain = FuelV2FuelCheckpointCalculator.From(
            capacity,
            new FuelV2FuelCheckpointInputs(
                CurrentFuelLiters: 1d,
                ExpectedFuelToBoxLiters: 2d,
                PlannedServiceAddLiters: 10d,
                ExpectedBoxToPitExitFuelLiters: 0.5d));
        var invalidCapacityFirstGreen = FuelV2FuelCheckpointCalculator.From(
            FuelV2EffectiveCapacityResolver.From(-1d, 1d, 1d),
            new FuelV2FuelCheckpointInputs(EstimatedFormationFuelLiters: 1d));
        var invalidCapacityWithoutFormation = FuelV2FuelCheckpointCalculator.From(
            FuelV2EffectiveCapacityResolver.From(-1d, 1d, 1d));

        Assert.Contains(FuelV2FuelCheckpointInputKind.PlannedServiceAdd, invalidChain.ProvidedInputKinds);
        Assert.Contains(FuelV2FuelCheckpointInputKind.ExpectedBoxToPitExitFuel, invalidChain.ProvidedInputKinds);
        Assert.Null(invalidChain.ServiceComplete);
        Assert.Null(invalidChain.ExpectedPitExit);
        Assert.Equal(FuelV2FuelCheckpointSelectionState.Invalid,
            invalidChain.Select(FuelV2FuelCheckpointKind.ServiceComplete).State);
        Assert.Equal(FuelV2FuelCheckpointSelectionState.Invalid,
            invalidChain.Select(FuelV2FuelCheckpointKind.ExpectedPitExit).State);

        Assert.Equal(FuelV2FuelCheckpointSelectionState.Conflicted,
            conflictedChain.Select(FuelV2FuelCheckpointKind.ExpectedAtBox).State);
        Assert.Null(conflictedChain.ServiceComplete);
        Assert.Null(conflictedChain.ExpectedPitExit);
        Assert.Equal(FuelV2FuelCheckpointSelectionState.Conflicted,
            conflictedChain.Select(FuelV2FuelCheckpointKind.ServiceComplete).State);
        Assert.Equal(FuelV2FuelCheckpointSelectionState.Conflicted,
            conflictedChain.Select(FuelV2FuelCheckpointKind.ExpectedPitExit).State);

        Assert.Equal(FuelV2FuelCheckpointSelectionState.Invalid,
            invalidCapacityFirstGreen.Select(FuelV2FuelCheckpointKind.FirstGreen).State);
        Assert.Equal(FuelV2FuelCheckpointSelectionState.Unavailable,
            invalidCapacityWithoutFormation.Select(FuelV2FuelCheckpointKind.FirstGreen).State);

        var noServiceProjectionRequested = FuelV2FuelCheckpointCalculator.From(
            capacity,
            new FuelV2FuelCheckpointInputs(CurrentFuelLiters: -1d));
        Assert.Equal(FuelV2FuelCheckpointSelectionState.Unavailable,
            noServiceProjectionRequested.Select(FuelV2FuelCheckpointKind.ExpectedAtBox).State);
        Assert.Equal(FuelV2FuelCheckpointSelectionState.Unavailable,
            noServiceProjectionRequested.Select(FuelV2FuelCheckpointKind.ServiceComplete).State);
        Assert.Equal(FuelV2FuelCheckpointSelectionState.Unavailable,
            noServiceProjectionRequested.Select(FuelV2FuelCheckpointKind.ExpectedPitExit).State);
    }

    [Fact]
    public void Composer_PlanUsesLapBudgetSelectionsAndCarriesDegradedContext()
    {
        var capacity = FuelV2EffectiveCapacityResolver.From(60d, 1d, 1d);
        var checkpoints = FuelV2FuelCheckpointCalculator.From(
            capacity,
            new FuelV2FuelCheckpointInputs(MeasuredFirstGreenFuelLiters: 60d));
        var lapBudget = LapBudget() with { CanDriveFuelAdvice = false };

        var snapshot = FuelV2SnapshotComposer.From(new FuelV2SnapshotCompositionInputs(
            LapBudget: lapBudget,
            FuelCheckpoints: checkpoints,
            BurnWindows: FuelV2FuelPerLapCalculator.FromAcceptedLaps([10d]),
            Boundary: new FuelV2BoundaryComposition(null, 0d, 0d),
            TargetUsage: new FuelV2TargetUsageComposition(
                FuelV2FuelCheckpointKind.FirstGreen,
                FuelV2BurnBucketId.Last,
                [6]),
            Plan: new FuelV2FullRacePlanComposition(
                FuelV2LapBudgetValueKind.PrimaryLapsRemaining,
                FuelV2LapBudgetValueKind.PossibleLapsRemaining,
                FuelV2FuelCheckpointKind.FirstGreen,
                FuelV2BurnBucketId.Last,
                Options: null)));

        Assert.Equal(12d, snapshot.PlanDependencies?.PlannedRaceLaps);
        Assert.Equal(11.5d, snapshot.PlanDependencies?.RaceLapsRemaining);
        Assert.Contains(FuelV2PlanStateFlag.DegradedLapBudget, Assert.IsType<FuelV2PlanSnapshot>(snapshot.Plan).StateFlags);
        Assert.Equal(FuelV2WorkbenchTone.Warning, snapshot.Plan?.Tone);
    }

    [Fact]
    public void FromBoundary_RejectsAProjectionFromDifferentCheckpointOwnership()
    {
        var capacity = FuelV2EffectiveCapacityResolver.From(60d, 1d, 1d);
        var firstCheckpoints = FuelV2FuelCheckpointCalculator.From(
            capacity,
            new FuelV2FuelCheckpointInputs(CurrentFuelLiters: 20d, MeasuredAtBoxFuelLiters: 10d));
        var secondCheckpoints = FuelV2FuelCheckpointCalculator.From(
            capacity,
            new FuelV2FuelCheckpointInputs(CurrentFuelLiters: 20d, MeasuredAtBoxFuelLiters: 10d));
        var boundary = FuelV2BoundaryFeasibilityCalculator.From(
            firstCheckpoints,
            FuelV2FuelPerLapCalculator.FromAcceptedLaps([10d]),
            targetLaps: 2);

        Assert.Throws<ArgumentException>(() => FuelV2PitRequestCalculator.FromBoundary(secondCheckpoints, boundary));
    }

    private static FuelV2LapBudgetProjection LapBudget()
    {
        return new FuelV2LapBudgetProjection(
            PrimaryLapsRemaining: 12,
            PossibleLapsRemaining: 11.5d,
            EstimatedFinishLap: 42.5d,
            ProjectionSource: LiveRaceLapBudgetSource.TimedLiveClock,
            ActionableSource: LiveRaceLapBudgetSource.TimedLiveClock,
            Confidence: LiveRaceLapBudgetConfidence.High,
            CanDriveFuelAdvice: true,
            StateFlags: []);
    }

    private static double Value(FuelV2Scalar? value)
    {
        return Assert.IsType<double>(value?.Value);
    }

    private static FuelV2ComposedSnapshot ComposeWithBudget(
        FuelV2FuelCheckpointSnapshot checkpoints,
        FuelV2FuelPerLapWindows windows,
        FuelV2FuelCheckpointKind budgetKind)
    {
        return FuelV2SnapshotComposer.From(new FuelV2SnapshotCompositionInputs(
            LapBudget: LapBudget(),
            FuelCheckpoints: checkpoints,
            BurnWindows: windows,
            Boundary: new FuelV2BoundaryComposition(2, 0d, 0d),
            TargetUsage: new FuelV2TargetUsageComposition(
                budgetKind,
                FuelV2BurnBucketId.Last,
                [2]),
            Plan: new FuelV2FullRacePlanComposition(
                FuelV2LapBudgetValueKind.PrimaryLapsRemaining,
                FuelV2LapBudgetValueKind.PrimaryLapsRemaining,
                budgetKind,
                FuelV2BurnBucketId.Last,
                Options: null)));
    }
}
