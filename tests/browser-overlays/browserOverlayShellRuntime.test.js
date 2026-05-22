import { afterEach, describe, expect, it } from 'vitest';
import { readFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import vm from 'node:vm';
import { JSDOM } from 'jsdom';

const repoRoot = resolve(dirname(fileURLToPath(import.meta.url)), '../..');
const overlayShellPath = resolve(repoRoot, 'src/TmrOverlay.App/Overlays/BrowserSources/Assets/scripts/overlay-shell.js');
const openHarnesses = [];

afterEach(() => {
  while (openHarnesses.length) {
    openHarnesses.pop().close();
  }
});

describe('browser overlay shell runtime', () => {
  it('fetches renderable table models, forwards client query parameters, and posts render events', async () => {
    const harness = createOverlayShellHarness({
      query: '?fixture=race&client=obs-unit&clientKind=obs',
      page: {
        id: 'relative',
        title: 'Relative',
        forwardQueryParameters: ['fixture']
      },
      model: {
        overlayId: 'relative',
        title: 'Relative',
        status: 'scoring | race',
        bodyKind: 'table',
        source: 'source: shell test',
        rootOpacity: 0.72,
        columns: [
          { label: 'Pos', dataKey: 'position', width: 48, alignment: 'right' },
          { label: 'Driver', dataKey: 'driver', width: 180, alignment: 'left' }
        ],
        rows: [
          { cells: ['5', '#55 Focus Driver'], isReference: true, carClassColorHex: '#00e8ff' }
        ],
        metrics: [],
        headerItems: [
          { key: 'status', value: 'scoring | race' },
          { key: 'timeRemaining', value: '06:37:08', tone: 'warning' }
        ],
        shouldRender: true,
        effectiveSettings: {
          rendered: {
            browserSource: {
              baseWidth: 322,
              baseHeight: 308
            }
          }
        }
      }
    });

    await harness.refresh();

    expect(harness.fetchCalls).toContain('/api/overlay-model/relative?fixture=race');
    expect(harness.fetchCalls).toContain('/api/browser-source-event?fixture=race');
    expect(harness.document.querySelector('.overlay').style.opacity).toBe('0.72');
    expect(harness.document.querySelector('.overlay').style.getPropertyValue('--relative-overlay-width')).toBe('322px');
    expect(harness.document.querySelector('.overlay').classList.contains('has-header-band')).toBe(true);
    expect(harness.document.getElementById('time-remaining').textContent).toBe('06:37:08');
    expect(harness.document.querySelector('table').textContent).toContain('#55 Focus Driver');
    expect(harness.document.querySelector('col').getAttribute('style')).toBe('width:48px;');
    expect(harness.document.getElementById('source').hidden).toBe(true);
    expect(harness.events).toEqual(expect.arrayContaining([
      expect.objectContaining({
        event: 'page-loaded',
        overlayId: 'relative',
        clientId: 'obs-unit',
        clientKind: 'obs'
      }),
      expect.objectContaining({
        event: 'model-render',
        overlayId: 'relative',
        shouldRender: true,
        status: 'scoring | race'
      })
    ]));
  });

  it('clears content and chrome for product-hidden models', async () => {
    const harness = createOverlayShellHarness({
      page: {
        id: 'fuel-calculator',
        title: 'Fuel Calculator'
      },
      model: {
        overlayId: 'fuel-calculator',
        title: 'Fuel Calculator',
        status: 'hidden | no enabled content',
        bodyKind: 'metrics',
        rootOpacity: 0.5,
        metrics: [{ label: 'Stint', value: '31 laps' }],
        headerItems: [{ key: 'timeRemaining', value: '06:37:08' }],
        shouldRender: false
      }
    });

    await harness.refresh();

    expect(harness.document.getElementById('content').innerHTML).toBe('');
    expect(harness.document.querySelector('.overlay').style.opacity).toBe('0');
    expect(harness.document.querySelector('.overlay').classList.contains('has-header-band')).toBe(false);
    expect(harness.document.querySelector('.header-items').textContent).toBe('');
    expect(harness.document.getElementById('source').hidden).toBe(true);
    expect(harness.events).toEqual(expect.arrayContaining([
      expect.objectContaining({
        event: 'model-hidden',
        overlayId: 'fuel-calculator',
        shouldRender: false,
        status: 'hidden | no enabled content'
      })
    ]));
  });

  it('renders graph models through the shell canvas path', async () => {
    const harness = createOverlayShellHarness({
      page: {
        id: 'gap-to-leader',
        title: 'Gap To Leader'
      },
      geometry: {
        gapGraph: {
          axisWidth: 40,
          xAxisHeight: 17,
          endpointLabelLaneWidth: 30,
          metricsTableGap: 8
        }
      },
      model: {
        overlayId: 'gap-to-leader',
        title: 'Gap To Leader',
        status: 'live | race',
        bodyKind: 'graph',
        points: [4.5, 3.2, 2.1, 1.8],
        graph: {
          showGraph: true,
          showTrendMetrics: false
        },
        metrics: [],
        rows: [],
        headerItems: [],
        shouldRender: true
      }
    });

    await harness.refresh();

    expect(harness.document.querySelector('.model-graph')).not.toBeNull();
    expect(harness.document.querySelector('.overlay').style.getPropertyValue('--gap-panel-width')).toBe('410px');
    expect(harness.canvasCalls).toEqual(expect.arrayContaining([
      'setTransform',
      'clearRect',
      'moveTo',
      'lineTo',
      'fillText',
      'stroke'
    ]));
  });

  it('renders null model fallback without inventing source chrome', async () => {
    const harness = createOverlayShellHarness({
      page: {
        id: 'standings',
        title: 'Standings'
      },
      model: null
    });

    await harness.refresh();

    expect(harness.document.getElementById('content').textContent).toContain('Waiting for overlay model.');
    expect(harness.document.querySelector('.header-items').textContent).toBe('');
    expect(harness.document.querySelector('.overlay').classList.contains('has-header-band')).toBe(false);
    expect(harness.document.getElementById('source').hidden).toBe(true);
    expect(harness.events).toEqual(expect.arrayContaining([
      expect.objectContaining({
        event: 'model-null',
        overlayId: 'standings',
        shouldRender: null
      })
    ]));
  });
});

function createOverlayShellHarness({
  page,
  model,
  query = '',
  geometry = {}
}) {
  const fetchCalls = [];
  const events = [];
  const canvasCalls = [];
  const dom = new JSDOM(
    '<!doctype html><html><head></head><body><section class="overlay"><div class="header" hidden><div class="header-items"></div></div><div id="content" class="content"><div class="empty">Waiting for live telemetry.</div></div><div id="source" class="source" hidden></div></section></body></html>',
    {
      pretendToBeVisual: true,
      runScripts: 'outside-only',
      url: `http://localhost:8765/overlays/${page.id}${query}`
    });
  const { window } = dom;
  installCanvasMock(window, canvasCalls);
  window.setInterval = () => 1;
  window.fetch = async (input, init = {}) => {
    const url = new URL(String(input), window.location.href);
    fetchCalls.push(`${url.pathname}${url.search}`);
    if (url.pathname === '/api/browser-source-event') {
      events.push(JSON.parse(String(init.body || '{}')));
      return jsonResponse({ ok: true });
    }
    if (url.pathname === `/api/overlay-model/${page.id}`) {
      return jsonResponse({ model });
    }
    return {
      ok: false,
      status: 404,
      json: async () => ({})
    };
  };

  const script = new vm.Script(overlayShellSource(page, geometry), { filename: overlayShellPath });
  script.runInContext(dom.getInternalVMContext());
  const harness = {
    dom,
    document: window.document,
    fetchCalls,
    events,
    canvasCalls,
    refresh: () => window.__tmrRefresh(),
    close: () => window.close()
  };
  openHarnesses.push(harness);
  return harness;
}

function overlayShellSource(page, geometry) {
  return readFileSync(overlayShellPath, 'utf8')
    .replace('{{PAGE_JSON}}', JSON.stringify({
      refreshIntervalMilliseconds: 250,
      forwardQueryParameters: [],
      ...page
    }))
    .replace('{{GEOMETRY_JSON}}', JSON.stringify({
      gapGraph: {},
      metricRows: {},
      ...geometry
    }))
    .replace('{{MODULE_SCRIPT}}', `
      let shellRuntimeModel = null;
      TmrBrowserOverlay.register({
        start({ refresh }) {
          window.__tmrRefresh = refresh;
        },
        async beforeRefresh() {
          shellRuntimeModel = await fetchOverlayModel(page.id);
        },
        render() {
          renderOverlayModel(shellRuntimeModel);
        }
      });
    `);
}

function installCanvasMock(window, calls) {
  const target = {
    measureText: (text) => ({ width: String(text || '').length * 7 }),
    getImageData: () => ({ data: new Uint8ClampedArray([0, 0, 0, 255]) })
  };
  const context = new Proxy(target, {
    get(current, property) {
      if (property in current) {
        return current[property];
      }
      if (typeof property === 'string') {
        current[property] = (..._args) => {
          calls.push(property);
        };
        return current[property];
      }
      return undefined;
    }
  });

  window.HTMLCanvasElement.prototype.getContext = function getContext(type) {
    return type === '2d' ? context : null;
  };
  window.HTMLCanvasElement.prototype.getBoundingClientRect = () => ({
    width: 320,
    height: 180,
    top: 0,
    left: 0,
    right: 320,
    bottom: 180
  });
}

function jsonResponse(payload) {
  return {
    ok: true,
    status: 200,
    json: async () => payload
  };
}
