# Fuel Calculator V2 Design Notes

This document captures product and telemetry decisions for the future Fuel
Calculator V2 branch. The current production behavior remains documented in
`docs/fuel-calculator-logic.md`.

Fuel Calculator V2 should rebuild strategy around evidence families instead of
making the current scalar calculator more aggressive. The first family to
harden is race lap budget quality, because every fuel-to-finish, stint rhythm,
stop count, and refuel recommendation depends on how many laps the race will
actually run.

## V1.2.3 Diagnostics And Learned History

The V1.2.3 diagnostics branch ships default-on Fuel V2 evidence collection
without connecting Fuel V2 strategy advice or workbench rendering to production
overlays. `FuelV2CaptureRecorder` writes compact per-session evidence under
`fuel-v2-capture/`, either inside raw captures or under the logs root when raw
capture is off. After finalization, `FuelV2HistoryImporter` promotes selected
derived facts into `history/user/fuel-v2/` while
`FuelV2History:UseForStrategy=false`.

This makes teammate builds useful for model calibration before Fuel V2 is a
user-facing strategy path. The V1.3 Fuel V2 calculator/workbench branch should
compare its parked top-half models against these sidecars and learned summaries:
scope/provenance, fuel-cap facts, accepted and rejected lap-burn windows,
sector-burn samples, pit/service windows, team stint shape, lap-budget
source/missing-signal counts, and source confidence/rejection labels. These
records are training and calibration evidence only until a later promotion
decision explicitly allows Fuel V2 strategy to consume them.

## Race Lap Budget Quality Gate

Goal: classify the race lap budget before Fuel V2 promotes strategy advice.
Lap-count races can usually use iRacing's published lap fields directly. Timed
races need stricter source and confidence handling because the app must infer
the finish lap from the session clock, leader progress, and pace.

### Product Decision

Race lap budget / laps logic V2 should be a shared Core model from the start,
with source, confidence, and missing-signal details. Fuel V2 consumes this
contract, but does not own it. The same contract should eventually feed
Session / Weather, Pit Service, Standings class separators, Track Map, and
shared header/footer options that display laps or race-budget context. It should
not only expose a decimal `RaceLapsRemaining` value.

User-facing strategy advice such as stop deletion, underfueling, stretch-to-one-
more-lap prompts, or pit-service refuel recommendations should require a trusted
race lap budget. When the lap budget is weak, Fuel should show constraints and
caveats instead of confident advice.

Conservative bias:

- Overestimating by one lap is acceptable for safety.
- Underestimating by one lap can ruin the race and must block aggressive advice.
- Timed-race uncertainty should widen the displayed range, show source context,
  or degrade advice rather than hide the source quality.

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

### Known Versus Projected Display Policy

Fuel V2 should show what is known as the primary strategy state, and show what
may happen as labeled projection/scenario evidence. The app should not collapse
those into one confident number unless the projection has become proven enough
for the advice being shown.

Primary/known state:

- comes from finite published lap fields, confirmed race clock/progress, current
  lap-down state, and clean pace evidence;
- drives fuel-to-finish, pit-service quantity, stop count, and warnings;
- uses the conservative higher fuel requirement when a timed-race projection
  range crosses an integer lap boundary.

Projected/scenario state:

- can show likely outcomes such as "leader pace suggests 6-7 laps", "strategy
  car may be lapped before the finish", or "pit-cycle-adjusted pace points to a
  shorter finish";
- must name the assumption and confidence reason, such as leader on pit road,
  last-lap-only pace, missing clean rolling pace, or unconfirmed future lap-down
  state;
- can inform planning and engineering discussion, but should not lower fuel
  required for safety-critical advice until the relevant event is confirmed or
  the confidence threshold is deliberately met.

This makes the UI honest about uncertainty: show the driver or engineer what the
model knows now, expose the plausible future, and make the conservative value the
one that controls actionable fuel advice.

Working hypothesis, not yet a final product decision: keep the primary race
projection as simple and conservative as possible, and put the sophistication in
quality/risk labeling around the edges. For long races, especially endurance
events with many stints, the useful strategy answer may be a stable stint rhythm
such as "plan on 7-lap stints" rather than a fragile exact prediction of every
future leader, pit, caution, repair, and lap-down transition. The model should
track when that simple answer is near an important boundary, then surface the
edge case instead of pretending the precision is better than the evidence.

Historical fuel evidence can improve the stint rhythm without making the
race-distance estimator more adventurous. Normal green race or practice history
can seed the main rhythm; qualifying/push history can inform the high-burn upper
limit. A useful planning row may be: "based on the conservative lap budget and
current/historical burn, repeated N-lap stints leaves an estimated M-lap final
stint." If `M` is small, such as a one- or two-lap final splash window, Fuel V2
should mark the plan as edge-sensitive because a single extra race lap, damage
repair, caution, or leader change can change whether the final stop is needed or
how much fuel it needs. This lets the calculator be useful for 20-stint races
without pretending it can predict every future disruption.

Pit-cycle-informed race length can be useful as an internal scenario for races
where stops are certain. Before the field has stopped, leader pace may
over-project by roughly one lap because it has not paid the pit-lane/service
cost yet. After the leader and relevant strategy classes complete one stop, Fuel
V2 can estimate likely remaining pit cycles and use that to inform sensitivity
alerts, such as "predicted pit-cycle-adjusted finish is 25 laps while the
displayed conservative counter is 24." This should remain under-the-hood
scenario evidence unless confidence is deliberately promoted; it should not
replace the primary counter or lower required fuel by itself.

Candidate UI row: `Possible strategy change`. This row can expose emerging
scenario evidence without presenting it as a hard recommendation. Examples:

- "Possible: pit-cycle pace may make this 25 laps, not 24."
- "Possible: projected final stint is only 1 lap."
- "Possible: if leader catches this class, fuel target drops by about 1 lap."
- "Possible: no-stop is only viable at 12.5 L/lap, about 1.0 L/lap below your
  13.5 L/lap baseline."

The row should carry source/confidence text and should stay visually softer than
the primary fuel-to-finish or pit-service recommendation. It can become more
confident as more evidence arrives, such as after the first pit cycle, after a
leader change settles, after the strategy car's lap-down state is confirmed, or
after normal historical burn or qualifying upper-limit evidence agrees with live
stint burn. It must not be phrased as "do this" until the underlying strategy
gate has enough confidence for actionable advice.

