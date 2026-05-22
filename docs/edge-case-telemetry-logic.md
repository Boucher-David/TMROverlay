# Edge-Case Telemetry Logic

This file explains the compact edge-case reports written after live telemetry sessions when edge-case collection is enabled. The collector is enabled by default for tester builds so diagnostics bundles include bounded unusual-state evidence while raw capture remains opt-in.

Implementation files:

- `src/TmrOverlay.Core/Telemetry/EdgeCases/TelemetryEdgeCaseDetector.cs`
- `src/TmrOverlay.App/Telemetry/TelemetryEdgeCaseRecorder.cs`

## Purpose

Edge-case telemetry reports are diagnostic summaries, not raw captures. They preserve small windows around unusual live states and enough end-of-session context to explain long spectated, parked, or replay-heavy sessions.

The recorder intentionally keeps a bounded clip list. When the clip cap is reached, later observations still appear in `observationSummaries` and increment dropped counts so the report does not hide the fact that additional edge cases occurred.

## Startup, Grid, Tow, And Replay Context

iRacing can expose telemetry that looks contradictory while the car is being gridded, towed, sitting in the pits, or replaying a few seconds behind live coverage.

The detector treats these as context, not faults:

- focus/camera changes are recorded as info.
- duplicate `SessionTime` during startup/grid/tow/replay context is info.
- tire-set and fast-repair counters initializing from zero during startup/grid/tow context are info.
- replay playback near collection start is info.
- `EnterExitReset` and `PlayerCarTowTime` are watched as reset/tow context signals when they exist in the SDK schema.
- lap-distance discontinuities are recorded as active-reset/tow/pit/garage/replay context when supporting signals are present, and as warnings only when the jump has no known context.
- engine warnings while the engine appears off or unpressurized are info.

## Grid Timing Rows

On the grid, cars are physically separated by qualifying order before timing rows are meaningful. A car starting several rows ahead can have a non-zero lap-distance separation while `CarIdxF2Time` or `CarIdxEstTime` still reads zero.

During grid, race-start, pit, tow, or replay context, zero timing rows with physical separation are recorded as:

- `timing.uninitialized-start-context.<source>.car-<idx>`
- severity `info`
- a `context` field such as `stationary-grid`, `grid-or-parade`, `race-start`, `pit-or-tow`, or `off-track-or-replay`

After start context, the same zero-timing contradiction remains a warning:

- `timing.zero.<source>.car-<idx>`

This keeps expected grid behavior out of the warning bucket while preserving suspicious timing failures during live running.

## Grouped Raw Signals

Some raw channels tend to activate together and should not spend the whole clip budget as separate findings.

The detector groups these cases:

- startup engineering channels such as tire wear, tire temperature, tire pressure, suspension, brakes, and wheel speed become one `raw.startup-engineering-baseline` observation at collection start.
- active pit-service commands become one `raw.pit-commands.active` observation with a `variables` list.
- pit-service request channel changes become `raw.pit-command.changed.<variable>.<value>` observations. These include viewer context fields such as `IsOnTrack`, `IsOnTrackCar`, `IsReplayPlaying`, `CamCarIdx`, camera group/camera number/state, pit-road state, garage state, and stall state. Pair them with the `live-overlay-diagnostics.json` role context, which preserves session-info local role such as `DriverInfo.DriverCarIdx` resolving to a `Drivers[]` row with `IsSpectator`, so future work can prove whether non-driving/spotter contexts can change pit values.
- forecast-like weather channels are watched dynamically when an SDK variable name or description contains `forecast`; if one is active, the detector records `weather.forecast-signal-present` and keeps the raw values in the artifact.
- ARB/anti-roll and wing-like driver adjustment channels are watched dynamically when a future SDK schema exposes a variable name or description matching those terms.
- Clutch-like driver input channels are also watched dynamically so dual-clutch or car-specific clutch inputs can be proven in diagnostic artifacts before the production input overlay renders them.

## Artifact Contents

Each report includes:

- watched raw schema and missing watched variables
- clip triggers and bounded frame windows
- total observation count
- dropped observation count after the clip cap
- per-key observation summaries with last fields
- final sampled context frames from the end of the session
- selected nearby/class timing rows
- scalar raw watch values

Reports intentionally exclude `telemetry.bin`.
