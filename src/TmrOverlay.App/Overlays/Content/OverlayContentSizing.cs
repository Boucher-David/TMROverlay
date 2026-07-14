using System.Drawing;
using TmrOverlay.App.Overlays.FuelCalculator;
using TmrOverlay.App.Overlays.Flags;
using TmrOverlay.App.Overlays.GapToLeader;
using TmrOverlay.App.Overlays.InputState;
using TmrOverlay.App.Overlays.PitService;
using TmrOverlay.App.Overlays.Relative;
using TmrOverlay.App.Overlays.SessionWeather;
using TmrOverlay.App.Overlays.SimpleTelemetry;
using TmrOverlay.App.Overlays.Standings;
using TmrOverlay.App.Overlays.StreamChat;
using TmrOverlay.App.Overlays;
using TmrOverlay.Core.Overlays;
using TmrOverlay.Core.Settings;
using OverlaySizes = TmrOverlay.App.Overlays.OverlayGeometryContractValues.OverlaySizes;
using TableGeometry = TmrOverlay.App.Overlays.OverlayGeometryContractValues.TableGeometry;

namespace TmrOverlay.App.Overlays.Content;

internal static class OverlayContentSizing
{
    private const int MinimumRelativeHeight = TableGeometry.RelativeMinimumHeight;
    private const int RelativeTableChromeHeight = TableGeometry.RelativeHeightChrome;
    private const int RelativeTableRowHeight = TableGeometry.RelativeRowHeight;
    private const int RelativeTableRowGap = TableGeometry.RelativeRowGap;
    private const int MinimumGapToLeaderWidth = OverlaySizes.GapToLeaderMinimumWidth;
    private const int GapToLeaderGraphOnlyWidth = OverlaySizes.GapToLeaderGraphOnlyWidth;
    private const int GapToLeaderTrendOnlyWidth = OverlaySizes.GapToLeaderTrendOnlyWidth;
    private const int GapToLeaderMinimumHeight = OverlaySizes.GapToLeaderMinimumHeight;
    private const int RelativeHeaderChromeCollapseHeight = OverlaySizes.RelativeHeaderChromeCollapseHeight;

    private static MetricRowsGeometryContract MetricGeometry => OverlayGeometryContracts.MetricRows;

    public static Size BaseSizeFor(
        OverlayDefinition definition,
        OverlaySettings settings,
        OverlaySessionKind? sessionKind = null)
    {
        var baseSize = new Size(definition.DefaultWidth, definition.DefaultHeight);
        if (string.Equals(definition.Id, InputStateOverlayDefinition.Definition.Id, StringComparison.Ordinal))
        {
            baseSize = new Size(
                InputStateRenderModelBuilder.BaseWidthForEnabledContent(settings, definition.DefaultWidth, sessionKind),
                definition.DefaultHeight);
            return ApplyChromeHeight(definition, settings, sessionKind, baseSize);
        }

        if (string.Equals(definition.Id, FuelCalculatorOverlayDefinition.Definition.Id, StringComparison.Ordinal)
            && OverlayContentColumnSettings.TryGetContentDefinition(definition.Id, out var fuelContentDefinition))
        {
            baseSize = new Size(
                definition.DefaultWidth,
                FuelCalculatorHeight(definition, settings, fuelContentDefinition, sessionKind));
            return ApplyChromeHeight(definition, settings, sessionKind, baseSize);
        }

        if (string.Equals(definition.Id, GapToLeaderOverlayDefinition.Definition.Id, StringComparison.Ordinal))
        {
            baseSize = GapToLeaderSize(definition, settings, sessionKind);
            return ApplyChromeHeight(definition, settings, sessionKind, baseSize);
        }

        if (!OverlayContentColumnSettings.TryGetContentDefinition(definition.Id, out var contentDefinition))
        {
            return ApplyChromeHeight(definition, settings, sessionKind, baseSize);
        }

        if (contentDefinition.Columns.Count > 0)
        {
            baseSize = new Size(
                TableOverlayWidth(definition, settings, contentDefinition, sessionKind),
                TableOverlayHeight(definition, settings));
            return ApplyChromeHeight(definition, settings, sessionKind, baseSize);
        }

        if (UsesSimpleTelemetryContentSizing(definition.Id))
        {
            baseSize = new Size(
                SimpleTelemetryWidth(definition, settings, contentDefinition, sessionKind),
                SimpleTelemetryHeight(definition, settings, contentDefinition, sessionKind));
            return ApplyChromeHeight(definition, settings, sessionKind, baseSize);
        }

        return ApplyChromeHeight(definition, settings, sessionKind, baseSize);
    }

