using TmrOverlay.Core.Overlays;
using SettingsGeometry = TmrOverlay.App.Overlays.OverlayGeometryContractValues.SettingsGeometry;

namespace TmrOverlay.App.Overlays.SettingsPanel;

internal static class SettingsOverlayDefinition
{
    public static OverlayDefinition Definition { get; } = new(
        Id: "settings",
        DisplayName: "Settings",
        DefaultWidth: SettingsGeometry.ShellWidth,
        DefaultHeight: SettingsGeometry.ShellHeight);
}
