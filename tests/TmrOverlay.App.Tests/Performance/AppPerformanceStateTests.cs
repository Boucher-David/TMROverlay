using TmrOverlay.App.Performance;
using TmrOverlay.App.Telemetry;
using Xunit;

namespace TmrOverlay.App.Tests.Performance;

public sealed class AppPerformanceStateTests
{
    [Fact]
    public void Snapshot_IncludesRollingOperationStats()
    {
        var state = new AppPerformanceState();

        state.RecordOperation("test.operation", TimeSpan.FromMilliseconds(1));
        state.RecordOperation("test.operation", TimeSpan.FromMilliseconds(2), succeeded: false);
        state.RecordOperation("test.operation", TimeSpan.FromMilliseconds(3));

        var snapshot = state.Snapshot();
        var metric = Assert.Single(snapshot.Metrics.Where(metric => metric.Id == "test.operation"));
        Assert.Equal(3, metric.Count);
        Assert.Equal(1, metric.ErrorCount);
        Assert.Equal(2d, metric.AverageMilliseconds);
        Assert.Equal(3d, metric.LastMilliseconds);
        Assert.Equal(3d, metric.MaxMilliseconds);
        Assert.Equal(3d, metric.P95Milliseconds);
    }

    [Fact]
    public void Snapshot_CalculatesTelemetryFrameRateFromFrameTimestamps()
    {
        var state = new AppPerformanceState();
        var start = DateTimeOffset.Parse("2026-04-28T12:00:00Z");

        state.RecordTelemetryFrame(start);
        state.RecordTelemetryFrame(start.AddSeconds(1));
        state.RecordTelemetryFrame(start.AddSeconds(2));

        var snapshot = state.Snapshot();

        Assert.Equal(3, snapshot.TelemetryFrameCount);
        Assert.Equal(1d, snapshot.TelemetryFramesPerSecond);
    }

    [Fact]
    public void RecordCaptureWrite_TracksLatestCaptureStatusAndWriterDuration()
    {
        var state = new AppPerformanceState();
        var timestamp = DateTimeOffset.Parse("2026-04-28T12:00:00Z");

        state.RecordCaptureWrite(new TelemetryCaptureWriteStatus(
            TimestampUtc: timestamp,
            CaptureId: "capture-1",
            DirectoryPath: @"C:\captures\capture-1",
            FramesWritten: 42,
            SessionInfoSnapshotCount: 2,
            PendingMessageCount: 3,
            TelemetryFileBytes: 4096,
            Exception: null,
            LastWriteDuration: TimeSpan.FromMilliseconds(4)));

        var snapshot = state.Snapshot();
        var writerMetric = Assert.Single(snapshot.Metrics.Where(metric => metric.Id == AppPerformanceMetricIds.CaptureWriterWrite));

        Assert.Equal(1, snapshot.Capture.WriteStatusCount);
        Assert.Equal("capture-1", snapshot.Capture.LastCaptureId);
        Assert.Equal(42, snapshot.Capture.LastFramesWritten);
        Assert.Equal(2, snapshot.Capture.LastSessionInfoSnapshotCount);
        Assert.Equal(3, snapshot.Capture.LastPendingMessageCount);
        Assert.Equal(4096, snapshot.Capture.LastTelemetryFileBytes);
        Assert.Equal(timestamp, snapshot.Capture.LastWriteAtUtc);
        Assert.Equal(1, writerMetric.Count);
        Assert.Equal(4d, writerMetric.AverageMilliseconds);
    }

