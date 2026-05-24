import { expect, test } from '@playwright/test';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { renderOverlayHtml } from './browserOverlayTestHost.js';
import { repoRoot } from './browserOverlayAssets.js';

const snapshotRoot = resolve(repoRoot, 'fixtures/telemetry-analysis/overlay-real-data-snapshots');
const snapshots = Object.freeze({
  relative: readSnapshot('relative-practice-timing-real-data.json'),
  standings: readSnapshot('standings-practice-no-results-stability.json'),
  trackMap: readSnapshot('track-map-focus-and-practice-marker-policy.json'),
  trackMapPlayerFocus: readSnapshot('track-map-player-focus-class-color-real-data.json'),
  flags: readSnapshot('flags-meatball-local-policy.json'),
  fuel: readSnapshot('fuel-measured-burn-real-data.json'),
  pitService: readSnapshot('pit-service-refuel-pit-window-real-data.json'),
  gap: readSnapshot('gap-to-leader-long-tail-real-data.json'),
  gapPitWindow: readSnapshot('gap-to-leader-pit-window-real-data.json'),
  gapThreat: readSnapshot('gap-to-leader-threat-capture-shaped.json'),
  gapEndurance: readSnapshot('gap-to-leader-endurance-domain-capture-shaped.json')
});

test.describe('real-data compact snapshot browser rendering', () => {
  test('renders Relative practice timing from compact real-data seconds', async ({ page }) => {
    const snapshot = snapshots.relative;
    const model = relativeModel(snapshot);
    const { requests, browserSourceEvents } = await installOverlayRoutes(page, 'relative', model);

    await page.setViewportSize({ width: 360, height: 308 });
    await page.goto('http://localhost:8765/overlays/relative');

    await expect(page.locator('tbody tr')).toHaveCount(snapshot.expected.rows.length);
    await expect(page.locator('tbody tr.focus')).toContainText('0.000');
    for (const row of snapshot.expected.rows) {
      await expect(page.locator('tbody')).toContainText(row.display);
    }

    const deltas = await page.locator('tbody td:nth-child(3)').allTextContents();
    expect(deltas).toEqual(snapshot.expected.rows.map((row) => row.display));
    for (const text of deltas) {
      expect(text).not.toContain('m');
      expect(text).not.toBe('--');
    }
    expect(requests).toContain('/api/overlay-model/relative');
    expect(requests).not.toContain('/api/snapshot');
    expect(browserSourceEvents).toEqual(expect.arrayContaining([
      expect.objectContaining({ event: 'model-render', overlayId: 'relative' })
    ]));
  });

  test('keeps Standings visible but rowless before practice results exist', async ({ page }) => {
    const snapshot = snapshots.standings;
    const model = standingsNoResultsModel(snapshot);
    const { requests, browserSourceEvents } = await installOverlayRoutes(page, 'standings', model);

    await page.setViewportSize({ width: 692, height: 313 });
    await page.goto('http://localhost:8765/overlays/standings');

    await expect(page.locator('.overlay')).toHaveCSS('opacity', '1');
    await expect(page.locator('.header-items')).toContainText('Practice');
    await expect(page.locator('tbody tr')).toHaveCount(snapshot.expected.bodyRowCount);
    await expect(page.locator('#content')).toBeHidden();
    await expect(page.locator('table')).toHaveCount(0);
    const contentText = await page.locator('#content').textContent();
    for (const pattern of snapshot.expected.forbiddenTextPatterns) {
      expect(contentText).not.toContain(pattern);
    }
    expect(requests).toContain('/api/overlay-model/standings');
    expect(requests).not.toContain('/api/snapshot');
    expect(browserSourceEvents).toEqual(expect.arrayContaining([
      expect.objectContaining({ event: 'model-render', overlayId: 'standings' })
    ]));
  });

  test('renders Track Map focus and practice open-session markers', async ({ page }) => {
    const snapshot = snapshots.trackMap;
    const model = trackMapModel(snapshot);
    const { requests, browserSourceEvents } = await installOverlayRoutes(page, 'track-map', model);

    await page.setViewportSize({ width: 360, height: 360 });
    await page.goto('http://localhost:8765/overlays/track-map');

    await expect(page.locator('.track svg')).toBeVisible();
    const markerLabels = await page.locator('.track svg text').allTextContents();
    expect(markerLabels.sort()).toEqual(snapshot.expected.practiceMarkerPolicy.visibleCarIdxs.map(String).sort());
    for (const hiddenCarIdx of snapshot.expected.practiceMarkerPolicy.hiddenCarIdxs) {
      expect(markerLabels).not.toContain(String(hiddenCarIdx));
    }

    const markerEvidence = await page.evaluate(() => [...document.querySelectorAll('.track svg g')].map((group) => ({
      label: group.querySelector('text')?.textContent,
      radius: Number(group.querySelector('circle')?.getAttribute('r')),
      fill: group.querySelector('circle')?.getAttribute('fill')
    })));
    const focusMarker = markerEvidence.find((marker) => marker.label === String(snapshots.trackMap.expected.focusMarker.carIdx));
    const playerMarker = markerEvidence.find((marker) => marker.label === String(snapshots.trackMap.expected.playerMarker.carIdx));
    expect(focusMarker?.radius).toBeGreaterThan(playerMarker?.radius ?? 0);
    expect(focusMarker?.fill).toBe('rgba(255,218,89,1.000)');
    expect(playerMarker?.fill).toBe('rgba(0,174,239,1.000)');
    expect(requests).toContain('/api/overlay-model/track-map');
    expect(requests).not.toContain('/api/snapshot');
    expect(browserSourceEvents).toEqual(expect.arrayContaining([
      expect.objectContaining({ event: 'model-render', overlayId: 'track-map' })
    ]));
  });

  test('renders Track Map player focus with class-owned white marker fill', async ({ page }) => {
    const snapshot = snapshots.trackMapPlayerFocus;
    const model = trackMapPlayerFocusModel(snapshot);
    const { requests, browserSourceEvents } = await installOverlayRoutes(page, 'track-map', model);

    await page.setViewportSize({ width: 360, height: 360 });
    await page.goto('http://localhost:8765/overlays/track-map');

    await expect(page.locator('.track svg')).toBeVisible();
    const marker = await page.locator('.track svg g').evaluate((group) => ({
      label: group.querySelector('text')?.textContent,
      radius: Number(group.querySelector('circle')?.getAttribute('r')),
      fill: group.querySelector('circle')?.getAttribute('fill')
    }));

    expect(marker.label).toBe(String(snapshot.expected.focusMarker.carIdx));
    expect(marker.fill).toBe('rgba(255,255,255,1.000)');
    expect(marker.fill).not.toBe('rgba(0,232,255,1.000)');
    expect(marker.radius).toBeGreaterThan(9);
    expect(requests).toContain('/api/overlay-model/track-map');
    expect(requests).not.toContain('/api/snapshot');
    expect(browserSourceEvents).toEqual(expect.arrayContaining([
      expect.objectContaining({ event: 'model-render', overlayId: 'track-map' })
    ]));
  });

  test('renders confirmed local-driver meatball and hides global-only counterexample', async ({ page }) => {
    const snapshot = snapshots.flags;
    const localModel = flagsMeatballModel(snapshot);
    const local = await installOverlayRoutes(page, 'flags', localModel);

    await page.setViewportSize({ width: 360, height: 170 });
    await page.goto('http://localhost:8765/overlays/flags?case=local');

    await expect(page.locator('.flags-v2')).toBeVisible();
    await expect(page.locator('.flag-meatball')).toHaveCount(1);
    await expect(page.locator('.flag-label')).toHaveText(snapshot.expected.confirmedMeatball.displayKind);
    expect(local.requests).toContain('/api/overlay-model/flags');
    expect(local.requests).not.toContain('/api/snapshot');
    expect(local.browserSourceEvents).toEqual(expect.arrayContaining([
      expect.objectContaining({ event: 'model-render', overlayId: 'flags' })
    ]));

    await page.unroute('**/*');
    const hidden = await installOverlayRoutes(page, 'flags', flagsGlobalOnlyHiddenModel(snapshot));
    await page.goto('http://localhost:8765/overlays/flags?case=global-only');

    await expect(page.locator('.flags-v2')).toHaveCount(0);
    await expect(page.locator('#content')).toBeEmpty();
    await expect.poll(async () => page.locator('.overlay').evaluate((element) =>
      Number.parseFloat(getComputedStyle(element).opacity))).toBe(0);
    expect(hidden.browserSourceEvents).toEqual(expect.arrayContaining([
      expect.objectContaining({ event: 'model-hidden', overlayId: 'flags' })
    ]));
  });

  test('renders Fuel measured burn evidence without history-required fallback copy', async ({ page }) => {
    const snapshot = snapshots.fuel;
    const model = fuelMeasuredBurnModel(snapshot);
    const { requests, browserSourceEvents } = await installOverlayRoutes(page, 'fuel-calculator', model);

    await page.setViewportSize({ width: 503, height: 315 });
    await page.goto('http://localhost:8765/overlays/fuel-calculator');

    await expect(page.locator('.metric-section-title')).toHaveText(['Current Session Fuel']);
    await expect(page.locator('#content')).toContainText('Current-session burn');
    await expect(page.locator('#content')).toContainText('rolling-local-fuel-delta');
    await expect(page.locator('#content')).toContainText(formatLitersPerLap(snapshot.rawEvidence.postRaceSummary.fuelPerLapLiters));
    const contentText = await page.locator('#content').textContent();
    expect(contentText).not.toMatch(/unavailable|history required|post-session/i);
    expect(requests).toContain('/api/overlay-model/fuel-calculator');
    expect(requests).not.toContain('/api/snapshot');
    expect(browserSourceEvents).toEqual(expect.arrayContaining([
      expect.objectContaining({ event: 'model-render', overlayId: 'fuel-calculator' })
    ]));
  });

  test('renders Pit Service refuel request row without Fuel Calculator strategy labels', async ({ page }) => {
    const snapshot = snapshots.pitService;
    const model = pitServiceRefuelModel(snapshot);
    const { requests, browserSourceEvents } = await installOverlayRoutes(page, 'pit-service', model);

    await page.setViewportSize({ width: 530, height: 360 });
    await page.goto('http://localhost:8765/overlays/pit-service');

    await expect(page.locator('.metric')).toHaveCount(1);
    await expect(page.locator('.metric .label')).toHaveText(snapshot.expected.fuelRequestRow.label);
    await expect(page.locator('.segment-label')).toHaveText(snapshot.expected.fuelRequestRow.segmentLabels);
    await expect(page.locator('.segment-value')).toHaveText(snapshot.expected.fuelRequestRow.segmentValues);
    const contentText = await page.locator('#content').textContent();
    for (const label of snapshot.expected.fuelRequestRow.forbiddenSegmentLabels) {
      expect(contentText).not.toContain(label);
    }
    expect(requests).toContain('/api/overlay-model/pit-service');
    expect(requests).not.toContain('/api/snapshot');
    expect(browserSourceEvents).toEqual(expect.arrayContaining([
      expect.objectContaining({ event: 'model-render', overlayId: 'pit-service' })
    ]));
  });

  test('renders Gap To Leader long-tail real data without far-behind outlier series', async ({ page }) => {
    const snapshot = snapshots.gap;
    const model = gapLongTailModel(snapshot);
    const { requests, browserSourceEvents } = await installOverlayRoutes(page, 'gap-to-leader', model);

    await page.setViewportSize({ width: 654, height: 336 });
    await page.goto('http://localhost:8765/overlays/gap-to-leader');

    await expect(page.locator('.model-graph')).toBeVisible();
    const selectedClassPositions = model.graph.series.map((series) => series.classPosition);
    expect(selectedClassPositions).toEqual(snapshot.expected.graph.selectedClassPositions);
    for (const classPosition of snapshot.expected.graph.forbiddenClassPositions) {
      expect(selectedClassPositions).not.toContain(classPosition);
    }
    expect(model.graph.scale.isFocusRelative).toBe(true);
    expect(model.graph.scale.behindSeconds).toBeLessThanOrEqual(snapshot.expected.graph.axisBehindSecondsMaximum);
    expect(model.graph.scale.behindSeconds).toBeGreaterThanOrEqual(snapshot.expected.graph.maxIncludedGapToFocusSeconds);

    const canvasHasPaint = await page.locator('.model-graph').evaluate((canvas) => {
      const context = canvas.getContext('2d');
      const pixels = context.getImageData(0, 0, canvas.width, canvas.height).data;
      for (let index = 3; index < pixels.length; index += 4) {
        if (pixels[index] > 0) return true;
      }
      return false;
    });
    expect(canvasHasPaint).toBe(true);
    expect(requests).toContain('/api/overlay-model/gap-to-leader');
    expect(requests).not.toContain('/api/snapshot');
    expect(browserSourceEvents).toEqual(expect.arrayContaining([
      expect.objectContaining({ event: 'model-render', overlayId: 'gap-to-leader' })
    ]));
  });

  test('renders Gap To Leader pit-window real data without treating the ahead car as a threat', async ({ page }) => {
    const snapshot = snapshots.gapPitWindow;
    const model = gapPitWindowModel(snapshot);
    const { requests, browserSourceEvents } = await installOverlayRoutes(page, 'gap-to-leader', model);

    await page.setViewportSize({ width: 654, height: 336 });
    await page.goto('http://localhost:8765/overlays/gap-to-leader');

    await expect(page.locator('.model-graph')).toBeVisible();
    expect(model.graph.series.map((series) => series.classPosition)).toEqual(snapshot.expected.graph.selectedClassPositions);
    expect(model.graph.comparisonLabel).toBe(snapshot.expected.graph.comparisonLabel);
    expect(model.graph.activeThreat).toBeNull();
    expect(model.graph.threatCarIdx).toBeNull();
    expect(model.graph.pitWindows).toHaveLength(1);
    expect(model.graph.pitWindows[0]).toEqual(expect.objectContaining({
      carIdx: snapshot.rawEvidence.focusCar.carIdx,
      classPosition: snapshot.rawEvidence.focusCar.classPosition,
      isReference: true,
      durationSeconds: snapshot.rawEvidence.pitWindow.durationSeconds,
      lap: snapshot.rawEvidence.pitWindow.displayLap
    }));
    for (const classPosition of snapshot.expected.graph.forbiddenThreatClassPositions) {
      expect(model.graph.series.find((series) => series.classPosition === classPosition)?.carIdx).not.toBe(model.graph.threatCarIdx);
    }
    expect(requests).toContain('/api/overlay-model/gap-to-leader');
    expect(browserSourceEvents).toEqual(expect.arrayContaining([
      expect.objectContaining({ event: 'model-render', overlayId: 'gap-to-leader' })
    ]));
  });

  test('renders Gap To Leader capture-shaped active threat behind the focused car', async ({ page }) => {
    const snapshot = snapshots.gapThreat;
    const model = gapThreatModel(snapshot);
    const { requests, browserSourceEvents } = await installOverlayRoutes(page, 'gap-to-leader', model);

    await page.setViewportSize({ width: 654, height: 336 });
    await page.goto('http://localhost:8765/overlays/gap-to-leader');

    await expect(page.locator('.model-graph')).toBeVisible();
    expect(model.graph.series.map((series) => series.classPosition)).toEqual(snapshot.expected.graph.selectedClassPositions);
    expect(model.graph.comparisonLabel).toBe(snapshot.expected.graph.comparisonLabel);
    expect(model.graph.activeThreat.chaser.label).toBe(snapshot.expected.graph.activeThreat.label);
    expect(model.graph.activeThreat.chaser.carIdx).toBe(snapshot.expected.graph.activeThreat.carIdx);
    expect(model.graph.threatCarIdx).toBe(snapshot.expected.graph.activeThreat.carIdx);
    const focusPosition = snapshot.expected.graph.focusClassPosition;
    const threatPosition = snapshot.expected.graph.activeThreat.classPosition;
    expect(threatPosition).toBeGreaterThan(focusPosition);
    for (const classPosition of snapshot.expected.graph.forbiddenThreatClassPositions) {
      expect(model.graph.series.find((series) => series.classPosition === classPosition)?.carIdx).not.toBe(model.graph.threatCarIdx);
    }
    expect(requests).toContain('/api/overlay-model/gap-to-leader');
    expect(browserSourceEvents).toEqual(expect.arrayContaining([
      expect.objectContaining({ event: 'model-render', overlayId: 'gap-to-leader' })
    ]));
  });

  test('renders Gap To Leader capture-shaped four-hour domain with markers and weather bands', async ({ page }) => {
    const snapshot = snapshots.gapEndurance;
    const model = gapEnduranceModel(snapshot);
    const { requests, browserSourceEvents } = await installOverlayRoutes(page, 'gap-to-leader', model);

    await page.setViewportSize({ width: 654, height: 336 });
    await page.goto('http://localhost:8765/overlays/gap-to-leader');

    await expect(page.locator('.model-graph')).toBeVisible();
    expect(model.graph.endSeconds - model.graph.startSeconds).toBeGreaterThanOrEqual(snapshot.expected.graph.minimumDurationHours * 60 * 60);
    expect(model.graph.series.map((series) => series.classPosition)).toEqual(snapshot.expected.graph.selectedClassPositions);
    expect(model.graph.series.length).toBeGreaterThanOrEqual(snapshot.expected.graph.minimumVisibleSeriesCount);
    expect(model.graph.weather.length).toBeGreaterThanOrEqual(snapshot.expected.graph.minimumWeatherBandCount);
    expect(model.graph.leaderChanges.length + model.graph.driverChanges.length).toBeGreaterThanOrEqual(snapshot.expected.graph.minimumGraphMarkerCount);
    expect(model.graph.pitWindows.length).toBeGreaterThanOrEqual(snapshot.expected.graph.minimumPitWindowBandCount);
    expect(model.graph.activeThreat).toBeNull();
    expect(model.graph.threatCarIdx).toBeNull();
    expect(requests).toContain('/api/overlay-model/gap-to-leader');
    expect(browserSourceEvents).toEqual(expect.arrayContaining([
      expect.objectContaining({ event: 'model-render', overlayId: 'gap-to-leader' })
    ]));
  });
});

