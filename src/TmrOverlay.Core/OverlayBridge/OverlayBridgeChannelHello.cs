namespace TmrOverlay.Core.OverlayBridge;

/// <summary>
/// The endpoint role carried inside a protected Bridge circuit. The active Publisher is the
/// inner TLS server and a Viewer is the inner TLS client; the role pair is repeated in Hello to
/// prevent a reflected or cross-wired circuit from being treated as a valid peer.
/// </summary>
internal enum OverlayBridgeChannelEndpointRole
{
    Publisher = 1,
    Viewer = 2
}

/// <summary>
/// Bounded, canonical control record exchanged immediately after a mutually authenticated inner
/// TLS handshake. It binds the protected byte circuit to the relay/control-plane decision before
/// any sector publication can be read. It deliberately contains no telemetry, settings, capture,
/// history, pairing secret, certificate, or private-key material.
/// </summary>
internal sealed record OverlayBridgeChannelHello
{
    public OverlayBridgeChannelHello(
        OverlayBridgeProtocolVersion protocolVersion,
        string roomId,
        string streamId,
        OverlayBridgeSessionBinding session,
        ReadOnlyMemory<byte> ownerPolicyHash,
        long ownerPolicyEpoch,
        string publisherLeaseId,
        long publisherLeaseEpoch,
        OverlayBridgeChannelEndpointRole endpointRole,
        OverlayBridgeChannelEndpointRole expectedPeerRole,
        Guid circuitId,
        ReadOnlyMemory<byte> circuitNonce,
        OverlayBridgeCapability negotiatedCapabilities)
    {
        ProtocolVersion = protocolVersion;
        RoomId = roomId;
        StreamId = streamId;
        Session = session;
        OwnerPolicyHash = ownerPolicyHash.ToArray();
        OwnerPolicyEpoch = ownerPolicyEpoch;
        PublisherLeaseId = publisherLeaseId;
        PublisherLeaseEpoch = publisherLeaseEpoch;
        EndpointRole = endpointRole;
        ExpectedPeerRole = expectedPeerRole;
        CircuitId = circuitId;
        CircuitNonce = circuitNonce.ToArray();
        NegotiatedCapabilities = negotiatedCapabilities;
    }

    public OverlayBridgeProtocolVersion ProtocolVersion { get; }

    public string RoomId { get; }

    public string StreamId { get; }

    public OverlayBridgeSessionBinding Session { get; }

    /// <summary>Exactly 32 SHA-256 bytes, copied on construction.</summary>
    public ReadOnlyMemory<byte> OwnerPolicyHash { get; }

    public long OwnerPolicyEpoch { get; }

    public string PublisherLeaseId { get; }

    public long PublisherLeaseEpoch { get; }

    public OverlayBridgeChannelEndpointRole EndpointRole { get; }

    public OverlayBridgeChannelEndpointRole ExpectedPeerRole { get; }

    public Guid CircuitId { get; }

    /// <summary>Exactly 32 random bytes, copied on construction.</summary>
    public ReadOnlyMemory<byte> CircuitNonce { get; }

    /// <summary>The exact control-plane capability intersection for this circuit.</summary>
    public OverlayBridgeCapability NegotiatedCapabilities { get; }

