# TmrOverlay Agent Notes

Start here when continuing work in this repo.

## Current Product Shape

- Windows tray application in `src/TmrOverlay.App/`
- Platform-neutral settings, history, live telemetry, fuel, overlay metadata, and post-race analysis models in `src/TmrOverlay.Core/`
- Deprecated tracked local macOS harness in `local-mac/TmrOverlayMac/` for secondary mock-telemetry and legacy native-shell scaffolding; it is not a V1 parity, screenshot, or release gate
- Startup surface: fixed-size settings app window; driving/support overlays are opt-in from settings and default hidden
- Settings panel owns overlay visibility, scale/custom size, content/header/footer session gates where relevant, shared font/units, and support capture/diagnostics controls; future product surfaces such as Overlay Bridge and post-race analysis should not be exposed as ordinary overlay tabs without a product pass
- iRacing ingestion through `irsdkSharp`
- Default-on localhost routes for supported OBS overlays; future Overlay Bridge work remains separate from localhost
- Raw capture pipeline that writes:
  - `capture-manifest.json`
  - `telemetry-schema.json`
  - `telemetry.bin`
  - `latest-session.yaml`
  - `session-info/`

## Read Next

`AGENTS.md` is the authoritative repo-level contract. The repo skill below is supplemental context for deeper product/current-state details.

- `skills/tmr-overlay-context/SKILL.md`
- `skills/tmr-overlay-hot-start/SKILL.md`
- `skills/tmr-overlay-validation/SKILL.md`
- `docs/model-v2-future-branches.md`
- `skills/tmr-overlay-context/references/current-state.md`
- `skills/tmr-overlay-context/references/fuel-overlay-context.md`
- `skills/tmr-overlay-context/references/overlay-research.md`
- `docs/overlay-logic.md`
- `docs/capture-format.md`
- `docs/data-contracts.md`
- `telemetry.md`
- `README.md`

## Guardrails

- Preserve the collector-first architecture unless there is a strong reason to change it.
- Keep Windows as the production/iRacing runtime, browser review as the primary local development surface, and localhost as the OBS route surface.
- The mac harness under `local-mac/TmrOverlayMac/` is tracked source but deprecated secondary scaffolding. Keep it buildable when touched, but do not treat it as a product parity target or screenshot authority.
- If you change the raw capture format, update `docs/capture-format.md` and `README.md` in the same pass.
- Prefer shared Core models/read services, descriptor-driven overlay options, and `OverlayTheme` tokens over one-off UI contracts.
- Design mocks should combine new visual treatment with current production content contracts by default. Keep the displayed fields, ordering, data source semantics, settings-driven content options, and native/localhost parity aligned with the current product unless the mock is explicitly proposing a content change. Browser review is the local development surface for checking that parity, not a separate product runtime.
- Product overlays should read normalized live state through `ILiveTelemetrySource`; telemetry providers should write through `ILiveTelemetrySink`.
- Mirror shared app/overlay/boilerplate changes into the tracked mac harness only when deliberately maintaining that secondary scaffold; native/browser/localhost parity is the active product target.
- In the Windows app, fully qualify timer types: use `System.Threading.Timer` for hosted/background services and `System.Windows.Forms.Timer` for UI refresh loops. WinForms implicit globals import both namespaces, so bare `Timer` is ambiguous on Windows.
- Waiting/unavailable/error preview states must use deterministic isolated fixtures. Do not let local user history, cached telemetry, or machine-specific paths make an empty state look populated unless the scenario explicitly tests history fallback or support-path display.
- For wider app changes, carry validation discipline into tests and fixtures: assert both data that should appear and data that must stay hidden, cover failure/degraded paths, and keep performance/diagnostics/update flows fixture-driven where possible.
- During exploratory or iterative implementation, do not run the full docs/tests/validation sweep after every prompt. Use targeted checks only when they directly de-risk the current edit, and defer broader docs, fixtures, screenshots, and validation until the user-approved stopping point or branch-complete pass.
- If a durable user-data schema changes, treat backwards compatibility as part of the same validation sweep: update version constants, migrations or compatible readers, docs, the schema-compatibility test, and the versioned snapshots under `fixtures/data-contracts/` before final validation.
- Treat data-contract snapshot tests as product-contract evidence. When a snapshot mapping or compatibility test fails, first decide whether the snapshot is exposing a real product/reader/fixture mismatch before changing assertions; do not blanket-update expected values just to make the test pass.
- Treat screenshot and manifest validation failures as product evidence until proven otherwise. Never assume a red screenshot check is "just a validator issue" or "just stale expectations"; first classify it with concrete evidence from the generated screenshot, manifest fields, source fixture, renderer, or generator. If the current artifacts cannot prove whether browser, localhost, and Windows rendered the right behavior, add or improve capture/manifest evidence and keep the semantic assertion strict until the ambiguity is resolved.
- When implementation behavior, calculations, defaults, source labels, fixture data, or validation semantics change, update the affected build test assertions and test fixtures in the same pass. Treat stale passing or failing assertions as stale references, not as a separate cleanup task.
- When code changes add or materially change an overlay, settings tab/region, renderer path, preview mode, browser/localhost route, or native surface, update the screenshot generators and screenshot validation profiles in the same pass. The validation must prove native Windows, browser review, and localhost coverage exists; do not rely on manually inspected screenshots that are not represented in `tools/validate_overlay_screenshots.py`.
- New UI surfaces must include forensic screenshot evidence at the same standard as existing overlay validators: deterministic fixture state, manifest semantics for visible text/data/availability, geometry or pixel evidence for layout-sensitive behavior, degraded/non-happy-path states where relevant, and explicit browser review, localhost, and Windows/native parity coverage or a documented reason a surface does not exist on one of those runtimes.
- For live-model, snapshot-reader, data-contract, or overlay data-mapping changes, prefer a targeted data-snapshot render sweep over expanding the default screenshot generator to all historical snapshots. Keep the selected fixture list explicit and validate the manifest metadata so the artifact explains which snapshot/model source produced each image.
- Before declaring a branch complete, run the branch-complete release hygiene in `skills/tmr-overlay-validation/SKILL.md`: patch stale docs/context references, regenerate and validate screenshot artifacts for overlay/settings UI changes, inspect branch commits, sanitize the first commit or planned squash text, update `VERSION.md`, align `Directory.Build.props` version metadata for milestone branches, and create annotated tags only after the release commit is on `main` or explicitly designated as the release point.
- The authoring machine used for the initial scaffold did not have `dotnet` installed, so build/test verification still needs to happen on Windows.