Historical/pre-live strategy must be visible in the Fuel overlay when it is
actionable enough to matter. It should not be only a Fuel tab diagnostic. The
likely placement is the overlay's top strategy section, separate from the `Stint
N` row group, so the driver can see the plan shape before a completed live race
lap exists without confusing it with the live stint breakdown.

This should be a stable `Strategy summary` row rather than a recommendation row
that appears and disappears as the driver flirts with making a strategy viable.
If no-stop or stretch math is close, the summary can keep showing the comparison
while the recommendation state changes elsewhere. Example row shapes:

```text
Strategy summary: no-stop needs 12.5 L/lap; history 13.5
Strategy summary: 7-lap rhythm, final 2 laps
```

The row should name the source compactly when space allows, such as `history`,
`quali upper`, or `live pending`, and should remain scenario/framing copy until
live green-lap burn confirms or rejects it.

If the Fuel tab or overlay section is labeled `Strategy`, it is reasonable for
users to read it as strategy suggestion space. For initial V2, park explicit
advice-level settings and default to conservative behavior: core fuel,
configured margin, stable strategy summary, and high-confidence risk/status
context. Use teammate testing to decide whether controls such as conservative
facts only, soft scenarios, or more assertive strategy suggestions are actually
needed.

### User Fuel Margin Policy

Fuel V2 should expose a user-configured fuel margin in the Fuel tab. The user's
margin is the driver's chosen policy for how much extra fuel to carry beyond the
model's estimated requirement, expressed primarily in laps. The app should not
hide that policy inside a hard-coded reserve constant.

The core overlay policy should stay intentionally simple. The primary fuel
answer is the trusted lap budget multiplied by the selected clean-race burn
baseline, plus the user's configured margin and any known current-fuel
adjustments. Evidence around the edges should explain the situation or block weak
advice; it should not turn the core overlay into a constantly self-tuning
strategy engine.

Initial product decision:

- The Fuel tab owns the editable margin control.
- The primary control should be lap-based, such as `+0.0`, `+0.5`, `+1.0`, or
  `+2.0 laps`, because drivers usually reason about finish safety in laps.
- The model converts that lap margin to fuel using the active burn baseline and
  shows the equivalent liters/gallons where useful.
- Overlay advice uses the configured margin when computing `fuel to finish`,
  `fuel add amount`, last-safe-lap, no-stop, and pit-now scenarios.
- The tab should show both values: model requirement without user margin and
  target requirement with user margin.
- The user's margin is not a substitute for confidence handling. If evidence is
  weak, contradictory, caution-affected, or near an integer lap boundary, Fuel V2
  should show source/range context, mark advice degraded, or hide high-impact
  advice rather than quietly adding a hidden safety tax.
- Yellow, formation, pit, repair, and other edge-state fuel samples should
  update current fuel and current-race context, but should not enter the normal
  historical race-burn baseline unless they are deliberately classified into a
  separate edge-state bucket.
- Example: if a yellow-flag lap burns half the historical green-lap average, the
  app should classify it as a caution outlier. It can inform the current race
  situation, but it must not make future historical projections think the normal
  race burn is now half as high.

Open implementation details:

- Decide default margin by profile or session type. A conservative default such
  as `+1.0 lap` is a reasonable starting point, but the default should be a
  visible setting, not hidden behavior.
- Decide allowed range and step size. The UI should support fractional laps for
  short races and whole-lap presets for quick race use.
- Decide whether advanced users can enter an absolute fuel margin in liters or
  gallons in addition to lap margin.
- Decide persistence scope: global default, car/series-specific override, and
  one-session temporary override.

### Effective Session Fuel Capacity Policy

Fuel V2 must use the session-effective fuel limit for live strategy. Physical
tank size is not the live calculation target when the session rules cap usable
fuel below the car's tank.

Hard product decision:

- Capacity-dependent live advice uses effective session capacity only.
- Effective session capacity comes from the physical tank plus the active rule
  cap fields, such as `DriverCarMaxFuelPct` and `CarClassMaxFuelPct`.
- When caps are percentages of the physical tank, use the applicable most
  restrictive positive cap for the local car/session. If no cap is present or
  the cap is effectively unrestricted, effective session capacity can equal the
  physical tank.
- If the car has a 75 L physical tank but the session cap is 50 L, full-tank
  stint, add-to-full, no-stop, last-safe-lap, and pit-service quantity
  calculations must use 50 L.
- Physical tank size remains useful for diagnostics, sanity checks, history
  scope, and detecting missing or contradictory rule data, but it must not drive
  confident live strategy when a lower session cap is known.
- If effective session capacity is missing or contradictory, Fuel V2 should
  degrade or hide capacity-dependent advice rather than silently fall back to
  the physical tank.
- Historical service analysis should not require knowing physical max versus
  session max. It should classify real stop timing from observed service
  windows, requested fuel, and actual fuel deltas. Fuel caps are retained as
  context that may help explain or narrow service timing, not as a hard
  prerequisite for historical analysis.
- Historical strategy rows that specifically ask "full tank" or "add to cap"
  still need the effective session capacity for that session, because those
  rows are capacity-dependent by definition.

Stretch/no-stop scenarios should compare required burn against grounded burn
history instead of only comparing current tank to a coarse lap count. For the
35-minute / 4-lap Dallara example, a no-stop scenario may be technically
possible only if post-grid race-lap burn is about `12.5 L/lap`; if live or
historical Dallara/Nürburgring evidence says the realistic average is closer to
`13.5 L/lap`, the useful output is the delta: the driver needs roughly `1.0
L/lap` less than baseline, or about `7.4%` fuel saving, and the app should
describe whether current sectors/laps are trending toward that target. That is a
strategy possibility, not a recommendation, until enough live stint evidence
proves the target is actually being achieved.

Initial 35-minute Dallara stretch/no-stop probe:

- Tested three 35-minute / 4-lap Dallara captures:
  `capture-20260522-194832-318`, `capture-20260523-034827-919`, and early sample
  `capture-20260523-194833-742`.
- Ignore the first race-session `FuelLevel = 0` frames before green. The useful
  point is first green or first valid team progress, where fuel was about
  `49.4-50.1 L` and the lap budget was authoritative at 4 laps.
- At first valid progress, no-reserve no-stop required about `12.35-12.55 L/lap`
  depending on the capture. With a known `13.5 L/lap` baseline, that is roughly
  `0.95-1.15 L/lap` below baseline, or about `7-8.5%` saving. A 1 L finish
  reserve pushes the required saving toward roughly `9-10.5%`; a 2 L reserve
  pushes it toward roughly `11-12.2%`.
- With the known `13.5 L/lap` baseline, a soft `Possible strategy change` row
  could appear as soon as fuel is non-zero and the finite 4-lap budget is
  trusted, then become cleaner once team progress becomes valid about 18-21
  seconds after green. The row should say the required target and saving delta,
  not claim the no-stop is recommended.
- Without historical normal race/practice burn or qualifying upper-limit
  evidence, live-only evidence is much later and weaker.
  In `capture-20260522-194832-318`, live burn through the first half lap and
  first lap was about `13.35-13.45 L/lap`, while the remaining no-stop target was
  already near `12.2 L/lap`, so the correct row would be "not tracking yet." In
  the full `capture-20260523-034827-919`, live burn stayed around
  `12.6-12.9 L/lap` through the first two laps against a target near
  `12.1-12.4 L/lap`; it only converged with the required target around lap 3,
  which is too late for useful early no-stop advice.
- Product implication: historical normal race/practice burn and qualifying
  upper-limit burn are what make this row useful early. Practice burn is real
  event preparation evidence and should be allowed by default when context
  matches. Qualifying burn should be treated as a real high-burn comparison
  point, not as the normal race baseline. Live data should confirm or reject the
  stretch target, but should not be the only source for showing the initial
  possible-strategy delta.
- If the baseline is known before the race, the possible row can appear before
  green as a pre-race scenario, as long as it is explicit about the assumptions:
  expected fuel at green, trusted 4-lap budget, matched Dallara/Nürburgring burn
  baseline, and required saving. The row should update after green once actual
  fuel level replaces assumed fuel-at-green.
- Sector-level burn can make the live-only case useful earlier, but only as
  cumulative confirmation/rejection rather than a single-sector proof. On the
  35-minute Dallara captures, cumulative burn through the first `0.26-0.51` lap
  was enough to show a trend several minutes before the first completed lap:
  one capture was already clearly above the no-stop target (`~13.5 L/lap`
  cumulative versus `~12.2 L/lap` required), while the full 4-lap capture was
  closer but still slightly above target (`~12.8 L/lap` cumulative versus
  `~12.4 L/lap` required). This is useful warning evidence, not proof that the
  strategy is safe.

Current V1 already has a historical baseline system, but it is intentionally
coarse. It stores exact car/track/session aggregates with mean/min/max/sample
count for fuel per lap and fuel per hour, plus lap-time, stint, pit-lane,
service, tire-service, no-tire-service, and fill-rate aggregates. User history is
queried by default; tracked packaged baseline history is ignored unless
`SessionHistory:UseBaselineHistory` is enabled. Fuel strategy selects usage in
this order: completed live green-lap burn, exact-context history mean, then
unavailable. The current overlay can show min/avg/max in source text, but the
strategy model mostly uses the selected mean and does not yet classify the
range.

The live measured path is fairly conservative today. It requires a completed
green lap (`SessionState == 4`) from local/team fuel-level deltas, valid
progress, local focus, on-track surface, no pit road, no pit stall, no active
service, no garage, no teammate pit-road state, `0.95-1.25` laps of progress,
`20-1800` seconds elapsed, at least `0.05 L` burned, and no more than
`40 L/lap`. It rolls the last three accepted lap samples. It does not use
partial sectors or instantaneous `FuelUsePerHour` for strategy burn.

Historical baseline taxonomy starting policy:

- Normal green race laps are the strongest burn baseline for core overlay
  advice.
- Clean practice laps are real event usage and are allowed by default for race
  planning when car/track/rules context matches. Users commonly practice a lot
  for a specific event, and those laps should help inform race strategy before a
  completed race lap exists. Label them as practice/history and let live race
  burn confirm or replace them once available. TMR collects a lot of event data;
  Fuel V2 should use that data to inform projections instead of waiting until the
  race has already produced enough completed live laps.
- Qualifying or push laps are real fuel usage and can inform upper-limit
  analysis. They should be labeled separately from normal race burn, but they
  are useful for questions like "how much fuel does this car use when pushed?"
  and for conservative high-burn bounds.
- Fuel-save laps are only useful when deliberately tagged or clearly inferred.
  They should not lower the normal baseline by accident.
- Yellow, formation, wet, heavy traffic, and draft-affected laps should not be
  blended into normal historical projections. Preserve labels or rejection
  reasons where possible. Wet live usage can still inform the actual displayed
  fuel strategy when it is clearly lower in the current race, but Fuel V2 does
  not need a deep wet/traffic/draft taxonomy at first. Those effects are real,
  but trying to count them precisely early would be annoying, fragile, and likely
  to create false precision.
- Sector evidence is separate. It can explain live trend before a completed lap
  exists and may later produce sector baselines, but it should not be mixed into
  full-lap history until it agrees with completed-lap fuel deltas.

The historical summary path is less strict than the live strategy path and needs
Fuel V2 review before powering a high-stakes stretch row. The global historical
fuel-per-lap accumulator accepts on-track, non-pit, non-garage, moving, valid
fuel samples, but it does not currently require `SessionState == 4`, local focus,
or the same track-surface gate as live measured burn. A summary contributes to
the aggregate when confidence is `medium` or `high`: `medium` is any reliable
fuel-per-lap value with at least one valid lap of distance or one completed valid
lap; `high` requires at least three valid laps and three completed valid laps.
Quality reasons such as `short_green_sample`, `no_completed_laps`,
`dropped_frames`, or `non_race_test_session` are recorded, but only `low` or
`none` confidence blocks aggregate contribution. Notably, `non_race_test_session`
is a reason, not an absolute rejection gate today.

Initial data rejection policy:

- Samples that can influence overlay strategy should share the same hard
  rejection gates across live, race history, practice history, and qualifying
  upper-limit evidence.
- Hard reject pit road, pit stall/service, garage, tow/reset/refuel jumps,
  invalid or negative progress, implausible fuel deltas, missing local/team
  focus, and obvious session-state mismatches.
- A simple off-track event does not have to invalidate the sample by itself.
  Treat it as a weakness/outlier signal attached to the sample. If later clean
  matching laps prove the off-track sample is too far outside the normal range,
  Fuel V2 can demote it, exclude it from the primary baseline, or keep it only as
  a labeled outlier.
- The model should preserve enough metadata to explain why a sample was hard
  rejected, accepted cleanly, or accepted with weakness.

Fuel V2 should keep the existing aggregate data, then add richer classified
baseline facts so a stretch row can say whether the required target is normal,
optimistic, or outside known evidence. Candidate additions:

- normal race-lap average and sample count;
- normal practice-lap average and sample count, allowed by default when context
  matches;
- minimum observed valid lap burn and the conditions that made it valid;
- maximum valid green-lap burn, excluding pit/yellow/reset artifacts;
- qualifying or push-lap burn, collected separately as upper-limit evidence;
- fuel-save burn, such as a "5L save average" or other tagged fuel-saving run;
- sector-burn baselines for long tracks, with enough aggregation to avoid
  overfitting one sector;
- confidence/rejection metadata: car, track, session type, fuel limit, weather,
  setup-ish context where available, traffic/yellow/pit exclusions, and recency.

Two current gaps matter for the Dallara examples:

- Exact session keys mean race, qualifying, practice, and test data stay
  separated. That is safe, but Fuel V2 should still allow clean matching
  practice laps to seed race planning by default. Qualifying/push and fuel-save
  baselines remain separate labels because they answer different questions than
  normal event burn.
- Session info exposes both physical tank capacity (`DriverCarFuelMaxLtr`) and
  effective fuel-limit fields such as `DriverCarMaxFuelPct` and per-class
  `CarClassMaxFuelPct` in captures. Current parsed history stores the physical
  tank capacity but not the effective event fuel cap, so limited-fuel races can
  overstate full-tank stint potential. Fuel V2 should parse the effective cap
  before enabling full-tank or add-to-full live strategy for capped sessions.
  Historical stop timing should still be learned from the observed stop and fuel
  delta even when cap metadata is incomplete.

The product value is not only the average. For example, if no-stop requires
`12.5 L/lap`, an average of `13.5`, a historical minimum of `12.7`, and no
validated fuel-save sample below `12.5` should produce a very different warning
than an average of `13.5` with repeated saved stints around `12.3-12.5`.

Adversarial projection scenarios to fixture before Fuel V2 makes aggressive
recommendations:

- Lead-car retirement or major slowdown after creating an extra-lap boundary:
  for example, the leader is 30 seconds ahead and initially appears to make a
  7-lap timed race possible, then retires halfway through the projected final
  lap. Fuel V2 must detect the overall leader identity/progress shift, preserve
  the previous 7-lap scenario as stale history, and recompute from the new
  official leader. It should not instantly lower safety-critical fuel unless the
  new leader state and checkered timing are confirmed enough to make the shorter
  finish real.
- Meatball/damage repair for either the lead car or strategy/focus car: a
  six-minute repair can change both the race finish trigger and the strategy
  car's own lap-down state. If the lead car receives the meatball, the likely
  overall leader can change. If the strategy/focus car receives it, the race
  finish may stay the same while the car's required distance drops by one or more
  laps. The model needs separate leader-progress and strategy-car-progress
  confidence so a repair-induced lap-down update is recognized as confirmed only
  after telemetry shows the new gap, not merely because the repair is predicted.

Race pace should prefer:

1. Rolling overall leader pace from completed clean green laps.
2. Overall leader last lap when rolling pace is not ready.
3. Class leader pace only when overall leader pace is unavailable and the class
   context is explicitly documented.
4. Team/player strategy lap time only as a low-confidence fallback.
5. Historical or session estimated lap time only as pre-green planning seed.

### Quality Rules

The race lap budget model should carry at least:

- `primaryLapsRemaining`: the conservative/actionable whole-lap value that Fuel
  can use for `fuel to finish`, refuel amount, and margin math.
- `possibleLapsRemaining`: decimal alternate/range context for boundary cases,
  displayed when useful but not used to lower safety-critical fuel by itself.
- `source`: one of the V2 lap-budget source enum values listed below.
- `confidence`: one of the V2 confidence enum values listed below.
- `stateFlags`: additive V2 reason flags listed below.
- `canDriveFuelAdvice`: Fuel-specific gate that says whether
  `primaryLapsRemaining` may drive fuel-to-finish, refuel, margin, stop deletion,
  or stop-risk advice.
- `displayLabel`: shared short text for general overlay chrome, such as
  `6 laps` or `laps unknown`, separate from Fuel-specific strategy copy.
- estimated finish lap
- leader progress, including fractional lap position where available
- strategy car progress, including fractional lap position where available
- race pace and pace source
- strategy-car pace and pace source, when needed for lapped-car projection
- session time remaining
- session state/phase
- missing or contradictory signals
- pace/phase and outlier classification

Fuel-specific policy such as user fuel margin should be applied by Fuel after it
consumes the shared lap-budget contract, not stored as part of the shared laps
model.

Display-label decision: the shared lap-budget model owns the simple actionable
lap-count language. It should say `N laps` or the appropriate unavailable form,
and consumers should consume that label rather than formatting their own
competing lap-count text. Fuel, Pit Service, Session / Weather, Track Map,
Standings, and shared header/footer renderers can still add their own surrounding
strategy copy, but the base lap-budget label comes from the shared model. When a
surface needs boundary context, it should consume the model's decimal
`possibleLapsRemaining` value rather than recomputing its own estimate.

Consumer policy: most consumers should stay dumb until proven otherwise. Shared
header/footer slots, standings separators, track-map labels, and similar display
surfaces should consume the model-provided header/content value, such as
`6 laps`, without reinterpreting confidence, source, or state flags. Fuel is the
primary consumer that turns the lap-budget contract into strategy math, margin
math, refuel advice, and stop-risk decisions.

Fuel actionability decision: the shared model should publish one Fuel-specific
gate, `canDriveFuelAdvice`, instead of requiring Fuel to duplicate every
source/confidence/flag rule. This does not make every consumer smart. It simply
lets the shared lap-budget model say whether its current `primaryLapsRemaining`
is safe enough for Fuel math; Fuel still applies user margin, fuel-cap, burn
evidence, unit display, and strategy copy after consuming the gate.

The exact relationship between `medium` confidence and `canDriveFuelAdvice`
should be decided by replay/fixture testing, not locked by planning text. Start
with the conservative assumption that lower-confidence states must not reduce
fuel or delete stops, then let the V2 laps tests show whether any medium states
are stable enough for specific Fuel actions.

Lap-budget persistence decision: do not persist every live
`primaryLapsRemaining` or `possibleLapsRemaining` projection. Do persist compact
final race-distance facts when they are proven, because they can seed future
planning months later for the same track/session/car-class shape. A future race
can benefit from knowing that a matching 35-minute Dallara session previously
finished at 6 laps, but that fact should be context or a pre-green seed until
live race evidence takes over.

Initial confidence enum:

- `authoritative`: published remaining laps or fixed lap total.
- `high`: timed race with positive live clock, leader progress, and rolling clean
  overall leader pace.
- `medium`: timed race with positive live clock and leader progress, but only
  one-frame leader pace, weak strategy-car pace, or class-leader pace.
- `low`: scheduled/pre-green estimate, no leader progress, or strategy/team pace
  fallback.
- `blocked`: active/end-of-race ambiguity, missing clock, missing progress, or
  contradictory SDK fields.

`blocked` means the lap-budget model must not publish a Fuel-actionable
`primaryLapsRemaining`. `low` can still be useful planning context in settings,
pre-race, diagnostics, or non-actionable header/footer display, but it should not
drive overlay refuel advice or stop deletion.

Initial source enum:

- `published-laps-remaining`: finite SDK/session field directly reports laps
  remaining.
- `fixed-lap-total`: fixed-lap race total plus leader/team progress determines
  remaining distance.
- `timed-live-clock`: active timed race projection from live remaining clock,
  leader progress, and accepted pace evidence.
- `timed-pre-green-estimate`: scheduled race time and seed pace before enough
  live race progress exists.
- `timed-expired-final-lap`: timed race clock has expired and final-lap /
  own-checkered state determines the remaining budget.
- `missing-active-clock`: active race state exists but the clock needed for timed
  projection is missing, contradictory, or unusable.
- `unavailable`: no trustworthy lap budget can be published.

Initial state flags:

- `pre-green`: session has not produced live race-progress evidence yet.
- `boundary-risk`: plausible finish range straddles a lap boundary.
- `leader-progress-missing`: leader progress needed for the chosen source is
  missing or unusable.
- `strategy-progress-missing`: local/team strategy-car progress needed for the
  chosen source is missing or unusable.
- `pace-seed-only`: pace comes from scheduled, historical, or estimated seed
  data rather than clean live race laps.
- `pace-contaminated`: recent pace evidence may include yellow, pit, damage,
  traffic, draft, wet-condition, leader-change, or other disruption.
- `own-checkered-pending`: finish state depends on whether the strategy car has
  personally taken checkered.
- `session-finished`: session has finished and the model is reporting final
  state rather than live strategy state.
- `contradictory-fields`: SDK/session fields disagree in a way that affects lap
  budget.
- `published-field-transient`: an authoritative-looking published lap field has
  recently changed in a suspicious short-lived way.
- `clock-expired`: timed-race clock has reached zero or sentinel final-lap
  behavior.
- `clock-missing`: active timed race is missing a usable remaining-clock value.

State flags explain why a source/confidence decision was made. They should not
become a second source enum or confidence ladder.

Initial pace-contamination policy:

- Hard invalids should reject pace evidence up front: missing lap time, zero or
  negative lap time, known pit lap, a lap/progress sample that cannot be matched
  to a real completed race lap, or a physically impossible outlier for the
  car/track/session scope.
- Yellow, wet, traffic, draft, damage, leader-change, and similar conditions
  should usually start as contamination flags that degrade confidence or add
  boundary risk rather than automatically discarding the pace sample. Replay
  tests can later prove which of those conditions should become hard
  disqualifiers for specific Fuel actions.
- Plausible slow pace must not be rejected just because it is materially slower
  than the normal baseline. In a 35-minute race, a driver may intentionally give
  up 6+ seconds per lap to hit a `12.5 L/lap` fuel target and save roughly 40
  seconds of pit time. That pace should be degraded or labeled as fuel-save /
  strategy context until corroborated, but it is still real race evidence and may
  be the point of the strategy.

Primary strictness from replay evidence:

- Treat this as a "never low" contract for Fuel. At replay checkpoints after
  `N` completed leader/team laps, `primaryLapsRemaining` must not be below the
  eventual strategy-car laps still required to finish. Being one lap high is
  acceptable and should be labeled as conservative/boundary-risk when relevant.
- Actionable fuel math rounds up to whole laps. Fractional remaining distance is
  useful context for progress, stint shape, and possible-range display, but a
  value such as `5.65` laps must become `6` laps for `primaryLapsRemaining` when
  calculating final fuel requirement.
- Do not overload `primaryLapsRemaining` with decimal estimates. It is the
  rounded-up, safety-critical lap count. Fractional values such as `5.65` belong
  in leader/strategy-car progress, estimated finish-lap detail,
  `possibleLapsRemaining`, or diagnostics.
- Keep `possibleLapsRemaining` decimal. Seeing a boundary value such as `6.01`
  laps remaining, then watching it tick to `5.99`, can explain why the leader may
  need one more stop and why Fuel can remove one actionable lap only after the
  boundary is truly crossed.
- Store `possibleLapsRemaining` as a numeric decimal with enough precision for
  testing and model comparison. Initial Fuel/detail display can round it to two
  decimal places, such as `6.01` or `5.99`, while the shared `displayLabel`
  remains the simple actionable whole-lap text. This display precision is a
  starting convention and can change after real UI testing.
- Existing telemetry supports asymmetric strictness. In the 45-minute Dallara
  timed race, leader-lap checkpoints projected `7` laps while the race finished
  at `6` until later slower pace evidence settled. In the 4-hour team race,
  start and early/mid-race projections were `31` while the actual result was
  `30`, then later checkpoints matched `30`. These are acceptable conservative
  misses, not reasons to lower the primary count early.
- For timed races, the current rolling projection threshold of `3` clean leader
  laps is enough to publish a projection, but not enough by itself to lower the
  actionable primary count when the plausible finish range straddles a lap
  boundary. In that case, keep the higher whole-lap count in
  `primaryLapsRemaining` and put the decimal boundary estimate in
  `possibleLapsRemaining`.
- Lower `primaryLapsRemaining` to the smaller timed-race count only when clean
  rolling pace, current leader progress, and recent projection history agree on a
  single finish lap without pit/yellow/leader-change contamination, or when
  finite published lap fields or own-checkered/final-lap state prove the shorter
  distance.
- Finite published `SessionLapsRemainEx` / `SessionLapsTotal` remains
  authoritative, but short-lived decreases should be debounced or degraded before
  reducing the actionable primary count. A transient `4 -> 3 -> 4` blip should
  not make fuel strategy flicker or briefly underfuel; increases can apply
  immediately because they are conservative.

Advice gating:

- `authoritative` and `high` can support normal fuel-to-finish and refuel advice.
- `medium` can support fuel-to-finish with the user margin plus visible
  source/range context, but should avoid stop-deletion or edge-sensitive advice.
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

### Initial Replay Findings

The first v1.3 race-lap-budget probe used scratch raw-capture decoders against
local `telemetry.bin` files and should be promoted into compact replay-window
fixtures. The probe compared SDK fields, leader progress, and simple projected
finish laps against eventual race length where a capture contained enough finish
evidence.

Timed-race findings:

- `capture-20260522-204847-774` is the best current 45-minute Dallara timed
  race proof. `SessionLapsRemainEx` and `SessionLapsTotal` stayed at `32767`,
  so the race length had to come from live clock, overall leader progress, and
  pace. The overall winner, a faster lead-class car, completed 6 laps. The local
  Dallara strategy car also completed 6 laps, about 191 seconds behind the
  overall leader.
- In that capture, the timed projection was safely high early and mid-race. At
  leader lap 1 through about leader lap 4, using the current leader pace and
  live clock projected lap 7 while the actual race ended on lap 6. The estimate
  became correct after the later slower leader lap was included and stayed
  correct through clock expiry.
- Pit cycles and slow-lap contamination are a likely reason for the projection
  flip. Around leader lap 4 the overall leader was observed on pit road, and the
  leader's last-lap signal stretched from roughly 444 seconds early to roughly
  479 seconds near the finish. Because the projection sat close to an integer
  boundary, that pace change was enough to move `ceil(leaderProgress +
  timeRemain / pace)` from 7 to 6.
- `SessionTimeRemain` reached `0` while `SessionState` was still `4` and the
  leader had not crossed the final line yet. This is a `timed-expired-final-lap`
  state, not race complete. Shortly after state `5`, `SessionTimeRemain` became
  positive again as a post-race/cooldown-style clock, so post-checkered positive
  clocks must be ignored for active strategy.
- `capture-20260522-185231-444` is useful for early/mid-race timed-clock
  behavior, but it ends before clock expiry or checkered and the player car is a
  spectator row. It should not be used as final race-length proof.
- `capture-20260426-130334-932` proves the same long-timed-race shape at 4-hour
  scale. The final race length is inferable as 30 laps. Early estimates can be
  one lap high, which is a safe conservative bias. Dirty/missing leader fields and
  pit-contaminated slow rolling pace can under-project, which must degrade or
  block safety-critical advice.
- `capture-20260502-143722-571` is a 24-hour mid-session rejoin sanity target,
  not a finish-proof target. It has long-clock and team-car progress evidence,
  but no eventual finish inside the capture and local fuel scalars are mostly
  zero.

Fixed-lap findings:

- `capture-20260523-034827-919` proves that the 35-minute / 4-lap Dallara
  family behaves as a fixed-lap race when live lap fields are finite. The race
  metadata includes `SessionTime = 2100 sec`, but `SessionLapsRemainEx` and
  `SessionLapsTotal` are finite `4` from race start and count down through the
  race. Treating the same frames as timed would over-project to 5 laps around
  mid-race.
- `capture-20260522-194832-318` is a partial active-race fixed-lap sample with
  the same 4-lap behavior, but it contains an early non-monotonic
  `SessionLapsRemainEx` blip from `4` to `3` back to `4`. Fuel V2 should treat
  finite remaining laps as authoritative, but avoid visible strategy flicker
  from one-frame or short-lived count glitches.
- `capture-20260523-200213-824` proves GR86 3-lap fixed-lap start behavior:
  `SessionLapsRemainEx` and `SessionLapsTotal` become finite `3`, while
  `SessionTimeRemain` is an unusable `604800` sentinel. The capture does not
  include the finish, so a full GR86 finish window is still needed.
- `RaceLaps` is useful as phase/sanity evidence, not as a remaining-lap source.
  It can increment past the final lap after checkered, such as reaching `5` in a
  4-lap Dallara race or `7` after a 6-lap timed Dallara finish.

Checkpoint comparison findings:

- In the 45-minute Dallara timed race, a cold race-start or mid-race join without
  completed leader laps can still produce a useful simple estimate from
  `DriverCarEstLapTime`: `ceil(2700 / 450.9073) = 6`, matching the actual 6-lap
  finish. At a simulated cold mid-race join around session time `1350s`, the
  first available leader-last-lap estimate used leader progress `2.539`,
  strategy-car progress `2.352`, `SessionTimeRemain ~= 1553s`, and leader last
  lap `444s`. That projected finish lap `7` while actual was `6`, so the first
  live estimate was one lap high and safe.
- The same 45-minute race became exact late: by about session time `2500s`, the
  leader last-lap signal had stretched to about `479s`, producing finish lap `6`
  and strategy total `6`. This reinforces the boundary-risk rule: early
  last-lap projections can be a safe overestimate, but the UI should label the
  6/7-lap edge until pace evidence settles.
- The 45-minute capture also exposes two current-logic edge risks. First, when
  the race clock becomes positive just after green, local `CarIdxLapCompleted`
  can still be invalid; falling back to `RaceLaps` as strategy progress can
  undercount remaining laps. Second, when `SessionTimeRemain == 0` while
  `SessionState` is still `4`, skipping the timed branch can fall back to full
  scheduled time and re-expand the estimate. Fuel V2 needs explicit
  `timed-expired-final-lap` handling and an own-checkered policy before treating
  `SessionState >= 5` as zero actionable distance for the strategy car.
- In the 4-hour team race, race start with `DriverCarEstLapTime = 465.166s`
  projected `31` laps from scheduled time, while the actual team and leader
  result was `30` laps. Halfway through stint 1 (`~1821s`) and stint 2
  (`~5616s`), current leader-last-lap projection still produced finish lap `31`.
  Halfway through stint 3 (`~9302s`), halfway through stint 4 (`~12978s`), and
  near the end (`~14300s`/`~14401s`), the same simple projection matched the
  actual 30-lap finish.
- The 4-hour race is a strong adversarial source because the overall leader
  identity changed repeatedly during the race and the strategy/team car had a
  fast-repair-used value of `1` by the final stint. Current progress-based
  projection handled the confirmed lap-down state at the sampled checkpoints,
  but it did not predict the future leader shifts or repair impact before those
  were visible in telemetry. That matches the "known state drives advice,
  projections are scenarios" policy.
- The app's rolling-clean overall-leader pace threshold may be too strict or too
  sparse for long multiclass/endurance conditions with leader changes and pit
  cycles. In these probes, the working current estimate usually came from
  one-frame overall leader last-lap evidence rather than the rolling leader pace
  window. Fuel V2 should decide whether to keep that conservative simplicity,
  lower confidence accordingly, or add a separate rolling field that survives
  leader identity changes without accepting contaminated laps.
- In the 35-minute / 4-lap Dallara race, `SessionLapsRemainEx` was the right
  source at every sampled checkpoint. Treat the finite value as an authoritative
  whole-lap budget, not as a continuous finish-progress equation; for example,
  at mid-race the team car was already around `1.725` laps of progress and
  `SessionLapsRemainEx` was `3`, which is conservative and user-useful even
  though `teamProgress + 3` is not the eventual race total.

Project decisions from this probe:

- Finite live `SessionLapsRemainEx` and `SessionLapsTotal` beat clock-derived
  estimates, even when session YAML also contains a finite `SessionTime`.
- Timed-race projections should expose a finish-lap range or boundary-risk flag
  when the computed value is near a whole-lap boundary. User-facing advice must
  use the higher fuel requirement until confidence improves.
- Pace evidence should carry contamination reasons such as leader on pit road,
  yellow/caution, invalid last-lap values, dirty leader progress, or large
  rolling-pace drift. Contaminated slow pace must not be allowed to
  under-project race length for stop deletion or underfueling advice.
- Active strategy should stop consuming remaining-clock and remaining-lap
  signals once `SessionState >= 5`. Preserve final result/context for summaries,
  but do not keep projecting from post-checkered cooldown clocks or toggling
  remaining-lap fields.

### Action Items

- Add compact replay-window evidence for Dallara `capture-20260522-204847-774`:
  first active clock, after leader laps 1/2/4/5, first zero clock, first state
  `5`, leader final start/finish crossing, and local Dallara final
  start/finish crossing.
- For Dallara replay windows, record the overall leader, local Dallara class
  leader, and strategy car progress separately so Fuel V2 proves it uses the
  overall leader for the race finish event and the strategy car's own projected
  checkered crossing for laps remaining.
- Add a 45-minute Dallara cold mid-race join fixture around session time
  `1350s`, where first-live leader-last-lap projection says 7 laps and the
  actual result is 6, so the UI can prove this appears as conservative
  boundary-risk rather than overconfident advice.
- Add compact replay-window evidence for the GR86 3-lap race showing
  `SessionLapsRemainEx`, `SessionLapsTotal`, `RaceLaps`, and
  `SessionTimeRemain` across race-session start and the first completed lap.
- Add a compact fixed-lap Dallara fixture from `capture-20260523-034827-919`
  covering race start, first lap, each `SessionLapsRemainEx` decrement,
  checkered, and post-checkered remaining-field toggles.
- Add a compact fixed-lap Dallara fixture from `capture-20260522-194832-318`
  covering the early `SessionLapsRemainEx` `4 -> 3 -> 4` blip so UI strategy
  does not flicker on short-lived lap-count glitches.
- Generate compact 4-hour and 24-hour replay-window evidence before using them
  as durable fuel evidence; keep raw `telemetry.bin` out of committed fixtures.
- For the 4-hour capture, include leader-identity-change windows and the
  final-stint fast-repair-used window so Fuel V2 can test sudden leader shifts,
  damage/repair lap-down changes, and post-repair strategy-car progress.
- Generate compact sidecars for representative NASCAR/oval IBT files before
  making oval fuel or caution policy decisions; do not commit source `.ibt`
  payloads.
- Compare raw SDK fields, `LiveRaceProgressModel`, `LiveRaceProjectionModel`, and
  Fuel strategy output for the same frames.
- Add a compact fixture that records race lap budget inputs and expected quality
  classification without committing raw `telemetry.bin`.
- Add "laps after N" fixture sweeps for timed and fixed-lap races. For each
  checkpoint, record leader/team progress, candidate source, primary count,
  possible range, eventual strategy laps to finish, and whether the candidate was
  exact, one-high, or undercounted. Undercounts are failures for Fuel advice;
  one-high timed estimates are acceptable conservative outputs.
  Initial fixture rows should use this compact contract shape:

```text
captureId
sessionPhase
overallLeaderCarId
overallLeaderCompletedLap
overallLeaderLapProgress
classLeaderCarId
classLeaderCompletedLap
classLeaderLapProgress
focusCarId
focusCarCompletedLap
focusCarLapProgress
inputs
paceEvidence
source
confidence
stateFlags
primaryLapsRemaining
possibleLapsRemaining
displayLabel
canDriveFuelAdvice
eventualLapsToFinish
classification
```

  `classification` starts as `exact`, `one-high`, `undercount`, or `blocked`.
  The sweep should directly prove the "never low" rule for Fuel: undercounts fail
  the contract, one-high timed estimates are acceptable conservative results, and
  blocked rows explain why Fuel should not act.
  Use explicit overall-leader, class-leader, and focus/strategy-car checkpoint
  fields instead of one ambiguous `checkpointLap`; endurance, multiclass, and
  lap-down scenarios need those perspectives separated. Include both completed
  lap and fractional progress for each role where available so fixtures can audit
  integer checkpoint behavior and explain timed-race boundary math.
  Include a compact `inputs` object for the raw SDK/session values that produced
  the result, especially `SessionLapsRemainEx`, `SessionLapsTotal`,
  `SessionTimeRemain`, `SessionTime`, `SessionState`, `RaceLaps`,
  `DriverCarEstLapTime`, and the `CarIdx*` progress fields used for overall
  leader, class leader, and focus car. The fixture should explain failures
  without committing or reopening full raw captures.
  Include a compact `paceEvidence` object with selected pace source, pace value,
  sample count or window identity, rolling/window metadata when relevant, and
  contamination flags such as pit, yellow, wet, traffic, draft, damage,
  leader-change, invalid lap, or outlier. Timed projections need this to explain
  why the model chose a 6-lap versus 7-lap budget.
  `paceEvidence` can also include a lightweight `paceMode` hint such as
  `normal`, `fuel-save-candidate`, `push-candidate`, `contaminated`, or
  `invalid`. Do not overbuild this classifier up front. Average lap time may be
  enough for the first V2 model, especially when normal traffic constantly makes
  laps several seconds slower. Let replay testing prove whether a more detailed
  pace-mode split is worth using.
- Add tests that prevent scheduled race time from reappearing as the remaining
  lap budget after an active timed-race clock expires.
- Add a race-start test where `SessionTimeRemain` is positive but local/team
  progress is still invalid, so Fuel V2 does not undercount by falling back to
  `RaceLaps` as strategy-car progress.
- Add an own-checkered test for timed races where the overall leader has
  finished but the strategy car still has to cross its own line; `SessionState`
  and leader checkered must not erase the strategy car's fuel-to-line risk too
  early.
- Add tests that prevent finite lap-count races from being reclassified as
  timed races when `SessionTimeRemain` or YAML `SessionTime` is finite.
- Add a timed-race boundary-risk test where a projection near an integer finish
  lap exposes a range or conservative confidence state instead of a single
  overconfident lap count.
- Add a strategy-rhythm planning fixture that combines conservative lap budget
  with normal historical burn and qualifying/push upper-limit burn, then reports
  repeated N-lap stints plus estimated final-stint remainder, including an
  edge-sensitive flag when the final stint remainder is small.
- Add a pit-cycle-informed timed-race scenario using the 4-hour capture: compare
  leader projection before any stop, after the first leader/class stop, and after
  later stop cycles so V2 can test "displayed conservative counter" versus
  internal pit-adjusted race-length alerts.
- Add fixture expectations for a soft `Possible strategy change` row that can
  surface emerging strategy scenarios with confidence/source labels without
  changing the primary recommendation.
- Add a 35-minute Dallara stretch/no-stop fixture that compares required burn
  after grid/formation fuel against realistic live/history/quali burn, including
  an example where `12.5 L/lap` required versus `13.5 L/lap` baseline is shown
  as a difficult possible strategy rather than a recommendation.
- For that fixture, include both a history-backed early row at first valid team
  progress and a live-only progression that remains "not tracking yet" through
  the first one to two laps unless live burn actually beats the required target
  with margin.
- Add cumulative sector-burn expectations for the same fixture so the possible
  row can update before lap 1 completes, while rejecting single-sector noise on
  very short sectors.
- Decide which `medium` and `low` race-budget states should show a degraded
  source/range label versus hide high-impact advice entirely.

### Open Questions

- Which race-budget states are allowed to show a degraded source/range label,
  and which must hide high-impact advice?
- Should final-lap behavior be anchored to overall leader only, or should class
  winner behavior matter for class-specific strategy displays?
- How should cautions/yellows affect timed-race pace selection: freeze the
  previous green pace for normal burn, classify the yellow as current-race
  context, and keep it out of historical green-burn projections?
- Which shared header/footer display options should consume the lap-budget
  model first, and which should wait until Fuel V2 replay evidence stabilizes?

## Next-Session Backlog

These are Fuel V2 areas to return to next time. Some now have planning sections
below, but still need implementation design, compact replay proof, or final
product decisions before they should drive overlay advice.

Implementation sequencing decision: start V2 by building and validating the
under-the-hood data contracts, not by adding overlay rows. The first slices
should promote core model contracts such as race lap budget / laps logic, fuel
capacity, burn evidence, sector burn evidence, teammate/endurance stint shape,
pit-service evidence, and `nextPitRequest`. Use the new compact data tooling,
replay-window fixtures, and contract tests to prove each model before Fuel,
Pit Service, Track Map, shared header/footer options, or Settings UI consumes it
as user-facing advice.

The initial contract-first pass should:

- define explicit V2 source/confidence fields instead of renderer-local booleans;
- preserve source units, normalized values, scope keys, and rejection/degradation
  reasons;
- produce deterministic fixture outputs for shown/hidden/degraded/rejected
  states before overlay copy is finalized;
- keep shared Core contracts renderer-neutral so Windows native, browser review,
  and localhost/OBS consume the same model shape;
- update durable schema/version/data-contract snapshots only when the model
  becomes persisted user history rather than temporary replay evidence.

Race lap budget and reserve:

- Race lap budget / laps logic V2: make this the first contract target. It
  should classify fixed-lap, timed-race, pre-green, boundary-risk, leader/focus
  disruption, own-checkered, and unavailable states as structured model output
  with source, confidence, and conservative/possible lap counts. It is a shared
  Core model, not a Fuel-owned helper, because it will eventually be consumed by
  Fuel, Pit Service, Session / Weather, Standings, Track Map, and shared
  header/footer display options.
- User fuel margin policy: expose a lap-based Fuel tab setting so the user
  decides the normal extra fuel target; avoid hidden model reserve constants.
- Cautions, yellows, and pace phases: classify edge-state fuel usage as
  current-race context/outlier evidence. Do not blend caution burn into normal
  historical green-burn projections.
- Leader/focus disruption scenarios: fixture leader retirement, leader damage,
  strategy-car meatball/repair, and sudden lap-down changes so Fuel V2 does not
  lower fuel targets on unconfirmed future state.
- Shared race-budget ownership: build lap-budget confidence as a Core model from
  the start. Fuel is the first strategy consumer, but header/footer display
  should eventually be able to show the same laps/race-budget state without
  duplicating Fuel logic.

Fuel usage and baseline evidence:

- Effective tank capacity: parse and model event fuel caps such as
  `DriverCarMaxFuelPct` and `CarClassMaxFuelPct`, starting fuel, limited-fuel
  series, and the difference between physical tank size and usable race fuel.
  Live strategy must use usable session fuel, not physical tank size, whenever a
  session cap exists.
- Historical baseline taxonomy: keep normal green race laps as the primary
  baseline; allow clean matching practice laps by default as event-relevant
  normal burn; keep push/quali as separate upper-limit evidence; treat wet,
  traffic, draft, caution, and formation as context/noise until deliberately
  classified; keep sector evidence separate from full-lap history.
- Historical-only strategy display: make exact normal race/practice history,
  qualifying/push upper-limit burn, or known fuel-save baselines visible as a
  top-section Fuel overlay row when they are strong enough to matter before a
  completed live race lap exists. Keep the row separate from the `Stint N` row
  group; do not hide it only in the Fuel tab.
- Practice and qualifying usage: allow clean matching practice evidence by
  default for race planning; use qualifying/push evidence for upper-limit
  planning when car/rules match.
- Data rejection audit: make live and historical burn gates consistent for hard
  rejects, but allow minor off-track as a weakness/outlier signal that later
  clean laps can demote or exclude from the primary baseline.
- Sector fuel validation: prove sector fuel-level deltas and `FuelUsePerHour`
  integration against completed-lap deltas, then use sector evidence for a
  visible live sector-adjusted lap-burn cell without treating it as historical
  truth until the completed lap confirms it.
- Energy-unit support: preserve source units for fields reported as `l or kWh`
  and avoid contract names that hard-code liters unnecessarily, but keep the
  first strategy implementation combustion-fuel based until kWh captures exist.
  Overlay display must still respect the user's selected fuel units for that
  overlay/surface, including converting liters to gallons when the Fuel overlay
  is configured for gallons. Do not assume a single metric-vs-imperial switch;
  future settings should allow custom combinations such as mph with liters or
  kph with gallons.

Stint planning and strategy language:

- Stint planner behavior: include teammate/endurance stints in Fuel V2 from the
  start. Decide how Fuel V2 should expose planned stints, final-stint targets,
  required saving, completed-stint numbering, teammate stint targets, and active
  driver changes without over-recommending a fragile rhythm.
- Stop optimization and rhythm comparison: include skip-stop and longer-rhythm
  helpers in `Strategy summary` first, not as hard commands. The summary can
  compare pre-race expected rhythm against in-race updated rhythm as the user's
  actual burn, lap budget, traffic, cautions, or stint shape changes. It must
  not show outrageous suggestions; candidate rhythms need bounded assumptions
  and should be hidden or softened when the required saving is unrealistic.
- Stretch/no-stop rows: keep required burn versus known baseline visible in the
  top `Strategy summary` row, then let separate recommendation/advice state say
  whether it is tracking, possible, unsafe, or not tracking.
- UI trust language: defer exact row wording until implementation, because the
  copy will need to be felt out in the actual overlay. Keep the intent clear:
  weak evidence must not look like a command, and strategy rows should remain
  source/confidence aware.

Pit-service evidence and diagnostics:

- Implement the pit evidence data model and diagnostic export shape:
  `pitServiceEvidence[]`, `learnedServiceFacts[]`,
  `unknownCandidateSignals[]`, import safety, and evidence-complete
  reclassification.
- Implement shared `nextPitRequest` consumption across Fuel V2 and Pit Service
  as required V2 infrastructure so fuel, tires, tearoff/wiper, fast repair,
  repairs, and penalty state update both surfaces together.
- Prove service overlap/order by car/service rules so "four tires: +8s" means
  incremental stop loss versus the selected fuel plan, not raw tire duration.
  Strategy-facing service rows must show incremental loss relative to the
  current selected `nextPitRequest`; raw component durations belong in
  diagnostics unless the row is explicitly a total stop estimate.
- Refuel/stop verification: prove a real pit stop happened, then classify what
  actually happened. The core signals are pit entry, pit stall/service activity,
  pit exit, and observed tank delta. Requested fuel and pit-service settings are
  explanatory context, not the source of truth and not a reason to invalidate the
  stop by themselves. A requested amount above available tank capacity is not a
  bad sample when the observed add matches the session-effective cap. For
  example, requesting `50 L` when the tank can only accept `30 L` should
  classify as a valid cap-limited fill, not as failed refuel evidence.
- Fast repair and discrete service proof: collect or import clean samples so
  fast repair, tearoff/wiper, setup adjustments, and other exposed services can
  graduate like tires/fuel. Once isolated, these services should use the same
  unavailable/observed/corroborated/proven promotion ladder as the main service
  facts.
- Required/optional repair handling: consume live repair timers/status while in
  pit lane so strategy can update around known waiting time, but do not pretend
  exact repair duration is a fixed learned service. Historical repair evidence is
  mostly diagnostics or conservative lower-bound context.
- Black-flag/penalty holds: classify stop-and-go and stop-and-hold windows as
  race-control obligations separate from normal service timing. Consume known
  live hold time for current strategy updates, but keep historical penalty holds
  diagnostic unless they are needed to explain a past stop.
- Unknown schema discovery: preserve plausible new `PitSv*`, `dp*`, `dc*`, and
  `CarIdx*` service signals as first-class diagnostics and bundle evidence until
  semantics can be mapped. Unknown signals must not affect overlay advice until
  mapped and promoted.

Pit-lane, team, and shared evidence:

- Fuel-to-pit-box risk: account for pit stall location, pit-lane travel, late or
  wrapped/shared boxes, and minimum fuel at pit entry. This is a V2
  risk/status output, not an optimization input; it should warn when the car may
  reach pit entry but not the assigned stall, and it must not pollute normal
  race-lap burn history.
- Pit-lane travel baselines: prove track/config/version, pit speed,
  `DriverPitTrkPct`, entry/exit behavior, and car effects before using pit loss
  as a strategy constant. When evidence is strong, pit-lane loss can influence
  stop-loss, rejoin, and strategy-summary calculations, but it does not need a
  standalone driving-overlay row by default.
- Opponent/cohort evidence: build same-car/class pit-road distributions from
  `CarIdxOnPitRoad` so local stop classifications can be corroborated or
  flagged as outliers. Cohort evidence is context only: it can strengthen or
  challenge a local classification, but it must not replace local proof for fill
  rate, service timing, or strategy-grade advice.
- Team and endurance model: handle teammate stint length, active driver changes,
  reconnects, scalar fuel validity, and teammate targets as V2 scope from the
  start. If Overlay Bridge provides valid teammate fuel state, consume it like
  live fuel state for the active team car, at sector/lap cadence instead of
  frame cadence. The V2 strategy model should not treat teammate/endurance
  stints as a post-V2 add-on.
- Shared evidence import: support teammate/support diagnostic bundles and future
  Overlay Bridge evidence as source-labeled samples that can strengthen but not
  silently replace local proof. This is separate from live Bridge teammate fuel
  state, which is the current source of truth for that teammate's stint when
  valid.

Pit now versus later and traffic:

- Current stint plan comparison: define exact candidate plans for `pit now`
  versus current stint plan, early stop, pit in `N` laps, and last safe lap.
- Projected rejoin: prove stop-loss plus field projection well enough for
  informational rows such as "pit now exits into traffic", "pit now clear by
  3.2s", and future Track Map `Pit` ghost markers. Treat this as rejoin context
  first, not box/stay-out optimization.
- Tire-payback model: build tire-age, warmup/outlap, traffic, fuel-load, and
  temperature baselines before showing strategy-grade "tires pay back" rows.
  Keep this hidden or explicitly experimental until both stop-loss and on-track
  tire-performance evidence are credible.
- Fuel weight and clean-air value: V3 strategy modeling. V2 can preserve fuel
  load, traffic, and clean-air context as evidence, but should not use those
  effects to recommend underfueling, boxing, or staying out.

Validation and fixtures:

- Every strategy idea should eventually have compact replay evidence for
  `shown`, `hidden`, `shown as possible`, `degraded`, and `rejected` states.
- Fixture known Dallara 35m/45m, 4h GT3, GR86, NASCAR, repair, fast repair,
  black-flag hold, and imported-bundle cases.
- Add diagnostic-bundle import tests that prove a receiving model can re-run
  classification from evidence, not just trust the sender's final label.

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
- `DriverCarFuelMaxLtr`: session-info physical tank capacity. Useful for
  diagnostics and max-fuel sanity checks, but live strategy must use the
  effective session cap when `DriverCarMaxFuelPct` or `CarClassMaxFuelPct` limit
  usable fuel below the physical tank.
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

Unit policy:

- Internal evidence should preserve the source unit and the normalized value used
  for calculations. For current combustion captures, the normalized strategy unit
  is liters.
- Contracts should avoid hard-coding `liters` into field names unless the value
  is explicitly normalized to liters. This keeps room for `l or kWh` telemetry
  without pretending V2 has validated electric/energy strategy.
- Overlay and Fuel tab display must respect the user's selected unit for that
  measurement family and surface. This should not be treated as one blanket
  metric/imperial switch. A user may want mph with liters, kph with gallons, or
  different defaults depending on overlay context. If telemetry and calculations
  are in liters but the Fuel overlay is configured for gallons, displayed tank
  level, burn, target, add amount, and margin equivalents should be converted to
  gallons.
- kWh/energy support should preserve data and units, but strategy-grade kWh
  advice should wait for real captures and validation.

What is not proven available from live telemetry:

- Direct trustworthy `FuelPerLap`.
- Opponent or class fuel levels.
- Teammate-active fuel level from a non-driving/spectator client.
- iRacing-computed fuel to finish.
- iRacing black-box laps-left/fuel-left estimate as a public SDK field.
- A final refuel recommendation for the current race.
- A safe strategy value derived from one frame of `FuelUsePerHour`.

Recommended Fuel V2 source hierarchy for usage:

1. `measured-green-lap-delta`: local/team fuel-level delta over completed valid
   green-lap progress. This should be the only live source allowed to drive
   confident fuel-per-lap strategy.
2. `measured-partial-lap-delta`: local/team fuel-level delta over enough clean
   partial-lap distance. This may become a future early-race estimator, but it
   needs replay evidence and stricter confidence before driving stop deletion.
3. `historical-exact-normal-context`: matching normal green race or practice
   history, clearly labeled as historical/model rather than live measured.
   Practice is allowed by default when car/track/rule context matches because it
   is real event usage.
4. `historical-upper-limit`: matching qualifying or push-lap usage. This can
   inform conservative high-burn bounds, but should not replace normal green
   race burn for core advice.
5. `historical-near-context`: nearby history when exact context is missing,
   lower confidence and wider displayed range.
6. `instantaneous-smoothed`: smoothed `FuelUsePerHour` converted through
   `DriverCarFuelKgPerLtr`. This is diagnostic-only until it agrees with
   measured fuel-level deltas across replay windows.
7. `level-only`: current fuel exists, but usage is unknown.
8. `unavailable`: no valid fuel level.

For live measured usage, Fuel V2 should preserve the current filtering posture:
use only racing/green local active context; reject pit road, pit stall,
pit-service, garage, focus-on-other-car, invalid fuel, refuel/reset, negative
progress, and implausible fuel deltas. Minor off-track should be a weakness
signal rather than an automatic reject unless fuel/progress jumps or later clean
laps prove it is an outlier. Current fuel level can still update in grid, pit,
and pre-green phases, but those frames must not seed burn-rate evidence.

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
- In the 4-hour team-race capture, teammate stints had no direct
  `FuelLevel`/`FuelUsePerHour` scalar available to the non-driving local client,
  even though team-car timing and pit context remained available through
  `CarIdx*` arrays. The iRacing black box may display laps-left information from
  simulator-internal state or an internal estimate, but that value is not proven
  exposed in the captured SDK schema.

Replay questions:

- How quickly after green can a completed-lap fuel delta become available for
  common race lengths?
- Can partial-lap deltas become reliable enough for early-race planning without
  underfueling?
- Does `FuelUsePerHour` become stable after smoothing by throttle/green-lap
  windows, or does it remain too sensitive for strategy?
- In team races, can any newer SDK schema or session state expose the black-box
  fuel/laps-left value while a teammate is driving, or is it simulator UI-only?
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
- Fuel V2 should treat formation burn as an operational adjustment/input to
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
- tow/reset/garage transitions, and severe off-track/reset-like events: fuel
  level or progress can jump or become invalid and must not be treated as
  consumption. Minor off-track without jumps can remain a weak sample.

Product decision: edge-state fuel deltas should update current fuel and produce
risk/status evidence, but they should not seed normal fuel-per-lap strategy
unless replay proof shows a specific edge estimator is reliable.

Starting classification policy:

- Clean green race laps are the normal burn baseline.
- Yellow, formation, pit, repair, tow/reset, severe off-track, and other
  edge-state samples are current-race context unless explicitly classified into
  their own non-green bucket. Minor off-track can remain a weak green sample when
  fuel/progress continuity is intact.
- Outlier samples should explain what is happening now, but should not rewrite
  normal historical projections.
- The overlay should stay dumb at the core: lap budget, selected clean burn
  baseline, current fuel, and user margin. Edge-state evidence can inform,
  label, degrade, or hide advice, but should not silently retune the primary
  calculation.

Fuel advice accounting decision: keep clean racing consumption and edge-state
adjustments separate. Normal green-lap/sector samples should produce the primary
`fuel per lap` estimate. Pre-green burn, pit-entry-to-box burn, pit-exit/blend
burn, and service deltas should be tracked as operational adjustments or
current-state context that affects recommendations such as `fuel to next stop`,
`fuel to finish`, `minimum fuel at pit entry`, and `fuel to pit box risk`.

This prevents pit-lane artifacts from corrupting the driver's race-pace fuel
number while still accounting for the real risk case: a car can have enough fuel
to reach pit entry but not enough fuel to reach a late/shared pit stall.
Fuel-to-box risk should use current fuel, expected burn to pit entry/stall,
`DriverPitTrkPct`, pit-lane travel/burn evidence, and the current assigned-box
context. It should surface as a status/risk row such as `low fuel to stall`, not
as a strategy optimizer, and its pit-lane consumption samples should stay out of
normal clean-lap history.

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
- During refuel service, which stop-window signals most reliably prove entry,
  service activity, exit, and actual tank increase? Requested fuel can explain
  intent, but observed stop effects should drive the evidence classification.

### Pit Service, Tire, And Repair Timing

Fuel-service time becomes straightforward once Fuel V2 has a credible fill-rate
baseline and a credible fuel-to-add target:

```text
fuelToAddLiters = max(0, targetFuelAtPitExit - expectedFuelAtBox)
fuelServiceSeconds = fuelToAddLiters / fillRateLitersPerSecond
```

The hard part is not the arithmetic. The hard parts are proving the fill-rate
sample, proving which services were active during the window, and deciding
whether the final service estimate is fuel-only, tire-overlapped, repair-
overlapped, or contaminated by a driver swap, tow, reset, garage, or unknown
service state.

Timing model:

- Split pit time into three separate buckets: pit entry to box, stationary
  service, and box to pit exit or racing context.
- Estimate fuel service from liters to add and credible fill-rate. For marginal
  fuel decisions, the incremental cost is `additionalLiters / fillRate`.
- Treat proven fixed-duration tasks as car/service-rule constants. Once replay
  evidence proves how long a tire swap, windshield tearoff, or other static
  pit-service task takes for car `N` under a rule set, that value should not need
  a multi-capture synthesis model every race.
- Treat stationary service time as the maximum of active service buckets until
  replay evidence proves a service is serialized:

  ```text
  stationaryServiceSeconds =
      max(fuelServiceSeconds, tireServiceSeconds, repairServiceSeconds, otherServiceSeconds)
  ```

- Keep pit-lane travel separate from stationary service. A late pit box matters
  for fuel-to-box risk and total pit-lane loss, but not for the fill-rate
  itself.
- Tire timing should compare equivalent stops, not just add tire time on top of
  fuel time. The dormant V1 helper already has the right rough shape:

  ```text
  noTireStopSeconds = max(fuelServiceSeconds, noTireServiceSeconds)
  tireStopSeconds = max(fuelServiceSeconds, tireServiceSeconds)
  tireTimeLossSeconds = max(0, tireStopSeconds - noTireStopSeconds)
  ```

- Fast repair should be modeled like other discrete pit services such as tires,
  refuel, windshield tearoff, or wiper service where exposed. Once isolated, it
  can become a durable car/rule service constant and use the same promotion
  ladder as the main service facts. Actual required/optional repairs remain
  separate because damage repair duration can vary dramatically. A meatball
  repair window should never be rolled into normal tire, fuel, tearoff,
  fast-repair, or pit-lane timing baselines.
- Black flags and penalty holds must be a separate race-control class, not a
  repair or service class. A stop-and-go or stop-and-hold penalty adds enforced
  hold time to the stop, but it should not contaminate tire, fuel, repair, or
  pit-lane baselines. When live race-control state exposes a known hold, Fuel V2
  can update current strategy around that obligation. Historical penalty holds
  should remain diagnostics/explanation, not pre-stop service predictions.
- In the normal pit-service model, nearly everything can become a durable
  constant, solved equation, or track/session baseline after enough clean
  evidence. Fuel quantity is strategy-dependent, but refuel time becomes a solved
  equation once the per-liter rate is proven. Pit-lane travel should be treated
  as dynamic until proven, then promoted to a durable strategy input when repeat
  stops show the same track, pit box, pit speed, entry/exit behavior, and no
  traffic/penalty contamination. Promotion means it can affect total stop,
  rejoin, and `pit now vs later` math; it does not imply the overlay must show a
  separate pit-lane loss row.

Stable versus synthesized service model:

- Stable after proof: tire swap duration by car/service rules, tearoff/wiper
  duration where exposed, fast-repair duration, no-tire stationary overhead,
  driver-swap duration if exposed, service overlap and ordering rules, and any
  setup-adjustment service that appears in telemetry and proves to have a fixed
  duration.
- Solved after proof, but variable per stop: refuel seconds per liter. Once
  repeated clean stops prove the car/rules fill-rate, the time for a given fuel
  quantity is an equation, not a guess.
- Track/session baseline after proof: pit-lane travel/loss from entry to box and
  box to exit. Once repeated clean stops prove it is stable for the track,
  assigned box, pit-speed rule, and entry/exit behavior, it can become a durable
  strategy input instead of a live-only estimate. Use it inside total stop,
  rejoin, and strategy-summary calculations before adding any standalone
  display.
- Calculated every stop, not unknown timing constants: liters to add, fuel
  service duration, requested tires, requested tearoff, selected fast repair,
  and any proven fixed service task selected by the strategy.
- Truly dynamic every stop: actual required/optional repair duration, plus
  pit-lane travel when the baseline is not proven or the stop is contaminated by
  traffic, abnormal entry/exit, tow/reset/garage transitions, penalty holds, or
  contradictory stop-window/tank-delta signals. These windows should invalidate
  or degrade the stop sample rather than become normal timing constants.
- Required/optional repairs should primarily be consumed live from repair
  timers/status while the car is in pit lane. If telemetry says there are six
  minutes of repair remaining, Fuel V2 can update strategy around that known
  waiting time. Historical repair evidence can exist as diagnostics or
  conservative lower-bound context, but it should not become a fixed service
  constant or a normal pre-stop prediction.
- Setup adjustments such as ARB or wing should fit the stable-service bucket if
  iRacing exposes reliable request/status fields for the car. Current corpus
  searches prove pit-service request fields for tires, fuel, fast repair,
  tire pressures, and windshield tearoff; ARB/wing pit-service request fields
  are not yet proven in the local captures.

Scope-key model:

- Default pit-service mechanics should be scoped per car first. Tire service,
  no-tire stationary overhead, tearoff/wiper service, fast repair, service
  overlap/order rules, and any exposed setup-adjustment service are fundamentally
  properties of what iRacing does to that car during service.
- Refuel rate should also start as car-scoped, then include service-rule
  modifiers if evidence shows the series/session changes the fill behavior.
- Physical tank capacity is per car by default, but usable race fuel is
  car plus series/session rules. Fuel V2 must distinguish physical capacity,
  starting fuel, requested fuel, actual fuel delta, and effective event caps
  such as limited-fuel Dallara races where the race does not allow the full
  physical tank. Live strategy calculations must use the effective event cap as
  the capacity input.
- Pit-lane travel/loss should start as car plus track, then include the assigned
  pit box, pit-speed rule, entry/exit behavior, and traffic/penalty rejection.
  If repeated clean stops prove the same track/box/rule combination is stable,
  that value can be promoted to a durable baseline for strategy comparisons and
  overlay calculations without requiring a visible pit-loss row.
- Historical baselines should store the scope key that proved the value, not
  only the numeric result. A future strategy row needs to know whether it is
  using exact car/track/session evidence, car-only service evidence, or a weaker
  borrowed baseline.

Telemetry-backed scope dimensions:

- Car identity is well represented by `CarID`, `CarPath`, car display names, and
  `DriverCarVersion`. Fixed service mechanics should key primarily by car, with
  `DriverCarVersion` retained for drift/change review instead of silently mixing
  old and new iRacing car behavior.
- Service/rule identity is represented by `SeriesID`, `SeasonID`, `RaceWeek`,
  `CarClassID`, `CarClassMaxFuelPct`, `DriverCarMaxFuelPct`,
  `FastRepairsLimit`, fixed setup, team racing, dry tire set limits, weight
  penalty, and power adjustment. These are rule/context fields, not proof by
  themselves, but they are the right dimensions to keep when deciding whether a
  car-only baseline can be reused.
- Track identity is represented by `TrackID`, `TrackName`,
  `TrackDisplayName`, `TrackConfigName`, and `TrackVersion`. Pit travel should
  use the track/config/version, while most stationary service mechanics should
  not split by track unless evidence proves track-specific behavior.
- Pit-lane context is represented by `TrackPitSpeedLimit`,
  `TrackNumPitStalls`, and local `DriverPitTrkPct`. These matter for pit-lane
  travel/loss, fuel-to-box risk, and rejoin/traffic projection. In the inspected
  captures, `DriverPitTrkPct` is available for the local driver, but not as a
  per-driver field in `DriverInfo.Drivers[]`, so opponent pit-box location cannot
  be directly read from the same field for the whole grid.
- Session type should be stored, but not used as a hard split for stationary
  service proof by default. Practice, qualifying, and race can likely share fuel
  fill-rate, tire service, tearoff/wiper, fast repair, and overlap/order evidence
  when car and service rules match. Race-vs-practice matters for strategy
  applicability, traffic, penalties, pit assignment, driver swap, and fuel caps,
  but not necessarily for the physical service mechanics.
- Effective fuel capacity should use both physical `DriverCarFuelMaxLtr` and
  rule caps such as `DriverCarMaxFuelPct` / `CarClassMaxFuelPct`. The inspected
  Dallara captures show the same car and track with `0.680` and `0.800` fuel
  caps across different series/rules. That must split live strategy fuel
  capacity. For historical service timing, fuel cap is context only: the
  observed liters added and service window should drive fill-rate classification,
  while cap metadata can help explain which service/rule shape was active.
  Capacity-dependent live advice should be hidden or degraded when this
  effective cap cannot be parsed or inferred safely.
- iRacing `BuildVersion`, `DriverCarVersion`, and `TrackVersion` should be
  retained as version evidence. A version change does not automatically discard
  a proven baseline, but it should make drift detection more sensitive and
  explain why a previously proven value may need fresh corroboration.

Recommended initial scope policy:

| Evidence target | Initial scope key | Do not split by default | Why |
| --- | --- | --- | --- |
| Fuel fill-rate | car + car version + service/rule signature; retain fuel cap as context | track, practice vs race, fuel cap by default | Refueling is learned from observed liters added and service windows. Fuel cap can help narrow the active service/rule shape, but should not split timing evidence unless repeated samples prove cap-dependent behavior. |
| Tire service duration | car + car version + tire/service-rule signature | track, practice vs race, fuel cap | Tire service should be a car/rules constant once isolated. |
| Tearoff/wiper/setup service | car + car version + exposed service-rule signature | track, practice vs race | These are stationary service mechanics, not track geometry. |
| Fast repair duration | car + car version + fast-repair/rule signature | track, practice vs race | Fast repair is a discrete service once isolated; exact availability comes from rules. |
| Required/optional repair live status | car + car version + repair/rule signature | track for stationary repair | Damage varies too much for fixed timing. Live repair timers/status are useful for current strategy; historical repair data is lower-bound/diagnostic context. Pit travel stays separate. |
| Service overlap/order | car + service-rule signature + service-shape bucket | track, practice vs race | Overlap behavior is how iRacing services that car; service shape matters more than session type. |
| Pit-lane travel/loss | track/config/version + pit speed + `DriverPitTrkPct` + car + entry/exit behavior | stationary service rules | Track geometry, assigned box, and pit speed dominate this bucket. Use it as a calculation input when strong; no standalone overlay row is required. |
| Fuel-to-box risk | track/config/version + `DriverPitTrkPct` + current fuel burn/rate | stationary service proof | A late pit box affects whether the car reaches the stall, not the service duration. |
| Penalty hold | race-control rule + session event context + observed hold | car/track service baselines | Penalty time is an obligation layered on top of service, not a normal service mechanic. |

If a future sample disagrees, prefer widening or narrowing the rule signature
based on evidence rather than assuming every telemetry field must be part of the
primary key. The model should store rich metadata, then promote the smallest key
that explains the observed behavior without hiding drift.

Promotion and corroboration model:

- Promotion thresholds should not be fixed sample counts alone. A single clean
  local stop can be strong evidence for a known service task, while multiple
  messy stops can still be unusable. Promote based on sample cleanliness,
  variance, scope match, and whether independent signals agree.
- Field-level pit windows can aid confidence. `CarIdxOnPitRoad` lets Fuel V2
  infer pit-road entry/exit windows for every observed car, and per-car tire
  compound fields can sometimes add context. This is not full opponent service
  telemetry, because it does not expose opponent fuel, requested service,
  pit-stall state, or repair timers.
- Same-car or same-class cohort stops in the same race can corroborate the local
  interpretation. If every LMP2 in the field has a similar pit-road window, such
  as about `45s`, that does not prove they all took the same fuel or tires, but
  it strongly suggests they likely performed the same service family and that
  the local stop is not an outlier.
- Cohort timing should tighten confidence intervals and flag suspicious samples:
  local stop much longer than cohort could mean repair, penalty hold, missed box,
  traffic, tow/reset, or an extra service; local stop much shorter could mean no
  fuel, no tires, drive-through, or incomplete detection.
- Opponent/cohort evidence should be labeled as corroborating evidence, not as a
  promoted baseline by itself. It can raise confidence in a local stop
  classification when local telemetry already supports that classification, and
  it can block promotion when the local stop disagrees with the race cohort.
  Opponent windows should not directly teach fuel fill-rate, tire timing,
  service overlap, or refuel strategy because iRacing does not expose their
  requested service, tank delta, stall state, or repair status.

Confidence states and UI copy policy:

| State | Evidence requirement | Allowed user-facing text | Not allowed |
| --- | --- | --- | --- |
| `unavailable` | Required telemetry or scope key is missing, or the signal has never been seen for this scope. | Hide the row by default, or show "service timing unavailable" in diagnostics/details. | No seconds, no recommendations, no "likely." |
| `learning` | Telemetry exists, but there are no clean local samples yet, or current samples are contaminated/tiny/contradictory. | "Learning tire service from clean stops" or "Need a clean fuel stop for Dallara P217." | No strategy-grade timing, no "free tires," no exact stop-loss claim. |
| `observed` | At least one occurrence proves the signal exists for the car/scope: for example a tire counter changed, fuel was requested, a repair timer appeared, or a service flag was present. The signal does not need to be isolated yet. | "Observed tire service signal for this car" in diagnostics/post-race detail. | No timing/rate claims, no durable baseline language, no hard advice, no cross-car reuse. |
| `corroborated` | The signal has been isolated to a credible time, rate, quantity, or service-shape sample. This can be one clean sample when independent local signals agree and contamination checks pass. | "Corroborated estimate: Dallara fuel-only service about 1.7 L/s" with scope and sample count. Soft scenario rows can use this when labeled. | No unconditional "will"; do not claim the value is repeatable or solved yet. |
| `proven` | Multiple isolated samples in the same scope show low variance, contamination checks pass, and the service shape/overlap behavior is known. | Strategy-grade rows: "Add 24L: about 14s fuel service" or "Four tires cost +N.s versus this fuel plan." | No scope expansion without evidence; still show source/scope in details. |

Sample rejection and degradation should be explicit. A contaminated sample can be
recorded for diagnostics and post-race explanation, but the user-facing service
state should fall back to `learning` or `unavailable` until a clean sample exists.
Rejected samples include repair windows, black-flag holds, tow/reset/garage
transitions, missing local fuel scalar, missing `PitstopActive` for service
timing, tiny fuel additions used as fill-rate baselines, and windows where
service flags disagree with the observed tank/counter changes.

Implementation posture should be more conservative than the state vocabulary
allows. `learning` and `observed` states are useful for diagnostics, post-race
analysis, and explaining why a row is not available, but they should not power
overlay advice by default. Fuel V2 overlay advice should normally require at
least `corroborated` evidence, and high-impact rows such as tire time loss,
pit-now-vs-later, repair timing, or "tires are free" should prefer `proven`
evidence unless a product decision explicitly allows a softer, source-labeled
scenario row.

Signals should help prove each other. Once a component is `proven`, it becomes a
known input in future stop decomposition instead of remaining part of the
unknown service time. For example, a proven fill-rate lets Fuel V2 compute the
expected fuel-service duration for a stop, which makes tire, tearoff, repair, or
penalty residuals easier to isolate. A proven tire-service duration similarly
helps classify whether a later fuel-plus-tire stop was normal, delayed, repaired,
or penalty-held.

That decomposition must respect the service overlap model. Fuel, tires, tearoff,
repairs, and holds are not automatically additive. When services overlap, a
proven component may explain the stationary time as the limiting `max(...)`
bucket rather than as a subtraction from total service time. When services are
serialized or penalty-held, the model can allocate explicit residual time to the
serialized component. A stop can promote an unknown signal only when the known
components and overlap/order rules leave a defensible isolated duration or rate.

`proven` is not a terminal state. Fuel V2 should keep collecting clean samples
after a signal is proven so it can tighten the mean, variance, and scope model,
and so it can detect drift. A previously proven value that changes materially
should not silently overwrite the baseline or continue powering advice as if
nothing changed. It should move through an explicit review state, such as
`proven_with_drift`, until enough new isolated samples prove whether the old
baseline was wrong for this scope, iRacing changed the service behavior, the
series/session rules changed, or the latest stop was contaminated.

The UI tab can expose this without alarming the driver mid-race: "Proven tire
service changed from 39.2s to 43.8s in the last clean stop; collecting more
samples." Overlay advice should either keep the older proven value with a
visible stale/drift label, use the conservative side of the old/new range, or
withhold the row, depending on how high-impact the advice is.

Current capture confidence by pit signal:

| Signal | Current capture confidence | Evidence | UI posture today |
| --- | --- | --- | --- |
| Pit-road entry/exit window | `corroborated` for local/team detection, `learning` for durable stop-loss baselines. | Many summaries have `CarIdxOnPitRoad` pit windows. The 4h GT3 baseline has three consistent pit-road windows at `63.867s`, `66.667s`, and `63.933s`. Dallara, GR86, BMW, NASCAR, and Porsche summaries also prove the window detector fires. | Safe to show "last pit-road time" in diagnostics. Do not yet use pit-lane loss as a proven strategy constant except as a scoped estimate. |
| Pit-stall signal | `observed` to `corroborated` for local stall detection. | Race summaries and live diagnostics repeatedly set `pit_stall_signal` / `sawPlayerPitStall`, including Dallara, GR86, BMW, and NASCAR captures. Some windows have long stall time without service. | Safe as context: "in stall." Not enough by itself to prove service or fuel/tire timing. |
| `PitstopActive` service window | `observed`. | Clear local service-active windows exist in the Dallara 45m fuel stop (`17.450s`), Dallara 35m small fuel stop (`3.317s`), Dallara abnormal long stop (`69.467s`), and NASCAR tire stop (`58.750s`). | Can label a last stop as service-active. Needs matching service shape before it teaches a baseline. |
| Fuel request flags and requested liters | `observed`, close to `corroborated` as intent/context for Dallara fuel-only classification. | Live diagnostics preserve `entryPitServiceFlags = 48` and requested fuel near `30L` / `4L` for the Dallara 45m and 35m fuel stops, matching actual tank deltas. Summary-only rows can lose this because final flags clear to `0`. | Show in diagnostics/details. Persist flag transitions, but do not let request/actual mismatch invalidate an otherwise real stop. Observed stop window and tank delta are the source of truth. |
| Actual fuel delta | `corroborated` for local tank delta when fuel scalar exists. | Dallara 45m added `29.629L`; Dallara 35m added `3.594L`; NASCAR tire stop added only `0.847L`; several no-service windows correctly show zero or negative burn-only deltas. | Safe as source text: "added 29.6L." Combine with service-active before timing advice. |
| Fill rate | `observed`, not proven. | Best candidate is Dallara 45m at `1.698 L/s`. Dallara 35m is a small-add low-confidence `1.084 L/s`. Dallara abnormal long stop gives `0.141 L/s` and should be rejected/contaminated. NASCAR tire stop gives `0.014 L/s` and proves naive `fuelAdded / serviceActive` can be wrong. 4h GT3 aggregate reports `2.680 L/s`, but lacks rich per-stop proof in the compact summary. | Allow "observed fill-rate candidate" in diagnostics. No proven per-car fill-rate row yet. |
| Tire service | `observed`, not corroborated by rich local windows yet. | NASCAR has one tire-set-change stop with `58.750s` service active. The 4h GT3 aggregate reports tire-change service at `39.200s`, but compact data lacks per-stop service/flag/tire-counter detail. Dallara race stops show no tire changes. | "Observed tire service in NASCAR capture" is allowed. No "tires cost N seconds" strategy copy yet. |
| Tearoff | `observed` as requested service, duration unproven. | Dallara fuel stops have flags `48`, which decode as fuel plus tearoff. There is no isolated tearoff-only or fuel-without-tearoff comparison in the current race evidence. | Can mention request state in diagnostics. Must not claim tearoff duration. |
| Fast repair | `unavailable` / `learning`, but service class is static once isolated. | Current summarized race pit stops do not show a clean `fastRepairUsed = true` service window. | Hide timing rows until isolated. Diagnostics can say no clean fast-repair sample; once proven, treat like tires/fuel/tearoff rather than like open-ended damage repair. |
| Other discrete services | `unavailable` / `learning` until isolated. | Tearoff/wiper, setup adjustment, and similar discrete pit-service options should be discovered from request/status fields and clean stop windows. | Use the same service-fact promotion ladder as fuel, tires, and fast repair once isolated. Do not mix them with variable required/optional repair timing. |
| Required/optional repair timers | `observed` as live countdown fields when present; `learning` for historical lower-bound context. | `PitRepairLeft` and `PitOptRepairLeft` are present in telemetry schemas, but no clean repair service window has been classified from the current race summaries. | Use live repair timers/status to update current strategy while in pit lane. Do not present fixed pre-stop repair-duration advice from history. |
| Black-flag / penalty hold | `observed` when live race-control hold state is known; `learning` for historical explanation. | Live diagnostics show `pitWindowsWithBlackFlag = 0` in the inspected diagnostics. Some windows carry session-flag bits, but no confirmed stop-and-go or timed hold is classified. | Use known live hold time to update current strategy. Keep penalty holds separate from service timing and historical baselines. |
| Cohort/opponent pit timing | Signal available, model still `learning`. | `CarIdxOnPitRoad` can expose field pit-road windows, but current capture summaries do not yet include cohort distributions by car/class/service family. | Use as corroboration, outlier detection, and diagnostics. It cannot promote local service timing, fill-rate, or strategy advice by itself. |
| Service overlap/order | `learning`. | Current captures show fuel and tearoff can be requested together, and tire/fuel contamination exists, but we do not yet have paired clean no-tire/tire or fuel-only/fuel-plus-tire comparisons in the same car scope. | Do not show "tires are free" or additive tire/fuel math until proven. |

Current telemetry/model inventory:

- Live pit context includes `OnPitRoad`, team/car pit-road signals,
  `PlayerCarInPitStall`, `PitstopActive`, `PlayerCarPitSvStatus`,
  `PitSvFlags`, `PitSvFuel`, tire-set counters, fast-repair counters,
  `PitRepairLeft`, and `PitOptRepairLeft`.
- Race-control context includes global `SessionFlags`, per-car
  `CarIdxSessionFlags`, and `PlayerCarPitSvStatus`. Existing Flags overlay code
  already distinguishes local black flags, disqualification, scoring-invalid,
  furled black, and repair/meatball-style local flags from normal global session
  flags.
- Opponent/cohort context currently comes mainly from `CarIdxOnPitRoad`,
  `CarIdxTireCompound`, class/car metadata, timing/scoring rows, and local
  race-control flags. That is enough to infer cohort pit-road windows and
  compare stop durations, but not enough to know another car's exact service
  request or fuel amount. Treat these windows as a reason to ask "does our local
  stop look normal?" rather than as a replacement timing or fuel source.
- Pit request context includes `dpFuelFill`, `dpFuelAddKg`, tire-change request
  controls, pressure controls, `dpWindshieldTearoff`, and fast-repair request
  controls.
- `LiveFuelPitModel` and `LivePitServiceModel` already map most of these fields
  into shared live state. `LivePitServiceRequest.FromFlags` decodes the service
  flags into requested corners, fuel, tearoff, and fast repair.
- `HistoricalSessionAccumulator.PitStopBuilder` already records pit-lane
  seconds, pit-stall seconds, `PitstopActive` seconds, fuel before/after, fuel
  added, fill-rate, tire-set change, fast-repair use, and confidence flags.
- Current summary data can lose useful `PitSvFlags` transitions because the
  recorded summary flag can be the final flag value after service has cleared.
  Compact replay windows should preserve flag transitions instead of trusting
  only the final per-stop value.

Schema discovery and unknown signal policy:

- Fuel V2 should not require a production code release before it can notice a
  plausible new pit-service signal. Raw capture import and diagnostics should
  preserve telemetry-schema metadata for unknown fields that match established
  iRacing naming patterns, then classify them as `unknown_observed` until a
  human or future mapping promotes them.
- Known naming families in the inspected schema include:
  - `PitSv*`: pit-service request/status values, such as `PitSvFlags`,
    `PitSvFuel`, tire pressures, and pending tire compound.
  - `dp*`: pit-service control/request values, such as `dpFuelAddKg`,
    `dpFuelFill`, tire-change requests, tire pressure adjustments,
    `dpWindshieldTearoff`, `dpFastRepair`, and fuel auto-fill fields.
  - `dc*`: driver/in-car controls that may affect service or race context, such
    as pit-speed limiter, wiper controls, dash page, brake bias, and other car
    controls.
  - `CarIdx*`: per-car array signals, such as pit-road state, tire compound,
    session flags, fast-repair counters, and timing/scoring rows.
- Pattern confidence should require more than a prefix. The schema record should
  also support the classification through description text, unit, type, count,
  value transitions during a pit window, or correlation with known service
  events. For example, a new `dp...` field with "Pitstop" in its description
  and changes only around service request windows is a stronger candidate than a
  generic `dc...` driver control that changes during normal driving.
- Unknown fields should start diagnostics-only. They can be stored, graphed, and
  included in shared evidence bundles, but they should not affect overlay advice
  until promoted to an explicit known signal or until a general classifier has
  enough corroborated evidence to prove what the field means.
- Treat unknown candidate signals as first-class evidence for diagnostics and
  support bundles, not as throwaway logs. They are useful precisely because a
  later build or support workflow can map them without requiring a new raw
  capture.
- A newly discovered field should carry the original schema name, type, unit,
  description, value range, sample transitions, first/last capture versions, and
  proposed semantic family. If iRacing adds a new service such as another setup
  adjustment, wiper/tearoff variant, or pit-service option, Fuel V2 should be
  able to retain the evidence immediately and later backfill semantics without
  losing the original captures.

Fill-rate credibility rules:

- A fill-rate sample needs a clear local fuel scalar, valid tank increase, clear
  `PitstopActive` window, and enough fuel added that fixed start/stop overhead
  does not dominate the denominator.
- Fuel-only or mostly-fuel stops are the best fill-rate samples. Tire, fast
  repair, required/optional repair, tow, garage, and driver-swap windows should
  either be rejected or labeled as contaminated until a fuel-active sub-window
  can be isolated. Once fast repair itself is proven, it can become a known
  fixed service component instead of a generic contamination reason.
- Tiny fuel additions should be low confidence even when the computed rate is
  plausible. They are useful as service-window evidence, not as primary
  fill-rate baselines.
- Multiple clean stops at the same track can solve the fuel coefficient even
  when each stop adds a different quantity. If pit-lane time and all other
  service tasks are effectively constant, fit or compare:

  ```text
  serviceSeconds = fixedStationaryOverhead + litersAdded * secondsPerLiter
  secondsPerLiter = deltaServiceSeconds / deltaLitersAdded
  fillRateLitersPerSecond = 1 / secondsPerLiter
  ```

  Four same-car, same-track stops with similar pit-lane time, no tire/repair/
  setup changes, no penalties, and varied fuel quantities should be enough to
  produce a high-confidence refuel-per-liter baseline or expose that another
  service variable is contaminating the sample.
- Service overlap/order is itself a learnable collection-model fact, likely
  scoped by car and service rules. Fuel, tires, driver swap, and possibly fast
  repair will usually start together, so strategy copy should express the
  incremental stop cost rather than the raw component duration: "four tires add
  +8s to this stop" means the selected fuel plan plus tires is eight seconds
  longer than the selected fuel plan alone, not that the tire service itself is
  eight seconds long. That overlap rule should be treated as a hypothesis until
  repeated captures prove it for the relevant car/service shape. Other services,
  including tearoff/wiper, setup adjustments, fast repair, and repair classes,
  must be observed and proven before the model assumes whether they overlap,
  serialize, or create a fixed overhead.
- Pit-lane travel can use the same evidence posture. Multiple clean stops at the
  same track and pit assignment should produce a stable pit-lane loss baseline:

  ```text
  totalStopLossSeconds =
      pitLaneTravelSeconds + stationaryServiceSeconds + release/mergeVarianceSeconds
  ```

  Once `pitLaneTravelSeconds` is proven stable enough, Fuel V2 can combine it
  with fixed service constants and fuel-per-liter timing to estimate the actual
  time lost by a planned stop. That turns pit decisions into durable strategy
  comparisons, such as whether taking two tires, four tires, or an added fuel
  quantity changes total race time.
- Black-flag penalty windows should be classified before any timing sample is
  accepted. If the stop includes a stop-and-go or stop-and-hold penalty, the
  enforced hold belongs in a `penaltyHoldSeconds` bucket:

  ```text
  totalStopLossSeconds =
      pitLaneTravelSeconds
      + stationaryServiceSeconds
      + penaltyHoldSeconds
      + release/mergeVarianceSeconds
  ```

  Penalty time can still matter for strategy and post-race explanation, but it
  should not teach the model that tires, fuel, repairs, or pit-lane travel took
  longer than normal.
- Tire timing should be bucketed by actual and requested service shape:
  no tires, two tires/side tires where applicable, four tires, tire-only,
  fuel-plus-tires, and tire-plus-repair.
- Stable service constants need proof, then reuse. For example, once a Dallara
  tire-service duration is proven by clean windows, Fuel V2 should not require
  more Dallara captures to know the tire duration unless the car, tire rule,
  tire count, compound behavior, or iRacing service behavior changes.
- Repair and fast-repair timing should be bucketed separately. Fast repair is a
  discrete service that can become a proven constant. Required repair, optional
  repair, and unknown repair/service status stay in dynamic or lower-bound
  buckets.
- Fill-rate baselines should be scoped tightly at first, likely by car and
  session rules, then widened only after replay evidence shows the rate is stable
  across tracks, series, and fuel caps.

Initial replay and history findings:

- Dallara 45-minute race, `capture-20260522-204847-774`: one fuel-only-looking
  service stop at `2117.217s -> 2159.783s`, pit lane `42.567s`, pit stall
  `17.317s`, service active `17.450s`, fuel added `29.629L`, observed fill rate
  `1.698 L/s`, no tire-set change, no fast repair. This is the best current
  Dallara fill-rate candidate.
- Dallara 35-minute race, `capture-20260523-034827-919`: one small-add stop at
  `1644.650s -> 1673.650s`, pit lane `29.000s`, pit stall `3.733s`, service
  active `3.317s`, fuel added `3.594L`, observed fill rate `1.084 L/s`, no
  tire-set change, no fast repair. Useful evidence, but lower confidence because
  the small add makes start/stop overhead large relative to the measured service.
- NASCAR race, `capture-20260515-210810-124`: a tire-change stop had service
  active `58.750s`, tire-set changed, and only `0.847L` added. The computed
  `0.014 L/s` is not a valid fill-rate sample; it is evidence that tire/repair
  buckets can contaminate naive `fuelAdded / serviceActive` math.
- 4h GT3 baseline, `capture-20260426-130334-932`: aggregate history reports
  average pit lane `64.822s`, service `39.200s`, observed fill rate `2.680 L/s`,
  and tire-change service `39.200s`. The compact summary also has three pit
  windows of `63.867s`, `66.667s`, and `63.933s`, but it lacks rich per-stop
  service details. This should become a compact replay-window target before GT3
  tire/fuel timing advice is trusted.

Product decisions and hypotheses:

- Fuel V2 can calculate actual fuel service duration once `litersToAdd` and a
  credible `fillRateLitersPerSecond` are known.
- Fuel V2 should model fixed-duration service tasks as proven constants, model
  refuel time as a proven per-liter equation, and promote pit-lane travel to a
  durable track/session baseline when repeated clean stops prove it. With those
  pieces, planned stop loss can become a strategy-grade estimate. Pit-lane loss
  may influence the overlay through total stop, rejoin, or strategy-summary
  calculations, but it should not be a standalone row unless that proves useful.
  Actual required/optional repair duration remains the main genuinely dynamic
  service-time input.
- Fuel capacity must be modeled as both physical tank capacity and effective
  event capacity. Physical max fuel is usually car-scoped; effective max fuel is
  car plus series/session rules and must be parsed or inferred before full-tank
  stint and pit-service recommendations become confident. Live fuel analysis
  that depends on capacity must use effective event capacity only; calculating
  against a 75 L physical tank in a 50 L capped session is a product bug.
- Historical pit-service timing does not need capacity to be solved first. It
  should learn from the observed stop shape, requested fuel when available, and
  actual tank delta; capacity/rule metadata is an extra classifier that can help
  identify the active service type or explain differences.
- Requested fuel is useful when it agrees, but it is not required for historical
  pit-service timing. A clean pit entry/stall/service/exit window plus actual
  tank delta is enough to learn what happened.
- Black flags should be modeled as race-control obligations layered on top of
  service timing. A black-flag stop can be a mandatory strategy state, but the
  hold itself should be source-labeled and never merged into normal stop-loss
  baselines. Historical penalty holds explain past stops; live race-control hold
  state is what should drive current strategy updates.
- The user-visible pit-service recommendation should name its source:
  live measured, exact historical baseline, known car baseline, or unavailable.
- Tire advice such as "tires are free" should remain hidden or soft until
  no-tire and tire-service buckets are proven for the relevant car/service
  rules.
- Pit-service timing advice is not a day-one requirement. Fuel V2 can ship useful
  fuel-to-finish and refuel-amount logic before it has enough local evidence to
  say "tires cost N seconds" or "four tires are free." It is acceptable for the
  model to learn over several races before promoting these rows.
- User-facing service timing should progress through explicit states:
  unavailable, learning, observed, corroborated, and proven. Early states can
  show descriptive context such as "learning tire service from recent stops,"
  but strategy-grade rows should wait for a proven or strongly corroborated
  scope match.
- Any "tires cost N seconds" row should describe incremental stop loss relative
  to the selected fuel plan, not blindly add tire time on top of fuel time. Fuel,
  tires, tearoff, and other service can overlap, so the useful number is the
  extra time versus the same stop without that service.
- Exact repair advice should be informational until replay evidence proves how
  required and optional repair interact with refuel and tire service. Fast repair
  can graduate like any other discrete service once isolated and repeated.
- The Pit Service overlay should eventually consume the selected Fuel V2
  service-time model instead of independently inventing a fuel/tire strategy.

Action items:

- Build compact replay windows around the known Dallara fuel-only stops, the
  NASCAR tire-change stop, the 4h GT3 stops, and any available repair,
  meatball, fast-repair, black-flag stop-and-go/hold, or driver-swap windows.
  Treat this as opportunistic evidence, not a complete capture plan. The user
  can intentionally collect a few cars/scenarios, but most proof will come from
  teammates and normal usage encountering services naturally.
- Improve pit-window export so it does not merge long windows across unrelated
  team/nonlocal intervals, and so it preserves service-flag transitions rather
  than only final stop state.
- Add a fill-rate sample classifier with minimum fuel-added threshold, service
  contamination reasons, outlier rejection, and source/confidence labels.
- Add a cohort pit-window classifier that groups same-car/class stops in the
  same race, compares pit-road duration distributions, and records whether the
  local stop aligns with or deviates from the cohort. The classifier should emit
  corroboration/outlier labels only; it should not create strategy-grade service
  facts without matching local proof.
- Add display confidence gates for pit-service advice so new users see safe core
  fuel strategy first, while service timing rows appear only as learning or
  proven evidence supports them.
- Add a service-bucket taxonomy for fuel-only, no-tire service, tire service,
  fuel-plus-tire, fast repair, required/optional repair lower bounds, penalty
  hold, driver swap, tow/reset/garage, and unknown service.
- Add validation fixtures that prove pit entry, pit stall/service activity, pit
  exit, actual fuel delta, computed fuel-service seconds, observed
  `PitstopActive` seconds, tire counter deltas, and repair counter/timer state
  for the same stop. Include requested fuel when available as context, not as a
  required validity gate.
- Make diagnostics export service-evidence summaries that are easy for teammates
  to share: scope key, signal states, clean/rejected samples, rejection reasons,
  isolated timing/rate values, and drift flags. New teammates should be able to
  contribute useful corroboration without manually describing the pit stop.
- Add a telemetry-schema discovery report for unknown-but-plausible service
  signals. It should list fields matching known naming families, observed value
  transitions around pit windows, inferred semantic family, and whether the field
  is diagnostics-only, mapped, or promoted.
- Investigate Overlay Bridge as both a live team-state path and a future
  shared-evidence path. For the active teammate's fuel state, Bridge messages are
  consumed as the real current team-car state at sector/lap cadence, equivalent
  to how the local client consumes its own live fuel state at frame cadence. For
  learned service facts or clean historical sample summaries, Bridge data remains
  source-labeled evidence that helps corroborate fuel fill-rate, tire service,
  pit-lane travel, and overlap behavior faster than isolated local learning.
  Shared proof must keep scope, source, privacy, and conflict handling explicit;
  remote learned samples should usually corroborate local evidence before they
  become strategy-grade advice for a driver.
- Keep the default history store compact and derived. Normal Fuel V2 learning
  should persist service/fuel evidence summaries, scope keys, source labels,
  confidence, and rejection reasons, not raw telemetry frames. Raw captures and
  richer frame-adjacent evidence stay opt-in diagnostic/development artifacts.
- Persist compact race-distance outcome facts, not live projection streams. Useful
  fields include track/config, session/race duration or lap total, series/session
  type, car class or car scope, final overall-leader laps, final class-leader
  laps, final focus/team-car laps, finish source, confidence, and notable state
  flags. These facts can seed future pre-race lap-budget estimates, but they must
  not override live authoritative fields or fresh race-progress evidence.

Shared diagnostic evidence model:

- Default retention boundary: Fuel V2 should store compact derived evidence for
  ordinary history, not full raw telemetry. The normal local store needs enough
  information to reproduce fuel/service decisions and explain confidence, but it
  should not retain frame-by-frame payloads or private local history beyond what
  the model actually uses.
- Race-distance history should stay compact and scoped. Store the final observed
  outcome and enough context to decide whether a future race is comparable; do
  not store every lap-budget recalculation, full progress timeline, or raw
  teammate/local history by default.
- Versioning/migration should reuse the existing durable-data posture instead of
  inventing a separate settings model. User-facing Fuel controls belong in the
  versioned app settings path, while learned fuel/service evidence should follow
  the history pattern: explicit version constants, compatible readers, skipped
  future/unsupported records, rebuilt aggregates where possible, and data
  contract snapshots when the durable schema changes.
- A teammate should be able to share an individual race diagnostic bundle and
  have it strengthen another teammate's model without sending the entire raw
  capture. The bundle should contain compact service-evidence records, not just
  screenshots or prose.
- Sharing can happen through multiple paths. Overlay Bridge is one future live
  path, but offline support bundles are just as important: a teammate can send a
  race diagnostic bundle to us during development/testing, and we can import it
  into the local evidence store here to train or validate the model. Later, the
  same bundle shape could let users import teammate evidence directly to seed or
  strengthen their own local models.
- Live Overlay Bridge teammate fuel state is not just imported learning
  evidence. When a teammate's client publishes valid current fuel, sector burn,
  completed-lap burn, pit-entry/stall/exit, or tank-delta facts for the active
  team car, the receiving strategy model should treat those facts as the current
  state of that stint. The lower update cadence only affects freshness and
  confidence labels; it should not make the receiver infer a different fuel state
  from local history when the teammate has published the value they are actually
  using.
- Overlay Bridge should exchange current/derived fuel facts and provenance, not
  raw telemetry frames or a teammate's private local history. Source labels,
  freshness, session identity, and scope metadata are required so the receiving
  app can distinguish live teammate state, imported teammate evidence, support
  bundle evidence, and local proof.
- Imported teammate evidence should preserve source identity at the evidence
  level: local capture, teammate bundle, Overlay Bridge live share, or imported
  replay. The model should never merge remote samples into local proof without a
  visible source count and scope match.
- Remote evidence can promote `observed` signals and can help move a value to
  `corroborated` when the scope matches and the sample is clean. Promotion to
  `proven` should either require at least one clean local sample or a deliberate
  product decision that multiple independent teammate bundles are enough for a
  low-risk service fact.
- Remote evidence is especially useful for car/rule stationary mechanics:
  fill-rate, tire duration, tearoff/wiper duration, fast repair, service
  overlap/order, and repair lower bounds. It is weaker for local pit-lane travel
  unless the track, pit speed, pit-box position, and entry/exit behavior match.
- Imported bundles should include enough evidence to re-run the classifier:
  scope key, pit-window timings, service flags and transitions, fuel/tire/repair
  counters, unknown schema signal transitions, clean/rejected status, rejection
  reasons, and isolated values. The receiving model should not trust only the
  sender's final label.
- Therefore diagnostic bundles must be evidence-complete, not merely
  explanation-complete. Every exported service-evidence record should include
  the raw facts needed for a receiving build to independently reproduce the
  sender's classification, or to reject it under newer rules. At minimum this
  means:
  - capture/session identity and app/model/schema versions;
  - full scope metadata used by promotion: car identity/version, service/rule
    fields, track/version, pit-speed and pit-box fields, session/race-control
    fields, fuel caps, fast-repair limits, and setup/rule modifiers where
    available;
  - window boundaries for pit road, pit stall, `PitstopActive`, service flags,
    fuel scalar changes, tire counter changes, fast-repair counter changes,
    repair timers, penalty/black-flag state, garage/tow/reset state, and driver
    swap/team-driver state;
  - entry/exit/min/max values for fuel, requested fuel, service flags,
    tire/change requests, tire-set counters, tire compound, repair timers,
    session flags, car/team pit-road state, and unknown candidate service
    fields;
  - raw or sampled transition points for short-lived signals, especially service
    flags that can clear before the summary is written;
  - computed values with their formula inputs: fuel delta, liters requested,
    service seconds, pit-lane seconds, fill-rate candidate, residual service
    time, lower-bound repair time, and overlap/order hypothesis;
  - rejection/degradation reasons with enough detail to distinguish "tiny fuel
    add," "missing service active," "repair contamination," "penalty hold,"
    "garage/tow/reset," "flag disagreement," and "scope mismatch."
- The exported bundle can stay compact; it does not need to include every frame
  of raw telemetry. But it must preserve enough samples around each pit-service
  edge that future classifiers can answer "what changed, when, and under what
  scope?" without asking for the full raw capture.
- Richer diagnostic/support bundles are explicit exports. They can include more
  compact transition evidence than ordinary local history, but should still avoid
  raw frame dumps unless the user deliberately enabled raw capture or a
  development workflow requires it.
- Conflicts should degrade or flag confidence rather than silently average. If a
  teammate bundle says Dallara fuel rate is `1.7 L/s` and another clean matching
  bundle says `1.1 L/s`, the model should surface variance/drift and keep overlay
  advice conservative until the difference is explained by scope, sample
  contamination, fuel quantity, service overlap, or iRacing behavior change.

Future diagnostic export shape:

```text
diagnosticBundle
  bundleVersion
  appVersion
  captureId
  sessionScope
  telemetrySchemaSummary
  pitServiceEvidence[]
  learnedServiceFacts[]
  unknownCandidateSignals[]
  importSafety

