using TmrOverlay.Core.Fuel.V2;
using Xunit;

namespace TmrOverlay.App.Tests.Fuel.V2;

public sealed class FuelV2BoundaryFeasibilityCalculatorTests
{
    [Fact]
    public void ExactBoundary_UsesExactMathForSafeLapsAndOneMoreCompleteLapEdge()
    {
        var snapshot = Calculate(
            capacity: ResolvedCapacity(60d),
            currentFuelLiters: 30d,
            atBoxFuelLiters: 10d,
            targetLaps: 5,
            burnLitersPerLap: 10d);

        var cell = snapshot.Bucket(FuelV2BurnBucketId.Last);
        Assert.Equal(FuelV2RangeBoundaryState.Available, cell.RangeState);
        Assert.Equal(3d, Value(cell.FractionalRangeLaps), precision: 9);
        Assert.Equal(3, cell.SafeWholeLaps);
        Assert.Equal(10d, Value(cell.FuelToNextCompleteLapLiters), precision: 9);
        Assert.Contains(FuelV2BoundaryStateFlag.ExactLapBoundary, cell.StateFlags);

        Assert.Equal(FuelV2TargetFeasibilityState.Feasible, cell.FeasibilityState);
        Assert.Equal(50d, Value(cell.DesiredFuelLiters), precision: 9);
        Assert.Equal(40d, Value(cell.DesiredAddLiters), precision: 9);
        Assert.Equal(50d, Value(cell.TankRoomLiters), precision: 9);
        Assert.Equal(40d, Value(cell.ClampedAddLiters), precision: 9);
        Assert.Equal(0d, Value(cell.ShortfallLiters), precision: 9);
        Assert.Equal(6, cell.MaximumFeasibleLaps);
    }

    [Theory]
    [InlineData(29.99999d, 2, 0.00001d)]
    [InlineData(30.00001d, 3, 9.99999d)]
    public void NearBoundary_DisplayRoundingCannotChangeSafeWholeLaps(
        double currentFuelLiters,
        int expectedSafeWholeLaps,
        double expectedFuelToNextLap)
    {
        var snapshot = Calculate(
            capacity: ResolvedCapacity(60d),
            currentFuelLiters: currentFuelLiters,
            atBoxFuelLiters: 10d,
            targetLaps: 3,
            burnLitersPerLap: 10d);

        var cell = snapshot.Bucket(FuelV2BurnBucketId.Last);
        Assert.Equal(expectedSafeWholeLaps, cell.SafeWholeLaps);
        Assert.Equal(expectedFuelToNextLap, Value(cell.FuelToNextCompleteLapLiters), precision: 8);
        Assert.DoesNotContain(FuelV2BoundaryStateFlag.ExactLapBoundary, cell.StateFlags);
    }

    [Fact]
    public void KnownZero_IsFactualForBothRangeAndServiceRatherThanUnavailable()
    {
        var snapshot = Calculate(
            capacity: ResolvedCapacity(60d),
            currentFuelLiters: 0d,
            atBoxFuelLiters: 0d,
            targetLaps: 2,
            burnLitersPerLap: 10d);

        var cell = snapshot.Bucket(FuelV2BurnBucketId.Last);
        Assert.Equal(FuelV2RangeBoundaryState.KnownZero, cell.RangeState);
        Assert.Equal(0d, Value(cell.FractionalRangeLaps));
        Assert.Equal(0, cell.SafeWholeLaps);
        Assert.Equal(10d, Value(cell.FuelToNextCompleteLapLiters));
        Assert.Equal(FuelV2TargetFeasibilityState.Feasible, cell.FeasibilityState);
        Assert.Equal(20d, Value(cell.DesiredAddLiters));
        Assert.Contains(FuelV2BoundaryStateFlag.KnownZeroRangeFuel, cell.StateFlags);
        Assert.Contains(FuelV2BoundaryStateFlag.KnownZeroServiceFuel, cell.StateFlags);
    }

    [Fact]
    public void ServiceFeasibility_UsesExpectedAtBoxRatherThanCurrentFuel()
    {
        var snapshot = Calculate(
            capacity: ResolvedCapacity(60d),
            currentFuelLiters: 40d,
            atBoxFuelLiters: 10d,
            targetLaps: 2,
            burnLitersPerLap: 10d);

        var cell = snapshot.Bucket(FuelV2BurnBucketId.Last);
        Assert.Equal(4d, Value(cell.FractionalRangeLaps));
        Assert.Equal(10d, Value(cell.DesiredAddLiters));
        Assert.Equal(FuelV2FuelCheckpointKind.Current, snapshot.RangeCheckpoint!.Kind);
        Assert.Equal(FuelV2FuelCheckpointKind.ExpectedAtBox, snapshot.ServiceBaselineCheckpoint!.Kind);
        Assert.Equal(FuelV2PitRequestTargetCheckpoint.ServiceComplete, snapshot.ServiceTargetCheckpoint);
    }

