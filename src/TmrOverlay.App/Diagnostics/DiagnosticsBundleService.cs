using System.IO.Compression;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using TmrOverlay.Core.AppInfo;
using TmrOverlay.App.History;
using TmrOverlay.App.Installation;
using TmrOverlay.App.Localhost;
using TmrOverlay.App.Overlays;
using TmrOverlay.App.Overlays.BrowserSources;
using TmrOverlay.App.Overlays.CarRadar;
using TmrOverlay.App.Overlays.Flags;
using TmrOverlay.App.Overlays.FuelCalculator;
using TmrOverlay.App.Overlays.GapToLeader;
using TmrOverlay.App.Overlays.GarageCover;
using TmrOverlay.App.Overlays.InputState;
using TmrOverlay.App.Overlays.PitService;
using TmrOverlay.App.Overlays.Relative;
using TmrOverlay.App.Overlays.SessionWeather;
using TmrOverlay.App.Overlays.Standings;
using TmrOverlay.App.Overlays.StreamChat;
using TmrOverlay.App.Overlays.TrackMap;
using TmrOverlay.App.Performance;
using TmrOverlay.App.Settings;
using TmrOverlay.App.Storage;
using TmrOverlay.App.Telemetry;
using TmrOverlay.App.TrackMaps;
using TmrOverlay.App.Updates;
using TmrOverlay.Core.Analysis;
using TmrOverlay.Core.Overlays;
using TmrOverlay.Core.Settings;
using TmrOverlay.Core.History;
using TmrOverlay.Core.Telemetry.Live;

namespace TmrOverlay.App.Diagnostics;

internal sealed class DiagnosticsBundleService
{
    private const int MaxRecentAnalysisFiles = 12;
    private const int MaxRecentHistorySummaryFiles = 50;
    private const int MaxRecentHistoryAggregateFiles = 50;
    private const int MaxRecentFuelV2HistorySummaryFiles = 50;
    private const int MaxRecentFuelV2HistoryAggregateFiles = 50;
    private const int MaxRecentEdgeCaseFiles = 20;
    private const int MaxLatestCaptureIbtAnalysisFiles = 12;
    private const int MaxRecentModelParityFiles = 10;
    private const int MaxRecentOverlayDiagnosticsFiles = 10;
    private const int MaxRecentFuelV2CaptureFiles = 10;
    private const int MaxRecentTrackMapReports = 10;
    private const int MaxRecentEventFilesForDiagnostics = 10;
    private const int MaxBundleNameSegmentLength = 48;
    private const int MaxLiveTelemetryCarExamples = 20;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly AppStorageOptions _storageOptions;
    private readonly LiveModelParityOptions _liveModelParityOptions;
    private readonly LiveOverlayDiagnosticsOptions _liveOverlayDiagnosticsOptions;
    private readonly FuelV2CaptureOptions _fuelV2CaptureOptions;
    private readonly string _fuelV2HistoryRoot;
    private readonly string _fuelV2HistoryBundleRoot;
    private readonly IbtAnalysisOptions _ibtAnalysisOptions;
    private readonly TelemetryCaptureState _captureState;
    private readonly LocalhostOverlayState _localhostOverlayState;
    private readonly TrackMapStore _trackMapStore;
    private readonly AppSettingsStore _settingsStore;
    private readonly ILiveTelemetrySource _liveTelemetrySource;
    private readonly BrowserOverlayModelFactory _browserOverlayModelFactory;
    private readonly SessionPreviewState _sessionPreviewState;
    private readonly AppPerformanceState _performanceState;
    private readonly AppPerformanceSnapshotRecorder _performanceRecorder;
    private readonly LiveOverlayWindowCaptureStore _liveOverlayWindowCaptureStore;
    private readonly ForegroundWindowTracker _foregroundWindowTracker;
    private readonly ReleaseUpdateService _releaseUpdates;
    private readonly StreamChatOverlaySource _streamChatSource;
    private readonly ILogger<DiagnosticsBundleService> _logger;
    private readonly object _sync = new();
    private string? _lastBundlePath;
    private DateTimeOffset? _lastBundleCreatedAtUtc;
    private string? _lastBundleSource;
    private string? _lastError;
    private DateTimeOffset? _lastErrorAtUtc;
    private string? _lastErrorSource;

    public DiagnosticsBundleService(
        AppStorageOptions storageOptions,
        LiveModelParityOptions liveModelParityOptions,
        LiveOverlayDiagnosticsOptions liveOverlayDiagnosticsOptions,
        IbtAnalysisOptions ibtAnalysisOptions,
        TelemetryCaptureState captureState,
        LocalhostOverlayState localhostOverlayState,
        TrackMapStore trackMapStore,
        AppSettingsStore settingsStore,
        ILiveTelemetrySource liveTelemetrySource,
        BrowserOverlayModelFactory browserOverlayModelFactory,
        SessionPreviewState sessionPreviewState,
        AppPerformanceState performanceState,
        AppPerformanceSnapshotRecorder performanceRecorder,
        LiveOverlayWindowCaptureStore liveOverlayWindowCaptureStore,
        ForegroundWindowTracker foregroundWindowTracker,
        ReleaseUpdateService releaseUpdates,
        StreamChatOverlaySource streamChatSource,
        ILogger<DiagnosticsBundleService> logger,
        FuelV2CaptureOptions? fuelV2CaptureOptions = null,
        FuelV2HistoryOptions? fuelV2HistoryOptions = null)
    {
        _storageOptions = storageOptions;
        _liveModelParityOptions = liveModelParityOptions;
        _liveOverlayDiagnosticsOptions = liveOverlayDiagnosticsOptions;
        _fuelV2CaptureOptions = fuelV2CaptureOptions ?? new FuelV2CaptureOptions();
        _fuelV2HistoryRoot = fuelV2HistoryOptions?.ResolvedHistoryRoot
            ?? Path.Combine(storageOptions.UserHistoryRoot, "fuel-v2");
        _fuelV2HistoryBundleRoot = $"history/user/{fuelV2HistoryOptions?.DirectoryName ?? "fuel-v2"}";
        _ibtAnalysisOptions = ibtAnalysisOptions;
        _captureState = captureState;
        _localhostOverlayState = localhostOverlayState;
        _trackMapStore = trackMapStore;
        _settingsStore = settingsStore;
        _liveTelemetrySource = liveTelemetrySource;
        _browserOverlayModelFactory = browserOverlayModelFactory;
        _sessionPreviewState = sessionPreviewState;
        _performanceState = performanceState;
        _performanceRecorder = performanceRecorder;
        _liveOverlayWindowCaptureStore = liveOverlayWindowCaptureStore;
        _foregroundWindowTracker = foregroundWindowTracker;
        _releaseUpdates = releaseUpdates;
        _streamChatSource = streamChatSource;
        _logger = logger;
    }

    public DiagnosticsBundleStatus Snapshot()
    {
        lock (_sync)
        {
            return new DiagnosticsBundleStatus(
                _lastBundlePath,
                _lastBundleCreatedAtUtc,
                _lastBundleSource,
                _lastError,
                _lastErrorAtUtc,
                _lastErrorSource);
        }
    }

    public string CreateBundle(string source = DiagnosticsBundleSources.Manual)
    {
        var bundleStarted = System.Diagnostics.Stopwatch.GetTimestamp();
        var bundleSucceeded = false;
        try
        {
            Directory.CreateDirectory(_storageOptions.DiagnosticsRoot);
            var createdAtUtc = DateTimeOffset.UtcNow;
            var bundleIdentity = ResolveBundleIdentity();
            var bundlePath = CreateUniqueBundlePath(createdAtUtc, bundleIdentity);

            using var archive = ZipFile.Open(bundlePath, ZipArchiveMode.Create);
            var liveOverlayWindowsSnapshot = _liveOverlayWindowCaptureStore.Snapshot();

            var metadataStarted = System.Diagnostics.Stopwatch.GetTimestamp();
            var metadataSucceeded = false;
            try
            {
                AddTextEntry(archive, "metadata/app-version.json", JsonSerializer.Serialize(AppVersionInfo.Current, JsonOptions));
                AddTextEntry(archive, "metadata/diagnostics-bundle.json", JsonSerializer.Serialize(new
                {
                    CreatedAtUtc = createdAtUtc,
                    Source = source,
                    FileName = Path.GetFileName(bundlePath),
                    Naming = new
                    {
                        bundleIdentity.CarName,
                        bundleIdentity.TrackName,
                        bundleIdentity.CarSlug,
                        bundleIdentity.TrackSlug,
                        bundleIdentity.Source
                    }
                }, JsonOptions));
                AddTextEntry(archive, "metadata/storage.json", JsonSerializer.Serialize(_storageOptions, JsonOptions));
                AddTextEntry(archive, "metadata/telemetry-state.json", JsonSerializer.Serialize(_captureState.Snapshot(), JsonOptions));
                var localhostSnapshot = _localhostOverlayState.Snapshot();
                AddTextEntry(archive, "metadata/localhost-overlays.json", JsonSerializer.Serialize(localhostSnapshot, JsonOptions));
                AddTextEntry(archive, "metadata/browser-overlays.json", JsonSerializer.Serialize(BrowserOverlayDiagnostics(), JsonOptions));
                AddTextEntry(archive, "metadata/localhost-overlay-models.json", JsonSerializer.Serialize(LocalhostOverlayModelDiagnostics(localhostSnapshot), JsonOptions));
                AddTextEntry(archive, "metadata/session-preview.json", JsonSerializer.Serialize(_sessionPreviewState.Snapshot(), JsonOptions));
                AddTextEntry(archive, "metadata/shared-settings-contract.json", JsonSerializer.Serialize(SharedOverlayContract.DiagnosticsSnapshot(), JsonOptions));
                AddTextEntry(archive, "metadata/overlay-geometry-contract.json", JsonSerializer.Serialize(OverlayGeometryContractDiagnostics(), JsonOptions));
                AddTextEntry(archive, "metadata/release-updates.json", JsonSerializer.Serialize(_releaseUpdates.Snapshot(), JsonOptions));
                AddTextEntry(archive, "metadata/installer-cleanup.json", JsonSerializer.Serialize(InstallerCleanup.LegacyInstallerCleanupSnapshot(), JsonOptions));
                AddTextEntry(archive, "metadata/evidence-quality.json", JsonSerializer.Serialize(EvidenceQualityDiagnostics(liveOverlayWindowsSnapshot), JsonOptions));
                AddTextEntry(archive, "metadata/latest-capture-evidence.json", JsonSerializer.Serialize(LatestCaptureEvidenceDiagnostics(), JsonOptions));
                AddTextEntry(archive, "metadata/ibt-analysis.json", JsonSerializer.Serialize(IbtAnalysisDiagnostics(), JsonOptions));
                AddTextEntry(archive, "metadata/track-maps.json", JsonSerializer.Serialize(TrackMapDiagnostics(), JsonOptions));
                AddTextEntry(archive, "metadata/garage-cover.json", JsonSerializer.Serialize(GarageCoverDiagnostics(), JsonOptions));
                AddTextEntry(archive, "metadata/stream-chat.json", JsonSerializer.Serialize(StreamChatDiagnostics(), JsonOptions));
                AddTextEntry(archive, "metadata/flags.json", JsonSerializer.Serialize(FlagsDiagnostics(), JsonOptions));
                AddTextEntry(archive, "metadata/live-telemetry-synthesis.json", JsonSerializer.Serialize(LiveTelemetrySynthesis(), JsonOptions));
                metadataSucceeded = true;
            }
            finally
            {
                _performanceState.RecordOperation(
                    AppPerformanceMetricIds.DiagnosticsBundleMetadata,
                    metadataStarted,
                    metadataSucceeded);
            }

            var runtimeSettingsStarted = System.Diagnostics.Stopwatch.GetTimestamp();
            var runtimeSettingsSucceeded = false;
            try
            {
                AddFileIfExists(archive, _storageOptions.RuntimeStatePath, "runtime/runtime-state.json");
                AddSharedContractFiles(archive);
                AddSanitizedSettingsIfExists(archive, Path.Combine(_storageOptions.SettingsRoot, "settings.json"));
                runtimeSettingsSucceeded = true;
            }
            finally
            {
                _performanceState.RecordOperation(
                    AppPerformanceMetricIds.DiagnosticsBundleRuntimeSettings,
                    runtimeSettingsStarted,
                    runtimeSettingsSucceeded);
            }

            var logsStarted = System.Diagnostics.Stopwatch.GetTimestamp();
            var logsSucceeded = false;
            try
            {
                AddRecentFiles(archive, _storageOptions.LogsRoot, "*.log", "logs", maxFiles: 10);
                logsSucceeded = true;
            }
            finally
            {
                _performanceState.RecordOperation(
                    AppPerformanceMetricIds.DiagnosticsBundleLogs,
                    logsStarted,
                    logsSucceeded);
            }

            var performanceStarted = System.Diagnostics.Stopwatch.GetTimestamp();
            var performanceSucceeded = false;
            try
            {
                AddRecentFiles(archive, _performanceRecorder.PerformanceLogsRoot, "*.jsonl", "performance", maxFiles: 10);
                performanceSucceeded = true;
            }
            finally
            {
                _performanceState.RecordOperation(
                    AppPerformanceMetricIds.DiagnosticsBundlePerformanceFiles,
                    performanceStarted,
                    performanceSucceeded);
            }

            var eventsStarted = System.Diagnostics.Stopwatch.GetTimestamp();
            var eventsSucceeded = false;
            try
            {
                AddRecentFiles(archive, _storageOptions.EventsRoot, "*.jsonl", "events", maxFiles: 10);
                eventsSucceeded = true;
            }
            finally
            {
                _performanceState.RecordOperation(
                    AppPerformanceMetricIds.DiagnosticsBundleEvents,
                    eventsStarted,
                    eventsSucceeded);
            }

            var latestCaptureStarted = System.Diagnostics.Stopwatch.GetTimestamp();
            var latestCaptureSucceeded = false;
            try
            {
                AddLatestCaptureMetadata(archive);
                latestCaptureSucceeded = true;
            }
            finally
            {
                _performanceState.RecordOperation(
                    AppPerformanceMetricIds.DiagnosticsBundleLatestCapture,
                    latestCaptureStarted,
                    latestCaptureSucceeded);
            }

            var edgeCasesStarted = System.Diagnostics.Stopwatch.GetTimestamp();
            var edgeCasesSucceeded = false;
            try
            {
                AddRecentFiles(
                    archive,
                    Path.Combine(_storageOptions.LogsRoot, "edge-cases"),
                    "*-edge-cases.json",
                    "edge-cases",
                    MaxRecentEdgeCaseFiles);
                edgeCasesSucceeded = true;
            }
            finally
            {
                _performanceState.RecordOperation(
                AppPerformanceMetricIds.DiagnosticsBundleEdgeCases,
                edgeCasesStarted,
                edgeCasesSucceeded);
            }

            var modelParityStarted = System.Diagnostics.Stopwatch.GetTimestamp();
            var modelParitySucceeded = false;
            try
            {
                AddRecentFiles(
                    archive,
                    Path.Combine(_storageOptions.LogsRoot, _liveModelParityOptions.LogDirectoryName),
                    $"*{_liveModelParityOptions.OutputFileName}",
                    "model-parity",
                    MaxRecentModelParityFiles);
                modelParitySucceeded = true;
            }
            finally
            {
                _performanceState.RecordOperation(
                    "diagnostics.bundle.model-parity",
                    modelParityStarted,
                    modelParitySucceeded);
            }

            var overlayDiagnosticsStarted = System.Diagnostics.Stopwatch.GetTimestamp();
            var overlayDiagnosticsSucceeded = false;
            try
            {
                AddRecentFiles(
                    archive,
                    Path.Combine(_storageOptions.LogsRoot, _liveOverlayDiagnosticsOptions.LogDirectoryName),
                    $"*{_liveOverlayDiagnosticsOptions.OutputFileName}",
                    "overlay-diagnostics",
                    MaxRecentOverlayDiagnosticsFiles);
                overlayDiagnosticsSucceeded = true;
            }
            finally
            {
                _performanceState.RecordOperation(
                    AppPerformanceMetricIds.DiagnosticsBundleOverlayDiagnostics,
                    overlayDiagnosticsStarted,
                    overlayDiagnosticsSucceeded);
            }

            var fuelV2CaptureStarted = System.Diagnostics.Stopwatch.GetTimestamp();
            var fuelV2CaptureSucceeded = false;
            try
            {
                AddRecentFiles(
                    archive,
                    Path.Combine(_storageOptions.LogsRoot, _fuelV2CaptureOptions.LogDirectoryName),
                    $"*{_fuelV2CaptureOptions.OutputFileName}",
                    "fuel-v2-capture",
                    MaxRecentFuelV2CaptureFiles);
                fuelV2CaptureSucceeded = true;
            }
            finally
            {
                _performanceState.RecordOperation(
                    "diagnostics.bundle.fuel-v2-capture",
                    fuelV2CaptureStarted,
                    fuelV2CaptureSucceeded);
            }

            var liveOverlayWindowsStarted = System.Diagnostics.Stopwatch.GetTimestamp();
            var liveOverlayWindowsSucceeded = false;
            try
            {
                AddLiveOverlayWindows(archive, liveOverlayWindowsSnapshot);
                liveOverlayWindowsSucceeded = true;
            }
            finally
            {
                _performanceState.RecordOperation(
                    AppPerformanceMetricIds.DiagnosticsBundleLiveOverlayWindows,
                    liveOverlayWindowsStarted,
                    liveOverlayWindowsSucceeded);
            }

            var windowZOrderStarted = System.Diagnostics.Stopwatch.GetTimestamp();
            var windowZOrderSucceeded = false;
            try
            {
                AddTextEntry(
                    archive,
                    "metadata/window-z-order.json",
                    JsonSerializer.Serialize(
                        WindowsTopLevelWindowDiagnostics.Capture(_foregroundWindowTracker.SnapshotHistory()),
                        JsonOptions));
                windowZOrderSucceeded = true;
            }
            finally
            {
                _performanceState.RecordOperation(
                    AppPerformanceMetricIds.DiagnosticsBundleWindowZOrder,
                    windowZOrderStarted,
                    windowZOrderSucceeded);
            }

            var historyStarted = System.Diagnostics.Stopwatch.GetTimestamp();
            var historySucceeded = false;
            try
            {
                AddUserHistoryMetadata(archive);
                historySucceeded = true;
            }
            finally
            {
                _performanceState.RecordOperation(
                    AppPerformanceMetricIds.DiagnosticsBundleHistory,
                    historyStarted,
                    historySucceeded);
            }

            var trackMapsStarted = System.Diagnostics.Stopwatch.GetTimestamp();
            var trackMapsSucceeded = false;
            try
            {
                AddRecentFiles(
                    archive,
                    Path.Combine(_storageOptions.LogsRoot, "track-maps"),
                    "*.json",
                    "track-maps",
                    MaxRecentTrackMapReports);
                trackMapsSucceeded = true;
            }
            finally
            {
                _performanceState.RecordOperation(
                    "diagnostics.bundle.track-maps",
                    trackMapsStarted,
                    trackMapsSucceeded);
            }

            var performanceSnapshot = _performanceState.Snapshot();
            AddTextEntry(archive, "metadata/performance.json", JsonSerializer.Serialize(performanceSnapshot, JsonOptions));
            AddTextEntry(archive, "metadata/ui-freeze-watch.json", JsonSerializer.Serialize(UiFreezeWatch(performanceSnapshot), JsonOptions));

            _logger.LogInformation("Created diagnostics bundle {DiagnosticsBundlePath}.", bundlePath);
            RecordSuccess(bundlePath, createdAtUtc, source);
            bundleSucceeded = true;
            return bundlePath;
        }
        catch (Exception exception)
        {
            RecordFailure(exception, source);
            throw;
        }
        finally
        {
            _performanceState.RecordOperation(
                AppPerformanceMetricIds.DiagnosticsBundleCreate,
                bundleStarted,
                bundleSucceeded);
        }
    }

    private string CreateUniqueBundlePath(DateTimeOffset createdAtUtc, DiagnosticsBundleIdentity identity)
    {
        var timestamp = createdAtUtc.UtcDateTime.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
        var baseName = $"{identity.CarSlug}-{identity.TrackSlug}-{timestamp}";
        var path = Path.Combine(_storageOptions.DiagnosticsRoot, $"{baseName}.zip");
        for (var index = 2; File.Exists(path); index++)
        {
            path = Path.Combine(_storageOptions.DiagnosticsRoot, $"{baseName}-{index}.zip");
        }

        return path;
    }

    private DiagnosticsBundleIdentity ResolveBundleIdentity()
    {
        var captureDirectory = LatestCaptureDirectory();
        if (!string.IsNullOrWhiteSpace(captureDirectory))
        {
            var latestSessionPath = Path.Combine(captureDirectory, "latest-session.yaml");
            if (File.Exists(latestSessionPath))
            {
                try
                {
                    var context = SessionInfoSummaryParser.Parse(File.ReadAllText(latestSessionPath));
                    if (TryBuildBundleIdentity(context, "latest-capture", out var captureIdentity))
                    {
                        return captureIdentity;
                    }
                }
                catch (Exception exception)
                {
                    _logger.LogDebug(exception, "Failed to parse latest session info for diagnostics bundle naming.");
                }
            }
        }

        try
        {
            if (TryBuildBundleIdentity(_liveTelemetrySource.Snapshot().Context, "live-telemetry", out var liveIdentity))
            {
                return liveIdentity;
            }
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Failed to read live telemetry context for diagnostics bundle naming.");
        }

        if (TryResolveRecentAnalysisBundleIdentity(out var analysisIdentity))
        {
            return analysisIdentity;
        }

        if (TryResolveRecentAggregateBundleIdentity(out var aggregateIdentity))
        {
            return aggregateIdentity;
        }

        return new DiagnosticsBundleIdentity(
            CarName: "unknown car",
            TrackName: "unknown track",
            CarSlug: "unknown-car",
            TrackSlug: "unknown-track",
            Source: "fallback");
    }

    private bool TryResolveRecentAnalysisBundleIdentity(out DiagnosticsBundleIdentity identity)
    {
        identity = default!;

        var analysisDirectory = Path.Combine(_storageOptions.UserHistoryRoot, "analysis");
        foreach (var file in EnumerateRecentFilesForNaming(analysisDirectory, "*.json", MaxRecentAnalysisFiles))
        {
            var analysis = ReadNamingJson<PostRaceAnalysis>(file.FullName, "post-race analysis");
            if (analysis is null)
            {
                continue;
            }

            if (analysis.Combo is not null
                && TryResolveAggregateBundleIdentity(analysis.Combo, "history-analysis", out identity))
            {
                return true;
            }

            var carName = ExtractAnalysisCarName(analysis);
            var trackName = ExtractAnalysisTrackName(analysis);
            if (analysis.Combo is not null)
            {
                carName ??= analysis.Combo.CarKey;
                trackName ??= analysis.Combo.TrackKey;
            }

            if (TryBuildBundleIdentity(carName, trackName, "history-analysis", out identity))
            {
                return true;
            }
        }

        return false;
    }

    private bool TryResolveRecentAggregateBundleIdentity(out DiagnosticsBundleIdentity identity)
    {
        identity = default!;

        var carsRoot = Path.Combine(_storageOptions.UserHistoryRoot, "cars");
        foreach (var file in EnumerateRecentRecursiveFilesForNaming(carsRoot, "aggregate.json", MaxRecentHistoryAggregateFiles))
        {
            var aggregate = ReadNamingJson<HistoricalSessionAggregate>(file.FullName, "history aggregate");
            if (aggregate is not null
                && TryBuildBundleIdentity(aggregate.Car, aggregate.Track, "history-aggregate", out identity))
            {
                return true;
            }
        }

        return false;
    }

    private bool TryResolveAggregateBundleIdentity(
        HistoricalComboIdentity combo,
        string source,
        out DiagnosticsBundleIdentity identity)
    {
        identity = default!;

        if (string.IsNullOrWhiteSpace(combo.CarKey)
            || string.IsNullOrWhiteSpace(combo.TrackKey)
            || string.IsNullOrWhiteSpace(combo.SessionKey))
        {
            return false;
        }

        var aggregatePath = Path.Combine(
            _storageOptions.UserHistoryRoot,
            "cars",
            combo.CarKey,
            "tracks",
            combo.TrackKey,
            "sessions",
            combo.SessionKey,
            "aggregate.json");
        var aggregate = ReadNamingJson<HistoricalSessionAggregate>(aggregatePath, "history aggregate");
        return aggregate is not null
            && TryBuildBundleIdentity(aggregate.Car, aggregate.Track, source, out identity);
    }

    private static bool TryBuildBundleIdentity(
        HistoricalSessionContext context,
        string source,
        out DiagnosticsBundleIdentity identity)
    {
        return TryBuildBundleIdentity(context.Car, context.Track, source, out identity);
    }

    private static bool TryBuildBundleIdentity(
        HistoricalCarIdentity? car,
        HistoricalTrackIdentity? track,
        string source,
        out DiagnosticsBundleIdentity identity)
    {
        var carName = FirstNonEmpty(car?.CarScreenNameShort, car?.CarScreenName, car?.CarPath);
        var trackName = FirstNonEmpty(track?.TrackDisplayName, track?.TrackName, track?.TrackConfigName);
        return TryBuildBundleIdentity(carName, trackName, source, out identity);
    }

    private static bool TryBuildBundleIdentity(
        string? carName,
        string? trackName,
        string source,
        out DiagnosticsBundleIdentity identity)
    {
        if (string.IsNullOrWhiteSpace(carName) && string.IsNullOrWhiteSpace(trackName))
        {
            identity = default!;
            return false;
        }

        identity = new DiagnosticsBundleIdentity(
            CarName: carName ?? "unknown car",
            TrackName: trackName ?? "unknown track",
            CarSlug: SlugSegment(carName, "unknown-car"),
            TrackSlug: SlugSegment(trackName, "unknown-track"),
            Source: source);
        return true;
    }

