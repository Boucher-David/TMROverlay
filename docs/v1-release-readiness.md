# V1 Release Readiness

This is the first-pass teammate handoff for the V1.0 private team build. Use it with [Windows Releases](windows-release.md) and [Overlay Behavior Reference](overlay-behavior-reference.md).

## Scope

V1.0 is a Windows iRacing companion app for team-only testing. It is intended to be useful in real sessions, but it is not the end state for strategy, broadcast, replay, or advanced teammate workflows.

Included V1 surfaces:

- Windows tray app and fixed-size Settings window.
- Native overlays: Standings, Relative, Track Map, Fuel Calculator, Flags, Session / Weather, Pit Service, Input / Car State, local in-car Radar, Gap To Leader, and Stream Chat.
- Localhost OBS routes for supported overlays, including Garage Cover.
- Velopack MSI install/update assets plus portable zip fallback.
- Support bundles, rolling logs, performance snapshots, live overlay diagnostics, compact capture sidecars, and opt-in raw telemetry capture.

Not V1 product scope:

- Overlay Bridge or teammate-to-teammate data sharing.
- Post-race analysis as a normal user workflow.
- Fuel rhythm optimization, tire-service advice, or command-capable pit controls.
- Advanced spotter/engineer views, VR, or broad broadcast timing products.
- The deprecated tracked macOS harness as a release, parity, or screenshot gate.

## Fuel Calculator V1

The fuel calculator is intentionally conservative for V1:

- It only renders in local player in-car or pit context.
- It stays hidden when focus is on another car, focus is unavailable, the player car is unavailable, garage/setup context is active, or telemetry is stale/disconnected.
- Current fuel level can update on grid, pre-green, green, pit road, and pit stall frames.
- Strategy burn updates only from completed valid green-lap fuel-level deltas.
- Fuel consumed before green lowers current fuel, but it does not count as completed-lap burn evidence.
- `FuelUsePerHour` remains diagnostic/current-fuel context and does not become fuel-per-lap strategy evidence.
- Practice and qualifying can show measured completed-lap usage as min/avg/max plus sample count, but those rows are observational and do not use partial-lap, instantaneous, or historical fallback data.
- Exact car/track/session history can fill burn-rate gaps until the current session has measured green-lap evidence.
- Rhythm optimization and tire-service advice are hidden until a V1.x/V2 fuel pass has stronger source buckets.

Known teammate telemetry limitation:

- Local scalar fuel channels are reliable for the local driver window.
- During teammate stints or focus-on-other-car contexts, team progress and `CarIdx*` arrays can remain useful, but direct scalar fuel can disappear, become zero, or stop representing the active car state.
- V1 does not render modeled active strategy in those contexts. Completed stint history can still be stored for future modeling.

## Teammate Test Flow

1. Install the MSI from the release artifact unless testing the portable fallback.
2. Start iRacing first when testing live telemetry behavior.
3. Launch TmrOverlay from the Desktop or Start Menu shortcut.
4. Open Settings from the tray icon.
5. Enable only the overlays being tested.
6. For OBS, add localhost browser sources from the Settings size hints and localhost route list.
7. If something is wrong, create a Support diagnostics bundle before closing the app.

When reporting an issue, include:

- Release tag or build version.
- Overlay name and whether it was native Windows, localhost/OBS, or browser review.
- Session type, car, track, and whether the user was driving, in pits, in garage, spectating, replaying, or a teammate was driving.
- Expected behavior, actual behavior, and whether it changed after a pit stop, driver swap, caution, session transition, or Settings toggle.
- Diagnostics bundle. Use raw capture only when intentionally asked for deeper telemetry evidence.

## Validation Gates

Before tagging V1.0:

- Windows CI restore/build/test must pass.
- Browser review tests must pass.
- Localhost tests must pass.
- Screenshot expectation validation must pass.
- Windows-rendered overlay screenshots must be generated and validated in CI.
- Windows installer screenshots must be generated and validated in CI.
- Package publish dry run and Velopack MSI dry run must pass.
- Durable data-contract compatibility tests must prove the previous release snapshot still loads and maps.
- A real Windows/iRacing smoke test should verify native focus, topmost, click-through/no-activate behavior, installer install/update, localhost OBS routes, and support bundle contents.

Local non-Windows validation is useful for browser/localhost/product-contract checks, but it does not replace the Windows .NET and WinForms gates.

## Accepted V1 Rough Edges

- Unsigned builds may trigger Windows SmartScreen or antivirus warnings.
- Fuel strategy waits for better evidence instead of guessing from instantaneous burn.
- Teammate-driving fuel strategy is intentionally hidden rather than modeled live.
- Track maps depend on bundled/generated map availability and can fall back to a placeholder.
- Installer chrome follows Velopack/MSI constraints and is only styled as close to the app design as practical.
- Some future-facing docs and model notes remain in the repo for V1.x planning, but they are not teammate-facing product surfaces.

## Release Checklist

- Confirm `VERSION.md` and `Directory.Build.props` match the intended tag.
- Confirm `README.md`, `docs/windows-release.md`, and this file agree on install/update/support flow.
- Merge the release branch to `main` only after CI is green.
- Create the annotated tag on the release commit.
- Verify GitHub Release assets include the MSI, Velopack packages/feed, portable zip, checksum, manifest, and generated release notes.
- Install the MSI on a teammate Windows machine and create one diagnostics bundle from the installed app.
