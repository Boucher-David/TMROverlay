using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace TmrOverlay.Core.OverlayBridge;

/// <summary>
/// Exact policy-backed validator for one self-signed Bridge TLS peer. System trust and certificate
/// names are deliberately not authorization inputs: a room uses an Owner-signed allowlist of
/// exact P-256 SPKIs and TLS proves possession of that key. Chain/name errors are therefore
/// expected for the short-lived self-signed leaves and are not accepted on their own.
/// </summary>
internal sealed class OverlayBridgeTlsPeerValidator
{
    private const string ServerAuthenticationOid = "1.3.6.1.5.5.7.3.1";
    private const string ClientAuthenticationOid = "1.3.6.1.5.5.7.3.2";

    private readonly OverlayBridgeSignedRoomPolicy _signedPolicy;
    private readonly OverlayBridgeDeviceIdentity _ownerIdentity;
    private readonly OverlayBridgeDeviceIdentity _expectedPeerIdentity;
    private readonly OverlayBridgeChannelEndpointRole _localEndpointRole;
    private readonly Func<DateTimeOffset> _now;

    public OverlayBridgeTlsPeerValidator(
        OverlayBridgeSignedRoomPolicy signedPolicy,
        OverlayBridgeDeviceIdentity ownerIdentity,
        OverlayBridgeDeviceIdentity expectedPeerIdentity,
        OverlayBridgeChannelEndpointRole localEndpointRole,
        Func<DateTimeOffset>? now = null)
    {
        _signedPolicy = signedPolicy ?? throw new ArgumentNullException(nameof(signedPolicy));
        _ownerIdentity = ownerIdentity ?? throw new ArgumentNullException(nameof(ownerIdentity));
        _expectedPeerIdentity = expectedPeerIdentity ?? throw new ArgumentNullException(nameof(expectedPeerIdentity));
        _localEndpointRole = localEndpointRole;
        _now = now ?? (() => DateTimeOffset.UtcNow);

        if (!OverlayBridgeChannelHelloContracts.TryGetOppositeRole(localEndpointRole, out _))
        {
            throw new ArgumentOutOfRangeException(nameof(localEndpointRole));
        }
    }

    /// <summary>Adapter suitable for <see cref="SslStream"/> remote-certificate callbacks.</summary>
    public bool Validate(X509Certificate? certificate, X509Chain? chain, SslPolicyErrors sslPolicyErrors)
    {
        return TryValidate(certificate, chain, sslPolicyErrors).IsAccepted;
    }

    public OverlayBridgeTlsPeerValidationResult TryValidate(
        X509Certificate? certificate,
        X509Chain? chain,
        SslPolicyErrors sslPolicyErrors)
    {
        // `chain` and SslPolicyErrors are deliberately not a bypass. The required policy/SPKI,
        // leaf, validity, and EKU checks below establish the room's trust decision instead.
        _ = chain;
        _ = sslPolicyErrors;

        var now = _now().ToUniversalTime();
        if (!OverlayBridgeRoomPolicySigner.TryVerify(
                _signedPolicy,
                _ownerIdentity,
                now,
                out var policyError))
        {
            return OverlayBridgeTlsPeerValidationResult.Rejected(
                policyError == OverlayBridgeRoomPolicyVerificationError.PolicyExpired
                    ? OverlayBridgeTlsPeerValidationError.PolicyExpired
                    : OverlayBridgeTlsPeerValidationError.InvalidSignedPolicy);
        }

        if (!TryAuthorizeExpectedPeer(out var role))
        {
            return OverlayBridgeTlsPeerValidationResult.Rejected(
                OverlayBridgeTlsPeerValidationError.PeerNotAuthorizedByPolicy);
        }

        if (!IsRoleAllowedOnExpectedPeerEndpoint(role))
        {
            return OverlayBridgeTlsPeerValidationResult.Rejected(
                OverlayBridgeTlsPeerValidationError.PeerRoleNotAllowedOnEndpoint);
        }

        if (certificate is null)
        {
            return OverlayBridgeTlsPeerValidationResult.Rejected(
                OverlayBridgeTlsPeerValidationError.MissingCertificate);
        }

        if (certificate is X509Certificate2 certificate2)
        {
            return ValidateLeaf(certificate2, now);
        }

        using var copiedLeaf = new X509Certificate2(certificate);
        return ValidateLeaf(copiedLeaf, now);
    }

    private OverlayBridgeTlsPeerValidationResult ValidateLeaf(X509Certificate2 leaf, DateTimeOffset now)
    {
        if (!IsValidSelfSignedLeaf(leaf, now))
        {
            return OverlayBridgeTlsPeerValidationResult.Rejected(
                OverlayBridgeTlsPeerValidationError.InvalidLeafCertificate);
        }

        if (!HasExpectedEnhancedKeyUsage(leaf))
        {
            return OverlayBridgeTlsPeerValidationResult.Rejected(
                OverlayBridgeTlsPeerValidationError.InvalidEnhancedKeyUsage);
        }

        if (!_expectedPeerIdentity.MatchesPresentedCertificate(_expectedPeerIdentity.DeviceId, leaf))
        {
            return OverlayBridgeTlsPeerValidationResult.Rejected(
                OverlayBridgeTlsPeerValidationError.PeerSpkiMismatch);
        }

        return OverlayBridgeTlsPeerValidationResult.Accepted();
    }

    private bool TryAuthorizeExpectedPeer(out OverlayBridgeMemberRole role)
    {
        var policy = _signedPolicy.Policy;
        if (policy.OwnerBinding.Matches(_expectedPeerIdentity))
        {
            role = OverlayBridgeMemberRole.Owner;
            return true;
        }

        if (policy.TryFindApprovedDevice(_expectedPeerIdentity, out var approved))
        {
            role = approved!.Role;
            return true;
        }

        role = default;
        return false;
    }

