using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using Microsoft.Extensions.Logging;
using TmrOverlay.App.Overlays.Abstractions;
using TmrOverlay.App.Overlays.Styling;
using TmrOverlay.App.Performance;
using TmrOverlay.Core.Overlays;
using TmrOverlay.Core.Settings;
using TmrOverlay.Core.Telemetry.Live;

namespace TmrOverlay.App.Overlays.CarRadar;

internal sealed class CarRadarForm : PersistentOverlayForm
{
    private static readonly Color TransparentColor = Color.Black;
    private readonly double _configuredOpacity;
    private const double RadarRangeSeconds = 2d;
    private const double FocusedCarLengthMeters = 4.746d;
    private const double RadarRangeMeters = FocusedCarLengthMeters * 6d;
    private const double ContactWindowMeters = FocusedCarLengthMeters;
    private const double SideAttachmentWindowMeters = FocusedCarLengthMeters * 2d;
    private const double ProximityWarningGapMeters = 2.0d;
    private const double FadeInSeconds = 0.25d;
    private const double FadeOutSeconds = 0.85d;
    private const double MinimumVisibleAlpha = 0.02d;
    private const int RefreshIntervalMilliseconds = 100;
    private const int MaxWideRowRadarCars = 18;
    private const float MulticlassWarningArcStartDegrees = 62.5f;
    private const float MulticlassWarningArcSweepDegrees = 55f;
    private const float FocusedCarWidth = 20f;
    private const float FocusedCarHeight = 36f;
    private const float RadarCarWidth = 20f;
    private const float RadarCarHeight = 36f;
    private const float CarCornerRadius = 4f;
    private const float SeparatedCarPaddingPixels = 2f;
    private const float RadarEdgeCenterPaddingPixels = 2f;
    private const float GridRowReferenceMeters = 8f;
    private const float MinimumDistinctRowGapPixels = 48f;
    private const float WideRowBucketPixels = 30f;
    private const double WideRowLongitudinalBucketMeters = 2d;
    private const float WideRowSlotPitchPixels = 36f;
    private const double ProximityRedStart = 0.74d;
    private const float UsableRadarRadiusInset = RadarEdgeCenterPaddingPixels;
    private const float DistinctRowPixelsPerMeter = MinimumDistinctRowGapPixels / GridRowReferenceMeters;

    private readonly ILiveTelemetrySource _liveTelemetrySource;
    private readonly ILogger<CarRadarForm> _logger;
    private readonly AppPerformanceState _performanceState;
    private readonly System.Windows.Forms.Timer _refreshTimer;
    private readonly OverlaySettings _settings;
    private readonly string _fontFamily;
    private readonly Dictionary<int, RadarCarVisual> _carVisuals = [];
    private CarRadarOverlayViewModel _viewModel = CarRadarOverlayViewModel.Empty;
    private DateTimeOffset? _lastRefreshAtUtc;
    private double _radarAlpha;
    private double _leftSideAlpha;
    private double _rightSideAlpha;
    private bool _settingsPreviewVisible;
    private string? _overlayError;
    private string? _lastLoggedError;
    private DateTimeOffset? _lastLoggedErrorAtUtc;
    private long? _lastRefreshSequence;

    private bool ShowMulticlassWarning =>
        _settings.GetBooleanOption(OverlayOptionKeys.RadarMulticlassWarning, defaultValue: true);

    private int MulticlassWarningRangeSeconds =>
        _settings.GetIntegerOption(
            OverlayOptionKeys.RadarMulticlassWarningSeconds,
            CarRadarOverlayViewModel.DefaultMulticlassWarningRangeSeconds,
            CarRadarOverlayViewModel.MinimumMulticlassWarningRangeSeconds,
            CarRadarOverlayViewModel.MaximumMulticlassWarningRangeSeconds);

    private int RadarVisibilitySeconds =>
        _settings.GetIntegerOption(
            OverlayOptionKeys.RadarVisibilitySeconds,
            CarRadarOverlayViewModel.DefaultRadarVisibilitySeconds,
            CarRadarOverlayViewModel.MinimumRadarVisibilitySeconds,
            CarRadarOverlayViewModel.MaximumRadarVisibilitySeconds);

    public CarRadarForm(
        ILiveTelemetrySource liveTelemetrySource,
        ILogger<CarRadarForm> logger,
        AppPerformanceState performanceState,
        OverlaySettings settings,
        string fontFamily,
        Action saveSettings)
        : base(
            settings,
            saveSettings,
            CarRadarOverlayDefinition.Definition.DefaultWidth,
            CarRadarOverlayDefinition.Definition.DefaultHeight)
    {
        _liveTelemetrySource = liveTelemetrySource;
        _logger = logger;
        _performanceState = performanceState;
        _settings = settings;
        _fontFamily = fontFamily;
        _configuredOpacity = Math.Clamp(settings.Opacity, 0d, 1d);
        BackColor = TransparentColor;
        TransparencyKey = TransparentColor;
        Padding = Padding.Empty;
        ApplyCircularWindowRegion();

        _refreshTimer = new System.Windows.Forms.Timer
        {
            Interval = RefreshIntervalMilliseconds
        };
        _refreshTimer.Tick += (_, _) =>
        {
            _performanceState.RecordOverlayTimerTick(
                CarRadarOverlayDefinition.Definition.Id,
                RefreshIntervalMilliseconds,
                Visible,
                !Visible || Opacity <= 0.001d);
            RefreshOverlay();
        };
        _refreshTimer.Start();

        RefreshOverlay();
    }

