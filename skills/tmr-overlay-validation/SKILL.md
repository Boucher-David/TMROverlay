---
name: tmr-overlay-validation
description: Use when validating tmrOverlay changes, especially before handing code back from a non-Windows machine or when .NET build/test cannot run locally. Consolidates repo validation from AGENTS.md, README.md, CI, docs, and local static checks for C# compile-shape hazards.
---

# TmrOverlay Validation

Use this skill before finalizing code changes in this repo. Start with the checks that match the files changed, then report anything that could not run on the current machine.

## Always Run

```bash
git diff --check
```

```bash
rg -n "^(<<<<<<<|>>>>>>>|=======)$" --glob '!captures/**' --glob '!captures-latest/**' --glob '!local-mac/TmrOverlayMac/.build/**' --glob '!**/*.png' --glob '!**/*.jpg' --glob '!**/*.bin'
```

```bash
python3 tools/validate_overlay_screenshots.py --profile windows-expectations
```

## Branch-Complete Release Hygiene

Run this section when a branch is being declared complete, prepared for merge, or promoted to a product milestone.

Make branch-readiness artifacts current before writing the final squash text:

- Run a stale-reference sweep across docs, mocks, tests, browser review assets, localhost routes, repo skills, and release notes for old behavior names, settings tabs, option labels, source labels, screenshot assumptions, and API call patterns that no longer match the branch implementation.
- Patch affected behavior docs as part of this branch-complete pass. Overlay behavior changes should update `docs/overlay-logic.md` and the matching overlay-specific logic note. App/settings/platform changes should update README/current-state docs when the product shape changed. Product direction, validation assumptions, analysis assumptions, live-model contract notes, and future-branch notes should land here unless the change is a durable raw-capture/schema change that requires same-pass docs.
- If changing Windows build/release commands, make `README.md`, release-command docs, CI docs, and any in-app command references agree during this branch-complete pass.
- If changing Windows publishing/package contents, verify PR validation runs a publish dry run with the same package audit, the release workflow audits the publish folder for accidental repo/dev-folder leaks, emits a package manifest, and documents which user data stays outside the install folder.
- For overlay, settings UI, browser route, localhost route, preview fixture, or native renderer changes, update the screenshot generator and `tools/validate_overlay_screenshots.py` coverage expectations in the same pass. A new overlay/settings tab/region/preview state is not complete until native Windows, browser review, and localhost screenshot validation profiles either include it or explicitly justify why it is not a product surface.
- For overlay or settings UI changes, regenerate browser review screenshots with `npm run screenshots:browser-review -- --output artifacts/browser-review-screenshots`, run `python3 tools/validate_overlay_screenshots.py --profile browser-review-ci --root artifacts/browser-review-screenshots`, and inspect the uploaded `browser-review-screenshots` artifact from CI. Regenerate localhost route screenshots separately with `npm run screenshots:localhost -- --output artifacts/localhost-screenshots`, run `python3 tools/validate_overlay_screenshots.py --profile localhost-ci --root artifacts/localhost-screenshots`, and inspect the uploaded `localhost-screenshots` artifact from CI. Browser review and localhost are separate screenshot authorities; native Windows is still validated separately.
- For live-model, snapshot-reader, data-contract, or overlay data-mapping changes, do not expand the default screenshot generator to every historical snapshot. Add or update a targeted data-snapshot render sweep instead: an allowlisted fixture manifest, a dedicated screenshot command/profile, manifest metadata for snapshot id and model source, and validation that each selected fixture renders through browser and localhost routes. Keep this separate from the always-on deterministic preview matrix until it has proven stable enough to gate PRs.
- Legacy tracked screenshots under `mocks/` remain validation/design artifacts until retired. Keep `python3 tools/validate_overlay_screenshots.py` passing, but do not treat the deprecated mac harness as the screenshot generator for new V1 parity work.
- Windows-rendered screenshots are CI artifacts generated from real WinForms forms with `tools/TmrOverlay.WindowsScreenshots`. Run `python3 tools/validate_overlay_screenshots.py --profile screenshot-expectations` before CI to catch stale expected dimensions and missing native/browser/localhost screenshot contracts after overlay or settings changes. On Windows, run `dotnet run --project .\tools\TmrOverlay.WindowsScreenshots\TmrOverlay.WindowsScreenshots.csproj --configuration Release -- artifacts\windows-overlay-screenshots` and `python tools\validate_overlay_screenshots.py --profile windows-ci --root artifacts\windows-overlay-screenshots`; pass an MSI path after the output folder when local installer screenshots should be bundled under `artifacts\windows-overlay-screenshots\installer`. On non-Windows machines, inspect the uploaded PR artifact after CI.
- For localhost or telemetry-backed overlay changes, run the aligned race-start Chrome validation across the whole browser overlay catalog. Use or generate replay JSONs that cover the same race segment for each corpus source: roughly `-120s` before green, green, and `+120s` after green. Start `tools/browser-review/standings-replay-server.mjs` for each aligned replay, then run `npm run validate:browser:race-start -- http://127.0.0.1:<port> <artifact-dir>`. The validator must produce one PNG screenshot per overlay/frame and `race-start-overlay-validation.json`; missing, tiny, non-PNG, or dimensionless screenshots fail the run and are summarized in `screenshotArtifacts`. Keep both the generated screenshots and the JSON report with the branch review artifacts. This complements deterministic screenshots because it exercises shared browser chrome, header growth, replay timing, settings-backed overlays, canvas/SVG overlays, and all live-refresh API paths against one synchronized telemetry clock.
- In browser review replay validation, spoof settings/UI API state when needed to exercise column visibility, row limits, header/footer toggles, scale, opacity, and content/header/footer session gates. Do not spoof normalized overlay data fields such as relative gaps, lap relationships, class positions, or timing evidence when real captured or live-model data can drive the behavior. Keep replay-clock labels such as pre-green/green frame offsets separate from overlay header/status fields; when validating real header changes or states, assert the production model's `headerItems`/`status` output instead of accepting harness metadata. Browser review validation should prove that production contracts render correctly under chosen settings, not that hand-authored overlay-model rows look plausible.
- For real-capture browser replay work, derive overlay models from `frame.live.models` or exported production-shaped overlay models. Road Atlanta, the 4h capture, and the 24h mid-race rejoin capture are the preferred scenario set when available because they cover race start/pit-start behavior, sustained race progression, and no-history mid-session joins. Export graph/stream validation replays at dense raw telemetry cadence and check the replay status/source cadence metadata before trusting screenshots. Gap To Leader samples separated by more than the production 10s missing-segment threshold should fail validation unless they represent explicit missing telemetry; re-export denser raw frames instead of patching overlay data, graph points, or segment flags to make sparse samples look connected. Demo rows are acceptable only for non-capture review fixtures and must not be treated as live-behavior validation.
- Tester-facing documentation screenshots under `docs/assets/` should be regenerated when release/install/support handoff steps change. The current Windows release tutorial is generated with `swift tools/render_release_tutorial.swift` and validated with `python3 tools/validate_overlay_screenshots.py --profile release-tutorial --root docs/assets`.
- Treat screenshot changes as validation artifacts. If screenshots cannot be regenerated on the current machine, say so explicitly and do not describe the branch as fully ready without that gap.
- Re-run `git diff --name-status "$(git merge-base main HEAD)"..HEAD` after docs and screenshots are current so `VERSION.md` and the final handoff text describe the actual final branch contents.

