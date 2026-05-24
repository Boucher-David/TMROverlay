using TmrOverlay.App.Overlays.Content;
using TmrOverlay.Core.Overlays;
using TmrOverlay.Core.Settings;

namespace TmrOverlay.App.Overlays.Standings;

internal static class StandingsOverlaySizing
{
    public const int ChromeOnlyClientHeight = 40;

    private const int AssumedBrowserSourceClassCount = 3;
    private const int HeaderHeight = 38;
    private const int BodyGap = 12;
    private const int PaddingSize = 16;
    private const int FooterHeight = 32;
    private const int CollapsedFooterHeight = 8;
    private const int TableHeaderReserveHeight = 30;
    private const int RowHeight = 30;
    private const int RowGap = 5;
    private const int BodyBorderReserve = 1;

    public static int TargetClientHeightForRows(
        int rowCount,
        int persistedHeight,
        bool showHeader,
        bool showFooter)
    {
        var visibleRows = Math.Clamp(
            Math.Max(1, rowCount),
            1,
            StandingsOverlayViewModel.MaximumRenderedRows);
        var persistedVisibleRows = VisibleRowsForHeight(persistedHeight, showHeader, showFooter);
        if (visibleRows <= persistedVisibleRows)
        {
            return persistedHeight;
        }

        return Math.Max(
            persistedHeight,
            HeaderReserveHeight(showHeader)
            + FooterReserveHeight(showFooter)
            + BodyBorderReserve
            + TableHeaderReserveHeight
            + (visibleRows * (RowHeight + RowGap)));
    }

    public static int VisibleRowsForHeight(int clientHeight, bool showHeader, bool showFooter)
    {
        var bodyHeight = clientHeight
            - HeaderReserveHeight(showHeader)
            - FooterReserveHeight(showFooter)
            - BodyBorderReserve;
        return Math.Max(1, (bodyHeight - TableHeaderReserveHeight) / (RowHeight + RowGap));
    }

    public static int RecommendedBrowserSourceRows(OverlaySettings settings, OverlaySessionKind? sessionKind)
    {
        var carsInClass = settings.GetIntegerOption(
            OverlayOptionKeys.StandingsCarsInClass,
            defaultValue: StandingsBrowserSettings.Default.MaximumRows,
            minimum: StandingsBrowserSettings.MinimumCarsInClass,
            maximum: StandingsBrowserSettings.MaximumCarsInClass);
        var classSeparatorBlock = OverlayContentColumnSettings.Standings.Blocks?
            .FirstOrDefault(block => string.Equals(block.Id, OverlayContentColumnSettings.StandingsClassSeparatorBlockId, StringComparison.Ordinal));
        var classSeparatorsEnabled = classSeparatorBlock is null
            || OverlayContentColumnSettings.BlockEnabled(settings, classSeparatorBlock, sessionKind);
        if (!classSeparatorsEnabled)
        {
            return carsInClass;
        }

        var otherClassRows = settings.GetIntegerOption(
            OverlayOptionKeys.StandingsOtherClassRows,
            defaultValue: StandingsBrowserSettings.Default.OtherClassRowsPerClass,
            minimum: 0,
            maximum: 6);
        var classCount = otherClassRows > 0 ? AssumedBrowserSourceClassCount : 1;
        var otherClassCount = Math.Max(0, classCount - 1);
        return Math.Clamp(
            carsInClass + classCount + otherClassCount * otherClassRows,
            1,
            StandingsOverlayViewModel.MaximumRenderedRows);
    }

    private static int HeaderReserveHeight(bool showHeader)
    {
        return showHeader ? HeaderHeight + BodyGap : PaddingSize;
    }

    private static int FooterReserveHeight(bool showFooter)
    {
        return showFooter ? FooterHeight : CollapsedFooterHeight;
    }
}
