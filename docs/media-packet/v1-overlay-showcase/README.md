# TmrOverlay V1 Overlay Showcase

This folder is a shareable media packet for teammates and product review. It uses curated, feature-rich browser-review states, including real-derived scenarios when available, but it is separate from screenshot validation artifacts and should not be treated as browser/native/localhost parity proof.

Source screenshots come from the deterministic browser-review screenshot suite. Refresh this packet after regenerating and validating browser-review screenshots:

```bash
npm run screenshots:browser-review -- --output artifacts/browser-review-screenshots
python3 tools/validate_overlay_screenshots.py --profile browser-review-ci --root artifacts/browser-review-screenshots
npm run media:packet
```

| Item | Image | Shows |
| --- | --- | --- |
| Settings - General | [app/01-settings-general.png](app/01-settings-general.png) | Shared units, update status, and deterministic preview controls. |
| Settings - Standings | [app/02-settings-standings.png](app/02-settings-standings.png) | Standings overlay visibility, sizing, browser source, and preview controls. |
| Settings - Relative | [app/03-settings-relative.png](app/03-settings-relative.png) | Relative overlay visibility, row count, sizing, browser source, and preview controls. |
| Settings - Gap To Leader | [app/04-settings-gap-to-leader.png](app/04-settings-gap-to-leader.png) | Gap trend overlay visibility, sizing, browser source, and preview controls. |
| Settings - Track Map | [app/05-settings-track-map.png](app/05-settings-track-map.png) | Track map visibility, opacity, map-building, sector boundaries, and browser source controls. |
| Settings - Stream Chat | [app/06-settings-stream-chat.png](app/06-settings-stream-chat.png) | Stream chat provider, visibility, sizing, and preview controls. |
| Settings - Garage Cover | [app/07-settings-garage-cover.png](app/07-settings-garage-cover.png) | Garage cover image import, preview, clear, visibility, and sizing controls. |
| Settings - Fuel Calculator | [app/08-settings-fuel-calculator.png](app/08-settings-fuel-calculator.png) | Fuel calculator visibility, sizing, browser source, and preview controls. |
| Settings - Inputs | [app/09-settings-inputs.png](app/09-settings-inputs.png) | Input state visibility, trace/readout content, sizing, browser source, and preview controls. |
| Settings - Car Radar | [app/10-settings-car-radar.png](app/10-settings-car-radar.png) | Car radar visibility, multiclass warning, sizing, browser source, and preview controls. |
| Settings - Flags | [app/11-settings-flags.png](app/11-settings-flags.png) | Flags overlay visibility, sizing, browser source, and preview controls. |
| Settings - Session / Weather | [app/12-settings-session-weather.png](app/12-settings-session-weather.png) | Session and weather overlay visibility, content, sizing, browser source, and preview controls. |
| Settings - Pit Service | [app/13-settings-pit-service.png](app/13-settings-pit-service.png) | Pit service overlay visibility, tire grid content, sizing, browser source, and preview controls. |
| Settings - Diagnostics | [app/14-settings-diagnostics.png](app/14-settings-diagnostics.png) | Diagnostic telemetry, local map building, support bundle, and support folder controls. |
| Standings | [overlays/01-standings-race.png](overlays/01-standings-race.png) | Multi-class race standings with class headers, focus row, gaps, laps, and pit state. |
| Relative | [overlays/02-relative-race.png](overlays/02-relative-race.png) | Nearby cars around the focus driver with lap relationship coloring and compact empty-row spacing. |
| Fuel Calculator | [overlays/03-fuel-calculator-race.png](overlays/03-fuel-calculator-race.png) | Race stint plan, current fuel, stop count, and measured/history-backed burn evidence. |
| Gap To Leader | [overlays/04-gap-to-leader-race.png](overlays/04-gap-to-leader-race.png) | Live gap trend with connected history lines, intervals, and race-context summary rows. |
| Track Map | [overlays/05-track-map-race.png](overlays/05-track-map-race.png) | IBT-derived Nurburgring 24h track shape with car markers and focus-car highlighting. |
| Track Map Fallback | [overlays/06-track-map-circle-fallback.png](overlays/06-track-map-circle-fallback.png) | Circular fallback map used when generated geometry is unavailable. |
| Session / Weather | [overlays/07-session-weather-race.png](overlays/07-session-weather-race.png) | Session, clock, lap, track, weather, wind, temperature, and atmosphere metrics. |
| Pit Service - Active | [overlays/08-pit-service-active.png](overlays/08-pit-service-active.png) | Pit signal, service status, fuel/repair requests, and per-tire service grid. |
| Pit Service - Idle | [overlays/09-pit-service-idle.png](overlays/09-pit-service-idle.png) | Pit-ready idle state with safe placeholders for unavailable tire and service values. |
| Input / Car State | [overlays/10-input-state-race.png](overlays/10-input-state-race.png) | Throttle, brake, steering, gear, speed, ABS, and trace graph evidence. |
| Car Radar | [overlays/11-car-radar-both-sides.png](overlays/11-car-radar-both-sides.png) | Both-sides proximity warning with radar rings and side-pressure arcs. |
| Flags | [overlays/12-flags-all-kinds.png](overlays/12-flags-all-kinds.png) | Full flag palette: green, blue, yellow, caution, red, black, repair, white, and checkered. |
| Stream Chat | [overlays/13-stream-chat-twitch-rich.png](overlays/13-stream-chat-twitch-rich.png) | Twitch-style chat with badges, emotes, names, timestamps, and message text. |
| Garage Cover | [overlays/14-garage-cover-visible.png](overlays/14-garage-cover-visible.png) | Garage-visible full-canvas cover image used for broadcast or stream masking. |
