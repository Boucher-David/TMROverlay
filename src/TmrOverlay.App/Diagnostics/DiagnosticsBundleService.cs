using System.IO.Compression;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using TmrOverlay.Core.AppInfo;
using TmrOverlay.App.Installation;
using TmrOverlay.App.Localhost;
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
    private const int MaxRecentEdgeCaseFiles = 20;
    private const int MaxLatestCaptureIbtAnalysisFiles = 12;
    private const int MaxRecentModelParityFiles = 10;
    private const int MaxRecentOverlayDiagnosticsFiles = 10;
    private const int MaxRecentTrackMapReports = 10;
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
    private readonly IbtAnalysisOptions _ibtAnalysisOptions;
    private readonly TelemetryCaptureState _captureState;
    private readonly LocalhostOverlayState _localhostOverlayState;
    private readonly TrackMapStore _trackMapStore;
    private readonly AppSettingsStore _settingsStore;
    private readonly ILiveTelemetrySource _liveTelemetrySource;
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
        SessionPreviewState sessionPreviewState,
        AppPerformanceState performanceState,
        AppPerformanceSnapshotRecorder performanceRecorder,
        LiveOverlayWindowCaptureStore liveOverlayWindowCaptureStore,
        ForegroundWindowTracker foregroundWindowTracker,
        ReleaseUpdateService releaseUpdates,
        StreamChatOverlaySource streamChatSource,
        ILogger<DiagnosticsBundleService> logger)
    {
        _storageOptions = storageOptions;
        _liveModelParityOptions = liveModelParityOptions;
        _liveOverlayDiagnosticsOptions = liveOverlayDiagnosticsOptions;
        _ibtAnalysisOptions = ibtAnalysisOptions;
        _captureState = captureState;
        _localhostOverlayState = localhostOverlayState;
        _trackMapStore = trackMapStore;
        _settingsStore = settingsStore;
        _liveTelemetrySource = liveTelemetrySource;
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
                AddTextEntry(archive, "metadata/localhost-overlays.json", JsonSerializer.Serialize(_localhostOverlayState.Snapshot(), JsonOptions));
                AddTextEntry(archive, "metadata/browser-overlays.json", JsonSerializer.Serialize(BrowserOverlayDiagnostics(), JsonOptions));
                AddTextEntry(archive, "metadata/session-preview.json", JsonSerializer.Serialize(_sessionPreviewState.Snapshot(), JsonOptions));
                AddTextEntry(archive, "metadata/shared-settings-contract.json", JsonSerializer.Serialize(SharedOverlayContract.DiagnosticsSnapshot(), JsonOptions));
                AddTextEntry(archive, "metadata/release-updates.json", JsonSerializer.Serialize(_releaseUpdates.Snapshot(), JsonOptions));
                AddTextEntry(archive, "metadata/installer-cleanup.json", JsonSerializer.Serialize(InstallerCleanup.LegacyInstallerCleanupSnapshot(), JsonOptions));
                AddTextEntry(archive, "metadata/evidence-quality.json", JsonSerializer.Serialize(EvidenceQualityDiagnostics(), JsonOptions));
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

            var liveOverlayWindowsStarted = System.Diagnostics.Stopwatch.GetTimestamp();
            var liveOverlayWindowsSucceeded = false;
            try
            {
                AddLiveOverlayWindows(archive);
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

    private object EvidenceQualityDiagnostics()
    {
        var now = DateTimeOffset.UtcNow;
        var liveSnapshot = _liveTelemetrySource.Snapshot();
        var lastActiveSnapshot = _liveTelemetrySource.LastActiveSnapshot();
        var localhost = _localhostOverlayState.Snapshot();
        var liveOverlays = _liveOverlayWindowCaptureStore.Snapshot();
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
                liveOverlays.EvidenceWarnings
            },
            LatestCapture = new
            {
                CaptureDirectory = latestCapture,
                Exists = !string.IsNullOrWhiteSpace(latestCapture) && Directory.Exists(latestCapture),
                CaptureManifestExists = !string.IsNullOrWhiteSpace(latestCapture) && File.Exists(Path.Combine(latestCapture, "capture-manifest.json")),
                CaptureSynthesisExists = !string.IsNullOrWhiteSpace(latestCapture) && File.Exists(Path.Combine(latestCapture, "capture-synthesis.json")),
                LiveOverlayDiagnosticsExists = !string.IsNullOrWhiteSpace(latestCapture) && File.Exists(Path.Combine(latestCapture, _liveOverlayDiagnosticsOptions.OutputFileName)),
                LiveModelParityExists = !string.IsNullOrWhiteSpace(latestCapture) && File.Exists(Path.Combine(latestCapture, _liveModelParityOptions.OutputFileName)),
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
            Synthesis = new
            {
                Path = synthesisPath,
                Exists = File.Exists(synthesisPath),
                TotalFrameRecords = (int?)synthesis?["frameScan"]?["totalFrameRecords"],
                SampledFrameCount = (int?)synthesis?["frameScan"]?["sampledFrameCount"],
                ValidDistanceLaps = (double?)synthesis?["session"]?["metrics"]?["validDistanceLaps"],
                CompletedValidLaps = (int?)synthesis?["session"]?["metrics"]?["completedValidLaps"]
            },
            LiveOverlayDiagnostics = new
            {
                Path = liveOverlayDiagnosticsPath,
                Exists = File.Exists(liveOverlayDiagnosticsPath),
                FrameCount = (int?)liveOverlayDiagnostics?["totals"]?["frameCount"],
                PitWindowCount = (int?)liveOverlayDiagnostics?["fuel"]?["pitWindowCount"],
                PitWindowsWithFuelIncrease = (int?)liveOverlayDiagnostics?["fuel"]?["pitWindowsWithFuelIncrease"],
                PitWindowsWithBlackFlag = (int?)liveOverlayDiagnostics?["fuel"]?["pitWindowsWithBlackFlag"],
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
        string? statusReadError = null;
        if (!string.IsNullOrWhiteSpace(statusPath) && File.Exists(statusPath))
        {
            try
            {
                var node = JsonNode.Parse(File.ReadAllText(statusPath));
                status = (string?)node?["status"];
                reason = (string?)node?["reason"];
                sourcePath = (string?)node?["sourcePath"];
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
                StatusReadError: statusReadError));
    }

    private string IbtAnalysisOutputDirectoryName()
    {
        return string.IsNullOrWhiteSpace(_ibtAnalysisOptions.OutputDirectoryName)
            ? "ibt-analysis"
            : _ibtAnalysisOptions.OutputDirectoryName;
    }

    private TrackMapStoreDiagnosticsSnapshot TrackMapDiagnostics()
    {
        return _trackMapStore.DiagnosticsSnapshot(CurrentTrackIdentity(), TrackMapUserMapLookupEnabled());
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

    private void AddLiveOverlayWindows(ZipArchive archive)
    {
        AddTextEntry(
            archive,
            "live-overlays/manifest.json",
            JsonSerializer.Serialize(_liveOverlayWindowCaptureStore.Snapshot(), JsonOptions));
        foreach (var file in _liveOverlayWindowCaptureStore.CaptureFiles())
        {
            AddFileIfExists(archive, file.SourcePath, file.EntryName);
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
                    var sessionAllowed = OverlaySessionAllowedForDiagnostics(definition, availability.SessionKind);
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
                    CarClass = "CarIdxClass > 0",
                    TrackSurface = "CarIdxTrackSurface >= 0",
                    SentinelNote = "-1 and 0 are not valid official positions; gridding/startup/replay contexts can still have class and progress before official order is populated."
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
            RaceProjectionModel = RaceProjectionModelSummary(lastActiveSnapshot.Models.RaceProjection),
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
            OfficialPositionValidCount = cars.Count(HasOfficialPosition),
            OfficialClassPositionValidCount = cars.Count(car => car.ClassPosition is > 0),
            LapDistanceProgressValidCount = cars.Count(HasProgress),
            EstimatedTimeNonNegativeCount = cars.Count(car => car.EstimatedTimeSeconds is >= 0d),
            EstimatedTimePositiveCount = cars.Count(car => car.EstimatedTimeSeconds is > 0d),
            F2TimeNonNegativeCount = cars.Count(car => car.F2TimeSeconds is >= 0d),
            F2TimePositiveCount = cars.Count(car => car.F2TimeSeconds is > 0d),
            CarClassValidCount = cars.Count(car => car.CarClass is > 0),
            TrackSurfaceValidCount = cars.Count(car => car.TrackSurface is >= 0),
            OnPitRoadKnownCount = cars.Count(car => car.OnPitRoad is not null),
            FullOfficialTimingCount = cars.Count(car =>
                HasOfficialPosition(car)
                && car.ClassPosition is > 0
                && car.CarClass is > 0
                && car.F2TimeSeconds is >= 0d),
            FullProgressTimingCount = cars.Count(car =>
                HasProgress(car)
                && car.CarClass is > 0
                && car.EstimatedTimeSeconds is >= 0d)
        };
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

    private static bool OverlaySessionAllowedForDiagnostics(
        OverlayDefinition definition,
        OverlaySessionKind? sessionKind)
    {
        if (string.Equals(definition.Id, FlagsOverlayDefinition.Definition.Id, StringComparison.Ordinal)
            && sessionKind is null)
        {
            return false;
        }

        if (string.Equals(definition.Id, GapToLeaderOverlayDefinition.Definition.Id, StringComparison.Ordinal))
        {
            return OverlayAvailabilityEvaluator.NormalizeSessionKind(sessionKind) == OverlaySessionKind.Race;
        }

        return true;
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
    string? StatusReadError);
