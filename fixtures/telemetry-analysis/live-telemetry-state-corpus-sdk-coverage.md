# Live Telemetry Corpus SDK Coverage

This note compares `live-telemetry-state-corpus.json` with the iRacing SDK telemetry schemas available from the local raw captures used for the endurance states. The broader field inventory now lives in `sdk-field-availability-corpus.json` and `sdk-field-availability-corpus.md`.

The corpus intentionally does not include mock player names. State payloads avoid `DriverName`, `UserName`, `UserID`, `TeamName`, and per-driver labels. They keep redacted source-selection evidence such as car indexes, class counts, row counts, source labels, timing coverage, and track/session context.

Session-info does expose identity/display fields in the local endurance captures: `UserName`, `UserID`, `TeamName`, `TeamID`, `ClubName`, `DivisionName`, `AbbrevName`, `CarScreenName`, `CarScreenNameShort`, and class/car IDs. If V1 needs Team Name or richer driver-directory validation, add redacted presence/count fixtures rather than committing real names.

## Current Corpus Scope

- The checked-in corpus has 12 states across AI multi-session, open-player practice, four-hour endurance, and 24-hour endurance captures.
- The corpus is focused on Standings, Relative, and Gap To Leader source-selection behavior.
- The local endurance schemas expose 334 SDK variables; the broader SDK availability corpus now tracks 340 variables after adding compact NASCAR and PCup schema evidence.
- `sdk-field-availability-corpus.json` tracks those 340 SDK variables with SDK-declared array/storage maximums, primitive type bounds, sampled observed ranges, source coverage, and redacted session-info identity shape.
- The corpus extractor currently uses about 40 raw scalar/array variables for source-selection evidence.
- The May 11 AI/open raw capture directories are not present in this checkout, so SDK-schema comparison for the state corpus remains based on the local four-hour and 24-hour endurance captures; the separate SDK field corpus also preserves the May 2026 NASCAR/PCup schema evidence.

## Direct App SDK Reads Not Represented In The Corpus

The production telemetry reader directly consumes these SDK variables that are not represented in the current corpus:

```text
AirTemp
Brake
Clutch
DCDriversSoFar
DCLapStatus
FastRepairUsed
FuelLevel
FuelLevelPct
FuelPress
FuelUsePerHour
LapDeltaToBestLap
LapDeltaToBestLap_DD
LapDeltaToOptimalLap
LapDeltaToOptimalLap_DD
LapDeltaToSessionBestLap
LapDeltaToSessionBestLap_DD
LapDeltaToSessionLastlLap
LapDeltaToSessionLastlLap_DD
LapDeltaToSessionOptimalLap
LapDeltaToSessionOptimalLap_DD
OilPress
OilTemp
PlayerTireCompound
RPM
SolarAltitude
SolarAzimuth
SteeringWheelAngle
Throttle
TireSetsUsed
TrackTempCrew
TrackWetness
Voltage
WaterTemp
WeatherDeclaredWet
```

## High-Signal SDK Areas Missing From This Corpus

- `PlayerCarPosition`, `PlayerCarClassPosition`, `PlayerCarPitSvStatus`, `PlayerCarTowTime`, and incident counters: useful for local-player cross-checks, pit/service status, tow/garage behavior, and support diagnostics.
- `CarIdxSessionFlags`, `CarIdxPaceFlags`, `CarIdxPaceLine`, `CarIdxPaceRow`, and `PaceMode`: useful for grid/start/pace/flag behavior and pre-green validation.
- `CarIdxGear`, `CarIdxRPM`, `CarIdxSteer`, `CarIdxTireCompound`, and `CarIdxTrackSurfaceMaterial`: useful for future per-car context but not currently required for Standings/Relative/Gap source selection.
- `DCDriversSoFar` and `DCLapStatus`: useful for endurance driver-change and team-stint validation.
- `FuelLevel`, `FuelUsePerHour`, `FuelLevelPct`, `dpFuel*`, tire-set/service fields, and fast-repair fields: useful for Fuel and Pit Service corpus expansion.
- `WeatherDeclaredWet`, `TrackWetness`, `Precipitation`, `TrackTempCrew`, `AirTemp`, `SolarAltitude`, and `SolarAzimuth`: useful for Session / Weather and wet-track validation.
- `LapDeltaTo*` channels: useful for future timing/driver-performance displays, but not a replacement for race gap evidence.
- `RadioTransmit*`, `CamCamera*`, `ReplaySession*`, and replay playback fields: useful for spectator/replay-mode validation if those contexts become V1-relevant.

## Recommended Next Corpus Expansion

Keep the current 10-state corpus as the source-selection baseline. Add separate compact fixtures for:

- Input / Car State: throttle, brake, clutch, steering angle/max angle, ABS active, gear, speed, RPM, engine warning bits, oil/water/fuel pressure, voltage.
- Fuel and Pit Service: fuel level/use, pit service status, requested fuel/tires/repair, fast repair, tire sets, `DCDriversSoFar`, and `DCLapStatus`. The dedicated Pit Service tire inventory corpus now covers limited NASCAR tire inventory, PCup unlimited-practice counters, and NASCAR weight-jacker request fields.
- Session / Weather: wetness, declared wet, precipitation, air/track temp, wind/fog/humidity where exposed, and solar fields.
- Flags/start behavior: global/per-car session flags, pace mode, pace row/line, and pre-green/grid states.
- Driver directory identity shape: redacted `hasUserName`, `hasTeamName`, `hasCarScreenName`, class-name fallback availability, and counts by class/team fields.
