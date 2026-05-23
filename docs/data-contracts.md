# Durable Data Contracts

This note defines the v1.0-era user-data compatibility contract.

## Contract Boundary

The app writes several kinds of local data under `%LOCALAPPDATA%\TmrOverlay`.
They do not all deserve the same compatibility promise.

### Durable User Data

These are product contracts. Future releases must migrate them, read them
compatibly, or explicitly skip unsupported versions without destroying them:

- `settings/settings.json`
- `history/user/cars/.../summaries/*.json`
- `history/user/cars/.../aggregate.json`
- `history/user/cars/{carKey}/radar-calibration.json`
- `track-maps/user/*.json`

### Versioned Diagnostics

These are diagnostic contracts. They should remain parseable by tools that know
their format version, but the app should not rewrite them in place:

- raw capture `capture-manifest.json`
- raw capture `telemetry-schema.json`
- raw capture `telemetry.bin`
- raw capture `latest-session.yaml` and `session-info/*.yaml`
- compact sidecars such as `capture-synthesis.json`, `ibt-analysis/*.json`,
  `live-model-parity.json`, and `live-overlay-diagnostics.json`
- app-owned overlay forensics packages under `forensics/<capture-id>/`, including
  `storage-boundary.json`, `input-inventory.json`, `package-status.json`,
  `obs-readiness.json`, `evidence-gaps.json`, `overlay-forensics.json`, and
  derived replay/model/renderer artifacts when present

### Disposable Runtime Data

These can be ignored, overwritten, or dropped if incompatible:

- `runtime-state.json`
- logs, performance snapshots, diagnostics bundles, and support bundle outputs
- temporary caches

## v0.19.0 Baseline

`v0.19.0` is the first release snapshot checked into
`fixtures/data-contracts/v0.19.0/`. It is the v1-candidate durable contract
baseline for:

- `AppSettingsMigrator.CurrentVersion = 11`
- `SharedOverlayContract` contract version `1`
- history `summaryVersion = 1`
- history `collectionModelVersion = 1`
- history `aggregateVersion = 3`
- history `carRadarCalibrationAggregateVersion = 1`
- post-race `analysisVersion = 1`
- track-map `schemaVersion = 2`
- track-map `generationVersion = 1`
- raw-capture manifest `formatVersion = 1`
- runtime-state `runtimeStateVersion = 1`

App-owned overlay forensics packages are diagnostic/support output, not durable
settings, history, or generated map state. Introducing
`%LOCALAPPDATA%\TmrOverlay\forensics\<capture-id>` does not rewrite the v0.19.0
baseline snapshot because no released durable reader schema changes. Future
snapshots should add only compact, sanitized forensics fixtures when production
readers or validators start depending on a released forensics contract.

The v0.19.0 snapshot intentionally contains representative user choices,
schema-shaped history samples, generated map geometry, raw-capture metadata, and
runtime state. The tests should prove current code can load the old settings,
preserve user choices, materialize compact history and track-map samples into
app-style storage paths, rebuild stale derived history, read supported generated
maps, parse diagnostic metadata, and map the release settings into browser,
localhost, and native overlay consumers.

## Snapshot Workflow

Each durable contract release should get one directory:

```text
fixtures/data-contracts/vMAJOR.MINOR.PATCH/
```

At minimum, include:

- `data-contract.json` with product version, version constants, sample paths,
  reader names, and compatibility rules.
- `schemas/app-settings.txt` for the exact persisted app-settings model shape.
- `schemas/history.txt` for the exact persisted history/analysis model shape.
- representative persisted files for settings, compact schema-shaped history
  samples, generated track-map samples, raw-capture metadata, and runtime state.

Keep snapshots compact, synthetic or sanitized, and source-reviewable. Do not
commit raw `telemetry.bin`, source `.ibt`, private driver/team identity, local
absolute paths, diagnostics bundles, bulky forensics packages, or full session
YAML.

## Required Validation

Every branch that changes durable data behavior must run the data-contract
snapshot tests. The current tests exercise the previous released v0.19.0
snapshot through the current readers:

```powershell
dotnet test .\tests\TmrOverlay.App.Tests\TmrOverlay.App.Tests.csproj --filter DataContracts
```

Windows CI also runs this as a named `Data contract snapshot tests -
localhost/native` step before the full solution test pass, while the browser
review mapping runs in the dedicated browser test step. Data-contract
regressions are visible as their own gates instead of only failing inside the
catch-all solution tests.

The snapshot-to-overlay mapping tests intentionally drive localhost and native
consumers from a production-shaped, model-only live snapshot. That protects the
current runtime contract: released settings must map into browser, localhost,
and native overlays without relying on `LatestSample` as an overlay-rendering
input.

Overlay evidence contracts are not durable user data, but they are validation
contracts. Browser/localhost/native evidence must preserve local-role context
when it is known: `DriverInfo.DriverCarIdx`/`PlayerCarIdx`, focus car index,
`DriverInfo.Drivers[].IsSpectator` for the local and focus rows, derived
`localRole`, and a nullable `isSpotting` slot for a future true spotting signal.
The v1.0-era durable snapshots do not bump for this because no persisted
settings/history/map schema changed.

On non-Windows machines without `dotnet`, the branch can still update fixtures
and docs, but Windows/CI must run the test before release.

## Change Rules

When a durable schema changes:

- Add the new release snapshot in the same branch.
- Keep the previous release snapshot and prove current code can load or migrate
  it.
- Bump the narrowest version constant that describes the change.
- Add a migration, compatible reader, or explicit skip path before overlays or
  strategy code consume the changed data.
- Update schema snapshots, `docs/history-data-evolution.md`, this note, and any
  release/update docs that mention compatibility.

When a setting default changes without changing the persisted setting shape, do
not bump the settings schema by default. Prefer migrator logic that only applies
the default transition to versions that predate the baseline that introduced the
new default.

## Snapshot Test Failures

Treat snapshot failures as contract signals, not routine assertion maintenance.
Before changing expected values, identify which side is wrong:

- If the fixture no longer represents a real released app-data shape, fix the
  fixture and document why an older snapshot correction was necessary.
- If current readers or overlay consumers no longer honor the released contract,
  fix the production reader/model path or add an explicit migration/adapter.
- If the assertion was too broad or imprecise, narrow it to the exact setting,
  row, segment, route, or native consumer behavior it intended to protect.

The snapshot should stay a truthful representation of released data and its
expected effect on browser, localhost, and native surfaces.
