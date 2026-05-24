using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TmrOverlay.Core.AppInfo;
using TmrOverlay.App.Events;
using TmrOverlay.App.Storage;

namespace TmrOverlay.App.Runtime;

internal sealed class RuntimeStateService : IHostedService, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly AppStorageOptions _storageOptions;
    private readonly AppEventRecorder _events;
    private readonly ILogger<RuntimeStateService> _logger;
    private readonly object _sync = new();
    private RuntimeState? _currentState;
    private System.Threading.Timer? _heartbeatTimer;

    public RuntimeStateService(
        AppStorageOptions storageOptions,
        AppEventRecorder events,
        ILogger<RuntimeStateService> logger)
    {
        _storageOptions = storageOptions;
        _events = events;
        _logger = logger;
    }

    public RuntimeState? PreviousState { get; private set; }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        PreviousState = ReadPreviousState();
        if (PreviousState is not null && !PreviousState.StoppedCleanly)
        {
            _logger.LogWarning("Previous TmrOverlay run did not stop cleanly. Started at {StartedAtUtc}.", PreviousState.StartedAtUtc);
            _events.Record("previous_run_unclean", new Dictionary<string, string?>
            {
                ["startedAtUtc"] = PreviousState.StartedAtUtc.ToString("O"),
                ["shutdownPhase"] = PreviousState.ShutdownPhase,
                ["shutdownReason"] = PreviousState.ShutdownReason,
                ["shutdownStartedAtUtc"] = PreviousState.ShutdownStartedAtUtc?.ToString("O")
            });
        }

        var startedAtUtc = DateTimeOffset.UtcNow;
        _currentState = new RuntimeState
        {
            StartedAtUtc = startedAtUtc,
            ProcessId = Environment.ProcessId,
            ProcessIdentity = RuntimeProcessIdentity.Capture(startedAtUtc),
            LastHeartbeatAtUtc = startedAtUtc,
            StoppedCleanly = false,
            AppVersion = AppVersionInfo.Current
        };
        WriteState(_currentState);
        _events.Record("app_started");

        _heartbeatTimer = new System.Threading.Timer(_ => Heartbeat(), null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
        return Task.CompletedTask;
    }

    public void MarkHostStopStarted(string reason)
    {
        var timestampUtc = DateTimeOffset.UtcNow;
        lock (_sync)
        {
            if (_currentState is not null)
            {
                _currentState.LastHeartbeatAtUtc = timestampUtc;
                _currentState.ShutdownStartedAtUtc ??= timestampUtc;
                _currentState.ShutdownPhase = "host_stop_started";
                _currentState.ShutdownReason = reason;
                WriteState(_currentState);
            }
        }

        _events.Record("host_stop_started", new Dictionary<string, string?>
        {
            ["reason"] = reason
        });
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            if (_currentState is not null)
            {
                var timestampUtc = DateTimeOffset.UtcNow;
                _currentState.LastHeartbeatAtUtc = timestampUtc;
                _currentState.ShutdownStartedAtUtc ??= timestampUtc;
                _currentState.ShutdownCompletedAtUtc = timestampUtc;
                _currentState.ShutdownPhase = "host_stop_completed";
                _currentState.StoppedAtUtc = timestampUtc;
                _currentState.StoppedCleanly = true;
                WriteState(_currentState);
            }
        }

        _events.Record("app_stopped");
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _heartbeatTimer?.Dispose();
    }

    private RuntimeState? ReadPreviousState()
    {
        if (!File.Exists(_storageOptions.RuntimeStatePath))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(_storageOptions.RuntimeStatePath);
            return JsonSerializer.Deserialize<RuntimeState>(stream, JsonOptions);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to read previous runtime state from {RuntimeStatePath}.", _storageOptions.RuntimeStatePath);
            return null;
        }
    }

    private void Heartbeat()
    {
        lock (_sync)
        {
            if (_currentState is null)
            {
                return;
            }

            _currentState.LastHeartbeatAtUtc = DateTimeOffset.UtcNow;
            WriteState(_currentState);
        }
    }

    private void WriteState(RuntimeState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_storageOptions.RuntimeStatePath)!);
        File.WriteAllText(_storageOptions.RuntimeStatePath, JsonSerializer.Serialize(state, JsonOptions));
    }
}
