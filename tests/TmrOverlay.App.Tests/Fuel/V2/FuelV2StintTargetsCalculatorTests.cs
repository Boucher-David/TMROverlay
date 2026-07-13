using TmrOverlay.Core.Fuel.V2;
using Xunit;

namespace TmrOverlay.App.Tests.Fuel.V2;

public sealed class FuelV2StintTargetsCalculatorTests
{
    private static readonly FuelV2Scalar ReferenceBurn = FuelV2Scalar.From(
        10d,
        "reference",
        FuelV2Confidence.CleanBaseline,
        [FuelV2SampleContextFlag.CleanRace],
        burnBucketId: FuelV2BurnBucketId.Last,
        burnSource: FuelV2BurnSource.LiveLastLap,
        sampleCount: 1,
        strategyEligible: true);

    [Fact]
    public void KnownZeroFuelProducesFactualZeroAndInfeasibleCandidates()
    {
        var snapshot = FuelV2StintTargetsCalculator.From(
            currentFuelLiters: 0d,
            referenceBurn: ReferenceBurn,
            targetLaps: 2,
            remainingLaps: 10d);

        Assert.Equal(0d, snapshot.CurrentFuelLiters);
        Assert.Equal(0d, snapshot.UsableFuelLiters);
        Assert.Equal(0d, snapshot.CurrentRangeLaps);
        var plan = Assert.Single(snapshot.Targets, cell => cell.Role == FuelV2StintTargetRole.Plan);
        Assert.Equal(0d, Assert.IsType<double>(plan.RequiredFuelPerLap.Value));
        Assert.Equal(FuelV2WorkbenchTone.Error, plan.Tone);
        Assert.False(plan.DisplayEligible);
        Assert.Equal("unrealistic", plan.ReasonLabel);
        Assert.Same(ReferenceBurn, plan.ReferenceBurn);
        Assert.Equal(FuelV2BurnBucketId.Last, plan.ReferenceBurn?.BurnBucketId);
        Assert.Equal(FuelV2BurnSource.LiveLastLap, plan.ReferenceBurn?.BurnSource);
        Assert.True(plan.ReferenceBurn!.StrategyEligible);
    }

    [Fact]
    public void ZeroRemainingLapsProducesNoTargetOrCandidates()
    {
        var snapshot = FuelV2StintTargetsCalculator.From(
            currentFuelLiters: 50d,
            referenceBurn: ReferenceBurn,
            targetLaps: 5,
            remainingLaps: 0d);

        Assert.Null(snapshot.TargetLaps);
        Assert.Empty(snapshot.Targets);
        Assert.Equal("finished", snapshot.StatusLabel);
        Assert.Equal(FuelV2WorkbenchTone.Info, snapshot.Tone);
    }

    [Theory]
    [InlineData(9.20d, FuelV2WorkbenchTone.Warning)]
    [InlineData(9.19d, FuelV2WorkbenchTone.Warning)]
    [InlineData(8.4d, FuelV2WorkbenchTone.Error)]
    public void SavingSeverityIsMonotonic(double usableFuelLiters, FuelV2WorkbenchTone expectedTone)
    {
        var snapshot = FuelV2StintTargetsCalculator.From(
            currentFuelLiters: usableFuelLiters,
            referenceBurn: ReferenceBurn,
            targetLaps: 1,
            remainingLaps: 3d);

        var plan = Assert.Single(snapshot.Targets, cell => cell.Role == FuelV2StintTargetRole.Plan);
        Assert.Equal(expectedTone, plan.Tone);
        Assert.Equal(expectedTone, snapshot.Tone);
    }

