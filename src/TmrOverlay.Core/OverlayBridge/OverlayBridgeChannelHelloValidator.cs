namespace TmrOverlay.Core.OverlayBridge;

/// <summary>
/// Per-circuit Hello gate. A transport host constructs it from a control-plane-authenticated
/// binding and calls <see cref="ValidatePeer"/> exactly once before allowing framed facts onto
/// the circuit. A rejected Hello never changes remote fact-composition state.
/// </summary>
internal sealed class OverlayBridgeChannelHelloValidator
{
    private readonly object _gate = new();
    private readonly OverlayBridgeChannelHelloExpectedBinding _expected;
    private readonly OverlayBridgeChannelNonceReplayCache? _nonceReplayCache;
    private bool _peerHelloAccepted;

    public OverlayBridgeChannelHelloValidator(
        OverlayBridgeChannelHelloExpectedBinding expected,
        OverlayBridgeChannelNonceReplayCache? nonceReplayCache = null)
    {
        _expected = expected ?? throw new ArgumentNullException(nameof(expected));
        _nonceReplayCache = nonceReplayCache;
    }

    public OverlayBridgeChannelHelloValidationResult ValidatePeer(OverlayBridgeChannelHello? peerHello)
    {
        lock (_gate)
        {
            if (_peerHelloAccepted)
            {
                return OverlayBridgeChannelHelloValidationResult.Rejected(
                    OverlayBridgeChannelHelloPeerValidationError.DuplicateHello);
            }

            if (!_expected.TryValidate(out _))
            {
                return OverlayBridgeChannelHelloValidationResult.Rejected(
                    OverlayBridgeChannelHelloPeerValidationError.InvalidExpectedBinding);
            }

            if (peerHello is null || !peerHello.TryValidate(out _))
            {
                return OverlayBridgeChannelHelloValidationResult.Rejected(
                    OverlayBridgeChannelHelloPeerValidationError.InvalidPeerHello);
            }

            var mismatch = FindMismatch(peerHello);
            if (mismatch != OverlayBridgeChannelHelloPeerValidationError.None)
            {
                return OverlayBridgeChannelHelloValidationResult.Rejected(mismatch);
            }

            if (_nonceReplayCache is not null && !_nonceReplayCache.TryRegister(peerHello.CircuitNonce.Span))
            {
                return OverlayBridgeChannelHelloValidationResult.Rejected(
                    OverlayBridgeChannelHelloPeerValidationError.DuplicateCircuitNonce);
            }

            _peerHelloAccepted = true;
            return OverlayBridgeChannelHelloValidationResult.Accepted();
        }
    }

