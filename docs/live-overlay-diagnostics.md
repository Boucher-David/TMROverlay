# Live Overlay Diagnostics

`live-overlay-diagnostics.json` is an opt-in passive observer artifact for the current fuel, radar, gap, timing, flags, and design-v2 candidate overlays. It is intended to test overlay assumptions from real sessions before model-v2 behavior changes are made while keeping raw capture opt-in.

The recorder does not change overlay output. It watches normalized live snapshots and writes bounded summaries for:

- gap semantics: race vs non-race frames, class-gap source counts, large gaps, gap jumps, class row availability, and pit context
- scoring/source coverage: scoring source counts (`None`, `StartingGrid`, `SessionResults`), max scoring row/class-group counts, and max result/live-scoring/live-timing/live-spatial coverage counts
- focus context: unavailable focus frames, raw `CamCarIdx`, reason counts, session kind/state counts, on-track/garage/pit context, and bounded `focus.unavailable` examples
- radar semantics: focus kind, local-only suppression, pit/garage unavailability, local progress gaps, side-signal frames, side signal without placement candidates, timing-only vs spatial placement coverage, nearby-car counts, and multiclass approach frames
- fuel and pit-service semantics: valid level frames, instantaneous burn frames, burn-without-level frames, team timing without local fuel, pit context, driver-control changes, pit-service signal/request/change frames, whether those pit-service signals occur while focus is on another car or the user is off track, and local-strategy suppression reasons for the V1 Fuel/Pit overlays
- position cadence: sampled position/class-position changes that happen before the car completes another lap
- lap-delta readiness: availability and `_OK` usability counts for live iRacing delta channels such as best lap, optimal lap, session best, session optimal, and session last lap
- relative lap-relationship probe: diagnostics-only counts for official completed-lap relationships among nearby cars, pit-road relationship counts, and same-lap cars near the wrap where a future branch might infer "about to lap" or "about to be lapped" behavior
- sector-timing readiness: session-info sector metadata coverage, focus/ahead/behind progress coverage, missing lap-counter frames, synthetic start/finish wraps, progress discontinuities, derived sector-boundary crossings, and bounded examples of completed sector intervals derived from car progress
- track-map sector highlights: model-v2 sector availability, live timing frames, personal-best sector frames, best-lap sector frames, full-lap highlight frames, and highlight counts by status
- flags semantics: raw `SessionFlags` coverage, active raw flag frames, displayable flag frames, enabled display flag counts, and bounded examples of active flag state

## Output Locations

When raw capture is active, the artifact is written beside the raw-capture sidecars:

```text
captures/capture-*/live-overlay-diagnostics.json
```

When raw capture is not active, it is written under the logs root:

```text
%LOCALAPPDATA%\TmrOverlay\logs\overlay-diagnostics\*-live-overlay-diagnostics.json
```

The deprecated mac harness has a secondary diagnostics mirror under `~/Library/Application Support/TmrOverlayMac/logs/overlay-diagnostics/` and can also write into mock capture directories, but Windows diagnostics remain the product path.

## Guardrails

- Disabled by default for normal builds; enable it with `LiveOverlayDiagnostics:Enabled=true` or a `TMR_LiveOverlayDiagnostics__Enabled=true` override when a session should write this observer artifact.
- Bounded by sampled frame and event caps.
- Event examples are exact-duplicate suppressed and capped per kind before the global cap, so a stable condition such as a multi-lap class gap cannot crowd out unrelated radar/fuel/position examples.
- Frame and event examples include session state, on-track/garage/pit context, raw `CamCarIdx`, focus-unavailable reason, player car index, focus car index, scoring source, scoring row/class-group counts, and coverage row counts so startup/degraded telemetry can be separated from real focus or source-selection bugs.
- Radar event examples include the focus kind, raw `CarLeftRight`, raw nearby-car count, whether the production radar had data, and nearby/timing/spatial row counts. This lets suppressed spectator/teammate focus and other partial radar cases be reviewed from the capture without making them normal overlay UI.
- Relative lap relationship examples are probe-only. They use raw nearby `CarIdxLapCompleted` and `CarIdxLapDistPct` against the current focus progress to help decide a later Relative V2.5 color treatment; current overlays do not consume these counts.
- Sector timing interval examples are derived diagnostics only for future timing-table work. They can use valid `LapDistPct` when lap counters are unavailable, but large reset-style progress jumps are counted as discontinuities instead of completed sectors. Track Map sector highlight state is now a production model-v2 contract under `LiveTelemetrySnapshot.Models.TrackMap`.
- Best-effort: failures are logged and must not stop live telemetry, history, raw capture, IBT analysis, or overlays.
- Additive: older captures without this file remain valid.
- Diagnostic only: event counts are evidence for future branches, not automatic behavior changes.

