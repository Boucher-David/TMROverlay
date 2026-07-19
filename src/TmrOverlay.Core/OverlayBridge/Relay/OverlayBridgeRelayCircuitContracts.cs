namespace TmrOverlay.Core.OverlayBridge.Relay;

/// <summary>
/// The minimum room-instance routing scope assigned by an authenticated Bridge control plane.
/// These are opaque identifiers; a relay never receives labels, simulator state, or facts.
/// </summary>
internal sealed record OverlayBridgeRelayCircuitScope
{
    public OverlayBridgeRelayCircuitScope(
        string roomId,
        string roomInstanceId,
        string streamId)
    {
        RequireIdentifier(roomId, nameof(roomId));
        RequireIdentifier(roomInstanceId, nameof(roomInstanceId));
        RequireIdentifier(streamId, nameof(streamId));

        RoomId = roomId;
        RoomInstanceId = roomInstanceId;
        StreamId = streamId;
    }

    public string RoomId { get; }

    public string RoomInstanceId { get; }

    public string StreamId { get; }

    private static void RequireIdentifier(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > OverlayBridgeFactContracts.MaxOpaqueIdentifierLength)
        {
            throw new ArgumentException(
                $"{parameterName} must be a non-empty opaque identifier no longer than {OverlayBridgeFactContracts.MaxOpaqueIdentifierLength} characters.",
                parameterName);
        }
    }
}

