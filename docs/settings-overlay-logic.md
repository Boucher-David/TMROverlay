# Settings And Overlay Manager Logic

This file explains how the main settings window and overlay manager decide what appears.

Implementation files:

- `src/TmrOverlay.App/Overlays/OverlayManager.cs`
- `src/TmrOverlay.App/Overlays/SettingsPanel/SettingsOverlayForm.cs`
- `src/TmrOverlay.App/Overlays/Abstractions/PersistentOverlayForm.cs`

## Product Shape

The settings window is the main UI. It is a normal desktop window, not a driving overlay.

Treat settings as the app control plane rather than a v1 or model-v2 overlay consumer. Overlay v2 migrations change which live model a driving overlay reads; settings owns lifecycle, visibility, content/header/footer session gates, shared units/fonts, diagnostics access, and future app-platform controls. Settings information architecture should stay separate from telemetry model migration.

Driving overlays are managed windows that can sit above the simulator. Current driving overlays:

- Standings.
- Fuel Calculator.
- Relative.
- Track Map.
- Flags.
- Session / Weather.
- Pit Service.
- Input / Car State.
- Car Radar.
- Gap To Leader.

## Startup

At startup, `OverlayManager.ShowStartupOverlays`:

1. Loads settings.
2. Ensures managed overlay settings exist.
3. Opens the settings window.
4. Applies overlay settings to driving overlays.
5. Saves normalized settings.
6. Starts a 1 second timer that reapplies session visibility rules.

## Settings Window Placement

The settings window:

- Uses default size from `SettingsOverlayDefinition`; the current fixed client size is wide enough for the full tab list and the single-column teammate Support flow.
- Enforces its default size as both the minimum and maximum.
- Recenters every time it is opened.
- Does not preserve user-dragged placement between runs.
- Uses normal desktop/taskbar behavior by default. When the Settings window is active, it is temporarily promoted above product overlays so Settings remains clickable; after focus returns to iRacing or another app, Settings stays visible but drops out of the topmost layer, and product overlays return to their saved topmost behavior.
- Activates after being shown.

This differs from driving overlays, which persist placement through `PersistentOverlayForm`.

## Settings Tabs

The settings UI has:

- A branded `Tech Mates Racing Overlay` header with the TMR logo.
- Flat vertical left-side tabs in a taller desktop settings window so all current overlay and support tabs fit without scrolling.
- General.
- User-facing overlay tabs ordered by common race workflow.
- Support as the final tab.

Managed overlay tabs are generated from `OverlayManager.ManagedOverlayDefinitions`. Only user-facing overlay definitions should be listed there; internal app-health surfaces live in Support instead of being promoted as normal overlays. Adding a driving overlay definition there is what makes the settings tab eligible to appear; overlay-specific options only appear when the definition provides option descriptors or a custom settings block.

The tab list keeps General first, puts common race timing overlays early, keeps driver telemetry and race-state overlays after that, and leaves Support last. Future platform surfaces such as Overlay Bridge, publishing/update status, post-race analysis, and overlay builder should not be added as ordinary driving-overlay tabs without a product pass.

Flags exposes flag-category enable controls plus the shared scale control instead of content/header/footer session gates, opacity, or custom timing. The flag overlay ignores background flag bits such as serviceable/start-hidden as standalone display triggers and does not paint plain steady-state green running by itself. Multiple active display categories can be shown together, so white/black/meatball can persist while transient yellow/debris/blue states come and go. The visible renderer is icon-only; exact black-flag instruction text is deferred until a reliable telemetry source exists.

Most driving overlays expose opacity next to scale and keep the existing overlay opacity as their default. The transparent flag renderer and the radar do not expose opacity because their readability comes from semantic color/shape rather than a normal panel background.

Normal scale-capable overlays are size-controlled by their definition plus `Scale %`. Width and height remain in saved settings for compatibility and runtime placement persistence, but `OverlayManager` normalizes them from the app-decided base size and then applies scale instead of treating width/height as independent user-facing layout inputs. Track Map and Radar stay square because their definitions are square; Flags, Stream Chat, and Garage Cover also use scale-owned dimensions instead of direct width/height controls. Table overlays derive their base width from the current visible content columns before scale is applied, so compact column defaults make the overlay narrower while the scale slider remains a true visual scale. Advanced table width/order editing is deferred to the future V2 custom settings slideout.

