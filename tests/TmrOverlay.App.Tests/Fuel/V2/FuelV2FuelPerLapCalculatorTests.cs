using TmrOverlay.Core.Fuel.V2;
using Xunit;

namespace TmrOverlay.App.Tests.Fuel.V2;

public sealed class FuelV2FuelPerLapCalculatorTests
{
    [Fact]
    public void FromAcceptedLaps_FiltersNonPositiveAndNonFiniteSamples()
    {
        var windows = FuelV2FuelPerLapCalculator.FromAcceptedLaps(
            [0d, -1d, double.NaN, double.PositiveInfinity, double.NegativeInfinity, 1.25d, 2.5d, double.NaN]);

        Assert.Equal(2, windows.AcceptedLapCount);
        AssertScalarValue(windows.Last, 2.5d);
        AssertScalarValue(windows.Max, 2.5d);
    }

    [Fact]
    public void FromAcceptedLaps_WithNoAcceptedSamples_LeavesLockedWindowsUnavailable()
    {
        var windows = FuelV2FuelPerLapCalculator.FromAcceptedLaps(
            [0d, -1d, double.NaN, double.PositiveInfinity, double.NegativeInfinity]);

        Assert.Equal(0, windows.AcceptedLapCount);
        Assert.Null(windows.Last);
        Assert.Null(windows.FiveLapAverage);
        Assert.Null(windows.TenLapAverage);
        Assert.Null(windows.Max);
    }

    [Fact]
    public void FromAcceptedLaps_UsesTrailingFullFiveAndTenLapAverages()
    {
        var windows = FuelV2FuelPerLapCalculator.FromAcceptedLaps(
            Enumerable.Range(1, 11).Select(value => (double)value).ToArray());

        Assert.Equal(11, windows.AcceptedLapCount);
        AssertScalarValue(windows.Last, 11d);
        AssertScalarValue(windows.FiveLapAverage, 9d);
        AssertScalarValue(windows.TenLapAverage, 6.5d);
    }

    [Fact]
    public void FromAcceptedLaps_DoesNotProducePartialAveragesByDefault()
    {
        var fourSamples = FuelV2FuelPerLapCalculator.FromAcceptedLaps([1d, 2d, 3d, 4d]);
        var nineSamples = FuelV2FuelPerLapCalculator.FromAcceptedLaps(
            Enumerable.Range(1, 9).Select(value => (double)value).ToArray());

        Assert.Null(fourSamples.FiveLapAverage);
        Assert.Null(fourSamples.TenLapAverage);
        Assert.NotNull(nineSamples.FiveLapAverage);
        Assert.Null(nineSamples.TenLapAverage);
    }

    [Fact]
    public void FromAcceptedLaps_PartialTenLapWindowStartsAtSixWithoutSectorSeedContext()
    {
        var fiveSamples = FuelV2FuelPerLapCalculator.FromAcceptedLaps(
            [1d, 2d, 3d, 4d, 5d],
            new FuelV2FuelPerLapWindowOptions(AllowPartialWindows: true));
        var sixSamples = FuelV2FuelPerLapCalculator.FromAcceptedLaps(
            [1d, 2d, 3d, 4d, 5d, 6d],
            new FuelV2FuelPerLapWindowOptions(AllowPartialWindows: true));

        Assert.Null(fiveSamples.TenLapAverage);
        var partial = Assert.IsType<FuelV2Scalar>(sixSamples.TenLapAverage);
        Assert.Equal(3.5d, Assert.IsType<double>(partial.Value), precision: 6);
        Assert.Contains(FuelV2SampleContextFlag.CleanRace, partial.ContextFlags);
        Assert.DoesNotContain(FuelV2SampleContextFlag.SeededSectorProfile, partial.ContextFlags);
        Assert.False(partial.CleanBaselineEligible);
    }

