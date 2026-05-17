# Live Model Groundwork

The live telemetry boundary now has two layers:

1. Existing overlay-specific slices: fuel, proximity, and leader gap.
2. Shared completed models under `LiveTelemetrySnapshot.Models`, now used directly by active native, localhost, and browser overlay runtimes.

The shared models are intended to support simple telemetry-first overlays such as standings, relative, local in-car radar, flags, weather, timing tables, and future map surfaces, plus deeper analysis products, without letting each overlay rediscover the same race state in its form code.

## Compatibility

`LiveTelemetrySnapshot.Models` is live-only and defaulted to `LiveRaceModels.Empty`.

This does not change:

- raw capture files
- compact history summaries or aggregates
- post-race analysis JSON
- existing capture synthesis artifacts
- existing overlay-specific snapshot properties

Active overlay runtimes treat completed live models as the contract. `LiveTelemetrySnapshot.CompleteModels()` is the compatibility bridge for older or partially populated live snapshots: it derives missing model families from the normalized `HistoricalTelemetrySample` once, then native, localhost, and browser overlay consumers read the completed model graph. Browser overlay pages fetch `/api/overlay-model/{id}` only; the legacy localhost `/api/snapshot` endpoint remains a diagnostic/compatibility route and is not part of the overlay page runtime. Gap/relative timing code treats zero placeholder F2/estimated-time arrays as unavailable until positive timing evidence appears.

Older data collected on Windows remains readable because the new models can be derived from the normalized `HistoricalTelemetrySample` already produced by the collector. The builder tolerates missing timing, driver, weather, pit, and proximity fields by marking the affected model as `Unavailable` or `Partial` instead of throwing. Direct `LatestSample` reads should stay in collector, diagnostics, compatibility, and test code; product overlay rendering should use completed models.

Overlay availability is now a shared Core contract through `OverlayAvailabilityEvaluator`: disconnected, waiting-for-telemetry, stale, fresh/live, and session-kind classification are derived once from the promoted live snapshot. Native overlays, localhost routes, Garage Cover detection, and the overlay manager consume that shared language instead of each feature inventing its own freshness window.

Status / Diagnostics V2 is the app-health companion to the live overlay model. `AppDiagnosticsStatusModel` converts `TelemetryCaptureStatusSnapshot` into common status, detail, capture, health, Support-tab status, session-state, and current-issue text. The Support tab and diagnostics bundles consume that model so first-run waiting, stale build warnings, raw-capture writer errors, dropped frames, and healthy live analysis follow one priority order without promoting app health as a standalone user overlay.

## Model Families

- `LiveSessionModel`: session clock, lap limits, session labels, track/car display labels, and missing live session signals.
- `LiveDriverDirectoryModel`: session-info driver identity keyed by `CarIdx`, plus player/focus references.
- `LiveReferenceModel`: normalized player/focus/reference facts, including focus identity, whether focus is the player/team car, focus timing/progress, local player pit/garage context, and evidence for timing and spatial placement. Product overlays should start from this model for reference-car identity instead of deriving their own focus/player rules from raw sample fields.
- `LiveCoverageModel`: roster, scoring-result, live-position, live-timing, live-spatial, and live-proximity row counts so consumers can distinguish full live coverage from partial iRacing-transmitted rows.
- `LiveScoringModel`: scoring rows parsed from session YAML `ResultsPositions` or race starting-grid rows, grouped by class and enriched with driver identity, grounded class labels/colors, lap totals, and best/last lap values. Race sessions hold the starting grid until official position/class-position/progress coverage becomes meaningful, then stop using grid as a stale fallback.
- `LiveTimingModel`: reusable overall/class rows with live position, class, lap progress, timing, gap, pit, and driver identity fields.
- `LiveRaceProgressModel`: shared strategy/reference race progress, leader progress, lap gaps, live race pace, and legacy race-laps-remaining estimates.
- `LiveRaceProjectionModel`: stateful rolling race-distance projection derived from clean leader/class/team lap windows. It estimates timed-race laps remaining for overall leader, reference class, and team strategy consumers while rejecting non-racing states, caution/yellow windows, pit-road team laps, obvious best-lap outliers, and rolling pace outliers.
- `LiveRelativeModel`: focus-relative proximity first, with focus/class-gap timing as a fallback for timing-table and relative-style consumers. It uses normalized reference facts and does not promote `PlayerCarIdx` to the reference when focus is unavailable. When proximity has lap-distance placement but no direct relative seconds, relative rows may carry an inferred display-time gap from live lap-distance delta and current lap-time context; radar keeps using the stricter local-player spatial model instead.
- `LiveSpatialModel`: local-radar side occupancy, physical lap/meter placement, nearest-car, and multiclass-approach state for radar-style consumers; timing-only nearby context stays in Relative/diagnostics, and broader focus-relative placement remains an advanced branch. Spatial/radar context is intentionally overlay-specific interpretation on top of normalized facts, not the general reference model.
- `LiveWeatherModel`: live wetness, declared-wet state, temperatures, skies, current precipitation, wind, humidity, fog, pressure, solar angle, and session-info rubber state.
- `LiveFuelPitModel`: live fuel plus pit-road/service signals.
- `LiveRaceEventModel`: basic on-track, in-garage, Garage-screen-visible, lap, and driver-change context.
- `LiveInputTelemetryModel`: local speed, tire compound, gear/RPM, pedals, steering, engine-warning, electrical, temperature, and pressure signals from normalized samples.

