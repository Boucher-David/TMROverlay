using TmrOverlay.Core.Fuel.V2;
using Xunit;

namespace TmrOverlay.App.Tests.Fuel.V2;

public sealed class FuelV2CapacityAndCheckpointCalculatorTests
{
    [Fact]
    public void Capacity_MatchingDriverAndClassCapsResolveAuthoritativeLimitedFuel()
    {
        var capacity = FuelV2EffectiveCapacityResolver.From(
            physicalTankCapacityLiters: 75d,
            driverCarMaxFuelPercent: 0.8d,
            carClassMaxFuelPercent: 0.8d,
            maxObservedFuelLiters: 58.99d);

        Assert.Equal(60d, capacity.EffectiveCapacityLiters);
        Assert.Equal(0.8d, capacity.AppliedFuelPercent);
        Assert.Equal(FuelV2CapacitySource.MatchingDriverAndClassCaps, capacity.Source);
        Assert.Equal(FuelV2CapacityConfidence.Authoritative, capacity.Confidence);
        Assert.Contains(FuelV2CapacityStateFlag.Limited, capacity.StateFlags);
        Assert.True(capacity.CanDriveFuelAdvice);
    }

    [Fact]
    public void Capacity_SingleDirectDriverCapCanResolveWithoutClassDuplication()
    {
        var capacity = FuelV2EffectiveCapacityResolver.From(75d, 0.68d, null);

        Assert.Equal(51d, capacity.EffectiveCapacityLiters);
        Assert.Equal(FuelV2CapacitySource.DriverCarCap, capacity.Source);
        Assert.Equal(FuelV2CapacityConfidence.High, capacity.Confidence);
        Assert.True(capacity.CanDriveFuelAdvice);
    }

    [Fact]
    public void Capacity_SingleClassCapCanResolveWithoutDriverDuplication()
    {
        var capacity = FuelV2EffectiveCapacityResolver.From(75d, null, 0.68d);

        Assert.Equal(51d, capacity.EffectiveCapacityLiters);
        Assert.Equal(FuelV2CapacitySource.CarClassCap, capacity.Source);
        Assert.Equal(FuelV2CapacityConfidence.High, capacity.Confidence);
        Assert.True(capacity.CanDriveFuelAdvice);
    }

    [Fact]
    public void Capacity_MatchingFullCapsRemainExplicitlyUnrestricted()
    {
        var capacity = FuelV2EffectiveCapacityResolver.From(75d, 1d, 1d);

        Assert.Equal(75d, capacity.EffectiveCapacityLiters);
        Assert.Equal(1d, capacity.AppliedFuelPercent);
        Assert.Contains(FuelV2CapacityStateFlag.Unrestricted, capacity.StateFlags);
        Assert.True(capacity.CanDriveFuelAdvice);
    }

    [Fact]
    public void Capacity_MissingPhysicalTankCannotProduceAnEffectiveCapacity()
    {
        var capacity = FuelV2EffectiveCapacityResolver.From(null, 0.8d, 0.8d);

        Assert.Null(capacity.EffectiveCapacityLiters);
        Assert.Equal(FuelV2CapacitySource.Unavailable, capacity.Source);
        Assert.Equal(FuelV2CapacityConfidence.Unavailable, capacity.Confidence);
        Assert.Contains(FuelV2CapacityStateFlag.MissingPhysicalCapacity, capacity.StateFlags);
        Assert.False(capacity.CanDriveFuelAdvice);
    }

    [Fact]
    public void Capacity_PhysicalTankWithoutCapEvidenceDoesNotSilentlyBecomeEffectiveCapacity()
    {
        var capacity = FuelV2EffectiveCapacityResolver.From(75d, null, null);

        Assert.Equal(75d, capacity.PhysicalTankCapacityLiters);
        Assert.Null(capacity.EffectiveCapacityLiters);
        Assert.Equal(FuelV2CapacitySource.PhysicalTankOnly, capacity.Source);
        Assert.Equal(FuelV2CapacityConfidence.Contextual, capacity.Confidence);
        Assert.Contains(FuelV2CapacityStateFlag.MissingCapEvidence, capacity.StateFlags);
        Assert.False(capacity.CanDriveFuelAdvice);
    }

