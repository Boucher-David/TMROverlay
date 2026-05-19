using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using Microsoft.Extensions.Logging;
using TmrOverlay.App.Overlays.Abstractions;
using TmrOverlay.App.Overlays.Styling;
using TmrOverlay.App.Performance;
using TmrOverlay.Core.History;
using TmrOverlay.Core.Overlays;
using TmrOverlay.Core.Settings;
using TmrOverlay.Core.Telemetry.Live;

namespace TmrOverlay.App.Overlays.GapToLeader;

internal sealed class GapToLeaderForm : PersistentOverlayForm
{
    private static readonly TimeSpan TrendWindow = TimeSpan.FromHours(4);
    private const int AxisLabelWidth = 64;
    private const int XAxisLabelLaneHeight = 20;
    private const int MaxTrendPointsPerCar = 36_000;
    private const int MaxWeatherPoints = 36_000;
    private const int MaxDriverChangeMarkers = 64;
    private const double StickyVisibilityMinimumSeconds = 120d;
    private const double StickyVisibilityLaps = 1.5d;
    private const double EntryTailSeconds = 300d;
    private const double EntryFadeSeconds = 45d;
    private const double MissingSegmentGapSeconds = 10d;
    private const double MissingTelemetryGraceSeconds = 5d;
    private const double MinimumTrendDomainSeconds = 120d;
    private const double MinimumTrendDomainLaps = 1.5d;
    private const double TrendRightPaddingSeconds = 20d;
    private const double TrendRightPaddingLaps = 0.15d;
    private const double FocusScaleMinimumReferenceGapSeconds = 90d;
    private const double FocusScaleMinimumReferenceGapLaps = 0.5d;
    private const double FocusScaleMinimumRangeSeconds = 20d;
    private const double FocusScaleMinimumRangeLaps = 0.1d;
    private const double FocusScaleCandidateRangeMinimumSeconds = 15d;
    private const double FocusScaleCandidateRangeMaximumSeconds = 90d;
    private const double FocusScaleCandidateRangeLaps = 0.5d;
    private const double FocusScalePaddingMultiplier = 1.18d;
    private const double FocusScaleTriggerRatio = 3d;
    private const double SameLapReferenceBoundaryLaps = 0.95d;
    private const float FocusScaleReferenceRatio = 0.56f;
    private const float FocusScaleTopPadding = 18f;
    private const float FocusScaleBottomPadding = 8f;
    private const int RefreshIntervalMilliseconds = 500;

    private readonly ILiveTelemetrySource _liveTelemetrySource;
    private readonly ILogger<GapToLeaderForm> _logger;
    private readonly AppPerformanceState _performanceState;
    private readonly OverlaySettings _settings;
    private readonly string _fontFamily;
    private readonly Label _titleLabel;
    private readonly Label _statusLabel;
    private readonly Label _timeRemainingLabel;
    private readonly Label _sourceLabel;
    // Overlay-local render buffer only. The gap overlay never persists this trace.
    private readonly Dictionary<int, List<GapTrendPoint>> _series = [];
    private readonly List<WeatherTrendPoint> _weather = [];
    private readonly List<DriverChangeMarker> _driverChangeMarkers = [];
    private readonly List<LeaderChangeMarker> _leaderChangeMarkers = [];
    private readonly Dictionary<int, CarRenderState> _carRenderStates = [];
    private readonly Dictionary<int, DriverIdentity> _driverIdentities = [];
    private readonly System.Windows.Forms.Timer _refreshTimer;
    private long _lastSequence;
    private long? _lastRefreshSequence;
    private LiveLeaderGapSnapshot _gap = LiveLeaderGapSnapshot.Unavailable;
    private DateTimeOffset? _latestPointAtUtc;
    private double? _latestAxisSeconds;
    private double? _trendStartAxisSeconds;
    private double? _lapReferenceSeconds;
    private int? _lastDriversSoFar;
    private int? _lastClassLeaderCarIdx;
    private ReferenceContext? _lastReferenceContext;
    private string? _overlayError;
    private string? _lastLoggedError;
    private DateTimeOffset? _lastLoggedErrorAtUtc;

    public GapToLeaderForm(
        ILiveTelemetrySource liveTelemetrySource,
        ILogger<GapToLeaderForm> logger,
        AppPerformanceState performanceState,
        OverlaySettings settings,
        string fontFamily,
        Action saveSettings)
        : base(
            settings,
            saveSettings,
            GapToLeaderOverlayDefinition.Definition.DefaultWidth,
            GapToLeaderOverlayDefinition.Definition.DefaultHeight)
    {
        _liveTelemetrySource = liveTelemetrySource;
        _logger = logger;
        _performanceState = performanceState;
        _settings = settings;
        _fontFamily = fontFamily;

        BackColor = OverlayTheme.Colors.WindowBackground;
        Padding = new Padding(OverlayTheme.Layout.OverlayChromePadding);

        _titleLabel = OverlayChrome.CreateTitleLabel(_fontFamily, "Class Gap Trend", width: 210);
        _statusLabel = OverlayChrome.CreateStatusLabel(_fontFamily, titleWidth: 210, clientWidth: ClientSize.Width, minimumWidth: 120);
        _timeRemainingLabel = OverlayChrome.CreateTimeRemainingLabel(_fontFamily, titleWidth: 210, clientWidth: ClientSize.Width);
        _sourceLabel = OverlayChrome.CreateSourceLabel(_fontFamily, ClientSize.Width, ClientSize.Height, minimumWidth: 260);

        Controls.Add(_titleLabel);
        Controls.Add(_statusLabel);
        Controls.Add(_timeRemainingLabel);
        Controls.Add(_sourceLabel);

        RegisterDragSurfaces(_titleLabel, _statusLabel, _timeRemainingLabel, _sourceLabel);

        _refreshTimer = new System.Windows.Forms.Timer
        {
            Interval = RefreshIntervalMilliseconds
        };
        _refreshTimer.Tick += (_, _) =>
        {
            _performanceState.RecordOverlayTimerTick(
                GapToLeaderOverlayDefinition.Definition.Id,
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
        if (_statusLabel is null || _timeRemainingLabel is null || _sourceLabel is null)
        {
            return;
        }

        _statusLabel.Location = OverlayChrome.StatusLocation(titleWidth: 210);
        _statusLabel.Size = OverlayChrome.StatusSize(ClientSize.Width, titleWidth: 210, minimumWidth: 120);
        _timeRemainingLabel.Location = OverlayChrome.HeaderTimeRemainingLocation(ClientSize.Width, titleWidth: 210);
        _timeRemainingLabel.Size = new Size(OverlayChrome.HeaderTimeRemainingWidth(ClientSize.Width, titleWidth: 210), OverlayTheme.Layout.OverlayStatusHeight);
        _sourceLabel.Location = OverlayChrome.SourceLocation(ClientSize.Height);
        _sourceLabel.Size = OverlayChrome.SourceSize(ClientSize.Width, minimumWidth: 260);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _refreshTimer.Stop();
            _refreshTimer.Dispose();
            _titleLabel.Dispose();
            _statusLabel.Dispose();
            _timeRemainingLabel.Dispose();
            _sourceLabel.Dispose();
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

            using var borderPen = new Pen(OverlayTheme.Colors.WindowBorder);
            OverlayChrome.DrawWindowBorder(e.Graphics, ClientSize);

            var graphBounds = new Rectangle(
                14,
                42,
                Math.Max(280, ClientSize.Width - 28),
                Math.Max(120, ClientSize.Height - 76));
            using var graphBrush = new SolidBrush(OverlayTheme.Colors.PanelBackground);
            e.Graphics.FillRectangle(graphBrush, graphBounds);
            e.Graphics.DrawRectangle(borderPen, graphBounds);

            if (_overlayError is not null)
            {
                DrawError(e.Graphics, graphBounds);
                succeeded = true;
                return;
            }

            var drawStarted = Stopwatch.GetTimestamp();
            var drawSucceeded = false;
            try
            {
                DrawGraph(e.Graphics, graphBounds);
                drawSucceeded = true;
            }
            finally
            {
                _performanceState.RecordOperation(
                    AppPerformanceMetricIds.OverlayGapDrawGraph,
                    drawStarted,
                    drawSucceeded);
            }

            succeeded = true;
        }
        catch (Exception exception)
        {
            ReportOverlayError(exception, "render");
            DrawError(
                e.Graphics,
                new Rectangle(
                    14,
                    42,
                    Math.Max(280, ClientSize.Width - 28),
                    Math.Max(120, ClientSize.Height - 76)));
        }
        finally
        {
            _performanceState.RecordOperation(
                AppPerformanceMetricIds.OverlayGapPaint,
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
                    AppPerformanceMetricIds.OverlayGapSnapshot,
                    snapshotStarted,
                    snapshotSucceeded);
            }

            var now = DateTimeOffset.UtcNow;
            var previousSequence = _lastRefreshSequence;
            var viewModel = GapToLeaderOverlayViewModel.From(snapshot, now);
            _gap = viewModel.Gap;
            _lapReferenceSeconds = viewModel.LapReferenceSeconds;
            if (snapshot.Sequence != _lastSequence)
            {
                _lastSequence = snapshot.Sequence;
                var recordStarted = Stopwatch.GetTimestamp();
                var recordSucceeded = false;
                try
                {
                    RecordGapSnapshot(snapshot, _gap);
                    recordSucceeded = true;
                }
                finally
                {
                    _performanceState.RecordOperation(
                        AppPerformanceMetricIds.OverlayGapRecordSnapshot,
                        recordStarted,
                        recordSucceeded);
                }
            }

            _overlayError = null;
            IReadOnlyList<ChartSeriesSelection> selectedSeries = [];
            if (_gap.HasData)
            {
                var selectStarted = Stopwatch.GetTimestamp();
                var selectSucceeded = false;
                try
                {
                    selectedSeries = SelectChartSeries();
                    selectSucceeded = true;
                }
                finally
                {
                    _performanceState.RecordOperation(
                        AppPerformanceMetricIds.OverlayGapSelectSeries,
                        selectStarted,
                        selectSucceeded);
                }
            }

            var trendDomain = SelectTimeDomain(selectedSeries);
            var gapScale = SelectGapScale(selectedSeries, trendDomain.StartSeconds, trendDomain.EndSeconds);
            var uiChanged = OverlayChrome.ApplyChromeState(
                this,
                _titleLabel,
                _statusLabel,
                _sourceLabel,
                ChromeStateFor(snapshot, _gap, selectedSeries.Count, trendDomain.DurationSeconds, gapScale),
                titleWidth: 210,
                timeRemainingLabel: _timeRemainingLabel);
            var sequenceChanged = previousSequence != snapshot.Sequence;
            _performanceState.RecordOverlayRefreshDecision(
                GapToLeaderOverlayDefinition.Definition.Id,
                now,
                previousSequence,
                snapshot.Sequence,
                snapshot.LastUpdatedAtUtc,
                applied: sequenceChanged || uiChanged);
            _lastRefreshSequence = snapshot.Sequence;
            if (sequenceChanged || uiChanged)
            {
                Invalidate();
            }

            succeeded = true;
        }
        catch (Exception exception)
        {
            ReportOverlayError(exception, "refresh");
            OverlayChrome.ApplyChromeState(
                this,
                _titleLabel,
                _statusLabel,
                _sourceLabel,
                OverlayChromeState.Error("Class Gap Trend", "graph error", TrimError(_overlayError)),
                titleWidth: 210,
                timeRemainingLabel: _timeRemainingLabel);
            Invalidate();
        }
        finally
        {
            _performanceState.RecordOperation(
                AppPerformanceMetricIds.OverlayGapRefresh,
                started,
                succeeded);
        }
    }

