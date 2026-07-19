using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using TmrOverlay.Core.OverlayBridge;

namespace TmrOverlay.App.Tests.OverlayBridge;

public sealed class OverlayBridgeTlsPeerValidationTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Validator_AcceptsOnlyExactAuthorizedP256LeafWithRequiredViewerUsage()
    {
        using var owner = CreateDevice("owner", ServerAuthenticationOid);
        using var viewer = CreateDevice("viewer", ClientAuthenticationOid);
        var signed = CreatePolicy(owner, viewer.Identity, OverlayBridgeMemberRole.ViewOnly);
        var validator = new OverlayBridgeTlsPeerValidator(
            signed,
            owner.Identity,
            viewer.Identity,
            OverlayBridgeChannelEndpointRole.Publisher,
            () => Now);

        var accepted = validator.TryValidate(viewer.Certificate, chain: null, SslPolicyErrors.RemoteCertificateChainErrors);

        Assert.True(accepted.IsAccepted);
        Assert.Equal(OverlayBridgeTlsPeerValidationError.None, accepted.Error);
    }

    [Fact]
    public void Validator_RejectsWrongUsageSpkiRoleAndExpiredPolicy()
    {
        using var owner = CreateDevice("owner", ServerAuthenticationOid);
        using var viewer = CreateDevice("viewer", ClientAuthenticationOid);
        using var wrongUsage = CreateDevice("viewer", ServerAuthenticationOid);
        var signed = CreatePolicy(owner, viewer.Identity, OverlayBridgeMemberRole.ViewOnly);

        var wrongUsageValidator = new OverlayBridgeTlsPeerValidator(
            signed,
            owner.Identity,
            viewer.Identity,
            OverlayBridgeChannelEndpointRole.Publisher,
            () => Now);
        var wrongUsageResult = wrongUsageValidator.TryValidate(wrongUsage.Certificate, null, SslPolicyErrors.None);
        Assert.False(wrongUsageResult.IsAccepted);
        Assert.Contains(wrongUsageResult.Error, new[]
        {
            OverlayBridgeTlsPeerValidationError.InvalidEnhancedKeyUsage,
            OverlayBridgeTlsPeerValidationError.PeerSpkiMismatch
        });

        var viewerCannotPublish = new OverlayBridgeTlsPeerValidator(
            signed,
            owner.Identity,
            viewer.Identity,
            OverlayBridgeChannelEndpointRole.Viewer,
            () => Now);
        var roleResult = viewerCannotPublish.TryValidate(viewer.Certificate, null, SslPolicyErrors.None);
        Assert.False(roleResult.IsAccepted);
        Assert.Equal(OverlayBridgeTlsPeerValidationError.PeerRoleNotAllowedOnEndpoint, roleResult.Error);

        var expired = CreatePolicy(owner, viewer.Identity, OverlayBridgeMemberRole.ViewOnly, expiresAtUtc: Now.AddMinutes(-1));
        var expiredValidator = new OverlayBridgeTlsPeerValidator(
            expired,
            owner.Identity,
            viewer.Identity,
            OverlayBridgeChannelEndpointRole.Publisher,
            () => Now);
        var expiredResult = expiredValidator.TryValidate(viewer.Certificate, null, SslPolicyErrors.None);
        Assert.False(expiredResult.IsAccepted);
        Assert.Equal(OverlayBridgeTlsPeerValidationError.PolicyExpired, expiredResult.Error);
    }

    [Fact]
    public void Options_RequireModernMutualTlsAlpnAndNoRenegotiation()
    {
        using var owner = CreateDevice("owner", ServerAuthenticationOid);
        using var viewer = CreateDevice("viewer", ClientAuthenticationOid);
        var signed = CreatePolicy(owner, viewer.Identity, OverlayBridgeMemberRole.TeamMember);
        var viewerValidator = new OverlayBridgeTlsPeerValidator(
            signed, owner.Identity, viewer.Identity, OverlayBridgeChannelEndpointRole.Publisher, () => Now);
        var publisherValidator = new OverlayBridgeTlsPeerValidator(
            signed, owner.Identity, owner.Identity, OverlayBridgeChannelEndpointRole.Viewer, () => Now);

        var server = OverlayBridgeTlsOptions.CreatePublisherServerOptions(owner.Certificate, viewerValidator);
        var client = OverlayBridgeTlsOptions.CreateViewerClientOptions(viewer.Certificate, publisherValidator);

        Assert.True(server.ClientCertificateRequired);
        Assert.False(server.AllowRenegotiation);
        Assert.False(client.AllowRenegotiation);
        Assert.Equal(SslProtocols.Tls12 | SslProtocols.Tls13, server.EnabledSslProtocols);
        Assert.Equal(SslProtocols.Tls12 | SslProtocols.Tls13, client.EnabledSslProtocols);
        Assert.Equal(OverlayBridgeTlsOptions.AlpnIdentifier, Encoding.ASCII.GetString(server.ApplicationProtocols.Single().Protocol.Span));
        Assert.Equal(OverlayBridgeTlsOptions.AlpnIdentifier, Encoding.ASCII.GetString(client.ApplicationProtocols.Single().Protocol.Span));
    }

    private static OverlayBridgeSignedRoomPolicy CreatePolicy(
        Device owner,
        OverlayBridgeDeviceIdentity viewerIdentity,
        OverlayBridgeMemberRole viewerRole,
        DateTimeOffset? expiresAtUtc = null)
    {
        var policy = new OverlayBridgeRoomPolicy(
            "room-tls",
            "instance-tls",
            policyEpoch: 1,
            OverlayBridgeDevicePolicyBinding.FromIdentity(owner.Identity),
            Now.AddMinutes(-1),
            expiresAtUtc ?? Now.AddHours(1),
            [new OverlayBridgeApprovedDevice(
                OverlayBridgeDevicePolicyBinding.FromIdentity(viewerIdentity),
                viewerRole,
                OverlayBridgeFactContracts.FirstRemoteReleaseCapabilities)]);
        return new OverlayBridgeRoomPolicySigner(owner.Identity, owner.Key).Sign(policy);
    }

    private static Device CreateDevice(string deviceId, string enhancedKeyUsageOid)
    {
        var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var identity = OverlayBridgeDeviceIdentity.Create(deviceId, key.ExportSubjectPublicKeyInfo());
        var request = new CertificateRequest($"CN=tmr-bridge-{deviceId}", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new Oid(enhancedKeyUsageOid) }, critical: true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        var certificate = request.CreateSelfSigned(Now.AddHours(-1), Now.AddHours(1));
        return new Device(key, identity, certificate);
    }

    private sealed class Device(ECDsa key, OverlayBridgeDeviceIdentity identity, X509Certificate2 certificate) : IDisposable
    {
        public ECDsa Key { get; } = key;

        public OverlayBridgeDeviceIdentity Identity { get; } = identity;

        public X509Certificate2 Certificate { get; } = certificate;

        public void Dispose()
        {
            Certificate.Dispose();
            Key.Dispose();
        }
    }

    private const string ServerAuthenticationOid = "1.3.6.1.5.5.7.3.1";
    private const string ClientAuthenticationOid = "1.3.6.1.5.5.7.3.2";
}
