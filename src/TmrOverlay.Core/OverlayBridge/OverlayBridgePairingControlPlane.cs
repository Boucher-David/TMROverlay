using System.Security.Cryptography;

namespace TmrOverlay.Core.OverlayBridge;

/// <summary>
/// Small in-memory pairing control-plane state for one owner-created room instance.  It has no
/// listener, relay call, settings persistence, telemetry dependency, or private-key storage.
/// A later app host may persist only public room state while keeping the injected signing key in
/// platform-protected storage.
/// </summary>
internal sealed class OverlayBridgePairingControlPlane
{
    private readonly object _gate = new();
    private readonly OverlayBridgeRoomPolicySigner _policySigner;
    private readonly OverlayBridgeInviteTokenGenerator _inviteTokenGenerator;
    private readonly Dictionary<string, PendingInvite> _invitesByTokenHash = new(StringComparer.Ordinal);
    private readonly Dictionary<Guid, PendingPairing> _pendingPairings = [];
    private readonly Dictionary<string, OverlayBridgeApprovedDevice> _approvedByDeviceId = new(StringComparer.Ordinal);
    private readonly HashSet<string> _revokedDeviceIds = new(StringComparer.Ordinal);
    private long _lastPolicyEpoch;

    public OverlayBridgePairingControlPlane(
        string roomId,
        string roomInstanceId,
        OverlayBridgeRoomPolicySigner policySigner,
        OverlayBridgeInviteTokenGenerator? inviteTokenGenerator = null)
    {
        if (!OverlayBridgeRoomPolicyValidation.IsValidOpaqueIdentifier(roomId))
        {
            throw new ArgumentException("Room identifiers must be compact opaque identifiers.", nameof(roomId));
        }

        if (!OverlayBridgeRoomPolicyValidation.IsValidOpaqueIdentifier(roomInstanceId))
        {
            throw new ArgumentException("Room-instance identifiers must be compact opaque identifiers.", nameof(roomInstanceId));
        }

        ArgumentNullException.ThrowIfNull(policySigner);
        RoomId = roomId;
        RoomInstanceId = roomInstanceId;
        _policySigner = policySigner;
        _inviteTokenGenerator = inviteTokenGenerator ?? OverlayBridgeInviteTokenGenerator.Cryptographic;
    }

    public string RoomId { get; }

    public string RoomInstanceId { get; }

    /// <summary>
    /// Last signed membership policy.  It is null until an Owner explicitly approves a pending
    /// Viewer request; redemption alone never changes authorization.
    /// </summary>
    public OverlayBridgeSignedRoomPolicy? CurrentPolicy { get; private set; }

    /// <summary>
    /// Creates the initial Owner-signed policy before any teammate is enrolled, or renews it
    /// after expiry without altering membership. This lets the Owner establish its own exact
    /// policy/identity binding before it creates circuits or shares an invite; an Owner is never
    /// smuggled into the invited-device list just to make an empty room usable.
    /// </summary>
    public OverlayBridgeSignedRoomPolicy EnsureCurrentPolicy(DateTimeOffset now, TimeSpan policyLifetime)
    {
        lock (_gate)
        {
            now = now.ToUniversalTime();
            if (!OverlayBridgePairingLimits.IsValidPolicyLifetime(policyLifetime))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(policyLifetime),
                    "Room policies must have a positive bounded lifetime.");
            }

            if (CurrentPolicy is not null && now < CurrentPolicy.Policy.ExpiresAtUtc)
            {
                return CurrentPolicy;
            }

