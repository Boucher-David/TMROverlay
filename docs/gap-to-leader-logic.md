# Gap To Leader Logic

This file explains how the class gap trend graph derives and draws its data.

Implementation files:

- `src/TmrOverlay.Core/Telemetry/Live/LiveTelemetrySnapshot.cs`
- `src/TmrOverlay.Core/Telemetry/Live/LiveRaceModels.cs`
- `src/TmrOverlay.App/Overlays/GapToLeader/GapToLeaderLiveModelAdapter.cs`
- `src/TmrOverlay.App/Overlays/GapToLeader/GapToLeaderForm.cs`

## Purpose

The gap-to-leader overlay is a live in-class race trend graph. It shows how the currently focused car and nearby same-class cars relate to the nearest eligible same-lap reference over time. That reference is normally the class leader, but it can move down the order when the focused car is more than a lap behind the cars ahead.

The overlay is race-only product UI. The settings tab omits ordinary display-in-session controls, and the overlay manager normalizes its visibility to race sessions only.

It is separate from radar. Radar needs live proximity placement. The gap graph can use same-class timing rows even when lap-distance placement is unavailable.

## Refresh Loop

The overlay refreshes on its UI timer.

Each refresh:

1. Reads the latest live telemetry snapshot.
2. If the snapshot sequence changed, records a new gap snapshot.
3. Updates labels and status only when values changed.
4. Invalidates for repaint only when the snapshot sequence, labels, or colors changed.
5. Records performance submetrics plus input age, input-change rate, and whether the tick needed a repaint.

Snapshots are ignored when no new sequence exists.

## Live Gap Input

Gap V2 reads promoted model state first:

- `LiveReferenceModel` supplies the normalized focus/player/reference identity so Gap To Leader follows the same focus facts as Standings, Relative, Track Map, and diagnostics before applying graph-specific reference selection.
- `LiveTimingModel` supplies focus/class rows, class leader identity, gap evidence, class positions, deltas to the focused car, and same-class rows for graphing.
- `LiveRaceProgressModel` supplies reference/leader lap-progress gaps and race-progress fallback when second-based timing evidence is partial or unavailable.
- `LiveWeatherModel`, `LiveRaceEventModel`, `LiveDriverDirectoryModel`, and `LiveFuelPitModel.Fuel` supply the supporting graph context previously read directly from the latest sample.

`LiveLeaderGapSnapshot` remains a compatibility/fallback slice. `GapToLeaderLiveModelAdapter` converts the promoted model state into the graph's existing internal row shape and falls back to `LiveLeaderGapSnapshot` only when promoted model data is absent.

The graph input contains:

- Overall leader gap.
- Class leader gap.
- A same-class car list for graphing.

Reference car:

- Prefer the normalized reference car from `LiveReferenceModel`, which uses `CamCarIdx` when the camera focus car is valid.
- Do not fall back to `PlayerCarIdx` when focus is unavailable; local-player fallback remains an overlay-specific rule for local-only contexts, not the shared reference model.
- Fuel and Pit Service use local player/team telemetry only after `LiveLocalStrategyContext` confirms the focus is the player car. Compact history and strategy history remain player/team scoped, but Fuel/Pit do not display that strategy surface while the user is focused on another car.

Reference progress:

- Prefer focused-car lap completed plus focused-car lap distance.
- Fall back to team/player progress only when the focus car is the player car.
- When the camera focus is missing or focused on a non-player car with missing progress, missing focus progress stays unavailable instead of silently using team progress.

## Leader Gap Values

For model-v2 class leader gaps, the first valid rule wins:

1. If reference position is 1, or leader car id equals reference car id:
   - Gap is zero.
   - Source is `position`.
2. If the timing row's gap evidence is usable and second-based gap exists:
   - Use `GapSecondsToClassLeader`.
   - Keep the timing evidence source, normally `CarIdxF2Time`.
3. If the timing row's gap evidence is usable and lap-based gap exists:
   - Use `GapLapsToClassLeader`.
   - Keep the timing evidence source.
4. If promoted race progress has a reference-to-class-leader lap gap:
   - `gapLaps = max(0, leaderProgress - referenceProgress)`
   - Source is `LiveRaceProgress`.
5. Otherwise unavailable.

Valid second gaps must be finite, non-negative, and less than 86400.
Race timing evidence must also be meaningful: all-zero `CarIdxF2Time`/estimated-time frames from early green or degraded AI/spectated states are not treated as reliable class-gap evidence. Positive F2 timing is required for derived class-gap and focus-delta calculations except for an explicit leader/reference zero row.

Data review note from the May 2026 capture analysis:

