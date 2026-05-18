# Current State

Last updated: 2026-05-15

## Project Goal

`tmrOverlay` is being built as a Windows-native iRacing companion. The product direction has shifted from always-on raw capture toward live telemetry analysis plus compact historical session summaries. Raw capture remains available as an opt-in diagnostic/development mode.

## Implemented So Far

### Application shell

- `assets/`
  - contains repo-owned source visual assets such as brand/logo images for future app icons, overlay branding, docs, and publishing work
  - the v0.9 publishing branch derives the Windows executable icon from the source logo into `src/TmrOverlay.App/Assets/TmrOverlay.ico`

- `src/TmrOverlay.App/TmrOverlay.App.csproj`
  - .NET 8 WinForms app
  - `WinExe`
  - uses `irsdkSharp` and `Microsoft.Extensions.Hosting`
  - references `src/TmrOverlay.Core/` for platform-neutral settings, telemetry/history models, live read models, fuel strategy logic, overlay metadata, and post-race analysis models

- `src/TmrOverlay.App/Program.cs`
  - uses a local named mutex so a second Windows launch exits instead of starting another telemetry collector
  - builds a generic host
  - loads `appsettings.json`
  - starts the tray application context

- `src/TmrOverlay.App/Shell/NotifyIconApplicationContext.cs`
  - owns the tray icon
  - exposes:
    - open latest capture
    - open capture root
    - open logs
    - create diagnostics bundle
    - exit
  - shows the overlay form on startup

### On-screen feedback

- `src/TmrOverlay.App/Overlays/`
  - overlay modules are separated by type
  - `Abstractions/` contains Windows overlay form helpers such as `PersistentOverlayForm`, which centralizes frame setup, drag handling, and per-overlay settings persistence; shared table/chrome helpers keep dense readout overlays aligned on title/time-remaining chrome, table borders, cell labels, row sizing, and change-detection setters, with `OverlayChromeState` still carrying status/source evidence for model diagnostics while rendered header status and footer source chrome stay disabled
  - `Styling/OverlayTheme.cs` contains human-editable shared Windows overlay colors, font helpers, and common layout tokens; data-visualization-specific colors can remain near their drawing code
  - shared durable-settings defaults and Design V2 role colors live in `shared/tmr-overlay-contract.json` with `shared/tmr-overlay-contract.schema.json`; native and localhost rendering read this contract before user-local overrides, and browser review uses the same assets for local validation plus screenshot generation
  - durable user-data release snapshots live under `fixtures/data-contracts/`; `v0.19.0` is the first V1-candidate baseline for settings, compact history, generated track maps, raw-capture metadata, and runtime state, and future schema branches should prove the previous release snapshot loads through current code
  - optional `overlay-theme.json` under the app settings root can override Windows shared font/color tokens without recompiling
  - `AppDiagnosticsStatusModel` feeds the Support tab and diagnostics bundle app-health summaries; the old floating Collector Status overlay is no longer a managed user-facing overlay
  - `SettingsPanel/` contains a branded fixed-size tabbed settings window for user-managed overlay visibility, scale/opacity when applicable, units, support capture/log/diagnostics access, and overlay-specific display options; overlay tabs use left-side General/Content/Header/Footer sections, with Input / Car State suppressing unused Header/Footer sections and Garage Cover using General/Preview
  - `FuelCalculator/` contains the first strategy overlay, backed by live telemetry plus exact car/track/session history
  - `Standings/` contains a compact scoring-first table backed by `LiveTelemetrySnapshot.Models.Scoring`, with live timing enrichment, official multiclass table ordering, full-width class-colored separators, content-row visibility, configurable other-class row counts shared by native and localhost rendering, and timing fallback when scoring is unavailable
  - `Relative/` contains the first production model-v2 overlay, a telemetry-first relative table backed by `LiveTelemetrySnapshot.Models.Relative`
  - `TrackMap/` contains the transparent map-only track-map overlay: generated local map geometry when available, circle fallback otherwise, smoothed live car dots placed only from rows with usable lap-distance progress, scoring-backed focused/player `P<N>` text and class colors when available, full-opacity continuous white track lines, schema-v2 sector boundaries, model-v2 green/purple sector highlights, invalid-lap-counter fallback from valid `LapDistPct`, active-reset-style boundary seeding, and setting-driven internal map fill opacity
  - `GarageCover/` contains a localhost-only streamer privacy surface for OBS; it uses the specific iRacing Garage/setup-screen-visible signal, fails closed to an opaque imported image or bundled stock fallback when telemetry is unavailable/stale, copies imported images into app-owned settings storage, exposes scale plus settings-tab image preview/import/clear controls and detection state, and uses crop-to-cover image fitting
  - `Flags/` contains a compact transparent icon-only scale-controlled overlay with procedurally drawn live session flags; it can show multiple active user-facing categories at once, ignores background-only SDK bits and plain steady-state green running by themselves, and requires recognized live session context before the runtime window is shown
  - `CarRadar/` contains a transparent circular local-player in-car proximity overlay, backed by local player/team progress, player-only `CarLeftRight`, physically placed nearby `CarIdx*` progress/position telemetry, and optional timing-based multiclass warning context
  - `GapToLeader/` contains a rolling in-class gap trend graph, backed by completed model-v2 timing/race-progress state, a four-hour visible window, and same-lap reference selection for long endurance gaps

- `src/TmrOverlay.App/Diagnostics/AppDiagnosticsStatusModel.cs`
  - shared support/status model for app health, live telemetry state, raw capture write health, warnings/errors, Support-tab labels, and diagnostics bundle summaries
  - the former floating Collector Status overlay has been removed from the managed user-facing overlay set; app-health status should remain in Support unless a future support-only tool has a concrete workflow
  - new overlay features should log unexpected refresh/render failures and surface a compact visible error state, while normal telemetry gaps should degrade to waiting/unavailable

