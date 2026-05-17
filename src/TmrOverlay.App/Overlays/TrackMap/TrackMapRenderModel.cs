using System.Drawing;
using TmrOverlay.App.Overlays.Styling;
using TmrOverlay.Core.Telemetry.Live;
using TmrOverlay.Core.TrackMaps;

namespace TmrOverlay.App.Overlays.TrackMap;

internal sealed class TrackMapRenderModelBuilder
{
    private const int OffTrackSurface = 0;
    private const int OnTrackSurface = 3;
    private static readonly TimeSpan OffTrackFlashDuration = TimeSpan.FromSeconds(2.5d);

    private readonly Dictionary<int, double> _smoothedMarkerProgress = [];
    private readonly Dictionary<int, int?> _lastMarkerTrackSurface = [];
    private readonly Dictionary<int, DateTimeOffset> _offTrackFlashUntilUtc = [];
    private DateTimeOffset? _lastSmoothingAtUtc;
    private string? _lastTrackMapKey;

    public void ResetSmoothing()
    {
        _smoothedMarkerProgress.Clear();
        _lastMarkerTrackSurface.Clear();
        _offTrackFlashUntilUtc.Clear();
        _lastSmoothingAtUtc = null;
        _lastTrackMapKey = null;
    }

    public TrackMapRenderModel Build(TrackMapOverlayViewModel viewModel, DateTimeOffset now)
    {
        var trackMapKey = viewModel.TrackMap?.Identity.Key;
        if (!string.Equals(trackMapKey, _lastTrackMapKey, StringComparison.Ordinal))
        {
            _smoothedMarkerProgress.Clear();
            _lastMarkerTrackSurface.Clear();
            _offTrackFlashUntilUtc.Clear();
            _lastSmoothingAtUtc = null;
            _lastTrackMapKey = trackMapKey;
        }

        var markers = SmoothMarkers(viewModel.Markers, now);
        markers = ApplyOffTrackFlash(markers, now);
        return TrackMapRenderModel.FromViewModel(viewModel, markers);
    }

    private IReadOnlyList<TrackMapOverlayMarker> SmoothMarkers(IReadOnlyList<TrackMapOverlayMarker> markers, DateTimeOffset now)
    {
        if (markers.Count == 0)
        {
            _smoothedMarkerProgress.Clear();
            _lastMarkerTrackSurface.Clear();
            _offTrackFlashUntilUtc.Clear();
            _lastSmoothingAtUtc = null;
            return markers;
        }

        var elapsed = _lastSmoothingAtUtc is { } previous
            ? Math.Clamp((now - previous).TotalSeconds, 0d, 0.25d)
            : TrackMapRenderModel.RefreshIntervalMilliseconds / 1000d;
        _lastSmoothingAtUtc = now;
        var alpha = 1d - Math.Exp(-elapsed / TrackMapRenderModel.MarkerSmoothingSeconds);
        var activeCarIdxs = markers.Select(marker => marker.CarIdx).ToHashSet();
        foreach (var carIdx in _smoothedMarkerProgress.Keys.Where(carIdx => !activeCarIdxs.Contains(carIdx)).ToArray())
        {
            _smoothedMarkerProgress.Remove(carIdx);
            _lastMarkerTrackSurface.Remove(carIdx);
            _offTrackFlashUntilUtc.Remove(carIdx);
        }

        return markers
            .Select(marker =>
            {
                if (!_smoothedMarkerProgress.TryGetValue(marker.CarIdx, out var current))
                {
                    _smoothedMarkerProgress[marker.CarIdx] = marker.LapDistPct;
                    return marker;
                }

                var smoothed = NormalizeProgress(current + ProgressDelta(current, marker.LapDistPct) * Math.Clamp(alpha, 0d, 1d));
                _smoothedMarkerProgress[marker.CarIdx] = smoothed;
                return marker with { LapDistPct = smoothed };
            })
            .ToArray();
    }