    [Fact]
    public void TankLimitedTarget_RetainsDesiredClampShortfallAndMaximumFeasibleLaps()
    {
        var snapshot = Calculate(
            capacity: ResolvedCapacity(50d),
            currentFuelLiters: 20d,
            atBoxFuelLiters: 10d,
            targetLaps: 6,
            burnLitersPerLap: 10d);

        var cell = snapshot.Bucket(FuelV2BurnBucketId.Last);
        Assert.Equal(FuelV2TargetFeasibilityState.Unachievable, cell.FeasibilityState);
        Assert.Equal(60d, Value(cell.DesiredFuelLiters));
        Assert.Equal(50d, Value(cell.DesiredAddLiters));
        Assert.Equal(40d, Value(cell.TankRoomLiters));
        Assert.Equal(40d, Value(cell.ClampedAddLiters));
        Assert.Equal(10d, Value(cell.ShortfallLiters));
        Assert.Equal(5, cell.MaximumFeasibleLaps);
        Assert.Contains(FuelV2BoundaryStateFlag.TankLimited, cell.StateFlags);
    }

    [Theory]
    [InlineData(0d, (int)FuelV2TargetFeasibilityState.Feasible, 5, 0d)]
    [InlineData(0.0001d, (int)FuelV2TargetFeasibilityState.Unachievable, 4, 0.0001d)]
    public void MarginCrossingCapacityBoundary_UsesMathematicalValuesRatherThanDisplayRounding(
        double reserveLiters,
        int expectedState,
        int expectedMaximumLaps,
        double expectedShortfall)
    {
        var snapshot = Calculate(
            capacity: ResolvedCapacity(50d),
            currentFuelLiters: 20d,
            atBoxFuelLiters: 10d,
            targetLaps: 5,
            burnLitersPerLap: 10d,
            reserveLiters: reserveLiters);

        var cell = snapshot.Bucket(FuelV2BurnBucketId.Last);
        Assert.Equal((FuelV2TargetFeasibilityState)expectedState, cell.FeasibilityState);
        Assert.Equal(expectedMaximumLaps, cell.MaximumFeasibleLaps);
        Assert.Equal(expectedShortfall, Value(cell.ShortfallLiters), precision: 8);
    }

    [Fact]
    public void ReserveAndPitLaneFuel_ReduceMaximumFeasibleLapsWithoutChangingBurnEvidence()
    {
        var snapshot = Calculate(
            capacity: ResolvedCapacity(50d),
            currentFuelLiters: 20d,
            atBoxFuelLiters: 10d,
            targetLaps: 4,
            burnLitersPerLap: 10d,
            reserveLiters: 2d,
            pitLaneFuelLiters: 1d);

        var cell = snapshot.Bucket(FuelV2BurnBucketId.Last);
        Assert.Equal(43d, Value(cell.DesiredFuelLiters));
        Assert.Equal(4, cell.MaximumFeasibleLaps);
        Assert.Contains(FuelV2SampleContextFlag.PitRoad, cell.DesiredFuelLiters!.ContextFlags);
        Assert.Equal(FuelV2BurnBucketId.Last, cell.DesiredFuelLiters.BurnBucketId);
        Assert.Equal(FuelV2BurnSource.LiveLastLap, cell.DesiredFuelLiters.BurnSource);
    }

    [Fact]
    public void MissingCapacity_RetainsDesiredFactsButNotTankOrAchievabilityFacts()
    {
        var snapshot = Calculate(
            capacity: FuelV2EffectiveCapacityResolver.From(null, null, null),
            currentFuelLiters: 20d,
            atBoxFuelLiters: 10d,
            targetLaps: 2,
            burnLitersPerLap: 10d);

        var cell = snapshot.Bucket(FuelV2BurnBucketId.Last);
        Assert.Equal(FuelV2TargetFeasibilityState.Unavailable, cell.FeasibilityState);
        Assert.Equal(20d, Value(cell.DesiredFuelLiters));
        Assert.Equal(10d, Value(cell.DesiredAddLiters));
        Assert.Null(cell.TankRoomLiters);
        Assert.Null(cell.ClampedAddLiters);
        Assert.Null(cell.ShortfallLiters);
        Assert.Null(cell.MaximumFeasibleLaps);
    }

