using System.Formats.Cbor;

namespace TmrOverlay.Core.OverlayBridge;

/// <summary>
/// Canonical CBOR codec for the Owner-signed room-policy body.  The signature is deliberately
/// outside this object and is calculated over the exact bytes emitted here.  The decoder is a
/// bounded untrusted-input reader; it is not a reflection serializer or a settings reader.
/// </summary>
internal static class OverlayBridgeRoomPolicyCborCodec
{
    /// <summary>
    /// Independent policy-object version.  It is not the iRacing schema or the facts schema;
    /// a signed policy reader must reject a shape it cannot reproduce and verify exactly.
    /// </summary>
    public const int CurrentPolicyFormatVersion = 1;
    private const int MaximumPolicyBytes = 16 * 1024;
    private const int MaximumApprovedDeviceEntries = OverlayBridgeRoomPolicyValidation.MaximumApprovedDevices;
    private const int ExpectedRootMapEntries = 9;
    private const int ExpectedApprovedDeviceMapEntries = 3;

    public static byte[] Encode(OverlayBridgeRoomPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        if (!policy.TryValidate(out var validationError))
        {
            throw new ArgumentException($"Cannot encode invalid Overlay Bridge room policy: {validationError}.", nameof(policy));
        }

        var writer = new CborWriter(CborConformanceMode.Canonical);
        writer.WriteStartMap(ExpectedRootMapEntries);
        writer.WriteInt32(OverlayBridgeRoomPolicyCborKeys.PolicyFormatVersion);
        writer.WriteInt32(CurrentPolicyFormatVersion);
        writer.WriteInt32(OverlayBridgeRoomPolicyCborKeys.RoomId);
        writer.WriteTextString(policy.RoomId);
        writer.WriteInt32(OverlayBridgeRoomPolicyCborKeys.RoomInstanceId);
        writer.WriteTextString(policy.RoomInstanceId);
        writer.WriteInt32(OverlayBridgeRoomPolicyCborKeys.PolicyEpoch);
        writer.WriteInt64(policy.PolicyEpoch);
        writer.WriteInt32(OverlayBridgeRoomPolicyCborKeys.OwnerDeviceId);
        writer.WriteTextString(policy.OwnerBinding.DeviceId);
        writer.WriteInt32(OverlayBridgeRoomPolicyCborKeys.OwnerSpkiSha256);
        writer.WriteByteString(policy.OwnerBinding.SubjectPublicKeyInfoSha256.Span);
        writer.WriteInt32(OverlayBridgeRoomPolicyCborKeys.IssuedAtUtcTicks);
        writer.WriteInt64(policy.IssuedAtUtc.UtcDateTime.Ticks);
        writer.WriteInt32(OverlayBridgeRoomPolicyCborKeys.ExpiresAtUtcTicks);
        writer.WriteInt64(policy.ExpiresAtUtc.UtcDateTime.Ticks);
        writer.WriteInt32(OverlayBridgeRoomPolicyCborKeys.ApprovedDevices);
        writer.WriteStartArray(policy.ApprovedDevices.Count);
        foreach (var approved in policy.ApprovedDevices)
        {
            writer.WriteStartMap(ExpectedApprovedDeviceMapEntries);
            writer.WriteInt32(OverlayBridgeRoomPolicyCborKeys.ApprovedDevice.DeviceId);
            writer.WriteTextString(approved.Binding.DeviceId);
            writer.WriteInt32(OverlayBridgeRoomPolicyCborKeys.ApprovedDevice.SpkiSha256);
            writer.WriteByteString(approved.Binding.SubjectPublicKeyInfoSha256.Span);
            writer.WriteInt32(OverlayBridgeRoomPolicyCborKeys.ApprovedDevice.RoleAndCapabilities);
            writer.WriteStartArray(2);
            writer.WriteInt32((int)approved.Role);
            writer.WriteInt32((int)approved.GrantedCapabilities);
            writer.WriteEndArray();
            writer.WriteEndMap();
        }

        writer.WriteEndArray();
        writer.WriteEndMap();
        var encoded = writer.Encode();
        if (encoded.Length > MaximumPolicyBytes)
        {
            throw new InvalidOperationException("Overlay Bridge room policy exceeds its bounded canonical-CBOR limit.");
        }

        return encoded;
    }