    private IReadOnlyList<TrackMapOverlayMarker> ApplyOffTrackFlash(IReadOnlyList<TrackMapOverlayMarker> markers, DateTimeOffset now)
    {
        if (markers.Count == 0)
        {
            return markers;
        }

        foreach (var marker in markers)
        {
            if (!marker.IsFocus
                && _lastMarkerTrackSurface.TryGetValue(marker.CarIdx, out var previous)
                && previous == OnTrackSurface
                && marker.TrackSurface == OffTrackSurface)
            {
                _offTrackFlashUntilUtc[marker.CarIdx] = now.Add(OffTrackFlashDuration);
            }

            _lastMarkerTrackSurface[marker.CarIdx] = marker.TrackSurface;
        }

        foreach (var carIdx in _offTrackFlashUntilUtc.Keys.Where(carIdx => _offTrackFlashUntilUtc[carIdx] <= now).ToArray())
        {
            _offTrackFlashUntilUtc.Remove(carIdx);
        }

        return markers
            .Select(marker =>
            {
                if (marker.IsFocus || !_offTrackFlashUntilUtc.TryGetValue(marker.CarIdx, out var flashUntil))
                {
                    return marker;
                }

                var remaining = Math.Clamp((flashUntil - now).TotalSeconds, 0d, OffTrackFlashDuration.TotalSeconds);
                var progress = 1d - remaining / OffTrackFlashDuration.TotalSeconds;
                return marker with
                {
                    AlertKind = TrackMapMarkerAlertKind.OffTrack,
                    AlertPulseProgress = Math.Clamp(progress, 0d, 1d)
                };
            })
            .ToArray();
    }

    private static double ProgressDelta(double current, double target)
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

        return delta;
    }

    private static double NormalizeProgress(double value)
    {
        if (!TrackMapRenderModel.IsFinite(value))
        {
            return 0d;
        }

        var normalized = value % 1d;
        return normalized < 0d ? normalized + 1d : normalized;
    }
}