Model rows now carry explicit `LiveSignalEvidence` for source, quality, usability, and missing reason. This evidence is not meant to dominate every overlay. For telemetry-first overlays such as standings, relative, local in-car radar, flags, session/weather, and timing tables, the normal path should render direct iRacing telemetry quietly and only expose evidence when data is stale, unavailable, modeled, or derived. Local in-car radar can stay simple by using local-player side/proximity telemetry only while the user is driving; non-local focus, teammate focus, spectator mode, and multiclass interpretation remain advanced evidence-aware radar cases. Timing rows still distinguish timing availability from spatial progress and radar placement eligibility, so a same-class F2 row can remain usable for standings/relative/gap timing while staying unavailable for map/non-local radar placement. Fuel/pit models distinguish valid fuel level, instantaneous burn diagnostics, rolling measured burn, and measured-baseline eligibility. This keeps model-v2 consumers from treating first-pass overlay heuristics as equally reliable raw facts without turning every normal telemetry row into a confidence report.

Current long-capture findings reflected in the v2 contract:

- `FuelUsePerHour` is diagnostic until smoothed or confirmed by rolling fuel-level deltas.
- `CarLeftRight` side occupancy is separate from decoded nearby-car placement.
- Leader gaps with missing leader F2 timing are partial evidence, not reliable zero-based gaps.
- Raw session flags need semantic normalization before more overlays consume them: background bits such as `serviceable` and `start hidden` should not become user-facing alerts by themselves, while global `SessionFlags` and per-car `CarIdxSessionFlags` should be preserved separately so blue/debris/yellow and driver-specific black/repair/furled states can be displayed in the right scope.
- `SessionState` is a phase within the active `SessionNum`, not the practice/qualifying/race selector. Race pre-green phases can expose a real pre-race countdown through positive `SessionTimeRemain`, but later grid/pace pre-green phases can return `-1.0`; model consumers should preserve that distinction instead of deriving countdowns from `SessionTimeTotal - SessionTime`. Race `SessionState == 3` can use `CarIdxEstTime` plus `CarIdxLapDistPct` for Relative timing fallback, including local tow cases where iRacing still displays estimated gaps; Standings keeps starting-grid rows conservative until official timing coverage is meaningful.
- IBT can enrich local-car post-race trajectory and vehicle dynamics, but raw/live capture remains the source for opponent timing, radar side state, focus, and class-gap context.

The 24-hour live-overlay review adds product semantics on top of those source findings: race-gap graphs should not pretend practice/qualifying/test timing is the same thing as race-position gap; the first radar path should stay local in-car while focus/multiclass cases collect evidence for a later advanced branch; and endurance fuel strategy needs team-stint evidence rather than stitched local scalar fuel.

## Timing Columns

`TimingColumnRegistry` defines reusable timing data metadata and formatters for table-style overlays. Windows table display settings now use `OverlayContentColumnSettings` for per-overlay content columns and widths, while timing data keys remain the shared semantic layer.

Current default columns cover:

- overall position
- class position
- car number
- driver
- car class
- gap
- interval
- last lap
- best lap
- pit state

The registry keeps timing display keys stable so table semantics can be validated without copying formatter logic. Standings and Relative now have shared content-column settings on top of those data concepts, with overlay-owned option keys so user choices stay independent. Content definitions keep compact overlay table headers separate from settings-facing labels, so settings can say `Class position` while the overlay still renders `CLS`.

## Parity Mode