    [Fact]
    public void OverlayRefreshDecision_TracksUnchangedSequenceSkips()
    {
        var state = new AppPerformanceState();
        var timestamp = DateTimeOffset.Parse("2026-05-07T12:00:00Z");

        state.RecordOverlayRefreshDecision(
            "standings",
            timestamp,
            previousSequence: 7,
            currentSequence: 7,
            latestInputAtUtc: timestamp.AddMilliseconds(-50),
            applied: false);

        var snapshot = state.Snapshot();
        var skipped = Assert.Single(snapshot.OverlayUpdates.Where(metric => metric.Id == "overlay.standings.update.skipped"));
        var unchanged = Assert.Single(snapshot.OverlayUpdates.Where(metric => metric.Id == "overlay.standings.update.skipped_unchanged_sequence"));

        Assert.Equal(1d, skipped.Last);
        Assert.Equal(1d, unchanged.Last);
    }

    [Fact]
    public void OverlayDiagnostics_TrackTimerLifecyclePaintAndLocalhostSignals()
    {
        var state = new AppPerformanceState();
        var timestamp = DateTimeOffset.Parse("2026-05-07T12:00:00Z");

        state.RecordOverlayTimerTick("track-map", 50, visible: false, pauseEligible: true);
        state.RecordOverlaySettingsMutation(
            "unit_system",
            timestamp,
            existingFormCount: 3,
            updatedFormCount: 2,
            recreatedFormCount: 0);
        state.RecordOverlayLifecycleState(
            "track-map",
            timestamp,
            enabled: true,
            sessionAllowed: false,
            settingsPreview: false,
            desiredVisible: false,
            actualVisible: false,
            hasForm: true,
            liveTelemetryAvailable: true,
            contextAvailable: true,
            fadeAlpha: 1d,
            fadesWhenLiveTelemetryUnavailable: true,
            pauseEligible: true);
        state.RecordOperation(AppPerformanceMetricIds.OverlayTrackMapPaint, TimeSpan.FromMilliseconds(2));
        state.RecordLocalhostActivity(
            timestamp,
            enabled: true,
            listening: true,
            totalRequests: 0,
            failedRequests: 0,
            hasRecentRequests: false,
            lastRequestAgeSeconds: null);
        state.RecordLocalhostRequest("snapshot", 200, TimeSpan.FromMilliseconds(3), succeeded: true);

        var snapshot = state.Snapshot();

        Assert.Contains(snapshot.OverlayUpdates, metric => metric.Id == "overlay.track_map.timer.tick" && metric.Count == 1);
        Assert.Contains(snapshot.OverlayUpdates, metric => metric.Id == "overlay.timer.cadence.50ms.tick" && metric.Count == 1);
        Assert.Contains(snapshot.OverlayUpdates, metric => metric.Id == "overlay.timer.active_count" && metric.Last == 1d);
        Assert.Contains(snapshot.OverlayUpdates, metric => metric.Id == "overlay.timer.cadence.50ms.active_count" && metric.Last == 1d);
        Assert.Contains(snapshot.OverlayUpdates, metric => metric.Id == "overlay.manager.settings.unit_system.changed" && metric.Last == 1d);
        Assert.Contains(snapshot.OverlayUpdates, metric => metric.Id == "overlay.manager.settings.unit_system.existing_form_count" && metric.Last == 3d);
        Assert.Contains(snapshot.OverlayUpdates, metric => metric.Id == "overlay.manager.settings.unit_system.updated_form_count" && metric.Last == 2d);
        Assert.Contains(snapshot.OverlayUpdates, metric => metric.Id == "overlay.manager.settings.unit_system.recreated_form_count" && metric.Last == 0d);
        Assert.Contains(snapshot.OverlayUpdates, metric => metric.Id == "overlay.track_map.lifecycle.hidden_by_session" && metric.Last == 1d);
        Assert.Contains(snapshot.OverlayUpdates, metric => metric.Id == "overlay.track_map.lifecycle.context_available" && metric.Last == 1d);
        Assert.Contains(snapshot.OverlayUpdates, metric => metric.Id == "overlay.track_map.lifecycle.pause_eligible" && metric.Last == 1d);
        Assert.Contains(snapshot.OverlayUpdates, metric => metric.Id == "overlay.track_map.paint.sample" && metric.Count == 1);
        Assert.Contains(snapshot.OverlayUpdates, metric => metric.Id == "localhost.idle_no_recent_requests" && metric.Last == 1d);
        Assert.Contains(snapshot.OverlayUpdates, metric => metric.Id == "localhost.request.route.snapshot.tick" && metric.Count == 1);
    }

