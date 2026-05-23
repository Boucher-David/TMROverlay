import { expect, test } from '@playwright/test';
import {
  browserOverlayApiResponse,
  freshLiveSnapshot,
  renderOverlayHtml
} from './browserOverlayTestHost.js';
import {
  overlayGeometry,
  renderAppValidatorReviewHtml,
  renderInstallerReviewHtml,
  renderSettingsGeneralReviewHtml,
  settingsBrowserSourceSize
} from './browserOverlayAssets.js';
import { startReviewServer } from './reviewServerTestHost.js';

test.describe('browser overlay Playwright integration', () => {
  test('renders standings in a real browser layout without horizontal overflow', async ({ page }) => {
    const requests = await installBrowserOverlayRoutes(page, 'standings', {
      live: freshLiveSnapshot({}),
      model: standingsDisplayModel()
    });

    await page.setViewportSize({ width: 692, height: 520 });
    await page.goto('http://localhost:8765/overlays/standings');

    const rows = page.locator('tbody tr');
    await expect(rows).toHaveCount(6);
    await expect(page.locator('#status')).toHaveCount(0);
    await expect(page.locator('.header-items')).toHaveText('06:37:08');
    await expect(rows.nth(4)).toHaveClass(/focus/);

    const overlayBox = await page.locator('.overlay').boundingBox();
    expect(overlayBox?.width).toBeGreaterThan(480);
    expect(overlayBox?.height).toBeGreaterThan(180);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
    expect(requests).toContain('/api/overlay-model/standings');
    expect(requests).not.toContain('/api/snapshot');
  });

  test('fits standings rows inside advertised OBS browser source height', async ({ page }) => {
    const model = standingsDisplayModel({ rows: standingsRecommendedBrowserSourceRows() });
    const sourceSize = settingsBrowserSourceSize('standings', {
      carsInClass: 14,
      otherClassRows: 2
    }, 'race');

    await installBrowserOverlayRoutes(page, 'standings', {
      live: freshLiveSnapshot({}),
      model
    });

    await page.setViewportSize({ width: sourceSize.baseWidth, height: sourceSize.baseHeight });
    await page.goto('http://localhost:8765/overlays/standings?preview=race');

    await expect(page.locator('tbody tr')).toHaveCount(model.rows.length);
    const fit = await page.evaluate(() => {
      const overlay = document.querySelector('.overlay')?.getBoundingClientRect();
      const lastRow = document.querySelector('tbody tr:last-child')?.getBoundingClientRect();
      return {
        lastRowBottom: lastRow?.bottom ?? 0,
        overlayBottom: overlay?.bottom ?? 0,
        viewportHeight: window.innerHeight
      };
    });
    expect(fit.overlayBottom).toBeLessThanOrEqual(fit.viewportHeight + 1);
    expect(fit.lastRowBottom).toBeLessThanOrEqual(fit.viewportHeight + 1);
  });

  test('uses native normal table height constants in browser layout', async ({ page }) => {
    await installBrowserOverlayRoutes(page, 'standings', {
      live: freshLiveSnapshot({}),
      model: standingsDisplayModel()
    });

    await page.setViewportSize({ width: 692, height: 520 });
    await page.goto('http://localhost:8765/overlays/standings');

    await expect(page.locator('tbody tr')).toHaveCount(6);
    await expectElementHeight(page.locator('thead th').first(), 25);
    await expectElementHeight(page.locator('tbody tr:not(.class-header) td').first(), 28);
    await expectElementHeight(page.locator('tbody tr.class-header td').first(), 35);
    await expectElementHeight(page.locator('.class-header-band').first(), 24);

    const tableOffsets = await page.evaluate(() => {
      const table = document.querySelector('table').getBoundingClientRect();
      const header = document.querySelector('thead th').getBoundingClientRect();
      const firstBodyRow = document.querySelector('tbody tr td').getBoundingClientRect();
      const band = document.querySelector('.class-header-band').getBoundingClientRect();
      return {
        headerTop: header.top - table.top,
        bodyTop: firstBodyRow.top - table.top,
        classBandTop: band.top - firstBodyRow.top
      };
    });
    expect(tableOffsets.headerTop).toBeCloseTo(5, 0);
    expect(tableOffsets.bodyTop).toBeCloseTo(35, 0);
    expect(tableOffsets.classBandTop).toBeCloseTo(11, 0);
  });

  test('uses configured table width for compact standings columns', async ({ page }) => {
    const full = standingsDisplayModel();
    const driverOnly = standingsDisplayModel({
      columns: full.columns.filter((column) => column.dataKey === 'driver'),
      rows: full.rows.map((row) => row.cells.length > 0
        ? { ...row, cells: [row.cells[2]] }
        : row)
    });
    await installBrowserOverlayRoutes(page, 'standings', {
      live: freshLiveSnapshot({}),
      model: driverOnly
    });

    await page.setViewportSize({ width: 320, height: 360 });
    await page.goto('http://localhost:8765/overlays/standings');

    await expect(page.locator('thead th')).toHaveText(['Driver']);
    const overlayBox = await page.locator('.overlay').boundingBox();
    expect(overlayBox?.width).toBeCloseTo(284, 0);
  });

  test('uses native relative table row height in browser layout', async ({ page }) => {
    await installBrowserOverlayRoutes(page, 'relative', {
      live: freshLiveSnapshot({}),
      model: relativeDisplayModel()
    });

    await page.setViewportSize({ width: 360, height: 308 });
    await page.goto('http://localhost:8765/overlays/relative');

    await expect(page.locator('tbody tr')).toHaveCount(3);
    await expectElementHeight(page.locator('thead th').first(), 25);
    await expectElementHeight(page.locator('tbody tr:not(.class-header) td').first(), 26);
  });

  test('applies production model root opacity in localhost overlay routes', async ({ page }) => {
    await installBrowserOverlayRoutes(page, 'standings', {
      live: freshLiveSnapshot({}),
      model: {
        ...standingsDisplayModel(),
        rootOpacity: 0.5
      }
    });

    await page.setViewportSize({ width: 692, height: 520 });
    await page.goto('http://localhost:8765/overlays/standings');

    await expect(page.locator('tbody tr')).toHaveCount(6);
    await expect(page.locator('.overlay')).toHaveCSS('opacity', '0.5');
  });

  test('renders stream chat unavailable state without telemetry fade', async ({ page }) => {
    const requests = await installBrowserOverlayRoutes(page, 'stream-chat', {
      live: {
        isConnected: false,
        isCollecting: false,
        lastUpdatedAtUtc: null,
        sequence: 0,
        models: {}
      },
      settings: {
        provider: 'none',
        isConfigured: false,
        streamlabsWidgetUrl: null,
        twitchChannel: null,
        status: 'not_configured'
      }
    });

    await page.setViewportSize({ width: 380, height: 520 });
    await page.goto('http://localhost:8765/overlays/stream-chat');

    await expect(page.locator('.chat-line')).toHaveCount(1);
    await expect(page.locator('.chat-name')).toHaveText('TMR');
    await expect(page.locator('.chat-text')).toHaveText('Choose Streamlabs or Twitch in the Stream Chat settings tab.');
    await expect(page.locator('.header')).toBeVisible();
    await expect(page.locator('.title')).toHaveCount(0);
    await expect(page.locator('.header')).not.toContainText('Stream Chat');
    await expect(page).toHaveTitle('TMR Stream Chat');
    await expect(page.locator('.overlay')).toHaveCSS('opacity', '1');
    const overlayBox = await page.locator('.overlay').boundingBox();
    expect(overlayBox?.width).toBe(380);
    expect(overlayBox?.height).toBe(520);
    const geometry = overlayGeometry().streamChat;
    const rowBox = await page.locator('.chat-line').boundingBox();
    expect(rowBox?.x).toBeCloseTo(1 + geometry.contentHorizontalPadding, 0);
    expect(rowBox?.width).toBeCloseTo(geometry.overlayWidth - 2 - geometry.contentHorizontalPadding * 2, 0);
    expect(requests).toContain('/api/overlay-model/stream-chat');
  });

  test('renders latest stream chat rows inside narrow browser sources', async ({ page }) => {
    await installBrowserOverlayRoutes(page, 'stream-chat', {
      live: {
        isConnected: false,
        isCollecting: false,
        lastUpdatedAtUtc: null,
        sequence: 0,
        models: {}
      },
      settings: {
        provider: 'none',
        isConfigured: false,
        streamlabsWidgetUrl: null,
        twitchChannel: null,
        status: 'replay_static',
        replayRows: Array.from({ length: 12 }, (_, index) => ({
          name: `viewer${index + 1}`,
          text: `message ${index + 1}`,
          kind: 'message'
        }))
      }
    });

    await page.setViewportSize({ width: 360, height: 260 });
    await page.goto('http://localhost:8765/overlays/stream-chat');

    await expect(page.locator('.chat-line')).toHaveCount(4);
    await expect(page.locator('.chat-text').last()).toHaveText('message 12');
    await expect(page.locator('.chat-text').first()).toHaveText('message 9');
    await expect(page.locator('.chat-text', { hasText: /^message 1$/ })).toHaveCount(0);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  });

  test('wraps long stream chat messages and prunes older rows to fit', async ({ page }) => {
    await installBrowserOverlayRoutes(page, 'stream-chat', {
      live: {
        isConnected: false,
        isCollecting: false,
        lastUpdatedAtUtc: null,
        sequence: 0,
        models: {}
      },
      settings: {
        provider: 'none',
        isConfigured: false,
        streamlabsWidgetUrl: null,
        twitchChannel: null,
        status: 'replay_static',
        replayRows: [
          { name: 'viewer1', text: 'older message', kind: 'message' },
          { name: 'viewer2', text: 'another older message', kind: 'message' },
          {
            name: 'viewer3',
            text: 'this is a much longer Twitch chat message that should wrap onto multiple lines instead of clipping the lower half of the text or overflowing horizontally',
            kind: 'message'
          }
        ]
      }
    });

    await page.setViewportSize({ width: 300, height: 170 });
    await page.goto('http://localhost:8765/overlays/stream-chat');

    const lastRowHeight = await page.locator('.chat-line').last().evaluate((el) => el.getBoundingClientRect().height);
    expect(lastRowHeight).toBeGreaterThan(44);
    expect(await page.evaluate(() => {
      const chat = document.querySelector('.chat');
      return chat.scrollHeight <= chat.clientHeight + 1;
    })).toBe(true);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  });

  test('renders twitch stream chat metadata without replacing row styling', async ({ page }) => {
    await installBrowserOverlayRoutes(page, 'stream-chat', {
      live: {
        isConnected: false,
        isCollecting: false,
        lastUpdatedAtUtc: null,
        sequence: 0,
        models: {}
      },
      settings: {
        provider: 'twitch',
        isConfigured: true,
        streamlabsWidgetUrl: null,
        twitchChannel: 'techmatesracing',
        status: 'configured_twitch',
        replayRows: [{
          name: 'viewer42',
          text: 'Kappa',
          kind: 'message',
          source: 'twitch',
          authorColorHex: '#62C7FF',
          metadata: ['100 bits'],
          badges: [{ id: 'subscriber', version: '12', label: 'sub 12' }],
          segments: [{ kind: 'emote', text: 'Kappa', imageUrl: 'https://static-cdn.jtvnw.net/emoticons/v2/25/default/dark/1.0' }]
        }]
      }
    });

    await page.setViewportSize({ width: 380, height: 520 });
    await page.goto('http://localhost:8765/overlays/stream-chat');

    await expect(page.locator('.chat-name')).toHaveCSS('color', 'rgb(98, 199, 255)');
    await expect(page.locator('.chat-chip')).toHaveCount(1);
    await expect(page.locator('.chat-badge')).toHaveText('sub');
    await expect(page.locator('.chat-badge')).toHaveAttribute('title', 'subscriber 12');
    await expect(page.locator('.chat-emote')).toHaveAttribute('alt', 'Kappa');
    await expect(page.locator('.chat-line')).toHaveCSS('background-color', /rgb\(18, 31, 60\)|rgba\(18, 31, 60/);
  });

  test('keeps input graph smoothing inside each trace segment', async ({ page }) => {
    await page.addInitScript(() => {
      window.__tmrBezierCalls = [];
      const originalMoveTo = CanvasRenderingContext2D.prototype.moveTo;
      const originalLineTo = CanvasRenderingContext2D.prototype.lineTo;
      const originalBezierCurveTo = CanvasRenderingContext2D.prototype.bezierCurveTo;
      CanvasRenderingContext2D.prototype.moveTo = function patchedMoveTo(x, y) {
        this.__tmrCurrentPoint = { x, y };
        return originalMoveTo.call(this, x, y);
      };
      CanvasRenderingContext2D.prototype.lineTo = function patchedLineTo(x, y) {
        this.__tmrCurrentPoint = { x, y };
        return originalLineTo.call(this, x, y);
      };
      CanvasRenderingContext2D.prototype.bezierCurveTo = function patchedBezierCurveTo(c1x, c1y, c2x, c2y, x, y) {
        window.__tmrBezierCalls.push({
          startY: this.__tmrCurrentPoint?.y,
          c1y,
          c2y,
          endY: y
        });
        this.__tmrCurrentPoint = { x, y };
        return originalBezierCurveTo.call(this, c1x, c1y, c2x, c2y, x, y);
      };
    });

    await installBrowserOverlayRoutes(page, 'input-state', {
      live: [0.12, 1, 1, 0.12, 0.88, 0, 0, 0.88].map((throttle, index) =>
        inputStateLiveSnapshot(index, throttle)),
      settings: {
        showThrottle: true,
        showBrake: true,
        showClutch: true,
        showSteering: false,
        showGear: false,
        showSpeed: false
      }
    });

    await page.setViewportSize({ width: 460, height: 220 });
    await page.goto('http://localhost:8765/overlays/input-state');
    await expect(page.locator('.input-graph')).toBeVisible();
    await expect.poll(
      async () => page.evaluate(() => window.__tmrBezierCalls?.length ?? 0),
      { timeout: 3500 }
    ).toBeGreaterThanOrEqual(12);

    const violations = await page.evaluate(() => {
      const tolerance = 0.001;
      return (window.__tmrBezierCalls || []).filter((call) => {
        if (!Number.isFinite(call.startY) || !Number.isFinite(call.endY)) {
          return false;
        }

        const min = Math.min(call.startY, call.endY) - tolerance;
        const max = Math.max(call.startY, call.endY) + tolerance;
        return call.c1y < min || call.c1y > max || call.c2y < min || call.c2y > max;
      });
    });
    expect(violations).toEqual([]);
  });

  test('shrinks input overlay width when only the right rail is enabled', async ({ page }) => {
    await installBrowserOverlayRoutes(page, 'input-state', {
      live: inputStateLiveSnapshot(0, 0.72),
      settings: {
        showThrottleTrace: false,
        showBrakeTrace: false,
        showClutchTrace: false,
        showThrottle: true,
        showBrake: true,
        showClutch: false,
        showSteering: true,
        showGear: true,
        showSpeed: true
      }
    });

    await page.setViewportSize({ width: 520, height: 260 });
    await page.goto('http://localhost:8765/overlays/input-state');

    await expect(page.locator('.input-rail')).toBeVisible();
    await expect(page.locator('.input-graph')).toHaveCount(0);
    await expect(page.locator('.overlay')).toHaveClass(/input-rail-only/);

    const overlayWidth = await boundingBoxWidth(page.locator('.overlay'));
    expect(overlayWidth).toBeGreaterThanOrEqual(270);
    expect(overlayWidth).toBeLessThanOrEqual(282);

    const railWidth = await boundingBoxWidth(page.locator('.input-rail'));
    expect(railWidth).toBeGreaterThan(220);
  });

  test('shrinks input overlay width when only the graph is enabled', async ({ page }) => {
    await installBrowserOverlayRoutes(page, 'input-state', {
      live: inputStateLiveSnapshot(0, 0.72),
      settings: {
        showThrottleTrace: true,
        showBrakeTrace: true,
        showClutchTrace: false,
        showThrottle: false,
        showBrake: false,
        showClutch: false,
        showSteering: false,
        showGear: false,
        showSpeed: false
      }
    });

    await page.setViewportSize({ width: 520, height: 260 });
    await page.goto('http://localhost:8765/overlays/input-state');

    await expect(page.locator('.input-graph')).toBeVisible();
    await expect(page.locator('.input-rail')).toHaveCount(0);
    await expect(page.locator('.overlay')).toHaveClass(/input-graph-only/);

    const overlayWidth = await boundingBoxWidth(page.locator('.overlay'));
    expect(overlayWidth).toBeGreaterThanOrEqual(374);
    expect(overlayWidth).toBeLessThanOrEqual(386);
  });

  test('keeps input steering wheel compact inside the browser rail', async ({ page }) => {
    await installBrowserOverlayRoutes(page, 'input-state', {
      live: inputStateLiveSnapshot(0, 0.72)
    });

    await page.setViewportSize({ width: 520, height: 260 });
    await page.goto('http://localhost:8765/overlays/input-state');

    await expect(page.locator('.input-wheel svg')).toBeVisible();
    const wheel = await page.locator('.input-wheel svg').boundingBox();
    const rail = await page.locator('.input-rail').boundingBox();

    expect(wheel).not.toBeNull();
    expect(rail).not.toBeNull();
    expect(wheel?.width).toBeGreaterThan(20);
    expect(wheel?.height).toBeGreaterThan(20);
    expect(wheel?.width).toBeLessThanOrEqual(40);
    expect(wheel?.height).toBeLessThanOrEqual(40);
    expect(wheel.x).toBeGreaterThanOrEqual(rail.x);
    expect(wheel.x + wheel.width).toBeLessThanOrEqual(rail.x + rail.width);
  });

  test('keeps input steering wheel visible at minimum scale', async ({ page }) => {
    await installBrowserOverlayRoutes(page, 'input-state', {
      live: inputStateLiveSnapshot(0, 0.72)
    });

    await page.setViewportSize({ width: 328, height: 172 });
    await page.goto('http://localhost:8765/overlays/input-state');

    await expect(page.locator('.input-wheel svg')).toBeVisible();
    const wheel = await page.locator('.input-wheel svg').boundingBox();
    const rail = await page.locator('.input-rail').boundingBox();
    const graph = await page.locator('.input-graph-panel').boundingBox();

    expect(wheel).not.toBeNull();
    expect(rail).not.toBeNull();
    expect(graph).not.toBeNull();
    expect(wheel?.width).toBeGreaterThanOrEqual(14);
    expect(wheel?.height).toBeGreaterThanOrEqual(14);
    expect(wheel?.width).toBeLessThanOrEqual(32);
    expect(wheel?.height).toBeLessThanOrEqual(32);
    expect(wheel.x).toBeGreaterThanOrEqual(rail.x);
    expect(wheel.y).toBeGreaterThanOrEqual(rail.y);
    expect(wheel.x + wheel.width).toBeLessThanOrEqual(rail.x + rail.width);
    expect(wheel.y + wheel.height).toBeLessThanOrEqual(rail.y + rail.height);
    expect(graph.x + graph.width).toBeLessThanOrEqual(rail.x - 8);
  });

  test('renders fuel practice as compact range and usage sections', async ({ page }) => {
    const geometry = overlayGeometry().metricRows;
    const expectedHeight = fuelCalculatorHeightFromGeometry(2, 2, geometry);
    await installBrowserOverlayRoutes(page, 'fuel-calculator', {
      live: freshLiveSnapshot({}),
      model: fuelPracticeDisplayModel()
    });

    await page.setViewportSize({ width: 520, height: 260 });
    await page.goto('http://localhost:8765/overlays/fuel-calculator?preview=practice');

    await expect(page.locator('.overlay')).toHaveClass(/fuel-non-race/);
    await expect(page.locator('.metric-section-title')).toHaveText(['Fuel Range', 'Fuel Usage']);
    await expect(page.locator('#content')).toContainText('Practice Usage');
    await expect(page.locator('#content')).not.toContainText('Race Information');
    await expect(page.locator('#content')).not.toContainText('Stint Targets');
    await expectElementHeight(page.locator('.metric.segmented').first(), geometry.segmentedRowHeight);
    await expectElementHeight(page.locator('.metric-section-title').first(), geometry.sectionTitleHeight);
    await expect(page.locator('.metric-section-title').first()).toHaveCSS('margin-bottom', `${geometry.sectionTitleBottomGap}px`);
    const fuelContentHeight = await page.locator('.overlay').evaluate((overlay) =>
      window.getComputedStyle(overlay).getPropertyValue('--fuel-content-height').trim()
    );
    expect(fuelContentHeight).toBe(`${expectedHeight}px`);

    const overlay = await page.locator('.overlay').boundingBox();
    expect(overlay?.width).toBe(503);
    expect(overlay?.height).toBe(expectedHeight);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  });

  test('uses metricRows geometry constants for browser metric overlay rows and tire grids', async ({ page }) => {
    const geometry = overlayGeometry().metricRows;
    await installBrowserOverlayRoutes(page, 'session-weather', {
      live: freshLiveSnapshot({}),
      model: sessionWeatherMetricDisplayModel()
    });

    await page.setViewportSize({ width: 520, height: 520 });
    await page.goto('http://localhost:8765/overlays/session-weather?preview=race');

    await expectElementHeight(page.locator('.metric:not(.segmented)').first(), geometry.plainRowHeight);
    await expectElementHeight(page.locator('.metric.segmented').first(), geometry.segmentedRowHeight);
    await expectElementHeight(page.locator('.metric.directional').first(), geometry.directionalRowHeight);
    await expectElementHeight(page.locator('.metric-section-title').first(), geometry.sectionTitleHeight);
    const sessionSectionGap = await page.locator('.metric-section').evaluateAll((sections) => {
      const first = sections[0].getBoundingClientRect();
      const second = sections[1].getBoundingClientRect();
      return second.top - first.bottom;
    });
    expect(sessionSectionGap).toBeCloseTo(geometry.sectionGap, 0);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  });

  test('sizes session weather missing review fixture from effective weather-off content', async ({ page }) => {
    const reviewServer = await startReviewServer();
    const geometry = overlayGeometry().metricRows;
    const expectedHeight = geometry.minimumSimpleTelemetryHeight
      + Math.round((496 - geometry.minimumSimpleTelemetryHeight) * ((12 - 1) / (26 - 1)));
    try {
      const modelResponse = await reviewServer.getJson('/api/overlay-model/session-weather?preview=race&fixture=session-weather-missing');
      expect(modelResponse.model.metricSections).toHaveLength(1);
      expect(modelResponse.model.metricSections[0].title).toBe('Session');
      expect(modelResponse.model.effectiveSettings.rendered.browserSource).toMatchObject({
        baseWidth: 464,
        baseHeight: expectedHeight,
        width: 464,
        height: expectedHeight
      });

      await page.setViewportSize({ width: 520, height: 520 });
      await page.goto(`${reviewServer.baseUrl}/review/overlays/session-weather?preview=race&fixture=session-weather-missing`);

      await expect(page.locator('.metric-section-title')).toHaveText(['Session']);
      const overlayBox = await page.locator('.overlay').boundingBox();
      expect(overlayBox?.width).toBe(464);
      expect(overlayBox?.height).toBe(expectedHeight);
    } finally {
      await reviewServer.stop();
    }
  });

  test('uses metricRows geometry constants for browser pit service metric and grid rows', async ({ page }) => {
    const geometry = overlayGeometry().metricRows;
    await installBrowserOverlayRoutes(page, 'pit-service', {
      live: freshLiveSnapshot({}),
      model: pitServiceMetricDisplayModel()
    });

    await page.setViewportSize({ width: 560, height: 740 });
    await page.goto('http://localhost:8765/overlays/pit-service?preview=race');

    await expectElementHeight(page.locator('.metric.segmented').first(), geometry.segmentedRowHeight);
    await expectElementHeight(page.locator('.tire-grid-head').first(), geometry.metricGridHeaderHeight);
    await expectElementHeight(page.locator('.tire-grid-row').first(), geometry.metricGridRowHeight);
    const gridSectionGap = await page.locator('.metric-section').evaluateAll((sections) => {
      const metrics = sections[0].getBoundingClientRect();
      const grid = sections[1].getBoundingClientRect();
      return grid.top - metrics.bottom;
    });
    expect(gridSectionGap).toBeCloseTo(geometry.sectionGap, 0);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  });

  test('application browser source sizes use metricRows geometry contract for metric overlays', async ({ page }) => {
    const geometry = overlayGeometry().metricRows;
    const tireAnalysisOff = Object.fromEntries([
      'Compound',
      'Change request',
      'Set limit',
      'Sets available',
      'Sets used',
      'Pressure',
      'Temperature',
      'Wear',
      'Distance'
    ].map((label) => [label, false]));
    await page.route('**/*', async (route) => {
      const url = new URL(route.request().url());
      if (url.hostname === 'localhost' && url.pathname === '/review/app') {
        await route.fulfill({
          status: 200,
          contentType: 'text/html; charset=utf-8',
          body: renderAppValidatorReviewHtml({
            previewMode: 'practice',
            selectedTab: url.searchParams.get('tab') || 'fuel-calculator',
            selectedRegion: 'general',
            reviewState: {
              overlays: {
                'pit-service': {
                  content: tireAnalysisOff
                }
              }
            }
          })
        });
        return;
      }

      await route.fulfill({
        status: 404,
        contentType: 'text/plain; charset=utf-8',
        body: 'not found'
      });
    });

    await page.setViewportSize({ width: 1280, height: 720 });
    await page.goto('http://localhost:8765/review/app?preview=practice&tab=fuel-calculator&region=general');

    await expect(page.locator('[data-evidence-key="fuel-calculator.browser-source.size.value"]'))
      .toHaveText(`OBS size 503 x ${fuelCalculatorHeightFromGeometry(2, 2, geometry)}`);

    await page.getByRole('link', { name: 'Session / Weather' }).click();
    await expect(page.locator('[data-evidence-key="session-weather.browser-source.size.value"]'))
      .toHaveText(`OBS size ${Math.min(464, geometry.minimumSimpleTelemetryWidth)} x ${Math.max(geometry.minimumSimpleTelemetryHeight, 496 - geometry.nonRaceSimpleTelemetryHeightReduction)}`);

    await page.getByRole('link', { name: 'Pit Service' }).click();
    await expect(page.locator('[data-evidence-key="pit-service.browser-source.size.value"]'))
      .toHaveText(`OBS size ${geometry.pitServiceMetricOnlyWidth} x ${pitServiceMetricOnlyHeightFromGeometry(geometry)}`);
  });

  test('updates table chrome and columns while localhost overlay stays mounted', async ({ page }) => {
    const initial = standingsDisplayModel({
      headerItems: [{ key: 'timeRemaining', value: '06:37:08', tone: 'success' }]
    });
    const compact = standingsWithoutPitColumn({
      headerItems: [{ key: 'timeRemaining', value: '00:42', tone: 'warning' }]
    });
    await installBrowserOverlayRoutes(page, 'standings', {
      live: freshLiveSnapshot({}),
      model: [initial, compact]
    });

    await page.setViewportSize({ width: 692, height: 520 });
    await page.goto('http://localhost:8765/overlays/standings');

    await expect(page.locator('thead th')).toHaveText(['Pos', 'CAR', 'Driver', 'GAP', 'INT', 'FAST', 'LAST', 'PIT']);
    await expect(page.locator('.header-item')).toHaveAttribute('data-tone', 'success');
    await expect(page.locator('tbody tr').last()).toContainText('IN');

    await expect.poll(async () => page.locator('.header-item').getAttribute('data-tone'), {
      timeout: 3500
    }).toBe('warning');
    await expect(page.locator('.header-item')).toHaveAttribute('data-key', 'timeRemaining');
    await expect(page.locator('.header-item')).toHaveText('00:42');
    await expect(page.locator('thead th')).toHaveText(['Pos', 'CAR', 'Driver', 'GAP', 'INT', 'FAST', 'LAST']);
    await expect(page.locator('tbody tr').last()).not.toContainText('IN');
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  });

  test('clears stale rendered DOM when localhost model becomes product-hidden', async ({ page }) => {
    await installBrowserOverlayRoutes(page, 'standings', {
      live: freshLiveSnapshot({}),
      model: [
        standingsDisplayModel(),
        hiddenDisplayModel('standings', 'disabled by settings')
      ]
    });

    await page.setViewportSize({ width: 692, height: 520 });
    await page.goto('http://localhost:8765/overlays/standings');

    await expect(page.locator('tbody tr')).toHaveCount(6);
    await expect(page.locator('.header-item')).toHaveCount(1);

    await expect.poll(async () => page.locator('.overlay').evaluate((element) =>
      window.getComputedStyle(element).opacity
    ), { timeout: 3500 }).toBe('0');
    await expect(page.locator('tbody tr')).toHaveCount(0);
    await expect(page.locator('thead th')).toHaveCount(0);
    await expect(page.locator('.header-item')).toHaveCount(0);
    await expect(page.locator('#content')).toBeEmpty();
  });

  test('clears stale input graph and rail when all input content becomes disabled', async ({ page }) => {
    await installBrowserOverlayRoutes(page, 'input-state', {
      live: inputStateLiveSnapshot(0, 0.72),
      settings: [
        {},
        {
          showThrottleTrace: false,
          showBrakeTrace: false,
          showClutchTrace: false,
          showThrottle: false,
          showBrake: false,
          showClutch: false,
          showSteering: false,
          showGear: false,
          showSpeed: false
        }
      ]
    });

    await page.setViewportSize({ width: 520, height: 260 });
    await page.goto('http://localhost:8765/overlays/input-state');

    await expect(page.locator('.input-graph')).toBeVisible();
    await expect(page.locator('.input-rail')).toBeVisible();

    await expect.poll(async () => page.locator('.overlay').evaluate((element) =>
      element.classList.contains('input-empty')
    ), { timeout: 3500 }).toBe(true);
    await expect(page.locator('.input-graph')).toHaveCount(0);
    await expect(page.locator('.input-rail')).toHaveCount(0);
    await expect(page.locator('.empty')).toHaveCount(0);
    await expect(page.locator('#content')).toBeEmpty();
    await expect.poll(async () => page.locator('.overlay').evaluate((element) =>
      window.getComputedStyle(element).opacity
    ), { timeout: 3500 }).toBe('0');
  });

  test('renders General settings preview controls without forcing hidden overlays', async ({ page }) => {
    await page.route('**/*', async (route) => {
      const url = new URL(route.request().url());
      if (url.hostname === 'localhost' && url.pathname === '/review/settings/general') {
        await route.fulfill({
          status: 200,
          contentType: 'text/html; charset=utf-8',
          body: renderSettingsGeneralReviewHtml({ previewMode: 'off' })
        });
        return;
      }

      await route.fulfill({
        status: 404,
        contentType: 'text/plain; charset=utf-8',
        body: 'not found'
      });
    });

    await page.setViewportSize({ width: 1280, height: 720 });
    await page.goto('http://localhost:8765/review/settings/general');

    await expect(page.locator('h1')).toHaveText('General');
    await expect(page.locator('.sidebar-tab')).toHaveCount(14);
    await expect(page.locator('.sidebar-tab.active')).toHaveText('General');
    await expect(page.getByText('Preview off')).toBeVisible();
    await expect(page.locator('.overlay-frame')).toHaveCount(0);
    await expect(page.getByRole('button', { name: 'Race' })).toHaveAttribute('aria-pressed', 'false');

    await page.getByRole('button', { name: 'Race' }).click();

    await expect(page.getByText('Race preview active')).toBeVisible();
    await expect(page.getByRole('button', { name: 'Race' })).toHaveAttribute('aria-pressed', 'true');
    await expect(page.locator('.body-lines')).toContainText('Hidden overlays stay hidden; Stream Chat is not forced open.');
    await expect.poll(async () => page.locator('.preview-panel').evaluate((panel) => {
      const bodyLines = panel.querySelector('.body-lines');
      if (!bodyLines) return false;

      const panelRect = panel.getBoundingClientRect();
      const bodyLinesRect = bodyLines.getBoundingClientRect();
      return panel.scrollHeight <= panel.clientHeight && bodyLinesRect.bottom <= panelRect.bottom;
    }), { timeout: 3500 }).toBe(true);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  });

  test('renders application settings review with production-style overlay tabs', async ({ page }) => {
    await page.route('**/*', async (route) => {
      const url = new URL(route.request().url());
      if (url.hostname === 'localhost' && url.pathname === '/review/app') {
        await route.fulfill({
          status: 200,
          contentType: 'text/html; charset=utf-8',
          body: renderAppValidatorReviewHtml({
            previewMode: 'qualifying',
            selectedTab: 'pit-service',
            selectedRegion: 'content'
          })
        });
        return;
      }

      await route.fulfill({
        status: 404,
        contentType: 'text/plain; charset=utf-8',
        body: 'not found'
      });
    });

    await page.setViewportSize({ width: 1280, height: 720 });
    await page.goto('http://localhost:8765/review/app?preview=qualifying&tab=pit-service&region=content');

    await expect(page.locator('#settings-app')).toHaveCSS('background-color', 'rgba(0, 0, 0, 0)');
    await expect(page.locator('#settings-app')).toHaveJSProperty('clientWidth', 1152);
    await expect(page.locator('#settings-app')).toHaveJSProperty('clientHeight', 608);
    await expect(page.locator('.settings-window')).toHaveCSS('border-top-left-radius', '18px');
    await expect(page.locator('.titlebar')).toHaveCSS('cursor', 'move');
    await expect(page.locator('h1')).toHaveText('Pit Service');
    await expect(page.locator('.sidebar-tab.active')).toHaveText('Pit Service');
    await expect(page.locator('.region-segment.active')).toHaveText('Content');
    await expect(page.locator('h2')).toContainText('Pit Service Cells');
    await expect(page.locator('.grid-toggle-row')).toHaveCount(20);
    await expect(page.locator('.compact-matrix-legend')).toHaveCount(2);
    await expect(page.locator('.compact-matrix-legend').first().locator('span')).toHaveText(['Item', 'P', 'Q', 'R']);
    await expect(page.locator('.compact-matrix-legend').nth(1).locator('span')).toHaveText(['Item', 'P', 'Q', 'R']);
    await expect(page.locator('.grid-toggle-row.sessions').first()).toHaveCSS('grid-template-columns', /24px 24px 24px/);
    await expect(page.locator('.grid-toggle-row.sessions').first()).not.toContainText('Test');
    await expect(page.locator('.overlay-frame')).toHaveCount(0);

    await page.getByRole('tab', { name: 'General' }).click();
    await expect(page.locator('.mini-check')).toHaveCount(0);
    await expect(page.getByText('Sessions')).toHaveCount(0);

    await expect(page.locator('.region-segment')).toHaveText(['General', 'Content', 'Header']);
    await page.getByRole('tab', { name: 'Header' }).click();
    await expect(page.locator('.region-segment.active')).toHaveText('Header');
    await expect(page.locator('.chrome-head')).toHaveText(['Item', 'Practice', 'Qualifying', 'Race']);
    await expect(page.getByRole('tab', { name: 'Footer' })).toHaveCount(0);

    await page.goto('http://localhost:8765/review/app?preview=qualifying&tab=pit-service&region=footer');
    await expect(page.locator('.region-segment')).toHaveText(['General', 'Content', 'Header']);
    await expect(page.locator('.region-segment.active')).toHaveText('General');
    await expect(page.getByRole('tab', { name: 'Footer' })).toHaveCount(0);

    await page.getByRole('link', { name: 'Stream Chat' }).click();
    await expect(page.locator('.region-segment')).toHaveText(['General', 'Content', 'Twitch']);
    await page.getByRole('tab', { name: 'Content' }).click();
    await expect(page.locator('h2')).toContainText('Chat Source');
    await expect(page.locator('.content-body').getByText('Visible')).toHaveCount(0);
    await page.getByRole('tab', { name: 'Twitch' }).click();
    await expect(page.locator('h2')).toContainText('Twitch Metadata');
    await expect(page.getByText('Badges')).toBeVisible();
    await expect(page.getByRole('tab', { name: 'Streamlabs' })).toHaveCount(0);

    await page.getByRole('link', { name: 'Track Map' }).click();
    await expect(page.locator('.content-heading-copy p')).toHaveText('Live car location and sector context.');
    await page.getByRole('tab', { name: 'Content' }).click();
    await expect(page.getByText('Sector boundaries')).toBeVisible();
    await expect(page.getByText('Local map building')).toHaveCount(0);

    await page.getByRole('link', { name: 'Garage Cover' }).click();
    await expect(page.locator('.region-segment')).toHaveText(['General', 'Preview']);
    await expect(page.getByText('Show Test Cover')).toHaveCount(0);
    await expect(page.getByText('Detection')).toHaveCount(0);
    await page.getByRole('tab', { name: 'Preview' }).click();
    await expect(page.locator('.cover-preview.standalone')).toBeVisible();
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  });

  test('renders browser installer review with captured MSI menu structure', async ({ page }) => {
    await page.route('**/*', async (route) => {
      const url = new URL(route.request().url());
      if (url.hostname === 'localhost' && url.pathname === '/review/installer') {
        await route.fulfill({
          status: 200,
          contentType: 'text/html; charset=utf-8',
          body: renderInstallerReviewHtml({
            menuId: url.searchParams.get('menu') || 'welcome'
          })
        });
        return;
      }

      await route.fulfill({
        status: 404,
        contentType: 'text/plain; charset=utf-8',
        body: 'not found'
      });
    });

    await page.setViewportSize({ width: 900, height: 620 });
    await page.goto('http://localhost:8765/review/installer?menu=welcome');

    await expect(page.locator('.installer-window')).toHaveAttribute('data-menu-id', 'welcome');
    await expect(page.locator('.installer-titlebar-title')).toHaveText('Tech Mates Racing Overlay Setup');
    await expect(page.locator('.installer-splash img')).toHaveAttribute('src', /^data:image\/bmp;base64,/);
    await expect(page.locator('.installer-heading')).toHaveText('Welcome to the Tech Mates Racing Overlay Setup Wizard');
    await expect(page.locator('.installer-copy')).toContainText('change the way Tech Mates Racing Overlay features are installed');
    await expect(page.getByRole('button', { name: /Back/ })).toBeDisabled();
    await expect(page.getByRole('button', { name: /Next/ })).toBeEnabled();

    await page.goto('http://localhost:8765/review/installer?menu=installer-page-02');
    await expect(page.locator('.installer-window')).toHaveAttribute('data-menu-id', 'installer-page-02');
    await expect(page.locator('.installer-banner img')).toHaveAttribute('src', /^data:image\/bmp;base64,/);
    await expect(page.locator('.installer-heading')).toHaveText('Installation Scope');
    await expect(page.locator('.installer-copy')).toContainText('Choose the installation scope and folder');
    await expect(page.locator('.installer-copy')).toContainText('Install &just for you (David)');
    await expect(page.locator('.installer-copy')).toContainText('Install for all users of this &machine');
    await expect(page.getByRole('button', { name: /Next/ })).toBeEnabled();

    await page.goto('http://localhost:8765/review/installer?menu=ready-to-install');
    await expect(page.locator('.installer-window')).toHaveAttribute('data-menu-id', 'ready-to-install');
    await expect(page.locator('.installer-heading')).toHaveText('Ready to install Tech Mates Racing Overlay');
    await expect(page.locator('.installer-copy')).toContainText('Click Install to begin the installation.');
    await expect(page.getByRole('button', { name: /Back/ })).toBeEnabled();
    await expect(page.getByRole('button', { name: /Install/ })).toBeEnabled();

    await page.goto('http://localhost:8765/review/installer?menu=cancel-confirm');
    await expect(page.locator('.installer-window')).toHaveAttribute('data-menu-id', 'cancel-confirm');
    await expect(page.locator('.installer-cancel-window')).toContainText('Are you sure you want to cancel Tech Mates Racing Overlay installation?');
    await expect(page.getByRole('button', { name: /Yes/ })).toBeVisible();
    await expect(page.getByRole('button', { name: /No/ })).toBeVisible();
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  });

  test('application gap window uses one symmetric count control and zero hides the graph', async ({ page }) => {
    const reviewState = {
      overlays: {
        'gap-to-leader': {
          carsAhead: 1,
          carsBehind: 1,
          content: {},
          sessions: {},
          chrome: {}
        }
      }
    };
    const patches = [];
    await page.route('**/*', async (route) => {
      const request = route.request();
      const url = new URL(request.url());
      if (url.hostname === 'localhost' && url.pathname === '/review/app') {
        await route.fulfill({
          status: 200,
          contentType: 'text/html; charset=utf-8',
          body: renderAppValidatorReviewHtml({
            selectedTab: 'gap-to-leader',
            selectedRegion: 'general',
            reviewState
          })
        });
        return;
      }

      if (url.hostname === 'localhost' && url.pathname === '/api/review/settings') {
        const patch = JSON.parse(request.postData() || '{}');
        patches.push(patch);
        reviewState.overlays[patch.overlayId] ??= { content: {}, sessions: {}, chrome: {} };
        if (patch.kind === 'number') {
          reviewState.overlays[patch.overlayId][patch.key] = patch.value;
        }
        await route.fulfill({
          status: 200,
          contentType: 'application/json; charset=utf-8',
          body: JSON.stringify({ ok: true, reviewState })
        });
        return;
      }

      if (url.hostname === 'localhost' && url.pathname === '/api/overlay-model/gap-to-leader') {
        const state = reviewState.overlays['gap-to-leader'] || {};
        const shouldRender = Number(state.carsAhead ?? 5) > 0 || Number(state.carsBehind ?? 5) > 0;
        await route.fulfill({
          status: 200,
          contentType: 'application/json; charset=utf-8',
          body: JSON.stringify({
            model: {
              overlayId: 'gap-to-leader',
              title: 'Gap To Leader',
              bodyKind: 'graph',
              columns: [],
              rows: [],
              metrics: [],
              points: shouldRender ? [3, 2, 1] : [],
              shouldRender
            }
          })
        });
        return;
      }

      await route.fulfill({
        status: 404,
        contentType: 'text/plain; charset=utf-8',
        body: 'not found'
      });
    });

    await page.setViewportSize({ width: 1280, height: 720 });
    await page.goto('http://localhost:8765/review/app?tab=gap-to-leader');

    await expect(page.getByText('Class gap window')).toBeVisible();
    await expect(page.getByText('1 each side')).toBeVisible();
    await expect(page.getByText('Cars ahead')).toHaveCount(0);
    await expect(page.getByText('Cars behind')).toHaveCount(0);

    await page.locator('.field-row', { hasText: 'Class gap window' }).locator('.stepper .action-button').first().click();

    await expect.poll(() => patches.filter((patch) => patch.kind === 'number').length).toBe(2);
    expect(patches.filter((patch) => patch.kind === 'number')).toEqual([
      { kind: 'number', overlayId: 'gap-to-leader', key: 'carsAhead', value: 0 },
      { kind: 'number', overlayId: 'gap-to-leader', key: 'carsBehind', value: 0 }
    ]);
    const modelResponse = await page.evaluate(async () => {
      const response = await fetch('/api/overlay-model/gap-to-leader');
      return response.json();
    });
    expect(modelResponse.model.shouldRender).toBe(false);
  });

  test('uses native settings geometry for shell gaps and controls', async ({ page }) => {
    await page.route('**/*', async (route) => {
      const url = new URL(route.request().url());
      if (url.hostname === 'localhost' && url.pathname === '/review/app') {
        await route.fulfill({
          status: 200,
          contentType: 'text/html; charset=utf-8',
          body: renderAppValidatorReviewHtml({
            selectedTab: url.searchParams.get('tab') || 'relative',
            selectedRegion: url.searchParams.get('region') || 'general'
          })
        });
        return;
      }

      await route.fulfill({
        status: 404,
        contentType: 'text/plain; charset=utf-8',
        body: 'not found'
      });
    });

    await page.setViewportSize({ width: 1280, height: 720 });
    await page.goto('http://localhost:8765/review/app?tab=relative&region=general');
    await expectRelativeRect(page, '.settings-window', '.region-segments', { x: 262, y: 166, height: 42 });
    await expectRelativeRect(page, '.settings-window', '.overlay-controls', { x: 262, y: 236, width: 392, height: 266 });
    await expectRelativeRect(page, '.settings-window', '.browser-source-panel', { x: 682, y: 236, width: 414, height: 132 });
    await expectRelativeRect(page, '.settings-window', '.browser-source-panel .action-button', { x: 1004, y: 338, width: 70, height: 30 });
    await expectRelativeRect(page, '.settings-window', '.slider', { x: 410, width: 180, height: 28 });
    await expectRelativeRect(page, '.settings-window', '.stepper', { x: 410, width: 180, height: 32 });

    await page.goto('http://localhost:8765/review/app?tab=stream-chat&region=content');
    await expectRelativeRect(page, '.settings-window', '.segmented.provider-choice', { x: 410, width: 300, height: 30 });
    await expectRelativeRect(page, '.settings-window', '.text-input.streamlabs-url-input', { x: 410, width: 420, height: 28 });
    await expectRelativeRect(page, '.settings-window', '.text-input.twitch-channel-input', { x: 410, width: 210, height: 28 });

    await page.goto('http://localhost:8765/review/app?tab=support');
    await expectRelativeRect(page, '.settings-window', '.support-grid .panel:first-child', { x: 262, y: 178, width: 392, height: 278 });
  });

  test('application settings controls post native-shaped review setting changes', async ({ page }) => {
    const overlaySizes = overlayGeometry().overlaySizes;
    const patches = [];
    await page.route('**/*', async (route) => {
      const request = route.request();
      const url = new URL(request.url());
      if (url.hostname === 'localhost' && url.pathname === '/review/app') {
        await route.fulfill({
          status: 200,
          contentType: 'text/html; charset=utf-8',
          body: renderAppValidatorReviewHtml({
            selectedTab: 'input-state',
            selectedRegion: 'content'
          })
        });
        return;
      }

      if (url.hostname === 'localhost' && url.pathname === '/api/review/settings') {
        const patch = JSON.parse(request.postData() || '{}');
        patches.push(patch);
        await route.fulfill({
          status: 200,
          contentType: 'application/json; charset=utf-8',
          body: JSON.stringify({ ok: true })
        });
        return;
      }

      await route.fulfill({
        status: 404,
        contentType: 'text/plain; charset=utf-8',
        body: 'not found'
      });
    });

    await page.setViewportSize({ width: 1280, height: 720 });
    await page.goto('http://localhost:8765/review/app?tab=input-state&region=content');

    await expect(page.locator('.matrix-head', { hasText: 'Test' })).toHaveCount(0);
    await expect(page.locator('.matrix-head', { hasText: 'Practice' })).toBeVisible();
    await page.locator('.matrix-session button').first().click();
    await expect.poll(() => patches.length).toBeGreaterThan(0);
    expect(patches).toContainEqual({
      kind: 'content',
      overlayId: 'input-state',
      key: 'input-state.trace.throttle',
      label: 'Throttle trace',
      session: 'Practice',
      enabled: false
    });
    await page.locator('.matrix-session button').nth(3).click();
    await page.locator('.matrix-session button').nth(6).click();
    await page.getByRole('tab', { name: 'General' }).click();
    await expect(page.locator('[data-evidence-key="input-state.browser-source.size.value"]'))
      .toHaveText(`OBS size ${overlaySizes.inputStateWidth} x ${overlaySizes.inputStateHeight}`);

    await page.getByRole('link', { name: 'Pit Service' }).click();
    await page.getByRole('tab', { name: 'Header' }).click();
    await page.locator('.chrome-check').first().getByRole('button').click();
    await expect.poll(() => patches.length).toBeGreaterThan(1);
    expect(patches).toContainEqual({
      kind: 'chrome',
      overlayId: 'pit-service',
      area: 'header',
      label: 'Time remaining',
      session: 'Practice',
      enabled: false
    });
    await page.getByRole('tab', { name: 'General' }).click();
    await expect(page.locator('[data-evidence-key="pit-service.browser-source.size.value"]'))
      .toHaveText(`OBS size ${overlaySizes.pitServiceWidth} x ${overlaySizes.pitServiceHeight}`);
  });

  test('application diagnostics buttons update real review-server support state', async ({ page }) => {
    const reviewServer = await startReviewServer();
    const patches = [];
    await page.addInitScript(() => {
      Object.defineProperty(navigator, 'clipboard', {
        configurable: true,
        value: {
          writeText: async (text) => {
            window.__tmrCopiedText = text;
          }
        }
      });
    });

    page.on('request', (request) => {
      const url = new URL(request.url());
      if (url.pathname === '/api/review/settings' && request.method() === 'POST') {
        patches.push(JSON.parse(request.postData() || '{}'));
      }
    });

    const hasPatch = (expected) => patches.some((patch) => Object.entries(expected)
      .every(([key, value]) => patch[key] === value));

    try {
      await page.setViewportSize({ width: 1280, height: 720 });
      await page.goto(`${reviewServer.baseUrl}/review/app?tab=support`);

      await expect(page.locator('h1')).toHaveText('Diagnostics');
      await expect(page.getByText('Data Analysis Opt-out')).toBeVisible();
      await expect(page.getByRole('button', { name: 'Car / track history' })).toBeDisabled();
      await page.getByRole('button', { name: 'Enhanced iRacing Telemetry Capture' }).click();
      await expect(page.getByText('Enhanced capture will save forensics at session end.')).toBeVisible();
      await expect.poll(() => hasPatch({ kind: 'support', action: 'rawCapture', enabled: true })).toBe(true);
      await page.reload();
      await expect(page.getByRole('button', { name: 'Enhanced iRacing Telemetry Capture' })).toHaveAttribute('aria-pressed', 'true');

      await page.getByRole('button', { name: 'Local map building' }).click();
      await expect(page.getByText('Local map building disabled.')).toBeVisible();
      await expect.poll(() => hasPatch({
        kind: 'content',
        overlayId: 'track-map',
        key: 'track-map.build-from-telemetry',
        label: 'Local map building',
        enabled: false
      })).toBe(true);

      await page.getByRole('button', { name: 'Create Bundle' }).click();
      await expect(page.getByText('Created diagnostics bundle.')).toBeVisible();
      await expect(page.getByText('review-diagnostics-bundle.zip')).toBeVisible();
      await expect.poll(() => hasPatch({
        kind: 'support',
        action: 'createBundle',
        path: '/tmp/tmr-overlay-review/diagnostics/review-diagnostics-bundle.zip'
      })).toBe(true);

      await reviewServer.postReviewPatch({
        kind: 'support',
        action: 'createBundle',
        path: '/tmp/tmr-overlay-review/diagnostics/bmw-m4-gt3-evo-gesamtstrecke-vln-history-analysis-20260520-142233-123.zip'
      });
      await page.reload();
      const latestBundleValue = page.locator('[data-evidence-key="support.bundle.latest.value"]');
      await expect(latestBundleValue).toHaveText('bmw-m4-gt3-e...-142233-123.zip');
      const latestBundleMetrics = await latestBundleValue.evaluate((element) => ({
        clientWidth: element.clientWidth,
        scrollWidth: element.scrollWidth,
        textLength: element.textContent?.length || 0
      }));
      expect(latestBundleMetrics.textLength).toBeLessThanOrEqual(30);
      expect(latestBundleMetrics.scrollWidth).toBeLessThanOrEqual(latestBundleMetrics.clientWidth);

      await page.getByRole('button', { name: 'Open Bundle Folder' }).click();
      await expect(page.getByText('Opened diagnostics folder.')).toBeVisible();
      await expect.poll(() => hasPatch({ kind: 'support', action: 'openDiagnostics' })).toBe(true);

      await page.reload();
      await expect(page.getByText('Opened diagnostics folder.')).toBeVisible();
    } finally {
      await reviewServer.stop();
    }
  });

  test('application scale and opacity sliders update UI state and localhost model settings', async ({ page }) => {
    const patches = [];
    const reviewState = { unitSystem: 'Metric', overlays: {} };
    await page.route('**/*', async (route) => {
      const request = route.request();
      const url = new URL(request.url());
      if (url.hostname === 'localhost' && url.pathname === '/review/app') {
        await route.fulfill({
          status: 200,
          contentType: 'text/html; charset=utf-8',
          body: renderAppValidatorReviewHtml({
            selectedTab: 'input-state',
            selectedRegion: 'general',
            reviewState
          })
        });
        return;
      }

      if (url.hostname === 'localhost' && url.pathname === '/api/review/settings') {
        const patch = JSON.parse(request.postData() || '{}');
        patches.push(patch);
        reviewState.overlays[patch.overlayId] ??= {};
        if (patch.kind === 'number') {
          reviewState.overlays[patch.overlayId][patch.key] = patch.value;
        }
        await route.fulfill({
          status: 200,
          contentType: 'application/json; charset=utf-8',
          body: JSON.stringify({ ok: true, reviewState })
        });
        return;
      }

      if (url.hostname === 'localhost' && url.pathname === '/api/overlay-model/input-state') {
        const opacityPercent = reviewState.overlays['input-state']?.opacityPercent ?? 100;
        await route.fulfill({
          status: 200,
          contentType: 'application/json; charset=utf-8',
          body: JSON.stringify({
            model: {
              overlayId: 'input-state',
              title: 'Inputs',
              bodyKind: 'input-state',
              rootOpacity: opacityPercent / 100,
              inputs: {}
            }
          })
        });
        return;
      }

      if (url.hostname === 'localhost' && url.pathname === '/api/overlay-model/stream-chat') {
        const opacityPercent = reviewState.overlays['stream-chat']?.opacityPercent ?? 100;
        await route.fulfill({
          status: 200,
          contentType: 'application/json; charset=utf-8',
          body: JSON.stringify({
            model: {
              overlayId: 'stream-chat',
              title: 'Stream Chat',
              bodyKind: 'stream-chat',
              rootOpacity: opacityPercent / 100,
              streamChat: { status: 'configured_twitch', rows: [] }
            }
          })
        });
        return;
      }

      await route.fulfill({
        status: 404,
        contentType: 'text/plain; charset=utf-8',
        body: 'not found'
      });
    });

    await page.setViewportSize({ width: 1280, height: 720 });
    await page.goto('http://localhost:8765/review/app?tab=input-state&region=general');

    const scaleSlider = page.getByRole('slider', { name: 'Scale' });
    await expect(scaleSlider).toHaveAttribute('min', '60');
    await expect(scaleSlider).toHaveAttribute('max', '200');
    await expect(scaleSlider).toHaveAttribute('step', '1');
    await scaleSlider.focus();
    await page.keyboard.press('ArrowRight');
    await expect(page.getByText('101%')).toBeVisible();
    await expect(page.getByText('OBS size 525 x 263')).toBeVisible();
    expect(patches).toContainEqual({
      kind: 'number',
      overlayId: 'input-state',
      key: 'scalePercent',
      value: 101
    });
    const draggedScale = await dragPercentSliderTo(page, scaleSlider, 137, 60, 200);
    expect(draggedScale).toBeGreaterThanOrEqual(136);
    expect(draggedScale).toBeLessThanOrEqual(138);
    expect([60, 75, 100, 125, 150, 175, 200]).not.toContain(draggedScale);
    await expect(page.getByText(`${draggedScale}%`)).toBeVisible();
    expect(patches).toContainEqual({
      kind: 'number',
      overlayId: 'input-state',
      key: 'scalePercent',
      value: draggedScale
    });

    const opacitySlider = page.getByRole('slider', { name: 'Opacity' });
    await expect(opacitySlider).toHaveAttribute('min', '20');
    await expect(opacitySlider).toHaveAttribute('max', '100');
    await expect(opacitySlider).toHaveAttribute('step', '1');
    await opacitySlider.focus();
    await page.keyboard.press('ArrowLeft');
    await expect(page.getByText('99%')).toBeVisible();
    expect(patches).toContainEqual({
      kind: 'number',
      overlayId: 'input-state',
      key: 'opacityPercent',
      value: 99
    });
    const draggedOpacity = await dragPercentSliderTo(page, opacitySlider, 83, 20, 100);
    expect(draggedOpacity).toBeGreaterThanOrEqual(82);
    expect(draggedOpacity).toBeLessThanOrEqual(87);
    expect([20, 30, 40, 50, 60, 70, 80, 90, 100]).not.toContain(draggedOpacity);
    await expect(page.getByText(`${draggedOpacity}%`)).toBeVisible();
    expect(patches).toContainEqual({
      kind: 'number',
      overlayId: 'input-state',
      key: 'opacityPercent',
      value: draggedOpacity
    });

    const modelResponse = await page.evaluate(async () => {
      const response = await fetch('/api/overlay-model/input-state');
      return response.json();
    });
    expect(modelResponse.model.rootOpacity).toBe(draggedOpacity / 100);

    await page.getByRole('link', { name: 'Stream Chat' }).click();
    await expect(page.getByRole('slider', { name: 'Opacity' })).toBeVisible();
    const streamChatOpacitySlider = page.getByRole('slider', { name: 'Opacity' });
    await streamChatOpacitySlider.focus();
    await page.keyboard.press('ArrowLeft');
    await expect(page.getByText('99%')).toBeVisible();
    expect(patches).toContainEqual({
      kind: 'number',
      overlayId: 'stream-chat',
      key: 'opacityPercent',
      value: 99
    });

    const streamChatModelResponse = await page.evaluate(async () => {
      const response = await fetch('/api/overlay-model/stream-chat');
      return response.json();
    });
    expect(streamChatModelResponse.model.rootOpacity).toBe(0.99);
  });

  test('application settings patches update real review-server model evidence', async ({ page }) => {
    const reviewServer = await startReviewServer();
    try {
      await page.setViewportSize({ width: 1280, height: 720 });
      await page.goto(`${reviewServer.baseUrl}/review/app?preview=race&tab=relative&region=content`);

      await (await sessionToggleForMatrixItem(page, 'Pit status', 'Race')).click();
      await expect.poll(async () => {
        const model = await fetchOverlayModelFromPage(page, 'relative', 'race');
        return (model.columns || []).some((column) => column.dataKey === 'pit');
      }, { timeout: 3500 }).toBe(true);

      const modelWithPit = await fetchOverlayModelFromPage(page, 'relative', 'race');
      expect(modelWithPit.effectiveSettings.rendered.browserSource).toMatchObject({
        baseWidth: 440,
        baseHeight: 308,
        width: 440,
        height: 308
      });
      expect(modelWithPit.effectiveSettings.settings).toContainEqual(expect.objectContaining({
        key: 'relative.content.relative.pit.enabled',
        session: 'race',
        value: true
      }));

      await (await sessionToggleForMatrixItem(page, 'Pit status', 'Race')).click();
      await expect.poll(async () => {
        const model = await fetchOverlayModelFromPage(page, 'relative', 'race');
        return (model.columns || []).some((column) => column.dataKey === 'pit');
      }, { timeout: 3500 }).toBe(false);

      const modelWithoutPit = await fetchOverlayModelFromPage(page, 'relative', 'race');
      expect(modelWithoutPit.effectiveSettings.settings).toContainEqual(expect.objectContaining({
        key: 'relative.content.relative.pit.enabled',
        session: 'race',
        value: false
      }));
      expect(modelWithoutPit.effectiveSettings.rendered.rowCount).toBe(7);

      await page.getByRole('tab', { name: 'Header' }).click();
      await page.locator('.chrome-check button').nth(2).click();
      await page.getByRole('tab', { name: 'General' }).click();

      await expect(page.getByText('OBS size 392 x 274')).toBeVisible();
      const modelWithoutHeader = await fetchOverlayModelFromPage(page, 'relative', 'race');
      expect(modelWithoutHeader.headerItems || []).toEqual([]);
      expect(modelWithoutHeader.effectiveSettings.rendered.headerItems).toEqual([]);
      expect(modelWithoutHeader.effectiveSettings.rendered.browserSource).toMatchObject({
        baseWidth: 392,
        baseHeight: 274,
        width: 392,
        height: 274,
        scalePercent: 100
      });
      expect(modelWithoutHeader.effectiveSettings.settings).toContainEqual(expect.objectContaining({
        key: 'chrome.header.time-remaining.race',
        value: false
      }));
    } finally {
      await reviewServer.stop();
    }
  });

  test('application browser source copy button writes the localhost overlay URL', async ({ page }) => {
    await page.addInitScript(() => {
      Object.defineProperty(navigator, 'clipboard', {
        configurable: true,
        value: {
          writeText: async (text) => {
            window.__tmrCopiedText = text;
          }
        }
      });
    });

    await page.route('**/*', async (route) => {
      const url = new URL(route.request().url());
      if (url.hostname === 'localhost' && url.pathname === '/review/app') {
        await route.fulfill({
          status: 200,
          contentType: 'text/html; charset=utf-8',
          body: renderAppValidatorReviewHtml({
            selectedTab: 'input-state',
            selectedRegion: 'general'
          })
        });
        return;
      }

      await route.fulfill({
        status: 404,
        contentType: 'text/plain; charset=utf-8',
        body: 'not found'
      });
    });

    await page.setViewportSize({ width: 1280, height: 720 });
    await page.goto('http://localhost:8765/review/app?tab=input-state&region=general');

    await page.getByRole('button', { name: 'Copy' }).click();

    await expect.poll(() => page.evaluate(() => window.__tmrCopiedText)).toBe('http://localhost:8765/overlays/input-state');
    await expect(page.getByRole('button', { name: 'Copied' })).toBeVisible();
  });

  test('application settings controls update review overlay models like production settings', async ({ page }) => {
    const reviewState = { unitSystem: 'Metric', overlays: {} };
    await page.route('**/*', async (route) => {
      const request = route.request();
      const url = new URL(request.url());
      if (url.hostname === 'localhost' && url.pathname === '/review/app') {
        await route.fulfill({
          status: 200,
          contentType: 'text/html; charset=utf-8',
          body: renderAppValidatorReviewHtml({
            selectedTab: 'car-radar',
            selectedRegion: 'general',
            reviewState
          })
        });
        return;
      }

      if (url.hostname === 'localhost' && url.pathname === '/api/review/settings') {
        const patch = JSON.parse(request.postData() || '{}');
        reviewState.overlays[patch.overlayId] ??= { content: {}, sessions: {}, chrome: {} };
        if (patch.kind === 'content') {
          const session = String(patch.session || '').trim().toLowerCase();
          if (session) {
            reviewState.overlays[patch.overlayId].content[`${patch.key}.${session}`] = patch.enabled;
            reviewState.overlays[patch.overlayId].content[`${patch.label}.${session}`] = patch.enabled;
          } else {
            reviewState.overlays[patch.overlayId].content[patch.key] = patch.enabled;
            reviewState.overlays[patch.overlayId].content[patch.label] = patch.enabled;
          }
        }
        if (patch.kind === 'number' && Number.isFinite(Number(patch.value))) {
          reviewState.overlays[patch.overlayId][patch.key] = Number(patch.value);
        }
        await route.fulfill({
          status: 200,
          contentType: 'application/json; charset=utf-8',
          body: JSON.stringify({ ok: true, reviewState })
        });
        return;
      }

      if (url.hostname === 'localhost' && url.pathname === '/api/overlay-model/car-radar') {
        const content = reviewState.overlays['car-radar']?.content || {};
        const warningEnabled = content['radar.multiclass-warning'] !== false
          && content['Faster-class warning'] !== false;
        await route.fulfill({
          status: 200,
          contentType: 'application/json; charset=utf-8',
          body: JSON.stringify({
            model: {
              overlayId: 'car-radar',
              title: 'Car Radar',
              bodyKind: 'car-radar',
              status: warningEnabled ? 'faster class' : 'clear',
              carRadar: {
                showMulticlassWarning: warningEnabled,
                multiclassWarningRangeSeconds: reviewState.overlays['car-radar']?.multiclassWarningSeconds ?? 5,
                radarVisibilitySeconds: reviewState.overlays['car-radar']?.radarVisibilitySeconds ?? 2,
                strongestMulticlassApproach: warningEnabled ? { relativeSeconds: -3.2 } : null,
                renderModel: { shouldRender: warningEnabled, width: 300, height: 300 }
              }
            }
          })
        });
        return;
      }

      await route.fulfill({
        status: 404,
        contentType: 'text/plain; charset=utf-8',
        body: 'not found'
      });
    });

    await page.setViewportSize({ width: 1280, height: 720 });
    await page.goto('http://localhost:8765/review/app?tab=car-radar');

    await expect(page.getByText('Radar proximity')).toHaveCount(0);
    await expect(page.locator('.region-segment')).toHaveText(['General']);
    await expect(page.getByText('Warning window')).toHaveCount(0);
    await expect(page.getByText('Multiclass window')).toBeVisible();
    await expect(page.getByText('Radar range')).toBeVisible();
    await page.locator('.field-row', { hasText: 'Faster-class warning' }).getByRole('button').click();
    await expect(page.getByText('Multiclass window')).toHaveCount(0);
    await expect(page.getByText('Radar range')).toBeVisible();

    const modelResponse = await page.evaluate(async () => {
      const response = await fetch('/api/overlay-model/car-radar');
      return response.json();
    });

    expect(modelResponse.model.carRadar.showMulticlassWarning).toBe(false);
    expect(modelResponse.model.carRadar.strongestMulticlassApproach).toBeNull();
  });

  test('preserves preview and replay controls on browser overlay API calls', async ({ page }) => {
    const requests = await installBrowserOverlayRoutes(page, 'standings', {
      live: freshLiveSnapshot({}),
      model: standingsDisplayModel()
    });

    await page.setViewportSize({ width: 692, height: 520 });
    await page.goto('http://localhost:8765/overlays/standings?preview=race&rel=0');
    await expect(page.locator('tbody tr')).toHaveCount(6);

    expect(requests).not.toContain('/api/snapshot?preview=race&rel=0');
    expect(requests).toContain('/api/overlay-model/standings?preview=race&rel=0');
  });
});

async function dragPercentSliderTo(page, slider, value, min, max) {
  const box = await slider.boundingBox();
  expect(box).not.toBeNull();

  const ratio = (value - min) / Math.max(1, max - min);
  const startX = box.x + box.width / 2;
  const targetX = box.x + Math.max(0, Math.min(1, ratio)) * box.width;
  const y = box.y + box.height / 2;
  await page.mouse.move(startX, y);
  await page.mouse.down();
  await page.mouse.move(targetX, y, { steps: 8 });
  await page.mouse.up();
  return Number(await slider.inputValue());
}

async function installBrowserOverlayRoutes(page, overlayId, fixture) {
  const requests = [];
  let liveFrameIndex = 0;
  await page.route('**/*', async (route) => {
    const request = route.request();
    const url = new URL(request.url());
    requests.push(url.pathname);
    if (url.search) {
      requests.push(`${url.pathname}${url.search}`);
    }

    if (url.hostname === 'localhost' && url.pathname === `/overlays/${overlayId}`) {
      await route.fulfill({
        status: 200,
        contentType: 'text/html; charset=utf-8',
        body: renderOverlayHtml(overlayId)
      });
      return;
    }

    const advancesLiveFrame = url.pathname === `/api/overlay-model/${overlayId}`;
    const frameKey = advancesLiveFrame ? 'frame' : url.pathname;
    const payload = browserOverlayApiResponse(overlayId, url.pathname, {
      ...fixture,
      live: resolveFrameFixture(fixture.live, frameKey, liveFrameIndex),
      settings: resolveFrameFixture(fixture.settings, frameKey, liveFrameIndex),
      model: resolveFrameFixture(fixture.model, frameKey, liveFrameIndex)
    });
    if (advancesLiveFrame) {
      liveFrameIndex += 1;
    }
    if (payload) {
      await route.fulfill({
        status: 200,
        contentType: 'application/json; charset=utf-8',
        body: JSON.stringify(payload)
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

  return requests;
}

async function boundingBoxWidth(locator) {
  let width = 0;
  await expect.poll(async () => {
    const box = await locator.boundingBox();
    width = box?.width ?? 0;
    return width;
  }, { timeout: 3500 }).toBeGreaterThan(0);
  return width;
}

async function expectElementHeight(locator, expectedHeight) {
  await expect(locator).toBeVisible();
  const box = await locator.boundingBox();
  expect(box).not.toBeNull();
  expect(Math.abs(box.height - expectedHeight)).toBeLessThanOrEqual(0.25);
}

async function expectRelativeRect(page, originSelector, targetSelector, expected) {
  const actual = await page.evaluate(({ originSelector: origin, targetSelector: target }) => {
    const originElement = document.querySelector(origin);
    const targetElement = document.querySelector(target);
    if (!originElement || !targetElement) {
      return null;
    }

    const originRect = originElement.getBoundingClientRect();
    const targetRect = targetElement.getBoundingClientRect();
    return {
      x: targetRect.left - originRect.left,
      y: targetRect.top - originRect.top,
      width: targetRect.width,
      height: targetRect.height
    };
  }, { originSelector, targetSelector });

  expect(actual, `${targetSelector} should exist`).not.toBeNull();
  for (const [key, value] of Object.entries(expected)) {
    expect(Math.abs(actual[key] - value), `${targetSelector}.${key}`).toBeLessThanOrEqual(3.5);
  }
}

function fuelCalculatorHeightFromGeometry(rowCount, sectionCount, geometry) {
  if (rowCount <= 0 || sectionCount <= 0) {
    return geometry.minimumFuelCalculatorHeight;
  }

  const rowGaps = Math.max(0, rowCount - sectionCount) * geometry.rowGap;
  const sectionGaps = Math.max(0, sectionCount - 1) * geometry.sectionGap;
  const height = geometry.headerChromeHeight
    + geometry.fuelContentVerticalPadding
    + sectionCount * geometry.fuelSectionTitleReserveHeight
    + rowCount * geometry.segmentedRowHeight
    + rowGaps
    + sectionGaps
    + geometry.collapsedFooterReserveHeight;
  return Math.max(geometry.minimumFuelCalculatorHeight, Math.min(315, height));
}

function metricSectionHeightFromGeometry(rowCount, segmentedRows, geometry) {
  if (rowCount <= 0) return 0;
  const plainRows = Math.max(0, rowCount - segmentedRows);
  return geometry.sectionTitleHeight
    + geometry.sectionTitleBottomGap
    + segmentedRows * geometry.segmentedRowHeight
    + plainRows * geometry.plainRowHeight
    + Math.max(0, rowCount - 1) * geometry.rowGap;
}

function pitServiceMetricOnlyHeightFromGeometry(geometry) {
  const sectionHeights = [
    metricSectionHeightFromGeometry(1, 1, geometry),
    metricSectionHeightFromGeometry(2, 2, geometry),
    metricSectionHeightFromGeometry(4, 4, geometry)
  ];
  const metricHeight = sectionHeights.reduce((total, value) => total + value, 0)
    + Math.max(0, sectionHeights.length - 1) * geometry.pitServiceSectionGap;
  return Math.max(
    geometry.minimumSimpleTelemetryHeight,
    Math.min(707, metricHeight + geometry.pitServiceContentChromeHeight)
  );
}

function resolveFrameFixture(value, path, frameIndex) {
  if (path !== 'frame') {
    return Array.isArray(value) ? value[0] : typeof value === 'function' ? value(0) : value;
  }

  if (Array.isArray(value)) {
    return value[Math.min(frameIndex, value.length - 1)];
  }

  return typeof value === 'function' ? value(frameIndex) : value;
}

async function sessionToggleForMatrixItem(page, label, session) {
  const sessionIndex = { Practice: 0, Qualifying: 1, Race: 2 }[session];
  expect(sessionIndex).toBeDefined();
  const labels = (await page.locator('.matrix-item').allTextContents())
    .map((text) => text.trim());
  const rowIndex = labels.findIndex((candidate) => candidate === label);
  expect(rowIndex).toBeGreaterThanOrEqual(0);
  return page.locator('.matrix-session button').nth(rowIndex * 3 + sessionIndex);
}

async function fetchOverlayModelFromPage(page, overlayId, preview = 'race') {
  return page.evaluate(async ({ overlayId, preview }) => {
    const response = await fetch(`/api/overlay-model/${overlayId}?preview=${preview}`);
    if (!response.ok) {
      throw new Error(`overlay model fetch failed ${response.status}`);
    }
    const payload = await response.json();
    return payload.model;
  }, { overlayId, preview });
}

function inputStateLiveSnapshot(index, throttle) {
  return {
    ...freshLiveSnapshot({
      raceEvents: {
        hasData: true,
        isOnTrack: true,
        isInGarage: false
      },
      reference: {
        hasData: true,
        playerCarIdx: 10,
        focusCarIdx: 10,
        focusIsPlayer: true,
        isOnTrack: true,
        isInGarage: false
      },
      driverDirectory: {
        hasData: true,
        playerCarIdx: 10,
        focusCarIdx: 10
      },
      inputs: {
        hasData: true,
        quality: 'raw',
        throttle,
        brake: 1,
        clutch: 0,
        steeringWheelAngle: 0,
        gear: 3,
        speedMetersPerSecond: 60,
        brakeAbsActive: false,
        trace: Array.from({ length: index + 2 }, (_, pointIndex) => ({
          throttle: pointIndex === index + 1 ? throttle : pointIndex % 2 === 0 ? 0.12 : 1,
          brake: pointIndex % 2 === 0 ? 0.88 : 0.12,
          clutch: 0,
          brakeAbsActive: false
        }))
      }
    }),
    sequence: 100 + index
  };
}

function standingsDisplayModel(overrides = {}) {
  return {
    overlayId: 'standings',
    title: 'Standings',
    status: 'scoring | 5/5 live',
    source: 'source: scoring snapshot + live timing',
    bodyKind: 'table',
    columns: [
      { id: 'standings.class-position', label: 'Pos', dataKey: 'class-position', width: 35, alignment: 'right' },
      { id: 'standings.car-number', label: 'CAR', dataKey: 'car-number', width: 50, alignment: 'right' },
      { id: 'standings.driver', label: 'Driver', dataKey: 'driver', width: 250, alignment: 'left' },
      { id: 'standings.gap', label: 'GAP', dataKey: 'gap', width: 60, alignment: 'right' },
      { id: 'standings.interval', label: 'INT', dataKey: 'interval', width: 60, alignment: 'right' },
      { id: 'standings.fastest-lap', label: 'FAST', dataKey: 'fastest-lap', width: 70, alignment: 'right' },
      { id: 'standings.last-lap', label: 'LAST', dataKey: 'last-lap', width: 70, alignment: 'right' },
      { id: 'standings.pit', label: 'PIT', dataKey: 'pit', width: 48, alignment: 'right' }
    ],
    rows: [
      headerRow('LMP2', '2 cars | 10.00 laps', '#33CEFF'),
      carRow(['1', '#8', 'Proto One', 'Lap 22', '-45.0', '1:45.884', '1:46.210', '']),
      headerRow('GT3', '3 cars | 12.40 laps', '#FFAA00'),
      carRow(['1', '#11', 'GT3 Leader', 'Lap 21', '-2.0', '1:53.112', '1:53.112', '']),
      carRow(['2', '#71', 'Focus Racer', '+3.4', '0.0', '1:54.228', '1:54.901', ''], { isReference: true }),
      carRow(['3', '#91', 'Chaser', '+8.9', '+5.5', '1:55.480', '1:56.004', 'IN'], { isPit: true })
    ],
    metrics: [],
    headerItems: [{ key: 'timeRemaining', value: '06:37:08' }],
    ...overrides
  };
}

function standingsRecommendedBrowserSourceRows() {
  const car = (position, number, driver, gap, interval, fastest, last, pit = '', extra = {}) =>
    carRow([String(position), `#${number}`, driver, gap, interval, fastest, last, pit], extra);

  return [
    headerRow('GTP', '2 cars | 31.25 laps', '#C3413B'),
    car(1, 4, 'Prototype Leader', 'Lap 31', '-28.1', '1:33.210', '1:34.012'),
    car(2, 38, 'Prototype Chase', '+6.4', '+6.4', '1:33.880', '1:34.452'),
    headerRow('LMP2', '2 cars | 30.80 laps', '#33CEFF'),
    car(1, 8, 'LMP Two', 'Lap 30', '-9.5', '1:45.884', '1:46.210'),
    car(2, 18, 'LMP Traffic', '+14.1', '+14.1', '1:46.231', '1:47.016'),
    headerRow('GT3', '14 cars | 29.40 laps', '#FFAA00'),
    car(1, 11, 'GT3 Leader', 'Lap 29', '-2.0', '1:53.112', '1:53.112'),
    car(2, 71, 'Focus Racer', '+3.4', '0.0', '1:54.228', '1:54.901', '', { isReference: true }),
    car(3, 91, 'Chaser One', '+8.9', '+5.5', '1:55.480', '1:56.004', 'IN', { isPit: true }),
    car(4, 33, 'Chaser Two', '+15.2', '+6.3', '1:55.903', '1:56.440'),
    car(5, 12, 'Chaser Three', '+21.7', '+6.5', '1:56.004', '1:56.881'),
    car(6, 82, 'Chaser Four', '+30.0', '+8.3', '1:56.330', '1:57.210'),
    car(7, 48, 'Chaser Five', '+42.4', '+12.4', '1:56.752', '1:58.004'),
    car(8, 27, 'Chaser Six', '+55.0', '+12.6', '1:57.110', '1:58.334'),
    car(9, 52, 'Chaser Seven', '+1:05.0', '+10.0', '1:57.402', '1:58.512'),
    car(10, 63, 'Chaser Eight', '+1:22.5', '+17.5', '1:57.830', '1:59.030'),
    car(11, 77, 'Chaser Nine', '+1:44.1', '+21.6', '1:58.012', '1:59.400'),
    car(12, 5, 'Chaser Ten', '+2:03.6', '+19.5', '1:58.220', '2:00.100'),
    car(13, 44, 'Chaser Eleven', '+2:30.0', '+26.4', '1:58.940', '2:00.430'),
    car(14, 99, 'Chaser Twelve', '+3:02.4', '+32.4', '1:59.210', '2:01.004')
  ];
}

function standingsWithoutPitColumn(overrides = {}) {
  const model = standingsDisplayModel(overrides);
  return {
    ...model,
    columns: model.columns.filter((column) => column.dataKey !== 'pit'),
    rows: model.rows.map((row) => row.cells.length > 0
      ? {
          ...row,
          cells: row.cells.slice(0, -1),
          isPit: false
        }
      : row)
  };
}

function relativeDisplayModel(overrides = {}) {
  return {
    overlayId: 'relative',
    title: 'Relative',
    status: '5 - 2/4 cars',
    source: 'source: scoring snapshot + live timing',
    bodyKind: 'table',
    columns: [
      { id: 'relative.position', label: 'Pos', dataKey: 'relative-position', width: 48, alignment: 'right' },
      { id: 'relative.driver', label: 'Driver', dataKey: 'driver', width: 240, alignment: 'left' },
      { id: 'relative.interval', label: 'INT', dataKey: 'interval', width: 58, alignment: 'right' }
    ],
    rows: [
      carRow(['4', 'Ahead Driver', '-1.2']),
      carRow(['5', 'Focus Driver', '0.0'], { isReference: true }),
      carRow(['6', 'Behind Driver', '+1.5'])
    ],
    metrics: [],
    headerItems: [],
    ...overrides
  };
}

function fuelPracticeDisplayModel(overrides = {}) {
  return {
    overlayId: 'fuel-calculator',
    title: 'Fuel Calculator',
    status: 'fuel range',
    source: 'usage 3.1 L/lap (measured green lap) | range 23.9 laps | 34.2 laps/tank',
    bodyKind: 'metrics',
    metrics: [],
    metricSections: [
      {
        title: 'Fuel Range',
        rows: [{
          label: 'Fuel',
          value: '74.0 L | range 23.9 laps | tank 34.2 laps',
          tone: 'info',
          segments: [
            { label: 'Level', value: '74.0 L', tone: 'info' },
            { label: 'Usage', value: '3.1 L/lap', tone: 'info' },
            { label: 'Range', value: '23.9 laps', tone: 'info' },
            { label: 'Tank', value: '34.2 laps', tone: 'info' }
          ]
        }]
      },
      {
        title: 'Fuel Usage',
        rows: [{
          label: 'Practice Usage',
          value: 'min 3.0 L/lap | avg 3.1 L/lap | max 3.2 L/lap',
          tone: 'info',
          segments: [
            { label: 'Min', value: '3.0 L/lap', tone: 'info' },
            { label: 'Avg', value: '3.1 L/lap', tone: 'info' },
            { label: 'Max', value: '3.2 L/lap', tone: 'info' },
            { label: 'Laps', value: '3 laps', tone: 'info' }
          ]
        }]
      }
    ],
    headerItems: [{ key: 'timeRemaining', value: '06:37:08', tone: 'success' }],
    ...overrides
  };
}

function sessionWeatherMetricDisplayModel(overrides = {}) {
  return {
    overlayId: 'session-weather',
    title: 'Session / Weather',
    status: 'live',
    source: 'session 06:37:08 | weather clear',
    bodyKind: 'metrics',
    metrics: [],
    metricSections: [
      {
        title: 'Session',
        rows: [
          { label: 'Session', value: 'Practice', tone: 'info' },
          {
            label: 'Clock',
            value: 'Elapsed 12:34 | Remaining 06:37',
            tone: 'info',
            segments: [
              { label: 'Elapsed', value: '12:34', tone: 'info' },
              { label: 'Remaining', value: '06:37', tone: 'success' }
            ]
          }
        ]
      },
      {
        title: 'Weather',
        rows: [
          {
            label: 'Wind',
            value: 'Facing NW | 11 mph',
            tone: 'info',
            segments: [
              { label: 'Facing', value: 'NW', tone: 'info', rotationDegrees: 315 },
              { label: 'Speed', value: '11 mph', tone: 'info' }
            ]
          }
        ]
      }
    ],
    gridSections: [],
    headerItems: [{ key: 'timeRemaining', value: '06:37:08', tone: 'success' }],
    ...overrides
  };
}

function pitServiceMetricDisplayModel(overrides = {}) {
  return {
    overlayId: 'pit-service',
    title: 'Pit Service',
    status: 'armed',
    source: 'pit service live',
    bodyKind: 'metrics',
    metrics: [],
    metricSections: [
      {
        title: 'Service',
        rows: [
          {
            label: 'Fuel',
            value: 'Request 12.0 L | Selected 12.0 L',
            tone: 'info',
            segments: [
              { label: 'Request', value: '12.0 L', tone: 'info' },
              { label: 'Selected', value: '12.0 L', tone: 'success' }
            ]
          }
        ]
      }
    ],
    gridSections: [
      {
        title: 'Tires',
        headers: ['Info', 'FL', 'FR', 'RL', 'RR'],
        rows: [
          {
            label: 'Pressure',
            tone: 'info',
            cells: [
              { value: '27.5', tone: 'info' },
              { value: '27.6', tone: 'info' },
              { value: '27.3', tone: 'info' },
              { value: '27.4', tone: 'info' }
            ]
          }
        ]
      }
    ],
    headerItems: [{ key: 'timeRemaining', value: '06:37:08', tone: 'success' }],
    ...overrides
  };
}

function hiddenDisplayModel(overlayId, status) {
  return {
    overlayId,
    title: overlayId,
    status,
    source: '',
    bodyKind: 'table',
    columns: [],
    rows: [],
    metrics: [],
    points: [],
    headerItems: [],
    shouldRender: false
  };
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
