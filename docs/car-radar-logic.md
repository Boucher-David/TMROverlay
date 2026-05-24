# Car Radar Logic

This file explains how the car radar decides when to appear, where cars are drawn, and how proximity is colored.

Implementation files:

- `src/TmrOverlay.App/Cars/CarSpecificationCatalog.cs`
- `src/TmrOverlay.Core/Telemetry/Live/LiveRaceModels.cs`
- `src/TmrOverlay.Core/Telemetry/Live/LiveRaceModelBuilder.cs`
- `src/TmrOverlay.Core/Telemetry/Live/LiveTelemetrySnapshot.cs`
- `src/TmrOverlay.Core/Telemetry/Live/LiveTelemetryStore.cs`
- `src/TmrOverlay.App/Overlays/CarRadar/CarRadarOverlayViewModel.cs`
- `src/TmrOverlay.App/Overlays/CarRadar/CarRadarRenderModel.cs`
- `src/TmrOverlay.App/Overlays/DesignV2/DesignV2LiveOverlayForm.cs`
- `src/TmrOverlay.App/Overlays/BrowserSources/Assets/modules/car-radar.js`

## Purpose

The radar is a live proximity overlay. It is not a historical or replay overlay.

The production radar is local-player in-car only. The native overlay window is eligible to show only when a valid local player car exists, that car is on track, not in the garage or pit context, and the active camera focus resolves to the local player car. When focus is unavailable or explicitly another car, the production radar is hidden instead of trying to reinterpret local side telemetry for that watched car.

It renders from `LiveTelemetrySnapshot.Models.Spatial`, which is populated from the local-only live proximity model. It uses fresh live telemetry only:

- Local player/team lap-distance, timing, and car-class fields. `Focus*` fields are used only when focus is not explicitly another car.
- `CarLeftRight` for side occupancy.
- Nearby `CarIdxLapDistPct` and lap completion for physical-distance placement when track length is known.
- Nearby `CarIdxEstTime` or `CarIdxF2Time` for diagnostics, Relative, and optional faster-class warning context when available and plausible.
- Session YAML class pace fields (`CarClassRelSpeed`, with `CarClassEstLapTime` as fallback) to decide which other classes are actually faster than the local class.

Non-local focus and teammate/spectator camera states are collected as diagnostics and future model-v2 analysis inputs. They are not the first product radar contract.

If live timing is missing or suspicious, the car may remain in the live proximity snapshot for diagnostics and non-radar consumers. Radar car rectangles require physical distance from live lap-distance progress plus known track length. Timing-only rows do not draw radar targets.

## Freshness

The overlay reads `ILiveTelemetrySource.Snapshot()` every 100 ms.

The snapshot is usable only when:

- Connected.
- Collecting.
- `LastUpdatedAtUtc` exists.
- Snapshot age is between 0 and 1.5 seconds.

Stale snapshots are treated as unavailable so old traffic does not remain painted.

## Live Proximity Input

`LiveProximitySnapshot.From` derives local-only proximity from the current telemetry sample. `LiveRaceModelBuilder.BuildSpatial` then projects the same local radar contract into model v2 so the Windows radar reads `LiveSpatialModel` rather than the legacy `LiveProximitySnapshot` slice.

1. Check the local radar context:
   - Require the local player/team car to be on track.
   - Require a valid local `PlayerCarIdx`.
   - Hide the radar while the user is in garage/replay/off-track context.
   - Hide the radar when `CamCarIdx`/focus is unavailable or explicitly another car.
2. Determine local-car lap distance:
   - Use `FocusLapDistPct` only when focus is local or not explicitly non-player.
   - Fall back to team/player lap distance.
3. If local lap distance is unavailable:
   - Keep side occupancy from `CarLeftRight` when the local radar context is valid.
   - Mark nearby cars unavailable.
4. If the local car is on pit road or in a pit track-surface state:
   - Mark the radar unavailable.
   - Hide side occupancy.
5. Convert each nearby car:
   - `relativeLaps = car.LapDistPct - localLapDistPct`
   - Wrap across start/finish so values stay within `-0.5` to `0.5`.
   - Preserve position, class, track surface, pit-road flag, F2 time, and estimated time.
   - Calculate relative meters when track length exists.
