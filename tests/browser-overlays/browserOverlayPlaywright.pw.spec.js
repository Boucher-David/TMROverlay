import { expect, test } from '@playwright/test';
import {
  browserOverlayApiResponse,
  freshLiveSnapshot,
  renderOverlayHtml
} from './browserOverlayTestHost.js';
import {
  renderAppValidatorReviewHtml,
  renderInstallerReviewHtml,
  renderSettingsGeneralReviewHtml
} from './browserOverlayAssets.js';

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
    await expect(page.locator('.title')).toHaveText('Stream Chat');
    await expect(page.locator('.overlay')).toHaveCSS('opacity', '1');
    const overlayBox = await page.locator('.overlay').boundingBox();
    expect(overlayBox?.width).toBe(380);
    expect(overlayBox?.height).toBe(520);
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

    await expect(page.locator('h1')).toHaveText('Pit Service');
    await expect(page.locator('.sidebar-tab.active')).toHaveText('Pit Service');
    await expect(page.locator('.region-segment.active')).toHaveText('Content');
    await expect(page.locator('h2')).toContainText('Pit Service Cells');
    await expect(page.locator('.grid-toggle-row')).toHaveCount(20);
    await expect(page.locator('.compact-matrix-legend span')).toHaveText(['Item', 'P', 'Q', 'R']);
    await expect(page.locator('.grid-toggle-row.sessions').first()).toHaveCSS('grid-template-columns', /24px 24px 24px/);
    await expect(page.locator('.grid-toggle-row.sessions').first()).not.toContainText('Test');
    await expect(page.locator('.overlay-frame')).toHaveCount(0);

    await page.getByRole('tab', { name: 'General' }).click();
    await expect(page.locator('.mini-check')).toHaveCount(0);
    await expect(page.getByText('Sessions')).toHaveCount(0);

    await page.getByRole('tab', { name: 'Header' }).click();
    await expect(page.locator('.chrome-head')).toHaveText(['Item', 'Practice', 'Qualifying', 'Race']);
    await page.getByRole('tab', { name: 'Footer' }).click();
    await expect(page.locator('.region-segment.active')).toHaveText('Footer');
    await expect(page.locator('h2')).toContainText('Footer');
    await expect(page.getByText('Source')).toBeVisible();
    await expect(page.locator('.chrome-head')).toHaveText(['Item', 'Practice', 'Qualifying', 'Race']);

    await page.getByRole('link', { name: 'Stream Chat' }).click();
    await expect(page.locator('.region-segment')).toHaveText(['General', 'Content', 'Twitch', 'Streamlabs']);
    await page.getByRole('tab', { name: 'Content' }).click();
    await expect(page.locator('h2')).toContainText('Chat Source');
    await expect(page.locator('.content-body').getByText('Visible')).toHaveCount(0);
    await page.getByRole('tab', { name: 'Twitch' }).click();
    await expect(page.locator('h2')).toContainText('Twitch Metadata');
    await expect(page.getByText('Badges')).toBeVisible();
    await page.getByRole('tab', { name: 'Streamlabs' }).click();
    await expect(page.locator('h2')).toContainText('Streamlabs');
    await expect(page.getByText('Streamlabs-specific message controls')).toBeVisible();

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
            selectedRegion: 'content',
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
    await page.goto('http://localhost:8765/review/app?tab=gap-to-leader&region=content');

    await expect(page.getByText('Class gap window')).toBeVisible();
    await expect(page.getByText('Cars each side')).toBeVisible();
    await expect(page.getByText('1 each side')).toBeVisible();
    await expect(page.getByText('Cars ahead')).toHaveCount(0);
    await expect(page.getByText('Cars behind')).toHaveCount(0);

    await page.locator('.matrix-control-count .stepper .action-button').first().click();

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

  test('application settings controls post native-shaped review setting changes', async ({ page }) => {
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
    await expect(page.getByText('OBS size 276 x 260')).toBeVisible();

    await page.getByRole('link', { name: 'Pit Service' }).click();
    await page.getByRole('tab', { name: 'Footer' }).click();
    await page.locator('.chrome-check').first().getByRole('button').click();
    await expect.poll(() => patches.length).toBeGreaterThan(1);
    expect(patches).toContainEqual({
      kind: 'chrome',
      overlayId: 'pit-service',
      area: 'footer',
      label: 'Source',
      session: 'Practice',
      enabled: false
    });
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

      await route.fulfill({
        status: 404,
        contentType: 'text/plain; charset=utf-8',
        body: 'not found'
      });
    });

    await page.setViewportSize({ width: 1280, height: 720 });
    await page.goto('http://localhost:8765/review/app?tab=input-state&region=general');

    const scaleSlider = page.getByRole('slider', { name: 'Scale' });
    await scaleSlider.focus();
    await page.keyboard.press('ArrowRight');
    await expect(page.getByText('125%')).toBeVisible();
    await expect(page.getByText('OBS size 650 x 325')).toBeVisible();
    expect(patches).toContainEqual({
      kind: 'number',
      overlayId: 'input-state',
      key: 'scalePercent',
      value: 125
    });

    const opacitySlider = page.getByRole('slider', { name: 'Opacity' });
    await opacitySlider.focus();
    await page.keyboard.press('ArrowLeft');
    await expect(page.getByText('90%')).toBeVisible();
    expect(patches).toContainEqual({
      kind: 'number',
      overlayId: 'input-state',
      key: 'opacityPercent',
      value: 90
    });

    const modelResponse = await page.evaluate(async () => {
      const response = await fetch('/api/overlay-model/input-state');
      return response.json();
    });
    expect(modelResponse.model.rootOpacity).toBe(0.9);
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
            selectedRegion: 'content',
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
        await route.fulfill({
          status: 200,
          contentType: 'application/json; charset=utf-8',
          body: JSON.stringify({ ok: true, reviewState })
        });
        return;
      }

      if (url.hostname === 'localhost' && url.pathname === '/api/overlay-model/car-radar') {
        const content = reviewState.overlays['car-radar']?.content || {};
        const warningEnabled = content['radar.multiclass-warning.practice'] !== false
          && content['Faster-class warning.practice'] !== false;
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
    await page.goto('http://localhost:8765/review/app?tab=car-radar&region=content');

    await expect(page.getByText('Radar proximity')).toHaveCount(0);
    await page.locator('.matrix-session button').first().click();

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
    const payload = browserOverlayApiResponse(overlayId, url.pathname, {
      ...fixture,
      live: resolveLiveFixture(fixture.live, advancesLiveFrame ? 'frame' : url.pathname, liveFrameIndex)
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

function resolveLiveFixture(live, path, frameIndex) {
  if (path !== 'frame') {
    return Array.isArray(live) ? live[0] : typeof live === 'function' ? live(0) : live;
  }

  if (Array.isArray(live)) {
    return live[Math.min(frameIndex, live.length - 1)];
  }

  return typeof live === 'function' ? live(frameIndex) : live;
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

function standingsDisplayModel() {
  return {
    overlayId: 'standings',
    title: 'Standings',
    status: 'scoring | 5/5 live',
    source: 'source: scoring snapshot + live timing',
    bodyKind: 'table',
    columns: [
      { id: 'standings.class-position', label: 'CLS', dataKey: 'class-position', width: 35, alignment: 'right' },
      { id: 'standings.car-number', label: 'CAR', dataKey: 'car-number', width: 50, alignment: 'right' },
      { id: 'standings.driver', label: 'Driver', dataKey: 'driver', width: 250, alignment: 'left' },
      { id: 'standings.gap', label: 'GAP', dataKey: 'gap', width: 60, alignment: 'right' },
      { id: 'standings.interval', label: 'INT', dataKey: 'interval', width: 60, alignment: 'right' },
      { id: 'standings.fastest-lap', label: 'FAST', dataKey: 'fastest-lap', width: 70, alignment: 'right' },
      { id: 'standings.last-lap', label: 'LAST', dataKey: 'last-lap', width: 70, alignment: 'right' },
      { id: 'standings.pit', label: 'PIT', dataKey: 'pit', width: 30, alignment: 'right' }
    ],
    rows: [
      headerRow('LMP2', '2 cars | ~10 laps', '#33CEFF'),
      carRow(['1', '#8', 'Proto One', 'Lap 22', '-45.0', '1:45.884', '1:46.210', '']),
      headerRow('GT3', '3 cars | ~12.4 laps', '#FFAA00'),
      carRow(['1', '#11', 'GT3 Leader', 'Lap 21', '-2.0', '1:53.112', '1:53.112', '']),
      carRow(['2', '#71', 'Focus Racer', '+3.4', '0.0', '1:54.228', '1:54.901', ''], { isReference: true }),
      carRow(['3', '#91', 'Chaser', '+8.9', '+5.5', '1:55.480', '1:56.004', 'IN'], { isPit: true })
    ],
    metrics: [],
    headerItems: [{ key: 'timeRemaining', value: '06:37:08' }]
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
