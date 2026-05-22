using TmrOverlay.Core.Overlays;
using OverlaySizes = TmrOverlay.App.Overlays.OverlayGeometryContractValues.OverlaySizes;

namespace TmrOverlay.App.Overlays.GapToLeader;

internal static class GapToLeaderOverlayDefinition
{
    public static OverlayDefinition Definition { get; } = new(
        Id: "gap-to-leader",
        DisplayName: "Gap To Leader",
        DefaultWidth: OverlaySizes.GapToLeaderWidth,
        DefaultHeight: OverlaySizes.GapToLeaderHeight,
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
                defaultValue: 5),
            OverlaySettingsOptionDescriptor.Boolean(
                OverlayOptionKeys.GapGraphEnabled,
                "Graph",
                defaultValue: true),
            OverlaySettingsOptionDescriptor.Boolean(
                OverlayOptionKeys.GapTrendLastEnabled,
                "Last signal",
                defaultValue: true),
            OverlaySettingsOptionDescriptor.Boolean(
                OverlayOptionKeys.GapTrend5LEnabled,
                "5L signal",
                defaultValue: true),
            OverlaySettingsOptionDescriptor.Boolean(
                OverlayOptionKeys.GapTrend10LEnabled,
                "10L signal",
                defaultValue: true),
            OverlaySettingsOptionDescriptor.Boolean(
                OverlayOptionKeys.GapTrendPitEnabled,
                "Pit signal",
                defaultValue: true),
            OverlaySettingsOptionDescriptor.Boolean(
                OverlayOptionKeys.GapTrendPitLapEnabled,
                "PLap signal",
                defaultValue: true),
            OverlaySettingsOptionDescriptor.Boolean(
                OverlayOptionKeys.GapTrendStintEnabled,
                "Stint signal",
                defaultValue: true),
            OverlaySettingsOptionDescriptor.Boolean(
                OverlayOptionKeys.GapTrendTireEnabled,
                "Tire signal",
                defaultValue: true),
            OverlaySettingsOptionDescriptor.Boolean(
                OverlayOptionKeys.GapTrendStatusEnabled,
                "Status signal",
                defaultValue: true)
        ],
        ShowSessionFilters: false,
        FadeWhenLiveTelemetryUnavailable: true);
}