async function installOverlayRoutes(page, overlayId, model) {
  const requests = [];
  const browserSourceEvents = [];
  await page.route('**/*', async (route) => {
    const request = route.request();
    const url = new URL(request.url());
    requests.push(url.pathname);

    if (url.hostname === 'localhost' && (url.pathname === `/overlays/${overlayId}` || url.pathname === `/review/overlays/${overlayId}`)) {
      await route.fulfill({
        status: 200,
        contentType: 'text/html; charset=utf-8',
        body: renderOverlayHtml(overlayId)
      });
      return;
    }

    if (url.hostname === 'localhost' && url.pathname === `/api/overlay-model/${overlayId}`) {
      await route.fulfill({
        status: 200,
        contentType: 'application/json; charset=utf-8',
        body: JSON.stringify({
          generatedAtUtc: '2026-05-23T00:00:00Z',
          model: typeof model === 'function' ? model() : model
        })
      });
      return;
    }

    if (url.hostname === 'localhost' && url.pathname === '/api/browser-source-event') {
      browserSourceEvents.push(JSON.parse(request.postData() || '{}'));
      await route.fulfill({
        status: 200,
        contentType: 'application/json; charset=utf-8',
        body: '{"ok":true}'
      });
      return;
    }

    if (url.hostname === 'localhost') {
      await route.fulfill({
        status: 404,
        contentType: 'application/json; charset=utf-8',
        body: '{}'
      });
      return;
    }

    await route.abort();
  });

  return { requests, browserSourceEvents };
}

