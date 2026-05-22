using TmrOverlay.Core.Overlays;
using OverlaySizes = TmrOverlay.App.Overlays.OverlayGeometryContractValues.OverlaySizes;

namespace TmrOverlay.App.Overlays.Relative;

internal static class RelativeOverlayDefinition
{
    public static OverlayDefinition Definition { get; } = new(
        Id: "relative",
        DisplayName: "Relative",
        DefaultWidth: OverlaySizes.RelativeWidth,
        DefaultHeight: OverlaySizes.RelativeHeight,
        Options:
        [
            OverlaySettingsOptionDescriptor.Integer(
                OverlayOptionKeys.RelativeCarsEachSide,
                "Cars each side",
                0,
                8,
                defaultValue: 3)
        ],
        FadeWhenLiveTelemetryUnavailable: true);
}
