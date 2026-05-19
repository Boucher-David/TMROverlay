using Microsoft.Extensions.Logging.Abstractions;
using TmrOverlay.App.Events;
using TmrOverlay.App.Runtime;
using TmrOverlay.App.Storage;
using Xunit;

namespace TmrOverlay.App.Tests.Runtime;

public sealed class RuntimeStateServiceTests
{
    [Fact]
    public async Task StartAndStop_WritesCleanRuntimeState()
    {
        var root = Path.Combine(Path.GetTempPath(), "tmr-overlay-runtime-test", Guid.NewGuid().ToString("N"));
        try
        {
            var storage = CreateStorage(root);
            using var service = new RuntimeStateService(
                storage,
                new AppEventRecorder(storage),
                NullLogger<RuntimeStateService>.Instance);

            await service.StartAsync(CancellationToken.None);
            var startedState = File.ReadAllText(storage.RuntimeStatePath);
            Assert.Contains("\"stoppedCleanly\": false", startedState);

            await service.StopAsync(CancellationToken.None);
            var stoppedState = File.ReadAllText(storage.RuntimeStatePath);
            Assert.Contains("\"stoppedCleanly\": true", stoppedState);
            Assert.Contains("\"stoppedAtUtc\"", stoppedState);
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
    public async Task Start_WritesRunningProcessIdentityEvidence()
    {
        var root = Path.Combine(Path.GetTempPath(), "tmr-overlay-runtime-test", Guid.NewGuid().ToString("N"));
        try
        {
            var storage = CreateStorage(root);
            using var service = new RuntimeStateService(
                storage,
                new AppEventRecorder(storage),
                NullLogger<RuntimeStateService>.Instance);

            await service.StartAsync(CancellationToken.None);

            using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(storage.RuntimeStatePath));
            var state = document.RootElement;
            Assert.False(state.GetProperty("stoppedCleanly").GetBoolean());
            Assert.False(state.TryGetProperty("stoppedAtUtc", out var stoppedAtUtc) && stoppedAtUtc.ValueKind != System.Text.Json.JsonValueKind.Null);
            Assert.True(state.TryGetProperty("processId", out var processId));
            Assert.Equal(Environment.ProcessId, processId.GetInt32());
            Assert.True(state.TryGetProperty("processIdentity", out var processIdentity));
            Assert.Equal(Environment.ProcessId, processIdentity.GetProperty("processId").GetInt32());
            Assert.False(string.IsNullOrWhiteSpace(processIdentity.GetProperty("processName").GetString()));
            Assert.True(processIdentity.TryGetProperty("capturedAtUtc", out var capturedAtUtc));
            Assert.Equal(System.Text.Json.JsonValueKind.String, capturedAtUtc.ValueKind);

            await service.StopAsync(CancellationToken.None);
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
    public async Task Start_DetectsPreviousUncleanRun()
    {
        var root = Path.Combine(Path.GetTempPath(), "tmr-overlay-runtime-test", Guid.NewGuid().ToString("N"));
        try
        {
            var storage = CreateStorage(root);
            Directory.CreateDirectory(Path.GetDirectoryName(storage.RuntimeStatePath)!);
            File.WriteAllText(
                storage.RuntimeStatePath,
                """
                {
                  "runtimeStateVersion": 1,
                  "startedAtUtc": "2026-04-26T12:00:00+00:00",
                  "lastHeartbeatAtUtc": "2026-04-26T12:01:00+00:00",
                  "stoppedCleanly": false
                }
                """);

            using var service = new RuntimeStateService(
                storage,
                new AppEventRecorder(storage),
                NullLogger<RuntimeStateService>.Instance);

            await service.StartAsync(CancellationToken.None);
            await service.StopAsync(CancellationToken.None);

            Assert.NotNull(service.PreviousState);
            Assert.False(service.PreviousState!.StoppedCleanly);
            var eventFile = Assert.Single(Directory.EnumerateFiles(storage.EventsRoot, "events-*.jsonl"));
            Assert.Contains("previous_run_unclean", File.ReadAllText(eventFile));
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
