using TmrOverlay.Core.Fuel.V2;
using Xunit;

namespace TmrOverlay.App.Tests.Fuel.V2;

public sealed class FuelV2PitRouteFuelProjectionTests
{
    [Fact]
    public void Projection_KeepsCurrentToEntryEntryToBoxAndBoxToExitDistinctBeforeFeedingCheckpoints()
    {
        var route = FuelV2PitRouteFuelProjectionCalculator.From(new FuelV2PitRouteFuelProjectionInputs(
            Scope: Scope(),
            CurrentToPitEntry: Estimate(0.80d, "live current-to-entry"),
            PitEntryToBox: Estimate(0.35d, "learned exact-box entry-to-box"),
            BoxToPitExit: Estimate(0.14d, "learned exact-box box-to-exit")));

        var checkpoints = FuelV2FuelCheckpointCalculator.From(
            FuelV2EffectiveCapacityResolver.From(60d, 1d, 1d),
            new FuelV2FuelCheckpointInputs(
                CurrentFuelLiters: 33.29d,
                ExpectedFuelToBoxLiters: route.ExpectedFuelToBoxLiters,
                PlannedServiceAddLiters: 20d,
                ExpectedBoxToPitExitFuelLiters: route.ExpectedBoxToPitExitFuelLiters));

        Assert.Equal(FuelV2PitRouteFuelProjectionState.Available, route.State);
        Assert.True(route.CanDriveFuelStrategy);
        Assert.Equal(1.15d, Assert.IsType<double>(route.ExpectedFuelToBoxLiters), precision: 6);
        Assert.Equal(0.14d, Assert.IsType<double>(route.ExpectedBoxToPitExitFuelLiters), precision: 6);
        Assert.Equal(32.14d, Assert.IsType<double>(checkpoints.ExpectedAtBox?.Liters), precision: 6);
        Assert.Equal(52.14d, Assert.IsType<double>(checkpoints.ServiceComplete?.Liters), precision: 6);
        Assert.Equal(52.00d, Assert.IsType<double>(checkpoints.ExpectedPitExit?.Liters), precision: 6);
    }

    [Fact]
    public void Projection_DoesNotTreatAMissingBoxSpecificSegmentAsZero()
    {
        var route = FuelV2PitRouteFuelProjectionCalculator.From(new FuelV2PitRouteFuelProjectionInputs(
            Scope: Scope(),
            CurrentToPitEntry: Estimate(0.80d, "live current-to-entry"),
            PitEntryToBox: null,
            BoxToPitExit: Estimate(0.14d, "learned exact-box box-to-exit")));

        Assert.Equal(FuelV2PitRouteFuelProjectionState.Partial, route.State);
        Assert.False(route.CanDriveFuelStrategy);
        Assert.Null(route.ExpectedFuelToBoxLiters);
        Assert.Null(route.ExpectedBoxToPitExitFuelLiters);
        Assert.Contains(FuelV2PitRouteFuelProjectionFlag.PitEntryToBoxUnavailable, route.StateFlags);
    }

    [Fact]
    public void Projection_RejectsInvalidOrObservedOnlyRouteEvidence()
    {
        var invalid = FuelV2PitRouteFuelProjectionCalculator.From(new FuelV2PitRouteFuelProjectionInputs(
            Scope: Scope(),
            CurrentToPitEntry: Estimate(-0.1d, "invalid"),
            PitEntryToBox: Estimate(0.35d, "learned"),
            BoxToPitExit: Estimate(0.14d, "learned")));
        var observedOnly = FuelV2PitRouteFuelProjectionCalculator.From(new FuelV2PitRouteFuelProjectionInputs(
            Scope: Scope(),
            CurrentToPitEntry: Estimate(0.80d, "one observed pass", FuelV2PitRouteEvidenceConfidence.Observed),
            PitEntryToBox: Estimate(0.35d, "learned"),
            BoxToPitExit: Estimate(0.14d, "learned")));

        Assert.Equal(FuelV2PitRouteFuelProjectionState.Invalid, invalid.State);
        Assert.Null(invalid.ExpectedFuelToBoxLiters);
        Assert.Contains(FuelV2PitRouteFuelProjectionFlag.CurrentToPitEntryInvalid, invalid.StateFlags);

        Assert.Equal(FuelV2PitRouteFuelProjectionState.Partial, observedOnly.State);
        Assert.Null(observedOnly.ExpectedFuelToBoxLiters);
        Assert.Contains(FuelV2PitRouteFuelProjectionFlag.CurrentToPitEntryUnavailable, observedOnly.StateFlags);
    }

