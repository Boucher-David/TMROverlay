# Fuel Calculator V2 Design Notes

This document captures product and telemetry decisions for the future Fuel
Calculator V2 branch. The current production behavior remains documented in
`docs/fuel-calculator-logic.md`.

Fuel Calculator V2 should rebuild strategy around evidence families instead of
making the current scalar calculator more aggressive. The first family to
harden is race lap budget quality, because every fuel-to-finish, stint rhythm,
stop count, and refuel recommendation depends on how many laps the race will
actually run.

## Race Lap Budget Quality Gate

Goal: classify the race lap budget before Fuel V2 promotes strategy advice.
Lap-count races can usually use iRacing's published lap fields directly. Timed
races need stricter source and confidence handling because the app must infer
the finish lap from the session clock, leader progress, and pace.

### Product Decision

Fuel V2 must treat race lap budget as a first-class model with source,
confidence, and missing-signal details. It should not only expose a decimal
`RaceLapsRemaining` value.

User-facing strategy advice such as stop deletion, underfueling, stretch-to-one-
more-lap prompts, or pit-service refuel recommendations should require a trusted
race lap budget. When the lap budget is weak, Fuel should show constraints and
caveats instead of confident advice.

Conservative bias:

- Overestimating by one lap is acceptable for safety.
- Underestimating by one lap can ruin the race and must block aggressive advice.
- Timed-race uncertainty should widen reserve/margin rather than hide the source
  quality.

### Current iRacing Signals

Observed SDK/session sources relevant to race lap budget:

- `SessionLapsRemainEx`: iRacing's published remaining-laps value. Treat finite
  positive values below the unlimited sentinel as authoritative.
- `SessionLaps`: session YAML scheduled laps. Use as fixed-lap fallback when it
  is a real lap count, not `unlimited`.
- `SessionLapsTotal`: live total laps when finite and plausible.
- `SessionTimeRemain`: seconds left until the session ends. In timed races this
  is the main live clock, but during pre-green race phases it can be a grid
  countdown or `-1`.
- `SessionTime`: elapsed active session time.
- `SessionState`: race phase. Values below `4` can still be an active Race
  session, so timed-race clock logic must not treat pre-green countdown as race
  duration remaining.
- `RaceLaps`: laps completed in the race. Useful as a sanity signal, not a
  remaining-laps estimate.
- `CarIdxLapCompleted` and `CarIdxLapDistPct`: per-car progress; useful for
  leader, class leader, strategy car, and final-lap sanity.
- `DriverCarEstLapTime` and `CarClassEstLapTime`: seed estimates from session
  info. Useful before live pace exists, but lower confidence than completed
  race laps.
- `CarIdxEstTime`: estimated time to reach each car's current location on track.
  Useful for Relative/Gap style position context, but not a direct race-length
  estimator.
- `CarIdxF2Time`: race gap/leader timing when populated; useful as timing
  context, but not itself a lap-budget source.

No observed field should be treated as "iRacing's final estimated race lap
count" without replay proof. Current evidence points to a model built from
published lap fields, live clock, leader progress, and pace.

### Source Hierarchy

Fuel V2 should classify race lap budget sources roughly as:

1. `published-laps-remaining`: `SessionLapsRemainEx` finite, positive, and below
   the unlimited sentinel.
2. `fixed-lap-total`: finite scheduled/live lap total minus strategy car
   progress.
3. `timed-live-clock`: race is running, `SessionTimeRemain` is positive, leader
   progress exists, and race pace is trusted.
4. `timed-live-clock-no-leader-progress`: race is running, clock and pace are
   trusted, but leader progress is missing. This can seed planning but should be
   lower confidence because the final lap cannot be anchored to the leader.
5. `scheduled-pre-green`: scheduled race time divided by estimated or historical
   pace before green. This is planning-only and must not become confident advice.
6. `timed-expired-final-lap`: clock is zero or negative while the race is still
   active. Estimate from leader progress, observed final-lap behavior, and
   checkered/session-end signals, not from full scheduled race time.
7. `missing-active-clock`: race is active but live clock, progress, or pace is
   missing or contradictory.
8. `unavailable`: no defensible race lap budget.

Existing V1 behavior already handles several pieces: published laps remaining
win over timed estimates, pre-green positive `SessionTimeRemain` is not treated
as race time remaining, and rolling clean leader pace can replace a one-frame
last-lap estimate after enough clean laps. V2 should preserve those rules and
make their quality visible to strategy.

### Timed-Race Formula

For a normal active timed race, first estimate the overall leader's finish
event:

```text
estimatedFinishLap = ceil(overallLeaderProgressLaps + sessionTimeRemainSeconds / overallLeaderPaceSeconds)
leaderTimeToFinishSeconds = (estimatedFinishLap - overallLeaderProgressLaps) * overallLeaderPaceSeconds
```

Then estimate when the strategy car will receive the checkered:

```text
strategyProgressAtLeaderFinish = strategyCarProgressLaps + leaderTimeToFinishSeconds / strategyCarPaceSeconds
strategyFinishLap = ceil(strategyProgressAtLeaderFinish)
strategyLapsRemaining = strategyFinishLap - strategyCarProgressLaps
```

This separates the race's finish trigger from the strategy car's required
distance. The overall leader determines when the race reaches the checkered, but
a lapped strategy car receives the checkered at its own next line crossing after
the overall leader finishes. In multiclass races, the lower-class strategy car
must not use its own class leader to decide the race finish event, and it also
must not blindly assume it will complete the overall winner's lap count. Being a
lap down can reduce the strategy car's required distance by roughly one lap.
If strategy-car progress or pace is missing, Fuel V2 may fall back to the
overall winner's lap count as a conservative upper bound, but that must lower
confidence because it can overstate fuel required and distort stop timing.