- `src/TmrOverlay.App/Overlays/SettingsPanel/`
  - wide 1240x680 settings window with a WinForms Design V2 shell, TMR logo, `Tech Mates Racing Overlay` title bar, outrun-style sidebar tabs, and card-style settings regions so the Windows main app can be compared directly against browser review and localhost behavior
  - the settings window is recentered whenever it opens and does not persist user-dragged placement between runs
  - opens on startup and can be reopened from the tray menu
  - acts as the main UI; clicking its `X` or otherwise closing it through the user close path exits the application instead of hiding the app to the tray
  - uses normal desktop/taskbar/Alt+Tab behavior by default, but temporarily promotes itself above product overlays while active so Settings remains clickable; product overlays keep their own always-on-top setting and remain click-through/no-activate where appropriate
  - sidebar tabs include General, user-facing overlay tabs ordered by common race workflow, and Support last
  - General exposes a metric/imperial unit selector; user-facing font selection stays hidden while native/browser/localhost parity remains a theme-level concern
  - General includes a Show Preview control for Practice, Qualifying, and Race; it swaps native/localhost reads to deterministic session fixtures while leaving normal overlay enabled state, content/header/footer session gates, positions, scale, opacity, topmost, and Stream Chat configuration untouched
  - Support is task-oriented for teammate handoff: visible app version/build metadata, diagnostic telemetry capture first, diagnostics bundle actions, compact current state, and storage shortcuts without exposing advanced collection internals as normal teammate controls
  - first-run/no-iRacing waiting states are worded as expected idle states in Support instead of active failures
  - per-overlay tabs expose visibility, scale, opacity, descriptor-driven overlay-specific display options, V2 content-toggle matrices, current shared header controls such as time remaining, empty footer regions where no footer controls are active, and recommended OBS source sizes when those controls make sense for that overlay; content/header/footer session columns show Practice, Qualifying, and Race, with Practice also writing the Test setting keys
  - opening the radar settings tab previews the radar overlay only when the overlay is enabled, so the tab no longer overrides the `Visible` checkbox
  - visibility, scale, opacity, unit, and display-option changes are coalesced briefly before save/apply so checkbox bursts do not recursively rebuild overlays; content/header/footer session gates are rechecked against live session type

- Design V2 overlay styling:
  - Windows live overlays default to the Design V2 shell for live testing, with `TMR_WINDOWS_DESIGN_V2_LIVE_OVERLAYS=false` kept as an opt-out
  - localhost routes use the same V2 shell/colors as native review surfaces
  - browser review is now the primary local overlay parity loop, while localhost route screenshots are a separate route-surface gate: `npm run review:browser` serves `/review/app`, `/review/settings/general`, individual browser pages under `/review/overlays/<overlay-id>`, and localhost pages under `/overlays/<overlay-id>` from the same asset files as localhost routes; `npm run screenshots:browser-review` captures browser review/settings into `artifacts/browser-review-screenshots`, and `npm run screenshots:localhost` captures localhost routes into `artifacts/localhost-screenshots`
  - Playwright integration tests run against managed Chromium by default so browser tests do not launch the user's normal Chrome app; set `TMR_PLAYWRIGHT_CHANNEL=chrome` only when a system Chrome run is intentionally needed
  - treat UI/style v2 as telemetry-first by default: standings, relative, local in-car radar, flags, session/weather context, and timing tables should be dense, stable windows into iRacing telemetry
  - persistent source footers are removed from current user-facing overlay chrome; source remains model, manifest, and diagnostic evidence
  - reserve model-v2 source, quality, usability, freshness, and missing-reason evidence for stale, unavailable, modeled, or derived values, especially analysis products like fuel strategy, non-local radar focus/multiclass interpretation, and gap graphs
  - use competitor overlay analysis as the product-shape check: small purpose-built overlays, dense information, low-noise dark styling, and semantic color instead of one monolithic dashboard
  - the Windows settings app uses an additive WinForms Design V2 surface over the same production settings contracts, while browser review mirrors that shape for local inspection
  - the tracked mac harness keeps legacy generated `mocks/design-v2/` artifacts for telemetry-first standings, relative, local in-car radar, flag display, table semantics, and narrower analysis-exception states, but it is deprecated secondary scaffolding rather than the product parity target or screenshot authority
  - Design V2 now has shared role-color tokens for native overlays and localhost CSS, plus live and generated component-review surfaces for overlay shells, buttons, controls, status pills, table rows, graph chrome, localhost blocks, sidebar tabs, section panels, and settings content blocks
  - future style groundwork should continue promoting reviewed semantic theme tokens and reusable primitives into Windows/browser overlay code for headers, optional diagnostic badges/footers, metric rows, table cells, graph panels, shared borders, severity colors, class colors, text fitting, and empty/error/waiting states
  - keep migrations additive and screenshot-validated, with overlay-specific domain layout local

- `src/TmrOverlay.App/Overlays/FuelCalculator/`
  - draggable three-column fuel strategy overlay placed in the left-side timing stack by default
  - estimates timed-race laps from shared `LiveRaceProjectionModel` clean-lap pace when enough evidence exists, falling back to session time, selected lap time, and leader/team progress
  - uses measured completed green-lap fuel deltas first, then exact user history for car/track/session combos; tracked baseline/sample history is opt-in
  - V1 keeps the overlay local in-car/pit only; future fuel work can revisit modeled stint-history analysis when a selected/focused driver lacks exposed live fuel scalars
  - renders whole-lap stint targets, target liters-per-lap, planned stint/stop count, final stint length, laps-per-tank, and min/avg/max burn when history exists
  - keeps the full table row layout visible so strategy changes do not resize or switch the overlay view during live updates
  - hides rhythm optimization and tire-service advice for V1 until pit-service source buckets are strong enough to support those recommendations
  - accounts for overall leader/reference-class/team projection for timed-race lap count and stores class-leader context as model/source evidence without rendering a shared source footer
  - can bias future stint targets toward historical team-stint evidence, currently 8 laps for the 4-hour Nürburgring baseline, without labeling teammate rows in the UI
  - warns when a target such as an 8-lap stint needs realistic fuel saving versus nominal tank range or avoids extra stops over longer races
  - uses blank future rows when no fuel stop is needed or fewer stints are known, instead of hiding rows based on noisy collection thresholds
  - caches exact history lookups briefly by car/track/session so live fuel refreshes do not reread aggregate JSON every tick
  - skips expensive strategy/UI work when the live snapshot sequence and display options are unchanged, and only mutates labels/table layout when target values changed

- `src/TmrOverlay.App/Overlays/Relative/`
  - draggable telemetry-first relative overlay placed near the right-side driver stack by default
  - is the first production overlay to consume `LiveTelemetrySnapshot.Models.Relative` directly instead of a legacy overlay-specific snapshot slice
  - renders actual cars ahead above a reference row and actual cars behind below it, with configurable 0-8 maximum row counts per side; during live telemetry it keeps those configured row slots stable and leaves unused slots blank so the reference row does not jump
  - uses shared compact content-column settings for plain numeric position, full driver name, gap, and optional pit column; class highlighting is intentionally quiet by default
  - uses model-v2 relative rows from proximity first and timing/class-gap fallback when proximity is unavailable; when proximity has lap-distance placement but no direct relative seconds, the relative model can infer display seconds from live lap distance times the current lap-time signal while leaving radar timing stricter
  - uses scoring/results data only to enrich cars already present in relative telemetry; scoring-only cars are deliberately not added to Relative because they do not have honest live relative placement
  - keeps pit-road relative rows visible, but de-emphasizes them so passing cars in the pits remain useful context without reading as active on-track threats
  - colors whole-lap-ahead/behind rows through text color independent of car-class row accents
  - normalizes gap signs by row direction so cars ahead display negative gaps and cars behind display positive gaps regardless of source sign
  - keeps configured ahead/reference/behind row slots stable during live telemetry so nearby-car churn updates labels in place instead of causing table-height and reference-row jumps
  - keeps source/evidence chrome quiet in the normal case; the footer only calls out live proximity, model-v2 timing fallback, partial timing, or waiting
  - treats snapshots older than 1.5 seconds as stale and shows waiting instead of retaining old rows
  - has a matching generated mac-harness screenshot set under `mocks/relative/`

