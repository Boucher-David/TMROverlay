# 24-Hour Live Overlay Findings

> Historical evidence note: this file captures observed behavior from one
> 24-hour race and maps it against the May 2026 raw-capture/IBT analysis. Keep
> it as product/data-model evidence, not as current overlay behavior or an open
> branch checklist.

These notes capture live-overlay behavior observed during a 24-hour race and map it against the May 2026 raw-capture/IBT analysis. Treat this as product and data-model evidence, not as a statement that the current overlays already behave this way.

## Summary

The live race reinforced the same model-v2 direction as the raw data pass:

- overlay logic needs session semantics, not just reusable numeric channels
- focus/team/local-driver data must carry explicit source quality
- fuel strategy needs a holistic team-stint model, not scalar live fuel stitched across driver changes
- gap and radar overlays should consume normalized timing/proximity evidence rather than each deciding source quality locally

## Findings

| Area | Observed behavior | Match against data findings | Model-v2 implication |
| --- | --- | --- | --- |
| Gap graph outside race sessions | Practice, qualifying, and test sessions made the gap graph misleading because gap meaning follows fastest-lap or timing-board behavior rather than race position. | Matches the source-quality issue: `CarIdxF2Time` can be valid timing but semantically wrong for a race-gap graph in non-race sessions. | Timing rows need session-purpose semantics. Race-gap overlays should default to race sessions or show a different metric/mode outside races. |
| Multi-lap leader gap scaling | When the leader was multiple laps ahead, small fights near the user's position became unreadable because an 8-minute lap expanded the Y-axis too much. | Matches the capture finding that class gaps can jump and grow very large while local battles remain important. | Gap graph should support local delta views, lap-normalized views, segmented axes, or separate leader-gap and local-battle panels. |
| Radar focus handling | Radar did not handle focused cars well and struggled when the user's team car was driven by a teammate. | Matches the raw/IBT split: live `CarIdx*` arrays are needed for focus/radar, while scalar player-only signals like `CarLeftRight` do not apply to arbitrary focus cars. | The first production/model-v2 radar path is now local in-car only. Non-local focus, teammate/spectator focus, and suppressed side signals should be collected as diagnostics for a later advanced focus-relative branch. |
| Fuel context stitching | Fuel calculator stitched contexts when a teammate got in the car instead of combining local fuel evidence with teammate stint-length evidence. | Matches the long-capture finding: local scalar fuel can disappear or stop representing the active team stint, while team progress remains useful. | Fuel needs a team strategy model with local measured fuel windows, teammate stint lengths, pit/service history, and explicit modeled-vs-measured labels. |
| Impossible stint suggestions | Fuel calculator suggested impossible 5-lap time-saving options even though the team ran 7-lap stints. | Matches the concern that instantaneous burn and incomplete context can whipsaw strategy. | Strategy suggestions need stronger feasibility constraints from max fuel, historical completed stints, current stint phase, and live/session pit context before surfacing "time-saving" claims. |
| Gap threat/leader section | Threat and leader text did not handle pit cycles and on-track pace differences well, and it did not add much value. | Matches the research direction that mature overlays separate standings/relative/radar/strategy concerns. | Move threat/leader interpretation toward dedicated relative, standings, or strategy overlays rather than keeping it embedded in the class-gap trend. |
| Gap graph update cadence | The graph probably needs to update more often than lap completion, and it is worth testing whether iRacing exposes live position changes before lap completion. | The collector already reads `CarIdxPosition` and `CarIdxClassPosition` each frame, and the raw/offline analysis plus live overlay diagnostics now count sampled intra-lap position/class-position changes. The same diagnostics now measure lap-delta channel availability and sector-boundary intervals for timing-table candidates. | Use the new cadence, lap-delta, and sector-timing evidence across several sessions before treating continuous standings/relative/sector updates as a model-v2 guarantee. |
| Faster-class warning | Radar faster-class warning did not work well, likely due to the same focus/team-driver source problems as radar placement. | Matches current evidence: side occupancy is local-player scalar, while focus-relative placement depends on valid focused `CarIdx*` timing/progress. | Keep collecting local/degraded examples, but do not make it the center of the simple local radar design. |

## Recommended Follow-Up

1. Use `live-overlay-diagnostics.json` and `tools/analysis/analyze_capture_assumptions.py` outputs to gather more intra-lap position/class-position, lap-delta, and sector-timing evidence.
2. Keep the current gap overlay race-only by default; design separate non-race timing modes before enabling it for practice/qualifying/test.
3. Split gap graph display modes into leader-gap context and local battle readability.
4. Keep production radar local in-car first, and use diagnostics to gather suppressed non-local focus, local progress-missing, pit/garage, side-without-placement, and multiclass examples for review.
5. Rework fuel strategy around team stint history and measured-local fuel windows before trusting live strategy recommendations in endurance races.