### Lapped-Car Projection Policy

Fuel V2 needs an explicit policy for future lap-down estimates. There are two
different states:

- confirmed state: the strategy car's current progress relative to the overall
  leader, including any lap deficit that already exists.
- projected state: whether the overall leader is likely to lap the strategy car
  before the timed finish event.

The conservative default should be:

- Actionable fuel-to-finish, pit-service quantity, and stop-deletion advice use
  confirmed current lap-down state.
- Predicted future lapping can be shown as a scenario or confidence input, but
  it should not reduce required fuel for aggressive advice until the lap-down
  state is confirmed or the race is near enough to finish that the projection is
  high confidence.
- When projected future lapping is likely but not confirmed, Fuel may show both
  `current-state laps remaining` and `likely lapped laps remaining`, with the
  higher fuel requirement driving safety-critical advice.

This avoids underfueling when a predicted lapping event does not happen because
of traffic, cautions, pit cycles, class battles, or pace changes. It also lets
Fuel become more precise as the actual leader-relative gap changes, instead of
relying on a fragile early-race estimate.

Race pace should prefer:

1. Rolling overall leader pace from completed clean green laps.
2. Overall leader last lap when rolling pace is not ready.
3. Class leader pace only when overall leader pace is unavailable and the class
   context is explicitly documented.
4. Team/player strategy lap time only as a low-confidence fallback.
5. Historical or session estimated lap time only as pre-green planning seed.

### Quality Rules

The race lap budget model should carry at least:

- source id
- confidence band
- estimated finish lap
- strategy laps remaining
- leader progress
- strategy car progress
- race pace and pace source
- strategy-car pace and pace source, when needed for lapped-car projection
- session time remaining
- session state/phase
- missing or contradictory signals
- conservative reserve adjustment recommendation

Recommended confidence bands:

- `authoritative`: published remaining laps or fixed lap total.
- `high`: timed race with positive live clock, leader progress, and rolling clean
  overall leader pace.
- `medium`: timed race with positive live clock and leader progress, but only
  one-frame leader pace, weak strategy-car pace, or class-leader pace.
- `low`: scheduled/pre-green estimate, no leader progress, or strategy/team pace
  fallback.
- `blocked`: active/end-of-race ambiguity, missing clock, missing progress, or
  contradictory SDK fields.

Advice gating:

- `authoritative` and `high` can support normal fuel-to-finish and refuel advice.
- `medium` can support conservative fuel-to-finish with extra reserve.
- `low` can support planning rows but should not recommend deleting a stop.
- `blocked` should suppress actionable strategy advice.

### Race Lap Budget Evidence Inventory

The local repo contains several useful capture families for race lap budget
validation. Treat these as local evidence targets until compact replay-window
fixtures exist; do not commit raw `telemetry.bin` payloads.

Known local evidence to verify with replay:

- `capture-20260522-185231-444`: Race session with `SessionTime = 2700 sec`,
  `SessionLaps = unlimited`, and `SessionLapsRemainEx = 32767`. The synthesis
  shows `SessionTimeRemain` moving from `-1` to normal timed-race values, with
  `CarIdxEstTime` and `CarIdxF2Time` populated across many car indexes. This
  multiclass capture is useful because the local Dallara is not the lead class;
  race-distance projection must be anchored to the overall leader/lead class,
  not the local class. This is a good mid-race/live-clock-availability target,
  but it only covers session time about 366s to 931s.
- `capture-20260522-204847-774`: another 45-minute Dallara race with
  `SessionLapsRemainEx = 32767` and broad timing array coverage. It covers
  session time about 24s to 3119s against a 2700s scheduled race, so it is the
  best current Dallara target for multiclass overall-leader projection,
  clock-expiry, final-lap, and post-checkered replay inspection.
- `capture-20260522-194832-318`: Race session with `SessionTime = 2100 sec` and
  `SessionLaps = 4`. This should be classified carefully before assuming it is a
  pure timed race, because the metadata carries both a time and a lap count. It
  covers session time about 23s to 1733s, so it is useful before expiry but not
  enough by itself to prove end behavior.
- `capture-20260523-034827-919`: another Dallara race with `SessionTime = 2100
  sec` and `SessionLaps = 4`. It covers session time about 37s to 2210s, so it
  is the best current target for deciding whether the 35-minute / 4-lap metadata
  behaves as fixed-lap, timed-with-lap-cap, or a session-template artifact near
  and after scheduled time expiry.
- `capture-20260523-194833-742`: Dallara race with `SessionTime = 2100 sec` and
  `SessionLaps = 4`, but only about 20s to 231s of session time. Keep it as an
  early-race/control sample, not a finish-behavior target.
- `capture-20260521-225342-086`: BMW M4 GT3 EVO 45-minute race at
  Gesamtstrecke Long with `SessionLaps = unlimited`, `SessionLapsRemainEx =
  32767`, and `SessionTimeRemain` moving from `-1` to normal timed-race values.
  It provides a non-Dallara car/class comparison for early timed-race clock
  behavior, but only covers session time about 332s to 556s.
- `captures/capture-20260426-130334-932`: four-hour team race, 1,036,026 frames
  and 2,208 session-info snapshots. This is the primary endurance/team-race
  target for lap-budget and fuel-strategy validation, but it needs replay-window
  extraction before becoming a durable compact fixture.
- `captures/capture-20260502-143722-571`: 24-hour mid-session rejoin/no-history
  capture, 277,680 frames. It should validate long timed-race clock and
  non-local race-overlay context, but local fuel/input scalars are mostly zero,
  so it is not a primary local-fuel strategy target.
