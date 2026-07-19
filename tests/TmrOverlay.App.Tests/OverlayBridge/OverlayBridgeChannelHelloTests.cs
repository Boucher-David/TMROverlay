using System.Formats.Cbor;
using TmrOverlay.Core.OverlayBridge;
using Xunit;

namespace TmrOverlay.App.Tests.OverlayBridge;

public sealed class OverlayBridgeChannelHelloTests
{
    [Fact]
    public void EncodeDecode_RoundTripsBoundedCanonicalHello()
    {
        var hello = CreateBinding(OverlayBridgeChannelEndpointRole.Publisher).CreateLocalHello();

        var encoded = OverlayBridgeChannelHelloCborCodec.Encode(hello);

        Assert.True(OverlayBridgeChannelHelloCborCodec.TryDecode(encoded, out var decoded, out var error));
        Assert.Equal(OverlayBridgeChannelHelloDecodeError.None, error);
        Assert.NotNull(decoded);
        Assert.Equal(hello.ProtocolVersion, decoded!.ProtocolVersion);
        Assert.Equal(hello.RoomId, decoded.RoomId);
        Assert.Equal(hello.StreamId, decoded.StreamId);
        Assert.Equal(hello.Session, decoded.Session);
        Assert.Equal(hello.OwnerPolicyHash.ToArray(), decoded.OwnerPolicyHash.ToArray());
        Assert.Equal(hello.OwnerPolicyEpoch, decoded.OwnerPolicyEpoch);
        Assert.Equal(hello.PublisherLeaseId, decoded.PublisherLeaseId);
        Assert.Equal(hello.PublisherLeaseEpoch, decoded.PublisherLeaseEpoch);
        Assert.Equal(OverlayBridgeChannelEndpointRole.Publisher, decoded.EndpointRole);
        Assert.Equal(OverlayBridgeChannelEndpointRole.Viewer, decoded.ExpectedPeerRole);
        Assert.Equal(hello.CircuitId, decoded.CircuitId);
        Assert.Equal(hello.CircuitNonce.ToArray(), decoded.CircuitNonce.ToArray());
        Assert.Equal(hello.NegotiatedCapabilities, decoded.NegotiatedCapabilities);
        Assert.Equal(encoded, OverlayBridgeChannelHelloCborCodec.Encode(decoded));
    }

    [Fact]
    public void TryDecode_RejectsDuplicateMissingAndUnknownRequiredFields()
    {
        // Both payloads are deliberately hand formed because a canonical writer prevents the
        // duplicate/unknown required-key cases from being emitted by normal code.
        var duplicate = new byte[] { 0xa2, 0x01, 0x01, 0x01, 0x01 };
        var unknownRequired = new byte[] { 0xa1, 0x0f, 0x01 };
        var missing = new byte[] { 0xa1, 0x01, 0x01 };

        Assert.False(OverlayBridgeChannelHelloCborCodec.TryDecode(duplicate, out _, out var duplicateError));
        Assert.Contains(duplicateError, new[]
        {
            OverlayBridgeChannelHelloDecodeError.DuplicateKey,
            OverlayBridgeChannelHelloDecodeError.InvalidCbor
        });
        Assert.False(OverlayBridgeChannelHelloCborCodec.TryDecode(unknownRequired, out _, out var unknownError));
        Assert.Equal(OverlayBridgeChannelHelloDecodeError.UnknownRequiredField, unknownError);
        Assert.False(OverlayBridgeChannelHelloCborCodec.TryDecode(missing, out _, out var missingError));
        Assert.Equal(OverlayBridgeChannelHelloDecodeError.MissingRequiredField, missingError);
    }

    [Fact]
    public void TryDecode_RejectsOversizedPayloadAndOversizedRequiredField()
    {
        var oversizedPayload = new byte[OverlayBridgeChannelHelloContracts.MaximumEncodedBytes + 1];
        Assert.False(OverlayBridgeChannelHelloCborCodec.TryDecode(oversizedPayload, out _, out var oversizedPayloadError));
        Assert.Equal(OverlayBridgeChannelHelloDecodeError.OversizedPayload, oversizedPayloadError);

        var encoded = OverlayBridgeChannelHelloCborCodec.Encode(
            CreateBinding(OverlayBridgeChannelEndpointRole.Publisher).CreateLocalHello());
        var oversizedRoom = RewriteRootTextField(
            encoded,
            OverlayBridgeCborKeyRegistry.ChannelHello.RoomId,
            new string('r', OverlayBridgeFactContracts.MaxOpaqueIdentifierLength + 1));

        Assert.False(OverlayBridgeChannelHelloCborCodec.TryDecode(oversizedRoom, out _, out var oversizedFieldError));
        Assert.Equal(OverlayBridgeChannelHelloDecodeError.ValueOutOfRange, oversizedFieldError);
    }