pitServiceEvidence[]
  scopeKey
  window
  rawSignals
  transitions
  computedCandidates
  classification
  source
  sharePolicy

learnedServiceFacts[]
  signal
  state
  scopeKey
  sampleCount
  localSampleCount
  remoteSampleCount
  mean
  variance
  lastObserved
  driftState
  adviceAllowed
```

This is a planning shape, not an implementation commitment. The important
contract is that an exported bundle must carry enough raw signal evidence and
scope metadata for another model to independently classify the samples, not only
consume the sender's final learned facts.

### Fuel Tab Diagnostics Presentation

The Fuel tab should be the explanation and evidence surface for Fuel V2. The
driving overlay should remain conservative and action-focused; the tab can show
why advice is available, why it is withheld, what has been learned, and what
evidence changed.

Primary Fuel tab groups:

- `Strategy controls`: user-editable fuel margin, plus any session-specific
  strategy inputs that affect Fuel V2 advice. The tab should show the configured
  lap margin and its current fuel equivalent so the driver can see what is their
  preference versus what the model is estimating. Do not add advice-depth
  controls to initial V2; keep the default posture conservative and use teammate
  testing to decide whether that setting is worth exposing.
- `Advice availability`: one compact summary of what the overlay is currently
  allowed to use. It should name blocked rows and the reason, such as "tire time
  hidden: only observed, needs corroborated/proven evidence" or "pit-lane loss
  estimated: track/box baseline not proven."
- `Learned service facts`: table of promoted facts by signal and scope. Columns
  should include signal, state, value, variance/range, sample count,
  local/imported counts, scope match, last observed, drift state, and whether
  overlay advice is allowed.
- `Current session evidence`: last pit window and current session samples, with
  clean/rejected status and source-labeled computed candidates. This is where an
  observed-but-not-actionable signal can be visible without becoming overlay
  advice.
- `Imported evidence`: teammate/support/Overlay Bridge samples, source counts,
  scope matches, and conflicts. Imported samples should be visibly separate from
  local proof even when they contribute to corroboration.
- `Drift and conflicts`: previously proven values that no longer match recent
  clean samples, plus unresolved disagreements between local and imported
  evidence. This should explain whether the overlay is using the old proven
  value, the conservative side of a range, or withholding advice.
- `Unknown candidate signals`: schema-discovered `PitSv*`, `dp*`, `dc*`, or
  `CarIdx*` fields that behaved like service signals but are not mapped yet.
  These should show pattern confidence and transitions, not strategy advice.
- `Rejected samples`: pit windows that were intentionally not used, with
  rejection reasons such as tiny fuel add, missing service active, penalty hold,
  repair contamination, tow/reset/garage, flag disagreement, or scope mismatch.

Advice availability should be explicit but restrained. Candidate tab copy:

```text
Fuel add timing: available from proven Dallara P217 fill-rate.
Tire timing: hidden from overlay; one observed tire signal, no isolated timing.
Pit-lane loss: session estimate only; pit box baseline not proven.
Repair timing: diagnostics only; no clean repair lower-bound sample.
Imported evidence: 2 matching teammate samples strengthen fill-rate confidence.
```

The overlay should not repeat all of this detail. It should show only the
current actionable result, source/confidence where space allows, and edge-state
warnings when the advice is degraded. Examples:

```text
Add 24L: ~14s fuel service
Four tires: +8s vs this fuel plan
Pit now exits traffic
```

If a row is blocked, the overlay should usually omit it rather than display a
diagnostic explanation. The Fuel tab owns the "why not" details.

Drift presentation:

- A proven fact with one surprising clean sample should become visible in the
  tab immediately, but overlay advice should stay conservative.
- A proven fact with repeated matching drift should move toward a new proven
  value, preserving the old value and version/scope history.
- Drift copy should name the measured change and source: "Fuel fill-rate changed
  from `1.70 L/s` to `1.45 L/s` across two clean matching samples after car
  version `X`; overlay using conservative range."

Imported evidence presentation:

- Imported samples should show source type and sample counts, not personal
  teammate identity by default.
- The tab should distinguish `local`, `imported support bundle`, `imported
  teammate bundle`, `Overlay Bridge`, and `raw replay/imported capture`.
- A fact can show both model state and source posture, for example:
  `corroborated, 1 local + 2 imported`, or `proven local, imported conflict`.
- Users should be able to export the compact evidence bundle for a race without
  exporting the entire raw capture, and the receiving model should be able to
  re-run classification from the bundle evidence.

The tab should keep the core distinction visible: learned facts are evidence,
not automatically advice. Overlay advice requires a matching scope, a permitted
state, no active drift/conflict, and a service shape that the model knows how to
apply to the current strategy.

### Overlay Advice Presentation Contract

The Fuel overlay should show only compact actionable rows for the current
session and selected strategy. It should not become a diagnostics surface. If a
service row is not advice-ready, the default behavior is to omit the row and let
the Fuel tab explain why.

Pit Service overlay relationship:

- The Pit Service overlay should be the live request/validation surface for the
  same normalized next-stop request that Fuel V2 uses. It already belongs with
  local pit-service telemetry and should help prove whether Fuel V2 is reading
  current pit selections correctly.
- Fuel V2 should own strategy math: fuel-to-finish, target fuel, planned add
  amount, service-time estimates, and whether the current request is strategy
  safe. Pit Service should show the selected/requested service state and can
  surface Fuel V2's selected recommendation through the shared request model. It
  should not independently recalculate laps-to-go or refuel advice.
- `nextPitRequest` is required shared infrastructure for V2. It should be built
  from `LiveFuelPitModel`, `LivePitServiceModel`, race-control state, repair
  state, and learned service facts. Fuel and Pit Service should both consume that
  model so changing tires, fuel, tearoff, fast repair, or repair state updates
  both surfaces in the same frame.
- Pit Service is useful for diagnostics because it can show what the app thinks
  the user selected right now. If Fuel overlay advice is missing or stale, the
  first debugging question should be whether Pit Service shows the same current
  request that iRacing shows in the black box.
- Pit Service should stay read-only for V1-style overlays. A future pit
  crew/engineer surface can own command-capable controls, but read-only telemetry
  validation and simulator command actions should not be mixed accidentally.

Pit-service timing rows apply to the next planned pit stop only. They should not
be repeated across every stint row or shown as a generic race-long service fact.
The overlay can use learned facts to calculate the next stop's expected fuel
time, tire delta, pit-lane loss, fast repair, repair lower bound, or penalty
hold, but the displayed value must be tied to the current pit request and target
fuel plan.

Strategy-facing service timing should be incremental against the current
selected stop. If fuel-only is expected to take `14s` and fuel plus four tires is
expected to take `39s`, the tire strategy row should show `+25s versus fuel`, not
`39s tires`. Raw component durations can appear in diagnostics, and a clearly
labeled total stop estimate can show total expected service/stop time, but
decision rows should answer "what changes if I select this service?"

The next-stop rows must update whenever the user changes pit selections or fuel
targets. Relevant live changes include requested fuel quantity/autofill, tire
corner selections, tire compound/pressure where applicable, tearoff/wiper
request, fast repair, driver swap/team service state if exposed, repair state,
and race-control penalty/black-flag state. A stale service row is worse than a
hidden row; if the selected service shape cannot be recomputed confidently, the
row should degrade or disappear until the model has a valid current request.

State-to-overlay policy:

| Evidence state | Overlay behavior | Notes |
| --- | --- | --- |
| `unavailable` | Hide the row. | No explanatory placeholder in the driving overlay. |
| `learning` | Hide the row. | Learning belongs in the Fuel tab, not live advice. |
| `observed` | Hide the row. | The signal exists, but no isolated timing/rate can be used. |
| `corroborated` | May show a soft estimate for low/medium-impact rows when scope matches and no conflict exists. Label as estimated. | Example: "Fuel service est. ~14s." High-impact rows can still require `proven`. |
| `proven` | May show strategy-grade advice when scope matches and the service shape is understood. | Example: "Four tires: +8s vs fuel." |
| `proven_with_drift` | Use conservative range or hide, depending on impact. Mark degraded if shown. | Example: "Fuel service: 14-17s est." |
| imported-only corroboration | Usually hide or label as estimated unless at least one local clean sample agrees. | Remote evidence can strengthen confidence but should not silently look local. |
| conflict / scope mismatch | Hide high-impact rows; optionally show a conservative degraded row for low-impact estimates. | Fuel tab owns conflict explanation. |

Overlay row eligibility:

- `Fuel to finish` remains the primary row and can use live/historical fuel
  burn policy separately from pit-service timing. If the fuel burn source is
  degraded, the overlay can show a conservative warning rather than hiding the
  core fuel row.
- `Fuel add amount` can show when race-lap budget, target fuel, tank state,
  user margin, and effective session fuel cap are valid. This does not require
  pit-service timing proof.
- `Fuel service time` can show when planned liters-to-add and a matching
  corroborated/proven fill-rate exist. If only corroborated, label as estimated.
- `Tire time loss` can show only when tire service and service overlap/order are
  proven or deliberately allowed as a corroborated soft estimate. The value must
  be incremental stop loss relative to the selected fuel plan, not raw tire
  duration.
- `Pit-lane loss` does not need a standalone driving-overlay row in V2. When the
  track/box/pit-speed scope is strong, it can influence total stop, projected
  rejoin, `pit now vs later`, and `Strategy summary` values. If a standalone row
  is added later, require strong scoped evidence and hide/degrade it when
  `DriverPitTrkPct`, pit-speed, or entry/exit behavior does not match the
  baseline.
- `Fast repair` can show once isolated and proven/corroborated like any other
  discrete service. Before then, hide timing advice.
- `Required/optional repair` should not show fixed pre-stop timing advice. If
  live repair timers/status are present while in pit lane, Fuel V2 can use that
  known waiting time to update strategy. Historical lower-bound repair context can
  stay in diagnostics or conservative source text, but should not look like a
  solved service constant.
- `Penalty hold` should show only when the hold is known from race-control state
  or a proven detected hold, and must remain separate from service timing. It is
  an obligation/risk row, not strategy optimization.
- `Fuel to box risk` should show only as a low-fuel-to-stall status when current
  fuel and pit-entry/stall context indicate the car may not reach the assigned
  box. It is separate from normal race-burn strategy and should not promote
  pit-lane burn into clean lap history.
- `Unknown candidate signals` never show in the overlay.

Recommended overlay copy style:

```text
Add 24L
Fuel service: ~14s
Four tires: +8s vs fuel
Pit now exits traffic
Low fuel to stall
Repair: at least ~90s
```

Avoid overlay copy such as:

```text
Learning tire timing from recent stops
Observed tire service signal
Imported teammate evidence disagrees with local sample
Tires take 39s
```

Those statements are true diagnostics, but they are not live driving advice. The
overlay should show the decision-relevant value only when it clears the advice
gate; otherwise it should stay quiet and let the Fuel tab carry the explanation.

### Pit Now Versus Later Strategy Value

`Pit now vs later costs X but saves Y on track` is a higher strategy layer than
pit-service timing. The service model can provide the stop-loss side of the
equation, but the on-track value needs tire performance, fuel weight, traffic,
stint length, lap-budget confidence, and whether the stop is optional or
unavoidable.

Decisions to lock for Fuel V2:

- Treat `pit now vs later` as a complete plan comparison, not a direct command.
  The default UI language should be scenario-based: "pit now projects +3s
  slower," "pit now likely rejoins in traffic," or "pit now avoids an unsafe
  fuel window." Do not say "pit now" as an instruction unless every safety and
  strategy input clears a deliberately high confidence gate.
- The first useful version should answer the next-stop question: "if I pit now
  with the current selected service request, what is the expected stop loss and
  rejoin consequence?" It should not try to optimize every future stint before
  the stop-loss and rejoin model is reliable.
- Compare `pit now` against a specific later plan, not a vague "later." The
  primary comparison should usually be the current stint plan, because it is the
  user's active strategy context. Additional scenarios can compare against an
  early stop, "pit in N laps," or "pit on the last safe lap." The displayed
  delta must name which comparison is being used.
- Separate obligation from optimization. Low-fuel, black-flag, required repair,
  or pit-window-closing cases are mandatory/risk states; the row can explain the
  cost of satisfying the obligation, but it should not present the obligation as
  optional tire or undercut strategy.
- The row must consume the same `nextPitRequest` used by Fuel and Pit Service.
  Changing selected fuel, tires, tearoff/wiper, fast repair, repair state, or
  penalty state must recompute the comparison immediately or hide/degrade it.
- Tire-payback strategy is not a day-one row. It requires credible stop-loss
  evidence plus tire-age/performance evidence, including warmup/outlap, traffic,
  fuel-load, and condition context. Until then, the app can show service timing
  and rejoin/traffic scenarios without claiming fresh tires will pay back.
- Projected rejoin and traffic may be the earliest valuable version. If stop
  time and field positions are credible, "pit now exits into traffic" can be a
  useful soft warning even before the app knows tire degradation well enough to
  calculate a full undercut/overcut recommendation.
- Rejoin/traffic output should start as information, not advice. It can say
  "clear by 3.2s", "rejoin 1.8s behind #24", or "pit now exits into GT traffic"
  when the projection is strong enough, but it should not yet say "stay out" or
  "box now to avoid traffic" as a strategy command. A future version can compare
  box-now versus stay-out traffic scenarios once the model knows how to value
  traffic loss, clean-air gain, and alternative pit windows without false
  precision.
- For overlay advice, prefer omission over weak math. The Fuel tab can show the
  scenario inputs and why the row is blocked; the overlay should show only a
  compact result when the current comparison clears the advice gate.
- Present this like a normal race engineer: show the high-confidence information
  the driver needs now, then add richer scenarios as the model gains confidence.
  The first row might only say "pit now rejoins traffic" or "pit now costs +42s
  versus current stint plan." Later, with stronger tire/traffic/history data,
  the same area can add "early stop may recover ~6s over 8 laps" or "four tires
  likely pays back by lap 5."

Plan comparison should compare whole candidate plans, not isolated stop rows:

```text
planTotalTime =
    projectedOnTrackSeconds
    + plannedStopLossSeconds
    + penaltyHoldSeconds
    + riskReserveSeconds
