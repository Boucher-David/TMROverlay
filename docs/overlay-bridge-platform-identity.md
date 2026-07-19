# Overlay Bridge Platform Identity Validation

The future remote Bridge client has one app/user-scoped P-256 device identity. On Windows, `OverlayBridgeWindowsCngDeviceKeyStore` creates or opens the named key in the Microsoft Software Key Storage Provider with signing-only usage and `CngExportPolicies.None`. The app derives the public device ID from the SPKI hash, makes an in-memory short-lived self-signed TLS leaf, and passes the same non-exportable key to `OverlayBridgeRoomPolicySigner`.

It writes no PFX, PEM, private-key blob, room secret, invite, relay address, pairing record, or telemetry. The KSP key name is an application lookup label, not a credential or room identifier. A future policy/configuration store may retain only the approved public identity and policy metadata described in the V1.3 proposal; its private owner key stays in the Windows user key store.

The certificate's issuer, subject, and Windows trust-store state are not membership authority. The production mTLS validator must verify the self-signed leaf's time/key usage and bind the presented P-256 SPKI to the current owner-verified policy before admitting a Bridge circuit.

## Deterministic coverage and native gap

`OverlayBridgeDeviceCredentialStoreTests` uses an injected ephemeral test key to prove the identity, self-signed certificate, and owner-policy signer use the same public key and that unsupported platforms fail closed. It intentionally does not exercise an OS key store or test private-key export behavior.

Before enabling Oracle/remote connection, manually validate on a Windows runner under a normal non-admin user:

1. Call the Windows credential store twice and confirm the public device identity is stable.
2. Confirm `Certificate.HasPrivateKey` and a policy signature succeed while `ExportPkcs8PrivateKey` and PFX/private export fail for the CNG-backed key.
3. Restart the app and repeat, then remove the application/user data through the product uninstall path and explicitly define whether the retained Windows KSP key is removed or deliberately preserved.
4. Run the future strict mTLS callback against this certificate, including expired leaf, altered EKU, wrong policy SPKI, revoked policy epoch, and role mismatch rejection cases.

No listener, relay connection, Oracle credential, pairing workflow, durable bridge configuration, or telemetry export is enabled by this identity seam.
