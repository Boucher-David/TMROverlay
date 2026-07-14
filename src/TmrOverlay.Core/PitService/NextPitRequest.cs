using TmrOverlay.Core.Fuel.V2;
using TmrOverlay.Core.Telemetry.Live;

namespace TmrOverlay.Core.PitService;

// This is the one shared next-stop view. It intentionally reuses the live
// Pit Service request, so Fuel V2 does not become a second owner of tire,
// tearoff, fast-repair, or repair selection.
internal sealed record NextPitRequest(
    LivePitServiceRequest CurrentSelection,
    FuelV2SelectedPitFuelPlan? FuelPlan,
    NextPitRequestFuelAlignment FuelAlignment,
    bool IsServiceActive,
    bool IsInPitStall,
    string Status)
{
    // Timing is deliberately absent in this first contract. A later shared
    // learned-service selector may add a time estimate only after the ruleset
    // and stationary-service evidence match the current request shape.
    public bool HasFuelPlan => FuelPlan?.CanDrivePitRequest == true;
}

internal enum NextPitRequestFuelAlignment
{
    NotPlanned = 0,
    NotRequested = 1,
    Unknown = 2,
    MatchesPlan = 3,
    DiffersFromPlan = 4
}

internal static class NextPitRequestComposer
{
    private const double FuelAlignmentToleranceLiters = 0.1d;

    public static NextPitRequest From(
        LivePitServiceModel livePitService,
        FuelV2SelectedPitFuelPlan? fuelPlan)
    {
        ArgumentNullException.ThrowIfNull(livePitService);

        var alignment = FuelAlignment(livePitService.Request, fuelPlan);
        return new NextPitRequest(
            CurrentSelection: livePitService.Request,
            FuelPlan: fuelPlan?.CanDrivePitRequest == true ? fuelPlan : null,
            FuelAlignment: alignment,
            IsServiceActive: livePitService.PitstopActive,
            IsInPitStall: livePitService.PlayerCarInPitStall,
            Status: Status(livePitService, fuelPlan, alignment));
    }

    private static NextPitRequestFuelAlignment FuelAlignment(
        LivePitServiceRequest currentSelection,
        FuelV2SelectedPitFuelPlan? fuelPlan)
    {
        if (fuelPlan?.CanDrivePitRequest != true)
        {
            return NextPitRequestFuelAlignment.NotPlanned;
        }

        var plannedAdd = fuelPlan.FuelToAddLiters.Value;
        if (plannedAdd is null)
        {
            return NextPitRequestFuelAlignment.NotPlanned;
        }

        if (plannedAdd <= FuelAlignmentToleranceLiters && !currentSelection.Fuel)
        {
            return NextPitRequestFuelAlignment.NotRequested;
        }

        if (!currentSelection.Fuel)
        {
            return NextPitRequestFuelAlignment.DiffersFromPlan;
        }

        if (currentSelection.FuelLiters is not { } requestedAdd)
        {
            return NextPitRequestFuelAlignment.Unknown;
        }

        return Math.Abs(requestedAdd - plannedAdd.Value) <= FuelAlignmentToleranceLiters
            ? NextPitRequestFuelAlignment.MatchesPlan
            : NextPitRequestFuelAlignment.DiffersFromPlan;
    }

    private static string Status(
        LivePitServiceModel livePitService,
        FuelV2SelectedPitFuelPlan? fuelPlan,
        NextPitRequestFuelAlignment alignment)
    {
        if (livePitService.PitstopActive)
        {
            return "service-active";
        }

        if (fuelPlan?.CanDrivePitRequest != true)
        {
            return "live-selection-only";
        }

        return alignment switch
        {
            NextPitRequestFuelAlignment.MatchesPlan => "fuel-selection-matches-plan",
            NextPitRequestFuelAlignment.DiffersFromPlan => "fuel-selection-differs-from-plan",
            NextPitRequestFuelAlignment.NotRequested => "no-refuel-needed",
            _ => "fuel-selection-unconfirmed"
        };
    }
}
