# Version History

TmrOverlay uses SemVer-style annotated Git tags for product milestones:

- Tags use `vMAJOR.MINOR.PATCH`, for example `v0.6.0`.
- `0.x` versions are pre-release product milestones, so breaking local data or settings changes are still possible when they are documented and migrated.
- Patch versions are reserved for hardening, diagnostics, and compatibility work inside the same product shape.
- The checked-in build version should move with the branch that introduces the next milestone.

## Active Context Pointer

Use `docs/model-v2-future-branches.md` for session-handoff notes, current model-v2 app theory, and future-product signals. Keep this file focused on version history and milestone/release planning.

## Current Branch Target

### v0.20.1 - Windows Fixes And V1 Candidate Polish

Current branch name:

```text
v0.20.1-windows-fixes
```

Release/tag decision:

- Treat this as a patch hardening branch on top of the tagged `v0.20.0` model-completeness baseline unless the final branch diff deliberately widens into a new milestone.
- Do not create the `v0.20.1` release tag until the branch is merged or explicitly designated as the release point.
- `Directory.Build.props` is aligned to `0.20.1` so branch-built MSI and screenshot artifacts are distinguishable from the tagged `v0.20.0` baseline.
- Keep `fixtures/data-contracts/v0.19.0/` as the previous durable-release snapshot unless this branch deliberately changes a persisted user-data contract.

Planned scope:

- Fix Windows/native, browser review, and localhost parity issues in the current overlay/settings surfaces.
- Preserve completed `LiveTelemetrySnapshot.Models` as the active native, localhost, and browser overlay runtime contract.
- Keep `LatestSample` as collector, diagnostics, compatibility, and test evidence rather than an overlay rendering input.
- Keep localhost overlay pages on `/api/overlay-model/{id}` and keep `/api/snapshot` as a diagnostic/compatibility route, not an overlay page runtime dependency.
- Keep browser review as the primary local development surface, localhost as the OBS/browser-source route surface, and Windows as the production/iRacing behavior gate.
- Keep the deprecated mac harness out of the V1 parity, screenshot, and release gate.
- For any overlay/settings/renderer/browser/localhost change, keep screenshot generator and validation-profile coverage aligned across native Windows, browser review, and localhost.

Branch-complete checklist:

1. Re-read the final branch diff before writing the squash title/body; do not reuse the stale v0.20.0 model-completeness text for this patch branch.
2. Confirm `Directory.Build.props` stays aligned to `0.20.1` before tagging.
3. Refresh `docs/model-v2-future-branches.md` and any behavior docs touched by the actual fixes.
4. Run the branch-appropriate browser review, localhost, Windows screenshot, and Windows .NET gates before tagging.

Suggested squash title:

```text
[v0.20.1] Harden Windows parity and screenshot validation
```

Suggested squash body:

- Aligned native Windows, browser review, and localhost overlay parity for the Design V2 surfaces, including configured overlay sizes, table/metric row geometry, track map defaults/evidence, input graph evidence, Gap To Leader graph evidence, Stream Chat evidence, and review fixture data.
- Added Standings fastest-lap and last-lap columns, kept race-only Relative lap-down coloring, and preserved native/browser/localhost content-option coverage through shared settings contracts and tests.
- Expanded screenshot generation and validation so browser review, localhost, native Windows, settings components, installer menus, rows, cells, graph lines, colors, text, and fixture/source metadata can be compared in CI with detailed manifest failures.
- Added Windows installer screenshot capture and the browser installer review surface, kept MSI capture bundled with the Windows screenshot command, and removed the unresolved welcome placeholder path.
- Refreshed telemetry and SDK corpora for pit-service tire inventory evidence, NASCAR/PCup tire-set interpretation, and SDK field availability without changing durable user-data schemas.
- Refreshed branch docs, archived historical/research-only docs under `docs/archive/`, documented local branch MSI/screenshot generation, and aligned shared build metadata to `0.20.1`.

## Next Planned Milestone