Gap To Leader is race-only, so its settings tab omits the redundant `Display in sessions` filter group.

Overlay settings tabs contain left-side region tabs. `General` contains visibility, scale, opacity, image actions where applicable, and localhost details. It does not show a separate session-filter row; session gating lives with the content/header/footer rows it controls. `Content` owns overlay-specific display/content controls. Shared-chrome-capable overlays then expose `Header` and `Footer` sections for session-scoped chrome items. The current header region includes supported items such as time remaining, while rendered header status and footer source controls are removed; footer regions can be intentionally empty until a real footer item exists. Input / Car State suppresses Header/Footer because it is graph/content-only in this branch. These keyed options are stored per overlay through `OverlaySettings.Options`, default on where appropriate, and are intentionally narrow so future header/footer items can expand the same settings pattern without changing general overlay controls.

Each user-facing overlay tab shows a selectable localhost URL plus a copy button when a route exists. It also shows a recommended OBS source size as plain `WxH` text derived from the overlay's current content columns or default size. Localhost routes are available when `LocalhostOverlays` is enabled in configuration, and localhost-only surfaces such as Garage Cover do not expose a native `Visible` checkbox. Overlay modules own localhost route descriptors and page scripts; localhost owns the HTTP transport, route catalog, and generic HTML shell.

The app-level General tab includes a Show Preview control for Practice, Qualifying, and Race. Preview mode swaps the live telemetry source to deterministic session fixtures so native overlays and localhost routes render realistic mock session data. It does not override overlay `Enabled` state, content/header/footer session gating, positioning, scale, opacity, Stream Chat configuration, or topmost behavior; hidden overlays stay hidden unless the user has enabled them or is using an existing overlay-specific settings preview.

Standings and Relative use a shared content-column manager. The content definition has reusable data keys, but each overlay owns its own option keys so changing Standings columns does not imply Relative changes. Columns remain in one ordered list internally and keep durable width/order fields for compatibility and renderer sizing, but the current V1 Design V2 settings surface exposes only the actual visible content rows plus overlay-specific count controls. Native and localhost rendering use the same compact default content and the recommended OBS width follows visible columns instead of being floored by the old large default overlay width.

Standings exposes normal overlay visibility, scale, opacity, its localhost URL, content-row visibility, and a Multiclass sections row. The Multiclass sections row can be disabled and controls how many other-class cars are shown. The native overlay and localhost route both use those settings when they group scoring rows by class.

Track Map exposes normal overlay visibility, scale, map-fill opacity, content toggles, and map-source status. Bundled app maps are used automatically when the current track identity matches, while local IBT-derived map generation can be toggled from Support/Diagnostics as an app-level automatic capture behavior. Schema-v2 generated maps carry sector boundaries used by the live model-v2 green/purple sector highlights; the map-fill opacity setting still does not dim the white outline, markers, or sector status. The local map-building toggle controls future automatic post-session generation and whether user-generated maps are eligible at runtime; when disabled, runtime lookup uses bundled app maps before falling back to the circle placeholder. The settings tab no longer exposes one-off `.ibt` conversion controls; use `tools/TmrOverlay.TrackMapGenerator` for bundled-asset generation and QA.

Stream Chat is exposed as both a native overlay and a localhost route. The settings tab saves one active source: not configured, Streamlabs Chat Box URL, or Twitch channel. New and migrated settings default to Twitch channel `techmatesracing` through the shared settings contract. The saved values are reused on load so enabling the native overlay or opening the localhost route reconnects automatically. The native overlay supports public Twitch channel chat; Streamlabs embeds the configured widget URL in `/overlays/stream-chat`. Both chat surfaces replace the first placeholder row with either a connected message or a connection error. The inactive provider is ignored even if its field has a saved value. Native Stream Chat is no-activate and uses the same OS-level click-through style as other input-transparent overlays, with a small close button kept as an escape hatch if window behavior regresses. Provider auth, live/offline broadcaster status through Twitch Helix, moderation controls, write/chat-command support, and richer rate-limit handling are separate follow-up work. Streamlabs widget URLs are treated as private local settings and redacted from diagnostics bundles.

