using System.Drawing;
using System.Reflection;
using System.Runtime.CompilerServices;
using TmrOverlay.App.Overlays;
using TmrOverlay.App.Overlays.DesignV2;
using Xunit;

namespace TmrOverlay.App.Tests.Overlays;

public sealed class DesignV2TableRenderingTests
{
    [Fact]
    public void PartialTableRowsUseFadedNativeTextAndFill()
    {
        var table = new DesignV2TableBody([], []);
        var partial = new DesignV2TableRow(
            ["3", "#91", "Chaser"],
            IsReference: false,
            IsClassHeader: false,
            DesignV2Evidence.Partial,
            "#FFAA00");
        var measured = partial with { Evidence = DesignV2Evidence.Measured };

        var partialText = InvokeColor("TableTextColor", table, partial);
        var partialFill = InvokeColor("TableRowFillColor", table, partial);
        var measuredFill = InvokeColor("TableRowFillColor", table, measured);

        Assert.True(partialText.A < 255);
        Assert.NotEqual(measuredFill.ToArgb(), partialFill.ToArgb());
    }

    [Fact]
    public void GapTrendMetricsUseProportionalNativeSignalsTableColumns()
    {
        var graph = new DesignV2GraphBody(
            Points: [],
            Series: [],
            Weather: [],
            LeaderChanges: [],
            DriverChanges: [],
            PitWindows: [],
            StartSeconds: 0d,
            EndSeconds: 420d,
            MaxGapSeconds: 20d,
            LapReferenceSeconds: 84d,
            SelectedSeriesCount: 3,
            TrendMetrics:
            [
                Metric("Last"),
                Metric("5L"),
                Metric("10L"),
                Metric("Pit", "pit"),
                Metric("PLap", "pitLap"),
                Metric("Stint", "stint"),
                Metric("Tire", "tire"),
                Metric("Status", "status")
            ],
            ActiveThreat: null,
            ThreatCarIdx: null,
            MetricDeadbandSeconds: 0.25d,
            ComparisonLabel: "P5");
        var tableBounds = new RectangleF(0f, 0f, 220f, 164f);

        var rows = InvokeRows("BuildGraphMetricRows", tableBounds, graph);

        Assert.Equal(graph.TrendMetrics.Count, rows.Count);
        Assert.All(rows, row =>
        {
            Assert.Equal(3, row.Cells.Count);
            AssertInside(tableBounds, row.Bounds);
            AssertInside(row.Bounds, row.Cells[0].Bounds);
            AssertInside(row.Bounds, row.Cells[1].Bounds);
            AssertInside(row.Bounds, row.Cells[2].Bounds);
            Assert.True(Right(row.Cells[0].Bounds) < row.Cells[1].Bounds.X);
            Assert.True(Right(row.Cells[1].Bounds) < row.Cells[2].Bounds.X);
            Assert.True(Right(row.Cells[2].Bounds) <= tableBounds.Right);
            Assert.True(row.Cells[1].Bounds.X - Right(row.Cells[0].Bounds) >= 5f);
            Assert.True(row.Cells[2].Bounds.X - Right(row.Cells[1].Bounds) >= 5f);
        });

        var firstCells = rows[0].Cells;
        Assert.InRange(firstCells[0].Bounds.Width, 48f, 56f);
        Assert.InRange(firstCells[1].Bounds.Width, 64f, 72f);
        Assert.True(firstCells[2].Bounds.Width >= 70f);
        Assert.Equal("Metric", firstCells[0].ColumnLabel);
        Assert.Equal("P5", firstCells[1].ColumnLabel);
        Assert.Equal("Threat", firstCells[2].ColumnLabel);
    }