- Long raw captures confirmed `CarIdxF2Time` is the best available source for class-gap seconds when leader and reference rows are both valid.
- Some sampled frames had a valid reference `CarIdxF2Time` while the leader F2 row was missing or not trustworthy. Model-v2 gap evidence marks those rows partial, so the graph does not use the misleading second gap and falls back to race-progress lap gap when available.
- Same-class timing rows can have standings/F2 data while lap-distance progress is missing. The graph should keep accepting those rows for timing, but it should flag the source as partial and avoid using them for radar-style placement.
- Large class-gap jumps can happen around leader changes, session transitions, pit windows, and stale/uninitialized timing rows. Model-v2 should carry source quality and missing-leader reasons so the overlay can segment or de-emphasize those points instead of drawing them as normal continuity.

Long-race feedback changed the reference behavior from "always leader" to "nearest eligible same-lap reference." The graph limits the +/- comparison field to cars on the same lap as the focused car. If P1/reference crosses more than one lap ahead, the graph switches to the next same-lap in-class car such as P2 or P3. If the focused car unlaps itself from P1, P1 becomes eligible again. Out-of-reach cars can still be named as `P<N> +N lap` reference context, but they do not draw the white same-lap reference line.

The compact Dallara pit-window replay fixture `gap-to-leader-pit-window-real-data` proves the production browser model keeps the leader/ahead/focus series selected through pit entry, active pit road, exit, and post-exit stabilization while excluding far-behind outliers. It also proves the `Pit`/`PLap` metric state transitions from active duration to the completed pit duration/lap. Browser/localhost/native screenshot replay for that compact fixture is still pending.

## Same-Class Car List

The graph list always attempts to include:

- Class leader row.
- Focused/reference car row.
- Same-class candidate rows.

Candidate source is `LiveTimingModel.ClassRows` when model-v2 timing is available. Those rows already merge focus, player/team, class leader, focus-class rows, class-gap rows, and nearby rows under the shared timing contract.

For each candidate:

1. Keep class leader, focused/reference, usable-gap, and focused-delta rows.
2. Use second-based gap only when row `GapEvidence.IsUsable`.
3. Use lap-based gap only when row `GapEvidence.IsUsable`.
4. For the focused/reference row, fall back to `LiveRaceProgressModel.ReferenceClassLeaderGapLaps` when timing evidence is partial.
5. Use delta to the focused car only when the row's gap evidence is usable.
6. Skip rows that have no chartable gap and no focused-car delta, unless they are the class leader or reference row.

Duplicate car ids are grouped. Focused/reference rows win, then class leader rows.

## Chart Gap Seconds

The drawing code needs seconds for the Y-axis.

For each `LiveClassGapCar`:

- Use `GapSecondsToClassLeader` when available.
- Otherwise convert laps to seconds using a lap reference, falling back to 60 seconds when no valid reference is available.

Lap reference is selected from live lap-time context and kept only when it looks valid.
The compact Design V2 native and localhost gap graphs use the same conversion for their focused-car point buffer, so lap-distance placement fallback still advances the trend when second-based leader timing is partial or missing.

## Recording Points

When a new snapshot is recorded:

1. Choose timestamp from latest sample, last updated time, or now.
2. Choose axis seconds:
   - Use `LiveSessionModel.SessionTimeSeconds` when finite and non-negative.
   - Otherwise use Unix time seconds.
3. Store latest timestamp and axis seconds.
4. Set trend start to the first recorded axis seconds.
5. Record weather condition.
6. Record driver-change markers.
7. Record leader-change markers.
8. For each class car with chart gap seconds:
   - Add or replace a `GapTrendPoint`.
   - Mark a new segment if the gap since the previous point is more than 10 seconds.
   - Keep at most 36000 points per car.
9. Update render state for each car.
10. Prune points older than the four-hour window.

## Render State Selection

Every car gets a render state when seen.

Always desired:

- Focused/reference car.
- Current same-lap reference car.

Additional desired cars:

- Nearest same-class cars ahead of the focused car, up to the `GapCarsAhead` setting.
- Nearest same-class cars behind the focused car, up to the `GapCarsBehind` setting.
- When either setting changes, the currently recorded render states are re-targeted immediately so the graph updates without waiting for a new sample.

Ahead/behind is based on delta seconds to the focused car:

- Negative delta means ahead of the focused car.
- Positive delta means behind the focused car.

Recently desired cars stay visible for continuity.

Sticky visibility duration:

- At least 120 seconds.
- Or 1.5 laps when a valid lap reference makes that longer.

## Entry, Exit, And Missing Telemetry