    [Fact]
    public void ConflictingCapacity_RetainsDiagnosticMathButCannotClaimFeasibility()
    {
        var conflictedCapacity = FuelV2EffectiveCapacityResolver.From(75d, 0.8d, 0.68d);
        var snapshot = Calculate(
            capacity: conflictedCapacity,
            currentFuelLiters: 20d,
            atBoxFuelLiters: 10d,
            targetLaps: 5,
            burnLitersPerLap: 10d);

        var cell = snapshot.Bucket(FuelV2BurnBucketId.Last);
        Assert.Equal(51d, snapshot.Capacity.EffectiveCapacityLiters);
        Assert.Equal(FuelV2TargetFeasibilityState.CapacityConflicted, cell.FeasibilityState);
        Assert.Equal(41d, Value(cell.TankRoomLiters));
        Assert.Equal(40d, Value(cell.ClampedAddLiters));
        Assert.Equal(0d, Value(cell.ShortfallLiters));
        Assert.Equal(5, cell.MaximumFeasibleLaps);
    }

    [Fact]
    public void TargetAlreadyCovered_ProducesKnownZeroAddWithoutBecomingUnavailable()
    {
        var snapshot = Calculate(
            capacity: ResolvedCapacity(60d),
            currentFuelLiters: 30d,
            atBoxFuelLiters: 30d,
            targetLaps: 2,
            burnLitersPerLap: 10d);

        var cell = snapshot.Bucket(FuelV2BurnBucketId.Last);
        Assert.Equal(FuelV2TargetFeasibilityState.Feasible, cell.FeasibilityState);
        Assert.Equal(0d, Value(cell.DesiredAddLiters));
        Assert.Equal(0d, Value(cell.ClampedAddLiters));
        Assert.Contains(FuelV2BoundaryStateFlag.TargetAlreadyCovered, cell.StateFlags);
    }

    [Theory]
    [InlineData(-1d, 0d)]
    [InlineData(0d, -1d)]
    [InlineData(double.NaN, 0d)]
    [InlineData(0d, double.PositiveInfinity)]
    public void InvalidAdjustments_AreDistinctFromUnavailableAndProduceNoServiceMath(
        double reserveLiters,
        double pitLaneFuelLiters)
    {
        var snapshot = Calculate(
            capacity: ResolvedCapacity(60d),
            currentFuelLiters: 20d,
            atBoxFuelLiters: 10d,
            targetLaps: 2,
            burnLitersPerLap: 10d,
            reserveLiters: reserveLiters,
            pitLaneFuelLiters: pitLaneFuelLiters);

        var cell = snapshot.Bucket(FuelV2BurnBucketId.Last);
        Assert.Equal(FuelV2TargetFeasibilityState.Invalid, cell.FeasibilityState);
        Assert.Null(cell.DesiredFuelLiters);
        Assert.Equal(FuelV2RangeBoundaryState.Available, cell.RangeState);
    }

    [Fact]
    public void InvalidTypedBurn_IsDistinctFromMissingBucket()
    {
        var invalidBurn = FuelV2Scalar.From(
            -10d,
            "invalid burn",
            FuelV2Confidence.Contextual,
            burnBucketId: FuelV2BurnBucketId.Last,
            burnSource: FuelV2BurnSource.LiveLastLap,
            sampleCount: 1);
        var windows = new FuelV2FuelPerLapWindows(invalidBurn, null, null, null, null, null, 0);
        var checkpoints = Checkpoints(ResolvedCapacity(60d), 20d, 10d);

        var snapshot = FuelV2BoundaryFeasibilityCalculator.From(checkpoints, windows, 2);

        Assert.Equal(FuelV2RangeBoundaryState.Invalid, snapshot.Bucket(FuelV2BurnBucketId.Last).RangeState);
        Assert.Equal(FuelV2TargetFeasibilityState.Invalid, snapshot.Bucket(FuelV2BurnBucketId.Last).FeasibilityState);
        Assert.Equal(FuelV2RangeBoundaryState.Unavailable, snapshot.Bucket(FuelV2BurnBucketId.FiveLapAverage).RangeState);
        Assert.Equal(FuelV2TargetFeasibilityState.Unavailable, snapshot.Bucket(FuelV2BurnBucketId.FiveLapAverage).FeasibilityState);
    }