- Tiny May 2 24-hour captures such as `capture-20260502-141919-875`,
  `capture-20260502-141936-220`, and `capture-20260502-141939-725` show
  `SessionTime = 86400 sec`, `SessionLaps = unlimited`, and
  `SessionLapsRemainEx = 32767`, with live `SessionTimeRemain` around 30,727s in
  the non-one-frame samples. Keep them as sanity/disconnect edge cases, not as
  normal strategy validation windows.
- `capture-20260523-200213-824`: Toyota GR86 race with `SessionLaps = 3` and
  unusual `SessionTime = unlimited` metadata. The synthesis shows
  `SessionLapsRemainEx` and `SessionLapsTotal` switching from the unlimited
  sentinel to `3` at race-session start, while `SessionTimeRemain` is not a
  usable race-length clock for this lap-limited race. This is a strong
  fixed-lap quality-gate target: finite published lap fields should beat any
  clock-derived estimate, and clock sentinels must not downgrade an otherwise
  authoritative fixed-lap budget.
- `captures/IBT/stockcars ...`: the ignored local IBT corpus includes many
  stock-car oval telemetry files for Ford Mustang and Impala at Charlotte and
  Daytona, including large multi-lap samples. These are useful for NASCAR/oval
  lap, fuel-burn, caution, pit-cycle, and local-car behavior once compact IBT
  sidecars are generated. They should not be treated as a replacement for live
  raw capture when Fuel V2 needs opponent timing, team/focus context, or
  `CarIdx*` leader arrays; IBT is primarily a local-car post-session evidence
  source.
- Practice and qualifying captures are negative controls. They prove that
  `SessionTimeRemain`, `SessionLapsRemainEx = 32767`, and lap/time estimates can
  appear outside races; Fuel V2 race lap budget logic must stay race-gated.

Replay questions:

- At green, when does `SessionTimeRemain` become normal race time rather than
  grid countdown or `-1`?
- At mid-race, does `ceil(leaderProgress + timeRemaining / leaderPace)` match the
  eventual finishing lap?
- Near clock expiry, what does iRacing publish for `SessionTimeRemain`,
  `SessionState`, `SessionFlags`, leader progress, and checkered state?
- After checkered in timed races, does `SessionTimeRemain` become a post-race or
  cooldown clock? The 45-minute Dallara synthesis shows `SessionState` reaching
  `5` while `SessionTimeRemain` remains positive, so final-lap logic must be
  state/flag gated rather than blindly consuming the clock.
- Does `SessionLapsRemainEx` ever become finite late in a timed race, or does it
  stay at the unlimited sentinel? The 45-minute Dallara synthesis observed so
  far keeps it at `32767` throughout the sampled race and checkered window.
- Does `RaceLaps` match completed race laps closely enough to sanity-check the
  strategy lap budget?
- Does the 35-minute / 4-lap metadata represent a fixed-lap race, a timed race
  with a lap cap, or a session-template artifact? The live synthesis for
  `capture-20260523-034827-919` shows `SessionLapsTotal = 4` and
  `SessionLapsRemainEx` counting down from `4`, so Fuel V2 should initially
  classify this family as fixed-lap when those finite live fields are present,
  even though session YAML also carries `SessionTime = 2100 sec`.

Evidence classes to keep separate:

- Short fixed-lap road races, such as the GR86 3-lap capture, prove published
  lap fields and clock-sentinel handling.
- Timed sprint races, such as the 45-minute Dallara captures, prove live clock,
  overall-leader/lead-class pace projection, clock-expiry, and post-checkered
  handling.
- Fixed-lap-with-time-metadata races, such as the 35-minute / 4-lap Dallara
  captures, prove that finite live lap fields must override timed-race
  classification.
- Team endurance races, such as the 4-hour and 24-hour Nürburgring captures,
  prove long timed-race behavior, mid-session rejoin/no-history behavior,
  team/focus strategy-car selection, and local-car fuel evidence limits.
- NASCAR/oval IBT evidence proves stock-car local fuel and oval lap behavior
  after compact sidecars exist, but it does not by itself prove live
  race-budget logic that depends on opponent arrays.

### Action Items

- Add replay-window evidence for Dallara race start, mid-race, final two leader
  laps, clock expiry, and post-checkered frames.
- For Dallara replay windows, record the overall leader, local Dallara class
  leader, and strategy car progress separately so Fuel V2 proves it uses the
  overall leader for the race finish event and the strategy car's own projected
  checkered crossing for laps remaining.
- Add compact replay-window evidence for the GR86 3-lap race showing
  `SessionLapsRemainEx`, `SessionLapsTotal`, `RaceLaps`, and
  `SessionTimeRemain` across race-session start and the first completed lap.
- Generate or recover compact synthesis for the 4-hour and 24-hour endurance
  captures before using them as durable fuel evidence; keep raw `telemetry.bin`
  out of committed fixtures.
- Generate compact sidecars for representative NASCAR/oval IBT files before
  making oval fuel or caution policy decisions; do not commit source `.ibt`
  payloads.
- Compare raw SDK fields, `LiveRaceProgressModel`, `LiveRaceProjectionModel`, and
  Fuel strategy output for the same frames.
- Add a compact fixture that records race lap budget inputs and expected quality
  classification without committing raw `telemetry.bin`.
- Add tests that prevent scheduled race time from reappearing as the remaining
  lap budget after an active timed-race clock expires.
- Decide the reserve/margin adjustment attached to `medium` and `low` timed-race
  estimates before user-facing V2 advice is enabled.

### Open Questions

- Should Fuel V2 always add one full lap of reserve for `medium` timed estimates,
  or should it scale by confidence and race length?
- Should final-lap behavior be anchored to overall leader only, or should class
  winner behavior matter for class-specific strategy displays?
- How should cautions/yellows affect timed-race pace selection: freeze the
  previous green pace, widen reserve, or project from slower live pace with a
  clear caution source label?
