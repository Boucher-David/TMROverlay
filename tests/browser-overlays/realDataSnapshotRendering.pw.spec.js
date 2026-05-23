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
  flags: readSnapshot('flags-meatball-local-policy.json'),
  fuel: readSnapshot('fuel-measured-burn-real-data.json'),
  pitService: readSnapshot('pit-service-refuel-pit-window-real-data.json')
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
    await expect(page.locator('#content')).toContainText('Waiting for live rows.');
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

  test('renders Track Map focus marker and hides practice pending-grid markers', async ({ page }) => {
    const snapshot = snapshots.trackMap;
    const model = trackMapModel(snapshot);
    const { requests, browserSourceEvents } = await installOverlayRoutes(page, 'track-map', model);

    await page.setViewportSize({ width: 360, height: 360 });
    await page.goto('http://localhost:8765/overlays/track-map');

    await expect(page.locator('.track svg')).toBeVisible();
    const markerLabels = await page.locator('.track svg text').allTextContents();
    expect(markerLabels.sort()).toEqual(['10', '22']);
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
    columns: [
      { label: 'Pos', dataKey: 'classPosition', width: 35, alignment: 'right' },
      { label: 'CAR', dataKey: 'carNumber', width: 50, alignment: 'center' },
      { label: 'Driver', dataKey: 'driver', width: 250, alignment: 'left' }
    ],
    rows: [],
    shouldRender: true
  };
}

function trackMapModel(snapshot) {
  const rowsByCarIdx = new Map(snapshot.rawEvidence.timingRows.map((row) => [row.carIdx, row]));
  const markerCarIdxs = snapshot.expected.practiceMarkerPolicy.visibleCarIdxs;
  const markers = markerCarIdxs.map((carIdx) => {
    const row = rowsByCarIdx.get(carIdx);
    const isFocus = carIdx === snapshot.expected.focusMarker.carIdx;
    return {
      carIdx,
      label: String(carIdx),
      x: isFocus ? 230 : 130,
      y: isFocus ? 120 : 220,
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
