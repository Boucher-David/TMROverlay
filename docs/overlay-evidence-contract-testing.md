# Overlay Evidence Contract Testing

## Purpose

The existing validation fleet is strong at proving generated artifacts, route coverage, screenshot geometry, and known screenshot-manifest assertions. The V1.0.3 forensics showed a gap: many failures are semantic data-contract failures before they become screenshot failures.

The overlay evidence contract lane is a parallel validation approach, and the evidence fields are now integrated into the app model layer. It normalizes each overlay model into a contract that describes runtime surfaces, settings fingerprints, provenance, semantic rows/columns/text, and render evidence. Screenshot validation remains useful, but it should validate pixels after this contract says the model is meaningful.

## Current Fleet vs Evidence Contract

| Area | Current fleet | Evidence contract lane |
| --- | --- | --- |
| Primary proof | Generated screenshots, manifests, selected model assertions, route response tests | Normalized semantic contract before screenshot generation |
| Best at | Pixel/layout drift, screenshot coverage, native/browser/localhost artifact parity | Data meaning, provenance, settings parity, row identity, forbidden text, unavailable/stale state policy |
| Typical blind spot | Artifact exists but does not prove semantic parity | Does not replace final pixel/layout validation |
| Failure timing | Often after screenshots or support bundles exist | Before pixels, directly from model/evidence JSON |
| Cross-runtime parity | Manifest parity after artifacts are generated | `browserReview`, `localhostObs`, and `windowsNative` evidence fields in one object |

## Contract Fields

Every overlay evidence contract uses:

- `runtimeSurfaces`: browser review, localhost OBS, and Windows native source evidence, route identity, settings hashes, and pixel status.
- `settingsFingerprint`: shared and overlay-specific settings hash parity.
- `provenance`: `live-capture`, `synthetic-preview`, `stale-history`, or `unavailable`, plus capture specificity and source contract.
- `semanticModel`: title/text/body kind, row identities, column keys, placeholder count, metric text, layout density, fallback state, graph/input/table evidence.

`BrowserOverlayDisplayModel` is the production presentation contract for the
browser-review and localhost/OBS renderers. Production model replay serializes
that response from the C# `BrowserOverlayModelFactory`, and the review server
returns it byte-for-byte for `fixture=production-model-replay`; synthetic Node
fixtures remain renderer tests and must not be mistaken for production model
evidence. Native WinForms keeps its own rendering/lifecycle model, so parity
starts with test-only semantic projections of shared simple-overlay adapters
(Session / Weather and Pit Service) rather than an artificial universal runtime
presentation model. Geometry, chrome, and pixel parity remain their existing
surface-specific contracts.

The scenario JSON remains an evidence registry, not application input. Its
top-level execution suites bind selected covered scenarios to generated
manifests after screenshot validation. The first suite executes every
minimum-scale case across browser review, localhost/OBS, and supported Windows
native overlays, writing a per-artifact execution report that identifies the
scenario, fixture variant, required `shouldRender` state, semantic body kind,
surface, and exact manifest/contract hashes. The final cross-surface job
validates those three reports against the checked-out contract as well as
running the existing manifest comparator.

The adjacent `all-overlay-visible-hidden-visible` runtime suite deliberately
separates three concerns: a Windows C# localhost test drives all twelve real
`BrowserOverlayModelFactory` routes through enabled → disabled → restored, a
browser test verifies the shared renderer clears stale content/header and
opacity before polling and rendering a restored response, and the native
visibility test proves the same toggle transition reaches every Windows-native
overlay decision. Its browser models are explicit renderer-protocol fixtures,
never a substitute for C# model evidence.

## Validation Rules

The integrated implementation lives in:

- `tools/validation/overlay-evidence-contract.mjs`
- `tests/browser-overlays/overlayEvidenceContract.test.js`
- `tools/validation/overlay-evidence-framework-comparison.json`
- `tools/browser-review/server.mjs`
- `src/TmrOverlay.App/Overlays/BrowserSources/BrowserOverlayModelFactory.cs`
- `tests/TmrOverlay.App.Tests/Overlays/BrowserOverlayModelFactoryTests.cs`

Run it with:

```bash
npm run test:evidence-contract
```

CI runs the same command in the separate `Overlay evidence contract` workflow job. This lane is intentionally separate from `npm run test:settings-effects`. That lets us compare the old and new approaches without hiding whether the evidence-contract lane catches failures earlier.

## Closure Policy

For each confirmed fix or new forensic finding, add:

- a low-level semantic contract assertion,
- a forbidden-content assertion where relevant,
- cross-runtime settings/provenance/route evidence when a surface can drift,
- screenshot/layout evidence only after the contract is meaningful,
- one comparison-matrix row tying the finding to the lane that catches it.

## Current V1.0.3 State

The evidence-contract lane is expected to fail on the current branch, but the intended remaining failures are product/data-contract regressions rather than missing evidence plumbing. Browser review and the production browser/localhost model factory now emit settings hashes, route evidence, native pixel-evidence status, row and column identities, provenance, table/timing diagnostics, metric layout density, fuel strategy evidence, input availability evidence, map fallback evidence, local role context, and unavailable-content policy fields.

Local role context is explicit validation data, not overlay UI. It carries
player/focus car identity, `DriverInfo.Drivers[].IsSpectator` when available,
derived `localRole`, and a nullable `isSpotting` field reserved for a future SDK
or derived spotting signal.

Local check on 2026-05-19: `npm run test:evidence-contract` passed the comparison, normalization, plumbing-separation, and cross-contract comparison tests, then failed the semantic product contract and explicit provenance tests as intended. The useful red families are redundant ordinary overlay titles, Input waiting/trace availability, and unavailable previews carrying stale-looking content.