    [Fact]
    public void Validator_AcceptsOnlyExactOppositeRoleBindingAndOnlyOnce()
    {
        var publisher = CreateBinding(OverlayBridgeChannelEndpointRole.Publisher);
        var viewer = CreateBinding(OverlayBridgeChannelEndpointRole.Viewer);
        var validator = new OverlayBridgeChannelHelloValidator(publisher);

        var accepted = validator.ValidatePeer(viewer.CreateLocalHello());
        var duplicate = validator.ValidatePeer(viewer.CreateLocalHello());

        Assert.True(accepted.IsAccepted);
        Assert.Equal(OverlayBridgeChannelHelloPeerValidationError.None, accepted.Error);
        Assert.False(duplicate.IsAccepted);
        Assert.Equal(OverlayBridgeChannelHelloPeerValidationError.DuplicateHello, duplicate.Error);
    }

    [Fact]
    public void Validator_RejectsCircuitNoncePolicySessionLeaseAndRoleMixups()
    {
        var publisher = CreateBinding(OverlayBridgeChannelEndpointRole.Publisher);
        var peer = CreateBinding(OverlayBridgeChannelEndpointRole.Viewer).CreateLocalHello();

        Assert.Equal(
            OverlayBridgeChannelHelloPeerValidationError.CircuitNonceMismatch,
            Validate(publisher, peer, circuitNonce: NewBytes(31)).Error);
        Assert.Equal(
            OverlayBridgeChannelHelloPeerValidationError.OwnerPolicyHashMismatch,
            Validate(publisher, peer, ownerPolicyHash: NewBytes(71)).Error);
        Assert.Equal(
            OverlayBridgeChannelHelloPeerValidationError.SessionMismatch,
            Validate(
                publisher,
                peer,
                session: peer.Session with { SessionEpoch = peer.Session.SessionEpoch + 1 }).Error);
        Assert.Equal(
            OverlayBridgeChannelHelloPeerValidationError.PublisherLeaseEpochMismatch,
            Validate(publisher, peer, publisherLeaseEpoch: peer.PublisherLeaseEpoch + 1).Error);
        Assert.Equal(
            OverlayBridgeChannelHelloPeerValidationError.NegotiatedCapabilitiesMismatch,
            Validate(publisher, peer, negotiatedCapabilities: OverlayBridgeCapability.Environment).Error);
        Assert.Equal(
            OverlayBridgeChannelHelloPeerValidationError.EndpointRoleMismatch,
            Validate(
                publisher,
                peer,
                endpointRole: OverlayBridgeChannelEndpointRole.Publisher,
                expectedPeerRole: OverlayBridgeChannelEndpointRole.Viewer).Error);
    }

    [Fact]
    public void Validator_RejectsARecentCircuitNonceOnAnotherOtherwiseBoundCircuit()
    {
        var nonceCache = new OverlayBridgeChannelNonceReplayCache(maximumEntries: 2);
        var firstPublisher = CreateBinding(OverlayBridgeChannelEndpointRole.Publisher);
        var firstViewer = CreateBinding(OverlayBridgeChannelEndpointRole.Viewer);
        var first = new OverlayBridgeChannelHelloValidator(firstPublisher, nonceCache)
            .ValidatePeer(firstViewer.CreateLocalHello());

        var secondCircuitId = Guid.Parse("ffb48637-4c12-455f-8f5f-98652d53d9ad");
        var secondPublisher = CreateBinding(
            OverlayBridgeChannelEndpointRole.Publisher,
            circuitId: secondCircuitId);
        var secondViewer = CreateBinding(
            OverlayBridgeChannelEndpointRole.Viewer,
            circuitId: secondCircuitId);
        var duplicateNonce = new OverlayBridgeChannelHelloValidator(secondPublisher, nonceCache)
            .ValidatePeer(secondViewer.CreateLocalHello());

        Assert.True(first.IsAccepted);
        Assert.False(duplicateNonce.IsAccepted);
        Assert.Equal(OverlayBridgeChannelHelloPeerValidationError.DuplicateCircuitNonce, duplicateNonce.Error);
    }

