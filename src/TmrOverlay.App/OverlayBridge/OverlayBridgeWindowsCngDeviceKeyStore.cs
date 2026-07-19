using System.Security.Cryptography;
using System.Runtime.Versioning;

namespace TmrOverlay.App.OverlayBridge;

/// <summary>
/// Windows-only CNG implementation for the single per-user Overlay Bridge P-256 identity key.
/// The Microsoft Software Key Storage Provider retains the private material.  This class never
/// serializes it, never requests an export policy, and does not provide a non-Windows fallback.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class OverlayBridgeWindowsCngDeviceKeyStore : IOverlayBridgeDeviceKeyStore
{
    public OverlayBridgeDeviceKeyStoreResult TryOpenOrCreate(string keyName)
    {
        if (string.IsNullOrWhiteSpace(keyName))
        {
            throw new ArgumentException("A Windows CNG key name is required.", nameof(keyName));
        }

        if (!OperatingSystem.IsWindows())
        {
            return OverlayBridgeDeviceKeyStoreResult.UnsupportedPlatform;
        }

        try
        {
            var key = OpenOrCreateKey(keyName);
            if (!IsAcceptableBridgeKey(key))
            {
                key.Dispose();
                return OverlayBridgeDeviceKeyStoreResult.Unavailable;
            }

            return OverlayBridgeDeviceKeyStoreResult.Available(new ECDsaCng(key));
        }
        catch (CryptographicException)
        {
            return OverlayBridgeDeviceKeyStoreResult.Unavailable;
        }
        catch (PlatformNotSupportedException)
        {
            return OverlayBridgeDeviceKeyStoreResult.UnsupportedPlatform;
        }
    }

    private static CngKey OpenOrCreateKey(string keyName)
    {
        var provider = CngProvider.MicrosoftSoftwareKeyStorageProvider;
        if (CngKey.Exists(keyName, provider))
        {
            return CngKey.Open(keyName, provider);
        }

        try
        {
            return CngKey.Create(
                CngAlgorithm.ECDsaP256,
                keyName,
                new CngKeyCreationParameters
                {
                    Provider = provider,
                    KeyUsage = CngKeyUsages.Signing,
                    ExportPolicy = CngExportPolicies.None
                });
        }
        catch (CryptographicException) when (CngKey.Exists(keyName, provider))
        {
            // A concurrent first-use may have created the named user key after Exists() but
            // before Create().  Reopen it and apply the same strict P-256/non-exportable checks.
            return CngKey.Open(keyName, provider);
        }
    }

    private static bool IsAcceptableBridgeKey(CngKey key)
    {
        return string.Equals(
                key.Algorithm.Algorithm,
                CngAlgorithm.ECDsaP256.Algorithm,
                StringComparison.Ordinal)
            && (key.KeyUsage & CngKeyUsages.Signing) == CngKeyUsages.Signing
            && key.ExportPolicy == CngExportPolicies.None;
    }
}
