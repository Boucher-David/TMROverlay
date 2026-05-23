using TmrOverlay.App.History;
using TmrOverlay.App.Overlays;
using TmrOverlay.App.Overlays.Abstractions;
using TmrOverlay.App.Overlays.CarRadar;
using TmrOverlay.App.Overlays.Content;
using TmrOverlay.App.Overlays.FuelCalculator;
using TmrOverlay.App.Overlays.Flags;
using TmrOverlay.App.Overlays.GarageCover;
using TmrOverlay.App.Overlays.GapToLeader;
using TmrOverlay.App.Overlays.InputState;
using TmrOverlay.App.Overlays.PitService;
using TmrOverlay.App.Overlays.Relative;
using TmrOverlay.App.Overlays.SessionWeather;
using TmrOverlay.App.Overlays.SimpleTelemetry;
using TmrOverlay.App.Overlays.Standings;
using TmrOverlay.App.Overlays.StreamChat;
using TmrOverlay.App.Overlays.TrackMap;
using TmrOverlay.App.TrackMaps;
using TmrOverlay.Core.Fuel;
using TmrOverlay.Core.History;
using TmrOverlay.Core.Overlays;
using TmrOverlay.Core.Settings;
using TmrOverlay.Core.Telemetry.Live;
using TmrOverlay.Core.TrackMaps;

namespace TmrOverlay.App.Overlays.BrowserSources;

internal sealed class BrowserOverlayModelFactory
{
    private const int MaximumRelativeRows = 17;
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
    private const double TrackMapReloadIntervalSeconds = 10d;
    private readonly SessionHistoryQueryService _historyQueryService;
    private readonly TrackMapStore? _trackMapStore;
    private readonly StreamChatOverlaySource? _streamChatSource;
    private readonly SessionWeatherOverlayViewModel.StatefulBuilder _sessionWeatherBuilder;
    private readonly PitServiceOverlayViewModel.StatefulBuilder _pitServiceBuilder;
    private readonly TrackMapRenderModelBuilder _trackMapRenderBuilder = new();
    private readonly object _buildSync = new();
    private readonly object _gapSync = new();
    private readonly List<double> _gapPoints = [];
    private readonly Dictionary<int, List<BrowserGapTrendPoint>> _gapSeries = [];
    private readonly List<BrowserGapWeatherPoint> _gapWeather = [];
    private readonly List<BrowserGapLeaderChangeMarker> _gapLeaderChanges = [];
    private readonly List<BrowserGapDriverChangeMarker> _gapDriverChanges = [];
    private readonly Dictionary<int, BrowserGapCarRenderState> _gapCarRenderStates = [];
    private readonly Dictionary<int, BrowserGapDriverIdentity> _gapDriverIdentities = [];
    private readonly List<InputStateTracePoint> _inputTrace = [];
    private HistoricalComboIdentity? _cachedHistoryCombo;
    private SessionHistoryLookupResult? _cachedHistory;
    private DateTimeOffset _cachedHistoryAtUtc;
    private string? _cachedRadarCalibrationCarKey;
    private CarRadarCalibrationLookupResult? _cachedRadarCalibration;
    private DateTimeOffset _cachedRadarCalibrationAtUtc;
    private TrackMapDocument? _cachedTrackMap;
    private string? _cachedTrackMapIdentityKey;
    private bool _cachedTrackMapIncludeUserMaps;
    private DateTimeOffset _nextTrackMapReloadAtUtc;
    private BrowserGapReferenceContext? _lastGapReferenceContext;
    private long? _lastGapSequence;
    private double? _latestGapAxisSeconds;
    private double? _gapTrendStartAxisSeconds;
    private double? _lastGapLapReferenceSeconds;
    private double? _currentGapFuelStintStartAxisSeconds;
    private double? _lastGapFuelLevelLiters;
    private int? _lastGapDriversSoFar;
    private int? _lastGapClassLeaderCarIdx;

    public BrowserOverlayModelFactory(
        SessionHistoryQueryService historyQueryService,
        TrackMapStore? trackMapStore = null,
        StreamChatOverlaySource? streamChatSource = null)
    {
        _historyQueryService = historyQueryService;
        _trackMapStore = trackMapStore;
        _streamChatSource = streamChatSource;
        _sessionWeatherBuilder = SessionWeatherOverlayViewModel.CreateStatefulBuilder();
        _pitServiceBuilder = PitServiceOverlayViewModel.CreateStatefulBuilder();
    }

    public bool TryBuild(
        string overlayId,
        LiveTelemetrySnapshot snapshot,
        ApplicationSettings settings,
        DateTimeOffset now,
        out BrowserOverlayModelResponse response)
    {
        lock (_buildSync)
        {
            return TryBuildCore(overlayId, snapshot, settings, now, out response);
        }
    }

    private bool TryBuildCore(
        string overlayId,
        LiveTelemetrySnapshot snapshot,
        ApplicationSettings settings,
        DateTimeOffset now,
        out BrowserOverlayModelResponse response)
    {
        var modelSnapshot = snapshot with { Models = snapshot.CompleteModels() };
        var unitSystem = UnitSystem(settings);
        var sessionKind = OverlayAvailabilityEvaluator.CurrentSessionKind(modelSnapshot);
        if (!string.Equals(overlayId, GarageCoverOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase)
            && TryGetDefinition(overlayId, out var hiddenDefinition)
            && TryBuildHiddenProductModel(hiddenDefinition, modelSnapshot, settings, now, out response))
        {
            return true;
        }

        BrowserOverlayDisplayModel? model = null;
        if (string.Equals(overlayId, StandingsOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase))
        {
            model = BuildStandings(modelSnapshot, settings, now);
        }
        else if (string.Equals(overlayId, RelativeOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase))
        {
            model = BuildRelative(modelSnapshot, settings, now);
        }
        else if (string.Equals(overlayId, FuelCalculatorOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase))
        {
            model = BuildFuel(modelSnapshot, settings, unitSystem, now);
        }
        else if (string.Equals(overlayId, SessionWeatherOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase))
        {
            var overlay = FindOverlay(settings, SessionWeatherOverlayDefinition.Definition.Id);
            var viewModel = _sessionWeatherBuilder.Build(modelSnapshot, now, unitSystem, overlay);
            var headerItems = HeaderItems(overlay, modelSnapshot, viewModel.Status, SimpleChromeTone(viewModel.Tone));
            var shouldRender = overlay is null
                || OverlayContentSizing.HasRenderableContent(SessionWeatherOverlayDefinition.Definition, overlay, sessionKind);
            shouldRender &= HasSimpleTelemetryContent(viewModel);
            model = FromSimple(
                SessionWeatherOverlayDefinition.Definition.Id,
                viewModel,
                headerItems,
                string.Empty,
                shouldRender);
        }
        else if (string.Equals(overlayId, PitServiceOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase))
        {
            var overlay = FindOverlay(settings, PitServiceOverlayDefinition.Definition.Id);
            var viewModel = _pitServiceBuilder.Build(modelSnapshot, now, unitSystem, overlay);
            var headerItems = HeaderItems(overlay, modelSnapshot, PitServiceOverlayViewModel.HeaderStatus(viewModel.Status), SimpleChromeTone(viewModel.Tone));
            var shouldRender = overlay is null
                || OverlayContentSizing.HasRenderableContent(PitServiceOverlayDefinition.Definition, overlay, sessionKind);
            shouldRender &= HasSimpleTelemetryContent(viewModel);
            model = FromSimple(
                PitServiceOverlayDefinition.Definition.Id,
                viewModel,
                headerItems,
                SourceText(overlay, modelSnapshot, viewModel.Source),
                shouldRender);
        }
        else if (string.Equals(overlayId, InputStateOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase))
        {
            model = BuildInputState(modelSnapshot, settings, unitSystem, now);
        }
        else if (string.Equals(overlayId, CarRadarOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase))
        {
            model = BuildCarRadar(modelSnapshot, settings, now);
        }
        else if (string.Equals(overlayId, GapToLeaderOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase))
        {
            model = BuildGapToLeader(modelSnapshot, settings, now);
        }
        else if (string.Equals(overlayId, FlagsOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase))
        {
            model = BuildFlags(modelSnapshot, settings, now);
        }
        else if (string.Equals(overlayId, TrackMapOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase))
        {
            model = BuildTrackMap(modelSnapshot, settings, now);
        }
        else if (string.Equals(overlayId, GarageCoverOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase))
        {
            model = BuildGarageCover(modelSnapshot, settings, now);
        }
        else if (string.Equals(overlayId, StreamChatOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase))
        {
            model = BuildStreamChat(modelSnapshot, settings, now);
        }

        if (model is null)
        {
            response = null!;
            return false;
        }

        model = SuppressUnavailableRenderedContent(WithoutOrdinaryOverlayTitle(model));

        if (TryGetDefinition(overlayId, out var definition))
        {
            model = model with
            {
                RootOpacity = BrowserRootOpacity(definition, FindOverlay(settings, definition.Id))
            };
        }

        model = model with
        {
            EffectiveSettings = EffectiveSettingsEvidence(model, overlayId, settings, modelSnapshot, now)
        };

        response = new BrowserOverlayModelResponse(now, model);
        return true;
    }

    private BrowserOverlayDisplayModel BuildStandings(
        LiveTelemetrySnapshot snapshot,
        ApplicationSettings settings,
        DateTimeOffset now)
    {
        var browserSettings = StandingsBrowserSettings.From(
            settings,
            OverlayAvailabilityEvaluator.CurrentSessionKind(snapshot));
        var overlay = FindOverlay(settings, StandingsOverlayDefinition.Definition.Id);
        var viewModel = StandingsOverlayViewModel.From(
            snapshot,
            now,
            browserSettings.MaximumRows,
            browserSettings.OtherClassRowsPerClass,
            browserSettings.ClassSeparatorsEnabled);
        var columns = BrowserColumnsWithValidationCapacity(browserSettings.Columns);
        var rows = viewModel.Rows
            .Select(row => new BrowserOverlayDisplayRow(
                Cells: columns
                    .Select(column => StandingsCell(row, column.DataKey))
                    .ToArray(),
                IsReference: row.IsReference,
                IsClassHeader: row.IsClassHeader,
                IsPit: !string.IsNullOrWhiteSpace(row.Pit),
                IsPartial: row.IsPartial,
                IsPendingGrid: row.IsPendingGrid,
                CarClassColorHex: row.CarClassColorHex,
                HeaderTitle: row.IsClassHeader ? row.Driver : null,
                HeaderDetail: row.IsClassHeader ? ClassHeaderDetail(row.Gap, row.Interval) : null,
                CellTones: columns
                    .Select(column => StandingsCellTone(row, column.DataKey))
                    .ToArray()))
            .ToArray();
        var headerItems = HeaderItems(overlay, snapshot, viewModel.Status, viewModel.Rows.Count == 0 ? "waiting" : "info");

        return BrowserOverlayDisplayModel.Table(
            StandingsOverlayDefinition.Definition.Id,
            StandingsOverlayDefinition.Definition.DisplayName,
            BrowserStatus(headerItems, viewModel.Status),
            SourceText(overlay, snapshot, viewModel.Source),
            columns,
            rows,
            headerItems,
            ShouldRenderTable(columns, rows));
    }

    private static string StandingsCell(StandingsOverlayRowViewModel row, string dataKey)
    {
        return dataKey switch
        {
            OverlayContentColumnSettings.DataClassPosition => row.IsClassHeader ? string.Empty : row.ClassPosition,
            OverlayContentColumnSettings.DataCarNumber => row.IsClassHeader ? string.Empty : row.CarNumber,
            OverlayContentColumnSettings.DataDriver => row.Driver,
            OverlayContentColumnSettings.DataGap => row.IsClassHeader ? row.Gap : row.Gap,
            OverlayContentColumnSettings.DataInterval => row.Interval,
            OverlayContentColumnSettings.DataFastestLap => row.FastestLap,
            OverlayContentColumnSettings.DataLastLap => row.LastLap,
            OverlayContentColumnSettings.DataPit => row.Pit,
            _ => string.Empty
        };
    }

    private static string? StandingsCellTone(StandingsOverlayRowViewModel row, string dataKey)
    {
        if (string.Equals(dataKey, OverlayContentColumnSettings.DataFastestLap, StringComparison.Ordinal))
        {
            if (row.IsClassFastestLap)
            {
                return "best-lap";
            }

            return row.IsRecentCarBestLap ? "personal-best" : null;
        }

        if (string.Equals(dataKey, OverlayContentColumnSettings.DataLastLap, StringComparison.Ordinal))
        {
            if (row.IsClassFastestLastLap)
            {
                return "best-lap";
            }

            return row.IsRecentCarBestLastLap ? "personal-best" : null;
        }

        return null;
    }

    private BrowserOverlayDisplayModel BuildRelative(
        LiveTelemetrySnapshot snapshot,
        ApplicationSettings settings,
        DateTimeOffset now)
    {
        var browserSettings = RelativeBrowserSettings.From(
            settings,
            OverlayAvailabilityEvaluator.CurrentSessionKind(snapshot));
        var overlay = FindOverlay(settings, RelativeOverlayDefinition.Definition.Id);
        var viewModel = RelativeOverlayViewModel.From(
            snapshot,
            now,
            browserSettings.CarsAhead,
            browserSettings.CarsBehind);
        var columns = BrowserColumnsWithValidationCapacity(browserSettings.Columns);
        BrowserOverlayDisplayRow[] rows = viewModel.Rows.Count == 0
            ? []
            : viewModel.StableRows(
                    browserSettings.CarsAhead,
                    browserSettings.CarsBehind,
                    MaximumRelativeRows)
                .Select(row => row is null
                    ? BrowserPlaceholderRow(columns.Count)
                    : new BrowserOverlayDisplayRow(
                        Cells: columns
                            .Select(column => RelativeCell(row, column.DataKey))
                            .ToArray(),
                        IsReference: row.IsReference,
                        IsClassHeader: false,
                        IsPit: row.IsPit,
                        IsPartial: row.IsPartial,
                        IsPendingGrid: false,
                        CarClassColorHex: row.ClassColorHex,
                        HeaderTitle: null,
                        HeaderDetail: null,
                        RelativeLapDelta: row.LapDeltaToReference))
                .ToArray();
        var headerItems = HeaderItems(overlay, snapshot, viewModel.Status, viewModel.Rows.Count == 0 ? "waiting" : "info");

        return BrowserOverlayDisplayModel.Table(
            RelativeOverlayDefinition.Definition.Id,
            RelativeOverlayDefinition.Definition.DisplayName,
            BrowserStatus(headerItems, viewModel.Status),
            SourceText(overlay, snapshot, viewModel.Source),
            columns,
            rows,
            headerItems,
            ShouldRenderTable(columns, rows));
    }

    private static BrowserOverlayDisplayRow BrowserPlaceholderRow(int cellCount)
    {
        return new BrowserOverlayDisplayRow(
            Cells: Enumerable.Repeat(string.Empty, Math.Max(0, cellCount)).ToArray(),
            IsReference: false,
            IsClassHeader: false,
            IsPit: false,
            IsPartial: false,
            IsPendingGrid: false,
            CarClassColorHex: null,
            HeaderTitle: null,
            HeaderDetail: null,
            IsPlaceholder: true);
    }

    private static bool ShouldRenderTable(
        IReadOnlyList<OverlayContentBrowserColumn> columns,
        IReadOnlyList<BrowserOverlayDisplayRow> rows)
    {
        return columns.Count > 0 && rows.Count > 0;
    }

    private static string RelativeCell(RelativeOverlayRowViewModel row, string dataKey)
    {
        return dataKey switch
        {
            OverlayContentColumnSettings.DataRelativePosition => row.Position,
            OverlayContentColumnSettings.DataDriver => row.Driver,
            OverlayContentColumnSettings.DataGap => row.Gap,
            OverlayContentColumnSettings.DataPit => row.IsPit ? "PIT" : string.Empty,
            _ => string.Empty
        };
    }

    private BrowserOverlayDisplayModel BuildFuel(
        LiveTelemetrySnapshot snapshot,
        ApplicationSettings settings,
        string unitSystem,
        DateTimeOffset now)
    {
        var overlay = FindOverlay(settings, FuelCalculatorOverlayDefinition.Definition.Id);
        var effectiveOverlay = OverlayOrDefault(settings, FuelCalculatorOverlayDefinition.Definition);
        const bool showFooter = false;
        var strategyModel = LiveFuelStrategyModel.From(snapshot, now, LookupHistory);
        if (!strategyModel.IsAvailable)
        {
            var waitingHeaderItems = HeaderItems(overlay, snapshot, strategyModel.Status, "waiting");
            return BrowserOverlayDisplayModel.MetricRows(
                FuelCalculatorOverlayDefinition.Definition.Id,
                FuelCalculatorOverlayDefinition.Definition.DisplayName,
                BrowserStatus(waitingHeaderItems, strategyModel.Status),
                SourceText(overlay, snapshot, "source: waiting"),
                [],
                waitingHeaderItems,
                shouldRender: false) with
            {
                FuelStrategyEvidence = new BrowserOverlayFuelStrategyEvidence("unavailable", SuccessCopyRequiresMeasuredNeed: true)
            };
        }

        var viewModel = FuelCalculatorViewModel.From(
            strategyModel,
            showAdvice: false,
            unitSystem,
            maximumRows: FuelVisibleRowsForHeight(
                effectiveOverlay.Height > 0
                    ? effectiveOverlay.Height
                    : FuelCalculatorOverlayDefinition.Definition.DefaultHeight,
                showFooter),
            contentSettings: effectiveOverlay);
        var metrics = MetricSectionsFrom(viewModel.MetricSections)
            .SelectMany(section => section.Rows)
            .ToArray();
        var headerItems = HeaderItems(overlay, snapshot, viewModel.Status, FuelChromeTone(strategyModel.Strategy));

        return BrowserOverlayDisplayModel.MetricRows(
            FuelCalculatorOverlayDefinition.Definition.Id,
            FuelCalculatorOverlayDefinition.Definition.DisplayName,
            BrowserStatus(headerItems, viewModel.Status),
            SourceText(overlay, snapshot, viewModel.Source),
            metrics,
            headerItems,
            metricSections: MetricSectionsFrom(viewModel.MetricSections),
            shouldRender: metrics.Length > 0 || viewModel.MetricSections.Any(section => section.Rows.Count > 0)) with
        {
            FuelStrategyEvidence = FuelStrategyEvidence(strategyModel.Strategy)
        };
    }

