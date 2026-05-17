# Fuel Overlay Context

Last updated: 2026-04-26

## Current Analyzed Evidence

Legacy short wet-test sample, retained here only as a derived note after the raw capture was removed from git:

High-level context:

- track: Nürburgring Combined / Gesamtstrecke 24h
- event type: `Offline Testing`
- weather: overcast, static weather, 35% precipitation
- car: Mercedes-AMG GT3 2020
- setup: custom user setup loaded

Session/car values observed from session YAML:

- `DriverCarFuelMaxLtr = 106.000`
- `DriverCarFuelKgPerLtr = 0.750`
- `DriverCarEstLapTime = 529.7409`
- tire options listed: hard and wet

## Derived Summary From The Sample

Approximate decoded summary from the raw frames:

- total valid telemetry duration: about 76.1 s
- on-track time: about 67.0 s
- on-pit-road time: about 6.6 s
- moving time: about 61.9 s
- max speed: about 205.1 kph
- max RPM: about 7443
- max lap progress reached: about 8.2% of a lap

Fuel-specific rough derivation from the sample:

- fuel dropped from about `106.0 L` to about `104.934 L`
- fuel used: about `1.066 L`
- derived burn: about `50.45 L/h`
- derived burn using session density: about `37.84 kg/h`
- rough full-tank projection from this short sample:
  - about `126.1 min`
  - about `14.3 laps` using `DriverCarEstLapTime`

Treat those full-tank estimates as low-confidence because the original capture was short, partial-lap, wet, and included pit-road time. Do not treat the raw sample as a repo fixture; future fuel validation should use compact tracked history/fixture data or ignored local captures.

Long endurance capture:

- `captures/capture-20260426-130334-932`
- Nürburgring Gesamtstrecke VLN / `nurburgring combinedshortb`
- Mercedes-AMG GT3 2020
- 4-hour team race, 30 completed laps, final P7 overall / P6 class
- raw capture: 1,036,026 frames, 0 dropped frames, 2,208 session-info snapshots

Derived development/sample baseline now tracked under `history/baseline/cars/car-156-mercedesamgevogt3/tracks/track-262-nurburgring-combinedshortb/sessions/race/`:

- fuel per lap: about `13.363 L/lap`
- fuel per hour: about `99.364 L/h`
- max fuel: `106 L`, which gives about `7.93 laps/tank` at the historical average
- for a 30-lap timed-race estimate, a neutral whole-lap target is `8/8/7/7`; when the 4-hour teammate stint evidence is enabled, the alternating team model becomes `7/8/7/8`
- each 8-lap stint needs about `0.11 L/lap` saving versus the baseline average
- extrapolating the same pace to 24 hours gives about `180 laps`; a 7-lap rhythm would be about `26 stints / 25 stops`, while an 8-lap rhythm is about `23 stints / 22 stops`, so the 8-lap rhythm avoids about 3 stops or about 194 seconds at the observed average pit-lane time
- valid local-driver fuel distance: about `14.112 laps`
- unique team lap-time samples: 27
- pit/service count: 3
- top-level sanitized stint history now records local-driver stints as 7 laps and teammate-driver stints as 8 laps for this combo
- baseline pit estimates include about `2.68 L/s` observed refuel rate and about `39.2 s` inferred tire/service time
- source limitation: fuel comes only from local-driver scalar frames; teammate stints have timing/position from `CarIdx*` arrays but no direct fuel scalar

## What iRacing Exposes For Fuel Work

The currently observed raw fields are enough to build a fuel/stint overlay even though the SDK does not appear to hand us a ready-made stint answer.

Fields already seen or expected to matter:

- raw fuel state:
  - `FuelLevel`
  - `FuelLevelPct`
  - `FuelUsePerHour`
- timing/session context:
  - `SessionTimeRemain`
  - `SessionLapsRemainEx`
  - `LapDistPct`
  - `SessionTime`
- car metadata:
  - `DriverCarFuelMaxLtr`
  - `DriverCarFuelKgPerLtr`
  - `DriverCarEstLapTime`
- service state:
  - `PitstopActive`
  - `PitSvFuel`
  - `PitSvTireCompound`

Current working assumption:

- iRacing exposes raw usage and context for the local driver
- in team events, `CarIdx*` arrays remain useful while spotting/teammate-driving, but scalar `FuelLevel`/`FuelUsePerHour` can become invalid or zero
- we derive:
  - burn rate
  - time remaining on current fuel
  - laps remaining on current fuel
  - estimated fuel to finish
  - pit-window suggestions

## Estimated Lap Time Scope

Important nuance for future multi-class work:

- the current sample capture only shows the player's `DriverCarEstLapTime` and one `CarClassEstLapTime` value in session YAML because the session is effectively single-car/single-class
- prior doc review suggested `CarClassEstLapTime` can exist as a per-car telemetry array when available

For overlay design:

- start with the player's own estimated lap time
- if future endurance captures include full-field estimated lap time data, derive class-level logic by grouping per-car values by class ID

## First Fuel Overlay Direction

The first real fuel overlay should favor clarity over breadth. The current implementation is a draggable fuel calculator window backed by `FuelStrategyCalculator`.

