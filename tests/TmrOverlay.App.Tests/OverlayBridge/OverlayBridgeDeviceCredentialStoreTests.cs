using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using TmrOverlay.App.OverlayBridge;
using TmrOverlay.Core.OverlayBridge;
using Xunit;

namespace TmrOverlay.App.Tests.OverlayBridge;

public sealed class OverlayBridgeDeviceCredentialStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CredentialStore_DerivesStablePublicIdentityAndUsesTheSameKeyForCertificateAndOwnerPolicySigner()
    {
        using var testKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var store = new OverlayBridgeDeviceCredentialStore(
            new InMemoryKeyStore(testKey),
            new FixedTimeProvider(Now));

        var firstResult = store.TryLoadOrCreate();
        Assert.Equal(OverlayBridgeDeviceCredentialLoadOutcome.Available, firstResult.Outcome);
        using var credential = Assert.IsType<OverlayBridgeDeviceCredential>(firstResult.Credential);

        var secondResult = store.TryLoadOrCreate();
        Assert.Equal(OverlayBridgeDeviceCredentialLoadOutcome.Available, secondResult.Outcome);
        using var secondCredential = Assert.IsType<OverlayBridgeDeviceCredential>(secondResult.Credential);
        Assert.StartsWith("bridge-", credential.Identity.DeviceId, StringComparison.Ordinal);
        Assert.Equal(credential.Identity.DeviceId, secondCredential.Identity.DeviceId);
        Assert.True(credential.Certificate.HasPrivateKey);
        Assert.True(credential.Identity.MatchesPresentedCertificate(
            credential.Identity.DeviceId,
            credential.Certificate));
        Assert.Equal(
            X509KeyUsageFlags.DigitalSignature,
            credential.Certificate.Extensions
                .OfType<X509KeyUsageExtension>()
                .Single()
                .KeyUsages);

        var policy = new OverlayBridgeRoomPolicy(
            "room-a",
            "instance-a",
            1,
            OverlayBridgeDevicePolicyBinding.FromIdentity(credential.Identity),
            Now,
            Now.AddHours(1),
            []);
        var signed = credential.OwnerPolicySigner.Sign(policy);

        Assert.True(OverlayBridgeRoomPolicySigner.TryVerify(
            signed,
            credential.Identity,
            Now.AddMinutes(1),
            out var verificationError));
        Assert.Equal(OverlayBridgeRoomPolicyVerificationError.None, verificationError);
    }

    [Fact]
    public void CredentialStore_UsesOnlyPlatformBoundaryAndDoesNotFallBackToPlaintextOrInMemoryKeyStorage()
    {
        var store = new OverlayBridgeDeviceCredentialStore(
            new UnavailableKeyStore(OverlayBridgeDeviceKeyStoreOutcome.UnsupportedPlatform),
            new FixedTimeProvider(Now));

        var result = store.TryLoadOrCreate();

        Assert.Equal(OverlayBridgeDeviceCredentialLoadOutcome.UnsupportedPlatform, result.Outcome);
        Assert.Null(result.Credential);
    }

    [Fact]
    public void CredentialStore_RejectsAPlatformKeyThatIsNotP256()
    {
        using var p384 = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        var store = new OverlayBridgeDeviceCredentialStore(
            new InMemoryKeyStore(p384),
            new FixedTimeProvider(Now));

        var result = store.TryLoadOrCreate();

        Assert.Equal(OverlayBridgeDeviceCredentialLoadOutcome.Unavailable, result.Outcome);
        Assert.Null(result.Credential);
    }

    private sealed class InMemoryKeyStore : IOverlayBridgeDeviceKeyStore
    {
        private readonly byte[] _pkcs8;

        public InMemoryKeyStore(ECDsa key)
        {
            _pkcs8 = key.ExportPkcs8PrivateKey();
        }

        public OverlayBridgeDeviceKeyStoreResult TryOpenOrCreate(string keyName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(keyName);

            // This fake exists only in deterministic tests. The real key store never materializes
            // PKCS#8 and has no persistence path outside the Windows KSP.
            var ecdsa = ECDsa.Create();
            ecdsa.ImportPkcs8PrivateKey(_pkcs8, out _);
            return OverlayBridgeDeviceKeyStoreResult.Available(ecdsa);
        }
    }

    private sealed class UnavailableKeyStore(OverlayBridgeDeviceKeyStoreOutcome outcome) : IOverlayBridgeDeviceKeyStore
    {
        public OverlayBridgeDeviceKeyStoreResult TryOpenOrCreate(string keyName) => outcome switch
        {
            OverlayBridgeDeviceKeyStoreOutcome.UnsupportedPlatform => OverlayBridgeDeviceKeyStoreResult.UnsupportedPlatform,
            _ => OverlayBridgeDeviceKeyStoreResult.Unavailable
        };
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
