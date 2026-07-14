import { existsSync, readFileSync, readdirSync, statSync } from 'node:fs';
import { createHash } from 'node:crypto';
import { createServer } from 'node:http';
import { resolve } from 'node:path';
import {
  browserAssetRoot,
  browserOverlayApiResponse,
  browserOverlayPage,
  browserOverlayPages,
  freshLiveSnapshot,
  repoRoot,
  renderOverlayHtml,
  renderOverlayIndexHtml,
  renderAppValidatorReviewHtml,
  renderInstallerReviewHtml,
  renderSettingsGeneralReviewHtml,
  overlayGeometry,
  settingsBrowserSourceSize
} from '../../tests/browser-overlays/browserOverlayAssets.js';

const port = Number.parseInt(process.env.TMR_BROWSER_REVIEW_PORT || '5177', 10);
const initialReviewUnitSystem = normalizeUnitSystem(process.env.TMR_REVIEW_UNIT_SYSTEM || process.env.TMR_UNIT_SYSTEM || 'Metric');
const productionModelReplayRoot = resolveOptionalDirectory(process.env.TMR_BROWSER_REVIEW_MODEL_REPLAY_ROOT);
const reviewAppState = createReviewAppState();
const reviewNurburgringTrackMap = JSON.parse(readFileSync(
  resolve(repoRoot, 'fixtures/screenshot-scenarios/track-map-nurburgring-24h.json'),
  'utf8'));
const gapLongTailRealDataSnapshot = JSON.parse(readFileSync(
  resolve(repoRoot, 'fixtures/telemetry-analysis/overlay-real-data-snapshots/gap-to-leader-long-tail-real-data.json'),
  'utf8'));
const gapPitWindowRealDataSnapshot = JSON.parse(readFileSync(
  resolve(repoRoot, 'fixtures/telemetry-analysis/overlay-real-data-snapshots/gap-to-leader-pit-window-real-data.json'),
  'utf8'));
const gapThreatCaptureShapedSnapshot = JSON.parse(readFileSync(
  resolve(repoRoot, 'fixtures/telemetry-analysis/overlay-real-data-snapshots/gap-to-leader-threat-capture-shaped.json'),
  'utf8'));
const gapEnduranceDomainCaptureShapedSnapshot = JSON.parse(readFileSync(
  resolve(repoRoot, 'fixtures/telemetry-analysis/overlay-real-data-snapshots/gap-to-leader-endurance-domain-capture-shaped.json'),
  'utf8'));
const trackMapFocusPracticeSnapshot = JSON.parse(readFileSync(
  resolve(repoRoot, 'fixtures/telemetry-analysis/overlay-real-data-snapshots/track-map-focus-and-practice-marker-policy.json'),
  'utf8'));
const trackMapPlayerFocusClassColorSnapshot = JSON.parse(readFileSync(
  resolve(repoRoot, 'fixtures/telemetry-analysis/overlay-real-data-snapshots/track-map-player-focus-class-color-real-data.json'),
  'utf8'));
const fuelV2BottomHalfRealHistoryReferences = JSON.parse(readFileSync(
  resolve(repoRoot, 'fixtures/telemetry-analysis/fuel-v2-bottom-half-real-history/manifest.json'),
  'utf8'));
const fuelV2HistoryBridgeFixture = JSON.parse(readFileSync(
  resolve(repoRoot, 'fixtures/telemetry-analysis/fuel-v2-history-bridge/dallara-classified-13.50.json'),
  'utf8'));
const clients = new Set();
const productionOverlayModelIds = new Set(browserOverlayPages()
  .filter((page) => page.modelRoute)
  .map((page) => page.page.id));
const assetBackedReviewOverlayModelIds = new Set([
  'input-state',
  'car-radar',
  'track-map',
  'flags',
  'garage-cover',
  'stream-chat'
]);
const opacityExcludedOverlayIds = new Set([
  'car-radar',
  'flags',
  'garage-cover',
  'track-map'
]);
const collapsibleBrowserSourceChromeIds = new Set([
  'standings',
  'relative',
  'fuel-calculator',
  'gap-to-leader',
  'session-weather',
  'pit-service'
]);
const sessionWeatherWeatherContentLabels = [
  'Wetness',
  'Declared surface',
  'Rubber',
  'Skies',
  'Weather',
  'Rain',
  'Wind direction',
  'Wind speed',
  'Facing wind',
  'Air temp',
  'Track temp',
  'Humidity',
  'Fog',
  'Pressure'
];
const sectionOffContentLabelsByFixture = new Map([
  ['input-no-content', [
    'Throttle trace',
    'Brake trace',
    'Clutch trace',
    'Throttle %',
    'Brake %',
    'Clutch %',
    'Steering wheel',
    'Gear',
    'Speed'
  ]],
  ['input-graph-only', [
    'Throttle %',
    'Brake %',
    'Clutch %',
    'Steering wheel',
    'Gear',
    'Speed'
  ]],
  ['input-rail-only', [
    'Throttle trace',
    'Brake trace',
    'Clutch trace'
  ]],
  ['relative-driver-only', [
    'Relative position',
    'Relative delta',
    'Pit status'
  ]],
  ['relative-position-driver', [
    'Relative delta',
    'Pit status'
  ]],
  ['relative-no-content', [
    'Relative position',
    'Driver',
    'Relative delta',
    'Pit status'
  ]],
  ['standings-no-pit', [
    'Pit status'
  ]],
  ['standings-driver-only', [
    'Class position',
    'Car number',
    'Class gap',
    'Previous interval',
    'Fastest lap',
    'Last lap',
    'Pit status'
  ]],
  ['standings-no-content', [
    'Class position',
    'Car number',
    'Driver',
    'Class gap',
    'Previous interval',
    'Fastest lap',
    'Last lap',
    'Pit status'
  ]],
  ['standings-content-off-chrome-on', [
    'Class position',
    'Car number',
    'Driver',
    'Class gap',
    'Previous interval',
    'Fastest lap',
    'Last lap',
    'Pit status'
  ]],
  ['fuel-plan-off', [
    'Plan'
  ]],
  ['fuel-fuel-off', [
    'Fuel'
  ]],
  ['fuel-stint-targets-off', [
    'Stint targets'
  ]],
  ['fuel-race-information-off', [
    'Plan',
    'Fuel'
  ]],
  ['session-weather-session-off', [
    'Session type',
    'Session name',
    'Session mode',
    'Elapsed time',
    'Remaining time',
    'Total time',
    'Event type',
    'Car',
    'Track name',
    'Track length',
    'Laps remaining',
    'Laps total'
  ]],
  ['session-weather-weather-off', sessionWeatherWeatherContentLabels],
  ['pit-service-session-off', [
    'Session time',
    'Session laps'
  ]],
  ['pit-service-signal-off', [
    'Release',
    'Pit status'
  ]],
  ['pit-service-service-off', [
    'Fuel requested',
    'Fuel selected',
    'Tearoff requested',
    'Required repair',
    'Optional repair',
    'Fast repair selected',
    'Fast repairs available'
  ]],
  ['pit-service-grid-only', [
    'Session time',
    'Session laps',
    'Release',
    'Pit status',
    'Fuel requested',
    'Fuel selected',
    'Tearoff requested',
    'Required repair',
    'Optional repair',
    'Fast repair selected',
    'Fast repairs available'
  ]],
  ['pit-service-tire-analysis-off', [
    'Compound',
    'Change request',
    'Set limit',
    'Sets available',
    'Sets used',
    'Pressure',
    'Temperature',
    'Wear',
    'Distance'
  ]],
  ['gap-tire-trend-off', [
    'Tire'
  ]],
  ['gap-trend-off', [
    'Last',
    '5L',
    '10L',
    'Pit',
    'PLap',
    'Stint',
    'Tire',
    'Status'
  ]],
  ['gap-graph-off', [
    'Graph'
  ]]
]);
let reloadTimer = null;

class ReviewHttpError extends Error {
  constructor(statusCode, message) {
    super(message);
    this.name = 'ReviewHttpError';
    this.statusCode = statusCode;
  }
}

const server = createServer((request, response) => {
  const url = new URL(request.url || '/', `http://${request.headers.host || `localhost:${port}`}`);
  const path = normalizePath(url.pathname);

  try {
    if (path === '/api/review/settings' && request.method === 'POST') {
      handleReviewSettingsPost(request, response);
      return;
    }

    if (path === '/review/events') {
      serveEvents(request, response);
      return;
    }

    if (path === '/api/garage-cover/default-image') {
      serveBinary(
        response,
        'image/png',
        readFileSync(resolve(repoRoot, 'assets/brand/Team_Logo_4k_TMRBRANDING.png')));
      return;
    }

    if (path === '/api/garage-cover/image') {
      serveBinary(
        response,
        'image/png',
        readFileSync(resolve(repoRoot, 'assets/brand/Team_Logo_4k_TMRBRANDING.png')));
      return;
    }

    const apiPayload = reviewApiResponse(path, url.searchParams);
    if (apiPayload) {
      serveJson(response, apiPayload);
      return;
    }

    if (path === '/' || path === '/review' || path === '/review/overlays') {
      serveHtml(response, withLiveReload(renderOverlayIndexHtml(port)));
      return;
    }

    if (path === '/review/app') {
      serveHtml(response, withLiveReload(renderAppValidatorReviewHtml({
        previewMode: url.searchParams.get('preview') || 'off',
        selectedTab: url.searchParams.get('tab') || 'general',
        selectedRegion: url.searchParams.get('region') || 'general',
        reviewComponent: url.searchParams.get('component') || null,
        reviewState: reviewStateForRequest(url.searchParams)
      })));
      return;
    }

    if (path === '/review/settings/general') {
      serveHtml(response, withLiveReload(renderSettingsGeneralReviewHtml({
        previewMode: url.searchParams.get('preview') || 'off',
        reviewState: reviewAppState
      })));
      return;
    }

    if (path === '/review/installer') {
      serveHtml(response, withLiveReload(renderInstallerReviewHtml({
        menuId: url.searchParams.get('menu') || 'welcome'
      })));
      return;
    }

    if (path === '/review/workbenches/fuel-stint-n') {
      serveHtml(response, withLiveReload(renderFuelStintNWorkbenchHtml(url.searchParams)));
      return;
    }

    const overlayId = overlayIdFromPath(path);
    if (overlayId) {
      serveHtml(response, withLiveReload(renderOverlayHtml(overlayId)));
      return;
    }

    serveText(response, 404, 'Not found');
  } catch (error) {
    const statusCode = error instanceof ReviewHttpError ? error.statusCode : 500;
    serveText(response, statusCode, error instanceof Error ? error.stack || error.message : String(error));
  }
});

server.listen(port, '127.0.0.1', () => {
  console.log(`Browser review server: http://127.0.0.1:${port}/review`);
  console.log(`Overlay routes:        http://127.0.0.1:${port}/overlays/standings`);
  console.log(`Asset root:            ${browserAssetRoot}`);
});

startAssetPolling();

function renderFuelStintNWorkbenchHtml(searchParams = new URLSearchParams()) {
  const selectedTab = searchParams.get('tab') === 'v1' ? 'v1' : 'v2';
  const workbenches = [
    {
      id: 'short-finish',
      title: 'V2 workbench - Dallara fixed four-lap start',
      detail: 'Short-finish shape: Current Stint, Final Stint, and a compact Strategy row. Fuel Target is a consumption threshold; its colour reflects the live feasibility state. Tires below uses exact-shape evidence language only; no service seconds are implied until rules are proven.',
      fixture: 'fuel-v2-bottom-half-dallara-four-lap',
      version: 'v2'
    },
    {
      id: 'endurance-stint-five',
      title: 'V2 workbench - Endurance race at Stint 5',
      detail: 'Four-row cap: Stint 5, Stint 6, Final Stint, and Strategy. This synthetic no-tire-evidence case proves that the entire Tires column disappears, rather than showing unknown cells.',
      fixture: 'fuel-v2-bottom-half-endurance-stint-five',
      version: 'v2'
    },
    {
      id: 'endurance-stint-six',
      title: 'V2 workbench - Endurance race after the Stint 5 stop',
      detail: 'The same no-tire-evidence plan has rolled forward gracefully: Stint 6, Stint 7, Final Stint, and Strategy.',
      fixture: 'fuel-v2-bottom-half-endurance-stint-six',
      kind: 'strategy',
      version: 'v2'
    },
    {
      id: 'test-fresh-combo',
      title: 'V2 workbench - Test, fresh car / layout',
      detail: 'No race strategy is shown. The lower half becomes a Model Readiness checklist for this exact car and track layout. Each cell states the factual collection status it needs.',
      fixture: 'fuel-v2-model-readiness-test-fresh-combo',
      kind: 'readiness',
      preview: 'test',
      version: 'v2'
    },
    {
      id: 'test-fuel-baseline',
      title: 'V2 workbench - Test, clean fuel baseline',
      detail: 'A clean Test fuel baseline is useful and saved as Test provenance. It improves fuel calculations without pretending that pit-route, refuel, or tire-service evidence has been collected.',
      fixture: 'fuel-v2-model-readiness-test-fuel-baseline',
      kind: 'readiness',
      preview: 'test',
      version: 'v2'
    },
    {
      id: 'practice-pit-service',
      title: 'V2 workbench - Practice, pit-service evidence',
      detail: 'This richer Practice example shows the desired distinction: saved evidence is green; evidence that is real but not yet broad enough for a strategy model remains amber and asks for a precise next sample.',
      fixture: 'fuel-v2-model-readiness-practice-pit-service',
      kind: 'readiness',
      preview: 'practice',
      version: 'v2'
    }
  ];
  const requestedScenario = searchParams.get('scenario');
  const workbench = workbenches.find((candidate) => candidate.id === requestedScenario)
    || workbenches[0];
  const v1Workbench = {
    title: 'Current V1 overlay - Dallara 35-minute race start',
    detail: 'This is the actual current V1 Fuel Calculator review model and layout—not V1 data restyled as V2. It remains here as a visual/content reference while V2 lower-half choices are still being made.',
    fixture: 'fuel-dallara-35m-v1',
    version: 'v1'
  };
  const displayedWorkbenches = selectedTab === 'v1' ? [v1Workbench] : [workbench];
  const cards = displayedWorkbenches.map((candidate) => `
    <section class="workbench-card ${candidate.version}">
      <header>
        <h2>${candidate.title}</h2>
        <p>${candidate.detail}</p>
        <a href="/review/overlays/fuel-calculator?preview=${candidate.preview || 'race'}&amp;fixture=${candidate.fixture}" target="_blank" rel="noreferrer">Open alone</a>
      </header>
      <iframe
        title="${candidate.title}"
        src="/review/overlays/fuel-calculator?preview=${candidate.preview || 'race'}&amp;fixture=${candidate.fixture}"></iframe>
    </section>`).join('');
  const tabLinks = [
    { id: 'v1', label: 'Current V1' },
    { id: 'v2', label: 'V2 workbench' }
  ].map((tab) => {
    const active = tab.id === selectedTab ? ' active' : '';
    const query = tab.id === 'v2' && requestedScenario
      ? `?tab=v2&amp;scenario=${encodeURIComponent(requestedScenario)}`
      : `?tab=${tab.id}`;
    return `<a class="workbench-tab${active}" href="/review/workbenches/fuel-stint-n${query}">${tab.label}</a>`;
  }).join('');
  const scenarioLinks = (kind) => workbenches.filter((candidate) => candidate.kind === kind || (!candidate.kind && kind === 'strategy')).map((candidate) => {
    const active = candidate.id === workbench.id ? ' active' : '';
    return `<a class="scenario-link${active}" href="/review/workbenches/fuel-stint-n?tab=v2&amp;scenario=${candidate.id}">${candidate.id.replaceAll('-', ' ')}</a>`;
  }).join('');

  return `<!doctype html>
<html lang="en">
  <head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <title>Fuel V2 - Stint N Workbench</title>
    <style>
      :root { color-scheme: dark; font-family: Inter, ui-sans-serif, system-ui, sans-serif; }
      * { box-sizing: border-box; }
      body { margin: 0; background: #080b10; color: #f3f6fb; }
      main { min-width: 0; padding: 18px; }
      .page-header { margin: 0 0 16px; }
      h1, h2, p { margin: 0; }
      h1 { font-size: 20px; letter-spacing: 0.02em; }
      .page-header p, .workbench-card p { margin-top: 5px; color: #94a0b3; font-size: 12px; line-height: 1.45; }
      .workbench-tabs, .scenario-switcher { display: flex; flex-wrap: wrap; gap: 7px; margin-top: 12px; }
      .workbench-tab { border: 1px solid #4c576a; border-radius: 6px 6px 0 0; color: #b8cbe1; font-size: 12px; font-weight: 700; padding: 6px 10px; text-decoration: none; }
      .workbench-tab.active { border-color: #62d9f5; background: #123543; color: #e7fbff; }
      .scenario-link { border: 1px solid #33445d; border-radius: 999px; color: #b8cbe1; font-size: 11px; padding: 4px 8px; text-decoration: none; text-transform: capitalize; }
      .scenario-link.active { border-color: #4fbddc; background: #123543; color: #dff8ff; }
      .scenario-group-label { align-self: center; color: #718096; font-size: 10px; font-weight: 800; letter-spacing: .05em; text-transform: uppercase; }
      .comparison-grid { display: grid; grid-template-columns: minmax(560px, 1fr); gap: 16px; align-items: start; }
      .workbench-card { min-width: 0; border: 1px solid #253044; border-radius: 8px; background: #0d121b; overflow: hidden; }
      .workbench-card header { min-height: 66px; padding: 12px 14px; border-bottom: 1px solid #253044; }
      .workbench-card h2 { font-size: 14px; }
      .workbench-card a { display: inline-block; margin-top: 8px; color: #62d9f5; font-size: 11px; font-weight: 700; text-decoration: none; }
      .workbench-card a:hover { text-decoration: underline; }
      iframe { display: block; width: 100%; height: 720px; border: 0; background: #080b10; }
      @media (max-width: 900px) {
        .comparison-grid { grid-template-columns: 1fr; }
      }
    </style>
  </head>
  <body>
    <main>
      <header class="page-header">
        <h1>Fuel Calculator - V1 / V2 workbench</h1>
        <p>${selectedTab === 'v1'
          ? 'Use this tab to inspect the current V1 overlay in its real layout before deciding what content or hierarchy V2 should retain.'
          : workbench.kind === 'readiness'
            ? 'In Test and Practice, the race Strategy grid is replaced by Model Readiness: exact car + layout evidence and its factual collection state. It is a collection guide, not strategy advice.'
            : 'The V2 pane preserves the approved top half, then caps the lower half at Current Stint, Next Stint, Final Stint, and Strategy. Final never duplicates Current or Next; Tires remains an explicit evidence-gated column.'}</p>
        <nav class="workbench-tabs" aria-label="Fuel workbench version">${tabLinks}</nav>
        ${selectedTab === 'v2' ? `<nav class="scenario-switcher" aria-label="Race strategy workbench scenario"><span class="scenario-group-label">Race strategy</span>${scenarioLinks('strategy')}</nav>
        <nav class="scenario-switcher" aria-label="Test and Practice readiness workbench scenario"><span class="scenario-group-label">Test / Practice readiness</span>${scenarioLinks('readiness')}</nav>` : ''}
      </header>
      <div class="comparison-grid">${cards}</div>
    </main>
  </body>
</html>`;
}

function createReviewAppState() {
  return {
    unitSystem: initialReviewUnitSystem,
    support: {
      rawCaptureEnabled: false,
      latestBundlePath: '',
      statusText: '',
      statusTone: 'neutral',
      updateText: 'Current v1.0.3.',
      updateTone: 'success',
      canCheckUpdates: true,
      canInstallUpdate: false,
      canRestartUpdate: false,
      updatePendingRestart: false
    },
    overlays: Object.create(null)
  };
}

function reviewStateForRequest(searchParams) {
  const updateStatus = String(searchParams.get('update') || '').trim().toLowerCase();
  const update = updateStatusFixture(updateStatus);
  if (!update) return reviewAppState;
  return {
    ...reviewAppState,
    support: {
      ...reviewAppState.support,
      ...update
    }
  };
}

function updateStatusFixture(updateStatus) {
  switch (updateStatus) {
    case 'disabled':
      return {
        updateText: 'Disabled.',
        updateTone: 'muted',
        canCheckUpdates: false,
        canInstallUpdate: false,
        canRestartUpdate: false,
        updatePendingRestart: false
      };
    case 'not-installed':
      return {
        updateText: 'Dev run.',
        updateTone: 'muted',
        canCheckUpdates: false,
        canInstallUpdate: false,
        canRestartUpdate: false,
        updatePendingRestart: false
      };
    case 'idle':
      return {
        updateText: 'Ready.',
        updateTone: 'muted',
        canCheckUpdates: true,
        canInstallUpdate: false,
        canRestartUpdate: false,
        updatePendingRestart: false
      };
    case 'up-to-date':
      return {
        updateText: 'Current v1.0.3.',
        updateTone: 'success',
        canCheckUpdates: true,
        canInstallUpdate: false,
        canRestartUpdate: false,
        updatePendingRestart: false
      };
    case 'available':
      return {
        updateText: 'v1.0.4 available.',
        updateTone: 'warning',
        canCheckUpdates: true,
        canInstallUpdate: true,
        canRestartUpdate: false,
        updatePendingRestart: false
      };
    case 'checking':
      return {
        updateText: 'Checking...',
        updateTone: 'info',
        canCheckUpdates: false,
        canInstallUpdate: false,
        canRestartUpdate: false,
        updatePendingRestart: false
      };
    case 'downloading':
      return {
        updateText: 'Downloading v1.0.4: 42%.',
        updateTone: 'info',
        canCheckUpdates: false,
        canInstallUpdate: false,
        canRestartUpdate: false,
        updatePendingRestart: false
      };
    case 'pending-restart':
      return {
        updateText: 'v1.0.4 pending restart.',
        updateTone: 'warning',
        canCheckUpdates: true,
        canInstallUpdate: false,
        canRestartUpdate: true,
        updatePendingRestart: true
      };
    case 'applying':
      return {
        updateText: 'Restarting for v1.0.4.',
        updateTone: 'info',
        canCheckUpdates: false,
        canInstallUpdate: false,
        canRestartUpdate: false,
        updatePendingRestart: false
      };
    case 'failed':
      return {
        updateText: 'Check failed.',
        updateTone: 'error',
        canCheckUpdates: true,
        canInstallUpdate: false,
        canRestartUpdate: false,
        updatePendingRestart: false
      };
    default:
      return null;
  }
}

async function handleReviewSettingsPost(request, response) {
  try {
    const body = await readRequestBody(request, 64 * 1024);
    const patch = body ? JSON.parse(body) : {};
    applyReviewSettingsPatch(patch);
    serveJson(response, {
      ok: true,
      reviewState: reviewAppState
    });
  } catch (error) {
    response.writeHead(400, { 'Content-Type': 'application/json; charset=utf-8' });
    response.end(JSON.stringify({
      ok: false,
      error: error instanceof Error ? error.message : String(error)
    }));
  }
}

function readRequestBody(request, maxBytes) {
  return new Promise((resolveBody, rejectBody) => {
    let body = '';
    request.setEncoding('utf8');
    request.on('data', (chunk) => {
      body += chunk;
      if (body.length > maxBytes) {
        rejectBody(new Error('request body too large'));
        request.destroy();
      }
    });
    request.on('end', () => resolveBody(body));
    request.on('error', rejectBody);
  });
}

function applyReviewSettingsPatch(patch) {
  const kind = String(patch?.kind || '').trim();
  if (kind === 'unitSystem') {
    reviewAppState.unitSystem = normalizeUnitSystem(patch.value);
    return;
  }
  if (kind === 'support') {
    applyReviewSupportPatch(patch);
    return;
  }

  const overlayId = normalizeOverlayId(patch?.overlayId);
  if (!overlayId) {
    return;
  }

  const overlay = reviewOverlayState(overlayId);
  switch (kind) {
    case 'overlayEnabled':
      overlay.enabled = patch.enabled === true;
      break;
    case 'session':
      for (const session of sessionKeys(patch.session)) {
        overlay.sessions[session] = patch.enabled === true;
      }
      break;
    case 'content':
      {
        const label = typeof patch.label === 'string' ? patch.label.trim() : '';
        const key = typeof patch.key === 'string' ? patch.key.trim() : '';
        const sessions = sessionKeys(patch.session);
        const names = Array.from(new Set([key, label].filter(Boolean)));
        for (const name of names) {
          if (sessions.length === 0) {
            overlay.content[name] = patch.enabled === true;
            continue;
          }

          for (const session of sessions) {
            overlay.content[`${name}.${session}`] = patch.enabled === true;
          }
        }
      }
      break;
    case 'chrome':
      {
        const area = String(patch.area || '').toLowerCase() === 'footer' ? 'footer' : 'header';
        const label = String(patch.label || '').trim();
        const sessions = sessionKeys(patch.session);
        if (label && sessions.length > 0) {
          overlay.chrome[area] ??= Object.create(null);
          overlay.chrome[area][label] ??= Object.create(null);
          for (const session of sessions) {
            overlay.chrome[area][label][session] = patch.enabled === true;
          }
        }
      }
      break;
    case 'streamChatProvider':
      overlay.provider = providerFromLabel(patch.providerLabel);
      break;
    case 'streamChatText':
      overlay.streamlabsWidgetUrl = typeof patch.streamlabsWidgetUrl === 'string' ? patch.streamlabsWidgetUrl : '';
      overlay.twitchChannel = typeof patch.twitchChannel === 'string' ? patch.twitchChannel.trim() : '';
      break;
    case 'garageCover':
      if (patch.action === 'import') {
        overlay.garageHasImage = true;
      } else if (patch.action === 'clear') {
        overlay.garageHasImage = false;
        overlay.garagePreviewVisible = false;
      } else if (patch.action === 'preview') {
        overlay.garagePreviewVisible = true;
      }
      break;
    case 'number':
      {
        const key = String(patch.key || '').trim();
        const value = Number(patch.value);
        if (key && Number.isFinite(value)) {
          overlay[key] = value;
        }
      }
      break;
  }
}

function applyReviewSupportPatch(patch) {
  reviewAppState.support ??= {
      rawCaptureEnabled: false,
      latestBundlePath: '',
      statusText: '',
      statusTone: 'neutral',
      updateText: 'Current v1.0.3.',
      updateTone: 'success',
      canCheckUpdates: true,
      canInstallUpdate: false,
      canRestartUpdate: false,
      updatePendingRestart: false
  };
  const support = reviewAppState.support;
  const action = String(patch?.action || '').trim();
  switch (action) {
    case 'rawCapture':
      support.rawCaptureEnabled = patch.enabled === true;
      support.statusText = support.rawCaptureEnabled
        ? 'Enhanced iRacing telemetry capture will start with live data.'
        : 'Enhanced iRacing telemetry capture disabled.';
      support.statusTone = 'success';
      break;
    case 'createBundle':
      support.latestBundlePath = typeof patch.path === 'string' ? patch.path : support.latestBundlePath;
      support.statusText = 'Created diagnostics bundle.';
      support.statusTone = 'success';
      break;
    case 'copyBundlePath':
      support.statusText = patch.ok === true
        ? 'Copied bundle path.'
        : patch.reason === 'clipboardUnavailable'
          ? 'Clipboard unavailable. Select the path instead.'
          : 'No diagnostics bundle yet.';
      support.statusTone = patch.ok === true ? 'success' : 'error';
      break;
    case 'checkUpdates':
      support.updateText = 'Current v1.0.3.';
      support.updateTone = 'success';
      support.statusText = 'Checked for updates.';
      support.statusTone = 'success';
      break;
    default:
      support.statusText = supportActionMessage(action);
      support.statusTone = 'success';
      break;
  }
}

function supportActionMessage(action) {
  return {
    installUpdate: 'No installable update in review run.',
    openLogs: 'Opened logs folder.',
    openDiagnostics: 'Opened diagnostics folder.',
    openCaptures: 'Opened captures folder.',
    openHistory: 'Opened history folder.'
  }[action] || 'Updated diagnostics state.';
}

function reviewOverlayState(overlayId) {
  reviewAppState.overlays[overlayId] ??= {
    sessions: Object.create(null),
    content: Object.create(null),
    chrome: Object.create(null)
  };
  reviewAppState.overlays[overlayId].sessions ??= Object.create(null);
  reviewAppState.overlays[overlayId].content ??= Object.create(null);
  reviewAppState.overlays[overlayId].chrome ??= Object.create(null);
  return reviewAppState.overlays[overlayId];
}

function normalizeOverlayId(value) {
  const id = String(value || '').trim().toLowerCase();
  return browserOverlayPages().some((page) => page.page.id === id) ? id : null;
}

function sessionKey(value) {
  const normalized = String(value || '').trim().toLowerCase();
  if (normalized === 'test') return 'practice';
  return normalized === 'qual' ? 'qualifying' : normalized;
}

function sessionKeys(value) {
  const key = sessionKey(value);
  if (!key) return [];
  return key === 'practice' ? ['practice', 'test'] : [key];
}

function providerFromLabel(value) {
  const normalized = String(value || '').trim().toLowerCase();
  if (normalized === 'streamlabs') return 'streamlabs';
  if (normalized === 'not configured' || normalized === 'none') return 'none';
  return 'twitch';
}

function fixtureVariant(searchParams = new URLSearchParams()) {
  return String(searchParams.get('fixture') || '').trim().toLowerCase();
}

function garageCoverImageMode(searchParams = new URLSearchParams()) {
  const mode = String(searchParams.get('garageImageMode') || '').trim().toLowerCase();
  return ['ready', 'missing', 'not-configured'].includes(mode) ? mode : 'ready';
}

function garageCoverFallbackReason(imageMode) {
  return imageMode === 'missing' ? 'file_missing' : 'not_configured';
}

function fixtureMatches(searchParams, ...variants) {
  const fixture = fixtureVariant(searchParams);
  return variants.includes(fixture);
}

function reviewApiResponse(path, searchParams = new URLSearchParams()) {
  const previewMode = normalizePreviewMode(searchParams.get('preview'));
  if (path === '/api/snapshot') {
    return { live: reviewLiveSnapshot(previewMode, searchParams) };
  }

  if (path.startsWith('/api/overlay-model/')) {
    const overlayId = decodeURIComponent(path.slice('/api/overlay-model/'.length)).trim().toLowerCase();
    if (!productionOverlayModelIds.has(overlayId)) {
      return null;
    }

    const replayResponse = productionModelReplayResponse(overlayId, searchParams);
    if (replayResponse) {
      return replayResponse;
    }

    const page = browserOverlayPage(overlayId);
    return { model: reviewDisplayModelWithRootOpacity(page.page.id, previewMode, searchParams) };
  }

  const page = browserOverlayPages().find((candidate) => candidate.settingsRoute === path);
  if (!page) {
    return null;
  }

  return browserOverlayApiResponse(page.page.id, path, {
    live: reviewLiveSnapshot(previewMode, searchParams),
    settings: reviewSettings(page.page.id, previewMode, searchParams),
    model: productionOverlayModelIds.has(page.page.id)
      ? reviewDisplayModelWithRootOpacity(page.page.id, previewMode, searchParams)
      : null
  });
}

function resolveOptionalDirectory(value) {
  const normalized = String(value || '').trim();
  return normalized ? resolve(normalized) : null;
}

function productionModelReplayResponse(overlayId, searchParams) {
  if (fixtureVariant(searchParams) !== 'production-model-replay') {
    return null;
  }

  if (!productionModelReplayRoot) {
    throw new Error(
      'fixture=production-model-replay requires TMR_BROWSER_REVIEW_MODEL_REPLAY_ROOT to point at a production model-replay output directory.');
  }

  const modelRowsPath = resolve(productionModelReplayRoot, 'overlays', overlayId, 'models.jsonl');
  if (!existsSync(modelRowsPath)) {
    throw new Error(`Production model-replay rows not found for ${overlayId}: ${modelRowsPath}`);
  }

  const rows = readFileSync(modelRowsPath, 'utf8')
    .split(/\r?\n/)
    .filter((line) => line.trim().length > 0)
    .map((line, index) => {
      try {
        return JSON.parse(line);
      } catch (error) {
        throw new Error(`Invalid production model-replay JSON at ${modelRowsPath}:${index + 1}: ${formatError(error)}`);
      }
    })
    .filter((row) => row?.overlayId === overlayId && row?.response?.model);
  if (rows.length === 0) {
    throw new Error(`No serialized production model response for ${overlayId} in ${modelRowsPath}`);
  }

  const requestedFrameValue = searchParams.has('frame')
    ? searchParams.get('frame')
    : searchParams.has('frameIndex')
      ? searchParams.get('frameIndex')
      : null;
  if (requestedFrameValue === null) {
    return rows[0].response;
  }

  const requestedFrame = Number(requestedFrameValue);
  if (!Number.isInteger(requestedFrame)) {
    throw new ReviewHttpError(400, `Production model-replay frame must be an integer: ${requestedFrameValue}`);
  }

  const selected = rows.find((row) => row.frameIndex === requestedFrame);
  if (!selected) {
    throw new ReviewHttpError(404, `Production model-replay frame ${requestedFrame} was not found for ${overlayId}`);
  }

  // Do not run fixture rows through the Node review builders, content filters,
  // chrome logic, opacity synthesis, or evidence wrapper. This response was
  // produced by the C# live store plus BrowserOverlayModelFactory and must be
  // rendered byte-for-byte as its browser model contract.
  return selected.response;
}

function startAssetPolling() {
  let assetSignature = readAssetSignature(browserAssetRoot);
  const interval = setInterval(() => {
    try {
      const nextSignature = readAssetSignature(browserAssetRoot);
      if (nextSignature !== assetSignature) {
        assetSignature = nextSignature;
        broadcastReload();
      }
    } catch (error) {
      console.warn(`Live reload polling failed: ${formatError(error)}`);
    }
  }, 500);

  if (typeof interval.unref === 'function') {
    interval.unref();
  }

  console.log('Live reload polling browser assets every 500ms.');
}

function readAssetSignature(root) {
  const files = listAssetFiles(root);
  let maxMtimeMs = 0;

  for (const file of files) {
    const stats = statSync(file);
    if (stats.mtimeMs > maxMtimeMs) {
      maxMtimeMs = stats.mtimeMs;
    }
  }

  return `${files.length}:${maxMtimeMs}`;
}

function listAssetFiles(root) {
  const files = [];

  for (const entry of readdirSync(root, { withFileTypes: true })) {
    const child = resolve(root, entry.name);
    if (entry.isDirectory()) {
      files.push(...listAssetFiles(child));
    } else if (entry.isFile()) {
      files.push(child);
    }
  }

  return files;
}

function formatError(error) {
  return error instanceof Error ? error.message : String(error);
}

function overlayIdFromPath(path) {
  const reviewPrefix = '/review/overlays/';
  if (path.startsWith(reviewPrefix)) {
    return decodeURIComponent(path.slice(reviewPrefix.length));
  }

  const overlayPrefix = '/overlays/';
  if (path.startsWith(overlayPrefix)) {
    try {
      return browserOverlayPage(path).page.id;
    } catch {
      return decodeURIComponent(path.slice(overlayPrefix.length));
    }
  }

  return null;
}

function reviewLiveSnapshot(previewMode = 'off', searchParams = new URLSearchParams()) {
  const sessionKind = previewMode === 'off' ? 'practice' : previewMode;
  const sessionType = sessionKind === 'qualifying' ? 'Qualify' : titleCase(sessionKind);
  const inputTrace = reviewInputTrace();
  const currentInputs = inputTrace[inputTrace.length - 1] || {
    throttle: 0,
    brake: 0,
    clutch: 0,
    brakeAbsActive: false
  };
  const live = freshLiveSnapshot({
    session: {
      hasData: true,
      sessionType,
      sessionName: `${sessionType} Preview`,
      eventType: sessionType,
      currentSessionNum: sessionKind === 'race' ? 2 : sessionKind === 'qualifying' ? 1 : 0,
      sessionTimeSeconds: sessionKind === 'race' ? 62571.436719 : 460,
      sessionTimeRemainSeconds: sessionKind === 'race' ? 23828.563281 : 740,
      sessionLapsRemainEx: sessionKind === 'race' ? 32767 : null,
      sessionLapsTotal: sessionKind === 'race' ? 32767 : null,
      trackDisplayName: 'Gesamtstrecke 24h',
      carScreenName: 'Aston Martin Vantage GT3 EVO'
    },
    inputs: {
      hasData: true,
      throttle: currentInputs.throttle,
      brake: currentInputs.brake,
      clutch: currentInputs.clutch,
      steeringWheelAngle: -0.18,
      gear: sessionKind === 'race' ? 6 : 4,
      rpm: sessionKind === 'race' ? 7900 : 7120,
      speedMetersPerSecond: sessionKind === 'race' ? 77.889366 : 63.4,
      brakeAbsActive: currentInputs.brakeAbsActive,
      trace: inputTrace
    },
    raceEvents: {
      hasData: true,
      isOnTrack: true,
      isInGarage: false,
      isGarageVisible: false,
      lapDistPct: 0.42
    },
    reference: {
      hasData: true,
      playerCarIdx: 42,
      focusCarIdx: 42,
      focusIsPlayer: true,
      isOnTrack: true,
      isInGarage: false,
      lapDistPct: 0.42,
      playerYawNorthRadians: 0.35
    },
    driverDirectory: {
      hasData: true,
      playerCarIdx: 42,
      focusCarIdx: 42
    },
    spatial: {
      hasData: true,
      referenceCarIdx: 42,
      referenceLapDistPct: 0.42,
      hasCarLeft: false,
      hasCarRight: sessionKind === 'race',
      sideStatus: sessionKind === 'race' ? 'right' : 'clear',
      strongestMulticlassApproach: {
        relativeSeconds: -2.4
      },
      referenceCarClassColorHex: '#ffda59',
      cars: [
        { carIdx: 33, relativeSeconds: -1.1, carClassColorHex: '#33ceff' },
        { carIdx: 91, relativeSeconds: 0.2, relativeMeters: 2, carClassColorHex: '#ffaa00' },
        { carIdx: 12, relativeSeconds: -2.8, carClassColorHex: '#ff4fd8' }
      ]
    },
    trackMap: {
      sectors: [
        { startPct: 0, endPct: 0.32, highlight: 'personal-best' },
        { startPct: 0.32, endPct: 0.68, highlight: 'none' },
        { startPct: 0.68, endPct: 1, highlight: 'best-lap' }
      ]
    },
    timing: {
      focusCarIdx: 42,
      focusRow: {
        carIdx: 42,
        overallPosition: 24,
        classPosition: 24,
        lapDistPct: 0.42,
        hasSpatialProgress: true,
        hasTakenGrid: true,
        isFocus: true,
        trackSurface: 3
      },
      overallRows: reviewTrackMapTimingRows(),
      classRows: []
    }
  });
  return applyReviewFixtureToLiveSnapshot(live, fixtureVariant(searchParams));
}

function applyReviewFixtureToLiveSnapshot(live, fixture) {
  if (!fixture || !live?.models) {
    return live;
  }

  const models = live.models;
  if (fixture === 'input-state-mock-data') {
    const inputTrace = reviewInputTrace();
    const currentInputs = inputTrace[inputTrace.length - 1] || {
      throttle: 0,
      brake: 0,
      clutch: 0,
      brakeAbsActive: false
    };
    models.inputs = {
      ...(models.inputs || {}),
      hasData: true,
      throttle: currentInputs.throttle,
      brake: currentInputs.brake,
      clutch: currentInputs.clutch,
      steeringWheelAngle: -0.18,
      gear: 6,
      rpm: 7900,
      speedMetersPerSecond: 77.889366,
      brakeAbsActive: currentInputs.brakeAbsActive,
      trace: inputTrace
    };
    return live;
  }

  if (fixture === 'input-waiting' || fixture === 'input-no-data') {
    models.inputs = {
      ...(models.inputs || {}),
      hasData: false,
      trace: []
    };
    return live;
  }

  if (fixture.startsWith('car-radar-')) {
    const variant = fixture.replace('car-radar-', '');
    const sides = {
      left: [true, false],
      right: [false, true],
      'both-sides': [true, true],
      clear: [false, false],
      'side-no-placement': [true, false]
    }[variant];
    if (sides) {
      const [hasCarLeft, hasCarRight] = sides;
      const cars = {
        left: [
          { carIdx: 21, relativeSeconds: -0.2, relativeMeters: -2, carClassColorHex: '#33ceff' }
        ],
        right: [
          { carIdx: 22, relativeSeconds: 0.2, relativeMeters: 2, carClassColorHex: '#ffaa00' }
        ],
        'both-sides': [
          { carIdx: 21, relativeSeconds: -0.2, relativeMeters: -2, carClassColorHex: '#33ceff' },
          { carIdx: 22, relativeSeconds: 0.2, relativeMeters: 2, carClassColorHex: '#ffaa00' }
        ],
        clear: [],
        'side-no-placement': [
          { carIdx: 23, relativeSeconds: 1.2, relativeMeters: 18, carClassColorHex: '#ffda59' }
        ]
      }[variant] || [];
      models.spatial = {
        ...(models.spatial || {}),
        hasData: true,
        hasCarLeft,
        hasCarRight,
        sideStatus: hasCarLeft && hasCarRight
          ? 'both'
          : hasCarLeft
            ? 'left'
            : hasCarRight
              ? 'right'
              : 'clear',
        cars,
        multiclassApproaches: [],
        strongestMulticlassApproach: null
      };
    }
    return live;
  }

  if (fixture === 'track-map-no-markers') {
    models.timing = {
      ...(models.timing || {}),
      focusCarIdx: null,
      focusRow: null,
      overallRows: [],
      classRows: []
    };
    models.reference = {
      ...(models.reference || {}),
      focusCarIdx: null,
      lapDistPct: null,
      playerTrackSurface: 1,
      trackSurface: 1,
      onPitRoad: true,
      playerOnPitRoad: true
    };
    return live;
  }

  if (fixture === 'track-map-focus-practice-real-data') {
    const raw = trackMapFocusPracticeSnapshot.rawEvidence;
    const rowsByCarIdx = new Map(raw.timingRows.map((row) => [row.carIdx, row]));
    const toTimingRow = (row) => reviewTimingRow(
      row.carIdx,
      null,
      null,
      row.lapDistPct,
      row.classColor,
      {
        isFocus: row.role === 'focus',
        isPlayer: row.role === 'player',
        hasTakenGrid: row.hasTakenGrid === true,
        carClassName: row.carClass,
        trackSurface: 3
      });
    const focusRaw = rowsByCarIdx.get(raw.focusCarIdx);
    const playerRaw = rowsByCarIdx.get(raw.playerCarIdx);
    const timingRows = raw.timingRows.map(toTimingRow);
    models.session = {
      ...(models.session || {}),
      hasData: true,
      sessionType: raw.sessionType,
      eventType: raw.sessionType,
      sessionName: `${raw.sessionType} compact real-data marker policy`
    };
    models.reference = {
      ...(models.reference || {}),
      hasData: true,
      playerCarIdx: raw.playerCarIdx,
      focusCarIdx: raw.focusCarIdx,
      focusIsPlayer: false,
      hasExplicitNonPlayerFocus: true,
      lapDistPct: focusRaw?.lapDistPct ?? null,
      playerLapDistPct: playerRaw?.lapDistPct ?? null,
      trackSurface: 3,
      playerTrackSurface: 3,
      onPitRoad: false,
      playerOnPitRoad: false,
      classPosition: null,
      overallPosition: null
    };
    models.driverDirectory = {
      ...(models.driverDirectory || {}),
      hasData: true,
      playerCarIdx: raw.playerCarIdx,
      focusCarIdx: raw.focusCarIdx
    };
    models.timing = {
      ...(models.timing || {}),
      hasData: true,
      focusCarIdx: raw.focusCarIdx,
      playerCarIdx: raw.playerCarIdx,
      focusRow: focusRaw ? toTimingRow(focusRaw) : null,
      playerRow: playerRaw ? toTimingRow(playerRaw) : null,
      overallRows: timingRows,
      classRows: []
    };
    models.scoring = {
      ...(models.scoring || {}),
      rows: []
    };
    return live;
  }

  if (fixture === 'track-map-player-focus-class-color') {
    const raw = trackMapPlayerFocusClassColorSnapshot.rawEvidence;
    const row = raw.timingRows.find((candidate) => candidate.role === 'player-focus');
    const focusRow = reviewTimingRow(
      row.carIdx,
      1,
      1,
      row.lapDistPct,
      row.classColor,
      {
        isFocus: true,
        isPlayer: true,
        hasTakenGrid: row.hasTakenGrid === true,
        carClassName: row.carClass,
        trackSurface: 3
      });
    models.reference = {
      ...(models.reference || {}),
      hasData: true,
      playerCarIdx: raw.playerCarIdx,
      focusCarIdx: raw.focusCarIdx,
      focusIsPlayer: true,
      hasExplicitNonPlayerFocus: false,
      lapDistPct: row.lapDistPct,
      playerLapDistPct: row.lapDistPct,
      trackSurface: 3,
      playerTrackSurface: 3,
      onPitRoad: false,
      playerOnPitRoad: false,
      classPosition: 1,
      overallPosition: 1
    };
    models.driverDirectory = {
      ...(models.driverDirectory || {}),
      hasData: true,
      playerCarIdx: raw.playerCarIdx,
      focusCarIdx: raw.focusCarIdx
    };
    models.timing = {
      ...(models.timing || {}),
      hasData: true,
      focusCarIdx: raw.focusCarIdx,
      playerCarIdx: raw.playerCarIdx,
      focusRow,
      playerRow: focusRow,
      overallRows: [focusRow],
      classRows: []
    };
    models.scoring = {
      ...(models.scoring || {}),
      rows: []
    };
    return live;
  }

  if (fixture.startsWith('garage-')) {
    const variant = fixture.replace('garage-', '');
    if (variant === 'disconnected') {
      live.isConnected = false;
    }
    if (variant === 'stale') {
      live.lastUpdatedAtUtc = '2026-05-17T12:00:00.000Z';
    }
    const garageVisible = variant === 'visible' || variant.startsWith('visible-');
    models.raceEvents = {
      ...(models.raceEvents || {}),
      hasData: variant !== 'disconnected',
      isGarageVisible: garageVisible,
      isInGarage: garageVisible,
      isOnTrack: !garageVisible
    };
    models.reference = {
      ...(models.reference || {}),
      isInGarage: garageVisible,
      isOnTrack: !garageVisible
    };
  }

  return live;
}

function reviewSettings(overlayId, previewMode = 'off', searchParams = new URLSearchParams()) {
  const overlayState = effectiveSettingsOverlayState(
    overlayId,
    reviewAppState.overlays[overlayId] || {},
    previewMode,
    searchParams);
  const unitSystem = reviewAppState.unitSystem;
  const session = sessionKeyFromPreview(previewMode);
  if (overlayId === 'stream-chat') {
    if (fixtureMatches(searchParams, 'stream-chat-twitch-rich')) {
      return {
        provider: 'twitch',
        isConfigured: true,
        streamlabsWidgetUrl: null,
        twitchChannel: 'techmatesracing',
        status: 'connected',
        replayStatus: 'replay chat | twitch',
        replayRows: reviewStreamChatRichRows(),
        contentOptions: streamChatContentOptionsFromReviewState(overlayState)
      };
    }
    if (fixtureMatches(searchParams, 'stream-chat-streamlabs-configured')) {
      return {
        provider: 'streamlabs',
        isConfigured: true,
        streamlabsWidgetUrl: 'https://streamlabs.com/widgets/chat-box/review-token',
        twitchChannel: null,
        status: 'connected',
        contentOptions: streamChatContentOptionsFromReviewState(overlayState)
      };
    }
    const provider = overlayState.provider || 'none';
    return {
      provider,
      isConfigured: provider !== 'none',
      streamlabsWidgetUrl: provider === 'streamlabs' ? overlayState.streamlabsWidgetUrl || 'https://streamlabs.com/widgets/chat-box/review-token' : null,
      twitchChannel: provider === 'twitch' ? overlayState.twitchChannel || 'techmatesracing' : null,
      status: provider === 'none' ? 'not_configured' : 'connected',
      contentOptions: streamChatContentOptionsFromReviewState(overlayState)
    };
  }

  if (overlayId === 'garage-cover') {
    const garageFixture = fixtureVariant(searchParams);
    if (garageFixture.startsWith('garage-')) {
      const imageMode = garageCoverImageMode(searchParams);
      const hasImage = imageMode === 'ready';
      const previewVisible = overlayState.garagePreviewVisible === true;
      return {
        hasImage,
        imageVersion: hasImage ? `review-${garageFixture}` : null,
        fallbackReason: hasImage ? null : garageCoverFallbackReason(imageMode),
        previewVisible
      };
    }
    return {
      hasImage: overlayState.garageHasImage === true,
      imageVersion: overlayState.garageHasImage === true ? 'review' : null,
      fallbackReason: overlayState.garageHasImage === true ? null : 'not_configured',
      previewVisible: overlayState.garagePreviewVisible === true
    };
  }

  if (overlayId === 'track-map') {
    const fallbackMap = String(searchParams.get('trackMap') || '').toLowerCase() === 'fallback';
    return {
      trackMap: fallbackMap ? null : reviewTrackMap(),
      trackMapSettings: {
        internalOpacity: Math.max(0, Math.min(1, Number(overlayState.opacityPercent ?? 0) / 100)),
        showSectorBoundaries: contentEnabled(overlayState, 'Sector boundaries', true, [], session),
        includeUserMaps: contentEnabled(
          overlayState,
          'track-map.build-from-telemetry',
          true,
          ['Local map building'],
          session)
      }
    };
  }

  if (overlayId === 'input-state') {
    if (fixtureMatches(searchParams, 'input-state-mock-data')) {
      return inputStateReviewSettings(unitSystem);
    }
    if (fixtureMatches(searchParams, 'input-graph-only')) {
      return inputStateReviewSettings(unitSystem, {
        showThrottle: false,
        showBrake: false,
        showClutch: false,
        showSteering: false,
        showGear: false,
        showSpeed: false
      });
    }
    if (fixtureMatches(searchParams, 'input-rail-only')) {
      return inputStateReviewSettings(unitSystem, {
        showThrottleTrace: false,
        showBrakeTrace: false,
        showClutchTrace: false
      });
    }
    if (fixtureMatches(searchParams, 'input-no-content')) {
      return inputStateReviewSettings(unitSystem, {
        showThrottleTrace: false,
        showBrakeTrace: false,
        showClutchTrace: false,
        showThrottle: false,
        showBrake: false,
        showClutch: false,
        showSteering: false,
        showGear: false,
        showSpeed: false
      });
    }
    return {
      unitSystem,
      showThrottleTrace: contentEnabled(overlayState, 'Throttle trace', true, [], session),
      showBrakeTrace: contentEnabled(overlayState, 'Brake trace', true, [], session),
      showClutchTrace: contentEnabled(overlayState, 'Clutch trace', true, [], session),
      showThrottle: contentEnabled(overlayState, 'Throttle %', true, ['Throttle'], session),
      showBrake: contentEnabled(overlayState, 'Brake %', true, ['Brake'], session),
      showClutch: contentEnabled(overlayState, 'Clutch %', true, ['Clutch'], session),
      showSteering: contentEnabled(overlayState, 'Steering wheel', true, ['Steering'], session),
      showGear: contentEnabled(overlayState, 'Gear', true, [], session),
      showSpeed: contentEnabled(overlayState, 'Speed', true, [], session)
    };
  }

  if (overlayId === 'car-radar') {
    return {
      showMulticlassWarning: contentEnabled(overlayState, 'Faster-class warning', true, ['radar.multiclass-warning'], session),
      multiclassWarningSeconds: clampInteger(overlayState?.multiclassWarningSeconds, 5, 3, 10),
      radarVisibilitySeconds: clampInteger(overlayState?.radarVisibilitySeconds, 2, 2, 5)
    };
  }

  if (overlayId === 'flags') {
    if (fixtureMatches(searchParams, 'flags-all-kinds')) {
      return {
        flags: reviewAllFlags(),
        showGreen: true,
        showBlue: true,
        showYellow: true,
        showCritical: true,
        showFinish: true
      };
    }
    if (fixtureMatches(searchParams, 'flags-six-kinds')) {
      return {
        flags: reviewAllFlags().slice(0, 6),
        showGreen: true,
        showBlue: true,
        showYellow: true,
        showCritical: true,
        showFinish: true
      };
    }
    if (fixtureMatches(searchParams, 'flags-race-start-pseudo')) {
      return {
        flags: [
          { kind: 'yellow', category: 'yellow', label: 'One to green', detail: null, tone: 'warning' },
          { kind: 'green', category: 'green', label: 'Start', detail: null, tone: 'success' }
        ],
        showGreen: true,
        showBlue: true,
        showYellow: true,
        showCritical: true,
        showFinish: true
      };
    }
    if (fixtureMatches(searchParams, 'flags-practice-pseudo-suppressed')) {
      return {
        flags: reviewFlagsForSession('practice'),
        showGreen: true,
        showBlue: true,
        showYellow: true,
        showCritical: true,
        showFinish: true
      };
    }
    if (fixtureMatches(searchParams, 'flags-practice-local-yellow')) {
      return {
        flags: [
          { kind: 'yellow', category: 'yellow', label: 'Yellow', detail: 'local', tone: 'warning' },
          { kind: 'blue', category: 'blue', label: 'Blue', detail: null, tone: 'info' }
        ],
        showGreen: true,
        showBlue: true,
        showYellow: true,
        showCritical: true,
        showFinish: true
      };
    }
    return {
      flags: reviewFlagsForSession(session),
      showGreen: contentEnabled(overlayState, 'Green / start / ready', true, ['Green']),
      showBlue: contentEnabled(overlayState, 'Blue', true),
      showYellow: contentEnabled(overlayState, 'Yellow / debris / caution', true, ['Yellow']),
      showCritical: contentEnabled(overlayState, 'Red / black / repair', true, ['Red / black']),
      showFinish: contentEnabled(overlayState, 'White / checkered / final laps', true, ['White / checkered'])
    };
  }

  return {
    unitSystem,
    reviewOverlayState: overlayState
  };
}

function inputStateReviewSettings(unitSystem, overrides = {}) {
  return {
    unitSystem,
    showThrottleTrace: true,
    showBrakeTrace: true,
    showClutchTrace: true,
    showThrottle: true,
    showBrake: true,
    showClutch: true,
    showSteering: true,
    showGear: true,
    showSpeed: true,
    ...overrides
  };
}

function reviewFlagsForSession(session) {
  const blue = { kind: 'blue', category: 'blue', label: 'Blue', detail: null, tone: 'info' };
  if (session === 'race') {
    return [
      { kind: 'yellow', category: 'yellow', label: 'Yellow', detail: null, tone: 'warning' },
      blue,
      { kind: 'checkered', category: 'finish', label: 'Checkered', detail: null, tone: 'info' }
    ];
  }

  return [blue];
}

function reviewAllFlags() {
  return [
    { kind: 'green', category: 'green', label: 'Green', detail: null, tone: 'success' },
    { kind: 'blue', category: 'blue', label: 'Blue', detail: null, tone: 'info' },
    { kind: 'yellow', category: 'yellow', label: 'Yellow', detail: null, tone: 'warning' },
    { kind: 'debris', category: 'yellow', label: 'Debris', detail: null, tone: 'warning' },
    { kind: 'caution', category: 'yellow', label: 'Caution', detail: 'waving', tone: 'warning' },
    { kind: 'red', category: 'critical', label: 'Red', detail: null, tone: 'error' },
    { kind: 'black', category: 'critical', label: 'Black', detail: null, tone: 'error' },
    { kind: 'meatball', category: 'critical', label: 'Repair', detail: null, tone: 'error' },
    { kind: 'white', category: 'finish', label: 'White', detail: null, tone: 'info' },
    { kind: 'checkered', category: 'finish', label: 'Checkered', detail: null, tone: 'info' }
  ];
}

function reviewStreamChatRichRows() {
  return [
    {
      name: 'RaceCtrl',
      text: 'Green flag at the line',
      kind: 'notice',
      authorColorHex: '#62FF9F',
      metadata: ['12:04', 'first'],
      badges: [{ id: 'moderator', version: '1', label: 'mod', roomId: '1234' }],
      segments: [{ kind: 'text', text: 'Green flag at the line', imageUrl: null }]
    },
    {
      name: 'TechMate',
      text: 'Brake trace looks clean Kappa',
      kind: 'message',
      authorColorHex: '#37A2FF',
      metadata: ['12:05', 'reply'],
      badges: [{ id: 'subscriber', version: '12', label: 'sub', roomId: '1234' }],
      segments: [
        { kind: 'text', text: 'Brake trace looks clean ', imageUrl: null },
        { kind: 'emote', text: 'Kappa', imageUrl: 'https://static-cdn.jtvnw.net/emoticons/v2/25/default/dark/2.0' }
      ]
    },
    {
      name: 'CrewChief',
      text: 'Box this lap for fuel and tires',
      kind: 'message',
      authorColorHex: '#FFDA59',
      metadata: ['12:06'],
      badges: [{ id: 'vip', version: '1', label: 'vip', roomId: '1234' }],
      segments: [{ kind: 'text', text: 'Box this lap for fuel and tires', imageUrl: null }]
    }
  ];
}

function contentEnabled(overlayState, label, defaultValue = true, aliases = [], session = null) {
  return contentLabelsEnabled(overlayState, [label, ...aliases], defaultValue, session);
}

function contentLabelsEnabled(overlayState, labels, defaultValue = true, session = null) {
  const content = overlayState?.content || {};
  let hasExplicitValue = false;
  let hasEnabledValue = false;
  for (const candidate of labels) {
    if (session) {
      const sessionCandidate = `${candidate}.${session}`;
      if (Object.hasOwn(content, sessionCandidate)) {
        hasExplicitValue = true;
        hasEnabledValue ||= content[sessionCandidate] !== false;
      }
    }

    if (Object.hasOwn(content, candidate)) {
      hasExplicitValue = true;
      hasEnabledValue ||= content[candidate] !== false;
    }
  }

  return hasExplicitValue ? hasEnabledValue : defaultValue;
}

function streamChatContentOptionsFromReviewState(overlayState) {
  return {
    showAuthorColor: contentEnabled(overlayState, 'Author color', true),
    showBadges: contentEnabled(overlayState, 'Badges', true),
    showBits: contentEnabled(overlayState, 'Bits', true),
    showFirstMessage: contentEnabled(overlayState, 'First message', true),
    showReplies: contentEnabled(overlayState, 'Replies', true),
    showTimestamps: contentEnabled(overlayState, 'Timestamps', true),
    showEmotes: contentEnabled(overlayState, 'Emotes', true),
    showAlerts: contentEnabled(overlayState, 'Alerts', true),
    showMessageIds: contentEnabled(overlayState, 'Message IDs', false)
  };
}

function reviewTrackMapTimingRows() {
  return [
    reviewTimingRow(8, 1, 1, 0.10, '#33CEFF'),
    reviewTimingRow(17, 2, 1, 0.24, '#33CEFF'),
    reviewTimingRow(33, 3, 2, 0.64, '#FFAA00'),
    reviewTimingRow(42, 24, 24, 0.42, '#00E8FF', { isFocus: true })
  ];
}

function reviewTimingRow(carIdx, overallPosition, classPosition, lapDistPct, carClassColorHex, extra = {}) {
  return {
    carIdx,
    overallPosition,
    classPosition,
    lapDistPct,
    carClassColorHex,
    hasSpatialProgress: true,
    hasTakenGrid: true,
    trackSurface: 3,
    ...extra
  };
}

function reviewInputTrace() {
  return Array.from({ length: 180 }, (_, index) => {
    const t = index / 18;
    const braking = Math.max(
      inputPulse(index, 38, 7) * 0.94,
      inputPulse(index, 86, 8) * 0.86,
      inputPulse(index, 136, 7) * 0.98);
    let brake = Math.max(0, Math.min(1, braking));
    let throttle = Math.max(0, Math.min(1, 0.82 + Math.sin(t * 1.15) * 0.18));
    throttle = Math.max(0, Math.min(1, throttle * (1 - Math.min(1, brake * 1.08))));
    let clutch = Math.max(0, Math.min(1, 1 - Math.max(
      inputPulse(index, 24, 2.5) * 0.72,
      inputPulse(index, 64, 2.4) * 0.58,
      inputPulse(index, 113, 2.4) * 0.62,
      inputPulse(index, 154, 2.6) * 0.7)));
    if (index < 16) {
      throttle = 1;
      brake = 0;
      clutch = 1;
    } else if (index >= 166) {
      throttle = 0;
      brake = 1;
      clutch = 1;
    }
    return {
      throttle,
      brake,
      clutch,
      brakeAbsActive: index > 112 && index < 132 || index >= 166
    };
  });
}

function inputPulse(index, center, width) {
  const distance = (index - center) / width;
  return Math.exp(-(distance * distance));
}

function reviewDisplayModelWithRootOpacity(overlayId, previewMode = 'off', searchParams = new URLSearchParams()) {
  const hiddenModel = hiddenProductDisplayModel(overlayId, previewMode, searchParams);
  if (hiddenModel) {
    return withEffectiveSettingsEvidence({ ...hiddenModel, rootOpacity: 1 }, overlayId, previewMode, searchParams);
  }

  const model = reviewDisplayModel(overlayId, previewMode, searchParams);
  if (!model) {
    return model;
  }

  if (opacityExcludedOverlayIds.has(overlayId)) {
    return withEffectiveSettingsEvidence({ ...model, rootOpacity: 1 }, overlayId, previewMode, searchParams);
  }

  const overlayState = effectiveSettingsOverlayState(
    overlayId,
    reviewAppState.overlays[overlayId] || {},
    previewMode,
    searchParams);
  const percent = Number(overlayState.opacityPercent ?? 100);
  const opacity = Number.isFinite(percent) ? Math.max(0.2, Math.min(1, percent / 100)) : 1;
  return withEffectiveSettingsEvidence({ ...model, rootOpacity: opacity }, overlayId, previewMode, searchParams);
}

function hiddenProductDisplayModel(overlayId, previewMode = 'off', searchParams = new URLSearchParams()) {
  const overlayState = effectiveSettingsOverlayState(
    overlayId,
    reviewAppState.overlays[overlayId] || {},
    previewMode,
    searchParams);
  const normalizedPreviewMode = normalizePreviewMode(previewMode);
  const session = sessionKeyFromPreview(previewMode);
  const overlayDisabled = Object.hasOwn(overlayState, 'enabled') && overlayState.enabled === false;
  const sessionDisabled = Object.hasOwn(overlayState?.sessions || {}, session) && overlayState.sessions[session] === false;
  const relativeQualifying = overlayId === 'relative' && normalizedPreviewMode === 'qualifying';
  const noRenderableContent = !reviewHasRenderableContent(overlayId, overlayState, session);
  const chromeOnlyRenderable = noRenderableContent
    && overlayId === 'standings'
    && chromeEnabled(overlayState, 'header', 'Time remaining', session, true);
  if (!overlayDisabled && !sessionDisabled && !relativeQualifying && (!noRenderableContent || chromeOnlyRenderable)) {
    return null;
  }

  const page = browserOverlayPage(overlayId);
  return {
    overlayId,
    title: page.title,
    status: overlayDisabled
      ? 'disabled | product hidden'
      : sessionDisabled
        ? 'hidden | session disabled'
        : relativeQualifying
          ? 'hidden | qualifying unsupported'
          : 'hidden | no enabled content',
    source: '',
    bodyKind: hiddenBodyKind(overlayId),
    columns: [],
    rows: [],
    metrics: [],
    points: [],
    headerItems: [],
    shouldRender: false
  };
}

function reviewHasRenderableContent(overlayId, overlayState, session) {
  if (overlayId === 'gap-to-leader') {
    return reviewGapHasRenderableContent(overlayState, session);
  }

  const renderableContentOverlays = new Set([
    'standings',
    'relative',
    'fuel-calculator',
    'session-weather',
    'pit-service',
    'input-state',
    'flags'
  ]);
  if (!renderableContentOverlays.has(overlayId)) {
    return true;
  }

  return reviewRenderableContentRows(overlayId, session)
    .some((row) => contentLabelsEnabled(overlayState, [row.key, row.label].filter(Boolean), row.defaultEnabled, session));
}

function reviewRenderableContentRows(overlayId, session) {
  const rows = reviewEffectiveContentRows(overlayId);
  if (overlayId !== 'fuel-calculator') {
    return rows;
  }

  return session === 'test' || session === 'practice' || session === 'qualifying'
    ? rows.filter((row) => ['Fuel range', 'Fuel usage'].includes(row.label))
    : rows.filter((row) => ['Plan', 'Fuel', 'Stint targets'].includes(row.label));
}

function hiddenBodyKind(overlayId) {
  return {
    standings: 'table',
    relative: 'table',
    'fuel-calculator': 'metrics',
    'session-weather': 'metrics',
    'pit-service': 'metrics',
    'input-state': 'inputs',
    'car-radar': 'car-radar',
    'gap-to-leader': 'graph',
    'track-map': 'track-map',
    flags: 'flags',
    'garage-cover': 'garage-cover',
    'stream-chat': 'stream-chat'
  }[overlayId] || 'table';
}

function withEffectiveSettingsEvidence(model, overlayId, previewMode = 'off', searchParams = new URLSearchParams()) {
  const renderModel = suppressUnavailableRenderedContent(withoutOrdinaryOverlayTitle(model));
  return {
    ...renderModel,
    effectiveSettings: reviewEffectiveSettings(renderModel, overlayId, previewMode, searchParams)
  };
}

function withoutOrdinaryOverlayTitle(model) {
  if (!model || model.overlayId === 'garage-cover') {
    return model;
  }

  return String(model.title || '').trim()
    ? { ...model, title: '' }
    : model;
}

function suppressUnavailableRenderedContent(model) {
  if (!isUnavailableModel(model)
    || unavailableContentPolicy(model) === 'section-aware-placeholders'
    || unavailableContentPolicy(model) === 'chrome-only-placeholder') {
    return model;
  }

  return {
    ...model,
    shouldRender: false,
    columns: [],
    rows: [],
    metrics: [],
    points: [],
    headerItems: [],
    graph: emptyGraphModel(model.graph),
    gridSections: [],
    metricSections: [],
    carRadar: emptyCarRadarModel(model.carRadar),
    trackMap: emptyTrackMapModel(model.trackMap),
    inputs: emptyInputModel(model.inputs),
    flags: emptyFlagsModel(model.flags),
    streamChat: emptyStreamChatModel(model.streamChat)
  };
}

function emptyGraphModel(graph) {
  if (!graph) return graph;
  return {
    ...graph,
    series: [],
    weather: [],
    leaderChanges: [],
    driverChanges: [],
    pitWindows: [],
    selectedSeriesCount: 0,
    trendMetrics: [],
    activeThreat: null,
    threatCarIdx: null,
    scale: null
  };
}

function emptyInputModel(inputs) {
  if (!inputs) return inputs;
  return {
    ...inputs,
    hasGraph: false,
    hasRail: false,
    hasContent: false,
    trace: [],
    tracePointCount: 0
  };
}

function emptyFlagsModel(flags) {
  if (!flags) return flags;
  return {
    ...flags,
    flags: []
  };
}

function emptyCarRadarModel(carRadar) {
  if (!carRadar) return carRadar;
  return {
    ...carRadar,
    isAvailable: false,
    hasCarLeft: false,
    hasCarRight: false,
    cars: [],
    strongestMulticlassApproach: null,
    hasCurrentSignal: false,
    renderModel: emptyCarRadarRenderModel(carRadar.renderModel)
  };
}

function emptyCarRadarRenderModel(renderModel) {
  return {
    ...(renderModel || {}),
    shouldRender: false,
    cars: [],
    labels: [],
    rings: [],
    multiclassArc: null
  };
}

function emptyTrackMapModel(trackMap) {
  if (!trackMap) return trackMap;
  return {
    ...trackMap,
    markers: [],
    sectors: [],
    renderModel: emptyTrackMapRenderModel(trackMap.renderModel)
  };
}

function emptyTrackMapRenderModel(renderModel) {
  return {
    ...(renderModel || {}),
    isAvailable: false,
    shouldRender: false,
    markers: [],
    primitives: [],
    labels: []
  };
}

function emptyStreamChatModel(streamChat) {
  if (!streamChat) return streamChat;
  return {
    ...streamChat,
    rows: []
  };
}

function reviewEffectiveSettings(model, overlayId, previewMode = 'off', searchParams = new URLSearchParams()) {
  const normalizedPreviewMode = normalizePreviewMode(previewMode);
  const session = sessionKeyFromPreview(previewMode);
  const overlayState = reviewAppState.overlays[overlayId] || {};
  const effectiveOverlayState = effectiveSettingsOverlayState(overlayId, overlayState, previewMode, searchParams);
  const fixture = fixtureVariant(searchParams) || null;
  const settings = reviewEffectiveSettingList(overlayId, effectiveOverlayState, session, searchParams);
  const sharedSettingsHash = stableEvidenceHash(settings.filter((item) => sharedEffectiveSettingKeys.has(item.key)));
  const overlaySettingsHash = stableEvidenceHash(settings);
  const browserSource = reviewEffectiveBrowserSource(overlayId, effectiveOverlayState, normalizedPreviewMode, model);
  return {
    overlayId,
    previewMode: normalizedPreviewMode,
    sources: {
      browserReview: {
        applied: true,
        fixtureVariant: fixture,
        sharedSettingsHash,
        overlaySettingsHash,
        routePath: `/review/overlays/${overlayId}`
      },
      localhostObs: {
        applied: true,
        fixtureVariant: fixture,
        sharedSettingsHash,
        overlaySettingsHash,
        routePath: `/overlays/${overlayId}`
      },
      windowsNative: {
        applied: true,
        fixtureVariant: fixture,
        sharedSettingsHash,
        overlaySettingsHash,
        routePath: `native://${overlayId}`,
        pixelEvidence: {
          status: 'unsupported',
          reason: 'browser-review model contract; native pixels are validated by Windows screenshot artifacts'
        }
      }
    },
    rendered: {
      bodyKind: model?.bodyKind || null,
      shouldRender: model?.shouldRender !== false,
      rowCount: effectiveRenderedRowCount(model),
      columnKeys: tableColumnKeys(model),
      rowIdentities: tableRowIdentities(model),
      placeholderRowCount: placeholderRowCount(model),
      headerItems: (model?.headerItems || []).map((item) => ({
        key: item?.key || null,
        value: item?.value || '',
        tone: normalizeHeaderTone(item?.tone)
      })),
      browserSource,
      provenance: reviewRenderedProvenance(model, fixture),
      relativeTimingEvidence: relativeTimingEvidence(overlayId, normalizedPreviewMode, model),
      tableStatus: tableStatusEvidence(overlayId, model),
      timingSanity: timingSanityEvidence(overlayId, model),
      fuelStrategy: fuelStrategyEvidence(overlayId, model),
      layout: layoutDensityEvidence(overlayId, model, browserSource),
      inputAvailability: inputAvailabilityEvidence(overlayId, model),
      mapFallback: mapFallbackEvidence(overlayId, model),
      unavailableContentPolicy: unavailableContentPolicy(model),
      roleContext: reviewRoleContext(searchParams)
    },
    settings
  };
}

function reviewRoleContext(searchParams = new URLSearchParams()) {
  const fixture = fixtureVariant(searchParams);
  if (fixture === 'spectator-role') {
    return {
      playerCarIdx: 63,
      focusCarIdx: 42,
      focusIsPlayer: false,
      hasExplicitNonPlayerFocus: true,
      playerIsSpectator: true,
      focusIsSpectator: false,
      localRole: 'spectator',
      roleSource: 'DriverInfo.Drivers[].IsSpectator',
      isSpotting: null,
      spottingSignalStatus: 'not-observed'
    };
  }

  return {
    playerCarIdx: 42,
    focusCarIdx: 42,
    focusIsPlayer: true,
    hasExplicitNonPlayerFocus: false,
    playerIsSpectator: false,
    focusIsSpectator: false,
    localRole: 'driver',
    roleSource: 'DriverInfo.Drivers[].IsSpectator',
    isSpotting: null,
    spottingSignalStatus: 'not-observed'
  };
}

const sharedEffectiveSettingKeys = new Set([
  'general.unitSystem',
  'scalePercent',
  'opacityPercent'
]);

function reviewEffectiveBrowserSource(overlayId, overlayState, previewMode, model = null) {
  const sourceSize = settingsBrowserSourceSize(overlayId, overlayState, previewMode);
  if (overlayId === 'standings') {
    const baseHeight = standingsBrowserSourceHeightForModel(model, sourceSize.baseHeight);
    sourceSize.baseHeight = baseHeight;
    sourceSize.height = Math.round(baseHeight * sourceSize.scale);
  } else if (overlayId === 'fuel-calculator') {
    const baseHeight = fuelBrowserSourceHeightForModel(model, sourceSize.baseHeight);
    sourceSize.baseHeight = baseHeight;
    sourceSize.height = Math.round(baseHeight * sourceSize.scale);
    if (isFuelLapsWorkbenchModel(model)) {
      sourceSize.baseWidth = 1500;
      sourceSize.width = Math.round(sourceSize.baseWidth * sourceSize.scale);
    }
  } else if (isSimpleTelemetryModelDrivenSizeOverlay(overlayId)) {
    applySourceSize(
      sourceSize,
      simpleTelemetryBrowserSourceSizeForModel(
        overlayId,
        overlayState,
        previewMode,
        model,
        sourceSize));
  } else if (overlayId === 'flags') {
    applySourceSize(sourceSize, flagsBrowserSourceSizeForModel(model, sourceSize));
  }

  const opacityPercent = opacityExcludedOverlayIds.has(overlayId)
    ? 100
    : clampInteger(overlayState?.opacityPercent, 100, overlayId === 'track-map' ? 0 : 20, 100);
  return {
    ...sourceSize,
    opacity: Number((opacityPercent / 100).toFixed(3)),
    opacityPercent
  };
}

function standingsBrowserSourceHeightForModel(model, fallbackHeight) {
  if (!model || !Array.isArray(model.rows)) {
    return fallbackHeight;
  }

  const hasHeader = Array.isArray(model.headerItems)
    && model.headerItems.some((item) => String(item?.value || '').trim());
  if (model.rows.length <= 0) {
    return hasHeader ? 40 : fallbackHeight;
  }

  const persistedHeight = 313 - (hasHeader ? 0 : 38);
  return reviewStandingsHeightForRows(model.rows.length, Math.max(28, persistedHeight), hasHeader, false);
}

function reviewStandingsHeightForRows(rowCount, persistedHeight, showHeader, showFooter) {
  const rows = Math.max(1, Math.min(24, Number(rowCount || 0)));
  const persistedRows = reviewStandingsVisibleRowsForHeight(persistedHeight, showHeader, showFooter);
  if (rows <= persistedRows) return persistedHeight;

  return Math.max(
    persistedHeight,
    reviewStandingsHeaderReserveHeight(showHeader)
      + (showFooter ? 32 : 8)
      + 1
      + 30
      + rows * (30 + 5));
}

function reviewStandingsVisibleRowsForHeight(height, showHeader, showFooter) {
  const bodyHeight = Number(height || 0)
    - reviewStandingsHeaderReserveHeight(showHeader)
    - (showFooter ? 32 : 8)
    - 1;
  return Math.max(1, Math.floor((bodyHeight - 30) / (30 + 5)));
}

function reviewStandingsHeaderReserveHeight(showHeader) {
  return showHeader ? 38 + 12 : 16;
}

function fuelBrowserSourceHeightForModel(model, fallbackHeight) {
  if (model?.shouldRender === false) {
    return 88;
  }

  const metricSections = Array.isArray(model?.metricSections)
    ? model.metricSections.filter((section) => Array.isArray(section?.rows) && section.rows.length > 0)
    : [];
  const gridSections = Array.isArray(model?.gridSections)
    ? model.gridSections.filter((section) => Array.isArray(section?.rows) && section.rows.length > 0)
    : [];
  const chartSections = Array.isArray(model?.chartSections)
    ? model.chartSections.filter((section) => Array.isArray(section?.series) && section.series.length > 0)
    : [];
  const metricRowCount = metricSections.reduce((total, section) => total + section.rows.length, 0);
  const gridRowCount = gridSections.reduce((total, section) => total + section.rows.length + 1, 0);
  const rowCount = metricRowCount + gridRowCount;
  const sectionCount = metricSections.length + gridSections.length;
  if (rowCount <= 0 && chartSections.length <= 0) {
    return fallbackHeight;
  }

  let height = fuelContentHeight(rowCount, sectionCount, {
    clampToDefault: !isFuelLapsWorkbenchModel(model)
  });
  if (chartSections.length > 0) {
    if (rowCount > 0) {
      height = Math.max(height, fuelChartStackHeight(chartSections));
    } else {
      height += fuelChartStackHeight(chartSections);
    }
  }
  const hasHeader = Array.isArray(model?.headerItems)
    && model.headerItems.some((item) => String(item?.value || '').trim());
  if (!hasHeader) {
    height = Math.max(80, height - 38);
  }

  return isFuelLapsWorkbenchModel(model)
    ? Math.max(height, 540)
    : height;
}

function isFuelLapsWorkbenchModel(model) {
  if (model?.overlayId !== 'fuel-calculator') {
    return false;
  }

  const status = String(model?.status || '').trim().toLowerCase();
  return status === 'laps workbench'
    || status === 'fuel/lap workbench'
    || status === 'fuel/range workbench'
    || status === 'fuel/target usage workbench'
    || status === 'fuel/plan workbench'
    || status === 'fuel/stint targets workbench'
    || status === 'fuel/stint sequence workbench'
    || status === 'fuel/pit request workbench'
    || status === 'fuel/sector burn workbench';
}

function fuelContentHeight(rowCount, sectionCount, options = {}) {
  const metricRows = overlayGeometry().metricRows || {};
  const minimumHeight = metricGeometryNumber(metricRows, 'minimumFuelCalculatorHeight', 126);
  if (rowCount <= 0 || sectionCount <= 0) return minimumHeight;
  const rowGaps = Math.max(0, rowCount - sectionCount) * metricGeometryNumber(metricRows, 'rowGap', 5);
  const sectionGaps = Math.max(0, sectionCount - 1) * metricGeometryNumber(metricRows, 'sectionGap', 8);
  const segmentedRowHeight = options?.clampToDefault === false
    ? 43
    : metricGeometryNumber(metricRows, 'segmentedRowHeight', 35);
  const height = metricGeometryNumber(metricRows, 'headerChromeHeight', 38)
    + metricGeometryNumber(metricRows, 'fuelContentVerticalPadding', 26)
    + sectionCount * metricGeometryNumber(metricRows, 'fuelSectionTitleReserveHeight', 14)
    + rowCount * segmentedRowHeight
    + rowGaps
    + sectionGaps
    + metricGeometryNumber(metricRows, 'collapsedFooterReserveHeight', 8);
  const maximumHeight = overlaySizeNumber('fuelCalculatorHeight', 315);
  return Math.round(Math.max(
    minimumHeight,
    options?.clampToDefault === false ? height : Math.min(maximumHeight, height)));
}

function fuelChartSectionHeight(section) {
  const height = Number(section?.height);
  return Number.isFinite(height) && height > 0 ? Math.round(height) : 310;
}

function fuelChartStackHeight(sections) {
  const metricRows = overlayGeometry().metricRows || {};
  return metricGeometryNumber(metricRows, 'headerChromeHeight', 38)
    + metricGeometryNumber(metricRows, 'fuelContentVerticalPadding', 26)
    + sections.reduce((total, section) => total + fuelChartSectionHeight(section), 0)
    + Math.max(0, sections.length - 1) * 10
    + metricGeometryNumber(metricRows, 'collapsedFooterReserveHeight', 8);
}

function isSimpleTelemetryModelDrivenSizeOverlay(overlayId) {
  return overlayId === 'pit-service' || overlayId === 'session-weather';
}

function simpleTelemetryBrowserSourceSizeForModel(overlayId, overlayState, previewMode, model, sourceSize) {
  const metricRowCounts = (model?.metricSections || [])
    .map((section) => Array.isArray(section?.rows) ? section.rows.length : 0)
    .filter((count) => count > 0);
  const gridRowCounts = (model?.gridSections || [])
    .map((section) => Array.isArray(section?.rows) ? section.rows.length : 0)
    .filter((count) => count > 0);
  if (metricRowCounts.length === 0 && gridRowCounts.length === 0) {
    return sourceSize;
  }

  const fullWidth = overlayId === 'pit-service'
    ? overlaySizeNumber('pitServiceWidth', 530)
    : overlaySizeNumber('sessionWeatherWidth', 464);
  const fullHeight = overlayId === 'pit-service'
    ? overlaySizeNumber('pitServiceHeight', 707)
    : overlaySizeNumber('sessionWeatherHeight', 496);
  const geometry = overlayGeometry().metricRows || {};
  const hasGridSections = gridRowCounts.length > 0;
  const baseWidth = overlayId === 'pit-service'
    ? hasGridSections
      ? fullWidth
      : Math.min(fullWidth, metricGeometryNumber(geometry, 'pitServiceMetricOnlyWidth', 360))
    : sourceSize.baseWidth;
  const baseHeight = reviewChromeAdjustedBaseHeight(
    overlayId,
    overlayState,
    fullSessionWeatherChromeOffHeight(
      overlayId,
      overlayState,
      previewMode,
      metricRowCounts,
      gridRowCounts,
      fullHeight)
      ?? simpleTelemetryRenderedHeight(metricRowCounts, gridRowCounts, fullHeight),
    previewMode);

  return scaledSourceSize(sourceSize, baseWidth, baseHeight);
}

function fullSessionWeatherChromeOffHeight(overlayId, overlayState, previewMode, metricRowCounts, gridRowCounts, fullHeight) {
  if (overlayId !== 'session-weather') {
    return null;
  }

  const session = sessionKeyFromPreview(previewMode);
  if (chromeEnabled(overlayState, 'header', 'Time remaining', session, true)) {
    return null;
  }

  if (gridRowCounts.some((count) => count > 0)) {
    return null;
  }

  const rowCount = metricRowCounts.reduce((total, count) => total + Math.max(0, Number(count || 0)), 0);
  return rowCount >= 10 ? fullHeight : null;
}

function simpleTelemetryRenderedHeight(metricRowCounts, gridRowCounts, maximumHeight) {
  const geometry = overlayGeometry().metricRows || {};
  const metricHeight = metricSectionsHeight(metricRowCounts, geometry);
  const gridHeight = gridSectionsHeight(gridRowCounts, geometry);
  const contentHeight = metricHeight
    + (metricHeight > 0 && gridHeight > 0 ? metricGeometryNumber(geometry, 'metricGridGap', 10) : 0)
    + gridHeight;
  const height = contentHeight + metricGeometryNumber(geometry, 'pitServiceContentChromeHeight', 38);
  return Math.round(Math.max(
    metricGeometryNumber(geometry, 'minimumSimpleTelemetryHeight', 126),
    Math.min(maximumHeight, height)));
}

function metricSectionsHeight(rowCounts, geometry) {
  const sectionHeights = rowCounts
    .filter((count) => count > 0)
    .map((count) => metricSectionHeight(count, geometry));
  return sectionHeights.reduce((total, value) => total + value, 0)
    + Math.max(0, sectionHeights.length - 1) * metricGeometryNumber(geometry, 'pitServiceSectionGap', 8);
}

function metricSectionHeight(rowCount, geometry) {
  return metricGeometryNumber(geometry, 'sectionTitleHeight', 14)
    + metricGeometryNumber(geometry, 'sectionTitleBottomGap', 6)
    + rowCount * metricGeometryNumber(geometry, 'segmentedRowHeight', 35)
    + Math.max(0, rowCount - 1) * metricGeometryNumber(geometry, 'rowGap', 5);
}

function gridSectionsHeight(rowCounts, geometry) {
  const sectionHeights = rowCounts
    .filter((count) => count > 0)
    .map((count) => gridSectionHeight(count, geometry));
  return sectionHeights.reduce((total, value) => total + value, 0)
    + Math.max(0, sectionHeights.length - 1) * metricGeometryNumber(geometry, 'metricGridGap', 10);
}

function gridSectionHeight(rowCount, geometry) {
  return metricGeometryNumber(geometry, 'metricGridHeaderHeight', 26)
    + metricGeometryNumber(geometry, 'metricGridHeaderBottomGap', 6)
    + rowCount * metricGeometryNumber(geometry, 'metricGridRowHeight', 28)
    + Math.max(0, rowCount - 1) * metricGeometryNumber(geometry, 'metricGridRowGap', 4);
}

function flagsBrowserSourceSizeForModel(model, sourceSize) {
  const count = Array.isArray(model?.flags?.flags) ? model.flags.flags.length : 0;
  const size = flagsSizeForDisplayedFlagCount(count);
  return scaledSourceSize(sourceSize, size.width, size.height);
}

function flagsSizeForDisplayedFlagCount(count) {
  const flagsGeometry = overlayGeometry().flags || {};
  const overlaySizes = overlayGeometry().overlaySizes || {};
  const minimumWidth = flagGeometryNumber(flagsGeometry, 'minimumWidth', 180);
  const minimumHeight = flagGeometryNumber(flagsGeometry, 'minimumHeight', 96);
  if (count <= 1) {
    return { width: minimumWidth, height: minimumHeight };
  }

  const { columns, rows } = flagsGridForCount(count, flagsGeometry);
  const padding = flagGeometryNumber(flagsGeometry, 'outerPadding', 8);
  const gap = flagGeometryNumber(flagsGeometry, 'cellGap', 8);
  const defaultWidth = overlaySizeNumberFrom(overlaySizes, 'flagsWidth', 270);
  const defaultHeight = overlaySizeNumberFrom(overlaySizes, 'flagsHeight', 128);
  const defaultCellWidth = (defaultWidth - 2 * padding - gap) / 2;
  const defaultCellHeight = (defaultHeight - 2 * padding - gap) / 2;
  const width = Math.round(columns * defaultCellWidth + Math.max(0, columns - 1) * gap + 2 * padding);
  const height = Math.round(rows * defaultCellHeight + Math.max(0, rows - 1) * gap + 2 * padding);
  return {
    width: Math.max(minimumWidth, Math.min(flagGeometryNumber(flagsGeometry, 'maximumWidth', 960), width)),
    height: Math.max(minimumHeight, Math.min(flagGeometryNumber(flagsGeometry, 'maximumHeight', 420), height))
  };
}

function flagsGridForCount(count, flagsGeometry) {
  if (count <= 1) return { columns: 1, rows: 1 };
  if (count <= flagGeometryNumber(flagsGeometry, 'gridTwoCountMaximum', 2)) return { columns: 2, rows: 1 };
  if (count <= flagGeometryNumber(flagsGeometry, 'gridFourCountMaximum', 4)) return { columns: 2, rows: 2 };
  if (count <= flagGeometryNumber(flagsGeometry, 'gridSixCountMaximum', 6)) return { columns: 3, rows: 2 };
  const columns = flagGeometryNumber(flagsGeometry, 'gridMaximumColumns', 4);
  return { columns, rows: Math.ceil(count / columns) };
}

function flagGeometryNumber(flagsGeometry, key, fallback) {
  const value = Number(flagsGeometry?.[key]);
  return Number.isFinite(value) ? value : fallback;
}

function reviewChromeAdjustedBaseHeight(overlayId, overlayState, fullHeight, previewMode = 'off') {
  if (!collapsibleBrowserSourceChromeIds.has(overlayId)) {
    return fullHeight;
  }

  const session = sessionKeyFromPreview(previewMode);
  if (chromeEnabled(overlayState, 'header', 'Time remaining', session, true)) {
    return fullHeight;
  }

  return Math.max(80, fullHeight - (overlayId === 'relative'
    ? overlaySizeNumber('relativeHeaderChromeCollapseHeight', 34)
    : 38));
}

function scaledSourceSize(sourceSize, baseWidth, baseHeight) {
  const scale = Number(sourceSize?.scale || 1);
  return {
    ...sourceSize,
    baseWidth: Math.round(baseWidth),
    baseHeight: Math.round(baseHeight),
    width: Math.max(1, Math.round(baseWidth * scale)),
    height: Math.max(1, Math.round(baseHeight * scale))
  };
}

function applySourceSize(target, source) {
  target.baseWidth = source.baseWidth;
  target.baseHeight = source.baseHeight;
  target.width = source.width;
  target.height = source.height;
}

function metricGeometryNumber(metricRows, key, fallback) {
  const value = Number(metricRows?.[key]);
  return Number.isFinite(value) ? value : fallback;
}

function overlaySizeNumber(key, fallback) {
  return overlaySizeNumberFrom(overlayGeometry().overlaySizes || {}, key, fallback);
}

function overlaySizeNumberFrom(overlaySizes, key, fallback) {
  const value = Number(overlaySizes?.[key]);
  return Number.isFinite(value) ? value : fallback;
}

function effectiveRenderedRowCount(model) {
  if (model?.bodyKind === 'stream-chat' && Array.isArray(model?.streamChat?.rows)) {
    return model.streamChat.rows.length;
  }

  return (model?.rows || []).length;
}

function tableColumnKeys(model) {
  return (model?.columns || []).map((column) => String(column?.dataKey || column?.label || ''));
}

function tableRowIdentities(model) {
  return (model?.rows || []).map((row) => {
    const cells = (row?.cells || []).map((cell) => String(cell ?? ''));
    const kind = row?.isClassHeader ? 'class-header' : row?.isPlaceholder ? 'placeholder' : 'row';
    const primary = row?.headerTitle || cells.slice(0, 2).join('/');
    return [kind, primary, visibleRowDetail(row), row?.isReference ? 'reference' : ''].join('|');
  });
}

function visibleRowDetail(row) {
  const detail = row?.headerDetail || '';
  return row?.isClassHeader ? String(detail).toUpperCase() : detail;
}

function placeholderRowCount(model) {
  return (model?.rows || []).filter((row) =>
    row?.isPlaceholder || !(row?.cells || []).some((cell) => String(cell || '').trim())).length;
}

function reviewRenderedProvenance(model, fixture) {
  return {
    evidenceClass: isUnavailableModel(model) ? 'unavailable' : 'synthetic-preview',
    captureSpecific: false,
    sourceContract: 'tools/browser-review/server.mjs',
    syntheticStateKind: fixture ? syntheticStateKind(fixture) : null
  };
}

function syntheticStateKind(fixture) {
  const normalized = String(fixture || '').trim();
  if (!normalized) return null;
  if (normalized === 'input-state-mock-data') return 'input-state-mock-data';
  if (/waiting|no-cars|no-content|no-data|hidden|unavailable/i.test(normalized)) return 'forced-unavailable';
  if (/right|all-kinds|twitch|rich|evidence|graph-only|rail-only|min-scale/i.test(normalized)) return 'forced-preview-state';
  return 'fixture-variant';
}

function isUnavailableModel(model) {
  return model?.shouldRender === false || /waiting|unavailable|hidden/i.test(String(model?.status || ''));
}

function relativeTimingEvidence(overlayId, previewMode, model) {
  if (overlayId !== 'relative') return null;
  return {
    sessionKind: previewMode,
    physicalProximityAvailable: previewMode === 'practice' || previewMode === 'race',
    timingColumnKeys: tableColumnKeys(model).filter((key) => /gap|interval|delta/i.test(key))
  };
}

function tableStatusEvidence(overlayId, model) {
  if (overlayId !== 'standings') return null;
  const rows = model?.rows || [];
  const classHeaderCount = rows.filter((row) => row?.isClassHeader).length;
  const placeholderCount = placeholderRowCount(model);
  const dataRowCount = Math.max(0, rows.length - classHeaderCount - placeholderCount);
  return {
    dataRowCount,
    classHeaderCount,
    placeholderRowCount: placeholderCount,
    clippedRowCount: 0,
    statusCarCount: dataRowCount
  };
}

function timingSanityEvidence(overlayId, model) {
  if (overlayId !== 'standings') return null;
  const timingValues = (model?.rows || [])
    .flatMap((row) => row?.cells || [])
    .map((cell) => parseTimingSeconds(cell))
    .filter(Number.isFinite);
  const maximum = timingValues.length ? Math.max(...timingValues.map(Math.abs)) : 0;
  return {
    maxIntervalGapRatio: Number((maximum / Math.max(1, maximum)).toFixed(3)),
    absurdIntervalCount: timingValues.filter((value) => Math.abs(value) > 600).length
  };
}

function fuelStrategyEvidence(overlayId, model) {
  if (overlayId !== 'fuel-calculator') return null;
  const text = metricModelText(model);
  let additionalFuelNeedState = 'unavailable';
  if (/\bNeed\s+Covered\b|\bCovered\b/i.test(text)) {
    additionalFuelNeedState = 'not-needed';
  } else if (/\bNeed\s+\+\d/i.test(text)) {
    additionalFuelNeedState = 'measured';
  }

  return {
    additionalFuelNeedState: model?.shouldRender === false ? 'unavailable' : additionalFuelNeedState,
    successCopyRequiresMeasuredNeed: true
  };
}

function metricModelText(model) {
  const sections = [
    ...(model?.metricSections || []),
    ...(model?.gridSections || []),
    ...(model?.chartSections || [])
  ];
  return JSON.stringify({
    status: model?.status || '',
    source: model?.source || '',
    metrics: model?.metrics || [],
    sections
  });
}

function layoutDensityEvidence(overlayId, model, browserSource) {
  if (!['fuel-calculator', 'pit-service'].includes(overlayId)) return null;
  const contentRowCount = semanticContentRowCount(model);
  const sectionCount = (model?.metricSections || []).length + (model?.gridSections || []).length;
  const estimatedContentHeight = overlayId === 'fuel-calculator'
    ? fuelContentHeight(contentRowCount, sectionCount, { clampToDefault: !isFuelLapsWorkbenchModel(model) })
    : Math.max(0, 38 + contentRowCount * 30 + sectionCount * 18);
  const height = Number(browserSource?.height || 0);
  const unusedHeightRatio = height > 0
    ? Math.max(0, Math.min(1, (height - estimatedContentHeight) / height))
    : 0;
  return {
    contentRowCount,
    unusedHeightRatio: Number(unusedHeightRatio.toFixed(3))
  };
}

function inputAvailabilityEvidence(overlayId, model) {
  if (overlayId !== 'input-state') return null;
  const inputs = model?.inputs || {};
  return {
    fixtureControlsAvailable: true,
    isAvailable: inputs.isAvailable === true,
    tracePointCount: inputTracePointCount(inputs),
    hasGraph: inputs.hasGraph === true,
    hasRail: inputs.hasRail === true
  };
}

function mapFallbackEvidence(overlayId, model) {
  if (overlayId !== 'track-map') return null;
  if (model?.shouldRender === false) return null;
  const trackMap = model?.trackMap || {};
  const mapKind = trackMap.mapKind || trackMap.renderModel?.mapKind || null;
  if (mapKind !== 'circle') return null;
  return {
    kind: 'circle',
    reason: 'no-generated-track-map',
    currentTrackKey: 'review-fixture-track'
  };
}

function unavailableContentPolicy(model) {
  if (!isUnavailableModel(model)) return null;
  if (isSectionAwareUnavailablePlaceholderModel(model)) return 'section-aware-placeholders';
  if (isChromeOnlyUnavailablePlaceholderModel(model)) return 'chrome-only-placeholder';
  if (!hasSemanticRenderedContent(model)) {
    return 'suppress-rendered-content';
  }
  return null;
}

function isSectionAwareUnavailablePlaceholderModel(model) {
  return model?.overlayId === 'session-weather'
    && Array.isArray(model?.metricSections)
    && model.metricSections.some((section) => (section?.rows || []).length)
    && /weather unavailable/i.test(String(model?.status || ''));
}

function isChromeOnlyUnavailablePlaceholderModel(model) {
  return model?.overlayId === 'standings'
    && model?.shouldRender === true
    && (model?.rows || []).length === 0
    && (model?.headerItems || []).some((item) => String(item?.value || '').trim());
}

function hasSemanticRenderedContent(model) {
  return Boolean(
    (model?.rows || []).length
    || (model?.points || []).length
    || (model?.metrics || []).length
    || (model?.metricSections || []).some((section) => (section.rows || []).length)
    || (model?.gridSections || []).some((section) => (section.rows || []).length)
    || (model?.chartSections || []).some((section) => (section.series || []).length)
    || (model?.graph?.series || []).length
    || (model?.graph?.trendMetrics || []).length
    || model?.inputs?.hasGraph
    || model?.inputs?.hasRail
    || inputTracePointCount(model?.inputs) > 0
    || model?.carRadar?.renderModel?.shouldRender === true
    || (model?.trackMap?.renderModel?.markers || []).length
    || (model?.flags?.flags || []).length
    || (model?.streamChat?.rows || []).length
  );
}

function semanticContentRowCount(model) {
  return (model?.metrics || []).length
    + (model?.metricSections || []).reduce((total, section) => total + (section.rows || []).length, 0)
    + (model?.gridSections || []).reduce((total, section) => total + (section.rows || []).length, 0)
    + (model?.chartSections || []).reduce((total, section) => total + Math.max(1, (section.series || []).length), 0)
    + (model?.rows || []).length
    + (model?.streamChat?.rows || []).length;
}

function inputTracePointCount(inputs) {
  if (Number.isFinite(inputs?.tracePointCount)) return inputs.tracePointCount;
  return Array.isArray(inputs?.trace) ? inputs.trace.length : 0;
}

function parseTimingSeconds(value) {
  const text = String(value || '').trim();
  const match = text.match(/^([+-])?(?:(\d+):)?(\d+(?:\.\d+)?)s?$/i);
  if (!match) return null;
  const sign = match[1] === '-' ? -1 : 1;
  const minutes = match[2] ? Number(match[2]) : 0;
  const seconds = Number(match[3]);
  if (!Number.isFinite(minutes) || !Number.isFinite(seconds)) return null;
  return sign * (minutes * 60 + seconds);
}

function stableEvidenceHash(value) {
  return createHash('sha256').update(stableEvidenceJson(value)).digest('hex').slice(0, 16);
}

function stableEvidenceJson(value) {
  if (Array.isArray(value)) {
    return `[${value.map(stableEvidenceJson).join(',')}]`;
  }
  if (value && typeof value === 'object') {
    return `{${Object.keys(value).sort().map((key) => `${JSON.stringify(key)}:${stableEvidenceJson(value[key])}`).join(',')}}`;
  }
  return JSON.stringify(value);
}

function effectiveSettingsOverlayState(overlayId, overlayState, previewMode = 'off', searchParams = new URLSearchParams()) {
  let effectiveState = overlayState;
  const sectionOffLabels = sectionOffContentLabelsByFixture.get(fixtureVariant(searchParams));
  if (sectionOffLabels) {
    effectiveState = withDisabledContentLabels(effectiveState, sectionOffLabels);
  }

  if (overlayId === 'relative' && fixtureVariant(searchParams) === 'rightmost-evidence') {
    effectiveState = {
      ...effectiveState,
      content: {
        ...(effectiveState.content || {}),
        'Pit status': true,
        'Pit status.race': true,
        'relative.content.relative.pit.enabled': true,
        'relative.content.relative.pit.enabled.race': true
      }
    };
  }

  if (overlayId === 'relative' && fixtureVariant(searchParams) === 'relative-rows-2') {
    effectiveState = {
      ...effectiveState,
      carsEachSide: 2
    };
  }

  if (overlayId === 'standings' && fixtureVariant(searchParams) === 'standings-class-separators-off') {
    effectiveState = {
      ...effectiveState,
      content: {
        ...(effectiveState.content || {}),
        'standings.class-separators.enabled': false,
        'standings.class-separators.enabled.practice': false,
        'standings.class-separators.enabled.qualifying': false,
        'standings.class-separators.enabled.race': false,
        'Class separators': false,
        'Multiclass sections': false
      }
    };
  }

  if (overlayId === 'standings' && fixtureVariant(searchParams) === 'standings-focused-class-only') {
    effectiveState = {
      ...effectiveState,
      otherClassRows: 0
    };
  }

  if (
    (overlayId === 'standings' && fixtureVariant(searchParams) === 'standings-no-content')
    || (overlayId === 'relative' && fixtureVariant(searchParams) === 'relative-no-content')
  ) {
    const session = sessionKeyFromPreview(previewMode);
    effectiveState = {
      ...effectiveState,
      chrome: {
        ...(effectiveState.chrome || {}),
        header: {
          ...(effectiveState.chrome?.header || {}),
          'Time remaining': {
            ...(effectiveState.chrome?.header?.['Time remaining'] || {}),
            [session]: false
          }
        }
      }
    };
  }

  if (fixtureVariant(searchParams).endsWith('-min-scale')) {
    effectiveState = {
      ...effectiveState,
      scalePercent: 60
    };
  }

  if (overlayId === 'garage-cover'
    && fixtureVariant(searchParams).startsWith('garage-')
    && !Object.hasOwn(effectiveState, 'enabled')) {
    effectiveState = {
      ...effectiveState,
      enabled: true
    };
  }

  if (fixtureVariant(searchParams) === 'chrome-off' && collapsibleBrowserSourceChromeIds.has(overlayId)) {
    const session = sessionKeyFromPreview(previewMode);
    effectiveState = {
      ...effectiveState,
      chrome: {
        ...(effectiveState.chrome || {}),
        header: {
          ...(effectiveState.chrome?.header || {}),
          'Time remaining': {
            ...(effectiveState.chrome?.header?.['Time remaining'] || {}),
            [session]: false
          }
        }
      }
    };
  }

  return effectiveState;
}

function withDisabledContentLabels(overlayState, labels) {
  const content = {
    ...(overlayState?.content || {})
  };
  for (const label of labels) {
    content[label] = false;
  }

  return {
    ...overlayState,
    content
  };
}

function reviewEffectiveSettingList(overlayId, overlayState, session, searchParams = new URLSearchParams()) {
  const settings = [
    effectiveSetting('overlayEnabled', overlayState.enabled === true),
    effectiveSetting(`session.${session}.enabled`, overlaySessionEnabled(overlayId, overlayState, session)),
    effectiveSetting('general.unitSystem', reviewAppState.unitSystem),
    effectiveSetting('scalePercent', clampInteger(overlayState?.scalePercent, 100, 60, 200)),
    effectiveSetting(
      'opacityPercent',
      clampInteger(
        overlayState?.opacityPercent,
        overlayId === 'track-map' ? 0 : 100,
        overlayId === 'track-map' ? 0 : 20,
        100))
  ];

  if (overlayId === 'relative') {
    settings.push(effectiveSetting('carsEachSide', clampInteger(overlayState?.carsEachSide, 3, 0, 8)));
  }

  if (overlayId === 'standings') {
    settings.push(
      effectiveContentSetting(overlayState, session, 'standings.class-separators.enabled', 'Class separators', true),
      effectiveSetting('carsInClass', clampInteger(overlayState?.carsInClass, 14, 1, 24)),
      effectiveSetting('otherClassRows', clampInteger(overlayState?.otherClassRows, 2, 0, 6))
    );
  }

  if (overlayId === 'gap-to-leader') {
    const carsAhead = clampInteger(overlayState?.carsAhead, 5, 0, 12);
    const carsBehind = clampInteger(overlayState?.carsBehind, 5, 0, 12);
    settings.push(
      effectiveSetting('carsAhead', carsAhead),
      effectiveSetting('carsBehind', carsBehind),
      effectiveSetting('gap.cars-window', { carsAhead, carsBehind })
    );
  }

  if (overlayId === 'stream-chat') {
    settings.push(effectiveSetting('stream-chat.provider', reviewSettings(overlayId, 'race', searchParams).provider));
  }

  if (overlayId === 'car-radar') {
    settings.push(
      effectiveContentSetting(overlayState, session, 'radar.multiclass-warning', 'Faster-class warning', true),
      effectiveSetting('radar.multiclass-warning-seconds', clampInteger(overlayState?.multiclassWarningSeconds, 5, 3, 10)),
      effectiveSetting('radar.visibility-seconds', clampInteger(overlayState?.radarVisibilitySeconds, 2, 2, 5)));
  }

  if (overlayId === 'garage-cover') {
    settings.push(
      effectiveSetting('garage-cover.previewVisible', reviewSettings(overlayId, 'race', searchParams).previewVisible === true),
      effectiveContentSetting(overlayState, session, 'Content', 'Content', true)
    );
  }

  if (supportsSharedChrome(overlayId)) {
    settings.push(effectiveSetting(
      `chrome.header.time-remaining.${session}`,
      chromeEnabled(overlayState, 'header', 'Time remaining', session, true),
      session));
  }

  if (overlayId === 'input-state') {
    settings.push(effectiveSetting(
      'input-state.trace.*',
      contentLabelsEnabled(overlayState, [
        'input-state.trace.throttle',
        'input-state.trace.brake',
        'input-state.trace.clutch',
        'Throttle trace',
        'Brake trace',
        'Clutch trace'
      ], true, session),
      session));
  }

  for (const row of reviewEffectiveContentRows(overlayId)) {
    settings.push(effectiveContentSetting(overlayState, session, row.key, row.label, row.defaultEnabled));
  }

  return settings;
}

function overlaySessionEnabled(overlayId, overlayState, session) {
  if (Object.hasOwn(overlayState?.sessions || {}, session)) {
    return overlayState.sessions[session] === true;
  }

  return overlayId === 'gap-to-leader' ? session === 'race' : true;
}

function effectiveSetting(key, value, session = null) {
  return session
    ? { key, value, session }
    : { key, value };
}

function effectiveContentSetting(overlayState, session, key, label, defaultEnabled) {
  return effectiveSetting(
    key,
    contentLabelsEnabled(overlayState, [key, label].filter(Boolean), defaultEnabled, session),
    session);
}

function reviewEffectiveContentRows(overlayId) {
  return {
    standings: [
      ['standings.content.standings.class-position.enabled', 'Class position', true],
      ['standings.content.standings.car-number.enabled', 'Car number', true],
      ['standings.content.standings.driver.enabled', 'Driver', true],
      ['standings.content.standings.gap.enabled', 'Class gap', true],
      ['standings.content.standings.interval.enabled', 'Previous interval', true],
      ['standings.content.standings.fastest-lap.enabled', 'Fastest lap', true],
      ['standings.content.standings.last-lap.enabled', 'Last lap', true],
      ['standings.content.standings.pit.enabled', 'Pit status', true]
    ],
    relative: [
      ['relative.content.relative.position.enabled', 'Relative position', true],
      ['relative.content.relative.driver.enabled', 'Driver', true],
      ['relative.content.relative.gap.enabled', 'Relative delta', true],
      ['relative.content.relative.pit.enabled', 'Pit status', false]
    ],
    'fuel-calculator': [
      ['fuel-calculator.race.plan.enabled', 'Plan', true],
      ['fuel-calculator.race.fuel.enabled', 'Fuel', true],
      ['fuel-calculator.race.stint-targets.enabled', 'Stint targets', true],
      ['fuel-calculator.range.fuel.enabled', 'Fuel range', true],
      ['fuel-calculator.usage.enabled', 'Fuel usage', true],
      ['fuel-calculator.model-readiness.enabled', 'Model readiness', true]
    ],
    'gap-to-leader': [
      ['gap.graph.enabled', 'Graph', true],
      ['gap.trend.last.enabled', 'Last', true],
      ['gap.trend.5l.enabled', '5L', true],
      ['gap.trend.10l.enabled', '10L', true],
      ['gap.trend.pit.enabled', 'Pit', true],
      ['gap.trend.pit-lap.enabled', 'PLap', true],
      ['gap.trend.stint.enabled', 'Stint', true],
      ['gap.trend.tire.enabled', 'Tire', true],
      ['gap.trend.status.enabled', 'Status', true]
    ],
    'track-map': [
      ['track-map.sector-boundaries.enabled', 'Sector boundaries', true]
    ],
    'stream-chat': [
      ['stream-chat.twitch.author-color', 'Author color', true],
      ['stream-chat.twitch.badges', 'Badges', true],
      ['stream-chat.twitch.bits', 'Bits', true],
      ['stream-chat.twitch.first-message', 'First message', true],
      ['stream-chat.twitch.replies', 'Replies', true],
      ['stream-chat.twitch.timestamps', 'Timestamps', true],
      ['stream-chat.twitch.emotes', 'Emotes', true],
      ['stream-chat.twitch.alerts', 'Alerts', true],
      ['stream-chat.twitch.message-ids', 'Message IDs', false]
    ],
    'input-state': [
      ['input-state.trace.throttle', 'Throttle trace', true],
      ['input-state.trace.brake', 'Brake trace', true],
      ['input-state.trace.clutch', 'Clutch trace', true],
      ['input-state.current.throttle', 'Throttle %', true],
      ['input-state.current.brake', 'Brake %', true],
      ['input-state.current.clutch', 'Clutch %', true],
      ['input-state.current.steering', 'Steering wheel', true],
      ['input-state.current.gear', 'Gear', true],
      ['input-state.current.speed', 'Speed', true]
    ],
    'car-radar': [],
    flags: [
      ['flags.show-green', 'Green / start / ready', true],
      ['flags.show-blue', 'Blue', true],
      ['flags.show-yellow', 'Yellow / debris / caution', true],
      ['flags.show-critical', 'Red / black / repair', true],
      ['flags.show-finish', 'White / checkered / final laps', true]
    ],
    'session-weather': [
      ['session-weather.session.type.enabled', 'Session type', true],
      ['session-weather.session.name.enabled', 'Session name', true],
      ['session-weather.session.mode.enabled', 'Session mode', true],
      ['session-weather.clock.elapsed.enabled', 'Elapsed time', true],
      ['session-weather.clock.remaining.enabled', 'Remaining time', true],
      ['session-weather.clock.total.enabled', 'Total time', true],
      ['session-weather.event.type.enabled', 'Event type', true],
      ['session-weather.event.car.enabled', 'Car', true],
      ['session-weather.track.name.enabled', 'Track name', true],
      ['session-weather.track.length.enabled', 'Track length', true],
      ['session-weather.laps.remaining.enabled', 'Laps remaining', true],
      ['session-weather.laps.total.enabled', 'Laps total', true],
      ['session-weather.surface.wetness.enabled', 'Wetness', true],
      ['session-weather.surface.declared.enabled', 'Declared surface', true],
      ['session-weather.surface.rubber.enabled', 'Rubber', true],
      ['session-weather.sky.skies.enabled', 'Skies', true],
      ['session-weather.sky.weather.enabled', 'Weather', true],
      ['session-weather.sky.rain.enabled', 'Rain', true],
      ['session-weather.wind.direction.enabled', 'Wind direction', true],
      ['session-weather.wind.speed.enabled', 'Wind speed', true],
      ['session-weather.wind.facing.enabled', 'Facing wind', true],
      ['session-weather.temps.air.enabled', 'Air temp', true],
      ['session-weather.temps.track.enabled', 'Track temp', true],
      ['session-weather.atmosphere.humidity.enabled', 'Humidity', true],
      ['session-weather.atmosphere.fog.enabled', 'Fog', true],
      ['session-weather.atmosphere.pressure.enabled', 'Pressure', true]
    ],
    'pit-service': [
      ['pit-service.session.time.enabled', 'Session time', true],
      ['pit-service.session.laps.enabled', 'Session laps', true],
      ['pit-service.signal.release.enabled', 'Release', true],
      ['pit-service.signal.status.enabled', 'Pit status', true],
      ['pit-service.service.fuel-requested.enabled', 'Fuel requested', true],
      ['pit-service.service.fuel-selected.enabled', 'Fuel selected', true],
      ['pit-service.service.tearoff-requested.enabled', 'Tearoff requested', true],
      ['pit-service.service.repair-required.enabled', 'Required repair', true],
      ['pit-service.service.repair-optional.enabled', 'Optional repair', true],
      ['pit-service.service.fast-repair-selected.enabled', 'Fast repair selected', true],
      ['pit-service.service.fast-repair-available.enabled', 'Fast repairs available', true],
      ['pit-service.tire-analysis.compound', 'Compound', true],
      ['pit-service.tire-analysis.change', 'Change request', true],
      ['pit-service.tire-analysis.set-limit', 'Set limit', true],
      ['pit-service.tire-analysis.sets-available', 'Sets available', true],
      ['pit-service.tire-analysis.sets-used', 'Sets used', true],
      ['pit-service.tire-analysis.pressure', 'Pressure', true],
      ['pit-service.tire-analysis.temperature', 'Temperature', true],
      ['pit-service.tire-analysis.wear', 'Wear', true],
      ['pit-service.tire-analysis.distance', 'Distance', true]
    ]
  }[overlayId]?.map(([key, label, defaultEnabled]) => ({ key, label, defaultEnabled })) || [];
}

function reviewDisplayModel(overlayId, previewMode = 'off', searchParams = new URLSearchParams()) {
  const fixture = fixtureVariant(searchParams);
  const withChrome = (model) => applyReviewChrome(model, overlayId, previewMode, fixture === 'chrome-off');

  if (assetBackedReviewOverlayModelIds.has(overlayId)) {
    return withChrome(reviewAssetBackedDisplayModel(overlayId, previewMode, searchParams));
  }

  const overlayState = effectiveSettingsOverlayState(
    overlayId,
    reviewAppState.overlays[overlayId] || {},
    previewMode,
    searchParams);
  const session = sessionKeyFromPreview(previewMode);
  const previewLabel = previewMode === 'off' ? 'review fixture' : `${previewMode} preview`;
  // BrowserOverlayModelFactory is a C# application service and cannot be executed
  // directly from this Node review server. The fallback builders below emit the
  // BrowserOverlayDisplayModel JSON contract used by production browser sources.
  switch (overlayId) {
    case 'standings':
      return withChrome(filterTableModelContent(filterStandingsReviewRows(standingsDisplayModel(previewLabel, session, fixture), overlayState), 'standings', overlayState, session, fixture));
    case 'relative':
      {
        const effectiveOverlayState = fixture === 'rightmost-evidence'
          ? {
              ...overlayState,
              content: {
                ...(overlayState.content || {}),
                'Pit status': true,
                'Pit status.race': true,
                'relative.content.relative.pit.enabled': true,
                'relative.content.relative.pit.enabled.race': true
              }
            }
          : fixture === 'relative-rows-2'
            ? {
                ...overlayState,
                carsEachSide: 2
              }
          : overlayState;
        const relativeModel = fixture === 'relative-empty-rows'
          ? relativeFocusOnlyDisplayModel(previewLabel, session)
          : relativeDisplayModel(previewLabel, session);
        return withChrome(filterTableModelContent(filterRelativeReviewRows(relativeModel, effectiveOverlayState), 'relative', effectiveOverlayState, session));
      }
    case 'fuel-calculator':
      {
        if (fixture === 'fuel-waiting' || fixture === 'fuel-no-data') {
          return withChrome(metricsModel(
            'fuel-calculator',
            'Fuel Calculator',
            fixture === 'fuel-no-data' ? 'waiting for fuel telemetry' : 'waiting for local fuel context',
            [],
            'source: waiting',
            [],
            [],
            [],
            false));
        }

        if (fixture === 'fuel-dallara-35m-v1') {
          return withChrome(fuelDallara35mV1DisplayModel(overlayState, session));
        }

        if (fixture === 'fuel-laps-workbench-fuel') {
          return withChrome(fuelPerLapWorkbenchReviewModel('populated'));
        }

        if (fixture === 'fuel-laps-workbench-fuel-degraded') {
          return withChrome(fuelPerLapWorkbenchReviewModel('degraded'));
        }

        if (fixture === 'fuel-laps-workbench-fuel-no-data') {
          return withChrome(fuelPerLapWorkbenchReviewModel('no-data'));
        }

        if (fixture === 'fuel-laps-workbench-fuel-trusted-seed') {
          return withChrome(fuelPerLapWorkbenchReviewModel('trusted-seed'));
        }

        if (fixture === 'fuel-laps-workbench-laps') {
          return withChrome(fuelLapsWorkbenchReviewModel({ includeLapRows: true, activeWorkbench: 'range' }));
        }

        if (fixture === 'fuel-laps-workbench-range') {
          return withChrome(fuelLapsWorkbenchReviewModel({ activeWorkbench: 'range' }));
        }

        if (fixture === 'fuel-laps-workbench-target') {
          return withChrome(fuelLapsWorkbenchReviewModel({ activeWorkbench: 'target' }));
        }

        if (fixture === 'fuel-laps-workbench-capacity') {
          return withChrome(fuelLapsWorkbenchReviewModel({ activeWorkbench: 'capacity' }));
        }

        if (fixture === 'fuel-laps-workbench-checkpoints') {
          return withChrome(fuelLapsWorkbenchReviewModel({ activeWorkbench: 'checkpoints' }));
        }

        if (fixture === 'fuel-laps-workbench-boundary') {
          return withChrome(fuelLapsWorkbenchReviewModel({ activeWorkbench: 'boundary' }));
        }

        if (fixture === 'fuel-laps-workbench-snapshot') {
          return withChrome(fuelLapsWorkbenchReviewModel({ activeWorkbench: 'snapshot' }));
        }

        if (fixture === 'fuel-laps-workbench-sector') {
          return withChrome(fuelLapsWorkbenchReviewModel({ activeWorkbench: 'sector' }));
        }

        if (fixture === 'fuel-laps-workbench-pit') {
          return withChrome(fuelLapsWorkbenchReviewModel({ activeWorkbench: 'pit' }));
        }

        if (fixture === 'fuel-laps-workbench-plan') {
          return withChrome(fuelLapsWorkbenchReviewModel({ activeWorkbench: 'plan' }));
        }

        if (fixture === 'fuel-v2-composite-dallara-35m') {
          return withChrome(fuelV2CompositeWorkbenchReviewModel('dallara-35m'));
        }

        if (fixture === 'fuel-laps-workbench-stint-n'
            || fixture === 'fuel-v2-composite-vln-full-race') {
          return withChrome(fuelV2CompositeWorkbenchReviewModel('vln-full-race'));
        }

        if (fixture === 'fuel-v2-composite-current-service') {
          return withChrome(fuelV2CompositeWorkbenchReviewModel('current-service'));
        }

        if (fixture === 'fuel-v2-bottom-half-no-data') {
          return withChrome(fuelV2BottomHalfStateGateReviewModel('no-data'));
        }

        if (fixture === 'fuel-v2-bottom-half-dallara-three-lap-control') {
          return withChrome(fuelV2BottomHalfStateGateReviewModel('dallara-three-lap-control'));
        }

        if (fixture === 'fuel-v2-bottom-half-dallara-four-lap') {
          return withChrome(fuelV2BottomHalfStateGateReviewModel('dallara-four-lap'));
        }

        if (fixture === 'fuel-v2-bottom-half-endurance-stint-five') {
          return withChrome(fuelV2BottomHalfStateGateReviewModel('endurance-stint-five'));
        }

        if (fixture === 'fuel-v2-bottom-half-endurance-stint-six') {
          return withChrome(fuelV2BottomHalfStateGateReviewModel('endurance-stint-six'));
        }

        if (fixture === 'fuel-v2-bottom-half-dallara-timed') {
          return withChrome(fuelV2BottomHalfStateGateReviewModel('dallara-timed'));
        }

        if (fixture === 'fuel-v2-bottom-half-charlotte-degraded') {
          return withChrome(fuelV2BottomHalfStateGateReviewModel('charlotte-degraded'));
        }

        if (fixture === 'fuel-v2-model-readiness-test-fresh-combo') {
          return withChrome(fuelV2ModelReadinessReviewModel('test-fresh-combo'));
        }

        if (fixture === 'fuel-v2-model-readiness-test-fuel-baseline') {
          return withChrome(fuelV2ModelReadinessReviewModel('test-fuel-baseline'));
        }

        if (fixture === 'fuel-v2-model-readiness-practice-pit-service') {
          return withChrome(fuelV2ModelReadinessReviewModel('practice-pit-service'));
        }

        if (fixture === 'fuel-laps-workbench' || fixture === 'fuel-laps-workbench-stint') {
          return withChrome(fuelLapsWorkbenchReviewModel({ activeWorkbench: 'stint' }));
        }

        const calculating = fixture === 'fuel-calculating';
        const raceRows = calculating
          ? [
              metricRow('Plan', '31 laps | Calculating | Calculating', 'waiting', [
                metricSegment('Race', '31 laps', 'info'),
                metricSegment('Remain', '30.4 laps', 'info'),
                metricSegment('Stints', 'Calculating', 'waiting'),
                metricSegment('Stops', 'Calculating', 'waiting'),
                metricSegment('Save', 'Calculating', 'waiting')
              ]),
              metricRow('Fuel', `${formatFuelVolume(74.0)} | Calculating | Calculating`, 'waiting', [
                metricSegment('Current', formatFuelVolume(74.0), 'info'),
                metricSegment('Burn', 'Calculating', 'waiting'),
                metricSegment('Tank', 'Calculating', 'waiting'),
                metricSegment('Need', 'Calculating', 'waiting')
              ])
            ]
          : [
              metricRow('Plan', '31 laps | 3 stints | 2 stops', 'info', [
                metricSegment('Race', '31 laps', 'info'),
                metricSegment('Remain', '30.4 laps', 'info'),
                metricSegment('Stints', '3', 'info'),
                metricSegment('Stops', '2', 'info'),
                metricSegment('Save', formatFuelPerLap(0.2), 'warning')
              ]),
              metricRow('Fuel', `${formatFuelVolume(74.0)} | ${formatFuelPerLap(3.1)} | Covered`, 'success', [
                metricSegment('Current', formatFuelVolume(74.0), 'info'),
                metricSegment('Burn', formatFuelPerLap(3.1), 'info'),
                metricSegment('Tank', '34.2 laps', 'info'),
                metricSegment('Need', 'Covered', 'success')
              ])
            ];
        const stintRows = calculating ? [] : [
          metricRow('Stint 1', `12 laps | target ${formatFuelPerLap(3.1)}`, 'info', [
            metricSegment('Laps', '12 laps', 'info'),
            metricSegment('Target', formatFuelPerLap(3.1), 'info'),
            metricSegment('Save', formatFuelPerLap(0.2), 'warning')
          ]),
          metricRow('Stint 2', `12 laps | target ${formatFuelPerLap(3.1)}`, 'info', [
            metricSegment('Laps', '12 laps', 'info'),
            metricSegment('Target', formatFuelPerLap(3.1), 'info'),
            metricSegment('Save', 'None', 'success')
          ]),
          metricRow('Stint 3', `7 laps final | target ${formatFuelPerLap(3.1)}`, 'info', [
            metricSegment('Laps', '7 laps', 'info'),
            metricSegment('Target', formatFuelPerLap(3.1), 'info'),
            metricSegment('Save', 'None', 'success')
          ])
        ];
        const usageLabel = session === 'qualifying'
          ? 'Quali Usage'
          : session === 'practice'
            ? 'Practice Usage'
            : session === 'test'
              ? 'Test Usage'
              : null;
        const usageSection = usageLabel == null ? null : {
          title: 'Fuel Usage',
          rows: [
            metricRow(usageLabel, `min ${formatFuelPerLap(3.0)} | avg ${formatFuelPerLap(3.1)} | max ${formatFuelPerLap(3.2)}`, 'info', [
              metricSegment('Min', formatFuelPerLap(3.0), 'info'),
              metricSegment('Avg', formatFuelPerLap(3.1), 'info'),
              metricSegment('Max', formatFuelPerLap(3.2), 'info'),
              metricSegment('Laps', '3 laps', 'info')
            ])
          ]
        };
        let metricSections = usageLabel == null
          ? [{ title: 'Race Information', rows: raceRows }]
          : [
              {
                title: 'Fuel Range',
                rows: [
                  metricRow('Fuel', `${formatFuelVolume(74.0)} | range 23.9 laps | tank 34.2 laps`, 'info', [
                    metricSegment('Level', formatFuelVolume(74.0), 'info'),
                    metricSegment('Usage', formatFuelPerLap(3.1), 'info'),
                    metricSegment('Range', '23.9 laps', 'info'),
                    metricSegment('Tank', '34.2 laps', 'info')
                  ])
                ]
              },
              usageSection
            ].filter(Boolean);
        if (usageLabel != null) {
          metricSections = filterMetricSectionsByContent('fuel-calculator', metricSections, overlayState, session);
          return withChrome(metricsModel(
            'fuel-calculator',
            'Fuel Calculator',
            'fuel range',
            metricSections.flatMap((section) => section.rows),
            `usage ${formatFuelPerLap(3.1)} (measured green lap) | range 23.9 laps | 34.2 laps/tank | history user | measured min/avg/max 3.0/3.1/3.2 L/lap`,
            [],
            metricSections,
            [
              { key: 'timeRemaining', value: '06:37:08' }
            ]));
        }
        if (stintRows.length > 0) {
          metricSections.push({ title: 'Stint Targets', rows: stintRows });
        }
        metricSections = filterMetricSectionsByContent('fuel-calculator', metricSections, overlayState, session);
        return withChrome(metricsModel(
          'fuel-calculator',
          'Fuel Calculator',
          calculating ? 'calculating strategy' : '3 stints / 2 stops',
          metricSections.flatMap((section) => section.rows),
          calculating
            ? 'burn Calculating (unavailable) | Calculating | history user | gap O0.18 C0.04'
            : `burn ${formatFuelPerLap(3.1)} (measured green lap) | 34.2 laps/tank | history user | gap O0.18 C0.04`,
          [],
          metricSections,
          [
            { key: 'timeRemaining', value: '06:37:08' }
          ]));
      }
    case 'session-weather':
      {
        if (fixture === 'session-weather-no-data') {
          return withChrome(metricsModel(
            'session-weather',
            'Session / Weather',
            'waiting for session telemetry',
            [],
            '',
            [],
            [],
            [],
            false));
        }

        if (fixture === 'session-weather-missing') {
          const clock = reviewSessionWeatherClock('race');
          const laps = reviewSessionWeatherLaps('race');
          const sessionRows = [
            metricRow('Session', 'Race | race preview | Team', 'normal', [
              metricSegment('Type', 'Race', 'normal'),
              metricSegment('Name', 'race preview', 'normal'),
              metricSegment('Mode', 'Team', 'normal')
            ]),
            metricRow('Clock', `${clock.elapsed} | ${clock.left} | ${clock.total}`, 'normal', [
              metricSegment('Elapsed', clock.elapsed, 'normal'),
              metricSegment('Left', clock.left, 'normal'),
              metricSegment('Total', clock.total, 'normal')
            ]),
            metricRow('Event', 'Race | Aston Martin Vantage GT3 EVO', 'normal', [
              metricSegment('Event', 'Race', 'normal'),
              metricSegment('Car', 'Aston Martin Vantage GT3 EVO', 'normal')
            ]),
            metricRow('Track', 'Gesamtstrecke 24h | 25.4 km', 'normal', [
              metricSegment('Name', 'Gesamtstrecke 24h', 'normal'),
              metricSegment('Length', formatDistance(25380), 'normal')
            ]),
            metricRow('Laps', `${laps.remaining} | ${laps.total}`, 'normal', [
              metricSegment('Remaining', laps.remaining, 'normal'),
              metricSegment('Total', laps.total, 'normal')
            ])
          ];
          const weatherRows = [
            metricRow('Surface', '-- | -- | --', 'waiting', [
              metricSegment('Wetness', '--', 'waiting'),
              metricSegment('Declared', '--', 'waiting'),
              metricSegment('Rubber', '--', 'waiting')
            ]),
            metricRow('Sky', '-- | -- | --', 'waiting', [
              metricSegment('Skies', '--', 'waiting'),
              metricSegment('Weather', '--', 'waiting'),
              metricSegment('Rain', '--', 'waiting')
            ]),
            metricRow('Wind', '-- | -- | --', 'waiting', [
              metricSegment('Dir', '--', 'waiting'),
              metricSegment('Speed', '--', 'waiting'),
              metricSegment('Facing', '--', 'waiting')
            ]),
            metricRow('Temps', '-- | --', 'waiting', [
              metricSegment('Air', '--', 'waiting'),
              metricSegment('Track', '--', 'waiting')
            ]),
            metricRow('Atmosphere', '-- | -- | --', 'waiting', [
              metricSegment('Hum', '--', 'waiting'),
              metricSegment('Fog', '--', 'waiting'),
              metricSegment('Pressure', '--', 'waiting')
            ])
          ];
          const metricSections = [
            { title: 'Session', rows: sessionRows },
            { title: 'Weather', rows: weatherRows }
          ];
          return withChrome(metricsModel('session-weather', 'Session / Weather', 'weather unavailable', metricSections.flatMap((section) => section.rows), 'weather source unavailable | session data present', [], metricSections, [
            { key: 'timeRemaining', value: clock.left }
          ]));
        }
        const sessionType = session === 'qualifying' ? 'Qualify' : sessionDisplayName(session);
        const sessionName = sessionDisplayName(session);
        const reviewAirTempC = 22;
        const reviewTrackTempC = 31;
        const clock = reviewSessionWeatherClock(session);
        const laps = reviewSessionWeatherLaps(session);
        const rubber = session === 'race' ? 'Moderate Usage' : 'Clean';
        const sessionRows = [
          metricRow('Session', `${sessionType} | ${previewLabel} | Team`, 'normal', [
            metricSegment('Type', sessionType, 'normal'),
            metricSegment('Name', previewLabel, 'normal'),
            metricSegment('Mode', 'Team', 'normal')
          ]),
          metricRow('Clock', `${clock.elapsed} | ${clock.left} | ${clock.total}`, 'normal', [
            metricSegment('Elapsed', clock.elapsed, 'normal'),
            metricSegment('Left', clock.left, 'normal'),
            metricSegment('Total', clock.total, 'normal')
          ]),
          metricRow('Event', `${sessionType} | Aston Martin Vantage GT3 EVO`, 'normal', [
            metricSegment('Event', sessionType, 'normal'),
            metricSegment('Car', 'Aston Martin Vantage GT3 EVO', 'normal')
          ]),
          metricRow('Track', `Gesamtstrecke 24h | ${formatDistance(25380)}`, 'normal', [
            metricSegment('Name', 'Gesamtstrecke 24h', 'normal'),
            metricSegment('Length', formatDistance(25380), 'normal')
          ])
        ];
        if (session === 'race') {
          sessionRows.push(metricRow('Laps', `${laps.remaining} | ${laps.total}`, 'normal', [
            metricSegment('Remaining', laps.remaining, 'normal'),
            metricSegment('Total', laps.total, 'normal')
          ]));
        }
        const weatherRows = [
          metricRow('Surface', `Unknown | Dry | ${rubber}`, 'normal', [
            metricSegment('Wetness', 'Unknown', 'waiting'),
            metricSegment('Declared', 'Dry', 'normal'),
            metricSegment('Rubber', rubber, 'normal')
          ]),
          metricRow('Sky', 'Mostly Cloudy | Dynamic | 0%', 'normal', [
            metricSegment('Skies', 'Mostly Cloudy', 'normal'),
            metricSegment('Weather', 'Dynamic', 'normal'),
            metricSegment('Rain', '0%', 'normal')
          ]),
          metricRow('Wind', `NE | ${formatSpeed(10 / 3.6)} | Head`, 'normal', [
            metricSegment('Dir', 'NE', 'normal'),
            metricSegment('Speed', formatSpeed(10 / 3.6), 'normal'),
            metricSegment('Facing', 'Head', 'normal', { rotationDegrees: 0 })
          ]),
          metricRow('Temps', `${formatTemperature(reviewAirTempC)} | ${formatTemperature(reviewTrackTempC)}`, temperatureTone(reviewTrackTempC), [
            metricSegment('Air', formatTemperature(reviewAirTempC), temperatureTone(reviewAirTempC), { accentHex: temperatureAccentHex(reviewAirTempC) }),
            metricSegment('Track', formatTemperature(reviewTrackTempC), temperatureTone(reviewTrackTempC), { accentHex: temperatureAccentHex(reviewTrackTempC) })
          ]),
          metricRow('Atmosphere', `48% | 0% | ${formatAirPressure(101300)}`, 'normal', [
            metricSegment('Hum', '48%', 'normal'),
            metricSegment('Fog', '0%', 'normal'),
            metricSegment('Pressure', formatAirPressure(101300), 'normal')
          ])
        ];
        const metricSections = filterMetricSectionsByContent('session-weather', [
          { title: 'Session', rows: sessionRows },
          { title: 'Weather', rows: weatherRows }
        ], overlayState, session);
        return withChrome(metricsModel('session-weather', 'Session / Weather', sessionType, metricSections.flatMap((section) => section.rows), '', [], metricSections, [
          { key: 'timeRemaining', value: clock.left }
        ]));
      }
    case 'pit-service':
      {
        if (fixture === 'pit-service-no-data') {
          return withChrome(metricsModel(
            'pit-service',
            'Pit Service',
            'waiting for pit telemetry',
            [],
            'source: waiting',
            [],
            [],
            [],
            false));
        }

        if (fixture === 'pit-service-idle') {
          const pitSections = [
            {
              title: 'Session',
              rows: [
                metricRow('Time / Laps', '03:58 | 148/179 laps', 'normal', [
                  metricSegment('Time', '03:58', 'normal'),
                  metricSegment('Laps', '148/179 laps', 'normal')
                ])
              ]
            },
            {
              title: 'Pit Signal',
              rows: [
                metricRow('Release', 'GREEN - pit ready', 'success', undefined, { rowColorHex: '#62FF9F' }),
                metricRow('Pit status', 'idle', 'normal')
              ]
            },
            {
              title: 'Service Request',
              rows: [
                metricRow('Fuel request', 'No | --', 'normal', [
                  metricSegment('Requested', 'No', 'normal'),
                  metricSegment('Selected', '--', 'waiting')
                ]),
                metricRow('Tearoff', 'No', 'normal', [
                  metricSegment('Requested', 'No', 'normal')
                ]),
                metricRow('Repair', '-- | --', 'normal', [
                  metricSegment('Required', '--', 'normal'),
                  metricSegment('Optional', '--', 'normal')
                ]),
                metricRow('Fast repair', 'No | 1', 'normal', [
                  metricSegment('Selected', 'No', 'normal'),
                  metricSegment('Available', '1', 'success')
                ])
              ]
            }
          ];
          const tireRows = [
            gridRow('Compound', ['--', '--', '--', '--']),
            gridRow('Change request', ['Keep', 'Keep', 'Keep', 'Keep']),
            gridRow('Set limit', ['4 sets', '4 sets', '4 sets', '4 sets']),
            gridRow('Sets available', ['2', '2', '2', '2']),
            gridRow('Sets used', ['2', '2', '2', '2']),
            gridRow('Pressure', ['--', '--', '--', '--']),
            gridRow('Temperature', ['--', '--', '--', '--']),
            gridRow('Wear', ['--', '--', '--', '--']),
            gridRow('Distance', ['--', '--', '--', '--'])
          ];
          return withChrome(metricsModel('pit-service', 'Pit Service', 'pit ready', pitSections.flatMap((section) => section.rows), 'source: player/team pit service telemetry', [
            {
              title: 'Tire Analysis',
              headers: ['Info', 'FL', 'FR', 'RL', 'RR'],
              rows: tireRows
            }
          ], pitSections, [
            { key: 'timeRemaining', value: '00:03:58' }
          ]));
        }
        const pitSessionRows = session === 'race'
          ? [
              metricRow('Time / Laps', '03:58 | 148/179 laps', 'normal', [
                metricSegment('Time', '03:58', 'normal'),
                metricSegment('Laps', '148/179 laps', 'normal')
              ])
            ]
          : [
              metricRow('Time', '03:58', 'normal', [
                metricSegment('Time', '03:58', 'normal')
              ])
            ];
        const pitSections = filterMetricSectionsByContent('pit-service', [
          {
          title: 'Session',
          rows: pitSessionRows
          },
          {
          title: 'Pit Signal',
          rows: [
            metricRow('Release', 'RED - service active', 'error', undefined, { rowColorHex: '#FF6274' }),
            metricRow('Pit status', 'in progress', 'error', undefined, { rowColorHex: '#FF6274' })
          ]
          },
          {
          title: 'Service Request',
          rows: [
            metricRow('Fuel request', `Yes | ${formatFuelVolume(31.6)}`, 'normal', [
              metricSegment('Requested', 'Yes', 'success'),
              metricSegment('Selected', formatFuelVolume(31.6), 'info')
            ]),
            metricRow('Tearoff', 'Yes', 'normal', [
              metricSegment('Requested', 'Yes', 'success')
            ]),
            metricRow('Repair', '12s | 18s', 'error', [
              metricSegment('Required', '12s', 'error'),
              metricSegment('Optional', '18s', 'warning')
            ]),
            metricRow('Fast repair', 'Yes | 1', 'normal', [
              metricSegment('Selected', 'Yes', 'success'),
              metricSegment('Available', '1', 'success')
            ])
          ]
          }
        ], overlayState, session);
        const tireRows = filterGridRowsByContent('pit-service', [
          gridRow('Compound', [
            gridCell('S', 'success'),
            gridCell('S', 'success'),
            gridCell('S', 'info'),
            gridCell('S', 'success')
          ]),
          gridRow('Change request', [
            gridCell('Change', 'success'),
            gridCell('Change', 'success'),
            gridCell('Keep', 'info'),
            gridCell('Change', 'success')
          ]),
          gridRow('Set limit', ['4 sets', '4 sets', '4 sets', '4 sets']),
          gridRow('Sets available', ['2', '2', gridCell('0', 'error'), '2']),
          gridRow('Sets used', ['2', '2', '3', '2']),
          gridRow('Pressure', [formatPressure(1.89), formatPressure(1.90), formatPressure(1.92), formatPressure(1.91)]),
          gridRow('Temperature', [formatTemperature(83), formatTemperature(84), formatTemperature(79), formatTemperature(80)]),
          gridRow('Wear', ['92/91/90%', '93/92/91%', '96/95/94%', '97/96/95%']),
          gridRow('Distance', [formatDistance(18400), formatDistance(18400), formatDistance(18400), formatDistance(18400)])
        ], overlayState, session);
        return withChrome(metricsModel('pit-service', 'Pit Service', 'service active', pitSections.flatMap((section) => section.rows), 'source: player/team pit service telemetry', [
          {
            title: 'Tire Analysis',
            headers: ['Info', 'FL', 'FR', 'RL', 'RR'],
            rows: tireRows
          }
        ].filter((section) => section.rows.length > 0), pitSections, [
          { key: 'timeRemaining', value: '00:03:58' }
        ]));
      }
    case 'gap-to-leader':
      if (fixture === 'gap-no-cars') {
        return withChrome({
          overlayId,
          title: 'Gap To Leader',
          status: 'hidden | race gap',
          source: '',
          bodyKind: 'graph',
          columns: [],
          rows: [],
          metrics: [],
          points: [],
          graph: reviewEmptyGapGraph(),
          headerItems: [],
          shouldRender: false
        });
      }
      if (fixture === 'gap-long-tail-real-data') {
        return withChrome(reviewGapLongTailRealDataModel());
      }
      if (fixture === 'gap-pit-window-real-data') {
        return withChrome(reviewGapPitWindowRealDataModel());
      }
      if (fixture === 'gap-threat-capture-shaped') {
        return withChrome(reviewGapThreatCaptureShapedModel());
      }
      if (fixture === 'gap-endurance-domain-capture-shaped') {
        return withChrome(reviewGapEnduranceDomainCaptureShapedModel());
      }
      if (session !== 'race') {
        return withChrome({
          overlayId,
          title: 'Gap To Leader',
          status: 'hidden | race only',
          source: '',
          bodyKind: 'graph',
          columns: [],
          rows: [],
          metrics: [],
          points: [],
          headerItems: [],
          shouldRender: false
        });
      }

      if (!reviewGapHasRenderableContent(overlayState, session)) {
        return withChrome({
          overlayId,
          title: 'Gap To Leader',
          status: 'hidden | race gap',
          source: '',
          bodyKind: 'graph',
          columns: [],
          rows: [],
          metrics: [],
          points: [],
          headerItems: [],
          shouldRender: false
        });
      }

      return withChrome({
        overlayId,
        title: 'Gap To Leader',
        status: 'live | race gap',
        source: `source: live gap telemetry | cars ${reviewGapCarCount(overlayState)}`,
        bodyKind: 'graph',
        columns: [],
        rows: [],
        metrics: [],
        points: reviewGapPoints(overlayState),
        graph: reviewGapGraph(overlayState, session),
        headerItems: [
          { key: 'timeRemaining', value: '06:37:08' }
        ],
        shouldRender: true
      });
    default:
      return tableModel(overlayId, browserOverlayPage(overlayId).title, `live | ${previewLabel}`, []);
  }
}

function reviewGapCarCount(overlayState) {
  if (Number(overlayState?.carsAhead ?? 5) <= 0 && Number(overlayState?.carsBehind ?? 5) <= 0) {
    return '0/0';
  }

  return '3/3';
}

function reviewGapPoints(overlayState) {
  if (!reviewGapGraphEnabled(overlayState, 'race')) {
    return [];
  }

  return [240.8, 240.1, 239.5, 238.8, 238.2, 237.6, 237.1, 236.5];
}

function reviewGapHasRenderableContent(overlayState, session) {
  return reviewGapWindowEnabled(overlayState)
    && (reviewGapGraphEnabled(overlayState, session) || reviewGapTrendEnabled(overlayState, session));
}

function reviewGapWindowEnabled(overlayState) {
  return Number(overlayState?.carsAhead ?? 5) > 0 || Number(overlayState?.carsBehind ?? 5) > 0;
}

function reviewGapGraphEnabled(overlayState, session) {
  return reviewGapWindowEnabled(overlayState)
    && contentLabelsEnabled(overlayState, ['gap.graph.enabled', 'Graph'], true, session);
}

function reviewGapTrendEnabled(overlayState, session) {
  return reviewGapTrendRows()
    .some((row) => contentLabelsEnabled(overlayState, [row.key, row.label], true, session));
}

function reviewGapTrendRows() {
  return [
    { label: 'Last', key: 'gap.trend.last.enabled' },
    { label: '5L', key: 'gap.trend.5l.enabled' },
    { label: '10L', key: 'gap.trend.10l.enabled' },
    { label: 'Pit', key: 'gap.trend.pit.enabled' },
    { label: 'PLap', key: 'gap.trend.pit-lap.enabled' },
    { label: 'Stint', key: 'gap.trend.stint.enabled' },
    { label: 'Tire', key: 'gap.trend.tire.enabled' },
    { label: 'Status', key: 'gap.trend.status.enabled' }
  ];
}

function reviewEmptyGapGraph() {
  return {
    series: [],
    weather: [],
    leaderChanges: [],
    driverChanges: [],
    pitWindows: [],
    startSeconds: 62571.436719,
    endSeconds: 63360.136719,
    maxGapSeconds: 500,
    lapReferenceSeconds: 525.8,
    selectedSeriesCount: 0,
    trendMetrics: [],
    activeThreat: null,
    threatCarIdx: null,
    metricDeadbandSeconds: 0.25,
    comparisonLabel: '--',
    showGraph: false,
    showTrendMetrics: false,
    scale: {
      maxGapSeconds: 500,
      isFocusRelative: false,
      aheadSeconds: 0,
      behindSeconds: 0,
      referencePoints: [],
      latestReferenceGapSeconds: 0
    }
  };
}

function reviewGapGraph(overlayState = {}, session = 'race') {
  const startSeconds = 62571.436719;
  const endSeconds = startSeconds + 420;
  const timestampStart = Date.parse('2026-05-17T12:00:00.000Z');
  const trend = [
    { offset: 0, leader: 0, ahead: 234.0, focus: 240.8, threat: 251.0 },
    { offset: 60, leader: 0.4, ahead: 234.2, focus: 240.1, threat: 249.4 },
    { offset: 120, leader: 0.1, ahead: 234.4, focus: 239.5, threat: 247.8 },
    { offset: 180, leader: 0.6, ahead: 234.6, focus: 238.8, threat: 246.2 },
    { offset: 240, leader: 0.3, ahead: 234.8, focus: 238.2, threat: 244.6 },
    { offset: 300, leader: 0.2, ahead: 235.0, focus: 237.6, threat: 243.2 },
    { offset: 360, leader: 0.5, ahead: 235.2, focus: 237.1, threat: 241.8 },
    { offset: 420, leader: 0.0, ahead: 235.4, focus: 236.5, threat: 240.4 }
  ];
  const point = (sample, carIdx, gapSeconds, isReference, isClassLeader, classPosition, index) => ({
    timestampUtc: new Date(timestampStart + sample.offset * 1000).toISOString(),
    axisSeconds: startSeconds + sample.offset,
    gapSeconds,
    carIdx,
    isReference,
    isClassLeader,
    classPosition,
    completedLap: 120 + index,
    startsSegment: index === 0
  });
  const referencePoints = trend.map((sample, index) => point(sample, 42, sample.focus, true, false, 24, index));
  const threat = { carIdx: 43, label: 'P25', gainSeconds: 5.3 };
  const focusPit = { seconds: 82, lap: 12, isActive: true };
  const comparisonPit = { seconds: 88, lap: 12, isActive: false };
  const threatPit = { seconds: 91, lap: 13, isActive: false };
  const focusTire = { label: 'Dry', shortLabel: 'D', isWet: false };
  const comparisonTire = { label: 'Dry', shortLabel: 'D', isWet: false };
  const threatTire = { label: 'Wet', shortLabel: 'W', isWet: true };
  const showGraph = reviewGapGraphEnabled(overlayState, session);
  const trendMetrics = [
    { label: 'Last', focusGapChangeSeconds: null, chaser: null, state: 'last', stateLabel: null, primaryText: '0.0', comparisonText: '+0.4', threatText: '-0.7' },
    { label: '5L', focusGapChangeSeconds: -1.8, chaser: threat, state: 'ready', stateLabel: null, completedReferenceLaps: 10 },
    { label: '10L', focusGapChangeSeconds: -3.4, chaser: threat, state: 'ready', stateLabel: null, completedReferenceLaps: 10 },
    { label: 'Pit', focusGapChangeSeconds: null, chaser: null, state: 'pit', stateLabel: null, primaryPit: focusPit, comparisonPit, threatPit },
    { label: 'PLap', focusGapChangeSeconds: null, chaser: null, state: 'pitLap', stateLabel: null, primaryPit: focusPit, comparisonPit, threatPit },
    { label: 'Stint', focusGapChangeSeconds: null, chaser: null, state: 'stint', stateLabel: null, comparisonText: '18L', threatText: '17L' },
    { label: 'Tire', focusGapChangeSeconds: null, chaser: null, state: 'tire', stateLabel: null, primaryTire: focusTire, comparisonTire, threatTire },
    { label: 'Status', focusGapChangeSeconds: null, chaser: null, state: 'status', stateLabel: null, comparisonText: 'Track', threatText: 'Track' }
  ].filter((metric) => contentLabelsEnabled(
    overlayState,
    [reviewGapTrendRows().find((row) => row.label === metric.label)?.key, metric.label].filter(Boolean),
    true,
    session));
  const activeThreat = trendMetrics.find((metric) => metric.label === '5L') || trendMetrics.find((metric) => metric.chaser) || null;
  const threatCarIdx = activeThreat?.chaser?.carIdx ?? null;
  const series = [
    reviewGapSeries(8, false, true, 1, trend.map((sample, index) => point(sample, 8, sample.leader, false, true, 1, index)), 0, threatCarIdx),
    reviewGapSeries(41, false, false, 23, trend.map((sample, index) => point(sample, 41, sample.ahead, false, false, 23, index)), 1, threatCarIdx),
    reviewGapSeries(42, true, false, 24, referencePoints, 2, threatCarIdx),
    reviewGapSeries(43, false, false, 25, trend.map((sample, index) => point(sample, 43, sample.threat, false, false, 25, index)), 3, threatCarIdx)
  ];
  return {
    series: showGraph ? series : [],
    weather: [],
    leaderChanges: [],
    driverChanges: [],
    pitWindows: [],
    startSeconds,
    endSeconds,
    maxGapSeconds: 250,
    lapReferenceSeconds: 525.8,
    selectedSeriesCount: showGraph ? series.length : 0,
    trendMetrics,
    activeThreat,
    threatCarIdx,
    metricDeadbandSeconds: 0.25,
    comparisonLabel: 'P23',
    showGraph,
    showTrendMetrics: trendMetrics.length > 0,
    scale: {
      maxGapSeconds: 20,
      isFocusRelative: true,
      aheadSeconds: 8,
      behindSeconds: 8,
      referencePoints,
      latestReferenceGapSeconds: 236.5
    }
  };
}

function reviewGapLongTailRealDataModel() {
  const snapshot = gapLongTailRealDataSnapshot;
  const startSeconds = snapshot.provenance.sampleSessionTimeSeconds - 420;
  const focusGaps = [
    1.78,
    1.66,
    1.58,
    1.51,
    1.46,
    1.42,
    1.39,
    snapshot.rawEvidence.focusCar.gapToClassLeaderSeconds
  ];
  const leaderGaps = focusGaps.map(() => 0);
  const aheadGaps = [0.96, 0.91, 0.86, 0.79, 0.72, 0.64, 0.55, 0.43];
  const point = (index, gapSeconds) => ({
    axisSeconds: startSeconds + index * 60,
    gapSeconds,
    startsSegment: index === 0
  });
  const threatCarIdx = null;
  const series = [
    reviewGapSeries(
      snapshot.rawEvidence.classLeader.carIdx,
      false,
      true,
      snapshot.rawEvidence.classLeader.classPosition,
      leaderGaps.map((value, index) => point(index, value)),
      0,
      threatCarIdx),
    reviewGapSeries(
      16,
      false,
      false,
      4,
      aheadGaps.map((value, index) => point(index, value)),
      1,
      threatCarIdx),
    reviewGapSeries(
      snapshot.rawEvidence.focusCar.carIdx,
      true,
      false,
      snapshot.rawEvidence.focusCar.classPosition,
      focusGaps.map((value, index) => point(index, value)),
      2,
      threatCarIdx)
  ];

  return {
    overlayId: 'gap-to-leader',
    title: 'Gap To Leader',
    status: 'live | Dallara long-tail policy',
    source: 'source: compact Dallara race capture',
    bodyKind: 'graph',
    columns: [],
    rows: [],
    metrics: [],
    points: [],
    graph: {
      series,
      weather: [],
      leaderChanges: [],
      driverChanges: [],
      pitWindows: [],
      startSeconds,
      endSeconds: startSeconds + 420,
      maxGapSeconds: snapshot.expected.graph.axisBehindSecondsMaximum,
      lapReferenceSeconds: 525.8,
      selectedSeriesCount: series.length,
      trendMetrics: reviewGapLongTailTrendMetrics(),
      activeThreat: null,
      threatCarIdx,
      metricDeadbandSeconds: 0.25,
      comparisonLabel: snapshot.expected.graph.comparisonLabel,
      showGraph: true,
      showTrendMetrics: true,
      scale: {
        maxGapSeconds: snapshot.expected.graph.axisBehindSecondsMaximum,
        isFocusRelative: true,
        aheadSeconds: 2,
        behindSeconds: snapshot.expected.graph.axisBehindSecondsMaximum,
        referencePoints: focusGaps.map((value, index) => point(index, value)),
        latestReferenceGapSeconds: focusGaps[focusGaps.length - 1]
      }
    },
    headerItems: [
      { key: 'timeRemaining', value: '00:38:44' }
    ],
    shouldRender: true
  };
}

function reviewGapLongTailTrendMetrics() {
  const focusPit = { seconds: 78, lap: 3, isActive: false };
  const comparisonPit = { seconds: 82, lap: 3, isActive: false };
  const dryTire = { label: 'Dry', shortLabel: 'D', isWet: false };
  return [
    { label: 'Last', focusGapChangeSeconds: null, chaser: null, state: 'last', stateLabel: null, primaryText: '0.0', comparisonText: '-0.2', threatText: '--' },
    { label: '5L', focusGapChangeSeconds: -0.4, chaser: null, state: 'ready', stateLabel: null, completedReferenceLaps: 5, comparisonText: '-0.5', threatText: '--' },
    { label: '10L', focusGapChangeSeconds: -0.7, chaser: null, state: 'ready', stateLabel: null, completedReferenceLaps: 10, comparisonText: '-0.9', threatText: '--' },
    { label: 'Pit', focusGapChangeSeconds: null, chaser: null, state: 'pit', stateLabel: null, primaryPit: focusPit, comparisonPit, threatPit: null },
    { label: 'PLap', focusGapChangeSeconds: null, chaser: null, state: 'pitLap', stateLabel: null, primaryPit: focusPit, comparisonPit, threatPit: null },
    { label: 'Stint', focusGapChangeSeconds: null, chaser: null, state: 'stint', stateLabel: null, comparisonText: '3L', threatText: '--' },
    { label: 'Tire', focusGapChangeSeconds: null, chaser: null, state: 'tire', stateLabel: null, primaryTire: dryTire, comparisonTire: dryTire, threatTire: null },
    { label: 'Status', focusGapChangeSeconds: null, chaser: null, state: 'status', stateLabel: null, comparisonText: 'Track', threatText: '--' }
  ];
}

function reviewGapPitWindowRealDataModel() {
  const snapshot = gapPitWindowRealDataSnapshot;
  const rawFrames = snapshot.rawEvidence.orderedFrames || [];
  const pitWindow = snapshot.rawEvidence.pitWindow || {};
  const firstFrame = rawFrames[0] || { sessionTimeSeconds: 0, focusGapToClassLeaderSeconds: 0 };
  const lastFrame = rawFrames[rawFrames.length - 1] || firstFrame;
  const orderedFrames = [
    ...rawFrames,
    {
      ...lastFrame,
      role: 'post-exit-render-stable',
      sessionTimeSeconds: Number(lastFrame.sessionTimeSeconds || 0) + 6,
      focusGapToClassLeaderSeconds: Number(lastFrame.focusGapToClassLeaderSeconds || 0) + 0.1
    }
  ];
  const point = (frame, gapSeconds) => ({
    axisSeconds: Number(frame.sessionTimeSeconds),
    gapSeconds,
    startsSegment: frame === orderedFrames[0]
  });
  const focusPoints = orderedFrames.map((frame) =>
    point(frame, Math.max(0, Number(frame.focusGapToClassLeaderSeconds || 0))));
  const leaderPoints = orderedFrames.map((frame) => point(frame, 0));
  const aheadPoints = orderedFrames.map((frame) =>
    point(frame, Math.max(0.3, Number(frame.focusGapToClassLeaderSeconds || 0) - 1.1)));
  const threatCarIdx = null;
  const series = [
    reviewGapSeries(13, false, true, 1, leaderPoints, 0, threatCarIdx),
    reviewGapSeries(16, false, false, 4, aheadPoints, 1, threatCarIdx),
    reviewGapSeries(snapshot.rawEvidence.focusCar.carIdx, true, false, snapshot.rawEvidence.focusCar.classPosition, focusPoints, 2, threatCarIdx)
  ];
  const maxFocusGap = Math.max(...focusPoints.map((item) => item.gapSeconds).filter(Number.isFinite), 1);
  const axisPaddingSeconds = 4;
  const pitWindows = Number.isFinite(Number(pitWindow.entrySessionTimeSeconds))
    ? [{
        entryAxisSeconds: Number(pitWindow.entrySessionTimeSeconds),
        exitAxisSeconds: Number.isFinite(Number(pitWindow.exitSessionTimeSeconds)) ? Number(pitWindow.exitSessionTimeSeconds) : null,
        carIdx: Number(snapshot.rawEvidence.focusCar.carIdx),
        classPosition: Number(snapshot.rawEvidence.focusCar.classPosition),
        isReference: true,
        isActive: false,
        durationSeconds: Number.isFinite(Number(pitWindow.durationSeconds)) ? Number(pitWindow.durationSeconds) : null,
        lap: Number.isFinite(Number(pitWindow.displayLap)) ? Number(pitWindow.displayLap) : null
      }]
    : [];
  return {
    overlayId: 'gap-to-leader',
    title: 'Gap To Leader',
    status: 'live | Dallara pit-window policy',
    source: 'source: compact Dallara pit-window capture',
    bodyKind: 'graph',
    columns: [],
    rows: [],
    metrics: [],
    points: [],
    graph: {
      series,
      weather: [],
      leaderChanges: [],
      driverChanges: [],
      pitWindows,
      startSeconds: Number(firstFrame.sessionTimeSeconds || 0) - axisPaddingSeconds,
      endSeconds: Number(orderedFrames[orderedFrames.length - 1]?.sessionTimeSeconds || firstFrame.sessionTimeSeconds || 0) + axisPaddingSeconds,
      maxGapSeconds: Math.ceil(maxFocusGap + 2),
      lapReferenceSeconds: 525.8,
      selectedSeriesCount: series.length,
      trendMetrics: reviewGapPitWindowTrendMetrics(snapshot),
      activeThreat: null,
      threatCarIdx,
      metricDeadbandSeconds: 0.25,
      comparisonLabel: 'P4',
      showGraph: true,
      showTrendMetrics: true,
      scale: {
        maxGapSeconds: Math.ceil(maxFocusGap + 2),
        isFocusRelative: true,
        aheadSeconds: Math.ceil(maxFocusGap + 1),
        behindSeconds: 3,
        referencePoints: focusPoints,
        latestReferenceGapSeconds: focusPoints[focusPoints.length - 1]?.gapSeconds ?? null
      }
    },
    headerItems: [
      { key: 'timeRemaining', value: '00:37:19' }
    ],
    shouldRender: true
  };
}

function reviewGapPitWindowTrendMetrics(snapshot) {
  const expected = snapshot.expected?.pitMetrics || {};
  const seconds = Number(expected.lastDurationSeconds);
  const lap = Number(expected.lastPitLap);
  const primaryPit = {
    seconds: Number.isFinite(seconds) ? seconds : 42.6,
    lap: Number.isFinite(lap) ? lap : 24,
    isActive: false
  };
  const comparisonPit = {
    seconds: 39.8,
    lap: Math.max(1, (primaryPit.lap || 1) - 1),
    isActive: false
  };
  const threatPit = {
    seconds: 41.2,
    lap: primaryPit.lap,
    isActive: false
  };
  const dryTire = { label: 'Dry', shortLabel: 'D', isWet: false };
  return [
    { label: 'Last', focusGapChangeSeconds: null, chaser: null, state: 'last', stateLabel: null, primaryText: '0.0', comparisonText: '-0.2', threatText: '--' },
    { label: '5L', focusGapChangeSeconds: -0.3, chaser: null, state: 'ready', stateLabel: null, completedReferenceLaps: 5, comparisonText: '-0.3', threatText: '--' },
    { label: '10L', focusGapChangeSeconds: -0.6, chaser: null, state: 'ready', stateLabel: null, completedReferenceLaps: 10, comparisonText: '-0.7', threatText: '--' },
    { label: 'Pit', focusGapChangeSeconds: null, chaser: null, state: 'pit', stateLabel: null, primaryPit, comparisonPit, threatPit: null },
    { label: 'PLap', focusGapChangeSeconds: null, chaser: null, state: 'pitLap', stateLabel: null, primaryPit, comparisonPit, threatPit: null },
    { label: 'Stint', focusGapChangeSeconds: null, chaser: null, state: 'stint', stateLabel: null, comparisonText: '8L', threatText: '--' },
    { label: 'Tire', focusGapChangeSeconds: null, chaser: null, state: 'tire', stateLabel: null, primaryTire: dryTire, comparisonTire: dryTire, threatTire: null },
    { label: 'Status', focusGapChangeSeconds: null, chaser: null, state: 'status', stateLabel: null, comparisonText: 'Track', threatText: '--' }
  ];
}

function reviewGapThreatCaptureShapedModel() {
  const snapshot = gapThreatCaptureShapedSnapshot;
  const expected = snapshot.expected?.graph || {};
  const trend = snapshot.rawEvidence?.orderedTrend || [];
  const startSeconds = Number(snapshot.provenance?.sampleWindowSessionTimeSeconds?.[0] || 0);
  const threatCarIdx = Number(expected.activeThreat?.carIdx);
  const activeThreat = {
    label: '5L',
    focusGapChangeSeconds: -0.6,
    chaser: {
      carIdx: threatCarIdx,
      label: expected.activeThreat?.label || 'P6',
      gainSeconds: Number(expected.activeThreat?.gainSeconds || 0)
    },
    state: 'ready',
    stateLabel: null,
    completedReferenceLaps: 10
  };
  const point = (sample, gapSeconds, index) => ({
    axisSeconds: startSeconds + Number(sample.offsetSeconds || 0),
    gapSeconds,
    completedLap: 20 + index,
    startsSegment: index === 0
  });
  const series = [
    reviewGapSeries(
      snapshot.rawEvidence.classLeader.carIdx,
      false,
      true,
      snapshot.rawEvidence.classLeader.classPosition,
      trend.map((sample, index) => point(sample, Number(sample.leaderGapSeconds || 0), index)),
      0,
      threatCarIdx),
    reviewGapSeries(
      snapshot.rawEvidence.comparisonAhead.carIdx,
      false,
      false,
      snapshot.rawEvidence.comparisonAhead.classPosition,
      trend.map((sample, index) => point(sample, Number(sample.aheadGapSeconds || 0), index)),
      1,
      threatCarIdx),
    reviewGapSeries(
      snapshot.rawEvidence.focusCar.carIdx,
      true,
      false,
      snapshot.rawEvidence.focusCar.classPosition,
      trend.map((sample, index) => point(sample, Number(sample.focusGapSeconds || 0), index)),
      2,
      threatCarIdx),
    reviewGapSeries(
      threatCarIdx,
      false,
      false,
      expected.activeThreat?.classPosition || 6,
      trend.map((sample, index) => point(sample, Number(sample.threatGapSeconds || 0), index)),
      3,
      threatCarIdx)
  ];
  const focusPoints = series.find((item) => item.isReference)?.points || [];
  return {
    overlayId: 'gap-to-leader',
    title: 'Gap To Leader',
    status: 'live | capture-shaped threat',
    source: 'source: capture-shaped Dallara threat fixture',
    bodyKind: 'graph',
    columns: [],
    rows: [],
    metrics: [],
    points: [],
    graph: {
      series,
      weather: [],
      leaderChanges: [],
      driverChanges: [],
      pitWindows: [],
      startSeconds,
      endSeconds: startSeconds + Number(trend[trend.length - 1]?.offsetSeconds || 600),
      maxGapSeconds: 16,
      lapReferenceSeconds: 525.8,
      selectedSeriesCount: series.length,
      trendMetrics: reviewGapCaptureShapedTrendMetrics(activeThreat),
      activeThreat,
      threatCarIdx,
      metricDeadbandSeconds: 0.25,
      comparisonLabel: expected.comparisonLabel || 'P4',
      showGraph: true,
      showTrendMetrics: true,
      scale: {
        maxGapSeconds: 16,
        isFocusRelative: false,
        aheadSeconds: 0,
        behindSeconds: 0,
        referencePoints: focusPoints,
        latestReferenceGapSeconds: focusPoints[focusPoints.length - 1]?.gapSeconds ?? null
      }
    },
    headerItems: [
      { key: 'timeRemaining', value: '00:18:20' }
    ],
    shouldRender: true
  };
}

function reviewGapEnduranceDomainCaptureShapedModel() {
  const snapshot = gapEnduranceDomainCaptureShapedSnapshot;
  const expected = snapshot.expected?.graph || {};
  const domain = snapshot.rawEvidence?.domain || { startSeconds: 0, endSeconds: 14400 };
  const startSeconds = Number(domain.startSeconds || 0);
  const endSeconds = Number(domain.endSeconds || 14400);
  const offsets = [0, 1800, 3600, 5400, 7200, 9000, 10800, 12600, 14400];
  const point = (offsetSeconds, gapSeconds, index) => ({
    axisSeconds: startSeconds + offsetSeconds,
    gapSeconds,
    completedLap: 2 + index * 4,
    startsSegment: index === 0
  });
  const line = (values) => offsets.map((offset, index) => point(offset, values[index], index));
  const series = [
    reviewGapSeries(14, false, true, 1, line([0, 0.4, 0.2, 0, 0.6, 0.1, 0.5, 0.3, 0]), 0, null),
    reviewGapSeries(43, false, false, 8, line([31.1, 32.0, 30.6, 29.8, 31.3, 32.4, 30.9, 31.8, 30.7]), 1, null),
    reviewGapSeries(23, false, false, 9, line([33.2, 34.0, 33.1, 32.4, 34.5, 35.0, 33.7, 34.1, 33.6]), 2, null),
    reviewGapSeries(15, true, false, 10, line([36.4, 37.1, 36.2, 35.8, 37.0, 38.2, 36.6, 37.5, 36.8]), 3, null),
    reviewGapSeries(2, false, false, 11, line([37.4, 38.0, 37.2, 36.5, 38.2, 39.1, 37.6, 38.3, 37.8]), 4, null),
    reviewGapSeries(9, false, false, 12, line([37.8, 38.6, 38.1, 37.2, 39.0, 40.0, 38.4, 39.1, 38.6]), 5, null),
    reviewGapSeries(28, false, false, 13, line([42.7, 43.6, 44.8, 43.1, 45.2, 46.0, 44.3, 45.4, 44.1]), 6, null)
  ];
  const weather = (snapshot.rawEvidence?.weatherPeriods || []).map((period) => ({
    axisSeconds: Number(period.axisSeconds || 0),
    condition: period.condition
  }));
  const leaderChanges = (snapshot.rawEvidence?.graphMarkers || [])
    .filter((marker) => marker.kind === 'leader-change')
    .map((marker) => ({
      timestampUtc: new Date(Date.UTC(2026, 4, 17, 12, 0, 0) + Number(marker.axisSeconds || 0) * 1000).toISOString(),
      axisSeconds: Number(marker.axisSeconds || 0),
      previousLeaderCarIdx: 14,
      newLeaderCarIdx: marker.axisSeconds > 8000 ? 34 : 32
    }));
  const driverChanges = (snapshot.rawEvidence?.graphMarkers || [])
    .filter((marker) => marker.kind === 'driver-change')
    .map((marker) => ({
      timestampUtc: new Date(Date.UTC(2026, 4, 17, 12, 0, 0) + Number(marker.axisSeconds || 0) * 1000).toISOString(),
      axisSeconds: Number(marker.axisSeconds || 0),
      carIdx: Number(marker.carIdx || 15),
      gapSeconds: 36.9,
      isReference: true,
      label: marker.label || 'DR'
    }));
  const pitWindows = (snapshot.rawEvidence?.pitWindows || []).map((window) => ({
    entryAxisSeconds: Number(window.entryAxisSeconds || window.entrySessionTimeSeconds || 0),
    exitAxisSeconds: Number.isFinite(Number(window.exitAxisSeconds ?? window.exitSessionTimeSeconds))
      ? Number(window.exitAxisSeconds ?? window.exitSessionTimeSeconds)
      : null,
    carIdx: Number(window.carIdx || snapshot.rawEvidence?.focusCar?.carIdx || 15),
    classPosition: Number(window.classPosition || snapshot.rawEvidence?.focusCar?.classPosition || 10),
    isReference: window.isReference !== false,
    isActive: window.isActive === true,
    durationSeconds: Number.isFinite(Number(window.durationSeconds)) ? Number(window.durationSeconds) : null,
    lap: Number.isFinite(Number(window.lap)) ? Number(window.lap) : null
  }));
  const focusPoints = series.find((item) => item.isReference)?.points || [];
  return {
    overlayId: 'gap-to-leader',
    title: 'Gap To Leader',
    status: 'live | capture-shaped 4h graph',
    source: 'source: capture-shaped endurance gap fixture',
    bodyKind: 'graph',
    columns: [],
    rows: [],
    metrics: [],
    points: [],
    graph: {
      series,
      weather,
      leaderChanges,
      driverChanges,
      pitWindows,
      startSeconds,
      endSeconds,
      maxGapSeconds: 48,
      lapReferenceSeconds: 525.8,
      selectedSeriesCount: series.length,
      trendMetrics: reviewGapCaptureShapedTrendMetrics(null),
      activeThreat: null,
      threatCarIdx: null,
      metricDeadbandSeconds: 0.25,
      comparisonLabel: expected.comparisonLabel || 'P9',
      showGraph: true,
      showTrendMetrics: true,
      scale: {
        maxGapSeconds: 48,
        isFocusRelative: false,
        aheadSeconds: 0,
        behindSeconds: 0,
        referencePoints: focusPoints,
        latestReferenceGapSeconds: focusPoints[focusPoints.length - 1]?.gapSeconds ?? null
      }
    },
    headerItems: [
      { key: 'timeRemaining', value: '03:12:48' }
    ],
    shouldRender: true
  };
}

function reviewGapCaptureShapedTrendMetrics(activeThreat) {
  const dryTire = { label: 'Dry', shortLabel: 'D', isWet: false };
  return [
    { label: 'Last', focusGapChangeSeconds: null, chaser: null, state: 'last', stateLabel: null, primaryText: '0.0', comparisonText: '-0.3', threatText: activeThreat ? '-0.6' : '--' },
    activeThreat || { label: '5L', focusGapChangeSeconds: -0.4, chaser: null, state: 'ready', stateLabel: null, completedReferenceLaps: 8, comparisonText: '-0.5', threatText: '--' },
    { label: '10L', focusGapChangeSeconds: -0.7, chaser: activeThreat?.chaser || null, state: 'ready', stateLabel: null, completedReferenceLaps: 12, comparisonText: '-0.9', threatText: activeThreat ? '-3.5' : '--' },
    { label: 'Pit', focusGapChangeSeconds: null, chaser: null, state: 'pit', stateLabel: null, primaryPit: { seconds: 42.6, lap: 24, isActive: false }, comparisonPit: { seconds: 39.8, lap: 23, isActive: false }, threatPit: activeThreat ? { seconds: 41.2, lap: 24, isActive: false } : null },
    { label: 'PLap', focusGapChangeSeconds: null, chaser: null, state: 'pitLap', stateLabel: null, primaryPit: { seconds: 42.6, lap: 24, isActive: false }, comparisonPit: { seconds: 39.8, lap: 23, isActive: false }, threatPit: activeThreat ? { seconds: 41.2, lap: 24, isActive: false } : null },
    { label: 'Stint', focusGapChangeSeconds: null, chaser: null, state: 'stint', stateLabel: null, comparisonText: '18L', threatText: activeThreat ? '16L' : '--' },
    { label: 'Tire', focusGapChangeSeconds: null, chaser: null, state: 'tire', stateLabel: null, primaryTire: dryTire, comparisonTire: dryTire, threatTire: activeThreat ? dryTire : null },
    { label: 'Status', focusGapChangeSeconds: null, chaser: null, state: 'status', stateLabel: null, comparisonText: 'Track', threatText: activeThreat ? 'Track' : '--' }
  ];
}

function reviewGapSeries(carIdx, isReference, isClassLeader, classPosition, points, index, threatCarIdx) {
  const baseColor = reviewGapSeriesColor({ carIdx, isReference, isClassLeader }, index, threatCarIdx);
  const alpha = isReference || isClassLeader || carIdx === threatCarIdx ? 1 : 0.48;
  return {
    carIdx,
    isReference,
    isClassLeader,
    classPosition,
    alpha: 1,
    isStickyExit: false,
    isStale: false,
    baseColor,
    renderedColor: reviewGapColorWithAlpha(baseColor, alpha),
    points
  };
}

function reviewGapSeriesColor(series, index, threatCarIdx) {
  if (series.carIdx === threatCarIdx) return '#ff6274';
  if (series.isReference) return '#00e8ff';
  if (series.isClassLeader) return '#fff7ff';
  return ['#ffd15b', '#62ff9f', '#ff2aa7'][index % 3];
}

function reviewGapColorWithAlpha(hex, alpha) {
  const normalized = String(hex || '').replace('#', '');
  if (!/^[0-9a-f]{6}$/i.test(normalized)) return hex;
  const red = Number.parseInt(normalized.slice(0, 2), 16);
  const green = Number.parseInt(normalized.slice(2, 4), 16);
  const blue = Number.parseInt(normalized.slice(4, 6), 16);
  const clampedAlpha = Math.max(0, Math.min(1, Number(alpha)));
  return `rgba(${red}, ${green}, ${blue}, ${Number.isFinite(clampedAlpha) ? clampedAlpha.toFixed(3).replace(/0+$/, '').replace(/\.$/, '') : '1'})`;
}

function applyReviewChrome(model, overlayId, previewMode, forceChromeOff = false) {
  if (!supportsSharedChrome(overlayId)) {
    return model;
  }

  if (model?.shouldRender === false) {
    return {
      ...model,
      headerItems: []
    };
  }

  const overlayState = reviewAppState.overlays[overlayId] || {};
  const session = sessionKeyFromPreview(previewMode);
  const showTime = !forceChromeOff && chromeEnabled(overlayState, 'header', 'Time remaining', session, true);
  const fallbackTone = headerToneForItem('timeRemaining');
  return {
    ...model,
    headerItems: (model.headerItems || []).filter((item) => {
      const key = String(item?.key || '').toLowerCase();
      if (key === 'timeremaining') return showTime;
      if (key === 'status') return false;
      return true;
    }).map((item) => ({
      ...item,
      tone: normalizeHeaderTone(item?.tone || fallbackTone)
    }))
  };
}

function headerToneForModel(overlayId, model) {
  return headerToneForItem('timeRemaining');
}

function headerToneForItem(key) {
  return String(key || '').toLowerCase() === 'timeremaining'
    ? 'normal'
    : 'normal';
}

function normalizeHeaderTone(tone) {
  const token = String(tone || '').trim().toLowerCase();
  if (token === 'modeled') return 'info';
  return ['normal', 'waiting', 'info', 'success', 'warning', 'error'].includes(token)
    ? token
    : 'normal';
}

function supportsSharedChrome(overlayId) {
  return new Set(['standings', 'relative', 'fuel-calculator', 'gap-to-leader', 'session-weather', 'pit-service']).has(overlayId);
}

function sessionKeyFromPreview(previewMode) {
  return normalizePreviewMode(previewMode) === 'off' ? 'practice' : normalizePreviewMode(previewMode);
}

function sessionDisplayName(session) {
  return session === 'qualifying'
    ? 'Qualifying'
    : session === 'race'
      ? 'Race'
      : session === 'test'
        ? 'Test'
        : 'Practice';
}

function reviewSessionWeatherClock(session) {
  if (session === 'race') {
    return { elapsed: '17:22:51', left: '6:37:09', total: '24:00:00' };
  }

  if (session === 'qualifying') {
    return { elapsed: '5:05', left: '14:55', total: '20:00' };
  }

  return { elapsed: '7:40', left: '12:20', total: '20:00' };
}

function reviewSessionWeatherLaps(session) {
  if (session === 'race') {
    return { remaining: '49.6 est', total: '170 est' };
  }

  if (session === 'qualifying') {
    return { remaining: '3.3 est', total: '10 est' };
  }

  return { remaining: '3.6 est', total: '10 est' };
}

function chromeEnabled(overlayState, area, label, session, defaultValue) {
  return overlayState?.chrome?.[area]?.[label]?.[session] ?? defaultValue;
}

function reviewAssetBackedDisplayModel(overlayId, previewMode = 'off', searchParams = new URLSearchParams()) {
  const page = browserOverlayPage(overlayId);
  return browserOverlayApiResponse(overlayId, page.modelRoute, {
    live: reviewLiveSnapshot(previewMode, searchParams),
    settings: reviewSettings(overlayId, previewMode, searchParams)
  }).model;
}

function relativeDisplayModel(previewLabel = 'review fixture', session = 'practice') {
  const showLapRelationship = session === 'race' || session === 'practice';
  const timingLabel = session === 'race' || session === 'practice' ? 'Delta' : 'Est';
  return {
    overlayId: 'relative',
    title: 'Relative',
    status: `5 - 2/4 cars | ${previewLabel}`,
    source: 'source: review fixture',
    bodyKind: 'table',
    columns: [
      { id: 'relative.position', label: 'Pos', dataKey: 'relative-position', width: 48, alignment: 'right' },
      { id: 'relative.driver', label: 'Driver', dataKey: 'driver', width: 240, alignment: 'left' },
      { id: 'relative.gap', label: timingLabel, dataKey: 'gap', width: 70, alignment: 'right' },
      { id: 'relative.pit', label: 'Pit', dataKey: 'pit', width: 48, alignment: 'right' }
    ],
    rows: [
      relativeRow(['3', '#34 Near Ahead', '-2.350', ''], { carClassColorHex: '#33CEFF', relativeLapDelta: showLapRelationship ? 1 : null }),
      relativeRow(['5', '#55 Focus Driver', '0.000', ''], { isReference: true, carClassColorHex: '#FFDA59', relativeLapDelta: showLapRelationship ? 0 : null }),
      relativeRow(['6', '#61 Near Behind', '+1.200', 'IN'], { carClassColorHex: '#FF4FD8', relativeLapDelta: showLapRelationship ? -2 : null, isPit: true })
    ],
    metrics: [],
    points: [],
    shouldRender: true,
    headerItems: [
      { key: 'timeRemaining', value: '06:37:08' }
    ]
  };
}

function relativeFocusOnlyDisplayModel(previewLabel = 'review fixture', session = 'practice') {
  const model = relativeDisplayModel(previewLabel, session);
  return {
    ...model,
    status: `5 - focus only | ${previewLabel}`,
    rows: model.rows.filter((row) => row.isReference)
  };
}

function filterRelativeReviewRows(model, overlayState) {
  const eachSide = clampInteger(overlayState?.carsEachSide, 3, 0, 8);
  const rows = model.rows || [];
  const focusIndex = rows.findIndex((row) => row.isReference);
  if (focusIndex < 0) {
    return model;
  }

  const ahead = rows.slice(0, focusIndex).slice(-eachSide);
  const behind = rows.slice(focusIndex + 1).slice(0, eachSide);
  const visibleRows = Math.max(1, Math.min(17, eachSide + eachSide + 1));
  const stableRows = Array.from(
    { length: visibleRows },
    () => relativePlaceholderRow(model.columns?.length || 0));
  const referenceSlot = Math.min(eachSide, stableRows.length - 1);
  const aheadStart = Math.max(0, referenceSlot - ahead.length);
  ahead.forEach((row, index) => {
    stableRows[aheadStart + index] = row;
  });
  stableRows[referenceSlot] = rows[focusIndex];
  const behindStart = referenceSlot + 1;
  behind.forEach((row, index) => {
    if (behindStart + index < stableRows.length) {
      stableRows[behindStart + index] = row;
    }
  });

  return {
    ...model,
    rows: stableRows
  };
}

function filterStandingsReviewRows(model, overlayState) {
  const showClassHeaders = contentLabelsEnabled(
    overlayState,
    ['standings.class-separators.enabled', 'Multiclass sections', 'Class separators'],
    true);
  const carsInClass = clampInteger(overlayState?.carsInClass, 14, 1, 24);
  const otherClassRows = clampInteger(overlayState?.otherClassRows, 2, 0, 6);
  const rows = model.rows || [];
  const referenceIndex = rows.findIndex((row) => row.isReference);
  const referenceClassHeaderIndex = findClassHeaderBefore(rows, referenceIndex);
  const primaryRowIndexes = primaryClassRowIndexes(rows, referenceIndex, referenceClassHeaderIndex);
  const primaryRows = new Set(primaryRowIndexes);
  const visiblePrimaryRows = new Set(selectRowsAroundReference(primaryRowIndexes, referenceIndex, carsInClass));
  let currentOtherClassHeaderIndex = null;
  let currentOtherCount = 0;
  const filteredRows = rows.filter((row, index) => {
    if (row.isClassHeader) {
      currentOtherClassHeaderIndex = index === referenceClassHeaderIndex ? null : index;
      currentOtherCount = 0;
      if (currentOtherClassHeaderIndex !== null && otherClassRows <= 0) {
        return false;
      }

      return showClassHeaders;
    }

    if (primaryRows.has(index) && !visiblePrimaryRows.has(index)) {
      return false;
    }

    if (currentOtherClassHeaderIndex === null || index === referenceIndex || referenceClassHeaderIndex < 0) {
      return true;
    }

    if (currentOtherCount >= otherClassRows) {
      return false;
    }

    currentOtherCount += 1;
    return true;
  });

  return { ...model, rows: filteredRows };
}

function primaryClassRowIndexes(rows, referenceIndex, referenceClassHeaderIndex) {
  if (referenceIndex < 0) {
    return rows
      .map((row, index) => row.isClassHeader ? null : index)
      .filter((index) => index !== null);
  }

  const start = referenceClassHeaderIndex >= 0 ? referenceClassHeaderIndex + 1 : 0;
  let end = rows.length;
  for (let index = referenceIndex + 1; index < rows.length; index += 1) {
    if (rows[index]?.isClassHeader) {
      end = index;
      break;
    }
  }

  return rows
    .map((row, index) => index >= start && index < end && !row.isClassHeader ? index : null)
    .filter((index) => index !== null);
}

function selectRowsAroundReference(rowIndexes, referenceIndex, maximumRows) {
  if (rowIndexes.length <= maximumRows) {
    return rowIndexes;
  }

  const referenceSlot = rowIndexes.indexOf(referenceIndex);
  if (referenceSlot < 0) {
    return rowIndexes.slice(0, maximumRows);
  }

  const before = Math.floor((maximumRows - 1) / 2);
  let start = Math.max(0, referenceSlot - before);
  start = Math.min(start, Math.max(0, rowIndexes.length - maximumRows));
  return rowIndexes.slice(start, start + maximumRows);
}

function findClassHeaderBefore(rows, index) {
  for (let cursor = index; cursor >= 0; cursor -= 1) {
    if (rows[cursor]?.isClassHeader) {
      return cursor;
    }
  }

  return -1;
}

function filterTableModelContent(model, overlayId, overlayState, session = null, fixture = '') {
  const labelForColumn = (column) => tableContentLabel(overlayId, column);
  const columnsWithIndex = (model.columns || []).map((column, index) => ({
    column,
    index,
    contentLabel: labelForColumn(column)
  }));
  const visible = columnsWithIndex.filter((entry) => contentEnabled(overlayState, entry.contentLabel, tableContentDefault(overlayId, entry.contentLabel), [], session));
  const suppressFallbackColumn = overlayId === 'standings'
    && columnsWithIndex.length > 0
    && visible.length === 0
    && chromeEnabled(overlayState, 'header', 'Time remaining', session, true);
  const retained = visible.length
    ? visible
    : overlayId === 'relative' || suppressFallbackColumn
      ? []
      : columnsWithIndex.filter((entry) => entry.contentLabel === 'Driver').slice(0, 1);
  return {
    ...model,
    status: suppressFallbackColumn ? 'chrome only | content disabled' : model.status,
    columns: retained.map((entry) => entry.column),
    rows: suppressFallbackColumn
      ? []
      : (model.rows || []).map((row) => row.isClassHeader
        ? row
        : {
            ...row,
            cells: retained.map((entry) => row.cells?.[entry.index] ?? ''),
            ...(Array.isArray(row.cellTones)
              ? { cellTones: retained.map((entry) => row.cellTones?.[entry.index] || null) }
              : {})
          })
  };
}

function tableContentLabel(overlayId, column) {
  const dataKey = String(column?.dataKey || '').toLowerCase();
  if (overlayId === 'standings') {
    return {
      'class-position': 'Class position',
      'car-number': 'Car number',
      driver: 'Driver',
      gap: 'Class gap',
      interval: 'Previous interval',
      'fastest-lap': 'Fastest lap',
      'last-lap': 'Last lap',
      pit: 'Pit status'
    }[dataKey] || column?.label || dataKey;
  }

  if (overlayId === 'relative') {
    return {
      'relative-position': 'Relative position',
      driver: 'Driver',
      gap: 'Relative delta',
      pit: 'Pit status'
    }[dataKey] || column?.label || dataKey;
  }

  return column?.label || dataKey;
}

function tableContentDefault(overlayId, label) {
  if (overlayId === 'relative' && label === 'Pit status') {
    return false;
  }

  return true;
}

function tableModel(overlayId, title, status, rows) {
  return {
    overlayId,
    title,
    status,
    source: 'source: review fixture',
    bodyKind: 'table',
    columns: [
      { label: 'POS', dataKey: 'position', width: 52, alignment: 'left' },
      { label: 'Driver', dataKey: 'driver', width: 190, alignment: 'left' },
      { label: 'GAP', dataKey: 'gap', width: 70, alignment: 'right' },
      { label: 'Class', dataKey: 'class', width: 70, alignment: 'left' }
    ],
    rows: rows.map((cells, index) => ({
      cells,
      isClassHeader: false,
      isReference: index === 1,
      isPit: false,
      isPartial: false,
      carClassColorHex: null,
      headerTitle: null,
      headerDetail: null
    })),
    metrics: [],
    shouldRender: true
  };
}

function relativeRow(cells, extra = {}) {
  return {
    cells,
    isClassHeader: false,
    isReference: false,
    isPit: false,
    isPartial: false,
    carClassColorHex: null,
    headerTitle: null,
    headerDetail: null,
    ...extra
  };
}

function relativePlaceholderRow(cellCount) {
  return relativeRow(
    Array.from({ length: Math.max(0, cellCount) }, () => ''),
    { isPlaceholder: true });
}

function fuelDallara35mV1DisplayModel(overlayState, session) {
  const firstGreenFuelLiters = 49.6766;
  const selectedBurnLitersPerLap = 12.7250;
  const tankRangeLaps = firstGreenFuelLiters / selectedBurnLitersPerLap;
  const metricSections = filterMetricSectionsByContent('fuel-calculator', [
    {
      title: 'Race Information',
      rows: [
        metricRow('Plan', '4 laps | 2 stints | 1 stop', 'info', [
          metricSegment('Race', '4 laps', 'info'),
          metricSegment('Remain', '4.0 laps', 'info'),
          metricSegment('Stints', '2', 'info'),
          metricSegment('Stops', '1', 'info'),
          metricSegment('Save', 'None', 'success')
        ]),
        metricRow('Fuel', `${formatFuelVolume(firstGreenFuelLiters)} | ${formatFuelPerLap(selectedBurnLitersPerLap)} | Covered`, 'success', [
          metricSegment('Current', formatFuelVolume(firstGreenFuelLiters), 'info'),
          metricSegment('Burn', formatFuelPerLap(selectedBurnLitersPerLap), 'info'),
          metricSegment('Tank', `${tankRangeLaps.toFixed(1)} laps`, 'info'),
          metricSegment('Need', 'Covered', 'success')
        ])
      ]
    },
    {
      title: 'Stint Targets',
      rows: [
        metricRow('Stint 1', `2 laps | target ${formatFuelPerLap(selectedBurnLitersPerLap)}`, 'info', [
          metricSegment('Laps', '2 laps', 'info'),
          metricSegment('Target', formatFuelPerLap(selectedBurnLitersPerLap), 'info'),
          metricSegment('Save', 'None', 'success')
        ]),
        metricRow('Stint 2', `2 laps final | target ${formatFuelPerLap(selectedBurnLitersPerLap)}`, 'info', [
          metricSegment('Laps', '2 laps final', 'info'),
          metricSegment('Target', formatFuelPerLap(selectedBurnLitersPerLap), 'info'),
          metricSegment('Save', 'None', 'success')
        ])
      ]
    }
  ], overlayState, session);

  return metricsModel(
    'fuel-calculator',
    'Fuel Calculator',
    '2 stints / 1 stop',
    metricSections.flatMap((section) => section.rows),
    `source: capture-20260523-034827-919 35m Dallara start; first green fuel ${firstGreenFuelLiters.toFixed(1)} L; V1 selected burn ${selectedBurnLitersPerLap.toFixed(1)} L/lap; 4-lap no-stop comparison not surfaced`,
    [],
    metricSections,
    [{ key: 'timeRemaining', value: '35:00' }]);
}

const fuelV2BurnBucketId = Object.freeze({
  last: 'Last',
  fiveLapAverage: 'FiveLapAverage',
  tenLapAverage: 'TenLapAverage',
  historicalNormal: 'HistoricalNormal',
  maximum: 'Maximum',
  minimum: 'Minimum',
  qualifying: 'Qualifying'
});

const fuelV2BurnBucketOrder = Object.freeze([
  fuelV2BurnBucketId.last,
  fuelV2BurnBucketId.fiveLapAverage,
  fuelV2BurnBucketId.tenLapAverage,
  fuelV2BurnBucketId.historicalNormal,
  fuelV2BurnBucketId.maximum,
  fuelV2BurnBucketId.minimum,
  fuelV2BurnBucketId.qualifying
]);

const fuelV2BurnBucketContract = Object.freeze({
  [fuelV2BurnBucketId.last]: Object.freeze({ label: 'Last', burnSource: 'LiveLastLap', sampleCount: 1, confidence: 'CleanBaseline', strategyEligible: true, cleanBaselineEligible: true }),
  [fuelV2BurnBucketId.fiveLapAverage]: Object.freeze({ label: '5L', burnSource: 'LiveFiveLapAverage', sampleCount: 5, confidence: 'CleanBaseline', strategyEligible: true, cleanBaselineEligible: true }),
  [fuelV2BurnBucketId.tenLapAverage]: Object.freeze({ label: '10L', burnSource: 'LiveTenLapAverage', sampleCount: 10, confidence: 'CleanBaseline', strategyEligible: true, cleanBaselineEligible: true }),
  [fuelV2BurnBucketId.historicalNormal]: Object.freeze({ label: 'History', burnSource: 'HistoricalNormal', sampleCount: null, confidence: 'Seeded', strategyEligible: false, cleanBaselineEligible: false }),
  [fuelV2BurnBucketId.maximum]: Object.freeze({ label: 'Max', burnSource: 'LiveMaximum', sampleCount: null, confidence: 'CleanBaseline', strategyEligible: true, cleanBaselineEligible: true }),
  [fuelV2BurnBucketId.minimum]: Object.freeze({ label: 'Min', burnSource: 'LiveMinimum', sampleCount: null, confidence: 'CleanBaseline', strategyEligible: true, cleanBaselineEligible: true }),
  [fuelV2BurnBucketId.qualifying]: Object.freeze({ label: 'Quali', burnSource: 'QualifyingSeed', sampleCount: 1, confidence: 'Seeded', strategyEligible: false, cleanBaselineEligible: false })
});

function fuelV2BurnEvidence(bucketId, raw = {}, defaults = {}, hasValue = true) {
  const contract = fuelV2BurnBucketContract[bucketId];
  if (!contract) throw new Error(`Unknown Fuel V2 burn bucket: ${bucketId}`);
  const sampleCountInput = raw.sampleCount ?? defaults.sampleCount ?? contract.sampleCount;
  const sampleCount = Number.isInteger(Number(sampleCountInput)) && Number(sampleCountInput) > 0
    ? Number(sampleCountInput)
    : null;
  const contextFlags = raw.contextFlags ?? defaults.contextFlags ?? (hasValue ? ['CleanRace'] : []);
  const strategyEligible = raw.strategyEligible
    ?? defaults.strategyEligible
    ?? contract.strategyEligible;
  const cleanBaselineEligible = raw.cleanBaselineEligible
    ?? defaults.cleanBaselineEligible
    ?? contract.cleanBaselineEligible;
  const confidence = raw.confidence
    ?? defaults.confidence
    ?? contract.confidence;
  const burnSource = raw.burnSource
    ?? defaults.burnSource
    ?? contract.burnSource;
  const source = raw.source
    ?? defaults.source
    ?? (hasValue ? burnSource : 'unavailable');

  return {
    id: bucketId,
    label: contract.label,
    source: hasValue ? String(source) : 'unavailable',
    burnSource: hasValue ? String(burnSource) : 'Unavailable',
    sampleCount: hasValue ? sampleCount : null,
    confidence: hasValue ? String(confidence) : 'Unavailable',
    contextFlags: hasValue ? [...new Set(contextFlags)] : [],
    displayEligible: hasValue && (raw.displayEligible ?? defaults.displayEligible ?? true),
    cleanBaselineEligible: hasValue && Boolean(cleanBaselineEligible),
    strategyEligible: hasValue && Boolean(strategyEligible),
    detailLabel: hasValue ? String(raw.detailLabel ?? raw.label ?? defaults.detailLabel ?? '').trim() : ''
  };
}

function fuelV2BurnBucket(bucketId, rawValue, defaults = {}) {
  const raw = typeof rawValue === 'object' && rawValue !== null ? rawValue : { value: rawValue };
  const numeric = raw.value === null || raw.value === undefined ? Number.NaN : Number(raw.value);
  const hasValue = Number.isFinite(numeric) && numeric > 0;
  return {
    ...fuelV2BurnEvidence(bucketId, raw, defaults, hasValue),
    value: hasValue ? numeric : null
  };
}

function fuelV2DerivedBurnBucket(bucket, value, operationSource) {
  const numeric = value === null || value === undefined ? Number.NaN : Number(value);
  const hasValue = Number.isFinite(numeric);
  return {
    ...bucket,
    value: hasValue ? numeric : null,
    source: hasValue ? `${operationSource} <- ${bucket.source}` : 'unavailable',
    confidence: hasValue ? bucket.confidence : 'Unavailable',
    displayEligible: hasValue && bucket.displayEligible,
    cleanBaselineEligible: false,
    strategyEligible: hasValue && bucket.strategyEligible
  };
}

function fuelV2BurnBucketEvidence(bucket) {
  return {
    burnBucketId: bucket.id,
    burnSource: bucket.burnSource,
    sampleCount: bucket.sampleCount,
    confidence: bucket.confidence,
    contextFlags: bucket.contextFlags,
    displayEligible: bucket.displayEligible,
    cleanBaselineEligible: bucket.cleanBaselineEligible,
    strategyEligible: bucket.strategyEligible,
    provenance: bucket.source
  };
}

function fuelPerLapWorkbenchReviewModel(state = 'populated') {
  // Browser review mirrors the accepted Core Fuel/Lap window contract using
  // explicit accepted-span controls. Core remains the calculation authority;
  // these isolated states keep the eventual product cell reviewable while
  // other workbench cells are active.
  const fuelPerLapSamples = state === 'populated'
    ? [12.95, 13.00, 13.20, 13.30, 13.65, 13.49, 13.49, 13.50, 13.50, 13.52]
    : state === 'degraded'
      ? [13.54]
      : [0, -1, Number.NaN];
  const maximumSeed = state === 'trusted-seed'
    ? {
        value: 14.2,
        source: 'trusted historical maximum',
        burnSource: 'HistoricalSeed',
        sampleCount: 12,
        confidence: 'Seeded',
        displayEligible: true,
        cleanBaselineEligible: false,
        strategyEligible: true
      }
    : null;
  const windows = fuelPerLapWorkbenchWindows(fuelPerLapSamples, maximumSeed);
  const row = fuelPerLapWorkbenchRow(windows);
  const source = state === 'populated'
    ? 'source: Fuel V2 workbench; VLN 4h capture-derived accepted clean-span control. Browser mirrors staged Core positive-finite filtering, trailing full Last/5L/10L, live-or-seed Max semantics, and typed bucket provenance/eligibility.'
    : state === 'degraded'
      ? 'source: Fuel V2 workbench; Dallara 4L blip capture-derived single accepted clean-span control. Full 5L and 10L windows remain unavailable until they mature.'
      : state === 'trusted-seed'
        ? 'source: Fuel V2 workbench; explicit trusted historical Max seed proves strategy eligibility does not imply clean-baseline eligibility or clean confidence.'
      : 'source: Fuel V2 workbench; deterministic invalid-input control produces no accepted clean fuel spans. Fuel/Lap buckets remain unavailable rather than using edge-state or invented data.';

  return metricsModel(
    'fuel-calculator',
    'Fuel Calculator',
    'fuel/lap workbench',
    [row],
    source,
    [],
    [{ title: 'Fuel/Lap Workbench', rows: [row] }],
    [{ key: 'timeRemaining', value: 'Fuel/Lap V2', tone: 'info' }]);
}

function fuelPerLapWorkbenchWindows(fuelPerLapSamples, maxSeed = null, historicalNormalSeed = null) {
  const samples = (fuelPerLapSamples || [])
    .map(Number)
    .filter((value) => Number.isFinite(value) && value > 0);
  const trailingAverage = (sampleCount) => samples.length >= sampleCount
    ? samples.slice(-sampleCount).reduce((total, value) => total + value, 0) / sampleCount
    : null;
  const liveMax = samples.length > 0
    ? fuelV2BurnBucket(
        fuelV2BurnBucketId.maximum,
        Math.max(...samples),
        { sampleCount: samples.length, source: `live max (${samples.length})` })
    : null;
  const seedMax = fuelV2BurnBucket(
    fuelV2BurnBucketId.maximum,
    maxSeed,
    { burnSource: 'HistoricalSeed', source: 'maximum seed', strategyEligible: false, cleanBaselineEligible: false, confidence: 'Seeded' });
  const maximum = liveMax?.value !== null && liveMax?.value !== undefined && seedMax.value !== null
    ? liveMax.value >= seedMax.value ? liveMax : seedMax
    : liveMax ?? (seedMax.value ? seedMax : null);
  const history = fuelV2BurnBucket(
    fuelV2BurnBucketId.historicalNormal,
    historicalNormalSeed,
    { burnSource: 'HistoricalNormal', source: 'classified history normal', strategyEligible: false, cleanBaselineEligible: false, confidence: 'Seeded' });

  return {
    acceptedLapCount: samples.length,
    buckets: {
      [fuelV2BurnBucketId.last]: fuelV2BurnBucket(
        fuelV2BurnBucketId.last,
        samples.length > 0 ? samples.at(-1) : null,
        { source: 'live last lap (1)' }),
      [fuelV2BurnBucketId.fiveLapAverage]: fuelV2BurnBucket(
        fuelV2BurnBucketId.fiveLapAverage,
        trailingAverage(5),
        { source: 'live 5L average (5)' }),
      [fuelV2BurnBucketId.tenLapAverage]: fuelV2BurnBucket(
        fuelV2BurnBucketId.tenLapAverage,
        trailingAverage(10),
        { source: 'live 10L average (10)' }),
      [fuelV2BurnBucketId.historicalNormal]: history,
      [fuelV2BurnBucketId.maximum]: maximum ?? fuelV2BurnBucket(fuelV2BurnBucketId.maximum, null),
      [fuelV2BurnBucketId.minimum]: fuelV2BurnBucket(
        fuelV2BurnBucketId.minimum,
        samples.length > 0 ? Math.min(...samples) : null,
        { sampleCount: samples.length, source: `live min (${samples.length})` }),
      [fuelV2BurnBucketId.qualifying]: fuelV2BurnBucket(fuelV2BurnBucketId.qualifying, null)
    }
  };
}

function fuelLapsWorkbenchReviewModel({ includeLapRows = false, activeWorkbench = 'target' } = {}) {
  // Browser-only replay workbench data. Keep formulas and field shape aligned
  // with staged Core V2 calculators under src/TmrOverlay.Core/Fuel/V2; do not
  // treat these fixtures as production FuelStrategyCalculator wiring.
  const lapRows = [
    fuelLapsWorkbenchRow('Dallara 45m / V1', 'timed / selected pace', 'info', ['5.99', '6.04', '5.93', '--', '6.06'], '6'),
    fuelLapsWorkbenchRow('Dallara 45m / V2', 'lap budget / same shape', 'info', ['5.99 seed', '6.04', '5.93', '--', '6.06'], '6'),
    fuelLapsWorkbenchRow('Dallara 4L full / V1', 'fixed / full race', 'info', ['4', '4', '4', '--', '4'], '4'),
    fuelLapsWorkbenchRow('Dallara 4L full / V2', 'lap budget / SDK authority', 'info', ['4 SDK', '4 SDK', '4 SDK', '--', '4 SDK'], '4'),
    fuelLapsWorkbenchRow('Dallara 4L blip / V1', 'fixed / transient field', 'warning', ['4', '4', '4', '--', '4'], '4'),
    fuelLapsWorkbenchRow('Dallara 4L blip / V2', 'lap budget / SDK authority', 'info', ['4 SDK', '4 SDK', '4 SDK', '--', '4 SDK'], '4'),
    fuelLapsWorkbenchRow('GR86 3L start / V1', 'fixed / short start', 'info', ['3', '3', '--', '--', '3'], '3'),
    fuelLapsWorkbenchRow('GR86 3L start / V2', 'lap budget / SDK authority', 'info', ['3 SDK', '3 SDK', '--', '--', '3 SDK'], '3'),
    fuelLapsWorkbenchRow('VLN 4h team / V1', 'timed / selected pace', 'info', ['30.96', '30.39', '30', '--', '31'], '30'),
    fuelLapsWorkbenchRow('VLN 4h team / V2', 'lap budget / same shape', 'info', ['30.96 seed', '30.39', '30', '--', '31'], '30'),
    fuelLapsWorkbenchRow('24h rejoin / V1', 'seed then selected pace', 'waiting', ['179.68', '173.30', '166.01 degraded', '173.13', '--'], '173'),
    fuelLapsWorkbenchRow('24h rejoin / V2', 'lap budget / held degraded pace', 'warning', fuelLapsWorkbenchV2Values(fuelLapsWorkbench24hScenario()), '173'),
    fuelLapsWorkbenchRow('24h rejoin / Clean', 'quali/clean running pace', 'info', ['179', '174', '174', '174', '--'], '173'),
    fuelLapsWorkbenchRow('24h rejoin 8h / V1', 'synthetic 8h / selected pace', 'warning', ['59.89', '58.78', '51.65 degraded', '58.60', '--'], '59?'),
    fuelLapsWorkbenchRow('24h rejoin 8h / V2', 'synthetic 8h / held degraded pace', 'warning', fuelLapsWorkbenchV2Values(fuelLapsWorkbench24hScenario({ durationSeconds: 28800 })), '59?'),
    fuelLapsWorkbenchRow('24h rejoin 8h / Clean', 'synthetic 8h / clean pace', 'info', ['60', '59', '59', '59', '--'], '59?'),
    fuelLapsWorkbenchRow('24h rejoin / Elapsed', 'observed elapsed pace', 'info', ['--', '172', '172', '--', '--'], '173')
  ];
  const rangeRows = [
    fuelRangeWorkbenchRow('VLN 4h team / Mid S1', 'race / all V2 windows', 'info', {
      fuel: 61.64,
      rangeV1: 4.61,
      last: 4.56,
      five: 4.57,
      ten: 4.61,
      max: 4.52
    }),
    fuelRangeWorkbenchRow('Dallara 45m / Mid S1', 'race / partial 5L', 'info', {
      fuel: 31.23,
      rangeV1: 2.29,
      last: 2.27,
      five: { value: 2.29, suffix: '3/5', sampleCount: 3, strategyEligible: false, cleanBaselineEligible: false, confidence: 'Contextual' },
      ten: null,
      max: 2.26
    }),
    fuelRangeWorkbenchRow('Dallara 45m / Stop 1', 'race / post-stop snapshot', 'info', {
      fuel: 33.29,
      rangeV1: 2.44,
      last: 2.42,
      five: { value: 2.44, suffix: '3/5', sampleCount: 3, strategyEligible: false, cleanBaselineEligible: false, confidence: 'Contextual' },
      ten: null,
      max: 2.41
    }),
    fuelRangeWorkbenchRow('Dallara 45m / Half Rem', 'race / remaining-time midpoint', 'info', {
      fuel: 20.36,
      rangeV1: 1.50,
      last: 1.48,
      five: { value: 1.49, suffix: '3/5', sampleCount: 3, strategyEligible: false, cleanBaselineEligible: false, confidence: 'Contextual' },
      ten: null,
      max: 1.48
    }),
    fuelRangeWorkbenchRow('Dallara 4L full / Mid S1', 'fixed race / partial 5L', 'info', {
      fuel: 30.98,
      rangeV1: 2.43,
      last: 2.51,
      five: { value: 2.45, suffix: '3/5', sampleCount: 3, strategyEligible: false, cleanBaselineEligible: false, confidence: 'Contextual' },
      ten: null,
      max: 2.39
    }),
    fuelRangeWorkbenchRow('Dallara 4L full / Half', 'fixed race / partial 5L', 'info', {
      fuel: 26.21,
      rangeV1: 2.05,
      last: 2.12,
      five: { value: 2.07, suffix: '3/5', sampleCount: 3, strategyEligible: false, cleanBaselineEligible: false, confidence: 'Contextual' },
      ten: null,
      max: 2.02
    }),
    fuelRangeWorkbenchRow('Dallara 4L full / Stop 1', 'fixed race / late snapshot', 'info', {
      fuel: 15.27,
      rangeV1: 1.20,
      last: 1.24,
      five: { value: 1.21, suffix: '3/5', sampleCount: 3, strategyEligible: false, cleanBaselineEligible: false, confidence: 'Contextual' },
      ten: null,
      max: 1.18
    }),
    fuelRangeWorkbenchRow('Dallara 4L blip / Mid S1', 'abnormal stop / one span', 'warning', {
      fuel: 29.58,
      rangeV1: 2.21,
      last: 2.18,
      five: null,
      ten: null,
      max: 2.18
    }),
    fuelRangeWorkbenchRow('Dallara 4L blip / Stop 1', 'abnormal stop / repair snapshot', 'warning', {
      fuel: 18.86,
      rangeV1: 1.41,
      last: 1.39,
      five: null,
      ten: null,
      max: 1.39
    }),
    fuelRangeWorkbenchRow('Dallara 4L blip / Half', 'abnormal stop / one span', 'warning', {
      fuel: 31.42,
      rangeV1: 2.35,
      last: 2.32,
      five: null,
      ten: null,
      max: 2.32
    }),
    fuelRangeWorkbenchRow('Dallara Daytona gear test / Long', 'non-race stress / all V2 windows', 'warning', {
      fuel: 63.71,
      rangeV1: 37.70,
      last: 42.19,
      five: 34.25,
      ten: 33.53,
      max: 25.18
    }),
    fuelRangeWorkbenchRow('Dallara Daytona gear test / Short', 'non-race stress / last + 5L + max', 'warning', {
      fuel: 66.17,
      rangeV1: 34.83,
      last: 44.11,
      five: 36.56,
      ten: null,
      max: 24.33
    }),
    fuelRangeWorkbenchRow('Dallara quali seed / Mid', 'qualifying / max seed only', 'warning', {
      fuel: 46.91,
      rangeV1: 3.40,
      last: null,
      five: null,
      ten: null,
      max: { value: 3.40, burnSource: 'QualifyingSeed', sampleCount: 1, strategyEligible: false, cleanBaselineEligible: false, confidence: 'Seeded', source: 'qualifying seed' }
    }),
    fuelRangeWorkbenchRow('Stress / Known zero fuel', 'zero is factual', 'info', {
      fuel: 0,
      rangeV1: null,
      last: 0,
      five: 0,
      ten: 0,
      max: 0
    }),
    fuelRangeWorkbenchRow('Stress / Explicit null fuel', 'null stays unavailable', 'waiting', {
      fuel: null,
      rangeV1: null,
      last: null,
      five: null,
      ten: null,
      max: null
    })
  ];
  const targetUsageCurrentRows = [
    fuelTargetUsageWorkbenchRow('VLN 4h team / 5-lap stretch', 'current fuel / no reserve', 'info', {
      budget: 61.6400,
      budgetLabel: 'Fuel',
      referenceBurn: 13.5176,
      referenceBucketId: fuelV2BurnBucketId.last
    }),
    fuelTargetUsageWorkbenchRow('Dallara 45m / Stop edge', 'current fuel / no reserve', 'info', {
      budget: 33.2921,
      budgetLabel: 'Fuel',
      referenceBurn: 13.7571,
      referenceBucketId: fuelV2BurnBucketId.last
    }),
    fuelTargetUsageWorkbenchRow('Dallara 4L full / 2-lap edge', 'current fuel / no reserve', 'info', {
      budget: 26.2104,
      budgetLabel: 'Fuel',
      referenceBurn: 12.3634,
      referenceBucketId: fuelV2BurnBucketId.last
    }),
    fuelTargetUsageWorkbenchRow('Dallara 4L blip / Abnormal', 'abnormal stop / no reserve', 'warning', {
      budget: 29.5762,
      budgetLabel: 'Fuel',
      referenceBurn: 13.5671,
      referenceBucketId: fuelV2BurnBucketId.last
    }),
    fuelTargetUsageWorkbenchRow('Stress / Round-centered candidates', '44.0 L / 10.0 L reference', 'info', {
      budget: 44.0,
      budgetLabel: 'Fuel',
      referenceBurn: 10.0,
      referenceBucketId: fuelV2BurnBucketId.last
    }),
    fuelTargetUsageWorkbenchRow('Stress / Missing reference', 'required burn remains factual', 'waiting', {
      budget: 44.0,
      budgetLabel: 'Fuel',
      referenceBurn: null,
      referenceBucketId: fuelV2BurnBucketId.last,
      targetLaps: [5, -1, 4, 4, 3]
    }),
    fuelTargetUsageWorkbenchRow('Stress / Missing reference identity', 'numeric comparator fails closed', 'warning', {
      budget: 44.0,
      budgetLabel: 'Fuel',
      referenceBurn: 10.0,
      targetLaps: [3, 4, 5]
    })
  ];
  const targetUsageCapRows = [
    fuelTargetUsageWorkbenchRow('VLN 4h team / Green start', 'first green fuel / no reserve', 'info', {
      budget: 102.8464,
      budgetLabel: 'Green',
      referenceBurn: 13.5176,
      referenceBucketId: fuelV2BurnBucketId.last
    }),
    fuelTargetUsageWorkbenchRow('Dallara 45m / Green start', 'first green fuel / no reserve', 'info', {
      budget: 58.9871,
      budgetLabel: 'Green',
      referenceBurn: 13.7593,
      referenceBucketId: fuelV2BurnBucketId.last
    }),
    fuelTargetUsageWorkbenchRow('Dallara 4L full / Green start', 'first green fuel / no reserve', 'info', {
      budget: 50.1023,
      budgetLabel: 'Green',
      referenceBurn: 12.3421,
      referenceBucketId: fuelV2BurnBucketId.last
    }),
    fuelTargetUsageWorkbenchRow('Dallara quali seed / Green est', 'expected green fuel / seed burn', 'warning', {
      budget: 50.1023,
      budgetLabel: 'Green',
      referenceBurn: 13.7982,
      referenceBucketId: fuelV2BurnBucketId.qualifying,
      referenceConfidence: 'Seeded',
      referenceSource: 'qualifying seed'
    })
  ];
  const targetUsageSections = [
    { title: 'Target Usage - Green Start', rows: targetUsageCapRows },
    { title: 'Target Usage - Current Edges', rows: targetUsageCurrentRows }
  ];
  const capacityGridSections = [
    {
      title: 'Effective Capacity V2',
      headers: ['Scenario', 'Physical', 'Driver cap', 'Class cap', 'Observed', 'Effective', 'Source', 'State'],
      rows: [
        fuelCapacityWorkbenchGridRow('Dallara 45m / 80% event cap', {
          physicalCapacityLiters: 75,
          driverCapPercent: 0.8,
          classCapPercent: 0.8,
          observedFuelLiters: 58.99
        }),
        fuelCapacityWorkbenchGridRow('Dallara 4L / 68% event cap', {
          physicalCapacityLiters: 75,
          driverCapPercent: 0.68,
          classCapPercent: 0.68,
          observedFuelLiters: 50.10
        }),
        fuelCapacityWorkbenchGridRow('VLN 4h / unrestricted', {
          physicalCapacityLiters: 104.94,
          driverCapPercent: 1,
          classCapPercent: 1,
          observedFuelLiters: 102.85
        }),
        fuelCapacityWorkbenchGridRow('Stress / Driver cap only', {
          physicalCapacityLiters: 75,
          driverCapPercent: 0.68,
          classCapPercent: null,
          observedFuelLiters: 50
        }),
        fuelCapacityWorkbenchGridRow('Stress / Missing cap evidence', {
          physicalCapacityLiters: 75,
          driverCapPercent: null,
          classCapPercent: null,
          observedFuelLiters: 50
        }),
        fuelCapacityWorkbenchGridRow('Stress / Conflicting caps', {
          physicalCapacityLiters: 75,
          driverCapPercent: 0.8,
          classCapPercent: 0.68,
          observedFuelLiters: 50
        }),
        fuelCapacityWorkbenchGridRow('Stress / Observed above cap', {
          physicalCapacityLiters: 75,
          driverCapPercent: 0.68,
          classCapPercent: 0.68,
          observedFuelLiters: 52
        }),
        fuelCapacityWorkbenchGridRow('Stress / Invalid driver cap', {
          physicalCapacityLiters: 75,
          driverCapPercent: 1.2,
          classCapPercent: 0.8,
          observedFuelLiters: 50
        }),
        fuelCapacityWorkbenchGridRow('Stress / Missing physical tank', {
          physicalCapacityLiters: null,
          driverCapPercent: 0.8,
          classCapPercent: 0.8,
          observedFuelLiters: 50
        })
      ]
    }
  ];
  const checkpointGridSections = [
    {
      title: 'Fuel Budget Flow V2',
      headers: ['Scenario', 'Cap', 'First green', 'Current', 'At box', 'Service', 'Pit exit', 'Request target', 'State'],
      rows: [
        fuelCheckpointWorkbenchGridRow('Dallara 45m / Measured green', {
          capacity: { physicalCapacityLiters: 75, driverCapPercent: 0.8, classCapPercent: 0.8 },
          measuredFirstGreenFuelLiters: 58.99,
          currentFuelLiters: 58.99
        }),
        fuelCheckpointWorkbenchGridRow('Stress / Single-cap confidence', {
          capacity: { physicalCapacityLiters: 75, driverCapPercent: 0.8, classCapPercent: null }
        }),
        fuelCheckpointWorkbenchGridRow('Dallara 4L / Estimated green', {
          capacity: { physicalCapacityLiters: 75, driverCapPercent: 0.68, classCapPercent: 0.68 },
          estimatedFormationFuelLiters: 0.9
        }),
        fuelCheckpointWorkbenchGridRow('Stress / Formation exceeds cap', {
          capacity: { physicalCapacityLiters: 75, driverCapPercent: 0.68, classCapPercent: 0.68 },
          estimatedFormationFuelLiters: 52
        }),
        fuelCheckpointWorkbenchGridRow('Dallara 45m / Projected pit cycle', {
          capacity: { physicalCapacityLiters: 75, driverCapPercent: 0.8, classCapPercent: 0.8 },
          currentFuelLiters: 33.29,
          expectedFuelToBoxLiters: 1,
          plannedServiceAddLiters: 20,
          expectedBoxToPitExitFuelLiters: 0.14
        }),
        fuelCheckpointWorkbenchGridRow('Stress / Measured overrides projections', {
          capacity: { physicalCapacityLiters: 60, driverCapPercent: 1, classCapPercent: 1 },
          currentFuelLiters: 30,
          measuredAtBoxFuelLiters: 20,
          expectedFuelToBoxLiters: 2,
          measuredServiceCompleteFuelLiters: 50,
          plannedServiceAddLiters: 10,
          measuredPitExitFuelLiters: 49.8,
          expectedBoxToPitExitFuelLiters: 0.1
        }),
        fuelCheckpointWorkbenchGridRow('Stress / Known zero flow', {
          capacity: { physicalCapacityLiters: 60, driverCapPercent: 1, classCapPercent: 1 },
          currentFuelLiters: 0,
          measuredAtBoxFuelLiters: 0,
          plannedServiceAddLiters: 0,
          expectedBoxToPitExitFuelLiters: 0
        }),
        fuelCheckpointWorkbenchGridRow('Stress / Missing current telemetry', {
          capacity: { physicalCapacityLiters: 60, driverCapPercent: 1, classCapPercent: 1 },
          currentFuelLiters: null,
          expectedFuelToBoxLiters: 1,
          plannedServiceAddLiters: 20,
          expectedBoxToPitExitFuelLiters: 0.14
        }),
        fuelCheckpointWorkbenchGridRow('Stress / Above capacity is not clamped', {
          capacity: { physicalCapacityLiters: 75, driverCapPercent: 0.68, classCapPercent: 0.68 },
          measuredAtBoxFuelLiters: 20,
          plannedServiceAddLiters: 40,
          expectedBoxToPitExitFuelLiters: 0.5
        }),
        fuelCheckpointWorkbenchGridRow('Stress / Invalid transition input', {
          capacity: { physicalCapacityLiters: 60, driverCapPercent: 1, classCapPercent: 1 },
          currentFuelLiters: 20,
          expectedFuelToBoxLiters: -1,
          plannedServiceAddLiters: 10
        }),
        fuelCheckpointWorkbenchGridRow('Stress / Cannot reach box', {
          capacity: { physicalCapacityLiters: 60, driverCapPercent: 1, classCapPercent: 1 },
          currentFuelLiters: 1,
          expectedFuelToBoxLiters: 2,
          plannedServiceAddLiters: 20,
          expectedBoxToPitExitFuelLiters: 0.1
        })
      ]
    }
  ];
  const boundaryGridSections = [
    {
      title: 'Boundary and Feasibility V2',
      headers: ['Scenario', 'Bucket', 'Range', 'Safe', 'Next lap', 'Desired / add', 'Room / clamp', 'Shortfall', 'Max', 'State'],
      rows: [
        fuelBoundaryWorkbenchGridRow('Dallara 45m / Projected stop', {
          bucketId: fuelV2BurnBucketId.last,
          capacity: { physicalCapacityLiters: 75, driverCapPercent: 0.8, classCapPercent: 0.8 },
          currentFuelLiters: 33.2921,
          measuredAtBoxFuelLiters: 32.29,
          targetLaps: 2,
          burnLitersPerLap: 13.7571
        }),
        fuelBoundaryWorkbenchGridRow('Stress / Exact 3-lap edge', {
          bucketId: fuelV2BurnBucketId.last,
          capacity: { physicalCapacityLiters: 60, driverCapPercent: 1, classCapPercent: 1 },
          currentFuelLiters: 30,
          measuredAtBoxFuelLiters: 10,
          targetLaps: 5,
          burnLitersPerLap: 10
        }),
        fuelBoundaryWorkbenchGridRow('Stress / Below displayed 3.00', {
          bucketId: fuelV2BurnBucketId.last,
          capacity: { physicalCapacityLiters: 60, driverCapPercent: 1, classCapPercent: 1 },
          currentFuelLiters: 29.99999,
          measuredAtBoxFuelLiters: 10,
          targetLaps: 3,
          burnLitersPerLap: 10
        }),
        fuelBoundaryWorkbenchGridRow('Stress / Above displayed 3.00', {
          bucketId: fuelV2BurnBucketId.last,
          capacity: { physicalCapacityLiters: 60, driverCapPercent: 1, classCapPercent: 1 },
          currentFuelLiters: 30.00001,
          measuredAtBoxFuelLiters: 10,
          targetLaps: 3,
          burnLitersPerLap: 10
        }),
        fuelBoundaryWorkbenchGridRow('Stress / Known zero facts', {
          bucketId: fuelV2BurnBucketId.last,
          capacity: { physicalCapacityLiters: 60, driverCapPercent: 1, classCapPercent: 1 },
          currentFuelLiters: 0,
          measuredAtBoxFuelLiters: 0,
          targetLaps: 2,
          burnLitersPerLap: 10
        }),
        fuelBoundaryWorkbenchGridRow('Stress / Tank-limited target', {
          bucketId: fuelV2BurnBucketId.last,
          capacity: { physicalCapacityLiters: 50, driverCapPercent: 1, classCapPercent: 1 },
          currentFuelLiters: 20,
          measuredAtBoxFuelLiters: 10,
          targetLaps: 6,
          burnLitersPerLap: 10
        }),
        fuelBoundaryWorkbenchGridRow('Stress / Margin flips feasibility', {
          bucketId: fuelV2BurnBucketId.last,
          capacity: { physicalCapacityLiters: 50, driverCapPercent: 1, classCapPercent: 1 },
          currentFuelLiters: 20,
          measuredAtBoxFuelLiters: 10,
          targetLaps: 5,
          burnLitersPerLap: 10,
          reserveFuelLiters: 0.0001
        }),
        fuelBoundaryWorkbenchGridRow('Stress / Missing current only', {
          bucketId: fuelV2BurnBucketId.last,
          capacity: { physicalCapacityLiters: 60, driverCapPercent: 1, classCapPercent: 1 },
          currentFuelLiters: null,
          measuredAtBoxFuelLiters: 10,
          targetLaps: 2,
          burnLitersPerLap: 10
        }),
        fuelBoundaryWorkbenchGridRow('Stress / Missing at-box only', {
          bucketId: fuelV2BurnBucketId.last,
          capacity: { physicalCapacityLiters: 60, driverCapPercent: 1, classCapPercent: 1 },
          currentFuelLiters: 20,
          measuredAtBoxFuelLiters: null,
          targetLaps: 2,
          burnLitersPerLap: 10
        }),
        fuelBoundaryWorkbenchGridRow('Stress / Missing capacity', {
          bucketId: fuelV2BurnBucketId.last,
          capacity: { physicalCapacityLiters: null, driverCapPercent: null, classCapPercent: null },
          currentFuelLiters: 20,
          measuredAtBoxFuelLiters: 10,
          targetLaps: 2,
          burnLitersPerLap: 10
        }),
        fuelBoundaryWorkbenchGridRow('Stress / Conflicting capacity', {
          bucketId: fuelV2BurnBucketId.last,
          capacity: { physicalCapacityLiters: 75, driverCapPercent: 0.8, classCapPercent: 0.68 },
          currentFuelLiters: 20,
          measuredAtBoxFuelLiters: 10,
          targetLaps: 5,
          burnLitersPerLap: 10
        }),
        fuelBoundaryWorkbenchGridRow('Stress / Invalid reserve', {
          bucketId: fuelV2BurnBucketId.last,
          capacity: { physicalCapacityLiters: 60, driverCapPercent: 1, classCapPercent: 1 },
          currentFuelLiters: 20,
          measuredAtBoxFuelLiters: 10,
          targetLaps: 2,
          burnLitersPerLap: 10,
          reserveFuelLiters: -1
        }),
        fuelBoundaryWorkbenchGridRow('Stress / Invalid current telemetry', {
          bucketId: fuelV2BurnBucketId.last,
          capacity: { physicalCapacityLiters: 60, driverCapPercent: 1, classCapPercent: 1 },
          currentFuelLiters: -1,
          measuredAtBoxFuelLiters: 10,
          targetLaps: 2,
          burnLitersPerLap: 10
        }),
        fuelBoundaryWorkbenchGridRow('Stress / Invalid at-box telemetry', {
          bucketId: fuelV2BurnBucketId.last,
          capacity: { physicalCapacityLiters: 60, driverCapPercent: 1, classCapPercent: 1 },
          currentFuelLiters: 20,
          measuredAtBoxFuelLiters: -1,
          expectedFuelToBoxLiters: 1,
          targetLaps: 2,
          burnLitersPerLap: 10
        }),
        fuelBoundaryWorkbenchGridRow('Stress / Invalid capacity evidence', {
          bucketId: fuelV2BurnBucketId.last,
          capacity: { physicalCapacityLiters: -1, driverCapPercent: 1, classCapPercent: 1 },
          currentFuelLiters: 20,
          measuredAtBoxFuelLiters: 10,
          targetLaps: 2,
          burnLitersPerLap: 10
        }),
        fuelBoundaryWorkbenchGridRow('Stress / Incomplete burn evidence', {
          bucketId: fuelV2BurnBucketId.last,
          capacity: { physicalCapacityLiters: 60, driverCapPercent: 1, classCapPercent: 1 },
          currentFuelLiters: 20,
          measuredAtBoxFuelLiters: 10,
          targetLaps: 2,
          burnLitersPerLap: { value: 10, burnSource: 'Unavailable' }
        }),
        fuelBoundaryWorkbenchGridRow('Stress / Finite overflow edge', {
          bucketId: fuelV2BurnBucketId.last,
          capacity: { physicalCapacityLiters: 60, driverCapPercent: 1, classCapPercent: 1 },
          currentFuelLiters: Number.MAX_VALUE,
          measuredAtBoxFuelLiters: 10,
          targetLaps: null,
          burnLitersPerLap: Number.MAX_VALUE
        }),
        fuelBoundaryWorkbenchGridRow('Stress / Seed-only evidence', {
          capacity: { physicalCapacityLiters: 60, driverCapPercent: 1, classCapPercent: 1 },
          currentFuelLiters: 24,
          measuredAtBoxFuelLiters: 12,
          targetLaps: 4,
          bucketId: fuelV2BurnBucketId.qualifying,
          burnLitersPerLap: {
            value: 12,
            burnSource: 'QualifyingSeed',
            sampleCount: 1,
            confidence: 'Seeded',
            strategyEligible: false,
            cleanBaselineEligible: false,
            source: 'qualifying seed'
          }
        })
      ]
    }
  ];
  const snapshotGridSections = [
    {
      title: 'Shared Snapshot V2',
      headers: ['Scenario', 'Lap budget', 'Capacity', 'Current / box', 'Range', 'Fuel to add', 'Target usage', 'Plan', 'Contract'],
      rows: [
        fuelSharedSnapshotWorkbenchGridRow('Live control / explicit owners', {
          lapBudget: { primaryLapsRemaining: 12, possibleLapsRemaining: 11.5, canDriveFuelAdvice: true },
          checkpoints: {
            capacity: { physicalCapacityLiters: 100, driverCapPercent: 0.6, classCapPercent: 0.6 },
            measuredFirstGreenFuelLiters: 58,
            currentFuelLiters: 40,
            measuredAtBoxFuelLiters: 10,
            measuredServiceCompleteFuelLiters: 50,
            measuredPitExitFuelLiters: 49
          },
          burns: {
            [fuelV2BurnBucketId.last]: 10,
            [fuelV2BurnBucketId.fiveLapAverage]: { value: 10, sampleCount: 5 }
          },
          boundary: { targetLaps: 2, reserveFuelLiters: 1, pitLaneFuelLiters: 0.5 },
          targetUsage: {
            fuelBudgetCheckpoint: 'firstGreen',
            referenceBurnBucketId: fuelV2BurnBucketId.fiveLapAverage,
            targetLaps: [4, 5, 6]
          },
          plan: {
            mode: 'current-checkpoint',
            plannedRaceLapsSource: 'primaryLapsRemaining',
            raceLapsRemainingSource: 'primaryLapsRemaining',
            currentFuelCheckpoint: 'current',
            currentBurnBucketId: fuelV2BurnBucketId.last,
            futureFuelCheckpoint: 'serviceComplete',
            futureBurnBucketId: fuelV2BurnBucketId.fiveLapAverage
          },
          contractLabel: 'current→range; at-box→service; first-green/5L target; explicit current+future plan'
        }),
        fuelSharedSnapshotWorkbenchGridRow('Replay control / VLN 4h Mid S1', {
          lapBudget: {
            primaryLapsRemaining: 31,
            possibleLapsRemaining: 30.96,
            estimatedFinishLap: 31,
            canDriveFuelAdvice: true
          },
          checkpoints: {
            capacity: { physicalCapacityLiters: 104.94, driverCapPercent: 1, classCapPercent: 1 },
            measuredFirstGreenFuelLiters: 102.8464,
            currentFuelLiters: 61.64
          },
          burns: {
            [fuelV2BurnBucketId.last]: {
              value: 13.52,
              burnSource: 'LiveLastLap',
              sampleCount: 1,
              source: 'VLN 4h replay last lap'
            },
            [fuelV2BurnBucketId.fiveLapAverage]: {
              value: 13.5,
              burnSource: 'LiveFiveLapAverage',
              sampleCount: 5,
              source: 'VLN 4h replay 5L average'
            },
            [fuelV2BurnBucketId.tenLapAverage]: {
              value: 13.36,
              burnSource: 'LiveTenLapAverage',
              sampleCount: 10,
              source: 'VLN 4h replay 10L average'
            },
            [fuelV2BurnBucketId.maximum]: {
              value: 13.65,
              burnSource: 'LiveMaximum',
              sampleCount: 10,
              source: 'VLN 4h replay maximum'
            }
          },
          boundary: { targetLaps: null, reserveFuelLiters: 0, pitLaneFuelLiters: 0 },
          targetUsage: {
            fuelBudgetCheckpoint: 'firstGreen',
            referenceBurnBucketId: fuelV2BurnBucketId.last,
            targetLaps: [7, 8, 9]
          },
          plan: {
            mode: 'full-race',
            plannedRaceLapsSource: 'primaryLapsRemaining',
            raceLapsRemainingSource: 'possibleLapsRemaining',
            fuelBudgetCheckpoint: 'firstGreen',
            burnBucketId: fuelV2BurnBucketId.fiveLapAverage
          },
          contractLabel: 'Replay-backed accepted spans support full Last/5L/10L/Max; absent at-box evidence does not invent a pit request'
        }),
        fuelSharedSnapshotWorkbenchGridRow('Degraded / no implicit fallback', {
          lapBudget: { primaryLapsRemaining: 12, possibleLapsRemaining: 11.5, canDriveFuelAdvice: true },
          checkpoints: {
            capacity: { physicalCapacityLiters: 60, driverCapPercent: 1, classCapPercent: 1 },
            currentFuelLiters: 40
          },
          burns: {
            [fuelV2BurnBucketId.last]: 10
          },
          boundary: { targetLaps: null, reserveFuelLiters: 0, pitLaneFuelLiters: 0 },
          targetUsage: {
            fuelBudgetCheckpoint: 'firstGreen',
            referenceBurnBucketId: null,
            targetLaps: [4]
          },
          plan: {
            mode: 'full-race',
            plannedRaceLapsSource: 'primaryLapsRemaining',
            raceLapsRemainingSource: 'primaryLapsRemaining',
            fuelBudgetCheckpoint: 'effectiveCapacity',
            burnBucketId: fuelV2BurnBucketId.fiveLapAverage
          },
          contractLabel: 'Current and Last stay visible; missing FirstGreen, 5L, target, and Plan inputs stay missing'
        }),
        fuelSharedSnapshotWorkbenchGridRow('Unavailable / owner shells only', {
          lapBudget: { primaryLapsRemaining: null, possibleLapsRemaining: null },
          checkpoints: { capacity: {} },
          burns: {},
          boundary: { targetLaps: null, reserveFuelLiters: 0, pitLaneFuelLiters: 0 },
          targetUsage: {
            fuelBudgetCheckpoint: 'current',
            referenceBurnBucketId: fuelV2BurnBucketId.last,
            targetLaps: [1, 2, 3]
          },
          plan: null,
          contractLabel: 'Typed unavailable facts; no fabricated capacity, checkpoint, bucket, request, target, or plan'
        }),
        fuelSharedSnapshotWorkbenchGridRow('Missing capacity / diagnostic request only', {
          lapBudget: { primaryLapsRemaining: 2, possibleLapsRemaining: 2, canDriveFuelAdvice: true },
          checkpoints: {
            capacity: {},
            currentFuelLiters: 20,
            measuredAtBoxFuelLiters: 10
          },
          burns: { [fuelV2BurnBucketId.last]: 10 },
          boundary: { targetLaps: 2, reserveFuelLiters: 0, pitLaneFuelLiters: 0 },
          targetUsage: {
            fuelBudgetCheckpoint: 'current',
            referenceBurnBucketId: fuelV2BurnBucketId.last,
            targetLaps: [2]
          },
          plan: null,
          contractLabel: 'Range and desired add remain factual; missing capacity cannot claim clamp or feasibility'
        }),
        fuelSharedSnapshotWorkbenchGridRow('Unrequested projections / invalid upstreams stay unavailable', {
          lapBudget: { primaryLapsRemaining: 12, possibleLapsRemaining: 11.5, canDriveFuelAdvice: true },
          checkpoints: {
            capacity: { physicalCapacityLiters: -1, driverCapPercent: 1, classCapPercent: 1 },
            currentFuelLiters: -1
          },
          burns: { [fuelV2BurnBucketId.last]: 10 },
          boundary: { targetLaps: null, reserveFuelLiters: 0, pitLaneFuelLiters: 0 },
          targetUsage: {
            fuelBudgetCheckpoint: 'firstGreen',
            referenceBurnBucketId: fuelV2BurnBucketId.last,
            targetLaps: [4]
          },
          plan: {
            mode: 'full-race',
            plannedRaceLapsSource: 'primaryLapsRemaining',
            raceLapsRemainingSource: 'primaryLapsRemaining',
            fuelBudgetCheckpoint: 'expectedAtBox',
            burnBucketId: fuelV2BurnBucketId.last
          },
          contractLabel: 'No formation or current-to-box input: FirstGreen and AtBox remain Unavailable'
        }),
        fuelSharedSnapshotWorkbenchGridRow('Invalid capacity / diagnostic boundary only', {
          lapBudget: { primaryLapsRemaining: 2, possibleLapsRemaining: 2 },
          checkpoints: {
            capacity: { physicalCapacityLiters: -1, driverCapPercent: 1, classCapPercent: 1 },
            currentFuelLiters: 20,
            measuredAtBoxFuelLiters: 10
          },
          burns: {
            [fuelV2BurnBucketId.last]: 10
          },
          boundary: { targetLaps: 2, reserveFuelLiters: 0, pitLaneFuelLiters: 0 },
          targetUsage: {
            fuelBudgetCheckpoint: 'current',
            referenceBurnBucketId: fuelV2BurnBucketId.last,
            targetLaps: [2]
          },
          plan: null,
          contractLabel: 'Boundary retains invalid diagnostic math; Fuel To Add projection is suppressed like Core'
        }),
        fuelSharedSnapshotWorkbenchGridRow('Conflicted cap / blocked selected budget', {
          lapBudget: { primaryLapsRemaining: 12, possibleLapsRemaining: 11.5, canDriveFuelAdvice: true },
          checkpoints: {
            capacity: { physicalCapacityLiters: 75, driverCapPercent: 0.8, classCapPercent: 0.68 },
            currentFuelLiters: 20,
            measuredAtBoxFuelLiters: 10
          },
          burns: { [fuelV2BurnBucketId.last]: 10 },
          boundary: { targetLaps: 2, reserveFuelLiters: 0, pitLaneFuelLiters: 0 },
          targetUsage: {
            fuelBudgetCheckpoint: 'effectiveCapacity',
            referenceBurnBucketId: fuelV2BurnBucketId.last,
            targetLaps: [4]
          },
          plan: {
            mode: 'full-race',
            plannedRaceLapsSource: 'primaryLapsRemaining',
            raceLapsRemainingSource: 'primaryLapsRemaining',
            fuelBudgetCheckpoint: 'effectiveCapacity',
            burnBucketId: fuelV2BurnBucketId.last
          },
          contractLabel: 'Conflicted cap remains visible but cannot drive Target Usage or Plan'
        }),
        fuelSharedSnapshotWorkbenchGridRow('Invalid at-box / projected fallback blocked', {
          lapBudget: { primaryLapsRemaining: 12, possibleLapsRemaining: 11.5, canDriveFuelAdvice: true },
          checkpoints: {
            capacity: { physicalCapacityLiters: 60, driverCapPercent: 1, classCapPercent: 1 },
            currentFuelLiters: 20,
            measuredAtBoxFuelLiters: -1,
            expectedFuelToBoxLiters: 1
          },
          burns: { [fuelV2BurnBucketId.last]: 10 },
          boundary: { targetLaps: 2, reserveFuelLiters: 0, pitLaneFuelLiters: 0 },
          targetUsage: {
            fuelBudgetCheckpoint: 'expectedAtBox',
            referenceBurnBucketId: fuelV2BurnBucketId.last,
            targetLaps: [2]
          },
          plan: {
            mode: 'current-checkpoint',
            plannedRaceLapsSource: 'primaryLapsRemaining',
            raceLapsRemainingSource: 'primaryLapsRemaining',
            currentFuelCheckpoint: 'current',
            currentBurnBucketId: fuelV2BurnBucketId.last,
            futureFuelCheckpoint: 'expectedAtBox',
            futureBurnBucketId: fuelV2BurnBucketId.last
          },
          contractLabel: 'Projected 19 L fact is retained; invalid measured at-box dependency blocks derived use'
        }),
        fuelSharedSnapshotWorkbenchGridRow('Invalid descendant chain / provided projections blocked', {
          lapBudget: { primaryLapsRemaining: 12, possibleLapsRemaining: 11.5, canDriveFuelAdvice: true },
          checkpoints: {
            capacity: { physicalCapacityLiters: 60, driverCapPercent: 1, classCapPercent: 1 },
            currentFuelLiters: -1,
            expectedFuelToBoxLiters: 1,
            plannedServiceAddLiters: 10,
            expectedBoxToPitExitFuelLiters: 0.5
          },
          burns: { [fuelV2BurnBucketId.last]: 10 },
          boundary: { targetLaps: 2, reserveFuelLiters: 0, pitLaneFuelLiters: 0 },
          targetUsage: {
            fuelBudgetCheckpoint: 'serviceComplete',
            referenceBurnBucketId: fuelV2BurnBucketId.last,
            targetLaps: [2]
          },
          plan: {
            mode: 'full-race',
            plannedRaceLapsSource: 'primaryLapsRemaining',
            raceLapsRemainingSource: 'primaryLapsRemaining',
            fuelBudgetCheckpoint: 'expectedPitExit',
            burnBucketId: fuelV2BurnBucketId.last
          },
          contractLabel: 'Provided service and pit-exit projections retain the upstream Invalid state'
        }),
        fuelSharedSnapshotWorkbenchGridRow('Clamped projection / conflicted zero', {
          lapBudget: { primaryLapsRemaining: 2, possibleLapsRemaining: 2, canDriveFuelAdvice: true },
          checkpoints: {
            capacity: { physicalCapacityLiters: 60, driverCapPercent: 1, classCapPercent: 1 },
            currentFuelLiters: 1,
            expectedFuelToBoxLiters: 2
          },
          burns: { [fuelV2BurnBucketId.last]: 10 },
          boundary: { targetLaps: 2, reserveFuelLiters: 0, pitLaneFuelLiters: 0 },
          targetUsage: {
            fuelBudgetCheckpoint: 'expectedAtBox',
            referenceBurnBucketId: fuelV2BurnBucketId.last,
            targetLaps: [2]
          },
          plan: {
            mode: 'full-race',
            plannedRaceLapsSource: 'primaryLapsRemaining',
            raceLapsRemainingSource: 'primaryLapsRemaining',
            fuelBudgetCheckpoint: 'expectedAtBox',
            burnBucketId: fuelV2BurnBucketId.last
          },
          contractLabel: 'Projection-clamped zero is conflicted, not a clean zero budget'
        }),
        fuelSharedSnapshotWorkbenchGridRow('Known zero / factual selection', {
          lapBudget: { primaryLapsRemaining: 2, possibleLapsRemaining: 2, canDriveFuelAdvice: true },
          checkpoints: {
            capacity: { physicalCapacityLiters: 60, driverCapPercent: 1, classCapPercent: 1 },
            currentFuelLiters: 0,
            measuredAtBoxFuelLiters: 0
          },
          burns: { [fuelV2BurnBucketId.last]: 10 },
          boundary: { targetLaps: 2, reserveFuelLiters: 0, pitLaneFuelLiters: 0 },
          targetUsage: {
            fuelBudgetCheckpoint: 'current',
            referenceBurnBucketId: fuelV2BurnBucketId.last,
            targetLaps: [2]
          },
          plan: {
            mode: 'full-race',
            plannedRaceLapsSource: 'primaryLapsRemaining',
            raceLapsRemainingSource: 'primaryLapsRemaining',
            fuelBudgetCheckpoint: 'current',
            burnBucketId: fuelV2BurnBucketId.last
          },
          contractLabel: 'Clean zero stays selectable; Target Usage still requires a positive budget and Plan retains zero'
        }),
        fuelSharedSnapshotWorkbenchGridRow('Seed-only / retained without implicit plan', {
          lapBudget: { primaryLapsRemaining: 5, possibleLapsRemaining: 5, canDriveFuelAdvice: false },
          checkpoints: {
            capacity: { physicalCapacityLiters: 60, driverCapPercent: 1, classCapPercent: 1 },
            measuredFirstGreenFuelLiters: 60,
            currentFuelLiters: 24,
            measuredAtBoxFuelLiters: 12
          },
          burns: {
            [fuelV2BurnBucketId.qualifying]: {
              value: 12,
              burnSource: 'QualifyingSeed',
              sampleCount: 1,
              confidence: 'Seeded',
              strategyEligible: false,
              cleanBaselineEligible: false,
              source: 'qualifying replay seed'
            }
          },
          boundary: { targetLaps: 4, reserveFuelLiters: 0, pitLaneFuelLiters: 0 },
          targetUsage: {
            fuelBudgetCheckpoint: 'firstGreen',
            referenceBurnBucketId: fuelV2BurnBucketId.qualifying,
            targetLaps: [4, 5]
          },
          plan: null,
          contractLabel: 'Quali remains typed contextual evidence; no Last bucket or Plan selection is invented'
        })
      ]
    }
  ];
  const stintTargetV1Rows = [
    metricRow('Dallara 35m / V1 Stint 1', `2 laps | target ${formatFuelPerLap(12.73)}`, 'info', [
      metricSegment('Laps', '2 laps', 'info'),
      metricSegment('Target', formatFuelPerLap(12.73), 'info'),
      metricSegment('Save', 'None', 'success')
    ]),
    metricRow('Dallara 35m / V1 Stint 2', `2 laps | target ${formatFuelPerLap(12.73)}`, 'info', [
      metricSegment('Laps', '2 laps', 'info'),
      metricSegment('Target', formatFuelPerLap(12.73), 'info'),
      metricSegment('Save', 'None', 'success')
    ]),
    metricRow('Dallara 35m / V1 no-stop', '4 laps | not surfaced', 'warning', [
      metricSegment('Laps', '4 laps', 'info'),
      metricSegment('Target', '--', 'waiting'),
      metricSegment('Save', 'Not shown', 'warning')
    ])
  ];
  const stintTargetV2GridSections = [
    {
      title: 'Stint Targets V2 - Current Tank',
      headers: ['Scenario', 'To go', 'Tank', 'Short', 'Plan', 'Stretch', 'Extra', 'Live', 'Status'],
      rows: [
        fuelStintTargetV2WorkbenchGridRow('Dallara 35m start / History baseline', {
          remainingLaps: 4,
          currentFuelLiters: 49.6766,
          referenceBurnLitersPerLap: { value: 13.50, label: 'history' },
          targetLaps: 3,
          planLabel: 'sensible plan'
        }),
        fuelStintTargetV2WorkbenchGridRow('Dallara 35m start / First-stint actual', {
          remainingLaps: 4,
          currentFuelLiters: 49.6766,
          referenceBurnLitersPerLap: { value: 12.6250, label: 'actual' },
          targetLaps: 3,
          planLabel: 'hindsight trend'
        }),
        fuelStintTargetV2WorkbenchGridRow('Dallara 35m start / Low-burn hindsight', {
          remainingLaps: 4,
          currentFuelLiters: 49.6766,
          referenceBurnLitersPerLap: { value: 12.3421, label: 'hindsight' },
          targetLaps: 3,
          planLabel: 'not start-known'
        }),
        fuelStintTargetV2WorkbenchGridRow('Dallara 35m half / Captured', {
          remainingLaps: 2,
          currentFuelLiters: 26.2104,
          referenceBurnLitersPerLap: 12.3634,
          targetLaps: 2,
          planLabel: 'finish now'
        }),
        fuelStintTargetV2WorkbenchGridRow('Dallara 35m stop / Captured', {
          remainingLaps: 2,
          currentFuelLiters: 15.27,
          referenceBurnLitersPerLap: { value: 12.9407, label: 'max' },
          targetLaps: 2,
          planLabel: 'one-lap short'
        }),
        fuelStintTargetV2WorkbenchGridRow('VLN 4h team / Mid S1', {
          remainingLaps: 30.4,
          currentFuelLiters: 61.64,
          referenceBurnLitersPerLap: 13.5176,
          targetLaps: 5,
          planLabel: 'current 5-lap stretch'
        }),
        fuelStintTargetV2WorkbenchGridRow('Dallara 45m / Stop 1 finish', {
          remainingLaps: 2,
          currentFuelLiters: 33.2921,
          referenceBurnLitersPerLap: 13.7571,
          targetLaps: 2,
          planLabel: 'finish now'
        }),
        fuelStintTargetV2WorkbenchGridRow('Dallara 45m / Half Rem', {
          remainingLaps: 3,
          currentFuelLiters: 20.36,
          referenceBurnLitersPerLap: 13.7568,
          targetLaps: 2,
          planLabel: 'stretch before stop'
        }),
        fuelStintTargetV2WorkbenchGridRow('Dallara 4L blip / Repair edge', {
          remainingLaps: 2,
          currentFuelLiters: 18.86,
          referenceBurnLitersPerLap: { value: 13.5683, label: 'repair' },
          targetLaps: 2,
          planLabel: 'repair context',
          flags: ['repair']
        }),
        fuelStintTargetV2WorkbenchGridRow('Mock 24h GT3 / Full handoff', {
          remainingLaps: 7,
          currentFuelLiters: 103.5,
          reserveFuelLiters: 1.0,
          referenceBurnLitersPerLap: { value: 14.21, label: 'history' },
          targetLaps: 7,
          planLabel: 'teammate stint'
        }),
        fuelStintTargetV2WorkbenchGridRow('Mock NASCAR / 10 to go caution', {
          remainingLaps: 10,
          currentFuelLiters: 12.0,
          referenceBurnLitersPerLap: { value: 5.0, label: 'green' },
          targetLaps: 3,
          planLabel: 'condition mix?',
          flags: ['condition']
        })
      ]
    },
    {
      title: 'Stint Targets V2 - Stress Cases',
      headers: ['Scenario', 'To go', 'Tank', 'Short', 'Plan', 'Stretch', 'Extra', 'Live', 'Status'],
      rows: [
        fuelStintTargetV2WorkbenchGridRow('Stress / Exact edge', {
          remainingLaps: 4,
          currentFuelLiters: 54.0,
          referenceBurnLitersPerLap: 13.5,
          targetLaps: 4,
          planLabel: 'edge target'
        }),
        fuelStintTargetV2WorkbenchGridRow('Stress / Reserve flips Dallara', {
          remainingLaps: 4,
          currentFuelLiters: 49.6766,
          reserveFuelLiters: 2.0,
          referenceBurnLitersPerLap: { value: 13.50, label: 'history' },
          targetLaps: 4,
          planLabel: '2L reserve'
        }),
        fuelStintTargetV2WorkbenchGridRow('Stress / One lap too many', {
          remainingLaps: 3,
          currentFuelLiters: 20.0,
          referenceBurnLitersPerLap: 13.5,
          targetLaps: 2,
          planLabel: 'do not stretch'
        }),
        fuelStintTargetV2WorkbenchGridRow('Stress / Absurd N+1', {
          remainingLaps: 8,
          currentFuelLiters: 28.0,
          referenceBurnLitersPerLap: 14.0,
          targetLaps: 3,
          planLabel: 'guardrail'
        }),
        fuelStintTargetV2WorkbenchGridRow('Stress / Stretch costs more than stop', {
          remainingLaps: 4,
          currentFuelLiters: 50.0,
          referenceBurnLitersPerLap: { value: 13.0, label: 'green' },
          targetLaps: 3,
          planLabel: 'pace vs stop',
          targetTimeContexts: {
            4: { stopAvoidanceSeconds: 35, paceLossSeconds: 48 }
          }
        }),
        fuelStintTargetV2WorkbenchGridRow('Stress / +2 stretch plausible', {
          remainingLaps: 7,
          currentFuelLiters: 78.0,
          referenceBurnLitersPerLap: { value: 12.5, label: 'history' },
          targetLaps: 5,
          planLabel: 'multi-lap stretch',
          targetTimeContexts: {
            6: { stopAvoidanceSeconds: 60, paceLossSeconds: 20 },
            7: { stopAvoidanceSeconds: 60, paceLossSeconds: 55 }
          }
        }),
        fuelStintTargetV2WorkbenchGridRow('Stress / Caution-only burn', {
          remainingLaps: 10,
          currentFuelLiters: 12.0,
          referenceBurnLitersPerLap: { value: 1.0, label: 'caution' },
          targetLaps: 10,
          planLabel: 'yellow fuel only',
          flags: ['condition']
        }),
        fuelStintTargetV2WorkbenchGridRow('Stress / No live burn', {
          remainingLaps: 4,
          currentFuelLiters: 49.0,
          referenceBurnLitersPerLap: null,
          targetLaps: 4,
          planLabel: 'learning'
        }),
        fuelStintTargetV2WorkbenchGridRow('Stress / Known zero fuel', {
          remainingLaps: 4,
          currentFuelLiters: 0,
          referenceBurnLitersPerLap: 10.0,
          targetLaps: 2,
          planLabel: 'zero is factual'
        }),
        fuelStintTargetV2WorkbenchGridRow('Stress / Race finished', {
          remainingLaps: 0,
          currentFuelLiters: 50.0,
          referenceBurnLitersPerLap: 10.0,
          targetLaps: 5,
          planLabel: 'no target after finish'
        }),
        fuelStintTargetV2WorkbenchGridRow('Stress / Save threshold 92', {
          remainingLaps: 3,
          currentFuelLiters: 9.20,
          referenceBurnLitersPerLap: 10.0,
          targetLaps: 1,
          planLabel: 'boundary'
        }),
        fuelStintTargetV2WorkbenchGridRow('Stress / Save threshold below 92', {
          remainingLaps: 3,
          currentFuelLiters: 9.19,
          referenceBurnLitersPerLap: 10.0,
          targetLaps: 1,
          planLabel: 'worse boundary'
        }),
        fuelStintTargetV2WorkbenchGridRow('Stress / Held tracking', {
          remainingLaps: 3,
          currentFuelLiters: 10.0,
          referenceBurnLitersPerLap: 10.0,
          targetLaps: 1,
          planLabel: 'held context',
          flags: ['held']
        }),
        fuelStintTargetV2WorkbenchGridRow('Stress / Explicit null telemetry', {
          remainingLaps: null,
          currentFuelLiters: null,
          referenceBurnLitersPerLap: 10.0,
          targetLaps: 2,
          planLabel: 'null stays unavailable',
          flags: ['held']
        })
      ]
    }
  ];
  const planV1Rows = [
    fuelPlanWorkbenchRow('VLN 4h team / Mid S1', '31 laps | 5 stints | 4 stops', 'info', {
      race: '31 laps',
      remain: '30.4',
      stints: '5',
      stops: '4',
      save: 'None'
    }),
    fuelPlanWorkbenchRow('24h rejoin / Mid S1', '173 laps | 25 stints | 24 stops', 'info', {
      race: '173 laps',
      remain: '173.3',
      stints: '25',
      stops: '24',
      save: 'None'
    }),
    fuelPlanWorkbenchRow('24h rejoin / Stop 1 degraded', '166 laps | 24 stints | 23 stops', 'warning', {
      race: '166 laps',
      remain: '166.0 degraded',
      stints: '24',
      stops: '23',
      save: 'None',
      saveTone: 'warning'
    }),
    fuelPlanWorkbenchRow('Dallara 45m / Half Rem', '6 laps | 2 stints | 1 stop', 'info', {
      race: '6 laps',
      remain: '3.0',
      stints: '2',
      stops: '1',
      save: 'None'
    }),
    fuelPlanWorkbenchRow('Dallara 4L full / Stop 1', '4 laps | 2 stints | 1 stop', 'info', {
      race: '4 laps',
      remain: '2.0',
      stints: '2',
      stops: '1',
      save: 'None'
    }),
    fuelPlanWorkbenchRow('Dallara 4L blip / Repair edge', '4 laps | 2 stints | 1 stop', 'warning', {
      race: '4 laps',
      remain: '2.0',
      stints: '2',
      stops: '1',
      save: 'None',
      saveTone: 'warning'
    })
  ];
  const planV2GridSections = [
    {
      title: 'Plan V2 - Green Start / Full Race',
      headers: ['Scenario', 'Race', 'Start cap', 'Rhythm', 'Stops', 'Final'],
      rows: [
        fuelPlanV2StartWorkbenchGridRow('VLN 4h team / Green start', {
          raceLaps: 31,
          remainLaps: 30.4,
          usableFuelLiters: 102.8464,
          burnLitersPerLap: 13.5176
        }),
        fuelPlanV2StartWorkbenchGridRow('24h rejoin / History start', {
          raceLaps: 173,
          remainLaps: 173.3,
          usableFuelLiters: 104.9,
          burnLitersPerLap: { value: 14.2142, label: 'history' }
        }),
        fuelPlanV2StartWorkbenchGridRow('24h rejoin / Degraded held', {
          raceLaps: 173,
          remainLaps: 173.1,
          usableFuelLiters: 104.9,
          burnLitersPerLap: { value: 14.2142, label: 'history' },
          flags: ['held', 'degraded']
        }),
        fuelPlanV2StartWorkbenchGridRow('Dallara 45m / Green start', {
          raceLaps: 6,
          remainLaps: 6,
          usableFuelLiters: 58.9871,
          burnLitersPerLap: 13.7593
        }),
        fuelPlanV2StartWorkbenchGridRow('Dallara 4L full / Green start', {
          raceLaps: 4,
          remainLaps: 4,
          usableFuelLiters: 50.1023,
          burnLitersPerLap: 12.3421
        }),
        fuelPlanV2StartWorkbenchGridRow('Dallara 4L blip / Green start', {
          raceLaps: 4,
          remainLaps: 4,
          usableFuelLiters: 50.0,
          burnLitersPerLap: { value: 13.5683, label: 'repair' },
          flags: ['repair']
        }),
        fuelPlanV2StartWorkbenchGridRow('GR86 3L / Fixed start', {
          raceLaps: 3,
          remainLaps: 3,
          usableFuelLiters: 82.85,
          burnLitersPerLap: { value: 5.4618, label: 'partial' }
        }),
        fuelPlanV2StartWorkbenchGridRow('NASCAR Ford 50L / Capture start', {
          raceLaps: 50,
          remainLaps: 50,
          usableFuelLiters: 75.672,
          burnLitersPerLap: { value: 1.1004, label: 'green' },
          flags: ['condition']
        }),
        fuelPlanV2StartWorkbenchGridRow('Stress / Sub-lap full budget', {
          raceLaps: 2,
          remainLaps: 2,
          usableFuelLiters: 9.9,
          burnLitersPerLap: 10.0
        }),
        fuelPlanV2StartWorkbenchGridRow('Stress / Known zero full budget', {
          raceLaps: 2,
          remainLaps: 2,
          usableFuelLiters: 0,
          burnLitersPerLap: 10.0
        }),
        fuelPlanV2StartWorkbenchGridRow('Stress / Explicit null full budget', {
          raceLaps: 2,
          remainLaps: 2,
          usableFuelLiters: null,
          burnLitersPerLap: 10.0
        })
      ]
    },
    {
      title: 'Plan V2 - Current Checkpoint / From Here',
      headers: ['Scenario', 'Total', 'To go', 'Now/Full', 'Rhythm from now', 'Stops now', 'Final'],
      rows: [
        fuelPlanV2CheckpointWorkbenchGridRow('VLN 4h team / Mid S1', {
          raceLaps: 31,
          remainLaps: 30.4,
          currentFuelLiters: 61.64,
          futureFuelLiters: 102.8464,
          burnLitersPerLap: 13.5176
        }),
        fuelPlanV2CheckpointWorkbenchGridRow('24h rejoin / Current fuel n/a', {
          raceLaps: 173,
          remainLaps: 173.3,
          futureFuelLiters: 104.9,
          futureBurnLitersPerLap: { value: 14.2142, label: 'history' },
          flags: ['degraded']
        }),
        fuelPlanV2CheckpointWorkbenchGridRow('Dallara 45m / Stop 1 finish', {
          raceLaps: 6,
          remainLaps: 2,
          currentFuelLiters: 33.2921,
          futureFuelLiters: 58.9871,
          currentBurnLitersPerLap: 13.7571,
          futureBurnLitersPerLap: 13.7593
        }),
        fuelPlanV2CheckpointWorkbenchGridRow('Dallara 45m / Half Rem', {
          raceLaps: 6,
          remainLaps: 3,
          currentFuelLiters: 20.36,
          futureFuelLiters: 58.9871,
          currentBurnLitersPerLap: 13.7593,
          futureBurnLitersPerLap: 13.7593
        }),
        fuelPlanV2CheckpointWorkbenchGridRow('Dallara 4L full / Half', {
          raceLaps: 4,
          remainLaps: 2,
          currentFuelLiters: 26.2104,
          futureFuelLiters: 50.1023,
          currentBurnLitersPerLap: 12.3634,
          futureBurnLitersPerLap: 12.3421
        }),
        fuelPlanV2CheckpointWorkbenchGridRow('Dallara 4L full / Stop 1', {
          raceLaps: 4,
          remainLaps: 2,
          currentFuelLiters: 15.27,
          futureFuelLiters: 50.1023,
          currentBurnLitersPerLap: { value: 12.9407, label: 'max' },
          futureBurnLitersPerLap: 12.3421
        }),
        fuelPlanV2CheckpointWorkbenchGridRow('Dallara 4L blip / Repair edge', {
          raceLaps: 4,
          remainLaps: 2,
          currentFuelLiters: 18.86,
          futureFuelLiters: 50.0,
          currentBurnLitersPerLap: { value: 13.5683, label: 'repair' },
          futureBurnLitersPerLap: { value: 13.5683, label: 'repair' },
          flags: ['repair']
        }),
        fuelPlanV2CheckpointWorkbenchGridRow('Mock NASCAR / 10 to go caution', {
          raceLaps: 50,
          remainLaps: 10,
          currentFuelLiters: 12.0,
          futureFuelLiters: 75.672,
          burnLitersPerLap: { value: 5.0, label: 'green' },
          flags: ['condition'],
          finalLabel: 'condition mix?'
        }),
        fuelPlanV2CheckpointWorkbenchGridRow('Stress / Sub-lap future budget', {
          raceLaps: 2,
          remainLaps: 2,
          currentFuelLiters: 9.9,
          futureFuelLiters: 9.9,
          currentBurnLitersPerLap: 10.0,
          futureBurnLitersPerLap: 10.0
        }),
        fuelPlanV2CheckpointWorkbenchGridRow('Stress / Known zero checkpoint fuel', {
          raceLaps: 2,
          remainLaps: 2,
          currentFuelLiters: 0,
          futureFuelLiters: 0,
          currentBurnLitersPerLap: 10.0,
          futureBurnLitersPerLap: 10.0
        }),
        fuelPlanV2CheckpointWorkbenchGridRow('Stress / Explicit null checkpoint fuel', {
          raceLaps: 2,
          remainLaps: 2,
          currentFuelLiters: null,
          futureFuelLiters: null,
          currentBurnLitersPerLap: 10.0,
          futureBurnLitersPerLap: 10.0
        })
      ]
    }
  ];
  const pitRequestRows = [
    fuelPitRequestWorkbenchRow('VLN 4h team / 7-lap stint', '61.6 L / 7 laps / no reserve', 'info', {
      currentFuel: 61.64,
      tankCapacity: 104.94,
      targetLaps: 7,
      maxBurn: { value: 13.6372, label: 'seed' },
      sectorBurn: { value: 13.0838, label: 'sector' },
      lastBurn: 13.5175,
      fiveBurn: 13.4880,
      tenBurn: 13.3709,
      v1Burn: 13.3709
    }),
    fuelPitRequestWorkbenchRow('24h rejoin / No local fuel', 'fuel n/a / 7 laps / history fallback', 'warning', {
      currentFuel: null,
      tankCapacity: 104.9,
      targetLaps: 7,
      maxBurn: { value: 14.2142, label: 'history' },
      sectorBurn: { value: 14.2142, label: 'history' },
      lastBurn: null,
      fiveBurn: null,
      tenBurn: null,
      v1Burn: null
    }),
    fuelPitRequestWorkbenchRow('Dallara 45m / 3-lap finish', '33.3 L / 3 laps / no reserve', 'info', {
      currentFuel: 33.2921,
      tankCapacity: 60.0,
      targetLaps: 3,
      maxBurn: { value: 13.8141, label: 'seed' },
      sectorBurn: { value: 13.8978, label: 'sector' },
      lastBurn: 13.7571,
      fiveBurn: { value: 13.6443, label: '3/5', sampleCount: 3, strategyEligible: false, cleanBaselineEligible: false, confidence: 'Contextual' },
      tenBurn: null,
      v1Burn: 13.6443
    }),
    fuelPitRequestWorkbenchRow('Dallara 45m / 2-lap edge', '20.4 L / 2 laps / no reserve', 'info', {
      currentFuel: 20.36,
      tankCapacity: 60.0,
      targetLaps: 2,
      maxBurn: { value: 13.7568, label: 'seed' },
      sectorBurn: { value: 13.8978, label: 'sector' },
      lastBurn: 13.7568,
      fiveBurn: { value: 13.6644, label: '3/5', sampleCount: 3, strategyEligible: false, cleanBaselineEligible: false, confidence: 'Contextual' },
      tenBurn: null,
      v1Burn: 13.5733
    }),
    fuelPitRequestWorkbenchRow('Dallara 4L full / 2-lap finish', '15.3 L / 2 laps / no reserve', 'info', {
      currentFuel: 15.27,
      tankCapacity: 51.0,
      targetLaps: 2,
      maxBurn: { value: 12.9407, label: 'seed' },
      sectorBurn: { value: 12.4393, label: 'sector' },
      lastBurn: 12.3145,
      fiveBurn: { value: 12.6198, label: '3/5', sampleCount: 3, strategyEligible: false, cleanBaselineEligible: false, confidence: 'Contextual' },
      tenBurn: null,
      v1Burn: 12.7250
    }),
    fuelPitRequestWorkbenchRow('Dallara 4L blip / Repair edge', '18.9 L / 2 laps / abnormal', 'warning', {
      currentFuel: 18.86,
      tankCapacity: 51.0,
      targetLaps: 2,
      maxBurn: { value: 13.5683, label: 'seed' },
      sectorBurn: { value: 13.3491, label: 'sector' },
      lastBurn: 13.5683,
      fiveBurn: null,
      tenBurn: null,
      v1Burn: 13.3759
    }),
    fuelPitRequestWorkbenchRow('Dallara quali seed / 4-lap target', '0.0 L / 4 laps / stress', 'warning', {
      currentFuel: 0.0,
      tankCapacity: 51.0,
      targetLaps: 4,
      maxBurn: { value: 13.7982, label: 'quali', burnSource: 'QualifyingSeed', sampleCount: 1, strategyEligible: false, cleanBaselineEligible: false, confidence: 'Seeded' },
      qualiBurn: { value: 13.7982, label: 'quali', burnSource: 'QualifyingSeed', sampleCount: 1, strategyEligible: false, cleanBaselineEligible: false, confidence: 'Seeded' },
      sectorBurn: { value: 13.7982, label: 'quali' },
      lastBurn: null,
      fiveBurn: null,
      tenBurn: null,
      v1Burn: null
    }),
    fuelPitRequestWorkbenchRow('Stress / Invalid negative reserve', '20.0 L / invalid adjustment', 'error', {
      currentFuel: 20.0,
      tankCapacity: 60.0,
      targetLaps: 2,
      reserveFuel: -1.0,
      lastBurn: 10.0,
      fiveBurn: 10.0,
      tenBurn: 10.0,
      maxBurn: 10.0,
      minBurn: 10.0,
      qualiBurn: 10.0,
      v1Burn: null
    }),
    fuelPitRequestWorkbenchRow('Stress / Invalid zero capacity', '10.0 L / invalid capacity', 'error', {
      currentFuel: 10.0,
      tankCapacity: 0.0,
      targetLaps: 2,
      lastBurn: 10.0,
      v1Burn: null
    }),
    fuelPitRequestWorkbenchRow('Stress / No explicit extrema', '0.0 L / 1 lap / explicit buckets', 'warning', {
      currentFuel: 0.0,
      tankCapacity: 100.0,
      targetLaps: 1,
      sectorBurn: { value: 20.0, label: 'not a bucket' },
      lastBurn: 10.0,
      fiveBurn: 9.0,
      tenBurn: 8.0,
      v1Burn: null
    })
  ];
  const pitRequestMockRows = [
    fuelPitRequestWorkbenchRow('Mock GT3 6h / Stop 1 full stint', '18.0 L / 7 laps / stop 1 of 3', 'modeled', {
      currentFuel: 18.0,
      tankCapacity: 104.9,
      targetLaps: 7,
      maxBurn: { value: 13.9, label: 'seed' },
      sectorBurn: { value: 13.6, label: 'sector' },
      lastBurn: 13.50,
      fiveBurn: 13.45,
      tenBurn: 13.35,
      v1Burn: 13.55
    }),
    fuelPitRequestWorkbenchRow('Mock GT3 6h / Stop 2 reserve', '41.0 L / 5 laps / 2.7 L margin', 'modeled', {
      currentFuel: 41.0,
      tankCapacity: 104.9,
      targetLaps: 5,
      reserveFuel: 2.0,
      pitLaneFuel: 0.7,
      maxBurn: { value: 13.7, label: 'seed' },
      sectorBurn: { value: 13.3, label: 'sector' },
      lastBurn: 13.45,
      fiveBurn: 13.38,
      tenBurn: 13.32,
      v1Burn: 13.50
    }),
    fuelPitRequestWorkbenchRow('Mock GT3 6h / Splash finish', '37.5 L / 3 laps / final stop', 'modeled', {
      currentFuel: 37.5,
      tankCapacity: 104.9,
      targetLaps: 3,
      maxBurn: { value: 13.8, label: 'seed' },
      sectorBurn: { value: 13.4, label: 'sector' },
      lastBurn: 13.35,
      fiveBurn: 13.42,
      tenBurn: 13.48,
      v1Burn: 13.50
    }),
    fuelPitRequestWorkbenchRow('Mock GT3 6h / 8-lap cap edge', '25.0 L / 8 laps / impossible', 'warning', {
      currentFuel: 25.0,
      tankCapacity: 104.9,
      targetLaps: 8,
      maxBurn: { value: 13.9, label: 'seed' },
      sectorBurn: { value: 13.7, label: 'sector' },
      lastBurn: 13.60,
      fiveBurn: 13.50,
      tenBurn: 13.40,
      v1Burn: 13.55
    }),
    fuelPitRequestWorkbenchRow('Mock GT3 6h / Sector spike', '33.0 L / 5 laps / live burn high', 'warning', {
      currentFuel: 33.0,
      tankCapacity: 104.9,
      targetLaps: 5,
      maxBurn: { value: 13.5, label: 'seed' },
      sectorBurn: { value: 14.2, label: 'sector' },
      lastBurn: 13.45,
      fiveBurn: 13.40,
      tenBurn: 13.35,
      v1Burn: 13.40
    }),
    fuelPitRequestWorkbenchRow('Mock 24h GT3 / Full handoff', '103.5 L / 7 laps / leave full', 'modeled', {
      currentFuel: 103.5,
      tankCapacity: 104.9,
      targetLaps: 7,
      reserveFuel: 1.0,
      maxBurn: { value: 14.21, label: 'history' },
      sectorBurn: { value: 14.05, label: 'sector' },
      lastBurn: 14.10,
      fiveBurn: 14.18,
      tenBurn: 14.25,
      v1Burn: 14.20
    }),
    fuelPitRequestWorkbenchRow('Mock 24h GT3 / Short fill', '52.0 L / 4 laps / teammate stint', 'modeled', {
      currentFuel: 52.0,
      tankCapacity: 104.9,
      targetLaps: 4,
      reserveFuel: 1.5,
      pitLaneFuel: 0.8,
      maxBurn: { value: 14.21, label: 'history' },
      sectorBurn: { value: 13.95, label: 'sector' },
      lastBurn: 14.05,
      fiveBurn: 14.18,
      tenBurn: null,
      v1Burn: 14.20
    }),
    fuelPitRequestWorkbenchRow('Mock 24h GT3 / Spotter handoff', 'fuel n/a / 7 laps / remote car', 'warning', {
      currentFuel: null,
      tankCapacity: 104.9,
      targetLaps: 7,
      maxBurn: { value: 14.21, label: 'history' },
      sectorBurn: { value: 14.05, label: 'bridge' },
      lastBurn: null,
      fiveBurn: null,
      tenBurn: null,
      v1Burn: null
    }),
    fuelPitRequestWorkbenchRow('Mock Bridge / Teammate packets', '58.4 L / 5 laps / sector+lap packets', 'modeled', {
      currentFuel: 58.4,
      tankCapacity: 104.9,
      targetLaps: 5,
      reserveFuel: 1.0,
      pitLaneFuel: 0.6,
      maxBurn: { value: 14.21, label: 'history' },
      sectorBurn: { value: 14.60, label: 'bridge' },
      lastBurn: { value: 14.10, label: 'bridge lap', burnSource: 'HistoricalSeed', sampleCount: 1, strategyEligible: false, cleanBaselineEligible: false, confidence: 'Contextual' },
      fiveBurn: { value: 14.18, label: 'bridge 5L', burnSource: 'HistoricalSeed', sampleCount: 5, strategyEligible: false, cleanBaselineEligible: false, confidence: 'Contextual' },
      tenBurn: null,
      v1Burn: null
    }),
    fuelPitRequestWorkbenchRow('Mock NASCAR / Repair caution', '72.1 L / 68 laps / oval yellow', 'warning', {
      currentFuel: 72.1,
      tankCapacity: 75.7,
      targetLaps: 68,
      reserveFuel: 0.5,
      maxBurn: { value: 1.12, label: 'seed' },
      sectorBurn: { value: 0.86, label: 'caution' },
      lastBurn: 1.07,
      fiveBurn: 1.04,
      tenBurn: 1.02,
      minBurn: { value: 0.86, label: 'caution', burnSource: 'HistoricalSeed', strategyEligible: false, cleanBaselineEligible: false, confidence: 'Contextual' },
      v1Burn: 1.08
    }),
    fuelPitRequestWorkbenchRow('Mock NASCAR / 3 green + 2 caution', '72.1 L / 68 laps / 1.0L caution', 'warning', {
      currentFuel: 72.1,
      tankCapacity: 75.7,
      targetLaps: 68,
      reserveFuel: 0.5,
      maxBurn: { value: 1.12, label: 'seed' },
      sectorBurn: { value: 1.0, label: 'caution' },
      lastBurn: { value: 1.0, label: 'caution', burnSource: 'HistoricalSeed', sampleCount: 1, strategyEligible: false, cleanBaselineEligible: false, confidence: 'Contextual' },
      fiveBurn: { value: 1.046, label: '3G+2Y', burnSource: 'HistoricalSeed', sampleCount: 5, strategyEligible: false, cleanBaselineEligible: false, confidence: 'Contextual' },
      tenBurn: null,
      minBurn: { value: 1.0, label: 'caution', burnSource: 'HistoricalSeed', strategyEligible: false, cleanBaselineEligible: false, confidence: 'Contextual' },
      v1Burn: 1.08
    }),
    fuelPitRequestWorkbenchRow('Mock NASCAR / 5L green + 1L caution', '72.1 L / 68 laps / stress mix', 'warning', {
      currentFuel: 72.1,
      tankCapacity: 75.7,
      targetLaps: 68,
      reserveFuel: 0.5,
      maxBurn: { value: 5.0, label: 'green' },
      sectorBurn: { value: 1.0, label: 'caution' },
      lastBurn: { value: 1.0, label: 'caution', burnSource: 'HistoricalSeed', sampleCount: 1, strategyEligible: false, cleanBaselineEligible: false, confidence: 'Contextual' },
      fiveBurn: { value: 3.4, label: '3G+2Y', burnSource: 'HistoricalSeed', sampleCount: 5, strategyEligible: false, cleanBaselineEligible: false, confidence: 'Contextual' },
      tenBurn: null,
      minBurn: { value: 1.0, label: 'caution', burnSource: 'HistoricalSeed', strategyEligible: false, cleanBaselineEligible: false, confidence: 'Contextual' },
      v1Burn: 5.0
    }),
    fuelPitRequestWorkbenchRow('Mock GR86 Nord / 16-lap edge', '81.1 L / 16 laps / cap pressure', 'warning', {
      currentFuel: 81.1,
      tankCapacity: 83.3,
      targetLaps: 16,
      maxBurn: { value: 5.46, label: 'history' },
      sectorBurn: { value: 5.30, label: 'sector' },
      lastBurn: 5.21,
      fiveBurn: null,
      tenBurn: null,
      v1Burn: 5.46
    }),
    fuelPitRequestWorkbenchRow('Mock Porsche Cup Spa / Tiny top-up', '45.0 L / 13 laps / history', 'modeled', {
      currentFuel: 45.0,
      tankCapacity: 67.0,
      targetLaps: 13,
      maxBurn: { value: 3.60, label: 'seed' },
      sectorBurn: { value: 3.50, label: 'history' },
      lastBurn: 3.54,
      fiveBurn: { value: 3.53, label: '3/5', sampleCount: 3, strategyEligible: false, cleanBaselineEligible: false, confidence: 'Contextual' },
      tenBurn: null,
      v1Burn: 3.54
    }),
    fuelPitRequestWorkbenchRow('Mock pit mistake / One lap short', '15.0 L / 2 laps / just refueled', 'error', {
      currentFuel: 15.0,
      tankCapacity: 60.0,
      targetLaps: 2,
      reserveFuel: 1.0,
      pitLaneFuel: 0.4,
      maxBurn: { value: 13.8, label: 'seed' },
      sectorBurn: { value: 13.2, label: 'sector' },
      lastBurn: 13.5,
      fiveBurn: 13.45,
      tenBurn: null,
      v1Burn: 13.6
    })
  ];
  const dallaraSectorStarts = [
    0.0, 0.055834, 0.085078, 0.125389, 0.166193, 0.265561, 0.370098,
    0.43161, 0.513453, 0.590393, 0.665183, 0.73914, 0.818135, 0.949683
  ];
  const dallaraSectorRows = [
    {
      label: 'Lap 1',
      burns: [0.7598481993817501, 0.40226782958104224, 0.6235557495425468, 0.5804278678620207, 1.3567395021482298, 1.39586020797325, 0.787613157447872, 1.1673551768545707, 1.1161083747768288, 1.0890585158768715, 0.9370388878801705, 1.121568555615056, 1.7440338024102608, 0.6772615571478262],
      warningIndexes: [1, 2, 4, 5, 11]
    },
    {
      label: 'Lap 2',
      burns: [0.7575401149620902, 0.4021852071574834, 0.6229869804958241, 0.5921859042724336, 1.3785955847525848, 1.395861232090823, 0.7925186127602473, 1.2032782804520785, 1.1113016501357684, 1.0884850477891064, 0.9237638261329089, 1.0933981812897784, 1.7310389282762273, 0.6699049573093596],
      warningIndexes: [2, 11]
    },
    {
      label: 'Lap 3 pit-in',
      burns: [0.783822078951026, 0.41401701336312513, 0.6223177073081931, 0.5867685378865328, 1.3539382516370235, 1.4038470192722237, 0.7660975363180018, 1.176056421624498, 1.1083989720084926, 1.0703077874142473, 0.9271530718396113, 1.1273021143024433, 1.7366435143480912, 0.6187148836372272],
      warningIndexes: [2, 13],
      pitIndexes: [13],
      baselineEligible: false
    },
    {
      label: 'Lap 4 refuel',
      burns: [-28.982979844795, 0.403772156071, 0.624943389935, 0.57705331726, 1.377535984809, 1.397596869208, 0.765050209042, 1.176881204868, 1.121434055696, 1.063354768728, 0.918198019861, 1.131269275595, 1.741686431828, 0.666060843462],
      warningIndexes: [0],
      pitIndexes: [0],
      burnOverrides: { 0: 1.011716486230273 },
      baselineEligible: false,
      baselineBreak: true
    },
    {
      label: 'Lap 5 non-green',
      burns: [0.763841664716, 0.420835587835, 0.624009844499, 0.581022174484, 1.376035317787, 1.38724620465, 0.740364552874, 1.199307326597, 1.108741800789, 1.0791256062, 0.908734825878, 1.117356807257, 1.729232051167, 0.655206173909],
      warningIndexes: [8, 9, 10, 11, 12, 13],
      trafficIndexes: [12, 13],
      baselineEligible: false
    }
  ];
  const dallaraSectorSpeedsKph = [
    148.6, 164.4, 178.0, 188.2, 172.9, 242.2, 183.4,
    172.3, 226.2, 186.8, 187.0, 209.6, 238.9, 220.0
  ];
  const dallaraSectorTableRows = dallaraSectorRows.slice(0, 5);
  const vlnSectorStarts = [
    0.0, 0.059239, 0.114229, 0.21989, 0.330979, 0.396273,
    0.483261, 0.564969, 0.644386, 0.722857, 0.806738, 0.946542
  ];
  const vlnSectorRows = [
    {
      label: 'Lap 2',
      burns: [0.7532693164404662, 0.8029803584438042, 1.3677481508859302, 1.3176363680908594, 0.7296434356159125, 1.17952600600141, 1.1827762368767623, 1.1438367059187158, 0.8048902883879521, 1.1446966855620389, 1.7964940486720877, 0.6766241112658875]
    },
    {
      label: 'Lap 3',
      burns: [0.7971368599727313, 0.8018314871214685, 1.3581014277198733, 1.3674858295743348, 0.7271136520205701, 1.1778559000391695, 1.2085004535102541, 1.11423007309579, 0.8631307755087079, 1.1955865565194586, 1.795046854474279, 0.6775373780106975],
      warningIndexes: [3, 4, 10]
    },
    {
      label: 'Lap 4',
      burns: [0.8600512468947272, 0.8140310950137604, 1.4336858716580565, 1.423600725204821, 0.7878611563303366, 1.2332568657231846, 1.218475274128707, 1.1725282184614798, 0.9000760518794024, 1.1959009536054737, 1.8122971909613028, 0.7333868548247793],
      warningIndexes: [3]
    },
    {
      label: 'Lap 5',
      burns: [0.8467356638233312, 0.816556275990294, 1.3978722843979412, 1.3846555995919232, 0.7470347029502946, 1.22231600507763, 1.215347500414449, 1.1377588563916916, 0.851730836025073, 1.1935311413312562, 1.7847671793174165, 0.725350314995584]
    },
    {
      label: 'Lap 6 pit-in',
      burns: [0.8322839744295472, 0.8016017902781378, 1.3193865044458661, 1.3810662631860815, 0.7549524233186098, 1.1703804640922684, 1.1933573411839085, 1.144742724160139, 0.8725994196892053, 1.1883578891696391, 1.8153566409316735, 0.741439027685848],
      warningIndexes: [11],
      pitIndexes: [11],
      baselineEligible: false
    }
  ];
  const vlnSectorSpeedsKph = [
    143.9, 155.0, 162.2, 226.2, 171.9, 160.7,
    208.3, 177.3, 171.3, 192.0, 217.5, 203.8
  ];
  const vlnSectorTableRows = [
    { ...vlnSectorRows[0], trafficIndexes: [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11] },
    { ...vlnSectorRows[1], trafficIndexes: [0] },
    { ...vlnSectorRows[2], trafficIndexes: [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11] },
    { ...vlnSectorRows[3], trafficIndexes: [0] },
    { ...vlnSectorRows[4], trafficIndexes: [0, 1, 2, 3, 4, 5, 6, 7, 8, 11] },
    {
      label: 'Lap 15 refuel',
      burns: [-93.098896845706, 0.873129988979, 1.477429331729, 1.394773086987, 0.819625097418, 1.263450752383, 1.238191925576, 1.16017466307, 0.872061005592, 1.175723654729, 1.830513057684, 0.679753961172],
      warningIndexes: [0, 3],
      pitIndexes: [0],
      burnOverrides: { 0: 1.1364776759013684 },
      baselineEligible: false,
      baselineBreak: true
    },
    {
      label: 'Lap 16',
      burns: [0.850053975405, 0.839592940658, 1.440936427772, 1.415286717133, 0.823165252255, 1.235203704062, 1.225140343905, 1.170636350315, 0.913259073803, 1.213960444098, 1.799637184892, 0.726511162422],
      warningIndexes: [1, 2, 3],
      trafficIndexes: [2, 3, 4, 5, 6, 7, 8, 9, 10, 11]
    },
    {
      label: 'Lap 17',
      burns: [0.852066408204, 0.810492057134, 1.437255742056, 1.376360328865, 0.778898258563, 1.219743115526, 1.224215719143, 1.179577030816, 0.910056142547, 1.211407745963, 1.819568906948, 0.733201716167],
      warningIndexes: [3, 5],
      trafficIndexes: [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
    },
    {
      label: 'Lap 18',
      burns: [0.853000179419, 0.824187415208, 1.432115591562, 1.412643424928, 0.763763093235, 1.247687919877, 1.21536265097, 1.159016110042, 0.879070338247, 1.201681429469, 1.815674362741, 0.730108011049]
    },
    {
      label: 'Lap 19 gap',
      burns: [0.858243258015, 0.895121234913, 1.402117120244, 1.422373554673, 0.77755663374, 1.223791536094, 1.223192964602, 1.151955777819, 0.912205548605, 1.191116849992, 1.821551774593, 0.735265085203],
      warningIndexes: [1, 4],
      trafficIndexes: [0, 8],
      baselineEligible: false
    },
    {
      label: 'Lap 20',
      burns: [0.852292364615, 0.82136094621, 1.442267245172, 1.407059680627, 0.769047590021, 1.216067044902, 1.225243964216, 1.157188428747, 0.891022972424, 1.181974072364, 1.798616863357, 0.676385917817],
      trafficIndexes: [11]
    },
    {
      label: 'Lap 21 pit-in',
      burns: [0.846849986708, 0.816143873386, 1.438458699086, 1.405194273285, 0.779802288349, 1.250814486081, 1.228527534877, 1.146352265541, 0.917413204478, 1.213169454991, 1.819709622526, 0.743699969108],
      warningIndexes: [11],
      pitIndexes: [11],
      trafficIndexes: [0, 1, 2, 4, 5, 6, 9, 11],
      baselineEligible: false
    }
  ];
  const sectorBurnGridSections = [
    {
      title: 'Dallara 45m - Live Projection By Sector',
      headers: fuelSectorLiveProjectionHeaders(dallaraSectorTableRows),
      rows: fuelSectorLiveProjectionGridRows(dallaraSectorTableRows, dallaraSectorStarts, 5, dallaraSectorSpeedsKph)
    },
    {
      title: 'VLN 4h Team - Live Projection By Sector',
      headers: fuelSectorLiveProjectionHeaders(vlnSectorTableRows),
      rows: fuelSectorLiveProjectionGridRows(vlnSectorTableRows, vlnSectorStarts, 3, vlnSectorSpeedsKph)
    }
  ];
  const metricSections = includeLapRows
    ? [
        { title: 'Laps Workbench', rows: lapRows },
        { title: 'Fuel Range Workbench', rows: rangeRows }
      ]
    : activeWorkbench === 'range'
      ? [
          { title: 'Fuel Range Workbench', rows: rangeRows }
        ]
    : activeWorkbench === 'sector'
      ? []
    : activeWorkbench === 'capacity' || activeWorkbench === 'checkpoints' || activeWorkbench === 'boundary' || activeWorkbench === 'snapshot'
      ? []
    : activeWorkbench === 'pit'
      ? [
          { title: 'Fuel To Add Workbench', rows: pitRequestRows },
          { title: 'Fuel To Add Hypotheticals', rows: pitRequestMockRows }
        ]
    : activeWorkbench === 'plan'
      ? [
          { title: 'V1 Plan Row Evidence', rows: planV1Rows }
        ]
    : activeWorkbench === 'stint'
      ? [
          { title: 'V1 Stint Targets Evidence', rows: stintTargetV1Rows }
        ]
    : [
        ...targetUsageSections
      ];
  const gridSections = activeWorkbench === 'sector'
    ? sectorBurnGridSections
    : activeWorkbench === 'capacity'
      ? capacityGridSections
    : activeWorkbench === 'checkpoints'
      ? checkpointGridSections
    : activeWorkbench === 'boundary'
      ? boundaryGridSections
    : activeWorkbench === 'snapshot'
      ? snapshotGridSections
    : activeWorkbench === 'plan'
      ? planV2GridSections
      : activeWorkbench === 'stint'
        ? stintTargetV2GridSections
      : [];
  const chartSections = [];
  const rows = metricSections.flatMap((section) => section.rows);
  const workbenchStatus = activeWorkbench === 'sector'
    ? 'fuel/sector burn workbench'
    : activeWorkbench === 'capacity'
      ? 'fuel/effective capacity workbench'
    : activeWorkbench === 'checkpoints'
      ? 'fuel/checkpoint flow workbench'
    : activeWorkbench === 'boundary'
      ? 'fuel/boundary feasibility workbench'
    : activeWorkbench === 'snapshot'
      ? 'fuel/shared snapshot workbench'
    : activeWorkbench === 'range'
      ? 'fuel/range workbench'
      : activeWorkbench === 'pit'
        ? 'fuel/pit request workbench'
        : activeWorkbench === 'plan'
          ? 'fuel/plan workbench'
          : activeWorkbench === 'stint'
            ? 'fuel/stint targets workbench'
          : 'fuel/target usage workbench';
  const workbenchSource = activeWorkbench === 'sector'
    ? 'source: Fuel V2 workbench; mirrors staged Core sector-burn logic. Rows are real SplitTimeInfo sector boundaries and cells show Live projected L/lap for each lap. Sector labels include median replay speed as context; green cell borders mark pit/refuel/service event-window overlap, not inherently bad sector numbers. Lap 1 uses low-confidence track-percent fallback; later laps hold at S0 and use prior-lap same-sector cumulative scaling from S0+S1 onward. Actual is completed lap burn.'
    : activeWorkbench === 'range'
      ? 'source: Fuel V2 workbench; mirrors staged Core range logic. Current-tank range compares V1 selected burn with explicit V2 Last/5L/10L/Max bucket IDs while retaining each bucket source, samples, and strategy eligibility.'
      : activeWorkbench === 'capacity'
        ? 'source: Fuel V2 workbench; mirrors the typed Core effective-capacity resolver. Physical tank size is never silently treated as usable session fuel when cap evidence is missing or conflicting.'
      : activeWorkbench === 'checkpoints'
        ? 'source: Fuel V2 workbench; mirrors the typed Core fuel-checkpoint flow. Effective cap, first green, current, at-box, service-complete, and pit-exit fuel remain distinct; the pit request targets service-complete fuel and box-to-exit consumption is applied afterward once.'
      : activeWorkbench === 'boundary'
        ? 'source: Fuel V2 workbench; mirrors the factual Core boundary/feasibility owner. Current fuel drives fractional range while expected-at-box fuel drives service-complete target math. Safe laps and feasibility use full-precision values, not formatted display values; capacity conflicts and impossible targets remain explicit.'
      : activeWorkbench === 'snapshot'
        ? 'source: Fuel V2 workbench; mirrors the thin Core snapshot composer. It retains independently owned lap, capacity/checkpoint, and burn-window facts, then projects Boundary, Range, Fuel To Add, Target Usage, and Plan only from explicitly named checkpoints, buckets, target laps, and adjustments. Missing selections do not fall back to Current, Last, or physical tank capacity.'
      : activeWorkbench === 'pit'
        ? 'source: Fuel V2 workbench; mirrors staged Core pit-request logic. Fuel To Add is target laps times selected burn plus optional reserve/pit-lane adjustment minus current fuel, clamped at zero and marked if the tank cannot hold the request. Visible cells use explicit shared burn-bucket IDs: Last, 5L, 10L, Max, Min, and Quali. Sector/live/bridge evidence may be assigned to a bucket only by the fixture contract; display copy never infers bucket identity and missing Max/Min/Quali are not synthesized. Real capture rows keep reserve and learned pit-lane burn at zero; hypothetical rows may exercise those inputs.'
        : activeWorkbench === 'plan'
        ? 'source: Fuel V2 workbench; V1 Plan rows are shown first for direct comparison, then V2 splits green-start/full-race planning from current-checkpoint planning. Current checkpoints use the live tank to reach the next stop and full/refueled capacity for later stints.'
          : activeWorkbench === 'stint'
            ? 'source: Fuel V2 workbench; V1 Stint Targets rows are shown first, then V2 compares current tank range with dynamic Short/Plan/Stretch/Extra targets. Cells show required L/lap, save needed versus live/history burn, and when available whether stretch pace-loss is worth less than the stop/refuel time avoided. This is target context, not advice.'
          : 'source: Fuel V2 workbench; mirrors staged Core target-usage logic. Target Usage cells are required L/lap from fuel budget / target laps, no reserve subtracted; Last is the live burn comparator when available';
  const workbenchTitle = activeWorkbench === 'sector'
    ? 'Sector Burn V2'
    : activeWorkbench === 'capacity'
      ? 'Capacity V2'
    : activeWorkbench === 'checkpoints'
      ? 'Fuel Flow V2'
    : activeWorkbench === 'boundary'
      ? 'Boundary V2'
    : activeWorkbench === 'snapshot'
      ? 'Shared Snapshot V2'
    : activeWorkbench === 'range'
      ? 'Range V2'
      : activeWorkbench === 'pit'
        ? 'Fuel To Add V2'
        : activeWorkbench === 'plan'
          ? 'Plan V2'
          : activeWorkbench === 'stint'
            ? 'Stint Targets V2'
          : 'Target V2';
  return metricsModel(
    'fuel-calculator',
    'Fuel Calculator',
    workbenchStatus,
    rows,
    workbenchSource,
    gridSections,
    metricSections,
    [{ key: 'timeRemaining', value: workbenchTitle, tone: 'info' }],
    true,
    chartSections);
}

function fuelV2CompositeWorkbenchReviewModel(scenarioId) {
  const scenario = fuelV2CompositeWorkbenchScenario(scenarioId);
  const windows = fuelPerLapWorkbenchWindows(
    scenario.acceptedBurnSpans,
    scenario.maximumSeed,
    scenario.historicalNormalSeed);
  const sharedInputs = {
    ...scenario.inputs,
    burns: windows.buckets
  };
  const fullRaceSnapshot = fuelSharedSnapshot({
    ...sharedInputs,
    targetUsage: scenario.greenTargetUsage,
    plan: scenario.fullRacePlan
  });
  const currentSnapshot = fuelSharedSnapshot({
    ...sharedInputs,
    targetUsage: scenario.currentTargetUsage,
    plan: scenario.currentPlan
  });
  const topHalfRows = [
    fuelV2CompositeLapRow(currentSnapshot, scenario.lapSourceLabel),
    fuelV2CompositeFuelPerLapRow(windows),
    fuelV2CompositeRangeRow(currentSnapshot),
    fuelV2CompositeTargetUsageRow(
      'Target Usage - Green',
      fullRaceSnapshot,
      'Green',
      scenario.greenTargetUsage.referenceBurnBucketId),
    fuelV2CompositeTargetUsageRow(
      'Target Usage - Current',
      currentSnapshot,
      'Fuel',
      scenario.currentTargetUsage.referenceBurnBucketId),
    fuelV2CompositeFuelToAddRow(currentSnapshot),
    fuelV2CompositePlanRow('Plan - Full Race', fullRaceSnapshot),
    fuelV2CompositePlanRow('Plan - From Here', currentSnapshot)
  ];
  const lowerHalfRows = [
    fuelV2CompositeStintTargetsRow(currentSnapshot, scenario.stintTargets)
  ];
  const gridSections = [
    {
      title: 'Stint N - Contract Pending',
      headers: ['Stint', 'Start', 'Target', 'Burn', 'Required', 'Add', 'End', 'Feasibility', 'Save', 'Evidence'],
      rows: [
        gridRow('Stint N', [
          '--',
          '--',
          '--',
          '--',
          '--',
          '--',
          '--',
          '--',
          gridCell('not modeled', 'warning')
        ], 'waiting')
      ]
    }
  ];
  const metricSections = [
    { title: 'Top Half - Approved Cell Baseline', rows: topHalfRows },
    { title: 'Stint Targets', rows: lowerHalfRows }
  ];

  return metricsModel(
    'fuel-calculator',
    'Fuel Calculator',
    'fuel/stint sequence workbench',
    [...topHalfRows, ...lowerHalfRows],
    'source: Fuel V2 browser-only composition baseline. Approved cell calculations are projected from shared typed snapshots in their established order; V1 references and engineering proof tables remain outside V2. Exact copy, geometry, and keep/hide choices are intentionally still open. Stint N remains shape-only and does not issue strategy advice.',
    gridSections,
    metricSections,
    [{ key: 'timeRemaining', value: scenario.title, tone: 'info' }],
    true);
}

function fuelV2BottomHalfStateGateReviewModel(stateId) {
  const base = fuelV2CompositeWorkbenchReviewModel('dallara-35m');
  const topHalf = base.metricSections.find((section) => section.title === 'Top Half - Approved Cell Baseline');
  const drivingTopHalfRows = fuelV2DrivingOverlayTopHalfRows(topHalf?.rows || []);
  const state = fuelV2BottomHalfState(stateId);
  const metricSections = [{ title: 'Race Overview', rows: drivingTopHalfRows }];
  const gridSections = state.showBottomHalf
    ? [{
        title: 'Stint Targets',
        headers: state.headers ?? fuelV2StintTargetHeaders(state.hasTireServiceEvidence),
        rows: state.gridRows
      }]
    : [];
  const rows = metricSections.flatMap((section) => section.rows);

  return metricsModel(
    'fuel-calculator',
    'Fuel Calculator',
    'fuel/stint sequence workbench',
    rows,
    'source: Fuel V2 driving-candidate workbench. The compact Race Overview reuses approved Plan and Fuel/Lap snapshot cells; Range, Target Usage, Fuel To Add, and separate Plan variants remain retained diagnostic models. Provisional Stint Targets use the explicit classified History seed from the deterministic format-2 bridge fixture; sanitized V1 references remain observed comparison evidence only. A next-stop refuel is a typed predicted pit-entry calculation, never a future observed fact. This browser-only workbench cannot drive runtime strategy.',
    gridSections,
    metricSections,
    [{ key: 'timeRemaining', value: state.title, tone: state.tone }],
    true);
}

function fuelV2ModelReadinessReviewModel(stateId) {
  const state = fuelV2ModelReadinessState(stateId);
  const burnWindows = fuelPerLapWorkbenchWindows(state.cleanBurnSamples);
  const fuelStateRow = metricRow('Fuel', `${state.currentFuel} L | ${state.maximumFuel} L`, 'info', [
    metricSegment('Current', `${state.currentFuel} L`, 'info'),
    metricSegment('Maximum', `${state.maximumFuel} L`, 'info')
  ], { segmentColumnCount: 2 });
  const fuelUsageRow = fuelV2ModelReadinessFuelUsageRow(burnWindows);
  const modelReadinessRows = state.modelRows;
  const allRows = [fuelStateRow, fuelUsageRow, ...modelReadinessRows];
  const metricSections = [
    { title: 'Fuel State', rows: [fuelStateRow] },
    { title: 'Fuel Usage', rows: [fuelUsageRow] },
    { title: 'Model Readiness', rows: modelReadinessRows }
  ];

  return metricsModel(
    'fuel-calculator',
    'Fuel Calculator',
    'fuel/model readiness workbench',
    allRows,
    `source: Fuel V2 browser-only Test / Practice readiness PoC. ${state.source}`,
    [],
    metricSections,
    [
      { key: 'timeRemaining', value: state.sessionLabel, tone: 'info' },
      { key: 'track', value: 'Exact car + layout', tone: 'info' }
    ],
    true);
}

function fuelV2ModelReadinessFuelUsageRow(windows, label = 'Fuel/Lap') {
  const bucketIds = [
    fuelV2BurnBucketId.last,
    fuelV2BurnBucketId.fiveLapAverage,
    fuelV2BurnBucketId.tenLapAverage,
    fuelV2BurnBucketId.maximum,
    fuelV2BurnBucketId.minimum
  ];
  const segments = bucketIds.map((bucketId) => {
    const bucket = windows.buckets[bucketId];
    const value = Number(bucket?.value);
    const label = fuelV2BurnBucketContract[bucketId].label;
    const available = Number.isFinite(value) && value > 0;
    return metricSegment(
      label,
      available ? `${value.toFixed(2)} L/lap` : '--',
      available ? 'success' : 'waiting');
  });
  const availableCount = segments.filter((segment) => segment.value !== '--').length;
  return metricRow(
    label,
    availableCount ? `${availableCount} / ${segments.length} buckets` : 'Awaiting clean lap',
    availableCount === segments.length ? 'success' : availableCount ? 'warning' : 'waiting',
    segments,
    { segmentColumnCount: segments.length });
}

function fuelV2ModelReadinessState(stateId) {
  const waiting = (label) => ({ label, value: 'Collect', tone: 'error' });
  const baseModelRows = [
    fuelV2ModelReadinessStatusRow('Pit route', [
      waiting('To box'),
      waiting('From box'),
      waiting('Full stop'),
      waiting('Pit box'),
      { label: 'Pit lane pass', value: 'Optional', tone: 'info' }
    ]),
    fuelV2ModelReadinessStatusRow('Refuel', [
      waiting('Small fill'),
      waiting('Large fill'),
      waiting('Fuel flow'),
      waiting('Fuel only'),
      waiting('Fuel + tires')
    ]),
    fuelV2ModelReadinessStatusRow('Tires', [
      waiting('1 tire'),
      waiting('Fronts'),
      waiting('Rears'),
      waiting('Left'),
      waiting('Right'),
      waiting('4 tires'),
    ])
  ];

  if (stateId === 'test-fresh-combo') {
    return {
      sessionLabel: 'Test · fresh combination',
      currentFuel: '68.50',
      maximumFuel: '100.00',
      cleanBurnSamples: [],
      modelRows: baseModelRows,
      source: 'No Test / Practice evidence has been promoted for this combination.'
    };
  }

  if (stateId === 'test-fuel-baseline') {
    return {
      sessionLabel: 'Test · fuel baseline',
      currentFuel: '54.20',
      maximumFuel: '100.00',
      cleanBurnSamples: [13.49, 13.50, 13.51],
      modelRows: baseModelRows,
      source: 'Clean Test burn is retained as Test provenance and may inform non-race fuel calculations; it is not relabeled as race history.'
    };
  }

  if (stateId === 'practice-pit-service') {
    return {
      sessionLabel: 'Practice · service sampling',
      currentFuel: '41.80',
      maximumFuel: '100.00',
      cleanBurnSamples: [13.45, 13.50, 13.48, 13.47, 13.49, 13.48, 13.50, 13.46],
      modelRows: [
        fuelV2ModelReadinessStatusRow('Pit route', [
          waiting('To box'),
          waiting('From box'),
          waiting('Full stop'),
          { label: 'Pit box', value: 'Detected', tone: 'success' },
          { label: 'Pit lane pass', value: 'Optional', tone: 'info' }
        ]),
        fuelV2ModelReadinessStatusRow('Refuel', [
          waiting('Small fill'),
          { label: 'Large fill', value: '+70.0 L', tone: 'success' },
          { label: 'Fuel flow', value: 'Observed', tone: 'success' },
          waiting('Fuel only'),
          { label: 'Fuel + tires', value: 'Confirmed', tone: 'success' }
        ]),
        fuelV2ModelReadinessStatusRow('Tires', [
          { label: '1 tire', value: '0 / 4 corners', tone: 'error' },
          waiting('Fronts'),
          waiting('Rears'),
          waiting('Left'),
          waiting('Right'),
          { label: '4 tires', value: 'Confirmed', tone: 'success' },
        ])
      ],
      source: 'The checklist distinguishes saved observed evidence from evidence that is useful but still too narrow to support a broad pit-service or tire strategy model.'
    };
  }

  throw new Error(`Unknown Fuel V2 model-readiness state fixture: ${stateId}`);
}

function fuelV2ModelReadinessStatusRow(label, cells) {
  const normalizedCells = cells.map((cell) => ({
    label: cell.label,
    value: cell.value,
    tone: cell.tone || 'waiting'
  }));
  const rowTone = normalizedCells.some((cell) => cell.tone === 'error')
    ? 'error'
    : normalizedCells.some((cell) => cell.tone === 'warning')
      ? 'warning'
      : normalizedCells.every((cell) => cell.tone === 'success')
        ? 'success'
        : 'info';
  return metricRow(
    label,
    normalizedCells.map((cell) => cell.value).join(' | '),
    rowTone,
    normalizedCells.map((cell) => metricSegment(cell.label, cell.value, cell.tone)),
    { segmentColumnCount: normalizedCells.length });
}

function fuelV2DrivingOverlayTopHalfRows(rows) {
  const planFullRace = rows.find((row) => row.label === 'Plan - Full Race');
  const planFromHere = rows.find((row) => row.label === 'Plan - From Here');
  const fuelPerLap = rows.find((row) => row.label === 'Fuel/Lap');
  const overview = fuelV2DrivingPlanOverviewRow(planFullRace, planFromHere);
  return [overview, fuelPerLap].filter(Boolean);
}

function fuelV2DrivingPlanOverviewRow(fullRacePlan, currentPlan) {
  if (!fullRacePlan && !currentPlan) return null;
  const full = Array.isArray(fullRacePlan?.segments) ? fullRacePlan.segments : [];
  const current = Array.isArray(currentPlan?.segments) ? currentPlan.segments : [];
  const segmentAt = (segments, index, label) => {
    const source = segments[index];
    return metricSegment(label, source?.value || '--', source?.tone || 'waiting');
  };
  const tone = fullRacePlan?.tone === 'error' || currentPlan?.tone === 'error'
    ? 'error'
    : fullRacePlan?.tone === 'waiting' && currentPlan?.tone === 'waiting'
      ? 'waiting'
      : 'info';
  return metricRow('Plan', 'race overview', tone, [
    segmentAt(full, 0, 'Race'),
    segmentAt(current, 1, 'Remain'),
    segmentAt(full, 2, 'Rhythm'),
    segmentAt(full, 3, 'Stops'),
    segmentAt(full, 4, 'Final')
  ], { segmentColumnCount: 5 });
}

function fuelV2BottomHalfState(stateId) {
  const fixedFourLap = fuelV2BottomHalfRealHistoryScenario('dallara-nurburgring-fixed-four-lap');
  const fixedStartFuel = Number(fixedFourLap?.fuel?.firstCleanStintStartFuelLiters);
  const fixedHistoryBurn = Number(fuelV2HistoryNormalSeed()?.value);
  const fixedSafeLaps = Number.isFinite(fixedStartFuel) && Number.isFinite(fixedHistoryBurn)
    ? Math.floor(fixedStartFuel / fixedHistoryBurn)
    : null;
  const fixedRequiredBurn = (targetLaps) => Number.isFinite(fixedStartFuel) && targetLaps > 0
    ? fixedStartFuel / targetLaps
    : null;
  const fuelLabel = (value) => Number.isFinite(value) ? `${value.toFixed(2)} L` : '--';
  const burnLabel = (value) => Number.isFinite(value) ? `${value.toFixed(2)} L/lap` : '--';

  if (stateId === 'no-data') {
    return {
      title: 'V2 - Bottom half unavailable',
      tone: 'waiting',
      showBottomHalf: false,
      gridRows: []
    };
  }

  if (stateId === 'dallara-three-lap-control') {
    const targetLaps = 3;
    return fuelV2BottomHalfStintRows({
      title: 'V2 - Dallara three-lap control',
      tone: 'success',
      headers: [],
      rows: [
        gridSummaryRow(
          `No pit stop needed — ${targetLaps} laps fit at ${burnLabel(fixedHistoryBurn)}. Keep the current tire set.`,
          'success')
      ]
    });
  }

  if (stateId === 'dallara-four-lap') {
    const targetLaps = 4;
    const required = fixedRequiredBurn(targetLaps);
    const firstStintLaps = fixedSafeLaps;
    const finalStintLaps = Number.isInteger(firstStintLaps) ? targetLaps - firstStintLaps : null;
    const predictedAtBox = Number.isInteger(firstStintLaps)
      && Number.isFinite(fixedStartFuel)
      && Number.isFinite(fixedHistoryBurn)
      ? fixedStartFuel - firstStintLaps * fixedHistoryBurn
      : null;
    const finalFuelNeed = Number.isInteger(finalStintLaps) && finalStintLaps > 0 && Number.isFinite(fixedHistoryBurn)
      ? finalStintLaps * fixedHistoryBurn
      : null;
    const predictedRefuel = Number.isFinite(predictedAtBox) && Number.isFinite(finalFuelNeed)
      ? Math.max(0, finalFuelNeed - predictedAtBox)
      : null;
    const saveAgainstHistory = Number.isFinite(required) && Number.isFinite(fixedHistoryBurn)
      ? fixedHistoryBurn - required
      : null;
    return fuelV2BottomHalfStintRows({
      title: 'V2 - Dallara fixed four-lap start',
      tone: 'warning',
      hasTireServiceEvidence: true,
      rows: [
        gridRow('Stint 1', fuelV2StintTargetCells(true,
        gridCell(`${firstStintLaps ?? '--'} laps`, 'info'),
        gridCell(Number.isFinite(required) ? `≤ ${burnLabel(required)}` : '--', 'warning'),
        gridCell('Keep tires', 'info'),
        gridCell(Number.isFinite(saveAgainstHistory) ? `Save ${burnLabel(saveAgainstHistory)}` : 'Awaiting live fuel', 'warning')
        ), 'normal'),
        gridRow('Final Stint', fuelV2StintTargetCells(true,
          gridCell(`${finalStintLaps ?? '--'} lap${finalStintLaps === 1 ? '' : 's'}`, 'info'),
          gridCell(Number.isFinite(finalFuelNeed) ? `≤ ${burnLabel(finalFuelNeed)}` : '--', 'info'),
          gridCell('Front tires — observed', 'info'),
          gridCell(
            Number.isFinite(predictedRefuel)
              ? `Add ${fuelLabel(predictedRefuel)} at pit`
              : 'Awaiting pit-entry fuel forecast',
            Number.isFinite(predictedRefuel) ? 'warning' : 'waiting')
        ), 'normal'),
        gridRow('Strategy', fuelV2StintTargetCells(true,
          gridCell('1 planned stop', 'warning'),
          gridCell(`${burnLabel(fixedHistoryBurn)} baseline`, 'info'),
          gridCell('4 tires — collect sample', 'waiting'),
          gridCell('Replan at pit exit', 'info')
        ), 'normal')
      ]
    });
  }

  if (stateId === 'endurance-stint-five') {
    return fuelV2BottomHalfEnduranceRows(5, 20);
  }

  if (stateId === 'endurance-stint-six') {
    return fuelV2BottomHalfEnduranceRows(6, 20);
  }

  if (stateId === 'dallara-timed') {
    return {
      title: 'V2 - Dallara timed forty-five-minute reference',
      tone: 'warning',
      showBottomHalf: false,
      gridRows: []
    };
  }

  if (stateId === 'charlotte-degraded') {
    return {
      title: 'V2 - Charlotte tire-service reference',
      tone: 'warning',
      showBottomHalf: false,
      gridRows: []
    };
  }

  throw new Error(`Unknown Fuel V2 bottom-half state fixture: ${stateId}`);
}

function fuelV2BottomHalfStintRows({ title, tone, rows, headers, hasTireServiceEvidence = false }) {
  return {
    title,
    tone,
    showBottomHalf: true,
    gridRows: rows,
    hasTireServiceEvidence,
    ...(headers !== undefined ? { headers } : {})
  };
}

function fuelV2BottomHalfEnduranceRows(currentStintNumber, finalStintNumber) {
  const nextStintNumber = currentStintNumber + 1;
  const plannedStintLaps = 28;
  const finalStintLaps = 11;
  const plannedBurnTarget = '≤ 3.80 L/lap';
  const rows = [];
  if (currentStintNumber >= finalStintNumber) {
    rows.push(gridRow('Final Stint', fuelV2StintTargetCells(false,
      gridCell(`${finalStintLaps} laps`, 'info'),
      gridCell(plannedBurnTarget, 'info'),
      null,
      gridCell('Tracking live fuel', 'info')
    ), 'normal'));
  } else {
    rows.push(gridRow(`Stint ${currentStintNumber}`, fuelV2StintTargetCells(false,
      gridCell(`${plannedStintLaps} laps`, 'info'),
      gridCell(plannedBurnTarget, 'info'),
      null,
      gridCell('Tracking live fuel', 'info')
    ), 'normal'));
  }

  if (nextStintNumber < finalStintNumber) {
    rows.push(gridRow(`Stint ${nextStintNumber}`, fuelV2StintTargetCells(false,
      gridCell(`${plannedStintLaps} laps`, 'info'),
      gridCell(plannedBurnTarget, 'info'),
      null,
      gridCell('Planned after pit', 'info')
    ), 'normal'));
  }

  if (finalStintNumber > currentStintNumber && finalStintNumber !== nextStintNumber) {
    rows.push(gridRow('Final Stint', fuelV2StintTargetCells(false,
      gridCell(`${finalStintLaps} laps`, 'info'),
      gridCell(plannedBurnTarget, 'info'),
      null,
      gridCell('Forecast only', 'info')
    ), 'normal'));
  } else if (nextStintNumber === finalStintNumber) {
    rows.push(gridRow('Final Stint', fuelV2StintTargetCells(false,
      gridCell(`${finalStintLaps} laps`, 'info'),
      gridCell(plannedBurnTarget, 'info'),
      null,
      gridCell('Forecast only', 'info')
    ), 'normal'));
  }

  rows.push(gridRow('Strategy', fuelV2StintTargetCells(false,
    gridCell(`${finalStintNumber - 1} planned stops`, 'info'),
    gridCell(`${plannedBurnTarget.replace('≤ ', '')} baseline`, 'info'),
    null,
    gridCell('Replan at pit exit', 'info')
  ), 'normal'));
  return fuelV2BottomHalfStintRows({
    title: `V2 - Endurance row shape at Stint ${currentStintNumber}`,
    tone: 'info',
    rows
  });
}

function fuelV2BottomHalfRealHistoryScenario(id) {
  const scenario = fuelV2BottomHalfRealHistoryReferences?.scenarios?.find((candidate) => candidate.id === id);
  if (!scenario) {
    throw new Error(`Missing Fuel V2 bottom-half real-history reference: ${id}`);
  }

  return scenario;
}

function fuelV2HistoryNormalSeed() {
  // Browser mirror of FuelV2HistoryNormalBurnQueryService: accept only the
  // current aggregate version, exact car/layout identity, a race/practice
  // family, classified/learning evidence, and a positive finite aggregate
  // mean. This fixture has one Race family; Qualifying is deliberately never
  // considered normal-history fallback.
  const fixture = fuelV2HistoryBridgeFixture;
  const context = fixture?.queryContext;
  const aggregate = fixture?.persistedAggregate;
  const combo = aggregate?.scope?.combo;
  const metric = aggregate?.acceptedLapFuelPerLapLiters;
  const expected = fixture?.expectedSelection;
  const config = String(context?.trackConfigName || '').trim();
  const configHex = Array.from(new TextEncoder().encode(config))
    .map((byte) => byte.toString(16).padStart(2, '0').toUpperCase())
    .join('');
  const requestedFamily = String(context?.sessionFamily || '').trim().toLowerCase();
  const exactIdentity = Number.isInteger(context?.carId)
    && Number.isInteger(context?.trackId)
    && config.length > 0
    && combo?.carKey === `car-id-${context.carId}`
    && combo?.trackLayoutKey === `track-id-${context.trackId}-config-${configHex}`
    && combo?.trackLayoutIdentitySource === 'track-id-and-config';
  const exactFamily = requestedFamily === 'race'
    && ['race', 'practice'].includes(combo?.sessionKey)
    || requestedFamily === 'practice' && combo?.sessionKey === 'practice';
  const mean = Number(metric?.mean);
  const sampleCount = Number(metric?.sampleCount);
  const usable = fixture?.rawCaptureExpectation?.formatVersion === 2
    && aggregate?.aggregateVersion === 2
    && expected?.status === 'Selected'
    && expected?.burnBucketId === fuelV2BurnBucketId.historicalNormal
    && expected?.burnSource === 'HistoricalNormal'
    && exactIdentity
    && exactFamily
    && Number(aggregate?.classifiedSessionCount) > 0
    && Number(aggregate?.learningEligibleSessionCount) > 0
    && Number.isFinite(mean) && mean > 0
    && Number.isInteger(sampleCount) && sampleCount > 0;
  if (!usable) return null;

  return {
    value: mean,
    source: `classified ${combo.sessionKey} history; ${sampleCount} accepted lap windows; ${aggregate.learningEligibleSessionCount} learning-eligible sessions`,
    burnSource: 'HistoricalNormal',
    sampleCount,
    confidence: 'Seeded',
    displayEligible: true,
    cleanBaselineEligible: false,
    strategyEligible: false,
    detailLabel: `${combo.sessionKey} (${sampleCount})`
  };
}

function fuelV2CompositeWorkbenchScenario(scenarioId) {
  if (scenarioId === 'dallara-35m') {
    const historicalNormalSeed = fuelV2HistoryNormalSeed();
    return {
      title: 'V2 - Dallara 35m',
      lapSourceLabel: 'seed',
      acceptedBurnSpans: [],
      maximumSeed: {
        value: 13.7982,
        label: 'quali seed',
        source: 'matching qualifying seed',
        burnSource: 'HistoricalSeed',
        sampleCount: 1,
        confidence: 'Seeded',
        strategyEligible: false,
        cleanBaselineEligible: false
      },
      historicalNormalSeed,
      inputs: {
        lapBudget: {
          primaryLapsRemaining: 4,
          possibleLapsRemaining: 4,
          estimatedFinishLap: 4,
          canDriveFuelAdvice: true
        },
        checkpoints: {
          capacity: { physicalCapacityLiters: 75, driverCapPercent: 0.68, classCapPercent: 0.68 },
          measuredFirstGreenFuelLiters: 49.6766,
          currentFuelLiters: 49.6766
        },
        boundary: { targetLaps: 3, reserveFuelLiters: 0, pitLaneFuelLiters: 0 }
      },
      greenTargetUsage: {
        fuelBudgetCheckpoint: 'firstGreen',
        referenceBurnBucketId: fuelV2BurnBucketId.historicalNormal,
        targetLaps: [3, 4, 5]
      },
      currentTargetUsage: {
        fuelBudgetCheckpoint: 'current',
        referenceBurnBucketId: fuelV2BurnBucketId.historicalNormal,
        targetLaps: [3, 4, 5]
      },
      fullRacePlan: {
        mode: 'full-race',
        plannedRaceLapsSource: 'primaryLapsRemaining',
        raceLapsRemainingSource: 'possibleLapsRemaining',
        fuelBudgetCheckpoint: 'firstGreen',
        burnBucketId: fuelV2BurnBucketId.historicalNormal
      },
      currentPlan: {
        mode: 'current-checkpoint',
        plannedRaceLapsSource: 'primaryLapsRemaining',
        raceLapsRemainingSource: 'possibleLapsRemaining',
        currentFuelCheckpoint: 'current',
        currentBurnBucketId: fuelV2BurnBucketId.historicalNormal,
        futureFuelCheckpoint: 'effectiveCapacity',
        futureBurnBucketId: fuelV2BurnBucketId.historicalNormal
      },
      stintTargets: {
        referenceBurnBucketId: fuelV2BurnBucketId.historicalNormal,
        targetLaps: 3,
        planLabel: 'classified History seed — confirm live',
        flags: ['degraded']
      }
    };
  }

  if (scenarioId === 'current-service') {
    return {
      title: 'V2 - Current + Service',
      lapSourceLabel: 'control',
      acceptedBurnSpans: [9.8, 10.1, 10.0, 9.9, 10.0],
      maximumSeed: null,
      inputs: {
        lapBudget: {
          primaryLapsRemaining: 12,
          possibleLapsRemaining: 11.5,
          estimatedFinishLap: 12,
          canDriveFuelAdvice: true
        },
        checkpoints: {
          capacity: { physicalCapacityLiters: 100, driverCapPercent: 0.6, classCapPercent: 0.6 },
          measuredFirstGreenFuelLiters: 58,
          currentFuelLiters: 40,
          measuredAtBoxFuelLiters: 10,
          measuredServiceCompleteFuelLiters: 50,
          measuredPitExitFuelLiters: 49
        },
        boundary: { targetLaps: 5, reserveFuelLiters: 0, pitLaneFuelLiters: 0 }
      },
      greenTargetUsage: {
        fuelBudgetCheckpoint: 'firstGreen',
        referenceBurnBucketId: fuelV2BurnBucketId.fiveLapAverage,
        targetLaps: [5, 6, 7]
      },
      currentTargetUsage: {
        fuelBudgetCheckpoint: 'current',
        referenceBurnBucketId: fuelV2BurnBucketId.last,
        targetLaps: [3, 4, 5]
      },
      fullRacePlan: {
        mode: 'full-race',
        plannedRaceLapsSource: 'primaryLapsRemaining',
        raceLapsRemainingSource: 'possibleLapsRemaining',
        fuelBudgetCheckpoint: 'firstGreen',
        burnBucketId: fuelV2BurnBucketId.fiveLapAverage
      },
      currentPlan: {
        mode: 'current-checkpoint',
        plannedRaceLapsSource: 'primaryLapsRemaining',
        raceLapsRemainingSource: 'possibleLapsRemaining',
        currentFuelCheckpoint: 'current',
        currentBurnBucketId: fuelV2BurnBucketId.last,
        futureFuelCheckpoint: 'serviceComplete',
        futureBurnBucketId: fuelV2BurnBucketId.fiveLapAverage
      },
      stintTargets: {
        referenceBurnBucketId: fuelV2BurnBucketId.last,
        targetLaps: 4,
        planLabel: 'current target'
      }
    };
  }

  return {
    title: 'V2 - VLN Full Race',
    lapSourceLabel: 'live',
    acceptedBurnSpans: [12.95, 13.00, 13.20, 13.30, 13.65, 13.49, 13.49, 13.50, 13.50, 13.52],
    maximumSeed: null,
    inputs: {
      lapBudget: {
        primaryLapsRemaining: 31,
        possibleLapsRemaining: 30.96,
        estimatedFinishLap: 31,
        canDriveFuelAdvice: true
      },
      checkpoints: {
        capacity: { physicalCapacityLiters: 104.94, driverCapPercent: 1, classCapPercent: 1 },
        measuredFirstGreenFuelLiters: 102.8464,
        currentFuelLiters: 61.64
      },
      boundary: { targetLaps: 7, reserveFuelLiters: 0, pitLaneFuelLiters: 0 }
    },
    greenTargetUsage: {
      fuelBudgetCheckpoint: 'firstGreen',
      referenceBurnBucketId: fuelV2BurnBucketId.last,
      targetLaps: [7, 8, 9]
    },
    currentTargetUsage: {
      fuelBudgetCheckpoint: 'current',
      referenceBurnBucketId: fuelV2BurnBucketId.last,
      targetLaps: [4, 5, 6]
    },
    fullRacePlan: {
      mode: 'full-race',
      plannedRaceLapsSource: 'primaryLapsRemaining',
      raceLapsRemainingSource: 'possibleLapsRemaining',
      fuelBudgetCheckpoint: 'firstGreen',
      burnBucketId: fuelV2BurnBucketId.fiveLapAverage
    },
    currentPlan: {
      mode: 'current-checkpoint',
      plannedRaceLapsSource: 'primaryLapsRemaining',
      raceLapsRemainingSource: 'possibleLapsRemaining',
      currentFuelCheckpoint: 'current',
      currentBurnBucketId: fuelV2BurnBucketId.last,
      futureFuelCheckpoint: 'serviceComplete',
      futureBurnBucketId: fuelV2BurnBucketId.fiveLapAverage
    },
    stintTargets: {
      referenceBurnBucketId: fuelV2BurnBucketId.last,
      targetLaps: 5,
      planLabel: 'current 5-lap stretch'
    }
  };
}

function fuelV2CompositeLapRow(snapshot, sourceLabel) {
  const lapBudget = snapshot.lapBudget;
  const projected = Number.isFinite(lapBudget?.estimatedFinishLap)
    ? lapBudget.estimatedFinishLap.toFixed(2)
    : '--';
  const tone = fuelSharedLapBudgetTone(lapBudget);
  const suffix = String(sourceLabel || '').trim();
  return metricRow('Lap', projected === '--' || !suffix ? projected : `${projected} ${suffix}`, tone);
}

function fuelV2CompositeFuelPerLapRow(windows) {
  const buckets = fuelV2BurnBucketOrder.map((bucketId) => windows.buckets[bucketId]);
  const segments = buckets.map((bucket) => {
    const seeded = bucket?.confidence === 'Seeded'
      || bucket?.burnSource === 'HistoricalSeed'
      || bucket?.burnSource === 'HistoricalNormal';
    return metricSegment(
      bucket.label,
      seeded && Number.isFinite(bucket.value)
        ? `${fuelPerLapWorkbenchValue(bucket.value)} ${bucket.detailLabel || 'seed'}`
        : fuelPerLapWorkbenchValue(bucket.value),
      Number.isFinite(bucket.value) ? 'info' : 'waiting',
      fuelV2BurnBucketEvidence(bucket));
  });
  const available = buckets.some((bucket) => Number.isFinite(bucket.value));
  return metricRow(
    'Fuel/Lap',
    available ? 'shared burn evidence' : '--',
    available ? 'info' : 'waiting',
    segments,
    { segmentColumnCount: fuelV2BurnBucketOrder.length });
}

function fuelV2CompositeRangeRow(snapshot) {
  const rangeKeys = [
    [fuelV2BurnBucketId.last, 'last'],
    [fuelV2BurnBucketId.fiveLapAverage, 'fiveLapAverage'],
    [fuelV2BurnBucketId.tenLapAverage, 'tenLapAverage'],
    [fuelV2BurnBucketId.historicalNormal, null],
    [fuelV2BurnBucketId.maximum, 'maximum'],
    [fuelV2BurnBucketId.minimum, null],
    [fuelV2BurnBucketId.qualifying, null]
  ];
  const segments = rangeKeys.map(([bucketId, rangeKey]) => {
    const burn = snapshot.burnBuckets[bucketId].bucket;
    const range = fuelV2DerivedBurnBucket(burn, rangeKey ? snapshot.range[rangeKey] : null, `range from ${burn.label}`);
    const segment = fuelRangeWorkbenchSegment({ ...range, displaySuffix: '' });
    return {
      ...segment,
      tone: segment.value === '--' ? 'waiting' : 'info'
    };
  });
  const available = segments.some((segment) => segment.value !== '--');
  return metricRow(
    'Laps In Tank',
    available ? 'bucket ranges' : '--',
    available ? 'info' : 'waiting',
    segments,
    { segmentColumnCount: fuelV2BurnBucketOrder.length });
}

function fuelV2CompositeTargetUsageRow(label, snapshot, budgetLabel, referenceBucketId) {
  const targetUsage = snapshot.targetUsage;
  const reference = targetUsage.referenceBurn
    ?? fuelV2BurnBucket(referenceBucketId, null);
  const segments = [
    metricSegment(
      budgetLabel,
      fuelBoundaryLitersLabel(targetUsage.fuelBudgetLiters),
      fuelStintNDependencyTone(targetUsage.fuelBudgetSelection.state)),
    metricSegment(
      reference.label,
      fuelV2CompositeBurnValue(reference),
      fuelTargetUsageReferenceTone(reference),
      fuelV2BurnBucketEvidence(reference)),
    ...targetUsage.targets.map((target) => metricSegment(
      fuelTargetUsageLapLabel(target.targetLaps),
      fuelPerLapWorkbenchValue(target.requiredFuelPerLap),
      fuelTargetUsageTone(target.requiredFuelPerLap, reference.value),
      fuelV2CompositeTargetReferenceEvidence(reference)))
  ];
  return metricRow(label, targetUsage.fuelBudgetSelection.state, fuelStintNDependencyTone(targetUsage.fuelBudgetSelection.state), segments);
}

function fuelV2CompositeBurnValue(burn) {
  const value = fuelPerLapWorkbenchValue(burn?.value);
  if (value === '--') return value;
  const suffix = burn?.detailLabel || (burn?.confidence === 'Seeded' ? 'seed' : '');
  return suffix ? `${value} ${suffix}` : value;
}

function fuelV2CompositeTargetReferenceEvidence(reference) {
  return {
    referenceBurnBucketId: reference.id,
    referenceBurnSource: reference.burnSource,
    referenceSampleCount: reference.sampleCount,
    referenceConfidence: reference.confidence,
    referenceContextFlags: reference.contextFlags,
    referenceDisplayEligible: reference.displayEligible,
    referenceCleanBaselineEligible: reference.cleanBaselineEligible,
    referenceStrategyEligible: reference.strategyEligible,
    referenceProvenance: reference.source
  };
}

function fuelV2CompositeFuelToAddRow(snapshot) {
  const bucketKeys = [
    [fuelV2BurnBucketId.last, 'last'],
    [fuelV2BurnBucketId.fiveLapAverage, 'fiveLapAverage'],
    [fuelV2BurnBucketId.tenLapAverage, 'tenLapAverage'],
    [fuelV2BurnBucketId.historicalNormal, null],
    [fuelV2BurnBucketId.maximum, 'maximum'],
    [fuelV2BurnBucketId.minimum, 'minimum'],
    [fuelV2BurnBucketId.qualifying, 'qualifying']
  ];
  const segments = bucketKeys.map(([bucketId, requestKey]) => {
    const burn = snapshot.burnBuckets[bucketId].bucket;
    const request = requestKey ? snapshot.pitRequest?.[requestKey] ?? null : null;
    const amount = request?.clampedAddLiters ?? request?.desiredAddLiters;
    const limited = request?.stateFlags?.has('tank-limited') === true;
    const value = Number.isFinite(amount)
      ? limited ? `${amount.toFixed(2)} L cap` : `+${amount.toFixed(2)} L`
      : '--';
    const tone = fuelV2CompositeFuelToAddTone(request, burn, amount);
    return metricSegment(
      burn.label,
      value,
      tone,
      fuelV2BurnBucketEvidence(fuelV2DerivedBurnBucket(burn, amount, `pit add from ${burn.label}`)));
  });
  const available = segments.some((segment) => segment.value !== '--');
  return metricRow(
    'Fuel To Add',
    available ? 'explicit service target' : '--',
    available ? 'info' : 'waiting',
    segments,
    { segmentColumnCount: fuelV2BurnBucketOrder.length });
}

function fuelV2CompositeFuelToAddTone(request, burn, amount) {
  if (!request || !Number.isFinite(amount)) return 'waiting';
  const feasibilityTone = fuelBoundaryFeasibilityTone(request.feasibilityState);
  if (feasibilityTone === 'error' || feasibilityTone === 'waiting') return feasibilityTone;
  if (amount <= 0.001) return 'success';
  if (!burn.strategyEligible
      || burn.id === fuelV2BurnBucketId.maximum
      || burn.id === fuelV2BurnBucketId.minimum
      || burn.id === fuelV2BurnBucketId.qualifying) return 'warning';
  return 'info';
}

function fuelV2CompositePlanRow(label, snapshot) {
  const plan = snapshot.plan;
  const mode = snapshot.planDependencies?.mode;
  const values = mode === 'current-checkpoint'
    ? [
        ['Total', plan?.raceLabel],
        ['To go', plan?.remainLabel],
        ['Now/Full', plan?.currentCapacityLabel],
        ['Rhythm', plan?.rhythmLabel],
        ['Stops', plan?.stopsLabel],
        ['Final', plan?.finalLabel]
      ]
    : [
        ['Race', plan?.raceLabel],
        ['Start cap', plan?.stintCapacityLabel],
        ['Rhythm', plan?.rhythmLabel],
        ['Stops', plan?.stopsLabel],
        ['Final', plan?.finalLabel]
      ];
  const tone = plan?.tone || 'waiting';
  const segmentTone = tone === 'error' || tone === 'waiting' ? tone : 'normal';
  const segments = values.map(([segmentLabel, value]) => metricSegment(
    segmentLabel,
    value || '--',
    value && value !== '--' ? segmentTone : 'waiting'));
  return metricRow(label, plan?.rhythmLabel || '--', tone, segments);
}

function fuelV2CompositeStintTargetsRow(snapshot, config) {
  const currentFuel = fuelSharedCheckpointSelection(snapshot.checkpoints, 'current');
  const referenceBurn = snapshot.burnBuckets[config.referenceBurnBucketId]?.bucket ?? null;
  const row = fuelStintTargetV2WorkbenchGridRow('Stint Targets', {
    remainingLaps: snapshot.lapBudget?.possibleLapsRemaining
      ?? snapshot.lapBudget?.primaryLapsRemaining,
    currentFuelLiters: currentFuel.calculationLiters,
    reserveFuelLiters: config.reserveFuelLiters || 0,
    pitLaneFuelLiters: config.pitLaneFuelLiters || 0,
    referenceBurnLitersPerLap: referenceBurn,
    targetLaps: config.targetLaps,
    planLabel: config.planLabel,
    flags: config.flags || [],
    targetTimeContexts: config.targetTimeContexts
  });
  const labels = ['To go', 'Tank', 'Short', 'Plan', 'Stretch', 'Extra', 'Live', 'Status'];
  const segments = labels.map((segmentLabel, index) => metricSegment(
    segmentLabel,
    row.cells[index]?.value || '--',
    row.cells[index]?.tone || row.tone));
  return metricRow('Stint Targets', row.cells.at(-1)?.value || '--', row.tone, segments);
}

function fuelStintNDependencyTone(state) {
  if (state === 'available') return 'info';
  if (state === 'invalid' || state === 'conflicted') return 'error';
  return 'waiting';
}

function fuelCapacityWorkbenchGridRow(label, inputs) {
  const snapshot = fuelCapacitySnapshot(inputs);
  return gridRow(label, [
    gridCell(fuelCapacityLitersLabel(snapshot.physicalCapacityLiters), fuelCapacityValueTone(snapshot.physicalCapacityLiters)),
    gridCell(fuelCapacityPercentLabel(snapshot.driverCapPercent), fuelCapacityValueTone(snapshot.driverCapPercent)),
    gridCell(fuelCapacityPercentLabel(snapshot.classCapPercent), fuelCapacityValueTone(snapshot.classCapPercent)),
    gridCell(fuelCapacityLitersLabel(snapshot.observedFuelLiters), fuelCapacityValueTone(snapshot.observedFuelLiters)),
    gridCell(fuelCapacityLitersLabel(snapshot.effectiveCapacityLiters), snapshot.canDriveFuelAdvice ? 'info' : 'warning'),
    gridCell(fuelCapacitySourceLabel(snapshot.source), fuelCapacityStateTone(snapshot)),
    gridCell(fuelCapacityStateLabel(snapshot), fuelCapacityStateTone(snapshot))
  ], fuelCapacityStateTone(snapshot));
}

function fuelCapacitySnapshot(inputs) {
  const flags = new Set();
  const physicalCapacityLiters = fuelCapacityPositiveNumber(inputs?.physicalCapacityLiters);
  const driverCapPercent = fuelCapacityPercent(inputs?.driverCapPercent);
  const classCapPercent = fuelCapacityPercent(inputs?.classCapPercent);
  const observedFuelLiters = fuelCapacityNonNegativeNumber(inputs?.observedFuelLiters);
  fuelCapacityInvalidFlag(flags, inputs?.physicalCapacityLiters, physicalCapacityLiters, 'invalid-physical');
  fuelCapacityInvalidFlag(flags, inputs?.driverCapPercent, driverCapPercent, 'invalid-driver');
  fuelCapacityInvalidFlag(flags, inputs?.classCapPercent, classCapPercent, 'invalid-class');
  fuelCapacityInvalidFlag(flags, inputs?.observedFuelLiters, observedFuelLiters, 'invalid-observed');

  if (!Number.isFinite(physicalCapacityLiters)) {
    flags.add('missing-physical');
    return fuelCapacityResult(
      physicalCapacityLiters,
      driverCapPercent,
      classCapPercent,
      observedFuelLiters,
      null,
      null,
      'unavailable',
      'unavailable',
      flags,
      false);
  }

  if (!Number.isFinite(driverCapPercent) && !Number.isFinite(classCapPercent)) {
    flags.add('missing-cap');
    return fuelCapacityResult(
      physicalCapacityLiters,
      driverCapPercent,
      classCapPercent,
      observedFuelLiters,
      null,
      null,
      'physical-only',
      fuelCapacityHasInvalidEvidence(flags) ? 'conflicted' : 'contextual',
      flags,
      false);
  }

  let appliedFuelPercent;
  let source;
  let confidence = 'high';
  if (Number.isFinite(driverCapPercent) && Number.isFinite(classCapPercent)) {
    appliedFuelPercent = Math.min(driverCapPercent, classCapPercent);
    if (Math.abs(driverCapPercent - classCapPercent) <= 0.0005) {
      source = 'matching-driver-class';
      confidence = 'authoritative';
    } else {
      source = 'most-restrictive-conflict';
      confidence = 'conflicted';
      flags.add('driver-class-conflict');
    }
  } else if (Number.isFinite(driverCapPercent)) {
    appliedFuelPercent = driverCapPercent;
    source = 'driver';
  } else {
    appliedFuelPercent = classCapPercent;
    source = 'class';
  }

  const effectiveCapacityLiters = physicalCapacityLiters * appliedFuelPercent;
  flags.add(appliedFuelPercent >= 1 - 0.0005 ? 'unrestricted' : 'limited');
  if (Number.isFinite(observedFuelLiters) && observedFuelLiters > effectiveCapacityLiters + 0.05) {
    flags.add('observed-above-cap');
    confidence = 'conflicted';
  }
  if (fuelCapacityHasInvalidEvidence(flags)) {
    confidence = 'conflicted';
  }

  return fuelCapacityResult(
    physicalCapacityLiters,
    driverCapPercent,
    classCapPercent,
    observedFuelLiters,
    effectiveCapacityLiters,
    appliedFuelPercent,
    source,
    confidence,
    flags,
    confidence === 'authoritative' || confidence === 'high');
}

function fuelCapacityResult(
  physicalCapacityLiters,
  driverCapPercent,
  classCapPercent,
  observedFuelLiters,
  effectiveCapacityLiters,
  appliedFuelPercent,
  source,
  confidence,
  flags,
  canDriveFuelAdvice) {
  return {
    physicalCapacityLiters,
    driverCapPercent,
    classCapPercent,
    observedFuelLiters,
    effectiveCapacityLiters,
    appliedFuelPercent,
    source,
    confidence,
    flags,
    canDriveFuelAdvice: canDriveFuelAdvice && Number.isFinite(effectiveCapacityLiters)
  };
}

function fuelCapacityInvalidFlag(flags, rawValue, acceptedValue, flag) {
  if (rawValue !== null && rawValue !== undefined && !Number.isFinite(acceptedValue)) {
    flags.add(flag);
  }
}

function fuelCapacityHasInvalidEvidence(flags) {
  return ['invalid-physical', 'invalid-driver', 'invalid-class', 'invalid-observed', 'driver-class-conflict', 'observed-above-cap']
    .some((flag) => flags.has(flag));
}

function fuelCapacityPositiveNumber(value) {
  if (value === null || value === undefined) return null;
  const numeric = Number(value);
  return Number.isFinite(numeric) && numeric > 0 ? numeric : null;
}

function fuelCapacityNonNegativeNumber(value) {
  if (value === null || value === undefined) return null;
  const numeric = Number(value);
  return Number.isFinite(numeric) && numeric >= 0 ? numeric : null;
}

function fuelCapacityPercent(value) {
  const numeric = fuelCapacityPositiveNumber(value);
  return Number.isFinite(numeric) && numeric <= 1 ? numeric : null;
}

function fuelCapacityLitersLabel(value) {
  return Number.isFinite(value) ? `${value.toFixed(2)} L` : '--';
}

function fuelCapacityPercentLabel(value) {
  return Number.isFinite(value) ? `${(value * 100).toFixed(1)}%` : '--';
}

function fuelCapacitySourceLabel(source) {
  if (source === 'matching-driver-class') return 'driver + class';
  if (source === 'most-restrictive-conflict') return 'min reported';
  if (source === 'physical-only') return 'physical only';
  if (source === 'driver') return 'driver cap';
  if (source === 'class') return 'class cap';
  return '--';
}

function fuelCapacityStateLabel(snapshot) {
  if (snapshot.flags.has('observed-above-cap')) return 'observed above cap; conflict';
  if (snapshot.flags.has('driver-class-conflict')) return 'cap conflict; no advice';
  if (['invalid-physical', 'invalid-driver', 'invalid-class', 'invalid-observed'].some((flag) => snapshot.flags.has(flag))) {
    return 'invalid evidence; no advice';
  }
  if (snapshot.flags.has('missing-physical')) return 'missing physical; no advice';
  if (snapshot.flags.has('missing-cap')) return 'missing cap; no advice';
  return `${snapshot.flags.has('limited') ? 'limited' : 'unrestricted'}; ${snapshot.confidence}`;
}

function fuelCapacityStateTone(snapshot) {
  if (snapshot.confidence === 'conflicted') return 'error';
  if (!snapshot.canDriveFuelAdvice) return 'warning';
  return 'info';
}

function fuelCapacityValueTone(value) {
  return Number.isFinite(value) ? 'info' : 'waiting';
}

function fuelCheckpointWorkbenchGridRow(label, inputs) {
  const snapshot = fuelCheckpointSnapshot(inputs);
  return gridRow(label, [
    fuelCheckpointGridCell(snapshot.effectiveCapacity),
    fuelCheckpointGridCell(snapshot.firstGreen),
    fuelCheckpointGridCell(snapshot.current),
    fuelCheckpointGridCell(snapshot.expectedAtBox),
    fuelCheckpointGridCell(snapshot.serviceComplete),
    fuelCheckpointGridCell(snapshot.expectedPitExit),
    gridCell('service complete', 'info'),
    gridCell(fuelCheckpointStateLabel(snapshot), fuelCheckpointStateTone(snapshot))
  ], fuelCheckpointStateTone(snapshot));
}

function fuelCheckpointSnapshot(inputs) {
  const capacity = fuelCapacitySnapshot(inputs?.capacity || {});
  const flags = new Set();
  const providedInputKinds = fuelCheckpointProvidedInputKinds(inputs);
  const invalidInputKinds = fuelCheckpointInvalidInputKinds(inputs);
  if (!capacity.canDriveFuelAdvice) {
    flags.add(Number.isFinite(capacity.effectiveCapacityLiters) ? 'capacity-conflicted' : 'capacity-unavailable');
  }
  if (invalidInputKinds.size > 0) {
    flags.add('invalid-input');
  }

  let effectiveCapacity = Number.isFinite(capacity.effectiveCapacityLiters)
    ? fuelCheckpointFact(
        capacity.effectiveCapacityLiters,
        'resolved-capacity',
        capacity.confidence === 'authoritative'
          ? 'authoritative'
          : capacity.confidence === 'high'
            ? 'high'
            : 'conflicted')
    : null;
  let firstGreen = fuelCheckpointMeasured(inputs?.measuredFirstGreenFuelLiters, 'measured-first-green');
  if (!firstGreen
      && Number.isFinite(capacity.effectiveCapacityLiters)
      && Number.isFinite(fuelCapacityNonNegativeNumber(inputs?.estimatedFormationFuelLiters))) {
    const rawFirstGreen = capacity.effectiveCapacityLiters - fuelCapacityNonNegativeNumber(inputs?.estimatedFormationFuelLiters);
    firstGreen = fuelCheckpointFact(
      Math.max(0, rawFirstGreen),
      'estimated-capacity-formation',
      capacity.canDriveFuelAdvice ? 'estimated' : 'conflicted',
      rawFirstGreen < 0 ? ['estimated', 'projection-clamped-zero'] : ['estimated']);
  }

  effectiveCapacity = fuelCheckpointApplyCapacity(effectiveCapacity, capacity, flags);
  firstGreen = fuelCheckpointApplyCapacity(firstGreen, capacity, flags);
  const current = fuelCheckpointApplyCapacity(
    fuelCheckpointMeasured(inputs?.currentFuelLiters, 'measured-current'),
    capacity,
    flags);
  const expectedAtBox = fuelCheckpointApplyCapacity(
    fuelCheckpointMeasured(inputs?.measuredAtBoxFuelLiters, 'measured-at-box')
      || fuelCheckpointSubtract(current, inputs?.expectedFuelToBoxLiters, 'projected-current-to-box'),
    capacity,
    flags);
  const serviceComplete = fuelCheckpointApplyCapacity(
    fuelCheckpointMeasured(inputs?.measuredServiceCompleteFuelLiters, 'measured-service-complete')
      || fuelCheckpointAdd(expectedAtBox, inputs?.plannedServiceAddLiters, 'planned-service-add'),
    capacity,
    flags);
  const expectedPitExit = fuelCheckpointApplyCapacity(
    fuelCheckpointMeasured(inputs?.measuredPitExitFuelLiters, 'measured-pit-exit')
      || fuelCheckpointSubtract(serviceComplete, inputs?.expectedBoxToPitExitFuelLiters, 'projected-box-to-exit'),
    capacity,
    flags);

  return {
    capacity,
    effectiveCapacity,
    firstGreen,
    current,
    expectedAtBox,
    serviceComplete,
    expectedPitExit,
    pitRequestTarget: 'service-complete',
    flags,
    providedInputKinds,
    invalidInputKinds
  };
}

function fuelCheckpointMeasured(value, source) {
  const liters = fuelCapacityNonNegativeNumber(value);
  return Number.isFinite(liters) ? fuelCheckpointFact(liters, source, 'measured', ['measured']) : null;
}

function fuelCheckpointSubtract(baseline, consumptionValue, source) {
  const consumption = fuelCapacityNonNegativeNumber(consumptionValue);
  if (!fuelCheckpointCanProjectFrom(baseline) || !Number.isFinite(consumption)) return null;
  const rawValue = baseline.liters - consumption;
  const flags = ['estimated'];
  if (rawValue < 0) flags.push('projection-clamped-zero');
  else if (baseline.confidence === 'conflicted') flags.push('derived-from-conflict');
  return fuelCheckpointFact(
    Math.max(0, rawValue),
    source,
    baseline.confidence === 'conflicted' || rawValue < 0 ? 'conflicted' : 'estimated',
    flags);
}

function fuelCheckpointAdd(baseline, addValue, source) {
  const add = fuelCapacityNonNegativeNumber(addValue);
  if (!fuelCheckpointCanProjectFrom(baseline) || !Number.isFinite(add)) return null;
  return fuelCheckpointFact(
    baseline.liters + add,
    source,
    baseline.confidence === 'conflicted' ? 'conflicted' : 'estimated',
    baseline.confidence === 'conflicted' ? ['estimated', 'derived-from-conflict'] : ['estimated']);
}

function fuelCheckpointCanProjectFrom(fact) {
  return fact
    && Number.isFinite(fact.liters)
    && !fact.flags.has('projection-clamped-zero');
}

function fuelCheckpointFact(liters, source, confidence, flags = []) {
  return { liters, source, confidence, flags: new Set(flags) };
}

function fuelCheckpointApplyCapacity(fact, capacity, snapshotFlags) {
  if (!fact || !Number.isFinite(fact.liters)) return fact;
  if (fact.flags.has('projection-clamped-zero')) snapshotFlags.add('projection-clamped-zero');
  if (fact.flags.has('derived-from-conflict')) snapshotFlags.add('derived-from-conflict');
  if (fact.liters === 0) fact.flags.add('known-zero');
  if (Number.isFinite(capacity.effectiveCapacityLiters)
      && fact.liters > capacity.effectiveCapacityLiters + 0.001) {
    fact.flags.add('above-capacity');
    snapshotFlags.add('above-capacity');
    fact.confidence = 'conflicted';
  }
  return fact;
}

function fuelCheckpointGridCell(fact) {
  if (!fact || !Number.isFinite(fact.liters)) return gridCell('--', 'waiting');
  const confidenceSuffix = fact.confidence === 'authoritative'
    ? ' resolved'
    : fact.confidence === 'high'
      ? ' high'
    : fact.confidence === 'measured'
      ? ' measured'
      : fact.confidence === 'conflicted'
        ? ' conflict'
        : ' est';
  const boundarySuffix = fact.flags.has('projection-clamped-zero') ? ' floor' : '';
  const tone = fact.confidence === 'conflicted'
    ? 'error'
    : fact.confidence === 'estimated'
      ? 'warning'
      : 'info';
  return gridCell(`${fact.liters.toFixed(2)} L${confidenceSuffix}${boundarySuffix}`, tone);
}

function fuelCheckpointInvalidInputKinds(inputs) {
  return new Set(fuelCheckpointInputValues(inputs)
    .filter(([, value]) => value !== null && value !== undefined && !Number.isFinite(fuelCapacityNonNegativeNumber(value)))
    .map(([kind]) => kind));
}

function fuelCheckpointProvidedInputKinds(inputs) {
  return new Set(fuelCheckpointInputValues(inputs)
    .filter(([, value]) => value !== null && value !== undefined)
    .map(([kind]) => kind));
}

function fuelCheckpointInputValues(inputs) {
  return [
    ['measured-first-green-fuel', inputs?.measuredFirstGreenFuelLiters],
    ['estimated-formation-fuel', inputs?.estimatedFormationFuelLiters],
    ['current-fuel', inputs?.currentFuelLiters],
    ['measured-at-box-fuel', inputs?.measuredAtBoxFuelLiters],
    ['expected-fuel-to-box', inputs?.expectedFuelToBoxLiters],
    ['measured-service-complete-fuel', inputs?.measuredServiceCompleteFuelLiters],
    ['planned-service-add', inputs?.plannedServiceAddLiters],
    ['measured-pit-exit-fuel', inputs?.measuredPitExitFuelLiters],
    ['expected-box-to-pit-exit-fuel', inputs?.expectedBoxToPitExitFuelLiters]
  ];
}

function fuelCheckpointStateLabel(snapshot) {
  if (snapshot.flags.has('above-capacity')) return 'above cap; not clamped';
  if (snapshot.flags.has('projection-clamped-zero')) return 'projection below zero; chain stopped';
  if (snapshot.flags.has('derived-from-conflict')) return 'derived from conflicted checkpoint';
  if (snapshot.flags.has('invalid-input')) return 'invalid transition input';
  if (snapshot.flags.has('capacity-conflicted')) return 'capacity conflict';
  if (snapshot.flags.has('capacity-unavailable')) return 'capacity unavailable';
  return 'ordered facts';
}

function fuelCheckpointStateTone(snapshot) {
  if (snapshot.flags.has('above-capacity')
      || snapshot.flags.has('projection-clamped-zero')
      || snapshot.flags.has('derived-from-conflict')
      || snapshot.flags.has('invalid-input')) return 'error';
  if (snapshot.flags.has('capacity-conflicted') || snapshot.flags.has('capacity-unavailable')) return 'warning';
  return 'info';
}

const fuelBoundaryMathematicalTolerance = 0.000000001;

function fuelBoundaryWorkbenchGridRow(label, inputs) {
  const checkpoints = fuelCheckpointSnapshot(inputs);
  const bucketId = inputs?.bucketId;
  if (!fuelV2BurnBucketContract[bucketId]) {
    throw new Error(`Fuel boundary fixture requires an explicit typed bucket ID: ${label}`);
  }
  const rawBurn = inputs?.burnLitersPerLap;
  const rawBurnValue = typeof rawBurn === 'object' && rawBurn !== null ? rawBurn.value : rawBurn;
  const burnWasProvided = rawBurnValue !== null && rawBurnValue !== undefined;
  const burn = fuelV2BurnBucket(bucketId, rawBurn, bucketId === fuelV2BurnBucketId.qualifying
    ? {
        burnSource: 'QualifyingSeed',
        strategyEligible: false,
        cleanBaselineEligible: false,
        confidence: 'Seeded'
      }
    : {});
  const burnState = fuelV2HasTypedBurnEvidence(burn, bucketId)
    ? 'available'
    : burnWasProvided
      ? 'invalid'
      : 'unavailable';
  const cell = fuelBoundaryCell(
    checkpoints,
    burn,
    burnState,
    inputs?.targetLaps,
    inputs?.reserveFuelLiters,
    inputs?.pitLaneFuelLiters);
  const tone = fuelBoundaryStateTone(cell);
  const bucketSuffix = burn.burnSource === 'QualifyingSeed' ? ' seeded' : '';

  return gridRow(label, [
    gridCell(
      `${burn.label}${bucketSuffix}`,
      burnState === 'invalid' ? 'error' : burnState === 'available' ? (burn.strategyEligible ? 'info' : 'warning') : 'waiting'),
    gridCell(fuelBoundaryRangeLabel(cell.fractionalRangeLaps), fuelBoundaryRangeTone(cell.rangeState)),
    gridCell(Number.isInteger(cell.safeWholeLaps) ? `${cell.safeWholeLaps}` : '--', fuelBoundaryRangeTone(cell.rangeState)),
    gridCell(fuelBoundaryLitersLabel(cell.fuelToNextCompleteLapLiters, 5), fuelBoundaryRangeTone(cell.rangeState)),
    gridCell(fuelBoundaryPairLabel(cell.desiredFuelLiters, cell.desiredAddLiters), fuelBoundaryFeasibilityTone(cell.feasibilityState)),
    gridCell(fuelBoundaryPairLabel(cell.tankRoomLiters, cell.clampedAddLiters), fuelBoundaryFeasibilityTone(cell.feasibilityState)),
    gridCell(fuelBoundaryLitersLabel(cell.shortfallLiters, 5), fuelBoundaryFeasibilityTone(cell.feasibilityState)),
    gridCell(Number.isInteger(cell.maximumFeasibleLaps) ? `${cell.maximumFeasibleLaps}` : '--', fuelBoundaryFeasibilityTone(cell.feasibilityState)),
    gridCell(fuelBoundaryStateLabel(cell), tone)
  ], tone);
}

function fuelBoundaryCell(
  checkpoints,
  burn,
  burnState,
  targetLapsInput,
  reserveInput,
  pitLaneInput,
  serviceBaseline = checkpoints?.expectedAtBox) {
  const stateFlags = new Set();
  const result = {
    rangeState: 'unavailable',
    fractionalRangeLaps: null,
    safeWholeLaps: null,
    fuelToNextCompleteLapLiters: null,
    feasibilityState: 'unavailable',
    desiredFuelLiters: null,
    desiredAddLiters: null,
    tankRoomLiters: null,
    clampedAddLiters: null,
    shortfallLiters: null,
    maximumFeasibleLaps: null,
    stateFlags
  };

  const current = checkpoints?.current;
  const rangeInputInvalid = checkpoints?.invalidInputKinds?.has('current-fuel') === true;
  if (rangeInputInvalid || burnState === 'invalid' || (current && !Number.isFinite(current.liters))) {
    result.rangeState = 'invalid';
  } else if (burnState === 'available' && current && Number.isFinite(current.liters) && current.liters >= 0) {
    const fractionalRange = current.liters / burn.value;
    const safeWholeLaps = fuelBoundaryWholeLaps(fractionalRange);
    if (Number.isInteger(safeWholeLaps) && safeWholeLaps < 2147483647) {
      const exactBoundary = Math.abs(fractionalRange - Math.round(fractionalRange)) <= fuelBoundaryMathematicalTolerance;
      result.rangeState = current.liters === 0 ? 'known-zero' : 'available';
      result.fractionalRangeLaps = fractionalRange;
      result.safeWholeLaps = safeWholeLaps;
      result.fuelToNextCompleteLapLiters = Math.max(0, (safeWholeLaps + 1) * burn.value - current.liters);
      if (!Number.isFinite(result.fuelToNextCompleteLapLiters)) {
        result.rangeState = 'invalid';
        result.fractionalRangeLaps = null;
        result.safeWholeLaps = null;
        result.fuelToNextCompleteLapLiters = null;
      } else {
        if (exactBoundary) stateFlags.add('exact-lap-boundary');
        if (current.liters === 0) stateFlags.add('known-zero-range');
      }
    } else {
      result.rangeState = 'invalid';
    }
  }

  const reserve = fuelPitRequestNonNegative(reserveInput);
  const pitLane = fuelPitRequestNonNegative(pitLaneInput);
  const targetLaps = targetLapsInput === null || targetLapsInput === undefined
    ? null
    : Number(targetLapsInput);
  if (burnState === 'invalid'
      || targetLaps !== null && (!Number.isInteger(targetLaps) || targetLaps <= 0)
      || !Number.isFinite(reserve)
      || !Number.isFinite(pitLane)) {
    result.feasibilityState = 'invalid';
    return result;
  }
  if (burnState !== 'available' || targetLaps === null) return result;

  const desiredFuel = targetLaps * burn.value + reserve + pitLane;
  if (!Number.isFinite(desiredFuel) || desiredFuel < 0) {
    result.feasibilityState = 'invalid';
    return result;
  }
  result.desiredFuelLiters = desiredFuel;

  const capacity = checkpoints?.capacity;
  const capacityValue = capacity?.effectiveCapacityLiters;
  const capacityInputInvalid = fuelBoundaryCapacityInputInvalid(capacity);
  const serviceInputInvalid = fuelBoundaryServiceInputInvalid(checkpoints);
  if (!capacityInputInvalid && Number.isFinite(capacityValue) && capacityValue > 0) {
    result.maximumFeasibleLaps = fuelBoundaryWholeLaps(Math.max(0, capacityValue - reserve - pitLane) / burn.value);
  }

  const atBox = serviceBaseline;
  if (!atBox) {
    if (serviceInputInvalid || capacityInputInvalid) result.feasibilityState = 'invalid';
    return result;
  }
  if (serviceInputInvalid || !Number.isFinite(atBox.liters) || atBox.liters < 0) {
    result.feasibilityState = 'invalid';
    return result;
  }

  if (atBox.liters === 0) stateFlags.add('known-zero-service');
  result.desiredAddLiters = Math.max(0, desiredFuel - atBox.liters);
  if (result.desiredAddLiters === 0) stateFlags.add('target-already-covered');
  if (capacityInputInvalid) {
    result.feasibilityState = 'invalid';
    return result;
  }
  if (!Number.isFinite(capacityValue)) return result;
  if (capacityValue <= 0) {
    result.feasibilityState = 'invalid';
    return result;
  }

  result.tankRoomLiters = Math.max(0, capacityValue - atBox.liters);
  result.clampedAddLiters = Math.min(result.desiredAddLiters, result.tankRoomLiters);
  result.shortfallLiters = Math.max(0, result.desiredAddLiters - result.clampedAddLiters);
  if (result.shortfallLiters > fuelBoundaryMathematicalTolerance) stateFlags.add('tank-limited');

  const checkpointConflicted = atBox.confidence === 'conflicted'
    || atBox.flags.has('above-capacity')
    || atBox.flags.has('derived-from-conflict')
    || atBox.flags.has('projection-clamped-zero');
  const capacityConflicted = !capacity.canDriveFuelAdvice
    || atBox.liters > capacityValue + fuelBoundaryMathematicalTolerance
    || checkpointConflicted;
  result.feasibilityState = capacityConflicted
    ? 'capacity-conflicted'
    : result.shortfallLiters > fuelBoundaryMathematicalTolerance
      ? 'unachievable'
      : 'feasible';
  return result;
}

function fuelV2HasTypedBurnEvidence(burn, expectedBucketId) {
  return Number.isFinite(burn?.value)
    && burn.value > 0
    && burn.id === expectedBucketId
    && fuelV2BurnBucketContract[burn.id]
    && burn.burnSource !== 'Unavailable';
}

function fuelBoundaryServiceInputInvalid(checkpoints) {
  return fuelSharedCheckpointSelection(checkpoints, 'expectedAtBox').state === 'invalid';
}

function fuelBoundaryCapacityInputInvalid(capacity) {
  return ['invalid-physical', 'invalid-driver', 'invalid-class', 'invalid-observed']
    .some((flag) => capacity?.flags?.has(flag));
}

function fuelBoundaryWholeLaps(fractionalLaps) {
  if (!Number.isFinite(fractionalLaps) || fractionalLaps < 0 || fractionalLaps > 2147483647) return null;
  const nearestWhole = Math.round(fractionalLaps);
  return Math.abs(fractionalLaps - nearestWhole) <= fuelBoundaryMathematicalTolerance
    ? nearestWhole
    : Math.floor(fractionalLaps);
}

function fuelBoundaryRangeLabel(value) {
  return Number.isFinite(value) ? `${value.toFixed(6)} laps` : '--';
}

function fuelBoundaryLitersLabel(value, precision = 2) {
  return Number.isFinite(value) ? `${value.toFixed(precision)} L` : '--';
}

function fuelBoundaryPairLabel(first, second) {
  return `${fuelBoundaryLitersLabel(first)} / ${fuelBoundaryLitersLabel(second)}`;
}

function fuelBoundaryStateLabel(cell) {
  const flags = [...cell.stateFlags];
  const flagLabel = flags.length > 0 ? `; ${flags.join(', ')}` : '';
  return `range: ${cell.rangeState}; target: ${cell.feasibilityState}${flagLabel}`;
}

function fuelBoundaryRangeTone(state) {
  if (state === 'invalid') return 'error';
  if (state === 'unavailable') return 'waiting';
  return 'info';
}

function fuelBoundaryFeasibilityTone(state) {
  if (state === 'invalid' || state === 'capacity-conflicted' || state === 'unachievable') return 'error';
  if (state === 'unavailable') return 'waiting';
  return 'info';
}

function fuelBoundaryStateTone(cell) {
  const feasibilityTone = fuelBoundaryFeasibilityTone(cell.feasibilityState);
  if (feasibilityTone === 'error' || cell.rangeState === 'invalid') return 'error';
  if (feasibilityTone === 'waiting' || cell.rangeState === 'unavailable') return 'warning';
  return 'info';
}

function fuelSharedSnapshotWorkbenchGridRow(label, inputs) {
  const snapshot = fuelSharedSnapshot(inputs);
  const lastBoundary = snapshot.boundaryByBucket[fuelV2BurnBucketId.last];
  const selectedTarget = snapshot.targetUsage.targets.find((target) => target.targetLaps === 5)
    || snapshot.targetUsage.targets[0];
  const targetSelectionState = snapshot.targetUsage.fuelBudgetSelection.state;
  const targetSelectionSuffix = targetSelectionState === 'available'
    ? ''
    : ` [${targetSelectionState}]`;
  const planFuelState = snapshot.planDependencies?.fuelBudget?.state;
  const futurePlanFuelState = snapshot.planDependencies?.futureFuelBudget?.state;
  const planSelectionSuffix = planFuelState && planFuelState !== 'available'
    ? ` [${planFuelState}]`
    : futurePlanFuelState && futurePlanFuelState !== 'available'
      ? ` [future ${futurePlanFuelState}]`
      : '';
  const planLabel = snapshot.plan
    ? `${snapshot.plan.rhythmLabel} / ${snapshot.plan.stopsLabel} stops${planSelectionSuffix}`
    : '--';
  const tone = fuelSharedSnapshotTone(snapshot);

  return gridRow(label, [
    gridCell(fuelSharedLapBudgetLabel(snapshot.lapBudget), fuelSharedLapBudgetTone(snapshot.lapBudget)),
    gridCell(fuelCapacityLitersLabel(snapshot.capacity.effectiveCapacityLiters), fuelCapacityStateTone(snapshot.capacity)),
    gridCell(
      `${fuelBoundaryLitersLabel(snapshot.checkpoints.current?.liters)} / ${fuelBoundaryLitersLabel(snapshot.checkpoints.expectedAtBox?.liters)}`,
      snapshot.checkpoints.current || snapshot.checkpoints.expectedAtBox ? 'info' : 'waiting'),
    gridCell(fuelBoundaryRangeLabel(lastBoundary.fractionalRangeLaps), fuelBoundaryRangeTone(lastBoundary.rangeState)),
    gridCell(
      fuelBoundaryLitersLabel(snapshot.pitRequest?.last?.clampedAddLiters ?? snapshot.pitRequest?.last?.desiredAddLiters),
      fuelBoundaryFeasibilityTone(lastBoundary.feasibilityState)),
    gridCell(
      selectedTarget
        ? `${fuelBoundaryLitersLabel(snapshot.targetUsage.fuelBudgetLiters)} / ${fuelRangeFuelPerLap(selectedTarget.requiredFuelPerLap)} @${selectedTarget.targetLaps}${targetSelectionSuffix}`
        : '--',
      selectedTarget ? fuelTargetUsageTone(selectedTarget.requiredFuelPerLap, snapshot.targetUsage.referenceBurn?.value) : 'waiting'),
    gridCell(planLabel, snapshot.plan?.tone || 'waiting'),
    gridCell(inputs.contractLabel || 'explicit composition', tone)
  ], tone);
}

function fuelSharedSnapshot(inputs) {
  if (!inputs?.lapBudget || !inputs?.checkpoints || !inputs?.burns || !inputs?.boundary || !inputs?.targetUsage) {
    throw new Error('Fuel shared snapshot fixture requires explicit lap, checkpoint, burn, boundary, and target-usage owners.');
  }
  if (!Object.prototype.hasOwnProperty.call(inputs.boundary, 'targetLaps')
      || !Object.prototype.hasOwnProperty.call(inputs.boundary, 'reserveFuelLiters')
      || !Object.prototype.hasOwnProperty.call(inputs.boundary, 'pitLaneFuelLiters')) {
    throw new Error('Fuel shared snapshot boundary requires explicit target, reserve, and pit-lane inputs.');
  }
  if (!Array.isArray(inputs.targetUsage.targetLaps)) {
    throw new Error('Fuel shared snapshot target usage requires explicit target laps.');
  }

  const checkpoints = fuelCheckpointSnapshot(inputs.checkpoints);
  const burnBuckets = Object.fromEntries(fuelV2BurnBucketOrder.map((bucketId) => {
    const rawWasProvided = Object.prototype.hasOwnProperty.call(inputs.burns, bucketId);
    const bucket = fuelV2BurnBucket(bucketId, inputs.burns[bucketId]);
    return [bucketId, {
      bucket,
      state: fuelV2HasTypedBurnEvidence(bucket, bucketId)
        ? 'available'
        : rawWasProvided
          ? 'invalid'
          : 'unavailable'
    }];
  }));
  const boundaryByBucket = Object.fromEntries(fuelV2BurnBucketOrder.map((bucketId) => {
    const burn = burnBuckets[bucketId];
    return [bucketId, fuelBoundaryCell(
      checkpoints,
      burn.bucket,
      burn.state,
      inputs.boundary.targetLaps,
      inputs.boundary.reserveFuelLiters,
      inputs.boundary.pitLaneFuelLiters)];
  }));
  const pitRequest = inputs.boundary.targetLaps === null || inputs.boundary.targetLaps === undefined
    ? null
    : {
        last: fuelSharedPitRequestCell(boundaryByBucket[fuelV2BurnBucketId.last], burnBuckets[fuelV2BurnBucketId.last]),
        fiveLapAverage: fuelSharedPitRequestCell(boundaryByBucket[fuelV2BurnBucketId.fiveLapAverage], burnBuckets[fuelV2BurnBucketId.fiveLapAverage]),
        tenLapAverage: fuelSharedPitRequestCell(boundaryByBucket[fuelV2BurnBucketId.tenLapAverage], burnBuckets[fuelV2BurnBucketId.tenLapAverage]),
        maximum: fuelSharedPitRequestCell(boundaryByBucket[fuelV2BurnBucketId.maximum], burnBuckets[fuelV2BurnBucketId.maximum]),
        minimum: fuelSharedPitRequestCell(boundaryByBucket[fuelV2BurnBucketId.minimum], burnBuckets[fuelV2BurnBucketId.minimum]),
        qualifying: fuelSharedPitRequestCell(boundaryByBucket[fuelV2BurnBucketId.qualifying], burnBuckets[fuelV2BurnBucketId.qualifying])
      };
  const targetBudget = fuelSharedCheckpointSelection(checkpoints, inputs.targetUsage.fuelBudgetCheckpoint);
  const targetReference = fuelSharedBurnBucket(burnBuckets, inputs.targetUsage.referenceBurnBucketId);
  const targetBudgetLiters = Number.isFinite(targetBudget.calculationLiters) && targetBudget.calculationLiters > 0
    ? targetBudget.calculationLiters
    : null;
  const targetUsage = {
    fuelBudgetLiters: targetBudgetLiters,
    fuelBudgetSelection: targetBudget,
    referenceBurn: targetReference,
    targets: fuelSharedExplicitTargetLaps(inputs.targetUsage.targetLaps).map((targetLaps) => ({
      targetLaps,
      requiredFuelPerLap: fuelTargetUsageRequiredBurn(targetBudgetLiters, targetLaps)
    }))
  };

  const plan = fuelSharedPlan(inputs.plan, inputs.lapBudget, checkpoints, burnBuckets);
  return {
    lapBudget: inputs.lapBudget,
    capacity: checkpoints.capacity,
    checkpoints,
    burnBuckets,
    boundaryByBucket,
    range: {
      last: boundaryByBucket[fuelV2BurnBucketId.last].fractionalRangeLaps,
      fiveLapAverage: boundaryByBucket[fuelV2BurnBucketId.fiveLapAverage].fractionalRangeLaps,
      tenLapAverage: boundaryByBucket[fuelV2BurnBucketId.tenLapAverage].fractionalRangeLaps,
      maximum: boundaryByBucket[fuelV2BurnBucketId.maximum].fractionalRangeLaps
    },
    pitRequest,
    targetUsage,
    plan: plan.snapshot,
    planComposition: inputs.plan,
    planDependencies: plan.dependencies
  };
}

function fuelSharedPitRequestCell(boundary, burn) {
  const add = boundary?.clampedAddLiters ?? boundary?.desiredAddLiters;
  return burn?.state === 'available'
    && Number.isFinite(add)
    && Number.isFinite(boundary?.desiredFuelLiters)
    && boundary.feasibilityState !== 'invalid'
      ? boundary
      : null;
}

function fuelSharedPlan(plan, lapBudget, checkpoints, burnBuckets) {
  if (!plan) return { snapshot: null, dependencies: null };
  const plannedRaceLaps = fuelSharedLapValue(lapBudget, plan.plannedRaceLapsSource);
  const raceLapsRemaining = fuelSharedLapValue(lapBudget, plan.raceLapsRemainingSource);
  const flags = [...new Set([
    ...(plan.flags || []),
    ...(lapBudget?.actionableSource === 'TimedLiveClockHeldCleanPace' ? ['held'] : []),
    ...(lapBudget?.canDriveFuelAdvice === false ? ['degraded'] : [])
  ])];
  if (plan.mode === 'full-race') {
    const budget = fuelSharedCheckpointSelection(checkpoints, plan.fuelBudgetCheckpoint);
    const burn = fuelSharedBurnBucket(burnBuckets, plan.burnBucketId);
    return {
      snapshot: fuelPlanV2Snapshot({
        raceLaps: plannedRaceLaps,
        remainLaps: raceLapsRemaining,
        usableFuelLiters: budget.calculationLiters,
        burnLitersPerLap: burn,
        flags
      }),
      dependencies: {
        mode: plan.mode,
        plannedRaceLapsSource: plan.plannedRaceLapsSource,
        plannedRaceLaps,
        raceLapsRemainingSource: plan.raceLapsRemainingSource,
        raceLapsRemaining,
        fuelBudget: budget,
        burnBucketId: plan.burnBucketId,
        burn,
        futureFuelBudget: null,
        futureBurnBucketId: null,
        futureBurn: null
      }
    };
  }
  if (plan.mode === 'current-checkpoint') {
    const currentFuel = fuelSharedCheckpointSelection(checkpoints, plan.currentFuelCheckpoint);
    const currentBurn = fuelSharedBurnBucket(burnBuckets, plan.currentBurnBucketId);
    const futureFuel = fuelSharedCheckpointSelection(checkpoints, plan.futureFuelCheckpoint);
    const futureBurn = fuelSharedBurnBucket(burnBuckets, plan.futureBurnBucketId);
    return {
      snapshot: fuelPlanV2CurrentSnapshot({
        raceLaps: plannedRaceLaps,
        remainLaps: raceLapsRemaining,
        currentFuelLiters: currentFuel.calculationLiters,
        currentBurnLitersPerLap: currentBurn,
        futureFuelLiters: futureFuel.calculationLiters,
        futureBurnLitersPerLap: futureBurn,
        flags
      }),
      dependencies: {
        mode: plan.mode,
        plannedRaceLapsSource: plan.plannedRaceLapsSource,
        plannedRaceLaps,
        raceLapsRemainingSource: plan.raceLapsRemainingSource,
        raceLapsRemaining,
        fuelBudget: currentFuel,
        burnBucketId: plan.currentBurnBucketId,
        burn: currentBurn,
        futureFuelBudget: futureFuel,
        futureBurnBucketId: plan.futureBurnBucketId,
        futureBurn
      }
    };
  }
  throw new Error(`Unknown Fuel shared snapshot plan mode: ${plan.mode}`);
}

function fuelSharedCheckpoint(checkpoints, checkpointName) {
  const checkpointByName = {
    effectiveCapacity: checkpoints.effectiveCapacity,
    firstGreen: checkpoints.firstGreen,
    current: checkpoints.current,
    expectedAtBox: checkpoints.expectedAtBox,
    serviceComplete: checkpoints.serviceComplete,
    expectedPitExit: checkpoints.expectedPitExit
  };
  return Object.prototype.hasOwnProperty.call(checkpointByName, checkpointName)
    ? checkpointByName[checkpointName]
    : null;
}

function fuelSharedCheckpointSelection(checkpoints, checkpointName) {
  const checkpoint = fuelSharedCheckpoint(checkpoints, checkpointName);
  const dependencyState = fuelSharedCheckpointDependencyState(checkpoints, checkpointName, checkpoint);
  const conflicted = checkpoint?.confidence === 'conflicted'
    || ['above-capacity', 'derived-from-conflict', 'projection-clamped-zero', 'invalid-input']
      .some((flag) => checkpoint?.flags?.has(flag));
  const state = dependencyState
    ? dependencyState
    : !checkpoint
      ? 'unavailable'
      : !Number.isFinite(checkpoint.liters) || checkpoint.liters < 0
        ? 'invalid'
        : conflicted
          ? 'conflicted'
          : 'available';
  return {
    checkpointName,
    checkpoint,
    state,
    calculationLiters: state === 'available' ? checkpoint.liters : null,
    sourceLabel: fuelSharedCheckpointSourceLabel(checkpointName, checkpoint, state)
  };
}

function fuelSharedCheckpointDependencyState(checkpoints, checkpointName, checkpoint) {
  const invalid = checkpoints?.invalidInputKinds || new Set();
  const provided = checkpoints?.providedInputKinds || new Set();
  const blockedState = (selection) => ['invalid', 'conflicted'].includes(selection.state)
    ? selection.state
    : null;
  if (checkpointName === 'effectiveCapacity') {
    return ['invalid-physical', 'invalid-driver', 'invalid-class', 'invalid-observed']
      .some((flag) => checkpoints?.capacity?.flags?.has(flag))
        ? 'invalid'
        : null;
  }
  if (checkpointName === 'firstGreen') {
    return checkpoint?.source === 'measured-first-green'
      ? null
      : (invalid.has('measured-first-green-fuel')
        || invalid.has('estimated-formation-fuel'))
          ? 'invalid'
          : provided.has('estimated-formation-fuel')
            ? blockedState(fuelSharedCheckpointSelection(checkpoints, 'effectiveCapacity'))
            : null;
  }
  if (checkpointName === 'current') return invalid.has('current-fuel') ? 'invalid' : null;
  if (checkpointName === 'expectedAtBox') {
    return checkpoint?.source === 'measured-at-box'
      ? null
      : (invalid.has('measured-at-box-fuel')
        || invalid.has('expected-fuel-to-box'))
          ? 'invalid'
          : provided.has('expected-fuel-to-box')
            ? blockedState(fuelSharedCheckpointSelection(checkpoints, 'current'))
            : null;
  }
  if (checkpointName === 'serviceComplete') {
    if (checkpoint?.source === 'measured-service-complete') return null;
    if (invalid.has('measured-service-complete-fuel') || invalid.has('planned-service-add')) return 'invalid';
    return provided.has('planned-service-add')
      ? blockedState(fuelSharedCheckpointSelection(checkpoints, 'expectedAtBox'))
      : null;
  }
  if (checkpointName === 'expectedPitExit') {
    if (checkpoint?.source === 'measured-pit-exit') return null;
    if (invalid.has('measured-pit-exit-fuel') || invalid.has('expected-box-to-pit-exit-fuel')) return 'invalid';
    return provided.has('expected-box-to-pit-exit-fuel')
      ? blockedState(fuelSharedCheckpointSelection(checkpoints, 'serviceComplete'))
      : null;
  }
  return 'invalid';
}

function fuelSharedCheckpointSourceLabel(checkpointName, checkpoint, state) {
  const sourceLabels = {
    'resolved-capacity': 'resolved effective capacity',
    'measured-first-green': 'measured first-green telemetry',
    'estimated-capacity-formation': 'estimated capacity minus formation fuel',
    'measured-current': 'measured current telemetry',
    'measured-at-box': 'measured at-box telemetry',
    'projected-current-to-box': 'projected current-to-box fuel',
    'measured-service-complete': 'measured service-complete telemetry',
    'planned-service-add': 'planned service-complete fuel',
    'measured-pit-exit': 'measured pit-exit telemetry',
    'projected-box-to-exit': 'projected pit-exit fuel'
  };
  const source = sourceLabels[checkpoint?.source] || `${checkpointName} fuel`;
  if (state === 'invalid') return `${source}; invalid input`;
  if (state === 'conflicted') return `${source}; conflicted`;
  if (state === 'unavailable') return `${checkpointName} fuel unavailable`;
  return source;
}

function fuelSharedLapValue(lapBudget, valueKind) {
  const rawValue = {
    primaryLapsRemaining: lapBudget?.primaryLapsRemaining,
    possibleLapsRemaining: lapBudget?.possibleLapsRemaining,
    estimatedFinishLap: lapBudget?.estimatedFinishLap
  }[valueKind];
  if (rawValue === null || rawValue === undefined) return null;
  const value = Number(rawValue);
  return Number.isFinite(value) && value >= 0 ? value : null;
}

function fuelSharedBurnBucket(burnBuckets, bucketId) {
  const selected = burnBuckets[bucketId];
  return selected?.state === 'available' ? selected.bucket : null;
}

function fuelSharedExplicitTargetLaps(targetLaps) {
  return [...new Set(targetLaps
    .map((laps) => Number(laps))
    .filter((laps) => Number.isInteger(laps) && laps > 0))]
    .sort((left, right) => left - right);
}

function fuelSharedLapBudgetLabel(lapBudget) {
  const primary = Number.isInteger(lapBudget?.primaryLapsRemaining)
    ? `${lapBudget.primaryLapsRemaining}`
    : '--';
  const possible = Number.isFinite(lapBudget?.possibleLapsRemaining)
    ? lapBudget.possibleLapsRemaining.toFixed(2)
    : '--';
  return `${primary} / ${possible}`;
}

function fuelSharedLapBudgetTone(lapBudget) {
  return Number.isInteger(lapBudget?.primaryLapsRemaining) || Number.isFinite(lapBudget?.possibleLapsRemaining)
    ? 'info'
    : 'waiting';
}

function fuelSharedSnapshotTone(snapshot) {
  const last = snapshot.boundaryByBucket[fuelV2BurnBucketId.last];
  if (last.rangeState === 'invalid'
      || last.feasibilityState === 'invalid'
      || last.feasibilityState === 'capacity-conflicted'
      || last.feasibilityState === 'unachievable') return 'error';
  if (!Number.isFinite(snapshot.capacity.effectiveCapacityLiters)
      && last.rangeState === 'unavailable'
      && !snapshot.plan) return 'waiting';
  if (last.rangeState === 'unavailable'
      || last.feasibilityState === 'unavailable') return 'warning';
  return 'info';
}

function fuelLapsWorkbenchRow(label, value, tone, checkpointValues, realValue) {
  const checkpointLabels = ['Start', 'Mid S1', 'Stop 1', 'Recover', 'Half Rem'];
  const segments = checkpointLabels
    .map((label, index) => metricSegment(
      label,
      fuelLapsWorkbenchFormatCell(checkpointValues[index]),
      fuelLapsWorkbenchTone(checkpointValues[index], realValue)));
  segments.push(metricSegment('Real', fuelLapsWorkbenchFormatCell(realValue), 'modeled'));

  return metricRow(
    label,
    value,
    tone,
    segments);
}

function fuelPlanWorkbenchRow(label, value, tone, plan) {
  const segments = [
    metricSegment('Race', plan?.race || '--', plan?.raceTone || 'info'),
    metricSegment('Remain', plan?.remain || '--', plan?.remainTone || 'info'),
    metricSegment('Stints', plan?.stints || '--', plan?.stintsTone || 'info'),
    metricSegment('Stops', plan?.stops || '--', plan?.stopsTone || 'info'),
    metricSegment('Save', plan?.save || '--', plan?.saveTone || 'success')
  ];

  return metricRow(
    label,
    value,
    tone,
    segments);
}

function fuelStintTargetV2WorkbenchGridRow(label, target) {
  const snapshot = fuelStintTargetV2Snapshot(target);
  const cells = [
    gridCell(fuelPlanNumberLabel(snapshot.remainingLaps), fuelStintTargetContextTone(snapshot)),
    gridCell(fuelStintTargetRangeLabel(snapshot.currentRangeLaps), fuelStintTargetRangeTone(snapshot)),
    fuelStintTargetCandidateCell(snapshot, 'short'),
    fuelStintTargetCandidateCell(snapshot, 'plan'),
    fuelStintTargetCandidateCell(snapshot, 'stretch'),
    fuelStintTargetCandidateCell(snapshot, 'extra'),
    gridCell(fuelStintTargetBurnLabel(snapshot.referenceBurn, snapshot.referenceLabel), fuelStintTargetBurnTone(snapshot)),
    gridCell(snapshot.statusLabel, snapshot.statusTone)
  ];

  return {
    label,
    tone: snapshot.statusTone,
    cells
  };
}

function fuelStintTargetV2Snapshot(target) {
  const rawReferenceBurn = fuelPlanBurnValue(target?.referenceBurnLitersPerLap);
  const referenceBurn = Number.isFinite(rawReferenceBurn) && rawReferenceBurn > 0
    ? rawReferenceBurn
    : null;
  const currentFuel = fuelPlanNonNegativeNumber(target?.currentFuelLiters);
  const reserveFuel = fuelPlanNonNegativeNumber(target?.reserveFuelLiters) || 0;
  const pitLaneFuel = fuelPlanNonNegativeNumber(target?.pitLaneFuelLiters) || 0;
  const usableFuel = Number.isFinite(currentFuel)
    ? Math.max(0, currentFuel - reserveFuel - pitLaneFuel)
    : null;
  const remainingLaps = fuelPlanNonNegativeNumber(target?.remainingLaps);
  const fallbackTarget = fuelStintTargetDefaultTargetLaps(usableFuel, referenceBurn, remainingLaps);
  const targetLaps = remainingLaps === 0
    ? null
    : fuelStintTargetPositiveInteger(target?.targetLaps) || fallbackTarget;
  const currentRangeLaps = Number.isFinite(usableFuel) && Number.isFinite(referenceBurn) && referenceBurn > 0
    ? usableFuel / referenceBurn
    : null;
  const candidateLaps = fuelStintTargetCandidateLaps(targetLaps, target?.candidateTargetLaps);
  const candidates = candidateLaps.map((laps) => fuelStintTargetCandidate(
    usableFuel,
    referenceBurn,
    remainingLaps,
    targetLaps,
    laps,
    target));
  const targetCandidate = candidates.find((candidate) => candidate.role === 'plan');
  const flags = new Set(target?.flags || []);
  const status = remainingLaps === 0
    ? { label: 'finished', tone: 'info' }
    : fuelStintTargetStatus(targetCandidate, currentRangeLaps, targetLaps, flags, target?.planLabel);

  return {
    currentFuel,
    usableFuel,
    remainingLaps,
    referenceBurn,
    referenceLabel: fuelPitRequestBurnLabel(target?.referenceBurnLitersPerLap),
    targetLaps,
    currentRangeLaps,
    candidates,
    statusLabel: status.label,
    statusTone: status.tone,
    flags
  };
}

function fuelStintTargetDefaultTargetLaps(usableFuel, referenceBurn, remainingLaps) {
  if (remainingLaps === 0) {
    return null;
  }

  if (!Number.isFinite(usableFuel) || !Number.isFinite(referenceBurn) || referenceBurn <= 0) {
    return null;
  }

  const projected = Math.max(1, Math.ceil(usableFuel / referenceBurn - 0.000001));
  return Number.isFinite(remainingLaps) && remainingLaps > 0
    ? Math.max(1, Math.min(projected, Math.ceil(remainingLaps)))
    : projected;
}

function fuelStintTargetCandidateLaps(targetLaps, candidateTargetLaps) {
  if (Array.isArray(candidateTargetLaps)) {
    const explicit = candidateTargetLaps
      .map((value) => fuelStintTargetPositiveInteger(value))
      .filter((value) => Number.isFinite(value) && value > 0);
    if (explicit.length > 0) {
      return [...new Set(explicit)].sort((left, right) => left - right);
    }
  }

  if (!Number.isFinite(targetLaps) || targetLaps <= 0) {
    return [];
  }

  return [targetLaps - 1, targetLaps, targetLaps + 1, targetLaps + 2]
    .filter((laps) => laps > 0)
    .filter((laps, index, values) => values.indexOf(laps) === index);
}

function fuelStintTargetCandidate(usableFuel, referenceBurn, remainingLaps, plannedTargetLaps, candidateLaps, target) {
  const laps = fuelStintTargetPositiveInteger(candidateLaps);
  const requiredBurn = Number.isFinite(usableFuel) && usableFuel >= 0 && Number.isFinite(laps) && laps > 0
    ? usableFuel / laps
    : null;
  const saveRequired = Number.isFinite(requiredBurn) && Number.isFinite(referenceBurn)
    ? Math.max(0, referenceBurn - requiredBurn)
    : null;
  const offset = Number.isFinite(laps) && Number.isFinite(plannedTargetLaps)
    ? laps - plannedTargetLaps
    : null;
  const role = fuelStintTargetCandidateRole(offset, target?.candidateTargetLaps);
  const strategyDeltaSeconds = fuelStintTargetStrategyDeltaSeconds(target, laps);
  const visibility = fuelStintTargetCandidateVisibility(
    requiredBurn,
    referenceBurn,
    remainingLaps,
    plannedTargetLaps,
    laps,
    role,
    strategyDeltaSeconds);
  let tone = fuelStintTargetTone(requiredBurn, referenceBurn);
  if (Number.isFinite(strategyDeltaSeconds) && strategyDeltaSeconds < -0.001 && tone !== 'error') {
    tone = 'warning';
  }
  const flags = new Set(target?.flags || []);
  if (fuelStintTargetHasContextFlag(flags) && tone === 'success') {
    tone = 'warning';
  }

  return {
    offset,
    role,
    laps,
    requiredBurn,
    referenceBurn,
    saveRequired,
    strategyDeltaSeconds,
    displayEligible: visibility.displayEligible,
    reasonLabel: visibility.reasonLabel,
    tone
  };
}

function fuelStintTargetCandidateRole(offset, candidateTargetLaps) {
  if (Array.isArray(candidateTargetLaps) && (offset < -1 || offset > 2)) {
    return 'custom';
  }

  if (offset < 0) return 'short';
  if (offset === 0) return 'plan';
  if (offset === 1) return 'stretch';
  if (offset === 2) return 'extra';
  return 'custom';
}

function fuelStintTargetStrategyDeltaSeconds(target, laps) {
  if (!Number.isFinite(laps)) {
    return null;
  }

  const rawContexts = target?.targetTimeContexts || {};
  const context = rawContexts[laps] || rawContexts[String(laps)] || null;
  const stopAvoidanceSeconds = fuelPlanNonNegativeNumber(context?.stopAvoidanceSeconds);
  const paceLossSeconds = fuelPlanNonNegativeNumber(context?.paceLossSeconds);
  return Number.isFinite(stopAvoidanceSeconds) && Number.isFinite(paceLossSeconds)
    ? stopAvoidanceSeconds - paceLossSeconds
    : null;
}

function fuelStintTargetCandidateVisibility(
  requiredBurn,
  referenceBurn,
  remainingLaps,
  plannedTargetLaps,
  laps,
  role,
  strategyDeltaSeconds) {
  if (!Number.isFinite(requiredBurn)) {
    return { displayEligible: false, reasonLabel: 'learning' };
  }

  if (Number.isFinite(remainingLaps) && remainingLaps > 0 && laps > Math.ceil(remainingLaps + 0.000001)) {
    return { displayEligible: false, reasonLabel: 'past finish' };
  }

  if (fuelStintTargetIsUnrealistic(requiredBurn, referenceBurn)) {
    return { displayEligible: false, reasonLabel: 'unrealistic' };
  }

  if (Number.isFinite(strategyDeltaSeconds) && strategyDeltaSeconds < -0.001) {
    return { displayEligible: false, reasonLabel: 'not worth time' };
  }

  if (role === 'short'
    && Number.isFinite(referenceBurn)
    && requiredBurn >= referenceBurn
    && laps < plannedTargetLaps) {
    return { displayEligible: false, reasonLabel: 'safe short' };
  }

  if (Number.isFinite(strategyDeltaSeconds) && strategyDeltaSeconds > 0.001) {
    return { displayEligible: true, reasonLabel: 'time gain' };
  }

  if (fuelStintTargetIsBigSave(requiredBurn, referenceBurn)) {
    return { displayEligible: true, reasonLabel: 'large save' };
  }

  if (Number.isFinite(referenceBurn) && referenceBurn > 0 && requiredBurn < referenceBurn) {
    return { displayEligible: true, reasonLabel: 'save' };
  }

  return { displayEligible: true, reasonLabel: 'tracking' };
}

function fuelStintTargetCandidateCell(snapshot, role) {
  const candidate = snapshot.candidates.find((item) => item.role === role);
  if (!candidate || !Number.isFinite(candidate.laps) || candidate.laps <= 0) {
    return gridCell('--', 'waiting');
  }

  const label = fuelStintTargetCandidateLabel(candidate);
  return gridCell(label, candidate.tone);
}

function fuelStintTargetCandidateLabel(candidate) {
  if (!Number.isFinite(candidate.requiredBurn)) {
    return `${candidate.laps}: --`;
  }

  if (!candidate.displayEligible) {
    if (candidate.reasonLabel === 'not worth time' && Number.isFinite(candidate.strategyDeltaSeconds)) {
      return `${candidate.laps}: not worth +${Math.abs(candidate.strategyDeltaSeconds).toFixed(0)}s`;
    }

    return `${candidate.laps}: hide ${candidate.reasonLabel}`;
  }

  if (!Number.isFinite(candidate.referenceBurn) || candidate.referenceBurn <= 0) {
    return `${candidate.laps}: ${candidate.requiredBurn.toFixed(2)}`;
  }

  if (Number.isFinite(candidate.strategyDeltaSeconds) && candidate.strategyDeltaSeconds > 0.001) {
    return `${candidate.laps}: ${candidate.requiredBurn.toFixed(2)} worth ${candidate.strategyDeltaSeconds.toFixed(0)}s`;
  }

  if (fuelStintTargetIsUnrealistic(candidate.requiredBurn, candidate.referenceBurn)) {
    return `${candidate.laps}: ${candidate.requiredBurn.toFixed(2)} unrealistic`;
  }

  if (fuelStintTargetIsBigSave(candidate.requiredBurn, candidate.referenceBurn)) {
    return `${candidate.laps}: ${candidate.requiredBurn.toFixed(2)} big save`;
  }

  if (Number.isFinite(candidate.saveRequired) && candidate.saveRequired > 0.005) {
    return `${candidate.laps}: ${candidate.requiredBurn.toFixed(2)} save ${candidate.saveRequired.toFixed(2)}`;
  }

  return `${candidate.laps}: ${candidate.requiredBurn.toFixed(2)} ok`;
}

function fuelStintTargetTone(requiredBurn, referenceBurn) {
  if (!Number.isFinite(requiredBurn)) return 'waiting';
  if (!Number.isFinite(referenceBurn) || referenceBurn <= 0) return 'info';

  if (fuelStintTargetIsUnrealistic(requiredBurn, referenceBurn)) return 'error';
  const ratio = requiredBurn / referenceBurn;
  if (ratio >= 1.0 - 0.000000001) return 'success';
  return ratio >= 0.85 - 0.000000001 ? 'warning' : 'error';
}

function fuelStintTargetIsBigSave(requiredBurn, referenceBurn) {
  if (!Number.isFinite(requiredBurn) || !Number.isFinite(referenceBurn) || referenceBurn <= 0) return false;
  const ratio = requiredBurn / referenceBurn;
  return ratio < 0.92 - 0.000000001 && ratio >= 0.85 - 0.000000001;
}

function fuelStintTargetIsUnrealistic(requiredBurn, referenceBurn) {
  if (!Number.isFinite(requiredBurn) || !Number.isFinite(referenceBurn) || referenceBurn <= 0) return false;
  return requiredBurn / referenceBurn < 0.85 - 0.000000001;
}

function fuelStintTargetStatus(targetCandidate, currentRangeLaps, targetLaps, flags, planLabel) {
  const prefix = planLabel ? `${planLabel}; ` : '';
  if (!targetCandidate || !Number.isFinite(targetCandidate.requiredBurn)) {
    return {
      label: `${prefix}learning`,
      tone: fuelStintTargetHasContextFlag(flags) ? 'warning' : 'waiting'
    };
  }

  if (flags.has('condition')) {
    return { label: `${prefix}condition mix`, tone: 'warning' };
  }

  if (flags.has('repair')) {
    return { label: `${prefix}repair context`, tone: 'warning' };
  }

  if (Number.isFinite(targetCandidate.strategyDeltaSeconds) && targetCandidate.strategyDeltaSeconds < -0.001) {
    return { label: `${prefix}not worth time`, tone: 'warning' };
  }

  if (Number.isFinite(currentRangeLaps) && Number.isFinite(targetLaps) && currentRangeLaps >= targetLaps - 0.000001) {
    return {
      label: `${prefix}tracking`,
      tone: fuelStintTargetHasContextFlag(flags) ? 'warning' : 'success'
    };
  }

  if (fuelStintTargetIsUnrealistic(targetCandidate.requiredBurn, targetCandidate.referenceBurn)) {
    return { label: `${prefix}not tracking`, tone: 'error' };
  }

  if (fuelStintTargetIsBigSave(targetCandidate.requiredBurn, targetCandidate.referenceBurn)) {
    return { label: `${prefix}large save`, tone: 'warning' };
  }

  if (Number.isFinite(targetCandidate.saveRequired) && targetCandidate.saveRequired > 0.005) {
    return { label: `${prefix}save ${targetCandidate.saveRequired.toFixed(2)} L/lap`, tone: 'warning' };
  }

  return {
    label: `${prefix}edge`,
    tone: fuelStintTargetHasContextFlag(flags) && targetCandidate.tone === 'success'
      ? 'warning'
      : targetCandidate.tone
  };
}

function fuelStintTargetHasContextFlag(flags) {
  return ['held', 'degraded', 'condition', 'repair', 'final-edge', 'tank-limited']
    .some((flag) => flags.has(flag));
}

function fuelStintTargetContextTone(snapshot) {
  if (snapshot.flags.has('repair') || snapshot.flags.has('condition')) return 'warning';
  return Number.isFinite(snapshot.remainingLaps) ? 'info' : 'waiting';
}

function fuelStintTargetRangeTone(snapshot) {
  if (!Number.isFinite(snapshot.currentRangeLaps)) return 'waiting';
  if (snapshot.flags.has('repair') || snapshot.flags.has('condition')) return 'warning';
  return 'info';
}

function fuelStintTargetBurnTone(snapshot) {
  if (!Number.isFinite(snapshot.referenceBurn)) return 'waiting';
  return snapshot.flags.has('repair') || snapshot.flags.has('condition') ? 'warning' : 'info';
}

function fuelStintTargetBurnLabel(value, label = '') {
  if (!Number.isFinite(value)) return '--';
  return label ? `${value.toFixed(2)} ${label}` : `${value.toFixed(2)} L/lap`;
}

function fuelStintTargetRangeLabel(value) {
  return Number.isFinite(value) ? `${value.toFixed(2)} laps` : '--';
}

function fuelStintTargetPositiveInteger(value) {
  const numeric = Number(value);
  return Number.isInteger(numeric) && numeric > 0 ? numeric : null;
}

function fuelPlanV2StartWorkbenchGridRow(label, plan) {
  const snapshot = fuelPlanV2Snapshot(plan);
  return gridRow(label, [
    snapshot.raceLabel,
    snapshot.stintCapacityLabel,
    snapshot.rhythmLabel,
    snapshot.stopsLabel,
    snapshot.finalLabel
  ], snapshot.tone);
}

function fuelPlanV2CheckpointWorkbenchGridRow(label, plan) {
  const snapshot = fuelPlanV2CurrentSnapshot(plan);
  return gridRow(label, [
    snapshot.raceLabel,
    snapshot.remainLabel,
    snapshot.currentCapacityLabel,
    snapshot.rhythmLabel,
    snapshot.stopsLabel,
    snapshot.finalLabel
  ], snapshot.tone);
}

function fuelPlanV2Snapshot(plan) {
  const flags = new Set(plan?.flags || []);
  const raceLaps = fuelPlanPositiveNumber(plan?.raceLaps);
  const remainLaps = fuelPlanNonNegativeNumber(plan?.remainLaps);
  const usableFuel = fuelPlanNonNegativeNumber(plan?.usableFuelLiters);
  const burn = fuelPlanBurnValue(plan?.burnLitersPerLap);
  const stintCapacity = fuelPlanNonNegativeNumber(plan?.stintCapacityLaps)
    ?? fuelPlanStintCapacityFromFuel(usableFuel, burn);
  const hasPlan = Number.isFinite(raceLaps) && Number.isFinite(stintCapacity) && stintCapacity > 0;
  const stintCount = hasPlan ? Math.max(1, Math.ceil(raceLaps / stintCapacity - 0.000001)) : null;
  const stops = Number.isFinite(stintCount) ? Math.max(0, stintCount - 1) : null;
  const finalStint = hasPlan && Number.isFinite(stintCount)
    ? fuelPlanFinalStintLaps(raceLaps, stintCapacity, stintCount)
    : null;
  if (Number.isFinite(finalStint) && finalStint <= 1.25 && stops > 0) {
    flags.add('final-edge');
  }

  return {
    raceLabel: plan?.raceLabel || fuelPlanRaceLabel(raceLaps, flags),
    remainLabel: plan?.remainLabel || fuelPlanNumberLabel(remainLaps),
    stintCapacityLabel: plan?.stintCapacityLabel || fuelPlanCapacityLabel(stintCapacity),
    rhythmLabel: plan?.rhythmLabel || fuelPlanRhythmLabel(raceLaps, stintCapacity, stintCount, finalStint),
    stopsLabel: plan?.stopsLabel || fuelPlanCountLabel(stops),
    finalLabel: plan?.finalLabel || fuelPlanFinalLabel(finalStint),
    tone: fuelPlanTone(raceLaps, stintCapacity, flags)
  };
}

function fuelPlanV2CurrentSnapshot(plan) {
  const flags = new Set(plan?.flags || []);
  const raceLaps = fuelPlanPositiveNumber(plan?.raceLaps);
  const remainLaps = fuelPlanNonNegativeNumber(plan?.remainLaps);
  const currentFuel = fuelPlanNonNegativeNumber(plan?.currentFuelLiters ?? plan?.usableFuelLiters);
  const futureFuel = fuelPlanNonNegativeNumber(plan?.futureFuelLiters ?? plan?.usableFuelLiters);
  const currentBurn = fuelPlanBurnValue(plan?.currentBurnLitersPerLap ?? plan?.burnLitersPerLap);
  const futureBurn = fuelPlanBurnValue(plan?.futureBurnLitersPerLap ?? plan?.burnLitersPerLap);
  const currentCapacity = fuelPlanStintRangeFromFuel(currentFuel, currentBurn);
  const futureCapacity = fuelPlanStintCapacityFromFuel(futureFuel, futureBurn);
  const currentPlan = fuelPlanCurrentCheckpointPlan(remainLaps, currentCapacity, futureCapacity);
  if (Number.isFinite(currentPlan.finalStint)
      && currentPlan.finalStint <= 1.25
      && currentPlan.stops > 0) {
    flags.add('final-edge');
  }

  return {
    raceLabel: plan?.raceLabel || fuelPlanRaceLabel(raceLaps, flags),
    remainLabel: plan?.remainLabel || fuelPlanNumberLabel(remainLaps),
    currentCapacityLabel: plan?.currentCapacityLabel || fuelPlanCurrentCapacityLabel(currentCapacity, futureCapacity),
    rhythmLabel: plan?.rhythmLabel || currentPlan.rhythmLabel,
    stopsLabel: plan?.stopsLabel || fuelPlanCountLabel(currentPlan.stops),
    finalLabel: plan?.finalLabel || fuelPlanFinalLabel(currentPlan.finalStint),
    tone: fuelPlanCurrentTone(
      currentPlan.stops !== null,
      remainLaps,
      currentCapacity ?? futureCapacity,
      flags)
  };
}

function fuelPlanCurrentCheckpointPlan(remainLaps, currentCapacity, futureCapacity) {
  if (!Number.isFinite(remainLaps) || !Number.isFinite(currentCapacity)) {
    return {
      stops: null,
      finalStint: null,
      rhythmLabel: Number.isFinite(futureCapacity)
        ? `now -- + ${fuelPlanFormatNumber(futureCapacity)}-lap rhythm`
        : '--'
    };
  }

  if (remainLaps <= currentCapacity + 0.000001) {
    return {
      stops: 0,
      finalStint: remainLaps,
      rhythmLabel: `${fuelPlanLapLabel(remainLaps)} no stop`
    };
  }

  if (!Number.isFinite(futureCapacity) || futureCapacity <= 0) {
    return {
      stops: null,
      finalStint: null,
      rhythmLabel: `${fuelPlanFormatNumber(currentCapacity)} now + --`
    };
  }

  const afterCurrent = Math.max(0, remainLaps - currentCapacity);
  const futureStints = Math.max(1, Math.ceil(afterCurrent / futureCapacity - 0.000001));
  const finalStint = fuelPlanFinalStintLaps(afterCurrent, futureCapacity, futureStints);

  return {
    stops: futureStints,
    finalStint,
    rhythmLabel: fuelPlanCurrentRhythmLabel(currentCapacity, futureCapacity, futureStints, finalStint)
  };
}

function fuelPlanFinalStintLaps(raceLaps, stintCapacity, stintCount) {
  const final = raceLaps - stintCapacity * Math.max(0, stintCount - 1);
  return final > 0.000001 ? final : stintCapacity;
}

function fuelPlanStintCapacityFromFuel(usableFuel, burn) {
  return Number.isFinite(usableFuel) && usableFuel >= 0 && Number.isFinite(burn) && burn > 0
    ? Math.floor(usableFuel / burn)
    : null;
}

function fuelPlanStintRangeFromFuel(usableFuel, burn) {
  return Number.isFinite(usableFuel) && usableFuel >= 0 && Number.isFinite(burn) && burn > 0
    ? Math.max(0, usableFuel / burn)
    : null;
}

function fuelPlanBurnValue(burn) {
  if (typeof burn === 'object' && burn !== null) {
    return Number(burn.value);
  }

  return Number(burn);
}

function fuelPlanRaceLabel(raceLaps, flags) {
  if (!Number.isFinite(raceLaps)) return '--';
  return `${fuelPlanFormatNumber(raceLaps)} laps${flags.has('held') ? ' held' : ''}`;
}

function fuelPlanNumberLabel(value) {
  return Number.isFinite(value) ? fuelPlanFormatNumber(value) : '--';
}

function fuelPlanCapacityLabel(value) {
  return Number.isFinite(value) ? fuelPlanLapLabel(value) : '--';
}

function fuelPlanCurrentCapacityLabel(currentCapacity, futureCapacity) {
  if (!Number.isFinite(currentCapacity) && !Number.isFinite(futureCapacity)) {
    return '--';
  }

  if (!Number.isFinite(currentCapacity)) {
    return `-- / ${fuelPlanFormatNumber(futureCapacity)} laps`;
  }

  if (!Number.isFinite(futureCapacity)) {
    return `${fuelPlanFormatNumber(currentCapacity)} / -- laps`;
  }

  if (Math.abs(currentCapacity - futureCapacity) <= 0.000001) {
    return fuelPlanCapacityLabel(currentCapacity);
  }

  return `${fuelPlanFormatNumber(currentCapacity)} / ${fuelPlanFormatNumber(futureCapacity)} laps`;
}

function fuelPlanRhythmLabel(raceLaps, stintCapacity, stintCount, finalStint) {
  if (!Number.isFinite(raceLaps) || !Number.isFinite(stintCapacity) || !Number.isFinite(stintCount) || !Number.isFinite(finalStint)) {
    return '--';
  }

  if (stintCount <= 1) {
    return `${fuelPlanLapLabel(raceLaps)} no stop`;
  }

  if (stintCount > 8) {
    return `${fuelPlanFormatNumber(stintCapacity)}-lap rhythm`;
  }

  if (Math.abs(finalStint - stintCapacity) <= 0.000001) {
    return `${fuelPlanFormatNumber(stintCapacity)} x${stintCount}`;
  }

  return `${fuelPlanFormatNumber(stintCapacity)} x${stintCount - 1} + ${fuelPlanFormatNumber(finalStint)}`;
}

function fuelPlanCurrentRhythmLabel(currentCapacity, futureCapacity, futureStints, finalStint) {
  if (!Number.isFinite(currentCapacity) || !Number.isFinite(futureCapacity) || !Number.isFinite(futureStints) || !Number.isFinite(finalStint)) {
    return '--';
  }

  if (futureStints > 8) {
    return `${fuelPlanFormatNumber(currentCapacity)} now + ${fuelPlanFormatNumber(futureCapacity)}-lap rhythm`;
  }

  if (futureStints <= 1) {
    return `${fuelPlanFormatNumber(currentCapacity)} now + ${fuelPlanFormatNumber(finalStint)}`;
  }

  if (Math.abs(finalStint - futureCapacity) <= 0.000001) {
    return `${fuelPlanFormatNumber(currentCapacity)} now + ${fuelPlanFormatNumber(futureCapacity)} x${futureStints}`;
  }

  return `${fuelPlanFormatNumber(currentCapacity)} now + ${fuelPlanFormatNumber(futureCapacity)} x${futureStints - 1} + ${fuelPlanFormatNumber(finalStint)}`;
}

function fuelPlanCountLabel(count) {
  return Number.isFinite(count) ? String(count) : '--';
}

function fuelPlanFinalLabel(finalStint) {
  return Number.isFinite(finalStint) ? fuelPlanLapLabel(finalStint) : '--';
}

function fuelPlanLapLabel(value) {
  if (!Number.isFinite(value)) return '--';
  const unit = Math.abs(value - 1) <= 0.000001 ? 'lap' : 'laps';
  return `${fuelPlanFormatNumber(value)} ${unit}`;
}

function fuelPlanTone(raceLaps, stintCapacity, flags) {
  if (!Number.isFinite(raceLaps)) {
    return 'waiting';
  }

  const hasWarningContext = ['held', 'degraded', 'condition', 'repair', 'final-edge', 'tank-limited']
    .some((flag) => flags.has(flag));
  if (!Number.isFinite(stintCapacity) || stintCapacity <= 0) {
    return hasWarningContext ? 'warning' : 'waiting';
  }

  return hasWarningContext ? 'warning' : 'info';
}

function fuelPlanCurrentTone(planAvailable, remainingLaps, currentOrFutureCapacity, flags) {
  if (planAvailable) {
    return fuelPlanTone(remainingLaps, currentOrFutureCapacity, flags);
  }

  return ['held', 'degraded', 'condition', 'repair', 'final-edge', 'tank-limited']
    .some((flag) => flags.has(flag))
    ? 'warning'
    : 'waiting';
}

function fuelPlanPositiveNumber(value) {
  const numeric = Number(value);
  return Number.isFinite(numeric) && numeric > 0 ? numeric : null;
}

function fuelPlanNonNegativeNumber(value) {
  if (value === null || value === undefined) return null;
  const numeric = Number(value);
  return Number.isFinite(numeric) && numeric >= 0 ? numeric : null;
}

function fuelPlanFormatNumber(value) {
  return Math.abs(value - Math.round(value)) <= 0.000001
    ? Math.round(value).toFixed(0)
    : value.toFixed(1);
}

function fuelLapsWorkbenchTone(value, realValue) {
  if (value === '--') return 'waiting';
  const valueText = String(value || '').toLowerCase();
  if (valueText.includes('degraded') || valueText.includes('held')) return 'warning';
  const modeled = Number.parseFloat(value);
  const actual = Number.parseFloat(realValue);
  if (!Number.isFinite(modeled) || !Number.isFinite(actual)) return 'info';
  const delta = Math.abs(modeled - actual);
  if (delta <= 0.25) return 'success';
  return delta <= 1 ? 'warning' : 'error';
}

function fuelPerLapWorkbenchRow(windows) {
  // Preserve the established live Last/5L/10L/Max cells and add the typed
  // History seed as its own visible V2 workbench cell. Min/Quali remain
  // intentionally out of this compact top-half row.
  const displayedBucketIds = [
    fuelV2BurnBucketId.last,
    fuelV2BurnBucketId.fiveLapAverage,
    fuelV2BurnBucketId.tenLapAverage,
    fuelV2BurnBucketId.historicalNormal,
    fuelV2BurnBucketId.maximum
  ];
  const buckets = displayedBucketIds.map((bucketId) => windows.buckets[bucketId]);
  const segments = buckets.map((bucket) => metricSegment(
    bucket.label,
    fuelPerLapWorkbenchValue(bucket.value),
    Number.isFinite(bucket.value) ? 'info' : 'waiting',
    fuelV2BurnBucketEvidence(bucket)));
  const availableValues = buckets.map((bucket) => bucket.value).filter(Number.isFinite);

  return metricRow(
    'Fuel/Lap',
    availableValues.length > 0
      ? availableValues.map((value) => value.toFixed(2)).join(' | ')
      : '--',
    availableValues.length > 0 ? 'info' : 'waiting',
    segments);
}

function fuelPerLapWorkbenchValue(value) {
  return Number.isFinite(value) ? `${value.toFixed(2)} L/lap` : '--';
}

function fuelRangeWorkbenchBucket(bucketId, rawRange) {
  const raw = typeof rawRange === 'object' && rawRange !== null
    ? rawRange
    : { value: rawRange };
  const numeric = raw.value === null || raw.value === undefined ? Number.NaN : Number(raw.value);
  if (!Number.isFinite(numeric) || numeric < 0) {
    return fuelV2BurnBucket(bucketId, null);
  }

  const evidence = {
    ...fuelV2BurnEvidence(bucketId, raw, {
    burnSource: raw.burnSource,
    sampleCount: raw.sampleCount,
    strategyEligible: raw.strategyEligible,
    confidence: raw.confidence,
    contextFlags: raw.contextFlags,
      source: raw.source
    }, true),
    value: null
  };
  return {
    ...fuelV2DerivedBurnBucket(evidence, numeric, `range from ${evidence.label}`),
    displaySuffix: String(raw.suffix ?? '').trim()
  };
}

function fuelRangeWorkbenchSegment(bucket) {
  const formattedRange = fuelRangeLaps(bucket.value);
  const value = formattedRange === '--' || !bucket.displaySuffix
    ? formattedRange
    : `${formattedRange} ${bucket.displaySuffix}`;
  const tone = !Number.isFinite(bucket.value)
    ? 'waiting'
    : bucket.id === fuelV2BurnBucketId.maximum || !bucket.strategyEligible
      ? 'warning'
      : 'info';
  return metricSegment(bucket.label, value, tone, fuelV2BurnBucketEvidence(bucket));
}

function fuelRangeWorkbenchRow(label, value, tone, range) {
  const last = fuelRangeWorkbenchBucket(fuelV2BurnBucketId.last, range.last);
  const five = fuelRangeWorkbenchBucket(fuelV2BurnBucketId.fiveLapAverage, range.five);
  const ten = fuelRangeWorkbenchBucket(fuelV2BurnBucketId.tenLapAverage, range.ten);
  const maximum = fuelRangeWorkbenchBucket(fuelV2BurnBucketId.maximum, range.max);
  const segments = [
    metricSegment('Fuel', fuelRangeVolume(range.fuel), fuelRangeValueTone(range.fuel)),
    metricSegment('V1 Ref', fuelRangeLaps(range.rangeV1), fuelRangeValueTone(range.rangeV1)),
    fuelRangeWorkbenchSegment(last),
    fuelRangeWorkbenchSegment(five),
    fuelRangeWorkbenchSegment(ten),
    fuelRangeWorkbenchSegment(maximum)
  ];

  return metricRow(
    label,
    value,
    tone,
    segments);
}

function fuelTargetUsageWorkbenchRow(label, value, tone, targetUsage) {
  const referenceBucketId = targetUsage.referenceBucketId;
  const referenceBurn = fuelV2BurnBucketContract[referenceBucketId]
    ? fuelV2BurnBucket(referenceBucketId, targetUsage.referenceBurn, {
        burnSource: targetUsage.referenceBurnSource,
        sampleCount: targetUsage.referenceSampleCount,
        strategyEligible: targetUsage.referenceStrategyEligible,
        confidence: targetUsage.referenceConfidence,
        source: targetUsage.referenceSource
      })
    : {
        id: null,
        label: 'Reference',
        value: null,
        source: 'unavailable: missing burn bucket identity',
        burnSource: 'Unavailable',
        sampleCount: null,
        confidence: 'Unavailable',
        contextFlags: [],
        displayEligible: false,
        cleanBaselineEligible: false,
        strategyEligible: false,
        detailLabel: ''
      };
  const referenceTone = fuelTargetUsageReferenceTone(referenceBurn);
  const targetSegments = fuelTargetUsageTargetLaps(targetUsage, referenceBurn.value)
    .map((laps) => {
      const requiredBurn = fuelTargetUsageRequiredBurn(targetUsage.budget, laps);
      return metricSegment(
        fuelTargetUsageLapLabel(laps),
        fuelRangeFuelPerLap(requiredBurn),
        fuelTargetUsageTone(requiredBurn, referenceBurn.value),
        {
          referenceBurnBucketId: referenceBurn.id,
          referenceBurnSource: referenceBurn.burnSource,
          referenceSampleCount: referenceBurn.sampleCount,
          referenceConfidence: referenceBurn.confidence,
          referenceContextFlags: referenceBurn.contextFlags,
          referenceDisplayEligible: referenceBurn.displayEligible,
          referenceCleanBaselineEligible: referenceBurn.cleanBaselineEligible,
          referenceStrategyEligible: referenceBurn.strategyEligible,
          referenceProvenance: referenceBurn.source
        });
    });
  const segments = [
    metricSegment(
      targetUsage.budgetLabel || 'Fuel',
      fuelRangeVolume(targetUsage.budget),
      fuelRangeValueTone(targetUsage.budget)),
    metricSegment(
      referenceBurn.label,
      fuelRangeFuelPerLap(referenceBurn.value),
      referenceTone,
      fuelV2BurnBucketEvidence(referenceBurn)),
    ...targetSegments
  ];

  return metricRow(
    label,
    value,
    tone,
    segments);
}

function fuelPitRequestWorkbenchRow(label, value, tone, request) {
  const segments = fuelPitRequestBuckets(request).map((bucket) => metricSegment(
    bucket.label,
    fuelPitRequestAddLabel(request, bucket),
    fuelPitRequestTone(request, bucket),
    fuelV2BurnBucketEvidence(fuelV2DerivedBurnBucket(
      bucket,
      fuelPitRequestAddAmount(request, bucket)?.amount,
      `pit add from ${bucket.label}`))));

  return metricRow(
    label,
    value,
    tone,
    segments);
}

function fuelSectorLiveProjectionHeaders(rows) {
  return [
    'Sector',
    ...rows.map((row) => row.label)
  ];
}

function fuelSectorLiveProjectionGridRows(rows, sectorStarts, firstLapGateIndex, sectorSpeedsKph = []) {
  const lapCells = rows.map((row, rowIndex) => {
    const baselineBurns = row.baselineBreak === true ? null : fuelSectorPreviousBaselineBurns(rows, rowIndex);
    const warnings = new Set(row.warningIndexes || []);
    const invalids = new Set(row.invalidIndexes || []);
    const pitIndexes = new Set(row.pitIndexes || []);
    const burns = fuelSectorEffectiveBurns(row);
    const totalBurn = fuelSectorBurnTotal(burns);
    const acceptedProjection = fuelSectorPartialProjection(burns, invalids, sectorStarts);
    const rejected = row.rejected === true
      || (!invalids.size && (!Number.isFinite(totalBurn) || totalBurn <= 0))
      || (invalids.size > 0 && !Number.isFinite(acceptedProjection));
    if (rejected) {
      return [
        ...sectorStarts.map((_, sectorIndex) => ({
          value: '--',
          tone: 'error',
          pitContext: pitIndexes.has(sectorIndex)
        })),
        {
          value: 'Rejected',
          tone: 'error'
        }
      ];
    }

    const projections = fuelSectorLiveProjectionValues(burns, baselineBurns, sectorStarts, invalids);
    const sectorCells = projections.map((value, sectorIndex) => ({
      value: fuelSectorProjectionValue(value),
      tone: fuelSectorLiveProjectionTone(rowIndex, sectorIndex, firstLapGateIndex, warnings, invalids, row.baselineBreak === true),
      pitContext: pitIndexes.has(sectorIndex)
    }));
    return [
      ...sectorCells,
      {
        value: invalids.size > 0
          ? `${fuelSectorProjectionValue(acceptedProjection)} post`
          : row.burnOverrides
            ? `${fuelSectorProjectionValue(fuelSectorBurnTotal(burns))} pit`
            : fuelSectorProjectionValue(fuelSectorBurnTotal(burns)),
        tone: invalids.size > 0 || warnings.size > 0 || row.burnOverrides ? 'warning' : 'modeled'
      }
    ];
  });

  const rowCount = sectorStarts.length + 1;
  return Array.from({ length: rowCount }, (_, rowIndex) => {
    const cells = lapCells.map((cellsForLap) => cellsForLap[rowIndex] || { value: '--', tone: 'waiting' });
    const speed = sectorSpeedsKph[rowIndex];
    const label = rowIndex < sectorStarts.length
      ? Number.isFinite(speed)
        ? `S${rowIndex} · ${Math.round(speed)} kph`
        : `S${rowIndex}`
      : 'Actual';
    return {
      label,
      tone: cells.some((cell) => cell.tone === 'error') ? 'error' : cells.some((cell) => cell.tone === 'warning') ? 'warning' : 'info',
      cells
    };
  });
}

function fuelSectorPreviousBaselineBurns(rows, rowIndex) {
  for (let index = rowIndex - 1; index >= 0; index -= 1) {
    const candidate = rows[index];
    if (candidate?.baselineBreak === true) {
      return null;
    }
    const burns = fuelSectorEffectiveBurns(candidate);
    const totalBurn = fuelSectorBurnTotal(burns);
    if (candidate?.baselineEligible !== false && !candidate?.rejected && Number.isFinite(totalBurn) && totalBurn > 0) {
      return burns;
    }
  }
  return null;
}

function fuelSectorLiveProjectionValues(burns, baselineBurns, sectorStarts, invalidIndexes = new Set()) {
  let cumulativeBurn = 0;
  let cumulativeStartIndex = 0;
  return burns.map((burn, sectorIndex) => {
    if (invalidIndexes.has(sectorIndex)) {
      cumulativeBurn = 0;
      cumulativeStartIndex = sectorIndex + 1;
      return null;
    }

    cumulativeBurn += burn;
    if (baselineBurns && cumulativeStartIndex === 0 && sectorIndex === 0) {
      return fuelSectorBurnTotal(baselineBurns);
    }
    if (baselineBurns && cumulativeStartIndex === 0) {
      const baselineCumulative = fuelSectorBurnTotal(baselineBurns.slice(0, sectorIndex + 1));
      const baselineFullLap = fuelSectorBurnTotal(baselineBurns);
      return baselineCumulative > 0
        ? baselineFullLap * (cumulativeBurn / baselineCumulative)
        : null;
    }

    const sectorEnd = sectorIndex + 1 < sectorStarts.length ? sectorStarts[sectorIndex + 1] : 1;
    const sectorStart = sectorStarts[cumulativeStartIndex] || 0;
    const sectorProgress = sectorEnd - sectorStart;
    return sectorProgress > 0 ? cumulativeBurn / sectorProgress : null;
  });
}

function fuelSectorLiveProjectionTone(rowIndex, sectorIndex, firstLapGateIndex, warnings, invalids = new Set(), partialRow = false) {
  if (invalids.has(sectorIndex)) return 'error';
  if (partialRow) return 'warning';
  if (rowIndex === 0 && sectorIndex < firstLapGateIndex) return 'warning';
  if (rowIndex > 0 && sectorIndex === 0) return 'warning';
  return warnings.has(sectorIndex) ? 'warning' : 'info';
}

function fuelSectorBurnTotal(burns) {
  return burns.reduce((total, burn) => total + burn, 0);
}

function fuelSectorProjectionValue(value) {
  return Number.isFinite(value) ? value.toFixed(2) : '--';
}

function fuelSectorEffectiveBurns(row) {
  const burns = (row?.burns || []).slice();
  const overrides = row?.burnOverrides || {};
  for (const [key, value] of Object.entries(overrides)) {
    const index = Number.parseInt(key, 10);
    const burn = Number(value);
    if (Number.isInteger(index) && index >= 0 && index < burns.length && Number.isFinite(burn)) {
      burns[index] = burn;
    }
  }
  return burns;
}

function fuelSectorPartialProjection(burns, invalidIndexes, sectorStarts) {
  if (!invalidIndexes.size) {
    const total = fuelSectorBurnTotal(burns);
    return Number.isFinite(total) && total > 0 ? total : null;
  }

  const lastInvalidIndex = Math.max(...Array.from(invalidIndexes));
  const startIndex = lastInvalidIndex + 1;
  if (startIndex >= burns.length || startIndex >= sectorStarts.length) {
    return null;
  }

  const acceptedBurn = fuelSectorBurnTotal(burns.slice(startIndex));
  const acceptedProgress = 1 - (sectorStarts[startIndex] || 0);
  return Number.isFinite(acceptedBurn) && acceptedBurn > 0 && acceptedProgress > 0
    ? acceptedBurn / acceptedProgress
    : null;
}

function fuelSectorShapeChartSection(title, rows, sectorStarts, options = {}) {
  const series = fuelSectorProjectionChartSeries(rows, sectorStarts, options.firstLapGateIndex || 3);
  const range = fuelSectorProjectionChartRange(series);
  return {
    title: title.replace('Cumulative Sector Shape', 'Live Projection Trace'),
    height: options.height || 320,
    yAxisLabel: 'Live projected L/lap',
    yMin: range.min,
    yMax: range.max,
    yTicks: [
      { value: range.max, label: `${range.max.toFixed(1)} L` },
      { value: (range.min + range.max) / 2, label: `${((range.min + range.max) / 2).toFixed(1)} L` },
      { value: range.min, label: `${range.min.toFixed(1)} L` }
    ],
    notes: options.notes || [],
    series
  };
}

function fuelSectorProjectionChartSeries(rows, sectorStarts, firstLapGateIndex) {
  const series = [];
  let baselineBurns = null;
  let liveRowIndex = 0;
  for (const row of rows) {
    const totalBurn = fuelSectorBurnTotal(row.burns);
    const rejected = row.rejected === true || !Number.isFinite(totalBurn) || totalBurn <= 0;
    if (rejected) {
      continue;
    }

    const warningIndexes = new Set(row.warningIndexes || []);
    const trafficIndexes = new Set(row.trafficIndexes || []);
    const projections = fuelSectorLiveProjectionValues(row.burns, baselineBurns, sectorStarts);
    const points = projections
      .map((value, sectorIndex) => ({
        sectorIndex,
        x: sectorIndex + 1 < sectorStarts.length ? sectorStarts[sectorIndex + 1] : 1,
        y: value
      }))
      .filter((point) => Number.isFinite(point.y));
    series.push({
      label: `${row.label} ${fuelSectorProjectionValue(totalBurn)}L`,
      tone: warningIndexes.size > 0 ? 'warning' : 'info',
      degraded: liveRowIndex === 0 || warningIndexes.size > 0,
      trafficIndexes: Array.from(trafficIndexes),
      points,
      firstLapGateIndex
    });

    if (row.baselineEligible !== false) {
      baselineBurns = row.burns;
    }
    liveRowIndex += 1;
  }
  return series.filter((item) => item.points.length >= 2);
}

function fuelSectorProjectionChartRange(series) {
  const values = series
    .flatMap((item) => item.points || [])
    .map((point) => Number(point.y))
    .filter((value) => Number.isFinite(value));
  if (values.length === 0) {
    return { min: 0, max: 1 };
  }

  const rawMin = Math.min(...values);
  const rawMax = Math.max(...values);
  const spread = Math.max(0.25, rawMax - rawMin);
  const padding = Math.max(0.15, spread * 0.15);
  const min = Math.floor((rawMin - padding) * 4) / 4;
  const max = Math.ceil((rawMax + padding) * 4) / 4;
  return max > min ? { min, max } : { min: min - 0.25, max: max + 0.25 };
}

function fuelTargetUsageReferenceTone(referenceBurn) {
  if (!Number.isFinite(referenceBurn?.value)) return 'waiting';
  return referenceBurn.strategyEligible ? 'info' : 'warning';
}

function fuelTargetUsageTargetLaps(targetUsage, normalizedReferenceBurn) {
  if (Array.isArray(targetUsage.targetLaps) && targetUsage.targetLaps.length > 0) {
    return [...new Set(targetUsage.targetLaps
      .map((laps) => Number(laps))
      .filter((laps) => Number.isInteger(laps) && laps > 0))]
      .sort((left, right) => left - right);
  }

  const fuelBudget = Number(targetUsage.budget);
  const referenceBurn = Number(normalizedReferenceBurn);
  if (!Number.isFinite(fuelBudget) || fuelBudget <= 0 || !Number.isFinite(referenceBurn) || referenceBurn <= 0) {
    return [];
  }

  const projectedLaps = fuelBudget / referenceBurn;
  if (projectedLaps > 1000) return [];
  const centerLap = Math.max(1, Math.round(projectedLaps));
  return centerLap === 1
    ? [1, 2, 3]
    : [centerLap - 1, centerLap, centerLap + 1];
}

function fuelTargetUsageRequiredBurn(budget, targetLaps) {
  const fuelBudget = Number(budget);
  const laps = Number(targetLaps);
  return Number.isFinite(fuelBudget) && fuelBudget > 0 && Number.isFinite(laps) && laps > 0
    ? fuelBudget / laps
    : null;
}

function fuelTargetUsageLapLabel(targetLaps) {
  const laps = Number(targetLaps);
  if (!Number.isFinite(laps)) return 'Laps';
  return laps === 1 ? '1 lap' : `${laps} laps`;
}

function fuelTargetUsageTone(requiredBurn, referenceBurn) {
  if (!Number.isFinite(requiredBurn)) return 'waiting';
  if (!Number.isFinite(referenceBurn) || referenceBurn <= 0) return 'info';

  const ratio = requiredBurn / referenceBurn;
  if (ratio >= 1.0) return 'success';
  return ratio >= 0.95 ? 'warning' : 'error';
}

function fuelPitRequestBuckets(request) {
  return [
    fuelV2BurnBucket(fuelV2BurnBucketId.last, request?.lastBurn),
    fuelV2BurnBucket(fuelV2BurnBucketId.fiveLapAverage, request?.fiveBurn),
    fuelV2BurnBucket(fuelV2BurnBucketId.tenLapAverage, request?.tenBurn),
    fuelV2BurnBucket(fuelV2BurnBucketId.maximum, request?.maxBurn, {
      burnSource: 'HistoricalSeed',
      strategyEligible: false,
      cleanBaselineEligible: false,
      confidence: 'Seeded'
    }),
    fuelV2BurnBucket(fuelV2BurnBucketId.minimum, request?.minBurn),
    fuelV2BurnBucket(fuelV2BurnBucketId.qualifying, request?.qualiBurn, {
      burnSource: 'QualifyingSeed',
      strategyEligible: false,
      cleanBaselineEligible: false,
      confidence: 'Seeded'
    })
  ];
}

function fuelPitRequestAddLabel(request, burn) {
  const add = fuelPitRequestAddAmount(request, burn);
  if (!add) return '--';
  const suffix = fuelPitRequestBurnLabel(burn);
  const value = add.limited
    ? `${add.amount.toFixed(1)} L cap`
    : `+${add.amount.toFixed(1)} L`;
  return suffix
    ? `${value} ${suffix}`
    : value;
}

function fuelPitRequestTone(request, burn) {
  const add = fuelPitRequestAddAmount(request, burn);
  if (!add) return 'waiting';
  if (add.feasibilityState === 'invalid'
      || add.feasibilityState === 'capacity-conflicted'
      || add.feasibilityState === 'unachievable') return 'error';
  if (add.feasibilityState === 'unavailable') return 'waiting';
  if (add.amount <= 0.001) return 'success';
  if (!burn.strategyEligible
    || burn.id === fuelV2BurnBucketId.maximum
    || burn.id === fuelV2BurnBucketId.minimum
    || burn.id === fuelV2BurnBucketId.qualifying) {
    return 'warning';
  }
  return 'info';
}

function fuelPitRequestAddAmount(request, burn) {
  const currentFuel = request?.currentFuel;
  const tankCapacity = request?.tankCapacity;
  const capacityInputs = tankCapacity === null || tankCapacity === undefined
    ? { physicalCapacityLiters: null, driverCapPercent: null, classCapPercent: null }
    : { physicalCapacityLiters: tankCapacity, driverCapPercent: 1, classCapPercent: 1 };
  const checkpoints = fuelCheckpointSnapshot({
    capacity: capacityInputs,
    currentFuelLiters: currentFuel
  });
  const burnState = fuelV2HasTypedBurnEvidence(burn, burn?.id) ? 'available' : 'unavailable';
  const boundary = fuelBoundaryCell(
    checkpoints,
    burn,
    burnState,
    request?.targetLaps,
    request?.reserveFuel,
    request?.pitLaneFuel,
    checkpoints.current);
  if (boundary.feasibilityState === 'invalid') return null;
  const amount = boundary.clampedAddLiters ?? boundary.desiredAddLiters;
  if (!Number.isFinite(amount) || !Number.isFinite(boundary.desiredFuelLiters)) return null;

  return {
    amount,
    limited: boundary.stateFlags.has('tank-limited'),
    targetFuel: boundary.desiredFuelLiters,
    feasibilityState: boundary.feasibilityState,
    desiredAdd: boundary.desiredAddLiters,
    tankRoom: boundary.tankRoomLiters,
    shortfall: boundary.shortfallLiters,
    maximumFeasibleLaps: boundary.maximumFeasibleLaps
  };
}

function fuelPitRequestBurnLabel(burn) {
  if (typeof burn === 'object' && burn !== null) {
    return String(burn.id ? burn.detailLabel : burn.label || '').trim();
  }

  return '';
}

function fuelPitRequestNonNegative(value) {
  if (value === null || value === undefined) return 0;
  const numeric = Number(value);
  return Number.isFinite(numeric) && numeric >= 0 ? numeric : null;
}

function fuelRangeValueTone(value) {
  return Number.isFinite(value) ? 'info' : 'waiting';
}

function fuelRangeVolume(value) {
  return Number.isFinite(value) ? `${value.toFixed(1)} L` : '--';
}

function fuelRangeFuelPerLap(value) {
  return Number.isFinite(value) ? `${value.toFixed(2)} L` : '--';
}

function fuelRangeLaps(value) {
  if (typeof value === 'string') return value;
  return Number.isFinite(value) ? value.toFixed(2) : '--';
}

function fuelLapsWorkbenchFormatCell(value) {
  if (value === '--' || value == null) return '--';
  const text = String(value);
  const match = /^(\s*)([-+]?\d+(?:\.\d+)?)(.*)$/.exec(text);
  if (!match) return text;

  const parsed = Number.parseFloat(match[2]);
  return Number.isFinite(parsed)
    ? `${match[1]}${parsed.toFixed(2)}${match[3]}`
    : text;
}

function fuelLapsWorkbenchV2Values(scenario) {
  return scenario.checkpoints.map((checkpoint) => fuelLapsWorkbenchV2Value(checkpoint));
}

function fuelLapsWorkbenchV2Value(checkpoint) {
  if (!checkpoint) return '--';

  let finishLap = null;
  let suffix = '';
  if (checkpoint.preGreen) {
    finishLap = checkpoint.durationSeconds / checkpoint.paceSeconds;
    suffix = ' seed';
  } else {
    const progress = checkpoint.synthetic
      ? checkpoint.leaderProgressLaps - checkpoint.baselineLeaderProgressLaps
      : checkpoint.leaderProgressLaps;
    const remaining = checkpoint.synthetic
      ? checkpoint.durationSeconds - Math.max(0, checkpoint.sessionTimeSeconds - checkpoint.baselineSessionTimeSeconds)
      : checkpoint.durationSeconds - checkpoint.sessionTimeSeconds;
    finishLap = progress + Math.max(0, remaining) / checkpoint.paceSeconds;
  }

  if (checkpoint.paceContaminated && Number.isFinite(finishLap)) {
    const cleanFinishLap = fuelLapsWorkbenchCleanFinishLap(checkpoint);
    const heldFinishLap = Math.max(
      Number.isFinite(cleanFinishLap) ? cleanFinishLap : finishLap,
      Number.isFinite(checkpoint.previousCleanFinishLap) ? checkpoint.previousCleanFinishLap : finishLap);
    if (heldFinishLap > Math.ceil(finishLap)) {
      suffix = ' held degraded';
    }
  }

  return Number.isFinite(finishLap) ? `${finishLap.toFixed(2)}${suffix}` : '--';
}

function fuelLapsWorkbenchCleanFinishLap(checkpoint) {
  if (!Number.isFinite(checkpoint.cleanPaceSeconds) || checkpoint.cleanPaceSeconds <= 0) {
    return null;
  }

  const progress = checkpoint.synthetic
    ? checkpoint.leaderProgressLaps - checkpoint.baselineLeaderProgressLaps
    : checkpoint.leaderProgressLaps;
  const remaining = checkpoint.synthetic
    ? checkpoint.durationSeconds - Math.max(0, checkpoint.sessionTimeSeconds - checkpoint.baselineSessionTimeSeconds)
    : checkpoint.durationSeconds - checkpoint.sessionTimeSeconds;
  return progress + Math.max(0, remaining) / checkpoint.cleanPaceSeconds;
}

function fuelLapsWorkbench24hScenario({ durationSeconds = 86400 } = {}) {
  const baselineSessionTimeSeconds = 57012.003;
  const baselineLeaderProgressLaps = 113.3288;
  const synthetic = durationSeconds !== 86400;
  const cleanFinishLap = synthetic ? 59 : 174;
  const checkpoint = (values) => ({
    durationSeconds,
    baselineSessionTimeSeconds,
    baselineLeaderProgressLaps,
    synthetic,
    ...values
  });
  return {
    checkpoints: [
      checkpoint({
        preGreen: true,
        paceSeconds: 480.8602
      }),
      checkpoint({
        sessionTimeSeconds: 58504.0,
        leaderProgressLaps: 116.3744,
        paceSeconds: 490.0
      }),
      checkpoint({
        sessionTimeSeconds: 59810.019,
        leaderProgressLaps: 119.3791,
        paceSeconds: 570.265,
        cleanPaceSeconds: 491.778,
        previousCleanFinishLap: cleanFinishLap,
        paceContaminated: true,
        frontPackPaceDisagreement: true
      }),
      checkpoint({
        sessionTimeSeconds: 60359.203,
        leaderProgressLaps: 120.0060,
        paceSeconds: 490.191
      }),
      null
    ]
  };
}

function metricsModel(
  overlayId,
  title,
  status,
  metrics,
  source = 'source: review fixture',
  gridSections = [],
  metricSections = [],
  headerItems = [],
  shouldRender = true,
  chartSections = []) {
  return {
    overlayId,
    title,
    status,
    source,
    bodyKind: 'metrics',
    columns: [],
    rows: [],
    metrics: metrics.map(metricModelRow),
    points: [],
    headerItems,
    gridSections,
    chartSections,
    shouldRender,
    metricSections: metricSections.map((section) => ({
      title: section.title,
      rows: section.rows.map(metricModelRow)
    }))
  };
}

function metricRow(label, value, tone, segments = undefined, extra = {}) {
  return segments ? { label, value, tone, segments, ...extra } : { label, value, tone, ...extra };
}

function metricSegment(label, value, tone, extra = {}) {
  return { label, value, tone, ...extra };
}

function filterMetricSectionsByContent(overlayId, sections, overlayState, session = null) {
  return sections
    .map((section) => ({
      ...section,
      rows: (section.rows || [])
        .map((row) => filterMetricRowByContent(overlayId, row, overlayState, session, section.title))
        .filter(Boolean)
    }))
    .filter((section) => section.rows.length > 0);
}

function filterMetricRowByContent(overlayId, row, overlayState, session = null, sectionTitle = null) {
  const metric = Array.isArray(row)
    ? metricRow(row[0], row[1], row[2])
    : row;
  const rowLabels = metricContentLabels(overlayId, metric.label, null, sectionTitle);

  if (!Array.isArray(metric.segments) || metric.segments.length === 0) {
    if (contentLabelsEnabled(overlayState, rowLabels, true, session)) {
      return metric;
    }

    return null;
  }

  const segments = metric.segments.filter((segment) => {
    const labels = metricContentLabels(overlayId, metric.label, segment.label, sectionTitle);
    return contentLabelsEnabled(overlayState, labels, true, session);
  });
  if (segments.length === 0) {
    return null;
  }

  return {
    ...metric,
    segments,
    value: segments.length === metric.segments.length
      ? metric.value
      : segments.map((segment) => segment.value).filter(Boolean).join(' | ') || metric.value
  };
}

function filterGridRowsByContent(overlayId, rows, overlayState, session = null) {
  return rows.filter((row) => {
    const labels = metricContentLabels(overlayId, row.label, null);
    return contentLabelsEnabled(overlayState, labels, true, session);
  });
}

function metricContentLabels(overlayId, rowLabel, segmentLabel, sectionTitle = null) {
  if (overlayId === 'fuel-calculator') {
    return fuelMetricContentLabels(sectionTitle, rowLabel, segmentLabel);
  }

  const key = segmentLabel ? `${rowLabel}|${segmentLabel}` : rowLabel;
  const maps = {
    'session-weather': {
      'Session|Type': ['Session type'],
      'Session|Name': ['Session name'],
      'Session|Mode': ['Session mode'],
      'Clock|Elapsed': ['Elapsed time'],
      'Clock|Left': ['Remaining time'],
      'Clock|Total': ['Total time'],
      'Event|Event': ['Event type'],
      'Event|Car': ['Car'],
      'Laps|Remaining': ['Laps remaining'],
      'Laps|Total': ['Laps total'],
      'Track|Name': ['Track name'],
      'Track|Length': ['Track length'],
      'Surface|Wetness': ['Wetness'],
      'Surface|Declared': ['Declared surface'],
      'Surface|Rubber': ['Rubber'],
      'Sky|Skies': ['Skies'],
      'Sky|Weather': ['Weather'],
      'Sky|Rain': ['Rain'],
      'Wind|Dir': ['Wind direction'],
      'Wind|Speed': ['Wind speed'],
      'Wind|Facing': ['Facing wind', 'Facing arrow'],
      'Temps|Air': ['Air temp'],
      'Temps|Track': ['Track temp'],
      'Atmosphere|Hum': ['Humidity'],
      'Atmosphere|Fog': ['Fog'],
      'Atmosphere|Pressure': ['Pressure']
    },
    'pit-service': {
      'Time / Laps|Time': ['Session time'],
      'Time / Laps|Laps': ['Session laps'],
      Release: ['Release'],
      'Pit status': ['Pit status'],
      'Fuel request|Requested': ['Fuel requested'],
      'Fuel request|Selected': ['Fuel selected'],
      'Tearoff|Requested': ['Tearoff requested'],
      'Repair|Required': ['Required repair', 'Repair required'],
      'Repair|Optional': ['Optional repair', 'Repair optional'],
      'Fast repair|Selected': ['Fast repair selected'],
      'Fast repair|Available': ['Fast repairs available', 'Fast repair available'],
      Compound: ['Compound', 'Tire compound'],
      'Change request': ['Change request', 'Tire change'],
      'Set limit': ['Set limit', 'Tire set limit'],
      'Sets available': ['Sets available', 'Tire sets available'],
      'Sets used': ['Sets used', 'Tire sets used'],
      Pressure: ['Pressure', 'Tire pressure'],
      Temperature: ['Temperature', 'Tire temperature'],
      Wear: ['Wear', 'Tire wear'],
      Distance: ['Distance', 'Tire distance']
    }
  };

  return maps[overlayId]?.[key] || maps[overlayId]?.[rowLabel] || [];
}

function fuelMetricContentLabels(sectionTitle, rowLabel, segmentLabel) {
  if (sectionTitle === 'Race Information' && rowLabel === 'Plan') {
    return ['Plan'];
  }

  if (sectionTitle === 'Race Information' && rowLabel === 'Fuel') {
    return ['Fuel'];
  }

  if (sectionTitle === 'Stint Targets') {
    return ['Stint targets'];
  }

  if (sectionTitle === 'Fuel Range') {
    return ['Fuel range'];
  }

  if (sectionTitle === 'Fuel Usage') {
    return ['Fuel usage'];
  }

  return segmentLabel ? [`${rowLabel}|${segmentLabel}`] : [rowLabel];
}

function metricModelRow(row) {
  if (!Array.isArray(row)) {
    return {
      label: row?.label || '',
      value: row?.value || '--',
      tone: row?.tone || 'normal',
      ...(Array.isArray(row?.segments) && row.segments.length > 0 ? { segments: row.segments } : {}),
      ...(Number.isInteger(row?.segmentColumnCount) ? { segmentColumnCount: row.segmentColumnCount } : {}),
      ...(row?.rowColorHex ? { rowColorHex: row.rowColorHex } : {})
    };
  }

  const [label, value, tone] = row;
  return { label, value, tone };
}

function gridRow(label, values, tone = 'normal') {
  return {
    label,
    tone,
    cells: values.map((value) => typeof value === 'object' && value !== null
      ? { value: value.value, tone: value.tone || tone }
      : { value, tone })
  };
}

function gridSummaryRow(summary, tone = 'normal') {
  return { summary, tone };
}

function fuelV2StintTargetHeaders(hasTireServiceEvidence) {
  return [
    'Stint',
    'Plan',
    'Fuel target',
    ...(hasTireServiceEvidence ? ['Tires'] : []),
    'Live state'
  ];
}

function fuelV2StintTargetCells(hasTireServiceEvidence, plan, fuelTarget, tires, liveState) {
  return [
    plan,
    fuelTarget,
    ...(hasTireServiceEvidence ? [tires ?? gridCell('—', 'normal')] : []),
    liveState
  ];
}

function gridCell(value, tone) {
  return { value, tone };
}

function standingsDisplayModel(previewLabel = 'review fixture', session = 'race', fixture = '') {
  if (fixture === 'standings-no-results-chrome-on' || fixture === 'standings-zero-default-timing') {
    return {
      overlayId: 'standings',
      title: 'Standings',
      status: 'waiting for standings',
      source: fixture === 'standings-zero-default-timing'
        ? 'source: zero/default timing placeholders filtered'
        : 'source: waiting for standings',
      bodyKind: 'table',
      columns: [],
      rows: [],
      metrics: [],
      shouldRender: true,
      headerItems: [
        { key: 'timeRemaining', value: '06:37:08' }
      ]
    };
  }

  const raceColumns = session === 'race';
  const columns = raceColumns
    ? [
        { label: 'Pos', dataKey: 'class-position', width: 35, alignment: 'right' },
        { label: 'CAR', dataKey: 'car-number', width: 50, alignment: 'right' },
        { label: 'Driver', dataKey: 'driver', width: 250, alignment: 'left' },
        { label: 'GAP', dataKey: 'gap', width: 60, alignment: 'right' },
        { label: 'INT', dataKey: 'interval', width: 60, alignment: 'right' },
        { label: 'FAST', dataKey: 'fastest-lap', width: 70, alignment: 'right' },
        { label: 'LAST', dataKey: 'last-lap', width: 70, alignment: 'right' },
        { label: 'PIT', dataKey: 'pit', width: 48, alignment: 'right' }
      ]
    : [
        { label: 'Pos', dataKey: 'class-position', width: 35, alignment: 'right' },
        { label: 'CAR', dataKey: 'car-number', width: 50, alignment: 'right' },
        { label: 'Driver', dataKey: 'driver', width: 250, alignment: 'left' },
        { label: 'FAST', dataKey: 'fastest-lap', width: 70, alignment: 'right' },
        { label: 'LAST', dataKey: 'last-lap', width: 70, alignment: 'right' },
        { label: 'PIT', dataKey: 'pit', width: 48, alignment: 'right' }
      ];
  const isStartingGrid = fixture === 'standings-starting-grid';
  const classLayoutFixture = {
    'standings-one-class': 'one-class layout',
    'standings-two-class': 'two-class layout',
    'standings-three-class': 'three-class layout',
    'standings-min-scale': 'minimum-scale layout'
  }[fixture] || '';
  const statusPrefix = isStartingGrid ? 'starting grid' : 'scoring';
  const source = isStartingGrid
    ? 'source: starting grid + live timing'
    : classLayoutFixture
      ? `source: preview fixture ${classLayoutFixture}`
      : 'source: preview fixture extremes';
  const rows = isStartingGrid
    ? [
        headerRow('LMP2', '2 cars', '#33CEFF'),
        carRow(['1', '#8', 'Kousuke Konishi', 'Leader', '--', '1:45.884', '1:46.210', '']),
        headerRow('GT3', '3 cars', '#FFAA00'),
        carRow(['1', '#000', 'Kauan Vigliazzi Teixeira Lemos', 'Leader', '--', '1:53.112', '1:53.112', ''], { cellTones: [null, null, null, null, null, 'best-lap', 'best-lap', null] }),
        carRow(['24', '#3094', 'Tech Mates Racing', '--', '--', '--', '--', ''], { isReference: true }),
        carRow(['49', '#60', 'Tommie Wittens', '--', '--', '--', '--', ''], { isPendingGrid: true })
      ]
    : fixture === 'standings-one-class'
      ? [
          headerRow('GT3', '3 cars | 12.40 laps', '#FFAA00'),
          carRow(['1', '#000', 'Kauan Vigliazzi Teixeira Lemos', 'Leader', '-2.0', '1:53.112', '1:53.112', ''], { cellTones: [null, null, null, null, null, 'best-lap', 'best-lap', null] }),
          carRow(['24', '#3094', 'Tech Mates Racing', '+3.4', '0.0', '1:54.228', '1:54.228', ''], { isReference: true, cellTones: [null, null, null, null, null, 'personal-best', 'personal-best', null] }),
          carRow(['49', '#60', 'Tommie Wittens', '+8.9', '+5.5', '1:55.480', '1:56.004', 'IN'], { isPit: true })
        ]
      : fixture === 'standings-three-class'
        ? [
            headerRow('GTP', '2 cars | 9.00 laps', '#FF6274'),
            carRow(['1', '#4', 'Mika Alvarez', 'Leader', '-73.0', '1:38.502', '1:39.004', ''], { cellTones: [null, null, null, null, null, 'best-lap', null, null] }),
            headerRow('LMP2', '2 cars | 10.00 laps', '#33CEFF'),
            carRow(['1', '#8', 'Kousuke Konishi', 'Leader', '-45.0', '1:45.884', '1:46.210', ''], { cellTones: [null, null, null, null, null, 'best-lap', null, null] }),
            headerRow('GT3', '3 cars | 12.40 laps', '#FFAA00'),
            carRow(['1', '#000', 'Kauan Vigliazzi Teixeira Lemos', 'Leader', '-2.0', '1:53.112', '1:53.112', ''], { cellTones: [null, null, null, null, null, 'best-lap', 'best-lap', null] }),
            carRow(['24', '#3094', 'Tech Mates Racing', '+3.4', '0.0', '1:54.228', '1:54.228', ''], { isReference: true, cellTones: [null, null, null, null, null, 'personal-best', 'personal-best', null] }),
            carRow(['49', '#60', 'Tommie Wittens', '+8.9', '+5.5', '1:55.480', '1:56.004', 'IN'], { isPit: true })
          ]
    : raceColumns
    ? [
        headerRow('LMP2', '2 cars | 10.00 laps', '#33CEFF'),
        carRow(['1', '#8', 'Kousuke Konishi', 'Leader', '-45.0', '1:45.884', '1:46.210', ''], { cellTones: [null, null, null, null, null, 'best-lap', null, null] }),
        headerRow('GT3', '3 cars | 12.40 laps', '#FFAA00'),
        carRow(['1', '#000', 'Kauan Vigliazzi Teixeira Lemos', 'Leader', '-2.0', '1:53.112', '1:53.112', ''], { cellTones: [null, null, null, null, null, 'best-lap', 'best-lap', null] }),
        carRow(['24', '#3094', 'Tech Mates Racing', '+3.4', '0.0', '1:54.228', '1:54.228', ''], { isReference: true, cellTones: [null, null, null, null, null, 'personal-best', 'personal-best', null] }),
        carRow(['49', '#60', 'Tommie Wittens', '+8.9', '+5.5', '1:55.480', '1:56.004', 'IN'], { isPit: true })
      ]
    : [
        headerRow('LMP2', '2 cars', '#33CEFF'),
        carRow(['1', '#8', 'Kousuke Konishi', '1:45.884', '1:46.210', ''], { cellTones: [null, null, null, 'best-lap', null, null] }),
        headerRow('GT3', '3 cars', '#FFAA00'),
        carRow(['1', '#000', 'Kauan Vigliazzi Teixeira Lemos', '1:53.112', '1:53.112', ''], { cellTones: [null, null, null, 'best-lap', 'best-lap', null] }),
        carRow(['24', '#3094', 'Tech Mates Racing', '1:54.228', '1:54.228', ''], { isReference: true, cellTones: [null, null, null, 'personal-best', 'personal-best', null] }),
        carRow(['49', '#60', 'Tommie Wittens', '1:55.480', '1:56.004', 'IN'], { isPit: true })
      ];
  return {
    overlayId: 'standings',
    title: 'Standings',
    status: `${statusPrefix} | ${previewLabel}`,
    source,
    bodyKind: 'table',
    columns,
    rows,
    metrics: [],
    shouldRender: true,
    headerItems: [
      { key: 'timeRemaining', value: '06:37:08' }
    ]
  };
}

function normalizePreviewMode(mode) {
  const normalized = String(mode || '').trim().toLowerCase();
  return ['test', 'practice', 'qualifying', 'race'].includes(normalized) ? normalized : 'off';
}

function titleCase(value) {
  const text = String(value || '');
  return text.length ? text.charAt(0).toUpperCase() + text.slice(1) : '';
}

function normalizeUnitSystem(value) {
  return String(value || '').trim().toLowerCase() === 'imperial'
    ? 'Imperial'
    : 'Metric';
}

function isImperial() {
  return reviewAppState.unitSystem === 'Imperial';
}

function formatFuelVolume(liters) {
  if (!Number.isFinite(liters)) return '--';
  return isImperial()
    ? `${(liters * 0.2641720524).toFixed(1)} gal`
    : `${liters.toFixed(1)} L`;
}

function formatFuelPerLap(liters) {
  if (!Number.isFinite(liters)) return '--';
  return isImperial()
    ? `${(liters * 0.2641720524).toFixed(1)} gal/lap`
    : `${liters.toFixed(1)} L/lap`;
}

function formatTemperature(celsius) {
  if (!Number.isFinite(celsius)) return '--';
  return isImperial()
    ? `${Math.round(celsius * 9 / 5 + 32)} F`
    : `${Math.round(celsius)} C`;
}

function formatPressure(bar) {
  if (!Number.isFinite(bar)) return '--';
  return isImperial()
    ? `${Math.round(bar * 14.5037738)} psi`
    : `${bar.toFixed(1)} bar`;
}

function formatDistance(meters) {
  if (!Number.isFinite(meters)) return '--';
  return isImperial()
    ? `${(meters / 1609.344).toFixed(1)} mi`
    : `${(meters / 1000).toFixed(1)} km`;
}

function formatAirPressure(pascals) {
  if (!Number.isFinite(pascals)) return '--';
  return isImperial()
    ? `${(pascals / 3386.389).toFixed(2)} inHg`
    : `${Math.round(pascals / 100)} hPa`;
}

function temperatureTone(celsius) {
  if (!Number.isFinite(celsius)) return 'normal';
  if (celsius >= 50) return 'error';
  if (celsius >= 42) return 'warning';
  if (celsius <= 20 || celsius >= 34) return 'info';
  return 'normal';
}

function temperatureAccentHex(celsius) {
  if (!Number.isFinite(celsius)) return null;
  if (celsius >= 50) return '#FF6274';
  if (celsius >= 42) return '#FF7D49';
  if (celsius >= 34) return '#FFD15B';
  return celsius <= 20 ? '#33CEFF' : '#62FF9F';
}

function formatSpeed(metersPerSecond) {
  if (!Number.isFinite(metersPerSecond)) return '--';
  return isImperial()
    ? `${Math.round(metersPerSecond * 2.2369362921)} mph`
    : `${Math.round(metersPerSecond * 3.6)} km/h`;
}

function headerRow(headerTitle, headerDetail, carClassColorHex) {
  return {
    cells: [],
    isClassHeader: true,
    isReference: false,
    isPit: false,
    isPartial: false,
    carClassColorHex,
    headerTitle,
    headerDetail
  };
}

function carRow(cells, extra = {}) {
  return {
    cells,
    isClassHeader: false,
    isReference: false,
    isPit: false,
    isPartial: false,
    carClassColorHex: null,
    headerTitle: null,
    headerDetail: null,
    ...extra
  };
}

function clampInteger(value, defaultValue, minimum, maximum) {
  const number = Number(value);
  if (!Number.isFinite(number)) return defaultValue;
  return Math.max(minimum, Math.min(maximum, Math.trunc(number)));
}

function reviewTrackMap() {
  return reviewNurburgringTrackMap;
}

function withLiveReload(html) {
  const script = `
  <script>
    (() => {
      const events = new EventSource('/review/events');
      events.addEventListener('reload', () => window.location.reload());
    })();
  </script>`;
  return html.replace('</body>', `${script}\n</body>`);
}

function serveEvents(request, response) {
  response.writeHead(200, {
    'Content-Type': 'text/event-stream',
    'Cache-Control': 'no-cache',
    Connection: 'keep-alive'
  });
  response.write('\n');
  clients.add(response);
  request.on('close', () => clients.delete(response));
}

function broadcastReload() {
  for (const client of clients) {
    client.write('event: reload\ndata: assets\n\n');
  }
}

function serveHtml(response, body) {
  response.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8' });
  response.end(body);
}

function serveJson(response, payload) {
  response.writeHead(200, { 'Content-Type': 'application/json; charset=utf-8' });
  response.end(JSON.stringify(payload));
}

function serveBinary(response, contentType, body) {
  response.writeHead(200, { 'Content-Type': contentType, 'Cache-Control': 'no-store' });
  response.end(body);
}

function serveText(response, status, body) {
  response.writeHead(status, { 'Content-Type': 'text/plain; charset=utf-8' });
  response.end(body);
}

function normalizePath(path) {
  return resolve('/', path).replaceAll('\\', '/');
}
