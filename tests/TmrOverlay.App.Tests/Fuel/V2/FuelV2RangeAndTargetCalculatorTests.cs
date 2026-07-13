using TmrOverlay.Core.Fuel.V2;
using Xunit;

namespace TmrOverlay.App.Tests.Fuel.V2;

public sealed class FuelV2RangeAndTargetCalculatorTests
{
    [Fact]
    public void Range_KnownZeroFuelProducesFactualZeroForAvailableBurns()
    {
        var windows = FuelV2FuelPerLapCalculator.FromAcceptedLaps([10d]);

        var range = FuelV2RangeCalculator.From(0d, windows);

        Assert.Equal(0d, range.CurrentFuelLiters);
        Assert.Equal(0d, Assert.IsType<double>(Assert.IsType<FuelV2Scalar>(range.Last).Value));
        Assert.Equal(0d, Assert.IsType<double>(Assert.IsType<FuelV2Scalar>(range.Max).Value));
        Assert.Null(range.FiveLapAverage);
        Assert.Null(range.TenLapAverage);
    }

    [Theory]
    [InlineData(-1d)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Range_InvalidFuelRemainsUnavailable(double currentFuelLiters)
    {
        var range = FuelV2RangeCalculator.From(
            currentFuelLiters,
            FuelV2FuelPerLapCalculator.FromAcceptedLaps([10d]));

        Assert.Null(range.CurrentFuelLiters);
        Assert.Null(range.Last);
    }

    [Theory]
    [InlineData(4.4d, 3, 4, 5)]
    [InlineData(4.5d, 4, 5, 6)]
    [InlineData(1d, 1, 2, 3)]
    [InlineData(0.6d, 1, 2, 3)]
    public void AroundProjectedTarget_UsesAcceptedThreeCandidateRoundCenteredRule(
        double projectedTargetLaps,
        int first,
        int second,
        int third)
    {
        Assert.Equal(new[] { first, second, third }, FuelV2TargetUsageCalculator.AroundProjectedTarget(projectedTargetLaps));
    }

    [Theory]
    [InlineData(1000.01d)]
    [InlineData(double.MaxValue)]
    public void AroundProjectedTarget_RejectsImplausibleValuesBeforeIntegerConversion(double projectedTargetLaps)
    {
        Assert.Empty(FuelV2TargetUsageCalculator.AroundProjectedTarget(projectedTargetLaps));
    }

    [Fact]
    public void TargetUsage_UsesComparatorBandsWithoutMakingMissingReferenceUnavailable()
    {
        var reference = FuelV2Scalar.From(10d, "reference", FuelV2Confidence.CleanBaseline);

        var success = SingleTarget(10d, reference);
        var warning = SingleTarget(9.6d, reference);
        var error = SingleTarget(9.4d, reference);
        var noReference = SingleTarget(9.4d, referenceBurn: null);

        Assert.Equal(FuelV2WorkbenchTone.Success, success.Tone);
        Assert.Equal(FuelV2WorkbenchTone.Warning, warning.Tone);
        Assert.Equal(FuelV2WorkbenchTone.Error, error.Tone);
        Assert.Equal(FuelV2WorkbenchTone.Info, noReference.Tone);
    }

    private static FuelV2TargetUsageCell SingleTarget(double budget, FuelV2Scalar? referenceBurn)
    {
        return Assert.Single(FuelV2TargetUsageCalculator.From(
            budget,
            "control budget",
            referenceBurn,
            [1]).Targets);
    }
}
