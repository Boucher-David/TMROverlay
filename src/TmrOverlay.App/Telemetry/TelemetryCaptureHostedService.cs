using System.Diagnostics;
using irsdkSharp;
using irsdkSharp.Enums;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TmrOverlay.App.Analysis;
using TmrOverlay.App.Diagnostics;
using TmrOverlay.App.Events;
using TmrOverlay.App.History;
using TmrOverlay.App.Performance;
using TmrOverlay.App.Settings;
using TmrOverlay.App.TrackMaps;
using TmrOverlay.Core.Overlays;
using TmrOverlay.Core.History;
using TmrOverlay.Core.Telemetry.EdgeCases;
using TmrOverlay.Core.Telemetry.Live;

namespace TmrOverlay.App.Telemetry;

internal sealed class TelemetryCaptureHostedService : IHostedService
{
    private readonly ILogger<TelemetryCaptureHostedService> _logger;
    private readonly TelemetryCaptureOptions _options;
    private readonly IbtAnalysisOptions _ibtOptions;
    private readonly IbtAnalysisService _ibtAnalysis;
    private readonly IbtTrackMapBuilder _trackMapBuilder;
    private readonly TrackMapStore _trackMapStore;
    private readonly AppSettingsStore _settingsStore;
    private readonly AppEventRecorder _events;
    private readonly SessionHistoryStore _sessionHistoryStore;
    private readonly PostRaceAnalysisPipeline _postRaceAnalysisPipeline;
    private readonly DiagnosticsBundleService _diagnosticsBundleService;
    private readonly ILiveTelemetrySink _liveTelemetrySink;
    private readonly ILiveTelemetrySource _liveTelemetrySource;
    private readonly TelemetryCaptureState _state;
    private readonly AppPerformanceState _performance;
    private readonly TelemetryEdgeCaseRecorder _edgeCaseRecorder;
    private readonly LiveModelParityRecorder _liveModelParityRecorder;
    private readonly LiveOverlayDiagnosticsRecorder _liveOverlayDiagnosticsRecorder;
    private readonly object _sync = new();
    private readonly SemaphoreSlim _postSessionArtifactSemaphore = new(1, 1);
    private readonly CancellationTokenSource _startupArtifactCancellation = new();
    private IRacingSDK? _sdk;
    private TelemetryCaptureSession? _activeCapture;
    private HistoricalSessionAccumulator? _activeHistory;
    private Task _finalizerTask = Task.CompletedTask;
    private Task _startupArtifactTask = Task.CompletedTask;
    private string? _activeSourceId;
    private DateTimeOffset? _activeStartedAtUtc;
    private IReadOnlyList<string> _activeRawWatchVariableNames = [];
    private IReadOnlyDictionary<string, string> _activeRawWatchVariableGroups = new Dictionary<string, string>();
    private int _sessionInfoSnapshotCount;
    private int _frameIndex;
    private int _lastSessionInfoUpdate = -1;

    public TelemetryCaptureHostedService(
        ILogger<TelemetryCaptureHostedService> logger,
        TelemetryCaptureOptions options,
        IbtAnalysisOptions ibtOptions,
        IbtAnalysisService ibtAnalysis,
        IbtTrackMapBuilder trackMapBuilder,
        TrackMapStore trackMapStore,
        AppSettingsStore settingsStore,
        AppEventRecorder events,
        SessionHistoryStore sessionHistoryStore,
        PostRaceAnalysisPipeline postRaceAnalysisPipeline,
        DiagnosticsBundleService diagnosticsBundleService,
        ILiveTelemetrySink liveTelemetrySink,
        LiveTelemetryStore liveTelemetrySource,
        TelemetryCaptureState state,
        AppPerformanceState performance,
        TelemetryEdgeCaseRecorder edgeCaseRecorder,
        LiveModelParityRecorder liveModelParityRecorder,
        LiveOverlayDiagnosticsRecorder liveOverlayDiagnosticsRecorder)
    {
        _logger = logger;
        _options = options;
        _ibtOptions = ibtOptions;
        _ibtAnalysis = ibtAnalysis;
        _trackMapBuilder = trackMapBuilder;
        _trackMapStore = trackMapStore;
        _settingsStore = settingsStore;
        _events = events;
        _sessionHistoryStore = sessionHistoryStore;
        _postRaceAnalysisPipeline = postRaceAnalysisPipeline;
        _diagnosticsBundleService = diagnosticsBundleService;
        _liveTelemetrySink = liveTelemetrySink;
        _liveTelemetrySource = liveTelemetrySource;
        _state = state;
        _performance = performance;
        _edgeCaseRecorder = edgeCaseRecorder;
        _liveModelParityRecorder = liveModelParityRecorder;
        _liveOverlayDiagnosticsRecorder = liveOverlayDiagnosticsRecorder;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _state.SetCaptureRoot(_options.ResolvedCaptureRoot);
        _state.SetRawCaptureEnabled(_options.RawCaptureEnabled);

        if (_options.RawCaptureEnabled)
        {
            try
            {
                Directory.CreateDirectory(_options.ResolvedCaptureRoot);
            }
            catch (Exception exception)
            {
                _state.RecordError($"Cannot create capture root: {exception.Message}");
                _logger.LogError(exception, "Failed to create telemetry capture root {CaptureRoot}.", _options.ResolvedCaptureRoot);
                throw;
            }
        }

        _sdk = new IRacingSDK();
        _sdk.OnConnected += HandleConnected;
        _sdk.OnDisconnected += HandleDisconnected;
        _sdk.OnDataChanged += HandleDataChanged;

        _logger.LogInformation(
            "Telemetry collection service started. Raw capture enabled: {RawCaptureEnabled}. IBT analysis enabled: {IbtAnalysisEnabled}. IBT telemetry logging enabled: {IbtTelemetryLoggingEnabled}. Capture root: {CaptureRoot}. IBT root: {IbtTelemetryRoot}.",
            _options.RawCaptureEnabled,
            _ibtOptions.Enabled,
            _ibtOptions.TelemetryLoggingEnabled,
            _options.ResolvedCaptureRoot,
            _ibtOptions.TelemetryRoot);
        _startupArtifactTask = Task.Run(
            () => RecoverPendingPostSessionArtifactsAsync(_startupArtifactCancellation.Token),
            CancellationToken.None);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_sdk is not null)
        {
            _sdk.OnConnected -= HandleConnected;
            _sdk.OnDisconnected -= HandleDisconnected;
            _sdk.OnDataChanged -= HandleDataChanged;
        }

        TelemetryCaptureSession? captureToFinalize;
        HistoricalSessionAccumulator? historyToFinalize;
        var finalization = CaptureFinalizationContext.Empty;
        lock (_sync)
        {
            captureToFinalize = _activeCapture;
            historyToFinalize = _activeHistory;
            finalization = BuildFinalizationContext(captureToFinalize);
            _activeCapture = null;
            _activeHistory = null;
            _activeSourceId = null;
            _activeStartedAtUtc = null;
            _activeRawWatchVariableNames = [];
            _activeRawWatchVariableGroups = new Dictionary<string, string>();
            _sessionInfoSnapshotCount = 0;
            _lastSessionInfoUpdate = -1;
        }

        _startupArtifactCancellation.Cancel();
        try
        {
            await _startupArtifactTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || _startupArtifactCancellation.IsCancellationRequested)
        {
            // Startup artifact recovery is best-effort and must not block app shutdown.
        }

        if (captureToFinalize is not null || historyToFinalize is not null)
        {
            TryStopIbtTelemetryLogging(_sdk, captureToFinalize, "app_stopped");
            await FinalizeCollectionAsync(captureToFinalize, historyToFinalize, finalization).ConfigureAwait(false);
        }