    private void RecordGapSnapshot(LiveTelemetrySnapshot snapshot, LiveLeaderGapSnapshot gap)
    {
        var timestamp = snapshot.LastUpdatedAtUtc
            ?? DateTimeOffset.UtcNow;
        var axisSeconds = SelectAxisSeconds(timestamp, snapshot.Models.Session.SessionTimeSeconds);
        _latestPointAtUtc = timestamp;
        _latestAxisSeconds = axisSeconds;
        if (_trendStartAxisSeconds is null || axisSeconds < _trendStartAxisSeconds.Value)
        {
            _trendStartAxisSeconds = axisSeconds;
        }

        RecordReferenceContext(snapshot, axisSeconds);
        RecordWeatherSnapshot(snapshot, axisSeconds);
        RecordDriverChangeMarkers(snapshot, gap, timestamp, axisSeconds);
        RecordLeaderChange(gap, timestamp, axisSeconds);

        foreach (var car in gap.ClassCars)
        {
            var gapSeconds = ChartGapSeconds(car);
            if (gapSeconds is null)
            {
                continue;
            }

            if (!_series.TryGetValue(car.CarIdx, out var points))
            {
                points = [];
                _series[car.CarIdx] = points;
            }

            var startsSegment = points.Count == 0
                || axisSeconds - points[^1].AxisSeconds > MissingSegmentGapSeconds
                || !ShouldConnectGapSeriesPoint(points[^1].GapSeconds, gapSeconds.Value, _lapReferenceSeconds);
            if (points.Count > 0 && points[^1].TimestampUtc == timestamp)
            {
                points[^1] = new GapTrendPoint(timestamp, axisSeconds, gapSeconds.Value, car.IsReferenceCar, car.IsClassLeader, car.ClassPosition, points[^1].StartsSegment);
            }
            else
            {
                points.Add(new GapTrendPoint(timestamp, axisSeconds, gapSeconds.Value, car.IsReferenceCar, car.IsClassLeader, car.ClassPosition, startsSegment));
            }

            if (points.Count > MaxTrendPointsPerCar)
            {
                points.RemoveRange(0, points.Count - MaxTrendPointsPerCar);
            }
        }

        UpdateCarRenderStates(gap, axisSeconds);
        PruneOldPoints(axisSeconds);
    }

    private void DrawGraph(Graphics graphics, Rectangle graphBounds)
    {
        Rectangle plotBounds;
        Rectangle axisBounds;
        IReadOnlyList<ChartSeriesSelection> selectedSeries;
        TrendDomain domain;
        GapScale gapScale;
        var prepareStarted = Stopwatch.GetTimestamp();
        var prepareSucceeded = false;
        try
        {
            var innerBounds = Rectangle.Inflate(graphBounds, -12, -14);
            innerBounds.Y += 10;
            innerBounds.Height -= XAxisLabelLaneHeight;
            plotBounds = new Rectangle(
                innerBounds.Left + AxisLabelWidth,
                innerBounds.Top,
                Math.Max(40, innerBounds.Width - AxisLabelWidth),
                innerBounds.Height);
            axisBounds = new Rectangle(innerBounds.Left, innerBounds.Top, AxisLabelWidth - 8, innerBounds.Height);

            selectedSeries = SelectChartSeries();
            if (!HasGapComparisonSeries(selectedSeries))
            {
                selectedSeries = [];
            }

            domain = SelectTimeDomain(selectedSeries);
            gapScale = SelectGapScale(selectedSeries, domain.StartSeconds, domain.EndSeconds);
            prepareSucceeded = true;
        }
        finally
        {
            _performanceState.RecordOperation(
                AppPerformanceMetricIds.OverlayGapDrawPrepare,
                prepareStarted,
                prepareSucceeded);
        }

        if (selectedSeries.Count == 0)
        {
            DrawWaitingForTiming(graphics, graphBounds);
            return;
        }

        var staticStarted = Stopwatch.GetTimestamp();
        var staticSucceeded = false;
        try
        {
            DrawWeatherBands(graphics, plotBounds, domain);
            DrawGridLines(graphics, plotBounds, axisBounds, domain, gapScale, _lapReferenceSeconds);
            DrawLeaderChangeMarkers(graphics, plotBounds, domain);

            using var leaderPen = new Pen(Color.FromArgb(235, 255, 255, 255), 1.6f);
            graphics.DrawLine(leaderPen, plotBounds.Left, plotBounds.Top, plotBounds.Right, plotBounds.Top);
            staticSucceeded = true;
        }
        finally
        {
            _performanceState.RecordOperation(
                AppPerformanceMetricIds.OverlayGapDrawStatic,
                staticStarted,
                staticSucceeded);
        }

        var endpointLabels = new List<EndpointLabel>();
        var seriesStarted = Stopwatch.GetTimestamp();
        var seriesSucceeded = false;
        try
        {
            foreach (var selection in selectedSeries
                .OrderBy(selection => selection.State.IsClassLeader)
                .ThenBy(selection => selection.State.IsReferenceCar))
            {
                var state = selection.State;
                if (gapScale.IsFocusRelative && state.IsClassLeader)
                {
                    continue;
                }

                if (!_series.TryGetValue(state.CarIdx, out var points))
                {
                    continue;
                }

                var visiblePoints = points
                    .Where(point => point.AxisSeconds >= selection.DrawStartSeconds && point.AxisSeconds >= domain.StartSeconds && point.AxisSeconds <= domain.EndSeconds)
                    .ToArray();
                if (visiblePoints.Length == 0)
                {
                    continue;
                }

                var color = WithAlpha(SeriesColor(state), selection.Alpha * SeriesAlphaMultiplier(state));
                using var pen = new Pen(color, state.IsReferenceCar ? 2.8f : state.IsClassLeader ? 1.8f : 1.25f);
                if (selection.IsStale || selection.IsStickyExit)
                {
                    pen.DashStyle = DashStyle.Dash;
                }

                DrawSeriesSegments(graphics, visiblePoints, pen, color, state.IsReferenceCar, domain, gapScale, plotBounds);
                AddPositionLabel(endpointLabels, state, visiblePoints[^1], color, domain, gapScale, plotBounds);
                if (selection.IsStale)
                {
                    DrawTerminalMarker(graphics, visiblePoints[^1], color, domain, gapScale, plotBounds);
                }
            }

            seriesSucceeded = true;
        }
        finally
        {
            _performanceState.RecordOperation(
                AppPerformanceMetricIds.OverlayGapDrawSeries,
                seriesStarted,
                seriesSucceeded);
        }

        var labelsStarted = Stopwatch.GetTimestamp();
        var labelsSucceeded = false;
        try
        {
            DrawPositionLabels(graphics, endpointLabels, plotBounds);
            DrawDriverChangeMarkers(graphics, plotBounds, domain, gapScale);
            DrawScaleLabels(graphics, plotBounds, axisBounds, gapScale);
            labelsSucceeded = true;
        }
        finally
        {
            _performanceState.RecordOperation(
                AppPerformanceMetricIds.OverlayGapDrawLabels,
                labelsStarted,
                labelsSucceeded);
        }
    }

    private void RecordWeatherSnapshot(LiveTelemetrySnapshot snapshot, double axisSeconds)
    {
        var condition = SelectWeatherCondition(snapshot);
        if (_weather.Count > 0 && Math.Abs(_weather[^1].AxisSeconds - axisSeconds) < 0.001d)
        {
            _weather[^1] = new WeatherTrendPoint(axisSeconds, condition);
        }
        else
        {
            _weather.Add(new WeatherTrendPoint(axisSeconds, condition));
        }

        if (_weather.Count > MaxWeatherPoints)
        {
            _weather.RemoveRange(0, _weather.Count - MaxWeatherPoints);
        }
    }

    private void RecordDriverChangeMarkers(
        LiveTelemetrySnapshot snapshot,
        LiveLeaderGapSnapshot gap,
        DateTimeOffset timestamp,
        double axisSeconds)
    {
        RecordExplicitTeamDriverChangeMarker(snapshot, gap, timestamp, axisSeconds);
        RecordSessionInfoDriverChangeMarkers(snapshot, gap, timestamp, axisSeconds);
    }

    private void RecordExplicitTeamDriverChangeMarker(
        LiveTelemetrySnapshot snapshot,
        LiveLeaderGapSnapshot gap,
        DateTimeOffset timestamp,
        double axisSeconds)
    {
        if (snapshot.Models.RaceEvents.DriversSoFar is not { } driversSoFar || driversSoFar <= 0)
        {
            return;
        }

        if (_lastDriversSoFar is { } previousDrivers)
        {
            if (driversSoFar < previousDrivers)
            {
                _lastDriversSoFar = driversSoFar;
                return;
            }

            if (driversSoFar > previousDrivers
                && ReferenceUsesPlayerCar(snapshot)
                && gap.ClassCars.FirstOrDefault(car => car.IsReferenceCar) is { } reference
                && ChartGapSeconds(reference) is { } gapSeconds)
            {
                AddDriverChangeMarker(new DriverChangeMarker(
                    timestamp,
                    axisSeconds,
                    reference.CarIdx,
                    gapSeconds,
                    true,
                    $"D{driversSoFar}",
                    DriverChangeMarkerSource.TeamControl));
            }
        }

        _lastDriversSoFar = driversSoFar;
    }

