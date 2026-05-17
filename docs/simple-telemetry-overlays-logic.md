# Simple Telemetry Overlays Logic

The simple telemetry overlays are small read-only windows into normalized iRacing telemetry. They use `LiveTelemetrySnapshot.Models` directly and keep source/evidence text quiet in the normal case.

Session / Weather and Pit Service use the shared `SimpleTelemetryOverlayForm` shell. Flags and Input / Car State now use custom lightweight forms because their first useful shape is graphical rather than table-first.

- availability uses `OverlayAvailabilityEvaluator` so native and localhost overlays share the same connected/collecting/fresh rules
- snapshots older than 1.5 seconds show a waiting state
- disconnected telemetry shows `waiting for iRacing`; connected but not collecting telemetry shows `waiting for telemetry`
- unexpected refresh/render exceptions show a compact visible error and are logged
- rows stay table-shaped so the window does not resize as values appear or disappear
- source footers are validation/admin chrome, not default user-facing overlay furniture

## Flags

`Flags` reads `LiveSessionModel.SessionState` and `SessionFlags`. It renders as a compact transparent icon-only overlay with procedurally drawn waving flags rather than a table, text label, or full-screen border. It requires recognized live session context before the window is shown; generated mac screenshots and demo states can still force review visuals.

The settings tab lets the user choose which flag categories can display and uses the shared scale control for overlay size. There are no per-flag display timers: each flag paints while the current telemetry state maps to an enabled user-facing flag category and clears when that telemetry state clears or changes to a disabled category. Multiple meaningful categories can render at once, so persistent states such as white, meatball, and black can stay up while transient yellow/debris/blue states appear or disappear around them.

The transparent native flags window is click-through/no-activate and is also hidden while the Settings window is active. Diagnostics bundles include UI-freeze-watch metrics that make Windows validation distinguish a real UI-thread stall from an overlay window/input-capture problem.

Background SDK bits such as `serviceable` and `start hidden` are decoded for diagnostics but do not trigger the overlay by themselves. User-facing categories are:

- green-held/start-ready/start-set/start-go; plain steady-state green running does not show by itself
- blue
- yellow/caution/debris/one-to-green
- critical red/black/repair/furled/disqualification/driver-flag states
- finish/countdown/white/checkered

Flag design is semantic: green for green-flag running, amber for yellow/caution, blue for blue flags, white/checkered for finish/countdown, red for stopped sessions, black for penalties, and black-with-orange-disc for repair/meatball states. Internal labels are retained for diagnostics and tests, but the user-facing renderer does not show penalty text until telemetry capture has a reliable source for exact instructions such as slow-down time or drive-through/stop-go penalty type.

## Session / Weather

`Session / Weather` combines `LiveSessionModel` and `LiveWeatherModel`. It remains a standalone driver/engineer/spotter overlay, and the same session/weather fields are also candidates for optional compact header/footer context in other overlays.

It displays:

- session type/name/team label
- elapsed and remaining clock
- remaining and total laps
- track display name and length
- air and track temperatures in the selected unit system
- track wetness, declared-wet state, and rubber state
- skies/weather type/current precipitation
- wind speed/direction plus humidity/fog when telemetry exposes them

Wet or declared-wet states use an info tone. Declared-wet surface mismatches use a warning tone. Temperature, surface, sky/rain, and wind/atmosphere rows get a short info-tone highlight after their formatted value changes so the driver or spotter can notice condition shifts without turning the overlay into a warning panel.

The live model carries additional atmospheric fields such as pressure and solar angle for future compact widgets/analysis. iRacing telemetry in current captures has not exposed a reliable forecast channel, so the overlay does not display forecast text. Compact edge-case diagnostics dynamically include forecast-like raw channels if a future SDK schema exposes a variable whose name or description contains `forecast`.

## Pit Service

`Pit Service` reads `LiveFuelPitModel` and is read-only in this branch. Product-wise it belongs with Fuel, not Input / Car State: for V1 it is a local active driver/team context overlay. Native overlay management hides the window when the camera focus is unavailable, focused on another car, in garage/setup context, or otherwise lacks a valid local player car; localhost/model callers still receive a waiting state for the same condition. It still renders while the local car is on pit road, in the pit stall, or service is active because that is the primary moment for this overlay.

It displays:

- `Session`: compact clock and lap context, including time remaining plus known remaining/total race laps when telemetry exposes plausible values
- `Pit Signal`:
  - release signal with red/yellow/green row tone:
    - `go` when `PlayerCarPitSvStatus` reports service complete
    - `service active` while pit service is in progress or `PitstopActive` is true
    - `repair active` while required repair time remains
    - `optional repair` while optional repair time remains without required repair or active service
    - `go (inferred)` when the car is in the stall, service is not active, and no repair timer is blocking release but the explicit service-complete status is unavailable
  - decoded pit service status from `PlayerCarPitSvStatus`
- `Service Request`:
  - fuel request as `Requested` plus selected add amount in the chosen unit system
  - windshield tearoff requested state
  - required and optional repair time, styled as a problem row when repair is needed
  - fast repair as selected state plus remaining availability; local/team used counters stay diagnostic/detail evidence rather than primary overlay content
