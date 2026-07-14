using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using TmrOverlay.App.Events;
using TmrOverlay.App.History;
using TmrOverlay.App.Storage;
using TmrOverlay.Core.AppInfo;
using TmrOverlay.Core.Fuel.V2;
using TmrOverlay.Core.History;
using TmrOverlay.Core.PitService;
using TmrOverlay.Core.Telemetry.EdgeCases;
using TmrOverlay.Core.Telemetry.Live;

namespace TmrOverlay.App.Telemetry;

internal sealed class FuelV2CaptureRecorder
{
    private const int GreenSessionState = 4;
    private const int OnTrackSurface = 3;
    private const double MinimumAcceptedLapProgress = 0.95d;
    private const double MaximumAcceptedLapProgress = 1.25d;
    private const double MinimumFuelBurnLiters = 0.05d;
    private const double MaximumFuelBurnLitersPerLap = 40d;
    private const double MinimumLapSeconds = 20d;
    private const double MaximumLapSeconds = 1800d;
    private const double MinimumPitFuelIncreaseLiters = 0.25d;

    private static readonly string[] RawValueNames =
    [
        "EnterExitReset",
        "PlayerCarTowTime",
        "SessionState",
        "SessionFlags",
        "SessionTimeRemain",
        "SessionTimeTotal",
        "SessionLapsRemainEx",
        "SessionLapsTotal",
        "RaceLaps",
        "FuelLevel",
        "FuelLevelPct",
        "FuelUsePerHour",
        "DriverCarMaxFuelPct",
        "CarClassMaxFuelPct",
        "PitSvFlags",
        "PitSvFuel",
        "PitSvTireCompound",
        "dpFuelFill",
        "dpFuelAddKg",
        "dpFuelAutoFillEnabled",
        "dpFuelAutoFillActive",
        "dpFastRepair",
        "PlayerCarPitSvStatus",
        "PitRepairLeft",
        "PitOptRepairLeft",
        "PlayerCarDryTireSetLimit",
        "FastRepairUsed",
        "FastRepairAvailable",
        "DCDriversSoFar",
        "DCLapStatus",
        "TrackWetness",
        "WeatherDeclaredWet",
        "Precipitation",
        "Skies"
    ];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly FuelV2CaptureOptions _options;
    private readonly AppStorageOptions _storageOptions;
    private readonly AppEventRecorder _events;
    private readonly ILogger<FuelV2CaptureRecorder> _logger;
    private readonly FuelV2HistoryNormalBurnQueryService? _normalHistoryQueryService;
    private readonly object _sync = new();
    private readonly Dictionary<string, int> _sessionFrameCounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _contextFlagCounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _fuelEvidenceCounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _lapBudgetSourceCounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _lapBudgetMissingSignalCounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _raceControlCounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _weatherScopeCounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _pitServiceRequestCounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _raceBurnSelectorStateCounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _raceBurnSelectorBucketCounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _raceBurnSelectorCandidateBucketCounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _raceBurnSelectorConflictCounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _raceBurnSelectorHistoryStatusCounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTimeOffset> _lastSampledFrameAtUtcBySessionKind = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _sampleFrameCountsBySessionKind = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<FuelV2FrameSample> _sampleFrames = [];
    private readonly List<FuelV2EventSample> _eventSamples = [];
    private readonly List<FuelV2LapBurnWindowSample> _acceptedLapBurnWindows = [];
    private readonly List<FuelV2LapBurnWindowSample> _rejectedLapBurnWindows = [];
    private readonly List<FuelV2SectorBurnSample> _sectorBurnSamples = [];
    private readonly List<FuelV2PitWindowSample> _pitWindows = [];
    private readonly List<PitServiceStationaryServiceObservation> _stationaryServiceObservations = [];
    private readonly List<FuelV2TeamStintSample> _teamStints = [];
    private readonly List<FuelV2RaceBurnSelectorShadowTransition> _raceBurnSelectorTransitions = [];
    private string? _connectionSourceId;
    private int _nextSessionOrdinal;
    private string? _sourceId;
    private DateTimeOffset? _startedAtUtc;
    private FuelV2CaptureSessionIdentity? _sessionIdentity;
    private string _sessionBoundaryKind = "first-observed";
    private string? _lastArtifactPath;
    private int _frameCount;
    private int _sampledFrameCount;
    private int _droppedFrameSampleCount;
    private int _droppedEventSampleCount;
    private int _framesWithLocalFuel;
    private int _framesWithTeamProgress;
    private int _framesWithTeamProgressWithoutLocalFuel;
    private int _framesWithInstantaneousBurn;
    private int _framesWithMeasuredBurn;
    private int _framesBaselineEligible;
    private int _framesWithLapBudget;
    private int _framesWithRaceProjection;
    private int _framesWithSectorMetadata;
    private int _acceptedLapBurnWindowCount;
    private int _rejectedLapBurnWindowCount;
    private int _sectorBurnWindowCount;
    private int _sectorBurnRejectedCount;
    private int _pitWindowCount;
    private int _pitWindowsWithFuelIncrease;
    private int _stationaryServiceObservationCount;
    private int _droppedStationaryServiceObservationCount;
    private int _teamStintCount;
    private int _driverChangeEventCount;
    private double? _minFuelLiters;
    private double? _maxFuelLiters;
    private CapacityObservationScope? _capacityObservationScope;
    private double? _maxObservedFuelForCapacityScopeLiters;
    private double? _lastFuelLiters;
    private double? _maxObservedFuelIncreaseLiters;
    private double? _maxObservedFuelDecreaseLiters;
    private int? _lastDriversSoFar;
    private FuelV2SessionScopeSample? _latestSessionScope;
    private FuelAnchor? _cleanLapAnchor;
    private FuelAnchor? _currentLapAnchor;
    private SectorAnchor? _sectorAnchor;
    private PitWindowBuilder? _activePitWindow;
    private PitServiceStationaryServiceTracker _stationaryServiceTracker = new();
    private TeamStintBuilder? _activeTeamStint;
    private FuelV2RaceBurnEvidenceSelectionTracker _raceBurnSelector = new();
    private FuelV2RaceBurnSelectorShadowTransition? _latestRaceBurnSelectorTransition;
    private int _raceBurnSelectorFramesEvaluated;

    public FuelV2CaptureRecorder(
        FuelV2CaptureOptions options,
        AppStorageOptions storageOptions,
        AppEventRecorder events,
        ILogger<FuelV2CaptureRecorder> logger,
        FuelV2HistoryNormalBurnQueryService? normalHistoryQueryService = null)
    {
        _options = options;
        _storageOptions = storageOptions;
        _events = events;
        _logger = logger;
        _normalHistoryQueryService = normalHistoryQueryService;
    }

    public string DiagnosticsLogRoot => Path.Combine(_storageOptions.LogsRoot, _options.LogDirectoryName);

    public string OutputFileName => _options.OutputFileName;

    public string CaptureDirectoryName => _options.CaptureDirectoryName;

    public string? LastArtifactPath
    {
        get
        {
            lock (_sync)
            {
                return _lastArtifactPath;
            }
        }
    }

    public void StartCollection(string sourceId, DateTimeOffset startedAtUtc)
    {
        lock (_sync)
        {
            _connectionSourceId = sourceId;
            _nextSessionOrdinal = 0;
            _lastArtifactPath = null;
            ResetSessionState();
        }
    }

    private void ResetSessionState()
    {
        _sourceId = null;
        _startedAtUtc = null;
        _sessionIdentity = null;
        _sessionBoundaryKind = "first-observed";
        _frameCount = 0;
        _sampledFrameCount = 0;
        _droppedFrameSampleCount = 0;
        _droppedEventSampleCount = 0;
        _framesWithLocalFuel = 0;
        _framesWithTeamProgress = 0;
        _framesWithTeamProgressWithoutLocalFuel = 0;
        _framesWithInstantaneousBurn = 0;
        _framesWithMeasuredBurn = 0;
        _framesBaselineEligible = 0;
        _framesWithLapBudget = 0;
        _framesWithRaceProjection = 0;
        _framesWithSectorMetadata = 0;
        _acceptedLapBurnWindowCount = 0;
        _rejectedLapBurnWindowCount = 0;
        _sectorBurnWindowCount = 0;
        _sectorBurnRejectedCount = 0;
        _pitWindowCount = 0;
        _pitWindowsWithFuelIncrease = 0;
        _stationaryServiceObservationCount = 0;
        _droppedStationaryServiceObservationCount = 0;
        _teamStintCount = 0;
        _driverChangeEventCount = 0;
        _minFuelLiters = null;
        _maxFuelLiters = null;
        _capacityObservationScope = null;
        _maxObservedFuelForCapacityScopeLiters = null;
        _lastFuelLiters = null;
        _maxObservedFuelIncreaseLiters = null;
        _maxObservedFuelDecreaseLiters = null;
        _lastDriversSoFar = null;
        _latestSessionScope = null;
        _cleanLapAnchor = null;
        _currentLapAnchor = null;
        _sectorAnchor = null;
        _activePitWindow = null;
        _stationaryServiceTracker = new PitServiceStationaryServiceTracker();
        _activeTeamStint = null;
        _sessionFrameCounts.Clear();
        _contextFlagCounts.Clear();
        _fuelEvidenceCounts.Clear();
        _lapBudgetSourceCounts.Clear();
        _lapBudgetMissingSignalCounts.Clear();
        _raceControlCounts.Clear();
        _weatherScopeCounts.Clear();
        _pitServiceRequestCounts.Clear();
        _raceBurnSelectorStateCounts.Clear();
        _raceBurnSelectorBucketCounts.Clear();
        _raceBurnSelectorCandidateBucketCounts.Clear();
        _raceBurnSelectorConflictCounts.Clear();
        _raceBurnSelectorHistoryStatusCounts.Clear();
        _lastSampledFrameAtUtcBySessionKind.Clear();
        _sampleFrameCountsBySessionKind.Clear();
        _sampleFrames.Clear();
        _eventSamples.Clear();
        _acceptedLapBurnWindows.Clear();
        _rejectedLapBurnWindows.Clear();
        _sectorBurnSamples.Clear();
        _pitWindows.Clear();
        _stationaryServiceObservations.Clear();
        _teamStints.Clear();
        _raceBurnSelectorTransitions.Clear();
        _raceBurnSelector = new FuelV2RaceBurnEvidenceSelectionTracker();
        _latestRaceBurnSelectorTransition = null;
        _raceBurnSelectorFramesEvaluated = 0;
    }

    public IReadOnlyList<string> RecordFrame(
        LiveTelemetrySnapshot snapshot,
        RawTelemetryWatchSnapshot? rawWatch = null,
        string? captureDirectory = null)
    {
        if (!_options.Enabled)
        {
            return [];
        }

        lock (_sync)
        {
            if (_connectionSourceId is null)
            {
                return [];
            }

            var models = snapshot.CompleteModels();
            var sample = snapshot.LatestSample;
            var capturedAtUtc = snapshot.LastUpdatedAtUtc
                ?? sample?.CapturedAtUtc
                ?? DateTimeOffset.UtcNow;
            var requestedIdentity = FuelV2CaptureSessionIdentity.From(snapshot, models.Session);
            var completedArtifacts = new List<string>();
            if (_sessionIdentity is not null && _sessionIdentity.Contradicts(requestedIdentity))
            {
                var completed = CompleteActiveSession(capturedAtUtc, captureDirectory, "session-transition");
                if (completed is not null)
                {
                    completedArtifacts.Add(completed);
                }

                ResetSessionState();
            }

            if (_sessionIdentity is null)
            {
                // Do not let an unqualified SDK refresh become the opening
                // frames of an eventually strategy-grade segment. We wait for
                // a complete car/layout/session-occurrence identity instead.
                if (!requestedIdentity.IsReadyToRecord)
                {
                    return completedArtifacts;
                }

                BeginSession(
                    requestedIdentity,
                    capturedAtUtc,
                    completedArtifacts.Count > 0 ? "session-transition" : "first-observed");
            }
            else
            {
                // A transient metadata loss cannot safely be attributed to an
                // active classified segment. Skipping that frame is safer than
                // later claiming its fuel/lap evidence belongs to this car,
                // layout, and session occurrence.
                if (!requestedIdentity.CanConfirm(_sessionIdentity))
                {
                    return completedArtifacts;
                }

                _sessionIdentity = _sessionIdentity.Merge(requestedIdentity);
            }

            var sessionKind = SessionKind(snapshot.Context, models.Session);
            var progress = TeamProgress(sample);
            var currentFuel = CurrentFuelLiters(snapshot, models);
            var contextFlags = ContextFlags(sample, models, progress, currentFuel, _lastFuelLiters, _currentLapAnchor);
            var lapProjection = CurrentLapProjection(sample, progress, currentFuel);
            UpdateCapacityObservationScope(snapshot, currentFuel);
            _latestSessionScope = SessionScope(
                snapshot,
                models,
                currentFuel,
                _maxObservedFuelForCapacityScopeLiters);

            _frameCount++;
            Increment(_sessionFrameCounts, sessionKind);
            foreach (var flag in contextFlags)
            {
                Increment(_contextFlagCounts, flag);
            }

            RecordFuelEvidence(snapshot, models, currentFuel, progress);
            RecordRaceBurnSelectorShadow(snapshot, capturedAtUtc);
            RecordLapBudget(models);
            RecordRaceControl(sample, models);
            RecordWeather(models.Weather);
            RecordPitService(models.PitService);
            TrackLapBurnWindow(sample, progress, currentFuel, contextFlags, capturedAtUtc);
            TrackSectorBurn(sample, models, progress, currentFuel, contextFlags, capturedAtUtc);
            TrackStationaryServiceObservation(sample, models, currentFuel, capturedAtUtc);
            TrackPitWindow(sample, models, currentFuel, capturedAtUtc);
            TrackTeamStint(sample, progress, currentFuel, contextFlags, capturedAtUtc);
            TrackDriverChange(sample, snapshot, capturedAtUtc);
            RecordSampleFrame(
                snapshot,
                models,
                rawWatch ?? RawTelemetryWatchSnapshot.Empty,
                sessionKind,
                progress,
                currentFuel,
                lapProjection,
                contextFlags,
                capturedAtUtc);

            return completedArtifacts;
        }
    }

