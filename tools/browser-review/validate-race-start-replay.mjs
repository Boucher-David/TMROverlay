import { existsSync, mkdirSync, readFileSync, statSync, writeFileSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { chromium } from '@playwright/test';
import { browserOverlayPages } from '../../tests/browser-overlays/browserOverlayAssets.js';

const { baseUrl, outputDir, relativeSeconds, requireCaptureLive } = parseArguments(process.argv.slice(2));
mkdirSync(outputDir, { recursive: true });

const browser = await chromium.launch();
const context = await browser.newContext({
  viewport: { width: 900, height: 600 },
  deviceScaleFactor: 1
});

const results = [];
try {
  for (const frameSeconds of relativeSeconds) {
    for (const overlay of browserOverlayPages()) {
      results.push(await validateOverlayFrame(context, overlay, frameSeconds, requireCaptureLive));
    }
  }
} finally {
  await browser.close();
}

const reportPath = join(outputDir, 'race-start-overlay-validation.json');
writeFileSync(reportPath, `${JSON.stringify({
  baseUrl,
  relativeSeconds,
  requireCaptureLive,
  generatedAtUtc: new Date().toISOString(),
  screenshotArtifacts: summarizeScreenshotArtifacts(results),
  results
}, null, 2)}\n`);

const failures = results.flatMap((result) => result.failures.map((failure) => `${result.overlayId}@${result.relativeSeconds}s: ${failure}`));
const validScreenshotCount = results.filter((result) => result.screenshotArtifact?.isValid).length;
console.log(`Validated ${results.length} overlay frames and ${validScreenshotCount} screenshots from ${baseUrl}`);
console.log(`Screenshots/report: ${outputDir}`);
if (failures.length > 0) {
  console.error(failures.join('\n'));
  process.exitCode = 1;
}

async function validateOverlayFrame(context, overlay, frameSeconds, requireCaptureLive) {
  const page = await context.newPage();
  const consoleErrors = [];
  const pageErrors = [];
  const failedRequests = [];
  page.on('console', (message) => {
    if (message.type() === 'error') {
      consoleErrors.push(message.text());
    }
  });
  page.on('pageerror', (error) => pageErrors.push(error.message));
  page.on('requestfailed', (request) => failedRequests.push(`${request.method()} ${request.url()} ${request.failure()?.errorText || ''}`));

  const url = overlayUrl(overlay, frameSeconds);
  try {
    await page.goto(url, { waitUntil: 'domcontentloaded' });
    await page.waitForSelector('.overlay', { timeout: 5000 });
    await page.waitForTimeout(800);

    const modelResponse = overlay.modelRoute
      ? await page.evaluate(async (modelRoute) => {
        const response = await fetch(modelRoute);
        return response.ok ? response.json() : { error: `${response.status} ${response.statusText}` };
      }, overlay.modelRoute)
      : null;
    const snapshotResponse = await page.evaluate(async () => {
      const response = await fetch('/api/snapshot');
      return response.ok ? response.json() : { error: `${response.status} ${response.statusText}` };
    });
    const replayStatusResponse = await page.evaluate(async () => {
      const response = await fetch('/api/replay/status');
      return response.ok ? response.json() : { error: `${response.status} ${response.statusText}` };
    });
    const screenshotPath = join(outputDir, screenshotName(overlay.page.id, frameSeconds));
    await page.screenshot({ path: screenshotPath, fullPage: true });
    const screenshotArtifact = inspectScreenshotArtifact(screenshotPath);

    const metrics = await page.evaluate(() => {
      const overlay = document.querySelector('.overlay');
      const content = document.querySelector('#content');
      const status = document.querySelector('#status');
      const timer = document.querySelector('#time-remaining');
      const box = overlay?.getBoundingClientRect();
      return {
        overlayWidth: box?.width ?? 0,
        overlayHeight: box?.height ?? 0,
        scrollWidth: document.documentElement.scrollWidth,
        scrollHeight: document.documentElement.scrollHeight,
        viewportWidth: window.innerWidth,
        viewportHeight: window.innerHeight,
        bodyText: document.body.innerText.trim(),
        contentText: content?.innerText.trim() ?? '',
        statusText: status?.textContent?.trim() ?? '',
        timeRemainingText: timer && !timer.hidden ? timer.textContent.trim() : '',
        tableRows: document.querySelectorAll('tbody tr').length,
        metricCount: document.querySelectorAll('.metric').length,
        canvasCount: document.querySelectorAll('canvas').length,
        svgCount: document.querySelectorAll('svg').length,
        hasRadar: Boolean(document.querySelector('.radar-v2')),
        radarChromeVisible: radarChromeVisible(),
        hasTrackMap: Boolean(document.querySelector('.track svg')),
        hasFlags: Boolean(document.querySelector('.flags-v2 .flag-cell')),
        hasChatLine: Boolean(document.querySelector('.chat-line')),
        hasGarageCover: Boolean(document.querySelector('.garage-cover')),
        tableRowClasses: [...document.querySelectorAll('tbody tr')].map((row) => [...row.classList]),
        hasWaitingText: (document.body.innerText || '').toLowerCase().includes('waiting')
      };

      function radarChromeVisible() {
        const radar = document.querySelector('.radar-v2');
        if (!radar) return null;
        return ['.radar-title', '.radar-status']
          .map((selector) => document.querySelector(selector))
          .filter(Boolean)
          .every((element) => isInsideRadarCircle(radar, element));
      }

      function isInsideRadarCircle(radar, element) {
        const radarBox = radar.getBoundingClientRect();
        const elementBox = element.getBoundingClientRect();
        if (getComputedStyle(radar).overflow === 'visible') {
          return elementBox.left >= 0
            && elementBox.top >= 0
            && elementBox.right <= window.innerWidth
            && elementBox.bottom <= window.innerHeight;
        }

        const radius = Math.min(radarBox.width, radarBox.height) / 2;
        const centerX = radarBox.left + radarBox.width / 2;
        const centerY = radarBox.top + radarBox.height / 2;
        const elementY = elementBox.top + elementBox.height / 2 - centerY;
        const halfWidthAtY = Math.sqrt(Math.max(0, radius * radius - elementY * elementY));
        return elementBox.left >= centerX - halfWidthAtY + 1
          && elementBox.right <= centerX + halfWidthAtY - 1;
      }
    });
    const canvasPixels = await canvasHasVisiblePixels(page);
    const failures = validateMetrics(overlay.page.id, metrics, canvasPixels, modelResponse)
      .concat(overlay.modelRoute ? validateModel(overlay.page.id, modelResponse, metrics) : [])
      .concat(validateCaptureLiveSnapshot(snapshotResponse, requireCaptureLive))
      .concat(validateReplayStatus(overlay.page.id, replayStatusResponse, requireCaptureLive))
      .concat(screenshotArtifact.failures)
      .concat(consoleErrors.map((error) => `console error: ${error}`))
      .concat(pageErrors.map((error) => `page error: ${error}`))
      .concat(failedRequests.map((request) => `request failed: ${request}`));

    return {
      overlayId: overlay.page.id,
      route: overlay.route,
      relativeSeconds: frameSeconds,
      url,
      screenshotPath,
      screenshotArtifact,
      metrics,
      model: summarizeModel(modelResponse),
      snapshot: summarizeSnapshot(snapshotResponse),
      replayStatus: summarizeReplayStatus(replayStatusResponse),
      canvasPixels,
      failures
    };
  } catch (error) {
    return {
      overlayId: overlay.page.id,
      route: overlay.route,
      relativeSeconds: frameSeconds,
      url,
      screenshotPath: null,
      screenshotArtifact: null,
      metrics: null,
      canvasPixels: [],
      failures: [error instanceof Error ? error.message : String(error)]
    };
  } finally {
    await page.close();
  }
}

function validateCaptureLiveSnapshot(response, requireCaptureLive) {
  if (!requireCaptureLive) {
    return [];
  }
  if (!response || response.error) {
    return [`snapshot fetch failed: ${response?.error || 'missing response'}`];
  }
  const live = response.live || {};
  const models = live.models || {};
  const failures = [];
  for (const name of ['session', 'reference', 'driverDirectory', 'timing', 'scoring', 'inputs']) {
    if (!models[name]) {
      failures.push(`capture live model missing: ${name}`);
    }
  }
  if (models.session?.quality !== 'capture-derived') {
    failures.push(`session model was not capture-derived (${models.session?.quality || 'missing quality'})`);
  }
  if (!Array.isArray(models.timing?.overallRows) || models.timing.overallRows.length === 0) {
    failures.push('capture timing model has no rows');
  }
  if (!Array.isArray(models.driverDirectory?.drivers) || models.driverDirectory.drivers.length === 0) {
    failures.push('capture driver directory has no drivers');
  }
  if (models.inputs?.hasData !== true) {
    failures.push('capture inputs model has no input data');
  }
  return failures;
}

function validateReplayStatus(overlayId, response, requireCaptureLive) {
  if (overlayId !== 'gap-to-leader' || !requireCaptureLive) {
    return [];
  }
  if (!response || response.error) {
    return [`replay status fetch failed: ${response?.error || 'missing response'}`];
  }

  const cadence = response.timing?.sourceCadence || response.source?.cadence;
  if (!cadence) {
    return ['replay status missing source cadence metadata; re-export the capture with the current exporter before validating Gap To Leader'];
  }

  if (cadence.denseForGapToLeader === false) {
    return [sparseCadenceFailure(cadence)];
  }

  return [];
}

function validateMetrics(overlayId, metrics, canvasPixels, modelResponse = null) {
  const failures = [];
  if (!metrics) {
    return ['metrics missing'];
  }

  if (metrics.overlayWidth < 40 || metrics.overlayHeight < 40) {
    failures.push(`overlay box too small (${metrics.overlayWidth}x${metrics.overlayHeight})`);
  }
  if (metrics.scrollWidth > metrics.viewportWidth + 1) {
    failures.push(`horizontal overflow (${metrics.scrollWidth}px > ${metrics.viewportWidth}px)`);
  }
  if (!metrics.bodyText && !isTextlessOverlay(overlayId)) {
    failures.push('page rendered no visible text');
  }
  if (metrics.statusText === 'localhost offline') {
    failures.push('overlay reported localhost offline');
  }

  if (overlayId === 'standings' || overlayId === 'relative') {
    if (metrics.tableRows <= 0) {
      failures.push('table overlay rendered no rows');
    }
  }
  if (['fuel-calculator', 'session-weather', 'pit-service'].includes(overlayId)
    && metrics.metricCount <= 0
    && !localStrategyContextUnavailable(metrics)) {
    failures.push('metric overlay rendered no metrics');
  }
  if (overlayId === 'input-state'
    && !inputStateContextUnavailable(metrics)
    && !canvasPixels.some((canvas) => canvas.selector === '.input-graph' && canvas.hasPixels)) {
    failures.push('input graph canvas is blank');
  }
  if (overlayId === 'gap-to-leader' && !canvasPixels.some((canvas) => canvas.selector === '.model-graph' && canvas.hasPixels)) {
    failures.push('gap trend canvas is blank');
  }
  if (overlayId === 'track-map' && !metrics.hasTrackMap) {
    failures.push('track map SVG missing');
  }
  if (overlayId === 'car-radar' && !metrics.hasRadar && !localInCarContextUnavailable(metrics)) {
    failures.push('radar body missing');
  }
  if (overlayId === 'car-radar' && metrics.radarChromeVisible === false && !localInCarContextUnavailable(metrics)) {
    failures.push('radar title/status is clipped by circular viewport');
  }
  if (overlayId === 'stream-chat' && !metrics.hasChatLine) {
    failures.push('stream chat status line missing');
  }
  if (overlayId === 'garage-cover' && modelResponse?.model?.shouldRender === true && !metrics.hasGarageCover) {
    failures.push('garage cover body missing');
  }
  if (overlayId === 'flags' && modelResponse?.model?.shouldRender === true && !metrics.hasFlags) {
    failures.push('flags body missing');
  }

  return failures;
}

function inputStateContextUnavailable(metrics) {
  return localInCarContextUnavailable(metrics);
}

function localInCarContextUnavailable(metrics) {
  const text = String(metrics?.bodyText || '').toLowerCase();
  const status = String(metrics?.statusText || '').toLowerCase();
  return text.includes('waiting for player in car')
    || status.includes('waiting for player in car');
}

function localStrategyContextUnavailable(metrics) {
  const text = String(metrics?.bodyText || '').toLowerCase();
  const status = String(metrics?.statusText || '').toLowerCase();
  return text.includes('waiting for local ')
    || status.includes('waiting for local ');
}

function validateModel(overlayId, response, metrics = null) {
  const failures = [];
  if (!response || response.error) {
    return [`overlay model fetch failed: ${response?.error || 'missing response'}`];
  }

  const model = response.model || {};
  if (overlayId === 'standings') {
    const sessionState = response.replay?.sessionState;
    const source = String(model.source || '').toLowerCase();
    const isPreGreen = Number.isFinite(sessionState) && sessionState < 4;
    const isGrid = source.includes('starting grid');
    const isGridOnly = isGrid && !source.includes('live timing');
    const timingValues = tableValuesForKeys(model, ['gap', 'interval']);
    const lapDistanceValue = timingValues.find((value) => /\d+\.\d+L$/i.test(value));
    if (lapDistanceValue) {
      failures.push(`standings rendered fractional lap-distance gap value "${lapDistanceValue}"`);
    }
    if (isPreGreen || isGridOnly) {
      for (const value of timingValues) {
        if (/^\+\d/.test(value)) {
          failures.push(`standings rendered unstable ${isPreGreen ? 'pre-green' : 'grid-only'} gap value "${value}"`);
          break;
        }
      }
    }
  }

  if (overlayId === 'relative') {
    const columns = Array.isArray(model.columns) ? model.columns : [];
    const relativeContract = [
      ['relative.position', 'Pos', 'relative-position'],
      ['relative.driver', 'Driver', 'driver'],
      ['relative.gap', 'Delta', 'gap']
    ];
    const pitColumn = columns[relativeContract.length];
    const hasOnlyAllowedPitColumn = columns.length === relativeContract.length
      || (columns.length === relativeContract.length + 1
        && pitColumn?.id === 'relative.pit'
        && pitColumn?.label === 'Pit'
        && pitColumn?.dataKey === 'pit');
    if (!hasOnlyAllowedPitColumn
      || relativeContract.some(([id, label, dataKey], index) =>
        columns[index]?.id !== id
        || columns[index]?.label !== label
        || columns[index]?.dataKey !== dataKey)) {
      failures.push('relative replay model columns do not match production Relative contract');
    }

    if (columns.some((column) => String(column?.label || '').toLowerCase() === 'class')) {
      failures.push('relative replay model exposed a visible Class column');
    }

    const directionLabels = new Set(['ahead', 'behind', 'you']);
    const directionValue = (Array.isArray(model.rows) ? model.rows : [])
      .map((row) => String(row?.cells?.[0] || '').trim().toLowerCase())
      .find((value) => directionLabels.has(value));
    if (directionValue) {
      failures.push(`relative replay model used direction text in Pos column (${directionValue})`);
    }

    const rows = Array.isArray(model.rows) ? model.rows : [];
    if (rows.length > 0) {
      const referenceIndexes = rows
        .map((row, index) => row?.isReference === true ? index : -1)
        .filter((index) => index >= 0);
      if (referenceIndexes.length !== 1) {
        failures.push(`relative replay model expected one reference row, found ${referenceIndexes.length}`);
      } else if (rows.length % 2 !== 1 || referenceIndexes[0] !== Math.floor(rows.length / 2)) {
        failures.push(`relative replay model did not keep the reference row centered in ${rows.length} stable rows`);
      }
    }

    const lapClasses = new Set(['lap-ahead-1', 'lap-ahead-2', 'lap-behind-1', 'lap-behind-2']);
    const renderedLapClasses = (metrics?.tableRowClasses || [])
      .flat()
      .filter((className) => lapClasses.has(className));
    const modelLapDeltas = rows
      .map((row) => Number(row?.relativeLapDelta ?? row?.lapDeltaToReference))
      .filter((value) => Number.isFinite(value) && value !== 0);
    if (modelLapDeltas.length > 0 && renderedLapClasses.length === 0) {
      failures.push('relative replay model had lap relationships but rendered no lapped-car text class');
    }
    if (modelLapDeltas.some((value) => value === 1) && !renderedLapClasses.includes('lap-ahead-1')) {
      failures.push('relative replay did not render one-lap-ahead text class');
    }
    if (modelLapDeltas.some((value) => value <= -2) && !renderedLapClasses.includes('lap-behind-2')) {
      failures.push('relative replay did not render two-plus-laps-behind text class');
    }

    for (const value of tableValuesForKeys(model, ['gap'])) {
      if (/\d(?:\.\d+)?L$/i.test(value)) {
        failures.push(`relative rendered lap-distance gap value "${value}"`);
        break;
      }
    }
  }

  if (overlayId === 'gap-to-leader') {
    const points = gapGraphPoints(model);
    if (points.some((point) => !Number.isFinite(point.gapSeconds) || point.gapSeconds < 0 || !Number.isFinite(point.axisSeconds))) {
      failures.push('gap-to-leader model included invalid or negative graph points');
    }
    const pointsByCar = new Map();
    for (const point of points) {
      const carIdx = Number.isFinite(point.carIdx) ? point.carIdx : -1;
      if (!pointsByCar.has(carIdx)) pointsByCar.set(carIdx, []);
      pointsByCar.get(carIdx).push(point);
    }
    for (const carPoints of pointsByCar.values()) {
      carPoints.sort((a, b) => a.axisSeconds - b.axisSeconds);
      for (let index = 1; index < carPoints.length; index += 1) {
        const axisDelta = carPoints[index].axisSeconds - carPoints[index - 1].axisSeconds;
        if (axisDelta > 10) {
          const carLabel = Number.isFinite(carPoints[index].carIdx) && carPoints[index].carIdx >= 0
            ? `car ${carPoints[index].carIdx}`
            : 'a graph series';
          if (!carPoints[index].startsSegment) {
            failures.push(`${carLabel} connected a ${axisDelta.toFixed(1)}s Gap To Leader source gap; production segmentation starts a missing segment after 10s`);
          } else if (!isIntendedMissingSegment(carPoints[index].segmentReason)) {
            failures.push(`${carLabel} has ${axisDelta.toFixed(1)}s between Gap To Leader graph points, which production renders as separated dots. Export denser raw frames instead of changing graph segment flags.`);
          }
          break;
        }

        if (carPoints[index].startsSegment) {
          continue;
        }

        if (Math.abs(carPoints[index].gapSeconds - carPoints[index - 1].gapSeconds) > 180) {
          failures.push('gap-to-leader model included an implausible single-frame jump');
          break;
        }
      }
    }
    if (model.graph && !Array.isArray(model.graph.series)) {
      failures.push('gap-to-leader graph payload did not expose a series array');
    }
    const graph = model.graph || {};
    const referenceSeries = Array.isArray(graph.series)
      ? graph.series.find((series) => series?.isReference === true)
      : null;
    const referencePoints = (Array.isArray(referenceSeries?.points) ? referenceSeries.points : [])
      .filter((point) => Number.isFinite(point?.axisSeconds) && Number.isFinite(point?.gapSeconds))
      .sort((a, b) => a.axisSeconds - b.axisSeconds);
    const latestReferencePoint = referencePoints[referencePoints.length - 1];
    const lapReferenceSeconds = Number(graph.lapReferenceSeconds);
    if (Number.isFinite(lapReferenceSeconds)
      && lapReferenceSeconds > 20
      && latestReferencePoint?.gapSeconds >= lapReferenceSeconds * 0.95
      && graph.scale?.isFocusRelative !== true) {
      failures.push('gap-to-leader lapped reference stayed on leader scale; export/render should use focus-relative scale');
    }
  }

  return failures;
}

function gapGraphPoints(model) {
  const graphSeries = Array.isArray(model?.graph?.series) ? model.graph.series : [];
  const graphPoints = graphSeries.flatMap((series) =>
    (Array.isArray(series?.points) ? series.points : []).map((point) => ({
      axisSeconds: Number(point?.axisSeconds),
      gapSeconds: Number(point?.gapSeconds),
      carIdx: Number(series?.carIdx ?? point?.carIdx),
      startsSegment: point?.startsSegment === true,
      segmentReason: typeof point?.segmentReason === 'string' ? point.segmentReason : null
    })));
  if (graphPoints.length > 0) {
    return graphPoints;
  }

  return (Array.isArray(model?.points) ? model.points : []).map((point, index) => ({
    axisSeconds: index,
    gapSeconds: Number(point),
    carIdx: -1,
    startsSegment: false,
    segmentReason: null
  }));
}

function isIntendedMissingSegment(reason) {
  return [
    'data-unavailable',
    'explicit-missing-data',
    'telemetry-unavailable',
    'car-unavailable',
    'not-in-class'
  ].includes(String(reason || '').toLowerCase());
}

function tableValuesForKeys(model, keys) {
  const columns = Array.isArray(model.columns) ? model.columns : [];
  const wanted = new Set(keys.map((key) => key.toLowerCase()));
  const indexes = columns
    .map((column, index) => ({ column, index }))
    .filter(({ column }) => {
      const label = String(column?.label || '').toLowerCase();
      const dataKey = String(column?.dataKey || '').toLowerCase();
      return wanted.has(label) || wanted.has(dataKey);
    })
    .map(({ index }) => index);
  if (indexes.length === 0) {
    return [];
  }

  return (Array.isArray(model.rows) ? model.rows : [])
    .filter((row) => row && row.isClassHeader !== true)
    .flatMap((row) => indexes.map((index) => String(row.cells?.[index] ?? '').trim()))
    .filter(Boolean);
}

function summarizeModel(response) {
  const model = response?.model || {};
  return {
    source: model.source || null,
    status: model.status || null,
    rowCount: Array.isArray(model.rows) ? model.rows.length : 0,
    pointCount: model.graph ? gapGraphPoints(model).length : Array.isArray(model.points) ? model.points.length : 0
  };
}

function summarizeSnapshot(response) {
  const models = response?.live?.models || {};
  return {
    sourceId: response?.live?.sourceId || null,
    sessionQuality: models.session?.quality || null,
    timingRowCount: Array.isArray(models.timing?.overallRows) ? models.timing.overallRows.length : 0,
    scoringRowCount: Array.isArray(models.scoring?.rows) ? models.scoring.rows.length : 0,
    driverCount: Array.isArray(models.driverDirectory?.drivers) ? models.driverDirectory.drivers.length : 0,
    inputsHasData: models.inputs?.hasData === true
  };
}

function summarizeReplayStatus(response) {
  const cadence = response?.timing?.sourceCadence || response?.source?.cadence || {};
  const delta = cadence.sourceSessionDeltaSeconds || {};
  return {
    effectiveTimingMode: response?.timing?.effectiveMode || null,
    speedMultiplier: response?.timing?.speedMultiplier ?? null,
    denseForGapToLeader: typeof cadence.denseForGapToLeader === 'boolean' ? cadence.denseForGapToLeader : null,
    maxSourceSessionDeltaSeconds: Number.isFinite(delta.max) ? delta.max : null,
    medianSourceSessionDeltaSeconds: Number.isFinite(delta.median) ? delta.median : null
  };
}

function sparseCadenceFailure(cadence) {
  const delta = cadence?.sourceSessionDeltaSeconds || {};
  const maxDelta = Number.isFinite(delta.max) ? `${delta.max}s` : 'unknown';
  const threshold = Number.isFinite(cadence?.gapToLeaderMissingSegmentThresholdSeconds)
    ? `${cadence.gapToLeaderMissingSegmentThresholdSeconds}s`
    : '10s';
  return `Gap To Leader replay source cadence is sparse (max SessionTime delta ${maxDelta}, threshold ${threshold}); export denser raw frames instead of changing graph semantics or segment flags`;
}

function summarizeScreenshotArtifacts(results) {
  const invalid = results.filter((result) => !result.screenshotArtifact?.isValid);
  return {
    expected: results.length,
    valid: results.length - invalid.length,
    invalid: invalid.length
  };
}

function inspectScreenshotArtifact(screenshotPath) {
  const failures = [];
  if (!screenshotPath || !existsSync(screenshotPath)) {
    return {
      path: screenshotPath,
      isValid: false,
      exists: false,
      byteLength: 0,
      width: 0,
      height: 0,
      failures: ['screenshot artifact missing']
    };
  }

  const stats = statSync(screenshotPath);
  const data = readFileSync(screenshotPath);
  const isPng = hasPngSignature(data);
  const dimensions = isPng ? readPngDimensions(data) : { width: 0, height: 0 };

  if (stats.size < 100) {
    failures.push(`screenshot artifact too small (${stats.size} bytes)`);
  }
  if (!isPng) {
    failures.push('screenshot artifact is not a PNG');
  }
  if (dimensions.width <= 0 || dimensions.height <= 0) {
    failures.push(`screenshot artifact has invalid dimensions (${dimensions.width}x${dimensions.height})`);
  }

  return {
    path: screenshotPath,
    isValid: failures.length === 0,
    exists: true,
    byteLength: stats.size,
    width: dimensions.width,
    height: dimensions.height,
    failures
  };
}

function hasPngSignature(data) {
  return data.length >= 24
    && data[0] === 0x89
    && data[1] === 0x50
    && data[2] === 0x4e
    && data[3] === 0x47
    && data[4] === 0x0d
    && data[5] === 0x0a
    && data[6] === 0x1a
    && data[7] === 0x0a;
}

function readPngDimensions(data) {
  if (data.length < 24) {
    return { width: 0, height: 0 };
  }

  return {
    width: data.readUInt32BE(16),
    height: data.readUInt32BE(20)
  };
}

function isTextlessOverlay(overlayId) {
  return overlayId === 'track-map' || overlayId === 'garage-cover' || overlayId === 'flags';
}

async function canvasHasVisiblePixels(page) {
  return page.evaluate(() => Array.from(document.querySelectorAll('canvas')).map((canvas) => {
    const selector = canvas.className
      ? `.${String(canvas.className).split(/\s+/).filter(Boolean).join('.')}`
      : 'canvas';
    const context = canvas.getContext('2d');
    if (!context || canvas.width <= 0 || canvas.height <= 0) {
      return { selector, width: canvas.width, height: canvas.height, hasPixels: false };
    }

    const image = context.getImageData(0, 0, canvas.width, canvas.height).data;
    for (let offset = 0; offset < image.length; offset += 4) {
      if (image[offset + 3] > 0) {
        return { selector, width: canvas.width, height: canvas.height, hasPixels: true };
      }
    }
    return { selector, width: canvas.width, height: canvas.height, hasPixels: false };
  }));
}

function overlayUrl(overlay, relativeSeconds) {
  const url = new URL(overlay.route, normalizedBaseUrl());
  url.searchParams.set('rel', String(relativeSeconds));
  return url.toString();
}

function screenshotName(overlayId, relativeSeconds) {
  const label = relativeSeconds === 0
    ? 'green'
    : `${relativeSeconds > 0 ? 'plus' : 'minus'}${Math.abs(relativeSeconds)}s`;
  return `${overlayId}-${label}.png`;
}

function normalizedBaseUrl() {
  return baseUrl.endsWith('/') ? baseUrl : `${baseUrl}/`;
}

function parseArguments(args) {
  const [rawBaseUrl, rawOutputDir, ...options] = args;
  if (!rawBaseUrl || !rawOutputDir) {
    console.error('Usage: node tools/browser-review/validate-race-start-replay.mjs <base-url> <output-dir> [--rel=-120,0,120] [--require-capture-live]');
    process.exit(2);
  }

  const relOption = options.find((option) => option.startsWith('--rel='));
  const relValues = relOption
    ? relOption.slice('--rel='.length).split(',').map((value) => Number.parseFloat(value))
    : [-120, 0, 120];
  if (relValues.length === 0 || relValues.some((value) => !Number.isFinite(value))) {
    console.error(`Invalid --rel value: ${relOption}`);
    process.exit(2);
  }

  return {
    baseUrl: rawBaseUrl,
    outputDir: resolve(rawOutputDir),
    relativeSeconds: relValues,
    requireCaptureLive: options.includes('--require-capture-live')
  };
}
