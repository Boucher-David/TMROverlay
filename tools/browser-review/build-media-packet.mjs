#!/usr/bin/env node
import {
  copyFileSync,
  existsSync,
  mkdirSync,
  readFileSync,
  rmSync,
  writeFileSync
} from 'node:fs';
import { dirname, join, relative, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const repoRoot = resolve(dirname(fileURLToPath(import.meta.url)), '../..');
const args = parseArgs(process.argv.slice(2));
const sourceRoot = resolve(repoRoot, args.source || 'artifacts/browser-review-screenshots');
const outputRoot = resolve(repoRoot, args.output || 'docs/media-packet/v1-overlay-showcase');
const sourceManifestPath = join(sourceRoot, 'manifest.json');

const appItems = [
  {
    source: 'settings/general.png',
    output: 'app/01-settings-general.png',
    title: 'Settings - General',
    description: 'Shared units, update status, and deterministic preview controls.'
  },
  {
    source: 'settings/standings.png',
    output: 'app/02-settings-standings.png',
    title: 'Settings - Standings',
    description: 'Standings overlay visibility, sizing, browser source, and preview controls.'
  },
  {
    source: 'settings/relative.png',
    output: 'app/03-settings-relative.png',
    title: 'Settings - Relative',
    description: 'Relative overlay visibility, row count, sizing, browser source, and preview controls.'
  },
  {
    source: 'settings/gap-to-leader.png',
    output: 'app/04-settings-gap-to-leader.png',
    title: 'Settings - Gap To Leader',
    description: 'Gap trend overlay visibility, sizing, browser source, and preview controls.'
  },
  {
    source: 'settings/track-map.png',
    output: 'app/05-settings-track-map.png',
    title: 'Settings - Track Map',
    description: 'Track map visibility, opacity, map-building, sector boundaries, and browser source controls.'
  },
  {
    source: 'settings/stream-chat.png',
    output: 'app/06-settings-stream-chat.png',
    title: 'Settings - Stream Chat',
    description: 'Stream chat provider, visibility, sizing, and preview controls.'
  },
  {
    source: 'settings/garage-cover.png',
    output: 'app/07-settings-garage-cover.png',
    title: 'Settings - Garage Cover',
    description: 'Garage cover image import, preview, clear, visibility, and sizing controls.'
  },
  {
    source: 'settings/fuel-calculator.png',
    output: 'app/08-settings-fuel-calculator.png',
    title: 'Settings - Fuel Calculator',
    description: 'Fuel calculator visibility, sizing, browser source, and preview controls.'
  },
  {
    source: 'settings/inputs.png',
    output: 'app/09-settings-inputs.png',
    title: 'Settings - Inputs',
    description: 'Input state visibility, trace/readout content, sizing, browser source, and preview controls.'
  },
  {
    source: 'settings/car-radar.png',
    output: 'app/10-settings-car-radar.png',
    title: 'Settings - Car Radar',
    description: 'Car radar visibility, multiclass warning, sizing, browser source, and preview controls.'
  },
  {
    source: 'settings/flags.png',
    output: 'app/11-settings-flags.png',
    title: 'Settings - Flags',
    description: 'Flags overlay visibility, sizing, browser source, and preview controls.'
  },
  {
    source: 'settings/session-weather.png',
    output: 'app/12-settings-session-weather.png',
    title: 'Settings - Session / Weather',
    description: 'Session and weather overlay visibility, content, sizing, browser source, and preview controls.'
  },
  {
    source: 'settings/pit-service.png',
    output: 'app/13-settings-pit-service.png',
    title: 'Settings - Pit Service',
    description: 'Pit service overlay visibility, tire grid content, sizing, browser source, and preview controls.'
  },
  {
    source: 'settings/support.png',
    output: 'app/14-settings-diagnostics.png',
    title: 'Settings - Diagnostics',
    description: 'Diagnostic telemetry, local map building, support bundle, and support folder controls.'
  }
];

const overlayItems = [
  {
    source: 'browser-overlays/standings-race.png',
    output: 'overlays/01-standings-race.png',
    title: 'Standings',
    description: 'Multi-class race standings with class headers, focus row, gaps, laps, and pit state.'
  },
  {
    source: 'browser-overlays/relative-race.png',
    output: 'overlays/02-relative-race.png',
    title: 'Relative',
    description: 'Nearby cars around the focus driver with lap relationship coloring and compact empty-row spacing.'
  },
  {
    source: 'browser-overlays/fuel-calculator-race.png',
    output: 'overlays/03-fuel-calculator-race.png',
    title: 'Fuel Calculator',
    description: 'Race stint plan, current fuel, stop count, and measured/history-backed burn evidence.'
  },
  {
    source: 'browser-overlays/gap-to-leader-race.png',
    output: 'overlays/04-gap-to-leader-race.png',
    title: 'Gap To Leader',
    description: 'Live gap trend with connected history lines, intervals, and race-context summary rows.'
  },
  {
    source: 'browser-overlays/track-map-race.png',
    output: 'overlays/05-track-map-race.png',
    title: 'Track Map',
    description: 'IBT-derived Nurburgring 24h track shape with car markers and focus-car highlighting.',
    curation: 'real-derived Nurburgring 24h track-map scenario'
  },
  {
    source: 'browser-overlays/track-map-fallback.png',
    output: 'overlays/06-track-map-circle-fallback.png',
    title: 'Track Map Fallback',
    description: 'Circular fallback map used when generated geometry is unavailable.'
  },
  {
    source: 'browser-overlays/session-weather-race.png',
    output: 'overlays/07-session-weather-race.png',
    title: 'Session / Weather',
    description: 'Session, clock, lap, track, weather, wind, temperature, and atmosphere metrics.'
  },
  {
    source: 'browser-overlays/pit-service-race.png',
    output: 'overlays/08-pit-service-active.png',
    title: 'Pit Service - Active',
    description: 'Pit signal, service status, fuel/repair requests, and per-tire service grid.'
  },
  {
    source: 'browser-overlays/pit-service-idle.png',
    output: 'overlays/09-pit-service-idle.png',
    title: 'Pit Service - Idle',
    description: 'Pit-ready idle state with safe placeholders for unavailable tire and service values.'
  },
  {
    source: 'browser-overlays/input-state-race.png',
    output: 'overlays/10-input-state-race.png',
    title: 'Input / Car State',
    description: 'Throttle, brake, steering, gear, speed, ABS, and trace graph evidence.'
  },
  {
    source: 'browser-overlays/car-radar-both-sides.png',
    output: 'overlays/11-car-radar-both-sides.png',
    title: 'Car Radar',
    description: 'Both-sides proximity warning with radar rings and side-pressure arcs.'
  },
  {
    source: 'browser-overlays/flags-all-kinds.png',
    output: 'overlays/12-flags-all-kinds.png',
    title: 'Flags',
    description: 'Full flag palette: green, blue, yellow, caution, red, black, repair, white, and checkered.'
  },
  {
    source: 'browser-overlays/stream-chat-twitch-rich.png',
    output: 'overlays/13-stream-chat-twitch-rich.png',
    title: 'Stream Chat',
    description: 'Twitch-style chat with badges, emotes, names, timestamps, and message text.'
  },
  {
    source: 'browser-overlays/garage-cover-garage-visible.png',
    output: 'overlays/14-garage-cover-visible.png',
    title: 'Garage Cover',
    description: 'Garage-visible full-canvas cover image used for broadcast or stream masking.'
  }
];

const mediaItems = [...appItems, ...overlayItems];

if (!existsSync(sourceManifestPath)) {
  throw new Error(`Missing browser-review screenshot manifest at ${relative(repoRoot, sourceManifestPath)}`);
}

const sourceManifest = JSON.parse(readFileSync(sourceManifestPath, 'utf8'));
const screenshots = Array.isArray(sourceManifest.screenshots) ? sourceManifest.screenshots : [];
const screenshotsByPath = new Map(screenshots.map((screenshot) => [screenshot.path, screenshot]));

rmSync(outputRoot, { recursive: true, force: true });
mkdirSync(outputRoot, { recursive: true });

const manifestItems = mediaItems.map((item) => {
  const screenshot = screenshotsByPath.get(item.source);
  if (!screenshot) {
    throw new Error(`Source screenshot ${item.source} is missing from ${relative(repoRoot, sourceManifestPath)}`);
  }

  const sourcePath = join(sourceRoot, item.source);
  const outputPath = join(outputRoot, item.output);
  if (!existsSync(sourcePath)) {
    throw new Error(`Source screenshot file ${relative(repoRoot, sourcePath)} does not exist`);
  }

  mkdirSync(dirname(outputPath), { recursive: true });
  copyFileSync(sourcePath, outputPath);

  return {
    title: item.title,
    description: item.description,
    path: item.output,
    source: item.source,
    width: screenshot.width ?? null,
    height: screenshot.height ?? null,
    bytes: screenshot.bytes ?? null,
    status: screenshot.status ?? null,
    bodyKind: screenshot.bodyKind ?? null,
    curation: item.curation ?? null,
    scenarioEvidence: screenshot.scenarioEvidence ?? null
  };
});

writeFileSync(
  join(outputRoot, 'manifest.json'),
  `${JSON.stringify({
    contract: 'tmr-overlay-media-packet/v1',
    source: relative(repoRoot, sourceManifestPath),
    sourceSurface: sourceManifest.surfaceMode ?? null,
    unitSystem: sourceManifest.unitSystem ?? null,
    itemCount: manifestItems.length,
    items: manifestItems
  }, null, 2)}\n`
);

writeFileSync(join(outputRoot, 'README.md'), mediaReadme(manifestItems));
console.log(`Wrote ${manifestItems.length} media packet screenshots to ${relative(repoRoot, outputRoot)}`);

function mediaReadme(items) {
  const rows = items
    .map((item) => `| ${item.title} | [${item.path}](${item.path}) | ${item.description} |`)
    .join('\n');

  return `# TmrOverlay V1 Overlay Showcase

This folder is a shareable media packet for teammates and product review. It uses curated, feature-rich browser-review states, including real-derived scenarios when available, but it is separate from screenshot validation artifacts and should not be treated as browser/native/localhost parity proof.

Source screenshots come from the deterministic browser-review screenshot suite. Refresh this packet after regenerating and validating browser-review screenshots:

\`\`\`bash
npm run screenshots:browser-review -- --output artifacts/browser-review-screenshots
python3 tools/validate_overlay_screenshots.py --profile browser-review-ci --root artifacts/browser-review-screenshots
npm run media:packet
\`\`\`

| Item | Image | Shows |
| --- | --- | --- |
${rows}
`;
}

function parseArgs(argv) {
  const parsed = {};
  for (let index = 0; index < argv.length; index += 1) {
    const arg = argv[index];
    if (arg === '--source') {
      parsed.source = argv[++index];
    } else if (arg === '--output') {
      parsed.output = argv[++index];
    } else if (arg === '--help' || arg === '-h') {
      console.log('Usage: node tools/browser-review/build-media-packet.mjs [--source artifacts/browser-review-screenshots] [--output docs/media-packet/v1-overlay-showcase]');
      process.exit(0);
    } else {
      throw new Error(`Unknown argument: ${arg}`);
    }
  }

  return parsed;
}