    private static bool ReferenceUsesPlayerCar(LiveTelemetrySnapshot snapshot)
    {
        var reference = snapshot.Models.Reference;
        if (reference.HasData)
        {
            return reference.FocusIsPlayer;
        }

        var directory = snapshot.Models.DriverDirectory;
        return directory.FocusCarIdx is not null
            && directory.PlayerCarIdx is not null
            && directory.FocusCarIdx == directory.PlayerCarIdx;
    }

    private void RecordSessionInfoDriverChangeMarkers(
        LiveTelemetrySnapshot snapshot,
        LiveLeaderGapSnapshot gap,
        DateTimeOffset timestamp,
        double axisSeconds)
    {
        if (snapshot.Context.Drivers.Count == 0)
        {
            return;
        }

        foreach (var driver in snapshot.Context.Drivers)
        {
            if (ToDriverIdentity(driver) is not { } identity)
            {
                continue;
            }

            if (_driverIdentities.TryGetValue(identity.CarIdx, out var previous)
                && !previous.HasSameDriver(identity)
                && gap.ClassCars.FirstOrDefault(car => car.CarIdx == identity.CarIdx) is { } car
                && ChartGapSeconds(car) is { } gapSeconds)
            {
                AddDriverChangeMarker(new DriverChangeMarker(
                    timestamp,
                    axisSeconds,
                    identity.CarIdx,
                    gapSeconds,
                    car.IsReferenceCar,
                    identity.ShortLabel,
                    DriverChangeMarkerSource.SessionInfo));
            }

            _driverIdentities[identity.CarIdx] = identity;
        }
    }

    private void AddDriverChangeMarker(DriverChangeMarker marker)
    {
        if (_driverChangeMarkers.Any(existing =>
                existing.CarIdx == marker.CarIdx
                && Math.Abs(existing.AxisSeconds - marker.AxisSeconds) < 5d))
        {
            return;
        }

        _driverChangeMarkers.Add(marker);
        if (_driverChangeMarkers.Count > MaxDriverChangeMarkers)
        {
            _driverChangeMarkers.RemoveRange(0, _driverChangeMarkers.Count - MaxDriverChangeMarkers);
        }
    }

    private void RecordLeaderChange(LiveLeaderGapSnapshot gap, DateTimeOffset timestamp, double axisSeconds)
    {
        if (gap.ClassLeaderCarIdx is not { } leaderCarIdx)
        {
            return;
        }

        if (_lastClassLeaderCarIdx is { } previousLeader && previousLeader != leaderCarIdx)
        {
            _leaderChangeMarkers.Add(new LeaderChangeMarker(timestamp, axisSeconds, previousLeader, leaderCarIdx));
        }

        _lastClassLeaderCarIdx = leaderCarIdx;
    }

    private void RecordReferenceContext(LiveTelemetrySnapshot snapshot, double axisSeconds)
    {
        var context = SelectReferenceContext(snapshot);
        if (context is null)
        {
            return;
        }

        if (_lastReferenceContext is not null && _lastReferenceContext != context)
        {
            _series.Clear();
            _weather.Clear();
            _driverChangeMarkers.Clear();
            _leaderChangeMarkers.Clear();
            _carRenderStates.Clear();
            _lastClassLeaderCarIdx = null;
            _trendStartAxisSeconds = axisSeconds;
            _latestAxisSeconds = axisSeconds;
        }

        _lastReferenceContext = context;
    }

    private void UpdateCarRenderStates(LiveLeaderGapSnapshot gap, double axisSeconds)
    {
        var desiredCarIds = SelectDesiredCarIds(gap.ClassCars);
        foreach (var car in gap.ClassCars)
        {
            if (ChartGapSeconds(car) is not { } gapSeconds)
            {
                continue;
            }

            if (!_carRenderStates.TryGetValue(car.CarIdx, out var state))
            {
                state = new CarRenderState(car.CarIdx);
                _carRenderStates[car.CarIdx] = state;
            }

            var wasVisible = ShouldKeepVisible(state, axisSeconds);
            state.LastSeenAxisSeconds = axisSeconds;
            state.LastGapSeconds = gapSeconds;
            state.IsReferenceCar = car.IsReferenceCar;
            state.IsClassLeader = car.IsClassLeader;
            state.ClassPosition = car.ClassPosition;
            state.DeltaSecondsToReference = car.DeltaSecondsToReference;
            state.IsCurrentlyDesired = desiredCarIds.Contains(car.CarIdx);
            if (state.IsCurrentlyDesired)
            {
                if (!wasVisible)
                {
                    state.VisibleSinceAxisSeconds = axisSeconds;
                }

                state.LastDesiredAxisSeconds = axisSeconds;
            }
        }

        foreach (var state in _carRenderStates.Values)
        {
            if (!desiredCarIds.Contains(state.CarIdx))
            {
                state.IsCurrentlyDesired = false;
            }
        }
    }

    private HashSet<int> SelectDesiredCarIds(IReadOnlyList<LiveClassGapCar> cars)
    {
        var selected = new HashSet<int>();
        var reference = cars.FirstOrDefault(car =>
            car.IsReferenceCar
            && ChartGapSeconds(car) is not null);
        var anchor = reference;
        var referenceCanAnchor = anchor is not null && !IsLappedGraphGap(anchor);
        foreach (var car in cars.Where(car => car.IsClassLeader || (referenceCanAnchor && car.IsReferenceCar)))
        {
            selected.Add(car.CarIdx);
        }

        if (!referenceCanAnchor || anchor is null)
        {
            foreach (var car in cars
                .Where(car => !car.IsClassLeader && !IsLappedGraphGap(car))
                .OrderBy(car => car.ClassPosition ?? int.MaxValue)
                .ThenBy(car => ChartGapSeconds(car) ?? double.MaxValue)
                .Take(Math.Max(1, _settings.GetIntegerOption(OverlayOptionKeys.GapCarsBehind, defaultValue: 5, minimum: 0, maximum: 12))))
            {
                selected.Add(car.CarIdx);
            }

            if (selected.Count <= 1)
            {
                foreach (var car in cars
                    .Where(car => !car.IsClassLeader)
                    .OrderBy(car => ChartGapSeconds(car) ?? double.MaxValue)
                    .ThenBy(car => car.ClassPosition ?? int.MaxValue)
                    .Take(Math.Max(1, _settings.GetIntegerOption(OverlayOptionKeys.GapCarsBehind, defaultValue: 5, minimum: 0, maximum: 12))))
                {
                    selected.Add(car.CarIdx);
                }
            }

            return selected;
        }

        foreach (var car in cars
            .Where(car => !car.IsReferenceCar
                && !car.IsClassLeader
                && IsSameLapReferenceCandidate(car, anchor)
                && car.DeltaSecondsToReference is not null
                && car.DeltaSecondsToReference.Value < 0d)
            .OrderByDescending(car => car.DeltaSecondsToReference!.Value)
            .Take(_settings.GetIntegerOption(OverlayOptionKeys.GapCarsAhead, defaultValue: 5, minimum: 0, maximum: 12)))
        {
            selected.Add(car.CarIdx);
        }

        foreach (var car in cars
            .Where(car => !car.IsReferenceCar
                && !car.IsClassLeader
                && IsSameLapReferenceCandidate(car, anchor)
                && car.DeltaSecondsToReference is not null
                && car.DeltaSecondsToReference.Value > 0d)
            .OrderBy(car => car.DeltaSecondsToReference!.Value)
            .Take(_settings.GetIntegerOption(OverlayOptionKeys.GapCarsBehind, defaultValue: 5, minimum: 0, maximum: 12)))
        {
            selected.Add(car.CarIdx);
        }

        return selected;
    }

    private bool ShouldKeepVisible(CarRenderState state, double axisSeconds)
    {
        return state.LastDesiredAxisSeconds is { } lastDesired
            && axisSeconds - lastDesired <= StickyVisibilitySeconds();
    }

    private double StickyVisibilitySeconds()
    {
        return Math.Max(
            StickyVisibilityMinimumSeconds,
            _lapReferenceSeconds is { } lapSeconds && IsValidLapReference(lapSeconds)
                ? lapSeconds * StickyVisibilityLaps
                : 0d);
    }

    private IReadOnlyList<ChartSeriesSelection> SelectChartSeries()
    {
        var now = _latestAxisSeconds ?? SelectAxisSeconds(_latestPointAtUtc ?? DateTimeOffset.UtcNow, null);
        return _carRenderStates.Values
            .Where(state => ShouldKeepVisible(state, now))
            .Select(state => ToSelection(state, now))
            .OrderBy(selection => selection.State.GapSortValue)
            .ToArray();
    }

    private static bool HasGapComparisonSeries(IReadOnlyList<ChartSeriesSelection> selectedSeries)
    {
        return selectedSeries.Any(selection => !selection.State.IsClassLeader);
    }

    private ChartSeriesSelection ToSelection(CarRenderState state, double now)
    {
        var lastDesired = state.LastDesiredAxisSeconds ?? now;
        var visibleSince = state.VisibleSinceAxisSeconds ?? lastDesired;
        var isStickyExit = !state.IsCurrentlyDesired;
        var isStale = now - state.LastSeenAxisSeconds > MissingTelemetryGraceSeconds;
        var stickySeconds = StickyVisibilitySeconds();
        var exitAlpha = isStickyExit
            ? 1d - Math.Clamp((now - lastDesired) / Math.Max(1d, stickySeconds), 0d, 1d)
            : 1d;
        var entryAlpha = Math.Clamp((now - visibleSince) / EntryFadeSeconds, 0d, 1d);
        var alpha = Math.Clamp(Math.Min(exitAlpha, 0.35d + entryAlpha * 0.65d), 0.18d, 1d);
        var drawStartSeconds = now - visibleSince <= EntryFadeSeconds
            ? Math.Max(0d, visibleSince - EntryTailSeconds)
            : double.NegativeInfinity;
        return new ChartSeriesSelection(
            state,
            alpha,
            isStickyExit,
            isStale,
            drawStartSeconds);
    }

