using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using Microsoft.Extensions.Logging;
using TmrOverlay.App.History;
using TmrOverlay.App.Overlays.Abstractions;
using TmrOverlay.App.Overlays.CarRadar;
using TmrOverlay.App.Overlays.Content;
using TmrOverlay.App.Overlays.Flags;
using TmrOverlay.App.Overlays.FuelCalculator;
using TmrOverlay.App.Overlays.GapToLeader;
using TmrOverlay.App.Overlays.InputState;
using TmrOverlay.App.Overlays.PitService;
using TmrOverlay.App.Overlays.Relative;
using TmrOverlay.App.Overlays.SessionWeather;
using TmrOverlay.App.Overlays.SimpleTelemetry;
using TmrOverlay.App.Overlays.Standings;
using TmrOverlay.App.Overlays.StreamChat;
using TmrOverlay.App.Overlays.Styling;
using TmrOverlay.App.Overlays.TrackMap;
using TmrOverlay.App.TrackMaps;
using TmrOverlay.App.Performance;
using TmrOverlay.Core.Fuel;
using TmrOverlay.Core.History;
using TmrOverlay.Core.Overlays;
using TmrOverlay.Core.Settings;
using TmrOverlay.Core.Telemetry.Live;
using TmrOverlay.Core.TrackMaps;

namespace TmrOverlay.App.Overlays.DesignV2;

internal enum DesignV2LiveOverlayKind
{
    Standings,
    FuelCalculator,
    Relative,
    TrackMap,
    StreamChat,
    Flags,
    SessionWeather,
    PitService,
    InputState,
    CarRadar,
    GapToLeader
}

internal sealed class DesignV2LiveOverlayForm : PersistentOverlayForm, IUnitSystemAwareOverlay
{
    private const int DefaultRefreshIntervalMilliseconds = 250;
    private const int PaddingSize = 16;
    private const int HeaderHeight = 38;
    private const int FooterHeight = 32;
    private const int BodyGap = 12;
    private const int StreamChatCloseButtonSize = 22;
    private const int StreamChatCloseButtonRightMargin = 10;
    private const int StreamChatCloseButtonTop = 9;
    private const int StreamChatCloseButtonStatusReserve = 34;
    private const int RowHeight = 30;
    private const int RowGap = 5;
    private const int ColumnGap = 8;
    private const float PlainMetricRowHeight = 30f;
    private const float SegmentedMetricRowHeight = 35f;
    private const float DirectionalMetricRowHeight = 37.5f;
    private const float MetricRowGap = 5f;
    private const float MetricSectionTitleHeight = 11f;
    private const float MetricSectionTitleBottomGap = 4f;
    private const float MetricSectionGap = 8f;
    private const float TableHeaderTop = 5f;
    private const float TableHeaderHeight = 25f;
    private const float TableBodyTop = TableHeaderTop + TableHeaderHeight + RowGap;
    private const float TableRowHeight = 28f;
    private const float TablePlaceholderRowHeight = 14f;
    private const float TableClassHeaderRowHeight = 35f;
    private const float TableClassHeaderBandTop = 11f;
    private const float TableClassHeaderBandHeight = 24f;
    private const int MinimumColumnWidth = 24;
    private const int MetricLabelWidth = 118;
    private const float MetricGridTitleHeight = 11f;
    private const float MetricGridTitleBottomGap = 4f;
    private const float MetricGridHeaderHeight = 20f;
    private const float MetricGridHeaderBottomGap = 4f;
    private const float MetricGridRowHeight = 29f;
    private const float MetricGridRowGap = 4f;
    private const float MetricGridCellInset = 4f;
    private const float MetricGridCellHeight = 21f;
    private const float FlagOuterPadding = 8f;
    private const float FlagCellGap = 8f;
    private const float TrackSectorBoundaryTickLength = 17f;
    private const float TrackPitLineWidth = 2.2f;
    private const double TrackMapReloadIntervalSeconds = 10d;
    private const int GapMaxTrendPointsPerCar = 36_000;
    private const int GapMaxWeatherPoints = 36_000;
    private const int GapMaxDriverChangeMarkers = 64;
    private const double GapTrendWindowSeconds = 4d * 60d * 60d;
    private const double GapMinimumTrendDomainSeconds = 120d;
    private const double GapMinimumTrendDomainLaps = 1.5d;
    private const double GapTrendRightPaddingSeconds = 20d;
    private const double GapTrendRightPaddingLaps = 0.15d;
    private const double GapMissingSegmentSeconds = 10d;
    private const double GapMissingTelemetryGraceSeconds = 5d;
    private const double GapEntryTailSeconds = 300d;
    private const double GapEntryFadeSeconds = 45d;
    private const double GapDefaultLapReferenceSeconds = 60d;
    private const double GapFilteredRangeMinimumSeconds = 15d;
    private const double GapFilteredRangeMaximumSeconds = 90d;
    private const double GapFilteredRangeLaps = 0.5d;
    private const double GapFocusScaleMinimumReferenceGapSeconds = 90d;
    private const double GapFocusScaleMinimumReferenceGapLaps = 0.5d;
    private const double GapFocusScaleMinimumRangeSeconds = 20d;
    private const double GapFocusScaleMinimumRangeLaps = 0.1d;
    private const double GapFocusScalePaddingMultiplier = 1.18d;
    private const double GapFocusScaleTriggerRatio = 3d;
    private const double GapSameLapReferenceBoundaryLaps = 0.95d;
    private const double GapMetricDeadbandMinimumSeconds = 0.25d;
    private const double GapMetricDeadbandLapFraction = 0.0025d;
    private const double GapThreatMinimumGainSeconds = 0.5d;
    private const double GapThreatGainLapFraction = 0.005d;
    private const double GapFuelStintResetMinimumLiters = 5d;
    private const int GapOnTrackSurface = 3;
    private const float GapEndpointLabelLaneWidth = 38f;
    private const float GapEndpointLabelPinThreshold = 4f;
    private const float GapEndpointLabelHeight = 13f;
    private const float GapEndpointLabelGap = 1f;
    private const float GapMetricsTableWidth = 184f;
    private const float GapMetricsTableGap = 10f;
    private const float GapMetricsMinimumPlotWidth = 300f;
    private const float GapMetricsMinimumTableHeight = 164f;
    private const float GapThreatBadgeHeight = 16f;
    private const float GapFocusScaleReferenceRatio = 0.56f;
    private const float GapFocusScaleTopPadding = 18f;
    private const float GapFocusScaleBottomPadding = 8f;
    private const double FocusedCarLengthMeters = 4.746d;
    private const double RadarRangeMeters = FocusedCarLengthMeters * 6d;

    private static readonly Color TransparentColor = Color.FromArgb(1, 2, 3);
    private static Color Surface => OverlayTheme.DesignV2.Surface;
    private static Color SurfaceInset => OverlayTheme.DesignV2.SurfaceInset;
    private static Color SurfaceRaised => OverlayTheme.DesignV2.SurfaceRaised;
    private static Color TitleBar => OverlayTheme.DesignV2.TitleBar;
    private static Color Border => OverlayTheme.DesignV2.Border;
    private static Color BorderMuted => OverlayTheme.DesignV2.BorderMuted;
    private static Color TextPrimary => OverlayTheme.DesignV2.TextPrimary;
    private static Color TextSecondary => OverlayTheme.DesignV2.TextSecondary;
    private static Color TextMuted => OverlayTheme.DesignV2.TextMuted;
    private static Color Cyan => OverlayTheme.DesignV2.Cyan;
    private static Color Magenta => OverlayTheme.DesignV2.Magenta;
    private static Color Amber => OverlayTheme.DesignV2.Amber;
    private static Color Green => OverlayTheme.DesignV2.Green;
    private static Color Orange => OverlayTheme.DesignV2.Orange;
    private static Color Error => OverlayTheme.DesignV2.Error;
    private static Color OneLapAheadText => Color.FromArgb(255, 155, 164);
    private static Color MultipleLapsAheadText => Error;
    private static Color OneLapBehindText => Color.FromArgb(150, 210, 255);
    private static Color MultipleLapsBehindText => Color.FromArgb(82, 158, 255);
    private static Color TrackInterior => OverlayTheme.DesignV2.TrackInterior;
    private static Color TrackHalo => OverlayTheme.DesignV2.TrackHalo;
    private static Color TrackLine => OverlayTheme.DesignV2.TrackLine;
    private static Color TrackMarkerBorder => OverlayTheme.DesignV2.TrackMarkerBorder;
    private static Color PitLineColor => OverlayTheme.DesignV2.PitLine;
    private static Color StartFinishBoundaryColor => OverlayTheme.DesignV2.StartFinishBoundary;
    private static Color StartFinishBoundaryShadowColor => OverlayTheme.DesignV2.StartFinishBoundaryShadow;
    private static Color PersonalBestSectorColor => OverlayTheme.DesignV2.PersonalBestSector;
    private static Color BestLapSectorColor => OverlayTheme.DesignV2.BestLapSector;
    private static Color FlagPoleColor => OverlayTheme.DesignV2.FlagPole;
    private static Color FlagPoleShadowColor => OverlayTheme.DesignV2.FlagPoleShadow;

    private readonly DesignV2LiveOverlayKind _kind;
    private readonly OverlayDefinition _definition;
    private readonly ILiveTelemetrySource _liveTelemetrySource;
    private readonly TrackMapStore _trackMapStore;
    private readonly SessionHistoryQueryService _historyQueryService;
    private readonly StreamChatOverlaySource _streamChatSource;
    private readonly AppPerformanceState _performanceState;
    private readonly ILogger _logger;
    private readonly OverlaySettings _settings;
    private readonly string _fontFamily;
    private string _unitSystem;
    private readonly System.Windows.Forms.Timer _refreshTimer;
    private readonly PitServiceOverlayViewModel.StatefulBuilder _pitServiceBuilder;
    private readonly SessionWeatherOverlayViewModel.StatefulBuilder _sessionWeatherBuilder;
    private readonly List<double> _gapPoints = [];
    private readonly Dictionary<int, List<DesignV2GapTrendPoint>> _gapSeries = [];
    private readonly List<DesignV2GapWeatherPoint> _gapWeather = [];
    private readonly List<DesignV2GapDriverChangeMarker> _gapDriverChangeMarkers = [];
    private readonly List<DesignV2GapLeaderChangeMarker> _gapLeaderChangeMarkers = [];
    private readonly Dictionary<int, DesignV2GapCarRenderState> _gapCarRenderStates = [];
    private readonly Dictionary<int, DesignV2GapDriverIdentity> _gapDriverIdentities = [];
    private readonly List<InputStateTracePoint> _inputTrace = [];
    private readonly TrackMapRenderModelBuilder _trackMapRenderBuilder = new();
    private readonly Dictionary<int, double> _smoothedTrackMarkerProgress = new();
    private DesignV2OverlayModel _model;
    private TrackMapDocument? _trackMap;
    private string? _trackMapIdentityKey;
    private bool? _trackMapIncludeUserMaps;
    private HistoricalComboIdentity? _cachedHistoryCombo;
    private SessionHistoryLookupResult? _cachedHistory;
    private DateTimeOffset _cachedHistoryAtUtc;
    private string? _cachedRadarCalibrationCarKey;
    private CarRadarCalibrationLookupResult? _cachedRadarCalibration;
    private DateTimeOffset _cachedRadarCalibrationAtUtc;
    private string? _lastLoggedError;
    private DateTimeOffset? _lastLoggedErrorAtUtc;
    private DateTimeOffset? _lastTrackMarkerSmoothingAtUtc;
    private DateTimeOffset _nextTrackMapReloadAtUtc = DateTimeOffset.MinValue;
    private long? _lastGapSequence;
    private CarRadarRenderModel _lastRadarRenderableModel = CarRadarRenderModel.Empty;
    private DateTimeOffset? _lastRadarFadeAtUtc;
    private double _radarSurfaceAlpha;
    private double? _latestGapAxisSeconds;
    private double? _gapTrendStartAxisSeconds;
    private double? _lastGapLapReferenceSeconds;
    private double? _currentGapFuelStintStartAxisSeconds;
    private double? _lastGapFuelLevelLiters;
    private DesignV2GapReferenceContext? _lastGapReferenceContext;
    private int? _lastGapDriversSoFar;
    private int? _lastGapClassLeaderCarIdx;
    private bool _settingsPreviewVisible;
    private bool _flagsManagedEnabled = true;
    private bool _flagsSettingsOverlayActive;

    public DesignV2LiveOverlayForm(
        DesignV2LiveOverlayKind kind,
        OverlayDefinition definition,
        ILiveTelemetrySource liveTelemetrySource,
        TrackMapStore trackMapStore,
        SessionHistoryQueryService historyQueryService,
        StreamChatOverlaySource streamChatSource,
        AppPerformanceState performanceState,
        ILogger logger,
        OverlaySettings settings,
        string fontFamily,
        string unitSystem,
        Action saveSettings)
        : base(settings, saveSettings, definition.DefaultWidth, definition.DefaultHeight)
    {
        _kind = kind;
        _definition = definition;
        _liveTelemetrySource = liveTelemetrySource;
        _trackMapStore = trackMapStore;
        _historyQueryService = historyQueryService;
        _streamChatSource = streamChatSource;
        _performanceState = performanceState;
        _logger = logger;
        _settings = settings;
        _fontFamily = fontFamily;
        _unitSystem = NormalizeUnitSystem(unitSystem);
        _pitServiceBuilder = PitServiceOverlayViewModel.CreateStatefulBuilder();
        _sessionWeatherBuilder = SessionWeatherOverlayViewModel.CreateStatefulBuilder();
        _model = InitialModelFor(kind);

        BackColor = UsesTransparentBackground(kind) ? TransparentColor : Color.Black;
        if (UsesTransparentBackground(kind))
        {
            TransparencyKey = TransparentColor;
        }
        Padding = Padding.Empty;
        SetStyle(
            ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw
            | ControlStyles.UserPaint,
            true);

        var refreshIntervalMilliseconds = RefreshIntervalFor(kind);
        _refreshTimer = new System.Windows.Forms.Timer
        {
            Interval = refreshIntervalMilliseconds
        };
        _refreshTimer.Tick += (_, _) =>
        {
            _performanceState.RecordOverlayTimerTick(
                _definition.Id,
                refreshIntervalMilliseconds,
                Visible,
                !Visible || Opacity <= 0.001d);
            RefreshOverlay();
        };
        _refreshTimer.Start();

    }

    public void SetSettingsPreviewVisible(bool previewVisible)
    {
        if (_settingsPreviewVisible == previewVisible)
        {
            return;
        }

        _settingsPreviewVisible = previewVisible;
        if (_kind == DesignV2LiveOverlayKind.CarRadar && previewVisible)
        {
            _radarSurfaceAlpha = 1d;
            _lastRadarFadeAtUtc = null;
        }

        Invalidate();
    }

    private static int RefreshIntervalFor(DesignV2LiveOverlayKind kind)
    {
        return kind switch
        {
            DesignV2LiveOverlayKind.InputState => InputStateRenderModelBuilder.RefreshIntervalMilliseconds,
            DesignV2LiveOverlayKind.CarRadar => CarRadarRenderModel.RefreshIntervalMilliseconds,
            DesignV2LiveOverlayKind.TrackMap => TrackMapRenderModel.RefreshIntervalMilliseconds,
            _ => DefaultRefreshIntervalMilliseconds
        };
    }

    public override bool IsIntrinsicallyInputTransparentOverlay => IsInputTransparentKind(_kind);

    public bool IsInputTransparentOverlay => IsIntrinsicallyInputTransparentOverlay;

    public string DiagnosticKind => KindName(_kind);

    public string DiagnosticStatus => _model.Status;

    public string DiagnosticEvidence => _model.Evidence.ToString();

    public string DiagnosticBodyKind => BodyName(_model.Body);

    public bool? DiagnosticRadarShouldRender => _model.Body is DesignV2RadarBody radar
        ? radar.RenderModel.ShouldRender
        : null;

    public double? DiagnosticRadarSurfaceAlpha => _model.Body is DesignV2RadarBody radar
        ? radar.SurfaceAlpha
        : null;

    public int? DiagnosticRadarCarCount => _model.Body is DesignV2RadarBody radar
        ? radar.RenderModel.Cars.Count
        : null;

    public DesignV2LayoutDiagnostics DiagnosticLayout => BuildLayoutDiagnostics(ClientRectangle, _model);

    public string DiagnosticUnitSystem => _unitSystem;

    public void SetUnitSystem(string unitSystem)
    {
        _unitSystem = NormalizeUnitSystem(unitSystem);
    }

    public void SetFlagsManagedState(bool managedEnabled, bool settingsOverlayActive)
    {
        _flagsManagedEnabled = managedEnabled;
        _flagsSettingsOverlayActive = settingsOverlayActive;
        Invalidate();
    }

    private static string NormalizeUnitSystem(string unitSystem)
    {
        return string.Equals(unitSystem, "Imperial", StringComparison.OrdinalIgnoreCase)
            ? "Imperial"
            : "Metric";
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

    protected override Size GetPersistedOverlaySize()
    {
        if (_kind == DesignV2LiveOverlayKind.Standings)
        {
            return new Size(
                _settings.Width > 0 ? _settings.Width : _definition.DefaultWidth,
                _settings.Height > 0 ? _settings.Height : _definition.DefaultHeight);
        }

        return base.GetPersistedOverlaySize();
    }

    protected override bool ShouldReceiveInputWhileTransparent(Point clientPoint)
    {
        return _kind == DesignV2LiveOverlayKind.StreamChat
            && (IsStreamChatCloseButtonHit(clientPoint) || IsStreamChatDragHit(clientPoint, ClientSize));
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (_kind == DesignV2LiveOverlayKind.StreamChat
            && e.Button == MouseButtons.Left
            && IsStreamChatCloseButtonHit(e.Location))
        {
            DisableOverlayAndClose();
            return;
        }

        base.OnMouseUp(e);
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
            e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            if (!DrawCustomOverlay(e.Graphics, ClientRectangle, _model))
            {
                DrawOverlay(e.Graphics, ClientRectangle, _model);
            }
            succeeded = true;
        }
        finally
        {
            _performanceState.RecordOperation($"overlay.{_definition.Id}.design_v2.paint", started, succeeded);
        }
    }

    private void RefreshOverlay()
    {
        var started = Stopwatch.GetTimestamp();
        var succeeded = false;
        try
        {
            var snapshot = _liveTelemetrySource.Snapshot();
            _model = BuildModel(snapshot, DateTimeOffset.UtcNow);
            ApplyModelVisibility(_model);
            Invalidate();
            succeeded = true;
        }
        catch (Exception exception)
        {
            ReportOverlayError(exception);
            _model = new DesignV2OverlayModel(
                TitleFor(_kind),
                "overlay error",
                Trim(exception.Message, 96),
                DesignV2Evidence.Error,
                new DesignV2MetricRowsBody([new DesignV2MetricRow("Error", Trim(exception.Message, 120), DesignV2Evidence.Error)]));
            if (_kind == DesignV2LiveOverlayKind.StreamChat)
            {
                _model = _model with
                {
                    HeaderText = "overlay error",
                    ShowFooter = false,
                    ShowHeader = true
                };
            }
            ApplyModelVisibility(_model);
            Invalidate();
        }
        finally
        {
            _performanceState.RecordOperation($"overlay.{_definition.Id}.design_v2.refresh", started, succeeded);
        }
    }

    private DesignV2OverlayModel BuildModel(LiveTelemetrySnapshot snapshot, DateTimeOffset now)
    {
        snapshot = snapshot with { Models = snapshot.CompleteModels() };
        var model = _kind switch
        {
            DesignV2LiveOverlayKind.Standings => BuildStandingsModel(snapshot, now),
            DesignV2LiveOverlayKind.Relative => BuildRelativeModel(snapshot, now),
            DesignV2LiveOverlayKind.FuelCalculator => BuildFuelModel(snapshot, now),
            DesignV2LiveOverlayKind.SessionWeather => FromSimple(_sessionWeatherBuilder.Build(snapshot, now, _unitSystem, _settings)),
            DesignV2LiveOverlayKind.PitService => FromSimple(_pitServiceBuilder.Build(snapshot, now, _unitSystem, _settings)),
            DesignV2LiveOverlayKind.InputState => BuildInputModel(snapshot, now),
            DesignV2LiveOverlayKind.Flags => BuildFlagsModel(snapshot, now),
            DesignV2LiveOverlayKind.CarRadar => BuildRadarModel(snapshot, now),
            DesignV2LiveOverlayKind.GapToLeader => BuildGapModel(snapshot, now),
            DesignV2LiveOverlayKind.TrackMap => BuildTrackMapModel(snapshot, now),
            DesignV2LiveOverlayKind.StreamChat => BuildStreamChatModel(),
            _ => WaitingModel(TitleFor(_kind), "waiting")
        };
        return ApplyChromeSettings(model, snapshot);
    }

    private DesignV2OverlayModel ApplyChromeSettings(DesignV2OverlayModel model, LiveTelemetrySnapshot snapshot)
    {
        if (_kind == DesignV2LiveOverlayKind.InputState)
        {
            return model with
            {
                HeaderText = string.Empty,
                ShowFooter = false
            };
        }

        if (_kind == DesignV2LiveOverlayKind.StreamChat)
        {
            return model with
            {
                HeaderText = model.Status,
                ShowFooter = false,
                ShowHeader = true
            };
        }

        var headerText = BuildHeaderText(_settings, snapshot, HeaderStatusFor(_kind, model.Status));
        var showFooter = ShowFooterForSettings(_kind, _settings, snapshot);
        return model with
        {
            HeaderText = headerText,
            ShowFooter = showFooter
        };
    }

    private static string HeaderStatusFor(DesignV2LiveOverlayKind kind, string status)
    {
        return kind == DesignV2LiveOverlayKind.PitService
            ? PitServiceOverlayViewModel.HeaderStatus(status)
            : status;
    }

    internal static string BuildHeaderText(OverlaySettings settings, LiveTelemetrySnapshot snapshot, string status)
    {
        var parts = new List<string>(2);
        if (OverlayChromeSettings.ShowHeaderStatus(settings, snapshot) && !string.IsNullOrWhiteSpace(status))
        {
            parts.Add(status);
        }

        if (OverlayChromeSettings.ShowHeaderTimeRemaining(settings, snapshot))
        {
            var timeRemaining = OverlayHeaderTimeFormatter.FormatTimeRemaining(snapshot);
            if (!string.IsNullOrWhiteSpace(timeRemaining))
            {
                parts.Add(timeRemaining);
            }
        }

        return string.Join(" | ", parts);
    }

    internal static bool ShowFooterForSettings(DesignV2LiveOverlayKind kind, OverlaySettings settings, LiveTelemetrySnapshot snapshot)
    {
        if (kind == DesignV2LiveOverlayKind.SessionWeather)
        {
            return false;
        }

        return OverlayChromeSettings.ShowFooterSource(settings, snapshot);
    }

    private void ApplyModelVisibility(DesignV2OverlayModel model)
    {
        if (_kind == DesignV2LiveOverlayKind.FuelCalculator)
        {
            SetLiveTelemetryAvailable(model.ShouldRender);
        }
    }

    private DesignV2OverlayModel BuildStandingsModel(LiveTelemetrySnapshot snapshot, DateTimeOffset now)
    {
        var sessionKind = OverlayAvailabilityEvaluator.CurrentSessionKind(snapshot);
        var showClassSeparators = OverlayContentColumnSettings.Standings.Blocks is { Count: > 0 } blocks
            && OverlayContentColumnSettings.BlockEnabled(_settings, blocks[0], sessionKind);
        var otherRows = OverlayContentColumnSettings.Standings.Blocks is { Count: > 0 } otherBlocks
            ? OverlayContentColumnSettings.BlockCount(_settings, otherBlocks[0])
            : 2;
        var visibleRows = StandingsVisibleRowsForHeight(ClientSize.Height);
        if (snapshot.Models.Scoring.HasData)
        {
            var requiredRows = StandingsOverlayViewModel.ExpandRowBudgetForClassGroups(
                snapshot.Models.Scoring.ClassGroups,
                visibleRows,
                otherRows,
                showClassSeparators);
            if (EnsureClientHeightForStandingsRows(requiredRows))
            {
                visibleRows = StandingsVisibleRowsForHeight(ClientSize.Height);
            }
        }

        var viewModel = StandingsOverlayViewModel.From(
            snapshot,
            now,
            maximumRows: Math.Clamp(visibleRows, 1, StandingsOverlayViewModel.MaximumRenderedRows),
            otherClassRowsPerClass: otherRows,
            showClassSeparators: showClassSeparators);
        var visibleColumns = OverlayContentColumnSettings.VisibleColumnsFor(_settings, OverlayContentColumnSettings.Standings, sessionKind);
        var columns = visibleColumns
            .Select(column => new DesignV2Column(column.Label, column.Width, AlignmentFor(column.Alignment)))
            .ToArray();
        var rows = viewModel.Rows.Select(row => new DesignV2TableRow(
            ValuesForStandingsRow(row, visibleColumns),
            row.IsReference,
            row.IsClassHeader,
            row.IsPartial ? DesignV2Evidence.Partial : DesignV2Evidence.Measured,
            row.CarClassColorHex,
            row.IsClassHeader ? row.Driver : string.Empty,
            row.IsClassHeader ? ClassHeaderDetail(row) : string.Empty,
            CellForegrounds: CellForegroundsForStandingsRow(row, visibleColumns))).ToArray();
        return new DesignV2OverlayModel(
            "Standings",
            viewModel.Status,
            viewModel.Source,
            rows.Length == 0 ? DesignV2Evidence.Unavailable : DesignV2Evidence.Measured,
            new DesignV2TableBody(columns, rows));
    }

    private bool EnsureClientHeightForStandingsRows(int rowCount)
    {
        var targetHeight = TargetClientHeightForStandingsRows(rowCount);
        if (ClientSize.Height >= targetHeight)
        {
            return false;
        }

        ClientSize = new Size(ClientSize.Width, targetHeight);
        return true;
    }

    private int TargetClientHeightForStandingsRows(int rowCount)
    {
        var persistedHeight = _settings.Height > 0
            ? _settings.Height
            : _definition.DefaultHeight;
        var visibleRows = Math.Clamp(
            Math.Max(1, rowCount),
            1,
            StandingsOverlayViewModel.MaximumRenderedRows);
        var persistedVisibleRows = StandingsVisibleRowsForHeight(persistedHeight);
        if (visibleRows <= persistedVisibleRows)
        {
            return persistedHeight;
        }

        return Math.Max(
            persistedHeight,
            HeaderHeight + FooterHeight + BodyGap + 1 + RowHeight + (visibleRows * (RowHeight + RowGap)));
    }

    private static int StandingsVisibleRowsForHeight(int clientHeight)
    {
        var bodyHeight = clientHeight - HeaderHeight - FooterHeight - BodyGap - 1;
        return Math.Max(1, (bodyHeight - RowHeight) / (RowHeight + RowGap));
    }

