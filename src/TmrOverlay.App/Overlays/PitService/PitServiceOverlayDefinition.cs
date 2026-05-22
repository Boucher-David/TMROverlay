using TmrOverlay.Core.Overlays;
using OverlaySizes = TmrOverlay.App.Overlays.OverlayGeometryContractValues.OverlaySizes;

namespace TmrOverlay.App.Overlays.PitService;

internal static class PitServiceOverlayDefinition
{
    public static OverlayDefinition Definition { get; } = new(
        Id: "pit-service",
        DisplayName: "Pit Service",
        DefaultWidth: OverlaySizes.PitServiceWidth,
        DefaultHeight: OverlaySizes.PitServiceHeight,
        FadeWhenLiveTelemetryUnavailable: true,
        ContextRequirement: OverlayContextRequirement.LocalPlayerInCarOrPit);
}
