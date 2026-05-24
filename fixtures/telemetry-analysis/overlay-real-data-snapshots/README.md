# Overlay Real-Data Snapshots

Compact, redacted overlay snapshots derived from real capture analysis.

These files are not raw captures. They intentionally keep only the fields needed
to turn a real observation into a deterministic CI assertion:

- scenario ids from `tools/validation/overlay-scenario-contract.json`
- redacted capture provenance
- raw evidence fields that explain why the frame/window matters
- expected model/render contract fields that should be proven before pixels

Synthetic or capture-shaped snapshots are allowed only when the file says so in
`source.sourceCategory`, keeps the provenance honest, and uses the fixture to
prove renderer/product capability rather than claiming raw telemetry replay.

Do not commit `telemetry.bin`, full session YAML, source `.ibt` files, driver
names, user IDs, or team identities here.
