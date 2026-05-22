using TmrOverlay.Core.Overlays;
using OverlaySizes = TmrOverlay.App.Overlays.OverlayGeometryContractValues.OverlaySizes;

namespace TmrOverlay.App.Overlays.TrackMap;

internal static class TrackMapOverlayDefinition
{
    public static OverlayDefinition Definition { get; } = new(
        Id: "track-map",
        DisplayName: "Track Map",
        DefaultWidth: OverlaySizes.TrackMapWidth,
        DefaultHeight: OverlaySizes.TrackMapHeight,
        FadeWhenLiveTelemetryUnavailable: true);
}
