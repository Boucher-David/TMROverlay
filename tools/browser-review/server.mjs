import { readFileSync, readdirSync, statSync } from 'node:fs';
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
  renderSettingsGeneralReviewHtml
} from '../../tests/browser-overlays/browserOverlayAssets.js';

const port = Number.parseInt(process.env.TMR_BROWSER_REVIEW_PORT || '5177', 10);
const initialReviewUnitSystem = normalizeUnitSystem(process.env.TMR_REVIEW_UNIT_SYSTEM || process.env.TMR_UNIT_SYSTEM || 'Metric');
const reviewAppState = createReviewAppState();
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
  'stream-chat',
  'car-radar',
  'flags',
  'garage-cover',
  'track-map'
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
        reviewState: reviewAppState
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
      updateText: 'No update available.',
      canCheckUpdates: true,
      canInstallUpdate: false,
      canRestartUpdate: false,
      updatePendingRestart: false
    },
    overlays: Object.create(null)
  };
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
      updateText: 'No update available.',
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
        ? 'Diagnostic telemetry will start with live data.'
        : 'Diagnostic telemetry capture disabled.';
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
      support.updateText = 'No update available.';
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

function reviewApiResponse(path, searchParams = new URLSearchParams()) {
  const previewMode = normalizePreviewMode(searchParams.get('preview'));
  if (path === '/api/snapshot') {
    return { live: reviewLiveSnapshot(previewMode) };
  }

  if (path.startsWith('/api/overlay-model/')) {
    const overlayId = decodeURIComponent(path.slice('/api/overlay-model/'.length)).trim().toLowerCase();
    if (!productionOverlayModelIds.has(overlayId)) {
      return null;
    }

    const page = browserOverlayPage(overlayId);
    return { model: reviewDisplayModelWithRootOpacity(page.page.id, previewMode) };
  }

  const page = browserOverlayPages().find((candidate) => candidate.settingsRoute === path);
  if (!page) {
    return null;
  }

  return browserOverlayApiResponse(page.page.id, path, {
    live: reviewLiveSnapshot(previewMode),
    settings: reviewSettings(page.page.id, previewMode),
    model: productionOverlayModelIds.has(page.page.id)
      ? reviewDisplayModelWithRootOpacity(page.page.id, previewMode)
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

function reviewLiveSnapshot(previewMode = 'off') {
  const sessionKind = previewMode === 'off' ? 'practice' : previewMode;
  const sessionType = sessionKind === 'qualifying' ? 'Qualify' : titleCase(sessionKind);
  return freshLiveSnapshot({
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
      throttle: 0.78,
      brake: 0.16,
      clutch: 0,
      steeringWheelAngle: -0.18,
      gear: sessionKind === 'race' ? 6 : 4,
      rpm: sessionKind === 'race' ? 7900 : 7120,
      speedMetersPerSecond: sessionKind === 'race' ? 77.889366 : 63.4,
      brakeAbsActive: true,
      trace: reviewInputTrace()
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
}

function reviewSettings(overlayId, previewMode = 'off') {
  const overlayState = reviewAppState.overlays[overlayId] || {};
  const unitSystem = reviewAppState.unitSystem;
  const session = sessionKeyFromPreview(previewMode);
  if (overlayId === 'stream-chat') {
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
    return {
      hasImage: overlayState.garageHasImage === true,
      imageVersion: overlayState.garageHasImage === true ? 'review' : null,
      fallbackReason: overlayState.garageHasImage === true ? null : 'not_configured',
      previewVisible: overlayState.garagePreviewVisible === true || previewMode !== 'off'
    };
  }

  if (overlayId === 'track-map') {
    return {
      trackMap: reviewTrackMap(),
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
      showMulticlassWarning: contentEnabled(overlayState, 'Faster-class warning', true, [], session)
    };
  }

  if (overlayId === 'flags') {
    return {
      flags: reviewFlagsForSession(session),
      showGreen: contentEnabled(overlayState, 'Green', true),
      showBlue: contentEnabled(overlayState, 'Blue', true),
      showYellow: contentEnabled(overlayState, 'Yellow', true),
      showCritical: contentEnabled(overlayState, 'Red / black', true),
      showFinish: contentEnabled(overlayState, 'White / checkered', true)
    };
  }

  return {
    unitSystem,
    reviewOverlayState: overlayState
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
    const t = index / 10;
    return {
      throttle: Math.max(0, Math.min(1, 0.68 + Math.sin(t) * 0.28)),
      brake: Math.max(0, Math.min(1, Math.sin(t * 0.58 + 1.6) - 0.42)),
      clutch: Math.max(0, Math.min(1, 0.08 + Math.sin(t * 0.35) * 0.06)),
      brakeAbsActive: index > 112 && index < 132
    };
  });
}

function reviewDisplayModelWithRootOpacity(overlayId, previewMode = 'off') {
  const model = reviewDisplayModel(overlayId, previewMode);
  if (!model || opacityExcludedOverlayIds.has(overlayId)) {
    return model ? { ...model, rootOpacity: 1 } : model;
  }

  const overlayState = reviewAppState.overlays[overlayId] || {};
  const percent = Number(overlayState.opacityPercent ?? 100);
  const opacity = Number.isFinite(percent) ? Math.max(0.2, Math.min(1, percent / 100)) : 1;
  return { ...model, rootOpacity: opacity };
}

function reviewDisplayModel(overlayId, previewMode = 'off') {
  if (assetBackedReviewOverlayModelIds.has(overlayId)) {
    return applyReviewChrome(reviewAssetBackedDisplayModel(overlayId, previewMode), overlayId, previewMode);
  }

  const overlayState = reviewAppState.overlays[overlayId] || {};
  const session = sessionKeyFromPreview(previewMode);
  const previewLabel = previewMode === 'off' ? 'review fixture' : `${previewMode} preview`;
  // BrowserOverlayModelFactory is a C# application service and cannot be executed
  // directly from this Node review server. The fallback builders below emit the
  // BrowserOverlayDisplayModel JSON contract used by production browser sources.
  switch (overlayId) {
    case 'standings':
      return applyReviewChrome(filterTableModelContent(filterStandingsReviewRows(standingsDisplayModel(previewLabel), overlayState), 'standings', overlayState, session), overlayId, previewMode);
    case 'relative':
      return applyReviewChrome(filterTableModelContent(filterRelativeReviewRows(relativeDisplayModel(previewLabel, session), overlayState), 'relative', overlayState, session), overlayId, previewMode);
    case 'fuel-calculator':
      {
        const raceRows = [
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
        const stintRows = [
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
        const metricSections = [
          { title: 'Race Information', rows: raceRows }
        ];
        const usageLabel = session === 'qualifying'
          ? 'Quali Usage'
          : session === 'practice'
            ? 'Practice Usage'
            : null;
        if (usageLabel != null) {
          metricSections.push({
            title: 'Fuel Usage',
            rows: [
              metricRow(usageLabel, `min ${formatFuelPerLap(3.0)} | avg ${formatFuelPerLap(3.1)} | max ${formatFuelPerLap(3.2)}`, 'info', [
                metricSegment('Min', formatFuelPerLap(3.0), 'info'),
                metricSegment('Avg', formatFuelPerLap(3.1), 'info'),
                metricSegment('Max', formatFuelPerLap(3.2), 'info'),
                metricSegment('Laps', '3 laps', 'info')
              ])
            ]
          });
        }
        const visibleStintRows = usageLabel != null ? stintRows.slice(0, 1) : stintRows;
        metricSections.push({ title: 'Stint Targets', rows: visibleStintRows });
        return applyReviewChrome(metricsModel(
          'fuel-calculator',
          'Fuel Calculator',
          '3 stints / 2 stops',
          metricSections.flatMap((section) => section.rows),
          `burn ${formatFuelPerLap(3.1)} (measured green lap) | 34.2 laps/tank | history user | gap O0.18 C0.04`,
          [],
          metricSections,
          [
            { key: 'status', value: '3 stints / 2 stops' },
            { key: 'timeRemaining', value: '06:37:08' }
          ]), overlayId, previewMode);
      }
    case 'session-weather':
      {
        const sessionType = session === 'qualifying' ? 'Qualify' : sessionDisplayName(session);
        const sessionName = sessionDisplayName(session);
        const reviewAirTempC = 22;
        const reviewTrackTempC = 31;
        const clock = reviewSessionWeatherClock(session);
        const laps = reviewSessionWeatherLaps(session);
        const rubber = session === 'race' ? 'Moderate Usage' : 'Clean';
        const sessionRows = [
          metricRow('Session', `${sessionType} | ${previewLabel} | team`, 'normal', [
            metricSegment('Type', sessionType, 'normal'),
            metricSegment('Name', previewLabel, 'normal'),
            metricSegment('Mode', 'Team', 'normal')
          ]),
          metricRow('Clock', `${clock.elapsed} elapsed | ${clock.left} left | ${clock.total} total`, 'normal', [
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
          ]),
          metricRow('Laps', `${laps.remaining} left | ${laps.total} total`, 'normal', [
            metricSegment('Remaining', laps.remaining, 'normal'),
            metricSegment('Total', laps.total, 'normal')
          ])
        ];
        const weatherRows = [
          metricRow('Surface', `Unknown | Rubber ${rubber}`, 'normal', [
            metricSegment('Wetness', 'Unknown', 'waiting'),
            metricSegment('Declared', 'Dry', 'normal'),
            metricSegment('Rubber', rubber, 'normal')
          ]),
          metricRow('Sky', 'Mostly Cloudy | Dynamic | rain:0%', 'normal', [
            metricSegment('Skies', 'Mostly Cloudy', 'normal'),
            metricSegment('Weather', 'Dynamic', 'normal'),
            metricSegment('Rain', '0%', 'normal')
          ]),
          metricRow('Wind', `NE | ${formatSpeed(10 / 3.6)} | Head`, 'normal', [
            metricSegment('Dir', 'NE', 'normal'),
            metricSegment('Speed', formatSpeed(10 / 3.6), 'normal'),
            metricSegment('Facing', 'Head', 'normal', { rotationDegrees: 0 })
          ]),
          metricRow('Temps', `air ${formatTemperature(reviewAirTempC)} | track ${formatTemperature(reviewTrackTempC)}`, temperatureTone(reviewTrackTempC), [
            metricSegment('Air', formatTemperature(reviewAirTempC), temperatureTone(reviewAirTempC), { accentHex: temperatureAccentHex(reviewAirTempC) }),
            metricSegment('Track', formatTemperature(reviewTrackTempC), temperatureTone(reviewTrackTempC), { accentHex: temperatureAccentHex(reviewTrackTempC) })
          ]),
          metricRow('Atmosphere', `hum 48% | fog 0% | ${formatAirPressure(101300)}`, 'normal', [
            metricSegment('Hum', '48%', 'normal'),
            metricSegment('Fog', '0%', 'normal'),
            metricSegment('Pressure', formatAirPressure(101300), 'normal')
          ])
        ];
        const metricSections = filterMetricSectionsByContent('session-weather', [
          { title: 'Session', rows: sessionRows },
          { title: 'Weather', rows: weatherRows }
        ], overlayState, session);
        return applyReviewChrome(metricsModel('session-weather', 'Session / Weather', sessionType, metricSections.flatMap((section) => section.rows), '', [], metricSections, [
          { key: 'status', value: sessionType },
          { key: 'timeRemaining', value: clock.left }
        ]), overlayId, previewMode);
      }
    case 'pit-service':
      {
        const pitSections = filterMetricSectionsByContent('pit-service', [
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
            metricRow('Release', 'RED - service active', 'error', undefined, { rowColorHex: '#FF6274' }),
            metricRow('Pit status', 'in progress', 'error', undefined, { rowColorHex: '#FF6274' })
          ]
          },
          {
          title: 'Service Request',
          rows: [
            metricRow('Fuel request', `requested | ${formatFuelVolume(31.6)}`, 'normal', [
              metricSegment('Requested', 'Yes', 'success'),
              metricSegment('Selected', formatFuelVolume(31.6), 'info')
            ]),
            metricRow('Tearoff', 'requested', 'normal', [
              metricSegment('Requested', 'Yes', 'success')
            ]),
            metricRow('Repair', '12s required', 'error', [
              metricSegment('Required', '12s', 'error'),
              metricSegment('Optional', '18s', 'warning')
            ]),
            metricRow('Fast repair', 'selected | available 1', 'normal', [
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
        return applyReviewChrome(metricsModel('pit-service', 'Pit Service', 'service active', pitSections.flatMap((section) => section.rows), 'source: player/team pit service telemetry', [
          {
            title: 'Tire Analysis',
            headers: ['Info', 'FL', 'FR', 'RL', 'RR'],
            rows: tireRows
          }
        ].filter((section) => section.rows.length > 0), pitSections, [
          { key: 'status', value: 'service active' },
          { key: 'timeRemaining', value: '00:03:58' }
        ]), overlayId, previewMode);
      }
    case 'gap-to-leader':
      if (session !== 'race') {
        return applyReviewChrome({
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
        }, overlayId, previewMode);
      }

      if (Number(overlayState.carsAhead ?? 5) <= 0 && Number(overlayState.carsBehind ?? 5) <= 0) {
        return applyReviewChrome({
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
        }, overlayId, previewMode);
      }

      return applyReviewChrome({
        overlayId,
        title: 'Gap To Leader',
        status: 'live | race gap',
        source: `source: live gap telemetry | cars ${reviewGapCarCount(overlayState)}`,
        bodyKind: 'graph',
        columns: [],
        rows: [],
        metrics: [],
        points: reviewGapPoints(overlayState),
        graph: reviewGapGraph(),
        headerItems: [
          { key: 'status', value: 'live | race gap' },
          { key: 'timeRemaining', value: '06:37:08' }
        ],
        shouldRender: true
      }, overlayId, previewMode);
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
  if (Number(overlayState?.carsAhead ?? 5) <= 0 && Number(overlayState?.carsBehind ?? 5) <= 0) {
    return [];
  }

  return [0, 0, 243.63125];
}

function reviewGapGraph() {
  const timestampUtc = new Date('2026-05-17T12:00:00.000Z').toISOString();
  const axisSeconds = 62571.436719;
  const referenceGap = 243.63124999999854;
  const point = (carIdx, gapSeconds, isReference, isClassLeader, classPosition) => ({
    timestampUtc,
    axisSeconds,
    gapSeconds,
    carIdx,
    isReference,
    isClassLeader,
    classPosition,
    startsSegment: true
  });
  const series = [
    reviewGapSeries(8, false, true, 1, [point(8, 0, false, true, 1)]),
    reviewGapSeries(17, false, true, 1, [point(17, 0, false, true, 1)]),
    reviewGapSeries(42, true, false, 24, [point(42, referenceGap, true, false, 24)])
  ];
  return {
    series,
    weather: [],
    leaderChanges: [],
    driverChanges: [],
    startSeconds: axisSeconds,
    endSeconds: 63360.136719,
    maxGapSeconds: 500,
    lapReferenceSeconds: 525.8,
    selectedSeriesCount: series.length,
    trendMetrics: [
      { label: '5L', focusGapChangeSeconds: null, chaser: null, state: 'warming', stateLabel: '0.0L' },
      { label: '10L', focusGapChangeSeconds: null, chaser: null, state: 'warming', stateLabel: '0.0L' },
      { label: 'Pit', focusGapChangeSeconds: null, chaser: null, state: 'pit', stateLabel: null },
      { label: 'PLap', focusGapChangeSeconds: null, chaser: null, state: 'pitLap', stateLabel: null },
      { label: 'Stint', focusGapChangeSeconds: null, chaser: null, state: 'stint', stateLabel: null },
      { label: 'Tire', focusGapChangeSeconds: null, chaser: null, state: 'tire', stateLabel: null },
      { label: 'Last', focusGapChangeSeconds: null, chaser: null, state: 'last', stateLabel: null, comparisonText: '8:13.000' },
      { label: 'Status', focusGapChangeSeconds: null, chaser: null, state: 'status', stateLabel: null, comparisonText: 'Track' }
    ],
    activeThreat: null,
    threatCarIdx: null,
    metricDeadbandSeconds: 0.25,
    comparisonLabel: 'P1',
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

function reviewGapSeries(carIdx, isReference, isClassLeader, classPosition, points) {
  return {
    carIdx,
    isReference,
    isClassLeader,
    classPosition,
    alpha: 0.35,
    isStickyExit: false,
    isStale: false,
    points
  };
}

function applyReviewChrome(model, overlayId, previewMode) {
  if (!supportsSharedChrome(overlayId)) {
    return model;
  }

  const overlayState = reviewAppState.overlays[overlayId] || {};
  const session = sessionKeyFromPreview(previewMode);
  const showStatus = chromeEnabled(overlayState, 'header', 'Status', session, true);
  const showTime = chromeEnabled(overlayState, 'header', 'Time remaining', session, true);
  const showSource = overlayId === 'session-weather'
    ? false
    : chromeEnabled(overlayState, 'footer', 'Source', session, true);
  return {
    ...model,
    source: showSource ? model.source : '',
    headerItems: (model.headerItems || []).filter((item) => {
      const key = String(item?.key || '').toLowerCase();
      if (key === 'status') return showStatus;
      if (key === 'timeremaining') return showTime;
      return true;
    })
  };
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

function reviewAssetBackedDisplayModel(overlayId, previewMode = 'off') {
  const page = browserOverlayPage(overlayId);
  return browserOverlayApiResponse(overlayId, page.modelRoute, {
    live: reviewLiveSnapshot(previewMode),
    settings: reviewSettings(overlayId, previewMode)
  }).model;
}

function relativeDisplayModel(previewLabel = 'review fixture', session = 'practice') {
  const showLapRelationship = session === 'race';
  return {
    overlayId: 'relative',
    title: 'Relative',
    status: `5 - 2/4 cars | ${previewLabel}`,
    source: 'source: review fixture',
    bodyKind: 'table',
    columns: [
      { id: 'relative.position', label: 'Pos', dataKey: 'relative-position', width: 38, alignment: 'right' },
      { id: 'relative.driver', label: 'Driver', dataKey: 'driver', width: 250, alignment: 'left' },
      { id: 'relative.gap', label: 'Delta', dataKey: 'gap', width: 70, alignment: 'right' },
      { id: 'relative.pit', label: 'Pit', dataKey: 'pit', width: 30, alignment: 'right' }
    ],
    rows: [
      relativeRow(['3', '#34 Near Ahead', '-2.350', ''], { carClassColorHex: '#33CEFF', relativeLapDelta: showLapRelationship ? 1 : null }),
      relativeRow(['5', '#55 Focus Driver', '0.000', ''], { isReference: true, carClassColorHex: '#FFDA59', relativeLapDelta: showLapRelationship ? 0 : null }),
      relativeRow(['6', '#61 Near Behind', '+1.200', 'IN'], { carClassColorHex: '#FF4FD8', relativeLapDelta: showLapRelationship ? -2 : null, isPit: true })
    ],
    metrics: [],
    points: [],
    headerItems: [
      { key: 'status', value: `5 - 2/4 cars | ${previewLabel}` },
      { key: 'timeRemaining', value: '06:37:08' }
    ]
  };
}

function filterRelativeReviewRows(model, overlayState) {
  const eachSide = clampInteger(overlayState?.carsEachSide, 5, 0, 8);
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
  const otherClassRows = clampInteger(overlayState?.otherClassRows, 2, 0, 6);
  const rows = model.rows || [];
  const referenceIndex = rows.findIndex((row) => row.isReference);
  const referenceClassHeaderIndex = findClassHeaderBefore(rows, referenceIndex);
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

function findClassHeaderBefore(rows, index) {
  for (let cursor = index; cursor >= 0; cursor -= 1) {
    if (rows[cursor]?.isClassHeader) {
      return cursor;
    }
  }

  return -1;
}

function filterTableModelContent(model, overlayId, overlayState, session = null) {
  const labelForColumn = (column) => tableContentLabel(overlayId, column);
  const columnsWithIndex = (model.columns || []).map((column, index) => ({
    column,
    index,
    contentLabel: labelForColumn(column)
  }));
  const visible = columnsWithIndex.filter((entry) => contentEnabled(overlayState, entry.contentLabel, tableContentDefault(overlayId, entry.contentLabel), [], session));
  const retained = visible.length ? visible : columnsWithIndex.filter((entry) => entry.contentLabel === 'Driver').slice(0, 1);
  return {
    ...model,
    columns: retained.map((entry) => entry.column),
    rows: (model.rows || []).map((row) => row.isClassHeader
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
      interval: 'Focus interval',
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
  return overlayId === 'relative' && label === 'Pit status' ? false : true;
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
    metrics: []
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
  headerItems = [{ key: 'status', value: status }]) {
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
        .map((row) => filterMetricRowByContent(overlayId, row, overlayState, session))
        .filter(Boolean)
    }))
    .filter((section) => section.rows.length > 0);
}

function filterMetricRowByContent(overlayId, row, overlayState, session = null) {
  const metric = Array.isArray(row)
    ? metricRow(row[0], row[1], row[2])
    : row;
  const rowLabels = metricContentLabels(overlayId, metric.label, null);

  if (!Array.isArray(metric.segments) || metric.segments.length === 0) {
    if (contentLabelsEnabled(overlayState, rowLabels, true, session)) {
      return metric;
    }

    return null;
  }

  const segments = metric.segments.filter((segment) => {
    const labels = metricContentLabels(overlayId, metric.label, segment.label);
    return contentLabelsEnabled(overlayState, labels, true, session);
  });
  if (segments.length === 0) {
    return null;
  }

  return {
    ...metric,
    segments,
    value: segments.map((segment) => segment.value).filter(Boolean).join(' | ') || metric.value
  };
}

function filterGridRowsByContent(overlayId, rows, overlayState, session = null) {
  return rows.filter((row) => {
    const labels = metricContentLabels(overlayId, row.label, null);
    return contentLabelsEnabled(overlayState, labels, true, session);
  });
}

function metricContentLabels(overlayId, rowLabel, segmentLabel) {
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

function standingsDisplayModel(previewLabel = 'review fixture') {
  return {
    overlayId: 'standings',
    title: 'Standings',
    status: `scoring | ${previewLabel}`,
    source: 'source: preview fixture extremes',
    bodyKind: 'table',
    columns: [
      { label: 'CLS', dataKey: 'class-position', width: 35, alignment: 'right' },
      { label: 'CAR', dataKey: 'car-number', width: 50, alignment: 'right' },
      { label: 'Driver', dataKey: 'driver', width: 250, alignment: 'left' },
      { label: 'GAP', dataKey: 'gap', width: 60, alignment: 'right' },
      { label: 'INT', dataKey: 'interval', width: 60, alignment: 'right' },
      { label: 'FAST', dataKey: 'fastest-lap', width: 70, alignment: 'right' },
      { label: 'LAST', dataKey: 'last-lap', width: 70, alignment: 'right' },
      { label: 'PIT', dataKey: 'pit', width: 30, alignment: 'right' }
    ],
    rows: [
      headerRow('LMP2', '2 cars | ~10 laps', '#33CEFF'),
      carRow(['1', '#8', 'Kousuke Konishi', 'Leader', '-45.0', '1:45.884', '1:46.210', ''], { cellTones: [null, null, null, null, null, 'best-lap', null, null] }),
      headerRow('GT3', '3 cars | ~12.4 laps', '#FFAA00'),
      carRow(['1', '#000', 'Kauan Vigliazzi Teixeira Lemos', 'Leader', '-2.0', '1:53.112', '1:53.112', ''], { cellTones: [null, null, null, null, null, 'best-lap', 'best-lap', null] }),
      carRow(['24', '#3094', 'Tech Mates Racing', '+3.4', '0.0', '1:54.228', '1:54.228', ''], { isReference: true, cellTones: [null, null, null, null, null, 'personal-best', 'personal-best', null] }),
      carRow(['49', '#60', 'Tommie Wittens', '+8.9', '+5.5', '1:55.480', '1:56.004', 'IN'], { isPit: true })
    ],
    metrics: [],
    headerItems: [
      { key: 'status', value: `scoring | ${previewLabel}` },
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
  return {
    racingLine: {
      closed: true,
      points: [
        { x: 0, y: 48, lapDistPct: 0 },
        { x: 65, y: 92, lapDistPct: 0.16 },
        { x: 132, y: 82, lapDistPct: 0.32 },
        { x: 170, y: 12, lapDistPct: 0.48 },
        { x: 112, y: -48, lapDistPct: 0.64 },
        { x: 28, y: -36, lapDistPct: 0.82 },
        { x: 0, y: 48, lapDistPct: 1 }
      ]
    },
    pitLane: {
      closed: false,
      points: [
        { x: 6, y: 42, lapDistPct: 0.02 },
        { x: 42, y: 24, lapDistPct: 0.08 },
        { x: 88, y: 30, lapDistPct: 0.14 }
      ]
    }
  };
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
