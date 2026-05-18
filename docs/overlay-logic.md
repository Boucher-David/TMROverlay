# Overlay Logic Docs

These files explain the current overlay behavior in English so design changes can be proposed against readable logic instead of C#.

They should be updated whenever overlay behavior, telemetry derivation, analysis rules, visibility rules, or user-facing state transitions change.

## Overlay Logic

- [Overlay Behavior Reference](overlay-behavior-reference.md)
- [Overlay Flow Diagrams](overlay-flow-diagrams.md)
- [Status Overlay Logic](status-overlay-logic.md)
- [Settings And Overlay Manager Logic](settings-overlay-logic.md)
- [Fuel Calculator Logic](fuel-calculator-logic.md)
- [Standings Overlay Logic](standings-overlay-logic.md)
- [Relative Overlay Logic](relative-overlay-logic.md)
- [Track Map Overlay Logic](track-map-overlay-logic.md)
- [Garage Cover Overlay Logic](garage-cover-overlay-logic.md)
- [Simple Telemetry Overlays Logic](simple-telemetry-overlays-logic.md)
- [Car Radar Logic](car-radar-logic.md)
- [Gap To Leader Logic](gap-to-leader-logic.md)

## Related Analysis Logic

- [Live Model Groundwork](live-model-groundwork.md)
- [Live Overlay Diagnostics](live-overlay-diagnostics.md)
- [24-Hour Live Overlay Findings](archive/live-overlay-24h-findings.md)
- [Model V2 Future Branch Notes](model-v2-future-branches.md)
- [Edge-Case Telemetry Logic](edge-case-telemetry-logic.md)
- [IBT Analysis](ibt-analysis.md)
- [Post-Race Analysis Logic](post-race-analysis-logic.md)

## Design V2 Overlay Styling

Relative, Standings, Track Map, Car Radar, and the simple Flags, Session / Weather, Pit Service, and Input / Car State overlays now consume model-v2 state directly for their promoted contracts. The production native overlays default to the Design V2 shell, with `TMR_WINDOWS_DESIGN_V2_LIVE_OVERLAYS=false` kept as an opt-out while live testing finishes. Localhost pages use the same V2 visual shell and compact telemetry-first styling so OBS/localhost matches the native overlay direction. Shared Design V2 role colors start in `shared/tmr-overlay-contract.json`; native overlays and localhost CSS read those tokens so changing a shared hex value updates the product surfaces after restart. Browser review is the local development screenshot surface, localhost screenshots validate OBS/browser-source routes, and Windows screenshots remain the native production evidence. The tracked mac harness is deprecated secondary scaffolding and is not the current parity target.

Treat overlay UI/style v2 as telemetry-first by default: standings, relative, local in-car radar/blindspot, flags, session/weather context, and timing tables should be dense, stable windows into iRacing telemetry. Normal rows should not look like confidence reports, and persistent source footers are not part of current end-user overlay chrome. Normalized source, quality, usability, freshness, and missing-reason states remain model/manifest/diagnostic evidence and can become exception UI only for stale, unavailable, modeled, or derived values, especially in analysis products like fuel strategy, non-local radar focus/multiclass interpretation, gap graphs, and a future pit crew/engineer control overlay.

Competitor overlay analysis remains the product-shape check: small purpose-built overlays, dense information, low-noise dark styling, and semantic color rather than one monolithic dashboard. Browser review is the fast local iteration loop, localhost is validated as its own route surface, and the combined local screenshot artifact can be used for side-by-side review before Windows CI produces native artifacts. The tracked mac harness remains only as deprecated secondary scaffolding for old `mocks/design-v2/` states and native-shell experiments; it is no longer treated as part of the product parity gate. Design V2 also has current/default and outrun token sets plus generated component-review artifacts for overlay shells, controls, buttons, status pills, table rows, graph chrome, localhost blocks, sidebar tabs, section panels, and settings content blocks. Windows has an additive WinForms V2 settings surface over the existing `ApplicationSettings`, `OverlaySettings`, content descriptors, localhost routes, and support workflows rather than replacing those contracts. Tracked mocks and Windows CI artifacts include cropped settings-component screenshots for tabs, buttons, inputs, content matrices, and localhost blocks, so platform drift can be reviewed at primitive scale.

Race-data overlays opt into a shared live-telemetry fade through `OverlayAvailabilityEvaluator`. When the app is disconnected, not collecting, or the latest normalized live snapshot is stale, those overlays fade out instead of retaining stale race state on screen. Native overlays, simple telemetry view models, Garage Cover detection, and localhost routes now use the same connected/collecting/fresh terminology where practical. Status and Stream Chat are not treated as race-data overlays for this policy. Garage Cover is localhost-only and still fails closed to the configured cover or fallback when telemetry is unavailable, stale, or the Garage screen is visible.

Native overlay visibility also honors descriptor-level context requirements before a managed window is shown. Most race overlays can render against any fresh telemetry focus. Radar and Inputs are local-player in-car surfaces for V1, while Fuel Calculator and Pit Service require the local player to be the current focus and to be in an active in-car or pit context. When those requirements are not met, the overlay remains hidden even if its Visible toggle is on; diagnostics record the requirement, availability, and reason instead of forcing the overlay visible.

The browser review server is now the primary local parity surface for browser UI and broad app-layout review. `/review/app` renders the settings shell with a full overlay validator stage, `/review/settings/general` mirrors the General tab preview surface, `/review/overlays/<overlay-id>` renders browser review pages, and `/overlays/<overlay-id>` renders localhost pages from the same assets used by the Windows localhost server. Localhost overlay pages use `/api/overlay-model/{id}` as their runtime contract and no longer bootstrap from the legacy snapshot helper. This replaces most routine mac harness work for browser layout and JavaScript behavior, but Windows remains authoritative for native overlay windows, focus/topmost/click-through behavior, iRacing SDK capture, installer/update behavior, and real WinForms screenshot artifacts.

## Maintenance Rule

When implementation changes:

1. Update the matching logic doc in the same pass.
2. Search docs, mocks, tests, skills, browser review assets, and localhost routes for old behavior names or old assumptions.
3. Regenerate screenshots when visual overlay behavior changed.
4. Run screenshot validation after regenerated artifacts are written.