- Should race lap budget be a shared Core model consumed by Session / Weather,
  Pit Service, Standings class separators, and Fuel, or remain Fuel-owned until
  the replay evidence stabilizes?

## Live Fuel Usage Telemetry

iRacing exposes live fuel state and instantaneous engine burn, but it does not
currently provide a proven direct `fuel per lap` or `fuel to finish` strategy
field in the local captures. Fuel V2 should continue to derive strategy burn
from fuel-level deltas over valid lap progress, with instantaneous burn kept as
diagnostic or short-term context until replay evidence proves a safe smoothing
policy.

Observed live fuel-related SDK fields:

The three primary live fuel scalar fields are `FuelLevel`, `FuelLevelPct`, and
`FuelUsePerHour`. iRacing also publishes fuel-adjacent pit-service, pit-request,
fuel-pressure, and session-info fields that are important context but do not
directly measure per-lap usage.

- `FuelLevel`: scalar local-car fuel remaining, in liters for the current
  combustion captures. This is the primary live tank-state field.
- `FuelLevelPct`: scalar local-car fuel percentage. Captures expose it as a
  fraction-like percent value; use it as display/support context, not as the
  source of truth when `FuelLevel` is available.
- `FuelUsePerHour`: instantaneous engine fuel use in kg/h. Convert to liters per
  hour only when session info exposes `DriverCarFuelKgPerLtr`. In observed raw
  schema it is an `irFloat` scalar with unit `kg/h` and description
  `Engine fuel used instantaneous`.
- `DriverCarFuelMaxLtr`: session-info tank capacity. Useful for full-tank stint
  estimates and max-fuel sanity checks.
- `DriverCarFuelKgPerLtr`: session-info density conversion for kg/h to liters/h.
- `FuelPress`: engine fuel pressure. Useful for car-state diagnostics, not fuel
  strategy burn.
- `PitSvFuel`: pit-service fuel amount, reported as `l or kWh`.
- `dpFuelFill`, `dpFuelAddKg`, `dpFuelAutoFillEnabled`, and
  `dpFuelAutoFillActive`: driver pit-request fuel controls. Useful for requested
  service state, not measured fuel burn.
- `PitstopActive`, `OnPitRoad`, `PlayerCarInPitStall`, `PitSvFlags`, and
  `PlayerCarPitSvStatus`: pit/refuel context needed to reject burn samples and
  detect service windows.
- `LapCompleted`, `LapDistPct`, `LapDist`, `SessionTime`, and `SessionState`:
  local progress and phase fields needed to convert fuel-level deltas into
  measured per-lap burn.
- `CarIdxLapCompleted` and `CarIdxLapDistPct`: per-car progress arrays used to
  recover team/focus progress and race context. They do not expose per-car fuel.

What is not proven available from live telemetry:

- Direct trustworthy `FuelPerLap`.
- Opponent or class fuel levels.
- iRacing-computed fuel to finish.
- A final refuel recommendation for the current race.
- A safe strategy value derived from one frame of `FuelUsePerHour`.

Recommended Fuel V2 source hierarchy for usage:

1. `measured-green-lap-delta`: local/team fuel-level delta over completed valid
   green-lap progress. This should be the only live source allowed to drive
   confident fuel-per-lap strategy.
2. `measured-partial-lap-delta`: local/team fuel-level delta over enough clean
   partial-lap distance. This may become a future early-race estimator, but it
   needs replay evidence and stricter confidence before driving stop deletion.
3. `historical-exact-context`: matching car/track/session history, clearly
   labeled as historical/model rather than live measured.
4. `historical-near-context`: nearby history when exact context is missing,
   lower confidence and wider reserve.
5. `instantaneous-smoothed`: smoothed `FuelUsePerHour` converted through
   `DriverCarFuelKgPerLtr`. This is diagnostic-only until it agrees with
   measured fuel-level deltas across replay windows.
6. `level-only`: current fuel exists, but usage is unknown.
7. `unavailable`: no valid fuel level.

For live measured usage, Fuel V2 should preserve the current filtering posture:
use only racing/green local active context; reject pit road, pit stall,
pit-service, garage, focus-on-other-car, invalid fuel, refuel/reset, negative
progress, and implausible fuel deltas. Current fuel level can still update in
grid, pit, and pre-green phases, but those frames must not seed burn-rate
evidence.

Current evidence notes:

- The Dallara 45-minute capture schema includes `FuelLevel`, `FuelLevelPct`,
  `FuelUsePerHour`, pit-service fuel fields, and lap progress fields, but no
  direct fuel-per-lap field.
- The GR86 3-lap capture shows live `FuelLevel` up to about 83.33 L and
  `FuelUsePerHour` changing throughout the sampled race window, making it a good
  short fixed-lap target for validating completed-lap fuel deltas.
- Existing 4-hour Nürburgring review found sampled valid-level `FuelUsePerHour`
  could imply materially higher per-lap burn than fuel-delta history for the same
  combo, so instantaneous burn must not drive strategy without smoothing and
  agreement checks.

Replay questions:

- How quickly after green can a completed-lap fuel delta become available for
  common race lengths?
- Can partial-lap deltas become reliable enough for early-race planning without
  underfueling?
- Does `FuelUsePerHour` become stable after smoothing by throttle/green-lap
  windows, or does it remain too sensitive for strategy?
- In team races, when a teammate is driving, does scalar `FuelLevel` represent
  the active team car consistently enough to measure teammate stint burn?
- During pit service, which fuel request and service fields best distinguish
  requested fuel, actual fuel added, and final tank level?

### Fuel Flow Correlation

`FuelUsePerHour` behaves like an instantaneous engine fuel-flow signal, not just
a gear or speed proxy. In the first frame-level correlation pass, throttle was
the strongest individual correlate, but `RPM * throttle` was stronger than
throttle alone. At near-100% throttle, fuel flow still rose with RPM inside the
same gear.

