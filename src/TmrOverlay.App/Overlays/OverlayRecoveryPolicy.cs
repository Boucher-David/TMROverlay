using TmrOverlay.Core.Overlays;
using TmrOverlay.Core.Settings;

namespace TmrOverlay.App.Overlays;

internal static class OverlayRecoveryPolicy
{
    public static bool HasEnabledManagedOverlays(
        ApplicationSettings settings,
        IEnumerable<OverlayDefinition> definitions)
    {
        return definitions.Any(definition =>
            settings.Overlays.Any(overlay =>
                overlay.Enabled
                && string.Equals(overlay.Id, definition.Id, StringComparison.OrdinalIgnoreCase)));
    }

    public static int DisableManagedOverlays(
        ApplicationSettings settings,
        IEnumerable<OverlayDefinition> definitions)
    {
        var disabledCount = 0;
        foreach (var definition in definitions)
        {
            var overlay = settings.Overlays.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, definition.Id, StringComparison.OrdinalIgnoreCase));
            if (overlay is null || !overlay.Enabled)
            {
                continue;
            }

            overlay.Enabled = false;
            disabledCount++;
        }

        return disabledCount;
    }
}