    public static bool HasRenderableContent(
        OverlayDefinition definition,
        OverlaySettings settings,
        OverlaySessionKind? sessionKind = null)
    {
        if (string.Equals(definition.Id, InputStateOverlayDefinition.Definition.Id, StringComparison.Ordinal))
        {
            return InputStateRenderModelBuilder.HasEnabledContent(settings, sessionKind);
        }

        if (string.Equals(definition.Id, FuelCalculatorOverlayDefinition.Definition.Id, StringComparison.Ordinal))
        {
            return OverlayContentColumnSettings.TryGetContentDefinition(definition.Id, out var contentDefinition)
                && EnabledFuelBlockCount(settings, contentDefinition, sessionKind) > 0;
        }

        if (string.Equals(definition.Id, GapToLeaderOverlayDefinition.Definition.Id, StringComparison.Ordinal))
        {
            return GapWindowEnabled(settings)
                && (GapGraphEnabled(settings, sessionKind) || EnabledGapTrendBlockCount(settings, sessionKind) > 0);
        }

        if (string.Equals(definition.Id, SessionWeatherOverlayDefinition.Definition.Id, StringComparison.Ordinal)
            || string.Equals(definition.Id, PitServiceOverlayDefinition.Definition.Id, StringComparison.Ordinal))
        {
            return OverlayContentColumnSettings.TryGetContentDefinition(definition.Id, out var contentDefinition)
                && EnabledBlockCount(settings, contentDefinition, sessionKind) > 0;
        }

        if (string.Equals(definition.Id, FlagsOverlayDefinition.Definition.Id, StringComparison.Ordinal))
        {
            return settings.GetBooleanOption(OverlayOptionKeys.FlagsShowGreen, defaultValue: true)
                || settings.GetBooleanOption(OverlayOptionKeys.FlagsShowBlue, defaultValue: true)
                || settings.GetBooleanOption(OverlayOptionKeys.FlagsShowYellow, defaultValue: true)
                || settings.GetBooleanOption(OverlayOptionKeys.FlagsShowCritical, defaultValue: true)
                || settings.GetBooleanOption(OverlayOptionKeys.FlagsShowFinish, defaultValue: true);
        }

        if (OverlayContentColumnSettings.TryGetContentDefinition(definition.Id, out var columnContentDefinition)
            && columnContentDefinition.Columns.Count > 0)
        {
            return OverlayContentColumnSettings.EnabledColumnsFor(
                    settings,
                    columnContentDefinition.Columns,
                    sessionKind)
                .Count > 0;
        }

        return true;
    }

    public static int RelativeVisibleRows(OverlaySettings settings)
    {
        return Math.Clamp(RelativeBrowserSettings.CarsEachSide(settings), 0, 8) * 2 + 1;
    }

    public static int EnabledBlockCount(
        OverlaySettings settings,
        OverlayContentDefinition definition,
        OverlaySessionKind? sessionKind = null)
    {
        if (definition.Blocks is not { Count: > 0 } blocks)
        {
            return 0;
        }

        return blocks.Count(block => OverlayContentColumnSettings.BlockEnabled(settings, block, sessionKind));
    }

    public static int DefaultEnabledBlockCount(OverlayContentDefinition definition)
    {
        return definition.Blocks?.Count(block => block.DefaultEnabled) ?? 0;
    }

    public static int FuelCalculatorHeightForContent(int rowCount, int sectionCount, bool clampToDefaultHeight = true)
    {
        if (rowCount <= 0 || sectionCount <= 0)
        {
            return MetricGeometry.MinimumFuelCalculatorHeight;
        }

        var rowGaps = (int)Math.Round(Math.Max(0, rowCount - sectionCount) * MetricGeometry.RowGap);
        var sectionGaps = (int)Math.Round(Math.Max(0, sectionCount - 1) * MetricGeometry.SectionGap);
        var height = MetricGeometry.HeaderChromeHeight
            + MetricGeometry.FuelContentVerticalPadding
            + sectionCount * MetricGeometry.FuelSectionTitleReserveHeight
            + (int)Math.Round(rowCount * MetricGeometry.SegmentedRowHeight)
            + rowGaps
            + sectionGaps
            + MetricGeometry.CollapsedFooterReserveHeight;
        return clampToDefaultHeight
            ? Math.Clamp(height, MetricGeometry.MinimumFuelCalculatorHeight, FuelCalculatorOverlayDefinition.Definition.DefaultHeight)
            : Math.Max(MetricGeometry.MinimumFuelCalculatorHeight, height);
    }

    public static Size FuelCalculatorSizeForMetricSections(
        OverlayDefinition definition,
        OverlaySettings settings,
        OverlaySessionKind? sessionKind,
        IReadOnlyList<SimpleTelemetryMetricSectionViewModel> metricSections,
        bool clampToDefaultHeight = true,
        int? contentWidth = null)
    {
        var visibleSections = metricSections
            .Where(section => section.Rows.Count > 0)
            .ToArray();
        if (visibleSections.Length == 0)
        {
            return BaseSizeFor(definition, settings, sessionKind);
        }

        var rowCount = visibleSections.Sum(section => section.Rows.Count);
        var baseSize = new Size(
            contentWidth is > 0 ? contentWidth.Value : definition.DefaultWidth,
            FuelCalculatorHeightForContent(rowCount, visibleSections.Length, clampToDefaultHeight));
        return ApplyChromeHeight(definition, settings, sessionKind, baseSize);
    }

