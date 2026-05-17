using System.Diagnostics;
using System.Runtime.InteropServices;
using TmrOverlay.App.Telemetry;

namespace TmrOverlay.App.Performance;

internal sealed class AppPerformanceState
{
    private const int RecentSampleCapacity = 512;

    private readonly object _sync = new();
    private readonly DateTimeOffset _startedAtUtc = DateTimeOffset.UtcNow;
    private readonly Dictionary<string, RollingPerformanceMetric> _metrics = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RollingValueMetric> _iracingSystemMetrics = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RollingValueMetric> _overlayUpdateMetrics = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTimeOffset> _lastOverlayRefreshAtUtc = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, OverlayLifecycleDiagnosticState> _lastOverlayLifecycleStates = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, OverlayTimerDiagnosticState> _lastOverlayTimerTicks = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, OverlayWindowDiagnosticSnapshot> _lastOverlayWindowStates = new(StringComparer.OrdinalIgnoreCase);
    private long _telemetryFrameCount;
    private DateTimeOffset? _firstTelemetryFrameAtUtc;
    private DateTimeOffset? _lastTelemetryFrameAtUtc;
    private long _captureWriteStatusCount;
    private string? _lastCaptureId;
    private string? _lastCaptureDirectory;
    private int _lastCaptureFramesWritten;
    private int _lastCaptureSessionInfoSnapshotCount;
    private int _lastCapturePendingMessageCount;
    private long? _lastTelemetryFileBytes;
    private DateTimeOffset? _lastCaptureWriteAtUtc;
    private string? _lastCaptureWriteError;

    public void RecordTelemetryFrame(DateTimeOffset capturedAtUtc)
    {
        lock (_sync)
        {
            _telemetryFrameCount++;
            _firstTelemetryFrameAtUtc ??= capturedAtUtc;
            _lastTelemetryFrameAtUtc = capturedAtUtc;
        }
    }

    public void RecordOperation(string metricId, TimeSpan elapsed, bool succeeded = true)
    {
        if (string.IsNullOrWhiteSpace(metricId))
        {
            return;
        }

        var recordedAtUtc = DateTimeOffset.UtcNow;
        lock (_sync)
        {
            if (!_metrics.TryGetValue(metricId, out var metric))
            {
                metric = new RollingPerformanceMetric(metricId, RecentSampleCapacity);
                _metrics[metricId] = metric;
            }

            metric.Record(elapsed, succeeded, recordedAtUtc);
            RecordOverlayPaintSample(metricId, succeeded, recordedAtUtc);
        }
    }

    public void RecordOperation(string metricId, long startedTimestamp, bool succeeded = true)
    {
        RecordOperation(metricId, Stopwatch.GetElapsedTime(startedTimestamp), succeeded);
    }

    public void RecordIRacingSystemTelemetry(
        DateTimeOffset timestampUtc,
        double? chanQuality,
        double? chanPartnerQuality,
        double? chanLatency,
        double? chanAvgLatency,
        double? chanClockSkew,
        double? frameRate,
        double? cpuUsageForeground,
        double? gpuUsage,
        double? memPageFaultsPerSecond,
        double? memSoftPageFaultsPerSecond,
        double? isReplayPlaying,
        double? isOnTrack)
    {
        lock (_sync)
        {
            RecordIRacingSystemValue(AppPerformanceValueIds.IRacingChanQuality, chanQuality, timestampUtc);
            RecordIRacingSystemValue(AppPerformanceValueIds.IRacingChanPartnerQuality, chanPartnerQuality, timestampUtc);
            RecordIRacingSystemValue(AppPerformanceValueIds.IRacingChanLatency, chanLatency, timestampUtc);
            RecordIRacingSystemValue(AppPerformanceValueIds.IRacingChanAvgLatency, chanAvgLatency, timestampUtc);
            RecordIRacingSystemValue(AppPerformanceValueIds.IRacingChanClockSkew, chanClockSkew, timestampUtc);
            RecordIRacingSystemValue(AppPerformanceValueIds.IRacingFrameRate, frameRate, timestampUtc);
            RecordIRacingSystemValue(AppPerformanceValueIds.IRacingCpuUsageForeground, cpuUsageForeground, timestampUtc);
            RecordIRacingSystemValue(AppPerformanceValueIds.IRacingGpuUsage, gpuUsage, timestampUtc);
            RecordIRacingSystemValue(AppPerformanceValueIds.IRacingMemPageFaultsPerSecond, memPageFaultsPerSecond, timestampUtc);
            RecordIRacingSystemValue(AppPerformanceValueIds.IRacingMemSoftPageFaultsPerSecond, memSoftPageFaultsPerSecond, timestampUtc);
            RecordIRacingSystemValue(AppPerformanceValueIds.IRacingIsReplayPlaying, isReplayPlaying, timestampUtc);
            RecordIRacingSystemValue(AppPerformanceValueIds.IRacingIsOnTrack, isOnTrack, timestampUtc);
        }
    }

    public void RecordOverlayRefreshDecision(
        string overlayId,
        DateTimeOffset timestampUtc,
        long? previousSequence,
        long currentSequence,
        DateTimeOffset? latestInputAtUtc,
        bool applied)
    {
        if (string.IsNullOrWhiteSpace(overlayId))
        {
            return;
        }

        lock (_sync)
        {
            var prefix = $"overlay.{NormalizeMetricSegment(overlayId)}.update";
            if (_lastOverlayRefreshAtUtc.TryGetValue(overlayId, out var previousRefreshAtUtc))
            {
                RecordOverlayUpdateValue(
                    $"{prefix}.refresh_interval_seconds",
                    Math.Max(0d, (timestampUtc - previousRefreshAtUtc).TotalSeconds),
                    timestampUtc);
            }

            _lastOverlayRefreshAtUtc[overlayId] = timestampUtc;

            if (previousSequence is not null)
            {
                var sequenceDelta = Math.Max(0, currentSequence - previousSequence.Value);
                var sequenceUnchanged = currentSequence == previousSequence.Value;
                RecordOverlayUpdateValue($"{prefix}.sequence_delta", sequenceDelta, timestampUtc);
                RecordOverlayUpdateValue($"{prefix}.input_changed", sequenceDelta > 0 ? 1d : 0d, timestampUtc);
                RecordOverlayUpdateValue(
                    $"{prefix}.skipped_unchanged_sequence",
                    !applied && sequenceUnchanged ? 1d : 0d,
                    timestampUtc);
            }

            if (latestInputAtUtc is { } inputAtUtc)
            {
                RecordOverlayUpdateValue(
                    $"{prefix}.input_age_seconds",
                    Math.Max(0d, (timestampUtc - inputAtUtc).TotalSeconds),
                    timestampUtc);
            }

            RecordOverlayUpdateValue($"{prefix}.applied", applied ? 1d : 0d, timestampUtc);
            RecordOverlayUpdateValue($"{prefix}.skipped", applied ? 0d : 1d, timestampUtc);
        }
    }

