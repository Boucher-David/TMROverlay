using TmrOverlay.Core.Fuel.V2;
using Xunit;

namespace TmrOverlay.App.Tests.Fuel.V2;

public sealed class FuelV2PitRequestAndPlanCalculatorTests
{
    [Fact]
    public void PitRequest_KnownZeroFuelProducesFullFactualAdd()
    {
        var snapshot = FuelV2PitRequestCalculator.From(
            currentFuelLiters: 0d,
            targetLaps: 2,
            windows: FuelV2FuelPerLapCalculator.FromAcceptedLaps([10d]),
            tankCapacityLiters: 100d,
            reserveLiters: 1d,
            pitLaneFuelLiters: 0.5d);

        var cell = Assert.IsType<FuelV2PitRequestCell>(snapshot.Last);
        Assert.True(snapshot.AdjustmentsValid);
        Assert.Equal(FuelV2BurnBucketId.Last, cell.BurnBucketId);
        Assert.Equal(21.5d, Assert.IsType<double>(cell.TargetFuelLiters.Value), precision: 6);
        Assert.Equal(21.5d, Assert.IsType<double>(cell.FuelToAddLiters.Value), precision: 6);
        Assert.Equal(FuelV2BurnBucketId.Last, cell.FuelToAddLiters.BurnBucketId);
        Assert.Equal(FuelV2BurnSource.LiveLastLap, cell.FuelToAddLiters.BurnSource);
        Assert.Equal(1, cell.FuelToAddLiters.SampleCount);
        Assert.True(cell.FuelToAddLiters.StrategyEligible);
        Assert.Contains("pit add from Last", cell.FuelToAddLiters.Source);
        Assert.Contains("live last lap", cell.FuelToAddLiters.Source);
        Assert.Equal(FuelV2BurnBucketId.Last, cell.TargetFuelLiters.BurnBucketId);
        Assert.Equal(FuelV2BurnSource.LiveLastLap, cell.TargetFuelLiters.BurnSource);
        Assert.False(cell.TankLimited);
    }

    [Fact]
    public void PitRequest_OptionalBucketsUseExplicitIdentityWithoutLabelInference()
    {
        var historicalMax = FuelV2Scalar.From(
            14d,
            "historical maximum",
            FuelV2Confidence.Seeded,
            burnSource: FuelV2BurnSource.HistoricalSeed,
            sampleCount: 12,
            strategyEligible: true);
        var historicalMin = FuelV2Scalar.From(
            9d,
            "historical minimum",
            FuelV2Confidence.Seeded,
            burnSource: FuelV2BurnSource.HistoricalSeed,
            sampleCount: 8);
        var qualifying = FuelV2Scalar.From(
            12d,
            "display copy deliberately does not say quali",
            FuelV2Confidence.Seeded,
            burnSource: FuelV2BurnSource.QualifyingSeed,
            sampleCount: 1);
        var windows = FuelV2FuelPerLapCalculator.FromAcceptedLaps(
            [],
            new FuelV2FuelPerLapWindowOptions(
                MaxSeed: historicalMax,
                MinSeed: historicalMin,
                QualifyingSeed: qualifying));

        var snapshot = FuelV2PitRequestCalculator.From(
            currentFuelLiters: 0d,
            targetLaps: 1,
            windows: windows,
            tankCapacityLiters: 100d);

        var maximum = Assert.IsType<FuelV2PitRequestCell>(snapshot.Max);
        var minimum = Assert.IsType<FuelV2PitRequestCell>(snapshot.Min);
        var quali = Assert.IsType<FuelV2PitRequestCell>(snapshot.QualifyingSeed);
        Assert.Equal(FuelV2BurnBucketId.Maximum, maximum.BurnBucketId);
        Assert.Equal(FuelV2BurnSource.HistoricalSeed, maximum.FuelToAddLiters.BurnSource);
        Assert.Equal(12, maximum.FuelToAddLiters.SampleCount);
        Assert.True(maximum.FuelToAddLiters.StrategyEligible);
        Assert.Equal(FuelV2BurnBucketId.Minimum, minimum.BurnBucketId);
        Assert.Equal(FuelV2BurnSource.HistoricalSeed, minimum.FuelToAddLiters.BurnSource);
        Assert.Equal(FuelV2BurnBucketId.Qualifying, quali.BurnBucketId);
        Assert.Equal(FuelV2BurnSource.QualifyingSeed, quali.FuelToAddLiters.BurnSource);
        Assert.Equal("Quali", quali.Label);
    }

    [Theory]
    [InlineData(-1d, 0d)]
    [InlineData(0d, -1d)]
    [InlineData(double.NaN, 0d)]
    [InlineData(0d, double.PositiveInfinity)]
    public void PitRequest_InvalidAdjustmentsCannotProduceCells(double reserveLiters, double pitLaneFuelLiters)
    {
        var snapshot = FuelV2PitRequestCalculator.From(
            currentFuelLiters: 0d,
            targetLaps: 2,
            windows: FuelV2FuelPerLapCalculator.FromAcceptedLaps([10d]),
            reserveLiters: reserveLiters,
            pitLaneFuelLiters: pitLaneFuelLiters);

        Assert.False(snapshot.AdjustmentsValid);
        Assert.Null(snapshot.Last);
        Assert.Null(snapshot.Max);
    }