    [Fact]
    public void Projection_RejectsABoxSpecificEstimateFromAnotherAssignedPitBox()
    {
        var routeA = Scope();
        var routeB = routeA with { PitBoxIdentity = "driver-pit-track-percent:0.091" };
        var route = FuelV2PitRouteFuelProjectionCalculator.From(new FuelV2PitRouteFuelProjectionInputs(
            Scope: routeB,
            CurrentToPitEntry: Estimate(0.80d, "live current-to-entry", scope: routeB),
            PitEntryToBox: Estimate(0.35d, "learned box A entry-to-box", scope: routeA),
            BoxToPitExit: Estimate(0.14d, "learned box A box-to-exit", scope: routeA)));

        Assert.Equal(FuelV2PitRouteFuelProjectionState.Partial, route.State);
        Assert.False(route.CanDriveFuelStrategy);
        Assert.Null(route.ExpectedFuelToBoxLiters);
        Assert.Null(route.ExpectedBoxToPitExitFuelLiters);
        Assert.Contains(FuelV2PitRouteFuelProjectionFlag.PitEntryToBoxScopeMismatch, route.StateFlags);
        Assert.Contains(FuelV2PitRouteFuelProjectionFlag.BoxToPitExitScopeMismatch, route.StateFlags);
    }

    [Fact]
    public void Projection_RejectsUndefinedConfidenceAndAFiniteSegmentOverflow()
    {
        var undefinedConfidence = FuelV2PitRouteFuelProjectionCalculator.From(new FuelV2PitRouteFuelProjectionInputs(
            Scope: Scope(),
            CurrentToPitEntry: Estimate(0.80d, "unknown confidence", (FuelV2PitRouteEvidenceConfidence)999),
            PitEntryToBox: Estimate(0.35d, "learned"),
            BoxToPitExit: Estimate(0.14d, "learned")));
        var overflow = FuelV2PitRouteFuelProjectionCalculator.From(new FuelV2PitRouteFuelProjectionInputs(
            Scope: Scope(),
            CurrentToPitEntry: Estimate(double.MaxValue, "large entry burn"),
            PitEntryToBox: Estimate(double.MaxValue, "large box burn"),
            BoxToPitExit: Estimate(0d, "valid zero exit burn")));

        Assert.Equal(FuelV2PitRouteFuelProjectionState.Partial, undefinedConfidence.State);
        Assert.False(undefinedConfidence.CanDriveFuelStrategy);
        Assert.Null(undefinedConfidence.ExpectedFuelToBoxLiters);
        Assert.Contains(FuelV2PitRouteFuelProjectionFlag.CurrentToPitEntryUnavailable, undefinedConfidence.StateFlags);

        Assert.Equal(FuelV2PitRouteFuelProjectionState.Invalid, overflow.State);
        Assert.False(overflow.CanDriveFuelStrategy);
        Assert.Null(overflow.ExpectedFuelToBoxLiters);
        Assert.Null(overflow.ExpectedBoxToPitExitFuelLiters);
        Assert.Contains(FuelV2PitRouteFuelProjectionFlag.CurrentToBoxTotalInvalid, overflow.StateFlags);
    }

    private static FuelV2PitRouteFuelEstimate Estimate(
        double liters,
        string source,
        FuelV2PitRouteEvidenceConfidence confidence = FuelV2PitRouteEvidenceConfidence.Corroborated,
        FuelV2PitRouteScope? scope = null)
    {
        return new FuelV2PitRouteFuelEstimate(liters, source, confidence, scope ?? Scope());
    }

    private static FuelV2PitRouteScope Scope()
    {
        return new FuelV2PitRouteScope(
            CarIdentity: "car:dalara-p217",
            ExactLayoutIdentity: "track:daytona-road",
            TrackVersionIdentity: "track-version:2026.2",
            PitSpeedRuleIdentity: "pit-speed:60-kph",
            RuleSetIdentity: "rules:race",
            PitBoxIdentity: "driver-pit-track-percent:0.058335");
    }
}