    public static bool TryDecode(
        ReadOnlyMemory<byte> payload,
        out OverlayBridgeRoomPolicy? policy,
        out OverlayBridgeRoomPolicyDecodeError error)
    {
        policy = null;
        if (payload.IsEmpty)
        {
            error = OverlayBridgeRoomPolicyDecodeError.EmptyPayload;
            return false;
        }

        if (payload.Length > MaximumPolicyBytes)
        {
            error = OverlayBridgeRoomPolicyDecodeError.OversizedPayload;
            return false;
        }

        try
        {
            var reader = new CborReader(payload, CborConformanceMode.Canonical);
            var rootEntries = reader.ReadStartMap();
            if (rootEntries is not { } fixedRootEntries || fixedRootEntries != ExpectedRootMapEntries)
            {
                error = OverlayBridgeRoomPolicyDecodeError.InvalidMapLength;
                return false;
            }

            var previousKey = 0;
            var policyFormatVersion = 0;
            string? roomId = null;
            string? roomInstanceId = null;
            long policyEpoch = 0;
            string? ownerDeviceId = null;
            byte[]? ownerFingerprint = null;
            long issuedTicks = 0;
            long expiresTicks = 0;
            List<OverlayBridgeApprovedDevice>? approvedDevices = null;

            for (var index = 0; index < fixedRootEntries; index++)
            {
                var key = ReadNextKey(reader, ref previousKey);
                switch (key)
                {
                    case OverlayBridgeRoomPolicyCborKeys.PolicyFormatVersion:
                        policyFormatVersion = reader.ReadInt32();
                        break;
                    case OverlayBridgeRoomPolicyCborKeys.RoomId:
                        roomId = ReadBoundedText(reader);
                        break;
                    case OverlayBridgeRoomPolicyCborKeys.RoomInstanceId:
                        roomInstanceId = ReadBoundedText(reader);
                        break;
                    case OverlayBridgeRoomPolicyCborKeys.PolicyEpoch:
                        policyEpoch = reader.ReadInt64();
                        break;
                    case OverlayBridgeRoomPolicyCborKeys.OwnerDeviceId:
                        ownerDeviceId = ReadBoundedText(reader);
                        break;
                    case OverlayBridgeRoomPolicyCborKeys.OwnerSpkiSha256:
                        ownerFingerprint = ReadExactSha256(reader);
                        break;
                    case OverlayBridgeRoomPolicyCborKeys.IssuedAtUtcTicks:
                        issuedTicks = reader.ReadInt64();
                        break;
                    case OverlayBridgeRoomPolicyCborKeys.ExpiresAtUtcTicks:
                        expiresTicks = reader.ReadInt64();
                        break;
                    case OverlayBridgeRoomPolicyCborKeys.ApprovedDevices:
                        approvedDevices = ReadApprovedDevices(reader);
                        break;
                    default:
                        error = OverlayBridgeRoomPolicyDecodeError.UnknownRequiredField;
                        return false;
                }
            }

            reader.ReadEndMap();
            if (reader.BytesRemaining != 0
                || policyFormatVersion != CurrentPolicyFormatVersion
                || roomId is null
                || roomInstanceId is null
                || ownerDeviceId is null
                || ownerFingerprint is null
                || approvedDevices is null)
            {
                error = policyFormatVersion == 0
                    ? OverlayBridgeRoomPolicyDecodeError.MissingRequiredField
                    : OverlayBridgeRoomPolicyDecodeError.UnsupportedFormatVersion;
                return false;
            }

            policy = new OverlayBridgeRoomPolicy(
                roomId,
                roomInstanceId,
                policyEpoch,
                new OverlayBridgeDevicePolicyBinding(ownerDeviceId, ownerFingerprint),
                new DateTimeOffset(issuedTicks, TimeSpan.Zero),
                new DateTimeOffset(expiresTicks, TimeSpan.Zero),
                approvedDevices);

            if (!payload.Span.SequenceEqual(Encode(policy)))
            {
                policy = null;
                error = OverlayBridgeRoomPolicyDecodeError.NonCanonicalPayload;
                return false;
            }

            error = OverlayBridgeRoomPolicyDecodeError.None;
            return true;
        }
        catch (CborContentException)
        {
            error = OverlayBridgeRoomPolicyDecodeError.InvalidCbor;
            return false;
        }
        catch (ArgumentException)
        {
            error = OverlayBridgeRoomPolicyDecodeError.InvalidPolicy;
            return false;
        }
        catch (OverflowException)
        {
            error = OverlayBridgeRoomPolicyDecodeError.InvalidPolicy;
            return false;
        }
    }

