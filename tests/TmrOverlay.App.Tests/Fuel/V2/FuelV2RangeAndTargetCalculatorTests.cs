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

    [Fact]
    public void Range_DerivedCellsRetainTypedBurnEvidenceAndSourceChain()
    {
        var windows = FuelV2FuelPerLapCalculator.FromAcceptedLaps(
            Enumerable.Range(1, 10).Select(value => 10d + value / 10d).ToArray());

        var range = FuelV2RangeCalculator.From(100d, windows);

        var last = Assert.IsType<FuelV2Scalar>(range.Last);
        Assert.Equal(FuelV2BurnBucketId.Last, last.BurnBucketId);
        Assert.Equal(FuelV2BurnSource.LiveLastLap, last.BurnSource);
        Assert.Equal(1, last.SampleCount);
        Assert.Equal(FuelV2Confidence.CleanBaseline, last.Confidence);
        Assert.Contains(FuelV2SampleContextFlag.CleanRace, last.ContextFlags);
        Assert.True(last.DisplayEligible);
        Assert.True(last.StrategyEligible);
        Assert.Contains("range from Last", last.Source);
        Assert.Contains("live last lap", last.Source);

        var maximum = Assert.IsType<FuelV2Scalar>(range.Max);
        Assert.Equal(FuelV2BurnBucketId.Maximum, maximum.BurnBucketId);
        Assert.Equal(FuelV2BurnSource.LiveMaximum, maximum.BurnSource);
        Assert.Equal(10, maximum.SampleCount);
        Assert.True(maximum.StrategyEligible);
    }

    [Fact]
    public void Range_ProjectsExactHistoricalNormalIntoItsOwnAlignedBucket()
    {
        var history = FuelV2Scalar.From(
            13.5d,
            "classified race history",
            FuelV2Confidence.Seeded,
            burnBucketId: FuelV2BurnBucketId.HistoricalNormal,
            burnSource: FuelV2BurnSource.HistoricalNormal,
            sampleCount: 4,
            strategyEligible: false);
        var windows = FuelV2FuelPerLapCalculator.FromAcceptedLaps(
            [],
            new FuelV2FuelPerLapWindowOptions(HistoricalNormalSeed: history));

        var range = FuelV2RangeCalculator.From(40d, windows);

        var historicalRange = Assert.IsType<FuelV2Scalar>(range.HistoricalNormal);
        Assert.Equal(FuelV2BurnBucketId.HistoricalNormal, historicalRange.BurnBucketId);
        Assert.Equal(FuelV2BurnSource.HistoricalNormal, historicalRange.BurnSource);
        Assert.Equal(FuelV2Confidence.Seeded, historicalRange.Confidence);
        Assert.Equal(4, historicalRange.SampleCount);
        Assert.Equal(40d / 13.5d, Assert.IsType<double>(historicalRange.Value), precision: 6);
        Assert.Same(historicalRange, range.Bucket(FuelV2BurnBucketId.HistoricalNormal));
        Assert.Null(range.Last);
        Assert.Null(range.Max);
    }

    [Fact]
    public void Range_MismatchedBucketIdentityIsRejectedInsteadOfRelabeled()
    {
        var misplacedMaximum = FuelV2Scalar.From(
            10d,
            "misplaced maximum",
            FuelV2Confidence.CleanBaseline,
            burnBucketId: FuelV2BurnBucketId.Maximum,
            burnSource: FuelV2BurnSource.LiveMaximum,
            sampleCount: 5,
            strategyEligible: true);
        var windows = new FuelV2FuelPerLapWindows(
            Last: misplacedMaximum,
            FiveLapAverage: null,
            TenLapAverage: null,
            Max: null,
            Min: null,
            QualifyingSeed: null,
            AcceptedLapCount: 5);

        var range = FuelV2RangeCalculator.From(50d, windows);

        Assert.Null(windows.Bucket(FuelV2BurnBucketId.Last));
        Assert.Null(range.Last);
    }

    [Fact]
    public void Range_MissingBucketIdentityIsRejectedInsteadOfInferredFromRecordPosition()
    {
        var untypedBurn = FuelV2Scalar.From(
            10d,
            "legacy untyped burn",
            FuelV2Confidence.CleanBaseline,
            burnSource: FuelV2BurnSource.LiveLastLap,
            sampleCount: 1,
            strategyEligible: true);
        var windows = new FuelV2FuelPerLapWindows(
            Last: untypedBurn,
            FiveLapAverage: null,
            TenLapAverage: null,
            Max: null,
            Min: null,
            QualifyingSeed: null,
            AcceptedLapCount: 1);

        var range = FuelV2RangeCalculator.From(50d, windows);

        Assert.Null(windows.Bucket(FuelV2BurnBucketId.Last));
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
        var reference = FuelV2Scalar.From(
            10d,
            "reference",
            FuelV2Confidence.CleanBaseline,
            [FuelV2SampleContextFlag.CleanRace],
            burnBucketId: FuelV2BurnBucketId.Last,
            burnSource: FuelV2BurnSource.LiveLastLap,
            sampleCount: 1,
            strategyEligible: true);

        var success = SingleTarget(10d, reference);
        var warning = SingleTarget(9.6d, reference);
        var error = SingleTarget(9.4d, reference);
        var noReference = SingleTarget(9.4d, referenceBurn: null);

        Assert.Equal(FuelV2WorkbenchTone.Success, success.Tone);
        Assert.Equal(FuelV2WorkbenchTone.Warning, warning.Tone);
        Assert.Equal(FuelV2WorkbenchTone.Error, error.Tone);
        Assert.Equal(FuelV2WorkbenchTone.Info, noReference.Tone);
        Assert.Same(reference, success.ReferenceBurn);
        Assert.Equal(FuelV2BurnBucketId.Last, success.ReferenceBurn?.BurnBucketId);
        Assert.Equal(FuelV2BurnSource.LiveLastLap, success.ReferenceBurn?.BurnSource);
        Assert.True(success.ReferenceBurn!.StrategyEligible);
        Assert.Null(noReference.ReferenceBurn);
    }

    [Fact]
    public void TargetUsage_UntypedPositiveReferenceFailsClosedInsteadOfDrivingComparatorTone()
    {
        var untypedReference = FuelV2Scalar.From(
            10d,
            "numeric comparator without bucket identity",
            FuelV2Confidence.CleanBaseline,
            burnSource: FuelV2BurnSource.LiveLastLap,
            sampleCount: 1,
            strategyEligible: true);

        var snapshot = FuelV2TargetUsageCalculator.From(
            10d,
            "control budget",
            untypedReference,
            [1]);

        var target = Assert.Single(snapshot.Targets);
        Assert.Null(snapshot.ReferenceBurn);
        Assert.Null(target.ReferenceBurn);
        Assert.Equal(FuelV2WorkbenchTone.Info, target.Tone);
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
