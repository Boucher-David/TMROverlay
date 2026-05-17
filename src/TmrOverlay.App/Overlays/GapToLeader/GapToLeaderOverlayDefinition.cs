using TmrOverlay.Core.Overlays;

namespace TmrOverlay.App.Overlays.GapToLeader;

internal static class GapToLeaderOverlayDefinition
{
    public static OverlayDefinition Definition { get; } = new(
        Id: "gap-to-leader",
        DisplayName: "Gap To Leader",
        DefaultWidth: 654,
        DefaultHeight: 357,
        Options:
        [
            OverlaySettingsOptionDescriptor.Integer(
                OverlayOptionKeys.GapCarsAhead,
                "Cars ahead",
                0,
                12,
                defaultValue: 5),
            OverlaySettingsOptionDescriptor.Integer(
                OverlayOptionKeys.GapCarsBehind,
                "Cars behind",
                0,
                12,
                defaultValue: 5)
        ],
        ShowSessionFilters: false,
        FadeWhenLiveTelemetryUnavailable: true);
}