    private static BrowserOverlayDisplayModel BuildFlags(
        LiveTelemetrySnapshot snapshot,
        ApplicationSettings settings,
        DateTimeOffset now)
    {
        var overlay = FindOverlay(settings, FlagsOverlayDefinition.Definition.Id);
        var viewModel = FlagsOverlayViewModel.ForDisplay(snapshot, now);
        var flags = viewModel.Flags
            .Where(flag => IsFlagCategoryEnabled(overlay, flag.Category))
            .Select(BrowserFlagDisplayItem.From)
            .ToArray();
        var headerItems = HeaderItems(overlay, snapshot, viewModel.Status, SimpleChromeTone(viewModel.Tone));

        return new BrowserOverlayDisplayModel(
            FlagsOverlayDefinition.Definition.Id,
            FlagsOverlayDefinition.Definition.DisplayName,
            BrowserStatus(headerItems, viewModel.Status),
            SourceText(overlay, snapshot, "source: session flags telemetry"),
            "flags",
            [],
            [],
            [],
            [],
            headerItems,
            Flags: new BrowserFlagsModel(flags, viewModel.IsWaiting),
            ShouldRender: !viewModel.IsWaiting && flags.Length > 0);
    }

    private static BrowserOverlayDisplayModel FromSimple(
        string overlayId,
        SimpleTelemetryOverlayViewModel viewModel,
        IReadOnlyList<BrowserOverlayHeaderItem>? headerItems = null,
        string? source = null,
        bool shouldRender = true)
    {
        headerItems ??= [];
        return BrowserOverlayDisplayModel.MetricRows(
            overlayId,
            viewModel.Title,
            BrowserStatus(headerItems, viewModel.Status),
            source ?? viewModel.Source,
            viewModel.Rows
                .Select(row => new BrowserOverlayMetricRow(
                    row.Label,
                    row.Value,
                    ToneName(row.Tone))
                {
                    Segments = BrowserSegmentsFrom(row.Segments),
                    RowColorHex = row.RowColorHex
                })
                .ToArray(),
            headerItems,
            GridSectionsFrom(viewModel.Sections),
            MetricSectionsFrom(viewModel.MetricSections),
            shouldRender);
    }

    private static bool HasSimpleTelemetryContent(SimpleTelemetryOverlayViewModel viewModel)
    {
        return viewModel.Rows.Count > 0
            || viewModel.MetricSections.Any(section => section.Rows.Count > 0)
            || viewModel.Sections.Any(section => section.Rows.Count > 0);
    }

