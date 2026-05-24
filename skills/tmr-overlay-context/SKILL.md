---
name: tmr-overlay-context
description: Use when continuing work in the tmrOverlay repo and deeper product/current-state context is needed beyond AGENTS.md. Summarizes the Windows tray app, normal-desktop settings UI, live overlay suite, iRacing telemetry pipeline, opt-in raw capture format, diagnostics/performance support paths, browser review screenshot workflow, deprecated mac scaffold, fuel-overlay findings, overlay research notes, known limitations, and next priorities.
---

# TmrOverlay Context

Use this repo-local skill when the task is about continuing or extending `tmrOverlay`.

`AGENTS.md` is the authoritative repo-level contract. This skill is a supplemental context loader; keep detailed product state in the referenced files instead of duplicating every guardrail here.

## Workflow

1. Read `AGENTS.md` first if it is not already in context.
2. Read `references/current-state.md`.
3. If the task is about SDK field semantics, `SessionState`, `SessionNum`, `SessionFlags`, `CarIdx*` timing/position/progress arrays, live source selection, or telemetry-backed overlay behavior, read `references/iracing-sdk-telemetry-interpretation.md`.
4. If the task is about fuel, strategy, stint logic, or telemetry interpretation, read `references/fuel-overlay-context.md`.
5. If the task is about overlay features, layout, UI direction, or screenshot review, read `references/overlay-research.md`.
6. Inspect git status before editing because this repo may accumulate ongoing local changes.
7. For any overlay functionality question or behavior change, inspect the app-owned contracts first: `tools/validation/overlay-scenario-contract.json`, `src/TmrOverlay.Core/Overlays/OverlayBehaviorDescriptorCatalog.cs`, `src/TmrOverlay.App/Overlays/BrowserSources/Assets/contracts/overlay-geometry.json`, data-contract notes/snapshots, screenshot validation profiles, overlay evidence-contract assertions, and the relevant overlay logic doc. Summarize the current contract before proposing behavior, and update matching contracts/evidence with code changes.
8. Treat fixtures as contracts: assert visible data, assert absent data, and isolate waiting/unavailable/error states from local user history or cached telemetry unless the scenario explicitly tests those paths.
9. Before designing or changing telemetry-backed overlay behavior, consult the compact corpora under `fixtures/telemetry-analysis/` as pre-feature evidence. Start by comparing current local raw-capture `telemetry-schema.json` outputs with the tracked SDK availability corpus via `python3 tools/analysis/check_sdk_schema_against_corpus.py` when captures are available. Prefer known captured SDK fields, session states, and observed source behavior over guessed availability. If the existing corpus does not cover the feature or edge case, add a compact redacted corpus expansion or an explicit missing-target note instead of inventing fallback semantics from theory alone. If the schema check reports SDK fields or declared shapes that are new to the tracked corpus, update `sdk-field-availability-corpus.json`/`.md` or record the gap so new iRacing features remain visible for product planning.
10. Before changing a live overlay after capture analysis, first write down what is already proven or already fixed in current `main`. For each proposed feature or fix, state the behavior delta from current behavior before implementation: what changes, what remains unchanged, and whether the delta is a bug fix, an additive option, or a product-breaking change that needs explicit approval. Avoid broad rewrites when a narrow leak path is enough. In particular, do not re-gate Relative through Radar locality, do not loosen Radar's local-in-car contract, do not reintroduce lap-distance text as a gap fallback, do not redo clutch input ingestion when `ClutchRaw` fallback is already present, and do not rebuild track-map generation when the symptom is marker filtering or presentation.
11. Keep the model boundary explicit: shared live models should expose normalized facts, and overlay-specific adapters/views should do overlay-specific filtering, labelling, ordering, or degraded-state presentation. If an overlay borrows another overlay's context rules, treat that as suspect unless the product intent is explicitly shared.
12. For settings UI, overlay layout, browser-source sizing, screenshot evidence, or parity work, start from shared contracts instead of legacy renderer-local numbers. Geometry, sizing, typography metrics that affect fitting, control hit areas, crop bounds, default overlay sizes, table/grid columns, row/header heights, graph/canvas bounds, and manifest/diagnostic evidence belong in `overlay-geometry.json` or another explicit shared contract. Native consumers should use generated C# constants, browser/localhost should use contract JSON or generated CSS variables, and validators should catch stale generated output or unknown CSS/evidence fields.
13. Keep paint-only details local only when they are genuinely decorative and unmeasured. Once a value affects bounds, text fit, screenshots, manifests, diagnostics, or CI semantic comparison, treat it as contract-owned even if it looks visual.
14. When implementation behavior, calculations, defaults, source labels, fixture data, or validation semantics change, update the affected build test assertions and test fixtures in the same pass. Treat stale passing or failing assertions as stale references, not as a separate cleanup task.
15. When changing overlay behavior, surface support, sizing/scale, content-gating, chrome/no-data policy, replay fixtures, localhost/OBS routing, native support, or validation evidence, update `tools/validation/overlay-scenario-contract.json` in the same pass. Treat it as the parseable scenario coverage tracker: `covered` scenarios need durable evidence references, while `partial` and `missing` scenarios need explicit gaps or intended assertions.
16. Before branch-complete handoff, use `skills/tmr-overlay-validation/SKILL.md` to make docs/screenshots current, inspect branch commits, sanitize the first commit or planned squash text, update `VERSION.md`, align build version metadata, and tag only the release point.
17. If you change product direction, validation assumptions, capture format, or analysis assumptions, make sure the relevant reference/docs file is updated during the branch-complete sweep so future sessions inherit the new context. Raw capture format changes still need same-pass docs per `AGENTS.md`.

## Primary Files

- `src/TmrOverlay.App/Program.cs`
- `src/TmrOverlay.App/Shell/NotifyIconApplicationContext.cs`
- `src/TmrOverlay.App/Overlays/SettingsPanel/SettingsOverlayForm.cs`
- `src/TmrOverlay.App/Overlays/Status/StatusOverlayForm.cs`
- `src/TmrOverlay.App/Overlays/FuelCalculator/`
- `src/TmrOverlay.App/Overlays/CarRadar/`
- `src/TmrOverlay.App/Overlays/GapToLeader/`
- `src/TmrOverlay.App/Overlays/BrowserSources/Assets/contracts/overlay-geometry.json`
- `src/TmrOverlay.App/Overlays/OverlayGeometryContracts.cs`
- `src/TmrOverlay.App/Overlays/OverlayGeometryContractValues.g.cs`
- `src/TmrOverlay.App/Telemetry/`
- `tools/validation/overlay-scenario-contract.json`
- `tools/generate_overlay_geometry_constants.py`
- `tools/browser-review/render-screenshots.mjs`
- `local-mac/TmrOverlayMac/Sources/TmrOverlayMac/Preview/OverlayScreenshotGenerator.swift`
- `tools/validate_overlay_screenshots.py`
- `mocks/README.md`
- `docs/overlay-logic.md`
- `docs/capture-format.md`
- `telemetry.md`
- `README.md`

## Intent

The current goal is a dependable Windows iRacing companion with a small customizable overlay suite:

- the app is alive
- iRacing is connected
- live session data is actually being captured
- fuel/radar/class-gap overlays consume normalized live state
- users can manage overlay visibility, scale, sessions, font, units, and basic overlay display options
- raw capture remains an opt-in diagnostic/development mode

The next major milestone is hardening the live overlay suite, settings/customization surface, diagnostics/performance visibility, and update-notification path enough for a v1.0 production pass while using browser review for mock-telemetry and screenshot iteration.
