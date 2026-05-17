# Relative Overlay Logic

The Relative overlay is the first production overlay using the model-v2 live state as its primary input. It is intentionally a simple telemetry-first window: nearby cars, the local/reference row, and quiet source text only when the source is waiting, partial, or timing fallback.

## Code Paths

- `src/TmrOverlay.App/Overlays/Relative/RelativeOverlayDefinition.cs`
- `src/TmrOverlay.App/Overlays/Relative/RelativeBrowserSource.cs`
- `src/TmrOverlay.App/Overlays/Relative/RelativeBrowserSettings.cs`
- `src/TmrOverlay.App/Overlays/Relative/RelativeOverlayViewModel.cs`
- `src/TmrOverlay.App/Overlays/Relative/RelativeForm.cs`
- `src/TmrOverlay.Core/Telemetry/Live/LiveRaceModelBuilder.cs`

Browser review is the primary local design surface for Relative. The deprecated mac harness still has a legacy mirror in `local-mac/TmrOverlayMac/Sources/TmrOverlayMac/Overlays/Relative/`, but it is not the product parity target.

## Inputs

The Windows overlay reads:

- `LiveTelemetrySnapshot.Models.Reference` for normalized focus/player/reference facts.
- `LiveTelemetrySnapshot.Models.Relative` for relative rows and reference car index.
- `LiveTelemetrySnapshot.Models.Timing` for the reference row position/class context and pit-road status.
- `LiveTelemetrySnapshot.Models.Scoring` for scoring-only enrichment of rows that already exist in relative telemetry.
- `LiveTelemetrySnapshot.Models.DriverDirectory` for car number, driver name, class label, and class color.
- Snapshot connection, collection, sequence, and freshness metadata.

It treats snapshots older than 1.5 seconds as stale and shows a waiting state instead of keeping old rows visible.

## Row Selection

`RelativeOverlayViewModel.From` builds:

1. Cars ahead from `LiveRelativeRow.IsAhead`.
2. Cars behind from `LiveRelativeRow.IsBehind`.
3. One reference row from the normalized reference model, then `Relative.ReferenceCarIdx` and timing focus references.

Relative does not promote `PlayerCarIdx` to the reference row when `CamCarIdx`/focus is unavailable. If the focus car is missing, the overlay waits with `waiting for focus-relative telemetry` instead of showing a player-centered view the user did not ask for. When focus is the player/team car, model-v2 can still use team/local timing fields to fill missing focused-car details because those fields describe the same car.

Local off-track, garage, and pit-context suppression only applies when the relative reference is the local player/team car. If the current focus is a non-player AI/spectated car and live relative/timing evidence exists for that focus, the local player's garage or off-track state does not suppress the whole Relative overlay.

Cars ahead and behind are sorted by the best available absolute relative value:

1. Relative seconds.
2. Relative meters.
3. Relative laps.

The nearest configured cars are selected first. Ahead rows are then rendered farthest-to-nearest above the reference row, while behind rows render nearest-to-farthest below it. User settings cap each side from 0 to 8 cars, defaulting to 5 ahead and 5 behind.

The live table uses fixed slots once a reference row exists: configured ahead slots, the reference row, then configured behind slots. Empty slots stay blank so the reference row and table height do not jump when nearby cars enter or leave. This lets live numbers update in place instead of forcing the whole relative table to re-layout every tick. Waiting and unavailable states still collapse to one placeholder row.

The reference row stays between the selected ahead and behind rows instead of drifting into a standings-style sort.

Scoring snapshot rows do not create Relative rows. If a car exists only in scoring/results data, it is deliberately excluded from Relative because there is no honest live relative placement for it. Scoring can only enrich cars that already have relative/timing placement.

Pit-road cars are allowed to stay in Relative when they come from live nearby-car telemetry. Radar and spatial placement still exclude them, but Relative keeps them visible so the driver can see pit-lane cars being passed.

## Display Rules

Rows show:

- Position from the configured content column, usually class position first and overall position as fallback, shown as a plain number rather than a `C<N>` or `P<N>` label.
- `#<car number> <full driver name>` when a car number is known.
- `0.000` for the reference gap.
- `-<value>` for cars ahead and `+<value>` for cars behind, regardless of whether the source row came from proximity or timing fallback.
- Seconds first, then physical distance when available. Lap-fraction-only rows render unavailable instead of showing `L` units in the Relative overlay.
- Optional pit column when enabled in Content settings.

Relative uses the shared content-column manager. Columns have Relative-owned option keys and drive both native/localhost rendering plus the OBS size recommendation. The current V1 Design V2 settings surface exposes visible content rows plus the rows-around-focus count control; advanced table width/order editing is deferred to the future V2 custom settings slideout. The default content keeps the table compact: position, full driver name, and gap are visible; pit is available but disabled by default.

Normal rows are quiet. Reference rows are visually emphasized; pit rows remain visible but are de-emphasized with muted text/background so the driver can see they are being passed in pit lane rather than treated as an on-track threat. Fully degraded rows are muted.

Whole-lap relationships are visual text states, not extra columns. A car one lap ahead of the reference uses light red text, two or more laps ahead uses the stronger error red, one lap behind uses light blue text, and two or more laps behind uses stronger blue text. This text color is independent of car class; class color remains a row accent/background cue.

Relative seconds come from live proximity timing when available. If proximity has only lap-distance placement, the model-v2 relative row can infer a display seconds gap from live lap-distance delta multiplied by the current local/focus lap-time signal. Radar does not consume that inferred seconds value; it remains stricter and uses only live proximity seconds or physical distance for proximity placement.

Race `SessionState == 3` can use model-v2 timing fallback from positive `CarIdxEstTime` plus valid `CarIdxLapDistPct` so Relative can update during the pre-green roll to the line. This fallback is not created from grid rows alone. It also remains useful when the local player tows or sits in pit lane with valid timing/progress facts, because iRacing itself continues showing estimated relative gaps in those contexts. Radar remains stricter and still requires local in-car spatial context.

## Source And Status Text

Status shows the reference position and visible car count, such as `6 - 10 cars`. If more relative rows are available than the configured window shows, it uses `shown/available`, such as `6 - 10/14 cars`.

Source text is intentionally low emphasis:

- `source: live proximity telemetry` when rows come from proximity.
- `source: model-v2 timing fallback` when relative rows come from timing/class-gap fallback.
- `source: partial timing` when rows are fully degraded or partial.
- `source: waiting` when no fresh relative telemetry exists.

## Waiting And Error States

The overlay shows waiting when:

- iRacing is disconnected.
- Live collection has not started.
- The latest snapshot is stale.
- There is no focus/reference row.

Unexpected refresh/render failures are logged through the overlay logger and surfaced as a compact visible `relative error` state.

## Managed Overlay Behavior

`OverlayManager` registers Relative as a normal managed driving overlay:

- Default id: `relative`.
- Default size: `360x373`.
- Default position: `(650, 24)`, near the right-side driver stack.
- Settings expose a symmetric rows-around-focus count and Content-tab row visibility controls.
- Theme font, visibility, scale, opacity, content/header/footer session gates, and persistence follow the same managed-overlay behavior as the other product overlays.

The localhost route also reads `/api/relative` so it can honor the same rows-around-focus and content settings as the native overlay. Browser review replay validation should spoof settings/API options when testing content and header/footer states, while keeping relative row values, lap relationships, class positions, and timing evidence capture-derived when available.
