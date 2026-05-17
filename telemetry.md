# Telemetry Schema Summary

This file describes the current raw capture model in three layers:

- `event`: capture-level and weekend-level context
- `session`: the active iRacing session state
- `car`: static vehicle identity plus dynamic usage telemetry

The current production path analyzes live SDK data and stores compact session history. Raw capture remains available as an opt-in diagnostic/development mode; when enabled it stores raw frame payloads in `telemetry.bin`, variable definitions in `telemetry-schema.json`, session YAML in `latest-session.yaml`, and capture metadata in `capture-manifest.json`. Post-session tooling can also add compact sidecars: `capture-synthesis.json` for sampled raw-capture investigation, `ibt-analysis/*.json` for bounded analysis of the best matching iRacing `.ibt` file, `live-model-parity.json` for comparing current overlay inputs with the additive model-v2 live state, and `live-overlay-diagnostics.json` for passive gap/radar/fuel/position-cadence assumption checks. The IBT sidecars include `ibt-local-car-summary.json`, which focuses on local-car trajectory/fuel/vehicle-dynamics readiness without copying the source `.ibt`. The parity sidecar includes a promotion-readiness signal and emits a `live_model_v2_promotion_candidate` app event when the session meets the configured evidence thresholds.

The app also writes compact edge-case telemetry artifacts after each live session under `logs/edge-cases/` and, when raw capture is not active, live overlay diagnostics under `logs/overlay-diagnostics/`. These JSON files are independent of raw capture and are meant to answer targeted diagnostics questions without storing full-frame payloads. Edge-case artifacts include short pre/post clips around detector triggers, selected normalized live fields, selected nearby/class timing rows, watched scalar raw values, watched-variable schema, and the watched variables that were missing for the current car/session. They also watch reset/tow context such as `EnterExitReset` and `PlayerCarTowTime` when the SDK exposes those variables, pit-request command changes with viewer/camera context, ARB/anti-roll/wing-like adjustment channels when present, and forecast-like weather channels if a future SDK schema exposes a matching variable. Overlay diagnostics summarize current-overlay assumptions such as focus availability, non-race gap use, large gap scaling, radar side/focus evidence, Fuel/Pit local-strategy suppression, fuel burn without level, pit-service signal changes, intra-lap position cadence, sector timing with missing lap counters, and reset-style progress discontinuities.

## Event Schema

Event-level data is the highest-level context for a capture. It combines the local capture manifest with the `WeekendInfo` block from the iRacing session YAML.

### Primary sources

- `capture-manifest.json`
- `latest-session.yaml` -> `WeekendInfo`

### Core fields

- Capture identity:
  - `captureId`
  - `startedAtUtc`
  - `finishedAtUtc`
  - `frameCount`
  - `droppedFrameCount`
  - `tickRate`
  - `bufferLength`
  - `variableCount`
- Track and venue:
  - `TrackName`
  - `TrackDisplayName`
  - `TrackConfigName`
  - `TrackID`
  - `TrackLength`
  - `TrackCountry`
  - `TrackCity`
  - `TrackNumTurns`
- Event and build context:
  - `EventType`
  - `Category`
  - `Official`
  - `SeriesID`
  - `SeasonID`
  - `SessionID`
  - `SubSessionID`
  - `BuildVersion`
  - `TrackVersion`
- Weather and environment:
  - `TrackWeatherType`
  - `TrackSkies`
  - `TrackSurfaceTemp`
  - `TrackAirTemp`
  - `TrackWindVel` / live `WindVel`
  - `TrackWindDir` / live `WindDir`
  - `TrackRelativeHumidity`
  - live `RelativeHumidity`
  - `TrackPrecipitation` / live `Precipitation`
  - `TrackWetness` from live telemetry
  - live `FogLevel`, `AirPressure`, `SolarAltitude`, and `SolarAzimuth`
  - forecast-like raw variables if a future SDK schema exposes a name or description containing `forecast`

### Example from short diagnostic capture

- Event type: offline test
- Track: Nürburgring Combined / Gesamtstrecke 24h
- Build version: `2026.04.21.01`
- Weather: overcast, static weather, 35% precipitation

## Session Schema

Session-level data explains what is happening in the current iRacing session and how that state changes over time.

### Primary sources

- `latest-session.yaml` -> `SessionInfo`
- `telemetry-schema.json` / `telemetry.bin`

### Static or slowly changing fields

- `CurrentSessionNum`
- `Sessions[].SessionNum`
- `Sessions[].SessionType`
- `Sessions[].SessionName`
- `Sessions[].SessionTime`
- `Sessions[].SessionLaps`
- `Sessions[].SessionTrackRubberState`
- `ResultsPositions`
- `ResultsFastestLap`

### Live session-state fields

- `SessionTime`
- `SessionTick`
- `SessionNum`
- `SessionState`
- `SessionFlags`
- `SessionTimeRemain`
- `SessionLapsRemainEx`
- `SessionTimeOfDay`
- `IsOnTrack`
- `IsReplayPlaying`
- `SessionInfoUpdate`

### Race-start phase and countdown semantics