        try
        {
            await _finalizerTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _state.RecordError($"Capture finalizer failed: {exception.Message}");
            throw;
        }
    }

    private void HandleConnected()
    {
        _state.MarkConnected();
        _liveTelemetrySink.MarkConnected();
        _events.Record("iracing_connected");
        _logger.LogInformation("Connected to iRacing.");
    }

    private void HandleDisconnected()
    {
        _logger.LogInformation("Disconnected from iRacing.");
        _events.Record("iracing_disconnected");

        _state.MarkDisconnected();
        _liveTelemetrySink.MarkDisconnected();

        TelemetryCaptureSession? captureToFinalize;
        HistoricalSessionAccumulator? historyToFinalize;
        var finalization = CaptureFinalizationContext.Empty;
        lock (_sync)
        {
            captureToFinalize = _activeCapture;
            historyToFinalize = _activeHistory;
            finalization = BuildFinalizationContext(captureToFinalize);
            _activeCapture = null;
            _activeHistory = null;
            _activeSourceId = null;
            _activeStartedAtUtc = null;
            _activeRawWatchVariableNames = [];
            _activeRawWatchVariableGroups = new Dictionary<string, string>();
            _sessionInfoSnapshotCount = 0;
            _lastSessionInfoUpdate = -1;
            _frameIndex = 0;
        }

        if (captureToFinalize is not null || historyToFinalize is not null)
        {
            TryStopIbtTelemetryLogging(_sdk, captureToFinalize, "iracing_disconnected");
            _finalizerTask = FinalizeCollectionAsync(captureToFinalize, historyToFinalize, finalization);
        }
    }

    private void HandleDataChanged()
    {
        var dataChangedStarted = Stopwatch.GetTimestamp();
        var succeeded = false;
        var sdk = _sdk;

        if (sdk is null || !sdk.IsConnected() || sdk.Header is null)
        {
            return;
        }

        try
        {
            var capturedAtUtc = DateTimeOffset.UtcNow;
            TelemetryCaptureSession? capture;
            var collectionStarted = Stopwatch.GetTimestamp();
            var collectionSucceeded = false;
            try
            {
                capture = GetOrCreateCollection(sdk, capturedAtUtc);
                collectionSucceeded = true;
            }
            finally
            {
                _performance.RecordOperation(
                    AppPerformanceMetricIds.TelemetryDataChangedGetCollection,
                    collectionStarted,
                    collectionSucceeded);
            }

            var sessionInfoUpdate = sdk.Header.SessionInfoUpdate;

            bool latestSessionInfoUpdate;
            var sessionVersionStarted = Stopwatch.GetTimestamp();
            var sessionVersionSucceeded = false;
            try
            {
                latestSessionInfoUpdate = UpdateSessionInfoVersion(sessionInfoUpdate);
                sessionVersionSucceeded = true;
            }
            finally
            {
                _performance.RecordOperation(
                    AppPerformanceMetricIds.TelemetryDataChangedSessionInfoVersion,
                    sessionVersionStarted,
                    sessionVersionSucceeded);
            }

            if (latestSessionInfoUpdate)
            {
                var sessionInfoStarted = Stopwatch.GetTimestamp();
                var sessionInfoSucceeded = false;
                try
                {
                    RecordSessionInfoSnapshot(capture, sdk, capturedAtUtc, sessionInfoUpdate);
                    sessionInfoSucceeded = true;
                }
                finally
                {
                    _performance.RecordOperation(
                        AppPerformanceMetricIds.TelemetryDataChangedSessionInfoSnapshot,
                        sessionInfoStarted,
                        sessionInfoSucceeded);
                }
            }

            if (capture is not null)
            {
                var rawCaptureStarted = Stopwatch.GetTimestamp();
                var rawCaptureSucceeded = false;
                try
                {
                    RecordRawCaptureFrame(capture, sdk, capturedAtUtc, sessionInfoUpdate);
                    rawCaptureSucceeded = true;
                }
                finally
                {
                    _performance.RecordOperation(
                        AppPerformanceMetricIds.TelemetryDataChangedRawCaptureFrame,
                        rawCaptureStarted,
                        rawCaptureSucceeded);
                }
            }

            var historicalFrameStarted = Stopwatch.GetTimestamp();
            var historicalFrameSucceeded = false;
            try
            {
                RecordHistoricalFrame(sdk, capturedAtUtc, sessionInfoUpdate);
                historicalFrameSucceeded = true;
            }
            finally
            {
                _performance.RecordOperation(
                    AppPerformanceMetricIds.TelemetryDataChangedHistoricalFrame,
                    historicalFrameStarted,
                    historicalFrameSucceeded);
            }

            var stateFrameStarted = Stopwatch.GetTimestamp();
            var stateFrameSucceeded = false;
            try
            {
                _state.RecordFrame(capturedAtUtc);
                _performance.RecordTelemetryFrame(capturedAtUtc);
                stateFrameSucceeded = true;
            }
            finally
            {
                _performance.RecordOperation(
                    AppPerformanceMetricIds.TelemetryDataChangedStateFrame,
                    stateFrameStarted,
                    stateFrameSucceeded);
            }

            succeeded = true;
        }
        catch (Exception exception)
        {
            _state.RecordError($"Telemetry read failed: {exception.Message}");
            _logger.LogError(exception, "An error occurred while reading telemetry from iRacing.");
        }
        finally
        {
            _performance.RecordOperation(
                AppPerformanceMetricIds.TelemetryDataChanged,
                dataChangedStarted,
                succeeded);
        }
    }

    private TelemetryCaptureSession? GetOrCreateCollection(IRacingSDK sdk, DateTimeOffset startedAtUtc)
    {
        lock (_sync)
        {
            if (_activeHistory is not null)
            {
                if (_activeCapture is null && _state.IsRawCaptureEnabled())
                {
                    _activeCapture = TryStartRawCaptureLocked(sdk, startedAtUtc, queueCurrentSessionInfo: true);
                }

                return _activeCapture;
            }

            TelemetryCaptureSession? capture = null;
            if (_state.IsRawCaptureEnabled())
            {
                capture = TryStartRawCaptureLocked(sdk, startedAtUtc, queueCurrentSessionInfo: false);
            }

            _activeHistory = new HistoricalSessionAccumulator();
            var sourceId = capture?.CaptureId ?? $"session-{startedAtUtc:yyyyMMdd-HHmmss-fff}";
            _activeSourceId = sourceId;
            _activeStartedAtUtc = capture?.StartedAtUtc ?? startedAtUtc;
            var edgeCaseSchema = ReadEdgeCaseSchema(sdk);
            _activeRawWatchVariableNames = edgeCaseSchema.WatchedVariables
                .Select(variable => variable.Name)
                .ToArray();
            _activeRawWatchVariableGroups = edgeCaseSchema.WatchedVariables
                .ToDictionary(
                    variable => variable.Name,
                    variable => variable.Group,
                    StringComparer.OrdinalIgnoreCase);
            _sessionInfoSnapshotCount = 0;
            _frameIndex = 0;
            _lastSessionInfoUpdate = -1;
            _state.MarkCollectionStarted(_activeStartedAtUtc.Value);
            _liveTelemetrySink.MarkCollectionStarted(sourceId, _activeStartedAtUtc.Value);
            _edgeCaseRecorder.StartCollection(sourceId, _activeStartedAtUtc.Value, edgeCaseSchema);
            _liveModelParityRecorder.StartCollection(sourceId, _activeStartedAtUtc.Value);
            _liveOverlayDiagnosticsRecorder.StartCollection(sourceId, _activeStartedAtUtc.Value);

            if (capture is not null)
            {
                _activeCapture = capture;
            }

            _events.Record("telemetry_collection_started", new Dictionary<string, string?>
            {
                ["sourceId"] = _activeSourceId,
                ["rawCaptureEnabled"] = _state.IsRawCaptureEnabled().ToString()
            });
            _logger.LogInformation(
                "Started live telemetry collection {SourceId}. Raw capture enabled: {RawCaptureEnabled}.",
                _activeSourceId,
                _state.IsRawCaptureEnabled());

            return capture;
        }
    }

    private TelemetryCaptureSession? TryStartRawCaptureLocked(
        IRacingSDK sdk,
        DateTimeOffset capturedAtUtc,
        bool queueCurrentSessionInfo)
    {
        try
        {
            return StartRawCaptureLocked(sdk, capturedAtUtc, queueCurrentSessionInfo);
        }
        catch (Exception exception)
        {
            _state.RecordError($"Failed to start raw capture: {exception.Message}");
            _state.SetRawCaptureEnabled(false);
            _events.Record("capture_start_failed", new Dictionary<string, string?>
            {
                ["error"] = exception.GetType().Name
            });
            _logger.LogError(exception, "Failed to start raw telemetry capture.");
            return null;
        }
    }

    private TelemetryCaptureSession StartRawCaptureLocked(
        IRacingSDK sdk,
        DateTimeOffset capturedAtUtc,
        bool queueCurrentSessionInfo)
    {
        Directory.CreateDirectory(_options.ResolvedCaptureRoot);
        var capture = CreateCaptureSession(sdk);
        _activeCapture = capture;
        _state.MarkCaptureStarted(capture.DirectoryPath, capture.StartedAtUtc);
        _events.Record("capture_started", new Dictionary<string, string?>
        {
            ["captureId"] = capture.CaptureId,
            ["captureDirectory"] = capture.DirectoryPath
        });
        _logger.LogInformation("Started raw capture {CaptureDirectory}.", capture.DirectoryPath);

        if (queueCurrentSessionInfo && sdk.Header is not null)
        {
            RecordCurrentCaptureSessionInfoSnapshot(capture, sdk, capturedAtUtc, sdk.Header.SessionInfoUpdate);
        }

        TryStartIbtTelemetryLogging(sdk, capture);
        return capture;
    }

    private void TryStartIbtTelemetryLogging(IRacingSDK sdk, TelemetryCaptureSession capture)
    {
        if (!_ibtOptions.Enabled || !_ibtOptions.TelemetryLoggingEnabled)
        {
            return;
        }

        try
        {
            var result = sdk.BroadcastMessage(
                BroadcastMessageTypes.TelemCommand,
                (int)TelemCommandModeTypes.Start,
                0);
            _events.Record("ibt_telemetry_logging_start_requested", new Dictionary<string, string?>
            {
                ["captureId"] = capture.CaptureId,
                ["captureDirectory"] = capture.DirectoryPath,
                ["telemetryRoot"] = _ibtOptions.TelemetryRoot,
                ["broadcastResult"] = result.ToString()
            });
            _logger.LogInformation(
                "Requested iRacing IBT telemetry logging for raw capture {CaptureId}. Broadcast result: {BroadcastResult}.",
                capture.CaptureId,
                result);
        }
        catch (Exception exception)
        {
            _state.RecordWarning($"IBT telemetry logging start failed: {exception.Message}");
            _events.Record("ibt_telemetry_logging_start_failed", new Dictionary<string, string?>
            {
                ["captureId"] = capture.CaptureId,
                ["captureDirectory"] = capture.DirectoryPath,
                ["error"] = exception.GetType().Name
            });
            _logger.LogWarning(exception, "Failed to request iRacing IBT telemetry logging for raw capture {CaptureId}.", capture.CaptureId);
        }
    }

    private void TryStopIbtTelemetryLogging(IRacingSDK? sdk, TelemetryCaptureSession? capture, string endedReason)
    {
        if (!_ibtOptions.Enabled || !_ibtOptions.TelemetryLoggingEnabled || sdk is null || capture is null)
        {
            return;
        }

        try
        {
            if (!sdk.IsConnected())
            {
                return;
            }

            var result = sdk.BroadcastMessage(
                BroadcastMessageTypes.TelemCommand,
                (int)TelemCommandModeTypes.Stop,
                0);
            _events.Record("ibt_telemetry_logging_stop_requested", new Dictionary<string, string?>
            {
                ["captureId"] = capture.CaptureId,
                ["captureDirectory"] = capture.DirectoryPath,
                ["telemetryRoot"] = _ibtOptions.TelemetryRoot,
                ["endedReason"] = endedReason,
                ["broadcastResult"] = result.ToString()
            });
            _logger.LogInformation(
                "Requested iRacing IBT telemetry logging stop for raw capture {CaptureId}. Reason: {EndedReason}. Broadcast result: {BroadcastResult}.",
                capture.CaptureId,
                endedReason,
                result);
        }
        catch (Exception exception)
        {
            _state.RecordWarning($"IBT telemetry logging stop failed: {exception.Message}");
            _events.Record("ibt_telemetry_logging_stop_failed", new Dictionary<string, string?>
            {
                ["captureId"] = capture.CaptureId,
                ["captureDirectory"] = capture.DirectoryPath,
                ["endedReason"] = endedReason,
                ["error"] = exception.GetType().Name
            });
            _logger.LogWarning(exception, "Failed to request iRacing IBT telemetry logging stop for raw capture {CaptureId}.", capture.CaptureId);
        }
    }

    private TelemetryCaptureSession CreateCaptureSession(IRacingSDK sdk)
    {
        var headers = IRacingSDK.GetVarHeaders(sdk);
        if (headers is null || sdk.Header is null)
        {
            throw new InvalidOperationException("The SDK headers are not available yet.");
        }

        var schema = headers.Values
            .OrderBy(header => header.Offset)
            .Select(header => new TelemetryVariableSchema(
                Name: header.Name,
                TypeName: header.Type.ToString(),
                TypeCode: (int)header.Type,
                Count: header.Count,
                Offset: header.Offset,
                ByteSize: header.Bytes,
                Length: header.Length,
                Unit: header.Unit,
                Description: header.Desc))
            .ToArray();

        return TelemetryCaptureSession.Create(
            _options.ResolvedCaptureRoot,
            _options.QueueCapacity,
            _options.StoreSessionInfoSnapshots,
            sdk.Header.Version,
            sdk.Header.TickRate,
            sdk.Header.BufferLength,
            schema,
            RecordCaptureWriteStatus);
    }

    private static RawTelemetrySchemaSnapshot ReadEdgeCaseSchema(IRacingSDK sdk)
    {
        var headers = IRacingSDK.GetVarHeaders(sdk);
        if (headers is null)
        {
            return RawTelemetrySchemaSnapshot.Empty;
        }

        var watched = headers.Values
            .Where(header => RawTelemetryWatchVariables.ShouldWatch(header.Name, header.Desc))
            .OrderBy(header => header.Name, StringComparer.OrdinalIgnoreCase)
            .Select(header => new RawTelemetryWatchedVariable(
                Name: header.Name,
                Group: RawTelemetryWatchVariables.GroupFor(header.Name, header.Desc),
                TypeName: header.Type.ToString(),
                Count: header.Count,
                Unit: string.IsNullOrWhiteSpace(header.Unit) ? null : header.Unit,
                Description: string.IsNullOrWhiteSpace(header.Desc) ? null : header.Desc))
            .ToArray();
        var found = watched.Select(variable => variable.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = RawTelemetryWatchVariables.Names
            .Where(name => !found.Contains(name))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new RawTelemetrySchemaSnapshot(watched, missing);
    }

    private static RawTelemetryWatchSnapshot ReadRawTelemetryWatchSnapshot(
        IRacingSDK sdk,
        IReadOnlyCollection<string> variableNames,
        IReadOnlyDictionary<string, string> variableGroups)
    {
        var values = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in variableNames)
        {
            if (TryReadRawWatchValue(sdk, name, out var value))
            {
                values[name] = value;
            }
        }

        return values.Count == 0
            ? RawTelemetryWatchSnapshot.Empty
            : new RawTelemetryWatchSnapshot(values) { VariableGroups = variableGroups };
    }

    private static bool TryReadRawWatchValue(IRacingSDK sdk, string variableName, out double value)
    {
        value = 0d;
        try
        {
            switch (sdk.GetData(variableName))
            {
                case bool boolValue:
                    value = boolValue ? 1d : 0d;
                    return true;
                case byte byteValue:
                    value = byteValue;
                    return true;
                case sbyte sbyteValue:
                    value = sbyteValue;
                    return true;
                case short shortValue:
                    value = shortValue;
                    return true;
                case ushort ushortValue:
                    value = ushortValue;
                    return true;
                case int intValue:
                    value = intValue;
                    return true;
                case uint uintValue:
                    value = uintValue;
                    return true;
                case long longValue:
                    value = longValue;
                    return true;
                case ulong ulongValue when ulongValue <= long.MaxValue:
                    value = ulongValue;
                    return true;
                case float floatValue when !float.IsNaN(floatValue) && !float.IsInfinity(floatValue):
                    value = floatValue;
                    return true;
                case double doubleValue when !double.IsNaN(doubleValue) && !double.IsInfinity(doubleValue):
                    value = doubleValue;
                    return true;
                default:
                    return false;
            }
        }
        catch
        {
            return false;
        }
    }

    private void RecordCaptureWriteStatus(TelemetryCaptureWriteStatus writeStatus)
    {
        var started = Stopwatch.GetTimestamp();
        var succeeded = false;
        try
        {
            _state.RecordCaptureWrite(writeStatus);
            _performance.RecordCaptureWrite(writeStatus);
            succeeded = true;
        }
        finally
        {
            _performance.RecordOperation(
                AppPerformanceMetricIds.CaptureWriteStatusCallback,
                started,
                succeeded);
        }
    }

    private bool UpdateSessionInfoVersion(int sessionInfoUpdate)
    {
        lock (_sync)
        {
            if (sessionInfoUpdate == _lastSessionInfoUpdate)
            {
                return false;
            }

            _lastSessionInfoUpdate = sessionInfoUpdate;
            return true;
        }
    }

    private static byte[] ReadTelemetryBuffer(IRacingSDK sdk)
    {
        var fileView = IRacingSDK.GetFileMapView(sdk);
        if (fileView is null || sdk.Header is null)
        {
            throw new InvalidOperationException("The SDK memory map is not available.");
        }

        var payload = GC.AllocateUninitializedArray<byte>(sdk.Header.BufferLength);
        fileView.ReadArray(sdk.Header.Offset, payload, 0, payload.Length);
        return payload;
    }

    private static int ReadInt32(IRacingSDK sdk, string variableName)
    {
        return sdk.GetData(variableName) switch
        {
            int value => value,
            uint value => unchecked((int)value),
            _ => 0
        };
    }

    private static int? ReadNullableInt32(IRacingSDK sdk, string variableName)
    {
        return sdk.GetData(variableName) switch
        {
            int value => value,
            uint value => unchecked((int)value),
            _ => null
        };
    }

    private static double ReadDouble(IRacingSDK sdk, string variableName)
    {
        return sdk.GetData(variableName) switch
        {
            double value => value,
            float value => value,
            int value => value,
            _ => double.NaN
        };
    }

    private static double? ReadNullableDouble(IRacingSDK sdk, string variableName)
    {
        var value = ReadDouble(sdk, variableName);
        return double.IsNaN(value) || double.IsInfinity(value) || value < 0d ? null : value;
    }

    private static double? ReadNullableFiniteDouble(IRacingSDK sdk, string variableName)
    {
        var value = ReadDouble(sdk, variableName);
        return double.IsNaN(value) || double.IsInfinity(value) ? null : value;
    }

    private static bool? ReadNullableBoolean(IRacingSDK sdk, string variableName)
    {
        return sdk.GetData(variableName) switch
        {
            bool value => value,
            int value => value != 0,
            uint value => value != 0,
            _ => null
        };
    }

    private static HistoricalTireConditionSnapshot ReadTireCondition(IRacingSDK sdk)
    {
        return new HistoricalTireConditionSnapshot(
            LeftFront: ReadTireCornerCondition(sdk, "LF"),
            RightFront: ReadTireCornerCondition(sdk, "RF"),
            LeftRear: ReadTireCornerCondition(sdk, "LR"),
            RightRear: ReadTireCornerCondition(sdk, "RR"));
    }

    private static HistoricalTireCornerCondition ReadTireCornerCondition(IRacingSDK sdk, string prefix)
    {
        return new HistoricalTireCornerCondition(
            WearLeft: ReadNullableFiniteDouble(sdk, $"{prefix}wearL"),
            WearMiddle: ReadNullableFiniteDouble(sdk, $"{prefix}wearM"),
            WearRight: ReadNullableFiniteDouble(sdk, $"{prefix}wearR"),
            TemperatureCLeft: ReadNullableFiniteDouble(sdk, $"{prefix}tempCL"),
            TemperatureCMiddle: ReadNullableFiniteDouble(sdk, $"{prefix}tempCM"),
            TemperatureCRight: ReadNullableFiniteDouble(sdk, $"{prefix}tempCR"),
            ColdPressureKpa: ReadNullableFiniteDouble(sdk, $"{prefix}coldPressure"),
            OdometerMeters: ReadNullableFiniteDouble(sdk, $"{prefix}odometer"));
    }

    private static HistoricalPitServiceTireRequest ReadPitServiceTireRequest(IRacingSDK sdk)
    {
        return new HistoricalPitServiceTireRequest(
            RequestedTireCompound: ReadNullableInt32(sdk, "PitSvTireCompound"),
            LeftFrontServicePressureKpa: ReadNullableFiniteDouble(sdk, "PitSvLFP"),
            RightFrontServicePressureKpa: ReadNullableFiniteDouble(sdk, "PitSvRFP"),
            LeftRearServicePressureKpa: ReadNullableFiniteDouble(sdk, "PitSvLRP"),
            RightRearServicePressureKpa: ReadNullableFiniteDouble(sdk, "PitSvRRP"),
            LeftFrontColdPressurePa: ReadNullableFiniteDouble(sdk, "dpLFTireColdPress"),
            RightFrontColdPressurePa: ReadNullableFiniteDouble(sdk, "dpRFTireColdPress"),
            LeftRearColdPressurePa: ReadNullableFiniteDouble(sdk, "dpLRTireColdPress"),
            RightRearColdPressurePa: ReadNullableFiniteDouble(sdk, "dpRRTireColdPress"),
            LeftFrontChangeRequested: ReadNullableBoolean(sdk, "dpLFTireChange"),
            RightFrontChangeRequested: ReadNullableBoolean(sdk, "dpRFTireChange"),
            LeftRearChangeRequested: ReadNullableBoolean(sdk, "dpLRTireChange"),
            RightRearChangeRequested: ReadNullableBoolean(sdk, "dpRRTireChange"));
    }

    private static int? ReadInt32ArrayElement(IRacingSDK sdk, string variableName, int index)
    {
        if (index < 0)
        {
            return null;
        }

        return sdk.GetData(variableName) switch
        {
            int[] values when index < values.Length => values[index],
            Array values when index < values.Length && values.GetValue(index) is not null => Convert.ToInt32(values.GetValue(index)),
            _ => null
        };
    }

    private static double? ReadDoubleArrayElement(IRacingSDK sdk, string variableName, int index)
    {
        if (index < 0)
        {
            return null;
        }

        return sdk.GetData(variableName) switch
        {
            double[] values when index < values.Length => values[index],
            float[] values when index < values.Length => values[index],
            int[] values when index < values.Length => values[index],
            Array values when index < values.Length && values.GetValue(index) is not null => Convert.ToDouble(values.GetValue(index)),
            _ => null
        };
    }

    private static double? ReadNullableDoubleArrayElement(IRacingSDK sdk, string variableName, int index)
    {
        var value = ReadDoubleArrayElement(sdk, variableName, index);
        return value is null || double.IsNaN(value.Value) || double.IsInfinity(value.Value) || value.Value < 0d
            ? null
            : value;
    }

    private static bool? ReadBooleanArrayElement(IRacingSDK sdk, string variableName, int index)
    {
        if (index < 0)
        {
            return null;
        }

        return sdk.GetData(variableName) switch
        {
            bool[] values when index < values.Length => values[index],
            int[] values when index < values.Length => values[index] != 0,
            Array values when index < values.Length && values.GetValue(index) is not null => Convert.ToBoolean(values.GetValue(index)),
            _ => null
        };
    }

    private static CarProgress? ReadLeaderProgress(IRacingSDK sdk)
    {
        CarProgress? bestProgress = null;

        for (var carIdx = 0; carIdx < 64; carIdx++)
        {
            var progress = ReadCarProgress(sdk, carIdx, requireLapProgress: false);
            if (progress is null)
            {
                continue;
            }

            if (progress.Position == 1)
            {
                return progress;
            }

            if (!progress.HasLapProgress)
            {
                continue;
            }

            if (bestProgress is null || progress.TotalLaps > bestProgress.TotalLaps)
            {
                bestProgress = progress;
            }
        }

        return bestProgress;
    }

    private static CarProgress? ReadClassLeaderProgress(IRacingSDK sdk, int referenceCarIdx)
    {
        var referenceClass = ReadInt32ArrayElement(sdk, "CarIdxClass", referenceCarIdx);
        if (referenceClass is null)
        {
            return null;
        }

        CarProgress? bestClassProgress = null;
        for (var carIdx = 0; carIdx < 64; carIdx++)
        {
            var carClass = ReadInt32ArrayElement(sdk, "CarIdxClass", carIdx);
            if (carClass != referenceClass)
            {
                continue;
            }

            var progress = ReadCarProgress(sdk, carIdx, requireLapProgress: false);
            if (progress is null)
            {
                continue;
            }

            if (progress.ClassPosition == 1)
            {
                return progress;
            }

            if (!progress.HasLapProgress)
            {
                continue;
            }

            if (bestClassProgress is null || progress.TotalLaps > bestClassProgress.TotalLaps)
            {
                bestClassProgress = progress;
            }
        }

        return bestClassProgress;
    }

    private static FocusCarSelection ReadFocusCarSelection(IRacingSDK sdk)
    {
        var camCarIdx = ReadNullableInt32(sdk, "CamCarIdx");
        if (camCarIdx is null)
        {
            return new FocusCarSelection(null, null, "cam_car_idx_missing");
        }

        if (camCarIdx is < 0 or >= 64)
        {
            return new FocusCarSelection(camCarIdx, null, "cam_car_idx_invalid");
        }

        if (ReadCarProgress(sdk, camCarIdx.Value, requireLapProgress: false) is not null)
        {
            return new FocusCarSelection(camCarIdx, camCarIdx, null);
        }

        return new FocusCarSelection(camCarIdx, null, "cam_car_progress_unavailable");
    }

    private static CarProgress? ReadCarProgress(IRacingSDK sdk, int carIdx, bool requireLapProgress = true)
    {
        var lapCompleted = ReadInt32ArrayElement(sdk, "CarIdxLapCompleted", carIdx);
        var lapDistPct = ReadDoubleArrayElement(sdk, "CarIdxLapDistPct", carIdx);
        var f2TimeSeconds = ReadNullableDoubleArrayElement(sdk, "CarIdxF2Time", carIdx);
        var estimatedTimeSeconds = ReadNullableDoubleArrayElement(sdk, "CarIdxEstTime", carIdx);
        var position = ReadInt32ArrayElement(sdk, "CarIdxPosition", carIdx);
        var classPosition = ReadInt32ArrayElement(sdk, "CarIdxClassPosition", carIdx);
        var hasLapProgress = HasLapProgress(lapCompleted, lapDistPct);
        if (!hasLapProgress && requireLapProgress)
        {
            return null;
        }

        if (!hasLapProgress && !HasStandingOrTiming(position, classPosition, f2TimeSeconds, estimatedTimeSeconds))
        {
            return null;
        }

        return new CarProgress(
            CarIdx: carIdx,
            LapCompleted: hasLapProgress ? lapCompleted!.Value : -1,
            LapDistPct: hasLapProgress ? Math.Clamp(lapDistPct!.Value, 0d, 1d) : -1d,
            F2TimeSeconds: f2TimeSeconds,
            EstimatedTimeSeconds: estimatedTimeSeconds,
            LastLapTimeSeconds: ReadNullableDoubleArrayElement(sdk, "CarIdxLastLapTime", carIdx),
            BestLapTimeSeconds: ReadNullableDoubleArrayElement(sdk, "CarIdxBestLapTime", carIdx),
            Position: position,
            ClassPosition: classPosition,
            CarClass: ReadInt32ArrayElement(sdk, "CarIdxClass", carIdx),
            TireCompound: ReadInt32ArrayElement(sdk, "CarIdxTireCompound", carIdx));
    }

    private static IReadOnlyList<HistoricalCarProximity> ReadNearbyCars(IRacingSDK sdk, int referenceCarIdx)
    {
        if (referenceCarIdx < 0)
        {
            return [];
        }

        var cars = new List<HistoricalCarProximity>();
        for (var carIdx = 0; carIdx < 64; carIdx++)
        {
            if (carIdx == referenceCarIdx)
            {
                continue;
            }

            var lapCompleted = ReadInt32ArrayElement(sdk, "CarIdxLapCompleted", carIdx);
            var lapDistPct = ReadDoubleArrayElement(sdk, "CarIdxLapDistPct", carIdx);
            if (lapDistPct is null
                || double.IsNaN(lapDistPct.Value)
                || double.IsInfinity(lapDistPct.Value)
                || lapDistPct < 0d)
            {
                continue;
            }

            cars.Add(new HistoricalCarProximity(
                CarIdx: carIdx,
                LapCompleted: lapCompleted is >= 0 ? lapCompleted.Value : -1,
                LapDistPct: Math.Clamp(lapDistPct.Value, 0d, 1d),
                F2TimeSeconds: ReadNullableDoubleArrayElement(sdk, "CarIdxF2Time", carIdx),
                EstimatedTimeSeconds: ReadNullableDoubleArrayElement(sdk, "CarIdxEstTime", carIdx),
                Position: ReadInt32ArrayElement(sdk, "CarIdxPosition", carIdx),
                ClassPosition: ReadInt32ArrayElement(sdk, "CarIdxClassPosition", carIdx),
                CarClass: ReadInt32ArrayElement(sdk, "CarIdxClass", carIdx),
                TrackSurface: ReadInt32ArrayElement(sdk, "CarIdxTrackSurface", carIdx),
                OnPitRoad: ReadBooleanArrayElement(sdk, "CarIdxOnPitRoad", carIdx),
                TireCompound: ReadInt32ArrayElement(sdk, "CarIdxTireCompound", carIdx)));
        }

        return cars;
    }

    private static IReadOnlyList<HistoricalCarProximity> ReadClassCars(IRacingSDK sdk, int referenceCarIdx)
    {
        if (referenceCarIdx < 0)
        {
            return [];
        }

        var referenceClass = ReadInt32ArrayElement(sdk, "CarIdxClass", referenceCarIdx);
        if (referenceClass is null)
        {
            return [];
        }

        var cars = new List<HistoricalCarProximity>();
        for (var carIdx = 0; carIdx < 64; carIdx++)
        {
            var carClass = ReadInt32ArrayElement(sdk, "CarIdxClass", carIdx);
            if (carClass != referenceClass)
            {
                continue;
            }

            var lapCompleted = ReadInt32ArrayElement(sdk, "CarIdxLapCompleted", carIdx);
            var lapDistPct = ReadDoubleArrayElement(sdk, "CarIdxLapDistPct", carIdx);
            var f2TimeSeconds = ReadNullableDoubleArrayElement(sdk, "CarIdxF2Time", carIdx);
            var estimatedTimeSeconds = ReadNullableDoubleArrayElement(sdk, "CarIdxEstTime", carIdx);
            var position = ReadInt32ArrayElement(sdk, "CarIdxPosition", carIdx);
            var classPosition = ReadInt32ArrayElement(sdk, "CarIdxClassPosition", carIdx);
            var hasLapProgress = HasLapProgress(lapCompleted, lapDistPct);
            if (!hasLapProgress && !HasStandingOrTiming(position, classPosition, f2TimeSeconds, estimatedTimeSeconds))
            {
                continue;
            }

            cars.Add(new HistoricalCarProximity(
                CarIdx: carIdx,
                LapCompleted: hasLapProgress ? lapCompleted!.Value : -1,
                LapDistPct: hasLapProgress ? Math.Clamp(lapDistPct!.Value, 0d, 1d) : -1d,
                F2TimeSeconds: f2TimeSeconds,
                EstimatedTimeSeconds: estimatedTimeSeconds,
                Position: position,
                ClassPosition: classPosition,
                CarClass: carClass,
                TrackSurface: ReadInt32ArrayElement(sdk, "CarIdxTrackSurface", carIdx),
                OnPitRoad: ReadBooleanArrayElement(sdk, "CarIdxOnPitRoad", carIdx),
                TireCompound: ReadInt32ArrayElement(sdk, "CarIdxTireCompound", carIdx)));
        }

        return cars;
    }

    private static IReadOnlyList<HistoricalCarProximity> ReadAllTimingCars(IRacingSDK sdk)
    {
        var cars = new List<HistoricalCarProximity>();
        for (var carIdx = 0; carIdx < 64; carIdx++)
        {
            var lapCompleted = ReadInt32ArrayElement(sdk, "CarIdxLapCompleted", carIdx);
            var lapDistPct = ReadDoubleArrayElement(sdk, "CarIdxLapDistPct", carIdx);
            var f2TimeSeconds = ReadNullableDoubleArrayElement(sdk, "CarIdxF2Time", carIdx);
            var estimatedTimeSeconds = ReadNullableDoubleArrayElement(sdk, "CarIdxEstTime", carIdx);
            var position = ReadInt32ArrayElement(sdk, "CarIdxPosition", carIdx);
            var classPosition = ReadInt32ArrayElement(sdk, "CarIdxClassPosition", carIdx);
            var hasLapProgress = HasLapProgress(lapCompleted, lapDistPct);
            if (!hasLapProgress && !HasStandingOrTiming(position, classPosition, f2TimeSeconds, estimatedTimeSeconds))
            {
                continue;
            }

            cars.Add(new HistoricalCarProximity(
                CarIdx: carIdx,
                LapCompleted: hasLapProgress ? lapCompleted!.Value : -1,
                LapDistPct: hasLapProgress ? Math.Clamp(lapDistPct!.Value, 0d, 1d) : -1d,
                F2TimeSeconds: f2TimeSeconds,
                EstimatedTimeSeconds: estimatedTimeSeconds,
                Position: position,
                ClassPosition: classPosition,
                CarClass: ReadInt32ArrayElement(sdk, "CarIdxClass", carIdx),
                TrackSurface: ReadInt32ArrayElement(sdk, "CarIdxTrackSurface", carIdx),
                OnPitRoad: ReadBooleanArrayElement(sdk, "CarIdxOnPitRoad", carIdx),
                TireCompound: ReadInt32ArrayElement(sdk, "CarIdxTireCompound", carIdx)));
        }

        return cars;
    }

    private static bool HasLapProgress(int? lapCompleted, double? lapDistPct)
    {
        return lapCompleted is >= 0
            && lapDistPct is { } pct
            && !double.IsNaN(pct)
            && !double.IsInfinity(pct)
            && pct >= 0d;
    }

    private static bool HasStandingOrTiming(
        int? position,
        int? classPosition,
        double? f2TimeSeconds,
        double? estimatedTimeSeconds)
    {
        return position is > 0
            || classPosition is > 0
            || f2TimeSeconds is not null
            || estimatedTimeSeconds is not null;
    }

    private static bool ReadBoolean(IRacingSDK sdk, string variableName)
    {
        return sdk.GetData(variableName) switch
        {
            bool value => value,
            int value => value != 0,
            _ => false
        };
    }

    private void RecordRawCaptureFrame(
        TelemetryCaptureSession capture,
        IRacingSDK sdk,
        DateTimeOffset capturedAtUtc,
        int sessionInfoUpdate)
    {
        try
        {
            TelemetryFrameEnvelope frame;
            var rawReadStarted = Stopwatch.GetTimestamp();
            var rawReadSucceeded = false;
            try
            {
                var payload = ReadTelemetryBuffer(sdk);
                frame = new TelemetryFrameEnvelope(
                    CapturedAtUtc: capturedAtUtc,
                    FrameIndex: Interlocked.Increment(ref _frameIndex),
                    SessionTick: ReadInt32(sdk, "SessionTick"),
                    SessionInfoUpdate: sessionInfoUpdate,
                    SessionTime: ReadDouble(sdk, "SessionTime"),
                    Payload: payload);
                rawReadSucceeded = true;
            }
            finally
            {
                _performance.RecordOperation(
                    AppPerformanceMetricIds.TelemetryRawFrameRead,
                    rawReadStarted,
                    rawReadSucceeded);
            }

            var queueStarted = Stopwatch.GetTimestamp();
            var queueSucceeded = false;
            try
            {
                if (capture.TryQueueFrame(frame))
                {
                    queueSucceeded = true;
                    return;
                }

                RecordRawCaptureDroppedFrame(capture);
                queueSucceeded = true;
            }
            finally
            {
                _performance.RecordOperation(
                    AppPerformanceMetricIds.TelemetryRawFrameQueue,
                    queueStarted,
                    queueSucceeded);
            }
        }
        catch (Exception exception)
        {
            capture.RecordDroppedFrame();
            _state.RecordDroppedFrame();
            _state.RecordError($"Raw capture frame read failed: {exception.Message}");
            _events.Record("capture_frame_read_failed", new Dictionary<string, string?>
            {
                ["captureId"] = capture.CaptureId,
                ["error"] = exception.GetType().Name
            });
            _logger.LogError(
                exception,
                "Failed to read a raw telemetry frame. Live telemetry will continue from SDK variables.");
        }
    }

    private void RecordRawCaptureDroppedFrame(TelemetryCaptureSession capture)
    {
        capture.RecordDroppedFrame();
        _state.RecordDroppedFrame();
        var writerFault = capture.WriterFault;
        if (writerFault is not null)
        {
            _state.RecordError($"Capture writer failed: {writerFault.Message}");
            _events.Record("capture_writer_failed", new Dictionary<string, string?>
            {
                ["captureId"] = capture.CaptureId,
                ["error"] = writerFault.Message
            });
            _logger.LogError(writerFault, "Dropped telemetry frame because the capture writer failed. Live telemetry will continue.");
            return;
        }

        _state.RecordWarning("Dropped telemetry frame because the capture queue is full.");
        _events.Record("capture_dropped_frame", new Dictionary<string, string?>
        {
            ["captureId"] = capture.CaptureId
        });
        _logger.LogWarning("Dropped telemetry frame because the capture queue is full. Live telemetry will continue.");
    }

    private void RecordSessionInfoSnapshot(
        TelemetryCaptureSession? capture,
        IRacingSDK sdk,
        DateTimeOffset capturedAtUtc,
        int sessionInfoUpdate)
    {
        string sessionInfoYaml;
        var readStarted = Stopwatch.GetTimestamp();
        var readSucceeded = false;
        try
        {
            sessionInfoYaml = sdk.GetSessionInfo();
            readSucceeded = true;
        }
        catch (Exception exception)
        {
            _state.RecordWarning($"Session info read failed: {exception.Message}");
            _events.Record("session_info_read_failed", new Dictionary<string, string?>
            {
                ["error"] = exception.GetType().Name
            });
            _logger.LogWarning(exception, "Failed to read session info. Live telemetry frames will continue with the previous session context.");
            return;
        }
        finally
        {
            _performance.RecordOperation(
                AppPerformanceMetricIds.TelemetrySessionInfoRead,
                readStarted,
                readSucceeded);
        }

        if (string.IsNullOrWhiteSpace(sessionInfoYaml))
        {
            return;
        }

        if (capture is not null)
        {
            var captureQueueStarted = Stopwatch.GetTimestamp();
            var captureQueueSucceeded = false;
            try
            {
                RecordCaptureSessionInfoSnapshot(capture, capturedAtUtc, sessionInfoUpdate, sessionInfoYaml);
                captureQueueSucceeded = true;
            }
            finally
            {
                _performance.RecordOperation(
                    AppPerformanceMetricIds.TelemetrySessionInfoCaptureQueue,
                    captureQueueStarted,
                    captureQueueSucceeded);
            }
        }

        var applyStarted = Stopwatch.GetTimestamp();
        var applySucceeded = false;
        try
        {
            HistoricalSessionAccumulator? history;
            lock (_sync)
            {
                history = _activeHistory;
                _sessionInfoSnapshotCount++;
            }

            _liveTelemetrySink.ApplySessionInfo(sessionInfoYaml);
            history?.ApplySessionInfo(sessionInfoYaml);
            applySucceeded = true;
        }
        finally
        {
            _performance.RecordOperation(
                AppPerformanceMetricIds.TelemetrySessionInfoApply,
                applyStarted,
                applySucceeded);
        }
    }

    private void RecordCurrentCaptureSessionInfoSnapshot(
        TelemetryCaptureSession capture,
        IRacingSDK sdk,
        DateTimeOffset capturedAtUtc,
        int sessionInfoUpdate)
    {
        string sessionInfoYaml;
        var readStarted = Stopwatch.GetTimestamp();
        var readSucceeded = false;
        try
        {
            sessionInfoYaml = sdk.GetSessionInfo();
            readSucceeded = true;
        }
        catch (Exception exception)
        {
            _state.RecordWarning($"Raw capture session info read failed: {exception.Message}");
            _events.Record("capture_session_info_read_failed", new Dictionary<string, string?>
            {
                ["captureId"] = capture.CaptureId,
                ["error"] = exception.GetType().Name
            });
            _logger.LogWarning(exception, "Failed to read current session info for raw capture. Raw frame capture and live telemetry will continue.");
            return;
        }
        finally
        {
            _performance.RecordOperation(
                AppPerformanceMetricIds.TelemetrySessionInfoRead,
                readStarted,
                readSucceeded);
        }

        var captureQueueStarted = Stopwatch.GetTimestamp();
        var captureQueueSucceeded = false;
        try
        {
            RecordCaptureSessionInfoSnapshot(capture, capturedAtUtc, sessionInfoUpdate, sessionInfoYaml);
            captureQueueSucceeded = true;
        }
        finally
        {
            _performance.RecordOperation(
                AppPerformanceMetricIds.TelemetrySessionInfoCaptureQueue,
                captureQueueStarted,
                captureQueueSucceeded);
        }
    }

    private void RecordCaptureSessionInfoSnapshot(
        TelemetryCaptureSession capture,
        DateTimeOffset capturedAtUtc,
        int sessionInfoUpdate,
        string sessionInfoYaml)
    {
        if (string.IsNullOrWhiteSpace(sessionInfoYaml))
        {
            return;
        }

        var sessionSnapshot = new SessionInfoSnapshot(
            CapturedAtUtc: capturedAtUtc,
            SessionInfoUpdate: sessionInfoUpdate,
            Yaml: sessionInfoYaml);

        if (!capture.TryQueueSessionInfo(sessionSnapshot))
        {
            var writerFault = capture.WriterFault;
            if (writerFault is not null)
            {
                _state.RecordError($"Capture writer failed while saving session info: {writerFault.Message}");
                _logger.LogError(writerFault, "Dropped session info update because the capture writer failed.");
                return;
            }

            _state.RecordWarning($"Dropped session info update {sessionInfoUpdate} because the capture queue is full.");
            _logger.LogWarning("Dropped session info update {SessionInfoUpdate} because the capture queue is full.", sessionInfoUpdate);
        }
    }

    private void RecordHistoricalFrame(IRacingSDK sdk, DateTimeOffset capturedAtUtc, int sessionInfoUpdate)
    {
        HistoricalSessionAccumulator? history;
        IReadOnlyList<string> rawWatchVariableNames;
        IReadOnlyDictionary<string, string> rawWatchVariableGroups;
        lock (_sync)
        {
            history = _activeHistory;
            rawWatchVariableNames = _activeRawWatchVariableNames;
            rawWatchVariableGroups = _activeRawWatchVariableGroups;
        }

        HistoricalTelemetrySample sample;
        RawTelemetryWatchSnapshot rawWatch = RawTelemetryWatchSnapshot.Empty;
        var buildSampleStarted = Stopwatch.GetTimestamp();
        var buildSampleSucceeded = false;
        try
        {
            var playerCarIdx = ReadInt32(sdk, "PlayerCarIdx");
            var focusSelection = ReadFocusCarSelection(sdk);
            var focusCarIdx = focusSelection.FocusCarIdx;
            var focusProgress = focusCarIdx is { } focusProgressCarIdx
                ? ReadCarProgress(sdk, focusProgressCarIdx, requireLapProgress: false)
                : null;

            CarProgress? leaderProgress;
            var leaderStarted = Stopwatch.GetTimestamp();
            var leaderSucceeded = false;
            try
            {
                leaderProgress = ReadLeaderProgress(sdk);
                leaderSucceeded = true;
            }
            finally
            {
                _performance.RecordOperation(
                    AppPerformanceMetricIds.TelemetryHistoryReadLeader,
                    leaderStarted,
                    leaderSucceeded);
            }

            CarProgress? classLeaderProgress;
            var classLeaderStarted = Stopwatch.GetTimestamp();
            var classLeaderSucceeded = false;
            try
            {
                classLeaderProgress = ReadClassLeaderProgress(sdk, playerCarIdx);
                classLeaderSucceeded = true;
            }
            finally
            {
                _performance.RecordOperation(
                    AppPerformanceMetricIds.TelemetryHistoryReadClassLeader,
                    classLeaderStarted,
                    classLeaderSucceeded);
            }

            var focusClassLeaderProgress = focusCarIdx is null
                ? null
                : focusCarIdx == playerCarIdx
                    ? classLeaderProgress
                    : ReadClassLeaderProgress(sdk, focusCarIdx.Value);

            IReadOnlyList<HistoricalCarProximity> nearbyCars;
            var nearbyStarted = Stopwatch.GetTimestamp();
            var nearbySucceeded = false;
            try
            {
                nearbyCars = focusCarIdx is { } nearbyFocusCarIdx
                    ? ReadNearbyCars(sdk, nearbyFocusCarIdx)
                    : [];
                nearbySucceeded = true;
            }
            finally
            {
                _performance.RecordOperation(
                    AppPerformanceMetricIds.TelemetryHistoryReadNearbyCars,
                    nearbyStarted,
                    nearbySucceeded);
            }

            IReadOnlyList<HistoricalCarProximity> classCars;
            var classCarsStarted = Stopwatch.GetTimestamp();
            var classCarsSucceeded = false;
            try
            {
                classCars = ReadClassCars(sdk, playerCarIdx);
                classCarsSucceeded = true;
            }
            finally
            {
                _performance.RecordOperation(
                    AppPerformanceMetricIds.TelemetryHistoryReadClassCars,
                    classCarsStarted,
                    classCarsSucceeded);
            }

            var focusClassCars = focusCarIdx is null
                ? []
                : focusCarIdx == playerCarIdx
                    ? classCars
                    : ReadClassCars(sdk, focusCarIdx.Value);
            var allCars = ReadAllTimingCars(sdk);

            sample = new HistoricalTelemetrySample(
                CapturedAtUtc: capturedAtUtc,
                SessionTime: ReadDouble(sdk, "SessionTime"),
                SessionTick: ReadInt32(sdk, "SessionTick"),
                SessionInfoUpdate: sessionInfoUpdate,
                IsOnTrack: ReadBoolean(sdk, "IsOnTrack"),
                IsInGarage: ReadBoolean(sdk, "IsInGarage"),
                OnPitRoad: ReadBoolean(sdk, "OnPitRoad"),
                PitstopActive: ReadBoolean(sdk, "PitstopActive"),
                PlayerCarInPitStall: ReadBoolean(sdk, "PlayerCarInPitStall"),
                FuelLevelLiters: ReadDouble(sdk, "FuelLevel"),
                FuelLevelPercent: ReadDouble(sdk, "FuelLevelPct"),
                FuelUsePerHourKg: ReadDouble(sdk, "FuelUsePerHour"),
                SpeedMetersPerSecond: ReadDouble(sdk, "Speed"),
                Lap: ReadInt32(sdk, "Lap"),
                LapCompleted: ReadInt32(sdk, "LapCompleted"),
                LapDistPct: ReadDouble(sdk, "LapDistPct"),
                LapLastLapTimeSeconds: ReadNullableDouble(sdk, "LapLastLapTime"),
                LapBestLapTimeSeconds: ReadNullableDouble(sdk, "LapBestLapTime"),
                AirTempC: ReadDouble(sdk, "AirTemp"),
                TrackTempCrewC: ReadDouble(sdk, "TrackTempCrew"),
                TrackWetness: ReadInt32(sdk, "TrackWetness"),
                WeatherDeclaredWet: ReadBoolean(sdk, "WeatherDeclaredWet"),
                PlayerTireCompound: ReadInt32(sdk, "PlayerTireCompound"),
                Skies: ReadNullableInt32(sdk, "Skies"),
                PrecipitationPercent: ReadNullableDouble(sdk, "Precipitation"),
                WindVelocityMetersPerSecond: ReadNullableDouble(sdk, "WindVel"),
                WindDirectionRadians: ReadNullableDouble(sdk, "WindDir"),
                RelativeHumidityPercent: ReadNullableDouble(sdk, "RelativeHumidity"),
                FogLevelPercent: ReadNullableDouble(sdk, "FogLevel"),
                AirPressurePa: ReadNullableDouble(sdk, "AirPressure"),
                SolarAltitudeRadians: ReadNullableFiniteDouble(sdk, "SolarAltitude"),
                SolarAzimuthRadians: ReadNullableFiniteDouble(sdk, "SolarAzimuth"),
                IsGarageVisible: ReadNullableBoolean(sdk, "IsGarageVisible"),
                IsReplayPlaying: ReadNullableBoolean(sdk, "IsReplayPlaying"),
                SessionTimeRemain: ReadNullableDouble(sdk, "SessionTimeRemain"),
                SessionTimeTotal: ReadNullableDouble(sdk, "SessionTimeTotal"),
                SessionLapsRemainEx: ReadInt32(sdk, "SessionLapsRemainEx"),
                SessionLapsTotal: ReadInt32(sdk, "SessionLapsTotal"),
                SessionState: ReadInt32(sdk, "SessionState"),
                SessionFlags: ReadNullableInt32(sdk, "SessionFlags"),
                RaceLaps: ReadInt32(sdk, "RaceLaps"),
                PlayerCarIdx: playerCarIdx,
                RawCamCarIdx: focusSelection.RawCamCarIdx,
                FocusCarIdx: focusCarIdx,
                FocusUnavailableReason: focusSelection.UnavailableReason,
                FocusLapCompleted: focusProgress?.LapCompleted,
                FocusLapDistPct: focusProgress?.LapDistPct,
                FocusF2TimeSeconds: focusProgress?.F2TimeSeconds,
                FocusEstimatedTimeSeconds: focusProgress?.EstimatedTimeSeconds,
                FocusLastLapTimeSeconds: focusProgress?.LastLapTimeSeconds,
                FocusBestLapTimeSeconds: focusProgress?.BestLapTimeSeconds,
                FocusPosition: focusProgress?.Position,
                FocusClassPosition: focusProgress?.ClassPosition,
                FocusCarClass: focusProgress?.CarClass,
                FocusTireCompound: focusProgress?.TireCompound,
                FocusOnPitRoad: focusCarIdx is { } focusPitCarIdx
                    ? ReadBooleanArrayElement(sdk, "CarIdxOnPitRoad", focusPitCarIdx)
                    : null,
                FocusTrackSurface: focusCarIdx is { } focusSurfaceCarIdx
                    ? ReadInt32ArrayElement(sdk, "CarIdxTrackSurface", focusSurfaceCarIdx)
                    : null,
                TeamLapCompleted: ReadInt32ArrayElement(sdk, "CarIdxLapCompleted", playerCarIdx),
                TeamLapDistPct: ReadDoubleArrayElement(sdk, "CarIdxLapDistPct", playerCarIdx),
                TeamF2TimeSeconds: ReadNullableDoubleArrayElement(sdk, "CarIdxF2Time", playerCarIdx),
                TeamEstimatedTimeSeconds: ReadNullableDoubleArrayElement(sdk, "CarIdxEstTime", playerCarIdx),
                TeamLastLapTimeSeconds: ReadNullableDoubleArrayElement(sdk, "CarIdxLastLapTime", playerCarIdx),
                TeamBestLapTimeSeconds: ReadNullableDoubleArrayElement(sdk, "CarIdxBestLapTime", playerCarIdx),
                TeamPosition: ReadInt32ArrayElement(sdk, "CarIdxPosition", playerCarIdx),
                TeamClassPosition: ReadInt32ArrayElement(sdk, "CarIdxClassPosition", playerCarIdx),
                TeamCarClass: ReadInt32ArrayElement(sdk, "CarIdxClass", playerCarIdx),
                TeamTireCompound: ReadInt32ArrayElement(sdk, "CarIdxTireCompound", playerCarIdx),
                LeaderCarIdx: leaderProgress?.CarIdx,
                LeaderLapCompleted: leaderProgress?.LapCompleted,
                LeaderLapDistPct: leaderProgress?.LapDistPct,
                LeaderF2TimeSeconds: leaderProgress?.F2TimeSeconds,
                LeaderEstimatedTimeSeconds: leaderProgress?.EstimatedTimeSeconds,
                LeaderLastLapTimeSeconds: leaderProgress?.LastLapTimeSeconds,
                LeaderBestLapTimeSeconds: leaderProgress?.BestLapTimeSeconds,
                LeaderTireCompound: leaderProgress?.TireCompound,
                ClassLeaderCarIdx: classLeaderProgress?.CarIdx,
                ClassLeaderLapCompleted: classLeaderProgress?.LapCompleted,
                ClassLeaderLapDistPct: classLeaderProgress?.LapDistPct,
                ClassLeaderF2TimeSeconds: classLeaderProgress?.F2TimeSeconds,
                ClassLeaderEstimatedTimeSeconds: classLeaderProgress?.EstimatedTimeSeconds,
                ClassLeaderLastLapTimeSeconds: classLeaderProgress?.LastLapTimeSeconds,
                ClassLeaderBestLapTimeSeconds: classLeaderProgress?.BestLapTimeSeconds,
                ClassLeaderTireCompound: classLeaderProgress?.TireCompound,
                FocusClassLeaderCarIdx: focusClassLeaderProgress?.CarIdx,
                FocusClassLeaderLapCompleted: focusClassLeaderProgress?.LapCompleted,
                FocusClassLeaderLapDistPct: focusClassLeaderProgress?.LapDistPct,
                FocusClassLeaderF2TimeSeconds: focusClassLeaderProgress?.F2TimeSeconds,
                FocusClassLeaderEstimatedTimeSeconds: focusClassLeaderProgress?.EstimatedTimeSeconds,
                FocusClassLeaderLastLapTimeSeconds: focusClassLeaderProgress?.LastLapTimeSeconds,
                FocusClassLeaderBestLapTimeSeconds: focusClassLeaderProgress?.BestLapTimeSeconds,
                FocusClassLeaderTireCompound: focusClassLeaderProgress?.TireCompound,
                PlayerTrackSurface: ReadNullableInt32(sdk, "PlayerTrackSurface"),
                CarLeftRight: ReadNullableInt32(sdk, "CarLeftRight"),
                NearbyCars: nearbyCars,
                ClassCars: classCars,
                FocusClassCars: focusClassCars,
                AllCars: allCars,
                TeamOnPitRoad: ReadBooleanArrayElement(sdk, "CarIdxOnPitRoad", playerCarIdx),
                TeamFastRepairsUsed: ReadInt32ArrayElement(sdk, "CarIdxFastRepairsUsed", playerCarIdx),
                PitServiceStatus: ReadNullableInt32(sdk, "PlayerCarPitSvStatus"),
                PitServiceFlags: ReadInt32(sdk, "PitSvFlags"),
                PitServiceFuelLiters: ReadNullableDouble(sdk, "PitSvFuel"),
                PitRepairLeftSeconds: ReadNullableDouble(sdk, "PitRepairLeft"),
                PitOptRepairLeftSeconds: ReadNullableDouble(sdk, "PitOptRepairLeft"),
                PlayerCarDryTireSetLimit: ReadInt32(sdk, "PlayerCarDryTireSetLimit"),
                TireSetsUsed: ReadInt32(sdk, "TireSetsUsed"),
                TireSetsAvailable: ReadInt32(sdk, "TireSetsAvailable"),
                LeftTireSetsUsed: ReadInt32(sdk, "LeftTireSetsUsed"),
                RightTireSetsUsed: ReadInt32(sdk, "RightTireSetsUsed"),
                FrontTireSetsUsed: ReadInt32(sdk, "FrontTireSetsUsed"),
                RearTireSetsUsed: ReadInt32(sdk, "RearTireSetsUsed"),
                LeftTireSetsAvailable: ReadInt32(sdk, "LeftTireSetsAvailable"),
                RightTireSetsAvailable: ReadInt32(sdk, "RightTireSetsAvailable"),
                FrontTireSetsAvailable: ReadInt32(sdk, "FrontTireSetsAvailable"),
                RearTireSetsAvailable: ReadInt32(sdk, "RearTireSetsAvailable"),
                LeftFrontTiresUsed: ReadInt32(sdk, "LFTiresUsed"),
                RightFrontTiresUsed: ReadInt32(sdk, "RFTiresUsed"),
                LeftRearTiresUsed: ReadInt32(sdk, "LRTiresUsed"),
                RightRearTiresUsed: ReadInt32(sdk, "RRTiresUsed"),
                LeftFrontTiresAvailable: ReadInt32(sdk, "LFTiresAvailable"),
                RightFrontTiresAvailable: ReadInt32(sdk, "RFTiresAvailable"),
                LeftRearTiresAvailable: ReadInt32(sdk, "LRTiresAvailable"),
                RightRearTiresAvailable: ReadInt32(sdk, "RRTiresAvailable"),
                FastRepairUsed: ReadInt32(sdk, "FastRepairUsed"),
                FastRepairAvailable: ReadInt32(sdk, "FastRepairAvailable"),
                TireCondition: ReadTireCondition(sdk),
                PitServiceTireRequest: ReadPitServiceTireRequest(sdk),
                DriversSoFar: ReadInt32(sdk, "DCDriversSoFar"),
                DriverChangeLapStatus: ReadInt32(sdk, "DCLapStatus"),
                LapCurrentLapTimeSeconds: ReadNullableDouble(sdk, "LapCurrentLapTime"),
                LapDeltaToBestLapSeconds: ReadNullableFiniteDouble(sdk, "LapDeltaToBestLap"),
                LapDeltaToBestLapRate: ReadNullableFiniteDouble(sdk, "LapDeltaToBestLap_DD"),
                LapDeltaToBestLapOk: ReadNullableBoolean(sdk, "LapDeltaToBestLap_OK"),
                LapDeltaToOptimalLapSeconds: ReadNullableFiniteDouble(sdk, "LapDeltaToOptimalLap"),
                LapDeltaToOptimalLapRate: ReadNullableFiniteDouble(sdk, "LapDeltaToOptimalLap_DD"),
                LapDeltaToOptimalLapOk: ReadNullableBoolean(sdk, "LapDeltaToOptimalLap_OK"),
                LapDeltaToSessionBestLapSeconds: ReadNullableFiniteDouble(sdk, "LapDeltaToSessionBestLap"),
                LapDeltaToSessionBestLapRate: ReadNullableFiniteDouble(sdk, "LapDeltaToSessionBestLap_DD"),
                LapDeltaToSessionBestLapOk: ReadNullableBoolean(sdk, "LapDeltaToSessionBestLap_OK"),
                LapDeltaToSessionOptimalLapSeconds: ReadNullableFiniteDouble(sdk, "LapDeltaToSessionOptimalLap"),
                LapDeltaToSessionOptimalLapRate: ReadNullableFiniteDouble(sdk, "LapDeltaToSessionOptimalLap_DD"),
                LapDeltaToSessionOptimalLapOk: ReadNullableBoolean(sdk, "LapDeltaToSessionOptimalLap_OK"),
                LapDeltaToSessionLastLapSeconds: ReadNullableFiniteDouble(sdk, "LapDeltaToSessionLastlLap"),
                LapDeltaToSessionLastLapRate: ReadNullableFiniteDouble(sdk, "LapDeltaToSessionLastlLap_DD"),
                LapDeltaToSessionLastLapOk: ReadNullableBoolean(sdk, "LapDeltaToSessionLastlLap_OK"),
                Gear: ReadNullableInt32(sdk, "Gear"),
                Rpm: ReadNullableFiniteDouble(sdk, "RPM"),
                Throttle: ReadNullableFiniteDouble(sdk, "Throttle"),
                Brake: ReadNullableFiniteDouble(sdk, "Brake"),
                Clutch: ReadNullableFiniteDouble(sdk, "Clutch"),
                ClutchRaw: ReadNullableFiniteDouble(sdk, "ClutchRaw"),
                SteeringWheelAngle: ReadNullableFiniteDouble(sdk, "SteeringWheelAngle"),
                PlayerYawNorthRadians: ReadNullableFiniteDouble(sdk, "YawNorth"),
                BrakeAbsActive: ReadNullableBoolean(sdk, "BrakeABSactive"),
                EngineWarnings: ReadNullableInt32(sdk, "EngineWarnings"),
                Voltage: ReadNullableFiniteDouble(sdk, "Voltage"),
                WaterTempC: ReadNullableFiniteDouble(sdk, "WaterTemp"),
                FuelPressureBar: ReadNullableFiniteDouble(sdk, "FuelPress"),
                OilTempC: ReadNullableFiniteDouble(sdk, "OilTemp"),
                OilPressureBar: ReadNullableFiniteDouble(sdk, "OilPress"));
            rawWatch = ReadRawTelemetryWatchSnapshot(sdk, rawWatchVariableNames, rawWatchVariableGroups);
            _performance.RecordIRacingSystemTelemetry(
                capturedAtUtc,
                chanQuality: rawWatch.Get("ChanQuality"),
                chanPartnerQuality: rawWatch.Get("ChanPartnerQuality"),
                chanLatency: rawWatch.Get("ChanLatency"),
                chanAvgLatency: rawWatch.Get("ChanAvgLatency"),
                chanClockSkew: rawWatch.Get("ChanClockSkew"),
                frameRate: rawWatch.Get("FrameRate"),
                cpuUsageForeground: rawWatch.Get("CpuUsageFG"),
                gpuUsage: rawWatch.Get("GpuUsage"),
                memPageFaultsPerSecond: rawWatch.Get("MemPageFaultSec"),
                memSoftPageFaultsPerSecond: rawWatch.Get("MemSoftPageFaultSec"),
                isReplayPlaying: rawWatch.Get("IsReplayPlaying"),
                isOnTrack: rawWatch.Get("IsOnTrack"));
            buildSampleSucceeded = true;
        }
        finally
        {
            _performance.RecordOperation(
                AppPerformanceMetricIds.TelemetryHistoryBuildSample,
                buildSampleStarted,
                buildSampleSucceeded);
        }

        var liveSinkStarted = Stopwatch.GetTimestamp();
        var liveSinkSucceeded = false;
        try
        {
            _liveTelemetrySink.RecordFrame(sample);
            liveSinkSucceeded = true;

            try
            {
                var liveSnapshot = _liveTelemetrySource.Snapshot();
                _liveModelParityRecorder.RecordFrame(liveSnapshot);
                _liveOverlayDiagnosticsRecorder.RecordFrame(liveSnapshot, rawWatch);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Failed to record live model observer frame.");
            }
        }
        finally
        {
            _performance.RecordOperation(
                AppPerformanceMetricIds.LiveTelemetrySink,
                liveSinkStarted,
                liveSinkSucceeded);
        }

        var edgeCaseStarted = Stopwatch.GetTimestamp();
        var edgeCaseSucceeded = false;
        try
        {
            _edgeCaseRecorder.RecordFrame(sample, rawWatch);
            edgeCaseSucceeded = true;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to record telemetry edge-case frame.");
        }
        finally
        {
            _performance.RecordOperation(
                AppPerformanceMetricIds.TelemetryEdgeCaseRecordFrame,
                edgeCaseStarted,
                edgeCaseSucceeded);
        }

        if (history is null)
        {
            return;
        }

        var historyStarted = Stopwatch.GetTimestamp();
        var historySucceeded = false;
        try
        {
            history.RecordFrame(sample);
            historySucceeded = true;
        }
        finally
        {
            _performance.RecordOperation(
                AppPerformanceMetricIds.HistoryRecordFrame,
                historyStarted,
                historySucceeded);
        }
    }

    private CaptureFinalizationContext BuildFinalizationContext(TelemetryCaptureSession? capture)
    {
        return new CaptureFinalizationContext(
            SourceId: _activeSourceId ?? capture?.CaptureId,
            StartedAtUtc: _activeStartedAtUtc ?? capture?.StartedAtUtc,
            DroppedFrameCount: capture?.DroppedFrameCount ?? 0,
            SessionInfoSnapshotCount: capture?.SessionInfoSnapshotCount ?? _sessionInfoSnapshotCount);
    }

    private async Task FinalizeCollectionAsync(
        TelemetryCaptureSession? capture,
        HistoricalSessionAccumulator? history,
        CaptureFinalizationContext finalization)
    {
        var finalizeStarted = Stopwatch.GetTimestamp();
        var finalizeSucceeded = false;
        var parityCompleted = false;
        var overlayDiagnosticsCompleted = false;
        try
        {
            _state.MarkCaptureStopped();
            if (capture is not null)
            {
                var captureFinalizeStarted = Stopwatch.GetTimestamp();
                var captureFinalizeSucceeded = false;
                try
                {
                    await capture.DisposeAsync().ConfigureAwait(false);
                    _logger.LogInformation("Finalized raw capture {CaptureDirectory}.", capture.DirectoryPath);
                    _events.Record("capture_finalized", new Dictionary<string, string?>
                    {
                        ["captureId"] = capture.CaptureId,
                        ["captureDirectory"] = capture.DirectoryPath,
                        ["frameCount"] = capture.FrameCount.ToString(),
                        ["droppedFrameCount"] = capture.DroppedFrameCount.ToString()
                    });
                    captureFinalizeSucceeded = true;
                }
                finally
                {
                    _performance.RecordOperation(
                        AppPerformanceMetricIds.TelemetryFinalizeCapture,
                        captureFinalizeStarted,
                        captureFinalizeSucceeded);
                }
            }

            if (history is not null && finalization.SourceId is not null && finalization.StartedAtUtc is not null)
            {
                HistoricalSessionSummary summary;
                var buildSummaryStarted = Stopwatch.GetTimestamp();
                var buildSummarySucceeded = false;
                try
                {
                    summary = history.BuildSummary(
                        finalization.SourceId,
                        finalization.StartedAtUtc.Value,
                        capture?.FinishedAtUtc ?? DateTimeOffset.UtcNow,
                        capture?.DroppedFrameCount ?? finalization.DroppedFrameCount,
                        capture?.SessionInfoSnapshotCount ?? finalization.SessionInfoSnapshotCount);
                    buildSummarySucceeded = true;
                }
                finally
                {
                    _performance.RecordOperation(
                        AppPerformanceMetricIds.TelemetryFinalizeBuildSummary,
                        buildSummaryStarted,
                        buildSummarySucceeded);
                }

                var saveHistoryStarted = Stopwatch.GetTimestamp();
                var saveHistorySucceeded = false;
                try
                {
                    await _sessionHistoryStore.SaveAsync(summary, CancellationToken.None).ConfigureAwait(false);
                    saveHistorySucceeded = true;
                }
                finally
                {
                    _performance.RecordOperation(
                        AppPerformanceMetricIds.TelemetryFinalizeSaveHistory,
                        saveHistoryStarted,
                        saveHistorySucceeded);
                }

                var saveAnalysisStarted = Stopwatch.GetTimestamp();
                var saveAnalysisSucceeded = false;
                try
                {
                    await _postRaceAnalysisPipeline.SaveFromSummaryAsync(summary, CancellationToken.None).ConfigureAwait(false);
                    saveAnalysisSucceeded = true;
                }
                finally
                {
                    _performance.RecordOperation(
                        AppPerformanceMetricIds.TelemetryFinalizeSaveAnalysis,
                        saveAnalysisStarted,
                        saveAnalysisSucceeded);
                }

                _events.Record("history_summary_saved", new Dictionary<string, string?>
                {
                    ["sourceId"] = finalization.SourceId,
                    ["carKey"] = summary.Combo.CarKey,
                    ["trackKey"] = summary.Combo.TrackKey,
                    ["sessionKey"] = summary.Combo.SessionKey,
                    ["confidence"] = summary.Quality.Confidence
                });
                _logger.LogInformation("Saved session history summary for {SourceId}.", finalization.SourceId);
            }

            var edgeCaseFinalizeStarted = Stopwatch.GetTimestamp();
            var edgeCaseFinalizeSucceeded = false;
            try
            {
                _edgeCaseRecorder.CompleteCollection(capture?.FinishedAtUtc ?? DateTimeOffset.UtcNow);
                edgeCaseFinalizeSucceeded = true;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Failed to save telemetry edge-case artifact.");
            }
            finally
            {
                _performance.RecordOperation(
                    AppPerformanceMetricIds.TelemetryFinalizeEdgeCases,
                    edgeCaseFinalizeStarted,
                    edgeCaseFinalizeSucceeded);
            }

            CompleteLiveOverlayDiagnostics(capture?.DirectoryPath, capture?.FinishedAtUtc ?? DateTimeOffset.UtcNow);
            overlayDiagnosticsCompleted = true;

            if (capture is not null)
            {
                await WritePostSessionArtifactsAsync(
                    capture.DirectoryPath,
                    capture.CaptureId,
                    waitForIRacingExit: _sdk?.IsConnected() != true,
                    CancellationToken.None).ConfigureAwait(false);
            }

            CompleteLiveModelParity(capture?.DirectoryPath, capture?.FinishedAtUtc ?? DateTimeOffset.UtcNow);
            parityCompleted = true;

            finalizeSucceeded = true;
        }
        catch (Exception exception)
        {
            _state.RecordError($"Telemetry collection finalization failed: {exception.Message}");
            _logger.LogError(exception, "Failed to finalize telemetry collection.");
        }
        finally
        {
            if (!parityCompleted)
            {
                CompleteLiveModelParity(capture?.DirectoryPath, capture?.FinishedAtUtc ?? DateTimeOffset.UtcNow);
            }

            if (!overlayDiagnosticsCompleted)
            {
                CompleteLiveOverlayDiagnostics(capture?.DirectoryPath, capture?.FinishedAtUtc ?? DateTimeOffset.UtcNow);
            }

            _performance.RecordOperation(
                AppPerformanceMetricIds.TelemetryFinalizeCollection,
                finalizeStarted,
                finalizeSucceeded);
        }

        CreateEndOfSessionDiagnosticsBundle(finalization);
    }

    private void CompleteLiveModelParity(string? captureDirectory, DateTimeOffset finishedAtUtc)
    {
        try
        {
            _liveModelParityRecorder.CompleteCollection(finishedAtUtc, captureDirectory);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to complete live model parity artifact.");
        }
    }

    private void CompleteLiveOverlayDiagnostics(string? captureDirectory, DateTimeOffset finishedAtUtc)
    {
        try
        {
            _liveOverlayDiagnosticsRecorder.CompleteCollection(finishedAtUtc, captureDirectory);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to complete live overlay diagnostics artifact.");
        }
    }

    private async Task RecoverPendingPostSessionArtifactsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var pendingSynthesis = CaptureSynthesisService.FindPendingSynthesisCaptures(_options.ResolvedCaptureRoot);
            var pendingIbt = _ibtAnalysis.FindPendingAnalysisCaptures(_options.ResolvedCaptureRoot);
            var pendingDirectories = pendingSynthesis
                .Select(item => new PendingArtifactDirectory(
                    item.DirectoryPath,
                    item.CaptureId,
                    item.StartedAtUtc,
                    item.Reason))
                .Concat(pendingIbt.Select(item => new PendingArtifactDirectory(
                    item.DirectoryPath,
                    item.CaptureId,
                    item.StartedAtUtc,
                    item.Reason)))
                .GroupBy(item => item.DirectoryPath, StringComparer.OrdinalIgnoreCase)
                .Select(group => group
                    .OrderBy(item => item.StartedAtUtc ?? DateTimeOffset.MaxValue)
                    .First())
                .OrderBy(item => item.StartedAtUtc ?? DateTimeOffset.MaxValue)
                .ThenBy(item => item.DirectoryPath, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (pendingDirectories.Length == 0)
            {
                return;
            }

            _events.Record("post_session_artifact_recovery_found", new Dictionary<string, string?>
            {
                ["pendingCount"] = pendingDirectories.Length.ToString(),
                ["captureRoot"] = _options.ResolvedCaptureRoot
            });
            _logger.LogInformation(
                "Startup artifact recovery found {PendingCount} raw captures missing capture synthesis or IBT analysis sidecars.",
                pendingDirectories.Length);

            foreach (var pending in pendingDirectories)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await WritePostSessionArtifactsAsync(
                    pending.DirectoryPath,
                    pending.CaptureId,
                    waitForIRacingExit: true,
                    cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Startup recovery is best-effort and may be cancelled during app shutdown.
        }
        catch (Exception exception)
        {
            _events.Record("post_session_artifact_recovery_failed", new Dictionary<string, string?>
            {
                ["captureRoot"] = _options.ResolvedCaptureRoot,
                ["error"] = exception.GetType().Name
            });
            _logger.LogWarning(exception, "Startup post-session artifact recovery failed.");
        }
    }

    private async Task WritePostSessionArtifactsAsync(
        string captureDirectory,
        string? captureId,
        bool waitForIRacingExit,
        CancellationToken cancellationToken)
    {
        await _postSessionArtifactSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!Directory.Exists(captureDirectory))
            {
                return;
            }

            if (!CaptureSynthesisService.HasStableSynthesis(captureDirectory))
            {
                await WriteCaptureSynthesisAsync(captureDirectory, captureId, cancellationToken).ConfigureAwait(false);
            }

            if (_ibtOptions.Enabled && !_ibtAnalysis.HasAnalysisStatus(captureDirectory))
            {
                if (waitForIRacingExit)
                {
                    var ibtReady = await WaitForIRacingExitAsync(captureDirectory, captureId, cancellationToken).ConfigureAwait(false);
                    if (!ibtReady)
                    {
                        return;
                    }
                }
                else if (IsIRacingStillRunning(out var blocker))
                {
                    RecordIbtAnalysisSkippedForRunningSim(captureDirectory, captureId, blocker, "sim_running");
                    return;
                }

                await WriteIbtAnalysisAsync(captureDirectory, captureId, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _postSessionArtifactSemaphore.Release();
        }
    }

    private async Task WriteCaptureSynthesisAsync(
        string captureDirectory,
        string? captureId,
        CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMilliseconds(_options.MaxSynthesisMilliseconds));
            var result = await CaptureSynthesisService.WriteAsync(captureDirectory, timeout.Token).ConfigureAwait(false);
            _events.Record("capture_synthesis_saved", new Dictionary<string, string?>
            {
                ["captureId"] = captureId,
                ["captureDirectory"] = captureDirectory,
                ["synthesisPath"] = result.Path,
                ["stableSynthesisPath"] = result.StablePath,
                ["synthesisBytes"] = result.Bytes.ToString(),
                ["telemetryBytes"] = result.TelemetryBytes.ToString(),
                ["elapsedMilliseconds"] = result.ElapsedMilliseconds.ToString(),
                ["processCpuMilliseconds"] = result.ProcessCpuMilliseconds.ToString(),
                ["totalFrameRecords"] = result.TotalFrameRecords.ToString(),
                ["sampledFrameCount"] = result.SampledFrameCount.ToString(),
                ["sampleStride"] = result.SampleStride.ToString(),
                ["fieldCount"] = result.FieldCount.ToString()
            });
            _logger.LogInformation(
                "Saved capture synthesis {CaptureSynthesisPath} ({CaptureSynthesisBytes} bytes) in {ElapsedMilliseconds} ms from {TelemetryBytes} telemetry bytes.",
                result.Path,
                result.Bytes,
                result.ElapsedMilliseconds,
                result.TelemetryBytes);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _state.RecordWarning("Capture synthesis timed out.");
            _events.Record("capture_synthesis_failed", new Dictionary<string, string?>
            {
                ["captureId"] = captureId,
                ["captureDirectory"] = captureDirectory,
                ["error"] = "Timeout",
                ["maxSynthesisMilliseconds"] = _options.MaxSynthesisMilliseconds.ToString()
            });
            _logger.LogWarning(
                "Capture synthesis timed out after {MaxSynthesisMilliseconds} ms for {CaptureDirectory}.",
                _options.MaxSynthesisMilliseconds,
                captureDirectory);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _state.RecordWarning($"Capture synthesis failed: {exception.Message}");
            _events.Record("capture_synthesis_failed", new Dictionary<string, string?>
            {
                ["captureId"] = captureId,
                ["captureDirectory"] = captureDirectory,
                ["error"] = exception.GetType().Name
            });
            _logger.LogWarning(exception, "Failed to write capture synthesis for {CaptureDirectory}.", captureDirectory);
        }
    }

    private async Task WriteIbtAnalysisAsync(
        string captureDirectory,
        string? captureId,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _ibtAnalysis.WriteAsync(captureDirectory, cancellationToken).ConfigureAwait(false);
            var properties = new Dictionary<string, string?>
            {
                ["captureId"] = captureId,
                ["captureDirectory"] = captureDirectory,
                ["status"] = result.Status,
                ["reason"] = result.Reason,
                ["statusPath"] = result.StatusPath,
                ["outputDirectory"] = result.OutputDirectory,
                ["sourcePath"] = result.SourcePath,
                ["sourceBytes"] = result.SourceBytes?.ToString(),
                ["elapsedMilliseconds"] = result.ElapsedMilliseconds.ToString(),
                ["fieldCount"] = result.FieldCount?.ToString(),
                ["totalRecordCount"] = result.TotalRecordCount?.ToString(),
                ["sampledRecordCount"] = result.SampledRecordCount?.ToString()
            };

            if (string.Equals(result.Status, IbtAnalysisStatus.Succeeded, StringComparison.OrdinalIgnoreCase))
            {
                _events.Record("ibt_analysis_saved", properties);
                _logger.LogInformation(
                    "Saved IBT analysis for {CaptureDirectory} from {IbtPath} in {ElapsedMilliseconds} ms.",
                    captureDirectory,
                    result.SourcePath,
                    result.ElapsedMilliseconds);
                await TryGenerateTrackMapAsync(result.SourcePath, captureId, cancellationToken).ConfigureAwait(false);
                return;
            }

            _events.Record(
                string.Equals(result.Status, IbtAnalysisStatus.Failed, StringComparison.OrdinalIgnoreCase)
                    ? "ibt_analysis_failed"
                    : "ibt_analysis_skipped",
                properties);
            if (string.Equals(result.Status, IbtAnalysisStatus.Failed, StringComparison.OrdinalIgnoreCase))
            {
                _state.RecordWarning($"IBT analysis failed: {result.Reason ?? "unknown"}");
                _logger.LogWarning(
                    "IBT analysis failed for {CaptureDirectory}. Reason: {Reason}.",
                    captureDirectory,
                    result.Reason);
                return;
            }

            _logger.LogInformation(
                "Skipped IBT analysis for {CaptureDirectory}. Reason: {Reason}.",
                captureDirectory,
                result.Reason);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _state.RecordWarning($"IBT analysis failed: {exception.Message}");
            _events.Record("ibt_analysis_failed", new Dictionary<string, string?>
            {
                ["captureId"] = captureId,
                ["captureDirectory"] = captureDirectory,
                ["error"] = exception.GetType().Name
            });
            _logger.LogWarning(exception, "Failed to write IBT analysis for {CaptureDirectory}.", captureDirectory);
        }
    }

    private Task TryGenerateTrackMapAsync(
        string? ibtPath,
        string? captureId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(ibtPath))
        {
            return Task.CompletedTask;
        }

        try
        {
            if (!IsTrackMapGenerationEnabled())
            {
                _events.Record("track_map_generation_skipped", new Dictionary<string, string?>
                {
                    ["captureId"] = captureId,
                    ["sourcePath"] = ibtPath,
                    ["reason"] = "disabled_by_user"
                });
                return Task.CompletedTask;
            }

            var track = _trackMapBuilder.ReadTrackIdentity(ibtPath, cancellationToken);
            if (_trackMapStore.HasCompleteMap(track))
            {
                _events.Record("track_map_generation_skipped", new Dictionary<string, string?>
                {
                    ["captureId"] = captureId,
                    ["sourcePath"] = ibtPath,
                    ["reason"] = "complete_map_already_exists"
                });
                return Task.CompletedTask;
            }

            var build = _trackMapBuilder.BuildFromIbt(ibtPath, captureId, cancellationToken);
            if (build.Document is null)
            {
                _events.Record("track_map_generation_rejected", new Dictionary<string, string?>
                {
                    ["captureId"] = captureId,
                    ["sourcePath"] = ibtPath,
                    ["reasons"] = string.Join(",", build.RejectionReasons)
                });
                return Task.CompletedTask;
            }

            var save = _trackMapStore.SaveIfImproved(build.Document);
            _events.Record(save.Saved ? "track_map_generated" : "track_map_generation_skipped", new Dictionary<string, string?>
            {
                ["captureId"] = captureId,
                ["sourcePath"] = ibtPath,
                ["mapPath"] = save.Path,
                ["reason"] = save.Reason,
                ["confidence"] = build.Document.Quality.Confidence.ToString(),
                ["completeLapCount"] = build.Document.Quality.CompleteLapCount.ToString(),
                ["missingBinCount"] = build.Document.Quality.MissingBinCount.ToString()
            });
            if (save.Saved)
            {
                _logger.LogInformation(
                    "Generated track map {TrackMapPath} from {IbtPath} with {Confidence} confidence.",
                    save.Path,
                    ibtPath,
                    build.Document.Quality.Confidence);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _events.Record("track_map_generation_failed", new Dictionary<string, string?>
            {
                ["captureId"] = captureId,
                ["sourcePath"] = ibtPath,
                ["error"] = exception.GetType().Name
            });
            _logger.LogWarning(exception, "Failed to generate track map from {IbtPath}.", ibtPath);
        }

        return Task.CompletedTask;
    }

    private bool IsTrackMapGenerationEnabled()
    {
        try
        {
            var settings = _settingsStore.Load();
            var trackMap = settings.Overlays.FirstOrDefault(
                overlay => string.Equals(overlay.Id, "track-map", StringComparison.OrdinalIgnoreCase));
            return trackMap?.GetBooleanOption(OverlayOptionKeys.TrackMapBuildFromTelemetry, defaultValue: true) ?? true;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to read track map generation setting. Defaulting to enabled.");
            return true;
        }
    }

    private async Task<bool> WaitForIRacingExitAsync(
        string captureDirectory,
        string? captureId,
        CancellationToken cancellationToken)
    {
        if (!IsIRacingStillRunning(out var blocker))
        {
            return true;
        }

        var timeout = TimeSpan.FromSeconds(_ibtOptions.MaxIRacingExitWaitSeconds);
        if (timeout <= TimeSpan.Zero)
        {
            RecordIbtAnalysisSkippedForRunningSim(captureDirectory, captureId, blocker, "sim_running");
            return false;
        }

        _events.Record("ibt_analysis_waiting_for_iracing_exit", new Dictionary<string, string?>
        {
            ["captureId"] = captureId,
            ["captureDirectory"] = captureDirectory,
            ["blocker"] = blocker,
            ["maxWaitSeconds"] = _ibtOptions.MaxIRacingExitWaitSeconds.ToString()
        });
        _logger.LogInformation(
            "Waiting up to {MaxWaitSeconds} seconds before IBT analysis for {CaptureDirectory} because iRacing is still running: {Blocker}.",
            _ibtOptions.MaxIRacingExitWaitSeconds,
            captureDirectory,
            blocker);

        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var remaining = deadline - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                break;
            }

            await Task.Delay(
                remaining < TimeSpan.FromSeconds(5) ? remaining : TimeSpan.FromSeconds(5),
                cancellationToken).ConfigureAwait(false);

            if (!IsIRacingStillRunning(out blocker))
            {
                return true;
            }
        }

        RecordIbtAnalysisSkippedForRunningSim(captureDirectory, captureId, blocker, "sim_exit_wait_timeout");
        return false;
    }

    private static bool IsIRacingStillRunning(out string reason)
    {
        var running = CaptureSynthesisService.FindRunningIRacingSimProcesses();
        if (running.Count == 0)
        {
            reason = string.Empty;
            return false;
        }

        reason = string.Join(", ", running);
        return true;
    }

    private void RecordIbtAnalysisSkippedForRunningSim(
        string captureDirectory,
        string? captureId,
        string blocker,
        string reason)
    {
        _events.Record("ibt_analysis_skipped_iracing_running", new Dictionary<string, string?>
        {
            ["captureId"] = captureId,
            ["captureDirectory"] = captureDirectory,
            ["blocker"] = blocker,
            ["reason"] = reason
        });
        _logger.LogInformation(
            "Skipped IBT analysis for {CaptureDirectory} because iRacing is still running: {Blocker}. Reason: {Reason}.",
            captureDirectory,
            blocker,
            reason);
    }

    private void CreateEndOfSessionDiagnosticsBundle(CaptureFinalizationContext finalization)
    {
        var diagnosticsStarted = Stopwatch.GetTimestamp();
        var diagnosticsSucceeded = false;
        try
        {
            var bundlePath = _diagnosticsBundleService.CreateBundle(DiagnosticsBundleSources.SessionFinalization);
            _events.Record("diagnostics_bundle_created", new Dictionary<string, string?>
            {
                ["bundlePath"] = bundlePath,
                ["source"] = DiagnosticsBundleSources.SessionFinalization,
                ["sourceId"] = finalization.SourceId
            });
            _logger.LogInformation(
                "Created end-of-session diagnostics bundle {DiagnosticsBundlePath} for {SourceId}.",
                bundlePath,
                finalization.SourceId);
            diagnosticsSucceeded = true;
        }
        catch (Exception exception)
        {
            _events.Record("diagnostics_bundle_failed", new Dictionary<string, string?>
            {
                ["error"] = exception.GetType().Name,
                ["source"] = DiagnosticsBundleSources.SessionFinalization,
                ["sourceId"] = finalization.SourceId
            });
            _logger.LogWarning(
                exception,
                "Failed to create end-of-session diagnostics bundle for {SourceId}.",
                finalization.SourceId);
        }
        finally
        {
            _performance.RecordOperation(
                AppPerformanceMetricIds.TelemetryFinalizeDiagnosticsBundle,
                diagnosticsStarted,
                diagnosticsSucceeded);
        }
    }

    private sealed record CaptureFinalizationContext(
        string? SourceId,
        DateTimeOffset? StartedAtUtc,
        int DroppedFrameCount,
        int SessionInfoSnapshotCount)
    {
        public static CaptureFinalizationContext Empty { get; } = new(null, null, 0, 0);
    }

    private sealed record PendingArtifactDirectory(
        string DirectoryPath,
        string? CaptureId,
        DateTimeOffset? StartedAtUtc,
        string Reason);

    private sealed record FocusCarSelection(
        int? RawCamCarIdx,
        int? FocusCarIdx,
        string? UnavailableReason);

    private sealed record CarProgress(
        int CarIdx,
        int LapCompleted,
        double LapDistPct,
        double? F2TimeSeconds,
        double? EstimatedTimeSeconds,
        double? LastLapTimeSeconds,
        double? BestLapTimeSeconds,
        int? Position,
        int? ClassPosition,
        int? CarClass,
        int? TireCompound)
    {
        public bool HasLapProgress => LapCompleted >= 0 && LapDistPct >= 0d;

        public double TotalLaps => LapCompleted + LapDistPct;
    }
}