    public static Size SimpleTelemetrySizeForRenderedSections(
        OverlayDefinition definition,
        OverlaySettings settings,
        OverlaySessionKind? sessionKind,
        IReadOnlyList<SimpleTelemetryMetricSectionViewModel> metricSections,
        IReadOnlyList<SimpleTelemetryGridSectionViewModel> gridSections)
    {
        if (!UsesSimpleTelemetryContentSizing(definition.Id)
            || !OverlayContentColumnSettings.TryGetContentDefinition(definition.Id, out var contentDefinition))
        {
            return BaseSizeFor(definition, settings, sessionKind);
        }

        var visibleMetricSections = metricSections
            .Where(section => section.Rows.Count > 0)
            .ToArray();
        var visibleGridSections = gridSections
            .Where(section => section.Rows.Count > 0)
            .ToArray();
        if (visibleMetricSections.Length == 0 && visibleGridSections.Length == 0)
        {
            return BaseSizeFor(definition, settings, sessionKind);
        }

        var width = SimpleTelemetryRenderedWidth(
            definition,
            settings,
            contentDefinition,
            sessionKind,
            visibleGridSections.Length > 0);
        var baseSize = new Size(
            width,
            SimpleTelemetryRenderedHeight(
                visibleMetricSections.Select(section => section.Rows.Count).ToArray(),
                visibleGridSections.Select(section => section.Rows.Count).ToArray(),
                definition.DefaultHeight));
        return EnsureFullSessionWeatherChromeOffHeight(
            definition,
            settings,
            sessionKind,
            visibleMetricSections.Select(section => section.Rows.Count).ToArray(),
            visibleGridSections.Select(section => section.Rows.Count).ToArray(),
            ApplyChromeHeight(definition, settings, sessionKind, baseSize));
    }

    public static Size SimpleTelemetrySizeForRenderedRowCounts(
        OverlayDefinition definition,
        OverlaySettings settings,
        OverlaySessionKind? sessionKind,
        IReadOnlyList<int> metricRowCounts,
        IReadOnlyList<int> gridRowCounts)
    {
        if (!UsesSimpleTelemetryContentSizing(definition.Id)
            || !OverlayContentColumnSettings.TryGetContentDefinition(definition.Id, out var contentDefinition))
        {
            return BaseSizeFor(definition, settings, sessionKind);
        }

        var visibleMetricRowCounts = metricRowCounts.Where(rowCount => rowCount > 0).ToArray();
        var visibleGridRowCounts = gridRowCounts.Where(rowCount => rowCount > 0).ToArray();
        if (visibleMetricRowCounts.Length == 0 && visibleGridRowCounts.Length == 0)
        {
            return BaseSizeFor(definition, settings, sessionKind);
        }

        var width = SimpleTelemetryRenderedWidth(
            definition,
            settings,
            contentDefinition,
            sessionKind,
            visibleGridRowCounts.Length > 0);
        var baseSize = new Size(
            width,
            SimpleTelemetryRenderedHeight(visibleMetricRowCounts, visibleGridRowCounts, definition.DefaultHeight));
        return EnsureFullSessionWeatherChromeOffHeight(
            definition,
            settings,
            sessionKind,
            visibleMetricRowCounts,
            visibleGridRowCounts,
            ApplyChromeHeight(definition, settings, sessionKind, baseSize));
    }

    private static Size EnsureFullSessionWeatherChromeOffHeight(
        OverlayDefinition definition,
        OverlaySettings settings,
        OverlaySessionKind? sessionKind,
        IReadOnlyList<int> metricRowCounts,
        IReadOnlyList<int> gridRowCounts,
        Size size)
    {
        if (!string.Equals(definition.Id, SessionWeatherOverlayDefinition.Definition.Id, StringComparison.Ordinal)
            || HasSelectedHeaderChrome(definition.Id, settings, sessionKind)
            || gridRowCounts.Any(rowCount => rowCount > 0)
            || metricRowCounts.Where(rowCount => rowCount > 0).Sum() < 10)
        {
            return size;
        }

        var fullChromeOffSize = ApplyChromeHeight(
            definition,
            settings,
            sessionKind,
            new Size(size.Width, SimpleTelemetryDefaultHeightForSession(definition, sessionKind)));
        return size.Height >= fullChromeOffSize.Height
            ? size
            : new Size(size.Width, fullChromeOffSize.Height);
    }