    internal void SetSettingsPreviewVisible(bool visible)
    {
        if (_settingsPreviewVisible == visible)
        {
            return;
        }

        _settingsPreviewVisible = visible;
        if (visible)
        {
            _radarAlpha = 1d;
        }

        ApplyWindowVisibilityOpacity();
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Region?.Dispose();
            _refreshTimer.Stop();
            _refreshTimer.Dispose();
        }

        base.Dispose(disposing);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        ApplyCircularWindowRegion();
    }

    protected override void PersistOverlayFrame()
    {
        var currentOpacity = Opacity;
        try
        {
            Opacity = _configuredOpacity;
            base.PersistOverlayFrame();
        }
        finally
        {
            Opacity = currentOpacity;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var started = Stopwatch.GetTimestamp();
        var succeeded = false;
        try
        {
            base.OnPaint(e);
            if (!ShouldPaintRadar())
            {
                succeeded = true;
                return;
            }

            if (_overlayError is not null)
            {
                DrawError(e.Graphics);
                succeeded = true;
                return;
            }

            var drawStarted = Stopwatch.GetTimestamp();
            var drawSucceeded = false;
            try
            {
                DrawRadar(e.Graphics);
                drawSucceeded = true;
            }
            finally
            {
                _performanceState.RecordOperation(
                    AppPerformanceMetricIds.OverlayRadarDraw,
                    drawStarted,
                    drawSucceeded);
            }

            succeeded = true;
        }
        catch (Exception exception)
        {
            ReportOverlayError(exception, "render");
            DrawError(e.Graphics);
        }
        finally
        {
            _performanceState.RecordOperation(
                AppPerformanceMetricIds.OverlayRadarPaint,
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
            var now = DateTimeOffset.UtcNow;
            var elapsedSeconds = _lastRefreshAtUtc is { } lastRefresh
                ? Math.Clamp((now - lastRefresh).TotalSeconds, 0d, 0.5d)
                : FadeInSeconds;
            _lastRefreshAtUtc = now;

            LiveTelemetrySnapshot snapshot;
            long? previousSequence;
            var snapshotStarted = Stopwatch.GetTimestamp();
            var snapshotSucceeded = false;
            try
            {
                snapshot = _liveTelemetrySource.Snapshot();
                previousSequence = _lastRefreshSequence;
                _viewModel = CarRadarOverlayViewModel.From(
                    snapshot,
                    now,
                    previewVisible: _settingsPreviewVisible,
                    showMulticlassWarning: ShowMulticlassWarning,
                    multiclassWarningRangeSeconds: MulticlassWarningRangeSeconds,
                    radarVisibilitySeconds: RadarVisibilitySeconds);
                snapshotSucceeded = true;
            }
            finally
            {
                _performanceState.RecordOperation(
                    AppPerformanceMetricIds.OverlayRadarSnapshot,
                    snapshotStarted,
                    snapshotSucceeded);
            }

            _overlayError = null;
            var fadeStarted = Stopwatch.GetTimestamp();
            var fadeSucceeded = false;
            try
            {
                var sequenceChanged = previousSequence != snapshot.Sequence;
                var visualChanged = UpdateFadeState(now, elapsedSeconds);
                var opacityChanged = ApplyWindowVisibilityOpacity();
                var repaintNeeded = opacityChanged
                    || visualChanged
                    || (sequenceChanged && ShouldPaintRadar());
                _performanceState.RecordOverlayRefreshDecision(
                    CarRadarOverlayDefinition.Definition.Id,
                    now,
                    previousSequence,
                    snapshot.Sequence,
                    snapshot.LastUpdatedAtUtc,
                    applied: repaintNeeded);
                _lastRefreshSequence = snapshot.Sequence;
                if (repaintNeeded)
                {
                    Invalidate();
                }

                fadeSucceeded = true;
            }
            finally
            {
                _performanceState.RecordOperation(
                    AppPerformanceMetricIds.OverlayRadarFadeState,
                    fadeStarted,
                    fadeSucceeded);
            }

            succeeded = true;
        }
        catch (Exception exception)
        {
            ReportOverlayError(exception, "refresh");
            Invalidate();
        }
        finally
        {
            _performanceState.RecordOperation(
                AppPerformanceMetricIds.OverlayRadarRefresh,
                started,
                succeeded);
        }
    }

    private bool ShouldPaintRadar()
    {
        return _overlayError is not null
            || _settingsPreviewVisible
            || _radarAlpha > MinimumVisibleAlpha
            || _leftSideAlpha > MinimumVisibleAlpha
            || _rightSideAlpha > MinimumVisibleAlpha
            || _carVisuals.Values.Any(visual => visual.Alpha > MinimumVisibleAlpha);
    }

    private bool ApplyWindowVisibilityOpacity()
    {
        var visibleAlpha = _overlayError is not null || _settingsPreviewVisible
            ? 1d
            : Math.Max(
                _radarAlpha,
                Math.Max(
                    Math.Max(_leftSideAlpha, _rightSideAlpha),
                    _carVisuals.Values.Select(visual => visual.Alpha).DefaultIfEmpty(0d).Max()));
        var nextOpacity = visibleAlpha <= MinimumVisibleAlpha
            ? 0d
            : Math.Clamp(_configuredOpacity * Math.Max(0.12d, visibleAlpha), 0d, 1d);
        if (Math.Abs(Opacity - nextOpacity) <= 0.001d)
        {
            return false;
        }

        Opacity = nextOpacity;
        return true;
    }

    private bool HasCurrentRadarSignal()
    {
        return _overlayError is not null
            || _settingsPreviewVisible
            || _viewModel.HasCurrentSignal;
    }

    private bool UpdateFadeState(DateTimeOffset now, double elapsedSeconds)
    {
        var previousRadarAlpha = _radarAlpha;
        var previousLeftSideAlpha = _leftSideAlpha;
        var previousRightSideAlpha = _rightSideAlpha;
        var previousCarAlphas = _carVisuals.ToDictionary(pair => pair.Key, pair => pair.Value.Alpha);

        UpdateRadarAlpha(HasCurrentRadarSignal(), elapsedSeconds);
        UpdateSideWarningAlphas(elapsedSeconds);
        UpdateCarVisuals(now, elapsedSeconds);

        return AlphaChanged(previousRadarAlpha, _radarAlpha)
            || AlphaChanged(previousLeftSideAlpha, _leftSideAlpha)
            || AlphaChanged(previousRightSideAlpha, _rightSideAlpha)
            || previousCarAlphas.Count != _carVisuals.Count
            || _carVisuals.Any(pair =>
                !previousCarAlphas.TryGetValue(pair.Key, out var previousAlpha)
                || AlphaChanged(previousAlpha, pair.Value.Alpha));
    }

    private void UpdateRadarAlpha(bool hasCurrentSignal, double elapsedSeconds)
    {
        var target = hasCurrentSignal ? 1d : 0d;
        var duration = target > _radarAlpha ? FadeInSeconds : FadeOutSeconds;
        _radarAlpha = MoveToward(_radarAlpha, target, elapsedSeconds / duration);
    }

    private void UpdateSideWarningAlphas(double elapsedSeconds)
    {
        _leftSideAlpha = MoveTowardSideAlpha(_leftSideAlpha, _viewModel.HasCarLeft, elapsedSeconds);
        _rightSideAlpha = MoveTowardSideAlpha(_rightSideAlpha, _viewModel.HasCarRight, elapsedSeconds);
    }

    private static double MoveTowardSideAlpha(double current, bool visible, double elapsedSeconds)
    {
        var target = visible ? 1d : 0d;
        var duration = target > current ? FadeInSeconds : FadeOutSeconds;
        return MoveToward(current, target, elapsedSeconds / duration);
    }

    private void UpdateCarVisuals(DateTimeOffset now, double elapsedSeconds)
    {
        var currentCars = CurrentRadarCars()
            .GroupBy(car => car.CarIdx)
            .Select(group => group.MinBy(car => Math.Abs(RangeRatio(car)))!)
            .ToDictionary(car => car.CarIdx);

        foreach (var car in currentCars.Values)
        {
            if (!_carVisuals.TryGetValue(car.CarIdx, out var visual))
            {
                visual = new RadarCarVisual(car);
                _carVisuals[car.CarIdx] = visual;
            }

            visual.Car = car;
            visual.LastSeenAtUtc = now;
            visual.Alpha = MoveToward(visual.Alpha, 1d, elapsedSeconds / FadeInSeconds);
        }

        foreach (var visual in _carVisuals.Values)
        {
            if (currentCars.ContainsKey(visual.Car.CarIdx))
            {
                continue;
            }

            visual.Alpha = MoveToward(visual.Alpha, 0d, elapsedSeconds / FadeOutSeconds);
        }

        foreach (var carIdx in _carVisuals
            .Where(pair => pair.Value.Alpha <= MinimumVisibleAlpha && (now - pair.Value.LastSeenAtUtc).TotalSeconds > FadeOutSeconds)
            .Select(pair => pair.Key)
            .ToArray())
        {
            _carVisuals.Remove(carIdx);
        }
    }

    private void ApplyCircularWindowRegion()
    {
        if (ClientSize.Width <= 0 || ClientSize.Height <= 0)
        {
            return;
        }

        var diameter = Math.Min(ClientSize.Width, ClientSize.Height);
        var bounds = new Rectangle(
            (ClientSize.Width - diameter) / 2,
            (ClientSize.Height - diameter) / 2,
            diameter,
            diameter);
        using var path = new GraphicsPath();
        path.AddEllipse(bounds);
        var previous = Region;
        Region = new Region(path);
        previous?.Dispose();
    }

    private IEnumerable<LiveSpatialCar> CurrentRadarCars()
    {
        return _viewModel.Cars;
    }

    private LiveMulticlassApproach? CurrentMulticlassApproach()
    {
        return _viewModel.StrongestMulticlassApproach;
    }

    private static double MoveToward(double current, double target, double delta)
    {
        if (delta <= 0d)
        {
            return current;
        }

        return current < target
            ? Math.Min(target, current + delta)
            : Math.Max(target, current - delta);
    }

    private static bool AlphaChanged(double previous, double current)
    {
        return Math.Abs(previous - current) > 0.001d;
    }

    private void DrawRadar(Graphics graphics)
    {
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var diameter = Math.Min(ClientSize.Width, ClientSize.Height) - 8;
        var bounds = new RectangleF(
            (ClientSize.Width - diameter) / 2f,
            (ClientSize.Height - diameter) / 2f,
            diameter,
            diameter);

        using var circleBrush = new SolidBrush(WithAlpha(82, 12, 18, 22));
        using var circlePen = new Pen(WithAlpha(88, 0, 232, 255), 1.2f);
        graphics.FillEllipse(circleBrush, bounds);
        graphics.DrawEllipse(circlePen, bounds);

        DrawMulticlassApproachWarning(graphics, bounds);
        DrawDistanceRings(graphics, bounds);
        var sideAttachments = CurrentSideWarningAttachments();
        DrawNearbyCars(graphics, bounds, sideAttachments);
        DrawSideWarningCars(graphics, bounds, sideAttachments);
        DrawPlayerCar(graphics, bounds);
    }

    private void DrawError(Graphics graphics)
    {
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var diameter = Math.Min(ClientSize.Width, ClientSize.Height) - 8;
        var bounds = new RectangleF(
            (ClientSize.Width - diameter) / 2f,
            (ClientSize.Height - diameter) / 2f,
            diameter,
            diameter);

        using var circleBrush = new SolidBrush(Color.FromArgb(150, 32, 14, 18));
        using var circlePen = new Pen(Color.FromArgb(210, 236, 112, 99), 1.4f);
        graphics.FillEllipse(circleBrush, bounds);
        graphics.DrawEllipse(circlePen, bounds);

        using var titleFont = OverlayTheme.Font(_fontFamily, 10f, FontStyle.Bold);
        using var detailFont = OverlayTheme.Font(_fontFamily, 7.5f);
        using var titleBrush = new SolidBrush(OverlayTheme.Colors.TextPrimary);
        using var detailBrush = new SolidBrush(OverlayTheme.Colors.TextSecondary);
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter
        };
        graphics.DrawString("radar error", titleFont, titleBrush, bounds, format);
        var detailBounds = new RectangleF(bounds.Left + 28f, bounds.Top + bounds.Height / 2f + 14f, bounds.Width - 56f, 28f);
        graphics.DrawString(TrimError(_overlayError), detailFont, detailBrush, detailBounds, format);
    }

    private void ReportOverlayError(Exception exception, string operation)
    {
        _overlayError = $"{operation}: {exception.Message}";
        var now = DateTimeOffset.UtcNow;
        if (!string.Equals(_lastLoggedError, _overlayError, StringComparison.Ordinal)
            || _lastLoggedErrorAtUtc is null
            || now - _lastLoggedErrorAtUtc.Value > TimeSpan.FromSeconds(30))
        {
            _lastLoggedError = _overlayError;
            _lastLoggedErrorAtUtc = now;
            _logger.LogError(exception, "Car radar overlay {Operation} failed.", operation);
        }
    }

    private void DrawDistanceRings(Graphics graphics, RectangleF bounds)
    {
        using var labelFont = OverlayTheme.Font(_fontFamily, 7.5f);
        using var labelBrush = new SolidBrush(WithAlpha(118, 220, 230, 236));
        using var labelFormat = new StringFormat
        {
            Alignment = StringAlignment.Near,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter
        };
        var centerX = bounds.X + bounds.Width / 2f;
        var centerY = bounds.Y + bounds.Height / 2f;
        for (var index = 1; index <= 2; index++)
        {
            var inset = bounds.Width * index / 6f;
            var radius = bounds.Width / 2f - inset;
            using var ringPen = new Pen(RingStrokeColor(index), 1f);
            graphics.DrawEllipse(
                ringPen,
                bounds.X + inset,
                bounds.Y + inset,
                bounds.Width - inset * 2f,
                bounds.Height - inset * 2f);
            var labelBounds = new RectangleF(
                centerX + radius * 0.35f,
                centerY - radius - 8f,
                58f,
                16f);
            graphics.DrawString(FormatRingDistance(radius), labelFont, labelBrush, labelBounds, labelFormat);
        }
    }

    private Color RingStrokeColor(int ringIndex)
    {
        return WithAlpha(40, 255, 255, 255);
    }

    private void DrawPlayerCar(Graphics graphics, RectangleF bounds)
    {
        var centerX = bounds.X + bounds.Width / 2f;
        var centerY = bounds.Y + bounds.Height / 2f;
        var carRect = new RectangleF(
            centerX - FocusedCarWidth / 2f,
            centerY - FocusedCarHeight / 2f,
            FocusedCarWidth,
            FocusedCarHeight);
        using var brush = new SolidBrush(WithAlpha(240, 255, 255, 255));
        using var pen = new Pen(ClassBorderColor(_viewModel.Spatial.ReferenceCarClassColorHex, _radarAlpha), 2f);
        graphics.FillRoundedRectangle(brush, carRect, CarCornerRadius);
        graphics.DrawRoundedRectangle(pen, carRect, CarCornerRadius);
    }

    private void DrawMulticlassApproachWarning(Graphics graphics, RectangleF bounds)
    {
        if (CurrentMulticlassApproach() is not { } approach)
        {
            return;
        }

        var urgency = Math.Clamp(approach.Urgency, 0d, 1d);
        var alpha = (int)Math.Round(120d + urgency * 110d);
        var warningColor = WithAlpha(alpha, 236, 112, 99);
        var arcBounds = RectangleF.Inflate(bounds, -4f, -4f);
        using var arcPen = new Pen(warningColor, 5f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        graphics.DrawArc(arcPen, arcBounds, MulticlassWarningArcStartDegrees, MulticlassWarningArcSweepDegrees);

        using var font = OverlayTheme.Font(_fontFamily, 9f, FontStyle.Bold);
        using var textBrush = new SolidBrush(WithAlpha(alpha, 255, 225, 220));
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter
        };
        var labelBounds = new RectangleF(
            bounds.Left + 28f,
            bounds.Bottom - 48f,
            bounds.Width - 56f,
            18f);
        graphics.DrawString(FormatMulticlassWarning(approach), font, textBrush, labelBounds, format);
    }

    private void DrawNearbyCars(Graphics graphics, RectangleF bounds, SideWarningAttachments sideAttachments)
    {
        foreach (var placement in RadarCarPlacements(bounds, sideAttachments))
        {
            var visual = placement.Visual;
            var car = visual.Car;
            var renderedAlpha = visual.Alpha * _radarAlpha * RadarEntryOpacity(car);
            var color = ProximityColor(ProximityTint(car), renderedAlpha);

            using var brush = new SolidBrush(color);
            using var pen = new Pen(ClassBorderColor(car.CarClassColorHex, renderedAlpha), 2f);
            graphics.FillRoundedRectangle(brush, placement.Bounds, CarCornerRadius);
            graphics.DrawRoundedRectangle(pen, placement.Bounds, CarCornerRadius);
        }
    }

    private IReadOnlyList<RadarCarPlacement> RadarCarPlacements(
        RectangleF bounds,
        SideWarningAttachments sideAttachments)
    {
        return WideRowPositionedNearbyCars(bounds, sideAttachments);
    }

    private IReadOnlyList<RadarCarPlacement> WideRowPositionedNearbyCars(
        RectangleF bounds,
        SideWarningAttachments sideAttachments)
    {
        var centerX = bounds.X + bounds.Width / 2f;
        var centerY = bounds.Y + bounds.Height / 2f;
        var usableRadius = bounds.Width / 2f - UsableRadarRadiusInset;
        var visibleCars = _carVisuals.Values
            .Where(visual => visual.Alpha > MinimumVisibleAlpha)
            .Where(visual => !sideAttachments.Contains(visual.Car.CarIdx))
            .OrderBy(visual => Math.Abs(RangeRatio(visual.Car)))
            .Take(MaxWideRowRadarCars)
            .ToArray();
        var candidates = visibleCars
            .Select((visual, index) =>
            {
                var offset = LongitudinalOffset(visual.Car, usableRadius);
                return new WideRowCandidate(
                    visual,
                    index,
                    offset,
                    ReliableRelativeMeters(visual.Car),
                    PlacementDirection(visual.Car, index, offset));
            })
            .ToArray();
        var rows = new List<WideRadarRow>();

        foreach (var candidate in candidates.OrderBy(candidate => candidate.IdealOffset))
        {
            var row = rows.FirstOrDefault(row => IsSameWideRadarRow(row, candidate));
            if (row is null)
            {
                rows.Add(new WideRadarRow(
                    candidate.IdealOffset,
                    candidate.LongitudinalMeters,
                    candidate.Direction,
                    [candidate]));
                continue;
            }

            row.Candidates.Add(candidate);
        }

        return rows
            .SelectMany(row => PlacementsForRow(row, centerX, centerY, usableRadius))
            .ToArray();
    }

    private static bool IsSameWideRadarRow(WideRadarRow row, WideRowCandidate candidate)
    {
        if (Math.Abs(row.Direction - candidate.Direction) > 0.001f)
        {
            return false;
        }

        if (row.AnchorMeters is { } rowMeters && candidate.LongitudinalMeters is { } candidateMeters)
        {
            return Math.Abs(rowMeters - candidateMeters) <= WideRowLongitudinalBucketMeters;
        }

        return Math.Abs(row.AnchorOffset - candidate.IdealOffset) <= WideRowBucketPixels;
    }

    private static IReadOnlyList<RadarCarPlacement> PlacementsForRow(
        WideRadarRow row,
        float centerX,
        float centerY,
        float usableRadius)
    {
        if (row.Candidates.Count == 0)
        {
            return [];
        }

        var rowOffset = row.Candidates.Sum(candidate => candidate.IdealOffset) / row.Candidates.Count;
        var clampedRowMagnitude = Math.Min(Math.Abs(rowOffset), usableRadius);
        var availableHalfWidth = MathF.Sqrt(Math.Max(
            0f,
            usableRadius * usableRadius - clampedRowMagnitude * clampedRowMagnitude));
        var maxCenterOffset = Math.Max(0f, availableHalfWidth - RadarCarWidth / 2f - 4f);
        var minimumSlots = row.Candidates.Count > 1 ? 2 : 1;
        var maxSlots = Math.Max(minimumSlots, (int)MathF.Floor(maxCenterOffset * 2f / WideRowSlotPitchPixels) + 1);
        var visibleCandidates = row.Candidates
            .OrderBy(candidate => candidate.SourceIndex)
            .ThenBy(candidate => candidate.Visual.Car.CarIdx)
            .Take(maxSlots)
            .ToArray();
        var lineWidth = WideRowSlotPitchPixels * Math.Max(0, visibleCandidates.Length - 1);
        var placements = new List<RadarCarPlacement>(visibleCandidates.Length);
        for (var slotIndex = 0; slotIndex < visibleCandidates.Length; slotIndex++)
        {
            var candidate = visibleCandidates[slotIndex];
            var xOffset = slotIndex * WideRowSlotPitchPixels - lineWidth / 2f;
            placements.Add(new RadarCarPlacement(
                candidate.Visual,
                RadarCarBounds(centerX + xOffset, centerY - rowOffset),
                rowOffset));
        }

        return placements;
    }

    private static RectangleF RadarCarBounds(float centerX, float centerY)
    {
        return new RectangleF(
            centerX - RadarCarWidth / 2f,
            centerY - RadarCarHeight / 2f,
            RadarCarWidth,
            RadarCarHeight);
    }

    private static float PlacementDirection(LiveSpatialCar car, int index, float idealOffset)
    {
        if (idealOffset < 0f)
        {
            return -1f;
        }

        if (idealOffset > 0f)
        {
            return 1f;
        }

        if (Math.Abs(car.RelativeLaps) > 0.0001d)
        {
            return car.RelativeLaps < 0d ? -1f : 1f;
        }

        return index % 2 == 0 ? 1f : -1f;
    }

    private SideWarningAttachments CurrentSideWarningAttachments()
    {
        var usedCarIdxs = new HashSet<int>();
        var left = _leftSideAlpha > MinimumVisibleAlpha
            ? SelectSideAttachment(usedCarIdxs)
            : null;
        if (left is not null)
        {
            usedCarIdxs.Add(left.Car.CarIdx);
        }

        var right = _rightSideAlpha > MinimumVisibleAlpha
            ? SelectSideAttachment(usedCarIdxs)
            : null;
        return new SideWarningAttachments(left, right);
    }

    private RadarCarVisual? SelectSideAttachment(ISet<int> excludedCarIdxs)
    {
        return _carVisuals.Values
            .Where(visual => visual.Alpha > MinimumVisibleAlpha)
            .Where(visual => !excludedCarIdxs.Contains(visual.Car.CarIdx))
            .Where(visual => IsSideAttachmentCandidate(visual.Car))
            .OrderBy(visual => Math.Abs(RangeRatio(visual.Car)))
            .ThenByDescending(visual => visual.Alpha)
            .ThenBy(visual => visual.Car.CarIdx)
            .FirstOrDefault();
    }

    private bool IsSideAttachmentCandidate(LiveSpatialCar car)
    {
        if (!IsInRadarRange(car))
        {
            return false;
        }

        if (ReliableRelativeMeters(car) is { } meters)
        {
            return Math.Abs(meters) <= SideAttachmentWindowMeters;
        }

        return false;
    }

    private void DrawSideWarningCars(
        Graphics graphics,
        RectangleF bounds,
        SideWarningAttachments sideAttachments)
    {
        if (_leftSideAlpha <= MinimumVisibleAlpha && _rightSideAlpha <= MinimumVisibleAlpha)
        {
            return;
        }

        var centerX = bounds.X + bounds.Width / 2f;
        var centerY = bounds.Y + bounds.Height / 2f;
        var usableRadius = bounds.Width / 2f - UsableRadarRadiusInset;
        if (_leftSideAlpha > MinimumVisibleAlpha)
        {
            DrawWarningCar(
                graphics,
                centerX - 42f,
                SideWarningCenterY(centerY, usableRadius, sideAttachments.Left),
                _leftSideAlpha * _radarAlpha * SideAttachmentAlpha(sideAttachments.Left),
                mappedToTimedCar: sideAttachments.Left is not null,
                sideAttachments.Left?.Car.CarClassColorHex);
        }

        if (_rightSideAlpha > MinimumVisibleAlpha)
        {
            DrawWarningCar(
                graphics,
                centerX + 42f,
                SideWarningCenterY(centerY, usableRadius, sideAttachments.Right),
                _rightSideAlpha * _radarAlpha * SideAttachmentAlpha(sideAttachments.Right),
                mappedToTimedCar: sideAttachments.Right is not null,
                sideAttachments.Right?.Car.CarClassColorHex);
        }
    }

    private float SideWarningCenterY(float centerY, float usableRadius, RadarCarVisual? visual)
    {
        if (visual is null)
        {
            return centerY;
        }

        var maximumBias = FocusedCarHeight * 0.55f;
        var offset = Math.Clamp(LongitudinalOffset(visual.Car, usableRadius), -maximumBias, maximumBias);
        return centerY - offset;
    }

    private static double SideAttachmentAlpha(RadarCarVisual? visual)
    {
        return visual is null ? 1d : Math.Max(0.45d, visual.Alpha);
    }

    private static void DrawWarningCar(
        Graphics graphics,
        float x,
        float y,
        double alphaMultiplier,
        bool mappedToTimedCar,
        string? carClassColorHex)
    {
        var carRect = new RectangleF(
            x - RadarCarWidth / 2f,
            y - RadarCarHeight / 2f,
            RadarCarWidth,
            RadarCarHeight);
        var fillAlpha = mappedToTimedCar ? 245 : 238;
        using var brush = new SolidBrush(Color.FromArgb(ScaleAlpha(fillAlpha, alphaMultiplier), 236, 112, 99));
        using var pen = new Pen(ClassBorderColor(carClassColorHex, alphaMultiplier), 2f);
        graphics.FillRoundedRectangle(brush, carRect, CarCornerRadius);
        graphics.DrawRoundedRectangle(pen, carRect, CarCornerRadius);
    }

    private float LongitudinalOffset(LiveSpatialCar car, float usableRadius)
    {
        if (ReliableRelativeMeters(car) is { } meters)
        {
            return LongitudinalOffsetFromDistance(meters, usableRadius);
        }

        return Math.Sign(car.RelativeLaps) * usableRadius;
    }

    private static float LongitudinalOffsetFromDistance(double meters, float usableRadius)
    {
        var sign = Math.Sign(meters);
        if (sign == 0)
        {
            return 0f;
        }

        var absMeters = Math.Abs(meters);
        var separatedCenterOffset = SeparatedCenterOffset(usableRadius);

        if (absMeters <= ContactWindowMeters)
        {
            return (float)(sign * (absMeters / ContactWindowMeters) * separatedCenterOffset);
        }

        var rowAwareOffset = separatedCenterOffset
            + (float)(absMeters - ContactWindowMeters) * DistinctRowPixelsPerMeter;
        return sign * rowAwareOffset;
    }

    private Color WithAlpha(int alpha, int red, int green, int blue)
    {
        return Color.FromArgb(ScaleAlpha(alpha, _radarAlpha), red, green, blue);
    }

    private Color ProximityColor(double proximityTint, double visualAlpha)
    {
        var normalized = Math.Clamp(proximityTint, 0d, 1d);
        var alpha = ScaleAlpha(238, visualAlpha);
        var baseColor = Color.FromArgb(255, 255, 255);
        var yellow = Color.FromArgb(255, 220, 66);
        var alertRed = Color.FromArgb(255, 24, 16);

        if (normalized <= 0d)
        {
            return Color.FromArgb(alpha, baseColor.R, baseColor.G, baseColor.B);
        }

        if (normalized < ProximityRedStart)
        {
            var yellowMix = SmoothStep(0d, ProximityRedStart, normalized);
            return Color.FromArgb(
                alpha,
                Lerp(baseColor.R, yellow.R, yellowMix),
                Lerp(baseColor.G, yellow.G, yellowMix),
                Lerp(baseColor.B, yellow.B, yellowMix));
        }

        var redMix = SmoothStep(ProximityRedStart, 1d, normalized);
        return Color.FromArgb(
            alpha,
            Lerp(yellow.R, alertRed.R, redMix),
            Lerp(yellow.G, alertRed.G, redMix),
            Lerp(yellow.B, alertRed.B, redMix));
    }

    private double ProximityTint(LiveSpatialCar car)
    {
        if (ReliableRelativeMeters(car) is { } meters)
        {
            return BumperGapProximity(Math.Abs(meters));
        }

        return 0d;
    }

    private double RadarEntryOpacity(LiveSpatialCar car)
    {
        if (ReliableRelativeMeters(car) is { } meters)
        {
            var warningStartMeters = ContactWindowMeters + ProximityWarningGapMeters;
            return OpacityBetweenRangeEdgeAndWarningStart(
                Math.Abs(meters),
                warningStartMeters,
                RadarRangeMeters);
        }

        return 0d;
    }

    private static double OpacityBetweenRangeEdgeAndWarningStart(
        double absoluteValue,
        double warningStart,
        double radarRange)
    {
        if (absoluteValue <= warningStart)
        {
            return 1d;
        }

        if (radarRange <= warningStart)
        {
            return absoluteValue <= radarRange ? 1d : 0d;
        }

        var normalized = 1d - Math.Clamp((absoluteValue - warningStart) / (radarRange - warningStart), 0d, 1d);
        return SmoothStep(0d, 1d, normalized);
    }

    private static double BumperGapProximity(double centerDistanceMeters)
    {
        var bumperGapMeters = centerDistanceMeters - FocusedCarLengthMeters;
        return 1d - Math.Clamp(bumperGapMeters / ProximityWarningGapMeters, 0d, 1d);
    }

    private static int ScaleAlpha(int alpha, double multiplier)
    {
        return (int)Math.Round(Math.Clamp(alpha * multiplier, 0d, 255d));
    }

    private static Color ClassBorderColor(string? colorHex, double alphaMultiplier)
    {
        var alpha = ScaleAlpha(245, alphaMultiplier);
        return OverlayClassColor.TryParse(colorHex) is { } color
            ? Color.FromArgb(alpha, color.R, color.G, color.B)
            : Color.FromArgb(alpha, 255, 255, 255);
    }

    private static int Lerp(int start, int end, double ratio)
    {
        return (int)Math.Round(start + (end - start) * ratio);
    }

    private static double SmoothStep(double edge0, double edge1, double value)
    {
        var ratio = Math.Clamp((value - edge0) / (edge1 - edge0), 0d, 1d);
        return ratio * ratio * (3d - 2d * ratio);
    }

    private double RangeRatio(LiveSpatialCar car)
    {
        if (ReliableRelativeMeters(car) is { } meters)
        {
            return Math.Clamp(meters / RadarRangeMeters, -1d, 1d);
        }

        return Math.Sign(car.RelativeLaps);
    }

    private bool IsInRadarRange(LiveSpatialCar car)
    {
        return CarRadarOverlayViewModel.IsInRadarRange(car);
    }

    private static double? ReliableRelativeMeters(LiveSpatialCar car)
    {
        return CarRadarOverlayViewModel.ReliableRelativeMeters(car);
    }

    private static float SeparatedCenterOffset(float usableRadius)
    {
        return Math.Min(
            usableRadius,
            FocusedCarHeight / 2f + RadarCarHeight / 2f + SeparatedCarPaddingPixels);
    }

    private static string FormatRingDistance(float offsetPixels)
    {
        var meters = DistanceForLongitudinalOffset(offsetPixels);
        return FormattableString.Invariant($"{meters:0}m");
    }

    private static double DistanceForLongitudinalOffset(float offsetPixels)
    {
        var usableRadius = 146f - UsableRadarRadiusInset;
        var separatedCenterOffset = SeparatedCenterOffset(usableRadius);
        var absOffset = Math.Clamp(Math.Abs(offsetPixels), 0f, usableRadius);
        if (absOffset <= separatedCenterOffset)
        {
            return ContactWindowMeters * absOffset / Math.Max(0.001f, separatedCenterOffset);
        }

        return ContactWindowMeters + (absOffset - separatedCenterOffset) / DistinctRowPixelsPerMeter;
    }

    private static string FormatMulticlassWarning(LiveMulticlassApproach approach)
    {
        return approach.RelativeSeconds is { } seconds
            ? FormattableString.Invariant($"Faster class approaching {Math.Abs(seconds):0.0}s")
            : "Faster class approaching";
    }

    private static string TrimError(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return "see logs";
        }

        return message.Length <= 46 ? message : string.Concat(message.AsSpan(0, 43), "...");
    }

    private sealed class RadarCarVisual
    {
        public RadarCarVisual(LiveSpatialCar car)
        {
            Car = car;
            LastSeenAtUtc = DateTimeOffset.UtcNow;
        }

        public LiveSpatialCar Car { get; set; }

        public double Alpha { get; set; }

        public DateTimeOffset LastSeenAtUtc { get; set; }
    }

    private sealed record RadarCarPlacement(RadarCarVisual Visual, RectangleF Bounds, float Offset);

    private sealed record SideWarningAttachments(RadarCarVisual? Left, RadarCarVisual? Right)
    {
        public bool Contains(int carIdx)
        {
            return Left?.Car.CarIdx == carIdx || Right?.Car.CarIdx == carIdx;
        }
    }

    private sealed record WideRowCandidate(
        RadarCarVisual Visual,
        int SourceIndex,
        float IdealOffset,
        double? LongitudinalMeters,
        float Direction);

    private sealed record WideRadarRow(
        float AnchorOffset,
        double? AnchorMeters,
        float Direction,
        List<WideRowCandidate> Candidates);
}

internal static class GraphicsRadarExtensions
{
    public static void FillRoundedRectangle(this Graphics graphics, Brush brush, RectangleF bounds, float radius)
    {
        using var path = RoundedRectangle(bounds, radius);
        graphics.FillPath(brush, path);
    }

    public static void DrawRoundedRectangle(this Graphics graphics, Pen pen, RectangleF bounds, float radius)
    {
        using var path = RoundedRectangle(bounds, radius);
        graphics.DrawPath(pen, path);
    }

    private static GraphicsPath RoundedRectangle(RectangleF bounds, float radius)
    {
        var diameter = radius * 2f;
        var path = new GraphicsPath();
        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180f, 90f);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270f, 90f);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0f, 90f);
        path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90f, 90f);
        path.CloseFigure();
        return path;
    }
}