Garage Cover is localhost-only. Its settings tab exposes scale, the localhost URL, image import/clear in General, a static cover preview in Preview, and the current Garage/setup-screen detection state, but not native visibility, session gates, opacity, or desktop frame controls. The imported image is copied into app-owned settings storage before being served to the localhost route; preview and localhost rendering use crop-to-cover fitting so unusually large, small, tall, or wide images still fill the cover area. When no user image is available or the image cannot load, the localhost route uses the bundled stock fallback art. With fresh telemetry it appears only while iRacing reports the Garage screen as visible; when telemetry is unavailable or stale, it fails closed to the cover/fallback rather than flashing transparent.

The selected overlay tab is reported back to `OverlayManager`.

The General tab exposes the metric/imperial unit selector. User-facing font selection is intentionally hidden for now so Windows and mac review surfaces stay stable; shared typography remains a theme/platform default that can be revisited from the visual-token layer. Support capture and support paths intentionally live in the Support tab instead of General.

## Overlay Settings Normalization

For every managed driving overlay, `OverlayManager` ensures:

- An `OverlaySettings` entry exists.
- Scale is clamped from `0.6` to `2.0`.
- Width and height are valid.
- Invalid sizes are reset from the overlay default size and scale.

The gap-to-leader overlay is normalized to race-only visibility whenever managed overlay settings are ensured.

The flags overlay is normalized back to the compact procedural-flag default when old saved settings still contain the previous primary-screen border dimensions. Older compact custom sizes are converted into an approximate scale, then width/height return to definition-derived values.

Saved Garage Cover frame values from earlier tester builds are no longer used for a desktop overlay. The app keeps a scale-derived 16:9 recommendation while localhost dimensions are ultimately controlled in OBS or the host browser.

## Session Visibility

Every second, and whenever settings change, `OverlayManager`:

1. Reads the latest live telemetry context.
2. Classifies the session string into Test, Practice, Qualifying, Race, or unknown, then normalizes Test to Practice for user-facing settings gates.
3. For each driving overlay, checks:
   - Overlay enabled.
   - Allowed for current session kind.
   - Special radar settings preview state.
4. Shows or hides each overlay.

Unknown session kind is allowed by default.

Session classification is string based:

- Contains `test`: Test.
- Contains `practice`: Practice.
- Contains `qual`: Qualifying.
- Contains `race`: Race.

The settings UI only shows Practice, Qualifying, and Race. Toggling Practice applies to both Practice and Test-backed settings keys so Test sessions do not need a separate visible column.

## Radar Settings Preview

When the user selects the radar settings tab:

1. `SettingsOverlayForm` reports the selected tab id.
2. `OverlayManager` sees that the selected overlay id is `car-radar`.
3. If the radar overlay is enabled, the radar overlay is forced visible even if filtered out by session rules.
4. `CarRadarForm.SetSettingsPreviewVisible(true)` forces radar alpha to full so the user can see changes.

When leaving the radar tab, preview mode is turned off. If the radar overlay is disabled, selecting its settings tab does not override the `Visible` checkbox.

## Settings Tab Previews

The Design V2 settings surface does not show static render-preview panels for ordinary overlay tabs. Those panels are hidden until a real preview renderer exists so they do not imply live overlay rendering where none is wired. Garage Cover keeps its image preview because that tab previews the actual configured localhost cover image. Radar keeps its native settings-preview behavior through the live overlay window rather than an embedded settings-tab render.

## Scale And Units

Scale:

- Each overlay scale is clamped from `0.6` to `2.0`.
- Client size is recalculated from default dimensions and scale.
- The manager remembers the last applied scale to avoid unnecessary resizing.

Units:

- If the global unit system changes, managed overlay forms stay alive. Unit-aware overlays receive the new unit system in place and repaint values on their normal refresh cadence; non-unit overlays such as Stream Chat must not close, hide, or visibly flash.
- Unit system is either `Metric` or `Imperial`.

Typography:

- The user-facing font selector is hidden while shared typography stays a theme/platform concern.
- Managed overlays render with the platform/theme default font unless an advanced theme override is introduced.

## Settings Persistence

Driving overlays persist their placement and size through their `OverlaySettings`.

The settings window stores normalized size but is recentered on open, so saved coordinates do not control startup placement.

Settings option changes are queued briefly and coalesced before save/apply. This keeps checkbox bursts and content/header/footer edits from recursively saving settings, rebuilding overlay visibility, and refreshing browser-size readouts inside the original control event. Closing the settings window flushes any pending save/apply work before exit.

## Diagnostics In Settings

The Support tab:

- Shows visible app version/build metadata at the top of the tab, followed by teammate-oriented diagnostic capture and diagnostics bundle actions.
- Uses the shared `AppDiagnosticsStatusModel` for app status, live/session state, and current issue text so Support and diagnostics bundles explain collector health from the same priority order.
- Treats first-run/no-iRacing waiting as an expected idle state instead of an active issue.
- Keeps diagnostic telemetry at the top of the handoff flow and explains when teammates should enable it.
- Makes bundle actions primary: create diagnostics bundle, copy latest bundle path, and open the diagnostics folder.
- Shows compact current state only after the primary teammate actions: app status, live/session state, and current warning/error.
- Shows release update status/actions in the Support tab: installed/portable state, current/latest version, last checked time, and last error.
- Keeps local storage shortcuts together for logs, diagnostics, captures, and history without exposing advanced collection internals as normal teammate controls.
- Reports the latest automatic end-of-session diagnostics bundle.

Diagnostics bundles include performance and UI-freeze-watch metadata for settings and flags validation. The freeze-watch summary pulls settings save/apply timings, settings-driven overlay form recreation counts, UI timer lateness, flags refresh/render timings, overlay window bounds/topmost/click-through state, and possible settings-window input interception risk from the rolling performance snapshot. Bundles also include `metadata/live-telemetry-synthesis.json`, `metadata/flags.json`, `metadata/stream-chat.json`, `metadata/browser-overlays.json`, `metadata/session-preview.json`, `metadata/shared-settings-contract.json`, `metadata/window-z-order.json`, the shared contract/schema files, and `live-overlays/manifest.json`. `metadata/window-z-order.json` records the current foreground window, a bounded foreground-change history, and a top-to-bottom snapshot of desktop top-level windows with HWND, process, class/title, bounds, extended styles, and topmost state. Rolling live-window overlay crops are included only when `LiveOverlayWindowDiagnostics:CaptureScreenshots=true`, so support can compare expected overlay visibility with pixels captured from the desktop or the marked form-render fallback during an explicit diagnostic run. The live telemetry synthesis, flags metadata, stream-chat metadata, browser catalog, session-preview metadata, shared contract metadata, window z-order snapshot, and live overlay manifest record current focus/session context, official-position/progress coverage, overlay context gating, actual native renderer/body metadata, the full localhost route surface, recommended OBS source sizes, resolved settings defaults, preview guardrails, foreground/topmost state, provider/connection state, and Design V2 token values so diagnostics can expose native/localhost parity drift after Design V2 changes.

While the settings window is active, the transparent Flags overlay suppresses its own window even if live flags are present. This is a defensive input policy for the feedback case where an always-on-top transparent flag window appeared to make Settings unclickable.

## Design Notes

- Keep settings as a normal desktop UI.
- Keep driving overlays focused and simulator-friendly.
- Show release update available, downloading, pending restart, applying, and update-check failure states as a yellow banner above the tabs and below the main app title area. Expose explicit install/restart actions only from the settings surface and tray menu; do not show modal prompts over the simulator.
- If an overlay has preview behavior, document which settings tab triggers it.
- If new overlay visibility rules are added, update this file and the overlay-specific logic doc.
