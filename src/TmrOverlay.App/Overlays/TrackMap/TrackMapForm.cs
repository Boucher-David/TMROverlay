using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using Microsoft.Extensions.Logging;
using TmrOverlay.App.Overlays.Abstractions;
using TmrOverlay.App.Overlays.Content;
using TmrOverlay.App.Overlays.Styling;
using TmrOverlay.App.Performance;
using TmrOverlay.App.TrackMaps;
using TmrOverlay.Core.Overlays;
using TmrOverlay.Core.Settings;
using TmrOverlay.Core.Telemetry.Live;
using TmrOverlay.Core.TrackMaps;

namespace TmrOverlay.App.Overlays.TrackMap;

internal sealed class TrackMapForm : PersistentOverlayForm
{
    private static readonly Color TransparentColor = Color.Black;
    private static readonly Color TrackInteriorColor = Color.FromArgb(9, 14, 18);
    private static readonly Color TrackHaloColor = Color.FromArgb(82, 255, 255, 255);
    private static readonly Color TrackLineColor = Color.FromArgb(255, 222, 238, 246);
    private static readonly Color SectorBoundaryColor = Color.FromArgb(235, 98, 199, 255);
    private static readonly Color PersonalBestSectorColor = Color.FromArgb(255, 80, 214, 124);
    private static readonly Color BestLapSectorColor = Color.FromArgb(255, 182, 92, 255);
    private static readonly Color PitLineColor = Color.FromArgb(190, 98, 199, 255);
    private static readonly Color MarkerBorderColor = Color.FromArgb(230, 8, 14, 18);
    private static readonly Color FocusMarkerColor = Color.FromArgb(255, 98, 199, 255);
    private static readonly Color DefaultMarkerColor = Color.FromArgb(245, 236, 244, 248);
    private const int TrackInteriorMaximumAlpha = 150;
    private const int RefreshIntervalMilliseconds = 50;
    private const float MapPadding = 20f;
    private const float TrackHaloWidth = 11f;
    private const float TrackLineWidth = 4.4f;
    private const float SectorBoundaryTickLength = 17f;
    private const float SectorBoundaryTickWidth = 2.2f;
    private const float PitLineWidth = 2.2f;
    private const float MarkerRadius = 3.6f;
    private const float FocusMarkerRadius = 5.7f;
    private const float FocusMarkerTextSize = 7.6f;
    private const double MapReloadIntervalSeconds = 10d;
    private const double MarkerSmoothingSeconds = 0.14d;

    private readonly ILiveTelemetrySource _liveTelemetrySource;
    private readonly TrackMapStore _trackMapStore;
    private readonly ILogger<TrackMapForm> _logger;
    private readonly AppPerformanceState _performanceState;
    private readonly OverlaySettings _settings;
    private readonly Action _saveSettings;
    private readonly System.Windows.Forms.Timer _refreshTimer;
    private LiveTelemetrySnapshot _latestSnapshot = LiveTelemetrySnapshot.Empty;
    private TrackMapOverlayViewModel _viewModel = TrackMapOverlayViewModel.Empty;
    private TrackMapDocument? _trackMap;
    private string? _trackMapIdentityKey;
    private bool? _trackMapIncludeUserMaps;
    private readonly Dictionary<int, double> _smoothedMarkerProgress = [];
    private DateTimeOffset? _lastMarkerSmoothingAtUtc;
    private DateTimeOffset _nextMapReloadAtUtc = DateTimeOffset.MinValue;
    private string? _lastLoggedError;
    private DateTimeOffset? _lastLoggedErrorAtUtc;