            CurrentPolicy = SignCurrentPolicy(now, policyLifetime);
            return CurrentPolicy;
        }
    }

    public OverlayBridgeViewerInvite CreateViewerInvite(DateTimeOffset now, TimeSpan lifetime)
    {
        lock (_gate)
        {
            now = now.ToUniversalTime();
            if (lifetime <= TimeSpan.Zero || lifetime > OverlayBridgePairingLimits.MaximumInviteLifetime)
            {
                throw new ArgumentOutOfRangeException(nameof(lifetime), "Viewer invites must have a short bounded lifetime.");
            }

            var token = _inviteTokenGenerator.CreateToken();
            if (!OverlayBridgePairingLimits.IsValidInviteToken(token))
            {
                throw new InvalidOperationException("The invite token generator returned an invalid capability.");
            }

            var tokenHash = OverlayBridgePairingLimits.HashInviteToken(token);
            if (_invitesByTokenHash.ContainsKey(tokenHash))
            {
                throw new InvalidOperationException("The invite token generator returned a duplicate capability.");
            }

            var invite = new OverlayBridgeViewerInvite(
                Guid.NewGuid(),
                RoomId,
                RoomInstanceId,
                token,
                now.Add(lifetime),
                OverlayBridgeFactContracts.FirstRemoteReleaseCapabilities);
            _invitesByTokenHash.Add(tokenHash, new PendingInvite(invite));
            return invite;
        }
    }

    /// <summary>
    /// Redeems a Viewer-only capability into a pending request.  The token remains present until
    /// an Owner approves it so the same exact device can safely retry; a different device cannot
    /// race or replace the first identity.  No redemption can grant membership by itself.
    /// </summary>
    public OverlayBridgePairingRedemptionResult RedeemViewerInvite(
        string? inviteToken,
        OverlayBridgeDeviceIdentity deviceIdentity,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(deviceIdentity);
        lock (_gate)
        {
            now = now.ToUniversalTime();

            if (!OverlayBridgePairingLimits.IsValidInviteToken(inviteToken))
            {
                return OverlayBridgePairingRedemptionResult.Rejected(OverlayBridgePairingRejectionReason.InvalidInvite);
            }

            var tokenHash = OverlayBridgePairingLimits.HashInviteToken(inviteToken!);
            if (!_invitesByTokenHash.TryGetValue(tokenHash, out var pendingInvite))
            {
                return OverlayBridgePairingRedemptionResult.Rejected(OverlayBridgePairingRejectionReason.UnknownOrConsumedInvite);
            }

            if (now >= pendingInvite.Invite.ExpiresAtUtc)
            {
                _invitesByTokenHash.Remove(tokenHash);
                return OverlayBridgePairingRedemptionResult.Rejected(OverlayBridgePairingRejectionReason.InviteExpired);
            }

            if (_approvedByDeviceId.ContainsKey(deviceIdentity.DeviceId))
            {
                return OverlayBridgePairingRedemptionResult.Rejected(OverlayBridgePairingRejectionReason.DeviceAlreadyApproved);
            }

            if (pendingInvite.Pending is { } existing)
            {
                return existing.DeviceIdentity.MatchesPresentedSubjectPublicKeyInfo(
                    deviceIdentity.DeviceId,
                    deviceIdentity.SubjectPublicKeyInfo.Span)
                    ? OverlayBridgePairingRedemptionResult.Pending(existing.Request)
                    : OverlayBridgePairingRedemptionResult.Rejected(OverlayBridgePairingRejectionReason.InviteBoundToDifferentDevice);
            }

            var request = new OverlayBridgePendingPairingRequest(
                Guid.NewGuid(),
                pendingInvite.Invite.InviteId,
                RoomId,
                RoomInstanceId,
                OverlayBridgeDevicePolicyBinding.FromIdentity(deviceIdentity),
                now,
                pendingInvite.Invite.RequestedCapabilities);
            var pairing = new PendingPairing(request, deviceIdentity, tokenHash);
            pendingInvite.Pending = pairing;
            _pendingPairings.Add(request.RequestId, pairing);
            return OverlayBridgePairingRedemptionResult.Pending(request);
        }
    }

    /// <summary>
    /// The only operation that turns a pending viewer request into an approved policy entry.
    /// Approval consumes the invitation and signs an incremented policy epoch.
    /// </summary>
    public OverlayBridgePairingApprovalResult Approve(
        Guid requestId,
        OverlayBridgeMemberRole role,
        DateTimeOffset now,
        TimeSpan policyLifetime)
    {
        lock (_gate)
        {
            now = now.ToUniversalTime();
            if (!OverlayBridgeRoomPolicyValidation.IsAllowedInvitedMemberRole(role))
            {
                return OverlayBridgePairingApprovalResult.Rejected(OverlayBridgePairingRejectionReason.InvalidRole);
            }

            if (!OverlayBridgePairingLimits.IsValidPolicyLifetime(policyLifetime))
            {
                return OverlayBridgePairingApprovalResult.Rejected(OverlayBridgePairingRejectionReason.InvalidPolicyLifetime);
            }

            if (!_pendingPairings.TryGetValue(requestId, out var pending))
            {
                return OverlayBridgePairingApprovalResult.Rejected(OverlayBridgePairingRejectionReason.UnknownPendingRequest);
            }

            if (!_invitesByTokenHash.TryGetValue(pending.InviteTokenHash, out var invite)
                || now >= invite.Invite.ExpiresAtUtc)
            {
                ConsumePending(pending);
                return OverlayBridgePairingApprovalResult.Rejected(OverlayBridgePairingRejectionReason.InviteExpired);
            }

            if (_approvedByDeviceId.ContainsKey(pending.DeviceIdentity.DeviceId))
            {
                return OverlayBridgePairingApprovalResult.Rejected(OverlayBridgePairingRejectionReason.DeviceAlreadyApproved);
            }

            var approved = new OverlayBridgeApprovedDevice(
                OverlayBridgeDevicePolicyBinding.FromIdentity(pending.DeviceIdentity),
                role,
                pending.Request.RequestedCapabilities);
            _approvedByDeviceId.Add(approved.Binding.DeviceId, approved);
            _revokedDeviceIds.Remove(approved.Binding.DeviceId);
            ConsumePending(pending);

            CurrentPolicy = SignCurrentPolicy(now, policyLifetime);
            return OverlayBridgePairingApprovalResult.Approved(approved, CurrentPolicy);
        }
    }

    /// <summary>
    /// Removes a device from the next Owner-signed policy.  This is an immediate local admission
    /// fuse; a transport host must additionally close that device's circuits before forwarding a
    /// later publication.
    /// </summary>
    public OverlayBridgePairingRevocationResult Revoke(
        string? deviceId,
        DateTimeOffset now,
        TimeSpan policyLifetime)
    {
        lock (_gate)
        {
            now = now.ToUniversalTime();
            if (!OverlayBridgeIdentityValidation.IsValidDeviceId(deviceId))
            {
                return OverlayBridgePairingRevocationResult.Rejected(OverlayBridgePairingRejectionReason.UnknownApprovedDevice);
            }

            if (!OverlayBridgePairingLimits.IsValidPolicyLifetime(policyLifetime))
            {
                return OverlayBridgePairingRevocationResult.Rejected(OverlayBridgePairingRejectionReason.InvalidPolicyLifetime);
            }

            if (!_approvedByDeviceId.Remove(deviceId!))
            {
                return OverlayBridgePairingRevocationResult.Rejected(OverlayBridgePairingRejectionReason.UnknownApprovedDevice);
            }

            _revokedDeviceIds.Add(deviceId!);
            CurrentPolicy = SignCurrentPolicy(now, policyLifetime);
            return OverlayBridgePairingRevocationResult.Revoked(CurrentPolicy);
        }
    }

    /// <summary>
    /// Checks exact device/SPKI authorization under the current policy.  It is deliberately
    /// independent from friendly metadata and carries the same expiration/signature check a
    /// future mTLS peer validator must apply before admitting a channel hello.
    /// </summary>
    public bool TryAuthorizeCurrentDevice(
        OverlayBridgeDeviceIdentity? deviceIdentity,
        DateTimeOffset now,
        out OverlayBridgeApprovedDevice? approved,
        out OverlayBridgeRoomPolicyVerificationError verificationError)
    {
        lock (_gate)
        {
            approved = null;
            verificationError = OverlayBridgeRoomPolicyVerificationError.InvalidPolicy;
            if (deviceIdentity is null || _revokedDeviceIds.Contains(deviceIdentity.DeviceId))
            {
                return false;
            }

            if (!OverlayBridgeRoomPolicySigner.TryVerify(CurrentPolicy, _policySigner.OwnerIdentity, now, out verificationError)
                || CurrentPolicy is null)
            {
                return false;
            }

            if (CurrentPolicy.Policy.OwnerBinding.Matches(deviceIdentity))
            {
                approved = new OverlayBridgeApprovedDevice(
                    CurrentPolicy.Policy.OwnerBinding,
                    OverlayBridgeMemberRole.Owner,
                    OverlayBridgeFactContracts.FirstRemoteReleaseCapabilities);
                verificationError = OverlayBridgeRoomPolicyVerificationError.None;
                return true;
            }

            if (!CurrentPolicy.Policy.TryFindApprovedDevice(deviceIdentity, out approved))
            {
                verificationError = OverlayBridgeRoomPolicyVerificationError.InvalidPolicy;
                return false;
            }

            return true;
        }
    }

    private OverlayBridgeSignedRoomPolicy SignCurrentPolicy(DateTimeOffset now, TimeSpan policyLifetime)
    {
        var policy = new OverlayBridgeRoomPolicy(
            RoomId,
            RoomInstanceId,
            checked(++_lastPolicyEpoch),
            OverlayBridgeDevicePolicyBinding.FromIdentity(_policySigner.OwnerIdentity),
            now,
            now.Add(policyLifetime),
            _approvedByDeviceId.Values.OrderBy(approved => approved.Binding.DeviceId, StringComparer.Ordinal));
        return _policySigner.Sign(policy);
    }

    private void ConsumePending(PendingPairing pending)
    {
        _pendingPairings.Remove(pending.Request.RequestId);
        _invitesByTokenHash.Remove(pending.InviteTokenHash);
    }

    private sealed class PendingInvite(OverlayBridgeViewerInvite invite)
    {
        public OverlayBridgeViewerInvite Invite { get; } = invite;

        public PendingPairing? Pending { get; set; }
    }

    private sealed class PendingPairing(
        OverlayBridgePendingPairingRequest request,
        OverlayBridgeDeviceIdentity deviceIdentity,
        string inviteTokenHash)
    {
        public OverlayBridgePendingPairingRequest Request { get; } = request;

        public OverlayBridgeDeviceIdentity DeviceIdentity { get; } = deviceIdentity;

        public string InviteTokenHash { get; } = inviteTokenHash;
    }
}