    [Fact]
    public void NegativeTimeEvidenceIsRejectedInsteadOfHidingCandidate()
    {
        var snapshot = FuelV2StintTargetsCalculator.From(
            currentFuelLiters: 10d,
            referenceBurn: ReferenceBurn,
            targetLaps: 1,
            remainingLaps: 3d,
            options: new FuelV2StintTargetsOptions(
                TargetTimeContexts: new Dictionary<int, FuelV2StintTargetTimeContext>
                {
                    [1] = new(StopAvoidanceSeconds: -1d, PaceLossSeconds: 0d)
                }));

        var plan = Assert.Single(snapshot.Targets, cell => cell.Role == FuelV2StintTargetRole.Plan);
        Assert.Null(plan.StrategyDeltaSeconds);
        Assert.True(plan.DisplayEligible);
    }

    [Fact]
    public void ValidNegativeStrategyDeltaHidesCandidateWithoutSuccessTone()
    {
        var snapshot = FuelV2StintTargetsCalculator.From(
            currentFuelLiters: 10d,
            referenceBurn: ReferenceBurn,
            targetLaps: 1,
            remainingLaps: 3d,
            options: new FuelV2StintTargetsOptions(
                TargetTimeContexts: new Dictionary<int, FuelV2StintTargetTimeContext>
                {
                    [1] = new(StopAvoidanceSeconds: 35d, PaceLossSeconds: 48d)
                }));

        var plan = Assert.Single(snapshot.Targets, cell => cell.Role == FuelV2StintTargetRole.Plan);
        Assert.Equal(-13d, plan.StrategyDeltaSeconds);
        Assert.False(plan.DisplayEligible);
        Assert.Equal("not worth time", plan.ReasonLabel);
        Assert.Equal(FuelV2WorkbenchTone.Warning, plan.Tone);
        Assert.Equal(FuelV2WorkbenchTone.Warning, snapshot.Tone);
    }

    [Fact]
    public void DegradedContextCannotReportSuccessTone()
    {
        var snapshot = FuelV2StintTargetsCalculator.From(
            currentFuelLiters: 10d,
            referenceBurn: ReferenceBurn,
            targetLaps: 1,
            remainingLaps: 3d,
            options: new FuelV2StintTargetsOptions(
                StateFlags: [FuelV2PlanStateFlag.HeldLapBudget]));

        Assert.Equal("tracking", snapshot.StatusLabel);
        Assert.Equal(FuelV2WorkbenchTone.Warning, snapshot.Tone);
        var plan = Assert.Single(snapshot.Targets, cell => cell.Role == FuelV2StintTargetRole.Plan);
        Assert.Equal(FuelV2WorkbenchTone.Warning, plan.Tone);
    }

    [Fact]
    public void DegradedUnavailableContextUsesWarningInsteadOfPlainWaiting()
    {
        var snapshot = FuelV2StintTargetsCalculator.From(
            currentFuelLiters: null,
            referenceBurn: ReferenceBurn,
            targetLaps: 2,
            remainingLaps: null,
            options: new FuelV2StintTargetsOptions(
                StateFlags: [FuelV2PlanStateFlag.HeldLapBudget]));

        Assert.Equal("learning", snapshot.StatusLabel);
        Assert.Equal(FuelV2WorkbenchTone.Warning, snapshot.Tone);
    }

    [Fact]
    public void UntypedPositiveReferenceFailsClosedInsteadOfDrivingStintTargets()
    {
        var untypedReference = FuelV2Scalar.From(
            10d,
            "numeric comparator without bucket identity",
            FuelV2Confidence.CleanBaseline,
            burnSource: FuelV2BurnSource.LiveLastLap,
            sampleCount: 1,
            strategyEligible: true);

        var snapshot = FuelV2StintTargetsCalculator.From(
            currentFuelLiters: 20d,
            referenceBurn: untypedReference,
            targetLaps: 2,
            remainingLaps: 4d);

        Assert.Null(snapshot.ReferenceBurn);
        Assert.Null(snapshot.CurrentRangeLaps);
        var plan = Assert.Single(snapshot.Targets, cell => cell.Role == FuelV2StintTargetRole.Plan);
        Assert.Null(plan.ReferenceBurn);
        Assert.Null(plan.SaveRequiredLitersPerLap);
        Assert.Equal(FuelV2WorkbenchTone.Info, plan.Tone);
    }
}