Sampled clean on-track frames:

- Dallara P217, Nurburgring combined long race
  (`capture-20260522-204847-774`): 36,689 usable sampled frames.
  - `corr(FuelUsePerHour, throttle) = 0.990`.
  - `corr(FuelUsePerHour, RPM) = 0.569`.
  - `corr(FuelUsePerHour, speed) = 0.513`.
  - `corr(FuelUsePerHour, gear) = 0.474`.
  - `corr(FuelUsePerHour, RPM * throttle) = 0.996`.
  - throttle-bin mean fuel flow: 0-5% = 0.1 kg/h, 5-25% = 13.7 kg/h,
    25-50% = 38.1 kg/h, 50-75% = 68.5 kg/h, 75-95% = 90.8 kg/h,
    95%+ = 103.8 kg/h.
- Toyota GR86, Nordschleife race
  (`capture-20260523-200213-824`): 32,965 usable sampled frames.
  - `corr(FuelUsePerHour, throttle) = 0.975`.
  - `corr(FuelUsePerHour, RPM) = 0.516`.
  - `corr(FuelUsePerHour, speed) = 0.365`.
  - `corr(FuelUsePerHour, gear) = 0.254`.
  - `corr(FuelUsePerHour, RPM * throttle) = 0.994`.
  - throttle-bin mean fuel flow: 0-5% = 3.0 kg/h, 5-25% = 7.5 kg/h,
    25-50% = 17.7 kg/h, 50-75% = 27.1 kg/h, 75-95% = 34.0 kg/h,
    95%+ = 37.3 kg/h.

Near-100% throttle, per-gear RPM correlation:

- Dallara P217, throttle >= 99%, brake < 5%:
  - gear 1: `corr(fuel, RPM) = 0.986`, about 15.7 kg/h per 1000 RPM.
  - gear 2: `0.964`, about 15.1 kg/h per 1000 RPM.
  - gear 3: `0.921`, about 9.3 kg/h per 1000 RPM.
  - gear 4: `0.916`, about 8.1 kg/h per 1000 RPM.
  - gear 5: `0.912`, about 7.6 kg/h per 1000 RPM.
  - gear 6: `0.974`, about 6.4 kg/h per 1000 RPM.
- Toyota GR86, throttle >= 99%, brake < 5%:
  - gear 1: `corr(fuel, RPM) = 0.995`, about 6.0 kg/h per 1000 RPM.
  - gear 2: `0.979`, about 4.6 kg/h per 1000 RPM.
  - gear 3: `0.979`, about 4.0 kg/h per 1000 RPM.
  - gear 4: `0.958`, about 3.8 kg/h per 1000 RPM.
  - gear 5: `0.957`, about 3.6 kg/h per 1000 RPM.
  - gear 6: `0.972`, about 4.9 kg/h per 1000 RPM.

Long-straight GT3 evidence:

- The 4h Nurburgring VLN GT3 capture (`capture-20260426-130334-932`) gives a
  better repeated-straight test than the shorter Dallara/GR86 samples. A
  10-frame-stride pass reconstructed 16 clean lap starts with a mean lap duration
  of about 480 seconds. The user's approximate anchors, around 3:00 and around
  7:30 into an 8:15-ish lap, line up with usable high-throttle gear-5/6 windows.
- Mid-lap window, 160-220 seconds into the lap:
  - gear 5: 502 samples, 106.7 kg/h, 6660 RPM, 232 kph, mean
    `LapDistPct: 0.393`.
  - gear 6: 315 samples, 107.0 kg/h, 6664 RPM, 260 kph, mean
    `LapDistPct: 0.354`.
- End-straight window, 430-490 seconds into the lap:
  - gear 5: 246 samples, 106.0 kg/h, 6770 RPM, 236 kph, mean
    `LapDistPct: 0.826`.
  - gear 6: 2142 samples, 106.0 kg/h, 6810 RPM, 265 kph, mean
    `LapDistPct: 0.901`.
- Track-position window `LapDistPct 0.86-0.98` showed the same pattern:
  gear 5 and gear 6 both averaged about 106.0 kg/h, while gear 6 averaged about
  265 kph versus 234 kph for gear 5.
- This reinforces the current interpretation: gear 5 versus gear 6 does not show
  a large independent fuel-flow difference at similar WOT RPM in this GT3
  capture, but it can materially change fuel per distance because the car covers
  more distance per hour in 6th at similar kg/h. Sector-flow integration must
  account for both fuel flow and distance/speed.
- The 24h team/rejoin capture (`capture-20260502-143722-571`) was not usable for
  this straight-window comparison in the current pass because scalar fuel,
  throttle, and lap-distance samples were not simultaneously valid.

### Normal Race-Speed Fuel Per Distance

Normal race-speed fuel usage reinforces that `FuelUsePerHour` alone is not the
strategy metric. A higher gear or higher road speed can look similar in kg/h
while materially reducing fuel per distance because the car covers more track in
the same time. Fuel V2 should therefore treat live flow as an integration source:
convert flow plus speed/distance into `L/km`, sector burn, and lap burn, then
calibrate those values against `FuelLevel` deltas before using them for
strategy-safe finish projections.

Sampled clean on-track, throttle >= 99%, brake < 5% frames:

- Dallara P217 (`capture-20260522-204847-774`):
  - gear 1: 1.202 L/km, 98.7 kg/h, 109 kph.
  - gear 6: 0.519 L/km, 106.4 kg/h, 274 kph.
- Toyota GR86 (`capture-20260523-200213-824`):
  - gear 2: 0.473 L/km, 37.3 kg/h, 105 kph.
  - gear 6: 0.234 L/km, 38.5 kg/h, 219 kph.
