namespace TmrOverlay.Core.OverlayBridge;

/// <summary>
/// A sanitized, display-only summary of the future Overlay Bridge runtime.
/// This is intentionally separate from persisted settings and transport payloads.
/// </summary>
public sealed record OverlayBridgeSupportSnapshot(
    OverlayBridgeAvailability Availability,
    bool Enabled,
    OverlayBridgeTransportStatus TransportStatus,
    string? SchemaVersion,
    string? SchemaHash,
    int ConnectedPairedClientCount,
    TimeSpan? LatestFrameAge,
    string? LastSafeError)
{
    public static OverlayBridgeSupportSnapshot Unavailable { get; } = new(
        Availability: OverlayBridgeAvailability.Unavailable,
        Enabled: false,
        TransportStatus: OverlayBridgeTransportStatus.Unavailable,
        SchemaVersion: null,
        SchemaHash: null,
        ConnectedPairedClientCount: 0,
        LatestFrameAge: null,
        LastSafeError: null);
}

public enum OverlayBridgeAvailability
{
    Unavailable,
    Available
}

/// <summary>
/// Transport lifecycle labels for future read-only, explicitly allowlisted bridge implementations.
/// They do not imply that pairing, listeners, credentials, or data export exist in this build.
/// </summary>
public enum OverlayBridgeTransportStatus
{
    Unavailable,
    Disabled,
    AwaitingPairing,
    Ready,
    Connected,
    Degraded
}
