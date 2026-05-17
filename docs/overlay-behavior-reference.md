# Overlay Behavior Reference

This is the human-readable review file for how each overlay decides what to show. It is intentionally written for teammates reviewing behavior, not for C# implementation details.

For visual decision paths, see [Overlay Flow Diagrams](overlay-flow-diagrams.md).

## Global Rules

Nomenclature:

- **Browser** means the local review UI opened directly in a browser, such as `/review/app` or `/review/overlays/<overlay-id>`.
- **Localhost** means the OBS source URL a user can add to OBS, such as `/overlays/<overlay-id>`.
- **Native** means the Windows application and Windows overlay windows.

All managed native overlays use the same outer visibility rules:

1. The Settings window opens at startup. Driving/support overlays stay hidden unless the user enables them.
2. An overlay is eligible to show when its setting is enabled and its content/header/footer session gates allow the current session kind.
3. The General-tab Show Preview mode supplies deterministic Practice, Qualifying, or Race telemetry only. It does not enable hidden overlays, bypass content/header/footer session gates, move windows, change opacity, change topmost state, or force Stream Chat open.
4. Race-data overlays fade out when live telemetry is disconnected, not collecting, or stale. Status and Stream Chat are not race-data overlays for this fade policy.
5. Local-only overlays also require their declared live context before their native windows are shown. Radar and Inputs require local player in-car focus; Fuel Calculator and Pit Service require local player focus plus active in-car or pit context.
6. Settings preview controls can show an overlay for configuration review, but that is treated as a settings preview, not a normal runtime visibility decision.
7. Localhost pages read the same normalized overlay models where practical. The browser review server uses deterministic fixtures and is a development/review surface, not the production iRacing runtime.

## Settings

Purpose: Control the app, overlays, diagnostics, updates, localhost hints, and support bundle workflow.

Behavior:

- Always opens at startup and is the normal control surface.
- Keeps a fixed application-window size.
- General owns units, session preview, broad app controls, and support/diagnostics.
- Each overlay tab owns enabled state, size/scale, opacity where useful, content settings, header/footer controls where supported, and localhost sizing hints.
- Overlay options are stored in keyed `OverlaySettings.Options` entries so new header, footer, and content controls can be added without expanding the durable settings model each time.

## Standings

Purpose: Show the live scoring table around the reference car while preserving multiclass context.

Inputs:

- Primary: `LiveTelemetrySnapshot.Models.Scoring`
- Fallback: `LiveTelemetrySnapshot.Models.Timing`
- Driver labels and class colors from the driver directory and scoring rows

Behavior:

- Waits for fresh telemetry, then waits for scoring or timing rows.
- Uses the scoring model when available because it carries grouped multiclass standings.
- Selects the reference car from scoring/timing focus rows or the driver directory focus reference.
- In race sessions, can render scoring/grid rows before green while keeping gap/interval cells unavailable until positive timing evidence exists.
- Keeps class groups in official table order instead of reordering the table around the reference class.
- Shows focused-class rows plus bounded rows for other classes when Multiclass sections are enabled.
- Highlights the reference row, pit rows, class headers, and partial rows.
- If scoring is missing, falls back to timing rows ordered by class position, overall position, class-leader gap, lap gap, then car index.

Reviewer checks:

- Does the reference class stay visible under row limits?
- Are other classes present without taking over the table?
- Do practice/qualifying/race sessions avoid misleading pre-lap rows?

## Relative

Purpose: Show nearby cars ahead and behind the reference car.

Inputs:

- Primary: `LiveTelemetrySnapshot.Models.Relative.Rows`
- Reference from model-v2 focus rows; no player fallback when focus is unavailable
- Driver directory and scoring rows for labels, numbers, classes, and colors

Behavior:

- Waits for fresh telemetry and relative data.
- Builds a reference row when a focus car index is known.
- Takes a configured number of cars ahead and behind.
- Ahead rows are sorted nearest-first for selection, then displayed farthest-to-nearest above the reference row.
- Behind rows are sorted nearest-first below the reference row.
- Gap text prefers usable relative seconds. Partial rows are marked when neither timing nor placement evidence is fully usable.
- Reference position is suppressed when the current session kind makes official position misleading.

Reviewer checks:

- Are cars ordered naturally around the reference car?
- Are partial/fallback rows visually distinct enough without making normal rows noisy?
- Does the reference row stay stable when scoring is missing?

## Fuel Calculator

Purpose: Estimate fuel range, stops, and final stint shape without presenting unproven strategy advice as fact.

Inputs:

- `LiveLocalStrategyContext`
- `FuelStrategySnapshot`
- User and baseline session history for burn-rate context
- Unit system setting

Behavior:

- Requires a local active driver/team context. In native overlay mode, focus on another car, unavailable focus, missing player car, garage/setup context, and no active local/pit context keep the enabled window hidden; localhost/model callers show `waiting for local fuel context`.
- Shows planned race laps, stint count, and final stint target when those are known.
- Otherwise shows current fuel, race laps remaining, and additional fuel needed.
- Uses measured completed green-lap fuel deltas first, exact history second, and keeps instantaneous `FuelUsePerHour` diagnostic-only.
- In practice and qualifying, shows a `Practice Usage` or `Quali Usage` row with measured completed-lap min/avg/max and sample count; it waits for completed-lap evidence and does not use history or instantaneous burn for that row.
- Displays stint rows for meaningful stints or a no-fuel-stop finish state.
- Source text shows fuel-per-lap source, full-tank range, history source, historical min/avg/max when available, and leader/class gap context.
- Rhythm optimization and tire-service tradeoff advice are hidden for V1.

Reviewer checks:

- Does the overlay distinguish measured green-lap burn from historical/baseline burn?
- Does it avoid using instantaneous `FuelUsePerHour` as primary strategy burn?
- Do practice/qualifying usage rows stay observational and wait for completed green laps?
- Does the final stint display avoid implying an unnecessary fuel stop?
- Are units correct in Metric and Imperial modes?

## Track Map

Purpose: Draw the track, sector highlights, local car, and nearby/opponent markers.

Inputs:

- Track map documents from bundled assets or user-generated IBT maps
- `LiveTelemetrySnapshot.Models.TrackMap`
- Driver/class context for markers

Behavior:

- Uses the best map for the current track identity when available.
- Falls back to a deterministic placeholder when no map exists.
- Reloads map identity periodically so newly generated maps can appear without restarting.
- Draws racing line, pit lane when available, start/finish, sector boundaries, sector highlight state, and car markers.
- Smooths marker progress so live telemetry jitter does not make cars visually jump.
- Keeps IBT-derived raw source files out of app storage; stored maps contain derived display geometry and quality metadata only.

Reviewer checks:

- Does the fallback avoid pretending to be a real track?
- Do sector highlights match the model state?
- Do markers move smoothly without hiding stale or unavailable telemetry?

## Stream Chat

Purpose: Show stream chat messages without depending on iRacing telemetry.

Inputs:

- Stream Chat provider settings
- Twitch IRC websocket when configured
- Streamlabs widget settings where supported

Behavior:

- Defaults disabled and not configured.
- Does not fade with telemetry availability.
- Is not forced open by diagnostics or session preview.
- Connects only when the provider/channel settings are valid.
- Keeps a bounded message list and visible message budget.
- Shows unavailable/not-configured state instead of stealing focus or opening because telemetry preview is active.

Reviewer checks:

- Hidden Stream Chat must stay hidden during Show Preview and diagnostics.
- A not-configured chat should not look like a broken telemetry overlay.
- The overlay should not take focus from the Settings window or the app UI.

## Garage Cover

Purpose: Provide an OBS/localhost privacy cover for garage/setup screens.

Inputs:

- Garage/setup visibility from live race-event state
- User-imported cover image when configured
- Browser-source route state

Behavior:

- Localhost-only.
- Fails closed: if telemetry is missing, stale, unavailable, or the garage is visible, it renders the cover/fallback rather than exposing garage content.
- Uses the configured image when available; otherwise renders a deterministic fallback.
- Does not appear as a normal native driving overlay.

Reviewer checks:

- Does missing telemetry cover the screen instead of exposing setup details?
- Does the localhost route stay useful even without a user image?

## Flags

Purpose: Show active race-control flags.

Inputs:

- Normalized flag/race-event state from live telemetry
- Flag category settings

Behavior:

- Managed by normal overlay visibility, but internally hides when there are no displayable flags.
- Requires a known session kind before showing through normal runtime logic.
- Uses transparent/click-through native behavior.
- Filters active flags through enabled categories before rendering.
- Suppresses itself while the Settings overlay is active when needed to protect Settings input and z-order.

Reviewer checks:

- Does the overlay stay gone when there are no active flags?
- Do category toggles hide only the intended flag types?
- Does it avoid blocking Settings interaction?

## Session / Weather

Purpose: Show session clock, lap context, track identity, temperatures, surface, sky, wind, humidity, and fog.

Inputs:

- `LiveTelemetrySnapshot.Models.Session`
- Weather, race progress, and race projection models
- Unit system setting

Behavior:

- Waits for fresh telemetry and session data.
- Formats elapsed/remain clocks compactly when appropriate.
- Shows lap totals and remaining lap context only when values are finite and plausible.
- Shows track display name plus track length when known.
- Converts temperatures and wind speed by unit system.
- Highlights changed weather/surface values with info tone.

Reviewer checks:

- Are invalid/unbounded lap counts hidden or clearly represented?
- Are Metric and Imperial conversions correct?
- Does the overlay avoid showing stale weather as live?

## Pit Service

Purpose: Summarize session context, pit release/service state, requested service, and tire-service evidence.

Inputs:

- Normalized pit/fuel service model
- Fuel, tire, repair, pit-road, and pit-stall state
- Unit system setting

Behavior:

- Requires fresh telemetry, local active driver/team context, and pit-service data. In native overlay mode, focus on another car, unavailable focus, missing player car, garage/setup context, and no active local/pit context keep the enabled window hidden; localhost/model callers show `waiting for local pit-service context`.
- Still renders during the local pit window: on pit road, in the pit stall, or while service is active.
- Groups rows as `Session`, `Pit Signal`, `Service Request`, and `Tire Analysis` in both native and localhost surfaces.
- Production localhost pages build the Pit Service model from the same normalized live snapshot and `PitServiceOverlayViewModel` path as native Design V2; spoofed all-row Pit Service data is limited to browser tooling.
- Shows compact time plus lap context when telemetry exposes plausible values, so lap-based races can display remaining/total laps alongside the clock.
- Shows release and pit-service status in `Pit Signal`; the old pit-location row is intentionally omitted because it duplicated lower-value context.
- Shows service requests as segmented rows: fuel request is requested state plus selected fuel amount, tearoff is requested state, repair is required/optional time, and fast repair is selected state plus remaining availability.
- Shows tire analysis as per-corner chip cells for compound, change/keep, set limits/availability/used state, cold pressure, temperature, wear, and distance when those rows are enabled and available.
- Uses warning/info/success tones for release, repair, and tire availability/change state rather than treating all pit messages equally.
- Formats fuel volume according to the selected unit system.
- Does not show estimated fuel or in-car setup rows in this pass; estimated fuel belongs to the future Fuel Calculator V2 shared strategy model, and ARB/wing rows need deliberate raw-capture proof before promotion.

Reviewer checks:

- Does the overlay distinguish requested service from active/completed service?
- Are repair and tire states understandable under partial telemetry?
- Do the localhost and native Design V2 Pit Service layouts stay functionally identical?

## Input / Car State

Purpose: Show local car controls and mechanical state.

Inputs:

- `LiveTelemetrySnapshot.Models.Inputs`
- Local car state, engine warnings, gear, RPM, speed, temperatures, pressures, and voltage
- Unit system setting

Behavior:

- Waits for fresh telemetry, then requires the focused local player in-car context and local car telemetry.
- Shows speed, gear/RPM, pedals, steering, warnings, electrical state, cooling, and oil/fuel pressure.
- Converts speed, temperature, and pressure by unit system.
- Treats engine warnings as warning-tone rows.
- Suppresses shared header/footer controls because this overlay is graph/content-focused.

Reviewer checks:

- Are missing values rendered as unavailable rather than zero?
- Do warnings stand out without making normal telemetry noisy?

## Car Radar

Purpose: Show local side pressure and nearby car placement.

Inputs:

- Spatial/radar model
- Local reference car progress
- Nearby car placement and side-signal state

Behavior:

- Waits for fresh telemetry.
- Focuses on local in-car radar. Spectator/teammate focus or missing local context suppresses normal radar display.
- Uses side occupancy signals for immediate left/right pressure when available.
- Uses nearby placement candidates for visual car positions when telemetry is usable.
- Degrades gracefully when only timing evidence is available; diagnostics can record those cases, but the end-user radar should not pretend timing-only data is exact spatial placement.

Reviewer checks:

- Does local-only suppression avoid showing wrong radar for spectator/teammate focus?
- Are left/right pressure states correct when `CarLeftRight` changes?
- Does the overlay avoid overclaiming exact position from partial data?

## Gap To Leader

Purpose: Show gap trend to leader or class leader.

Inputs:

- Timing/leader-gap model
- Race/session kind
- Recent gap samples retained in the overlay form

Behavior:

- Waits for fresh telemetry and usable gap data.
- Tracks bounded recent gap points for a compact trend graph.
- Handles race and non-race gap semantics separately because practice/qualifying gaps can mean different things than race order.
- Avoids treating huge lap-equivalent gaps or discontinuities as ordinary trend movement.
- Uses diagnostics artifacts for future tuning, but the production overlay only displays the current bounded model state.

Reviewer checks:

- Does the graph avoid wild jumps on session resets or pit/context changes?
- Does the status text make race vs non-race gap source clear enough?

## Status

Purpose: Give support/development visibility into app state.

Behavior:

- Not treated as a race-data overlay.
- Shows telemetry/capture/support state rather than driving data.
- Should stay separate from future product surfaces unless deliberately promoted through a product pass.

Reviewer checks:

- Does it help diagnose startup/capture state without becoming an end-user driving overlay?

## Browser Review Surface

Purpose: Fast local review for browser UI, localhost/OBS parity, and broad app layout.

Behavior:

- `npm run review:browser` starts a local fixture-backed server.
- `/review/app` shows the Settings shell plus a full overlay validator stage.
- `/review/settings/general` shows the General tab preview controls.
- `/review/overlays/<overlay-id>` renders browser review pages, and `/overlays/<overlay-id>` renders localhost pages from the same assets used by Windows localhost routes.
- Preview query parameters provide deterministic mock telemetry; they do not change production native overlay visibility rules.

Reviewer checks:

- Browser review is the right loop for layout, JavaScript behavior, and localhost parity.
- Windows remains the authority for native focus, topmost, click-through, iRacing SDK capture, installer/update behavior, and WinForms screenshots.
