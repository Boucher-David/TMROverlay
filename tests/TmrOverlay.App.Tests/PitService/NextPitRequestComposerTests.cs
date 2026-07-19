using TmrOverlay.Core.Fuel.V2;
using TmrOverlay.Core.PitService;
using TmrOverlay.Core.Telemetry.Live;
using Xunit;

namespace TmrOverlay.App.Tests.PitService;

public sealed class NextPitRequestComposerTests
{
    [Fact]
    public void Decode_MapsSharedPitServiceFlagsOnce()
    {
        var selection = PitServiceRequestFlags.Decode(0x01 | 0x08 | 0x10 | 0x40);

        Assert.True(selection.LeftFrontTire);
        Assert.False(selection.RightFrontTire);
        Assert.False(selection.LeftRearTire);
        Assert.True(selection.RightRearTire);
        Assert.Equal(2, selection.RequestedTireCount);
        Assert.True(selection.Fuel);
        Assert.False(selection.Tearoff);
        Assert.True(selection.FastRepair);
    }

    [Fact]
    public void From_MatchesLiveFuelSelectionToExplicitFuelV2Plan()
    {
        var live = LivePitServiceModel.Empty with
        {
            Request = new LivePitServiceRequest(
                LeftFrontTire: true,
                RightFrontTire: true,
                LeftRearTire: true,
                RightRearTire: true,
                Fuel: true,
                Tearoff: false,
                FastRepair: false,
                FuelLiters: 12.54d,
                RequestedTireCompoundIndex: 1,
                RequestedTireCompoundLabel: "Dry",
                RequestedTireCompoundShortLabel: "D")
        };

        var request = NextPitRequestComposer.From(live, SelectedFuelPlan(12.5d));

        Assert.True(request.HasFuelPlan);
        Assert.Equal(NextPitRequestFuelAlignment.MatchesPlan, request.FuelAlignment);
        Assert.Equal(4, request.CurrentSelection.RequestedTireCount);
        Assert.Equal("fuel-selection-matches-plan", request.Status);
    }

    [Fact]
    public void From_RefusesToTreatAnUnselectedOrIneligibleFuelCellAsPlan()
    {
        var live = LivePitServiceModel.Empty with
        {
            Request = LivePitServiceRequest.Empty with { Fuel = true, FuelLiters = 12.5d }
        };
        var ineligible = SelectedFuelPlan(12.5d) with
        {
            FuelToAddLiters = FuelV2Scalar.From(
                12.5d,
                "test",
                FuelV2Confidence.CleanBaseline,
                strategyEligible: false)
        };

        var request = NextPitRequestComposer.From(live, ineligible);

        Assert.False(request.HasFuelPlan);
        Assert.Equal(NextPitRequestFuelAlignment.NotPlanned, request.FuelAlignment);
        Assert.Equal("live-selection-only", request.Status);
    }

    [Fact]
    public void TryFromBoundaryCell_AllowsAnExplicitHistoricalNormalSelection()
    {
        var historicalFuel = FuelV2Scalar.From(
            13.5d,
            "exact classified history",
            FuelV2Confidence.CleanBaseline,
            burnBucketId: FuelV2BurnBucketId.HistoricalNormal,
            burnSource: FuelV2BurnSource.HistoricalNormal,
            strategyEligible: true);
        var cell = new FuelV2BoundaryFeasibilityCell(
            BurnBucketId: FuelV2BurnBucketId.HistoricalNormal,
            Burn: historicalFuel,
            RangeState: FuelV2RangeBoundaryState.Available,
            FractionalRangeLaps: null,
            SafeWholeLaps: 3,
            FuelToNextCompleteLapLiters: null,
            FeasibilityState: FuelV2TargetFeasibilityState.Feasible,
            DesiredFuelLiters: historicalFuel with { Value = 53.5d },
            DesiredAddLiters: historicalFuel with { Value = 12.5d },
            TankRoomLiters: historicalFuel with { Value = 25d },
            ClampedAddLiters: historicalFuel with { Value = 12.5d },
            ShortfallLiters: null,
            MaximumFeasibleLaps: 4,
            StateFlags: []);

        var plan = FuelV2SelectedPitFuelPlan.TryFromBoundaryCell(cell, "history-backed plan");

        Assert.NotNull(plan);
        Assert.Equal(FuelV2BurnBucketId.HistoricalNormal, plan.BurnBucketId);
        Assert.Equal(12.5d, plan.FuelToAddLiters.Value);
        Assert.True(plan.CanDrivePitRequest);
    }

    private static FuelV2SelectedPitFuelPlan SelectedFuelPlan(double fuelToAddLiters)
    {
        return new FuelV2SelectedPitFuelPlan(
            BurnBucketId: FuelV2BurnBucketId.HistoricalNormal,
            FuelToAddLiters: FuelV2Scalar.From(
                fuelToAddLiters,
                "test",
                FuelV2Confidence.CleanBaseline,
                burnBucketId: FuelV2BurnBucketId.HistoricalNormal,
                burnSource: FuelV2BurnSource.HistoricalNormal,
                strategyEligible: true),
            TargetFuelLiters: FuelV2Scalar.From(
                40d,
                "test",
                FuelV2Confidence.CleanBaseline,
                burnBucketId: FuelV2BurnBucketId.HistoricalNormal,
                burnSource: FuelV2BurnSource.HistoricalNormal,
                strategyEligible: true),
            FeasibilityState: FuelV2TargetFeasibilityState.Feasible,
            TankLimited: false,
            DesiredAddLiters: null,
            TankRoomLiters: null,
            ShortfallLiters: null,
            MaximumFeasibleLaps: null,
            SelectionReason: "test selection");
    }
}
