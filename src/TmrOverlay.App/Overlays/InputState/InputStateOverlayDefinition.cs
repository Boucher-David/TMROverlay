using TmrOverlay.Core.Overlays;

namespace TmrOverlay.App.Overlays.InputState;

internal static class InputStateOverlayDefinition
{
    public static OverlayDefinition Definition { get; } = new(
        Id: "input-state",
        DisplayName: "Inputs",
        DefaultWidth: 520,
        DefaultHeight: 260,
        FadeWhenLiveTelemetryUnavailable: true,
        ContextRequirement: OverlayContextRequirement.LocalPlayerInCarOrPit);
}
