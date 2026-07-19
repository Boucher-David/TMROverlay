using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace TmrOverlay.Core.OverlayBridge;

/// <summary>
/// A device's public identity for Overlay Bridge pairing.  The private key is deliberately
/// absent: platform key storage owns it, while this contract retains only the P-256 SPKI and
/// its SHA-256 binding.  A friendly label is not identity and intentionally does not appear
/// here or in an owner policy.
/// </summary>
internal sealed class OverlayBridgeDeviceIdentity
{
    private readonly byte[] _subjectPublicKeyInfo;
    private readonly byte[] _subjectPublicKeyInfoSha256;

    private OverlayBridgeDeviceIdentity(string deviceId, byte[] subjectPublicKeyInfo)
    {
        DeviceId = deviceId;
        _subjectPublicKeyInfo = subjectPublicKeyInfo;
        _subjectPublicKeyInfoSha256 = SHA256.HashData(subjectPublicKeyInfo);
    }

    public string DeviceId { get; }

    /// <summary>
    /// DER-encoded SubjectPublicKeyInfo for a P-256 ECDSA public key.  This data is public and
    /// is needed only while forming/verifying an owner-signed policy or validating a presented
    /// inner-TLS certificate.  It is never a private key, PFX, room secret, or invite token.
    /// </summary>
    public ReadOnlyMemory<byte> SubjectPublicKeyInfo => _subjectPublicKeyInfo;

    /// <summary>SHA-256 of <see cref="SubjectPublicKeyInfo"/> used in signed allowlists.</summary>
    public ReadOnlyMemory<byte> SubjectPublicKeyInfoSha256 => _subjectPublicKeyInfoSha256;

    public static OverlayBridgeDeviceIdentity Create(
        string deviceId,
        ReadOnlySpan<byte> subjectPublicKeyInfo)
    {
        if (!OverlayBridgeIdentityValidation.IsValidDeviceId(deviceId))
        {
            throw new ArgumentException("Overlay Bridge device identifiers must be compact ASCII identifiers.", nameof(deviceId));
        }

        if (!OverlayBridgeIdentityValidation.IsP256SubjectPublicKeyInfo(subjectPublicKeyInfo))
        {
            throw new ArgumentException(
                "Overlay Bridge device identities require one complete ECDSA P-256 SubjectPublicKeyInfo value.",
                nameof(subjectPublicKeyInfo));
        }

        return new OverlayBridgeDeviceIdentity(deviceId, subjectPublicKeyInfo.ToArray());
    }

    /// <summary>
    /// Tests a presented certificate/public-key binding without relying on a display name,
    /// issuer, thumbprint, or a trust-store decision.  The future SslStream validator must call
    /// this only after it has selected an owner-verified current policy.
    /// </summary>
    public bool MatchesPresentedSubjectPublicKeyInfo(
        string? presentedDeviceId,
        ReadOnlySpan<byte> presentedSubjectPublicKeyInfo)
    {
        if (!string.Equals(DeviceId, presentedDeviceId, StringComparison.Ordinal)
            || !OverlayBridgeIdentityValidation.IsP256SubjectPublicKeyInfo(presentedSubjectPublicKeyInfo))
        {
            return false;
        }

        var presentedHash = SHA256.HashData(presentedSubjectPublicKeyInfo);
        return CryptographicOperations.FixedTimeEquals(
            _subjectPublicKeyInfoSha256,
            presentedHash);
    }

    /// <summary>
    /// Convenience adapter for a future SslStream certificate callback.  This checks only the
    /// exact device/SPKI association; certificate validity period, key usage, self-signed-leaf
    /// rules, room policy verification, and endpoint role remain separate required checks.
    /// </summary>
    public bool MatchesPresentedCertificate(string? presentedDeviceId, X509Certificate2? certificate)
    {
        if (certificate is null)
        {
            return false;
        }

        using var publicKey = certificate.GetECDsaPublicKey();
        return publicKey is not null
            && MatchesPresentedSubjectPublicKeyInfo(
                presentedDeviceId,
                publicKey.ExportSubjectPublicKeyInfo());
    }
}

/// <summary>
/// The subset of a device identity that belongs in an owner-signed room policy.  It stores an
/// exact SPKI hash, never a certificate thumbprint or a reusable/shared credential.
/// </summary>
internal sealed class OverlayBridgeDevicePolicyBinding
{
    private readonly byte[] _subjectPublicKeyInfoSha256;

    public OverlayBridgeDevicePolicyBinding(
        string deviceId,
        ReadOnlySpan<byte> subjectPublicKeyInfoSha256)
    {
        if (!OverlayBridgeIdentityValidation.IsValidDeviceId(deviceId))
        {
            throw new ArgumentException("Overlay Bridge device identifiers must be compact ASCII identifiers.", nameof(deviceId));
        }

        if (subjectPublicKeyInfoSha256.Length != SHA256.HashSizeInBytes)
        {
            throw new ArgumentException("A device-policy binding must use a SHA-256 SPKI hash.", nameof(subjectPublicKeyInfoSha256));
        }

        DeviceId = deviceId;
        _subjectPublicKeyInfoSha256 = subjectPublicKeyInfoSha256.ToArray();
    }

    public string DeviceId { get; }

    public ReadOnlyMemory<byte> SubjectPublicKeyInfoSha256 => _subjectPublicKeyInfoSha256;

    public static OverlayBridgeDevicePolicyBinding FromIdentity(OverlayBridgeDeviceIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        return new OverlayBridgeDevicePolicyBinding(identity.DeviceId, identity.SubjectPublicKeyInfoSha256.Span);
    }

    public bool Matches(OverlayBridgeDeviceIdentity? identity)
    {
        return identity is not null
            && string.Equals(DeviceId, identity.DeviceId, StringComparison.Ordinal)
            && CryptographicOperations.FixedTimeEquals(
                _subjectPublicKeyInfoSha256,
                identity.SubjectPublicKeyInfoSha256.Span);
    }
}

internal static class OverlayBridgeIdentityValidation
{
    public const int MaxDeviceIdentifierLength = 128;

    public static bool IsValidDeviceId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaxDeviceIdentifierLength)
        {
            return false;
        }

        foreach (var character in value)
        {
            if (!(character is >= 'a' and <= 'z'
                or >= 'A' and <= 'Z'
                or >= '0' and <= '9'
                or '-' or '_'))
            {
                return false;
            }
        }

        return true;
    }

    public static bool IsP256SubjectPublicKeyInfo(ReadOnlySpan<byte> subjectPublicKeyInfo)
    {
        if (subjectPublicKeyInfo.IsEmpty || subjectPublicKeyInfo.Length > 1024)
        {
            return false;
        }

        try
        {
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(subjectPublicKeyInfo, out var bytesRead);
            return bytesRead == subjectPublicKeyInfo.Length && key.KeySize == 256;
        }
        catch (CryptographicException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