function readSnapshot(fileName) {
  const snapshot = JSON.parse(readFileSync(resolve(snapshotRoot, fileName), 'utf8'));
  if (snapshot.schemaVersion !== 1) {
    throw new Error(`${fileName} expected schemaVersion 1`);
  }
  return snapshot;
}

function relativeModel(snapshot) {
  const rows = snapshot.expected.rows.map((row, index) => ({
    isReference: row.role === 'reference',
    cells: [String(index - 1), `Car ${row.carIdx}`, row.display]
  }));
  return {
    ...baseModel('relative', 'relative-practice-timing-real-data', 'source: model-v2 timing fallback'),
    bodyKind: 'table',
    columns: [
      { label: 'Pos', dataKey: 'position', width: 48, alignment: 'right' },
      { label: 'Driver', dataKey: 'driver', width: 210, alignment: 'left' },
      { label: 'Delta', dataKey: 'relativeDelta', width: 70, alignment: 'right' }
    ],
    rows,
    shouldRender: true
  };
}

function standingsNoResultsModel(snapshot) {
  return {
    ...baseModel('standings', snapshot.expected.source, 'source: waiting-for-practice-results'),
    bodyKind: 'table',
    headerItems: [{ key: 'session', value: 'Practice', tone: 'waiting' }],
    columns: [],
    rows: [],
    shouldRender: true
  };
}

