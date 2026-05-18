# IBT Analysis

TmrOverlay can request iRacing's own binary telemetry logging and then analyze the resulting `.ibt` file after the live session ends. This is an investigation path for deciding whether IBT is a better post-race source than TmrOverlay's live raw capture.

## Runtime Behavior

- `IbtAnalysis:TelemetryLoggingEnabled` defaults to `true`.
- IBT logging follows the same switch as raw capture. The startup raw-capture flag or the Support tab's diagnostic telemetry capture control starts a raw segment and sends iRacing's telemetry start command; stopping or finalizing that raw segment sends the telemetry stop command.
- `IbtAnalysis:Enabled` defaults to `true`.
- After raw capture finalization, the app writes `capture-synthesis.json` immediately with its own timeout, then waits up to `IbtAnalysis:MaxIRacingExitWaitSeconds` for iRacing to stop writing telemetry before looking for the best matching `.ibt` under the configured telemetry root.
- The app writes compact JSON sidecars under the raw capture directory in `ibt-analysis/`.
- The app does not copy the source `.ibt` into the capture directory by default.
- Diagnostics bundles include `metadata/ibt-analysis.json` with the active IBT settings and latest-capture status-file evidence, so support can distinguish a missing `.ibt` candidate from IBT analysis or telemetry logging being disabled.

The default telemetry root is:

```text
%USERPROFILE%\Documents\iRacing\telemetry
```

Override it with:

```powershell
$env:TMR_IbtAnalysis__TelemetryRoot = "D:\iRacing\telemetry"
```

Keep only the post-session sidecar analysis path enabled while disabling start/stop logging:

```powershell
$env:TMR_IbtAnalysis__Enabled = "true"
$env:TMR_IbtAnalysis__TelemetryLoggingEnabled = "false"
```

Re-enable the iRacing start/stop command requests while keeping analysis available:

```powershell
$env:TMR_IbtAnalysis__Enabled = "true"
$env:TMR_IbtAnalysis__TelemetryLoggingEnabled = "true"
```

Disable only the iRacing start/stop command requests while keeping analysis available:

```powershell
$env:TMR_IbtAnalysis__TelemetryLoggingEnabled = "false"
```

Disable the whole IBT sidecar path:

```powershell
$env:TMR_IbtAnalysis__Enabled = "false"
```

## Sidecar Files

When analysis succeeds, the capture directory contains:

```text
ibt-analysis/status.json
ibt-analysis/ibt-schema-summary.json
ibt-analysis/ibt-vs-live-schema.json
ibt-analysis/ibt-field-summary.json
ibt-analysis/ibt-local-car-summary.json
```

`status.json` is always written when analysis runs. If no matching `.ibt` is available, the telemetry root is missing, the candidate is too large, or analysis times out, the file records `status: "skipped"` with a reason instead of throwing into history or post-race analysis finalization.

`ibt-schema-summary.json` records the selected IBT source path, file size, header, disk header, parsed session context, type counts, and post-race candidate fields.

`ibt-vs-live-schema.json` compares the IBT schema against the current TmrOverlay live/raw schema. This is the main artifact for finding disk-only fields such as position, acceleration, and other post-race-only signals.

`ibt-field-summary.json` samples bounded telemetry records and records compact per-field first/last values, min/max/mean, non-default counts, and change counts.

`ibt-local-car-summary.json` reduces those sampled fields into local-car analysis groups: trajectory, fuel, vehicle dynamics, tires/wheels, pit service, weather, and missing opponent context. It also records track-map readiness from `Lat`/`Lon`/`Alt` plus lap-distance coverage. This sidecar is the committed contract for future local-car post-race analysis; the source `.ibt` file still stays outside the capture directory unless explicitly configured.

## Track Map Generation

The current IBT path can write a reusable user track-map asset after successful IBT analysis when the app-level `track-map.build-from-telemetry` capture setting is enabled. The default is enabled, and the Support/Diagnostics tab exposes the `Build local maps from IBT telemetry` checkbox as the user control. Fresh installs use bundled app map JSON when available and otherwise keep the circle fallback until local IBT-derived map generation creates a user map. Disabling the setting returns runtime lookup to bundled app maps plus circle fallback; stored user maps remain on disk but are not used by the overlay while disabled.

The app keeps source `.ibt` files external, extracts compact derived geometry, and saves generated user maps under app-owned local storage:

```text
%LOCALAPPDATA%\TmrOverlay\track-maps\user
```

Generated maps are keyed by the normalized track identity from session info, including track id when available, track/config/layout names, track length, and track version. The reusable schema-v2 map document stores local meter coordinates plus `LapDistPct` samples, not raw latitude/longitude, and carries sector boundaries from `SplitTimeInfo.Sectors`. It includes quality metrics for lap coverage, missing bins, closure, length delta, repeatability, pit-lane samples, and confidence. Overlays read maps through `TrackMapStore` rather than directly from capture folders or the iRacing telemetry directory.

