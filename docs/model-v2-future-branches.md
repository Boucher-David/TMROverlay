# Model V2 Future Branch Notes

This file is the durable handoff record for model-v2 app theory, telemetry-source decisions, and future product branches. Read the current-state sections first. Older roadmap sections below are preserved as planning history and should not be treated as open work unless the current-state notes still call them out.

## Current State As Of 2026-05-17

The model-v2 layer is no longer just passive evidence. Core overlays are already normalized live-model consumers across Standings, Relative, local Radar, Flags, Session / Weather, Pit Service, Input / Car State, Fuel, Gap To Leader, and Track Map. Some overlays still use adapter or compatibility slices where that keeps behavior stable, but new reusable telemetry fields should land in Core/live models first, then map into overlay view models.

Current production shape:

- Windows tray app remains the production/iRacing runtime.
- Settings is the real startup/app surface and owns overlay visibility, scale/custom size, content/header/footer options, shared font/units, localhost routes, support capture, and diagnostics.
- Browser review is the primary local development and review surface. Localhost routes are the OBS/browser-source surface, and native Windows remains the production behavior gate.
- Screenshot validation for overlay/settings changes must account for native Windows, browser review, and localhost coverage. Browser review and localhost are separate local artifacts; Windows artifacts prove real WinForms rendering and window behavior.
- The tracked mac harness is deprecated secondary scaffolding, not a V1 parity, screenshot, or release gate. Keep it buildable when deliberately touched, but do not let it drive product parity decisions.
- Supported V1-candidate overlay family includes Standings, Relative, Gap To Leader, Fuel Calculator, Session / Weather, Pit Service, Track Map, Stream Chat, Input / Car State, Car Radar, Flags, and Garage Cover, with local OBS/localhost routes where supported.
- Local-only overlays are intentionally context-gated: Radar and Inputs require local player in-car context; Fuel Calculator and Pit Service require local player focus plus in-car or pit context.
- Standings, Relative, Gap To Leader, and Track Map must remain usable in non-local focus/spectated contexts. Their data-source decisions should come from scoring/timing/focus arrays, not local-player-only scalar context.

Current evidence/tooling shape:

- Raw capture remains opt-in and writes `capture-manifest.json`, `telemetry-schema.json`, `telemetry.bin`, `latest-session.yaml`, and optional `session-info/` snapshots.
- Compact sidecars and tools are additive: `capture-synthesis.json`, `live-model-parity.json`, `live-overlay-diagnostics.json`, `ibt-analysis/*.json`, `tools/analysis/analyze_capture_assumptions.py`, and compact fixture corpora under `fixtures/telemetry-analysis/`.
- `live_model_v2_promotion_candidate` is still an evidence/review signal, not an automatic cutover trigger.

## Active Hot Start Notes

- 2026-05-11: Added `skills/tmr-overlay-hot-start/SKILL.md` so future sessions read this file and `VERSION.md` before implementation instead of relying on chat memory.
- 2026-05-11: Current telemetry investigation uses two uploaded v0.18.8 captures: AI multi-session Practice/Lone Qualify/Race at Oran Park and open player Practice at Spa, plus local four-hour and 24-hour endurance captures. Raw captures/zips stay local; compact redacted fixture corpus belongs under `fixtures/telemetry-analysis/`.
- 2026-05-11: Source-selection pass landed from the compact corpus: Standings keeps race starting grid until meaningful official race coverage appears, practice/qualifying/test still wait for valid laps, Relative only applies local garage/off-track suppression when the reference is local, and Gap/Relative timing require positive F2/estimated-time evidence instead of all-zero placeholders. The current settings UI rolls Test visibility into the Practice column even though the model can still distinguish the session kind.
- 2026-05-11: AI race class names can have blank `CarClassShortName`; the accepted grounded fallback derives names from session-info car names/paths when possible, such as common `GT3`/`GT4`/`TCR` tokens or a single-car class screen name.
- 2026-05-11: Diagnostics for UI unclickable/freeze reports point to visible topmost overlay windows with `inputTransparent=false` and `noActivate=true`; these can intercept mouse input over Settings without taking focus. Patched Settings-visible protection so managed overlays become click-through/non-topmost while Settings is open, widened the diagnostics risk metric, gated Radar settings preview by its Visible toggle, and flushed pending settings saves before app exit to reduce restart/toggle mismatch risk.
- 2026-05-12: v0.18.10 tagged after overlay z-order/input diagnostics hardening. The compact live telemetry corpus now includes 12 tracked states across AI multi-session, open-player practice, four-hour endurance, and 24-hour endurance captures, including four-hour pre-countdown/pre-grid race-start phases plus normal race-running and pit/service contexts. Remaining corpus gaps are AI race green with player focus and a degraded missing-focus state.
- 2026-05-12: Added a redacted SDK field availability corpus, now 340 fields after preserving May 2026 NASCAR and PCup schema evidence alongside local four-hour and 24-hour endurance captures. It includes SDK-declared array/storage maximums, primitive type bounds, sampled observed ranges, and identity shape counts. New telemetry-backed work should first run `tools/analysis/check_sdk_schema_against_corpus.py` against available local raw-capture schemas so newly exposed iRacing SDK fields become corpus/product-planning inputs instead of invisible drift.
- 2026-05-12: Race-start captures confirmed that active race `SessionNum` plus `SessionState` separates early pre-grid/countdown, gridding/pace, and green phases. Standings now stays on grounded starting-grid/scoring rows before green and dims rows for cars that have not taken the grid, while Relative can use observed estimated timing/lap-distance signals during race `SessionState == 3`, including tow/pit cases. Settings stays visible after Alt+Tab/focus loss and drops out of the topmost layer instead of auto-hiding; update installs preserve existing app data.
- 2026-05-13: Tire compound context should live in Core live models before overlay use. `DriverInfo.DriverTires[]` maps SDK tire-compound indices to labels such as dry/hard/wet, while `CarIdxTireCompound` exposes each transmitted car's current compound. Future Pit Service work should also mine local tire-service counters and requests, especially `LFTiresUsed`/`RFTiresUsed`/`LRTiresUsed`/`RRTiresUsed`, left/right/front/rear set counters, `TireSetsAvailable`, `PitSvTireCompound`, `PitSvLFP/RFP/LRP/RRP`, and `dpLFTireChange`/`dpRFTireChange`/`dpLRTireChange`/`dpRRTireChange`; NASCAR tire-set-limit races make these first-class Pit Service overlay context rather than only diagnostics. Treat visible tire remaining as proven only when `PlayerCarDryTireSetLimit` is positive; PCup open-practice captures had a zero dry-tire limit but non-zero available counters, so those values should stay diagnostic/service-menu evidence rather than visible remaining inventory. Display a shared remaining count rather than per-corner buckets unless future captures prove the inventory is actually split by corner, side, or axle.
- 2026-05-13: After Gap To Leader native/localhost parity work is saved, the recommended next overlay pass is Pit Service. It should use the newly normalized tire-compound model plus the SDK/service fields above to make tire set limits, selected tires, changed tires, fuel/repair state, and pit-service release context clearer without mixing read-only telemetry with command-capable pit controls.
- 2026-05-13: Pit Service first-pass parity should show fuel request as Requested/Selected only. Do not add a production Pit Service estimated/recommended fuel field yet; Fuel Calculator v2 should own a future pit-service fuel recommendation model, which Pit Service can surface later after that source exists.
- 2026-05-13: Hide Pit Service in-car setup rows for the first pass. ARB/wing-style pit-request telemetry probably exists somewhere in the SDK, but it needs a deliberate raw capture where the driver changes those values before it becomes production overlay content. The May 2026 NASCAR capture proved `dpWeightJackerLeft`/`dpWeightJackerRight`, but those are NASCAR wedge/weight-jacker pit request channels, not ARB/wing, and should stay documented evidence until a separate product pass decides whether they belong anywhere. Track this as a V1.x Pit Service v2 investigation, not as browser spoof data or unsupported native rows.
- 2026-05-14: Fuel Calculator first-pass parity should keep shared/Core fuel work to the low-risk `LiveFuelStrategyModel` contract that centralizes local-context gating, history lookup, and existing `FuelStrategyCalculator` output for native and localhost consumers. Do not pull rolling measured-burn windows, pit-service refuel recommendations, or team-stint intelligence into this branch; those belong to Fuel Calculator v2. Neutral fuel facts such as Race, Remain, Laps, Target, Tank, Stints, and Stops should use data-presentation tones, not magenta/modelled warning-looking tones.
- 2026-05-14: Stream Chat first-pass parity should keep the practical V1 enhancements only: Design V2 shell, fixed-height chat history, wrapping rows, author color, visible badges, inline Twitch emotes, native/localhost parity, and browser review replay coverage. The richer Twitch IRC/EventSub and Streamlabs API/widget analysis is parked in `docs/stream-v2.md` as a dedicated V1.x Stream Chat V2 branch/tag candidate, not as required V1 branch-complete scope.
- 2026-05-15: `fixtures/data-contracts/v0.19.0/` is now the first release snapshot for durable user-data validation. Future durable schema branches should keep the previous release snapshot loading through current code, add a new snapshot when the persisted contract changes, and update `docs/data-contracts.md` plus history/settings compatibility tests in the same pass.
- 2026-05-15: The model-completeness branch makes completed `LiveTelemetrySnapshot.Models` the active overlay runtime contract for native, localhost, and browser surfaces. `LatestSample` remains collector/diagnostic/compatibility evidence, and `LiveTelemetrySnapshot.CompleteModels()` is the bridge for older or partial live snapshots before model builders/renderers run.
- 2026-05-17: v0.20.0 is tagged as the model-completeness runtime-contract baseline. The active `v0.20.1-windows-fixes` branch is a patch hardening branch in the same product shape: preserve completed live models as the overlay runtime contract, fix native Windows/browser review/localhost parity issues, keep localhost separate from future Overlay Bridge work, and avoid durable schema changes unless a Windows-tested compatibility issue requires them.
- 2026-05-17: Version hygiene should stay explicit. `VERSION.md` owns current branch target and branch-complete release text, `Directory.Build.props` version metadata moves only when a branch is deliberately promoted as the next product build, and annotated tags should be created only after the release commit is on `main` or explicitly designated as the release point.