```

Then:

```text
pitNowNetSeconds =
    planTotalTime(pit-later-plan) - planTotalTime(pit-now-plan)
```

Positive values mean the pit-now plan is projected faster. Negative values mean
staying out is projected faster. This should be displayed as a scenario until
the inputs are proven enough for strategy-grade advice.

Important distinctions:

- If the stop is mandatory or unavoidable, pitting now usually moves stop loss
  earlier; it does not necessarily add a full extra stop. The comparison is
  undercut/overcut timing, tire state, traffic, and fuel quantity, not simply
  `minus one pit stop`.
- If pitting now creates an extra stop, the full extra stop loss matters and
  should be compared against on-track time saved.
- If pitting now changes service selection, such as two tires versus no tires,
  the cost side should use incremental service loss relative to the selected
  fuel plan.
- If pitting now avoids a later splash, avoids unsafe fuel, clears damage, or
  satisfies a black-flag obligation, the comparison must source-label that
  obligation instead of presenting it as tire strategy.

Candidate cost inputs:

- proven pit-lane travel/loss baseline for the track/session/box;
- stationary service time from fixed service constants and fuel-per-liter
  equation;
- incremental tire/tearoff/setup service cost relative to the fuel plan;
- outlap and inlap deltas when replay evidence proves they differ from normal
  laps;
- penalty hold or repair time, source-labeled and excluded from normal
  baselines.

Candidate on-track savings inputs:

- tire age/degradation: expected lap-time loss from staying out on the current
  tire set versus pitting for fresh tires;
- tire warmup/outlap: time lost before new tires reach normal pace;
- fuel weight: V3 expected lap-time gain/loss from different fuel loads, if
  proven for the car;
- traffic and clean-air: V3 likely time lost staying in traffic or gained by
  undercutting into clean air;
- lap-budget/stint length: how many laps remain for the benefit to pay back;
- risk state: low fuel, damage, required repair, black flag, or pit-window
  closing.

Projected rejoin and traffic:

Once Fuel V2 has a rough planned stop time, it can estimate where the user will
rejoin and whether that point is traffic-heavy. This could power a future Track
Map `Pit` ghost marker, similar to overlays that show a projected pit-out dot.

The basic model:

```text
pitExitTime =
    now
    + timeToPitEntrySeconds
    + pitLaneEntryToBoxSeconds
    + stationaryServiceSeconds
    + boxToPitExitSeconds