    [Fact]
    public void InvalidCurrentTelemetry_IsDistinctFromMissingWithoutPoisoningMeasuredAtBoxService()
    {
        var checkpoints = FuelV2FuelCheckpointCalculator.From(
            ResolvedCapacity(60d),
            new FuelV2FuelCheckpointInputs(
                CurrentFuelLiters: -1d,
                MeasuredAtBoxFuelLiters: 10d));

        var snapshot = FuelV2BoundaryFeasibilityCalculator.From(
            checkpoints,
            FuelV2FuelPerLapCalculator.FromAcceptedLaps([10d]),
            targetLaps: 2);

        var cell = snapshot.Bucket(FuelV2BurnBucketId.Last);
        Assert.Contains(FuelV2FuelCheckpointInputKind.CurrentFuel, checkpoints.InvalidInputKinds);
        Assert.Equal(FuelV2RangeBoundaryState.Invalid, cell.RangeState);
        Assert.Equal(FuelV2TargetFeasibilityState.Feasible, cell.FeasibilityState);
        Assert.Equal(10d, Value(cell.DesiredAddLiters));
    }

    [Fact]
    public void InvalidMeasuredAtBoxTelemetry_CannotFallBackToAFeasibleProjection()
    {
        var checkpoints = FuelV2FuelCheckpointCalculator.From(
            ResolvedCapacity(60d),
            new FuelV2FuelCheckpointInputs(
                CurrentFuelLiters: 20d,
                MeasuredAtBoxFuelLiters: -1d,
                ExpectedFuelToBoxLiters: 1d));
        Assert.Equal(19d, checkpoints.ExpectedAtBox!.Liters);

        var snapshot = FuelV2BoundaryFeasibilityCalculator.From(
            checkpoints,
            FuelV2FuelPerLapCalculator.FromAcceptedLaps([10d]),
            targetLaps: 2);

        var cell = snapshot.Bucket(FuelV2BurnBucketId.Last);
        Assert.Contains(FuelV2FuelCheckpointInputKind.MeasuredAtBoxFuel, checkpoints.InvalidInputKinds);
        Assert.Equal(FuelV2RangeBoundaryState.Available, cell.RangeState);
        Assert.Equal(FuelV2TargetFeasibilityState.Invalid, cell.FeasibilityState);
        Assert.Equal(20d, Value(cell.DesiredFuelLiters));
        Assert.Null(cell.DesiredAddLiters);
    }

    [Fact]
    public void InvalidCapacityEvidence_IsDistinctFromMissingCapacity()
    {
        var invalidCapacity = FuelV2EffectiveCapacityResolver.From(-1d, 1d, 1d);
        var invalid = Calculate(invalidCapacity, 20d, 10d, 2, 10d)
            .Bucket(FuelV2BurnBucketId.Last);
        var missing = Calculate(
                FuelV2EffectiveCapacityResolver.From(null, null, null),
                20d,
                10d,
                2,
                10d)
            .Bucket(FuelV2BurnBucketId.Last);

        Assert.Equal(FuelV2TargetFeasibilityState.Invalid, invalid.FeasibilityState);
        Assert.Equal(20d, Value(invalid.DesiredFuelLiters));
        Assert.Equal(10d, Value(invalid.DesiredAddLiters));
        Assert.Null(invalid.MaximumFeasibleLaps);
        Assert.Equal(FuelV2TargetFeasibilityState.Unavailable, missing.FeasibilityState);
    }

    [Fact]
    public void PositiveBurnWithUnavailableTypedSource_IsInvalidRatherThanInferred()
    {
        var incompleteBurn = FuelV2Scalar.From(
            10d,
            "typed ID without typed source",
            FuelV2Confidence.Contextual,
            burnBucketId: FuelV2BurnBucketId.Last,
            burnSource: FuelV2BurnSource.Unavailable,
            sampleCount: 1);
        var windows = new FuelV2FuelPerLapWindows(incompleteBurn, null, null, null, null, null, 0);

        var snapshot = FuelV2BoundaryFeasibilityCalculator.From(
            Checkpoints(ResolvedCapacity(60d), 20d, 10d),
            windows,
            targetLaps: 2);

        Assert.Equal(FuelV2RangeBoundaryState.Invalid, snapshot.Bucket(FuelV2BurnBucketId.Last).RangeState);
        Assert.Equal(FuelV2TargetFeasibilityState.Invalid, snapshot.Bucket(FuelV2BurnBucketId.Last).FeasibilityState);
    }

