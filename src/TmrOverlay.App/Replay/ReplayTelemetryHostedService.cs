using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TmrOverlay.App.Events;
using TmrOverlay.App.Performance;
using TmrOverlay.App.Telemetry;
using TmrOverlay.Core.Telemetry.Live;

namespace TmrOverlay.App.Replay;

internal sealed class ReplayTelemetryHostedService : IHostedService
{
    private readonly ReplayOptions _options;
    private readonly TelemetryCaptureState _state;
    private readonly ILiveTelemetrySink _liveTelemetrySink;
    private readonly AppPerformanceState _performance;
    private readonly AppEventRecorder _events;
    private readonly ILogger<ReplayTelemetryHostedService> _logger;
    private CancellationTokenSource? _replayCancellation;
    private Task? _replayTask;

    public ReplayTelemetryHostedService(
        ReplayOptions options,
        TelemetryCaptureState state,
        ILiveTelemetrySink liveTelemetrySink,
        AppPerformanceState performance,
        AppEventRecorder events,
        ILogger<ReplayTelemetryHostedService> logger)
    {
        _options = options;
        _state = state;
        _liveTelemetrySink = liveTelemetrySink;
        _performance = performance;
        _events = events;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return Task.CompletedTask;
        }

        if (string.IsNullOrWhiteSpace(_options.CaptureDirectory))
        {
            _logger.LogWarning("Replay mode is enabled, but no Replay:CaptureDirectory was configured.");
            return Task.CompletedTask;
        }

        _replayCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _replayTask = Task.Run(() => RunReplayAsync(_replayCancellation.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _replayCancellation?.Cancel();

        if (_replayTask is not null)
        {
            try
            {
                await _replayTask.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_replayCancellation?.IsCancellationRequested == true)
            {
                // Expected when the host is stopping replay mode.
            }
        }

        _state.MarkDisconnected();
        _liveTelemetrySink.MarkDisconnected();
    }

    private async Task RunReplayAsync(CancellationToken cancellationToken)
    {
        RawCaptureSemanticReplayReader replay;
        try
        {
            replay = RawCaptureSemanticReplayReader.Open(_options.CaptureDirectory!);
        }
        catch (Exception exception)
        {
            _state.RecordError($"Replay capture could not be opened: {exception.Message}");
            _events.Record("replay_failed", new Dictionary<string, string?>
            {
                ["captureDirectory"] = _options.CaptureDirectory,
                ["reason"] = "open_failed",
                ["error"] = exception.Message
            });
            _logger.LogError(exception, "Replay capture could not be opened from {CaptureDirectory}.", _options.CaptureDirectory);
            return;
        }

        _logger.LogInformation(
            "Replay mode started from {CaptureDirectory} with {FrameCount} frames.",
            replay.CaptureDirectory,
            replay.Manifest.FrameCount);
        _events.Record("replay_started", new Dictionary<string, string?>
        {
            ["captureId"] = replay.Manifest.CaptureId,
            ["captureDirectory"] = replay.CaptureDirectory,
            ["frameCount"] = replay.Manifest.FrameCount.ToString()
        });

        try
        {
            _state.SetCaptureRoot(Path.GetDirectoryName(replay.CaptureDirectory) ?? replay.CaptureDirectory);
            _state.SetRawCaptureEnabled(true);
            _state.MarkConnected();
            _liveTelemetrySink.MarkConnected();
            var replayStartedAtUtc = DateTimeOffset.UtcNow;

            _state.MarkCaptureStarted(replay.CaptureDirectory, replayStartedAtUtc);
            _liveTelemetrySink.MarkCollectionStarted(replay.Manifest.CaptureId, replayStartedAtUtc);

            var speedMultiplier = PlaybackSpeedMultiplier(_options.SpeedMultiplier);
            var frameIntervalMs = Math.Max(1, (int)Math.Round(1000d / Math.Max(1, replay.Manifest.TickRate) / speedMultiplier));
            var telemetryFileBytes = ReadTelemetryFileBytes(replay.CaptureDirectory, replay.Manifest.TelemetryFile);
            var replayedFrames = 0;
            double? previousSessionTime = null;

            var replayFilter = _options.ToSemanticFilter();
            foreach (var semanticFrame in replay.ReadFrames(replayFilter, cancellationToken))
            {
                var frame = semanticFrame.Frame;
                await DelayForFrameAsync(frame, previousSessionTime, frameIntervalMs, speedMultiplier, cancellationToken).ConfigureAwait(false);
                previousSessionTime = frame.SessionTime;

                if (semanticFrame.SessionInfoChanged && !string.IsNullOrWhiteSpace(semanticFrame.SessionInfoYaml))
                {
                    _liveTelemetrySink.ApplySessionInfo(semanticFrame.SessionInfoYaml);
                }

                var replayedAtUtc = DateTimeOffset.UtcNow;
                var sample = semanticFrame.Sample with
                {
                    CapturedAtUtc = replayedAtUtc
                };
                _liveTelemetrySink.RecordFrame(sample);
                replayedFrames++;

                _state.RecordFrame(replayedAtUtc);
                _performance.RecordTelemetryFrame(replayedAtUtc);
                var writeStatus = new TelemetryCaptureWriteStatus(
                    TimestampUtc: replayedAtUtc,
                    CaptureId: replay.Manifest.CaptureId,
                    DirectoryPath: replay.CaptureDirectory,
                    FramesWritten: replayedFrames,
                    SessionInfoSnapshotCount: replay.Manifest.SessionInfoSnapshotCount,
                    PendingMessageCount: 0,
                    TelemetryFileBytes: telemetryFileBytes,
                    Exception: null);
                _state.RecordCaptureWrite(writeStatus);
                _performance.RecordCaptureWrite(writeStatus);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected when the host stops while replay is active.
        }
        catch (Exception exception)
        {
            _state.RecordError($"Replay failed: {exception.Message}");
            _events.Record("replay_failed", new Dictionary<string, string?>
            {
                ["captureId"] = replay.Manifest.CaptureId,
                ["captureDirectory"] = replay.CaptureDirectory,
                ["reason"] = "playback_failed",
                ["error"] = exception.Message
            });
            _logger.LogError(exception, "Replay mode failed for {CaptureDirectory}.", replay.CaptureDirectory);
        }
        finally
        {
            _state.MarkCaptureStopped();
            _state.MarkDisconnected();
            _liveTelemetrySink.MarkDisconnected();
            _events.Record("replay_stopped");
            _logger.LogInformation("Replay mode stopped.");
        }
    }

    private async Task DelayForFrameAsync(
        TelemetryFrameEnvelope frame,
        double? previousSessionTime,
        int frameIntervalMs,
        double speedMultiplier,
        CancellationToken cancellationToken)
    {
        if (previousSessionTime is null)
        {
            return;
        }

        var deltaSeconds = frame.SessionTime - previousSessionTime.Value;
        var delayMilliseconds = deltaSeconds is > 0d and < 10d
            ? Math.Max(1, (int)Math.Round(deltaSeconds * 1000d / speedMultiplier))
            : frameIntervalMs;
        await Task.Delay(delayMilliseconds, cancellationToken).ConfigureAwait(false);
    }

    private static double PlaybackSpeedMultiplier(double configuredValue)
    {
        return double.IsFinite(configuredValue) && configuredValue > 0d
            ? configuredValue
            : 1d;
    }

    private static long? ReadTelemetryFileBytes(string captureDirectory, string telemetryFileName)
    {
        var telemetryPath = Path.Combine(captureDirectory, telemetryFileName);
        return File.Exists(telemetryPath)
            ? new FileInfo(telemetryPath).Length
            : null;
    }
}