carProgressAtPitExit =
    carCurrentProgress + elapsedSecondsUntilPitExit / carProjectedLapSeconds
```

For the strategy car, the visible projected marker should represent the expected
pit-exit or merge location for the planned stop. For traffic, project nearby
field cars to the same `pitExitTime`, then compare gaps around the rejoin point.

Useful outputs:

- "pit out into clean air";
- "pit now clear by 3.2s";
- "rejoin P12, 1.8s behind #24";
- "pit now exits into GT traffic";
- Track Map `Pit` ghost dot at the projected rejoin point, with confidence or
  risk color.

Required inputs:

- planned stop loss from the service-time model;
- time to reach pit entry if the user pits this lap;
- pit-entry, pit-box, pit-exit, and merge location evidence from pit-lane
  windows, `DriverPitTrkPct`, `LapDistPct`, and generated track-map pit-lane
  geometry when available;
- live field progress from `CarIdxLapCompleted` / `CarIdxLapDistPct` and scoring
  rows;
- pace estimates for nearby cars, preferably recent clean pace by car/class;
- competitor pit-road state, because cars already on pit road or likely to pit
  can make a rejoin projection unstable.

Confidence rules:

- High confidence needs proven stop timing, valid generated track/pit geometry or
  stable pit-entry/exit percentages, fresh field positions, and usable recent
  pace for the cars near the projected rejoin.
- Medium confidence can still show a soft `Pit` marker from stop-loss and
  live `LapDistPct`, but should label missing pit geometry, weak opponent pace,
  or uncertain service time.
- Low confidence should avoid exact rejoin claims. It can say "traffic unknown"
  or "pit-out projection unavailable" rather than inventing a precise gap.
- Cautions, active pit cycles, multiclass speed deltas, cars on pit road, missing
  field rows, black flags, repairs, and lap-down ambiguity should widen the
  traffic risk band.

Product posture: this may be more valuable earlier than tire-payback advice once
stop-loss is known. Even a rough "you will rejoin in traffic" warning can change
strategy, but it should remain source-labeled and avoid exact position or gap
claims until the field projection is strong. The first version should answer
"where do I come out?" rather than "should I box or stay out?"

Simple break-even framing is still useful when the input scope is narrow:

```text
breakEvenLaps = incrementalStopLossSeconds / expectedFreshTireGainSecondsPerLap
netTireValueSeconds =
    expectedFreshTireGainSecondsPerLap * usefulLapsRemaining
    - incrementalStopLossSeconds
