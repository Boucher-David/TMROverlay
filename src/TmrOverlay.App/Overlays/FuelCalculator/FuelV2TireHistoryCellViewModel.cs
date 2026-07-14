using TmrOverlay.App.History;
using TmrOverlay.App.Overlays.SimpleTelemetry;

namespace TmrOverlay.App.Overlays.FuelCalculator;

// Presentation contract for the future Stint-row Tires cell. The current
// factual Fuel V2 overlay deliberately has no Stint rows yet, but both native
// and localhost composition carry this typed cell now so that the eventual
// row cannot fall back to V1's generic tire-set timing estimate.
internal sealed record FuelV2TireHistoryCellViewModel(
    bool ShouldRender,
    string Value,
    string Detail,
    SimpleTelemetryTone Tone,
    bool HasConfirmedExactOutcome,
    bool CanCalibrateServiceTime)
{
    public static FuelV2TireHistoryCellViewModel Hidden { get; } = new(
        ShouldRender: false,
        Value: "--",
        Detail: "No exact tire-service history is available.",
        Tone: SimpleTelemetryTone.Waiting,
        HasConfirmedExactOutcome: false,
        CanCalibrateServiceTime: false);

    public static FuelV2TireHistoryCellViewModel From(FuelV2TireServiceHistorySelection? selection)
    {
        if (selection?.RequestedShape is not { RequestedTireCount: > 0 } shape)
        {
            return Hidden;
        }

        if (selection.HasConfirmedExactOutcome && selection.Profile is { } profile)
        {
            return new FuelV2TireHistoryCellViewModel(
                ShouldRender: true,
                Value: $"{shape.DisplayLabel} — observed",
                Detail: $"{profile.ConfirmedOutcomeCount} exact confirmed outcome(s); {selection.Detail}",
                Tone: SimpleTelemetryTone.Info,
                HasConfirmedExactOutcome: true,
                CanCalibrateServiceTime: profile.CanCalibrateServiceTime);
        }

        if (selection.Status == FuelV2TireServiceHistorySelectionStatus.RequestedOnly)
        {
            return new FuelV2TireHistoryCellViewModel(
                ShouldRender: true,
                Value: $"{shape.DisplayLabel} — collect sample",
                Detail: selection.Detail,
                Tone: SimpleTelemetryTone.Waiting,
                HasConfirmedExactOutcome: false,
                CanCalibrateServiceTime: false);
        }

        return Hidden;
    }
}