    public void RecordOverlayTimerTick(
        string overlayId,
        int intervalMilliseconds,
        bool visible,
        bool pauseEligible)
    {
        if (string.IsNullOrWhiteSpace(overlayId))
        {
            return;
        }

        var timestampUtc = DateTimeOffset.UtcNow;
        var safeInterval = Math.Max(0, intervalMilliseconds);
        lock (_sync)
        {
            var prefix = $"overlay.{NormalizeMetricSegment(overlayId)}.timer";
            if (_lastOverlayTimerTicks.TryGetValue(overlayId, out var previousTick))
            {
                var elapsedMilliseconds = Math.Max(0d, (timestampUtc - previousTick.LastTickAtUtc).TotalMilliseconds);
                var lateMilliseconds = Math.Max(0d, elapsedMilliseconds - safeInterval);
                var lateThresholdMilliseconds = Math.Max(250d, safeInterval * 0.5d);
                RecordOverlayUpdateValue($"{prefix}.elapsed_ms", elapsedMilliseconds, timestampUtc);
                RecordOverlayUpdateValue($"{prefix}.late_ms", lateMilliseconds, timestampUtc);
                RecordOverlayUpdateValue($"{prefix}.late", lateMilliseconds > lateThresholdMilliseconds ? 1d : 0d, timestampUtc);
            }

            _lastOverlayTimerTicks[overlayId] = new OverlayTimerDiagnosticState(
                safeInterval,
                visible,
                pauseEligible,
                timestampUtc);
            RecordOverlayUpdateValue($"{prefix}.tick", 1d, timestampUtc);
            RecordOverlayUpdateValue($"{prefix}.interval_ms", safeInterval, timestampUtc);
            RecordOverlayUpdateValue($"{prefix}.visible", visible ? 1d : 0d, timestampUtc);
            RecordOverlayUpdateValue($"{prefix}.pause_eligible", pauseEligible ? 1d : 0d, timestampUtc);

            var cadencePrefix = $"overlay.timer.cadence.{safeInterval}ms";
            RecordOverlayUpdateValue($"{cadencePrefix}.tick", 1d, timestampUtc);
            RecordOverlayUpdateValue($"{cadencePrefix}.visible", visible ? 1d : 0d, timestampUtc);
            RecordOverlayUpdateValue($"{cadencePrefix}.pause_eligible", pauseEligible ? 1d : 0d, timestampUtc);
        }
    }

    public void RecordOverlaySettingsMutation(
        string settingName,
        DateTimeOffset timestampUtc,
        int existingFormCount,
        int updatedFormCount,
        int recreatedFormCount)
    {
        if (string.IsNullOrWhiteSpace(settingName))
        {
            return;
        }

        var normalizedSettingName = NormalizeMetricSegment(settingName);
        lock (_sync)
        {
            var prefix = $"overlay.manager.settings.{normalizedSettingName}";
            RecordOverlayUpdateValue($"{prefix}.changed", 1d, timestampUtc);
            RecordOverlayUpdateValue($"{prefix}.existing_form_count", Math.Max(0, existingFormCount), timestampUtc);
            RecordOverlayUpdateValue($"{prefix}.updated_form_count", Math.Max(0, updatedFormCount), timestampUtc);
            RecordOverlayUpdateValue($"{prefix}.recreated_form_count", Math.Max(0, recreatedFormCount), timestampUtc);
        }
    }

    public void RecordOverlayLifecycleState(
        string overlayId,
        DateTimeOffset timestampUtc,
        bool enabled,
        bool sessionAllowed,
        bool settingsPreview,
        bool desiredVisible,
        bool actualVisible,
        bool hasForm,
        bool liveTelemetryAvailable,
        bool contextAvailable,
        double fadeAlpha,
        bool fadesWhenLiveTelemetryUnavailable,
        bool pauseEligible)
    {
        if (string.IsNullOrWhiteSpace(overlayId))
        {
            return;
        }

        lock (_sync)
        {
            var normalizedOverlayId = NormalizeMetricSegment(overlayId);
            var prefix = $"overlay.{normalizedOverlayId}.lifecycle";
            var clampedFadeAlpha = Math.Clamp(fadeAlpha, 0d, 1d);
            var fadedUnavailable = fadesWhenLiveTelemetryUnavailable
                && !liveTelemetryAvailable
                && clampedFadeAlpha <= 0.01d;
            var current = new OverlayLifecycleDiagnosticState(
                enabled,
                sessionAllowed,
                settingsPreview,
                desiredVisible,
                actualVisible,
                hasForm,
                liveTelemetryAvailable,
                contextAvailable,
                fadedUnavailable,
                pauseEligible);
            _lastOverlayLifecycleStates.TryGetValue(overlayId, out var previous);

            RecordOverlayUpdateValue($"{prefix}.enabled", enabled ? 1d : 0d, timestampUtc);
            RecordOverlayUpdateValue($"{prefix}.session_allowed", sessionAllowed ? 1d : 0d, timestampUtc);
            RecordOverlayUpdateValue($"{prefix}.settings_preview", settingsPreview ? 1d : 0d, timestampUtc);
            RecordOverlayUpdateValue($"{prefix}.desired_visible", desiredVisible ? 1d : 0d, timestampUtc);
            RecordOverlayUpdateValue($"{prefix}.actual_visible", actualVisible ? 1d : 0d, timestampUtc);
            RecordOverlayUpdateValue($"{prefix}.has_form", hasForm ? 1d : 0d, timestampUtc);
            RecordOverlayUpdateValue($"{prefix}.live_available", liveTelemetryAvailable ? 1d : 0d, timestampUtc);
            RecordOverlayUpdateValue($"{prefix}.context_available", contextAvailable ? 1d : 0d, timestampUtc);
            RecordOverlayUpdateValue($"{prefix}.fade_alpha", clampedFadeAlpha, timestampUtc);
            RecordOverlayUpdateValue($"{prefix}.faded_unavailable", fadedUnavailable ? 1d : 0d, timestampUtc);
            RecordOverlayUpdateValue($"{prefix}.hidden_by_settings", !settingsPreview && !enabled ? 1d : 0d, timestampUtc);
            RecordOverlayUpdateValue($"{prefix}.hidden_by_session", !settingsPreview && enabled && !sessionAllowed ? 1d : 0d, timestampUtc);
            RecordOverlayUpdateValue($"{prefix}.hidden_by_context", !settingsPreview && enabled && sessionAllowed && !contextAvailable ? 1d : 0d, timestampUtc);
            RecordOverlayUpdateValue($"{prefix}.hidden_or_faded", !actualVisible || fadedUnavailable ? 1d : 0d, timestampUtc);
            RecordOverlayUpdateValue($"{prefix}.pause_eligible", pauseEligible ? 1d : 0d, timestampUtc);
            RecordOverlayUpdateValue($"{prefix}.transition", previous is not null && !previous.Equals(current) ? 1d : 0d, timestampUtc);
            RecordOverlayUpdateValue(
                $"{prefix}.visibility_transition",
                previous is not null && previous.ActualVisible != actualVisible ? 1d : 0d,
                timestampUtc);
            RecordOverlayUpdateValue(
                $"{prefix}.pause_eligible_transition",
                previous is not null && previous.PauseEligible != pauseEligible ? 1d : 0d,
                timestampUtc);

            _lastOverlayLifecycleStates[overlayId] = current;
        }
    }