- 4h GT3 (`capture-20260426-130334-932`):
  - gear 2: 1.145 L/km, 104.6 kg/h, 123 kph.
  - gear 6: 0.537 L/km, 106.0 kg/h, 263 kph.

All-throttle race-speed buckets showed the same broad shape:

- Dallara P217: 0.811-0.902 L/km around 80-120 kph, falling to 0.501 L/km at
  280-300 kph.
- Toyota GR86: about 0.357 L/km around 80-120 kph, falling to about
  0.208-0.218 L/km at 200-240 kph.
- 4h GT3: about 0.915-0.940 L/km around 80-120 kph, falling to 0.525 L/km at
  260-280 kph.

Product interpretation:

- For race pace, the useful driver/engineer signal is distance-integrated burn:
  sector burn, projected lap burn, and fuel-per-distance trends.
- Raw `kg/h` remains useful as a diagnostic for throttle/RPM/load behavior, but
  it should not be interpreted as fuel efficiency without speed or distance.
- Race-speed fuel saving and formation-lap fuel saving should remain separate
  advice families. Formation advice is fixed-distance/fixed-speed gear/RPM
  optimization; race-speed advice is about integrated sector/lap burn under
  traffic, lift/coast, braking, acceleration, and top-speed behavior.

### Formation Lap Fuel Optimization

Formation-lap fuel advice is a different product question from race-pace fuel
strategy. The distance is fixed and the speed is nearly constrained by the field,
so the useful metric is fuel per distance (`L/km` or `L/lap`) at the target pace,
not raw `kg/h`.

Early evidence from pre-green/formation-like frames:

- Dallara P217 race capture (`capture-20260522-204847-774`):
  - At 80-100 kph, gear 1 averaged about `0.569 L/km`, while a smaller gear 2
    sample averaged about `0.161 L/km`.
  - At 120-140 kph, gear 1 and gear 2 were similar: about `0.526 L/km` and
    `0.533 L/km`.
- Toyota GR86 race capture (`capture-20260523-200213-824`):
  - At 60-80 kph, gear 1 averaged about `0.461 L/km`, while gear 2 averaged
    about `0.162 L/km`.
  - At 80-100 kph, gear 1 averaged about `0.408 L/km`, while gear 2 averaged
    about `0.256 L/km`.
  - At 100-120 kph, gear 2 averaged about `0.324 L/km`, while gear 3 averaged
    about `0.192 L/km`.
- 4h GT3 capture (`capture-20260426-130334-932`) had limited pre-green GT3
  samples in the current pass, mostly gear 2 around 80-120 kph. It is useful as
  signal-availability evidence but not yet enough to choose a GT3 formation-lap
  target gear.

Formation-lap interpretation:

- For a fixed-speed formation lap, a higher gear/lower RPM generally looks like
  lower fuel per distance, as long as the car is not lugging or requiring large
  throttle corrections to maintain position.
- The actionable future output should be framed as evidence, not instruction:
  for example, "formation burn was lowest near 90 kph in gear 2 / ~5000 RPM for
  this car-capture sample." Drivers still need control, tire temp, brake temp,
  traffic, and anti-stall margin.
- Fuel V2 should treat formation burn as an operational reserve/input to
  `fuel to finish`, not as normal race-lap fuel usage.

Interpretation:

- The live flow signal is not just `gear + throttle`. RPM matters materially
  even when throttle is effectively pinned.
- Gear has some correlation, but it is likely acting mostly as a proxy for RPM,
  speed, and load. It is weaker than throttle, RPM, and `RPM * throttle` in the
  tested captures. At throttle >= 99%, same-RPM-bin comparisons showed much
  smaller gear differences than the raw per-gear means:
  - Dallara P217: raw WOT gear means ranged from 94.8 to 106.4 kg/h, but in
    7000-7499 RPM and 7500-7999 RPM bins, same-bin gear spread was only about
    0.7 kg/h and 0.2 kg/h respectively. The lower 6500-6999 RPM bin had a larger
    3.9 kg/h spread, which may reflect load/speed/transient effects.
  - Toyota GR86: raw WOT gear means ranged from 25.7 to 38.5 kg/h, but matched
    500 RPM bins with multiple gears only spread about 0.3 to 0.7 kg/h.
- Speed is useful context but is weaker than engine-side signals for
  instantaneous fuel flow.
- For Fuel V2, `FuelUsePerHour` is a good candidate for high-frequency burn
  intensity, sector-flow integration, and driver-behavior diagnostics. It still
  needs calibration against `FuelLevel` deltas before influencing safe strategy
  values.

### Fuel Consumption Edge States

Fuel V2 should track fuel consumption around race edges separately from clean
lap-burn evidence. These states may not produce trustworthy per-lap usage, but
they can still change whether the current stint is safe.

Important edge states:

- pre-race grid and pace phases: the car can burn fuel before green while
  `SessionTimeRemain` is not yet usable as race time remaining.
- pit entry and pit lane travel: fuel continues to matter after committing to a
  stop, and pit-lane distance/time does not map cleanly to normal lap progress.
- pit stall/service: fuel level may increase, pit request fields may change, and
  instantaneous burn may be low, zero, or noisy.
- pit exit and blend line: fuel burn resumes before the car is fully back in a
  normal racing-lap sample window.
- tow/reset/garage/off-track transitions: fuel level or progress can jump or
  become invalid and must not be treated as consumption.

Product decision: edge-state fuel deltas should update current fuel and produce
risk/status evidence, but they should not seed normal fuel-per-lap strategy
unless replay proof shows a specific edge estimator is reliable.

