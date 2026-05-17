using TmrOverlay.App.History;
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
    private const int FuelHeaderHeight = 38;
    private const int FuelFooterHeight = 32;
    private const int FuelBodyGap = 12;
    private const int FuelRowHeight = 30;
    private const int FuelRowGap = 5;
    private const double TrackMapReloadIntervalSeconds = 10d;
    private readonly SessionHistoryQueryService _historyQueryService;
    private readonly TrackMapStore? _trackMapStore;
    private readonly StreamChatOverlaySource? _streamChatSource;
    private readonly SessionWeatherOverlayViewModel.StatefulBuilder _sessionWeatherBuilder;
    private readonly PitServiceOverlayViewModel.StatefulBuilder _pitServiceBuilder;
    private readonly TrackMapRenderModelBuilder _trackMapRenderBuilder = new();
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
        var modelSnapshot = snapshot with { Models = snapshot.CompleteModels() };
        var unitSystem = UnitSystem(settings);
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
            var headerItems = HeaderItems(overlay, modelSnapshot, viewModel.Status);
            model = FromSimple(
                SessionWeatherOverlayDefinition.Definition.Id,
                viewModel,
                headerItems,
                string.Empty);
        }
        else if (string.Equals(overlayId, PitServiceOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase))
        {
            var overlay = FindOverlay(settings, PitServiceOverlayDefinition.Definition.Id);
            var viewModel = _pitServiceBuilder.Build(modelSnapshot, now, unitSystem, overlay);
            var headerItems = HeaderItems(overlay, modelSnapshot, PitServiceOverlayViewModel.HeaderStatus(viewModel.Status));
            model = FromSimple(
                PitServiceOverlayDefinition.Definition.Id,
                viewModel,
                headerItems,
                SourceText(overlay, modelSnapshot, viewModel.Source));
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

        if (TryGetDefinition(overlayId, out var definition))
        {
            model = model with
            {
                RootOpacity = BrowserRootOpacity(definition, FindOverlay(settings, definition.Id))
            };
        }

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
        var rows = viewModel.Rows
            .Select(row => new BrowserOverlayDisplayRow(
                Cells: browserSettings.Columns
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
                CellTones: browserSettings.Columns
                    .Select(column => StandingsCellTone(row, column.DataKey))
                    .ToArray()))
            .ToArray();
        var headerItems = HeaderItems(overlay, snapshot, viewModel.Status);

        return BrowserOverlayDisplayModel.Table(
            StandingsOverlayDefinition.Definition.Id,
            StandingsOverlayDefinition.Definition.DisplayName,
            BrowserStatus(headerItems, viewModel.Status),
            SourceText(overlay, snapshot, viewModel.Source),
            browserSettings.Columns,
            rows,
            headerItems);
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
        BrowserOverlayDisplayRow[] rows = viewModel.Rows.Count == 0
            ? []
            : viewModel.StableRows(
                    browserSettings.CarsAhead,
                    browserSettings.CarsBehind,
                    MaximumRelativeRows)
                .Select(row => row is null
                    ? BrowserPlaceholderRow(browserSettings.Columns.Count)
                    : new BrowserOverlayDisplayRow(
                        Cells: browserSettings.Columns
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
        var headerItems = HeaderItems(overlay, snapshot, viewModel.Status);

        return BrowserOverlayDisplayModel.Table(
            RelativeOverlayDefinition.Definition.Id,
            RelativeOverlayDefinition.Definition.DisplayName,
            BrowserStatus(headerItems, viewModel.Status),
            SourceText(overlay, snapshot, viewModel.Source),
            browserSettings.Columns,
            rows,
            headerItems);
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
        var showFooter = overlay is null || OverlayChromeSettings.ShowFooterSource(overlay, snapshot);
        var strategyModel = LiveFuelStrategyModel.From(snapshot, now, LookupHistory);
        if (!strategyModel.IsAvailable)
        {
            var waitingHeaderItems = HeaderItems(overlay, snapshot, strategyModel.Status);
            return BrowserOverlayDisplayModel.MetricRows(
                FuelCalculatorOverlayDefinition.Definition.Id,
                FuelCalculatorOverlayDefinition.Definition.DisplayName,
                BrowserStatus(waitingHeaderItems, strategyModel.Status),
                SourceText(overlay, snapshot, "source: waiting"),
                [],
                waitingHeaderItems,
                shouldRender: false);
        }

        var viewModel = FuelCalculatorViewModel.From(
            strategyModel,
            overlay is null
                || OverlayContentColumnSettings.ContentEnabledForSession(
                    overlay,
                    OverlayOptionKeys.FuelAdvice,
                    defaultEnabled: true,
                    OverlayAvailabilityEvaluator.CurrentSessionKind(snapshot)),
            unitSystem,
            maximumRows: FuelVisibleRowsForHeight(
                effectiveOverlay.Height > 0
                    ? effectiveOverlay.Height
                    : FuelCalculatorOverlayDefinition.Definition.DefaultHeight,
                showFooter));
        var metrics = MetricSectionsFrom(viewModel.MetricSections)
            .SelectMany(section => section.Rows)
            .ToArray();
        var headerItems = HeaderItems(overlay, snapshot, viewModel.Status);

        return BrowserOverlayDisplayModel.MetricRows(
            FuelCalculatorOverlayDefinition.Definition.Id,
            FuelCalculatorOverlayDefinition.Definition.DisplayName,
            BrowserStatus(headerItems, viewModel.Status),
            SourceText(overlay, snapshot, viewModel.Source),
            metrics,
            headerItems,
            metricSections: MetricSectionsFrom(viewModel.MetricSections));
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
        var headerItems = HeaderItems(overlay, snapshot, viewModel.Status);

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
        string? source = null)
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
            MetricSectionsFrom(viewModel.MetricSections));
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
        if (isRace)
        {
            RecordGapSnapshot(snapshot, gap, settings);
        }

        if (isRace
            && viewModel.FocusedTrendPointSeconds is { } seconds
            && ShouldAcceptGapPoint(snapshot, seconds))
        {
            _gapPoints.Add(seconds);
            if (_gapPoints.Count > 120)
            {
                _gapPoints.RemoveRange(0, _gapPoints.Count - 120);
            }
        }

        var headerItems = HeaderItems(overlay, snapshot, viewModel.Status);
        var shouldRender = isRace && GapWindowEnabled(overlay);
        var graph = shouldRender ? BuildBrowserGapGraph(settings) : null;
        IReadOnlyList<double> points = graph?.SelectedSeriesCount > 0
            ? _gapPoints.ToArray()
            : Array.Empty<double>();
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
        var viewModel = CarRadarOverlayViewModel.From(
            snapshot,
            now,
            previewVisible: false,
            showMulticlassWarning,
            calibration);
        var headerItems = HeaderItems(overlay, snapshot, viewModel.Status);
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
                viewModel.PreviewVisible,
                viewModel.HasCurrentSignal,
                CarRadarRenderModel.FromViewModel(viewModel, calibration)));
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
            Inputs: inputModel);
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
        var status = viewModel.IsAvailable ? "live" : viewModel.Status;
        var headerItems = HeaderItems(overlay, snapshot, status);
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
                renderModel));
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
        var headerItems = HeaderItems(overlay, snapshot, viewModel.Status);
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
                viewModel.Detection));
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

        var context = SelectGapReferenceContext(modelSnapshot);
        if (_lastGapReferenceContext is not null && _lastGapReferenceContext != context)
        {
            _gapSeries.Clear();
            _gapWeather.Clear();
            _gapLeaderChanges.Clear();
            _gapDriverChanges.Clear();
            _gapCarRenderStates.Clear();
            _lastGapClassLeaderCarIdx = null;
            _currentGapFuelStintStartAxisSeconds = null;
            _lastGapFuelLevelLiters = null;
            _gapTrendStartAxisSeconds = axisSeconds;
        }

        _lastGapReferenceContext = context;
        var lapReferenceSeconds = GapToLeaderLiveModelAdapter.SelectLapReferenceSeconds(modelSnapshot);
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

    private BrowserGapGraph? BuildBrowserGapGraph(ApplicationSettings settings)
    {
        var selectedSeries = SelectGapSeries();
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
        var trendMetrics = BuildBrowserGapTrendMetrics();
        var activeThreat = ActiveBrowserGapThreat(trendMetrics);
        var threatCarIdx = activeThreat?.Chaser?.CarIdx;
        var series = selectedSeries
            .Select(selection => new BrowserGapSeries(
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
            scale);
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
        return paceMetrics
            .Concat(BuildBrowserGapPitTrendMetrics(referenceState, threatCarIdx))
            .Append(BuildBrowserGapStintTrendMetric(referenceState, threatCarIdx))
            .Append(BuildBrowserGapTireTrendMetric(referenceState, threatCarIdx))
            .Concat(BuildBrowserGapExtraTrendMetrics(referenceState, threatCarIdx))
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

        return new[]
        {
            new BrowserGapTrendMetric(
                "Last",
                null,
                null,
                "last",
                null,
                PrimaryText: BrowserGapLapTimeText(referenceState.LastLapTimeSeconds),
                ThreatText: BrowserGapLapTimeText(threatState?.LastLapTimeSeconds),
                ComparisonText: BrowserGapLapTimeText(comparisonState?.LastLapTimeSeconds)),
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
        if (!IsFinite(lookbackSeconds)
            || lookbackSeconds <= 0d
            || LatestBrowserGapTrendPoint(referenceState.CarIdx) is not { } referenceCurrent)
        {
            return new BrowserGapTrendMetric(label, null, null, "unavailable", null);
        }

        var targetAxisSeconds = latest - lookbackSeconds;
        var chaser = StrongestBrowserGapBehindGain(referenceState, referenceCurrent, targetAxisSeconds, latest);
        if (BrowserGapTrendPointNear(referenceState.CarIdx, targetAxisSeconds) is not { } referencePast)
        {
            return new BrowserGapTrendMetric(
                label,
                null,
                chaser,
                chaser is null ? "warming" : "ready",
                BrowserGapWarmupLabel(referenceState.CarIdx, latest, targetLaps));
        }

        var comparisonState = BrowserGapComparisonCar(referenceState, referenceCurrent);
        if (comparisonState is null || LatestBrowserGapTrendPoint(comparisonState.CarIdx) is not { } comparisonCurrent)
        {
            return new BrowserGapTrendMetric(label, null, chaser, "ready", "leader");
        }

        if (BrowserGapTrendPointNear(comparisonState.CarIdx, targetAxisSeconds) is not { } comparisonPast)
        {
            return new BrowserGapTrendMetric(
                label,
                null,
                chaser,
                chaser is null ? "warming" : "ready",
                BrowserGapWarmupLabel(comparisonState.CarIdx, latest, targetLaps));
        }

        var currentDelta = referenceCurrent.GapSeconds - comparisonCurrent.GapSeconds;
        var pastDelta = referencePast.GapSeconds - comparisonPast.GapSeconds;
        return new BrowserGapTrendMetric(label, currentDelta - pastDelta, chaser, "ready", null);
    }

    private string? BrowserGapWarmupLabel(int referenceCarIdx, double latest, double? targetLaps)
    {
        if (targetLaps is not { } laps
            || laps <= 0d
            || _lastGapLapReferenceSeconds is not { } lapReferenceSeconds
            || !IsValidLapReference(lapReferenceSeconds)
            || FirstBrowserGapTrendPoint(referenceCarIdx) is not { } first)
        {
            return null;
        }

        var availableLaps = Math.Max(0d, (latest - first.AxisSeconds) / lapReferenceSeconds);
        return $"{Math.Min(availableLaps, laps).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}L";
    }

    private BrowserBehindGainMetric? StrongestBrowserGapBehindGain(
        BrowserGapCarRenderState referenceState,
        BrowserGapTrendPoint referenceCurrent,
        double targetAxisSeconds,
        double latest)
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
                best = new BrowserBehindGainMetric(state.CarIdx, BrowserGapCarShortLabel(state), gainSeconds);
            }
        }

        return best;
    }

    private BrowserGapCarRenderState? BrowserGapCarAhead(
        BrowserGapCarRenderState referenceState,
        BrowserGapTrendPoint referenceCurrent)
    {
        return _gapCarRenderStates.Values
            .Where(state => state.CarIdx != referenceState.CarIdx && !state.IsReference)
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
            .Where(state => state.CarIdx != referenceState.CarIdx && !state.IsReference)
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

    private BrowserGapTrendPoint? FirstBrowserGapTrendPoint(int carIdx)
    {
        return _gapSeries.TryGetValue(carIdx, out var points) && points.Count > 0
            ? points[0]
            : null;
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
            new BrowserGapTrendMetric("5L", null, null, state, null),
            new BrowserGapTrendMetric("10L", null, null, state, null),
            new BrowserGapTrendMetric("Pit", null, null, state, null),
            new BrowserGapTrendMetric("PLap", null, null, state, null),
            new BrowserGapTrendMetric("Stint", null, null, state, null),
            new BrowserGapTrendMetric("Tire", null, null, state, null),
            new BrowserGapTrendMetric("Last", null, null, state, null),
            new BrowserGapTrendMetric("Status", null, null, state, null)
        };
    }

    private IReadOnlyList<BrowserGapTrendPoint> PointsForGapCar(int carIdx, double startSeconds, double endSeconds)
    {
        return _gapSeries.TryGetValue(carIdx, out var points)
            ? points.Where(point => point.AxisSeconds >= startSeconds && point.AxisSeconds <= endSeconds).ToArray()
            : Array.Empty<BrowserGapTrendPoint>();
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
        var leaderScaleMax = SelectBrowserMaxGapSeconds(selectedSeries, startSeconds, endSeconds);
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

    private static string BrowserGapCarShortLabel(BrowserGapCarRenderState state)
    {
        return $"#{state.CarIdx}";
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
        string status)
    {
        var items = new List<BrowserOverlayHeaderItem>();
        if (overlay is null || OverlayChromeSettings.ShowHeaderStatus(overlay, snapshot))
        {
            items.Add(new BrowserOverlayHeaderItem("status", status));
        }

        if (overlay is null || OverlayChromeSettings.ShowHeaderTimeRemaining(overlay, snapshot))
        {
            var timeRemaining = OverlayHeaderTimeFormatter.FormatTimeRemaining(snapshot);
            if (!string.IsNullOrWhiteSpace(timeRemaining))
            {
                items.Add(new BrowserOverlayHeaderItem("timeRemaining", timeRemaining));
            }
        }

        return items;
    }

    private static string SourceText(OverlaySettings? overlay, LiveTelemetrySnapshot snapshot, string source)
    {
        return overlay is null || OverlayChromeSettings.ShowFooterSource(overlay, snapshot)
            ? source
            : string.Empty;
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
        var bodyHeight = height - FuelHeaderHeight - (showFooter ? FuelFooterHeight : 8) - FuelBodyGap - 34;
        return Math.Max(1, bodyHeight / (FuelRowHeight + FuelRowGap));
    }

    private static string BrowserStatus(IReadOnlyList<BrowserOverlayHeaderItem> headerItems, string fallback)
    {
        return headerItems.FirstOrDefault(item => string.Equals(item.Key, "status", StringComparison.OrdinalIgnoreCase))?.Value
            ?? headerItems.FirstOrDefault()?.Value
            ?? fallback;
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
    double RootOpacity = 1d)
{
    public static BrowserOverlayDisplayModel Table(
        string overlayId,
        string title,
        string status,
        string source,
        IReadOnlyList<OverlayContentBrowserColumn> columns,
        IReadOnlyList<BrowserOverlayDisplayRow> rows,
        IReadOnlyList<BrowserOverlayHeaderItem>? headerItems = null)
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
            headerItems ?? []);
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

internal sealed record BrowserCarRadarModel(
    bool IsAvailable,
    bool HasCarLeft,
    bool HasCarRight,
    IReadOnlyList<LiveSpatialCar> Cars,
    LiveMulticlassApproach? StrongestMulticlassApproach,
    bool ShowMulticlassWarning,
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
        return new BrowserFlagDisplayItem(
            item.Kind.ToString().ToLowerInvariant(),
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
    BrowserGapScale? Scale = null);

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
    string? ComparisonText = null);

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
    IReadOnlyList<BrowserGapTrendPoint> Points);

internal sealed record BrowserGapTrendPoint(
    DateTimeOffset TimestampUtc,
    double AxisSeconds,
    double GapSeconds,
    int CarIdx,
    bool IsReference,
    bool IsClassLeader,
    int? ClassPosition,
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
    string Value);

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