internal sealed record TrackMapRenderModel(
    double Width,
    double Height,
    bool IsAvailable,
    string MapKind,
    IReadOnlyList<TrackMapRenderPrimitive> Primitives,
    IReadOnlyList<TrackMapRenderMarker> Markers)
{
    public const int RefreshIntervalMilliseconds = 100;
    public const double MarkerSmoothingSeconds = 0.14d;
    public const double DesignWidth = 360d;
    public const double DesignHeight = 360d;

    private const double MapPadding = 20d;
    private const double TrackHaloWidth = 11d;
    private const double TrackLineWidth = 4.4d;
    private const double SectorHighlightWidth = 5.8d;
    private const double SectorBoundaryTickLength = 17d;
    private const double NormalBoundaryTickWidth = 2.2d;
    private const double StartFinishShadowWidth = 5.8d;
    private const double StartFinishMainWidth = 3.2d;
    private const double StartFinishHighlightWidth = 1.2d;
    private const double PitLineWidth = 2.2d;
    private const double MarkerRadius = 3.6d;
    private const double MarkerLabelRadius = 4.8d;
    private const double FocusMarkerRadius = 5.7d;
    private const double NonFocusMarkerLabelRadius = 5.7d;
    private const double NonFocusMarkerRadiusReduction = 2d;
    private const double MarkerTextSize = 5.4d;
    private const double FocusMarkerTextSize = 7.6d;
    private const double OffTrackAlertRingMinimumGap = 3.2d;
    private const double OffTrackAlertRingExpansion = 5.2d;
    private const double OffTrackAlertRingStrokeWidth = 2.3d;
    private static readonly Color OffTrackAlertMarkerColor = Color.FromArgb(255, 255, 218, 89);
    private static readonly Color OffTrackAlertRingColor = Color.FromArgb(255, 255, 247, 210);
    private const int TrackInteriorMaximumAlpha = 150;
    private static readonly TrackMapRenderRect TrackRect = new(
        MapPadding,
        MapPadding,
        DesignWidth - MapPadding * 2d,
        DesignHeight - MapPadding * 2d);

    public static TrackMapRenderModel Empty { get; } = new(
        DesignWidth,
        DesignHeight,
        IsAvailable: false,
        MapKind: "circle",
        Primitives: CirclePrimitives([], TrackMapBrowserSettings.Default.InternalOpacity, showSectorBoundaries: true),
        Markers: []);

    public static TrackMapRenderModel FromViewModel(
        TrackMapOverlayViewModel viewModel,
        IReadOnlyList<TrackMapOverlayMarker>? markers = null)
    {
        markers ??= viewModel.Markers;
        var transform = viewModel.TrackMap?.RacingLine.Points is { Count: >= 3 } && viewModel.TrackMap is { } document
            ? TrackMapTransform.From(document, TrackRect)
            : null;
        var hasGeneratedMap = transform is not null && viewModel.TrackMap is not null;

        var primitives = hasGeneratedMap && transform is not null && viewModel.TrackMap is not null
            ? GeneratedPrimitives(
                viewModel.TrackMap,
                transform,
                viewModel.Sectors,
                viewModel.InternalOpacity,
                viewModel.ShowSectorBoundaries)
            : CirclePrimitives(
                viewModel.Sectors,
                viewModel.InternalOpacity,
                viewModel.ShowSectorBoundaries);
        var renderMarkers = markers
            .OrderBy(marker => marker.IsFocus)
            .ThenBy(marker => marker.CarIdx)
            .Select(marker => RenderMarker(marker, viewModel.TrackMap, transform))
            .ToArray();

        return new TrackMapRenderModel(
            DesignWidth,
            DesignHeight,
            viewModel.IsAvailable,
            hasGeneratedMap ? "generated" : "circle",
            primitives,
            renderMarkers);
    }

    internal static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }

    private static IReadOnlyList<TrackMapRenderPrimitive> GeneratedPrimitives(
        TrackMapDocument document,
        TrackMapTransform transform,
        IReadOnlyList<LiveTrackSectorSegment> sectors,
        double internalOpacity,
        bool showSectorBoundaries)
    {
        var primitives = new List<TrackMapRenderPrimitive>();
        var racingLine = RenderPath(document.RacingLine, transform);
        if (document.RacingLine.Closed && racingLine.Count >= 3)
        {
            primitives.Add(TrackMapRenderPrimitive.Path(
                racingLine,
                Closed: true,
                Fill: TrackInteriorFill(internalOpacity)));
        }

        primitives.Add(TrackMapRenderPrimitive.Path(
            racingLine,
            Closed: document.RacingLine.Closed,
            Stroke: ColorOf(OverlayTheme.DesignV2.TrackHalo),
            StrokeWidth: TrackHaloWidth));
        primitives.Add(TrackMapRenderPrimitive.Path(
            racingLine,
            Closed: document.RacingLine.Closed,
            Stroke: ColorOf(OverlayTheme.DesignV2.TrackLine),
            StrokeWidth: TrackLineWidth));

        foreach (var sector in sectors.Where(HasHighlight))
        {
            foreach (var range in SegmentRanges(sector.StartPct, sector.EndPct))
            {
                var segment = RenderGeometrySegment(document.RacingLine, transform, range.StartPct, range.EndPct);
                if (segment.Count >= 2)
                {
                    primitives.Add(TrackMapRenderPrimitive.Path(
                        segment,
                        Closed: false,
                        Stroke: SectorHighlightColor(sector.Highlight),
                        StrokeWidth: SectorHighlightWidth));
                }
            }
        }

        if (showSectorBoundaries)
        {
            foreach (var boundary in BoundaryMarkers(sectors))
            {
                if (GeometryBoundaryTick(document.RacingLine, transform, boundary.Progress) is { } tick)
                {
                    AddBoundaryLines(
                        primitives,
                        tick.Start,
                        tick.End,
                        IsStartFinishProgress(boundary.Progress),
                        boundary.Highlight);
                }
            }
        }

        if (document.PitLane is { Points.Count: >= 2 } pitLane)
        {
            primitives.Add(TrackMapRenderPrimitive.Path(
                RenderPath(pitLane, transform),
                Closed: false,
                Stroke: ColorOf(OverlayTheme.DesignV2.PitLine),
                StrokeWidth: PitLineWidth));
        }

        return primitives;
    }

    private static IReadOnlyList<TrackMapRenderPrimitive> CirclePrimitives(
        IReadOnlyList<LiveTrackSectorSegment> sectors,
        double internalOpacity,
        bool showSectorBoundaries)
    {
        var primitives = new List<TrackMapRenderPrimitive>
        {
            TrackMapRenderPrimitive.Ellipse(TrackRect, Fill: TrackInteriorFill(internalOpacity)),
            TrackMapRenderPrimitive.Ellipse(
                TrackRect,
                Stroke: ColorOf(OverlayTheme.DesignV2.TrackHalo),
                StrokeWidth: TrackHaloWidth),
            TrackMapRenderPrimitive.Ellipse(
                TrackRect,
                Stroke: ColorOf(OverlayTheme.DesignV2.TrackLine),
                StrokeWidth: TrackLineWidth)
        };

        foreach (var sector in sectors.Where(HasHighlight))
        {
            foreach (var range in SegmentRanges(sector.StartPct, sector.EndPct))
            {
                var sweep = (range.EndPct - range.StartPct) * 360d;
                if (sweep > 0d)
                {
                    primitives.Add(TrackMapRenderPrimitive.Arc(
                        TrackRect,
                        StartDegrees: range.StartPct * 360d - 90d,
                        SweepDegrees: sweep,
                        Stroke: SectorHighlightColor(sector.Highlight),
                        StrokeWidth: SectorHighlightWidth));
                }
            }
        }

        if (showSectorBoundaries)
        {
            var center = new TrackMapRenderPoint(
                TrackRect.X + TrackRect.Width / 2d,
                TrackRect.Y + TrackRect.Height / 2d);
            foreach (var boundary in BoundaryMarkers(sectors))
            {
                var progress = boundary.Progress;
                var point = PointOnEllipse(progress);
                var dx = point.X - center.X;
                var dy = point.Y - center.Y;
                var length = Math.Max(0.001d, Math.Sqrt(dx * dx + dy * dy));
                var unitX = dx / length;
                var unitY = dy / length;
                var half = BoundaryTickLength(progress) / 2d;
                AddBoundaryLines(
                    primitives,
                    new TrackMapRenderPoint(point.X - unitX * half, point.Y - unitY * half),
                    new TrackMapRenderPoint(point.X + unitX * half, point.Y + unitY * half),
                    IsStartFinishProgress(progress),
                    boundary.Highlight);
            }
        }

        return primitives;
    }

    private static TrackMapRenderMarker RenderMarker(
        TrackMapOverlayMarker marker,
        TrackMapDocument? document,
        TrackMapTransform? transform)
    {
        var point = document?.RacingLine.Points is { Count: >= 3 } && transform is not null
            ? PointOnGeometry(document.RacingLine, transform, marker.LapDistPct) ?? PointOnEllipse(marker.LapDistPct)
            : PointOnEllipse(marker.LapDistPct);
        var label = marker.Position is > 0
            ? marker.Position.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : null;
        var radius = label is { Length: > 0 }
            ? MarkerRadiusForLabel(label, marker.IsFocus)
            : marker.IsFocus ? FocusMarkerRadius : Math.Max(1.2d, MarkerRadius - NonFocusMarkerRadiusReduction);
        var labelFontSize = marker.IsFocus ? FocusMarkerTextSize : MarkerTextSize;
        var alertActive = marker.AlertKind == TrackMapMarkerAlertKind.OffTrack;
        var alertPulseProgress = alertActive
            ? Math.Clamp(marker.AlertPulseProgress, 0d, 1d)
            : 0d;
        var alertRingRadius = alertActive
            ? radius + OffTrackAlertRingMinimumGap + OffTrackAlertRingExpansion * alertPulseProgress
            : 0d;
        var alertRingAlpha = alertActive
            ? (int)Math.Round(230d - alertPulseProgress * 140d)
            : 0;

        return new TrackMapRenderMarker(
            marker.CarIdx,
            point.X,
            point.Y,
            radius,
            marker.IsFocus,
            Fill: MarkerColor(marker.ClassColorHex, marker.IsFocus, marker.AlertKind),
            Stroke: ColorOf(OverlayTheme.DesignV2.TrackMarkerBorder),
            StrokeWidth: marker.IsFocus ? 2d : 1.4d,
            Label: label,
            LabelColor: ColorOf(Color.FromArgb(255, 5, 13, 17)),
            LabelFontSize: labelFontSize,
            AlertKind: alertActive ? "off-track" : null,
            AlertPulseProgress: alertPulseProgress,
            AlertRingRadius: alertRingRadius,
            AlertRingStroke: alertActive
                ? ColorOf(Color.FromArgb(Math.Clamp(alertRingAlpha, 0, 255), OffTrackAlertRingColor))
                : null,
            AlertRingStrokeWidth: OffTrackAlertRingStrokeWidth);
    }

    private static IReadOnlyList<TrackMapRenderPoint> RenderPath(TrackMapGeometry geometry, TrackMapTransform transform)
    {
        return geometry.Points
            .Where(point => IsFinite(point.X) && IsFinite(point.Y))
            .Select(transform.Map)
            .ToArray();
    }

    private static IReadOnlyList<TrackMapRenderPoint> RenderGeometrySegment(
        TrackMapGeometry geometry,
        TrackMapTransform transform,
        double startPct,
        double endPct)
    {
        if (geometry.Points.Count < 2 || endPct <= startPct)
        {
            return [];
        }

        var startPoint = PointOnGeometry(geometry, transform, startPct);
        var endPoint = PointOnGeometry(geometry, transform, endPct);
        if (startPoint is null || endPoint is null)
        {
            return [];
        }

        var points = new List<TrackMapRenderPoint> { startPoint.Value };
        points.AddRange(geometry.Points
            .Where(point => point.LapDistPct > startPct && point.LapDistPct < endPct)
            .Select(transform.Map));
        points.Add(endPoint.Value);
        return points;
    }

    private static TrackMapBoundaryTick? GeometryBoundaryTick(
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
        var length = Math.Max(0.001d, Math.Sqrt(dx * dx + dy * dy));
        var normalX = -dy / length;
        var normalY = dx / length;
        var half = BoundaryTickLength(progress) / 2d;
        return new TrackMapBoundaryTick(
            new TrackMapRenderPoint(center.Value.X - normalX * half, center.Value.Y - normalY * half),
            new TrackMapRenderPoint(center.Value.X + normalX * half, center.Value.Y + normalY * half));
    }

    private static void AddBoundaryLines(
        List<TrackMapRenderPrimitive> primitives,
        TrackMapRenderPoint start,
        TrackMapRenderPoint end,
        bool isStartFinish,
        string highlight)
    {
        if (isStartFinish)
        {
            var mainColor = BoundaryHighlightColor(highlight)
                ?? ColorOf(OverlayTheme.DesignV2.StartFinishBoundary);
            primitives.Add(TrackMapRenderPrimitive.Line(
                start,
                end,
                ColorOf(OverlayTheme.DesignV2.StartFinishBoundaryShadow),
                StartFinishShadowWidth));
            primitives.Add(TrackMapRenderPrimitive.Line(
                start,
                end,
                mainColor,
                StartFinishMainWidth));
            primitives.Add(TrackMapRenderPrimitive.Line(
                start,
                end,
                ColorOf(Color.FromArgb(235, 255, 247, 255)),
                StartFinishHighlightWidth));
            return;
        }

        primitives.Add(TrackMapRenderPrimitive.Line(
            start,
            end,
            BoundaryHighlightColor(highlight) ?? ColorOf(OverlayTheme.DesignV2.Cyan),
            NormalBoundaryTickWidth));
    }

    private static TrackMapRenderPoint? PointOnGeometry(
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
            if (target >= previous.LapDistPct && target <= current.LapDistPct)
            {
                return Interpolate(previous, current, target, transform);
            }
        }

        if (geometry.Closed)
        {
            var previous = points[^1];
            var current = points[0] with { LapDistPct = points[0].LapDistPct + 1d };
            var adjusted = target < previous.LapDistPct ? target + 1d : target;
            return Interpolate(previous, current, adjusted, transform);
        }

        return transform.Map(points.MinBy(point => Math.Abs(point.LapDistPct - target)) ?? points[0]);
    }

    private static TrackMapRenderPoint Interpolate(
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

    private static TrackMapRenderPoint PointOnEllipse(double progress)
    {
        var angle = NormalizeProgress(progress) * Math.PI * 2d - Math.PI / 2d;
        return new TrackMapRenderPoint(
            TrackRect.X + TrackRect.Width / 2d + Math.Cos(angle) * TrackRect.Width / 2d,
            TrackRect.Y + TrackRect.Height / 2d + Math.Sin(angle) * TrackRect.Height / 2d);
    }

    private static IEnumerable<TrackMapProgressRange> SegmentRanges(double startPct, double endPct)
    {
        var start = NormalizeProgress(startPct);
        var end = endPct >= 1d ? 1d : NormalizeProgress(endPct);
        if (end <= start && endPct < 1d)
        {
            yield return new TrackMapProgressRange(start, 1d);
            yield return new TrackMapProgressRange(0d, end);
            yield break;
        }

        yield return new TrackMapProgressRange(start, Math.Clamp(end, 0d, 1d));
    }

    private static IEnumerable<TrackMapBoundaryMarker> BoundaryMarkers(IReadOnlyList<LiveTrackSectorSegment> sectors)
    {
        if (sectors.Count < 2)
        {
            yield break;
        }

        var seen = new HashSet<int>();
        foreach (var sector in sectors)
        {
            var progress = sector.EndPct >= 1d ? 1d : NormalizeProgress(sector.EndPct);
            var key = (int)Math.Round(NormalizeProgress(progress) * 100_000d);
            if (seen.Add(key))
            {
                yield return new TrackMapBoundaryMarker(progress, sector.BoundaryHighlight);
            }
        }
    }

    private static bool HasHighlight(LiveTrackSectorSegment sector)
    {
        return string.Equals(sector.Highlight, LiveTrackSectorHighlights.PersonalBest, StringComparison.Ordinal)
            || string.Equals(sector.Highlight, LiveTrackSectorHighlights.BestLap, StringComparison.Ordinal);
    }

    private static TrackMapRenderColor SectorHighlightColor(string highlight)
    {
        return string.Equals(highlight, LiveTrackSectorHighlights.BestLap, StringComparison.Ordinal)
            ? ColorOf(OverlayTheme.DesignV2.BestLapSector)
            : ColorOf(OverlayTheme.DesignV2.PersonalBestSector);
    }

    private static TrackMapRenderColor? BoundaryHighlightColor(string? highlight)
    {
        if (string.Equals(highlight, LiveTrackSectorHighlights.PersonalBest, StringComparison.Ordinal))
        {
            return ColorOf(OverlayTheme.DesignV2.PersonalBestSector);
        }

        return string.Equals(highlight, LiveTrackSectorHighlights.BestLap, StringComparison.Ordinal)
            ? ColorOf(OverlayTheme.DesignV2.BestLapSector)
            : null;
    }

    private static TrackMapRenderColor MarkerColor(string? classColorHex, bool isFocus, TrackMapMarkerAlertKind alertKind)
    {
        if (alertKind == TrackMapMarkerAlertKind.OffTrack)
        {
            return ColorOf(OffTrackAlertMarkerColor);
        }

        if (isFocus)
        {
            return ColorOf(OverlayTheme.DesignV2.Cyan);
        }

        var color = OverlayClassColor.TryParseWithAlpha(classColorHex, 245)
            ?? Color.FromArgb(245, 237, 245, 250);
        return ColorOf(color);
    }

    private static TrackMapRenderColor? TrackInteriorFill(double opacity)
    {
        var trackInterior = OverlayTheme.DesignV2.TrackInterior;
        var alpha = (int)Math.Round(TrackInteriorMaximumAlpha * TrackMapOverlayViewModel.ClampInternalOpacity(opacity));
        if (alpha <= 0)
        {
            return null;
        }

        return new TrackMapRenderColor(trackInterior.R, trackInterior.G, trackInterior.B, alpha);
    }

    private static TrackMapRenderColor ColorOf(Color color)
    {
        return new TrackMapRenderColor(color.R, color.G, color.B, color.A);
    }

    private static double MarkerRadiusForLabel(string label, bool isFocus)
    {
        return isFocus
            ? Math.Max(FocusMarkerRadius, 5.1d + label.Length * 2.9d)
            : Math.Max(NonFocusMarkerLabelRadius, 3.9d + Math.Max(0, label.Length - 2) * 1.9d);
    }

    private static double BoundaryTickLength(double progress)
    {
        return IsStartFinishProgress(progress)
            ? SectorBoundaryTickLength * 1.45d
            : SectorBoundaryTickLength;
    }

    private static bool IsStartFinishProgress(double progress)
    {
        var normalized = NormalizeProgress(progress);
        return normalized <= 0.0005d || normalized >= 0.9995d;
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

    private readonly record struct TrackMapBoundaryTick(TrackMapRenderPoint Start, TrackMapRenderPoint End);

    private readonly record struct TrackMapBoundaryMarker(double Progress, string Highlight);

    private readonly record struct TrackMapProgressRange(double StartPct, double EndPct);

    private sealed record TrackMapTransform(
        double MinX,
        double MaxY,
        double Scale,
        double Left,
        double Top,
        double Width,
        double Height)
    {
        public static TrackMapTransform? From(TrackMapDocument document, TrackMapRenderRect bounds)
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

            var renderedWidth = geometryWidth * scale;
            var renderedHeight = geometryHeight * scale;
            return new TrackMapTransform(
                minX,
                maxY,
                scale,
                bounds.X + (bounds.Width - renderedWidth) / 2d,
                bounds.Y + (bounds.Height - renderedHeight) / 2d,
                renderedWidth,
                renderedHeight);
        }

        public TrackMapRenderPoint Map(TrackMapPoint point)
        {
            return new TrackMapRenderPoint(
                Left + (point.X - MinX) * Scale,
                Top + (MaxY - point.Y) * Scale);
        }
    }
}

