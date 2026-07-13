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
        Assert.Equal(21.5d, Assert.IsType<double>(cell.TargetFuelLiters.Value), precision: 6);
        Assert.Equal(21.5d, Assert.IsType<double>(cell.FuelToAddLiters.Value), precision: 6);
        Assert.False(cell.TankLimited);
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
        var burn = FuelV2Scalar.From(10d, "reference", FuelV2Confidence.CleanBaseline);

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
        var burn = FuelV2Scalar.From(10d, "reference", FuelV2Confidence.CleanBaseline);

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
        var burn = FuelV2Scalar.From(10d, "reference", FuelV2Confidence.CleanBaseline);

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
        var burn = FuelV2Scalar.From(10d, "reference", FuelV2Confidence.CleanBaseline);

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
}