Use it with `live-model-parity.json`, `ibt-analysis/ibt-local-car-summary.json`, and `captures/_analysis/raw-capture-overlay-assumptions.json` when deciding whether model v2 is ready to power overlays.

Settings/Flags freeze triage is intentionally separate from `live-overlay-diagnostics.json`. Diagnostics bundles include `metadata/ui-freeze-watch.json`, which summarizes rolling performance data for settings save/apply churn, settings-driven overlay form recreation counts, UI timer lateness, flags refresh/render timings, and overlay window click-through/topmost/no-activate/input-intercept state. They also include `metadata/live-telemetry-synthesis.json`, `metadata/flags.json`, `metadata/stream-chat.json`, `metadata/window-z-order.json`, `metadata/browser-overlays.json`, `metadata/session-preview.json`, `metadata/shared-settings-contract.json`, the `shared/` contract/schema files, and `live-overlays/manifest.json`. `metadata/window-z-order.json` is a Windows desktop snapshot: it records the current foreground HWND, a bounded foreground-change history for Alt+Tab/focus triage, and top-level windows in observed z-order with process, title/class, bounds, extended styles, and topmost state. The live telemetry synthesis file is always bundled and captures the current `CamCarIdx`/focus context, player/focus car snapshots, session phase, official-position vs progress/timing coverage counts, flags model summary, local in-car/pit context decisions, and desired overlay visibility decisions without including raw telemetry payloads. `metadata/flags.json` records the same current Flags display summary without needing to inspect synthesis, and `metadata/stream-chat.json` records provider, route, connection, and retained-message diagnostics without copying private Streamlabs widget URLs. Rolling live-window overlay PNGs are opt-in through `LiveOverlayWindowDiagnostics:CaptureScreenshots=true`; when present, each screenshot entry is marked as a desktop screen crop or a form-render fallback. Records include the actual native form, native renderer, Design V2 body kind when applicable, matching localhost route, recommended OBS source size, and context requirement/availability/reason so native/localhost parity and local-context gating issues are visible in the bundle. Settings active state is only reported when the Settings window is visible; hidden Settings windows must not keep `settingsOverlayActive=true` in freeze-watch or live-overlay diagnostics. `metadata/browser-overlays.json` lists the full localhost route catalog, including localhost-only overlays such as Garage Cover, while `metadata/session-preview.json` records whether General-tab Show Preview was active and confirms it does not override overlay enabled state or content/header/footer session gates. Managed overlays are no longer forced visible for diagnostics; rolling crops only represent overlays that the app is actually trying to show through normal settings/session state, local-context gating, or an explicit settings preview. The desktop/form screenshot work is throttled so one settings-apply tick cannot capture every overlay synchronously. `screenshotRepresentsCurrentState` only means the PNG still matches the current native window bounds, opacity, visibility, and settings-overlay capture mode. Use `screenshotAgeSeconds` to judge whether live telemetry content may have moved since the latest rolling crop. Use those files with `metadata/performance.json` and recent logs when validating Windows reports where enabling Flags, changing units, Stream Chat, or Standings appears to freeze, flash, or block the Settings UI.

Installer/update residue triage is also bundled separately. Diagnostics bundles include `metadata/installer-cleanup.json`, which records whether startup legacy cleanup ran, which stale `TechMatesRacing.TmrOverlay` package folders or shortcuts were removed, and which paths were skipped. Use it when a tester reports that a Start Menu or Desktop shortcut opens an older installed build after an MSI update.
