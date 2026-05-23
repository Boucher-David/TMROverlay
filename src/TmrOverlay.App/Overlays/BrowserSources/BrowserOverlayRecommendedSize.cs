using System.Drawing;
using TmrOverlay.App.Overlays.Content;
using TmrOverlay.App.Overlays.Standings;
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
        if (string.Equals(definition.Id, StandingsOverlayDefinition.Definition.Id, StringComparison.Ordinal))
        {
            baseSize = new Size(
                baseSize.Width,
                StandingsOverlaySizing.TargetClientHeightForRows(
                    StandingsOverlaySizing.RecommendedBrowserSourceRows(settings, sessionKind),
                    baseSize.Height,
                    OverlayChromeSettings.ShowHeaderTimeRemainingForSession(settings, sessionKind),
                    OverlayChromeSettings.ShowFooterSourceForSession(settings, sessionKind)));
        }

        if (!OverlayContentColumnSettings.TryGetContentDefinition(definition.Id, out var contentDefinition)
            || contentDefinition.Columns.Count == 0)
        {
            return baseSize;
        }

        return new Size(
            BrowserTableWidth(settings, contentDefinition, sessionKind, baseSize.Width),
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
        OverlaySettings settings,
        OverlayContentDefinition contentDefinition,
        OverlaySessionKind? sessionKind,
        int fallbackWidth)
    {
        var visibleWidth = OverlayContentColumnSettings
            .VisibleColumnsFor(settings, contentDefinition, sessionKind)
            .Sum(column => column.Width);
        if (visibleWidth <= 0)
        {
            return fallbackWidth;
        }

        return visibleWidth + contentDefinition.BrowserWidthPadding;
    }
}
