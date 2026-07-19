# Data Contract Snapshots

This folder stores compact release snapshots for durable user-data contracts.

Each released app-data contract gets one version directory, for example
`v0.19.0/`. A snapshot directory should contain:

- `data-contract.json`: the release's version constants, durable file classes, sample paths, and compatibility policy.
- `settings/settings.json`: representative user settings that must load through the current `AppSettingsMigrator`.
- `history/samples/`: representative compact history documents. Tests materialize these into app-style car/track/session paths before running current history maintenance.
- `track-maps/samples/`: representative generated map documents. Tests materialize these into the app-generated map filename before running current track-map readers.
- `captures/`: tiny raw-capture metadata/schema samples for diagnostic format compatibility. Do not include raw `telemetry.bin` payloads.
- `runtime-state.json`: a minimal diagnostic runtime-state sample, treated as readable but disposable.

A release snapshot may also contain a narrowly scoped `compatibility/` input
for an intermediate durable format. These inputs are frozen reader fixtures,
not a claim that the containing release wrote that later format; tests must
prove they load without rewriting the immutable source file.

Branch-complete validation should exercise the previous released snapshot against
the current code, including snapshot-to-browser, snapshot-to-localhost, and
snapshot-to-native mapping tests. Future durable schema changes should add the
new version snapshot in the same branch as the migration or compatibility
adapter.

Live overlay evidence contracts can grow without adding a new durable snapshot
when they do not change persisted user data. Local-role evidence is one such
case: tests should prove the model/evidence path can carry
`DriverInfo.Drivers[].IsSpectator` and a nullable future `isSpotting` field, but
the `v0.19.0` durable settings/history/map snapshot remains valid.

Do not store private driver/team identity, raw capture payloads, source `.ibt`
files, full session YAML, diagnostics bundles, or machine-specific paths here.
