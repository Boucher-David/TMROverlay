using System.Drawing;
using TmrOverlay.App.Overlays.Content;
using TmrOverlay.Core.Overlays;
using TmrOverlay.Core.Settings;

namespace TmrOverlay.App.Overlays.BrowserSources;

internal static class BrowserOverlayRecommendedSize
{
    public static Size For(
        OverlayDefinition definition,
        OverlaySettings settings,
        OverlaySessionKind? sessionKind = null)
    {
        return OverlayContentSizing.BaseSizeFor(definition, settings, sessionKind);
    }
}