function trackMapModel(snapshot) {
  const rowsByCarIdx = new Map(snapshot.rawEvidence.timingRows.map((row) => [row.carIdx, row]));
  const markerCarIdxs = snapshot.expected.practiceMarkerPolicy.visibleCarIdxs;
  const nonFocusPositions = [{ x: 130, y: 220 }, { x: 110, y: 150 }, { x: 180, y: 250 }];
  let nonFocusIndex = 0;
  const markers = markerCarIdxs.map((carIdx) => {
    const row = rowsByCarIdx.get(carIdx);
    const isFocus = carIdx === snapshot.expected.focusMarker.carIdx;
    const position = isFocus
      ? { x: 230, y: 120 }
      : nonFocusPositions[nonFocusIndex++ % nonFocusPositions.length];
    return {
      carIdx,
      label: String(carIdx),
      x: position.x,
      y: position.y,
      radius: isFocus ? 14 : 9,
      strokeWidth: 2,
      fill: colorFromHex(row.classColor),
      stroke: colorFromHex('#FFFFFF'),
      labelColor: colorFromHex('#051017')
    };
  });
  return {
    ...baseModel('track-map', 'track map live markers', 'source: compact real-data marker policy'),
    bodyKind: 'track-map',
    trackMap: {
      mapKind: 'fallback-live-markers',
      markerCount: markers.length,
      renderModel: {
        width: 360,
        height: 360,
        mapKind: 'fallback-live-markers',
        markerCount: markers.length,
        primitives: [
          {
            kind: 'ellipse',
            rect: { x: 48, y: 48, width: 264, height: 264 },
            fill: colorFromHex('#111820', 0),
            stroke: colorFromHex('#6EA8D9'),
            strokeWidth: 2
          }
        ],
        markers
      }
    },
    shouldRender: true
  };
}

