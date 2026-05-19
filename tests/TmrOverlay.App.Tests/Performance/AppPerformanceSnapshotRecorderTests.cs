using TmrOverlay.App.Performance;
using TmrOverlay.App.Storage;
using Xunit;

namespace TmrOverlay.App.Tests.Performance;

public sealed class AppPerformanceSnapshotRecorderTests
{
    [Fact]
    public void Record_WritesPerformanceSnapshotJsonlUnderLogsRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "tmr-overlay-performance-test", Guid.NewGuid().ToString("N"));
        try
        {
            var storage = CreateStorage(root);
            var state = new AppPerformanceState();
            var timestamp = DateTimeOffset.Parse("2026-04-28T12:00:00Z");
            state.RecordTelemetryFrame(timestamp);
            state.RecordOperation("test.operation", TimeSpan.FromMilliseconds(2));
            state.RecordIRacingSystemTelemetry(
                timestamp,
                chanQuality: 0.91d,
                chanPartnerQuality: 0.88d,
                chanLatency: 0.066667d,
                chanAvgLatency: 0.05d,
                chanClockSkew: 0.001d,
                frameRate: 59.8d,
                cpuUsageForeground: 12.5d,
                gpuUsage: 48.2d,
                memPageFaultsPerSecond: 0d,
                memSoftPageFaultsPerSecond: 1d,
                isReplayPlaying: 0d,
                isOnTrack: 1d);
            state.RecordOverlayRefreshDecision(
                "fuel-calculator",
                timestamp,
                previousSequence: 10,
                currentSequence: 12,
                latestInputAtUtc: timestamp.AddMilliseconds(-125),
                applied: true);
            state.RecordOverlayLiveTelemetryState(
                "fuel-calculator",
                timestamp,
                liveTelemetryAvailable: false,
                fadeAlpha: 0.25d);
            state.RecordOverlayWindowState(
                "flags",
                timestamp,
                actualVisible: true,
                topMost: false,
                alwaysOnTopSetting: true,
                inputTransparent: true,
                noActivate: true,
                settingsOverlayActive: true,
                settingsWindowVisible: true,
                intersectsSettingsWindow: false,
                settingsWindowInputProtected: true,
                x: 10,
                y: 20,
                width: 300,
                height: 120,
                opacity: 0.9d);
            var recorder = new AppPerformanceSnapshotRecorder(storage);

            recorder.Record(state.Snapshot());

            var files = Directory.GetFiles(recorder.PerformanceLogsRoot, "performance-*.jsonl");
            var file = Assert.Single(files);
            var content = File.ReadAllText(file);
            Assert.Contains("\"telemetryFrameCount\":1", content);
            Assert.Contains("\"id\":\"test.operation\"", content);
            Assert.Contains("\"budget\":", content);
            Assert.Contains("\"category\":\"unclassified\"", content);
            Assert.Contains("\"iRacingSystem\"", content);
            Assert.Contains("\"id\":\"iracing.chan_quality\"", content);
            Assert.Contains("\"overlayUpdates\"", content);
            Assert.Contains("\"id\":\"overlay.fuel_calculator.update.input_changed\"", content);
            Assert.Contains("\"id\":\"overlay.fuel_calculator.update.applied\"", content);
            Assert.Contains("\"id\":\"overlay.fuel_calculator.update.live_available\"", content);
            Assert.Contains("\"id\":\"overlay.fuel_calculator.update.fade_alpha\"", content);
            Assert.Contains("\"overlayWindows\"", content);
            Assert.Contains("\"overlayId\":\"flags\"", content);
            Assert.Contains("\"id\":\"overlay.flags.window.input_transparent\"", content);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static AppStorageOptions CreateStorage(string root)
    {
        return new AppStorageOptions
        {
            AppDataRoot = root,
            CaptureRoot = Path.Combine(root, "captures"),
            UserHistoryRoot = Path.Combine(root, "history", "user"),
            BaselineHistoryRoot = Path.Combine(root, "history", "baseline"),
            LogsRoot = Path.Combine(root, "logs"),
            SettingsRoot = Path.Combine(root, "settings"),
            DiagnosticsRoot = Path.Combine(root, "diagnostics"),
            TrackMapRoot = Path.Combine(root, "track-maps", "user"),
            EventsRoot = Path.Combine(root, "logs", "events"),
            RuntimeStatePath = Path.Combine(root, "runtime-state.json")
        };
    }
}
