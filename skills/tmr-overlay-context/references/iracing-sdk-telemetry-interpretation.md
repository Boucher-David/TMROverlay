# iRacing SDK Telemetry Interpretation

Last updated: 2026-05-21

Use this reference before changing telemetry-backed overlay behavior, especially
source selection for Standings, Relative, Gap To Leader, Track Map, Radar, Fuel,
Pit Service, Flags, or session timer/header fields.

## Source Material

- `https://github.com/apihlaja/node-irsdk`
- `https://sajax.github.io/irsdkdocs/yaml/driverinfo.html`
- `https://irsdk-node.bengsfort.dev/API-Reference/irsdk-node-types/interfaces/DriverInfo/`
- Inspected commit: `986a9e079cb8b8b780f8d55f1f0988c85596857a`
- Inspected files: `README.md`, `src/cpp/irsdk/irsdk_defines.h`,
  `src/cpp/IRSDKWrapper.*`, `src/cpp/IrSdkBindingHelpers.*`,
  `src/JsIrSdk.js`, `src/IrSdkConsts.js`, `sample-data/telemetry-desc.json`,
  `sample-data/telemetry.json`, and `sample-data/sessioninfo.json`.

`node-irsdk` is an unofficial Node binding, but it vendors an iRacing SDK
header and includes useful telemetry descriptors and conversion behavior. Treat
it as descriptor/reference material. TmrOverlay raw captures and the current
`irsdkSharp` schema remain the source of truth for what fields are present and
meaningful in a specific modern session.

## SDK Data Model

- The SDK exposes live variables through shared memory and session info through
  a YAML string.
- Live variables are written at the SDK tick rate, historically 60 Hz in the
  header comments, but the header allows other tick rates.
- The variable list can vary by car/session. Once the sim is running, the
  variable list is locked for that session.
- Variable headers define each value's name, type, offset, count, unit, and
  description. Array fields such as `CarIdxF2Time` are fixed-count SDK arrays,
  not roster-length arrays.
- Session info updates separately from live variables. Do not assume a live
  variable frame and the latest YAML snapshot changed at the same instant.
- SDK YAML can need robust parsing. `node-irsdk` special-cases unquoted
  `TeamName` values before YAML parsing.

TmrOverlay captures raw numeric values from the C# SDK pipeline. `node-irsdk`
converts several enum and bitfield units to strings or arrays for JavaScript,
so examples from that repo may not match our raw representation exactly.

## Driver Role / Spotting Signals

The SDK/reference material and current local captures do not expose a named raw
telemetry variable such as `IsSpotting`, `Spotter`, or `IsSpectating`. Keep
future schema scans open to a true role field, but do not assume one exists.

The strongest documented role signal today is session info:

- `DriverInfo.DriverCarIdx` points at the client entry in
  `DriverInfo.Drivers[]`.
- Each `DriverInfo.Drivers[]` row can include `IsSpectator`.
- A spectator/spotter local user should therefore be detected by resolving
  `DriverInfo.DriverCarIdx` to the matching `Drivers[]` row and checking
  `IsSpectator == 1`.

Local proof from the GR86 Nordschleife Industriefahrten spotter-only capture:
`DriverInfo.DriverCarIdx` resolves to the `CarIdx = 63` driver row with
`IsSpectator = 1`. Raw telemetry still reports `PlayerCarIdx = 63` for that
spectator entry, while `CamCarIdx` usually follows race cars. This makes the
session-info row identity a better local-role signal than camera focus alone.

`IsReplayPlaying` can be true during active spotting/watch contexts. Treat it
as camera/replay plumbing unless paired with stronger context; do not suppress
live race overlays from `IsReplayPlaying` alone. Future v1.x role gating should
preserve both the derived session-info role and any newly discovered raw role
field so replay fixtures can compare them directly.

## Core SDK Enums

### SessionState

`SessionState` is an SDK phase inside the active `SessionNum`; it is not the
practice/qualifying/race selector.

| Value | SDK name | Interpretation |
| --- | --- | --- |
| `0` | `Invalid` | No valid session state. |
| `1` | `GetInCar` | Driver can/should get in car. In race captures this can be an early pre-green countdown/grid phase. |
| `2` | `Warmup` | Warmup/grid transition phase inside the active session. In race captures this is still pre-green. |
| `3` | `ParadeLaps` | Parade/pace/moving pre-green phase. Cars may be moving and relative placement can be meaningful. |
| `4` | `Racing` | Green/racing phase. This can begin before all cars/classes cross start/finish. |
| `5` | `Checkered` | Checkered/post-green phase. |
| `6` | `CoolDown` | Cooldown/post-session phase. |

For race overlays, session type comes from session YAML (`SessionInfo.Sessions`
and the active `SessionNum`). `SessionState` only describes the phase of that
selected session.

### SessionFlags

`SessionFlags` is a bitfield. Important flags include:

| Flag | Meaning |
| --- | --- |
| `Checkered`, `White`, `Green`, `Yellow`, `Red`, `Blue` | Race-control flags. |
| `YellowWaving`, `Debris`, `Caution`, `CautionWaving` | Yellow-family variants. Keep debris visually distinct from plain yellow; caution variants usually indicate a specific safety-car/caution-period state rather than generic yellow. |
| `OneLapToGreen`, `GreenHeld` | Pre-green/start-control context. `OneLapToGreen` is a caution-period restart state, not just a generic yellow. |
| `TenToGo`, `FiveToGo` | Race-distance countdown flags. |
| `Black`, `Disqualify`, `Furled`, `Repair` | Driver flag/service states. |
| `StartHidden`, `StartReady`, `StartSet`, `StartGo` | Start-light states. |

The sample `node-irsdk` telemetry frame reports `SessionState = Racing` while
also showing `OneLapToGreen` and `StartHidden`. Do not infer a complete race
phase from one flag or one state field alone.

### Track Location

`CarIdxTrackSurface` uses the `irsdk_TrkLoc` enum:

| Value | SDK name | Interpretation |
| --- | --- | --- |
| `-1` | `NotInWorld` | Car is not in the world. |
| `0` | `OffTrack` | Car is off track. |
| `1` | `InPitStall` | Car is in its pit stall. |
| `2` | `ApproachingPits` | Car is approaching pits. |
| `3` | `OnTrack` | Car is on track. |

`CarIdxOnPitRoad` is a separate boolean for being between the pit-road cones.
Use both fields where pit/tow/garage distinctions matter.

### CarLeftRight

`CarLeftRight` is the local player side-warning bitfield:

| Value | SDK name | Interpretation |
| --- | --- | --- |
| `0` | `LROff` | Side warning off. |
| `1` | `LRClear` | No cars around the driver. |
| `2` | `LRCarLeft` | Car to the left. |
| `3` | `LRCarRight` | Car to the right. |
| `4` | `LRCarLeftRight` | Cars on both sides. |
| `5` | `LR2CarsLeft` | Two cars to the left. |
| `6` | `LR2CarsRight` | Two cars to the right. |

Use this as local-player side occupancy, not as a general per-car spatial
source. It does not identify which `CarIdx` caused the warning.

### Other Useful Enums

- `EngineWarnings`: water temperature, fuel pressure, oil pressure, engine
  stalled, pit speed limiter, rev limiter active.
- `PitSvFlags`: tire changes, fuel fill, windshield tearoff, fast repair.
- `PitSvStatus`: none, in progress, complete, plus too far left/right/forward/
  back, bad angle, and cannot-fix-that error states.
- `CameraState`: includes `IsSessionScreen`, `IsScenicActive`,
  `CamToolActive`, `UIHidden`, `UseAutoShotSelection`,
  `UseTemporaryEdits`, key acceleration, and mouse aim mode. This is camera UI
  state, not TmrOverlay window visibility.

## Field Descriptors To Preserve

From `sample-data/telemetry-desc.json`:

| Field | SDK descriptor |
| --- | --- |
| `SessionTime` | Seconds since session start. |
| `SessionNum` | Session number. |
| `SessionState` | Session state, unit `irsdk_SessionState`. |
| `SessionFlags` | Session flags, unit `irsdk_Flags`. |
| `SessionTimeRemain` | Seconds left till session ends. |
| `SessionLapsRemain` | Old laps-left field; descriptor says use `SessionLapsRemainEx`. |
| `SessionLapsRemainEx` | New improved laps left till session ends. |
| `CarIdxLap` | Laps started by car index. |
| `CarIdxLapCompleted` | Laps completed by car index. |
| `CarIdxLapDistPct` | Percentage distance around lap by car index. |
| `CarIdxTrackSurface` | Track surface type by car index, unit `irsdk_TrkLoc`. |
| `CarIdxOnPitRoad` | On pit road between the cones by car index. |
| `CarIdxPosition` | Cars position in race by car index. |
| `CarIdxClassPosition` | Cars class position in race by car index. |
| `CarIdxF2Time` | Race time behind leader or fastest lap time otherwise, seconds. |
| `CarIdxEstTime` | Estimated time to reach current location on track, seconds. |
| `CarLeftRight` | Notify if car is to the left or right of driver, unit `irsdk_CarLeftRight`. |

The `CarIdxF2Time` descriptor is especially easy to overread. It is a semantic
hint, not a guarantee that every positive-looking value is a usable race gap in
every phase.

## TmrOverlay Race-Start Findings

Local capture evidence currently matters more than descriptor theory for
race-start behavior.

- Four-hour raw capture: `captures/capture-20260426-130334-932`.
- Correct green anchor in that capture: raw frame `155436`, `SessionTime`
  `266.850`, transition from `SessionState = 3` to `SessionState = 4`.
- Race green can occur before all cars/classes cross start/finish. A green
  `SessionState = 4` frame can still precede settled start/finish timing for
  cars farther back on track.
- During race `SessionState = 3`, cars can be moving. `CarIdxLapDistPct` and
  positive `CarIdxEstTime` can be valid for many cars while official F2 gaps,
  `CarIdxPosition`, and `CarIdxClassPosition` are unavailable or placeholder.