/// <summary>
/// Capability returned to the Owner for out-of-band sharing.  Its token deliberately has no
/// implicit membership semantics and must never be written to diagnostics or a durable policy.
/// </summary>
internal sealed record OverlayBridgeViewerInvite(
    Guid InviteId,
    string RoomId,
    string RoomInstanceId,
    string Token,
    DateTimeOffset ExpiresAtUtc,
    OverlayBridgeCapability RequestedCapabilities);

internal sealed record OverlayBridgePendingPairingRequest(
    Guid RequestId,
    Guid InviteId,
    string RoomId,
    string RoomInstanceId,
    OverlayBridgeDevicePolicyBinding DeviceBinding,
    DateTimeOffset RequestedAtUtc,
    OverlayBridgeCapability RequestedCapabilities);

internal sealed record OverlayBridgePairingRedemptionResult(
    bool IsPending,
    OverlayBridgePendingPairingRequest? PendingRequest,
    OverlayBridgePairingRejectionReason RejectionReason)
{
    public static OverlayBridgePairingRedemptionResult Pending(OverlayBridgePendingPairingRequest request) =>
        new(true, request, OverlayBridgePairingRejectionReason.None);

    public static OverlayBridgePairingRedemptionResult Rejected(OverlayBridgePairingRejectionReason reason) =>
        new(false, null, reason);
}