Fuel advice accounting decision: keep clean racing consumption and edge-state
reserve separate. Normal green-lap/sector samples should produce the primary
`fuel per lap` estimate. Pre-green burn, pit-entry-to-box burn, pit-exit/blend
burn, and service deltas should be tracked as operational adjustments or
reserves that affect recommendations such as `fuel to next stop`, `fuel to
finish`, `minimum fuel at pit entry`, and `fuel to pit box risk`.

This prevents pit-lane artifacts from corrupting the driver's race-pace fuel
number while still accounting for the real risk case: a car can have enough fuel
to reach pit entry but not enough fuel to reach a late/shared pit stall.

Examples of useful edge-state outputs:

- `pre-green fuel burned`: fuel lost between grid/pace start and green.
- `pit-entry fuel remaining`: fuel at pit commitment.
- `fuel to pit box risk`: low-fuel warning when fuel remaining is small and the
  pit box is far down pit lane.
- `pit-lane fuel burned`: fuel lost from pit entry to pit stall, and from pit
  exit to racing context.
- `service fuel delta`: actual tank increase observed during service.

Pit-box location signal:

- `DriverPitTrkPct` appears in `DriverInfo` session YAML and is the promising
  static signal for the local driver's assigned pit stall location as a
  lap-distance percentage. Observed examples:
  - Dallara P217, Nurburgring combined long:
    `DriverPitTrkPct: 0.006658`, `TrackNumPitStalls: 38`.
  - GR86, Nordschleife Industriefahrten:
    `DriverPitTrkPct: 0.000118`, `TrackNumPitStalls: 19`.
  - 4h Nurburgring VLN team capture:
    `DriverPitTrkPct: 0.005342`, `TrackNumPitStalls: 38`.
  - 24h Nurburgring combined team/rejoin capture:
    `DriverPitTrkPct: 0.004002`, `TrackNumPitStalls: 38`.
- Live telemetry provides pit-state signals such as `OnPitRoad`,
  `CarIdxOnPitRoad`, and `PlayerCarInPitStall`, but the observed schemas do not
  expose a per-car pit-stall coordinate array or pit-stall index.
- Current capture audit: across 35 `latest-session.yaml` files in the working
  tree, 33 had `DriverPitTrkPct`, but no `DriverInfo.Drivers` entries contained
  pit/box/stall location keys. `QualifyResultsInfo.Results` provides per-car
  qualifying order fields such as `Position`, `ClassPosition`, `CarIdx`,
  `FastestLap`, and `FastestTime`, but not per-car pit-stall location.
- Project assumption to verify: when the field is larger than the available
  pit boxes, iRacing can wrap/share stall assignments. For example, if there are
  14 pit boxes, the 15th pit-order car can receive box 1. That means even a
  proven pit-order rule would be many-to-one:

  ```text
  boxOrdinal = ((pitOrderRank - 1) % TrackNumPitStalls) + 1
  ```

  This makes `DriverPitTrkPct` more valuable than inferred opponent stall
  locations for fuel-to-box risk, because the local assigned location is direct
  evidence while opponent locations may be shared and ambiguous.
- `QualifyResultsInfo.Position` and `ClassPosition` are zero-based in the
  observed YAML. Any user-facing output must convert them to one-based positions.
- The same-track Dallara/P217 Gesamtstrecke Long captures with local qualifying
  data show a monotonic but very small sample:
  - raw qualifying `Position: 11`, class `2` -> `DriverPitTrkPct: 0.008570`.
  - raw qualifying `Position: 15`, class `3` -> `DriverPitTrkPct: 0.007477`.
  - raw qualifying `Position: 16`, class `6` -> `DriverPitTrkPct: 0.007204`.
  - raw qualifying `Position: 18`, class `6` -> `DriverPitTrkPct: 0.006658`.
  This suggests pit-stall track percentage may be ordered by qualifying/grid
  placement for at least some road multiclass sessions, but it is not enough to
  derive all competitors' pit boxes.
- `DriverPitTrkPct` is session-scoped and can change before the race assignment
  settles. In captured session-info snapshots:
  - the 4h team capture changed from `0.006500` for `DriverCarIdx: 20` in one
    early practice snapshot, to `0.008235` for `DriverCarIdx: 15` in later
    practice/warmup snapshots, then to `0.005342` for `DriverCarIdx: 15` in the
    race.
  - the Dallara 45m capture changed from `0.004486` for `DriverCarIdx: 31` in
    warmup to `0.006658` for `DriverCarIdx: 19` for the race.
  - the GR86 capture changed from `0.004123` for `DriverCarIdx: 3` in an early
    snapshot to `0.000118` for `DriverCarIdx: 19` for warmup/race.
  Fuel V2 should treat the current/race-session value as the active evidence and
  should not cache an earlier practice/warmup pit percentage as the race box.
- Public iRacing wording describes `DriverPitTrkPct` as the relative track
  location of the player's pit stall in the session string. The public Sporting
  Code says pit boxes are assigned at event start and may be shared, but does
  not document a deterministic pit-lane ordering algorithm.
- Treat any "box is early/late" feature as local-car evidence first. Do not
  assume we can derive all competitors' stall locations or infer pit-lane order
  from qualifying/grid/class/car number until replay evidence or SDK docs prove
  that rule.

Investigation questions:

- How often does pre-race fuel burn materially change fuel-to-finish or first
  stint length?
- Which session states and flags best distinguish grid, pace lap, green, and
  post-checkered fuel burn windows?
- Can pit-lane distance to the stall be estimated from `DriverPitTrkPct`,
  `OnPitRoad`, `PlayerCarInPitStall`, `LapDistPct`, track-map pit-lane
  geometry, or elapsed pit-lane time?
- Does `DriverPitTrkPct` line up directly with `LapDistPct` and track-map
  geometry across road, oval, multiclass, team, and rejoin captures?