internal sealed record TrackMapRenderPrimitive(
    string Kind,
    IReadOnlyList<TrackMapRenderPoint> Points,
    bool Closed,
    TrackMapRenderRect? Rect,
    double StartDegrees,
    double SweepDegrees,
    TrackMapRenderColor? Fill,
    TrackMapRenderColor? Stroke,
    double StrokeWidth)
{
    public static TrackMapRenderPrimitive Path(
        IReadOnlyList<TrackMapRenderPoint> points,
        bool Closed,
        TrackMapRenderColor? Fill = null,
        TrackMapRenderColor? Stroke = null,
        double StrokeWidth = 0d)
    {
        return new TrackMapRenderPrimitive("path", points, Closed, null, 0d, 0d, Fill, Stroke, StrokeWidth);
    }

    public static TrackMapRenderPrimitive Ellipse(
        TrackMapRenderRect rect,
        TrackMapRenderColor? Fill = null,
        TrackMapRenderColor? Stroke = null,
        double StrokeWidth = 0d)
    {
        return new TrackMapRenderPrimitive("ellipse", [], Closed: false, rect, 0d, 0d, Fill, Stroke, StrokeWidth);
    }

    public static TrackMapRenderPrimitive Arc(
        TrackMapRenderRect rect,
        double StartDegrees,
        double SweepDegrees,
        TrackMapRenderColor Stroke,
        double StrokeWidth)
    {
        return new TrackMapRenderPrimitive("arc", [], Closed: false, rect, StartDegrees, SweepDegrees, null, Stroke, StrokeWidth);
    }

    public static TrackMapRenderPrimitive Line(
        TrackMapRenderPoint start,
        TrackMapRenderPoint end,
        TrackMapRenderColor Stroke,
        double StrokeWidth)
    {
        return new TrackMapRenderPrimitive("line", [start, end], Closed: false, null, 0d, 0d, null, Stroke, StrokeWidth);
    }
}

internal readonly record struct TrackMapRenderPoint(double X, double Y);

internal readonly record struct TrackMapRenderRect(double X, double Y, double Width, double Height);

internal sealed record TrackMapRenderMarker(
    int CarIdx,
    double X,
    double Y,
    double Radius,
    bool IsFocus,
    TrackMapRenderColor Fill,
    TrackMapRenderColor Stroke,
    double StrokeWidth,
    string? Label,
    TrackMapRenderColor LabelColor,
    double LabelFontSize,
    string? AlertKind = null,
    double AlertPulseProgress = 0d,
    double AlertRingRadius = 0d,
    TrackMapRenderColor? AlertRingStroke = null,
    double AlertRingStrokeWidth = 0d);

internal sealed record TrackMapRenderColor(int Red, int Green, int Blue, int Alpha);