- `src/TmrOverlay.App/Overlays/CarRadar/`
  - draggable 300px circular radar overlay placed near the right-side driver stack by default
  - transparent outside the circle and paints nothing when no cars are within proximity or multiclass warning range
  - is local-player in-car only: requires a valid local player car and hides for explicit non-player focus, garage/off-track, and pit contexts instead of trying to reinterpret spectator/teammate focus as production radar
  - uses local `CarLeftRight` for side occupancy and local player/team progress for nearby placement; `Focus*` fields are used only when focus is local or not explicitly another car
  - uses only fresh live nearby-car telemetry for placement around the local car; drawn radar cars require live `CarIdxLapDistPct` plus track length physical-distance placement, while `CarIdxEstTime` and `CarIdxF2Time` remain timing context for Relative, diagnostics, and optional multiclass warning timing without drawing radar targets
  - excludes pit-road cars from radar proximity and hides the radar while the local car is in pit-road states
  - draws the local car as a white rectangle and nearby traffic from any class as neutral-white rectangles that fade in between radar entry and the yellow-warning threshold, then move through yellow toward saturated alert red only inside the close bumper-gap warning buffer, using physical distance inside a car-length-based radar window with meter labels on the rings
  - keeps per-car visual state by `CarIdx`, fades the whole radar and side-warning rectangles in/out, and treats stale live snapshots as unavailable so old proximity does not stay painted forever
  - clips the Windows form to a circular region, uses a black transparency key instead of fuchsia, and drops form opacity to zero after fade-out so a transparency-key failure cannot leave a purple backing window visible
  - when `CarLeftRight` is active and a close decoded radar car likely caused the side warning, attaches that car to the side slot, suppresses its normal center-lane rectangle, and biases the side marker forward/back from the local car using longitudinal gap
  - tracks recent relative timing for other-class cars and can draw a short outer red arc with a live seconds gap when faster multiclass traffic is behind outside the physical radar-car range but within 5 seconds
  - currently does not have true per-car lateral telemetry; side occupancy comes from the scalar iRacing left/right signal, and decoded radar cars only attach to side slots when their physical distance is inside the close side-attachment window

- `src/TmrOverlay.App/Overlays/GapToLeader/`
  - draggable in-class gap trend graph placed below the radar by default
  - draws the class leader as the fixed top baseline
  - keeps bounded overlay-local four-hour in-memory traces for all available same-class timing rows; these traces are only for rendering and are not persisted
  - consumes a separate same-class timing row list so cars with valid standings/F2 timing but invalid lap-distance progress can still appear in the graph without polluting radar proximity placement
  - dynamically renders the focused car's class leader, the focused car, nearest five same-class cars ahead and behind, plus recently visible cars that need continuity as they enter/leave the nearby window
  - anchors the X-axis at the first visible sample, starts with a readable short window, grows toward the four-hour cap, then slides the window after four hours; it scales the Y-axis to the visible same-lap reference field spread, keeps axis labels in a left gutter, highlights whole-lap gap reference lines when the field spreads far enough, draws vertical 5-lap duration markers, and labels current line endpoints with compact current `P<N>` class-position tags
  - draws subtle weather-condition bands behind the graph from live `TrackWetness` / `WeatherDeclaredWet`; non-focused/non-leader context lines are intentionally dimmed to keep the focused-car gap and position readable
  - marks driver swaps as compact ticks/dots on the affected line while preserving line color; Windows uses real session-info driver-row changes by `CarIdx` plus the local `DCDriversSoFar` signal, while the mac harness can use named mock handoffs
  - marks leader changes and keeps old leader/currently exiting/missing-telemetry car lines visually continuous with fade/dash behavior instead of disappearing abruptly
  - can label current line endpoints with compact `P<N>` class-position text
  - prefers `CarIdxF2Time` for timed gaps, with lap-progress fallback from `CarIdxLapCompleted` and `CarIdxLapDistPct`; if the leader moves more than a lap ahead, it switches to the nearest eligible same-lap in-class reference and labels out-of-reach cars as `P<N> +N lap` context rather than drawing them as the white reference line
  - defaults to race sessions only; a hidden migration marker prevents that default from overriding later user session-filter changes

### Telemetry collection pipeline

- `src/TmrOverlay.App/Telemetry/TelemetryCaptureHostedService.cs`
  - owns the `IRacingSDK` instance
  - subscribes to `OnConnected`, `OnDisconnected`, and `OnDataChanged`
  - starts live telemetry collection on first usable live data
  - records compact historical telemetry samples every frame
  - snapshots session YAML into the history accumulator whenever `SessionInfoUpdate` changes
  - creates raw capture directories and copies the raw telemetry buffer only when raw capture is enabled by startup configuration or runtime overlay request
  - ties IBT telemetry logging to the same raw-capture switch by default, so raw capture start/stop requests also ask iRacing to start/stop `.ibt` logging
  - writes post-session sidecars after raw capture finalization: `capture-synthesis.json` runs immediately from the closed TmrOverlay capture with a timeout, while `ibt-analysis/*.json` waits only a bounded time for iRacing to stop writing before skipping and leaving the capture eligible for startup recovery
  - IBT analysis now includes `ibt-local-car-summary.json`, a bounded local-car trajectory/fuel/vehicle-dynamics summary with track-map readiness and explicit missing opponent-context fields; source `.ibt` files still stay out of capture directories by default
  - records live model-v2 parity in observer mode; Relative now consumes `LiveTelemetrySnapshot.Models.Relative` directly, while the remaining legacy overlay slices are compared against the additive models and summarized in `live-model-parity.json`
  - records passive live overlay diagnostics in observer mode by default; `live-overlay-diagnostics.json` summarizes gap session semantics, large gap/jump evidence, local-only radar suppression, clear-vs-active side signal evidence, side/focus/placement evidence, fuel source evidence, pit windows with fuel increases/black-flag overlap, raw ARB/driver-control and pit-command changes, suspicious non-race race-projection signals, sampled position cadence, lap-delta channel availability, relative lap-relationship probe counts, derived sector-timing coverage, and track-map sector-highlight coverage
  - records bounded compact edge-case telemetry artifacts for every live session by combining normalized live samples with selected scalar raw watch channels for fuel, tires, suspension, brakes, wheel speed, pit service, pit-request command changes with viewer/camera context, weather, forecast-like weather variables when present, ARB/anti-roll/wing-like driver-adjustment channels when present, engine/replay/system/network state, incidents, and driver-control changes; artifacts also count observations dropped after the clip cap and include a final sampled context tail so long spectated/parked sessions still have late-session telemetry context
  - isolates raw-capture frame queue/write/read failures from live history, normalized live telemetry, and overlay performance recording so overlays can keep updating while capture diagnostics run
  - logs and records app events for runtime raw-capture start failures instead of silently failing
  - writes normalized live frames through `ILiveTelemetrySink`, not directly to overlays