No version number is assigned here yet. After `v0.20.1`, keep the next branch focused on V1-candidate release readiness unless teammate testing or Windows validation forces another patch.

Likely scope:

- Use the telemetry and SDK corpora for overlay behavior validation as teammate test evidence arrives.
- Continue release-readiness checks: product-scope lock, installer/update polish, first-run docs, Windows-native behavior validation, and support-bundle completeness.
- Keep branch theory, fixture-corpus decisions, and future-product discussion in `docs/model-v2-future-branches.md`.

## Merged Mainline Milestones

### v0.20.0 - Model Completeness Runtime Contract

Commit: `8ca6a6e`

Squash title:

```text
[v0.20.0] Make overlay runtime depend on completed live models
```

Summary:

- Promoted completed `LiveTelemetrySnapshot.Models` as the active native, localhost, and browser overlay runtime contract, with compatibility completion for older or partial live snapshots.
- Removed production browser shell dependence on `/api/snapshot`, `latestSample`, and `window.TmrBrowserModel`; localhost overlay pages now render through `/api/overlay-model/{id}` only while legacy snapshot routes remain diagnostic/backcompat endpoints.
- Completed models before native Design V2, simple telemetry, Gap To Leader, Fuel, Relative, Track Map, Radar, Input, Session/Weather, and Pit Service consumers render, and preserved session-scoped settings behavior across localhost and native paths.
- Tightened Gap To Leader native runtime and settings parity by removing direct `LatestSample` use and showing race-only chrome controls.
- Updated browser, localhost, and data-contract mapping tests so the v0.19.0 released settings snapshot maps into model-only browser, localhost, and native overlay consumers.
- Refreshed model/runtime docs and branch-complete validation notes; no durable user-data schema changes were introduced.

### v0.19.0 - V1 Candidate Overlay Parity And Readiness

Commit: `8b8efd6`

Squash title:

```text
[v0.19.0] Prepare V1 candidate overlay parity
```

Summary:

- Bumped shared product/version metadata to 0.19.0 and treated the branch as the V1 candidate overlay-readiness pass.
- Standardized product vocabulary: native is the Windows app/overlay windows, localhost is the OBS route surface, and browser is the local review UI.
- Promoted shared live reference/session facts so Standings, Relative, Gap To Leader, Track Map, diagnostics, and browser review replay start from the same focus/player/reference context.
- Aligned the production overlay suite across native and localhost with shared render/view models, settings-backed content/header/footer controls, and production-shaped localhost models where applicable.
- Finished the V2 settings surface pass, hardened overlay availability/persistence, regenerated product/teammate screenshots, and refreshed docs/repo notes for the V1 candidate behavior.
- Validated local release hygiene and left Windows .NET restore/build/test, real WinForms behavior, installer/update, iRacing SDK, and OBS-localhost validation as required Windows/CI gates before V1 readiness.

### v0.18.11 - Race-Start Overlay And Persistence Hardening

Commit: `9091545`

Squash title:

```text
[v0.18.11] Add telemetry corpus readiness guardrails
```

Summary:

- Bumped shared .NET product/version metadata to 0.18.11.
- Extended compact live telemetry and SDK field availability corpora with redacted race-start, endurance, AI, open-practice, and SDK-shape evidence.
- Added schema-drift and corpus validation so telemetry-backed overlay behavior starts from captured evidence.
- Stabilized race-start Standings, Relative, Gap To Leader, Flags, Fuel, and shared header behavior around race `SessionNum`, `SessionState`, pre-green countdowns, and placeholder timing signals.
- Preserved user settings and app data on update while keeping installed-build uninstall cleanup destructive.
- Hardened overlay z-order/input protection, Stream Chat guardrails, diagnostics bundles, browser replay tooling, and screenshot validation.
- Updated telemetry-analysis, release, overlay logic, and model-v2 docs for the corpus-backed source-selection workflow.

### v0.18.10 - Overlay Z-Order And Input Diagnostics Hardening

Commit: `9d1356e`

Squash title:

```text
[v0.18.10] Harden overlay z-order and input diagnostics
```