internal sealed record OverlayBridgePairingApprovalResult(
    bool IsApproved,
    OverlayBridgeApprovedDevice? ApprovedDevice,
    OverlayBridgeSignedRoomPolicy? SignedPolicy,
    OverlayBridgePairingRejectionReason RejectionReason)
{
    public static OverlayBridgePairingApprovalResult Approved(
        OverlayBridgeApprovedDevice device,
        OverlayBridgeSignedRoomPolicy policy) =>
        new(true, device, policy, OverlayBridgePairingRejectionReason.None);

    public static OverlayBridgePairingApprovalResult Rejected(OverlayBridgePairingRejectionReason reason) =>
        new(false, null, null, reason);
}

internal sealed record OverlayBridgePairingRevocationResult(
    bool IsRevoked,
    OverlayBridgeSignedRoomPolicy? SignedPolicy,
    OverlayBridgePairingRejectionReason RejectionReason)
{
    public static OverlayBridgePairingRevocationResult Revoked(OverlayBridgeSignedRoomPolicy policy) =>
        new(true, policy, OverlayBridgePairingRejectionReason.None);

    public static OverlayBridgePairingRevocationResult Rejected(OverlayBridgePairingRejectionReason reason) =>
        new(false, null, reason);
}

internal enum OverlayBridgePairingRejectionReason
{
    None = 0,
    InvalidInvite = 1,
    UnknownOrConsumedInvite = 2,
    InviteExpired = 3,
    InviteBoundToDifferentDevice = 4,
    DeviceAlreadyApproved = 5,
    UnknownPendingRequest = 6,
    InvalidRole = 7,
    InvalidPolicyLifetime = 8,
    UnknownApprovedDevice = 9
}

/// <summary>Small test seam; production uses a cryptographically random 256-bit capability.</summary>
internal abstract class OverlayBridgeInviteTokenGenerator
{
    public static OverlayBridgeInviteTokenGenerator Cryptographic { get; } = new CryptographicInviteTokenGenerator();

    public abstract string CreateToken();

    private sealed class CryptographicInviteTokenGenerator : OverlayBridgeInviteTokenGenerator
    {
        public override string CreateToken()
        {
            return Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }
    }
}

internal static class OverlayBridgePairingLimits
{
    public static readonly TimeSpan MaximumInviteLifetime = TimeSpan.FromMinutes(30);

    public static bool IsValidPolicyLifetime(TimeSpan lifetime)
    {
        return lifetime > TimeSpan.Zero && lifetime <= OverlayBridgeRoomPolicyValidation.MaximumPolicyLifetime;
    }

    public static bool IsValidInviteToken(string? token)
    {
        return !string.IsNullOrWhiteSpace(token)
            && token.Length == 43
            && token.All(character => character is >= 'a' and <= 'z'
                or >= 'A' and <= 'Z'
                or >= '0' and <= '9'
                or '-' or '_');
    }

    public static string HashInviteToken(string token)
    {
        return Convert.ToHexString(SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(token)));
    }
}