    [Fact]
    public void TryValidate_RejectsInvalidNonceAndOversizedOpaqueIdentifierBeforeEncoding()
    {
        var binding = CreateBinding(OverlayBridgeChannelEndpointRole.Publisher);
        var invalidNonce = new OverlayBridgeChannelHello(
            binding.ProtocolVersion,
            binding.RoomId,
            binding.StreamId,
            binding.Session,
            binding.OwnerPolicyHash,
            binding.OwnerPolicyEpoch,
            binding.PublisherLeaseId,
            binding.PublisherLeaseEpoch,
            OverlayBridgeChannelEndpointRole.Publisher,
            OverlayBridgeChannelEndpointRole.Viewer,
            binding.CircuitId,
            new byte[OverlayBridgeChannelHelloContracts.CircuitNonceBytes],
            binding.NegotiatedCapabilities);
        var oversizedRoom = new OverlayBridgeChannelHello(
            binding.ProtocolVersion,
            new string('r', OverlayBridgeFactContracts.MaxOpaqueIdentifierLength + 1),
            binding.StreamId,
            binding.Session,
            binding.OwnerPolicyHash,
            binding.OwnerPolicyEpoch,
            binding.PublisherLeaseId,
            binding.PublisherLeaseEpoch,
            OverlayBridgeChannelEndpointRole.Publisher,
            OverlayBridgeChannelEndpointRole.Viewer,
            binding.CircuitId,
            binding.CircuitNonce,
            binding.NegotiatedCapabilities);

        Assert.False(invalidNonce.TryValidate(out var invalidNonceError));
        Assert.Equal(OverlayBridgeChannelHelloValidationError.InvalidCircuitNonce, invalidNonceError);
        Assert.False(oversizedRoom.TryValidate(out var oversizedRoomError));
        Assert.Equal(OverlayBridgeChannelHelloValidationError.InvalidRoomIdentifier, oversizedRoomError);
        Assert.Throws<ArgumentException>(() => OverlayBridgeChannelHelloCborCodec.Encode(oversizedRoom));
    }

    private static OverlayBridgeChannelHelloValidationResult Validate(
        OverlayBridgeChannelHelloExpectedBinding expected,
        OverlayBridgeChannelHello peer,
        OverlayBridgeSessionBinding? session = null,
        byte[]? ownerPolicyHash = null,
        long? publisherLeaseEpoch = null,
        OverlayBridgeChannelEndpointRole? endpointRole = null,
        OverlayBridgeChannelEndpointRole? expectedPeerRole = null,
        byte[]? circuitNonce = null,
        OverlayBridgeCapability? negotiatedCapabilities = null)
    {
        var candidate = new OverlayBridgeChannelHello(
            peer.ProtocolVersion,
            peer.RoomId,
            peer.StreamId,
            session ?? peer.Session,
            ownerPolicyHash is { } policyHash ? (ReadOnlyMemory<byte>)policyHash : peer.OwnerPolicyHash,
            peer.OwnerPolicyEpoch,
            peer.PublisherLeaseId,
            publisherLeaseEpoch ?? peer.PublisherLeaseEpoch,
            endpointRole ?? peer.EndpointRole,
            expectedPeerRole ?? peer.ExpectedPeerRole,
            peer.CircuitId,
            circuitNonce is { } nonce ? (ReadOnlyMemory<byte>)nonce : peer.CircuitNonce,
            negotiatedCapabilities ?? peer.NegotiatedCapabilities);

        return new OverlayBridgeChannelHelloValidator(expected).ValidatePeer(candidate);
    }

    private static OverlayBridgeChannelHelloExpectedBinding CreateBinding(
        OverlayBridgeChannelEndpointRole localRole,
        Guid? circuitId = null)
    {
        return new OverlayBridgeChannelHelloExpectedBinding(
            OverlayBridgeProtocolVersion.Current,
            "room-Hello-1",
            "stream-teamcar-8",
            new OverlayBridgeSessionBinding("session-hello-1", 9, "track-lemans", "teamcar-8"),
            NewBytes(41),
            ownerPolicyEpoch: 14,
            publisherLeaseId: "lease-hello-6",
            publisherLeaseEpoch: 21,
            localEndpointRole: localRole,
            circuitId: circuitId ?? Guid.Parse("15f2b699-4ed2-4af0-9b8e-5f904ec97d98"),
            circuitNonce: NewBytes(91),
            negotiatedCapabilities: OverlayBridgeFactContracts.FirstRemoteReleaseCapabilities);
    }

    private static byte[] NewBytes(int firstByte)
    {
        return Enumerable.Range(firstByte, OverlayBridgeChannelHelloContracts.CircuitNonceBytes)
            .Select(value => unchecked((byte)value))
            .ToArray();
    }

    private static byte[] RewriteRootTextField(byte[] encoded, int fieldKey, string replacement)
    {
        var reader = new CborReader(encoded, CborConformanceMode.Canonical);
        var mapLength = reader.ReadStartMap();
        Assert.NotNull(mapLength);

        var writer = new CborWriter(CborConformanceMode.Canonical);
        writer.WriteStartMap(mapLength!.Value);
        for (var index = 0; index < mapLength.Value; index++)
        {
            var key = reader.ReadInt32();
            writer.WriteInt32(key);
            if (key == fieldKey)
            {
                _ = reader.ReadTextString();
                writer.WriteTextString(replacement);
            }
            else
            {
                writer.WriteEncodedValue(reader.ReadEncodedValue().Span);
            }
        }

        reader.ReadEndMap();
        writer.WriteEndMap();
        return writer.Encode();
    }
}