    public void RecordOverlayWindowState(
        string overlayId,
        DateTimeOffset timestampUtc,
        bool actualVisible,
        bool topMost,
        bool alwaysOnTopSetting,
        bool inputTransparent,
        bool noActivate,
        bool settingsOverlayActive,
        bool settingsWindowVisible,
        bool intersectsSettingsWindow,
        bool settingsWindowInputProtected,
        int x,
        int y,
        int width,
        int height,
        double opacity)
    {
        if (string.IsNullOrWhiteSpace(overlayId))
        {
            return;
        }

        lock (_sync)
        {
            var normalizedOverlayId = NormalizeMetricSegment(overlayId);
            var prefix = $"overlay.{normalizedOverlayId}.window";
            var clampedOpacity = Math.Clamp(opacity, 0d, 1d);
            var effectiveSettingsOverlayActive = settingsOverlayActive && settingsWindowVisible;
            var inputInterceptRisk = actualVisible
                && !inputTransparent
                && (effectiveSettingsOverlayActive
                    || settingsWindowInputProtected
                    || intersectsSettingsWindow
                    || (settingsWindowVisible && topMost && noActivate)
                    || clampedOpacity <= 0.01d);
            RecordOverlayUpdateValue($"{prefix}.visible", actualVisible ? 1d : 0d, timestampUtc);
            RecordOverlayUpdateValue($"{prefix}.top_most", topMost ? 1d : 0d, timestampUtc);
            RecordOverlayUpdateValue($"{prefix}.always_on_top_setting", alwaysOnTopSetting ? 1d : 0d, timestampUtc);
            RecordOverlayUpdateValue($"{prefix}.input_transparent", inputTransparent ? 1d : 0d, timestampUtc);
            RecordOverlayUpdateValue($"{prefix}.no_activate", noActivate ? 1d : 0d, timestampUtc);
            RecordOverlayUpdateValue($"{prefix}.settings_overlay_active", effectiveSettingsOverlayActive ? 1d : 0d, timestampUtc);
            RecordOverlayUpdateValue($"{prefix}.settings_window_visible", settingsWindowVisible ? 1d : 0d, timestampUtc);
            RecordOverlayUpdateValue($"{prefix}.settings_window_intersects", intersectsSettingsWindow ? 1d : 0d, timestampUtc);
            RecordOverlayUpdateValue($"{prefix}.settings_window_input_protected", settingsWindowInputProtected ? 1d : 0d, timestampUtc);
            RecordOverlayUpdateValue($"{prefix}.input_intercept_risk", inputInterceptRisk ? 1d : 0d, timestampUtc);
            RecordOverlayUpdateValue($"{prefix}.opacity", clampedOpacity, timestampUtc);
            RecordOverlayUpdateValue($"{prefix}.x", x, timestampUtc);
            RecordOverlayUpdateValue($"{prefix}.y", y, timestampUtc);
            RecordOverlayUpdateValue($"{prefix}.width", Math.Max(0, width), timestampUtc);
            RecordOverlayUpdateValue($"{prefix}.height", Math.Max(0, height), timestampUtc);

            _lastOverlayWindowStates[overlayId] = new OverlayWindowDiagnosticSnapshot(
                OverlayId: overlayId,
                TimestampUtc: timestampUtc,
                Visible: actualVisible,
                TopMost: topMost,
                AlwaysOnTopSetting: alwaysOnTopSetting,
                InputTransparent: inputTransparent,
                NoActivate: noActivate,
                SettingsOverlayActive: effectiveSettingsOverlayActive,
                SettingsWindowVisible: settingsWindowVisible,
                SettingsWindowIntersects: intersectsSettingsWindow,
                SettingsWindowInputProtected: settingsWindowInputProtected,
                InputInterceptRisk: inputInterceptRisk,
                X: x,
                Y: y,
                Width: Math.Max(0, width),
                Height: Math.Max(0, height),
                Opacity: Math.Round(clampedOpacity, 3));
        }
    }