    public string? CompleteCollection(
        DateTimeOffset finishedAtUtc,
        string? captureDirectory,
        string? expectedConnectionSourceId = null)
    {
        if (!_options.Enabled)
        {
            return null;
        }

        lock (_sync)
        {
            if (expectedConnectionSourceId is not null
                && !string.Equals(_connectionSourceId, expectedConnectionSourceId, StringComparison.Ordinal))
            {
                return null;
            }

            var path = CompleteActiveSession(finishedAtUtc, captureDirectory, "collector-stop");
            ResetSessionState();
            _connectionSourceId = null;
            _nextSessionOrdinal = 0;
            return path;
        }
    }

    private void BeginSession(
        FuelV2CaptureSessionIdentity sessionIdentity,
        DateTimeOffset startedAtUtc,
        string boundaryKind)
    {
        _sessionIdentity = sessionIdentity;
        _sessionBoundaryKind = boundaryKind;
        _startedAtUtc = startedAtUtc;
        _nextSessionOrdinal++;
        _sourceId = FuelV2CaptureSessionIdentity.SourceId(
            _connectionSourceId!,
            _nextSessionOrdinal,
            sessionIdentity.SessionFamily);
    }

    private string? CompleteActiveSession(
        DateTimeOffset finishedAtUtc,
        string? captureDirectory,
        string boundaryKind)
    {
        if (_sourceId is null || _startedAtUtc is null || _sessionIdentity is null)
        {
            return null;
        }

        try
        {
            AddBoundaryEvent(boundaryKind, finishedAtUtc);
            FinalizeActiveStationaryServiceObservation();
            FinalizeActivePitWindow(finishedAtUtc);
            FinalizeActiveTeamStint(finishedAtUtc);

            var artifact = new FuelV2CaptureArtifact(
                    FormatVersion: 5,
                    SourceId: _sourceId,
                    StartedAtUtc: _startedAtUtc.Value,
                    FinishedAtUtc: finishedAtUtc,
                    AppVersion: AppVersionInfo.Current,
                    DataVersions: new FuelV2CaptureDataVersions(
                        HistoricalSummaryVersion: HistoricalDataVersions.SummaryVersion,
                        HistoricalCollectionModelVersion: HistoricalDataVersions.CollectionModelVersion,
                        HistoricalAggregateVersion: HistoricalDataVersions.AggregateVersion,
                        LiveModelContractVersion: 1),
                    Output: new FuelV2CaptureOutputScope(
                        Mode: string.IsNullOrWhiteSpace(captureDirectory) ? "rolling-log" : "raw-capture-sidecar",
                        CaptureDirectoryAttached: !string.IsNullOrWhiteSpace(captureDirectory),
                        CaptureDirectoryName: _options.CaptureDirectoryName,
                        OutputFileName: ArtifactFileName(_sourceId),
                        RawTelemetryExcluded: true,
                        DurableHistoryMutated: false),
                    SessionScope: _latestSessionScope,
                    Options: new FuelV2CaptureArtifactOptions(
                        MinimumFrameSpacingSeconds: _options.MinimumFrameSpacingSeconds,
                        MaxSampleFramesPerSession: _options.MaxSampleFramesPerSession,
                        MaxEventExamplesPerSession: _options.MaxEventExamplesPerSession,
                        MaxAcceptedLapWindows: _options.MaxAcceptedLapWindows,
                        MaxRejectedLapWindows: _options.MaxRejectedLapWindows,
                        MaxSectorBurnSamples: _options.MaxSectorBurnSamples,
                        MaxPitWindows: _options.MaxPitWindows,
                        MaxTeamStints: _options.MaxTeamStints,
                        MaxStationaryServiceObservations: _options.MaxStationaryServiceObservations),
                    Totals: new FuelV2CaptureTotals(
                        FrameCount: _frameCount,
                        SampledFrameCount: _sampledFrameCount,
                        DroppedFrameSampleCount: _droppedFrameSampleCount,
                        DroppedEventSampleCount: _droppedEventSampleCount,
                        SessionFrameCounts: Sorted(_sessionFrameCounts),
                        ContextFlagCounts: Sorted(_contextFlagCounts)),
                    Fuel: new FuelV2FuelEvidenceSummary(
                        FramesWithLocalFuel: _framesWithLocalFuel,
                        FramesWithTeamProgress: _framesWithTeamProgress,
                        FramesWithTeamProgressWithoutLocalFuel: _framesWithTeamProgressWithoutLocalFuel,
                        FramesWithInstantaneousBurn: _framesWithInstantaneousBurn,
                        FramesWithMeasuredBurn: _framesWithMeasuredBurn,
                        FramesBaselineEligible: _framesBaselineEligible,
                        MinFuelLiters: Round(_minFuelLiters),
                        MaxFuelLiters: Round(_maxFuelLiters),
                        MaxObservedFuelIncreaseLiters: Round(_maxObservedFuelIncreaseLiters),
                        MaxObservedFuelDecreaseLiters: Round(_maxObservedFuelDecreaseLiters),
                        FuelEvidenceCounts: Sorted(_fuelEvidenceCounts)),
                    LapBudget: new FuelV2LapBudgetEvidenceSummary(
                        FramesWithLapBudget: _framesWithLapBudget,
                        FramesWithRaceProjection: _framesWithRaceProjection,
                        SourceCounts: Sorted(_lapBudgetSourceCounts),
                        MissingSignalCounts: Sorted(_lapBudgetMissingSignalCounts)),
                    SectorBurn: new FuelV2SectorBurnEvidenceSummary(
                        FramesWithSectorMetadata: _framesWithSectorMetadata,
                        AcceptedSectorWindows: _sectorBurnWindowCount,
                        RejectedSectorWindows: _sectorBurnRejectedCount),
                    PitService: new FuelV2PitServiceEvidenceSummary(
                        PitWindowCount: _pitWindowCount,
                        PitWindowsWithFuelIncrease: _pitWindowsWithFuelIncrease,
                        RequestCounts: Sorted(_pitServiceRequestCounts),
                        StationaryServiceObservationCount: _stationaryServiceObservationCount,
                        RetainedStationaryServiceObservationCount: _stationaryServiceObservations.Count,
                        DroppedStationaryServiceObservationCount: _droppedStationaryServiceObservationCount),
                    Team: new FuelV2TeamEvidenceSummary(
                        TeamStintCount: _teamStintCount,
                        DriverChangeEventCount: _driverChangeEventCount),
                    RaceControl: new FuelV2RaceControlEvidenceSummary(
                        StateCounts: Sorted(_raceControlCounts)),
                    Weather: new FuelV2WeatherEvidenceSummary(
                        ScopeCounts: Sorted(_weatherScopeCounts)),
                    RaceBurnSelectorShadow: BuildRaceBurnSelectorShadowEvidence(),
                    SyntheticReplaySuitability: BuildSyntheticReplaySuitability(),
                    SampleFrames: _sampleFrames.ToArray(),
                    AcceptedLapBurnWindows: _acceptedLapBurnWindows.ToArray(),
                    RejectedLapBurnWindows: _rejectedLapBurnWindows.ToArray(),
                    SectorBurnSamples: _sectorBurnSamples.ToArray(),
                    PitWindows: _pitWindows.ToArray(),
                    TeamStints: _teamStints.ToArray(),
                    EventSamples: _eventSamples.ToArray(),
                    SessionLineage: _sessionIdentity.ToLineage(
                        _connectionSourceId,
                        _nextSessionOrdinal,
                        _sessionBoundaryKind,
                        boundaryKind),
                    StationaryServiceObservations: _stationaryServiceObservations.ToArray());

                var path = ResolveArtifactPath(captureDirectory, _sourceId);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                WriteArtifactAtomically(path, artifact);
                _lastArtifactPath = path;
                _events.Record("fuel_v2_capture_saved", new Dictionary<string, string?>
                {
                    ["sourceId"] = _sourceId,
                    ["artifactPath"] = path,
                    ["frameCount"] = _frameCount.ToString(),
                    ["acceptedLapBurnWindows"] = _acceptedLapBurnWindowCount.ToString(),
                    ["pitWindows"] = _pitWindowCount.ToString()
                });
                _logger.LogInformation(
                    "Saved Fuel V2 capture artifact {ArtifactPath} with {FrameCount} frames, {AcceptedLapBurnWindowCount} accepted lap burn windows, and {PitWindowCount} pit windows.",
                    path,
                    _frameCount,
                    _acceptedLapBurnWindowCount,
                    _pitWindowCount);
                return path;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Failed to save Fuel V2 capture artifact for {SourceId}.", _sourceId);
                return null;
            }
        }
    }

    private void RecordFuelEvidence(
        LiveTelemetrySnapshot snapshot,
        LiveRaceModels models,
        double? currentFuel,
        LapProgress? progress)
    {
        if (currentFuel is { } fuelLiters && IsPositiveFinite(fuelLiters))
        {
            _framesWithLocalFuel++;
            _minFuelLiters = _minFuelLiters is null ? fuelLiters : Math.Min(_minFuelLiters.Value, fuelLiters);
            _maxFuelLiters = _maxFuelLiters is null ? fuelLiters : Math.Max(_maxFuelLiters.Value, fuelLiters);

            if (_lastFuelLiters is { } previousFuel)
            {
                var delta = fuelLiters - previousFuel;
                if (delta > 0d)
                {
                    _maxObservedFuelIncreaseLiters = _maxObservedFuelIncreaseLiters is null
                        ? delta
                        : Math.Max(_maxObservedFuelIncreaseLiters.Value, delta);
                }
                else if (delta < 0d)
                {
                    var consumed = Math.Abs(delta);
                    _maxObservedFuelDecreaseLiters = _maxObservedFuelDecreaseLiters is null
                        ? consumed
                        : Math.Max(_maxObservedFuelDecreaseLiters.Value, consumed);
                }
            }

            _lastFuelLiters = fuelLiters;
        }

        if (progress is not null)
        {
            _framesWithTeamProgress++;
            if (!IsPositiveFinite(currentFuel))
            {
                _framesWithTeamProgressWithoutLocalFuel++;
            }
        }

        if (IsPositiveFinite(snapshot.Fuel.FuelUsePerHourLiters)
            || IsPositiveFinite(snapshot.Fuel.FuelUsePerHourKg)
            || IsPositiveFinite(snapshot.LatestSample?.FuelUsePerHourKg))
        {
            _framesWithInstantaneousBurn++;
        }

        if (IsPositiveFinite(snapshot.Fuel.FuelPerLapLiters)
            || models.FuelPit.MeasuredBurnEvidence.IsUsable)
        {
            _framesWithMeasuredBurn++;
        }

        if (models.FuelPit.BaselineEligibilityEvidence.IsUsable)
        {
            _framesBaselineEligible++;
        }

        Increment(_fuelEvidenceCounts, EvidenceKey(models.FuelPit.FuelLevelEvidence));
        Increment(_fuelEvidenceCounts, EvidenceKey(models.FuelPit.InstantaneousBurnEvidence));
        Increment(_fuelEvidenceCounts, EvidenceKey(models.FuelPit.MeasuredBurnEvidence));
        Increment(_fuelEvidenceCounts, EvidenceKey(models.FuelPit.BaselineEligibilityEvidence));
    }

    // This is deliberately capture-only. It evaluates the first strategy
    // policy against normalized live evidence and the display-only exact
    // history bucket, but no overlay, pit request, or durable history import
    // reads the result. Recording it now lets real sessions prove (or reject)
    // conservative disagreement behavior before that policy is promoted.
    private void RecordRaceBurnSelectorShadow(
        LiveTelemetrySnapshot snapshot,
        DateTimeOffset capturedAtUtc)
    {
        if (!string.Equals(_sessionIdentity?.SessionFamily, "race", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var historical = _normalHistoryQueryService?.Lookup(
            snapshot.Context,
            FuelV2HistoryLookupPurpose.Workbench);
        var composed = FuelV2LiveSnapshotComposer.From(
            snapshot,
            new FuelV2LiveSnapshotOptions(
                HistoricalNormalSeed: historical?.IsAvailable == true
                    ? historical.Burn
                    : null));
        var selection = _raceBurnSelector.Select(
            composed.BurnWindows,
            new FuelV2RaceBurnEvidenceSelectionOptions(
                IncludeDisplayOnlyEvidenceForShadowCapture: true));
        var conflict = RaceBurnSelectorConflict(composed.BurnWindows, historical);
        var transition = new FuelV2RaceBurnSelectorShadowTransition(
            CapturedAtUtc: capturedAtUtc,
            State: selection.State.ToString(),
            SelectedBucket: BucketName(selection.BurnBucketId),
            SelectedFuelPerLapLiters: Round(selection.Burn?.Value),
            CandidateBucket: BucketName(selection.CandidateBucketId),
            CandidateFuelPerLapLiters: Round(selection.CandidateBurn?.Value),
            HistoricalStatus: historical?.Status.ToString() ?? "not-configured",
            HistoricalFuelPerLapLiters: Round(historical?.Burn?.Value),
            Conflict: conflict,
            Flags: selection.StateFlags.Select(flag => flag.ToString()).ToArray(),
            Reason: selection.Reason,
            ShadowOnly: true,
            WouldSeedPlanIfPromoted: selection.CanSeedPlan,
            WouldDriveAdviceIfPromoted: selection.CanDriveAdvice);

        _raceBurnSelectorFramesEvaluated++;
        Increment(_raceBurnSelectorStateCounts, transition.State);
        Increment(_raceBurnSelectorBucketCounts, transition.SelectedBucket ?? "unavailable");
        Increment(_raceBurnSelectorCandidateBucketCounts, transition.CandidateBucket ?? "unavailable");
        Increment(_raceBurnSelectorConflictCounts, transition.Conflict);
        Increment(_raceBurnSelectorHistoryStatusCounts, transition.HistoricalStatus);
        if (!SameRaceBurnSelectorDecision(_latestRaceBurnSelectorTransition, transition)
            && _raceBurnSelectorTransitions.Count < _options.MaxEventExamplesPerSession)
        {
            _raceBurnSelectorTransitions.Add(transition);
        }

        _latestRaceBurnSelectorTransition = transition;
    }

    private FuelV2RaceBurnSelectorShadowEvidence BuildRaceBurnSelectorShadowEvidence()
    {
        return new FuelV2RaceBurnSelectorShadowEvidence(
            ShadowOnly: true,
            FramesEvaluated: _raceBurnSelectorFramesEvaluated,
            StateCounts: Sorted(_raceBurnSelectorStateCounts),
            SelectedBucketCounts: Sorted(_raceBurnSelectorBucketCounts),
            CandidateBucketCounts: Sorted(_raceBurnSelectorCandidateBucketCounts),
            ConflictCounts: Sorted(_raceBurnSelectorConflictCounts),
            HistoricalStatusCounts: Sorted(_raceBurnSelectorHistoryStatusCounts),
            Latest: _latestRaceBurnSelectorTransition,
            Transitions: _raceBurnSelectorTransitions.ToArray());
    }

    private static bool SameRaceBurnSelectorDecision(
        FuelV2RaceBurnSelectorShadowTransition? previous,
        FuelV2RaceBurnSelectorShadowTransition next)
    {
        return previous is not null
            && string.Equals(previous.State, next.State, StringComparison.Ordinal)
            && string.Equals(previous.SelectedBucket, next.SelectedBucket, StringComparison.Ordinal)
            && string.Equals(previous.CandidateBucket, next.CandidateBucket, StringComparison.Ordinal)
            && string.Equals(previous.HistoricalStatus, next.HistoricalStatus, StringComparison.Ordinal)
            && string.Equals(previous.Conflict, next.Conflict, StringComparison.Ordinal)
            && Math.Abs((previous.SelectedFuelPerLapLiters ?? 0d) - (next.SelectedFuelPerLapLiters ?? 0d)) < 0.000001d
            && Math.Abs((previous.CandidateFuelPerLapLiters ?? 0d) - (next.CandidateFuelPerLapLiters ?? 0d)) < 0.000001d
            && Math.Abs((previous.HistoricalFuelPerLapLiters ?? 0d) - (next.HistoricalFuelPerLapLiters ?? 0d)) < 0.000001d;
    }

    private static string RaceBurnSelectorConflict(
        FuelV2FuelPerLapWindows windows,
        FuelV2HistoryNormalBurnSelection? historical)
    {
        if (historical?.IsAvailable != true || historical.Burn?.Value is not { } historicalBurn)
        {
            return "history-unavailable";
        }

        var live = MostConservativeConfirmedLiveBurn(windows);
        if (live?.Value is not { } liveBurn)
        {
            return "live-unconfirmed";
        }

        const double agreementToleranceLitersPerLap = 0.05d;
        return liveBurn > historicalBurn + agreementToleranceLitersPerLap
            ? "live-higher"
            : liveBurn < historicalBurn - agreementToleranceLitersPerLap
                ? "live-lower"
                : "agrees";
    }

    private static FuelV2Scalar? MostConservativeConfirmedLiveBurn(FuelV2FuelPerLapWindows windows)
    {
        var candidates = new[] { windows.FiveLapAverage, windows.TenLapAverage }
            .Where(candidate => candidate is { HasValue: true, HasTypedBurnEvidence: true })
            .Cast<FuelV2Scalar>()
            .ToArray();
        return candidates.Length == 0
            ? null
            : candidates.OrderByDescending(candidate => candidate.Value).First();
    }

    private static string? BucketName(FuelV2BurnBucketId? bucketId)
    {
        return bucketId?.ToString();
    }

    private void RecordLapBudget(LiveRaceModels models)
    {
        if (models.RaceProgress.RaceLapsRemaining is not null
            || models.RaceProjection.EstimatedTeamLapsRemaining is not null
            || models.RaceProjection.EstimatedFinishLap is not null)
        {
            _framesWithLapBudget++;
        }

        if (models.RaceProjection.HasData)
        {
            _framesWithRaceProjection++;
        }

        Increment(_lapBudgetSourceCounts, $"remaining:{models.RaceProgress.RaceLapsRemainingSource}");
        Increment(_lapBudgetSourceCounts, $"projection:{models.RaceProjection.EstimatedTeamLapsRemainingSource}");
        Increment(_lapBudgetSourceCounts, $"leader-pace:{models.RaceProjection.OverallLeaderPaceSource}");
        Increment(_lapBudgetSourceCounts, $"team-pace:{models.RaceProjection.TeamPaceSource}");
        foreach (var signal in models.Session.MissingSignals)
        {
            Increment(_lapBudgetMissingSignalCounts, $"session:{signal}");
        }

        foreach (var signal in models.RaceProgress.MissingSignals)
        {
            Increment(_lapBudgetMissingSignalCounts, $"race-progress:{signal}");
        }

        foreach (var signal in models.RaceProjection.MissingSignals)
        {
            Increment(_lapBudgetMissingSignalCounts, $"race-projection:{signal}");
        }
    }

    private void RecordRaceControl(HistoricalTelemetrySample? sample, LiveRaceModels models)
    {
        Increment(_raceControlCounts, $"state:{models.Session.SessionState?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unknown"}");
        if (LiveRaceControlFlags.HasYellowFamily(models.Session.SessionFlags ?? sample?.SessionFlags))
        {
            Increment(_raceControlCounts, "yellow-family");
        }

        if (sample?.IsReplayPlaying == true)
        {
            Increment(_raceControlCounts, "replay");
        }
    }

    private void RecordWeather(LiveWeatherModel weather)
    {
        if (weather.TrackWetnessLabel is not null)
        {
            Increment(_weatherScopeCounts, $"wetness:{weather.TrackWetnessLabel}");
        }

        if (weather.WeatherDeclaredWet is { } wet)
        {
            Increment(_weatherScopeCounts, wet ? "declared-wet" : "declared-dry");
        }

        if (!string.IsNullOrWhiteSpace(weather.SkiesLabel))
        {
            Increment(_weatherScopeCounts, $"skies:{weather.SkiesLabel}");
        }
    }

    private void RecordPitService(LivePitServiceModel pit)
    {
        if (pit.Request.Fuel || pit.Request.FuelLiters is > 0d)
        {
            Increment(_pitServiceRequestCounts, "fuel");
        }

        if (pit.Request.RequestedTireCount > 0)
        {
            Increment(_pitServiceRequestCounts, $"tires:{pit.Request.RequestedTireCount}");
        }

        if (pit.Request.FastRepair)
        {
            Increment(_pitServiceRequestCounts, "fast-repair");
        }

        if (pit.Repair.RequiredSeconds is > 0d || pit.Repair.OptionalSeconds is > 0d)
        {
            Increment(_pitServiceRequestCounts, "repair-active");
        }
    }

    private void TrackLapBurnWindow(
        HistoricalTelemetrySample? sample,
        LapProgress? progress,
        double? currentFuel,
        IReadOnlyList<string> contextFlags,
        DateTimeOffset capturedAtUtc)
    {
        if (sample is null || progress is null || currentFuel is not { } fuelLiters || !IsPositiveFinite(fuelLiters))
        {
            RejectCleanAnchor("missing_fuel_or_progress", sample, progress, currentFuel, capturedAtUtc, contextFlags);
            return;
        }

        if (!contextFlags.Contains("clean-race", StringComparer.OrdinalIgnoreCase))
        {
            RejectCleanAnchor(contextFlags.FirstOrDefault(flag => !string.Equals(flag, "fuel-known", StringComparison.OrdinalIgnoreCase)) ?? "non_clean_context", sample, progress, currentFuel, capturedAtUtc, contextFlags);
            return;
        }

        if (_cleanLapAnchor is not { } anchor)
        {
            _cleanLapAnchor = FuelAnchor.From(progress, fuelLiters, sample.SessionTime, capturedAtUtc, contextFlags);
            return;
        }

        var progressDelta = progress.ProgressLaps - anchor.ProgressLaps;
        if (progressDelta < 0d)
        {
            AddLapWindow(_rejectedLapBurnWindows, "progress_reset", anchor, sample, progress, fuelLiters, capturedAtUtc, contextFlags, accepted: false);
            _cleanLapAnchor = FuelAnchor.From(progress, fuelLiters, sample.SessionTime, capturedAtUtc, contextFlags);
            return;
        }

        if (progressDelta < MinimumAcceptedLapProgress)
        {
            return;
        }

        var elapsedSeconds = sample.SessionTime - anchor.SessionTimeSeconds;
        var fuelUsed = anchor.FuelLiters - fuelLiters;
        double? fuelPerLap = progressDelta > 0d ? fuelUsed / progressDelta : null;
        var reason = LapWindowRejectionReason(progressDelta, elapsedSeconds, fuelUsed, fuelPerLap);
        if (reason is null)
        {
            AddLapWindow(_acceptedLapBurnWindows, "accepted", anchor, sample, progress, fuelLiters, capturedAtUtc, contextFlags, accepted: true);
        }
        else
        {
            AddLapWindow(_rejectedLapBurnWindows, reason, anchor, sample, progress, fuelLiters, capturedAtUtc, contextFlags, accepted: false);
        }

        _cleanLapAnchor = FuelAnchor.From(progress, fuelLiters, sample.SessionTime, capturedAtUtc, contextFlags);
    }

    private void TrackSectorBurn(
        HistoricalTelemetrySample? sample,
        LiveRaceModels models,
        LapProgress? progress,
        double? currentFuel,
        IReadOnlyList<string> contextFlags,
        DateTimeOffset capturedAtUtc)
    {
        if (models.TrackMap.HasSectors)
        {
            _framesWithSectorMetadata++;
        }

        if (sample is null || progress is null || currentFuel is not { } fuelLiters || !IsPositiveFinite(fuelLiters) || models.TrackMap.Sectors.Count == 0)
        {
            _sectorAnchor = null;
            return;
        }

        var sector = SectorFor(models.TrackMap.Sectors, progress.LapDistPct);
        if (sector is null)
        {
            _sectorAnchor = null;
            return;
        }

        if (_sectorAnchor is not { } anchor)
        {
            _sectorAnchor = SectorAnchor.From(progress, sector, fuelLiters, sample.SessionTime, capturedAtUtc, contextFlags);
            return;
        }

        if (progress.LapCompleted == anchor.LapCompleted && sector.SectorNum == anchor.SectorNum)
        {
            return;
        }

        var fuelUsed = anchor.FuelLiters - fuelLiters;
        var accepted = fuelUsed > 0d
            && !contextFlags.Any(IsSectorBaselineSuppressingFlag);
        if (accepted)
        {
            _sectorBurnWindowCount++;
        }
        else
        {
            _sectorBurnRejectedCount++;
        }

        if (_sectorBurnSamples.Count < _options.MaxSectorBurnSamples)
        {
            _sectorBurnSamples.Add(new FuelV2SectorBurnSample(
                CapturedAtUtc: capturedAtUtc,
                LapCompleted: anchor.LapCompleted,
                SectorNum: anchor.SectorNum,
                StartPct: Round(anchor.StartPct),
                EndPct: Round(sector.StartPct),
                FuelUsedLiters: fuelUsed > 0d ? Round(fuelUsed) : null,
                ProjectionLitersPerLap: SectorProjection(anchor.StartPct, sector.StartPct, fuelUsed),
                AcceptedForBaseline: accepted,
                ContextFlags: DistinctFlags(anchor.ContextFlags.Concat(contextFlags)),
                RejectionReason: accepted ? null : SectorRejectionReason(fuelUsed, contextFlags)));
        }

        _sectorAnchor = SectorAnchor.From(progress, sector, fuelLiters, sample.SessionTime, capturedAtUtc, contextFlags);
    }

    private void TrackPitWindow(
        HistoricalTelemetrySample? sample,
        LiveRaceModels models,
        double? currentFuel,
        DateTimeOffset capturedAtUtc)
    {
        var isPitContext = models.FuelPit.OnPitRoad
            || models.FuelPit.PitstopActive
            || models.FuelPit.PlayerCarInPitStall
            || models.FuelPit.TeamOnPitRoad == true;
        if (!isPitContext)
        {
            FinalizeActivePitWindow(capturedAtUtc);
            return;
        }

        if (_activePitWindow is null)
        {
            _activePitWindow = PitWindowBuilder.Start(capturedAtUtc, sample?.SessionTime, currentFuel, models);
        }

        _activePitWindow.Update(capturedAtUtc, sample?.SessionTime, currentFuel, models);
    }

    private void TrackStationaryServiceObservation(
        HistoricalTelemetrySample? sample,
        LiveRaceModels models,
        double? currentFuel,
        DateTimeOffset capturedAtUtc)
    {
        var completed = _stationaryServiceTracker.Track(
            PitServiceObservationFrame.From(
                capturedAtUtc,
                models.PitService,
                currentFuel,
                sample?.SessionTime));
        if (completed is not null)
        {
            RecordStationaryServiceObservation(completed);
        }
    }

    private void TrackTeamStint(
        HistoricalTelemetrySample? sample,
        LapProgress? progress,
        double? currentFuel,
        IReadOnlyList<string> contextFlags,
        DateTimeOffset capturedAtUtc)
    {
        if (sample is null
            || progress is null
            || contextFlags.Contains("pit-road", StringComparer.OrdinalIgnoreCase)
            || contextFlags.Contains("garage", StringComparer.OrdinalIgnoreCase))
        {
            FinalizeActiveTeamStint(capturedAtUtc);
            return;
        }

        if (_activeTeamStint is null)
        {
            _activeTeamStint = TeamStintBuilder.Start(capturedAtUtc, sample.SessionTime, progress, currentFuel);
        }

        _activeTeamStint.Update(capturedAtUtc, sample.SessionTime, progress, currentFuel);
    }

    private void TrackDriverChange(HistoricalTelemetrySample? sample, LiveTelemetrySnapshot snapshot, DateTimeOffset capturedAtUtc)
    {
        if (sample?.DriversSoFar is not { } driversSoFar)
        {
            return;
        }

        if (_lastDriversSoFar is { } previous && previous != driversSoFar)
        {
            _driverChangeEventCount++;
            AddEvent(
                "driver-control.changed",
                $"DCDriversSoFar changed from {previous} to {driversSoFar}",
                snapshot,
                capturedAtUtc);
        }

        _lastDriversSoFar = driversSoFar;
    }

    private void RecordSampleFrame(
        LiveTelemetrySnapshot snapshot,
        LiveRaceModels models,
        RawTelemetryWatchSnapshot rawWatch,
        string sessionKind,
        LapProgress? progress,
        double? currentFuel,
        double? lapProjection,
        IReadOnlyList<string> contextFlags,
        DateTimeOffset capturedAtUtc)
    {
        var countForSession = _sampleFrameCountsBySessionKind.TryGetValue(sessionKind, out var count) ? count : 0;
        if (countForSession >= _options.MaxSampleFramesPerSession)
        {
            _droppedFrameSampleCount++;
            return;
        }

        if (_lastSampledFrameAtUtcBySessionKind.TryGetValue(sessionKind, out var lastSampled)
            && (capturedAtUtc - lastSampled).TotalSeconds < _options.MinimumFrameSpacingSeconds)
        {
            _droppedFrameSampleCount++;
            return;
        }

        _sampleFrames.Add(new FuelV2FrameSample(
            CapturedAtUtc: capturedAtUtc,
            Sequence: snapshot.Sequence,
            SessionKind: sessionKind,
            SessionTimeSeconds: Round(models.Session.SessionTimeSeconds ?? snapshot.LatestSample?.SessionTime),
            SessionState: models.Session.SessionState ?? snapshot.LatestSample?.SessionState,
            SessionFlagsHex: FormatRawFlagsHex(models.Session.SessionFlags ?? snapshot.LatestSample?.SessionFlags),
            ContextFlags: contextFlags,
            Fuel: new FuelV2FrameFuelSample(
                FuelLevelLiters: Round(currentFuel),
                FuelLevelPercent: Round(snapshot.Fuel.FuelLevelPercent ?? snapshot.LatestSample?.FuelLevelPercent),
                FuelUsePerHourLiters: Round(snapshot.Fuel.FuelUsePerHourLiters),
                FuelUsePerHourKg: Round(snapshot.Fuel.FuelUsePerHourKg ?? snapshot.LatestSample?.FuelUsePerHourKg),
                MeasuredFuelPerLapLiters: Round(snapshot.Fuel.FuelPerLapLiters),
                CurrentLapProjectionLitersPerLap: Round(lapProjection),
                TankCapacityLiters: Round(snapshot.Context.Car.DriverCarFuelMaxLiters)),
            Progress: new FuelV2FrameProgressSample(
                Source: progress?.Source ?? "unavailable",
                LapCompleted: progress?.LapCompleted,
                LapDistPct: Round(progress?.LapDistPct),
                ProgressLaps: Round(progress?.ProgressLaps),
                LeaderProgressLaps: Round(models.RaceProgress.OverallLeaderProgressLaps),
                ClassLeaderProgressLaps: Round(models.RaceProgress.ClassLeaderProgressLaps),
                EstimatedFinishLap: Round(models.RaceProjection.EstimatedFinishLap),
                EstimatedTeamLapsRemaining: Round(models.RaceProjection.EstimatedTeamLapsRemaining),
                RaceLapsRemaining: Round(models.RaceProgress.RaceLapsRemaining),
                RaceLapsRemainingSource: models.RaceProgress.RaceLapsRemainingSource),
            Pit: FuelV2FramePitSample.From(models),
            Weather: FuelV2FrameWeatherSample.From(models.Weather),
            LapBudgetInputs: FuelV2FrameLapBudgetInputSample.From(snapshot, models),
            Evidence: new FuelV2FrameEvidenceSample(
                FuelLevel: EvidenceKey(models.FuelPit.FuelLevelEvidence),
                InstantaneousBurn: EvidenceKey(models.FuelPit.InstantaneousBurnEvidence),
                MeasuredBurn: EvidenceKey(models.FuelPit.MeasuredBurnEvidence),
                BaselineEligibility: EvidenceKey(models.FuelPit.BaselineEligibilityEvidence)),
            RawValues: rawWatch.Pick(RawValueNames)));
        _sampledFrameCount++;
        _sampleFrameCountsBySessionKind[sessionKind] = countForSession + 1;
        _lastSampledFrameAtUtcBySessionKind[sessionKind] = capturedAtUtc;
    }

    private void RejectCleanAnchor(
        string reason,
        HistoricalTelemetrySample? sample,
        LapProgress? progress,
        double? currentFuel,
        DateTimeOffset capturedAtUtc,
        IReadOnlyList<string> contextFlags)
    {
        if (_cleanLapAnchor is not { } anchor)
        {
            return;
        }

        if (sample is not null && progress is not null && currentFuel is { } fuelLiters && IsPositiveFinite(fuelLiters))
        {
            AddLapWindow(_rejectedLapBurnWindows, reason, anchor, sample, progress, fuelLiters, capturedAtUtc, contextFlags, accepted: false);
        }

        _cleanLapAnchor = null;
    }

    private void AddLapWindow(
        List<FuelV2LapBurnWindowSample> target,
        string reason,
        FuelAnchor anchor,
        HistoricalTelemetrySample sample,
        LapProgress progress,
        double currentFuel,
        DateTimeOffset capturedAtUtc,
        IReadOnlyList<string> contextFlags,
        bool accepted)
    {
        var progressDelta = progress.ProgressLaps - anchor.ProgressLaps;
        var elapsedSeconds = sample.SessionTime - anchor.SessionTimeSeconds;
        var fuelUsed = anchor.FuelLiters - currentFuel;
        var fuelPerLap = progressDelta > 0d && fuelUsed > 0d ? fuelUsed / progressDelta : (double?)null;
        if (accepted)
        {
            _acceptedLapBurnWindowCount++;
        }
        else
        {
            _rejectedLapBurnWindowCount++;
        }

        if (target.Count >= (accepted ? _options.MaxAcceptedLapWindows : _options.MaxRejectedLapWindows))
        {
            return;
        }

        target.Add(new FuelV2LapBurnWindowSample(
            StartedAtUtc: anchor.CapturedAtUtc,
            CompletedAtUtc: capturedAtUtc,
            StartedAtSessionTimeSeconds: Round(anchor.SessionTimeSeconds),
            CompletedAtSessionTimeSeconds: Round(sample.SessionTime),
            StartProgressLaps: Round(anchor.ProgressLaps),
            EndProgressLaps: Round(progress.ProgressLaps),
            ProgressDeltaLaps: Round(progressDelta),
            FuelStartLiters: Round(anchor.FuelLiters),
            FuelEndLiters: Round(currentFuel),
            FuelUsedLiters: fuelUsed > 0d ? Round(fuelUsed) : null,
            FuelPerLapLiters: Round(fuelPerLap),
            AcceptedForBaseline: accepted,
            RejectionReason: accepted ? null : reason,
            ContextFlags: DistinctFlags(anchor.ContextFlags.Concat(contextFlags))));
    }

    private void FinalizeActivePitWindow(DateTimeOffset endedAtUtc)
    {
        if (_activePitWindow is not { } window)
        {
            return;
        }

        var sample = window.Build(endedAtUtc);
        _pitWindowCount++;
        if (sample.SawFuelIncrease)
        {
            _pitWindowsWithFuelIncrease++;
        }

        if (_pitWindows.Count < _options.MaxPitWindows)
        {
            _pitWindows.Add(sample);
        }

        _activePitWindow = null;
    }

    private void FinalizeActiveStationaryServiceObservation()
    {
        var completed = _stationaryServiceTracker.Finish();
        if (completed is not null)
        {
            RecordStationaryServiceObservation(completed);
        }
    }

    private void RecordStationaryServiceObservation(PitServiceStationaryServiceObservation observation)
    {
        _stationaryServiceObservationCount++;
        if (_stationaryServiceObservations.Count < _options.MaxStationaryServiceObservations)
        {
            _stationaryServiceObservations.Add(observation);
        }
        else
        {
            _droppedStationaryServiceObservationCount++;
        }
    }

    private void FinalizeActiveTeamStint(DateTimeOffset endedAtUtc)
    {
        if (_activeTeamStint is not { } stint)
        {
            return;
        }

        var sample = stint.Build(endedAtUtc);
        if (sample.DistanceLaps.GetValueOrDefault() >= 0.1d || sample.DurationSeconds.GetValueOrDefault() >= 30d)
        {
            _teamStintCount++;
            if (_teamStints.Count < _options.MaxTeamStints)
            {
                _teamStints.Add(sample);
            }
        }

        _activeTeamStint = null;
    }

    private FuelV2SyntheticReplaySuitability BuildSyntheticReplaySuitability()
    {
        var reasons = new List<string>();
        if (_acceptedLapBurnWindowCount <= 0)
        {
            reasons.Add("no_local_fuel_truth_windows");
        }

        if (_teamStintCount <= 0)
        {
            reasons.Add("no_team_stint_shape");
        }

        if (_pitWindowCount <= 0)
        {
            reasons.Add("no_pit_window");
        }

        if (_framesWithTeamProgress <= 0)
        {
            reasons.Add("no_team_progress");
        }

        return new FuelV2SyntheticReplaySuitability(
            Suitable: reasons.Count == 0,
            Reasons: reasons.Count == 0 ? ["local fuel, team progress, stint, and pit evidence present"] : reasons.ToArray());
    }

    private void AddEvent(string kind, string detail, LiveTelemetrySnapshot snapshot, DateTimeOffset capturedAtUtc)
    {
        if (_eventSamples.Count >= _options.MaxEventExamplesPerSession)
        {
            _droppedEventSampleCount++;
            return;
        }

        _eventSamples.Add(new FuelV2EventSample(
            Kind: kind,
            CapturedAtUtc: capturedAtUtc,
            SessionTimeSeconds: Round(snapshot.LatestSample?.SessionTime),
            Sequence: snapshot.Sequence,
            Detail: detail));
    }

    private void AddBoundaryEvent(string boundaryKind, DateTimeOffset capturedAtUtc)
    {
        if (_eventSamples.Count >= _options.MaxEventExamplesPerSession)
        {
            _droppedEventSampleCount++;
            return;
        }

        _eventSamples.Add(new FuelV2EventSample(
            Kind: "session-boundary",
            CapturedAtUtc: capturedAtUtc,
            SessionTimeSeconds: null,
            Sequence: 0,
            Detail: boundaryKind));
    }

    private string ArtifactFileName(string sourceId)
    {
        return $"{SanitizeFileName(sourceId)}-{_options.OutputFileName}";
    }

    private static void WriteArtifactAtomically(string path, FuelV2CaptureArtifact artifact)
    {
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(artifact, JsonOptions), Encoding.UTF8);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private string ResolveArtifactPath(string? captureDirectory, string sourceId)
    {
        if (!string.IsNullOrWhiteSpace(captureDirectory))
        {
            return Path.Combine(captureDirectory, _options.CaptureDirectoryName, ArtifactFileName(sourceId));
        }

        return Path.Combine(DiagnosticsLogRoot, ArtifactFileName(sourceId));
    }

    private static IReadOnlyList<string> ContextFlags(
        HistoricalTelemetrySample? sample,
        LiveRaceModels models,
        LapProgress? progress,
        double? currentFuel,
        double? previousFuel,
        FuelAnchor? currentLapAnchor)
    {
        if (sample is null)
        {
            return ["no_sample"];
        }

        var flags = new List<string>();
        if (!IsPositiveFinite(sample.FuelLevelLiters))
        {
            flags.Add("fuel-missing");
        }
        else
        {
            flags.Add("fuel-known");
        }

        if (progress is null)
        {
            flags.Add("progress-missing");
        }

        if (models.Session.SessionState is null)
        {
            flags.Add("session-state-missing");
        }
        else if (models.Session.SessionState < GreenSessionState)
        {
            flags.Add("formation");
        }
        else if (models.Session.SessionState > GreenSessionState)
        {
            flags.Add("non-green-session-state");
        }

        if (LiveRaceControlFlags.HasYellowFamily(models.Session.SessionFlags ?? sample.SessionFlags))
        {
            flags.Add("caution-or-yellow");
        }

        if (models.FuelPit.OnPitRoad || models.FuelPit.TeamOnPitRoad == true || sample.OnPitRoad)
        {
            flags.Add("pit-road");
        }

        if (models.FuelPit.PitstopActive || sample.PitstopActive)
        {
            flags.Add("pit-service");
        }

        if (models.FuelPit.PlayerCarInPitStall || sample.PlayerCarInPitStall)
        {
            flags.Add("pit-stall");
        }

        if (sample.IsInGarage)
        {
            flags.Add("garage");
        }

        if (!sample.IsOnTrack)
        {
            flags.Add("not-on-track");
        }

        if (sample.PlayerTrackSurface is not null && sample.PlayerTrackSurface != OnTrackSurface)
        {
            flags.Add("off-track-surface");
        }

        if (previousFuel is { } previous && currentFuel is { } current && current - previous > MinimumPitFuelIncreaseLiters)
        {
            flags.Add("refuel");
        }

        if (currentLapAnchor is { } anchor && progress is not null && progress.ProgressLaps < anchor.ProgressLaps)
        {
            flags.Add("progress-reset");
        }

        if (sample.PlayerCarIdx is { } playerCarIdx
            && sample.FocusCarIdx is { } focusCarIdx
            && playerCarIdx != focusCarIdx)
        {
            flags.Add("focus-other-car");
        }

        if (flags.Count is 1
            && flags.Contains("fuel-known", StringComparer.OrdinalIgnoreCase)
            && progress is not null
            && models.Session.SessionState == GreenSessionState)
        {
            flags.Add("clean-race");
        }

        return flags.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static double? CurrentFuelLiters(LiveTelemetrySnapshot snapshot, LiveRaceModels models)
    {
        if (IsPositiveFinite(models.FuelPit.Fuel.FuelLevelLiters))
        {
            return models.FuelPit.Fuel.FuelLevelLiters;
        }

        if (IsPositiveFinite(snapshot.Fuel.FuelLevelLiters))
        {
            return snapshot.Fuel.FuelLevelLiters;
        }

        return IsPositiveFinite(snapshot.LatestSample?.FuelLevelLiters)
            ? snapshot.LatestSample!.FuelLevelLiters
            : null;
    }

    private double? CurrentLapProjection(HistoricalTelemetrySample? sample, LapProgress? progress, double? currentFuel)
    {
        if (sample is null || progress is null || currentFuel is not { } fuelLiters || !IsPositiveFinite(fuelLiters))
        {
            _currentLapAnchor = null;
            return null;
        }

        if (_currentLapAnchor is null
            || progress.LapCompleted != _currentLapAnchor.LapCompleted
            || progress.ProgressLaps < _currentLapAnchor.ProgressLaps)
        {
            _currentLapAnchor = FuelAnchor.From(progress, fuelLiters, sample.SessionTime, sample.CapturedAtUtc);
            return null;
        }

        var progressDelta = progress.ProgressLaps - _currentLapAnchor.ProgressLaps;
        var fuelUsed = _currentLapAnchor.FuelLiters - fuelLiters;
        return progressDelta > 0.05d && fuelUsed > 0d
            ? fuelUsed / progressDelta
            : null;
    }

    private static LapProgress? TeamProgress(HistoricalTelemetrySample? sample)
    {
        if (sample is null)
        {
            return null;
        }

        if (sample.TeamLapCompleted is { } teamLap
            && teamLap >= 0
            && sample.TeamLapDistPct is { } teamPct
            && IsFinite(teamPct)
            && teamPct >= 0d)
        {
            var pct = Math.Clamp(teamPct, 0d, 1d);
            return new LapProgress("team-car-array", teamLap, pct, teamLap + pct);
        }

        if (sample.LapCompleted >= 0 && IsFinite(sample.LapDistPct) && sample.LapDistPct >= 0d)
        {
            var pct = Math.Clamp(sample.LapDistPct, 0d, 1d);
            return new LapProgress("local-scalar", sample.LapCompleted, pct, sample.LapCompleted + pct);
        }

        return null;
    }

    private static LiveTrackSectorSegment? SectorFor(IReadOnlyList<LiveTrackSectorSegment> sectors, double lapDistPct)
    {
        return sectors
            .Where(sector => lapDistPct >= sector.StartPct && lapDistPct < sector.EndPct)
            .OrderBy(sector => sector.SectorNum)
            .FirstOrDefault()
            ?? sectors.LastOrDefault(sector => lapDistPct >= sector.StartPct);
    }

    private static string? LapWindowRejectionReason(double progressDelta, double elapsedSeconds, double fuelUsed, double? fuelPerLap)
    {
        if (progressDelta > MaximumAcceptedLapProgress)
        {
            return "progress_gap";
        }

        if (elapsedSeconds is < MinimumLapSeconds or > MaximumLapSeconds)
        {
            return "elapsed_time_out_of_range";
        }

        if (fuelUsed < MinimumFuelBurnLiters)
        {
            return "fuel_delta_too_small_or_refuel";
        }

        if (fuelPerLap is null || fuelPerLap > MaximumFuelBurnLitersPerLap)
        {
            return "fuel_per_lap_out_of_range";
        }

        return null;
    }

    private static double? SectorProjection(double startPct, double endPct, double fuelUsed)
    {
        var progress = endPct >= startPct
            ? endPct - startPct
            : 1d - startPct + endPct;
        return progress > 0d && fuelUsed > 0d
            ? Round(fuelUsed / progress)
            : null;
    }

    private static string SectorRejectionReason(double fuelUsed, IReadOnlyList<string> contextFlags)
    {
        if (fuelUsed <= 0d)
        {
            return "fuel_delta_negative_or_zero";
        }

        return contextFlags.FirstOrDefault(IsSectorBaselineSuppressingFlag)
            ?? "degraded_context";
    }

    private static bool IsSectorBaselineSuppressingFlag(string flag)
    {
        return flag is "refuel"
            or "progress-reset"
            or "pit-road"
            or "pit-service"
            or "pit-stall"
            or "garage"
            or "not-on-track"
            or "off-track-surface"
            or "caution-or-yellow"
            or "focus-other-car";
    }

    private static string SessionKind(HistoricalSessionContext context, LiveSessionModel session)
    {
        return session.SessionType
            ?? session.SessionName
            ?? context.Session.SessionType
            ?? context.Session.EventType
            ?? "unknown";
    }

    private static string EvidenceKey(LiveSignalEvidence evidence)
    {
        return evidence.MissingReason is null
            ? $"{evidence.Source}:{evidence.Quality}"
            : $"{evidence.Source}:{evidence.Quality}:{evidence.MissingReason}";
    }

    private static IReadOnlyDictionary<string, int> Sorted(Dictionary<string, int> values)
    {
        return values
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> DistinctFlags(IEnumerable<string> flags)
    {
        return flags
            .Where(flag => !string.IsNullOrWhiteSpace(flag))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void Increment(Dictionary<string, int> values, string key)
    {
        values[key] = values.TryGetValue(key, out var count) ? count + 1 : 1;
    }

    private static double? Round(double? value)
    {
        return value is { } finite && IsFinite(finite) ? Math.Round(finite, 6) : null;
    }

    private static bool IsPositiveFinite(double? value)
    {
        return value is { } finite && IsFinite(finite) && finite > 0d;
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }

    private void UpdateCapacityObservationScope(LiveTelemetrySnapshot snapshot, double? currentFuelLiters)
    {
        var context = snapshot.Context;
        var scope = new CapacityObservationScope(
            CarKey: snapshot.Combo.CarKey,
            SessionKey: snapshot.Combo.SessionKey,
            CurrentSessionNum: context.Session.CurrentSessionNum,
            SessionNum: context.Session.SessionNum,
            SubSessionId: context.Session.SubSessionId,
            PhysicalTankCapacityLiters: context.Car.DriverCarFuelMaxLiters,
            DriverCarMaxFuelPercent: context.FuelCapacityRules.DriverCarMaxFuelPercent,
            CarClassMaxFuelPercent: context.FuelCapacityRules.CarClassMaxFuelPercent);
        if (scope != _capacityObservationScope)
        {
            _capacityObservationScope = scope;
            _maxObservedFuelForCapacityScopeLiters = null;
        }

        if (currentFuelLiters is { } fuelLiters && IsPositiveFinite(fuelLiters))
        {
            _maxObservedFuelForCapacityScopeLiters = Math.Max(
                _maxObservedFuelForCapacityScopeLiters ?? 0d,
                fuelLiters);
        }
    }

    private static string FormatRawFlagsHex(int? flags)
    {
        return flags is { } value
            ? $"0x{unchecked((uint)value).ToString("X8", System.Globalization.CultureInfo.InvariantCulture)}"
            : "--";
    }

    private static string SanitizeFileName(string sourceId)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(sourceId.Select(character => invalid.Contains(character) ? '-' : character));
    }

    private static FuelV2SessionScopeSample SessionScope(
        LiveTelemetrySnapshot snapshot,
        LiveRaceModels models,
        double? currentFuelLiters,
        double? maxObservedFuelLiters)
    {
        var context = snapshot.Context;
        var combo = snapshot.Combo;
        var observedFuelCandidates = new[] { currentFuelLiters, maxObservedFuelLiters }
            .Where(value => IsPositiveFinite(value))
            .Select(value => value!.Value)
            .ToArray();
        var observedFuel = observedFuelCandidates.Length > 0
            ? observedFuelCandidates.Max()
            : (double?)null;
        var capacity = FuelV2EffectiveCapacityResolver.From(
            context.Car.DriverCarFuelMaxLiters,
            context.FuelCapacityRules.DriverCarMaxFuelPercent,
            context.FuelCapacityRules.CarClassMaxFuelPercent,
            observedFuel);
        var carIdentity = FuelV2HistoryIdentity.Car(
            context.Car.CarId,
            context.Car.CarPath);
        var layout = FuelV2HistoryIdentity.TrackLayout(
            context.Track.TrackId,
            context.Track.TrackName,
            FirstNonEmpty(context.Track.TrackDisplayName, models.Session.TrackDisplayName),
            context.Track.TrackConfigName);
        var sessionFamily = FuelV2HistoryIdentity.SessionFamily(
            FirstNonEmpty(models.Session.SessionType, context.Session.SessionType),
            FirstNonEmpty(models.Session.SessionName, context.Session.SessionName),
            FirstNonEmpty(models.Session.EventType, context.Session.EventType));
        return new FuelV2SessionScopeSample(
            Combo: new FuelV2ComboScope(
                CarKey: carIdentity.Key,
                TrackKey: combo.TrackKey,
                SessionKey: sessionFamily,
                TrackLayoutKey: layout.Key,
                TrackLayoutIdentitySource: layout.Source),
            Car: new FuelV2CarScope(
                CarId: context.Car.CarId,
                CarPath: context.Car.CarPath,
                CarScreenName: FirstNonEmpty(context.Car.CarScreenNameShort, context.Car.CarScreenName, models.Session.CarDisplayName),
                CarClassId: context.Car.CarClassId,
                CarClassShortName: context.Car.CarClassShortName,
                DriverCarVersion: context.Car.DriverCarVersion,
                DriverSetupName: context.Car.DriverSetupName,
                DriverSetupIsModified: context.Car.DriverSetupIsModified),
            Track: new FuelV2TrackScope(
                TrackId: context.Track.TrackId,
                TrackName: context.Track.TrackName,
                TrackDisplayName: FirstNonEmpty(context.Track.TrackDisplayName, models.Session.TrackDisplayName),
                TrackConfigName: context.Track.TrackConfigName,
                TrackLengthKm: Round(context.Track.TrackLengthKm ?? models.Session.TrackLengthKm),
                TrackVersion: context.Track.TrackVersion),
            Session: new FuelV2SessionIdentityScope(
                CurrentSessionNum: context.Session.CurrentSessionNum,
                SessionNum: context.Session.SessionNum,
                SessionType: FirstNonEmpty(models.Session.SessionType, context.Session.SessionType),
                SessionName: FirstNonEmpty(models.Session.SessionName, context.Session.SessionName),
                EventType: FirstNonEmpty(models.Session.EventType, context.Session.EventType),
                SessionLapsText: context.Session.SessionLaps,
                Official: context.Session.Official,
                TeamRacing: models.Session.TeamRacing ?? context.Session.TeamRacing,
                SeriesId: context.Session.SeriesId,
                SeasonId: context.Session.SeasonId,
                SessionId: context.Session.SessionId,
                SubSessionId: context.Session.SubSessionId,
                BuildVersion: context.Session.BuildVersion,
                DCRuleSet: context.Session.DCRuleSet,
                SessionTimeText: context.Session.SessionTime),
            FuelCapacity: new FuelV2FuelCapacityScope(
                PhysicalTankCapacityLiters: Round(context.Car.DriverCarFuelMaxLiters),
                FuelKgPerLiter: Round(context.Car.DriverCarFuelKgPerLiter),
                EffectiveSessionCapacityLiters: Round(capacity.EffectiveCapacityLiters),
                EffectiveSessionCapacitySource: FuelV2EffectiveCapacityResolver.SourceLabel(capacity.Source),
                DriverCarMaxFuelPercent: Round(capacity.DriverCarMaxFuelPercent),
                CarClassMaxFuelPercent: Round(capacity.CarClassMaxFuelPercent),
                Limitation: capacity.CanDriveFuelAdvice
                    ? string.Empty
                    : string.Join(',', capacity.StateFlags.Select(flag => flag.ToString()))),
            TrackSectors: context.Sectors
                .OrderBy(sector => sector.SectorNum)
                .Select(sector => new FuelV2TrackSectorScope(sector.SectorNum, Round(sector.SectorStartPct)))
                .ToArray());
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    private sealed record LapProgress(string Source, int LapCompleted, double LapDistPct, double ProgressLaps);

    private sealed record CapacityObservationScope(
        string CarKey,
        string SessionKey,
        int? CurrentSessionNum,
        int? SessionNum,
        int? SubSessionId,
        double? PhysicalTankCapacityLiters,
        double? DriverCarMaxFuelPercent,
        double? CarClassMaxFuelPercent);

    private sealed record FuelAnchor(
        int LapCompleted,
        double ProgressLaps,
        double FuelLiters,
        double SessionTimeSeconds,
        DateTimeOffset CapturedAtUtc,
        IReadOnlyList<string> ContextFlags)
    {
        public static FuelAnchor From(
            LapProgress progress,
            double fuelLiters,
            double sessionTimeSeconds,
            DateTimeOffset capturedAtUtc,
            IReadOnlyList<string>? contextFlags = null)
        {
            return new FuelAnchor(progress.LapCompleted, progress.ProgressLaps, fuelLiters, sessionTimeSeconds, capturedAtUtc, contextFlags ?? []);
        }
    }

    private sealed record SectorAnchor(
        int LapCompleted,
        int SectorNum,
        double StartPct,
        double FuelLiters,
        double SessionTimeSeconds,
        DateTimeOffset CapturedAtUtc,
        IReadOnlyList<string> ContextFlags)
    {
        public static SectorAnchor From(
            LapProgress progress,
            LiveTrackSectorSegment sector,
            double fuelLiters,
            double sessionTimeSeconds,
            DateTimeOffset capturedAtUtc,
            IReadOnlyList<string> contextFlags)
        {
            return new SectorAnchor(
                progress.LapCompleted,
                sector.SectorNum,
                sector.StartPct,
                fuelLiters,
                sessionTimeSeconds,
                capturedAtUtc,
                contextFlags);
        }
    }

    private sealed class PitWindowBuilder
    {
        private readonly DateTimeOffset _startedAtUtc;
        private readonly double? _startSessionTimeSeconds;
        private readonly double? _entryFuelLiters;
        private double? _lastSessionTimeSeconds;
        private double? _lastFuelLiters;
        private double? _maxFuelIncreaseLiters;
        private bool _sawFuelIncrease;
        private bool _sawPitStall;
        private bool _sawPitService;
        private bool _sawRepair;
        private int? _entryPitServiceFlags;
        private int? _lastPitServiceFlags;
        private double? _entryPitServiceFuelLiters;
        private double? _lastPitServiceFuelLiters;

        private PitWindowBuilder(DateTimeOffset startedAtUtc, double? sessionTimeSeconds, double? fuelLiters, LiveRaceModels models)
        {
            _startedAtUtc = startedAtUtc;
            _startSessionTimeSeconds = sessionTimeSeconds;
            _lastSessionTimeSeconds = sessionTimeSeconds;
            _entryFuelLiters = fuelLiters;
            _lastFuelLiters = fuelLiters;
            _entryPitServiceFlags = models.FuelPit.PitServiceFlags;
            _lastPitServiceFlags = models.FuelPit.PitServiceFlags;
            _entryPitServiceFuelLiters = models.FuelPit.PitServiceFuelLiters;
            _lastPitServiceFuelLiters = models.FuelPit.PitServiceFuelLiters;
        }

        public static PitWindowBuilder Start(DateTimeOffset startedAtUtc, double? sessionTimeSeconds, double? fuelLiters, LiveRaceModels models)
        {
            return new PitWindowBuilder(startedAtUtc, sessionTimeSeconds, fuelLiters, models);
        }

        public void Update(DateTimeOffset capturedAtUtc, double? sessionTimeSeconds, double? fuelLiters, LiveRaceModels models)
        {
            _lastSessionTimeSeconds = sessionTimeSeconds;
            _lastPitServiceFlags = models.FuelPit.PitServiceFlags;
            _lastPitServiceFuelLiters = models.FuelPit.PitServiceFuelLiters;
            _sawPitStall |= models.FuelPit.PlayerCarInPitStall;
            _sawPitService |= models.FuelPit.PitstopActive;
            _sawRepair |= models.FuelPit.PitRepairLeftSeconds is > 0d || models.FuelPit.PitOptRepairLeftSeconds is > 0d;

            if (fuelLiters is { } currentFuel)
            {
                var fuelIncrease = _lastFuelLiters is { } previousFuel
                    ? currentFuel - previousFuel
                    : _entryFuelLiters is { } entryFuel
                        ? currentFuel - entryFuel
                        : (double?)null;
                if (fuelIncrease is > MinimumPitFuelIncreaseLiters)
                {
                    _sawFuelIncrease = true;
                    _maxFuelIncreaseLiters = _maxFuelIncreaseLiters is null
                        ? fuelIncrease
                        : Math.Max(_maxFuelIncreaseLiters.Value, fuelIncrease.Value);
                }

                _lastFuelLiters = currentFuel;
            }
        }

        public FuelV2PitWindowSample Build(DateTimeOffset endedAtUtc)
        {
            return new FuelV2PitWindowSample(
                StartCapturedAtUtc: _startedAtUtc,
                EndCapturedAtUtc: endedAtUtc,
                StartSessionTimeSeconds: Round(_startSessionTimeSeconds),
                EndSessionTimeSeconds: Round(_lastSessionTimeSeconds),
                DurationSeconds: Round(Math.Max(0d, (endedAtUtc - _startedAtUtc).TotalSeconds)),
                EntryFuelLiters: Round(_entryFuelLiters),
                ExitFuelLiters: Round(_lastFuelLiters),
                NetFuelDeltaLiters: _entryFuelLiters is { } entry && _lastFuelLiters is { } exit ? Round(exit - entry) : null,
                MaxFuelIncreaseLiters: Round(_maxFuelIncreaseLiters),
                SawFuelIncrease: _sawFuelIncrease,
                SawPitStall: _sawPitStall,
                SawPitService: _sawPitService,
                SawRepair: _sawRepair,
                EntryPitServiceFlags: _entryPitServiceFlags,
                LastPitServiceFlags: _lastPitServiceFlags,
                EntryPitServiceFuelLiters: Round(_entryPitServiceFuelLiters),
                LastPitServiceFuelLiters: Round(_lastPitServiceFuelLiters));
        }
    }

    private sealed class TeamStintBuilder
    {
        private readonly DateTimeOffset _startedAtUtc;
        private readonly double _startSessionTimeSeconds;
        private readonly LapProgress _startProgress;
        private double? _fuelStartLiters;
        private double? _fuelEndLiters;
        private DateTimeOffset _lastCapturedAtUtc;
        private double _lastSessionTimeSeconds;
        private LapProgress _lastProgress;
        private bool _sawLocalFuel;

        private TeamStintBuilder(DateTimeOffset startedAtUtc, double sessionTimeSeconds, LapProgress progress, double? fuelLiters)
        {
            _startedAtUtc = startedAtUtc;
            _startSessionTimeSeconds = sessionTimeSeconds;
            _startProgress = progress;
            _lastCapturedAtUtc = startedAtUtc;
            _lastSessionTimeSeconds = sessionTimeSeconds;
            _lastProgress = progress;
            TrackFuel(fuelLiters);
        }

        public static TeamStintBuilder Start(DateTimeOffset startedAtUtc, double sessionTimeSeconds, LapProgress progress, double? fuelLiters)
        {
            return new TeamStintBuilder(startedAtUtc, sessionTimeSeconds, progress, fuelLiters);
        }

        public void Update(DateTimeOffset capturedAtUtc, double sessionTimeSeconds, LapProgress progress, double? fuelLiters)
        {
            _lastCapturedAtUtc = capturedAtUtc;
            _lastSessionTimeSeconds = sessionTimeSeconds;
            _lastProgress = progress;
            TrackFuel(fuelLiters);
        }

        public FuelV2TeamStintSample Build(DateTimeOffset fallbackEndedAtUtc)
        {
            var fuelUsed = _fuelStartLiters is { } startFuel && _fuelEndLiters is { } endFuel
                ? Math.Max(0d, startFuel - endFuel)
                : (double?)null;
            var distance = Math.Max(0d, _lastProgress.ProgressLaps - _startProgress.ProgressLaps);
            return new FuelV2TeamStintSample(
                StartedAtUtc: _startedAtUtc,
                EndedAtUtc: _lastCapturedAtUtc == _startedAtUtc ? fallbackEndedAtUtc : _lastCapturedAtUtc,
                StartSessionTimeSeconds: Round(_startSessionTimeSeconds),
                EndSessionTimeSeconds: Round(_lastSessionTimeSeconds),
                DurationSeconds: Round(Math.Max(0d, _lastSessionTimeSeconds - _startSessionTimeSeconds)),
                StartProgressLaps: Round(_startProgress.ProgressLaps),
                EndProgressLaps: Round(_lastProgress.ProgressLaps),
                DistanceLaps: Round(distance),
                FuelStartLiters: Round(_fuelStartLiters),
                FuelEndLiters: Round(_fuelEndLiters),
                FuelUsedLiters: Round(fuelUsed),
                FuelPerLapLiters: fuelUsed is { } used && distance > 0d ? Round(used / distance) : null,
                DriverRole: _sawLocalFuel ? "local-driver-scalar" : "team-driver-inferred",
                ConfidenceFlags: _sawLocalFuel ? ["local_fuel_scalar", "team_progress"] : ["team_progress", "fuel_unavailable"]);
        }

        private void TrackFuel(double? fuelLiters)
        {
            if (!IsPositiveFinite(fuelLiters))
            {
                return;
            }

            _sawLocalFuel = true;
            _fuelStartLiters ??= fuelLiters;
            _fuelEndLiters = fuelLiters;
        }
    }
}

internal sealed record FuelV2CaptureArtifact(
    int FormatVersion,
    string SourceId,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset FinishedAtUtc,
    AppVersionInfo AppVersion,
    FuelV2CaptureDataVersions DataVersions,
    FuelV2CaptureOutputScope Output,
    FuelV2SessionScopeSample? SessionScope,
    FuelV2CaptureArtifactOptions Options,
    FuelV2CaptureTotals Totals,
    FuelV2FuelEvidenceSummary Fuel,
    FuelV2LapBudgetEvidenceSummary LapBudget,
    FuelV2SectorBurnEvidenceSummary SectorBurn,
    FuelV2PitServiceEvidenceSummary PitService,
    FuelV2TeamEvidenceSummary Team,
    FuelV2RaceControlEvidenceSummary RaceControl,
    FuelV2WeatherEvidenceSummary Weather,
    FuelV2SyntheticReplaySuitability SyntheticReplaySuitability,
    IReadOnlyList<FuelV2FrameSample> SampleFrames,
    IReadOnlyList<FuelV2LapBurnWindowSample> AcceptedLapBurnWindows,
    IReadOnlyList<FuelV2LapBurnWindowSample> RejectedLapBurnWindows,
    IReadOnlyList<FuelV2SectorBurnSample> SectorBurnSamples,
    IReadOnlyList<FuelV2PitWindowSample> PitWindows,
    IReadOnlyList<FuelV2TeamStintSample> TeamStints,
    IReadOnlyList<FuelV2EventSample> EventSamples,
    FuelV2CaptureSessionLineage? SessionLineage = null,
    IReadOnlyList<PitServiceStationaryServiceObservation>? StationaryServiceObservations = null,
    FuelV2RaceBurnSelectorShadowEvidence? RaceBurnSelectorShadow = null);

// A bounded shadow record for the unpromoted race-burn selector. `ShadowOnly`
// is intentionally redundant on both the summary and every transition so a
// future consumer cannot mistake it for an approved live strategy plan.
internal sealed record FuelV2RaceBurnSelectorShadowEvidence(
    bool ShadowOnly,
    int FramesEvaluated,
    IReadOnlyDictionary<string, int> StateCounts,
    IReadOnlyDictionary<string, int> SelectedBucketCounts,
    IReadOnlyDictionary<string, int> CandidateBucketCounts,
    IReadOnlyDictionary<string, int> ConflictCounts,
    IReadOnlyDictionary<string, int> HistoricalStatusCounts,
    FuelV2RaceBurnSelectorShadowTransition? Latest,
    IReadOnlyList<FuelV2RaceBurnSelectorShadowTransition> Transitions);

internal sealed record FuelV2RaceBurnSelectorShadowTransition(
    DateTimeOffset CapturedAtUtc,
    string State,
    string? SelectedBucket,
    double? SelectedFuelPerLapLiters,
    string? CandidateBucket,
    double? CandidateFuelPerLapLiters,
    string HistoricalStatus,
    double? HistoricalFuelPerLapLiters,
    string Conflict,
    IReadOnlyList<string> Flags,
    string Reason,
    bool ShadowOnly,
    bool WouldSeedPlanIfPromoted,
    bool WouldDriveAdviceIfPromoted);

// Session lineage is written with a format-v2 artifact and copied into the
// durable summary. It is deliberately independent of Fuel/BOP/race-length
// values: those remain live-session context, not history family keys.
internal sealed record FuelV2CaptureSessionLineage(
    string? ConnectionSourceId,
    int SegmentOrdinal,
    string StartedByBoundaryKind,
    string EndedByBoundaryKind,
    string SessionFamily,
    string SessionOccurrenceKey,
    bool SessionOccurrenceVerified,
    string CarKey,
    string CarIdentitySource,
    string TrackLayoutKey,
    string TrackLayoutIdentitySource,
    bool ExactTrackLayoutVerified,
    bool ExactCarVerified);

internal sealed record FuelV2CaptureSessionIdentity(
    FuelV2HistoryCarIdentityKey CarIdentity,
    FuelV2HistoryLayoutIdentity TrackLayout,
    string SessionFamily,
    int? CurrentSessionNum,
    int? SessionNum,
    int? SessionId,
    int? SubSessionId)
{
    public string CarKey => CarIdentity.Key;

    public bool ExactCarVerified => CarIdentity.IsExact;

    public bool HasVerifiedSessionOccurrence => CurrentSessionNum is not null || SessionNum is not null;

    public bool IsReadyToRecord => ExactCarVerified
        && TrackLayout.IsExact
        && IsKnownSessionFamily(SessionFamily)
        && HasVerifiedSessionOccurrence;

    public static FuelV2CaptureSessionIdentity From(
        LiveTelemetrySnapshot snapshot,
        LiveSessionModel session)
    {
        var context = snapshot.Context;
        var trackLayout = FuelV2HistoryIdentity.TrackLayout(
            context.Track.TrackId,
            context.Track.TrackName,
            FirstNonEmpty(context.Track.TrackDisplayName, session.TrackDisplayName),
            context.Track.TrackConfigName);
        return new FuelV2CaptureSessionIdentity(
            CarIdentity: FuelV2HistoryIdentity.Car(context.Car.CarId, context.Car.CarPath),
            TrackLayout: trackLayout,
            SessionFamily: FuelV2HistoryIdentity.SessionFamily(
                FirstNonEmpty(session.SessionType, context.Session.SessionType),
                FirstNonEmpty(session.SessionName, context.Session.SessionName),
                FirstNonEmpty(session.EventType, context.Session.EventType)),
            CurrentSessionNum: context.Session.CurrentSessionNum,
            SessionNum: context.Session.SessionNum,
            SessionId: context.Session.SessionId,
            SubSessionId: context.Session.SubSessionId);
    }

    public bool Contradicts(FuelV2CaptureSessionIdentity next)
    {
        return (ExactCarVerified && next.ExactCarVerified
                && !string.Equals(CarKey, next.CarKey, StringComparison.OrdinalIgnoreCase))
            || (TrackLayout.IsExact && next.TrackLayout.IsExact
                && !string.Equals(TrackLayout.Key, next.TrackLayout.Key, StringComparison.OrdinalIgnoreCase))
            || (IsKnownSessionFamily(SessionFamily) && IsKnownSessionFamily(next.SessionFamily)
                && !string.Equals(SessionFamily, next.SessionFamily, StringComparison.OrdinalIgnoreCase))
            || KnownDistinct(CurrentSessionNum, next.CurrentSessionNum)
            || KnownDistinct(SessionNum, next.SessionNum)
            || KnownDistinct(SessionId, next.SessionId)
            || KnownDistinct(SubSessionId, next.SubSessionId);
    }

    public bool CanConfirm(FuelV2CaptureSessionIdentity active)
    {
        return IsReadyToRecord
            && active.IsReadyToRecord
            && string.Equals(CarKey, active.CarKey, StringComparison.OrdinalIgnoreCase)
            && string.Equals(TrackLayout.Key, active.TrackLayout.Key, StringComparison.OrdinalIgnoreCase)
            && string.Equals(SessionFamily, active.SessionFamily, StringComparison.OrdinalIgnoreCase)
            && !KnownDistinct(CurrentSessionNum, active.CurrentSessionNum)
            && !KnownDistinct(SessionNum, active.SessionNum)
            && !KnownDistinct(SessionId, active.SessionId)
            && !KnownDistinct(SubSessionId, active.SubSessionId);
    }

    public FuelV2CaptureSessionIdentity Merge(FuelV2CaptureSessionIdentity next)
    {
        return new FuelV2CaptureSessionIdentity(
            CarIdentity: next.ExactCarVerified ? next.CarIdentity : CarIdentity,
            TrackLayout: next.TrackLayout.IsExact ? next.TrackLayout : TrackLayout,
            SessionFamily: IsKnownSessionFamily(next.SessionFamily) ? next.SessionFamily : SessionFamily,
            CurrentSessionNum: next.CurrentSessionNum ?? CurrentSessionNum,
            SessionNum: next.SessionNum ?? SessionNum,
            SessionId: next.SessionId ?? SessionId,
            SubSessionId: next.SubSessionId ?? SubSessionId);
    }

    public static string SourceId(string connectionSourceId, int segmentOrdinal, string sessionFamily)
    {
        return $"{connectionSourceId}-fuel-v2-s{segmentOrdinal:D3}-{sessionFamily}";
    }

    public FuelV2CaptureSessionLineage ToLineage(
        string? connectionSourceId,
        int segmentOrdinal,
        string startedByBoundaryKind,
        string endedByBoundaryKind)
    {
        var occurrenceParts = new[]
        {
            CurrentSessionNum is { } currentSessionNum ? $"current-session:{currentSessionNum}" : null,
            SessionNum is { } sessionNum ? $"session:{sessionNum}" : null,
            SessionId is { } sessionId ? $"session-id:{sessionId}" : null,
            SubSessionId is { } subSessionId ? $"sub-session-id:{subSessionId}" : null
        }
        .Where(value => value is not null)
        .ToArray();
        var sessionOccurrenceVerified = CurrentSessionNum is not null || SessionNum is not null;
        return new FuelV2CaptureSessionLineage(
            ConnectionSourceId: connectionSourceId,
            SegmentOrdinal: segmentOrdinal,
            StartedByBoundaryKind: startedByBoundaryKind,
            EndedByBoundaryKind: endedByBoundaryKind,
            SessionFamily: SessionFamily,
            SessionOccurrenceKey: occurrenceParts.Length > 0
                ? string.Join("|", occurrenceParts)
                : "unverified",
            SessionOccurrenceVerified: sessionOccurrenceVerified,
            CarKey: CarKey,
            CarIdentitySource: CarIdentity.Source,
            TrackLayoutKey: TrackLayout.Key,
            TrackLayoutIdentitySource: TrackLayout.Source,
            ExactTrackLayoutVerified: TrackLayout.IsExact,
            ExactCarVerified: ExactCarVerified);
    }

    private static bool KnownDistinct(int? left, int? right)
    {
        return left is not null && right is not null && left != right;
    }

    private static bool IsKnownSessionFamily(string? value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && !string.Equals(value, "unknown", StringComparison.OrdinalIgnoreCase);
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }
}

internal sealed record FuelV2CaptureDataVersions(
    int HistoricalSummaryVersion,
    int HistoricalCollectionModelVersion,
    int HistoricalAggregateVersion,
    int LiveModelContractVersion);

internal sealed record FuelV2CaptureOutputScope(
    string Mode,
    bool CaptureDirectoryAttached,
    string CaptureDirectoryName,
    string OutputFileName,
    bool RawTelemetryExcluded,
    bool DurableHistoryMutated);

internal sealed record FuelV2SessionScopeSample(
    FuelV2ComboScope Combo,
    FuelV2CarScope Car,
    FuelV2TrackScope Track,
    FuelV2SessionIdentityScope Session,
    FuelV2FuelCapacityScope FuelCapacity,
    IReadOnlyList<FuelV2TrackSectorScope> TrackSectors);

internal sealed record FuelV2ComboScope(
    string CarKey,
    string TrackKey,
    string SessionKey,
    string? TrackLayoutKey = null,
    string TrackLayoutIdentitySource = "legacy-track-key");

internal sealed record FuelV2CarScope(
    int? CarId,
    string? CarPath,
    string? CarScreenName,
    int? CarClassId,
    string? CarClassShortName,
    string? DriverCarVersion,
    string? DriverSetupName,
    bool? DriverSetupIsModified);

internal sealed record FuelV2TrackScope(
    int? TrackId,
    string? TrackName,
    string? TrackDisplayName,
    string? TrackConfigName,
    double? TrackLengthKm,
    string? TrackVersion);

internal sealed record FuelV2SessionIdentityScope(
    int? CurrentSessionNum,
    int? SessionNum,
    string? SessionType,
    string? SessionName,
    string? EventType,
    string? SessionLapsText,
    bool? Official,
    bool? TeamRacing,
    int? SeriesId,
    int? SeasonId,
    int? SessionId,
    int? SubSessionId,
    string? BuildVersion,
    string? DCRuleSet = null,
    string? SessionTimeText = null);

internal sealed record FuelV2FuelCapacityScope(
    double? PhysicalTankCapacityLiters,
    double? FuelKgPerLiter,
    double? EffectiveSessionCapacityLiters,
    string EffectiveSessionCapacitySource,
    double? DriverCarMaxFuelPercent,
    double? CarClassMaxFuelPercent,
    string Limitation);

internal sealed record FuelV2TrackSectorScope(int SectorNum, double? SectorStartPct);

internal sealed record FuelV2CaptureArtifactOptions(
    double MinimumFrameSpacingSeconds,
    int MaxSampleFramesPerSession,
    int MaxEventExamplesPerSession,
    int MaxAcceptedLapWindows,
    int MaxRejectedLapWindows,
    int MaxSectorBurnSamples,
    int MaxPitWindows,
    int MaxTeamStints,
    int MaxStationaryServiceObservations = 80);

internal sealed record FuelV2CaptureTotals(
    int FrameCount,
    int SampledFrameCount,
    int DroppedFrameSampleCount,
    int DroppedEventSampleCount,
    IReadOnlyDictionary<string, int> SessionFrameCounts,
    IReadOnlyDictionary<string, int> ContextFlagCounts);

internal sealed record FuelV2FuelEvidenceSummary(
    int FramesWithLocalFuel,
    int FramesWithTeamProgress,
    int FramesWithTeamProgressWithoutLocalFuel,
    int FramesWithInstantaneousBurn,
    int FramesWithMeasuredBurn,
    int FramesBaselineEligible,
    double? MinFuelLiters,
    double? MaxFuelLiters,
    double? MaxObservedFuelIncreaseLiters,
    double? MaxObservedFuelDecreaseLiters,
    IReadOnlyDictionary<string, int> FuelEvidenceCounts);

internal sealed record FuelV2LapBudgetEvidenceSummary(
    int FramesWithLapBudget,
    int FramesWithRaceProjection,
    IReadOnlyDictionary<string, int> SourceCounts,
    IReadOnlyDictionary<string, int> MissingSignalCounts);

internal sealed record FuelV2SectorBurnEvidenceSummary(
    int FramesWithSectorMetadata,
    int AcceptedSectorWindows,
    int RejectedSectorWindows);

internal sealed record FuelV2PitServiceEvidenceSummary(
    int PitWindowCount,
    int PitWindowsWithFuelIncrease,
    IReadOnlyDictionary<string, int> RequestCounts,
    int StationaryServiceObservationCount = 0,
    int RetainedStationaryServiceObservationCount = 0,
    int DroppedStationaryServiceObservationCount = 0);

internal sealed record FuelV2TeamEvidenceSummary(
    int TeamStintCount,
    int DriverChangeEventCount);

internal sealed record FuelV2RaceControlEvidenceSummary(IReadOnlyDictionary<string, int> StateCounts);

internal sealed record FuelV2WeatherEvidenceSummary(IReadOnlyDictionary<string, int> ScopeCounts);

internal sealed record FuelV2SyntheticReplaySuitability(bool Suitable, IReadOnlyList<string> Reasons);

internal sealed record FuelV2FrameSample(
    DateTimeOffset CapturedAtUtc,
    long Sequence,
    string SessionKind,
    double? SessionTimeSeconds,
    int? SessionState,
    string SessionFlagsHex,
    IReadOnlyList<string> ContextFlags,
    FuelV2FrameFuelSample Fuel,
    FuelV2FrameProgressSample Progress,
    FuelV2FramePitSample Pit,
    FuelV2FrameWeatherSample Weather,
    FuelV2FrameLapBudgetInputSample LapBudgetInputs,
    FuelV2FrameEvidenceSample Evidence,
    IReadOnlyDictionary<string, double> RawValues);

internal sealed record FuelV2FrameFuelSample(
    double? FuelLevelLiters,
    double? FuelLevelPercent,
    double? FuelUsePerHourLiters,
    double? FuelUsePerHourKg,
    double? MeasuredFuelPerLapLiters,
    double? CurrentLapProjectionLitersPerLap,
    double? TankCapacityLiters);

internal sealed record FuelV2FrameProgressSample(
    string Source,
    int? LapCompleted,
    double? LapDistPct,
    double? ProgressLaps,
    double? LeaderProgressLaps,
    double? ClassLeaderProgressLaps,
    double? EstimatedFinishLap,
    double? EstimatedTeamLapsRemaining,
    double? RaceLapsRemaining,
    string RaceLapsRemainingSource);

internal sealed record FuelV2FrameLapBudgetInputSample(
    double? SessionTimeRemainSeconds,
    double? SessionTimeTotalSeconds,
    int? SessionLapsRemainEx,
    int? ModelSessionLapsRemain,
    int? SessionLapsTotal,
    int? RaceLaps,
    string? SessionLapsText,
    double? StrategyCarProgressLaps,
    double? ReferenceCarProgressLaps,
    double? OverallLeaderProgressLaps,
    double? ClassLeaderProgressLaps,
    double? StrategyLapTimeSeconds,
    string StrategyLapTimeSource,
    double? RacePaceSeconds,
    string RacePaceSource,
    double? OverallLeaderPaceSeconds,
    string OverallLeaderPaceSource,
    double? OverallLeaderPaceConfidence,
    double? ReferenceClassPaceSeconds,
    string ReferenceClassPaceSource,
    double? ReferenceClassPaceConfidence,
    double? TeamPaceSeconds,
    string TeamPaceSource,
    double? TeamPaceConfidence,
    IReadOnlyList<string> MissingSignals)
{
    public static FuelV2FrameLapBudgetInputSample From(LiveTelemetrySnapshot snapshot, LiveRaceModels models)
    {
        return new FuelV2FrameLapBudgetInputSample(
            SessionTimeRemainSeconds: Round(models.Session.SessionTimeRemainSeconds ?? snapshot.LatestSample?.SessionTimeRemain),
            SessionTimeTotalSeconds: Round(models.Session.SessionTimeTotalSeconds ?? snapshot.LatestSample?.SessionTimeTotal),
            SessionLapsRemainEx: snapshot.LatestSample?.SessionLapsRemainEx,
            ModelSessionLapsRemain: models.Session.SessionLapsRemain,
            SessionLapsTotal: models.Session.SessionLapsTotal ?? snapshot.LatestSample?.SessionLapsTotal,
            RaceLaps: models.Session.RaceLaps ?? snapshot.LatestSample?.RaceLaps,
            SessionLapsText: snapshot.Context.Session.SessionLaps,
            StrategyCarProgressLaps: Round(models.RaceProgress.StrategyCarProgressLaps),
            ReferenceCarProgressLaps: Round(models.RaceProgress.ReferenceCarProgressLaps),
            OverallLeaderProgressLaps: Round(models.RaceProgress.OverallLeaderProgressLaps),
            ClassLeaderProgressLaps: Round(models.RaceProgress.ClassLeaderProgressLaps),
            StrategyLapTimeSeconds: Round(models.RaceProgress.StrategyLapTimeSeconds),
            StrategyLapTimeSource: models.RaceProgress.StrategyLapTimeSource,
            RacePaceSeconds: Round(models.RaceProgress.RacePaceSeconds),
            RacePaceSource: models.RaceProgress.RacePaceSource,
            OverallLeaderPaceSeconds: Round(models.RaceProjection.OverallLeaderPaceSeconds),
            OverallLeaderPaceSource: models.RaceProjection.OverallLeaderPaceSource,
            OverallLeaderPaceConfidence: Round(models.RaceProjection.OverallLeaderPaceConfidence),
            ReferenceClassPaceSeconds: Round(models.RaceProjection.ReferenceClassPaceSeconds),
            ReferenceClassPaceSource: models.RaceProjection.ReferenceClassPaceSource,
            ReferenceClassPaceConfidence: Round(models.RaceProjection.ReferenceClassPaceConfidence),
            TeamPaceSeconds: Round(models.RaceProjection.TeamPaceSeconds),
            TeamPaceSource: models.RaceProjection.TeamPaceSource,
            TeamPaceConfidence: Round(models.RaceProjection.TeamPaceConfidence),
            MissingSignals: models.Session.MissingSignals
                .Select(signal => $"session:{signal}")
                .Concat(models.RaceProgress.MissingSignals.Select(signal => $"race-progress:{signal}"))
                .Concat(models.RaceProjection.MissingSignals.Select(signal => $"race-projection:{signal}"))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray());
    }

    private static double? Round(double? value)
    {
        return value is { } finite && !double.IsNaN(finite) && !double.IsInfinity(finite)
            ? Math.Round(finite, 6)
            : null;
    }
}

internal sealed record FuelV2FramePitSample(
    bool OnPitRoad,
    bool PitstopActive,
    bool PlayerCarInPitStall,
    bool? TeamOnPitRoad,
    int? PitServiceStatus,
    int? PitServiceFlags,
    double? PitServiceFuelLiters,
    double? RepairRequiredSeconds,
    double? RepairOptionalSeconds,
    int RequestedTireCount,
    bool FastRepairSelected)
{
    public static FuelV2FramePitSample From(LiveRaceModels models)
    {
        return new FuelV2FramePitSample(
            OnPitRoad: models.FuelPit.OnPitRoad,
            PitstopActive: models.FuelPit.PitstopActive,
            PlayerCarInPitStall: models.FuelPit.PlayerCarInPitStall,
            TeamOnPitRoad: models.FuelPit.TeamOnPitRoad,
            PitServiceStatus: models.FuelPit.PitServiceStatus,
            PitServiceFlags: models.FuelPit.PitServiceFlags,
            PitServiceFuelLiters: FuelV2CaptureRecorderRound(models.FuelPit.PitServiceFuelLiters),
            RepairRequiredSeconds: FuelV2CaptureRecorderRound(models.FuelPit.PitRepairLeftSeconds),
            RepairOptionalSeconds: FuelV2CaptureRecorderRound(models.FuelPit.PitOptRepairLeftSeconds),
            RequestedTireCount: models.PitService.Request.RequestedTireCount,
            FastRepairSelected: models.PitService.Request.FastRepair);
    }

    private static double? FuelV2CaptureRecorderRound(double? value)
    {
        return value is { } finite && !double.IsNaN(finite) && !double.IsInfinity(finite)
            ? Math.Round(finite, 6)
            : null;
    }
}

internal sealed record FuelV2FrameWeatherSample(
    int? TrackWetness,
    string? TrackWetnessLabel,
    bool? WeatherDeclaredWet,
    double? PrecipitationPercent,
    string? SkiesLabel,
    double? AirTempC,
    double? TrackTempCrewC)
{
    public static FuelV2FrameWeatherSample From(LiveWeatherModel weather)
    {
        return new FuelV2FrameWeatherSample(
            TrackWetness: weather.TrackWetness,
            TrackWetnessLabel: weather.TrackWetnessLabel,
            WeatherDeclaredWet: weather.WeatherDeclaredWet,
            PrecipitationPercent: Round(weather.PrecipitationPercent),
            SkiesLabel: weather.SkiesLabel,
            AirTempC: Round(weather.AirTempC),
            TrackTempCrewC: Round(weather.TrackTempCrewC));
    }

    private static double? Round(double? value)
    {
        return value is { } finite && !double.IsNaN(finite) && !double.IsInfinity(finite)
            ? Math.Round(finite, 6)
            : null;
    }
}

internal sealed record FuelV2FrameEvidenceSample(
    string FuelLevel,
    string InstantaneousBurn,
    string MeasuredBurn,
    string BaselineEligibility);

internal sealed record FuelV2LapBurnWindowSample(
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    double? StartedAtSessionTimeSeconds,
    double? CompletedAtSessionTimeSeconds,
    double? StartProgressLaps,
    double? EndProgressLaps,
    double? ProgressDeltaLaps,
    double? FuelStartLiters,
    double? FuelEndLiters,
    double? FuelUsedLiters,
    double? FuelPerLapLiters,
    bool AcceptedForBaseline,
    string? RejectionReason,
    IReadOnlyList<string> ContextFlags);

internal sealed record FuelV2SectorBurnSample(
    DateTimeOffset CapturedAtUtc,
    int LapCompleted,
    int SectorNum,
    double? StartPct,
    double? EndPct,
    double? FuelUsedLiters,
    double? ProjectionLitersPerLap,
    bool AcceptedForBaseline,
    IReadOnlyList<string> ContextFlags,
    string? RejectionReason);

internal sealed record FuelV2PitWindowSample(
    DateTimeOffset StartCapturedAtUtc,
    DateTimeOffset EndCapturedAtUtc,
    double? StartSessionTimeSeconds,
    double? EndSessionTimeSeconds,
    double? DurationSeconds,
    double? EntryFuelLiters,
    double? ExitFuelLiters,
    double? NetFuelDeltaLiters,
    double? MaxFuelIncreaseLiters,
    bool SawFuelIncrease,
    bool SawPitStall,
    bool SawPitService,
    bool SawRepair,
    int? EntryPitServiceFlags,
    int? LastPitServiceFlags,
    double? EntryPitServiceFuelLiters,
    double? LastPitServiceFuelLiters);

internal sealed record FuelV2TeamStintSample(
    DateTimeOffset StartedAtUtc,
    DateTimeOffset EndedAtUtc,
    double? StartSessionTimeSeconds,
    double? EndSessionTimeSeconds,
    double? DurationSeconds,
    double? StartProgressLaps,
    double? EndProgressLaps,
    double? DistanceLaps,
    double? FuelStartLiters,
    double? FuelEndLiters,
    double? FuelUsedLiters,
    double? FuelPerLapLiters,
    string DriverRole,
    IReadOnlyList<string> ConfidenceFlags);

internal sealed record FuelV2EventSample(
    string Kind,
    DateTimeOffset CapturedAtUtc,
    double? SessionTimeSeconds,
    long Sequence,
    string Detail);