Relative was the first product overlay to read the additive model-v2 live state directly through `LiveTelemetrySnapshot.Models.Relative`. Standings now uses `LiveTelemetrySnapshot.Models.Scoring` for scoring-snapshot row order and class grouping, with `LiveTelemetrySnapshot.Models.Timing` as live enrichment only. Relative, Standings, Gap To Leader, Track Map, Design V2 previews, and diagnostics share `LiveTelemetrySnapshot.Models.Reference` for focus/player/reference identity before applying overlay-specific presentation rules. Track Map still plots cars from valid live lap-distance progress, but scoring rows provide preferred marker labels and class colors when available. The simple Flags, Session / Weather, Pit Service, and Input / Car State overlays also consume `LiveTelemetrySnapshot.Models` directly. Car Radar reads `LiveTelemetrySnapshot.Models.Spatial` for the simplified local in-car radar contract. Fuel strategy now consumes `LiveFuelPitModel`, `LiveSessionModel`, `LiveRaceProgressModel`, and `LiveRaceProjectionModel` before combining those live facts with history-derived strategy inputs. Session / Weather, Flags, Standings class separators, and localhost settings also prefer projection values for timed-race lap estimates instead of trusting sentinel lap counts from test sessions. Gap To Leader now derives graph rows from `LiveTimingModel` and leader/reference lap context from `LiveRaceProgressModel`, falling back to the legacy leader-gap slice only when promoted model data is absent.

`LiveModelParityAnalyzer` compares those existing slices against equivalent values in `LiveTelemetrySnapshot.Models` for fuel/pit, proximity/relative/spatial, timing/leader-gap, weather, session, and race-event state. Track Map now consumes `LiveTelemetrySnapshot.Models.TrackMap` directly for live sector-highlight segments over schema-v2 map assets. The Windows collector records sampled parity frames and mismatch summaries through `LiveModelParityRecorder`. A separate `LiveOverlayDiagnosticsRecorder` watches the same normalized snapshots for product assumptions found during the 24-hour race and design-v2 candidate work, including non-race gap semantics, large gap scaling, local-only radar suppression and side/placement evidence, fuel source stitching, intra-lap position cadence, lap-delta channel availability, derived sector-timing coverage, and track-map sector highlight coverage.

At session finalization the app writes `live-model-parity.json`. When raw capture is active, the file is written beside the raw capture sidecars. When raw capture is not active, it is written under the logs model-parity folder. The artifact includes:

- sampled parity frames
- aggregate mismatch counts by family/key
- model-v2 coverage counts versus legacy overlay-input coverage
- `promotionReadiness`, which marks a session as a model-v2 promotion candidate only after it has enough frames, low enough mismatch rate, and enough model coverage for the legacy overlay inputs observed in that session
- post-session signal availability from `capture-synthesis.json`, `telemetry-schema.json`, and `ibt-analysis/*.json`

When `promotionReadiness.isCandidate` is true, the recorder also emits a `live_model_v2_promotion_candidate` app event. Treat that as a review signal, not an automatic migration: overlays should move to model v2 only after several candidate artifacts cover the normal session types and edge cases we care about.

This lets collected raw/IBT sessions evaluate whether model v2 matches current overlay behavior before any overlay is migrated to the new model.

`live-overlay-diagnostics.json` is not a parity artifact and should not be used as a pass/fail gate. It is a bounded evidence artifact for deciding which model-v2 behavior branches to do next.

## Deferred Overlay UI V2

Model v2 is data-contract work, not a visual architecture rewrite. Overlay UI/style v2 should render model-v2 telemetry directly by default: standings, relative, local in-car radar, flags, session/weather, and timing tables should feel like simple windows into iRacing telemetry. Source, quality, usability, and missing-reason evidence should become exception UI for stale, unavailable, modeled, or derived values. Competitor overlay analysis keeps the product shape grounded in small, dense, purpose-built overlays.

A separate overlay UI/style v2 pass should eventually standardize shared visual primitives across the WinForms overlays:

- semantic theme tokens for spacing, typography, borders, severity, table, and graph roles
- reusable header, status badge, source footer, metric row, table cell, graph panel, and empty/error/waiting-state helpers
- shared text fitting, border drawing, severity color, class-color, and stale-data styling helpers

That work should be additive first and migrate one overlay at a time with screenshot validation, keeping overlay-specific domain layout local. Shared primitives should be able to consume model-v2 source/evidence state directly instead of each overlay inventing its own live, degraded, stale, history-backed, or unavailable display language.

While model-v2 parity data is still being collected, browser review is the primary local development loop for deterministic fixtures and JavaScript behavior. Localhost screenshots are a separate OBS/browser-source route gate, and Windows screenshots remain the production WinForms evidence for native sizing, focus/topmost/click-through behavior, installer behavior, and real iRacing runtime integration. The tracked mac harness is deprecated secondary scaffolding for legacy `mocks/design-v2/` contact sheets and mock live scenes; it is not a V1 parity or release gate.