    private static string SlugSegment(string? value, string fallback)
    {
        var slug = SessionHistoryPath.Slug(value);
        if (string.IsNullOrWhiteSpace(slug) || string.Equals(slug, "unknown", StringComparison.OrdinalIgnoreCase))
        {
            slug = fallback;
        }

        return slug.Length <= MaxBundleNameSegmentLength
            ? slug
            : slug[..MaxBundleNameSegmentLength].Trim('-');
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

    private IEnumerable<FileInfo> EnumerateRecentFilesForNaming(
        string directory,
        string searchPattern,
        int maxFiles)
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        try
        {
            return Directory
                .EnumerateFiles(directory, searchPattern)
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Take(maxFiles)
                .ToArray();
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Failed to enumerate {Directory} for diagnostics bundle naming.", directory);
            return [];
        }
    }

    private IEnumerable<FileInfo> EnumerateRecentRecursiveFilesForNaming(
        string directory,
        string searchPattern,
        int maxFiles)
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        try
        {
            return Directory
                .EnumerateFiles(directory, searchPattern, SearchOption.AllDirectories)
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Take(maxFiles)
                .ToArray();
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Failed to enumerate {Directory} recursively for diagnostics bundle naming.", directory);
            return [];
        }
    }

    private T? ReadNamingJson<T>(string path, string description)
        where T : class
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize<T>(stream, JsonOptions);
        }
        catch (Exception exception)
        {
            _logger.LogDebug(
                exception,
                "Failed to parse {Description} {Path} for diagnostics bundle naming.",
                description,
                path);
            return null;
        }
    }

    private static string? ExtractAnalysisCarName(PostRaceAnalysis analysis)
    {
        return TextBeforeDelimiter(analysis.Subtitle, " | ");
    }

    private static string? ExtractAnalysisTrackName(PostRaceAnalysis analysis)
    {
        return TextBeforeDelimiter(analysis.Title, " - ");
    }

    private static string? TextBeforeDelimiter(string? value, string delimiter)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var index = value.IndexOf(delimiter, StringComparison.Ordinal);
        return index > 0
            ? value[..index].Trim()
            : value.Trim();
    }

    private void RecordSuccess(string bundlePath, DateTimeOffset createdAtUtc, string source)
    {
        lock (_sync)
        {
            _lastBundlePath = bundlePath;
            _lastBundleCreatedAtUtc = createdAtUtc;
            _lastBundleSource = source;
            _lastError = null;
            _lastErrorAtUtc = null;
            _lastErrorSource = null;
        }
    }

    private void RecordFailure(Exception exception, string source)
    {
        lock (_sync)
        {
            _lastError = exception.Message;
            _lastErrorAtUtc = DateTimeOffset.UtcNow;
            _lastErrorSource = source;
        }
    }

    private void AddUserHistoryMetadata(ZipArchive archive)
    {
        if (!Directory.Exists(_storageOptions.UserHistoryRoot))
        {
            return;
        }

        AddRecentFiles(
            archive,
            Path.Combine(_storageOptions.UserHistoryRoot, "analysis"),
            "*.json",
            "analysis",
            MaxRecentAnalysisFiles);
        AddFileIfExists(
            archive,
            Path.Combine(_storageOptions.UserHistoryRoot, ".maintenance", "manifest.json"),
            "history/user/.maintenance/manifest.json");

        var carsRoot = Path.Combine(_storageOptions.UserHistoryRoot, "cars");
        AddRecentRecursiveFiles(
            archive,
            carsRoot,
            file => string.Equals(file.Name, "aggregate.json", StringComparison.OrdinalIgnoreCase),
            "history/user/cars",
            MaxRecentHistoryAggregateFiles);
        AddRecentRecursiveFiles(
            archive,
            carsRoot,
            file => string.Equals(file.Directory?.Name, "summaries", StringComparison.OrdinalIgnoreCase),
            "history/user/cars",
            MaxRecentHistorySummaryFiles);

        AddFileIfExists(
            archive,
            Path.Combine(_fuelV2HistoryRoot, "manifest.json"),
            $"{_fuelV2HistoryBundleRoot}/manifest.json");
        var fuelV2CarsRoot = Path.Combine(_fuelV2HistoryRoot, "cars");
        AddRecentRecursiveFiles(
            archive,
            fuelV2CarsRoot,
            file => string.Equals(file.Name, "aggregate.json", StringComparison.OrdinalIgnoreCase),
            $"{_fuelV2HistoryBundleRoot}/cars",
            MaxRecentFuelV2HistoryAggregateFiles);
        AddRecentRecursiveFiles(
            archive,
            fuelV2CarsRoot,
            file => string.Equals(file.Directory?.Name, "summaries", StringComparison.OrdinalIgnoreCase),
            $"{_fuelV2HistoryBundleRoot}/cars",
            MaxRecentFuelV2HistorySummaryFiles);
    }

    private void AddLatestCaptureMetadata(ZipArchive archive)
    {
        var captureDirectory = LatestCaptureDirectory();
        if (string.IsNullOrWhiteSpace(captureDirectory) || !Directory.Exists(captureDirectory))
        {
            return;
        }

        AddFileIfExists(archive, Path.Combine(captureDirectory, "capture-manifest.json"), "latest-capture/capture-manifest.json");
        AddFileIfExists(archive, Path.Combine(captureDirectory, "telemetry-schema.json"), "latest-capture/telemetry-schema.json");
        AddFileIfExists(archive, Path.Combine(captureDirectory, "latest-session.yaml"), "latest-capture/latest-session.yaml");
        AddFileIfExists(archive, Path.Combine(captureDirectory, "capture-synthesis.json"), "latest-capture/capture-synthesis.json");
        AddFileIfExists(
            archive,
            Path.Combine(captureDirectory, _liveModelParityOptions.OutputFileName),
            $"latest-capture/{_liveModelParityOptions.OutputFileName}");
        AddFileIfExists(
            archive,
            Path.Combine(captureDirectory, _liveOverlayDiagnosticsOptions.OutputFileName),
            $"latest-capture/{_liveOverlayDiagnosticsOptions.OutputFileName}");
        // A Fuel V2 connection can now contain several classified session
        // sidecars (practice, qualifying, race). Retain the legacy fixed-name
        // sidecar when present, but never collapse the current segment files.
        AddRecentFiles(
            archive,
            Path.Combine(captureDirectory, _fuelV2CaptureOptions.CaptureDirectoryName),
            $"*{_fuelV2CaptureOptions.OutputFileName}",
            $"latest-capture/{_fuelV2CaptureOptions.CaptureDirectoryName}",
            MaxRecentFuelV2CaptureFiles);
        AddRecentFiles(
            archive,
            Path.Combine(captureDirectory, "ibt-analysis"),
            "*.json",
            "latest-capture/ibt-analysis",
            MaxLatestCaptureIbtAnalysisFiles);
    }

    private string? LatestCaptureDirectory()
    {
        var snapshot = _captureState.Snapshot();
        return snapshot.CurrentCaptureDirectory ?? snapshot.LastCaptureDirectory;
    }

    private object EvidenceQualityDiagnostics(LiveOverlayWindowCaptureManifest liveOverlays)
    {
        var now = DateTimeOffset.UtcNow;
        var liveSnapshot = _liveTelemetrySource.Snapshot();
        var lastActiveSnapshot = _liveTelemetrySource.LastActiveSnapshot();
        var localhost = _localhostOverlayState.Snapshot();
        var releaseUpdateSnapshot = _releaseUpdates.Snapshot();
        var updateEvents = UpdateEventDiagnostics();
        var updateApplyShutdown = UpdateApplyShutdownDiagnostics(releaseUpdateSnapshot);
        var latestCapture = LatestCaptureDirectory();
        var warnings = new List<string>();

        if (!liveSnapshot.IsConnected && lastActiveSnapshot is not null)
        {
            warnings.Add("current_live_telemetry_disconnected_use_last_active");
        }

        if (localhost.Enabled && localhost.TotalRequests == 0)
        {
            warnings.Add("localhost_enabled_without_route_requests");
        }

        foreach (var warning in liveOverlays.EvidenceWarnings)
        {
            warnings.Add(warning);
        }

        if (string.IsNullOrWhiteSpace(latestCapture) || !Directory.Exists(latestCapture))
        {
            warnings.Add("latest_capture_missing");
        }

        if (updateEvents.UpdateCheckFailedCount > 0)
        {
            warnings.Add("recent_update_check_failures");
        }

        if (updateEvents.UpdateFailureSummary.TransientFailureCount > 0)
        {
            warnings.Add("transient_update_check_failures");
        }

        if (string.Equals(
                updateApplyShutdown.Classification,
                "update_apply_shutdown_incomplete",
                StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add("update_apply_shutdown_incomplete");
        }

        var visibleWithoutPixelEvidence = liveOverlays.Overlays
            .Where(overlay => overlay.ActualVisible && string.IsNullOrWhiteSpace(overlay.ScreenshotPath))
            .Select(overlay => overlay.OverlayId)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var visibleWithoutCurrentScreenshots = liveOverlays.Overlays
            .Where(overlay => overlay.ActualVisible && !overlay.ScreenshotRepresentsCurrentState)
            .Select(overlay => overlay.OverlayId)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var canProveVisibleOverlayPixels = liveOverlays.ScreenshotCoverage.VisibleOverlayCount > 0
            && liveOverlays.ScreenshotCoverage.VisibleOverlayCount == liveOverlays.ScreenshotCoverage.CurrentScreenshotOverlayCount;

        return new
        {
            GeneratedAtUtc = now,
            Warnings = warnings.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray(),
            LiveTelemetry = new
            {
                CurrentConnected = liveSnapshot.IsConnected,
                CurrentCollecting = liveSnapshot.IsCollecting,
                CurrentSourceId = liveSnapshot.SourceId,
                CurrentSequence = liveSnapshot.Sequence,
                CurrentLastUpdatedAtUtc = liveSnapshot.LastUpdatedAtUtc,
                LastActiveAvailable = lastActiveSnapshot is not null,
                LastActiveSourceId = lastActiveSnapshot?.SourceId,
                LastActiveSequence = lastActiveSnapshot?.Sequence,
                LastActiveLastUpdatedAtUtc = lastActiveSnapshot?.LastUpdatedAtUtc,
                LastActiveAgeSeconds = lastActiveSnapshot?.LastUpdatedAtUtc is { } updatedAt
                    ? Math.Round(Math.Max(0d, (now - updatedAt).TotalSeconds), 3)
                    : (double?)null
            },
            Localhost = new
            {
                localhost.Enabled,
                localhost.Status,
                localhost.TotalRequests,
                localhost.LastRequestAtUtc,
                localhost.LastRequestRoute,
                localhost.HasRecentRequests
            },
            LiveOverlayWindows = new
            {
                liveOverlays.CaptureScreenshotsEnabled,
                liveOverlays.ScreenshotCoverage,
                liveOverlays.EvidenceWarnings,
                VisualProof = new
                {
                    CanProveVisibleOverlayPixels = canProveVisibleOverlayPixels,
                    VisibleOverlayIdsWithoutPixelEvidence = visibleWithoutPixelEvidence,
                    VisibleOverlayIdsWithoutCurrentScreenshots = visibleWithoutCurrentScreenshots,
                    Limitation = canProveVisibleOverlayPixels
                        ? "All currently visible live overlay windows have current screenshot/pixel evidence in this bundle."
                        : "The bundle cannot prove the current pixels for every visible live overlay window. Use live-overlays/manifest.json and live-overlays/*.png when present; otherwise reproduce with screenshot capture enabled."
                }
            },
            UpdateEvents = updateEvents,
            UpdateFlow = new
            {
                HasRecentUpdateCheckFailures = updateEvents.UpdateCheckFailedCount > 0,
                LatestFailureAtUtc = updateEvents.LatestUpdateCheckFailureAtUtc,
                LatestFailureSource = updateEvents.LatestUpdateCheckFailureSource,
                LatestFailureError = updateEvents.LatestUpdateCheckFailureError,
                LatestSuccessAtUtc = updateEvents.LatestUpdateCheckSuccessAtUtc,
                LatestSuccessSource = updateEvents.LatestUpdateCheckSuccessSource,
                LatestSuccessResult = updateEvents.LatestUpdateCheckSuccessResult,
                Summary = updateEvents.UpdateFailureSummary,
                ApplyShutdown = updateApplyShutdown,
                Interpretation = updateEvents.UpdateFailureSummary.Interpretation
            },
            LatestCapture = new
            {
                CaptureDirectory = latestCapture,
                Exists = !string.IsNullOrWhiteSpace(latestCapture) && Directory.Exists(latestCapture),
                CaptureManifestExists = !string.IsNullOrWhiteSpace(latestCapture) && File.Exists(Path.Combine(latestCapture, "capture-manifest.json")),
                CaptureSynthesisExists = !string.IsNullOrWhiteSpace(latestCapture) && File.Exists(Path.Combine(latestCapture, "capture-synthesis.json")),
                LiveOverlayDiagnosticsExists = !string.IsNullOrWhiteSpace(latestCapture) && File.Exists(Path.Combine(latestCapture, _liveOverlayDiagnosticsOptions.OutputFileName)),
                LiveModelParityExists = !string.IsNullOrWhiteSpace(latestCapture) && File.Exists(Path.Combine(latestCapture, _liveModelParityOptions.OutputFileName)),
                FuelV2CaptureExists = !string.IsNullOrWhiteSpace(latestCapture)
                    && FuelV2CapturePaths(latestCapture).TotalCount > 0,
                IbtStatusExists = !string.IsNullOrWhiteSpace(latestCapture) && File.Exists(Path.Combine(latestCapture, IbtAnalysisOutputDirectoryName(), "status.json"))
            }
        };
    }

    private object LatestCaptureEvidenceDiagnostics()
    {
        var captureDirectory = LatestCaptureDirectory();
        if (string.IsNullOrWhiteSpace(captureDirectory) || !Directory.Exists(captureDirectory))
        {
            return new
            {
                CaptureDirectory = captureDirectory,
                Exists = false
            };
        }

        var manifest = TryReadCaptureManifest(captureDirectory);
        var latestSessionPath = Path.Combine(captureDirectory, "latest-session.yaml");
        HistoricalSessionContext? context = null;
        IReadOnlyList<SessionInfoSetupSignal> setupSignals = [];
        string? latestSessionReadError = null;
        if (File.Exists(latestSessionPath))
        {
            try
            {
                var yaml = File.ReadAllText(latestSessionPath);
                context = SessionInfoSummaryParser.Parse(yaml);
                setupSignals = ExtractSetupSignals(yaml);
            }
            catch (Exception exception)
            {
                latestSessionReadError = exception.GetType().Name;
            }
        }

        var synthesisPath = Path.Combine(captureDirectory, "capture-synthesis.json");
        var synthesis = TryReadJsonObject(synthesisPath);
        var liveOverlayDiagnosticsPath = Path.Combine(captureDirectory, _liveOverlayDiagnosticsOptions.OutputFileName);
        var liveOverlayDiagnostics = TryReadJsonObject(liveOverlayDiagnosticsPath);
        var fuelV2CapturePaths = FuelV2CapturePaths(captureDirectory);
        var fuelV2CapturePath = fuelV2CapturePaths.RecentPaths.FirstOrDefault();
        var fuelV2Capture = fuelV2CapturePath is null ? null : TryReadJsonObject(fuelV2CapturePath);
        var lapDeltaQuality = LapDeltaQualityFromDiagnostics(liveOverlayDiagnostics?["lapDelta"] as JsonObject);
        var lapProfileReadiness = LapProfileReadinessFromDiagnostics(liveOverlayDiagnostics?["lapProfile"] as JsonObject);
        var postRaceFuelEvidence = PostRaceFuelEvidence(synthesis, liveOverlayDiagnostics);
        var fuelV2CaptureEvidence = FuelV2CaptureEvidence(fuelV2Capture);

        return new
        {
            CaptureDirectory = captureDirectory,
            Exists = true,
            Manifest = manifest is null
                ? null
                : new
                {
                    manifest.CaptureId,
                    manifest.CollectionId,
                    manifest.StartedAtUtc,
                    manifest.FinishedAtUtc,
                    manifest.FrameCount,
                    manifest.DroppedFrameCount,
                    manifest.SessionInfoSnapshotCount,
                    manifest.TickRate,
                    manifest.VariableCount
                },
            LatestSession = new
            {
                Path = latestSessionPath,
                Exists = File.Exists(latestSessionPath),
                ReadError = latestSessionReadError,
                SessionType = context?.Session.SessionType,
                SessionName = context?.Session.SessionName,
                EventType = context?.Session.EventType,
                CurrentSessionNum = context?.Session.CurrentSessionNum,
                IsRaceSession = IsRaceSession(context),
                TrackId = context?.Track.TrackId,
                TrackName = context?.Track.TrackName,
                TrackDisplayName = context?.Track.TrackDisplayName,
                TrackLengthKm = context?.Track.TrackLengthKm,
                SetupSignalCount = setupSignals.Count,
                SetupSignals = setupSignals
            },
            SetupAdjustmentEvidence = SetupAdjustmentEvidence(setupSignals),
            Synthesis = new
            {
                Path = synthesisPath,
                Exists = File.Exists(synthesisPath),
                TotalFrameRecords = (int?)synthesis?["frameScan"]?["totalFrameRecords"],
                SampledFrameCount = (int?)synthesis?["frameScan"]?["sampledFrameCount"],
                ValidDistanceLaps = (double?)synthesis?["session"]?["metrics"]?["validDistanceLaps"],
                CompletedValidLaps = (int?)synthesis?["session"]?["metrics"]?["completedValidLaps"]
            },
            PostRaceFuelEvidence = postRaceFuelEvidence,
            FuelV2Capture = new
            {
                Path = fuelV2CapturePath,
                Exists = fuelV2CapturePath is not null,
                fuelV2CaptureEvidence.FormatVersion,
                fuelV2CaptureEvidence.FrameCount,
                fuelV2CaptureEvidence.SampledFrameCount,
                fuelV2CaptureEvidence.AcceptedLapBurnWindowCount,
                fuelV2CaptureEvidence.RejectedLapBurnWindowCount,
                fuelV2CaptureEvidence.PitWindowCount,
                fuelV2CaptureEvidence.TeamStintCount,
                fuelV2CaptureEvidence.SyntheticReplaySuitable,
                fuelV2CaptureEvidence.SyntheticReplayReasons,
                fuelV2CaptureEvidence.SessionFrameCounts,
                fuelV2CaptureEvidence.ContextFlagCounts,
                fuelV2CaptureEvidence.LapBudgetSourceCounts,
                fuelV2CaptureEvidence.LapBudgetMissingSignalCounts,
                // SegmentCount is the full durable capture count. Segments is
                // deliberately capped for diagnostics-bundle size, and its
                // count is reported separately so support does not mistake
                // the included sample for the total history.
                SegmentCount = fuelV2CapturePaths.TotalCount,
                IncludedSegmentCount = fuelV2CapturePaths.RecentPaths.Count,
                Segments = fuelV2CapturePaths.RecentPaths.Select(path =>
                {
                    var evidence = FuelV2CaptureEvidence(TryReadJsonObject(path));
                    return new
                    {
                        Path = path,
                        FileName = Path.GetFileName(path),
                        evidence.FormatVersion,
                        evidence.FrameCount,
                        evidence.AcceptedLapBurnWindowCount,
                        evidence.PitWindowCount,
                        evidence.TeamStintCount
                    };
                }).ToArray()
            },
            LapDeltaQuality = lapDeltaQuality,
            LapProfileReadiness = lapProfileReadiness,
            LiveOverlayDiagnostics = new
            {
                Path = liveOverlayDiagnosticsPath,
                Exists = File.Exists(liveOverlayDiagnosticsPath),
                FrameCount = (int?)liveOverlayDiagnostics?["totals"]?["frameCount"],
                FlagsFramesWithDisplayFlags = (int?)liveOverlayDiagnostics?["flags"]?["framesWithDisplayFlags"],
                FlagsFramesWithYellowFamilyRawFlags = (int?)liveOverlayDiagnostics?["flags"]?["framesWithYellowFamilyRawFlags"],
                FlagsFramesWithCarIdxYellowFamilyFlags = (int?)liveOverlayDiagnostics?["flags"]?["framesWithCarIdxYellowFamilyFlags"],
                FlagsDisplayTransitionFrames = (int?)liveOverlayDiagnostics?["flags"]?["displayTransitionFrames"],
                FlagsDisplayClearedTransitionFrames = (int?)liveOverlayDiagnostics?["flags"]?["displayClearedTransitionFrames"],
                FlagsLongestDisplayDurationFrames = (int?)liveOverlayDiagnostics?["flags"]?["longestDisplayDurationFrames"],
                FlagsLongestDisplayDurationSeconds = (double?)liveOverlayDiagnostics?["flags"]?["longestDisplayDurationSeconds"],
                FlagsLongestDisplayState = (string?)liveOverlayDiagnostics?["flags"]?["longestDisplayState"],
                FlagsYellowFamilyBitCounts = liveOverlayDiagnostics?["flags"]?["yellowFamilyBitCounts"],
                FlagsYellowFamilyStateCounts = liveOverlayDiagnostics?["flags"]?["yellowFamilyStateCounts"],
                FlagsCarIdxYellowFamilyBitCounts = liveOverlayDiagnostics?["flags"]?["carIdxYellowFamilyBitCounts"],
                FlagsCarIdxYellowFamilyStateCounts = liveOverlayDiagnostics?["flags"]?["carIdxYellowFamilyStateCounts"],
                FlagsRawToDisplayLabelCounts = liveOverlayDiagnostics?["flags"]?["rawToDisplayLabelCounts"],
                FlagsDisplayLabelStateCounts = liveOverlayDiagnostics?["flags"]?["displayLabelStateCounts"],
                FlagsDisplayKindCounts = liveOverlayDiagnostics?["flags"]?["displayKindCounts"],
                FlagsDisplayCategoryCounts = liveOverlayDiagnostics?["flags"]?["displayCategoryCounts"],
                FlagsDisplayLabelCounts = liveOverlayDiagnostics?["flags"]?["displayLabelCounts"],
                FlagsToneCounts = liveOverlayDiagnostics?["flags"]?["toneCounts"],
                RadarSideTransitionFrames = (int?)liveOverlayDiagnostics?["radar"]?["sideTransitionFrames"],
                RadarOppositeSideFlipFrames = (int?)liveOverlayDiagnostics?["radar"]?["oppositeSideFlipFrames"],
                RadarSideTransitionWithoutPlacementFrames = (int?)liveOverlayDiagnostics?["radar"]?["sideTransitionWithoutPlacementFrames"],
                TrackMapFramesWithSectors = (int?)liveOverlayDiagnostics?["trackMap"]?["framesWithSectors"],
                TrackMapFramesWithLiveTiming = (int?)liveOverlayDiagnostics?["trackMap"]?["framesWithLiveTiming"],
                TrackMapHighlightedSectorFrames = (int?)liveOverlayDiagnostics?["trackMap"]?["framesWithHighlightedSectors"],
                FuelFramesWithFuelLevel = (int?)liveOverlayDiagnostics?["fuel"]?["framesWithFuelLevel"],
                FuelTeamContextWithoutFuelLevelFrames = (int?)liveOverlayDiagnostics?["fuel"]?["teamContextWithoutFuelLevelFrames"],
                FuelPitServiceNonPlayerFocusFrames = (int?)liveOverlayDiagnostics?["fuel"]?["pitServiceNonPlayerFocusFrames"],
                FuelLocalStrategyUnavailableFrames = (int?)liveOverlayDiagnostics?["fuel"]?["fuelLocalStrategyUnavailableFrames"],
                FuelLocalStrategyUnavailableReasonCounts = liveOverlayDiagnostics?["fuel"]?["fuelLocalStrategyUnavailableReasonCounts"],
                PitWindowCount = (int?)liveOverlayDiagnostics?["fuel"]?["pitWindowCount"],
                PitWindowsWithFuelIncrease = (int?)liveOverlayDiagnostics?["fuel"]?["pitWindowsWithFuelIncrease"],
                PitWindowsWithBlackFlag = (int?)liveOverlayDiagnostics?["fuel"]?["pitWindowsWithBlackFlag"],
                LapDeltaObservedFrames = lapDeltaQuality.ObservedFrames,
                LapDeltaFramesWithAnyValue = lapDeltaQuality.FramesWithAnyValue,
                LapDeltaFramesWithAnyUsableValue = lapDeltaQuality.FramesWithAnyUsableValue,
                LapDeltaMaxAbsDeltaSeconds = lapDeltaQuality.MaxAbsDeltaSeconds,
                LapDeltaClassification = lapDeltaQuality.Classification,
                LapDeltaValueFrameCounts = lapDeltaQuality.ValueFrameCounts,
                LapDeltaUsableFrameCounts = lapDeltaQuality.UsableFrameCounts,
                LapProfileObservedFrames = lapProfileReadiness.ObservedFrames,
                LapProfileFramesWithTimingRows = lapProfileReadiness.FramesWithTimingRows,
                LapProfileFramesWithScoringRows = lapProfileReadiness.FramesWithScoringRows,
                LapProfileFramesWithBestAndLastLap = lapProfileReadiness.FramesWithBestAndLastLap,
                LapProfileFramesWithRecentPersonalBest = lapProfileReadiness.FramesWithRecentPersonalBest,
                LapProfileFramesWithClassFastestBestLap = lapProfileReadiness.FramesWithClassFastestBestLap,
                LapProfileFramesWithClassFastestLastLap = lapProfileReadiness.FramesWithClassFastestLastLap,
                LapProfileMaxRows = lapProfileReadiness.MaxRows,
                LapProfileMaxRowsWithBestAndLastLap = lapProfileReadiness.MaxRowsWithBestAndLastLap,
                LapProfileClassification = lapProfileReadiness.Classification,
                LapProfileSourceFrameCounts = lapProfileReadiness.SourceFrameCounts,
                LapProfileSourceRowCounts = lapProfileReadiness.SourceRowCounts,
                NonRaceRaceLapSignalFrames = (int?)liveOverlayDiagnostics?["raceProjection"]?["nonRaceRaceLapSignalFrames"],
                NonRaceRaceProjectionFrames = (int?)liveOverlayDiagnostics?["raceProjection"]?["nonRaceRaceProjectionFrames"]
            }
        };
    }

    private static CaptureManifest? TryReadCaptureManifest(string captureDirectory)
    {
        var path = Path.Combine(captureDirectory, "capture-manifest.json");
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<CaptureManifest>(File.ReadAllText(path), JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static JsonObject? TryReadJsonObject(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
        }
        catch
        {
            return null;
        }
    }

    private FuelV2CapturePathInventory FuelV2CapturePaths(string captureDirectory)
    {
        var directory = Path.Combine(captureDirectory, _fuelV2CaptureOptions.CaptureDirectoryName);
        if (!Directory.Exists(directory))
        {
            return FuelV2CapturePathInventory.Empty;
        }

        var paths = Directory
            .EnumerateFiles(directory, $"*{_fuelV2CaptureOptions.OutputFileName}", SearchOption.TopDirectoryOnly)
            .OrderByDescending(path => File.GetLastWriteTimeUtc(path))
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new FuelV2CapturePathInventory(
            paths.Length,
            paths.Take(MaxRecentFuelV2CaptureFiles).ToArray());
    }

    private static FuelV2CaptureEvidenceDiagnostics FuelV2CaptureEvidence(JsonObject? fuelV2Capture)
    {
        if (fuelV2Capture is null)
        {
            return FuelV2CaptureEvidenceDiagnostics.Empty;
        }

        return new FuelV2CaptureEvidenceDiagnostics(
            FormatVersion: (int?)fuelV2Capture["formatVersion"],
            FrameCount: (int?)fuelV2Capture["totals"]?["frameCount"],
            SampledFrameCount: (int?)fuelV2Capture["totals"]?["sampledFrameCount"],
            AcceptedLapBurnWindowCount: (fuelV2Capture["acceptedLapBurnWindows"] as JsonArray)?.Count,
            RejectedLapBurnWindowCount: (fuelV2Capture["rejectedLapBurnWindows"] as JsonArray)?.Count,
            PitWindowCount: (int?)fuelV2Capture["pitService"]?["pitWindowCount"],
            TeamStintCount: (int?)fuelV2Capture["team"]?["teamStintCount"],
            SyntheticReplaySuitable: (bool?)fuelV2Capture["syntheticReplaySuitability"]?["suitable"],
            SyntheticReplayReasons: fuelV2Capture["syntheticReplaySuitability"]?["reasons"] as JsonArray,
            SessionFrameCounts: fuelV2Capture["totals"]?["sessionFrameCounts"],
            ContextFlagCounts: fuelV2Capture["totals"]?["contextFlagCounts"],
            LapBudgetSourceCounts: fuelV2Capture["lapBudget"]?["sourceCounts"],
            LapBudgetMissingSignalCounts: fuelV2Capture["lapBudget"]?["missingSignalCounts"]);
    }

    private UpdateApplyShutdownDiagnosticsSnapshot UpdateApplyShutdownDiagnostics(ReleaseUpdateSnapshot releaseSnapshot)
    {
        var events = ReadRecentAppEvents();
        var runtimeState = TryReadJsonObject(_storageOptions.RuntimeStatePath);
        var runtimeExists = runtimeState is not null;
        var runtimeStartedAtUtc = ParseDateTimeOffset(runtimeState?["startedAtUtc"]);
        var runtimeStoppedCleanly = (bool?)runtimeState?["stoppedCleanly"];
        var runtimeStoppedAtUtc = ParseDateTimeOffset(runtimeState?["stoppedAtUtc"]);
        var runtimeShutdownStartedAtUtc = ParseDateTimeOffset(runtimeState?["shutdownStartedAtUtc"]);
        var runtimeShutdownCompletedAtUtc = ParseDateTimeOffset(runtimeState?["shutdownCompletedAtUtc"]);
        var runtimeShutdownPhase = (string?)runtimeState?["shutdownPhase"];
        var currentRunEvents = EventsAtOrAfter(events, runtimeStartedAtUtc);
        var updateApplyStarted = EventsNamed(currentRunEvents, "update_apply_started");
        var updateApplyHandoffRequested = EventsNamed(currentRunEvents, "update_apply_handoff_requested");
        var updateApplyHandoffReturned = EventsNamed(currentRunEvents, "update_apply_handoff_returned");
        var updateApplyFailed = EventsNamed(currentRunEvents, "update_apply_failed");
        var applicationExitRequested = EventsNamed(currentRunEvents, "application_exit_requested_for_update");
        var hostStopStarted = EventsNamed(currentRunEvents, "host_stop_started");
        var hostStopCompleted = EventsNamed(currentRunEvents, "host_stop_completed");
        var appStopped = EventsNamed(currentRunEvents, "app_stopped");
        var latestApplyStarted = LatestAppEvent(updateApplyStarted);
        var latestHandoffRequested = LatestAppEvent(updateApplyHandoffRequested);
        var latestHandoffReturned = LatestAppEvent(updateApplyHandoffReturned);
        var latestApplyFailed = LatestAppEvent(updateApplyFailed);
        var latestHostStopCompleted = LatestAppEvent(hostStopCompleted);
        var latestAppStopped = LatestAppEvent(appStopped);
        var releaseApplyStartedInCurrentRuntime = TimestampAtOrAfter(
            releaseSnapshot.LastApplyStartedAtUtc,
            runtimeStartedAtUtc);
        var releaseIndicatesApplying = releaseSnapshot.Status == ReleaseUpdateStatus.Applying
            ? releaseSnapshot.LastApplyStartedAtUtc is null || releaseApplyStartedInCurrentRuntime
            : releaseSnapshot.OperationInProgress && releaseApplyStartedInCurrentRuntime;
        var applySignalPresent = updateApplyStarted.Count > 0 || releaseIndicatesApplying;
        var applyStartedAtUtc = latestApplyStarted?.TimestampUtc
            ?? (releaseApplyStartedInCurrentRuntime ? releaseSnapshot.LastApplyStartedAtUtc : null);
        var applyFailedAfterStart = EventAtOrAfter(latestApplyFailed, applyStartedAtUtc);
        var appStoppedAfterStart = EventAtOrAfter(latestAppStopped, applyStartedAtUtc);
        var hostStopCompletedAfterStart = EventAtOrAfter(latestHostStopCompleted, applyStartedAtUtc);
        var shutdownIncomplete = applySignalPresent
            && runtimeStoppedCleanly == false
            && !appStoppedAfterStart
            && !hostStopCompletedAfterStart
            && !applyFailedAfterStart;
        var classification = !applySignalPresent
            ? "no_update_apply_signal"
            : applyFailedAfterStart
                ? "update_apply_failed"
                : shutdownIncomplete
                    ? "update_apply_shutdown_incomplete"
                    : appStoppedAfterStart || runtimeStoppedCleanly == true || hostStopCompletedAfterStart
                        ? "update_apply_shutdown_completed"
                        : "update_apply_shutdown_unproven";

        return new UpdateApplyShutdownDiagnosticsSnapshot(
            Classification: classification,
            Interpretation: classification switch
            {
                "update_apply_shutdown_incomplete" => "Update apply started, but the bundle shows no clean app stop and runtime-state is still dirty. Treat this as update apply or shutdown handoff limbo, not as a telemetry freeze.",
                "update_apply_failed" => "Update apply reported a failure after apply start.",
                "update_apply_shutdown_completed" => "Update apply started and shutdown completion evidence was found.",
                "update_apply_shutdown_unproven" => "Update apply evidence exists, but the bundle cannot prove whether shutdown completed.",
                _ => "No update-apply shutdown signal was found in recent events."
            },
            ReleaseStatus: releaseSnapshot.Status.ToString(),
            ReleaseOperationInProgress: releaseSnapshot.OperationInProgress,
            ReleaseLastApplyStartedAtUtc: releaseSnapshot.LastApplyStartedAtUtc,
            RuntimeStateExists: runtimeExists,
            RuntimeStartedAtUtc: runtimeStartedAtUtc,
            RuntimeStoppedCleanly: runtimeStoppedCleanly,
            RuntimeStoppedAtUtc: runtimeStoppedAtUtc,
            RuntimeShutdownStartedAtUtc: runtimeShutdownStartedAtUtc,
            RuntimeShutdownCompletedAtUtc: runtimeShutdownCompletedAtUtc,
            RuntimeShutdownPhase: runtimeShutdownPhase,
            UpdateApplyStartedCount: updateApplyStarted.Count,
            UpdateApplyHandoffRequestedCount: updateApplyHandoffRequested.Count,
            UpdateApplyHandoffReturnedCount: updateApplyHandoffReturned.Count,
            UpdateApplyFailedCount: updateApplyFailed.Count,
            ApplicationExitRequestedForUpdateCount: applicationExitRequested.Count,
            HostStopStartedCount: hostStopStarted.Count,
            HostStopCompletedCount: hostStopCompleted.Count,
            AppStoppedCount: appStopped.Count,
            HostStopCompletedAfterApplyStart: hostStopCompletedAfterStart,
            AppStoppedAfterApplyStart: appStoppedAfterStart,
            LatestUpdateApplyStartedAtUtc: latestApplyStarted?.TimestampUtc,
            LatestUpdateApplyHandoffRequestedAtUtc: latestHandoffRequested?.TimestampUtc,
            LatestUpdateApplyHandoffReturnedAtUtc: latestHandoffReturned?.TimestampUtc,
            LatestApplicationExitRequestedForUpdateAtUtc: LatestAppEvent(applicationExitRequested)?.TimestampUtc,
            LatestHostStopStartedAtUtc: LatestAppEvent(hostStopStarted)?.TimestampUtc,
            LatestHostStopCompletedAtUtc: latestHostStopCompleted?.TimestampUtc,
            LatestAppStoppedAtUtc: latestAppStopped?.TimestampUtc);
    }

    private static IReadOnlyList<AppEventDiagnostics> EventsAtOrAfter(
        IReadOnlyList<AppEventDiagnostics> events,
        DateTimeOffset? timestampUtc)
    {
        if (timestampUtc is not { } timestamp)
        {
            return events;
        }

        return events
            .Where(appEvent => appEvent.TimestampUtc is { } eventTimestamp && eventTimestamp >= timestamp)
            .ToArray();
    }

    private static bool EventAtOrAfter(AppEventDiagnostics? appEvent, DateTimeOffset? timestampUtc)
    {
        return timestampUtc is { } timestamp
            && appEvent?.TimestampUtc is { } eventTimestamp
            && eventTimestamp >= timestamp;
    }

    private static bool TimestampAtOrAfter(DateTimeOffset? timestampUtc, DateTimeOffset? thresholdUtc)
    {
        return timestampUtc is { } timestamp
            && (thresholdUtc is not { } threshold || timestamp >= threshold);
    }

    private IReadOnlyList<AppEventDiagnostics> ReadRecentAppEvents()
    {
        if (!Directory.Exists(_storageOptions.EventsRoot))
        {
            return [];
        }

        var events = new List<AppEventDiagnostics>();
        foreach (var file in Directory
                     .EnumerateFiles(_storageOptions.EventsRoot, "*.jsonl")
                     .Select(path => new FileInfo(path))
                     .OrderByDescending(file => file.LastWriteTimeUtc)
                     .Take(MaxRecentEventFilesForDiagnostics))
        {
            try
            {
                foreach (var line in File.ReadLines(file.FullName))
                {
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    if (TryReadAppEvent(line, out var timestampUtc, out var name, out var properties)
                        && !string.IsNullOrWhiteSpace(name))
                    {
                        events.Add(new AppEventDiagnostics(file.Name, timestampUtc, name, properties));
                    }
                }
            }
            catch
            {
                // Event diagnostics are best-effort; malformed files are counted by UpdateEventDiagnostics.
            }
        }

        return events;
    }

    private static IReadOnlyList<AppEventDiagnostics> EventsNamed(
        IReadOnlyList<AppEventDiagnostics> events,
        string name)
    {
        return events
            .Where(appEvent => string.Equals(appEvent.Name, name, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    private static AppEventDiagnostics? LatestAppEvent(IReadOnlyList<AppEventDiagnostics> events)
    {
        return events
            .OrderByDescending(item => item.TimestampUtc ?? DateTimeOffset.MinValue)
            .FirstOrDefault();
    }

    private static DateTimeOffset? ParseDateTimeOffset(JsonNode? node)
    {
        return DateTimeOffset.TryParse(
            (string?)node,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed)
                ? parsed
                : null;
    }

    private UpdateEventDiagnosticsSnapshot UpdateEventDiagnostics()
    {
        if (!Directory.Exists(_storageOptions.EventsRoot))
        {
            return new UpdateEventDiagnosticsSnapshot(
                EventsRoot: _storageOptions.EventsRoot,
                Exists: false,
                RecentEventFilesScanned: 0,
                MalformedEventLineCount: 0,
                UpdateCheckStartedCount: 0,
                UpdateCheckSucceededCount: 0,
                UpdateCheckFailedCount: 0,
                LatestUpdateCheckFailureAtUtc: null,
                LatestUpdateCheckFailureSource: null,
                LatestUpdateCheckFailureError: null,
                LatestUpdateCheckSuccessAtUtc: null,
                LatestUpdateCheckSuccessSource: null,
                LatestUpdateCheckSuccessResult: null,
                UpdateCheckFailureSourceCounts: EmptyStringIntDictionary(),
                UpdateCheckFailureErrorCounts: EmptyStringIntDictionary(),
                UpdateFailureSummary: BuildUpdateFailureSummary([], []),
                Files: []);
        }

        var files = Directory
            .EnumerateFiles(_storageOptions.EventsRoot, "*.jsonl")
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .Take(MaxRecentEventFilesForDiagnostics)
            .ToArray();
        var started = new List<UpdateCheckEvent>();
        var succeeded = new List<UpdateCheckEvent>();
        var failed = new List<UpdateCheckEvent>();
        var fileDiagnostics = new List<UpdateEventFileDiagnostics>();
        var malformed = 0;

        foreach (var file in files)
        {
            var fileStarted = 0;
            var fileSucceeded = 0;
            var fileFailed = 0;
            var fileMalformed = 0;
            try
            {
                foreach (var line in File.ReadLines(file.FullName))
                {
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    if (!TryReadAppEvent(line, out var timestampUtc, out var name, out var properties))
                    {
                        malformed++;
                        fileMalformed++;
                        continue;
                    }

                    if (string.Equals(name, "update_check_started", StringComparison.OrdinalIgnoreCase))
                    {
                        started.Add(UpdateCheckEvent.From(file.Name, timestampUtc, properties));
                        fileStarted++;
                    }
                    else if (string.Equals(name, "update_check_succeeded", StringComparison.OrdinalIgnoreCase))
                    {
                        succeeded.Add(UpdateCheckEvent.From(file.Name, timestampUtc, properties));
                        fileSucceeded++;
                    }
                    else if (string.Equals(name, "update_check_failed", StringComparison.OrdinalIgnoreCase))
                    {
                        failed.Add(UpdateCheckEvent.From(file.Name, timestampUtc, properties));
                        fileFailed++;
                    }
                }
            }
            catch
            {
                malformed++;
                fileMalformed++;
            }

            fileDiagnostics.Add(new UpdateEventFileDiagnostics(
                FileName: file.Name,
                UpdateCheckStartedCount: fileStarted,
                UpdateCheckSucceededCount: fileSucceeded,
                UpdateCheckFailedCount: fileFailed,
                MalformedEventLineCount: fileMalformed));
        }

        var latestFailure = LatestEvent(failed);
        var latestSuccess = LatestEvent(succeeded);
        return new UpdateEventDiagnosticsSnapshot(
            EventsRoot: _storageOptions.EventsRoot,
            Exists: true,
            RecentEventFilesScanned: files.Length,
            MalformedEventLineCount: malformed,
            UpdateCheckStartedCount: started.Count,
            UpdateCheckSucceededCount: succeeded.Count,
            UpdateCheckFailedCount: failed.Count,
            LatestUpdateCheckFailureAtUtc: latestFailure?.TimestampUtc,
            LatestUpdateCheckFailureSource: latestFailure?.Source,
            LatestUpdateCheckFailureError: latestFailure?.Error,
            LatestUpdateCheckSuccessAtUtc: latestSuccess?.TimestampUtc,
            LatestUpdateCheckSuccessSource: latestSuccess?.Source,
            LatestUpdateCheckSuccessResult: latestSuccess?.Result,
            UpdateCheckFailureSourceCounts: CountBy(failed.Select(item => item.Source), "unknown"),
            UpdateCheckFailureErrorCounts: CountBy(failed.Select(item => item.Error), "unknown"),
            UpdateFailureSummary: BuildUpdateFailureSummary(failed, succeeded),
            Files: fileDiagnostics
                .OrderBy(file => file.FileName, StringComparer.OrdinalIgnoreCase)
                .ToArray());
    }

    private static UpdateFailureSummaryDiagnostics BuildUpdateFailureSummary(
        IReadOnlyList<UpdateCheckEvent> failed,
        IReadOnlyList<UpdateCheckEvent> succeeded)
    {
        var recoveriesByFailure = failed
            .Select(failure => new
            {
                Failure = failure,
                Recovery = FirstSuccessAfterFailure(failure, succeeded)
            })
            .ToArray();
        var recoveredFailures = recoveriesByFailure
            .Where(item => item.Recovery is not null)
            .ToArray();
        var unrecoveredFailureCount = failed.Count - recoveredFailures.Length;
        var latestFailure = LatestEvent(failed);
        var latestTransientFailure = recoveredFailures
            .OrderByDescending(item => item.Failure.TimestampUtc ?? DateTimeOffset.MinValue)
            .FirstOrDefault();
        var latestRecovery = latestTransientFailure?.Recovery;
        var classification = failed.Count == 0
            ? "no_update_check_failures"
            : recoveredFailures.Length > 0
                ? unrecoveredFailureCount > 0
                    ? "mixed_transient_and_unrecovered_update_check_failures"
                    : "transient_update_check_failures_recovered"
                : "unrecovered_update_check_failures";

        return new UpdateFailureSummaryDiagnostics(
            Classification: classification,
            FailureCount: failed.Count,
            SuccessCount: succeeded.Count,
            TransientFailureCount: recoveredFailures.Length,
            UnrecoveredFailureCount: unrecoveredFailureCount,
            LatestFailureAtUtc: latestFailure?.TimestampUtc,
            LatestFailureSource: latestFailure?.Source,
            LatestFailureError: latestFailure?.Error,
            LatestTransientFailureAtUtc: latestTransientFailure?.Failure.TimestampUtc,
            LatestTransientFailureSource: latestTransientFailure?.Failure.Source,
            LatestTransientFailureError: latestTransientFailure?.Failure.Error,
            LatestRecoveryAtUtc: latestRecovery?.TimestampUtc,
            LatestRecoverySource: latestRecovery?.Source,
            LatestRecoveryResult: latestRecovery?.Result,
            Interpretation: classification switch
            {
                "transient_update_check_failures_recovered" => "Update-check failures were observed and later recovered in events/*.jsonl; treat the updater as transiently degraded rather than permanently failed.",
                "mixed_transient_and_unrecovered_update_check_failures" => "Some update-check failures recovered, but at least one recent failure has no later successful check in the scanned event logs.",
                "unrecovered_update_check_failures" => "Recent update-check failures were observed with no later successful update check in the scanned event logs.",
                _ => "No recent update-check failures were found in bundled event logs."
            });
    }

    private static UpdateCheckEvent? FirstSuccessAfterFailure(
        UpdateCheckEvent failure,
        IReadOnlyList<UpdateCheckEvent> succeeded)
    {
        if (failure.TimestampUtc is not { } failedAtUtc)
        {
            return null;
        }

        return succeeded
            .Where(success => success.TimestampUtc is { } succeededAtUtc && succeededAtUtc >= failedAtUtc)
            .OrderBy(success => success.TimestampUtc)
            .FirstOrDefault();
    }

    private static bool TryReadAppEvent(
        string line,
        out DateTimeOffset? timestampUtc,
        out string? name,
        out JsonObject? properties)
    {
        timestampUtc = null;
        name = null;
        properties = null;

        try
        {
            var node = JsonNode.Parse(line) as JsonObject;
            if (node is null)
            {
                return false;
            }

            if (DateTimeOffset.TryParse(
                    (string?)node["timestampUtc"],
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var parsedTimestamp))
            {
                timestampUtc = parsedTimestamp;
            }

            name = (string?)node["name"];
            properties = node["properties"] as JsonObject;
            return !string.IsNullOrWhiteSpace(name);
        }
        catch
        {
            return false;
        }
    }

    private static UpdateCheckEvent? LatestEvent(IReadOnlyList<UpdateCheckEvent> events)
    {
        return events
            .OrderByDescending(item => item.TimestampUtc ?? DateTimeOffset.MinValue)
            .FirstOrDefault();
    }

    private static IReadOnlyDictionary<string, int> CountBy(IEnumerable<string?> values, string fallback)
    {
        return values
            .Select(value => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim())
            .GroupBy(value => value, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyDictionary<string, int> EmptyStringIntDictionary()
    {
        return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    }

    private static object SetupAdjustmentEvidence(IReadOnlyList<SessionInfoSetupSignal> setupSignals)
    {
        var wingOrArbSignals = setupSignals
            .Where(signal =>
                signal.Key.Contains("Arb", StringComparison.OrdinalIgnoreCase)
                || signal.Key.Contains("AntiRoll", StringComparison.OrdinalIgnoreCase)
                || signal.Key.Contains("Wing", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        return new
        {
            StaticSessionInfoSignalCount = setupSignals.Count,
            StaticWingOrArbSignalCount = wingOrArbSignals.Length,
            HasStaticWingOrArbSignals = wingOrArbSignals.Length > 0,
            LiveAdjustmentChangeEvidenceAvailable = false,
            EvidenceSource = "latest-session.yaml setup snapshot",
            Limitation = "Session-info setup values prove the static setup snapshot only. They do not prove an in-session wing/ARB adjustment unless a later capture adds session-info diffs, raw telemetry variable changes, or another live adjustment signal."
        };
    }

    private static object PostRaceFuelEvidence(JsonObject? synthesis, JsonObject? liveOverlayDiagnostics)
    {
        var validDistanceLaps = (double?)synthesis?["session"]?["metrics"]?["validDistanceLaps"];
        var completedValidLaps = (int?)synthesis?["session"]?["metrics"]?["completedValidLaps"];
        var fuel = liveOverlayDiagnostics?["fuel"] as JsonObject;
        var framesWithFuelLevel = (int?)fuel?["framesWithFuelLevel"];
        var teamContextWithoutFuelLevelFrames = (int?)fuel?["teamContextWithoutFuelLevelFrames"];
        var pitServiceNonPlayerFocusFrames = (int?)fuel?["pitServiceNonPlayerFocusFrames"];
        var fuelLocalStrategyUnavailableFrames = (int?)fuel?["fuelLocalStrategyUnavailableFrames"];
        var reasonCounts = JsonObjectToIntDictionary(fuel?["fuelLocalStrategyUnavailableReasonCounts"]);
        var hasSynthesisMetrics = validDistanceLaps is not null || completedValidLaps is not null;
        var hasFuelDiagnostics = fuel is not null;
        var hasNoCompletedLocalDistance = hasSynthesisMetrics
            && (completedValidLaps.GetValueOrDefault() <= 0 || validDistanceLaps.GetValueOrDefault() <= 0d);
        var hasLocalFuelEvidenceGap = teamContextWithoutFuelLevelFrames.GetValueOrDefault() > 0
            || fuelLocalStrategyUnavailableFrames.GetValueOrDefault() > 0
            || pitServiceNonPlayerFocusFrames.GetValueOrDefault() > 0
            || reasonCounts.Count > 0;
        var classification = ClassifyPostRaceFuelEvidence(
            hasSynthesisMetrics,
            hasFuelDiagnostics,
            hasNoCompletedLocalDistance,
            hasLocalFuelEvidenceGap);

        return new
        {
            EvidenceAvailable = hasSynthesisMetrics || hasFuelDiagnostics,
            SynthesisMetricsAvailable = hasSynthesisMetrics,
            LiveFuelDiagnosticsAvailable = hasFuelDiagnostics,
            ValidDistanceLaps = validDistanceLaps,
            CompletedValidLaps = completedValidLaps,
            FramesWithFuelLevel = framesWithFuelLevel,
            TeamContextWithoutFuelLevelFrames = teamContextWithoutFuelLevelFrames,
            PitServiceNonPlayerFocusFrames = pitServiceNonPlayerFocusFrames,
            FuelLocalStrategyUnavailableFrames = fuelLocalStrategyUnavailableFrames,
            FuelLocalStrategyUnavailableReasonCounts = reasonCounts,
            MissingLocalPlayerFuelEvidence = string.Equals(classification, "missing_local_player_fuel_evidence", StringComparison.Ordinal),
            Classification = classification,
            Interpretation = PostRaceFuelEvidenceInterpretation(classification)
        };
    }

    private static string ClassifyPostRaceFuelEvidence(
        bool hasSynthesisMetrics,
        bool hasFuelDiagnostics,
        bool hasNoCompletedLocalDistance,
        bool hasLocalFuelEvidenceGap)
    {
        if (!hasSynthesisMetrics && !hasFuelDiagnostics)
        {
            return "not_recorded";
        }

        if (hasNoCompletedLocalDistance && hasLocalFuelEvidenceGap)
        {
            return "missing_local_player_fuel_evidence";
        }

        if (hasLocalFuelEvidenceGap)
        {
            return "local_player_fuel_evidence_degraded";
        }

        if (hasNoCompletedLocalDistance)
        {
            return "missing_valid_lap_distance";
        }

        return "not_classified_missing_local_fuel";
    }

    private static string PostRaceFuelEvidenceInterpretation(string classification)
    {
        return classification switch
        {
            "missing_local_player_fuel_evidence" => "The capture contains post-race/fuel failure evidence tied to local-player or team fuel availability, not just generic zero valid laps.",
            "local_player_fuel_evidence_degraded" => "Fuel diagnostics show local-player/team fuel evidence gaps even though the summary has some valid lap-distance evidence.",
            "missing_valid_lap_distance" => "Post-race synthesis lacks valid completed local distance, but bundled fuel diagnostics do not prove a local-player fuel evidence gap.",
            "not_recorded" => "No post-race synthesis metrics or live fuel diagnostics were available in the bundle.",
            _ => "The bundled metadata does not prove a local-player fuel evidence gap."
        };
    }

    private static LapDeltaQualityDiagnostics LapDeltaQualityFromDiagnostics(JsonObject? lapDelta)
    {
        if (lapDelta is null)
        {
            return new LapDeltaQualityDiagnostics(
                EvidenceAvailable: false,
                ObservedFrames: null,
                FramesWithAnyValue: null,
                FramesWithAnyUsableValue: null,
                MaxAbsDeltaSeconds: null,
                ValueFrameCounts: EmptyStringIntDictionary(),
                UsableFrameCounts: EmptyStringIntDictionary(),
                Classification: "not_recorded",
                ValuesPresentWithoutUsableQuality: false,
                AllObservedValuesZero: false,
                Interpretation: "No lap-delta diagnostics block was found in the latest capture overlay diagnostics.");
        }

        var observedFrames = (int?)lapDelta["observedFrames"];
        var framesWithAnyValue = (int?)lapDelta["framesWithAnyValue"];
        var framesWithAnyUsableValue = (int?)lapDelta["framesWithAnyUsableValue"];
        var maxAbsDeltaSeconds = (double?)lapDelta["maxAbsDeltaSeconds"];
        var valueFrameCounts = JsonObjectToIntDictionary(lapDelta["valueFrameCounts"]);
        var usableFrameCounts = JsonObjectToIntDictionary(lapDelta["usableFrameCounts"]);
        return BuildLapDeltaQuality(
            evidenceAvailable: true,
            observedFrames,
            framesWithAnyValue,
            framesWithAnyUsableValue,
            maxAbsDeltaSeconds,
            valueFrameCounts,
            usableFrameCounts);
    }

    private static LapDeltaQualityDiagnostics LapDeltaQualityFromSample(HistoricalTelemetrySample? sample)
    {
        if (sample is null)
        {
            return new LapDeltaQualityDiagnostics(
                EvidenceAvailable: false,
                ObservedFrames: null,
                FramesWithAnyValue: null,
                FramesWithAnyUsableValue: null,
                MaxAbsDeltaSeconds: null,
                ValueFrameCounts: EmptyStringIntDictionary(),
                UsableFrameCounts: EmptyStringIntDictionary(),
                Classification: "not_recorded",
                ValuesPresentWithoutUsableQuality: false,
                AllObservedValuesZero: false,
                Interpretation: "No latest telemetry sample is available for lap-delta quality diagnostics.");
        }

        var valueCounts = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var usableCounts = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        double? maxAbsDeltaSeconds = null;
        var anyValue = false;
        var anyUsable = false;
        foreach (var signal in LapDeltaSignals(sample))
        {
            if (IsFinite(signal.Seconds))
            {
                anyValue = true;
                valueCounts[signal.Key] = 1;
                maxAbsDeltaSeconds = Max(maxAbsDeltaSeconds, Math.Abs(signal.Seconds!.Value));
            }

            if (signal.IsUsable)
            {
                anyUsable = true;
                usableCounts[signal.Key] = 1;
            }
        }

        return BuildLapDeltaQuality(
            evidenceAvailable: true,
            observedFrames: 1,
            framesWithAnyValue: anyValue ? 1 : 0,
            framesWithAnyUsableValue: anyUsable ? 1 : 0,
            maxAbsDeltaSeconds,
            valueCounts,
            usableCounts);
    }

    private static LapDeltaQualityDiagnostics BuildLapDeltaQuality(
        bool evidenceAvailable,
        int? observedFrames,
        int? framesWithAnyValue,
        int? framesWithAnyUsableValue,
        double? maxAbsDeltaSeconds,
        IReadOnlyDictionary<string, int> valueFrameCounts,
        IReadOnlyDictionary<string, int> usableFrameCounts)
    {
        var valuesPresentWithoutUsableQuality = framesWithAnyValue.GetValueOrDefault() > 0
            && framesWithAnyUsableValue.GetValueOrDefault() <= 0;
        var allObservedValuesZero = valuesPresentWithoutUsableQuality
            && (maxAbsDeltaSeconds is null || Math.Abs(maxAbsDeltaSeconds.Value) <= double.Epsilon);
        var classification = ClassifyLapDeltaQuality(
            evidenceAvailable,
            observedFrames,
            framesWithAnyValue,
            framesWithAnyUsableValue,
            maxAbsDeltaSeconds);

        return new LapDeltaQualityDiagnostics(
            EvidenceAvailable: evidenceAvailable,
            ObservedFrames: observedFrames,
            FramesWithAnyValue: framesWithAnyValue,
            FramesWithAnyUsableValue: framesWithAnyUsableValue,
            MaxAbsDeltaSeconds: maxAbsDeltaSeconds,
            ValueFrameCounts: valueFrameCounts,
            UsableFrameCounts: usableFrameCounts,
            Classification: classification,
            ValuesPresentWithoutUsableQuality: valuesPresentWithoutUsableQuality,
            AllObservedValuesZero: allObservedValuesZero,
            Interpretation: valuesPresentWithoutUsableQuality
                ? "Lap-delta values are present but no quality/OK signal marks them usable; overlays and analysis should treat them as unavailable instead of displaying zero deltas."
                : "Lap-delta quality is either usable or no lap-delta values were observed.");
    }

    private static string ClassifyLapDeltaQuality(
        bool evidenceAvailable,
        int? observedFrames,
        int? framesWithAnyValue,
        int? framesWithAnyUsableValue,
        double? maxAbsDeltaSeconds)
    {
        if (!evidenceAvailable)
        {
            return "not_recorded";
        }

        if (observedFrames.GetValueOrDefault() <= 0)
        {
            return "no_observed_frames";
        }

        if (framesWithAnyValue.GetValueOrDefault() <= 0)
        {
            return "no_values";
        }

        if (framesWithAnyUsableValue.GetValueOrDefault() > 0)
        {
            return "usable";
        }

        return maxAbsDeltaSeconds is null || Math.Abs(maxAbsDeltaSeconds.Value) <= double.Epsilon
            ? "values_present_without_usable_quality_all_zero"
            : "values_present_without_usable_quality";
    }

    private static IReadOnlyDictionary<string, int> JsonObjectToIntDictionary(JsonNode? node)
    {
        if (node is not JsonObject jsonObject)
        {
            return EmptyStringIntDictionary();
        }

        var values = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in jsonObject)
        {
            if ((int?)item.Value is { } count)
            {
                values[item.Key] = count;
            }
        }

        return values;
    }

    private static IEnumerable<LapDeltaSignalDiagnostic> LapDeltaSignals(HistoricalTelemetrySample sample)
    {
        yield return new LapDeltaSignalDiagnostic(
            "toBestLap",
            sample.LapDeltaToBestLapSeconds,
            sample.LapDeltaToBestLapRate,
            sample.LapDeltaToBestLapOk);
        yield return new LapDeltaSignalDiagnostic(
            "toOptimalLap",
            sample.LapDeltaToOptimalLapSeconds,
            sample.LapDeltaToOptimalLapRate,
            sample.LapDeltaToOptimalLapOk);
        yield return new LapDeltaSignalDiagnostic(
            "toSessionBestLap",
            sample.LapDeltaToSessionBestLapSeconds,
            sample.LapDeltaToSessionBestLapRate,
            sample.LapDeltaToSessionBestLapOk);
        yield return new LapDeltaSignalDiagnostic(
            "toSessionOptimalLap",
            sample.LapDeltaToSessionOptimalLapSeconds,
            sample.LapDeltaToSessionOptimalLapRate,
            sample.LapDeltaToSessionOptimalLapOk);
        yield return new LapDeltaSignalDiagnostic(
            "toSessionLastLap",
            sample.LapDeltaToSessionLastLapSeconds,
            sample.LapDeltaToSessionLastLapRate,
            sample.LapDeltaToSessionLastLapOk);
    }

    private static LapProfileReadinessDiagnostics LapProfileReadinessFromDiagnostics(JsonObject? lapProfile)
    {
        if (lapProfile is null)
        {
            return BuildLapProfileReadiness(
                evidenceAvailable: false,
                observedFrames: null,
                framesWithTimingRows: null,
                framesWithScoringRows: null,
                framesWithAnyRows: null,
                framesWithAnyBestLap: null,
                framesWithAnyLastLap: null,
                framesWithBestAndLastLap: null,
                framesWithRecentPersonalBest: null,
                framesWithClassFastestBestLap: null,
                framesWithClassFastestLastLap: null,
                maxRows: null,
                maxRowsWithBestAndLastLap: null,
                maxRowsWithRecentPersonalBest: null,
                maxRowsWithClassFastestBestLap: null,
                maxRowsWithClassFastestLastLap: null,
                sourceFrameCounts: EmptyStringIntDictionary(),
                sourceRowCounts: EmptyStringIntDictionary());
        }

        return BuildLapProfileReadiness(
            evidenceAvailable: true,
            observedFrames: (int?)lapProfile["observedFrames"],
            framesWithTimingRows: (int?)lapProfile["framesWithTimingRows"],
            framesWithScoringRows: (int?)lapProfile["framesWithScoringRows"],
            framesWithAnyRows: (int?)lapProfile["framesWithAnyRows"],
            framesWithAnyBestLap: (int?)lapProfile["framesWithAnyBestLap"],
            framesWithAnyLastLap: (int?)lapProfile["framesWithAnyLastLap"],
            framesWithBestAndLastLap: (int?)lapProfile["framesWithBestAndLastLap"],
            framesWithRecentPersonalBest: (int?)lapProfile["framesWithRecentPersonalBest"],
            framesWithClassFastestBestLap: (int?)lapProfile["framesWithClassFastestBestLap"],
            framesWithClassFastestLastLap: (int?)lapProfile["framesWithClassFastestLastLap"],
            maxRows: (int?)lapProfile["maxRows"],
            maxRowsWithBestAndLastLap: (int?)lapProfile["maxRowsWithBestAndLastLap"],
            maxRowsWithRecentPersonalBest: (int?)lapProfile["maxRowsWithRecentPersonalBest"],
            maxRowsWithClassFastestBestLap: (int?)lapProfile["maxRowsWithClassFastestBestLap"],
            maxRowsWithClassFastestLastLap: (int?)lapProfile["maxRowsWithClassFastestLastLap"],
            sourceFrameCounts: JsonObjectToIntDictionary(lapProfile["sourceFrameCounts"]),
            sourceRowCounts: JsonObjectToIntDictionary(lapProfile["sourceRowCounts"]));
    }

    private static LapProfileReadinessDiagnostics LapProfileReadinessFromSnapshot(LiveTelemetrySnapshot snapshot)
    {
        var timingRows = LapProfileTimingRows(snapshot.Models.Timing).ToArray();
        var scoringRows = LapProfileScoringRows(snapshot.Models.Scoring).ToArray();
        var mergedRows = MergeLapProfileRows(timingRows, scoringRows).ToArray();
        var readiness = LapProfileReadiness(mergedRows);
        var sourceFrameCounts = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var sourceRowCounts = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        RecordLapProfileSourceCounts(sourceFrameCounts, sourceRowCounts, "timing", timingRows);
        RecordLapProfileSourceCounts(sourceFrameCounts, sourceRowCounts, "scoring", scoringRows);
        RecordLapProfileSourceCounts(sourceFrameCounts, sourceRowCounts, "merged", mergedRows);

        return BuildLapProfileReadiness(
            evidenceAvailable: true,
            observedFrames: 1,
            framesWithTimingRows: timingRows.Length > 0 ? 1 : 0,
            framesWithScoringRows: scoringRows.Length > 0 ? 1 : 0,
            framesWithAnyRows: mergedRows.Length > 0 ? 1 : 0,
            framesWithAnyBestLap: readiness.RowsWithBestLap > 0 ? 1 : 0,
            framesWithAnyLastLap: readiness.RowsWithLastLap > 0 ? 1 : 0,
            framesWithBestAndLastLap: readiness.RowsWithBestAndLastLap > 0 ? 1 : 0,
            framesWithRecentPersonalBest: readiness.RowsWithRecentPersonalBest > 0 ? 1 : 0,
            framesWithClassFastestBestLap: readiness.RowsWithClassFastestBestLap > 0 ? 1 : 0,
            framesWithClassFastestLastLap: readiness.RowsWithClassFastestLastLap > 0 ? 1 : 0,
            maxRows: mergedRows.Length,
            maxRowsWithBestAndLastLap: readiness.RowsWithBestAndLastLap,
            maxRowsWithRecentPersonalBest: readiness.RowsWithRecentPersonalBest,
            maxRowsWithClassFastestBestLap: readiness.RowsWithClassFastestBestLap,
            maxRowsWithClassFastestLastLap: readiness.RowsWithClassFastestLastLap,
            sourceFrameCounts,
            sourceRowCounts);
    }

    private static LapProfileReadinessDiagnostics BuildLapProfileReadiness(
        bool evidenceAvailable,
        int? observedFrames,
        int? framesWithTimingRows,
        int? framesWithScoringRows,
        int? framesWithAnyRows,
        int? framesWithAnyBestLap,
        int? framesWithAnyLastLap,
        int? framesWithBestAndLastLap,
        int? framesWithRecentPersonalBest,
        int? framesWithClassFastestBestLap,
        int? framesWithClassFastestLastLap,
        int? maxRows,
        int? maxRowsWithBestAndLastLap,
        int? maxRowsWithRecentPersonalBest,
        int? maxRowsWithClassFastestBestLap,
        int? maxRowsWithClassFastestLastLap,
        IReadOnlyDictionary<string, int> sourceFrameCounts,
        IReadOnlyDictionary<string, int> sourceRowCounts)
    {
        var classification = ClassifyLapProfileReadiness(
            evidenceAvailable,
            observedFrames,
            framesWithAnyRows,
            framesWithAnyBestLap,
            framesWithAnyLastLap,
            framesWithBestAndLastLap,
            framesWithRecentPersonalBest,
            framesWithClassFastestBestLap,
            framesWithClassFastestLastLap);
        var bestVsLastReady = framesWithBestAndLastLap.GetValueOrDefault() > 0;
        var recentPersonalBestEvidenceAvailable = framesWithRecentPersonalBest.GetValueOrDefault() > 0;
        var classFastestEvidenceAvailable = framesWithClassFastestBestLap.GetValueOrDefault() > 0
            || framesWithClassFastestLastLap.GetValueOrDefault() > 0;

        return new LapProfileReadinessDiagnostics(
            EvidenceAvailable: evidenceAvailable,
            ObservedFrames: observedFrames,
            FramesWithTimingRows: framesWithTimingRows,
            FramesWithScoringRows: framesWithScoringRows,
            FramesWithAnyRows: framesWithAnyRows,
            FramesWithAnyBestLap: framesWithAnyBestLap,
            FramesWithAnyLastLap: framesWithAnyLastLap,
            FramesWithBestAndLastLap: framesWithBestAndLastLap,
            FramesWithRecentPersonalBest: framesWithRecentPersonalBest,
            FramesWithClassFastestBestLap: framesWithClassFastestBestLap,
            FramesWithClassFastestLastLap: framesWithClassFastestLastLap,
            MaxRows: maxRows,
            MaxRowsWithBestAndLastLap: maxRowsWithBestAndLastLap,
            MaxRowsWithRecentPersonalBest: maxRowsWithRecentPersonalBest,
            MaxRowsWithClassFastestBestLap: maxRowsWithClassFastestBestLap,
            MaxRowsWithClassFastestLastLap: maxRowsWithClassFastestLastLap,
            SourceFrameCounts: sourceFrameCounts,
            SourceRowCounts: sourceRowCounts,
            Classification: classification,
            BestVsLastReady: bestVsLastReady,
            RecentPersonalBestEvidenceAvailable: recentPersonalBestEvidenceAvailable,
            ClassFastestEvidenceAvailable: classFastestEvidenceAvailable,
            Interpretation: bestVsLastReady
                ? "Lap profile rows include usable best and last lap values; standings best-vs-last highlighting can be evaluated from bundle evidence."
                : "Lap profile rows do not yet prove usable best-vs-last lap pairs.");
    }

    private static string ClassifyLapProfileReadiness(
        bool evidenceAvailable,
        int? observedFrames,
        int? framesWithAnyRows,
        int? framesWithAnyBestLap,
        int? framesWithAnyLastLap,
        int? framesWithBestAndLastLap,
        int? framesWithRecentPersonalBest,
        int? framesWithClassFastestBestLap,
        int? framesWithClassFastestLastLap)
    {
        if (!evidenceAvailable)
        {
            return "not_recorded";
        }

        if (observedFrames.GetValueOrDefault() <= 0)
        {
            return "no_observed_frames";
        }

        if (framesWithAnyRows.GetValueOrDefault() <= 0)
        {
            return "no_rows";
        }

        if (framesWithAnyBestLap.GetValueOrDefault() <= 0 || framesWithAnyLastLap.GetValueOrDefault() <= 0)
        {
            return "missing_best_or_last_lap";
        }

        if (framesWithBestAndLastLap.GetValueOrDefault() <= 0)
        {
            return "best_and_last_not_on_same_row";
        }

        if (framesWithRecentPersonalBest.GetValueOrDefault() > 0
            && framesWithClassFastestBestLap.GetValueOrDefault() > 0
            && framesWithClassFastestLastLap.GetValueOrDefault() > 0)
        {
            return "best_vs_last_and_highlight_ready";
        }

        if (framesWithRecentPersonalBest.GetValueOrDefault() > 0)
        {
            return "best_vs_last_recent_personal_best_ready";
        }

        return "best_vs_last_ready";
    }

    private static void RecordLapProfileSourceCounts(
        SortedDictionary<string, int> sourceFrameCounts,
        SortedDictionary<string, int> sourceRowCounts,
        string source,
        IReadOnlyList<LapProfileRowDiagnostic> rows)
    {
        if (rows.Count == 0)
        {
            return;
        }

        Increment(sourceFrameCounts, $"{source}:rows");
        Increment(sourceRowCounts, $"{source}:rows", rows.Count);
        var readiness = LapProfileReadiness(rows);
        RecordLapProfileSourceReadiness(sourceFrameCounts, sourceRowCounts, source, "best-lap", readiness.RowsWithBestLap);
        RecordLapProfileSourceReadiness(sourceFrameCounts, sourceRowCounts, source, "last-lap", readiness.RowsWithLastLap);
        RecordLapProfileSourceReadiness(sourceFrameCounts, sourceRowCounts, source, "best-and-last-lap", readiness.RowsWithBestAndLastLap);
        RecordLapProfileSourceReadiness(sourceFrameCounts, sourceRowCounts, source, "recent-personal-best", readiness.RowsWithRecentPersonalBest);
        RecordLapProfileSourceReadiness(sourceFrameCounts, sourceRowCounts, source, "class-fastest-best-lap", readiness.RowsWithClassFastestBestLap);
        RecordLapProfileSourceReadiness(sourceFrameCounts, sourceRowCounts, source, "class-fastest-last-lap", readiness.RowsWithClassFastestLastLap);
    }

    private static void RecordLapProfileSourceReadiness(
        SortedDictionary<string, int> sourceFrameCounts,
        SortedDictionary<string, int> sourceRowCounts,
        string source,
        string key,
        int rowCount)
    {
        if (rowCount <= 0)
        {
            return;
        }

        Increment(sourceFrameCounts, $"{source}:{key}");
        Increment(sourceRowCounts, $"{source}:{key}", rowCount);
    }

    private static IEnumerable<LapProfileRowDiagnostic> LapProfileTimingRows(LiveTimingModel timing)
    {
        return timing.OverallRows
            .Concat(timing.ClassRows)
            .GroupBy(row => row.CarIdx)
            .Select(group => ToLapProfileRow(SelectLapProfileTimingRow(group)))
            .OrderBy(row => row.CarIdx);
    }

    private static IEnumerable<LapProfileRowDiagnostic> LapProfileScoringRows(LiveScoringModel scoring)
    {
        IEnumerable<LiveScoringRow> rows = scoring.Rows.Count > 0
            ? scoring.Rows
            : scoring.ClassGroups.SelectMany(group => group.Rows);

        return rows
            .GroupBy(row => row.CarIdx)
            .Select(group => ToLapProfileRow(SelectLapProfileScoringRow(group)))
            .OrderBy(row => row.CarIdx);
    }

    private static IEnumerable<LapProfileRowDiagnostic> MergeLapProfileRows(
        IReadOnlyList<LapProfileRowDiagnostic> timingRows,
        IReadOnlyList<LapProfileRowDiagnostic> scoringRows)
    {
        var timingByCarIdx = timingRows
            .GroupBy(row => row.CarIdx)
            .ToDictionary(group => group.Key, group => group.First());
        var scoringByCarIdx = scoringRows
            .GroupBy(row => row.CarIdx)
            .ToDictionary(group => group.Key, group => group.First());

        foreach (var carIdx in timingByCarIdx.Keys.Concat(scoringByCarIdx.Keys).Distinct().OrderBy(value => value))
        {
            timingByCarIdx.TryGetValue(carIdx, out var timing);
            scoringByCarIdx.TryGetValue(carIdx, out var scoring);
            yield return new LapProfileRowDiagnostic(
                CarIdx: carIdx,
                CarClass: scoring?.CarClass ?? timing?.CarClass,
                CarClassName: FirstNonEmpty(scoring?.CarClassName, timing?.CarClassName),
                CarClassColorHex: FirstNonEmpty(scoring?.CarClassColorHex, timing?.CarClassColorHex),
                BestLapTimeSeconds: ValidLapTimeSeconds(scoring?.BestLapTimeSeconds)
                    ?? ValidLapTimeSeconds(timing?.BestLapTimeSeconds),
                LastLapTimeSeconds: ValidLapTimeSeconds(scoring?.LastLapTimeSeconds)
                    ?? ValidLapTimeSeconds(timing?.LastLapTimeSeconds));
        }
    }

    private static LiveTimingRow SelectLapProfileTimingRow(IEnumerable<LiveTimingRow> rows)
    {
        return rows
            .OrderByDescending(row => ValidLapTimeSeconds(row.BestLapTimeSeconds) is not null
                && ValidLapTimeSeconds(row.LastLapTimeSeconds) is not null)
            .ThenByDescending(row => ValidLapTimeSeconds(row.BestLapTimeSeconds) is not null)
            .ThenByDescending(row => ValidLapTimeSeconds(row.LastLapTimeSeconds) is not null)
            .ThenByDescending(row => row.HasTiming)
            .ThenByDescending(row => row.Quality)
            .First();
    }

    private static LiveScoringRow SelectLapProfileScoringRow(IEnumerable<LiveScoringRow> rows)
    {
        return rows
            .OrderByDescending(row => ValidLapTimeSeconds(row.BestLapTimeSeconds) is not null
                && ValidLapTimeSeconds(row.LastLapTimeSeconds) is not null)
            .ThenByDescending(row => ValidLapTimeSeconds(row.BestLapTimeSeconds) is not null)
            .ThenByDescending(row => ValidLapTimeSeconds(row.LastLapTimeSeconds) is not null)
            .ThenBy(row => row.ClassPosition ?? int.MaxValue)
            .ThenBy(row => row.OverallPosition ?? int.MaxValue)
            .First();
    }

    private static LapProfileRowDiagnostic ToLapProfileRow(LiveTimingRow row)
    {
        return new LapProfileRowDiagnostic(
            CarIdx: row.CarIdx,
            CarClass: row.CarClass,
            CarClassName: row.CarClassName,
            CarClassColorHex: row.CarClassColorHex,
            BestLapTimeSeconds: ValidLapTimeSeconds(row.BestLapTimeSeconds),
            LastLapTimeSeconds: ValidLapTimeSeconds(row.LastLapTimeSeconds));
    }

    private static LapProfileRowDiagnostic ToLapProfileRow(LiveScoringRow row)
    {
        return new LapProfileRowDiagnostic(
            CarIdx: row.CarIdx,
            CarClass: row.CarClass,
            CarClassName: row.CarClassName,
            CarClassColorHex: row.CarClassColorHex,
            BestLapTimeSeconds: ValidLapTimeSeconds(row.BestLapTimeSeconds),
            LastLapTimeSeconds: ValidLapTimeSeconds(row.LastLapTimeSeconds));
    }

    private static LapProfileFrameReadinessDiagnostic LapProfileReadiness(IReadOnlyList<LapProfileRowDiagnostic> rows)
    {
        var classFastestLapByClass = rows
            .Where(row => ValidLapTimeSeconds(row.BestLapTimeSeconds) is not null)
            .GroupBy(row => ClassKey(row.CarClass, row.CarClassName, row.CarClassColorHex), StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(row => ValidLapTimeSeconds(row.BestLapTimeSeconds))
                    .Where(value => value is not null)
                    .Select(value => value!.Value)
                    .Min(),
                StringComparer.Ordinal);
        var bestLapRows = 0;
        var lastLapRows = 0;
        var bestAndLastRows = 0;
        var recentPersonalBestRows = 0;
        var classFastestBestLapRows = 0;
        var classFastestLastLapRows = 0;

        foreach (var row in rows)
        {
            var bestLap = ValidLapTimeSeconds(row.BestLapTimeSeconds);
            var lastLap = ValidLapTimeSeconds(row.LastLapTimeSeconds);
            var classFastestLap = classFastestLapByClass.TryGetValue(
                    ClassKey(row.CarClass, row.CarClassName, row.CarClassColorHex),
                    out var fastestLap)
                ? fastestLap
                : (double?)null;
            var isClassFastestBestLap = IsMatchingLapTime(bestLap, classFastestLap);
            var isClassFastestLastLap = IsMatchingLapTime(lastLap, classFastestLap);

            if (bestLap is not null)
            {
                bestLapRows++;
            }

            if (lastLap is not null)
            {
                lastLapRows++;
            }

            if (bestLap is not null && lastLap is not null)
            {
                bestAndLastRows++;
            }

            if (isClassFastestBestLap)
            {
                classFastestBestLapRows++;
            }

            if (isClassFastestLastLap)
            {
                classFastestLastLapRows++;
            }

            if (!isClassFastestBestLap && IsMatchingLapTime(lastLap, bestLap))
            {
                recentPersonalBestRows++;
            }
        }

        return new LapProfileFrameReadinessDiagnostic(
            bestLapRows,
            lastLapRows,
            bestAndLastRows,
            recentPersonalBestRows,
            classFastestBestLapRows,
            classFastestLastLapRows);
    }

    private static double? ValidLapTimeSeconds(double? seconds)
    {
        return LiveRaceProgressProjector.ValidLapTime(seconds);
    }

    private static bool IsMatchingLapTime(double? lapTimeSeconds, double? referenceLapTimeSeconds)
    {
        return lapTimeSeconds is { } lapTime
            && referenceLapTimeSeconds is { } referenceLapTime
            && Math.Abs(lapTime - referenceLapTime) <= 0.0005d;
    }

    private static string ClassKey(int? carClass, string? className, string? classColorHex)
    {
        if (carClass is { } value)
        {
            return FormattableString.Invariant($"id:{value}");
        }

        return FirstNonEmpty(className, classColorHex)?.Trim().ToUpperInvariant() ?? "unknown";
    }

    private static void Increment(SortedDictionary<string, int> values, string? key, int amount = 1)
    {
        if (amount <= 0)
        {
            return;
        }

        var normalizedKey = string.IsNullOrWhiteSpace(key) ? "unknown" : key.Trim();
        values.TryGetValue(normalizedKey, out var count);
        values[normalizedKey] = count + amount;
    }

    private static IReadOnlyList<SessionInfoSetupSignal> ExtractSetupSignals(string yaml)
    {
        var signals = new List<SessionInfoSetupSignal>();
        var stack = new List<(int Indent, string Key)>();
        foreach (var rawLine in yaml.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            var indent = line.TakeWhile(char.IsWhiteSpace).Count();
            var trimmed = line.Trim();
            var separator = trimmed.IndexOf(':', StringComparison.Ordinal);
            if (separator <= 0)
            {
                continue;
            }

            var key = trimmed[..separator].Trim();
            if (string.IsNullOrWhiteSpace(key) || key.StartsWith("-", StringComparison.Ordinal))
            {
                continue;
            }

            while (stack.Count > 0 && stack[^1].Indent >= indent)
            {
                stack.RemoveAt(stack.Count - 1);
            }

            var value = trimmed[(separator + 1)..].Trim();
            var path = string.Join(".", stack.Select(item => item.Key).Append(key));
            if (!string.IsNullOrWhiteSpace(value) && IsSetupSignalPath(path, key))
            {
                signals.Add(new SessionInfoSetupSignal(path, key, value));
                if (signals.Count >= 40)
                {
                    break;
                }
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                stack.Add((indent, key));
            }
        }

        return signals
            .OrderBy(signal => signal.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool IsSetupSignalPath(string path, string key)
    {
        if (!path.Contains("CarSetup", StringComparison.OrdinalIgnoreCase)
            && !path.Contains("Chassis", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return key.Contains("Arb", StringComparison.OrdinalIgnoreCase)
            || key.Contains("AntiRoll", StringComparison.OrdinalIgnoreCase)
            || key.Contains("Wing", StringComparison.OrdinalIgnoreCase)
            || string.Equals(key, "FuelLevel", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRaceSession(HistoricalSessionContext? context)
    {
        return ContainsRace(context?.Session.SessionType)
            || ContainsRace(context?.Session.SessionName)
            || ContainsRace(context?.Session.EventType);
    }

    private static bool ContainsRace(string? value)
    {
        return value?.IndexOf("race", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private IbtAnalysisDiagnosticsSnapshot IbtAnalysisDiagnostics()
    {
        var captureDirectory = LatestCaptureDirectory();
        var outputDirectoryName = IbtAnalysisOutputDirectoryName();
        var statusPath = string.IsNullOrWhiteSpace(captureDirectory)
            ? null
            : Path.Combine(captureDirectory, outputDirectoryName, "status.json");
        string? status = null;
        string? reason = null;
        string? sourcePath = null;
        string? candidateSelectedPath = null;
        string? sessionMatchStatus = null;
        string? sessionMatchReason = null;
        IReadOnlyList<string> sessionMatchMismatches = [];
        string? statusReadError = null;
        if (!string.IsNullOrWhiteSpace(statusPath) && File.Exists(statusPath))
        {
            try
            {
                var node = JsonNode.Parse(File.ReadAllText(statusPath));
                status = (string?)node?["status"];
                reason = (string?)node?["reason"];
                candidateSelectedPath = (string?)node?["candidateSelection"]?["selectedPath"];
                sourcePath = (string?)node?["sourcePath"]
                    ?? (string?)node?["source"]?["path"]
                    ?? candidateSelectedPath;
                sessionMatchStatus = (string?)node?["sessionMatch"]?["status"];
                sessionMatchReason = (string?)node?["sessionMatch"]?["reason"];
                sessionMatchMismatches = node?["sessionMatch"]?["mismatches"] is JsonArray mismatches
                    ? mismatches
                        .Select(item => (string?)item)
                        .Where(item => !string.IsNullOrWhiteSpace(item))
                        .Select(item => item!)
                        .ToArray()
                    : [];
            }
            catch (Exception exception)
            {
                statusReadError = exception.GetType().Name;
            }
        }

        return new IbtAnalysisDiagnosticsSnapshot(
            Enabled: _ibtAnalysisOptions.Enabled,
            TelemetryLoggingEnabled: _ibtAnalysisOptions.TelemetryLoggingEnabled,
            TelemetryRoot: _ibtAnalysisOptions.TelemetryRoot,
            MaxCandidateAgeMinutes: _ibtAnalysisOptions.MaxCandidateAgeMinutes,
            MaxCandidateBytes: _ibtAnalysisOptions.MaxCandidateBytes,
            MaxAnalysisMilliseconds: _ibtAnalysisOptions.MaxAnalysisMilliseconds,
            MaxSampledRecords: _ibtAnalysisOptions.MaxSampledRecords,
            MinStableAgeSeconds: _ibtAnalysisOptions.MinStableAgeSeconds,
            MaxIRacingExitWaitSeconds: _ibtAnalysisOptions.MaxIRacingExitWaitSeconds,
            MaxCandidateFiles: _ibtAnalysisOptions.MaxCandidateFiles,
            CopyIbtIntoCaptureDirectory: _ibtAnalysisOptions.CopyIbtIntoCaptureDirectory,
            OutputDirectoryName: outputDirectoryName,
            LatestCapture: new LatestCaptureIbtAnalysisDiagnostics(
                CaptureDirectory: captureDirectory,
                StatusPath: statusPath,
                StatusExists: !string.IsNullOrWhiteSpace(statusPath) && File.Exists(statusPath),
                Status: status,
                Reason: reason,
                SourcePath: sourcePath,
                CandidateSelectedPath: candidateSelectedPath,
                SessionMatchStatus: sessionMatchStatus,
                SessionMatchReason: sessionMatchReason,
                SessionMatchMismatches: sessionMatchMismatches,
                StatusReadError: statusReadError));
    }

    private string IbtAnalysisOutputDirectoryName()
    {
        return string.IsNullOrWhiteSpace(_ibtAnalysisOptions.OutputDirectoryName)
            ? "ibt-analysis"
            : _ibtAnalysisOptions.OutputDirectoryName;
    }

    private object TrackMapDiagnostics()
    {
        var snapshot = _trackMapStore.DiagnosticsSnapshot(CurrentTrackIdentity(), TrackMapUserMapLookupEnabled());
        return new
        {
            snapshot.GeneratedAtUtc,
            snapshot.UserRoot,
            snapshot.BundledRoot,
            snapshot.UserMapCount,
            snapshot.BundledMapCount,
            snapshot.InvalidUserMapCount,
            snapshot.InvalidBundledMapCount,
            snapshot.CurrentTrack,
            snapshot.RecentMaps,
            RuntimeLookup = new TrackMapRuntimeLookupDiagnostics(
                NativeReloadIntervalSeconds: 10d,
                BrowserModelFactoryReloadIntervalSeconds: 10d,
                LocalhostTrackMapRoute: "/api/track-map",
                BrowserOverlayRoute: TrackMapBrowserSource.Page.CanonicalRoute,
                BrowserSourceRefreshIntervalMilliseconds: TrackMapRenderModel.RefreshIntervalMilliseconds,
                ReloadPolicy: "Native and browser model factories re-read the selected track-map document on track/source changes and at least every 10 seconds; localhost /api/track-map resolves the best map on each request."),
            BuildEvents = BuildTrackMapGenerationEventDiagnostics()
        };
    }

    private TrackMapGenerationEventDiagnosticsSnapshot BuildTrackMapGenerationEventDiagnostics()
    {
        if (!Directory.Exists(_storageOptions.EventsRoot))
        {
            return new TrackMapGenerationEventDiagnosticsSnapshot(
                EventsRoot: _storageOptions.EventsRoot,
                Exists: false,
                RecentEventFilesScanned: 0,
                MalformedEventLineCount: 0,
                GeneratedCount: 0,
                SkippedCount: 0,
                RejectedCount: 0,
                FailedCount: 0,
                LatestGeneratedAtUtc: null,
                LatestGeneratedCaptureId: null,
                LatestGeneratedMapPath: null,
                EventNameCounts: EmptyStringIntDictionary(),
                ReasonCounts: EmptyStringIntDictionary(),
                RecentEvents: []);
        }

        var files = Directory
            .EnumerateFiles(_storageOptions.EventsRoot, "*.jsonl")
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .Take(MaxRecentEventFilesForDiagnostics)
            .ToArray();
        var events = new List<TrackMapGenerationEventDiagnostics>();
        var eventNameCounts = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var reasonCounts = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var malformed = 0;

        foreach (var file in files)
        {
            try
            {
                foreach (var line in File.ReadLines(file.FullName))
                {
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    if (!TryReadAppEvent(line, out var timestampUtc, out var name, out var properties))
                    {
                        malformed++;
                        continue;
                    }

                    if (!IsTrackMapGenerationEvent(name))
                    {
                        continue;
                    }

                    Increment(eventNameCounts, name);
                    var reason = FirstNonEmpty((string?)properties?["reason"], (string?)properties?["reasons"], (string?)properties?["error"]);
                    Increment(reasonCounts, reason);
                    events.Add(TrackMapGenerationEventDiagnostics.From(file.Name, timestampUtc, name!, properties));
                }
            }
            catch
            {
                malformed++;
            }
        }

        var latestGenerated = events
            .Where(item => string.Equals(item.Name, "track_map_generated", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.TimestampUtc ?? DateTimeOffset.MinValue)
            .FirstOrDefault();

        return new TrackMapGenerationEventDiagnosticsSnapshot(
            EventsRoot: _storageOptions.EventsRoot,
            Exists: true,
            RecentEventFilesScanned: files.Length,
            MalformedEventLineCount: malformed,
            GeneratedCount: events.Count(item => string.Equals(item.Name, "track_map_generated", StringComparison.OrdinalIgnoreCase)),
            SkippedCount: events.Count(item => string.Equals(item.Name, "track_map_generation_skipped", StringComparison.OrdinalIgnoreCase)),
            RejectedCount: events.Count(item => string.Equals(item.Name, "track_map_generation_rejected", StringComparison.OrdinalIgnoreCase)),
            FailedCount: events.Count(item => string.Equals(item.Name, "track_map_generation_failed", StringComparison.OrdinalIgnoreCase)),
            LatestGeneratedAtUtc: latestGenerated?.TimestampUtc,
            LatestGeneratedCaptureId: latestGenerated?.CaptureId,
            LatestGeneratedMapPath: latestGenerated?.MapPath,
            EventNameCounts: eventNameCounts,
            ReasonCounts: reasonCounts,
            RecentEvents: events
                .OrderByDescending(item => item.TimestampUtc ?? DateTimeOffset.MinValue)
                .ThenBy(item => item.FileName, StringComparer.OrdinalIgnoreCase)
                .Take(20)
                .ToArray());
    }

    private static bool IsTrackMapGenerationEvent(string? name)
    {
        return string.Equals(name, "track_map_generated", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "track_map_generation_skipped", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "track_map_generation_rejected", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "track_map_generation_failed", StringComparison.OrdinalIgnoreCase);
    }

    private HistoricalTrackIdentity? CurrentTrackIdentity()
    {
        var latestCaptureContext = LatestCaptureContext();
        if (HasTrackIdentity(latestCaptureContext?.Track))
        {
            return latestCaptureContext!.Track;
        }

        try
        {
            var liveTrack = _liveTelemetrySource.Snapshot().Context.Track;
            return HasTrackIdentity(liveTrack) ? liveTrack : null;
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Failed to read live telemetry track for diagnostics bundle track-map lookup.");
            return null;
        }
    }

    private HistoricalSessionContext? LatestCaptureContext()
    {
        var captureDirectory = LatestCaptureDirectory();
        if (string.IsNullOrWhiteSpace(captureDirectory))
        {
            return null;
        }

        var latestSessionPath = Path.Combine(captureDirectory, "latest-session.yaml");
        if (!File.Exists(latestSessionPath))
        {
            return null;
        }

        try
        {
            return SessionInfoSummaryParser.Parse(File.ReadAllText(latestSessionPath));
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Failed to parse latest capture session info for diagnostics bundle.");
            return null;
        }
    }

    private bool TrackMapUserMapLookupEnabled()
    {
        try
        {
            var settings = _settingsStore.Load();
            var trackMap = settings.Overlays.FirstOrDefault(
                overlay => string.Equals(overlay.Id, TrackMapOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase));
            return trackMap?.GetBooleanOption(OverlayOptionKeys.TrackMapBuildFromTelemetry, defaultValue: true) ?? true;
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Failed to read track-map user map setting for diagnostics bundle.");
            return true;
        }
    }

    private static bool HasTrackIdentity(HistoricalTrackIdentity? track)
    {
        return track is not null
            && (track.TrackId is not null
                || !string.IsNullOrWhiteSpace(track.TrackName)
                || !string.IsNullOrWhiteSpace(track.TrackDisplayName)
                || !string.IsNullOrWhiteSpace(track.TrackConfigName));
    }

    private void AddLiveOverlayWindows(
        ZipArchive archive,
        LiveOverlayWindowCaptureManifest liveOverlayWindowsSnapshot)
    {
        AddTextEntry(
            archive,
            "live-overlays/manifest.json",
            JsonSerializer.Serialize(liveOverlayWindowsSnapshot, JsonOptions));
        foreach (var file in _liveOverlayWindowCaptureStore.CaptureFiles())
        {
            AddFileIfExists(archive, file.SourcePath, file.EntryName);
        }

        var previewCapture = LiveOverlayPreviewScreenshotCapture.Capture(
            _storageOptions,
            _liveOverlayWindowCaptureStore.Options,
            _settingsStore,
            _trackMapStore,
            _performanceState);
        AddTextEntry(
            archive,
            "live-overlays/previews/manifest.json",
            JsonSerializer.Serialize(previewCapture.Manifest, JsonOptions));
        foreach (var image in previewCapture.Images)
        {
            AddBinaryEntry(archive, image.EntryName, image.PngBytes);
        }
    }

    private static object BrowserOverlayDiagnostics()
    {
        return new
        {
            Pages = BrowserOverlayCatalog.Pages
                .Select(page => new
                {
                    page.Id,
                    page.Title,
                    page.CanonicalRoute,
                    page.Routes,
                    page.RequiresTelemetry,
                    page.RenderWhenTelemetryUnavailable,
                    page.FadeWhenTelemetryUnavailable,
                    page.RefreshIntervalMilliseconds,
                    page.BodyClass
                })
                .OrderBy(page => page.Id, StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };
    }

    private object LocalhostOverlayModelDiagnostics(LocalhostOverlaySnapshot localhost)
    {
        try
        {
            var now = DateTimeOffset.UtcNow;
            var settings = _settingsStore.Load();
            var currentSnapshot = _liveTelemetrySource.Snapshot();
            var lastActiveSnapshot = _liveTelemetrySource.LastActiveSnapshot();
            var includeLastActive = (!currentSnapshot.IsConnected || !currentSnapshot.IsCollecting)
                && lastActiveSnapshot is not null;
            var overlayModelRequestCount = RequestCountForRoute(localhost.RouteCounts, "overlay_model");
            var overlayModelSuccessCount = BrowserOverlayCatalog.Pages.Sum(page =>
                RequestCountForPathStatus(localhost.PathStatusCodeCounts, [$"/api/overlay-model/{page.Id}"], 200));

            return new
            {
                GeneratedAtUtc = now,
                Purpose = "distinguishes localhost HTTP availability from browser-source model renderability",
                Localhost = new
                {
                    localhost.Enabled,
                    localhost.Port,
                    localhost.Prefix,
                    localhost.Status,
                    localhost.CapturedAtUtc,
                    localhost.TotalRequests,
                    localhost.SuccessfulRequests,
                    localhost.FailedRequests,
                    localhost.RequestErrorCount,
                    localhost.LastRequestAtUtc,
                    localhost.LastRequestPath,
                    localhost.LastRequestSourceUrl,
                    localhost.LastRequestRoute,
                    localhost.LastRequestStatusCode,
                    localhost.LastRequestClientKind,
                    localhost.HasRecentRequests,
                    localhost.LastPageEventAtUtc,
                    localhost.LastPageEventKind,
                    localhost.LastPageEventOverlayId,
                    localhost.LastPageEventClientId,
                    localhost.LastPageEventClientKind,
                    localhost.LastPageEventSourceUrl,
                    localhost.LastPageEventShouldRender,
                    localhost.LastPageEventStatus,
                    localhost.LastPageEventError,
                    localhost.ClientCounts,
                    localhost.RouteClientCounts,
                    localhost.PathClientCounts,
                    localhost.SourceUrlCounts,
                    localhost.SourceUrlClientCounts,
                    localhost.PageEventCounts,
                    localhost.PageEventOverlayCounts,
                    localhost.PageEventClientCounts,
                    localhost.PageEventSourceUrlCounts,
                    localhost.PageEventOverlayClientCounts,
                    localhost.PageEventClientIdCounts,
                    localhost.PageEventSourceUrlClientCounts,
                    localhost.RecentPageEvents,
                    OverlayModelRequestCount = overlayModelRequestCount,
                    OverlayModelSuccessCount = overlayModelSuccessCount,
                    AnyOverlayModelRequestSucceeded = overlayModelSuccessCount > 0
                },
                Telemetry = new
                {
                    Current = LiveSnapshotSummary("current", currentSnapshot),
                    LastActiveAvailable = lastActiveSnapshot is not null,
                    LastActiveIncluded = includeLastActive,
                    LastActive = includeLastActive && lastActiveSnapshot is not null
                        ? LiveSnapshotSummary("last-active", lastActiveSnapshot)
                        : null
                },
                Pages = BrowserOverlayCatalog.Pages
                    .Select(page => LocalhostOverlayPageModelDiagnostics(
                        page,
                        settings,
                        currentSnapshot,
                        lastActiveSnapshot,
                        includeLastActive,
                        now,
                        localhost))
                    .OrderBy(page => page.Id, StringComparer.OrdinalIgnoreCase)
                    .ToArray()
            };
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to collect localhost overlay model diagnostics metadata.");
            return new
            {
                GeneratedAtUtc = DateTimeOffset.UtcNow,
                Error = exception.GetType().Name,
                ErrorMessage = exception.Message
            };
        }
    }

    private LocalhostOverlayPageModelDiagnosticsSnapshot LocalhostOverlayPageModelDiagnostics(
        BrowserOverlayPage page,
        ApplicationSettings settings,
        LiveTelemetrySnapshot currentSnapshot,
        LiveTelemetrySnapshot? lastActiveSnapshot,
        bool includeLastActive,
        DateTimeOffset now,
        LocalhostOverlaySnapshot localhost)
    {
        var modelApiPath = $"/api/overlay-model/{page.Id}";
        var currentModelSnapshot = page.RequiresTelemetry ? currentSnapshot : LiveTelemetrySnapshot.Empty;
        var lastActiveModel = includeLastActive && page.RequiresTelemetry && lastActiveSnapshot is not null
            ? BuildOverlayModelDiagnostics(page, lastActiveSnapshot, settings, now, "last-active", matchesLocalhostEndpoint: false)
            : null;

        return new LocalhostOverlayPageModelDiagnosticsSnapshot(
            Id: page.Id,
            Title: page.Title,
            HtmlRoute: page.CanonicalRoute,
            ModelApiPath: modelApiPath,
            RequiresTelemetry: page.RequiresTelemetry,
            RenderWhenTelemetryUnavailable: page.RenderWhenTelemetryUnavailable,
            FadeWhenTelemetryUnavailable: page.FadeWhenTelemetryUnavailable,
            RefreshIntervalMilliseconds: page.RefreshIntervalMilliseconds,
            HtmlRouteRequestCount: RequestCountForPaths(localhost.PathCounts, page.Routes),
            HtmlRouteSuccessCount: RequestCountForPathStatus(localhost.PathStatusCodeCounts, page.Routes, 200),
            ModelApiRequestCount: RequestCountForPaths(localhost.PathCounts, [modelApiPath]),
            ModelApiSuccessCount: RequestCountForPathStatus(localhost.PathStatusCodeCounts, [modelApiPath], 200),
            PageLoadedEventCount: RequestCountForRoute(localhost.PageEventOverlayCounts, $"{page.Id}|page-loaded"),
            ModelRenderEventCount: RequestCountForRoute(localhost.PageEventOverlayCounts, $"{page.Id}|model-render"),
            ModelHiddenEventCount: RequestCountForRoute(localhost.PageEventOverlayCounts, $"{page.Id}|model-hidden"),
            ModelNullEventCount: RequestCountForRoute(localhost.PageEventOverlayCounts, $"{page.Id}|model-null"),
            ModelErrorEventCount: RequestCountForRoute(localhost.PageEventOverlayCounts, $"{page.Id}|model-error"),
            SourceUrlCounts: SourceUrlCountsForPaths(localhost.SourceUrlCounts, page.Routes.Append(modelApiPath)),
            SourceUrlClientCounts: SourceUrlClientCountsForPaths(localhost.SourceUrlClientCounts, page.Routes.Append(modelApiPath)),
            PageEventSourceUrlCounts: SourceUrlCountsForPaths(localhost.PageEventSourceUrlCounts, page.Routes),
            PageEventSourceUrlClientCounts: SourceUrlClientCountsForPaths(localhost.PageEventSourceUrlClientCounts, page.Routes),
            PageEventClientCounts: ClientCountsForPrefixedKeys(localhost.PageEventOverlayClientCounts, page.Id),
            PageEventClientIdCounts: ClientCountsForPrefixedKeys(localhost.PageEventClientIdCounts, page.Id),
            RecentPageEvents: localhost.RecentPageEvents
                .Where(pageEvent => string.Equals(pageEvent.OverlayId, page.Id, StringComparison.OrdinalIgnoreCase))
                .ToArray(),
            Current: BuildOverlayModelDiagnostics(page, currentModelSnapshot, settings, now, "current", matchesLocalhostEndpoint: true),
            LastActive: lastActiveModel);
    }

    private LocalhostOverlayModelDiagnosticsSnapshot BuildOverlayModelDiagnostics(
        BrowserOverlayPage page,
        LiveTelemetrySnapshot snapshot,
        ApplicationSettings settings,
        DateTimeOffset now,
        string snapshotSource,
        bool matchesLocalhostEndpoint)
    {
        try
        {
            if (_browserOverlayModelFactory.TryBuild(page.Id, snapshot, settings, now, out var response))
            {
                var model = response.Model;
                return new LocalhostOverlayModelDiagnosticsSnapshot(
                    SnapshotSource: snapshotSource,
                    MatchesLocalhostEndpoint: matchesLocalhostEndpoint,
                    BuildStatus: "built",
                    RenderDecision: model.ShouldRender ? "would_render" : "built_but_should_not_render",
                    GeneratedAtUtc: response.GeneratedAtUtc,
                    ShouldRender: model.ShouldRender,
                    Status: model.Status,
                    Source: model.Source,
                    BodyKind: model.BodyKind,
                    RootOpacity: model.RootOpacity,
                    Content: BrowserOverlayModelContentDiagnostics(model),
                    Error: null,
                    ErrorMessage: null);
            }

            return new LocalhostOverlayModelDiagnosticsSnapshot(
                SnapshotSource: snapshotSource,
                MatchesLocalhostEndpoint: matchesLocalhostEndpoint,
                BuildStatus: "not_built",
                RenderDecision: "model_not_found",
                GeneratedAtUtc: now,
                ShouldRender: false,
                Status: null,
                Source: null,
                BodyKind: null,
                RootOpacity: null,
                Content: null,
                Error: null,
                ErrorMessage: null);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to build localhost overlay model diagnostics for {OverlayId}.", page.Id);
            return new LocalhostOverlayModelDiagnosticsSnapshot(
                SnapshotSource: snapshotSource,
                MatchesLocalhostEndpoint: matchesLocalhostEndpoint,
                BuildStatus: "error",
                RenderDecision: "build_error",
                GeneratedAtUtc: now,
                ShouldRender: false,
                Status: null,
                Source: null,
                BodyKind: null,
                RootOpacity: null,
                Content: null,
                Error: exception.GetType().Name,
                ErrorMessage: exception.Message);
        }
    }

    private static object LiveSnapshotSummary(string source, LiveTelemetrySnapshot snapshot)
    {
        var session = snapshot.Models.Session;
        var reference = snapshot.Models.Reference;
        var directory = snapshot.Models.DriverDirectory;
        var sample = snapshot.LatestSample;

        return new
        {
            Source = source,
            snapshot.IsConnected,
            snapshot.IsCollecting,
            snapshot.SourceId,
            snapshot.StartedAtUtc,
            snapshot.LastUpdatedAtUtc,
            snapshot.Sequence,
            HasLatestSample = sample is not null,
            SessionKind = OverlayAvailabilityEvaluator.CurrentSessionKind(snapshot).ToString(),
            Session = new
            {
                SessionType = session.SessionType ?? snapshot.Context.Session.SessionType,
                EventType = session.EventType ?? snapshot.Context.Session.EventType,
                SessionState = session.SessionState ?? sample?.SessionState,
                SessionFlagsHex = FormatRawFlagsHex(session.SessionFlags ?? sample?.SessionFlags),
                session.SessionTimeRemainSeconds,
                session.SessionLapsRemain,
                session.RaceLaps
            },
            Focus = new
            {
                PlayerCarIdx = reference.PlayerCarIdx ?? directory.PlayerCarIdx ?? sample?.PlayerCarIdx,
                FocusCarIdx = reference.FocusCarIdx ?? directory.FocusCarIdx ?? sample?.FocusCarIdx,
                RawCamCarIdx = sample?.RawCamCarIdx,
                reference.HasExplicitNonPlayerFocus,
                FocusUnavailableReason = reference.FocusUnavailableReason ?? sample?.FocusUnavailableReason,
                ReferenceCarClass = reference.ReferenceCarClass ?? directory.ReferenceCarClass
            },
            Models = new
            {
                SessionHasData = session.HasData,
                DriverDirectoryHasData = directory.HasData,
                ReferenceHasData = reference.HasData,
                TimingHasData = snapshot.Models.Timing.HasData,
                ScoringHasData = snapshot.Models.Scoring.HasData,
                SpatialHasData = snapshot.Models.Spatial.HasData,
                FuelPitHasData = snapshot.Models.FuelPit.HasData,
                InputsHasData = snapshot.Models.Inputs.HasData,
                TrackMapHasSectors = snapshot.Models.TrackMap.HasSectors,
                TrackMapHasLiveTiming = snapshot.Models.TrackMap.HasLiveTiming
            }
        };
    }

    private static object BrowserOverlayModelContentDiagnostics(BrowserOverlayDisplayModel model)
    {
        return new
        {
            ColumnCount = model.Columns.Count,
            RowCount = model.Rows.Count,
            ReferenceRowCount = model.Rows.Count(row => row.IsReference),
            ClassHeaderCount = model.Rows.Count(row => row.IsClassHeader),
            PitRowCount = model.Rows.Count(row => row.IsPit),
            PartialRowCount = model.Rows.Count(row => row.IsPartial),
            PendingGridRowCount = model.Rows.Count(row => row.IsPendingGrid),
            PlaceholderRowCount = model.Rows.Count(row => row.IsPlaceholder),
            MetricCount = model.Metrics.Count,
            PointCount = model.Points.Count,
            HeaderItemCount = model.HeaderItems.Count,
            HeaderItems = model.HeaderItems.Select(item => new
            {
                item.Key,
                item.Value,
                item.Tone
            }).ToArray(),
            GridSectionCount = model.GridSections?.Count ?? 0,
            GridRowCount = model.GridSections?.Sum(section => section.Rows.Count) ?? 0,
            MetricSectionCount = model.MetricSections?.Count ?? 0,
            MetricSectionRowCount = model.MetricSections?.Sum(section => section.Rows.Count) ?? 0,
            Graph = GapGraphDiagnostics(model.Graph),
            CarRadar = model.CarRadar is null
                ? null
                : new
                {
                    model.CarRadar.IsAvailable,
                    model.CarRadar.HasCurrentSignal,
                    model.CarRadar.HasCarLeft,
                    model.CarRadar.HasCarRight,
                    CarCount = model.CarRadar.Cars.Count,
                    model.CarRadar.ShowMulticlassWarning,
                    model.CarRadar.PreviewVisible
                },
            TrackMap = model.TrackMap is null
                ? null
                : new
                {
                    MarkerCount = model.TrackMap.Markers.Count,
                    SectorCount = model.TrackMap.Sectors.Count,
                    model.TrackMap.ShowSectorBoundaries,
                    model.TrackMap.InternalOpacity,
                    model.TrackMap.IncludeUserMaps,
                    RenderMarkerCount = model.TrackMap.RenderModel.Markers.Count,
                    RenderPrimitiveCount = model.TrackMap.RenderModel.Primitives.Count
                },
            GarageCover = model.GarageCover is null
                ? null
                : new
                {
                    model.GarageCover.ShouldCover,
                    ImageStatus = model.GarageCover.BrowserSettings.ImageStatus,
                    DetectionState = model.GarageCover.Detection.State,
                    model.GarageCover.Detection.IsFresh
                },
            StreamChat = model.StreamChat is null
                ? null
                : new
                {
                    RowCount = model.StreamChat.Rows.Count,
                    model.StreamChat.Settings.Provider,
                    model.StreamChat.Settings.IsConfigured
                },
            Inputs = model.Inputs is null
                ? null
                : new
                {
                    model.Inputs.IsAvailable,
                    model.Inputs.HasContent,
                    model.Inputs.HasGraph,
                    model.Inputs.HasRail,
                    TracePointCount = model.Inputs.Trace.Count,
                    model.Inputs.ShowThrottle,
                    model.Inputs.ShowBrake,
                    model.Inputs.ShowClutch,
                    model.Inputs.ShowSteering,
                    model.Inputs.ShowGear,
                    model.Inputs.ShowSpeed
                },
            Flags = model.Flags is null
                ? null
                : new
                {
                    FlagCount = model.Flags.Flags.Count,
                    model.Flags.IsWaiting
                },
            EffectiveRendered = EffectiveRenderedDiagnostics(model.EffectiveSettings?.Rendered),
            model.FuelStrategyEvidence
        };
    }

    private static object? GapGraphDiagnostics(BrowserGapGraph? graph)
    {
        return graph is null
            ? null
            : new
            {
                SeriesCount = graph.Series.Count,
                graph.SelectedSeriesCount,
                SeriesPointCount = graph.Series.Sum(series => series.Points.Count),
                ReferenceSeriesCount = graph.Series.Count(series => series.IsReference),
                ClassLeaderSeriesCount = graph.Series.Count(series => series.IsClassLeader),
                StaleSeriesCount = graph.Series.Count(series => series.IsStale),
                StickyExitSeriesCount = graph.Series.Count(series => series.IsStickyExit),
                WeatherPointCount = graph.Weather.Count,
                LeaderChangeMarkerCount = graph.LeaderChanges.Count,
                DriverChangeMarkerCount = graph.DriverChanges.Count,
                PitWindowCount = graph.PitWindows.Count,
                graph.StartSeconds,
                graph.EndSeconds,
                graph.MaxGapSeconds,
                graph.LapReferenceSeconds,
                graph.ThreatCarIdx,
                ActiveThreatState = graph.ActiveThreat?.State,
                ActiveThreatLabel = graph.ActiveThreat?.Label,
                graph.MetricDeadbandSeconds,
                graph.ComparisonLabel,
                graph.ShowGraph,
                graph.ShowTrendMetrics,
                TrendMetricCount = graph.TrendMetrics.Count,
                Scale = graph.Scale is null
                    ? null
                    : new
                    {
                        graph.Scale.IsFocusRelative,
                        graph.Scale.MaxGapSeconds,
                        graph.Scale.AheadSeconds,
                        graph.Scale.BehindSeconds,
                        graph.Scale.LatestReferenceGapSeconds,
                        ReferencePointCount = graph.Scale.ReferencePoints.Count
                    }
            };
    }

    private static object? EffectiveRenderedDiagnostics(BrowserOverlayEffectiveRendered? rendered)
    {
        return rendered is null
            ? null
            : new
            {
                rendered.BodyKind,
                rendered.ShouldRender,
                rendered.RowCount,
                HeaderItemCount = rendered.HeaderItems.Count,
                rendered.ColumnKeys,
                RowIdentityCount = rendered.RowIdentities?.Count ?? 0,
                rendered.PlaceholderRowCount,
                rendered.BrowserSource,
                rendered.Provenance,
                rendered.RelativeTimingEvidence,
                rendered.TableStatus,
                rendered.TimingSanity,
                rendered.FuelStrategy,
                rendered.Layout,
                rendered.InputAvailability,
                rendered.MapFallback,
                rendered.UnavailableContentPolicy
            };
    }

    private static long RequestCountForRoute(IReadOnlyDictionary<string, long> counts, string route)
    {
        return counts.TryGetValue(route, out var count) ? count : 0L;
    }

    private static long RequestCountForPaths(IReadOnlyDictionary<string, long> counts, IEnumerable<string> paths)
    {
        var normalizedPaths = paths
            .Select(BrowserOverlayPage.NormalizeRoute)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return counts
            .Where(item => normalizedPaths.Contains(BrowserOverlayPage.NormalizeRoute(item.Key)))
            .Sum(item => item.Value);
    }

    private static IReadOnlyDictionary<string, long> SourceUrlCountsForPaths(
        IReadOnlyDictionary<string, long> counts,
        IEnumerable<string> paths)
    {
        var normalizedPaths = paths
            .Select(BrowserOverlayPage.NormalizeRoute)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return counts
            .Where(item => normalizedPaths.Contains(BrowserOverlayPage.NormalizeRoute(SourceUrlPath(item.Key))))
            .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyDictionary<string, long> SourceUrlClientCountsForPaths(
        IReadOnlyDictionary<string, long> counts,
        IEnumerable<string> paths)
    {
        var normalizedPaths = paths
            .Select(BrowserOverlayPage.NormalizeRoute)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return counts
            .Where(item => TrySplitSourceUrlClientKey(item.Key, out var sourceUrl, out _)
                && normalizedPaths.Contains(BrowserOverlayPage.NormalizeRoute(SourceUrlPath(sourceUrl))))
            .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyDictionary<string, long> ClientCountsForPrefixedKeys(
        IReadOnlyDictionary<string, long> counts,
        string prefixValue)
    {
        var prefix = $"{prefixValue}|";
        return counts
            .Where(item => item.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                item => item.Key[prefix.Length..],
                item => item.Value,
                StringComparer.OrdinalIgnoreCase);
    }

    private static string SourceUrlPath(string sourceUrl)
    {
        var queryIndex = sourceUrl.IndexOf('?', StringComparison.Ordinal);
        return queryIndex >= 0 ? sourceUrl[..queryIndex] : sourceUrl;
    }

    private static bool TrySplitSourceUrlClientKey(string key, out string sourceUrl, out string clientKind)
    {
        var separator = key.LastIndexOf('|');
        if (separator <= 0 || separator >= key.Length - 1)
        {
            sourceUrl = key;
            clientKind = string.Empty;
            return false;
        }

        sourceUrl = key[..separator];
        clientKind = key[(separator + 1)..];
        return true;
    }

    private static long RequestCountForPathStatus(
        IReadOnlyDictionary<string, long> counts,
        IEnumerable<string> paths,
        int statusCode)
    {
        var statusKey = statusCode.ToString(CultureInfo.InvariantCulture);
        var normalizedPaths = paths
            .Select(BrowserOverlayPage.NormalizeRoute)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return counts
            .Where(item => TrySplitPathStatusKey(item.Key, out var path, out var status)
                && string.Equals(status, statusKey, StringComparison.OrdinalIgnoreCase)
                && normalizedPaths.Contains(BrowserOverlayPage.NormalizeRoute(path)))
            .Sum(item => item.Value);
    }

    private static bool TrySplitPathStatusKey(string key, out string path, out string status)
    {
        var separator = key.LastIndexOf('|');
        if (separator <= 0 || separator >= key.Length - 1)
        {
            path = string.Empty;
            status = string.Empty;
            return false;
        }

        path = key[..separator];
        status = key[(separator + 1)..];
        return true;
    }

    private object GarageCoverDiagnostics()
    {
        try
        {
            return GarageCoverBrowserSettings.Diagnostics(
                _settingsStore.Load(),
                _localhostOverlayState.Snapshot(),
                _liveTelemetrySource.Snapshot());
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to collect Garage Cover diagnostics metadata.");
            return new
            {
                Route = "/overlays/garage-cover",
                Error = exception.Message
            };
        }
    }

    private object StreamChatDiagnostics()
    {
        try
        {
            return _streamChatSource.DiagnosticsSnapshot(StreamChatOverlayViewModel.BrowserSettingsFrom(_settingsStore.Load()));
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to collect Stream Chat diagnostics metadata.");
            return new
            {
                Route = "/overlays/stream-chat",
                Error = exception.Message
            };
        }
    }

    private object FlagsDiagnostics()
    {
        try
        {
            var settings = _settingsStore.Load();
            var snapshot = _liveTelemetrySource.Snapshot();
            return FlagsModelSummary(snapshot, settings, DateTimeOffset.UtcNow);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to collect Flags diagnostics metadata.");
            return new
            {
                Route = "/overlays/flags",
                Error = exception.Message
            };
        }
    }

    private object LiveTelemetrySynthesis()
    {
        try
        {
            var snapshot = _liveTelemetrySource.Snapshot();
            var now = DateTimeOffset.UtcNow;
            var lastActiveSnapshot = _liveTelemetrySource.LastActiveSnapshot();
            var availability = OverlayAvailabilityEvaluator.FromSnapshot(snapshot, now);
            var sample = snapshot.LatestSample;
            IReadOnlyList<HistoricalCarProximity> allCars = sample?.AllCars ?? [];
            var reference = snapshot.Models.Reference;
            var resolvedPlayerCarIdx = reference.PlayerCarIdx
                ?? snapshot.Models.DriverDirectory.PlayerCarIdx
                ?? sample?.PlayerCarIdx;
            var resolvedFocusCarIdx = reference.FocusCarIdx
                ?? snapshot.Models.DriverDirectory.FocusCarIdx
                ?? sample?.FocusCarIdx;
            var focusCar = resolvedFocusCarIdx is { } focusCarIdx
                ? allCars.FirstOrDefault(car => car.CarIdx == focusCarIdx)
                : null;
            var playerCar = resolvedPlayerCarIdx is { } playerCarIdx
                ? allCars.FirstOrDefault(car => car.CarIdx == playerCarIdx)
                : null;
            var settingsSnapshot = _settingsStore.Load();
            var focusContext = new
            {
                PlayerCarIdx = resolvedPlayerCarIdx,
                RawCamCarIdx = sample?.RawCamCarIdx,
                FocusCarIdx = resolvedFocusCarIdx,
                LatestSampleFocusCarIdx = sample?.FocusCarIdx,
                FocusUnavailableReason = reference.FocusUnavailableReason ?? sample?.FocusUnavailableReason,
                FocusDiffersFromPlayer = reference.HasData
                    ? reference.HasExplicitNonPlayerFocus
                    : resolvedPlayerCarIdx is { } playerIdx
                        && resolvedFocusCarIdx is { } focusIdx
                        && playerIdx != focusIdx,
                IsOnTrack = reference.HasData ? reference.IsOnTrack : sample?.IsOnTrack,
                IsInGarage = reference.HasData ? reference.IsInGarage : sample?.IsInGarage,
                IsGarageVisible = sample?.IsGarageVisible,
                IsReplayPlaying = sample?.IsReplayPlaying,
                OnPitRoad = reference.OnPitRoad ?? sample?.OnPitRoad,
                PlayerCarInPitStall = reference.HasData ? reference.PlayerCarInPitStall : sample?.PlayerCarInPitStall,
                SessionState = sample?.SessionState,
                SessionStateLabel = SessionStateLabel(sample?.SessionState),
                Availability = availability
            };
            var focusVsLocalContext = FocusVsLocalStrategyContext(
                snapshot,
                resolvedPlayerCarIdx,
                resolvedFocusCarIdx);
            var lapDeltaQuality = LapDeltaQualityFromSample(sample);
            var lapProfileReadiness = LapProfileReadinessFromSnapshot(snapshot);

            var carFieldCoverage = BuildCarFieldCoverage(allCars);
            var overlays = ManagedOverlayDefinitions()
                .Select(definition =>
                {
                    var settings = settingsSnapshot.GetOrAddOverlay(
                        definition.Id,
                        definition.DefaultWidth,
                        definition.DefaultHeight,
                        0,
                        0,
                        defaultEnabled: false);
                    var sessionAllowed = OverlaySessionPolicyEvaluator.IsAllowed(definition.Id, availability.SessionKind);
                    var context = LiveLocalStrategyContext.ForRequirement(snapshot, now, definition.ContextRequirement);
                    return new
                    {
                        definition.Id,
                        definition.DisplayName,
                        ContextRequirement = definition.ContextRequirement.ToString(),
                        settings.Enabled,
                        SessionAllowed = sessionAllowed,
                        ContextAvailable = context.IsAvailable,
                        ContextReason = context.Reason,
                        DesiredVisible = settings.Enabled && sessionAllowed && context.IsAvailable
                    };
                })
                .OrderBy(overlay => overlay.Id, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return new
            {
                GeneratedAtUtc = now,
                Evidence = new
                {
                    CurrentSnapshotConnected = snapshot.IsConnected,
                    CurrentSnapshotCollecting = snapshot.IsCollecting,
                    CurrentSnapshotSourceId = snapshot.SourceId,
                    LastActiveAvailable = lastActiveSnapshot is not null,
                    LastActiveSourceId = lastActiveSnapshot?.SourceId,
                    LastActiveSequence = lastActiveSnapshot?.Sequence,
                    LastActiveLastUpdatedAtUtc = lastActiveSnapshot?.LastUpdatedAtUtc,
                    LastActiveIsCurrentSnapshot = lastActiveSnapshot is not null && IsSameSnapshot(snapshot, lastActiveSnapshot),
                    CurrentDisconnectedWithLastActive = !snapshot.IsConnected && lastActiveSnapshot is not null
                },
                LastActive = LastActiveLiveTelemetrySummary(lastActiveSnapshot, snapshot, settingsSnapshot, now),
                FocusVsLocalContext = focusVsLocalContext,
                LapDeltaQuality = lapDeltaQuality,
                LapProfileReadiness = lapProfileReadiness,
                Snapshot = new
                {
                    snapshot.IsConnected,
                    snapshot.IsCollecting,
                    snapshot.SourceId,
                    snapshot.StartedAtUtc,
                    snapshot.LastUpdatedAtUtc,
                    snapshot.Sequence,
                    TelemetryAgeSeconds = availability.TelemetryAgeSeconds,
                    SessionKind = availability.SessionKind?.ToString(),
                    snapshot.Combo,
                    Session = snapshot.Models.Session
                },
                Focus = focusContext,
                SessionPhase = new
                {
                    SessionState = sample?.SessionState,
                    Label = SessionStateLabel(sample?.SessionState),
                    IsReplayPlaying = sample?.IsReplayPlaying,
                    SessionTime = sample?.SessionTime,
                    StartupOrPreGreenNote = "SessionState 1/2/3 and early SessionState 4 frames can have progress/timing arrays before official positions become valid."
                },
                FlagsModel = FlagsModelSummary(snapshot, settingsSnapshot, now),
                FieldSemantics = new
                {
                    OfficialPosition = "CarIdxPosition > 0",
                    OfficialClassPosition = "CarIdxClassPosition > 0",
                    LapDistanceProgress = "CarIdxLapCompleted >= 0 and CarIdxLapDistPct >= 0",
                    EstimatedTime = "CarIdxEstTime >= 0; positive counts exclude zero placeholders",
                    F2Time = "CarIdxF2Time >= 0; positive counts exclude zero placeholders",
                    CarClass = "CarIdxClass >= 0",
                    TrackSurface = "CarIdxTrackSurface >= 0",
                    SentinelNote = "-1 and 0 are not valid official positions; CarIdxClass 0 can be a valid single-class identifier; gridding/startup/replay contexts can still have class and progress before official order is populated."
                },
                CarFieldCoverage = carFieldCoverage,
                FocusCar = CarSnapshot(focusCar),
                PlayerCar = CarSnapshot(playerCar),
                TimingModel = new
                {
                    snapshot.Models.Timing.HasData,
                    snapshot.Models.Timing.Quality,
                    OverallRowCount = snapshot.Models.Timing.OverallRows.Count,
                    ClassRowCount = snapshot.Models.Timing.ClassRows.Count,
                    snapshot.Models.Coverage
                },
                RelativeModel = new
                {
                    snapshot.Models.Relative.HasData,
                    snapshot.Models.Relative.Quality,
                    snapshot.Models.Relative.ReferenceCarIdx,
                    RowCount = snapshot.Models.Relative.Rows.Count
                },
                ReferenceModel = ReferenceModelSummary(snapshot.Models.Reference),
                DriverDirectoryModel = DriverDirectoryModelSummary(snapshot.Models.DriverDirectory),
                RaceProgressModel = RaceProgressModelSummary(snapshot.Models.RaceProgress),
                RaceProjectionModel = RaceProjectionModelSummary(snapshot.Models.RaceProjection),
                IRatingProjectionModel = IRatingProjectionModelSummary(snapshot.Models.IRatingProjection),
                IncidentPressureModel = IncidentPressureModelSummary(snapshot.Models.IncidentPressure),
                RaceEventsModel = RaceEventsModelSummary(snapshot.Models.RaceEvents),
                TireCompoundModel = TireCompoundModelSummary(snapshot.Models.TireCompounds),
                TireConditionModel = TireConditionModelSummary(snapshot.Models.TireCondition),
                WeatherModel = WeatherModelSummary(snapshot.Models.Weather),
                InputsModel = InputsModelSummary(snapshot.Models.Inputs),
                FuelPitModel = FuelPitModelSummary(snapshot.Models.FuelPit),
                PitServiceModel = PitServiceModelSummary(snapshot.Models.PitService),
                SpatialRadarModel = SpatialRadarModelSummary(snapshot.Models.Spatial),
                ScoringModel = new
                {
                    snapshot.Models.Scoring.HasData,
                    snapshot.Models.Scoring.Quality,
                    snapshot.Models.Scoring.Source,
                    snapshot.Models.Scoring.ReferenceCarIdx,
                    RowCount = snapshot.Models.Scoring.Rows.Count,
                    ClassGroupCount = snapshot.Models.Scoring.ClassGroups.Count,
                    ClassGroups = snapshot.Models.Scoring.ClassGroups
                        .Select(group => new
                        {
                            group.CarClass,
                            group.ClassName,
                            group.RowCount,
                            group.IsReferenceClass
                        })
                        .ToArray()
                },
                TrackMapModel = TrackMapModelSummary(snapshot.Models.TrackMap),
                LocalContexts = new
                {
                    FuelCalculator = LiveLocalStrategyContext.ForFuelCalculator(snapshot, now),
                    PitService = LiveLocalStrategyContext.ForPitService(snapshot, now),
                    LocalInCar = LiveLocalStrategyContext.ForRequirement(snapshot, now, OverlayContextRequirement.LocalPlayerInCar)
                },
                Overlays = overlays,
                Cars = allCars
                    .OrderByDescending(HasOfficialPosition)
                    .ThenByDescending(HasProgress)
                    .ThenBy(car => car.Position is > 0 ? car.Position.Value : int.MaxValue)
                    .ThenBy(car => car.CarIdx)
                    .Take(MaxLiveTelemetryCarExamples)
                    .Select(CarSnapshot)
                    .ToArray()
            };
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to collect live telemetry synthesis metadata.");
            return new
            {
                GeneratedAtUtc = DateTimeOffset.UtcNow,
                Error = exception.Message
            };
        }
    }

    private static object? LastActiveLiveTelemetrySummary(
        LiveTelemetrySnapshot? lastActiveSnapshot,
        LiveTelemetrySnapshot currentSnapshot,
        ApplicationSettings settings,
        DateTimeOffset now)
    {
        if (lastActiveSnapshot is null || IsSameSnapshot(lastActiveSnapshot, currentSnapshot))
        {
            return null;
        }

        var availability = OverlayAvailabilityEvaluator.FromSnapshot(lastActiveSnapshot, now);
        var sample = lastActiveSnapshot.LatestSample;
        return new
        {
            Snapshot = new
            {
                lastActiveSnapshot.IsConnected,
                lastActiveSnapshot.IsCollecting,
                lastActiveSnapshot.SourceId,
                lastActiveSnapshot.StartedAtUtc,
                lastActiveSnapshot.LastUpdatedAtUtc,
                lastActiveSnapshot.Sequence,
                TelemetryAgeSeconds = availability.TelemetryAgeSeconds,
                SessionKind = availability.SessionKind?.ToString(),
                lastActiveSnapshot.Combo,
                Session = lastActiveSnapshot.Models.Session
            },
            Focus = new
            {
                PlayerCarIdx = lastActiveSnapshot.Models.Reference.PlayerCarIdx
                    ?? lastActiveSnapshot.Models.DriverDirectory.PlayerCarIdx
                    ?? sample?.PlayerCarIdx,
                RawCamCarIdx = sample?.RawCamCarIdx,
                FocusCarIdx = lastActiveSnapshot.Models.Reference.FocusCarIdx
                    ?? lastActiveSnapshot.Models.DriverDirectory.FocusCarIdx
                    ?? sample?.FocusCarIdx,
                LatestSampleFocusCarIdx = sample?.FocusCarIdx,
                FocusUnavailableReason = lastActiveSnapshot.Models.Reference.FocusUnavailableReason ?? sample?.FocusUnavailableReason,
                SessionState = sample?.SessionState,
                SessionStateLabel = SessionStateLabel(sample?.SessionState),
                Availability = availability
            },
            SessionPhase = new
            {
                SessionState = sample?.SessionState,
                Label = SessionStateLabel(sample?.SessionState),
                IsReplayPlaying = sample?.IsReplayPlaying,
                SessionTime = sample?.SessionTime
            },
            FlagsModel = FlagsModelSummary(lastActiveSnapshot, settings, now),
            CarFieldCoverage = BuildCarFieldCoverage(sample?.AllCars ?? []),
            RaceProgressModel = RaceProgressModelSummary(lastActiveSnapshot.Models.RaceProgress),
            FocusVsLocalContext = FocusVsLocalStrategyContext(
                lastActiveSnapshot,
                lastActiveSnapshot.Models.Reference.PlayerCarIdx
                    ?? lastActiveSnapshot.Models.DriverDirectory.PlayerCarIdx
                    ?? sample?.PlayerCarIdx,
                lastActiveSnapshot.Models.Reference.FocusCarIdx
                    ?? lastActiveSnapshot.Models.DriverDirectory.FocusCarIdx
                    ?? sample?.FocusCarIdx),
            LapDeltaQuality = LapDeltaQualityFromSample(sample),
            LapProfileReadiness = LapProfileReadinessFromSnapshot(lastActiveSnapshot),
            RaceProjectionModel = RaceProjectionModelSummary(lastActiveSnapshot.Models.RaceProjection),
            IRatingProjectionModel = IRatingProjectionModelSummary(lastActiveSnapshot.Models.IRatingProjection),
            IncidentPressureModel = IncidentPressureModelSummary(lastActiveSnapshot.Models.IncidentPressure),
            RaceEventsModel = RaceEventsModelSummary(lastActiveSnapshot.Models.RaceEvents),
            FuelPitModel = FuelPitModelSummary(lastActiveSnapshot.Models.FuelPit),
            PitServiceModel = PitServiceModelSummary(lastActiveSnapshot.Models.PitService),
            SpatialRadarModel = SpatialRadarModelSummary(lastActiveSnapshot.Models.Spatial),
            TrackMapModel = TrackMapModelSummary(lastActiveSnapshot.Models.TrackMap),
            LocalContexts = new
            {
                FuelCalculator = LiveLocalStrategyContext.ForFuelCalculator(lastActiveSnapshot, now),
                PitService = LiveLocalStrategyContext.ForPitService(lastActiveSnapshot, now),
                LocalInCar = LiveLocalStrategyContext.ForRequirement(lastActiveSnapshot, now, OverlayContextRequirement.LocalPlayerInCar)
            }
        };
    }

    private static bool IsSameSnapshot(LiveTelemetrySnapshot first, LiveTelemetrySnapshot second)
    {
        return first.Sequence == second.Sequence
            && string.Equals(first.SourceId, second.SourceId, StringComparison.Ordinal)
            && first.LastUpdatedAtUtc == second.LastUpdatedAtUtc;
    }

    private static object ReferenceModelSummary(LiveReferenceModel reference)
    {
        return new
        {
            reference.HasData,
            reference.Quality,
            reference.PlayerCarIdx,
            reference.FocusCarIdx,
            reference.FocusIsPlayer,
            reference.HasExplicitNonPlayerFocus,
            reference.FocusUsesPlayerLocalFallback,
            reference.FocusUnavailableReason,
            reference.ReferenceCarClass,
            reference.OverallPosition,
            reference.ClassPosition,
            reference.LapCompleted,
            reference.LapDistPct,
            reference.ProgressLaps,
            reference.F2TimeSeconds,
            reference.EstimatedTimeSeconds,
            reference.TrackSurface,
            reference.OnPitRoad,
            reference.PlayerCarClass,
            reference.PlayerLapCompleted,
            reference.PlayerLapDistPct,
            reference.PlayerProgressLaps,
            reference.PlayerTrackSurface,
            reference.PlayerOnPitRoad,
            reference.PlayerYawNorthRadians,
            reference.IsOnTrack,
            reference.IsInGarage,
            reference.PlayerCarInPitStall,
            reference.HasTimingReference,
            reference.HasTrackPlacement,
            TimingEvidence = EvidenceSummary(reference.TimingEvidence),
            SpatialEvidence = EvidenceSummary(reference.SpatialEvidence),
            MissingSignalCount = reference.MissingSignals.Count,
            reference.MissingSignals
        };
    }

    private static object DriverDirectoryModelSummary(LiveDriverDirectoryModel directory)
    {
        return new
        {
            directory.HasData,
            directory.Quality,
            directory.PlayerCarIdx,
            directory.FocusCarIdx,
            directory.ReferenceCarClass,
            HasPlayerDriver = directory.PlayerDriver is not null,
            HasFocusDriver = directory.FocusDriver is not null,
            DriverCount = directory.Drivers.Count,
            ClassCount = directory.Drivers
                .Where(driver => driver.CarClassId is not null)
                .Select(driver => driver.CarClassId!.Value)
                .Distinct()
                .Count(),
            SpectatorCount = directory.Drivers.Count(driver => driver.IsSpectator == true)
        };
    }

    private static object RaceProgressModelSummary(LiveRaceProgressModel progress)
    {
        return new
        {
            progress.HasData,
            progress.Quality,
            progress.StrategyCarProgressLaps,
            progress.ReferenceCarProgressLaps,
            progress.OverallLeaderProgressLaps,
            progress.ClassLeaderProgressLaps,
            progress.StrategyOverallLeaderGapLaps,
            progress.StrategyClassLeaderGapLaps,
            progress.ReferenceOverallLeaderGapLaps,
            progress.ReferenceClassLeaderGapLaps,
            progress.StrategyOverallPosition,
            progress.StrategyClassPosition,
            progress.ReferenceOverallPosition,
            progress.ReferenceClassPosition,
            progress.StrategyLapTimeSeconds,
            progress.StrategyLapTimeSource,
            progress.RacePaceSeconds,
            progress.RacePaceSource,
            progress.RaceLapsRemaining,
            progress.RaceLapsRemainingSource,
            MissingSignalCount = progress.MissingSignals.Count,
            progress.MissingSignals
        };
    }

    private static object FocusVsLocalStrategyContext(
        LiveTelemetrySnapshot snapshot,
        int? playerCarIdx,
        int? focusCarIdx)
    {
        var progress = snapshot.Models.RaceProgress;
        var reference = snapshot.Models.Reference;
        var focusDiffersFromPlayer = reference.HasData
            ? reference.HasExplicitNonPlayerFocus
            : playerCarIdx is { } player && focusCarIdx is { } focus && player != focus;
        var strategyLooksUnavailableWhileReferenceHasProgress =
            focusDiffersFromPlayer
            && progress.ReferenceCarProgressLaps is > 0d
            && progress.StrategyCarProgressLaps.GetValueOrDefault() <= 0d;

        return new
        {
            PlayerCarIdx = playerCarIdx,
            FocusCarIdx = focusCarIdx,
            FocusDiffersFromPlayer = focusDiffersFromPlayer,
            Classification = focusDiffersFromPlayer
                ? "focus_differs_from_local_strategy_context"
                : "focus_matches_local_strategy_context",
            StrategyContext = "local-player/team",
            StrategyContextLabel = ContextLabel("local-player/team", playerCarIdx),
            StrategyContextCarIdx = playerCarIdx,
            ReferenceContext = "focus/reference",
            ReferenceContextLabel = ContextLabel("focus/reference", focusCarIdx),
            ReferenceContextCarIdx = focusCarIdx,
            StrategyAndReferenceContextsDiffer = focusDiffersFromPlayer,
            StrategyFieldsAreLocalPlayerOrTeam = true,
            ReferenceFieldsAreFocusOrReferenceCar = true,
            progress.StrategyCarProgressLaps,
            progress.ReferenceCarProgressLaps,
            progress.StrategyOverallPosition,
            progress.ReferenceOverallPosition,
            progress.StrategyClassPosition,
            progress.ReferenceClassPosition,
            StrategyLooksUnavailableWhileReferenceHasProgress = strategyLooksUnavailableWhileReferenceHasProgress,
            Evidence = new
            {
                ReferenceModelHasData = reference.HasData,
                reference.HasExplicitNonPlayerFocus,
                reference.FocusUsesPlayerLocalFallback,
                ReferenceMissingSignals = reference.MissingSignals,
                StrategyFields = new[]
                {
                    nameof(progress.StrategyCarProgressLaps),
                    nameof(progress.StrategyOverallPosition),
                    nameof(progress.StrategyClassPosition)
                },
                ReferenceFields = new[]
                {
                    nameof(progress.ReferenceCarProgressLaps),
                    nameof(progress.ReferenceOverallPosition),
                    nameof(progress.ReferenceClassPosition)
                },
                Rule = "Fuel/strategy fields describe local-player/team context; focus/reference fields describe the active camera/reference car."
            },
            Interpretation = strategyLooksUnavailableWhileReferenceHasProgress
                ? "Strategy progress/position appears unavailable for the local-player/team context while reference progress is available for the focused car. Labels should not imply strategy fields describe the focused competitor."
                : "Strategy fields describe the local-player/team context; reference fields describe the focused/reference car context."
        };
    }

    private static string ContextLabel(string context, int? carIdx)
    {
        return carIdx is { } value
            ? $"{context} car {value}"
            : $"{context} unavailable";
    }

    private static object RaceProjectionModelSummary(LiveRaceProjectionModel projection)
    {
        return new
        {
            projection.HasData,
            projection.Quality,
            projection.OverallLeaderPaceSeconds,
            projection.OverallLeaderPaceSource,
            projection.OverallLeaderPaceConfidence,
            projection.ReferenceClassPaceSeconds,
            projection.ReferenceClassPaceSource,
            projection.ReferenceClassPaceConfidence,
            projection.TeamPaceSeconds,
            projection.TeamPaceSource,
            projection.TeamPaceConfidence,
            projection.EstimatedFinishLap,
            projection.EstimatedTeamLapsRemaining,
            projection.EstimatedTeamLapsRemainingSource,
            ClassProjectionCount = projection.ClassProjections.Count,
            ClassProjections = projection.ClassProjections
                .Select(classProjection => new
                {
                    classProjection.CarClass,
                    classProjection.ClassName,
                    classProjection.PaceSeconds,
                    classProjection.PaceSource,
                    classProjection.PaceConfidence,
                    classProjection.EstimatedLapsRemaining,
                    classProjection.EstimatedLapsRemainingSource
                })
                .ToArray(),
            MissingSignalCount = projection.MissingSignals.Count,
            projection.MissingSignals
        };
    }

    private static object IRatingProjectionModelSummary(LiveIRatingProjectionModel projection)
    {
        return new
        {
            projection.HasData,
            projection.Quality,
            Evidence = EvidenceSummary(projection.Evidence),
            projection.ProjectionScope,
            projection.ReferenceCarIdx,
            projection.ReferenceProjectedChange,
            projection.ReferenceProjectedIRating,
            ClassProjectionCount = projection.ClassProjections.Count,
            RowCount = projection.Rows.Count,
            ClassProjections = projection.ClassProjections
                .Select(classProjection => new
                {
                    classProjection.CarClass,
                    classProjection.ClassName,
                    classProjection.FieldSize,
                    classProjection.StrengthOfField,
                    RowCount = classProjection.Rows.Count
                })
                .ToArray(),
            ReferenceRow = projection.ReferenceCarIdx is { } referenceCarIdx
                ? projection.Rows
                    .Where(row => row.CarIdx == referenceCarIdx)
                    .Select(IRatingProjectionRowSummary)
                    .FirstOrDefault()
                : null,
            MissingSignalCount = projection.MissingSignals.Count,
            projection.MissingSignals,
            LimitationCount = projection.Limitations.Count,
            projection.Limitations,
            EstimationNote = "Live projection is derived from DriverInfo.IRating and current class results. Team races publish a team-entry estimate only; weighted team rating, per-driver lap-share distribution, fair-share eligibility, and zero-lap teammate effects are not applied from live session YAML."
        };
    }

    private static object IRatingProjectionRowSummary(LiveIRatingProjectionRow row)
    {
        return new
        {
            row.CarIdx,
            row.CarClass,
            row.ClassPosition,
            row.CurrentIRating,
            row.ProjectedChange,
            row.ProjectedIRating,
            row.IsPlayer,
            row.IsFocus
        };
    }

    private static object IncidentPressureModelSummary(LiveIncidentPressureModel pressure)
    {
        return new
        {
            pressure.HasData,
            pressure.Quality,
            Evidence = EvidenceSummary(pressure.Evidence),
            pressure.PlayerCarIdx,
            pressure.FocusCarIdx,
            pressure.PlayerCarTeamIncidentCount,
            pressure.PlayerCarMyIncidentCount,
            pressure.PlayerCarDriverIncidentCount,
            pressure.PlayerIncidents,
            pressure.CurrentFlaggedCarCount,
            pressure.CurrentOffTrackCarCount,
            CarCount = pressure.Cars.Count,
            Cars = pressure.Cars
                .Take(MaxLiveTelemetryCarExamples)
                .Select(IncidentPressureCarSummary)
                .ToArray(),
            MissingSignalCount = pressure.MissingSignals.Count,
            pressure.MissingSignals,
            EstimationNote = "Only local/player incident counters are true counts. Other-car pressure is a low-confidence estimate from per-car flags, current surface state, and observed off-track transitions."
        };
    }

    private static object IncidentPressureCarSummary(LiveIncidentPressureCar car)
    {
        return new
        {
            car.CarIdx,
            car.DriverName,
            car.TeamName,
            car.CarNumber,
            car.CarClass,
            car.IsPlayer,
            car.IsFocus,
            car.SessionFlags,
            SessionFlagsHex = FormatRawFlagsHex(car.SessionFlags),
            car.TrackSurface,
            car.OnPitRoad,
            car.HasBlackFlag,
            car.HasDisqualifyFlag,
            car.HasRepairFlag,
            car.HasFurledFlag,
            car.IsCurrentlyOffTrack,
            car.ObservedOffTrackTransitions,
            car.PressureScore,
            car.PressureLevel,
            Evidence = EvidenceSummary(car.Evidence)
        };
    }

    private static object RaceEventsModelSummary(LiveRaceEventModel raceEvents)
    {
        return new
        {
            raceEvents.HasData,
            raceEvents.Quality,
            raceEvents.IsOnTrack,
            raceEvents.IsInGarage,
            raceEvents.IsGarageVisible,
            raceEvents.OnPitRoad,
            raceEvents.Lap,
            raceEvents.LapCompleted,
            raceEvents.LapDistPct,
            raceEvents.DriversSoFar,
            raceEvents.DriverChangeLapStatus
        };
    }

    private static object TireCompoundModelSummary(LiveTireCompoundModel tireCompounds)
    {
        return new
        {
            tireCompounds.HasData,
            tireCompounds.Quality,
            DefinitionCount = tireCompounds.Definitions.Count,
            CarCount = tireCompounds.Cars.Count,
            PlayerCar = TireCompoundCarSummary(tireCompounds.PlayerCar),
            FocusCar = TireCompoundCarSummary(tireCompounds.FocusCar),
            Definitions = tireCompounds.Definitions
                .Select(definition => new
                {
                    definition.Index,
                    definition.Label,
                    definition.ShortLabel,
                    definition.IsWet
                })
                .ToArray(),
            CarEvidenceCounts = tireCompounds.Cars
                .GroupBy(car => EvidenceKey(car.Evidence))
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase)
        };
    }

    private static object TireConditionModelSummary(LiveTireConditionModel tireCondition)
    {
        return new
        {
            tireCondition.HasData,
            tireCondition.Quality,
            Evidence = EvidenceSummary(tireCondition.Evidence),
            Corners = new[]
            {
                TireCornerConditionSummary(tireCondition.LeftFront),
                TireCornerConditionSummary(tireCondition.RightFront),
                TireCornerConditionSummary(tireCondition.LeftRear),
                TireCornerConditionSummary(tireCondition.RightRear)
            }
        };
    }

    private static object FlagsModelSummary(
        LiveTelemetrySnapshot snapshot,
        ApplicationSettings settings,
        DateTimeOffset now)
    {
        var overlay = settings.GetOrAddOverlay(
            FlagsOverlayDefinition.Definition.Id,
            FlagsOverlayDefinition.Definition.DefaultWidth,
            FlagsOverlayDefinition.Definition.DefaultHeight,
            defaultEnabled: false);
        var viewModel = FlagsOverlayViewModel.ForDisplay(snapshot, now);
        var displayFlags = viewModel.Flags
            .Select(flag => new
            {
                Kind = flag.Kind.ToString(),
                Category = flag.Category.ToString(),
                flag.Label,
                flag.Detail,
                Tone = flag.Tone.ToString(),
                Enabled = IsFlagCategoryEnabled(overlay, flag.Category)
            })
            .ToArray();
        var enabledDisplayFlags = displayFlags
            .Where(flag => flag.Enabled)
            .ToArray();
        var session = snapshot.Models.Session;

        return new
        {
            Route = "/overlays/flags",
            OverlayId = FlagsOverlayDefinition.Definition.Id,
            overlay.Enabled,
            EnabledCategories = new
            {
                Green = overlay.GetBooleanOption(OverlayOptionKeys.FlagsShowGreen, defaultValue: true),
                Blue = overlay.GetBooleanOption(OverlayOptionKeys.FlagsShowBlue, defaultValue: true),
                Yellow = overlay.GetBooleanOption(OverlayOptionKeys.FlagsShowYellow, defaultValue: true),
                Critical = overlay.GetBooleanOption(OverlayOptionKeys.FlagsShowCritical, defaultValue: true),
                Finish = overlay.GetBooleanOption(OverlayOptionKeys.FlagsShowFinish, defaultValue: true)
            },
            Snapshot = new
            {
                snapshot.IsConnected,
                snapshot.IsCollecting,
                snapshot.SourceId,
                snapshot.LastUpdatedAtUtc,
                snapshot.Sequence
            },
            Session = new
            {
                session.HasData,
                session.Quality,
                session.SessionType,
                session.SessionName,
                session.EventType,
                session.SessionState,
                SessionStateLabel = SessionStateLabel(session.SessionState),
                session.SessionFlags,
                SessionFlagsHex = FormatRawFlagsHex(session.SessionFlags),
                session.SessionTimeRemainSeconds,
                session.SessionLapsRemain,
                session.RaceLaps,
                session.SessionLapsTotal
            },
            viewModel.IsWaiting,
            viewModel.Status,
            Tone = viewModel.Tone.ToString(),
            RawFlags = viewModel.RawFlags,
            RawFlagsHex = FormatRawFlagsHex(viewModel.RawFlags),
            DisplayFlagCount = displayFlags.Length,
            EnabledDisplayFlagCount = enabledDisplayFlags.Length,
            SuppressedBySettingsCount = displayFlags.Length - enabledDisplayFlags.Length,
            DisplayFlags = displayFlags,
            EnabledDisplayFlags = enabledDisplayFlags
        };
    }

    private static object WeatherModelSummary(LiveWeatherModel weather)
    {
        return new
        {
            weather.HasData,
            weather.Quality,
            weather.AirTempC,
            weather.TrackTempCrewC,
            weather.TrackWetness,
            weather.TrackWetnessLabel,
            weather.WeatherDeclaredWet,
            weather.DeclaredWetSurfaceMismatch,
            weather.WeatherType,
            weather.SkiesLabel,
            weather.PrecipitationPercent,
            weather.WindVelocityMetersPerSecond,
            weather.WindDirectionRadians,
            weather.RelativeHumidityPercent,
            weather.FogLevelPercent,
            weather.AirPressurePa,
            weather.SolarAltitudeRadians,
            weather.SolarAzimuthRadians,
            weather.RubberState
        };
    }

    private static object InputsModelSummary(LiveInputTelemetryModel inputs)
    {
        return new
        {
            inputs.HasData,
            inputs.Quality,
            inputs.HasPedalInputs,
            inputs.HasSteeringInput,
            inputs.SpeedMetersPerSecond,
            inputs.Gear,
            inputs.Rpm,
            HasThrottle = inputs.Throttle is not null,
            HasBrake = inputs.Brake is not null,
            HasClutch = inputs.Clutch is not null,
            HasSteeringWheelAngle = inputs.SteeringWheelAngle is not null,
            inputs.BrakeAbsActive,
            inputs.EngineWarnings,
            HasVoltage = inputs.Voltage is not null,
            HasWaterTemp = inputs.WaterTempC is not null,
            HasOilTemp = inputs.OilTempC is not null,
            HasOilPressure = inputs.OilPressureBar is not null,
            HasFuelPressure = inputs.FuelPressureBar is not null
        };
    }

    private static object FuelPitModelSummary(LiveFuelPitModel fuelPit)
    {
        return new
        {
            fuelPit.HasData,
            fuelPit.Quality,
            Fuel = new
            {
                fuelPit.Fuel.HasValidFuel,
                fuelPit.Fuel.Source,
                fuelPit.Fuel.FuelLevelLiters,
                fuelPit.Fuel.FuelLevelPercent,
                HasFuelUsePerHour = fuelPit.Fuel.FuelUsePerHourKg is not null
                    || fuelPit.Fuel.FuelUsePerHourLiters is not null,
                fuelPit.Fuel.FuelPerLapLiters,
                fuelPit.Fuel.LapTimeSeconds,
                fuelPit.Fuel.LapTimeSource,
                fuelPit.Fuel.Confidence
            },
            fuelPit.OnPitRoad,
            fuelPit.PitstopActive,
            fuelPit.PlayerCarInPitStall,
            fuelPit.TeamOnPitRoad,
            FuelLevelEvidence = EvidenceSummary(fuelPit.FuelLevelEvidence),
            InstantaneousBurnEvidence = EvidenceSummary(fuelPit.InstantaneousBurnEvidence),
            MeasuredBurnEvidence = EvidenceSummary(fuelPit.MeasuredBurnEvidence),
            BaselineEligibilityEvidence = EvidenceSummary(fuelPit.BaselineEligibilityEvidence),
            PitService = new
            {
                fuelPit.PitServiceStatus,
                fuelPit.PitServiceFlags,
                fuelPit.PitServiceFuelLiters,
                fuelPit.PitRepairLeftSeconds,
                fuelPit.PitOptRepairLeftSeconds,
                HasAnySignal = fuelPit.PitServiceStatus is not null
                    || fuelPit.PitServiceFlags is not null
                    || fuelPit.PitServiceFuelLiters is not null
                    || fuelPit.PitRepairLeftSeconds is not null
                    || fuelPit.PitOptRepairLeftSeconds is not null
            },
            TireSets = new
            {
                fuelPit.PlayerCarDryTireSetLimit,
                fuelPit.TireSetsUsed,
                fuelPit.TireSetsAvailable,
                fuelPit.RequestedTireCompound
            },
            FastRepair = new
            {
                fuelPit.FastRepairUsed,
                fuelPit.FastRepairAvailable,
                fuelPit.TeamFastRepairsUsed
            }
        };
    }

    private static object PitServiceModelSummary(LivePitServiceModel pit)
    {
        return new
        {
            pit.HasData,
            pit.Quality,
            pit.OnPitRoad,
            pit.PitstopActive,
            pit.PlayerCarInPitStall,
            pit.TeamOnPitRoad,
            pit.Status,
            pit.Flags,
            Request = new
            {
                pit.Request.LeftFrontTire,
                pit.Request.RightFrontTire,
                pit.Request.LeftRearTire,
                pit.Request.RightRearTire,
                pit.Request.Fuel,
                pit.Request.Tearoff,
                pit.Request.FastRepair,
                pit.Request.FuelLiters,
                pit.Request.RequestedTireCompoundIndex,
                pit.Request.RequestedTireCompoundLabel,
                pit.Request.RequestedTireCompoundShortLabel,
                pit.Request.RequestedTireCount,
                pit.Request.HasAnyRequest
            },
            Repair = new
            {
                pit.Repair.RequiredSeconds,
                pit.Repair.OptionalSeconds
            },
            Tires = new
            {
                pit.Tires.RequestedTireCount,
                pit.Tires.DryTireSetLimit,
                pit.Tires.TireSetsUsed,
                pit.Tires.TireSetsAvailable,
                pit.Tires.LeftTireSetsUsed,
                pit.Tires.RightTireSetsUsed,
                pit.Tires.FrontTireSetsUsed,
                pit.Tires.RearTireSetsUsed,
                pit.Tires.LeftTireSetsAvailable,
                pit.Tires.RightTireSetsAvailable,
                pit.Tires.FrontTireSetsAvailable,
                pit.Tires.RearTireSetsAvailable,
                pit.Tires.LeftFrontTiresUsed,
                pit.Tires.RightFrontTiresUsed,
                pit.Tires.LeftRearTiresUsed,
                pit.Tires.RightRearTiresUsed,
                pit.Tires.LeftFrontTiresAvailable,
                pit.Tires.RightFrontTiresAvailable,
                pit.Tires.LeftRearTiresAvailable,
                pit.Tires.RightRearTiresAvailable,
                pit.Tires.RequestedCompoundIndex,
                pit.Tires.RequestedCompoundLabel,
                pit.Tires.RequestedCompoundShortLabel,
                pit.Tires.CurrentCompoundIndex,
                pit.Tires.CurrentCompoundLabel,
                pit.Tires.CurrentCompoundShortLabel,
                pit.Tires.LeftFrontChangeRequested,
                pit.Tires.RightFrontChangeRequested,
                pit.Tires.LeftRearChangeRequested,
                pit.Tires.RightRearChangeRequested,
                pit.Tires.LeftFrontPressureKpa,
                pit.Tires.RightFrontPressureKpa,
                pit.Tires.LeftRearPressureKpa,
                pit.Tires.RightRearPressureKpa
            },
            FastRepair = new
            {
                pit.FastRepair.Selected,
                pit.FastRepair.LocalUsed,
                pit.FastRepair.LocalAvailable,
                pit.FastRepair.TeamUsed
            }
        };
    }

    private static object SpatialRadarModelSummary(LiveSpatialModel spatial)
    {
        return new
        {
            spatial.HasData,
            spatial.Quality,
            spatial.ReferenceCarIdx,
            spatial.ReferenceCarClass,
            spatial.CarLeftRight,
            spatial.SideStatus,
            spatial.HasCarLeft,
            spatial.HasCarRight,
            spatial.SideOverlapWindowSeconds,
            spatial.TrackLengthMeters,
            spatial.ReferenceLapDistPct,
            CarCount = spatial.Cars.Count,
            TimingPlacementCarCount = spatial.Cars.Count(car => car.RelativeSeconds is not null),
            MeterPlacementCarCount = spatial.Cars.Count(car => car.RelativeMeters is not null),
            MulticlassApproachCount = spatial.MulticlassApproaches.Count,
            spatial.StrongestMulticlassApproach,
            NearestAhead = SpatialCarSummary(spatial.NearestAhead),
            NearestBehind = SpatialCarSummary(spatial.NearestBehind),
            PlacementEvidenceCounts = spatial.Cars
                .GroupBy(car => EvidenceKey(car.PlacementEvidence))
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase)
        };
    }

    private static object TrackMapModelSummary(LiveTrackMapModel trackMap)
    {
        var highlighted = trackMap.Sectors
            .Where(sector => !string.Equals(sector.Highlight, LiveTrackSectorHighlights.None, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return new
        {
            trackMap.HasSectors,
            trackMap.HasLiveTiming,
            trackMap.Quality,
            SectorCount = trackMap.Sectors.Count,
            HighlightedSectorCount = highlighted.Length,
            HighlightCounts = trackMap.Sectors
                .GroupBy(sector => string.IsNullOrWhiteSpace(sector.Highlight) ? LiveTrackSectorHighlights.None : sector.Highlight)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase),
            BoundaryHighlightCounts = trackMap.Sectors
                .GroupBy(sector => string.IsNullOrWhiteSpace(sector.BoundaryHighlight) ? LiveTrackSectorHighlights.None : sector.BoundaryHighlight)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase)
        };
    }

    private static bool IsFlagCategoryEnabled(OverlaySettings overlay, FlagDisplayCategory category)
    {
        return category switch
        {
            FlagDisplayCategory.Green => overlay.GetBooleanOption(OverlayOptionKeys.FlagsShowGreen, defaultValue: true),
            FlagDisplayCategory.Blue => overlay.GetBooleanOption(OverlayOptionKeys.FlagsShowBlue, defaultValue: true),
            FlagDisplayCategory.Yellow => overlay.GetBooleanOption(OverlayOptionKeys.FlagsShowYellow, defaultValue: true),
            FlagDisplayCategory.Critical => overlay.GetBooleanOption(OverlayOptionKeys.FlagsShowCritical, defaultValue: true),
            FlagDisplayCategory.Finish => overlay.GetBooleanOption(OverlayOptionKeys.FlagsShowFinish, defaultValue: true),
            _ => true
        };
    }

    private static string FormatRawFlagsHex(int? flags)
    {
        return flags is { } value
            ? $"0x{unchecked((uint)value).ToString("X8", CultureInfo.InvariantCulture)}"
            : "--";
    }

    private static object? SpatialCarSummary(LiveSpatialCar? car)
    {
        return car is null
            ? null
            : new
            {
                car.CarIdx,
                car.Quality,
                PlacementEvidence = EvidenceSummary(car.PlacementEvidence),
                car.RelativeLaps,
                car.RelativeSeconds,
                car.RelativeMeters,
                car.OverallPosition,
                car.ClassPosition,
                car.CarClass,
                car.TrackSurface,
                car.OnPitRoad
            };
    }

    private static object? TireCompoundCarSummary(LiveCarTireCompound? car)
    {
        return car is null
            ? null
            : new
            {
                car.CarIdx,
                car.CompoundIndex,
                car.Label,
                car.ShortLabel,
                car.IsWet,
                car.IsPlayer,
                car.IsFocus,
                Evidence = EvidenceSummary(car.Evidence)
            };
    }

    private static object TireCornerConditionSummary(LiveTireCornerCondition corner)
    {
        return new
        {
            corner.Corner,
            corner.HasData,
            Wear = AcrossTreadSummary(corner.Wear),
            TemperatureC = AcrossTreadSummary(corner.TemperatureC),
            corner.ColdPressureKpa,
            corner.OdometerMeters,
            corner.PitServicePressureKpa,
            corner.BlackBoxColdPressurePa,
            corner.ChangeRequested
        };
    }

    private static object AcrossTreadSummary(LiveTireAcrossTreadValues values)
    {
        return new
        {
            values.HasData,
            values.Left,
            values.Middle,
            values.Right
        };
    }

    private static object EvidenceSummary(LiveSignalEvidence evidence)
    {
        return new
        {
            evidence.Source,
            evidence.Quality,
            evidence.IsUsable,
            evidence.MissingReason
        };
    }

    private static string EvidenceKey(LiveSignalEvidence evidence)
    {
        if (evidence.IsUsable)
        {
            return string.IsNullOrWhiteSpace(evidence.Source)
                ? "usable"
                : $"usable:{evidence.Source}";
        }

        return string.IsNullOrWhiteSpace(evidence.MissingReason)
            ? "unavailable"
            : $"missing:{evidence.MissingReason}";
    }

    private static object BuildCarFieldCoverage(IReadOnlyList<HistoricalCarProximity> cars)
    {
        return new
        {
            RowCount = cars.Count,
            SdkCarIdxSlotRowCount = cars
                .Where(HasSdkCarIdxSlot)
                .Select(car => car.CarIdx)
                .Distinct()
                .Count(),
            CompetitorLikeSignalRowCount = cars.Count(HasCompetitorLikeSignal),
            OfficialPositionValidCount = cars.Count(HasOfficialPosition),
            OfficialClassPositionValidCount = cars.Count(car => car.ClassPosition is > 0),
            LapDistanceProgressValidCount = cars.Count(HasProgress),
            EstimatedTimeNonNegativeCount = cars.Count(car => car.EstimatedTimeSeconds is >= 0d),
            EstimatedTimePositiveCount = cars.Count(car => car.EstimatedTimeSeconds is > 0d),
            F2TimeNonNegativeCount = cars.Count(car => car.F2TimeSeconds is >= 0d),
            F2TimePositiveCount = cars.Count(car => car.F2TimeSeconds is > 0d),
            CarClassValidCount = cars.Count(HasKnownCarClass),
            TrackSurfaceValidCount = cars.Count(car => car.TrackSurface is >= 0),
            SessionFlagsKnownCount = cars.Count(car => car.SessionFlags is not null),
            SessionFlagsActiveCount = cars.Count(car => car.SessionFlags is not null and not 0),
            OnPitRoadKnownCount = cars.Count(car => car.OnPitRoad is not null),
            FullOfficialTimingCount = cars.Count(car =>
                HasOfficialPosition(car)
                && car.ClassPosition is > 0
                && HasKnownCarClass(car)
                && car.F2TimeSeconds is >= 0d),
            FullProgressTimingCount = cars.Count(car =>
                HasProgress(car)
                && HasKnownCarClass(car)
                && car.EstimatedTimeSeconds is >= 0d)
        };
    }

    private static bool HasKnownCarClass(HistoricalCarProximity car)
    {
        return car.CarClass is >= 0;
    }

    private static bool HasSdkCarIdxSlot(HistoricalCarProximity car)
    {
        // Car rows are emitted only after the live/replay reader validates
        // them against the current schema. Their index need not fit the old
        // fixed 64-slot SDK table.
        return car.CarIdx >= 0;
    }

    private static bool HasCompetitorLikeSignal(HistoricalCarProximity car)
    {
        return HasOfficialPosition(car)
            || car.ClassPosition is > 0
            || HasProgress(car)
            || car.EstimatedTimeSeconds is > 0d
            || car.F2TimeSeconds is > 0d;
    }

    private static object? CarSnapshot(HistoricalCarProximity? car)
    {
        return car is null
            ? null
            : new
            {
                car.CarIdx,
                car.Position,
                car.ClassPosition,
                car.CarClass,
                car.LapCompleted,
                car.LapDistPct,
                car.EstimatedTimeSeconds,
                car.F2TimeSeconds,
                car.TrackSurface,
                car.SessionFlags,
                SessionFlagsHex = FormatRawFlagsHex(car.SessionFlags),
                car.OnPitRoad,
                HasOfficialPosition = HasOfficialPosition(car),
                HasProgress = HasProgress(car)
            };
    }

    private static bool HasOfficialPosition(HistoricalCarProximity car)
    {
        return car.Position is > 0;
    }

    private static bool HasProgress(HistoricalCarProximity car)
    {
        return car.LapCompleted >= 0
            && IsFinite(car.LapDistPct)
            && car.LapDistPct >= 0d;
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }

    private static bool IsFinite(double? value)
    {
        return value is { } finite && IsFinite(finite);
    }

    private static double? Max(double? current, double candidate)
    {
        return current is null || candidate > current.Value
            ? candidate
            : current;
    }

    private static string SessionStateLabel(int? sessionState)
    {
        return sessionState switch
        {
            1 => "get-in-car",
            2 => "warmup",
            3 => "parade-laps",
            4 => "racing",
            5 => "checkered",
            6 => "cool-down",
            _ => "unknown"
        };
    }

    private static IReadOnlyList<OverlayDefinition> ManagedOverlayDefinitions()
    {
        return
        [
            StandingsOverlayDefinition.Definition,
            FuelCalculatorOverlayDefinition.Definition,
            RelativeOverlayDefinition.Definition,
            TrackMapOverlayDefinition.Definition,
            StreamChatOverlayDefinition.Definition,
            GarageCoverOverlayDefinition.Definition,
            FlagsOverlayDefinition.Definition,
            SessionWeatherOverlayDefinition.Definition,
            PitServiceOverlayDefinition.Definition,
            InputStateOverlayDefinition.Definition,
            CarRadarOverlayDefinition.Definition,
            GapToLeaderOverlayDefinition.Definition
        ];
    }

    private static object UiFreezeWatch(AppPerformanceSnapshot performance)
    {
        static bool IsUiFreezeMetric(string id)
        {
            return id.StartsWith("overlay.settings.", StringComparison.OrdinalIgnoreCase)
                || id.StartsWith("overlay.manager.", StringComparison.OrdinalIgnoreCase)
                || id.StartsWith("overlay.flags.", StringComparison.OrdinalIgnoreCase)
                || id.StartsWith("overlay.timer.", StringComparison.OrdinalIgnoreCase)
                || id.Contains(".timer.", StringComparison.OrdinalIgnoreCase)
                || id.Contains(".window.", StringComparison.OrdinalIgnoreCase);
        }

        return new
        {
            performance.TimestampUtc,
            Metrics = performance.Metrics
                .Where(metric => IsUiFreezeMetric(metric.Id))
                .OrderBy(metric => metric.Id, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            Values = performance.OverlayUpdates
                .Where(value => IsUiFreezeMetric(value.Id))
                .OrderBy(value => value.Id, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            Windows = performance.OverlayWindows
        };
    }

    private static void AddRecentFiles(
        ZipArchive archive,
        string directory,
        string searchPattern,
        string entryDirectory,
        int maxFiles)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        var files = Directory
            .EnumerateFiles(directory, searchPattern)
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .Take(maxFiles);

        foreach (var file in files)
        {
            AddFileIfExists(archive, file.FullName, $"{entryDirectory}/{file.Name}");
        }
    }

    private static void AddRecentRecursiveFiles(
        ZipArchive archive,
        string rootDirectory,
        Func<FileInfo, bool> includeFile,
        string entryDirectory,
        int maxFiles)
    {
        if (!Directory.Exists(rootDirectory))
        {
            return;
        }

        var root = Path.GetFullPath(rootDirectory);
        var files = Directory
            .EnumerateFiles(root, "*.json", SearchOption.AllDirectories)
            .Select(path => new FileInfo(path))
            .Where(includeFile)
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .Take(maxFiles);

        foreach (var file in files)
        {
            var relativePath = Path.GetRelativePath(root, file.FullName);
            AddFileIfExists(
                archive,
                file.FullName,
                $"{entryDirectory}/{ToZipEntryPath(relativePath)}");
        }
    }

    private static void AddFileIfExists(ZipArchive archive, string sourcePath, string entryName)
    {
        if (!File.Exists(sourcePath))
        {
            return;
        }

        archive.CreateEntryFromFile(sourcePath, entryName, CompressionLevel.Fastest);
    }

    private static void AddBinaryEntry(ZipArchive archive, string entryName, byte[] content)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Fastest);
        using var stream = entry.Open();
        stream.Write(content, 0, content.Length);
    }

    private static void AddSanitizedSettingsIfExists(ZipArchive archive, string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            var node = JsonNode.Parse(File.ReadAllText(path));
            if (node is not null)
            {
                RedactStreamChatSecrets(node);
                AddTextEntry(archive, "settings/settings.json", node.ToJsonString(JsonOptions));
                return;
            }
        }
        catch
        {
            AddTextEntry(
                archive,
                "settings/settings-redacted.txt",
                "Settings could not be parsed; omitted to avoid copying private stream chat widget URLs.");
            return;
        }

        AddTextEntry(
            archive,
            "settings/settings-redacted.txt",
            "Settings were empty or invalid; omitted to avoid copying private stream chat widget URLs.");
    }

    private static object OverlayGeometryContractDiagnostics()
    {
        var current = OverlayGeometryContracts.Current;
        var currentJson = JsonSerializer.Serialize(current, JsonOptions);
        JsonNode? sourceContract = null;
        string? sourceJsonSha256 = null;
        string? sourceError = null;

        try
        {
            var sourceJson = OverlayGeometryContracts.BrowserJson();
            sourceContract = JsonNode.Parse(sourceJson);
            sourceJsonSha256 = Sha256Hex(sourceJson);
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            sourceError = exception.Message;
        }

        return new
        {
            EvidenceVersion = 1,
            Source = "overlay-geometry-contract",
            SourceAsset = "src/TmrOverlay.App/Overlays/BrowserSources/Assets/contracts/overlay-geometry.json",
            RuntimeContractSha256 = Sha256Hex(currentJson),
            SourceJsonSha256 = sourceJsonSha256,
            SourceError = sourceError,
            Current = current,
            SourceContract = sourceContract
        };
    }

    private static string Sha256Hex(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }

    private static void AddSharedContractFiles(ZipArchive archive)
    {
        var contractPath = SharedOverlayContract.LoadStatus.Path ?? SharedOverlayContract.TryFindDefaultContractPath();
        if (contractPath is not null)
        {
            AddFileIfExists(archive, contractPath, SharedOverlayContract.DefaultContractRelativePath);
        }

        var schemaPath = SharedOverlayContract.TryFindDefaultSchemaPath();
        if (schemaPath is not null)
        {
            AddFileIfExists(archive, schemaPath, SharedOverlayContract.DefaultSchemaRelativePath);
        }
    }

    private static void RedactStreamChatSecrets(JsonNode node)
    {
        if (node["overlays"] is not JsonArray overlays)
        {
            return;
        }

        foreach (var overlay in overlays.OfType<JsonObject>())
        {
            if (!string.Equals((string?)overlay["id"], "stream-chat", StringComparison.OrdinalIgnoreCase)
                || overlay["options"] is not JsonObject options
                || !options.ContainsKey(OverlayOptionKeys.StreamChatStreamlabsUrl))
            {
                continue;
            }

            options[OverlayOptionKeys.StreamChatStreamlabsUrl] = "<redacted>";
        }
    }

    private static void AddTextEntry(ZipArchive archive, string entryName, string content)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Fastest);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream);
        writer.Write(content);
    }

    private static string ToZipEntryPath(string relativePath)
    {
        return relativePath.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
    }
}

internal static class DiagnosticsBundleSources
{
    public const string Manual = "manual";
    public const string SessionFinalization = "session_finalization";
}

internal sealed record DiagnosticsBundleStatus(
    string? LastBundlePath,
    DateTimeOffset? LastBundleCreatedAtUtc,
    string? LastBundleSource,
    string? LastError,
    DateTimeOffset? LastErrorAtUtc,
    string? LastErrorSource);

internal sealed record DiagnosticsBundleIdentity(
    string CarName,
    string TrackName,
    string CarSlug,
    string TrackSlug,
    string Source);

internal sealed record SessionInfoSetupSignal(
    string Path,
    string Key,
    string Value);

internal sealed record UpdateEventDiagnosticsSnapshot(
    string EventsRoot,
    bool Exists,
    int RecentEventFilesScanned,
    int MalformedEventLineCount,
    int UpdateCheckStartedCount,
    int UpdateCheckSucceededCount,
    int UpdateCheckFailedCount,
    DateTimeOffset? LatestUpdateCheckFailureAtUtc,
    string? LatestUpdateCheckFailureSource,
    string? LatestUpdateCheckFailureError,
    DateTimeOffset? LatestUpdateCheckSuccessAtUtc,
    string? LatestUpdateCheckSuccessSource,
    string? LatestUpdateCheckSuccessResult,
    IReadOnlyDictionary<string, int> UpdateCheckFailureSourceCounts,
    IReadOnlyDictionary<string, int> UpdateCheckFailureErrorCounts,
    UpdateFailureSummaryDiagnostics UpdateFailureSummary,
    IReadOnlyList<UpdateEventFileDiagnostics> Files);

internal sealed record UpdateFailureSummaryDiagnostics(
    string Classification,
    int FailureCount,
    int SuccessCount,
    int TransientFailureCount,
    int UnrecoveredFailureCount,
    DateTimeOffset? LatestFailureAtUtc,
    string? LatestFailureSource,
    string? LatestFailureError,
    DateTimeOffset? LatestTransientFailureAtUtc,
    string? LatestTransientFailureSource,
    string? LatestTransientFailureError,
    DateTimeOffset? LatestRecoveryAtUtc,
    string? LatestRecoverySource,
    string? LatestRecoveryResult,
    string Interpretation);

internal sealed record UpdateEventFileDiagnostics(
    string FileName,
    int UpdateCheckStartedCount,
    int UpdateCheckSucceededCount,
    int UpdateCheckFailedCount,
    int MalformedEventLineCount);

internal sealed record UpdateApplyShutdownDiagnosticsSnapshot(
    string Classification,
    string Interpretation,
    string ReleaseStatus,
    bool ReleaseOperationInProgress,
    DateTimeOffset? ReleaseLastApplyStartedAtUtc,
    bool RuntimeStateExists,
    DateTimeOffset? RuntimeStartedAtUtc,
    bool? RuntimeStoppedCleanly,
    DateTimeOffset? RuntimeStoppedAtUtc,
    DateTimeOffset? RuntimeShutdownStartedAtUtc,
    DateTimeOffset? RuntimeShutdownCompletedAtUtc,
    string? RuntimeShutdownPhase,
    int UpdateApplyStartedCount,
    int UpdateApplyHandoffRequestedCount,
    int UpdateApplyHandoffReturnedCount,
    int UpdateApplyFailedCount,
    int ApplicationExitRequestedForUpdateCount,
    int HostStopStartedCount,
    int HostStopCompletedCount,
    int AppStoppedCount,
    bool HostStopCompletedAfterApplyStart,
    bool AppStoppedAfterApplyStart,
    DateTimeOffset? LatestUpdateApplyStartedAtUtc,
    DateTimeOffset? LatestUpdateApplyHandoffRequestedAtUtc,
    DateTimeOffset? LatestUpdateApplyHandoffReturnedAtUtc,
    DateTimeOffset? LatestApplicationExitRequestedForUpdateAtUtc,
    DateTimeOffset? LatestHostStopStartedAtUtc,
    DateTimeOffset? LatestHostStopCompletedAtUtc,
    DateTimeOffset? LatestAppStoppedAtUtc);

internal sealed record AppEventDiagnostics(
    string FileName,
    DateTimeOffset? TimestampUtc,
    string Name,
    JsonObject? Properties);

internal sealed record UpdateCheckEvent(
    string FileName,
    DateTimeOffset? TimestampUtc,
    string? Source,
    string? Result,
    string? Error)
{
    public static UpdateCheckEvent From(
        string fileName,
        DateTimeOffset? timestampUtc,
        JsonObject? properties)
    {
        return new UpdateCheckEvent(
            FileName: fileName,
            TimestampUtc: timestampUtc,
            Source: (string?)properties?["source"],
            Result: (string?)properties?["result"],
            Error: (string?)properties?["error"]);
    }
}

internal sealed record TrackMapRuntimeLookupDiagnostics(
    double NativeReloadIntervalSeconds,
    double BrowserModelFactoryReloadIntervalSeconds,
    string LocalhostTrackMapRoute,
    string BrowserOverlayRoute,
    int BrowserSourceRefreshIntervalMilliseconds,
    string ReloadPolicy);

internal sealed record TrackMapGenerationEventDiagnosticsSnapshot(
    string EventsRoot,
    bool Exists,
    int RecentEventFilesScanned,
    int MalformedEventLineCount,
    int GeneratedCount,
    int SkippedCount,
    int RejectedCount,
    int FailedCount,
    DateTimeOffset? LatestGeneratedAtUtc,
    string? LatestGeneratedCaptureId,
    string? LatestGeneratedMapPath,
    IReadOnlyDictionary<string, int> EventNameCounts,
    IReadOnlyDictionary<string, int> ReasonCounts,
    IReadOnlyList<TrackMapGenerationEventDiagnostics> RecentEvents);

internal sealed record TrackMapGenerationEventDiagnostics(
    string FileName,
    DateTimeOffset? TimestampUtc,
    string Name,
    string? CaptureId,
    string? SourcePath,
    string? SourceFileName,
    string? MapPath,
    string? MapFileName,
    string? Reason,
    string? Confidence,
    int? CompleteLapCount,
    int? MissingBinCount,
    string? Error)
{
    public static TrackMapGenerationEventDiagnostics From(
        string fileName,
        DateTimeOffset? timestampUtc,
        string name,
        JsonObject? properties)
    {
        var sourcePath = (string?)properties?["sourcePath"];
        var mapPath = (string?)properties?["mapPath"];
        return new TrackMapGenerationEventDiagnostics(
            FileName: fileName,
            TimestampUtc: timestampUtc,
            Name: name,
            CaptureId: (string?)properties?["captureId"],
            SourcePath: sourcePath,
            SourceFileName: string.IsNullOrWhiteSpace(sourcePath) ? null : Path.GetFileName(sourcePath),
            MapPath: mapPath,
            MapFileName: string.IsNullOrWhiteSpace(mapPath) ? null : Path.GetFileName(mapPath),
            Reason: FirstNonEmpty((string?)properties?["reason"], (string?)properties?["reasons"], (string?)properties?["error"]),
            Confidence: (string?)properties?["confidence"],
            CompleteLapCount: ParseInt((string?)properties?["completeLapCount"]),
            MissingBinCount: ParseInt((string?)properties?["missingBinCount"]),
            Error: (string?)properties?["error"]);
    }

    private static int? ParseInt(string? value)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
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
}

internal sealed record LapDeltaSignalDiagnostic(
    string Key,
    double? Seconds,
    double? Rate,
    bool? Ok)
{
    public bool IsUsable => Ok == true && Seconds is { } value && !double.IsNaN(value) && !double.IsInfinity(value);
}

internal sealed record LocalhostOverlayPageModelDiagnosticsSnapshot(
    string Id,
    string Title,
    string HtmlRoute,
    string ModelApiPath,
    bool RequiresTelemetry,
    bool RenderWhenTelemetryUnavailable,
    bool FadeWhenTelemetryUnavailable,
    int RefreshIntervalMilliseconds,
    long HtmlRouteRequestCount,
    long HtmlRouteSuccessCount,
    long ModelApiRequestCount,
    long ModelApiSuccessCount,
    long PageLoadedEventCount,
    long ModelRenderEventCount,
    long ModelHiddenEventCount,
    long ModelNullEventCount,
    long ModelErrorEventCount,
    IReadOnlyDictionary<string, long> SourceUrlCounts,
    IReadOnlyDictionary<string, long> SourceUrlClientCounts,
    IReadOnlyDictionary<string, long> PageEventSourceUrlCounts,
    IReadOnlyDictionary<string, long> PageEventSourceUrlClientCounts,
    IReadOnlyDictionary<string, long> PageEventClientCounts,
    IReadOnlyDictionary<string, long> PageEventClientIdCounts,
    IReadOnlyList<LocalhostOverlayPageEventSample> RecentPageEvents,
    LocalhostOverlayModelDiagnosticsSnapshot Current,
    LocalhostOverlayModelDiagnosticsSnapshot? LastActive);

internal sealed record LocalhostOverlayModelDiagnosticsSnapshot(
    string SnapshotSource,
    bool MatchesLocalhostEndpoint,
    string BuildStatus,
    string RenderDecision,
    DateTimeOffset GeneratedAtUtc,
    bool ShouldRender,
    string? Status,
    string? Source,
    string? BodyKind,
    double? RootOpacity,
    object? Content,
    string? Error,
    string? ErrorMessage);

internal sealed record LapDeltaQualityDiagnostics(
    bool EvidenceAvailable,
    int? ObservedFrames,
    int? FramesWithAnyValue,
    int? FramesWithAnyUsableValue,
    double? MaxAbsDeltaSeconds,
    IReadOnlyDictionary<string, int> ValueFrameCounts,
    IReadOnlyDictionary<string, int> UsableFrameCounts,
    string Classification,
    bool ValuesPresentWithoutUsableQuality,
    bool AllObservedValuesZero,
    string Interpretation);

internal sealed record LapProfileReadinessDiagnostics(
    bool EvidenceAvailable,
    int? ObservedFrames,
    int? FramesWithTimingRows,
    int? FramesWithScoringRows,
    int? FramesWithAnyRows,
    int? FramesWithAnyBestLap,
    int? FramesWithAnyLastLap,
    int? FramesWithBestAndLastLap,
    int? FramesWithRecentPersonalBest,
    int? FramesWithClassFastestBestLap,
    int? FramesWithClassFastestLastLap,
    int? MaxRows,
    int? MaxRowsWithBestAndLastLap,
    int? MaxRowsWithRecentPersonalBest,
    int? MaxRowsWithClassFastestBestLap,
    int? MaxRowsWithClassFastestLastLap,
    IReadOnlyDictionary<string, int> SourceFrameCounts,
    IReadOnlyDictionary<string, int> SourceRowCounts,
    string Classification,
    bool BestVsLastReady,
    bool RecentPersonalBestEvidenceAvailable,
    bool ClassFastestEvidenceAvailable,
    string Interpretation);

internal sealed record LapProfileRowDiagnostic(
    int CarIdx,
    int? CarClass,
    string? CarClassName,
    string? CarClassColorHex,
    double? BestLapTimeSeconds,
    double? LastLapTimeSeconds);

internal sealed record LapProfileFrameReadinessDiagnostic(
    int RowsWithBestLap,
    int RowsWithLastLap,
    int RowsWithBestAndLastLap,
    int RowsWithRecentPersonalBest,
    int RowsWithClassFastestBestLap,
    int RowsWithClassFastestLastLap);

internal sealed record FuelV2CaptureEvidenceDiagnostics(
    int? FormatVersion,
    int? FrameCount,
    int? SampledFrameCount,
    int? AcceptedLapBurnWindowCount,
    int? RejectedLapBurnWindowCount,
    int? PitWindowCount,
    int? TeamStintCount,
    bool? SyntheticReplaySuitable,
    JsonArray? SyntheticReplayReasons,
    JsonNode? SessionFrameCounts,
    JsonNode? ContextFlagCounts,
    JsonNode? LapBudgetSourceCounts,
    JsonNode? LapBudgetMissingSignalCounts)
{
    public static FuelV2CaptureEvidenceDiagnostics Empty { get; } = new(
        FormatVersion: null,
        FrameCount: null,
        SampledFrameCount: null,
        AcceptedLapBurnWindowCount: null,
        RejectedLapBurnWindowCount: null,
        PitWindowCount: null,
        TeamStintCount: null,
        SyntheticReplaySuitable: null,
        SyntheticReplayReasons: null,
        SessionFrameCounts: null,
        ContextFlagCounts: null,
        LapBudgetSourceCounts: null,
        LapBudgetMissingSignalCounts: null);
}

internal sealed record FuelV2CapturePathInventory(
    int TotalCount,
    IReadOnlyList<string> RecentPaths)
{
    public static FuelV2CapturePathInventory Empty { get; } = new(0, []);
}

internal sealed record IbtAnalysisDiagnosticsSnapshot(
    bool Enabled,
    bool TelemetryLoggingEnabled,
    string TelemetryRoot,
    int MaxCandidateAgeMinutes,
    long MaxCandidateBytes,
    int MaxAnalysisMilliseconds,
    int MaxSampledRecords,
    int MinStableAgeSeconds,
    int MaxIRacingExitWaitSeconds,
    int MaxCandidateFiles,
    bool CopyIbtIntoCaptureDirectory,
    string OutputDirectoryName,
    LatestCaptureIbtAnalysisDiagnostics LatestCapture);

internal sealed record LatestCaptureIbtAnalysisDiagnostics(
    string? CaptureDirectory,
    string? StatusPath,
    bool StatusExists,
    string? Status,
    string? Reason,
    string? SourcePath,
    string? CandidateSelectedPath,
    string? SessionMatchStatus,
    string? SessionMatchReason,
    IReadOnlyList<string> SessionMatchMismatches,
    string? StatusReadError);