When a car becomes desired:

- It fades in over 45 seconds.
- Its line can include a 300 second entry tail so it does not appear as a disconnected stub.

When a car is no longer desired:

- It fades out over the sticky visibility window.
- It is drawn dashed while exiting.

When a car has not been seen for more than 5 seconds:

- It is stale.
- It is drawn dashed.
- A small terminal marker is drawn at the last point.

## X-Axis Domain

The trend window is four hours.

Before four hours of data exists:

- X-axis starts at the first visible or first recorded sample.
- End starts at a readable minimum window, currently at least 120 seconds or 1.5 valid reference laps.
- The right edge adds a small look-ahead pad, currently at least 20 seconds or 0.15 valid reference laps.
- The visible duration grows as the race develops, capped at four hours, so early-race lines stay readable instead of being compressed into a full endurance-race width.

After more than four hours exists:

- Window slides to the latest four hours.

## Y-Axis Domain

Y-axis maximum is based on visible points in the current X-axis domain.

1. Find maximum visible gap seconds.
2. Ensure it is at least 1 second.
3. Round up to a nice ceiling.

The current same-lap reference baseline is always drawn at the top. When that reference is the class leader, the Y-axis label still reads `Leader`; when the focused car is lapped, the label uses the nearest eligible position context instead of pretending the out-of-reach leader is still a comparable zero line.

The localhost graph uses the same orientation: the zero/reference line is the top of the plot and larger positive gaps draw downward. Browser review validation rejects invalid, negative, and outlier gap points before drawing so a placeholder sample cannot invert the chart or drag the leader baseline to the bottom.

## Drawing Order

The graph draws:

1. Weather bands.
2. Grid lines.
3. Leader-change markers.
4. Current reference baseline.
5. Selected car series.
6. Endpoint position labels.
7. Driver-change markers.
8. Scale labels.

## Series Drawing

For each selected car:

- Focused/reference car gets the strongest line.
- Class leader gets a leader-style line.
- Other cars get thinner, dimmer lines.
- Stale or sticky-exit cars use dashed lines.
- Missing telemetry gaps start new line segments.
- Single-point segments draw as points.
- Last point gets an endpoint dot.

Endpoint labels:

- Use current class position as `P<N>`.
- Avoid overlapping labels by stacking them vertically.
- Draw connector lines when label Y differs from point Y.

## Weather Bands

Weather points are recorded from live wetness and declared-wet signals.

Bands fill the graph background between weather samples:

- Dry or unknown: no band.
- Wet states: subtle blue/gray band.
- Declared wet: band plus a top strip.

## Driver Change Markers

Driver change markers come from:

- Explicit team driver-change signal when available.
- Session-info driver row changes by car index.

Markers are drawn on the affected line with:

- Vertical tick.
- Dot.
- Small label.

Focused-car markers use the main accent. Explicit team driver-change markers still only apply when the focused/reference car is the team car.

## Leader Change Markers

When class leader car id changes:

- Add a vertical dotted marker.
- Label it `leader`.

The leader baseline is a role. The old leader's car can continue as a normal trace after losing the lead.

## Source And Status Text

When gap data exists:

- Status shows focused-car class position and class leader gap.
- Source row shows four-hour class trend and selected car count.

When no gap data exists:

- Status is `waiting`.
- Source is `source: waiting`.

## Design Notes

- Keep radar and gap inputs separate.
- Prefer F2 timing for gap seconds.
- Use lap progress fallback for trend participation when seconds are unavailable.
- Keep old leaders and recently visible cars understandable instead of disappearing abruptly.
- Make early-race graphs grow from the left until four hours are filled.

## 24-Hour Race Findings

Live endurance-race review found several product issues that should guide the model-v2 migration:

- Practice, qualifying, and test sessions make this graph misleading because the useful comparison is not race-position gap to class leader. Keep the race-gap view race-oriented unless a dedicated non-race timing mode exists.
- Multi-lap leader gaps can make local position battles unreadable, especially on long laps. The current graph mitigates this by keeping +/- comparisons same-lap and switching the reference to the nearest eligible in-class car when the leader is more than a lap ahead. Future graph modes can still add richer pit-strategy or alternate-reference context if endurance review shows the same-lap rule hides useful strategy information.
- Threat and leader summary text did not handle pit cycles and on-track pace differences well. That interpretation may belong in future relative, standings, or strategy overlays rather than in this graph.
- The graph should be tested for more frequent update cadence. The collector reads `CarIdxPosition` and `CarIdxClassPosition` every frame, but we still need to verify from race captures whether iRacing updates those positions continuously or only around lap/scoring events.
