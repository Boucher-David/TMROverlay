import { readFileSync, readdirSync, statSync } from 'node:fs';
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

    const overlayId = overlayIdFromPath(path);
    if (overlayId) {
      serveHtml(response, withLiveReload(renderOverlayHtml(overlayId)));
      return;
    }

    serveText(response, 404, 'Not found');
  } catch (error) {
    serveText(response, 500, error instanceof Error ? error.stack || error.message : String(error));
  }
});

server.listen(port, '127.0.0.1', () => {
  console.log(`Browser review server: http://127.0.0.1:${port}/review`);
  console.log(`Overlay routes:        http://127.0.0.1:${port}/overlays/standings`);
  console.log(`Asset root:            ${browserAssetRoot}`);
});

startAssetPolling();

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

  return session === 'practice' || session === 'qualifying'
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
      ['fuel-calculator.usage.enabled', 'Fuel usage', true]
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

        if (fixture === 'fuel-laps-workbench-laps') {
          return withChrome(fuelLapsWorkbenchReviewModel({ includeLapRows: true, activeWorkbench: 'range' }));
        }

        if (fixture === 'fuel-laps-workbench-range') {
          return withChrome(fuelLapsWorkbenchReviewModel({ activeWorkbench: 'range' }));
        }

        if (fixture === 'fuel-laps-workbench-target') {
          return withChrome(fuelLapsWorkbenchReviewModel({ activeWorkbench: 'target' }));
        }

        if (fixture === 'fuel-laps-workbench-sector') {
          return withChrome(fuelLapsWorkbenchReviewModel({ activeWorkbench: 'sector' }));
        }

        if (!fixture || fixture === 'fuel-laps-workbench' || fixture === 'fuel-laps-workbench-pit') {
          return withChrome(fuelLapsWorkbenchReviewModel({ activeWorkbench: 'pit' }));
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
      five: '2.29 3/5',
      ten: null,
      max: 2.26
    }),
    fuelRangeWorkbenchRow('Dallara 45m / Stop 1', 'race / post-stop snapshot', 'info', {
      fuel: 33.29,
      rangeV1: 2.44,
      last: 2.42,
      five: '2.44 3/5',
      ten: null,
      max: 2.41
    }),
    fuelRangeWorkbenchRow('Dallara 45m / Half Rem', 'race / remaining-time midpoint', 'info', {
      fuel: 20.36,
      rangeV1: 1.50,
      last: 1.48,
      five: '1.49 3/5',
      ten: null,
      max: 1.48
    }),
    fuelRangeWorkbenchRow('Dallara 4L full / Mid S1', 'fixed race / partial 5L', 'info', {
      fuel: 30.98,
      rangeV1: 2.43,
      last: 2.51,
      five: '2.45 3/5',
      ten: null,
      max: 2.39
    }),
    fuelRangeWorkbenchRow('Dallara 4L full / Half', 'fixed race / partial 5L', 'info', {
      fuel: 26.21,
      rangeV1: 2.05,
      last: 2.12,
      five: '2.07 3/5',
      ten: null,
      max: 2.02
    }),
    fuelRangeWorkbenchRow('Dallara 4L full / Stop 1', 'fixed race / late snapshot', 'info', {
      fuel: 15.27,
      rangeV1: 1.20,
      last: 1.24,
      five: '1.21 3/5',
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
      max: 3.40
    })
  ];
  const targetUsageCurrentRows = [
    fuelTargetUsageWorkbenchRow('VLN 4h team / 5-lap stretch', 'current fuel / no reserve', 'info', {
      budget: 61.6400,
      budgetLabel: 'Fuel',
      referenceBurn: 13.5176
    }),
    fuelTargetUsageWorkbenchRow('Dallara 45m / Stop edge', 'current fuel / no reserve', 'info', {
      budget: 33.2921,
      budgetLabel: 'Fuel',
      referenceBurn: 13.7571
    }),
    fuelTargetUsageWorkbenchRow('Dallara 4L full / 2-lap edge', 'current fuel / no reserve', 'info', {
      budget: 26.2104,
      budgetLabel: 'Fuel',
      referenceBurn: 12.3634
    }),
    fuelTargetUsageWorkbenchRow('Dallara 4L blip / Abnormal', 'abnormal stop / no reserve', 'warning', {
      budget: 29.5762,
      budgetLabel: 'Fuel',
      referenceBurn: 13.5671
    })
  ];
  const targetUsageCapRows = [
    fuelTargetUsageWorkbenchRow('VLN 4h team / Green start', 'first green fuel / no reserve', 'info', {
      budget: 102.8464,
      budgetLabel: 'Green',
      referenceBurn: 13.5176
    }),
    fuelTargetUsageWorkbenchRow('Dallara 45m / Green start', 'first green fuel / no reserve', 'info', {
      budget: 58.9871,
      budgetLabel: 'Green',
      referenceBurn: 13.7593
    }),
    fuelTargetUsageWorkbenchRow('Dallara 4L full / Green start', 'first green fuel / no reserve', 'info', {
      budget: 50.1023,
      budgetLabel: 'Green',
      referenceBurn: 12.3421
    }),
    fuelTargetUsageWorkbenchRow('Dallara quali seed / Green est', 'expected green fuel / seed burn', 'warning', {
      budget: 50.1023,
      budgetLabel: 'Green',
      referenceBurn: 13.7982,
      referenceLabel: 'Quali'
    })
  ];
  const targetUsageSections = [
    { title: 'Target Usage - Green Start', rows: targetUsageCapRows },
    { title: 'Target Usage - Current Edges', rows: targetUsageCurrentRows }
  ];
  const pitRequestRows = [
    fuelPitRequestWorkbenchRow('VLN 4h team / 7-lap stint', '61.6 L / 7 laps / no reserve', 'info', {
      currentFuel: 61.64,
      tankCapacity: 104.94,
      targetLaps: 7,
      startBurn: { value: 13.6372, label: 'seed' },
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
      startBurn: { value: 14.2142, label: 'history' },
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
      startBurn: { value: 13.8141, label: 'seed' },
      sectorBurn: { value: 13.8978, label: 'sector' },
      lastBurn: 13.7571,
      fiveBurn: { value: 13.6443, label: '3/5' },
      tenBurn: null,
      v1Burn: 13.6443
    }),
    fuelPitRequestWorkbenchRow('Dallara 45m / 2-lap edge', '20.4 L / 2 laps / no reserve', 'info', {
      currentFuel: 20.36,
      tankCapacity: 60.0,
      targetLaps: 2,
      startBurn: { value: 13.7568, label: 'seed' },
      sectorBurn: { value: 13.8978, label: 'sector' },
      lastBurn: 13.7568,
      fiveBurn: { value: 13.6644, label: '3/5' },
      tenBurn: null,
      v1Burn: 13.5733
    }),
    fuelPitRequestWorkbenchRow('Dallara 4L full / 2-lap finish', '15.3 L / 2 laps / no reserve', 'info', {
      currentFuel: 15.27,
      tankCapacity: 51.0,
      targetLaps: 2,
      startBurn: { value: 12.9407, label: 'seed' },
      sectorBurn: { value: 12.4393, label: 'sector' },
      lastBurn: 12.3145,
      fiveBurn: { value: 12.6198, label: '3/5' },
      tenBurn: null,
      v1Burn: 12.7250
    }),
    fuelPitRequestWorkbenchRow('Dallara 4L blip / Repair edge', '18.9 L / 2 laps / abnormal', 'warning', {
      currentFuel: 18.86,
      tankCapacity: 51.0,
      targetLaps: 2,
      startBurn: { value: 13.5683, label: 'seed' },
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
      startBurn: { value: 13.7982, label: 'quali' },
      sectorBurn: { value: 13.7982, label: 'quali' },
      lastBurn: null,
      fiveBurn: null,
      tenBurn: null,
      v1Burn: null
    })
  ];
  const pitRequestMockRows = [
    fuelPitRequestWorkbenchRow('Mock GT3 6h / Stop 1 full stint', '18.0 L / 7 laps / stop 1 of 3', 'modeled', {
      currentFuel: 18.0,
      tankCapacity: 104.9,
      targetLaps: 7,
      startBurn: { value: 13.9, label: 'seed' },
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
      startBurn: { value: 13.7, label: 'seed' },
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
      startBurn: { value: 13.8, label: 'seed' },
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
      startBurn: { value: 13.9, label: 'seed' },
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
      startBurn: { value: 13.5, label: 'seed' },
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
      startBurn: { value: 14.21, label: 'history' },
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
      startBurn: { value: 14.21, label: 'history' },
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
      startBurn: { value: 14.21, label: 'history' },
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
      startBurn: { value: 14.21, label: 'history' },
      sectorBurn: { value: 14.60, label: 'bridge' },
      lastBurn: { value: 14.10, label: 'bridge lap' },
      fiveBurn: { value: 14.18, label: 'bridge 5L' },
      tenBurn: null,
      v1Burn: null
    }),
    fuelPitRequestWorkbenchRow('Mock NASCAR / Repair caution', '72.1 L / 68 laps / oval yellow', 'warning', {
      currentFuel: 72.1,
      tankCapacity: 75.7,
      targetLaps: 68,
      reserveFuel: 0.5,
      startBurn: { value: 1.12, label: 'seed' },
      sectorBurn: { value: 0.86, label: 'caution' },
      lastBurn: 1.07,
      fiveBurn: 1.04,
      tenBurn: 1.02,
      minBurn: { value: 0.86, label: 'caution' },
      v1Burn: 1.08
    }),
    fuelPitRequestWorkbenchRow('Mock NASCAR / 3 green + 2 caution', '72.1 L / 68 laps / 1.0L caution', 'warning', {
      currentFuel: 72.1,
      tankCapacity: 75.7,
      targetLaps: 68,
      reserveFuel: 0.5,
      startBurn: { value: 1.12, label: 'seed' },
      sectorBurn: { value: 1.0, label: 'caution' },
      lastBurn: { value: 1.0, label: 'caution' },
      fiveBurn: { value: 1.046, label: '3G+2Y' },
      tenBurn: null,
      minBurn: { value: 1.0, label: 'caution' },
      v1Burn: 1.08
    }),
    fuelPitRequestWorkbenchRow('Mock NASCAR / 5L green + 1L caution', '72.1 L / 68 laps / stress mix', 'warning', {
      currentFuel: 72.1,
      tankCapacity: 75.7,
      targetLaps: 68,
      reserveFuel: 0.5,
      startBurn: { value: 5.0, label: 'green' },
      sectorBurn: { value: 1.0, label: 'caution' },
      lastBurn: { value: 1.0, label: 'caution' },
      fiveBurn: { value: 3.4, label: '3G+2Y' },
      tenBurn: null,
      maxBurn: { value: 5.0, label: 'green' },
      minBurn: { value: 1.0, label: 'caution' },
      v1Burn: 5.0
    }),
    fuelPitRequestWorkbenchRow('Mock GR86 Nord / 16-lap edge', '81.1 L / 16 laps / cap pressure', 'warning', {
      currentFuel: 81.1,
      tankCapacity: 83.3,
      targetLaps: 16,
      startBurn: { value: 5.46, label: 'history' },
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
      startBurn: { value: 3.60, label: 'seed' },
      sectorBurn: { value: 3.50, label: 'history' },
      lastBurn: 3.54,
      fiveBurn: { value: 3.53, label: '3/5' },
      tenBurn: null,
      v1Burn: 3.54
    }),
    fuelPitRequestWorkbenchRow('Mock pit mistake / One lap short', '15.0 L / 2 laps / just refueled', 'error', {
      currentFuel: 15.0,
      tankCapacity: 60.0,
      targetLaps: 2,
      reserveFuel: 1.0,
      pitLaneFuel: 0.4,
      startBurn: { value: 13.8, label: 'seed' },
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
    : activeWorkbench === 'pit'
      ? [
          { title: 'Fuel To Add Workbench', rows: pitRequestRows },
          { title: 'Fuel To Add Hypotheticals', rows: pitRequestMockRows }
        ]
    : [
        ...targetUsageSections
      ];
  const gridSections = activeWorkbench === 'sector' ? sectorBurnGridSections : [];
  const chartSections = [];
  const rows = metricSections.flatMap((section) => section.rows);
  const workbenchStatus = activeWorkbench === 'sector'
    ? 'fuel/sector burn workbench'
    : activeWorkbench === 'range'
      ? 'fuel/range workbench'
      : activeWorkbench === 'pit'
        ? 'fuel/pit request workbench'
        : 'fuel/target usage workbench';
  const workbenchSource = activeWorkbench === 'sector'
    ? 'source: Fuel V2 workbench; mirrors staged Core sector-burn logic. Rows are real SplitTimeInfo sector boundaries and cells show Live projected L/lap for each lap. Sector labels include median replay speed as context; green cell borders mark pit/refuel/service event-window overlap, not inherently bad sector numbers. Lap 1 uses low-confidence track-percent fallback; later laps hold at S0 and use prior-lap same-sector cumulative scaling from S0+S1 onward. Actual is completed lap burn.'
    : activeWorkbench === 'range'
      ? 'source: Fuel V2 workbench; mirrors staged Core range logic. Current-tank range compares V1 selected burn with V2 Last/5L/10L/Max windows'
      : activeWorkbench === 'pit'
        ? 'source: Fuel V2 workbench; mirrors staged Core pit-request logic. Fuel To Add is target laps times selected burn plus optional reserve/pit-lane adjustment minus current fuel, clamped at zero and marked if the tank cannot hold the request. Visible cells use the shared burn buckets: Last, 5L, 10L, Max, Min, and Quali. Sector/live/bridge evidence can feed a labeled bucket, but it is no longer a standalone display column. Real capture rows keep reserve and learned pit-lane burn at zero; hypothetical rows may exercise those inputs.'
        : 'source: Fuel V2 workbench; mirrors staged Core target-usage logic. Target Usage cells are required L/lap from fuel budget / target laps, no reserve subtracted; Last is the live burn comparator when available';
  const workbenchTitle = activeWorkbench === 'sector'
    ? 'Sector Burn V2'
    : activeWorkbench === 'range'
      ? 'Range V2'
      : activeWorkbench === 'pit'
        ? 'Fuel To Add V2'
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

function fuelLapsWorkbenchTone(value, realValue) {
  if (value === '--') return 'waiting';
  const valueText = String(value || '').toLowerCase();
  if (valueText.includes('degraded') || valueText.includes('held')) return 'warning';
  const modeled = Number.parseFloat(value);
  const actual = Number.parseFloat(realValue);
  if (!Number.isFinite(modeled) || !Number.isFinite(actual)) return 'info';
  const delta = Math.abs(modeled - actual);
  if (delta <= 0.005) return 'success';
  return delta <= 1 ? 'warning' : 'error';
}

function fuelPerLapWorkbenchRow(label, value, tone, windowValues, finalValue) {
  const windowLabels = ['Last', '5L', '10L', 'Max'];
  const segments = windowLabels
    .map((label, index) => metricSegment(
      label,
      fuelLapsWorkbenchFormatCell(windowValues[index]),
      fuelPerLapWorkbenchTone(windowValues[index], finalValue)));
  segments.push(metricSegment('V1 Ref', fuelLapsWorkbenchFormatCell(finalValue), 'modeled'));

  return metricRow(
    label,
    value,
    tone,
    segments);
}

function fuelPerLapWorkbenchTone(value, finalValue) {
  if (value === '--') return 'waiting';
  const text = String(value || '').toLowerCase();
  if (text.includes('partial')
    || text.includes('low')
    || text.includes('stint')
    || text.includes('high')
    || text.includes('one')
    || text.includes('quali')) {
    return 'warning';
  }
  const modeled = Number.parseFloat(value);
  const actual = Number.parseFloat(finalValue);
  if (!Number.isFinite(modeled) || !Number.isFinite(actual)) return 'info';
  const delta = Math.abs(modeled - actual);
  if (delta <= 0.05) return 'success';
  return delta <= 0.50 ? 'warning' : 'error';
}

function fuelRangeWorkbenchRow(label, value, tone, range) {
  const segments = [
    metricSegment('Fuel', fuelRangeVolume(range.fuel), fuelRangeValueTone(range.fuel)),
    metricSegment('V1 Ref', fuelRangeLaps(range.rangeV1), fuelRangeValueTone(range.rangeV1)),
    metricSegment('Last', fuelRangeLaps(range.last), fuelRangeV2WindowTone(range.last, 'last')),
    metricSegment('5L', fuelRangeLaps(range.five), fuelRangeV2WindowTone(range.five, 'five')),
    metricSegment('10L', fuelRangeLaps(range.ten), fuelRangeV2WindowTone(range.ten, 'ten')),
    metricSegment('Max', fuelRangeLaps(range.max), fuelRangeV2WindowTone(range.max, 'max'))
  ];

  return metricRow(
    label,
    value,
    tone,
    segments);
}

function fuelTargetUsageWorkbenchRow(label, value, tone, targetUsage) {
  const referenceLabel = targetUsage.referenceLabel || 'Last';
  const referenceTone = fuelTargetUsageReferenceTone(targetUsage);
  const targetSegments = fuelTargetUsageTargetLaps(targetUsage)
    .map((laps) => {
      const requiredBurn = fuelTargetUsageRequiredBurn(targetUsage.budget, laps);
      return metricSegment(
        fuelTargetUsageLapLabel(laps),
        fuelRangeFuelPerLap(requiredBurn),
        fuelTargetUsageTone(requiredBurn, targetUsage.referenceBurn));
    });
  const segments = [
    metricSegment(
      targetUsage.budgetLabel || 'Fuel',
      fuelRangeVolume(targetUsage.budget),
      fuelRangeValueTone(targetUsage.budget)),
    metricSegment(
      referenceLabel,
      fuelRangeFuelPerLap(targetUsage.referenceBurn),
      referenceTone),
    ...targetSegments
  ];

  return metricRow(
    label,
    value,
    tone,
    segments);
}

function fuelPitRequestWorkbenchRow(label, value, tone, request) {
  const segments = [
    metricSegment('Last', fuelPitRequestAddLabel(request, request.lastBurn, 'last'), fuelPitRequestTone(request, request.lastBurn, 'last')),
    metricSegment('5L', fuelPitRequestAddLabel(request, request.fiveBurn, 'five'), fuelPitRequestTone(request, request.fiveBurn, 'five')),
    metricSegment('10L', fuelPitRequestAddLabel(request, request.tenBurn, 'ten'), fuelPitRequestTone(request, request.tenBurn, 'ten')),
    metricSegment('Max', fuelPitRequestAddLabel(request, fuelPitRequestMaxBurn(request), 'max'), fuelPitRequestTone(request, fuelPitRequestMaxBurn(request), 'max')),
    metricSegment('Min', fuelPitRequestAddLabel(request, fuelPitRequestMinBurn(request), 'min'), fuelPitRequestTone(request, fuelPitRequestMinBurn(request), 'min')),
    metricSegment('Quali', fuelPitRequestAddLabel(request, fuelPitRequestQualiBurn(request), 'quali'), fuelPitRequestTone(request, fuelPitRequestQualiBurn(request), 'quali'))
  ];

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

function fuelTargetUsageReferenceTone(targetUsage) {
  if (!Number.isFinite(targetUsage.referenceBurn)) return 'waiting';
  return targetUsage.referenceLabel ? 'warning' : 'info';
}

function fuelTargetUsageTargetLaps(targetUsage) {
  if (Array.isArray(targetUsage.targetLaps) && targetUsage.targetLaps.length > 0) {
    return targetUsage.targetLaps;
  }

  const fuelBudget = Number(targetUsage.budget);
  const referenceBurn = Number(targetUsage.referenceBurn);
  if (!Number.isFinite(fuelBudget) || fuelBudget <= 0 || !Number.isFinite(referenceBurn) || referenceBurn <= 0) {
    return [];
  }

  const projectedLaps = fuelBudget / referenceBurn;
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

function fuelPitRequestMaxBurn(request) {
  if (request?.maxBurn !== undefined) return request.maxBurn;
  return fuelPitRequestExtremeBurn([
    request?.lastBurn,
    request?.fiveBurn,
    request?.tenBurn,
    request?.startBurn,
    request?.sectorBurn,
    request?.qualiBurn
  ], 'max');
}

function fuelPitRequestMinBurn(request) {
  if (request?.minBurn !== undefined) return request.minBurn;
  return fuelPitRequestExtremeBurn([
    request?.lastBurn,
    request?.fiveBurn,
    request?.tenBurn
  ], 'min');
}

function fuelPitRequestQualiBurn(request) {
  if (request?.qualiBurn !== undefined) return request.qualiBurn;
  return fuelPitRequestBurnLabel(request?.startBurn).toLowerCase() === 'quali'
    ? request.startBurn
    : null;
}

function fuelPitRequestExtremeBurn(burns, mode) {
  const candidates = burns
    .map((burn) => ({ burn, value: fuelPitRequestBurnValue(burn) }))
    .filter((candidate) => Number.isFinite(candidate.value) && candidate.value > 0);
  if (candidates.length === 0) return null;

  return candidates.reduce((selected, candidate) => {
    return mode === 'min'
      ? candidate.value < selected.value ? candidate : selected
      : candidate.value > selected.value ? candidate : selected;
  }).burn;
}

function fuelPitRequestAddLabel(request, burn, window) {
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

function fuelPitRequestTone(request, burn, window) {
  const add = fuelPitRequestAddAmount(request, burn);
  if (!add) return 'waiting';
  if (add.limited) return 'error';
  if (add.amount <= 0.001) return 'success';
  if (fuelPitRequestBurnLabel(burn)) return 'warning';
  return window === 'max' || window === 'min' || window === 'quali' ? 'warning' : 'info';
}

function fuelPitRequestAddAmount(request, burn) {
  const currentFuelInput = request?.currentFuel;
  const currentFuel = currentFuelInput === null || currentFuelInput === undefined
    ? Number.NaN
    : Number(currentFuelInput);
  const targetLaps = Number(request?.targetLaps);
  const burnValue = fuelPitRequestBurnValue(burn);
  if (!Number.isFinite(currentFuel)
    || currentFuel < 0
    || !Number.isFinite(targetLaps)
    || targetLaps <= 0
    || !Number.isFinite(burnValue)
    || burnValue <= 0) {
    return null;
  }

  const reserve = fuelPitRequestNonNegative(request?.reserveFuel);
  const pitLane = fuelPitRequestNonNegative(request?.pitLaneFuel);
  const targetFuel = targetLaps * burnValue + reserve + pitLane;
  const rawAdd = Math.max(0, targetFuel - currentFuel);
  const tankCapacity = Number(request?.tankCapacity);
  const room = Number.isFinite(tankCapacity) && tankCapacity >= 0
    ? Math.max(0, tankCapacity - currentFuel)
    : null;
  const limited = room !== null && rawAdd > room + 0.001;
  return {
    amount: limited ? room : rawAdd,
    limited,
    targetFuel
  };
}

function fuelPitRequestBurnValue(burn) {
  if (typeof burn === 'object' && burn !== null) {
    return Number(burn.value);
  }

  return Number(burn);
}

function fuelPitRequestBurnLabel(burn) {
  if (typeof burn === 'object' && burn !== null) {
    return String(burn.label || '').trim();
  }

  return '';
}

function fuelPitRequestNonNegative(value) {
  const numeric = Number(value);
  return Number.isFinite(numeric) && numeric > 0 ? numeric : 0;
}

function fuelRangeValueTone(value) {
  return Number.isFinite(value) ? 'info' : 'waiting';
}

function fuelRangeV2WindowTone(value, window) {
  if (typeof value === 'string' && value.includes('/')) return 'warning';
  if (!Number.isFinite(value)) return 'waiting';
  return window === 'max' ? 'warning' : 'info';
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
      finishLap = heldFinishLap;
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
  return ['practice', 'qualifying', 'race'].includes(normalized) ? normalized : 'off';
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
