# Garage Cover Overlay Logic

`Garage Cover` is a localhost streamer privacy surface for OBS use. It is not created as a native desktop overlay.

## Visibility

The overlay reads `LiveTelemetrySnapshot.Models.RaceEvents.IsGarageVisible`, which is derived from iRacing's `IsGarageVisible` telemetry value. This is intentionally different from `IsInGarage`: the cover should react to the visible Garage/setup screen, not just whether the car physics are in a garage state.

The localhost route is served at `/overlays/garage-cover`. The cover renders only when the Garage Cover overlay is enabled and fresh telemetry reports `IsGarageVisible=true`. Disconnected, not collecting, stale, waiting, and settings-preview states do not open the live cover.

Garage Cover is treated as an ordinary opt-in product surface for visibility purposes, even though it is localhost-only. A disabled Garage Cover source may still load and poll in OBS, but its model remains hidden until the user enables it in TMR settings.

The settings tab shows the configured cover image in a Preview region and keeps import/clear actions in General. The Preview region is settings-only evidence for the selected image; it does not force the live localhost cover open.

When fresh telemetry reports `IsGarageVisible=false`, or telemetry is unavailable, the localhost page fades out.

## Display

The app does not manage desktop placement or size for Garage Cover. The user sizes and crops the localhost source in OBS or another local capture tool.

The cover is always opaque. It does not expose the normal opacity control because semi-transparent setup coverage can leak private setup information.

The user can import a PNG, JPG, BMP, or GIF image. The app copies that file into app-owned settings storage under `garage-cover/cover.*` and stores that app-owned path in overlay settings. Runtime rendering loads from the app-owned copy, not the original selected file. The Garage Cover settings tab also renders a small static preview of the selected image, or the bundled stock fallback cover when no usable image is configured.

The localhost server exposes `GET /api/garage-cover` for image metadata, `GET /api/garage-cover/image` for the app-owned imported image, and `GET /api/garage-cover/default-image` for the bundled stock fallback cover. If no image is imported, the saved image cannot be loaded, or the browser cannot decode the image, the localhost page paints the stock fallback first and only drops to a black centered `TMR` text fallback if the bundled asset is unavailable. The fallback still covers setup details.

Diagnostics bundles include `metadata/garage-cover.json` with localhost route state, product enabled state, current cover decision, image status/metadata, preview state, last Garage-visible detection state, and fallback reason. The imported image itself is not copied into diagnostics bundles.

## Product Boundary

V1 is deliberately a privacy cover, not a rich broadcast panel. It should avoid showing race information that could make the setup-cover state harder to trust.

V2 can treat the Garage-visible state as a broadcast context trigger. Once model-v2 race analysis and layout primitives are stronger, the same surface could optionally show safe stream-facing information such as session timing, standings excerpts, sponsor/team art, recent stint summaries, or upcoming pit/strategy context while continuing to hide setup details.