    private TrendDomain SelectTimeDomain(IReadOnlyList<ChartSeriesSelection> selectedSeries)
    {
        var latest = _latestAxisSeconds ?? SelectAxisSeconds(_latestPointAtUtc ?? DateTimeOffset.UtcNow, null);
        var anchor = _trendStartAxisSeconds ?? FirstVisibleAxisSeconds(selectedSeries) ?? latest;
        var elapsed = Math.Max(0d, latest - anchor);
        if (elapsed >= TrendWindow.TotalSeconds)
        {
            return new TrendDomain(latest - TrendWindow.TotalSeconds, latest);
        }

        var rightPadding = TrendRightPadding();
        var minimumWindow = MinimumTrendDomain();
        var duration = Math.Min(
            TrendWindow.TotalSeconds,
            Math.Max(minimumWindow, elapsed + rightPadding));
        return new TrendDomain(anchor, anchor + duration);
    }

    private double MinimumTrendDomain()
    {
        return Math.Max(
            MinimumTrendDomainSeconds,
            _lapReferenceSeconds is { } lapSeconds && IsValidLapReference(lapSeconds)
                ? lapSeconds * MinimumTrendDomainLaps
                : 0d);
    }

    private double TrendRightPadding()
    {
        return Math.Max(
            TrendRightPaddingSeconds,
            _lapReferenceSeconds is { } lapSeconds && IsValidLapReference(lapSeconds)
                ? lapSeconds * TrendRightPaddingLaps
                : 0d);
    }

    private double? FirstVisibleAxisSeconds(IReadOnlyList<ChartSeriesSelection> selectedSeries)
    {
        double? firstVisiblePoint = null;
        foreach (var selection in selectedSeries)
        {
            if (!_series.TryGetValue(selection.State.CarIdx, out var points))
            {
                continue;
            }

            foreach (var point in points.Where(point => point.AxisSeconds >= selection.DrawStartSeconds))
            {
                if (firstVisiblePoint is null || point.AxisSeconds < firstVisiblePoint.Value)
                {
                    firstVisiblePoint = point.AxisSeconds;
                }
            }
        }

        return firstVisiblePoint;
    }

    private double SelectMaxGapSeconds(
        IReadOnlyList<ChartSeriesSelection> selectedSeries,
        double startSeconds,
        double endSeconds)
    {
        var maxGap = selectedSeries
            .Where(selection => _series.ContainsKey(selection.State.CarIdx))
            .SelectMany(selection => _series[selection.State.CarIdx].Where(point => point.AxisSeconds >= selection.DrawStartSeconds))
            .Where(point => point.AxisSeconds >= startSeconds && point.AxisSeconds <= endSeconds)
            .Select(point => point.GapSeconds)
            .DefaultIfEmpty(1d)
            .Max();
        return NiceCeiling(Math.Max(1d, maxGap));
    }

    private GapScale SelectGapScale(
        IReadOnlyList<ChartSeriesSelection> selectedSeries,
        double startSeconds,
        double endSeconds)
    {
        var scaleSeries = selectedSeries
            .Where(ShouldUseForGapScale)
            .ToArray();
        var leaderScaleMax = SelectMaxGapSeconds(scaleSeries, startSeconds, endSeconds);
        var referenceSelection = selectedSeries.FirstOrDefault(selection => selection.State.IsReferenceCar);
        if (referenceSelection is null
            || !_series.TryGetValue(referenceSelection.State.CarIdx, out var rawReferencePoints))
        {
            return GapScale.Leader(leaderScaleMax);
        }

        var referencePoints = rawReferencePoints
            .Where(point => point.AxisSeconds >= startSeconds && point.AxisSeconds <= endSeconds)
            .OrderBy(point => point.AxisSeconds)
            .ToArray();
        if (referencePoints.Length == 0)
        {
            return GapScale.Leader(leaderScaleMax);
        }

        var latestReferenceGap = ReferenceGapAt(referencePoints, endSeconds);
        var triggerGap = FocusScaleMinimumReferenceGap();
        if (latestReferenceGap < triggerGap)
        {
            return GapScale.Leader(leaderScaleMax);
        }

        var maxAheadSeconds = 0d;
        var maxBehindSeconds = 0d;
        var hasLocalComparison = false;
        foreach (var selection in scaleSeries.Where(selection => !selection.State.IsClassLeader))
        {
            if (!_series.TryGetValue(selection.State.CarIdx, out var points))
            {
                continue;
            }

            foreach (var point in points.Where(point =>
                point.AxisSeconds >= selection.DrawStartSeconds
                && point.AxisSeconds >= startSeconds
                && point.AxisSeconds <= endSeconds))
            {
                var delta = point.GapSeconds - ReferenceGapAt(referencePoints, point.AxisSeconds);
                hasLocalComparison |= !selection.State.IsReferenceCar && Math.Abs(delta) > 0.001d;
                if (delta < 0d)
                {
                    maxAheadSeconds = Math.Max(maxAheadSeconds, Math.Abs(delta));
                }
                else
                {
                    maxBehindSeconds = Math.Max(maxBehindSeconds, delta);
                }
            }
        }

        var minimumRange = FocusScaleMinimumRange();
        var aheadRange = NiceCeiling(Math.Max(minimumRange, maxAheadSeconds * FocusScalePaddingMultiplier));
        var behindRange = NiceCeiling(Math.Max(minimumRange, maxBehindSeconds * FocusScalePaddingMultiplier));
        var localRange = Math.Max(aheadRange, behindRange);
        var forceFocusScaleForLappedReference = ShouldForceFocusScaleForLappedReference(latestReferenceGap);
        if (!forceFocusScaleForLappedReference
            && (!hasLocalComparison || leaderScaleMax < Math.Max(triggerGap, localRange * FocusScaleTriggerRatio)))
        {
            return GapScale.Leader(leaderScaleMax);
        }

        return GapScale.FocusRelative(
            leaderScaleMax,
            aheadRange,
            behindRange,
            referencePoints,
            latestReferenceGap);
    }

    private bool ShouldUseForGapScale(ChartSeriesSelection selection)
    {
        return GapToLeaderPresentationRules.ShouldUseForFocusScale(
            selection.State.IsReferenceCar,
            selection.State.IsClassLeader,
            selection.IsStale,
            selection.IsStickyExit,
            selection.State.IsCurrentlyDesired,
            selection.State.DeltaSecondsToReference,
            FocusScaleCandidateRangeSeconds());
    }

    private double FocusScaleCandidateRangeSeconds()
    {
        var lapScaledRange = _lapReferenceSeconds is { } lapSeconds && IsValidLapReference(lapSeconds)
            ? lapSeconds * FocusScaleCandidateRangeLaps
            : 0d;
        return Math.Min(
            FocusScaleCandidateRangeMaximumSeconds,
            Math.Max(FocusScaleCandidateRangeMinimumSeconds, lapScaledRange));
    }

    private double FocusScaleMinimumReferenceGap()
    {
        return GapToLeaderPresentationRules.FocusScaleTriggerSeconds(
            _lapReferenceSeconds,
            FocusScaleMinimumReferenceGapSeconds,
            FocusScaleMinimumReferenceGapLaps);
    }

    private double FocusScaleMinimumRange()
    {
        return Math.Max(
            FocusScaleMinimumRangeSeconds,
            _lapReferenceSeconds is { } lapSeconds && IsValidLapReference(lapSeconds)
                ? lapSeconds * FocusScaleMinimumRangeLaps
                : 0d);
    }

    private bool ShouldForceFocusScaleForLappedReference(double latestReferenceGap)
    {
        return _lapReferenceSeconds is { } lapSeconds
            && IsValidLapReference(lapSeconds)
            && latestReferenceGap >= lapSeconds * SameLapReferenceBoundaryLaps;
    }

    private static PointF ToGraphPoint(
        GapTrendPoint point,
        TrendDomain domain,
        GapScale gapScale,
        Rectangle plotBounds)
    {
        return new PointF(
            AxisSecondsToX(point.AxisSeconds, domain, plotBounds),
            GapToY(point, gapScale, plotBounds));
    }

    private static PointF ToGraphPoint(
        DriverChangeMarker marker,
        TrendDomain domain,
        GapScale gapScale,
        Rectangle plotBounds)
    {
        return new PointF(
            AxisSecondsToX(marker.AxisSeconds, domain, plotBounds),
            GapToY(marker.AxisSeconds, marker.GapSeconds, gapScale, plotBounds));
    }

