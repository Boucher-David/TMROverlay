# Fixtures

This folder contains small, git-tracked sample data for tests, parser development, and overlay prototyping.

Raw captures stay under `captures/` and are intentionally ignored because they can be multi-GB. Fixtures should be synthetic or sanitized, compact enough to review in git, and stable enough to act as contracts for the fields the app expects.

## Current Fixtures

- `iracing/session-info/synthetic-endurance-team-race.yaml`
  - Synthetic iRacing endurance session-info shape.
  - Includes weekend, session, driver, result, setup, split, radio, and camera data.
  - Adds a fixture-only `SyntheticTelemetryFrames` block because live fuel/input values come from `telemetry.bin`, not from iRacing session YAML.
- `telemetry-analysis/*.example.json`
  - Compact, sanitized examples derived from raw capture and IBT investigation artifacts.
  - Preserve source-availability and model-v2 evidence assumptions without committing source `.ibt` or `telemetry.bin` payloads.
- `telemetry-analysis/fuel-v2-white-room-24h/`
  - A constructed endurance fixture family for deterministic Fuel V2 model/presenter replay. Its manifest explicitly forbids capture-specific or raw-telemetry claims.
- `data-contracts/v*/`
  - Versioned durable user-data snapshots.
  - Exercise app-settings, compact history, generated track-map, raw-capture metadata, and runtime-state compatibility from a previous release into current code.
  - See `docs/data-contracts.md`.