Summary:

- Bumped shared .NET product/version metadata to 0.18.10.
- Kept product overlays in their saved topmost band while making Settings temporarily topmost only while active, so overlays can stay above iRacing after focus returns to the sim.
- Preserved Stream Chat and transparent overlay click-through/no-activate behavior while avoiding the previous Settings z-order demotion path.
- Added `metadata/window-z-order.json` diagnostics with current foreground HWND, recent foreground-window changes, and top-level desktop window topmost/z-order state.
- Fixed Input / Car State steering conversion from iRacing radians to displayed degrees, smoothed native/localhost/mac input traces, and guarded native/localhost wheel rendering against bottom clipping.
- Updated docs and Windows screenshot tool wiring for the foreground-window diagnostics dependency.

### v0.18.7 - V1 Candidate Focus And Diagnostics Hardening

Commit: `bb17917`

Summary:

- Bumped shared .NET product/version metadata to 0.18.7.
- Hardened focus semantics and diagnostics after V1-candidate Windows testing.
- Expanded browser validation, diagnostics bundle metadata, and local review tooling while preserving Windows as the production runtime.
- Kept remaining V1 work focused on validation, installer/update polish, first-run/user docs, privacy/defaults review, and Windows-native behavior checks.

### v0.18.4 - Harden Installer Cleanup And Windows Validation Fixes

Commit: `265788e`

Summary:

- Bumped shared .NET product/version metadata to 0.18.4.
- Added a Velopack uninstall hook that removes TmrOverlay's LocalAppData stores while guarding against unsafe configured paths.
- Added startup/update cleanup for stale TechMatesRacing.TmrOverlay package folders and shortcuts, plus diagnostics bundle metadata for the cleanup result.
- Updated release packaging to request Desktop and Start Menu shortcuts and added branded installer welcome/splash assets.
- Fixed invisible/topmost overlay input diagnostics, support/localhost screenshot crops, simple telemetry overlay heights, and blank fuel calculator rows.
- Regenerated MSI banner/logo artwork with a brighter first-screen treatment and updated installer release docs.

### v0.18.3 - Refresh Windows Overlay Screenshots And Settings Typography

Commit: `b2c5e71`

Squash title:

```text
[v0.18.3] Refresh Windows overlay screenshots and settings typography
```

Summary:

- Bumped shared .NET product/version metadata to 0.18.3.
- Refreshed Windows overlay/settings screenshot artifacts, manifest, and contact sheet for the current UI.
- Fixed Windows Design V2 settings typography scale so the production settings surface matches the expected visual density.
- Removed stale tracked diagnostic zip artifacts from release-ready source.

### v0.18.2 - Force Startup Diagnostics For Frozen UI

Commit: `3759454`

Squash title:

```text
[v0.18.2] Force startup diagnostics for frozen UI
```

Summary:

- Bumped shared .NET product/version metadata to 0.18.2.
- Forced raw diagnostic telemetry capture on at startup so testers can collect evidence even when Settings is frozen.
- Kept advanced telemetry/model/overlay diagnostics enabled and increased file logging to Debug with larger retained log files.
- Preserved the v0.18.1 MSI/Velopack install and update behavior for patch upgrade testing.

### v0.18.1 - Restore Windows Settings Clickability

Commit: `0a6b070`

Squash title:

```text
[v0.18.1] Restore Windows settings clickability
```

Summary:

- Bumped shared .NET product/version metadata to 0.18.1.
- Kept product overlay windows from taking activation when they are shown while the Settings app is already active.
- Reasserted the Settings window after overlay settings are applied so restored startup overlays cannot sit above it and intercept clicks.
- Preserved the v0.18.0 MSI/Velopack install and update behavior for patch upgrade testing.

### v0.18.0 - Windows MSI Install And Active Updates

Commit: `88357bd`

Squash title:

```text
[v0.18.0] Add MSI install flow and active Velopack updates
```

Summary:

- Bumped shared .NET product/version metadata to 0.18.0.
- Updated Velopack SDK/CLI packaging to generate the MSI installer and update feed from the same release build.
- Added tracked MSI banner/logo artwork generated from the TMR brand asset.
- Published one user-facing Windows installer option, the MSI, while keeping Velopack update packages/feed and the portable zip fallback.
- Added user-initiated update download/install and restart-to-apply actions in the tray menu and Support settings surface.
- Expanded release update state and diagnostics metadata for progress, pending restart/apply state, action availability, and failures.
- Updated Windows release/update docs and regenerated the teammate installer guide.

### v0.17.0 - Design V2 Theme Foundation

Commit: `b30afd7`

Squash title:

```text
[v0.17.0] Promote Design V2 settings across mac and Windows
```

Summary:

- Bumped shared .NET product/version metadata to 0.17.0.
- Made the tracked mac harness Design V2 settings shell the primary mac settings surface for the main app and current overlay tabs.
- Promoted Design V2 into the Windows production settings app through an additive WinForms V2 surface over the existing settings contracts.
- Preserved production settings behavior for visibility, scale, opacity, sessions, header/footer/content toggles, localhost routes, Garage Cover image import, and Stream Chat setup.
- Added shared Design V2 tokens, component screenshots, and screenshot validation coverage for mac and Windows settings review.

### v0.16.1 - Overlay Feedback Hardening

Commit: `d39188e`

Squash title:

```text
[v0.16.1] Harden overlay layout, race projection, and diagnostics
```

Summary:

- Bumped shared .NET product/version metadata to 0.16.1.
- Added shared content-column architecture for Standings, Relative, and Inputs with overlay-owned keys, visible/editable pixel widths, stable disabled ordering, matching native/localhost defaults, and recommended OBS source dimensions.
- Reworked Standings native/localhost behavior around scoring-first start rows, configurable columns, class-colored separators, configurable other-class row blocks, projection-backed class lap estimates, class color caching, and a wider default layout.
- Hardened Gap To Leader for long races with a four-hour visible window, same-lap reference selection, leader/reference transitions, clearer axis labels, and local 4-hour/24-hour telemetry demo coverage.
- Added shared live race projection data for timed-race lap estimates and reused it in Fuel, Session / Weather, Flags, Standings separators, localhost metadata, and future strategy/fuel calculations.
- Updated Input / Car State native/localhost rendering so the line graph is primary and current speed/gear/pedal/steering widgets are toggleable right-side content.
- Reworked Settings overlay tabs into left-side General/Content/Header/Footer sections, with Inputs suppressing unused header/footer tabs and settings save/apply work coalesced to avoid recursive UI churn.
- Added flags/settings freeze hardening and diagnostics, brought the mac harness into source control and CI, and aligned release packaging around the `TMROverlay` package id and `TMROverlay.exe` executable name.

### v0.13.0 - Core Overlay Readiness

Commit: `f9b7026`

Squash title:

```text
[v0.13.0] Harden core overlays on model-v2 telemetry
```

Summary:

- Bumped shared .NET product/version metadata to 0.13.0.
- Added shared model-v2 race-progress, overlay availability/freshness, and app diagnostics status contracts.
- Promoted core overlays to normalized model-v2 consumers across Standings, Relative, local Radar, Flags, Session / Weather, Pit Service, Input / Car State, Fuel, Gap To Leader, and Track Map.
- Reworked Standings around scoring-snapshot ordering, class grouping, configurable other-class rows, pit labels, and localhost settings.
- Kept Track Map marker placement honest under partial live coverage by plotting only cars with usable spatial progress and using scoring rows only for identity/color enrichment.
- Scoped Radar to accurate local player-in-car proximity and side-warning telemetry while leaving non-local focus and timing-only placement as future evidence-aware work.
- Added Garage Cover image import, preview, localhost fail-closed behavior, and diagnostics.
- Extended compact diagnostics and docs for forecast-like weather, spotter/teammate pit changes, TC/dual-clutch evidence, relative lap-relationship probes, and future V1.N model foundations.

### v0.12.0 - Teammate Beta Hardening