Inspect the branch shape and the text that GitHub is likely to expose in a PR squash:

```bash
git fetch origin --tags
git status --short --branch
git log --reverse --oneline "$(git merge-base main HEAD)"..HEAD
git diff --name-status "$(git merge-base main HEAD)"..HEAD
git tag --list --sort=version:refname -n2
```

Sanitize the first branch commit / squash text before handoff. The first commit message or planned PR squash title should not be a throwaway message such as "more changes", "fix", "done", "one diagnostic", "Even more overlays", or an accidental-main note. It should be versioned, product-facing, and specific enough to explain the branch without relying on private conversation context.

For branch-complete handoff:

- Update `VERSION.md` with the target version, suggested squash title, and squash body.
- Ensure the suggested title uses `vMAJOR.MINOR.PATCH`, for example `[v0.7.0] Add simple model-v2 overlays and design-v2 preview states`.
- Ensure the body summarizes the actual branch diff, including product behavior, data/schema/diagnostic changes, docs/validation artifacts, CI/release-pipeline changes, and any user-data compatibility impact.
- Re-read the final branch commit list and `git diff --name-status` after late fixes. If CI, validation, version metadata, build metadata, or follow-up bug fixes were added after the original release note was drafted, update `VERSION.md` again before final handoff.
- Keep the final answer's recommended squash title/body identical to the current `VERSION.md` suggestion unless explicitly calling out a requested alternative.
- Remove or generalize sensitive local details from the squash text: raw local paths, private diagnostic file names, uncommitted capture names, accidental branch-operation notes, and private conversation shorthand.
- Ensure `Directory.Build.props` `VersionPrefix` matches the branch target version when the branch is intended to produce that app milestone.
- Before publishing a milestone, verify persistent AppData compatibility: settings migrations still load old settings, history maintenance backs up/migrates or skips old/future/corrupt history safely, and any durable user-data schema change updates version constants, migrations or compatible readers, tests, and docs.
- Protect released data-contract snapshots under `fixtures/data-contracts/v*/`: do not edit older release snapshots during normal branch work. If an older snapshot is wrong, make the correction explicit in the branch summary. Durable schema branches should add a new snapshot for the new release while keeping the previous release snapshot in place.
- Run the versioned data-contract snapshot tests during branch-complete validation so the previous released snapshot under `fixtures/data-contracts/` loads into the current app and maps into browser, localhost, and native overlay consumers. On Windows this is `dotnet test .\tests\TmrOverlay.App.Tests\TmrOverlay.App.Tests.csproj --filter DataContracts`, and Windows CI has a named `Data contract snapshot tests - localhost/native` step for the same gate. Browser review mapping runs in the dedicated browser test step. If this cannot run locally, call out that Windows/CI must run it before release.
- If a data-contract snapshot test fails, investigate the contract first: decide whether the fixture is wrong, production mapping/reader behavior regressed, or the assertion is imprecise. Only update the assertion after that diagnosis is clear.
- Do not create the release tag on an unmerged feature branch unless the user explicitly declares that branch as the release point.