    private static double ReferenceGapAt(IReadOnlyList<GapTrendPoint> referencePoints, double axisSeconds)
    {
        if (referencePoints.Count == 0)
        {
            return 0d;
        }

        if (axisSeconds <= referencePoints[0].AxisSeconds)
        {
            return referencePoints[0].GapSeconds;
        }

        var last = referencePoints[^1];
        if (axisSeconds >= last.AxisSeconds)
        {
            return last.GapSeconds;
        }

        var low = 0;
        var high = referencePoints.Count - 1;
        while (low <= high)
        {
            var mid = low + (high - low) / 2;
            var midSeconds = referencePoints[mid].AxisSeconds;
            if (Math.Abs(midSeconds - axisSeconds) < 0.001d)
            {
                return referencePoints[mid].GapSeconds;
            }

            if (midSeconds < axisSeconds)
            {
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        var after = referencePoints[Math.Clamp(low, 0, referencePoints.Count - 1)];
        var before = referencePoints[Math.Clamp(low - 1, 0, referencePoints.Count - 1)];
        var span = after.AxisSeconds - before.AxisSeconds;
        if (span <= 0.001d)
        {
            return before.GapSeconds;
        }

        var ratio = Math.Clamp((axisSeconds - before.AxisSeconds) / span, 0d, 1d);
        return before.GapSeconds + (after.GapSeconds - before.GapSeconds) * ratio;
    }

    private static float AxisSecondsToX(double axisSeconds, TrendDomain domain, Rectangle plotBounds)
    {
        var totalSeconds = Math.Max(1d, domain.EndSeconds - domain.StartSeconds);
        var ratio = Math.Clamp((axisSeconds - domain.StartSeconds) / totalSeconds, 0d, 1d);
        return plotBounds.Left + (float)(ratio * plotBounds.Width);
    }

    private static void DrawPoint(Graphics graphics, PointF point, Color color, float radius)
    {
        using var brush = new SolidBrush(color);
        graphics.FillEllipse(brush, point.X - radius, point.Y - radius, radius * 2f, radius * 2f);
    }

    private static void DrawSeriesSegments(
        Graphics graphics,
        IReadOnlyList<GapTrendPoint> points,
        Pen pen,
        Color color,
        bool isReferenceCar,
        TrendDomain domain,
        GapScale gapScale,
        Rectangle plotBounds)
    {
        var segment = new List<PointF>();
        foreach (var point in points)
        {
            if (point.StartsSegment && segment.Count > 0)
            {
                DrawSegment(graphics, segment, pen, color, isReferenceCar);
                segment.Clear();
            }

            segment.Add(ToGraphPoint(point, domain, gapScale, plotBounds));
        }

        DrawSegment(graphics, segment, pen, color, isReferenceCar);
    }

    private static void DrawSegment(Graphics graphics, IReadOnlyList<PointF> segment, Pen pen, Color color, bool isReferenceCar)
    {
        if (segment.Count == 0)
        {
            return;
        }

        if (segment.Count == 1)
        {
            DrawPoint(graphics, segment[0], color, isReferenceCar ? 4f : 3f);
            return;
        }

        graphics.DrawLines(pen, segment.ToArray());
        DrawPoint(graphics, segment[^1], color, isReferenceCar ? 4.5f : 3f);
    }

    private static void AddPositionLabel(
        List<EndpointLabel> labels,
        CarRenderState state,
        GapTrendPoint point,
        Color color,
        TrendDomain domain,
        GapScale gapScale,
        Rectangle plotBounds)
    {
        if (state.ClassPosition is not { } position || position <= 0)
        {
            return;
        }

        var graphPoint = ToGraphPoint(point, domain, gapScale, plotBounds);
        labels.Add(new EndpointLabel(
            $"P{position}",
            graphPoint,
            color,
            state.IsReferenceCar,
            state.IsClassLeader));
    }

    private void DrawPositionLabels(Graphics graphics, IReadOnlyList<EndpointLabel> labels, Rectangle plotBounds)
    {
        if (labels.Count == 0)
        {
            return;
        }

        const float labelHeight = 13f;
        const float labelGap = 1f;
        var ordered = labels
            .OrderBy(label => label.Point.Y)
            .ThenByDescending(label => label.IsReferenceCar)
            .ThenByDescending(label => label.IsClassLeader)
            .Select(label => new PositionedEndpointLabel(label, label.Point.Y - labelHeight / 2f))
            .ToArray();

        var minY = plotBounds.Top + 1f;
        var maxY = plotBounds.Bottom - labelHeight - 1f;
        for (var index = 0; index < ordered.Length; index++)
        {
            var y = Math.Clamp(ordered[index].Y, minY, maxY);
            if (index > 0)
            {
                y = Math.Max(y, ordered[index - 1].Y + labelHeight + labelGap);
            }

            ordered[index] = ordered[index] with { Y = y };
        }

        if (ordered[^1].Y > maxY)
        {
            var shift = ordered[^1].Y - maxY;
            for (var index = 0; index < ordered.Length; index++)
            {
                ordered[index] = ordered[index] with { Y = Math.Max(minY, ordered[index].Y - shift) };
            }
        }

        foreach (var positioned in ordered)
        {
            DrawPositionLabel(graphics, positioned.Label, positioned.Y, plotBounds);
        }
    }

    private void DrawPositionLabel(
        Graphics graphics,
        EndpointLabel label,
        float y,
        Rectangle plotBounds)
    {
        const float labelHeight = 13f;
        using var font = OverlayTheme.Font(_fontFamily, label.IsReferenceCar ? 7.5f : 7f);
        var textSize = graphics.MeasureString(label.Text, font);
        var x = Math.Min(plotBounds.Right - textSize.Width - 2f, label.Point.X + 6f);
        var labelBounds = new RectangleF(x - 2f, y, textSize.Width + 4f, labelHeight);

        if (Math.Abs(y + 6.5f - label.Point.Y) > 3f)
        {
            using var connectorPen = new Pen(WithAlpha(label.Color, 0.32d), 1f);
            graphics.DrawLine(connectorPen, label.Point.X + 3f, label.Point.Y, labelBounds.Left, y + 6.5f);
        }

        using var backgroundBrush = new SolidBrush(Color.FromArgb(label.IsReferenceCar ? 188 : 150, 18, 30, 42));
        using var textBrush = new SolidBrush(WithAlpha(label.Color, label.IsReferenceCar ? 1d : 0.78d));
        graphics.FillRectangle(backgroundBrush, labelBounds);
        graphics.DrawString(label.Text, font, textBrush, x, y - 1f);
    }

    private static void DrawTerminalMarker(
        Graphics graphics,
        GapTrendPoint point,
        Color color,
        TrendDomain domain,
        GapScale gapScale,
        Rectangle plotBounds)
    {
        var graphPoint = ToGraphPoint(point, domain, gapScale, plotBounds);
        using var pen = new Pen(color, 1.2f);
        graphics.DrawLine(pen, graphPoint.X - 4f, graphPoint.Y - 4f, graphPoint.X + 4f, graphPoint.Y + 4f);
        graphics.DrawLine(pen, graphPoint.X - 4f, graphPoint.Y + 4f, graphPoint.X + 4f, graphPoint.Y - 4f);
    }

    private void DrawLeaderChangeMarkers(Graphics graphics, Rectangle plotBounds, TrendDomain domain)
    {
        var markers = _leaderChangeMarkers
            .Where(marker => marker.AxisSeconds >= domain.StartSeconds && marker.AxisSeconds <= domain.EndSeconds)
            .ToArray();
        if (markers.Length == 0)
        {
            return;
        }

        using var markerPen = new Pen(Color.FromArgb(115, 255, 255, 255), 1f)
        {
            DashStyle = DashStyle.Dot
        };
        using var labelFont = OverlayTheme.Font(_fontFamily, 7f);
        using var labelBrush = new SolidBrush(Color.FromArgb(150, 218, 226, 230));

        foreach (var marker in markers)
        {
            var x = AxisSecondsToX(marker.AxisSeconds, domain, plotBounds);
            graphics.DrawLine(markerPen, x, plotBounds.Top, x, plotBounds.Bottom);
            graphics.DrawString("leader", labelFont, labelBrush, x + 4f, plotBounds.Top + 4f);
        }
    }

    private void DrawWeatherBands(Graphics graphics, Rectangle plotBounds, TrendDomain domain)
    {
        if (_weather.Count == 0)
        {
            return;
        }

        var points = _weather
            .Where(point => point.AxisSeconds <= domain.EndSeconds)
            .OrderBy(point => point.AxisSeconds)
            .ToArray();
        if (points.Length == 0)
        {
            return;
        }

        var startIndex = Array.FindLastIndex(points, point => point.AxisSeconds <= domain.StartSeconds);
        var firstIndex = startIndex >= 0 ? startIndex : Array.FindIndex(points, point => point.AxisSeconds >= domain.StartSeconds);
        if (firstIndex < 0)
        {
            return;
        }

        for (var index = firstIndex; index < points.Length; index++)
        {
            var point = points[index];
            var segmentStart = Math.Max(domain.StartSeconds, point.AxisSeconds);
            var segmentEnd = index + 1 < points.Length
                ? Math.Min(domain.EndSeconds, points[index + 1].AxisSeconds)
                : domain.EndSeconds;
            if (segmentEnd <= domain.StartSeconds || segmentEnd <= segmentStart)
            {
                continue;
            }

            if (WeatherBandColor(point.Condition) is not { } color)
            {
                continue;
            }

            var left = AxisSecondsToX(segmentStart, domain, plotBounds);
            var right = AxisSecondsToX(segmentEnd, domain, plotBounds);
            var width = Math.Max(1f, right - left);
            using var brush = new SolidBrush(color);
            graphics.FillRectangle(brush, left, plotBounds.Top, width, plotBounds.Height);

            if (point.Condition == WeatherCondition.DeclaredWet)
            {
                using var stripBrush = new SolidBrush(Color.FromArgb(44, 94, 190, 255));
                graphics.FillRectangle(stripBrush, left, plotBounds.Top, width, 4f);
            }
        }
    }

    private void DrawDriverChangeMarkers(Graphics graphics, Rectangle plotBounds, TrendDomain domain, GapScale gapScale)
    {
        var markers = _driverChangeMarkers
            .Where(marker => marker.AxisSeconds >= domain.StartSeconds && marker.AxisSeconds <= domain.EndSeconds)
            .ToArray();
        if (markers.Length == 0)
        {
            return;
        }

        using var tickPen = new Pen(Color.FromArgb(205, 255, 255, 255), 1.2f);
        using var markerFill = new SolidBrush(Color.FromArgb(18, 30, 42));
        using var referenceMarkerPen = new Pen(Color.FromArgb(112, 224, 146), 1.8f);
        using var otherMarkerPen = new Pen(Color.FromArgb(220, 235, 245, 255), 1.4f);
        using var labelFont = OverlayTheme.Font(_fontFamily, 7f);
        using var labelBrush = new SolidBrush(Color.FromArgb(190, 205, 218, 228));

        foreach (var marker in markers)
        {
            var point = ToGraphPoint(marker, domain, gapScale, plotBounds);
            graphics.DrawLine(tickPen, point.X, point.Y - 9f, point.X, point.Y + 9f);
            graphics.FillEllipse(markerFill, point.X - 4.5f, point.Y - 4.5f, 9f, 9f);
            graphics.DrawEllipse(marker.IsReferenceCar ? referenceMarkerPen : otherMarkerPen, point.X - 4.5f, point.Y - 4.5f, 9f, 9f);
            graphics.DrawString(marker.Label, labelFont, labelBrush, point.X + 6f, point.Y - 16f);
        }
    }

    private void DrawGridLines(
        Graphics graphics,
        Rectangle plotBounds,
        Rectangle axisBounds,
        TrendDomain domain,
        GapScale gapScale,
        double? lapReferenceSeconds)
    {
        DrawLapIntervalLines(graphics, plotBounds, domain, lapReferenceSeconds);

        if (gapScale.IsFocusRelative)
        {
            DrawFocusRelativeGridLines(graphics, plotBounds, axisBounds, gapScale);
            return;
        }

        using var gridPen = new Pen(Color.FromArgb(34, 255, 255, 255), 1f);
        using var gridFont = OverlayTheme.Font(_fontFamily, 7f);
        using var gridBrush = new SolidBrush(Color.FromArgb(120, 138, 152, 160));
        using var labelFormat = new StringFormat
        {
            Alignment = StringAlignment.Far,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter
        };
        var step = NiceGridStep(gapScale.MaxGapSeconds / 4d);
        for (var value = step; value < gapScale.MaxGapSeconds; value += step)
        {
            var y = GapToY(value, gapScale.MaxGapSeconds, plotBounds);
            graphics.DrawLine(gridPen, plotBounds.Left, y, plotBounds.Right, y);
            DrawAxisLabel(graphics, FormatAxisSeconds(value), gridFont, gridBrush, axisBounds, y, labelFormat);
        }

        if (lapReferenceSeconds is not { } lapSeconds || lapSeconds < 20d || gapScale.MaxGapSeconds < lapSeconds * 0.85d)
        {
            return;
        }

        using var lapPen = new Pen(Color.FromArgb(150, 255, 255, 255), 1.25f);
        using var lapBrush = new SolidBrush(Color.FromArgb(205, 255, 255, 255));
        for (var lap = 1; lap * lapSeconds < gapScale.MaxGapSeconds; lap++)
        {
            var y = GapToY(lap * lapSeconds, gapScale.MaxGapSeconds, plotBounds);
            graphics.DrawLine(lapPen, plotBounds.Left, y, plotBounds.Right, y);
            DrawAxisLabel(graphics, $"+{lap} lap", gridFont, lapBrush, axisBounds, y, labelFormat);
        }
    }

    private void DrawFocusRelativeGridLines(Graphics graphics, Rectangle plotBounds, Rectangle axisBounds, GapScale gapScale)
    {
        using var gridPen = new Pen(Color.FromArgb(34, 255, 255, 255), 1f);
        using var referencePen = new Pen(Color.FromArgb(92, 112, 224, 146), 1.2f);
        using var gridFont = OverlayTheme.Font(_fontFamily, 7f);
        using var gridBrush = new SolidBrush(Color.FromArgb(120, 138, 152, 160));
        using var referenceBrush = new SolidBrush(Color.FromArgb(185, 112, 224, 146));
        using var labelFormat = new StringFormat
        {
            Alignment = StringAlignment.Far,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter
        };

        var referenceY = FocusReferenceY(plotBounds);
        graphics.DrawLine(referencePen, plotBounds.Left, referenceY, plotBounds.Right, referenceY);
        DrawAxisLabel(graphics, "focus", gridFont, referenceBrush, axisBounds, referenceY, labelFormat);

        var aheadStep = NiceGridStep(gapScale.AheadSeconds / 2d);
        for (var value = aheadStep; value < gapScale.AheadSeconds; value += aheadStep)
        {
            var y = GapDeltaToY(-value, gapScale, plotBounds);
            graphics.DrawLine(gridPen, plotBounds.Left, y, plotBounds.Right, y);
            DrawAxisLabel(graphics, FormatDeltaSeconds(-value), gridFont, gridBrush, axisBounds, y, labelFormat);
        }

        var behindStep = NiceGridStep(gapScale.BehindSeconds / 2d);
        for (var value = behindStep; value < gapScale.BehindSeconds; value += behindStep)
        {
            var y = GapDeltaToY(value, gapScale, plotBounds);
            graphics.DrawLine(gridPen, plotBounds.Left, y, plotBounds.Right, y);
            DrawAxisLabel(graphics, FormatDeltaSeconds(value), gridFont, gridBrush, axisBounds, y, labelFormat);
        }
    }

    private void DrawLapIntervalLines(
        Graphics graphics,
        Rectangle plotBounds,
        TrendDomain domain,
        double? lapReferenceSeconds)
    {
        if (lapReferenceSeconds is not { } lapSeconds || lapSeconds < 20d)
        {
            return;
        }

        var intervalSeconds = lapSeconds * 5d;
        var durationSeconds = domain.EndSeconds - domain.StartSeconds;
        if (durationSeconds < intervalSeconds * 0.75d)
        {
            return;
        }

        using var linePen = new Pen(Color.FromArgb(34, 255, 255, 255), 1f);
        using var labelFont = OverlayTheme.Font(_fontFamily, 7f);
        using var labelBrush = new SolidBrush(Color.FromArgb(128, 138, 152, 160));
        using var labelFormat = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Near,
            Trimming = StringTrimming.EllipsisCharacter
        };

        for (var elapsed = intervalSeconds; elapsed < durationSeconds; elapsed += intervalSeconds)
        {
            var x = plotBounds.Left + (float)(elapsed / durationSeconds * plotBounds.Width);
            graphics.DrawLine(linePen, x, plotBounds.Top, x, plotBounds.Bottom);
            graphics.DrawString(
                $"{elapsed / lapSeconds:0}L",
                labelFont,
                labelBrush,
                new RectangleF(x - 18f, plotBounds.Bottom + 1f, 36f, 12f),
                labelFormat);
        }
    }

    private void DrawScaleLabels(Graphics graphics, Rectangle plotBounds, Rectangle axisBounds, GapScale gapScale)
    {
        using var font = OverlayTheme.Font(_fontFamily, 7.5f);
        using var brush = new SolidBrush(Color.FromArgb(138, 152, 160));
        using var labelFormat = new StringFormat
        {
            Alignment = StringAlignment.Far,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter
        };
        DrawAxisLabel(graphics, ScaleTopReferenceLabel(gapScale), font, brush, axisBounds, plotBounds.Top, labelFormat);
        if (gapScale.IsFocusRelative)
        {
            DrawAxisLabel(graphics, FormatDeltaSeconds(-gapScale.AheadSeconds), font, brush, axisBounds, plotBounds.Top + FocusScaleTopPadding, labelFormat);
            DrawAxisLabel(graphics, FormatDeltaSeconds(gapScale.BehindSeconds), font, brush, axisBounds, plotBounds.Bottom - FocusScaleBottomPadding, labelFormat);
            return;
        }

        DrawAxisLabel(graphics, FormatAxisSeconds(gapScale.MaxGapSeconds), font, brush, axisBounds, plotBounds.Bottom, labelFormat);
    }

    private static void DrawAxisLabel(
        Graphics graphics,
        string text,
        Font font,
        Brush brush,
        Rectangle axisBounds,
        float y,
        StringFormat labelFormat)
    {
        graphics.DrawString(
            text,
            font,
            brush,
            new RectangleF(axisBounds.Left, y - 8f, axisBounds.Width, 16f),
            labelFormat);
    }

    private OverlayChromeState ChromeStateFor(
        LiveTelemetrySnapshot snapshot,
        LiveLeaderGapSnapshot gap,
        int selectedSeriesCount,
        double trendDurationSeconds,
        GapScale gapScale)
    {
        var showStatus = OverlayChromeSettings.ShowHeaderStatus(_settings, snapshot);
        var timeRemaining = OverlayChromeSettings.ShowHeaderTimeRemaining(_settings, snapshot)
            ? OverlayHeaderTimeFormatter.FormatTimeRemaining(snapshot)
            : string.Empty;
        var footerMode = OverlayChromeSettings.ShowFooterSource(_settings, snapshot)
            ? OverlayChromeFooterMode.Always
            : OverlayChromeFooterMode.Never;
        var status = gap.HasData
            ? $"{FormatPosition(gap.ReferenceClassPosition)} {FormatGap(gap.ClassLeaderGap)}"
            : "waiting";
        var scaleText = gapScale.IsFocusRelative ? "local scale" : "class trend";
        var source = gap.HasData
            ? $"{FormatTrendWindow(TimeSpan.FromSeconds(trendDurationSeconds))} {scaleText} | cars {selectedSeriesCount}"
            : "source: waiting";
        return new OverlayChromeState(
            "Class Gap Trend",
            showStatus ? status : string.Empty,
            gap.HasData ? OverlayChromeTone.Info : OverlayChromeTone.Waiting,
            source,
            footerMode,
            TimeRemaining: timeRemaining);
    }

    private void DrawWaitingForTiming(Graphics graphics, Rectangle graphBounds)
    {
        using var overlayBrush = new SolidBrush(Color.FromArgb(112, 10, 16, 23));
        graphics.FillRectangle(overlayBrush, graphBounds);

        using var font = OverlayTheme.Font(_fontFamily, 10f, FontStyle.Bold);
        using var detailFont = OverlayTheme.Font(_fontFamily, 8f);
        using var brush = new SolidBrush(OverlayTheme.Colors.TextMuted);
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };
        graphics.DrawString("waiting for timing", font, brush, graphBounds, format);

        var detailBounds = new Rectangle(
            graphBounds.Left,
            graphBounds.Top + graphBounds.Height / 2 + 14,
            graphBounds.Width,
            18);
        graphics.DrawString("trend will populate when live gaps are grounded", detailFont, brush, detailBounds, format);
    }