    [Fact]
    public void OverlayWindowState_FlagsVisibleInvisibleInputInterception()
    {
        var state = new AppPerformanceState();
        var timestamp = DateTimeOffset.Parse("2026-05-09T12:00:00Z");

        state.RecordOverlayWindowState(
            "standings",
            timestamp,
            actualVisible: true,
            topMost: true,
            alwaysOnTopSetting: true,
            inputTransparent: false,
            noActivate: false,
            settingsOverlayActive: false,
            settingsWindowVisible: false,
            intersectsSettingsWindow: false,
            settingsWindowInputProtected: false,
            x: 10,
            y: 20,
            width: 780,
            height: 520,
            opacity: 0d);

        var snapshot = state.Snapshot();

        Assert.Contains(snapshot.OverlayUpdates, metric =>
            metric.Id == "overlay.standings.window.input_intercept_risk" && metric.Last == 1d);
        var window = Assert.Single(snapshot.OverlayWindows);
        Assert.True(window.InputInterceptRisk);
    }

    [Fact]
    public void OverlayWindowState_FlagsTopMostNoActivateOverlayRiskWhileSettingsVisible()
    {
        var state = new AppPerformanceState();
        var timestamp = DateTimeOffset.Parse("2026-05-09T12:00:00Z");

        state.RecordOverlayWindowState(
            "relative",
            timestamp,
            actualVisible: true,
            topMost: true,
            alwaysOnTopSetting: true,
            inputTransparent: false,
            noActivate: true,
            settingsOverlayActive: false,
            settingsWindowVisible: true,
            intersectsSettingsWindow: false,
            settingsWindowInputProtected: false,
            x: 822,
            y: 18,
            width: 438,
            height: 360,
            opacity: 0.88d);

        var snapshot = state.Snapshot();

        Assert.Contains(snapshot.OverlayUpdates, metric =>
            metric.Id == "overlay.relative.window.settings_window_visible" && metric.Last == 1d);
        Assert.Contains(snapshot.OverlayUpdates, metric =>
            metric.Id == "overlay.relative.window.input_intercept_risk" && metric.Last == 1d);
        var window = Assert.Single(snapshot.OverlayWindows);
        Assert.True(window.SettingsWindowVisible);
        Assert.True(window.InputInterceptRisk);
    }

    [Fact]
    public void OverlayWindowState_DoesNotReportSettingsActiveWhenSettingsWindowIsHidden()
    {
        var state = new AppPerformanceState();
        var timestamp = DateTimeOffset.Parse("2026-05-09T12:00:00Z");

        state.RecordOverlayWindowState(
            "relative",
            timestamp,
            actualVisible: true,
            topMost: false,
            alwaysOnTopSetting: false,
            inputTransparent: false,
            noActivate: false,
            settingsOverlayActive: true,
            settingsWindowVisible: false,
            intersectsSettingsWindow: false,
            settingsWindowInputProtected: false,
            x: 822,
            y: 18,
            width: 438,
            height: 360,
            opacity: 1d);

        var snapshot = state.Snapshot();

        Assert.Contains(snapshot.OverlayUpdates, metric =>
            metric.Id == "overlay.relative.window.settings_overlay_active" && metric.Last == 0d);
        Assert.Contains(snapshot.OverlayUpdates, metric =>
            metric.Id == "overlay.relative.window.input_intercept_risk" && metric.Last == 0d);
        var window = Assert.Single(snapshot.OverlayWindows);
        Assert.False(window.SettingsOverlayActive);
        Assert.False(window.SettingsWindowVisible);
        Assert.False(window.InputInterceptRisk);
    }
}