For a milestone already on `main`, create and push an annotated tag after confirming the release commit:

```bash
git tag -a vX.Y.Z <commit> -m "vX.Y.Z - Short release title" -m "Short release summary."
git push origin vX.Y.Z
```

Use `git push --force-with-lease` only for an explicitly approved repair of an accidental main push, and preserve the work on a branch before moving `main`.

## Local C# Compile-Shape Check

When any `.cs` file changes, run the local scanner. This is the main C# validation available on machines without `dotnet`:

```bash
python3 skills/tmr-overlay-validation/scripts/check-csharp-member-duplicates.py
```

This is a broad static pass for C# compile-shape hazards that compile catches but plain source review often misses. It should look for generated or implicit members colliding with explicitly declared members, primary-constructor signatures that reference declarations that are only available inside the type body, targetless nullable conditional expressions, and known external enum member typos for third-party APIs used by the app.

Examples of the kind of issue this should catch:

- A positional record parameter creates `SkiesLabel`, then the same record declares a helper method named `SkiesLabel`.
- A nested record primary constructor takes `ActivityBadge`, but `ActivityBadge` is declared inside that nested record body, outside primary-constructor scope.
- A field, property, event, nested type, or primary-constructor generated property reuses a member name in the same type in a way that is not ordinary method overloads.
- A `var` local uses a conditional expression with a value-type-looking branch and a plain `null` branch, such as `var meters = trackKm is { } km ? km * 1000d : null;`. Use an explicit nullable local type or cast the null branch, such as `: (double?)null`.
- A known third-party enum reference uses a member not present in the local validation contract, such as `TelemCommandModeTypes.ToStart` instead of `TelemCommandModeTypes.Start` for `irsdkSharp` 0.9.0.

When editing snapshots/read models, prefer helper names that describe the operation, such as `FormatSkiesLabel` or `DetermineDeclaredWetSurfaceMismatch`, instead of names identical to public generated properties.

Before trusting this scanner after changing it, run:

```bash
python3 skills/tmr-overlay-validation/scripts/check-csharp-member-duplicates.py --self-test
```

## Windows .NET Gate

When `dotnet` is available, use the same production gate as CI:

```powershell
dotnet restore .\tmrOverlay.sln
dotnet build .\tmrOverlay.sln --configuration Release --no-restore
dotnet test .\tmrOverlay.sln --configuration Release --no-build
```

For quick app-focused checks during Windows development, use:

```powershell
dotnet build .\src\TmrOverlay.App\TmrOverlay.App.csproj -c Debug
dotnet test .\tmrOverlay.sln
```

If `dotnet` is not available locally, state that clearly. The local scanner is a fallback, not a replacement for Windows build/test verification; Windows compile errors can still appear for API surface mismatches the scanner does not model.

## Python Tools

When touching `tools/analysis/*.py`, run `py_compile` over the touched Python tools. For the current repo tools this is usually:

```bash
env PYTHONPYCACHEPREFIX=/tmp/tmr-pycache python3 -m py_compile tools/analysis/export_history_from_capture.py tools/analysis/yaml_forensics.py
```

If additional Python analysis tools exist in the checkout, include them in the same command rather than leaving changed tools uncompiled.

## Deprecated macOS Harness

The tracked macOS harness is deprecated secondary scaffolding. It is not a V1 release gate, screenshot authority, or product parity target. When deliberately touching `local-mac/TmrOverlayMac/`, run from that directory when the local toolchain supports it:

```bash
swift build
```

```bash
swift test
```

If the active developer directory is Command Line Tools and `swift test` fails with `no such module 'XCTest'`, use the full Xcode toolchain instead of treating that as a validation blocker:

```bash
DEVELOPER_DIR=/Applications/Xcode.app/Contents/Developer swift test
```

If that rerun is blocked by local sandboxing or permissions, request approval to run it outside the sandbox and report the exact result.

For overlay behavior changes, prefer browser review and Windows/native validation. Only run a live mac-harness smoke test when the change explicitly maintains that secondary scaffold and a GUI session is available. Use an isolated app-data root so the check does not disturb normal local settings/history:

```bash
TMR_MAC_APP_DATA_ROOT=/tmp/tmroverlay-live-validate ./run.sh
```

Let it run long enough to connect and start mock live analysis, then confirm:

- the process is running as `TmrOverlayMac`
- the log contains mock connection and collection-start entries
- `logs/performance/*.jsonl` shows nonzero telemetry frames and overlay refresh data
- when overlay diagnostics are enabled for the smoke run, `logs/overlay-diagnostics/*-live-overlay-diagnostics.json` is being written

This live smoke test only checks the deprecated mac scaffold. Browser-generated screenshots plus Windows/native validation are the product checks. If screen capture or Accessibility permissions are unavailable from the shell, report that limitation and rely on process/log/diagnostic evidence for the mac-only check.

## Change-Specific Checks

- For telemetry-backed overlay behavior, use the corpora under `fixtures/telemetry-analysis/` as pre/post validation sources. Before the change, inspect the live-state and SDK availability corpora to confirm the fields and session states are known from captured evidence, and run `python3 tools/analysis/check_sdk_schema_against_corpus.py` when local captures are available to catch SDK fields or declared shapes that are new to the tracked corpus. After the change, add or update compact redacted corpus entries/tests so the behavior is validated against known information instead of guessed fallback data. Keep raw `telemetry.bin`, source `.ibt`, full session YAML, and private driver/team identity values out of tracked fixtures.
- Keep public snapshot member names stable unless the requested change intentionally updates the contract.
- If changing shared live models or normalized snapshot contracts, keep existing overlay-specific members additive/stable, add focused builder tests, and do not change durable history/raw-capture schemas unless explicitly required.
- If changing raw capture format, verify `docs/capture-format.md`, `telemetry.md`, and `README.md` were updated in the same pass.
- If changing IBT capture or analysis behavior, verify `docs/ibt-analysis.md` when present, `docs/capture-format.md`, `telemetry.md`, and `README.md` stay aligned. IBT analysis must remain a compact sidecar: no source `.ibt` copy by default, bounded candidate scanning, file-size and stability checks, timeout/sample limits, and skipped/failed status files instead of failures in compact history, post-race analysis, or capture synthesis.
- If changing shared contracts, reusable overlay behavior, or app boilerplate, inspect whether the Windows native app, browser review, and localhost routes need matching changes. Update the tracked mac harness only when deliberately maintaining that deprecated scaffold.