6. Exclude cars on pit road or in pit track-surface states.
7. Keep cars where absolute relative laps is at most `0.5` and not effectively zero.
8. Sort by absolute relative laps.
9. Nearest ahead is the smallest positive relative laps.
10. Nearest behind is the largest negative relative laps.

Pit-road cars do not appear on radar. The gap overlay can still use separate timing rows for race context.

## Relative Seconds

Relative seconds are optional.

The first valid source wins:

1. `CarIdxEstTime` difference:
   - `delta = carEstimatedTimeSeconds - focusEstimatedTimeSeconds`
   - If a live lap time exists, wrap deltas larger than half a lap.
   - Accept only when plausible against relative lap direction.

2. `CarIdxF2Time` difference:
   - `delta = focusF2TimeSeconds - carF2TimeSeconds`
   - Accept only when plausible against relative lap direction.

3. Otherwise, relative seconds is null.

Plausibility rules:

- Delta must be finite.
- Near-zero deltas are rejected when lap-distance progress says the car is meaningfully separated. This guards against uninitialized timing rows such as `CarIdxF2Time == 0` for multiple cars.
- Delta sign must match relative lap sign when both are non-zero.
- If live lap time exists:
  - Calculate lap-distance estimate: `abs(relativeLaps * lapTimeSeconds)`.
  - Maximum accepted delta is `max(5, min(lapTime / 2, lapDistanceEstimate + 10))`.
- If live lap time does not exist:
  - Absolute delta must be at most 60 seconds.

Live lap time for this plausibility check comes from current local live lap-time fields only:

- Focus last/best lap only when focus is local.
- Team last lap.
- Team best lap.
- Player last lap.
- Player best lap.

It does not come from fuel strategy or history.

## Side Occupancy

`CarLeftRight` maps to side labels:

- `0`: off.
- `1`: clear.
- `2`: left.
- `3`: right.
- `4`: both sides.
- `5`: two left.
- `6`: two right.
- Missing: waiting.

Left side is active for `2`, `4`, and `5`.

Right side is active for `3`, `4`, and `6`.

`CarLeftRight` is a player-car scalar. When the camera is focused on another car, side occupancy is hidden instead of applying the player's side warning to the watched car.

For side-by-side placement, `CarLeftRight` is necessary but not sufficient. Timing never creates an alongside state by itself, and a side marker is shown only when the current spatial model can attach the side signal to a close physical car candidate.

When a side warning exists, the radar may attach that side slot to a rendered decoded car only when:

- The car has reliable relative meters inside the side-attachment window.

The side-attachment window uses the current local car body-length model and spans two local-car body lengths.

If no physically placed car qualifies, the radar suppresses the side-warning rectangle from `CarLeftRight`. This avoids false side warnings from stale or technical spotter state when the only decoded car is far laterally or longitudinally away. The raw `CarLeftRight` state remains diagnostics evidence, but it is not rendered as a side marker without an attachment candidate.

When a side warning is active and a nearby physically placed car is close enough to be the likely source of that warning, the radar attaches that car to the side slot and suppresses the same car's normal center-lane rectangle. This avoids showing one opponent twice during a pass. The side marker is biased slightly forward or backward from the local car based on the car's longitudinal gap, so a pass that has moved to the front-right/front-left does not keep looking like a centered side block.

Data review note from the May 2026 capture analysis:

- Long Nürburgring captures showed many frames with physical/timing proximity candidates but no side signal, and a smaller number of frames with a side signal but no clean same-frame contact candidate.
- That supports keeping the current split: lap-distance/track-length and timing are longitudinal proximity inputs, while `CarLeftRight` remains the side-occupancy gate.
- Side warnings now require both sources: `CarLeftRight` plus a close decoded placement candidate. Frames with side signal but no placement stay visible in diagnostics as suppressed partial evidence rather than user-facing warning geometry.

## Radar Visibility

The radar fades in when any current signal exists:

- Overlay error.
- Settings preview mode.
- Left side occupancy with an attached close spatial candidate.
- Right side occupancy with an attached close spatial candidate.
- At least one car in radar range.
- Faster-class warning.

It fades out when no signal exists.

Settings preview mode is enabled while the radar settings tab is selected. Preview mode forces the radar visible even if normal visibility rules would hide it.