    private static int TableOverlayWidth(
        OverlayDefinition definition,
        OverlaySettings settings,
        OverlayContentDefinition contentDefinition,
        OverlaySessionKind? sessionKind)
    {
        var visibleWidth = OverlayContentColumnSettings
            .VisibleColumnsFor(settings, contentDefinition, sessionKind)
            .Sum(column => column.Width);
        if (visibleWidth <= 0)
        {
            return definition.DefaultWidth;
        }

        return visibleWidth + contentDefinition.BrowserWidthPadding;
    }

    private static int TableOverlayHeight(OverlayDefinition definition, OverlaySettings settings)
    {
        if (string.Equals(definition.Id, RelativeOverlayDefinition.Definition.Id, StringComparison.Ordinal))
        {
            return Math.Max(MinimumRelativeHeight, RelativeOverlayHeightForRows(RelativeVisibleRows(settings)));
        }

        return definition.DefaultHeight;
    }

    private static int RelativeOverlayHeightForRows(int visibleRows)
    {
        var rowCount = Math.Max(1, visibleRows);
        return RelativeTableChromeHeight
            + rowCount * RelativeTableRowHeight
            + Math.Max(0, rowCount - 1) * RelativeTableRowGap;
    }

    private static int SimpleTelemetryWidth(
        OverlayDefinition definition,
        OverlaySettings settings,
        OverlayContentDefinition contentDefinition,
        OverlaySessionKind? sessionKind)
    {
        if (string.Equals(definition.Id, PitServiceOverlayDefinition.Definition.Id, StringComparison.Ordinal))
        {
            return PitServiceWidth(definition, settings, contentDefinition, sessionKind);
        }

        var enabledCount = EnabledBlockCount(settings, contentDefinition, sessionKind);
        var defaultCount = DefaultEnabledBlockCount(contentDefinition);
        if (enabledCount <= 0 || enabledCount >= defaultCount)
        {
            return definition.DefaultWidth;
        }

        return Math.Min(definition.DefaultWidth, MetricGeometry.MinimumSimpleTelemetryWidth);
    }

    private static int SimpleTelemetryHeight(
        OverlayDefinition definition,
        OverlaySettings settings,
        OverlayContentDefinition contentDefinition,
        OverlaySessionKind? sessionKind)
    {
        if (string.Equals(definition.Id, PitServiceOverlayDefinition.Definition.Id, StringComparison.Ordinal))
        {
            return PitServiceHeight(definition, settings, contentDefinition, sessionKind);
        }

        var enabledCount = EnabledSimpleTelemetryBlockCount(definition, settings, contentDefinition, sessionKind);
        var defaultCount = Math.Max(1, DefaultEnabledSimpleTelemetryBlockCount(definition, contentDefinition, sessionKind));
        var sessionDefaultHeight = SimpleTelemetryDefaultHeightForSession(definition, sessionKind);
        if (enabledCount <= 0 || enabledCount >= defaultCount)
        {
            return sessionDefaultHeight;
        }

        if (defaultCount == 1)
        {
            return Math.Max(MetricGeometry.MinimumSimpleTelemetryHeight, sessionDefaultHeight);
        }

        var progress = (enabledCount - 1) / (double)(defaultCount - 1);
        var height = MetricGeometry.MinimumSimpleTelemetryHeight
            + (int)Math.Round((sessionDefaultHeight - MetricGeometry.MinimumSimpleTelemetryHeight) * progress);
        return Math.Clamp(height, MetricGeometry.MinimumSimpleTelemetryHeight, sessionDefaultHeight);
    }

    private static int SimpleTelemetryRenderedWidth(
        OverlayDefinition definition,
        OverlaySettings settings,
        OverlayContentDefinition contentDefinition,
        OverlaySessionKind? sessionKind,
        bool hasGridSections)
    {
        if (string.Equals(definition.Id, PitServiceOverlayDefinition.Definition.Id, StringComparison.Ordinal))
        {
            return hasGridSections
                ? definition.DefaultWidth
                : Math.Min(definition.DefaultWidth, MetricGeometry.PitServiceMetricOnlyWidth);
        }

        return SimpleTelemetryWidth(definition, settings, contentDefinition, sessionKind);
    }

    private static int SimpleTelemetryRenderedHeight(
        IReadOnlyList<int> metricRowCounts,
        IReadOnlyList<int> gridRowCounts,
        int maximumHeight)
    {
        var metricHeight = MetricSectionsHeight(metricRowCounts);
        var gridHeight = GridSectionsHeight(gridRowCounts);
        var contentHeight = metricHeight
            + (metricHeight > 0 && gridHeight > 0 ? (int)Math.Round(MetricGeometry.MetricGridGap) : 0)
            + gridHeight;
        var height = contentHeight + (int)Math.Round(MetricGeometry.PitServiceContentChromeHeight);
        return Math.Clamp(height, MetricGeometry.MinimumSimpleTelemetryHeight, maximumHeight);
    }