## Current v0.20.1 Branch Focus

The current branch is `v0.20.1-windows-fixes`, a patch hardening branch after the tagged v0.20.0 model-completeness release. Treat v0.20.0 as the current runtime-contract baseline and keep this branch focused on Windows/native, browser review, and localhost parity fixes unless the main thread deliberately widens the milestone.

Current branch focus:

- Preserve completed `LiveTelemetrySnapshot.Models` as the active overlay runtime contract across native Windows, localhost, and browser review surfaces.
- Keep browser and localhost overlay pages on `/api/overlay-model/{id}` only; keep `/api/snapshot` as a diagnostic/compatibility route, not an overlay page dependency.
- Use browser review as the primary local development loop, localhost screenshots for OBS route coverage, and Windows artifacts for native WinForms rendering plus focus/topmost/click-through/iRacing behavior.
- Keep the v0.19.0 data-contract snapshot as the previous durable-release baseline unless a durable schema change is deliberately introduced. Any schema change needs the workflow in `docs/data-contracts.md`.
- If overlay/settings/renderer/browser/localhost behavior changes, update screenshot generators and validation profiles in the same pass so native Windows, browser review, and localhost coverage are represented.
- Keep the deprecated mac harness out of the patch parity/release gate.
- Keep the V1-candidate overlay logic and product scope stable while this patch branch is in flight.

Ongoing telemetry/model-v2 guardrails from v0.19.0 and v0.20.0 still apply:

- Keep the compact tracked "full-picture" live telemetry fixture corpus current as representative real states are collected. Keep it redacted and compact: no raw `telemetry.bin`, no source `.ibt`, no private chat/settings values, and no full-session payloads.
- Keep the SDK field availability corpus current with local capture schemas before telemetry-backed feature work. If iRacing adds SDK fields or changes declared shape, update the corpus or document the gap before deciding overlay behavior from guesses.
- Include labeled states for pre-grid/gridding, green start, green plus delay, normal race running, spectating another car, local player in-car, pit road/stall/service, garage/setup visible, replay if observed, multiclass coverage, and degraded/missing focus or official-position fields.
- Use the corpus to validate AI/spectated Standings, Relative, and Gap To Leader behavior after implementation changes instead of relying on hypothetical fallback data.
- Treat browser validation for Standings, Relative, and Gap To Leader as a native/localhost parity check: browser models should come from production-shaped telemetry contracts and settings, not from hand-authored rows or fake graph fields.
- Preserve the current valid-lap gate for Standings in practice/qualifying/test; the UI should keep Test controlled by the visible Practice setting.
- Keep broad validation, screenshot regeneration, installer/update polish, first-run docs, privacy/defaults review, and Windows-native behavior checks as V1-candidate readiness work. For docs-only cleanup, prefer lightweight formatting/stale-reference checks instead of broad test or screenshot sweeps.

Current source-selection guardrails:

- Standings, Relative, Gap To Leader, and Track Map source behavior should stay grounded in diagnostics, fixture corpus states, and SDK field availability. Do not add a rendering fallback unless the source field is observed and meaningful in the relevant session state.
- Do not fall back to hypothetical data just to render something. Rendering should be grounded in fields that are actually present and meaningful for that session state.
- Treat field location as session-dependent: AI/spectated race states may expose usable scoring/timing/focus arrays even when local-player scalar context is absent or misleading.
- Preserve player-only session behavior while adding AI/spectated support. The same source-selection rules must account for local-player focus, non-player camera focus, team handoff, pit/service, and early-green degraded timing.
- Race gaps need positive/meaningful timing evidence. A green session with all-zero placeholder `CarIdxF2Time` should ignore F2 for Standings gap math. Relative and Gap To Leader can use captured, plausible `CarIdxEstTime` plus wrap-aware lap-distance evidence when the telemetry relationship is coherent. If official positions and estimated timing are both missing or implausible, those overlays should remain waiting/degraded.
- Race-start UI should distinguish scoring/order from timing. Starting-grid rows can render before green, but Standings gap/interval values need positive timing evidence; Relative may use estimated timing only where captured telemetry shows iRacing exposes usable focus-relative placement.

## SessionState Overlay Follow-Up Sweep

The race-start investigation mostly changed Standings and Relative, but the same `SessionNum`/`SessionState` learning should guide future overlay work:

| Overlay | Current impact from race-start learning | Future investigation |
| --- | --- | --- |
| Standings | Directly changed. Race `SessionState` is treated as a phase inside the active race `SessionNum`; grid/scoring rows can render before green, cars not yet on the grid are dimmed, and `GAP`/`INT` cells wait for positive timing evidence. | Capture more `SessionState = 2` and early `4` examples across race types and classes to refine when official position/class-position coverage is trustworthy. |
| Relative | Directly changed. Race `SessionState = 3` may use observed `CarIdxEstTime` plus valid lap-distance signals for focus-relative timing, including tow/pit edge cases. | Validate `SessionState = 2`, mid-race tow, pace/caution, and multiclass estimated-gap quality before broadening estimated timing further. |
| Gap To Leader | Directly affected. It should reject placeholder/all-zero race `CarIdxF2Time`, use usable F2 when it becomes meaningful, and allow plausible wrap-aware `CarIdxEstTime` timing after green so early race gaps do not disappear solely because the field has not crossed start/finish. | Keep validating early-green, first-lap, lap-2, tow, and multiclass captures to make sure estimated timing is only accepted when the telemetry relationship is coherent. |
| Fuel Calculator | Affected through race progress. Positive race pre-green `SessionTimeRemain` is a grid countdown, not race time remaining, so strategy skips that live-clock branch until the race is running while still using live fuel level/burn evidence. | Validate timed-race projections around gridding, green, cautions, red/checkered states, and endurance team handoff. When a car hits the grid and the engine is on, it can consume fuel during `SessionState` `2`, `3`, and early `4`, so future checks should verify burn/current-fuel handling separately from remaining-race-time handling. |
| Track Map | Indirectly affected. Lap-distance/progress can show cars before green, but grid/tow state changes whether a marker represents gridded, towed, or genuinely racing movement. | Investigate pre-green map markers for ungridded cars, gridded cars, pit/towed local cars, and formation-lap motion. |
| Flags | Likely relevant as user context. Flags, session name, and future header content can explain pre-green versus green without labeling timing rows as estimated. | Build corpus coverage for flags across states `1`, `2`, `3`, `4`, countdown expiry, all-cars-gridded auto-advance, pace/yellow, and caution starts. |
| Session / Weather | Header time remaining now uses `MM:SS` for race pre-green countdowns and `HH:MM` for normal session remaining time. | Validate session labels/countdowns across practice, qualifying, race transitions, and `SessionTimeRemain = -1` gaps. |
| Pit Service | Tow/pit during grid is local context, but should not be confused with a normal race pit stop. | Validate tow-to-pits, pit stall, and service/tire/fuel flags during pre-green and early green. |
| Input / Car State | Graph bounds were fixed; `SessionState` is mostly context for whether inputs are racing, gridding, towing, or stationary. | Capture inputs while gridding, towing, pit-stalled, and replaying to distinguish active driver control from stationary/towed states. |
| Car Radar | Should stay local in-car. Pre-green grid proximity can be close but not necessarily a threat. | Decide whether radar should suppress or soften threat styling before green, especially during gridding and pace-line states. |
| Stream Chat | No telemetry impact. | No `SessionState` work needed beyond preserving user settings through updates. |
| Garage Cover | No direct race-start impact, but tow/garage distinctions matter for streamer privacy. | Validate Garage/setup-screen visibility during tow-to-pits and grid sessions so the cover does not flash incorrectly. |