    [Fact]
    public void FiniteInputsThatOverflowNextLapEdge_AreInvalidRatherThanPartiallyAvailable()
    {
        var snapshot = Calculate(
            capacity: ResolvedCapacity(60d),
            currentFuelLiters: double.MaxValue,
            atBoxFuelLiters: 10d,
            targetLaps: null,
            burnLitersPerLap: double.MaxValue);

        var cell = snapshot.Bucket(FuelV2BurnBucketId.Last);
        Assert.Equal(FuelV2RangeBoundaryState.Invalid, cell.RangeState);
        Assert.Null(cell.FractionalRangeLaps);
        Assert.Null(cell.FuelToNextCompleteLapLiters);
        Assert.DoesNotContain(FuelV2BoundaryStateFlag.ExactLapBoundary, cell.StateFlags);
    }

    [Fact]
    public void LegacyCurrentFuelRequest_UsesCurrentProvenanceWithoutManufacturingAtBoxTelemetry()
    {
        var checkpoints = FuelV2FuelCheckpointCalculator.From(
            ResolvedCapacity(60d),
            new FuelV2FuelCheckpointInputs(CurrentFuelLiters: 10d));

        var snapshot = FuelV2BoundaryFeasibilityCalculator.FromLegacyCurrentFuelRequest(
            checkpoints,
            FuelV2FuelPerLapCalculator.FromAcceptedLaps([10d]),
            targetLaps: 2);

        Assert.Null(checkpoints.ExpectedAtBox);
        Assert.Equal(FuelV2FuelCheckpointKind.Current, snapshot.ServiceBaselineCheckpoint!.Kind);
        Assert.Equal(FuelV2FuelCheckpointSource.MeasuredCurrentTelemetry, snapshot.ServiceBaselineCheckpoint.Source);
        Assert.Equal(10d, Value(snapshot.Bucket(FuelV2BurnBucketId.Last).DesiredAddLiters));
    }

    [Fact]
    public void SeedOnlyBucket_RetainsItsEvidenceWithoutBecomingASelectedStrategyProfile()
    {
        var seed = FuelV2Scalar.From(
            12d,
            "qualifying seed",
            FuelV2Confidence.Seeded,
            burnSource: FuelV2BurnSource.QualifyingSeed,
            sampleCount: 1,
            strategyEligible: false);
        var windows = FuelV2FuelPerLapCalculator.FromAcceptedLaps(
            [],
            new FuelV2FuelPerLapWindowOptions(QualifyingSeed: seed));
        var checkpoints = Checkpoints(ResolvedCapacity(60d), 24d, 12d);

        var snapshot = FuelV2BoundaryFeasibilityCalculator.From(checkpoints, windows, 4);

        var qualifying = snapshot.Bucket(FuelV2BurnBucketId.Qualifying);
        Assert.Equal(2d, Value(qualifying.FractionalRangeLaps));
        Assert.Equal(FuelV2TargetFeasibilityState.Feasible, qualifying.FeasibilityState);
        Assert.Equal(FuelV2BurnSource.QualifyingSeed, qualifying.Burn!.BurnSource);
        Assert.False(qualifying.Burn.StrategyEligible);
        Assert.Equal(FuelV2RangeBoundaryState.Unavailable, snapshot.Bucket(FuelV2BurnBucketId.Last).RangeState);
    }

    private static FuelV2BoundaryFeasibilitySnapshot Calculate(
        FuelV2EffectiveCapacitySnapshot capacity,
        double? currentFuelLiters,
        double? atBoxFuelLiters,
        int? targetLaps,
        double burnLitersPerLap,
        double reserveLiters = 0d,
        double pitLaneFuelLiters = 0d)
    {
        return FuelV2BoundaryFeasibilityCalculator.From(
            Checkpoints(capacity, currentFuelLiters, atBoxFuelLiters),
            FuelV2FuelPerLapCalculator.FromAcceptedLaps([burnLitersPerLap]),
            targetLaps,
            reserveLiters,
            pitLaneFuelLiters);
    }

    private static FuelV2FuelCheckpointSnapshot Checkpoints(
        FuelV2EffectiveCapacitySnapshot capacity,
        double? currentFuelLiters,
        double? atBoxFuelLiters)
    {
        return FuelV2FuelCheckpointCalculator.From(
            capacity,
            new FuelV2FuelCheckpointInputs(
                CurrentFuelLiters: currentFuelLiters,
                MeasuredAtBoxFuelLiters: atBoxFuelLiters));
    }

    private static FuelV2EffectiveCapacitySnapshot ResolvedCapacity(double liters)
    {
        return FuelV2EffectiveCapacityResolver.From(liters, 1d, 1d);
    }

    private static double Value(FuelV2Scalar? scalar)
    {
        return Assert.IsType<double>(Assert.IsType<FuelV2Scalar>(scalar).Value);
    }
}
