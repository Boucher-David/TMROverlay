import { readFileSync, statSync } from 'node:fs';
import { createServer } from 'node:http';
import { resolve } from 'node:path';
import {
  browserAssetRoot,
  browserOverlayApiResponse,
  browserOverlayPage,
  browserOverlayPages,
  carRadarRenderModelFromState,
  repoRoot,
  renderOverlayHtml,
  renderOverlayIndexHtml
} from '../../tests/browser-overlays/browserOverlayAssets.js';

const replayPath = resolve(process.argv[2] || process.env.TMR_STANDINGS_REPLAY_JSON || '');
const port = Number.parseInt(process.env.TMR_STANDINGS_REPLAY_PORT || '5187', 10);
const frameMilliseconds = Number.parseInt(process.env.TMR_STANDINGS_REPLAY_FRAME_MS || '500', 10);
const requestedReplayTimingMode = normalizeReplayTimingMode(process.env.TMR_STANDINGS_REPLAY_TIMING || 'source');
const replaySpeedMultiplier = positiveNumber(process.env.TMR_STANDINGS_REPLAY_SPEED, 60);
const replayWindowSourceStart = optionalNumber(process.env.TMR_STANDINGS_REPLAY_SOURCE_START);
const replayWindowSourceEnd = optionalNumber(process.env.TMR_STANDINGS_REPLAY_SOURCE_END);
const replayWindowFrameStart = optionalInteger(process.env.TMR_STANDINGS_REPLAY_FRAME_START);
const replayWindowFrameEnd = optionalInteger(process.env.TMR_STANDINGS_REPLAY_FRAME_END);
const relativeCarsEachSide = clampInteger(process.env.TMR_RELATIVE_CARS_EACH_SIDE, 3, 0, 8);
const relativeShowPitColumn = parseBoolean(process.env.TMR_RELATIVE_SHOW_PIT_COLUMN, false);
const relativeShowHeaderTimeRemaining = parseBoolean(process.env.TMR_RELATIVE_SHOW_TIME_REMAINING, true);
const gapMissingSegmentThresholdSeconds = 10;
const gapGraphTrendWindowSeconds = positiveNumber(process.env.TMR_GAP_GRAPH_WINDOW_SECONDS, 4 * 60 * 60);
const gapGraphMaxContexts = clampInteger(process.env.TMR_GAP_GRAPH_MAX_CONTEXTS, 36000, 120, 60000);
const gapCarsAhead = clampInteger(process.env.TMR_GAP_CARS_AHEAD, 5, 0, 12);
const gapCarsBehind = clampInteger(process.env.TMR_GAP_CARS_BEHIND, 5, 0, 12);
const gapShowHeaderTimeRemaining = parseBoolean(process.env.TMR_GAP_SHOW_TIME_REMAINING, true);
const fuelShowHeaderTimeRemaining = parseBoolean(process.env.TMR_FUEL_SHOW_TIME_REMAINING, true);
const sessionWeatherShowHeaderTimeRemaining = parseBoolean(process.env.TMR_SESSION_WEATHER_SHOW_TIME_REMAINING, true);
const sessionWeatherDisabledContent = csvSet(process.env.TMR_SESSION_WEATHER_DISABLED_CELLS || '');
const pitServiceShowHeaderTimeRemaining = parseBoolean(process.env.TMR_PIT_SERVICE_SHOW_TIME_REMAINING, true);
const pitServiceDisabledContent = csvSet(process.env.TMR_PIT_SERVICE_DISABLED_CELLS || '');
const streamChatProvider = normalizeStreamChatProvider(process.env.TMR_STREAM_CHAT_PROVIDER || process.env.TMR_REVIEW_STREAM_CHAT_PROVIDER || 'live-review');
const streamChatTwitchChannel = normalizeTwitchChannel(process.env.TMR_STREAM_CHAT_TWITCH_CHANNEL || process.env.TMR_STREAM_CHAT_CHANNEL || 'techmatesracing');
const streamChatStreamlabsUrl = normalizeStreamlabsUrl(process.env.TMR_STREAM_CHAT_STREAMLABS_URL || '');
const streamChatFixtureMode = String(process.env.TMR_STREAM_CHAT_FIXTURE || '').trim().toLowerCase();
const streamChatLiveSource = createStreamChatLiveSource();
const reviewUnitSystem = normalizeUnitSystem(process.env.TMR_REVIEW_UNIT_SYSTEM || process.env.TMR_UNIT_SYSTEM || 'Metric');
const inputStateReviewSettings = {
  showThrottleTrace: parseBoolean(process.env.TMR_INPUT_SHOW_THROTTLE_TRACE, true),
  showBrakeTrace: parseBoolean(process.env.TMR_INPUT_SHOW_BRAKE_TRACE, true),
  showClutchTrace: parseBoolean(process.env.TMR_INPUT_SHOW_CLUTCH_TRACE, true),
  showThrottle: parseBoolean(process.env.TMR_INPUT_SHOW_THROTTLE, true),
  showBrake: parseBoolean(process.env.TMR_INPUT_SHOW_BRAKE, true),
  showClutch: parseBoolean(process.env.TMR_INPUT_SHOW_CLUTCH, true),
  showSteering: parseBoolean(process.env.TMR_INPUT_SHOW_STEERING, true),
  showGear: parseBoolean(process.env.TMR_INPUT_SHOW_GEAR, true),
  showSpeed: parseBoolean(process.env.TMR_INPUT_SHOW_SPEED, true)
};
const productionOverlayModelIds = new Set(browserOverlayPages()
  .filter((page) => page.modelRoute)
  .map((page) => page.page.id));
const assetBackedReplayOverlayModelIds = new Set([
  'fuel-calculator',
  'track-map',
  'garage-cover',
  'stream-chat'
]);
const replay = loadReplay(replayPath);
const replayCaptureSessionInfo = loadReplayCaptureSessionInfo(replay);
const replayTiming = analyzeReplayTiming(replay);
const replayTrackMapTiming = buildReplayTrackMapTiming(replay, replayCaptureSessionInfo);
const replayTrackMapMarkerAlerts = buildReplayTrackMapMarkerAlerts(replay);
const startedAtMs = Date.now();
startStreamChatLiveSource();

const server = createServer((request, response) => {
  const url = new URL(request.url || '/', `http://${request.headers.host || `localhost:${port}`}`);
  const path = normalizePath(url.pathname);
  try {
    if (path === '/' || path === '/review' || path === '/review/overlays') {
      serveHtml(response, renderOverlayIndexHtml(port));
      return;
    }

    const overlayId = overlayIdFromPath(path);
    if (overlayId) {
      browserOverlayPage(overlayId);
      serveHtml(response, renderOverlayHtml(overlayId));
      return;
    }

    if (path === '/api/snapshot') {
      const { frame, index } = currentFrame(url, request);
      serveJson(response, { live: liveSnapshot(frame, index, url.searchParams) });
      return;
    }

    if (path === '/api/garage-cover/default-image') {
      serveBinary(
        response,
        'image/png',
        readFileSync(resolve(repoRoot, 'assets/brand/Team_Logo_4k_TMRBRANDING.png')));
      return;
    }

    if (path.startsWith('/api/overlay-model/')) {
      const overlayId = decodeURIComponent(path.slice('/api/overlay-model/'.length)).trim().toLowerCase();
      if (!productionOverlayModelIds.has(overlayId)) {
        serveText(response, 404, 'Overlay model not configured');
        return;
      }

      const { frame, index } = currentFrame(url, request);
      serveJson(response, {
        generatedAtUtc: new Date().toISOString(),
        replay: frameMetadata(frame, index, url.searchParams),
        model: displayModel(overlayId, frame, index, url.searchParams)
      });
      return;
    }

    const settingsPage = browserOverlayPages().find((candidate) => candidate.settingsRoute === path);
    if (settingsPage) {
      const { frame, index } = currentFrame(url, request);
      serveJson(response, browserOverlayApiResponse(settingsPage.page.id, path, {
        live: liveSnapshot(frame, index, url.searchParams),
        settings: settings(settingsPage.page.id, frame, url.searchParams),
        model: displayModel(settingsPage.page.id, frame, index, url.searchParams)
      }));
      return;
    }

    if (path === '/api/replay/status') {
      const { frame, index } = currentFrame(url, request);
      serveJson(response, {
        source: replay.source,
        frameCount: replay.frames.length,
        current: frameMetadata(frame, index, url.searchParams),
        timing: replayStatusTiming(frame, index, url.searchParams),
        assetRoot: browserAssetRoot
      });
      return;
    }

    serveText(response, 404, 'Not found');
  } catch (error) {
    serveText(response, 500, error instanceof Error ? error.stack || error.message : String(error));
  }
});

server.listen(port, '127.0.0.1', () => {
  console.log(`Standings replay:      http://127.0.0.1:${port}/overlays/standings`);
  console.log(`All overlays review:   http://127.0.0.1:${port}/review/overlays`);
  console.log(`Replay status:         http://127.0.0.1:${port}/api/replay/status`);
  console.log(`Replay source:         ${replayPath}`);
  console.log(`Replay frames:         ${replay.frames.length}`);
  console.log(`Replay timing:         ${replayStatusTiming().effectiveMode}${replayStatusTiming().effectiveMode === 'source-elapsed' ? ` @ ${replaySpeedMultiplier}x` : ` @ ${frameMilliseconds}ms/frame`}`);
  console.log(`Source cadence:        ${formatCadenceSummary(replayTiming.sourceCadence)}`);
  if (replayCaptureSessionInfo) {
    console.log(`Capture session data:  ${replayCaptureSessionInfo.drivers.size} drivers, ${replayCaptureSessionInfo.sectors.length} sectors`);
  }
  if (replayTrackMapTiming) {
    console.log(`Track map timing:      ${replayTrackMapTiming.highlightEventCount} highlight events`);
  }
  console.log(`Relative rows:         ${relativeCarsEachSide * 2 + 1}`);
  console.log(`Gap cars ahead/behind: ${gapCarsAhead}/${gapCarsBehind}`);
});

function loadReplay(path) {
  if (!path) {
    throw new Error('Pass a replay JSON path as the first argument or TMR_STANDINGS_REPLAY_JSON.');
  }
  const stats = statSync(path);
  if (!stats.isFile()) {
    throw new Error(`Replay path is not a file: ${path}`);
  }
  const parsed = JSON.parse(readFileSync(path, 'utf8'));
  if (!Array.isArray(parsed.frames) || parsed.frames.length === 0) {
    throw new Error(`Replay has no frames: ${path}`);
  }
  return parsed;
}

function loadReplayCaptureSessionInfo(loadedReplay) {
  const captureDirectory = loadedReplay?.source?.captureDirectory;
  if (!captureDirectory) {
    return null;
  }

  const sessionPath = resolve(captureDirectory, 'latest-session.yaml');
  try {
    const stats = statSync(sessionPath);
    if (!stats.isFile()) {
      return null;
    }

    const yaml = readFileSync(sessionPath, 'utf8');
    return {
      drivers: parseSessionDrivers(yaml),
      sectors: parseSessionSectors(yaml)
    };
  } catch {
    return null;
  }
}

function parseSessionDrivers(yaml) {
  const drivers = new Map();
  const lines = yaml.split(/\r?\n/);
  let inDrivers = false;
  let current = null;

  for (const line of lines) {
    const trimmed = line.trim();
    if (trimmed === 'Drivers:') {
      inDrivers = true;
      continue;
    }

    if (!inDrivers) {
      continue;
    }

    if (trimmed === 'SplitTimeInfo:' || trimmed === 'CarSetup:') {
      addSessionDriver(drivers, current);
      current = null;
      break;
    }

    const carIdxMatch = /^-\s+CarIdx:\s*(-?\d+)/.exec(trimmed);
    if (carIdxMatch) {
      addSessionDriver(drivers, current);
      current = { carIdx: Number.parseInt(carIdxMatch[1], 10) };
      continue;
    }

    if (!current) {
      continue;
    }

    const fieldMatch = /^([A-Za-z][A-Za-z0-9_]*):\s*(.*)$/.exec(trimmed);
    if (!fieldMatch) {
      continue;
    }

    const [, key, rawValue] = fieldMatch;
    switch (key) {
      case 'CarClassID':
        current.carClass = optionalInteger(rawValue);
        break;
      case 'CarClassShortName':
        current.carClassName = cleanYamlScalar(rawValue);
        break;
      case 'CarClassColor':
        current.carClassColorHex = normalizeTelemetryHexColor(rawValue);
        break;
      default:
        break;
    }
  }

  addSessionDriver(drivers, current);
  return drivers;
}

function addSessionDriver(drivers, driver) {
  if (!driver || !Number.isInteger(driver.carIdx)) {
    return;
  }

  drivers.set(driver.carIdx, driver);
}

function parseSessionSectors(yaml) {
  const lines = yaml.split(/\r?\n/);
  const sectors = [];
  let inSplitTimeInfo = false;
  let inSectors = false;
  let current = null;

  for (const line of lines) {
    const trimmed = line.trim();
    if (trimmed === 'SplitTimeInfo:') {
      inSplitTimeInfo = true;
      continue;
    }

    if (!inSplitTimeInfo) {
      continue;
    }

    if (trimmed === 'Sectors:') {
      inSectors = true;
      continue;
    }

    if (!inSectors) {
      continue;
    }

    if (/^[A-Za-z][A-Za-z0-9_]*:\s*$/.test(trimmed)) {
      addSessionSector(sectors, current);
      current = null;
      break;
    }

    const sectorNumMatch = /^-\s+SectorNum:\s*(-?\d+)/.exec(trimmed);
    if (sectorNumMatch) {
      addSessionSector(sectors, current);
      current = { sectorNum: Number.parseInt(sectorNumMatch[1], 10) };
      continue;
    }

    if (!current) {
      continue;
    }

    const startPctMatch = /^SectorStartPct:\s*([-+]?\d*\.?\d+)/.exec(trimmed);
    if (startPctMatch) {
      current.startPct = Number.parseFloat(startPctMatch[1]);
    }
  }

  addSessionSector(sectors, current);
  const ordered = sectors
    .filter((sector) => Number.isInteger(sector.sectorNum)
      && Number.isFinite(sector.startPct)
      && sector.startPct >= 0
      && sector.startPct < 1)
    .sort((left, right) => left.startPct - right.startPct || left.sectorNum - right.sectorNum);
  if (ordered.length < 2) {
    return [];
  }

  return ordered.map((sector, index) => ({
    sectorNum: sector.sectorNum,
    startPct: roundProgress(sector.startPct),
    endPct: roundProgress(index + 1 < ordered.length ? ordered[index + 1].startPct : 1),
    highlight: 'none'
  }));
}

function addSessionSector(sectors, sector) {
  if (!sector || !Number.isInteger(sector.sectorNum)) {
    return;
  }

  sectors.push(sector);
}

function cleanYamlScalar(value) {
  const text = String(value ?? '').trim();
  if ((text.startsWith('"') && text.endsWith('"')) || (text.startsWith("'") && text.endsWith("'"))) {
    return text.slice(1, -1);
  }

  return text;
}

