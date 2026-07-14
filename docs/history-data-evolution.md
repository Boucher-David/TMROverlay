# History Data Evolution

This note defines how newer app versions should handle user data written by older versions.

## Goals

- Preserve useful user car/track/session history across app upgrades.
- Give highest migration priority to data that strengthens future user-facing overlay behavior, such as car/track combo history, stint shape, pit-service timing, fuel usage, lap pace, and confidence/source metadata.
- Prefer deterministic rebuilds over lossy aggregate mutation.
- Keep live telemetry collection and overlays resilient when legacy data is corrupt, incomplete, or unsupported.
- Make maintenance visible in diagnostics without putting modal upgrade prompts over the sim.

## Priority

Not every persisted file deserves the same upgrade effort.

High-priority data is user-focused history that makes the app better for that user later:

- car/track/session summaries
- fuel burn and stint history
- Fuel V2 learned history under `history/user/fuel-v2/`, including imported
  sidecar summaries, rebuilt aggregates, scope/source labels, and calibration
  evidence that is explicitly not consumed by V1 strategy yet
- pit lane, pit stall, tire-service, repair, and fill-rate history
- lap pace and leader/context metrics used by overlays
- confidence flags and source labels that explain whether a metric came from local-driver telemetry, team-car telemetry, inference, or baseline samples

Medium-priority data is user preference and app behavior state:

- overlay visibility, position, scale, units, and display options
- update channel or future bridge settings

Low-priority data is operational telemetry:

- performance snapshots
- debug logs
- runtime heartbeats
- diagnostics bundles

Low-priority data should usually stay readable or disposable, not migrated, unless it becomes an input to a user-facing historical model.

## Data Categories

### Source Data

Session summaries under `history/user/cars/.../summaries/` are the canonical compact history records. They should carry enough schema and collection metadata for future migration:

- `summaryVersion`
- `collectionModelVersion`
- `appVersion`
- `sourceCaptureId`
- combo identity
- quality/confidence flags

When a future collection model changes, migrate these records first when the old fields can be mapped honestly.

### Derived Data

These files should be rebuilt from source data instead of patched in place:

- `aggregate.json`
- post-race analysis JSON generated from a summary

Aggregates are running metrics and lose per-session detail. Rebuilding from migrated summaries avoids compounding old calculation mistakes.

### Immutable Diagnostics

These artifacts should remain readable as diagnostics but should not be migrated into the current history model:

- raw capture folders
- `telemetry.bin`
- raw telemetry schemas
- edge-case reports
- diagnostics bundles
- performance logs

If a tool needs to inspect them later, it should read their own `formatVersion` or schema metadata.

### Runtime Data

Runtime state and caches may be dropped or overwritten when incompatible. They should not affect user history compatibility.

## Versioning Rules

Every durable user-data schema should have an explicit current version constant in code and a matching migration path.

Use these version scopes:

- `summaryVersion`: JSON shape of a stored session summary.
- `collectionModelVersion`: meaning of derived history metrics, quality rules, stint/pit-stop extraction, and confidence flags.
- `aggregateVersion`: JSON shape of aggregate files.
- `analysisVersion`: JSON shape of post-race analysis files.
- `FuelV2HistoryDataVersions`: separate manifest, summary, aggregate, and import
  model versions for Fuel V2 learned history. These do not change the V1 history
  readers or strategy path.

The app should write only the current versions. Readers may accept older versions only through migration or explicit compatibility adapters.

`fixtures/data-contracts/v0.19.0/` is the first checked-in release snapshot for this policy. Future durable schema branches should add the next versioned snapshot there and keep tests proving the previous release snapshot can load into the current app. See `docs/data-contracts.md` for the full snapshot workflow.

## Validation Sweep

Schema changes are compatibility events. If a durable user-data model changes shape or meaning, the same sweep that checks stale docs and tests must also verify backwards compatibility:

- decide whether to bump `summaryVersion`, `collectionModelVersion`, `aggregateVersion`, or `analysisVersion`
- add or update migrations, rebuild logic, or compatible readers before overlays consume the data
- add tests for old data, unsupported future data, and degraded/corrupt data where the change can affect user history
- update the versioned release snapshot under `fixtures/data-contracts/` and keep the previous release snapshot covered by tests
- update `HistorySchemaCompatibilityTests` so the durable schema snapshot changes only after the compatibility decision is explicit
- update this note, repo context, and any user-facing docs that describe persisted history

## Maintenance Flow

Add a `HistoryMaintenanceService` before relying on history for overlays:

1. Read a maintenance manifest from `history/user/.maintenance/manifest.json`.
2. Inventory summary, aggregate, and analysis files.
3. Detect schema and collection model versions.
4. Run ordered migrations for supported summary versions.
5. Rebuild aggregates from compatible migrated summaries.
6. Rebuild or mark stale generated analysis files when the source summary changed.
7. Write a new manifest with counts, migrated versions, skipped files, failures, and timestamps.
8. Record an app event and include the manifest in diagnostics.

The service should be best-effort. If it fails, overlays should behave as if no compatible history exists.

## Write Safety

Migration writes must be atomic:

- write to a temp file in the same directory
- flush and replace the target file
- keep a timestamped backup for major migrations under `history/user/.backups/{yyyyMMdd-HHmmss}/`

Do not delete legacy source files in the same release that first migrates them.

## Compatibility Policy

Migrate when:

- required old fields are present
- units and meanings are known
- a confidence flag can honestly describe degraded precision
- aggregates can be rebuilt from source summaries

Skip when:

- a required field was never collected
- the old metric had a different meaning that cannot be mapped
- source data is corrupt or partial
- migration would require raw telemetry that is not present

Skipped files should remain on disk and be excluded from current aggregates. The manifest should include a reason such as `unsupported_schema`, `missing_required_field`, or `corrupt_json`.

## Implemented Slice

The current implementation is intentionally narrow:

- new session summaries include `collectionModelVersion`
- `HistoryMaintenanceService` runs in the background at startup when session history is enabled
- legacy summaries missing version metadata are normalized and backed up
- `aggregate.json` is rebuilt from all compatible summaries in each car/track/session folder
- corrupt, unsupported, or future-version summaries are skipped and recorded in the maintenance manifest
- `SessionHistoryQueryService` rejects incompatible aggregate versions instead of feeding them to overlays
- `aggregateVersion = 3` keeps combo aggregates track/session scoped and removes radar calibration from `aggregate.json`
- car radar body-size calibration is stored separately at `history/user/cars/{carKey}/radar-calibration.json`, versioned by `carRadarCalibrationAggregateVersion`
- diagnostics bundles include `history/user/.maintenance/manifest.json` when present
- Fuel V2 diagnostics can now promote compact derived evidence into
  `history/user/fuel-v2/` when `FuelV2History:Enabled=true`. The importer stores
  artifact provenance, session scope, fuel-cap facts, accepted/rejected burn
  evidence, lap-budget outcome metrics, pit/service windows, and team stint
  shape while excluding raw frame streams. `FuelV2History:UseForStrategy=false`
  keeps V1 fuel strategy from reading these records.
- Fuel V2 format 2 hardens this intake into immutable session segments. New
  records are classified only when exact car, exact `TrackId + TrackConfigName`
  layout, normalized Race/Practice/Qualifying/Offline-Testing family, and a current/session
  occurrence number agree throughout the accepted frames. The reusable family
  is car + layout; race length and fuel-cap/BoP remain explicit summary context
  for later ranking/live adjustment rather than path keys. Version-1 Fuel V2
  records are retained as `legacy-unclassified`, counted separately from
  retained-but-unclassified v2 records in the Fuel V2 manifest, and excluded
  from learned metrics rather than being guessed into a session family. Startup
  recovery replays every eligible retained compact sidecar idempotently after
  an interrupted import. A v1-only upgrade rebuilds the manifest/aggregate in
  current format without rewriting the v1 summaries, and a surviving v1 sidecar
  whose legacy source was already retained is skipped rather than duplicated.
  Reconnect duplicates of one verified occurrence are retained but only the
  strongest segment contributes to aggregate metrics; the aggregate reports
  excluded duplicate occurrences rather than silently double-weighting them.

