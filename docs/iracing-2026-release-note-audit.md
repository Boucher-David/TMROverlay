# iRacing 2026 Release-Note Audit

Reviewed 2026-07-17 against the [iRacing 2026 release-note index](https://support.iracing.com/support/solutions/31000076778), the current `irsdkSharp` ingestion path, raw-capture schema, replay reader, and Fuel V2 contracts. The current index still ends at Season 3 Patch 3 Hotfix 1 (2026-07-14), so there is no newer SDK/session-data release-note impact to add for this follow-up.

## Required changes found

### Dynamic `CarIdx` arrays — implemented in this branch

Season 3 Patch 1 added the `[Misc] irsdkLogAllCars=1` option and allows the `CarIdxXXX` SDK arrays to grow to the actual entry-table size. The application must not retain the former 64-entry assumption.

- Live collection and raw replay now derive their `CarIdx` loop bound from the current SDK/captured schema across the shared core timing arrays.
- The raw format was already forward-safe: `telemetry-schema.json` retains each variable's actual `Count`, and `telemetry.bin` retains the complete buffer. This does not require a capture-format or durable-history version bump.
- Core local-context/radar/history and diagnostics accept normalized, non-negative `CarIdx` values rather than applying a second hard-coded cap.
- Browser-review mirrors that rule. The real Windows Acura capture
  `capture-20260714-193157-308` exposes all 27 `CarIdx*` arrays with 72 slots;
  the redacted availability corpus retains that source shape, and replay
  regression coverage populates player/focus at its last valid index (71).

TmrOverlay does **not** write the user's iRacing `app.ini`. To collect all active-entry rows, users who need that evidence must opt into `[Misc] irsdkLogAllCars=1` in iRacing. The app still captures the actual schema it receives, whether or not that option is enabled.

### Pit-service rules — captured but deliberately not inferred

Season 3 describes series-specific fuel/tire-service arrangements. Format-5 Fuel V2 capture already retains `WeekendInfo.DCRuleSet` in raw session scope and immutable history. That field is useful provenance, but it is not a verified machine-readable service-order contract. The current reader therefore keeps every value as `Unknown` for sequential/parallel timing and cannot unlock tire duration, overlap, or strategy advice from it alone.

### Pace-car camera identity — hardened in this branch

[Season 3 Patch 3](https://support.iracing.com/support/solutions/articles/31000179134-2026-season-3-patch-3-release-notes-2026-07-10-04-)
notes a `CamCarIdx` correction while watching the pace car. Raw camera identity
is therefore not a general local-driver signal. The factual Fuel V2 fallback
accepts it only when fresh telemetry exactly matches the session-declared
`DriverInfo.DriverCarIdx`, that driver row explicitly says `IsSpectator=false`,
and no resolved player/focus identity conflicts. It remains a Fuel-State-only
display fallback; strategy, burn, range, history, and every other local overlay
continue to require normal local focus/progress.

## 2026 release-note disposition

| Release group | Impact | Disposition |
| --- | --- | --- |
| Season 1 initial, Patch 1, Patch 1 Hotfix | Car/track/tire/fuel physics and configuration updates | Live telemetry remains primary; exact layout identity intentionally splits renamed configurations rather than merging history. |
| Season 1 Patch 2 | Added `IncidentWarningInitialLimit` and `IncidentWarningSubsequentLimit` session values | Raw `session-info/` capture retains them. Add named parsing only with a future incident/rules surface; no Fuel V2 dependency. |
| Season 2 initial through Patch 4 Hotfix | Fuel-economy/BoP changes, pit UI, reconnect fixes | Fresh effective capacity and live clean burn outrank history; reconnect lineage/deduplication remains applicable. No published SDK schema addition requiring a V2 change. |
| Season 3 initial | Series-specific service arrangements and pit-rule changes | Preserve `DCRuleSet` only as raw provenance; no inferred timing model. |
| Season 3 Patch 1 | Dynamic all-car `CarIdx` arrays | Implemented above. |
| Season 3 Patch 2 and Patch 3 | Pit-speed/rules behaviour, multi-pace starts, driver-swap/lap-count and pace-car camera fixes, corrected GT3 telemetry values | Existing raw/session capture preserves the evidence. Patch 3 now has a narrow verified session-driver/raw-camera Fuel State fallback; camera identity alone remains untrusted. Future race-control/pit-route work should use new real captures; no named SDK field change was published. |
| Season 3 Patch 3 Hotfix 1 | Acura NSX GT3 EVO 22 loading repair | No SDK, session-data, raw-capture, or overlay-contract impact. |

## Follow-up evidence to collect

- The July 14 Acura and Mercedes Windows captures establish the 72-slot schema,
  but the Acura capture has one driver and therefore cannot prove populated
  high-index opponent rows. Collect an `irsdkLogAllCars=1` session with more
  than 64 active entrants, then replay it through timing, relative, standings,
  radar, and diagnostics.
- A current Season 3 pit-service capture that records `DCRuleSet` alongside observed stationary-service counters. It may validate a future service-rules contract, but must not be converted into timing advice by label alone.
- A multi-pace-start/late-driver-swap capture to validate the existing race-control and team-stint provenance against the fixed SDK behaviour.

The offline capture-analysis and fixture-export scripts follow the same schema-driven bound, so newly captured large fields can be inspected and turned into replay fixtures without silently dropping high-index cars.