function trackMapPlayerFocusModel(snapshot) {
  const row = snapshot.rawEvidence.timingRows.find((candidate) => candidate.role === 'player-focus');
  const marker = {
    carIdx: row.carIdx,
    label: String(row.carIdx),
    x: 230,
    y: 120,
    radius: 14,
    strokeWidth: 2,
    fill: colorFromHex(snapshot.expected.focusMarker.fill),
    stroke: colorFromHex('#FFFFFF'),
    labelColor: colorFromHex('#051017')
  };
  return {
    ...baseModel('track-map', 'track map player focus class color', 'source: compact GR86 player-focus class color'),
    bodyKind: 'track-map',
    trackMap: {
      mapKind: 'fallback-live-markers',
      markerCount: 1,
      renderModel: {
        width: 360,
        height: 360,
        mapKind: 'fallback-live-markers',
        markerCount: 1,
        primitives: [
          {
            kind: 'ellipse',
            rect: { x: 48, y: 48, width: 264, height: 264 },
            fill: colorFromHex('#111820', 0),
            stroke: colorFromHex('#6EA8D9'),
            strokeWidth: 2
          }
        ],
        markers: [marker]
      }
    },
    shouldRender: true
  };
}

function flagsMeatballModel(snapshot) {
  const expected = snapshot.expected.confirmedMeatball;
  return {
    ...baseModel('flags', expected.source, 'source: local-driver CarIdxSessionFlags'),
    bodyKind: 'flags',
    flags: {
      isWaiting: false,
      flags: [{
        kind: expected.displayKind.toLowerCase(),
        category: expected.category.toLowerCase(),
        label: expected.displayKind,
        detail: null,
        tone: 'error'
      }]
    },
    shouldRender: true
  };
}

function flagsGlobalOnlyHiddenModel(snapshot) {
  return {
    ...baseModel('flags', snapshot.expected.globalOnlyCriticalPolicy.reason, 'source: global-only critical bits rejected'),
    bodyKind: 'flags',
    flags: {
      isWaiting: false,
      flags: []
    },
    shouldRender: false
  };
}