    public TrackMapForm(
        ILiveTelemetrySource liveTelemetrySource,
        TrackMapStore trackMapStore,
        ILogger<TrackMapForm> logger,
        AppPerformanceState performanceState,
        OverlaySettings settings,
        string fontFamily,
        Action saveSettings)
        : base(
            settings,
            saveSettings,
            TrackMapOverlayDefinition.Definition.DefaultWidth,
            TrackMapOverlayDefinition.Definition.DefaultHeight)
    {
        _ = fontFamily;
        _liveTelemetrySource = liveTelemetrySource;
        _trackMapStore = trackMapStore;
        _logger = logger;
        _performanceState = performanceState;
        _settings = settings;
        _saveSettings = saveSettings;

        BackColor = TransparentColor;
        TransparencyKey = TransparentColor;
        DoubleBuffered = true;
        SetBaseOverlayOpacity(1d);
        Padding = Padding.Empty;

        _refreshTimer = new System.Windows.Forms.Timer
        {
            Interval = RefreshIntervalMilliseconds
        };
        _refreshTimer.Tick += (_, _) =>
        {
            _performanceState.RecordOverlayTimerTick(
                TrackMapOverlayDefinition.Definition.Id,
                RefreshIntervalMilliseconds,
                Visible,
                !Visible || Opacity <= 0.001d);
            RefreshOverlay();
        };
        _refreshTimer.Start();

        RefreshOverlay();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Invalidate();
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

    protected override void PersistOverlayFrame()
    {
        if (IsOverlayFramePersistenceSuppressed)
        {
            return;
        }

        _settings.X = Location.X;
        _settings.Y = Location.Y;
        _settings.Width = Size.Width;
        _settings.Height = Size.Height;
        _saveSettings();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var started = Stopwatch.GetTimestamp();
        var succeeded = false;
        try
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            DrawMap(e.Graphics);
            succeeded = true;
        }
        catch (Exception exception)
        {
            ReportOverlayError(exception, "render");
        }
        finally
        {
            _performanceState.RecordOperation(AppPerformanceMetricIds.OverlayTrackMapPaint, started, succeeded);
        }
    }

    private void RefreshOverlay()
    {
        var started = Stopwatch.GetTimestamp();
        var succeeded = false;
        try
        {
            var snapshotStarted = Stopwatch.GetTimestamp();
            var snapshotSucceeded = false;
            try
            {
                _latestSnapshot = _liveTelemetrySource.Snapshot();
                snapshotSucceeded = true;
            }
            finally
            {
                _performanceState.RecordOperation(
                    AppPerformanceMetricIds.OverlayTrackMapSnapshot,
                    snapshotStarted,
                    snapshotSucceeded);
            }

            var now = DateTimeOffset.UtcNow;
            RefreshTrackMap(_latestSnapshot, now);
            _viewModel = TrackMapOverlayViewModel.From(_latestSnapshot, now, _settings, _trackMap);
            Invalidate();
            succeeded = true;
        }
        catch (Exception exception)
        {
            ReportOverlayError(exception, "refresh");
        }
        finally
        {
            _performanceState.RecordOperation(AppPerformanceMetricIds.OverlayTrackMapRefresh, started, succeeded);
        }
    }

    private void RefreshTrackMap(LiveTelemetrySnapshot snapshot, DateTimeOffset now)
    {
        var identity = TrackMapIdentity.From(snapshot.Context.Track);
        var includeUserMaps = OverlayContentColumnSettings.ContentEnabledForSession(
            _settings,
            OverlayOptionKeys.TrackMapBuildFromTelemetry,
            defaultEnabled: true,
            OverlayAvailabilityEvaluator.CurrentSessionKind(snapshot));
        var identityChanged = !string.Equals(identity.Key, _trackMapIdentityKey, StringComparison.Ordinal);
        var mapSourceChanged = _trackMapIncludeUserMaps != includeUserMaps;
        if (!identityChanged
            && !mapSourceChanged
            && now < _nextMapReloadAtUtc)
        {
            return;
        }

        if (identityChanged || mapSourceChanged)
        {
            _smoothedMarkerProgress.Clear();
            _lastMarkerSmoothingAtUtc = null;
        }

        _trackMapIdentityKey = identity.Key;
        _trackMapIncludeUserMaps = includeUserMaps;
        _nextMapReloadAtUtc = now.AddSeconds(MapReloadIntervalSeconds);
        _trackMap = _trackMapStore.TryReadBest(
            snapshot.Context.Track,
            includeUserMaps: includeUserMaps);
    }

    private void DrawMap(Graphics graphics)
    {
        var drawBounds = DrawingBounds();
        if (drawBounds.Width <= 20f || drawBounds.Height <= 20f)
        {
            return;
        }

        var markers = SmoothMarkers(_viewModel.Markers.Select(ToTrackMapMarker).ToArray());
        using var interiorBrush = TrackInteriorBrush();
        var sectors = _viewModel.Sectors;
        var showSectorBoundaries = _viewModel.ShowSectorBoundaries;
        if (_viewModel.TrackMap?.RacingLine.Points is { Count: >= 3 })
        {
            DrawGeneratedMap(graphics, drawBounds, _viewModel.TrackMap, markers, sectors, interiorBrush, showSectorBoundaries);
            return;
        }

        DrawCircleFallback(graphics, drawBounds, markers, sectors, interiorBrush, showSectorBoundaries);
    }