/// <summary>
/// Immutable control-plane evidence assigned to one two-endpoint relay circuit after identity,
/// room policy, role, lease, nonce, and capability checks have succeeded. This is deliberately
/// not a cryptographic implementation: a future authenticated Hello/control-plane host creates
/// it, and this payload-blind relay seam only refuses a caller that presents different metadata.
/// </summary>
internal sealed record OverlayBridgeRelayAuthenticatedCircuitBinding
{
    public OverlayBridgeRelayAuthenticatedCircuitBinding(
        Guid circuitId,
        OverlayBridgeRelayCircuitScope scope,
        OverlayBridgeSessionBinding session,
        string publisherDeviceKeyId,
        string viewerDeviceKeyId,
        string ownerPolicyHash,
        long ownerPolicyEpoch,
        string circuitNonce,
        string publisherLeaseId,
        long publisherLeaseEpoch,
        long publicationEpoch,
        OverlayBridgeCapability grantedCapabilities,
        long expiresAtMonotonicMilliseconds)
    {
        if (circuitId == Guid.Empty)
        {
            throw new ArgumentException("A circuit identifier is required.", nameof(circuitId));
        }

        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(session);
        RequireIdentifier(publisherDeviceKeyId, nameof(publisherDeviceKeyId));
        RequireIdentifier(viewerDeviceKeyId, nameof(viewerDeviceKeyId));
        RequireCanonicalSha256Hex(ownerPolicyHash, nameof(ownerPolicyHash));
        RequireSessionBinding(session, nameof(session));

        if (string.Equals(publisherDeviceKeyId, viewerDeviceKeyId, StringComparison.Ordinal))
        {
            throw new ArgumentException("A circuit must bind distinct publisher and viewer device identities.", nameof(viewerDeviceKeyId));
        }

        if (ownerPolicyEpoch <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ownerPolicyEpoch), "The owner policy epoch must be positive.");
        }

        RequireCanonicalCircuitNonce(circuitNonce, nameof(circuitNonce));
        RequireIdentifier(publisherLeaseId, nameof(publisherLeaseId));

        if (publisherLeaseEpoch <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(publisherLeaseEpoch), "The publisher lease epoch must be positive.");
        }

        if (publicationEpoch <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(publicationEpoch), "The publication epoch must be positive.");
        }

        if (!OverlayBridgeFactContracts.IsKnownCapabilitySet(grantedCapabilities))
        {
            throw new ArgumentOutOfRangeException(nameof(grantedCapabilities), "The granted capability set must be known and non-empty.");
        }

        if (expiresAtMonotonicMilliseconds <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(expiresAtMonotonicMilliseconds),
                "Circuit expiry must be a positive elapsed monotonic timestamp.");
        }

        CircuitId = circuitId;
        Scope = scope;
        Session = session;
        PublisherDeviceKeyId = publisherDeviceKeyId;
        ViewerDeviceKeyId = viewerDeviceKeyId;
        OwnerPolicyHash = ownerPolicyHash;
        OwnerPolicyEpoch = ownerPolicyEpoch;
        CircuitNonce = circuitNonce;
        PublisherLeaseId = publisherLeaseId;
        PublisherLeaseEpoch = publisherLeaseEpoch;
        PublicationEpoch = publicationEpoch;
        GrantedCapabilities = grantedCapabilities;
        ExpiresAtMonotonicMilliseconds = expiresAtMonotonicMilliseconds;
    }

    public Guid CircuitId { get; }

    public OverlayBridgeRelayCircuitScope Scope { get; }

    /// <summary>The exact active simulator/session binding accepted by control plane and Hello.</summary>
    public OverlayBridgeSessionBinding Session { get; }

    public string PublisherDeviceKeyId { get; }

    public string ViewerDeviceKeyId { get; }

    public string OwnerPolicyHash { get; }

    public long OwnerPolicyEpoch { get; }

    /// <summary>Canonical uppercase hex of the 32-byte Hello nonce.</summary>
    public string CircuitNonce { get; }

    public string PublisherLeaseId { get; }

    public long PublisherLeaseEpoch { get; }

    /// <summary>
    /// The active lease's publication epoch. Every fact header must inherit it; this prevents a
    /// still-authenticated circuit from replaying a prior publisher epoch under the same lease.
    /// </summary>
    public long PublicationEpoch { get; }

    public OverlayBridgeCapability GrantedCapabilities { get; }

    public long ExpiresAtMonotonicMilliseconds { get; }

    /// <summary>
    /// Produces the publisher-side portion shared by every independently protected viewer
    /// circuit. A caller cannot use it to send to a circuit with a different room, policy,
    /// publisher identity, lease, or capability grant.
    /// </summary>
    public OverlayBridgeRelayPublisherBinding PublisherBinding => new(
        Scope,
        Session,
        PublisherDeviceKeyId,
        OwnerPolicyHash,
        OwnerPolicyEpoch,
        PublisherLeaseId,
        PublisherLeaseEpoch,
        PublicationEpoch,
        GrantedCapabilities);

    /// <summary>
    /// The publisher endpoint's exact circuit binding. A publisher must produce a separately
    /// protected frame for every viewer circuit; the relay does not take one protected byte
    /// sequence and reuse it for a different viewer.
    /// </summary>
    public OverlayBridgeRelayPublisherCircuitBinding PublisherCircuitBinding => new(
        CircuitId,
        CircuitNonce,
        PublisherBinding);

    /// <summary>
    /// Checks that a completed inner-TLS Hello names this exact authenticated relay circuit.
    /// The caller still performs the peer certificate/device-policy verification separately.
    /// </summary>
    public bool MatchesChannelHello(OverlayBridgeChannelHello? hello)
    {
        return hello is not null
            && hello.EndpointRole == OverlayBridgeChannelEndpointRole.Publisher
            && hello.ExpectedPeerRole == OverlayBridgeChannelEndpointRole.Viewer
            && string.Equals(Scope.RoomId, hello.RoomId, StringComparison.Ordinal)
            && string.Equals(Scope.StreamId, hello.StreamId, StringComparison.Ordinal)
            && Session.Equals(hello.Session)
            && string.Equals(OwnerPolicyHash, OverlayBridgeRelayBindingEncoding.ToCanonicalSha256Hex(hello.OwnerPolicyHash), StringComparison.Ordinal)
            && OwnerPolicyEpoch == hello.OwnerPolicyEpoch
            && string.Equals(PublisherLeaseId, hello.PublisherLeaseId, StringComparison.Ordinal)
            && PublisherLeaseEpoch == hello.PublisherLeaseEpoch
            && CircuitId == hello.CircuitId
            && string.Equals(CircuitNonce, OverlayBridgeRelayBindingEncoding.ToCanonicalCircuitNonceHex(hello.CircuitNonce), StringComparison.Ordinal)
            && GrantedCapabilities == hello.NegotiatedCapabilities;
    }

    private static void RequireIdentifier(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > OverlayBridgeFactContracts.MaxOpaqueIdentifierLength)
        {
            throw new ArgumentException(
                $"{parameterName} must be a non-empty opaque identifier no longer than {OverlayBridgeFactContracts.MaxOpaqueIdentifierLength} characters.",
                parameterName);
        }
    }

    private static void RequireSessionBinding(OverlayBridgeSessionBinding session, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(session.SessionId)
            || session.SessionEpoch <= 0
            || string.IsNullOrWhiteSpace(session.TrackKey)
            || string.IsNullOrWhiteSpace(session.TeamCarKey))
        {
            throw new ArgumentException("A complete positive session binding is required.", parameterName);
        }
    }

    private static void RequireCanonicalSha256Hex(string value, string parameterName)
    {
        if (!OverlayBridgeRelayBindingEncoding.IsCanonicalHex(value, OverlayBridgeChannelHelloContracts.Sha256HashBytes, rejectAllZero: false))
        {
            throw new ArgumentException("A canonical SHA-256 hexadecimal value is required.", parameterName);
        }
    }

    private static void RequireCanonicalCircuitNonce(string value, string parameterName)
    {
        if (!OverlayBridgeRelayBindingEncoding.IsCanonicalHex(value, OverlayBridgeChannelHelloContracts.CircuitNonceBytes, rejectAllZero: true))
        {
            throw new ArgumentException("A canonical nonzero 32-byte circuit nonce is required.", parameterName);
        }
    }
}

