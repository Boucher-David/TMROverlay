# TmrOverlay Media Packets

Shareable product screenshots live here. These images are for teammate-facing review and communication, separate from CI screenshot validation artifacts.

Refresh the V1 overlay showcase from the current browser-review screenshot artifact:

```bash
npm run screenshots:browser-review -- --output artifacts/browser-review-screenshots
python3 tools/validate_overlay_screenshots.py --profile browser-review-ci --root artifacts/browser-review-screenshots
npm run media:packet
```

The generated packet includes its own `manifest.json` with source screenshot paths, dimensions, and status labels.