    [Fact]
    public void PitRequest_NonPositiveBurnCannotProduceARequest()
    {
        var negativeBurn = FuelV2Scalar.From(-10d, "invalid burn", FuelV2Confidence.Contextual);
        var windows = new FuelV2FuelPerLapWindows(
            Last: negativeBurn,
            FiveLapAverage: null,
            TenLapAverage: null,
            Max: null,
            Min: null,
            QualifyingSeed: null,
            AcceptedLapCount: 0);

        var snapshot = FuelV2PitRequestCalculator.From(0d, 2, windows);

        Assert.Null(snapshot.Last);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void PitRequest_NonfiniteDirectScalarCannotProduceARequest(double burnValue)
    {
        var invalidBurn = new FuelV2Scalar(
            burnValue,
            "invalid direct scalar",
            FuelV2Confidence.Contextual,
            [],
            DisplayEligible: true,
            CleanBaselineEligible: false);
        var windows = new FuelV2FuelPerLapWindows(
            Last: invalidBurn,
            FiveLapAverage: null,
            TenLapAverage: null,
            Max: null,
            Min: null,
            QualifyingSeed: null,
            AcceptedLapCount: 0);

        var snapshot = FuelV2PitRequestCalculator.From(0d, 2, windows, tankCapacityLiters: 100d);

        Assert.Null(snapshot.Last);
    }

    [Fact]
    public void Plan_SubLapFuelDoesNotInventOneLapOfFutureCapacity()
    {
        var burn = TypedBurn(10d);

        var snapshot = FuelV2PlanCalculator.FromFuelBudget(
            plannedRaceLaps: 10d,
            raceLapsRemaining: 10d,
            usableStintFuelLiters: 9.9d,
            stintBurn: burn);

        Assert.Equal(0d, snapshot.StintCapacityLaps);
        Assert.Null(snapshot.PlannedStintCount);
        Assert.Null(snapshot.PlannedStopCount);
        Assert.Equal(FuelV2WorkbenchTone.Waiting, snapshot.Tone);
    }

    [Fact]
    public void Plan_CurrentRangeAndFutureWholeLapCapacityStayDistinctAtSubLapFuel()
    {
        var burn = TypedBurn(10d);

        var snapshot = FuelV2PlanCalculator.FromCurrentCheckpointFuelBudget(
            plannedRaceLaps: 10d,
            raceLapsRemaining: 5d,
            currentFuelLiters: 9.9d,
            currentBurn: burn,
            futureFuelLiters: 9.9d,
            futureBurn: burn);

        Assert.Equal(0.99d, Assert.IsType<double>(snapshot.CurrentStintCapacityLaps), precision: 6);
        Assert.Equal(0d, snapshot.FutureStintCapacityLaps);
        Assert.Null(snapshot.PlannedStopCount);
        Assert.Equal(FuelV2WorkbenchTone.Waiting, snapshot.Tone);
    }

    [Fact]
    public void Plan_KnownZeroFuelRemainsFactualAtRaceStart()
    {
        var burn = TypedBurn(10d);

        var snapshot = FuelV2PlanCalculator.FromFuelBudget(
            plannedRaceLaps: 2d,
            raceLapsRemaining: 2d,
            usableStintFuelLiters: 0d,
            stintBurn: burn);

        Assert.Equal(0d, snapshot.UsableStintFuelLiters);
        Assert.Equal(0d, snapshot.StintCapacityLaps);
        Assert.Null(snapshot.PlannedStopCount);
        Assert.Equal(FuelV2WorkbenchTone.Waiting, snapshot.Tone);
    }

    [Fact]
    public void Plan_KnownZeroFuelRemainsFactualAtCurrentCheckpoint()
    {
        var burn = TypedBurn(10d);

        var snapshot = FuelV2PlanCalculator.FromCurrentCheckpointFuelBudget(
            plannedRaceLaps: 2d,
            raceLapsRemaining: 2d,
            currentFuelLiters: 0d,
            currentBurn: burn,
            futureFuelLiters: 0d,
            futureBurn: burn);

        Assert.Equal(0d, snapshot.CurrentStintCapacityLaps);
        Assert.Equal(0d, snapshot.FutureStintCapacityLaps);
        Assert.Null(snapshot.PlannedStopCount);
        Assert.Equal(FuelV2WorkbenchTone.Waiting, snapshot.Tone);
    }

    [Fact]
    public void Plan_UntypedPositiveBurnFailsClosedInsteadOfProducingCapacity()
    {
        var untypedBurn = FuelV2Scalar.From(
            10d,
            "numeric burn without bucket identity",
            FuelV2Confidence.CleanBaseline,
            burnSource: FuelV2BurnSource.LiveLastLap,
            sampleCount: 1,
            strategyEligible: true);

        var snapshot = FuelV2PlanCalculator.FromFuelBudget(
            plannedRaceLaps: 10d,
            raceLapsRemaining: 10d,
            usableStintFuelLiters: 100d,
            stintBurn: untypedBurn);

        Assert.Null(snapshot.StintBurn);
        Assert.Null(snapshot.StintCapacityLaps);
        Assert.Null(snapshot.PlannedStintCount);
        Assert.Equal(FuelV2WorkbenchTone.Waiting, snapshot.Tone);
    }

    private static FuelV2Scalar TypedBurn(double value)
    {
        return FuelV2Scalar.From(
            value,
            "reference",
            FuelV2Confidence.CleanBaseline,
            [FuelV2SampleContextFlag.CleanRace],
            burnBucketId: FuelV2BurnBucketId.Last,
            burnSource: FuelV2BurnSource.LiveLastLap,
            sampleCount: 1,
            strategyEligible: true);
    }
}