    [Fact]
    public void FromAcceptedLaps_IgnoresNonPositiveSeeds()
    {
        var invalidSeed = FuelV2Scalar.From(-1d, "invalid seed", FuelV2Confidence.Seeded);

        var windows = FuelV2FuelPerLapCalculator.FromAcceptedLaps(
            [],
            new FuelV2FuelPerLapWindowOptions(
                MaxSeed: invalidSeed,
                MinSeed: invalidSeed,
                QualifyingSeed: invalidSeed));

        Assert.Null(windows.Max);
        Assert.Null(windows.Min);
        Assert.Null(windows.QualifyingSeed);
    }

    [Fact]
    public void FromAcceptedLaps_MaxKeepsHigherLabeledSeed()
    {
        var maxSeed = FuelV2Scalar.From(
            4d,
            "historical max seed",
            FuelV2Confidence.Contextual,
            [FuelV2SampleContextFlag.CleanRace]);
        var qualifyingSeed = FuelV2Scalar.From(
            5d,
            "qualifying seed",
            FuelV2Confidence.Seeded,
            [FuelV2SampleContextFlag.SeededSectorProfile],
            displayEligible: false,
            cleanBaselineEligible: true);

        var windows = FuelV2FuelPerLapCalculator.FromAcceptedLaps(
            [2d, 3d],
            new FuelV2FuelPerLapWindowOptions(
                MaxSeed: maxSeed,
                QualifyingSeed: qualifyingSeed));

        var max = Assert.IsType<FuelV2Scalar>(windows.Max);
        Assert.Equal(5d, max.Value);
        Assert.Equal("qualifying seed", max.Source);
        Assert.Equal(FuelV2Confidence.Seeded, max.Confidence);
        Assert.Equal([FuelV2SampleContextFlag.SeededSectorProfile], max.ContextFlags);
        Assert.True(max.DisplayEligible);
        Assert.False(max.CleanBaselineEligible);
    }

    [Fact]
    public void FromAcceptedLaps_MaxUsesHigherLiveMaximum()
    {
        var maxSeed = FuelV2Scalar.From(4d, "historical max seed", FuelV2Confidence.Seeded);
        var qualifyingSeed = FuelV2Scalar.From(5d, "qualifying seed", FuelV2Confidence.Seeded);

        var windows = FuelV2FuelPerLapCalculator.FromAcceptedLaps(
            [2d, 6d, 3d],
            new FuelV2FuelPerLapWindowOptions(
                MaxSeed: maxSeed,
                QualifyingSeed: qualifyingSeed));

        AssertScalarValue(windows.Max, 6d);
        AssertCleanLiveWindow(windows.Max);
    }

    [Fact]
    public void FromAcceptedLaps_LiveWindowsCarryCleanEvidenceDimensions()
    {
        var windows = FuelV2FuelPerLapCalculator.FromAcceptedLaps(
            Enumerable.Range(1, 10).Select(value => (double)value).ToArray());

        AssertCleanLiveWindow(windows.Last);
        AssertCleanLiveWindow(windows.FiveLapAverage);
        AssertCleanLiveWindow(windows.TenLapAverage);
        AssertCleanLiveWindow(windows.Max);
    }

    private static void AssertScalarValue(FuelV2Scalar? scalar, double expected)
    {
        var value = Assert.IsType<FuelV2Scalar>(scalar);
        Assert.Equal(expected, Assert.IsType<double>(value.Value), precision: 6);
    }

    private static void AssertCleanLiveWindow(FuelV2Scalar? scalar)
    {
        var value = Assert.IsType<FuelV2Scalar>(scalar);
        Assert.True(value.HasValue);
        Assert.False(string.IsNullOrWhiteSpace(value.Source));
        Assert.Equal(FuelV2Confidence.CleanBaseline, value.Confidence);
        Assert.Contains(FuelV2SampleContextFlag.CleanRace, value.ContextFlags);
        Assert.True(value.DisplayEligible);
        Assert.True(value.CleanBaselineEligible);
    }
}