The Windows form clips the overlay window to a circular region, uses a black transparency key instead of the old fuchsia key, and drives form opacity to zero when the radar has fully faded. That is a defensive transparency fallback: if the WinForms transparency key fails in a particular desktop/compositor path, the backing window is no longer purple and the hidden radar still fades out completely.

## Radar Range

Radar car range is:

- 6 local-car lengths. The length source is exact bundled car spec, trusted user calibration, bundled estimate, then the default `4.746 m` fallback.

Cars are in range when physical relative meters are inside that range. Timing-only cars are deliberately excluded from radar placement because this overlay is a local safety instrument, not a relative table.

There is also a conservative timing-aware visibility envelope for fast closures:

- If a car is outside the physical range but has reliable relative meters and relative seconds, infer `abs(relativeMeters) / abs(relativeSeconds)`.
- The visibility envelope expands up to 2 seconds of inferred closure distance.
- The expansion is capped at 15 local-car lengths, currently `4.746 m * 15 = 71.19 m`.
- This only affects whether a distant fast-closing car can fade in faintly at the radar edge. Placement is still clamped to the physical radar edge, and proximity color still uses physical bumper gap.

Range ratio:

- `relativeMeters / VisualRadarRangeMeters`.
- Clamped from `-1` to `1`.

Range ratio drives color and ordering.

Longitudinal placement uses signed relative meters with a car-length contact window:

- `0.0 m` maps to the local car rectangle.
- A non-zero car whose absolute gap is at least the local-car-length contact window is placed outside the local car rectangle.
- A car rectangle should overlap the local car only when the reliable distance gap is inside that contact window. In practice, that visual overlap means contact, a near-contact stack, or an actual side-overlap/alongside condition.
- Outside the contact window, remaining distance is scaled out to the radar edge.

Negative values draw behind the local car. Positive values draw ahead.

## Fade State

Each refresh updates:

- Whole radar alpha.
- Left warning alpha.
- Right warning alpha.
- Per-car visual alpha.

Fade timing:

- Fade in: 0.25 seconds.
- Fade out: 0.85 seconds.

Cars are tracked by `CarIdx`. If a car disappears from current range, its visual alpha fades out before the visual state is removed.

The refresh loop records whether the timer tick saw a new live snapshot sequence, how old that input was, and whether the tick actually needed a repaint because the snapshot, opacity, or fade state changed.

## Drawing

The radar draws:

1. Circular dark radar background.
2. Optional faster-class warning arc.
3. Two range rings with labels.
4. Nearby car rectangles.
5. Side-warning rectangles.
6. Focused car rectangle.
7. Ring and warning labels.

Ring labels show timing boundaries rather than meter labels:

- Inner/outer labels show timing boundaries inside the radar range, for example `1.3s` and `0.7s`.
- Timing remains available to Relative and diagnostics, but it does not place radar cars.
- The outer distance ring uses the design cyan/blue stroke. The faster-class warning arc stays warm red so the two signals do not compete.

## Car Color

The production car fill is neutral proximity color, not iRacing class color. Class color is used as a thin rectangle border so multiclass context is visible without making normal traffic look like a warning.

Proximity color does not begin across the full radar range; it begins only when bumper gap is inside the close warning buffer:

- With relative meters, bumper gap is `abs(relativeMeters) - 4.746 m`.
- The warning buffer is currently `2.0 m`, roughly one rendered car width in the radar UI.
- Outside that buffer, the car stays white.
- Inside that buffer, the car blends from white toward yellow, then saturated alert red.
- At nose-to-tail contact or overlap, the car reaches alert red.

Timing-only nearby cars do not draw radar rectangles, so there is no timing fallback proximity color.

The focused/user car remains white. Its border, and nearby car borders, use iRacing class colors so multiclass context is visible without taking over the proximity fill color.

The car alpha is based on visual fade, radar fade, physical distance inside the radar range, and the capped timing-aware visibility envelope. A car entering at the outer radar range is faint, then fades toward full opacity by the time it reaches the yellow-warning threshold. The overlay itself fades in/out; cars do not turn purple as a proximity state.

## Lateral Placement

The radar does not have true lane-level lateral telemetry.

Approximation:

