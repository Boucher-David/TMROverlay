using System.Security.Cryptography;
using TmrOverlay.Core.OverlayBridge;
using Xunit;

namespace TmrOverlay.App.Tests.OverlayBridge;

public sealed class OverlayBridgePairingControlPlaneTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 19, 16, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan InviteLifetime = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan PolicyLifetime = TimeSpan.FromHours(4);

    [Fact]
    public void RedeemThenExplicitApproval_SignsExactDeviceBindingAndConsumesInvite()
    {
        using var owner = CreateParticipant("owner-mac");
        using var viewer = CreateParticipant("viewer-windows");
        var plane = CreatePlane(owner, "A");
        var invite = plane.CreateViewerInvite(Now, InviteLifetime);

        var redemption = plane.RedeemViewerInvite(invite.Token, viewer.Identity, Now.AddMinutes(1));

        Assert.True(redemption.IsPending);
        Assert.NotNull(redemption.PendingRequest);
        Assert.Null(plane.CurrentPolicy);
        Assert.Equal(OverlayBridgeFactContracts.FirstRemoteReleaseCapabilities, redemption.PendingRequest!.RequestedCapabilities);

        var approval = plane.Approve(
            redemption.PendingRequest.RequestId,
            OverlayBridgeMemberRole.TeamMember,
            Now.AddMinutes(2),
            PolicyLifetime);

        Assert.True(approval.IsApproved);
        Assert.NotNull(approval.ApprovedDevice);
        Assert.NotNull(approval.SignedPolicy);
        Assert.Equal(1, approval.SignedPolicy!.Policy.PolicyEpoch);
        Assert.Equal(32, approval.SignedPolicy.PolicyHashSha256.Length);
        Assert.Equal(OverlayBridgeMemberRole.TeamMember, approval.ApprovedDevice!.Role);
        Assert.Equal(OverlayBridgeFactContracts.FirstRemoteReleaseCapabilities, approval.ApprovedDevice.GrantedCapabilities);
        Assert.True(OverlayBridgeRoomPolicySigner.TryVerify(
            approval.SignedPolicy,
            owner.Identity,
            Now.AddMinutes(3),
            out var verificationError));
        Assert.Equal(OverlayBridgeRoomPolicyVerificationError.None, verificationError);
        Assert.True(plane.TryAuthorizeCurrentDevice(viewer.Identity, Now.AddMinutes(3), out var authorized, out verificationError));
        Assert.Same(approval.ApprovedDevice, authorized);
        Assert.Equal(OverlayBridgeRoomPolicyVerificationError.None, verificationError);

        var replay = plane.RedeemViewerInvite(invite.Token, viewer.Identity, Now.AddMinutes(3));
        Assert.False(replay.IsPending);
        Assert.Equal(OverlayBridgePairingRejectionReason.UnknownOrConsumedInvite, replay.RejectionReason);
    }

    [Fact]
    public void Redemption_IsViewerOnlyAndCannotAuthorizeUntilOwnerApproves()
    {
        using var owner = CreateParticipant("owner");
        using var viewer = CreateParticipant("viewer");
        var plane = CreatePlane(owner, "B");
        var invite = plane.CreateViewerInvite(Now, InviteLifetime);

        var pending = plane.RedeemViewerInvite(invite.Token, viewer.Identity, Now);

        Assert.True(pending.IsPending);
        Assert.False(plane.TryAuthorizeCurrentDevice(viewer.Identity, Now, out var approved, out var error));
        Assert.Null(approved);
        Assert.Equal(OverlayBridgeRoomPolicyVerificationError.MissingPolicyOrOwner, error);
        Assert.Null(plane.CurrentPolicy);
    }

    [Fact]
    public void Redeem_SameIdentityIsIdempotentButDifferentIdentityCannotRaceInvite()
    {
        using var owner = CreateParticipant("owner");
        using var firstViewer = CreateParticipant("viewer-one");
        using var secondViewer = CreateParticipant("viewer-two");
        var plane = CreatePlane(owner, "C");
        var invite = plane.CreateViewerInvite(Now, InviteLifetime);

        var first = plane.RedeemViewerInvite(invite.Token, firstViewer.Identity, Now);
        var retry = plane.RedeemViewerInvite(invite.Token, firstViewer.Identity, Now.AddSeconds(5));
        var replacement = plane.RedeemViewerInvite(invite.Token, secondViewer.Identity, Now.AddSeconds(5));

        Assert.True(first.IsPending);
        Assert.True(retry.IsPending);
        Assert.Equal(first.PendingRequest!.RequestId, retry.PendingRequest!.RequestId);
        Assert.False(replacement.IsPending);
        Assert.Equal(OverlayBridgePairingRejectionReason.InviteBoundToDifferentDevice, replacement.RejectionReason);
    }

    [Fact]
    public void Approve_ExpiredInviteDoesNotGrantPolicyMembership()
    {
        using var owner = CreateParticipant("owner");
        using var viewer = CreateParticipant("viewer");
        var plane = CreatePlane(owner, "D");
        var invite = plane.CreateViewerInvite(Now, InviteLifetime);
        var pending = plane.RedeemViewerInvite(invite.Token, viewer.Identity, Now.AddMinutes(1));

        var approval = plane.Approve(
            pending.PendingRequest!.RequestId,
            OverlayBridgeMemberRole.ViewOnly,
            Now.Add(InviteLifetime),
            PolicyLifetime);

        Assert.False(approval.IsApproved);
        Assert.Equal(OverlayBridgePairingRejectionReason.InviteExpired, approval.RejectionReason);
        Assert.Null(plane.CurrentPolicy);
        Assert.False(plane.TryAuthorizeCurrentDevice(viewer.Identity, Now.AddMinutes(11), out _, out _));
    }

    [Fact]
    public void Revoke_RemovesMembershipInNewSignedEpochAndIsImmediateLocalFuse()
    {
        using var owner = CreateParticipant("owner");
        using var viewer = CreateParticipant("viewer");
        var plane = CreatePlane(owner, "E");
        var invite = plane.CreateViewerInvite(Now, InviteLifetime);
        var pending = plane.RedeemViewerInvite(invite.Token, viewer.Identity, Now);
        var approval = plane.Approve(
            pending.PendingRequest!.RequestId,
            OverlayBridgeMemberRole.ViewOnly,
            Now.AddMinutes(1),
            PolicyLifetime);
        var oldPolicy = approval.SignedPolicy!;

        var revocation = plane.Revoke(viewer.Identity.DeviceId, Now.AddMinutes(2), PolicyLifetime);

        Assert.True(revocation.IsRevoked);
        Assert.NotNull(revocation.SignedPolicy);
        Assert.Equal(oldPolicy.Policy.PolicyEpoch + 1, revocation.SignedPolicy!.Policy.PolicyEpoch);
        Assert.Empty(revocation.SignedPolicy.Policy.ApprovedDevices);
        Assert.True(OverlayBridgeRoomPolicySigner.TryVerify(
            revocation.SignedPolicy,
            owner.Identity,
            Now.AddMinutes(3),
            out var verificationError));
        Assert.Equal(OverlayBridgeRoomPolicyVerificationError.None, verificationError);
        Assert.False(plane.TryAuthorizeCurrentDevice(viewer.Identity, Now.AddMinutes(3), out _, out verificationError));
        Assert.Equal(OverlayBridgeRoomPolicyVerificationError.InvalidPolicy, verificationError);
    }

    [Fact]
    public void Authorize_RejectsSameDeviceIdWithDifferentP256CertificateKey()
    {
        using var owner = CreateParticipant("owner");
        using var viewer = CreateParticipant("viewer");
        using var impersonator = CreateParticipant("viewer");
        var plane = CreatePlane(owner, "F");
        var invite = plane.CreateViewerInvite(Now, InviteLifetime);
        var pending = plane.RedeemViewerInvite(invite.Token, viewer.Identity, Now);
        Assert.True(plane.Approve(
            pending.PendingRequest!.RequestId,
            OverlayBridgeMemberRole.TeamMember,
            Now.AddMinutes(1),
            PolicyLifetime).IsApproved);

        Assert.False(plane.TryAuthorizeCurrentDevice(impersonator.Identity, Now.AddMinutes(2), out _, out var error));
        Assert.Equal(OverlayBridgeRoomPolicyVerificationError.InvalidPolicy, error);
        Assert.False(viewer.Identity.MatchesPresentedSubjectPublicKeyInfo(
            impersonator.Identity.DeviceId,
            impersonator.Identity.SubjectPublicKeyInfo.Span));
    }

    [Fact]
    public void Authorize_AllowsTheExactImplicitRoomOwnerButInviteApprovalCannotGrantOwner()
    {
        using var owner = CreateParticipant("owner");
        using var viewer = CreateParticipant("viewer");
        var plane = CreatePlane(owner, "owner");
        var initialPolicy = plane.EnsureCurrentPolicy(Now, PolicyLifetime);
        Assert.Empty(initialPolicy.Policy.ApprovedDevices);
        Assert.True(plane.TryAuthorizeCurrentDevice(owner.Identity, Now.AddMinutes(1), out var ownerBeforeInvite, out var initialError));
        Assert.Equal(OverlayBridgeRoomPolicyVerificationError.None, initialError);
        Assert.Equal(OverlayBridgeMemberRole.Owner, ownerBeforeInvite!.Role);

        var invite = plane.CreateViewerInvite(Now, InviteLifetime);
        var pending = plane.RedeemViewerInvite(invite.Token, viewer.Identity, Now);

        var rejected = plane.Approve(
            pending.PendingRequest!.RequestId,
            OverlayBridgeMemberRole.Owner,
            Now.AddMinutes(1),
            PolicyLifetime);
        Assert.False(rejected.IsApproved);
        Assert.Equal(OverlayBridgePairingRejectionReason.InvalidRole, rejected.RejectionReason);

        var approved = plane.Approve(
            pending.PendingRequest.RequestId,
            OverlayBridgeMemberRole.TeamMember,
            Now.AddMinutes(1),
            PolicyLifetime);
        Assert.True(approved.IsApproved);

        Assert.True(plane.TryAuthorizeCurrentDevice(owner.Identity, Now.AddMinutes(2), out var ownerAuthorization, out var error));
        Assert.Equal(OverlayBridgeRoomPolicyVerificationError.None, error);
        Assert.NotNull(ownerAuthorization);
        Assert.Equal(OverlayBridgeMemberRole.Owner, ownerAuthorization!.Role);
    }

    [Fact]
    public void EnsureCurrentPolicy_ReturnsCurrentEpochUntilExpiryThenRenewalsIncrement()
    {
        using var owner = CreateParticipant("owner");
        var plane = CreatePlane(owner, "renew");

        var initial = plane.EnsureCurrentPolicy(Now, TimeSpan.FromMinutes(2));
        var beforeExpiry = plane.EnsureCurrentPolicy(Now.AddMinutes(1), TimeSpan.FromMinutes(2));
        var afterExpiry = plane.EnsureCurrentPolicy(Now.AddMinutes(3), TimeSpan.FromMinutes(2));

        Assert.Same(initial, beforeExpiry);
        Assert.Equal(initial.Policy.PolicyEpoch + 1, afterExpiry.Policy.PolicyEpoch);
        Assert.True(OverlayBridgeRoomPolicySigner.TryVerify(
            afterExpiry,
            owner.Identity,
            Now.AddMinutes(4),
            out var verificationError));
        Assert.Equal(OverlayBridgeRoomPolicyVerificationError.None, verificationError);
    }

    [Fact]
    public void DeviceIdentity_MatchesOnlyTheExactPresentedEcdsaCertificateKeyAndDeviceId()
    {
        using var deviceKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var identity = OverlayBridgeDeviceIdentity.Create("viewer", deviceKey.ExportSubjectPublicKeyInfo());
        var request = new CertificateRequest(
            "CN=synthetic-bridge-viewer",
            deviceKey,
            HashAlgorithmName.SHA256);
        using var certificate = request.CreateSelfSigned(Now.AddMinutes(-1), Now.AddHours(1));

        Assert.True(identity.MatchesPresentedCertificate("viewer", certificate));
        Assert.False(identity.MatchesPresentedCertificate("other-viewer", certificate));
    }

    [Fact]
    public void SignedPolicy_RoundTripsCanonicalCborAndRejectsTamperedSignatureAndWrongOwner()
    {
        using var owner = CreateParticipant("owner");
        using var viewer = CreateParticipant("viewer");
        using var differentOwner = CreateParticipant("different-owner");
        var plane = CreatePlane(owner, "G");
        var invite = plane.CreateViewerInvite(Now, InviteLifetime);
        var pending = plane.RedeemViewerInvite(invite.Token, viewer.Identity, Now);
        var signed = plane.Approve(
            pending.PendingRequest!.RequestId,
            OverlayBridgeMemberRole.ViewOnly,
            Now.AddMinutes(1),
            PolicyLifetime).SignedPolicy!;
        var encoded = OverlayBridgeRoomPolicyCborCodec.Encode(signed.Policy);

        Assert.True(OverlayBridgeRoomPolicyCborCodec.TryDecode(encoded, out var decoded, out var decodeError));
        Assert.Equal(OverlayBridgeRoomPolicyDecodeError.None, decodeError);
        Assert.NotNull(decoded);
        Assert.Equal(signed.Policy.PolicyEpoch, decoded!.PolicyEpoch);
        Assert.Equal(encoded, OverlayBridgeRoomPolicyCborCodec.Encode(decoded));
        Assert.False(OverlayBridgeRoomPolicySigner.TryVerify(signed, differentOwner.Identity, Now.AddMinutes(2), out var wrongOwner));
        Assert.Equal(OverlayBridgeRoomPolicyVerificationError.OwnerBindingMismatch, wrongOwner);

        var signature = signed.Signature.ToArray();
        signature[0] ^= 0x01;
        var tampered = new OverlayBridgeSignedRoomPolicy(signed.Policy, signature);
        Assert.False(OverlayBridgeRoomPolicySigner.TryVerify(tampered, owner.Identity, Now.AddMinutes(2), out var tamperedError));
        Assert.Equal(OverlayBridgeRoomPolicyVerificationError.InvalidSignature, tamperedError);
    }

    [Fact]
    public void PolicyVerification_RejectsExpiredPolicyWithoutTreatingItAsMembership()
    {
        using var owner = CreateParticipant("owner");
        using var viewer = CreateParticipant("viewer");
        var plane = CreatePlane(owner, "H");
        var invite = plane.CreateViewerInvite(Now, InviteLifetime);
        var pending = plane.RedeemViewerInvite(invite.Token, viewer.Identity, Now);
        var signed = plane.Approve(
            pending.PendingRequest!.RequestId,
            OverlayBridgeMemberRole.ViewOnly,
            Now.AddMinutes(1),
            TimeSpan.FromMinutes(2)).SignedPolicy!;

        Assert.False(OverlayBridgeRoomPolicySigner.TryVerify(
            signed,
            owner.Identity,
            Now.AddMinutes(3),
            out var verificationError));
        Assert.Equal(OverlayBridgeRoomPolicyVerificationError.PolicyExpired, verificationError);
        Assert.False(plane.TryAuthorizeCurrentDevice(viewer.Identity, Now.AddMinutes(3), out _, out verificationError));
        Assert.Equal(OverlayBridgeRoomPolicyVerificationError.PolicyExpired, verificationError);
    }

    [Fact]
    public void PolicyVerification_RejectsAPolicyBeforeItsIssuedAtTime()
    {
        using var owner = CreateParticipant("owner");
        var policy = new OverlayBridgeRoomPolicy(
            "room-future-policy",
            "instance-future-policy",
            policyEpoch: 1,
            ownerBinding: OverlayBridgeDevicePolicyBinding.FromIdentity(owner.Identity),
            issuedAtUtc: Now.AddMinutes(2),
            expiresAtUtc: Now.AddHours(1),
            approvedDevices: []);
        var signed = new OverlayBridgeRoomPolicySigner(owner.Identity, owner.Key).Sign(policy);

        Assert.False(OverlayBridgeRoomPolicySigner.TryVerify(signed, owner.Identity, Now, out var verificationError));
        Assert.Equal(OverlayBridgeRoomPolicyVerificationError.PolicyNotYetValid, verificationError);
    }

    [Fact]
    public void CreateViewerInvite_BoundsOutstandingCapabilitiesAndPurgesExpiredEntries()
    {
        using var owner = CreateParticipant("owner");
        var plane = new OverlayBridgePairingControlPlane(
            "room-invite-limit",
            "instance-invite-limit",
            new OverlayBridgeRoomPolicySigner(owner.Identity, owner.Key),
            new SequentialTokenGenerator());

        for (var index = 0; index < OverlayBridgePairingLimits.MaximumOutstandingViewerInvites; index++)
        {
            _ = plane.CreateViewerInvite(Now, InviteLifetime);
        }

        Assert.Throws<InvalidOperationException>(() => plane.CreateViewerInvite(Now, InviteLifetime));

        // Issuing a later invite sweeps old capability and pending-request state rather than
        // preserving expired links indefinitely.
        var renewed = plane.CreateViewerInvite(Now.Add(InviteLifetime), InviteLifetime);
        Assert.Equal("room-invite-limit", renewed.RoomId);
    }

    [Fact]
    public void Constructors_RejectNonP256IdentityAndNonFirstReleaseCapabilities()
    {
        using var rsa = RSA.Create(2048);
        Assert.Throws<ArgumentException>(() => OverlayBridgeDeviceIdentity.Create("rsa-device", rsa.ExportSubjectPublicKeyInfo()));

        using var owner = CreateParticipant("owner");
        using var viewer = CreateParticipant("viewer");
        Assert.Throws<ArgumentOutOfRangeException>(() => new OverlayBridgeApprovedDevice(
            OverlayBridgeDevicePolicyBinding.FromIdentity(viewer.Identity),
            OverlayBridgeMemberRole.ViewOnly,
            OverlayBridgeCapability.ActiveTeamCar | OverlayBridgeCapability.Environment));

        Assert.Throws<ArgumentException>(() => new OverlayBridgeRoomPolicy(
            "room", "instance", 1, OverlayBridgeDevicePolicyBinding.FromIdentity(owner.Identity),
            Now, Now.AddHours(25), []));
    }

    private static OverlayBridgePairingControlPlane CreatePlane(Participant owner, string tokenSuffix)
    {
        return new OverlayBridgePairingControlPlane(
            "room-alpha",
            $"instance-{tokenSuffix}",
            new OverlayBridgeRoomPolicySigner(owner.Identity, owner.Key),
            new FixedTokenGenerator($"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa{tokenSuffix}"));
    }

    private static Participant CreateParticipant(string deviceId)
    {
        var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return new Participant(
            key,
            OverlayBridgeDeviceIdentity.Create(deviceId, key.ExportSubjectPublicKeyInfo()));
    }

    private sealed class Participant(ECDsa key, OverlayBridgeDeviceIdentity identity) : IDisposable
    {
        public ECDsa Key { get; } = key;

        public OverlayBridgeDeviceIdentity Identity { get; } = identity;

        public void Dispose() => Key.Dispose();
    }

    private sealed class FixedTokenGenerator(string token) : OverlayBridgeInviteTokenGenerator
    {
        public override string CreateToken() => token;
    }

    private sealed class SequentialTokenGenerator : OverlayBridgeInviteTokenGenerator
    {
        private int next;

        public override string CreateToken() => new string('a', 42) + (char)('A' + next++);
    }
}
