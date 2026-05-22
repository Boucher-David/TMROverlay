using TmrOverlay.Core.Overlays;
using OverlaySizes = TmrOverlay.App.Overlays.OverlayGeometryContractValues.OverlaySizes;

namespace TmrOverlay.App.Overlays.GarageCover;

internal static class GarageCoverOverlayDefinition
{
    public static OverlayDefinition Definition { get; } = new(
        Id: "garage-cover",
        DisplayName: "Garage Cover",
        DefaultWidth: OverlaySizes.GarageCoverWidth,
        DefaultHeight: OverlaySizes.GarageCoverHeight,
        ShowScaleControl: true,
        ShowOpacityControl: false);
}