- `CarLeftRight` creates side slots: one left, one right, both sides, two left, or two right.
- A rendered car can occupy a side slot only when physical distance places it within a close side-attachment window around the local car. The side signal remains the lateral gate, but it must be paired with an attachment candidate; timing does not select a decoded car for the side marker.
- Otherwise distribute multiple radar cars across three simple lanes based on `CarIdx` and draw index.
- A single visible car is centered.

## Faster-Class Warning

`LiveTelemetryStore` tracks short per-car local proximity history and can build early faster-class warnings from other-class traffic behind the local car.

Only classes faster than the local class qualify:

- Prefer iRacing `CarClassRelSpeed` from session YAML.
- If relative speed is missing or equal, fall back to lower `CarClassEstLapTime`.
- If neither signal can establish ordering, suppress the warning instead of guessing.

The early-warning seconds range is outside the physical radar-car range:

- Physical radar-car range: 6 local-car lengths when track length is known.
- Faster-class warning range: greater than 2 seconds behind and up to 5 seconds behind.

If multiple faster-class cars are in range, the warning uses the nearest reliable relative-seconds gap behind the local car, then urgency as a tie-breaker. The label reads `Faster class approaching Ns` and counts down from the real relative gap.

When the local radar context is unavailable, or the local reference car changes, the short closing-rate history is reset so approach rates measured against an old reference are not applied to the next local radar frame.

The radar can draw:

- A red outer arc.
- A small text label with the relative seconds when known.

This is still live-only derived state. It is not persisted into compact history.

## Radar Calibration History

Session history now stores a small radar calibration evidence scaffold:

- Clean `CarLeftRight` side-window durations are collected while the local player car is on track, out of garage and pit context, moving, and attached to a valid player car index.
- `SessionState = 3` pre-grid/rolling-to-grid rows remain valid evidence. Those rows often put cars beside each other in ordered pairs and can provide strong side-window samples.
- Summary fields are explicitly marked by confidence flags such as `carleft-right-clean-transition`, `identity-backed-window`, `identity-backed-body-length`, `no-car-identity`, and `not-live-consumed`.
- Learned body-length calibration is stored at `history/user/cars/{carKey}/radar-calibration.json`, not inside the car/track/session aggregate.
- Exact bundled specs in `Assets/CarSpecs/car-specs.json` are treated as already calibrated and win over learned history.
- Trusted learned calibration is used for cars without an exact bundled spec. Once a learned body-length metric is trusted, the car-level aggregate stops accepting new samples.
- Low-confidence bundled estimates are only fallback data; trusted learned user calibration wins over them.

A compact fixture derived from the four-hour Nurburgring capture keeps this behavior covered without committing the raw multi-GB capture. That capture produced 13 clean side-window samples from 35 raw side windows, with a mean clean overlap duration of about `0.619s`.

## Design Notes

- Keep radar logic live-only.
- Do not use fuel estimates or historical lap times to invent radar seconds.
- Prefer physical distance from lap-distance progress and track length for radar thresholds.
- Use live relative seconds as a fallback and for faster-class warning timing.
- Do not render pit-road traffic on radar.
- Color should stay neutral white until close bumper-gap proximity, then move through yellow toward saturated alert red.
- Car opacity should also communicate early proximity: faint at the radar edge, full near the yellow-warning threshold.
- Visibility should be communicated by alpha fade only.
- Consume only exact specs or trusted car-scoped calibration for body-size changes; weak side-window evidence remains diagnostics/history only.

## 24-Hour Race Findings

Live endurance-race review found that radar focus handling needs a deeper model-v2 pass before it should be treated as mature:

- The radar did not handle arbitrary focused cars well.
- It also struggled when the user's team car was active but another driver was in the car.
- Faster-class warning likely failed for the same root reason: focus-relative timing/progress and local-player side occupancy are different signal families.

Current product direction is to keep the production radar local-only while the diagnostics collector captures the suppressed/partial cases:

- `radar.local-suppressed-non-player-focus` for spectator or teammate focus.
- `radar.local-unavailable-pit-or-garage` for local off-track/garage/pit contexts.
- `radar.local-progress-missing` when side/timing context exists but local lap progress is unavailable.
- `radar.side-without-placement` when `CarLeftRight` reports side pressure without a close decoded placement candidate.

Future radar work can consume model-v2 focus-relative placement evidence, preserve `CarLeftRight` as local-player side occupancy only, and clearly suppress or relabel partial states when the current focus cannot support safe radar placement.
