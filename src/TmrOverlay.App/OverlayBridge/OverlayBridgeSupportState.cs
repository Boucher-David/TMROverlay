using TmrOverlay.Core.OverlayBridge;

namespace TmrOverlay.App.OverlayBridge;

/// <summary>
/// Holds only safe health metadata for the future Overlay Bridge Support page.
/// No transport, listener, pairing material, credentials, or telemetry payload is owned here.
/// </summary>
internal sealed class OverlayBridgeSupportState
{
    private readonly object _sync = new();
    private OverlayBridgeSupportSnapshot _snapshot = OverlayBridgeSupportSnapshot.Unavailable;

    public OverlayBridgeSupportSnapshot Snapshot()
    {
        lock (_sync)
        {
            return _snapshot;
        }
    }

    // Reserved for a future transport host to publish a sanitized health snapshot.
    // This slice deliberately has no caller and performs no networking or persistence.
    public void Replace(OverlayBridgeSupportSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        lock (_sync)
        {
            _snapshot = snapshot;
        }
    }
}