`SessionNum` selects the active session entry from session YAML, such as practice, qualifying, or race. `SessionState` is a phase inside that active session; values below `4` can occur while the active `SessionNum` is already a race. Do not interpret `SessionState` values `1`, `2`, or `3` as practice/qualifying/race session types.

Observed race-start captures show that the common pre-race UI countdown is sometimes exposed through `SessionTimeRemain`, but only during an early pre-green race phase. In the four-hour race-start capture, the active session was `Race`/`RACE` with `SessionNum = 2`; while `SessionState = 1`, `SessionTimeRemain` counted down from about 109 seconds to 9 seconds. After the state advanced to `2` and then `3`, the race was still pre-green, but `SessionTimeRemain` returned `-1.0`. After green, `SessionTimeRemain` became the race clock once the sim began publishing normal remaining-session time.

Telemetry consumers should therefore treat positive `SessionTimeRemain` during a race pre-green phase as a real pre-race countdown, but they must not infer a countdown from `SessionTimeTotal - SessionTime` when `SessionTimeRemain` is negative. Later grid/pace pre-green phases can have no exposed countdown even though the race has not started. This matters for any future session timer, flags, race-start, Standings, Relative, Gap To Leader, or fuel/strategy behavior that wants to distinguish gridding from live race timing.

Observed race-start and endurance captures also show that scoring/results data and live timing data become available at different points. Race pre-green states (`SessionState` values below `4`) can have usable starting-grid rows before any `CarIdxPosition`, `CarIdxClassPosition`, or positive `CarIdxF2Time` data exists. After green (`SessionState == 4`), official position rows can appear before F2 race gap timing is available; the May 12, 2026 uploaded capture had current race results and position/class-position rows after green, but `CarIdxF2Time` remained non-positive for sampled cars, so live gap and interval values could not be computed. The longer four-hour and 24-hour race captures later showed the normal running-race shape: current race results, broad position/class-position coverage, and positive F2 timing for most cars.

`CarIdxEstTime` may be populated before green and in early green frames where `CarIdxF2Time` is still zero. Treat it as track-position estimation unless a consumer has a separate confidence rule. Relative can use positive `CarIdxEstTime` plus valid `CarIdxLapDistPct` during race `SessionState == 3` to keep showing estimated ahead/behind rows, including after a local tow to pit lane. Standings remains more conservative for now: pre-green race rows keep starting-grid order and do not show estimated class gap or focus interval cells. The compact comparison lives in `fixtures/telemetry-analysis/session-state-signal-availability.md`.

### Useful analysis questions

- Was the sim connected and streaming valid data?
- What was the current session type when the capture occurred?
- How long was the car on track vs off track or on pit road?
- How far through the lap or session did the driver get?
- Did flags, wetness, or session status change mid-capture?
- Did the compact edge-case detector see timing contradictions, side-occupancy mismatches, pit/fuel/tire service transitions, weather changes, replay state, incidents, engine warnings, low FPS, or high latency?

### Example from short diagnostic capture

- Session type: `Offline Testing`
- Captured duration: about 77 seconds
- On-track time: about 67 seconds
- On-pit-road time: about 6.6 seconds
- Max lap progress reached: about 8.2% of a lap

## Car Schema

Car data splits into two parts:

1. static identity and setup context
2. dynamic usage and state telemetry

### Car identity and setup

Primary source:

- `latest-session.yaml` -> `DriverInfo` and `Drivers[]`

Useful fields:

- Driver identity:
  - `DriverCarIdx`
  - `DriverUserID`
  - `UserName`
  - `TeamName`
- Vehicle identity:
  - `CarPath`
  - `CarID`
  - `CarScreenName`
  - `CarScreenNameShort`
  - `CarIsElectric`
- Configuration and setup:
  - `DriverSetupName`
  - `DriverSetupIsModified`
  - `DriverSetupLoadTypeName`
  - `DriverSetupPassedTech`
  - `DriverCarVersion`
  - `DriverGearboxType`
  - `DriverCarFuelMaxLtr`
  - `DriverCarRedLine`
  - `DriverCarGearNumForward`
  - `DriverCarSLShiftRPM`
  - `DriverTires`

### Dynamic car usage telemetry

Primary source:

- `telemetry-schema.json` + `telemetry.bin`

#### Driver inputs

- `Throttle`
- `Brake`
- `Clutch`
- `ThrottleRaw`
- `BrakeRaw`
- `ClutchRaw`
- `SteeringWheelAngle`
- `BrakeABSactive`
- future clutch-like fields by name/description, so dual-clutch support can be validated before UI promotion

`Clutch` and `ClutchRaw` use the iRacing engagement scale (`0=disengaged`, `1=fully engaged`). The live Inputs overlay converts that into a driver-control scale before rendering, so `1` means the clutch control is pressed/disengaging.

#### Motion and powertrain

- `Speed`
- `Gear`
- `RPM`
- `LapDistPct`
- `OnPitRoad`
- `PlayerTrackSurface`
- `PlayerTrackSurfaceMaterial`

#### Fuel, tires, and service state