- `SessionTimeRemain` can be a real pre-race countdown during an early race
  pre-green phase. Later grid/pace pre-green phases can return `-1.0`. Do not
  synthesize a countdown from `SessionTimeTotal - SessionTime` when the SDK
  returns negative remaining time.
- A local player tow during race `SessionState = 2` or `3` is real behavior.
  iRacing may still show relative-style estimated gaps after the tow, even
  though standings gaps should remain conservative before green.

## F2 Timing Caveats

Observed four-hour race-start frames showed `CarIdxF2Time` can be populated
with a placeholder-like value that exactly matches `(CarIdxPosition - 1) / 1000`
for almost every row, even after green:

| Race time from green | Rows matching placeholder pattern |
| --- | --- |
| `+2s` | `56/56` |
| `+120s` | `58/58` |
| `+480s` | `58/58` |
| `+540s` | `1/58` |

Raw examples at green `+120s`, ordered by class position:

| CarIdx | ClassPos | LapCompleted | LapDistPct | Raw F2 | Raw EstTime |
| --- | --- | --- | --- | --- | --- |
| `32` | `3` | `0` | `0.208256` | `0.0020000001` | `114.3955688` |
| `43` | `5` | `0` | `0.191982` | `0.0040000002` | `101.3106613` |
| `29` | `14` | `0` | `0.198814` | `0.0130000003` | `109.4597473` |
| `23` | `20` | `0` | `0.196072` | `0.0189999994` | `106.2214050` |
| `15` | `25` | `0` | `0.193158` | `0.0240000002` | `104.2456284` |

Those F2 values look positive but are not useful gap seconds.

After the first-lap transition, F2 became representative in the same capture.
Raw examples at green `+540s`:

| CarIdx | ClassPos | Raw F2 | Raw EstTime |
| --- | --- | --- | --- |
| `32` | `3` | `4.3737001419` | `58.0048981` |
| `29` | `5` | `10.1205997467` | `52.2730370` |
| `23` | `9` | `17.6886005402` | `45.5174675` |
| `15` | `12` | `22.9242000580` | `40.1945381` |
| `43` | `16` | `24.0006008148` | `38.6203651` |

Do not treat `CarIdxF2Time > 0` as sufficient. A timing consumer should reject
race F2 when it matches `(position - 1) / 1000`, when max F2 is tiny for a
whole field, or when same-class timing coverage/coherence is weak. The existing
corpus and replay validators should be expanded whenever this rule changes.

## Lap-Distance Caveats

`CarIdxLapDistPct` can be meaningful before `CarIdxLapCompleted` is initialized
to a non-negative lap count. Do not automatically discard lap-distance progress
only because `CarIdxLapCompleted == -1`.

This matters for:

- Relative estimated timing during race `SessionState = 3`.
- Early-green gap analysis before F2 becomes coherent.
- Track Map pre-green/race-start marker work.
- Radar/spatial checks that should use physical distance and track location,
  not timing-only fields.

## Overlay Source Policy

- Standings: use session YAML/grid/scoring as the row universe and order before
  green. Do not show pre-green estimated `GAP`/`INT` from `CarIdxEstTime`.
  After green, row order can use current race results/positions when meaningful,
  but gap/interval cells still need coherent F2 or another explicit timing
  source.
- Relative: from race `SessionState = 3`, positive `CarIdxEstTime` plus valid
  lap-distance can be used for estimated ahead/behind rows, including local tow
  cases. Do not create relative rows from grid order alone.
- Gap To Leader: graph race gaps only from coherent race timing. Early-green
  F2 placeholders and all-zero timing rows are not reliable. If estimated
  timing is later adopted for early green, keep source-aware continuity and do
  not mix it silently with coherent F2.
- Track Map and Radar: physical placement should be based on lap-distance,
  track length, track location, and pit-road state. `CarIdxEstTime` and
  `CarIdxF2Time` are timing context, not sufficient spatial placement inputs.
- Fuel: current fuel level and burn can change while cars are gridding or
  pacing. Race pre-green countdowns should not be treated as remaining race
  duration, but fuel telemetry itself remains live.
- Flags/session header: combine `SessionState`, `SessionFlags`,
  `SessionTimeRemain`, and session YAML. A single flag or state number is not
  enough to explain the race phase.

## Checklist Before Telemetry Source Changes

1. Check the current raw capture schema against the tracked SDK availability
   corpus when captures are available.
2. Read `fixtures/telemetry-analysis/session-state-signal-availability.md` for
   known phase-by-phase evidence.
3. Verify whether the field is direct SDK data, parsed session YAML, or a
   TmrOverlay-derived model value.
4. Preserve the distinction between row universe/order, position, gap timing,
   physical placement, and local-player-only state.
5. Treat `SessionState` as a phase inside active `SessionNum`.
6. Reject placeholder timing explicitly. Do not use "positive value exists" as
   the only gate for F2 or estimated timing.
7. Update fixtures and validation assumptions in the same pass as behavior
   changes.