Commit: `267c841`

Squash title:

```text
[v0.12.0] Harden teammate beta install and support flow
```

Summary:

- Bumped shared .NET product/version metadata to 0.12.0.
- Added Support-tab version/build metadata and clearer teammate support actions.
- Hardened waiting/support states for beta handoff.
- Updated validation docs and Windows screenshot expectation checks.
- Kept release/update flow focused on portable teammate beta distribution.

### v0.11.0 - Standings, Track Maps, Localhost, And Live Overlay Polish

Commit: `f6be067`

Squash title:

```text
[v0.11.0] Add standings, track maps, localhost, and live polish
```

Summary:

- Bumped shared .NET product/version metadata to 0.11.0.
- Added production Standings and Track Map overlay registrations, settings tabs, Windows screenshot fixtures, and mac harness review surfaces.
- Added compact Standings rendering from normalized model-v2 timing rows, including leader gap, focus interval, and pit-road status.
- Added a transparent map-only Track Map surface with bundled-map support, circle fallback, live car dots, IBT-derived local map generation, confidence diagnostics, bundled-map assets, and sector-highlight diagnostics.
- Added `LocalhostOverlays` support for the supported overlay suite while keeping Overlay Bridge separate as a future trusted teammate-to-teammate sharing boundary.
- Added Stream Chat source selection for Streamlabs Chat Box widget URLs or public Twitch channel chat, with diagnostics redaction for private Streamlabs URLs.
- Restored flat settings tabs, kept the larger fixed settings window, and hardened live overlay refresh/fade behavior for Relative, Fuel Calculator, Inputs, and local Radar.
- Regenerated tracked settings/overlay screenshots and updated docs/context for the v0.11 product shape.

### v0.10.0 - Windows Screenshot Parity Validation

Commit: `526c47f`

Squash title:

```text
[v0.10.0] Add Windows screenshot parity validation
```

Summary:

- Added WinForms screenshot generation and CI validation for Windows overlay parity artifacts.
- Branded the app entry point with the TMR logo.
- Removed the user-facing font picker and kept font choice in theme/platform defaults.
- Added a teammate-facing release install/support tutorial image plus validation/docs for the handoff flow.
- Stabilized Windows screenshot rendering and validation against CI runner constraints.
- Updated v0.x/v1/v2 roadmap planning around teammate hardening, overlay bridge, model migration, style v2, replay, track maps, strategy, engineer, streaming, builder, broader platform work, and later VR scope.

### v0.9.0 - Production Publishing And Updates

Commit: `8e4961d`

Squash title:

```text
[v0.9.0] Add production publishing and teammate update flow
```

Summary:

- Bumped shared .NET product/version metadata to 0.9.0 and wired a generated Windows executable icon derived from the checked-in TMR logo.
- Hardened the Windows GitHub Actions workflow so PR/main run restore/build/test, tracked screenshot validation, and a self-contained publish dry run with package audit, while tag/manual release packaging publishes a self-contained win-x64 app, zips it, generates manifest/checksum files, and uploads release artifacts.
- Added tag-driven GitHub Release creation for vMAJOR.MINOR.PATCH tags with versioned Windows zip/manifest/checksum assets and generated release notes.
- Added Windows tester release docs covering package contents, download, checksum verification, portable install, app-data compatibility, schema-migration expectations, upgrade, rollback, unsigned SmartScreen expectations, and diagnostics handoff.
- Reworked the Support tab into a task-oriented surface for app status, diagnostics bundle actions, diagnostic telemetry capture, storage shortcuts, compact app activity, and discoverable advanced collection status.
- Disabled advanced collection outputs by default for tester builds, including edge-case clips, model-v2 parity, live overlay diagnostics, and post-race analysis, while keeping config/env overrides and tests for enabling them.
- Added repo-surface docs and ignore rules to separate customer/tester-facing material, product source, internal development assets, and generated local runtime data.
- Updated the update strategy to treat portable GitHub Releases as the v0.9 baseline while keeping signing, installer selection, and passive update checks as follow-up work.

