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
    expect(currentOverlay.document.querySelector('.header-items').textContent).toBe('06:37:08');
    expect(currentOverlay.document.getElementById('source').textContent).toBe('');
    expect(currentOverlay.document.getElementById('source').hidden).toBe(true);
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
});

function cssColor(value) {
  const match = /^#([0-9a-f]{6})([0-9a-f]{2})?$/i.exec(value);
  if (!match) return value;
  if (!match[2]) return `#${match[1].toLowerCase()}`;
  const rgb = Number.parseInt(match[1], 16);
  const alpha = Number.parseInt(match[2], 16) / 255;
  return `rgba(${(rgb >> 16) & 255}, ${(rgb >> 8) & 255}, ${rgb & 255}, ${Number(alpha.toFixed(3))})`;
}