- Fuel V2 format 3 adds bounded stationary-service observations to each
  immutable sidecar and imported summary. These are separate from pit-lane
  windows and retain request shape, raw service flags, counters, fuel-flow
  cadence, and qualification failures. Summary/import versions are `3`; the
  manifest and fuel-burn aggregate remain `2` because no service duration/rate
  is aggregated or used for advice. Format-2 classified summaries remain
  readable and eligible for learned burn history, but have no service evidence
  and therefore cannot yield service-time advice.
- Fuel V2 format 4 retains the same bounded stationary-service observations and
  adds raw entry/exit plus delta snapshots for total, side, axle, availability,
  and exact four-corner tire counters when the live SDK exposes them. The shared
  Pit Service classifier can therefore retain exact one-corner, front/rear,
  left/right, four-tire, and unusual-shape evidence without converting a
  requested selection into a claimed result. Request changes, repair, and
  interrupted telemetry make a window unsuitable for later tire-timing
  learning. Summary/import versions are `4`, manifest is `3`, and the fuel-burn
  aggregate remains `2`; format-3 summaries remain readable but cannot prove
  exact executed tire shapes.
- Fuel V2 format 5 preserves raw `WeekendInfo.DCRuleSet` in the classified
  session scope so immutable exact tire observations can be selected only
  against the same live iRacing rules identity. A versioned catalog maps only
  documented rule IDs to sequential or parallel service; absent, fair-share,
  and unfamiliar values stay unknown. The first read-time tire profile may
  report an exact observed shape or ask to collect a sample, but it emits no
  seconds or “free tires” claim. Summary/import versions are `5`; manifest is
  still `3` and the fuel-burn aggregate is still `2`; older formats remain
  readable under their original evidence limits.
- Fuel V2 history format 6 is this branch's single summary/import schema step.
  It promotes clean `Offline Testing` sidecars into a distinct `test` family
  using the existing Practice-equivalent accepted-lap quality gates and retains
  new format-6 direct local pit-route observations beside immutable exact
  summaries. Each route carries two-sample-confirmed pit-entry/stall/exit
  checkpoints plus `DriverPitTrkPct`, pit-speed, pit-stall-count, and raw rules
  provenance; continuous no-stall routes are retained as optional pit-lane-pass
  calibration. Route evidence remains out of the fuel aggregate and strategy until a later
  route learner explicitly promotes it. Normal history selection remains exact car + layout and keeps
  provenance explicit: Race reads `race`, then `practice`, then `test`;
  Practice reads `practice`, then `test`; Test reads `test`, then `practice`.
  Test evidence is never silently relabeled as Race or Practice. Summary/import
  versions are `6`, manifest is `4`, and rebuilt aggregates are `3`; compatible
  format-5 summaries remain readable (with an empty route list) and their aggregates rebuild without
  rewriting the immutable summaries.
- `HistorySchemaCompatibilityTests` snapshots durable summary, aggregate, and analysis model shapes so schema changes force a compatibility review during test validation

Radar calibration history is car-scoped, not track/session-scoped. Summaries may store clean `CarLeftRight` side-window durations, identity-backed body-length estimates, and confidence flags. The car-level aggregate stops accepting new learned samples once the body-length metric is trusted. Live radar uses exact bundled car specifications first, trusted user calibration second, low-confidence bundled estimates third, and the hard-coded default only when none of those are available.

Settings already use `AppSettingsMigrator`; history maintenance should follow that pattern but operate on directories of files instead of one settings document.

Future summary shape changes should add ordered summary migrations to the maintenance service, then keep aggregate rebuild logic derived from migrated summaries.
