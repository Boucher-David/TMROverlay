namespace TmrOverlay.Core.Overlays;

// The persisted Enabled setting is the outermost product authority. Preview
// can relax the telemetry/session/content gates for an already-enabled
// overlay, but it must never resurrect an overlay the user turned off.
internal static class OverlayVisibilityPolicy
{
    public static bool ShouldShowManagedOverlay(
        bool isUserEnabled,
        bool isSessionAllowed,
        bool hasRequiredContext,
        bool hasEnabledContent,
        bool isSettingsPreview)
    {
        return isUserEnabled
            && (isSettingsPreview
                || (isSessionAllowed && hasRequiredContext && hasEnabledContent));
    }
}