### v0.8.0 - Settings And Overlay Polish

Commit: `516d54f`

Squash title:

```text
[v0.8.0] Polish settings, overlay controls, and mac review parity
```

Summary:

- Reworked Settings into a fixed-size app control surface with driving overlays hidden by default, ordered user-facing tabs, Support-owned diagnostic capture, and internal/deferred product surfaces removed from the normal tab list.
- Added per-overlay opacity controls where useful, while keeping Flags and Radar exempt, and kept Gap To Leader race-only in settings/runtime policy.
- Polished Relative, Flags, Input/Car State, and Gap behavior: compact actual relative rows, vertical class marker, screen-border flag display that follows active telemetry flags, graphical pedal traces/wheel input, and live-updating gap context.
- Added TMR brand assets plus Windows tray/settings icon loading from the checked-in source logo.
- Brought the ignored mac harness to v0.8 review parity, including matching settings options, telemetry-driven flag display, overlay defaults, regenerated screenshots, and validation updates.
- Bumped settings migration to prune obsolete flag timer options now that flag display lifetime is driven by telemetry.
- Updated branch-complete hygiene so docs, screenshots, stale-reference sweeps, version text, and release metadata are treated as final branch-readiness artifacts.

### v0.7.0 - Simple Model-V2 Overlays, Design-V2 Preview, And Release Hygiene

Commit: `79fbec7`

Squash title:

```text
[v0.7.0] Add simple model-v2 overlays, design-v2 previews, and release hygiene
```

Summary:

- Added production model-v2 overlay paths for relative, flags, session/weather, pit-service, input/car-state, and local in-car radar.
- Added shared simple telemetry overlay shell/view-model helpers for dense direct-telemetry windows.
- Expanded live model-v2 timing, spatial, sector, lap-delta, and diagnostics evidence for simple overlays and later analysis products.
- Added design-v2 mock states for relative, standings, flags, sector comparison, blindspot, laptime delta, and stint laptime log.
- Documented future product branches including IBT-derived track-map generation, pit crew/engineer workflows, overlay bridge, streaming overlays, overlay builder, and publishing.
- Added version-history metadata, branch-complete release hygiene validation, and shared .NET build version metadata.
- Split CI into a stable Windows build/test merge gate and tag-triggered Windows release package artifact.
- Fixed session/weather nullable wet-flag handling and covered unknown wet telemetry with a focused test.

## Tagged Mainline Milestones

### v0.6.0 - Model V2 Groundwork, IBT Analysis, And Overlay Diagnostics

Commit: `109b9b3`

Squash title:

```text
[v0.6.0] Add model-v2 groundwork, IBT analysis, parity, and diagnostics
```

Summary:

- Added normalized live model-v2 contracts, timing column registry, live race model builder, and parity documents.
- Added bounded post-session capture synthesis and model-v2 promotion-readiness evidence.
- Added IBT telemetry logging/analysis sidecars, including local-car trajectory/fuel/vehicle-dynamics readiness and live-vs-IBT schema comparison.
- Added live overlay diagnostics for gap, radar, fuel, position cadence, lap delta, and sector-timing assumptions.
- Added offline raw-capture analysis tooling and compact telemetry-analysis fixtures.
- Added overlay catalog mocks and validation tooling for future design work.

### v0.5.5 - Diagnostics, History Compatibility, And Data-Collection Hardening

Commit: `edf7306`

Squash title:

```text
[v0.5.5] Harden diagnostics, history migration, telemetry edge cases, and screenshots
```

Summary:

- Hardened Windows runtime behavior after data-collection and diagnostic runs.
- Added history schema version constants, startup maintenance, compatible summary normalization, aggregate rebuilds, and schema-compatibility tests.
- Added compact telemetry edge-case detection and recording for suspicious live states.
- Added performance snapshots, app health logging, diagnostics bundle expansion, and retention updates.
- Reworked radar, fuel calculator, gap-to-leader, settings, and status overlay behavior from captured evidence.
- Replaced SVG-only mock states with rendered screenshot artifacts and added screenshot validation tooling.
- Added human-readable overlay logic docs for current overlays and update strategy.

