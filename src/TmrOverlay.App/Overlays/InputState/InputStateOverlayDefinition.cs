using TmrOverlay.Core.Overlays;
using OverlaySizes = TmrOverlay.App.Overlays.OverlayGeometryContractValues.OverlaySizes;

namespace TmrOverlay.App.Overlays.InputState;

internal static class InputStateOverlayDefinition
{
    public static OverlayDefinition Definition { get; } = new(
        Id: "input-state",
        DisplayName: "Inputs",
        DefaultWidth: OverlaySizes.InputStateWidth,
        DefaultHeight: OverlaySizes.InputStateHeight,
        FadeWhenLiveTelemetryUnavailable: true,
        ContextRequirement: OverlayContextRequirement.LocalPlayerInCarOrPit);
}
