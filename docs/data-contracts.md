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
- `history/user/fuel-v2/manifest.json`
- `history/user/fuel-v2/cars/.../summaries/*.json`
- `history/user/fuel-v2/cars/.../aggregate.json`
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
- Fuel V2 learned history is absent from this baseline; its first schema is
  versioned separately by `FuelV2HistoryDataVersions` when the Fuel V2
  diagnostics-only collection slice is enabled.
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

`fixtures/data-contracts/v1.2.3/` is the released Fuel V2 format-1
connection-history baseline. `fixtures/data-contracts/v1.3.0/` is the current
format-5 classified, exact-car/exact-layout session-history contract, including
a confirmed front-tire stationary-service counter example. The current reader loads the v1.2.3
fixture's frozen raw sidecar and summary as `legacy-unclassified` without
rewriting either one, then rebuilds the v1.3 aggregate separately. This makes
the migration boundary explicit rather than pretending legacy connection
evidence is newly classified history.

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

Fuel V2 learned history uses its own `FuelV2HistoryDataVersions` constants and
is stored under `history/user/fuel-v2/`. It is not read by V1 strategy while
`FuelV2History:UseForStrategy=false`; changes to its manifest, summary,
aggregate, or import semantics should bump the narrow Fuel V2 version constant
instead of the V1 `HistoricalDataVersions` constants.

The gated factual Fuel V2 presenter may read an exact classified normal-burn
aggregate only to populate its explicitly labeled `History` comparison bucket.
That display-only reader cannot select a strategy, emit fuel-to-add/pit advice,
or alter V1. `TmrOverlay.OverlayModelReplay` may stage supplied prior sidecars
under its explicit output directory for the same display/replay path; this is
ephemeral replay evidence, never a write to `history/user/fuel-v2/`, and it
rejects sidecars that finish after the earliest replayed frame.

Fuel V2 format-5 capture writes independent session segments and bounded
stationary-service observations. The importer
classifies one for learned history only when it has an injective exact-car
identity, exact `TrackId + TrackConfigName` layout identity,
race/practice/qualifying family, and verified occurrence that cross-check
against the raw scope. Planned lap/time race length, fuel BoP/effective
capacity, setup, weather, and special-session effects are summary context—not
storage partitions. Format-4 observations add raw entry/exit and delta snapshots
for total, side, axle, and exact four-corner tire counters when available, which
lets a later Core reader distinguish requested `LF`, `Front`, `Left`, or `4 tires`
from an executed result. It also preserves raw `WeekendInfo.DCRuleSet` in the
session identity. Capture and summary/import versions are `5`; manifest is `3`
and the fuel-burn aggregate remains `2`. `DCRuleSet` is retained as raw
provenance only; it cannot classify sequential/parallel service execution or
unlock service timing. Counter evidence remains source evidence only: it
cannot infer service overlap/order or timing advice.

Format-4 Fuel V2 sidecars and summaries remain compatible classified history
with exact tire-counter snapshots but no persisted service-rule identity.
Format-3 Fuel V2 sidecars and summaries remain compatible classified history with
stationary-service observations but no exact tire-counter snapshots. Format-2
sidecars and summaries remain compatible classified fuel history when their
lineage validates, but have no stationary-service observations. Neither can
prove an executed tire shape or produce service timing. Format-1 Fuel V2 sidecars and summaries are retained through compatible readers
as `legacy-unclassified`; they are not inferred into Race/Practice/Qualifying
and cannot contribute to the classified aggregate or a future strategy reader.
They remain at their existing legacy paths so the release does not delete or
rewrite mixed connection evidence. The manifest reports classified, legacy-v1,
retained-but-unclassified-v2, unreadable, and misfiled summary counts. Current v5 writes use content-hash
summary IDs, which makes duplicate import idempotent without treating readable
source labels as unique keys.

On a v1-only upgrade, Fuel V2 startup maintenance writes current-format derived
manifest/aggregate files but leaves every v1 summary byte-for-byte intact. If a
surviving v1 sidecar is later recovered, its existing legacy `sourceId` is
recognized and skipped rather than creating a second v2-hash summary for the
same evidence.

If a reconnect produces multiple classified sidecars for the same verified
occurrence, all summaries remain durable diagnostics but the rebuilt aggregate
uses only the strongest one and reports the excluded duplicate count. This
prevents a reconnect from double-weighting learned fuel evidence before a
future explicit evidence-merging policy exists.

Fuel V2 format-version-1 capture and summary models already contain physical
tank capacity, driver/class fuel-cap percentages, effective session capacity,
source, and limitation fields. Populating those existing nullable/placeholder
fields from live session info does not change the persisted shape or reinterpret
older values, so it does not by itself bump a Fuel V2 version. Readers must
continue to accept older artifacts whose effective capacity is null or whose
source records that the value was unavailable.

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