V1-candidate readiness discussion moved out of `VERSION.md`:

- Treat the fundamental overlay logic as ready for V1-candidate validation once fixture-backed source selection lands. Overlay behavior should then be stable enough that adding a straightforward content field, such as a Standings `Team name` column, is a descriptor/model wiring change instead of a table-behavior rewrite.
- New reusable telemetry fields should be consumed and normalized in Core/live models first, then mapped into overlay columns or rows; Standings/Relative should not own root data extraction for shared fields.
- Lock the V1 product scope: decide the final overlay list, make sure experimental/future surfaces are not exposed as normal user-facing tabs, keep browser review dev-only, and keep the deprecated mac harness out of the V1 parity/release gate.
- Prove installer/update polish: MSI install, upgrade, rollback, Velopack update checks, release notes, checksums, and the acceptable stance on unsigned SmartScreen warnings for V1.
- Add the minimum user-facing first-run docs: starting the app, enabling overlays, configuring OBS localhost URLs, Stream Chat setup, Garage Cover setup, diagnostics bundle creation, and raw capture being opt-in.
- Complete an explicit privacy/defaults pass: logged fields, diagnostics bundle contents, redactions, retention defaults, app-data locations, and confirmation that raw `telemetry.bin` and source `.ibt` payloads stay out of support bundles.
- Freeze durable settings/history schema unless a V1-blocking bug requires a change. Any schema change now needs version constants, migrations or compatible readers, docs, versioned data-contract snapshots, and compatibility tests in the same pass.
- Run a native Windows behavior sweep because browser review cannot prove focus, topmost, click-through, no-activate behavior, Stream Chat window behavior, iRacing SDK capture, installer/update behavior, or WinForms screenshot output.
- Harden the support posture so one teammate diagnostics bundle is enough to answer version, settings, update state, overlay visibility, localhost routes, runtime errors, recent telemetry state, and recent performance/freeze state without raw payloads.
- Keep V1.x performance and heavier analysis work out of this milestone unless validation finds a release-blocking regression. Use the V1.x roadmap for overlay lifecycle/timer efficiency, rendering/cache performance, capture replay, and larger post-race analysis products after the candidate is stable.
- Keep release hygiene boring and explicit: refresh `VERSION.md` from the actual branch diff before branch-complete handoff, align `Directory.Build.props` only when the branch is promoted as a product build, and create annotated tags only after merge or an explicit release-point decision.

## Historical Branch Scope

- Keep raw capture and advanced diagnostics bounded with failure isolation. For tester builds, raw capture should stay opt-in; compact model parity, live overlay diagnostics, and edge-case clips can run by default when a branch is explicitly collecting evidence.
- Preserve compatibility with already collected raw captures and synthesized data. New sidecars are additive; older captures remain readable.
- Record enough evidence to decide future overlay behavior from data:
  - `live-model-parity.json` for model-v2 coverage/mismatch and promotion-readiness.
  - `live-overlay-diagnostics.json` for gap/radar/fuel/position-cadence/lap-delta/sector-timing assumptions that came from the 24-hour race and design-v2 candidates.
  - `ibt-analysis/ibt-local-car-summary.json` for local-car trajectory, fuel, and vehicle-dynamics readiness.
  - `tools/analysis/analyze_capture_assumptions.py` for offline raw-capture checks, including sampled intra-lap position/class-position changes.
- Use the existing `live_model_v2_promotion_candidate` app event as the first "enough evidence to review cutover" signal. It is not an automatic migration trigger.

## Product Milestone Framing

V1.0 should be a polished core desktop overlay app, not the finish line for every endurance-analysis idea. The V1.0-candidate surface is now mostly product hardening around the current overlay suite rather than new model-v2 surface area:

- Settings as the real app entry point, with support/diagnostics and release/update awareness.
- Standings.
- Relative.
- Local in-car radar/blindspot.
- Flags.
- Gap To Leader, Fuel Calculator, Session / Weather, Pit Service, Track Map, Stream Chat, Input / Car State, and Garage Cover where current contracts remain telemetry-first, low-noise, and context-gated.
- Localhost routes as local OBS/capture support, not as the teammate-to-teammate Overlay Bridge.

Heavy analysis products should move to V1.x, where they can build on stable core telemetry contracts and teammate evidence without blocking the first product milestone. Scenario-based layout profiles and engineer/operator mode are large enough to be V2.0 product modes, and VR is a later V2.N platform item because it is a separate renderer with different performance, UX, and comfort constraints.

Remaining model-v2/future-branch candidates should be treated as V1.N foundations unless teammate testing proves one must move earlier: Driver Role / Focus V2, Race Events / Penalties V2, Pit / Service Strategy V2, Track Asset / Map Quality V2, Post-Race / Session Summary V2, player comparison/stat tracking, and broader analysis work. They are good model contracts, but each needs either broader product design, more telemetry proof, official/user-authorized data access, or replay-backed validation before becoming default V1.0 surface area.

## Historical V0.X Roadmap Snapshot

The v0.11 through v0.18 plans below are preserved as planning history. Many items have shipped or changed shape; use `VERSION.md` for authoritative milestone summaries and use the current-state section above for what is true now.

### v0.11.0 - Standings, Track Maps, Localhost Browser Sources, And Live Polish

Goal: close the pulled-forward user-visible overlay branch before the next teammate-beta hardening pass.

Likely scope:

- Ship first-pass production Standings backed by normalized timing rows.
- Ship a map-only Track Map overlay with bundled-map lookup, optional user IBT-derived map generation, circle fallback, live car dots, and model-v2 sector highlights.
- Add `LocalhostOverlays` routes for OBS/local capture tools, separate from the future teammate-to-teammate Overlay Bridge.
- Add Stream Chat as a normal read-only overlay for saved public Twitch channel chat plus a localhost route for one selected saved source: Streamlabs Chat Box widget URL or public Twitch channel chat.
- Keep settings as a flat-tab app control surface with selectable/copyable localhost URLs where routes exist.
- Harden existing live overlays from tester feedback: Relative/Fuel repaint churn, Relative display-time fallback, smaller Inputs layout, clearer local Radar side warnings, and stale race-data overlay fade behavior.
- Keep Windows build/test/publish and Windows-rendered screenshot validation CI-owned when local macOS validation cannot run `dotnet`.

### v0.12.0 - Teammate Beta Hardening

Goal: make the first shared builds easier to install, understand, and support.

Likely scope:

- Add visible in-app version/build metadata in Settings or Support.
- Add startup and manual update checks that compare the running version against Velopack-compatible release metadata where possible, falling back to a release manifest/GitHub Release lookup while the portable zip channel remains active. Avoid modal prompts during active sessions.
- Tighten the Support tab from real teammate feedback: clearer current issue text, diagnostics bundle status, copied-path behavior, diagnostic-capture guidance, concrete test bullets, and detailed handoff copy.
- Validate portable upgrade behavior against existing `%LOCALAPPDATA%\TmrOverlay` settings/history/diagnostics data.
- Polish first-run and no-iRacing-connected states so testers do not confuse expected waiting states with broken installs.
- Keep signed installer/update automation out of scope unless release friction proves the portable zip is not enough for private testers.

### v0.13.0 - Core Overlay Readiness

Goal: make the normalized model layer reliable enough for V1.0 by proving it through the core overlay consumers in the same branch.

Likely scope:

- Promote row identity, class labels, flag categories, local radar state, freshness, unavailable reasons, and basic timing into stable model-v2 contracts.
- Make mid-session joins explicit: local observation start, session clock, roster availability, current timing rows, best/last lap availability, and missing local history.
- Treat iRacing car-count/transmitted-row limits as first-class completeness signals rather than silently hiding missing competitors.
- Expose roster count, live row count, rows with timing, rows with spatial progress, and missing-row reasons.
- Harden production Standings around the stable timing contract: partial coverage, class labels/colors, pit labels, focus behavior, and larger-field edge cases.
- Harden Relative around stable model-v2 rows, user-centered context, class color, pit labels, and partial coverage.
- Harden local in-car Radar/Blindspot around player-only telemetry, clear unavailable states, and no spectator/team-analysis promises.
- Harden Flags around normalized flag categories and simple display behavior.
- Keep session/weather and pit-service as optional simple snapshots, not analysis products.
- Add shared overlay availability/freshness and status/diagnostics health models so V1.0 overlays use consistent waiting, stale, unavailable, session, and app-health language.
- Add passive once-per-startup/manual update checks in this branch if v0.12 teammate feedback shows release-discovery friction; keep prompts out of active sessions.
- Keep legacy live slices stable until no production core overlay depends on them.
- Use browser review screenshots and Windows screenshot artifacts to catch visual regressions.

### v0.14.0 - UI Polish And V1 Candidate Prep

Goal: make the core overlays/settings surface easier to review, maintain, and hand to a designer while tightening the remaining V1.0 release-candidate risks.

Likely scope:

- Promote shared visual primitives: overlay headers, quiet status badges, timing rows, relative rows, metric rows, flag strips, compact weather widgets, borders, state tones, and text-fitting rules.
- Keep typography, row heights, spacing, opacity defaults, and semantic colors in shared tokens instead of scattered form-local constants.
- Use browser review for fast style iteration, then verify stable primitives against Windows/native.
- Keep normal telemetry-first overlays quiet; only surface source/evidence chrome for stale, unavailable, modeled, or derived values.
- Refresh browser review screenshot artifacts and compare Windows CI artifacts before calling the branch done.
- Validate install, upgrade, support, diagnostics, and release/update handoff with real teammates.
- Verify AppData compatibility for settings, history, logs, diagnostics, runtime state, and optional captures.
- Tighten performance, startup behavior, and settings-window responsiveness for the core overlay set.

### v0.15.0 - Settings Layout And V1 UI Polish

Goal: make the settings app layout V1-ready as its own product pass instead of widening the v0.14 release-candidate cleanup branch.

Likely scope:

- Keep the shared per-overlay General/Content/Header/Footer settings behavior and make the surrounding settings surface clearer, easier to scan, and easier to extend.
- Revisit the left-tab structure, overlay option grouping, support/status grouping, localhost details, and shared preferences without exposing development-only surfaces as ordinary overlay tabs.
- Preserve app-owned scale controls and header/footer slot-fitting assumptions while improving how crowded overlay option sets are presented.
- Keep the Support tab as the product home for app-health, version/build, diagnostic capture, diagnostics bundles, and support folders.
- Refresh browser review screenshots and compare Windows CI artifacts before calling the branch done.

### v0.16.0 - Release Channel And V1 Candidate Escape Hatch

Goal: make Velopack the canonical installer/update channel using public GitHub Releases as the feed, while preserving the portable zip as a transitional fallback.

Likely scope:

- Add Velopack startup integration and CI `vpk pack` validation.
- Publish Velopack MSI/full/delta/feed assets to public GitHub Releases on release tags.
- Add passive startup/manual update checks for installed Velopack builds without embedding a GitHub token in the client.
- Show update available/failure state in the tray menu, settings banner, Support tab, and diagnostics bundle.
- Add explicit user-initiated download/install and restart-to-apply controls once the installer path is ready for teammate testing.
- Keep deep fuel/strategy/engineer/advanced-track-map/streaming/builder work out of the V1.0 release candidate unless it is hidden development tooling.

## Suggested V1.X Roadmap

V1.x is where heavy analysis overlays and broader platform features should mature, except for the engineer/operator mode, which is large enough to treat as V2.0. These branches can assume the V1.0 core app already has reliable release/support flow and stable core telemetry contracts.

### Important V1.x Foundation - Overlay Lifecycle And Timer Efficiency

Goal: make overlay refresh work proportional to what is actually visible or actively needed, without weakening telemetry freshness for overlays that are on screen.

v0.16 adds the diagnostic prep needed to measure this before changing behavior. Performance JSONL snapshots now record overlay timer ticks and active timer counts by cadence, visible versus pause-eligible timer samples, lifecycle visibility/session/fade states, lifecycle transitions, unchanged-sequence skips, explicit paint samples, localhost idle/request activity, and process GDI/USER handle pressure.

Likely scope:

- Add a shared overlay lifecycle contract so native overlays can pause high-frequency refresh timers while hidden, session-filtered out, or fully faded, then resume cleanly when shown again.
- Keep lifecycle handling centralized in shared overlay base/helper code instead of adding one-off timer checks to every overlay form.
- Preserve safety-critical refresh behavior for Radar, Track Map, Inputs, Flags, and live timing overlays when they are visible; this work is about hidden/disabled churn, not making active overlays stale.
- Extend performance diagnostics to distinguish visible refreshes, hidden skipped refreshes, fade-only work, and settings-driven lifecycle transitions.
- Validate with screenshot parity, overlay performance counters, and teammate long-session usage before using the pattern for heavier analysis or broadcast overlays.

Detailed performance backlog:

1. Overlay lifecycle: pause native overlay timers and model reads when an overlay is hidden, session-filtered out, or fully faded; resume without stale first-frame behavior when visible again.
2. Shared timer scheduler: replace scattered independent WinForms timers with shared cadence buckets, such as 50 ms, 100 ms, 250 ms, 500 ms, and 1000 ms subscriptions.
3. Sequence-aware refresh skipping: skip row rebuilds, label updates, layout, and paints when the underlying normalized snapshot sequence has not changed, with explicit exceptions for visible animation or local-driver safety views.
4. Paint and GDI allocation cleanup: cache stable `Pen`, `Brush`, `Font`, and layout resources per form/theme/scale, with clear disposal when theme or scale changes.
5. Track Map rendering split: cache static map geometry/background separately from live cars, sectors, labels, and focus highlights so high-frequency updates do not redraw the full map.
6. Input graph optimization: use fixed-size/ring sample buffers, avoid full repaints when driver input is unchanged, and keep ABS/TC active-state coloring tied to proven firing signals.
7. Radar draw optimization: keep visible radar accuracy as the priority while skipping all hidden work and caching static rings, labels, brushes, and geometry helpers.
8. Localhost efficiency: keep localhost routes available, but reduce route work when no clients are connected and allow localhost polling cadences to match each overlay's freshness needs.
9. Disk write batching: queue or batch low-priority app events, local logs, diagnostics, and performance snapshots so telemetry/UI paths do not block on frequent append writes.
10. Settings save/apply debouncing: avoid saving and reapplying scale, opacity, numeric, and text settings on every intermediate control change when idle/commit behavior is enough.
11. Diagnostics cost control: keep support bundles useful while avoiding repeated JSON/directory scans from hidden settings tabs or high-frequency overlay loops.
12. Store/history read caching: add invalidation-based caches for repeated `TrackMapStore`, session history, and analysis reads instead of rescanning disk-backed data.
13. Shared font/theme cache: centralize `OverlayTheme` font/resource creation by family, size, style, theme, and scale so paint/layout paths do not recreate stable objects.
14. Layout churn reduction: use set-if-changed helpers and suspend/resume layout patterns across overlay forms, not only the settings panel, and avoid rebuilding controls when value updates are enough.
15. Performance harness: create a repeatable long-run profile for all overlays hidden, all overlays visible, static telemetry, replay/active telemetry, and browser clients connected/disconnected; capture UI-thread time, paint counts, timer counts, disk writes, memory, and GDI handle pressure.

### V1.x Feature Addition - Stream Chat V2

Goal: turn Stream Chat into a deliberately richer stream-facing overlay after the V1 parity pass, without expanding the current branch's finish line.

Dedicated branch/tag candidate: Stream Chat V2 / `stream-v2` in the V1.x line.

Design handoff: `docs/stream-v2.md`.

Live validation notes: `docs/stream-v2.md` includes a 2026-05-15 Twitch live-review pass against `techmatesracing`, including fixed-height behavior, long-row wrapping, author color, badge fallback behavior, and remaining gaps for badge images, dense metadata chips, emotes, replies, bits, raids, and resubs.