    [Fact]
    public void GapGraphOffMetricTableUsesSharedFullFrameContract()
    {
        var geometry = OverlayGeometryContracts.GapGraph;
        var graph = new DesignV2GraphBody(
            Points: [],
            Series: [],
            Weather: [],
            LeaderChanges: [],
            DriverChanges: [],
            PitWindows: [],
            StartSeconds: 0d,
            EndSeconds: 420d,
            MaxGapSeconds: 20d,
            LapReferenceSeconds: 84d,
            SelectedSeriesCount: 3,
            TrendMetrics:
            [
                Metric("Last"),
                Metric("5L"),
                Metric("10L"),
                Metric("Pit", "pit"),
                Metric("PLap", "pitLap"),
                Metric("Stint", "stint"),
                Metric("Tire", "tire"),
                Metric("Status", "status")
            ],
            ActiveThreat: null,
            ThreatCarIdx: null,
            MetricDeadbandSeconds: 0.25d,
            ComparisonLabel: "P5",
            ShowGraph: false);
        var bodyBounds = new RectangleF(0f, 0f, 654f, 276f);

        var layout = InvokeBody("BuildGraphLayout", bodyBounds, graph);
        var graphLayout = layout.Graph ?? throw new InvalidOperationException("Graph layout was not emitted.");
        var metricsTable = graphLayout.MetricsTable ?? throw new InvalidOperationException("Metrics table was not emitted.");

        Assert.Equal(bodyBounds.Width - geometry.FrameInsetX * 2f, metricsTable.Width, 3);
        Assert.Equal(bodyBounds.Height - geometry.FrameInsetY * 2f, metricsTable.Height, 3);
        Assert.Equal(graph.TrendMetrics.Count, graphLayout.MetricRows.Count);
        var expectedAvailable = metricsTable.Height - geometry.MetricsBottomPadding - geometry.MetricsRowsTopOffset;
        var expectedRowHeight = Math.Max(
            geometry.MetricsRowMinHeight,
            Math.Min(geometry.MetricsRowMaxHeight, expectedAvailable / graph.TrendMetrics.Count));
        var expectedTextHeight = Math.Max(10f, Math.Min(14f, expectedRowHeight));
        var expectedThirdRowTop = metricsTable.Y
            + geometry.MetricsRowsTopOffset
            + expectedRowHeight * 2f
            - expectedTextHeight / 2f;

        Assert.Equal(expectedThirdRowTop, graphLayout.MetricRows[2].Bounds.Y, 3);
    }

