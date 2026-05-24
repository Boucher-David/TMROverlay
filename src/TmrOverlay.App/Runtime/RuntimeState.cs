using System.Diagnostics;
using TmrOverlay.Core.AppInfo;

namespace TmrOverlay.App.Runtime;

internal sealed class RuntimeState
{
    public int RuntimeStateVersion { get; init; } = 1;

    public required DateTimeOffset StartedAtUtc { get; init; }

    public int ProcessId { get; init; }

    public RuntimeProcessIdentity? ProcessIdentity { get; init; }

    public DateTimeOffset? LastHeartbeatAtUtc { get; set; }

    public DateTimeOffset? ShutdownStartedAtUtc { get; set; }

    public DateTimeOffset? ShutdownCompletedAtUtc { get; set; }

    public string? ShutdownPhase { get; set; }

    public string? ShutdownReason { get; set; }

    public DateTimeOffset? StoppedAtUtc { get; set; }

    public bool StoppedCleanly { get; set; }

    public AppVersionInfo? AppVersion { get; init; }
}

internal sealed class RuntimeProcessIdentity
{
    public required int ProcessId { get; init; }

    public string? ProcessName { get; init; }

    public string? ExecutablePath { get; init; }

    public DateTimeOffset? ProcessStartedAtUtc { get; init; }

    public DateTimeOffset CapturedAtUtc { get; init; }

    public static RuntimeProcessIdentity Capture(DateTimeOffset capturedAtUtc)
    {
        using var process = Process.GetCurrentProcess();
        return new RuntimeProcessIdentity
        {
            ProcessId = Environment.ProcessId,
            ProcessName = SafeProcessName(process),
            ExecutablePath = SafeExecutablePath(process),
            ProcessStartedAtUtc = SafeProcessStartedAtUtc(process),
            CapturedAtUtc = capturedAtUtc
        };
    }

    private static string? SafeProcessName(Process process)
    {
        try
        {
            return string.IsNullOrWhiteSpace(process.ProcessName) ? null : process.ProcessName;
        }
        catch
        {
            return null;
        }
    }

    private static string? SafeExecutablePath(Process process)
    {
        if (!string.IsNullOrWhiteSpace(Environment.ProcessPath))
        {
            return Environment.ProcessPath;
        }

        try
        {
            return process.MainModule?.FileName;
        }
        catch
        {
            return null;
        }
    }

    private static DateTimeOffset? SafeProcessStartedAtUtc(Process process)
    {
        try
        {
            return new DateTimeOffset(process.StartTime.ToUniversalTime(), TimeSpan.Zero);
        }
        catch
        {
            return null;
        }
    }
}
