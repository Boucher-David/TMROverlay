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
