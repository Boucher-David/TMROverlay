# iRacing 2026 Release-Note Audit

Reviewed 2026-07-18 against the current [iRacing release-note index](https://support.iracing.com/support/solutions/31000076778), the `irsdkSharp` ingestion path, raw-capture schema, replay reader, and diagnostics contracts.

## Dynamic `CarIdx` arrays — implemented

Season 3 Patch 1 added the `[Misc] irsdkLogAllCars=1` option and allows the `CarIdxXXX` SDK arrays to grow with the actual entry-table size. The application must not retain the former 64-entry assumption.

- Live collection and raw replay derive their `CarIdx` loop bounds from the current SDK or captured schema across shared timing arrays.
- The raw format already retains each variable's actual `Count` in `telemetry-schema.json` and its complete buffer in `telemetry.bin`; no capture-format or durable-history version change is required.
- Core local-context/radar/history and diagnostics accept normalized, non-negative `CarIdx` values rather than applying a second hard-coded cap.
- The redacted availability corpus records a real Windows Acura capture with 27 `CarIdx*` arrays of 72 slots. Replay regression coverage populates the final valid index (71).

TmrOverlay does not write the user's iRacing `app.ini`. Users who need all active-entry rows must opt into `[Misc] irsdkLogAllCars=1`; the app captures and honors whichever schema it receives.

## Follow-up evidence

The existing 72-slot capture proves schema capacity, not populated high-index opponents. Collect an `irsdkLogAllCars=1` race with more than 64 active entrants and replay it through timing, relative, standings, radar, and diagnostics before claiming end-to-end high-index live evidence.

## Overlay Bridge normalized-fuel preflight — 2026-07-18

Reviewed the current Season 3 Patch 3 / Hotfix 1 notes before promoting the Bridge projector's normalized fuel inputs. There is no new raw SDK fuel/capacity channel or Bridge-relevant telemetry schema change in those patches. Patch 3 does fix late-join team/driver lap-count reporting for driver-swap sessions; that reinforces, rather than removes, the need for session/lease provenance, immediate handoff invalidation, and real late-join/driver-swap replay coverage. Its `CamCarIdx` pace-car correction is not a projector dependency: publisher eligibility requires local confirmed in-car evidence and the projector does not use camera focus.

The 2026 Season 3 release adds series-specific regulations and changes pit/fuel behavior for some cars. The current normalized `LiveFuelPitModel` therefore promotes only capture/context-proven physical tank capacity and density (`DriverCarFuelMaxLiters`, `DriverCarFuelKgPerLiter`) plus clean-burn evidence. It deliberately leaves effective session capacity and maximum allowed fuel percentage unknown until the applicable ruleset/session fields have paired capture evidence; it must not treat the physical tank as a session restriction.

The tracked SDK availability corpus still records `FuelLevel`, `FuelLevelPct`, and `FuelUsePerHour`. The Bridge projector consumes no raw SDK frame: it reads those values only after the existing collector has normalized them into Model V2, and it publishes only a bounded completed-green-lap burn window. No capture-format, raw-capture compatibility, or durable user-data schema change is required by this promotion. Required next evidence is a real team driver-swap/late-join capture covering tank/density availability, session fuel restrictions when present, completed clean burns, pit/service, and the outgoing/incoming publisher transition.