    private static int MetricSectionsHeight(IReadOnlyList<int> rowCounts)
    {
        var sectionHeights = new List<int>();
        foreach (var rowCount in rowCounts)
        {
            AddMetricSectionHeight(sectionHeights, rowCount, segmentedRows: rowCount);
        }

        return sectionHeights.Sum()
            + (int)Math.Round(Math.Max(0, sectionHeights.Count - 1) * MetricGeometry.PitServiceSectionGap);
    }

    private static int GridSectionsHeight(IReadOnlyList<int> rowCounts)
    {
        var sectionHeights = rowCounts
            .Where(rowCount => rowCount > 0)
            .Select(GridSectionHeight)
            .ToArray();
        return sectionHeights.Sum()
            + (int)Math.Round(Math.Max(0, sectionHeights.Length - 1) * MetricGeometry.MetricGridGap);
    }

    private static int GridSectionHeight(int rowCount)
    {
        return rowCount <= 0
            ? 0
            : (int)Math.Round(
                MetricGeometry.MetricGridHeaderHeight
                + MetricGeometry.MetricGridHeaderBottomGap
                + rowCount * MetricGeometry.MetricGridRowHeight
                + Math.Max(0, rowCount - 1) * MetricGeometry.MetricGridRowGap);
    }

    private static int EnabledSimpleTelemetryBlockCount(
        OverlayDefinition definition,
        OverlaySettings settings,
        OverlayContentDefinition contentDefinition,
        OverlaySessionKind? sessionKind)
    {
        return SimpleTelemetryRelevantBlocks(definition, contentDefinition, sessionKind)
            .Count(block => OverlayContentColumnSettings.BlockEnabled(settings, block, sessionKind));
    }

    private static int DefaultEnabledSimpleTelemetryBlockCount(
        OverlayDefinition definition,
        OverlayContentDefinition contentDefinition,
        OverlaySessionKind? sessionKind)
    {
        return SimpleTelemetryRelevantBlocks(definition, contentDefinition, sessionKind)
            .Count(block => block.DefaultEnabled);
    }

    private static IReadOnlyList<OverlayContentBlockDefinition> SimpleTelemetryRelevantBlocks(
        OverlayDefinition definition,
        OverlayContentDefinition contentDefinition,
        OverlaySessionKind? sessionKind)
    {
        if (contentDefinition.Blocks is not { Count: > 0 } blocks)
        {
            return [];
        }

        if (OverlayAvailabilityEvaluator.NormalizeSessionKind(sessionKind) is OverlaySessionKind.Practice or OverlaySessionKind.Qualifying)
        {
            if (string.Equals(definition.Id, SessionWeatherOverlayDefinition.Definition.Id, StringComparison.Ordinal))
            {
                return blocks
                    .Where(block => !string.Equals(block.Id, OverlayContentColumnSettings.SessionWeatherLapsRemainingBlockId, StringComparison.Ordinal)
                        && !string.Equals(block.Id, OverlayContentColumnSettings.SessionWeatherLapsTotalBlockId, StringComparison.Ordinal))
                    .ToArray();
            }

            if (string.Equals(definition.Id, PitServiceOverlayDefinition.Definition.Id, StringComparison.Ordinal))
            {
                return blocks
                    .Where(block => !string.Equals(block.Id, OverlayContentColumnSettings.PitServiceSessionLapsBlockId, StringComparison.Ordinal))
                    .ToArray();
            }
        }

        return blocks;
    }

    private static int SimpleTelemetryDefaultHeightForSession(
        OverlayDefinition definition,
        OverlaySessionKind? sessionKind)
    {
        if (string.Equals(definition.Id, SessionWeatherOverlayDefinition.Definition.Id, StringComparison.Ordinal)
            && OverlayAvailabilityEvaluator.NormalizeSessionKind(sessionKind) is OverlaySessionKind.Practice or OverlaySessionKind.Qualifying)
        {
            return Math.Max(
                MetricGeometry.MinimumSimpleTelemetryHeight,
                definition.DefaultHeight - MetricGeometry.NonRaceSimpleTelemetryHeightReduction);
        }

        return definition.DefaultHeight;
    }

