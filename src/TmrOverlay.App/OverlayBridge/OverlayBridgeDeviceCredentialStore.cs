using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using TmrOverlay.Core.OverlayBridge;

namespace TmrOverlay.App.OverlayBridge;

/// <summary>
/// App-facing owner of a local Overlay Bridge device credential.  It deliberately has no room,
/// relay, listener, invite, telemetry, or settings responsibility.  Its only job is to obtain a
/// platform-kept P-256 key, derive the public device identity, make a short-lived self-signed
/// mTLS leaf certificate, and expose a signer that continues to use that non-exportable key.
/// </summary>
internal interface IOverlayBridgeDeviceCredentialStore
{
    OverlayBridgeDeviceCredentialLoadResult TryLoadOrCreate();
}

/// <summary>
/// Builds one application/user-scoped device identity.  No private material, PFX, PEM, room
/// secret, invite, or certificate file is written by this service.  Windows supplies the key
/// implementation; an unsupported platform fails closed without falling back to file storage.
/// </summary>
internal sealed class OverlayBridgeDeviceCredentialStore : IOverlayBridgeDeviceCredentialStore
{
    // This is a stable KSP lookup name, not an authentication secret or a room identifier.  The
    // key remains scoped to the current Windows user by the Microsoft Software KSP.
    internal const string WindowsKeyName = "TmrOverlay.OverlayBridge.DeviceIdentity.v1";

    private static readonly TimeSpan CertificateBackdate = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan CertificateLifetime = TimeSpan.FromDays(30);

    private readonly IOverlayBridgeDeviceKeyStore _keyStore;
    private readonly TimeProvider _timeProvider;

    public OverlayBridgeDeviceCredentialStore()
        : this(new OverlayBridgeWindowsCngDeviceKeyStore(), TimeProvider.System)
    {
    }

    internal OverlayBridgeDeviceCredentialStore(
        IOverlayBridgeDeviceKeyStore keyStore,
        TimeProvider timeProvider)
    {
        _keyStore = keyStore ?? throw new ArgumentNullException(nameof(keyStore));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public OverlayBridgeDeviceCredentialLoadResult TryLoadOrCreate()
    {
        using var keyResult = _keyStore.TryOpenOrCreate(WindowsKeyName);
        if (keyResult.Outcome != OverlayBridgeDeviceKeyStoreOutcome.Available
            || keyResult.SigningKey is null)
        {
            return OverlayBridgeDeviceCredentialLoadResult.FromKeyStore(keyResult.Outcome);
        }

        // Ownership transfers to the credential only after every public identity/certificate
        // check succeeds.  The credential disposes the ECDsa wrapper when its future transport
        // session stops; the CNG key itself remains in Windows' per-user key store.
        var signingKey = keyResult.DetachSigningKey();
        X509Certificate2? certificate = null;
        try
        {
            if (signingKey.KeySize != 256)
            {
                signingKey.Dispose();
                return OverlayBridgeDeviceCredentialLoadResult.Unavailable;
            }

            var identity = OverlayBridgeDeviceIdentity.Create(
                CreateDeterministicDeviceId(signingKey.ExportSubjectPublicKeyInfo()),
                signingKey.ExportSubjectPublicKeyInfo());
            certificate = CreateSelfSignedTlsCertificate(identity, signingKey);
            var ownerSigner = new OverlayBridgeRoomPolicySigner(identity, signingKey);
            return OverlayBridgeDeviceCredentialLoadResult.Available(
                new OverlayBridgeDeviceCredential(identity, certificate, ownerSigner, signingKey));
        }
        catch (CryptographicException)
        {
            certificate?.Dispose();
            signingKey.Dispose();
            return OverlayBridgeDeviceCredentialLoadResult.Unavailable;
        }
        catch (ArgumentException)
        {
            certificate?.Dispose();
            signingKey.Dispose();
            return OverlayBridgeDeviceCredentialLoadResult.Unavailable;
        }
        catch (NotSupportedException)
        {
            certificate?.Dispose();
            signingKey.Dispose();
            return OverlayBridgeDeviceCredentialLoadResult.Unavailable;
        }
    }

    private X509Certificate2 CreateSelfSignedTlsCertificate(
        OverlayBridgeDeviceIdentity identity,
        ECDsa signingKey)
    {
        var now = _timeProvider.GetUtcNow();
        var request = new CertificateRequest(
            new X500DistinguishedName($"CN=TmrOverlay Overlay Bridge {identity.DeviceId}"),
            signingKey,
            HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection
            {
                new Oid("1.3.6.1.5.5.7.3.1"), // TLS server authentication
                new Oid("1.3.6.1.5.5.7.3.2") // TLS client authentication
            },
            critical: true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

        return request.CreateSelfSigned(now - CertificateBackdate, now + CertificateLifetime);
    }

    private static string CreateDeterministicDeviceId(ReadOnlySpan<byte> subjectPublicKeyInfo)
    {
        var publicKeyHash = SHA256.HashData(subjectPublicKeyInfo);
        // The first 20 SHA-256 bytes provide 160 bits of identity correlation while keeping the
        // public identifier compact for pairing UX.  Policy/certificate validation still uses the
        // entire SPKI hash, so this label is never a substitute for exact key binding.
        return $"bridge-{Convert.ToHexString(publicKeyHash.AsSpan(0, 20))}";
    }
}

/// <summary>
/// A borrowed mTLS certificate and policy signer backed by one platform-owned private key.  The
/// type intentionally has no private-key export API.  Consumers may read the public identity and
/// pass <see cref="Certificate"/> to SslStream, but must dispose this lease when finished.
/// </summary>
internal sealed class OverlayBridgeDeviceCredential : IDisposable
{
    private readonly X509Certificate2 _certificate;
    private readonly ECDsa _signingKey;
    private bool _disposed;

    internal OverlayBridgeDeviceCredential(
        OverlayBridgeDeviceIdentity identity,
        X509Certificate2 certificate,
        OverlayBridgeRoomPolicySigner ownerPolicySigner,
        ECDsa signingKey)
    {
        Identity = identity ?? throw new ArgumentNullException(nameof(identity));
        _certificate = certificate ?? throw new ArgumentNullException(nameof(certificate));
        OwnerPolicySigner = ownerPolicySigner ?? throw new ArgumentNullException(nameof(ownerPolicySigner));
        _signingKey = signingKey ?? throw new ArgumentNullException(nameof(signingKey));
    }

    public OverlayBridgeDeviceIdentity Identity { get; }

    /// <summary>
    /// The same non-exportable private key backs this temporary self-signed leaf.  The future
    /// strict mTLS validator must bind its public SPKI to the current signed policy; it must not
    /// use the self-signed issuer, subject, or Windows trust store as membership authority.
    /// </summary>
    public X509Certificate2 Certificate => _certificate;

    /// <summary>
    /// Signs owner policy updates through the platform-backed key.  It has no persistence or
    /// private-key-export surface and is invalid after this credential lease is disposed.
    /// </summary>
    public OverlayBridgeRoomPolicySigner OwnerPolicySigner { get; }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _certificate.Dispose();
        _signingKey.Dispose();
    }
}

internal enum OverlayBridgeDeviceCredentialLoadOutcome
{
    Available = 0,
    UnsupportedPlatform = 1,
    Unavailable = 2
}

/// <summary>Safe startup result. It intentionally carries neither an exception message nor secret material.</summary>
internal sealed class OverlayBridgeDeviceCredentialLoadResult
{
    private OverlayBridgeDeviceCredentialLoadResult(
        OverlayBridgeDeviceCredentialLoadOutcome outcome,
        OverlayBridgeDeviceCredential? credential)
    {
        Outcome = outcome;
        Credential = credential;
    }

