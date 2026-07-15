import { expect, test } from '@playwright/test';
import {
  browserOverlayApiResponse,
  browserOverlayPages,
  freshLiveSnapshot,
  renderOverlayHtml
} from './browserOverlayTestHost.js';

// This is intentionally a renderer-protocol test, not a second application
// model implementation. The companion Windows C# localhost test feeds the
// real BrowserOverlayModelFactory through every product route. Here the
// compact sequence only proves that the shared browser asset clears stale DOM
// and can render again for every supported body family.
test.describe('browser overlay visible-hidden-visible renderer protocol', () => {
  for (const pageDefinition of browserOverlayPages()) {
    const overlayId = pageDefinition.page.id;

    test(`${overlayId} clears stale DOM and restores a new model`, async ({ page }) => {
      const models = transitionModels(pageDefinition);
      let modelRequestCount = 0;
      let phase = 'visible';
      let beginHidden = false;
      // Inputs refreshes every 50 ms, so retain the hidden state long enough
      // to observe all cleared DOM/opacity assertions before restoration. The
      // radar and map use similarly fast production refresh intervals.
      const fastPollingOverlay = ['input-state', 'car-radar', 'track-map'].includes(overlayId);
      let hiddenResponsesRemaining = fastPollingOverlay ? 20 : 5;

      await page.route('**/*', async (route) => {
        const url = new URL(route.request().url());
        if (url.hostname === 'localhost' && url.pathname === pageDefinition.route) {
          await route.fulfill({
            status: 200,
            contentType: 'text/html; charset=utf-8',
            body: renderOverlayHtml(overlayId)
          });
          return;
        }

        if (url.hostname === 'localhost' && url.pathname === pageDefinition.modelRoute) {
          if (beginHidden && phase === 'visible') {
            phase = 'hidden';
          }
          const model = phase === 'visible'
            ? models[0]
            : phase === 'hidden'
              ? models[1]
              : models[2];
          modelRequestCount += 1;
          if (phase === 'hidden') {
            hiddenResponsesRemaining -= 1;
            if (hiddenResponsesRemaining <= 0) phase = 'restored';
          }
          await route.fulfill({
            status: 200,
            contentType: 'application/json; charset=utf-8',
            body: JSON.stringify({ model })
          });
          return;
        }

        if (url.hostname === 'localhost' && url.pathname === '/api/browser-source-event') {
          await route.fulfill({
            status: 200,
            contentType: 'application/json; charset=utf-8',
            body: '{"ok":true}'
          });
          return;
        }

        const payload = browserOverlayApiResponse(overlayId, url.pathname, {
          live: freshLiveSnapshot({}),
          settings: {}
        });
        if (payload) {
          await route.fulfill({
            status: 200,
            contentType: 'application/json; charset=utf-8',
            body: JSON.stringify(payload)
          });
          return;
        }

        await route.fulfill({ status: 404, contentType: 'text/plain', body: 'not found' });
      });

      await page.setViewportSize({ width: 1280, height: 720 });
      await page.goto(`http://localhost:8765${pageDefinition.route}`);

      await expect(page.locator('.header-items')).toContainText('transition-one');
      await expectRenderedBody(page, overlayId, 'transition-one');
      await expect.poll(async () => page.locator('.overlay').evaluate((element) =>
        window.getComputedStyle(element).opacity
      ), { timeout: 3500 }).toBe('1');

      beginHidden = true;
      await expect(page.locator('.header-items')).toBeEmpty({ timeout: 3500 });
      await expect(page.locator('#content')).toBeEmpty();
      await expect.poll(async () => page.locator('.overlay').evaluate((element) =>
        window.getComputedStyle(element).opacity
      ), { timeout: 3500 }).toBe('0');

      await expect(page.locator('.header-items')).toContainText('transition-restored', { timeout: 3500 });
      await expectRenderedBody(page, overlayId, 'transition-restored');
      await expect.poll(async () => page.locator('.overlay').evaluate((element) =>
        window.getComputedStyle(element).opacity
      ), { timeout: 3500 }).toBe('1');
      expect(modelRequestCount, `${overlayId} should keep polling after a hidden model`).toBeGreaterThanOrEqual(
        fastPollingOverlay ? 22 : 7);
    });
  }
});

function transitionModels(pageDefinition) {
  const baselineResponse = browserOverlayApiResponse(
    pageDefinition.page.id,
    pageDefinition.modelRoute,
    { live: freshLiveSnapshot({}), settings: {} }
  );
  const visible = visibleProtocolModel(baselineResponse.model, 'transition-one');
  const hidden = {
    ...visible,
    status: 'hidden | transition protocol',
    source: '',
    columns: [],
    rows: [],
    metrics: [],
    points: [],
    headerItems: [],
    graph: null,
    carRadar: null,
    trackMap: null,
    garageCover: null,
    streamChat: null,
    inputs: null,
    flags: null,
    gridSections: [],
    metricSections: [],
    shouldRender: false
  };
  return [visible, hidden, visibleProtocolModel(baselineResponse.model, 'transition-restored')];
}