    private static int ReadNextKey(CborReader reader, ref int previousKey)
    {
        var key = reader.ReadInt32();
        if (key <= previousKey)
        {
            throw new CborContentException("Room-policy map keys must be strictly increasing.");
        }

        previousKey = key;
        return key;
    }

    private static string ReadBoundedText(CborReader reader)
    {
        var value = reader.ReadTextString();
        if (value.Length > OverlayBridgeFactContracts.MaxOpaqueIdentifierLength)
        {
            throw new CborContentException("Room-policy text exceeds the bounded identifier limit.");
        }

        return value;
    }

    private static byte[] ReadExactSha256(CborReader reader)
    {
        var value = reader.ReadByteString();
        if (value.Length != 32)
        {
            throw new CborContentException("Room-policy SPKI bindings must be SHA-256 values.");
        }

        return value;
    }

    private static List<OverlayBridgeApprovedDevice> ReadApprovedDevices(CborReader reader)
    {
        var count = reader.ReadStartArray();
        if (count is not { } fixedCount || fixedCount < 0 || fixedCount > MaximumApprovedDeviceEntries)
        {
            throw new CborContentException("Room-policy device list is not bounded.");
        }

        var devices = new List<OverlayBridgeApprovedDevice>(fixedCount);
        for (var index = 0; index < fixedCount; index++)
        {
            var fields = reader.ReadStartMap();
            if (fields is not { } fixedFields || fixedFields != ExpectedApprovedDeviceMapEntries)
            {
                throw new CborContentException("Room-policy approved-device map has an invalid size.");
            }

            var previousKey = 0;
            string? deviceId = null;
            byte[]? fingerprint = null;
            OverlayBridgeMemberRole? role = null;
            OverlayBridgeCapability? capabilities = null;

            for (var field = 0; field < fixedFields; field++)
            {
                var key = ReadNextKey(reader, ref previousKey);
                switch (key)
                {
                    case OverlayBridgeRoomPolicyCborKeys.ApprovedDevice.DeviceId:
                        deviceId = ReadBoundedText(reader);
                        break;
                    case OverlayBridgeRoomPolicyCborKeys.ApprovedDevice.SpkiSha256:
                        fingerprint = ReadExactSha256(reader);
                        break;
                    case OverlayBridgeRoomPolicyCborKeys.ApprovedDevice.RoleAndCapabilities:
                        var roleCapabilitiesCount = reader.ReadStartArray();
                        if (roleCapabilitiesCount != 2)
                        {
                            throw new CborContentException("Room-policy role/capability tuple is invalid.");
                        }

                        role = (OverlayBridgeMemberRole)reader.ReadInt32();
                        capabilities = (OverlayBridgeCapability)reader.ReadInt32();
                        reader.ReadEndArray();
                        break;
                    default:
                        throw new CborContentException("Unknown required approved-device field.");
                }
            }

            reader.ReadEndMap();
            if (deviceId is null || fingerprint is null || role is null || capabilities is null)
            {
                throw new CborContentException("Room-policy approved device is missing a required field.");
            }

            devices.Add(new OverlayBridgeApprovedDevice(
                new OverlayBridgeDevicePolicyBinding(deviceId, fingerprint),
                role.Value,
                capabilities.Value));
        }

        reader.ReadEndArray();
        return devices;
    }
}

internal enum OverlayBridgeRoomPolicyDecodeError
{
    None = 0,
    EmptyPayload = 1,
    OversizedPayload = 2,
    InvalidMapLength = 3,
    MissingRequiredField = 4,
    UnknownRequiredField = 5,
    NonCanonicalPayload = 6,
    InvalidCbor = 7,
    InvalidPolicy = 8,
    UnsupportedFormatVersion = 9
}

internal static class OverlayBridgeRoomPolicyCborKeys
{
    public const int PolicyFormatVersion = 1;
    public const int RoomId = 2;
    public const int RoomInstanceId = 3;
    public const int PolicyEpoch = 4;
    public const int OwnerDeviceId = 5;
    public const int OwnerSpkiSha256 = 6;
    public const int IssuedAtUtcTicks = 7;
    public const int ExpiresAtUtcTicks = 8;
    public const int ApprovedDevices = 9;

    internal static class ApprovedDevice
    {
        public const int DeviceId = 1;
        public const int SpkiSha256 = 2;
        public const int RoleAndCapabilities = 3;
    }
}