    private void DrawError(Graphics graphics, Rectangle graphBounds)
    {
        using var overlayBrush = new SolidBrush(Color.FromArgb(150, 42, 18, 22));
        graphics.FillRectangle(overlayBrush, graphBounds);

        using var borderPen = new Pen(Color.FromArgb(180, 236, 112, 99), 1f);
        graphics.DrawRectangle(borderPen, graphBounds);

        using var titleFont = OverlayTheme.Font(_fontFamily, 10f, FontStyle.Bold);
        using var detailFont = OverlayTheme.Font(_fontFamily, 8f);
        using var titleBrush = new SolidBrush(Color.FromArgb(238, 255, 225, 220));
        using var detailBrush = new SolidBrush(Color.FromArgb(205, 255, 225, 220));
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter
        };
        graphics.DrawString("gap graph error", titleFont, titleBrush, graphBounds, format);
        var detailBounds = new RectangleF(graphBounds.Left + 24f, graphBounds.Top + graphBounds.Height / 2f + 14f, graphBounds.Width - 48f, 28f);
        graphics.DrawString(TrimError(_overlayError), detailFont, detailBrush, detailBounds, format);
    }

    private string ScaleTopReferenceLabel(GapScale gapScale)
    {
        if (!gapScale.IsFocusRelative || FocusIsSameLapAsClassLeader())
        {
            return "leader";
        }

        return HighestEligibleReferencePosition() is { } position
            ? $"P{position}"
            : ClosestIneligibleHigherReferenceLabel() ?? "best";
    }

    private bool FocusIsSameLapAsClassLeader()
    {
        var reference = _gap.ClassCars.FirstOrDefault(car => car.IsReferenceCar);
        if (reference is null)
        {
            return _gap.ClassLeaderGap.IsLeader;
        }

        if (reference.IsClassLeader)
        {
            return true;
        }

        var leader = _gap.ClassCars.FirstOrDefault(car => car.IsClassLeader);
        return leader is not null && IsSameLapReferenceCandidate(leader, reference);
    }

    private int? HighestEligibleReferencePosition()
    {
        var reference = _gap.ClassCars.FirstOrDefault(car => car.IsReferenceCar);
        if (reference is null)
        {
            return null;
        }

        var referencePosition = reference.ClassPosition ?? _gap.ReferenceClassPosition;
        return _gap.ClassCars
            .Where(car => !car.IsReferenceCar
                && !car.IsClassLeader
                && referencePosition is not null
                && car.ClassPosition is not null
                && car.ClassPosition < referencePosition
                && IsSameLapReferenceCandidate(car, reference))
            .Select(car => car.ClassPosition)
            .Min();
    }

    private string? ClosestIneligibleHigherReferenceLabel()
    {
        var reference = _gap.ClassCars.FirstOrDefault(car => car.IsReferenceCar);
        if (reference is null
            || (reference.ClassPosition ?? _gap.ReferenceClassPosition) is not { } referencePosition
            || NormalizedClassLeaderGapLaps(reference) is not { } referenceGapLaps)
        {
            return null;
        }

        var closest = _gap.ClassCars
            .Where(car => !car.IsReferenceCar
                && car.ClassPosition is not null
                && car.ClassPosition < referencePosition)
            .Select(car => new
            {
                car.ClassPosition,
                LapDelta = NormalizedClassLeaderGapLaps(car) is { } candidateGapLaps
                    ? referenceGapLaps - candidateGapLaps
                    : double.NaN
            })
            .Where(car => !double.IsNaN(car.LapDelta) && car.LapDelta >= SameLapReferenceBoundaryLaps)
            .MinBy(car => car.LapDelta);

        return closest is null
            ? null
            : $"P{closest.ClassPosition} {FormatLapDelta(closest.LapDelta)}";
    }

    private bool IsSameLapReferenceCandidate(LiveClassGapCar candidate, LiveClassGapCar reference)
    {
        return NormalizedClassLeaderGapLaps(candidate) is { } candidateGapLaps
            && NormalizedClassLeaderGapLaps(reference) is { } referenceGapLaps
            && Math.Abs(candidateGapLaps - referenceGapLaps) < SameLapReferenceBoundaryLaps;
    }

    private bool IsLappedGraphGap(LiveClassGapCar car)
    {
        return ChartGapSeconds(car) is { } gapSeconds
            && _lapReferenceSeconds is { } lapSeconds
            && IsValidLapReference(lapSeconds)
            && gapSeconds >= lapSeconds * SameLapReferenceBoundaryLaps;
    }

    private double? NormalizedClassLeaderGapLaps(LiveClassGapCar car)
    {
        if (car.GapLapsToClassLeader is { } laps && !double.IsNaN(laps) && !double.IsInfinity(laps))
        {
            return laps;
        }

        if (ChartGapSeconds(car) is { } seconds
            && _lapReferenceSeconds is { } lapReferenceSeconds
            && IsValidLapReference(lapReferenceSeconds))
        {
            return seconds / lapReferenceSeconds;
        }

        return null;
    }

    private static string FormatLapDelta(double lapDelta)
    {
        var rounded = Math.Round(lapDelta);
        var useRounded = Math.Abs(lapDelta - rounded) < 0.08d;
        var value = useRounded ? rounded.ToString("0") : lapDelta.ToString("0.0");
        var pluralValue = useRounded ? rounded : lapDelta;
        var plural = Math.Abs(pluralValue - 1d) < 0.01d ? "lap" : "laps";
        return $"+{value} {plural}";
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
            _logger.LogError(exception, "Gap-to-leader overlay {Operation} failed.", operation);
        }
    }

    private void PruneOldPoints(double latestAxisSeconds)
    {
        var cutoff = latestAxisSeconds - TrendWindow.TotalSeconds;
        foreach (var carIdx in _series.Keys.ToArray())
        {
            _series[carIdx].RemoveAll(point => point.AxisSeconds < cutoff);
            if (_series[carIdx].Count == 0)
            {
                _series.Remove(carIdx);
            }
        }

        _weather.RemoveAll(point => point.AxisSeconds < cutoff);
        _driverChangeMarkers.RemoveAll(marker => marker.AxisSeconds < cutoff);
        _leaderChangeMarkers.RemoveAll(marker => marker.AxisSeconds < cutoff);
        foreach (var carIdx in _carRenderStates.Keys.ToArray())
        {
            if (!_series.ContainsKey(carIdx)
                && _carRenderStates[carIdx].LastDesiredAxisSeconds is { } lastDesired
                && latestAxisSeconds - lastDesired > StickyVisibilitySeconds())
            {
                _carRenderStates.Remove(carIdx);
            }
        }
    }

    private static Color SeriesColor(CarRenderState car)
    {
        if (car.IsClassLeader)
        {
            return Color.FromArgb(235, 255, 255, 255);
        }

        if (car.IsReferenceCar)
        {
            return Color.FromArgb(112, 224, 146);
        }

        return car.DeltaSecondsToReference is not null && car.DeltaSecondsToReference.Value < 0d
            ? Color.FromArgb(140, 190, 245)
            : Color.FromArgb(246, 184, 88);
    }

    private static double SeriesAlphaMultiplier(CarRenderState car)
    {
        return car.IsClassLeader || car.IsReferenceCar
            ? 1d
            : 0.48d;
    }

    private static Color WithAlpha(Color color, double alpha)
    {
        return Color.FromArgb(
            (int)Math.Clamp(color.A * alpha, 0d, 255d),
            color.R,
            color.G,
            color.B);
    }

    private double? ChartGapSeconds(LiveClassGapCar car)
    {
        return car.GapSecondsToClassLeader
            ?? (car.GapLapsToClassLeader is { } laps ? laps * (_lapReferenceSeconds ?? 60d) : null);
    }

    private static bool ShouldConnectGapSeriesPoint(double previousSeconds, double nextSeconds, double? lapReferenceSeconds)
    {
        if (!IsFinite(previousSeconds) || !IsFinite(nextSeconds))
        {
            return false;
        }

        var maximumJump = Math.Max(8d, Math.Min(45d, (lapReferenceSeconds ?? 90d) * 0.25d));
        return Math.Abs(nextSeconds - previousSeconds) <= maximumJump;
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }

    private static WeatherCondition SelectWeatherCondition(LiveTelemetrySnapshot snapshot)
    {
        var weather = snapshot.Models.Weather;
        if (!weather.HasData)
        {
            return WeatherCondition.Unknown;
        }

        if (weather.WeatherDeclaredWet == true)
        {
            return WeatherCondition.DeclaredWet;
        }

        return weather.TrackWetness switch
        {
            >= 4 => WeatherCondition.Wet,
            >= 2 => WeatherCondition.Damp,
            >= 0 => WeatherCondition.Dry,
            _ => WeatherCondition.Unknown
        };
    }

    private static Color? WeatherBandColor(WeatherCondition condition)
    {
        return condition switch
        {
            WeatherCondition.Damp => Color.FromArgb(12, 75, 170, 205),
            WeatherCondition.Wet => Color.FromArgb(20, 70, 135, 230),
            WeatherCondition.DeclaredWet => Color.FromArgb(28, 78, 142, 238),
            _ => null
        };
    }

    private static DriverIdentity? ToDriverIdentity(HistoricalSessionDriver driver)
    {
        if (driver.CarIdx is not { } carIdx || driver.IsSpectator == true)
        {
            return null;
        }

        var key = driver.UserId is { } userId && userId > 0
            ? FormattableString.Invariant($"id:{userId}")
            : !string.IsNullOrWhiteSpace(driver.UserName)
                ? $"name:{driver.UserName.Trim().ToUpperInvariant()}"
                : null;
        if (key is null)
        {
            return null;
        }

        return new DriverIdentity(
            carIdx,
            key,
            SelectDriverLabel(driver));
    }

    private static string SelectDriverLabel(HistoricalSessionDriver driver)
    {
        foreach (var value in new[] { driver.Initials, driver.AbbrevName, driver.UserName })
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim().Length <= 3
                    ? value.Trim()
                    : value.Trim()[..Math.Min(3, value.Trim().Length)].ToUpperInvariant();
            }
        }

        return "DR";
    }

    private static double SelectAxisSeconds(DateTimeOffset timestampUtc, double? sessionTimeSeconds)
    {
        return sessionTimeSeconds is { } sessionTime
            && sessionTime >= 0d
            && !double.IsNaN(sessionTime)
            && !double.IsInfinity(sessionTime)
            ? sessionTime
            : timestampUtc.ToUnixTimeMilliseconds() / 1000d;
    }

    private static double? SelectLapReferenceSeconds(LiveTelemetrySnapshot snapshot)
    {
        var focusRow = snapshot.Models.Timing.FocusRow;
        if (IsValidLapReference(focusRow?.LastLapTimeSeconds))
        {
            return focusRow?.LastLapTimeSeconds;
        }

        if (IsValidLapReference(focusRow?.BestLapTimeSeconds))
        {
            return focusRow?.BestLapTimeSeconds;
        }

        if (ReferenceUsesPlayerCar(snapshot) && IsValidLapReference(snapshot.Models.FuelPit.Fuel.LapTimeSeconds))
        {
            return snapshot.Models.FuelPit.Fuel.LapTimeSeconds;
        }

        if (IsValidLapReference(snapshot.Models.RaceProgress.StrategyLapTimeSeconds))
        {
            return snapshot.Models.RaceProgress.StrategyLapTimeSeconds;
        }

        if (ReferenceUsesPlayerCar(snapshot) && IsValidLapReference(snapshot.Context.Car.DriverCarEstLapTimeSeconds))
        {
            return snapshot.Context.Car.DriverCarEstLapTimeSeconds;
        }

        return ReferenceUsesTeamClass(snapshot) && IsValidLapReference(snapshot.Context.Car.CarClassEstLapTimeSeconds)
            ? snapshot.Context.Car.CarClassEstLapTimeSeconds
            : null;
    }

    private static bool ReferenceUsesTeamClass(LiveTelemetrySnapshot snapshot)
    {
        var reference = snapshot.Models.Reference;
        if (reference.HasData && reference.ReferenceCarClass is not null && reference.PlayerCarClass is not null)
        {
            return reference.ReferenceCarClass == reference.PlayerCarClass;
        }

        var directory = snapshot.Models.DriverDirectory;
        if (directory.FocusCarIdx is null)
        {
            return false;
        }

        var focusClass = directory.FocusDriver?.CarClassId ?? directory.ReferenceCarClass;
        var playerClass = directory.PlayerDriver?.CarClassId ?? directory.ReferenceCarClass;
        return focusClass is null
            || playerClass is null
            || focusClass == playerClass;
    }

    private static ReferenceContext? SelectReferenceContext(LiveTelemetrySnapshot snapshot)
    {
        var directory = snapshot.Models.DriverDirectory;
        var reference = snapshot.Models.Reference;
        if (!directory.HasData && !reference.HasData)
        {
            return null;
        }

        var referenceCarIdx = reference.FocusCarIdx ?? directory.FocusCarIdx;
        if (referenceCarIdx is null)
        {
            return null;
        }

        var referenceClass = reference.ReferenceCarClass
            ?? (ReferenceUsesPlayerCar(snapshot)
                ? directory.FocusDriver?.CarClassId ?? directory.PlayerDriver?.CarClassId ?? directory.ReferenceCarClass
                : directory.FocusDriver?.CarClassId ?? directory.ReferenceCarClass);
        return new ReferenceContext(referenceCarIdx, referenceClass);
    }

    private static bool IsValidLapReference(double? seconds)
    {
        return seconds is { } value && value is > 20d and < 1800d && !double.IsNaN(value) && !double.IsInfinity(value);
    }

    private static float GapToY(GapTrendPoint point, GapScale gapScale, Rectangle plotBounds)
    {
        return GapToY(point.AxisSeconds, point.GapSeconds, gapScale, plotBounds);
    }

    private static float GapToY(double axisSeconds, double gapSeconds, GapScale gapScale, Rectangle plotBounds)
    {
        if (!gapScale.IsFocusRelative)
        {
            return GapToY(gapSeconds, gapScale.MaxGapSeconds, plotBounds);
        }

        var referenceGap = ReferenceGapAt(gapScale.ReferencePoints, axisSeconds);
        return GapDeltaToY(gapSeconds - referenceGap, gapScale, plotBounds);
    }

    private static float GapDeltaToY(double deltaSeconds, GapScale gapScale, Rectangle plotBounds)
    {
        var referenceY = FocusReferenceY(plotBounds);
        if (deltaSeconds < 0d)
        {
            var ratio = Math.Clamp(Math.Abs(deltaSeconds) / Math.Max(1d, gapScale.AheadSeconds), 0d, 1d);
            return referenceY - (float)(ratio * Math.Max(1f, referenceY - (plotBounds.Top + FocusScaleTopPadding)));
        }

        var behindRatio = Math.Clamp(deltaSeconds / Math.Max(1d, gapScale.BehindSeconds), 0d, 1d);
        return referenceY + (float)(behindRatio * Math.Max(1f, plotBounds.Bottom - FocusScaleBottomPadding - referenceY));
    }

    private static float FocusReferenceY(Rectangle plotBounds)
    {
        return plotBounds.Top + plotBounds.Height * FocusScaleReferenceRatio;
    }

    private static float GapToY(double gapSeconds, double maxGapSeconds, Rectangle plotBounds)
    {
        return plotBounds.Top + (float)(Math.Clamp(gapSeconds / maxGapSeconds, 0d, 1d) * plotBounds.Height);
    }

    private static double NiceCeiling(double value)
    {
        if (value <= 1d)
        {
            return 1d;
        }

        var magnitude = Math.Pow(10d, Math.Floor(Math.Log10(value)));
        var normalized = value / magnitude;
        foreach (var step in new[] { 1d, 1.5d, 2d, 3d, 5d, 7.5d, 10d })
        {
            if (normalized <= step)
            {
                return step * magnitude;
            }
        }

        return 10d * magnitude;
    }

    private static double NiceGridStep(double value)
    {
        if (value <= 0.25d)
        {
            return 0.25d;
        }

        var magnitude = Math.Pow(10d, Math.Floor(Math.Log10(value)));
        var normalized = value / magnitude;
        foreach (var step in new[] { 1d, 2d, 2.5d, 5d, 10d })
        {
            if (normalized <= step)
            {
                return step * magnitude;
            }
        }

        return 10d * magnitude;
    }

    private static string FormatAxisSeconds(double seconds)
    {
        if (seconds < 10d)
        {
            return FormattableString.Invariant($"+{seconds:0.#}s");
        }

        return FormatGapSeconds(seconds);
    }

    private static string FormatDeltaSeconds(double seconds)
    {
        var sign = seconds > 0d ? "+" : seconds < 0d ? "-" : string.Empty;
        var absolute = Math.Abs(seconds);
        return absolute >= 60d
            ? FormattableString.Invariant($"{sign}{Math.Floor(absolute / 60d):0}:{absolute % 60d:00.0}")
            : FormattableString.Invariant($"{sign}{absolute:0.0}s");
    }

    private static string FormatTrendWindow(TimeSpan trendWindow)
    {
        return trendWindow.TotalHours >= 1d
            ? FormattableString.Invariant($"{trendWindow.TotalHours:0.#}h")
            : FormattableString.Invariant($"{trendWindow.TotalMinutes:0}m");
    }

    private static string FormatPosition(int? position)
    {
        return position is { } value && value > 0
            ? value.ToString("0")
            : "--";
    }

    private static string FormatGap(LiveGapValue gap)
    {
        if (!gap.HasData)
        {
            return "--";
        }

        if (gap.IsLeader)
        {
            return "leader";
        }

        return gap.Seconds is { } seconds
            ? FormatGapSeconds(seconds)
            : gap.Laps is { } laps
                ? FormattableString.Invariant($"+{laps:0.00} lap")
                : "--";
    }

    private static string FormatGapSeconds(double seconds)
    {
        return seconds >= 60d
            ? FormattableString.Invariant($"+{Math.Floor(seconds / 60d):0}:{seconds % 60d:00.0}")
            : FormattableString.Invariant($"+{seconds:0.0}s");
    }

    private static string TrimError(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return "see logs";
        }

        return message.Length <= 72 ? message : string.Concat(message.AsSpan(0, 69), "...");
    }

    private sealed record GapTrendPoint(
        DateTimeOffset TimestampUtc,
        double AxisSeconds,
        double GapSeconds,
        bool IsReferenceCar,
        bool IsClassLeader,
        int? ClassPosition,
        bool StartsSegment);

    private sealed record GapScale(
        double MaxGapSeconds,
        bool IsFocusRelative,
        double AheadSeconds,
        double BehindSeconds,
        IReadOnlyList<GapTrendPoint> ReferencePoints,
        double LatestReferenceGapSeconds)
    {
        public static GapScale Leader(double maxGapSeconds)
        {
            return new GapScale(
                MaxGapSeconds: maxGapSeconds,
                IsFocusRelative: false,
                AheadSeconds: 0d,
                BehindSeconds: 0d,
                ReferencePoints: [],
                LatestReferenceGapSeconds: 0d);
        }

        public static GapScale FocusRelative(
            double maxGapSeconds,
            double aheadSeconds,
            double behindSeconds,
            IReadOnlyList<GapTrendPoint> referencePoints,
            double latestReferenceGapSeconds)
        {
            return new GapScale(
                MaxGapSeconds: maxGapSeconds,
                IsFocusRelative: true,
                AheadSeconds: aheadSeconds,
                BehindSeconds: behindSeconds,
                ReferencePoints: referencePoints,
                LatestReferenceGapSeconds: latestReferenceGapSeconds);
        }
    }

    private sealed record WeatherTrendPoint(double AxisSeconds, WeatherCondition Condition);

    private sealed record LeaderChangeMarker(
        DateTimeOffset TimestampUtc,
        double AxisSeconds,
        int PreviousLeaderCarIdx,
        int NewLeaderCarIdx);

    private sealed record DriverChangeMarker(
        DateTimeOffset TimestampUtc,
        double AxisSeconds,
        int CarIdx,
        double GapSeconds,
        bool IsReferenceCar,
        string Label,
        DriverChangeMarkerSource Source);

    private sealed record DriverIdentity(
        int CarIdx,
        string DriverKey,
        string ShortLabel)
    {
        public bool HasSameDriver(DriverIdentity other)
        {
            return string.Equals(DriverKey, other.DriverKey, StringComparison.Ordinal);
        }
    }

    private sealed record ChartSeriesSelection(
        CarRenderState State,
        double Alpha,
        bool IsStickyExit,
        bool IsStale,
        double DrawStartSeconds);

    private sealed record EndpointLabel(
        string Text,
        PointF Point,
        Color Color,
        bool IsReferenceCar,
        bool IsClassLeader);

    private sealed record PositionedEndpointLabel(EndpointLabel Label, float Y);

    private sealed record ReferenceContext(int? CarIdx, int? CarClass);

    private sealed class CarRenderState(int carIdx)
    {
        public int CarIdx { get; } = carIdx;

        public double LastSeenAxisSeconds { get; set; }

        public double LastGapSeconds { get; set; }

        public double? LastDesiredAxisSeconds { get; set; }

        public double? VisibleSinceAxisSeconds { get; set; }

        public bool IsCurrentlyDesired { get; set; }

        public bool IsReferenceCar { get; set; }

        public bool IsClassLeader { get; set; }

        public int? ClassPosition { get; set; }

        public double? DeltaSecondsToReference { get; set; }

        public double GapSortValue => LastGapSeconds;
    }

    private sealed record TrendDomain(double StartSeconds, double EndSeconds)
    {
        public double DurationSeconds => Math.Max(1d, EndSeconds - StartSeconds);
    }

    private enum WeatherCondition
    {
        Unknown,
        Dry,
        Damp,
        Wet,
        DeclaredWet
    }

    private enum DriverChangeMarkerSource
    {
        TeamControl,
        SessionInfo
    }
}
