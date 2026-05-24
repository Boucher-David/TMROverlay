# Overlay Modules

Each overlay type should live in its own folder under `Overlays/`.

Overlay modules own presentation-specific code:

- default size and identity
- view/form implementation
- overlay-specific labels and layout
- overlay-specific persisted settings

Every driving overlay should be draggable and should remember its position, size, opacity, and always-on-top preference by overlay id. On Windows, derive overlay forms from `Abstractions/PersistentOverlayForm` and register child controls that should act as drag handles. Product overlays should keep the tool-window/always-on-top behavior that lets them sit over the sim; the settings window opts out so it behaves like a normal desktop window, recenters whenever it opens, and does not persist user placement. Browser review and localhost should mirror those settings contracts; the deprecated mac harness is no longer a product parity target.

Put common Windows overlay colors, typography, and chrome/layout constants in `Styling/OverlayTheme.cs`. Shared defaults for durable settings and Design V2 color roles start in `shared/tmr-overlay-contract.json`, which is loaded before user-local `overlay-theme.json` overrides. Keep chart/car-specific palette decisions next to the drawing code when the color is part of the data visualization itself. Browser, localhost, and native parity should usually be fixed by aligning shared role tokens, sizes, weights, row heights, padding, and state colors before changing overlay-specific domain drawing. The tracked mac harness may mirror the shared contract for secondary review when maintained, but it is not the production parity gate.

Overlay modules should not talk directly to iRacing or raw capture files. They should consume shared state, metrics, and history services from the app/core layers so multiple overlays can reuse the same telemetry interpretation.

The tray shell should not construct overlay windows directly. Add new overlay windows through `OverlayManager` so multiple overlay types can run together and share the same lifecycle/settings behavior.

Shared data sources intended for overlays:

- `src/TmrOverlay.Core/Telemetry/Live/ILiveTelemetrySource` - latest normalized live frame and derived live fuel/proximity/gap state. Providers should write through `ILiveTelemetrySink`.
- `History/SessionHistoryQueryService` - user aggregate lookup by car/track/session combo, with opt-in baseline/sample lookup.
- `Telemetry/TelemetryCaptureState` - collector health/status only, not product metrics.

Current modules:

- `SettingsPanel/` - branded fixed-size tabbed settings window with the TMR logo, `Tech Mates Racing Overlay` header, and flat vertical left tabs for user-managed visibility, scale when applicable, units, support/log/performance access, runtime diagnostic capture, app-health status, and overlay-specific display options
- `FuelCalculator/` - fuel/stint strategy overlay with tire-stop guidance from history
- `Standings/` - compact scoring-first table from `LiveTelemetrySnapshot.Models.Scoring`, with class grouping, configurable other-class row counts, live timing enrichment, partial-live row handling, and timing fallback when scoring is unavailable
- `Relative/` - telemetry-first relative table from `LiveTelemetrySnapshot.Models.Relative`, with stable configured ahead/reference/behind row slots, live or inferred display-time gaps, timing fallback labeling, and quiet source text unless rows are waiting or degraded
- `TrackMap/` - transparent map-only track surface with generated local geometry when available, circle fallback otherwise, live car dots placed from lap-distance progress, scoring-backed marker labels/colors when available, and white fallback for missing class colors
- `StreamChat/` - scale-controlled native read-only Twitch chat overlay plus localhost route for one saved chat source at a time: Streamlabs Chat Box widget URL or public Twitch channel chat
- `GarageCover/` - scale-owned localhost-only opaque streamer privacy surface with settings-tab image preview/import/clear controls, crop-to-cover image fitting, app-owned imported cover images, bundled stock fallback art, and fail-closed Garage/setup-screen detection
- `CarRadar/` - transparent circular local-player in-car proximity radar from `LiveTelemetrySnapshot.Models.Spatial`, using fresh local player/team progress, `CarLeftRight`, and physically placed `CarIdx*` progress arrays, hidden for missing player, explicit non-player focus, garage, and pit contexts, with car rectangles placed only from physical lap-distance meters, side occupancy rendered only when local `CarLeftRight` can attach to a close physical candidate, side-warning cars suppressed from the center lane instead of duplicated, neutral-white car rectangles fading in between radar entry and the yellow-warning threshold, and proximity color moving through yellow toward saturated alert red only inside the close bumper-gap warning buffer around the local car
- `GapToLeader/` - four-hour in-class gap trend graph from `CamCarIdx`, `CarIdxF2Time`, standings, and `CarIdx*` progress, with a bounded in-memory trace, adaptive Y-axis scaling, left-side axis labels, lap reference lines, weather bands, driver/leader-change markers, and endpoint position labels for the focused car context

Windows overlay code is production-facing and should stay real-data-driven. Use browser review fixtures and browser-generated screenshots for routine local design iteration; keep the tracked mac harness only as deprecated secondary scaffolding when a native-shell experiment explicitly needs it.

Expected future modules:

- `CompetitionDistanceGraph/`
- `Weather/`