Implemented initial overlay contents:

- overview row: planned race laps, planned stint count, and final stint target
- practice/qualifying observation row: completed green-lap min/avg/max usage and sample count, waiting until measured evidence exists
- stint rows: whole-lap targets such as neutral `8/8/7/7` or team-history-adjusted `7/8/7/8`
- per-stint target liters-per-lap only; selected burn source stays in the source footer
- stable content state: when no fuel stop remains, including after the final stop, the overlay keeps the same full table layout and leaves future rows blank so threshold changes do not switch the view mid-run
- shared source footer: selected burn source, laps-per-tank, history source, and min/avg/max burn when enabled
- status color:
  - gray: waiting for usable fuel/burn
  - amber: realistic fuel-saving target or final-stop deletion opportunity
  - green: current plan is covered by the selected model

For V1, rhythm optimization and tire-free/tire-change advice are hidden. They remain future fuel-calculator work because the current pit-service evidence can make those recommendations look more certain than they are.

Current derivations:

- `instant_lph = FuelUsePerHour / DriverCarFuelKgPerLtr` for diagnostic/current-fuel context only
- `minutes_left = FuelLevel / instant_lph * 60`
- `selected_fuel_per_lap = measured_completed_green_lap_delta` first, then exact user/baseline history
- `practice_quali_usage = measured_completed_green_lap_min_avg_max`; this is observation only and does not use history or instantaneous burn
- `laps_left = FuelLevel / selected_fuel_per_lap` only when selected burn evidence exists
- `race_laps = ceil(overall_leader_progress + session_time_remaining / leader_pace) - team_progress`
- `full_tank_laps = DriverCarFuelMaxLtr / selected_fuel_per_lap`
- `whole_lap_targets = distribute(ceil(race_laps_remaining), planned_stint_count)`
- `target_fuel_per_lap = available_fuel_for_stint / target_laps`
- `required_save_per_lap = max(0, target_laps * selected_fuel_per_lap - available_fuel) / target_laps`

The overlay must keep live race telemetry as the primary source for current fuel and race context. Current fuel may refresh during grid/pre-green/pit states, but strategy burn updates only after completed valid green laps from local fuel-level deltas. Exact user history is only a fallback/model until the current session has measured green-lap evidence. Baseline/sample history is opt-in for development and must not override live measured evidence.

During teammate stints, local scalar fuel can be unavailable. The same gap can happen when the user switches focus to another driver/car whose fuel scalars are not exposed. For V1, the fuel calculator is local in-car/pit only; it does not render modeled active strategy for teammate/focused-other-car contexts. Completed stints can still be stored in historical storage for later use.

When historical completed-stint data shows team stints around 8 laps and the fuel-saving requirement stays within the realistic threshold, the calculator can bias projected rows to 8 laps. This is a planning hint, not proof that current live teammate fuel is directly available, and the UI intentionally does not label rows as teammate stints.

## Next Direction

The next implementation step is to harden the fuel/stint overlay by adding:

- longer rolling smoothing and source-confidence labels for measured green-lap burn
- explicit reserve/margin settings
- broader fallback lookup for same car or similar track when exact car/track/session history is missing
- modeled stint-analysis rows when selected/focused-driver fuel is unavailable but completed-stint history exists
- confidence/source flags in the overlay so teammate-stint fuel is never treated as direct measured fuel unless a future source exposes it
- rhythm optimization and tire-service advice once pit-service history has stronger source buckets

## Pit Service Timing Direction

The long endurance capture confirms pit-service history is worth storing, but with source/confidence flags.

Strong signals:

- `CarIdxOnPitRoad[DriverCarIdx]` for team-car pit lane entry/exit.
- `SessionTime` for elapsed timing.
- `PlayerCarInPitStall`, `PitstopActive`, and low speed for pit-stall/service timing, with caveats during teammate handoff.
- `FuelLevel` and `FuelLevelPct` for fuel added/rate only while local-driver scalar telemetry is valid.
- `CarIdxFastRepairsUsed`, `FastRepairUsed`, `PitRepairLeft`, and `PitOptRepairLeft` for repair events.

Weaker/inferred signals:

- tire changes via `dpLFTireChange`, `dpRFTireChange`, `dpLRTireChange`, `dpRRTireChange`, tire-used counters, compound changes, and tire odometer resets.
- driver swaps via session-info active-driver changes, `DCDriversSoFar`, and `DCLapStatus`.

The 4-hour capture showed three physical stops:

- about 63.9s pit lane / 38.8s stationary, partial local fuel visibility.
- about 66.7s pit lane / 40.4s stationary, reliable full-fuel sample of about 97.3 L over 36.3s, about 2.68 L/s.
- about 63.9s pit lane / 38.4s stationary, partial local fuel visibility plus a fast-repair-used event.

Historical storage should keep both derived stop metrics and raw source flags so strategy calculations can learn per-car values such as tires/no-tires, full fuel, partial fuel, driver swap, and repair cost without overstating confidence.

The current tire guidance uses average tire-service seconds and observed fuel fill rate. A future pass should split this into stronger buckets such as no tires, left/right-side tires, four tires, full fuel, partial fuel, driver swap, and repair events.