- `src/TmrOverlay.App/Telemetry/TelemetryCaptureSession.cs`
  - owns the per-capture file writer
  - writes:
    - `telemetry.bin`
    - `telemetry-schema.json`
    - `capture-manifest.json`
    - `latest-session.yaml`
    - optional historical `session-info/*.yaml`
    - post-session `capture-synthesis.json` is written by `CaptureSynthesisService`, not by the live writer
    - post-session `ibt-analysis/*.json` is written by `IbtAnalysisService` when enabled and a candidate `.ibt` can be selected or a skipped/failed status is recorded
    - opt-in post-session `live-model-parity.json` is written by `LiveModelParityRecorder` when model-v2 parity collection is enabled
    - default-on post-session `live-overlay-diagnostics.json` is written by `LiveOverlayDiagnosticsRecorder` when overlay diagnostics collection is enabled; without raw capture the same observer artifact is written under `logs/overlay-diagnostics`
  - uses a bounded channel to decouple SDK callbacks from disk writes
  - drops frames when the queue fills instead of blocking the SDK callback path

- `src/TmrOverlay.App/Telemetry/TelemetryCaptureState.cs`
  - stores shared status for the tray and overlay
  - current connection state
  - current live-collection state
  - current raw capture directory when raw capture is enabled
  - frame counts
  - dropped-frame counts

- `src/TmrOverlay.App/Performance/AppPerformanceState.cs`
  - stores lightweight in-memory performance counters and rolling timing summaries
  - tracks telemetry callback throughput, normalized live sink time, edge-case recorder time, history accumulation time, continuous iRacing network/system values, capture writer write time, capture queue depth, overlay refresh timings, overlay update-decision rates, overlay timer cadence/active counts/late ticks, lifecycle visibility/session/fade states, overlay window/input-intercept state, settings save/apply queue metrics, skipped unchanged-sequence updates, explicit paint samples, localhost idle/request activity, dropped/written raw frames, process memory, GDI/USER handle pressure, and GC counts
  - intentionally stores aggregate/recent-window metrics rather than every telemetry frame
  - `AppPerformanceHostedService` writes periodic JSONL snapshots under the logs performance folder regardless of raw-capture state, with the first startup write delayed so launch does not immediately touch performance-log storage

- `src/TmrOverlay.Core/Telemetry/Live/`
  - `LiveTelemetryStore` is the shared normalized live source for product overlays
  - `ILiveTelemetrySource` is the read boundary for overlays and the local bridge
  - `ILiveTelemetrySink` is the write boundary for live iRacing collection and replay/dev providers
  - `LiveTelemetrySnapshot` includes fuel, proximity, leader-gap, and same-class gap graph compatibility inputs derived from each frame
  - `LiveTelemetrySnapshot.Models` is an additive live-only model layer with session, driver-directory, timing, relative, spatial, track-map, weather, fuel/pit, race-event, race projection, and input availability families; it is derived from existing normalized samples and keeps raw capture, compact history, post-race analysis, and existing synthesized data backwards-compatible
  - model-v2 rows carry `LiveSignalEvidence` so overlays can distinguish reliable timing, timing-only rows, spatial/radar placement eligibility, partial leader-gap timing, diagnostic instantaneous fuel burn, and rolling measured-fuel baseline eligibility as overlays migrate
  - `TimingColumnRegistry` defines reusable timing data keys/formatters, while `OverlayContentColumnSettings` defines per-overlay visible columns, durable width/order compatibility fields, and OBS size recommendations for current table overlays; the current V1 UI only exposes row visibility/count controls, with advanced width/order editing deferred to the future V2 custom settings slideout

- `local-mac/TmrOverlayMac/`
  - mirrors the live overlay diagnostics recorder for mock/demo runs so the four-hour preview and capture-derived radar/gap demos can emit `live-overlay-diagnostics.json`
  - is now secondary native-shell scaffolding; browser review is preferred for routine local design/functionality validation, while Windows still owns native focus/topmost/click-through/iRacing SDK validation
  - does not yet replay arbitrary 24-hour raw captures through every overlay; that is tracked as future harness work in `docs/model-v2-future-branches.md`
  - mirrors the Relative overlay for design-v2 iteration and generated validation screenshots

### Session history

- `src/TmrOverlay.App/History/`
  - owns disk storage and lookup for compact end-of-session summaries
  - stores user summaries under `%LOCALAPPDATA%/TmrOverlay/history/user/cars/{car}/tracks/{track}/sessions/{session}/`
  - writes a per-capture summary plus an aggregate for baseline lookup
  - runs background startup history maintenance that normalizes compatible legacy summary metadata, backs up rewritten summaries, rebuilds session aggregates from compatible summaries, rejects incompatible aggregate versions during lookup, and writes `.maintenance/manifest.json`
  - low-confidence samples are still stored but do not contribute to baseline aggregate values

- `src/TmrOverlay.Core/History/`
  - contains platform-neutral historical session context, telemetry samples, summary/aggregate models, slug/path helpers, session-info parsing, and the in-memory historical accumulator

- `src/TmrOverlay.App/Analysis/`
  - can persist post-race analysis JSON under the user history root when `PostRaceAnalysis:Enabled=true`; settings exposes it only as advanced collection status, not as a normal user-facing overlay tab
  - `PostRaceAnalysisPipeline` saves an analysis row after compact session history is saved when enabled and isolates analysis persistence/event failures from telemetry finalization

- `history/baseline/`
  - contains tracked, sanitized development/sample data from the 4-hour Nürburgring VLN Mercedes-AMG GT3 race capture
  - is not read by default because `SessionHistory:UseBaselineHistory` defaults to `false`
  - fuel baseline uses local-driver scalar fuel frames only
  - lap timing and pit counts use team-car `CarIdx*` telemetry because those remain valid during teammate stints
  - session summaries can store completed stint summaries and per-stop pit-service timing estimates with confidence flags
  - teammate stint fuel is modeled from user/baseline history unless a reliable live scalar source is available
  - the 4-hour baseline summary now includes sanitized local-driver 7-lap and teammate-driver 8-lap stint history for planning hints
  - the 4-hour baseline aggregate includes observed fuel fill rate and inferred tire-service timing for early tire guidance

### Storage