- In team races, does `DriverPitTrkPct` remain stable for the team entry across
  driver swaps and reconnects?
- What threshold should trigger a "fuel to pit box" warning, and should it be
  volume-only or distance/time adjusted?
- During refuel service, which field pair most reliably distinguishes requested
  fuel from actual tank increase?

### Sector Fuel Usage

Per-sector fuel usage looks feasible from live telemetry, but should be treated
as a faster estimator with its own confidence rules rather than as a trivial
replacement for completed-lap fuel burn.

Available sector inputs:

- `SplitTimeInfo.Sectors` in session info provides ordered sector boundaries as
  lap-distance percentages.
- `LapDistPct`, `LapCompleted`, `SessionTime`, and local/team progress identify
  sector boundary crossings.
- `FuelLevel` provides tank level at or around each boundary.
- `FuelUsePerHour` can be integrated across a sector as a high-cadence flow
  estimate, but it must be calibrated against `FuelLevel` deltas before it can
  influence strategy.

Two evidence methods are worth testing:

1. `sector-fuel-level-delta`: interpolate or sample `FuelLevel` at sector
   boundary crossings, subtract the next boundary fuel level, and normalize by
   sector length:

   ```text
   sectorFuelLiters = fuelAtSectorStart - fuelAtSectorEnd
   normalizedFuelPerLap = sectorFuelLiters / sectorLengthLapFraction
   ```

2. `sector-flow-integral`: integrate `FuelUsePerHour` over the sector duration,
   converting kg to liters with `DriverCarFuelKgPerLtr`:

   ```text
   sectorFuelLiters += (fuelUsePerHourKg / fuelKgPerLiter) * deltaSeconds / 3600
   ```

Expected confidence:

- Long sectors, such as Nürburgring sector windows, are the best first target
  because the fuel delta is larger and less dominated by precision/noise.
- Short oval or sprint-track sectors may be too small for raw `FuelLevel` deltas
  and may need aggregation across multiple sectors or full-lap confirmation.
- Sector estimates can help early-race planning before a full lap completes, but
  should not drive stop deletion or underfueling until they agree with completed
  green-lap deltas.
- Reject sector samples under the same conditions as lap-burn samples: non-green
  race state, pit road, pit stall, pit service, garage, focus-on-other-car,
  invalid progress, negative fuel delta, refuel/reset, or implausible burn.

Action item: add replay-window evidence for Dallara and GR86 sector crossings
that compares `sector-fuel-level-delta`, `sector-flow-integral`, and final
completed-lap fuel delta for the same lap.

### Overlay Bridge Fuel Cadence

Sector boundaries are a good candidate emit cadence for future Overlay Bridge
fuel sharing. They are much lower volume than frame-by-frame telemetry, but more
useful than waiting for completed laps on long tracks. For endurance and
multiclass races, this can let teammates see fuel trend updates during a
Nürburgring lap without saturating the bridge.

Important boundary: iRacing live telemetry does not expose opponent or teammate
fuel levels to a spectator client. Bridge teammate fuel updates should come from
each teammate's own running TMR client publishing local/team-car fuel evidence
while that driver has valid fuel context. The bridge should exchange compact
derived facts, not raw telemetry frames.

Recommended bridge fuel message cadence:

- emit on valid sector boundary completion;
- emit on completed lap;
- emit on pit entry, pit stall, fuel increase, and pit exit;
- optionally emit a slow heartbeat while fuel level is valid but no boundary has
  completed recently;
- avoid per-frame fuel updates.

Recommended compact payload:

```text
teamCarId / session car identity
driver id or local publisher id
session time
lap completed
sector number
sector start/end progress
fuel level liters
sector fuel delta liters, if measured
normalized fuel per lap, if measured
instantaneous-flow integral liters, if computed
sample confidence and rejection reasons
pit/service context
source timestamp
```

Bridge consumers should treat sector messages as evidence updates, not commands.
Strategy should still use the local Fuel V2 confidence hierarchy: sector deltas
can update teammate fuel trend and planning rows quickly, but stop deletion,
underfueling, and pit-service refuel advice require completed-lap agreement or a
near-finish high-confidence state.

### Track Map Fuel Sector Mode

A future Track Map option could visualize sector fuel usage instead of sector
time highlights. This would be especially useful for a teammate/engineer view fed
by Overlay Bridge: the engineer could see where the current driver is spending
fuel, not just the total burn number after a lap.

This should be a distinct Track Map display mode, not layered onto the existing
green/purple timing mode. The same sector colors cannot safely mean both
personal-best/session-best timing and fuel efficiency at the same time.

Possible fuel-sector semantics:

- green: sector fuel usage is better than target or rolling baseline;
- yellow: sector fuel usage is near target or expected;
- red: sector fuel usage is materially above target or rolling baseline;
- muted/gray: sector fuel evidence is missing, rejected, or too low confidence.

Possible comparison baselines:

- current stint rolling sector average;
- completed-lap fuel target distributed by expected sector share;
- historical sector baseline for the same car/track when enough evidence exists;
- teammate/bridge target when an engineer view is active.

Required evidence for promotion:

- sector fuel deltas must be normalized by sector length or learned sector share
  so long Nürburgring sectors do not always look bad just because they are long;
- color should be based on confidence-banded thresholds, not one-frame
  `FuelUsePerHour` spikes;
- pit entry, pit exit, reset, off-track, yellow/caution, and traffic-heavy
  sectors need source labels or rejection reasons;
- accessibility should not rely only on red/yellow/green hue if this becomes a
  primary engineering view.

Initial product posture: fuel-sector Track Map is an exploratory visualization
and engineering aid. It should not become proof that a driver can delete a stop
until sector estimates agree with completed-lap fuel deltas and race-lap-budget
confidence is high enough.