```

But this should remain a simplified explanation, not the full decision engine.
The full engine should compare complete plans because tire gain is rarely flat:
new tires may have warmup loss, gain may change with tire age, traffic can erase
or amplify the benefit, and the race may end before the benefit pays back.

Product posture:

- Day-one Fuel V2 does not need this row. It should appear only after the app has
  enough evidence for both stop loss and on-track performance deltas.
- Fuel-weight and clean-air valuation are V3. V2 should store the context for
  future analysis, but it should not claim a lighter fuel load or clean-air gain
  changes the strategy recommendation.
- The first version should be a soft scenario row, such as "Possible: four tires
  cost +8s but may save ~11s over 15 laps." It must show source/confidence and
  avoid commands unless the evidence is strong.
- Hide tire-payback from the driving overlay until it clears a deliberately high
  confidence gate. The Fuel tab can show an experimental/explaining state, but
  the overlay should not say fresh tires pay back by lap `N` from weak history or
  incomplete stop-loss data.
- The row should be more willing to say "not enough tire history yet" than to
  invent a tire-delta model from weak data.
- Historical baselines should store tire-age, stint-lap, fuel-load, traffic, and
  temperature context where available, because those are what turn tire timing
  into a real strategy estimate.

Action items:

- Add candidate-plan fixtures for `pit now`, `pit in N laps`, `take tires`, and
  `fuel only`, using the same lap-budget and service-time inputs.
- Add projected-rejoin fixtures that compare planned stop time against
  `CarIdxLapDistPct` field positions and expected pit-out traffic for clean-air,
  traffic, and unknown cases.
- Build tire-age lap-time baselines per car/track/session/rule context before
  showing strategy-grade tire payback rows.
- Add validation examples where pitting now is faster, slower, and unknown due
  to weak tire or traffic evidence.
- Decide how conservative the scenario row should be when the race-lap budget or
  tire-degradation model crosses a payoff boundary.

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

Overlay product direction:

- Fuel V2 should consider a visible sector-adjusted lap-burn cell next to the
  normal lap-burn cell. The normal cell shows completed/accepted lap burn; the
  sector-adjusted cell shows the current lap projected from completed sectors so
  the driver can react before the lap is over.
- This is especially useful when the driver is trying to hit a fuel target. If
  the target is `15 L/lap` and sector-adjusted projection after sector 1 is
  `16 L/lap`, that probably deserves highlighting immediately rather than
  waiting for the completed lap.
- The sector-adjusted cell is live guidance, not historical truth. It should not
  rewrite the normal baseline until the completed lap confirms it.
- Single-sector evidence can be shown when the sector is long enough and
  confidence is labeled; short/noisy sectors may require cumulative sectors or a
  muted/low-confidence state.
- The row/cell should compare against the active target or rolling baseline and
  use wording like `sector-adjusted`, `projected lap`, or `live lap burn` so it
  is not confused with completed lap burn.

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

Privacy/default-sharing decision: Bridge is not raw telemetry sync and should
not publish private local history by default. For fuel, it should send only the
current/derived facts needed for team strategy, plus provenance, freshness, scope,
and confidence labels. Diagnostics/support bundles can carry richer compact
evidence when the user explicitly exports them.

The iRacing black box may still show a laps-left fuel estimate while a teammate
is driving, but the 4-hour team capture did not expose that estimate or direct
teammate fuel level through the SDK schema. Treat it as hidden simulator UI state
until a capture proves a public field exists.

Live teammate fuel state decision: when Bridge is enabled and a teammate's client
publishes the fuel state they are actually using, Fuel V2 should consume it like
local live fuel state for that teammate's stint. The update cadence can be
sector, lap, pit event, or heartbeat rather than frame-by-frame, so the receiver
should show freshness/confidence when useful, but it should treat the published
current fuel and burn facts as the true team-car state until newer valid data or
an explicit invalidation arrives.

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
fuel level normalized value + unit
sector fuel delta normalized value + unit, if measured
normalized fuel per lap value + unit, if measured
instantaneous-flow integral normalized value + unit, if computed
sample confidence and rejection reasons
pit/service context
source timestamp
```

Bridge consumers should treat sector messages as live state facts, not commands.
For teammate/endurance planning, valid Bridge fuel state can update the same
strategy rows that local live telemetry would update. For learned baselines and
service-proof promotion, the same messages remain source-labeled evidence and
should follow the normal Fuel V2 confidence hierarchy; sector deltas can update
teammate fuel trend and planning rows quickly, while high-impact changes such as
stop deletion, underfueling, or pit-service refuel advice should require
completed-lap agreement, a near-finish high-confidence state, or explicit
current-stint proof.

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