    private RectangleF DrawingBounds()
    {
        var width = Math.Max(0, ClientSize.Width);
        var height = Math.Max(0, ClientSize.Height);
        var size = Math.Max(0f, Math.Min(width, height) - MapPadding * 2f);
        return new RectangleF(
            (width - size) / 2f,
            (height - size) / 2f,
            size,
            size);
    }

    private static void DrawGeneratedMap(
        Graphics graphics,
        RectangleF drawBounds,
        TrackMapDocument document,
        IReadOnlyList<TrackMapMarker> markers,
        IReadOnlyList<LiveTrackSectorSegment> sectors,
        Brush interiorBrush,
        bool showSectorBoundaries)
    {
        var transform = TrackMapTransform.From(document, drawBounds);
        if (transform is null)
        {
            DrawCircleFallback(graphics, drawBounds, markers, sectors, interiorBrush, showSectorBoundaries);
            return;
        }

        FillGeometry(graphics, document.RacingLine, transform, interiorBrush);

        using (var haloPen = new Pen(TrackHaloColor, TrackHaloWidth)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        })
        using (var trackPen = new Pen(TrackLineColor, TrackLineWidth)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        })
        {
            DrawGeometry(graphics, document.RacingLine, transform, haloPen);
            DrawGeometry(graphics, document.RacingLine, transform, trackPen);
        }

        DrawGeneratedSectorHighlights(
            graphics,
            document.RacingLine,
            transform,
            sectors);
        if (showSectorBoundaries)
        {
            DrawGeneratedSectorBoundaries(graphics, document.RacingLine, transform, sectors);
        }

        if (document.PitLane is { Points.Count: >= 2 } pitLane)
        {
            using var pitPen = new Pen(PitLineColor, PitLineWidth)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round
            };
            DrawGeometry(graphics, pitLane, transform, pitPen);
        }

        DrawMarkers(
            graphics,
            markers,
            marker => PointOnGeometry(document.RacingLine, transform, marker.LapDistPct));
    }

    private static void DrawCircleFallback(
        Graphics graphics,
        RectangleF drawBounds,
        IReadOnlyList<TrackMapMarker> markers,
        IReadOnlyList<LiveTrackSectorSegment> sectors,
        Brush interiorBrush,
        bool showSectorBoundaries)
    {
        graphics.FillEllipse(interiorBrush, drawBounds);
        using (var haloPen = new Pen(TrackHaloColor, TrackHaloWidth)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        })
        using (var trackPen = new Pen(TrackLineColor, TrackLineWidth)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        })
        {
            graphics.DrawEllipse(haloPen, drawBounds);
            graphics.DrawEllipse(trackPen, drawBounds);
        }

        DrawCircleSectorHighlights(graphics, drawBounds, sectors);
        if (showSectorBoundaries)
        {
            DrawCircleSectorBoundaries(graphics, drawBounds, sectors);
        }

        DrawMarkers(
            graphics,
            markers,
            marker => PointOnEllipse(drawBounds, marker.LapDistPct));
    }

    private static void DrawGeometry(
        Graphics graphics,
        TrackMapGeometry geometry,
        TrackMapTransform transform,
        Pen pen)
    {
        if (geometry.Points.Count < 2)
        {
            return;
        }

        using var path = new GraphicsPath();
        var previous = transform.Map(geometry.Points[0]);
        for (var index = 1; index < geometry.Points.Count; index++)
        {
            var current = transform.Map(geometry.Points[index]);
            path.AddLine(previous, current);
            previous = current;
        }

        if (geometry.Closed)
        {
            path.CloseFigure();
        }

        graphics.DrawPath(pen, path);
    }

    private static void DrawGeneratedSectorHighlights(
        Graphics graphics,
        TrackMapGeometry geometry,
        TrackMapTransform transform,
        IReadOnlyList<LiveTrackSectorSegment> sectors)
    {
        foreach (var sector in sectors.Where(HasHighlight))
        {
            using var pen = new Pen(SectorHighlightColor(sector.Highlight), TrackLineWidth + 1.2f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round
            };
            DrawGeometrySegment(graphics, geometry, transform, sector.StartPct, sector.EndPct, pen);
        }
    }

    private static void DrawGeneratedSectorBoundaries(
        Graphics graphics,
        TrackMapGeometry geometry,
        TrackMapTransform transform,
        IReadOnlyList<LiveTrackSectorSegment> sectors)
    {
        using var pen = new Pen(SectorBoundaryColor, SectorBoundaryTickWidth)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        foreach (var progress in SectorBoundaryProgresses(sectors))
        {
            if (GeometryBoundaryTick(geometry, transform, progress) is { } tick)
            {
                graphics.DrawLine(pen, tick.Start, tick.End);
            }
        }
    }

    private static void DrawGeometrySegment(
        Graphics graphics,
        TrackMapGeometry geometry,
        TrackMapTransform transform,
        double startPct,
        double endPct,
        Pen pen)
    {
        if (geometry.Points.Count < 2)
        {
            return;
        }

        using var path = new GraphicsPath();
        foreach (var range in SegmentRanges(startPct, endPct))
        {
            AddGeometrySegment(path, geometry, transform, range.StartPct, range.EndPct);
        }

        if (path.PointCount > 1)
        {
            graphics.DrawPath(pen, path);
        }
    }

    private static void AddGeometrySegment(
        GraphicsPath path,
        TrackMapGeometry geometry,
        TrackMapTransform transform,
        double startPct,
        double endPct)
    {
        if (endPct <= startPct)
        {
            return;
        }

        var startPoint = PointOnGeometry(geometry, transform, startPct);
        var endPoint = PointOnGeometry(geometry, transform, endPct);
        if (startPoint is null || endPoint is null)
        {
            return;
        }

        path.StartFigure();
        var previous = startPoint.Value;
        foreach (var point in geometry.Points.Where(point => point.LapDistPct > startPct && point.LapDistPct < endPct))
        {
            var current = transform.Map(point);
            path.AddLine(previous, current);
            previous = current;
        }

        path.AddLine(previous, endPoint.Value);
    }

    private static void DrawCircleSectorHighlights(
        Graphics graphics,
        RectangleF drawBounds,
        IReadOnlyList<LiveTrackSectorSegment> sectors)
    {
        foreach (var sector in sectors.Where(HasHighlight))
        {
            using var pen = new Pen(SectorHighlightColor(sector.Highlight), TrackLineWidth + 1.2f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            foreach (var range in SegmentRanges(sector.StartPct, sector.EndPct))
            {
                var sweep = (float)((range.EndPct - range.StartPct) * 360d);
                if (sweep <= 0f)
                {
                    continue;
                }

                graphics.DrawArc(
                    pen,
                    drawBounds,
                    (float)(NormalizeProgress(range.StartPct) * 360d - 90d),
                    sweep);
            }
        }
    }

    private static void DrawCircleSectorBoundaries(
        Graphics graphics,
        RectangleF drawBounds,
        IReadOnlyList<LiveTrackSectorSegment> sectors)
    {
        var center = new PointF(drawBounds.Left + drawBounds.Width / 2f, drawBounds.Top + drawBounds.Height / 2f);
        using var pen = new Pen(SectorBoundaryColor, SectorBoundaryTickWidth)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        foreach (var progress in SectorBoundaryProgresses(sectors))
        {
            var point = PointOnEllipse(drawBounds, progress);
            var dx = point.X - center.X;
            var dy = point.Y - center.Y;
            var length = MathF.Max(0.001f, MathF.Sqrt(dx * dx + dy * dy));
            var unitX = dx / length;
            var unitY = dy / length;
            var half = SectorBoundaryTickLength / 2f;
            graphics.DrawLine(
                pen,
                new PointF(point.X - unitX * half, point.Y - unitY * half),
                new PointF(point.X + unitX * half, point.Y + unitY * half));
        }
    }

    private static IEnumerable<double> SectorBoundaryProgresses(IReadOnlyList<LiveTrackSectorSegment> sectors)
    {
        if (sectors.Count < 2)
        {
            yield break;
        }

        var seen = new HashSet<double>();
        foreach (var sector in sectors)
        {
            var progress = Math.Round(NormalizeProgress(sector.StartPct), 5);
            if (seen.Add(progress))
            {
                yield return progress;
            }
        }
    }

    private static SectorBoundaryTick? GeometryBoundaryTick(
        TrackMapGeometry geometry,
        TrackMapTransform transform,
        double progress)
    {
        var center = PointOnGeometry(geometry, transform, progress);
        var before = PointOnGeometry(geometry, transform, progress - 0.002d);
        var after = PointOnGeometry(geometry, transform, progress + 0.002d);
        if (center is null || before is null || after is null)
        {
            return null;
        }

        var dx = after.Value.X - before.Value.X;
        var dy = after.Value.Y - before.Value.Y;
        var length = MathF.Max(0.001f, MathF.Sqrt(dx * dx + dy * dy));
        var normalX = -dy / length;
        var normalY = dx / length;
        var half = SectorBoundaryTickLength / 2f;
        return new SectorBoundaryTick(
            new PointF(center.Value.X - normalX * half, center.Value.Y - normalY * half),
            new PointF(center.Value.X + normalX * half, center.Value.Y + normalY * half));
    }

    private static bool HasHighlight(LiveTrackSectorSegment sector)
    {
        return string.Equals(sector.Highlight, LiveTrackSectorHighlights.PersonalBest, StringComparison.Ordinal)
            || string.Equals(sector.Highlight, LiveTrackSectorHighlights.BestLap, StringComparison.Ordinal);
    }

    private static Color SectorHighlightColor(string highlight)
    {
        return string.Equals(highlight, LiveTrackSectorHighlights.BestLap, StringComparison.Ordinal)
            ? BestLapSectorColor
            : PersonalBestSectorColor;
    }

    private static IEnumerable<SectorProgressRange> SegmentRanges(double startPct, double endPct)
    {
        var start = NormalizeProgress(startPct);
        var end = endPct >= 1d ? 1d : NormalizeProgress(endPct);
        if (end <= start && endPct < 1d)
        {
            yield return new SectorProgressRange(start, 1d);
            yield return new SectorProgressRange(0d, end);
            yield break;
        }

        yield return new SectorProgressRange(start, Math.Clamp(end, 0d, 1d));
    }

    private static void FillGeometry(
        Graphics graphics,
        TrackMapGeometry geometry,
        TrackMapTransform transform,
        Brush brush)
    {
        if (!geometry.Closed || geometry.Points.Count < 3)
        {
            return;
        }

        using var path = GeometryPath(geometry, transform);
        graphics.FillPath(brush, path);
    }

    private static GraphicsPath GeometryPath(TrackMapGeometry geometry, TrackMapTransform transform)
    {
        var path = new GraphicsPath();
        var previous = transform.Map(geometry.Points[0]);
        for (var index = 1; index < geometry.Points.Count; index++)
        {
            var current = transform.Map(geometry.Points[index]);
            path.AddLine(previous, current);
            previous = current;
        }

        if (geometry.Closed)
        {
            path.CloseFigure();
        }

        return path;
    }

    private static void DrawMarkers(
        Graphics graphics,
        IReadOnlyList<TrackMapMarker> markers,
        Func<TrackMapMarker, PointF?> pointForMarker)
    {
        foreach (var marker in markers
            .OrderBy(marker => marker.IsFocus)
            .ThenBy(marker => marker.CarIdx))
        {
            if (pointForMarker(marker) is not { } point)
            {
                continue;
            }

            var positionLabel = marker.IsFocus ? marker.PositionLabel : null;
            using var positionFont = positionLabel is { Length: > 0 }
                ? OverlayTheme.Font(OverlayTheme.DefaultFontFamily, FocusMarkerTextSize, FontStyle.Bold)
                : null;
            var radius = positionFont is not null
                ? FocusMarkerRadiusForLabel(graphics.MeasureString(positionLabel, positionFont))
                : marker.IsFocus ? FocusMarkerRadius : MarkerRadius;
            using var brush = new SolidBrush(marker.Color);
            using var border = new Pen(MarkerBorderColor, marker.IsFocus ? 2f : 1.4f);
            var markerRect = new RectangleF(point.X - radius, point.Y - radius, radius * 2f, radius * 2f);
            graphics.FillEllipse(brush, markerRect);
            graphics.DrawEllipse(border, markerRect);
            if (positionLabel is { Length: > 0 } && positionFont is not null)
            {
                DrawFocusPositionText(graphics, positionLabel, positionFont, markerRect);
            }
        }
    }

    private static float FocusMarkerRadiusForLabel(SizeF textSize)
    {
        return Math.Max(FocusMarkerRadius, Math.Max(textSize.Width, textSize.Height) / 2f + 3.5f);
    }

    private static void DrawFocusPositionText(Graphics graphics, string label, Font font, RectangleF markerRect)
    {
        using var textBrush = new SolidBrush(Color.FromArgb(255, 5, 12, 16));
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };
        graphics.DrawString(label, font, textBrush, markerRect, format);
    }

    private static PointF? PointOnGeometry(
        TrackMapGeometry geometry,
        TrackMapTransform transform,
        double progress)
    {
        var points = geometry.Points;
        if (points.Count == 0)
        {
            return null;
        }

        if (points.Count == 1)
        {
            return transform.Map(points[0]);
        }

        var target = NormalizeProgress(progress);
        for (var index = 1; index < points.Count; index++)
        {
            var previous = points[index - 1];
            var current = points[index];
            if (target < previous.LapDistPct || target > current.LapDistPct)
            {
                continue;
            }

            return Interpolate(previous, current, target, transform);
        }

        if (geometry.Closed)
        {
            var previous = points[^1];
            var current = points[0] with { LapDistPct = points[0].LapDistPct + 1d };
            var adjustedTarget = target < previous.LapDistPct ? target + 1d : target;
            return Interpolate(previous, current, adjustedTarget, transform);
        }

        return transform.Map(points.MinBy(point => Math.Abs(point.LapDistPct - target)) ?? points[0]);
    }

    private static PointF Interpolate(
        TrackMapPoint previous,
        TrackMapPoint current,
        double target,
        TrackMapTransform transform)
    {
        var span = current.LapDistPct - previous.LapDistPct;
        var ratio = span <= 0d ? 0d : Math.Clamp((target - previous.LapDistPct) / span, 0d, 1d);
        return transform.Map(new TrackMapPoint(
            target,
            previous.X + (current.X - previous.X) * ratio,
            previous.Y + (current.Y - previous.Y) * ratio));
    }

    private static PointF PointOnEllipse(RectangleF rect, double progress)
    {
        var angle = (NormalizeProgress(progress) * Math.PI * 2d) - (Math.PI / 2d);
        return new PointF(
            rect.Left + rect.Width / 2f + (float)Math.Cos(angle) * rect.Width / 2f,
            rect.Top + rect.Height / 2f + (float)Math.Sin(angle) * rect.Height / 2f);
    }

    private IReadOnlyList<TrackMapMarker> SmoothMarkers(IReadOnlyList<TrackMapMarker> markers)
    {
        if (markers.Count == 0)
        {
            _smoothedMarkerProgress.Clear();
            _lastMarkerSmoothingAtUtc = null;
            return markers;
        }

        var now = DateTimeOffset.UtcNow;
        var elapsedSeconds = _lastMarkerSmoothingAtUtc is { } previous
            ? Math.Clamp((now - previous).TotalSeconds, 0d, 0.25d)
            : RefreshIntervalMilliseconds / 1000d;
        _lastMarkerSmoothingAtUtc = now;
        var alpha = 1d - Math.Exp(-elapsedSeconds / MarkerSmoothingSeconds);
        var activeCarIdxs = markers.Select(marker => marker.CarIdx).ToHashSet();
        foreach (var carIdx in _smoothedMarkerProgress.Keys.Except(activeCarIdxs).ToArray())
        {
            _smoothedMarkerProgress.Remove(carIdx);
        }

        return markers
            .Select(marker =>
            {
                if (!_smoothedMarkerProgress.TryGetValue(marker.CarIdx, out var current))
                {
                    _smoothedMarkerProgress[marker.CarIdx] = marker.LapDistPct;
                    return marker;
                }

                var smoothed = SmoothProgress(current, marker.LapDistPct, alpha);
                _smoothedMarkerProgress[marker.CarIdx] = smoothed;
                return marker with { LapDistPct = smoothed };
            })
            .ToArray();
    }

    private static double SmoothProgress(double current, double target, double alpha)
    {
        var delta = target - current;
        if (delta > 0.5d)
        {
            delta -= 1d;
        }
        else if (delta < -0.5d)
        {
            delta += 1d;
        }

        return NormalizeProgress(current + delta * Math.Clamp(alpha, 0d, 1d));
    }

    private static TrackMapMarker ToTrackMapMarker(TrackMapOverlayMarker marker)
    {
        return new TrackMapMarker(
            marker.CarIdx,
            marker.LapDistPct,
            marker.IsFocus,
            MarkerColor(marker.ClassColorHex, marker.IsFocus),
            FormatPositionLabel(marker.Position));
    }

    private static string? FormatPositionLabel(int? position)
    {
        return position is > 0 ? $"P{position.Value}" : null;
    }

    private static Color MarkerColor(string? classColorHex, bool isFocus)
    {
        if (isFocus)
        {
            return FocusMarkerColor;
        }

        return OverlayClassColor.TryParseWithAlpha(classColorHex, 245) ?? DefaultMarkerColor;
    }

    private static double NormalizeProgress(double value)
    {
        if (!IsFinite(value))
        {
            return 0d;
        }

        var normalized = value % 1d;
        return normalized < 0d ? normalized + 1d : normalized;
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }

    private Brush TrackInteriorBrush()
    {
        var opacity = TrackMapOverlayViewModel.ClampInternalOpacity(_viewModel.InternalOpacity);
        var alpha = (int)Math.Round(TrackInteriorMaximumAlpha * opacity);
        return new SolidBrush(Color.FromArgb(alpha, TrackInteriorColor.R, TrackInteriorColor.G, TrackInteriorColor.B));
    }

    private void ReportOverlayError(Exception exception, string stage)
    {
        var now = DateTimeOffset.UtcNow;
        var message = exception.GetType().Name;
        if (string.Equals(_lastLoggedError, message, StringComparison.Ordinal)
            && _lastLoggedErrorAtUtc is { } lastLogged
            && now - lastLogged <= TimeSpan.FromSeconds(30))
        {
            return;
        }

        _logger.LogWarning(exception, "Track map overlay {Stage} failed.", stage);
        _lastLoggedError = message;
        _lastLoggedErrorAtUtc = now;
    }

    private sealed record TrackMapMarker(
        int CarIdx,
        double LapDistPct,
        bool IsFocus,
        Color Color,
        string? PositionLabel);

    private readonly record struct SectorBoundaryTick(PointF Start, PointF End);

    private readonly record struct SectorProgressRange(double StartPct, double EndPct);

    private sealed record TrackMapTransform(
        double MinX,
        double MaxY,
        double Scale,
        float Left,
        float Top,
        float Width,
        float Height)
    {
        public static TrackMapTransform? From(TrackMapDocument document, RectangleF bounds)
        {
            var points = document.RacingLine.Points
                .Concat(document.PitLane?.Points ?? [])
                .Where(point => IsFinite(point.X) && IsFinite(point.Y))
                .ToArray();
            if (points.Length == 0)
            {
                return null;
            }

            var minX = points.Min(point => point.X);
            var maxX = points.Max(point => point.X);
            var minY = points.Min(point => point.Y);
            var maxY = points.Max(point => point.Y);
            var geometryWidth = Math.Max(1d, maxX - minX);
            var geometryHeight = Math.Max(1d, maxY - minY);
            var scale = Math.Min(bounds.Width / geometryWidth, bounds.Height / geometryHeight);
            if (!IsFinite(scale) || scale <= 0d)
            {
                return null;
            }

            var renderedWidth = (float)(geometryWidth * scale);
            var renderedHeight = (float)(geometryHeight * scale);
            return new TrackMapTransform(
                MinX: minX,
                MaxY: maxY,
                Scale: scale,
                Left: bounds.Left + (bounds.Width - renderedWidth) / 2f,
                Top: bounds.Top + (bounds.Height - renderedHeight) / 2f,
                Width: renderedWidth,
                Height: renderedHeight);
        }

        public PointF Map(TrackMapPoint point)
        {
            return new PointF(
                Left + (float)((point.X - MinX) * Scale),
                Top + (float)((MaxY - point.Y) * Scale));
        }
    }
}