### v0.5.0 - Settings-Owned Overlay Control And Shared Core Refactor

Commit: `c327e64`

Squash title:

```text
[v0.5.0] Move overlay control into settings and split shared Core models
```

Summary:

- Added the settings panel as the primary app control surface for overlay visibility, scale, filters, support actions, and post-race analysis browsing.
- Moved platform-neutral app settings, overlay metadata, historical models, live telemetry abstractions, post-race analysis models, and fuel strategy logic into `TmrOverlay.Core`.
- Added descriptor-driven overlay options and settings migration.
- Added shared overlay theme tokens and centralized overlay manager behavior.
- Added disabled-by-default localhost HTTP scaffolding.
- Added a Windows .NET GitHub Actions build/test workflow.

### v0.4.0 - Radar And Gap-To-Leader Overlays

Commit: `5fa3174`

Squash title:

```text
[v0.4.0] Add proximity radar and gap-to-leader graph overlays
```

Summary:

- Added first-pass car radar and class gap-to-leader overlays.
- Expanded live telemetry snapshots with proximity, class timing, track progress, and gap graph inputs.
- Added car-radar and gap-to-leader screenshot mocks.
- Added parsing and history support needed for class/track context.
- Added proximity and live telemetry store tests.

### v0.3.0 - Fuel Calculator V1

Commit: `10f7d9d`

Squash title:

```text
[v0.3.0] Add fuel calculator v1 and stint-history support
```

Summary:

- Added the fuel calculator overlay and first fuel strategy calculator.
- Added live telemetry storage for fuel, lap, pit, and session context.
- Expanded session history summaries, aggregates, and query services for stint/fuel estimates.
- Added persistent overlay window behavior and overlay manager support for multiple overlays.
- Added fuel calculator mocks, post-race strategy notes, and focused tests.

### v0.2.1 - Post-Capture Collection Hardening

Commit: `838154b`

Squash title:

```text
[v0.2.1] Harden capture status, history export, and build freshness checks
```

Summary:

- Improved failed/waiting/live telemetry messaging in the status overlay.
- Added runtime build-freshness checks so local builds warn when source files are newer than the executable.
- Hardened raw capture session state, manifest metadata, status snapshots, and replay behavior.
- Added history export and YAML forensics tools for analyzing collected captures.
- Added baseline synthetic endurance history fixtures for future overlay work.

### v0.2.0 - App Boilerplate, Storage, History, And Mac Harness

Commit: `3d93d33`

Squash title:

```text
[v0.2.0] Add app storage, history, diagnostics, replay, and mac harness
```

Summary:

- Reorganized the Windows app into shell, overlay, storage, history, logging, settings, diagnostics, runtime, retention, and replay modules.
- Added app-owned local storage defaults for captures, history, logs, settings, diagnostics, events, and runtime state.
- Added compact car/track/session history summaries with baseline/user history separation.
- Added rolling logs, JSONL app events, runtime clean-shutdown markers, diagnostics bundles, and startup retention cleanup.
- Added persisted overlay settings and replay-mode scaffolding.
- Added the ignored local macOS harness for mock-telemetry overlay iteration.
- Added xUnit test scaffolding for Windows logic.

### v0.1.0 - Raw Capture Scaffold

Commit: `85dd734`

Squash title:

```text
[v0.1.0] Add raw capture scaffold and first telemetry sample
```

Summary:

- Added the first Windows tray/status-overlay capture scaffold.
- Added raw telemetry capture options and capture output documentation.
- Added the `TmrOverlay.cmd` local launcher.
- Added the first captured telemetry sample, schema, session info, and `telemetry.md` field summary.
- Added initial repo/agent context for fuel and overlay research.

### v0.0.0 - Initial Repo Boilerplate

Commit: `8d3358f`

Squash title:

```text
[v0.0.0] Add initial repo boilerplate
```

Summary:

- Created the initial repository scaffold before product versioning started.