Likely scope:

- Promote the current Twitch IRC row parsing into a richer stream message/event model that can preserve raw tags plus normalized display fields.
- Treat Twitch `PRIVMSG` and `USERNOTICE` as first-class row types so replies, bits/cheers, first messages, badges, resubs, raids, and other notice events can get purpose-built presentation.
- Decide whether Twitch EventSub `channel.chat.notification` joins or replaces the IRC notice path for richer alert semantics.
- Add deterministic rich fixtures for broadcaster/partner/premium rows, moderators, first-time chatters, inline emotes, bits, resubs, raids, and replies with parent message previews.
- Keep Streamlabs as two different product paths: an opaque Chat Box widget URL mode and a future authenticated Socket API event-feed mode.
- Model Streamlabs Socket API output as stream events/alerts, not as Twitch-style chat rows, unless a real payload proves structured row-level chat data is available.
- Add Streamlabs-specific toggles for donations/tips, follows, subs/resubs/gifts, bits, raids/hosts, YouTube superchats, amounts, viewer messages, source platform, and debug event IDs.
- Keep Twitch-only toggles, such as author color, Twitch badges, Twitch emote ranges, first-message, replies, and Twitch message IDs, scoped to Twitch until Streamlabs supplies equivalent verified payloads.
- Define native/localhost badge and emote image caching, failure fallbacks, and privacy-safe diagnostics for stream payloads.

### V1.x Feature Addition - Timing Tower Overlay

Goal: add a compact race timing tower separate from the full Standings overlay.

Likely scope:

- Build a simplified vertical race tower for quick position scanning rather than a full standings table with every timing column.
- Reuse the normalized timing/scoring model where possible, but keep the overlay UX independent from Standings so it can optimize for broadcast-style density, row stability, and compact class/pit/lap indicators.
- Treat it as primarily race-facing unless testing proves value in practice or qualifying.
- Start with scoring-snapshot ordering and completed-lap position movement, matching the v0.13 decision to avoid live/proximity compression when iRacing is not transmitting the full field.
- Keep the first version conservative: position, class/overall marker, driver or car label, gap/interval summary, pit/lap-state hints, local-car emphasis, and optional class filtering.
- Use screenshot/replay validation against large multiclass fields before adding animation, streamer-specific styling, or richer broadcast controls.

### V1.x Feature Addition - Player Comparison And Stat Tracking

Goal: explore a future player comparison/stat tracking tool or overlay without pulling it into the current V1-candidate source-selection hardening.

Initial product signals:

- Team request: compare players and track stats as a future overlay/tool family.
- Reddit signal from `/r/iRacing/comments/1t9m2zy/proper_iracing_companion_app/`: users asked for progression tracking, car/track stats, buddy/rival comparison, fastest-lap counts, filtered series standings against drivers with enough weeks, SR split by off-track vs contact incidents, and race history against other drivers.
- API/privacy posture matters: avoid a scraped public driver database; prefer user-authorized iRacing API/OAuth-style access and active-membership-aware data boundaries.

### v1.1 - Analysis Evidence Loop And Capture Replay

Goal: make real race evidence easier to replay through overlays without manually driving the app.

Likely scope:

- Add a development-only raw-capture replay provider for browser review or a separate tooling path.
- Decode selected four-hour/twenty-four-hour captures into normalized live snapshots at controllable playback speed.
- Drive one browser/localhost route for each overlay from replayed snapshots, generate screenshots/contact sheets, and emit replay-side validation diagnostics.
- Keep replay isolated from the Windows runtime collector and private capture folders.
- Use replay artifacts to decide model-v2 promotions, overlay simplifications, and edge-case UI behavior.

### v1.2 - Fuel And Strategy V2

Goal: rebuild fuel strategy around team-stint evidence instead of stitched scalar estimates.

Likely scope:

- Combine local measured fuel windows, team/focus progress, completed stint lengths, pit/service history, max-fuel constraints, and source labels.
- Reject impossible stint rhythms when current-session evidence and history disagree.
- Treat tire/repair/pit-service/setup-change evidence as input to strategy but avoid command-capable pit controls in this branch.
- Treat incident-count increases as suspected-damage candidates only. Confirm later with repair timers, fast-repair counters, or pit-service evidence when available, and estimate pace loss from post-event clean laps while controlling for fuel, tire age/compound, wetness, traffic, pit-out laps, and driver. Gap To Leader can eventually show timeline markers and pace-loss context, while Fuel can consume the simplified repair/unscheduled-stop consequence.
- Keep user-facing strategy recommendations conservative until enough teammate race data supports them.

### v1.3 - Gap, Sector, And Stint Analysis

Goal: turn the deeper timing and trend ideas into analysis products after core overlays are stable.

Likely scope:

- Rework gap-to-leader and gap-to-class behavior around race/session semantics instead of forcing one graph to explain every situation.
- Add sector comparison and stint laptime analysis only after model-v2 timing contracts and replay evidence support them.
- Keep source/evidence UI available because these products derive meaning from telemetry rather than simply displaying direct values.
- Use replay and live diagnostics to validate edge cases before making advice prominent.

### v1.4 - Track Map Expansion And QA

Goal: improve the v0.11 Track Map implementation with better assets, status reporting, and map-quality workflows after the basic local generation path has real usage.

Likely scope:

- Continue bundled-map QA as more `.ibt` sources are vetted; the current committed asset set is schema v2 and sector-capable.
- Expand settings/support status around current map source, quality, last generation result, and manual rebuild/replace actions.
- Add deterministic screenshot states for placeholder, preview/low confidence, high confidence, stale markers, and pit-lane marker placement.
- Improve pit-lane-aware marker placement when live telemetry exposes a reliable pit-lane progress signal.
- Use iRacing/Data API or other official/reference map sources only as QA references unless licensing and product rules justify bundled assets.

### v1.5 - Overlay Bridge And External Clients

Goal: turn future teammate-to-teammate data sharing into a documented developer/platform boundary after the core contracts have proven themselves. Local OBS/localhost overlays are a separate feature.

Likely scope:

- Define versioned JSON contracts for live telemetry, app health, overlay metadata, selected display settings, peer/session context, and schema capabilities.
- Keep the bridge disabled by default with explicit settings/support visibility for enabled state, allowed clients, connection count, last error, and schema version.
- Use normalized `LiveTelemetrySnapshot.Models` instead of exporting overlay-local temporary calculations.
- Add deterministic bridge fixture tests and sample payloads so external clients can be developed without iRacing running.
- Explore peer/missed-history context exchange as derived session context only: provenance, session identity, observation window, roster/timing coverage, schema version, and trust labels.

### v1.6 - Streaming And Broadcast Overlays

Goal: let the app support broadcast-style surfaces without coupling chat or web/widget rendering to the Windows collector.

Likely scope:

- Expand the current Streamlabs-widget localhost source and public Twitch channel native/localhost support into richer Twitch/YouTube chat overlays as a separate stream-facing feature set.
- Keep chat auth, tokens, moderation, reconnect behavior, and rate limits isolated from iRacing telemetry.
- Keep localhost stream overlays on the local `LocalhostOverlays` path unless they intentionally need peer data.
- Add deterministic offline preview states for chat-only and mixed telemetry/chat overlays.

### v1.7 - Overlay Builder And Designer Tooling

Goal: move toward configurable layouts only after the primitives and bridge contracts are stable.

Likely scope:

- Start as a local development/designer tool for arranging simple telemetry widgets, choosing shared theme tokens, and exporting deterministic previews.
- Generate or validate layouts against readability, content/header/footer session gates, stale-state handling, screenshot validation, and performance rules.
- Treat exported layouts as development artifacts that can inform V2.0 user-facing layout profiles, not as a full in-app profile manager yet.
- Keep production overlays hand-authored until generated layouts can meet the same quality bar.
- Treat DDU-style builder research as primitive/layout work, not as a reason to collapse purpose-built overlays into one configurable mega-widget.

## Suggested V2.X Roadmap

### v2.0 - Layout Profiles And Engineer Workspace

Goal: add scenario-specific overlay layouts plus a dedicated race engineer/spotter mode after the core app and first analysis products are stable.

Likely scope:

- Add named layout profiles for scenarios such as practice, qualifying, race, endurance stint, engineer/spotter, and broadcast review.
- Let a profile own overlay visibility, monitor/position/size, scale, opacity where supported, session gates, and per-overlay settings so a qualifying layout can differ from a race layout without hand-editing every overlay.
- Start with explicit user-selected profiles, then consider session-aware suggestions or switching once model-v2 session state and role state are reliable enough.
- Version profile files and migrate them like other AppData schemas; missing monitors or changed screen resolutions should fall back to a readable default layout instead of losing the profile.
- Validate saved profiles with screenshot and performance rules so a layout cannot silently produce unreadable tables, off-screen overlays, or too many expensive views.
- Start read-only: pit service state, fuel/repair/tire choices, stint/fuel analysis, driver-control context, team-car status, and useful spectator telemetry.
- Treat this as a mode/workspace, not just another tiny overlay. It may compose several approved widgets into a generated engineer layout once overlay-builder primitives are mature enough.
- Add an explicit pit-command service design before any simulator writes.
- Validate iRacing command behavior for active-car, teammate, spectator, and driver-swap states before promising remote/team control.
- Keep command-capable controls gated, obvious, and disabled by default if they ever ship.

### v2.N - VR Renderer / Headset Client

Goal: add VR support only after the desktop app, analysis products, model-v2 contracts, overlay bridge, and release/update path are stable enough to support a separate renderer.

Likely scope:

- Build VR as a dedicated renderer/client using compact model-v2 state through Overlay Bridge or a future snapshot boundary.
- Keep the Windows tray app as the telemetry, settings, storage, diagnostics, update, and release owner.
- Start with sparse local overlays: flag status, blindspot/radar warnings, and compact relative.
- Keep dense standings tables, gap graphs, strategy grids, long diagnostics, and chat-heavy surfaces desktop-first until they have a VR-specific interaction model.
- Treat VR frame budget and comfort as hard product requirements, not style polish. Avoid disk IO, JSON parsing, history lookup, network calls, image decode, or avoidable allocations in the render loop.
- Validate rendering at headset refresh targets before exposing it to teammates.

## Supporting Topic Notes

### Fuel Strategy V2

Rebuild strategy around a team-stint model rather than stitched scalar fuel. The model should combine local measured fuel windows, team/focus progress, completed stint lengths, pit/service history, max-fuel constraints, and explicit measured/model/source labels.

Do this before trusting time-saving stint suggestions. Strategy output should reject impossible or misleading stint rhythms when current-session stints and historical evidence disagree with a shorter suggestion.

### Local In-Car Radar V2

Keep the first radar v2 path simple by wiring it entirely to the local player while the user is in the car. In that mode, `CarLeftRight` and physically placed local proximity can be treated as direct telemetry for a compact warning surface instead of a broad focus-relative analysis product.

Hide or degrade the local radar when the user is not in the car, is spectating, is in replay/garage, or when the local-player telemetry needed for side/proximity display is unavailable. Do not try to explain teammate/spectator focus in this simple overlay. The current live proximity slice follows that rule by suppressing explicit non-player focus and pit/garage contexts.

Windows Car Radar now follows this v2 path by reading `LiveTelemetrySnapshot.Models.Spatial` for local side occupancy, physically placed local cars, and multiclass warning state. Timing-only nearby cars remain available to Relative and diagnostics, but they do not draw radar targets. The legacy `LiveProximitySnapshot` remains as an internal compatibility/diagnostic slice until parity and diagnostics no longer need it.

Post-session radar calibration now has a car-scoped history scaffold for clean `CarLeftRight` side-window durations and identity-backed body-length estimates, including useful `SessionState = 3` pre-grid rows where cars are paired on the way to the grid. Live radar uses exact bundled car specifications first, trusted user calibration for unknown cars second, and low-confidence bundled estimates only as fallback. Local IBT inspection did not expose car length, width, wheelbase, or similar dimension fields, so the bundled spec catalog is keyed by `CarID`/`CarPath` and learned calibration stays conservative.

### Radar Focus And Multiclass V2

Treat non-local focus, teammate focus, spectator mode, and multiclass warning as the advanced radar branch. Move those cases to model-v2 focus-relative evidence. Keep `CarLeftRight` local-player scoped, suppress or relabel it for non-player focus, and make teammate/team-car focus states explicit.

This branch should also define what the radar displays when spatial progress is missing but timing rows are valid.

Keep the collector looking for examples instead of dropping the evidence. `live-overlay-diagnostics.json` now records local-only radar suppression, pit/garage unavailability, local progress-missing frames, raw side signals suppressed for non-player focus, side-without-placement frames, timing-only placement rows, spatial placement rows, and multiclass approach frames so real captures can drive the advanced branch.

### Gap Graph V2

Split race-position gap behavior from practice/qualifying/test timing behavior. Race sessions can use leader/class gap semantics; non-race sessions need a separate timing mode or should stay hidden by default.

Improve readability for multi-lap gaps by separating leader-gap context from local-battle deltas, using lap-aware scaling, or moving threat/leader interpretation into future relative/standings/strategy overlays.

### Position Cadence And Timing Tables

Use raw-capture and live-overlay diagnostics to confirm whether `CarIdxPosition` and `CarIdxClassPosition` update intra-lap often enough for standings/relative overlays. The same diagnostics now count live lap-delta channel availability and best-effort sector-boundary intervals derived from session-info sectors plus `CarIdxLapCompleted`/`CarIdxLapDistPct`. If confirmed across enough sessions, promote position cadence, lap-delta readiness, and sector timing into explicit model-v2 timing-table assumptions.

### Session Bootstrap And Car Coverage

Treat mid-session joins as a first-class model-v2 availability case. The app should assume raw frame history starts when the local SDK connection starts, but current session context can still exist immediately through session YAML and current `CarIdx*` rows: roster, car classes, event/session metadata, current standings/result snapshots, completed laps, best/last laps where exposed, current session clock, and current track position for transmitted cars. A future diagnostics pass should compare a known mid-race join against a full-session capture and label which fields are real current-session context versus local-observation-only history.

Treat user-configured iRacing car coverage limits as a separate model-v2 completeness signal. Session YAML can describe the entered field, while live `CarIdx*` arrays may only carry currently transmitted cars. Standings, relative, gap, and post-race analysis should not silently treat missing live rows as retired or unknown competitors. Model v2 should expose roster count, live-row count, rows with timing, rows with spatial progress, missing-row reasons, and whether each overlay is showing full-field standings, nearby transmitted cars, or a partial mixed view.

### Parked Mac Overlay Replay

The deprecated mac harness records live overlay diagnostics from mock/demo snapshots, including the four-hour preview and capture-derived radar/gap demos. This is no longer the preferred replay direction. Future capture replay work should feed browser review and localhost first, then use Windows/native validation for product behavior.

Any replay provider should be a development tool only. It should read existing captures, skip or downsample aggressively, and avoid changing the Windows collector/runtime path.

### Browser And Windows Screenshot Parity Validation

Browser review is the fast local design and screenshot surface, localhost is the OBS/browser-source route surface, and Windows is the production/iRacing runtime. The browser-review screenshot generator captures `/review/app` and `/review/overlays/<id>` into `artifacts/browser-review-screenshots`. The localhost screenshot generator captures `/overlays/<id>` into `artifacts/localhost-screenshots`. A local `--surface all` run writes the combined review artifact to `artifacts/browser-localhost-screenshots`, but CI still validates browser review and localhost as separate jobs. The Windows-only screenshot generator renders the real WinForms forms with deterministic telemetry fixtures and uploads the resulting contact sheet plus per-state PNGs as GitHub Actions artifacts.

Use browser review, localhost, and Windows artifacts together as the parity gate. Browser screenshots prove local review layout and JavaScript behavior, localhost screenshots prove OBS route coverage from the same assets, and Windows artifacts prove the production forms still render, size, arrange, and behave under the WinForms runtime. The parity set should cover settings tabs plus the current production overlays: standings, fuel calculator, relative, track map, flags, session/weather, pit service, inputs, radar, gap to leader, and stream chat, with app status validated through Support and Garage Cover validated through its localhost route.

Keep the fixtures isolated from local history, app data, raw captures, and real machine paths. If a Windows screenshot state needs live telemetry, build it from normalized `LiveTelemetrySnapshot` data with explicit fixture values. If a future overlay needs replay evidence, add that through a separate capture-replay branch rather than letting the screenshot generator read private capture directories.

Use the parity artifacts to identify which visual differences are intentional platform rendering and which should become shared tokens. The likely shared set is color roles, title/status/table font sizes, row heights, padding, border opacity, state tones, graph grid alpha, and empty/error/waiting treatment. Font parity should start with a fallback policy rather than a bundled font or user-facing font picker: native defaults to `Segoe UI`, localhost follows the resolved app theme/CSS stack, and both should keep matching sizes/weights/line heights closely enough for review. If the screenshots still drift after token cleanup, evaluate bundling an OFL font such as Inter as a separate asset/licensing change.

