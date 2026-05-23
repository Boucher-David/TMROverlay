---
name: tmr-overlay-hot-start
description: Use when continuing work in the tmrOverlay repo, resuming after a closed terminal or compacted session, discussing what to do next, starting a new milestone or branch, or capturing product/app theory that should survive between Codex sessions. Forces a hot-start read of docs/model-v2-future-branches.md and VERSION.md before planning or implementation, and records durable model-v2 or future-branch decisions back into docs/model-v2-future-branches.md.
---

# Tmr Overlay Hot Start

Use this skill as the first step for repo continuity. Its purpose is to make the next Codex session recover the current app theory, active milestone, and agreed next steps without the user needing to restate yesterday's analysis.

## Hot Start

1. Read `AGENTS.md`.
2. Read `docs/model-v2-future-branches.md`.
3. Read `VERSION.md`, especially `Current Branch Target` and `Next Planned Milestone`.
4. Read `skills/tmr-overlay-context/SKILL.md` only when broader product context is needed after the active notes and milestone file.
5. Inspect `git status --short` before editing; preserve user changes and untracked local captures.
6. For overlay or validation work, check `tools/validation/overlay-scenario-contract.json` so the active scenario coverage state is not lost between sessions.
7. Restate the relevant active notes in one or two sentences before proposing or making implementation changes.

## Updating Notes

Update `docs/model-v2-future-branches.md` when a conversation produces durable context, such as:

- a product decision or rejected approach
- app theory about telemetry sources, overlay semantics, or support diagnostics
- a new fixture/capture finding that should steer later work
- a concrete next step for the active milestone
- a warning about what not to change yet
- a scenario-contract gap that should become a compact fixture, screenshot artifact, validator rule, or app-owned behavior descriptor

Keep notes concise and dated. Do not store raw telemetry payloads, private driver identities, zip contents, or machine-specific paths there unless the path itself is necessary for local development.

## Boundaries

Do not treat `docs/model-v2-future-branches.md` notes as a substitute for tests, fixtures, or docs. When a note becomes a stable product contract, move or mirror it into the appropriate durable file during the branch-complete pass, such as `docs/overlay-logic.md`, `docs/capture-format.md`, `telemetry.md`, `VERSION.md`, or a fixture README.