    public bool TryValidate(out OverlayBridgeChannelHelloValidationError error)
    {
        if (ProtocolVersion is null
            || ProtocolVersion.Major != OverlayBridgeProtocolVersion.Current.Major
            || ProtocolVersion.Minor < 0)
        {
            error = OverlayBridgeChannelHelloValidationError.UnsupportedProtocol;
            return false;
        }

        if (!OverlayBridgeChannelHelloContracts.IsValidOpaqueIdentifier(RoomId))
        {
            error = OverlayBridgeChannelHelloValidationError.InvalidRoomIdentifier;
            return false;
        }

        if (!OverlayBridgeChannelHelloContracts.IsValidOpaqueIdentifier(StreamId))
        {
            error = OverlayBridgeChannelHelloValidationError.InvalidStreamIdentifier;
            return false;
        }

        if (!TryValidateSession(Session))
        {
            error = OverlayBridgeChannelHelloValidationError.InvalidSessionBinding;
            return false;
        }

        if (!OverlayBridgeChannelHelloContracts.IsValidSha256Hash(OwnerPolicyHash.Span))
        {
            error = OverlayBridgeChannelHelloValidationError.InvalidOwnerPolicyHash;
            return false;
        }

        if (OwnerPolicyEpoch <= 0)
        {
            error = OverlayBridgeChannelHelloValidationError.InvalidOwnerPolicyEpoch;
            return false;
        }

        if (!OverlayBridgeChannelHelloContracts.IsValidOpaqueIdentifier(PublisherLeaseId))
        {
            error = OverlayBridgeChannelHelloValidationError.InvalidPublisherLeaseIdentifier;
            return false;
        }

        if (PublisherLeaseEpoch <= 0)
        {
            error = OverlayBridgeChannelHelloValidationError.InvalidPublisherLeaseEpoch;
            return false;
        }

        if (!OverlayBridgeChannelHelloContracts.TryGetOppositeRole(EndpointRole, out var oppositeRole)
            || ExpectedPeerRole != oppositeRole)
        {
            error = OverlayBridgeChannelHelloValidationError.InvalidEndpointRoles;
            return false;
        }

        if (CircuitId == Guid.Empty)
        {
            error = OverlayBridgeChannelHelloValidationError.InvalidCircuitIdentifier;
            return false;
        }

        if (!OverlayBridgeChannelHelloContracts.IsValidCircuitNonce(CircuitNonce.Span))
        {
            error = OverlayBridgeChannelHelloValidationError.InvalidCircuitNonce;
            return false;
        }

        if (!OverlayBridgeFactContracts.IsKnownCapabilitySet(NegotiatedCapabilities))
        {
            error = OverlayBridgeChannelHelloValidationError.InvalidNegotiatedCapabilities;
            return false;
        }

        error = OverlayBridgeChannelHelloValidationError.None;
        return true;
    }

    private static bool TryValidateSession(OverlayBridgeSessionBinding? session)
    {
        return session is not null
            && OverlayBridgeChannelHelloContracts.IsValidOpaqueIdentifier(session.SessionId)
            && session.SessionEpoch > 0
            && OverlayBridgeChannelHelloContracts.IsValidOpaqueIdentifier(session.TrackKey)
            && OverlayBridgeChannelHelloContracts.IsValidOpaqueIdentifier(session.TeamCarKey);
    }
}

/// <summary>
/// Immutable, control-plane-authenticated binding expected by one local circuit endpoint. The
/// future transport host obtains these values only after device approval, policy verification,
/// lease selection, and circuit creation; this Core type does not claim to authenticate them.
/// </summary>
internal sealed record OverlayBridgeChannelHelloExpectedBinding
{
    public OverlayBridgeChannelHelloExpectedBinding(
        OverlayBridgeProtocolVersion protocolVersion,
        string roomId,
        string streamId,
        OverlayBridgeSessionBinding session,
        ReadOnlyMemory<byte> ownerPolicyHash,
        long ownerPolicyEpoch,
        string publisherLeaseId,
        long publisherLeaseEpoch,
        OverlayBridgeChannelEndpointRole localEndpointRole,
        Guid circuitId,
        ReadOnlyMemory<byte> circuitNonce,
        OverlayBridgeCapability negotiatedCapabilities)
    {
        ProtocolVersion = protocolVersion;
        RoomId = roomId;
        StreamId = streamId;
        Session = session;
        OwnerPolicyHash = ownerPolicyHash.ToArray();
        OwnerPolicyEpoch = ownerPolicyEpoch;
        PublisherLeaseId = publisherLeaseId;
        PublisherLeaseEpoch = publisherLeaseEpoch;
        LocalEndpointRole = localEndpointRole;
        CircuitId = circuitId;
        CircuitNonce = circuitNonce.ToArray();
        NegotiatedCapabilities = negotiatedCapabilities;
    }

    public OverlayBridgeProtocolVersion ProtocolVersion { get; }

    public string RoomId { get; }

    public string StreamId { get; }

    public OverlayBridgeSessionBinding Session { get; }

    public ReadOnlyMemory<byte> OwnerPolicyHash { get; }

    public long OwnerPolicyEpoch { get; }

    public string PublisherLeaseId { get; }

    public long PublisherLeaseEpoch { get; }

    public OverlayBridgeChannelEndpointRole LocalEndpointRole { get; }