/// <summary>
/// Shared authenticated publisher metadata for a fan-out operation. The circuit nonce and
/// viewer identity intentionally stay circuit-local, so a protected byte sequence cannot be
/// routed to an unrelated endpoint by merely sharing publisher metadata.
/// </summary>
internal sealed record OverlayBridgeRelayPublisherBinding(
    OverlayBridgeRelayCircuitScope Scope,
    OverlayBridgeSessionBinding Session,
    string PublisherDeviceKeyId,
    string OwnerPolicyHash,
    long OwnerPolicyEpoch,
    string PublisherLeaseId,
    long PublisherLeaseEpoch,
    long PublicationEpoch,
    OverlayBridgeCapability GrantedCapabilities);

/// <summary>
/// The authenticated publisher endpoint on one virtual circuit. Both the unguessable circuit
/// id and the inner-Hello nonce are required in addition to the shared publisher binding.
/// </summary>
internal sealed record OverlayBridgeRelayPublisherCircuitBinding(
    Guid CircuitId,
    string CircuitNonce,
    OverlayBridgeRelayPublisherBinding PublisherBinding);

/// <summary>
/// One individually protected frame selected for a single viewer circuit. Its bytes are opaque
/// to this Core seam; it carries no decoded facts, publication header, or telemetry fields.
/// </summary>
internal sealed record OverlayBridgeRelayPublisherCircuitFrame(
    OverlayBridgeRelayPublisherCircuitBinding CircuitBinding,
    ReadOnlyMemory<byte> ProtectedBytes);

/// <summary>
/// Bounds for one viewer's relay queue. The queue must always be able to hold one entire
/// protected frame; an additional write while it is full disconnects only that stalled viewer.
/// </summary>
internal sealed record OverlayBridgeRelayQueueLimits
{
    public OverlayBridgeRelayQueueLimits(
        int maximumProtectedFrameBytes,
        int maximumQueuedBytes)
    {
        if (maximumProtectedFrameBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumProtectedFrameBytes),
                "The protected frame bound must be positive.");
        }

        if (maximumQueuedBytes < maximumProtectedFrameBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumQueuedBytes),
                "The per-viewer queue must hold one maximum protected frame.");
        }

        MaximumProtectedFrameBytes = maximumProtectedFrameBytes;
        MaximumQueuedBytes = maximumQueuedBytes;
    }

    public int MaximumProtectedFrameBytes { get; }

    public int MaximumQueuedBytes { get; }
}

