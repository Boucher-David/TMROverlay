using TmrOverlay.Core.AppInfo;

namespace TmrOverlay.App.Runtime;

internal sealed class RuntimeState
{
    public int RuntimeStateVersion { get; init; } = 1;

    public required DateTimeOffset StartedAtUtc { get; init; }

    public int ProcessId { get; init; }

    public DateTimeOffset? LastHeartbeatAtUtc { get; set; }

    public DateTimeOffset? StoppedAtUtc { get; set; }

    public bool StoppedCleanly { get; set; }

    public AppVersionInfo? AppVersion { get; init; }
}
