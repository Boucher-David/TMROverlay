using TmrOverlay.Core.Overlays;

namespace TmrOverlay.App.Overlays.Relative;

internal static class RelativeOverlayDefinition
{
    public static OverlayDefinition Definition { get; } = new(
        Id: "relative",
        DisplayName: "Relative",
        DefaultWidth: 360,
        DefaultHeight: 373,
        Options:
        [
            OverlaySettingsOptionDescriptor.Integer(
                OverlayOptionKeys.RelativeCarsEachSide,
                "Cars each side",
                0,
                8,
                defaultValue: 5)
        ],
        FadeWhenLiveTelemetryUnavailable: true);
}
