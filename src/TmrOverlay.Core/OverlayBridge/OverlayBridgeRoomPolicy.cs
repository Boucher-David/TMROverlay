using System.Security.Cryptography;

namespace TmrOverlay.Core.OverlayBridge;

/// <summary>
/// Viewer is intentionally the only enrollment role.  Team-member status is a separate explicit
/// Owner decision and is the only role that can later be considered by publisher-lease policy.
/// </summary>
internal enum OverlayBridgeMemberRole
{
    /// <summary>
    /// The implicit room owner bound by the policy signature. This role cannot be assigned to an
    /// invited device; it only represents the exact Owner SPKI recorded outside the member list.
    /// </summary>
    Owner = 0,
    ViewOnly = 1,
    TeamMember = 2
}

internal sealed class OverlayBridgeApprovedDevice
{
    public OverlayBridgeApprovedDevice(
        OverlayBridgeDevicePolicyBinding binding,
        OverlayBridgeMemberRole role,
        OverlayBridgeCapability grantedCapabilities)
    {
        ArgumentNullException.ThrowIfNull(binding);

        if (!OverlayBridgeRoomPolicyValidation.IsAllowedFirstReleaseRole(role))
        {
            throw new ArgumentOutOfRangeException(nameof(role));
        }

        if (!OverlayBridgeRoomPolicyValidation.IsAllowedFirstReleaseCapabilities(grantedCapabilities))
        {
            throw new ArgumentOutOfRangeException(nameof(grantedCapabilities));
        }

        Binding = binding;
        Role = role;
        GrantedCapabilities = grantedCapabilities;
    }

    public OverlayBridgeDevicePolicyBinding Binding { get; }

    public OverlayBridgeMemberRole Role { get; }

    public OverlayBridgeCapability GrantedCapabilities { get; }

    public bool Matches(OverlayBridgeDeviceIdentity? identity) => Binding.Matches(identity);
}

/// <summary>
/// The signed membership authority for one live room instance.  This intentionally stores
/// neither an Owner private key nor an invitation capability.  The signer is an application
/// boundary supplied by platform-kept key storage in a later slice.
/// </summary>
internal sealed class OverlayBridgeRoomPolicy
{
    private readonly OverlayBridgeApprovedDevice[] _approvedDevices;

    public OverlayBridgeRoomPolicy(
        string roomId,
        string roomInstanceId,
        long policyEpoch,
        OverlayBridgeDevicePolicyBinding ownerBinding,
        DateTimeOffset issuedAtUtc,
        DateTimeOffset expiresAtUtc,
        IEnumerable<OverlayBridgeApprovedDevice> approvedDevices)
    {
        ArgumentNullException.ThrowIfNull(ownerBinding);
        ArgumentNullException.ThrowIfNull(approvedDevices);

        RoomId = roomId;
        RoomInstanceId = roomInstanceId;
        PolicyEpoch = policyEpoch;
        OwnerBinding = ownerBinding;
        IssuedAtUtc = issuedAtUtc.ToUniversalTime();
        ExpiresAtUtc = expiresAtUtc.ToUniversalTime();
        _approvedDevices = approvedDevices.ToArray();

        if (!TryValidate(out var error))
        {
            throw new ArgumentException($"Overlay Bridge room policy is invalid: {error}.", nameof(approvedDevices));
        }
    }

    public string RoomId { get; }

    public string RoomInstanceId { get; }

    /// <summary>Strictly increasing for each Owner-signed membership update in this room instance.</summary>
    public long PolicyEpoch { get; }

    public OverlayBridgeDevicePolicyBinding OwnerBinding { get; }

    public DateTimeOffset IssuedAtUtc { get; }

    public DateTimeOffset ExpiresAtUtc { get; }

    public IReadOnlyList<OverlayBridgeApprovedDevice> ApprovedDevices => _approvedDevices;

    public bool TryFindApprovedDevice(OverlayBridgeDeviceIdentity? identity, out OverlayBridgeApprovedDevice? approved)
    {
        approved = null;
        if (identity is null)
        {
            return false;
        }

        foreach (var candidate in _approvedDevices)
        {
            if (candidate.Matches(identity))
            {
                approved = candidate;
                return true;
            }
        }

        return false;
    }

