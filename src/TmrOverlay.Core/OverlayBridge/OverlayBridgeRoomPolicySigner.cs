using System.Security.Cryptography;

namespace TmrOverlay.Core.OverlayBridge;

/// <summary>
/// Signs and verifies canonical owner policies with the standard P-256 ECDSA implementation.
/// The private key is injected and retained only for the lifetime of this service; persistence is
/// explicitly a platform-key-storage concern outside Core.
/// </summary>
internal sealed class OverlayBridgeRoomPolicySigner
{
    private readonly ECDsa _ownerPrivateKey;

    public OverlayBridgeRoomPolicySigner(
        OverlayBridgeDeviceIdentity ownerIdentity,
        ECDsa ownerPrivateKey)
    {
        ArgumentNullException.ThrowIfNull(ownerIdentity);
        ArgumentNullException.ThrowIfNull(ownerPrivateKey);

        var signerSpki = ownerPrivateKey.ExportSubjectPublicKeyInfo();
        if (!ownerIdentity.MatchesPresentedSubjectPublicKeyInfo(ownerIdentity.DeviceId, signerSpki))
        {
            throw new ArgumentException(
                "The Owner signing key must exactly match the Owner device identity SPKI.",
                nameof(ownerPrivateKey));
        }

        OwnerIdentity = ownerIdentity;
        _ownerPrivateKey = ownerPrivateKey;
    }

    public OverlayBridgeDeviceIdentity OwnerIdentity { get; }

    public OverlayBridgeSignedRoomPolicy Sign(OverlayBridgeRoomPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        if (!policy.OwnerBinding.Matches(OwnerIdentity))
        {
            throw new ArgumentException("A room policy may only be signed by its bound Owner identity.", nameof(policy));
        }

        var canonicalPolicy = OverlayBridgeRoomPolicyCborCodec.Encode(policy);
        var signature = _ownerPrivateKey.SignData(
            canonicalPolicy,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return new OverlayBridgeSignedRoomPolicy(policy, signature);
    }

    public static bool TryVerify(
        OverlayBridgeSignedRoomPolicy? signedPolicy,
        OverlayBridgeDeviceIdentity? expectedOwnerIdentity,
        DateTimeOffset now,
        out OverlayBridgeRoomPolicyVerificationError error)
    {
        if (signedPolicy is null || expectedOwnerIdentity is null)
        {
            error = OverlayBridgeRoomPolicyVerificationError.MissingPolicyOrOwner;
            return false;
        }

        if (!signedPolicy.Policy.TryValidate(out _))
        {
            error = OverlayBridgeRoomPolicyVerificationError.InvalidPolicy;
            return false;
        }

        if (!signedPolicy.Policy.OwnerBinding.Matches(expectedOwnerIdentity))
        {
            error = OverlayBridgeRoomPolicyVerificationError.OwnerBindingMismatch;
            return false;
        }

        var verifiedAtUtc = now.ToUniversalTime();
        if (verifiedAtUtc < signedPolicy.Policy.IssuedAtUtc)
        {
            error = OverlayBridgeRoomPolicyVerificationError.PolicyNotYetValid;
            return false;
        }

        if (verifiedAtUtc >= signedPolicy.Policy.ExpiresAtUtc)
        {
            error = OverlayBridgeRoomPolicyVerificationError.PolicyExpired;
            return false;
        }

        if (signedPolicy.Signature.Length != 64)
        {
            error = OverlayBridgeRoomPolicyVerificationError.InvalidSignature;
            return false;
        }

        try
        {
            using var verifier = ECDsa.Create();
            verifier.ImportSubjectPublicKeyInfo(expectedOwnerIdentity.SubjectPublicKeyInfo.Span, out var bytesRead);
            if (bytesRead != expectedOwnerIdentity.SubjectPublicKeyInfo.Length
                || verifier.KeySize != 256)
            {
                error = OverlayBridgeRoomPolicyVerificationError.OwnerBindingMismatch;
                return false;
            }

            var canonicalPolicy = OverlayBridgeRoomPolicyCborCodec.Encode(signedPolicy.Policy);
            if (!verifier.VerifyData(
                    canonicalPolicy,
                    signedPolicy.Signature.Span,
                    HashAlgorithmName.SHA256,
                    DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
            {
                error = OverlayBridgeRoomPolicyVerificationError.InvalidSignature;
                return false;
            }
        }
        catch (CryptographicException)
        {
            error = OverlayBridgeRoomPolicyVerificationError.InvalidSignature;
            return false;
        }

        error = OverlayBridgeRoomPolicyVerificationError.None;
        return true;
    }
}

internal sealed class OverlayBridgeSignedRoomPolicy
{
    private readonly byte[] _signature;

    public OverlayBridgeSignedRoomPolicy(OverlayBridgeRoomPolicy policy, ReadOnlySpan<byte> signature)
    {
        ArgumentNullException.ThrowIfNull(policy);
        if (signature.Length != 64)
        {
            throw new ArgumentException("An Overlay Bridge P-256 policy signature must be 64 bytes.", nameof(signature));
        }

        Policy = policy;
        _signature = signature.ToArray();
    }

    public OverlayBridgeRoomPolicy Policy { get; }

    public ReadOnlyMemory<byte> Signature => _signature;

    /// <summary>A safe policy correlation value, never a room secret or device private material.</summary>
    public string PolicyHash => Convert.ToHexString(
        SHA256.HashData(OverlayBridgeRoomPolicyCborCodec.Encode(Policy)));

    /// <summary>
    /// The 32-byte form bound into <c>BridgeChannelHello</c>. It is derived only from canonical
    /// signed-policy bytes and must be compared as bytes, not by a display hash string.
    /// </summary>
    public ReadOnlyMemory<byte> PolicyHashSha256 =>
        SHA256.HashData(OverlayBridgeRoomPolicyCborCodec.Encode(Policy));
}

internal enum OverlayBridgeRoomPolicyVerificationError
{
    None = 0,
    MissingPolicyOrOwner = 1,
    InvalidPolicy = 2,
    OwnerBindingMismatch = 3,
    PolicyExpired = 4,
    InvalidSignature = 5,
    PolicyNotYetValid = 6
}
