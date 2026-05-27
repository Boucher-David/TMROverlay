using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using TmrOverlay.App.Events;
using TmrOverlay.App.Storage;
using TmrOverlay.App.Telemetry;
using Xunit;

namespace TmrOverlay.App.Tests.Telemetry;

public sealed class FuelV2CaptureRecorderTests
{
    [Fact]
    public void CompleteCollection_WritesCaptureSidecarUnderConfiguredDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "tmr-overlay-fuel-v2-capture-recorder-test", Guid.NewGuid().ToString("N"));
        try
        {
            var storage = CreateStorage(root);
            var captureDirectory = Path.Combine(storage.CaptureRoot, "capture-fuel-v2");
            Directory.CreateDirectory(captureDirectory);
            var recorder = new FuelV2CaptureRecorder(
                new FuelV2CaptureOptions
                {
                    Enabled = true,
                    OutputFileName = "custom-fuel-v2.json",
                    CaptureDirectoryName = "custom-fuel-v2-sidecar",
                    LogDirectoryName = "custom-fuel-v2-logs"
                },
                storage,
                new AppEventRecorder(storage),
                NullLogger<FuelV2CaptureRecorder>.Instance);
            var startedAtUtc = DateTimeOffset.Parse("2026-05-10T12:00:00Z");
            recorder.StartCollection("capture-fuel-v2", startedAtUtc);

            var path = recorder.CompleteCollection(startedAtUtc.AddSeconds(5), captureDirectory);

            var expectedPath = Path.Combine(captureDirectory, "custom-fuel-v2-sidecar", "custom-fuel-v2.json");
            Assert.Equal(expectedPath, path);
            Assert.Equal(expectedPath, recorder.LastArtifactPath);
            Assert.True(File.Exists(expectedPath));
            Assert.Equal(Path.Combine(storage.LogsRoot, "custom-fuel-v2-logs"), recorder.DiagnosticsLogRoot);

            using var document = JsonDocument.Parse(File.ReadAllText(expectedPath));
            Assert.Equal(1, document.RootElement.GetProperty("formatVersion").GetInt32());
            Assert.Equal("capture-fuel-v2", document.RootElement.GetProperty("sourceId").GetString());
            Assert.Equal("raw-capture-sidecar", document.RootElement.GetProperty("output").GetProperty("mode").GetString());
            Assert.True(document.RootElement.GetProperty("output").GetProperty("rawTelemetryExcluded").GetBoolean());
            Assert.False(document.RootElement.GetProperty("output").GetProperty("durableHistoryMutated").GetBoolean());
            Assert.Equal(0, document.RootElement.GetProperty("totals").GetProperty("frameCount").GetInt32());
            Assert.Equal("no_local_fuel_truth_windows", document.RootElement.GetProperty("syntheticReplaySuitability").GetProperty("reasons")[0].GetString());
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void CompleteCollection_WithoutCaptureDirectoryWritesSanitizedDiagnosticsLogArtifact()
    {
        var root = Path.Combine(Path.GetTempPath(), "tmr-overlay-fuel-v2-capture-recorder-test", Guid.NewGuid().ToString("N"));
        try
        {
            var storage = CreateStorage(root);
            var recorder = new FuelV2CaptureRecorder(
                new FuelV2CaptureOptions
                {
                    Enabled = true,
                    OutputFileName = "diagnostics.json",
                    LogDirectoryName = "fuel-v2-log-root"
                },
                storage,
                new AppEventRecorder(storage),
                NullLogger<FuelV2CaptureRecorder>.Instance);
            var startedAtUtc = DateTimeOffset.Parse("2026-05-10T12:00:00Z");
            recorder.StartCollection("session/with/slashes", startedAtUtc);

            var path = recorder.CompleteCollection(startedAtUtc.AddSeconds(5), captureDirectory: null);

            var expectedPath = Path.Combine(storage.LogsRoot, "fuel-v2-log-root", "session-with-slashes-diagnostics.json");
            Assert.Equal(expectedPath, path);
            Assert.Equal(expectedPath, recorder.LastArtifactPath);
            Assert.True(File.Exists(expectedPath));
            using var document = JsonDocument.Parse(File.ReadAllText(expectedPath));
            Assert.Equal("rolling-log", document.RootElement.GetProperty("output").GetProperty("mode").GetString());
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void CompleteCollection_WhenDisabledReturnsNullAndDoesNotWriteArtifact()
    {
        var root = Path.Combine(Path.GetTempPath(), "tmr-overlay-fuel-v2-capture-recorder-test", Guid.NewGuid().ToString("N"));
        try
        {
            var storage = CreateStorage(root);
            var captureDirectory = Path.Combine(storage.CaptureRoot, "capture-fuel-v2");
            Directory.CreateDirectory(captureDirectory);
            var recorder = new FuelV2CaptureRecorder(
                new FuelV2CaptureOptions
                {
                    Enabled = false,
                    OutputFileName = "disabled.json",
                    CaptureDirectoryName = "fuel-v2-disabled"
                },
                storage,
                new AppEventRecorder(storage),
                NullLogger<FuelV2CaptureRecorder>.Instance);
            var startedAtUtc = DateTimeOffset.Parse("2026-05-10T12:00:00Z");
            recorder.StartCollection("capture-fuel-v2-disabled", startedAtUtc);

            var path = recorder.CompleteCollection(startedAtUtc.AddSeconds(5), captureDirectory);

            Assert.Null(path);
            Assert.Null(recorder.LastArtifactPath);
            Assert.False(File.Exists(Path.Combine(captureDirectory, "fuel-v2-disabled", "disabled.json")));
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
