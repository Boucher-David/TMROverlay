using System.Drawing;
using TmrOverlay.Core.History;
using TmrOverlay.App.Overlays;
using TmrOverlay.App.Overlays.BrowserSources;
using TmrOverlay.App.Overlays.Content;
using TmrOverlay.App.Overlays.GapToLeader;
using TmrOverlay.App.Overlays.PitService;
using TmrOverlay.App.Overlays.SessionWeather;
using TmrOverlay.App.Overlays.SimpleTelemetry;
using TmrOverlay.Core.Overlays;
using TmrOverlay.Core.Settings;
using TmrOverlay.Core.Telemetry.Live;
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

    [Fact]
    public void PitServiceSizingMatchesRenderedMetricAndGridSections()
    {
        var now = new DateTimeOffset(2026, 5, 25, 18, 20, 0, TimeSpan.Zero);
        var settings = NewOverlay(
            PitServiceOverlayDefinition.Definition.Id,
            PitServiceOverlayDefinition.Definition.DefaultWidth,
            PitServiceOverlayDefinition.Definition.DefaultHeight);
        var snapshot = Snapshot(now, LiveRaceModels.Empty with
        {
            Session = LiveSessionModel.Empty with
            {
                HasData = true,
                Quality = LiveModelQuality.Reliable,
                SessionType = "Test"
            },
            PitService = LivePitServiceModel.Empty with
            {
                HasData = true,
                Quality = LiveModelQuality.Reliable,
                Status = PitServiceStatusFormatter.InProgress,
                Flags = 0,
                Request = LivePitServiceRequest.Empty with
                {
                    FuelLiters = 45.5d
                },
                Tires = LivePitServiceTireState.Empty with
                {
                    CurrentCompoundShortLabel = "S",
                    LeftFrontChangeRequested = false,
                    RightFrontChangeRequested = false,
                    LeftRearChangeRequested = false,
                    RightRearChangeRequested = false
                }
            },
            TireCondition = new LiveTireConditionModel(
                HasData: true,
                Quality: LiveModelQuality.Reliable,
                Evidence: LiveSignalEvidence.Reliable("test tire condition"),
                LeftFront: TireCorner("LF"),
                RightFront: TireCorner("RF"),
                LeftRear: TireCorner("LR"),
                RightRear: TireCorner("RR"))
        });
        var viewModel = PitServiceOverlayViewModel.From(snapshot, now, "Metric", settings);

        Assert.Collection(
            viewModel.MetricSections,
            section =>
            {
                Assert.Equal("Pit Signal", section.Title);
                Assert.Equal(2, section.Rows.Count);
            },
            section =>
            {
                Assert.Equal("Service Request", section.Title);
                Assert.Equal(4, section.Rows.Count);
            });
        var grid = Assert.Single(viewModel.Sections);
        Assert.Equal("Tire Analysis", grid.Title);
        Assert.Equal(
            ["Compound", "Change", "Pressure", "Temp", "Wear"],
            grid.Rows.Select(row => row.Label).ToArray());

        var expected = ExpectedSimpleTelemetryRenderedSize(
            PitServiceOverlayDefinition.Definition.DefaultWidth,
            viewModel);

        Assert.Equal(
            expected,
            OverlayContentSizing.BaseSizeFor(PitServiceOverlayDefinition.Definition, settings, OverlaySessionKind.Practice));
        Assert.Equal(
            expected,
            BrowserOverlayRecommendedSize.For(PitServiceOverlayDefinition.Definition, settings, OverlaySessionKind.Practice));
    }

    [Fact]
    public void SessionWeatherSizingMatchesRenderedMetricSectionsWhenRowsAreOmitted()
    {
        var now = new DateTimeOffset(2026, 5, 25, 18, 25, 0, TimeSpan.Zero);
        var settings = NewOverlay(
            SessionWeatherOverlayDefinition.Definition.Id,
            SessionWeatherOverlayDefinition.Definition.DefaultWidth,
            SessionWeatherOverlayDefinition.Definition.DefaultHeight);
        var snapshot = Snapshot(now, LiveRaceModels.Empty with
        {
            Session = LiveSessionModel.Empty with
            {
                HasData = true,
                Quality = LiveModelQuality.Reliable,
                SessionType = "Practice",
                SessionTimeSeconds = 30d,
                TrackDisplayName = "Daytona",
                TrackLengthKm = 5.729d
            }
        });
        var viewModel = SessionWeatherOverlayViewModel.From(snapshot, now, "Metric", settings);

        var section = Assert.Single(viewModel.MetricSections);
        Assert.Equal("Session", section.Title);
        Assert.Equal(["Session", "Clock", "Track"], section.Rows.Select(row => row.Label).ToArray());
        Assert.Empty(viewModel.Sections);

        var expected = ExpectedSimpleTelemetryRenderedSize(
            SessionWeatherOverlayDefinition.Definition.DefaultWidth,
            viewModel);

        Assert.Equal(
            expected,
            OverlayContentSizing.BaseSizeFor(SessionWeatherOverlayDefinition.Definition, settings, OverlaySessionKind.Practice));
        Assert.Equal(
            expected,
            BrowserOverlayRecommendedSize.For(SessionWeatherOverlayDefinition.Definition, settings, OverlaySessionKind.Practice));
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
        var metricHeight = ExpectedMetricSectionsHeight([signalRows]);
        var gridHeight = tireRows <= 0
            ? 0
            : RoundToInt(
                MetricRows.MetricGridHeaderHeight
                + MetricRows.MetricGridHeaderBottomGap
                + tireRows * MetricRows.MetricGridRowHeight
                + Math.Max(0, tireRows - 1) * MetricRows.MetricGridRowGap);
        var contentHeight = metricHeight
            + (metricHeight > 0 && gridHeight > 0 ? RoundToInt(MetricRows.MetricGridGap) : 0)
            + gridHeight;
        var height = contentHeight + RoundToInt(MetricRows.PitServiceContentChromeHeight);
        return Math.Clamp(
            height,
            MetricRows.MinimumSimpleTelemetryHeight,
            PitServiceOverlayDefinition.Definition.DefaultHeight);
    }

    private static Size ExpectedSimpleTelemetryRenderedSize(
        int width,
        SimpleTelemetryOverlayViewModel viewModel)
    {
        var metricHeight = ExpectedMetricSectionsHeight(
            viewModel.MetricSections
                .Where(section => section.Rows.Count > 0)
                .Select(section => section.Rows.Count)
                .ToArray());
        var gridHeight = ExpectedGridSectionsHeight(
            viewModel.Sections
                .Where(section => section.Rows.Count > 0)
                .Select(section => section.Rows.Count)
                .ToArray());
        var contentHeight = metricHeight
            + (metricHeight > 0 && gridHeight > 0 ? RoundToInt(MetricRows.MetricGridGap) : 0)
            + gridHeight;
        var height = Math.Clamp(
            contentHeight + RoundToInt(MetricRows.PitServiceContentChromeHeight),
            MetricRows.MinimumSimpleTelemetryHeight,
            int.MaxValue);
        return new Size(width, height);
    }

    private static int ExpectedMetricSectionsHeight(IReadOnlyList<int> rowCounts)
    {
        var sectionHeights = rowCounts
            .Where(rowCount => rowCount > 0)
            .Select(rowCount => ExpectedMetricSectionHeight(rowCount: rowCount, segmentedRows: rowCount))
            .ToArray();
        return sectionHeights.Sum()
            + RoundToInt(Math.Max(0, sectionHeights.Length - 1) * MetricRows.PitServiceSectionGap);
    }

    private static int ExpectedMetricSectionHeight(int rowCount, int segmentedRows)
    {
        if (rowCount <= 0)
        {
            return 0;
        }

        var plainRows = Math.Max(0, rowCount - segmentedRows);
        return RoundToInt(MetricRows.SectionTitleHeight + MetricRows.SectionTitleBottomGap)
            + RoundToInt(segmentedRows * MetricRows.SegmentedRowHeight)
            + RoundToInt(plainRows * MetricRows.PlainRowHeight)
            + RoundToInt(Math.Max(0, rowCount - 1) * MetricRows.RowGap);
    }

    private static int ExpectedGridSectionsHeight(IReadOnlyList<int> rowCounts)
    {
        var sectionHeights = rowCounts
            .Where(rowCount => rowCount > 0)
            .Select(ExpectedGridSectionHeight)
            .ToArray();
        return sectionHeights.Sum()
            + RoundToInt(Math.Max(0, sectionHeights.Length - 1) * MetricRows.MetricGridGap);
    }

    private static int ExpectedGridSectionHeight(int rowCount)
    {
        return rowCount <= 0
            ? 0
            : RoundToInt(
                MetricRows.MetricGridHeaderHeight
                + MetricRows.MetricGridHeaderBottomGap
                + rowCount * MetricRows.MetricGridRowHeight
                + Math.Max(0, rowCount - 1) * MetricRows.MetricGridRowGap);
    }

    private static LiveTelemetrySnapshot Snapshot(DateTimeOffset now, LiveRaceModels models)
    {
        var normalizedModels = models with
        {
            DriverDirectory = models.DriverDirectory.HasData
                ? models.DriverDirectory
                : LiveDriverDirectoryModel.Empty with
                {
                    HasData = true,
                    Quality = LiveModelQuality.Reliable,
                    PlayerCarIdx = 10,
                    FocusCarIdx = 10
                },
            RaceEvents = models.RaceEvents.HasData
                ? models.RaceEvents
                : LiveRaceEventModel.Empty with
                {
                    HasData = true,
                    Quality = LiveModelQuality.Reliable,
                    IsOnTrack = true
                }
        };

        return LiveTelemetrySnapshot.Empty with
        {
            IsConnected = true,
            IsCollecting = true,
            LastUpdatedAtUtc = now,
            Sequence = 1,
            Context = HistoricalSessionContext.Empty,
            Combo = HistoricalComboIdentity.From(HistoricalSessionContext.Empty),
            Models = normalizedModels
        };
    }

    private static LiveTireCornerCondition TireCorner(string corner)
    {
        return new LiveTireCornerCondition(
            Corner: corner,
            Wear: new LiveTireAcrossTreadValues(0.91d, 0.9d, 0.89d),
            TemperatureC: new LiveTireAcrossTreadValues(78d, 80d, 79d),
            ColdPressureKpa: null,
            OdometerMeters: null,
            PitServicePressureKpa: 176d,
            BlackBoxColdPressurePa: null,
            ChangeRequested: false);
    }

    private static int RoundToInt(double value)
    {
        return (int)Math.Round(value);
    }
}