    public Guid CircuitId { get; }

    public ReadOnlyMemory<byte> CircuitNonce { get; }

    public OverlayBridgeCapability NegotiatedCapabilities { get; }

    public OverlayBridgeChannelHello CreateLocalHello()
    {
        if (!OverlayBridgeChannelHelloContracts.TryGetOppositeRole(LocalEndpointRole, out var peerRole))
        {
            throw new InvalidOperationException("The expected Bridge channel endpoint role is not valid.");
        }

        return new OverlayBridgeChannelHello(
            ProtocolVersion,
            RoomId,
            StreamId,
            Session,
            OwnerPolicyHash,
            OwnerPolicyEpoch,
            PublisherLeaseId,
            PublisherLeaseEpoch,
            LocalEndpointRole,
            peerRole,
            CircuitId,
            CircuitNonce,
            NegotiatedCapabilities);
    }

    public bool TryValidate(out OverlayBridgeChannelHelloValidationError error)
    {
        return CreateValidationHello().TryValidate(out error);
    }

    private OverlayBridgeChannelHello CreateValidationHello()
    {
        if (!OverlayBridgeChannelHelloContracts.TryGetOppositeRole(LocalEndpointRole, out var peerRole))
        {
            return new OverlayBridgeChannelHello(
                ProtocolVersion,
                RoomId,
                StreamId,
                Session,
                OwnerPolicyHash,
                OwnerPolicyEpoch,
                PublisherLeaseId,
                PublisherLeaseEpoch,
                LocalEndpointRole,
                LocalEndpointRole,
                CircuitId,
                CircuitNonce,
                NegotiatedCapabilities);
        }

        return new OverlayBridgeChannelHello(
            ProtocolVersion,
            RoomId,
            StreamId,
            Session,
            OwnerPolicyHash,
            OwnerPolicyEpoch,
            PublisherLeaseId,
            PublisherLeaseEpoch,
            LocalEndpointRole,
            peerRole,
            CircuitId,
            CircuitNonce,
            NegotiatedCapabilities);
    }
}

/// <summary>
/// Shared bounds for the post-TLS Hello. This is intentionally far smaller than a sector
/// publication: a Hello only binds metadata and must be cheap to reject before any facts flow.
/// </summary>
internal static class OverlayBridgeChannelHelloContracts
{
    public const int MaximumEncodedBytes = 4 * 1024;
    public const int Sha256HashBytes = 32;
    public const int CircuitNonceBytes = 32;
    public const int MaximumMapEntries = 16;

    public static bool IsValidOpaqueIdentifier(string? value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && value.Length <= OverlayBridgeFactContracts.MaxOpaqueIdentifierLength;
    }

    public static bool IsValidSha256Hash(ReadOnlySpan<byte> value)
    {
        return value.Length == Sha256HashBytes;
    }

    public static bool IsValidCircuitNonce(ReadOnlySpan<byte> value)
    {
        return value.Length == CircuitNonceBytes
            && value.IndexOfAnyExcept((byte)0) >= 0;
    }

    public static bool TryGetOppositeRole(
        OverlayBridgeChannelEndpointRole role,
        out OverlayBridgeChannelEndpointRole oppositeRole)
    {
        switch (role)
        {
            case OverlayBridgeChannelEndpointRole.Publisher:
                oppositeRole = OverlayBridgeChannelEndpointRole.Viewer;
                return true;
            case OverlayBridgeChannelEndpointRole.Viewer:
                oppositeRole = OverlayBridgeChannelEndpointRole.Publisher;
                return true;
            default:
                oppositeRole = default;
                return false;
        }
    }
}

internal enum OverlayBridgeChannelHelloValidationError
{
    None = 0,
    UnsupportedProtocol = 1,
    InvalidRoomIdentifier = 2,
    InvalidStreamIdentifier = 3,
    InvalidSessionBinding = 4,
    InvalidOwnerPolicyHash = 5,
    InvalidOwnerPolicyEpoch = 6,
    InvalidPublisherLeaseIdentifier = 7,
    InvalidPublisherLeaseEpoch = 8,
    InvalidEndpointRoles = 9,
    InvalidCircuitIdentifier = 10,
    InvalidCircuitNonce = 11,
    InvalidNegotiatedCapabilities = 12
}
