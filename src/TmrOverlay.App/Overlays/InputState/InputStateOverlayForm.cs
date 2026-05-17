using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using Microsoft.Extensions.Logging;
using TmrOverlay.App.Overlays.Abstractions;
using TmrOverlay.App.Overlays.SimpleTelemetry;
using TmrOverlay.App.Overlays.Styling;
using TmrOverlay.App.Performance;
using TmrOverlay.Core.Overlays;
using TmrOverlay.Core.Settings;
using TmrOverlay.Core.Telemetry.Live;

namespace TmrOverlay.App.Overlays.InputState;

internal sealed class InputStateOverlayForm : PersistentOverlayForm
{
    private const int RefreshIntervalMilliseconds = 50;
    private const int MaximumTracePoints = 180;
    private const int CompactLayoutWidthThreshold = 320;
    private const int CompactLayoutHeightThreshold = 180;
    private const int SideReadoutWidthThreshold = 460;
    private const int FullLayoutHeightThreshold = 270;
    private const int MinimumGraphWidthWithRail = 230;
    private const int RailGap = 10;
    private static readonly Color ThrottleTraceColor = Color.FromArgb(48, 214, 109);
    private static readonly Color BrakeTraceColor = Color.FromArgb(236, 112, 99);
    private static readonly Color AbsActiveTraceColor = Color.FromArgb(255, 209, 102);
    private static readonly Color ClutchTraceColor = Color.FromArgb(104, 193, 255);
    private readonly ILiveTelemetrySource _liveTelemetrySource;
    private readonly ILogger _logger;
    private readonly AppPerformanceState _performanceState;
    private readonly OverlaySettings _settings;
    private readonly string _fontFamily;
    private readonly string _unitSystem;
    private readonly System.Windows.Forms.Timer _refreshTimer;
    private readonly List<InputTracePoint> _trace = [];
    private LiveInputTelemetryModel _latestInputs = LiveInputTelemetryModel.Empty;
    private OverlayChromeState _chromeState = new(
        "Inputs",
        "waiting",
        OverlayChromeTone.Waiting,
        Source: null,
        FooterMode: OverlayChromeFooterMode.Never);
    private long? _lastRefreshSequence;
    private string? _lastLoggedError;
    private DateTimeOffset? _lastLoggedErrorAtUtc;

    private int FooterReserveHeight => 0;

    public InputStateOverlayForm(
        ILiveTelemetrySource liveTelemetrySource,
        ILogger logger,
        AppPerformanceState performanceState,
        OverlaySettings settings,
        string fontFamily,
        string unitSystem,
        Action saveSettings)
        : base(
            settings,
            saveSettings,
            InputStateOverlayDefinition.Definition.DefaultWidth,
            InputStateOverlayDefinition.Definition.DefaultHeight)
    {
        _liveTelemetrySource = liveTelemetrySource;
        _logger = logger;
        _performanceState = performanceState;
        _settings = settings;
        _fontFamily = fontFamily;
        _unitSystem = unitSystem;
        BackColor = OverlayTheme.Colors.WindowBackground;

        _refreshTimer = new System.Windows.Forms.Timer
        {
            Interval = RefreshIntervalMilliseconds
        };
        _refreshTimer.Tick += (_, _) =>
        {
            _performanceState.RecordOverlayTimerTick(
                InputStateOverlayDefinition.Definition.Id,
                RefreshIntervalMilliseconds,
                Visible,
                !Visible || Opacity <= 0.001d);
            RefreshOverlay();
        };
        _refreshTimer.Start();

        RefreshOverlay();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _refreshTimer.Stop();
            _refreshTimer.Dispose();
        }

        base.Dispose(disposing);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var started = Stopwatch.GetTimestamp();
        var succeeded = false;
        try
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            OverlayChrome.DrawWindowBorder(e.Graphics, ClientSize);

