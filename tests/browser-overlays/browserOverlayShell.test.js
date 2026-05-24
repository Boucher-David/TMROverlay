import { afterEach, describe, expect, it } from 'vitest';
import { readFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { freshLiveSnapshot, renderBrowserOverlay, waitFor } from './browserOverlayTestHost.js';

let currentOverlay;
const repoRoot = resolve(dirname(fileURLToPath(import.meta.url)), '../..');
const sharedContract = JSON.parse(readFileSync(resolve(repoRoot, 'shared/tmr-overlay-contract.json'), 'utf8'));

afterEach(() => {
  currentOverlay?.close();
  currentOverlay = null;
});

describe('browser overlay shell', () => {
  it('uses shared design token CSS variables from the repo contract', async () => {
    currentOverlay = await renderBrowserOverlay('relative', {
      live: {
        isConnected: false,
        isCollecting: false,
        lastUpdatedAtUtc: null,
        sequence: 0,
        models: {}
      },
      waitForSelector: null
    });

    const styleText = currentOverlay.document.querySelector('style').textContent;
    expect(styleText).toContain(`--tmr-cyan: ${cssColor(sharedContract.design.v2.colors.cyan)};`);
    expect(styleText).toContain(`--tmr-magenta: ${cssColor(sharedContract.design.v2.colors.magenta)};`);
    expect(styleText).toContain(`--tmr-amber: ${cssColor(sharedContract.design.v2.colors.amber)};`);
    expect(styleText).toContain(`--tmr-surface: ${cssColor(sharedContract.design.v2.colors.surface)};`);
  });

  it('uses model renderability for unavailable telemetry states', async () => {
    currentOverlay = await renderBrowserOverlay('standings', {
      live: {
        isConnected: false,
        isCollecting: false,
        lastUpdatedAtUtc: null,
        sequence: 0,
        models: {}
      },
      model: {
        overlayId: 'standings',
        title: 'Standings',
        status: 'iRacing disconnected',
        source: 'source: waiting',
        bodyKind: 'table',
        columns: [],
        rows: [],
        metrics: [],
        points: [],
        headerItems: [{ key: 'status', value: 'iRacing disconnected' }],
        shouldRender: false
      },
      waitForSelector: null
    });

    await waitFor(() => currentOverlay.document.querySelector('.overlay').style.opacity === '0');

    expect(currentOverlay.fetchCalls).toContain('/api/overlay-model/standings');
    expect(currentOverlay.fetchCalls).not.toContain('/api/snapshot');
    expect(currentOverlay.document.getElementById('content').textContent).toBe('');
    expect(currentOverlay.document.querySelector('.overlay').style.opacity).toBe('0');
    expect(currentOverlay.document.getElementById('status')).toBeNull();
    expect(currentOverlay.document.querySelector('.header-items').textContent).toBe('');
    expect(currentOverlay.dom.window.TmrBrowserModel).toBeUndefined();
  });

  it('renders table column widths as configured pixels without proportional stretching', async () => {
    currentOverlay = await renderBrowserOverlay('relative', {
      live: freshLiveSnapshot({}),
      model: {
        overlayId: 'relative',
        title: 'Relative',
        status: 'live | race',
        source: '',
        bodyKind: 'table',
        columns: [
          { label: 'Pos', dataKey: 'relative-position', width: 48, alignment: 'right' },
          { label: 'Driver', dataKey: 'driver', width: 240, alignment: 'left' }
        ],
        rows: [{ cells: ['5', '#55 Focus Driver'], isReference: true }],
        metrics: [],
        points: [],
        headerItems: [],
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

    const table = currentOverlay.document.querySelector('table');
    const columnStyles = [...currentOverlay.document.querySelectorAll('col')]
      .map((column) => column.getAttribute('style'));
    const headerStyles = [...currentOverlay.document.querySelectorAll('th')]
      .map((cell) => cell.getAttribute('style'));
    const cellStyles = [...currentOverlay.document.querySelectorAll('td')]
      .map((cell) => cell.getAttribute('style'));

    expect(table.getAttribute('style')).toContain('width:288px');
    expect(table.getAttribute('style')).toContain('min-width:288px');
    expect(columnStyles).toEqual(['width:48px;', 'width:240px;']);
    expect(headerStyles[0]).toContain('width:48px');
    expect(headerStyles[1]).toContain('width:240px');
    expect(cellStyles[0]).toContain('width:48px');
    expect(cellStyles[1]).toContain('width:240px');
    expect(currentOverlay.document.querySelector('.overlay').style.getPropertyValue('--relative-overlay-width')).toBe('322px');
  });

  it('renders steering wheel visual direction opposite the raw telemetry sign', async () => {
    currentOverlay = await renderBrowserOverlay('input-state', {
      live: freshLiveSnapshot({}),
      waitForSelector: '.input-wheel svg g',
      model: {
        overlayId: 'input-state',
        title: 'Inputs',
        status: 'live inputs',
        source: '',
        bodyKind: 'inputs',
        columns: [],
        rows: [],
        metrics: [],
        points: [],
        headerItems: [],
        shouldRender: true,
        inputs: {
          isAvailable: true,
          hasRail: true,
          hasGraph: false,
          hasContent: true,
          showSteering: true,
          steeringWheelAngle: Math.PI / 6,
          steeringWheelVisualAngle: -Math.PI / 6,
          steeringText: '+30 deg'
        }
      }
    });

    const transform = currentOverlay.document.querySelector('.input-wheel svg g').getAttribute('transform');
    const degrees = Number(/rotate\(([-0-9.]+)/.exec(transform)?.[1]);

    expect(currentOverlay.document.querySelector('.input-wheel-value').textContent).toBe('+30 deg');
    expect(degrees).toBeCloseTo(-30, 5);
  });

  it('posts browser-source events with spoofable OBS client identity', async () => {
    currentOverlay = await renderBrowserOverlay('standings', {
      live: {
        isConnected: false,
        isCollecting: false,
        lastUpdatedAtUtc: null,
        sequence: 0,
        models: {}
      },
      model: {
        overlayId: 'standings',
        title: 'Standings',
        status: 'hidden | telemetry unavailable',
        source: '',
        bodyKind: 'table',
        columns: [],
        rows: [],
        metrics: [],
        points: [],
        headerItems: [],
        shouldRender: false
      },
      query: '?client=obs-test&clientKind=obs',
      userAgent: 'Mozilla/5.0 OBS Studio/32.1.2',
      waitForSelector: null
    });

    await waitFor(() => currentOverlay.browserSourceEvents.some((event) => event.event === 'model-hidden'));

    expect(currentOverlay.fetchCalls).toContain('/api/browser-source-event');
    expect(currentOverlay.browserSourceEvents).toEqual(expect.arrayContaining([
      expect.objectContaining({
        event: 'page-loaded',
        overlayId: 'standings',
        clientId: 'obs-test',
        clientKind: 'obs'
      }),
      expect.objectContaining({
        event: 'model-hidden',
        overlayId: 'standings',
        clientId: 'obs-test',
        clientKind: 'obs',
        shouldRender: false,
        status: 'hidden | telemetry unavailable'
      })
    ]));
  });

  it('keeps polling while hidden and renders when the model becomes active without a reload', async () => {
    let currentModel = {
      overlayId: 'standings',
      title: 'Standings',
      status: 'disabled | product hidden',
      source: '',
      bodyKind: 'table',
      columns: [],
      rows: [],
      metrics: [],
      points: [],
      headerItems: [],
      shouldRender: false
    };
    currentOverlay = await renderBrowserOverlay('standings', {
      live: freshLiveSnapshot({}),
      model: () => currentModel,
      waitForSelector: null
    });

    await waitFor(() => currentOverlay.document.querySelector('.overlay').style.opacity === '0');
    await waitFor(() => currentOverlay.fetchCalls.filter((path) => path === '/api/overlay-model/standings').length >= 2);

    currentModel = {
      overlayId: 'standings',
      title: 'Standings',
      status: 'scoring | race',
      source: 'source: scoring telemetry',
      bodyKind: 'table',
      columns: [{ label: 'Driver', dataKey: 'driver', width: 140, align: 'left' }],
      rows: [{ cells: ['Driver 1'], isReference: true }],
      metrics: [],
      points: [],
      headerItems: [],
      shouldRender: true
    };

    await waitFor(() => currentOverlay.document.querySelector('.overlay').style.opacity === '1');
    expect(currentOverlay.document.querySelector('tbody').textContent).toContain('Driver 1');
    expect(currentOverlay.browserSourceEvents).toEqual(expect.arrayContaining([
      expect.objectContaining({ event: 'model-hidden', overlayId: 'standings' }),
      expect.objectContaining({ event: 'model-render', overlayId: 'standings', shouldRender: true })
    ]));
  });

  it('does not invent header or footer chrome when the model omits shared chrome items', async () => {
    currentOverlay = await renderBrowserOverlay('fuel-calculator', {
      live: freshLiveSnapshot({}),
      model: {
        overlayId: 'fuel-calculator',
        title: 'Fuel Calculator',
        status: 'need fuel',
        source: '',
        bodyKind: 'metrics',
        columns: [],
        rows: [],
        metrics: [
          { label: 'Plan', value: '31 laps | 3 stints | 2 stops', tone: 'modeled' }
        ],
        points: [],
        headerItems: [],
        gridSections: [],
        metricSections: []
      },
      waitForSelector: '.metric'
    });

    expect(currentOverlay.document.getElementById('status')).toBeNull();
    expect(currentOverlay.document.querySelector('.header').hidden).toBe(true);
    expect(currentOverlay.document.querySelector('.overlay').classList.contains('has-header-band')).toBe(false);
    expect(currentOverlay.document.querySelector('.header-items').textContent).toBe('');
    expect(currentOverlay.document.getElementById('source').textContent).toBe('');
    expect(currentOverlay.document.getElementById('source').hidden).toBe(true);
  });

  it('filters legacy status header items and source footer text from visible chrome', async () => {
    currentOverlay = await renderBrowserOverlay('fuel-calculator', {
      live: freshLiveSnapshot({}),
      model: {
        overlayId: 'fuel-calculator',
        title: 'Fuel Calculator',
        status: 'need fuel',
        source: 'source: model evidence',
        bodyKind: 'metrics',
        columns: [],
        rows: [],
        metrics: [
          { label: 'Plan', value: '31 laps | 3 stints | 2 stops', tone: 'modeled' }
        ],
        points: [],
        headerItems: [
          { key: 'status', value: 'need fuel' },
          { key: 'timeRemaining', value: '06:37:08' }
        ],
        gridSections: [],
        metricSections: []
      },
      waitForSelector: '.metric'
    });

    expect(currentOverlay.document.getElementById('status')).toBeNull();
    expect(currentOverlay.document.querySelector('.header').hidden).toBe(false);
    expect(currentOverlay.document.querySelector('.overlay').classList.contains('has-header-items')).toBe(true);
    expect(currentOverlay.document.querySelector('.header-items').textContent).toBe('06:37:08');
    expect(currentOverlay.document.getElementById('source').textContent).toBe('');
    expect(currentOverlay.document.getElementById('source').hidden).toBe(true);
  });

  it('applies model header item tone evidence to visible chrome', async () => {
    currentOverlay = await renderBrowserOverlay('fuel-calculator', {
      live: freshLiveSnapshot({}),
      model: {
        overlayId: 'fuel-calculator',
        title: 'Fuel Calculator',
        status: 'need fuel',
        source: '',
        bodyKind: 'metrics',
        columns: [],
        rows: [],
        metrics: [
          { label: 'Plan', value: '31 laps | 3 stints | 2 stops', tone: 'modeled' }
        ],
        points: [],
        headerItems: [
          { key: 'timeRemaining', value: '06:37:08', tone: 'warning' }
        ],
        gridSections: [],
        metricSections: []
      },
      waitForSelector: '.metric'
    });

    const timeRemaining = currentOverlay.document.getElementById('time-remaining');
    expect(timeRemaining.textContent).toBe('06:37:08');
    expect(timeRemaining.dataset.tone).toBe('warning');
    expect(timeRemaining.classList.contains('warning')).toBe(true);
  });

  it('hides overlays when the production display model is not renderable', async () => {
    currentOverlay = await renderBrowserOverlay('fuel-calculator', {
      live: freshLiveSnapshot({}),
      model: {
        overlayId: 'fuel-calculator',
        title: 'Fuel Calculator',
        status: 'waiting for local fuel context',
        source: 'source: waiting',
        bodyKind: 'metrics',
        columns: [],
        rows: [],
        metrics: [],
        points: [],
        headerItems: [{ key: 'status', value: 'waiting for local fuel context' }],
        gridSections: [],
        metricSections: [],
        shouldRender: false
      },
      waitForSelector: null
    });

    await waitFor(() => currentOverlay.document.querySelector('.overlay').style.opacity === '0');

    expect(currentOverlay.document.getElementById('content').textContent).toBe('');
    expect(currentOverlay.document.getElementById('status')).toBeNull();
    expect(currentOverlay.document.getElementById('source').hidden).toBe(true);
  });

  it.each([
    ['input-state', 'inputs'],
    ['car-radar', 'car-radar'],
    ['track-map', 'track-map'],
    ['flags', 'flags'],
    ['garage-cover', 'garage-cover'],
    ['stream-chat', 'stream-chat']
  ])('clears stale asset-backed DOM when %s model is product-hidden', async (overlayId, bodyKind) => {
    currentOverlay = await renderBrowserOverlay(overlayId, {
      live: freshLiveSnapshot({}),
      model: {
        overlayId,
        title: overlayId,
        status: 'disabled | product hidden',
        source: '',
        bodyKind,
        columns: [],
        rows: [],
        metrics: [],
        points: [],
        headerItems: [],
        shouldRender: false
      },
      waitForSelector: null
    });

    await waitFor(() => currentOverlay.document.querySelector('.overlay').style.opacity === '0');

    expect(currentOverlay.document.getElementById('content').textContent).toBe('');
    expect(currentOverlay.document.querySelector('.overlay').style.opacity).toBe('0');
    expect(currentOverlay.document.querySelector('.header-items')?.textContent ?? '').toBe('');
    expect(currentOverlay.document.getElementById('source').hidden).toBe(true);
  });

  it('does not mount Garage Cover content when fresh telemetry says the garage is hidden', async () => {
    currentOverlay = await renderBrowserOverlay('garage-cover', {
      live: freshLiveSnapshot({
        raceEvents: { hasData: true, isGarageVisible: false, isInGarage: false, isOnTrack: true }
      }),
      settings: { hasImage: false, imageVersion: null, fallbackReason: 'test', previewVisible: false },
      waitForSelector: null
    });

    await waitFor(() => currentOverlay.document.querySelector('.overlay').style.opacity === '0');

    expect(currentOverlay.document.querySelector('.garage-cover')).toBeNull();
    expect(currentOverlay.document.getElementById('content').textContent).toBe('');
    expect(currentOverlay.document.querySelector('.overlay').style.opacity).toBe('0');
    expect(currentOverlay.document.querySelector('.header-items')?.textContent ?? '').toBe('');
    expect(currentOverlay.document.getElementById('source').hidden).toBe(true);
  });

  it('keeps Garage Cover hidden when the model fetch fails', async () => {
    currentOverlay = await renderBrowserOverlay('garage-cover', {
      live: freshLiveSnapshot({
        raceEvents: { hasData: true, isGarageVisible: true, isInGarage: true, isOnTrack: false }
      }),
      failModelFetch: true,
      waitForSelector: null
    });

    await waitFor(() => currentOverlay.document.querySelector('.overlay').style.opacity === '0');

    expect(currentOverlay.fetchCalls).toContain('/api/overlay-model/garage-cover');
    expect(currentOverlay.document.querySelector('.garage-cover')).toBeNull();
    expect(currentOverlay.document.getElementById('content').textContent).toBe('');
    expect(currentOverlay.document.querySelector('.overlay').style.opacity).toBe('0');
    expect(currentOverlay.browserSourceEvents).toEqual(expect.arrayContaining([
      expect.objectContaining({ event: 'model-error', overlayId: 'garage-cover' }),
      expect.objectContaining({ event: 'model-hidden', overlayId: 'garage-cover', shouldRender: null })
    ]));
  });
});

function cssColor(value) {
  const match = /^#([0-9a-f]{6})([0-9a-f]{2})?$/i.exec(value);
  if (!match) return value;
  if (!match[2]) return `#${match[1].toLowerCase()}`;
  const rgb = Number.parseInt(match[1], 16);
  const alpha = Number.parseInt(match[2], 16) / 255;
  return `rgba(${(rgb >> 16) & 255}, ${(rgb >> 8) & 255}, ${rgb & 255}, ${Number(alpha.toFixed(3))})`;
}