### IBT-Derived Track Map Store

v0.11 pulled the first reusable derived-map path into the product branch. `TelemetryCaptureHostedService` can ask `IbtTrackMapBuilder` to derive compact track geometry after successful IBT analysis, and native/localhost Track Map overlays consume the result through `TrackMapStore` rather than reading `.ibt` files directly.

Generated user maps live in app-owned local storage, outside the iRacing telemetry folder and outside retention-managed capture directories. `Storage:TrackMapRoot` defaults under `%LOCALAPPDATA%\TmrOverlay\track-maps\user`, with user-discovered maps separated from bundled/baseline maps.

The first implementation keeps source `.ibt` files external and persists only compact derived geometry: schema version, generated time, track identity, source/provenance summary, quality metrics, coordinate-system metadata, and a resampled closed polyline in local meter coordinates with lap-distance percentages. Prefer normalized local coordinates over raw latitude/longitude in the reusable map file unless raw geographic values are explicitly needed for diagnostics.

Keep future work behind `IbtTrackMapBuilder` and `TrackMapStore` instead of extending overlays to read IBT files directly. The builder should continue selecting clean complete laps, filtering pit/outlap/noisy samples where possible, converting `Lat`/`Lon` to a local tangent-plane coordinate system, smoothing/resampling/simplifying the line, scoring coverage and closure quality, and merging only when a new source improves an existing map for the same track identity.

The remaining roadmap is hardening and asset QA: richer current-map status, manual rebuild/replace UX, continued bundled-map review from vetted sources, deterministic screenshot states for confidence/stale/pit-lane cases, and pit-lane-aware live marker placement when reliable live progress is available. The overlay should keep treating an IBT-derived map as the learned driven line for that track/config, not as official track boundaries.

### Uniform Model V2 Migration

Core overlays now consume completed `LiveTelemetrySnapshot.Models` where promoted contracts exist. Compatibility slices remain useful for diagnostics, older snapshots, and behavior stability, but they should not become the preferred path for new reusable telemetry fields.

Keep migration additive:

1. Move one clearly bounded consumer to completed model inputs behind tests.
2. Keep legacy slice fields stable until no production surface depends on them.
3. Compare browser review, localhost, Windows artifacts, and captured sidecars before removing old overlay-local interpretation.

### Overlay UI/Style V2

Model v2 does not standardize visual code by itself. Treat overlay UI/style v2 as the presentation language for model-v2 telemetry first, with evidence/source UI reserved for stale, unavailable, modeled, or derived values.

The 0.18.4 branch note is historical. That branch fixed blocking Windows validation issues found in screenshots and diagnostics: invisible/topmost overlay input behavior, clipped settings component crops, too-short Session / Weather and Pit Service windows, and blank Fuel Calculator rows. Future style work should be a deliberate V1.x or V2 pass with side-by-side browser review, localhost, and Windows screenshot evidence.

The alignment is:

- Data Model V2 defines normalized telemetry: stable rows, session context, direct iRacing values, availability, source, freshness, quality, usability, and missing reasons.
- Design Paradigm V2 renders simple telemetry windows by default: standings, relative, local in-car radar, flags, session/weather context, timing tables, and similar overlays should be dense, stable, and quiet unless data is stale, unavailable, modeled, or derived.
- Evidence/source UI is exception chrome for analysis products: fuel strategy, non-local radar focus/multiclass interpretation, gap graphs, stint planning, and other app-derived decisions can show measured/model/history labels, source footers, and deterministic unavailable states.
- Competitor overlay analysis validates the product shape: small purpose-built overlays, low-noise dark translucent styling, dense information layout, strong semantic color, and multiple overlays visible at once rather than one monolithic dashboard.

A separate UI/style branch should promote reviewed semantic theme tokens and reusable primitives into Windows/native, browser review, and localhost overlay code for headers, quiet status badges, metric rows, timing tables, relative tables, flag strips, compact weather widgets, optional header/footer context slots, validation/admin source footers, graph panels, borders, class/severity colors, text fitting, and empty/error/waiting states. Those primitives should be able to consume model-v2 source/evidence state directly, but the normal rendering path should not make confidence the center of the UI.

Use browser review as the design-v2 proving ground while model-v2 evidence is still being collected. The deprecated mac preview path still preserves legacy deterministic design-v2 states under `mocks/design-v2/`, but new screenshot review should be generated from browser fixtures and localhost route captures. Promote only the primitives and semantics that survive browser review, localhost, and Windows/native validation.

Migrate style one overlay at a time with screenshot validation.

### Overlay Bridge / External Overlay Platform

Overlay Bridge should become the boundary for trusted teammate-to-teammate context sharing after the normalized live snapshot schema is stable enough. Treat it as a platform branch, not as another in-process overlay and not as the local OBS/localhost server.

Bridge v2 should define:

1. Versioned snapshot contracts for model-v2 telemetry, app health, overlay metadata, and selected display settings.
2. Safe access controls, explicit enable/disable controls, peer/client status, and schema-version display in the settings panel.
3. A peer/client development path that consumes normalized app state rather than talking to iRacing directly.
4. Compatibility rules for bridge clients when model-v2 fields are added, renamed, deprecated, or unavailable.
5. An opt-in peer/session context path for trusted teammates to share missed-history summaries when one user joins mid-race after another user has already observed the session.

This branch fits after enough Windows overlays consume `LiveTelemetrySnapshot.Models` that the external schema reflects real product semantics instead of temporary overlay-local assumptions.

The peer context path should be treated as derived context exchange, not raw telemetry sync. It should carry provenance, session identity, observation window, roster/timing coverage, schema version, and trust/source labels, then merge only into model-v2 availability as partial remote context. It should not silently overwrite local telemetry, and it should not share raw `telemetry.bin`, source `.ibt` files, or private local history by default.

### VR Renderer

Treat VR support as a future renderer/client, not as a tweak to the current WinForms desktop overlays. Keep the Windows tray app as the telemetry, settings, storage, diagnostics, and release owner; use the Overlay Bridge or a future model-v2 snapshot boundary for a separate VR renderer when the normalized contracts are stable enough.

The first VR candidates should be sparse and local: compact flag/status, blindspot/radar warnings, and a compact relative surface. Dense standings tables, gap graphs, strategy grids, and long text diagnostics should stay desktop-first until they have a VR-specific interaction model.

VR has stricter performance constraints than desktop overlays. At 90 Hz, the frame budget is about 11.1 ms; at 120 Hz, it is about 8.3 ms. Dropped frames are a comfort problem, not just a visual quality issue. A VR renderer should never perform disk IO, JSON parsing, history lookup, image decode, network calls, or avoidable allocations in the render loop. Precompute and double-buffer telemetry snapshots, cache/rasterize text only when values change, minimize transparent overdraw and many independent quads, and prefer stable low-motion positions with larger text and fewer simultaneous overlays.

The model-v2 implication is that VR needs compact, already-interpreted overlay state: local safety signals, current flag category, nearby relative rows, freshness, and unavailable reasons. It should not duplicate raw telemetry interpretation in a headset renderer.

### Streaming / Broadcast Overlays

Twitch and YouTube chat overlays belong in a streaming/broadcast group. They are not model-v2 telemetry primitives, but they can sit beside telemetry overlays as stream-facing presentation surfaces. The current product includes a narrow read-only native Twitch overlay plus the local localhost path: Streamlabs Chat Box widget embedding and public Twitch channel chat.

A future streaming branch should keep chat ingestion isolated from iRacing telemetry and define rate limits, authentication/storage, moderation controls, reconnect behavior, and deterministic offline preview states. Browser-based stream overlays should use `LocalhostOverlays` for local OBS capture; Overlay Bridge should only enter the design when a stream overlay intentionally needs trusted peer/shared data.

### Overlay Builder

An overlay builder is a future creator/development platform, not a prerequisite for the first Windows overlays. It should build on design-v2 primitives and the overlay bridge schema after both are stable.

Builder v1 should likely start as a local development tool for arranging simple telemetry widgets, choosing shared theme tokens, and exporting deterministic preview states. Only later should it become a user-facing editor for custom overlays. Keep the production Windows overlays hand-authored until the builder can generate layouts that meet the same readability, session-filter, stale-state, screenshot-validation, and performance expectations.