- `src/TmrOverlay.App/Storage/AppStorageOptions.cs`
  - centralizes app-owned local storage roots
  - default writable root is `%LOCALAPPDATA%/TmrOverlay`
  - repository-local storage is opt-in with `Storage:UseRepositoryLocalStorage`
  - tracked sample/baseline history belongs under `history/baseline` but production lookup is opt-in

### Logging

- `src/TmrOverlay.App/Logging/`
  - custom local file logger provider
  - writes rolling logs under `%LOCALAPPDATA%/TmrOverlay/logs`
  - logs startup/shutdown, storage roots, telemetry service messages, and unhandled exceptions
  - logging cleanup is best-effort and must not affect capture reliability

### Application boilerplate

- `src/TmrOverlay.App/Settings/`
  - persists overlay settings under `%LOCALAPPDATA%/TmrOverlay/settings`
  - the disk store remains Windows app-owned; settings models and `AppSettingsMigrator` live in `src/TmrOverlay.Core/Settings/` so other development harnesses can follow the same schema
  - scale-capable overlays keep width/height as compatibility fields, but `OverlayManager` derives those dimensions from overlay defaults plus saved scale instead of treating width/height as independent user-facing controls
  - settings are versioned, migrate legacy per-overlay boolean/integer fields into the keyed overlay option bag, and prune obsolete option keys when an overlay control is removed; version 6 added per-session shared chrome options and the current migrator now removes the obsolete header-status/footer-source option keys

- `src/TmrOverlay.App/Events/`
  - writes JSONL app-event breadcrumbs under `%LOCALAPPDATA%/TmrOverlay/logs/events`

- `src/TmrOverlay.App/Runtime/`
  - writes a heartbeat/runtime-state file and detects the previous unclean shutdown
  - includes a local-development build freshness check that warns when source files in the checkout are newer than the running build

- `src/TmrOverlay.App/Diagnostics/`
  - creates support bundles with app/storage metadata, telemetry state, evidence-quality warnings, latest-capture evidence summaries, localhost overlay state/request counters, IBT analysis settings/status, track-map inventory plus current-track lookup evidence, lightweight performance snapshots, recent performance logs, runtime state, settings, logs/events, and latest capture metadata plus compact capture/IBT sidecars
  - includes recent compact edge-case telemetry artifacts under `edge-cases/`, including their final context tail when present
  - includes recent model-v2 parity artifacts under `model-parity/` plus the latest capture's `live-model-parity.json` when present
  - includes recent post-race analysis JSON at top-level `analysis/` plus recent user-history summaries and aggregates so collected car/track/session metrics can be inspected for accuracy
  - creates a best-effort diagnostics bundle automatically when a live telemetry session finalizes, and the Support tab reports the latest automatic bundle
  - intentionally excludes raw `telemetry.bin` and source `.ibt` payloads

- `docs/repo-surface.md`
  - separates customer/tester-facing material, product source, internal development assets, and ignored local runtime data
  - records cleanup candidates such as local `artifacts/`, root `tmroverlay-diagnostics-*` folders/zips, and ignored raw captures that should be moved to external storage or replaced with compact fixtures when analysis is complete

- `src/TmrOverlay.App/Retention/`
  - removes old capture directories and diagnostics bundles in background startup cleanup
  - removes old always-on performance JSONL logs and compact edge-case telemetry artifacts in background startup cleanup

- `src/TmrOverlay.App/Replay/`
  - provides a replay-mode seam for overlay development against an existing capture
  - is registered as the active telemetry provider through the shared provider registration path when `Replay:Enabled` is true

- `src/TmrOverlay.App/Localhost/`
  - default-on localhost server for OBS and other local capture tools; disable with `LocalhostOverlays:Enabled=false` when the local HTTP server is not wanted
  - exposes `GET /health`, legacy diagnostic `GET /snapshot` / `GET /api/snapshot`, per-overlay model routes under `GET /api/overlay-model/{id}`, legacy/settings routes such as `GET /api/standings`, `GET /api/relative`, `GET /api/track-map`, `GET /api/stream-chat`, `GET /api/garage-cover`, `GET /api/garage-cover/image`, and per-overlay HTML routes under `/overlays/{id}`
  - current localhost overlay routes cover standings, relative, fuel calculator, session/weather, pit service, input state, car radar, gap to leader, track map, flags, stream chat, and Garage Cover
  - localhost route metadata and page scripts live with overlay modules under `src/TmrOverlay.App/Overlays/`; localhost owns HTTP transport and the generic page shell
  - pages fetch `/api/overlay-model/{id}` only, and the server completes live models before dispatching to overlay model builders; local localhost overlays do not read directly from iRacing, raw capture files, browser `latestSample` helpers, or the legacy `/api/snapshot` route
  - records lifecycle status, request counts, route counts, failures, recent activity status, last-request details, full browser-route catalog metadata, shared settings/design-token contract metadata, and Garage Cover route/image/detection metadata into diagnostics bundles
  - Stream Chat reads one saved settings source at a time and defaults to public Twitch channel `techmatesracing` through the shared settings contract; the native overlay auto-connects to public Twitch channel chat, while the localhost route can also embed a Streamlabs Chat Box widget URL; Streamlabs widget URLs are redacted from diagnostics bundles
  - each overlay settings tab lists a selectable/copyable localhost URL, and the route remains usable even when the native overlay is hidden or the surface is localhost-only

### Deprecated local mac harness