    private static int PitServiceHeight(
        OverlayDefinition definition,
        OverlaySettings settings,
        OverlayContentDefinition contentDefinition,
        OverlaySessionKind? sessionKind)
    {
        var enabled = EnabledBlockIds(settings, contentDefinition, sessionKind);
        if (enabled.Count == 0)
        {
            return MetricGeometry.MinimumSimpleTelemetryHeight;
        }

        var sectionHeights = new List<int>();
        var sessionRows = PitServiceSessionRowCount(enabled, sessionKind);
        AddMetricSectionHeight(sectionHeights, sessionRows, segmentedRows: sessionRows);
        var signalRows = PitServiceSignalRowCount(enabled);
        AddMetricSectionHeight(sectionHeights, signalRows, segmentedRows: signalRows);
        AddMetricSectionHeight(
            sectionHeights,
            PitServiceServiceRowCount(enabled),
            segmentedRows: PitServiceServiceRowCount(enabled));

        var metricHeight = sectionHeights.Sum()
            + (int)Math.Round(Math.Max(0, sectionHeights.Count - 1) * MetricGeometry.PitServiceSectionGap);
        var tireRows = PitServiceTireRowCount(enabled);
        var gridHeight = tireRows <= 0
            ? 0
            : (int)Math.Round(
                MetricGeometry.MetricGridHeaderHeight
                + MetricGeometry.MetricGridHeaderBottomGap
                + tireRows * MetricGeometry.MetricGridRowHeight
                + Math.Max(0, tireRows - 1) * MetricGeometry.MetricGridRowGap);
        var contentHeight = metricHeight
            + (metricHeight > 0 && gridHeight > 0 ? (int)Math.Round(MetricGeometry.MetricGridGap) : 0)
            + gridHeight;
        var height = contentHeight + (int)Math.Round(MetricGeometry.PitServiceContentChromeHeight);
        var sessionDefaultHeight = SimpleTelemetryDefaultHeightForSession(definition, sessionKind);
        return Math.Clamp(height, MetricGeometry.MinimumSimpleTelemetryHeight, sessionDefaultHeight);
    }

    private static int PitServiceWidth(
        OverlayDefinition definition,
        OverlaySettings settings,
        OverlayContentDefinition contentDefinition,
        OverlaySessionKind? sessionKind)
    {
        var enabled = EnabledBlockIds(settings, contentDefinition, sessionKind);
        return PitServiceTireRowCount(enabled) > 0
            ? definition.DefaultWidth
            : Math.Min(definition.DefaultWidth, MetricGeometry.PitServiceMetricOnlyWidth);
    }