Once a complete map exists for the current generation/schema version and track identity, later sessions skip generation. A complete runtime map requires at least `Medium` confidence and enough generated racing-line points for its bin count. Higher-confidence maps can replace weaker stored maps.

For bundled assets, run `TmrOverlay.TrackMapGenerator` against the ignored `captures/IBT` corpus and output vetted derived JSON into `src/TmrOverlay.App/Assets/TrackMaps`. The generator strips absolute source paths down to source file names for bundled provenance and writes schema-v2 sector metadata when session info contains split sectors. Raw `.ibt` files remain ignored and must not be committed.

### May 2026 Track-Map Probe

A local one-off probe against ignored `.ibt` files confirmed that the raw IBT position fields are good enough to generate recognizable track maps without any external map assets. The probe output was written under ignored local artifacts:

```text
captures/track-map-probe/
```

The probe selected complete on-track laps, discarded pit-road and near-stationary samples, projected `Lat`/`Lon` degrees into local meter coordinates with an equirectangular projection, grouped samples by `LapDistPct`, and used per-bin median `x/y` coordinates to collapse many driven laps into one representative loop. It also drew representative raw laps behind the median loop so visual inspection could reveal drift, bad lap selection, or projection mistakes.

Best statistical candidate:

- Source: `captures/IBT/porsche963gtp_lagunaseca 2024-05-11 13-11-59.ibt`
- Coverage: 594,501 records, 125 complete filtered laps, 563,441 selected points
- Generated bins: 1,200, with zero missing bins before interpolation
- Repeatability: 1.04 m median per-bin spread, 1.38 m p95 spread
- Closure: 3.03 m from final bin back to start
- Length check: 3,546.7 m derived polyline vs 3,564.8 m median `LapDist` and 3,570.0 m session track length, about 0.5-0.7% short

More complex layout candidate:

- Source: `captures/IBT/mercedesamgevogt3_nurburgring combined 2026-05-01 22-25-32.ibt`
- Coverage: 421,338 records, 14 complete filtered laps, 413,732 selected points
- Generated bins: 5,500, with zero missing bins before interpolation
- Repeatability: 1.24 m median per-bin spread, 1.57 m p95 spread
- Closure: 4.57 m from final bin back to start
- Length check: 25,092.5 m derived polyline vs 25,175.7 m median `LapDist` and 25,176.4 m session track length, about 0.33% short

The generated images looked visually sane for both Laguna Seca and Nurburgring Combined. The small length shortfall is acceptable for a visual map prototype, but production should keep `LapDist`/track length as the canonical distance source and treat the generated `x/y` loop as display geometry. If the map is used for distance-sensitive overlays, resample along `LapDistPct` and optionally scale cumulative polyline distance to the session/`LapDist` length.

Production follow-up notes:

- Use complete-lap coverage, missing-bin percentage, per-bin spread, closure distance, and length delta as first-class quality metrics.
- Prefer median or robust average bins across several clean laps; a single lap is useful for preview but should be lower confidence.
- Key stored maps by `TrackID` plus normalized track/config identity. Some tested session YAML had empty `TrackConfigName`, so the key must tolerate missing config text.
- Store derived local meter points and `LapDistPct`, not raw `Lat`/`Lon`, in the app-owned map document. Keep source IBT paths as provenance only.
- Keep iRacing/Data API map assets as optional QA references, not runtime dependencies and not bundled product assets.
- The eventual feature should expose generated maps through a `TrackMapStore`; capture folders and the iRacing telemetry directory should remain analysis/provenance inputs only.
- Ship a deterministic placeholder map shape so the live positioning overlay has something to render before a real map exists. A simple circle/oval keyed only by `LapDistPct` is enough for the first fallback because it proves marker motion, stale-state handling, orientation-independent rendering, and settings behavior without implying track accuracy.
- Treat high-confidence generated maps as durable. Once a map for the normalized track identity reaches the production confidence threshold, do not regenerate it automatically from later sessions; only retry when no map exists, the stored map is below threshold, the generation algorithm/schema version changes, or the user explicitly requests a rebuild.

#### Confidence and Pit-Lane Follow-up

A second local probe compared Laguna Seca maps generated from one complete lap, the first five complete laps, and all complete positive-numbered laps from the same high-volume IBT. Local artifacts were written under:

```text
captures/track-map-probe/laguna-lap1-vs-lap5.png
captures/track-map-probe/laguna-lap1-vs-lap5-error.png
captures/track-map-probe/laguna-weak-one-lap-vs-reference.png
captures/track-map-probe/laguna-pit-lane-layer.png
```

Using the all-lap median as a reference:

- One clean lap from the high-volume Laguna IBT had zero missing bins, 0.48 m median error, 2.23 m p95 error, 3.35 m max error, and 3.47 m closure.
- The first five clean laps had zero missing bins, 0.24 m median error, 1.21 m p95 error, 2.66 m max error, and 3.39 m closure.
- A weaker Laguna IBT with only one complete lap had 16 missing bins out of 1,200, 0.56 m median error, 14.15 m p95 error, 18.98 m max error, and a visibly poorer start/pit-area trace.
- A hard-reject IBT with 11 records and 0.17 seconds of telemetry had no valid moving points and no complete laps.

Draft confidence bands for production:

- Reject: no complete positive lap, no valid moving coordinates, or large missing-bin coverage after attempted generation.
- Low confidence: one complete lap or no multi-lap repeatability metric; useful for preview only.
- Medium confidence: two to four complete laps with low missing bins, low closure, and stable length, but limited repeatability evidence.
- High confidence: five or more complete laps, zero or near-zero missing bins, closure under about 10 m, length delta under about 1%, and multi-lap repeatability p95 under about 3 m.

The same Laguna IBT also confirmed that pit-lane geometry should be handled as a separate open path, not folded into the closed racing line. The file had 9,738 `OnPitRoad` samples, 4,029 moving pit-road samples, and three drawable pit-lane passes. Resampling those passes by cumulative pit-lane distance produced a 327.2 m median pit-lane path with 0.58 m median spread and 1.38 m p95 spread. Production should use `OnPitRoad` to filter race-line coordinates and should store `racingLine` and `pitLane` geometries separately. Pit-lane generation should be resampled by open-lane progress or cumulative distance, not by closed-loop `LapDistPct`, because pit road can run parallel to the racing surface and does not close back on itself.

## Guardrails

IBT analysis is deliberately best-effort:

- It is separate from `capture-synthesis.json`; synthesis can run as soon as the TmrOverlay raw capture closes.
- Capture synthesis is bounded by `TelemetryCapture:MaxSynthesisMilliseconds`.
- `live-model-parity.json` consumes the resulting raw/IBT sidecar metadata after finalization so model-v2 review can see which live-model signals were available in raw capture, common with IBT, IBT-only, live-only, or missing; it also carries `promotionReadiness` so clean sessions can be flagged for model-v2 cutover review.
- It waits only up to `IbtAnalysis:MaxIRacingExitWaitSeconds` for the simulator process to exit before skipping the current run and leaving the capture eligible for startup recovery.
- It has an analysis timeout.
- It samples at most `IbtAnalysis:MaxSampledRecords` records.
- It ignores candidate files larger than `IbtAnalysis:MaxCandidateBytes`.
- It ignores files newer than `IbtAnalysis:MinStableAgeSeconds` so an active writer is not parsed.
- It scans only `IbtAnalysis:MaxCandidateFiles` recent `.ibt` files.
- It writes local-car summaries from the same bounded sample scan instead of doing a second unbounded pass over the IBT payload.
- It records skipped/failed status files and app events instead of failing compact history, post-race analysis, or capture synthesis.

## Current Investigation Notes

Initial inspection of one Mercedes-AMG GT3 Nürburgring IBT sample showed a version 2 file at 60 Hz with 286 fields, 62,270 disk records, and about 17.3 minutes of telemetry. Compared with the latest live/raw schema sample, 257 fields overlapped, 29 fields were IBT-only, and 77 fields were live/raw-only. The IBT-only set included `Lat`, `Lon`, `Alt`, wheel speeds, tire pressure/temperature fields, ride heights, and splitter ride height. The live/raw-only set included many `CarIdx*` arrays, `CarLeftRight`, replay/camera state, and disk-logging flags. That makes IBT promising for car-local post-race physics/position analysis, while live/raw capture remains important for opponent/team timing and overlay context.

May 2026 uploaded-data pass:

- `captures/IBT` contained 876 parseable `.ibt` files totaling about 21.4 GiB.
- All parsed files contained local-position fields such as `Lat`, `Lon`, and `Alt`.
- None of the parsed files contained the live opponent/timing arrays used by current overlays, including `CarIdxLapDistPct`, `CarIdxF2Time`, `CarIdxEstTime`, `CarIdxPosition`, `CarIdxClassPosition`, `CarIdxOnPitRoad`, or `CarLeftRight`.
- A local search across the IBT inventory and representative `strings` output did not find car dimension metadata such as `CarLength`, `CarWidth`, `Wheelbase`, body length, or vehicle dimensions. IBT can identify the car by `CarID`/`CarPath`, but radar body-size calibration still needs a curated external metadata table or another proven source for physical dimensions.
- Treat IBT as a strong local-car post-race enrichment source, especially for trajectory, fuel-level, tire, wheel, ride-height, and vehicle-dynamics analysis. Do not treat it as a replacement for raw/live capture when the analysis needs opponent timing, side occupancy, camera focus, team/focus `CarIdx`, or standings context.
- The repo keeps compact examples under `fixtures/telemetry-analysis/`. Do not commit raw `.ibt` files or raw `telemetry.bin` captures; commit derived example JSON when a new signal assumption needs to be preserved.