- `local-mac/TmrOverlayMac/`
  - tracked deprecated source for secondary local macOS native-shell scaffolding and mock-telemetry review
  - no longer serves as a V1 parity gate, CI gate, or screenshot authority
  - mirrors parts of the Windows app structure for local macOS development
  - uses mock telemetry instead of iRacing
  - defaults to live mock telemetry analysis plus compact history; raw mock capture is opt-in with `TMR_MAC_RAW_CAPTURE_ENABLED=true`
  - defaults writable data to `~/Library/Application Support/TmrOverlayMac`
  - includes the same categories of logs, events, settings, runtime state, diagnostics, retention, session history, and overlay folder structure
  - mirrors the capture-health overlay fields and build freshness warning
  - can preview spoofed overlay states with `TMR_MAC_DEMO_STATES=true ./run.sh`
  - the mac menu exposes manual demo states for waiting, connected-without-capture, healthy live-analysis, healthy raw-capture, stale build, dropped frames, frames-not-written, disk-stalled, and capture-error
  - going forward, shared product and app-boilerplate changes should be mirrored into browser review and localhost first; mirror mac only when deliberately maintaining the deprecated scaffold
  - overlay windows use the shared mac `OverlayWindow` through `OverlayManager`, so each overlay id can restore its saved position/size
  - the mac fuel overlay uses live mock telemetry plus local user history, matching the Windows baseline-disabled default
  - the mac live mock uses the tracked four-hour Nürburgring baseline shape from race time zero at 4x speed for faster fuel/gap overlay iteration
  - for overlay development, Windows should stay production-facing and real-data-driven; browser review fixtures and browser-generated screenshots are the primary local iteration path
  - the mac mock race mirrors the Windows radar/gap feature behavior with synthetic all-class timing rows, multiclass approach traffic, weather bands, and driver handoff events, while Windows remains real telemetry only
  - the mac harness may mirror selected current Windows overlay review surfaces when that secondary scaffold is maintained, but it should not be used to define product parity
  - the mac harness mirrors the settings window schema and basic tabbed UI for visibility, scale/opacity when applicable, content/header/footer session gates, units, support capture, and a mock Support/performance snapshot tab; mac diagnostics bundles include matching telemetry-state/performance metadata stubs and recent mock performance JSONL logs
  - `swift run TmrOverlayMacScreenshots` still exists for legacy tracked artifacts under `mocks/`, but new branch review screenshots should come from `npm run screenshots:browser-review` and `npm run screenshots:localhost`
  - Design V2 component review overlays can be opened live from the menu bar or with `TMR_MAC_DESIGN_V2_COMPONENTS_DEMO=current|outrun ./run.sh`, and legacy component artifacts under `mocks/design-v2/components/` come from the same mac-harness views
  - screenshot waiting/unavailable fixtures should be isolated from local user history and cached live telemetry; for example, the fuel waiting preview uses an empty temporary history root so it cannot accidentally show stale stint rows from this machine

### Tests

- `tests/TmrOverlay.App.Tests/`
  - xUnit test project for non-UI logic
  - currently covers storage path resolution, localhost overlay options/routes, history path slugs, history maintenance/rebuild behavior, local file log writing, settings persistence/migration, diagnostics bundle contents, performance snapshot aggregation, retention cleanup, runtime-state markers, live fuel/proximity/gap derivation, fuel strategy calculations, and fuel view-model empty-state behavior

- `docs/edge-case-telemetry-logic.md`
- `docs/history-data-evolution.md`
- `docs/overlay-logic.md`
  - index for human-readable overlay and analysis logic notes; update the matching note whenever overlay derivation, display rules, visibility rules, or analysis rules change
- `docs/archive/live-overlay-24h-findings.md`
  - live 24-hour race findings mapped to model-v2, fuel, radar, and gap follow-up work

- `tools/validate_overlay_screenshots.py`
  - validates that expected screenshot PNG artifacts exist, match fixed dimensions where appropriate, and are not blank
  - should be run after regenerating screenshots, but does not replace visual review for scenario correctness, text clipping/overlap, misleading populated-empty states, or platform-specific layout behavior
- `tools/browser-review/render-screenshots.mjs`
  - captures the browser review settings app and browser overlay routes into `artifacts/browser-review-screenshots`, localhost overlay routes into `artifacts/localhost-screenshots`, or both into `artifacts/browser-localhost-screenshots` when run with `--surface all`
  - is the local screenshot generator for current browser review and localhost route review, with the two surfaces validated independently

- Validation standard going forward:
  - rendered screenshots are validation artifacts as well as design artifacts
  - `skills/tmr-overlay-validation/SKILL.md` contains the repo validation checklist, including the local C# duplicate-member / primary-constructor scope scanner for Windows-only compile-shape hazards
  - each scenario fixture should make both positive expectations and negative expectations obvious
  - waiting/unavailable/error paths should be deterministic and isolated from local machine state unless the scenario explicitly tests history fallback or support-path display
  - the same fixture-driven approach applies to collectors, diagnostics bundles, retention, updater, settings, and performance telemetry paths
  - code changes should include a stale-reference sweep across docs, mocks, tests, browser review assets, localhost routes, and repo skills before final validation so old behavior names/descriptions/API patterns do not survive alongside new implementation behavior
  - durable user-data schema changes should be treated as backwards-compatibility validation work in the same sweep: update version constants, migrations or compatible readers, docs, and the schema-compatibility test
  - behavior, calculation, default, source-label, fixture-data, and validation-semantics changes should update affected build test assertions and fixtures in the same pass; stale passing or failing assertions are stale references

- `.github/workflows/windows-dotnet.yml`
  - restores, builds, and tests `tmrOverlay.sln` on `windows-latest`
  - this is the current production-platform verification gate because the local Mac does not have `dotnet`

## Capture Format

See `docs/capture-format.md`.

Short version for opt-in raw capture:

- `telemetry-schema.json` stores variable metadata
- `telemetry.bin` stores raw frame payloads with a small per-frame header
- `latest-session.yaml` stores the latest raw session string
- `session-info/` preserves session-history snapshots
- `capture-synthesis.json` is an additive compact sidecar written after finalization when possible and bounded by `TelemetryCapture:MaxSynthesisMilliseconds`
- `ibt-analysis/*.json` is an additive compact sidecar set written when IBT analysis is enabled; missing sidecars on older captures are expected and startup recovery can fill them later
- `live-model-parity.json` is an additive compact sidecar/log artifact written after finalization; it compares current overlay inputs with model v2, summarizes raw/IBT signal availability, and includes `promotionReadiness`
- `live_model_v2_promotion_candidate` app events are emitted when a session-level parity artifact passes the configured frame-count, mismatch-rate, and coverage thresholds; treat this as a review signal before migrating overlays, not an automatic cutover

Raw capture format is preserved for diagnostics and future deep-dive analysis, but it is no longer the default production data path.

Compact edge-case telemetry artifacts are separate from this raw capture format. They live under `%LOCALAPPDATA%/TmrOverlay/logs/edge-cases` and are included in diagnostics bundles when present. Their clip list is bounded, but they retain dropped-observation counts and a final sampled context tail for late-session diagnostics.

## Telemetry Summary

See `telemetry.md`.

That file is the human-oriented outline of the current schema in three layers:

- event
- session
- car

It should be kept in sync with how we interpret raw capture artifacts for analysis work.

## Analysis State

Additional preserved context lives in:

- `references/fuel-overlay-context.md`
- `references/overlay-research.md`

Those files should be read when work shifts from capture plumbing into:

- fuel and stint logic
- telemetry interpretation
- overlay feature design
- overlay UX/layout direction

## Configuration

`src/TmrOverlay.App/appsettings.json`

Current keys:

- `TelemetryCapture:StoreSessionInfoSnapshots`
- `TelemetryCapture:RawCaptureEnabled`
- `TelemetryCapture:QueueCapacity`
- `TelemetryCapture:MaxSynthesisMilliseconds`
- `TelemetryEdgeCases:Enabled`
- `TelemetryEdgeCases:PreTriggerSeconds`
- `TelemetryEdgeCases:PostTriggerSeconds`
- `TelemetryEdgeCases:MaxClipsPerSession`
- `TelemetryEdgeCases:MaxFramesPerClip`
- `TelemetryEdgeCases:MinimumFrameSpacingSeconds`
- `LiveModelParity:Enabled`
- `LiveModelParity:MinimumFrameSpacingSeconds`
- `LiveModelParity:MaxFramesPerSession`
- `LiveModelParity:MaxObservationsPerFrame`
- `LiveModelParity:MaxObservationSummaries`
- `LiveModelParity:PromotionCandidateMinimumFrames`
- `LiveModelParity:PromotionCandidateMaxMismatchFrameRate`
- `LiveModelParity:PromotionCandidateMinimumCoverageRatio`
- `LiveModelParity:OutputFileName`
- `LiveModelParity:LogDirectoryName`
- `LiveOverlayDiagnostics:Enabled`
- `LiveOverlayDiagnostics:MinimumFrameSpacingSeconds`
- `LiveOverlayDiagnostics:MaxSampleFramesPerSession`
- `LiveOverlayDiagnostics:MaxEventExamplesPerSession`
- `LiveOverlayDiagnostics:MaxEventExamplesPerKind`
- `LiveOverlayDiagnostics:LargeGapSeconds`
- `LiveOverlayDiagnostics:LargeGapLapEquivalent`
- `LiveOverlayDiagnostics:GapJumpSeconds`
- `LiveOverlayDiagnostics:OutputFileName`
- `LiveOverlayDiagnostics:LogDirectoryName`
- `IbtAnalysis:Enabled`
- `IbtAnalysis:TelemetryLoggingEnabled`
- `IbtAnalysis:TelemetryRoot`
- `IbtAnalysis:MaxCandidateAgeMinutes`
- `IbtAnalysis:MaxCandidateBytes`
- `IbtAnalysis:MaxAnalysisMilliseconds`
- `IbtAnalysis:MaxSampledRecords`
- `IbtAnalysis:MinStableAgeSeconds`
- `IbtAnalysis:MaxIRacingExitWaitSeconds`
- `IbtAnalysis:MaxCandidateFiles`
- `IbtAnalysis:CopyIbtIntoCaptureDirectory`
- `IbtAnalysis:OutputDirectoryName`
- `SessionHistory:Enabled`
- `SessionHistory:UseBaselineHistory`
- `PostRaceAnalysis:Enabled`
- `Storage:UseRepositoryLocalStorage`
- `Storage:AppDataRoot`
- `Storage:CaptureRoot`
- `Storage:UserHistoryRoot`
- `Storage:BaselineHistoryRoot`
- `Storage:LogsRoot`
- `Storage:SettingsRoot`
- `Storage:DiagnosticsRoot`
- `Storage:TrackMapRoot`
- `Storage:EventsRoot`
- `Storage:RuntimeStatePath`
- `Logging:File:Enabled`
- `Logging:File:MinimumLevel`
- `Logging:File:MaxFileBytes`
- `Logging:File:RetainedFileCount`
- `Retention:Enabled`
- `Retention:CaptureRetentionDays`
- `Retention:MaxCaptureDirectories`
- `Retention:DiagnosticsRetentionDays`
- `Retention:MaxDiagnosticsBundles`
- `Retention:PerformanceLogRetentionDays`
- `Retention:MaxPerformanceLogFiles`
- `Retention:EdgeCaseRetentionDays`
- `Retention:MaxEdgeCaseFiles`
- `Replay:Enabled`
- `Replay:CaptureDirectory`
- `Replay:SpeedMultiplier`
- `LocalhostOverlays:Enabled`
- `LocalhostOverlays:Port`

Current default:

- writable storage resolves under `%LOCALAPPDATA%/TmrOverlay`
- raw captures default to `%LOCALAPPDATA%/TmrOverlay/captures` but are disabled unless `TelemetryCapture:RawCaptureEnabled=true`; post-session capture synthesis defaults to a 60-second timeout
- edge-case telemetry is opt-in and writes bounded compact artifacts under `%LOCALAPPDATA%/TmrOverlay/logs/edge-cases`
- live model-v2 parity is opt-in, samples clean frames at most once per second, keeps up to 600 sampled frames and 200 mismatch summaries, writes `live-model-parity.json` beside raw captures or under `%LOCALAPPDATA%/TmrOverlay/logs/model-parity` when no raw capture exists, and marks a session as a promotion candidate after at least 10,000 frames, mismatch-frame rate at or below 0.1%, and model coverage at or above 98% for observed legacy overlay-input families
- live overlay diagnostics are default-on, sample normalized overlay assumptions at most once per second, and write `live-overlay-diagnostics.json` beside raw captures or under `%LOCALAPPDATA%/TmrOverlay/logs/overlay-diagnostics` when no raw capture exists
- IBT analysis and iRacing telemetry logging default on for raw captures, request iRacing telemetry logging with the raw-capture switch, read candidates from `%USERPROFILE%/Documents/iRacing/telemetry`, wait at most 60 seconds for iRacing to stop writing before skipping, derive local track-map geometry after successful analysis while the default-on Track Map generation setting remains enabled, ship vetted bundled maps through app assets, and do not copy source `.ibt` files by default
- user history defaults to `%LOCALAPPDATA%/TmrOverlay/history/user`
- local logs default to `%LOCALAPPDATA%/TmrOverlay/logs`
- app events default to `%LOCALAPPDATA%/TmrOverlay/logs/events`
- settings default to `%LOCALAPPDATA%/TmrOverlay/settings`
- diagnostics default to `%LOCALAPPDATA%/TmrOverlay/diagnostics`
- track maps default to `%LOCALAPPDATA%/TmrOverlay/track-maps/user`
- runtime state defaults to `%LOCALAPPDATA%/TmrOverlay/runtime-state.json`
- baseline/sample lookup defaults off with `SessionHistory:UseBaselineHistory=false`
- localhost routes default on with `LocalhostOverlays:Enabled=true` and `LocalhostOverlays:Port=8765`

Environment override pattern:

- `TMR_Storage__UseRepositoryLocalStorage=true`
- `TMR_TelemetryCapture__RawCaptureEnabled=true`
- `TMR_TelemetryEdgeCases__Enabled=false`
- `TMR_Storage__CaptureRoot`
- `TMR_Storage__UserHistoryRoot`
- `TMR_Storage__AppDataRoot`
- `TMR_Storage__TrackMapRoot`
- `TMR_Replay__Enabled=true`
- `TMR_Replay__CaptureDirectory`
- `TMR_LocalhostOverlays__Enabled=false`
- `TMR_LocalhostOverlays__Port`

## Real-World Data Sources Already Identified

### `irdashies`

This repo is useful as a reference implementation and as a source of captured fixtures.

What was found in the cloned repo:

- `test-data/` contains 40 `telemetry.json` files and 40 `session.json` files
- sample session inspected:
  - track: Road Atlanta
  - config: Full Course
  - event/session type: Practice
  - build version: `2026.02.02.02`
  - driver count: 2
- sample telemetry keys include:
  - `SessionTime`
  - `SessionTick`
  - `Brake`
  - `BrakeRaw`
  - `CarIdxBestLapTime`
  - `CarIdxClassPosition`
  - `AirTemp`

Use this repo when we want:

- realistic JSON fixture shapes
- ideas for session and telemetry modeling
- proof that downstream overlays can be developed against stored data

### `irsdkSharp`

Useful mainly as the current integration layer. The cloned repo also includes `tests/testdata/session.ibt`, which may be useful later for offline parsing or replay-style experiments.

### Official iRacing SDK docs

Treat the docs as schema/reference material, not as a ready-made real-world dataset source.

## Known Limitations

- The current machine does not have `dotnet`, so Windows build/test verification still needs to happen on a .NET-equipped machine.
- The local mac Swift package is deprecated secondary scaffolding and is not a V1 release gate.
- No general-purpose replay/decoder tool exists yet for `telemetry.bin`; targeted analysis scripts exist under `tools/analysis/`.
- v0.16 makes Velopack the installer/update channel using public GitHub Releases as the feed. Installed builds check with Velopack `GithubSource` without embedding a token; portable/dev runs skip update checks because they do not have a Velopack install identity. Signing remains pending before broader production distribution.
- The PR workflow runs browser tests, browser review screenshot generation/upload, restore/build/test, tracked screenshot validation, Windows screenshot expectation checks, Windows screenshot artifact generation/validation, a self-contained publish dry run with package audit, and a Velopack pack dry run. The release workflow audits the publish folder for accidental repo/dev-folder leaks, emits a package manifest, packs Velopack installer/update assets, keeps the portable zip fallback, and keeps user data under the app-data root instead of the install folder.
- Because the app-data root persists across portable installs, durable settings/history schema changes must include version bumps plus migrations or compatible readers. Incompatible/future history is skipped and left on disk instead of being fed to overlays.
- Overlay modules now live under `src/TmrOverlay.App/Overlays/`; settings, standings, fuel-calculator, relative, track-map, stream-chat, flags, session/weather, pit-service snapshot, input/car-state, car-radar, and gap-to-leader native overlays are wired. Garage Cover is wired as a localhost-only privacy surface, and app status lives in Support/diagnostics rather than a standalone user overlay. Scripts for localhost routes live with their overlay modules. Remaining future product surfaces should be added deliberately rather than as placeholder overlay tabs.
- Pure models and calculations have started moving into `src/TmrOverlay.Core/`; Windows remains the production app/runtime, while browser review is the primary local mock-telemetry development surface.
- The root-level launcher is `TmrOverlay.cmd`, not a standalone copied `.exe`, because a normal framework-dependent .NET build needs its companion output files.
- The primary analyzed real capture is the 4-hour Nürburgring VLN race capture. It proved that teammate stints retain `CarIdx*` timing/position data but do not expose direct scalar fuel fields.

## Recommended Next Steps

1. Validate at least one installed Velopack update from public GitHub Releases before adding active download/apply/restart controls.
2. Validate portable and Velopack upgrade behavior against existing `%LOCALAPPDATA%\TmrOverlay` settings/history/diagnostics data on Windows before broad teammate handoff.
3. Decide signing before broad distribution. Private teammate builds can remain unsigned, but production sharing should sign the executable or package.
4. Decide whether the portable zip remains a normal release asset long term or becomes support fallback only.
5. Keep update prompts passive and off-simulator until teammate installer testing proves the release channel reliable.
6. Treat overlay lifecycle/timer efficiency as an important V1.x foundation: hidden, session-filtered, or fully faded native overlays should pause high-frequency refresh timers through a shared lifecycle contract, then resume cleanly when visible. The future performance backlog should also cover shared cadence timers, sequence-aware refresh skips, paint/GDI/font/theme caching, Track Map static-raster caching, Input/Radar hot-path cleanup, localhost no-client reduction, disk write batching, settings-save debouncing, diagnostics/store/history caching, layout churn reduction, and a repeatable performance harness.
7. Identify remaining v1/legacy overlay slices and choose the next safe model-v2 migration target without pulling fuel/gap analysis products into the simple-overlay path prematurely.
8. Keep collecting radar diagnostics for suppressed non-local focus, local progress-missing, side-without-placement, and multiclass cases before expanding beyond local in-car radar.
9. Harden Track Map after first real usage: current-map quality/status, manual rebuild/replace UX, bundled map QA, confidence/stale/pit-lane screenshot states, and pit-lane-aware live markers when reliable telemetry exists.
10. Decide the Overlay Bridge v2 shape for teammate-to-teammate data sharing: enable/disable, allowed peers, schema version, connection health, and which normalized model-v2 context should become the trusted peer contract.
11. Expand Stream Chat beyond the current Streamlabs-widget localhost source and public Twitch channel modes when needed, keeping provider auth, moderation, rate limits, and offline preview states separate from iRacing telemetry.
12. Treat overlay builder as a later creator/development platform on top of design-v2 primitives and the bridge schema, not as a prerequisite for the first hand-authored production overlays.
13. Improve historical aggregation and confidence/source tracking as more user sessions are collected.
14. Keep raw capture available for diagnostics, but avoid making it the normal user data path.
15. Expand post-race strategy review/export beyond the first saved-analysis view; see `docs/post-race-strategy-analysis.md`.
16. When summary or other durable history shapes change, add ordered migrations or compatible readers to the historical data maintenance flow, update `HistorySchemaCompatibilityTests`, and keep car/track/session history higher priority than performance/logging data.

## Files Most Likely To Change Next

- `src/TmrOverlay.App/Diagnostics/AppDiagnosticsStatusModel.cs`
- `src/TmrOverlay.App/Overlays/SettingsPanel/SettingsOverlayForm.cs`
- `src/TmrOverlay.App/Overlays/Styling/OverlayTheme.cs`
- `src/TmrOverlay.Core/`
- `src/TmrOverlay.App/Localhost/`
- `src/TmrOverlay.App/Shell/NotifyIconApplicationContext.cs`
- `src/TmrOverlay.App/History/`
- `src/TmrOverlay.App/Performance/AppPerformanceState.cs`
- `src/TmrOverlay.App/Telemetry/TelemetryCaptureHostedService.cs`
- `src/TmrOverlay.App/Telemetry/TelemetryCaptureSession.cs`
- future V1.x Timing Tower overlay files once that race-only compact timing surface is pulled forward
- `telemetry.md`
- `skills/tmr-overlay-context/references/fuel-overlay-context.md`
- `skills/tmr-overlay-context/references/overlay-research.md`
- `docs/capture-format.md`
- `README.md`