    private static HashSet<string> EnabledBlockIds(
        OverlaySettings settings,
        OverlayContentDefinition contentDefinition,
        OverlaySessionKind? sessionKind)
    {
        var blocks = contentDefinition.Blocks ?? [];
        return blocks
            .Where(block => OverlayContentColumnSettings.BlockEnabled(settings, block, sessionKind))
            .Select(block => block.Id)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static void AddMetricSectionHeight(List<int> sectionHeights, int rowCount, int segmentedRows)
    {
        if (rowCount <= 0)
        {
            return;
        }

        var plainRows = Math.Max(0, rowCount - segmentedRows);
        sectionHeights.Add(
            (int)Math.Round(MetricGeometry.SectionTitleHeight + MetricGeometry.SectionTitleBottomGap)
            + (int)Math.Round(segmentedRows * MetricGeometry.SegmentedRowHeight)
            + (int)Math.Round(plainRows * MetricGeometry.PlainRowHeight)
            + (int)Math.Round(Math.Max(0, rowCount - 1) * MetricGeometry.RowGap));
    }

    private static int PitServiceSessionRowCount(IReadOnlySet<string> enabled, OverlaySessionKind? sessionKind)
    {
        var hasTime = enabled.Contains(OverlayContentColumnSettings.PitServiceSessionTimeBlockId);
        var hasLaps = enabled.Contains(OverlayContentColumnSettings.PitServiceSessionLapsBlockId)
            && OverlayAvailabilityEvaluator.NormalizeSessionKind(sessionKind) == OverlaySessionKind.Race;
        return hasTime || hasLaps ? 1 : 0;
    }

    private static int PitServiceSignalRowCount(IReadOnlySet<string> enabled)
    {
        var rows = 0;
        if (enabled.Contains(OverlayContentColumnSettings.PitServiceReleaseBlockId))
        {
            rows++;
        }

        if (enabled.Contains(OverlayContentColumnSettings.PitServicePitStatusBlockId))
        {
            rows++;
        }

        return rows;
    }

    private static int PitServiceServiceRowCount(IReadOnlySet<string> enabled)
    {
        var rows = 0;
        if (enabled.Contains(OverlayContentColumnSettings.PitServiceFuelRequestedBlockId)
            || enabled.Contains(OverlayContentColumnSettings.PitServiceFuelSelectedBlockId))
        {
            rows++;
        }

        if (enabled.Contains(OverlayContentColumnSettings.PitServiceTearoffRequestedBlockId))
        {
            rows++;
        }

        if (enabled.Contains(OverlayContentColumnSettings.PitServiceRepairRequiredBlockId)
            || enabled.Contains(OverlayContentColumnSettings.PitServiceRepairOptionalBlockId))
        {
            rows++;
        }

        if (enabled.Contains(OverlayContentColumnSettings.PitServiceFastRepairSelectedBlockId)
            || enabled.Contains(OverlayContentColumnSettings.PitServiceFastRepairAvailableBlockId))
        {
            rows++;
        }

        return rows;
    }

    private static int PitServiceTireRowCount(IReadOnlySet<string> enabled)
    {
        var tireBlocks = new[]
        {
            OverlayContentColumnSettings.PitServiceTireCompoundBlockId,
            OverlayContentColumnSettings.PitServiceTireChangeBlockId,
            OverlayContentColumnSettings.PitServiceTireSetLimitBlockId,
            OverlayContentColumnSettings.PitServiceTireSetsAvailableBlockId,
            OverlayContentColumnSettings.PitServiceTireSetsUsedBlockId,
            OverlayContentColumnSettings.PitServiceTirePressureBlockId,
            OverlayContentColumnSettings.PitServiceTireTemperatureBlockId,
            OverlayContentColumnSettings.PitServiceTireWearBlockId,
            OverlayContentColumnSettings.PitServiceTireDistanceBlockId
        };
        return tireBlocks.Count(enabled.Contains);
    }

    private static int FuelCalculatorHeight(
        OverlayDefinition definition,
        OverlaySettings settings,
        OverlayContentDefinition contentDefinition,
        OverlaySessionKind? sessionKind)
    {
        var normalized = OverlayAvailabilityEvaluator.NormalizeSessionKind(sessionKind);
        if (normalized is OverlaySessionKind.Practice or OverlaySessionKind.Qualifying)
        {
            var enabledRows = EnabledFuelBlockCount(settings, contentDefinition, sessionKind);
            return enabledRows <= 0
                ? MetricGeometry.MinimumFuelCalculatorHeight
                : FuelCalculatorHeightForContent(enabledRows, enabledRows);
        }

        var enabled = FuelRelevantBlocks(contentDefinition, sessionKind)
            .Where(block => OverlayContentColumnSettings.BlockEnabled(settings, block, sessionKind))
            .Select(block => block.Id)
            .ToHashSet(StringComparer.Ordinal);
        if (enabled.Count == 0)
        {
            return MetricGeometry.MinimumFuelCalculatorHeight;
        }

        var raceRows = 0;
        if (enabled.Contains(OverlayContentColumnSettings.FuelCalculatorRacePlanBlockId))
        {
            raceRows++;
        }

        if (enabled.Contains(OverlayContentColumnSettings.FuelCalculatorRaceFuelBlockId))
        {
            raceRows++;
        }

        var stintRows = enabled.Contains(OverlayContentColumnSettings.FuelCalculatorStintTargetsBlockId) ? 3 : 0;
        var sectionCount = (raceRows > 0 ? 1 : 0) + (stintRows > 0 ? 1 : 0);
        return FuelCalculatorHeightForContent(raceRows + stintRows, sectionCount);
    }

    private static Size GapToLeaderSize(
        OverlayDefinition definition,
        OverlaySettings settings,
        OverlaySessionKind? sessionKind)
    {
        var hasGraph = GapGraphEnabled(settings, sessionKind) && GapWindowEnabled(settings);
        var trendCount = EnabledGapTrendBlockCount(settings, sessionKind);
        if (!hasGraph && trendCount <= 0)
        {
            return new Size(MinimumGapToLeaderWidth, GapToLeaderMinimumHeight);
        }

        if (!hasGraph)
        {
            return new Size(GapToLeaderTrendOnlyWidth, definition.DefaultHeight);
        }

        if (trendCount <= 0)
        {
            return new Size(GapToLeaderGraphOnlyWidth, definition.DefaultHeight);
        }

        return new Size(definition.DefaultWidth, definition.DefaultHeight);
    }

    private static bool GapWindowEnabled(OverlaySettings settings)
    {
        return settings.GetIntegerOption(OverlayOptionKeys.GapCarsAhead, defaultValue: 5, minimum: 0, maximum: 12) > 0
            || settings.GetIntegerOption(OverlayOptionKeys.GapCarsBehind, defaultValue: 5, minimum: 0, maximum: 12) > 0;
    }

    private static bool GapGraphEnabled(OverlaySettings settings, OverlaySessionKind? sessionKind)
    {
        return OverlayContentColumnSettings.ContentEnabledForSession(
            settings,
            OverlayOptionKeys.GapGraphEnabled,
            defaultEnabled: true,
            sessionKind);
    }

    private static int EnabledGapTrendBlockCount(OverlaySettings settings, OverlaySessionKind? sessionKind)
    {
        return (OverlayContentColumnSettings.GapToLeader.Blocks ?? [])
            .Where(block => !string.Equals(block.EnabledOptionKey, OverlayOptionKeys.GapGraphEnabled, StringComparison.Ordinal))
            .Count(block => OverlayContentColumnSettings.BlockEnabled(settings, block, sessionKind));
    }

    private static int EnabledFuelBlockCount(
        OverlaySettings settings,
        OverlayContentDefinition definition,
        OverlaySessionKind? sessionKind)
    {
        return FuelRelevantBlocks(definition, sessionKind)
            .Count(block => OverlayContentColumnSettings.BlockEnabled(settings, block, sessionKind));
    }

    private static IReadOnlyList<OverlayContentBlockDefinition> FuelRelevantBlocks(
        OverlayContentDefinition definition,
        OverlaySessionKind? sessionKind)
    {
        if (definition.Blocks is not { Count: > 0 } blocks)
        {
            return [];
        }

        var normalized = OverlayAvailabilityEvaluator.NormalizeSessionKind(sessionKind);
        var ids = normalized is OverlaySessionKind.Practice or OverlaySessionKind.Qualifying
            ? new[]
            {
                OverlayContentColumnSettings.FuelCalculatorRangeBlockId,
                OverlayContentColumnSettings.FuelCalculatorUsageBlockId
            }
            : new[]
            {
                OverlayContentColumnSettings.FuelCalculatorRacePlanBlockId,
                OverlayContentColumnSettings.FuelCalculatorRaceFuelBlockId,
                OverlayContentColumnSettings.FuelCalculatorStintTargetsBlockId
            };

        return blocks
            .Where(block => ids.Contains(block.Id, StringComparer.Ordinal))
            .ToArray();
    }

    private static bool UsesSimpleTelemetryContentSizing(string overlayId)
    {
        return string.Equals(overlayId, SessionWeatherOverlayDefinition.Definition.Id, StringComparison.Ordinal)
            || string.Equals(overlayId, PitServiceOverlayDefinition.Definition.Id, StringComparison.Ordinal);
    }

    private static Size ApplyChromeHeight(
        OverlayDefinition definition,
        OverlaySettings settings,
        OverlaySessionKind? sessionKind,
        Size baseSize)
    {
        if (!UsesChromeReservedHeight(definition.Id))
        {
            return baseSize;
        }

        var height = baseSize.Height;
        if (!HasSelectedHeaderChrome(definition.Id, settings, sessionKind))
        {
            height -= HeaderChromeCollapseHeight(definition.Id);
        }

        if (HasSelectedFooterChrome(definition.Id, settings, sessionKind))
        {
            height += MetricGeometry.FooterChromeHeight - MetricGeometry.CollapsedFooterReserveHeight;
        }

        return new Size(
            baseSize.Width,
            Math.Max(MetricGeometry.MinimumChromeAdjustedHeight, height));
    }

    private static int HeaderChromeCollapseHeight(string overlayId)
    {
        return string.Equals(overlayId, RelativeOverlayDefinition.Definition.Id, StringComparison.Ordinal)
            ? RelativeHeaderChromeCollapseHeight
            : MetricGeometry.HeaderChromeHeight;
    }

    private static bool UsesChromeReservedHeight(string overlayId)
    {
        return string.Equals(overlayId, StandingsOverlayDefinition.Definition.Id, StringComparison.Ordinal)
            || string.Equals(overlayId, RelativeOverlayDefinition.Definition.Id, StringComparison.Ordinal)
            || string.Equals(overlayId, FuelCalculatorOverlayDefinition.Definition.Id, StringComparison.Ordinal)
            || string.Equals(overlayId, GapToLeaderOverlayDefinition.Definition.Id, StringComparison.Ordinal)
            || string.Equals(overlayId, SessionWeatherOverlayDefinition.Definition.Id, StringComparison.Ordinal)
            || string.Equals(overlayId, PitServiceOverlayDefinition.Definition.Id, StringComparison.Ordinal);
    }

    private static bool HasSelectedHeaderChrome(
        string overlayId,
        OverlaySettings settings,
        OverlaySessionKind? sessionKind)
    {
        if (string.Equals(overlayId, InputStateOverlayDefinition.Definition.Id, StringComparison.Ordinal)
            || string.Equals(overlayId, StreamChatOverlayDefinition.Definition.Id, StringComparison.Ordinal))
        {
            return false;
        }

        return OverlayChromeSettings.ShowHeaderTimeRemainingForSession(settings, sessionKind);
    }

    private static bool HasSelectedFooterChrome(
        string overlayId,
        OverlaySettings settings,
        OverlaySessionKind? sessionKind)
    {
        if (sessionKind is null
            || string.Equals(overlayId, InputStateOverlayDefinition.Definition.Id, StringComparison.Ordinal)
            || string.Equals(overlayId, StreamChatOverlayDefinition.Definition.Id, StringComparison.Ordinal))
        {
            return false;
        }

        return OverlayChromeSettings.ShowFooterSourceForSession(settings, sessionKind);
    }
}