internal enum OverlayBridgeRelayCircuitCloseReason
{
    None = 0,
    Explicit = 1,
    Expired = 2,
    ViewerBackpressure = 3
}

internal enum OverlayBridgeRelayForwardOutcome
{
    Enqueued = 0,
    CircuitClosed = 1,
    CircuitExpired = 2,
    AuthenticatedCircuitBindingMismatch = 3,
    ProtectedFrameTooLarge = 4,
    ViewerDisconnectedForBackpressure = 5,
    MonotonicTimeRegression = 6,
    NoFrameForCircuit = 7
}

internal enum OverlayBridgeRelayReceiveOutcome
{
    FrameAvailable = 0,
    NoFrameAvailable = 1,
    CircuitClosed = 2,
    CircuitExpired = 3,
    MonotonicTimeRegression = 4
}

internal sealed record OverlayBridgeRelayCircuitState(
    OverlayBridgeRelayAuthenticatedCircuitBinding Binding,
    OverlayBridgeRelayCircuitCloseReason CloseReason,
    long LastObservedMonotonicMilliseconds,
    long? LastAcceptedReceiptMonotonicMilliseconds,
    int QueuedFrameCount,
    int QueuedBytes)
{
    public bool IsClosed => CloseReason != OverlayBridgeRelayCircuitCloseReason.None;
}

internal sealed record OverlayBridgeRelayForwardResult(
    string ViewerDeviceKeyId,
    OverlayBridgeRelayForwardOutcome Outcome,
    OverlayBridgeRelayCircuitState State);

internal sealed record OverlayBridgeRelayReceiveResult(
    OverlayBridgeRelayReceiveOutcome Outcome,
    OverlayBridgeRelayOpaqueFrame? Frame,
    OverlayBridgeRelayCircuitState State);

/// <summary>
/// A relay-owned copy of protected bytes. The relay stores and forwards these bytes without
/// parsing, logging, decoding, or sharing their mutable backing storage with another viewer.
/// </summary>
internal sealed class OverlayBridgeRelayOpaqueFrame
{
    private readonly byte[] _protectedBytes;

    internal OverlayBridgeRelayOpaqueFrame(ReadOnlyMemory<byte> protectedBytes, long receivedAtMonotonicMilliseconds)
    {
        _protectedBytes = protectedBytes.ToArray();
        ReceivedAtMonotonicMilliseconds = receivedAtMonotonicMilliseconds;
    }

    public int ByteCount => _protectedBytes.Length;

    public long ReceivedAtMonotonicMilliseconds { get; }

    /// <summary>Copies the protected bytes for the viewer endpoint without exposing relay storage.</summary>
    public byte[] CopyProtectedBytes() => _protectedBytes.ToArray();
}

/// <summary>Canonical conversion at the relay/Hello boundary; no secret or telemetry is encoded.</summary>
internal static class OverlayBridgeRelayBindingEncoding
{
    public static string ToCanonicalSha256Hex(ReadOnlyMemory<byte> value)
    {
        return OverlayBridgeChannelHelloContracts.IsValidSha256Hash(value.Span)
            ? Convert.ToHexString(value.Span)
            : string.Empty;
    }

    public static string ToCanonicalCircuitNonceHex(ReadOnlyMemory<byte> value)
    {
        return OverlayBridgeChannelHelloContracts.IsValidCircuitNonce(value.Span)
            ? Convert.ToHexString(value.Span)
            : string.Empty;
    }

    public static bool IsCanonicalHex(string? value, int byteLength, bool rejectAllZero)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length != byteLength * 2)
        {
            return false;
        }

        foreach (var character in value)
        {
            if (!(character is >= '0' and <= '9' or >= 'A' and <= 'F'))
            {
                return false;
            }
        }

        return !rejectAllZero || value.Any(character => character != '0');
    }
}