function visibleProtocolModel(model, marker) {
  const protocolModel = {
    ...model,
    status: marker,
    source: `source: ${marker}`,
    headerItems: [{ key: 'transition', value: marker, tone: 'info' }],
    shouldRender: true,
    rootOpacity: 1
  };
  switch (model.overlayId) {
    case 'standings':
    case 'relative':
      protocolModel.columns = [{ key: 'transition', label: 'Transition', width: 160 }];
      protocolModel.rows = [{
        cells: [marker],
        isReference: true,
        isClassHeader: false,
        isPit: false,
        isPartial: false,
        isPendingGrid: false
      }];
      break;
    case 'fuel-calculator':
    case 'session-weather':
    case 'pit-service':
      protocolModel.bodyKind = 'metrics';
      protocolModel.metrics = [{ label: 'Transition', value: marker, tone: 'info', segments: [] }];
      protocolModel.metricSections = [];
      protocolModel.gridSections = [];
      break;
    case 'gap-to-leader':
      protocolModel.bodyKind = 'graph';
      protocolModel.points = [2, 1];
      protocolModel.graph = {
        ...(model.graph || {}),
        showGraph: true,
        showTrendMetrics: false,
        series: [{ label: marker, points: [{ x: 0, y: 2 }, { x: 1, y: 1 }] }]
      };
      break;
    case 'input-state':
      protocolModel.inputs = {
        hasContent: true,
        hasGraph: false,
        hasRail: true,
        showThrottle: true,
        throttle: 0.5,
        trace: []
      };
      break;
    case 'car-radar':
      protocolModel.carRadar = {
        ...(model.carRadar || {}),
        renderModel: {
          shouldRender: true,
          width: 180,
          height: 260,
          fadeInMilliseconds: 1,
          fadeOutMilliseconds: 1,
          background: { x: 0, y: 0, width: 180, height: 260, fill: '#101820', stroke: '#00e8ff', strokeWidth: 1 },
          rings: [{ x: 30, y: 30, width: 120, height: 200, fill: null, stroke: '#ffffff', strokeWidth: 1 }],
          cars: [{ x: 80, y: 80, width: 20, height: 36, radius: 4, fill: '#00e8ff', stroke: '#ffffff', strokeWidth: 1 }],
          labels: [{ x: 90, y: 130, text: marker, fill: '#ffffff', size: 10 }]
        }
      };
      break;
    case 'track-map':
      protocolModel.trackMap = {
        ...(model.trackMap || {}),
        renderModel: {
          width: 360,
          height: 360,
          primitives: [{ kind: 'line', points: [{ x: 20, y: 20 }, { x: 340, y: 340 }], stroke: '#00e8ff', strokeWidth: 4 }],
          markers: []
        }
      };
      break;
    case 'flags':
      protocolModel.flags = {
        flags: [{ kind: 'green', category: 'green', label: 'Green', tone: 'success' }],
        isWaiting: false
      };
      break;
    case 'garage-cover':
      protocolModel.garageCover = {
        ...(model.garageCover || {}),
        shouldCover: true,
        browserSettings: model.garageCover?.browserSettings || { hasImage: false, previewVisible: true }
      };
      break;
    case 'stream-chat':
      protocolModel.streamChat = {
        ...(model.streamChat || {}),
        rows: [{ name: 'TMR', text: marker, kind: 'system' }]
      };
      break;
  }
  return protocolModel;
}

async function expectRenderedBody(page, overlayId, marker) {
  const locator = overlayId === 'standings' || overlayId === 'relative'
    ? page.locator('#content table')
    : overlayId === 'fuel-calculator' || overlayId === 'session-weather' || overlayId === 'pit-service'
      ? page.locator('#content .metric')
      : overlayId === 'gap-to-leader'
        ? page.locator('#content .model-graph')
        : overlayId === 'input-state'
          ? page.locator('#content .input-rail')
          : overlayId === 'car-radar'
            ? page.locator('#content .radar-v2')
            : overlayId === 'track-map'
              ? page.locator('#content svg[aria-label="Track map"]')
              : overlayId === 'flags'
                ? page.locator('#content .flags-v2')
                : overlayId === 'garage-cover'
                  ? page.locator('#content .garage-cover')
                  : page.locator('#content .chat-line');

  await expect(locator).toBeVisible({ timeout: 3500 });
  if (overlayId === 'standings' || overlayId === 'relative'
    || overlayId === 'fuel-calculator' || overlayId === 'session-weather' || overlayId === 'pit-service'
    || overlayId === 'stream-chat') {
    await expect(page.locator('#content')).toContainText(marker);
  }
}
