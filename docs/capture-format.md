# Capture Format

Raw capture is an opt-in diagnostic/development mode. When `TelemetryCapture:RawCaptureEnabled` is `true`, each live capture produces a directory with four core artifacts:

- `capture-manifest.json`
- `telemetry-schema.json`
- `telemetry.bin`
- `latest-session.yaml`

Optional historical session snapshots are stored under `session-info/`.

After capture finalization, the app also writes compact post-session sidecars when possible:

- `capture-synthesis.json` summarizes the raw capture schema and sampled frame values for investigation without reopening the full binary payload. It is bounded by `TelemetryCapture:MaxSynthesisMilliseconds`.
- `ibt-analysis/*.json` summarizes the best matching iRacing `.ibt` file when IBT analysis is enabled and a stable candidate is available. Candidate selection considers capture-window timing plus IBT duration/record coverage so tiny warmup-only files do not outrank fuller session files solely because they were written later. The sidecars include schema comparison, bounded field stats, and a local-car summary for trajectory/fuel/vehicle-dynamics investigation.
- `live-model-parity.json` summarizes whether the additive model-v2 live state matched the legacy overlay inputs during the session when model-v2 parity collection is enabled, records raw/IBT signal availability for later model review, and includes `promotionReadiness` so enough clean evidence can be flagged for model-v2 cutover review.
- `live-overlay-diagnostics.json` summarizes passive focus/gap/radar/fuel/pit-service/position-cadence/lap-delta/sector-timing assumptions and track-map sector-highlight coverage when overlay diagnostics collection is enabled, including focus-unavailable context, local-only radar suppression, Fuel/Pit local-strategy suppression, missing lap-counter fallback, reset-style progress discontinuities, pit-service signal changes, and partial/degraded radar signals.
- `fuel-v2-capture/fuel-v2-diagnostics.json` summarizes compact Fuel Calculator V2 calibration evidence when Fuel V2 capture is enabled, including app/session scope, sampled fuel/progress/lap-budget inputs, lap-burn windows, sector-burn samples, pit windows, team stint windows, and source/rejection labels. The sidecar remains diagnostic evidence; a separate Fuel V2 importer can promote selected derived facts into `history/user/fuel-v2/` after finalization.

These sidecars are additive. Existing raw captures without them remain readable, startup recovery can fill missing sidecars later, and source `.ibt` files are not copied into capture directories by default.

When raw capture finalization succeeds, the app also creates an initial overlay forensics package under `%LOCALAPPDATA%\TmrOverlay\forensics\<capture-id>`. That package is not part of the immutable raw capture directory. It indexes the capture, compact sidecars, storage boundary, package/enrichment status, OBS/localhost readiness, and evidence gaps so the offline replay tool can add active production model samples, semantic manifests, and renderer/pixel evidence later without mutating the capture. If that forensics folder has already been enriched by the replay tool, later app finalization or startup recovery must preserve it instead of replacing it with a starter package.

Compact edge-case telemetry artifacts, live overlay diagnostics, and Fuel V2 capture artifacts collected without raw capture are not part of the raw capture format. They are written separately under the logs root as JSON and may be included in diagnostics bundles without including `telemetry.bin`.

## `telemetry-schema.json`

This file is a JSON array describing every telemetry variable exposed by the SDK for the capture:

- variable name
- type name and numeric type code
- element count
- byte offset inside the telemetry buffer
- unit
- description

The schema is written once per capture and is intended to be used when decoding `telemetry.bin`.

## `telemetry.bin`

`telemetry.bin` is an append-only little-endian binary file.

### File Header

The file begins with a 32-byte header:

1. `magic` - 8 ASCII bytes: `TMRCAP01`
2. `sdkVersion` - `int32`
3. `tickRate` - `int32`
4. `bufferLength` - `int32`
5. `variableCount` - `int32`
6. `captureStartUnixMs` - `int64`

### Frame Record

Each telemetry frame is appended as:

1. `capturedUnixMs` - `int64`
2. `frameIndex` - `int32`
3. `sessionTick` - `int32`
4. `sessionInfoUpdate` - `int32`
5. `sessionTime` - `float64`
6. `payloadLength` - `int32`
7. `payload` - raw telemetry buffer bytes

`payloadLength` should normally match the `bufferLength` value in the file header.

## Session YAML

`latest-session.yaml` is overwritten whenever iRacing increments `SessionInfoUpdate`.

If `StoreSessionInfoSnapshots` is enabled, the same YAML content is also written to:

```text
session-info/session-0001.yaml
session-info/session-0002.yaml
...
```

Those snapshots let us reconstruct session metadata changes over time without parsing the binary stream.

## Semantic Replay And Import

Raw replay is a development/evidence path, not a production collector mode. The
shared semantic replay reader composes:

- `capture-manifest.json`
- `telemetry-schema.json`
- `telemetry.bin`
- `latest-session.yaml`
- `session-info/session-*.yaml`

into frame rows containing the raw frame envelope, matching session YAML when
`SessionInfoUpdate` changes, and a decoded `HistoricalTelemetrySample`. Runtime
app replay and production overlay model replay should use this semantic reader
instead of each replay path rebuilding raw-frame decoding and session-info
lookup.

Replay consumers must keep these constraints:

- read only an explicit capture directory selected by developer/test
  configuration or tooling arguments
- do not mutate raw capture directories or copy source `.ibt` files into replay
  artifacts
- keep raw `telemetry.bin` and full private session YAML out of committed
  fixtures, diagnostics bundles, and ordinary screenshot artifacts
- record replay provenance in derived artifacts: capture id, source files,
  sample plan, frame index, session time, session tick, session-info update,
  session-info match source, model source, focused car, and renderer surface
- treat `latest-session.yaml` fallback for missing historical snapshots as
  degraded provenance, because early frames may be interpreted with later
  session metadata

The semantic reader now supports bounded replay windows and import inspection.
Frame index, session-time, session-type, and focus-car filters are additive and
can be used by the runtime replay provider, production model replay, and compact
export tooling without changing the raw capture. Import inspection compares the
binary header against the manifest, reports observed frame count and first/last
frame/session times, flags payload lengths that differ from manifest
`bufferLength`, detects unsupported schema type names, lists observed
`SessionInfoUpdate` values, and warns when exact `session-info/session-*.yaml`
snapshots are missing.

For a compact import gate without rendering overlays:

```powershell
dotnet run --project .\tools\TmrOverlay.RawCaptureReplayExport\TmrOverlay.RawCaptureReplayExport.csproj -- `
  --capture C:\path\to\capture-YYYYMMDD-HHMMSS-fff `
  --output C:\tmp\tmr-replay-import `
  --strict
```

The tool writes `import-summary.json`. Add `--emit-samples` to also write a
bounded `decoded-samples.jsonl` containing compact semantic rows only: frame
metadata, session-info provenance, session context, focused/local/team/leader
progress, local fuel/speed/input fields, and row counts for decoded car lists.
Use `--start-frame`, `--end-frame`, `--start-session-time`,
`--end-session-time`, `--session-types`, `--focus-car-idx`,
`--sample-frames`, `--sample-every`, and `--max-samples` to keep exports small.
The tool does not copy `telemetry.bin` or full private session YAML into the
output.
