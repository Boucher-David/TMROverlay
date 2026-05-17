using System.Drawing;
using TmrOverlay.App.Overlays.Content;
using TmrOverlay.App.Overlays.InputState;
using TmrOverlay.App.Overlays.Relative;
using TmrOverlay.Core.Overlays;
using TmrOverlay.Core.Settings;

namespace TmrOverlay.App.Overlays.BrowserSources;

internal static class BrowserOverlayRecommendedSize
{
    public static Size For(OverlayDefinition definition, OverlaySettings settings)
    {
        var baseSize = new Size(
            settings.Width > 0 ? settings.Width : definition.DefaultWidth,
            settings.Height > 0 ? settings.Height : definition.DefaultHeight);

        if (string.Equals(definition.Id, InputStateOverlayDefinition.Definition.Id, StringComparison.Ordinal))
        {
            return new Size(
                InputStateRenderModelBuilder.BaseWidthForEnabledContent(settings, definition.DefaultWidth),
                Math.Max(baseSize.Height, definition.DefaultHeight));
        }

        if (OverlayContentColumnSettings.TryGetContentDefinition(definition.Id, out var contentDefinition)
            && contentDefinition.Columns.Count > 0)
        {
            if (string.Equals(definition.Id, RelativeOverlayDefinition.Definition.Id, StringComparison.Ordinal))
            {
                return new Size(
                    Math.Max(definition.DefaultWidth, baseSize.Width),
                    Math.Max(definition.DefaultHeight, Math.Max(baseSize.Height, contentDefinition.BrowserMinimumHeight)));
            }

            var contentWidth = OverlayContentColumnSettings.TotalVisibleTableWidth(
                settings,
                contentDefinition);
            return new Size(
                Math.Max(definition.DefaultWidth, Math.Max(1, contentWidth + contentDefinition.BrowserWidthPadding)),
                Math.Max(definition.DefaultHeight, Math.Max(baseSize.Height, contentDefinition.BrowserMinimumHeight)));
        }

        return baseSize;
    }
}
