import { afterEach, describe, expect, it } from 'vitest';
import { readFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import vm from 'node:vm';
import { JSDOM } from 'jsdom';

const repoRoot = resolve(dirname(fileURLToPath(import.meta.url)), '../..');
const moduleRoot = resolve(repoRoot, 'src/TmrOverlay.App/Overlays/BrowserSources/Assets/modules');
const openHarnesses = [];

afterEach(() => {
  while (openHarnesses.length) {
    openHarnesses.pop().close();
  }
});

describe('browser runtime modules', () => {
  it.each([
    'fuel-calculator',
    'gap-to-leader',
    'pit-service',
    'relative',
    'session-weather',
    'standings'
  ])('delegates %s display models to the shared overlay renderer', async (moduleName) => {
    const model = {
      overlayId: moduleName,
      title: moduleName,
      status: 'live',
      shouldRender: true
    };
    const harness = createRuntimeModuleHarness(moduleName, { model });

    await harness.refresh();

    expect(harness.renderedOverlayModel).toBe(model);
  });

  it('renders car-radar SVG primitives from the render model', async () => {
    const harness = createRuntimeModuleHarness('car-radar', {
      model: {
        overlayId: 'car-radar',
        title: 'Car Radar',
        status: 'radar live',
        source: 'source: radar test',
        bodyKind: 'car-radar',
        carRadar: {
          renderModel: {
            width: 120,
            height: 180,
            fadeInMilliseconds: 60,
            fadeOutMilliseconds: 120,
            shouldRender: true,
            background: {
              x: 0,
              y: 0,
              width: 120,
              height: 180,
              fill: { red: 8, green: 10, blue: 12, alpha: 180 }
            },
            multiclassArc: {
              x: 12,
              y: 24,
              width: 96,
              height: 96,
              startDegrees: 0,
              sweepDegrees: 220,
              stroke: { red: 255, green: 207, blue: 74, alpha: 255 },
              strokeWidth: 4
            },
            rings: [
              {
                x: 24,
                y: 36,
                width: 72,
                height: 72,
                stroke: { red: 214, green: 220, blue: 226, alpha: 128 },
                strokeWidth: 1
              }
            ],
            cars: [
              {
                kind: 'left',
                x: 48,
                y: 104,
                width: 18,
                height: 34,
                radius: 3,
                fill: { red: 0, green: 232, blue: 255, alpha: 255 }
              }
            ],
            labels: [
              {
                x: 0,
                y: 6,
                width: 120,
                height: 20,
                alignment: 'center',
                color: { red: 247, green: 251, blue: 255, alpha: 255 },
                fontSize: 12,
                bold: true,
                text: 'CLEAR'
              }
            ]
          }
        },
        shouldRender: true
      }
    });

    await harness.refresh();

    expect(harness.document.querySelector('.radar-v2')).not.toBeNull();
    expect(harness.document.querySelector('.radar-v2 ellipse')).not.toBeNull();
    expect(harness.document.querySelector('.radar-v2 path')?.getAttribute('stroke')).toBe('rgba(255, 207, 74, 1)');
    expect(harness.document.querySelector('.radar-car-left')).not.toBeNull();
    expect(harness.document.querySelector('.radar-label').textContent).toBe('CLEAR');
    expect(harness.overlayOpacity).toBe(1);
    expect(harness.events).toEqual(expect.arrayContaining([
      expect.objectContaining({ event: 'model-render', overlayId: 'car-radar' })
    ]));
  });

  it('renders special flag visuals from the flags display model', async () => {
    const harness = createRuntimeModuleHarness('flags', {
      width: 420,
      height: 180,
      geometry: {
        flags: {
          minimumWidth: 180,
          minimumHeight: 96,
          checkeredColumns: 4,
          checkeredRows: 3
        }
      },
      model: {
        overlayId: 'flags',
        title: 'Flags',
        status: 'caution + debris + checkered',
        source: 'source: flags test',
        bodyKind: 'flags',
        flags: {
          flags: [
            { kind: 'caution', label: 'Caution', detail: 'sector 2' },
            { kind: 'debris', label: 'Debris' },
            { kind: 'checkered', label: 'Checkered' }
          ]
        },
        shouldRender: true
      }
    });

    await harness.refresh();

    expect(harness.document.querySelector('.flags-v2')).not.toBeNull();
    expect(harness.document.querySelectorAll('.flag-cell')).toHaveLength(3);
    expect(harness.document.querySelector('.flag-caution polygon')?.getAttribute('fill')).toBe('rgba(0,0,0,0.28)');
    expect(harness.document.querySelector('.flag-debris polygon')?.getAttribute('fill')).toBe('rgba(245,124,38,0.82)');
    expect(harness.document.querySelectorAll('.flag-checkered rect')).not.toHaveLength(0);
    expect(harness.overlayOpacity).toBe(1);
    expect(harness.events).toEqual(expect.arrayContaining([
      expect.objectContaining({ event: 'model-render', overlayId: 'flags' })
    ]));
  });

  it('hides the flags surface when no flag rows are renderable', async () => {
    const harness = createRuntimeModuleHarness('flags', {
      model: {
        overlayId: 'flags',
        title: 'Flags',
        status: 'clear',
        bodyKind: 'flags',
        flags: { flags: [] },
        shouldRender: true
      }
    });

    await harness.refresh();

    expect(harness.document.getElementById('content').innerHTML).toBe('');
    expect(harness.overlayOpacity).toBe(0);
    expect(harness.events).toEqual(expect.arrayContaining([
      expect.objectContaining({ event: 'model-hidden', overlayId: 'flags' })
    ]));
  });

  it('renders input-state rail-only readouts and clamps bar values', async () => {
    const harness = createRuntimeModuleHarness('input-state', {
      model: {
        overlayId: 'input-state',
        title: 'Inputs',
        status: 'live inputs',
        bodyKind: 'inputs',
        inputs: {
          hasGraph: false,
          hasRail: true,
          hasContent: true,
          showThrottle: true,
          throttle: 1.25,
          showBrake: true,
          brake: -0.2,
          brakeAbsActive: true,
          showClutch: true,
          clutch: 0.5,
          showSteering: true,
          steeringWheelAngle: Math.PI / 4,
          showGear: true,
          gear: -1,
          showSpeed: true,
          speedText: '42 mph'
        },
        shouldRender: true
      }
    });

    await harness.refresh();

    const overlay = harness.document.querySelector('.overlay');
    expect(overlay.classList.contains('input-rail-only')).toBe(true);
    expect(harness.document.querySelector('.input-graph')).toBeNull();
    expect(harness.document.querySelector('.input-rail').textContent).toContain('THR');
    expect(harness.document.querySelector('.input-rail').textContent).toContain('100%');
    expect(harness.document.querySelector('.input-rail').textContent).toContain('ABS');
    expect(harness.document.querySelector('.input-rail').textContent).toContain('0%');
    expect(harness.document.querySelector('.input-rail').textContent).toContain('CLT');
    expect(harness.document.querySelector('.input-rail').textContent).toContain('50%');
    expect(harness.document.querySelector('.input-rail').textContent).toContain('GEAR');
    expect(harness.document.querySelector('.input-rail').textContent).toContain('R');
    expect(harness.document.querySelector('.input-rail').textContent).toContain('42 mph');
    expect(harness.document.querySelector('.input-wheel svg g').getAttribute('transform')).toContain('rotate(-45');
    expect(harness.overlayOpacity).toBe(1);
  });

  it('draws graph-only input traces without mounting the rail', async () => {
    const harness = createRuntimeModuleHarness('input-state', {
      model: {
        overlayId: 'input-state',
        title: 'Inputs',
        status: 'input traces',
        bodyKind: 'inputs',
        inputs: {
          hasGraph: true,
          hasRail: false,
          hasContent: true,
          showThrottleTrace: true,
          showBrakeTrace: true,
          trace: [
            { throttle: 0, brake: 0 },
            { throttle: 0.35, brake: 0.65, brakeAbsActive: true },
            { throttle: 1, brake: 0.25 }
          ]
        },
        shouldRender: true
      }
    });

    await harness.refresh();

    const overlay = harness.document.querySelector('.overlay');
    expect(overlay.classList.contains('input-graph-only')).toBe(true);
    expect(harness.document.querySelector('.input-rail')).toBeNull();
    expect(harness.document.querySelector('.input-graph')).not.toBeNull();
    expect(harness.canvasCalls).toEqual(expect.arrayContaining([
      'bezierCurveTo',
      'lineTo',
      'stroke'
    ]));
  });

  it('clears input-state content when enabled options produce no content', async () => {
    const harness = createRuntimeModuleHarness('input-state', {
      model: {
        overlayId: 'input-state',
        title: 'Inputs',
        status: 'no enabled input rows',
        bodyKind: 'inputs',
        inputs: {
          hasGraph: false,
          hasRail: false,
          hasContent: false
        },
        shouldRender: true
      }
    });

    await harness.refresh();

    const overlay = harness.document.querySelector('.overlay');
    expect(overlay.classList.contains('input-empty')).toBe(true);
    expect(harness.document.getElementById('content').innerHTML).toBe('');
    expect(harness.overlayOpacity).toBe(0);
    expect(harness.events).toEqual(expect.arrayContaining([
      expect.objectContaining({ event: 'model-hidden', overlayId: 'input-state' })
    ]));
  });

  it('renders track-map primitives and labeled markers', async () => {
    const harness = createRuntimeModuleHarness('track-map', {
      model: {
        overlayId: 'track-map',
        title: 'Track Map',
        status: 'track live',
        bodyKind: 'track-map',
        trackMap: {
          renderModel: {
            width: 200,
            height: 120,
            primitives: [
              {
                kind: 'path',
                points: [{ x: 10, y: 10 }, { x: 60, y: 20 }, { x: 90, y: 80 }],
                stroke: { red: 214, green: 220, blue: 226, alpha: 255 },
                strokeWidth: 3
              },
              {
                kind: 'ellipse',
                rect: { x: 20, y: 30, width: 40, height: 20 },
                fill: { red: 0, green: 0, blue: 0, alpha: 0 },
                stroke: { red: 0, green: 232, blue: 255, alpha: 255 },
                strokeWidth: 2
              },
              {
                kind: 'arc',
                rect: { x: 80, y: 20, width: 60, height: 60 },
                startDegrees: -90,
                sweepDegrees: 270,
                stroke: { red: 255, green: 207, blue: 74, alpha: 255 },
                strokeWidth: 2
              },
              {
                kind: 'line',
                points: [{ x: 5, y: 100 }, { x: 190, y: 100 }],
                stroke: { red: 236, green: 76, blue: 86, alpha: 255 },
                strokeWidth: 1
              }
            ],
            markers: [
              {
                x: 110,
                y: 64,
                radius: 7,
                fill: { red: 0, green: 232, blue: 255, alpha: 255 },
                stroke: { red: 5, green: 13, blue: 17, alpha: 255 },
                strokeWidth: 1.5,
                alertRingStroke: { red: 255, green: 207, blue: 74, alpha: 255 },
                alertRingRadius: 11,
                alertRingStrokeWidth: 2,
                label: '5',
                labelFontSize: 8,
                labelColor: { red: 5, green: 13, blue: 17, alpha: 255 }
              }
            ]
          }
        },
        shouldRender: true
      }
    });

    await harness.refresh();

    expect(harness.document.querySelector('.track svg')?.getAttribute('viewBox')).toBe('0 0 200 120');
    expect(harness.document.querySelectorAll('.track path')).toHaveLength(2);
    expect(harness.document.querySelector('.track ellipse')).not.toBeNull();
    expect(harness.document.querySelector('.track line')).not.toBeNull();
    expect(harness.document.querySelectorAll('.track circle')).toHaveLength(2);
    expect(harness.document.querySelector('.track text').textContent).toBe('5');
    expect(harness.overlayOpacity).toBe(1);
  });

  it('renders garage-cover images and falls back to default art then text', async () => {
    const harness = createRuntimeModuleHarness('garage-cover', {
      model: {
        overlayId: 'garage-cover',
        title: 'Garage Cover',
        status: 'garage visible',
        bodyKind: 'garage-cover',
        garageCover: {
          shouldCover: true,
          browserSettings: {
            hasImage: true,
            imageVersion: 'abc 123',
            fallbackReason: null,
            previewVisible: false
          },
          detection: {
            displayText: 'garage visible',
            isFresh: true
          }
        },
        shouldRender: true
      }
    });

    await harness.refresh();

    const image = harness.document.querySelector('.garage-cover img');
    expect(image?.getAttribute('src')).toBe('/api/garage-cover/image?v=abc%20123');
    expect(harness.overlayOpacity).toBe(1);
    expect(harness.events).toEqual(expect.arrayContaining([
      expect.objectContaining({ event: 'model-render', overlayId: 'garage-cover' })
    ]));

    harness.dom.window.renderGarageCoverTextFallback(image);
    expect(image.dataset.fallback).toBe('default');
    expect(image.getAttribute('src')).toBe('/api/garage-cover/default-image?v=stock');

    harness.dom.window.renderGarageCoverTextFallback(image);
    expect(harness.document.querySelector('.garage-cover').classList.contains('garage-cover-fallback')).toBe(true);
    expect(harness.document.querySelector('.garage-cover').textContent).toBe('TMR');
  });

  it('renders stream-chat rows with metadata, emotes, and badge fallbacks', () => {
    const harness = createRuntimeModuleHarness('stream-chat', {
      geometry: {
        streamChat: {
          minimumAvailableHeight: 48,
          rowGap: 8,
          rowBudget: 52,
          maxRows: 4,
          availableHeightFallbackOffset: 42
        }
      }
    });

    harness.module.render({
      overlayId: 'stream-chat',
      title: 'Stream Chat',
      status: 'chat connected | twitch',
      bodyKind: 'stream-chat',
      headerItems: [{ key: 'provider', value: 'Twitch' }],
      streamChat: {
        rows: [
          {
            name: 'Viewer',
            text: 'hello Kappa',
            kind: 'message',
            authorColorHex: '#12abef',
            metadata: ['first', 'sub'],
            badges: [{ id: 'subscriber', version: '12', label: 'Subscriber' }],
            segments: [
              { kind: 'text', text: 'hello ' },
              {
                kind: 'emote',
                text: 'Kappa',
                imageUrl: 'https://static-cdn.jtvnw.net/emoticons/v2/25/default/dark/2.0'
              }
            ]
          }
        ]
      },
      shouldRender: true
    });

    expect(harness.document.querySelector('.chat-line.message')).not.toBeNull();
    expect(harness.document.querySelector('.chat-name').getAttribute('style')).toBe('color: #12abef');
    expect([...harness.document.querySelectorAll('.chat-chip')].map((chip) => chip.textContent)).toEqual(['first', 'sub']);
    expect(harness.document.querySelector('.chat-badge.fallback').textContent).toBe('Sub');
    expect(harness.document.querySelector('.chat-emote')?.getAttribute('alt')).toBe('Kappa');
    expect(harness.document.querySelector('.header-items').textContent).toBe('Twitch');
    expect(harness.overlayOpacity).toBe(1);
    expect(harness.events).toEqual(expect.arrayContaining([
      expect.objectContaining({ event: 'model-render', overlayId: 'stream-chat' })
    ]));
  });
});

function createRuntimeModuleHarness(moduleName, { geometry = {}, model = null, width = 360, height = 170 } = {}) {
  const dom = new JSDOM(
    '<!doctype html><html><head></head><body><div class="overlay"><div class="header"><div class="header-items"></div></div><div id="content"></div><div id="source"></div></div></body></html>',
    {
      pretendToBeVisual: true,
      runScripts: 'outside-only',
      url: `http://localhost:8765/overlays/${moduleName}`
    });
  const { window } = dom;
  const document = window.document;
  const events = [];
  const canvasCalls = [];
  const modulePath = resolve(moduleRoot, `${moduleName}.js`);

  Object.defineProperty(window, 'innerWidth', { value: width, configurable: true });
  Object.defineProperty(window, 'innerHeight', { value: height, configurable: true });
  installCanvasMock(window, canvasCalls);

  window.page = { id: moduleName, refreshIntervalMilliseconds: 250 };
  window.geometry = geometry;
  window.streamChatGeometry = geometry.streamChat || {};
  window.overlayEl = document.querySelector('.overlay');
  window.contentEl = document.getElementById('content');
  window.modelRootOpacity = 1;
  window.TmrBrowserOverlay = {
    module: null,
    register(registeredModule) {
      this.module = registeredModule;
    }
  };
  window.fetchOverlayModel = async () => (typeof model === 'function' ? model() : model);
  window.postBrowserSourceEvent = (event, payload) => {
    events.push({ event, overlayId: payload?.overlayId, shouldRender: payload?.shouldRender, status: payload?.status });
  };
  window.rootOpacityFromModel = (payload) => payload?.effectiveSettings?.opacity ?? 1;
  window.applyOverlayOpacity = (opacity) => {
    window.__overlayOpacity = opacity;
    window.overlayEl.style.opacity = String(opacity);
  };
  window.renderHeaderItems = (payload, fallback) => {
    document.querySelector('.header-items').textContent = (payload?.headerItems || [])
      .filter((item) => item?.key !== 'status')
      .map((item) => item.value)
      .join('') || fallback || '';
  };
  window.clearHeaderItems = () => {
    document.querySelector('.header-items').textContent = '';
  };
  window.renderFooterSource = (payload) => {
    const source = document.getElementById('source');
    source.textContent = payload?.source || '';
    source.hidden = source.textContent.length === 0;
  };
  window.clearFooterSource = () => {
    const source = document.getElementById('source');
    source.textContent = '';
    source.hidden = true;
  };
  window.escapeHtml = escapeHtml;
  window.escapeAttribute = escapeAttribute;
  window.formatPercent = formatPercent;
  window.formatNumber = formatNumber;
  window.numberOr = numberOr;
  window.renderOverlayModel = (payload) => {
    window.__renderedOverlayModel = payload;
  };
  window.requestAnimationFrame = (callback) => {
    callback();
    return 1;
  };
  window.themeColor = (_name, fallback) => fallback;
  window.themeRgba = (_name, _alpha, fallback) => fallback;
  window.fetch = async () => ({ ok: false, json: async () => ({}) });
  window.setInterval = () => 1;

  const script = new vm.Script(readFileSync(modulePath, 'utf8'), { filename: modulePath });
  script.runInContext(dom.getInternalVMContext());

  const harness = {
    dom,
    document,
    events,
    canvasCalls,
    module: window.TmrBrowserOverlay.module,
    get renderedOverlayModel() {
      return window.__renderedOverlayModel;
    },
    get overlayOpacity() {
      return window.__overlayOpacity;
    },
    async refresh() {
      if (this.module.beforeRefresh) {
        await this.module.beforeRefresh();
      }
      this.module.render();
    },
    close() {
      dom.window.close();
    }
  };
  openHarnesses.push(harness);
  return harness;
}

function installCanvasMock(window, calls) {
  const methods = [
    'beginPath',
    'bezierCurveTo',
    'clearRect',
    'clip',
    'lineTo',
    'moveTo',
    'rect',
    'restore',
    'save',
    'setTransform',
    'stroke'
  ];
  const context = {
    canvas: null,
    lineCap: '',
    lineJoin: '',
    lineWidth: 1,
    strokeStyle: ''
  };
  for (const method of methods) {
    context[method] = () => {
      calls.push(method);
    };
  }

  window.HTMLCanvasElement.prototype.getContext = function getContext(type) {
    if (type !== '2d') {
      return null;
    }
    context.canvas = this;
    return context;
  };
  window.HTMLCanvasElement.prototype.getBoundingClientRect = () => ({
    width: 240,
    height: 120,
    top: 0,
    left: 0,
    right: 240,
    bottom: 120
  });
}

function escapeHtml(value) {
  return String(value ?? '')
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
    .replaceAll("'", '&#39;');
}

function escapeAttribute(value) {
  return escapeHtml(value);
}

function formatPercent(value) {
  return Number.isFinite(value) ? `${Math.round(value * 100)}%` : '--';
}

function formatNumber(value, precision = 1) {
  const number = Number(value);
  return Number.isFinite(number) ? number.toFixed(precision).replace(/\.?0+$/, '') : '0';
}

function numberOr(value, fallback) {
  const number = Number(value);
  return Number.isFinite(number) ? number : fallback;
}