    [Fact]
    public void MetricRowsLayoutUsesSharedMetricRowsGeometryContract()
    {
        var geometry = OverlayGeometryContracts.MetricRows;
        var plain = new DesignV2MetricRow("Plain", "42", DesignV2Evidence.Measured);
        var segmented = new DesignV2MetricRow("Segmented", "A | B", DesignV2Evidence.Measured)
        {
            Segments =
            [
                new DesignV2MetricSegment("Left", "A", DesignV2Evidence.Measured),
                new DesignV2MetricSegment("Right", "B", DesignV2Evidence.Modeled)
            ]
        };
        var directional = new DesignV2MetricRow("Wind", "NW", DesignV2Evidence.Measured)
        {
            Segments =
            [
                new DesignV2MetricSegment("Facing", "NW", DesignV2Evidence.Measured, RotationDegrees: 315d)
            ]
        };
        var tireGrid = new DesignV2MetricGridSection(
            "Tires",
            ["Info", "FL", "FR"],
            [
                new DesignV2MetricGridRow(
                    "Pressure",
                    [
                        new DesignV2MetricGridCell("27.5", DesignV2Evidence.Measured),
                        new DesignV2MetricGridCell("27.7", DesignV2Evidence.Measured)
                    ],
                    DesignV2Evidence.Measured)
            ]);
        var metrics = new DesignV2MetricRowsBody(
            [],
            [new DesignV2MetricSection("Session", [plain, segmented, directional])],
            [tireGrid]);
        var bounds = new RectangleF(10f, 20f, geometry.MetricListMinimumWidth, 360f);

        var layout = InvokeInstanceBody("BuildMetricRowsLayout", bounds, metrics);

        Assert.Equal(3, layout.MetricRows.Count);
        Assert.Single(layout.MetricGrids);
        var title = layout.MetricRows[0].SectionTitleBounds
            ?? throw new InvalidOperationException("Metric section title bounds were not emitted.");
        Assert.Equal(bounds.Left + geometry.SectionTitleInsetX, title.X, 3);
        Assert.Equal(bounds.Top, title.Y, 3);
        Assert.Equal(bounds.Width - geometry.SectionTitleInsetX * 2f, title.Width, 3);
        Assert.Equal(geometry.SectionTitleHeight, title.Height, 3);

        var firstRow = layout.MetricRows[0];
        var secondRow = layout.MetricRows[1];
        var thirdRow = layout.MetricRows[2];
        var expectedFirstRowTop = bounds.Top + geometry.SectionTitleHeight + geometry.SectionTitleBottomGap;
        Assert.Equal(expectedFirstRowTop, firstRow.Bounds.Y, 3);
        Assert.Equal(geometry.PlainRowHeight, firstRow.Bounds.Height, 3);
        Assert.Equal(expectedFirstRowTop + geometry.PlainRowHeight + geometry.RowGap, secondRow.Bounds.Y, 3);
        Assert.Equal(geometry.SegmentedRowHeight, secondRow.Bounds.Height, 3);
        Assert.Equal(secondRow.Bounds.Y + geometry.SegmentedRowHeight + geometry.RowGap, thirdRow.Bounds.Y, 3);
        Assert.Equal(geometry.DirectionalRowHeight, thirdRow.Bounds.Height, 3);

        Assert.Equal(firstRow.Bounds.X + geometry.LabelPaddingLeft, firstRow.LabelBounds.X, 3);
        Assert.Equal(firstRow.Bounds.Y + geometry.CellVerticalPadding, firstRow.LabelBounds.Y, 3);
        Assert.Equal(geometry.LabelColumnWidth - geometry.LabelPaddingLeft - geometry.LabelPaddingRight, firstRow.LabelBounds.Width, 3);
        Assert.Equal(
            firstRow.Bounds.X + geometry.RowBorderWidth + geometry.LabelColumnWidth + geometry.ValueDividerWidth + geometry.ValuePaddingLeft,
            firstRow.ValueBounds.X,
            3);
        Assert.Equal(
            firstRow.Bounds.Width
                - geometry.RowBorderWidth * 2f
                - geometry.LabelColumnWidth
                - geometry.ValueDividerWidth
                - geometry.ValuePaddingLeft
                - geometry.ValuePaddingRight,
            firstRow.ValueBounds.Width,
            3);
        Assert.Equal(2, secondRow.Segments.Count);
        Assert.Equal(geometry.ValueSegmentGap, secondRow.Segments[1].Bounds.X - Right(secondRow.Segments[0].Bounds), 3);
        Assert.Equal(315d, thirdRow.Segments[0].RotationDegrees.GetValueOrDefault());

        var grid = layout.MetricGrids[0];
        Assert.Equal(geometry.MetricGridHeaderHeight, grid.Headers[0].Bounds.Height, 3);
        Assert.Equal(geometry.MetricGridRowHeight, grid.Rows[0].Bounds.Height, 3);
        Assert.Equal(geometry.MetricGridCellHeight, grid.Rows[0].Cells[0].Bounds.Height, 3);
    }