    public void RecordSettingsSaveApplyQueued(int coalescedRequestCount, bool timerAlreadyPending)
    {
        var timestampUtc = DateTimeOffset.UtcNow;
        lock (_sync)
        {
            RecordOverlayUpdateValue("overlay.settings.apply.queued", 1d, timestampUtc);
            RecordOverlayUpdateValue("overlay.settings.apply.coalesced_request_count", Math.Max(1, coalescedRequestCount), timestampUtc);
            RecordOverlayUpdateValue("overlay.settings.apply.timer_already_pending", timerAlreadyPending ? 1d : 0d, timestampUtc);
        }
    }

    public void RecordSettingsSaveApplyFlushed(int coalescedRequestCount, TimeSpan queuedFor, bool succeeded)
    {
        var timestampUtc = DateTimeOffset.UtcNow;
        lock (_sync)
        {
            RecordOverlayUpdateValue("overlay.settings.apply.flush", 1d, timestampUtc);
            RecordOverlayUpdateValue("overlay.settings.apply.flush_success", succeeded ? 1d : 0d, timestampUtc);
            RecordOverlayUpdateValue("overlay.settings.apply.flushed_request_count", Math.Max(1, coalescedRequestCount), timestampUtc);
            RecordOverlayUpdateValue("overlay.settings.apply.queued_ms", Math.Max(0d, queuedFor.TotalMilliseconds), timestampUtc);
        }
    }

    public void RecordLocalhostActivity(
        DateTimeOffset timestampUtc,
        bool enabled,
        bool listening,
        long totalRequests,
        long failedRequests,
        bool hasRecentRequests,
        double? lastRequestAgeSeconds)
    {
        lock (_sync)
        {
            RecordOverlayUpdateValue("localhost.enabled", enabled ? 1d : 0d, timestampUtc);
            RecordOverlayUpdateValue("localhost.listening", listening ? 1d : 0d, timestampUtc);
            RecordOverlayUpdateValue("localhost.total_requests", Math.Max(0, totalRequests), timestampUtc);
            RecordOverlayUpdateValue("localhost.failed_requests", Math.Max(0, failedRequests), timestampUtc);
            RecordOverlayUpdateValue("localhost.has_recent_requests", hasRecentRequests ? 1d : 0d, timestampUtc);
            RecordOverlayUpdateValue("localhost.idle_no_recent_requests", enabled && listening && !hasRecentRequests ? 1d : 0d, timestampUtc);
            RecordOverlayUpdateValue("localhost.last_request_age_seconds", lastRequestAgeSeconds, timestampUtc);
        }
    }

    public void RecordLocalhostRequest(
        string route,
        int statusCode,
        TimeSpan elapsed,
        bool succeeded)
    {
        var timestampUtc = DateTimeOffset.UtcNow;
        var routeSegment = NormalizeMetricSegment(string.IsNullOrWhiteSpace(route) ? "unknown" : route);
        lock (_sync)
        {
            var prefix = $"localhost.request.route.{routeSegment}";
            RecordOverlayUpdateValue($"{prefix}.tick", 1d, timestampUtc);
            RecordOverlayUpdateValue($"{prefix}.success", succeeded ? 1d : 0d, timestampUtc);
            RecordOverlayUpdateValue($"{prefix}.status_code", statusCode, timestampUtc);
            RecordOverlayUpdateValue($"{prefix}.duration_ms", Math.Max(0d, elapsed.TotalMilliseconds), timestampUtc);
        }
    }

    public void RecordOverlayLiveTelemetryState(
        string overlayId,
        DateTimeOffset timestampUtc,
        bool liveTelemetryAvailable,
        double fadeAlpha)
    {
        if (string.IsNullOrWhiteSpace(overlayId))
        {
            return;
        }

        lock (_sync)
        {
            var prefix = $"overlay.{NormalizeMetricSegment(overlayId)}.update";
            RecordOverlayUpdateValue($"{prefix}.live_available", liveTelemetryAvailable ? 1d : 0d, timestampUtc);
            RecordOverlayUpdateValue($"{prefix}.fade_alpha", Math.Clamp(fadeAlpha, 0d, 1d), timestampUtc);
        }
    }

    public void RecordCaptureWrite(TelemetryCaptureWriteStatus writeStatus)
    {
        lock (_sync)
        {
            _captureWriteStatusCount++;
            _lastCaptureId = writeStatus.CaptureId;
            _lastCaptureDirectory = writeStatus.DirectoryPath;
            _lastCaptureFramesWritten = writeStatus.FramesWritten;
            _lastCaptureSessionInfoSnapshotCount = writeStatus.SessionInfoSnapshotCount;
            _lastCapturePendingMessageCount = writeStatus.PendingMessageCount;
            _lastTelemetryFileBytes = writeStatus.TelemetryFileBytes ?? _lastTelemetryFileBytes;
            _lastCaptureWriteAtUtc = writeStatus.TimestampUtc;
            _lastCaptureWriteError = writeStatus.Exception?.Message;

            if (writeStatus.LastWriteDuration is { } duration)
            {
                if (!_metrics.TryGetValue(AppPerformanceMetricIds.CaptureWriterWrite, out var metric))
                {
                    metric = new RollingPerformanceMetric(AppPerformanceMetricIds.CaptureWriterWrite, RecentSampleCapacity);
                    _metrics[AppPerformanceMetricIds.CaptureWriterWrite] = metric;
                }

                metric.Record(duration, writeStatus.Exception is null, writeStatus.TimestampUtc);
            }
        }
    }