    private OverlayBridgeChannelHelloPeerValidationError FindMismatch(OverlayBridgeChannelHello peerHello)
    {
        if (_expected.ProtocolVersion.Major != peerHello.ProtocolVersion.Major
            || _expected.ProtocolVersion.Minor != peerHello.ProtocolVersion.Minor)
        {
            return OverlayBridgeChannelHelloPeerValidationError.ProtocolVersionMismatch;
        }

        if (!string.Equals(_expected.RoomId, peerHello.RoomId, StringComparison.Ordinal))
        {
            return OverlayBridgeChannelHelloPeerValidationError.RoomMismatch;
        }

        if (!string.Equals(_expected.StreamId, peerHello.StreamId, StringComparison.Ordinal))
        {
            return OverlayBridgeChannelHelloPeerValidationError.StreamMismatch;
        }

        if (!_expected.Session.Equals(peerHello.Session))
        {
            return OverlayBridgeChannelHelloPeerValidationError.SessionMismatch;
        }

        if (!_expected.OwnerPolicyHash.Span.SequenceEqual(peerHello.OwnerPolicyHash.Span))
        {
            return OverlayBridgeChannelHelloPeerValidationError.OwnerPolicyHashMismatch;
        }

        if (_expected.OwnerPolicyEpoch != peerHello.OwnerPolicyEpoch)
        {
            return OverlayBridgeChannelHelloPeerValidationError.OwnerPolicyEpochMismatch;
        }

        if (!string.Equals(_expected.PublisherLeaseId, peerHello.PublisherLeaseId, StringComparison.Ordinal))
        {
            return OverlayBridgeChannelHelloPeerValidationError.PublisherLeaseMismatch;
        }

        if (_expected.PublisherLeaseEpoch != peerHello.PublisherLeaseEpoch)
        {
            return OverlayBridgeChannelHelloPeerValidationError.PublisherLeaseEpochMismatch;
        }

        if (_expected.NegotiatedCapabilities != peerHello.NegotiatedCapabilities)
        {
            return OverlayBridgeChannelHelloPeerValidationError.NegotiatedCapabilitiesMismatch;
        }

        if (!OverlayBridgeChannelHelloContracts.TryGetOppositeRole(_expected.LocalEndpointRole, out var expectedPeerRole)
            || peerHello.EndpointRole != expectedPeerRole)
        {
            return OverlayBridgeChannelHelloPeerValidationError.EndpointRoleMismatch;
        }

        if (peerHello.ExpectedPeerRole != _expected.LocalEndpointRole)
        {
            return OverlayBridgeChannelHelloPeerValidationError.ExpectedPeerRoleMismatch;
        }

        if (_expected.CircuitId != peerHello.CircuitId)
        {
            return OverlayBridgeChannelHelloPeerValidationError.CircuitIdentifierMismatch;
        }

        if (!_expected.CircuitNonce.Span.SequenceEqual(peerHello.CircuitNonce.Span))
        {
            return OverlayBridgeChannelHelloPeerValidationError.CircuitNonceMismatch;
        }

        return OverlayBridgeChannelHelloPeerValidationError.None;
    }
}

internal sealed record OverlayBridgeChannelHelloValidationResult(
    bool IsAccepted,
    OverlayBridgeChannelHelloPeerValidationError Error)
{
    public static OverlayBridgeChannelHelloValidationResult Accepted() => new(
        IsAccepted: true,
        OverlayBridgeChannelHelloPeerValidationError.None);

    public static OverlayBridgeChannelHelloValidationResult Rejected(
        OverlayBridgeChannelHelloPeerValidationError error) => new(
        IsAccepted: false,
        error);
}

internal enum OverlayBridgeChannelHelloPeerValidationError
{
    None = 0,
    InvalidExpectedBinding = 1,
    InvalidPeerHello = 2,
    DuplicateHello = 3,
    ProtocolVersionMismatch = 4,
    RoomMismatch = 5,
    StreamMismatch = 6,
    SessionMismatch = 7,
    OwnerPolicyHashMismatch = 8,
    OwnerPolicyEpochMismatch = 9,
    PublisherLeaseMismatch = 10,
    PublisherLeaseEpochMismatch = 11,
    EndpointRoleMismatch = 12,
    ExpectedPeerRoleMismatch = 13,
    CircuitIdentifierMismatch = 14,
    CircuitNonceMismatch = 15,
    DuplicateCircuitNonce = 16,
    NegotiatedCapabilitiesMismatch = 17
}

/// <summary>
/// Small, bounded replay guard for circuit nonces accepted by one host/device. It is a
/// defense-in-depth check after the expected control-plane nonce comparison, not a source of
/// randomness or a substitute for a relay-issued fresh circuit binding.
/// </summary>
internal sealed class OverlayBridgeChannelNonceReplayCache
{
    private readonly object _gate = new();
    private readonly int _maximumEntries;
    private readonly Queue<string> _entries = new();
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);

    public OverlayBridgeChannelNonceReplayCache(int maximumEntries)
    {
        if (maximumEntries <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumEntries));
        }

        _maximumEntries = maximumEntries;
    }

    public bool TryRegister(ReadOnlySpan<byte> nonce)
    {
        if (!OverlayBridgeChannelHelloContracts.IsValidCircuitNonce(nonce))
        {
            return false;
        }

        var key = Convert.ToHexString(nonce);
        lock (_gate)
        {
            if (!_seen.Add(key))
            {
                return false;
            }

            _entries.Enqueue(key);
            while (_entries.Count > _maximumEntries)
            {
                _seen.Remove(_entries.Dequeue());
            }

            return true;
        }
    }
}
