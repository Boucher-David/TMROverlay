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
  settingsBrowserSourceSize
} from '../../tests/browser-overlays/browserOverlayAssets.js';

const port = Number.parseInt(process.env.TMR_BROWSER_REVIEW_PORT || '5177', 10);
const initialReviewUnitSystem = normalizeUnitSystem(process.env.TMR_REVIEW_UNIT_SYSTEM || process.env.TMR_UNIT_SYSTEM || 'Metric');
const reviewAppState = createReviewAppState();
const reviewNurburgringTrackMap = JSON.parse(readFileSync(
  resolve(repoRoot, 'fixtures/screenshot-scenarios/track-map-nurburgring-24h.json'),
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
  ['gap-trend-row-off', [
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
        { carIdx: 91, relativeSeconds: 1.7, carClassColorHex: '#ffaa00' },
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
    const sides = {
      left: [true, false],
      right: [false, true],
      'both-sides': [true, true],
      clear: [false, false]
    }[fixture.replace('car-radar-', '')];
    if (sides) {
      const [hasCarLeft, hasCarRight] = sides;
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
        cars: [],
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

  if (session === 'qualifying') {
    return [
      { kind: 'yellow', category: 'yellow', label: 'Yellow', detail: null, tone: 'warning' },
      blue
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
    || unavailableContentPolicy(model) === 'section-aware-placeholders') {
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
  if (!model || !Array.isArray(model.rows) || model.rows.length <= 0) {
    return fallbackHeight;
  }

  const hasHeader = Array.isArray(model.headerItems)
    && model.headerItems.some((item) => String(item?.value || '').trim());
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

  const sections = Array.isArray(model?.metricSections)
    ? model.metricSections.filter((section) => Array.isArray(section?.rows) && section.rows.length > 0)
    : [];
  const rowCount = sections.reduce((total, section) => total + section.rows.length, 0);
  if (rowCount <= 0 || sections.length <= 0) {
    return fallbackHeight;
  }

  let height = fuelContentHeight(rowCount, sections.length);
  const hasHeader = Array.isArray(model?.headerItems)
    && model.headerItems.some((item) => String(item?.value || '').trim());
  if (!hasHeader) {
    height = Math.max(80, height - 38);
  }

  return height;
}

function fuelContentHeight(rowCount, sectionCount) {
  if (rowCount <= 0 || sectionCount <= 0) return 126;
  const rowGaps = Math.max(0, rowCount - sectionCount) * 5;
  const sectionGaps = Math.max(0, sectionCount - 1) * 8;
  const height = 38 + 26 + sectionCount * 14 + rowCount * 35 + rowGaps + sectionGaps + 8;
  return Math.max(126, Math.min(315, height));
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
    ...(model?.gridSections || [])
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
  const estimatedContentHeight = Math.max(0, 38 + contentRowCount * 30 + sectionCount * 18);
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

function hasSemanticRenderedContent(model) {
  return Boolean(
    (model?.rows || []).length
    || (model?.points || []).length
    || (model?.metrics || []).length
    || (model?.metricSections || []).some((section) => (section.rows || []).length)
    || (model?.gridSections || []).some((section) => (section.rows || []).length)
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

  if (overlayId === 'garage-cover' && fixtureVariant(searchParams).startsWith('garage-')) {
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
        return withChrome(filterTableModelContent(filterRelativeReviewRows(relativeDisplayModel(previewLabel, session), effectiveOverlayState), 'relative', effectiveOverlayState, session));
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

function metricsModel(
  overlayId,
  title,
  status,
  metrics,
  source = 'source: review fixture',
  gridSections = [],
  metricSections = [],
  headerItems = [],
  shouldRender = true) {
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