- `Tire Analysis`:
  - current/requested compound and change state
  - tire set limit, available set count, used/change state, requested cold pressure, live temperature, wear, and distance when those content rows are enabled and telemetry exposes values
  - per-corner cells use the same compact chip treatment as other service rows; tire change is success/green, keep/static is info/blue, and zero available sets is error/red

Requested-service rows get a short info/blue highlight when their formatted value changes, such as fuel amount, tire request, repair time, tearoff, or fast repair selected/availability state. Red/yellow/green release and repair severity still wins over the change highlight.

Pit service details are player/team-car telemetry, not competitor-wide telemetry. Current captures expose per-car pit-road and fast-repair-used arrays (`CarIdxOnPitRoad`, `CarIdxFastRepairsUsed`) plus general per-car track-surface/timing arrays, but not per-car requested fuel, tire selections, repair timers, service-active state, or service-complete status. Standings/Relative can use those per-car arrays for pit-road context, but this overlay stays anchored to the local player/team car service snapshot and does not display while the user is intentionally following another car.

Tire remaining cells render only when the SDK proves a limited tire inventory with a positive `PlayerCarDryTireSetLimit`. In limited sessions, `0` remaining is valid and should be shown as a warning. In unlimited/open-practice contexts, non-zero `*TiresAvailable` or `*TireSetsAvailable` values can reflect service/menu availability rather than race inventory, so those remaining cells stay hidden. The visible remaining value is treated as one shared inventory count, using `TireSetsAvailable` first or matching corner/side counters only when they agree. The raw per-corner, side/axle, and total counters are still captured for diagnostics because limited NASCAR captures showed counters can diverge during service, which may reflect one-wheel-at-a-time service sequencing rather than separate inventory buckets.

Live overlay diagnostics count pit-service signal frames, request frames, value-change frames, non-player-focus frames, off-track frames, and local-strategy suppression reasons. That is intentionally diagnostic rather than product logic: a future Fuel/Pit Service V2 pass should decide how to present degraded-but-useful team strategy context for teammates, spectators, and garage/setup states after captures prove which channels remain trustworthy in those modes.

The overlay does not send iRacing pit commands. A future pit crew/engineer overlay should own command-capable controls for refuel amount, tire/repair/tearoff choices, and operator workflow so read-only pit telemetry and active simulator control do not get mixed. Suggested refuel numbers should consume the shared Fuel strategy/race-progress model instead of recalculating laps-to-go inside Pit Service; this first pass intentionally omits estimated fuel from Pit Service. In-car setup rows such as ARB and wing are still hidden until a deliberate raw capture proves live request/change telemetry for those values. The May 2026 NASCAR capture did prove NASCAR wedge/weight-jacker pit request channels (`dpWeightJackerLeft`/`dpWeightJackerRight`), but those are not ARB/wing signals and need a separate product pass before becoming Pit Service rows. Future spotter/engineer surfaces should normalize viewer context from `IsOnTrack`, `IsOnTrackCar`, `IsReplayPlaying`, `IsInGarage`, `IsGarageVisible`, `CamCarIdx`, and camera group data so in-car, replay/watching, garage, and other-car focus modes do not accidentally reuse driver-only assumptions. Do not show teammate/spotter pit-value-change notifications until a capture proves which telemetry signal distinguishes a teammate changing service requests from the local user changing them.

Browser review fixtures should exercise the same `Pit` column now exposed by the production Windows standings overlay; the deprecated mac harness may still carry legacy synthetic pit-window scenes, but it is no longer the product review surface.

## Input / Car State

`Input / Car State` reads `LiveInputTelemetryModel`. It renders for the local player while in-car on track or in the pit-road/pit-stall context, and still waits while the local player is in garage/setup, focused on another car, disconnected, stale, or missing input telemetry.

It displays:

- speed in the selected unit system
- gear and RPM
- throttle/brake/clutch-control percentages
- steering angle
- ABS activity when `BrakeABSactive` explicitly reports that ABS is reducing brake pressure
- raw engine warning bits
- voltage
- water temperature
- oil temperature plus oil/fuel pressure

The default native and localhost input overlays are graph-first. The line graph remains the primary surface, while current-value widgets live in a right-side content rail. Pedals are shown as vertical percentage bars, steering remains a wheel visualization, and gear/speed are numeric. Steering telemetry is treated as radians from iRacing and formatted to degrees only for display. iRacing `Clutch`/`ClutchRaw` report clutch engagement (`1` means fully engaged), so `LiveInputTelemetryModel.Clutch` inverts the preferred raw channel and displays driver clutch control input (`1` means pressed/disengaging). The graph trace uses a smoothed path for readability without changing the underlying 0..1 pedal/control samples. Throttle, brake, clutch, steering, gear, and speed are independent Content-tab options that can be turned on or off. Input / Car State does not expose Header/Footer settings in this branch.

Engine warning bits use a warning tone. Otherwise this overlay stays neutral and acts as a compact local-driver input surface. TC firing is deliberately not shown yet: current captures expose TC toggle/adjustment-style fields, but not a proven traction-control intervention signal equivalent to `BrakeABSactive`. Current captures also expose `Clutch` and `ClutchRaw`, while future clutch-like schema fields such as dual-clutch channels are watched diagnostically before being promoted into the production overlay.
