# Fuel V2 Strategy Stress Fixtures

`manifest.json` is the single catalogue for Fuel Calculator V2 stress cases.
It is intentionally broader than a normal test-fixture folder: Fuel needs the
same cases at race start, after live evidence changes, through pit service, and
in long-race interruptions.

Every case states its provenance and expected semantic result. Use it in three
ways:

- Core tests should consume `direct-core` cases or construct an equivalent
  normalized input. Those tests assert the actual selection, route, checkpoint,
  lifecycle, or scheduler contract.
- The V2 workbench should render the same normalized input with its provenance
  visible, so a captured fact, a history reference, and a constructed pressure
  test cannot be confused.
- Once the final V2 overlay owns a row, promote the appropriate case to
  deterministic browser-review, localhost/OBS, and Windows-native evidence.
  Hidden, unavailable, and degraded results are expected visual states.

Do not import a `capture-history-reference` or `archive-service-reference` into
`FuelV2HistoryImporter`. They are sanitized observation/reference material, not
classified format-2/format-3/format-4/format-5 learned history. Likewise, do not replace a raw
capture slice with a hand-authored model row: export a compact semantic slice
first, then attach it to the catalogue case.

The initial set intentionally records two evidence gaps:

- Current strict V2 intake does not produce a real continuous five/ten clean
  live-burn window from the retained 45-minute Dallara race. The synthetic
  selector cases lock the safety policy until a deliberate fragmented-lap
  intake decision is made.
- No retained archive proves a complete, box-scoped current-to-entry-to-box-to-
  pit-exit route. Daytona proves post-box fuel boundaries and incomplete stops;
  the complete route is a constructed Core contract until an exact raw capture
  can replace it.