    private static bool CanRenderGapGraph(BrowserGapGraph? graph)
    {
        if (graph is null)
        {
            return false;
        }

        return graph.ShowGraph && graph.Series.Count > 0
            || graph.ShowTrendMetrics && graph.TrendMetrics.Any(metric => !string.Equals(metric.State, "unavailable", StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<BrowserOverlayMetricSection> MetricSectionsFrom(
        IReadOnlyList<SimpleTelemetryMetricSectionViewModel> sections)
    {
        return sections
            .Where(section => section.Rows.Count > 0)
            .Select(section => new BrowserOverlayMetricSection(
                section.Title,
                section.Rows.Select(row => new BrowserOverlayMetricRow(
                    row.Label,
                    row.Value,
                    ToneName(row.Tone))
                {
                    Segments = BrowserSegmentsFrom(row.Segments),
                    RowColorHex = row.RowColorHex
                }).ToArray()))
            .ToArray();
    }

    private static IReadOnlyList<BrowserOverlayMetricSegment> BrowserSegmentsFrom(
        IReadOnlyList<SimpleTelemetryMetricSegmentViewModel> segments)
    {
        return segments
            .Select(segment => new BrowserOverlayMetricSegment(
                segment.Label,
                segment.Value,
                ToneName(segment.Tone),
                segment.AccentHex,
                segment.RotationDegrees))
            .ToArray();
    }

    private static IReadOnlyList<BrowserOverlayGridSection> GridSectionsFrom(
        IReadOnlyList<SimpleTelemetryGridSectionViewModel> sections)
    {
        return sections
            .Where(section => section.Rows.Count > 0)
            .Select(section => new BrowserOverlayGridSection(
                section.Title,
                section.Headers,
                section.Rows.Select(row => new BrowserOverlayGridRow(
                    row.Label,
                    row.Cells.Select(cell => new BrowserOverlayGridCell(
                        cell.Value,
                        ToneName(cell.Tone))).ToArray(),
                    ToneName(row.Tone))).ToArray()))
            .ToArray();
    }

    private BrowserOverlayDisplayModel BuildGapToLeader(LiveTelemetrySnapshot snapshot, ApplicationSettings settings, DateTimeOffset now)
    {
        var overlay = FindOverlay(settings, GapToLeaderOverlayDefinition.Definition.Id);
        var sessionKind = OverlayAvailabilityEvaluator.CurrentSessionKind(snapshot);
        var isRace = OverlayAvailabilityEvaluator.NormalizeSessionKind(sessionKind) == OverlaySessionKind.Race;
        var viewModel = GapToLeaderOverlayViewModel.From(snapshot, now);
        var gap = viewModel.Gap;
        var headerItems = HeaderItems(overlay, snapshot, viewModel.Status, gap.HasData ? "info" : "waiting");
        var showGraph = GapGraphEnabled(overlay, sessionKind) && GapWindowEnabled(overlay);
        var showTrendMetrics = GapAnyTrendMetricEnabled(overlay, sessionKind);
        var hasLiveGapData = viewModel.IsAvailable && gap.HasData;
        var canBuildGraph = isRace && hasLiveGapData && GapWindowEnabled(overlay) && (showGraph || showTrendMetrics);
        var shouldRender = false;
        BrowserGapGraph? graph;
        IReadOnlyList<double> points;
        lock (_gapSync)
        {
            if (isRace && hasLiveGapData)
            {
                RecordGapSnapshot(snapshot, gap, settings);
            }

            if (isRace
                && hasLiveGapData
                && viewModel.FocusedTrendPointSeconds is { } seconds
                && ShouldAcceptGapPoint(snapshot, seconds))
            {
                _gapPoints.Add(seconds);
                if (_gapPoints.Count > 120)
                {
                    _gapPoints.RemoveRange(0, _gapPoints.Count - 120);
                }
            }

            graph = canBuildGraph ? BuildBrowserGapGraph(settings, overlay, sessionKind, showGraph, showTrendMetrics) : null;
            shouldRender = CanRenderGapGraph(graph);
            points = graph?.ShowGraph == true && graph.SelectedSeriesCount > 0
                ? _gapPoints.ToArray()
                : Array.Empty<double>();
        }

        return new BrowserOverlayDisplayModel(
            GapToLeaderOverlayDefinition.Definition.Id,
            viewModel.Title,
            BrowserStatus(headerItems, viewModel.Status),
            SourceText(overlay, snapshot, viewModel.Source),
            "graph",
            Columns: [],
            Rows: [],
            Metrics: [],
            Points: points,
            HeaderItems: headerItems,
            Graph: graph,
            ShouldRender: shouldRender);
    }

    private BrowserOverlayDisplayModel BuildCarRadar(
        LiveTelemetrySnapshot snapshot,
        ApplicationSettings settings,
        DateTimeOffset now)
    {
        var overlay = FindOverlay(settings, CarRadarOverlayDefinition.Definition.Id);
        var calibration = CarRadarCalibrationProfile.FromHistory(LookupCarRadarCalibration(snapshot.Models.Session.Combo));
        var showMulticlassWarning = overlay is null
            || OverlayContentColumnSettings.ContentEnabledForSession(
                overlay,
                OverlayOptionKeys.RadarMulticlassWarning,
                defaultEnabled: true,
                OverlayAvailabilityEvaluator.CurrentSessionKind(snapshot));
        var multiclassWarningRangeSeconds = overlay?.GetIntegerOption(
            OverlayOptionKeys.RadarMulticlassWarningSeconds,
            CarRadarOverlayViewModel.DefaultMulticlassWarningRangeSeconds,
            CarRadarOverlayViewModel.MinimumMulticlassWarningRangeSeconds,
            CarRadarOverlayViewModel.MaximumMulticlassWarningRangeSeconds)
            ?? CarRadarOverlayViewModel.DefaultMulticlassWarningRangeSeconds;
        var radarVisibilitySeconds = overlay?.GetIntegerOption(
            OverlayOptionKeys.RadarVisibilitySeconds,
            CarRadarOverlayViewModel.DefaultRadarVisibilitySeconds,
            CarRadarOverlayViewModel.MinimumRadarVisibilitySeconds,
            CarRadarOverlayViewModel.MaximumRadarVisibilitySeconds)
            ?? CarRadarOverlayViewModel.DefaultRadarVisibilitySeconds;
        var viewModel = CarRadarOverlayViewModel.From(
            snapshot,
            now,
            previewVisible: false,
            showMulticlassWarning: showMulticlassWarning,
            calibrationProfile: calibration,
            multiclassWarningRangeSeconds: multiclassWarningRangeSeconds,
            radarVisibilitySeconds: radarVisibilitySeconds);
        var headerItems = HeaderItems(overlay, snapshot, viewModel.Status, viewModel.IsAvailable ? "info" : "waiting");
        var renderModel = CarRadarRenderModel.FromViewModel(viewModel, calibration);
        return new BrowserOverlayDisplayModel(
            CarRadarOverlayDefinition.Definition.Id,
            viewModel.Title,
            BrowserStatus(headerItems, viewModel.Status),
            SourceText(overlay, snapshot, viewModel.Source),
            "car-radar",
            Columns: [],
            Rows: [],
            Metrics: [],
            Points: [],
            HeaderItems: headerItems,
            CarRadar: new BrowserCarRadarModel(
                viewModel.IsAvailable,
                viewModel.HasCarLeft,
                viewModel.HasCarRight,
                viewModel.Cars,
                viewModel.StrongestMulticlassApproach,
                viewModel.ShowMulticlassWarning,
                viewModel.MulticlassWarningRangeSeconds,
                viewModel.RadarVisibilitySeconds,
                viewModel.PreviewVisible,
                viewModel.HasCurrentSignal,
                renderModel),
            ShouldRender: renderModel.ShouldRender);
    }

    private BrowserOverlayDisplayModel BuildInputState(
        LiveTelemetrySnapshot snapshot,
        ApplicationSettings settings,
        string unitSystem,
        DateTimeOffset now)
    {
        var overlay = FindOverlay(settings, InputStateOverlayDefinition.Definition.Id)
            ?? new OverlaySettings { Id = InputStateOverlayDefinition.Definition.Id };
        var inputModel = InputStateRenderModelBuilder.Build(snapshot, now, unitSystem, overlay, _inputTrace);
        return new BrowserOverlayDisplayModel(
            InputStateOverlayDefinition.Definition.Id,
            InputStateOverlayDefinition.Definition.DisplayName,
            inputModel.Status,
            string.Empty,
            "inputs",
            Columns: [],
            Rows: [],
            Metrics: [],
            Points: [],
            HeaderItems: Array.Empty<BrowserOverlayHeaderItem>(),
            Inputs: inputModel,
            ShouldRender: inputModel.HasContent);
    }

    private BrowserOverlayDisplayModel BuildTrackMap(
        LiveTelemetrySnapshot snapshot,
        ApplicationSettings settings,
        DateTimeOffset now)
    {
        var overlay = OverlayOrDefault(settings, TrackMapOverlayDefinition.Definition);
        var sessionKind = OverlayAvailabilityEvaluator.CurrentSessionKind(snapshot);
        var includeUserMaps = OverlayContentColumnSettings.ContentEnabledForSession(
            overlay,
            OverlayOptionKeys.TrackMapBuildFromTelemetry,
            defaultEnabled: true,
            sessionKind);
        var trackMap = ReadTrackMap(snapshot.Context.Track, includeUserMaps, now);
        var viewModel = TrackMapOverlayViewModel.From(snapshot, now, overlay, trackMap);
        var renderModel = _trackMapRenderBuilder.Build(viewModel, now);
        var status = viewModel.Status;
        var headerItems = HeaderItems(overlay, snapshot, status, viewModel.IsAvailable ? "info" : "waiting");
        var shouldRender = viewModel.IsAvailable && renderModel.Markers.Count > 0;
        return new BrowserOverlayDisplayModel(
            TrackMapOverlayDefinition.Definition.Id,
            viewModel.Title,
            BrowserStatus(headerItems, status),
            SourceText(overlay, snapshot, viewModel.Source),
            "track-map",
            Columns: [],
            Rows: [],
            Metrics: [],
            Points: [],
            HeaderItems: headerItems,
            TrackMap: new BrowserTrackMapModel(
                viewModel.Markers,
                viewModel.Sectors,
                viewModel.ShowSectorBoundaries,
                viewModel.InternalOpacity,
                viewModel.IncludeUserMaps,
                renderModel),
            ShouldRender: shouldRender);
    }

    private TrackMapDocument? ReadTrackMap(
        HistoricalTrackIdentity track,
        bool includeUserMaps,
        DateTimeOffset now)
    {
        if (_trackMapStore is null)
        {
            return null;
        }

        var identity = TrackMapIdentity.From(track);
        var identityChanged = !string.Equals(identity.Key, _cachedTrackMapIdentityKey, StringComparison.Ordinal)
            || includeUserMaps != _cachedTrackMapIncludeUserMaps;
        if (!identityChanged && now < _nextTrackMapReloadAtUtc)
        {
            return _cachedTrackMap;
        }

        _cachedTrackMapIdentityKey = identity.Key;
        _cachedTrackMapIncludeUserMaps = includeUserMaps;
        _nextTrackMapReloadAtUtc = now.AddSeconds(TrackMapReloadIntervalSeconds);
        _cachedTrackMap = _trackMapStore.TryReadBest(track, includeUserMaps);
        return _cachedTrackMap;
    }

    private static BrowserOverlayDisplayModel BuildGarageCover(
        LiveTelemetrySnapshot snapshot,
        ApplicationSettings settings,
        DateTimeOffset now)
    {
        var viewModel = GarageCoverViewModel.From(settings, snapshot, now);
        var overlay = FindOverlay(settings, GarageCoverOverlayDefinition.Definition.Id);
        var headerItems = HeaderItems(overlay, snapshot, viewModel.Status, viewModel.ShouldCover ? "info" : "normal");
        return new BrowserOverlayDisplayModel(
            GarageCoverOverlayDefinition.Definition.Id,
            viewModel.Title,
            BrowserStatus(headerItems, viewModel.Status),
            SourceText(overlay, snapshot, viewModel.Source),
            "garage-cover",
            Columns: [],
            Rows: [],
            Metrics: [],
            Points: [],
            HeaderItems: headerItems,
            GarageCover: new BrowserGarageCoverModel(
                viewModel.ShouldCover,
                viewModel.BrowserSettings,
                viewModel.Detection),
            ShouldRender: viewModel.ShouldCover);
    }

    private BrowserOverlayDisplayModel BuildStreamChat(
        LiveTelemetrySnapshot snapshot,
        ApplicationSettings settings,
        DateTimeOffset now)
    {
        var browserSettings = StreamChatOverlayViewModel.BrowserSettingsFrom(settings);
        var viewModel = _streamChatSource?.Snapshot(browserSettings)
            ?? StreamChatOverlayViewModel.From(
                StreamChatOverlayViewModel.InitialStatus(browserSettings),
                [StreamChatOverlayViewModel.InitialMessage(browserSettings)],
                contentOptions: browserSettings.ContentOptions);
        var headerItems = Array.Empty<BrowserOverlayHeaderItem>();
        return new BrowserOverlayDisplayModel(
            StreamChatOverlayDefinition.Definition.Id,
            viewModel.Title,
            BrowserStatus(headerItems, viewModel.Status),
            string.Empty,
            "stream-chat",
            Columns: [],
            Rows: [],
            Metrics: [],
            Points: [],
            HeaderItems: headerItems,
            StreamChat: new BrowserStreamChatModel(
                browserSettings,
                viewModel.Rows.Select(message => BrowserStreamChatMessage.From(message, browserSettings.ContentOptions)).ToArray()));
    }

    private bool ShouldAcceptGapPoint(LiveTelemetrySnapshot snapshot, double seconds)
    {
        if (!IsFinite(seconds) || seconds < 0d)
        {
            return false;
        }

        if (_gapPoints.Count == 0)
        {
            return true;
        }

        var previous = _gapPoints[^1];
        var lapReferenceSeconds = GapToLeaderLiveModelAdapter.SelectLapReferenceSeconds(snapshot);
        var maximumJump = Math.Max(30d, Math.Min(180d, (lapReferenceSeconds ?? 90d) * 0.5d));
        return Math.Abs(seconds - previous) <= maximumJump;
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

    private void RecordGapSnapshot(LiveTelemetrySnapshot snapshot, LiveLeaderGapSnapshot gap, ApplicationSettings settings)
    {
        if (_lastGapSequence == snapshot.Sequence)
        {
            return;
        }

        var modelSnapshot = snapshot with { Models = snapshot.CompleteModels() };
        _lastGapSequence = snapshot.Sequence;
        var timestamp = snapshot.LastUpdatedAtUtc
            ?? DateTimeOffset.UtcNow;
        var axisSeconds = SelectGapAxisSeconds(timestamp, modelSnapshot.Models.Session.SessionTimeSeconds);
        _latestGapAxisSeconds = axisSeconds;
        if (_gapTrendStartAxisSeconds is null || axisSeconds < _gapTrendStartAxisSeconds.Value)
        {
            _gapTrendStartAxisSeconds = axisSeconds;
        }

        var lapReferenceSeconds = GapToLeaderLiveModelAdapter.SelectLapReferenceSeconds(modelSnapshot);
        var context = SelectGapReferenceContext(modelSnapshot);
        if (_lastGapReferenceContext is not null && _lastGapReferenceContext != context)
        {
            ResetGapReferenceContext(axisSeconds);
        }

        _lastGapReferenceContext = context;
        _lastGapLapReferenceSeconds = lapReferenceSeconds;
        RecordGapFuelStint(modelSnapshot, axisSeconds);
        RecordGapWeather(modelSnapshot, axisSeconds);
        RecordGapDriverChangeMarkers(modelSnapshot, gap, timestamp, axisSeconds, lapReferenceSeconds);
        RecordGapLeaderChange(gap, timestamp, axisSeconds);
        foreach (var car in gap.ClassCars)
        {
            if (BrowserGapSeconds(car, lapReferenceSeconds) is not { } gapSeconds)
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
            var point = new BrowserGapTrendPoint(
                timestamp,
                axisSeconds,
                gapSeconds,
                car.CarIdx,
                car.IsReferenceCar,
                car.IsClassLeader,
                car.ClassPosition,
                GapToLeaderPresentationRules.CompletedLapFromCurrentLap(car.CurrentLap),
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

        UpdateGapCarRenderStates(modelSnapshot, gap, settings, axisSeconds, lapReferenceSeconds);
        PruneGapSeries(axisSeconds);
    }

    private void ResetGapReferenceContext(double axisSeconds)
    {
        _gapPoints.Clear();
        _gapSeries.Clear();
        _gapWeather.Clear();
        _gapDriverChanges.Clear();
        _gapLeaderChanges.Clear();
        _gapCarRenderStates.Clear();
        _gapTrendStartAxisSeconds = axisSeconds;
        _lastGapClassLeaderCarIdx = null;
        _currentGapFuelStintStartAxisSeconds = null;
        _lastGapFuelLevelLiters = null;
    }

    private void RecordGapWeather(LiveTelemetrySnapshot snapshot, double axisSeconds)
    {
        var condition = SelectGapWeatherCondition(snapshot);
        if (_gapWeather.Count > 0 && Math.Abs(_gapWeather[^1].AxisSeconds - axisSeconds) < 0.001d)
        {
            _gapWeather[^1] = new BrowserGapWeatherPoint(axisSeconds, condition);
        }
        else
        {
            _gapWeather.Add(new BrowserGapWeatherPoint(axisSeconds, condition));
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

    private void RecordGapDriverChangeMarkers(
        LiveTelemetrySnapshot snapshot,
        LiveLeaderGapSnapshot gap,
        DateTimeOffset timestamp,
        double axisSeconds,
        double? lapReferenceSeconds)
    {
        if (snapshot.Models.RaceEvents.DriversSoFar is { } driversSoFar && driversSoFar > 0)
        {
            if (_lastGapDriversSoFar is { } previousDrivers)
            {
                if (driversSoFar < previousDrivers)
                {
                    _lastGapDriversSoFar = driversSoFar;
                }
                else if (driversSoFar > previousDrivers
                    && ReferenceUsesPlayerCar(snapshot)
                    && gap.ClassCars.FirstOrDefault(car => car.IsReferenceCar) is { } reference
                    && BrowserGapSeconds(reference, lapReferenceSeconds) is { } gapSeconds)
                {
                    AddGapDriverChangeMarker(new BrowserGapDriverChangeMarker(
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

        foreach (var driver in snapshot.Context.Drivers)
        {
            if (ToGapDriverIdentity(driver) is not { } identity)
            {
                continue;
            }

            if (_gapDriverIdentities.TryGetValue(identity.CarIdx, out var previous)
                && !previous.HasSameDriver(identity)
                && gap.ClassCars.FirstOrDefault(car => car.CarIdx == identity.CarIdx) is { } car
                && BrowserGapSeconds(car, lapReferenceSeconds) is { } gapSeconds)
            {
                AddGapDriverChangeMarker(new BrowserGapDriverChangeMarker(
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

    private void AddGapDriverChangeMarker(BrowserGapDriverChangeMarker marker)
    {
        if (_gapDriverChanges.Any(existing =>
                existing.CarIdx == marker.CarIdx
                && Math.Abs(existing.AxisSeconds - marker.AxisSeconds) < 5d))
        {
            return;
        }

        _gapDriverChanges.Add(marker);
        if (_gapDriverChanges.Count > GapMaxDriverChangeMarkers)
        {
            _gapDriverChanges.RemoveRange(0, _gapDriverChanges.Count - GapMaxDriverChangeMarkers);
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
            _gapLeaderChanges.Add(new BrowserGapLeaderChangeMarker(timestamp, axisSeconds, previousLeader, leaderCarIdx));
        }

        _lastGapClassLeaderCarIdx = leaderCarIdx;
    }

    private void UpdateGapCarRenderStates(
        LiveTelemetrySnapshot snapshot,
        LiveLeaderGapSnapshot gap,
        ApplicationSettings settings,
        double axisSeconds,
        double? lapReferenceSeconds)
    {
        var desiredCarIds = SelectDesiredGapCarIds(gap.ClassCars, settings, lapReferenceSeconds);
        foreach (var state in _gapCarRenderStates.Values)
        {
            state.IsReference = false;
            state.IsClassLeader = false;
        }

        foreach (var car in gap.ClassCars)
        {
            if (BrowserGapSeconds(car, lapReferenceSeconds) is not { } gapSeconds)
            {
                continue;
            }

            if (!_gapCarRenderStates.TryGetValue(car.CarIdx, out var state))
            {
                state = new BrowserGapCarRenderState(car.CarIdx);
                _gapCarRenderStates[car.CarIdx] = state;
            }

            var wasVisible = ShouldKeepGapSeriesVisible(state, axisSeconds);
            state.LastSeenAxisSeconds = axisSeconds;
            state.LastGapSeconds = gapSeconds;
            state.IsReference = car.IsReferenceCar;
            state.IsClassLeader = car.IsClassLeader;
            state.ClassPosition = car.ClassPosition;
            state.DeltaSecondsToReference = car.DeltaSecondsToReference;
            state.GapLapsToLeader = NormalizedBrowserGapLaps(car, lapReferenceSeconds);
            state.CurrentLap = car.CurrentLap;
            var timingRow = BrowserGapTimingRow(snapshot.Models.Timing, car.CarIdx);
            state.LastLapTimeSeconds = timingRow?.LastLapTimeSeconds;
            state.BestLapTimeSeconds = timingRow?.BestLapTimeSeconds;
            state.TrackSurface = timingRow?.TrackSurface;
            state.OnPitRoad = timingRow?.OnPitRoad;
            var tire = GapTireCompound(snapshot.Models.TireCompounds, car.CarIdx);
            state.TireLabel = tire?.Label;
            state.TireShortLabel = tire?.ShortLabel;
            state.TireIsWet = tire?.IsWet;
            UpdateBrowserGapPitState(state, car, axisSeconds);
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

    private static LiveTimingRow? BrowserGapTimingRow(LiveTimingModel timing, int carIdx)
    {
        return timing.ClassRows.FirstOrDefault(row => row.CarIdx == carIdx)
            ?? timing.OverallRows.FirstOrDefault(row => row.CarIdx == carIdx)
            ?? (timing.FocusRow?.CarIdx == carIdx ? timing.FocusRow : null)
            ?? (timing.PlayerRow?.CarIdx == carIdx ? timing.PlayerRow : null);
    }

    private static void UpdateBrowserGapPitState(
        BrowserGapCarRenderState state,
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

    private HashSet<int> SelectDesiredGapCarIds(
        IReadOnlyList<LiveClassGapCar> cars,
        ApplicationSettings settings,
        double? lapReferenceSeconds)
    {
        var selected = new HashSet<int>();
        var reference = cars.FirstOrDefault(car =>
            car.IsReferenceCar
            && BrowserGapSeconds(car, lapReferenceSeconds) is not null);
        var anchor = reference;
        var referenceCanAnchor = anchor is not null && !IsLappedGraphGap(anchor, lapReferenceSeconds);
        foreach (var car in cars.Where(car => car.IsClassLeader || (referenceCanAnchor && car.IsReferenceCar)))
        {
            selected.Add(car.CarIdx);
        }

        var overlay = FindOverlay(settings, GapToLeaderOverlayDefinition.Definition.Id);
        var aheadCount = overlay?.GetIntegerOption(OverlayOptionKeys.GapCarsAhead, defaultValue: 5, minimum: 0, maximum: 12) ?? 5;
        var behindCount = overlay?.GetIntegerOption(OverlayOptionKeys.GapCarsBehind, defaultValue: 5, minimum: 0, maximum: 12) ?? 5;
        if (!referenceCanAnchor || anchor is null)
        {
            foreach (var car in cars
                .Where(car => !car.IsClassLeader && !IsLappedGraphGap(car, lapReferenceSeconds))
                .OrderBy(car => car.ClassPosition ?? int.MaxValue)
                .ThenBy(car => BrowserGapSeconds(car, lapReferenceSeconds) ?? double.MaxValue)
                .Take(Math.Max(1, behindCount)))
            {
                selected.Add(car.CarIdx);
            }

            if (selected.Count <= 1)
            {
                foreach (var car in cars
                    .Where(car => !car.IsClassLeader)
                    .OrderBy(car => BrowserGapSeconds(car, lapReferenceSeconds) ?? double.MaxValue)
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

    private BrowserGapGraph? BuildBrowserGapGraph(
        ApplicationSettings settings,
        OverlaySettings? overlay,
        OverlaySessionKind? sessionKind,
        bool showGraph,
        bool showTrendMetrics)
    {
        var selectedSeries = showGraph ? SelectGapSeries() : [];
        if (!HasGapComparisonSeries(selectedSeries))
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

        var comparisonLabel = BrowserGapComparisonLabel();
        var trendMetrics = showTrendMetrics
            ? BuildBrowserGapTrendMetrics()
                .Where(metric => GapTrendMetricEnabled(overlay, metric.Label, sessionKind))
                .ToArray()
            : [];
        var activeThreat = ActiveBrowserGapThreat(trendMetrics);
        var threatCarIdx = activeThreat?.Chaser?.CarIdx;
        var series = selectedSeries
            .Select((selection, index) =>
            {
                var baseColor = BrowserGapSeriesBaseColor(selection.State, index, threatCarIdx);
                var renderedColor = BrowserGapRenderedColor(baseColor, selection.Alpha * BrowserGapSeriesAlphaMultiplier(selection.State, threatCarIdx));
                return new BrowserGapSeries(
                    selection.State.CarIdx,
                    selection.State.IsReference,
                    selection.State.IsClassLeader,
                    selection.State.ClassPosition,
                    selection.Alpha,
                    selection.IsStickyExit,
                    selection.IsStale,
                    baseColor,
                    renderedColor,
                    PointsForGapCar(selection.State.CarIdx, Math.Max(startSeconds, selection.DrawStartSeconds), endSeconds));
            })
            .Where(series => series.Points.Count > 0)
            .ToArray();
        var scale = SelectBrowserGapScale(selectedSeries, startSeconds, endSeconds);
        return new BrowserGapGraph(
            series,
            _gapWeather.Where(point => point.AxisSeconds >= startSeconds && point.AxisSeconds <= endSeconds).ToArray(),
            _gapLeaderChanges.Where(marker => marker.AxisSeconds >= startSeconds && marker.AxisSeconds <= endSeconds).ToArray(),
            _gapDriverChanges.Where(marker => marker.AxisSeconds >= startSeconds && marker.AxisSeconds <= endSeconds).ToArray(),
            startSeconds,
            Math.Max(endSeconds, startSeconds + 1d),
            Math.Max(1d, scale.MaxGapSeconds),
            _lastGapLapReferenceSeconds,
            selectedSeries.Count,
            trendMetrics,
            activeThreat,
            threatCarIdx,
            GapMetricDeadbandSeconds(),
            comparisonLabel,
            scale,
            showGraph && selectedSeries.Count > 0,
            showTrendMetrics);
    }

    private IReadOnlyList<BrowserGapTrendMetric> BuildBrowserGapTrendMetrics()
    {
        if (_lastGapLapReferenceSeconds is not { } lapReferenceSeconds
            || !IsValidLapReference(lapReferenceSeconds)
            || _gapCarRenderStates.Values.FirstOrDefault(state => state.IsReference) is not { } referenceState)
        {
            return DefaultBrowserGapTrendMetrics("unavailable");
        }

        var latest = _latestGapAxisSeconds ?? 0d;
        var fiveLapMetric = BuildBrowserGapTrendMetric("5L", lapReferenceSeconds * 5d, 5d, latest, referenceState);
        var tenLapMetric = BuildBrowserGapTrendMetric("10L", lapReferenceSeconds * 10d, 10d, latest, referenceState);
        var paceMetrics = new[]
        {
            fiveLapMetric,
            tenLapMetric
        };
        var threatCarIdx = ActiveBrowserGapThreat(paceMetrics)?.Chaser?.CarIdx;
        var extraMetrics = BuildBrowserGapExtraTrendMetrics(referenceState, threatCarIdx);
        return extraMetrics.Take(1)
            .Concat(paceMetrics)
            .Concat(BuildBrowserGapPitTrendMetrics(referenceState, threatCarIdx))
            .Append(BuildBrowserGapStintTrendMetric(referenceState, threatCarIdx))
            .Append(BuildBrowserGapTireTrendMetric(referenceState, threatCarIdx))
            .Concat(extraMetrics.Skip(1))
            .ToArray();
    }

    private BrowserGapTrendMetric BuildBrowserGapStintTrendMetric(
        BrowserGapCarRenderState referenceState,
        int? threatCarIdx)
    {
        var threatState = threatCarIdx is { } carIdx && _gapCarRenderStates.TryGetValue(carIdx, out var state)
            ? state
            : null;
        var comparisonState = LatestBrowserGapTrendPoint(referenceState.CarIdx) is { } referenceCurrent
            ? BrowserGapComparisonCar(referenceState, referenceCurrent)
            : null;

        return new BrowserGapTrendMetric(
            "Stint",
            null,
            null,
            "stint",
            null,
            PrimaryText: BrowserGapStintLapText(referenceState),
            ThreatText: BrowserGapStintLapText(threatState),
            ComparisonText: BrowserGapStintLapText(comparisonState));
    }

    private IReadOnlyList<BrowserGapTrendMetric> BuildBrowserGapPitTrendMetrics(
        BrowserGapCarRenderState referenceState,
        int? threatCarIdx)
    {
        var threatState = threatCarIdx is { } carIdx && _gapCarRenderStates.TryGetValue(carIdx, out var state)
            ? state
            : null;
        var comparisonState = LatestBrowserGapTrendPoint(referenceState.CarIdx) is { } referenceCurrent
            ? BrowserGapComparisonCar(referenceState, referenceCurrent)
            : null;
        var primaryPit = BrowserGapPitMetricValue(referenceState);
        var comparisonPit = BrowserGapPitMetricValue(comparisonState);
        var threatPit = BrowserGapPitMetricValue(threatState);
        return new[]
        {
            new BrowserGapTrendMetric("Pit", null, null, "pit", null, primaryPit, threatPit, comparisonPit),
            new BrowserGapTrendMetric("PLap", null, null, "pitLap", null, primaryPit, threatPit, comparisonPit)
        };
    }

    private BrowserGapTrendMetric BuildBrowserGapTireTrendMetric(
        BrowserGapCarRenderState referenceState,
        int? threatCarIdx)
    {
        var threatState = threatCarIdx is { } carIdx && _gapCarRenderStates.TryGetValue(carIdx, out var state)
            ? state
            : null;
        var comparisonState = LatestBrowserGapTrendPoint(referenceState.CarIdx) is { } referenceCurrent
            ? BrowserGapComparisonCar(referenceState, referenceCurrent)
            : null;
        var primaryTire = BrowserGapTireMetricValue(referenceState);
        var threatTire = BrowserGapTireMetricValue(threatState);
        var comparisonTire = BrowserGapTireMetricValue(comparisonState);
        return new BrowserGapTrendMetric(
            "Tire",
            null,
            null,
            "tire",
            null,
            PrimaryTire: primaryTire,
            ThreatTire: threatTire,
            ComparisonTire: comparisonTire);
    }

    private IReadOnlyList<BrowserGapTrendMetric> BuildBrowserGapExtraTrendMetrics(
        BrowserGapCarRenderState referenceState,
        int? threatCarIdx)
    {
        var threatState = threatCarIdx is { } carIdx && _gapCarRenderStates.TryGetValue(carIdx, out var state)
            ? state
            : null;
        var comparisonState = LatestBrowserGapTrendPoint(referenceState.CarIdx) is { } referenceCurrent
            ? BrowserGapComparisonCar(referenceState, referenceCurrent)
            : null;
        var lastComparisonState = LatestBrowserGapTrendPoint(referenceState.CarIdx) is { } lastReferenceCurrent
            ? BrowserGapCarAhead(referenceState, lastReferenceCurrent)
            : null;

        return new[]
        {
            new BrowserGapTrendMetric(
                "Last",
                null,
                null,
                "last",
                null,
                PrimaryText: BrowserGapLastLapDeltaText(referenceState, referenceState),
                ThreatText: BrowserGapLastLapDeltaText(referenceState, threatState),
                ComparisonText: BrowserGapLastLapDeltaText(referenceState, lastComparisonState)),
            new BrowserGapTrendMetric(
                "Status",
                null,
                null,
                "status",
                null,
                PrimaryText: BrowserGapStatusText(referenceState),
                ThreatText: BrowserGapStatusText(threatState),
                ComparisonText: BrowserGapStatusText(comparisonState))
        };
    }

    private BrowserGapTrendMetric BuildBrowserGapTrendMetric(
        string label,
        double lookbackSeconds,
        double? targetLaps,
        double latest,
        BrowserGapCarRenderState referenceState)
    {
        var completedReferenceLaps = BrowserGapCompletedLapSpan(referenceState.CarIdx);
        if (!IsFinite(lookbackSeconds)
            || lookbackSeconds <= 0d
            || LatestBrowserGapTrendPoint(referenceState.CarIdx) is not { } referenceCurrent)
        {
            return new BrowserGapTrendMetric(label, null, null, "unavailable", null, CompletedReferenceLaps: completedReferenceLaps);
        }

        var targetAxisSeconds = latest - lookbackSeconds;
        if (!HasBrowserGapCompletedLapHistory(referenceState.CarIdx, targetLaps))
        {
            return new BrowserGapTrendMetric(label, null, null, "unavailable", null, CompletedReferenceLaps: completedReferenceLaps);
        }

        var chaser = StrongestBrowserGapBehindGain(referenceState, referenceCurrent, targetAxisSeconds, latest, targetLaps);
        if (BrowserGapTrendPointNear(referenceState.CarIdx, targetAxisSeconds) is not { } referencePast)
        {
            return new BrowserGapTrendMetric(
                label,
                null,
                chaser,
                "warming",
                null,
                CompletedReferenceLaps: completedReferenceLaps);
        }

        var comparisonState = BrowserGapComparisonCar(referenceState, referenceCurrent);
        if (comparisonState is null || LatestBrowserGapTrendPoint(comparisonState.CarIdx) is not { } comparisonCurrent)
        {
            return new BrowserGapTrendMetric(label, null, chaser, "ready", "leader", CompletedReferenceLaps: completedReferenceLaps);
        }

        if (!HasBrowserGapCompletedLapHistory(comparisonState.CarIdx, targetLaps))
        {
            return new BrowserGapTrendMetric(label, null, chaser, "unavailable", null, CompletedReferenceLaps: completedReferenceLaps);
        }

        if (BrowserGapTrendPointNear(comparisonState.CarIdx, targetAxisSeconds) is not { } comparisonPast)
        {
            return new BrowserGapTrendMetric(
                label,
                null,
                chaser,
                "warming",
                null,
                CompletedReferenceLaps: completedReferenceLaps);
        }

        var currentDelta = referenceCurrent.GapSeconds - comparisonCurrent.GapSeconds;
        var pastDelta = referencePast.GapSeconds - comparisonPast.GapSeconds;
        return new BrowserGapTrendMetric(label, currentDelta - pastDelta, chaser, "ready", null, CompletedReferenceLaps: completedReferenceLaps);
    }

    private BrowserBehindGainMetric? StrongestBrowserGapBehindGain(
        BrowserGapCarRenderState referenceState,
        BrowserGapTrendPoint referenceCurrent,
        double targetAxisSeconds,
        double latest,
        double? targetLaps)
    {
        if (HasBrowserGapPitActivityBetween(referenceState, targetAxisSeconds, latest)
            || BrowserGapTrendPointNear(referenceState.CarIdx, targetAxisSeconds) is not { } referencePast)
        {
            return null;
        }

        BrowserBehindGainMetric? best = null;
        foreach (var state in _gapCarRenderStates.Values)
        {
            if (state.CarIdx == referenceState.CarIdx
                || state.IsReference
                || !IsSameLapBrowserGapState(referenceState, state)
                || state.ClassPosition is not > 0
                || latest - state.LastSeenAxisSeconds > GapMissingTelemetryGraceSeconds
                || !HasBrowserGapCompletedLapHistory(state.CarIdx, targetLaps)
                || HasBrowserGapPitActivityBetween(state, targetAxisSeconds, latest)
                || LatestBrowserGapTrendPoint(state.CarIdx) is not { } current
                || current.GapSeconds <= referenceCurrent.GapSeconds
                || BrowserGapTrendPointNear(state.CarIdx, targetAxisSeconds) is not { } past)
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
                best = new BrowserBehindGainMetric(
                    state.CarIdx,
                    GapToLeaderPresentationRules.PositionLabel(state.ClassPosition)!,
                    gainSeconds);
            }
        }

        return best;
    }

    private BrowserGapCarRenderState? BrowserGapCarAhead(
        BrowserGapCarRenderState referenceState,
        BrowserGapTrendPoint referenceCurrent)
    {
        return _gapCarRenderStates.Values
            .Where(state => state.CarIdx != referenceState.CarIdx
                && !state.IsReference
                && state.ClassPosition is > 0
                && IsSameLapBrowserGapState(referenceState, state))
            .Select(state => new
            {
                State = state,
                Point = LatestBrowserGapTrendPoint(state.CarIdx)
            })
            .Where(item => item.Point is not null && item.Point.GapSeconds < referenceCurrent.GapSeconds - 0.001d)
            .OrderBy(item => referenceCurrent.GapSeconds - item.Point!.GapSeconds)
            .ThenBy(item => item.State.ClassPosition ?? int.MaxValue)
            .FirstOrDefault()
            ?.State;
    }

    private BrowserGapCarRenderState? BrowserGapComparisonCar(
        BrowserGapCarRenderState referenceState,
        BrowserGapTrendPoint referenceCurrent)
    {
        return BrowserGapCarAhead(referenceState, referenceCurrent)
            ?? BrowserGapCarBehind(referenceState, referenceCurrent);
    }

    private BrowserGapCarRenderState? BrowserGapCarBehind(
        BrowserGapCarRenderState referenceState,
        BrowserGapTrendPoint referenceCurrent)
    {
        return _gapCarRenderStates.Values
            .Where(state => state.CarIdx != referenceState.CarIdx
                && !state.IsReference
                && state.ClassPosition is > 0
                && IsSameLapBrowserGapState(referenceState, state))
            .Select(state => new
            {
                State = state,
                Point = LatestBrowserGapTrendPoint(state.CarIdx)
            })
            .Where(item => item.Point is not null && item.Point.GapSeconds > referenceCurrent.GapSeconds + 0.001d)
            .OrderBy(item => item.Point!.GapSeconds - referenceCurrent.GapSeconds)
            .ThenBy(item => item.State.ClassPosition ?? int.MaxValue)
            .FirstOrDefault()
            ?.State;
    }

    private string BrowserGapComparisonLabel()
    {
        var referenceState = _gapCarRenderStates.Values.FirstOrDefault(state => state.IsReference);
        if (referenceState is null || LatestBrowserGapTrendPoint(referenceState.CarIdx) is not { } referenceCurrent)
        {
            return "--";
        }

        var comparisonState = BrowserGapComparisonCar(referenceState, referenceCurrent);
        return comparisonState?.ClassPosition is > 0
            ? $"P{comparisonState.ClassPosition.Value}"
            : "--";
    }

    private BrowserPitMetricValue? BrowserGapPitMetricValue(BrowserGapCarRenderState? state)
    {
        if (state is null)
        {
            return null;
        }

        if (state.IsOnPitRoad && state.CurrentPitEntryAxisSeconds is { } entry)
        {
            var latest = _latestGapAxisSeconds ?? state.LastSeenAxisSeconds;
            return new BrowserPitMetricValue(
                Math.Max(0d, latest - entry),
                state.CurrentPitEntryLap ?? state.LastPitLap,
                true);
        }

        return state.LastPitDurationSeconds is { } duration
            ? new BrowserPitMetricValue(duration, state.LastPitLap, false)
            : null;
    }

    private static string BrowserGapStintLapText(BrowserGapCarRenderState? state)
    {
        if (state is null)
        {
            return "--";
        }

        if (state.CurrentLap is { } currentLap
            && (state.LastPitLap ?? state.CurrentPitEntryLap) is { } pitLap
            && currentLap >= pitLap)
        {
            return BrowserGapStintLapsText(currentLap - pitLap);
        }

        return "--";
    }

    private static string BrowserGapStintLapsText(double laps)
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

    private static string BrowserGapLapTimeText(double? seconds)
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

    private static string BrowserGapLastLapDeltaText(
        BrowserGapCarRenderState referenceState,
        BrowserGapCarRenderState? comparisonState)
    {
        return IsValidLapReference(referenceState.LastLapTimeSeconds)
            && IsValidLapReference(comparisonState?.LastLapTimeSeconds)
            ? FormatSignedSeconds(comparisonState!.LastLapTimeSeconds!.Value - referenceState.LastLapTimeSeconds!.Value)
            : "--";
    }

    private static string BrowserGapStatusText(BrowserGapCarRenderState? state)
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

    private static BrowserTireMetricValue? BrowserGapTireMetricValue(BrowserGapCarRenderState? state)
    {
        return string.IsNullOrWhiteSpace(state?.TireShortLabel)
            ? null
            : new BrowserTireMetricValue(state.TireLabel, state.TireShortLabel, state.TireIsWet == true);
    }

    private static bool HasBrowserGapPitActivityBetween(
        BrowserGapCarRenderState state,
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

    private BrowserGapTrendMetric? ActiveBrowserGapThreat(IReadOnlyList<BrowserGapTrendMetric> metrics)
    {
        return metrics
            .Where(metric => string.Equals(metric.State, "ready", StringComparison.Ordinal)
                && metric.Chaser is not null
                && metric.Chaser.GainSeconds >= GapThreatGainThresholdSeconds())
            .OrderByDescending(metric => metric.Chaser!.GainSeconds)
            .FirstOrDefault();
    }

    private BrowserGapTrendPoint? LatestBrowserGapTrendPoint(int carIdx)
    {
        return _gapSeries.TryGetValue(carIdx, out var points) && points.Count > 0
            ? points[^1]
            : null;
    }

    private bool HasBrowserGapCompletedLapHistory(int carIdx, double? targetLaps)
    {
        return BrowserGapCompletedLapSpan(carIdx) is { } completedLaps
            && targetLaps is { } laps
            && completedLaps >= laps;
    }

    private int? BrowserGapCompletedLapSpan(int carIdx)
    {
        if (!_gapSeries.TryGetValue(carIdx, out var points) || points.Count == 0)
        {
            return null;
        }

        var completedLaps = points
            .Where(point => point.CompletedLap is not null)
            .Select(point => point.CompletedLap!.Value)
            .ToArray();
        return completedLaps.Length == 0
            ? null
            : completedLaps[^1] - completedLaps[0];
    }

    private BrowserGapTrendPoint? BrowserGapTrendPointNear(int carIdx, double axisSeconds)
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

    private IReadOnlyList<BrowserGapTrendMetric> DefaultBrowserGapTrendMetrics(string state)
    {
        return new[]
        {
            new BrowserGapTrendMetric("Last", null, null, state, null),
            new BrowserGapTrendMetric("5L", null, null, state, null),
            new BrowserGapTrendMetric("10L", null, null, state, null),
            new BrowserGapTrendMetric("Pit", null, null, state, null),
            new BrowserGapTrendMetric("PLap", null, null, state, null),
            new BrowserGapTrendMetric("Stint", null, null, state, null),
            new BrowserGapTrendMetric("Tire", null, null, state, null),
            new BrowserGapTrendMetric("Status", null, null, state, null)
        };
    }

    private IReadOnlyList<BrowserGapTrendPoint> PointsForGapCar(int carIdx, double startSeconds, double endSeconds)
    {
        return _gapSeries.TryGetValue(carIdx, out var points)
            ? points.Where(point => point.AxisSeconds >= startSeconds && point.AxisSeconds <= endSeconds).ToArray()
            : Array.Empty<BrowserGapTrendPoint>();
    }

    private static string BrowserGapSeriesBaseColor(BrowserGapCarRenderState state, int index, int? threatCarIdx)
    {
        if (state.CarIdx == threatCarIdx)
        {
            return "#ec7063";
        }

        if (state.IsReference)
        {
            return "#00e8ff";
        }

        if (state.IsClassLeader)
        {
            return "#f7fbff";
        }

        return (index % 3) switch
        {
            0 => "#ffd15b",
            1 => "#70e092",
            _ => "#ff62d2"
        };
    }

    private static double BrowserGapSeriesAlphaMultiplier(BrowserGapCarRenderState state, int? threatCarIdx)
    {
        return state.IsClassLeader || state.IsReference || state.CarIdx == threatCarIdx
            ? 1d
            : 0.48d;
    }

    private static string BrowserGapRenderedColor(string hexColor, double alpha)
    {
        if (hexColor.Length != 7 || hexColor[0] != '#')
        {
            return hexColor;
        }

        var red = Convert.ToInt32(hexColor.Substring(1, 2), 16);
        var green = Convert.ToInt32(hexColor.Substring(3, 2), 16);
        var blue = Convert.ToInt32(hexColor.Substring(5, 2), 16);
        return $"rgba({red}, {green}, {blue}, {Math.Clamp(alpha, 0d, 1d).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)})";
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

    private double? FirstVisibleGapAxisSeconds(IReadOnlyList<BrowserGapSeriesSelection> selectedSeries)
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

    private BrowserGapScale SelectBrowserGapScale(
        IReadOnlyList<BrowserGapSeriesSelection> selectedSeries,
        double startSeconds,
        double endSeconds)
    {
        var scaleSeries = selectedSeries
            .Where(ShouldUseForBrowserGapScale)
            .ToArray();
        var leaderScaleMax = SelectBrowserMaxGapSeconds(scaleSeries, startSeconds, endSeconds);
        var referenceSelection = selectedSeries.FirstOrDefault(selection => selection.State.IsReference);
        if (referenceSelection is null
            || !_gapSeries.TryGetValue(referenceSelection.State.CarIdx, out var rawReferencePoints))
        {
            return BrowserGapScale.Leader(leaderScaleMax);
        }

        var referencePoints = rawReferencePoints
            .Where(point => point.AxisSeconds >= startSeconds && point.AxisSeconds <= endSeconds)
            .OrderBy(point => point.AxisSeconds)
            .ToArray();
        if (referencePoints.Length == 0)
        {
            return BrowserGapScale.Leader(leaderScaleMax);
        }

        var latestReferenceGap = BrowserGapReferenceAt(referencePoints, endSeconds);
        var triggerGap = GapFocusScaleMinimumReferenceGap();
        if (latestReferenceGap < triggerGap)
        {
            return BrowserGapScale.Leader(leaderScaleMax);
        }

        var maxAheadSeconds = 0d;
        var maxBehindSeconds = 0d;
        var hasLocalComparison = false;
        foreach (var selection in scaleSeries.Where(selection => !selection.State.IsClassLeader))
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
                var delta = point.GapSeconds - BrowserGapReferenceAt(referencePoints, point.AxisSeconds);
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
        var forceFocusScaleForLappedReference = ShouldForceBrowserFocusScaleForLappedReference(latestReferenceGap);
        if (!forceFocusScaleForLappedReference
            && (!hasLocalComparison || leaderScaleMax < Math.Max(triggerGap, localRange * GapFocusScaleTriggerRatio)))
        {
            return BrowserGapScale.Leader(leaderScaleMax);
        }

        return BrowserGapScale.FocusRelative(
            leaderScaleMax,
            aheadRange,
            behindRange,
            referencePoints,
            latestReferenceGap);
    }

    private bool ShouldUseForBrowserGapScale(BrowserGapSeriesSelection selection)
    {
        return GapToLeaderPresentationRules.ShouldUseForFocusScale(
            selection.State.IsReference,
            selection.State.IsClassLeader,
            selection.IsStale,
            selection.IsStickyExit,
            selection.State.IsCurrentlyDesired,
            selection.State.DeltaSecondsToReference,
            GapFilteredRangeSeconds());
    }

    private double SelectBrowserMaxGapSeconds(
        IReadOnlyList<BrowserGapSeriesSelection> selectedSeries,
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
        return GapToLeaderPresentationRules.FocusScaleTriggerSeconds(
            _lastGapLapReferenceSeconds,
            GapFocusScaleMinimumReferenceGapSeconds,
            GapFocusScaleMinimumReferenceGapLaps);
    }

    private double GapFocusScaleMinimumRange()
    {
        return Math.Max(
            GapFocusScaleMinimumRangeSeconds,
            _lastGapLapReferenceSeconds is { } lapSeconds && IsValidLapReference(lapSeconds)
                ? lapSeconds * GapFocusScaleMinimumRangeLaps
                : 0d);
    }

    private bool ShouldForceBrowserFocusScaleForLappedReference(double latestReferenceGap)
    {
        return _lastGapLapReferenceSeconds is { } lapSeconds
            && IsValidLapReference(lapSeconds)
            && latestReferenceGap >= lapSeconds * GapSameLapReferenceBoundaryLaps;
    }

    private IReadOnlyList<BrowserGapSeriesSelection> SelectGapSeries()
    {
        var now = _latestGapAxisSeconds ?? 0d;
        return _gapCarRenderStates.Values
            .Where(state => ShouldKeepGapSeriesVisible(state, now))
            .Select(state => ToGapSeriesSelection(state, now))
            .OrderBy(selection => selection.State.LastGapSeconds)
            .ToArray();
    }

    private static bool HasGapComparisonSeries(IReadOnlyList<BrowserGapSeriesSelection> selectedSeries)
    {
        return selectedSeries.Any(selection => !selection.State.IsClassLeader);
    }

    private bool ShouldKeepGapSeriesVisible(BrowserGapCarRenderState state, double axisSeconds)
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

    private BrowserGapSeriesSelection ToGapSeriesSelection(BrowserGapCarRenderState state, double now)
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
        return new BrowserGapSeriesSelection(state, alpha, isStickyExit, isStale, drawStartSeconds);
    }

    private void PruneGapSeries(double latestAxisSeconds)
    {
        var cutoff = latestAxisSeconds - GapTrendWindowSeconds;
        foreach (var carIdx in _gapSeries.Keys.ToArray())
        {
            _gapSeries[carIdx].RemoveAll(point => point.AxisSeconds < cutoff);
            if (_gapSeries[carIdx].Count == 0)
            {
                _gapSeries.Remove(carIdx);
            }
        }

        _gapWeather.RemoveAll(point => point.AxisSeconds < cutoff);
        _gapLeaderChanges.RemoveAll(marker => marker.AxisSeconds < cutoff);
        _gapDriverChanges.RemoveAll(marker => marker.AxisSeconds < cutoff);
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

    private static double SelectGapAxisSeconds(DateTimeOffset timestampUtc, double? sessionTimeSeconds)
    {
        return sessionTimeSeconds is { } sessionTime
            && sessionTime >= 0d
            && IsFinite(sessionTime)
            ? sessionTime
            : timestampUtc.ToUnixTimeMilliseconds() / 1000d;
    }

    private static BrowserGapReferenceContext? SelectGapReferenceContext(LiveTelemetrySnapshot snapshot)
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
        return new BrowserGapReferenceContext(referenceCarIdx, referenceClass);
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

    private static BrowserGapDriverIdentity? ToGapDriverIdentity(HistoricalSessionDriver driver)
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

        return new BrowserGapDriverIdentity(carIdx, key, SelectGapDriverLabel(driver));
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

    private static double? BrowserGapSeconds(LiveClassGapCar car, double? lapReferenceSeconds)
    {
        return ValidGapSeconds(car.GapSecondsToClassLeader)
            ?? (ValidGapLaps(car.GapLapsToClassLeader) is { } laps ? laps * ChartLapReferenceSeconds(lapReferenceSeconds) : null);
    }

    private static bool IsSameLapGapCandidate(
        LiveClassGapCar candidate,
        LiveClassGapCar reference,
        double? lapReferenceSeconds)
    {
        return NormalizedBrowserGapLaps(candidate, lapReferenceSeconds) is { } candidateGapLaps
            && NormalizedBrowserGapLaps(reference, lapReferenceSeconds) is { } referenceGapLaps
            && Math.Abs(candidateGapLaps - referenceGapLaps) < 0.95d;
    }

    private static bool IsLappedGraphGap(LiveClassGapCar car, double? lapReferenceSeconds)
    {
        return BrowserGapSeconds(car, lapReferenceSeconds) is { } gapSeconds
            && lapReferenceSeconds is { } lapSeconds
            && IsValidLapReference(lapSeconds)
            && gapSeconds >= lapSeconds * 0.95d;
    }

    private static double? NormalizedBrowserGapLaps(LiveClassGapCar car, double? lapReferenceSeconds)
    {
        if (ValidGapLaps(car.GapLapsToClassLeader) is { } laps)
        {
            return laps;
        }

        if (BrowserGapSeconds(car, lapReferenceSeconds) is { } seconds
            && lapReferenceSeconds is { } lapSeconds
            && IsValidLapReference(lapSeconds))
        {
            return seconds / lapSeconds;
        }

        return null;
    }

    private static double BrowserGapReferenceAt(IReadOnlyList<BrowserGapTrendPoint> referencePoints, double axisSeconds)
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

    private static BrowserGapWeatherCondition SelectGapWeatherCondition(LiveTelemetrySnapshot snapshot)
    {
        var weather = snapshot.Models.Weather;
        if (!weather.HasData)
        {
            return BrowserGapWeatherCondition.Unknown;
        }

        if (weather.WeatherDeclaredWet == true)
        {
            return BrowserGapWeatherCondition.DeclaredWet;
        }

        return weather.TrackWetness switch
        {
            >= 4 => BrowserGapWeatherCondition.Wet,
            >= 2 => BrowserGapWeatherCondition.Damp,
            >= 0 => BrowserGapWeatherCondition.Dry,
            _ => BrowserGapWeatherCondition.Unknown
        };
    }

    private static double ChartLapReferenceSeconds(double? lapReferenceSeconds)
    {
        return lapReferenceSeconds is { } value && IsValidLapReference(value)
            ? value
            : GapDefaultLapReferenceSeconds;
    }

    private static bool IsValidLapReference(double? seconds)
    {
        return seconds is { } value && value is > 20d and < 1800d && IsFinite(value);
    }

    private static double? ValidGapSeconds(double? seconds)
    {
        return seconds is { } value && IsFinite(value) && value >= 0d && value < 86400d
            ? value
            : null;
    }

    private static double? ValidGapLaps(double? laps)
    {
        return laps is { } value && IsFinite(value) && value >= 0d
            ? value
            : null;
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

    private bool IsSameLapBrowserGapState(
        BrowserGapCarRenderState referenceState,
        BrowserGapCarRenderState candidateState)
    {
        if (referenceState.GapLapsToLeader is { } referenceLaps
            && candidateState.GapLapsToLeader is { } candidateLaps)
        {
            return Math.Abs(candidateLaps - referenceLaps) < GapSameLapReferenceBoundaryLaps;
        }

        if (_lastGapLapReferenceSeconds is { } lapReferenceSeconds
            && IsValidLapReference(lapReferenceSeconds))
        {
            return Math.Abs(candidateState.LastGapSeconds - referenceState.LastGapSeconds) / lapReferenceSeconds
                < GapSameLapReferenceBoundaryLaps;
        }

        return false;
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

    private static OverlaySettings? FindOverlay(ApplicationSettings settings, string overlayId)
    {
        return settings.Overlays.FirstOrDefault(
            overlay => string.Equals(overlay.Id, overlayId, StringComparison.OrdinalIgnoreCase));
    }

    private static bool GapWindowEnabled(OverlaySettings? overlay)
    {
        return overlay is null
            || overlay.GetIntegerOption(OverlayOptionKeys.GapCarsAhead, defaultValue: 5, minimum: 0, maximum: 12) > 0
            || overlay.GetIntegerOption(OverlayOptionKeys.GapCarsBehind, defaultValue: 5, minimum: 0, maximum: 12) > 0;
    }

    private static bool GapGraphEnabled(OverlaySettings? overlay, OverlaySessionKind? sessionKind)
    {
        return overlay is null
            || OverlayContentColumnSettings.ContentEnabledForSession(
                overlay,
                OverlayOptionKeys.GapGraphEnabled,
                defaultEnabled: true,
                sessionKind);
    }

    private static bool GapAnyTrendMetricEnabled(OverlaySettings? overlay, OverlaySessionKind? sessionKind)
    {
        return overlay is null
            || GapTrendMetricOptionKeys.Any(key =>
                OverlayContentColumnSettings.ContentEnabledForSession(
                    overlay,
                    key,
                    defaultEnabled: true,
                    sessionKind));
    }

    private static bool GapTrendMetricEnabled(OverlaySettings? overlay, string label, OverlaySessionKind? sessionKind)
    {
        return overlay is null
            || GapTrendMetricOptionKey(label) is not { } key
            || OverlayContentColumnSettings.ContentEnabledForSession(
                overlay,
                key,
                defaultEnabled: true,
                sessionKind);
    }

    private static string? GapTrendMetricOptionKey(string label)
    {
        return label.Trim().ToUpperInvariant() switch
        {
            "LAST" => OverlayOptionKeys.GapTrendLastEnabled,
            "5L" => OverlayOptionKeys.GapTrend5LEnabled,
            "10L" => OverlayOptionKeys.GapTrend10LEnabled,
            "PIT" => OverlayOptionKeys.GapTrendPitEnabled,
            "PLAP" => OverlayOptionKeys.GapTrendPitLapEnabled,
            "STINT" => OverlayOptionKeys.GapTrendStintEnabled,
            "TIRE" => OverlayOptionKeys.GapTrendTireEnabled,
            "STATUS" => OverlayOptionKeys.GapTrendStatusEnabled,
            _ => null
        };
    }

    private static readonly string[] GapTrendMetricOptionKeys =
    [
        OverlayOptionKeys.GapTrendLastEnabled,
        OverlayOptionKeys.GapTrend5LEnabled,
        OverlayOptionKeys.GapTrend10LEnabled,
        OverlayOptionKeys.GapTrendPitEnabled,
        OverlayOptionKeys.GapTrendPitLapEnabled,
        OverlayOptionKeys.GapTrendStintEnabled,
        OverlayOptionKeys.GapTrendTireEnabled,
        OverlayOptionKeys.GapTrendStatusEnabled
    ];

    private static OverlaySettings OverlayOrDefault(ApplicationSettings settings, OverlayDefinition definition)
    {
        return FindOverlay(settings, definition.Id) ?? new OverlaySettings
        {
            Id = definition.Id,
            Width = definition.DefaultWidth,
            Height = definition.DefaultHeight,
            Opacity = string.Equals(definition.Id, TrackMapOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase)
                ? TrackMapBrowserSettings.Default.InternalOpacity
                : 1d
        };
    }

    private static bool TryBuildHiddenProductModel(
        OverlayDefinition definition,
        LiveTelemetrySnapshot snapshot,
        ApplicationSettings settings,
        DateTimeOffset now,
        out BrowserOverlayModelResponse response)
    {
        var overlay = FindOverlay(settings, definition.Id) ?? new OverlaySettings
        {
            Id = definition.Id,
            Width = definition.DefaultWidth,
            Height = definition.DefaultHeight,
            Opacity = string.Equals(definition.Id, TrackMapOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase)
                ? TrackMapBrowserSettings.Default.InternalOpacity
                : 1d
        };
        var sessionKind = OverlayAvailabilityEvaluator.NormalizeSessionKind(OverlayAvailabilityEvaluator.CurrentSessionKind(snapshot));
        if (ProductHiddenStatus(definition, overlay, sessionKind, snapshot, now) is not { } status)
        {
            response = null!;
            return false;
        }

        var model = new BrowserOverlayDisplayModel(
            definition.Id,
            definition.DisplayName,
            status,
            string.Empty,
            HiddenBodyKind(definition.Id),
            Columns: [],
            Rows: [],
            Metrics: [],
            Points: [],
            HeaderItems: [],
            ShouldRender: false,
            RootOpacity: BrowserRootOpacity(definition, overlay));
        model = SuppressUnavailableRenderedContent(WithoutOrdinaryOverlayTitle(model));
        model = model with
        {
            EffectiveSettings = EffectiveSettingsEvidence(model, definition.Id, settings, snapshot, now)
        };
        response = new BrowserOverlayModelResponse(now, model);
        return true;
    }

    private static string? ProductHiddenStatus(
        OverlayDefinition definition,
        OverlaySettings overlay,
        OverlaySessionKind? sessionKind,
        LiveTelemetrySnapshot snapshot,
        DateTimeOffset now)
    {
        if (!overlay.Enabled)
        {
            return "disabled | product hidden";
        }

        if (!OverlayEnabledForSession(overlay, sessionKind))
        {
            return "hidden | session disabled";
        }

        if (string.Equals(definition.Id, RelativeOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase)
            && OverlayAvailabilityEvaluator.NormalizeSessionKind(sessionKind) is OverlaySessionKind.Qualifying)
        {
            return "hidden | qualifying unsupported";
        }

        if (!OverlayContentSizing.HasRenderableContent(definition, overlay, sessionKind)
            || !GapWindowEnabled(overlay))
        {
            return "hidden | no enabled content";
        }

        var context = LiveLocalStrategyContext.ForRequirement(snapshot, now, definition.ContextRequirement);
        if (!context.IsAvailable)
        {
            return $"hidden | {context.StatusText}";
        }

        if (definition.FadeWhenLiveTelemetryUnavailable
            && !OverlayAvailabilityEvaluator.FromSnapshot(snapshot, now).IsAvailable)
        {
            return "hidden | telemetry unavailable";
        }

        return null;
    }

    private static string HiddenBodyKind(string overlayId)
    {
        if (string.Equals(overlayId, StandingsOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase)
            || string.Equals(overlayId, RelativeOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase))
        {
            return "table";
        }

        if (string.Equals(overlayId, FuelCalculatorOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase)
            || string.Equals(overlayId, SessionWeatherOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase)
            || string.Equals(overlayId, PitServiceOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase))
        {
            return "metrics";
        }

        if (string.Equals(overlayId, GapToLeaderOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase))
        {
            return "graph";
        }

        if (string.Equals(overlayId, InputStateOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase))
        {
            return "inputs";
        }

        if (string.Equals(overlayId, CarRadarOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase))
        {
            return "car-radar";
        }

        if (string.Equals(overlayId, TrackMapOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase))
        {
            return "track-map";
        }

        if (string.Equals(overlayId, FlagsOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase))
        {
            return "flags";
        }

        if (string.Equals(overlayId, GarageCoverOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase))
        {
            return "garage-cover";
        }

        return string.Equals(overlayId, StreamChatOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase)
            ? "stream-chat"
            : "table";
    }

    private static IReadOnlyList<OverlayContentBrowserColumn> BrowserColumnsWithValidationCapacity(
        IReadOnlyList<OverlayContentBrowserColumn> columns)
    {
        return columns
            .Select(column => string.Equals(column.DataKey, OverlayContentColumnSettings.DataPit, StringComparison.Ordinal)
                    && column.Width < 44
                ? column with { Width = 44 }
                : column)
            .ToArray();
    }

    private static BrowserOverlayEffectiveSettings EffectiveSettingsEvidence(
        BrowserOverlayDisplayModel model,
        string overlayId,
        ApplicationSettings settings,
        LiveTelemetrySnapshot snapshot,
        DateTimeOffset now)
    {
        var sessionKind = OverlayAvailabilityEvaluator.NormalizeSessionKind(OverlayAvailabilityEvaluator.CurrentSessionKind(snapshot));
        var session = EffectiveSettingsSessionKey(sessionKind);
        var hasDefinition = TryGetDefinition(overlayId, out var definition);
        var overlay = hasDefinition
            ? OverlayOrDefault(settings, definition)
            : FindOverlay(settings, overlayId) ?? new OverlaySettings { Id = overlayId };
        var clampedScale = Math.Clamp(overlay.Scale, 0.6d, 2d);
        var clampedOpacity = Math.Clamp(overlay.Opacity, 0d, 1d);
        var browserBaseSize = hasDefinition
            ? BrowserOverlayRecommendedSize.For(definition, overlay, sessionKind)
            : new System.Drawing.Size(Math.Max(0, overlay.Width), Math.Max(0, overlay.Height));
        if (string.Equals(overlayId, FuelCalculatorOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase)
            && model.ShouldRender)
        {
            browserBaseSize = new System.Drawing.Size(
                browserBaseSize.Width,
                FuelBrowserSourceHeight(model, browserBaseSize.Height));
        }

        var browserScaledSize = new System.Drawing.Size(
            Math.Max(1, (int)Math.Round(browserBaseSize.Width * clampedScale)),
            Math.Max(1, (int)Math.Round(browserBaseSize.Height * clampedScale)));
        var browserRootOpacity = hasDefinition
            ? BrowserRootOpacity(definition, overlay)
            : clampedOpacity;
        var effectiveSettings = new List<BrowserOverlayEffectiveSetting>
        {
            new("overlayEnabled", overlay.Enabled),
            new($"session.{session}.enabled", OverlayEnabledForSession(overlay, sessionKind)),
            new("general.unitSystem", UnitSystem(settings)),
            new("scalePercent", (int)Math.Round(clampedScale * 100d)),
            new("opacityPercent", (int)Math.Round(clampedOpacity * 100d))
        };

        AddContentEffectiveSettings(effectiveSettings, overlay, sessionKind, session);
        AddOverlaySpecificEffectiveSettings(effectiveSettings, overlayId, overlay, settings, sessionKind, session, now);
        var sharedSettingsHash = StableSettingsHash(effectiveSettings.Where(IsSharedEffectiveSetting));
        var overlaySettingsHash = StableSettingsHash(effectiveSettings);
        var browserSource = new BrowserOverlayEffectiveBrowserSource(
            BaseWidth: browserBaseSize.Width,
            BaseHeight: browserBaseSize.Height,
            Width: browserScaledSize.Width,
            Height: browserScaledSize.Height,
            Scale: Math.Round(clampedScale, 3),
            ScalePercent: (int)Math.Round(clampedScale * 100d),
            Opacity: Math.Round(browserRootOpacity, 3),
            OpacityPercent: (int)Math.Round(browserRootOpacity * 100d));

        return new BrowserOverlayEffectiveSettings(
            OverlayId: model.OverlayId,
            PreviewMode: session,
            Sources: new BrowserOverlayEffectiveSettingSources(
                BrowserReview: new BrowserOverlayEffectiveSettingSource(
                    Applied: true,
                    SharedSettingsHash: sharedSettingsHash,
                    OverlaySettingsHash: overlaySettingsHash,
                    RoutePath: $"/review/overlays/{overlayId}"),
                LocalhostObs: new BrowserOverlayEffectiveSettingSource(
                    Applied: true,
                    SharedSettingsHash: sharedSettingsHash,
                    OverlaySettingsHash: overlaySettingsHash,
                    RoutePath: $"/overlays/{overlayId}"),
                WindowsNative: new BrowserOverlayEffectiveSettingSource(
                    Applied: true,
                    SharedSettingsHash: sharedSettingsHash,
                    OverlaySettingsHash: overlaySettingsHash,
                    PixelEvidence: new BrowserOverlayNativePixelEvidence(
                        Status: "not-applicable",
                        Reason: "localhost/browser model contract; native pixels are validated by Windows screenshot artifacts"))),
            Rendered: new BrowserOverlayEffectiveRendered(
                BodyKind: model.BodyKind,
                ShouldRender: model.ShouldRender,
                RowCount: model.Rows.Count,
                HeaderItems: model.HeaderItems
                    .Select(item => new BrowserOverlayEffectiveHeaderItem(item.Key, item.Value, item.Tone))
                    .ToArray(),
                BrowserSource: browserSource,
                ColumnKeys: TableColumnKeys(model),
                RowIdentities: TableRowIdentities(model),
                PlaceholderRowCount: PlaceholderRowCount(model),
                Provenance: RenderedProvenance(model, snapshot),
                RelativeTimingEvidence: RelativeTimingEvidence(overlayId, session, model),
                TableStatus: TableStatusEvidence(overlayId, model),
                TimingSanity: TimingSanityEvidence(overlayId, model),
                FuelStrategy: EffectiveFuelStrategyEvidence(overlayId, model),
                Layout: LayoutDensityEvidence(overlayId, model, browserSource),
                InputAvailability: InputAvailabilityEvidence(overlayId, model),
                MapFallback: MapFallbackEvidence(overlayId, model, snapshot),
                UnavailableContentPolicy: UnavailableContentPolicy(model),
                RoleContext: RoleContextEvidence(snapshot)),
            Settings: effectiveSettings);
    }

    private static bool IsSharedEffectiveSetting(BrowserOverlayEffectiveSetting setting)
    {
        return setting.Key is "general.unitSystem" or "scalePercent" or "opacityPercent";
    }

    private static string StableSettingsHash(IEnumerable<BrowserOverlayEffectiveSetting> settings)
    {
        var payload = string.Join(
            "\n",
            settings
                .OrderBy(setting => setting.Key, StringComparer.Ordinal)
                .ThenBy(setting => setting.Session, StringComparer.Ordinal)
                .Select(setting => string.Join(
                    "\t",
                    setting.Key,
                    setting.Session ?? string.Empty,
                    StableSettingValue(setting.Value))));
        var bytes = System.Text.Encoding.UTF8.GetBytes(payload);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant()[..16];
    }

    private static string StableSettingValue(object? value)
    {
        return value switch
        {
            null => "null",
            bool boolean => boolean ? "true" : "false",
            string text => text,
            IFormattable formattable => formattable.ToString(null, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            _ => value.ToString() ?? string.Empty
        };
    }

    private static IReadOnlyList<string> TableColumnKeys(BrowserOverlayDisplayModel model)
    {
        return model.Columns
            .Select(column => string.IsNullOrWhiteSpace(column.DataKey) ? column.Label : column.DataKey)
            .ToArray();
    }

    private static IReadOnlyList<string> TableRowIdentities(BrowserOverlayDisplayModel model)
    {
        return model.Rows.Select(RowIdentity).ToArray();
    }

    private static string RowIdentity(BrowserOverlayDisplayRow row)
    {
        var kind = row.IsClassHeader
            ? "class-header"
            : row.IsPlaceholder
                ? "placeholder"
                : "row";
        var primary = row.HeaderTitle ?? string.Join("/", row.Cells.Take(2));
        return string.Join(
            "|",
            kind,
            primary,
            VisibleRowDetail(row),
            row.IsReference ? "reference" : string.Empty);
    }

    private static string VisibleRowDetail(BrowserOverlayDisplayRow row)
    {
        var detail = row.HeaderDetail ?? string.Empty;
        return row.IsClassHeader
            ? detail.ToUpperInvariant()
            : detail;
    }

    private static int PlaceholderRowCount(BrowserOverlayDisplayModel model)
    {
        return model.Rows.Count(row => row.IsPlaceholder || row.Cells.Count == 0 || row.Cells.All(string.IsNullOrWhiteSpace));
    }

    private static BrowserOverlayRenderedProvenance RenderedProvenance(
        BrowserOverlayDisplayModel model,
        LiveTelemetrySnapshot snapshot)
    {
        var hasLiveModelData = snapshot.Models.Session.HasData
            || snapshot.Models.Timing.HasData
            || snapshot.Models.Scoring.HasData
            || snapshot.Models.FuelPit.HasData
            || snapshot.Models.Inputs.HasData
            || snapshot.Models.Spatial.HasData;
        return new BrowserOverlayRenderedProvenance(
            EvidenceClass: IsUnavailableModel(model)
                ? "unavailable"
                : hasLiveModelData ? "live-capture" : "synthetic-preview",
            CaptureSpecific: hasLiveModelData || snapshot.LatestSample is not null,
            SourceContract: "src/TmrOverlay.App/Overlays/BrowserSources/BrowserOverlayModelFactory.cs");
    }

    private static BrowserOverlayRoleContextEvidence RoleContextEvidence(LiveTelemetrySnapshot snapshot)
    {
        var directory = snapshot.Models.DriverDirectory;
        var reference = snapshot.Models.Reference;
        var playerCarIdx = directory.PlayerCarIdx
            ?? reference.PlayerCarIdx
            ?? snapshot.LatestSample?.PlayerCarIdx;
        var focusCarIdx = directory.FocusCarIdx
            ?? reference.FocusCarIdx
            ?? snapshot.LatestSample?.FocusCarIdx;
        var playerDriver = DriverForCar(directory, playerCarIdx, directory.PlayerDriver);
        var focusDriver = DriverForCar(directory, focusCarIdx, directory.FocusDriver);
        var playerIsSpectator = playerDriver?.IsSpectator;
        var focusIsSpectator = focusDriver?.IsSpectator;
        var focusIsPlayer = reference.HasData
            ? reference.FocusIsPlayer
            : playerCarIdx is not null && focusCarIdx is not null && playerCarIdx == focusCarIdx;
        var hasExplicitNonPlayerFocus = reference.HasData
            ? reference.HasExplicitNonPlayerFocus
            : playerCarIdx is not null && focusCarIdx is not null && playerCarIdx != focusCarIdx;

        return new BrowserOverlayRoleContextEvidence(
            PlayerCarIdx: playerCarIdx,
            FocusCarIdx: focusCarIdx,
            FocusIsPlayer: focusIsPlayer,
            HasExplicitNonPlayerFocus: hasExplicitNonPlayerFocus,
            PlayerIsSpectator: playerIsSpectator,
            FocusIsSpectator: focusIsSpectator,
            LocalRole: LocalRole(playerIsSpectator),
            RoleSource: RoleSource(playerCarIdx, playerIsSpectator),
            IsSpotting: null,
            SpottingSignalStatus: "not-observed");
    }

    private static LiveDriverIdentity? DriverForCar(
        LiveDriverDirectoryModel directory,
        int? carIdx,
        LiveDriverIdentity? preferred)
    {
        if (preferred is not null && preferred.CarIdx == carIdx)
        {
            return preferred;
        }

        return carIdx is { } value
            ? directory.Drivers.FirstOrDefault(driver => driver.CarIdx == value)
            : null;
    }

    private static string LocalRole(bool? playerIsSpectator)
    {
        return playerIsSpectator switch
        {
            true => "spectator",
            false => "driver",
            _ => "unknown"
        };
    }

    private static string RoleSource(int? playerCarIdx, bool? playerIsSpectator)
    {
        if (playerIsSpectator is not null)
        {
            return "DriverInfo.Drivers[].IsSpectator";
        }

        return playerCarIdx is null
            ? "player-car-missing"
            : "driver-info-missing";
    }

    private static BrowserOverlayRelativeTimingEvidence? RelativeTimingEvidence(
        string overlayId,
        string session,
        BrowserOverlayDisplayModel model)
    {
        if (!string.Equals(overlayId, RelativeOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return new BrowserOverlayRelativeTimingEvidence(
            SessionKind: session,
            PhysicalProximityAvailable: string.Equals(session, "practice", StringComparison.OrdinalIgnoreCase)
                || string.Equals(session, "race", StringComparison.OrdinalIgnoreCase),
            TimingColumnKeys: TableColumnKeys(model)
                .Where(key => key.Contains("gap", StringComparison.OrdinalIgnoreCase)
                    || key.Contains("interval", StringComparison.OrdinalIgnoreCase)
                    || key.Contains("delta", StringComparison.OrdinalIgnoreCase))
                .ToArray());
    }

    private static BrowserOverlayTableStatusEvidence? TableStatusEvidence(string overlayId, BrowserOverlayDisplayModel model)
    {
        if (!string.Equals(overlayId, StandingsOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var classHeaderCount = model.Rows.Count(row => row.IsClassHeader);
        var placeholderRowCount = PlaceholderRowCount(model);
        var dataRowCount = Math.Max(0, model.Rows.Count - classHeaderCount - placeholderRowCount);
        return new BrowserOverlayTableStatusEvidence(
            DataRowCount: dataRowCount,
            ClassHeaderCount: classHeaderCount,
            PlaceholderRowCount: placeholderRowCount,
            ClippedRowCount: 0,
            StatusCarCount: dataRowCount);
    }

    private static BrowserOverlayTimingSanityEvidence? TimingSanityEvidence(string overlayId, BrowserOverlayDisplayModel model)
    {
        if (!string.Equals(overlayId, StandingsOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var timingValues = model.Rows
            .SelectMany(row => row.Cells)
            .Select(TryParseTimingSeconds)
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToArray();
        var maximum = timingValues.Length == 0 ? 0d : timingValues.Max(value => Math.Abs(value));
        return new BrowserOverlayTimingSanityEvidence(
            MaxIntervalGapRatio: maximum <= 0d ? 0d : Math.Round(maximum / Math.Max(1d, maximum), 3),
            AbsurdIntervalCount: timingValues.Count(value => Math.Abs(value) > 600d));
    }

    private static BrowserOverlayFuelStrategyEvidence FuelStrategyEvidence(FuelStrategySnapshot? strategy)
    {
        if (strategy is null || !HasTrustedFuelStrategy(strategy) || strategy.AdditionalFuelNeededLiters is null)
        {
            return new BrowserOverlayFuelStrategyEvidence(
                AdditionalFuelNeedState: "unavailable",
                SuccessCopyRequiresMeasuredNeed: true);
        }

        return new BrowserOverlayFuelStrategyEvidence(
            AdditionalFuelNeedState: strategy.AdditionalFuelNeededLiters > 0.1d ? "measured" : "not-needed",
            SuccessCopyRequiresMeasuredNeed: true);
    }

    private static bool HasTrustedFuelStrategy(FuelStrategySnapshot strategy)
    {
        return strategy.FuelPerLapLiters is not null
            && string.Equals(strategy.FuelPerLapSource, "measured green lap", StringComparison.OrdinalIgnoreCase)
            && strategy.RaceLapsRemaining is not null
            && strategy.AdditionalFuelNeededLiters is not null;
    }

    private static int FuelBrowserSourceHeight(BrowserOverlayDisplayModel model, int fallbackHeight)
    {
        if (!string.Equals(model.OverlayId, FuelCalculatorOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase))
        {
            return fallbackHeight;
        }

        var sectionCount = (model.MetricSections ?? []).Count(section => section.Rows.Count > 0);
        var rowCount = (model.MetricSections ?? []).Sum(section => section.Rows.Count);
        if (rowCount <= 0 || sectionCount <= 0)
        {
            return fallbackHeight;
        }

        var height = OverlayContentSizing.FuelCalculatorHeightForContent(rowCount, sectionCount);
        var hasVisibleHeader = model.HeaderItems.Any(item => !string.IsNullOrWhiteSpace(item.Value));
        return hasVisibleHeader
            ? height
            : Math.Max(OverlayGeometryContracts.MetricRows.MinimumChromeAdjustedHeight, height - OverlayGeometryContracts.MetricRows.HeaderChromeHeight);
    }

    private static BrowserOverlayFuelStrategyEvidence? EffectiveFuelStrategyEvidence(
        string overlayId,
        BrowserOverlayDisplayModel model)
    {
        if (!string.Equals(overlayId, FuelCalculatorOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return model.FuelStrategyEvidence
            ?? new BrowserOverlayFuelStrategyEvidence("unavailable", SuccessCopyRequiresMeasuredNeed: true);
    }

    private static BrowserOverlayLayoutEvidence? LayoutDensityEvidence(
        string overlayId,
        BrowserOverlayDisplayModel model,
        BrowserOverlayEffectiveBrowserSource browserSource)
    {
        if (!string.Equals(overlayId, FuelCalculatorOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(overlayId, PitServiceOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var contentRowCount = SemanticContentRowCount(model);
        var sectionCount = (model.MetricSections?.Count ?? 0) + (model.GridSections?.Count ?? 0);
        var geometry = OverlayGeometryContracts.MetricRows;
        var estimatedContentHeight = Math.Max(
            0,
            geometry.HeaderChromeHeight
            + (int)Math.Round(contentRowCount * geometry.PlainRowHeight)
            + (int)Math.Round(sectionCount * (geometry.SectionTitleHeight + geometry.SectionTitleBottomGap + geometry.SectionGap)));
        var unusedHeightRatio = browserSource.Height > 0
            ? Math.Clamp((browserSource.Height - estimatedContentHeight) / (double)browserSource.Height, 0d, 1d)
            : 0d;
        return new BrowserOverlayLayoutEvidence(
            ContentRowCount: contentRowCount,
            UnusedHeightRatio: Math.Round(unusedHeightRatio, 3));
    }

    private static BrowserOverlayInputAvailabilityEvidence? InputAvailabilityEvidence(
        string overlayId,
        BrowserOverlayDisplayModel model)
    {
        if (!string.Equals(overlayId, InputStateOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var inputs = model.Inputs;
        return new BrowserOverlayInputAvailabilityEvidence(
            FixtureControlsAvailable: true,
            IsAvailable: inputs?.IsAvailable == true,
            TracePointCount: inputs?.Trace.Count ?? 0,
            HasGraph: inputs?.HasGraph == true,
            HasRail: inputs?.HasRail == true);
    }

    private static BrowserOverlayMapFallbackEvidence? MapFallbackEvidence(
        string overlayId,
        BrowserOverlayDisplayModel model,
        LiveTelemetrySnapshot snapshot)
    {
        if (!string.Equals(overlayId, TrackMapOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase)
            || !model.ShouldRender
            || !string.Equals(model.TrackMap?.RenderModel.MapKind, "circle", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return new BrowserOverlayMapFallbackEvidence(
            Kind: "circle",
            Reason: "no-generated-track-map",
            CurrentTrackKey: snapshot.Models.Session.TrackDisplayName ?? "unknown-track");
    }

    private static string? UnavailableContentPolicy(BrowserOverlayDisplayModel model)
    {
        if (!IsUnavailableModel(model))
        {
            return null;
        }

        if (IsSectionAwareUnavailablePlaceholderModel(model))
        {
            return "section-aware-placeholders";
        }

        return !HasSemanticRenderedContent(model) ? "suppress-rendered-content" : null;
    }

    private static bool IsSectionAwareUnavailablePlaceholderModel(BrowserOverlayDisplayModel model)
    {
        return string.Equals(model.OverlayId, SessionWeatherOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase)
            && model.MetricSections?.Any(section => section.Rows.Count > 0) == true
            && model.Status.Contains("weather unavailable", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsUnavailableModel(BrowserOverlayDisplayModel model)
    {
        return !model.ShouldRender
            || model.Status.Contains("waiting", StringComparison.OrdinalIgnoreCase)
            || model.Status.Contains("unavailable", StringComparison.OrdinalIgnoreCase)
            || model.Status.Contains("hidden", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasSemanticRenderedContent(BrowserOverlayDisplayModel model)
    {
        return model.Rows.Count > 0
            || model.Points.Count > 0
            || model.Metrics.Count > 0
            || (model.MetricSections?.Any(section => section.Rows.Count > 0) == true)
            || (model.GridSections?.Any(section => section.Rows.Count > 0) == true)
            || model.Graph?.Series.Count > 0
            || model.Graph?.TrendMetrics.Count > 0
            || model.Inputs?.HasGraph == true
            || model.Inputs?.HasRail == true
            || model.Inputs?.Trace.Count > 0
            || model.CarRadar?.RenderModel.ShouldRender == true
            || model.TrackMap?.RenderModel.Markers.Count > 0
            || model.Flags?.Flags.Count > 0
            || model.StreamChat?.Rows.Count > 0;
    }

    private static BrowserOverlayDisplayModel SuppressUnavailableRenderedContent(BrowserOverlayDisplayModel model)
    {
        var unavailableContentPolicy = UnavailableContentPolicy(model);
        if (!IsUnavailableModel(model)
            || string.Equals(unavailableContentPolicy, "section-aware-placeholders", StringComparison.Ordinal))
        {
            return model;
        }

        return model with
        {
            ShouldRender = false,
            Columns = [],
            Rows = [],
            Metrics = [],
            Points = [],
            HeaderItems = [],
            Graph = EmptyGraphModel(model.Graph),
            CarRadar = EmptyCarRadarModel(model.CarRadar),
            TrackMap = EmptyTrackMapModel(model.TrackMap),
            Inputs = EmptyInputModel(model.Inputs),
            Flags = EmptyFlagsModel(model.Flags),
            StreamChat = EmptyStreamChatModel(model.StreamChat),
            GridSections = [],
            MetricSections = []
        };
    }

    private static BrowserGapGraph? EmptyGraphModel(BrowserGapGraph? graph)
    {
        return graph is null
            ? null
            : graph with
            {
                Series = [],
                Weather = [],
                LeaderChanges = [],
                DriverChanges = [],
                SelectedSeriesCount = 0,
                TrendMetrics = [],
                ActiveThreat = null,
                ThreatCarIdx = null,
                Scale = null
            };
    }

    private static BrowserCarRadarModel? EmptyCarRadarModel(BrowserCarRadarModel? carRadar)
    {
        return carRadar is null
            ? null
            : carRadar with
            {
                IsAvailable = false,
                HasCarLeft = false,
                HasCarRight = false,
                Cars = [],
                StrongestMulticlassApproach = null,
                HasCurrentSignal = false,
                RenderModel = CarRadarRenderModel.Empty
            };
    }

    private static BrowserTrackMapModel? EmptyTrackMapModel(BrowserTrackMapModel? trackMap)
    {
        return trackMap is null
            ? null
            : trackMap with
            {
                Markers = [],
                Sectors = [],
                RenderModel = trackMap.RenderModel with
                {
                    IsAvailable = false,
                    Primitives = [],
                    Markers = []
                }
            };
    }

    private static InputStateRenderModel? EmptyInputModel(InputStateRenderModel? inputs)
    {
        return inputs is null
            ? null
            : inputs with
            {
                HasGraph = false,
                HasRail = false,
                HasContent = false,
                Trace = []
            };
    }

    private static BrowserFlagsModel? EmptyFlagsModel(BrowserFlagsModel? flags)
    {
        return flags is null
            ? null
            : flags with { Flags = [] };
    }

    private static BrowserStreamChatModel? EmptyStreamChatModel(BrowserStreamChatModel? streamChat)
    {
        return streamChat is null
            ? null
            : streamChat with { Rows = [] };
    }

    private static BrowserOverlayDisplayModel WithoutOrdinaryOverlayTitle(BrowserOverlayDisplayModel model)
    {
        if (string.Equals(model.OverlayId, GarageCoverOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(model.Title))
        {
            return model;
        }

        return model with { Title = string.Empty };
    }

    private static int SemanticContentRowCount(BrowserOverlayDisplayModel model)
    {
        return model.Rows.Count
            + model.Metrics.Count
            + (model.MetricSections?.Sum(section => section.Rows.Count) ?? 0)
            + (model.GridSections?.Sum(section => section.Rows.Count) ?? 0)
            + (model.StreamChat?.Rows.Count ?? 0);
    }

    private static double? TryParseTimingSeconds(string? value)
    {
        var text = value?.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var sign = 1d;
        if (text[0] == '+' || text[0] == '-')
        {
            sign = text[0] == '-' ? -1d : 1d;
            text = text[1..];
        }

        if (text.EndsWith("s", StringComparison.OrdinalIgnoreCase))
        {
            text = text[..^1];
        }

        var parts = text.Split(':', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length > 2)
        {
            return null;
        }

        if (!double.TryParse(
                parts[^1],
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var seconds))
        {
            return null;
        }

        var minutes = 0d;
        if (parts.Length == 2
            && !double.TryParse(
                parts[0],
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out minutes))
        {
            return null;
        }

        return sign * (minutes * 60d + seconds);
    }

    private static void AddOverlaySpecificEffectiveSettings(
        List<BrowserOverlayEffectiveSetting> settings,
        string overlayId,
        OverlaySettings overlay,
        ApplicationSettings appSettings,
        OverlaySessionKind? sessionKind,
        string session,
        DateTimeOffset now)
    {
        if (string.Equals(overlayId, StandingsOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase))
        {
            settings.Add(new(
                "carsInClass",
                overlay.GetIntegerOption(
                    OverlayOptionKeys.StandingsCarsInClass,
                    defaultValue: StandingsBrowserSettings.Default.MaximumRows,
                    minimum: StandingsBrowserSettings.MinimumCarsInClass,
                    maximum: StandingsBrowserSettings.MaximumCarsInClass)));
            settings.Add(new(
                "otherClassRows",
                overlay.GetIntegerOption(
                    OverlayOptionKeys.StandingsOtherClassRows,
                    defaultValue: StandingsBrowserSettings.Default.OtherClassRowsPerClass,
                    minimum: 0,
                    maximum: 6)));
        }
        else if (string.Equals(overlayId, RelativeOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase))
        {
            settings.Add(new("carsEachSide", RelativeBrowserSettings.CarsEachSide(overlay)));
        }
        else if (string.Equals(overlayId, GapToLeaderOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase))
        {
            var carsAhead = overlay.GetIntegerOption(OverlayOptionKeys.GapCarsAhead, defaultValue: 5, minimum: 0, maximum: 12);
            var carsBehind = overlay.GetIntegerOption(OverlayOptionKeys.GapCarsBehind, defaultValue: 5, minimum: 0, maximum: 12);
            settings.Add(new("carsAhead", carsAhead));
            settings.Add(new("carsBehind", carsBehind));
            foreach (var block in OverlayContentColumnSettings.GapToLeader.Blocks ?? [])
            {
                settings.Add(new(
                    block.EnabledOptionKey,
                    OverlayContentColumnSettings.ContentEnabledForSession(
                        overlay,
                        block.EnabledOptionKey,
                        block.DefaultEnabled,
                        sessionKind),
                    session));
            }
            settings.Add(new("gap.cars-window", new Dictionary<string, int>
            {
                ["carsAhead"] = carsAhead,
                ["carsBehind"] = carsBehind
            }));
        }
        else if (string.Equals(overlayId, CarRadarOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase))
        {
            settings.Add(new(
                OverlayOptionKeys.RadarMulticlassWarning,
                OverlayContentColumnSettings.ContentEnabledForSession(
                    overlay,
                    OverlayOptionKeys.RadarMulticlassWarning,
                    defaultEnabled: true,
                    sessionKind),
                session));
            settings.Add(new(
                OverlayOptionKeys.RadarMulticlassWarningSeconds,
                overlay.GetIntegerOption(
                    OverlayOptionKeys.RadarMulticlassWarningSeconds,
                    CarRadarOverlayViewModel.DefaultMulticlassWarningRangeSeconds,
                    CarRadarOverlayViewModel.MinimumMulticlassWarningRangeSeconds,
                    CarRadarOverlayViewModel.MaximumMulticlassWarningRangeSeconds)));
            settings.Add(new(
                OverlayOptionKeys.RadarVisibilitySeconds,
                overlay.GetIntegerOption(
                    OverlayOptionKeys.RadarVisibilitySeconds,
                    CarRadarOverlayViewModel.DefaultRadarVisibilitySeconds,
                    CarRadarOverlayViewModel.MinimumRadarVisibilitySeconds,
                    CarRadarOverlayViewModel.MaximumRadarVisibilitySeconds)));
        }
        else if (string.Equals(overlayId, FlagsOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase))
        {
            settings.Add(new(OverlayOptionKeys.FlagsShowGreen, overlay.GetBooleanOption(OverlayOptionKeys.FlagsShowGreen, defaultValue: true), session));
            settings.Add(new(OverlayOptionKeys.FlagsShowBlue, overlay.GetBooleanOption(OverlayOptionKeys.FlagsShowBlue, defaultValue: true), session));
            settings.Add(new(OverlayOptionKeys.FlagsShowYellow, overlay.GetBooleanOption(OverlayOptionKeys.FlagsShowYellow, defaultValue: true), session));
            settings.Add(new(OverlayOptionKeys.FlagsShowCritical, overlay.GetBooleanOption(OverlayOptionKeys.FlagsShowCritical, defaultValue: true), session));
            settings.Add(new(OverlayOptionKeys.FlagsShowFinish, overlay.GetBooleanOption(OverlayOptionKeys.FlagsShowFinish, defaultValue: true), session));
        }
        else if (string.Equals(overlayId, TrackMapOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase))
        {
            settings.Add(new(
                OverlayOptionKeys.TrackMapSectorBoundariesEnabled,
                OverlayContentColumnSettings.ContentEnabledForSession(
                    overlay,
                    OverlayOptionKeys.TrackMapSectorBoundariesEnabled,
                    defaultEnabled: true,
                    sessionKind),
                session));
            settings.Add(new(
                OverlayOptionKeys.TrackMapBuildFromTelemetry,
                OverlayContentColumnSettings.ContentEnabledForSession(
                    overlay,
                    OverlayOptionKeys.TrackMapBuildFromTelemetry,
                    defaultEnabled: true,
                    sessionKind),
                session));
        }
        else if (string.Equals(overlayId, StreamChatOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase))
        {
            settings.Add(new(
                OverlayOptionKeys.StreamChatProvider,
                StreamChatOverlaySettings.FromOverlay(overlay).Provider));
        }
        else if (string.Equals(overlayId, GarageCoverOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase))
        {
            settings.Add(new(
                "garage-cover.previewVisible",
                GarageCoverViewModel.BrowserSettingsFrom(appSettings, now).PreviewVisible));
            settings.Add(new("Content", true));
        }

        if (SupportsSharedChrome(overlayId))
        {
            settings.Add(new(
                $"chrome.header.time-remaining.{session}",
                ChromeTimeRemainingEnabled(overlay, sessionKind),
                session));
        }
    }

    private static void AddContentEffectiveSettings(
        List<BrowserOverlayEffectiveSetting> settings,
        OverlaySettings overlay,
        OverlaySessionKind? sessionKind,
        string session)
    {
        if (!OverlayContentColumnSettings.TryGetContentDefinition(overlay.Id, out var definition))
        {
            return;
        }

        foreach (var column in definition.Columns)
        {
            var key = column.EnabledKey(overlay.Id);
            settings.Add(new(
                key,
                OverlayContentColumnSettings.ContentEnabledForSession(
                    overlay,
                    key,
                    column.DefaultEnabled,
                    sessionKind),
                session));
        }

        foreach (var block in definition.Blocks ?? [])
        {
            settings.Add(new(
                block.EnabledOptionKey,
                OverlayContentColumnSettings.ContentEnabledForSession(
                    overlay,
                    block.EnabledOptionKey,
                    block.DefaultEnabled,
                    sessionKind),
                session));
        }

        if (string.Equals(overlay.Id, InputStateOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase))
        {
            var throttle = OverlayContentColumnSettings.ContentEnabledForSession(
                overlay,
                OverlayOptionKeys.InputShowThrottleTrace,
                defaultEnabled: true,
                sessionKind);
            var brake = OverlayContentColumnSettings.ContentEnabledForSession(
                overlay,
                OverlayOptionKeys.InputShowBrakeTrace,
                defaultEnabled: true,
                sessionKind);
            var clutch = OverlayContentColumnSettings.ContentEnabledForSession(
                overlay,
                OverlayOptionKeys.InputShowClutchTrace,
                defaultEnabled: true,
                sessionKind);
            settings.Add(new("input-state.trace.*", throttle || brake || clutch, session));
        }
    }

    private static bool OverlayEnabledForSession(OverlaySettings overlay, OverlaySessionKind? sessionKind)
    {
        return sessionKind switch
        {
            OverlaySessionKind.Qualifying => overlay.ShowInQualifying,
            OverlaySessionKind.Race => overlay.ShowInRace,
            _ => overlay.ShowInPractice
        };
    }

    private static string EffectiveSettingsSessionKey(OverlaySessionKind? sessionKind)
    {
        return sessionKind switch
        {
            OverlaySessionKind.Qualifying => "qualifying",
            OverlaySessionKind.Race => "race",
            OverlaySessionKind.Practice => "practice",
            _ => "off"
        };
    }

    private static bool SupportsSharedChrome(string overlayId)
    {
        return string.Equals(overlayId, StandingsOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase)
            || string.Equals(overlayId, RelativeOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase)
            || string.Equals(overlayId, FuelCalculatorOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase)
            || string.Equals(overlayId, GapToLeaderOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase)
            || string.Equals(overlayId, SessionWeatherOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase)
            || string.Equals(overlayId, PitServiceOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ChromeTimeRemainingEnabled(OverlaySettings overlay, OverlaySessionKind? sessionKind)
    {
        return sessionKind switch
        {
            OverlaySessionKind.Practice => overlay.GetBooleanOption(OverlayOptionKeys.ChromeHeaderTimeRemainingPractice, defaultValue: true),
            OverlaySessionKind.Qualifying => overlay.GetBooleanOption(OverlayOptionKeys.ChromeHeaderTimeRemainingQualifying, defaultValue: true),
            OverlaySessionKind.Race => overlay.GetBooleanOption(OverlayOptionKeys.ChromeHeaderTimeRemainingRace, defaultValue: true),
            _ => overlay.GetBooleanOption(OverlayOptionKeys.ChromeHeaderTimeRemainingTest, defaultValue: true)
                || overlay.GetBooleanOption(OverlayOptionKeys.ChromeHeaderTimeRemainingPractice, defaultValue: true)
                || overlay.GetBooleanOption(OverlayOptionKeys.ChromeHeaderTimeRemainingQualifying, defaultValue: true)
                || overlay.GetBooleanOption(OverlayOptionKeys.ChromeHeaderTimeRemainingRace, defaultValue: true)
        };
    }

    private static bool TryGetDefinition(string overlayId, out OverlayDefinition definition)
    {
        definition = overlayId switch
        {
            var id when string.Equals(id, StandingsOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase) => StandingsOverlayDefinition.Definition,
            var id when string.Equals(id, RelativeOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase) => RelativeOverlayDefinition.Definition,
            var id when string.Equals(id, FuelCalculatorOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase) => FuelCalculatorOverlayDefinition.Definition,
            var id when string.Equals(id, SessionWeatherOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase) => SessionWeatherOverlayDefinition.Definition,
            var id when string.Equals(id, PitServiceOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase) => PitServiceOverlayDefinition.Definition,
            var id when string.Equals(id, InputStateOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase) => InputStateOverlayDefinition.Definition,
            var id when string.Equals(id, CarRadarOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase) => CarRadarOverlayDefinition.Definition,
            var id when string.Equals(id, GapToLeaderOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase) => GapToLeaderOverlayDefinition.Definition,
            var id when string.Equals(id, FlagsOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase) => FlagsOverlayDefinition.Definition,
            var id when string.Equals(id, TrackMapOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase) => TrackMapOverlayDefinition.Definition,
            var id when string.Equals(id, GarageCoverOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase) => GarageCoverOverlayDefinition.Definition,
            var id when string.Equals(id, StreamChatOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase) => StreamChatOverlayDefinition.Definition,
            _ => null!
        };
        return definition is not null;
    }

    private static double BrowserRootOpacity(OverlayDefinition definition, OverlaySettings? overlay)
    {
        if (!definition.ShowOpacityControl
            || string.Equals(definition.Id, TrackMapOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase))
        {
            return 1d;
        }

        return Math.Clamp(overlay?.Opacity ?? new OverlaySettings { Id = definition.Id }.Opacity, 0.2d, 1d);
    }

    private static string UnitSystem(ApplicationSettings settings)
    {
        return string.Equals(settings.General.UnitSystem, "Imperial", StringComparison.OrdinalIgnoreCase)
            ? "Imperial"
            : "Metric";
    }

    private static IReadOnlyList<BrowserOverlayHeaderItem> HeaderItems(
        OverlaySettings? overlay,
        LiveTelemetrySnapshot snapshot,
        string status,
        string tone = "normal")
    {
        var items = new List<BrowserOverlayHeaderItem>();
        if (overlay is null || OverlayChromeSettings.ShowHeaderTimeRemaining(overlay, snapshot))
        {
            var timeRemaining = OverlayHeaderTimeFormatter.FormatTimeRemaining(snapshot);
            if (!string.IsNullOrWhiteSpace(timeRemaining))
            {
                items.Add(new BrowserOverlayHeaderItem("timeRemaining", timeRemaining, HeaderToneFor("timeRemaining")));
            }
        }

        return items;
    }

    private static string HeaderToneFor(string key)
    {
        return string.Equals(key, "timeRemaining", StringComparison.OrdinalIgnoreCase)
            ? "normal"
            : "normal";
    }

    private static string SourceText(OverlaySettings? overlay, LiveTelemetrySnapshot snapshot, string source)
    {
        return source;
    }

    private static bool IsFlagCategoryEnabled(OverlaySettings? overlay, FlagDisplayCategory category)
    {
        return category switch
        {
            FlagDisplayCategory.Green => overlay?.GetBooleanOption(OverlayOptionKeys.FlagsShowGreen, defaultValue: true) ?? true,
            FlagDisplayCategory.Blue => overlay?.GetBooleanOption(OverlayOptionKeys.FlagsShowBlue, defaultValue: true) ?? true,
            FlagDisplayCategory.Yellow => overlay?.GetBooleanOption(OverlayOptionKeys.FlagsShowYellow, defaultValue: true) ?? true,
            FlagDisplayCategory.Critical => overlay?.GetBooleanOption(OverlayOptionKeys.FlagsShowCritical, defaultValue: true) ?? true,
            FlagDisplayCategory.Finish => overlay?.GetBooleanOption(OverlayOptionKeys.FlagsShowFinish, defaultValue: true) ?? true,
            _ => true
        };
    }

    private static int FuelVisibleRowsForHeight(int height, bool showFooter)
    {
        var geometry = OverlayGeometryContracts.MetricRows;
        var bodyHeight = height
            - geometry.HeaderChromeHeight
            - (showFooter ? geometry.FooterChromeHeight : geometry.CollapsedFooterReserveHeight)
            - geometry.BodyGap
            - 34;
        return Math.Max(1, (int)Math.Floor(bodyHeight / (geometry.PlainRowHeight + geometry.RowGap)));
    }

    private static string BrowserStatus(IReadOnlyList<BrowserOverlayHeaderItem> headerItems, string fallback)
    {
        return fallback;
    }

    private static string ClassHeaderDetail(params string[] parts)
    {
        return string.Join(" | ", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    private static string FormatPosition(int? position)
    {
        return position is > 0
            ? position.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : "--";
    }

    private static string FormatGap(LiveGapValue gap)
    {
        if (gap.IsLeader)
        {
            return "LEADER";
        }

        return FormatGap(gap.Seconds, gap.Laps);
    }

    private static string FormatGap(double? seconds, double? laps)
    {
        if (seconds is { } secondsValue && IsFinite(secondsValue))
        {
            return FormatSignedSeconds(secondsValue);
        }

        if (laps is { } lapsValue && IsFinite(lapsValue))
        {
            return $"+{lapsValue.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)} lap";
        }

        return "--";
    }

    private static string FormatSignedSeconds(double? seconds)
    {
        return seconds is { } value && IsFinite(value)
            ? $"{(value > 0d ? "+" : string.Empty)}{value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}"
            : "--";
    }

    private static string ToneName(SimpleTelemetryTone tone)
    {
        return tone.ToString().ToLowerInvariant();
    }

    private static string SimpleChromeTone(SimpleTelemetryTone tone)
    {
        return tone switch
        {
            SimpleTelemetryTone.Error => "error",
            SimpleTelemetryTone.Warning => "warning",
            SimpleTelemetryTone.Success => "success",
            SimpleTelemetryTone.Info => "info",
            SimpleTelemetryTone.Modeled => "info",
            SimpleTelemetryTone.Waiting => "waiting",
            _ => "info"
        };
    }

    private static string FuelChromeTone(FuelStrategySnapshot? strategy)
    {
        if (strategy is null || !strategy.HasData || !HasTrustedFuelStrategy(strategy))
        {
            return "waiting";
        }

        if (strategy.RhythmComparison is { IsRealistic: true, AdditionalStopCount: > 0 }
            || strategy.RequiredFuelSavingPercent is > 0d and <= 0.05d
            || strategy.StopOptimization is { IsRealistic: true, RequiredSavingLitersPerLap: > 0d })
        {
            return "warning";
        }

        return "success";
    }

    private static string NormalizeHeaderTone(string? tone)
    {
        if (string.Equals(tone, "modeled", StringComparison.OrdinalIgnoreCase))
        {
            return "info";
        }

        return new[] { "normal", "waiting", "info", "success", "warning", "error" }
            .Contains(tone ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            ? tone!.ToLowerInvariant()
            : "normal";
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }
}

internal sealed record BrowserOverlayModelResponse(
    DateTimeOffset GeneratedAtUtc,
    BrowserOverlayDisplayModel Model);

internal sealed record BrowserOverlayDisplayModel(
    string OverlayId,
    string Title,
    string Status,
    string Source,
    string BodyKind,
    IReadOnlyList<OverlayContentBrowserColumn> Columns,
    IReadOnlyList<BrowserOverlayDisplayRow> Rows,
    IReadOnlyList<BrowserOverlayMetricRow> Metrics,
    IReadOnlyList<double> Points,
    IReadOnlyList<BrowserOverlayHeaderItem> HeaderItems,
    BrowserGapGraph? Graph = null,
    BrowserCarRadarModel? CarRadar = null,
    BrowserTrackMapModel? TrackMap = null,
    BrowserGarageCoverModel? GarageCover = null,
    BrowserStreamChatModel? StreamChat = null,
    InputStateRenderModel? Inputs = null,
    BrowserFlagsModel? Flags = null,
    IReadOnlyList<BrowserOverlayGridSection>? GridSections = null,
    IReadOnlyList<BrowserOverlayMetricSection>? MetricSections = null,
    bool ShouldRender = true,
    double RootOpacity = 1d,
    BrowserOverlayEffectiveSettings? EffectiveSettings = null,
    BrowserOverlayFuelStrategyEvidence? FuelStrategyEvidence = null)
{
    public static BrowserOverlayDisplayModel Table(
        string overlayId,
        string title,
        string status,
        string source,
        IReadOnlyList<OverlayContentBrowserColumn> columns,
        IReadOnlyList<BrowserOverlayDisplayRow> rows,
        IReadOnlyList<BrowserOverlayHeaderItem>? headerItems = null,
        bool shouldRender = true)
    {
        return new BrowserOverlayDisplayModel(
            overlayId,
            title,
            status,
            source,
            "table",
            columns,
            rows,
            [],
            [],
            headerItems ?? [],
            ShouldRender: shouldRender);
    }

    public static BrowserOverlayDisplayModel MetricRows(
        string overlayId,
        string title,
        string status,
        string source,
        IReadOnlyList<BrowserOverlayMetricRow> metrics,
        IReadOnlyList<BrowserOverlayHeaderItem>? headerItems = null,
        IReadOnlyList<BrowserOverlayGridSection>? gridSections = null,
        IReadOnlyList<BrowserOverlayMetricSection>? metricSections = null,
        bool shouldRender = true)
    {
        return new BrowserOverlayDisplayModel(
            overlayId,
            title,
            status,
            source,
            "metrics",
            [],
            [],
            metrics,
            [],
            headerItems ?? [],
            GridSections: gridSections,
            MetricSections: metricSections,
            ShouldRender: shouldRender);
    }
}

internal sealed record BrowserOverlayEffectiveSettings(
    string OverlayId,
    string PreviewMode,
    BrowserOverlayEffectiveSettingSources Sources,
    BrowserOverlayEffectiveRendered Rendered,
    IReadOnlyList<BrowserOverlayEffectiveSetting> Settings);

internal sealed record BrowserOverlayEffectiveSettingSources(
    BrowserOverlayEffectiveSettingSource BrowserReview,
    BrowserOverlayEffectiveSettingSource LocalhostObs,
    BrowserOverlayEffectiveSettingSource WindowsNative);

internal sealed record BrowserOverlayEffectiveSettingSource(
    bool Applied,
    string? FixtureVariant = null,
    string? SharedSettingsHash = null,
    string? OverlaySettingsHash = null,
    string? RoutePath = null,
    BrowserOverlayNativePixelEvidence? PixelEvidence = null);

internal sealed record BrowserOverlayNativePixelEvidence(
    string Status,
    string Reason);

internal sealed record BrowserOverlayEffectiveRendered(
    string BodyKind,
    bool ShouldRender,
    int RowCount,
    IReadOnlyList<BrowserOverlayEffectiveHeaderItem> HeaderItems,
    BrowserOverlayEffectiveBrowserSource BrowserSource,
    IReadOnlyList<string>? ColumnKeys = null,
    IReadOnlyList<string>? RowIdentities = null,
    int? PlaceholderRowCount = null,
    BrowserOverlayRenderedProvenance? Provenance = null,
    BrowserOverlayRelativeTimingEvidence? RelativeTimingEvidence = null,
    BrowserOverlayTableStatusEvidence? TableStatus = null,
    BrowserOverlayTimingSanityEvidence? TimingSanity = null,
    BrowserOverlayFuelStrategyEvidence? FuelStrategy = null,
    BrowserOverlayLayoutEvidence? Layout = null,
    BrowserOverlayInputAvailabilityEvidence? InputAvailability = null,
    BrowserOverlayMapFallbackEvidence? MapFallback = null,
    string? UnavailableContentPolicy = null,
    BrowserOverlayRoleContextEvidence? RoleContext = null);

internal sealed record BrowserOverlayRenderedProvenance(
    string EvidenceClass,
    bool CaptureSpecific,
    string SourceContract,
    string? SyntheticStateKind = null);

internal sealed record BrowserOverlayRelativeTimingEvidence(
    string SessionKind,
    bool PhysicalProximityAvailable,
    IReadOnlyList<string> TimingColumnKeys);

internal sealed record BrowserOverlayTableStatusEvidence(
    int DataRowCount,
    int ClassHeaderCount,
    int PlaceholderRowCount,
    int ClippedRowCount,
    int StatusCarCount);

internal sealed record BrowserOverlayTimingSanityEvidence(
    double MaxIntervalGapRatio,
    int AbsurdIntervalCount);

internal sealed record BrowserOverlayFuelStrategyEvidence(
    string AdditionalFuelNeedState,
    bool SuccessCopyRequiresMeasuredNeed);

internal sealed record BrowserOverlayLayoutEvidence(
    int ContentRowCount,
    double UnusedHeightRatio);

internal sealed record BrowserOverlayInputAvailabilityEvidence(
    bool FixtureControlsAvailable,
    bool IsAvailable,
    int TracePointCount,
    bool HasGraph,
    bool HasRail);

internal sealed record BrowserOverlayMapFallbackEvidence(
    string Kind,
    string Reason,
    string CurrentTrackKey);

internal sealed record BrowserOverlayRoleContextEvidence(
    int? PlayerCarIdx,
    int? FocusCarIdx,
    bool FocusIsPlayer,
    bool HasExplicitNonPlayerFocus,
    bool? PlayerIsSpectator,
    bool? FocusIsSpectator,
    string LocalRole,
    string RoleSource,
    bool? IsSpotting,
    string SpottingSignalStatus);

internal sealed record BrowserOverlayEffectiveBrowserSource(
    int BaseWidth,
    int BaseHeight,
    int Width,
    int Height,
    double Scale,
    int ScalePercent,
    double Opacity,
    int OpacityPercent);

internal sealed record BrowserOverlayEffectiveHeaderItem(
    string Key,
    string Value,
    string Tone);

internal sealed record BrowserOverlayEffectiveSetting(
    string Key,
    object Value,
    string? Session = null);

internal sealed record BrowserCarRadarModel(
    bool IsAvailable,
    bool HasCarLeft,
    bool HasCarRight,
    IReadOnlyList<LiveSpatialCar> Cars,
    LiveMulticlassApproach? StrongestMulticlassApproach,
    bool ShowMulticlassWarning,
    int MulticlassWarningRangeSeconds,
    int RadarVisibilitySeconds,
    bool PreviewVisible,
    bool HasCurrentSignal,
    CarRadarRenderModel RenderModel);

internal sealed record BrowserTrackMapModel(
    IReadOnlyList<TrackMapOverlayMarker> Markers,
    IReadOnlyList<LiveTrackSectorSegment> Sectors,
    bool ShowSectorBoundaries,
    double InternalOpacity,
    bool IncludeUserMaps,
    TrackMapRenderModel RenderModel);

internal sealed record BrowserGarageCoverModel(
    bool ShouldCover,
    GarageCoverBrowserSettingsSnapshot BrowserSettings,
    GarageCoverDetectionSnapshot Detection);

internal sealed record BrowserStreamChatModel(
    StreamChatBrowserSettings Settings,
    IReadOnlyList<BrowserStreamChatMessage> Rows);

internal sealed record BrowserStreamChatMessage(
    string Name,
    string Text,
    string Kind,
    string Source,
    string? AuthorColorHex,
    IReadOnlyList<string> Metadata,
    IReadOnlyList<BrowserStreamChatBadge> Badges,
    IReadOnlyList<BrowserStreamChatSegment> Segments)
{
    public static BrowserStreamChatMessage From(StreamChatMessage message, StreamChatContentOptions options)
    {
        return new BrowserStreamChatMessage(
            message.Name,
            message.Text,
            message.Kind switch
            {
                StreamChatMessageKind.Error => "error",
                StreamChatMessageKind.System => "system",
                StreamChatMessageKind.Notice => "notice",
                _ => "message"
            },
            message.Source,
            StreamChatMessageDisplay.AuthorColorHex(message, options),
            StreamChatMessageDisplay.MetadataParts(message, options),
            StreamChatMessageDisplay.BadgeParts(message, options)
                .Select(badge => new BrowserStreamChatBadge(badge.Id, badge.Version, badge.Label, badge.RoomId))
                .ToArray(),
            StreamChatMessageDisplay.MessageSegments(message, options)
                .Select(segment => new BrowserStreamChatSegment(segment.Kind, segment.Text, segment.ImageUrl))
                .ToArray());
    }
}

internal sealed record BrowserStreamChatBadge(
    string Id,
    string Version,
    string Label,
    string? RoomId);

internal sealed record BrowserStreamChatSegment(
    string Kind,
    string Text,
    string? ImageUrl);

internal sealed record BrowserFlagsModel(
    IReadOnlyList<BrowserFlagDisplayItem> Flags,
    bool IsWaiting);

internal sealed record BrowserFlagDisplayItem(
    string Kind,
    string Category,
    string Label,
    string? Detail,
    string Tone)
{
    public static BrowserFlagDisplayItem From(FlagOverlayDisplayItem item)
    {
        var kind = string.Equals(item.Label, "Debris", StringComparison.OrdinalIgnoreCase)
            ? "debris"
            : item.Kind.ToString().ToLowerInvariant();
        return new BrowserFlagDisplayItem(
            kind,
            item.Category.ToString().ToLowerInvariant(),
            item.Label,
            item.Detail,
            item.Tone.ToString().ToLowerInvariant());
    }
}

internal sealed record BrowserGapGraph(
    IReadOnlyList<BrowserGapSeries> Series,
    IReadOnlyList<BrowserGapWeatherPoint> Weather,
    IReadOnlyList<BrowserGapLeaderChangeMarker> LeaderChanges,
    IReadOnlyList<BrowserGapDriverChangeMarker> DriverChanges,
    double StartSeconds,
    double EndSeconds,
    double MaxGapSeconds,
    double? LapReferenceSeconds,
    int SelectedSeriesCount,
    IReadOnlyList<BrowserGapTrendMetric> TrendMetrics,
    BrowserGapTrendMetric? ActiveThreat,
    int? ThreatCarIdx,
    double MetricDeadbandSeconds,
    string ComparisonLabel = "--",
    BrowserGapScale? Scale = null,
    bool ShowGraph = true,
    bool ShowTrendMetrics = true);

internal sealed record BrowserGapTrendMetric(
    string Label,
    double? FocusGapChangeSeconds,
    BrowserBehindGainMetric? Chaser,
    string State,
    string? StateLabel,
    BrowserPitMetricValue? PrimaryPit = null,
    BrowserPitMetricValue? ThreatPit = null,
    BrowserPitMetricValue? ComparisonPit = null,
    BrowserTireMetricValue? PrimaryTire = null,
    BrowserTireMetricValue? ThreatTire = null,
    BrowserTireMetricValue? ComparisonTire = null,
    string? PrimaryText = null,
    string? ThreatText = null,
    string? ComparisonText = null,
    int? CompletedReferenceLaps = null);

internal sealed record BrowserBehindGainMetric(
    int CarIdx,
    string Label,
    double GainSeconds);

internal sealed record BrowserPitMetricValue(
    double? Seconds,
    int? Lap,
    bool IsActive);

internal sealed record BrowserTireMetricValue(
    string? Label,
    string? ShortLabel,
    bool IsWet);

internal sealed record BrowserGapScale(
    double MaxGapSeconds,
    bool IsFocusRelative,
    double AheadSeconds,
    double BehindSeconds,
    IReadOnlyList<BrowserGapTrendPoint> ReferencePoints,
    double LatestReferenceGapSeconds)
{
    public static BrowserGapScale Leader(double maxGapSeconds)
    {
        return new BrowserGapScale(
            MaxGapSeconds: maxGapSeconds,
            IsFocusRelative: false,
            AheadSeconds: 0d,
            BehindSeconds: 0d,
            ReferencePoints: [],
            LatestReferenceGapSeconds: 0d);
    }

    public static BrowserGapScale FocusRelative(
        double maxGapSeconds,
        double aheadSeconds,
        double behindSeconds,
        IReadOnlyList<BrowserGapTrendPoint> referencePoints,
        double latestReferenceGapSeconds)
    {
        return new BrowserGapScale(
            MaxGapSeconds: maxGapSeconds,
            IsFocusRelative: true,
            AheadSeconds: aheadSeconds,
            BehindSeconds: behindSeconds,
            ReferencePoints: referencePoints,
            LatestReferenceGapSeconds: latestReferenceGapSeconds);
    }
}

internal sealed record BrowserGapSeries(
    int CarIdx,
    bool IsReference,
    bool IsClassLeader,
    int? ClassPosition,
    double Alpha,
    bool IsStickyExit,
    bool IsStale,
    string? BaseColor,
    string? RenderedColor,
    IReadOnlyList<BrowserGapTrendPoint> Points);

internal sealed record BrowserGapTrendPoint(
    DateTimeOffset TimestampUtc,
    double AxisSeconds,
    double GapSeconds,
    int CarIdx,
    bool IsReference,
    bool IsClassLeader,
    int? ClassPosition,
    int? CompletedLap,
    bool StartsSegment);

internal sealed record BrowserGapWeatherPoint(
    double AxisSeconds,
    BrowserGapWeatherCondition Condition);

internal sealed record BrowserGapLeaderChangeMarker(
    DateTimeOffset TimestampUtc,
    double AxisSeconds,
    int PreviousLeaderCarIdx,
    int NewLeaderCarIdx);

internal sealed record BrowserGapDriverChangeMarker(
    DateTimeOffset TimestampUtc,
    double AxisSeconds,
    int CarIdx,
    double GapSeconds,
    bool IsReference,
    string Label);

internal sealed record BrowserGapSeriesSelection(
    BrowserGapCarRenderState State,
    double Alpha,
    bool IsStickyExit,
    bool IsStale,
    double DrawStartSeconds);

internal sealed record BrowserGapDriverIdentity(
    int CarIdx,
    string DriverKey,
    string ShortLabel)
{
    public bool HasSameDriver(BrowserGapDriverIdentity other)
    {
        return string.Equals(DriverKey, other.DriverKey, StringComparison.Ordinal);
    }
}

internal sealed record BrowserGapReferenceContext(int? CarIdx, int? CarClass);

internal sealed class BrowserGapCarRenderState(int carIdx)
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

    public double? GapLapsToLeader { get; set; }

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

internal enum BrowserGapWeatherCondition
{
    Unknown,
    Dry,
    Damp,
    Wet,
    DeclaredWet
}

internal sealed record BrowserOverlayDisplayRow(
    IReadOnlyList<string> Cells,
    bool IsReference,
    bool IsClassHeader,
    bool IsPit,
    bool IsPartial,
    bool IsPendingGrid,
    string? CarClassColorHex,
    string? HeaderTitle,
    string? HeaderDetail,
    bool IsPlaceholder = false,
    int? RelativeLapDelta = null,
    IReadOnlyList<string?>? CellTones = null);

internal sealed record BrowserOverlayHeaderItem(
    string Key,
    string Value,
    string Tone = "normal");

internal sealed record BrowserOverlayMetricRow(
    string Label,
    string Value,
    string Tone)
{
    public IReadOnlyList<BrowserOverlayMetricSegment> Segments { get; init; } = [];

    public string? RowColorHex { get; init; }
}

internal sealed record BrowserOverlayMetricSegment(
    string Label,
    string Value,
    string Tone,
    string? AccentHex = null,
    double? RotationDegrees = null);

internal sealed record BrowserOverlayGridSection(
    string Title,
    IReadOnlyList<string> Headers,
    IReadOnlyList<BrowserOverlayGridRow> Rows);

internal sealed record BrowserOverlayGridRow(
    string Label,
    IReadOnlyList<BrowserOverlayGridCell> Cells,
    string Tone);

internal sealed record BrowserOverlayGridCell(
    string Value,
    string Tone);

internal sealed record BrowserOverlayMetricSection(
    string Title,
    IReadOnlyList<BrowserOverlayMetricRow> Rows);

internal static class BrowserOverlayTone
{
    public const string Live = "live";
    public const string Modeled = "modeled";
}
