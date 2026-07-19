using System.Formats.Cbor;

namespace TmrOverlay.Core.OverlayBridge;

/// <summary>
/// Canonical, bounded CBOR codec for the post-TLS <see cref="OverlayBridgeChannelHello"/>.
/// This is only a protected-channel control record; it neither opens a socket nor authenticates
/// the control-plane values it carries.
/// </summary>
internal static class OverlayBridgeChannelHelloCborCodec
{
    private const int RequiredFieldCount = 14;

    public static byte[] Encode(OverlayBridgeChannelHello hello)
    {
        ArgumentNullException.ThrowIfNull(hello);
        if (!hello.TryValidate(out var validationError))
        {
            throw new ArgumentException(
                $"Overlay Bridge channel Hello is not valid for encoding: {validationError}.",
                nameof(hello));
        }

        var writer = new CborWriter(CborConformanceMode.Canonical);
        writer.WriteStartMap(RequiredFieldCount);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.ChannelHello.ProtocolMajor);
        writer.WriteInt32(hello.ProtocolVersion.Major);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.ChannelHello.ProtocolMinor);
        writer.WriteInt32(hello.ProtocolVersion.Minor);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.ChannelHello.RoomId);
        writer.WriteTextString(hello.RoomId);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.ChannelHello.StreamId);
        writer.WriteTextString(hello.StreamId);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.ChannelHello.Session);
        WriteSession(writer, hello.Session);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.ChannelHello.OwnerPolicyHash);
        writer.WriteByteString(hello.OwnerPolicyHash.Span);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.ChannelHello.OwnerPolicyEpoch);
        writer.WriteInt64(hello.OwnerPolicyEpoch);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.ChannelHello.PublisherLeaseId);
        writer.WriteTextString(hello.PublisherLeaseId);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.ChannelHello.PublisherLeaseEpoch);
        writer.WriteInt64(hello.PublisherLeaseEpoch);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.ChannelHello.EndpointRole);
        writer.WriteInt32((int)hello.EndpointRole);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.ChannelHello.ExpectedPeerRole);
        writer.WriteInt32((int)hello.ExpectedPeerRole);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.ChannelHello.CircuitId);
        writer.WriteByteString(hello.CircuitId.ToByteArray(bigEndian: true));
        WriteKey(writer, OverlayBridgeCborKeyRegistry.ChannelHello.CircuitNonce);
        writer.WriteByteString(hello.CircuitNonce.Span);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.ChannelHello.NegotiatedCapabilities);
        writer.WriteInt32((int)hello.NegotiatedCapabilities);
        writer.WriteEndMap();

        var encoded = writer.Encode();
        if (encoded.Length > OverlayBridgeChannelHelloContracts.MaximumEncodedBytes)
        {
            throw new InvalidOperationException("Overlay Bridge channel Hello exceeded its encoded size limit.");
        }

        return encoded;
    }

    public static bool TryDecode(
        ReadOnlyMemory<byte> payload,
        out OverlayBridgeChannelHello? hello,
        out OverlayBridgeChannelHelloDecodeError error)
    {
        hello = null;
        if (payload.IsEmpty)
        {
            error = OverlayBridgeChannelHelloDecodeError.EmptyPayload;
            return false;
        }

        if (payload.Length > OverlayBridgeChannelHelloContracts.MaximumEncodedBytes)
        {
            error = OverlayBridgeChannelHelloDecodeError.OversizedPayload;
            return false;
        }

        try
        {
            var reader = new CborReader(payload, CborConformanceMode.Canonical);
            var decoded = ReadHello(reader);
            if (reader.BytesRemaining != 0)
            {
                error = OverlayBridgeChannelHelloDecodeError.TrailingData;
                return false;
            }

            if (!decoded.TryValidate(out var validationError))
            {
                error = validationError == OverlayBridgeChannelHelloValidationError.UnsupportedProtocol
                    ? OverlayBridgeChannelHelloDecodeError.UnsupportedProtocol
                    : OverlayBridgeChannelHelloDecodeError.InvalidHello;
                return false;
            }

            hello = decoded;
            error = OverlayBridgeChannelHelloDecodeError.None;
            return true;
        }
        catch (OverlayBridgeChannelHelloReadException exception)
        {
            error = exception.Error;
            return false;
        }
        catch (CborContentException)
        {
            error = OverlayBridgeChannelHelloDecodeError.InvalidCbor;
            return false;
        }
        catch (ArgumentException)
        {
            error = OverlayBridgeChannelHelloDecodeError.InvalidCbor;
            return false;
        }
        catch (OverflowException)
        {
            error = OverlayBridgeChannelHelloDecodeError.InvalidCbor;
            return false;
        }
        catch (InvalidOperationException)
        {
            error = OverlayBridgeChannelHelloDecodeError.InvalidCbor;
            return false;
        }
    }

    private static OverlayBridgeChannelHello ReadHello(CborReader reader)
    {
        var mapLength = reader.ReadStartMap();
        if (mapLength is null
            || mapLength.Value < RequiredFieldCount
            || mapLength.Value > OverlayBridgeChannelHelloContracts.MaximumMapEntries)
        {
            Fail(OverlayBridgeChannelHelloDecodeError.InvalidMapShape);
        }

        var seen = new bool[RequiredFieldCount + 1];
        var optionalKeys = new HashSet<int>();
        var protocolMajor = 0;
        var protocolMinor = 0;
        string? roomId = null;
        string? streamId = null;
        OverlayBridgeSessionBinding? session = null;
        byte[]? ownerPolicyHash = null;
        long ownerPolicyEpoch = 0;
        string? publisherLeaseId = null;
        long publisherLeaseEpoch = 0;
        var endpointRole = default(OverlayBridgeChannelEndpointRole);
        var expectedPeerRole = default(OverlayBridgeChannelEndpointRole);
        var circuitId = Guid.Empty;
        byte[]? circuitNonce = null;
        var negotiatedCapabilities = OverlayBridgeCapability.None;

        for (var index = 0; index < mapLength!.Value; index++)
        {
            var key = reader.ReadInt32();
            if (key < 0)
            {
                Fail(OverlayBridgeChannelHelloDecodeError.UnknownRequiredField);
            }

            if (key <= RequiredFieldCount)
            {
                if (seen[key])
                {
                    Fail(OverlayBridgeChannelHelloDecodeError.DuplicateKey);
                }

                seen[key] = true;
            }

            switch (key)
            {
                case OverlayBridgeCborKeyRegistry.ChannelHello.ProtocolMajor:
                    protocolMajor = reader.ReadInt32();
                    break;
                case OverlayBridgeCborKeyRegistry.ChannelHello.ProtocolMinor:
                    protocolMinor = reader.ReadInt32();
                    break;
                case OverlayBridgeCborKeyRegistry.ChannelHello.RoomId:
                    roomId = ReadTextString(reader);
                    break;
                case OverlayBridgeCborKeyRegistry.ChannelHello.StreamId:
                    streamId = ReadTextString(reader);
                    break;
                case OverlayBridgeCborKeyRegistry.ChannelHello.Session:
                    session = ReadSession(reader);
                    break;
                case OverlayBridgeCborKeyRegistry.ChannelHello.OwnerPolicyHash:
                    ownerPolicyHash = ReadByteString(reader, OverlayBridgeChannelHelloContracts.Sha256HashBytes);
                    break;
                case OverlayBridgeCborKeyRegistry.ChannelHello.OwnerPolicyEpoch:
                    ownerPolicyEpoch = reader.ReadInt64();
                    break;
                case OverlayBridgeCborKeyRegistry.ChannelHello.PublisherLeaseId:
                    publisherLeaseId = ReadTextString(reader);
                    break;
                case OverlayBridgeCborKeyRegistry.ChannelHello.PublisherLeaseEpoch:
                    publisherLeaseEpoch = reader.ReadInt64();
                    break;
                case OverlayBridgeCborKeyRegistry.ChannelHello.EndpointRole:
                    endpointRole = (OverlayBridgeChannelEndpointRole)reader.ReadInt32();
                    break;
                case OverlayBridgeCborKeyRegistry.ChannelHello.ExpectedPeerRole:
                    expectedPeerRole = (OverlayBridgeChannelEndpointRole)reader.ReadInt32();
                    break;
                case OverlayBridgeCborKeyRegistry.ChannelHello.CircuitId:
                    circuitId = ReadGuid(reader);
                    break;
                case OverlayBridgeCborKeyRegistry.ChannelHello.CircuitNonce:
                    circuitNonce = ReadByteString(reader, OverlayBridgeChannelHelloContracts.CircuitNonceBytes);
                    break;
                case OverlayBridgeCborKeyRegistry.ChannelHello.NegotiatedCapabilities:
                    negotiatedCapabilities = (OverlayBridgeCapability)reader.ReadInt32();
                    break;
                default:
                    if (key < OverlayBridgeCborKeyRegistry.OptionalFieldKeyStart)
                    {
                        Fail(OverlayBridgeChannelHelloDecodeError.UnknownRequiredField);
                    }

                    if (!optionalKeys.Add(key))
                    {
                        Fail(OverlayBridgeChannelHelloDecodeError.DuplicateKey);
                    }

                    _ = reader.ReadEncodedValue();
                    break;
            }
        }

        reader.ReadEndMap();
        if (seen.Skip(1).Any(value => !value))
        {
            Fail(OverlayBridgeChannelHelloDecodeError.MissingRequiredField);
        }

        return new OverlayBridgeChannelHello(
            new OverlayBridgeProtocolVersion(protocolMajor, protocolMinor),
            roomId!,
            streamId!,
            session!,
            ownerPolicyHash!,
            ownerPolicyEpoch,
            publisherLeaseId!,
            publisherLeaseEpoch,
            endpointRole,
            expectedPeerRole,
            circuitId,
            circuitNonce!,
            negotiatedCapabilities);
    }

    private static OverlayBridgeSessionBinding ReadSession(CborReader reader)
    {
        var mapLength = reader.ReadStartMap();
        if (mapLength is null || mapLength.Value < 4 || mapLength.Value > 8)
        {
            Fail(OverlayBridgeChannelHelloDecodeError.InvalidMapShape);
        }

        var seen = new bool[5];
        var optionalKeys = new HashSet<int>();
        string? sessionId = null;
        long sessionEpoch = 0;
        string? trackKey = null;
        string? teamCarKey = null;

        for (var index = 0; index < mapLength!.Value; index++)
        {
            var key = reader.ReadInt32();
            if (key < 0)
            {
                Fail(OverlayBridgeChannelHelloDecodeError.UnknownRequiredField);
            }

            if (key <= 4)
            {
                if (seen[key])
                {
                    Fail(OverlayBridgeChannelHelloDecodeError.DuplicateKey);
                }

                seen[key] = true;
            }

            switch (key)
            {
                case OverlayBridgeCborKeyRegistry.SessionBinding.SessionId:
                    sessionId = ReadTextString(reader);
                    break;
                case OverlayBridgeCborKeyRegistry.SessionBinding.SessionEpoch:
                    sessionEpoch = reader.ReadInt64();
                    break;
                case OverlayBridgeCborKeyRegistry.SessionBinding.TrackKey:
                    trackKey = ReadTextString(reader);
                    break;
                case OverlayBridgeCborKeyRegistry.SessionBinding.TeamCarKey:
                    teamCarKey = ReadTextString(reader);
                    break;
                default:
                    if (key < OverlayBridgeCborKeyRegistry.OptionalFieldKeyStart)
                    {
                        Fail(OverlayBridgeChannelHelloDecodeError.UnknownRequiredField);
                    }

                    if (!optionalKeys.Add(key))
                    {
                        Fail(OverlayBridgeChannelHelloDecodeError.DuplicateKey);
                    }

                    _ = reader.ReadEncodedValue();
                    break;
            }
        }

        reader.ReadEndMap();
        if (seen.Skip(1).Any(value => !value))
        {
            Fail(OverlayBridgeChannelHelloDecodeError.MissingRequiredField);
        }

        return new OverlayBridgeSessionBinding(sessionId!, sessionEpoch, trackKey!, teamCarKey!);
    }

    private static string ReadTextString(CborReader reader)
    {
        var value = reader.ReadTextString();
        if (value.Length > OverlayBridgeFactContracts.MaxOpaqueIdentifierLength)
        {
            Fail(OverlayBridgeChannelHelloDecodeError.ValueOutOfRange);
        }

        return value;
    }

    private static byte[] ReadByteString(CborReader reader, int exactLength)
    {
        var value = reader.ReadByteString();
        if (value.Length != exactLength)
        {
            Fail(OverlayBridgeChannelHelloDecodeError.ValueOutOfRange);
        }

        return value;
    }

    private static Guid ReadGuid(CborReader reader)
    {
        var bytes = ReadByteString(reader, 16);
        return new Guid(bytes, bigEndian: true);
    }

    private static void WriteSession(CborWriter writer, OverlayBridgeSessionBinding session)
    {
        writer.WriteStartMap(4);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.SessionBinding.SessionId);
        writer.WriteTextString(session.SessionId);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.SessionBinding.SessionEpoch);
        writer.WriteInt64(session.SessionEpoch);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.SessionBinding.TrackKey);
        writer.WriteTextString(session.TrackKey);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.SessionBinding.TeamCarKey);
        writer.WriteTextString(session.TeamCarKey);
        writer.WriteEndMap();
    }

    private static void WriteKey(CborWriter writer, int key)
    {
        writer.WriteInt32(key);
    }

    private static void Fail(OverlayBridgeChannelHelloDecodeError error)
    {
        throw new OverlayBridgeChannelHelloReadException(error);
    }

    private sealed class OverlayBridgeChannelHelloReadException : Exception
    {
        public OverlayBridgeChannelHelloReadException(OverlayBridgeChannelHelloDecodeError error)
        {
            Error = error;
        }

        public OverlayBridgeChannelHelloDecodeError Error { get; }
    }
}

/// <summary>Safe diagnostics categories for malformed protected-channel Hello records.</summary>
internal enum OverlayBridgeChannelHelloDecodeError
{
    None = 0,
    EmptyPayload = 1,
    OversizedPayload = 2,
    InvalidCbor = 3,
    TrailingData = 4,
    InvalidMapShape = 5,
    DuplicateKey = 6,
    MissingRequiredField = 7,
    UnknownRequiredField = 8,
    ValueOutOfRange = 9,
    UnsupportedProtocol = 10,
    InvalidHello = 11
}