            DrawInputLayout(e.Graphics);
            succeeded = true;
        }
        catch (Exception exception)
        {
            ReportOverlayError(exception, "render");
        }
        finally
        {
            _performanceState.RecordOperation(
                AppPerformanceMetricIds.OverlayInputStatePaint,
                started,
                succeeded);
        }
    }

    private void RefreshOverlay()
    {
        var started = Stopwatch.GetTimestamp();
        var succeeded = false;
        try
        {
            LiveTelemetrySnapshot snapshot;
            var snapshotStarted = Stopwatch.GetTimestamp();
            var snapshotSucceeded = false;
            try
            {
                snapshot = _liveTelemetrySource.Snapshot();
                snapshot = snapshot with { Models = snapshot.CompleteModels() };
                snapshotSucceeded = true;
            }
            finally
            {
                _performanceState.RecordOperation(
                    AppPerformanceMetricIds.OverlayInputStateSnapshot,
                    snapshotStarted,
                    snapshotSucceeded);
            }

            var previousSequence = _lastRefreshSequence;
            var now = DateTimeOffset.UtcNow;
            var availability = OverlayAvailabilityEvaluator.FromSnapshot(snapshot, now);
            if (!availability.IsAvailable)
            {
                ResetInputState(snapshot, availability.StatusText);
            }
            else if (!IsPlayerInCar(snapshot, now))
            {
                ResetInputState(snapshot, "waiting for player in car");
            }
            else if (!snapshot.Models.Inputs.HasData)
            {
                ResetInputState(snapshot, "waiting for inputs");
            }
            else
            {
                _latestInputs = snapshot.Models.Inputs;
                _chromeState = ChromeStateFor(
                    snapshot,
                    FormatStatus(_latestInputs),
                    OverlayChromeTone.Normal,
                    "source: local car telemetry");
                AddTracePoint(_latestInputs);
            }

            _lastRefreshSequence = snapshot.Sequence;
            _performanceState.RecordOverlayRefreshDecision(
                InputStateOverlayDefinition.Definition.Id,
                now,
                previousSequence,
                snapshot.Sequence,
                snapshot.LastUpdatedAtUtc,
                applied: true);
            Invalidate();
            succeeded = true;
        }
        catch (Exception exception)
        {
            _chromeState = OverlayChromeState.Error(
                "Inputs",
                "input error",
                source: null,
                footerMode: OverlayChromeFooterMode.Never);
            ReportOverlayError(exception, "refresh");
            Invalidate();
        }
        finally
        {
            _performanceState.RecordOperation(
                AppPerformanceMetricIds.OverlayInputStateRefresh,
                started,
                succeeded);
        }
    }

    private void AddTracePoint(LiveInputTelemetryModel inputs)
    {
        _trace.Add(new InputTracePoint(
            Throttle: Clamp01(inputs.Throttle),
            Brake: Clamp01(inputs.Brake),
            Clutch: Clamp01(inputs.Clutch),
            BrakeAbsActive: inputs.BrakeAbsActive == true));
        if (_trace.Count > MaximumTracePoints)
        {
            _trace.RemoveRange(0, _trace.Count - MaximumTracePoints);
        }
    }

    private void ResetInputState(LiveTelemetrySnapshot snapshot, string status)
    {
        _latestInputs = LiveInputTelemetryModel.Empty;
        _trace.Clear();
        _chromeState = ChromeStateFor(
            snapshot,
            status,
            OverlayChromeTone.Waiting,
            "source: waiting");
    }

    private void DrawHeader(Graphics graphics)
    {
        OverlayChrome.DrawHeader(graphics, _fontFamily, _chromeState, ClientSize.Width, titleWidth: 116);
    }

    private void DrawFooter(Graphics graphics)
    {
        OverlayChrome.DrawFooter(graphics, _fontFamily, _chromeState, ClientSize.Width, ClientSize.Height, minimumWidth: 180);
    }

    private OverlayChromeState ChromeStateFor(
        LiveTelemetrySnapshot snapshot,
        string status,
        OverlayChromeTone tone,
        string source)
    {
        return new OverlayChromeState(
            "Inputs",
            status,
            tone,
            source,
            OverlayChromeFooterMode.Never);
    }

    private void DrawInputLayout(Graphics graphics)
    {
        var content = new Rectangle(
            14,
            14,
            Math.Max(120, ClientSize.Width - 28),
            Math.Max(96, ClientSize.Height - 28));
        var railEnabled = InputRailEnabled();
        var railWidth = railEnabled
            ? Math.Clamp((int)Math.Round(content.Width * 0.30d), 126, 162)
            : 0;
        var graphWidth = railEnabled
            ? content.Width - railWidth - RailGap
            : content.Width;
        if (railEnabled && graphWidth < MinimumGraphWidthWithRail)
        {
            railEnabled = false;
            railWidth = 0;
            graphWidth = content.Width;
        }

        var graph = new Rectangle(
            content.Left,
            content.Top,
            Math.Max(120, graphWidth),
            content.Height);
        DrawPedalTraceGraph(graphics, graph);

        if (!railEnabled)
        {
            return;
        }

        DrawInputRail(
            graphics,
            new Rectangle(
                graph.Right + RailGap,
                content.Top,
                railWidth,
                content.Height));
    }

    private void DrawWideTraceState(Graphics graphics)
    {
        var graph = new Rectangle(
            14,
            44,
            Math.Max(260, ClientSize.Width - 28),
            Math.Max(80, ClientSize.Height - 118 - FooterReserveHeight));
        DrawPedalTraceGraph(graphics, graph);

        var readoutTop = graph.Bottom + 8;
        var readoutRect = new Rectangle(
            14,
            readoutTop,
            Math.Max(260, ClientSize.Width - 28),
            Math.Max(36, ClientSize.Height - readoutTop - 12 - FooterReserveHeight));
        DrawCompactReadouts(graphics, readoutRect);
    }

    private void DrawPedalTraceGraph(Graphics graphics, Rectangle graph)
    {
        using var background = new SolidBrush(OverlayTheme.Colors.PanelBackground);
        graphics.FillRectangle(background, graph);
        using var border = new Pen(OverlayTheme.Colors.WindowBorder);
        graphics.DrawRectangle(border, graph);

        using var gridPen = new Pen(Color.FromArgb(46, 255, 255, 255));
        for (var step = 1; step < 4; step++)
        {
            var y = graph.Top + graph.Height * step / 4;
            graphics.DrawLine(gridPen, graph.Left, y, graph.Right, y);
        }

        if (TraceEnabled(OverlayOptionKeys.InputShowThrottleTrace))
        {
            DrawTrace(graphics, graph, point => point.Throttle, ThrottleTraceColor);
        }

        if (TraceEnabled(OverlayOptionKeys.InputShowBrakeTrace))
        {
            DrawTrace(graphics, graph, point => point.Brake, BrakeTraceColor);
            DrawActiveTraceSegments(
                graphics,
                graph,
                point => point.Brake,
                point => point.BrakeAbsActive,
                AbsActiveTraceColor);
        }

        if (TraceEnabled(OverlayOptionKeys.InputShowClutchTrace))
        {
            DrawTrace(graphics, graph, point => point.Clutch, ClutchTraceColor);
        }

        using var font = OverlayTheme.Font(_fontFamily, 8.6f, FontStyle.Bold);
        DrawLegend(graphics, graph, font);
        if (_trace.Count < 2 && !string.IsNullOrWhiteSpace(_chromeState.Status))
        {
            using var emptyFont = OverlayTheme.Font(_fontFamily, 9.4f, FontStyle.Bold);
            using var emptyBrush = new SolidBrush(OverlayTheme.Colors.TextMuted);
            DrawText(
                graphics,
                _chromeState.Status,
                emptyFont,
                emptyBrush,
                graph,
                ContentAlignment.MiddleCenter);
        }
    }

    private void DrawInputRail(Graphics graphics, Rectangle rail)
    {
        using var background = new SolidBrush(OverlayTheme.Colors.PanelBackground);
        using var border = new Pen(OverlayTheme.Colors.WindowBorder);
        graphics.FillRectangle(background, rail);
        graphics.DrawRectangle(border, rail);

        var inner = Rectangle.Inflate(rail, -8, -8);
        if (inner.Width <= 0 || inner.Height <= 0)
        {
            return;
        }

        var numericItems = NumericRailItems();
        var bottom = inner.Bottom;
        if (numericItems.Count > 0)
        {
            var numericHeight = Math.Min(42, Math.Max(32, inner.Height / 4));
            var numeric = new Rectangle(inner.Left, bottom - numericHeight, inner.Width, numericHeight);
            DrawNumericRail(graphics, numeric, numericItems);
            bottom = numeric.Top - 8;
        }

        if (CurrentValueEnabled(OverlayOptionKeys.InputShowSteering) && bottom - inner.Top >= 54)
        {
            var wheelHeight = Math.Min(62, Math.Max(48, (bottom - inner.Top) / 3));
            var wheel = new Rectangle(inner.Left, bottom - wheelHeight, inner.Width, wheelHeight);
            DrawSteeringRail(graphics, wheel);
            bottom = wheel.Top - 8;
        }

        var pedals = PedalRailItems();
        if (pedals.Count == 0 || bottom <= inner.Top + 34)
        {
            return;
        }

        DrawPedalRail(graphics, new Rectangle(inner.Left, inner.Top, inner.Width, bottom - inner.Top), pedals);
    }

    private IReadOnlyList<(string Label, double? Value, Color Color)> PedalRailItems()
    {
        var items = new List<(string Label, double? Value, Color Color)>();
        if (CurrentValueEnabled(OverlayOptionKeys.InputShowThrottle))
        {
            items.Add(("T", Clamp01(_latestInputs.Throttle), ThrottleTraceColor));
        }

        if (CurrentValueEnabled(OverlayOptionKeys.InputShowBrake))
        {
            var brakeAbsActive = _latestInputs.BrakeAbsActive == true;
            items.Add((brakeAbsActive ? "ABS" : "B", Clamp01(_latestInputs.Brake), brakeAbsActive ? AbsActiveTraceColor : BrakeTraceColor));
        }

        if (CurrentValueEnabled(OverlayOptionKeys.InputShowClutch))
        {
            items.Add(("C", Clamp01(_latestInputs.Clutch), ClutchTraceColor));
        }

        return items;
    }

    private IReadOnlyList<(string Label, string Value)> NumericRailItems()
    {
        var items = new List<(string Label, string Value)>();
        if (CurrentValueEnabled(OverlayOptionKeys.InputShowGear))
        {
            items.Add(("Gear", FormatGear(_latestInputs.Gear)));
        }

        if (CurrentValueEnabled(OverlayOptionKeys.InputShowSpeed))
        {
            items.Add(("Speed", SimpleTelemetryOverlayViewModel.FormatSpeed(_latestInputs.SpeedMetersPerSecond, _unitSystem)));
        }

        return items;
    }

    private void DrawPedalRail(
        Graphics graphics,
        Rectangle bounds,
        IReadOnlyList<(string Label, double? Value, Color Color)> pedals)
    {
        using var labelFont = OverlayTheme.Font(_fontFamily, 7.8f, FontStyle.Bold);
        using var valueFont = new Font(FontFamily.GenericMonospace, 7.8f);
        var columnWidth = Math.Max(1, bounds.Width / pedals.Count);
        for (var index = 0; index < pedals.Count; index++)
        {
            var left = bounds.Left + index * columnWidth;
            var width = index == pedals.Count - 1
                ? bounds.Right - left
                : columnWidth;
            DrawVerticalInputBar(
                graphics,
                new Rectangle(left, bounds.Top, Math.Max(1, width), bounds.Height),
                pedals[index].Label,
                pedals[index].Value,
                pedals[index].Color,
                labelFont,
                valueFont);
        }
    }

    private static void DrawVerticalInputBar(
        Graphics graphics,
        Rectangle bounds,
        string label,
        double? value,
        Color color,
        Font labelFont,
        Font valueFont)
    {
        using var labelBrush = new SolidBrush(OverlayTheme.Colors.TextSubtle);
        using var valueBrush = new SolidBrush(OverlayTheme.Colors.TextPrimary);
        using var trackBrush = new SolidBrush(Color.FromArgb(42, 255, 255, 255));
        using var fillBrush = new SolidBrush(color);
        DrawText(graphics, label, labelFont, labelBrush, new RectangleF(bounds.Left, bounds.Top, bounds.Width, 15), ContentAlignment.MiddleCenter);

        var track = new Rectangle(
            bounds.Left + Math.Max(2, bounds.Width / 2 - 7),
            bounds.Top + 18,
            Math.Min(14, Math.Max(6, bounds.Width - 8)),
            Math.Max(18, bounds.Height - 38));
        graphics.FillRectangle(trackBrush, track);
        var normalized = Math.Clamp(value ?? 0d, 0d, 1d);
        if (normalized > 0d)
        {
            var fillHeight = (int)Math.Round(track.Height * normalized);
            graphics.FillRectangle(fillBrush, track.Left, track.Bottom - fillHeight, track.Width, fillHeight);
        }

        DrawText(
            graphics,
            FormatPercent(value),
            valueFont,
            valueBrush,
            new RectangleF(bounds.Left, bounds.Bottom - 17, bounds.Width, 17),
            ContentAlignment.MiddleCenter);
    }

    private void DrawSteeringRail(Graphics graphics, Rectangle bounds)
    {
        using var labelFont = OverlayTheme.Font(_fontFamily, 7.6f, FontStyle.Bold);
        using var valueFont = new Font(FontFamily.GenericMonospace, 7.6f);
        using var labelBrush = new SolidBrush(OverlayTheme.Colors.TextSubtle);
        using var valueBrush = new SolidBrush(OverlayTheme.Colors.TextPrimary);
        DrawText(graphics, "Wheel", labelFont, labelBrush, new RectangleF(bounds.Left, bounds.Top, bounds.Width, 14), ContentAlignment.MiddleLeft);
        DrawText(graphics, FormatSteering(_latestInputs.SteeringWheelAngle), valueFont, valueBrush, new RectangleF(bounds.Left, bounds.Top, bounds.Width, 14), ContentAlignment.MiddleRight);

        var size = Math.Min(bounds.Width, bounds.Height - 18);
        if (size < 24)
        {
            return;
        }

        var wheelRect = new Rectangle(
            bounds.Left + (bounds.Width - size) / 2,
            bounds.Top + 18,
            size,
            size);
        DrawWheel(graphics, wheelRect);
    }

    private void DrawNumericRail(Graphics graphics, Rectangle bounds, IReadOnlyList<(string Label, string Value)> items)
    {
        using var labelFont = OverlayTheme.Font(_fontFamily, 7.2f, FontStyle.Bold);
        using var valueFont = new Font(FontFamily.GenericMonospace, items.Count == 1 ? 10.4f : 8.8f, FontStyle.Bold);
        using var labelBrush = new SolidBrush(OverlayTheme.Colors.TextSubtle);
        using var valueBrush = new SolidBrush(OverlayTheme.Colors.TextPrimary);
        var cellWidth = Math.Max(1, bounds.Width / items.Count);
        for (var index = 0; index < items.Count; index++)
        {
            var left = bounds.Left + index * cellWidth;
            var width = index == items.Count - 1
                ? bounds.Right - left
                : cellWidth;
            var cell = new RectangleF(left, bounds.Top, width, bounds.Height);
            DrawText(graphics, items[index].Label, labelFont, labelBrush, new RectangleF(cell.Left, cell.Top, cell.Width, 14), ContentAlignment.MiddleCenter);
            DrawText(graphics, items[index].Value, valueFont, valueBrush, new RectangleF(cell.Left, cell.Top + 13, cell.Width, cell.Height - 13), ContentAlignment.MiddleCenter);
        }
    }

    private void DrawCompactState(Graphics graphics)
    {
        var content = new Rectangle(
            14,
            42,
            Math.Max(160, ClientSize.Width - 28),
            Math.Max(120, ClientSize.Height - 54 - FooterReserveHeight));
        using var background = new SolidBrush(OverlayTheme.Colors.PanelBackground);
        using var border = new Pen(OverlayTheme.Colors.WindowBorder);
        graphics.FillRectangle(background, content);
        graphics.DrawRectangle(border, content);

        var pedalHeight = Math.Min(76, Math.Max(58, content.Height / 2 - 8));
        var pedalRect = new Rectangle(content.Left + 10, content.Top + 8, content.Width - 20, pedalHeight);
        DrawCompactPedalBars(graphics, pedalRect);

        var readoutTop = pedalRect.Bottom + 8;
        var readoutRect = new Rectangle(
            content.Left + 10,
            readoutTop,
            content.Width - 20,
            Math.Max(44, content.Bottom - readoutTop - 8));
        DrawCompactReadouts(graphics, readoutRect);
    }

    private void DrawCompactPedalBars(Graphics graphics, Rectangle bounds)
    {
        using var labelFont = OverlayTheme.Font(_fontFamily, 8.2f, FontStyle.Bold);
        using var valueFont = new Font(FontFamily.GenericMonospace, 8.2f);
        var rowHeight = Math.Max(16, bounds.Height / 3);
        DrawCompactBar(graphics, bounds.Left, bounds.Top, bounds.Width, rowHeight, "T", Clamp01(_latestInputs.Throttle), ThrottleTraceColor, labelFont, valueFont);
        var brakeAbsActive = _latestInputs.BrakeAbsActive == true;
        DrawCompactBar(
            graphics,
            bounds.Left,
            bounds.Top + rowHeight,
            bounds.Width,
            rowHeight,
            brakeAbsActive ? "B ABS" : "B",
            Clamp01(_latestInputs.Brake),
            brakeAbsActive ? AbsActiveTraceColor : BrakeTraceColor,
            labelFont,
            valueFont);
        DrawCompactBar(graphics, bounds.Left, bounds.Top + rowHeight * 2, bounds.Width, rowHeight, "C", Clamp01(_latestInputs.Clutch), ClutchTraceColor, labelFont, valueFont);
    }

    private static void DrawCompactBar(
        Graphics graphics,
        int x,
        int y,
        int width,
        int height,
        string label,
        double? value,
        Color color,
        Font labelFont,
        Font valueFont)
    {
        var labelWidth = Math.Max(22, (int)Math.Ceiling(graphics.MeasureString(label, labelFont).Width) + 6);
        var valueWidth = 44;
        var barRect = new Rectangle(
            x + labelWidth,
            y + Math.Max(4, height / 2 - 4),
            Math.Max(20, width - labelWidth - valueWidth - 8),
            8);
        using var labelBrush = new SolidBrush(OverlayTheme.Colors.TextSubtle);
        using var trackBrush = new SolidBrush(Color.FromArgb(42, 255, 255, 255));
        using var valueBrush = new SolidBrush(color);
        using var valueTextBrush = new SolidBrush(OverlayTheme.Colors.TextPrimary);
        graphics.DrawString(label, labelFont, labelBrush, x, y + Math.Max(0, (height - 15) / 2));
        graphics.FillRectangle(trackBrush, barRect);

        var normalized = Math.Clamp(value ?? 0d, 0d, 1d);
        if (normalized > 0d)
        {
            graphics.FillRectangle(valueBrush, barRect.Left, barRect.Top, (int)Math.Round(barRect.Width * normalized), barRect.Height);
        }

        DrawText(
            graphics,
            FormatPercent(value),
            valueFont,
            valueTextBrush,
            new RectangleF(barRect.Right + 8, y, valueWidth, height),
            ContentAlignment.MiddleRight);
    }

    private void DrawCompactReadouts(Graphics graphics, Rectangle bounds)
    {
        using var labelFont = OverlayTheme.Font(_fontFamily, 7.8f);
        using var valueFont = new Font(FontFamily.GenericMonospace, 8.1f);
        using var labelBrush = new SolidBrush(OverlayTheme.Colors.TextSubtle);
        using var valueBrush = new SolidBrush(OverlayTheme.Colors.TextPrimary);
        var rows = new[]
        {
            ("Speed", SimpleTelemetryOverlayViewModel.FormatSpeed(_latestInputs.SpeedMetersPerSecond, _unitSystem)),
            ("Gear", FormatGear(_latestInputs.Gear)),
            ("RPM", FormatRpm(_latestInputs.Rpm)),
            ("Steer", FormatSteering(_latestInputs.SteeringWheelAngle)),
            ("Water", SimpleTelemetryOverlayViewModel.FormatTemperature(_latestInputs.WaterTempC, _unitSystem)),
            ("Oil", SimpleTelemetryOverlayViewModel.FormatPressure(_latestInputs.OilPressureBar, _unitSystem))
        };
        var columnCount = bounds.Width >= 300 ? 3 : 2;
        var rowCount = (int)Math.Ceiling(rows.Length / (double)columnCount);
        var cellWidth = Math.Max(70, bounds.Width / columnCount);
        var cellHeight = Math.Max(18, bounds.Height / Math.Max(1, rowCount));
        for (var index = 0; index < rows.Length; index++)
        {
            var column = index % columnCount;
            var row = index / columnCount;
            var cell = new RectangleF(
                bounds.Left + column * cellWidth,
                bounds.Top + row * cellHeight,
                Math.Min(cellWidth, bounds.Right - (bounds.Left + column * cellWidth)),
                cellHeight);
            DrawText(graphics, rows[index].Item1, labelFont, labelBrush, new RectangleF(cell.Left, cell.Top, 42, cell.Height), ContentAlignment.MiddleLeft);
            DrawText(graphics, rows[index].Item2, valueFont, valueBrush, new RectangleF(cell.Left + 42, cell.Top, Math.Max(20, cell.Width - 44), cell.Height), ContentAlignment.MiddleRight);
        }
    }

    private void DrawTrace(Graphics graphics, Rectangle graph, Func<InputTracePoint, double?> select, Color color)
    {
        var points = _trace
            .Select((point, index) => (Value: select(point), Index: index))
            .Where(point => point.Value is { } value && value >= 0d)
            .Select(point =>
                TracePoint(graph, point.Index, point.Value!.Value))
            .ToArray();
        if (points.Length < 2)
        {
            return;
        }

        using var pen = new Pen(color, 2f);
        var state = graphics.Save();
        try
        {
            graphics.SetClip(graph, CombineMode.Intersect);
            if (points.Length < 3)
            {
                graphics.DrawLines(pen, points);
                return;
            }

            using var path = SmoothTracePath(points);
            graphics.DrawPath(pen, path);
        }
        finally
        {
            graphics.Restore(state);
        }
    }

    private void DrawActiveTraceSegments(
        Graphics graphics,
        Rectangle graph,
        Func<InputTracePoint, double?> select,
        Func<InputTracePoint, bool> isActive,
        Color color)
    {
        if (_trace.Count < 2)
        {
            return;
        }

        using var pen = new Pen(color, 3f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        var state = graphics.Save();
        try
        {
            graphics.SetClip(graph, CombineMode.Intersect);
            for (var index = 1; index < _trace.Count; index++)
            {
                if (!isActive(_trace[index]))
                {
                    continue;
                }

                var previous = select(_trace[index - 1]);
                var current = select(_trace[index]);
                if (previous is not { } previousValue
                    || current is not { } currentValue
                    || previousValue < 0d
                    || currentValue < 0d)
                {
                    continue;
                }

                graphics.DrawLine(
                    pen,
                    TracePoint(graph, index - 1, previousValue),
                    TracePoint(graph, index, currentValue));
            }
        }
        finally
        {
            graphics.Restore(state);
        }
    }

    private static PointF TracePoint(Rectangle graph, int index, double value)
    {
        var x = graph.Left + (float)index / Math.Max(1, MaximumTracePoints - 1) * graph.Width;
        var y = graph.Bottom - (float)Math.Clamp(value, 0d, 1d) * graph.Height;
        return new PointF(x, y);
    }

    private static GraphicsPath SmoothTracePath(IReadOnlyList<PointF> points)
    {
        var path = new GraphicsPath();
        path.StartFigure();
        for (var index = 0; index < points.Count - 1; index++)
        {
            var p0 = index == 0 ? points[index] : points[index - 1];
            var p1 = points[index];
            var p2 = points[index + 1];
            var p3 = index + 2 < points.Count ? points[index + 2] : p2;
            var control1 = new PointF(
                p1.X + (p2.X - p0.X) / 6f,
                ClampSmoothTraceControlY(p1.Y + (p2.Y - p0.Y) / 6f, p1.Y, p2.Y));
            var control2 = new PointF(
                p2.X - (p3.X - p1.X) / 6f,
                ClampSmoothTraceControlY(p2.Y - (p3.Y - p1.Y) / 6f, p1.Y, p2.Y));
            path.AddBezier(p1, control1, control2, p2);
        }

        return path;
    }

    private static float ClampSmoothTraceControlY(float value, float segmentStartY, float segmentEndY)
    {
        var min = Math.Min(segmentStartY, segmentEndY);
        var max = Math.Max(segmentStartY, segmentEndY);
        return Math.Clamp(value, min, max);
    }

    private void DrawLegend(Graphics graphics, Rectangle graph, Font font)
    {
        var x = graph.Left + 8;
        if (TraceEnabled(OverlayOptionKeys.InputShowThrottleTrace))
        {
            DrawLegendItem(graphics, "Throttle", ThrottleTraceColor, font, ref x, graph.Top + 8);
        }

        if (TraceEnabled(OverlayOptionKeys.InputShowBrakeTrace))
        {
            DrawLegendItem(
                graphics,
                _latestInputs.BrakeAbsActive == true ? "Brake ABS" : "Brake",
                _latestInputs.BrakeAbsActive == true ? AbsActiveTraceColor : BrakeTraceColor,
                font,
                ref x,
                graph.Top + 8);
        }

        if (TraceEnabled(OverlayOptionKeys.InputShowClutchTrace))
        {
            DrawLegendItem(graphics, "Clutch", ClutchTraceColor, font, ref x, graph.Top + 8);
        }
    }

    private void DrawLegendItem(Graphics graphics, string label, Color color, Font font, ref int x, int y)
    {
        using var brush = new SolidBrush(color);
        graphics.FillRectangle(brush, x, y + 5, 14, 3);
        x += 18;
        graphics.DrawString(label, font, brush, x, y);
        x += (int)graphics.MeasureString(label, font).Width + 14;
    }

    private void DrawWheel(Graphics graphics)
    {
        var wheelRect = new Rectangle(Math.Max(Width - 150, 280), 56, 108, 108);
        DrawWheel(graphics, wheelRect);
    }

    private void DrawWheel(Graphics graphics, Rectangle wheelRect)
    {
        var center = new PointF(wheelRect.Left + wheelRect.Width / 2f, wheelRect.Top + wheelRect.Height / 2f);
        using var rimPen = new Pen(OverlayTheme.Colors.TextSecondary, 4f);
        graphics.DrawEllipse(rimPen, wheelRect);
        var angle = (float)(_latestInputs.SteeringWheelAngle ?? 0d);
        var spokeLength = wheelRect.Width * 0.4f;
        using var spokePen = new Pen(OverlayTheme.Colors.InfoText, 3f);
        for (var spoke = 0; spoke < 3; spoke++)
        {
            var theta = angle + (float)(spoke * Math.PI * 2d / 3d) - (float)Math.PI / 2f;
            graphics.DrawLine(
                spokePen,
                center,
                new PointF(
                    center.X + MathF.Cos(theta) * spokeLength,
                    center.Y + MathF.Sin(theta) * spokeLength));
        }
    }

    private void DrawCarState(Graphics graphics)
    {
        using var labelFont = OverlayTheme.Font(_fontFamily, 8.4f);
        using var valueFont = new Font(FontFamily.GenericMonospace, 8.4f);
        using var labelBrush = new SolidBrush(OverlayTheme.Colors.TextSubtle);
        using var valueBrush = new SolidBrush(OverlayTheme.Colors.TextPrimary);
        var x = Math.Max(Width - 158, 272);
        var y = 178;
        DrawPair(graphics, "Speed", SimpleTelemetryOverlayViewModel.FormatSpeed(_latestInputs.SpeedMetersPerSecond, _unitSystem), labelFont, valueFont, labelBrush, valueBrush, x, ref y);
        DrawPair(graphics, "Gear", FormatGear(_latestInputs.Gear), labelFont, valueFont, labelBrush, valueBrush, x, ref y);
        DrawPair(graphics, "RPM", FormatRpm(_latestInputs.Rpm), labelFont, valueFont, labelBrush, valueBrush, x, ref y);
        DrawPair(graphics, "Cooling", SimpleTelemetryOverlayViewModel.FormatTemperature(_latestInputs.WaterTempC, _unitSystem), labelFont, valueFont, labelBrush, valueBrush, x, ref y);
        DrawPair(graphics, "Oil", SimpleTelemetryOverlayViewModel.FormatPressure(_latestInputs.OilPressureBar, _unitSystem), labelFont, valueFont, labelBrush, valueBrush, x, ref y);
    }

    private static void DrawPair(Graphics graphics, string label, string value, Font labelFont, Font valueFont, Brush labelBrush, Brush valueBrush, int x, ref int y)
    {
        graphics.DrawString(label, labelFont, labelBrush, x, y);
        graphics.DrawString(value, valueFont, valueBrush, x + 70, y);
        y += 19;
    }

    private bool UseCompactLayout()
    {
        return ClientSize.Width < CompactLayoutWidthThreshold
            || ClientSize.Height < CompactLayoutHeightThreshold;
    }

    private bool UseWideTraceLayout()
    {
        return ClientSize.Height < FullLayoutHeightThreshold
            && ClientSize.Width < SideReadoutWidthThreshold;
    }

    private bool InputRailEnabled()
    {
        return CurrentValueEnabled(OverlayOptionKeys.InputShowThrottle)
            || CurrentValueEnabled(OverlayOptionKeys.InputShowBrake)
            || CurrentValueEnabled(OverlayOptionKeys.InputShowClutch)
            || CurrentValueEnabled(OverlayOptionKeys.InputShowSteering)
            || CurrentValueEnabled(OverlayOptionKeys.InputShowGear)
            || CurrentValueEnabled(OverlayOptionKeys.InputShowSpeed);
    }

    private bool CurrentValueEnabled(string optionKey)
    {
        return _settings.GetBooleanOption(optionKey, defaultValue: true);
    }

    private bool TraceEnabled(string optionKey)
    {
        return _settings.GetBooleanOption(optionKey, defaultValue: true);
    }

    private static string FormatStatus(LiveInputTelemetryModel inputs)
    {
        var status = string.Join(
            " | ",
            new[]
            {
                FormatGear(inputs.Gear),
                FormatRpm(inputs.Rpm),
                inputs.BrakeAbsActive == true ? "ABS" : "--"
            }.Where(value => value != "--"));
        return string.IsNullOrWhiteSpace(status) ? "--" : status;
    }

    private static bool IsPlayerInCar(LiveTelemetrySnapshot snapshot, DateTimeOffset now)
    {
        return LiveLocalStrategyContext.ForRequirement(
            snapshot,
            now,
            OverlayContextRequirement.LocalPlayerInCarOrPit).IsAvailable;
    }

    private static string FormatGear(int? gear)
    {
        return gear switch
        {
            -1 => "R",
            0 => "N",
            > 0 => gear.Value.ToString(CultureInfo.InvariantCulture),
            _ => "--"
        };
    }

    private static string FormatRpm(double? rpm)
    {
        return rpm is { } value && SimpleTelemetryOverlayViewModel.IsFinite(value)
            ? $"{value.ToString("0", CultureInfo.InvariantCulture)} rpm"
            : "--";
    }

    private static string FormatSteering(double? radians)
    {
        return radians is { } value && SimpleTelemetryOverlayViewModel.IsFinite(value)
            ? $"{(value * 180d / Math.PI).ToString("+0;-0;0", CultureInfo.InvariantCulture)} deg"
            : "--";
    }

    private static string FormatPercent(double? value)
    {
        return SimpleTelemetryOverlayViewModel.FormatPercent(value);
    }

    private static double? Clamp01(double? value)
    {
        return value is { } number && SimpleTelemetryOverlayViewModel.IsFinite(number)
            ? Math.Clamp(number, 0d, 1d)
            : null;
    }

    private static void DrawText(
        Graphics graphics,
        string text,
        Font font,
        Brush brush,
        RectangleF bounds,
        ContentAlignment alignment)
    {
        using var format = new StringFormat
        {
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap
        };
        format.Alignment = alignment is ContentAlignment.TopRight or ContentAlignment.MiddleRight or ContentAlignment.BottomRight
            ? StringAlignment.Far
            : alignment is ContentAlignment.TopCenter or ContentAlignment.MiddleCenter or ContentAlignment.BottomCenter
                ? StringAlignment.Center
                : StringAlignment.Near;
        format.LineAlignment = alignment is ContentAlignment.BottomLeft or ContentAlignment.BottomCenter or ContentAlignment.BottomRight
            ? StringAlignment.Far
            : alignment is ContentAlignment.MiddleLeft or ContentAlignment.MiddleCenter or ContentAlignment.MiddleRight
                ? StringAlignment.Center
                : StringAlignment.Near;
        graphics.DrawString(text, font, brush, bounds, format);
    }

    private void ReportOverlayError(Exception exception, string stage)
    {
        var message = $"{stage}: {exception.GetType().Name} {exception.Message}";
        var now = DateTimeOffset.UtcNow;
        if (string.Equals(_lastLoggedError, message, StringComparison.Ordinal)
            && _lastLoggedErrorAtUtc is { } lastLogged
            && now - lastLogged < TimeSpan.FromSeconds(30))
        {
            return;
        }

        _lastLoggedError = message;
        _lastLoggedErrorAtUtc = now;
        _logger.LogWarning(exception, "Input overlay {Stage} failed.", stage);
    }

    private sealed record InputTracePoint(double? Throttle, double? Brake, double? Clutch, bool BrakeAbsActive);
}