### Peer Reference: iRon DDU And Cover

The reviewed iRon DDU is useful product research, but it should not be interpreted as an immediate reason to build one configurable mega-overlay. In the code it is a hand-authored Direct2D dashboard with many compact information boxes, not a drag-and-drop user builder. The important lesson is the density and grouping of primitive race facts: gear, lap/session state, position, incidents, lap delta, fuel, tire/temperature-like readouts, bias, oil, and water can coexist in a compact lower-screen or secondary-display surface when each widget is small and predictable.

iRon's fuel block is the clearest functional reference. It averages recent valid green laps, ignores laps affected by pit road or caution-style flags, and applies a safety factor before estimating remaining laps and fuel-to-finish. TMR's fuel work should keep the richer stint/history/strategy model in purpose-built fuel and future engineer surfaces, but the DDU confirms that a compact "what do I need now" dashboard row can be valuable once those model-v2 facts are stable.

The iRon Cover overlay is deliberately just a blank rectangle. TMR's Garage Cover V1 is stronger as a streamer privacy feature because it keys off `IsGarageVisible`, stays opaque, uses app-owned imported artwork, and has bundled stock fallback art. The V2 opportunity is separate: a stream-facing garage/broadcast state can show safe session, standings, stint, sponsor, or team-art context while still covering setup details.

Product implication: keep pit release and garage privacy in V1 because they solve direct user pain now. Keep DDU-style layout composition, richer pit/engineer context, garage broadcast content, and user layout profiles in V2/design-v2, after the underlying model contracts and layout primitives are stable enough to make custom surfaces reliable.

### Layout Profiles

Scenario layout profiles are the user-facing version of configurable overlay sets. A profile should save the combination of overlays and their scenario-specific options, not only window placement. For example, qualifying may show relative, inputs, and lap delta with one set of scale/opacity/session gates, while race may show standings, relative, flags, radar, and pit-service context with different rows and placement.

Treat layout profiles as V2.0 because they need stable overlay metadata, durable settings schemas, monitor-aware placement, and validation. The first pass should be manual profile selection; automatic switching should wait until model-v2 session type, in-car/spectator role, driver-control state, and support diagnostics can explain why a profile changed.

### Custom Overlay Settings Slideout

A custom per-overlay settings slideout is V2 work, not part of the current V1-candidate patch branches. The V1 settings surface should stay on the shared flat regions that map directly to production behavior, such as General, Content, Header, Footer, Preview, and verified provider-specific tabs.

Use the V2 slideout for advanced overlay-specific controls after the settings schema and overlay metadata can explain them cleanly. Good candidates include Standings/Relative table column width/order controls, richer provider-specific stream controls, and future custom layout or profile editing. Do not expose those advanced controls in V1 unless they are already clearly wired, validated, and needed for the branch.

### Application Publishing

Publishing is app-platform work, not model-v2 overlay behavior. Current product tags drive Windows GitHub Release assets through the release workflow, including Velopack MSI/update assets plus the portable zip fallback.

A complete publishing path should still define signed Windows artifacts, installer and portable packaging, update-channel policy, rollback/compatibility expectations for durable user settings/history, diagnostics bundle expectations for tester builds, and passive update checks. The Windows executable icon is derived from `assets/brand/` into `src/TmrOverlay.App/Assets/`; future overlay-branding derivatives should follow the same source-to-platform-asset pattern.

### Brand Palette Refresh

Future design work should review `assets/brand/Team_Logo_4k_TMRBRANDING.png` before changing app or overlay color tokens. The image is a 3840x2160 fully opaque RGBA PNG with 227,192 unique RGB values, so the exact palette is mostly gradients, glow, antialiasing, snow/noise, and art texture rather than a small UI palette. Use the dominant flat colors and grouped palette candidates as discussion inputs instead of importing the full image palette. If an audit needs every exact hex, keep that as a generated CSV under ignored `artifacts/brand-palettes/`, not as tracked documentation.

Dominant exact image colors:

| Hex | Candidate use |
| --- | --- |
| `#FF2D97` | brand pink / primary action accent |
| `#11FFE3` | brand aqua / active cyan accent |
| `#2F004E` | deep purple hero or installer background |
| `#FFFFFF` | text, outlines, snow/highlight treatment |
| `#7B00FF` | electric purple accent / road stripe |
| `#FF2C42` | coral-red alert or tail-light accent |
| `#CF4E00` | sunset orange shadow |
| `#CA00F1` | neon violet-magenta |
| `#EA9C00` | amber/sun highlight |
| `#CE5751` | muted coral shadow |
| `#E6FEFB` | ice highlight |
| `#88FFF1` | aqua glow tint |
| `#111111` | near-black detail |
| `#009883` | teal shadow |
| `#242424` | dark neutral detail |
| `#6CFFEE` | secondary aqua tint |

Current Design V2 tokens are centralized in `shared/tmr-overlay-contract.json` and consumed by native overlays, browser review, and localhost CSS. Future palette experiments should update that shared contract first, then verify contrast, readability, browser review, localhost, and native parity before changing overlay-specific drawing code.

### Telemetry-First Overlay Branches

Do not wait for the deep-dive analysis products before building every overlay. Standings, relative, local in-car radar, flag display, session/weather, and timing-table overlays can be much simpler windows into normalized iRacing telemetry.

For these overlays, model v2 should prioritize stable row identity, column formatting, class/session labels, pit/flag/session state, freshness, and predictable unavailable states. It should still carry `LiveSignalEvidence`, but normal UI should only surface that evidence when the data is stale, unavailable, modeled, or derived.

The flag pass adds one model-v2 requirement: normalize flag categories rather than making each overlay inspect raw SDK bits. Preserve raw global `SessionFlags` and per-car `CarIdxSessionFlags`, but expose user-facing categories such as green start/resume, blue, yellow/debris/caution, finish/countdown, and critical driver flags. Treat `serviceable`, `start hidden`, and plain steady-state green running as background context unless combined with a displayable category.

Current telemetry-first readiness:

- Core overlays now have production native Windows, browser review, and localhost paths consuming completed `LiveTelemetrySnapshot.Models` where those contracts exist. Future design/model work should harden partial coverage, class labels/colors, focus behavior, larger-field edge cases, and degraded states rather than treating those overlays as unstarted.
- Flags, session/weather, pit-service snapshot, and input/car-state overlays consume `LiveTelemetrySnapshot.Models` directly. Flags and input/car-state use custom graphical forms while session/weather and pit-service continue through the simple telemetry shell.
- Sector comparison is a simple table visually, and live diagnostics now test whether sector metadata plus car progress can produce enough sector-boundary intervals. It still needs an explicit model-v2 row contract before it should be promoted beyond development review surfaces.
- Blindspot signal should stay local in-car only and can use the existing local-player `CarLeftRight`/proximity state without the advanced non-local radar branch.
- Laptime delta can now use diagnostics to confirm live delta-channel availability and `_OK` usability across sessions. Until that evidence is broad enough and represented in model v2, it remains a design/model target rather than a reliable Windows overlay.
- Stint laptime log can stay simple if it plots completed local lap times, resets on stint boundaries, and avoids strategy interpretation.

### Pit Crew / Engineer Overlay

Treat pit crew/engineer as a V2.0 operator mode/workspace, not as a hidden feature inside the read-only pit-service snapshot and not as a V1.0 core overlay. A spotter or engineer watching a team race needs a different telemetry surface than the in-car driver: pit stop request state, repair/fuel/tire choices, stint/fuel analysis, driver-control context, team-car status, and potentially command-capable controls for iRacing pit service variables.

Before adding command controls, isolate simulator writes behind an explicit pit-command service and UI state that makes scope clear. iRacing pit commands are active-car commands, so spectator/teammate behavior needs live validation before any team-operator workflow can promise control over the car being watched. The first pit-service overlay should therefore stay read-only while captures collect the pit/service/setup-change evidence needed for the V2.0 engineer product.

### Garage / Broadcast Context Surface

Garage Cover V1 is a privacy cover. V2 can use the same `IsGarageVisible` trigger as a stream-facing context state: show team art, session status, standings snippets, recent stint context, strategy notes, or sponsor-safe messaging while setup details are hidden.

Keep this separate from the native privacy requirement. The default cover must remain opaque and trustworthy; richer race information should be opt-in, deterministic, and sourced from model-v2 state that is already validated elsewhere. A future localhost version may fit OBS workflows better than only a native always-on-top window.