- `FuelLevel`
- `FuelLevelPct`
- `FuelUsePerHour`
- `PlayerTireCompound`
- `TireSetsUsed`
- `TireSetsAvailable`
- `PitstopActive`
- `PitRepairLeft`
- `PitOptRepairLeft`
- `PitSvFuel`
- `PitSvTireCompound`
- `PitSvFlags`
- pit request controls such as `dpLFTireChange`, `dpRFTireChange`, `dpLRTireChange`, `dpRRTireChange`, `dpFuelFill`, `dpFuelAddKg`, `dpFastRepair`, `dpWindshieldTearoff`, cold-pressure adjustment channels, and car-specific pit setup request channels such as NASCAR `dpWeightJackerLeft`/`dpWeightJackerRight`

#### In-car controls and condition

- `dcBrakeBias`
- `dcABS`
- `dcTractionControl`
- ARB/anti-roll/wing-like `dc*` or `dp*` adjustment channels when a future SDK schema exposes them; the May 2026 NASCAR/PCup captures did not expose ARB or wing variables, but they did separate in-car `dc*` controls from pit-request `dp*` controls
- `OilTemp`
- `OilPress`
- `WaterTemp`
- `WaterLevel`
- `FuelPress`
- `Voltage`
- `EngineWarnings`

#### Multi-car arrays for race/session context

- `CarIdxLap`
- `CarIdxLapCompleted`
- `CarIdxLapDistPct`
- `CarIdxTrackSurface`
- `CarIdxOnPitRoad`
- `CarIdxPosition`
- `CarIdxClassPosition`
- `CarIdxClass`
- `CarIdxF2Time`
- `CarIdxEstTime`
- `CarIdxRPM`
- `CarIdxGear`
- `CarIdxBestLapTime`
- `CarIdxTireCompound`
- `CarIdxTireCompound` and `PlayerTireCompound` values are indices into `DriverInfo.DriverTires[]`.
  Treat `-1` as unknown/unavailable, not as wet or as a wraparound list index.
  `CarIdxQualTireCompound` is qualifying metadata and should not be used as current tire state.
- `CamCarIdx`
- `CarLeftRight`

#### Proximity and leader-gap fields

- `CamCarIdx` is the active camera/focus car index. Focus-based visual overlays use it only when valid; when it is missing or invalid they report/wait for unavailable focus instead of silently promoting `PlayerCarIdx`. Fuel and Pit Service remain local active driver/team overlays for V1: they use player/team telemetry only when `CamCarIdx` is valid and points at the player car, while history and local radar remain anchored to the player/team car because those are local/team product surfaces.
- `CarLeftRight` is the authoritative scalar side-warning signal for cars to the left, right, or both sides of the driver. Radar placement can choose which rendered car occupies a side slot only when its reliable relative meters, or fallback timing gap, is inside the side-overlap window.
- `CarIdxF2Time` is used for race gap timing to the overall leader, class leader, same-class cars in the gap trend graph, and best-effort Standings enrichment for other classes when race-position data is available.
- Gap graph and standings timing rows are kept separate from radar proximity rows: cars with valid standings/F2 timing can remain graphable or table-enriched even when `CarIdxLapCompleted` or `CarIdxLapDistPct` is unavailable.
- `CarIdxEstTime`, `CarIdxLapCompleted`, and `CarIdxLapDistPct` provide the first-pass relative track-position model for nearby cars. Radar prefers `CarIdxLapDistPct` with track length for physical close-range distance and uses reliable estimated/F2 timing as fallback.
- `CarIdxTrackSurface` and `CarIdxOnPitRoad` distinguish cars physically on track from pit-road or off-track cars. Radar excludes pit-road cars and hides while the focused car is in pit-road states.
- The live radar model keeps a short in-memory proximity history so different-class cars approaching from behind can raise a multiclass warning before they reach the close-range radar circle. This derived warning is not persisted in compact history.

### Example from short diagnostic capture

- Car: Mercedes-AMG GT3 2020
- Setup: custom user setup loaded
- Gearbox: sequential
- Max fuel: 106 L
- Tire options listed in session YAML: hard and wet
- Usage seen in telemetry:
  - throttle/brake/clutch input
  - gear and RPM progression
  - ABS activity
  - brake-bias, ABS, and TC settings/toggles
  - fuel burn from about 106.0 L down to about 104.934 L

## Gaps And Limits

This schema summary is intentionally practical, not exhaustive. Not every iRacing variable is always present in every capture or every car/session combination.

The app now watches for richer engineering channels such as tire wear, tire temperature, tire pressure, tire odometer, suspension, brake pressure/temperature, and wheel speed. If any of those watched variables become exposed and non-zero in a future car/session, the edge-case artifact records a bounded clip around the first observation.

The currently analyzed diagnostic schema did **not** appear to expose richer engineering channels such as:

- tire wear
- tire temperature distribution
- brake temperatures
- wheel-speed channels
- suspension travel or damper position

If those metrics matter later, the first check should be whether iRacing exposes them for the target car/session at all. If they do, raw diagnostic capture can still record them for analysis, but the normal app path should prefer derived live metrics and compact history over storing full-frame payloads.