function normalizeTelemetryHexColor(value) {
  const text = cleanYamlScalar(value);
  const match = /^(?:0x|#)?([0-9a-f]{6})$/i.exec(text);
  return match ? `#${match[1].toUpperCase()}` : null;
}

function roundProgress(value) {
  return Math.round(value * 1_000_000) / 1_000_000;
}

function buildReplayTrackMapTiming(loadedReplay, sessionInfo) {
  const sectors = sessionInfo?.sectors || [];
  if (!Array.isArray(loadedReplay?.frames) || sectors.length < 2) {
    return null;
  }

  const bestSectorSeconds = new Map();
  const activeSectorHighlights = new Map();
  const sectorsByIndex = [];
  let state = null;
  let fullLapHighlight = null;
  let fullLapCompleted = null;
  let bestLapSeconds = null;
  let highlightEventCount = 0;

  for (let index = 0; index < loadedReplay.frames.length; index += 1) {
    const frame = loadedReplay.frames[index];
    const observation = replayTrackMapObservation(frame);
    if (!observation) {
      state = null;
      fullLapHighlight = null;
      fullLapCompleted = null;
      activeSectorHighlights.clear();
      sectorsByIndex[index] = replayTrackMapSectors(sectors, activeSectorHighlights, fullLapHighlight);
      continue;
    }

    if (!state) {
      state = seedReplayTrackMapState(observation, sectors);
      sectorsByIndex[index] = replayTrackMapSectors(sectors, activeSectorHighlights, fullLapHighlight);
      continue;
    }

    const previousProgress = state.lapCompleted + state.lapDistPct;
    const currentProgress = observation.lapCompleted + observation.lapDistPct;
    const progressDelta = currentProgress - previousProgress;
    if (progressDelta < 0
      || progressDelta > 0.12
      || !Number.isFinite(progressDelta)
      || observation.sessionTimeSeconds <= state.sessionTimeSeconds) {
      state = seedReplayTrackMapState(observation, sectors);
      fullLapHighlight = null;
      fullLapCompleted = null;
      activeSectorHighlights.clear();
      sectorsByIndex[index] = replayTrackMapSectors(sectors, activeSectorHighlights, fullLapHighlight);
      continue;
    }

    let nextState = { ...state };
    for (const crossing of replayTrackMapSectorCrossings(sectors, state, observation)) {
      if (crossing.sectorIndex === 1
        && fullLapCompleted != null
        && crossing.boundaryLapCompleted > fullLapCompleted) {
        fullLapHighlight = null;
        fullLapCompleted = null;
        activeSectorHighlights.clear();
      }

      if (nextState.lastBoundarySectorIndex != null
        && nextState.lastBoundarySessionTimeSeconds != null) {
        const elapsed = crossing.sessionTimeSeconds - nextState.lastBoundarySessionTimeSeconds;
        if (elapsed > 0 && elapsed < 900) {
          const completedSector = sectors[nextState.lastBoundarySectorIndex];
          const highlight = replayTrackMapSectorHighlight(bestSectorSeconds, completedSector, elapsed);
          if (highlight === 'none') {
            activeSectorHighlights.delete(completedSector.sectorNum);
          } else {
            activeSectorHighlights.set(completedSector.sectorNum, highlight);
            highlightEventCount += 1;
          }
        }
      }

      if (crossing.sectorIndex === 0) {
        const lapSeconds = nextState.lastLapStartSessionTimeSeconds != null
          ? crossing.sessionTimeSeconds - nextState.lastLapStartSessionTimeSeconds
          : observation.lastLapTimeSeconds;
        const lapHighlight = replayTrackMapLapHighlight(lapSeconds, bestLapSeconds, observation);
        if (lapHighlight.highlight === 'none') {
          fullLapHighlight = null;
          fullLapCompleted = null;
        } else {
          fullLapHighlight = lapHighlight.highlight;
          fullLapCompleted = crossing.boundaryLapCompleted - 1;
          bestLapSeconds = lapHighlight.bestLapSeconds;
          highlightEventCount += 1;
        }

        nextState.lastLapStartSessionTimeSeconds = crossing.sessionTimeSeconds;
      }

      nextState.lastBoundarySectorIndex = crossing.sectorIndex;
      nextState.lastBoundarySessionTimeSeconds = crossing.sessionTimeSeconds;
    }

    state = {
      ...nextState,
      lapCompleted: observation.lapCompleted,
      lapDistPct: observation.lapDistPct,
      sessionTimeSeconds: observation.sessionTimeSeconds
    };
    sectorsByIndex[index] = replayTrackMapSectors(sectors, activeSectorHighlights, fullLapHighlight);
  }

  return {
    sectorsByIndex,
    highlightEventCount
  };
}

function buildReplayTrackMapMarkerAlerts(loadedReplay) {
  const frames = Array.isArray(loadedReplay?.frames) ? loadedReplay.frames : [];
  const alertsByIndex = [];
  const lastSurfaceByCarIdx = new Map();
  const flashUntilByCarIdx = new Map();
  const offTrackFlashSeconds = 2.5;
  const offTrackSurface = 0;
  const onTrackSurface = 3;

  for (let index = 0; index < frames.length; index += 1) {
    const frame = frames[index];
    const elapsedSeconds = replayFrameElapsedSeconds(frame, index);
    for (const [carIdx, flashUntil] of [...flashUntilByCarIdx.entries()]) {
      if (flashUntil <= elapsedSeconds) {
        flashUntilByCarIdx.delete(carIdx);
      }
    }

    for (const row of replayTrackMapMarkerRows(frame)) {
      const carIdx = optionalInteger(row?.carIdx);
      if (carIdx === null) {
        continue;
      }

      const trackSurface = optionalInteger(row?.trackSurface);
      if (trackSurface === null) {
        continue;
      }

      const previousSurface = lastSurfaceByCarIdx.get(carIdx);
      if (previousSurface === onTrackSurface && trackSurface === offTrackSurface) {
        flashUntilByCarIdx.set(carIdx, elapsedSeconds + offTrackFlashSeconds);
      }

      lastSurfaceByCarIdx.set(carIdx, trackSurface);
    }

    const alerts = new Map();
    for (const [carIdx, flashUntil] of flashUntilByCarIdx.entries()) {
      const remainingSeconds = Math.max(0, Math.min(offTrackFlashSeconds, flashUntil - elapsedSeconds));
      alerts.set(carIdx, Math.max(0, Math.min(1, 1 - remainingSeconds / offTrackFlashSeconds)));
    }

    alertsByIndex[index] = alerts;
  }

  return alertsByIndex;
}

function replayFrameElapsedSeconds(frame, index) {
  return finiteNumber(frame?.sourceElapsedSeconds)
    ?? finiteNumber(frame?.sessionTimeSeconds)
    ?? index * 0.1;
}

function replayTrackMapMarkerRows(frame) {
  const timing = frame?.live?.models?.timing || {};
  return [
    ...(Array.isArray(timing.overallRows) ? timing.overallRows : []),
    ...(Array.isArray(timing.classRows) ? timing.classRows : [])
  ];
}

function replayTrackMapObservation(frame) {
  const row = frame?.live?.models?.timing?.focusRow;
  if (!row || typeof row !== 'object') {
    return null;
  }

  const lapCompleted = Number(row.lapCompleted);
  const lapDistPct = Number(row.lapDistPct);
  const sessionTimeSeconds = Number(frame.sessionTimeSeconds);
  if (!Number.isFinite(lapCompleted)
    || !Number.isFinite(lapDistPct)
    || !Number.isFinite(sessionTimeSeconds)
    || lapCompleted < 0
    || lapDistPct < 0
    || lapDistPct > 1.000001) {
    return null;
  }

  return {
    lapCompleted,
    lapDistPct: Math.min(Math.max(lapDistPct, 0), 1),
    sessionTimeSeconds,
    lastLapTimeSeconds: positiveNumberOrNull(row.lastLapTimeSeconds),
    lapDeltaToSessionBestLapSeconds: replayTrackMapLapDeltaToSessionBestLapSeconds(frame, row),
    lapDeltaToSessionBestLapOk: replayTrackMapLapDeltaToSessionBestLapOk(frame, row)
  };
}

function replayTrackMapLapDeltaToSessionBestLapSeconds(frame, row) {
  return optionalNumber(
    row.lapDeltaToSessionBestLapSeconds
      ?? row.lapDeltaToSessionBestLap
      ?? frame?.live?.latestSample?.lapDeltaToSessionBestLapSeconds
      ?? frame?.live?.latestSample?.lapDeltaToSessionBestLap);
}

function replayTrackMapLapDeltaToSessionBestLapOk(frame, row) {
  return optionalBoolean(
    row.lapDeltaToSessionBestLapOk
      ?? row.lapDeltaToSessionBestLap_OK
      ?? frame?.live?.latestSample?.lapDeltaToSessionBestLapOk
      ?? frame?.live?.latestSample?.lapDeltaToSessionBestLap_OK);
}

function seedReplayTrackMapState(observation, sectors) {
  const boundaryIndex = seedReplayTrackMapBoundaryIndex(observation.lapDistPct, sectors);
  return {
    lapCompleted: observation.lapCompleted,
    lapDistPct: observation.lapDistPct,
    sessionTimeSeconds: observation.sessionTimeSeconds,
    lastBoundarySectorIndex: boundaryIndex,
    lastBoundarySessionTimeSeconds: boundaryIndex == null ? null : observation.sessionTimeSeconds,
    lastLapStartSessionTimeSeconds: boundaryIndex === 0 ? observation.sessionTimeSeconds : null
  };
}

function seedReplayTrackMapBoundaryIndex(lapDistPct, sectors) {
  if (lapDistPct <= 0.02) {
    return 0;
  }

  const nearest = sectors
    .map((sector, sectorIndex) => ({
      sectorIndex,
      distance: lapDistPct - sector.startPct
    }))
    .filter((candidate) => candidate.distance >= 0)
    .sort((left, right) => left.distance - right.distance)[0];
  return nearest && nearest.distance <= 0.0125 ? nearest.sectorIndex : null;
}

function replayTrackMapSectorCrossings(sectors, previous, current) {
  const crossings = [];
  const previousProgress = previous.lapCompleted + previous.lapDistPct;
  const currentProgress = current.lapCompleted + current.lapDistPct;
  const progressDelta = currentProgress - previousProgress;
  const timeDelta = current.sessionTimeSeconds - previous.sessionTimeSeconds;

  for (let lap = previous.lapCompleted; lap <= current.lapCompleted; lap += 1) {
    for (let sectorIndex = 0; sectorIndex < sectors.length; sectorIndex += 1) {
      const sector = sectors[sectorIndex];
      const boundaryProgress = lap + sector.startPct;
      if (boundaryProgress <= previousProgress || boundaryProgress > currentProgress) {
        continue;
      }

      const interpolation = (boundaryProgress - previousProgress) / progressDelta;
      if (!Number.isFinite(interpolation) || interpolation < 0 || interpolation > 1) {
        continue;
      }

      crossings.push({
        sectorIndex,
        boundaryLapCompleted: lap,
        sessionTimeSeconds: previous.sessionTimeSeconds + timeDelta * interpolation
      });
    }
  }

  return crossings;
}

function replayTrackMapSectorHighlight(bestSectorSeconds, sector, elapsedSeconds) {
  const best = bestSectorSeconds.get(sector.sectorNum);
  if (best == null || elapsedSeconds < best - 0.001) {
    bestSectorSeconds.set(sector.sectorNum, elapsedSeconds);
    return 'personal-best';
  }

  return 'none';
}

function replayTrackMapLapHighlight(lapSeconds, bestLapSeconds, observation) {
  if (!Number.isFinite(lapSeconds) || lapSeconds <= 0 || lapSeconds >= 3600) {
    return { highlight: 'none', bestLapSeconds };
  }

  if (bestLapSeconds == null || lapSeconds < bestLapSeconds - 0.001) {
    const nextBestLapSeconds = lapSeconds;
    return {
      highlight: replayTrackMapIsSessionBestLapSignal(observation)
        || observation?.lapDeltaToSessionBestLapOk !== true
        ? 'best-lap'
        : 'personal-best',
      bestLapSeconds: nextBestLapSeconds
    };
  }

  return { highlight: 'none', bestLapSeconds };
}

function replayTrackMapIsSessionBestLapSignal(observation) {
  return observation?.lapDeltaToSessionBestLapOk === true
    && Number.isFinite(observation.lapDeltaToSessionBestLapSeconds)
    && observation.lapDeltaToSessionBestLapSeconds <= 0.001;
}

function replayTrackMapSectors(sectors, activeSectorHighlights, fullLapHighlight) {
  return sectors.map((sector) => ({
    ...sector,
    highlight: fullLapHighlight
      ?? activeSectorHighlights.get(sector.sectorNum)
      ?? 'none',
    boundaryHighlight: activeSectorHighlights.get(sector.sectorNum) ?? 'none'
  }));
}

function positiveNumberOrNull(value) {
  const number = Number(value);
  return Number.isFinite(number) && number > 0 ? number : null;
}

function analyzeReplayTiming(replayDocument) {
  const frames = replayDocument.frames || [];
  const firstSessionTime = firstFinite(frames.map((frame) => frame.sessionTimeSeconds));
  const firstCapturedUnixMs = firstFinite(frames.map((frame) => frame.capturedUnixMs));
  let previous = null;
  const sessionDeltas = [];
  const capturedDeltas = [];
  const frameIndexDeltas = [];
  let hasNonMonotonicSessionTime = false;

  frames.forEach((frame, index) => {
    if (!Number.isFinite(frame.sourceElapsedSeconds)) {
      if (Number.isFinite(frame.sessionTimeSeconds) && firstSessionTime !== null) {
        frame.sourceElapsedSeconds = roundNumber(frame.sessionTimeSeconds - firstSessionTime);
      } else if (Number.isFinite(frame.capturedUnixMs) && firstCapturedUnixMs !== null) {
        frame.sourceElapsedSeconds = roundNumber((frame.capturedUnixMs - firstCapturedUnixMs) / 1000);
      } else {
        frame.sourceElapsedSeconds = roundNumber(index * Math.max(1, frameMilliseconds) / 1000);
      }
    }

    if (previous) {
      if (!Number.isFinite(frame.sourceSessionDeltaSeconds)
        && Number.isFinite(frame.sessionTimeSeconds)
        && Number.isFinite(previous.sessionTimeSeconds)) {
        frame.sourceSessionDeltaSeconds = roundNumber(frame.sessionTimeSeconds - previous.sessionTimeSeconds);
      }
      if (!Number.isFinite(frame.sourceCapturedDeltaSeconds)
        && Number.isFinite(frame.capturedUnixMs)
        && Number.isFinite(previous.capturedUnixMs)) {
        frame.sourceCapturedDeltaSeconds = roundNumber((frame.capturedUnixMs - previous.capturedUnixMs) / 1000);
      }
      if (!Number.isInteger(frame.sourceFrameDelta)
        && Number.isInteger(frame.frameIndex)
        && Number.isInteger(previous.frameIndex)) {
        frame.sourceFrameDelta = frame.frameIndex - previous.frameIndex;
      }
    }

    if (Number.isFinite(frame.sourceSessionDeltaSeconds)) {
      if (frame.sourceSessionDeltaSeconds < 0) {
        hasNonMonotonicSessionTime = true;
      } else {
        sessionDeltas.push(frame.sourceSessionDeltaSeconds);
      }
    }
    if (Number.isFinite(frame.sourceCapturedDeltaSeconds) && frame.sourceCapturedDeltaSeconds >= 0) {
      capturedDeltas.push(frame.sourceCapturedDeltaSeconds);
    }
    if (Number.isInteger(frame.sourceFrameDelta) && frame.sourceFrameDelta >= 0) {
      frameIndexDeltas.push(frame.sourceFrameDelta);
    }
    previous = frame;
  });

  const timeline = frames
    .map((frame, index) => ({ index, elapsed: finiteNumber(frame.sourceElapsedSeconds) }))
    .filter((item) => Number.isFinite(item.elapsed));
  const isTimelineMonotonic = timeline.every((item, index) => index === 0 || item.elapsed >= timeline[index - 1].elapsed);
  const sourceDurationSeconds = timeline.length > 1
    ? Math.max(0, timeline[timeline.length - 1].elapsed - timeline[0].elapsed)
    : 0;
  const exportedCadence = replayDocument.source?.cadence || {};
  const computedCadence = {
    basis: exportedCadence.basis || 'raw-capture frame header sessionTime',
    selectedFrameCount: frames.length,
    gapToLeaderMissingSegmentThresholdSeconds: gapMissingSegmentThresholdSeconds,
    denseForGapToLeader: frames.length < 2 || (sessionDeltas.length > 0 && Math.max(...sessionDeltas) <= gapMissingSegmentThresholdSeconds),
    hasNonMonotonicSessionTime,
    sourceElapsedSeconds: summarizeNumbers(timeline.map((item) => item.elapsed)),
    sourceSessionDeltaSeconds: summarizeNumbers(sessionDeltas),
    sourceCapturedDeltaSeconds: summarizeNumbers(capturedDeltas),
    sourceFrameDelta: summarizeNumbers(frameIndexDeltas, 0)
  };

  return {
    timeline,
    isTimelineMonotonic,
    sourceDurationSeconds,
    sourceCadence: mergeCadence(exportedCadence, computedCadence)
  };
}

function effectiveReplayTimingMode() {
  return requestedReplayTimingMode === 'source-elapsed'
    && replayTiming.isTimelineMonotonic
    && replayTiming.sourceDurationSeconds > 0
    ? 'source-elapsed'
    : 'fixed-frame';
}

function replayStatusTiming(frame = null, index = null, searchParams = null) {
  const effectiveMode = effectiveReplayTimingMode();
  return {
    requestedMode: requestedReplayTimingMode,
    effectiveMode,
    speedMultiplier: effectiveMode === 'source-elapsed'
      ? replaySpeedFromSearchParams(searchParams) ?? replaySpeedMultiplier
      : null,
    fixedFrameMilliseconds: effectiveMode === 'fixed-frame' ? frameMilliseconds : null,
    replayWindow: replayWindowFromSearchParams(searchParams) ?? replayWindowStatus(),
    sourceDurationSeconds: roundNumber(replayTiming.sourceDurationSeconds),
    sourceTimelineMonotonic: replayTiming.isTimelineMonotonic,
    sourceCadence: replayTiming.sourceCadence,
    current: frame && Number.isInteger(index)
      ? {
          index,
          frameIndex: frame.frameIndex,
          sourceElapsedSeconds: finiteNumber(frame.sourceElapsedSeconds),
          sourceSessionDeltaSeconds: finiteNumber(frame.sourceSessionDeltaSeconds),
          sourceFrameDelta: Number.isInteger(frame.sourceFrameDelta) ? frame.sourceFrameDelta : null
        }
      : null
  };
}

function frameIndexForSourceElapsed(sourceElapsedSeconds) {
  const timeline = replayTiming.timeline;
  if (timeline.length === 0) {
    return 0;
  }

  let low = 0;
  let high = timeline.length - 1;
  while (low < high) {
    const middle = Math.floor((low + high + 1) / 2);
    if (timeline[middle].elapsed <= sourceElapsedSeconds) {
      low = middle;
    } else {
      high = middle - 1;
    }
  }
  return timeline[low].index;
}

function firstFrameIndexAtOrAfterSourceElapsed(sourceElapsedSeconds, maximumIndex) {
  const timeline = replayTiming.timeline.filter((item) => item.index <= maximumIndex);
  if (timeline.length === 0) {
    return 0;
  }

  let low = 0;
  let high = timeline.length - 1;
  while (low < high) {
    const middle = Math.floor((low + high) / 2);
    if (timeline[middle].elapsed < sourceElapsedSeconds) {
      low = middle + 1;
    } else {
      high = middle;
    }
  }
  return timeline[low].index;
}

function mergeCadence(exportedCadence, computedCadence) {
  return {
    ...computedCadence,
    ...exportedCadence,
    sourceElapsedSeconds: exportedCadence.sourceElapsedSeconds || computedCadence.sourceElapsedSeconds,
    sourceSessionDeltaSeconds: exportedCadence.sourceSessionDeltaSeconds || computedCadence.sourceSessionDeltaSeconds,
    sourceCapturedDeltaSeconds: exportedCadence.sourceCapturedDeltaSeconds || computedCadence.sourceCapturedDeltaSeconds,
    sourceFrameDelta: exportedCadence.sourceFrameDelta || computedCadence.sourceFrameDelta,
    denseForGapToLeader: typeof exportedCadence.denseForGapToLeader === 'boolean'
      ? exportedCadence.denseForGapToLeader
      : computedCadence.denseForGapToLeader
  };
}

function summarizeNumbers(values, digits = 3) {
  const numbers = values.filter((value) => Number.isFinite(value)).sort((a, b) => a - b);
  if (numbers.length === 0) {
    return { count: 0, min: null, median: null, max: null };
  }

  const middle = Math.floor(numbers.length / 2);
  const median = numbers.length % 2 === 0
    ? (numbers[middle - 1] + numbers[middle]) / 2
    : numbers[middle];
  return {
    count: numbers.length,
    min: roundNumber(numbers[0], digits),
    median: roundNumber(median, digits),
    max: roundNumber(numbers[numbers.length - 1], digits)
  };
}

function firstFinite(values) {
  const value = values.find((candidate) => Number.isFinite(candidate));
  return Number.isFinite(value) ? value : null;
}

function currentFrame(url = null, request = null) {
  const override = frameOverride(url, request);
  if (override !== null) {
    const index = Math.max(0, Math.min(replay.frames.length - 1, override));
    return { index, frame: replay.frames[index] };
  }

  const window = replayWindow(url, request);
  if (effectiveReplayTimingMode() === 'source-elapsed') {
    const index = currentSourceElapsedFrameIndex(url, request, window);
    return { index, frame: replay.frames[index] };
  }

  const index = currentFixedFrameIndex(window);
  return { index, frame: replay.frames[index] };
}

function currentSourceElapsedFrameIndex(url = null, request = null, window = null) {
  const duration = replayTiming.sourceDurationSeconds;
  if (!Number.isFinite(duration) || duration <= 0) {
    return currentFixedFrameIndex(window);
  }

  const speedMultiplier = replaySpeed(url, request);
  const elapsedSeconds = (Math.max(0, Date.now() - startedAtMs) / 1000) * speedMultiplier;
  const sourceWindow = window?.source;
  if (sourceWindow) {
    return frameIndexForSourceElapsed(sourceWindow.start + (elapsedSeconds % sourceWindow.duration));
  }

  const startElapsed = replayTiming.timeline[0]?.elapsed || 0;
  return frameIndexForSourceElapsed(startElapsed + (elapsedSeconds % duration));
}

function currentFixedFrameIndex(window = null) {
  const elapsed = Math.max(0, Date.now() - startedAtMs);
  const frameWindow = window?.frame;
  if (frameWindow) {
    const offset = Math.floor(elapsed / Math.max(1, frameMilliseconds)) % frameWindow.count;
    return frameWindow.start + offset;
  }

  return Math.floor(elapsed / Math.max(1, frameMilliseconds)) % replay.frames.length;
}

function frameOverride(url, request) {
  for (const searchParams of requestSearchParams(url, request)) {
    const frame = parseFrame(searchParams);
    if (frame !== null) {
      return frame;
    }
  }

  return null;
}

function parseFrame(searchParams) {
  const rawFrame = searchParams?.get('frame');
  if (rawFrame !== null && rawFrame !== undefined) {
    const frame = Number.parseInt(rawFrame, 10);
    return Number.isFinite(frame) ? frame : null;
  }

  const rawRelativeSeconds = searchParams?.get('rel');
  if (rawRelativeSeconds !== null && rawRelativeSeconds !== undefined) {
    const relativeSeconds = Number.parseFloat(rawRelativeSeconds);
    if (Number.isFinite(relativeSeconds)) {
      const index = replay.frames.findIndex((frame) => frame.raceStartRelativeSeconds === relativeSeconds);
      return index >= 0 ? index : null;
    }
  }

  return null;
}

function replayWindow(url = null, request = null) {
  for (const searchParams of requestSearchParams(url, request)) {
    const window = replayWindowFromSearchParams(searchParams);
    if (window) return window;
  }

  const envSourceWindow = boundedSourceWindow(replayWindowSourceStart, replayWindowSourceEnd);
  const envFrameWindow = boundedFrameWindow(replayWindowFrameStart, replayWindowFrameEnd);
  return envSourceWindow || envFrameWindow
    ? { source: envSourceWindow, frame: envFrameWindow }
    : null;
}

function replayWindowFromSearchParams(searchParams) {
  const sourceWindow = sourceReplayWindow(searchParams);
  const frameWindow = frameReplayWindow(searchParams);
  return sourceWindow || frameWindow
    ? { source: sourceWindow, frame: frameWindow }
    : null;
}

function sourceReplayWindow(searchParams) {
  const start = optionalNumber(searchParams?.get('sourceStart'));
  const end = optionalNumber(searchParams?.get('sourceEnd'));
  return boundedSourceWindow(start, end);
}

function frameReplayWindow(searchParams) {
  const start = optionalInteger(searchParams?.get('frameStart'));
  const end = optionalInteger(searchParams?.get('frameEnd'));
  return boundedFrameWindow(start, end);
}

function boundedSourceWindow(start, end) {
  if (!Number.isFinite(start) || !Number.isFinite(end) || start >= end || replayTiming.timeline.length === 0) {
    return null;
  }

  const timelineStart = replayTiming.timeline[0].elapsed;
  const timelineEnd = replayTiming.timeline[replayTiming.timeline.length - 1].elapsed;
  const boundedStart = clamp(start, timelineStart, timelineEnd);
  const boundedEnd = clamp(end, timelineStart, timelineEnd);
  return boundedStart < boundedEnd
    ? { start: boundedStart, end: boundedEnd, duration: boundedEnd - boundedStart }
    : null;
}

function boundedFrameWindow(start, end) {
  if (!Number.isInteger(start) || !Number.isInteger(end)) {
    return null;
  }

  const boundedStart = Math.max(0, Math.min(replay.frames.length - 1, start));
  const boundedEnd = Math.max(0, Math.min(replay.frames.length - 1, end));
  return boundedStart <= boundedEnd
    ? { start: boundedStart, end: boundedEnd, count: boundedEnd - boundedStart + 1 }
    : null;
}

function replaySpeed(url = null, request = null) {
  for (const searchParams of requestSearchParams(url, request)) {
    const speed = replaySpeedFromSearchParams(searchParams);
    if (speed !== null) {
      return speed;
    }
  }

  return replaySpeedMultiplier;
}

function replaySpeedFromSearchParams(searchParams = null) {
  return positiveNumber(searchParams?.get('replaySpeed'), null);
}

function replayWindowStatus() {
  const sourceWindow = boundedSourceWindow(replayWindowSourceStart, replayWindowSourceEnd);
  const frameWindow = boundedFrameWindow(replayWindowFrameStart, replayWindowFrameEnd);
  return sourceWindow || frameWindow
    ? { source: sourceWindow, frame: frameWindow }
    : null;
}

function requestSearchParams(url = null, request = null) {
  const candidates = [];
  if (url?.searchParams) {
    candidates.push(url.searchParams);
  }

  const referrer = request?.headers?.referer;
  if (referrer) {
    try {
      candidates.push(new URL(referrer).searchParams);
    } catch {
      // Ignore malformed referrers from external tools.
    }
  }

  return candidates;
}

function frameMetadata(frame, index, searchParams = null) {
  return {
    index,
    captureId: frame.captureId,
    frameIndex: frame.frameIndex,
    sessionTimeSeconds: frame.sessionTimeSeconds,
    sourceElapsedSeconds: finiteNumber(frame.sourceElapsedSeconds),
    sourceSessionDeltaSeconds: finiteNumber(frame.sourceSessionDeltaSeconds),
    sourceCapturedDeltaSeconds: finiteNumber(frame.sourceCapturedDeltaSeconds),
    sourceFrameDelta: Number.isInteger(frame.sourceFrameDelta) ? frame.sourceFrameDelta : null,
    sessionInfoUpdate: frame.sessionInfoUpdate,
    sessionState: frame.sessionState,
    sessionPhase: frame.sessionPhase,
    camCarIdx: frame.camCarIdx,
    playerCarIdx: frame.playerCarIdx,
    spoofFocus: spoofFocusMode(searchParams),
    timing: replayStatusTiming(frame, index, searchParams)
  };
}

function liveSnapshot(frame, index, searchParams = null) {
  if (frame.live && typeof frame.live === 'object') {
    const enrichedLive = enrichReplayLiveSnapshot(frame.live, index);
    return {
      ...enrichedLive,
      models: spoofLiveModels(enrichedLive.models || {}, frame, index, searchParams),
      sourceId: enrichedLive.sourceId || replay.source?.captureId || 'capture-replay',
      startedAtUtc: enrichedLive.startedAtUtc || replay.source?.startedAtUtc || null,
      lastUpdatedAtUtc: new Date().toISOString(),
      sequence: index + 1
    };
  }

  const relativeSeconds = Number.isFinite(frame.raceStartRelativeSeconds)
    ? frame.raceStartRelativeSeconds
    : (frame.sessionState === 4 ? index - 60 : index - 60) * 2;
  const lapProgress = normalizeProgress(0.18 + relativeSeconds / 360);
  const timer = headerTimeRemaining(frame);
  const referenceRow = referenceDisplayRow(frame);
  const onPitRoad = referenceRow?.isPit === true || referenceRow?.cells?.[5] === 'IN';
  const isPreGreen = Number.isFinite(frame.sessionState) && frame.sessionState < 4;
  const isGarageVisible = false;
  const now = new Date().toISOString();
  const inputPhase = (relativeSeconds + 120) / 240;
  const throttle = clamp(0.35 + 0.35 * Math.sin((relativeSeconds + 140) / 18), 0, 1);
  const brake = clamp(isPreGreen ? 0.08 : 0.04 + 0.16 * Math.max(0, Math.sin((relativeSeconds + 10) / 21)), 0, 1);
  const clutch = isPreGreen ? 0.18 : 0;
  const speed = isPreGreen ? Math.max(0, relativeSeconds + 120) * 0.25 : 34 + inputPhase * 38;

  return {
    isConnected: true,
    isCollecting: true,
    sourceId: replay.source?.captureId || 'standings-replay',
    startedAtUtc: replay.source?.startedAtUtc || null,
    lastUpdatedAtUtc: now,
    sequence: index + 1,
    context: {
      session: {
        sessionType: 'Race',
        sessionName: 'Race',
        eventType: 'Race'
      }
    },
    combo: {
      trackDisplayName: 'Race-start replay',
      carScreenName: referenceRow?.cells?.[2] || 'Replay car'
    },
    latestSample: {
      focusCarIdx: frame.camCarIdx,
      focusLapDistPct: lapProgress,
      sessionTime: frame.sessionTimeSeconds,
      sessionState: frame.sessionState
    },
    fuel: {
      fuelLevelLiters: Math.max(0, 74 - Math.max(0, relativeSeconds) * 0.006),
      fuelLevelPercent: 0.72,
      fuelUsePerHourKg: isPreGreen ? 8 : 82
    },
    proximity: {},
    leaderGap: {},
    models: {
      reference: {
        hasData: Number.isFinite(frame.camCarIdx),
        quality: 'inferred',
        playerCarIdx: Number.isFinite(frame.playerCarIdx) ? frame.playerCarIdx : null,
        focusCarIdx: Number.isFinite(frame.camCarIdx) ? frame.camCarIdx : null,
        focusIsPlayer: Number.isFinite(frame.camCarIdx) && Number.isFinite(frame.playerCarIdx) && frame.camCarIdx === frame.playerCarIdx,
        hasExplicitNonPlayerFocus: Number.isFinite(frame.camCarIdx) && Number.isFinite(frame.playerCarIdx) && frame.camCarIdx !== frame.playerCarIdx,
        referenceCarClass: null,
        lapDistPct: lapProgress,
        onPitRoad,
        isOnTrack: !isPreGreen || !onPitRoad,
        isInGarage: false,
        playerCarInPitStall: false,
        playerYawNorthRadians: Math.PI
      },
      session: {
        hasData: true,
        quality: 'reliable',
        sessionType: 'Race',
        sessionName: isPreGreen ? 'Race Grid' : 'Race',
        eventType: 'Race',
        carDisplayName: referenceRow?.cells?.[2] || 'Replay car',
        currentSessionNum: 2,
        sessionState: frame.sessionState,
        sessionPhase: frame.sessionPhase,
        sessionTimeSeconds: frame.sessionTimeSeconds,
        sessionTimeRemainSeconds: timer?.seconds ?? null,
        trackDisplayName: 'Race-start replay',
        carScreenName: referenceRow?.cells?.[2] || 'Replay car'
      },
      inputs: {
        hasData: true,
        quality: 'reliable',
        throttle,
        brake,
        clutch,
        steeringWheelAngle: 0.25 * Math.sin(relativeSeconds / 13),
        gear: isPreGreen ? 1 : 3,
        speedMetersPerSecond: speed,
        brakeAbsActive: !isPreGreen && brake > 0.12
      },
      raceEvents: {
        hasData: true,
        isOnTrack: !isGarageVisible,
        isInGarage: false,
        isGarageVisible,
        lapDistPct: lapProgress,
        onPitRoad
      },
      spatial: spatialModel(lapProgress, relativeSeconds),
      trackMap: trackMapModel(lapProgress),
      relative: relativeModel(relativeSeconds),
      fuelPit: {
        hasData: true,
        quality: isPreGreen ? 'partial' : 'reliable',
        fuel: {
          fuelLevelLiters: Math.max(0, 74 - Math.max(0, relativeSeconds) * 0.006),
          fuelLevelPercent: 0.72,
          fuelUsePerHourKg: isPreGreen ? 8 : 82
        }
      },
      raceProgress: {
        hasData: !isPreGreen,
        quality: isPreGreen ? 'partial' : 'inferred',
        referenceCarProgressLaps: isPreGreen ? null : 1 + lapProgress
      },
      raceProjection: {
        hasData: !isPreGreen,
        quality: isPreGreen ? 'partial' : 'inferred'
      },
      weather: {
        hasData: true,
        quality: 'reliable',
        airTempC: 21,
        trackTempCrewC: 29,
        trackWetness: 1,
        weatherDeclaredWet: false,
        windVelocityMetersPerSecond: 4.2,
        windDirectionRadians: Math.PI,
        relativeHumidityPercent: 48,
        fogLevelPercent: 0,
        airPressurePa: 101325,
        solarAltitudeRadians: 0.5,
        solarAzimuthRadians: 2.2
      }
    }
  };
}

function enrichReplayLiveSnapshot(live, index) {
  if (!replayCaptureSessionInfo || !live || typeof live !== 'object') {
    return live;
  }

  const models = live.models && typeof live.models === 'object'
    ? { ...live.models }
    : {};
  models.trackMap = enrichTrackMapModel(models.trackMap, index);
  models.timing = enrichTimingModel(models.timing, index);
  models.scoring = enrichRowsModel(models.scoring, 'rows');
  models.driverDirectory = enrichRowsModel(models.driverDirectory, 'drivers');
  models.relative = enrichRowsModel(models.relative, 'rows');
  models.spatial = enrichRowsModel(models.spatial, 'cars');

  return {
    ...live,
    models
  };
}

function enrichTrackMapModel(trackMap, index) {
  const timedSectors = replayTrackMapTiming?.sectorsByIndex?.[index] || null;
  const sectors = timedSectors || replayCaptureSessionInfo?.sectors || [];
  if (!sectors.length) {
    return trackMap;
  }

  const existing = trackMap && typeof trackMap === 'object' ? trackMap : {};
  if (!timedSectors && Array.isArray(existing.sectors) && existing.sectors.length >= 2) {
    return existing;
  }

  return {
    ...existing,
    hasData: existing.hasData ?? true,
    hasSectors: true,
    hasLiveTiming: timedSectors ? true : existing.hasLiveTiming ?? false,
    quality: existing.quality || 'capture-session-info',
    sectors
  };
}

function enrichTimingModel(timing, index) {
  if (!timing || typeof timing !== 'object') {
    return timing;
  }

  const focusCarIdx = optionalInteger(timing.focusCarIdx ?? timing.focusRow?.carIdx);
  return {
    ...timing,
    overallRows: enrichTimingRows(timing.overallRows, index, focusCarIdx),
    classRows: enrichTimingRows(timing.classRows, index, focusCarIdx),
    focusRow: enrichCarIdentity(timing.focusRow),
    playerRow: enrichCarIdentity(timing.playerRow)
  };
}

function enrichTimingRows(rows, index, focusCarIdx) {
  const enriched = enrichCarIdentityRows(rows);
  if (!Array.isArray(enriched)) {
    return enriched;
  }

  return enriched.map((row) => enrichTrackMapMarkerAlert(row, index, focusCarIdx));
}

function enrichTrackMapMarkerAlert(row, index, focusCarIdx) {
  if (!row || typeof row !== 'object') {
    return row;
  }

  const carIdx = optionalInteger(row.carIdx);
  if (carIdx === null) {
    return row;
  }

  const { trackMapAlertKind, trackMapAlertPulseProgress, alertKind, ...withoutAlert } = row;
  if (row.isFocus === true || carIdx === focusCarIdx) {
    return withoutAlert;
  }

  const activeAlerts = replayTrackMapMarkerAlerts?.[index];
  if (activeAlerts?.has(carIdx)) {
    return {
      ...withoutAlert,
      trackMapAlertKind: 'off-track',
      trackMapAlertPulseProgress: activeAlerts.get(carIdx)
    };
  }

  return withoutAlert;
}

function enrichRowsModel(model, rowsProperty) {
  if (!model || typeof model !== 'object') {
    return model;
  }

  return {
    ...model,
    [rowsProperty]: enrichCarIdentityRows(model[rowsProperty])
  };
}

function enrichCarIdentityRows(rows) {
  return Array.isArray(rows)
    ? rows.map(enrichCarIdentity)
    : rows;
}

function enrichCarIdentity(row) {
  if (!row || typeof row !== 'object' || !Number.isFinite(row.carIdx)) {
    return row;
  }

  const driver = replayCaptureSessionInfo?.drivers.get(row.carIdx);
  if (!driver) {
    return row;
  }

  const next = { ...row };
  if (Number.isInteger(driver.carClass)) {
    next.carClass = driver.carClass;
  }

  if (driver.carClassName) {
    next.carClassName = driver.carClassName;
  }

  if (driver.carClassColorHex) {
    next.carClassColorHex = driver.carClassColorHex;
  }

  return next;
}

function liveModelsForReplayFrame(index, searchParams = null) {
  const frame = replay.frames[index];
  return spoofLiveModels(frame?.live?.models || {}, frame, index, searchParams);
}

function spoofLiveModels(models, frame, index, searchParams = null) {
  const targetPosition = spoofFocusClassPosition(models, frame, index, searchParams);
  if (!targetPosition) {
    return models;
  }

  const timing = models?.timing || {};
  const target = timingRowByClassPosition(timing, targetPosition);
  if (!target || !Number.isFinite(target.carIdx)) {
    return models;
  }

  const spoofed = clonePlainObject(models);
  const spoofedTiming = spoofed.timing || {};
  const playerCarIdx = Number.isFinite(spoofed.reference?.playerCarIdx)
    ? spoofed.reference.playerCarIdx
    : Number.isFinite(frame?.playerCarIdx)
      ? frame.playerCarIdx
      : null;

  for (const rowsKey of ['classRows', 'overallRows']) {
    if (!Array.isArray(spoofedTiming[rowsKey])) {
      continue;
    }

    spoofedTiming[rowsKey] = spoofedTiming[rowsKey].map((row) => ({
      ...row,
      isFocus: row?.carIdx === target.carIdx,
      isPlayer: playerCarIdx !== null && row?.carIdx === playerCarIdx
    }));
  }

  const targetRow = timingRowByCarIdx(spoofedTiming, target.carIdx) || { ...target };
  targetRow.isFocus = true;
  targetRow.isPlayer = playerCarIdx !== null && target.carIdx === playerCarIdx;
  spoofedTiming.focusCarIdx = target.carIdx;
  spoofedTiming.focusRow = targetRow;
  spoofed.timing = spoofedTiming;

  const reference = spoofed.reference || {};
  spoofed.reference = {
    ...reference,
    hasData: true,
    focusCarIdx: target.carIdx,
    focusIsPlayer: playerCarIdx !== null && target.carIdx === playerCarIdx,
    hasExplicitNonPlayerFocus: playerCarIdx !== null && target.carIdx !== playerCarIdx,
    referenceCarClass: target.carClass ?? reference.referenceCarClass ?? null,
    lapDistPct: Number.isFinite(target.lapDistPct) ? target.lapDistPct : reference.lapDistPct ?? null,
    onPitRoad: target.onPitRoad === true,
    isOnTrack: target.onPitRoad !== true,
    isInGarage: false
  };

  return spoofed;
}

function spoofFocusMode(searchParams = null) {
  const raw = searchParams?.get('spoofFocus') ?? searchParams?.get('focus');
  return raw ? String(raw).trim().toLowerCase() : null;
}

function spoofFocusClassPosition(models, frame, index, searchParams = null) {
  const mode = spoofFocusMode(searchParams);
  if (!mode) {
    return null;
  }

  if (mode === 'leader' || mode === 'p1') {
    return 1;
  }

  const explicitPosition = /^p?(\d+)$/i.exec(mode);
  if (explicitPosition) {
    const position = Number.parseInt(explicitPosition[1], 10);
    return position > 0 ? position : null;
  }

  if (mode === 'switch' || mode === 'focus-switch' || mode === 'mid-switch') {
    return replayProgress(frame, index) < 0.5 ? 3 : 5;
  }

  if (mode === 'leader-switch' || mode === 'switch-leader') {
    return replayProgress(frame, index) < 0.5 ? 3 : 1;
  }

  return null;
}

function replayProgress(frame, index) {
  const elapsed = finiteNumber(frame?.sourceElapsedSeconds);
  const firstElapsed = replayTiming.timeline[0]?.elapsed;
  if (Number.isFinite(elapsed)
    && Number.isFinite(firstElapsed)
    && replayTiming.sourceDurationSeconds > 0) {
    return clamp((elapsed - firstElapsed) / replayTiming.sourceDurationSeconds, 0, 1);
  }

  return replay.frames.length > 1
    ? clamp(index / (replay.frames.length - 1), 0, 1)
    : 0;
}

function timingRowByClassPosition(timing, classPosition) {
  const position = Number.parseInt(classPosition, 10);
  if (!Number.isFinite(position) || position <= 0) {
    return null;
  }

  const rows = [
    ...(Array.isArray(timing?.classRows) ? timing.classRows : []),
    ...(Array.isArray(timing?.overallRows) ? timing.overallRows : [])
  ];
  return rows.find((row) => toPositiveInteger(row?.classPosition) === position) || null;
}

function timingRowByCarIdx(timing, carIdx) {
  const rows = [
    ...(Array.isArray(timing?.classRows) ? timing.classRows : []),
    ...(Array.isArray(timing?.overallRows) ? timing.overallRows : [])
  ];
  return rows.find((row) => row?.carIdx === carIdx) || null;
}

function clonePlainObject(value) {
  return globalThis.structuredClone
    ? globalThis.structuredClone(value)
    : JSON.parse(JSON.stringify(value));
}

function displayModel(overlayId, frame, index, searchParams = null) {
  const embeddedModel = embeddedReplayDisplayModel(overlayId, frame);
  if (embeddedModel) {
    return embeddedModel;
  }

  if (assetBackedReplayOverlayModelIds.has(overlayId)) {
    return replayAssetBackedDisplayModel(overlayId, frame, index, searchParams);
  }

  if (frame.live?.models) {
    return captureDisplayModel(overlayId, frame, index, searchParams);
  }

  const relativeSeconds = Number.isFinite(frame.raceStartRelativeSeconds)
    ? frame.raceStartRelativeSeconds
    : 0;
  const isPreGreen = Number.isFinite(frame.sessionState) && frame.sessionState < 4;
  const status = `${isPreGreen ? 'pre-green' : 'green'} | ${relativeSeconds >= 0 ? '+' : ''}${relativeSeconds}s`;

  // BrowserOverlayModelFactory is a C# application service and cannot be run
  // directly from this Node replay server. The remaining builders are isolated
  // here and emit BrowserOverlayDisplayModel-shaped JSON from replay frames.
  if (overlayId === 'relative') {
    const relative = relativeSettings();
    const relativeModel = syntheticRelativeRows(frame, isPreGreen, relative.carsAhead, relative.carsBehind);
    return relativeTableModel(
      relativeModel.status,
      replayHeaderItems(frame, relativeModel.status, relative),
      relativeModel.rows,
      sourceFromSettings(relative, 'source: race-start replay'),
      relative.columns);
  }

  const headerItems = replayHeaderItems(frame, status);

  if (overlayId === 'session-weather') {
    const sessionWeatherSettings = sessionWeatherSettingsModel();
    const session = {
      sessionType: 'Race',
      sessionName: isPreGreen ? 'Race Grid' : null,
      carDisplayName: referenceDisplayRow(frame)?.cells?.[2] || 'Replay car',
      teamRacing: true,
      sessionTimeSeconds: Math.max(0, 240 + relativeSeconds),
      sessionTimeRemainSeconds: Math.max(0, 14400 - 240 - relativeSeconds),
      sessionTimeTotalSeconds: 14400,
      sessionState: isPreGreen ? 3 : 4,
      sessionFlags: isPreGreen ? 0 : 4,
      sessionLapsTotal: 179,
      trackDisplayName: 'Gesamtstrecke 24h',
      trackLengthKm: 25.38
    };
    const weather = {
      hasData: true,
      airTempC: 21,
      trackTempCrewC: 29,
      trackWetness: 1,
      trackWetnessLabel: 'dry',
      weatherDeclaredWet: false,
      weatherType: 'constant',
      skiesLabel: 'partly cloudy',
      precipitationPercent: 0,
      windVelocityMetersPerSecond: 4.2,
      windDirectionRadians: Math.PI,
      relativeHumidityPercent: 48,
      fogLevelPercent: 0,
      airPressurePa: 101325,
      solarAltitudeRadians: 0.5,
      solarAzimuthRadians: 2.2,
      rubberState: 'moderate usage'
    };
    return sessionWeatherMetricsModel(
      session,
      weather,
      {},
      {},
      replayHeaderItems(frame, 'Race', sessionWeatherSettings),
      '',
      {
        reference: {
          hasData: true,
          focusIsPlayer: true,
          playerCarIdx: frame.playerCarIdx,
          focusCarIdx: frame.playerCarIdx,
          playerYawNorthRadians: Math.PI,
          isInGarage: false
        },
        raceEvents: {
          hasData: true,
          isOnTrack: true,
          isInGarage: false,
          isGarageVisible: false
        },
        fuelPit: {}
      });
  }

  if (overlayId === 'pit-service') {
    const pitServiceSettings = pitServiceSettingsModel();
    const releaseRow = pitSignalMetricRow('Release', referenceDisplayRow(frame)?.isPit ? 'pit road' : '--', referenceDisplayRow(frame)?.isPit ? 'info' : 'normal', 'pit-service.signal.release');
    const pitStatusRow = pitSignalMetricRow('Pit status', status || '--', 'normal', 'pit-service.signal.status');
    const fuelRequestRow = ['Fuel request', '--', 'normal', [
      pitSegment('Requested', '--', 'waiting', 'pit-service.service.fuel-requested'),
      pitSegment('Selected', '--', 'waiting', 'pit-service.service.fuel-selected')
    ]];
    const tearoffRow = ['Tearoff', '--', 'normal', [
      pitSegment('Requested', '--', 'waiting', 'pit-service.service.tearoff-requested')
    ]];
    const repairRow = ['Repair', 'Available', 'success', [
      pitSegment('Required', '--', 'success', 'pit-service.service.repair-required'),
      pitSegment('Optional', '--', 'success', 'pit-service.service.repair-optional')
    ]];
    const fastRepairRow = ['Fast repair', '--', 'normal', [
      pitSegment('Selected', '--', 'waiting', 'pit-service.service.fast-repair-selected'),
      pitSegment('Available', '--', 'waiting', 'pit-service.service.fast-repair-available')
    ]];
    const metricSections = filterMetricSectionsByContent([
      ['Pit Signal', [releaseRow, pitStatusRow]],
      ['Service Request', [fuelRequestRow, tearoffRow, repairRow, fastRepairRow]]
    ], pitServiceSettings.disabledContent);
    return metricsModel(
      overlayId,
      'Pit Service',
      status,
      replayHeaderItems(frame, status, pitServiceSettings),
      metricSections.flatMap(([, rows]) => rows),
      sourceFromSettings(pitServiceSettings, 'source: race-start replay'),
      [],
      metricSections);
  }

  if (overlayId === 'gap-to-leader') {
    const gapSettings = gapSettingsModel();
    return {
      overlayId,
      title: 'Gap To Leader',
      status: isPreGreen ? 'waiting for timing' : 'live | race gap',
      source: sourceFromSettings(gapSettings, 'source: race-start replay'),
      bodyKind: 'graph',
      columns: [],
      rows: [],
      metrics: [],
      points: isPreGreen ? [] : Array.from({ length: 24 }, (_, point) => 30 - point * 0.7 + Math.sin(point / 2) * 1.4),
      headerItems: replayHeaderItems(frame, isPreGreen ? 'waiting for timing' : 'live | race gap', gapSettings)
    };
  }

  if (overlayId === 'flags') {
    const flags = isPreGreen
      ? [flagItem('green', 'green', relativeSeconds >= 0 ? 'Start' : 'Ready', null, 'success')]
      : [flagItem('green', 'green', 'Green', 'held', 'success')];
    return flagsModel(flags, isPreGreen ? 'race start' : 'green held');
  }

  return tableModel(overlayId, browserOverlayPage(overlayId).title, status, headerItems, []);
}

function captureDisplayModel(overlayId, frame, index, searchParams = null) {
  const embeddedModel = embeddedReplayDisplayModel(overlayId, frame);
  if (embeddedModel) {
    return embeddedModel;
  }

  const live = liveSnapshot(frame, index, searchParams);
  const models = live.models || {};
  const relativeSeconds = Number.isFinite(frame.raceStartRelativeSeconds)
    ? frame.raceStartRelativeSeconds
    : null;
  const phase = frame.sessionPhase || models.session?.sessionPhase || 'capture';
  const status = relativeSeconds == null
    ? `${phase} | frame ${frame.frameIndex}`
    : `${phase} | ${relativeSeconds >= 0 ? '+' : ''}${relativeSeconds}s`;

  if (assetBackedReplayOverlayModelIds.has(overlayId)) {
    return replayAssetBackedDisplayModel(overlayId, frame, index, searchParams, live);
  }

  // BrowserOverlayModelFactory is a C# application service and cannot be run
  // directly from this Node replay server. The remaining builders are isolated
  // here and emit BrowserOverlayDisplayModel-shaped JSON from replay frames.
  if (overlayId === 'relative') {
    const relative = relativeSettings();
    const relativeModel = relativeRows(models, relative.carsAhead, relative.carsBehind);
    return relativeTableModel(
      relativeModel.status,
      captureHeaderItems(models, relativeModel.status, relative),
      relativeModel.rows,
      sourceFromSettings(relative, 'source: capture-derived live replay'),
      relative.columns);
  }

  const headerItems = captureHeaderItems(models, status);

  if (overlayId === 'session-weather') {
    const sessionWeatherSettings = sessionWeatherSettingsModel();
    return captureSessionWeatherModel(models, status, sessionWeatherSettings);
  }

  if (overlayId === 'pit-service') {
    const pitServiceSettings = pitServiceSettingsModel();
    const localContext = localInCarOrPitContext(models, 'waiting for local pit-service context');
    if (!localContext.isAvailable) {
      return metricsModel(
        overlayId,
        'Pit Service',
        localContext.statusText,
        captureHeaderItems(models, localContext.statusText, pitServiceSettings),
        [],
        sourceFromSettings(pitServiceSettings, 'source: waiting'));
    }

    return capturePitServiceModel(models, status, captureHeaderItems(models, status, pitServiceSettings), searchParams);
  }

  if (overlayId === 'car-radar') {
    return captureCarRadarModel(models, status, headerItems);
  }

  if (overlayId === 'gap-to-leader') {
    return captureGapToLeaderModel(models, frame, index, searchParams);
  }

  if (overlayId === 'flags') {
    return captureFlagsModel(models);
  }

  if (overlayId === 'input-state') {
    return captureInputStateModel(models, status, index, searchParams);
  }

  return tableModel(overlayId, browserOverlayPage(overlayId).title, status, headerItems, [], 'source: capture-derived live replay');
}

function captureFlagsModel(models) {
  const session = models?.session || {};
  const flags = flagItemsFromSession(session.sessionFlags, session.sessionState);
  const status = flags.length > 0
    ? flags.map((flag) => flag.label).join(' + ').toLowerCase()
    : 'none';
  return flagsModel(flags, status, session.hasData !== true);
}

function embeddedReplayDisplayModel(overlayId, frame) {
  const candidates = [
    frame?.overlayModels,
    frame?.models?.overlayModels,
    frame?.live?.models?.overlayModels,
    frame?.browserOverlayModels,
    frame?.models?.browserOverlayModels,
    frame?.live?.models?.browserOverlayModels
  ];

  for (const candidate of candidates) {
    const model = displayModelFromCollection(candidate, overlayId);
    if (model) {
      return normalizeEmbeddedDisplayModel(model, overlayId);
    }
  }

  if (isDisplayModelForOverlay(frame?.model, overlayId)) {
    return normalizeEmbeddedDisplayModel(frame.model, overlayId);
  }

  return null;
}

function displayModelFromCollection(collection, overlayId) {
  if (!collection) {
    return null;
  }

  if (Array.isArray(collection)) {
    return collection.find((model) => isDisplayModelForOverlay(model, overlayId)) || null;
  }

  if (typeof collection === 'object') {
    const exact = collection[overlayId];
    if (isDisplayModelForOverlay(exact, overlayId)) {
      return exact;
    }

    const camelKey = overlayId.replace(/-([a-z])/g, (_, letter) => letter.toUpperCase());
    const camel = collection[camelKey];
    if (isDisplayModelForOverlay(camel, overlayId)) {
      return camel;
    }

    return Object.values(collection).find((model) => isDisplayModelForOverlay(model, overlayId)) || null;
  }

  return null;
}

function isDisplayModelForOverlay(model, overlayId) {
  return model
    && typeof model === 'object'
    && typeof model.overlayId === 'string'
    && model.overlayId.toLowerCase() === overlayId;
}

function normalizeEmbeddedDisplayModel(model, overlayId) {
  const page = browserOverlayPage(overlayId);
  const normalized = {
    overlayId,
    title: model.title || page.title,
    status: model.status || '',
    source: model.source || '',
    bodyKind: model.bodyKind || 'table',
    columns: Array.isArray(model.columns) ? model.columns : [],
    rows: Array.isArray(model.rows) ? model.rows : [],
    metrics: Array.isArray(model.metrics) ? model.metrics : [],
    points: Array.isArray(model.points) ? model.points : [],
    headerItems: Array.isArray(model.headerItems) ? model.headerItems : [],
    gridSections: Array.isArray(model.gridSections) ? model.gridSections : [],
    metricSections: Array.isArray(model.metricSections) ? model.metricSections : [],
    shouldRender: model.shouldRender !== false
  };

  for (const extensionKey of ['carRadar', 'trackMap', 'garageCover', 'streamChat', 'inputState', 'graph']) {
    if (model[extensionKey] && typeof model[extensionKey] === 'object') {
      normalized[extensionKey] = clonePlainObject(model[extensionKey]);
    }
  }

  return normalized;
}

function replayAssetBackedDisplayModel(overlayId, frame, index, searchParams = null, live = null) {
  const page = browserOverlayPage(overlayId);
  return browserOverlayApiResponse(overlayId, page.modelRoute, {
    live: live ?? liveSnapshot(frame, index, searchParams),
    settings: settings(overlayId, frame, searchParams)
  }).model;
}

function captureCarRadarModel(models, fallbackStatus, headerItems) {
  const spatial = models.spatial || {};
  const inCar = isPlayerInCar(models);
  const cars = Array.isArray(spatial.cars) ? spatial.cars : [];
  const strongestMulticlassApproach = carRadarMulticlassApproach(spatial);
  const hasCurrentSignal = Boolean(
    spatial.hasCarLeft === true
    || spatial.hasCarRight === true
    || strongestMulticlassApproach
    || cars.length > 0);
  const carRadar = {
    isAvailable: inCar,
    hasCarLeft: spatial.hasCarLeft === true,
    hasCarRight: spatial.hasCarRight === true,
    cars,
    strongestMulticlassApproach,
    showMulticlassWarning: true,
    previewVisible: false,
    hasCurrentSignal,
    referenceCarClassColorHex: spatial.referenceCarClassColorHex
  };
  const status = !inCar
    ? 'waiting for player in car'
    : spatial.hasData === false
      ? 'waiting for radar'
      : spatial.hasCarLeft && spatial.hasCarRight
        ? 'cars both sides'
        : spatial.hasCarLeft
          ? 'car left'
          : spatial.hasCarRight
            ? 'car right'
            : strongestMulticlassApproach
              ? 'faster class'
              : fallbackStatus || 'clear';

  return {
    ...tableModel('car-radar', 'Car Radar', status, headerItems, [], inCar && spatial.hasData !== false ? 'source: capture-derived spatial telemetry' : 'source: waiting'),
    bodyKind: 'car-radar',
    carRadar: {
      ...carRadar,
      renderModel: carRadarRenderModelFromState(carRadar)
    }
  };
}

function carRadarMulticlassApproach(spatial) {
  const approaches = Array.isArray(spatial?.multiclassApproaches)
    ? spatial.multiclassApproaches
    : spatial?.strongestMulticlassApproach
      ? [spatial.strongestMulticlassApproach]
      : [];
  return approaches
    .filter(isInCarRadarMulticlassWarningRange)
    .sort((left, right) =>
      Math.abs(left?.relativeSeconds ?? Number.POSITIVE_INFINITY)
        - Math.abs(right?.relativeSeconds ?? Number.POSITIVE_INFINITY)
      || Number(right?.urgency || 0) - Number(left?.urgency || 0))[0] || null;
}

function isInCarRadarMulticlassWarningRange(approach) {
  const seconds = approach?.relativeSeconds;
  return Number.isFinite(seconds) && seconds < -2 && seconds >= -5;
}

function isPlayerInCar(models) {
  const reference = models.reference || {};
  const race = models.raceEvents || {};
  if (reference.focusIsPlayer === false) {
    return false;
  }

  if (race.isInGarage === true || race.isGarageVisible === true) {
    return false;
  }

  if (race.isOnTrack === false) {
    return false;
  }

  return reference.hasData !== false;
}

function localInCarOrPitContext(models, statusText) {
  const reference = models.reference || {};
  const race = models.raceEvents || {};
  const fuelPit = models.fuelPit || {};
  if (reference.focusIsPlayer === false) {
    return { isAvailable: false, reason: 'focus_on_another_car', statusText };
  }

  if (race.isInGarage === true || race.isGarageVisible === true) {
    return { isAvailable: false, reason: 'garage', statusText };
  }

  if (race.isOnTrack === true) {
    return { isAvailable: true, reason: 'available', statusText: 'live' };
  }

  if (race.onPitRoad === true
    || reference.onPitRoad === true
    || reference.playerCarInPitStall === true
    || fuelPit.onPitRoad === true
    || fuelPit.pitstopActive === true
    || fuelPit.playerCarInPitStall === true
    || fuelPit.teamOnPitRoad === true) {
    return { isAvailable: true, reason: 'available', statusText: 'live' };
  }

  return { isAvailable: false, reason: 'not_in_car', statusText };
}

function captureSessionWeatherModel(models, fallbackStatus, overlaySettings = sessionWeatherSettingsModel()) {
  const session = models.session || {};
  const weather = models.weather || {};
  const hasWeather = weather.hasData === true;
  const status = hasWetSurfaceSignal(weather)
    ? titleCaseDisplay(weather.trackWetnessLabel) || 'Declared Wet'
    : session.sessionType || fallbackStatus || 'live session';
  const headerItems = captureHeaderItems(models, status, overlaySettings);
  return sessionWeatherMetricsModel(
    session,
    weather,
    models.raceProgress,
    models.raceProjection,
    headerItems,
    '',
    models,
    overlaySettings.disabledContent);
}

function sessionWeatherMetricsModel(session, weather, raceProgress, raceProjection, headerItems, source, models = null, disabledContent = sessionWeatherDisabledContent) {
  const sessionRow = sessionWeatherRow('Session', formatSessionSummary(session), 'normal', [
    metricSegment('Type', session?.sessionType, 'normal', { key: 'session-weather.session.type' }),
    ...availableMetricSegments(metricSegment('Name', meaningfulSessionName(session), 'normal', { key: 'session-weather.session.name' })),
    metricSegment('Mode', session?.teamRacing === true ? 'Team' : session?.teamRacing === false ? 'Solo' : null, 'normal', { key: 'session-weather.session.mode' })
  ]);
  const eventRow = sessionWeatherRow('Event', formatSessionEvent(session), 'normal', [
    metricSegment('Event', session?.eventType, 'normal', { key: 'session-weather.event.type' }),
    metricSegment('Car', session?.carDisplayName, 'normal', { key: 'session-weather.event.car' })
  ]);
  const clock = sessionClockParts(session);
  const clockRow = sessionWeatherRow('Clock', formatSessionClock(session), 'normal', [
    metricSegment('Elapsed', clock.elapsed, 'normal', { key: 'session-weather.clock.elapsed' }),
    metricSegment(clock.remainingLabel, clock.remaining, 'normal', { key: 'session-weather.clock.remaining' }),
    metricSegment('Total', clock.total, 'normal', { key: 'session-weather.clock.total' })
  ]);
  const laps = sessionLapParts(session, raceProgress, raceProjection);
  const lapsRow = sessionWeatherRow('Laps', formatSessionLaps(session, raceProgress, raceProjection), 'normal', [
    metricSegment('Remaining', laps.remaining, 'normal', { key: 'session-weather.laps.remaining' }),
    metricSegment('Total', laps.total, 'normal', { key: 'session-weather.laps.total' })
  ]);
  const trackRow = sessionWeatherRow('Track', joinAvailable(session?.trackDisplayName, formatTrackLength(session?.trackLengthKm)), 'normal', [
    metricSegment('Name', session?.trackDisplayName, 'normal', { key: 'session-weather.track.name' }),
    metricSegment('Length', formatTrackLength(session?.trackLengthKm), 'normal', { key: 'session-weather.track.length' })
  ]);
  const wetTone = hasWetSurfaceSignal(weather) ? 'info' : 'normal';
  const tempsTone = strongestTone(temperatureTone(weather?.trackTempCrewC), 'normal');
  const trackTempAccent = temperatureAccentHex(weather?.trackTempCrewC);
  const tempsRow = sessionWeatherRow('Temps', formatWeatherTemps(weather), tempsTone, [
    metricSegment('Air', formatTemp(weather?.airTempC), temperatureTone(weather?.airTempC), { accentHex: temperatureAccentHex(weather?.airTempC), key: 'session-weather.temps.air' }),
    metricSegment('Track', formatTemp(weather?.trackTempCrewC), temperatureTone(weather?.trackTempCrewC), { accentHex: trackTempAccent, key: 'session-weather.temps.track' })
  ]);
  const wetnessToneValue = strongestTone(wetTone, wetnessTone(weather));
  const wetnessAccent = wetnessAccentHex(weather);
  const wetnessLabel = titleCaseDisplay(weather?.trackWetnessLabel) || trackWetnessLabel(weather?.trackWetness);
  const surfaceRow = sessionWeatherRow('Surface', formatWeatherSurface(weather), wetnessToneValue, [
    metricSegment('Wetness', wetnessLabel, wetnessToneValue, { accentHex: wetnessAccent, key: 'session-weather.surface.wetness' }),
    metricSegment('Declared', weather?.weatherDeclaredWet === true ? 'Wet' : weather?.weatherDeclaredWet === false ? 'Dry' : null, weather?.declaredWetSurfaceMismatch === true ? 'warning' : declaredWetTone(weather), { accentHex: weather?.weatherDeclaredWet === true ? wetnessAccent || '#33CEFF' : null, key: 'session-weather.surface.declared' }),
    metricSegment('Rubber', titleCaseDisplay(weather?.rubberState), 'normal', { key: 'session-weather.surface.rubber' })
  ]);
  const skyRow = sessionWeatherRow('Sky', formatWeatherSky(weather), rainTone(weather?.precipitationPercent), [
    metricSegment('Skies', titleCaseDisplay(weather?.skiesLabel), 'normal', { key: 'session-weather.sky.skies' }),
    metricSegment('Weather', weather?.weatherType, 'normal', { key: 'session-weather.sky.weather' }),
    metricSegment('Rain', formatWeatherPercent(weather?.precipitationPercent), rainTone(weather?.precipitationPercent), { accentHex: rainAccentHex(weather?.precipitationPercent), key: 'session-weather.sky.rain' })
  ]);
  const localWind = formatLocalWind(models, weather);
  const windSegments = [
    metricSegment('Dir', cardinalDirection(weather?.windDirectionRadians), 'normal', { key: 'session-weather.wind.direction' }),
    metricSegment('Speed', formatSpeed(weather?.windVelocityMetersPerSecond), 'normal', { key: 'session-weather.wind.speed' })
  ];
  if (localWind) {
    windSegments.push(metricSegment('Facing', localWind.directionLabel, 'normal', { rotationDegrees: localWind.relativeDegrees, key: 'session-weather.wind.facing' }));
  }
  const windRow = sessionWeatherRow('Wind', formatWindAtmosphere(weather, localWind), 'normal', windSegments);
  const atmosphereRow = sessionWeatherRow('Atmosphere', formatWeatherAtmosphere(weather), 'normal', [
    metricSegment('Hum', formatWeatherPercent(weather?.relativeHumidityPercent), 'normal', { key: 'session-weather.atmosphere.humidity' }),
    metricSegment('Fog', formatWeatherPercent(weather?.fogLevelPercent), 'normal', { key: 'session-weather.atmosphere.fog' }),
    metricSegment('Pressure', formatAirPressure(weather?.airPressurePa), 'normal', { key: 'session-weather.atmosphere.pressure' })
  ]);
  const sessionRows = [sessionRow, clockRow, ...availableRows(eventRow), trackRow, lapsRow];
  const weatherRows = [
    surfaceRow,
    skyRow,
    windRow,
    tempsRow,
    ...availableRows(atmosphereRow)
  ];
  const metricSections = filterMetricSectionsByContent([
    ['Session', sessionRows],
    ['Weather', weatherRows]
  ], disabledContent);
  return metricsModel(
    'session-weather',
    'Session / Weather',
    sessionWeatherStatus(session, weather),
    headerItems,
    metricSections.flatMap(([, rows]) => rows),
    source,
    [],
    metricSections);
}

function availableRows(row) {
  return row?.value && row.value !== '--' ? [row] : [];
}

function filterMetricSectionsByContent(metricSections, disabledContent = new Set()) {
  if (!(disabledContent instanceof Set) || disabledContent.size === 0) {
    return metricSections;
  }

  return metricSections
    .map(([title, rows]) => [
      title,
      rows
        .map((row) => filterMetricRowByContent(row, disabledContent))
        .filter(Boolean)
    ])
    .filter(([, rows]) => rows.length > 0);
}

function filterMetricRowByContent(row, disabledContent) {
  if (Array.isArray(row)) {
    const [label, value, tone, segments] = row;
    if (!Array.isArray(segments) || segments.length === 0) {
      return row;
    }

    const filtered = segments.filter((segment) => !segment?.key || !disabledContent.has(segment.key));
    if (filtered.length === 0) {
      return null;
    }

    return [label, joinAvailable(...filtered.map((segment) => segment?.value)), tone, filtered];
  }

  if (!Array.isArray(row?.segments) || row.segments.length === 0) {
    return row;
  }

  const filtered = row.segments.filter((segment) => !segment?.key || !disabledContent.has(segment.key));
  if (filtered.length === 0) {
    return null;
  }

  return {
    ...row,
    value: joinAvailable(...filtered.map((segment) => segment?.value)),
    segments: filtered
  };
}

function sessionWeatherStatus(session, weather) {
  if (weather?.declaredWetSurfaceMismatch === true) return 'wet mismatch';
  if (hasWetSurfaceSignal(weather)) return titleCaseDisplay(weather?.trackWetnessLabel) || 'Declared Wet';
  return String(session?.sessionType || '').trim() || 'live session';
}

function sessionWeatherRow(label, value, tone = 'normal', segments = []) {
  return {
    label,
    value: value || '--',
    tone,
    segments
  };
}

function metricSegment(label, value, tone = 'normal', extra = {}) {
  const text = String(value ?? '').trim();
  return { label, value: text && text !== '--' ? text : '--', tone, ...extra };
}

function availableMetricSegments(segment) {
  return segment?.value && segment.value !== '--' ? [segment] : [];
}

function strongestTone(left, right) {
  return toneWeight(left) >= toneWeight(right) ? left : right;
}

function toneWeight(tone) {
  switch (tone) {
    case 'error': return 50;
    case 'warning': return 40;
    case 'info': return 30;
    case 'success': return 20;
    case 'waiting': return 10;
    default: return 0;
  }
}

function capturePitServiceModel(models, fallbackStatus, headerItems, searchParams = null) {
  const pitServiceSettings = pitServiceSettingsModel();
  const spoofAllRows = pitServiceSpoofAllRowsEnabled(searchParams);
  const pitModels = spoofAllRows
    ? pitServiceAllRowsModels(models)
    : models;
  const pit = pitModels.fuelPit || {};
  const release = pitReleaseState(pit);
  const status = pitStatus(pit, release) || fallbackStatus || 'pit ready';
  const effectiveHeaderItems = spoofAllRows
      ? [
        { key: 'timeRemaining', value: formatHeaderSessionTimeRemaining(pitModels.session) || '' }
      ]
    : headerItems;
  const releaseRow = pitSignalMetricRow('Release', release.value, release.tone, 'pit-service.signal.release');
  const pitStatusRow = pitSignalMetricRow('Pit status', pitServiceStatusText(pit.pitServiceStatus), pitServiceActivityTone(pit, release), 'pit-service.signal.status');
  const timeLaps = pitTimeLaps(pitModels);
  const fuelRequestRow = pitFuelRequestSegmentedRow(pit);
  const tearoffRow = pitTearoffSegmentedRow(pit);
  const repairRow = pitRepairSegmentedRow(pit);
  const fastRepairRow = pitFastRepairSegmentedRow(pit);
  const metricSections = filterMetricSectionsByContent([
    ...(timeLaps === '--' ? [] : [['Session', [pitSessionTimeLapsSegmentedRow(pitModels)]]]),
    ['Pit Signal', [releaseRow, pitStatusRow]],
    ['Service Request', [fuelRequestRow, tearoffRow, repairRow, fastRepairRow]]
  ], pitServiceSettings.disabledContent);
  const metrics = metricSections.flatMap(([, rows]) => rows);
  return metricsModel(
    'pit-service',
    'Pit Service',
    status,
    effectiveHeaderItems,
    metrics,
    sourceFromSettings(
      pitServiceSettings,
      spoofAllRows ? 'source: spoofed pit service all-rows preview' : 'source: player/team pit service telemetry'),
    spoofAllRows ? pitServiceGridSectionsFromData(pitModels, pit) : [],
    metricSections);
}

function pitServiceSpoofAllRowsEnabled(searchParams) {
  const value = searchParams?.get('pitService') || '';
  return ['all', 'all-rows', 'full'].includes(value.trim().toLowerCase());
}

function pitServiceAllRowsModels(models) {
  const pit = pitServiceAllRowsPit();
  return {
    ...models,
    fuelPit: pit,
    session: {
      ...(models.session || {}),
      sessionType: 'Race',
      sessionName: 'Road Atlanta spoof',
      sessionTimeRemainSeconds: 86258.266667,
      sessionLapsRemain: 4,
      sessionLapsRemainEx: 4,
      sessionLapsTotal: 5
    },
    raceProgress: {
      ...(models.raceProgress || {}),
      raceLapsRemaining: 4
    },
    raceProjection: {
      ...(models.raceProjection || {}),
      estimatedTeamLapsRemaining: 4,
      estimatedFinishLap: 5
    },
    tireCompounds: pitServiceAllRowsTireCompounds(models),
    tireCondition: pitServiceAllRowsTireCondition()
  };
}

function pitServiceAllRowsPit() {
  return {
    hasData: true,
    quality: 'spoofed',
    onPitRoad: true,
    teamOnPitRoad: true,
    playerCarInPitStall: true,
    pitstopActive: true,
    pitServiceStatus: 1,
    pitServiceFlags: 0x7b,
    pitServiceFuelLiters: 45.5,
    pitRepairLeftSeconds: 12.2,
    pitOptRepairLeftSeconds: 18.4,
    playerCarDryTireSetLimit: 4,
    tireSetsUsed: 2,
    tireSetsAvailable: 2,
    leftTireSetsUsed: 1,
    rightTireSetsUsed: 1,
    frontTireSetsUsed: 1,
    rearTireSetsUsed: 1,
    leftTireSetsAvailable: 2,
    rightTireSetsAvailable: 2,
    frontTireSetsAvailable: 2,
    rearTireSetsAvailable: 2,
    leftFrontTiresUsed: 1,
    rightFrontTiresUsed: 1,
    leftRearTiresUsed: 1,
    rightRearTiresUsed: 1,
    leftFrontTiresAvailable: 2,
    rightFrontTiresAvailable: 2,
    leftRearTiresAvailable: 2,
    rightRearTiresAvailable: 2,
    requestedTireCompound: 1,
    fastRepairUsed: 0,
    fastRepairAvailable: 1,
    teamFastRepairsUsed: 1
  };
}

function pitServiceAllRowsTireCompounds(models) {
  const playerCarIdx = models.driverDirectory?.playerCarIdx ?? models.reference?.playerCarIdx ?? 15;
  return {
    hasData: true,
    quality: 'spoofed',
    definitions: [
      { index: 0, label: 'Medium', shortLabel: 'M', isWet: false },
      { index: 1, label: 'Soft', shortLabel: 'S', isWet: false },
      { index: 2, label: 'Wet', shortLabel: 'W', isWet: true }
    ],
    playerCar: {
      carIdx: playerCarIdx,
      compoundIndex: 0,
      label: 'Medium',
      shortLabel: 'M',
      isWet: false,
      isPlayer: true,
      isFocus: true
    },
    focusCar: {
      carIdx: playerCarIdx,
      compoundIndex: 0,
      label: 'Medium',
      shortLabel: 'M',
      isWet: false,
      isPlayer: true,
      isFocus: true
    },
    cars: []
  };
}

function pitServiceAllRowsTireCondition() {
  return {
    hasData: true,
    quality: 'spoofed',
    leftFront: pitServiceTireCorner('LF', [0.92, 0.91, 0.90], [80, 81, 82], 206.8, 20000, true),
    rightFront: pitServiceTireCorner('RF', [0.93, 0.92, 0.91], [79, 80, 81], 203.4, 19750, true),
    leftRear: pitServiceTireCorner('LR', [0.96, 0.95, 0.94], [72, 73, 74], 196.5, 20000, false),
    rightRear: pitServiceTireCorner('RR', [0.97, 0.96, 0.95], [73, 74, 75], 198.6, 19750, true)
  };
}

function pitServiceTireCorner(corner, wear, tempC, pressureKpa, odometerMeters, changeRequested) {
  return {
    corner,
    wear: { left: wear[0], middle: wear[1], right: wear[2] },
    temperatureC: { left: tempC[0], middle: tempC[1], right: tempC[2] },
    coldPressureKpa: pressureKpa,
    odometerMeters,
    pitServicePressureKpa: null,
    blackBoxColdPressurePa: null,
    changeRequested
  };
}

function pitServiceGridSectionsFromData(models, pit) {
  const condition = models.tireCondition || {};
  const rows = [
    pitServiceCompoundRow(models.tireCompounds || {}, pit),
    pitServiceChangeRow(condition, pit),
    pitServiceSetLimitRow(pit),
    pitServiceAvailableRow(pit),
    pitServiceUsedRow(pit),
    pitServicePressureRow(condition),
    pitServiceTemperatureRow(condition),
    pitServiceWearRow(condition),
    pitServiceDistanceRow(condition)
  ].filter(Boolean);
  return rows.length
    ? [{ title: 'Tire Analysis', headers: ['Info', 'FL', 'FR', 'RL', 'RR'], rows }]
    : [];
}

function pitServiceCompoundRow(tireCompounds, pit) {
  const current = firstText(tireCompounds.playerCar?.shortLabel, tireCompounds.playerCar?.label);
  const requestedDefinition = Array.isArray(tireCompounds.definitions)
    ? tireCompounds.definitions.find((definition) => definition?.index === pit?.requestedTireCompound)
    : null;
  const requested = firstText(requestedDefinition?.shortLabel, requestedDefinition?.label);
  const isChanging = Boolean(requested && requested !== current);
  const value = isChanging ? requested : firstText(current, requested);
  const cells = [value, value, value, value].map((cellValue) => ({
    value: cellValue,
    tone: isChanging ? 'success' : 'info'
  }));
  return value ? pitServiceGridRow('Compound', cells, 'normal') : null;
}

function pitServiceChangeRow(condition, pit) {
  const hasEvidence = Number.isInteger(pit?.pitServiceFlags)
    || pitServiceCorners().some(({ key }) => condition?.[key]?.changeRequested != null);
  if (!hasEvidence) return null;
  const values = pitServiceCorners().map(({ key, flag }) => {
    const requested = condition?.[key]?.changeRequested;
    const selected = requested === true || (requested == null && (pit?.pitServiceFlags & flag) !== 0);
    return {
      value: selected ? 'Change' : 'Keep',
      tone: selected ? 'success' : 'info'
    };
  });
  return pitServiceGridRow('Change', values);
}

function pitServiceSetLimitRow(pit) {
  const value = Number.isInteger(pit?.playerCarDryTireSetLimit) && pit.playerCarDryTireSetLimit > 0
    ? `${pit.playerCarDryTireSetLimit} sets`
    : null;
  return value ? pitServiceGridRow('Set limit', [value, value, value, value]) : null;
}

function pitServiceAvailableRow(pit) {
  const values = [
    pitServiceAvailableCell(pit?.leftFrontTiresAvailable ?? pit?.leftTireSetsAvailable ?? pit?.frontTireSetsAvailable ?? pit?.tireSetsAvailable),
    pitServiceAvailableCell(pit?.rightFrontTiresAvailable ?? pit?.rightTireSetsAvailable ?? pit?.frontTireSetsAvailable ?? pit?.tireSetsAvailable),
    pitServiceAvailableCell(pit?.leftRearTiresAvailable ?? pit?.leftTireSetsAvailable ?? pit?.rearTireSetsAvailable ?? pit?.tireSetsAvailable),
    pitServiceAvailableCell(pit?.rightRearTiresAvailable ?? pit?.rightTireSetsAvailable ?? pit?.rearTireSetsAvailable ?? pit?.tireSetsAvailable)
  ];
  return pitServiceAnyCellHasValue(values) ? pitServiceGridRow('Available', values) : null;
}

function pitServiceAvailableCell(value) {
  const display = pitServiceCounter(value, true);
  return {
    value: display,
    tone: display === '0' ? 'error' : 'normal'
  };
}

function pitServiceUsedRow(pit) {
  const values = [
    pitServiceCounter(pit?.leftFrontTiresUsed ?? pit?.leftTireSetsUsed ?? pit?.frontTireSetsUsed ?? pit?.tireSetsUsed, false),
    pitServiceCounter(pit?.rightFrontTiresUsed ?? pit?.rightTireSetsUsed ?? pit?.frontTireSetsUsed ?? pit?.tireSetsUsed, false),
    pitServiceCounter(pit?.leftRearTiresUsed ?? pit?.leftTireSetsUsed ?? pit?.rearTireSetsUsed ?? pit?.tireSetsUsed, false),
    pitServiceCounter(pit?.rightRearTiresUsed ?? pit?.rightTireSetsUsed ?? pit?.rearTireSetsUsed ?? pit?.tireSetsUsed, false)
  ];
  return pitServiceAnyCellHasValue(values) ? pitServiceGridRow('Used', values) : null;
}

function pitServicePressureRow(condition) {
  const values = pitServiceCorners().map(({ key }) => {
    const corner = condition?.[key] || {};
    const kpa = firstFinite([
      corner.pitServicePressureKpa,
      corner.coldPressureKpa,
      Number.isFinite(corner.blackBoxColdPressurePa) ? corner.blackBoxColdPressurePa / 1000 : null
    ]);
    return formatKpa(kpa);
  });
  return pitServiceAnyCellHasValue(values) ? pitServiceGridRow('Pressure', values) : null;
}

function pitServiceTemperatureRow(condition) {
  const values = pitServiceCorners().map(({ key }) => {
    const raw = acrossValues(condition?.[key]?.temperatureC)
      .filter((value) => Number.isFinite(value) && value > 0 && value < 250);
    const converted = raw.map((value) => isImperial() ? value * 9 / 5 + 32 : value);
    return pitServiceFormatAcross(converted, '0', isImperial() ? ' F' : ' C');
  });
  return pitServiceAnyCellHasValue(values) ? pitServiceGridRow('Temp', values) : null;
}

function pitServiceWearRow(condition) {
  const values = pitServiceCorners().map(({ key }) => {
    const raw = acrossValues(condition?.[key]?.wear)
      .filter((value) => Number.isFinite(value) && value > 0 && value <= 1)
      .map((value) => value * 100);
    return pitServiceFormatAcross(raw, '0', '%');
  });
  return pitServiceAnyCellHasValue(values) ? pitServiceGridRow('Wear', values) : null;
}

function pitServiceDistanceRow(condition) {
  const values = pitServiceCorners().map(({ key }) => {
    const meters = condition?.[key]?.odometerMeters;
    return formatMeters(meters);
  });
  return pitServiceAnyCellHasValue(values) ? pitServiceGridRow('Distance', values) : null;
}

function formatKpa(kpa) {
  if (!Number.isFinite(kpa) || kpa <= 0) return '--';
  return isImperial()
    ? `${Math.round(kpa * 0.145037738)} psi`
    : `${(kpa / 100).toFixed(1)} bar`;
}

function formatMeters(meters) {
  if (!Number.isFinite(meters) || meters <= 0) return '--';
  return isImperial()
    ? `${(meters / 1609.344).toFixed(1)} mi`
    : `${(meters / 1000).toFixed(1)} km`;
}

function pitServiceCorners() {
  return [
    { key: 'leftFront', label: 'LF', flag: 0x01 },
    { key: 'rightFront', label: 'RF', flag: 0x02 },
    { key: 'leftRear', label: 'LR', flag: 0x04 },
    { key: 'rightRear', label: 'RR', flag: 0x08 }
  ];
}

function pitServiceCounter(value, allowZero) {
  if (value === 255) return 'unlim';
  return Number.isInteger(value) && (value > 0 || allowZero && value === 0) ? String(value) : '--';
}

function acrossValues(values) {
  return [values?.left, values?.middle, values?.right];
}

function pitServiceFormatAcross(values, format, suffix) {
  if (!values.length) return '--';
  const formatted = values.map((value) => format === '0' ? Math.round(value).toFixed(0) : value.toFixed(1));
  const distinct = new Set(formatted);
  return `${distinct.size === 1 ? formatted[0] : formatted.join('/')}${suffix}`;
}

function pitServiceAnyCellHasValue(values) {
  return values.some((value) => value && value !== '--');
}

function pitServiceGridRow(label, values, tone = 'normal') {
  const cells = values.map((cell) => {
    if (cell && typeof cell === 'object') {
      return {
        value: cell.value || '--',
        tone: cell.tone || tone
      };
    }

    return { value: cell, tone };
  });
  return {
    label,
    cells,
    tone
  };
}

function captureInputStateModel(models, fallbackStatus, index, searchParams = null) {
  const inputs = models.inputs || {};
  if (inputs.hasData !== true) {
    return inputStateModel(
      'waiting for car telemetry',
      [],
      '',
      false,
      inputs,
      []);
  }

  const status = Number.isInteger(inputs.engineWarnings) && inputs.engineWarnings > 0
    ? 'engine warning'
    : joinAvailable(formatGear(inputs.gear), formatRpm(inputs.rpm), inputs.brakeAbsActive === true ? 'ABS' : null);
  return inputStateModel(
    status || fallbackStatus || 'live',
    [],
    '',
    true,
    inputs,
    captureInputTrace(index, inputs, searchParams));
}

function inputStateModel(status, headerItems, source, isAvailable, inputs, trace = null) {
  const displayInputs = isAvailable && inputs ? inputs : {};
  const hasGraph = inputStateReviewSettings.showThrottleTrace
    || inputStateReviewSettings.showBrakeTrace
    || inputStateReviewSettings.showClutchTrace;
  const hasRail = inputStateReviewSettings.showThrottle
    || inputStateReviewSettings.showBrake
    || inputStateReviewSettings.showClutch
    || inputStateReviewSettings.showSteering
    || inputStateReviewSettings.showGear
    || inputStateReviewSettings.showSpeed;
  const hasContent = hasGraph || hasRail;
  return {
    overlayId: 'input-state',
    title: 'Inputs',
    status: hasContent ? status : 'no input content enabled',
    source,
    bodyKind: 'inputs',
    columns: [],
    rows: [],
    metrics: [],
    points: [],
    headerItems,
    inputs: {
      isAvailable,
      throttle: displayInputs.throttle,
      brake: displayInputs.brake,
      clutch: displayInputs.clutch,
      steeringWheelAngle: displayInputs.steeringWheelAngle,
      speedMetersPerSecond: displayInputs.speedMetersPerSecond,
      gear: displayInputs.gear,
      speedText: formatSpeed(displayInputs.speedMetersPerSecond),
      gearText: formatGear(displayInputs.gear),
      steeringText: formatSteering(displayInputs.steeringWheelAngle),
      brakeAbsActive: displayInputs.brakeAbsActive === true,
      ...inputStateReviewSettings,
      hasGraph,
      hasRail,
      hasContent,
      sampleIntervalMilliseconds: 50,
      maximumTracePoints: 180,
      trace: isAvailable
        ? Array.isArray(trace) && trace.length > 0
          ? trace
          : [inputTracePoint(displayInputs)]
        : []
    }
  };
}

function captureInputTrace(index, currentInputs, searchParams = null) {
  const window = replayWindowFromSearchParams(searchParams) ?? replayWindowStatus();
  const sourceWindow = window?.source;
  const frameWindow = window?.frame;
  const windowStartIndex = sourceWindow
    ? firstFrameIndexAtOrAfterSourceElapsed(sourceWindow.start, index)
    : frameWindow?.start;
  const startIndex = Math.max(0, windowStartIndex ?? index - 179);
  const trace = replay.frames
    .slice(startIndex, index + 1)
    .map((frame) => frame.live?.models?.inputs)
    .filter((inputs) => inputs?.hasData === true)
    .map(inputTracePoint);

  if (trace.length === 0 && currentInputs?.hasData === true) {
    trace.push(inputTracePoint(currentInputs));
  }

  return trace;
}

function inputTracePoint(inputs) {
  return {
    throttle: clamp(Number.isFinite(inputs?.throttle) ? inputs.throttle : 0, 0, 1),
    brake: clamp(Number.isFinite(inputs?.brake) ? inputs.brake : 0, 0, 1),
    clutch: clamp(Number.isFinite(inputs?.clutch) ? inputs.clutch : 0, 0, 1),
    brakeAbsActive: inputs?.brakeAbsActive === true
  };
}

function captureHeaderItems(models, status, overlaySettings = null) {
  const items = [];
  const seconds = models.session?.sessionTimeRemainSeconds;
  if (overlaySettings?.showHeaderTimeRemaining !== false && Number.isFinite(seconds) && seconds >= 0) {
    items.push({ key: 'timeRemaining', value: formatDuration(seconds, models.session?.sessionPhase) });
  }
  return items;
}

function syntheticRelativeRows(frame, isPreGreen, carsAhead, carsBehind) {
  const rows = [
    relativeDisplayRow('2', 'Lap Ahead LMP2', isPreGreen ? '--' : '-8.940', false, '#33CEFF', 'ahead', 1),
    relativeDisplayRow('3', 'Race Start Leader', isPreGreen ? '--' : '-2.412', false, '#FFDA59', 'ahead'),
    relativeDisplayRow('5', referenceDisplayRow(frame)?.cells?.[2] || 'Replay focus', '0.000', true, '#33CEFF', null, 0),
    relativeDisplayRow('6', 'Following GT3', isPreGreen ? '--' : '+3.184', false, '#FFDA59', 'behind'),
    relativeDisplayRow('12', 'Lap Behind GT4', isPreGreen ? '--' : '+11.720', false, '#FF4FD8', 'behind', -2)
  ];
  const ahead = rows.filter((row) => row.isAhead === true).slice(-clamp(carsAhead, 0, 8));
  const reference = rows.find((row) => row.isReference === true);
  const behind = rows.filter((row) => row.isBehind === true).slice(0, clamp(carsBehind, 0, 8));
  const actualRows = [
    ...ahead,
    ...(reference ? [reference] : []),
    ...behind
  ];
  return {
    rows: stableRelativeRows(actualRows, carsAhead, carsBehind),
    status: relativeStatus(reference?.valuesByKey?.['relative-position'], actualRows.filter((row) => !row.isReference).length, rows.length - 1)
  };
}

function relativeRows(models, carsAhead, carsBehind) {
  const referenceCarIdx = models.reference?.focusCarIdx ?? models.relative?.referenceCarIdx;
  const timingByCarIdx = new Map((models.timing?.overallRows || []).map((row) => [row.carIdx, row]));
  const scoringByCarIdx = new Map((models.scoring?.rows || []).map((row) => [row.carIdx, row]));
  const driverByCarIdx = new Map((models.driverDirectory?.drivers || []).map((row) => [row.carIdx, row]));
  const reference = timingByCarIdx.get(referenceCarIdx) || scoringByCarIdx.get(referenceCarIdx) || driverByCarIdx.get(referenceCarIdx);
  const rows = (models.relative?.rows || [])
    .filter((row) => hasRelativeDisplayEvidence(row));
  const ahead = rows
    .filter((row) => isRelativeAhead(row))
    .sort((a, b) => relativeSortKey(a) - relativeSortKey(b) || (a.carIdx ?? 0) - (b.carIdx ?? 0))
    .slice(0, clamp(carsAhead, 0, 8))
    .sort((a, b) => relativeSortKey(b) - relativeSortKey(a) || (a.carIdx ?? 0) - (b.carIdx ?? 0));
  const behind = rows
    .filter((row) => isRelativeBehind(row))
    .sort((a, b) => relativeSortKey(a) - relativeSortKey(b) || (a.carIdx ?? 0) - (b.carIdx ?? 0))
    .slice(0, clamp(carsBehind, 0, 8));
  const referencePosition = reference
    ? positionLabel(reference, scoringByCarIdx.get(referenceCarIdx))
    : null;
  const actualRows = [
    ...ahead.map((row) => relativeDisplayRow(
      positionLabel(row, scoringByCarIdx.get(row.carIdx)),
      displayDriver(row, scoringByCarIdx.get(row.carIdx), driverByCarIdx.get(row.carIdx)),
      relativeDelta(row, 'ahead'),
      false,
      row.carClassColorHex || scoringByCarIdx.get(row.carIdx)?.carClassColorHex || driverByCarIdx.get(row.carIdx)?.carClassColorHex,
      'ahead',
      relativeLapDelta(row, timingByCarIdx.get(row.carIdx), reference))),
    reference
      ? relativeDisplayRow(
        positionLabel(reference, scoringByCarIdx.get(referenceCarIdx)),
        displayDriver(reference, scoringByCarIdx.get(referenceCarIdx), driverByCarIdx.get(referenceCarIdx)),
        '0.000',
        true,
        reference.carClassColorHex || scoringByCarIdx.get(referenceCarIdx)?.carClassColorHex || driverByCarIdx.get(referenceCarIdx)?.carClassColorHex,
        null,
        0)
      : relativeDisplayRow('--', 'Replay focus', '0.000', true, null, null, 0),
    ...behind.map((row) => relativeDisplayRow(
      positionLabel(row, scoringByCarIdx.get(row.carIdx)),
      displayDriver(row, scoringByCarIdx.get(row.carIdx), driverByCarIdx.get(row.carIdx)),
      relativeDelta(row, 'behind'),
      false,
      row.carClassColorHex || scoringByCarIdx.get(row.carIdx)?.carClassColorHex || driverByCarIdx.get(row.carIdx)?.carClassColorHex,
      'behind',
      relativeLapDelta(row, timingByCarIdx.get(row.carIdx), reference)))
  ];
  return {
    rows: stableRelativeRows(actualRows, carsAhead, carsBehind),
    status: relativeStatus(referencePosition, actualRows.filter((row) => !row.isReference).length, rows.length)
  };
}

function stableRelativeRows(rows, carsAhead, carsBehind = carsAhead) {
  const aheadCapacity = clamp(carsAhead, 0, 8);
  const behindCapacity = clamp(carsBehind, 0, 8);
  const reference = rows.find((row) => row.isReference === true);
  const hasReference = Boolean(reference);
  const visibleRows = Math.max(1, Math.min(17, aheadCapacity + behindCapacity + (hasReference ? 1 : 0)));
  const stableRows = Array.from({ length: visibleRows }, () => relativePlaceholderRow());
  const ahead = rows.filter((row) => row.isAhead === true);
  const aheadStart = Math.max(0, aheadCapacity - ahead.length);
  ahead.forEach((row, index) => {
    if (aheadStart + index < stableRows.length) {
      stableRows[aheadStart + index] = row;
    }
  });

  if (hasReference && aheadCapacity < stableRows.length) {
    stableRows[aheadCapacity] = reference;
  }

  const behindStart = hasReference ? aheadCapacity + 1 : aheadCapacity;
  rows.filter((row) => row.isBehind === true).forEach((row, index) => {
    if (behindStart + index < stableRows.length) {
      stableRows[behindStart + index] = row;
    }
  });

  return stableRows;
}

function relativeStatus(referencePosition, shownRows, availableRows) {
  const position = String(referencePosition || '').trim();
  const prefix = position && position !== '--' ? position : 'live relative';
  return availableRows > shownRows
    ? `${prefix} - ${shownRows}/${availableRows} cars`
    : `${prefix} - ${shownRows} cars`;
}

function relativeDisplayRow(position, driver, delta, isReference, carClassColorHex, direction = null, relativeLapDelta = null) {
  return {
    cells: [position || '--', driver || 'Replay focus', delta || '--'],
    valuesByKey: {
      'relative-position': position || '--',
      driver: driver || 'Replay focus',
      gap: delta || '--',
      pit: ''
    },
    isReference,
    isAhead: direction === 'ahead',
    isBehind: direction === 'behind',
    carClassColorHex: carClassColorHex || null,
    isPlaceholder: false,
    relativeLapDelta
  };
}

function relativePlaceholderRow() {
  return {
    cells: ['', '', ''],
    valuesByKey: {},
    isReference: false,
    isAhead: false,
    isBehind: false,
    carClassColorHex: null,
    isPlaceholder: true,
    relativeLapDelta: null
  };
}

function relativeLapDelta(row, timingRow = null, referenceRow = null) {
  const value = row?.lapDeltaToReference ?? row?.relativeLapDelta;
  if (Number.isFinite(value)) return value;
  const carLap = completedLap(timingRow) ?? completedLap(row);
  const referenceLap = completedLap(referenceRow);
  return Number.isFinite(carLap) && Number.isFinite(referenceLap)
    ? carLap - referenceLap
    : null;
}

function completedLap(row) {
  if (Number.isFinite(row?.lapCompleted) && row.lapCompleted >= 0) return row.lapCompleted;
  if (Number.isFinite(row?.progressLaps) && row.progressLaps >= 0) return Math.floor(row.progressLaps);
  return null;
}

function hasRelativeDisplayEvidence(row) {
  return Number.isFinite(row?.relativeSeconds)
    || Number.isFinite(row?.relativeMeters)
    || Number.isFinite(row?.relativeLaps);
}

function isRelativeAhead(row) {
  if (row?.isAhead === true) return true;
  if (row?.isBehind === true) return false;
  return Number.isFinite(row?.relativeSeconds) && row.relativeSeconds < 0;
}

function isRelativeBehind(row) {
  if (row?.isBehind === true) return true;
  if (row?.isAhead === true) return false;
  return Number.isFinite(row?.relativeSeconds) && row.relativeSeconds > 0;
}

function relativeSortKey(row) {
  if (Number.isFinite(row?.relativeSeconds)) return Math.abs(row.relativeSeconds);
  if (Number.isFinite(row?.relativeMeters)) return Math.abs(row.relativeMeters);
  if (Number.isFinite(row?.relativeLaps)) return Math.abs(row.relativeLaps);
  return Number.MAX_VALUE;
}

function relativeDelta(row, direction) {
  const sign = direction === 'ahead' ? '-' : '+';
  if (Number.isFinite(row?.relativeSeconds)) {
    return `${sign}${Math.abs(row.relativeSeconds).toFixed(3)}`;
  }
  if (Number.isFinite(row?.relativeMeters)) {
    return `${sign}${Math.abs(row.relativeMeters).toFixed(0)}m`;
  }
  return '--';
}

function positionLabel(row, fallbackRow = null) {
  const classPosition = Number(row?.classPosition ?? fallbackRow?.classPosition);
  if (Number.isFinite(classPosition) && classPosition > 0) {
    return `${classPosition}`;
  }

  const overallPosition = Number(row?.overallPosition ?? fallbackRow?.overallPosition);
  return Number.isFinite(overallPosition) && overallPosition > 0 ? `${overallPosition}` : '--';
}

function captureGapToLeaderModel(models, frame, index, searchParams = null) {
  const gapSettings = gapSettingsModel();
  const currentGap = focusedClassLeaderGap(models);
  const graph = captureGapGraph(index, searchParams);
  const hasGraphData = graph?.series?.some((series) => Array.isArray(series?.points) && series.points.length > 0) === true;
  const status = currentGap?.hasData || hasGraphData ? 'live | race gap' : 'waiting for timing';
  return {
    overlayId: 'gap-to-leader',
    title: 'Gap To Leader',
    status,
    source: sourceFromSettings(gapSettings, currentGap?.hasData
      ? `source: live gap telemetry | cars ${currentGap.classCarCount}`
      : hasGraphData
        ? `source: live gap telemetry | cars ${graph.selectedSeriesCount}`
      : 'source: waiting'),
    bodyKind: 'graph',
    columns: [],
    rows: [],
    metrics: [],
    points: gapTrendPoints(index),
    graph,
    headerItems: captureHeaderItems(models, status, gapSettings)
  };
}

function captureGapGraph(index, searchParams = null) {
  const context = gapGraphContext(liveModelsForReplayFrame(index, searchParams));
  if (!context) {
    return null;
  }

  const selectedCars = selectGapGraphCars(context);
  const selectedIds = new Set(selectedCars.map((car) => car.carIdx));
  if (selectedIds.size === 0) {
    return null;
  }

  const frameContexts = gapGraphFrameContexts(index, context.focusClass, searchParams);

  if (frameContexts.length === 0) {
    return null;
  }

  const startSeconds = frameContexts[0].context.axisSeconds;
  const rawEndSeconds = frameContexts[frameContexts.length - 1].context.axisSeconds;
  const lapReferenceSeconds = context.lapReferenceSeconds;
  const minimumWindow = Math.max(120, isValidLapReference(lapReferenceSeconds) ? lapReferenceSeconds * 1.5 : 0);
  const rightPadding = Math.max(20, isValidLapReference(lapReferenceSeconds) ? lapReferenceSeconds * 0.15 : 0);
  const endSeconds = Math.max(rawEndSeconds, startSeconds + Math.max(minimumWindow, rawEndSeconds - startSeconds + rightPadding));
  const weather = gapWeatherPoints(frameContexts, startSeconds, endSeconds);
  const series = [...selectedIds].map((carIdx) => {
    let previousAxis = null;
    let previousContextPosition = null;
    const points = [];
    let latestRow = null;
    frameContexts.forEach((item, contextPosition) => {
      const row = item.context.rows.find((candidate) => candidate?.carIdx === carIdx);
      if (!row) {
        return;
      }

      const gapValue = gapValueForGraphRow(row, item.context.leader, item.context.lapReferenceSeconds);
      const gapSeconds = gapValue?.seconds;
      if (!Number.isFinite(gapSeconds)) {
        return;
      }

      const axisSeconds = item.context.axisSeconds;
      const sourceGraphDeltaSeconds = previousAxis === null ? null : axisSeconds - previousAxis;
      const segmentReason = gapSegmentReason(
        frameContexts,
        previousContextPosition,
        contextPosition,
        sourceGraphDeltaSeconds,
        points.length > 0 ? points[points.length - 1]?.gapSeconds : null,
        gapSeconds,
        points.length > 0 ? points[points.length - 1]?.gapSource : null,
        gapValue?.source,
        item.context.lapReferenceSeconds);
      points.push({
        timestampUtc: item.frame?.live?.lastUpdatedAtUtc || new Date().toISOString(),
        axisSeconds,
        gapSeconds,
        gapSource: gapValue?.source || null,
        carIdx,
        sourceReplayIndex: item.index,
        sourceFrameIndex: item.frame?.frameIndex ?? null,
        sourceSessionDeltaSeconds: finiteNumber(item.frame?.sourceSessionDeltaSeconds),
        sourceGraphDeltaSeconds: finiteNumber(sourceGraphDeltaSeconds),
        isReference: row.carIdx === item.context.focusCarIdx,
        isClassLeader: isClassLeaderRow(row, item.context.leader),
        classPosition: toPositiveInteger(row.classPosition),
        startsSegment: segmentReason !== 'continuous',
        segmentReason
      });
      previousAxis = axisSeconds;
      previousContextPosition = contextPosition;
      latestRow = row;
    });

    const selected = selectedCars.find((car) => car.carIdx === carIdx);
    return {
      carIdx,
      isReference: selected?.isReference === true || latestRow?.carIdx === context.focusCarIdx,
      isClassLeader: selected?.isClassLeader === true || isClassLeaderRow(latestRow, context.leader),
      classPosition: toPositiveInteger(selected?.classPosition ?? latestRow?.classPosition),
      alpha: 1,
      isStickyExit: false,
      isStale: false,
      points
    };
  }).filter((item) => item.points.length > 0);

  if (series.reduce((total, item) => total + item.points.length, 0) < 2) {
    return null;
  }

  const leaderChanges = gapLeaderChangeMarkers(frameContexts, startSeconds, endSeconds);
  const scale = selectGapGraphScale(series, startSeconds, endSeconds, lapReferenceSeconds);
  const trendMetrics = demoGapTrendMetrics(series, lapReferenceSeconds);
  const activeThreat = trendMetrics.find((metric) => metric?.chaser) || null;
  return {
    series,
    weather,
    leaderChanges,
    driverChanges: [],
    startSeconds,
    endSeconds,
    maxGapSeconds: scale.maxGapSeconds,
    lapReferenceSeconds,
    selectedSeriesCount: series.length,
    trendMetrics,
    activeThreat,
    threatCarIdx: activeThreat?.chaser?.carIdx ?? null,
    metricDeadbandSeconds: Math.max(0.25, isValidLapReference(lapReferenceSeconds) ? lapReferenceSeconds * 0.0025 : 0),
    comparisonLabel: gapComparisonLabel(context),
    sourceCadence: graphSourceCadence(frameContexts),
    scale
  };
}

function demoGapTrendMetrics(series, lapReferenceSeconds) {
  const candidates = [...series]
    .filter((item) => !item?.isClassLeader)
    .sort((a, b) => (a.classPosition ?? Number.MAX_SAFE_INTEGER) - (b.classPosition ?? Number.MAX_SAFE_INTEGER) || a.carIdx - b.carIdx);
  const threat = candidates[1] || candidates[0] || series.find((item) => !item?.isClassLeader) || null;
  const chaser = threat
    ? { carIdx: threat.carIdx, label: Number.isFinite(threat.classPosition) ? `P${threat.classPosition}` : `#${threat.carIdx}`, gainSeconds: 1.4 }
    : null;
  const lap = Math.max(1, Math.round(chartLapReferenceSeconds(lapReferenceSeconds) / 10));
  return [
    {
      label: '5L',
      focusGapChangeSeconds: -0.8,
      chaser,
      state: 'ready',
      stateLabel: null
    },
    {
      label: '10L',
      focusGapChangeSeconds: 1.2,
      chaser: chaser ? { ...chaser, gainSeconds: 2.1 } : null,
      state: 'ready',
      stateLabel: null
    },
    {
      label: 'Pit',
      focusGapChangeSeconds: null,
      chaser: null,
      state: 'pit',
      stateLabel: null,
      primaryPit: { seconds: 42, lap, isActive: false },
      threatPit: chaser ? { seconds: 39, lap: lap + 1, isActive: false } : null,
      comparisonPit: { seconds: 44, lap, isActive: false }
    },
    {
      label: 'PLap',
      focusGapChangeSeconds: null,
      chaser: null,
      state: 'pitLap',
      stateLabel: null,
      primaryPit: { seconds: 42, lap, isActive: false },
      threatPit: chaser ? { seconds: 39, lap: lap + 1, isActive: false } : null,
      comparisonPit: { seconds: 44, lap, isActive: false }
    },
    {
      label: 'Stint',
      focusGapChangeSeconds: null,
      chaser: null,
      state: 'stint',
      stateLabel: null,
      primaryText: '5L',
      threatText: '6L',
      comparisonText: '5L'
    },
    {
      label: 'Tire',
      focusGapChangeSeconds: null,
      chaser: null,
      state: 'tire',
      stateLabel: null,
      primaryTire: { label: 'Hard', shortLabel: 'H', isWet: false },
      threatTire: { label: 'Wet', shortLabel: 'W', isWet: true },
      comparisonTire: { label: 'Hard', shortLabel: 'H', isWet: false }
    },
    {
      label: 'Last',
      focusGapChangeSeconds: null,
      chaser: null,
      state: 'last',
      stateLabel: null,
      primaryText: '1:31.842',
      threatText: '1:30.913',
      comparisonText: '1:32.104'
    },
    {
      label: 'Status',
      focusGapChangeSeconds: null,
      chaser: null,
      state: 'status',
      stateLabel: null,
      primaryText: 'Track',
      threatText: 'Track',
      comparisonText: 'Pit'
    }
  ];
}

function gapComparisonLabel(context) {
  if (!Number.isFinite(context?.focusGapSeconds)) {
    return inferredGapComparisonLabel(context);
  }

  const candidates = context.rows
    .map((row) => {
      const gapValue = gapValueForGraphRow(row, context.leader, context.lapReferenceSeconds);
      const gapSeconds = gapValue?.seconds;
      return {
        row,
        gapSeconds,
        classPosition: toPositiveInteger(row?.classPosition)
      };
    })
    .filter((item) =>
      item.row
      && Number.isFinite(item.row.carIdx)
      && item.row.carIdx !== context.focusCarIdx
      && Number.isFinite(item.gapSeconds));

  const ahead = candidates
    .filter((item) => item.gapSeconds < context.focusGapSeconds - 0.001)
    .sort((a, b) => (context.focusGapSeconds - a.gapSeconds) - (context.focusGapSeconds - b.gapSeconds)
      || (a.classPosition ?? Number.MAX_SAFE_INTEGER) - (b.classPosition ?? Number.MAX_SAFE_INTEGER))[0];
  const comparison = ahead || candidates
    .filter((item) => item.gapSeconds > context.focusGapSeconds + 0.001)
    .sort((a, b) => (a.gapSeconds - context.focusGapSeconds) - (b.gapSeconds - context.focusGapSeconds)
      || (a.classPosition ?? Number.MAX_SAFE_INTEGER) - (b.classPosition ?? Number.MAX_SAFE_INTEGER))[0];

  return Number.isFinite(comparison?.classPosition) && comparison.classPosition > 0
    ? `P${comparison.classPosition}`
    : inferredGapComparisonLabel(context);
}

function inferredGapComparisonLabel(context) {
  const classPositions = (Array.isArray(context?.rows) ? context.rows : [])
    .map((row) => toPositiveInteger(row?.classPosition))
    .filter((position) => Number.isFinite(position) && position > 0)
    .sort((a, b) => a - b);
  if (classPositions.length === 0) {
    return '--';
  }

  const focusPosition = toPositiveInteger(context?.focus?.classPosition);
  if (focusPosition > 1) {
    return `P${focusPosition - 1}`;
  }

  if (focusPosition === 1) {
    const behind = classPositions.find((position) => position > 1);
    return behind ? `P${behind}` : '--';
  }

  const firstNonLeader = classPositions.find((position) => position > 1);
  return firstNonLeader ? `P${firstNonLeader}` : `P${classPositions[0]}`;
}

function gapGraphFrameContexts(index, focusClass, searchParams = null) {
  const currentElapsed = finiteNumber(replay.frames[index]?.sourceElapsedSeconds);
  const startFrame = Number.isFinite(currentElapsed) && replayTiming.isTimelineMonotonic
    ? firstFrameIndexAtOrAfterSourceElapsed(Math.max(0, currentElapsed - gapGraphTrendWindowSeconds), index)
    : Math.max(0, index - gapGraphMaxContexts + 1);
  const boundedStart = Math.max(0, Math.min(index, startFrame));
  const frameCount = index - boundedStart + 1;
  const step = Math.max(1, Math.ceil(frameCount / gapGraphMaxContexts));
  const selectedIndexes = [];
  for (let frameIndex = boundedStart; frameIndex <= index; frameIndex += step) {
    selectedIndexes.push(frameIndex);
  }
  if (selectedIndexes[selectedIndexes.length - 1] !== index) {
    selectedIndexes.push(index);
  }

  const frameContexts = [];
  for (const frameIndex of selectedIndexes) {
    const frame = replay.frames[frameIndex];
    const frameContext = gapGraphContext(liveModelsForReplayFrame(frameIndex, searchParams), focusClass);
    if (frameContext) {
      frameContexts.push({ frame, index: frameIndex, context: frameContext });
    }
  }
  return frameContexts;
}

function gapSegmentReason(
  frameContexts,
  previousContextPosition,
  contextPosition,
  sourceGraphDeltaSeconds,
  previousGapSeconds = null,
  gapSeconds = null,
  previousGapSource = null,
  gapSource = null,
  lapReferenceSeconds = null) {
  if (previousContextPosition === null || previousContextPosition === undefined) {
    return 'first-point';
  }

  if (String(previousGapSource || '') !== String(gapSource || '')
    && Number.isFinite(previousGapSeconds)
    && Number.isFinite(gapSeconds)
    && !acceptsGapTrendPoint(previousGapSeconds, gapSeconds, lapReferenceSeconds)) {
    return 'source-crossover';
  }

  if (!Number.isFinite(sourceGraphDeltaSeconds) || sourceGraphDeltaSeconds <= gapMissingSegmentThresholdSeconds) {
    return 'continuous';
  }

  return hasSourceCadenceGapBetween(frameContexts, previousContextPosition, contextPosition)
    ? 'source-cadence-gap'
    : 'data-unavailable';
}

function hasSourceCadenceGapBetween(frameContexts, previousContextPosition, contextPosition) {
  for (let index = previousContextPosition + 1; index <= contextPosition; index += 1) {
    const previousAxis = frameContexts[index - 1]?.context?.axisSeconds;
    const axis = frameContexts[index]?.context?.axisSeconds;
    if (Number.isFinite(previousAxis) && Number.isFinite(axis) && axis - previousAxis > gapMissingSegmentThresholdSeconds) {
      return true;
    }
  }
  return false;
}

function graphSourceCadence(frameContexts) {
  const deltas = [];
  for (let index = 1; index < frameContexts.length; index += 1) {
    const previousAxis = frameContexts[index - 1]?.context?.axisSeconds;
    const axis = frameContexts[index]?.context?.axisSeconds;
    if (Number.isFinite(previousAxis) && Number.isFinite(axis) && axis >= previousAxis) {
      deltas.push(axis - previousAxis);
    }
  }
  const summary = summarizeNumbers(deltas);
  return {
    basis: 'graph frame context axisSeconds',
    contextCount: frameContexts.length,
    gapToLeaderMissingSegmentThresholdSeconds: gapMissingSegmentThresholdSeconds,
    denseForGapToLeader: frameContexts.length < 2 || (deltas.length > 0 && Math.max(...deltas) <= gapMissingSegmentThresholdSeconds),
    sourceSessionDeltaSeconds: summary
  };
}

function gapGraphContext(models, preferredClass = null) {
  if (!isGreenRace(models)) {
    return null;
  }

  const timing = models?.timing || {};
  const focusCarIdx = models?.reference?.focusCarIdx ?? timing.focusCarIdx;
  const focus = timing.focusRow
    || timing.playerRow
    || (Array.isArray(timing.classRows) ? timing.classRows.find((row) => row?.carIdx === focusCarIdx) : null)
    || (Array.isArray(timing.overallRows) ? timing.overallRows.find((row) => row?.carIdx === focusCarIdx) : null);
  if (!focus) {
    return null;
  }

  const focusClass = preferredClass ?? focus.carClass;
  const classFocus = focusClass == null ? focus : { ...focus, carClass: focusClass };
  const rows = classTimingRows(timing, classFocus);
  if (rows.length === 0) {
    return null;
  }

  const leader = rows.find((row) => toPositiveInteger(row?.classPosition) === 1)
    || rows.find((row) => toPositiveInteger(row?.overallPosition) === 1)
    || rows[0];

  const lapReference = lapReferenceSeconds(rows);
  const axisSeconds = Number.isFinite(models?.session?.sessionTimeSeconds)
    ? models.session.sessionTimeSeconds
    : Number.isFinite(models?.latestSample?.sessionTime)
      ? models.latestSample.sessionTime
      : Date.now() / 1000;
  const focusGapSeconds = gapSecondsForGraphRow(focus, leader, lapReference);
  return {
    models,
    timing,
    focus,
    focusCarIdx: focus.carIdx,
    focusClass,
    rows,
    leader,
    lapReferenceSeconds: lapReference,
    focusGapSeconds,
    axisSeconds
  };
}

function selectGapGraphCars(context) {
  const selected = new Map();
  const add = (car) => {
    if (car && Number.isFinite(car.carIdx) && !selected.has(car.carIdx)) {
      selected.set(car.carIdx, car);
    }
  };
  const candidates = context.rows
    .map((row) => {
      const gapValue = gapValueForGraphRow(row, context.leader, context.lapReferenceSeconds);
      const gapSeconds = gapValue?.seconds;
      const deltaSeconds = Number.isFinite(gapSeconds) && Number.isFinite(context.focusGapSeconds)
        ? gapSeconds - context.focusGapSeconds
        : null;
      return {
        row,
        carIdx: row.carIdx,
        classPosition: toPositiveInteger(row.classPosition),
        gapSeconds,
        deltaSeconds,
        isReference: row.carIdx === context.focusCarIdx,
        isClassLeader: isClassLeaderRow(row, context.leader)
      };
    })
    .filter((car) => Number.isFinite(car.carIdx) && Number.isFinite(car.gapSeconds));

  const reference = candidates.find((car) => car.isReference);
  const referenceCanAnchor = reference && !isLappedGraphGap(reference.gapSeconds, context.lapReferenceSeconds);
  candidates.filter((car) => car.isClassLeader || (referenceCanAnchor && car.isReference)).forEach(add);
  if (!referenceCanAnchor) {
    candidates
      .filter((car) => !car.isClassLeader && !isLappedGraphGap(car.gapSeconds, context.lapReferenceSeconds))
      .sort((a, b) => a.gapSeconds - b.gapSeconds || (a.classPosition ?? Number.MAX_SAFE_INTEGER) - (b.classPosition ?? Number.MAX_SAFE_INTEGER))
      .slice(0, Math.max(1, gapCarsBehind))
      .forEach(add);

    if (selected.size <= 1) {
      candidates
        .sort((a, b) => a.gapSeconds - b.gapSeconds || (a.classPosition ?? Number.MAX_SAFE_INTEGER) - (b.classPosition ?? Number.MAX_SAFE_INTEGER))
        .slice(0, 6)
        .forEach(add);
    }

    return [...selected.values()].sort((a, b) => a.gapSeconds - b.gapSeconds || a.carIdx - b.carIdx);
  }

  candidates
    .filter((car) => !car.isClassLeader && !car.isReference && car.deltaSeconds < 0 && isSameLapGapCandidate(car, context))
    .sort((a, b) => b.deltaSeconds - a.deltaSeconds)
    .slice(0, gapCarsAhead)
    .forEach(add);
  candidates
    .filter((car) => !car.isClassLeader && !car.isReference && car.deltaSeconds > 0 && isSameLapGapCandidate(car, context))
    .sort((a, b) => a.deltaSeconds - b.deltaSeconds)
    .slice(0, gapCarsBehind)
    .forEach(add);

  if (selected.size <= 1) {
    candidates
      .sort((a, b) => a.gapSeconds - b.gapSeconds || (a.classPosition ?? Number.MAX_SAFE_INTEGER) - (b.classPosition ?? Number.MAX_SAFE_INTEGER))
      .slice(0, 6)
      .forEach(add);
  }

  return [...selected.values()].sort((a, b) => a.gapSeconds - b.gapSeconds || a.carIdx - b.carIdx);
}

function gapSecondsForGraphRow(row, leader, lapReferenceSeconds) {
  return gapValueForGraphRow(row, leader, lapReferenceSeconds)?.seconds ?? null;
}

function gapValueForGraphRow(row, leader, lapReferenceSeconds) {
  if (!row || !leader) {
    return null;
  }

  if (isPlaceholderPitGapRow(row)) {
    return null;
  }

  if (row.carIdx === leader.carIdx || toPositiveInteger(row.classPosition) === 1) {
    return { seconds: 0, source: 'class-leader-row' };
  }

  const gap = derivedClassGap(row, leader, lapReferenceSeconds);
  if (!gap) {
    return null;
  }

  if (Number.isFinite(gap.seconds)) {
    return { seconds: gap.seconds, source: gap.source || null };
  }

  return Number.isFinite(gap.laps)
    ? { seconds: gap.laps * chartLapReferenceSeconds(lapReferenceSeconds), source: gap.source || null }
    : null;
}

function isSameLapGapCandidate(car, context) {
  if (!Number.isFinite(car?.gapSeconds) || !Number.isFinite(context?.focusGapSeconds)) {
    return false;
  }

  const lapReference = chartLapReferenceSeconds(context.lapReferenceSeconds);
  return Math.abs((car.gapSeconds - context.focusGapSeconds) / lapReference) < 0.95;
}

function isLappedGraphGap(gapSeconds, lapReferenceSeconds) {
  return Number.isFinite(gapSeconds)
    && isValidLapReference(lapReferenceSeconds)
    && gapSeconds >= lapReferenceSeconds * 0.95;
}

function isClassLeaderRow(row, leader) {
  return Boolean(row) && (row.carIdx === leader?.carIdx || toPositiveInteger(row.classPosition) === 1);
}

function gapWeatherPoints(frameContexts, startSeconds, endSeconds) {
  const points = [];
  for (const item of frameContexts) {
    const condition = gapWeatherCondition(item.context.models);
    if (points.length > 0 && points[points.length - 1].condition === condition) {
      continue;
    }

    points.push({
      axisSeconds: Math.max(startSeconds, Math.min(endSeconds, item.context.axisSeconds)),
      condition
    });
  }
  return points;
}

function gapWeatherCondition(models) {
  const weather = models?.weather || {};
  if (weather.weatherDeclaredWet === true) return 'DeclaredWet';
  const wetness = Number(weather.trackWetness);
  if (!Number.isFinite(wetness)) return 'Unknown';
  if (wetness >= 4) return 'Wet';
  if (wetness >= 2) return 'Damp';
  return 'Dry';
}

function gapLeaderChangeMarkers(frameContexts, startSeconds, endSeconds) {
  const markers = [];
  let previousLeader = null;
  for (const item of frameContexts) {
    const leaderCarIdx = item.context.leader?.carIdx;
    if (!Number.isFinite(leaderCarIdx)) {
      continue;
    }

    if (previousLeader !== null && previousLeader !== leaderCarIdx) {
      markers.push({
        timestampUtc: item.frame?.live?.lastUpdatedAtUtc || new Date().toISOString(),
        axisSeconds: Math.max(startSeconds, Math.min(endSeconds, item.context.axisSeconds)),
        previousLeaderCarIdx: previousLeader,
        newLeaderCarIdx: leaderCarIdx
      });
    }

    previousLeader = leaderCarIdx;
  }
  return markers;
}

function selectGapGraphScale(series, startSeconds, endSeconds, lapReferenceSeconds) {
  const allPoints = series.flatMap((item) => item.points || [])
    .filter((point) => Number.isFinite(point.gapSeconds) && point.axisSeconds >= startSeconds && point.axisSeconds <= endSeconds);
  const leaderMax = niceCeiling(Math.max(1, ...allPoints.map((point) => Math.max(0, point.gapSeconds))));
  const referenceSeries = series.find((item) => item.isReference);
  const referencePoints = (referenceSeries?.points || [])
    .filter((point) => Number.isFinite(point.axisSeconds) && Number.isFinite(point.gapSeconds) && point.axisSeconds >= startSeconds && point.axisSeconds <= endSeconds)
    .sort((a, b) => a.axisSeconds - b.axisSeconds);
  if (referencePoints.length === 0) {
    return gapLeaderScale(leaderMax);
  }

  const latestReferenceGap = referenceGapAt(referencePoints, endSeconds);
  const triggerGap = Math.max(90, isValidLapReference(lapReferenceSeconds) ? lapReferenceSeconds * 0.5 : 0);
  if (latestReferenceGap < triggerGap) {
    return gapLeaderScale(leaderMax);
  }

  let maxAheadSeconds = 0;
  let maxBehindSeconds = 0;
  let hasLocalComparison = false;
  for (const item of series.filter((candidate) => !candidate.isClassLeader)) {
    for (const point of item.points || []) {
      if (!Number.isFinite(point.axisSeconds) || !Number.isFinite(point.gapSeconds) || point.axisSeconds < startSeconds || point.axisSeconds > endSeconds) {
        continue;
      }

      const delta = point.gapSeconds - referenceGapAt(referencePoints, point.axisSeconds);
      hasLocalComparison ||= !item.isReference && Math.abs(delta) > 0.001;
      if (delta < 0) {
        maxAheadSeconds = Math.max(maxAheadSeconds, Math.abs(delta));
      } else {
        maxBehindSeconds = Math.max(maxBehindSeconds, delta);
      }
    }
  }

  const minimumRange = Math.max(20, isValidLapReference(lapReferenceSeconds) ? lapReferenceSeconds * 0.1 : 0);
  const aheadSeconds = niceCeiling(Math.max(minimumRange, maxAheadSeconds * 1.18));
  const behindSeconds = niceCeiling(Math.max(minimumRange, maxBehindSeconds * 1.18));
  const localRange = Math.max(aheadSeconds, behindSeconds);
  const forceFocusScaleForLappedReference = isValidLapReference(lapReferenceSeconds)
    && latestReferenceGap >= lapReferenceSeconds * 0.95;
  if (!forceFocusScaleForLappedReference
    && (!hasLocalComparison || leaderMax < Math.max(triggerGap, localRange * 3))) {
    return gapLeaderScale(leaderMax);
  }

  return {
    maxGapSeconds: leaderMax,
    isFocusRelative: true,
    aheadSeconds,
    behindSeconds,
    referencePoints,
    latestReferenceGapSeconds: latestReferenceGap
  };
}

function gapLeaderScale(maxGapSeconds) {
  return {
    maxGapSeconds,
    isFocusRelative: false,
    aheadSeconds: 0,
    behindSeconds: 0,
    referencePoints: [],
    latestReferenceGapSeconds: 0
  };
}

function referenceGapAt(referencePoints, axisSeconds) {
  const points = (Array.isArray(referencePoints) ? referencePoints : [])
    .filter((point) => Number.isFinite(point?.axisSeconds) && Number.isFinite(point?.gapSeconds))
    .sort((a, b) => a.axisSeconds - b.axisSeconds);
  if (points.length === 0) {
    return 0;
  }

  if (axisSeconds <= points[0].axisSeconds) {
    return points[0].gapSeconds;
  }

  const last = points[points.length - 1];
  if (axisSeconds >= last.axisSeconds) {
    return last.gapSeconds;
  }

  const afterIndex = points.findIndex((point) => point.axisSeconds >= axisSeconds);
  const after = points[Math.max(0, afterIndex)];
  const before = points[Math.max(0, afterIndex - 1)];
  const span = after.axisSeconds - before.axisSeconds;
  if (span <= 0.001) {
    return before.gapSeconds;
  }

  const ratio = Math.max(0, Math.min(1, (axisSeconds - before.axisSeconds) / span));
  return before.gapSeconds + (after.gapSeconds - before.gapSeconds) * ratio;
}

function niceCeiling(value) {
  if (!Number.isFinite(value) || value <= 1) {
    return 1;
  }

  const magnitude = Math.pow(10, Math.floor(Math.log10(value)));
  const normalized = value / magnitude;
  for (const step of [1, 1.5, 2, 3, 5, 7.5, 10]) {
    if (normalized <= step) {
      return step * magnitude;
    }
  }

  return 10 * magnitude;
}

function gapTrendPoints(index) {
  const values = [];
  const start = Math.max(0, index - 23);
  for (let frameIndex = start; frameIndex <= index; frameIndex += 1) {
    const frame = replay.frames[frameIndex];
    const gap = focusedClassLeaderGap(frame?.live?.models || {});
    if (!gap?.hasData || !Number.isFinite(gap.trendSeconds) || gap.trendSeconds < 0) {
      continue;
    }

    if (values.length === 0 || acceptsGapTrendPoint(values[values.length - 1], gap.trendSeconds, gap.lapReferenceSeconds)) {
      values.push(gap.trendSeconds);
    }
  }
  return values;
}

function focusedClassLeaderGap(models) {
  if (!isGreenRace(models)) {
    return null;
  }

  const timing = models?.timing || {};
  const focusCarIdx = models?.reference?.focusCarIdx ?? timing.focusCarIdx;
  const focus = timing.focusRow
    || timing.playerRow
    || (Array.isArray(timing.classRows) ? timing.classRows.find((row) => row?.carIdx === focusCarIdx) : null)
    || (Array.isArray(timing.overallRows) ? timing.overallRows.find((row) => row?.carIdx === focusCarIdx) : null);
  if (!focus) {
    return null;
  }

  const rows = classTimingRows(timing, focus);
  const leader = rows.find((row) => toPositiveInteger(row?.classPosition) === 1)
    || rows.find((row) => toPositiveInteger(row?.overallPosition) === 1)
    || rows[0];
  const classPosition = toPositiveInteger(focus.classPosition);
  if (classPosition === 1 || leader?.carIdx === focus.carIdx) {
    return {
      hasData: true,
      classPosition,
      seconds: 0,
      laps: 0,
      trendSeconds: 0,
      lapReferenceSeconds: lapReferenceSeconds(rows),
      classCarCount: rows.length
    };
  }

  if (!leader) {
    return null;
  }

  const lapReference = lapReferenceSeconds(rows);
  const gap = derivedClassGap(focus, leader, lapReference);
  if (!gap) {
    return null;
  }

  return {
    hasData: true,
    classPosition,
    seconds: gap.seconds,
    laps: gap.laps,
    trendSeconds: Number.isFinite(gap.seconds) ? gap.seconds : (gap.laps || 0) * chartLapReferenceSeconds(lapReference),
    lapReferenceSeconds: lapReference,
    classCarCount: rows.length
  };
}

function classTimingRows(timing, focus) {
  const rows = Array.isArray(timing?.classRows) && timing.classRows.length > 0
    ? timing.classRows
    : Array.isArray(timing?.overallRows)
      ? timing.overallRows
      : [];
  const focusClass = focus?.carClass;
  return rows
    .filter((row) => row && (focusClass == null || row.carClass == null || row.carClass === focusClass))
    .sort((a, b) =>
      (toPositiveInteger(a.classPosition) ?? Number.MAX_SAFE_INTEGER) - (toPositiveInteger(b.classPosition) ?? Number.MAX_SAFE_INTEGER)
      || (toPositiveInteger(a.overallPosition) ?? Number.MAX_SAFE_INTEGER) - (toPositiveInteger(b.overallPosition) ?? Number.MAX_SAFE_INTEGER)
      || (toPositiveInteger(a.carIdx) ?? Number.MAX_SAFE_INTEGER) - (toPositiveInteger(b.carIdx) ?? Number.MAX_SAFE_INTEGER));
}

function derivedClassGap(row, leader, lapReferenceSeconds) {
  const lapGap = wholeLapGap(leader, row);
  if (Number.isFinite(lapGap) && lapGap > 0) {
    return { seconds: null, laps: lapGap, source: 'CarIdxLapCompleted' };
  }

  const projected = estimatedSecondsBehind(row, leader, lapReferenceSeconds);
  if (Number.isFinite(projected)) {
    return { seconds: projected, laps: null, source: 'CarIdxEstTime+CarIdxLapDistPct' };
  }

  const startGap = estimatedGreenStartSecondsBehind(row, leader, lapReferenceSeconds);
  if (Number.isFinite(startGap)) {
    return { seconds: startGap, laps: null, source: 'CarIdxEstTime+CarIdxPosition' };
  }

  const rowF2 = usableF2ForRace(row);
  const leaderF2 = usableF2ForRace(leader);
  if (Number.isFinite(rowF2) && Number.isFinite(leaderF2) && rowF2 >= leaderF2) {
    return { seconds: rowF2 - leaderF2, laps: null, source: 'CarIdxF2Time' };
  }

  return null;
}

function estimatedGreenStartSecondsBehind(row, leader, lapReferenceSeconds) {
  if (!isRaceLaunchEstimatedRow(row) || !isRaceLaunchEstimatedRow(leader) || !isPositionBehind(row, leader)) {
    return null;
  }

  const rowEstimated = validTimingSeconds(row?.estimatedTimeSeconds);
  const leaderEstimated = validTimingSeconds(leader?.estimatedTimeSeconds);
  if (!Number.isFinite(rowEstimated) || !Number.isFinite(leaderEstimated)) {
    return null;
  }

  const seconds = leaderEstimated - rowEstimated;
  const maximumSeconds = Math.min(30, Math.max(5, chartLapReferenceSeconds(lapReferenceSeconds) * 0.25));
  return Number.isFinite(seconds) && seconds >= 0 && seconds <= maximumSeconds ? seconds : null;
}

function isRaceLaunchEstimatedRow(row) {
  if (!row || row.hasTakenGrid !== true || validLapDistPct(row.lapDistPct) !== null) {
    return false;
  }

  return toPositiveInteger(row.classPosition) === 1 || isRaceF2Placeholder(row);
}

function isPositionBehind(row, leader) {
  const rowClassPosition = toPositiveInteger(row?.classPosition);
  const leaderClassPosition = toPositiveInteger(leader?.classPosition);
  if (rowClassPosition && leaderClassPosition) {
    return rowClassPosition > leaderClassPosition;
  }

  const rowOverallPosition = toPositiveInteger(row?.overallPosition);
  const leaderOverallPosition = toPositiveInteger(leader?.overallPosition);
  return rowOverallPosition && leaderOverallPosition
    ? rowOverallPosition > leaderOverallPosition
    : false;
}

function wholeLapGap(leader, row) {
  const leaderProgress = progressLaps(leader);
  const rowProgress = progressLaps(row);
  if (Number.isFinite(leaderProgress) && Number.isFinite(rowProgress)) {
    const progressGap = leaderProgress - rowProgress;
    if (progressGap < 0.95) {
      return null;
    }

    const roundedGap = Math.round(progressGap);
    return roundedGap >= 1 && Math.abs(progressGap - roundedGap) <= 0.35
      ? roundedGap
      : progressGap;
  }

  const leaderLap = Number.isInteger(leader?.lapCompleted) ? leader.lapCompleted : null;
  const rowLap = Number.isInteger(row?.lapCompleted) ? row.lapCompleted : null;
  if (leaderLap == null || rowLap == null) {
    return null;
  }

  const laps = leaderLap - rowLap;
  return laps > 0 ? laps : null;
}

function estimatedSecondsBehind(row, leader, lapReferenceSeconds) {
  const rowEstimated = validTimingSeconds(row?.estimatedTimeSeconds);
  const leaderEstimated = validTimingSeconds(leader?.estimatedTimeSeconds);
  const rowLapDist = validLapDistPct(row?.lapDistPct);
  const leaderLapDist = validLapDistPct(leader?.lapDistPct);
  if (!Number.isFinite(rowEstimated)
    || !Number.isFinite(leaderEstimated)
    || !Number.isFinite(rowLapDist)
    || !Number.isFinite(leaderLapDist)) {
    return null;
  }

  let relativeLaps = rowLapDist - leaderLapDist;
  if (relativeLaps > 0.5) {
    relativeLaps -= 1;
  } else if (relativeLaps < -0.5) {
    relativeLaps += 1;
  }

  let seconds = rowEstimated - leaderEstimated;
  if (Number.isFinite(lapReferenceSeconds)) {
    if (seconds > lapReferenceSeconds / 2) {
      seconds -= lapReferenceSeconds;
    } else if (seconds < -lapReferenceSeconds / 2) {
      seconds += lapReferenceSeconds;
    }
  }

  if (!Number.isFinite(seconds) || seconds > 0) {
    return null;
  }

  const timingSign = Math.sign(seconds);
  const lapSign = Math.sign(relativeLaps);
  if (timingSign !== 0 && lapSign !== 0 && timingSign !== lapSign) {
    return null;
  }

  if (Number.isFinite(lapReferenceSeconds)) {
    const lapBasedSeconds = Math.abs(relativeLaps * lapReferenceSeconds);
    const maximumDelta = Math.max(5, Math.min(lapReferenceSeconds / 2, lapBasedSeconds + 10));
    if (Math.abs(seconds) > maximumDelta) {
      return null;
    }
  } else if (Math.abs(seconds) > 60) {
    return null;
  }

  return Math.max(0, -seconds);
}

function progressLaps(row) {
  const lap = Number.isInteger(row?.lapCompleted) && row.lapCompleted >= 0 ? row.lapCompleted : null;
  const pct = validLapDistPct(row?.lapDistPct);
  return lap !== null && Number.isFinite(pct) ? lap + pct : null;
}

function usableF2ForRace(row) {
  const f2 = validNonNegative(row?.f2TimeSeconds);
  if (!Number.isFinite(f2)) {
    return null;
  }

  const overallPosition = toPositiveInteger(row?.overallPosition);
  const classPosition = toPositiveInteger(row?.classPosition);
  if (f2 === 0) {
    return overallPosition === 1 || classPosition === 1 ? 0 : null;
  }

  if (classPosition !== 1 && f2 < 0.1) {
    return null;
  }

  if (isRaceF2Placeholder(row)) {
    return null;
  }

  return f2;
}

function isRaceF2Placeholder(row) {
  const f2 = validNonNegative(row?.f2TimeSeconds);
  const overallPosition = toPositiveInteger(row?.overallPosition);
  return Number.isFinite(f2)
    && overallPosition
    && overallPosition > 1
    && Math.abs(f2 - ((overallPosition - 1) / 1000)) <= 0.00002;
}

function isPlaceholderPitGapRow(row) {
  if (!row || row.hasTakenGrid === true) {
    return false;
  }

  if (row.onPitRoad !== true && !isKnownNonTrackSurface(row.trackSurface)) {
    return false;
  }

  return !hasUsableRaceTiming(row) && (!Number.isInteger(row.lapCompleted) || row.lapCompleted <= 0);
}

function hasUsableRaceTiming(row) {
  const f2 = validNonNegative(row?.f2TimeSeconds);
  return Number.isFinite(f2) && f2 >= 0.1 && !isRaceF2Placeholder(row);
}

function isKnownNonTrackSurface(trackSurface) {
  return trackSurface !== null && trackSurface !== undefined && trackSurface !== 3;
}

function lapReferenceSeconds(rows) {
  const samples = [];
  for (const row of rows) {
    for (const key of ['lastLapTimeSeconds', 'bestLapTimeSeconds']) {
      const seconds = validTimingSeconds(row?.[key]);
      if (Number.isFinite(seconds) && seconds >= 20 && seconds <= 300) {
        samples.push(seconds);
        break;
      }
    }
  }

  if (samples.length === 0) {
    return null;
  }

  samples.sort((a, b) => a - b);
  const middle = Math.floor(samples.length / 2);
  return samples.length % 2 === 0
    ? (samples[middle - 1] + samples[middle]) / 2
    : samples[middle];
}

function acceptsGapTrendPoint(previous, next, lapReferenceSeconds) {
  const maximumJump = Math.max(30, Math.min(180, chartLapReferenceSeconds(lapReferenceSeconds) * 0.5));
  return Math.abs(next - previous) <= maximumJump;
}

function isGreenRace(models) {
  const state = models?.session?.sessionState;
  if (!Number.isFinite(state) || state < 4) {
    return false;
  }

  const sessionText = String(models?.session?.sessionType || models?.session?.eventType || models?.session?.sessionName || '').toLowerCase();
  return !/\b(practice|qualifying|qualify|test)\b/.test(sessionText) || /\brace\b/.test(sessionText);
}

function validTimingSeconds(value) {
  return Number.isFinite(value) && value > 0.05 ? value : null;
}

function validNonNegative(value) {
  return Number.isFinite(value) && value >= 0 ? value : null;
}

function validLapDistPct(value) {
  return Number.isFinite(value) && value >= 0 ? Math.max(0, Math.min(1, value)) : null;
}

function chartLapReferenceSeconds(value) {
  return Number.isFinite(value) && value > 20 && value < 1800 ? value : 60;
}

function isValidLapReference(value) {
  return Number.isFinite(value) && value > 20 && value < 1800;
}

function toPositiveInteger(value) {
  return Number.isInteger(value) && value > 0 ? value : null;
}

function formatPosition(position) {
  return Number.isInteger(position) && position > 0 ? String(position) : '--';
}

function formatGapValue(gap) {
  if (!gap?.hasData) {
    return '--';
  }

  if (gap.seconds === 0 || gap.laps === 0) {
    return 'leader';
  }

  if (Number.isFinite(gap.seconds)) {
    return formatGapSeconds(gap.seconds);
  }

  return Number.isFinite(gap.laps)
    ? `+${gap.laps.toFixed(2)} lap`
    : '--';
}

function formatGapSeconds(seconds) {
  if (!Number.isFinite(seconds)) {
    return '--';
  }

  if (seconds >= 60) {
    const minutes = Math.floor(seconds / 60);
    return `+${minutes}:${String((seconds % 60).toFixed(1)).padStart(4, '0')}`;
  }

  return `+${seconds.toFixed(1)}s`;
}

function displayDriver(row, scoringRow = null, driverRow = null) {
  return row?.driverName
    || row?.teamName
    || scoringRow?.driverName
    || scoringRow?.teamName
    || driverRow?.driverName
    || driverRow?.teamName
    || `Car ${row?.carIdx ?? scoringRow?.carIdx ?? driverRow?.carIdx ?? '--'}`;
}

function formatSigned(value, digits = 1) {
  if (!Number.isFinite(value)) return '--';
  return `${value > 0 ? '+' : ''}${value.toFixed(digits)}`;
}

function joinAvailable(...values) {
  const parts = values
    .map((value) => String(value ?? '').trim())
    .filter((value) => value && value !== '--');
  return parts.length > 0 ? parts.join(' | ') : '--';
}

function firstText(...values) {
  return values.find((value) => typeof value === 'string' && value.trim().length > 0)?.trim() || null;
}

function formatLiters(value) {
  if (!Number.isFinite(value)) return '--';
  return isImperial()
    ? `${(value * 0.2641720524).toFixed(1)} gal`
    : `${value.toFixed(1)} L`;
}

function formatFuelPerLap(value) {
  if (!Number.isFinite(value)) return '--';
  return isImperial()
    ? `${(value * 0.2641720524).toFixed(1)} gal/lap`
    : `${value.toFixed(1)} L/lap`;
}

function formatPercent(value) {
  return Number.isFinite(value) ? `${Math.round(value * 100)}%` : '--';
}

function formatFuelBurn(value) {
  return Number.isFinite(value) ? `${value.toFixed(1)} kg/h` : '--';
}

function formatTemp(value) {
  if (!Number.isFinite(value)) return '--';
  return isImperial()
    ? `${Math.round(value * 9 / 5 + 32)} F`
    : `${Math.round(value)} C`;
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

function formatSpeed(value) {
  if (!Number.isFinite(value)) return '--';
  return isImperial()
    ? `${Math.round(value * 2.2369362921)} mph`
    : `${Math.round(value * 3.6)} km/h`;
}

function formatPressureBar(value) {
  if (!Number.isFinite(value)) return '--';
  return isImperial()
    ? `${Math.round(value * 14.5037738)} psi`
    : `${value.toFixed(1)} bar`;
}

function formatDurationCompact(seconds) {
  if (!Number.isFinite(seconds) || seconds < 0) return '--';
  const total = Math.floor(seconds);
  const hours = Math.floor(total / 3600);
  const minutes = Math.floor((total % 3600) / 60);
  const remainder = total % 60;
  return hours > 0
    ? `${hours}:${String(minutes).padStart(2, '0')}:${String(remainder).padStart(2, '0')}`
    : `${minutes}:${String(remainder).padStart(2, '0')}`;
}

function formatProgressLaps(value) {
  return Number.isFinite(value) ? `${value.toFixed(2)} laps` : '--';
}

function formatTrackLength(value) {
  return Number.isFinite(value) ? `${value.toFixed(2)} km` : null;
}

function trackWetnessLabel(value) {
  if (!Number.isFinite(value)) return '--';
  if (value === 0) return 'Unknown';
  if (value === 1) return 'Dry';
  if (value === 2) return 'Mostly Dry';
  if (value === 3) return 'Very Lightly Wet';
  if (value === 4) return 'Lightly Wet';
  if (value === 5) return 'Moderately Wet';
  if (value === 6) return 'Very Wet';
  if (value === 7) return 'Extremely Wet';
  return `Value ${value}`;
}

function titleCaseDisplay(value) {
  const text = String(value || '').trim();
  return text
    ? text.toLowerCase().replace(/\b\w/g, (match) => match.toUpperCase())
    : null;
}

function hasWetSurfaceSignal(weather) {
  return weather?.weatherDeclaredWet === true || (Number.isFinite(weather?.trackWetness) && weather.trackWetness > 1);
}

function formatSessionClock(session) {
  const parts = sessionClockParts(session);
  if (parts.elapsed === '--' && parts.remaining === '--' && parts.total === '--') return '--';
  return joinAvailable(
    parts.elapsed === '--' ? null : `${parts.elapsed} elapsed`,
    parts.remaining === '--' ? null : `${parts.remaining} ${parts.remainingLabel.toLowerCase()}`,
    parts.total === '--' ? null : `${parts.total} total`);
}

function sessionClockParts(session) {
  const elapsed = formatDurationCompact(session?.sessionTimeSeconds);
  const remain = formatDurationCompact(session?.sessionTimeRemainSeconds);
  const total = formatDurationCompact(session?.sessionTimeTotalSeconds);
  return {
    elapsed,
    remaining: remain,
    remainingLabel: isRacePreGreenSession(session) ? 'Countdown' : 'Left',
    total
  };
}

function isRacePreGreenSession(session) {
  const state = session?.sessionState;
  if (!Number.isFinite(state) || state < 1 || state > 3) return false;
  const text = `${session?.sessionType || ''} ${session?.sessionName || ''} ${session?.eventType || ''}`.toLowerCase();
  return text.includes('race');
}

function formatSessionLaps(session, raceProgress = {}, raceProjection = {}) {
  const laps = sessionLapParts(session, raceProgress, raceProjection);
  return `${laps.remaining || '--'} left | ${laps.total || '--'} total`;
}

function sessionLapParts(session, raceProgress = {}, raceProjection = {}) {
  const remain = formatRemainingLapCount(session?.sessionLapsRemain ?? session?.sessionLapsRemainEx)
    || formatEstimatedLapCount(raceProjection?.estimatedTeamLapsRemaining)
    || formatEstimatedLapCount(raceProgress?.raceLapsRemaining);
  const total = formatLapCount(session?.sessionLapsTotal)
    || formatEstimatedTotalLapCount(raceProjection?.estimatedFinishLap)
    || formatEstimatedRaceProgressTotalLaps(raceProgress)
    || formatLapCount(session?.raceLaps);
  return { remaining: remain, total };
}

function formatLapCount(value) {
  return Number.isFinite(value) && value > 0 && value <= 1000 ? String(Math.round(value)) : null;
}

function formatRemainingLapCount(value) {
  return Number.isFinite(value) && value >= 0 && value <= 1000 ? String(Math.round(value)) : null;
}

function formatEstimatedLapCount(value) {
  return Number.isFinite(value) && value >= 0 && value <= 1000 ? `${value.toFixed(value % 1 === 0 ? 0 : 1)} est` : null;
}

function formatEstimatedTotalLapCount(value) {
  return Number.isFinite(value) && value >= 0 && value <= 1000 ? `${Math.ceil(value)} est` : null;
}

function formatEstimatedRaceProgressTotalLaps(raceProgress = {}) {
  const remaining = raceProgress?.raceLapsRemaining;
  if (!Number.isFinite(remaining) || remaining < 0 || remaining > 1000) return null;
  const progress = [
    raceProgress?.overallLeaderProgressLaps,
    raceProgress?.classLeaderProgressLaps,
    raceProgress?.strategyCarProgressLaps
  ].find((value) => Number.isFinite(value) && value >= 0);
  return Number.isFinite(progress) ? `${Math.ceil(progress + remaining)} est` : null;
}

function pitTimeLaps(models) {
  const session = models.session || {};
  const timeRemaining = formatPitTimeRemaining(session);
  return joinAvailable(timeRemaining, pitCompactLaps(session, models.raceProgress, models.raceProjection));
}

function pitSessionTimeLapsSegmentedRow(models) {
  const session = models.session || {};
  const timeRemaining = formatPitTimeRemaining(session) || '--';
  const laps = pitCompactLaps(session, models.raceProgress, models.raceProjection) || '--';
  return ['Time / Laps', joinAvailable(timeRemaining, laps), 'normal', [
    pitSegment('Time', timeRemaining, 'normal', 'pit-service.session.time'),
    pitSegment('Laps', laps, 'normal', 'pit-service.session.laps')
  ]];
}

function pitCompactLaps(session, raceProgress = {}, raceProjection = {}) {
  const remain = formatRemainingLapCount(session?.sessionLapsRemain ?? session?.sessionLapsRemainEx)
    || formatEstimatedLapCount(raceProjection?.estimatedTeamLapsRemaining)
    || formatEstimatedLapCount(raceProgress?.raceLapsRemaining);
  const total = formatLapCount(session?.sessionLapsTotal)
    || formatEstimatedTotalLapCount(raceProjection?.estimatedFinishLap)
    || formatEstimatedRaceProgressTotalLaps(raceProgress)
    || formatLapCount(session?.raceLaps);
  if (remain && total) return `${remain}/${total} laps`;
  if (remain) return `${remain} laps`;
  if (total) return `${total} laps total`;
  return null;
}

function formatPitTimeRemaining(session) {
  const seconds = session?.sessionTimeRemainSeconds;
  if (!Number.isFinite(seconds) || seconds < 0) return null;
  const totalSeconds = Math.ceil(Math.max(0, seconds));
  if (isRacePreGreenSession(session)) {
    return `${String(Math.floor(totalSeconds / 60)).padStart(2, '0')}:${String(totalSeconds % 60).padStart(2, '0')}`;
  }
  const totalMinutes = Math.ceil(totalSeconds / 60);
  return `${String(Math.floor(totalMinutes / 60)).padStart(2, '0')}:${String(totalMinutes % 60).padStart(2, '0')}`;
}

function formatHeaderSessionTimeRemaining(session) {
  const seconds = session?.sessionTimeRemainSeconds;
  if (!Number.isFinite(seconds) || seconds < 0) return null;
  return formatDuration(seconds);
}

function formatWeatherTemps(weather) {
  const air = formatTemp(weather?.airTempC);
  const track = formatTemp(weather?.trackTempCrewC);
  return air === '--' && track === '--' ? '--' : `air ${air} | track ${track}`;
}

function formatWeatherSurface(weather) {
  const wetness = titleCaseDisplay(weather?.trackWetnessLabel) || trackWetnessLabel(weather?.trackWetness);
  return joinAvailable(wetness, weather?.weatherDeclaredWet === true ? 'Declared Wet' : null, weather?.rubberState ? `Rubber ${titleCaseDisplay(weather.rubberState)}` : null);
}

function formatWeatherSky(weather) {
  const precipitationPercent = formatWeatherPercent(weather?.precipitationPercent);
  const precipitation = precipitationPercent ? `rain:${precipitationPercent}` : null;
  return joinAvailable(titleCaseDisplay(weather?.skiesLabel), weather?.weatherType, precipitation);
}

function formatWindAtmosphere(weather, localWind = null) {
  const windSpeed = formatSpeed(weather?.windVelocityMetersPerSecond);
  const windDirection = cardinalDirection(weather?.windDirectionRadians);
  return windSpeed === '--' && !windDirection
    ? '--'
    : joinAvailable(windDirection, windSpeed === '--' ? null : windSpeed, localWind?.directionLabel);
}

function formatWeatherAtmosphere(weather) {
  const humidity = formatWeatherPercent(weather?.relativeHumidityPercent, 'hum');
  const fog = formatWeatherPercent(weather?.fogLevelPercent, 'fog');
  return joinAvailable(humidity, fog, formatAirPressure(weather?.airPressurePa));
}

function formatSessionSummary(session) {
  return joinAvailable(session?.sessionType, meaningfulSessionName(session), session?.teamRacing === true ? 'team' : null);
}

function meaningfulSessionName(session) {
  const name = String(session?.sessionName || '').trim();
  if (!name) return null;
  const type = String(session?.sessionType || '').trim();
  const event = String(session?.eventType || '').trim();
  return name.toLowerCase() === type.toLowerCase() || name.toLowerCase() === event.toLowerCase()
    ? null
    : name;
}

function formatSessionEvent(session) {
  return joinAvailable(session?.eventType, session?.carDisplayName);
}

function formatAirPressure(pascals) {
  if (!Number.isFinite(pascals) || pascals <= 0) return null;
  return isImperial()
    ? `${(pascals / 3386.389).toFixed(2)} inHg`
    : `${(pascals / 100).toFixed(0)} hPa`;
}

function normalizeWeatherPercent(value) {
  if (!Number.isFinite(value) || value < 0) return null;
  return Math.min(value <= 1 ? value * 100 : value, 100);
}

function formatWeatherPercent(value, label = null) {
  const normalized = normalizeWeatherPercent(value);
  if (!Number.isFinite(normalized)) return null;
  const formatted = `${normalized.toFixed(0)}%`;
  return label ? `${label} ${formatted}` : formatted;
}

function wetnessTone(weather) {
  return Number.isFinite(weather?.trackWetness) && weather.trackWetness >= 2 || weather?.weatherDeclaredWet === true
    ? 'info'
    : 'normal';
}

function declaredWetTone(weather) {
  return weather?.weatherDeclaredWet === true ? 'info' : 'normal';
}

function wetnessAccentHex(weather) {
  const value = weather?.trackWetness;
  if (!Number.isFinite(value)) {
    return weather?.weatherDeclaredWet === true ? '#33CEFF' : null;
  }

  if (value >= 7) return '#7A6BFF';
  if (value >= 5) return '#2F7DFF';
  if (value >= 3) return '#33CEFF';
  if (value >= 2) return '#8AD8FF';
  return weather?.weatherDeclaredWet === true ? '#33CEFF' : null;
}

function rainTone(percent) {
  const normalized = normalizeWeatherPercent(percent);
  return Number.isFinite(normalized) && normalized > 0 ? 'info' : 'normal';
}

function rainAccentHex(percent) {
  const normalized = normalizeWeatherPercent(percent);
  if (!Number.isFinite(normalized) || normalized <= 0) return null;
  if (normalized >= 70) return '#7A6BFF';
  if (normalized >= 40) return '#2F7DFF';
  if (normalized >= 15) return '#33CEFF';
  return '#8AD8FF';
}

function cardinalDirection(radians) {
  if (!Number.isFinite(radians)) return null;
  let degrees = radians * 180 / Math.PI;
  degrees %= 360;
  if (degrees < 0) degrees += 360;
  const directions = ['N', 'NE', 'E', 'SE', 'S', 'SW', 'W', 'NW'];
  return directions[Math.round(degrees / 45) % directions.length];
}

function formatLocalWind(models, weather) {
  if (!models || !isPlayerInCar(models)) return null;
  const reference = models.reference || {};
  const windSpeed = weather?.windVelocityMetersPerSecond;
  const windDirection = weather?.windDirectionRadians;
  const heading = reference.playerYawNorthRadians;
  if (!Number.isFinite(windSpeed)
    || windSpeed < 0
    || !Number.isFinite(windDirection)
    || !Number.isFinite(heading)) {
    return null;
  }

  const relativeRadians = normalizeSignedRadians(windDirection - heading);
  const headMetersPerSecond = windSpeed * Math.cos(relativeRadians);
  const crossMetersPerSecond = windSpeed * Math.sin(relativeRadians);
  const headTail = `${headMetersPerSecond >= 0 ? 'Head' : 'Tail'} ${formatSpeed(Math.abs(headMetersPerSecond))}`;
  const cross = Math.abs(crossMetersPerSecond) < 0.05
    ? '0'
    : `${crossMetersPerSecond >= 0 ? 'R' : 'L'} ${formatSpeed(Math.abs(crossMetersPerSecond))}`;
  return {
    value: joinAvailable(headTail, cross === '0' ? 'cross 0' : `cross ${cross}`),
    headTail,
    cross,
    directionLabel: relativeWindLabel(relativeRadians),
    relativeDegrees: relativeRadians * 180 / Math.PI
  };
}

function relativeWindLabel(relativeRadians) {
  const degrees = Math.abs(relativeRadians * 180 / Math.PI);
  if (degrees <= 22.5) return 'Head';
  if (degrees >= 157.5) return 'Tail';
  const side = relativeRadians >= 0 ? 'R' : 'L';
  if (degrees <= 67.5) return `Head ${side}`;
  return degrees >= 112.5 ? `Tail ${side}` : side;
}

function normalizeSignedRadians(radians) {
  let normalized = radians % (Math.PI * 2);
  if (normalized > Math.PI) normalized -= Math.PI * 2;
  if (normalized < -Math.PI) normalized += Math.PI * 2;
  return normalized;
}

function pitServiceStatusText(status) {
  switch (status) {
    case null:
    case undefined:
      return '--';
    case 0:
      return 'none';
    case 1:
      return 'in progress';
    case 2:
      return 'complete';
    case 100:
      return 'too far left';
    case 101:
      return 'too far right';
    case 102:
      return 'too far forward';
    case 103:
      return 'too far back';
    case 104:
      return 'bad angle';
    case 105:
      return 'cannot repair';
    default:
      return Number.isInteger(status) ? `status ${status}` : '--';
  }
}

function pitReleaseState(pit) {
  if (Number.isInteger(pit?.pitServiceStatus) && pit.pitServiceStatus >= 100) {
    return { kind: 'hold', value: `RED - ${pitServiceStatusText(pit.pitServiceStatus)}`, tone: 'error' };
  }
  if (pit?.pitServiceStatus === 2) {
    return { kind: 'go', value: 'GREEN - go', tone: 'success' };
  }
  if (pit?.pitstopActive || pit?.pitServiceStatus === 1) {
    return { kind: 'hold', value: 'RED - service active', tone: 'error' };
  }
  if (hasRequiredRepair(pit)) {
    return { kind: 'hold', value: 'RED - repair active', tone: 'error' };
  }
  if (hasOptionalRepair(pit)) {
    return { kind: 'advisory', value: 'YELLOW - optional repair', tone: 'warning' };
  }
  if (pit?.playerCarInPitStall) {
    return {
      kind: 'go',
      value: pit?.pitServiceStatus == null ? 'GREEN - go (inferred)' : 'GREEN - go',
      tone: 'success'
    };
  }
  if (pit?.onPitRoad || pit?.teamOnPitRoad === true) {
    return { kind: 'pending', value: 'pit road', tone: 'info' };
  }
  if (hasRequestedService(pit)) {
    return { kind: 'pending', value: 'armed', tone: 'info' };
  }
  return { kind: 'pending', value: '--', tone: 'normal' };
}

function pitStatus(pit, release) {
  if (Number.isInteger(pit?.pitServiceStatus) && pit.pitServiceStatus >= 100) return 'pit stall error';
  if (release?.kind === 'go') return 'release ready';
  if (release?.kind === 'advisory') return 'optional repair';
  if (release?.kind === 'hold') return 'hold';
  if (pit?.playerCarInPitStall) return 'in pit stall';
  if (pit?.pitstopActive) return 'service active';
  if (pit?.onPitRoad || pit?.teamOnPitRoad === true) return 'on pit road';
  return hasRequestedService(pit) ? 'service requested' : 'pit ready';
}

function pitServiceActivityTone(pit, release) {
  if (Number.isInteger(pit?.pitServiceStatus) && pit.pitServiceStatus >= 100) return 'error';
  if (pit?.pitServiceStatus === 2 || release?.kind === 'go') return 'success';
  if (pit?.pitstopActive || pit?.pitServiceStatus === 1) return 'warning';
  if (hasRequiredRepair(pit)) return 'error';
  if (hasOptionalRepair(pit)) return 'warning';
  return hasRequestedService(pit) ? 'success' : 'normal';
}

function pitFuelRequest(pit) {
  const requested = Number.isInteger(pit?.pitServiceFlags) && (pit.pitServiceFlags & 0x10) !== 0
    || Number.isFinite(pit?.pitServiceFuelLiters) && pit.pitServiceFuelLiters > 0;
  const amount = Number.isFinite(pit?.pitServiceFuelLiters) && pit.pitServiceFuelLiters > 0
    ? formatLiters(pit.pitServiceFuelLiters)
    : null;
  const value = joinAvailable(requested ? 'requested' : null, amount);
  if (value !== '--') return value;
  return Number.isInteger(pit?.pitServiceFlags) ? 'none' : '--';
}

function pitTearoff(pit) {
  if (Number.isInteger(pit?.pitServiceFlags) && (pit.pitServiceFlags & 0x20) !== 0) {
    return 'requested';
  }
  return Number.isInteger(pit?.pitServiceFlags) ? 'none' : '--';
}

function tireServiceCount(flags) {
  return Number.isInteger(flags) ? countBits(flags & 0x0f) : 0;
}

function countBits(value) {
  let count = 0;
  let remaining = value;
  while (remaining > 0) {
    count += remaining & 1;
    remaining >>= 1;
  }
  return count;
}

function pitRepair(pit) {
  return joinAvailable(
    Number.isFinite(pit?.pitRepairLeftSeconds) && pit.pitRepairLeftSeconds > 0 ? `${pit.pitRepairLeftSeconds.toFixed(0)}s required` : null,
    Number.isFinite(pit?.pitOptRepairLeftSeconds) && pit.pitOptRepairLeftSeconds > 0 ? `${pit.pitOptRepairLeftSeconds.toFixed(0)}s optional` : null);
}

function pitRepairTone(pit) {
  if (hasRequiredRepair(pit)) return 'error';
  return hasOptionalRepair(pit) ? 'warning' : 'normal';
}

function pitFastRepair(pit) {
  const selected = Number.isInteger(pit?.pitServiceFlags) && (pit.pitServiceFlags & 0x40) !== 0 ? 'selected' : null;
  const available = Number.isInteger(pit?.fastRepairAvailable) && pit.fastRepairAvailable >= 0
    ? `available ${pit.fastRepairAvailable}`
    : null;
  return joinAvailable(selected, available);
}

function pitFuelRequestSegmentedRow(pit) {
  const requested = pitFuelRequested(pit);
  const selectedLiters = Number.isFinite(pit?.pitServiceFuelLiters) && pit.pitServiceFuelLiters > 0
    ? pit.pitServiceFuelLiters
    : null;
  const selected = Number.isFinite(selectedLiters) ? formatLiters(selectedLiters) : '--';
  return ['Fuel request', pitFuelRequest(pit), 'normal', [
    pitSegment('Requested', requested ? 'Yes' : 'No', requested ? 'success' : 'error', 'pit-service.service.fuel-requested'),
    pitSegment('Selected', selected, selected === '--' ? 'waiting' : 'info', 'pit-service.service.fuel-selected')
  ]];
}

function pitTearoffSegmentedRow(pit) {
  const requested = pitTearoffRequested(pit);
  return ['Tearoff', pitTearoff(pit), 'normal', [
    pitSegment('Requested', requested ? 'Yes' : 'No', requested ? 'success' : 'error', 'pit-service.service.tearoff-requested')
  ]];
}

function pitRepairSegmentedRow(pit) {
  const required = Number.isFinite(pit?.pitRepairLeftSeconds) && pit.pitRepairLeftSeconds > 0
    ? `${pit.pitRepairLeftSeconds.toFixed(0)}s`
    : '--';
  const optional = Number.isFinite(pit?.pitOptRepairLeftSeconds) && pit.pitOptRepairLeftSeconds > 0
    ? `${pit.pitOptRepairLeftSeconds.toFixed(0)}s`
    : '--';
  return ['Repair', pitRepair(pit), pitRepairTone(pit), [
    pitSegment('Required', required, required === '--' ? 'success' : 'error', 'pit-service.service.repair-required'),
    pitSegment('Optional', optional, optional === '--' ? 'success' : 'warning', 'pit-service.service.repair-optional')
  ]];
}

function pitFastRepairSegmentedRow(pit) {
  const selected = pitFastRepairRequested(pit);
  const available = Number.isInteger(pit?.fastRepairAvailable) ? String(pit.fastRepairAvailable) : '--';
  return ['Fast repair', pitFastRepair(pit), 'normal', [
    pitSegment('Selected', selected ? 'Yes' : 'No', selected ? 'success' : 'error', 'pit-service.service.fast-repair-selected'),
    pitSegment('Available', available, Number.isInteger(pit?.fastRepairAvailable) && pit.fastRepairAvailable > 0 ? 'success' : 'warning', 'pit-service.service.fast-repair-available')
  ]];
}

function pitSignalMetricRow(label, value, tone, key = null) {
  return {
    label,
    value: value || '--',
    tone: tone || 'normal',
    rowColorHex: pitSignalColorHex(tone),
    segments: [pitSegment(label, value || '--', tone || 'normal', key)]
  };
}

function pitSignalColorHex(tone) {
  switch (tone) {
    case 'error':
      return '#FF6274';
    case 'warning':
      return '#FFD15B';
    case 'success':
      return '#62FF9F';
    case 'info':
      return '#00E8FF';
    default:
      return null;
  }
}

function pitSegment(label, value, tone = 'normal', key = null) {
  return { label, value, tone, key };
}

function pitFuelRequested(pit) {
  return Boolean(
    Number.isInteger(pit?.pitServiceFlags) && (pit.pitServiceFlags & 0x10) !== 0
    || Number.isFinite(pit?.pitServiceFuelLiters) && pit.pitServiceFuelLiters > 0);
}

function pitTearoffRequested(pit) {
  return Number.isInteger(pit?.pitServiceFlags) && (pit.pitServiceFlags & 0x20) !== 0;
}

function pitFastRepairRequested(pit) {
  return Number.isInteger(pit?.pitServiceFlags) && (pit.pitServiceFlags & 0x40) !== 0;
}

function hasRequiredRepair(pit) {
  return Number.isFinite(pit?.pitRepairLeftSeconds) && pit.pitRepairLeftSeconds > 0;
}

function hasOptionalRepair(pit) {
  return Number.isFinite(pit?.pitOptRepairLeftSeconds) && pit.pitOptRepairLeftSeconds > 0;
}

function hasRequestedService(pit) {
  return (Number.isInteger(pit?.pitServiceFlags) && pit.pitServiceFlags !== 0)
    || (Number.isFinite(pit?.pitServiceFuelLiters) && pit.pitServiceFuelLiters > 0)
    || hasRequiredRepair(pit)
    || hasOptionalRepair(pit);
}

function formatGear(gear) {
  if (gear === -1) return 'R';
  if (gear === 0) return 'N';
  return Number.isInteger(gear) && gear > 0 ? String(gear) : '--';
}

function formatRpm(value) {
  return Number.isFinite(value) ? `${value.toFixed(0)} rpm` : '--';
}

function formatPedals(inputs) {
  if (inputs?.hasPedalInputs === false) return '--';
  return joinAvailable(
    `T ${formatPercent(inputs?.throttle)}`,
    `B ${formatPercent(inputs?.brake)}${inputs?.brakeAbsActive === true ? ' ABS' : ''}`,
    `C ${formatPercent(inputs?.clutch)}`);
}

function formatSteering(radians) {
  return Number.isFinite(radians) ? `${(radians * 180 / Math.PI).toFixed(0)} deg` : '--';
}

function formatWarnings(value) {
  if (!Number.isInteger(value)) return '--';
  return value === 0 ? 'none' : `0x${value.toString(16).toUpperCase()}`;
}

function formatVoltage(value) {
  return Number.isFinite(value) ? `${value.toFixed(1)} V` : '--';
}

function formatOilFuel(inputs) {
  const oilTemp = formatTemp(inputs?.oilTempC);
  const oilPressure = formatPressureBar(inputs?.oilPressureBar);
  const fuelPressure = formatPressureBar(inputs?.fuelPressureBar);
  return joinAvailable(
    oilTemp === '--' ? null : `oil ${oilTemp}`,
    oilPressure === '--' ? null : `oil ${oilPressure}`,
    fuelPressure === '--' ? null : `fuel ${fuelPressure}`);
}

function formatDuration(seconds, phase) {
  const totalSeconds = Math.ceil(Math.max(0, seconds));
  const hours = Math.floor(totalSeconds / 3600);
  const minutes = Math.floor((totalSeconds % 3600) / 60);
  const remainingSeconds = totalSeconds % 60;
  return `${String(hours).padStart(2, '0')}:${String(minutes).padStart(2, '0')}:${String(remainingSeconds).padStart(2, '0')}`;
}

function tableModel(overlayId, title, status, headerItems, rows, source = 'source: race-start replay') {
  return {
    overlayId,
    title,
    status,
    source,
    bodyKind: 'table',
    columns: [],
    rows: rows.map((cells, rowIndex) => ({
      cells,
      isClassHeader: false,
      isReference: rowIndex === 1,
      isPit: false,
      isPartial: false,
      isPendingGrid: false,
      carClassColorHex: null,
      headerTitle: null,
      headerDetail: null
    })),
    metrics: [],
    points: [],
    headerItems
  };
}

function relativeTableModel(status, headerItems, rows, source = 'source: race-start replay', columns = relativeColumns()) {
  return {
    overlayId: 'relative',
    title: 'Relative',
    status,
    source,
    bodyKind: 'table',
    columns,
    rows: rows.map((row) => ({
      cells: relativeCells(row, columns),
      isClassHeader: false,
      isReference: row.isReference === true,
      isPlaceholder: row.isPlaceholder === true,
      isAhead: row.isAhead === true,
      isBehind: row.isBehind === true,
      isPit: false,
      isPartial: false,
      isPendingGrid: false,
      carClassColorHex: row.carClassColorHex || null,
      headerTitle: null,
      headerDetail: null,
      relativeLapDelta: row.relativeLapDelta ?? null
    })),
    metrics: [],
    points: [],
    headerItems
  };
}

function relativeCells(row, columns) {
  if (row?.valuesByKey) {
    return columns.map((column) => row.valuesByKey[column.dataKey] ?? '');
  }

  return row?.cells || row || [];
}

function metricsModel(
  overlayId,
  title,
  status,
  headerItems,
  metrics,
  source = 'source: race-start replay',
  gridSections = [],
  metricSections = []) {
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
    metricSections: metricSections.map(([title, rows]) => ({
      title,
      rows: rows.map(metricModelRow)
    }))
  };
}

function flagsModel(flags, status, isWaiting = false) {
  const visibleFlags = Array.isArray(flags) ? flags : [];
  return {
    overlayId: 'flags',
    title: 'Flags',
    status,
    source: 'source: session flags telemetry',
    bodyKind: 'flags',
    columns: [],
    rows: [],
    metrics: [],
    points: [],
    headerItems: [],
    flags: {
      flags: visibleFlags,
      isWaiting
    },
    shouldRender: !isWaiting && visibleFlags.length > 0
  };
}

function flagItemsFromSession(sessionFlags, sessionState) {
  const value = Number.isInteger(sessionFlags) ? sessionFlags : 0;
  const items = [];
  if ((value & 0x00000010) !== 0) items.push(flagItem('red', 'critical', 'Red', null, 'error'));
  if ((value & 0x00100000) !== 0) items.push(flagItem('meatball', 'critical', 'Repair', null, 'error'));
  if ((value & 0x00010000) !== 0) items.push(flagItem('black', 'critical', 'Black', null, 'error'));
  if ((value & 0x00008000) !== 0 || (value & 0x00004000) !== 0) items.push(flagItem('caution', 'yellow', 'Caution', (value & 0x00008000) !== 0 ? 'waving' : null, 'warning'));
  else if ((value & 0x00000008) !== 0 || (value & 0x00000100) !== 0 || (value & 0x00000200) !== 0 || (value & 0x00000040) !== 0 || (value & 0x00002000) !== 0) items.push(flagItem('yellow', 'yellow', (value & 0x00000200) !== 0 ? 'One to green' : (value & 0x00000040) !== 0 ? 'Debris' : 'Yellow', (value & 0x00000100) !== 0 || (value & 0x00002000) !== 0 ? 'waving' : null, 'warning'));
  if ((value & 0x00000020) !== 0) items.push(flagItem('blue', 'blue', 'Blue', null, 'info'));
  if ((value & 0x00000001) !== 0 || sessionState === 5) items.push(flagItem('checkered', 'finish', 'Checkered', sessionState === 5 && (value & 0x00000001) === 0 ? 'session complete' : null, 'info'));
  if ((value & 0x00000002) !== 0 || (value & 0x00001000) !== 0 || (value & 0x00000800) !== 0 || (value & 0x00000080) !== 0) items.push(flagItem('white', 'finish', (value & 0x00000002) !== 0 ? 'White' : (value & 0x00001000) !== 0 ? 'Five to go' : (value & 0x00000800) !== 0 ? 'Ten to go' : 'Crossed', null, 'info'));
  if ((value & 0x00000400) !== 0 || (value & 0x20000000) !== 0 || (value & 0x40000000) !== 0 || (value & 0x80000000) !== 0) items.push(flagItem('green', 'green', (value & 0x80000000) !== 0 ? 'Start' : (value & 0x40000000) !== 0 ? 'Set' : (value & 0x20000000) !== 0 ? 'Ready' : 'Green', (value & 0x00000400) !== 0 ? 'held' : null, 'success'));
  return items;
}

function flagItem(kind, category, label, detail, tone) {
  return { kind, category, label, detail, tone };
}

function metricModelRow(row) {
  if (!Array.isArray(row)) {
    const modelRow = {
      label: row?.label || '',
      value: row?.value || '--',
      tone: row?.tone || 'normal'
    };
    if (Array.isArray(row?.segments) && row.segments.length > 0) {
      modelRow.segments = row.segments.map(metricModelSegment);
    }
    if (row?.variant) {
      modelRow.variant = row.variant;
    }
    if (row?.rowColorHex) {
      modelRow.rowColorHex = row.rowColorHex;
    }
    if (row?.carClassColorHex) {
      modelRow.carClassColorHex = row.carClassColorHex;
    }

    return modelRow;
  }

  const [label, value, tone, segments] = row;
  const modelRow = { label, value, tone };
  if (Array.isArray(segments) && segments.length > 0) {
    modelRow.segments = segments.map(metricModelSegment);
  }

  return modelRow;
}

function metricModelSegment(segment) {
  const model = {
    label: segment?.label || '',
    value: segment?.value || '--',
    tone: segment?.tone || 'normal'
  };
  if (segment?.accentHex) {
    model.accentHex = segment.accentHex;
  }
  if (Number.isFinite(segment?.rotationDegrees)) {
    model.rotationDegrees = segment.rotationDegrees;
  }
  return model;
}

function settings(overlayId, frame, searchParams = null) {
  if (overlayId === 'standings') {
    return {
      maximumRows: 14,
      classSeparatorsEnabled: true,
      otherClassRowsPerClass: 2,
      columns: replay.frames[0]?.model?.columns || []
    };
  }

  if (overlayId === 'relative') {
    return relativeSettings();
  }

  if (overlayId === 'gap-to-leader') {
    return gapSettingsModel();
  }

  if (overlayId === 'fuel-calculator') {
    return fuelSettingsModel();
  }

  if (overlayId === 'session-weather') {
    return sessionWeatherSettingsModel();
  }

  if (overlayId === 'pit-service') {
    return pitServiceSettingsModel();
  }

  if (overlayId === 'stream-chat') {
    return streamChatSettingsModel(frame, searchParams);
  }

  if (overlayId === 'garage-cover') {
    return {
      hasImage: false,
      imageVersion: null,
      fallbackReason: 'race_start_validation',
      previewVisible: true
    };
  }

  if (overlayId === 'track-map') {
    return {
      trackMap: null,
      trackMapSettings: {
        internalOpacity: 0,
        showSectorBoundaries: true
      }
    };
  }

  if (overlayId === 'input-state') {
    return { ...inputStateReviewSettings };
  }

  return {};
}

function relativeSettings() {
  return {
    carsAhead: relativeCarsEachSide,
    carsBehind: relativeCarsEachSide,
    showHeaderTimeRemaining: relativeShowHeaderTimeRemaining,
    columns: relativeColumns()
  };
}

function gapSettingsModel() {
  return {
    carsAhead: gapCarsAhead,
    carsBehind: gapCarsBehind,
    showHeaderTimeRemaining: gapShowHeaderTimeRemaining
  };
}

function fuelSettingsModel() {
  return {
    unitSystem: reviewUnitSystem,
    showHeaderTimeRemaining: fuelShowHeaderTimeRemaining
  };
}

function sessionWeatherSettingsModel() {
  return {
    unitSystem: reviewUnitSystem,
    showHeaderTimeRemaining: sessionWeatherShowHeaderTimeRemaining,
    disabledContent: sessionWeatherDisabledContent
  };
}

function pitServiceSettingsModel() {
  return {
    unitSystem: reviewUnitSystem,
    showHeaderTimeRemaining: pitServiceShowHeaderTimeRemaining,
    disabledContent: pitServiceDisabledContent
  };
}

function streamChatSettingsModel(frame, searchParams = null) {
  if (streamChatFixtureModeFor(searchParams) === 'all') {
    return streamChatFixtureSettingsModel();
  }

  if (streamChatProvider === 'local-twitch' || streamChatProvider === 'live-review') {
    return streamChatLocalTwitchSettingsModel(frame);
  }

  if (streamChatProvider === 'twitch') {
    return streamChatTwitchChannel
      ? streamChatLocalTwitchSettingsModel(frame)
      : streamChatUnconfigured('twitch', 'missing_or_invalid_twitch_channel');
  }

  if (streamChatProvider === 'streamlabs') {
    return streamChatStreamlabsUrl
      ? withStreamChatChrome({
          provider: 'streamlabs',
          isConfigured: true,
          streamlabsWidgetUrl: streamChatStreamlabsUrl,
          twitchChannel: null,
          status: 'configured_streamlabs'
        })
      : streamChatUnconfigured('streamlabs', 'missing_or_invalid_streamlabs_url');
  }

  if (streamChatProvider === 'none') {
    return streamChatUnconfigured('none', 'not_configured');
  }

  return streamChatReplaySettingsModel(frame);
}

function streamChatLocalTwitchSettingsModel(frame) {
  if (streamChatProvider === 'live-review' && shouldUseStreamChatReviewFallback()) {
    return streamChatReplaySettingsModel(frame, 'replay chat | live unavailable');
  }

  return withStreamChatChrome({
    provider: streamChatLiveSource.channel ? 'twitch' : 'none',
    isConfigured: Boolean(streamChatLiveSource.channel),
    streamlabsWidgetUrl: null,
    twitchChannel: streamChatLiveSource.channel,
    status: streamChatLiveSource.channel ? 'configured_twitch' : 'missing_or_invalid_twitch_channel',
    replayStatus: streamChatLiveSource.status,
    replaySource: streamChatLiveSource.source,
    replayRows: streamChatLiveSource.rows
  });
}

function streamChatReplaySettingsModel(frame, status = 'replay chat | spoofed') {
  return withStreamChatChrome({
    provider: 'none',
    isConfigured: false,
    streamlabsWidgetUrl: null,
    twitchChannel: null,
    status: 'replay_static',
    replayStatus: status,
    replaySource: 'source: spoofed stream replay',
    replayRows: streamChatReplayRows(frame)
  });
}

function streamChatFixtureSettingsModel() {
  return withStreamChatChrome({
    provider: 'twitch',
    isConfigured: true,
    streamlabsWidgetUrl: null,
    twitchChannel: streamChatTwitchChannel || 'techmatesracing',
    status: 'configured_twitch',
    contentOptions: streamChatFixtureContentOptions(),
    replayStatus: 'fixture chat | all twitch features',
    replaySource: 'source: spoofed twitch feature fixture',
    replayRows: streamChatFixtureRows()
  });
}

function streamChatFixtureModeFor(searchParams = null) {
  const queryMode = String(searchParams?.get?.('streamChatFixture') || '').trim().toLowerCase();
  return queryMode || streamChatFixtureMode;
}

function shouldUseStreamChatReviewFallback() {
  return !streamChatLiveSource.channel
    || (streamChatLiveSource.connectionState === 'failed' && !streamChatLiveSource.hasEverConnected);
}

function streamChatUnconfigured(provider, status) {
  return withStreamChatChrome({
    provider,
    isConfigured: false,
    streamlabsWidgetUrl: null,
    twitchChannel: null,
    status
  });
}

function withStreamChatChrome(settings) {
  return { ...settings };
}

function streamChatFixtureContentOptions() {
  return {
    showAuthorColor: true,
    showBadges: true,
    showBits: true,
    showFirstMessage: true,
    showReplies: true,
    showTimestamps: true,
    showEmotes: true,
    showAlerts: true,
    showMessageIds: true
  };
}

function streamChatReplayRows(frame) {
  const frameIndex = replay.frames.indexOf(frame);
  const position = replayProgress(frame, frameIndex >= 0 ? frameIndex : 0);
  const allRows = [
    { name: 'TMR', text: 'Stream replay fixture: spoofed chat source.', kind: 'system' },
    { name: 'race_control', text: 'Green flag. Settle in and hit your marks.', kind: 'message', source: 'twitch', authorColorHex: '#62C7FF', metadata: ['14:02'], badges: [{ id: 'moderator', version: '1', label: 'mod' }], segments: [{ kind: 'text', text: 'Green flag. Settle in and hit your marks.', imageUrl: null }] },
    { name: 'crew_chief', text: 'Fuel window is open if the caution falls our way.', kind: 'message', source: 'twitch', authorColorHex: '#FFD15B', metadata: ['reply @race control'], badges: [{ id: 'vip', version: '1', label: 'vip' }], segments: [{ kind: 'text', text: 'Fuel window is open if the caution falls our way.', imageUrl: null }] },
    { name: 'viewer42', text: 'That GT3 traffic through esses is getting spicy. Kappa', kind: 'message', source: 'twitch', authorColorHex: '#B65CFF', metadata: ['first'], badges: [], segments: [{ kind: 'text', text: 'That GT3 traffic through esses is getting spicy. ', imageUrl: null }, { kind: 'emote', text: 'Kappa', imageUrl: 'https://static-cdn.jtvnw.net/emoticons/v2/25/default/dark/1.0' }] },
    { name: 'pit_wall', text: 'Box call is ready. No tires unless pace drops.', kind: 'notice', source: 'twitch', authorColorHex: '#62FF9F', metadata: ['alert resub'], badges: [{ id: 'subscriber', version: '12', label: 'sub 12' }], segments: [{ kind: 'text', text: 'Box call is ready. No tires unless pace drops.', imageUrl: null }] },
    { name: 'mod_bot', text: 'Replay chat is local test data, not a live channel.', kind: 'system' },
    { name: 'spotter', text: 'Faster class approaching behind.', kind: 'message', source: 'twitch', authorColorHex: '#FF7D49', metadata: ['100 bits'], badges: [], segments: [{ kind: 'text', text: 'Faster class approaching behind.', imageUrl: null }] },
    { name: 'crew_chief', text: 'Nice save. Keep the brake temps under control.', kind: 'message', source: 'twitch', authorColorHex: '#FFD15B', metadata: [], badges: [{ id: 'vip', version: '1', label: 'vip' }], segments: [{ kind: 'text', text: 'Nice save. Keep the brake temps under control.', imageUrl: null }] },
    { name: 'viewer_amy', text: 'Map overlay looks clear with the smaller car dots.', kind: 'message', source: 'twitch', authorColorHex: '#00E8FF', metadata: [], badges: [{ id: 'subscriber', version: '6', label: 'sub 6' }], segments: [{ kind: 'text', text: 'Map overlay looks clear with the smaller car dots.', imageUrl: null }] },
    { name: 'TMR', text: 'Replay source disconnected from external chat services.', kind: 'system' }
  ];
  const visibleCount = Math.max(3, Math.min(allRows.length, Math.floor(position * allRows.length) + 3));
  return allRows.slice(0, visibleCount);
}

function streamChatFixtureRows() {
  const roomId = '105433958';
  const channel = streamChatTwitchChannel || 'techmatesracing';
  return [
    {
      name: 'DuraKitty',
      text: 'leader is 6.6k',
      kind: 'message',
      source: 'twitch',
      authorColorHex: '#0000FF',
      metadata: ['11:46'],
      badges: [
        { id: 'broadcaster', version: '1', label: 'broadcaster', roomId },
        { id: 'partner', version: '1', label: 'partner', roomId: null },
        { id: 'premium', version: '1', label: 'premium', roomId: null }
      ],
      segments: [{ kind: 'text', text: 'leader is 6.6k', imageUrl: null }],
      twitch: {
        transport: 'irc',
        command: 'PRIVMSG',
        channel,
        tags: {
          'badge-info': '',
          badges: 'broadcaster/1,partner/1,premium/1',
          color: '#0000FF',
          'display-name': 'DuraKitty',
          emotes: '',
          'first-msg': '0',
          flags: '',
          id: '8f4a92c1-9f8b-4f52-9e42-000000000001',
          mod: '0',
          'returning-chatter': '1',
          'room-id': roomId,
          subscriber: '0',
          'tmi-sent-ts': '1778762700000',
          turbo: '0',
          'user-id': '700000001',
          'user-type': '',
          vip: '0'
        },
        badgeInfo: [],
        roles: {
          broadcaster: true,
          moderator: false,
          subscriber: false,
          vip: false,
          turbo: false,
          returningChatter: true
        }
      }
    },
    {
      name: 'sandman417',
      text: "oh you're in hell, good luck and don't spend 1/3 of the race in the pits",
      kind: 'message',
      source: 'twitch',
      authorColorHex: '#66CC44',
      metadata: ['11:48'],
      badges: [
        { id: 'moderator', version: '1', label: 'mod', roomId },
        { id: 'premium', version: '1', label: 'premium', roomId: null }
      ],
      segments: [{ kind: 'text', text: "oh you're in hell, good luck and don't spend 1/3 of the race in the pits", imageUrl: null }],
      twitch: {
        transport: 'irc',
        command: 'PRIVMSG',
        channel,
        tags: {
          'badge-info': '',
          badges: 'moderator/1,premium/1',
          color: '#66CC44',
          'display-name': 'sandman417',
          emotes: '',
          'first-msg': '0',
          flags: '',
          id: '8f4a92c1-9f8b-4f52-9e42-000000000002',
          mod: '1',
          'returning-chatter': '1',
          'room-id': roomId,
          subscriber: '0',
          'tmi-sent-ts': '1778762880000',
          turbo: '0',
          'user-id': '700000002',
          'user-type': 'mod',
          vip: '0'
        },
        roles: {
          broadcaster: false,
          moderator: true,
          subscriber: false,
          vip: false,
          turbo: false,
          returningChatter: true
        }
      }
    },
    {
      name: 'new_viewer',
      text: 'First time here and this overlay is clean! PogChamp',
      kind: 'message',
      source: 'twitch',
      authorColorHex: '#FF7D49',
      metadata: ['first', 'id c0ffee42', '11:49'],
      badges: [],
      segments: [
        { kind: 'text', text: 'First time here and this overlay is clean! ', imageUrl: null },
        { kind: 'emote', text: 'PogChamp', imageUrl: 'https://static-cdn.jtvnw.net/emoticons/v2/305954156/default/dark/1.0' }
      ],
      twitch: {
        transport: 'irc',
        command: 'PRIVMSG',
        channel,
        tags: {
          'badge-info': '',
          badges: '',
          color: '#FF7D49',
          'display-name': 'new_viewer',
          emotes: '305954156:43-50',
          'first-msg': '1',
          flags: '',
          id: '8f4a92c1-9f8b-4f52-9e42-000000000003',
          mod: '0',
          'returning-chatter': '0',
          'room-id': roomId,
          subscriber: '0',
          'tmi-sent-ts': '1778762940000',
          turbo: '0',
          'user-id': '700000003',
          'user-type': '',
          vip: '0'
        },
        roles: {
          broadcaster: false,
          moderator: false,
          subscriber: false,
          vip: false,
          turbo: false,
          returningChatter: false
        },
        message: {
          emoteOnly: false,
          emotes: [
            { id: '305954156', token: 'PogChamp', start: 43, end: 50 }
          ]
        }
      }
    },
    {
      name: 'cheer_wall',
      text: '100 bits for surviving that stint Kappa',
      kind: 'message',
      source: 'twitch',
      authorColorHex: '#FFD15B',
      metadata: ['100 bits', '11:51'],
      badges: [{ id: 'bits', version: '100', label: 'bits', roomId: null }],
      segments: [
        { kind: 'text', text: '100 bits for surviving that stint ', imageUrl: null },
        { kind: 'emote', text: 'Kappa', imageUrl: 'https://static-cdn.jtvnw.net/emoticons/v2/25/default/dark/1.0' }
      ],
      twitch: {
        transport: 'irc',
        command: 'PRIVMSG',
        channel,
        tags: {
          'badge-info': 'bits/100',
          badges: 'bits/100',
          bits: '100',
          color: '#FFD15B',
          'display-name': 'cheer_wall',
          emotes: '25:34-38',
          'first-msg': '0',
          flags: '',
          id: '8f4a92c1-9f8b-4f52-9e42-000000000004',
          mod: '0',
          'returning-chatter': '1',
          'room-id': roomId,
          subscriber: '0',
          'tmi-sent-ts': '1778763060000',
          turbo: '0',
          'user-id': '700000004',
          'user-type': '',
          vip: '0'
        },
        badgeInfo: [{ id: 'bits', value: '100' }],
        message: {
          bits: 100,
          cheermotes: [{ prefix: 'cheer', bits: 100 }],
          emotes: [{ id: '25', token: 'Kappa', start: 34, end: 38 }]
        }
      }
    },
    {
      name: 'sub_event',
      text: 'sub_event subscribed for 12 months Keep pushing.',
      kind: 'notice',
      source: 'twitch',
      authorColorHex: '#B65CFF',
      metadata: ['alert resub', '11:52'],
      badges: [{ id: 'subscriber', version: '12', label: 'sub 12', roomId }],
      segments: [{ kind: 'text', text: 'sub_event subscribed for 12 months Keep pushing.', imageUrl: null }],
      twitch: {
        transport: 'irc',
        command: 'USERNOTICE',
        channel,
        tags: {
          'badge-info': 'subscriber/12',
          badges: 'subscriber/12',
          color: '#B65CFF',
          'display-name': 'sub_event',
          emotes: '',
          id: '8f4a92c1-9f8b-4f52-9e42-000000000005',
          login: 'sub_event',
          mod: '0',
          'msg-id': 'resub',
          'msg-param-cumulative-months': '12',
          'msg-param-streak-months': '3',
          'msg-param-should-share-streak': '1',
          'msg-param-sub-plan': '1000',
          'msg-param-sub-plan-name': 'Tier 1',
          'room-id': roomId,
          subscriber: '1',
          'system-msg': 'sub_event\\shas\\ssubscribed\\sfor\\s12\\smonths!',
          'tmi-sent-ts': '1778763120000',
          turbo: '0',
          'user-id': '700000005',
          'user-type': ''
        },
        badgeInfo: [{ id: 'subscriber', value: '12' }],
        notice: {
          type: 'resub',
          systemMessage: 'sub_event has subscribed for 12 months!',
          cumulativeMonths: 12,
          streakMonths: 3,
          shouldShareStreak: true,
          subPlan: '1000',
          subPlanName: 'Tier 1',
          userMessage: 'Keep pushing.'
        },
        eventSub: {
          subscriptionType: 'channel.chat.notification',
          noticeType: 'resub',
          systemMessage: 'sub_event has subscribed for 12 months!',
          resub: {
            cumulativeMonths: 12,
            streakMonths: 3,
            durationMonths: 1,
            subTier: '1000',
            isPrime: false,
            isGift: false,
            gifterIsAnonymous: null,
            gifterUserId: null,
            gifterUserName: null,
            gifterUserLogin: null
          }
        }
      }
    },
    {
      name: 'raid_alert',
      text: 'FastPitCrew is raiding with 24 viewers. Welcome in.',
      kind: 'notice',
      source: 'twitch',
      authorColorHex: '#9147FF',
      metadata: ['alert raid', '11:53'],
      badges: [
        { id: 'vip', version: '1', label: 'vip', roomId: null },
        { id: 'turbo', version: '1', label: 'turbo', roomId: null }
      ],
      segments: [{ kind: 'text', text: 'FastPitCrew is raiding with 24 viewers. Welcome in.', imageUrl: null }],
      twitch: {
        transport: 'irc',
        command: 'USERNOTICE',
        channel,
        tags: {
          'badge-info': '',
          badges: 'vip/1,turbo/1',
          color: '#9147FF',
          'display-name': 'raid_alert',
          emotes: '',
          id: '8f4a92c1-9f8b-4f52-9e42-000000000006',
          login: 'raid_alert',
          mod: '0',
          'msg-id': 'raid',
          'msg-param-displayName': 'FastPitCrew',
          'msg-param-login': 'fastpitcrew',
          'msg-param-profileImageURL': 'https://static-cdn.jtvnw.net/jtv_user_pictures/fastpitcrew-profile_image-70x70.png',
          'msg-param-viewerCount': '24',
          'room-id': roomId,
          subscriber: '0',
          'system-msg': '24\\sraiders\\sfrom\\sFastPitCrew\\shave\\sjoined!',
          'tmi-sent-ts': '1778763180000',
          turbo: '1',
          'user-id': '700000006',
          'user-type': ''
        },
        notice: {
          type: 'raid',
          systemMessage: '24 raiders from FastPitCrew have joined!',
          raiderDisplayName: 'FastPitCrew',
          raiderLogin: 'fastpitcrew',
          viewerCount: 24,
          profileImageUrl: 'https://static-cdn.jtvnw.net/jtv_user_pictures/fastpitcrew-profile_image-70x70.png'
        },
        eventSub: {
          subscriptionType: 'channel.chat.notification',
          noticeType: 'raid',
          systemMessage: '24 raiders from FastPitCrew have joined!',
          raid: {
            userId: '700090024',
            userName: 'FastPitCrew',
            userLogin: 'fastpitcrew',
            viewerCount: 24,
            profileImageUrl: 'https://static-cdn.jtvnw.net/jtv_user_pictures/fastpitcrew-profile_image-70x70.png'
          }
        }
      }
    },
    {
      name: 'long_viewer_name_here',
      text: 'this is a much longer Twitch chat message that should wrap onto multiple lines instead of clipping the lower half of the text or overflowing horizontally inside the stream chat row cell',
      kind: 'message',
      source: 'twitch',
      authorColorHex: '#00E8FF',
      metadata: ['reply @crew_chief', 'id 9272af30', '11:54'],
      badges: [
        { id: 'vip', version: '1', label: 'vip', roomId: null },
        { id: 'premium', version: '1', label: 'premium', roomId: null }
      ],
      segments: [{ kind: 'text', text: 'this is a much longer Twitch chat message that should wrap onto multiple lines instead of clipping the lower half of the text or overflowing horizontally inside the stream chat row cell', imageUrl: null }],
      twitch: {
        transport: 'irc',
        command: 'PRIVMSG',
        channel,
        tags: {
          'badge-info': '',
          badges: 'vip/1,premium/1',
          color: '#00E8FF',
          'display-name': 'long_viewer_name_here',
          emotes: '',
          'first-msg': '0',
          flags: '',
          id: '8f4a92c1-9f8b-4f52-9e42-000000000007',
          mod: '0',
          'reply-parent-msg-id': '8f4a92c1-9f8b-4f52-9e42-000000000004',
          'reply-parent-user-id': '700000004',
          'reply-parent-user-login': 'crew_chief',
          'reply-parent-display-name': 'crew_chief',
          'reply-parent-msg-body': 'Box this lap if traffic stays this bad.',
          'reply-thread-parent-msg-id': '8f4a92c1-9f8b-4f52-9e42-000000000004',
          'reply-thread-parent-user-login': 'crew_chief',
          'returning-chatter': '1',
          'room-id': roomId,
          subscriber: '0',
          'tmi-sent-ts': '1778763240000',
          turbo: '0',
          'user-id': '700000007',
          'user-type': '',
          vip: '1'
        },
        reply: {
          parentMessageId: '8f4a92c1-9f8b-4f52-9e42-000000000004',
          parentUserId: '700000004',
          parentUserLogin: 'crew_chief',
          parentDisplayName: 'crew_chief',
          parentMessageBody: 'Box this lap if traffic stays this bad.',
          threadParentMessageId: '8f4a92c1-9f8b-4f52-9e42-000000000004',
          threadParentUserLogin: 'crew_chief'
        },
        roles: {
          broadcaster: false,
          moderator: false,
          subscriber: false,
          vip: true,
          turbo: false,
          returningChatter: true
        }
      }
    }
  ];
}

function normalizeStreamChatProvider(value) {
  const normalized = String(value || '').trim().toLowerCase();
  return ['live-review', 'replay', 'none', 'streamlabs', 'twitch', 'local-twitch'].includes(normalized)
    ? normalized
    : 'live-review';
}

function normalizeTwitchChannel(channel) {
  const raw = String(channel || '').trim();
  const withoutUrl = raw.replace(/^https?:\/\/(www\.)?twitch\.tv\//i, '');
  const value = withoutUrl.trim().replace(/^@/, '').split('/').filter(Boolean)[0]?.toLowerCase() || '';
  return /^[a-z0-9_]{3,25}$/.test(value) ? value : null;
}

function normalizeStreamlabsUrl(url) {
  const value = String(url || '').trim();
  if (!value) return null;
  try {
    const parsed = new URL(value);
    return parsed.protocol === 'https:'
      && (parsed.hostname === 'streamlabs.com' || parsed.hostname.endsWith('.streamlabs.com'))
      && (parsed.pathname === '/widgets/chat-box' || parsed.pathname.startsWith('/widgets/chat-box/'))
      ? parsed.toString()
      : null;
  } catch {
    return null;
  }
}

function createStreamChatLiveSource() {
  return {
    channel: streamChatTwitchChannel,
    status: streamChatTwitchChannel ? 'connecting | twitch' : 'twitch not configured',
    source: streamChatTwitchChannel ? `source: live twitch chat #${streamChatTwitchChannel}` : 'source: live twitch chat unavailable',
    rows: [
      streamChatTwitchChannel
        ? { name: 'TMR', text: `Connecting to #${streamChatTwitchChannel}...`, kind: 'system' }
        : { name: 'TMR', text: 'Twitch channel is missing or invalid.', kind: 'error' }
    ],
    socket: null,
    reconnectTimer: null,
    connectedAnnounced: false,
    hasEverConnected: false,
    connectionState: streamChatTwitchChannel ? 'connecting' : 'unavailable'
  };
}

function startStreamChatLiveSource() {
  if (streamChatFixtureMode === 'all'
    || !['local-twitch', 'live-review', 'twitch'].includes(streamChatProvider)
    || !streamChatLiveSource.channel) {
    return;
  }

  connectStreamChatLiveSource();
}

function connectStreamChatLiveSource() {
  clearTimeout(streamChatLiveSource.reconnectTimer);
  const socket = new WebSocket('wss://irc-ws.chat.twitch.tv:443');
  streamChatLiveSource.socket = socket;
  streamChatLiveSource.status = 'connecting | twitch';
  streamChatLiveSource.connectedAnnounced = false;
  streamChatLiveSource.connectionState = 'connecting';
  socket.addEventListener('open', () => {
    const nick = `justinfan${Math.floor(10000 + Math.random() * 89999)}`;
    socket.send('CAP REQ :twitch.tv/tags twitch.tv/commands');
    socket.send('PASS SCHMOOPIIE');
    socket.send(`NICK ${nick}`);
    socket.send(`JOIN #${streamChatLiveSource.channel}`);
    streamChatLiveSource.status = 'joining | twitch';
    streamChatLiveSource.connectionState = 'joining';
  });
  socket.addEventListener('message', (event) => processStreamChatLivePayload(socket, String(event.data || '')));
  socket.addEventListener('error', () => {
    confirmStreamChatLiveRow('TMR', 'Twitch chat connection error.', 'error');
    streamChatLiveSource.status = 'chat reconnecting | twitch';
    streamChatLiveSource.connectionState = 'failed';
  });
  socket.addEventListener('close', () => {
    if (streamChatLiveSource.socket !== socket) {
      return;
    }

    if (!streamChatLiveSource.connectedAnnounced) {
      confirmStreamChatLiveRow('TMR', 'Twitch chat disconnected before joining.', 'error');
      streamChatLiveSource.connectionState = 'failed';
    } else {
      streamChatLiveSource.connectionState = 'reconnecting';
    }
    streamChatLiveSource.status = 'chat reconnecting | twitch';
    streamChatLiveSource.reconnectTimer = setTimeout(connectStreamChatLiveSource, 3500);
  });
}

function processStreamChatLivePayload(socket, payload) {
  for (const rawLine of payload.split('\r\n')) {
    const line = rawLine.trim();
    if (!line) continue;
    if (line.startsWith('PING')) {
      socket.send(`PONG ${line.slice(5)}`);
      continue;
    }
    if (line.includes(' NOTICE * :Login authentication failed')
      || line.includes(' NOTICE * :Improperly formatted auth')) {
      confirmStreamChatLiveRow('TMR', 'Twitch rejected the chat connection.', 'error');
      streamChatLiveSource.status = 'twitch auth rejected';
      streamChatLiveSource.connectionState = 'failed';
      socket.close();
      continue;
    }
    if (line.includes(' RECONNECT ')) {
      socket.close();
      continue;
    }
    if (isStreamChatLiveJoined(line)) {
      announceStreamChatLiveConnected();
      continue;
    }
    if (line.includes(' USERNOTICE ')) {
      const notice = parseStreamChatLiveUserNotice(line);
      if (notice) {
        pushStreamChatLiveRow(notice.name, notice.text, notice.kind, notice);
      }
      continue;
    }
    if (!line.includes(' PRIVMSG ')) {
      continue;
    }

    const message = parseStreamChatLivePrivMsg(line);
    if (message) {
      pushStreamChatLiveRow(message.name, message.text, message.kind, message);
    }
  }
}

function isStreamChatLiveJoined(line) {
  const channel = streamChatLiveSource.channel;
  return line.includes(' 001 ')
    || (channel && line.includes(` ROOMSTATE #${channel}`));
}

function announceStreamChatLiveConnected() {
  if (streamChatLiveSource.connectedAnnounced) {
    return;
  }

  streamChatLiveSource.connectedAnnounced = true;
  streamChatLiveSource.hasEverConnected = true;
  streamChatLiveSource.connectionState = 'connected';
  confirmStreamChatLiveRow('TMR', `Chat connected to #${streamChatLiveSource.channel}.`, 'system');
  streamChatLiveSource.status = 'chat connected | twitch';
}

function parseStreamChatLivePrivMsg(line) {
  const messageIndex = line.indexOf(' PRIVMSG ');
  const textIndex = line.indexOf(' :', messageIndex);
  if (messageIndex < 0 || textIndex < 0) {
    return null;
  }

  const prefixAndTags = line.slice(0, messageIndex);
  const tags = parseStreamChatLiveTags(prefixAndTags);
  const fallbackName = prefixAndTags.match(/:([^! ]+)/)?.[1] || 'chat';
  const text = decodeStreamChatLiveTag(line.slice(textIndex + 2));
  return {
    name: tags['display-name'] || fallbackName,
    text,
    kind: 'message',
    source: 'twitch',
    authorColorHex: validStreamChatColor(tags.color),
    metadata: streamChatLiveMetadata(tags, false),
    badges: streamChatLiveBadges(tags),
    segments: streamChatLiveSegments(tags, text)
  };
}

function parseStreamChatLiveUserNotice(line) {
  const noticeIndex = line.indexOf(' USERNOTICE ');
  if (noticeIndex < 0) {
    return null;
  }

  const prefixAndTags = line.slice(0, noticeIndex);
  const tags = parseStreamChatLiveTags(prefixAndTags);
  const fallbackName = prefixAndTags.match(/:([^! ]+)/)?.[1] || 'chat';
  const textIndex = line.indexOf(' :', noticeIndex);
  const userText = textIndex >= 0 ? decodeStreamChatLiveTag(line.slice(textIndex + 2)) : '';
  const systemText = tags['system-msg'] || '';
  const text = [systemText, userText].filter(Boolean).join(' ') || 'Twitch chat event.';
  const segments = userText
    ? [
        ...(systemText ? [{ kind: 'text', text: `${systemText} `, imageUrl: null }] : []),
        ...streamChatLiveSegments(tags, userText)
      ]
    : [{ kind: 'text', text, imageUrl: null }];
  return {
    name: tags['display-name'] || fallbackName,
    text,
    kind: 'notice',
    source: 'twitch',
    authorColorHex: validStreamChatColor(tags.color),
    metadata: streamChatLiveMetadata(tags, true),
    badges: streamChatLiveBadges(tags),
    segments
  };
}

function parseStreamChatLiveTags(prefixAndTags) {
  if (!prefixAndTags.startsWith('@')) {
    return {};
  }

  const tagText = prefixAndTags.slice(1, prefixAndTags.indexOf(' '));
  return Object.fromEntries(tagText.split(';').map((pair) => {
    const splitAt = pair.indexOf('=');
    if (splitAt < 0) return [pair, ''];
    return [pair.slice(0, splitAt), decodeStreamChatLiveTag(pair.slice(splitAt + 1))];
  }));
}

function decodeStreamChatLiveTag(value) {
  return String(value || '')
    .replace(/\\s/g, ' ')
    .replace(/\\:/g, ';')
    .replace(/\\\\/g, '\\');
}

function validStreamChatColor(value) {
  const color = String(value || '').trim();
  return /^#[0-9a-f]{6}$/i.test(color) ? color.toUpperCase() : null;
}

function streamChatLiveMetadata(tags, isNotice) {
  const parts = [];
  if (isNotice && tags['msg-id']) {
    parts.push(`alert ${compactStreamChatToken(tags['msg-id'])}`);
  }
  const bits = Number(tags.bits || 0);
  if (bits > 0) {
    parts.push(`${bits} bits`);
  }
  if (tags['first-msg'] === '1') {
    parts.push('first');
  }
  const replyTo = tags['reply-parent-display-name'] || tags['reply-parent-user-login'];
  if (replyTo) {
    parts.push(`reply @${compactStreamChatToken(replyTo)}`);
  }
  const timestamp = Number(tags['tmi-sent-ts'] || 0);
  if (timestamp > 0) {
    parts.push(new Date(timestamp).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', hour12: false }));
  }
  return parts;
}

function streamChatLiveBadges(tags) {
  const roomId = String(tags['room-id'] || '').trim();
  return String(tags.badges || '')
    .split(',')
    .filter(Boolean)
    .map((badge) => {
      const [id, version = ''] = badge.split('/');
      const label = id === 'moderator'
        ? 'mod'
        : id === 'subscriber'
          ? (version ? `sub ${version}` : 'sub')
          : compactStreamChatToken(id);
      return { id, version, label, roomId: roomId || null };
    })
    .filter((badge) => badge.id && badge.label);
}

function streamChatLiveSegments(tags, text) {
  const message = String(text || '');
  const emotes = [];
  for (const emoteGroup of String(tags.emotes || '').split('/').filter(Boolean)) {
    const splitAt = emoteGroup.indexOf(':');
    if (splitAt <= 0) continue;
    const id = emoteGroup.slice(0, splitAt);
    for (const range of emoteGroup.slice(splitAt + 1).split(',').filter(Boolean)) {
      const [startText, endText] = range.split('-');
      const start = Number(startText);
      const end = Number(endText);
      if (!Number.isInteger(start) || !Number.isInteger(end) || start < 0 || end < start || end >= message.length) {
        continue;
      }
      emotes.push({ id, start, end, token: message.slice(start, end + 1) });
    }
  }

  if (!emotes.length) {
    return [{ kind: 'text', text: message, imageUrl: null }];
  }

  const segments = [];
  let offset = 0;
  for (const emote of emotes.sort((a, b) => a.start - b.start || a.end - b.end)) {
    if (emote.start < offset) continue;
    if (emote.start > offset) {
      segments.push({ kind: 'text', text: message.slice(offset, emote.start), imageUrl: null });
    }
    segments.push({ kind: 'emote', text: emote.token, imageUrl: streamChatEmoteUrl(emote.id) });
    offset = emote.end + 1;
  }
  if (offset < message.length) {
    segments.push({ kind: 'text', text: message.slice(offset), imageUrl: null });
  }
  return segments.length ? segments : [{ kind: 'text', text: message, imageUrl: null }];
}

function streamChatEmoteUrl(id) {
  return `https://static-cdn.jtvnw.net/emoticons/v2/${encodeURIComponent(String(id || ''))}/default/dark/1.0`;
}

function compactStreamChatToken(value) {
  const compact = String(value || '').trim().replace(/-/g, ' ');
  return compact.length <= 18 ? compact : compact.slice(0, 18);
}

function pushStreamChatLiveRow(name, text, kind, metadata = null) {
  streamChatLiveSource.rows.push({
    name,
    text,
    kind,
    source: metadata?.source || '',
    authorColorHex: metadata?.authorColorHex || null,
    metadata: Array.isArray(metadata?.metadata) ? metadata.metadata : [],
    badges: Array.isArray(metadata?.badges) ? metadata.badges : [],
    segments: Array.isArray(metadata?.segments) ? metadata.segments : [{ kind: 'text', text, imageUrl: null }]
  });
  if (streamChatLiveSource.rows.length > 64) {
    streamChatLiveSource.rows.splice(0, streamChatLiveSource.rows.length - 64);
  }
}

function confirmStreamChatLiveRow(name, text, kind) {
  if (streamChatLiveSource.rows.length === 1 && streamChatLiveSource.rows[0].kind === 'system') {
    streamChatLiveSource.rows[0] = { name, text, kind };
    return;
  }

  pushStreamChatLiveRow(name, text, kind);
}

function relativeColumns() {
  return [
    { id: 'relative.position', label: 'Pos', dataKey: 'relative-position', width: 38, alignment: 'right' },
    { id: 'relative.driver', label: 'Driver', dataKey: 'driver', width: 250, alignment: 'left' },
    { id: 'relative.gap', label: 'Delta', dataKey: 'gap', width: 70, alignment: 'right' },
    ...(relativeShowPitColumn
      ? [{ id: 'relative.pit', label: 'Pit', dataKey: 'pit', width: 30, alignment: 'right' }]
      : [])
  ];
}

function sourceFromSettings(overlaySettings, source) {
  return source;
}

function spatialModel(lapProgress, relativeSeconds) {
  return {
    hasData: true,
    quality: 'inferred',
    referenceCarIdx: 0,
    referenceCarClassColorHex: '#FFDA59',
    referenceLapDistPct: lapProgress,
    hasCarLeft: relativeSeconds > -20 && relativeSeconds < 20,
    hasCarRight: relativeSeconds > 40 && relativeSeconds < 80,
    sideStatus: relativeSeconds > -20 && relativeSeconds < 20 ? 'left' : 'clear',
    strongestMulticlassApproach: {
      relativeSeconds: relativeSeconds < 0 ? null : -2.4
    },
    cars: [
      { carIdx: 21, relativeSeconds: -1.4, relativeMeters: -42, relativeLaps: -0.01, carClassColorHex: '#33CEFF' },
      { carIdx: 44, relativeSeconds: 2.1, relativeMeters: 58, relativeLaps: 0.014, carClassColorHex: '#FFAA00' },
      { carIdx: 63, relativeSeconds: 3.4, relativeMeters: 90, relativeLaps: 0.022, carClassColorHex: '#FF4FD8' }
    ]
  };
}

function relativeModel(relativeSeconds) {
  return {
    hasData: true,
    quality: relativeSeconds < 0 ? 'partial' : 'inferred',
    referenceCarIdx: 0,
    rows: [
      {
        carIdx: 21,
        driverName: 'Lap Ahead LMP2',
        carNumber: '21',
        carClass: 4099,
        carClassColorHex: '#33CEFF',
        overallPosition: 2,
        classPosition: 2,
        relativeSeconds: relativeSeconds < 0 ? null : 8.94,
        relativeLaps: 1.02,
        isAhead: true,
        isBehind: false
      },
      {
        carIdx: 22,
        driverName: 'Race Start Leader',
        carNumber: '22',
        carClass: 4098,
        carClassColorHex: '#FFDA59',
        overallPosition: 3,
        classPosition: 3,
        relativeSeconds: relativeSeconds < 0 ? null : 2.412,
        relativeLaps: 0.01,
        isAhead: true,
        isBehind: false
      },
      {
        carIdx: 44,
        driverName: 'Following GT3',
        carNumber: '44',
        carClass: 4098,
        carClassColorHex: '#FFDA59',
        overallPosition: 6,
        classPosition: 6,
        relativeSeconds: relativeSeconds < 0 ? null : 3.184,
        relativeLaps: 0.014,
        isAhead: false,
        isBehind: true
      },
      {
        carIdx: 63,
        driverName: 'Lap Behind GT4',
        carNumber: '63',
        carClass: 4100,
        carClassColorHex: '#FF4FD8',
        overallPosition: 18,
        classPosition: 12,
        relativeSeconds: relativeSeconds < 0 ? null : 11.72,
        relativeLaps: -1.03,
        isAhead: false,
        isBehind: true
      }
    ]
  };
}

function trackMapModel(lapProgress) {
  return {
    hasData: true,
    sectors: [
      { startPct: 0, endPct: 0.34, highlight: lapProgress < 0.34 ? 'personal-best' : 'none' },
      { startPct: 0.34, endPct: 0.67, highlight: lapProgress >= 0.34 && lapProgress < 0.67 ? 'best-lap' : 'none' },
      { startPct: 0.67, endPct: 1, highlight: 'none' }
    ]
  };
}

function reviewTrackMap() {
  return {
    racingLine: {
      closed: true,
      points: [
        { x: 0, y: 48, lapDistPct: 0 },
        { x: 65, y: 92, lapDistPct: 0.16 },
        { x: 150, y: 60, lapDistPct: 0.34 },
        { x: 210, y: -24, lapDistPct: 0.52 },
        { x: 148, y: -86, lapDistPct: 0.70 },
        { x: 45, y: -64, lapDistPct: 0.86 },
        { x: 0, y: 48, lapDistPct: 1 }
      ]
    },
    pitLane: {
      closed: false,
      points: [
        { x: 72, y: 84, lapDistPct: 0.14 },
        { x: 118, y: 118, lapDistPct: 0.22 },
        { x: 168, y: 82, lapDistPct: 0.30 }
      ]
    }
  };
}

function referenceDisplayRow(frame) {
  return (frame.model?.rows || []).find((row) => row && row.isReference === true && !row.isClassHeader);
}

function replayHeaderItems(frame, status, overlaySettings = null) {
  const items = [];
  const timeRemaining = headerTimeRemaining(frame)?.value;
  if (overlaySettings?.showHeaderTimeRemaining !== false && timeRemaining) {
    items.push({ key: 'timeRemaining', value: timeRemaining });
  }
  return items;
}

function headerTimeRemaining(frame) {
  const item = (frame.model?.headerItems || [])
    .find((candidate) => String(candidate?.key || '').toLowerCase() === 'timeremaining');
  if (!item?.value) return null;
  return {
    value: String(item.value),
    seconds: parseDurationSeconds(String(item.value))
  };
}

function parseDurationSeconds(value) {
  const parts = value.split(':').map((part) => Number.parseInt(part, 10));
  if ((parts.length !== 2 && parts.length !== 3) || parts.some((part) => !Number.isFinite(part))) {
    return null;
  }
  if (parts.length === 3) {
    return parts[0] * 3600 + parts[1] * 60 + parts[2];
  }
  return parts[0] * 60 + parts[1];
}

function normalizeProgress(value) {
  const normalized = value % 1;
  return normalized < 0 ? normalized + 1 : normalized;
}

function finiteNumber(value) {
  return Number.isFinite(value) ? value : null;
}

function roundNumber(value, digits = 3) {
  if (!Number.isFinite(value)) return null;
  const factor = 10 ** digits;
  const rounded = Math.round(value * factor) / factor;
  return Object.is(rounded, -0) ? 0 : rounded;
}

function positiveNumber(value, fallback) {
  const parsed = Number.parseFloat(String(value ?? ''));
  return Number.isFinite(parsed) && parsed > 0 ? parsed : fallback;
}

function optionalNumber(value) {
  const parsed = Number.parseFloat(String(value ?? ''));
  return Number.isFinite(parsed) ? parsed : null;
}

function optionalInteger(value) {
  const parsed = Number.parseInt(String(value ?? ''), 10);
  return Number.isInteger(parsed) ? parsed : null;
}

function optionalBoolean(value) {
  if (typeof value === 'boolean') {
    return value;
  }

  if (typeof value === 'number') {
    return value === 1 ? true : value === 0 ? false : null;
  }

  if (typeof value !== 'string') {
    return null;
  }

  const normalized = value.trim().toLowerCase();
  if (['1', 'true', 'yes', 'on'].includes(normalized)) {
    return true;
  }

  if (['0', 'false', 'no', 'off'].includes(normalized)) {
    return false;
  }

  return null;
}

function normalizeReplayTimingMode(value) {
  const normalized = String(value || '').trim().toLowerCase();
  if (['source', 'source-elapsed', 'elapsed'].includes(normalized)) {
    return 'source-elapsed';
  }
  if (['fixed', 'fixed-frame', 'frame', 'compressed'].includes(normalized)) {
    return 'fixed-frame';
  }
  return 'source-elapsed';
}

function formatCadenceSummary(cadence) {
  const delta = cadence?.sourceSessionDeltaSeconds || {};
  const max = Number.isFinite(delta.max) ? `${delta.max}s max` : 'max unknown';
  const median = Number.isFinite(delta.median) ? `${delta.median}s median` : 'median unknown';
  const dense = cadence?.denseForGapToLeader === true ? 'dense' : 'sparse';
  return `${dense} (${median}, ${max}, threshold ${gapMissingSegmentThresholdSeconds}s)`;
}

function clamp(value, min, max) {
  return Math.max(min, Math.min(max, value));
}

function clampInteger(value, fallback, min, max) {
  const parsed = Number.parseInt(String(value ?? ''), 10);
  return Math.max(min, Math.min(max, Number.isFinite(parsed) ? parsed : fallback));
}

function parseBoolean(value, fallback) {
  if (value == null || value === '') {
    return fallback;
  }

  const normalized = String(value).trim().toLowerCase();
  if (['1', 'true', 'yes', 'on'].includes(normalized)) {
    return true;
  }

  if (['0', 'false', 'no', 'off'].includes(normalized)) {
    return false;
  }

  return fallback;
}

function csvSet(value) {
  return new Set(String(value || '')
    .split(',')
    .map((item) => item.trim())
    .filter(Boolean));
}

function normalizeUnitSystem(value) {
  return String(value || '').trim().toLowerCase() === 'imperial'
    ? 'Imperial'
    : 'Metric';
}

function isImperial() {
  return reviewUnitSystem === 'Imperial';
}

function overlayIdFromPath(path) {
  const reviewPrefix = '/review/overlays/';
  if (path.startsWith(reviewPrefix)) {
    return decodeURIComponent(path.slice(reviewPrefix.length));
  }

  const overlayPrefix = '/overlays/';
  if (path.startsWith(overlayPrefix)) {
    return decodeURIComponent(path.slice(overlayPrefix.length));
  }

  return null;
}

function normalizePath(path) {
  return path.length > 1 && path.endsWith('/') ? path.slice(0, -1) : path;
}

function serveJson(response, payload) {
  const body = JSON.stringify(payload);
  response.writeHead(200, {
    'content-type': 'application/json; charset=utf-8',
    'cache-control': 'no-store',
    'access-control-allow-origin': '*'
  });
  response.end(body);
}

function serveHtml(response, html) {
  response.writeHead(200, {
    'content-type': 'text/html; charset=utf-8',
    'cache-control': 'no-store'
  });
  response.end(html);
}

function serveBinary(response, contentType, body) {
  response.writeHead(200, {
    'content-type': contentType,
    'cache-control': 'no-store'
  });
  response.end(body);
}

function serveText(response, status, text) {
  response.writeHead(status, {
    'content-type': 'text/plain; charset=utf-8',
    'cache-control': 'no-store'
  });
  response.end(text);
}