function fuelMeasuredBurnModel(snapshot) {
  const summary = snapshot.rawEvidence.postRaceSummary;
  return {
    ...baseModel('fuel-calculator', 'measured burn available', 'source: rolling-local-fuel-delta'),
    bodyKind: 'metrics',
    headerItems: [{ key: 'session', value: 'Race', tone: 'live' }],
    metricSections: [{
      title: 'Current Session Fuel',
      rows: [{
        label: 'Current-session burn',
        value: formatLitersPerLap(summary.fuelPerLapLiters),
        tone: 'info',
        segments: [
          { label: 'Source', value: snapshot.expected.currentSessionMeasuredBurn.source, tone: 'info' },
          { label: 'Burn', value: formatLitersPerLap(summary.fuelPerLapLiters), tone: 'info' },
          { label: 'Laps', value: String(summary.completedValidLaps), tone: 'info' },
          { label: 'Added', value: `${summary.refuelPitStop.fuelAddedLiters.toFixed(1)} L`, tone: 'info' }
        ]
      }]
    }],
    shouldRender: true
  };
}

function pitServiceRefuelModel(snapshot) {
  const row = snapshot.expected.fuelRequestRow;
  return {
    ...baseModel('pit-service', 'pit refuel request', 'source: PitSvFuel compact real-data window'),
    bodyKind: 'metrics',
    headerItems: [{ key: 'state', value: 'Pit service', tone: 'live' }],
    metricSections: [{
      title: 'Service',
      rows: [{
        label: row.label,
        value: row.value,
        tone: 'info',
        segments: row.segmentLabels.map((label, index) => ({
          label,
          value: row.segmentValues[index],
          tone: 'info'
        }))
      }]
    }],
    shouldRender: true
  };
}

function gapLongTailModel(snapshot) {
  const startSeconds = snapshot.provenance.sampleSessionTimeSeconds - 420;
  const focusGaps = [1.78, 1.66, 1.58, 1.51, 1.46, 1.42, 1.39, snapshot.rawEvidence.focusCar.gapToClassLeaderSeconds];
  const leaderGaps = focusGaps.map(() => 0);
  const aheadGaps = [0.96, 0.91, 0.86, 0.79, 0.72, 0.64, 0.55, 0.43];
  const point = (index, gapSeconds) => ({
    axisSeconds: startSeconds + index * 60,
    gapSeconds,
    startsSegment: index === 0
  });
  const series = [
    gapSeries(snapshot.rawEvidence.classLeader.carIdx, 1, true, false, leaderGaps.map((value, index) => point(index, value))),
    gapSeries(16, 4, false, false, aheadGaps.map((value, index) => point(index, value))),
    gapSeries(snapshot.rawEvidence.focusCar.carIdx, 5, false, true, focusGaps.map((value, index) => point(index, value)))
  ];

  return {
    ...baseModel('gap-to-leader', 'live | Dallara long-tail policy', 'source: compact Dallara race capture'),
    bodyKind: 'graph',
    headerItems: [{ key: 'timeRemaining', value: '00:38:44', tone: 'live' }],
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
      trendMetrics: gapTrendMetrics(),
      activeThreat: null,
      threatCarIdx: null,
      metricDeadbandSeconds: 0.25,
      comparisonLabel: snapshot.expected.graph.comparisonLabel,
      showGraph: true,
      showTrendMetrics: true,
      scale: {
        maxGapSeconds: snapshot.expected.graph.axisBehindSecondsMaximum,
        isFocusRelative: true,
        aheadSeconds: 2.0,
        behindSeconds: snapshot.expected.graph.axisBehindSecondsMaximum,
        referencePoints: focusGaps.map((value, index) => point(index, value)),
        latestReferenceGapSeconds: focusGaps[focusGaps.length - 1]
      }
    },
    shouldRender: true
  };
}

function gapPitWindowModel(snapshot) {
  const rawFrames = snapshot.rawEvidence.orderedFrames;
  const firstFrame = rawFrames[0];
  const lastFrame = rawFrames[rawFrames.length - 1];
  const frames = [
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
    startsSegment: frame === frames[0]
  });
  const focusPoints = frames.map((frame) => point(frame, Math.max(0, Number(frame.focusGapToClassLeaderSeconds || 0))));
  const leaderPoints = frames.map((frame) => point(frame, 0));
  const aheadPoints = frames.map((frame) => point(frame, Math.max(0.3, Number(frame.focusGapToClassLeaderSeconds || 0) - 1.1)));
  const series = [
    gapSeries(13, 1, true, false, leaderPoints),
    gapSeries(16, 4, false, false, aheadPoints),
    gapSeries(snapshot.rawEvidence.focusCar.carIdx, snapshot.rawEvidence.focusCar.classPosition, false, true, focusPoints)
  ];
  const maxFocusGap = Math.max(...focusPoints.map((item) => item.gapSeconds).filter(Number.isFinite), 1);
  const pitWindow = snapshot.rawEvidence.pitWindow;
  const pitWindows = pitWindow ? [{
    entryAxisSeconds: pitWindow.entrySessionTimeSeconds,
    exitAxisSeconds: pitWindow.exitSessionTimeSeconds,
    carIdx: snapshot.rawEvidence.focusCar.carIdx,
    classPosition: snapshot.rawEvidence.focusCar.classPosition,
    isReference: true,
    isActive: false,
    durationSeconds: pitWindow.durationSeconds,
    lap: pitWindow.displayLap
  }] : [];

  return {
    ...baseModel('gap-to-leader', 'live | Dallara pit-window policy', 'source: compact Dallara pit-window capture'),
    bodyKind: 'graph',
    headerItems: [{ key: 'timeRemaining', value: '00:37:19', tone: 'live' }],
    graph: {
      series,
      weather: [],
      leaderChanges: [],
      driverChanges: [],
      pitWindows,
      startSeconds: Number(firstFrame.sessionTimeSeconds || 0) - 4,
      endSeconds: Number(frames[frames.length - 1]?.sessionTimeSeconds || firstFrame.sessionTimeSeconds || 0) + 4,
      maxGapSeconds: Math.ceil(maxFocusGap + 2),
      lapReferenceSeconds: 525.8,
      selectedSeriesCount: series.length,
      trendMetrics: gapTrendMetrics(),
      activeThreat: snapshot.expected.graph.activeThreat,
      threatCarIdx: null,
      metricDeadbandSeconds: 0.25,
      comparisonLabel: snapshot.expected.graph.comparisonLabel,
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
    shouldRender: true
  };
}