    private static IReadOnlyList<string> ValuesForStandingsRow(
        StandingsOverlayRowViewModel row,
        IReadOnlyList<OverlayContentColumnState> visibleColumns)
    {
        var valuesByKey = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [OverlayContentColumnSettings.DataClassPosition] = row.ClassPosition,
            [OverlayContentColumnSettings.DataCarNumber] = row.CarNumber,
            [OverlayContentColumnSettings.DataDriver] = row.Driver,
            [OverlayContentColumnSettings.DataGap] = row.Gap,
            [OverlayContentColumnSettings.DataInterval] = row.Interval,
            [OverlayContentColumnSettings.DataFastestLap] = row.FastestLap,
            [OverlayContentColumnSettings.DataLastLap] = row.LastLap,
            [OverlayContentColumnSettings.DataPit] = row.Pit
        };
        return visibleColumns
            .Select(column => valuesByKey.TryGetValue(column.DataKey, out var value) ? value : string.Empty)
            .ToArray();
    }

    private static IReadOnlyList<string?> CellForegroundsForStandingsRow(
        StandingsOverlayRowViewModel row,
        IReadOnlyList<OverlayContentColumnState> visibleColumns)
    {
        return visibleColumns
            .Select(column =>
            {
                if (IsClassFastestLapCell(row, column.DataKey))
                {
                    return ColorRgbHex(BestLapSectorColor);
                }

                return IsRecentCarBestLapCell(row, column.DataKey)
                    ? ColorRgbHex(Green)
                    : null;
            })
            .ToArray();
    }

    private static bool IsClassFastestLapCell(StandingsOverlayRowViewModel row, string? dataKey)
    {
        return string.Equals(dataKey, OverlayContentColumnSettings.DataFastestLap, StringComparison.Ordinal)
                && row.IsClassFastestLap
            || string.Equals(dataKey, OverlayContentColumnSettings.DataLastLap, StringComparison.Ordinal)
                && row.IsClassFastestLastLap;
    }

    private static bool IsRecentCarBestLapCell(StandingsOverlayRowViewModel row, string? dataKey)
    {
        return string.Equals(dataKey, OverlayContentColumnSettings.DataFastestLap, StringComparison.Ordinal)
                && row.IsRecentCarBestLap
            || string.Equals(dataKey, OverlayContentColumnSettings.DataLastLap, StringComparison.Ordinal)
                && row.IsRecentCarBestLastLap;
    }

    private static string ClassHeaderDetail(StandingsOverlayRowViewModel row)
    {
        var parts = new[] { row.Gap, row.Interval }
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .ToArray();
        return string.Join(" | ", parts);
    }

    private DesignV2OverlayModel BuildRelativeModel(LiveTelemetrySnapshot snapshot, DateTimeOffset now)
    {
        var sessionKind = OverlayAvailabilityEvaluator.CurrentSessionKind(snapshot);
        var carsEachSide = RelativeBrowserSettings.CarsEachSide(_settings);
        var viewModel = RelativeOverlayViewModel.From(
            snapshot,
            now,
            carsEachSide,
            carsEachSide);
        var visibleColumns = OverlayContentColumnSettings.VisibleColumnsFor(_settings, OverlayContentColumnSettings.Relative, sessionKind);
        var columns = visibleColumns
            .Select(column => new DesignV2Column(column.Label, column.Width, AlignmentFor(column.Alignment)))
            .ToArray();
        var rows = StableRelativeRows(viewModel, carsEachSide, carsEachSide)
            .Select(row => row is null
                ? BlankTableRow(columns.Length)
                : new DesignV2TableRow(
                    ValuesForRelativeRow(row, visibleColumns),
                    row.IsReference,
                    IsClassHeader: false,
                    row.IsPartial ? DesignV2Evidence.Partial : DesignV2Evidence.Measured,
                    row.ClassColorHex,
                    RelativeLapDelta: row.LapDeltaToReference))
            .ToArray();
        return new DesignV2OverlayModel(
            "Relative",
            viewModel.Status,
            viewModel.Source,
            rows.Length == 0 ? DesignV2Evidence.Unavailable : DesignV2Evidence.Live,
            new DesignV2TableBody(columns, rows));
    }

    internal static IReadOnlyList<RelativeOverlayRowViewModel?> StableRelativeRows(
        RelativeOverlayViewModel viewModel,
        int carsAhead,
        int carsBehind)
    {
        return viewModel.StableRows(carsAhead, carsBehind, maximumRows: 17);
    }

    private static DesignV2TableRow BlankTableRow(int columnCount)
    {
        return new DesignV2TableRow(
            Enumerable.Repeat(string.Empty, Math.Max(0, columnCount)).ToArray(),
            IsReference: false,
            IsClassHeader: false,
            DesignV2Evidence.Unavailable,
            ClassColorHex: null);
    }

    private static IReadOnlyList<string> ValuesForRelativeRow(
        RelativeOverlayRowViewModel row,
        IReadOnlyList<OverlayContentColumnState> visibleColumns)
    {
        var valuesByKey = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [OverlayContentColumnSettings.DataRelativePosition] = row.Position,
            [OverlayContentColumnSettings.DataDriver] = row.Driver,
            [OverlayContentColumnSettings.DataGap] = row.Gap,
            [OverlayContentColumnSettings.DataPit] = row.IsPit ? "PIT" : string.Empty
        };
        return visibleColumns
            .Select(column => valuesByKey.TryGetValue(column.DataKey, out var value) ? value : string.Empty)
            .ToArray();
    }

    private DesignV2OverlayModel BuildFuelModel(LiveTelemetrySnapshot snapshot, DateTimeOffset now)
    {
        var strategyModel = LiveFuelStrategyModel.From(snapshot, now, LookupHistory);
        if (!strategyModel.IsAvailable)
        {
            return new DesignV2OverlayModel(
                "Fuel Calculator",
                strategyModel.Status,
                "source: waiting",
                DesignV2Evidence.Unavailable,
                new DesignV2MetricRowsBody([]),
                ShouldRender: false);
        }

        var viewModel = FuelCalculatorViewModel.From(
            strategyModel,
            showAdvice: false,
            _unitSystem,
            maximumRows: FuelVisibleRowsForHeight(ClientSize.Height, ShowFooterForSettings(_kind, _settings, snapshot)));
        var metricSections = viewModel.MetricSections.Select(section => new DesignV2MetricSection(
            section.Title,
            section.Rows.Select(row => new DesignV2MetricRow(
                row.Label,
                row.Value,
                EvidenceFor(row.Tone))
            {
                Segments = row.Segments.Select(segment => new DesignV2MetricSegment(
                    segment.Label,
                    segment.Value,
                    EvidenceFor(segment.Tone),
                    segment.AccentHex,
                    segment.RotationDegrees)).ToArray(),
                RowColorHex = row.RowColorHex
            }).ToArray())).ToArray();
        var rows = metricSections.SelectMany(section => section.Rows).ToArray();
        return new DesignV2OverlayModel(
            "Fuel Calculator",
            viewModel.Status,
            viewModel.Source,
            DesignV2Evidence.Modeled,
            new DesignV2MetricRowsBody(rows, metricSections, []));
    }

    private static int FuelVisibleRowsForHeight(int height, bool showFooter)
    {
        var bodyHeight = height - HeaderHeight - (showFooter ? FooterHeight : 8) - BodyGap - 34;
        return Math.Max(1, bodyHeight / (RowHeight + RowGap));
    }

    private DesignV2OverlayModel BuildInputModel(LiveTelemetrySnapshot snapshot, DateTimeOffset now)
    {
        var inputModel = InputStateRenderModelBuilder.Build(snapshot, now, _unitSystem, _settings, _inputTrace);
        return new DesignV2OverlayModel(
            "Inputs",
            inputModel.Status,
            inputModel.Source,
            EvidenceFor(inputModel.Tone),
            new DesignV2InputsBody(
                inputModel.Throttle,
                inputModel.Brake,
                inputModel.Clutch,
                inputModel.SteeringWheelAngle,
                inputModel.SpeedMetersPerSecond,
                inputModel.Gear,
                inputModel.SpeedText,
                inputModel.GearText,
                inputModel.SteeringText,
                inputModel.BrakeAbsActive,
                inputModel.ShowThrottleTrace,
                inputModel.ShowBrakeTrace,
                inputModel.ShowClutchTrace,
                inputModel.IsAvailable,
                inputModel.ShowThrottle,
                inputModel.ShowBrake,
                inputModel.ShowClutch,
                inputModel.ShowSteering,
                inputModel.ShowGear,
                inputModel.ShowSpeed,
                inputModel.HasGraph,
                inputModel.HasRail,
                inputModel.HasContent,
                inputModel.Trace));
    }

    private DesignV2OverlayModel BuildRadarModel(LiveTelemetrySnapshot snapshot, DateTimeOffset now)
    {
        var calibration = CarRadarCalibrationProfile.FromHistory(LookupCarRadarCalibration(snapshot.Models.Session.Combo));
        var showMulticlassWarning = OverlayContentColumnSettings.ContentEnabledForSession(
            _settings,
            OverlayOptionKeys.RadarMulticlassWarning,
            defaultEnabled: true,
            OverlayAvailabilityEvaluator.CurrentSessionKind(snapshot));
        var viewModel = CarRadarOverlayViewModel.From(
            snapshot,
            now,
            _settingsPreviewVisible,
            showMulticlassWarning,
            calibration);
        var renderState = ApplyRadarSurfaceFade(CarRadarRenderModel.FromViewModel(viewModel, calibration), now);
        var body = RadarBodyFromViewModel(viewModel, renderState.RenderModel, renderState.SurfaceAlpha);
        return new DesignV2OverlayModel(
            viewModel.Title,
            viewModel.Status,
            viewModel.Source,
            viewModel.IsAvailable ? DesignV2Evidence.Live : DesignV2Evidence.Unavailable,
            body);
    }

    internal static DesignV2RadarBody RadarBodyFromViewModel(
        CarRadarOverlayViewModel viewModel,
        CarRadarCalibrationProfile? calibrationProfile = null)
    {
        var renderModel = CarRadarRenderModel.FromViewModel(viewModel, calibrationProfile);
        return RadarBodyFromViewModel(
            viewModel,
            renderModel,
            renderModel.ShouldRender ? 1d : 0d);
    }

    private static DesignV2RadarBody RadarBodyFromViewModel(
        CarRadarOverlayViewModel viewModel,
        CarRadarRenderModel renderModel,
        double surfaceAlpha)
    {
        return new DesignV2RadarBody(
            viewModel.IsAvailable,
            viewModel.HasCarLeft,
            viewModel.HasCarRight,
            viewModel.Cars,
            viewModel.StrongestMulticlassApproach,
            viewModel.ShowMulticlassWarning,
            viewModel.PreviewVisible,
            renderModel,
            surfaceAlpha);
    }

    internal static DesignV2RadarBody RadarBodyFromSpatial(
        LiveSpatialModel spatial,
        bool overlayAvailable,
        bool previewVisible,
        OverlaySettings settings,
        CarRadarCalibrationProfile? calibrationProfile = null)
    {
        var calibration = calibrationProfile ?? CarRadarCalibrationProfile.Default;
        var isAvailable = overlayAvailable && spatial.HasData;
        var cars = spatial.Cars
            .Where(car => CarRadarOverlayViewModel.IsInRadarRange(car, calibration))
            .ToArray();
        var showMulticlassWarning = settings.GetBooleanOption(OverlayOptionKeys.RadarMulticlassWarning, defaultValue: true);
        LiveMulticlassApproach? multiclass = showMulticlassWarning
            ? spatial.MulticlassApproaches
                .Where(CarRadarOverlayViewModel.IsInMulticlassWarningRange)
                .OrderBy(approach => approach.RelativeSeconds is { } seconds ? Math.Abs(seconds) : double.MaxValue)
                .ThenByDescending(approach => approach.Urgency)
                .FirstOrDefault()
            : null;
        var renderModel = CarRadarRenderModel.FromState(
            isAvailable || previewVisible,
            spatial.HasCarLeft,
            spatial.HasCarRight,
            cars,
            multiclass,
            showMulticlassWarning,
            previewVisible,
            previewVisible
                || spatial.HasCarLeft
                || spatial.HasCarRight
                || cars.Length > 0
                || multiclass is not null,
            spatial.ReferenceCarClassColorHex,
            calibration);
        return new DesignV2RadarBody(
            isAvailable || previewVisible,
            spatial.HasCarLeft,
            spatial.HasCarRight,
            cars,
            multiclass,
            showMulticlassWarning,
            previewVisible,
            renderModel,
            renderModel.ShouldRender ? 1d : 0d);
    }

    private RadarSurfaceRenderState ApplyRadarSurfaceFade(CarRadarRenderModel current, DateTimeOffset now)
    {
        var elapsedSeconds = _lastRadarFadeAtUtc is { } lastFadeAtUtc
            ? Math.Clamp((now - lastFadeAtUtc).TotalSeconds, 0d, 0.5d)
            : 0d;
        _lastRadarFadeAtUtc = now;

        if (current.ShouldRender)
        {
            _lastRadarRenderableModel = current;
        }

        var targetAlpha = current.ShouldRender ? 1d : 0d;
        var durationMilliseconds = current.ShouldRender
            ? current.FadeInMilliseconds
            : Math.Max(current.FadeOutMilliseconds, _lastRadarRenderableModel.FadeOutMilliseconds);
        var durationSeconds = Math.Max(0.001d, durationMilliseconds / 1000d);
        _radarSurfaceAlpha = MoveToward(_radarSurfaceAlpha, targetAlpha, elapsedSeconds / durationSeconds);

        if (!current.ShouldRender
            && _radarSurfaceAlpha <= Math.Max(current.MinimumVisibleAlpha, _lastRadarRenderableModel.MinimumVisibleAlpha))
        {
            _radarSurfaceAlpha = 0d;
            _lastRadarRenderableModel = CarRadarRenderModel.Empty;
        }

        var renderModel = current.ShouldRender
            ? current
            : _lastRadarRenderableModel;
        return new RadarSurfaceRenderState(renderModel, _radarSurfaceAlpha);
    }

    private DesignV2OverlayModel BuildGapModel(LiveTelemetrySnapshot snapshot, DateTimeOffset now)
    {
        var shouldRender = GapWindowEnabled(_settings);
        var viewModel = GapToLeaderOverlayViewModel.From(snapshot, now);
        var gap = viewModel.Gap;
        if (_lastGapSequence != snapshot.Sequence)
        {
            _lastGapSequence = snapshot.Sequence;
            RecordGapSnapshot(snapshot, gap, now);
            if (viewModel.FocusedTrendPointSeconds is { } seconds
                && IsFinite(seconds))
            {
                _gapPoints.Add(seconds);
                if (_gapPoints.Count > 120)
                {
                    _gapPoints.RemoveRange(0, _gapPoints.Count - 120);
                }
            }
        }

        var selectedSeries = SelectGapSeries();
        var graph = BuildGapGraphBody(selectedSeries, snapshot);
        if (!viewModel.IsAvailable)
        {
            return new DesignV2OverlayModel(
                viewModel.Title,
                viewModel.Status,
                viewModel.Source,
                DesignV2Evidence.Unavailable,
                graph,
                ShouldRender: shouldRender);
        }

        var footer = gap.HasData
            ? $"source: live gap telemetry | cars {selectedSeries.Count}/{gap.ClassCars.Count}"
            : viewModel.Source;
        return new DesignV2OverlayModel(
            viewModel.Title,
            viewModel.Status,
            footer,
            gap.HasData ? DesignV2Evidence.Live : DesignV2Evidence.Unavailable,
            graph,
            ShouldRender: shouldRender);
    }

    private static bool GapWindowEnabled(OverlaySettings settings)
    {
        return settings.GetIntegerOption(OverlayOptionKeys.GapCarsAhead, defaultValue: 5, minimum: 0, maximum: 12) > 0
            || settings.GetIntegerOption(OverlayOptionKeys.GapCarsBehind, defaultValue: 5, minimum: 0, maximum: 12) > 0;
    }

    private void RecordGapSnapshot(LiveTelemetrySnapshot snapshot, LiveLeaderGapSnapshot gap, DateTimeOffset now)
    {
        var modelSnapshot = snapshot with { Models = snapshot.CompleteModels() };
        var timestamp = snapshot.LastUpdatedAtUtc
            ?? now;
        var axisSeconds = SelectGapAxisSeconds(timestamp, modelSnapshot.Models.Session.SessionTimeSeconds);
        _latestGapAxisSeconds = axisSeconds;
        if (_gapTrendStartAxisSeconds is null || axisSeconds < _gapTrendStartAxisSeconds.Value)
        {
            _gapTrendStartAxisSeconds = axisSeconds;
        }

        var context = new DesignV2GapReferenceContext(
            modelSnapshot.Models.Reference.FocusCarIdx ?? modelSnapshot.Models.Timing.FocusCarIdx,
            modelSnapshot.Models.Reference.ReferenceCarClass ?? modelSnapshot.Models.Timing.FocusRow?.CarClass);
        if (_lastGapReferenceContext is not null && _lastGapReferenceContext != context)
        {
            _gapSeries.Clear();
            _gapWeather.Clear();
            _gapDriverChangeMarkers.Clear();
            _gapLeaderChangeMarkers.Clear();
            _gapCarRenderStates.Clear();
            _lastGapClassLeaderCarIdx = null;
            _currentGapFuelStintStartAxisSeconds = null;
            _lastGapFuelLevelLiters = null;
            _gapTrendStartAxisSeconds = axisSeconds;
        }

        _lastGapReferenceContext = context;
        var lapReferenceSeconds = TmrOverlay.App.Overlays.GapToLeader.GapToLeaderLiveModelAdapter.SelectLapReferenceSeconds(modelSnapshot);
        _lastGapLapReferenceSeconds = lapReferenceSeconds;
        RecordGapFuelStint(modelSnapshot, axisSeconds);
        RecordGapWeather(modelSnapshot, axisSeconds);
        RecordGapDriverChangeMarkers(modelSnapshot, gap, timestamp, axisSeconds, lapReferenceSeconds);
        RecordGapLeaderChange(gap, timestamp, axisSeconds);
        foreach (var car in gap.ClassCars)
        {
            if (DesignV2GapSeconds(car, lapReferenceSeconds) is not { } gapSeconds)
            {
                continue;
            }

            if (!_gapSeries.TryGetValue(car.CarIdx, out var points))
            {
                points = [];
                _gapSeries[car.CarIdx] = points;
            }

            var startsSegment = points.Count == 0
                || axisSeconds - points[^1].AxisSeconds > GapMissingSegmentSeconds
                || !ShouldConnectGapSeriesPoint(points[^1].GapSeconds, gapSeconds, lapReferenceSeconds);
            var point = new DesignV2GapTrendPoint(
                timestamp,
                axisSeconds,
                gapSeconds,
                car.CarIdx,
                car.IsReferenceCar,
                car.IsClassLeader,
                car.ClassPosition,
                startsSegment);
            if (points.Count > 0 && Math.Abs(points[^1].AxisSeconds - axisSeconds) < 0.001d)
            {
                points[^1] = point with { StartsSegment = points[^1].StartsSegment };
            }
            else
            {
                points.Add(point);
            }

            if (points.Count > GapMaxTrendPointsPerCar)
            {
                points.RemoveRange(0, points.Count - GapMaxTrendPointsPerCar);
            }
        }

        UpdateGapCarRenderStates(modelSnapshot, gap, axisSeconds, lapReferenceSeconds);
        PruneGapSeries(axisSeconds);
    }

    private static double SelectGapAxisSeconds(DateTimeOffset timestamp, double? sessionTimeSeconds)
    {
        return sessionTimeSeconds is { } sessionTime && IsFinite(sessionTime) && sessionTime >= 0d
            ? sessionTime
            : timestamp.ToUnixTimeMilliseconds() / 1000d;
    }

    private void RecordGapWeather(LiveTelemetrySnapshot snapshot, double axisSeconds)
    {
        var condition = SelectWeatherCondition(snapshot);
        if (_gapWeather.Count > 0 && Math.Abs(_gapWeather[^1].AxisSeconds - axisSeconds) < 0.001d)
        {
            _gapWeather[^1] = new DesignV2GapWeatherPoint(axisSeconds, condition);
        }
        else
        {
            _gapWeather.Add(new DesignV2GapWeatherPoint(axisSeconds, condition));
        }

        if (_gapWeather.Count > GapMaxWeatherPoints)
        {
            _gapWeather.RemoveRange(0, _gapWeather.Count - GapMaxWeatherPoints);
        }
    }

    private void RecordGapFuelStint(LiveTelemetrySnapshot snapshot, double axisSeconds)
    {
        var fuelLevelLiters = FirstValidFuelLevel(snapshot.Models.FuelPit.Fuel.FuelLevelLiters);
        if (fuelLevelLiters is null)
        {
            return;
        }

        if (_currentGapFuelStintStartAxisSeconds is null)
        {
            _currentGapFuelStintStartAxisSeconds = axisSeconds;
        }
        else if (_lastGapFuelLevelLiters is { } previous
            && fuelLevelLiters.Value - previous >= GapFuelStintResetMinimumLiters)
        {
            _currentGapFuelStintStartAxisSeconds = axisSeconds;
        }

        _lastGapFuelLevelLiters = fuelLevelLiters;
    }

    private void PruneGapSeries(double latestAxisSeconds)
    {
        var cutoff = latestAxisSeconds - GapTrendWindowSeconds;
        foreach (var carIdx in _gapSeries.Keys.ToArray())
        {
            var points = _gapSeries[carIdx];
            points.RemoveAll(point => point.AxisSeconds < cutoff);
            if (points.Count == 0)
            {
                _gapSeries.Remove(carIdx);
            }
        }

        _gapWeather.RemoveAll(point => point.AxisSeconds < cutoff);
        _gapDriverChangeMarkers.RemoveAll(marker => marker.AxisSeconds < cutoff);
        _gapLeaderChangeMarkers.RemoveAll(marker => marker.AxisSeconds < cutoff);
        foreach (var carIdx in _gapCarRenderStates.Keys.ToArray())
        {
            if (!_gapSeries.ContainsKey(carIdx)
                && _gapCarRenderStates[carIdx].LastDesiredAxisSeconds is { } lastDesired
                && latestAxisSeconds - lastDesired > GapStickyVisibilitySeconds())
            {
                _gapCarRenderStates.Remove(carIdx);
            }
        }
    }

    private void RecordGapDriverChangeMarkers(
        LiveTelemetrySnapshot snapshot,
        LiveLeaderGapSnapshot gap,
        DateTimeOffset timestamp,
        double axisSeconds,
        double? lapReferenceSeconds)
    {
        RecordExplicitGapTeamDriverChangeMarker(snapshot, gap, timestamp, axisSeconds, lapReferenceSeconds);
        RecordSessionInfoGapDriverChangeMarkers(snapshot, gap, timestamp, axisSeconds, lapReferenceSeconds);
    }

    private void RecordExplicitGapTeamDriverChangeMarker(
        LiveTelemetrySnapshot snapshot,
        LiveLeaderGapSnapshot gap,
        DateTimeOffset timestamp,
        double axisSeconds,
        double? lapReferenceSeconds)
    {
        if (snapshot.Models.RaceEvents.DriversSoFar is not { } driversSoFar || driversSoFar <= 0)
        {
            return;
        }

        if (_lastGapDriversSoFar is { } previousDrivers)
        {
            if (driversSoFar < previousDrivers)
            {
                _lastGapDriversSoFar = driversSoFar;
                return;
            }

            if (driversSoFar > previousDrivers
                && ReferenceUsesPlayerCar(snapshot)
                && gap.ClassCars.FirstOrDefault(car => car.IsReferenceCar) is { } reference
                && DesignV2GapSeconds(reference, lapReferenceSeconds) is { } gapSeconds)
            {
                AddGapDriverChangeMarker(new DesignV2GapDriverChangeMarker(
                    timestamp,
                    axisSeconds,
                    reference.CarIdx,
                    gapSeconds,
                    true,
                    $"D{driversSoFar}"));
            }
        }

        _lastGapDriversSoFar = driversSoFar;
    }

    private void RecordSessionInfoGapDriverChangeMarkers(
        LiveTelemetrySnapshot snapshot,
        LiveLeaderGapSnapshot gap,
        DateTimeOffset timestamp,
        double axisSeconds,
        double? lapReferenceSeconds)
    {
        if (snapshot.Context.Drivers.Count == 0)
        {
            return;
        }

        foreach (var driver in snapshot.Context.Drivers)
        {
            if (ToGapDriverIdentity(driver) is not { } identity)
            {
                continue;
            }

            if (_gapDriverIdentities.TryGetValue(identity.CarIdx, out var previous)
                && !previous.HasSameDriver(identity)
                && gap.ClassCars.FirstOrDefault(car => car.CarIdx == identity.CarIdx) is { } car
                && DesignV2GapSeconds(car, lapReferenceSeconds) is { } gapSeconds)
            {
                AddGapDriverChangeMarker(new DesignV2GapDriverChangeMarker(
                    timestamp,
                    axisSeconds,
                    identity.CarIdx,
                    gapSeconds,
                    car.IsReferenceCar,
                    identity.ShortLabel));
            }

            _gapDriverIdentities[identity.CarIdx] = identity;
        }
    }

    private void AddGapDriverChangeMarker(DesignV2GapDriverChangeMarker marker)
    {
        if (_gapDriverChangeMarkers.Any(existing =>
                existing.CarIdx == marker.CarIdx
                && Math.Abs(existing.AxisSeconds - marker.AxisSeconds) < 5d))
        {
            return;
        }

        _gapDriverChangeMarkers.Add(marker);
        if (_gapDriverChangeMarkers.Count > GapMaxDriverChangeMarkers)
        {
            _gapDriverChangeMarkers.RemoveRange(0, _gapDriverChangeMarkers.Count - GapMaxDriverChangeMarkers);
        }
    }

    private void RecordGapLeaderChange(LiveLeaderGapSnapshot gap, DateTimeOffset timestamp, double axisSeconds)
    {
        if (gap.ClassLeaderCarIdx is not { } leaderCarIdx)
        {
            return;
        }

        if (_lastGapClassLeaderCarIdx is { } previousLeader && previousLeader != leaderCarIdx)
        {
            _gapLeaderChangeMarkers.Add(new DesignV2GapLeaderChangeMarker(timestamp, axisSeconds, previousLeader, leaderCarIdx));
        }

        _lastGapClassLeaderCarIdx = leaderCarIdx;
    }

    private void UpdateGapCarRenderStates(
        LiveTelemetrySnapshot snapshot,
        LiveLeaderGapSnapshot gap,
        double axisSeconds,
        double? lapReferenceSeconds)
    {
        var desiredCarIds = SelectDesiredGapCarIds(gap.ClassCars, lapReferenceSeconds);
        foreach (var car in gap.ClassCars)
        {
            if (DesignV2GapSeconds(car, lapReferenceSeconds) is not { } gapSeconds)
            {
                continue;
            }

            if (!_gapCarRenderStates.TryGetValue(car.CarIdx, out var state))
            {
                state = new DesignV2GapCarRenderState(car.CarIdx);
                _gapCarRenderStates[car.CarIdx] = state;
            }

            var wasVisible = ShouldKeepGapSeriesVisible(state, axisSeconds);
            state.LastSeenAxisSeconds = axisSeconds;
            state.LastGapSeconds = gapSeconds;
            state.IsReference = car.IsReferenceCar;
            state.IsClassLeader = car.IsClassLeader;
            state.ClassPosition = car.ClassPosition;
            state.DeltaSecondsToReference = car.DeltaSecondsToReference;
            state.CurrentLap = car.CurrentLap;
            var timingRow = DesignV2GapTimingRow(snapshot.Models.Timing, car.CarIdx);
            state.LastLapTimeSeconds = timingRow?.LastLapTimeSeconds;
            state.BestLapTimeSeconds = timingRow?.BestLapTimeSeconds;
            state.TrackSurface = timingRow?.TrackSurface;
            state.OnPitRoad = timingRow?.OnPitRoad;
            var tire = GapTireCompound(snapshot.Models.TireCompounds, car.CarIdx);
            state.TireLabel = tire?.Label;
            state.TireShortLabel = tire?.ShortLabel;
            state.TireIsWet = tire?.IsWet;
            UpdateDesignV2GapPitState(state, car, axisSeconds);
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

        foreach (var state in _gapCarRenderStates.Values)
        {
            if (!desiredCarIds.Contains(state.CarIdx))
            {
                state.IsCurrentlyDesired = false;
            }
        }
    }

    private static LiveCarTireCompound? GapTireCompound(LiveTireCompoundModel tireCompounds, int carIdx)
    {
        return tireCompounds.Cars.FirstOrDefault(car => car.CarIdx == carIdx);
    }

    private static LiveTimingRow? DesignV2GapTimingRow(LiveTimingModel timing, int carIdx)
    {
        return timing.ClassRows.FirstOrDefault(row => row.CarIdx == carIdx)
            ?? timing.OverallRows.FirstOrDefault(row => row.CarIdx == carIdx)
            ?? (timing.FocusRow?.CarIdx == carIdx ? timing.FocusRow : null)
            ?? (timing.PlayerRow?.CarIdx == carIdx ? timing.PlayerRow : null);
    }

    private static void UpdateDesignV2GapPitState(
        DesignV2GapCarRenderState state,
        LiveClassGapCar car,
        double axisSeconds)
    {
        if (car.IsOnPitRoad)
        {
            if (!state.IsOnPitRoad)
            {
                state.CurrentPitEntryAxisSeconds = axisSeconds;
                state.CurrentPitEntryLap = car.CurrentLap;
            }

            state.IsOnPitRoad = true;
            if (state.CurrentPitEntryAxisSeconds is { } entry)
            {
                state.LastPitDurationSeconds = Math.Max(0d, axisSeconds - entry);
                state.LastPitLap = state.CurrentPitEntryLap ?? car.CurrentLap;
            }

            return;
        }

        if (state.IsOnPitRoad && state.CurrentPitEntryAxisSeconds is { } pitEntry)
        {
            state.LastPitDurationSeconds = Math.Max(0d, axisSeconds - pitEntry);
            state.LastPitLap = state.CurrentPitEntryLap ?? car.CurrentLap;
            state.LastPitExitAxisSeconds = axisSeconds;
        }

        state.IsOnPitRoad = false;
        state.CurrentPitEntryAxisSeconds = null;
        state.CurrentPitEntryLap = null;
    }

    private HashSet<int> SelectDesiredGapCarIds(IReadOnlyList<LiveClassGapCar> cars, double? lapReferenceSeconds)
    {
        var selected = new HashSet<int>();
        var reference = cars.FirstOrDefault(car =>
            car.IsReferenceCar
            && DesignV2GapSeconds(car, lapReferenceSeconds) is not null);
        var anchor = reference;
        var referenceCanAnchor = anchor is not null && !IsLappedGraphGap(anchor, lapReferenceSeconds);
        foreach (var car in cars.Where(car => car.IsClassLeader || (referenceCanAnchor && car.IsReferenceCar)))
        {
            selected.Add(car.CarIdx);
        }

        var aheadCount = _settings.GetIntegerOption(OverlayOptionKeys.GapCarsAhead, defaultValue: 5, minimum: 0, maximum: 12);
        var behindCount = _settings.GetIntegerOption(OverlayOptionKeys.GapCarsBehind, defaultValue: 5, minimum: 0, maximum: 12);
        if (!referenceCanAnchor || anchor is null)
        {
            foreach (var car in cars
                .Where(car => !car.IsClassLeader && !IsLappedGraphGap(car, lapReferenceSeconds))
                .OrderBy(car => car.ClassPosition ?? int.MaxValue)
                .ThenBy(car => DesignV2GapSeconds(car, lapReferenceSeconds) ?? double.MaxValue)
                .Take(Math.Max(1, behindCount)))
            {
                selected.Add(car.CarIdx);
            }

            if (selected.Count <= 1)
            {
                foreach (var car in cars
                    .Where(car => !car.IsClassLeader)
                    .OrderBy(car => DesignV2GapSeconds(car, lapReferenceSeconds) ?? double.MaxValue)
                    .ThenBy(car => car.ClassPosition ?? int.MaxValue)
                    .Take(Math.Max(1, behindCount)))
                {
                    selected.Add(car.CarIdx);
                }
            }

            return selected;
        }

        var rangeSeconds = GapFilteredRangeSeconds();
        foreach (var car in cars
            .Where(car => !car.IsReferenceCar
                && !car.IsClassLeader
                && IsSameLapGapCandidate(car, anchor, lapReferenceSeconds)
                && car.DeltaSecondsToReference is < 0d
                && Math.Abs(car.DeltaSecondsToReference.Value) <= rangeSeconds)
            .OrderByDescending(car => car.DeltaSecondsToReference!.Value)
            .Take(aheadCount))
        {
            selected.Add(car.CarIdx);
        }

        foreach (var car in cars
            .Where(car => !car.IsReferenceCar
                && !car.IsClassLeader
                && IsSameLapGapCandidate(car, anchor, lapReferenceSeconds)
                && car.DeltaSecondsToReference is > 0d
                && car.DeltaSecondsToReference.Value <= rangeSeconds)
            .OrderBy(car => car.DeltaSecondsToReference!.Value)
            .Take(behindCount))
        {
            selected.Add(car.CarIdx);
        }

        return selected;
    }

    private IReadOnlyList<DesignV2GapSeriesSelection> SelectGapSeries()
    {
        var now = _latestGapAxisSeconds ?? 0d;
        return _gapCarRenderStates.Values
            .Where(state => ShouldKeepGapSeriesVisible(state, now))
            .Select(state => ToGapSeriesSelection(state, now))
            .OrderBy(selection => selection.State.LastGapSeconds)
            .ToArray();
    }

    private static bool HasGapComparisonSeries(IReadOnlyList<DesignV2GapSeriesSelection> selectedSeries)
    {
        return selectedSeries.Any(selection => !selection.State.IsClassLeader);
    }

    private bool ShouldKeepGapSeriesVisible(DesignV2GapCarRenderState state, double axisSeconds)
    {
        return state.LastDesiredAxisSeconds is { } lastDesired
            && axisSeconds - lastDesired <= GapStickyVisibilitySeconds();
    }

    private double GapStickyVisibilitySeconds()
    {
        return GapFilteredRangeSeconds();
    }

    private double GapFilteredRangeSeconds()
    {
        var lapScaledRange = _lastGapLapReferenceSeconds is { } lapSeconds && IsValidLapReference(lapSeconds)
            ? lapSeconds * GapFilteredRangeLaps
            : 0d;
        return Math.Min(
            GapFilteredRangeMaximumSeconds,
            Math.Max(GapFilteredRangeMinimumSeconds, lapScaledRange));
    }

    private double GapTrendLookupToleranceSeconds()
    {
        return Math.Min(
            60d,
            Math.Max(
                8d,
                _lastGapLapReferenceSeconds is { } lapSeconds && IsValidLapReference(lapSeconds)
                    ? lapSeconds * 0.08d
                    : 60d * 0.08d));
    }

    private double GapThreatGainThresholdSeconds()
    {
        return Math.Max(
            GapThreatMinimumGainSeconds,
            _lastGapLapReferenceSeconds is { } lapSeconds && IsValidLapReference(lapSeconds)
                ? lapSeconds * GapThreatGainLapFraction
                : 0d);
    }

    private double GapMetricDeadbandSeconds()
    {
        return Math.Max(
            GapMetricDeadbandMinimumSeconds,
            _lastGapLapReferenceSeconds is { } lapSeconds && IsValidLapReference(lapSeconds)
                ? lapSeconds * GapMetricDeadbandLapFraction
                : 0d);
    }

    private DesignV2GapSeriesSelection ToGapSeriesSelection(DesignV2GapCarRenderState state, double now)
    {
        var lastDesired = state.LastDesiredAxisSeconds ?? now;
        var visibleSince = state.VisibleSinceAxisSeconds ?? lastDesired;
        var isStickyExit = !state.IsCurrentlyDesired;
        var isStale = now - state.LastSeenAxisSeconds > GapMissingTelemetryGraceSeconds;
        var stickySeconds = GapStickyVisibilitySeconds();
        var exitAlpha = isStickyExit
            ? 1d - Math.Clamp((now - lastDesired) / Math.Max(1d, stickySeconds), 0d, 1d)
            : 1d;
        var entryAlpha = Math.Clamp((now - visibleSince) / GapEntryFadeSeconds, 0d, 1d);
        var alpha = Math.Clamp(Math.Min(exitAlpha, 0.35d + entryAlpha * 0.65d), 0.18d, 1d);
        var drawStartSeconds = now - visibleSince <= GapEntryFadeSeconds
            ? Math.Max(0d, visibleSince - GapEntryTailSeconds)
            : double.NegativeInfinity;
        return new DesignV2GapSeriesSelection(state, alpha, isStickyExit, isStale, drawStartSeconds);
    }

    private bool IsSameLapGapCandidate(LiveClassGapCar candidate, LiveClassGapCar reference, double? lapReferenceSeconds)
    {
        return NormalizedDesignV2GapLaps(candidate, lapReferenceSeconds) is { } candidateGapLaps
            && NormalizedDesignV2GapLaps(reference, lapReferenceSeconds) is { } referenceGapLaps
            && Math.Abs(candidateGapLaps - referenceGapLaps) < 0.95d;
    }

    private static bool IsLappedGraphGap(LiveClassGapCar car, double? lapReferenceSeconds)
    {
        return DesignV2GapSeconds(car, lapReferenceSeconds) is { } gapSeconds
            && lapReferenceSeconds is { } lapSeconds
            && IsValidLapReference(lapSeconds)
            && gapSeconds >= lapSeconds * 0.95d;
    }

    private DesignV2GraphBody BuildGapGraphBody(IReadOnlyList<DesignV2GapSeriesSelection> selectedSeries, LiveTelemetrySnapshot snapshot)
    {
        var hasComparisonSeries = HasGapComparisonSeries(selectedSeries);
        if (!hasComparisonSeries)
        {
            selectedSeries = [];
        }

        var endSeconds = _latestGapAxisSeconds ?? 0d;
        var anchorSeconds = _gapTrendStartAxisSeconds ?? FirstVisibleGapAxisSeconds(selectedSeries) ?? endSeconds;
        var elapsedSeconds = Math.Max(0d, endSeconds - anchorSeconds);
        double startSeconds;
        if (elapsedSeconds >= GapTrendWindowSeconds)
        {
            startSeconds = endSeconds - GapTrendWindowSeconds;
        }
        else
        {
            var durationSeconds = Math.Min(
                GapTrendWindowSeconds,
                Math.Max(GapMinimumTrendDomainSecondsForCurrentLap(), elapsedSeconds + GapTrendRightPadding()));
            startSeconds = anchorSeconds;
            endSeconds = anchorSeconds + durationSeconds;
        }

        var comparisonLabel = DesignV2GapComparisonLabel();
        var trendMetrics = BuildDesignV2GapTrendMetrics();
        var activeThreat = ActiveDesignV2GapThreat(trendMetrics);
        var threatCarIdx = activeThreat?.Chaser?.CarIdx;
        var series = selectedSeries
            .Select(selection => new DesignV2GapSeries(
                selection.State.CarIdx,
                selection.State.IsReference,
                selection.State.IsClassLeader,
                selection.State.ClassPosition,
                selection.Alpha,
                selection.IsStickyExit,
                selection.IsStale,
                PointsForGapCar(selection.State.CarIdx, Math.Max(startSeconds, selection.DrawStartSeconds), endSeconds)))
            .Where(series => series.Points.Count > 0)
            .ToArray();
        var scale = SelectDesignV2GapScale(selectedSeries, startSeconds, endSeconds);
        return new DesignV2GraphBody(
            hasComparisonSeries ? _gapPoints.ToArray() : Array.Empty<double>(),
            series,
            _gapWeather.Where(point => point.AxisSeconds >= startSeconds && point.AxisSeconds <= endSeconds).ToArray(),
            _gapLeaderChangeMarkers.Where(marker => marker.AxisSeconds >= startSeconds && marker.AxisSeconds <= endSeconds).ToArray(),
            _gapDriverChangeMarkers.Where(marker => marker.AxisSeconds >= startSeconds && marker.AxisSeconds <= endSeconds).ToArray(),
            StartSeconds: startSeconds,
            EndSeconds: Math.Max(endSeconds, startSeconds + 1d),
            MaxGapSeconds: Math.Max(1d, scale.MaxGapSeconds),
            LapReferenceSeconds: TmrOverlay.App.Overlays.GapToLeader.GapToLeaderLiveModelAdapter.SelectLapReferenceSeconds(snapshot),
            SelectedSeriesCount: selectedSeries.Count,
            TrendMetrics: trendMetrics,
            ActiveThreat: activeThreat,
            ThreatCarIdx: threatCarIdx,
            MetricDeadbandSeconds: GapMetricDeadbandSeconds(),
            ComparisonLabel: comparisonLabel,
            Scale: scale);
    }

    private IReadOnlyList<DesignV2GapTrendMetric> BuildDesignV2GapTrendMetrics()
    {
        if (_lastGapLapReferenceSeconds is not { } lapReferenceSeconds
            || !IsValidLapReference(lapReferenceSeconds)
            || _gapCarRenderStates.Values.FirstOrDefault(state => state.IsReference) is not { } referenceState)
        {
            return DefaultDesignV2GapTrendMetrics("unavailable");
        }

        var latest = _latestGapAxisSeconds ?? 0d;
        var fiveLapMetric = BuildDesignV2GapTrendMetric("5L", lapReferenceSeconds * 5d, 5d, latest, referenceState);
        var tenLapMetric = BuildDesignV2GapTrendMetric("10L", lapReferenceSeconds * 10d, 10d, latest, referenceState);
        var paceMetrics = new[]
        {
            fiveLapMetric,
            tenLapMetric
        };
        var threatCarIdx = ActiveDesignV2GapThreat(paceMetrics)?.Chaser?.CarIdx;
        return paceMetrics
            .Concat(BuildDesignV2GapPitTrendMetrics(referenceState, threatCarIdx))
            .Append(BuildDesignV2GapStintTrendMetric(referenceState, threatCarIdx))
            .Append(BuildDesignV2GapTireTrendMetric(referenceState, threatCarIdx))
            .Concat(BuildDesignV2GapExtraTrendMetrics(referenceState, threatCarIdx))
            .ToArray();
    }

    private DesignV2GapTrendMetric BuildDesignV2GapStintTrendMetric(
        DesignV2GapCarRenderState referenceState,
        int? threatCarIdx)
    {
        var threatState = threatCarIdx is { } carIdx && _gapCarRenderStates.TryGetValue(carIdx, out var state)
            ? state
            : null;
        var comparisonState = LatestDesignV2GapTrendPoint(referenceState.CarIdx) is { } referenceCurrent
            ? DesignV2GapComparisonCar(referenceState, referenceCurrent)
            : null;

        return new DesignV2GapTrendMetric(
            "Stint",
            null,
            null,
            "stint",
            null,
            PrimaryText: DesignV2GapStintLapText(referenceState),
            ThreatText: DesignV2GapStintLapText(threatState),
            ComparisonText: DesignV2GapStintLapText(comparisonState));
    }

    private IReadOnlyList<DesignV2GapTrendMetric> BuildDesignV2GapPitTrendMetrics(
        DesignV2GapCarRenderState referenceState,
        int? threatCarIdx)
    {
        var threatState = threatCarIdx is { } carIdx && _gapCarRenderStates.TryGetValue(carIdx, out var state)
            ? state
            : null;
        var comparisonState = LatestDesignV2GapTrendPoint(referenceState.CarIdx) is { } referenceCurrent
            ? DesignV2GapComparisonCar(referenceState, referenceCurrent)
            : null;
        var primaryPit = DesignV2GapPitMetricValue(referenceState);
        var comparisonPit = DesignV2GapPitMetricValue(comparisonState);
        var threatPit = DesignV2GapPitMetricValue(threatState);
        return new[]
        {
            new DesignV2GapTrendMetric("Pit", null, null, "pit", null, primaryPit, threatPit, comparisonPit),
            new DesignV2GapTrendMetric("PLap", null, null, "pitLap", null, primaryPit, threatPit, comparisonPit)
        };
    }

    private DesignV2GapTrendMetric BuildDesignV2GapTireTrendMetric(
        DesignV2GapCarRenderState referenceState,
        int? threatCarIdx)
    {
        var threatState = threatCarIdx is { } carIdx && _gapCarRenderStates.TryGetValue(carIdx, out var state)
            ? state
            : null;
        var comparisonState = LatestDesignV2GapTrendPoint(referenceState.CarIdx) is { } referenceCurrent
            ? DesignV2GapComparisonCar(referenceState, referenceCurrent)
            : null;
        var primaryTire = DesignV2GapTireMetricValue(referenceState);
        var threatTire = DesignV2GapTireMetricValue(threatState);
        var comparisonTire = DesignV2GapTireMetricValue(comparisonState);
        return new DesignV2GapTrendMetric(
            "Tire",
            null,
            null,
            "tire",
            null,
            PrimaryTire: primaryTire,
            ThreatTire: threatTire,
            ComparisonTire: comparisonTire);
    }

    private IReadOnlyList<DesignV2GapTrendMetric> BuildDesignV2GapExtraTrendMetrics(
        DesignV2GapCarRenderState referenceState,
        int? threatCarIdx)
    {
        var threatState = threatCarIdx is { } carIdx && _gapCarRenderStates.TryGetValue(carIdx, out var state)
            ? state
            : null;
        var comparisonState = LatestDesignV2GapTrendPoint(referenceState.CarIdx) is { } referenceCurrent
            ? DesignV2GapComparisonCar(referenceState, referenceCurrent)
            : null;

        return new[]
        {
            new DesignV2GapTrendMetric(
                "Last",
                null,
                null,
                "last",
                null,
                PrimaryText: DesignV2GapLapTimeText(referenceState.LastLapTimeSeconds),
                ThreatText: DesignV2GapLapTimeText(threatState?.LastLapTimeSeconds),
                ComparisonText: DesignV2GapLapTimeText(comparisonState?.LastLapTimeSeconds)),
            new DesignV2GapTrendMetric(
                "Status",
                null,
                null,
                "status",
                null,
                PrimaryText: DesignV2GapStatusText(referenceState),
                ThreatText: DesignV2GapStatusText(threatState),
                ComparisonText: DesignV2GapStatusText(comparisonState))
        };
    }

    private DesignV2GapTrendMetric BuildDesignV2GapTrendMetric(
        string label,
        double lookbackSeconds,
        double? targetLaps,
        double latest,
        DesignV2GapCarRenderState referenceState)
    {
        if (!IsFinite(lookbackSeconds)
            || lookbackSeconds <= 0d
            || LatestDesignV2GapTrendPoint(referenceState.CarIdx) is not { } referenceCurrent)
        {
            return new DesignV2GapTrendMetric(label, null, null, "unavailable", null);
        }

        var targetAxisSeconds = latest - lookbackSeconds;
        var chaser = StrongestDesignV2GapBehindGain(referenceState, referenceCurrent, targetAxisSeconds, latest);
        if (DesignV2GapTrendPointNear(referenceState.CarIdx, targetAxisSeconds) is not { } referencePast)
        {
            return new DesignV2GapTrendMetric(
                label,
                null,
                chaser,
                chaser is null ? "warming" : "ready",
                DesignV2GapWarmupLabel(referenceState.CarIdx, latest, targetLaps));
        }

        var comparisonState = DesignV2GapComparisonCar(referenceState, referenceCurrent);
        if (comparisonState is null || LatestDesignV2GapTrendPoint(comparisonState.CarIdx) is not { } comparisonCurrent)
        {
            return new DesignV2GapTrendMetric(label, null, chaser, "ready", "leader");
        }

        if (DesignV2GapTrendPointNear(comparisonState.CarIdx, targetAxisSeconds) is not { } comparisonPast)
        {
            return new DesignV2GapTrendMetric(
                label,
                null,
                chaser,
                chaser is null ? "warming" : "ready",
                DesignV2GapWarmupLabel(comparisonState.CarIdx, latest, targetLaps));
        }

        var currentDelta = referenceCurrent.GapSeconds - comparisonCurrent.GapSeconds;
        var pastDelta = referencePast.GapSeconds - comparisonPast.GapSeconds;
        return new DesignV2GapTrendMetric(label, currentDelta - pastDelta, chaser, "ready", null);
    }

    private string? DesignV2GapWarmupLabel(int referenceCarIdx, double latest, double? targetLaps)
    {
        if (targetLaps is not { } laps
            || laps <= 0d
            || _lastGapLapReferenceSeconds is not { } lapReferenceSeconds
            || !IsValidLapReference(lapReferenceSeconds)
            || FirstDesignV2GapTrendPoint(referenceCarIdx) is not { } first)
        {
            return null;
        }

        var availableLaps = Math.Max(0d, (latest - first.AxisSeconds) / lapReferenceSeconds);
        return $"{Math.Min(availableLaps, laps).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}L";
    }

    private DesignV2BehindGainMetric? StrongestDesignV2GapBehindGain(
        DesignV2GapCarRenderState referenceState,
        DesignV2GapTrendPoint referenceCurrent,
        double targetAxisSeconds,
        double latest)
    {
        if (HasDesignV2GapPitActivityBetween(referenceState, targetAxisSeconds, latest)
            || DesignV2GapTrendPointNear(referenceState.CarIdx, targetAxisSeconds) is not { } referencePast)
        {
            return null;
        }

        DesignV2BehindGainMetric? best = null;
        foreach (var state in _gapCarRenderStates.Values)
        {
            if (state.CarIdx == referenceState.CarIdx
                || state.IsReference
                || HasDesignV2GapPitActivityBetween(state, targetAxisSeconds, latest)
                || LatestDesignV2GapTrendPoint(state.CarIdx) is not { } current
                || current.GapSeconds <= referenceCurrent.GapSeconds
                || DesignV2GapTrendPointNear(state.CarIdx, targetAxisSeconds) is not { } past)
            {
                continue;
            }

            var currentDelta = current.GapSeconds - referenceCurrent.GapSeconds;
            var pastDelta = past.GapSeconds - referencePast.GapSeconds;
            var gainSeconds = pastDelta - currentDelta;
            if (gainSeconds < GapThreatGainThresholdSeconds())
            {
                continue;
            }

            if (best is null || gainSeconds > best.GainSeconds)
            {
                best = new DesignV2BehindGainMetric(state.CarIdx, DesignV2GapCarShortLabel(state), gainSeconds);
            }
        }

        return best;
    }

    private DesignV2GapCarRenderState? DesignV2GapCarAhead(
        DesignV2GapCarRenderState referenceState,
        DesignV2GapTrendPoint referenceCurrent)
    {
        return _gapCarRenderStates.Values
            .Where(state => state.CarIdx != referenceState.CarIdx && !state.IsReference)
            .Select(state => new
            {
                State = state,
                Point = LatestDesignV2GapTrendPoint(state.CarIdx)
            })
            .Where(item => item.Point is not null && item.Point.GapSeconds < referenceCurrent.GapSeconds - 0.001d)
            .OrderBy(item => referenceCurrent.GapSeconds - item.Point!.GapSeconds)
            .ThenBy(item => item.State.ClassPosition ?? int.MaxValue)
            .FirstOrDefault()
            ?.State;
    }

    private DesignV2GapCarRenderState? DesignV2GapComparisonCar(
        DesignV2GapCarRenderState referenceState,
        DesignV2GapTrendPoint referenceCurrent)
    {
        return DesignV2GapCarAhead(referenceState, referenceCurrent)
            ?? DesignV2GapCarBehind(referenceState, referenceCurrent);
    }

    private DesignV2GapCarRenderState? DesignV2GapCarBehind(
        DesignV2GapCarRenderState referenceState,
        DesignV2GapTrendPoint referenceCurrent)
    {
        return _gapCarRenderStates.Values
            .Where(state => state.CarIdx != referenceState.CarIdx && !state.IsReference)
            .Select(state => new
            {
                State = state,
                Point = LatestDesignV2GapTrendPoint(state.CarIdx)
            })
            .Where(item => item.Point is not null && item.Point.GapSeconds > referenceCurrent.GapSeconds + 0.001d)
            .OrderBy(item => item.Point!.GapSeconds - referenceCurrent.GapSeconds)
            .ThenBy(item => item.State.ClassPosition ?? int.MaxValue)
            .FirstOrDefault()
            ?.State;
    }

    private string DesignV2GapComparisonLabel()
    {
        var referenceState = _gapCarRenderStates.Values.FirstOrDefault(state => state.IsReference);
        if (referenceState is null || LatestDesignV2GapTrendPoint(referenceState.CarIdx) is not { } referenceCurrent)
        {
            return "--";
        }

        var comparisonState = DesignV2GapComparisonCar(referenceState, referenceCurrent);
        return comparisonState?.ClassPosition is > 0
            ? $"P{comparisonState.ClassPosition.Value}"
            : "--";
    }

    private DesignV2PitMetricValue? DesignV2GapPitMetricValue(DesignV2GapCarRenderState? state)
    {
        if (state is null)
        {
            return null;
        }

        if (state.IsOnPitRoad && state.CurrentPitEntryAxisSeconds is { } entry)
        {
            var latest = _latestGapAxisSeconds ?? state.LastSeenAxisSeconds;
            return new DesignV2PitMetricValue(
                Math.Max(0d, latest - entry),
                state.CurrentPitEntryLap ?? state.LastPitLap,
                true);
        }

        return state.LastPitDurationSeconds is { } duration
            ? new DesignV2PitMetricValue(duration, state.LastPitLap, false)
            : null;
    }

    private static DesignV2TireMetricValue? DesignV2GapTireMetricValue(DesignV2GapCarRenderState? state)
    {
        return string.IsNullOrWhiteSpace(state?.TireShortLabel)
            ? null
            : new DesignV2TireMetricValue(state.TireLabel, state.TireShortLabel, state.TireIsWet == true);
    }

    private static string DesignV2GapStintLapText(DesignV2GapCarRenderState? state)
    {
        if (state is null)
        {
            return "--";
        }

        if (state.CurrentLap is { } currentLap
            && (state.LastPitLap ?? state.CurrentPitEntryLap) is { } pitLap
            && currentLap >= pitLap)
        {
            return DesignV2GapStintLapsText(currentLap - pitLap);
        }

        return "--";
    }

    private static string DesignV2GapStintLapsText(double laps)
    {
        if (!IsFinite(laps) || laps < 0d)
        {
            return "--";
        }

        var rounded = Math.Round(laps);
        return rounded >= 1d && Math.Abs(laps - rounded) <= 0.05d
            ? $"{rounded.ToString("0", System.Globalization.CultureInfo.InvariantCulture)}L"
            : $"{laps.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}L";
    }

    private static string DesignV2GapLapTimeText(double? seconds)
    {
        if (seconds is not { } value || !IsFinite(value) || value <= 0d)
        {
            return "--";
        }

        var minutes = (int)(value / 60d);
        var remainder = value - minutes * 60d;
        return minutes > 0
            ? $"{minutes.ToString(System.Globalization.CultureInfo.InvariantCulture)}:{remainder.ToString("00.000", System.Globalization.CultureInfo.InvariantCulture)}"
            : remainder.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string DesignV2GapStatusText(DesignV2GapCarRenderState? state)
    {
        if (state is null)
        {
            return "--";
        }

        if (state.IsOnPitRoad || state.OnPitRoad == true)
        {
            return "Pit";
        }

        return state.TrackSurface switch
        {
            null => "Track",
            GapOnTrackSurface => "Track",
            0 => "Off",
            1 => "Off",
            2 => "Out",
            _ => "Off"
        };
    }

    private static bool HasDesignV2GapPitActivityBetween(
        DesignV2GapCarRenderState state,
        double startSeconds,
        double endSeconds)
    {
        if (!IsFinite(startSeconds) || !IsFinite(endSeconds) || endSeconds < startSeconds)
        {
            return false;
        }

        if (state.IsOnPitRoad
            && state.CurrentPitEntryAxisSeconds is { } entry
            && entry <= endSeconds)
        {
            return true;
        }

        return state.LastPitExitAxisSeconds is { } exit
            && exit > startSeconds
            && exit <= endSeconds;
    }

    private DesignV2GapTrendMetric? ActiveDesignV2GapThreat(IReadOnlyList<DesignV2GapTrendMetric> metrics)
    {
        return metrics
            .Where(metric => string.Equals(metric.State, "ready", StringComparison.Ordinal)
                && metric.Chaser is not null
                && metric.Chaser.GainSeconds >= GapThreatGainThresholdSeconds())
            .OrderByDescending(metric => metric.Chaser!.GainSeconds)
            .FirstOrDefault();
    }

    private DesignV2GapTrendPoint? LatestDesignV2GapTrendPoint(int carIdx)
    {
        return _gapSeries.TryGetValue(carIdx, out var points) && points.Count > 0
            ? points[^1]
            : null;
    }

    private DesignV2GapTrendPoint? FirstDesignV2GapTrendPoint(int carIdx)
    {
        return _gapSeries.TryGetValue(carIdx, out var points) && points.Count > 0
            ? points[0]
            : null;
    }

    private DesignV2GapTrendPoint? DesignV2GapTrendPointNear(int carIdx, double axisSeconds)
    {
        if (!_gapSeries.TryGetValue(carIdx, out var points) || points.Count == 0)
        {
            return null;
        }

        var best = points.MinBy(point => Math.Abs(point.AxisSeconds - axisSeconds));
        return best is not null && Math.Abs(best.AxisSeconds - axisSeconds) <= GapTrendLookupToleranceSeconds()
            ? best
            : null;
    }

    private IReadOnlyList<DesignV2GapTrendMetric> DefaultDesignV2GapTrendMetrics(string state)
    {
        return new[]
        {
            new DesignV2GapTrendMetric("5L", null, null, state, null),
            new DesignV2GapTrendMetric("10L", null, null, state, null),
            new DesignV2GapTrendMetric("Pit", null, null, state, null),
            new DesignV2GapTrendMetric("PLap", null, null, state, null),
            new DesignV2GapTrendMetric("Stint", null, null, state, null),
            new DesignV2GapTrendMetric("Tire", null, null, state, null),
            new DesignV2GapTrendMetric("Last", null, null, state, null),
            new DesignV2GapTrendMetric("Status", null, null, state, null)
        };
    }

    private IReadOnlyList<DesignV2GapTrendPoint> PointsForGapCar(int carIdx, double startSeconds, double endSeconds)
    {
        return _gapSeries.TryGetValue(carIdx, out var points)
            ? points.Where(point => point.AxisSeconds >= startSeconds && point.AxisSeconds <= endSeconds).ToArray()
            : Array.Empty<DesignV2GapTrendPoint>();
    }

    private double GapMinimumTrendDomainSecondsForCurrentLap()
    {
        return Math.Max(
            GapMinimumTrendDomainSeconds,
            _lastGapLapReferenceSeconds is { } lapSeconds && IsValidLapReference(lapSeconds)
                ? lapSeconds * GapMinimumTrendDomainLaps
                : 0d);
    }

    private double GapTrendRightPadding()
    {
        return Math.Max(
            GapTrendRightPaddingSeconds,
            _lastGapLapReferenceSeconds is { } lapSeconds && IsValidLapReference(lapSeconds)
                ? lapSeconds * GapTrendRightPaddingLaps
                : 0d);
    }

    private double? FirstVisibleGapAxisSeconds(IReadOnlyList<DesignV2GapSeriesSelection> selectedSeries)
    {
        double? firstVisiblePoint = null;
        foreach (var selection in selectedSeries)
        {
            if (!_gapSeries.TryGetValue(selection.State.CarIdx, out var points))
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

    private DesignV2GapScale SelectDesignV2GapScale(
        IReadOnlyList<DesignV2GapSeriesSelection> selectedSeries,
        double startSeconds,
        double endSeconds)
    {
        var leaderScaleMax = SelectDesignV2MaxGapSeconds(selectedSeries, startSeconds, endSeconds);
        var referenceSelection = selectedSeries.FirstOrDefault(selection => selection.State.IsReference);
        if (referenceSelection is null
            || !_gapSeries.TryGetValue(referenceSelection.State.CarIdx, out var rawReferencePoints))
        {
            return DesignV2GapScale.Leader(leaderScaleMax);
        }

        var referencePoints = rawReferencePoints
            .Where(point => point.AxisSeconds >= startSeconds && point.AxisSeconds <= endSeconds)
            .OrderBy(point => point.AxisSeconds)
            .ToArray();
        if (referencePoints.Length == 0)
        {
            return DesignV2GapScale.Leader(leaderScaleMax);
        }

        var latestReferenceGap = GapReferenceAt(referencePoints, endSeconds);
        var triggerGap = GapFocusScaleMinimumReferenceGap();
        if (latestReferenceGap < triggerGap)
        {
            return DesignV2GapScale.Leader(leaderScaleMax);
        }

        var maxAheadSeconds = 0d;
        var maxBehindSeconds = 0d;
        var hasLocalComparison = false;
        foreach (var selection in selectedSeries.Where(selection => !selection.State.IsClassLeader))
        {
            if (!_gapSeries.TryGetValue(selection.State.CarIdx, out var points))
            {
                continue;
            }

            foreach (var point in points.Where(point =>
                point.AxisSeconds >= selection.DrawStartSeconds
                && point.AxisSeconds >= startSeconds
                && point.AxisSeconds <= endSeconds))
            {
                var delta = point.GapSeconds - GapReferenceAt(referencePoints, point.AxisSeconds);
                hasLocalComparison |= !selection.State.IsReference && Math.Abs(delta) > 0.001d;
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

        var minimumRange = GapFocusScaleMinimumRange();
        var aheadRange = NiceCeiling(Math.Max(minimumRange, maxAheadSeconds * GapFocusScalePaddingMultiplier));
        var behindRange = NiceCeiling(Math.Max(minimumRange, maxBehindSeconds * GapFocusScalePaddingMultiplier));
        var localRange = Math.Max(aheadRange, behindRange);
        var forceFocusScaleForLappedReference = ShouldForceDesignV2FocusScaleForLappedReference(latestReferenceGap);
        if (!forceFocusScaleForLappedReference
            && (!hasLocalComparison || leaderScaleMax < Math.Max(triggerGap, localRange * GapFocusScaleTriggerRatio)))
        {
            return DesignV2GapScale.Leader(leaderScaleMax);
        }

        return DesignV2GapScale.FocusRelative(
            leaderScaleMax,
            aheadRange,
            behindRange,
            referencePoints,
            latestReferenceGap);
    }

    private double SelectDesignV2MaxGapSeconds(
        IReadOnlyList<DesignV2GapSeriesSelection> selectedSeries,
        double startSeconds,
        double endSeconds)
    {
        var maxGap = selectedSeries
            .Where(selection => _gapSeries.ContainsKey(selection.State.CarIdx))
            .SelectMany(selection => _gapSeries[selection.State.CarIdx].Where(point => point.AxisSeconds >= selection.DrawStartSeconds))
            .Where(point => point.AxisSeconds >= startSeconds && point.AxisSeconds <= endSeconds)
            .Select(point => point.GapSeconds)
            .DefaultIfEmpty(1d)
            .Max();
        return NiceCeiling(Math.Max(1d, maxGap));
    }

    private double GapFocusScaleMinimumReferenceGap()
    {
        return Math.Max(
            GapFocusScaleMinimumReferenceGapSeconds,
            _lastGapLapReferenceSeconds is { } lapSeconds && IsValidLapReference(lapSeconds)
                ? lapSeconds * GapFocusScaleMinimumReferenceGapLaps
                : 0d);
    }

    private double GapFocusScaleMinimumRange()
    {
        return Math.Max(
            GapFocusScaleMinimumRangeSeconds,
            _lastGapLapReferenceSeconds is { } lapSeconds && IsValidLapReference(lapSeconds)
                ? lapSeconds * GapFocusScaleMinimumRangeLaps
                : 0d);
    }

    private bool ShouldForceDesignV2FocusScaleForLappedReference(double latestReferenceGap)
    {
        return _lastGapLapReferenceSeconds is { } lapSeconds
            && IsValidLapReference(lapSeconds)
            && latestReferenceGap >= lapSeconds * GapSameLapReferenceBoundaryLaps;
    }

    private static double GapReferenceAt(IReadOnlyList<DesignV2GapTrendPoint> referencePoints, double axisSeconds)
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

    private static double? DesignV2GapSeconds(LiveClassGapCar car, double? lapReferenceSeconds)
    {
        if (car.IsClassLeader)
        {
            return 0d;
        }

        if (car.GapSecondsToClassLeader is { } seconds && IsFinite(seconds) && seconds >= 0d)
        {
            return seconds;
        }

        return car.GapLapsToClassLeader is { } laps && IsFinite(laps) && laps >= 0d
            ? laps * (lapReferenceSeconds is { } lapSeconds && IsFinite(lapSeconds) && lapSeconds > 20d ? lapSeconds : GapDefaultLapReferenceSeconds)
            : null;
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

    private static double? NormalizedDesignV2GapLaps(LiveClassGapCar car, double? lapReferenceSeconds)
    {
        if (car.GapLapsToClassLeader is { } laps && IsFinite(laps))
        {
            return laps;
        }

        if (DesignV2GapSeconds(car, lapReferenceSeconds) is { } seconds
            && lapReferenceSeconds is { } lapSeconds
            && IsValidLapReference(lapSeconds))
        {
            return seconds / lapSeconds;
        }

        return null;
    }

    private static bool IsValidLapReference(double? seconds)
    {
        return seconds is { } value && value is > 20d and < 1800d && IsFinite(value);
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

    private static DesignV2GapDriverIdentity? ToGapDriverIdentity(HistoricalSessionDriver driver)
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

        return new DesignV2GapDriverIdentity(carIdx, key, SelectGapDriverLabel(driver));
    }

    private static string SelectGapDriverLabel(HistoricalSessionDriver driver)
    {
        foreach (var value in new[] { driver.Initials, driver.AbbrevName, driver.UserName })
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                var trimmed = value.Trim();
                return trimmed.Length <= 3
                    ? trimmed
                    : trimmed[..Math.Min(3, trimmed.Length)].ToUpperInvariant();
            }
        }

        return "DR";
    }

    private static DesignV2GapWeatherCondition SelectWeatherCondition(LiveTelemetrySnapshot snapshot)
    {
        var weather = snapshot.Models.Weather;
        if (!weather.HasData)
        {
            return DesignV2GapWeatherCondition.Unknown;
        }

        if (weather.WeatherDeclaredWet == true)
        {
            return DesignV2GapWeatherCondition.DeclaredWet;
        }

        return weather.TrackWetness switch
        {
            >= 4 => DesignV2GapWeatherCondition.Wet,
            >= 2 => DesignV2GapWeatherCondition.Damp,
            >= 0 => DesignV2GapWeatherCondition.Dry,
            _ => DesignV2GapWeatherCondition.Unknown
        };
    }

    private DesignV2OverlayModel BuildTrackMapModel(LiveTelemetrySnapshot snapshot, DateTimeOffset now)
    {
        RefreshTrackMap(snapshot, now);
        var viewModel = TrackMapOverlayViewModel.From(snapshot, now, _settings, _trackMap);
        return new DesignV2OverlayModel(
            viewModel.Title,
            viewModel.Status,
            viewModel.Source,
            viewModel.IsAvailable ? DesignV2Evidence.Live : DesignV2Evidence.Unavailable,
            new DesignV2TrackMapBody(_trackMapRenderBuilder.Build(viewModel, now)));
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
        if (!identityChanged && !mapSourceChanged && now < _nextTrackMapReloadAtUtc)
        {
            return;
        }

        if (identityChanged || mapSourceChanged)
        {
            _trackMapRenderBuilder.ResetSmoothing();
        }

        _trackMapIdentityKey = identity.Key;
        _trackMapIncludeUserMaps = includeUserMaps;
        _nextTrackMapReloadAtUtc = now.AddSeconds(TrackMapReloadIntervalSeconds);
        _trackMap = _trackMapStore.TryReadBest(
            snapshot.Context.Track,
            includeUserMaps: includeUserMaps);
    }

    private DesignV2OverlayModel BuildStreamChatModel()
    {
        var viewModel = _streamChatSource.Snapshot(_settings);
        var contentOptions = StreamChatContentOptions.From(_settings);
        var rows = viewModel.Rows
            .Select(message => new DesignV2ChatRow(
                message.Name,
                message.Text,
                ChatEvidence(message.Kind),
                StreamChatMessageDisplay.AuthorColorHex(message, contentOptions),
                StreamChatMessageDisplay.MetadataParts(message, contentOptions),
                StreamChatMessageDisplay.BadgeParts(message, contentOptions)
                    .Select(badge => badge.Label)
                    .ToArray(),
                StreamChatMessageDisplay.MessageSegments(message, contentOptions)))
            .ToArray();

        return new DesignV2OverlayModel(
            viewModel.Title,
            viewModel.Status,
            viewModel.Source,
            viewModel.HasLiveRows
                ? DesignV2Evidence.Live
                : viewModel.HasErrorRows
                    ? DesignV2Evidence.Error
                    : DesignV2Evidence.Unavailable,
            new DesignV2ChatBody(rows));
    }

    private static string KindName(DesignV2LiveOverlayKind kind)
    {
        return kind switch
        {
            DesignV2LiveOverlayKind.Standings => "standings",
            DesignV2LiveOverlayKind.FuelCalculator => "fuel-calculator",
            DesignV2LiveOverlayKind.Relative => "relative",
            DesignV2LiveOverlayKind.TrackMap => "track-map",
            DesignV2LiveOverlayKind.StreamChat => "stream-chat",
            DesignV2LiveOverlayKind.Flags => "flags",
            DesignV2LiveOverlayKind.SessionWeather => "session-weather",
            DesignV2LiveOverlayKind.PitService => "pit-service",
            DesignV2LiveOverlayKind.InputState => "input-state",
            DesignV2LiveOverlayKind.CarRadar => "car-radar",
            DesignV2LiveOverlayKind.GapToLeader => "gap-to-leader",
            _ => "unknown"
        };
    }

    private static string BodyName(DesignV2Body body)
    {
        return body switch
        {
            DesignV2TableBody => "table",
            DesignV2MetricRowsBody => "metric-rows",
            DesignV2GraphBody => "graph",
            DesignV2InputsBody => "inputs",
            DesignV2RadarBody => "radar",
            DesignV2ChatBody => "chat",
            DesignV2FlagsBody => "flags",
            DesignV2TrackMapBody => "track-map",
            _ => "unknown"
        };
    }

    private static DesignV2Evidence ChatEvidence(StreamChatMessageKind kind)
    {
        return kind switch
        {
            StreamChatMessageKind.Message => DesignV2Evidence.Live,
            StreamChatMessageKind.Notice => DesignV2Evidence.Live,
            StreamChatMessageKind.Error => DesignV2Evidence.Error,
            _ => DesignV2Evidence.Partial
        };
    }

    private DesignV2OverlayModel BuildFlagsModel(LiveTelemetrySnapshot snapshot, DateTimeOffset now)
    {
        var viewModel = FlagsOverlayViewModel.ForDisplay(snapshot, now);
        var flags = viewModel.Flags
            .Where(flag => IsFlagCategoryEnabled(flag.Category))
            .ToArray();
        return new DesignV2OverlayModel(
            "Flags",
            viewModel.Status,
            "source: session flags telemetry",
            EvidenceFor(viewModel.Tone),
            new DesignV2FlagsBody(
                flags,
                viewModel.IsWaiting,
                _flagsManagedEnabled,
                _flagsSettingsOverlayActive));
    }

    private DesignV2OverlayModel FromSimple(SimpleTelemetryOverlayViewModel viewModel)
    {
        return new DesignV2OverlayModel(
            viewModel.Title,
            viewModel.Status,
            viewModel.Source,
            EvidenceFor(viewModel.Tone),
            new DesignV2MetricRowsBody(viewModel.Rows.Select(row => new DesignV2MetricRow(
                row.Label,
                row.Value,
                EvidenceFor(row.Tone))
            {
                Segments = row.Segments.Select(segment => new DesignV2MetricSegment(
                    segment.Label,
                    segment.Value,
                    EvidenceFor(segment.Tone),
                    segment.AccentHex,
                    segment.RotationDegrees)).ToArray(),
                RowColorHex = row.RowColorHex
            }).ToArray(),
                viewModel.MetricSections.Select(section => new DesignV2MetricSection(
                    section.Title,
                    section.Rows.Select(row => new DesignV2MetricRow(
                        row.Label,
                        row.Value,
                        EvidenceFor(row.Tone))
                    {
                        Segments = row.Segments.Select(segment => new DesignV2MetricSegment(
                            segment.Label,
                            segment.Value,
                            EvidenceFor(segment.Tone),
                            segment.AccentHex,
                            segment.RotationDegrees)).ToArray(),
                        RowColorHex = row.RowColorHex
                    }).ToArray())).ToArray(),
                viewModel.Sections.Select(section => new DesignV2MetricGridSection(
                    section.Title,
                    section.Headers,
                    section.Rows.Select(row => new DesignV2MetricGridRow(
                        row.Label,
                        row.Cells.Select(cell => new DesignV2MetricGridCell(
                            cell.Value,
                            EvidenceFor(cell.Tone))).ToArray(),
                        EvidenceFor(row.Tone))).ToArray())).ToArray()));
    }

    private SessionHistoryLookupResult LookupHistory(HistoricalComboIdentity combo)
    {
        var now = DateTimeOffset.UtcNow;
        if (_cachedHistory is not null
            && _cachedHistoryCombo is not null
            && string.Equals(_cachedHistoryCombo.CarKey, combo.CarKey, StringComparison.Ordinal)
            && string.Equals(_cachedHistoryCombo.TrackKey, combo.TrackKey, StringComparison.Ordinal)
            && string.Equals(_cachedHistoryCombo.SessionKey, combo.SessionKey, StringComparison.Ordinal)
            && now - _cachedHistoryAtUtc <= TimeSpan.FromSeconds(30))
        {
            return _cachedHistory;
        }

        _cachedHistory = _historyQueryService.Lookup(combo);
        _cachedHistoryCombo = combo;
        _cachedHistoryAtUtc = now;
        return _cachedHistory;
    }

    private CarRadarCalibrationLookupResult LookupCarRadarCalibration(HistoricalComboIdentity combo)
    {
        var now = DateTimeOffset.UtcNow;
        if (_cachedRadarCalibration is not null
            && string.Equals(_cachedRadarCalibrationCarKey, combo.CarKey, StringComparison.Ordinal)
            && now - _cachedRadarCalibrationAtUtc <= TimeSpan.FromSeconds(30))
        {
            return _cachedRadarCalibration;
        }

        _cachedRadarCalibration = _historyQueryService.LookupCarRadarCalibration(combo);
        _cachedRadarCalibrationCarKey = combo.CarKey;
        _cachedRadarCalibrationAtUtc = now;
        return _cachedRadarCalibration;
    }

    private bool IsFlagCategoryEnabled(FlagDisplayCategory category)
    {
        return category switch
        {
            FlagDisplayCategory.Green => _settings.GetBooleanOption(OverlayOptionKeys.FlagsShowGreen, defaultValue: true),
            FlagDisplayCategory.Blue => _settings.GetBooleanOption(OverlayOptionKeys.FlagsShowBlue, defaultValue: true),
            FlagDisplayCategory.Yellow => _settings.GetBooleanOption(OverlayOptionKeys.FlagsShowYellow, defaultValue: true),
            FlagDisplayCategory.Critical => _settings.GetBooleanOption(OverlayOptionKeys.FlagsShowCritical, defaultValue: true),
            FlagDisplayCategory.Finish => _settings.GetBooleanOption(OverlayOptionKeys.FlagsShowFinish, defaultValue: true),
            _ => true
        };
    }

    private IReadOnlyList<DesignV2TrackMapMarker> SmoothTrackMapMarkers(IReadOnlyList<DesignV2TrackMapMarker> markers)
    {
        if (markers.Count == 0)
        {
            _smoothedTrackMarkerProgress.Clear();
            _lastTrackMarkerSmoothingAtUtc = null;
            return markers;
        }

        var now = DateTimeOffset.UtcNow;
        var elapsed = _lastTrackMarkerSmoothingAtUtc is { } last
            ? Math.Clamp((now - last).TotalSeconds, 0d, 0.25d)
            : 0.05d;
        _lastTrackMarkerSmoothingAtUtc = now;
        var alpha = 1d - Math.Exp(-elapsed / 0.14d);
        var activeCarIds = markers.Select(marker => marker.CarIdx).ToHashSet();
        foreach (var carIdx in _smoothedTrackMarkerProgress.Keys.Where(carIdx => !activeCarIds.Contains(carIdx)).ToArray())
        {
            _smoothedTrackMarkerProgress.Remove(carIdx);
        }

        return markers.Select(marker =>
        {
            if (!_smoothedTrackMarkerProgress.TryGetValue(marker.CarIdx, out var current))
            {
                _smoothedTrackMarkerProgress[marker.CarIdx] = marker.LapDistPct;
                return marker;
            }

            var smoothed = NormalizeProgress(current + ProgressDelta(current, marker.LapDistPct) * Math.Clamp(alpha, 0d, 1d));
            _smoothedTrackMarkerProgress[marker.CarIdx] = smoothed;
            return marker with { LapDistPct = smoothed };
        }).ToArray();
    }

    internal static IReadOnlyList<DesignV2TrackMapMarker> BuildTrackMapMarkers(LiveTelemetrySnapshot snapshot)
    {
        return TrackMapOverlayViewModel.BuildMarkers(snapshot)
            .Select(ToDesignV2TrackMapMarker)
            .ToArray();
    }

    private static DesignV2TrackMapMarker ToDesignV2TrackMapMarker(TrackMapOverlayMarker marker)
    {
        return new DesignV2TrackMapMarker(
            marker.CarIdx,
            marker.LapDistPct,
            marker.IsFocus,
            MarkerColor(marker.ClassColorHex, marker.IsFocus),
            marker.Position is > 0
                ? marker.Position.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : null);
    }

    private static Color MarkerColor(string? classColorHex, bool isFocus)
    {
        if (isFocus)
        {
            return Cyan;
        }

        return OverlayClassColor.TryParseWithAlpha(classColorHex, 245) ?? Color.FromArgb(245, 237, 245, 250);
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

    private DesignV2LayoutDiagnostics BuildLayoutDiagnostics(Rectangle bounds, DesignV2OverlayModel model)
    {
        var client = LayoutRect(bounds);
        var constants = new DesignV2LayoutConstants(
            PaddingSize,
            HeaderHeight,
            FooterHeight,
            BodyGap,
            RowHeight,
            RowGap,
            ColumnGap,
            MinimumColumnWidth,
            MetricLabelWidth);

        var custom = BuildCustomLayoutDiagnostics(bounds, model, client, constants);
        if (custom is not null)
        {
            return custom;
        }

        var outer = RectangleF.Inflate(bounds, -0.5f, -0.5f);
        var headerBottom = outer.Top;
        RectangleF? header = null;
        if (model.ShowHeader)
        {
            header = new RectangleF(outer.Left + 1, outer.Top + 1, outer.Width - 2, HeaderHeight);
            headerBottom = header.Value.Bottom;
        }

        var footerReserve = model.ShowFooter ? FooterHeight : 8;
        var bodyTop = model.ShowHeader
            ? headerBottom + BodyGap
            : outer.Top + PaddingSize;
        var body = new RectangleF(
            outer.Left + PaddingSize,
            bodyTop,
            outer.Width - PaddingSize * 2,
            Math.Max(1, outer.Bottom - bodyTop - footerReserve));
        var footer = model.ShowFooter
            ? FooterBounds(outer)
            : (RectangleF?)null;

        return new DesignV2LayoutDiagnostics(
            "design-v2-layout/v1",
            KindName(_kind),
            client,
            constants)
        {
            Outer = LayoutRect(outer),
            Header = LayoutRect(header),
            Body = LayoutRect(body),
            Footer = LayoutRect(footer),
            ShowHeader = model.ShowHeader,
            ShowFooter = model.ShowFooter,
            BodyLayout = BuildBodyLayout(body, model.Body)
        };
    }

    private DesignV2LayoutDiagnostics? BuildCustomLayoutDiagnostics(
        Rectangle bounds,
        DesignV2OverlayModel model,
        DesignV2LayoutRect client,
        DesignV2LayoutConstants constants)
    {
        var rect = RectangleF.Inflate(bounds, -0.5f, -0.5f);
        return model.Body switch
        {
            DesignV2RadarBody radar => new DesignV2LayoutDiagnostics(
                "design-v2-layout/v1",
                KindName(_kind),
                client,
                constants)
            {
                Body = LayoutRect(rect),
                BodyLayout = BuildRadarLayout(rect, radar)
            },
            DesignV2InputsBody inputs => BuildInputsLayoutDiagnostics(rect, client, constants, inputs),
            DesignV2TrackMapBody trackMap => new DesignV2LayoutDiagnostics(
                "design-v2-layout/v1",
                KindName(_kind),
                client,
                constants)
            {
                Body = LayoutRect(rect),
                BodyLayout = BuildTrackMapLayout(rect, trackMap)
            },
            DesignV2FlagsBody flags => new DesignV2LayoutDiagnostics(
                "design-v2-layout/v1",
                KindName(_kind),
                client,
                constants)
            {
                Body = LayoutRect(rect),
                BodyLayout = BuildFlagsLayout(rect, flags)
            },
            _ => null
        };
    }

    private DesignV2LayoutDiagnostics BuildInputsLayoutDiagnostics(
        RectangleF rect,
        DesignV2LayoutRect client,
        DesignV2LayoutConstants constants,
        DesignV2InputsBody inputs)
    {
        var header = new RectangleF(rect.Left, rect.Top, rect.Width, HeaderHeight);
        var content = InputsContentBounds(rect, header);
        return new DesignV2LayoutDiagnostics(
            "design-v2-layout/v1",
            KindName(_kind),
            client,
            constants)
        {
            Outer = LayoutRect(rect),
            Header = LayoutRect(header),
            Body = LayoutRect(content),
            ShowHeader = true,
            ShowFooter = false,
            BodyLayout = BuildInputsLayout(content, inputs)
        };
    }

    private DesignV2LayoutBody BuildBodyLayout(RectangleF rect, DesignV2Body body)
    {
        return body switch
        {
            DesignV2TableBody table => BuildTableLayout(rect, table),
            DesignV2MetricRowsBody metrics => BuildMetricRowsLayout(rect, metrics),
            DesignV2GraphBody graph => BuildGraphLayout(rect, graph),
            DesignV2ChatBody chat => BuildChatLayout(rect, chat),
            DesignV2InputsBody inputs => BuildInputsLayout(rect, inputs),
            DesignV2RadarBody radar => BuildRadarLayout(rect, radar),
            DesignV2FlagsBody flags => BuildFlagsLayout(rect, flags),
            DesignV2TrackMapBody trackMap => BuildTrackMapLayout(rect, trackMap),
            _ => new DesignV2LayoutBody(BodyName(body), LayoutRect(rect))
        };
    }

    private DesignV2LayoutBody BuildTableLayout(RectangleF rect, DesignV2TableBody table)
    {
        var layout = new DesignV2LayoutBody("table", LayoutRect(rect));
        if (table.Columns.Count == 0)
        {
            return layout;
        }

        var configuredWidth = table.Columns.Sum(column => Math.Max(MinimumColumnWidth, column.Width));
        var availableWidth = Math.Max(1f, rect.Width);
        var fit = Math.Min(1f, availableWidth / Math.Max(1, configuredWidth));
        var renderedTableWidth = configuredWidth * fit;
        var x = rect.Left;
        var columns = new List<DesignV2LayoutColumn>();
        for (var columnIndex = 0; columnIndex < table.Columns.Count; columnIndex++)
        {
            var column = table.Columns[columnIndex];
            var width = Math.Max(MinimumColumnWidth, column.Width) * fit;
            columns.Add(new DesignV2LayoutColumn(
                columnIndex,
                column.Label,
                column.Width,
                width,
                AlignmentName(column.Alignment),
                LayoutRect(new RectangleF(x, rect.Top + TableHeaderTop, width, TableHeaderHeight))));
            x += width;
        }

        var rows = new List<DesignV2LayoutRow>();
        var maximumRows = table.Rows.Count;
        var y = rect.Top + TableBodyTop;
        var drawnRows = 0;
        for (var sourceIndex = 0; sourceIndex < table.Rows.Count; sourceIndex++)
        {
            var row = table.Rows[sourceIndex];
            if (drawnRows >= maximumRows)
            {
                break;
            }

            if (row.IsClassHeader)
            {
                var classHeaderRowRect = new RectangleF(rect.Left, y, renderedTableWidth, TableClassHeaderRowHeight);
                if (classHeaderRowRect.Bottom > rect.Bottom)
                {
                    break;
                }

                var headerRect = new RectangleF(
                    classHeaderRowRect.Left,
                    classHeaderRowRect.Top + TableClassHeaderBandTop,
                    classHeaderRowRect.Width,
                    TableClassHeaderBandHeight);
                if (headerRect.Bottom > rect.Bottom)
                {
                    break;
                }

                rows.Add(new DesignV2LayoutRow(
                    drawnRows,
                    sourceIndex,
                    "class-header",
                    LayoutRect(classHeaderRowRect))
                {
                    Text = string.IsNullOrWhiteSpace(row.ClassHeaderTitle) ? "Class" : row.ClassHeaderTitle,
                    Detail = row.ClassHeaderDetail,
                    ClassColorHex = row.ClassColorHex,
                    Background = ColorHex(TryParseHexColor(row.ClassColorHex, out var headerColor)
                        ? Blend(SurfaceRaised, headerColor, 4, 2)
                        : Blend(SurfaceRaised, Cyan, 5, 1)),
                    Foreground = ColorHex(TextPrimary)
                });
                y += classHeaderRowRect.Height + RowGap;
                drawnRows++;
                continue;
            }

            var rowHeight = TableRowHeightFor(row);
            var rowRect = new RectangleF(rect.Left, y, renderedTableWidth, rowHeight);
            if (rowRect.Bottom > rect.Bottom)
            {
                break;
            }

            x = rowRect.Left;
            var cells = new List<DesignV2LayoutCell>();
            var rowTextColor = TableTextColor(row);
            for (var columnIndex = 0; columnIndex < columns.Count; columnIndex++)
            {
                var column = columns[columnIndex];
                var padding = TableCellHorizontalPadding(column.RenderedWidth);
                var cellRect = new RectangleF(
                    x + padding,
                    rowRect.Top,
                    Math.Max(1f, column.RenderedWidth - padding * 2f),
                    rowRect.Height);
                cells.Add(new DesignV2LayoutCell(
                    columnIndex,
                    column.Label,
                    columnIndex < row.Values.Count ? row.Values[columnIndex] : string.Empty,
                    LayoutRect(cellRect),
                    column.Alignment)
                {
                    Foreground = ColorHex(TableCellTextColor(row, columnIndex, rowTextColor))
                });
                x += column.RenderedWidth;
            }

            rows.Add(new DesignV2LayoutRow(
                drawnRows,
                sourceIndex,
                row.IsReference ? "reference" : "row",
                LayoutRect(rowRect))
            {
                Cells = cells,
                Evidence = row.Evidence.ToString(),
                RelativeLapDelta = row.RelativeLapDelta,
                ClassColorHex = row.ClassColorHex,
                Background = ColorHex(TableRowFillColor(row)),
                Foreground = ColorHex(row.IsReference ? TextPrimary : rowTextColor)
            });
            y += rowHeight + RowGap;
            drawnRows++;
        }

        return layout with
        {
            ConfiguredWidth = configuredWidth,
            AvailableWidth = availableWidth,
            FitScale = fit,
            Columns = columns,
            Rows = rows,
            MaximumRows = maximumRows,
            DrawnRows = rows.Count
        };
    }

    private static Color TableRowFillColor(DesignV2TableRow row)
    {
        return row.Evidence == DesignV2Evidence.Unavailable
            ? SurfaceInset
            : TryParseHexColor(row.ClassColorHex, out var rowClassColor)
                ? Blend(SurfaceRaised, rowClassColor, row.IsReference ? 10 : 12, 1)
                : row.IsReference
                    ? Blend(SurfaceRaised, Cyan, 10, 1)
                    : SurfaceRaised;
    }

    private static float MetricRowHeightFor(DesignV2MetricRow row)
    {
        if (row.Segments.Any(segment => segment.RotationDegrees is not null))
        {
            return DirectionalMetricRowHeight;
        }

        return row.Segments.Count > 0 ? SegmentedMetricRowHeight : PlainMetricRowHeight;
    }

    private DesignV2LayoutBody BuildMetricRowsLayout(RectangleF rect, DesignV2MetricRowsBody metrics)
    {
        var layout = new DesignV2LayoutBody("metric-rows", LayoutRect(rect));
        if (metrics.Rows.Count == 0 && metrics.MetricSections.Count == 0 && metrics.Sections.Count == 0)
        {
            return layout with { State = "waiting" };
        }

        var metricRows = new List<DesignV2LayoutMetricRow>();
        var metricGrids = new List<DesignV2LayoutMetricGrid>();
        var gridHeight = MetricGridHeight(metrics.Sections);
        var rowsRect = metrics.Sections.Count == 0
            ? rect
            : new RectangleF(rect.Left, rect.Top, rect.Width, Math.Max(RowHeight, rect.Height - gridHeight - 8f));
        var metricBottom = rowsRect.Top;

        if (metrics.MetricSections.Count > 0)
        {
            var y = rowsRect.Top;
            foreach (var section in metrics.MetricSections.Where(section => section.Rows.Count > 0))
            {
                var firstRowHeight = MetricRowHeightFor(section.Rows[0]);
                if (y + MetricSectionTitleHeight + MetricSectionTitleBottomGap + firstRowHeight > rowsRect.Bottom)
                {
                    break;
                }

                var titleRect = new RectangleF(rowsRect.Left + 4, y, rowsRect.Width - 8, MetricSectionTitleHeight);
                y += MetricSectionTitleHeight + MetricSectionTitleBottomGap;
                foreach (var row in section.Rows)
                {
                    var rowHeight = MetricRowHeightFor(row);
                    if (y + rowHeight > rowsRect.Bottom)
                    {
                        break;
                    }

                    var rowRect = new RectangleF(rowsRect.Left, y, rowsRect.Width, rowHeight);
                    metricRows.Add(LayoutMetricRow(row, rowRect, section.Title, titleRect));
                    metricBottom = Math.Max(metricBottom, rowRect.Bottom);
                    y += rowHeight + MetricRowGap;
                }

                y += MetricSectionGap - MetricRowGap;
            }
        }
        else
        {
            var y = rowsRect.Top;
            foreach (var row in metrics.Rows)
            {
                var rowHeight = MetricRowHeightFor(row);
                if (y + rowHeight > rowsRect.Bottom)
                {
                    break;
                }

                var rowRect = new RectangleF(rowsRect.Left, y, rowsRect.Width, rowHeight);
                metricRows.Add(LayoutMetricRow(row, rowRect, null, null));
                metricBottom = Math.Max(metricBottom, rowRect.Bottom);
                y += rowHeight + MetricRowGap;
            }
        }

        if (metrics.Sections.Count > 0)
        {
            var sectionTop = Math.Min(rect.Bottom - 72f, metricBottom + 8f);
            foreach (var section in metrics.Sections)
            {
                if (sectionTop >= rect.Bottom - 34f)
                {
                    break;
                }

                var maxRows = Math.Max(1, Math.Min(section.Rows.Count, MaximumMetricGridRows(section, rect.Bottom - sectionTop)));
                var sectionHeight = MetricGridSectionHeight(maxRows);
                var sectionRect = new RectangleF(rect.Left, sectionTop, rect.Width, Math.Min(sectionHeight, rect.Bottom - sectionTop));
                metricGrids.Add(LayoutMetricGrid(section, sectionRect, maxRows));
                sectionTop += sectionRect.Height + 8f;
            }
        }

        return layout with
        {
            RowsBounds = LayoutRect(rowsRect),
            GridHeight = gridHeight,
            MetricRows = metricRows,
            MetricGrids = metricGrids,
            DrawnRows = metricRows.Count
        };
    }

    private static DesignV2LayoutMetricRow LayoutMetricRow(
        DesignV2MetricRow row,
        RectangleF rowRect,
        string? section,
        RectangleF? sectionTitleBounds)
    {
        var labelRect = new RectangleF(rowRect.Left + 10, rowRect.Top + 6, MetricLabelWidth, 16);
        var valueRect = new RectangleF(rowRect.Left + MetricLabelWidth + 12, rowRect.Top + 4, rowRect.Width - MetricLabelWidth - 18, rowRect.Height - 8);
        var segments = new List<DesignV2LayoutMetricSegment>();
        var count = Math.Min(6, row.Segments.Count);
        if (count > 0)
        {
            var gap = 3f;
            var width = Math.Max(1f, (valueRect.Width - gap * (count - 1)) / count);
            var x = valueRect.Left;
            for (var index = 0; index < count; index++)
            {
                var segment = row.Segments[index];
                var segmentRect = new RectangleF(x, valueRect.Top, width, valueRect.Height);
                segments.Add(new DesignV2LayoutMetricSegment(
                    index,
                    segment.Label,
                    segment.Value,
                    LayoutRect(segmentRect),
                    LayoutRect(new RectangleF(segmentRect.Left + 4, segmentRect.Top + 2, segmentRect.Width - 8, 9)),
                    LayoutRect(new RectangleF(segmentRect.Left + 4, segmentRect.Top + 12, segmentRect.Width - 8, Math.Max(9, segmentRect.Height - 13))))
                {
                    Foreground = ColorHex(SegmentColor(segment)),
                    Background = ColorHex(SegmentBackground(segment)),
                    Accent = TryParseHexColor(segment.AccentHex, out var segmentAccent) ? ColorHex(segmentAccent) : null,
                    RotationDegrees = segment.RotationDegrees,
                    Evidence = segment.Evidence.ToString()
                });
                x += width + gap;
            }
        }

        var hasRowAccent = TryParseHexColor(row.RowColorHex, out var rowAccent);
        return new DesignV2LayoutMetricRow(row.Label, row.Value, LayoutRect(rowRect), LayoutRect(labelRect), LayoutRect(valueRect))
        {
            Section = section,
            SectionTitleBounds = LayoutRect(sectionTitleBounds),
            Segments = segments,
            Evidence = row.Evidence.ToString(),
            Background = ColorHex(hasRowAccent
                ? Blend(SurfaceRaised, rowAccent, 12, 1)
                : SurfaceRaised),
            Foreground = ColorHex(EvidenceColor(row.Evidence)),
            Accent = hasRowAccent ? ColorHex(rowAccent) : null
        };
    }

    private static DesignV2LayoutMetricGrid LayoutMetricGrid(
        DesignV2MetricGridSection section,
        RectangleF rect,
        int maximumRows)
    {
        var headers = section.Headers.Count == 0
            ? new[] { "Info", "FL", "FR", "RL", "RR" }
            : section.Headers.Take(5).ToArray();
        var columns = headers.Length;
        var gap = 3f;
        var usableWidth = rect.Width - MetricGridCellInset * 2f - Math.Max(0, columns - 1) * gap;
        var columnWidth = Math.Max(42f, usableWidth / Math.Max(1, columns));
        var widths = Enumerable.Range(0, columns)
            .Select(_ => columnWidth)
            .ToArray();
        var headerCells = new List<DesignV2LayoutCell>();
        var rows = new List<DesignV2LayoutRow>();
        var y = rect.Top + MetricGridTitleHeight + MetricGridTitleBottomGap;
        var x = rect.Left + MetricGridCellInset;
        for (var index = 0; index < columns; index++)
        {
            headerCells.Add(new DesignV2LayoutCell(
                index,
                headers[index],
                headers[index],
                LayoutRect(new RectangleF(x, y, widths[index], MetricGridHeaderHeight)),
                index == 0 ? "left" : "right")
            {
                Foreground = ColorHex(TextMuted)
            });
            x += widths[index] + gap;
        }

        y += MetricGridHeaderHeight + MetricGridHeaderBottomGap;
        foreach (var (row, rowIndex) in section.Rows.Take(maximumRows).Select((row, rowIndex) => (row, rowIndex)))
        {
            if (y + MetricGridRowHeight > rect.Bottom)
            {
                break;
            }

            var rowBounds = new RectangleF(rect.Left, y, rect.Width, MetricGridRowHeight);
            var cellTop = y + MetricGridCellInset;
            x = rect.Left + MetricGridCellInset;
            var cells = new List<DesignV2LayoutCell>
            {
                new(0, headers[0], row.Label, LayoutRect(new RectangleF(x, cellTop, widths[0], MetricGridCellHeight)), "left")
                {
                    Background = ColorHex(SurfaceRaised),
                    Foreground = ColorHex(TextSecondary)
                }
            };
            x += widths[0] + gap;
            for (var index = 1; index < columns; index++)
            {
                var cell = index - 1 < row.Cells.Count ? row.Cells[index - 1] : new DesignV2MetricGridCell("--", DesignV2Evidence.Unavailable);
                cells.Add(new DesignV2LayoutCell(index, headers[index], cell.Value, LayoutRect(new RectangleF(x, cellTop, widths[index], MetricGridCellHeight)), "right")
                {
                    Evidence = cell.Evidence.ToString(),
                    Background = ColorHex(EvidenceBackground(cell.Evidence)),
                    Foreground = ColorHex(EvidenceColor(cell.Evidence))
                });
                x += widths[index] + gap;
            }

            rows.Add(new DesignV2LayoutRow(rowIndex, rowIndex, "grid-row", LayoutRect(rowBounds))
            {
                Text = row.Label,
                Cells = cells,
                Evidence = row.Evidence.ToString(),
                Foreground = ColorHex(EvidenceColor(row.Evidence))
            });
            y += MetricGridRowHeight + MetricGridRowGap;
        }

        return new DesignV2LayoutMetricGrid(section.Title, LayoutRect(rect), headerCells, rows);
    }

    private static DesignV2LayoutBody BuildGraphLayout(RectangleF rect, DesignV2GraphBody graph)
    {
        var frame = RectangleF.Inflate(rect, -12, -14);
        const float axisWidth = 58f;
        const float xAxisHeight = 17f;
        var plotHeight = Math.Max(40, frame.Height - xAxisHeight);
        var metricsTableWidth = FocusedGapMetricsTableWidth(frame);
        var metricsTableRect = metricsTableWidth > 0f
            ? new RectangleF(frame.Right - metricsTableWidth, frame.Top, metricsTableWidth, plotHeight)
            : RectangleF.Empty;
        var chartRight = metricsTableWidth > 0f
            ? metricsTableRect.Left - GapMetricsTableGap
            : frame.Right;
        var labelLane = new RectangleF(
            chartRight - GapEndpointLabelLaneWidth,
            frame.Top,
            GapEndpointLabelLaneWidth,
            plotHeight);
        var plot = new RectangleF(
            frame.Left + axisWidth,
            frame.Top,
            Math.Max(40, labelLane.Left - (frame.Left + axisWidth)),
            plotHeight);
        var axisBounds = new RectangleF(frame.Left, frame.Top, axisWidth - 8, plot.Height);
        var scale = graph.Scale ?? DesignV2GapScale.Leader(graph.MaxGapSeconds ?? 1d);
        var maxGapSeconds = Math.Max(1d, scale.MaxGapSeconds);
        var seriesLayouts = graph.Series
            .Select((series, index) => BuildGraphSeriesLayout(graph, plot, maxGapSeconds, series, index))
            .ToArray();
        return new DesignV2LayoutBody("graph", LayoutRect(rect))
        {
            Graph = new DesignV2LayoutGraph(
                LayoutRect(frame),
                LayoutRect(plot),
                LayoutRect(axisBounds),
                LayoutRect(labelLane),
                metricsTableWidth > 0f ? LayoutRect(metricsTableRect) : null,
                graph.Points.Count,
                graph.Series.Count,
                graph.Series.Sum(series => series.Points.Count),
                graph.TrendMetrics.Count,
                graph.StartSeconds,
                graph.EndSeconds)
            {
                Series = seriesLayouts,
                WeatherBands = BuildGraphWeatherBands(graph, plot),
                Markers = BuildGraphMarkers(graph, plot, maxGapSeconds),
                GridLines = BuildGraphGridLines(graph, scale, plot),
                MetricRows = BuildGraphMetricRows(metricsTableRect, graph),
                Scale = scale.IsFocusRelative ? "focus-relative" : "leader",
                AheadSeconds = scale.IsFocusRelative ? scale.AheadSeconds : null,
                BehindSeconds = scale.IsFocusRelative ? scale.BehindSeconds : null,
                LatestReferenceGapSeconds = scale.IsFocusRelative ? scale.LatestReferenceGapSeconds : null,
                MaxGapSeconds = graph.MaxGapSeconds,
                LapReferenceSeconds = graph.LapReferenceSeconds,
                ComparisonLabel = graph.ComparisonLabel
            }
        };
    }

    private static DesignV2LayoutGraphSeries BuildGraphSeriesLayout(
        DesignV2GraphBody graph,
        RectangleF plot,
        double maxGapSeconds,
        DesignV2GapSeries series,
        int index)
    {
        var baseColor = GapSeriesColor(series, index, graph.ThreatCarIdx);
        var effectiveAlpha = series.Alpha * GapSeriesAlphaMultiplier(series, graph.ThreatCarIdx);
        var orderedPoints = series.Points
            .OrderBy(point => point.AxisSeconds)
            .Select(point =>
            {
                var graphPoint = GapGraphPoint(point, graph, plot, maxGapSeconds);
                return new DesignV2LayoutGraphPoint(
                    point.AxisSeconds,
                    point.GapSeconds,
                    point.StartsSegment,
                    LayoutPoint(graphPoint));
            })
            .ToArray();
        var latest = orderedPoints.LastOrDefault();
        var endpointLabel = series.IsClassLeader
            ? "P1"
            : series.ClassPosition is { } position
                ? $"P{position}"
                : $"#{series.CarIdx}";
        return new DesignV2LayoutGraphSeries(
            index,
            GapDrawPriority(series, graph.ThreatCarIdx),
            series.CarIdx,
            series.IsReference,
            series.IsClassLeader,
            series.ClassPosition,
            series.Points.Count,
            ColorHex(baseColor),
            ColorHex(WithAlpha(baseColor, effectiveAlpha)),
            series.Alpha,
            effectiveAlpha,
            series.IsReference ? 2.6f : series.IsClassLeader ? 1.8f : 1.25f,
            series.IsStickyExit || series.IsStale,
            series.IsStickyExit,
            series.IsStale)
        {
            Points = orderedPoints,
            LatestPoint = latest?.Point,
            EndpointLabel = endpointLabel
        };
    }

    private static IReadOnlyList<DesignV2LayoutGraphBand> BuildGraphWeatherBands(
        DesignV2GraphBody graph,
        RectangleF plot)
    {
        if (graph.Weather.Count == 0)
        {
            return [];
        }

        var bands = new List<DesignV2LayoutGraphBand>();
        foreach (var (point, index) in graph.Weather.Select((point, index) => (point, index)))
        {
            if (GapWeatherColor(point.Condition) is not { } color)
            {
                continue;
            }

            var nextAxis = index + 1 < graph.Weather.Count
                ? graph.Weather[index + 1].AxisSeconds
                : graph.EndSeconds;
            var x = GapAxisToX(graph, plot, point.AxisSeconds);
            var nextX = GapAxisToX(graph, plot, nextAxis);
            if (nextX <= x)
            {
                continue;
            }

            bands.Add(new DesignV2LayoutGraphBand(
                point.Condition.ToString(),
                point.AxisSeconds,
                nextAxis,
                LayoutRect(new RectangleF(x, plot.Top, Math.Max(1f, nextX - x), plot.Height)),
                ColorHex(color)));
        }

        return bands;
    }

    private static IReadOnlyList<DesignV2LayoutGraphMarker> BuildGraphMarkers(
        DesignV2GraphBody graph,
        RectangleF plot,
        double maxGapSeconds)
    {
        var markers = new List<DesignV2LayoutGraphMarker>();
        foreach (var marker in graph.LeaderChanges)
        {
            var x = GapAxisToX(graph, plot, marker.AxisSeconds);
            markers.Add(new DesignV2LayoutGraphMarker(
                "leader-change",
                "leader",
                LayoutPoint(x, plot.Top),
                LayoutPoint(x, plot.Bottom))
            {
                AxisSeconds = marker.AxisSeconds,
                Color = "#73FFFFFF"
            });
        }

        foreach (var marker in graph.DriverChanges)
        {
            var point = GapGraphPoint(marker.AxisSeconds, marker.GapSeconds, graph, plot, maxGapSeconds);
            var color = marker.IsReference ? Green : TextSecondary;
            markers.Add(new DesignV2LayoutGraphMarker(
                "driver-change",
                marker.Label,
                LayoutPoint(point),
                LayoutPoint(point))
            {
                AxisSeconds = marker.AxisSeconds,
                GapSeconds = marker.GapSeconds,
                CarIdx = marker.CarIdx,
                IsReference = marker.IsReference,
                Color = ColorHex(color)
            });
        }

        return markers;
    }

    private static IReadOnlyList<DesignV2LayoutLine> BuildGraphGridLines(
        DesignV2GraphBody graph,
        DesignV2GapScale scale,
        RectangleF plot)
    {
        var lines = new List<DesignV2LayoutLine>
        {
            new("axis-top", LayoutPoint(plot.Left, plot.Top), LayoutPoint(plot.Right, plot.Top))
            {
                Color = ColorHex(Color.FromArgb(70, TextMuted)),
                StrokeWidth = 1f
            },
            new("axis-bottom", LayoutPoint(plot.Left, plot.Bottom), LayoutPoint(plot.Right, plot.Bottom))
            {
                Color = ColorHex(Color.FromArgb(70, TextMuted)),
                StrokeWidth = 1f
            }
        };

        if (scale.IsFocusRelative)
        {
            var referenceY = GapFocusReferenceY(plot);
            lines.Add(new DesignV2LayoutLine("focus-reference", LayoutPoint(plot.Left, referenceY), LayoutPoint(plot.Right, referenceY))
            {
                Color = ColorHex(Color.FromArgb(110, Green)),
                StrokeWidth = 1.2f
            });
            AddFocusGraphGridLines(lines, "focus-ahead-grid", scale.AheadSeconds, value => GapDeltaToY(-value, scale, plot), plot);
            AddFocusGraphGridLines(lines, "focus-behind-grid", scale.BehindSeconds, value => GapDeltaToY(value, scale, plot), plot);
            return lines;
        }

        var step = NiceGridStep(scale.MaxGapSeconds / 4d);
        for (var value = step; value < scale.MaxGapSeconds; value += step)
        {
            var y = GapToY(value, scale.MaxGapSeconds, plot);
            lines.Add(new DesignV2LayoutLine("gap-grid", LayoutPoint(plot.Left, y), LayoutPoint(plot.Right, y))
            {
                Color = ColorHex(Color.FromArgb(46, TextMuted)),
                StrokeWidth = 1f
            });
        }

        if (graph.LapReferenceSeconds is not { } lapSeconds || lapSeconds < 20d || scale.MaxGapSeconds < lapSeconds * 0.85d)
        {
            return lines;
        }

        for (var lap = 1; lap * lapSeconds < scale.MaxGapSeconds; lap++)
        {
            var y = GapToY(lap * lapSeconds, scale.MaxGapSeconds, plot);
            lines.Add(new DesignV2LayoutLine("lap-grid", LayoutPoint(plot.Left, y), LayoutPoint(plot.Right, y))
            {
                Color = ColorHex(Color.FromArgb(130, TextPrimary)),
                StrokeWidth = 1.2f
            });
        }

        return lines;
    }

    private static void AddFocusGraphGridLines(
        ICollection<DesignV2LayoutLine> lines,
        string kind,
        double seconds,
        Func<double, float> yForValue,
        RectangleF plot)
    {
        var step = NiceGridStep(seconds / 2d);
        for (var value = step; value < seconds; value += step)
        {
            var y = yForValue(value);
            lines.Add(new DesignV2LayoutLine(kind, LayoutPoint(plot.Left, y), LayoutPoint(plot.Right, y))
            {
                Color = ColorHex(Color.FromArgb(46, TextMuted)),
                StrokeWidth = 1f
            });
        }
    }

    private static IReadOnlyList<DesignV2LayoutRow> BuildGraphMetricRows(RectangleF rect, DesignV2GraphBody graph)
    {
        if (rect.Width <= 0f || rect.Height <= 0f || graph.TrendMetrics.Count == 0)
        {
            return [];
        }

        var rowHeight = Math.Max(9.5f, Math.Min(26f, (rect.Height - 8f - 38f) / Math.Max(1, graph.TrendMetrics.Count)));
        var rowTextHeight = Math.Max(10f, Math.Min(14f, rowHeight));
        var rows = new List<DesignV2LayoutRow>();
        for (var index = 0; index < graph.TrendMetrics.Count; index++)
        {
            var metric = graph.TrendMetrics[index];
            var y = rect.Top + 38f + index * rowHeight;
            rows.Add(new DesignV2LayoutRow(
                index,
                index,
                "graph-metric-row",
                LayoutRect(new RectangleF(rect.Left + 8f, y, rect.Width - 16f, rowTextHeight)))
            {
                Text = metric.Label,
                Evidence = metric.State,
                Cells =
                [
                    new DesignV2LayoutCell(
                        0,
                        "Metric",
                        metric.Label,
                        LayoutRect(GapMetricLabelBounds(rect, y, rowTextHeight)),
                        "left")
                    {
                        Foreground = ColorHex(TextSecondary)
                    },
                    new DesignV2LayoutCell(
                        1,
                        string.IsNullOrWhiteSpace(graph.ComparisonLabel) ? "--" : graph.ComparisonLabel,
                        GapMetricValueText(metric),
                        LayoutRect(GapMetricValueBounds(rect, y, rowTextHeight)),
                        "left")
                    {
                        Foreground = ColorHex(GapMetricValueColor(metric, graph.MetricDeadbandSeconds))
                    },
                    new DesignV2LayoutCell(
                        2,
                        "Threat",
                        GapMetricChaserText(metric),
                        LayoutRect(GapMetricThreatBounds(rect, y, rowTextHeight)),
                        "left")
                    {
                        Foreground = ColorHex(GapMetricChaserColor(metric))
                    }
                ]
            });
        }

        return rows;
    }

    private DesignV2LayoutBody BuildChatLayout(RectangleF rect, DesignV2ChatBody chat)
    {
        using var bitmap = new Bitmap(1, 1);
        using var graphics = Graphics.FromImage(bitmap);
        using var messageFont = FontOf(10.5f);
        var rowWidth = Math.Max(1f, rect.Width - 16f);
        var innerHeight = Math.Max(1f, rect.Height - 16f);
        var visibleRows = VisibleChatRows(graphics, chat.Rows, messageFont, rowWidth, innerHeight);
        var totalRowsHeight = visibleRows.Sum(row => row.Height) + Math.Max(0, visibleRows.Count - 1) * 8f;
        var y = Math.Max(rect.Top + 8f, rect.Bottom - 8f - totalRowsHeight);
        var rows = new List<DesignV2LayoutRow>();
        for (var index = 0; index < visibleRows.Count; index++)
        {
            var row = visibleRows[index];
            var rowRect = new RectangleF(rect.Left + 8, y, rowWidth, row.Height);
            rows.Add(new DesignV2LayoutRow(index, index, "chat-row", LayoutRect(rowRect))
            {
                Text = row.Row.Author,
                Detail = row.Row.Message,
                Evidence = row.Row.Evidence.ToString(),
                Foreground = ColorHex(TryParseHexColor(row.Row.AuthorColorHex, out var authorColor)
                    ? authorColor
                    : EvidenceColor(row.Row.Evidence)),
                Background = ColorHex(SurfaceRaised)
            });
            y += row.Height + 8f;
        }

        return new DesignV2LayoutBody("chat", LayoutRect(rect))
        {
            Rows = rows,
            DrawnRows = rows.Count
        };
    }

    private static DesignV2LayoutBody BuildInputsLayout(RectangleF content, DesignV2InputsBody body)
    {
        var railWidth = body.HasRail
            ? body.HasGraph
                ? Math.Min(204f, Math.Max(136f, content.Width * 0.40f))
                : Math.Min(240f, content.Width)
            : 0f;
        RectangleF? graph = body.HasGraph
            ? new RectangleF(
                content.Left,
                content.Top,
                Math.Max(160, content.Width - railWidth - (body.HasRail ? 18 : 0)),
                Math.Max(40, content.Height))
            : null;
        RectangleF? rail = null;
        IReadOnlyList<DesignV2LayoutInputItem> inputItems = [];
        if (body.HasRail)
        {
            var railLeft = graph is { } railGraph
                ? railGraph.Right + 18
                : content.Left + (content.Width - railWidth) / 2f;
            rail = new RectangleF(railLeft, content.Top, railWidth, content.Height);
            var railContent = RectangleF.Inflate(rail.Value, -8, -8);
            inputItems = BuildInputRailLayout(
                    railContent,
                    body.ShowThrottle,
                    body.ShowBrake,
                    body.ShowClutch,
                    body.ShowSteering,
                    body.ShowGear,
                    body.ShowSpeed)
                .Items
                .Select(item => new DesignV2LayoutInputItem(item.Kind.ToString(), LayoutRect(item.Bounds)))
                .ToArray();
        }

        return new DesignV2LayoutBody("inputs", LayoutRect(content))
        {
            Inputs = new DesignV2LayoutInputs(
                body.HasContent,
                LayoutRect(graph),
                LayoutRect(rail),
                railWidth,
                body.Trace.Count,
                inputItems)
            {
                TraceSeries = BuildInputTraceLayouts(graph, body),
                GridLines = BuildInputGridLines(graph)
            }
        };
    }

    private static IReadOnlyList<DesignV2LayoutInputTraceSeries> BuildInputTraceLayouts(RectangleF? graph, DesignV2InputsBody body)
    {
        if (graph is not { } graphRect)
        {
            return [];
        }

        var series = new List<DesignV2LayoutInputTraceSeries>();
        AddTrace(body.ShowThrottleTrace, "throttle", Green, point => point.Throttle);
        AddTrace(body.ShowBrakeTrace, "brake", Error, point => point.Brake);
        AddTrace(body.ShowClutchTrace, "clutch", Cyan, point => point.Clutch);
        if (body.ShowBrakeTrace && body.Trace.Count >= 2)
        {
            var absSegments = new List<DesignV2LayoutTraceCurveSegment>();
            for (var index = 1; index < body.Trace.Count; index++)
            {
                if (!body.Trace[index].BrakeAbsActive)
                {
                    continue;
                }

                var start = InputTracePoint(body.Trace[index - 1].Brake, index - 1, body.Trace.Count, graphRect);
                var end = InputTracePoint(body.Trace[index].Brake, index, body.Trace.Count, graphRect);
                absSegments.Add(new DesignV2LayoutTraceCurveSegment(start, start, end, end));
            }

            if (absSegments.Count > 0)
            {
                series.Add(new DesignV2LayoutInputTraceSeries("brake-abs", ColorHex(Amber), 3f)
                {
                    Curves = absSegments
                });
            }
        }

        return series;

        void AddTrace(bool enabled, string kind, Color color, Func<InputStateTracePoint, double> select)
        {
            if (!enabled)
            {
                return;
            }

            if (body.Trace.Count < 2)
            {
                var y = graphRect.Top + graphRect.Height / 2f;
                var start = LayoutPoint(graphRect.Left + 8f, y);
                var end = LayoutPoint(graphRect.Right - 8f, y);
                series.Add(new DesignV2LayoutInputTraceSeries(kind, ColorHex(Color.FromArgb(60, color)), 2f)
                {
                    Curves = [new DesignV2LayoutTraceCurveSegment(start, start, end, end)]
                });
                return;
            }

            var points = body.Trace
                .Select((point, index) => InputTracePoint(select(point), index, body.Trace.Count, graphRect))
                .ToArray();
            series.Add(new DesignV2LayoutInputTraceSeries(kind, ColorHex(color), 2f)
            {
                Points = points,
                Curves = BuildSmoothTraceCurves(points)
            });
        }
    }

    private static IReadOnlyList<DesignV2LayoutLine> BuildInputGridLines(RectangleF? graph)
    {
        if (graph is not { } graphRect)
        {
            return [];
        }

        var lines = new List<DesignV2LayoutLine>();
        for (var step = 1; step < 4; step++)
        {
            var y = graphRect.Top + graphRect.Height * step / 4f;
            lines.Add(new DesignV2LayoutLine(
                "input-grid",
                LayoutPoint(graphRect.Left, y),
                LayoutPoint(graphRect.Right, y))
            {
                Color = ColorHex(Color.FromArgb(46, TextMuted)),
                StrokeWidth = 1f
            });
        }

        return lines;
    }

    private static DesignV2LayoutPoint InputTracePoint(double value, int index, int count, RectangleF rect)
    {
        return LayoutPoint(
            rect.Left + index / (float)Math.Max(1, count - 1) * rect.Width,
            rect.Bottom - (float)Math.Clamp(value, 0d, 1d) * rect.Height);
    }

    private static IReadOnlyList<DesignV2LayoutTraceCurveSegment> BuildSmoothTraceCurves(IReadOnlyList<DesignV2LayoutPoint> points)
    {
        if (points.Count < 2)
        {
            return [];
        }

        var curves = new List<DesignV2LayoutTraceCurveSegment>();
        for (var index = 0; index < points.Count - 1; index++)
        {
            var p0 = index == 0 ? points[index] : points[index - 1];
            var p1 = points[index];
            var p2 = points[index + 1];
            var p3 = index + 2 < points.Count ? points[index + 2] : p2;
            var control1 = LayoutPoint(
                p1.X + (p2.X - p0.X) / 6f,
                ClampSmoothInputControlY(p1.Y + (p2.Y - p0.Y) / 6f, p1.Y, p2.Y));
            var control2 = LayoutPoint(
                p2.X - (p3.X - p1.X) / 6f,
                ClampSmoothInputControlY(p2.Y - (p3.Y - p1.Y) / 6f, p1.Y, p2.Y));
            curves.Add(new DesignV2LayoutTraceCurveSegment(p1, control1, control2, p2));
        }

        return curves;
    }

    private static DesignV2LayoutBody BuildRadarLayout(RectangleF rect, DesignV2RadarBody radar)
    {
        var model = radar.RenderModel;
        var surfaceAlpha = Math.Clamp(radar.SurfaceAlpha, 0d, 1d);
        var diameter = Math.Max(20, Math.Min(rect.Width, rect.Height));
        var radarRect = new RectangleF(
            rect.Left + (rect.Width - diameter) / 2f,
            rect.Top + (rect.Height - diameter) / 2f,
            diameter,
            diameter);
        var scaleX = radarRect.Width / (float)Math.Max(1d, model.Width);
        var scaleY = radarRect.Height / (float)Math.Max(1d, model.Height);
        var primitives = RadarPrimitiveLayouts(radarRect, model, scaleX, scaleY, surfaceAlpha);
        var labels = model.Labels.Select(label => RadarLabelLayout(radarRect, label, scaleX, scaleY, surfaceAlpha)).ToArray();
        return new DesignV2LayoutBody("radar", LayoutRect(rect))
        {
            Vector = new DesignV2LayoutVector(
                LayoutRect(radarRect),
                model.Width,
                model.Height,
                scaleX,
                scaleY,
                model.Cars.Count,
                primitives.Count,
                labels.Length)
            {
                Items = model.Cars.Select(car => new DesignV2LayoutVectorItem(
                    car.Kind,
                    car.CarIdx,
                    LayoutRect(ScaleRect(radarRect, car.X, car.Y, car.Width, car.Height, scaleX, scaleY)))
                {
                    Fill = ColorHex(RenderColor(car.Fill, surfaceAlpha)),
                    Stroke = ColorHex(RenderColor(car.Stroke, surfaceAlpha)),
                    StrokeWidth = Math.Max(1f, (float)car.StrokeWidth * Math.Min(scaleX, scaleY))
                }).ToArray(),
                Primitives = primitives,
                Labels = labels,
                ShouldRender = model.ShouldRender && surfaceAlpha > model.MinimumVisibleAlpha,
                SurfaceAlpha = surfaceAlpha
            }
        };
    }

    private static DesignV2LayoutBody BuildTrackMapLayout(RectangleF rect, DesignV2TrackMapBody body)
    {
        var model = body.RenderModel;
        var size = Math.Max(20, Math.Min(rect.Width, rect.Height));
        var target = new RectangleF(
            rect.Left + (rect.Width - size) / 2f,
            rect.Top + (rect.Height - size) / 2f,
            size,
            size);
        var scaleX = target.Width / (float)Math.Max(1d, model.Width);
        var scaleY = target.Height / (float)Math.Max(1d, model.Height);
        var labels = model.Markers
            .Where(marker => !string.IsNullOrWhiteSpace(marker.Label))
            .Select(marker => TrackMapMarkerLabelLayout(target, marker, scaleX, scaleY))
            .ToArray();
        return new DesignV2LayoutBody("track-map", LayoutRect(rect))
        {
            Vector = new DesignV2LayoutVector(
                LayoutRect(target),
                model.Width,
                model.Height,
                scaleX,
                scaleY,
                model.Markers.Count,
                model.Primitives.Count,
                labels.Length)
            {
                Items = model.Markers.Select(marker => TrackMapMarkerItem(target, marker, scaleX, scaleY)).ToArray(),
                Primitives = model.Primitives.Select(primitive => TrackMapPrimitiveLayout(target, primitive, scaleX, scaleY)).ToArray(),
                Labels = labels,
                ShouldRender = model.IsAvailable
            }
        };
    }

    private static DesignV2LayoutBody BuildFlagsLayout(RectangleF rect, DesignV2FlagsBody body)
    {
        var bounds = new RectangleF(
            rect.Left + FlagOuterPadding,
            rect.Top + FlagOuterPadding,
            Math.Max(1f, rect.Width - FlagOuterPadding * 2f),
            Math.Max(1f, rect.Height - FlagOuterPadding * 2f));
        var (columns, rows) = FlagGridFor(body.Flags.Count);
        var cellWidth = (bounds.Width - (columns - 1) * FlagCellGap) / columns;
        var cellHeight = (bounds.Height - (rows - 1) * FlagCellGap) / rows;
        var cells = new List<DesignV2LayoutFlagCell>();
        for (var index = 0; index < body.Flags.Count; index++)
        {
            var row = index / columns;
            var column = index % columns;
            var cell = new RectangleF(
                bounds.Left + column * (cellWidth + FlagCellGap),
                bounds.Top + row * (cellHeight + FlagCellGap),
                cellWidth,
                cellHeight);
            var poleX = cell.Left + Math.Max(12f, cell.Width * 0.16f);
            var clothLeft = poleX + 1f;
            var clothWidth = Math.Max(48f, cell.Right - clothLeft - 8f);
            var clothHeight = Math.Max(24f, Math.Min(cell.Height * 0.7f, clothWidth * 0.58f));
            var clothTop = cell.Top + Math.Max(4f, (cell.Height - clothHeight) * 0.32f);
            cells.Add(new DesignV2LayoutFlagCell(
                index,
                row,
                column,
                body.Flags[index].Kind.ToString(),
                LayoutRect(cell),
                LayoutRect(new RectangleF(clothLeft, clothTop, clothWidth, clothHeight))));
        }

        return new DesignV2LayoutBody("flags", LayoutRect(rect))
        {
            GridColumns = columns,
            GridRows = rows,
            FlagCells = cells,
            DrawnRows = body.Flags.Count
        };
    }

    private static IReadOnlyList<DesignV2LayoutVectorPrimitive> RadarPrimitiveLayouts(
        RectangleF target,
        CarRadarRenderModel model,
        float scaleX,
        float scaleY,
        double surfaceAlpha)
    {
        var primitives = new List<DesignV2LayoutVectorPrimitive>
        {
            RadarCirclePrimitive("background", target, model.Background, scaleX, scaleY, surfaceAlpha)
        };
        primitives.AddRange(model.Rings.Select(ring => RadarCirclePrimitive("ring", target, ring, scaleX, scaleY, surfaceAlpha)));
        if (model.MulticlassArc is { } arc)
        {
            primitives.Add(new DesignV2LayoutVectorPrimitive(
                "arc",
                LayoutRect(ScaleRect(target, arc.X, arc.Y, arc.Width, arc.Height, scaleX, scaleY)),
                [],
                false,
                arc.StartDegrees,
                arc.SweepDegrees,
                null,
                ColorHex(RenderColor(arc.Stroke, surfaceAlpha)),
                Math.Max(1f, (float)arc.StrokeWidth * Math.Min(scaleX, scaleY))));
        }

        return primitives;
    }

    private static DesignV2LayoutVectorPrimitive RadarCirclePrimitive(
        string kind,
        RectangleF target,
        CarRadarRenderCircle circle,
        float scaleX,
        float scaleY,
        double surfaceAlpha)
    {
        return new DesignV2LayoutVectorPrimitive(
            kind,
            LayoutRect(ScaleRect(target, circle.X, circle.Y, circle.Width, circle.Height, scaleX, scaleY)),
            [],
            false,
            null,
            null,
            circle.Fill is { } fill ? ColorHex(RenderColor(fill, surfaceAlpha)) : null,
            circle.Stroke is { } stroke ? ColorHex(RenderColor(stroke, surfaceAlpha)) : null,
            circle.Stroke is not null && circle.StrokeWidth > 0d
                ? Math.Max(1f, (float)circle.StrokeWidth * Math.Min(scaleX, scaleY))
                : 0f);
    }

    private static DesignV2LayoutVectorLabel RadarLabelLayout(
        RectangleF target,
        CarRadarRenderText label,
        float scaleX,
        float scaleY,
        double surfaceAlpha)
    {
        var scale = Math.Min(scaleX, scaleY);
        return new DesignV2LayoutVectorLabel(
            label.Text,
            LayoutRect(ScaleRect(target, label.X, label.Y, label.Width, label.Height, scaleX, scaleY)),
            label.FontSize * scale,
            label.Bold,
            label.Alignment,
            ColorHex(RenderColor(label.Color, surfaceAlpha)));
    }

    private static DesignV2LayoutVectorItem TrackMapMarkerItem(
        RectangleF target,
        TrackMapRenderMarker marker,
        float scaleX,
        float scaleY)
    {
        var center = ScalePoint(target, new TrackMapRenderPoint(marker.X, marker.Y), scaleX, scaleY);
        var scale = Math.Min(scaleX, scaleY);
        var radius = (float)marker.Radius * scale;
        var markerRect = new RectangleF(center.X - radius, center.Y - radius, radius * 2f, radius * 2f);
        DesignV2LayoutRect? alertRingBounds = null;
        if (marker.AlertRingRadius > 0d)
        {
            var ringRadius = (float)marker.AlertRingRadius * scale;
            alertRingBounds = LayoutRect(new RectangleF(center.X - ringRadius, center.Y - ringRadius, ringRadius * 2f, ringRadius * 2f));
        }

        return new DesignV2LayoutVectorItem(
            marker.IsFocus ? "focus-marker" : "marker",
            marker.CarIdx,
            LayoutRect(markerRect))
        {
            Fill = ColorHex(RenderTrackMapColor(marker.Fill)),
            Stroke = ColorHex(RenderTrackMapColor(marker.Stroke)),
            StrokeWidth = Math.Max(1f, (float)marker.StrokeWidth * scale),
            Label = marker.Label,
            LabelColor = ColorHex(RenderTrackMapColor(marker.LabelColor)),
            AlertKind = marker.AlertKind,
            AlertRingBounds = alertRingBounds,
            AlertRingStroke = marker.AlertRingStroke is { } ringStroke ? ColorHex(RenderTrackMapColor(ringStroke)) : null,
            AlertRingStrokeWidth = marker.AlertRingStrokeWidth > 0d
                ? Math.Max(1f, (float)marker.AlertRingStrokeWidth * scale)
                : null
        };
    }

    private static DesignV2LayoutVectorLabel TrackMapMarkerLabelLayout(
        RectangleF target,
        TrackMapRenderMarker marker,
        float scaleX,
        float scaleY)
    {
        var center = ScalePoint(target, new TrackMapRenderPoint(marker.X, marker.Y), scaleX, scaleY);
        var scale = Math.Min(scaleX, scaleY);
        var radius = (float)marker.Radius * scale;
        var markerRect = new RectangleF(center.X - radius, center.Y - radius, radius * 2f, radius * 2f);
        return new DesignV2LayoutVectorLabel(
            marker.Label ?? string.Empty,
            LayoutRect(markerRect),
            marker.LabelFontSize * scale,
            true,
            "center",
            ColorHex(RenderTrackMapColor(marker.LabelColor)));
    }

    private static DesignV2LayoutVectorPrimitive TrackMapPrimitiveLayout(
        RectangleF target,
        TrackMapRenderPrimitive primitive,
        float scaleX,
        float scaleY)
    {
        var scale = Math.Min(scaleX, scaleY);
        var points = primitive.Points.Select(point => LayoutPoint(ScalePoint(target, point, scaleX, scaleY))).ToArray();
        return new DesignV2LayoutVectorPrimitive(
            primitive.Kind,
            primitive.Rect is { } rect
                ? LayoutRect(ScaleRect(target, rect.X, rect.Y, rect.Width, rect.Height, scaleX, scaleY))
                : BoundsForPoints(points),
            points,
            primitive.Closed,
            primitive.Kind == "arc" ? primitive.StartDegrees : null,
            primitive.Kind == "arc" ? primitive.SweepDegrees : null,
            primitive.Fill is { } fill ? ColorHex(RenderTrackMapColor(fill)) : null,
            primitive.Stroke is { } stroke ? ColorHex(RenderTrackMapColor(stroke)) : null,
            primitive.StrokeWidth > 0d ? Math.Max(1f, (float)primitive.StrokeWidth * scale) : 0f);
    }

    private static DesignV2LayoutRect? BoundsForPoints(IReadOnlyList<DesignV2LayoutPoint> points)
    {
        if (points.Count == 0)
        {
            return null;
        }

        var left = points.Min(point => point.X);
        var top = points.Min(point => point.Y);
        var right = points.Max(point => point.X);
        var bottom = points.Max(point => point.Y);
        return new DesignV2LayoutRect(left, top, Math.Max(0f, right - left), Math.Max(0f, bottom - top));
    }

    private static DesignV2LayoutRect LayoutRect(Rectangle bounds)
    {
        return new DesignV2LayoutRect(bounds.X, bounds.Y, bounds.Width, bounds.Height);
    }

    private static DesignV2LayoutRect LayoutRect(RectangleF bounds)
    {
        return new DesignV2LayoutRect(
            RoundLayout(bounds.X),
            RoundLayout(bounds.Y),
            RoundLayout(bounds.Width),
            RoundLayout(bounds.Height));
    }

    private static DesignV2LayoutRect? LayoutRect(RectangleF? bounds)
    {
        return bounds is { } value ? LayoutRect(value) : null;
    }

    private static DesignV2LayoutPoint LayoutPoint(PointF point)
    {
        return LayoutPoint(point.X, point.Y);
    }

    private static DesignV2LayoutPoint LayoutPoint(float x, float y)
    {
        return new DesignV2LayoutPoint(RoundLayout(x), RoundLayout(y));
    }

    private static float GapAxisToX(DesignV2GraphBody graph, RectangleF plot, double axisSeconds)
    {
        var domain = Math.Max(1d, graph.EndSeconds - graph.StartSeconds);
        return plot.Left + (float)Math.Clamp((axisSeconds - graph.StartSeconds) / domain, 0d, 1d) * plot.Width;
    }

    private static float RoundLayout(float value)
    {
        return MathF.Round(value, 3);
    }

    private static string AlignmentName(ContentAlignment alignment)
    {
        return alignment switch
        {
            ContentAlignment.TopRight or ContentAlignment.MiddleRight or ContentAlignment.BottomRight => "right",
            ContentAlignment.TopCenter or ContentAlignment.MiddleCenter or ContentAlignment.BottomCenter => "center",
            _ => "left"
        };
    }

    private static string ColorHex(Color color)
    {
        return $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    private static string ColorRgbHex(Color color)
    {
        return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    private bool DrawCustomOverlay(Graphics graphics, Rectangle bounds, DesignV2OverlayModel model)
    {
        var rect = RectangleF.Inflate(bounds, -0.5f, -0.5f);
        switch (model.Body)
        {
            case DesignV2RadarBody radar:
                DrawRadarOverlay(graphics, rect, radar);
                return true;
            case DesignV2InputsBody inputs:
                DrawInputsOverlay(graphics, rect, model, inputs);
                return true;
            case DesignV2TrackMapBody trackMap:
                DrawTrackMapOverlay(graphics, rect, trackMap);
                return true;
            case DesignV2FlagsBody flags:
                DrawFlagsOverlay(graphics, rect, flags);
                return true;
            default:
                return false;
        }
    }

    private void DrawOverlay(Graphics graphics, Rectangle bounds, DesignV2OverlayModel model)
    {
        var outer = RectangleF.Inflate(bounds, -0.5f, -0.5f);
        FillRounded(graphics, outer, 8, Surface, Border);
        FillRounded(graphics, new RectangleF(outer.Left, outer.Top + 7, 3, Math.Max(1, outer.Height - 14)), 2, EvidenceColor(model.Evidence), null);
        var headerBottom = outer.Top;
        if (model.ShowHeader)
        {
            var header = new RectangleF(outer.Left + 1, outer.Top + 1, outer.Width - 2, HeaderHeight);
            FillRounded(graphics, header, 7, TitleBar, null);
            using (var accent = new SolidBrush(Cyan))
            {
                graphics.FillRectangle(accent, outer.Left, header.Bottom - 1, outer.Width, 2);
            }

            using var titleFont = FontOf(14, FontStyle.Bold);
            using var statusFont = FontOf(11, FontStyle.Bold);
            var closeButtonSpace = _kind == DesignV2LiveOverlayKind.StreamChat ? StreamChatCloseButtonStatusReserve : 0;
            var titleWidth = _kind == DesignV2LiveOverlayKind.StreamChat
                ? Math.Min(190, Math.Max(80, outer.Width * 0.44f))
                : Math.Min(230, outer.Width * 0.55f);
            DrawText(graphics, model.Title, titleFont, TextPrimary, new RectangleF(outer.Left + 14, header.Top + 10, titleWidth, 18));
            DrawText(
                graphics,
                model.HeaderText ?? model.Status,
                statusFont,
                EvidenceColor(model.Evidence),
                new RectangleF(outer.Left + titleWidth + 24, header.Top + 10, Math.Max(1, outer.Width - titleWidth - 38 - closeButtonSpace), 18),
                ContentAlignment.MiddleRight);
            if (_kind == DesignV2LiveOverlayKind.StreamChat)
            {
                DrawStreamChatCloseButton(graphics, StreamChatCloseButtonBounds(outer));
            }

            headerBottom = header.Bottom;
        }

        var footerReserve = model.ShowFooter ? FooterHeight : 8;
        var bodyTop = model.ShowHeader
            ? headerBottom + BodyGap
            : outer.Top + PaddingSize;
        var body = new RectangleF(
            outer.Left + PaddingSize,
            bodyTop,
            outer.Width - PaddingSize * 2,
            Math.Max(1, outer.Bottom - bodyTop - footerReserve));
        DrawBody(graphics, body, model.Body);

        if (model.ShowFooter)
        {
            using var footerFont = FontOf(9f);
            DrawText(graphics, model.Footer, footerFont, TextMuted, FooterBounds(outer));
        }
    }

    private void DrawBody(Graphics graphics, RectangleF rect, DesignV2Body body)
    {
        switch (body)
        {
            case DesignV2TableBody table:
                DrawTable(graphics, rect, table);
                break;
            case DesignV2MetricRowsBody metrics:
                DrawMetricRows(graphics, rect, metrics.Rows, metrics.MetricSections, metrics.Sections);
                break;
            case DesignV2GraphBody graph:
                DrawGraph(graphics, rect, graph);
                break;
            case DesignV2ChatBody chat:
                DrawChat(graphics, rect, chat.Rows);
                break;
            case DesignV2InputsBody inputs:
                DrawInputs(graphics, rect, inputs);
                break;
            case DesignV2RadarBody radar:
                DrawRadar(graphics, rect, radar);
                break;
        }
    }

    private void DrawTable(Graphics graphics, RectangleF rect, DesignV2TableBody table)
    {
        FillRounded(graphics, rect, 5, SurfaceInset, BorderMuted);
        if (table.Columns.Count == 0)
        {
            return;
        }

        var configuredWidth = table.Columns.Sum(column => Math.Max(MinimumColumnWidth, column.Width));
        var availableWidth = Math.Max(1f, rect.Width);
        var fit = Math.Min(1f, availableWidth / Math.Max(1, configuredWidth));
        var renderedTableWidth = configuredWidth * fit;
        var x = rect.Left;
        using var headerFont = FontOf(7.8f, FontStyle.Bold);
        foreach (var column in table.Columns)
        {
            var width = Math.Max(MinimumColumnWidth, column.Width) * fit;
            DrawText(graphics, column.Label, headerFont, TextMuted, new RectangleF(x, rect.Top + TableHeaderTop, width, TableHeaderHeight), column.Alignment);
            x += width;
        }

        using var rowFont = FontOf(9f);
        using var rowBoldFont = FontOf(9f, FontStyle.Bold);
        using var classHeaderFont = FontOf(8.6f, FontStyle.Bold);
        using var classDetailFont = FontOf(8f, FontStyle.Bold);
        var y = rect.Top + TableBodyTop;
        var drawnRows = 0;
        foreach (var row in table.Rows)
        {
            if (row.IsClassHeader)
            {
                var classHeaderRowRect = new RectangleF(rect.Left, y, renderedTableWidth, TableClassHeaderRowHeight);
                if (classHeaderRowRect.Bottom > rect.Bottom)
                {
                    break;
                }

                var headerRect = new RectangleF(
                    classHeaderRowRect.Left,
                    classHeaderRowRect.Top + TableClassHeaderBandTop,
                    classHeaderRowRect.Width,
                    TableClassHeaderBandHeight);
                if (headerRect.Bottom > rect.Bottom)
                {
                    break;
                }

                var headerFill = TryParseHexColor(row.ClassColorHex, out var headerColor)
                    ? Blend(SurfaceRaised, headerColor, 4, 2)
                    : Blend(SurfaceRaised, Cyan, 5, 1);
                FillRounded(graphics, headerRect, 5, headerFill, Color.FromArgb(90, BorderMuted));
                if (TryParseHexColor(row.ClassColorHex, out var accent))
                {
                    FillRounded(graphics, new RectangleF(headerRect.Left, headerRect.Top, 3, headerRect.Height), 2, accent, null);
                }

                DrawText(
                    graphics,
                    string.IsNullOrWhiteSpace(row.ClassHeaderTitle) ? "Class" : row.ClassHeaderTitle,
                    classHeaderFont,
                    TextPrimary,
                    new RectangleF(headerRect.Left + 10, headerRect.Top + 5, headerRect.Width * 0.58f, 14));
                DrawText(
                    graphics,
                    row.ClassHeaderDetail,
                    classDetailFont,
                    TextSecondary,
                    new RectangleF(headerRect.Left + headerRect.Width * 0.58f, headerRect.Top + 5, headerRect.Width * 0.42f - 10, 14),
                    ContentAlignment.MiddleRight);
                y += classHeaderRowRect.Height + RowGap;
                drawnRows++;
                continue;
            }

            var rowHeight = TableRowHeightFor(row);
            var rowRect = new RectangleF(rect.Left, y, renderedTableWidth, rowHeight);
            if (rowRect.Bottom > rect.Bottom)
            {
                break;
            }

            var fill = row.Evidence == DesignV2Evidence.Unavailable
                ? SurfaceInset
                : TryParseHexColor(row.ClassColorHex, out var rowClassColor)
                    ? Blend(SurfaceRaised, rowClassColor, row.IsReference ? 10 : 12, 1)
                    : row.IsReference
                        ? Blend(SurfaceRaised, Cyan, 10, 1)
                        : SurfaceRaised;
            FillRounded(
                graphics,
                rowRect,
                5,
                fill,
                row.IsReference ? Color.FromArgb(77, Cyan) : Color.FromArgb(90, BorderMuted));
            if (TryParseHexColor(row.ClassColorHex, out var rowAccent))
            {
                FillRounded(graphics, new RectangleF(rowRect.Left, rowRect.Top, 3, rowRect.Height), 2, rowAccent, null);
            }

            x = rowRect.Left;
            var rowTextColor = TableTextColor(row);
            var textTop = rowRect.Top + Math.Max(0f, (rowRect.Height - 16f) / 2f);
            using var separatorPen = new Pen(row.IsReference ? Color.FromArgb(56, Cyan) : Color.FromArgb(23, TextMuted), 1f);
            for (var columnIndex = 0; columnIndex < table.Columns.Count; columnIndex++)
            {
                var column = table.Columns[columnIndex];
                var width = Math.Max(MinimumColumnWidth, column.Width) * fit;
                var value = columnIndex < row.Values.Count ? row.Values[columnIndex] : string.Empty;
                var padding = TableCellHorizontalPadding(width);
                if (columnIndex > 0)
                {
                    graphics.DrawLine(separatorPen, x, rowRect.Top + 4, x, rowRect.Bottom - 4);
                }

                DrawText(
                    graphics,
                    value,
                    row.IsReference || columnIndex == 0 ? rowBoldFont : rowFont,
                    TableCellTextColor(row, columnIndex, rowTextColor),
                    new RectangleF(x + padding, textTop, Math.Max(1f, width - padding * 2f), 16),
                    column.Alignment);
                x += width;
            }

            y += rowHeight + RowGap;
            drawnRows++;
        }
    }

    private static float TableRowHeightFor(DesignV2TableRow row)
    {
        return IsPlaceholderTableRow(row) ? TablePlaceholderRowHeight : TableRowHeight;
    }

    private static bool IsPlaceholderTableRow(DesignV2TableRow row)
    {
        return !row.IsClassHeader
            && row.Evidence == DesignV2Evidence.Unavailable
            && row.Values.All(string.IsNullOrWhiteSpace);
    }

    private static Color TableTextColor(DesignV2TableRow row)
    {
        if (row.Evidence == DesignV2Evidence.Unavailable)
        {
            return TextMuted;
        }

        return row.RelativeLapDelta switch
        {
            >= 2 => MultipleLapsAheadText,
            1 => OneLapAheadText,
            -1 => OneLapBehindText,
            <= -2 => MultipleLapsBehindText,
            _ => TextSecondary
        };
    }

    private static Color TableCellTextColor(DesignV2TableRow row, int columnIndex, Color rowTextColor)
    {
        if (row.CellForegrounds is not null
            && columnIndex >= 0
            && columnIndex < row.CellForegrounds.Count
            && TryParseHexColor(row.CellForegrounds[columnIndex], out var foreground))
        {
            return foreground;
        }

        return row.IsReference ? TextPrimary : rowTextColor;
    }

    private void DrawMetricRows(
        Graphics graphics,
        RectangleF rect,
        IReadOnlyList<DesignV2MetricRow> rows,
        IReadOnlyList<DesignV2MetricSection> metricSections,
        IReadOnlyList<DesignV2MetricGridSection> sections)
    {
        if (rows.Count == 0 && metricSections.Count == 0 && sections.Count == 0)
        {
            FillRounded(graphics, rect, 5, SurfaceInset, BorderMuted);
            using var waitingFont = FontOf(11, FontStyle.Bold);
            DrawText(graphics, "waiting", waitingFont, TextMuted, RectangleF.Inflate(rect, -12, -10));
            return;
        }

        using var labelFont = FontOf(7.8f, FontStyle.Bold);
        using var valueFont = FontOf(9f, FontStyle.Bold);
        using var sectionFont = FontOf(8.8f, FontStyle.Bold);
        var gridHeight = MetricGridHeight(sections);
        var rowsRect = sections.Count == 0
            ? rect
            : new RectangleF(rect.Left, rect.Top, rect.Width, Math.Max(RowHeight, rect.Height - gridHeight - 8f));
        var metricBottom = rowsRect.Top;
        if (metricSections.Count > 0)
        {
            metricBottom = DrawMetricSections(graphics, rowsRect, metricSections, sectionFont, labelFont, valueFont);
        }
        else
        {
            var y = rowsRect.Top;
            foreach (var row in rows)
            {
                var rowHeight = MetricRowHeightFor(row);
                if (y + rowHeight > rowsRect.Bottom)
                {
                    break;
                }

                var rowRect = new RectangleF(rowsRect.Left, y, rowsRect.Width, rowHeight);
                DrawMetricRow(graphics, rowRect, row, labelFont, valueFont);
                metricBottom = Math.Max(metricBottom, rowRect.Bottom);
                y += rowHeight + MetricRowGap;
            }
        }

        if (sections.Count == 0)
        {
            return;
        }

        var sectionTop = Math.Min(rect.Bottom - 72f, metricBottom + 8f);
        foreach (var section in sections)
        {
            if (sectionTop >= rect.Bottom - 34f)
            {
                break;
            }

            var maxRows = Math.Max(1, Math.Min(section.Rows.Count, MaximumMetricGridRows(section, rect.Bottom - sectionTop)));
            var sectionHeight = MetricGridSectionHeight(maxRows);
            var sectionRect = new RectangleF(rect.Left, sectionTop, rect.Width, Math.Min(sectionHeight, rect.Bottom - sectionTop));
            DrawMetricGridSection(graphics, sectionRect, section, maxRows);
            sectionTop += sectionRect.Height + 8f;
        }
    }

    private static float MetricGridHeight(IReadOnlyList<DesignV2MetricGridSection> sections)
    {
        if (sections.Count == 0)
        {
            return 0f;
        }

        return Math.Max(
            80f,
            sections.Sum(section => MetricGridSectionHeight(section.Rows.Count)) + Math.Max(0, sections.Count - 1) * 8f);
    }

    private static int MaximumMetricGridRows(DesignV2MetricGridSection section, float availableHeight)
    {
        if (section.Rows.Count == 0)
        {
            return 0;
        }

        var fixedHeight = MetricGridTitleHeight
            + MetricGridTitleBottomGap
            + MetricGridHeaderHeight
            + MetricGridHeaderBottomGap;
        var availableForRows = Math.Max(0f, availableHeight - fixedHeight + MetricGridRowGap);
        return Math.Max(1, (int)Math.Floor(availableForRows / (MetricGridRowHeight + MetricGridRowGap)));
    }

    private static float MetricGridSectionHeight(int rowCount)
    {
        if (rowCount <= 0)
        {
            return MetricGridTitleHeight;
        }

        return MetricGridTitleHeight
            + MetricGridTitleBottomGap
            + MetricGridHeaderHeight
            + MetricGridHeaderBottomGap
            + rowCount * MetricGridRowHeight
            + Math.Max(0, rowCount - 1) * MetricGridRowGap;
    }

    private float DrawMetricSections(
        Graphics graphics,
        RectangleF rect,
        IReadOnlyList<DesignV2MetricSection> sections,
        Font sectionFont,
        Font labelFont,
        Font valueFont)
    {
        var y = rect.Top;
        var metricBottom = rect.Top;
        foreach (var section in sections.Where(section => section.Rows.Count > 0))
        {
            var firstRowHeight = MetricRowHeightFor(section.Rows[0]);
            if (y + MetricSectionTitleHeight + MetricSectionTitleBottomGap + firstRowHeight > rect.Bottom)
            {
                return metricBottom;
            }

            DrawText(graphics, section.Title.ToUpperInvariant(), sectionFont, TextMuted, new RectangleF(rect.Left + 4, y, rect.Width - 8, MetricSectionTitleHeight), ContentAlignment.MiddleLeft);
            y += MetricSectionTitleHeight + MetricSectionTitleBottomGap;
            foreach (var row in section.Rows)
            {
                var rowHeight = MetricRowHeightFor(row);
                if (y + rowHeight > rect.Bottom)
                {
                    return metricBottom;
                }

                var rowRect = new RectangleF(rect.Left, y, rect.Width, rowHeight);
                DrawMetricRow(graphics, rowRect, row, labelFont, valueFont);
                metricBottom = Math.Max(metricBottom, rowRect.Bottom);
                y += rowHeight + MetricRowGap;
            }

            y += MetricSectionGap - MetricRowGap;
        }

        return metricBottom;
    }

    private void DrawMetricRow(
        Graphics graphics,
        RectangleF rowRect,
        DesignV2MetricRow row,
        Font labelFont,
        Font valueFont)
    {
        var hasAccent = TryParseHexColor(row.RowColorHex, out var accent);
        var fill = hasAccent ? Blend(SurfaceRaised, accent, 12, 1) : SurfaceRaised;
        FillRounded(graphics, rowRect, 5, fill, hasAccent ? WithAlpha(accent, 0.38d) : Color.FromArgb(90, BorderMuted));
        if (hasAccent)
        {
            FillRounded(graphics, new RectangleF(rowRect.Left, rowRect.Top, 3, rowRect.Height), 2, accent, null);
        }

        DrawText(graphics, row.Label, labelFont, TextMuted, new RectangleF(rowRect.Left + 10, rowRect.Top + 6, MetricLabelWidth, 16));
        var valueRect = new RectangleF(rowRect.Left + MetricLabelWidth + 12, rowRect.Top + 4, rowRect.Width - MetricLabelWidth - 18, rowRect.Height - 8);
        if (row.Segments.Count > 0)
        {
            DrawMetricSegments(graphics, valueRect, row.Segments);
            return;
        }

        DrawText(graphics, row.Value, valueFont, EvidenceColor(row.Evidence), new RectangleF(valueRect.Left, rowRect.Top + 6, valueRect.Width - 4, 16), ContentAlignment.MiddleRight);
    }

    private void DrawMetricSegments(
        Graphics graphics,
        RectangleF rect,
        IReadOnlyList<DesignV2MetricSegment> segments)
    {
        var count = Math.Min(6, segments.Count);
        if (count <= 0)
        {
            return;
        }

        using var segmentLabelFont = FontOf(7.2f, FontStyle.Bold);
        using var segmentValueFont = FontOf(9.2f, FontStyle.Bold);
        using var segmentValueSmallFont = FontOf(8.4f, FontStyle.Bold);
        using var segmentValueTinyFont = FontOf(7.6f, FontStyle.Bold);
        var gap = 3f;
        var width = Math.Max(1f, (rect.Width - gap * (count - 1)) / count);
        var x = rect.Left;
        foreach (var segment in segments.Take(count))
        {
            var segmentRect = new RectangleF(x, rect.Top, width, rect.Height);
            var segmentColor = SegmentColor(segment);
            FillRounded(graphics, segmentRect, 3, SegmentBackground(segment), null);
            DrawText(
                graphics,
                segment.Label,
                segmentLabelFont,
                TextMuted,
                new RectangleF(segmentRect.Left + 4, segmentRect.Top + 2, segmentRect.Width - 8, 9),
                ContentAlignment.MiddleCenter);

            var valueRect = new RectangleF(segmentRect.Left + 4, segmentRect.Top + 12, segmentRect.Width - 8, Math.Max(9, segmentRect.Height - 13));
            if (segment.RotationDegrees is { } rotation && IsFinite(rotation))
            {
                var arrowSize = Math.Min(11f, Math.Max(8f, valueRect.Height - 2f));
                var arrowRect = new RectangleF(valueRect.Left, valueRect.Top + (valueRect.Height - arrowSize) / 2f, arrowSize, arrowSize);
                DrawDirectionalArrow(graphics, arrowRect, segmentColor, rotation);
                valueRect = new RectangleF(valueRect.Left + arrowSize + 2f, valueRect.Top, Math.Max(1f, valueRect.Width - arrowSize - 2f), valueRect.Height);
            }

            DrawText(
                graphics,
                segment.Value,
                SegmentValueFontFor(graphics, segment.Value, segmentRect.Width - 8, segmentValueFont, segmentValueSmallFont, segmentValueTinyFont),
                segmentColor,
                valueRect,
                ContentAlignment.MiddleCenter);
            x += width + gap;
        }
    }

    private static Color SegmentColor(DesignV2MetricSegment segment)
    {
        return TryParseHexColor(segment.AccentHex, out var accent)
            ? accent
            : EvidenceColor(segment.Evidence);
    }

    private static Color SegmentBackground(DesignV2MetricSegment segment)
    {
        return TryParseHexColor(segment.AccentHex, out var accent)
            ? Blend(SurfaceRaised, accent, 8, 1)
            : EvidenceBackground(segment.Evidence);
    }

    private static void DrawDirectionalArrow(Graphics graphics, RectangleF rect, Color color, double rotationDegrees)
    {
        var state = graphics.Save();
        try
        {
            graphics.TranslateTransform(rect.Left + rect.Width / 2f, rect.Top + rect.Height / 2f);
            graphics.RotateTransform((float)rotationDegrees);
            var half = rect.Width / 2f;
            using var path = new GraphicsPath();
            path.AddPolygon(
            [
                new PointF(0f, -half),
                new PointF(half * 0.72f, half * 0.28f),
                new PointF(half * 0.22f, half * 0.18f),
                new PointF(half * 0.22f, half),
                new PointF(-half * 0.22f, half),
                new PointF(-half * 0.22f, half * 0.18f),
                new PointF(-half * 0.72f, half * 0.28f)
            ]);
            using var brush = new SolidBrush(color);
            graphics.FillPath(brush, path);
        }
        finally
        {
            graphics.Restore(state);
        }
    }

    private static Font SegmentValueFontFor(
        Graphics graphics,
        string value,
        float availableWidth,
        Font normalFont,
        Font smallFont,
        Font tinyFont)
    {
        if (string.IsNullOrWhiteSpace(value) || availableWidth <= 1f)
        {
            return normalFont;
        }

        if (graphics.MeasureString(value, normalFont).Width <= availableWidth)
        {
            return normalFont;
        }

        return graphics.MeasureString(value, smallFont).Width <= availableWidth
            ? smallFont
            : tinyFont;
    }

    private void DrawMetricGridSection(
        Graphics graphics,
        RectangleF rect,
        DesignV2MetricGridSection section,
        int maximumRows)
    {
        using var titleFont = FontOf(8.8f, FontStyle.Bold);
        using var headerFont = FontOf(8.6f, FontStyle.Bold);
        using var cellFont = FontOf(9.2f, FontStyle.Bold);

        DrawText(graphics, section.Title.ToUpperInvariant(), titleFont, TextMuted, new RectangleF(rect.Left + 4, rect.Top, rect.Width - 8, MetricGridTitleHeight));
        var headers = section.Headers.Count == 0
            ? new[] { "Info", "FL", "FR", "RL", "RR" }
            : section.Headers.Take(5).ToArray();
        var columns = headers.Length;
        var gap = 3f;
        var usableWidth = rect.Width - MetricGridCellInset * 2f - Math.Max(0, columns - 1) * gap;
        var columnWidth = Math.Max(42f, usableWidth / Math.Max(1, columns));
        var widths = Enumerable.Range(0, columns)
            .Select(_ => columnWidth)
            .ToArray();
        var y = rect.Top + MetricGridTitleHeight + MetricGridTitleBottomGap;
        var x = rect.Left + MetricGridCellInset;
        for (var index = 0; index < columns; index++)
        {
            var headerRect = new RectangleF(x, y, widths[index], MetricGridHeaderHeight);
            DrawText(
                graphics,
                headers[index],
                headerFont,
                TextMuted,
                headerRect,
                index == 0 ? ContentAlignment.MiddleLeft : ContentAlignment.MiddleRight);
            x += widths[index] + gap;
        }

        y += MetricGridHeaderHeight + MetricGridHeaderBottomGap;
        foreach (var row in section.Rows.Take(maximumRows))
        {
            if (y + MetricGridRowHeight > rect.Bottom)
            {
                break;
            }

            var rowRect = new RectangleF(rect.Left, y, rect.Width, MetricGridRowHeight);
            FillRounded(graphics, rowRect, 5, SurfaceRaised, BorderMuted);

            var cellTop = y + MetricGridCellInset;
            x = rect.Left + MetricGridCellInset;
            var labelRect = new RectangleF(x, cellTop, widths[0], MetricGridCellHeight);
            FillRounded(graphics, labelRect, 3, SurfaceRaised, null);
            DrawText(graphics, row.Label, cellFont, TextSecondary, RectangleF.Inflate(labelRect, -5, -3), ContentAlignment.MiddleLeft);
            x += widths[0] + gap;

            for (var index = 1; index < columns; index++)
            {
                var cell = index - 1 < row.Cells.Count ? row.Cells[index - 1] : new DesignV2MetricGridCell("--", DesignV2Evidence.Unavailable);
                var cellRect = new RectangleF(x, cellTop, widths[index], MetricGridCellHeight);
                FillRounded(graphics, cellRect, 3, EvidenceBackground(cell.Evidence), null);
                DrawText(graphics, cell.Value, cellFont, EvidenceColor(cell.Evidence), RectangleF.Inflate(cellRect, -5, -3), ContentAlignment.MiddleRight);
                x += widths[index] + gap;
            }

            y += MetricGridRowHeight + MetricGridRowGap;
        }
    }

    private void DrawGraph(Graphics graphics, RectangleF rect, DesignV2GraphBody graph)
    {
        FillRounded(graphics, rect, 5, SurfaceInset, BorderMuted);
        var totalSeriesPoints = graph.Series.Sum(series => series.Points.Count);
        if (totalSeriesPoints < 2 && graph.Points.Count < 2)
        {
            using var waitingFont = FontOf(11, FontStyle.Bold);
            DrawText(graphics, "waiting for trend", waitingFont, TextMuted, RectangleF.Inflate(rect, -12, -10));
            return;
        }

        var frame = RectangleF.Inflate(rect, -12, -14);
        const float axisWidth = 58f;
        const float xAxisHeight = 17f;
        var plotHeight = Math.Max(40, frame.Height - xAxisHeight);
        var metricsTableWidth = FocusedGapMetricsTableWidth(frame);
        var metricsTableRect = metricsTableWidth > 0f
            ? new RectangleF(frame.Right - metricsTableWidth, frame.Top, metricsTableWidth, plotHeight)
            : RectangleF.Empty;
        var chartRight = metricsTableWidth > 0f
            ? metricsTableRect.Left - GapMetricsTableGap
            : frame.Right;
        var labelLane = new RectangleF(
            chartRight - GapEndpointLabelLaneWidth,
            frame.Top,
            GapEndpointLabelLaneWidth,
            plotHeight);
        var plot = new RectangleF(
            frame.Left + axisWidth,
            frame.Top,
            Math.Max(40, labelLane.Left - (frame.Left + axisWidth)),
            plotHeight);
        var axisBounds = new RectangleF(frame.Left, frame.Top, axisWidth - 8, plot.Height);
        var scale = graph.Scale ?? DesignV2GapScale.Leader(graph.MaxGapSeconds ?? 1d);
        DrawGapWeatherBands(graphics, graph, plot);
        DrawGapLapIntervalLines(graphics, graph, plot);
        DrawGapGridLines(graphics, graph, scale, plot, axisBounds);
        using var axisFont = FontOf(9.5f);
        var max = Math.Max(1d, scale.MaxGapSeconds);
        DrawText(graphics, FormatTrendWindow(TimeSpan.FromSeconds(graph.EndSeconds - graph.StartSeconds)), axisFont, TextMuted, new RectangleF(plot.Left, plot.Bottom + 3, 56, 14));
        DrawText(graphics, "now", axisFont, TextMuted, new RectangleF(plot.Right - 44, plot.Bottom + 3, 44, 14), ContentAlignment.MiddleRight);
        DrawGapLeaderChangeMarkers(graphics, graph, plot);

        if (graph.Series.Count > 0)
        {
            var endpointLabels = new List<DesignV2GapEndpointLabel>();
            foreach (var (series, index) in graph.Series
                .Select((series, index) => (series, index))
                .OrderBy(item => GapDrawPriority(item.series, graph.ThreatCarIdx)))
            {
                if (scale.IsFocusRelative && series.IsClassLeader)
                {
                    continue;
                }

                if (DrawGapSeries(graphics, plot, graph, series, index, max) is { } label)
                {
                    endpointLabels.Add(label);
                }
            }

            if (graph.ActiveThreat is not null)
            {
                DrawGapThreatAnnotation(graphics, graph.ActiveThreat, plot);
            }

            DrawGapEndpointLabels(graphics, endpointLabels, plot, labelLane);
            DrawGapDriverChangeMarkers(graphics, graph, plot, max);
            DrawGapScaleLabels(graphics, scale, plot, axisBounds);
            if (metricsTableWidth > 0f)
            {
                DrawGapFocusedMetricsTable(graphics, metricsTableRect, graph);
            }

            return;
        }

        using var linePen = new Pen(Cyan, 2f);
        using var path = new GraphicsPath();
        for (var index = 0; index < graph.Points.Count; index++)
        {
            var progress = index / (float)Math.Max(1, graph.Points.Count - 1);
            var normalized = (float)Math.Clamp(graph.Points[index] / max, 0d, 1d);
            var point = new PointF(plot.Left + progress * plot.Width, plot.Top + normalized * plot.Height);
            if (index == 0)
            {
                path.StartFigure();
                path.AddLine(point, point);
            }
            else
            {
                path.AddLine(path.GetLastPoint(), point);
            }
        }

        DrawGapScaleLabels(graphics, scale, plot, axisBounds);
        graphics.DrawPath(linePen, path);
    }

    private static float FocusedGapMetricsTableWidth(RectangleF frame)
    {
        if (frame.Height < GapMetricsMinimumTableHeight)
        {
            return 0f;
        }

        var availableAfterTable = frame.Width
            - 58f
            - GapEndpointLabelLaneWidth
            - GapMetricsTableGap
            - GapMetricsTableWidth;
        return availableAfterTable >= GapMetricsMinimumPlotWidth ? GapMetricsTableWidth : 0f;
    }

    private void DrawGapThreatAnnotation(Graphics graphics, DesignV2GapTrendMetric metric, RectangleF plot)
    {
        if (metric.Chaser is not { } chaser)
        {
            return;
        }

        var text = $"Threat {chaser.Label} {FormatFocusedTrendChangeSeconds(-chaser.GainSeconds)} {metric.Label}";
        using var font = FontOf(8.5f, FontStyle.Bold);
        var size = graphics.MeasureString(text, font);
        var x = Math.Min(Math.Max(plot.Left + 2f, plot.Left + plot.Width / 2f - size.Width / 2f), plot.Right - size.Width - 8f);
        var y = plot.Bottom - GapThreatBadgeHeight - 6f;
        var badge = new RectangleF(x - 4f, y - 1f, size.Width + 8f, GapThreatBadgeHeight);
        FillRounded(graphics, badge, 3f, Color.FromArgb(214, 18, 24, 28), Color.FromArgb(97, Error));
        DrawText(graphics, text, font, Error, new RectangleF(x, y, size.Width + 2f, GapThreatBadgeHeight - 1f));
    }

    private static RectangleF GapMetricLabelBounds(RectangleF rect, float y, float height)
    {
        return new RectangleF(rect.Left + 8f, y, 44f, height);
    }

    private static RectangleF GapMetricValueBounds(RectangleF rect, float y, float height)
    {
        return new RectangleF(rect.Left + 56f, y, 46f, height);
    }

    private static RectangleF GapMetricThreatBounds(RectangleF rect, float y, float height)
    {
        return new RectangleF(rect.Left + 108f, y, Math.Max(1f, rect.Width - 114f), height);
    }

    private void DrawGapFocusedMetricsTable(Graphics graphics, RectangleF rect, DesignV2GraphBody graph)
    {
        FillRounded(graphics, rect, 3f, Color.FromArgb(188, 18, 24, 28), Color.FromArgb(38, TextPrimary));
        using var titleFont = FontOf(10f, FontStyle.Bold);
        using var headerFont = FontOf(8f);
        var rowHeight = Math.Max(9.5f, Math.Min(26f, (rect.Height - 8f - 38f) / Math.Max(1, graph.TrendMetrics.Count)));
        var rowTextHeight = Math.Max(10f, Math.Min(14f, rowHeight));
        using var rowFont = FontOf(rowHeight < 16f ? 8f : 9f);
        DrawText(graphics, "Trend", titleFont, TextPrimary, new RectangleF(rect.Left + 8f, rect.Top + 4f, rect.Width - 16f, 14f));
        DrawText(graphics, "Metric", headerFont, TextMuted, GapMetricLabelBounds(rect, rect.Top + 20f, 12f));
        DrawText(graphics, string.IsNullOrWhiteSpace(graph.ComparisonLabel) ? "--" : graph.ComparisonLabel, headerFont, TextMuted, GapMetricValueBounds(rect, rect.Top + 20f, 12f));
        DrawText(graphics, "Threat", headerFont, TextMuted, GapMetricThreatBounds(rect, rect.Top + 20f, 12f));

        for (var index = 0; index < graph.TrendMetrics.Count; index++)
        {
            var metric = graph.TrendMetrics[index];
            var y = rect.Top + 38f + index * rowHeight;
            DrawText(graphics, metric.Label, rowFont, TextSecondary, GapMetricLabelBounds(rect, y, rowTextHeight));
            DrawText(
                graphics,
                GapMetricValueText(metric),
                rowFont,
                GapMetricValueColor(metric, graph.MetricDeadbandSeconds),
                GapMetricValueBounds(rect, y, rowTextHeight));
            DrawText(
                graphics,
                GapMetricChaserText(metric),
                rowFont,
                GapMetricChaserColor(metric),
                GapMetricThreatBounds(rect, y, rowTextHeight));
        }
    }

    private static string GapMetricValueText(DesignV2GapTrendMetric metric)
    {
        if (string.Equals(metric.State, "pit", StringComparison.Ordinal))
        {
            return GapPitSecondsText(metric.ComparisonPit);
        }

        if (string.Equals(metric.State, "pitLap", StringComparison.Ordinal))
        {
            return GapPitLapText(metric.ComparisonPit);
        }

        if (string.Equals(metric.State, "tire", StringComparison.Ordinal))
        {
            return GapTireText(metric.ComparisonTire);
        }

        if (string.Equals(metric.State, "stint", StringComparison.Ordinal)
            || string.Equals(metric.State, "last", StringComparison.Ordinal)
            || string.Equals(metric.State, "status", StringComparison.Ordinal))
        {
            return string.IsNullOrWhiteSpace(metric.ComparisonText) ? "--" : metric.ComparisonText;
        }

        return metric.State switch
        {
            "ready" when metric.FocusGapChangeSeconds is { } value => FormatFocusedTrendChangeSeconds(-value),
            "ready" when !string.IsNullOrWhiteSpace(metric.StateLabel) => metric.StateLabel,
            "warming" => string.IsNullOrWhiteSpace(metric.StateLabel) ? "--" : metric.StateLabel,
            "leaderChanged" => "leader",
            _ => "--"
        };
    }

    private static Color GapMetricValueColor(DesignV2GapTrendMetric metric, double deadbandSeconds)
    {
        if (string.Equals(metric.State, "pit", StringComparison.Ordinal))
        {
            return GapPitSecondsComparisonColor(metric.PrimaryPit, metric.ComparisonPit);
        }

        if (string.Equals(metric.State, "pitLap", StringComparison.Ordinal))
        {
            return GapPitLapComparisonColor(metric.PrimaryPit, metric.ComparisonPit);
        }

        if (string.Equals(metric.State, "tire", StringComparison.Ordinal))
        {
            return GapTireComparisonColor(metric.PrimaryTire, metric.ComparisonTire);
        }

        if (string.Equals(metric.State, "last", StringComparison.Ordinal))
        {
            return GapLastLapComparisonColor(metric.PrimaryText, metric.ComparisonText);
        }

        if (string.Equals(metric.State, "stint", StringComparison.Ordinal)
            || string.Equals(metric.State, "status", StringComparison.Ordinal))
        {
            return GapNeutralMetricColor(metric.ComparisonText);
        }

        if (!string.Equals(metric.State, "ready", StringComparison.Ordinal) || metric.FocusGapChangeSeconds is not { } value)
        {
            return string.Equals(metric.State, "warming", StringComparison.Ordinal)
                || string.Equals(metric.State, "leaderChanged", StringComparison.Ordinal)
                ? TextMuted
                : Color.FromArgb(184, TextMuted);
        }

        if (Math.Abs(value) < deadbandSeconds)
        {
            return TextSecondary;
        }

        return value > 0d ? Error : Green;
    }

    private static string GapMetricChaserText(DesignV2GapTrendMetric metric)
    {
        if (string.Equals(metric.State, "pit", StringComparison.Ordinal))
        {
            return GapPitSecondsText(metric.ThreatPit);
        }

        if (string.Equals(metric.State, "pitLap", StringComparison.Ordinal))
        {
            return GapPitLapText(metric.ThreatPit);
        }

        if (string.Equals(metric.State, "tire", StringComparison.Ordinal))
        {
            return GapTireText(metric.ThreatTire);
        }

        if (string.Equals(metric.State, "stint", StringComparison.Ordinal)
            || string.Equals(metric.State, "last", StringComparison.Ordinal)
            || string.Equals(metric.State, "status", StringComparison.Ordinal))
        {
            return string.IsNullOrWhiteSpace(metric.ThreatText) ? "--" : metric.ThreatText;
        }

        return metric.State switch
        {
            "ready" when metric.Chaser is { } chaser => $"{chaser.Label} {FormatFocusedTrendChangeSeconds(-chaser.GainSeconds)}",
            "leaderChanged" => "reset",
            _ => "--"
        };
    }

    private static Color GapMetricChaserColor(DesignV2GapTrendMetric metric)
    {
        if (string.Equals(metric.State, "pit", StringComparison.Ordinal))
        {
            return GapPitSecondsComparisonColor(metric.PrimaryPit, metric.ThreatPit);
        }

        if (string.Equals(metric.State, "pitLap", StringComparison.Ordinal))
        {
            return GapPitLapComparisonColor(metric.PrimaryPit, metric.ThreatPit);
        }

        if (string.Equals(metric.State, "tire", StringComparison.Ordinal))
        {
            return GapTireComparisonColor(metric.PrimaryTire, metric.ThreatTire);
        }

        if (string.Equals(metric.State, "last", StringComparison.Ordinal))
        {
            return GapLastLapComparisonColor(metric.PrimaryText, metric.ThreatText);
        }

        if (string.Equals(metric.State, "stint", StringComparison.Ordinal)
            || string.Equals(metric.State, "status", StringComparison.Ordinal))
        {
            return GapNeutralMetricColor(metric.ThreatText);
        }

        return string.Equals(metric.State, "ready", StringComparison.Ordinal) && metric.Chaser is not null
            ? Error
            : Color.FromArgb(184, TextMuted);
    }

    private static string GapPitSecondsText(DesignV2PitMetricValue? pit)
    {
        if (pit?.Seconds is not { } seconds || !IsFinite(seconds))
        {
            return "--";
        }

        return seconds >= 60d
            ? $"{Math.Floor(seconds / 60d):0}:{seconds % 60d:00}"
            : $"{seconds:0}s";
    }

    private static string GapPitLapText(DesignV2PitMetricValue? pit)
    {
        return pit?.Lap is { } lap && lap > 0
            ? $"L{lap}"
            : "--";
    }

    private static Color GapPitSecondsComparisonColor(DesignV2PitMetricValue? focusPit, DesignV2PitMetricValue? comparisonPit)
    {
        if (comparisonPit?.Seconds is not { } comparisonSeconds || !IsFinite(comparisonSeconds))
        {
            return Color.FromArgb(184, TextMuted);
        }

        if (focusPit?.Seconds is not { } focusSeconds || !IsFinite(focusSeconds))
        {
            return TextSecondary;
        }

        var delta = focusSeconds - comparisonSeconds;
        if (Math.Abs(delta) <= 1d)
        {
            return TextSecondary;
        }

        return delta < 0d ? Green : Error;
    }

    private static Color GapPitLapComparisonColor(DesignV2PitMetricValue? focusPit, DesignV2PitMetricValue? comparisonPit)
    {
        if (comparisonPit?.Lap is not { } comparisonLap || comparisonLap <= 0)
        {
            return Color.FromArgb(184, TextMuted);
        }

        if (focusPit?.Lap is not { } focusLap || focusLap <= 0 || focusLap == comparisonLap)
        {
            return TextSecondary;
        }

        return focusLap < comparisonLap ? Green : Error;
    }

    private static string GapTireText(DesignV2TireMetricValue? tire)
    {
        return !string.IsNullOrWhiteSpace(tire?.ShortLabel)
            ? tire.ShortLabel!
            : !string.IsNullOrWhiteSpace(tire?.Label)
                ? tire.Label!
                : "--";
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static Color GapTireComparisonColor(DesignV2TireMetricValue? focusTire, DesignV2TireMetricValue? comparisonTire)
    {
        var comparison = FirstNonEmpty(comparisonTire?.ShortLabel, comparisonTire?.Label);
        if (string.IsNullOrWhiteSpace(comparison))
        {
            return Color.FromArgb(184, TextMuted);
        }

        var focus = FirstNonEmpty(focusTire?.ShortLabel, focusTire?.Label);
        if (string.IsNullOrWhiteSpace(focus)
            || string.Equals(focus, comparison, StringComparison.OrdinalIgnoreCase))
        {
            return comparisonTire?.IsWet == true ? Cyan : TextSecondary;
        }

        return comparisonTire?.IsWet == true ? Cyan : Amber;
    }

    private static Color GapLastLapComparisonColor(string? focusText, string? comparisonText)
    {
        var focus = LapTimeTextToSeconds(focusText);
        var comparison = LapTimeTextToSeconds(comparisonText);
        if (!comparison.HasValue)
        {
            return Color.FromArgb(184, TextMuted);
        }

        if (!focus.HasValue)
        {
            return TextSecondary;
        }

        var delta = comparison.Value - focus.Value;
        if (Math.Abs(delta) <= 0.05d)
        {
            return TextSecondary;
        }

        return delta < 0d ? Error : Green;
    }

    private static Color GapNeutralMetricColor(string? value)
    {
        return string.IsNullOrWhiteSpace(value) || string.Equals(value, "--", StringComparison.Ordinal)
            ? Color.FromArgb(184, TextMuted)
            : TextSecondary;
    }

    private static double? LapTimeTextToSeconds(string? value)
    {
        var text = value?.Trim();
        if (string.IsNullOrWhiteSpace(text) || string.Equals(text, "--", StringComparison.Ordinal))
        {
            return null;
        }

        var parts = text.Split(':', 2);
        if (parts.Length == 2
            && double.TryParse(parts[0], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var minutes)
            && double.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var seconds))
        {
            return minutes * 60d + seconds;
        }

        return double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var plainSeconds)
            ? plainSeconds
            : null;
    }

    private void DrawGapEndpointLabels(
        Graphics graphics,
        IReadOnlyList<DesignV2GapEndpointLabel> labels,
        RectangleF plot,
        RectangleF labelLane)
    {
        if (labels.Count == 0)
        {
            return;
        }

        var pinnedLabels = labels.Where(label => ShouldPinGapEndpointLabel(label, plot)).ToArray();
        var floatingLabels = labels.Where(label => !ShouldPinGapEndpointLabel(label, plot)).ToArray();
        foreach (var label in floatingLabels.OrderBy(GapEndpointLabelDrawPriority).ThenBy(label => label.Point.Y))
        {
            var y = ClampGapEndpointLabelY(label.Point.Y - GapEndpointLabelHeight / 2f, plot);
            DrawGapEndpointLabel(graphics, label, y, plot, plot, false);
        }

        DrawPinnedGapEndpointLabels(graphics, pinnedLabels, plot, labelLane);
    }

    private void DrawPinnedGapEndpointLabels(
        Graphics graphics,
        IReadOnlyList<DesignV2GapEndpointLabel> labels,
        RectangleF plot,
        RectangleF labelLane)
    {
        if (labels.Count == 0)
        {
            return;
        }

        var positioned = labels
            .OrderBy(label => label.Point.Y)
            .ThenBy(GapEndpointLabelDrawPriority)
            .Select(label => new DesignV2PositionedGapEndpointLabel(
                label,
                ClampGapEndpointLabelY(label.Point.Y - GapEndpointLabelHeight / 2f, labelLane)))
            .ToArray();

        for (var index = 1; index < positioned.Length; index++)
        {
            var minimumY = positioned[index - 1].Y + GapEndpointLabelHeight + GapEndpointLabelGap;
            if (positioned[index].Y < minimumY)
            {
                positioned[index] = positioned[index] with { Y = minimumY };
            }
        }

        var maxY = labelLane.Bottom - GapEndpointLabelHeight - 1f;
        if (positioned.Length > 0 && positioned[^1].Y > maxY)
        {
            var shift = positioned[^1].Y - maxY;
            for (var index = 0; index < positioned.Length; index++)
            {
                positioned[index] = positioned[index] with
                {
                    Y = Math.Max(labelLane.Top + 1f, positioned[index].Y - shift)
                };
            }
        }

        foreach (var item in positioned.OrderBy(item => GapEndpointLabelDrawPriority(item.Label)))
        {
            DrawGapEndpointLabel(graphics, item.Label, item.Y, plot, labelLane, true);
        }
    }

    private void DrawGapEndpointLabel(
        Graphics graphics,
        DesignV2GapEndpointLabel label,
        float y,
        RectangleF plot,
        RectangleF labelBounds,
        bool pinnedToLane)
    {
        using var font = FontOf(label.IsReference ? 10f : 9f);
        var size = graphics.MeasureString(label.Text, font);
        var x = pinnedToLane
            ? Math.Min(labelBounds.Right - size.Width - 1f, Math.Max(labelBounds.Left + 4f, label.Point.X + 8f))
            : Math.Min(labelBounds.Right - size.Width - 2f, label.Point.X + 6f);
        var background = new RectangleF(x - 2f, y, size.Width + 4f, GapEndpointLabelHeight);
        if (pinnedToLane || Math.Abs(y + GapEndpointLabelHeight / 2f - label.Point.Y) > 3f)
        {
            using var connector = new Pen(Color.FromArgb(82, label.Color), 1f);
            graphics.DrawLine(connector, label.Point.X + 3f, label.Point.Y, background.Left, y + GapEndpointLabelHeight / 2f);
        }

        using var backgroundBrush = new SolidBrush(Color.FromArgb(label.IsReference ? 188 : 150, 18, 30, 42));
        graphics.FillRectangle(backgroundBrush, background);
        DrawText(
            graphics,
            label.Text,
            font,
            WithAlpha(label.Color, label.IsReference ? 1d : 0.78d),
            new RectangleF(x, y - 1f, size.Width + 2f, GapEndpointLabelHeight + 2f));
    }

    private static bool ShouldPinGapEndpointLabel(DesignV2GapEndpointLabel label, RectangleF plot)
    {
        return label.Point.X >= plot.Right - GapEndpointLabelPinThreshold;
    }

    private static float ClampGapEndpointLabelY(float y, RectangleF bounds)
    {
        return Math.Min(Math.Max(y, bounds.Top + 1f), bounds.Bottom - GapEndpointLabelHeight - 1f);
    }

    private static int GapEndpointLabelDrawPriority(DesignV2GapEndpointLabel label)
    {
        if (label.IsReference)
        {
            return 2;
        }

        return label.IsClassLeader ? 1 : 0;
    }

    private DesignV2GapEndpointLabel? DrawGapSeries(
        Graphics graphics,
        RectangleF plot,
        DesignV2GraphBody graph,
        DesignV2GapSeries series,
        int seriesIndex,
        double maxGapSeconds)
    {
        var color = WithAlpha(
            GapSeriesColor(series, seriesIndex, graph.ThreatCarIdx),
            series.Alpha * GapSeriesAlphaMultiplier(series, graph.ThreatCarIdx));
        using var pen = new Pen(color, series.IsReference ? 2.6f : series.IsClassLeader ? 1.8f : 1.25f)
        {
            LineJoin = LineJoin.Round,
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        if (series.IsStale || series.IsStickyExit)
        {
            pen.DashStyle = DashStyle.Dash;
        }

        using var path = new GraphicsPath();
        var hasPoint = false;
        foreach (var point in series.Points.OrderBy(point => point.AxisSeconds))
        {
            var graphPoint = GapGraphPoint(point, graph, plot, maxGapSeconds);
            if (!hasPoint || point.StartsSegment)
            {
                path.StartFigure();
                path.AddLine(graphPoint, graphPoint);
                hasPoint = true;
                continue;
            }

            path.AddLine(path.GetLastPoint(), graphPoint);
        }

        graphics.DrawPath(pen, path);
        var latest = series.Points.OrderBy(point => point.AxisSeconds).LastOrDefault();
        if (latest is null)
        {
            return null;
        }

        var latestPoint = GapGraphPoint(latest, graph, plot, maxGapSeconds);
        var label = series.IsClassLeader
            ? "P1"
            : series.ClassPosition is { } position
                ? $"P{position}"
                : $"#{series.CarIdx}";
        if (series.IsStale)
        {
            using var markerPen = new Pen(color, 1.2f);
            graphics.DrawLine(markerPen, latestPoint.X - 4f, latestPoint.Y - 4f, latestPoint.X + 4f, latestPoint.Y + 4f);
            graphics.DrawLine(markerPen, latestPoint.X - 4f, latestPoint.Y + 4f, latestPoint.X + 4f, latestPoint.Y - 4f);
        }

        return new DesignV2GapEndpointLabel(label, latestPoint, color, series.IsReference, series.IsClassLeader);
    }

    private void DrawGapGridLines(
        Graphics graphics,
        DesignV2GraphBody graph,
        DesignV2GapScale scale,
        RectangleF plot,
        RectangleF axisBounds)
    {
        using var axisPen = new Pen(Color.FromArgb(70, TextMuted), 1);
        graphics.DrawLine(axisPen, plot.Left, plot.Top, plot.Right, plot.Top);
        graphics.DrawLine(axisPen, plot.Left, plot.Bottom, plot.Right, plot.Bottom);

        if (scale.IsFocusRelative)
        {
            DrawGapFocusGridLines(graphics, scale, plot, axisBounds);
            return;
        }

        using var gridPen = new Pen(Color.FromArgb(46, TextMuted), 1);
        using var gridFont = FontOf(7.5f);
        var step = NiceGridStep(scale.MaxGapSeconds / 4d);
        for (var value = step; value < scale.MaxGapSeconds; value += step)
        {
            var y = GapToY(value, scale.MaxGapSeconds, plot);
            graphics.DrawLine(gridPen, plot.Left, y, plot.Right, y);
            DrawText(
                graphics,
                FormatAxisSeconds(value),
                gridFont,
                TextMuted,
                new RectangleF(axisBounds.Left, y - 8f, axisBounds.Width, 16f),
                ContentAlignment.MiddleRight);
        }

        if (graph.LapReferenceSeconds is not { } lapSeconds || lapSeconds < 20d || scale.MaxGapSeconds < lapSeconds * 0.85d)
        {
            return;
        }

        using var lapPen = new Pen(Color.FromArgb(130, TextPrimary), 1.2f);
        for (var lap = 1; lap * lapSeconds < scale.MaxGapSeconds; lap++)
        {
            var y = GapToY(lap * lapSeconds, scale.MaxGapSeconds, plot);
            graphics.DrawLine(lapPen, plot.Left, y, plot.Right, y);
            DrawText(
                graphics,
                $"+{lap} lap",
                gridFont,
                TextPrimary,
                new RectangleF(axisBounds.Left, y - 8f, axisBounds.Width, 16f),
                ContentAlignment.MiddleRight);
        }
    }

    private void DrawGapFocusGridLines(Graphics graphics, DesignV2GapScale scale, RectangleF plot, RectangleF axisBounds)
    {
        using var gridPen = new Pen(Color.FromArgb(46, TextMuted), 1);
        using var referencePen = new Pen(Color.FromArgb(110, Green), 1.2f);
        using var gridFont = FontOf(7.5f);
        var referenceY = GapFocusReferenceY(plot);
        graphics.DrawLine(referencePen, plot.Left, referenceY, plot.Right, referenceY);
        DrawText(
            graphics,
            "focus",
            gridFont,
            Green,
            new RectangleF(axisBounds.Left, referenceY - 8f, axisBounds.Width, 16f),
            ContentAlignment.MiddleRight);

        var aheadStep = NiceGridStep(scale.AheadSeconds / 2d);
        for (var value = aheadStep; value < scale.AheadSeconds; value += aheadStep)
        {
            var y = GapDeltaToY(-value, scale, plot);
            graphics.DrawLine(gridPen, plot.Left, y, plot.Right, y);
            DrawText(
                graphics,
                FormatDeltaSeconds(-value),
                gridFont,
                TextMuted,
                new RectangleF(axisBounds.Left, y - 8f, axisBounds.Width, 16f),
                ContentAlignment.MiddleRight);
        }

        var behindStep = NiceGridStep(scale.BehindSeconds / 2d);
        for (var value = behindStep; value < scale.BehindSeconds; value += behindStep)
        {
            var y = GapDeltaToY(value, scale, plot);
            graphics.DrawLine(gridPen, plot.Left, y, plot.Right, y);
            DrawText(
                graphics,
                FormatDeltaSeconds(value),
                gridFont,
                TextMuted,
                new RectangleF(axisBounds.Left, y - 8f, axisBounds.Width, 16f),
                ContentAlignment.MiddleRight);
        }
    }

    private void DrawGapLapIntervalLines(Graphics graphics, DesignV2GraphBody graph, RectangleF plot)
    {
        if (graph.LapReferenceSeconds is not { } lapSeconds || lapSeconds < 20d)
        {
            return;
        }

        var intervalSeconds = lapSeconds * 5d;
        var durationSeconds = graph.EndSeconds - graph.StartSeconds;
        if (durationSeconds < intervalSeconds * 0.75d)
        {
            return;
        }

        using var linePen = new Pen(Color.FromArgb(34, TextPrimary), 1f);
        using var labelFont = FontOf(7f);
        for (var elapsed = intervalSeconds; elapsed < durationSeconds; elapsed += intervalSeconds)
        {
            var x = plot.Left + (float)(elapsed / durationSeconds * plot.Width);
            graphics.DrawLine(linePen, x, plot.Top, x, plot.Bottom);
            DrawText(
                graphics,
                $"{elapsed / lapSeconds:0}L",
                labelFont,
                TextMuted,
                new RectangleF(x - 18f, plot.Bottom + 1f, 36f, 12f),
                ContentAlignment.MiddleCenter);
        }
    }

    private void DrawGapScaleLabels(Graphics graphics, DesignV2GapScale scale, RectangleF plot, RectangleF axisBounds)
    {
        using var font = FontOf(9.5f);
        DrawText(graphics, scale.IsFocusRelative ? "local" : "leader", font, TextMuted, new RectangleF(axisBounds.Left, plot.Top - 7, axisBounds.Width, 14), ContentAlignment.MiddleRight);
        if (scale.IsFocusRelative)
        {
            DrawText(graphics, FormatDeltaSeconds(-scale.AheadSeconds), font, TextMuted, new RectangleF(axisBounds.Left, plot.Top + GapFocusScaleTopPadding - 8f, axisBounds.Width, 16f), ContentAlignment.MiddleRight);
            DrawText(graphics, FormatDeltaSeconds(scale.BehindSeconds), font, TextMuted, new RectangleF(axisBounds.Left, plot.Bottom - GapFocusScaleBottomPadding - 8f, axisBounds.Width, 16f), ContentAlignment.MiddleRight);
            return;
        }

        DrawText(graphics, FormatAxisSeconds(scale.MaxGapSeconds), font, TextMuted, new RectangleF(axisBounds.Left, plot.Bottom - 7, axisBounds.Width, 14), ContentAlignment.MiddleRight);
    }

    private void DrawGapLeaderChangeMarkers(Graphics graphics, DesignV2GraphBody graph, RectangleF plot)
    {
        if (graph.LeaderChanges.Count == 0)
        {
            return;
        }

        var domain = Math.Max(1d, graph.EndSeconds - graph.StartSeconds);
        using var pen = new Pen(Color.FromArgb(115, TextPrimary), 1f)
        {
            DashStyle = DashStyle.Dot
        };
        using var font = FontOf(7.5f, FontStyle.Bold);
        foreach (var marker in graph.LeaderChanges)
        {
            var x = plot.Left + (float)Math.Clamp((marker.AxisSeconds - graph.StartSeconds) / domain, 0d, 1d) * plot.Width;
            graphics.DrawLine(pen, x, plot.Top, x, plot.Bottom);
            DrawText(graphics, "leader", font, TextMuted, new RectangleF(x + 4f, plot.Top + 4f, 42f, 12f));
        }
    }

    private void DrawGapDriverChangeMarkers(Graphics graphics, DesignV2GraphBody graph, RectangleF plot, double maxGapSeconds)
    {
        if (graph.DriverChanges.Count == 0)
        {
            return;
        }

        using var font = FontOf(7.5f, FontStyle.Bold);
        foreach (var marker in graph.DriverChanges)
        {
            var point = GapGraphPoint(marker.AxisSeconds, marker.GapSeconds, graph, plot, maxGapSeconds);
            var color = marker.IsReference ? Green : TextSecondary;
            using var linePen = new Pen(Color.FromArgb(170, color), 1.2f);
            using var fill = new SolidBrush(Surface);
            graphics.DrawLine(linePen, point.X, point.Y - 8f, point.X, point.Y + 8f);
            graphics.FillEllipse(fill, point.X - 4f, point.Y - 4f, 8f, 8f);
            graphics.DrawEllipse(linePen, point.X - 4f, point.Y - 4f, 8f, 8f);
            DrawText(graphics, marker.Label, font, color, new RectangleF(point.X + 6f, point.Y - 16f, 28f, 12f));
        }
    }

    internal static PointF GapGraphPoint(
        DesignV2GapTrendPoint point,
        DesignV2GraphBody graph,
        RectangleF plot,
        double maxGapSeconds)
    {
        return GapGraphPoint(point.AxisSeconds, point.GapSeconds, graph, plot, maxGapSeconds);
    }

    private static PointF GapGraphPoint(
        double axisSeconds,
        double gapSeconds,
        DesignV2GraphBody graph,
        RectangleF plot,
        double maxGapSeconds)
    {
        var domain = Math.Max(1d, graph.EndSeconds - graph.StartSeconds);
        var x = plot.Left + (float)Math.Clamp((axisSeconds - graph.StartSeconds) / domain, 0d, 1d) * plot.Width;
        var scale = graph.Scale ?? DesignV2GapScale.Leader(maxGapSeconds);
        var y = scale.IsFocusRelative
            ? GapDeltaToY(gapSeconds - GapReferenceAt(scale.ReferencePoints, axisSeconds), scale, plot)
            : GapToY(gapSeconds, Math.Max(1d, maxGapSeconds), plot);
        return new PointF(x, y);
    }

    private static float GapToY(double gapSeconds, double maxGapSeconds, RectangleF plot)
    {
        return plot.Top + (float)(Math.Clamp(gapSeconds / Math.Max(1d, maxGapSeconds), 0d, 1d) * plot.Height);
    }

    private static float GapDeltaToY(double deltaSeconds, DesignV2GapScale scale, RectangleF plot)
    {
        var referenceY = GapFocusReferenceY(plot);
        if (deltaSeconds < 0d)
        {
            var ratio = Math.Clamp(Math.Abs(deltaSeconds) / Math.Max(1d, scale.AheadSeconds), 0d, 1d);
            return referenceY - (float)(ratio * Math.Max(1f, referenceY - (plot.Top + GapFocusScaleTopPadding)));
        }

        var behindRatio = Math.Clamp(deltaSeconds / Math.Max(1d, scale.BehindSeconds), 0d, 1d);
        return referenceY + (float)(behindRatio * Math.Max(1f, plot.Bottom - GapFocusScaleBottomPadding - referenceY));
    }

    private static float GapFocusReferenceY(RectangleF plot)
    {
        return plot.Top + plot.Height * GapFocusScaleReferenceRatio;
    }

    private static int GapDrawPriority(DesignV2GapSeries series, int? threatCarIdx)
    {
        if (series.IsReference)
        {
            return 3;
        }

        if (series.IsClassLeader)
        {
            return 2;
        }

        return series.CarIdx == threatCarIdx ? 1 : 0;
    }

    private static Color GapSeriesColor(DesignV2GapSeries series, int index, int? threatCarIdx)
    {
        if (series.CarIdx == threatCarIdx)
        {
            return Error;
        }

        if (series.IsReference)
        {
            return Cyan;
        }

        if (series.IsClassLeader)
        {
            return TextPrimary;
        }

        return (index % 3) switch
        {
            0 => Amber,
            1 => Green,
            _ => Magenta
        };
    }

    private static double GapSeriesAlphaMultiplier(DesignV2GapSeries series, int? threatCarIdx)
    {
        return series.IsClassLeader || series.IsReference || series.CarIdx == threatCarIdx
            ? 1d
            : 0.48d;
    }

    private static void DrawGapWeatherBands(Graphics graphics, DesignV2GraphBody graph, RectangleF plot)
    {
        if (graph.Weather.Count == 0)
        {
            return;
        }

        var domain = Math.Max(1d, graph.EndSeconds - graph.StartSeconds);
        foreach (var (point, index) in graph.Weather.Select((point, index) => (point, index)))
        {
            if (GapWeatherColor(point.Condition) is not { } color)
            {
                continue;
            }

            var nextAxis = index + 1 < graph.Weather.Count
                ? graph.Weather[index + 1].AxisSeconds
                : graph.EndSeconds;
            var x = plot.Left + (float)Math.Clamp((point.AxisSeconds - graph.StartSeconds) / domain, 0d, 1d) * plot.Width;
            var nextX = plot.Left + (float)Math.Clamp((nextAxis - graph.StartSeconds) / domain, 0d, 1d) * plot.Width;
            if (nextX <= x)
            {
                continue;
            }

            using var brush = new SolidBrush(color);
            graphics.FillRectangle(brush, x, plot.Top, nextX - x, plot.Height);
        }
    }

    private static Color? GapWeatherColor(DesignV2GapWeatherCondition condition)
    {
        return condition switch
        {
            DesignV2GapWeatherCondition.Damp => Color.FromArgb(16, Cyan),
            DesignV2GapWeatherCondition.Wet => Color.FromArgb(24, 70, 135, 230),
            DesignV2GapWeatherCondition.DeclaredWet => Color.FromArgb(32, 78, 142, 238),
            _ => null
        };
    }

    private void DrawChat(Graphics graphics, RectangleF rect, IReadOnlyList<DesignV2ChatRow> rows)
    {
        FillRounded(graphics, rect, 5, SurfaceInset, BorderMuted);
        using var authorFont = FontOf(9.8f, FontStyle.Bold);
        using var messageFont = FontOf(10.5f);
        var innerHeight = Math.Max(1f, rect.Height - 16f);
        var rowWidth = Math.Max(1f, rect.Width - 16f);
        var visibleRows = VisibleChatRows(graphics, rows, messageFont, rowWidth, innerHeight);
        var totalRowsHeight = visibleRows.Sum(row => row.Height) + Math.Max(0, visibleRows.Count - 1) * 8f;
        var y = Math.Max(rect.Top + 8f, rect.Bottom - 8f - totalRowsHeight);
        foreach (var row in visibleRows)
        {
            var rowRect = new RectangleF(rect.Left + 8, y, rowWidth, row.Height);
            FillRounded(graphics, rowRect, 5, SurfaceRaised, Color.FromArgb(76, BorderMuted));
            var authorColor = TryParseHexColor(row.Row.AuthorColorHex, out var twitchColor)
                ? twitchColor
                : EvidenceColor(row.Row.Evidence);
            var authorLeft = rowRect.Left + 10f + DrawChatBadges(graphics, row.Row.Badges, authorFont, rowRect);
            DrawText(graphics, row.Row.Author, authorFont, authorColor, new RectangleF(authorLeft, rowRect.Top + 6, rowRect.Width * 0.42f, 14));
            if (row.Row.Metadata is { Count: > 0 })
            {
                StreamChatGdiRenderer.DrawMetadataChips(
                    graphics,
                    row.Row.Metadata,
                    authorFont,
                    new RectangleF(rowRect.Left + rowRect.Width * 0.44f, rowRect.Top + 6, rowRect.Width * 0.52f, 16),
                    TextSecondary,
                    Color.FromArgb(32, Cyan),
                    Color.FromArgb(58, Cyan));
            }

            StreamChatGdiRenderer.DrawSegments(
                graphics,
                StreamChatGdiRenderer.EffectiveSegments(row.Row.Message, row.Row.Segments),
                messageFont,
                TextSecondary,
                new RectangleF(rowRect.Left + 10, rowRect.Top + 23, rowRect.Width - 20, rowRect.Height - 29),
                Color.FromArgb(40, 145, 71, 255),
                Color.FromArgb(96, 145, 71, 255),
                TextPrimary);
            y += row.Height + 8f;
        }
    }

    private static IReadOnlyList<DesignV2MeasuredChatRow> VisibleChatRows(
        Graphics graphics,
        IReadOnlyList<DesignV2ChatRow> rows,
        Font messageFont,
        float rowWidth,
        float availableHeight)
    {
        var measuredRows = new List<DesignV2MeasuredChatRow>();
        var usedHeight = 0f;
        foreach (var row in rows.Reverse())
        {
            var height = MeasureChatRowHeight(graphics, row, messageFont, rowWidth, availableHeight);
            var nextHeight = usedHeight + height + (measuredRows.Count == 0 ? 0f : 8f);
            if (measuredRows.Count > 0 && nextHeight > availableHeight)
            {
                break;
            }

            if (measuredRows.Count == 0 && height > availableHeight)
            {
                height = availableHeight;
            }

            measuredRows.Add(new DesignV2MeasuredChatRow(row, height));
            usedHeight += height + (measuredRows.Count == 1 ? 0f : 8f);
        }

        measuredRows.Reverse();
        return measuredRows;
    }

    private static float MeasureChatRowHeight(
        Graphics graphics,
        DesignV2ChatRow row,
        Font messageFont,
        float rowWidth,
        float availableHeight)
    {
        var messageWidth = Math.Max(80, (int)Math.Floor(rowWidth - 20f));
        var measuredHeight = StreamChatGdiRenderer.MeasureSegmentsHeight(
            graphics,
            StreamChatGdiRenderer.EffectiveSegments(row.Message, row.Segments),
            messageFont,
            messageWidth);
        var height = Math.Max(44f, 30f + measuredHeight);
        return Math.Min(Math.Max(44f, availableHeight), height);
    }

    private static float DrawChatBadges(
        Graphics graphics,
        IReadOnlyList<string>? badges,
        Font font,
        RectangleF rowRect)
    {
        if (badges is not { Count: > 0 })
        {
            return 0f;
        }

        var x = rowRect.Left + 10f;
        var maxRight = rowRect.Left + rowRect.Width * 0.34f;
        foreach (var badge in badges.Take(3))
        {
            var label = badge.ToUpperInvariant();
            var width = Math.Clamp(graphics.MeasureString(label, font).Width + 8f, 18f, 58f);
            if (x + width > maxRight)
            {
                break;
            }

            var badgeRect = new RectangleF(x, rowRect.Top + 7, width, 12);
            FillRounded(graphics, badgeRect, 2, Color.FromArgb(112, 145, 71, 255), Color.Transparent);
            DrawText(graphics, label, font, Color.White, badgeRect, ContentAlignment.MiddleCenter);
            x += width + 4f;
        }

        return Math.Max(0f, x - (rowRect.Left + 10f));
    }

    private static void DrawWrappedText(Graphics graphics, string text, Font font, Color color, RectangleF bounds)
    {
        using var brush = new SolidBrush(color);
        using var format = ChatWrapFormat();
        graphics.DrawString(text, font, brush, bounds, format);
    }

    private static StringFormat ChatWrapFormat()
    {
        return new StringFormat
        {
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = 0,
            Alignment = StringAlignment.Near,
            LineAlignment = StringAlignment.Near
        };
    }

    private void DrawInputs(Graphics graphics, RectangleF rect, DesignV2InputsBody body)
    {
        DrawInputsOverlay(
            graphics,
            rect,
            new DesignV2OverlayModel("Inputs", body.IsAvailable ? "live" : "waiting", "source: local input telemetry", body.IsAvailable ? DesignV2Evidence.Live : DesignV2Evidence.Unavailable, body),
            body);
    }

    private void DrawInputsOverlay(Graphics graphics, RectangleF rect, DesignV2OverlayModel model, DesignV2InputsBody body)
    {
        FillRounded(graphics, rect, 8, Surface, Color.FromArgb(235, Cyan));
        var header = new RectangleF(rect.Left, rect.Top, rect.Width, HeaderHeight);
        using (var titleBrush = new SolidBrush(TitleBar))
        {
            graphics.FillRectangle(titleBrush, header);
        }

        using (var accent = new SolidBrush(Magenta))
        {
            graphics.FillRectangle(accent, rect.Left, header.Bottom - 2, rect.Width, 2);
        }

        using var titleFont = FontOf(13f, FontStyle.Bold);
        DrawText(graphics, "Inputs", titleFont, TextPrimary, new RectangleF(rect.Left + 14, rect.Top + 10, 100, 16));

        var content = InputsContentBounds(rect, header);
        if (!body.HasContent)
        {
            using var waitingFont = FontOf(11, FontStyle.Bold);
            DrawText(graphics, "no input content enabled", waitingFont, TextMuted, content, ContentAlignment.MiddleCenter);
            return;
        }

        var railWidth = body.HasRail
            ? body.HasGraph
                ? Math.Min(204f, Math.Max(136f, content.Width * 0.40f))
                : Math.Min(240f, content.Width)
            : 0f;
        RectangleF? graph = body.HasGraph
            ? new RectangleF(
                content.Left,
                content.Top,
                Math.Max(160, content.Width - railWidth - (body.HasRail ? 18 : 0)),
                Math.Max(40, content.Height))
            : null;
        if (graph is { } graphRect)
        {
            FillRounded(graphics, graphRect, 5, SurfaceInset, BorderMuted);
            DrawInputGrid(graphics, graphRect);
        }

        if (body.ShowThrottleTrace && graph is { } throttleGraph)
        {
            DrawInputTrace(graphics, body.Trace, throttleGraph, Green, point => point.Throttle);
        }
        if (body.ShowBrakeTrace && graph is { } brakeGraph)
        {
            DrawInputTrace(graphics, body.Trace, brakeGraph, Error, point => point.Brake);
            DrawInputAbsTrace(graphics, body.Trace, brakeGraph);
        }
        if (body.ShowClutchTrace && graph is { } clutchGraph)
        {
            DrawInputTrace(graphics, body.Trace, clutchGraph, Cyan, point => point.Clutch);
        }
        if (graph is { } waitingGraph && body.Trace.Count < 2 && !body.IsAvailable)
        {
            using var waitingFont = FontOf(11, FontStyle.Bold);
            DrawText(graphics, "waiting for inputs", waitingFont, TextMuted, waitingGraph, ContentAlignment.MiddleCenter);
        }

        if (body.HasRail)
        {
            var railLeft = graph is { } railGraph
                ? railGraph.Right + 18
                : content.Left + (content.Width - railWidth) / 2f;
            var rail = new RectangleF(railLeft, content.Top, railWidth, content.Height);
            FillRounded(graphics, rail, 5, SurfaceRaised, BorderMuted);
            DrawInputRail(graphics, body, RectangleF.Inflate(rail, -8, -8));
        }
    }

    private static void DrawInputGrid(Graphics graphics, RectangleF rect)
    {
        using var pen = new Pen(Color.FromArgb(46, TextMuted), 1);
        for (var step = 1; step < 4; step++)
        {
            var y = rect.Top + rect.Height * step / 4f;
            graphics.DrawLine(pen, rect.Left, y, rect.Right, y);
        }
    }

    private static void DrawInputTrace(
        Graphics graphics,
        IReadOnlyList<InputStateTracePoint> trace,
        RectangleF rect,
        Color color,
        Func<InputStateTracePoint, double> select)
    {
        if (trace.Count < 2)
        {
            using var waitingPen = new Pen(Color.FromArgb(60, color), 2);
            graphics.DrawLine(waitingPen, rect.Left + 8, rect.Top + rect.Height / 2f, rect.Right - 8, rect.Top + rect.Height / 2f);
            return;
        }

        using var pen = new Pen(color, 2)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };
        var points = trace.Select((point, index) => new PointF(
            rect.Left + index / (float)Math.Max(1, trace.Count - 1) * rect.Width,
            rect.Bottom - (float)Math.Clamp(select(point), 0d, 1d) * rect.Height)).ToArray();
        if (points.Length > 1)
        {
            using var path = SmoothInputTracePath(points);
            graphics.DrawPath(pen, path);
        }
    }

    private static GraphicsPath SmoothInputTracePath(IReadOnlyList<PointF> points)
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
                ClampSmoothInputControlY(p1.Y + (p2.Y - p0.Y) / 6f, p1.Y, p2.Y));
            var control2 = new PointF(
                p2.X - (p3.X - p1.X) / 6f,
                ClampSmoothInputControlY(p2.Y - (p3.Y - p1.Y) / 6f, p1.Y, p2.Y));
            path.AddBezier(p1, control1, control2, p2);
        }

        return path;
    }

    private static float ClampSmoothInputControlY(float value, float segmentStartY, float segmentEndY)
    {
        var min = Math.Min(segmentStartY, segmentEndY);
        var max = Math.Max(segmentStartY, segmentEndY);
        return Math.Clamp(value, min, max);
    }

    private static void DrawInputAbsTrace(Graphics graphics, IReadOnlyList<InputStateTracePoint> trace, RectangleF rect)
    {
        if (trace.Count < 2)
        {
            return;
        }

        using var pen = new Pen(Amber, 3)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        for (var index = 1; index < trace.Count; index++)
        {
            if (!trace[index].BrakeAbsActive)
            {
                continue;
            }

            var previous = trace[index - 1];
            var current = trace[index];
            var x1 = rect.Left + (index - 1) / (float)Math.Max(1, trace.Count - 1) * rect.Width;
            var x2 = rect.Left + index / (float)Math.Max(1, trace.Count - 1) * rect.Width;
            var y1 = rect.Bottom - (float)Math.Clamp(previous.Brake, 0d, 1d) * rect.Height;
            var y2 = rect.Bottom - (float)Math.Clamp(current.Brake, 0d, 1d) * rect.Height;
            graphics.DrawLine(pen, x1, y1, x2, y2);
        }
    }

    private void DrawInputRail(Graphics graphics, DesignV2InputsBody body, RectangleF rect)
    {
        var layout = BuildInputRailLayout(
            rect,
            body.ShowThrottle,
            body.ShowBrake,
            body.ShowClutch,
            body.ShowSteering,
            body.ShowGear,
            body.ShowSpeed);
        foreach (var item in layout.Items)
        {
            switch (item.Kind)
            {
                case DesignV2InputRailItemKind.Throttle:
                    DrawInputBar(graphics, "THR", body.Throttle, Green, item.Bounds);
                    break;
                case DesignV2InputRailItemKind.Brake:
                    DrawInputBar(graphics, body.BrakeAbsActive ? "ABS" : "BRK", body.Brake, body.BrakeAbsActive ? Amber : Error, item.Bounds);
                    break;
                case DesignV2InputRailItemKind.Clutch:
                    DrawInputBar(graphics, "CLT", body.Clutch, Cyan, item.Bounds);
                    break;
                case DesignV2InputRailItemKind.SteeringWheel:
                    DrawInputWheel(graphics, body.SteeringWheelAngle, body.SteeringText, item.Bounds);
                    break;
                case DesignV2InputRailItemKind.Gear:
                    DrawInputReadout(graphics, "GEAR", body.GearText, item.Bounds);
                    break;
                case DesignV2InputRailItemKind.Speed:
                    DrawInputReadout(graphics, "SPD", body.SpeedText, item.Bounds);
                    break;
            }
        }
    }

    internal static DesignV2InputRailLayout BuildInputRailLayout(
        RectangleF rect,
        bool showThrottle,
        bool showBrake,
        bool showClutch,
        bool showSteering,
        bool showGear,
        bool showSpeed)
    {
        var items = new List<DesignV2InputRailItem>();
        var compact = rect.Height < 190f;
        var barHeight = compact ? 24f : 27f;
        var barGap = compact ? 7f : 11f;
        var readoutHeight = 20f;
        var readoutGap = compact ? 4f : 8f;
        var readoutKinds = new List<DesignV2InputRailItemKind>();
        if (showGear)
        {
            readoutKinds.Add(DesignV2InputRailItemKind.Gear);
        }

        if (showSpeed)
        {
            readoutKinds.Add(DesignV2InputRailItemKind.Speed);
        }

        var readoutReserve = readoutKinds.Count == 0
            ? 0f
            : readoutKinds.Count * readoutHeight + Math.Max(0, readoutKinds.Count - 1) * readoutGap;
        var y = rect.Top;
        AddBar(showThrottle, DesignV2InputRailItemKind.Throttle);
        AddBar(showBrake, DesignV2InputRailItemKind.Brake);
        AddBar(showClutch, DesignV2InputRailItemKind.Clutch);

        if (showSteering)
        {
            var readoutGapReserve = readoutReserve > 0f ? 8f : 0f;
            var wheelBottom = rect.Bottom - readoutReserve - readoutGapReserve;
            var wheelAvailable = wheelBottom - y;
            if (wheelAvailable >= 24f)
            {
                var wheelHeight = Math.Min(compact ? 58f : 78f, Math.Max(28f, wheelAvailable));
                items.Add(new DesignV2InputRailItem(
                    DesignV2InputRailItemKind.SteeringWheel,
                    new RectangleF(rect.Left, y, rect.Width, wheelHeight)));
            }
        }

        if (readoutKinds.Count > 0)
        {
            var readoutTop = Math.Max(rect.Top, rect.Bottom - readoutReserve);
            for (var index = 0; index < readoutKinds.Count; index++)
            {
                items.Add(new DesignV2InputRailItem(
                    readoutKinds[index],
                    new RectangleF(
                        rect.Left,
                        readoutTop + index * (readoutHeight + readoutGap),
                        rect.Width,
                        readoutHeight)));
            }
        }

        return new DesignV2InputRailLayout(items);

        void AddBar(bool enabled, DesignV2InputRailItemKind kind)
        {
            if (!enabled)
            {
                return;
            }

            items.Add(new DesignV2InputRailItem(kind, new RectangleF(rect.Left, y, rect.Width, barHeight)));
            y += barHeight + barGap;
        }
    }

    private void DrawInputBar(Graphics graphics, string label, double? value, Color color, RectangleF rect)
    {
        using var font = FontOf(9.5f, FontStyle.Bold);
        using var valueFont = FontOf(8.5f, FontStyle.Bold);
        DrawText(graphics, label, font, TextMuted, new RectangleF(rect.Left, rect.Top + 3, 42, 14));
        var bar = new RectangleF(rect.Left + 48, rect.Top + 5, Math.Max(8, rect.Width - 48), 12);
        FillRounded(graphics, bar, 6, SurfaceRaised, null);
        var fill = new RectangleF(bar.Left, bar.Top, bar.Width * (float)Math.Clamp(value ?? 0d, 0d, 1d), bar.Height);
        FillRounded(graphics, fill, 6, color, null);
        DrawText(graphics, FormatPercent(value), valueFont, TextMuted, new RectangleF(bar.Left, bar.Bottom + 1, bar.Width, 11), ContentAlignment.MiddleRight);
    }

    private void DrawInputReadout(Graphics graphics, string label, string value, RectangleF rect)
    {
        using var labelFont = FontOf(8.5f, FontStyle.Bold);
        using var valueFont = FontOf(12.5f, FontStyle.Bold);
        DrawText(graphics, label, labelFont, TextMuted, new RectangleF(rect.Left, rect.Top + 3, 42, 14));
        DrawText(graphics, value, valueFont, TextPrimary, new RectangleF(rect.Left + 50, rect.Top, rect.Width - 50, 18), ContentAlignment.MiddleRight);
    }

    private void DrawInputWheel(Graphics graphics, double? angleRadians, string angleText, RectangleF rect)
    {
        using var labelFont = FontOf(8.5f, FontStyle.Bold);
        using var valueFont = FontOf(10.5f, FontStyle.Bold);
        DrawText(graphics, "WHEEL", labelFont, TextMuted, new RectangleF(rect.Left, rect.Top, 54, 14));
        DrawText(graphics, angleText, valueFont, TextPrimary, new RectangleF(rect.Left + 58, rect.Top - 1, rect.Width - 58, 16), ContentAlignment.MiddleRight);

        var wheelSlot = new RectangleF(
            rect.Left + 2,
            rect.Top + 20,
            Math.Max(1, rect.Width - 4),
            Math.Max(1, rect.Height - 22));
        var diameter = Math.Max(8, Math.Min(wheelSlot.Width, wheelSlot.Height) - 4);
        var wheel = new RectangleF(
            wheelSlot.Left + (wheelSlot.Width - diameter) / 2f,
            wheelSlot.Top + (wheelSlot.Height - diameter) / 2f,
            diameter,
            diameter);
        using var ringPen = new Pen(TextSecondary, 3.4f);
        graphics.DrawEllipse(ringPen, wheel);
        var center = new PointF(wheel.Left + wheel.Width / 2f, wheel.Top + wheel.Height / 2f);
        var angle = (float)(angleRadians ?? 0d);
        using var spokePen = new Pen(Cyan, 3f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        foreach (var spokeAngle in new[] { -Math.PI / 2d, Math.PI / 6d, Math.PI * 5d / 6d })
        {
            var combined = spokeAngle + angle;
            var end = new PointF(
                center.X + (float)Math.Cos(combined) * wheel.Width * 0.34f,
                center.Y + (float)Math.Sin(combined) * wheel.Height * 0.34f);
            graphics.DrawLine(spokePen, center, end);
        }
    }

    private void DrawRadar(Graphics graphics, RectangleF rect, DesignV2RadarBody radar)
    {
        DrawRadarOverlay(graphics, rect, radar);
    }

    private void DrawRadarOverlay(Graphics graphics, RectangleF rect, DesignV2RadarBody radar)
    {
        var model = radar.RenderModel;
        var surfaceAlpha = Math.Clamp(radar.SurfaceAlpha, 0d, 1d);
        if (!model.ShouldRender || surfaceAlpha <= model.MinimumVisibleAlpha)
        {
            return;
        }

        var diameter = Math.Max(20, Math.Min(rect.Width, rect.Height));
        var radarRect = new RectangleF(
            rect.Left + (rect.Width - diameter) / 2f,
            rect.Top + (rect.Height - diameter) / 2f,
            diameter,
            diameter);
        var scaleX = radarRect.Width / (float)Math.Max(1d, model.Width);
        var scaleY = radarRect.Height / (float)Math.Max(1d, model.Height);

        DrawRenderCircle(graphics, radarRect, model.Background, scaleX, scaleY, surfaceAlpha);
        if (model.MulticlassArc is { } arc)
        {
            DrawRenderArc(graphics, radarRect, arc, scaleX, scaleY, surfaceAlpha);
        }

        foreach (var ring in model.Rings)
        {
            DrawRenderCircle(graphics, radarRect, ring, scaleX, scaleY, surfaceAlpha);
        }

        foreach (var car in model.Cars)
        {
            DrawRenderRectangle(graphics, radarRect, car, scaleX, scaleY, surfaceAlpha);
        }

        foreach (var label in model.Labels)
        {
            DrawRenderText(graphics, radarRect, label, scaleX, scaleY, surfaceAlpha);
        }
    }

    private static void DrawRenderCircle(
        Graphics graphics,
        RectangleF target,
        CarRadarRenderCircle circle,
        float scaleX,
        float scaleY,
        double alphaMultiplier)
    {
        var rect = ScaleRect(target, circle.X, circle.Y, circle.Width, circle.Height, scaleX, scaleY);
        if (circle.Fill is { } fill)
        {
            using var brush = new SolidBrush(RenderColor(fill, alphaMultiplier));
            graphics.FillEllipse(brush, rect);
        }

        if (circle.Stroke is { } stroke && circle.StrokeWidth > 0d)
        {
            using var pen = new Pen(RenderColor(stroke, alphaMultiplier), Math.Max(1f, (float)circle.StrokeWidth * Math.Min(scaleX, scaleY)));
            graphics.DrawEllipse(pen, rect);
        }
    }

    private static void DrawRenderArc(
        Graphics graphics,
        RectangleF target,
        CarRadarRenderArc arc,
        float scaleX,
        float scaleY,
        double alphaMultiplier)
    {
        using var pen = new Pen(RenderColor(arc.Stroke, alphaMultiplier), Math.Max(1f, (float)arc.StrokeWidth * Math.Min(scaleX, scaleY)))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        graphics.DrawArc(
            pen,
            ScaleRect(target, arc.X, arc.Y, arc.Width, arc.Height, scaleX, scaleY),
            (float)arc.StartDegrees,
            (float)arc.SweepDegrees);
    }

    private static void DrawRenderRectangle(
        Graphics graphics,
        RectangleF target,
        CarRadarRenderRectangle rectangle,
        float scaleX,
        float scaleY,
        double alphaMultiplier)
    {
        var rect = ScaleRect(target, rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height, scaleX, scaleY);
        using var path = RoundedPath(rect, (float)rectangle.Radius * Math.Min(scaleX, scaleY));
        using var brush = new SolidBrush(RenderColor(rectangle.Fill, alphaMultiplier));
        graphics.FillPath(brush, path);
        if (rectangle.StrokeWidth > 0d)
        {
            using var pen = new Pen(RenderColor(rectangle.Stroke, alphaMultiplier), Math.Max(1f, (float)rectangle.StrokeWidth * Math.Min(scaleX, scaleY)));
            graphics.DrawPath(pen, path);
        }
    }

    private void DrawRenderText(
        Graphics graphics,
        RectangleF target,
        CarRadarRenderText label,
        float scaleX,
        float scaleY,
        double alphaMultiplier)
    {
        using var font = FontOf((float)(label.FontSize * Math.Min(scaleX, scaleY)), label.Bold ? FontStyle.Bold : FontStyle.Regular);
        using var brush = new SolidBrush(RenderColor(label.Color, alphaMultiplier));
        using var format = new StringFormat
        {
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap,
            Alignment = label.Alignment switch
            {
                "center" => StringAlignment.Center,
                "far" => StringAlignment.Far,
                _ => StringAlignment.Near
            },
            LineAlignment = StringAlignment.Center
        };
        graphics.DrawString(
            label.Text,
            font,
            brush,
            ScaleRect(target, label.X, label.Y, label.Width, label.Height, scaleX, scaleY),
            format);
    }

    private static RectangleF ScaleRect(
        RectangleF target,
        double x,
        double y,
        double width,
        double height,
        float scaleX,
        float scaleY)
    {
        return new RectangleF(
            target.Left + (float)x * scaleX,
            target.Top + (float)y * scaleY,
            (float)width * scaleX,
            (float)height * scaleY);
    }

    private static Color RenderColor(CarRadarRenderColor color, double alphaMultiplier = 1d)
    {
        return Color.FromArgb(
            (int)Math.Round(Math.Clamp(color.Alpha * alphaMultiplier, 0d, 255d)),
            Math.Clamp(color.Red, 0, 255),
            Math.Clamp(color.Green, 0, 255),
            Math.Clamp(color.Blue, 0, 255));
    }

    private void DrawTrackMapOverlay(Graphics graphics, RectangleF rect, DesignV2TrackMapBody body)
    {
        var model = body.RenderModel;
        var size = Math.Max(20, Math.Min(rect.Width, rect.Height));
        var target = new RectangleF(
            rect.Left + (rect.Width - size) / 2f,
            rect.Top + (rect.Height - size) / 2f,
            size,
            size);
        var scaleX = target.Width / (float)Math.Max(1d, model.Width);
        var scaleY = target.Height / (float)Math.Max(1d, model.Height);

        foreach (var primitive in model.Primitives)
        {
            DrawTrackMapPrimitive(graphics, target, primitive, scaleX, scaleY);
        }

        foreach (var marker in model.Markers)
        {
            DrawTrackMapMarker(graphics, target, marker, scaleX, scaleY);
        }
    }

    private static void DrawTrackMapPrimitive(
        Graphics graphics,
        RectangleF target,
        TrackMapRenderPrimitive primitive,
        float scaleX,
        float scaleY)
    {
        switch (primitive.Kind)
        {
            case "ellipse":
                DrawTrackMapEllipsePrimitive(graphics, target, primitive, scaleX, scaleY);
                return;
            case "arc":
                DrawTrackMapArcPrimitive(graphics, target, primitive, scaleX, scaleY);
                return;
            case "line":
                DrawTrackMapLinePrimitive(graphics, target, primitive, scaleX, scaleY);
                return;
            default:
                DrawTrackMapPathPrimitive(graphics, target, primitive, scaleX, scaleY);
                return;
        }
    }

    private static void DrawTrackMapPathPrimitive(
        Graphics graphics,
        RectangleF target,
        TrackMapRenderPrimitive primitive,
        float scaleX,
        float scaleY)
    {
        if (primitive.Points.Count < 2)
        {
            return;
        }

        using var path = new GraphicsPath();
        var previous = ScalePoint(target, primitive.Points[0], scaleX, scaleY);
        for (var index = 1; index < primitive.Points.Count; index++)
        {
            var current = ScalePoint(target, primitive.Points[index], scaleX, scaleY);
            path.AddLine(previous, current);
            previous = current;
        }

        if (primitive.Closed)
        {
            path.CloseFigure();
        }

        if (primitive.Fill is { } fill)
        {
            using var brush = new SolidBrush(RenderTrackMapColor(fill));
            graphics.FillPath(brush, path);
        }

        if (primitive.Stroke is { } stroke && primitive.StrokeWidth > 0d)
        {
            using var pen = new Pen(RenderTrackMapColor(stroke), Math.Max(1f, (float)primitive.StrokeWidth * Math.Min(scaleX, scaleY)))
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round
            };
            graphics.DrawPath(pen, path);
        }
    }

    private static void DrawTrackMapEllipsePrimitive(
        Graphics graphics,
        RectangleF target,
        TrackMapRenderPrimitive primitive,
        float scaleX,
        float scaleY)
    {
        if (primitive.Rect is not { } rect)
        {
            return;
        }

        var scaled = ScaleRect(target, rect.X, rect.Y, rect.Width, rect.Height, scaleX, scaleY);
        if (primitive.Fill is { } fill)
        {
            using var brush = new SolidBrush(RenderTrackMapColor(fill));
            graphics.FillEllipse(brush, scaled);
        }

        if (primitive.Stroke is { } stroke && primitive.StrokeWidth > 0d)
        {
            using var pen = new Pen(RenderTrackMapColor(stroke), Math.Max(1f, (float)primitive.StrokeWidth * Math.Min(scaleX, scaleY)))
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            graphics.DrawEllipse(pen, scaled);
        }
    }

    private static void DrawTrackMapArcPrimitive(
        Graphics graphics,
        RectangleF target,
        TrackMapRenderPrimitive primitive,
        float scaleX,
        float scaleY)
    {
        if (primitive.Rect is not { } rect || primitive.Stroke is not { } stroke)
        {
            return;
        }

        using var pen = new Pen(RenderTrackMapColor(stroke), Math.Max(1f, (float)primitive.StrokeWidth * Math.Min(scaleX, scaleY)))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        graphics.DrawArc(
            pen,
            ScaleRect(target, rect.X, rect.Y, rect.Width, rect.Height, scaleX, scaleY),
            (float)primitive.StartDegrees,
            (float)primitive.SweepDegrees);
    }

    private static void DrawTrackMapLinePrimitive(
        Graphics graphics,
        RectangleF target,
        TrackMapRenderPrimitive primitive,
        float scaleX,
        float scaleY)
    {
        if (primitive.Points.Count < 2 || primitive.Stroke is not { } stroke)
        {
            return;
        }

        using var pen = new Pen(RenderTrackMapColor(stroke), Math.Max(1f, (float)primitive.StrokeWidth * Math.Min(scaleX, scaleY)))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        graphics.DrawLine(
            pen,
            ScalePoint(target, primitive.Points[0], scaleX, scaleY),
            ScalePoint(target, primitive.Points[1], scaleX, scaleY));
    }

    private void DrawTrackMapMarker(
        Graphics graphics,
        RectangleF target,
        TrackMapRenderMarker marker,
        float scaleX,
        float scaleY)
    {
        var center = ScalePoint(target, new TrackMapRenderPoint(marker.X, marker.Y), scaleX, scaleY);
        var radius = (float)marker.Radius * Math.Min(scaleX, scaleY);
        if (marker.AlertRingStroke is { } ringStroke && marker.AlertRingRadius > 0d)
        {
            var ringRadius = (float)marker.AlertRingRadius * Math.Min(scaleX, scaleY);
            var ringRect = new RectangleF(center.X - ringRadius, center.Y - ringRadius, ringRadius * 2f, ringRadius * 2f);
            using var ringPen = new Pen(
                RenderTrackMapColor(ringStroke),
                Math.Max(1f, (float)marker.AlertRingStrokeWidth * Math.Min(scaleX, scaleY)))
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            graphics.DrawEllipse(ringPen, ringRect);
        }

        var markerRect = new RectangleF(center.X - radius, center.Y - radius, radius * 2f, radius * 2f);
        using (var brush = new SolidBrush(RenderTrackMapColor(marker.Fill)))
        {
            graphics.FillEllipse(brush, markerRect);
        }
        using (var pen = new Pen(RenderTrackMapColor(marker.Stroke), Math.Max(1f, (float)marker.StrokeWidth * Math.Min(scaleX, scaleY))))
        {
            graphics.DrawEllipse(pen, markerRect);
        }

        if (marker.Label is { Length: > 0 })
        {
            using var markerFont = FontOf((float)marker.LabelFontSize * Math.Min(scaleX, scaleY), FontStyle.Bold);
            DrawText(graphics, marker.Label, markerFont, RenderTrackMapColor(marker.LabelColor), markerRect, ContentAlignment.MiddleCenter);
        }
    }

    private static PointF ScalePoint(
        RectangleF target,
        TrackMapRenderPoint point,
        float scaleX,
        float scaleY)
    {
        return new PointF(
            target.Left + (float)point.X * scaleX,
            target.Top + (float)point.Y * scaleY);
    }

    private static Color RenderTrackMapColor(TrackMapRenderColor color)
    {
        return Color.FromArgb(
            Math.Clamp(color.Alpha, 0, 255),
            Math.Clamp(color.Red, 0, 255),
            Math.Clamp(color.Green, 0, 255),
            Math.Clamp(color.Blue, 0, 255));
    }

    private void DrawGeneratedTrackMap(
        Graphics graphics,
        RectangleF trackRect,
        TrackMapDocument trackMap,
        DesignV2TrackMapTransform transform,
        IReadOnlyList<DesignV2TrackMapMarker> markers,
        IReadOnlyList<LiveTrackSectorSegment> sectors,
        Brush interior,
        bool showSectorBoundaries)
    {
        FillTrackMapGeometry(graphics, trackMap.RacingLine, transform, interior);
        using (var halo = new Pen(TrackHalo, 11f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        })
        using (var line = new Pen(TrackLine, 4.4f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        })
        {
            DrawTrackMapGeometry(graphics, trackMap.RacingLine, transform, halo);
            DrawTrackMapGeometry(graphics, trackMap.RacingLine, transform, line);
        }

        DrawGeneratedTrackMapSectorHighlights(graphics, trackMap.RacingLine, transform, sectors);
        if (showSectorBoundaries)
        {
            DrawGeneratedTrackMapSectorBoundaries(graphics, trackMap.RacingLine, transform, sectors);
        }

        if (trackMap.PitLane is { Points.Count: >= 2 } pitLane)
        {
            using var pitPen = new Pen(PitLineColor, TrackPitLineWidth)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round
            };
            DrawTrackMapGeometry(graphics, pitLane, transform, pitPen);
        }

        DrawTrackMapMarkers(
            graphics,
            markers,
            marker => PointOnTrackMapGeometry(trackMap.RacingLine, transform, marker.LapDistPct));
    }

    private void DrawCircleTrackMap(
        Graphics graphics,
        RectangleF trackRect,
        IReadOnlyList<DesignV2TrackMapMarker> markers,
        IReadOnlyList<LiveTrackSectorSegment> sectors,
        Brush interior,
        bool showSectorBoundaries)
    {
        graphics.FillEllipse(interior, trackRect);
        using (var halo = new Pen(TrackHalo, 11f))
        {
            graphics.DrawEllipse(halo, trackRect);
        }
        using (var line = new Pen(TrackLine, 4.4f))
        {
            graphics.DrawEllipse(line, trackRect);
        }

        DrawTrackMapSectorHighlights(graphics, trackRect, sectors);
        if (showSectorBoundaries)
        {
            DrawTrackMapSectorBoundaries(graphics, trackRect, sectors);
        }
        DrawTrackMapMarkers(graphics, trackRect, markers);
    }

    private static void DrawTrackMapGeometry(
        Graphics graphics,
        TrackMapGeometry geometry,
        DesignV2TrackMapTransform transform,
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

    private static void FillTrackMapGeometry(
        Graphics graphics,
        TrackMapGeometry geometry,
        DesignV2TrackMapTransform transform,
        Brush brush)
    {
        if (!geometry.Closed || geometry.Points.Count < 3)
        {
            return;
        }

        using var path = TrackMapGeometryPath(geometry, transform);
        graphics.FillPath(brush, path);
    }

    private static GraphicsPath TrackMapGeometryPath(
        TrackMapGeometry geometry,
        DesignV2TrackMapTransform transform)
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

    private static void DrawGeneratedTrackMapSectorHighlights(
        Graphics graphics,
        TrackMapGeometry geometry,
        DesignV2TrackMapTransform transform,
        IReadOnlyList<LiveTrackSectorSegment> sectors)
    {
        foreach (var sector in sectors.Where(HasTrackMapHighlight))
        {
            using var pen = new Pen(TrackMapSectorHighlightColor(sector.Highlight), 5.8f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round
            };
            DrawTrackMapGeometrySegment(graphics, geometry, transform, sector.StartPct, sector.EndPct, pen);
        }
    }

    private static void DrawGeneratedTrackMapSectorBoundaries(
        Graphics graphics,
        TrackMapGeometry geometry,
        DesignV2TrackMapTransform transform,
        IReadOnlyList<LiveTrackSectorSegment> sectors)
    {
        foreach (var progress in TrackMapBoundaryProgresses(sectors))
        {
            if (TrackMapGeometryBoundaryTick(geometry, transform, progress) is { } tick)
            {
                DrawTrackMapBoundaryTick(graphics, tick.Start, tick.End, IsStartFinishProgress(progress));
            }
        }
    }

    private static void DrawTrackMapGeometrySegment(
        Graphics graphics,
        TrackMapGeometry geometry,
        DesignV2TrackMapTransform transform,
        double startPct,
        double endPct,
        Pen pen)
    {
        if (geometry.Points.Count < 2)
        {
            return;
        }

        using var path = new GraphicsPath();
        foreach (var range in TrackMapSegmentRanges(startPct, endPct))
        {
            AddTrackMapGeometrySegment(path, geometry, transform, range.StartPct, range.EndPct);
        }

        if (path.PointCount > 1)
        {
            graphics.DrawPath(pen, path);
        }
    }

    private static void AddTrackMapGeometrySegment(
        GraphicsPath path,
        TrackMapGeometry geometry,
        DesignV2TrackMapTransform transform,
        double startPct,
        double endPct)
    {
        if (endPct <= startPct)
        {
            return;
        }

        var startPoint = PointOnTrackMapGeometry(geometry, transform, startPct);
        var endPoint = PointOnTrackMapGeometry(geometry, transform, endPct);
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

    private static DesignV2TrackMapBoundaryTick? TrackMapGeometryBoundaryTick(
        TrackMapGeometry geometry,
        DesignV2TrackMapTransform transform,
        double progress)
    {
        var center = PointOnTrackMapGeometry(geometry, transform, progress);
        var before = PointOnTrackMapGeometry(geometry, transform, progress - 0.002d);
        var after = PointOnTrackMapGeometry(geometry, transform, progress + 0.002d);
        if (center is null || before is null || after is null)
        {
            return null;
        }

        var dx = after.Value.X - before.Value.X;
        var dy = after.Value.Y - before.Value.Y;
        var length = Math.Max(0.001f, (float)Math.Sqrt(dx * dx + dy * dy));
        var normalX = -dy / length;
        var normalY = dx / length;
        var half = TrackMapBoundaryTickLength(progress) / 2f;
        return new DesignV2TrackMapBoundaryTick(
            new PointF(center.Value.X - normalX * half, center.Value.Y - normalY * half),
            new PointF(center.Value.X + normalX * half, center.Value.Y + normalY * half));
    }

    private static PointF? PointOnTrackMapGeometry(
        TrackMapGeometry geometry,
        DesignV2TrackMapTransform transform,
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

            return InterpolateTrackMapPoint(previous, current, target, transform);
        }

        if (geometry.Closed)
        {
            var previous = points[^1];
            var current = points[0] with { LapDistPct = points[0].LapDistPct + 1d };
            var adjustedTarget = target < previous.LapDistPct ? target + 1d : target;
            return InterpolateTrackMapPoint(previous, current, adjustedTarget, transform);
        }

        return transform.Map(points.MinBy(point => Math.Abs(point.LapDistPct - target)) ?? points[0]);
    }

    private static PointF InterpolateTrackMapPoint(
        TrackMapPoint previous,
        TrackMapPoint current,
        double target,
        DesignV2TrackMapTransform transform)
    {
        var span = current.LapDistPct - previous.LapDistPct;
        var ratio = span <= 0d ? 0d : Math.Clamp((target - previous.LapDistPct) / span, 0d, 1d);
        return transform.Map(new TrackMapPoint(
            target,
            previous.X + (current.X - previous.X) * ratio,
            previous.Y + (current.Y - previous.Y) * ratio));
    }

    private static void DrawTrackMapSectorHighlights(Graphics graphics, RectangleF rect, IReadOnlyList<LiveTrackSectorSegment> sectors)
    {
        foreach (var sector in sectors.Where(HasTrackMapHighlight))
        {
            using var pen = new Pen(TrackMapSectorHighlightColor(sector.Highlight), 5.8f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            foreach (var range in TrackMapSegmentRanges(sector.StartPct, sector.EndPct))
            {
                var start = (float)(range.StartPct * 360d - 90d);
                var sweep = (float)((range.EndPct - range.StartPct) * 360d);
                if (sweep > 0)
                {
                    graphics.DrawArc(pen, rect, start, sweep);
                }
            }
        }
    }

    private static void DrawTrackMapSectorBoundaries(Graphics graphics, RectangleF rect, IReadOnlyList<LiveTrackSectorSegment> sectors)
    {
        if (sectors.Count < 2)
        {
            return;
        }

        var center = new PointF(rect.Left + rect.Width / 2f, rect.Top + rect.Height / 2f);
        foreach (var progress in TrackMapBoundaryProgresses(sectors))
        {
            var point = TrackMapPoint(rect, progress);
            var dx = point.X - center.X;
            var dy = point.Y - center.Y;
            var length = Math.Max(0.001f, (float)Math.Sqrt(dx * dx + dy * dy));
            var unitX = dx / length;
            var unitY = dy / length;
            var half = TrackMapBoundaryTickLength(progress) / 2f;
            DrawTrackMapBoundaryTick(
                graphics,
                point.X - unitX * half,
                point.Y - unitY * half,
                point.X + unitX * half,
                point.Y + unitY * half,
                IsStartFinishProgress(progress));
        }
    }

    private static void DrawTrackMapBoundaryTick(
        Graphics graphics,
        float x1,
        float y1,
        float x2,
        float y2,
        bool isStartFinish)
    {
        DrawTrackMapBoundaryTick(graphics, new PointF(x1, y1), new PointF(x2, y2), isStartFinish);
    }

    private static void DrawTrackMapBoundaryTick(Graphics graphics, PointF start, PointF end, bool isStartFinish)
    {
        if (isStartFinish)
        {
            using var shadowPen = new Pen(StartFinishBoundaryShadowColor, 5.8f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            using var mainPen = new Pen(StartFinishBoundaryColor, 3.2f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            using var highlightPen = new Pen(Color.FromArgb(235, 255, 247, 255), 1.2f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            graphics.DrawLine(shadowPen, start, end);
            graphics.DrawLine(mainPen, start, end);
            graphics.DrawLine(highlightPen, start, end);
            return;
        }

        using var pen = new Pen(Cyan, 2.2f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        graphics.DrawLine(pen, start, end);
    }

    private void DrawTrackMapMarkers(Graphics graphics, RectangleF rect, IReadOnlyList<DesignV2TrackMapMarker> markers)
    {
        DrawTrackMapMarkers(graphics, markers, marker => TrackMapPoint(rect, marker.LapDistPct));
    }

    private void DrawTrackMapMarkers(
        Graphics graphics,
        IReadOnlyList<DesignV2TrackMapMarker> markers,
        Func<DesignV2TrackMapMarker, PointF?> pointForMarker)
    {
        foreach (var marker in markers.OrderBy(marker => marker.IsFocus).ThenBy(marker => marker.CarIdx))
        {
            if (pointForMarker(marker) is not { } point)
            {
                continue;
            }

            using var markerFont = FontOf(7.6f, FontStyle.Bold);
            var labelSize = marker.IsFocus && marker.PositionLabel is not null
                ? graphics.MeasureString(marker.PositionLabel, markerFont)
                : SizeF.Empty;
            var radius = marker.IsFocus
                ? Math.Max(5.7f, Math.Max(labelSize.Width, labelSize.Height) / 2f + 3.5f)
                : 3.6f;
            var markerRect = new RectangleF(point.X - radius, point.Y - radius, radius * 2f, radius * 2f);
            using (var brush = new SolidBrush(marker.Color))
            {
                graphics.FillEllipse(brush, markerRect);
            }
            using (var pen = new Pen(TrackMarkerBorder, marker.IsFocus ? 2f : 1.4f))
            {
                graphics.DrawEllipse(pen, markerRect);
            }
            if (marker.IsFocus && marker.PositionLabel is not null)
            {
                DrawText(graphics, marker.PositionLabel, markerFont, Color.FromArgb(5, 13, 17), markerRect, ContentAlignment.MiddleCenter);
            }
        }
    }

    private void DrawFlagsOverlay(Graphics graphics, RectangleF rect, DesignV2FlagsBody body)
    {
        if (!body.ManagedEnabled || body.SettingsOverlayActive || body.Flags.Count == 0)
        {
            return;
        }

        if (body.Flags.Count == 0)
        {
            using var waitingFont = FontOf(12f, FontStyle.Bold);
            DrawText(graphics, body.IsWaiting ? "waiting for flags" : "no active flags", waitingFont, TextMuted, rect, ContentAlignment.MiddleCenter);
            return;
        }

        var bounds = new RectangleF(
            rect.Left + FlagOuterPadding,
            rect.Top + FlagOuterPadding,
            Math.Max(1f, rect.Width - FlagOuterPadding * 2f),
            Math.Max(1f, rect.Height - FlagOuterPadding * 2f));
        var (columns, rows) = FlagGridFor(body.Flags.Count);
        var cellWidth = (bounds.Width - (columns - 1) * FlagCellGap) / columns;
        var cellHeight = (bounds.Height - (rows - 1) * FlagCellGap) / rows;
        for (var index = 0; index < body.Flags.Count; index++)
        {
            var row = index / columns;
            var column = index % columns;
            var cell = new RectangleF(
                bounds.Left + column * (cellWidth + FlagCellGap),
                bounds.Top + row * (cellHeight + FlagCellGap),
                cellWidth,
                cellHeight);
            DrawFlagCell(graphics, cell, body.Flags[index], index);
        }
    }

    private static void DrawFlagCell(Graphics graphics, RectangleF cell, FlagOverlayDisplayItem flag, int index)
    {
        var compact = cell.Height < 92f || cell.Width < 132f;
        var poleX = cell.Left + Math.Max(12f, cell.Width * 0.16f);
        var poleTop = cell.Top + 4f;
        var poleBottom = cell.Bottom - 2f;
        using (var shadowPen = new Pen(FlagPoleShadowColor, compact ? 2f : 3f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        })
        {
            graphics.DrawLine(shadowPen, poleX + 1f, poleTop + 1f, poleX + 1f, poleBottom + 1f);
        }
        using (var polePen = new Pen(FlagPoleColor, compact ? 2f : 3f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        })
        {
            graphics.DrawLine(polePen, poleX, poleTop, poleX, poleBottom);
        }

        var clothLeft = poleX + 1f;
        var clothWidth = Math.Max(48f, cell.Right - clothLeft - 8f);
        var clothHeight = Math.Max(24f, Math.Min(cell.Height * 0.7f, clothWidth * 0.58f));
        var clothTop = cell.Top + Math.Max(4f, (cell.Height - clothHeight) * 0.32f);
        var clothBounds = new RectangleF(clothLeft, clothTop, clothWidth, clothHeight);
        using var path = CreateFlagPath(clothBounds, compact ? 3.5f : 5.5f, index);
        DrawFlagCloth(graphics, path, flag, clothBounds);
    }

    private static void DrawFlagCloth(
        Graphics graphics,
        GraphicsPath path,
        FlagOverlayDisplayItem flag,
        RectangleF clothBounds)
    {
        if (flag.Kind == FlagDisplayKind.Checkered)
        {
            DrawCheckeredFlag(graphics, path, clothBounds);
            return;
        }

        using (var brush = new SolidBrush(FlagFillColor(flag.Kind)))
        {
            graphics.FillPath(brush, path);
        }

        if (flag.Kind == FlagDisplayKind.Meatball)
        {
            var diameter = Math.Min(clothBounds.Width, clothBounds.Height) * 0.44f;
            using var discBrush = new SolidBrush(Orange);
            graphics.FillEllipse(
                discBrush,
                clothBounds.Left + (clothBounds.Width - diameter) / 2f,
                clothBounds.Top + (clothBounds.Height - diameter) / 2f,
                diameter,
                diameter);
        }
        else if (flag.Kind == FlagDisplayKind.Caution)
        {
            using var stripeBrush = new SolidBrush(Color.FromArgb(72, 0, 0, 0));
            var stripeWidth = Math.Max(8f, clothBounds.Width * 0.12f);
            var oldClip = graphics.Clip;
            try
            {
                graphics.SetClip(path, CombineMode.Intersect);
                for (var x = clothBounds.Left - clothBounds.Height; x < clothBounds.Right; x += stripeWidth * 2.5f)
                {
                    graphics.FillPolygon(
                        stripeBrush,
                        [
                            new PointF(x, clothBounds.Bottom),
                            new PointF(x + stripeWidth, clothBounds.Bottom),
                            new PointF(x + stripeWidth + clothBounds.Height, clothBounds.Top),
                            new PointF(x + clothBounds.Height, clothBounds.Top)
                        ]);
                }
            }
            finally
            {
                graphics.SetClip(oldClip, CombineMode.Replace);
                oldClip.Dispose();
            }
        }

        DrawFlagOutline(graphics, path, flag.Kind);
    }

    private static void DrawCheckeredFlag(Graphics graphics, GraphicsPath path, RectangleF clothBounds)
    {
        var oldClip = graphics.Clip;
        try
        {
            graphics.SetClip(path, CombineMode.Intersect);
            using var whiteBrush = new SolidBrush(Color.FromArgb(245, 247, 250));
            using var blackBrush = new SolidBrush(Color.FromArgb(8, 10, 12));
            graphics.FillRectangle(whiteBrush, clothBounds);
            const int columns = 6;
            const int rows = 4;
            var squareWidth = clothBounds.Width / columns;
            var squareHeight = clothBounds.Height / rows;
            for (var row = 0; row < rows; row++)
            {
                for (var column = 0; column < columns; column++)
                {
                    if ((row + column) % 2 == 0)
                    {
                        continue;
                    }

                    graphics.FillRectangle(
                        blackBrush,
                        clothBounds.Left + column * squareWidth,
                        clothBounds.Top + row * squareHeight,
                        squareWidth + 1f,
                        squareHeight + 1f);
                }
            }
        }
        finally
        {
            graphics.SetClip(oldClip, CombineMode.Replace);
            oldClip.Dispose();
        }

        DrawFlagOutline(graphics, path, FlagDisplayKind.Checkered);
    }

    private static void DrawFlagOutline(Graphics graphics, GraphicsPath path, FlagDisplayKind kind)
    {
        var outline = kind == FlagDisplayKind.White || kind == FlagDisplayKind.Checkered
            ? Color.FromArgb(220, 26, 30, 34)
            : Color.FromArgb(172, 255, 255, 255);
        using var pen = new Pen(outline, 1.4f)
        {
            LineJoin = LineJoin.Round
        };
        graphics.DrawPath(pen, path);
    }

    private static GraphicsPath CreateFlagPath(RectangleF bounds, float wave, int index)
    {
        var phase = index % 2 == 0 ? 1f : -1f;
        var path = new GraphicsPath();
        var leftTop = new PointF(bounds.Left, bounds.Top);
        var rightTop = new PointF(bounds.Right, bounds.Top + wave * phase);
        var rightBottom = new PointF(bounds.Right, bounds.Bottom + wave * 0.4f * phase);
        var leftBottom = new PointF(bounds.Left, bounds.Bottom);
        path.StartFigure();
        path.AddBezier(
            leftTop,
            new PointF(bounds.Left + bounds.Width * 0.28f, bounds.Top - wave * phase),
            new PointF(bounds.Left + bounds.Width * 0.62f, bounds.Top + wave * phase),
            rightTop);
        path.AddLine(rightTop, rightBottom);
        path.AddBezier(
            rightBottom,
            new PointF(bounds.Left + bounds.Width * 0.62f, bounds.Bottom - wave * phase),
            new PointF(bounds.Left + bounds.Width * 0.28f, bounds.Bottom + wave * phase),
            leftBottom);
        path.CloseFigure();
        return path;
    }

    private static Color FlagFillColor(FlagDisplayKind kind)
    {
        return kind switch
        {
            FlagDisplayKind.Green => Color.FromArgb(48, 214, 109),
            FlagDisplayKind.Blue => Color.FromArgb(55, 162, 255),
            FlagDisplayKind.Yellow or FlagDisplayKind.Caution => Color.FromArgb(255, 207, 74),
            FlagDisplayKind.Red => Color.FromArgb(236, 76, 86),
            FlagDisplayKind.Black or FlagDisplayKind.Meatball => Color.FromArgb(8, 10, 12),
            FlagDisplayKind.White => Color.FromArgb(246, 248, 250),
            _ => Color.White
        };
    }

    private static (int Columns, int Rows) FlagGridFor(int count)
    {
        return count switch
        {
            <= 1 => (1, 1),
            2 => (2, 1),
            <= 4 => (2, 2),
            <= 6 => (3, 2),
            _ => (4, 2)
        };
    }

    private void ReportOverlayError(Exception exception)
    {
        var now = DateTimeOffset.UtcNow;
        var message = exception.Message;
        if (!string.Equals(_lastLoggedError, message, StringComparison.Ordinal)
            || _lastLoggedErrorAtUtc is null
            || now - _lastLoggedErrorAtUtc > TimeSpan.FromSeconds(30))
        {
            _logger.LogError(exception, "Design V2 overlay {OverlayId} failed.", _definition.Id);
            _lastLoggedError = message;
            _lastLoggedErrorAtUtc = now;
        }
    }

    private static DesignV2OverlayModel WaitingModel(string title, string status)
    {
        return new DesignV2OverlayModel(
            title,
            status,
            "source: waiting",
            DesignV2Evidence.Unavailable,
            new DesignV2MetricRowsBody([]));
    }

    private static DesignV2OverlayModel InitialModelFor(DesignV2LiveOverlayKind kind)
    {
        if (kind == DesignV2LiveOverlayKind.CarRadar)
        {
            return new DesignV2OverlayModel(
                "Car Radar",
                string.Empty,
                string.Empty,
                DesignV2Evidence.Unavailable,
                new DesignV2RadarBody(
                    false,
                    false,
                    false,
                    [],
                    null,
                    true,
                    false,
                    CarRadarRenderModel.Empty,
                    0d));
        }

        if (kind == DesignV2LiveOverlayKind.StreamChat)
        {
            return WaitingModel(TitleFor(kind), "waiting") with
            {
                HeaderText = "waiting",
                ShowFooter = false,
                ShowHeader = true
            };
        }

        return WaitingModel(TitleFor(kind), "waiting");
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

    private static string TitleFor(DesignV2LiveOverlayKind kind)
    {
        return kind switch
        {
            DesignV2LiveOverlayKind.Standings => "Standings",
            DesignV2LiveOverlayKind.FuelCalculator => "Fuel Calculator",
            DesignV2LiveOverlayKind.Relative => "Relative",
            DesignV2LiveOverlayKind.TrackMap => "Track Map",
            DesignV2LiveOverlayKind.StreamChat => "Stream Chat",
            DesignV2LiveOverlayKind.Flags => "Flags",
            DesignV2LiveOverlayKind.SessionWeather => "Session / Weather",
            DesignV2LiveOverlayKind.PitService => "Pit Service",
            DesignV2LiveOverlayKind.InputState => "Inputs",
            DesignV2LiveOverlayKind.CarRadar => "Car Radar",
            DesignV2LiveOverlayKind.GapToLeader => "Gap To Leader",
            _ => "Overlay"
        };
    }

    private static bool UsesTransparentBackground(DesignV2LiveOverlayKind kind)
    {
        return kind is DesignV2LiveOverlayKind.TrackMap
            or DesignV2LiveOverlayKind.CarRadar
            or DesignV2LiveOverlayKind.Flags;
    }

    private static string RadarStatusText(DesignV2RadarBody radar)
    {
        if (!radar.IsAvailable)
        {
            return "WAITING";
        }

        if (radar.ShowMulticlassWarning
            && radar.StrongestMulticlassApproach?.RelativeSeconds is { } seconds
            && IsFinite(seconds))
        {
            return $"{Math.Abs(seconds):0.0}s";
        }

        if (radar.HasLeft || radar.HasRight)
        {
            return "SIDE";
        }

        return "CLEAR";
    }

    internal static bool IsInputTransparentKind(DesignV2LiveOverlayKind kind)
    {
        return kind is DesignV2LiveOverlayKind.StreamChat;
    }

    private static RectangleF StreamChatCloseButtonBounds(RectangleF outer)
    {
        return new RectangleF(
            Math.Max(outer.Left + 4, outer.Right - StreamChatCloseButtonSize - StreamChatCloseButtonRightMargin),
            outer.Top + StreamChatCloseButtonTop,
            StreamChatCloseButtonSize,
            StreamChatCloseButtonSize);
    }

    private static Rectangle StreamChatCloseButtonBounds(Size clientSize)
    {
        return new Rectangle(
            Math.Max(4, clientSize.Width - StreamChatCloseButtonSize - StreamChatCloseButtonRightMargin),
            StreamChatCloseButtonTop,
            StreamChatCloseButtonSize,
            StreamChatCloseButtonSize);
    }

    private bool IsStreamChatCloseButtonHit(Point clientPoint)
    {
        return StreamChatCloseButtonBounds(ClientSize).Contains(clientPoint);
    }

    private static void DrawStreamChatCloseButton(Graphics graphics, RectangleF bounds)
    {
        FillRounded(graphics, bounds, 3, TitleBar, Border);
        using var glyphPen = new Pen(TextPrimary, 1.45f);
        graphics.DrawLine(glyphPen, bounds.Left + 7, bounds.Top + 7, bounds.Right - 7, bounds.Bottom - 7);
        graphics.DrawLine(glyphPen, bounds.Right - 7, bounds.Top + 7, bounds.Left + 7, bounds.Bottom - 7);
    }

    internal static bool IsStreamChatDragHit(Point clientPoint, Size clientSize)
    {
        var closeButtonLeft = Math.Max(4, clientSize.Width - StreamChatCloseButtonSize - StreamChatCloseButtonRightMargin);
        return clientPoint.X >= 0
            && clientPoint.X < closeButtonLeft
            && clientPoint.Y >= 0
            && clientPoint.Y < HeaderHeight;
    }

    private static bool HasTrackMapHighlight(LiveTrackSectorSegment sector)
    {
        return string.Equals(sector.Highlight, LiveTrackSectorHighlights.PersonalBest, StringComparison.Ordinal)
            || string.Equals(sector.Highlight, LiveTrackSectorHighlights.BestLap, StringComparison.Ordinal);
    }

    private static Color TrackMapSectorHighlightColor(string highlight)
    {
        return string.Equals(highlight, LiveTrackSectorHighlights.BestLap, StringComparison.Ordinal)
            ? BestLapSectorColor
            : PersonalBestSectorColor;
    }

    private static IEnumerable<DesignV2SectorProgressRange> TrackMapSegmentRanges(double startPct, double endPct)
    {
        var start = NormalizeProgress(startPct);
        var end = endPct >= 1d ? 1d : NormalizeProgress(endPct);
        if (end <= start && endPct < 1d)
        {
            yield return new DesignV2SectorProgressRange(start, 1d);
            yield return new DesignV2SectorProgressRange(0d, end);
            yield break;
        }

        yield return new DesignV2SectorProgressRange(start, Math.Clamp(end, 0d, 1d));
    }

    private static IEnumerable<double> TrackMapBoundaryProgresses(IReadOnlyList<LiveTrackSectorSegment> sectors)
    {
        var seen = new HashSet<int>();
        foreach (var sector in sectors)
        {
            var progress = NormalizeProgress(sector.StartPct);
            var key = (int)Math.Round(progress * 100_000d);
            if (seen.Add(key))
            {
                yield return progress;
            }
        }
    }

    private static PointF TrackMapPoint(RectangleF rect, double progress)
    {
        var angle = NormalizeProgress(progress) * Math.PI * 2d - Math.PI / 2d;
        return new PointF(
            rect.Left + rect.Width / 2f + (float)Math.Cos(angle) * rect.Width / 2f,
            rect.Top + rect.Height / 2f + (float)Math.Sin(angle) * rect.Height / 2f);
    }

    private static float TrackMapBoundaryTickLength(double progress)
    {
        return IsStartFinishProgress(progress)
            ? TrackSectorBoundaryTickLength * 1.45f
            : TrackSectorBoundaryTickLength;
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

    private static ContentAlignment AlignmentFor(OverlayContentColumnAlignment alignment)
    {
        return alignment switch
        {
            OverlayContentColumnAlignment.Center => ContentAlignment.MiddleCenter,
            OverlayContentColumnAlignment.Right => ContentAlignment.MiddleRight,
            _ => ContentAlignment.MiddleLeft
        };
    }

    private static DesignV2Evidence EvidenceFor(SimpleTelemetryTone tone)
    {
        return tone switch
        {
            SimpleTelemetryTone.Success => DesignV2Evidence.Live,
            SimpleTelemetryTone.Info => DesignV2Evidence.Measured,
            SimpleTelemetryTone.Modeled => DesignV2Evidence.Modeled,
            SimpleTelemetryTone.Warning => DesignV2Evidence.Partial,
            SimpleTelemetryTone.Error => DesignV2Evidence.Error,
            SimpleTelemetryTone.Waiting => DesignV2Evidence.Unavailable,
            SimpleTelemetryTone.Normal => DesignV2Evidence.Neutral,
            _ => DesignV2Evidence.Live
        };
    }

    private static Color EvidenceColor(DesignV2Evidence evidence)
    {
        return evidence switch
        {
            DesignV2Evidence.Neutral => TextPrimary,
            DesignV2Evidence.Live => Green,
            DesignV2Evidence.Measured => Cyan,
            DesignV2Evidence.Modeled => Magenta,
            DesignV2Evidence.Partial => Amber,
            DesignV2Evidence.Error => Error,
            _ => TextMuted
        };
    }

    private static Color EvidenceBackground(DesignV2Evidence evidence)
    {
        return evidence switch
        {
            DesignV2Evidence.Neutral => Color.FromArgb(20, TextMuted),
            DesignV2Evidence.Live => Blend(SurfaceRaised, Green, 8, 1),
            DesignV2Evidence.Measured => Blend(SurfaceRaised, Cyan, 8, 1),
            DesignV2Evidence.Modeled => Blend(SurfaceRaised, Magenta, 8, 1),
            DesignV2Evidence.Partial => Blend(SurfaceRaised, Amber, 8, 1),
            DesignV2Evidence.Error => Blend(SurfaceRaised, Error, 7, 1),
            _ => SurfaceInset
        };
    }

    private static string FormatAxisSeconds(double seconds)
    {
        if (!IsFinite(seconds))
        {
            return "--";
        }

        return seconds < 60d
            ? $"+{seconds:0.0}s"
            : $"+{Math.Floor(seconds / 60d):0}:{seconds % 60d:00.0}";
    }

    private static string FormatDeltaSeconds(double seconds)
    {
        if (!IsFinite(seconds))
        {
            return "--";
        }

        var sign = seconds > 0d ? "+" : seconds < 0d ? "-" : string.Empty;
        var absolute = Math.Abs(seconds);
        return absolute >= 60d
            ? $"{sign}{Math.Floor(absolute / 60d):0}:{absolute % 60d:00.0}"
            : $"{sign}{absolute:0.0}s";
    }

    private static string FormatTrendWindow(TimeSpan trendWindow)
    {
        return trendWindow.TotalHours >= 1d
            ? $"{trendWindow.TotalHours:0.#}h"
            : $"{trendWindow.TotalMinutes:0}m";
    }

    private static string FormatGapChangeSeconds(double seconds)
    {
        if (!IsFinite(seconds))
        {
            return "--";
        }

        return Math.Abs(seconds) < 0.05d
            ? "0.0"
            : $"{(seconds > 0d ? "+" : string.Empty)}{seconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}";
    }

    private static string FormatFocusedTrendChangeSeconds(double seconds)
    {
        return FormatGapChangeSeconds(seconds);
    }

    private static double NiceCeiling(double value)
    {
        if (!IsFinite(value) || value <= 0d)
        {
            return 1d;
        }

        var exponent = Math.Floor(Math.Log10(value));
        var baseValue = Math.Pow(10d, exponent);
        var normalized = value / baseValue;
        var nice = normalized <= 1d ? 1d
            : normalized <= 2d ? 2d
            : normalized <= 5d ? 5d
            : 10d;
        return nice * baseValue;
    }

    private static double NiceGridStep(double value)
    {
        if (!IsFinite(value) || value <= 0.25d)
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

    private static string FormatPercent(double? value)
    {
        return value is { } number && IsFinite(number)
            ? $"{Math.Round(Math.Clamp(number, 0d, 1d) * 100d):0}%"
            : "--";
    }

    private static string FormatGear(int? gear)
    {
        return gear switch
        {
            -1 => "R",
            0 => "N",
            > 0 => gear.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
            _ => "--"
        };
    }

    private static string FormatSteering(double? angleRadians)
    {
        return angleRadians is { } value && IsFinite(value)
            ? $"{(value * 180d / Math.PI).ToString("+0;-0;0", System.Globalization.CultureInfo.InvariantCulture)} deg"
            : "--";
    }

    private static double? FirstValidFuelLevel(params double?[] values)
    {
        foreach (var value in values)
        {
            if (value is { } fuelLevel && IsFinite(fuelLevel) && fuelLevel > 0d)
            {
                return fuelLevel;
            }
        }

        return null;
    }

    private static string DesignV2GapCarShortLabel(DesignV2GapCarRenderState state)
    {
        return $"#{state.CarIdx}";
    }

    private Font FontOf(float size, FontStyle style = FontStyle.Regular)
    {
        return new Font(string.IsNullOrWhiteSpace(_fontFamily) ? "Segoe UI" : _fontFamily, size, style, GraphicsUnit.Point);
    }

    private static RectangleF FooterBounds(RectangleF outer)
    {
        return new RectangleF(outer.Left + 10, outer.Bottom - 24, Math.Max(1, outer.Width - 20), 14);
    }

    private static RectangleF InputsContentBounds(RectangleF rect, RectangleF header)
    {
        return new RectangleF(
            rect.Left + 16,
            header.Bottom + 12,
            Math.Max(1, rect.Width - 32),
            Math.Max(1, rect.Height - HeaderHeight - 26));
    }

    private static float TableCellHorizontalPadding(float width)
    {
        return width switch
        {
            <= 38f => 3f,
            <= 62f => 5f,
            _ => 8f
        };
    }

    private static void DrawText(Graphics graphics, string text, Font font, Color color, RectangleF bounds, ContentAlignment alignment = ContentAlignment.MiddleLeft)
    {
        using var brush = new SolidBrush(color);
        using var format = new StringFormat
        {
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap,
            Alignment = alignment switch
            {
                ContentAlignment.MiddleCenter => StringAlignment.Center,
                ContentAlignment.MiddleRight => StringAlignment.Far,
                _ => StringAlignment.Near
            },
            LineAlignment = StringAlignment.Center
        };
        graphics.DrawString(text, font, brush, bounds, format);
    }

    private static void FillRounded(Graphics graphics, RectangleF rect, float radius, Color fill, Color? stroke)
    {
        using var path = RoundedPath(rect, radius);
        using var brush = new SolidBrush(fill);
        graphics.FillPath(brush, path);
        if (stroke is { } strokeColor)
        {
            using var pen = new Pen(strokeColor, 1f);
            graphics.DrawPath(pen, path);
        }
    }

    private static GraphicsPath RoundedPath(RectangleF rect, float radius)
    {
        var path = new GraphicsPath();
        var diameter = Math.Max(0, radius * 2);
        if (diameter <= 0)
        {
            path.AddRectangle(rect);
            return path;
        }

        path.AddArc(rect.Left, rect.Top, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Top, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static Color Blend(Color panel, Color accent, int panelWeight, int accentWeight)
    {
        var total = Math.Max(1, panelWeight + accentWeight);
        return Color.FromArgb(
            Math.Max(panel.A, accent.A),
            (panel.R * panelWeight + accent.R * accentWeight) / total,
            (panel.G * panelWeight + accent.G * accentWeight) / total,
            (panel.B * panelWeight + accent.B * accentWeight) / total);
    }

    private static Color WithAlpha(Color color, double alpha)
    {
        return Color.FromArgb(
            (int)Math.Clamp(color.A * alpha, 0d, 255d),
            color.R,
            color.G,
            color.B);
    }

    private static bool TryParseHexColor(string? value, out Color color)
    {
        color = Color.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim().TrimStart('#');
        if (trimmed.Length != 6 || !int.TryParse(trimmed, System.Globalization.NumberStyles.HexNumber, null, out var rgb))
        {
            return false;
        }

        color = Color.FromArgb((rgb >> 16) & 0xff, (rgb >> 8) & 0xff, rgb & 0xff);
        return true;
    }

    private static string FormatPosition(int? position)
    {
        return position?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "--";
    }

    private static string FormatGap(LiveGapValue gap)
    {
        if (gap.Seconds is { } seconds && IsFinite(seconds))
        {
            return seconds < 60d
                ? $"{seconds:+0.0;-0.0;0.0}s"
                : $"+{Math.Floor(seconds / 60d):0}:{seconds % 60d:00.0}";
        }

        if (gap.Laps is { } laps && IsFinite(laps))
        {
            return $"{laps:+0.00;-0.00;0.00} lap";
        }

        return "--";
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }

    private static string Trim(string value, int maximumLength)
    {
        return value.Length <= maximumLength ? value : value[..Math.Max(0, maximumLength - 1)] + "...";
    }
}

internal enum DesignV2Evidence
{
    Neutral,
    Live,
    Measured,
    Modeled,
    Partial,
    Unavailable,
    Error
}

internal sealed record DesignV2OverlayModel(
    string Title,
    string Status,
    string Footer,
    DesignV2Evidence Evidence,
    DesignV2Body Body,
    string? HeaderText = null,
    bool ShowFooter = true,
    bool ShouldRender = true,
    bool ShowHeader = true);

internal abstract record DesignV2Body;

internal sealed record DesignV2LayoutDiagnostics(
    string Contract,
    string Kind,
    DesignV2LayoutRect Client,
    DesignV2LayoutConstants Constants)
{
    public DesignV2LayoutRect? Outer { get; init; }

    public DesignV2LayoutRect? Header { get; init; }

    public DesignV2LayoutRect? Body { get; init; }

    public DesignV2LayoutRect? Footer { get; init; }

    public bool ShowHeader { get; init; }

    public bool ShowFooter { get; init; }

    public DesignV2LayoutBody? BodyLayout { get; init; }
}

internal readonly record struct DesignV2LayoutRect(float X, float Y, float Width, float Height);

internal readonly record struct DesignV2LayoutPoint(float X, float Y);

internal sealed record DesignV2LayoutConstants(
    int Padding,
    int HeaderHeight,
    int FooterHeight,
    int BodyGap,
    int RowHeight,
    int RowGap,
    int ColumnGap,
    int MinimumColumnWidth,
    int MetricLabelWidth);

internal sealed record DesignV2LayoutBody(string Kind, DesignV2LayoutRect Bounds)
{
    public string? State { get; init; }

    public float ConfiguredWidth { get; init; }

    public float AvailableWidth { get; init; }

    public float FitScale { get; init; }

    public int MaximumRows { get; init; }

    public int DrawnRows { get; init; }

    public int GridColumns { get; init; }

    public int GridRows { get; init; }

    public float GridHeight { get; init; }

    public DesignV2LayoutRect? RowsBounds { get; init; }

    public IReadOnlyList<DesignV2LayoutColumn> Columns { get; init; } = [];

    public IReadOnlyList<DesignV2LayoutRow> Rows { get; init; } = [];

    public IReadOnlyList<DesignV2LayoutMetricRow> MetricRows { get; init; } = [];

    public IReadOnlyList<DesignV2LayoutMetricGrid> MetricGrids { get; init; } = [];

    public IReadOnlyList<DesignV2LayoutFlagCell> FlagCells { get; init; } = [];

    public DesignV2LayoutGraph? Graph { get; init; }

    public DesignV2LayoutInputs? Inputs { get; init; }

    public DesignV2LayoutVector? Vector { get; init; }
}

internal sealed record DesignV2LayoutColumn(
    int Index,
    string Label,
    int ConfiguredWidth,
    float RenderedWidth,
    string Alignment,
    DesignV2LayoutRect Bounds);

internal sealed record DesignV2LayoutRow(
    int Index,
    int SourceIndex,
    string Kind,
    DesignV2LayoutRect Bounds)
{
    public string? Text { get; init; }

    public string? Detail { get; init; }

    public string? Evidence { get; init; }

    public string? Foreground { get; init; }

    public string? Background { get; init; }

    public string? ClassColorHex { get; init; }

    public int? RelativeLapDelta { get; init; }

    public IReadOnlyList<DesignV2LayoutCell> Cells { get; init; } = [];
}

internal sealed record DesignV2LayoutCell(
    int ColumnIndex,
    string ColumnLabel,
    string Text,
    DesignV2LayoutRect Bounds,
    string Alignment)
{
    public string? Evidence { get; init; }

    public string? Foreground { get; init; }

    public string? Background { get; init; }
}

internal sealed record DesignV2LayoutMetricRow(
    string Label,
    string Value,
    DesignV2LayoutRect Bounds,
    DesignV2LayoutRect LabelBounds,
    DesignV2LayoutRect ValueBounds)
{
    public string? Section { get; init; }

    public DesignV2LayoutRect? SectionTitleBounds { get; init; }

    public string? Evidence { get; init; }

    public string? Foreground { get; init; }

    public string? Background { get; init; }

    public string? Accent { get; init; }

    public IReadOnlyList<DesignV2LayoutMetricSegment> Segments { get; init; } = [];
}

internal sealed record DesignV2LayoutMetricSegment(
    int Index,
    string Label,
    string Value,
    DesignV2LayoutRect Bounds,
    DesignV2LayoutRect LabelBounds,
    DesignV2LayoutRect ValueBounds)
{
    public string? Evidence { get; init; }

    public string? Foreground { get; init; }

    public string? Background { get; init; }

    public string? Accent { get; init; }

    public double? RotationDegrees { get; init; }
}

internal sealed record DesignV2LayoutMetricGrid(
    string Title,
    DesignV2LayoutRect Bounds,
    IReadOnlyList<DesignV2LayoutCell> Headers,
    IReadOnlyList<DesignV2LayoutRow> Rows);

internal sealed record DesignV2LayoutGraph(
    DesignV2LayoutRect Frame,
    DesignV2LayoutRect Plot,
    DesignV2LayoutRect Axis,
    DesignV2LayoutRect LabelLane,
    DesignV2LayoutRect? MetricsTable,
    int PointCount,
    int SeriesCount,
    int SeriesPointCount,
    int TrendMetricCount,
    double StartSeconds,
    double EndSeconds)
{
    public IReadOnlyList<DesignV2LayoutGraphSeries> Series { get; init; } = [];

    public IReadOnlyList<DesignV2LayoutGraphBand> WeatherBands { get; init; } = [];

    public IReadOnlyList<DesignV2LayoutGraphMarker> Markers { get; init; } = [];

    public IReadOnlyList<DesignV2LayoutLine> GridLines { get; init; } = [];

    public IReadOnlyList<DesignV2LayoutRow> MetricRows { get; init; } = [];

    public string? Scale { get; init; }

    public double? AheadSeconds { get; init; }

    public double? BehindSeconds { get; init; }

    public double? LatestReferenceGapSeconds { get; init; }

    public double? MaxGapSeconds { get; init; }

    public double? LapReferenceSeconds { get; init; }

    public string? ComparisonLabel { get; init; }
}

internal sealed record DesignV2LayoutGraphSeries(
    int Index,
    int DrawPriority,
    int CarIdx,
    bool IsReference,
    bool IsClassLeader,
    int? ClassPosition,
    int PointCount,
    string BaseColor,
    string RenderedColor,
    double Alpha,
    double EffectiveAlpha,
    float StrokeWidth,
    bool IsDashed,
    bool IsStickyExit,
    bool IsStale)
{
    public IReadOnlyList<DesignV2LayoutGraphPoint> Points { get; init; } = [];

    public DesignV2LayoutPoint? LatestPoint { get; init; }

    public string? EndpointLabel { get; init; }
}

internal sealed record DesignV2LayoutGraphPoint(
    double AxisSeconds,
    double GapSeconds,
    bool StartsSegment,
    DesignV2LayoutPoint Point);

internal sealed record DesignV2LayoutGraphBand(
    string Kind,
    double StartAxisSeconds,
    double EndAxisSeconds,
    DesignV2LayoutRect Bounds,
    string Color);

internal sealed record DesignV2LayoutGraphMarker(
    string Kind,
    string Label,
    DesignV2LayoutPoint Start,
    DesignV2LayoutPoint End)
{
    public double? AxisSeconds { get; init; }

    public double? GapSeconds { get; init; }

    public int? CarIdx { get; init; }

    public bool? IsReference { get; init; }

    public string? Color { get; init; }
}

internal sealed record DesignV2LayoutInputs(
    bool HasContent,
    DesignV2LayoutRect? Graph,
    DesignV2LayoutRect? Rail,
    float RailWidth,
    int TracePointCount,
    IReadOnlyList<DesignV2LayoutInputItem> Items)
{
    public IReadOnlyList<DesignV2LayoutLine> GridLines { get; init; } = [];

    public IReadOnlyList<DesignV2LayoutInputTraceSeries> TraceSeries { get; init; } = [];
}

internal sealed record DesignV2LayoutInputItem(string Kind, DesignV2LayoutRect Bounds);

internal sealed record DesignV2LayoutLine(
    string Kind,
    DesignV2LayoutPoint Start,
    DesignV2LayoutPoint End)
{
    public string? Color { get; init; }

    public float? StrokeWidth { get; init; }
}

internal sealed record DesignV2LayoutInputTraceSeries(
    string Kind,
    string Color,
    float StrokeWidth)
{
    public IReadOnlyList<DesignV2LayoutPoint> Points { get; init; } = [];

    public IReadOnlyList<DesignV2LayoutTraceCurveSegment> Curves { get; init; } = [];
}

internal sealed record DesignV2LayoutTraceCurveSegment(
    DesignV2LayoutPoint Start,
    DesignV2LayoutPoint Control1,
    DesignV2LayoutPoint Control2,
    DesignV2LayoutPoint End);

internal sealed record DesignV2LayoutVector(
    DesignV2LayoutRect Target,
    double SourceWidth,
    double SourceHeight,
    float ScaleX,
    float ScaleY,
    int ItemCount,
    int PrimitiveCount,
    int LabelCount)
{
    public bool ShouldRender { get; init; }

    public double? SurfaceAlpha { get; init; }

    public IReadOnlyList<DesignV2LayoutVectorItem> Items { get; init; } = [];

    public IReadOnlyList<DesignV2LayoutVectorPrimitive> Primitives { get; init; } = [];

    public IReadOnlyList<DesignV2LayoutVectorLabel> Labels { get; init; } = [];
}

internal sealed record DesignV2LayoutVectorItem(
    string Kind,
    int? Id,
    DesignV2LayoutRect Bounds)
{
    public string? Fill { get; init; }

    public string? Stroke { get; init; }

    public float? StrokeWidth { get; init; }

    public string? Label { get; init; }

    public string? LabelColor { get; init; }

    public string? AlertKind { get; init; }

    public DesignV2LayoutRect? AlertRingBounds { get; init; }

    public string? AlertRingStroke { get; init; }

    public float? AlertRingStrokeWidth { get; init; }
}

internal sealed record DesignV2LayoutVectorPrimitive(
    string Kind,
    DesignV2LayoutRect? Bounds,
    IReadOnlyList<DesignV2LayoutPoint> Points,
    bool Closed,
    double? StartDegrees,
    double? SweepDegrees,
    string? Fill,
    string? Stroke,
    float StrokeWidth);

internal sealed record DesignV2LayoutVectorLabel(
    string Text,
    DesignV2LayoutRect Bounds,
    double FontSize,
    bool Bold,
    string Alignment,
    string Color);

internal sealed record DesignV2LayoutFlagCell(
    int Index,
    int Row,
    int Column,
    string Kind,
    DesignV2LayoutRect Bounds,
    DesignV2LayoutRect ClothBounds);

internal sealed record DesignV2TableBody(
    IReadOnlyList<DesignV2Column> Columns,
    IReadOnlyList<DesignV2TableRow> Rows) : DesignV2Body;

internal sealed record DesignV2MetricRowsBody(
    IReadOnlyList<DesignV2MetricRow> Rows,
    IReadOnlyList<DesignV2MetricSection> MetricSections,
    IReadOnlyList<DesignV2MetricGridSection> Sections) : DesignV2Body
{
    public DesignV2MetricRowsBody(IReadOnlyList<DesignV2MetricRow> rows)
        : this(rows, [], [])
    {
    }

    public DesignV2MetricRowsBody(
        IReadOnlyList<DesignV2MetricRow> rows,
        IReadOnlyList<DesignV2MetricGridSection> sections)
        : this(rows, [], sections)
    {
    }
}

internal sealed record DesignV2GraphBody(
    IReadOnlyList<double> Points,
    IReadOnlyList<DesignV2GapSeries> Series,
    IReadOnlyList<DesignV2GapWeatherPoint> Weather,
    IReadOnlyList<DesignV2GapLeaderChangeMarker> LeaderChanges,
    IReadOnlyList<DesignV2GapDriverChangeMarker> DriverChanges,
    double StartSeconds,
    double EndSeconds,
    double? MaxGapSeconds,
    double? LapReferenceSeconds,
    int SelectedSeriesCount,
    IReadOnlyList<DesignV2GapTrendMetric> TrendMetrics,
    DesignV2GapTrendMetric? ActiveThreat,
    int? ThreatCarIdx,
    double MetricDeadbandSeconds,
    string ComparisonLabel = "--",
    DesignV2GapScale? Scale = null) : DesignV2Body
{
    public DesignV2GraphBody(IReadOnlyList<double> points)
        : this(points, [], [], [], [], 0d, 0d, null, null, 0, [], null, null, 0d, "--")
    {
    }
}

internal sealed record DesignV2GapTrendMetric(
    string Label,
    double? FocusGapChangeSeconds,
    DesignV2BehindGainMetric? Chaser,
    string State,
    string? StateLabel,
    DesignV2PitMetricValue? PrimaryPit = null,
    DesignV2PitMetricValue? ThreatPit = null,
    DesignV2PitMetricValue? ComparisonPit = null,
    DesignV2TireMetricValue? PrimaryTire = null,
    DesignV2TireMetricValue? ThreatTire = null,
    DesignV2TireMetricValue? ComparisonTire = null,
    string? PrimaryText = null,
    string? ThreatText = null,
    string? ComparisonText = null);

internal sealed record DesignV2BehindGainMetric(
    int CarIdx,
    string Label,
    double GainSeconds);

internal sealed record DesignV2PitMetricValue(
    double? Seconds,
    int? Lap,
    bool IsActive);

internal sealed record DesignV2TireMetricValue(
    string? Label,
    string? ShortLabel,
    bool IsWet);

internal sealed record DesignV2GapScale(
    double MaxGapSeconds,
    bool IsFocusRelative,
    double AheadSeconds,
    double BehindSeconds,
    IReadOnlyList<DesignV2GapTrendPoint> ReferencePoints,
    double LatestReferenceGapSeconds)
{
    public static DesignV2GapScale Leader(double maxGapSeconds)
    {
        return new DesignV2GapScale(
            MaxGapSeconds: maxGapSeconds,
            IsFocusRelative: false,
            AheadSeconds: 0d,
            BehindSeconds: 0d,
            ReferencePoints: [],
            LatestReferenceGapSeconds: 0d);
    }

    public static DesignV2GapScale FocusRelative(
        double maxGapSeconds,
        double aheadSeconds,
        double behindSeconds,
        IReadOnlyList<DesignV2GapTrendPoint> referencePoints,
        double latestReferenceGapSeconds)
    {
        return new DesignV2GapScale(
            MaxGapSeconds: maxGapSeconds,
            IsFocusRelative: true,
            AheadSeconds: aheadSeconds,
            BehindSeconds: behindSeconds,
            ReferencePoints: referencePoints,
            LatestReferenceGapSeconds: latestReferenceGapSeconds);
    }
}

internal sealed record DesignV2GapSeries(
    int CarIdx,
    bool IsReference,
    bool IsClassLeader,
    int? ClassPosition,
    double Alpha,
    bool IsStickyExit,
    bool IsStale,
    IReadOnlyList<DesignV2GapTrendPoint> Points);

internal sealed record DesignV2GapTrendPoint(
    DateTimeOffset TimestampUtc,
    double AxisSeconds,
    double GapSeconds,
    int CarIdx,
    bool IsReference,
    bool IsClassLeader,
    int? ClassPosition,
    bool StartsSegment);

internal sealed record DesignV2GapWeatherPoint(
    double AxisSeconds,
    DesignV2GapWeatherCondition Condition);

internal sealed record DesignV2GapLeaderChangeMarker(
    DateTimeOffset TimestampUtc,
    double AxisSeconds,
    int PreviousLeaderCarIdx,
    int NewLeaderCarIdx);

internal sealed record DesignV2GapDriverChangeMarker(
    DateTimeOffset TimestampUtc,
    double AxisSeconds,
    int CarIdx,
    double GapSeconds,
    bool IsReference,
    string Label);

internal sealed record DesignV2GapEndpointLabel(
    string Text,
    PointF Point,
    Color Color,
    bool IsReference,
    bool IsClassLeader);

internal sealed record DesignV2PositionedGapEndpointLabel(
    DesignV2GapEndpointLabel Label,
    float Y);

internal sealed record DesignV2GapSeriesSelection(
    DesignV2GapCarRenderState State,
    double Alpha,
    bool IsStickyExit,
    bool IsStale,
    double DrawStartSeconds);

internal sealed record DesignV2GapDriverIdentity(
    int CarIdx,
    string DriverKey,
    string ShortLabel)
{
    public bool HasSameDriver(DesignV2GapDriverIdentity other)
    {
        return string.Equals(DriverKey, other.DriverKey, StringComparison.Ordinal);
    }
}

internal sealed class DesignV2GapCarRenderState(int carIdx)
{
    public int CarIdx { get; } = carIdx;

    public double LastSeenAxisSeconds { get; set; }

    public double LastGapSeconds { get; set; }

    public double? LastDesiredAxisSeconds { get; set; }

    public double? VisibleSinceAxisSeconds { get; set; }

    public bool IsCurrentlyDesired { get; set; }

    public bool IsReference { get; set; }

    public bool IsClassLeader { get; set; }

    public int? ClassPosition { get; set; }

    public double? DeltaSecondsToReference { get; set; }

    public int? CurrentLap { get; set; }

    public string? TireLabel { get; set; }

    public string? TireShortLabel { get; set; }

    public bool? TireIsWet { get; set; }

    public double? LastLapTimeSeconds { get; set; }

    public double? BestLapTimeSeconds { get; set; }

    public int? TrackSurface { get; set; }

    public bool? OnPitRoad { get; set; }

    public bool IsOnPitRoad { get; set; }

    public double? CurrentPitEntryAxisSeconds { get; set; }

    public int? CurrentPitEntryLap { get; set; }

    public double? LastPitDurationSeconds { get; set; }

    public int? LastPitLap { get; set; }

    public double? LastPitExitAxisSeconds { get; set; }
}

internal sealed record DesignV2GapReferenceContext(int? CarIdx, int? CarClass);

internal enum DesignV2GapWeatherCondition
{
    Unknown,
    Dry,
    Damp,
    Wet,
    DeclaredWet
}

internal sealed record DesignV2InputsBody(
    double? Throttle,
    double? Brake,
    double? Clutch,
    double? SteeringWheelAngle,
    double? SpeedMetersPerSecond,
    int? Gear,
    string SpeedText,
    string GearText,
    string SteeringText,
    bool BrakeAbsActive,
    bool ShowThrottleTrace,
    bool ShowBrakeTrace,
    bool ShowClutchTrace,
    bool IsAvailable,
    bool ShowThrottle,
    bool ShowBrake,
    bool ShowClutch,
    bool ShowSteering,
    bool ShowGear,
    bool ShowSpeed,
    bool HasGraph,
    bool HasRail,
    bool HasContent,
    IReadOnlyList<InputStateTracePoint> Trace) : DesignV2Body;

internal sealed record DesignV2RadarBody(
    bool IsAvailable,
    bool HasLeft,
    bool HasRight,
    IReadOnlyList<LiveSpatialCar> Cars,
    LiveMulticlassApproach? StrongestMulticlassApproach,
    bool ShowMulticlassWarning,
    bool PreviewVisible,
    CarRadarRenderModel RenderModel,
    double SurfaceAlpha) : DesignV2Body;

internal sealed record RadarSurfaceRenderState(
    CarRadarRenderModel RenderModel,
    double SurfaceAlpha);

internal sealed record DesignV2ChatBody(
    IReadOnlyList<DesignV2ChatRow> Rows) : DesignV2Body;

internal sealed record DesignV2FlagsBody(
    IReadOnlyList<FlagOverlayDisplayItem> Flags,
    bool IsWaiting,
    bool ManagedEnabled,
    bool SettingsOverlayActive) : DesignV2Body;

internal sealed record DesignV2TrackMapBody(TrackMapRenderModel RenderModel) : DesignV2Body;

internal sealed record DesignV2Column(
    string Label,
    int Width,
    ContentAlignment Alignment);

internal sealed record DesignV2TableRow(
    IReadOnlyList<string> Values,
    bool IsReference,
    bool IsClassHeader,
    DesignV2Evidence Evidence,
    string? ClassColorHex,
    string ClassHeaderTitle = "",
    string ClassHeaderDetail = "",
    int? RelativeLapDelta = null,
    IReadOnlyList<string?>? CellForegrounds = null);

internal sealed record DesignV2MetricRow(
    string Label,
    string Value,
    DesignV2Evidence Evidence)
{
    public IReadOnlyList<DesignV2MetricSegment> Segments { get; init; } = [];

    public string? RowColorHex { get; init; }
}

internal sealed record DesignV2MetricSegment(
    string Label,
    string Value,
    DesignV2Evidence Evidence,
    string? AccentHex = null,
    double? RotationDegrees = null);

internal sealed record DesignV2MetricSection(
    string Title,
    IReadOnlyList<DesignV2MetricRow> Rows);

internal sealed record DesignV2MetricGridSection(
    string Title,
    IReadOnlyList<string> Headers,
    IReadOnlyList<DesignV2MetricGridRow> Rows);

internal sealed record DesignV2MetricGridRow(
    string Label,
    IReadOnlyList<DesignV2MetricGridCell> Cells,
    DesignV2Evidence Evidence);

internal sealed record DesignV2MetricGridCell(
    string Value,
    DesignV2Evidence Evidence);

internal sealed record DesignV2ChatRow(
    string Author,
    string Message,
    DesignV2Evidence Evidence,
    string? AuthorColorHex = null,
    IReadOnlyList<string>? Metadata = null,
    IReadOnlyList<string>? Badges = null,
    IReadOnlyList<StreamChatDisplaySegment>? Segments = null);

internal sealed record DesignV2MeasuredChatRow(
    DesignV2ChatRow Row,
    float Height);

internal sealed record DesignV2InputRailLayout(
    IReadOnlyList<DesignV2InputRailItem> Items);

internal sealed record DesignV2InputRailItem(
    DesignV2InputRailItemKind Kind,
    RectangleF Bounds);

internal enum DesignV2InputRailItemKind
{
    Throttle,
    Brake,
    Clutch,
    SteeringWheel,
    Gear,
    Speed
}

internal sealed record DesignV2TrackMapMarker(
    int CarIdx,
    double LapDistPct,
    bool IsFocus,
    Color Color,
    string? PositionLabel);

internal readonly record struct DesignV2SectorProgressRange(double StartPct, double EndPct);

internal readonly record struct DesignV2TrackMapBoundaryTick(PointF Start, PointF End);

internal sealed record DesignV2TrackMapTransform(
    double MinX,
    double MaxY,
    double Scale,
    float Left,
    float Top,
    float Width,
    float Height)
{
    public static DesignV2TrackMapTransform? From(TrackMapDocument document, RectangleF bounds)
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
        return new DesignV2TrackMapTransform(
            minX,
            maxY,
            scale,
            bounds.Left + (bounds.Width - renderedWidth) / 2f,
            bounds.Top + (bounds.Height - renderedHeight) / 2f,
            renderedWidth,
            renderedHeight);
    }

    public PointF Map(TrackMapPoint point)
    {
        return new PointF(
            Left + (float)((point.X - MinX) * Scale),
            Top + (float)((MaxY - point.Y) * Scale));
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