    [Fact]
    public void MetricRowsLayoutStartsGridAtBodyTopWhenNoMetricRowsRender()
    {
        var geometry = OverlayGeometryContracts.MetricRows;
        var tireGrid = new DesignV2MetricGridSection(
            "Tires",
            ["Info", "FL", "FR"],
            [
                new DesignV2MetricGridRow(
                    "Pressure",
                    [
                        new DesignV2MetricGridCell("27.5", DesignV2Evidence.Measured),
                        new DesignV2MetricGridCell("27.7", DesignV2Evidence.Measured)
                    ],
                    DesignV2Evidence.Measured)
            ]);
        var metrics = new DesignV2MetricRowsBody([], [], [tireGrid]);
        var bounds = new RectangleF(10f, 20f, geometry.MetricListMinimumWidth, 360f);

        var layout = InvokeInstanceBody("BuildMetricRowsLayout", bounds, metrics);

        Assert.Empty(layout.MetricRows);
        var grid = Assert.Single(layout.MetricGrids);
        Assert.Equal(bounds.Top, grid.Bounds.Y, 3);
    }

    private static Color InvokeColor(string methodName, params object[] arguments)
    {
        var method = typeof(DesignV2LiveOverlayForm).GetMethod(
            methodName,
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"{methodName} was not found.");
        return (Color)(method.Invoke(null, arguments) ?? throw new InvalidOperationException($"{methodName} returned null."));
    }

    private static IReadOnlyList<DesignV2LayoutRow> InvokeRows(string methodName, params object[] arguments)
    {
        var method = typeof(DesignV2LiveOverlayForm).GetMethod(
            methodName,
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"{methodName} was not found.");
        return (IReadOnlyList<DesignV2LayoutRow>)(method.Invoke(null, arguments)
            ?? throw new InvalidOperationException($"{methodName} returned null."));
    }

    private static DesignV2LayoutBody InvokeBody(string methodName, params object[] arguments)
    {
        var method = typeof(DesignV2LiveOverlayForm).GetMethod(
            methodName,
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"{methodName} was not found.");
        return (DesignV2LayoutBody)(method.Invoke(null, arguments)
            ?? throw new InvalidOperationException($"{methodName} returned null."));
    }

    private static DesignV2LayoutBody InvokeInstanceBody(string methodName, params object[] arguments)
    {
        var method = typeof(DesignV2LiveOverlayForm).GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"{methodName} was not found.");
        var form = (DesignV2LiveOverlayForm)RuntimeHelpers.GetUninitializedObject(typeof(DesignV2LiveOverlayForm));
        return (DesignV2LayoutBody)(method.Invoke(form, arguments)
            ?? throw new InvalidOperationException($"{methodName} returned null."));
    }

    private static DesignV2GapTrendMetric Metric(string label, string state = "ready")
    {
        return new DesignV2GapTrendMetric(
            Label: label,
            FocusGapChangeSeconds: 0.4d,
            Chaser: new DesignV2BehindGainMetric(42, "P6", 0.7d),
            State: state,
            StateLabel: null,
            ComparisonText: "Track");
    }

    private static void AssertInside(RectangleF outer, DesignV2LayoutRect inner)
    {
        Assert.True(inner.X >= outer.Left, $"{inner} starts left of {outer}.");
        Assert.True(inner.Y >= outer.Top, $"{inner} starts above {outer}.");
        Assert.True(inner.X + inner.Width <= outer.Right + 0.001f, $"{inner} ends right of {outer}.");
        Assert.True(inner.Y + inner.Height <= outer.Bottom + 0.001f, $"{inner} ends below {outer}.");
    }

    private static void AssertInside(DesignV2LayoutRect outer, DesignV2LayoutRect inner)
    {
        Assert.True(inner.X >= outer.X, $"{inner} starts left of {outer}.");
        Assert.True(inner.Y >= outer.Y, $"{inner} starts above {outer}.");
        Assert.True(inner.X + inner.Width <= outer.X + outer.Width + 0.001f, $"{inner} ends right of {outer}.");
        Assert.True(inner.Y + inner.Height <= outer.Y + outer.Height + 0.001f, $"{inner} ends below {outer}.");
    }

    private static float Right(DesignV2LayoutRect rect)
    {
        return rect.X + rect.Width;
    }
}