function gapThreatModel(snapshot) {
  const startSeconds = Number(snapshot.provenance.sampleWindowSessionTimeSeconds[0]);
  const trend = snapshot.rawEvidence.orderedTrend;
  const threat = snapshot.expected.graph.activeThreat;
  const threatCarIdx = threat.carIdx;
  const point = (sample, gapSeconds, index) => ({
    axisSeconds: startSeconds + Number(sample.offsetSeconds),
    gapSeconds,
    completedLap: 20 + index,
    startsSegment: index === 0
  });
  const activeThreat = {
    label: '5L',
    focusGapChangeSeconds: -0.6,
    chaser: {
      carIdx: threatCarIdx,
      label: threat.label,
      gainSeconds: threat.gainSeconds
    },
    state: 'ready',
    completedReferenceLaps: 10
  };
  const series = [
    gapSeries(snapshot.rawEvidence.classLeader.carIdx, 1, true, false, trend.map((sample, index) => point(sample, sample.leaderGapSeconds, index))),
    gapSeries(snapshot.rawEvidence.comparisonAhead.carIdx, 4, false, false, trend.map((sample, index) => point(sample, sample.aheadGapSeconds, index))),
    gapSeries(snapshot.rawEvidence.focusCar.carIdx, 5, false, true, trend.map((sample, index) => point(sample, sample.focusGapSeconds, index))),
    gapSeries(threatCarIdx, threat.classPosition, false, false, trend.map((sample, index) => point(sample, sample.threatGapSeconds, index)))
  ];

  return {
    ...baseModel('gap-to-leader', 'live | capture-shaped threat', 'source: capture-shaped Dallara threat fixture'),
    bodyKind: 'graph',
    headerItems: [{ key: 'timeRemaining', value: '00:18:20', tone: 'live' }],
    graph: {
      series,
      weather: [],
      leaderChanges: [],
      driverChanges: [],
      pitWindows: [],
      startSeconds,
      endSeconds: startSeconds + Number(trend[trend.length - 1].offsetSeconds),
      maxGapSeconds: 16,
      lapReferenceSeconds: 525.8,
      selectedSeriesCount: series.length,
      trendMetrics: gapTrendMetrics(activeThreat),
      activeThreat,
      threatCarIdx,
      metricDeadbandSeconds: 0.25,
      comparisonLabel: snapshot.expected.graph.comparisonLabel,
      showGraph: true,
      showTrendMetrics: true,
      scale: {
        maxGapSeconds: 16,
        isFocusRelative: false,
        aheadSeconds: 0,
        behindSeconds: 0,
        referencePoints: series.find((item) => item.isReference).points,
        latestReferenceGapSeconds: trend[trend.length - 1].focusGapSeconds
      }
    },
    shouldRender: true
  };
}