    public bool TryValidate(out OverlayBridgeRoomPolicyValidationError error)
    {
        if (!OverlayBridgeRoomPolicyValidation.IsValidOpaqueIdentifier(RoomId)
            || !OverlayBridgeRoomPolicyValidation.IsValidOpaqueIdentifier(RoomInstanceId))
        {
            error = OverlayBridgeRoomPolicyValidationError.InvalidRoomBinding;
            return false;
        }

        if (PolicyEpoch <= 0)
        {
            error = OverlayBridgeRoomPolicyValidationError.InvalidPolicyEpoch;
            return false;
        }

        if (IssuedAtUtc.Offset != TimeSpan.Zero
            || ExpiresAtUtc.Offset != TimeSpan.Zero
            || ExpiresAtUtc <= IssuedAtUtc
            || ExpiresAtUtc - IssuedAtUtc > OverlayBridgeRoomPolicyValidation.MaximumPolicyLifetime)
        {
            error = OverlayBridgeRoomPolicyValidationError.InvalidPolicyLifetime;
            return false;
        }

        if (_approvedDevices.Length > OverlayBridgeRoomPolicyValidation.MaximumApprovedDevices)
        {
            error = OverlayBridgeRoomPolicyValidationError.TooManyApprovedDevices;
            return false;
        }

        var deviceIds = new HashSet<string>(StringComparer.Ordinal);
        var deviceFingerprints = new HashSet<string>(StringComparer.Ordinal);
        foreach (var approved in _approvedDevices)
        {
            if (approved is null
                || !OverlayBridgeRoomPolicyValidation.IsAllowedInvitedMemberRole(approved.Role)
                || !OverlayBridgeRoomPolicyValidation.IsAllowedFirstReleaseCapabilities(approved.GrantedCapabilities)
                || string.Equals(approved.Binding.DeviceId, OwnerBinding.DeviceId, StringComparison.Ordinal)
                || CryptographicOperations.FixedTimeEquals(
                    approved.Binding.SubjectPublicKeyInfoSha256.Span,
                    OwnerBinding.SubjectPublicKeyInfoSha256.Span))
            {
                error = OverlayBridgeRoomPolicyValidationError.InvalidApprovedDevice;
                return false;
            }

            if (!deviceIds.Add(approved.Binding.DeviceId)
                || !deviceFingerprints.Add(Convert.ToHexString(approved.Binding.SubjectPublicKeyInfoSha256.Span)))
            {
                error = OverlayBridgeRoomPolicyValidationError.DuplicateApprovedDevice;
                return false;
            }
        }

        error = OverlayBridgeRoomPolicyValidationError.None;
        return true;
    }
}

internal enum OverlayBridgeRoomPolicyValidationError
{
    None = 0,
    InvalidRoomBinding = 1,
    InvalidPolicyEpoch = 2,
    InvalidPolicyLifetime = 3,
    TooManyApprovedDevices = 4,
    InvalidApprovedDevice = 5,
    DuplicateApprovedDevice = 6
}

internal static class OverlayBridgeRoomPolicyValidation
{
    public const int MaximumApprovedDevices = 32;
    public static readonly TimeSpan MaximumPolicyLifetime = TimeSpan.FromHours(24);

    public static bool IsValidOpaqueIdentifier(string? value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && value.Length <= OverlayBridgeFactContracts.MaxOpaqueIdentifierLength
            && value.All(character => character is >= 'a' and <= 'z'
                or >= 'A' and <= 'Z'
                or >= '0' and <= '9'
                or '-' or '_');
    }

    public static bool IsAllowedFirstReleaseRole(OverlayBridgeMemberRole role)
    {
        return role is OverlayBridgeMemberRole.Owner
            or OverlayBridgeMemberRole.ViewOnly
            or OverlayBridgeMemberRole.TeamMember;
    }

    public static bool IsAllowedInvitedMemberRole(OverlayBridgeMemberRole role)
    {
        return role is OverlayBridgeMemberRole.ViewOnly or OverlayBridgeMemberRole.TeamMember;
    }

    public static bool IsAllowedFirstReleaseCapabilities(OverlayBridgeCapability capabilities)
    {
        return capabilities == OverlayBridgeFactContracts.FirstRemoteReleaseCapabilities;
    }
}