    public AppPerformanceSnapshot Snapshot()
    {
        lock (_sync)
        {
            var timestampUtc = DateTimeOffset.UtcNow;
            RecordOverlayTimerSummary(timestampUtc);
            var metrics = _metrics
                .Values
                .Select(metric => metric.Snapshot())
                .OrderBy(metric => metric.Id, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return new AppPerformanceSnapshot(
                TimestampUtc: timestampUtc,
                StartedAtUtc: _startedAtUtc,
                TelemetryFrameCount: _telemetryFrameCount,
                TelemetryFramesPerSecond: CalculateTelemetryFramesPerSecond(),
                Metrics: metrics,
                IRacingSystem: _iracingSystemMetrics
                    .Values
                    .Select(metric => metric.Snapshot())
                    .OrderBy(metric => metric.Id, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                OverlayUpdates: _overlayUpdateMetrics
                    .Values
                    .Select(metric => metric.Snapshot())
                    .OrderBy(metric => metric.Id, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                OverlayWindows: _lastOverlayWindowStates
                    .Values
                    .OrderBy(window => window.OverlayId, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                Capture: new CapturePerformanceSnapshot(
                    WriteStatusCount: _captureWriteStatusCount,
                    LastCaptureId: _lastCaptureId,
                    LastCaptureDirectory: _lastCaptureDirectory,
                    LastFramesWritten: _lastCaptureFramesWritten,
                    LastSessionInfoSnapshotCount: _lastCaptureSessionInfoSnapshotCount,
                    LastPendingMessageCount: _lastCapturePendingMessageCount,
                    LastTelemetryFileBytes: _lastTelemetryFileBytes,
                    LastWriteAtUtc: _lastCaptureWriteAtUtc,
                    LastWriteError: _lastCaptureWriteError),
                Process: ProcessPerformanceSnapshot.Capture());
        }
    }

    private double CalculateTelemetryFramesPerSecond()
    {
        if (_telemetryFrameCount < 2 || _firstTelemetryFrameAtUtc is null || _lastTelemetryFrameAtUtc is null)
        {
            return 0d;
        }

        var elapsedSeconds = (_lastTelemetryFrameAtUtc.Value - _firstTelemetryFrameAtUtc.Value).TotalSeconds;
        return elapsedSeconds <= 0d
            ? 0d
            : Math.Round((_telemetryFrameCount - 1) / elapsedSeconds, 2);
    }

    private void RecordIRacingSystemValue(string id, double? value, DateTimeOffset timestampUtc)
    {
        if (value is null || double.IsNaN(value.Value) || double.IsInfinity(value.Value))
        {
            return;
        }

        if (!_iracingSystemMetrics.TryGetValue(id, out var metric))
        {
            metric = new RollingValueMetric(id, RecentSampleCapacity);
            _iracingSystemMetrics[id] = metric;
        }

        metric.Record(value.Value, timestampUtc);
    }

    private void RecordOverlayUpdateValue(string id, double? value, DateTimeOffset timestampUtc)
    {
        if (value is null || double.IsNaN(value.Value) || double.IsInfinity(value.Value))
        {
            return;
        }

        if (!_overlayUpdateMetrics.TryGetValue(id, out var metric))
        {
            metric = new RollingValueMetric(id, RecentSampleCapacity);
            _overlayUpdateMetrics[id] = metric;
        }

        metric.Record(value.Value, timestampUtc);
    }

    private void RecordOverlayTimerSummary(DateTimeOffset timestampUtc)
    {
        var activeTimers = _lastOverlayTimerTicks
            .Values
            .Where(timer => IsTimerActive(timer, timestampUtc))
            .ToArray();
        RecordOverlayUpdateValue("overlay.timer.active_count", activeTimers.Length, timestampUtc);
        RecordOverlayUpdateValue("overlay.timer.visible_active_count", activeTimers.Count(timer => timer.Visible), timestampUtc);
        RecordOverlayUpdateValue("overlay.timer.pause_eligible_active_count", activeTimers.Count(timer => timer.PauseEligible), timestampUtc);

        foreach (var group in activeTimers.GroupBy(timer => timer.IntervalMilliseconds).OrderBy(group => group.Key))
        {
            var prefix = $"overlay.timer.cadence.{group.Key}ms";
            RecordOverlayUpdateValue($"{prefix}.active_count", group.Count(), timestampUtc);
            RecordOverlayUpdateValue($"{prefix}.visible_active_count", group.Count(timer => timer.Visible), timestampUtc);
            RecordOverlayUpdateValue($"{prefix}.pause_eligible_active_count", group.Count(timer => timer.PauseEligible), timestampUtc);
        }
    }

    private static bool IsTimerActive(OverlayTimerDiagnosticState timer, DateTimeOffset timestampUtc)
    {
        var activeWindow = TimeSpan.FromMilliseconds(Math.Max(timer.IntervalMilliseconds * 3, 1500));
        return timestampUtc - timer.LastTickAtUtc <= activeWindow;
    }

    private void RecordOverlayPaintSample(string metricId, bool succeeded, DateTimeOffset timestampUtc)
    {
        const string overlayPrefix = "overlay.";
        const string paintSuffix = ".paint";
        if (!metricId.StartsWith(overlayPrefix, StringComparison.OrdinalIgnoreCase)
            || !metricId.EndsWith(paintSuffix, StringComparison.OrdinalIgnoreCase)
            || metricId.Length <= overlayPrefix.Length + paintSuffix.Length)
        {
            return;
        }

        var overlayId = metricId.Substring(
            overlayPrefix.Length,
            metricId.Length - overlayPrefix.Length - paintSuffix.Length);
        if (string.IsNullOrWhiteSpace(overlayId))
        {
            return;
        }

        var prefix = $"overlay.{NormalizeMetricSegment(overlayId)}.paint";
        RecordOverlayUpdateValue($"{prefix}.sample", 1d, timestampUtc);
        RecordOverlayUpdateValue($"{prefix}.success", succeeded ? 1d : 0d, timestampUtc);
    }

    private static string NormalizeMetricSegment(string value)
    {
        return value.Trim().Replace('-', '_').Replace(' ', '_').ToLowerInvariant();
    }

    private sealed class RollingPerformanceMetric
    {
        private readonly string _id;
        private readonly double[] _recentMilliseconds;
        private int _nextSampleIndex;
        private int _recentSampleCount;
        private long _count;
        private long _errorCount;
        private double _totalMilliseconds;
        private double _lastMilliseconds;
        private double _maxMilliseconds;
        private DateTimeOffset? _lastRecordedAtUtc;

        public RollingPerformanceMetric(string id, int sampleCapacity)
        {
            _id = id;
            _recentMilliseconds = new double[sampleCapacity];
        }

        public void Record(TimeSpan elapsed, bool succeeded, DateTimeOffset timestampUtc)
        {
            var milliseconds = Math.Max(0d, elapsed.TotalMilliseconds);
            _count++;
            if (!succeeded)
            {
                _errorCount++;
            }

            _totalMilliseconds += milliseconds;
            _lastMilliseconds = milliseconds;
            _maxMilliseconds = Math.Max(_maxMilliseconds, milliseconds);
            _lastRecordedAtUtc = timestampUtc;

            _recentMilliseconds[_nextSampleIndex] = milliseconds;
            _nextSampleIndex = (_nextSampleIndex + 1) % _recentMilliseconds.Length;
            _recentSampleCount = Math.Min(_recentSampleCount + 1, _recentMilliseconds.Length);
        }

        public PerformanceMetricSnapshot Snapshot()
        {
            var recent = new double[_recentSampleCount];
            Array.Copy(_recentMilliseconds, recent, _recentSampleCount);
            Array.Sort(recent);
            var p95 = recent.Length == 0
                ? 0d
                : recent[Math.Clamp((int)Math.Ceiling(recent.Length * 0.95d) - 1, 0, recent.Length - 1)];

            return new PerformanceMetricSnapshot(
                Id: _id,
                Count: _count,
                ErrorCount: _errorCount,
                AverageMilliseconds: _count == 0 ? 0d : Math.Round(_totalMilliseconds / _count, 3),
                LastMilliseconds: Math.Round(_lastMilliseconds, 3),
                MaxMilliseconds: Math.Round(_maxMilliseconds, 3),
                P95Milliseconds: Math.Round(p95, 3),
                LastRecordedAtUtc: _lastRecordedAtUtc);
        }
    }

    private sealed class RollingValueMetric
    {
        private readonly string _id;
        private readonly double[] _recentValues;
        private int _nextSampleIndex;
        private int _recentSampleCount;
        private long _count;
        private double _total;
        private double _last;
        private double _minimum = double.PositiveInfinity;
        private double _maximum = double.NegativeInfinity;
        private DateTimeOffset? _lastRecordedAtUtc;

        public RollingValueMetric(string id, int sampleCapacity)
        {
            _id = id;
            _recentValues = new double[sampleCapacity];
        }

        public void Record(double value, DateTimeOffset timestampUtc)
        {
            _count++;
            _total += value;
            _last = value;
            _minimum = Math.Min(_minimum, value);
            _maximum = Math.Max(_maximum, value);
            _lastRecordedAtUtc = timestampUtc;

            _recentValues[_nextSampleIndex] = value;
            _nextSampleIndex = (_nextSampleIndex + 1) % _recentValues.Length;
            _recentSampleCount = Math.Min(_recentSampleCount + 1, _recentValues.Length);
        }

        public PerformanceValueSnapshot Snapshot()
        {
            var recent = new double[_recentSampleCount];
            Array.Copy(_recentValues, recent, _recentSampleCount);
            Array.Sort(recent);

            return new PerformanceValueSnapshot(
                Id: _id,
                Count: _count,
                Average: _count == 0 ? null : Round(_total / _count),
                Last: _count == 0 ? null : Round(_last),
                Minimum: _count == 0 ? null : Round(_minimum),
                Maximum: _count == 0 ? null : Round(_maximum),
                P05: Percentile(recent, 0.05d),
                P50: Percentile(recent, 0.50d),
                P95: Percentile(recent, 0.95d),
                LastRecordedAtUtc: _lastRecordedAtUtc);
        }

        private static double? Percentile(double[] sortedValues, double percentile)
        {
            if (sortedValues.Length == 0)
            {
                return null;
            }

            var index = Math.Clamp((int)Math.Ceiling(sortedValues.Length * percentile) - 1, 0, sortedValues.Length - 1);
            return Round(sortedValues[index]);
        }

        private static double Round(double value)
        {
            return Math.Round(value, 6);
        }
    }

    private sealed record OverlayLifecycleDiagnosticState(
        bool Enabled,
        bool SessionAllowed,
        bool SettingsPreview,
        bool DesiredVisible,
        bool ActualVisible,
        bool HasForm,
        bool LiveTelemetryAvailable,
        bool ContextAvailable,
        bool FadedUnavailable,
        bool PauseEligible);

    private sealed record OverlayTimerDiagnosticState(
        int IntervalMilliseconds,
        bool Visible,
        bool PauseEligible,
        DateTimeOffset LastTickAtUtc);
}

internal static class AppPerformanceMetricIds
{
    public const string TelemetryDataChanged = "telemetry.data_changed";
    public const string TelemetryDataChangedGetCollection = "telemetry.data_changed.get_collection";
    public const string TelemetryDataChangedSessionInfoVersion = "telemetry.data_changed.session_info_version";
    public const string TelemetryDataChangedSessionInfoSnapshot = "telemetry.data_changed.session_info_snapshot";
    public const string TelemetryDataChangedRawCaptureFrame = "telemetry.data_changed.raw_capture_frame";
    public const string TelemetryDataChangedHistoricalFrame = "telemetry.data_changed.historical_frame";
    public const string TelemetryDataChangedStateFrame = "telemetry.data_changed.state_frame";
    public const string LiveTelemetrySink = "telemetry.live_sink";
    public const string HistoryRecordFrame = "telemetry.history_record_frame";
    public const string TelemetryEdgeCaseRecordFrame = "telemetry.edge_cases.record_frame";
    public const string TelemetryHistoryBuildSample = "telemetry.history.build_sample";
    public const string TelemetryHistoryReadLeader = "telemetry.history.read_leader";
    public const string TelemetryHistoryReadClassLeader = "telemetry.history.read_class_leader";
    public const string TelemetryHistoryReadNearbyCars = "telemetry.history.read_nearby_cars";
    public const string TelemetryHistoryReadClassCars = "telemetry.history.read_class_cars";
    public const string TelemetrySessionInfoRead = "telemetry.session_info.read";
    public const string TelemetrySessionInfoApply = "telemetry.session_info.apply";
    public const string TelemetrySessionInfoCaptureQueue = "telemetry.session_info.capture_queue";
    public const string TelemetryRawFrameRead = "telemetry.raw_frame.read";
    public const string TelemetryRawFrameQueue = "telemetry.raw_frame.queue";
    public const string TelemetryFinalizeCollection = "telemetry.finalize_collection";
    public const string TelemetryFinalizeCapture = "telemetry.finalize_capture";
    public const string TelemetryFinalizeBuildSummary = "telemetry.finalize.build_summary";
    public const string TelemetryFinalizeSaveHistory = "telemetry.finalize.save_history";
    public const string TelemetryFinalizeSaveAnalysis = "telemetry.finalize.save_analysis";
    public const string TelemetryFinalizeEdgeCases = "telemetry.finalize.edge_cases";
    public const string TelemetryFinalizeDiagnosticsBundle = "telemetry.finalize.diagnostics_bundle";
    public const string CaptureWriterWrite = "capture.writer_write";
    public const string CaptureWriteStatusCallback = "capture.write_status_callback";
    public const string OverlayStatusRefresh = "overlay.status.refresh";
    public const string OverlayStatusSnapshot = "overlay.status.snapshot";
    public const string OverlayStatusHealth = "overlay.status.health";
    public const string OverlayStatusApplyUi = "overlay.status.apply_ui";
    public const string OverlayStatusPaint = "overlay.status.paint";
    public const string OverlayFuelRefresh = "overlay.fuel.refresh";
    public const string OverlayFuelSnapshot = "overlay.fuel.snapshot";
    public const string OverlayFuelHistoryLookup = "overlay.fuel.history_lookup";
    public const string OverlayFuelStrategy = "overlay.fuel.strategy";
    public const string OverlayFuelViewModel = "overlay.fuel.view_model";
    public const string OverlayFuelApplyUi = "overlay.fuel.apply_ui";
    public const string OverlayFuelRows = "overlay.fuel.rows";
    public const string OverlayFuelPaint = "overlay.fuel.paint";
    public const string OverlayRelativeRefresh = "overlay.relative.refresh";
    public const string OverlayRelativeSnapshot = "overlay.relative.snapshot";
    public const string OverlayRelativeViewModel = "overlay.relative.view_model";
    public const string OverlayRelativeApplyUi = "overlay.relative.apply_ui";
    public const string OverlayRelativeRows = "overlay.relative.rows";
    public const string OverlayRelativePaint = "overlay.relative.paint";
    public const string OverlayStandingsRefresh = "overlay.standings.refresh";
    public const string OverlayStandingsSnapshot = "overlay.standings.snapshot";
    public const string OverlayStandingsViewModel = "overlay.standings.view_model";
    public const string OverlayStandingsApplyUi = "overlay.standings.apply_ui";
    public const string OverlayStandingsPaint = "overlay.standings.paint";
    public const string OverlayTrackMapRefresh = "overlay.track_map.refresh";
    public const string OverlayTrackMapSnapshot = "overlay.track_map.snapshot";
    public const string OverlayTrackMapPaint = "overlay.track_map.paint";
    public const string OverlayStreamChatSettingsRefresh = "overlay.stream_chat.settings_refresh";
    public const string OverlayStreamChatConnect = "overlay.stream_chat.connect";
    public const string OverlayStreamChatPaint = "overlay.stream_chat.paint";
    public const string OverlayFlagsRefresh = "overlay.flags.refresh";
    public const string OverlayFlagsSnapshot = "overlay.flags.snapshot";
    public const string OverlayFlagsViewModel = "overlay.flags.view_model";
    public const string OverlayFlagsApplyUi = "overlay.flags.apply_ui";
    public const string OverlayFlagsRows = "overlay.flags.rows";
    public const string OverlayFlagsPaint = "overlay.flags.paint";
    public const string OverlaySessionWeatherRefresh = "overlay.session_weather.refresh";
    public const string OverlaySessionWeatherSnapshot = "overlay.session_weather.snapshot";
    public const string OverlaySessionWeatherViewModel = "overlay.session_weather.view_model";
    public const string OverlaySessionWeatherApplyUi = "overlay.session_weather.apply_ui";
    public const string OverlaySessionWeatherRows = "overlay.session_weather.rows";
    public const string OverlaySessionWeatherPaint = "overlay.session_weather.paint";
    public const string OverlayPitServiceRefresh = "overlay.pit_service.refresh";
    public const string OverlayPitServiceSnapshot = "overlay.pit_service.snapshot";
    public const string OverlayPitServiceViewModel = "overlay.pit_service.view_model";
    public const string OverlayPitServiceApplyUi = "overlay.pit_service.apply_ui";
    public const string OverlayPitServiceRows = "overlay.pit_service.rows";
    public const string OverlayPitServicePaint = "overlay.pit_service.paint";
    public const string OverlayInputStateRefresh = "overlay.input_state.refresh";
    public const string OverlayInputStateSnapshot = "overlay.input_state.snapshot";
    public const string OverlayInputStateViewModel = "overlay.input_state.view_model";
    public const string OverlayInputStateApplyUi = "overlay.input_state.apply_ui";
    public const string OverlayInputStateRows = "overlay.input_state.rows";
    public const string OverlayInputStatePaint = "overlay.input_state.paint";
    public const string OverlayRadarRefresh = "overlay.radar.refresh";
    public const string OverlayRadarSnapshot = "overlay.radar.snapshot";
    public const string OverlayRadarFadeState = "overlay.radar.fade_state";
    public const string OverlayRadarPaint = "overlay.radar.paint";
    public const string OverlayRadarDraw = "overlay.radar.draw";
    public const string OverlayGapRefresh = "overlay.gap.refresh";
    public const string OverlayGapSnapshot = "overlay.gap.snapshot";
    public const string OverlayGapRecordSnapshot = "overlay.gap.record_snapshot";
    public const string OverlayGapSelectSeries = "overlay.gap.select_series";
    public const string OverlayGapPaint = "overlay.gap.paint";
    public const string OverlayGapDrawGraph = "overlay.gap.draw_graph";
    public const string OverlayGapDrawPrepare = "overlay.gap.draw_prepare";
    public const string OverlayGapDrawStatic = "overlay.gap.draw_static";
    public const string OverlayGapDrawSeries = "overlay.gap.draw_series";
    public const string OverlayGapDrawLabels = "overlay.gap.draw_labels";
    public const string OverlaySettingsRefresh = "overlay.settings.refresh";
    public const string OverlaySettingsSyncCapture = "overlay.settings.sync_capture";
    public const string OverlaySettingsSyncDiagnostics = "overlay.settings.sync_diagnostics";
    public const string OverlaySettingsSyncAnalysis = "overlay.settings.sync_analysis";
    public const string OverlaySettingsSaveAndApply = "overlay.settings.save_and_apply";
    public const string OverlaySettingsSave = "overlay.settings.save";
    public const string OverlaySettingsApply = "overlay.settings.apply";
    public const string OverlaySettingsRefreshBrowserSizes = "overlay.settings.refresh_browser_sizes";
    public const string OverlayManagerApplySettings = "overlay.manager.apply_settings";
    public const string LocalhostRequest = "localhost.request";
    public const string DiagnosticsBundleCreate = "diagnostics.bundle.create";
    public const string DiagnosticsBundleMetadata = "diagnostics.bundle.metadata";
    public const string DiagnosticsBundleRuntimeSettings = "diagnostics.bundle.runtime_settings";
    public const string DiagnosticsBundleLogs = "diagnostics.bundle.logs";
    public const string DiagnosticsBundlePerformanceFiles = "diagnostics.bundle.performance_files";
    public const string DiagnosticsBundleEvents = "diagnostics.bundle.events";
    public const string DiagnosticsBundleEdgeCases = "diagnostics.bundle.edge_cases";
    public const string DiagnosticsBundleOverlayDiagnostics = "diagnostics.bundle.overlay_diagnostics";
    public const string DiagnosticsBundleLiveOverlayWindows = "diagnostics.bundle.live_overlay_windows";
    public const string DiagnosticsBundleWindowZOrder = "diagnostics.bundle.window_z_order";
    public const string DiagnosticsBundleLatestCapture = "diagnostics.bundle.latest_capture";
    public const string DiagnosticsBundleHistory = "diagnostics.bundle.history";
}

internal static class AppPerformanceValueIds
{
    public const string IRacingChanQuality = "iracing.chan_quality";
    public const string IRacingChanPartnerQuality = "iracing.chan_partner_quality";
    public const string IRacingChanLatency = "iracing.chan_latency";
    public const string IRacingChanAvgLatency = "iracing.chan_avg_latency";
    public const string IRacingChanClockSkew = "iracing.chan_clock_skew";
    public const string IRacingFrameRate = "iracing.frame_rate";
    public const string IRacingCpuUsageForeground = "iracing.cpu_usage_fg";
    public const string IRacingGpuUsage = "iracing.gpu_usage";
    public const string IRacingMemPageFaultsPerSecond = "iracing.mem_page_fault_sec";
    public const string IRacingMemSoftPageFaultsPerSecond = "iracing.mem_soft_page_fault_sec";
    public const string IRacingIsReplayPlaying = "iracing.is_replay_playing";
    public const string IRacingIsOnTrack = "iracing.is_on_track";
}

internal sealed record AppPerformanceSnapshot(
    DateTimeOffset TimestampUtc,
    DateTimeOffset StartedAtUtc,
    long TelemetryFrameCount,
    double TelemetryFramesPerSecond,
    IReadOnlyList<PerformanceMetricSnapshot> Metrics,
    IReadOnlyList<PerformanceValueSnapshot> IRacingSystem,
    IReadOnlyList<PerformanceValueSnapshot> OverlayUpdates,
    IReadOnlyList<OverlayWindowDiagnosticSnapshot> OverlayWindows,
    CapturePerformanceSnapshot Capture,
    ProcessPerformanceSnapshot Process);

internal sealed record OverlayWindowDiagnosticSnapshot(
    string OverlayId,
    DateTimeOffset TimestampUtc,
    bool Visible,
    bool TopMost,
    bool AlwaysOnTopSetting,
    bool InputTransparent,
    bool NoActivate,
    bool SettingsOverlayActive,
    bool SettingsWindowVisible,
    bool SettingsWindowIntersects,
    bool SettingsWindowInputProtected,
    bool InputInterceptRisk,
    int X,
    int Y,
    int Width,
    int Height,
    double Opacity);

internal sealed record PerformanceMetricSnapshot(
    string Id,
    long Count,
    long ErrorCount,
    double AverageMilliseconds,
    double LastMilliseconds,
    double MaxMilliseconds,
    double P95Milliseconds,
    DateTimeOffset? LastRecordedAtUtc);

internal sealed record PerformanceValueSnapshot(
    string Id,
    long Count,
    double? Average,
    double? Last,
    double? Minimum,
    double? Maximum,
    double? P05,
    double? P50,
    double? P95,
    DateTimeOffset? LastRecordedAtUtc);

internal sealed record CapturePerformanceSnapshot(
    long WriteStatusCount,
    string? LastCaptureId,
    string? LastCaptureDirectory,
    int LastFramesWritten,
    int LastSessionInfoSnapshotCount,
    int LastPendingMessageCount,
    long? LastTelemetryFileBytes,
    DateTimeOffset? LastWriteAtUtc,
    string? LastWriteError);

internal sealed record ProcessPerformanceSnapshot(
    long WorkingSetBytes,
    long PrivateMemoryBytes,
    long ManagedHeapBytes,
    int? GdiObjectCount,
    int? UserObjectCount,
    int Gen0Collections,
    int Gen1Collections,
    int Gen2Collections)
{
    private const int GrGdiObjects = 0;
    private const int GrUserObjects = 1;

    public static ProcessPerformanceSnapshot Capture()
    {
        using var process = Process.GetCurrentProcess();
        return new ProcessPerformanceSnapshot(
            WorkingSetBytes: process.WorkingSet64,
            PrivateMemoryBytes: process.PrivateMemorySize64,
            ManagedHeapBytes: GC.GetTotalMemory(forceFullCollection: false),
            GdiObjectCount: TryGetGuiResourceCount(process, GrGdiObjects),
            UserObjectCount: TryGetGuiResourceCount(process, GrUserObjects),
            Gen0Collections: GC.CollectionCount(0),
            Gen1Collections: GC.CollectionCount(1),
            Gen2Collections: GC.CollectionCount(2));
    }

    private static int? TryGetGuiResourceCount(Process process, int flag)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            return GetGuiResources(process.Handle, flag);
        }
        catch
        {
            return null;
        }
    }

    [DllImport("user32.dll")]
    private static extern int GetGuiResources(IntPtr hProcess, int uiFlags);
}
