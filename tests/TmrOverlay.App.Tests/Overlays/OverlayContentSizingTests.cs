using System.Drawing;
using TmrOverlay.App.Overlays;
using TmrOverlay.App.Overlays.BrowserSources;
using TmrOverlay.App.Overlays.Content;
using TmrOverlay.App.Overlays.GapToLeader;
using TmrOverlay.App.Overlays.PitService;
using TmrOverlay.Core.Overlays;
using TmrOverlay.Core.Settings;
using Xunit;
using MetricRows = TmrOverlay.App.Overlays.OverlayGeometryContractValues.MetricRows;
using OverlaySizes = TmrOverlay.App.Overlays.OverlayGeometryContractValues.OverlaySizes;

namespace TmrOverlay.App.Tests.Overlays;

public sealed class OverlayContentSizingTests
{
    [Fact]
    public void GapToLeaderSizingUsesContractWidthsForGraphTrendAndDisabledContent()
    {
        var full = NewOverlay(
            GapToLeaderOverlayDefinition.Definition.Id,
            GapToLeaderOverlayDefinition.Definition.DefaultWidth,
            GapToLeaderOverlayDefinition.Definition.DefaultHeight);
        Assert.Equal(
            new Size(OverlaySizes.GapToLeaderWidth, OverlaySizes.GapToLeaderHeight),
            BrowserOverlayRecommendedSize.For(GapToLeaderOverlayDefinition.Definition, full, OverlaySessionKind.Race));
        Assert.True(OverlayContentSizing.HasRenderableContent(
            GapToLeaderOverlayDefinition.Definition,
            full,
            OverlaySessionKind.Race));

        var graphOnly = NewOverlay(
            GapToLeaderOverlayDefinition.Definition.Id,
            GapToLeaderOverlayDefinition.Definition.DefaultWidth,
            GapToLeaderOverlayDefinition.Definition.DefaultHeight);
        SetGapTrendBlocks(graphOnly, enabled: false);
        Assert.Equal(
            new Size(OverlaySizes.GapToLeaderGraphOnlyWidth, OverlaySizes.GapToLeaderHeight),
            BrowserOverlayRecommendedSize.For(GapToLeaderOverlayDefinition.Definition, graphOnly, OverlaySessionKind.Race));
        Assert.True(OverlayContentSizing.HasRenderableContent(
            GapToLeaderOverlayDefinition.Definition,
            graphOnly,
            OverlaySessionKind.Race));

        var trendOnly = NewOverlay(
            GapToLeaderOverlayDefinition.Definition.Id,
            GapToLeaderOverlayDefinition.Definition.DefaultWidth,
            GapToLeaderOverlayDefinition.Definition.DefaultHeight);
        trendOnly.SetBooleanOption(OverlayOptionKeys.GapGraphEnabled, false);
        Assert.Equal(
            new Size(OverlaySizes.GapToLeaderTrendOnlyWidth, OverlaySizes.GapToLeaderHeight),
            BrowserOverlayRecommendedSize.For(GapToLeaderOverlayDefinition.Definition, trendOnly, OverlaySessionKind.Race));
        Assert.True(OverlayContentSizing.HasRenderableContent(
            GapToLeaderOverlayDefinition.Definition,
            trendOnly,
            OverlaySessionKind.Race));

        var empty = NewOverlay(
            GapToLeaderOverlayDefinition.Definition.Id,
            GapToLeaderOverlayDefinition.Definition.DefaultWidth,
            GapToLeaderOverlayDefinition.Definition.DefaultHeight);
        empty.SetBooleanOption(OverlayOptionKeys.GapGraphEnabled, false);
        SetGapTrendBlocks(empty, enabled: false);
        Assert.Equal(
            new Size(OverlaySizes.GapToLeaderMinimumWidth, OverlaySizes.GapToLeaderMinimumHeight),
            BrowserOverlayRecommendedSize.For(GapToLeaderOverlayDefinition.Definition, empty, OverlaySessionKind.Race));
        Assert.False(OverlayContentSizing.HasRenderableContent(
            GapToLeaderOverlayDefinition.Definition,
            empty,
            OverlaySessionKind.Race));
    }

    [Fact]
    public void GapToLeaderSizingCollapsesHeaderOnlyForSelectedSession()
    {
        var gap = NewOverlay(
            GapToLeaderOverlayDefinition.Definition.Id,
            GapToLeaderOverlayDefinition.Definition.DefaultWidth,
            GapToLeaderOverlayDefinition.Definition.DefaultHeight);

        gap.SetBooleanOption(OverlayOptionKeys.ChromeHeaderTimeRemainingPractice, false);

        Assert.Equal(
            new Size(OverlaySizes.GapToLeaderWidth, OverlaySizes.GapToLeaderHeight),
            BrowserOverlayRecommendedSize.For(GapToLeaderOverlayDefinition.Definition, gap, OverlaySessionKind.Race));
        Assert.Equal(
            new Size(OverlaySizes.GapToLeaderWidth, OverlaySizes.GapToLeaderHeight - MetricRows.HeaderChromeHeight),
            BrowserOverlayRecommendedSize.For(GapToLeaderOverlayDefinition.Definition, gap, OverlaySessionKind.Practice));
    }

