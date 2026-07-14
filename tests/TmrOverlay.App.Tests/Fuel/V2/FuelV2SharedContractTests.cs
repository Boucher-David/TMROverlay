using System.Text.Json;
using TmrOverlay.Core.Fuel.V2;
using TmrOverlay.Core.Telemetry.Live;
using Xunit;

namespace TmrOverlay.App.Tests.Fuel.V2;

public sealed class FuelV2SharedContractTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    [Fact]
    public void ComposedSnapshot_MatchesEveryDirectCalculatorForThePopulatedControl()
    {
        var inputs = PopulatedInputs();
        var composed = FuelV2SnapshotComposer.From(inputs);
        var boundary = FuelV2BoundaryFeasibilityCalculator.From(
            inputs.FuelCheckpoints,
            inputs.BurnWindows,
            inputs.Boundary.TargetLaps,
            inputs.Boundary.ReserveLiters,
            inputs.Boundary.PitLaneFuelLiters);
        var targetBudget = inputs.FuelCheckpoints.Select(inputs.TargetUsage.FuelBudgetCheckpointKind);
        var directPlan = FuelV2PlanCalculator.FromCurrentCheckpointFuelBudget(
            plannedRaceLaps: 12d,
            raceLapsRemaining: 12d,
            currentFuelLiters: inputs.FuelCheckpoints.Select(FuelV2FuelCheckpointKind.Current).CalculationLiters,
            currentBurn: inputs.BurnWindows.Last,
            futureFuelLiters: inputs.FuelCheckpoints.Select(FuelV2FuelCheckpointKind.ServiceComplete).CalculationLiters,
            futureBurn: inputs.BurnWindows.FiveLapAverage);

        AssertJsonEqual(boundary, composed.BoundaryFeasibility);
        AssertJsonEqual(
            FuelV2RangeCalculator.From(inputs.FuelCheckpoints.Current?.Liters, inputs.BurnWindows),
            composed.Range);
        AssertJsonEqual(
            FuelV2PitRequestCalculator.From(
                inputs.FuelCheckpoints,
                inputs.Boundary.TargetLaps!.Value,
                inputs.BurnWindows,
                inputs.Boundary.ReserveLiters,
                inputs.Boundary.PitLaneFuelLiters),
            composed.PitRequest);
        AssertJsonEqual(
            FuelV2TargetUsageCalculator.From(
                targetBudget.CalculationLiters,
                targetBudget.SourceLabel,
                inputs.BurnWindows.FiveLapAverage,
                inputs.TargetUsage.TargetLaps),
            composed.TargetUsage);
        AssertJsonEqual(directPlan, composed.Plan);
    }

    [Theory]
    [InlineData(30d, 3, 10d, true, (int)FuelV2RangeBoundaryState.Available)]
    [InlineData(29.99999d, 2, 0.00001d, false, (int)FuelV2RangeBoundaryState.Available)]
    [InlineData(30.00001d, 3, 9.99999d, false, (int)FuelV2RangeBoundaryState.Available)]
    [InlineData(0d, 0, 10d, true, (int)FuelV2RangeBoundaryState.KnownZero)]
    public void ComposedSnapshot_PreservesExactNearAndKnownZeroBoundaries(
        double currentFuelLiters,
        int expectedSafeLaps,
        double expectedNextLapFuel,
        bool expectedExactBoundary,
        int expectedRangeState)
    {
        var checkpoints = FuelV2FuelCheckpointCalculator.From(
            ResolvedCapacity(60d),
            new FuelV2FuelCheckpointInputs(
                MeasuredFirstGreenFuelLiters: 60d,
                CurrentFuelLiters: currentFuelLiters,
                MeasuredAtBoxFuelLiters: currentFuelLiters == 0d ? 0d : 10d));
        var windows = FuelV2FuelPerLapCalculator.FromAcceptedLaps([10d]);
        var inputs = Inputs(
            checkpoints,
            windows,
            boundary: new FuelV2BoundaryComposition(5, 0d, 0d),
            targetUsage: new FuelV2TargetUsageComposition(
                FuelV2FuelCheckpointKind.Current,
                FuelV2BurnBucketId.Last,
                [3]),
            plan: null);

        var composed = FuelV2SnapshotComposer.From(inputs);
        var direct = FuelV2BoundaryFeasibilityCalculator.From(checkpoints, windows, 5);
        var cell = composed.BoundaryFeasibility.Bucket(FuelV2BurnBucketId.Last);

        AssertJsonEqual(direct, composed.BoundaryFeasibility);
        Assert.Equal((FuelV2RangeBoundaryState)expectedRangeState, cell.RangeState);
        Assert.Equal(expectedSafeLaps, cell.SafeWholeLaps);
        Assert.Equal(expectedNextLapFuel, Value(cell.FuelToNextCompleteLapLiters), precision: 8);
        Assert.Equal(expectedExactBoundary,
            cell.StateFlags.Contains(FuelV2BoundaryStateFlag.ExactLapBoundary));
    }

    [Fact]
    public void ComposedSnapshot_IsolatesTargetBoundaryAndPlanDependencies()
    {
        var baselineInputs = PopulatedInputs();
        var baseline = FuelV2SnapshotComposer.From(baselineInputs);

        var changedTarget = FuelV2SnapshotComposer.From(baselineInputs with
        {
            TargetUsage = baselineInputs.TargetUsage with { TargetLaps = [2, 3] }
        });
        AssertJsonEqual(baseline.BoundaryFeasibility, changedTarget.BoundaryFeasibility);
        AssertJsonEqual(baseline.Range, changedTarget.Range);
        AssertJsonEqual(baseline.PitRequest, changedTarget.PitRequest);
        AssertJsonEqual(baseline.Plan, changedTarget.Plan);
        AssertJsonNotEqual(baseline.TargetUsage, changedTarget.TargetUsage);

        var changedBoundary = FuelV2SnapshotComposer.From(baselineInputs with
        {
            Boundary = baselineInputs.Boundary with { ReserveLiters = 2d }
        });
        AssertJsonEqual(baseline.Range, changedBoundary.Range);
        AssertJsonEqual(baseline.TargetUsage, changedBoundary.TargetUsage);
        AssertJsonEqual(baseline.Plan, changedBoundary.Plan);
        AssertJsonNotEqual(baseline.BoundaryFeasibility, changedBoundary.BoundaryFeasibility);
        AssertJsonNotEqual(baseline.PitRequest, changedBoundary.PitRequest);

        var changedPlan = FuelV2SnapshotComposer.From(baselineInputs with { Plan = null });
        AssertJsonEqual(baseline.BoundaryFeasibility, changedPlan.BoundaryFeasibility);
        AssertJsonEqual(baseline.Range, changedPlan.Range);
        AssertJsonEqual(baseline.PitRequest, changedPlan.PitRequest);
        AssertJsonEqual(baseline.TargetUsage, changedPlan.TargetUsage);
        Assert.Null(changedPlan.Plan);
    }

    [Fact]
    public void ComposedSnapshot_PreservesMissingAndConflictingCapacityStates()
    {
        var windows = FuelV2FuelPerLapCalculator.FromAcceptedLaps([10d]);
        var missingCheckpoints = FuelV2FuelCheckpointCalculator.From(
            FuelV2EffectiveCapacityResolver.From(null, null, null),
            new FuelV2FuelCheckpointInputs(CurrentFuelLiters: 20d, MeasuredAtBoxFuelLiters: 10d));
        var conflictedCheckpoints = FuelV2FuelCheckpointCalculator.From(
            FuelV2EffectiveCapacityResolver.From(75d, 0.8d, 0.68d),
            new FuelV2FuelCheckpointInputs(CurrentFuelLiters: 20d, MeasuredAtBoxFuelLiters: 10d));

        var missing = ComposeCapacityControl(missingCheckpoints, windows);
        var conflicted = ComposeCapacityControl(conflictedCheckpoints, windows);

        Assert.Equal(FuelV2FuelCheckpointSelectionState.Unavailable,
            missing.FuelCheckpoints.Select(FuelV2FuelCheckpointKind.EffectiveCapacity).State);
        Assert.Equal(FuelV2TargetFeasibilityState.Unavailable,
            missing.BoundaryFeasibility.Bucket(FuelV2BurnBucketId.Last).FeasibilityState);
        Assert.Equal(10d, Value(Assert.IsType<FuelV2PitRequestCell>(missing.PitRequest?.Last).DesiredAddLiters));

        Assert.Equal(FuelV2FuelCheckpointSelectionState.Conflicted,
            conflicted.FuelCheckpoints.Select(FuelV2FuelCheckpointKind.EffectiveCapacity).State);
        Assert.Equal(FuelV2TargetFeasibilityState.CapacityConflicted,
            conflicted.BoundaryFeasibility.Bucket(FuelV2BurnBucketId.Last).FeasibilityState);
        Assert.Null(conflicted.TargetUsage.FuelBudgetLiters);

        AssertJsonEqual(
            FuelV2BoundaryFeasibilityCalculator.From(missingCheckpoints, windows, 2),
            missing.BoundaryFeasibility);
        AssertJsonEqual(
            FuelV2BoundaryFeasibilityCalculator.From(conflictedCheckpoints, windows, 2),
            conflicted.BoundaryFeasibility);
    }

    [Fact]
    public void ComposedSnapshot_RetainsSeedOnlyEvidenceWithoutImplicitlySelectingAPlan()
    {
        var qualifyingSeed = FuelV2Scalar.From(
            12d,
            "replay qualifying seed",
            FuelV2Confidence.Seeded,
            burnSource: FuelV2BurnSource.QualifyingSeed,
            sampleCount: 1,
            strategyEligible: false);
        var windows = FuelV2FuelPerLapCalculator.FromAcceptedLaps(
            [],
            new FuelV2FuelPerLapWindowOptions(QualifyingSeed: qualifyingSeed));
        var checkpoints = FuelV2FuelCheckpointCalculator.From(
            ResolvedCapacity(60d),
            new FuelV2FuelCheckpointInputs(
                MeasuredFirstGreenFuelLiters: 60d,
                CurrentFuelLiters: 24d,
                MeasuredAtBoxFuelLiters: 12d));
        var inputs = Inputs(
            checkpoints,
            windows,
            boundary: new FuelV2BoundaryComposition(4, 0d, 0d),
            targetUsage: new FuelV2TargetUsageComposition(
                FuelV2FuelCheckpointKind.FirstGreen,
                FuelV2BurnBucketId.Qualifying,
                [4, 5]),
            plan: null);

        var composed = FuelV2SnapshotComposer.From(inputs);
        var qualifying = composed.BoundaryFeasibility.Bucket(FuelV2BurnBucketId.Qualifying);

        Assert.Equal(FuelV2RangeBoundaryState.Unavailable,
            composed.BoundaryFeasibility.Bucket(FuelV2BurnBucketId.Last).RangeState);
        Assert.Equal(2d, Value(qualifying.FractionalRangeLaps));
        Assert.Same(windows.QualifyingSeed, qualifying.Burn);
        Assert.Same(windows.QualifyingSeed, composed.TargetUsage.ReferenceBurn);
        Assert.False(Assert.IsType<FuelV2Scalar>(qualifying.Burn).StrategyEligible);
        Assert.Null(composed.PlanComposition);
        Assert.Null(composed.PlanDependencies);
        Assert.Null(composed.Plan);
        AssertJsonEqual(
            FuelV2TargetUsageCalculator.From(
                60d,
                "measured first-green telemetry",
                windows.QualifyingSeed,
                [4, 5]),
            composed.TargetUsage);
    }

    [Fact]
    public void ComposedSnapshot_RetainsTheOrderedStartAndPitCheckpointFlow()
    {
        var checkpoints = FuelV2FuelCheckpointCalculator.From(
            ResolvedCapacity(60d),
            new FuelV2FuelCheckpointInputs(
                EstimatedFormationFuelLiters: 1d,
                CurrentFuelLiters: 30d,
                ExpectedFuelToBoxLiters: 2d,
                PlannedServiceAddLiters: 20d,
                ExpectedBoxToPitExitFuelLiters: 0.5d));
        var windows = FuelV2FuelPerLapCalculator.FromAcceptedLaps([10d]);
        var inputs = Inputs(
            checkpoints,
            windows,
            boundary: new FuelV2BoundaryComposition(3, 0d, 0d),
            targetUsage: new FuelV2TargetUsageComposition(
                FuelV2FuelCheckpointKind.FirstGreen,
                FuelV2BurnBucketId.Last,
                [5, 6]),
            plan: new FuelV2CurrentCheckpointPlanComposition(
                FuelV2LapBudgetValueKind.PrimaryLapsRemaining,
                FuelV2LapBudgetValueKind.PossibleLapsRemaining,
                FuelV2FuelCheckpointKind.Current,
                FuelV2BurnBucketId.Last,
                FuelV2FuelCheckpointKind.ServiceComplete,
                FuelV2BurnBucketId.Last,
                Options: null));

        var composed = FuelV2SnapshotComposer.From(inputs);
        var orderedKinds = new[]
        {
            FuelV2FuelCheckpointKind.EffectiveCapacity,
            FuelV2FuelCheckpointKind.FirstGreen,
            FuelV2FuelCheckpointKind.Current,
            FuelV2FuelCheckpointKind.ExpectedAtBox,
            FuelV2FuelCheckpointKind.ServiceComplete,
            FuelV2FuelCheckpointKind.ExpectedPitExit
        };
        var selections = orderedKinds.Select(composed.FuelCheckpoints.Select).ToArray();

        Assert.Equal(orderedKinds, selections.Select(selection => selection.Kind));
        Assert.All(selections, selection => Assert.Equal(FuelV2FuelCheckpointSelectionState.Available, selection.State));
        Assert.Equal([60d, 59d, 30d, 28d, 48d, 47.5d],
            selections.Select(selection => selection.CalculationLiters!.Value));
        Assert.Equal(FuelV2FuelCheckpointKind.FirstGreen, composed.TargetUsageFuelBudget.Kind);
        Assert.Equal(FuelV2FuelCheckpointKind.Current, composed.PlanDependencies?.FuelBudget.Kind);
        Assert.Equal(FuelV2FuelCheckpointKind.ServiceComplete, composed.PlanDependencies?.FutureFuelBudget?.Kind);
        Assert.Equal(FuelV2FuelCheckpointKind.ExpectedAtBox,
            composed.BoundaryFeasibility.ServiceBaselineCheckpoint?.Kind);
        Assert.Equal(FuelV2PitRequestTargetCheckpoint.ServiceComplete,
            composed.BoundaryFeasibility.ServiceTargetCheckpoint);
    }

    [Theory]
    [InlineData((int)FuelV2LapBudgetValueKind.PrimaryLapsRemaining, 12d)]
    [InlineData((int)FuelV2LapBudgetValueKind.PossibleLapsRemaining, 11.5d)]
    [InlineData((int)FuelV2LapBudgetValueKind.EstimatedFinishLap, 42.5d)]
    public void ComposedPlan_MatchesDirectPlanForEveryNamedLapBudgetValue(
        int valueKind,
        double expectedValue)
    {
        var expectedValueKind = (FuelV2LapBudgetValueKind)valueKind;
        var capacity = ResolvedCapacity(60d);
        var checkpoints = FuelV2FuelCheckpointCalculator.From(
            capacity,
            new FuelV2FuelCheckpointInputs(MeasuredFirstGreenFuelLiters: 60d));
        var windows = FuelV2FuelPerLapCalculator.FromAcceptedLaps([10d]);
        var inputs = Inputs(
            checkpoints,
            windows,
            boundary: new FuelV2BoundaryComposition(null, 0d, 0d),
            targetUsage: new FuelV2TargetUsageComposition(
                FuelV2FuelCheckpointKind.FirstGreen,
                FuelV2BurnBucketId.Last,
                [6]),
            plan: new FuelV2FullRacePlanComposition(
                expectedValueKind,
                expectedValueKind,
                FuelV2FuelCheckpointKind.FirstGreen,
                FuelV2BurnBucketId.Last,
                Options: null));

        var composed = FuelV2SnapshotComposer.From(inputs);
        var direct = FuelV2PlanCalculator.FromFuelBudget(
            expectedValue,
            expectedValue,
            60d,
            windows.Last);

        Assert.Equal(expectedValue, composed.PlanDependencies?.PlannedRaceLaps);
        Assert.Equal(expectedValue, composed.PlanDependencies?.RaceLapsRemaining);
        AssertJsonEqual(direct, composed.Plan);
    }

    [Fact]
    public void LiveAndReplayShapedControls_AreDeterministicAndDefensivelyCopied()
    {
        var liveTargets = new List<int> { 4, 5, 6 };
        var liveFlags = new List<FuelV2PlanStateFlag> { FuelV2PlanStateFlag.FinalStintEdge };
        var liveBase = PopulatedInputs();
        var liveInputs = liveBase with
        {
            TargetUsage = liveBase.TargetUsage with { TargetLaps = liveTargets },
            Plan = liveBase.Plan is FuelV2CurrentCheckpointPlanComposition plan
                ? plan with { Options = new FuelV2PlanOptions(liveFlags) }
                : null
        };
        var replayInputs = ReplayInputs();

        var firstLive = FuelV2SnapshotComposer.From(liveInputs);
        var secondLive = FuelV2SnapshotComposer.From(liveInputs);
        var firstReplay = FuelV2SnapshotComposer.From(replayInputs);
        var secondReplay = FuelV2SnapshotComposer.From(replayInputs);

        AssertJsonEqual(firstLive, secondLive);
        AssertJsonEqual(firstReplay, secondReplay);

        liveTargets.Clear();
        liveFlags.Clear();
        Assert.Equal([4, 5, 6], firstLive.TargetUsageComposition.TargetLaps);
        Assert.Contains(
            FuelV2PlanStateFlag.FinalStintEdge,
            Assert.IsType<FuelV2CurrentCheckpointPlanComposition>(firstLive.PlanComposition).Options!.StateFlags!);

        Assert.Equal(104.94d, firstReplay.Capacity.EffectiveCapacityLiters);
        Assert.Equal(102.8464d, firstReplay.FuelCheckpoints.FirstGreen?.Liters);
        Assert.Equal(61.64d, firstReplay.FuelCheckpoints.Current?.Liters);
        Assert.Null(firstReplay.FuelCheckpoints.ExpectedAtBox);
        Assert.Equal(FuelV2BurnSource.LiveLastLap, firstReplay.BurnWindows.Last?.BurnSource);
        Assert.Equal(10, firstReplay.BurnWindows.AcceptedLapCount);
        Assert.Equal(5, firstReplay.BurnWindows.FiveLapAverage?.SampleCount);
        Assert.True(firstReplay.BurnWindows.FiveLapAverage?.StrategyEligible);
        Assert.Equal(FuelV2FuelCheckpointKind.FirstGreen, firstReplay.PlanDependencies?.FuelBudget.Kind);
        Assert.Equal(FuelV2BurnBucketId.FiveLapAverage, firstReplay.PlanDependencies?.BurnBucketId);
        Assert.Null(firstReplay.PlanDependencies?.FutureFuelBudget);
    }

    private static FuelV2ComposedSnapshot ComposeCapacityControl(
        FuelV2FuelCheckpointSnapshot checkpoints,
        FuelV2FuelPerLapWindows windows)
    {
        return FuelV2SnapshotComposer.From(Inputs(
            checkpoints,
            windows,
            boundary: new FuelV2BoundaryComposition(2, 0d, 0d),
            targetUsage: new FuelV2TargetUsageComposition(
                FuelV2FuelCheckpointKind.EffectiveCapacity,
                FuelV2BurnBucketId.Last,
                [4]),
            plan: null));
    }

    private static FuelV2SnapshotCompositionInputs PopulatedInputs()
    {
        var checkpoints = FuelV2FuelCheckpointCalculator.From(
            FuelV2EffectiveCapacityResolver.From(100d, 0.6d, 0.6d),
            new FuelV2FuelCheckpointInputs(
                MeasuredFirstGreenFuelLiters: 58d,
                CurrentFuelLiters: 40d,
                MeasuredAtBoxFuelLiters: 10d,
                MeasuredServiceCompleteFuelLiters: 50d,
                MeasuredPitExitFuelLiters: 49d));
        var windows = FuelV2FuelPerLapCalculator.FromAcceptedLaps(
            [10d, 11d, 9d, 10d, 10d, 10d, 10d, 10d, 10d, 10d]);
        return Inputs(
            checkpoints,
            windows,
            boundary: new FuelV2BoundaryComposition(2, 1d, 0.5d),
            targetUsage: new FuelV2TargetUsageComposition(
                FuelV2FuelCheckpointKind.FirstGreen,
                FuelV2BurnBucketId.FiveLapAverage,
                [4, 5, 6]),
            plan: new FuelV2CurrentCheckpointPlanComposition(
                FuelV2LapBudgetValueKind.PrimaryLapsRemaining,
                FuelV2LapBudgetValueKind.PrimaryLapsRemaining,
                FuelV2FuelCheckpointKind.Current,
                FuelV2BurnBucketId.Last,
                FuelV2FuelCheckpointKind.ServiceComplete,
                FuelV2BurnBucketId.FiveLapAverage,
                Options: null));
    }

    private static FuelV2SnapshotCompositionInputs ReplayInputs()
    {
        var checkpoints = FuelV2FuelCheckpointCalculator.From(
            FuelV2EffectiveCapacityResolver.From(104.94d, 1d, 1d),
            new FuelV2FuelCheckpointInputs(
                MeasuredFirstGreenFuelLiters: 102.8464d,
                CurrentFuelLiters: 61.64d));
        var windows = FuelV2FuelPerLapCalculator.FromAcceptedLaps(
            [12.95d, 13d, 13.2d, 13.3d, 13.65d, 13.49d, 13.49d, 13.5d, 13.5d, 13.52d]);
        return new FuelV2SnapshotCompositionInputs(
            LapBudget: LapBudget() with
            {
                PrimaryLapsRemaining = 31,
                PossibleLapsRemaining = 30.96d,
                EstimatedFinishLap = 31d
            },
            FuelCheckpoints: checkpoints,
            BurnWindows: windows,
            Boundary: new FuelV2BoundaryComposition(null, 0d, 0d),
            TargetUsage: new FuelV2TargetUsageComposition(
                FuelV2FuelCheckpointKind.FirstGreen,
                FuelV2BurnBucketId.Last,
                [7, 8, 9]),
            Plan: new FuelV2FullRacePlanComposition(
                FuelV2LapBudgetValueKind.PrimaryLapsRemaining,
                FuelV2LapBudgetValueKind.PossibleLapsRemaining,
                FuelV2FuelCheckpointKind.FirstGreen,
                FuelV2BurnBucketId.FiveLapAverage,
                Options: null));
    }

    private static FuelV2SnapshotCompositionInputs Inputs(
        FuelV2FuelCheckpointSnapshot checkpoints,
        FuelV2FuelPerLapWindows windows,
        FuelV2BoundaryComposition boundary,
        FuelV2TargetUsageComposition targetUsage,
        FuelV2PlanComposition? plan)
    {
        return new FuelV2SnapshotCompositionInputs(
            LapBudget(),
            checkpoints,
            windows,
            boundary,
            targetUsage,
            plan);
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

    private static FuelV2EffectiveCapacitySnapshot ResolvedCapacity(double liters)
    {
        return FuelV2EffectiveCapacityResolver.From(liters, 1d, 1d);
    }

    private static void AssertJsonEqual<T>(T expected, T actual)
    {
        Assert.Equal(JsonSerializer.Serialize(expected, JsonOptions), JsonSerializer.Serialize(actual, JsonOptions));
    }

    private static void AssertJsonNotEqual<T>(T expected, T actual)
    {
        Assert.NotEqual(JsonSerializer.Serialize(expected, JsonOptions), JsonSerializer.Serialize(actual, JsonOptions));
    }

    private static double Value(FuelV2Scalar? scalar)
    {
        return Assert.IsType<double>(Assert.IsType<FuelV2Scalar>(scalar).Value);
    }
}