    private bool IsRoleAllowedOnExpectedPeerEndpoint(OverlayBridgeMemberRole role)
    {
        // A Publisher must be the Owner or an explicitly selected Team member. View-only devices
        // may receive a publisher's facts, but can never authenticate as the publisher endpoint.
        return _localEndpointRole == OverlayBridgeChannelEndpointRole.Viewer
            ? role is OverlayBridgeMemberRole.Owner or OverlayBridgeMemberRole.TeamMember
            : role is OverlayBridgeMemberRole.Owner
                or OverlayBridgeMemberRole.TeamMember
                or OverlayBridgeMemberRole.ViewOnly;
    }

    private bool HasExpectedEnhancedKeyUsage(X509Certificate2 certificate)
    {
        var expectedOid = _localEndpointRole == OverlayBridgeChannelEndpointRole.Publisher
            ? ClientAuthenticationOid
            : ServerAuthenticationOid;
        var usages = certificate.Extensions
            .OfType<X509EnhancedKeyUsageExtension>()
            .SelectMany(extension => extension.EnhancedKeyUsages.Cast<Oid>());
        return usages.Any(usage => string.Equals(usage.Value, expectedOid, StringComparison.Ordinal));
    }

    private static bool IsValidSelfSignedLeaf(X509Certificate2 certificate, DateTimeOffset now)
    {
        var basicConstraints = certificate.Extensions.OfType<X509BasicConstraintsExtension>().ToArray();
        var keyUsages = certificate.Extensions.OfType<X509KeyUsageExtension>().ToArray();
        if (certificate.NotBefore.ToUniversalTime() > now.UtcDateTime
            || certificate.NotAfter.ToUniversalTime() <= now.UtcDateTime
            || !certificate.SubjectName.RawData.AsSpan().SequenceEqual(certificate.IssuerName.RawData)
            || basicConstraints.Length != 1
            || basicConstraints[0].CertificateAuthority
            || keyUsages.Length != 1)
        {
            return false;
        }

        using var publicKey = certificate.GetECDsaPublicKey();
        return (keyUsages[0].KeyUsages & X509KeyUsageFlags.DigitalSignature) != 0
            && publicKey is not null;
    }
}

internal sealed record OverlayBridgeTlsPeerValidationResult(
    bool IsAccepted,
    OverlayBridgeTlsPeerValidationError Error)
{
    public static OverlayBridgeTlsPeerValidationResult Accepted() => new(
        IsAccepted: true,
        OverlayBridgeTlsPeerValidationError.None);

    public static OverlayBridgeTlsPeerValidationResult Rejected(OverlayBridgeTlsPeerValidationError error) => new(
        IsAccepted: false,
        error);
}

internal enum OverlayBridgeTlsPeerValidationError
{
    None = 0,
    InvalidSignedPolicy = 1,
    PolicyExpired = 2,
    PeerNotAuthorizedByPolicy = 3,
    PeerRoleNotAllowedOnEndpoint = 4,
    MissingCertificate = 5,
    InvalidLeafCertificate = 6,
    InvalidEnhancedKeyUsage = 7,
    PeerSpkiMismatch = 8
}

/// <summary>
/// Centralizes the non-negotiable inner-TLS posture for a future relay circuit. The outer relay
/// remains payload blind; these options are only for the end-to-end circuit inside it.
/// </summary>
internal static class OverlayBridgeTlsOptions
{
    public const string AlpnIdentifier = "tmr-overlay-bridge/1";
    private static readonly SslApplicationProtocol AlpnProtocol = new(
        Encoding.ASCII.GetBytes(AlpnIdentifier));

    public static SslServerAuthenticationOptions CreatePublisherServerOptions(
        X509Certificate2 publisherCertificate,
        OverlayBridgeTlsPeerValidator viewerValidator)
    {
        ValidateLocalCertificate(publisherCertificate, nameof(publisherCertificate));
        ArgumentNullException.ThrowIfNull(viewerValidator);

        return new SslServerAuthenticationOptions
        {
            ServerCertificate = publisherCertificate,
            ClientCertificateRequired = true,
            EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
            CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
            AllowRenegotiation = false,
            ApplicationProtocols = [AlpnProtocol],
            RemoteCertificateValidationCallback = (_, certificate, chain, errors) =>
                viewerValidator.Validate(certificate, chain, errors)
        };
    }

    public static SslClientAuthenticationOptions CreateViewerClientOptions(
        X509Certificate2 viewerCertificate,
        OverlayBridgeTlsPeerValidator publisherValidator)
    {
        ValidateLocalCertificate(viewerCertificate, nameof(viewerCertificate));
        ArgumentNullException.ThrowIfNull(publisherValidator);

        return new SslClientAuthenticationOptions
        {
            // This is not DNS trust: the exact certificate key is verified by the callback.
            TargetHost = "tmr-overlay-bridge",
            ClientCertificates = new X509CertificateCollection(viewerCertificate),
            EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
            CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
            AllowRenegotiation = false,
            ApplicationProtocols = [AlpnProtocol],
            RemoteCertificateValidationCallback = (_, certificate, chain, errors) =>
                publisherValidator.Validate(certificate, chain, errors)
        };
    }

    private static void ValidateLocalCertificate(X509Certificate2? certificate, string parameterName)
    {
        using var privateKey = certificate?.GetECDsaPrivateKey();
        if (certificate is null
            || !certificate.HasPrivateKey
            || privateKey is null)
        {
            throw new ArgumentException(
                "An Overlay Bridge endpoint requires a private P-256 certificate from platform-kept identity storage.",
                parameterName);
        }
    }
}