function gapEnduranceModel(snapshot) {
  const startSeconds = Number(snapshot.rawEvidence.domain.startSeconds);
  const endSeconds = Number(snapshot.rawEvidence.domain.endSeconds);
  const offsets = [0, 1800, 3600, 5400, 7200, 9000, 10800, 12600, 14400];
  const point = (offsetSeconds, gapSeconds, index) => ({
    axisSeconds: startSeconds + offsetSeconds,
    gapSeconds,
    completedLap: 2 + index * 4,
    startsSegment: index === 0
  });
  const line = (values) => offsets.map((offset, index) => point(offset, values[index], index));
  const series = [
    gapSeries(14, 1, true, false, line([0, 0.4, 0.2, 0, 0.6, 0.1, 0.5, 0.3, 0])),
    gapSeries(43, 8, false, false, line([31.1, 32.0, 30.6, 29.8, 31.3, 32.4, 30.9, 31.8, 30.7])),
    gapSeries(23, 9, false, false, line([33.2, 34.0, 33.1, 32.4, 34.5, 35.0, 33.7, 34.1, 33.6])),
    gapSeries(15, 10, false, true, line([36.4, 37.1, 36.2, 35.8, 37.0, 38.2, 36.6, 37.5, 36.8])),
    gapSeries(2, 11, false, false, line([37.4, 38.0, 37.2, 36.5, 38.2, 39.1, 37.6, 38.3, 37.8])),
    gapSeries(9, 12, false, false, line([37.8, 38.6, 38.1, 37.2, 39.0, 40.0, 38.4, 39.1, 38.6])),
    gapSeries(28, 13, false, false, line([42.7, 43.6, 44.8, 43.1, 45.2, 46.0, 44.3, 45.4, 44.1]))
  ];
  const leaderChanges = snapshot.rawEvidence.graphMarkers
    .filter((marker) => marker.kind === 'leader-change')
    .map((marker) => ({ axisSeconds: marker.axisSeconds, previousLeaderCarIdx: 14, newLeaderCarIdx: 32 }));
  const driverChanges = snapshot.rawEvidence.graphMarkers
    .filter((marker) => marker.kind === 'driver-change')
    .map((marker) => ({ axisSeconds: marker.axisSeconds, carIdx: marker.carIdx, gapSeconds: 36.9, isReference: true, label: marker.label }));
  const pitWindows = (snapshot.rawEvidence.pitWindows || []).map((window) => ({
    entryAxisSeconds: window.entryAxisSeconds,
    exitAxisSeconds: window.exitAxisSeconds,
    carIdx: window.carIdx,
    classPosition: window.classPosition,
    isReference: window.isReference !== false,
    isActive: window.isActive === true,
    durationSeconds: window.durationSeconds,
    lap: window.lap
  }));

  return {
    ...baseModel('gap-to-leader', 'live | capture-shaped 4h graph', 'source: capture-shaped endurance gap fixture'),
    bodyKind: 'graph',
    headerItems: [{ key: 'timeRemaining', value: '03:12:48', tone: 'live' }],
    graph: {
      series,
      weather: snapshot.rawEvidence.weatherPeriods,
      leaderChanges,
      driverChanges,
      pitWindows,
      startSeconds,
      endSeconds,
      maxGapSeconds: 48,
      lapReferenceSeconds: 525.8,
      selectedSeriesCount: series.length,
      trendMetrics: gapTrendMetrics(),
      activeThreat: null,
      threatCarIdx: null,
      metricDeadbandSeconds: 0.25,
      comparisonLabel: snapshot.expected.graph.comparisonLabel,
      showGraph: true,
      showTrendMetrics: true,
      scale: {
        maxGapSeconds: 48,
        isFocusRelative: false,
        aheadSeconds: 0,
        behindSeconds: 0,
        referencePoints: series.find((item) => item.isReference).points,
        latestReferenceGapSeconds: 36.8
      }
    },
    shouldRender: true
  };
}

function gapSeries(carIdx, classPosition, isClassLeader, isReference, points) {
  return {
    carIdx,
    isReference,
    isClassLeader,
    classPosition,
    alpha: 1,
    isStickyExit: false,
    isStale: false,
    points
  };
}

function gapTrendMetrics(activeThreat = null) {
  return [
    { label: 'Last', state: 'last', primaryText: '0.0', comparisonText: '-0.2', threatText: activeThreat ? '-0.6' : '--' },
    activeThreat || { label: '5L', state: 'ready', focusGapChangeSeconds: -0.4, comparisonText: '-0.5', threatText: '--', completedReferenceLaps: 8 },
    { label: '10L', state: 'ready', focusGapChangeSeconds: -0.7, comparisonText: '-0.9', threatText: activeThreat ? '-3.5' : '--', chaser: activeThreat?.chaser || null, completedReferenceLaps: 12 },
    { label: 'Pit', state: 'pit', primaryPit: { seconds: 78, lap: 3, isActive: false }, comparisonPit: { seconds: 82, lap: 3, isActive: false }, threatPit: activeThreat ? { seconds: 80, lap: 3, isActive: false } : null },
    { label: 'PLap', state: 'pitLap', primaryPit: { seconds: 78, lap: 3, isActive: false }, comparisonPit: { seconds: 82, lap: 3, isActive: false }, threatPit: activeThreat ? { seconds: 80, lap: 3, isActive: false } : null },
    { label: 'Stint', state: 'stint', comparisonText: '3L', threatText: activeThreat ? '2L' : '--' },
    { label: 'Tire', state: 'tire', primaryTire: { shortLabel: 'D' }, comparisonTire: { shortLabel: 'D' }, threatTire: activeThreat ? { shortLabel: 'D' } : null },
    { label: 'Status', state: 'status', comparisonText: 'Track', threatText: activeThreat ? 'Track' : '--' }
  ];
}

function baseModel(overlayId, status, source) {
  return {
    overlayId,
    status,
    source,
    title: '',
    renderedTitle: '',
    rootOpacity: 1,
    bodyKind: 'metrics',
    columns: [],
    rows: [],
    metrics: [],
    metricSections: [],
    gridSections: [],
    points: [],
    headerItems: [],
    shouldRender: true
  };
}

function colorFromHex(hex, alpha = 255) {
  const value = Number.parseInt(String(hex).replace('#', ''), 16);
  return {
    red: (value >> 16) & 255,
    green: (value >> 8) & 255,
    blue: value & 255,
    alpha
  };
}

function formatLitersPerLap(value) {
  return `${Number(value).toFixed(2)} L/lap`;
}
