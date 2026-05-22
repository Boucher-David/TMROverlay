using TmrOverlay.Core.Settings;

namespace TmrOverlay.App.Overlays;

internal static class OverlayZOrderPolicy
{
    public static bool ShouldSettingsWindowBeTopMost(bool settingsWindowVisible)
    {
        return false;
    }

    public static bool ShouldManagedOverlayBeTopMost(
        OverlaySettings settings,
        bool settingsWindowActive = false,
        bool intersectsSettingsWindow = false)
    {
        return settings.AlwaysOnTop
            && (!settingsWindowActive || !intersectsSettingsWindow);
    }

    public static bool ShouldProtectSettingsWindowInput(
        bool settingsWindowVisible,
        bool isSettingsWindow,
        bool intersectsSettingsWindow)
    {
        return settingsWindowVisible && !isSettingsWindow && intersectsSettingsWindow;
    }

    public static bool ShouldOverlayBeInputTransparent(
        bool intrinsicallyTransparent,
        bool forceInputTransparent,
        bool settingsWindowVisible,
        bool isSettingsWindow,
        bool intersectsSettingsWindow)
    {
        return intrinsicallyTransparent
            || forceInputTransparent
            || ShouldProtectSettingsWindowInput(settingsWindowVisible, isSettingsWindow, intersectsSettingsWindow);
    }
}