    [Fact]
    public void PitServiceSizingUsesMetricOnlyWidthUntilTireGridIsEnabled()
    {
        var metricOnly = NewOverlay(
            PitServiceOverlayDefinition.Definition.Id,
            PitServiceOverlayDefinition.Definition.DefaultWidth,
            PitServiceOverlayDefinition.Definition.DefaultHeight);
        EnableOnlyBlocks(
            metricOnly,
            OverlayContentColumnSettings.PitService,
            OverlayContentColumnSettings.PitServiceReleaseBlockId);

        Assert.Equal(
            new Size(MetricRows.PitServiceMetricOnlyWidth, MetricRows.MinimumSimpleTelemetryHeight),
            BrowserOverlayRecommendedSize.For(PitServiceOverlayDefinition.Definition, metricOnly, OverlaySessionKind.Race));

        var tireGrid = NewOverlay(
            PitServiceOverlayDefinition.Definition.Id,
            PitServiceOverlayDefinition.Definition.DefaultWidth,
            PitServiceOverlayDefinition.Definition.DefaultHeight);
        EnableOnlyBlocks(
            tireGrid,
            OverlayContentColumnSettings.PitService,
            OverlayContentColumnSettings.PitServiceReleaseBlockId,
            OverlayContentColumnSettings.PitServiceTirePressureBlockId);

        Assert.Equal(
            new Size(
                PitServiceOverlayDefinition.Definition.DefaultWidth,
                ExpectedPitServiceHeight(signalRows: 1, tireRows: 1)),
            BrowserOverlayRecommendedSize.For(PitServiceOverlayDefinition.Definition, tireGrid, OverlaySessionKind.Race));
    }

    private static OverlaySettings NewOverlay(string id, int defaultWidth, int defaultHeight)
    {
        return new ApplicationSettings().GetOrAddOverlay(id, defaultWidth, defaultHeight);
    }

    private static void SetGapTrendBlocks(OverlaySettings settings, bool enabled)
    {
        foreach (var block in OverlayContentColumnSettings.GapToLeader.Blocks ?? [])
        {
            if (!string.Equals(block.EnabledOptionKey, OverlayOptionKeys.GapGraphEnabled, StringComparison.Ordinal))
            {
                settings.SetBooleanOption(block.EnabledOptionKey, enabled);
            }
        }
    }

    private static void EnableOnlyBlocks(
        OverlaySettings settings,
        OverlayContentDefinition definition,
        params string[] enabledBlockIds)
    {
        var enabled = enabledBlockIds.ToHashSet(StringComparer.Ordinal);
        foreach (var block in definition.Blocks ?? [])
        {
            settings.SetBooleanOption(block.EnabledOptionKey, enabled.Contains(block.Id));
        }
    }

    private static int ExpectedPitServiceHeight(int signalRows, int tireRows)
    {
        var metricHeight = ExpectedMetricSectionHeight(rowCount: signalRows, segmentedRows: signalRows);
        var gridHeight = tireRows <= 0
            ? 0
            : (int)Math.Round(
                MetricRows.MetricGridHeaderHeight
                + MetricRows.MetricGridHeaderBottomGap
                + tireRows * MetricRows.MetricGridRowHeight
                + Math.Max(0, tireRows - 1) * MetricRows.MetricGridRowGap);
        var contentHeight = metricHeight
            + (metricHeight > 0 && gridHeight > 0 ? (int)Math.Round(MetricRows.MetricGridGap) : 0)
            + gridHeight;
        var height = contentHeight + (int)Math.Round(MetricRows.PitServiceContentChromeHeight);
        return Math.Clamp(
            height,
            MetricRows.MinimumSimpleTelemetryHeight,
            PitServiceOverlayDefinition.Definition.DefaultHeight);
    }

    private static int ExpectedMetricSectionHeight(int rowCount, int segmentedRows)
    {
        if (rowCount <= 0)
        {
            return 0;
        }

        var plainRows = Math.Max(0, rowCount - segmentedRows);
        return (int)Math.Round(MetricRows.SectionTitleHeight + MetricRows.SectionTitleBottomGap)
            + (int)Math.Round(segmentedRows * MetricRows.SegmentedRowHeight)
            + (int)Math.Round(plainRows * MetricRows.PlainRowHeight)
            + (int)Math.Round(Math.Max(0, rowCount - 1) * MetricRows.RowGap);
    }
}