    [Fact]
    public void Capacity_ConflictingCapsRetainConservativeValueButCannotDriveAdvice()
    {
        var capacity = FuelV2EffectiveCapacityResolver.From(75d, 0.8d, 0.68d);

        Assert.Equal(51d, capacity.EffectiveCapacityLiters);
        Assert.Equal(0.68d, capacity.AppliedFuelPercent);
        Assert.Equal(FuelV2CapacitySource.MostRestrictiveReportedCap, capacity.Source);
        Assert.Equal(FuelV2CapacityConfidence.Conflicted, capacity.Confidence);
        Assert.Contains(FuelV2CapacityStateFlag.DriverClassCapConflict, capacity.StateFlags);
        Assert.False(capacity.CanDriveFuelAdvice);
    }

    [Fact]
    public void Capacity_ObservedFuelAboveResolvedCapIsExplicitConflict()
    {
        var capacity = FuelV2EffectiveCapacityResolver.From(75d, 0.68d, 0.68d, 52d);

        Assert.Equal(51d, capacity.EffectiveCapacityLiters);
        Assert.Contains(FuelV2CapacityStateFlag.ObservedFuelAboveResolvedCapacity, capacity.StateFlags);
        Assert.Equal(FuelV2CapacityConfidence.Conflicted, capacity.Confidence);
        Assert.False(capacity.CanDriveFuelAdvice);
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(1.01d)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Capacity_InvalidDriverCapCannotQuietlyBecomeTrusted(double invalidDriverCap)
    {
        var capacity = FuelV2EffectiveCapacityResolver.From(75d, invalidDriverCap, 0.8d);

        Assert.Equal(60d, capacity.EffectiveCapacityLiters);
        Assert.Contains(FuelV2CapacityStateFlag.InvalidDriverCap, capacity.StateFlags);
        Assert.Equal(FuelV2CapacityConfidence.Conflicted, capacity.Confidence);
        Assert.False(capacity.CanDriveFuelAdvice);
    }

    [Fact]
    public void Checkpoints_KeepEveryPitCycleStageDistinctAndApplyExitBurnOnce()
    {
        var snapshot = FuelV2FuelCheckpointCalculator.From(
            ResolvedCapacity(60d),
            new FuelV2FuelCheckpointInputs(
                MeasuredFirstGreenFuelLiters: 58.99d,
                CurrentFuelLiters: 33.29d,
                ExpectedFuelToBoxLiters: 1d,
                PlannedServiceAddLiters: 20d,
                ExpectedBoxToPitExitFuelLiters: 0.14d));

        AssertCheckpoint(snapshot.EffectiveCapacity, 60d, FuelV2FuelCheckpointSource.ResolvedEffectiveCapacity);
        AssertCheckpoint(snapshot.FirstGreen, 58.99d, FuelV2FuelCheckpointSource.MeasuredFirstGreenTelemetry);
        AssertCheckpoint(snapshot.Current, 33.29d, FuelV2FuelCheckpointSource.MeasuredCurrentTelemetry);
        AssertCheckpoint(snapshot.ExpectedAtBox, 32.29d, FuelV2FuelCheckpointSource.ProjectedCurrentToBox);
        AssertCheckpoint(snapshot.ServiceComplete, 52.29d, FuelV2FuelCheckpointSource.PlannedServiceAdd);
        AssertCheckpoint(snapshot.ExpectedPitExit, 52.15d, FuelV2FuelCheckpointSource.ProjectedBoxToPitExit);
        Assert.Equal(FuelV2PitRequestTargetCheckpoint.ServiceComplete, snapshot.PitRequestTargetCheckpoint);
    }

    [Fact]
    public void Checkpoints_SingleCapRetainsHighRatherThanAuthoritativeCapacityConfidence()
    {
        var snapshot = FuelV2FuelCheckpointCalculator.From(
            FuelV2EffectiveCapacityResolver.From(75d, 0.8d, null));

        AssertCheckpoint(snapshot.EffectiveCapacity, 60d, FuelV2FuelCheckpointSource.ResolvedEffectiveCapacity);
        Assert.Equal(FuelV2FuelCheckpointConfidence.High, snapshot.EffectiveCapacity!.Confidence);
    }

    [Fact]
    public void Checkpoints_MeasuredFactsOverrideAvailableProjections()
    {
        var snapshot = FuelV2FuelCheckpointCalculator.From(
            ResolvedCapacity(60d),
            new FuelV2FuelCheckpointInputs(
                CurrentFuelLiters: 30d,
                MeasuredAtBoxFuelLiters: 20d,
                ExpectedFuelToBoxLiters: 2d,
                MeasuredServiceCompleteFuelLiters: 50d,
                PlannedServiceAddLiters: 10d,
                MeasuredPitExitFuelLiters: 49.8d,
                ExpectedBoxToPitExitFuelLiters: 0.1d));

        AssertCheckpoint(snapshot.ExpectedAtBox, 20d, FuelV2FuelCheckpointSource.MeasuredAtBoxTelemetry);
        AssertCheckpoint(snapshot.ServiceComplete, 50d, FuelV2FuelCheckpointSource.MeasuredServiceCompleteTelemetry);
        AssertCheckpoint(snapshot.ExpectedPitExit, 49.8d, FuelV2FuelCheckpointSource.MeasuredPitExitTelemetry);
        Assert.Equal(FuelV2FuelCheckpointConfidence.Measured, snapshot.ExpectedPitExit!.Confidence);
    }

    [Fact]
    public void Checkpoints_FirstGreenEstimateIsExplicitlyDerivedFromCapacityAndFormationFuel()
    {
        var snapshot = FuelV2FuelCheckpointCalculator.From(
            ResolvedCapacity(51d),
            new FuelV2FuelCheckpointInputs(EstimatedFormationFuelLiters: 0.9d));

        AssertCheckpoint(snapshot.FirstGreen, 50.1d, FuelV2FuelCheckpointSource.EstimatedFromCapacityAndFormation);
        Assert.Equal(FuelV2FuelCheckpointConfidence.Estimated, snapshot.FirstGreen!.Confidence);
        Assert.Contains(FuelV2FuelCheckpointStateFlag.Estimated, snapshot.FirstGreen.StateFlags);
    }

    [Fact]
    public void Checkpoints_FormationEstimateCannotProduceNegativeFirstGreenFuel()
    {
        var snapshot = FuelV2FuelCheckpointCalculator.From(
            ResolvedCapacity(51d),
            new FuelV2FuelCheckpointInputs(EstimatedFormationFuelLiters: 52d));

        AssertCheckpoint(snapshot.FirstGreen, 0d, FuelV2FuelCheckpointSource.EstimatedFromCapacityAndFormation);
        Assert.Contains(FuelV2FuelCheckpointStateFlag.KnownZero, snapshot.FirstGreen!.StateFlags);
        Assert.Contains(FuelV2FuelCheckpointStateFlag.ProjectionClampedAtZero, snapshot.FirstGreen.StateFlags);
    }

    [Fact]
    public void Checkpoints_MissingCurrentFuelDoesNotFabricateFuturePitFacts()
    {
        var snapshot = FuelV2FuelCheckpointCalculator.From(
            ResolvedCapacity(60d),
            new FuelV2FuelCheckpointInputs(
                ExpectedFuelToBoxLiters: 1d,
                PlannedServiceAddLiters: 20d,
                ExpectedBoxToPitExitFuelLiters: 0.14d));

        Assert.Null(snapshot.Current);
        Assert.Null(snapshot.ExpectedAtBox);
        Assert.Null(snapshot.ServiceComplete);
        Assert.Null(snapshot.ExpectedPitExit);
    }

    [Fact]
    public void Checkpoints_KnownZeroRemainsDistinctFromUnavailable()
    {
        var snapshot = FuelV2FuelCheckpointCalculator.From(
            ResolvedCapacity(60d),
            new FuelV2FuelCheckpointInputs(
                CurrentFuelLiters: 0d,
                MeasuredAtBoxFuelLiters: 0d,
                PlannedServiceAddLiters: 0d,
                ExpectedBoxToPitExitFuelLiters: 0d));

        AssertCheckpoint(snapshot.Current, 0d, FuelV2FuelCheckpointSource.MeasuredCurrentTelemetry);
        AssertCheckpoint(snapshot.ExpectedAtBox, 0d, FuelV2FuelCheckpointSource.MeasuredAtBoxTelemetry);
        AssertCheckpoint(snapshot.ServiceComplete, 0d, FuelV2FuelCheckpointSource.PlannedServiceAdd);
        AssertCheckpoint(snapshot.ExpectedPitExit, 0d, FuelV2FuelCheckpointSource.ProjectedBoxToPitExit);
        Assert.Contains(FuelV2FuelCheckpointStateFlag.KnownZero, snapshot.ExpectedPitExit!.StateFlags);
    }

    [Fact]
    public void Checkpoints_AboveCapacityIsFlaggedWithoutGateFourClamping()
    {
        var snapshot = FuelV2FuelCheckpointCalculator.From(
            ResolvedCapacity(51d),
            new FuelV2FuelCheckpointInputs(
                MeasuredAtBoxFuelLiters: 20d,
                PlannedServiceAddLiters: 40d,
                ExpectedBoxToPitExitFuelLiters: 0.5d));

        AssertCheckpoint(snapshot.ServiceComplete, 60d, FuelV2FuelCheckpointSource.PlannedServiceAdd);
        AssertCheckpoint(snapshot.ExpectedPitExit, 59.5d, FuelV2FuelCheckpointSource.ProjectedBoxToPitExit);
        Assert.Contains(FuelV2FuelCheckpointStateFlag.AboveEffectiveCapacity, snapshot.ServiceComplete!.StateFlags);
        Assert.Contains(FuelV2FuelCheckpointStateFlag.AboveEffectiveCapacity, snapshot.StateFlags);
        Assert.Equal(FuelV2FuelCheckpointConfidence.Conflicted, snapshot.ServiceComplete.Confidence);
        Assert.Equal(FuelV2FuelCheckpointConfidence.Conflicted, snapshot.ExpectedPitExit!.Confidence);
        Assert.Contains(FuelV2FuelCheckpointStateFlag.DerivedFromConflictedCheckpoint, snapshot.ExpectedPitExit.StateFlags);
    }

    [Fact]
    public void Checkpoints_ImpossibleFuelToBoxCannotFabricateServiceOrExitFacts()
    {
        var snapshot = FuelV2FuelCheckpointCalculator.From(
            ResolvedCapacity(60d),
            new FuelV2FuelCheckpointInputs(
                CurrentFuelLiters: 1d,
                ExpectedFuelToBoxLiters: 2d,
                PlannedServiceAddLiters: 20d,
                ExpectedBoxToPitExitFuelLiters: 0.1d));

        AssertCheckpoint(snapshot.ExpectedAtBox, 0d, FuelV2FuelCheckpointSource.ProjectedCurrentToBox);
        Assert.Equal(FuelV2FuelCheckpointConfidence.Conflicted, snapshot.ExpectedAtBox!.Confidence);
        Assert.Contains(FuelV2FuelCheckpointStateFlag.ProjectionClampedAtZero, snapshot.ExpectedAtBox.StateFlags);
        Assert.Contains(FuelV2FuelCheckpointStateFlag.ProjectionClampedAtZero, snapshot.StateFlags);
        Assert.Null(snapshot.ServiceComplete);
        Assert.Null(snapshot.ExpectedPitExit);
    }

    [Fact]
    public void Checkpoints_InvalidAdjustmentIsRetainedAsInvalidAndCannotProduceDerivedFact()
    {
        var snapshot = FuelV2FuelCheckpointCalculator.From(
            ResolvedCapacity(60d),
            new FuelV2FuelCheckpointInputs(
                CurrentFuelLiters: 20d,
                ExpectedFuelToBoxLiters: -1d,
                PlannedServiceAddLiters: 10d));

        Assert.Contains(FuelV2FuelCheckpointStateFlag.InvalidInput, snapshot.StateFlags);
        Assert.Contains(FuelV2FuelCheckpointInputKind.ExpectedFuelToBox, snapshot.InvalidInputKinds);
        Assert.Null(snapshot.ExpectedAtBox);
        Assert.Null(snapshot.ServiceComplete);
    }

    private static FuelV2EffectiveCapacitySnapshot ResolvedCapacity(double liters)
    {
        return FuelV2EffectiveCapacityResolver.From(liters, 1d, 1d);
    }

    private static void AssertCheckpoint(
        FuelV2FuelCheckpoint? checkpoint,
        double expectedLiters,
        FuelV2FuelCheckpointSource expectedSource)
    {
        var fact = Assert.IsType<FuelV2FuelCheckpoint>(checkpoint);
        Assert.Equal(expectedLiters, Assert.IsType<double>(fact.Liters), precision: 6);
        Assert.Equal(expectedSource, fact.Source);
    }
}
