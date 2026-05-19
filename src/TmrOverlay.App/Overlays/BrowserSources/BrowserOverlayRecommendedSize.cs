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
        var baseSize = OverlayContentSizing.BaseSizeFor(definition, settings, sessionKind);
        if (!OverlayContentColumnSettings.TryGetContentDefinition(definition.Id, out var contentDefinition)
            || contentDefinition.Columns.Count == 0)
        {
            return baseSize;
        }

        return new Size(
            BrowserTableWidth(definition, settings, contentDefinition, sessionKind, baseSize.Width),
            baseSize.Height);
    }

    public static Size ScaledFor(
        OverlayDefinition definition,
        OverlaySettings settings,
        OverlaySessionKind? sessionKind = null)
    {
        var baseSize = For(definition, settings, sessionKind);
        return new Size(
            ScaleDimension(baseSize.Width, settings.Scale),
            ScaleDimension(baseSize.Height, settings.Scale));
    }

    private static int ScaleDimension(int defaultDimension, double scale)
    {
        return Math.Max(80, (int)Math.Round(defaultDimension * Math.Clamp(scale, 0.6d, 2d)));
    }

    private static int BrowserTableWidth(
        OverlayDefinition definition,
        OverlaySettings settings,
        OverlayContentDefinition contentDefinition,
        OverlaySessionKind? sessionKind,
        int fallbackWidth)
    {
        var visibleWidth = OverlayContentColumnSettings
            .VisibleColumnsFor(settings, contentDefinition, sessionKind)
            .Sum(column => column.Width);
        var defaultWidth = DefaultVisibleTableWidth(contentDefinition);
        if (visibleWidth <= 0 || defaultWidth <= 0)
        {
            return fallbackWidth;
        }

        var browserDefaultWidth = BrowserDefaultTableWidth(definition, contentDefinition);
        if (visibleWidth <= defaultWidth)
        {
            var proportionalWidth = (int)Math.Round(browserDefaultWidth * (visibleWidth / (double)defaultWidth));
            return Math.Max(360, proportionalWidth);
        }

        return Math.Max(browserDefaultWidth, visibleWidth + contentDefinition.BrowserWidthPadding);
    }

    private static int BrowserDefaultTableWidth(
        OverlayDefinition definition,
        OverlayContentDefinition contentDefinition)
    {
        var defaultWidth = definition.DefaultWidth;
        if (string.Equals(contentDefinition.OverlayId, "standings", StringComparison.Ordinal))
        {
            defaultWidth = Math.Max(
                defaultWidth,
                DefaultVisibleTableWidth(contentDefinition) + contentDefinition.BrowserWidthPadding);
        }

        return defaultWidth;
    }

    private static int DefaultVisibleTableWidth(OverlayContentDefinition definition)
    {
        return definition.Columns
            .Where(column => column.DefaultEnabled)
            .Sum(column => column.DefaultWidth);
    }
}