    public OverlayBridgeDeviceCredentialLoadOutcome Outcome { get; }

    public OverlayBridgeDeviceCredential? Credential { get; }

    public static OverlayBridgeDeviceCredentialLoadResult Available(OverlayBridgeDeviceCredential credential) =>
        new(OverlayBridgeDeviceCredentialLoadOutcome.Available, credential ?? throw new ArgumentNullException(nameof(credential)));

    public static OverlayBridgeDeviceCredentialLoadResult Unavailable =>
        new(OverlayBridgeDeviceCredentialLoadOutcome.Unavailable, null);

    internal static OverlayBridgeDeviceCredentialLoadResult FromKeyStore(OverlayBridgeDeviceKeyStoreOutcome outcome) => outcome switch
    {
        OverlayBridgeDeviceKeyStoreOutcome.UnsupportedPlatform =>
            new OverlayBridgeDeviceCredentialLoadResult(OverlayBridgeDeviceCredentialLoadOutcome.UnsupportedPlatform, null),
        OverlayBridgeDeviceKeyStoreOutcome.Available =>
            throw new ArgumentOutOfRangeException(nameof(outcome), "An available key-store result must include a signing key."),
        _ => Unavailable
    };
}

/// <summary>
/// Platform boundary kept deliberately below the app-facing credential store so unit tests can
/// prove identity/certificate/signer behavior without a Windows CNG dependency.
/// </summary>
internal interface IOverlayBridgeDeviceKeyStore
{
    OverlayBridgeDeviceKeyStoreResult TryOpenOrCreate(string keyName);
}

internal enum OverlayBridgeDeviceKeyStoreOutcome
{
    Available = 0,
    UnsupportedPlatform = 1,
    Unavailable = 2
}

internal sealed class OverlayBridgeDeviceKeyStoreResult : IDisposable
{
    private ECDsa? _signingKey;

    private OverlayBridgeDeviceKeyStoreResult(
        OverlayBridgeDeviceKeyStoreOutcome outcome,
        ECDsa? signingKey)
    {
        Outcome = outcome;
        _signingKey = signingKey;
    }

    public OverlayBridgeDeviceKeyStoreOutcome Outcome { get; }

    public ECDsa? SigningKey => _signingKey;

    public static OverlayBridgeDeviceKeyStoreResult Available(ECDsa signingKey) =>
        new(OverlayBridgeDeviceKeyStoreOutcome.Available, signingKey ?? throw new ArgumentNullException(nameof(signingKey)));

    public static OverlayBridgeDeviceKeyStoreResult UnsupportedPlatform =>
        new(OverlayBridgeDeviceKeyStoreOutcome.UnsupportedPlatform, null);

    public static OverlayBridgeDeviceKeyStoreResult Unavailable =>
        new(OverlayBridgeDeviceKeyStoreOutcome.Unavailable, null);

    public ECDsa DetachSigningKey()
    {
        if (Outcome != OverlayBridgeDeviceKeyStoreOutcome.Available || _signingKey is null)
        {
            throw new InvalidOperationException("A signing key can only be detached from an available platform-key result.");
        }

        var signingKey = _signingKey;
        _signingKey = null;
        return signingKey;
    }

    public void Dispose()
    {
        _signingKey?.Dispose();
        _signingKey = null;
    }
}
