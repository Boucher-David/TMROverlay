#!/usr/bin/env node
import { chromium } from '@playwright/test';
import { spawn } from 'node:child_process';
import {
  existsSync,
  mkdirSync,
  readFileSync,
  rmSync,
  statSync,
  writeFileSync
} from 'node:fs';
import { createHash } from 'node:crypto';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { browserOverlayPages } from '../../tests/browser-overlays/browserOverlayAssets.js';

const repoRoot = resolve(dirname(fileURLToPath(import.meta.url)), '../..');
const overlayPages = browserOverlayPages();
const overlayIds = overlayPages.map((page) => page.page.id);
const overlayPagesById = new Map(overlayPages.map((page) => [page.page.id, page]));
const sharedChromeOverlayIds = new Set([
  'standings',
  'relative',
  'fuel-calculator',
  'gap-to-leader',
  'session-weather',
  'pit-service'
]);
const configuredCanvasOverlaySizes = new Map([
  ['car-radar', { width: 300, height: 300 }],
  ['track-map', { width: 360, height: 360 }],
  ['flags', { width: 360, height: 170 }]
]);
const configuredCanvasCaptureBackdrop = {
  kind: 'solid-color',
  color: '#14191d',
  colorRgb: 'rgb(20, 25, 29)',
  scope: 'configured-canvas-screenshot-only'
};
const previewModes = ['practice', 'qualifying', 'race'];
const nonHappyPathOverlayVariants = [
  { overlayId: 'fuel-calculator', slug: 'waiting', query: 'fixture=fuel-waiting' },
  { overlayId: 'session-weather', slug: 'missing', query: 'fixture=session-weather-missing' },
  { overlayId: 'pit-service', slug: 'idle', query: 'fixture=pit-service-idle' },
  { overlayId: 'input-state', slug: 'waiting', query: 'fixture=input-waiting' },
  { overlayId: 'input-state', slug: 'no-content', query: 'fixture=input-no-content' },
  { overlayId: 'car-radar', slug: 'left', query: 'fixture=car-radar-left' },
  { overlayId: 'car-radar', slug: 'right', query: 'fixture=car-radar-right' },
  { overlayId: 'car-radar', slug: 'both-sides', query: 'fixture=car-radar-both-sides' },
  { overlayId: 'car-radar', slug: 'clear', query: 'fixture=car-radar-clear' },
  { overlayId: 'gap-to-leader', slug: 'no-cars', query: 'fixture=gap-no-cars' },
  { overlayId: 'track-map', slug: 'no-markers', query: 'fixture=track-map-no-markers' },
  { overlayId: 'flags', slug: 'all-kinds', query: 'fixture=flags-all-kinds' },
  { overlayId: 'garage-cover', slug: 'hidden', query: 'fixture=garage-hidden' },
  { overlayId: 'garage-cover', slug: 'garage-visible', query: 'fixture=garage-visible' },
  { overlayId: 'garage-cover', slug: 'stale', query: 'fixture=garage-stale' },
  { overlayId: 'garage-cover', slug: 'disconnected', query: 'fixture=garage-disconnected' },
  { overlayId: 'stream-chat', slug: 'twitch-rich', query: 'fixture=stream-chat-twitch-rich' },
  { overlayId: 'stream-chat', slug: 'streamlabs-configured', query: 'fixture=stream-chat-streamlabs-configured' }
];

const args = parseArgs(process.argv.slice(2));
const outputRoot = resolve(repoRoot, args.output || defaultOutputFor(args.surface));
const port = args.port || Number.parseInt(process.env.TMR_BROWSER_SCREENSHOT_PORT || '5199', 10);
const baseUrl = stripTrailingSlash(args.baseUrl || `http://127.0.0.1:${port}`);
const settleMilliseconds = args.settleMilliseconds ?? 350;
const screenshotUnitSystem = normalizeUnitSystem(process.env.TMR_REVIEW_UNIT_SYSTEM || process.env.TMR_UNIT_SYSTEM || 'Metric');
let serverProcess = null;

try {
  if (!args.baseUrl) {
    serverProcess = startReviewServer(port);
  }

  await waitForServer(`${baseUrl}${serverProbePath(args.surface)}`);
  rmSync(outputRoot, { recursive: true, force: true });
  mkdirSync(outputRoot, { recursive: true });

  const browser = await chromium.launch();
  const context = await browser.newContext({
    viewport: { width: 1280, height: 760 },
    deviceScaleFactor: 1
  });
  const page = await context.newPage();
  const manifest = [];

  for (const route of screenshotRoutes(args.surface)) {
    await captureRoute(page, route, manifest);
  }

  await browser.close();
  writeFileSync(
    join(outputRoot, 'manifest.json'),
    `${JSON.stringify({
      generatedAtUtc: new Date().toISOString(),
      baseUrl,
      surfaceMode: args.surface,
      unitSystem: screenshotUnitSystem,
      screenshots: manifest
    }, null, 2)}\n`);
  console.log(`Wrote ${manifest.length} ${args.surface} screenshots to ${outputRoot}`);
} finally {
  if (serverProcess) {
    serverProcess.kill('SIGTERM');
  }
}

function screenshotRoutes(surface) {
  const routes = [];
  if (surface === 'browser-review' || surface === 'all') {
    routes.push(
      settingsRoute('settings/general.png', '/review/app', { tab: 'general', region: 'general' }),
      settingsRoute('settings/diagnostics.png', '/review/app?tab=support', { tab: 'support', region: 'general' }),
      settingsRoute('settings/support.png', '/review/app?tab=support', { tab: 'support', region: 'general', pathAlias: 'windows-settings-support' }),
      settingsRoute('settings/inputs.png', '/review/app?tab=input-state', { tab: 'input-state', overlayId: 'input-state', region: 'general', pathAlias: 'windows-settings-inputs' }),
      settingsRoute('settings/inputs-content.png', '/review/app?tab=input-state&region=content', { tab: 'input-state', overlayId: 'input-state', region: 'content', pathAlias: 'windows-settings-inputs-content' }),
      ...settingsComponentRoutes(),
      installerReviewRoute('review-installer/welcome.png', '/review/installer?menu=welcome', { menuId: 'welcome' }),
      installerReviewRoute('review-installer/installer-page-02.png', '/review/installer?menu=installer-page-02', { menuId: 'installer-page-02' }),
      installerReviewRoute('review-installer/ready-to-install.png', '/review/installer?menu=ready-to-install', { menuId: 'ready-to-install' }),
      installerReviewRoute('review-installer/cancel-confirm.png', '/review/installer?menu=cancel-confirm', { menuId: 'cancel-confirm' }),
      ...previewModes.map((mode) =>
        settingsRoute(
          `settings/general-preview-${mode}.png`,
          `/review/app?preview=${encodeURIComponent(mode)}`,
          { tab: 'general', region: 'general', previewMode: mode }))
    );

    for (const overlayId of overlayIds) {
      for (const region of regionsForOverlay(overlayId)) {
        const suffix = region === 'general' ? '' : `-${region}`;
        routes.push(settingsRoute(
          `settings/${overlayId}${suffix}.png`,
          `/review/app?tab=${encodeURIComponent(overlayId)}${region === 'general' ? '' : `&region=${encodeURIComponent(region)}`}`,
          { tab: overlayId, overlayId, region }));
      }
    }
  }

  for (const overlayId of overlayIds) {
    if (surface === 'browser-review' || surface === 'all') {
      routes.push(overlayRoute(
        `browser-overlays/${overlayId}.png`,
        withPreview(`/review/overlays/${encodeURIComponent(overlayId)}`, 'race'),
        { surface: 'browser-review-overlay', overlayId, previewMode: 'race' }));
      if (overlayId === 'track-map') {
        routes.push(overlayRoute(
          'browser-overlays/track-map-fallback.png',
          `${withPreview('/review/overlays/track-map', 'race')}&trackMap=fallback`,
          { surface: 'browser-review-overlay', overlayId, previewMode: 'race', fixtureVariant: 'circle-fallback' }));
      }
      for (const variant of nonHappyPathOverlayVariants.filter((item) => item.overlayId === overlayId)) {
        routes.push(overlayRoute(
          `browser-overlays/${overlayId}-${variant.slug}.png`,
          `${withPreview(`/review/overlays/${encodeURIComponent(overlayId)}`, 'race')}&${variant.query}`,
          { surface: 'browser-review-overlay', overlayId, previewMode: 'race', fixtureVariant: variant.slug, minBytes: variantMinBytes(variant) }));
      }
    }
    if (surface === 'localhost' || surface === 'all') {
      routes.push(overlayRoute(
        `localhost-overlays/${overlayId}.png`,
        withPreview(`/overlays/${encodeURIComponent(overlayId)}`, 'race'),
        { surface: 'localhost-overlay', overlayId, previewMode: 'race' }));
      if (overlayId === 'track-map') {
        routes.push(overlayRoute(
          'localhost-overlays/track-map-fallback.png',
          `${withPreview('/overlays/track-map', 'race')}&trackMap=fallback`,
          { surface: 'localhost-overlay', overlayId, previewMode: 'race', fixtureVariant: 'circle-fallback' }));
      }
      for (const variant of nonHappyPathOverlayVariants.filter((item) => item.overlayId === overlayId)) {
        routes.push(overlayRoute(
          `localhost-overlays/${overlayId}-${variant.slug}.png`,
          `${withPreview(`/overlays/${encodeURIComponent(overlayId)}`, 'race')}&${variant.query}`,
          { surface: 'localhost-overlay', overlayId, previewMode: 'race', fixtureVariant: variant.slug, minBytes: variantMinBytes(variant) }));
      }
      for (const alias of localhostAliasesForOverlay(overlayId)) {
        routes.push(overlayRoute(
          `localhost-overlays/${overlayId}-alias-${aliasSlug(alias)}.png`,
          withPreview(alias, 'race'),
          { surface: 'localhost-overlay', overlayId, previewMode: 'race', routeAlias: alias }));
      }
    }
    for (const mode of previewModesForOverlay(overlayId)) {
      if (surface === 'browser-review' || surface === 'all') {
        routes.push(overlayRoute(
          `browser-overlays/${overlayId}-${mode}.png`,
          withPreview(`/review/overlays/${encodeURIComponent(overlayId)}`, mode),
          { surface: 'browser-review-overlay', overlayId, previewMode: mode }));
      }
      if (surface === 'localhost' || surface === 'all') {
        routes.push(overlayRoute(
          `localhost-overlays/${overlayId}-${mode}.png`,
          withPreview(`/overlays/${encodeURIComponent(overlayId)}`, mode),
          { surface: 'localhost-overlay', overlayId, previewMode: mode }));
        for (const alias of localhostAliasesForOverlay(overlayId)) {
          routes.push(overlayRoute(
            `localhost-overlays/${overlayId}-alias-${aliasSlug(alias)}-${mode}.png`,
            withPreview(alias, mode),
            { surface: 'localhost-overlay', overlayId, previewMode: mode, routeAlias: alias }));
        }
      }
    }
  }

  return routes;
}

function withPreview(urlPath, mode) {
  return `${urlPath}${urlPath.includes('?') ? '&' : '?'}preview=${encodeURIComponent(mode)}`;
}

function regionsForOverlay(overlayId) {
  if (overlayId === 'garage-cover') {
    return ['general', 'preview'];
  }
  if (overlayId === 'stream-chat') {
    return ['general', 'content', 'twitch', 'streamlabs'];
  }
  return sharedChromeOverlayIds.has(overlayId)
    ? ['general', 'content', 'header', 'footer']
    : ['general', 'content'];
}

function previewModesForOverlay(overlayId) {
  return overlayId === 'gap-to-leader' ? ['race'] : previewModes;
}

function localhostAliasesForOverlay(overlayId) {
  return overlayPagesById.get(overlayId)?.aliases || [];
}

function aliasSlug(alias) {
  const slug = String(alias || '')
    .split('/')
    .filter(Boolean)
    .pop();
  return String(slug || 'alias')
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '') || 'alias';
}

function variantMinBytes(variant) {
  const key = `${variant.overlayId}/${variant.slug}`;
  if ([
    'input-state/no-content',
    'gap-to-leader/no-cars'
  ].includes(key)) {
    return 100;
  }

  if ([
    'input-state/waiting',
    'car-radar/clear',
    'track-map/no-markers'
  ].includes(key)) {
    return 500;
  }

  return 1_000;
}

function settingsRoute(relativePath, urlPath, metadata = {}) {
  return {
    relativePath,
    urlPath,
    selector: '#settings-app',
    viewport: { width: 1280, height: 760 },
    minBytes: 10_000,
    surface: 'browser-review-settings',
    renderer: 'settings-general.html',
    sourceContract: 'src/TmrOverlay.App/Overlays/BrowserSources/Assets/templates/settings-general.html',
    ...metadata
  };
}

function settingsComponentRoutes() {
  const route = (fileName, urlPath, clip, metadata = {}) =>
    settingsComponentRoute(`components/settings/${fileName}.png`, urlPath, clip, metadata);

  return [
    route('sidebar-tabs', '/review/app', { x: 64, y: 116, width: 190, height: 506 }, { tab: 'general', region: 'general' }),
    route('region-tabs', '/review/app?tab=relative', { x: 306, y: 202, width: 420, height: 52 }, { tab: 'relative', overlayId: 'relative', region: 'general' }),
    route('unit-choice', '/review/app', { x: 306, y: 272, width: 392, height: 132 }, { tab: 'general', region: 'general' }),
    route('overlay-controls', '/review/app?tab=relative', { x: 306, y: 272, width: 392, height: 226 }, { tab: 'relative', overlayId: 'relative', region: 'general' }),
    route('content-matrix', '/review/app?tab=relative&region=content', { x: 306, y: 272, width: 690, height: 222 }, { tab: 'relative', overlayId: 'relative', region: 'content' }),
    route('chat-inputs', '/review/app?tab=stream-chat&region=content', { x: 306, y: 272, width: 650, height: 204 }, { tab: 'stream-chat', overlayId: 'stream-chat', region: 'content' }),
    route('support-buttons', '/review/app?tab=support', { x: 306, y: 410, width: 716, height: 202 }, { tab: 'support', region: 'general' }),
    route('browser-source', '/review/app?tab=relative', { x: 726, y: 272, width: 296, height: 132 }, { tab: 'relative', overlayId: 'relative', region: 'general' })
  ];
}

function settingsComponentRoute(relativePath, urlPath, clip, metadata = {}) {
  return settingsRoute(relativePath, urlPath, {
    ...metadata,
    clip,
    minBytes: 1_000,
    captureMode: 'settings-component-crop',
    surface: 'browser-review-settings-component',
    comparisonMode: 'browser-review-settings-component-vs-windows-settings-component',
    comparisonLimit: 'same-design-coordinate-crop'
  });
}

function overlayRoute(relativePath, urlPath, metadata = {}) {
  const configuredCanvasSize = metadata.overlayId ? configuredCanvasOverlaySizes.get(metadata.overlayId) : null;
  return {
    relativePath,
    urlPath,
    selector: '.overlay',
    viewport: configuredCanvasSize || { width: 1440, height: 900 },
    minBytes: 1_000,
    renderer: 'browser-overlay-assets',
    moduleAsset: metadata.overlayId ? `src/TmrOverlay.App/Overlays/BrowserSources/Assets/modules/${metadata.overlayId}.js` : null,
    sourceContract: 'src/TmrOverlay.App/Overlays/BrowserSources/BrowserOverlayModelFactory.cs',
    captureMode: configuredCanvasSize ? 'configured-browser-source-canvas' : 'cropped-overlay-element',
    configuredOverlaySize: configuredCanvasSize || null,
    comparisonMode: configuredCanvasSize ? 'browser-localhost-configured-canvas-vs-native-overlay-window' : null,
    comparisonLimit: null,
    compositingMode: configuredCanvasSize ? 'solid-review-backdrop' : null,
    captureBackdrop: configuredCanvasSize ? configuredCanvasCaptureBackdrop : null,
    ...metadata
  };
}

function installerReviewRoute(relativePath, urlPath, metadata = {}) {
  return {
    relativePath,
    urlPath,
    selector: '.installer-window',
    viewport: { width: 900, height: 620 },
    minBytes: 1_000,
    exactClip: true,
    surface: 'browser-review-installer',
    renderer: 'installer-review.html',
    sourceContract: 'src/TmrOverlay.App/Overlays/BrowserSources/Assets/templates/installer-review.html',
    moduleAsset: 'src/TmrOverlay.App/Overlays/BrowserSources/Assets/styles/installer-review.css',
    ...metadata
  };
}

async function captureRoute(page, route, manifest) {
  await page.setViewportSize(route.viewport);
  const url = `${baseUrl}${route.urlPath}`;
  await page.goto(url, { waitUntil: 'domcontentloaded' });
  const element = page.locator(route.selector).first();
  await element.waitFor({ state: 'visible', timeout: 5_000 });
  await page.waitForTimeout(settleMilliseconds);
  const model = await readOverlayModel(route);
  const dom = await readDomDiagnostics(element);

  const screenshotPath = join(outputRoot, route.relativePath);
  mkdirSync(dirname(screenshotPath), { recursive: true });
  const exactClip = route.exactClip === true
    ? await exactElementClip(element, dom.layout?.root)
    : route.clip
      ? await elementRelativeClip(element, route.clip)
    : null;
  const removeCaptureBackdrop = await injectCaptureBackdrop(page, route);
  try {
    if (exactClip) {
      await page.screenshot({
        path: screenshotPath,
        animations: 'disabled',
        clip: exactClip
      });
    } else {
      await element.screenshot({
        path: screenshotPath,
        animations: 'disabled'
      });
    }
  } finally {
    if (removeCaptureBackdrop) {
      await removeCaptureBackdrop();
    }
  }

  const artifact = inspectPng(screenshotPath, route.minBytes);
  manifest.push({
    path: route.relativePath,
    url: route.urlPath,
    selector: route.selector,
    surface: route.surface,
    renderer: route.renderer,
    sourceContract: route.sourceContract,
    moduleAsset: route.moduleAsset || null,
    captureMode: route.captureMode || null,
    cropBounds: route.clip || null,
    configuredOverlaySize: route.configuredOverlaySize || null,
    comparisonMode: route.comparisonMode || null,
    comparisonLimit: route.comparisonLimit || null,
    compositingMode: route.compositingMode || null,
    captureBackdrop: route.captureBackdrop || null,
    overlayId: route.overlayId || null,
    tab: route.tab || null,
    region: route.region || null,
    activeRegion: dom.activeRegion,
    routeAlias: route.routeAlias || null,
    fixtureVariant: route.fixtureVariant || null,
    previewMode: route.previewMode || null,
    unitSystem: route.unitSystem || screenshotUnitSystem,
    menuId: route.menuId || null,
    status: stringOrNull(model?.status),
    source: stringOrNull(model?.source),
    bodyKind: stringOrNull(model?.bodyKind),
    shouldRender: booleanOrNull(model?.shouldRender),
    rowCount: modelRowCount(model, dom.layout),
    metricCount: arrayLength(model?.metrics) + arrayLength(model?.metricSections) + arrayLength(model?.gridSections),
    flagCount: arrayLength(model?.flags?.flags),
    radarShouldRender: booleanOrNull(model?.carRadar?.renderModel?.shouldRender),
    trackMapMarkerCount: arrayLength(model?.trackMap?.markers),
    textSample: dom.textSample,
    contentBounds: dom.contentBounds,
    layout: dom.layout,
    uiEvidence: uiEvidence(route, dom),
    modelEvidence: modelLayoutEvidence(model, dom.layout),
    scenarioEvidence: scenarioEvidence(route, model, dom.layout),
    width: artifact.width,
    height: artifact.height,
    bytes: artifact.bytes
  });
}

async function injectCaptureBackdrop(page, route) {
  const backdrop = route.captureBackdrop;
  if (!backdrop?.color) {
    return null;
  }

  const selector = String(route.selector || '.overlay');
  const styleHandle = await page.addStyleTag({
    content: `
      html,
      body {
        background: ${backdrop.color} !important;
      }

      ${selector} {
        background-color: ${backdrop.color} !important;
      }
    `
  });
  return async () => {
    try {
      await styleHandle.evaluate((node) => node.remove());
    } finally {
      await styleHandle.dispose();
    }
  };
}

async function exactElementClip(element, root) {
  const box = await element.boundingBox();
  const width = Math.round(Number(root?.width ?? box?.width));
  const height = Math.round(Number(root?.height ?? box?.height));
  if (!box || width <= 0 || height <= 0) {
    return null;
  }

  return {
    x: Math.round(box.x),
    y: Math.round(box.y),
    width,
    height
  };
}

async function elementRelativeClip(element, clip) {
  const box = await element.boundingBox();
  const x = Math.round(Number(clip?.x));
  const y = Math.round(Number(clip?.y));
  const width = Math.round(Number(clip?.width));
  const height = Math.round(Number(clip?.height));
  if (!box || width <= 0 || height <= 0) {
    return null;
  }

  return {
    x: Math.round(box.x + x),
    y: Math.round(box.y + y),
    width,
    height
  };
}

async function readOverlayModel(route) {
  if (!route.overlayId || !route.surface?.endsWith('-overlay')) {
    return null;
  }

  const query = route.urlPath.includes('?') ? route.urlPath.slice(route.urlPath.indexOf('?')) : '';
  const response = await fetch(`${baseUrl}/api/overlay-model/${encodeURIComponent(route.overlayId)}${query}`, {
    headers: { accept: 'application/json' }
  });
  if (!response.ok) {
    throw new Error(`Failed to read ${route.overlayId} model for ${route.relativePath}: HTTP ${response.status}`);
  }

  const payload = await response.json();
  return payload?.model || null;
}

async function readDomDiagnostics(element) {
  return element.evaluate((node) => {
    const round = (value) => Math.round(Number(value || 0) * 1000) / 1000;
    const rectFor = (element, rootRect) => {
      const rect = element.getBoundingClientRect();
      if (rect.width <= 0 || rect.height <= 0) return null;
      return {
        x: round(rect.left - rootRect.left),
        y: round(rect.top - rootRect.top),
        width: round(rect.width),
        height: round(rect.height)
      };
    };
    const textFor = (element, limit = 160) => String(element.innerText || element.textContent || '')
      .replace(/\s+/g, ' ')
      .trim()
      .slice(0, limit) || null;
    const styleFor = (element) => {
      const style = window.getComputedStyle(element);
      return {
        color: style.color || null,
        backgroundColor: style.backgroundColor || null,
        borderColor: style.borderColor || null,
        fill: style.fill && style.fill !== 'none' ? style.fill : null,
        stroke: style.stroke && style.stroke !== 'none' ? style.stroke : null,
        opacity: style.opacity || null,
        fontFamily: style.fontFamily || null,
        fontSize: style.fontSize || null,
        fontWeight: style.fontWeight || null,
        display: style.display || null,
        gridTemplateColumns: style.gridTemplateColumns && style.gridTemplateColumns !== 'none'
          ? style.gridTemplateColumns
          : null,
        gridTemplateRows: style.gridTemplateRows && style.gridTemplateRows !== 'none'
          ? style.gridTemplateRows
          : null
      };
    };
    const controlKindFor = (tag, element) => {
      if (tag === 'button') return 'button';
      if (tag === 'a') return 'tab-link';
      if (tag === 'input') return element.getAttribute('type') || 'input';
      if (tag === 'select') return 'select';
      if (tag === 'textarea') return 'textarea';
      return null;
    };
    const attributeEvidence = (element) => {
      const tag = String(element.tagName || '').toLowerCase();
      const value = 'value' in element ? String(element.value ?? '') : null;
      return {
        role: element.getAttribute('role') || null,
        ariaLabel: element.getAttribute('aria-label') || null,
        ariaSelected: element.getAttribute('aria-selected') || null,
        ariaChecked: element.getAttribute('aria-checked') || null,
        type: element.getAttribute('type') || null,
        href: element.getAttribute('href') || null,
        value: value || null,
        checked: 'checked' in element ? Boolean(element.checked) : null,
        selected: 'selected' in element ? Boolean(element.selected) : null,
        disabled: 'disabled' in element ? Boolean(element.disabled) : null,
        controlKind: controlKindFor(tag, element)
      };
    };
    const roleSelectors = [
      ['settings-app', '#settings-app'],
      ['settings-titlebar', '.titlebar'],
      ['settings-sidebar', '.sidebar'],
      ['settings-sidebar-tab', '.sidebar-tab'],
      ['settings-content', '.content-card'],
      ['settings-content-header', '.content-header'],
      ['settings-content-body', '.content-body'],
      ['settings-region-segment', '.region-segment'],
      ['settings-panel', '.panel, .garage-preview-stage, .cover-preview'],
      ['settings-field-row', '.field-row, .status-row'],
      ['settings-button', 'button'],
      ['settings-segmented', '.segmented'],
      ['settings-choice', '.choice'],
      ['settings-matrix', '.matrix-table, .toggle-grid, .chrome-table'],
      ['settings-matrix-row', '.matrix-item, .grid-toggle-row, .chrome-row-label'],
      ['settings-matrix-cell', '.matrix-session, .matrix-visible, .matrix-head, .chrome-check, .chrome-head'],
      ['installer-window', '.installer-window'],
      ['installer-titlebar', '.installer-titlebar'],
      ['installer-body', '.installer-body'],
      ['installer-splash', '.installer-splash'],
      ['installer-banner', '.installer-banner'],
      ['installer-content', '.installer-content'],
      ['installer-maintenance-option', '.installer-maintenance-option'],
      ['installer-cancel-body', '.installer-cancel-body'],
      ['installer-cancel-footer', '.installer-cancel-footer'],
      ['installer-information-icon', '.installer-information-icon'],
      ['installer-heading', '.installer-heading'],
      ['installer-text', '.installer-text, .installer-note, .installer-field, .installer-option'],
      ['installer-footer', '.installer-footer'],
      ['installer-button', '.installer-button'],
      ['overlay', '.overlay'],
      ['header', '.header'],
      ['title', '.title'],
      ['status', '#status'],
      ['time-remaining', '#time-remaining'],
      ['content', '#content'],
      ['source', '#source'],
      ['table', 'table'],
      ['thead-row', 'thead tr'],
      ['table-header-cell', 'th'],
      ['table-row', 'tbody tr'],
      ['table-cell', 'tbody td'],
      ['class-header-band', '.class-header-band'],
      ['metric-list', '.metric-list'],
      ['metric-row', '.metric'],
      ['metric-label', '.metric .label'],
      ['metric-value', '.metric .value'],
      ['metric-segment', '.value-segment'],
      ['metric-section', '.metric-section'],
      ['metric-section-title', '.metric-section-title'],
      ['tire-grid', '.tire-grid'],
      ['tire-grid-row', '.tire-grid-row'],
      ['tire-grid-cell', '.tire-grid-cell, .tire-grid-label, .tire-grid-header'],
      ['graph-panel', '.model-graph-panel'],
      ['graph-canvas', '.model-graph, canvas'],
      ['flags', '.flags-v2'],
      ['flag-cell', '.flag-cell'],
      ['car-radar', '.radar-v2, .car-radar-v2'],
      ['radar-shape', '.radar-v2 rect, .radar-v2 circle, .radar-v2 path, .radar-v2 text, .car-radar-v2 rect, .car-radar-v2 circle, .car-radar-v2 path, .car-radar-v2 text'],
      ['track-map', '.track-map-v2, .track'],
      ['track-map-shape', '.track-map-v2 path, .track-map-v2 circle, .track-map-v2 text, .track svg path, .track svg circle, .track svg text'],
      ['garage-cover', '.garage-cover'],
      ['garage-cover-image', '.garage-cover img'],
      ['input-shell', '.input-state-v2, .input-layout'],
      ['input-graph', '.input-graph-panel, .input-graph'],
      ['input-rail', '.input-rail'],
      ['input-rail-group', '.input-bars, .input-readouts'],
      ['input-item', '.input-bar, .input-readout, .input-wheel'],
      ['input-bar', '.input-bar'],
      ['input-bar-label', '.input-bar-label'],
      ['input-bar-track', '.input-bar-track'],
      ['input-bar-fill', '.input-bar-track span'],
      ['input-bar-value', '.input-bar-value'],
      ['input-readout', '.input-readout'],
      ['input-readout-label', '.input-readout-label'],
      ['input-readout-value', '.input-readout-value'],
      ['input-wheel', '.input-wheel'],
      ['input-wheel-label', '.input-wheel-label'],
      ['input-wheel-value', '.input-wheel-value'],
      ['input-wheel-svg', '.input-wheel svg'],
      ['input-wheel-shape', '.input-wheel svg circle, .input-wheel svg line'],
      ['stream-chat-body', '.stream-chat-body, .chat'],
      ['chat-line', '.chat-line'],
      ['chat-name', '.chat-name'],
      ['chat-meta', '.chat-meta, .chat-chip'],
      ['chat-badge', '.chat-badge, .chat-badge.image'],
      ['chat-emote', '.chat-emote'],
      ['chat-text', '.chat-text']
    ];
    const text = String(node.innerText || node.textContent || '')
      .replace(/\s+/g, ' ')
      .trim()
      .slice(0, 500);
    const activeRegion = String(node.querySelector('.region-segment.active')?.textContent || '')
      .replace(/\s+/g, ' ')
      .trim();
    const selectors = [
      '#settings-app',
      '.titlebar',
      '.sidebar',
      '.content-card',
      '.content-body',
      '.panel',
      '.region-segments',
      '.overlay-panel',
      '.overlay-content',
      '.content',
      '.installer-window',
      '.installer-content',
      '.installer-maintenance-option',
      '.installer-cancel-body',
      '.installer-cancel-footer',
      '.installer-footer',
      '.metric-list',
      '.table',
      '.model-graph-panel',
      '.flags-v2',
      '.car-radar-v2',
      '.track-map-v2',
      '.garage-cover',
      '.stream-chat-body',
      'canvas',
      'svg'
    ];
    const rootRect = node.getBoundingClientRect();
    const elements = [];
    for (const [role, selector] of roleSelectors) {
      Array.from(node.querySelectorAll(selector)).forEach((element, index) => {
        const bounds = rectFor(element, rootRect);
        if (!bounds) return;
        elements.push({
          role,
          index,
          tag: element.tagName.toLowerCase(),
          id: element.id || null,
          className: String(element.className?.baseVal || element.className || '') || null,
          text: textFor(element),
          bounds,
          styles: styleFor(element),
          attributes: attributeEvidence(element)
        });
      });
    }
    const rects = selectors
      .flatMap((selector) => Array.from(node.querySelectorAll(selector)))
      .map((element) => element.getBoundingClientRect())
      .filter((rect) => rect.width > 0 && rect.height > 0);
    if (!rects.length) {
      return {
        textSample: text || null,
        activeRegion: activeRegion || null,
        contentBounds: null,
        layout: {
          contract: 'browser-layout/v1',
          root: { x: 0, y: 0, width: round(rootRect.width), height: round(rootRect.height) },
          elements
        }
      };
    }

    const left = Math.min(...rects.map((rect) => rect.left));
    const top = Math.min(...rects.map((rect) => rect.top));
    const right = Math.max(...rects.map((rect) => rect.right));
    const bottom = Math.max(...rects.map((rect) => rect.bottom));
    const width = right - left;
    const height = bottom - top;
    return {
      textSample: text || null,
      activeRegion: activeRegion || null,
      contentBounds: {
        x: Math.round(left - rootRect.left),
        y: Math.round(top - rootRect.top),
        width: Math.round(width),
        height: Math.round(height),
        aspectRatio: height > 0 ? Number((width / height).toFixed(4)) : null
      },
      layout: {
        contract: 'browser-layout/v1',
        root: { x: 0, y: 0, width: round(rootRect.width), height: round(rootRect.height) },
        contentBounds: {
          x: round(left - rootRect.left),
          y: round(top - rootRect.top),
          width: round(width),
          height: round(height)
        },
        elements
      }
    };
  });
}

function uiEvidence(route, dom) {
  if (route.surface?.includes('installer')) {
    return installerReviewUiEvidence(route, dom);
  }

  if (!route.surface?.includes('settings')) {
    return null;
  }

  const elements = Array.isArray(dom?.layout?.elements) ? dom.layout.elements : [];
  return {
    contract: 'settings-ui-evidence/v1',
    surface: route.surface,
    tab: route.tab || null,
    overlayId: route.overlayId || null,
    requestedRegion: route.region || null,
    activeRegion: dom.activeRegion || null,
    previewMode: route.previewMode || null,
    unitSystem: route.unitSystem || screenshotUnitSystem,
    root: dom.layout?.root || null,
    contentBounds: dom.contentBounds || null,
    sidebar: firstElement(elements, 'settings-sidebar'),
    content: firstElement(elements, 'settings-content'),
    contentBody: firstElement(elements, 'settings-content-body'),
    tabs: elements
      .filter((element) => element.role === 'settings-sidebar-tab')
      .map(settingElementEvidence),
    regions: elements
      .filter((element) => element.role === 'settings-region-segment')
      .map(settingElementEvidence),
    panels: elements
      .filter((element) => element.role === 'settings-panel')
      .map(settingElementEvidence),
    controls: elements
      .filter((element) => [
        'settings-field-row',
        'settings-button',
        'settings-segmented',
        'settings-choice',
        'settings-matrix',
        'settings-matrix-row',
        'settings-matrix-cell'
      ].includes(element.role))
      .map(settingElementEvidence),
    preview: settingsPreviewEvidence(elements)
  };
}

function installerReviewUiEvidence(route, dom) {
  const elements = Array.isArray(dom?.layout?.elements) ? dom.layout.elements : [];
  return {
    contract: 'browser-installer-ui-evidence/v1',
    surface: route.surface,
    windowTitle: 'Tech Mates Racing Overlay Setup',
    menuId: route.menuId || null,
    root: dom.layout?.root || null,
    contentBounds: dom.contentBounds || null,
    titlebar: firstElement(elements, 'installer-titlebar'),
    content: firstElement(elements, 'installer-content') || firstElement(elements, 'installer-cancel-body'),
    footer: firstElement(elements, 'installer-footer') || firstElement(elements, 'installer-cancel-footer'),
    modal: null,
    controls: elements
      .filter((element) => [
        'installer-window',
        'installer-titlebar',
        'installer-body',
        'installer-splash',
        'installer-banner',
        'installer-content',
        'installer-maintenance-option',
        'installer-cancel-body',
        'installer-cancel-footer',
        'installer-information-icon',
        'installer-footer',
      ].includes(element.role))
      .map(settingElementEvidence),
    buttons: elements
      .filter((element) => element.role === 'installer-button')
      .map(settingElementEvidence),
    textBlocks: elements
      .filter((element) => [
        'installer-heading',
        'installer-text'
      ].includes(element.role))
      .map(settingElementEvidence),
    palette: [
      { color: '#ffffff', samples: 1 },
      { color: '#f4f4f4', samples: 1 },
      { color: '#0078d4', samples: 1 },
      { color: '#1f2328', samples: 1 }
    ],
    sourceAssets: [
      sourceFileEvidence('assets/brand/TMRMsiBanner.bmp'),
      sourceFileEvidence('assets/brand/TMRMsiLogo.bmp'),
      sourceFileEvidence('assets/brand/TMRLogo.png')
    ]
  };
}

function firstElement(elements, role) {
  const element = elements.find((item) => item?.role === role);
  return element ? settingElementEvidence(element) : null;
}

function settingElementEvidence(element) {
  return {
    role: element.role || null,
    id: element.id || null,
    className: element.className || null,
    text: element.text || null,
    bounds: element.bounds || null,
    styles: element.styles || null,
    attributes: element.attributes || null
  };
}

function settingsPreviewEvidence(elements) {
  const previewRoles = new Set([
    'overlay',
    'header',
    'content',
    'source',
    'table',
    'metric-list',
    'graph-panel',
    'flags',
    'car-radar',
    'track-map',
    'input-shell',
    'stream-chat-body'
  ]);
  const previewElements = elements
    .filter((element) => previewRoles.has(element.role))
    .map(settingElementEvidence);
  if (!previewElements.length) {
    return null;
  }

  return {
    elementCount: previewElements.length,
    elements: previewElements
  };
}

function modelLayoutEvidence(model, layout) {
  if (!model || typeof model !== 'object') {
    return null;
  }

  const tableRendering = tableRenderedEvidence(layout);
  const metricRendering = metricRenderedEvidence(layout);
  let gridRowCursor = 0;
  return {
    contract: 'overlay-model-layout-evidence/v1',
    bodyKind: stringOrNull(model.bodyKind),
    unitSystem: screenshotUnitSystem,
    columns: (Array.isArray(model.columns) ? model.columns : []).map((column, index) => {
      const rendered = tableRendering.headers[index] || null;
      return {
        index,
        label: stringOrNull(column?.label),
        dataKey: stringOrNull(column?.dataKey),
        configuredWidth: Number.isFinite(Number(column?.width)) ? Number(column.width) : null,
        renderedWidth: numberOrNull(rendered?.bounds?.width),
        alignment: stringOrNull(column?.alignment),
        bounds: rendered?.bounds || null,
        foreground: rendered?.styles?.color || null,
        background: rendered?.styles?.backgroundColor || null
      };
    }),
    rows: (Array.isArray(model.rows) ? model.rows : []).slice(0, 80).map((row, index) =>
      tableRowEvidence(row, index, tableRendering)),
    metrics: (Array.isArray(model.metrics) ? model.metrics : []).map((row, index) =>
      metricEvidence(row, renderedMetricRowFor(metricRendering, row, index), metricRendering)),
    metricSections: (Array.isArray(model.metricSections) ? model.metricSections : []).map((section) => ({
      title: stringOrNull(section?.title),
      bounds: renderedMetricSectionBounds(metricRendering, section?.title),
      rows: (Array.isArray(section?.rows) ? section.rows : []).map((row) =>
        metricEvidence(row, renderedMetricRowFor(metricRendering, row), metricRendering))
    })),
    gridSections: (Array.isArray(model.gridSections) ? model.gridSections : []).map((section) => ({
      title: stringOrNull(section?.title),
      bounds: renderedMetricSectionBounds(metricRendering, section?.title),
      headers: Array.isArray(section?.headers) ? section.headers.map((header) => String(header ?? '')) : [],
      renderedHeaders: metricRendering.tireCells
        .filter((cell) => String(cell.className || '').includes('tire-grid-header'))
        .map((cell, index) => renderedCellEvidence(cell, index, null)),
      rows: (Array.isArray(section?.rows) ? section.rows : []).map((row, index) => {
        const renderedRow = metricRendering.tireRows[gridRowCursor++] || null;
        const renderedCells = renderedGridCellsForRow(metricRendering, renderedRow?.bounds);
        return {
          index,
          label: stringOrNull(row?.label),
          tone: stringOrNull(row?.tone),
          bounds: renderedRow?.bounds || null,
          cells: (Array.isArray(row?.cells) ? row.cells : []).map((cell, cellIndex) => ({
            value: stringOrNull(cell?.value),
            tone: stringOrNull(cell?.tone),
            bounds: renderedGridCellBounds(renderedCells, cellIndex, cell?.value)
          }))
        };
      })
    })),
    graph: graphModelEvidence(model, layout),
    inputs: model.inputs ? inputEvidence(model.inputs, layout) : null,
    flags: model.flags ? {
      count: arrayLength(model.flags?.flags),
      kinds: (Array.isArray(model.flags?.flags) ? model.flags.flags : []).map((flag) => stringOrNull(flag?.kind)),
      gridColumns: flagGrid(arrayLength(model.flags?.flags)).columns,
      gridRows: flagGrid(arrayLength(model.flags?.flags)).rows,
      grid: {
        columns: flagGrid(arrayLength(model.flags?.flags)).columns,
        rows: flagGrid(arrayLength(model.flags?.flags)).rows
      },
      cells: flagCellEvidence(model.flags, layout)
    } : null,
    carRadar: carRadarVectorEvidence(model.carRadar?.renderModel, layout),
    trackMap: trackMapVectorEvidence(model.trackMap?.renderModel || model.trackMap, layout),
    garageCover: model.garageCover ? garageCoverEvidence(model.garageCover, layout) : null,
    streamChat: model.streamChat ? streamChatEvidence(model.streamChat, layout) : null
  };
}

function tableRenderedEvidence(layout) {
  const elements = Array.isArray(layout?.elements) ? layout.elements : [];
  const headers = elements.filter((element) => element.role === 'table-header-cell');
  const rows = elements.filter((element) => element.role === 'table-row');
  const cells = elements.filter((element) => element.role === 'table-cell');
  const renderedRows = rows.map((row, index) => {
    const rowBounds = row.bounds;
    const rowCells = cells
      .filter((cell) => overlapsVertically(cell.bounds, rowBounds))
      .sort((a, b) => numberOr(a.bounds?.x, 0) - numberOr(b.bounds?.x, 0));
    return {
      index,
      row,
      cells: rowCells
    };
  });
  return { headers, rows: renderedRows };
}

function tableRowEvidence(row, index, tableRendering) {
  const rendered = tableRendering.rows[index] || null;
  return {
    index,
    kind: row?.rowKind || (row?.isClassHeader ? 'class-header' : 'row'),
    isReference: Boolean(row?.isReference || row?.isFocus || row?.isReferenceCar),
    isPartial: Boolean(row?.isPartial),
    classColorHex: stringOrNull(row?.carClassColorHex),
    relativeLapDelta: numberOrNull(row?.relativeLapDelta),
    text: rendered?.row?.text || null,
    foreground: rendered?.row?.styles?.color || null,
    background: rendered?.row?.styles?.backgroundColor || null,
    classList: classListForElement(rendered?.row),
    bounds: rendered?.row?.bounds || null,
    cells: Array.isArray(row?.cells) ? row.cells.map((cell) => String(cell ?? '')) : [],
    renderedCells: (rendered?.cells || []).map((cell, cellIndex) =>
      renderedCellEvidence(cell, cellIndex, tableRendering.headers[cellIndex]?.text || null))
  };
}

function renderedCellEvidence(cell, index, column) {
  return {
    columnIndex: index,
    column: stringOrNull(column),
    text: cell?.text || null,
    value: cell?.text || null,
    foreground: cell?.styles?.color || null,
    background: cell?.styles?.backgroundColor || null,
    bounds: cell?.bounds || null
  };
}

function classListForElement(element) {
  return String(element?.className || '')
    .split(/\s+/)
    .map((item) => item.trim())
    .filter(Boolean);
}

function garageCoverEvidence(garageCover, layout) {
  const cover = findElementBounds(layout, 'garage-cover', 'garage-cover')
    || findElementBounds(layout, 'content')
    || null;
  const image = findElementBounds(layout, 'garage-cover-image') || null;
  return {
    shouldCover: booleanOrNull(garageCover?.shouldCover),
    settings: garageCover?.browserSettings || null,
    detection: garageCover?.detection || null,
    detectionState: stringOrNull(garageCover?.detection?.state),
    detectionIsFresh: booleanOrNull(garageCover?.detection?.isFresh),
    detectionText: stringOrNull(garageCover?.detection?.displayText),
    bounds: cover,
    imageBounds: image
  };
}

function streamChatEvidence(streamChat, layout) {
  const renderedRows = elementsForRole(layout, 'chat-line').sort(compareBounds);
  const renderedNames = elementsForRole(layout, 'chat-name').sort(compareBounds);
  const renderedTexts = elementsForRole(layout, 'chat-text').sort(compareBounds);
  const renderedBadges = elementsForRole(layout, 'chat-badge').sort(compareBounds);
  const renderedMetadata = elementsForRole(layout, 'chat-meta').sort(compareBounds);
  const renderedEmotes = elementsForRole(layout, 'chat-emote').sort(compareBounds);
  const rows = Array.isArray(streamChat?.rows) ? streamChat.rows : [];
  return {
    settings: streamChat?.settings || null,
    rowCount: rows.length,
    renderedRowCount: renderedRows.length,
    firstRenderedText: renderedRows[0]?.text || null,
    lastRenderedText: renderedRows[renderedRows.length - 1]?.text || null,
    badgeCount: renderedBadges.length,
    metadataCount: renderedMetadata.length,
    emoteCount: renderedEmotes.length,
    rows: rows.slice(-36).map((row, index) => ({
      index,
      name: stringOrNull(row?.name),
      text: stringOrNull(row?.text),
      kind: stringOrNull(row?.kind),
      authorColorHex: stringOrNull(row?.authorColorHex),
      metadata: Array.isArray(row?.metadata) ? row.metadata.map((item) => String(item ?? '')) : [],
      badges: Array.isArray(row?.badges) ? row.badges.map((badge) => ({
        id: stringOrNull(badge?.id),
        version: stringOrNull(badge?.version),
        label: stringOrNull(badge?.label),
        roomId: stringOrNull(badge?.roomId)
      })) : [],
      segments: Array.isArray(row?.segments) ? row.segments.map((segment) => ({
        kind: stringOrNull(segment?.kind),
        text: stringOrNull(segment?.text),
        imageUrl: stringOrNull(segment?.imageUrl)
      })) : [],
      bounds: renderedRows[index]?.bounds || null,
      nameBounds: renderedNames[index]?.bounds || null,
      textBounds: renderedTexts[index]?.bounds || null
    }))
  };
}

function metricRenderedEvidence(layout) {
  const elements = Array.isArray(layout?.elements) ? layout.elements : [];
  return {
    metricRows: elements.filter((element) => element.role === 'metric-row'),
    metricLabels: elements.filter((element) => element.role === 'metric-label'),
    metricValues: elements.filter((element) => element.role === 'metric-value'),
    metricSegments: elements.filter((element) => element.role === 'metric-segment'),
    sections: elements.filter((element) => element.role === 'metric-section-title'),
    grids: elements.filter((element) => element.role === 'tire-grid'),
    tireRows: elements.filter((element) => element.role === 'tire-grid-row'),
    tireCells: elements.filter((element) => element.role === 'tire-grid-cell')
  };
}

function renderedMetricRowFor(metricRendering, row, fallbackIndex = null) {
  const label = stringOrNull(row?.label);
  if (label) {
    const normalized = normalizeEvidenceText(label);
    const labelElement = metricRendering.metricLabels.find((candidate) =>
      normalizeEvidenceText(candidate.text) === normalized);
    if (labelElement) {
      const byLabelBounds = metricRendering.metricRows.find((candidate) =>
        overlapsVertically(candidate.bounds, labelElement.bounds));
      if (byLabelBounds) {
        return byLabelBounds;
      }
    }

    const byText = metricRendering.metricRows.find((candidate) =>
      normalizeEvidenceText(candidate.text).startsWith(normalized));
    if (byText) {
      return byText;
    }
  }

  return fallbackIndex === null ? null : metricRendering.metricRows[fallbackIndex] || null;
}

function renderedMetricSectionBounds(metricRendering, title) {
  const normalized = normalizeEvidenceText(title);
  if (!normalized) {
    return null;
  }

  return metricRendering.sections.find((candidate) =>
    normalizeEvidenceText(candidate.text) === normalized)?.bounds || null;
}

function metricEvidence(row, rendered = null, metricRendering = null) {
  const segmentElements = rendered
    ? metricRenderedEvidenceForBounds(rendered.bounds, metricRendering)
    : [];
  return {
    label: stringOrNull(row?.label),
    value: stringOrNull(row?.value),
    tone: stringOrNull(row?.tone),
    rowColorHex: stringOrNull(row?.rowColorHex || row?.carClassColorHex),
    foreground: rendered?.styles?.color || null,
    background: rendered?.styles?.backgroundColor || null,
    bounds: rendered?.bounds || null,
    segments: (Array.isArray(row?.segments) ? row.segments : []).map((segment, index) => ({
      index,
      label: stringOrNull(segment?.label),
      value: stringOrNull(segment?.value),
      tone: stringOrNull(segment?.tone),
      accentHex: stringOrNull(segment?.accentHex),
      rotationDegrees: numberOrNull(segment?.rotationDegrees),
      bounds: segmentElements[index]?.bounds || null,
      foreground: segmentElements[index]?.styles?.color || null,
      background: segmentElements[index]?.styles?.backgroundColor || null
    }))
  };
}

function metricRenderedEvidenceForBounds(bounds, metricRendering) {
  if (!bounds || !metricRendering) {
    return [];
  }
  return metricRendering.metricSegments
    .filter((segment) => overlapsVertically(segment.bounds, bounds))
    .sort((left, right) => numberOr(left.bounds?.x, 0) - numberOr(right.bounds?.x, 0));
}

function renderedGridCellsForRow(metricRendering, rowBounds) {
  if (!rowBounds) {
    return [];
  }

  return metricRendering.tireCells
    .filter((cell) =>
      !String(cell.className || '').includes('tire-grid-label')
      && !String(cell.className || '').includes('tire-grid-header')
      && overlapsVertically(cell.bounds, rowBounds))
    .sort((left, right) => numberOr(left.bounds?.x, 0) - numberOr(right.bounds?.x, 0));
}

function renderedGridCellBounds(renderedCells, cellIndex, value) {
  const direct = renderedCells[cellIndex] || null;
  if (direct?.bounds) {
    return direct.bounds;
  }

  const text = stringOrNull(value);
  if (!text) {
    return null;
  }

  return renderedCells.find((cell) => cell.text === text)?.bounds || null;
}

function flagCellEvidence(flags, layout) {
  const renderedFlags = Array.isArray(flags?.flags) ? flags.flags : [];
  const grid = flagGrid(renderedFlags.length);
  const svgBounds = findElementBounds(layout, 'flags', 'flags-v2') || findElementBounds(layout, 'content');
  const cells = svgBounds ? computedFlagCells(svgBounds, grid, renderedFlags.length) : elementsForRole(layout, 'flag-cell').map((element) => element.bounds);
  return renderedFlags.map((flag, index) => {
    const cellBounds = cells[index] || null;
    return {
      index,
      row: Math.floor(index / Math.max(1, grid.columns)),
      column: index % Math.max(1, grid.columns),
      kind: stringOrNull(flag?.kind),
      label: stringOrNull(flag?.label),
      detail: stringOrNull(flag?.detail),
      fill: flagColor(flag?.kind),
      bounds: cellBounds,
      clothBounds: cellBounds ? flagClothBounds(cellBounds) : null
    };
  });
}

function flagGrid(count) {
  if (count <= 1) return { columns: 1, rows: 1, compact: false };
  if (count <= 2) return { columns: 2, rows: 1, compact: false };
  if (count <= 4) return { columns: 2, rows: 2, compact: true };
  if (count <= 6) return { columns: 3, rows: 2, compact: true };
  return { columns: 4, rows: Math.ceil(count / 4), compact: true };
}

function computedFlagCells(svgBounds, grid, count) {
  const padding = 8;
  const gap = 8;
  const bounds = {
    x: svgBounds.x + padding,
    y: svgBounds.y + padding,
    width: Math.max(1, svgBounds.width - padding * 2),
    height: Math.max(1, svgBounds.height - padding * 2)
  };
  const cellWidth = (bounds.width - (grid.columns - 1) * gap) / Math.max(1, grid.columns);
  const cellHeight = (bounds.height - (grid.rows - 1) * gap) / Math.max(1, grid.rows);
  return Array.from({ length: count }, (_unused, index) => {
    const row = Math.floor(index / Math.max(1, grid.columns));
    const column = index % Math.max(1, grid.columns);
    return rectEvidence({
      x: bounds.x + column * (cellWidth + gap),
      y: bounds.y + row * (cellHeight + gap),
      width: cellWidth,
      height: cellHeight
    });
  });
}

function flagClothBounds(cell) {
  const poleX = cell.x + Math.max(12, cell.width * 0.16);
  const clothLeft = poleX + 1;
  const clothWidth = Math.max(48, cell.x + cell.width - clothLeft - 8);
  const clothHeight = Math.max(24, Math.min(cell.height * 0.7, clothWidth * 0.58));
  const clothTop = cell.y + Math.max(4, (cell.height - clothHeight) * 0.32);
  return rectEvidence({
    x: clothLeft,
    y: clothTop,
    width: clothWidth,
    height: clothHeight
  });
}

function flagColor(kind) {
  const token = String(kind || '').toLowerCase();
  if (token === 'green') return 'rgb(48, 214, 109)';
  if (token === 'blue') return 'rgb(55, 162, 255)';
  if (token === 'yellow' || token === 'caution') return 'rgb(255, 207, 74)';
  if (token === 'red') return 'rgb(236, 76, 86)';
  if (token === 'white') return 'rgb(246, 248, 250)';
  if (token === 'checkered') return 'checkered';
  if (token === 'black' || token === 'meatball') return 'rgb(8, 10, 12)';
  return null;
}

function carRadarVectorEvidence(renderModel, layout) {
  if (!renderModel) {
    return null;
  }

  const sourceWidth = numberOr(renderModel.width, 300);
  const sourceHeight = numberOr(renderModel.height, 300);
  const target = findElementBounds(layout, 'car-radar', 'car-radar-v2')
    || findElementBounds(layout, 'car-radar', 'radar-v2')
    || findElementBounds(layout, 'content');
  const scale = vectorScale(target, sourceWidth, sourceHeight);
  const primitives = [
    vectorShapePrimitive('background', 'ellipse', renderModel.background, scale),
    ...(Array.isArray(renderModel.rings) ? renderModel.rings.map((ring, index) =>
      vectorShapePrimitive(`ring-${index + 1}`, 'ring', ring, scale)) : []),
    renderModel.multiclassArc
      ? vectorShapePrimitive('multiclass-arc', 'arc', renderModel.multiclassArc, scale)
      : null
  ].filter(Boolean);
  const items = (Array.isArray(renderModel.cars) ? renderModel.cars : []).map((car, index) => ({
    kind: stringOrNull(car?.kind) || 'car',
    id: stringOrNull(car?.id) || index,
    bounds: scaleRect(scale, car),
    fill: colorToCss(car?.fill),
    stroke: colorToCss(car?.stroke),
    strokeWidth: numberOrNull(car?.strokeWidth),
    label: stringOrNull(car?.label)
  }));
  const labels = (Array.isArray(renderModel.labels) ? renderModel.labels : []).map((label) => ({
    text: stringOrNull(label?.text),
    bounds: scaleRect(scale, label),
    fontSize: numberOrNull(label?.fontSize),
    bold: Boolean(label?.bold),
    alignment: stringOrNull(label?.alignment),
    color: colorToCss(label?.color)
  }));

  return {
    shouldRender: booleanOrNull(renderModel.shouldRender),
    width: numberOrNull(renderModel.width),
    height: numberOrNull(renderModel.height),
    targetBounds: target || null,
    scaleX: scale?.scaleX || null,
    scaleY: scale?.scaleY || null,
    carCount: arrayLength(renderModel.cars),
    itemCount: items.length,
    primitiveCount: primitives.length,
    labelCount: labels.length,
    ringCount: arrayLength(renderModel.rings),
    surfaceAlpha: renderModel.shouldRender === true ? 1 : numberOrNull(renderModel.minimumVisibleAlpha),
    items,
    primitives,
    labels
  };
}

function trackMapVectorEvidence(renderModel, layout) {
  if (!renderModel) {
    return null;
  }

  const sourceWidth = numberOr(renderModel.width, 360);
  const sourceHeight = numberOr(renderModel.height, 360);
  const target = findElementBounds(layout, 'track-map', 'track-map-v2')
    || findElementBounds(layout, 'track-map', 'track')
    || findElementBounds(layout, 'content');
  const scale = vectorScale(target, sourceWidth, sourceHeight);
  const primitives = (Array.isArray(renderModel.primitives) ? renderModel.primitives : [])
    .map((primitive, index) => vectorPrimitiveEvidence(primitive, index, scale));
  const items = (Array.isArray(renderModel.markers) ? renderModel.markers : [])
    .map((marker, index) => trackMapMarkerEvidence(marker, index, scale));
  const labels = (Array.isArray(renderModel.markers) ? renderModel.markers : [])
    .filter((marker) => stringOrNull(marker?.label))
    .map((marker) => trackMapMarkerLabelEvidence(marker, scale));

  return {
    markerCount: arrayLength(renderModel.markers),
    itemCount: items.length,
    primitiveCount: primitives.length,
    labelCount: labels.length,
    width: numberOrNull(renderModel.width),
    height: numberOrNull(renderModel.height),
    mapKind: stringOrNull(renderModel.mapKind),
    isAvailable: booleanOrNull(renderModel.isAvailable),
    targetBounds: target || null,
    scaleX: scale?.scaleX || null,
    scaleY: scale?.scaleY || null,
    shouldRender: renderModel.isAvailable === false ? false : true,
    items,
    primitives,
    labels
  };
}

function vectorShapePrimitive(id, kind, shape, scale) {
  if (!shape) {
    return null;
  }

  return {
    kind,
    id,
    bounds: scaleRect(scale, shape),
    points: [],
    closed: false,
    startDegrees: numberOrNull(shape?.startDegrees),
    sweepDegrees: numberOrNull(shape?.sweepDegrees),
    fill: colorToCss(shape?.fill),
    stroke: colorToCss(shape?.stroke),
    strokeWidth: numberOrNull(shape?.strokeWidth)
  };
}

function vectorPrimitiveEvidence(primitive, index, scale) {
  const points = (Array.isArray(primitive?.points) ? primitive.points : [])
    .map((point) => scalePoint(scale, point))
    .filter(Boolean);
  const rect = primitive?.rect || primitive?.bounds || null;
  return {
    kind: stringOrNull(primitive?.kind) || 'path',
    id: index,
    bounds: rect ? scaleRect(scale, rect) : boundsForPoints(points),
    points,
    closed: Boolean(primitive?.closed),
    startDegrees: numberOrNull(primitive?.startDegrees),
    sweepDegrees: numberOrNull(primitive?.sweepDegrees),
    fill: colorToCss(primitive?.fill),
    stroke: colorToCss(primitive?.stroke),
    strokeWidth: numberOrNull(primitive?.strokeWidth)
  };
}

function trackMapMarkerEvidence(marker, index, scale) {
  const markerRadius = numberOr(marker?.radius, 0);
  const alertRingRadius = numberOr(marker?.alertRingRadius, 0);
  const radius = Math.max(markerRadius, alertRingRadius);
  const center = scalePoint(scale, { x: marker?.x, y: marker?.y });
  const scaledRadiusX = radius * numberOr(scale?.scaleX, 1);
  const scaledRadiusY = radius * numberOr(scale?.scaleY, 1);
  return {
    kind: marker?.isFocus ? 'focus-marker' : 'car-marker',
    id: Number.isFinite(Number(marker?.carIdx)) ? Number(marker.carIdx) : index,
    bounds: center ? rectEvidence({
      x: center.x - scaledRadiusX,
      y: center.y - scaledRadiusY,
      width: scaledRadiusX * 2,
      height: scaledRadiusY * 2
    }) : null,
    fill: colorToCss(marker?.fill),
    stroke: colorToCss(marker?.stroke),
    strokeWidth: numberOrNull(marker?.strokeWidth),
    label: stringOrNull(marker?.label),
    labelColor: colorToCss(marker?.labelColor),
    alertKind: stringOrNull(marker?.alertKind),
    alertRingBounds: center && alertRingRadius > 0
      ? rectEvidence({
        x: center.x - alertRingRadius * numberOr(scale?.scaleX, 1),
        y: center.y - alertRingRadius * numberOr(scale?.scaleY, 1),
        width: alertRingRadius * 2 * numberOr(scale?.scaleX, 1),
        height: alertRingRadius * 2 * numberOr(scale?.scaleY, 1)
      })
      : null,
    alertRingStroke: colorToCss(marker?.alertRingStroke),
    alertRingStrokeWidth: numberOrNull(marker?.alertRingStrokeWidth)
  };
}

function trackMapMarkerLabelEvidence(marker, scale) {
  const center = scalePoint(scale, { x: marker?.x, y: marker?.y });
  const fontSize = numberOr(marker?.labelFontSize, 7.6) * numberOr(scale?.scaleY, 1);
  const radius = numberOr(marker?.radius, 5.7) * numberOr(scale?.scaleX, 1);
  return {
    text: stringOrNull(marker?.label),
    bounds: center ? rectEvidence({
      x: center.x - radius,
      y: center.y - fontSize,
      width: radius * 2,
      height: fontSize * 1.4
    }) : null,
    fontSize: numberOrNull(marker?.labelFontSize),
    bold: true,
    alignment: 'center',
    color: colorToCss(marker?.labelColor)
  };
}

function vectorScale(target, sourceWidth, sourceHeight) {
  if (!target || !Number.isFinite(sourceWidth) || sourceWidth <= 0 || !Number.isFinite(sourceHeight) || sourceHeight <= 0) {
    return null;
  }

  return {
    target,
    scaleX: target.width / sourceWidth,
    scaleY: target.height / sourceHeight
  };
}

function scaleRect(scale, rect) {
  if (!rect) {
    return null;
  }

  if (!scale) {
    return rectEvidence(rect);
  }

  return rectEvidence({
    x: scale.target.x + numberOr(rect.x, 0) * scale.scaleX,
    y: scale.target.y + numberOr(rect.y, 0) * scale.scaleY,
    width: numberOr(rect.width, 0) * scale.scaleX,
    height: numberOr(rect.height, 0) * scale.scaleY
  });
}

function scalePoint(scale, point) {
  if (!point || !Number.isFinite(Number(point.x)) || !Number.isFinite(Number(point.y))) {
    return null;
  }

  if (!scale) {
    return {
      x: round(point.x),
      y: round(point.y)
    };
  }

  return {
    x: round(scale.target.x + Number(point.x) * scale.scaleX),
    y: round(scale.target.y + Number(point.y) * scale.scaleY)
  };
}

function boundsForPoints(points) {
  const valid = (Array.isArray(points) ? points : [])
    .filter((point) => Number.isFinite(point?.x) && Number.isFinite(point?.y));
  if (!valid.length) {
    return null;
  }

  const left = Math.min(...valid.map((point) => point.x));
  const top = Math.min(...valid.map((point) => point.y));
  const right = Math.max(...valid.map((point) => point.x));
  const bottom = Math.max(...valid.map((point) => point.y));
  return rectEvidence({
    x: left,
    y: top,
    width: Math.max(0.001, right - left),
    height: Math.max(0.001, bottom - top)
  });
}

function rectEvidence(rect) {
  if (!rect) {
    return null;
  }

  return {
    x: round(rect.x),
    y: round(rect.y),
    width: round(rect.width),
    height: round(rect.height)
  };
}

function elementsForRole(layout, role) {
  const elements = Array.isArray(layout?.elements) ? layout.elements : [];
  return elements.filter((element) => element.role === role);
}

function rectIntersects(first, second) {
  if (!first || !second) {
    return false;
  }

  const firstLeft = numberOr(first.x, 0);
  const firstTop = numberOr(first.y, 0);
  const firstRight = firstLeft + numberOr(first.width, 0);
  const firstBottom = firstTop + numberOr(first.height, 0);
  const secondLeft = numberOr(second.x, 0);
  const secondTop = numberOr(second.y, 0);
  const secondRight = secondLeft + numberOr(second.width, 0);
  const secondBottom = secondTop + numberOr(second.height, 0);
  return firstRight > secondLeft
    && firstLeft < secondRight
    && firstBottom > secondTop
    && firstTop < secondBottom;
}

function compareBounds(left, right) {
  const topDelta = numberOr(left?.bounds?.y, 0) - numberOr(right?.bounds?.y, 0);
  if (Math.abs(topDelta) > 0.5) return topDelta;
  return numberOr(left?.bounds?.x, 0) - numberOr(right?.bounds?.x, 0);
}

function overlapsVertically(first, second) {
  if (!first || !second) {
    return false;
  }

  const top = Math.max(numberOr(first.y, 0), numberOr(second.y, 0));
  const bottom = Math.min(
    numberOr(first.y, 0) + numberOr(first.height, 0),
    numberOr(second.y, 0) + numberOr(second.height, 0));
  return bottom - top >= Math.min(numberOr(first.height, 0), numberOr(second.height, 0)) * 0.35;
}

function colorToCss(color) {
  if (!color) {
    return null;
  }

  if (typeof color === 'string') {
    return color || null;
  }

  const red = Math.max(0, Math.min(255, Math.round(numberOr(color.red, 0))));
  const green = Math.max(0, Math.min(255, Math.round(numberOr(color.green, 0))));
  const blue = Math.max(0, Math.min(255, Math.round(numberOr(color.blue, 0))));
  const alpha = Math.max(0, Math.min(1, numberOr(color.alpha, 255) / 255));
  return `rgba(${red}, ${green}, ${blue}, ${Number(alpha.toFixed(3))})`;
}

function normalizeEvidenceText(value) {
  return String(value || '')
    .replace(/\s+/g, ' ')
    .trim()
    .toLowerCase();
}

function scenarioEvidence(route, model, layout = null) {
  const sourceFiles = [
    route.sourceContract,
    route.moduleAsset
  ].filter(Boolean).map(sourceFileEvidence);
  const modelSummary = model ? {
    status: stringOrNull(model.status),
    source: stringOrNull(model.source),
    bodyKind: stringOrNull(model.bodyKind),
    shouldRender: booleanOrNull(model.shouldRender),
    rowCount: modelRowCount(model, layout),
    metricCount: arrayLength(model.metrics) + arrayLength(model.metricSections) + arrayLength(model.gridSections),
    flagCount: arrayLength(model.flags?.flags),
    carRadarCarCount: arrayLength(model.carRadar?.renderModel?.cars),
    trackMapMarkerCount: arrayLength(model.trackMap?.renderModel?.markers || model.trackMap?.markers)
  } : null;
  const base = {
    contract: 'screenshot-scenario-evidence/v1',
    surface: route.surface,
    renderer: route.renderer,
    sourceContract: route.sourceContract || null,
    moduleAsset: route.moduleAsset || null,
    overlayId: route.overlayId || null,
    tab: route.tab || null,
    region: route.region || null,
    previewMode: route.previewMode || null,
    unitSystem: route.unitSystem || screenshotUnitSystem,
    routeAlias: route.routeAlias || null,
    fixtureVariant: route.fixtureVariant || null,
    captureMode: route.captureMode || null,
    cropBounds: route.clip || null,
    configuredOverlaySize: route.configuredOverlaySize || null,
    comparisonMode: route.comparisonMode || null,
    comparisonLimit: route.comparisonLimit || null,
    compositingMode: route.compositingMode || null,
    captureBackdrop: route.captureBackdrop || null,
    fixture: route.surface?.includes('settings')
      ? 'browser-review-settings-fixture'
      : route.surface?.includes('installer')
        ? 'browser-review-installer-fixture'
        : 'browser-review-preview-fixture',
    urlPath: route.urlPath,
    selector: route.selector,
    settingsContract: {
      unitSystem: route.unitSystem || screenshotUnitSystem,
      unitToggleLifecycle: 'browser-review-renders-model-values-without-native-form-lifecycle'
    },
    sourceFiles,
    modelSummary,
    modelHash: model ? stableHash(model) : null
  };

  return {
    ...base,
    sourceHash: stableHash(sourceFiles),
    scenarioHash: stableHash(base)
  };
}

function sourceFileEvidence(relativePath) {
  const absolutePath = resolve(repoRoot, relativePath);
  if (!existsSync(absolutePath)) {
    return {
      path: relativePath,
      exists: false,
      bytes: null,
      sha256: null
    };
  }

  const data = readFileSync(absolutePath);
  return {
    path: relativePath,
    exists: true,
    bytes: data.length,
    sha256: createHash('sha256').update(data).digest('hex')
  };
}

function stableHash(value) {
  return createHash('sha256').update(stableJson(value)).digest('hex');
}

function stableJson(value) {
  if (value === null || typeof value !== 'object') {
    return JSON.stringify(value);
  }

  if (Array.isArray(value)) {
    return `[${value.map(stableJson).join(',')}]`;
  }

  return `{${Object.keys(value)
    .sort()
    .map((key) => `${JSON.stringify(key)}:${stableJson(value[key])}`)
    .join(',')}}`;
}

function graphModelEvidence(model, layout) {
  if (model?.graph) {
    return graphEvidence(model.graph, layout);
  }

  if (model?.bodyKind === 'graph' && Array.isArray(model?.points)) {
    return graphEvidence({
      points: model.points,
      startSeconds: 0,
      endSeconds: Math.max(1, model.points.length - 1),
      maxGapSeconds: Math.max(1, ...model.points.map(Number).filter(Number.isFinite)),
      comparisonLabel: stringOrNull(model?.status) || '--'
    }, layout);
  }

  return null;
}

function graphEvidence(graph, layout) {
  const canvasBounds = findElementBounds(layout, 'graph-canvas', 'model-graph');
  const geometry = canvasBounds ? browserGraphGeometry(graph, canvasBounds) : null;
  const geometrySeries = Array.isArray(geometry?.series) ? geometry.series : [];
  return {
    startSeconds: numberOrNull(graph?.startSeconds),
    endSeconds: numberOrNull(graph?.endSeconds),
    maxGapSeconds: numberOrNull(graph?.maxGapSeconds),
    lapReferenceSeconds: numberOrNull(graph?.lapReferenceSeconds),
    selectedSeriesCount: numberOrNull(graph?.selectedSeriesCount),
    metricDeadbandSeconds: numberOrNull(graph?.metricDeadbandSeconds),
    comparisonLabel: stringOrNull(graph?.comparisonLabel),
    canvasBounds,
    gridLines: geometry?.gridLines || null,
    geometry,
    series: (Array.isArray(graph?.series) ? graph.series : []).map((series, index) => ({
      index,
      carIdx: numberOrNull(series?.carIdx),
      classPosition: numberOrNull(series?.classPosition),
      isReference: Boolean(series?.isReference),
      isClassLeader: Boolean(series?.isClassLeader),
      alpha: numberOrNull(series?.alpha),
      isStickyExit: Boolean(series?.isStickyExit),
      isStale: Boolean(series?.isStale),
      pointCount: arrayLength(series?.points),
      baseColor: geometrySeries.find((candidate) => candidate.sourceIndex === index)?.baseColor || null,
      renderedColor: geometrySeries.find((candidate) => candidate.sourceIndex === index)?.renderedColor || null,
      effectiveAlpha: geometrySeries.find((candidate) => candidate.sourceIndex === index)?.effectiveAlpha ?? null,
      strokeWidth: geometrySeries.find((candidate) => candidate.sourceIndex === index)?.strokeWidth ?? null,
      isDashed: geometrySeries.find((candidate) => candidate.sourceIndex === index)?.isDashed ?? null,
      endpointLabel: geometrySeries.find((candidate) => candidate.sourceIndex === index)?.endpointLabel || null,
      latestPoint: geometrySeries.find((candidate) => candidate.sourceIndex === index)?.latestPoint || null
    })),
    trendMetricCount: arrayLength(graph?.trendMetrics),
    trendMetrics: (Array.isArray(graph?.trendMetrics) ? graph.trendMetrics : []).map((metric, index) => ({
      index,
      label: stringOrNull(metric?.label),
      state: stringOrNull(metric?.state),
      stateLabel: stringOrNull(metric?.stateLabel),
      valueText: graphMetricValueText(metric),
      chaserText: graphMetricChaserText(metric)
    })),
    weatherCount: arrayLength(graph?.weather),
    leaderChangeCount: arrayLength(graph?.leaderChanges),
    driverChangeCount: arrayLength(graph?.driverChanges)
  };
}

function browserGraphGeometry(graph, canvasBounds) {
  const local = browserGapGraphLayout(canvasBounds.width, canvasBounds.height);
  const scale = graph?.scale || { isFocusRelative: false, maxGapSeconds: graph?.maxGapSeconds };
  const maxGapSeconds = Math.max(1, numberOr(scale?.maxGapSeconds, graph?.maxGapSeconds, 1));
  const rawSeries = Array.isArray(graph?.series) ? graph.series : [];
  if (rawSeries.length === 0 && Array.isArray(graph?.points) && graph.points.length > 0) {
    return browserFallbackGraphGeometry(graph, canvasBounds, local);
  }

  const orderedSeries = rawSeries
    .map((series, sourceIndex) => ({ series, sourceIndex }))
    .sort((a, b) =>
      Number(Boolean(a.series?.isClassLeader)) - Number(Boolean(b.series?.isClassLeader))
      || Number(Boolean(a.series?.isReference)) - Number(Boolean(b.series?.isReference)));
  return {
    frame: canvasBounds,
    plot: offsetRect(canvasBounds, local.plot),
    axis: offsetRect(canvasBounds, local.axis),
    labelLane: offsetRect(canvasBounds, local.labelLane),
    metricsTable: local.metricsRect ? offsetRect(canvasBounds, local.metricsRect) : null,
    scale: scale?.isFocusRelative === true ? 'focus-relative' : 'leader',
    aheadSeconds: scale?.isFocusRelative === true ? numberOrNull(scale?.aheadSeconds) : null,
    behindSeconds: scale?.isFocusRelative === true ? numberOrNull(scale?.behindSeconds) : null,
    latestReferenceGapSeconds: scale?.isFocusRelative === true ? numberOrNull(scale?.latestReferenceGapSeconds) : null,
    gridLines: graphGridLines(graph, scale, local.plot, maxGapSeconds, canvasBounds),
    weatherBands: graphWeatherBands(graph, local.plot, canvasBounds),
    markers: graphMarkers(graph, scale, local.plot, maxGapSeconds, canvasBounds),
    metricRows: graphMetricRows(local.metricsRect, graph, canvasBounds),
    series: orderedSeries.map(({ series, sourceIndex }, drawIndex) =>
      graphSeriesGeometry(graph, scale, local.plot, maxGapSeconds, canvasBounds, series, sourceIndex, drawIndex))
  };
}

function browserFallbackGraphGeometry(graph, canvasBounds, local) {
  const values = graph.points.map(Number).filter(Number.isFinite);
  const max = Math.max(1, ...values.map((value) => Math.max(0, value)));
  const points = values.map((value, index) => {
    const progress = index / Math.max(1, values.length - 1);
    const normalized = Math.max(0, value) / max;
    return {
      axisSeconds: index,
      gapSeconds: value,
      startsSegment: index === 0,
      point: offsetPoint(canvasBounds, {
        x: local.plot.x + progress * local.plot.width,
        y: local.plot.y + normalized * local.plot.height
      })
    };
  });
  return {
    frame: canvasBounds,
    plot: offsetRect(canvasBounds, local.plot),
    axis: offsetRect(canvasBounds, local.axis),
    labelLane: offsetRect(canvasBounds, local.labelLane),
    metricsTable: local.metricsRect ? offsetRect(canvasBounds, local.metricsRect) : null,
    scale: 'leader',
    aheadSeconds: null,
    behindSeconds: null,
    latestReferenceGapSeconds: null,
    gridLines: graphGridLines(graph, { isFocusRelative: false, maxGapSeconds: max }, local.plot, max, canvasBounds),
    weatherBands: [],
    markers: [],
    metricRows: [],
    series: [{
      sourceIndex: 0,
      drawIndex: 0,
      carIdx: null,
      classPosition: null,
      isReference: true,
      isClassLeader: false,
      pointCount: values.length,
      baseColor: '#00e8ff',
      renderedColor: colorWithAlpha('#00e8ff', 1),
      alpha: 1,
      effectiveAlpha: 1,
      strokeWidth: 2,
      isDashed: false,
      endpointLabel: 'trend',
      latestPoint: points[points.length - 1]?.point || null,
      points
    }]
  };
}

function graphSeriesGeometry(graph, scale, plot, maxGapSeconds, canvasBounds, series, sourceIndex, drawIndex) {
  const baseColor = graphSeriesColor(series, drawIndex, graph?.threatCarIdx);
  const alpha = clamp01(numberOr(series?.alpha, 1));
  const effectiveAlpha = alpha * graphSeriesAlphaMultiplier(series, graph?.threatCarIdx);
  const points = (Array.isArray(series?.points) ? series.points : [])
    .filter((point) => Number.isFinite(point?.axisSeconds) && Number.isFinite(point?.gapSeconds))
    .sort((a, b) => a.axisSeconds - b.axisSeconds)
    .map((point) => ({
      axisSeconds: numberOrNull(point.axisSeconds),
      gapSeconds: numberOrNull(point.gapSeconds),
      startsSegment: Boolean(point.startsSegment),
      point: offsetPoint(canvasBounds, graphPoint(graph, scale, plot, maxGapSeconds, point.axisSeconds, point.gapSeconds))
    }));
  const latest = points[points.length - 1] || null;
  return {
    sourceIndex,
    drawIndex,
    carIdx: numberOrNull(series?.carIdx),
    classPosition: numberOrNull(series?.classPosition),
    isReference: Boolean(series?.isReference),
    isClassLeader: Boolean(series?.isClassLeader),
    pointCount: arrayLength(series?.points),
    baseColor,
    renderedColor: colorWithAlpha(baseColor, effectiveAlpha),
    alpha,
    effectiveAlpha,
    strokeWidth: series?.isReference ? 2.6 : series?.isClassLeader ? 1.8 : 1.25,
    isDashed: Boolean(series?.isStale || series?.isStickyExit),
    endpointLabel: series?.isClassLeader
      ? 'P1'
      : Number.isFinite(series?.classPosition)
        ? `P${series.classPosition}`
        : `#${series?.carIdx ?? '--'}`,
    latestPoint: latest?.point || null,
    points
  };
}

function graphWeatherBands(graph, plot, canvasBounds) {
  const weather = Array.isArray(graph?.weather) ? graph.weather : [];
  const domain = graphDomain(graph);
  return weather
    .map((point, index) => {
      const color = weatherColor(point?.condition);
      if (!color) return null;
      const nextAxis = Number.isFinite(weather[index + 1]?.axisSeconds) ? weather[index + 1].axisSeconds : domain.end;
      const start = Math.max(domain.start, numberOr(point?.axisSeconds, domain.start));
      const end = Math.min(domain.end, nextAxis);
      if (end <= start) return null;
      const x = axisToX(graph, plot, start);
      const nextX = axisToX(graph, plot, end);
      return {
        kind: String(point?.condition ?? ''),
        startAxisSeconds: round(start),
        endAxisSeconds: round(end),
        bounds: offsetRect(canvasBounds, { x, y: plot.y, width: Math.max(1, nextX - x), height: plot.height }),
        color
      };
    })
    .filter(Boolean);
}

function graphMarkers(graph, scale, plot, maxGapSeconds, canvasBounds) {
  const markers = [];
  for (const marker of Array.isArray(graph?.leaderChanges) ? graph.leaderChanges : []) {
    if (!Number.isFinite(marker?.axisSeconds)) continue;
    const x = axisToX(graph, plot, marker.axisSeconds);
    markers.push({
      kind: 'leader-change',
      label: 'leader',
      axisSeconds: numberOrNull(marker.axisSeconds),
      start: offsetPoint(canvasBounds, { x, y: plot.y }),
      end: offsetPoint(canvasBounds, { x, y: plot.y + plot.height }),
      color: 'rgba(255, 255, 255, 0.45)'
    });
  }

  for (const marker of Array.isArray(graph?.driverChanges) ? graph.driverChanges : []) {
    if (!Number.isFinite(marker?.axisSeconds) || !Number.isFinite(marker?.gapSeconds)) continue;
    const point = graphPoint(graph, scale, plot, maxGapSeconds, marker.axisSeconds, marker.gapSeconds);
    markers.push({
      kind: 'driver-change',
      label: stringOrNull(marker?.label),
      axisSeconds: numberOrNull(marker.axisSeconds),
      gapSeconds: numberOrNull(marker.gapSeconds),
      carIdx: numberOrNull(marker?.carIdx),
      isReference: Boolean(marker?.isReference),
      start: offsetPoint(canvasBounds, point),
      end: offsetPoint(canvasBounds, point),
      color: marker?.isReference ? '#70e092' : '#cdd8e4'
    });
  }

  return markers;
}

function graphMetricRows(metricsRect, graph, canvasBounds) {
  const metrics = Array.isArray(graph?.trendMetrics) ? graph.trendMetrics.filter(Boolean) : [];
  if (!metricsRect || metrics.length === 0) return [];
  const showThreatFooter = metrics.length <= 6;
  const rowAreaBottomPadding = showThreatFooter ? 48 : 8;
  const rowHeight = Math.max(9.5, Math.min(26, (metricsRect.height - rowAreaBottomPadding - 38) / Math.max(1, metrics.length)));
  return metrics.map((metric, index) => {
    const y = metricsRect.y + 43 + index * rowHeight;
    const row = { x: metricsRect.x + 8, y, width: metricsRect.width - 16, height: Math.max(10, Math.min(14, rowHeight)) };
    return {
      index,
      text: stringOrNull(metric?.label),
      state: stringOrNull(metric?.state),
      bounds: offsetRect(canvasBounds, row),
      cells: [
        { column: 'Metric', text: stringOrNull(metric?.label), bounds: offsetRect(canvasBounds, { x: metricsRect.x + 8, y, width: 44, height: row.height }) },
        { column: stringOrNull(graph?.comparisonLabel) || '--', text: graphMetricValueText(metric), bounds: offsetRect(canvasBounds, { x: metricsRect.x + 56, y, width: 46, height: row.height }) },
        { column: 'Threat', text: graphMetricChaserText(metric), bounds: offsetRect(canvasBounds, { x: metricsRect.x + 108, y, width: metricsRect.width - 114, height: row.height }) }
      ]
    };
  });
}

function inputEvidence(inputs, layout) {
  const canvasBounds = findElementBounds(layout, 'input-graph', 'input-graph')
    || findElementBounds(layout, 'graph-canvas', 'input-graph');
  const railBounds = findElementBounds(layout, 'input-rail', 'input-rail');
  const gridLines = canvasBounds ? inputGridLines(canvasBounds) : [];
  const series = canvasBounds ? inputTraceSeries(inputs, canvasBounds) : [];
  return {
    hasContent: booleanOrNull(inputs?.hasContent),
    hasGraph: booleanOrNull(inputs?.hasGraph),
    hasRail: booleanOrNull(inputs?.hasRail),
    isAvailable: booleanOrNull(inputs?.isAvailable),
    sampleIntervalMilliseconds: numberOrNull(inputs?.sampleIntervalMilliseconds),
    maximumTracePoints: numberOrNull(inputs?.maximumTracePoints),
    tracePointCount: arrayLength(inputs?.trace),
    grid: gridLines,
    series,
    graph: canvasBounds ? {
      bounds: canvasBounds,
      gridLines,
      series
    } : null,
    rail: railBounds ? inputRailEvidence(layout, railBounds) : null
  };
}

function inputRailEvidence(layout, railBounds) {
  const groups = elementsForRole(layout, 'input-rail-group')
    .filter((element) => rectIntersects(element.bounds, railBounds))
    .sort(compareBounds)
    .map((element, index) => ({
      index,
      role: element.role || null,
      kind: inputRailGroupKind(element),
      text: stringOrNull(element.text),
      bounds: element.bounds || null,
      styles: element.styles || null
    }));

  return {
    bounds: railBounds,
    railWidth: railBounds.width,
    groups,
    items: inputRailItems(layout, railBounds)
  };
}

function inputRailItems(layout, railBounds) {
  return elementsForRole(layout, 'input-item')
    .filter((element) => rectIntersects(element.bounds, railBounds))
    .sort(compareBounds)
    .map((element, index) => {
      const children = inputRailItemChildren(layout, element.bounds);
      const track = children.find((child) => child.role === 'input-bar-track') || null;
      const fill = children.find((child) => child.role === 'input-bar-fill') || null;
      return {
        index,
        kind: inputRailItemKind(element, children),
        role: element.role || null,
        className: element.className || null,
        text: stringOrNull(element.text),
        bounds: element.bounds || null,
        foreground: element.styles?.color || null,
        background: element.styles?.backgroundColor || null,
        fillRatio: track?.bounds && fill?.bounds
          ? round(numberOr(fill.bounds.width, 0) / Math.max(1, numberOr(track.bounds.width, 0)))
          : null,
        children
      };
    });
}

function inputRailItemChildren(layout, itemBounds) {
  const childRoles = new Set([
    'input-bar-label',
    'input-bar-track',
    'input-bar-fill',
    'input-bar-value',
    'input-readout-label',
    'input-readout-value',
    'input-wheel-label',
    'input-wheel-value',
    'input-wheel-svg',
    'input-wheel-shape'
  ]);
  const elements = Array.isArray(layout?.elements) ? layout.elements : [];
  return elements
    .filter((element) => childRoles.has(element.role) && rectIntersects(element.bounds, itemBounds))
    .sort(compareBounds)
    .map((element, index) => ({
      index,
      role: element.role || null,
      kind: inputRailChildKind(element),
      tag: element.tag || null,
      text: stringOrNull(element.text),
      bounds: element.bounds || null,
      foreground: element.styles?.color || null,
      background: element.styles?.backgroundColor || null,
      fill: element.styles?.fill || null,
      stroke: element.styles?.stroke || null,
      opacity: element.styles?.opacity || null,
      styles: element.styles || null
    }));
}

function inputRailItemKind(element, children) {
  const className = String(element?.className || '');
  const classNames = className.split(/\s+/);
  const label = normalizeEvidenceText(children.find((child) => child.role?.endsWith('-label'))?.text);
  if (classNames.includes('input-wheel')) return 'SteeringWheel';
  if (classNames.includes('input-bar')) {
    if (label === 'thr') return 'Throttle';
    if (label === 'brk' || label === 'abs') return 'Brake';
    if (label === 'clt') return 'Clutch';
    return 'InputBar';
  }
  if (classNames.includes('input-readout')) {
    if (label === 'gear') return 'Gear';
    if (label === 'spd') return 'Speed';
    return 'InputReadout';
  }
  return 'InputRailItem';
}

function inputRailChildKind(element) {
  const role = String(element?.role || '');
  if (role.includes('label')) return 'Label';
  if (role.includes('value')) return 'Value';
  if (role.includes('track')) return 'Track';
  if (role.includes('fill')) return 'Fill';
  if (role.includes('svg')) return 'WheelSvg';
  if (role.includes('shape')) return 'WheelShape';
  return null;
}

function inputRailGroupKind(element) {
  const classNames = String(element?.className || '').split(/\s+/);
  if (classNames.includes('input-bars')) return 'Bars';
  if (classNames.includes('input-readouts')) return 'Readouts';
  return 'Group';
}

function inputGridLines(bounds) {
  return [1, 2, 3].map((step) => {
    const y = bounds.y + bounds.height * step / 4;
    return {
      kind: 'input-grid',
      start: { x: round(bounds.x), y: round(y) },
      end: { x: round(bounds.x + bounds.width), y: round(y) },
      color: 'rgba(140, 174, 212, 0.18)',
      strokeWidth: 1
    };
  });
}

function inputTraceSeries(inputs, bounds) {
  const trace = Array.isArray(inputs?.trace) ? inputs.trace : [];
  const series = [];
  addTrace(Boolean(inputs?.showThrottleTrace), 'throttle', '#62ff9f', 'throttle', 2);
  addTrace(Boolean(inputs?.showBrakeTrace), 'brake', '#ff6274', 'brake', 2);
  addTrace(Boolean(inputs?.showClutchTrace), 'clutch', '#00e8ff', 'clutch', 2);
  if (inputs?.showBrakeTrace === true && trace.length >= 2) {
    const curves = [];
    for (let index = 1; index < trace.length; index += 1) {
      if (trace[index]?.brakeAbsActive !== true) continue;
      const start = inputTracePoint(trace[index - 1]?.brake, index - 1, trace.length, bounds);
      const end = inputTracePoint(trace[index]?.brake, index, trace.length, bounds);
      curves.push({ start, control1: start, control2: end, end });
    }
    if (curves.length) {
      series.push({ kind: 'brake-abs', color: '#ffd15b', strokeWidth: 3, pointCount: 0, curveCount: curves.length, points: [], curves });
    }
  }
  return series;

  function addTrace(enabled, kind, color, key, strokeWidth) {
    if (!enabled) return;
    if (trace.length < 2) {
      const y = bounds.y + bounds.height / 2;
      const start = { x: round(bounds.x + 8), y: round(y) };
      const end = { x: round(bounds.x + bounds.width - 8), y: round(y) };
      const curves = [{ start, control1: start, control2: end, end }];
      series.push({ kind, color: colorWithAlpha(color, 0.24), strokeWidth, pointCount: 0, curveCount: curves.length, points: [], curves });
      return;
    }
    const points = trace.map((point, index) => inputTracePoint(point?.[key], index, trace.length, bounds));
    const curves = smoothTraceCurves(points);
    series.push({ kind, color, strokeWidth, pointCount: points.length, curveCount: curves.length, points, curves });
  }
}

function browserGapGraphLayout(width, height) {
  const axisWidth = 58;
  const xAxisHeight = 17;
  const labelLaneWidth = 38;
  const metricsWidth = gapMetricsTableWidth(width);
  const plotHeight = Math.max(40, height - xAxisHeight);
  const metricsRect = metricsWidth > 0
    ? { x: width - metricsWidth, y: 0, width: metricsWidth, height: plotHeight }
    : null;
  const chartRight = metricsRect ? metricsRect.x - 10 : width - 4;
  const labelLane = { x: chartRight - labelLaneWidth, y: 0, width: labelLaneWidth, height: plotHeight };
  const plot = { x: axisWidth, y: 0, width: Math.max(40, labelLane.x - axisWidth), height: plotHeight };
  const axis = { x: 0, y: 0, width: axisWidth - 8, height: plotHeight };
  return { plot, axis, labelLane, metricsRect };
}

function gapMetricsTableWidth(width) {
  const metricsWidth = 184;
  const availableAfterTable = width - 58 - 38 - 10 - metricsWidth;
  return availableAfterTable >= 300 ? metricsWidth : 0;
}

function graphGridLines(graph, scale, plot, maxGapSeconds, canvasBounds) {
  const lines = [
    graphLine('axis-top', plot.x, plot.y, plot.x + plot.width, plot.y, 'rgba(140, 174, 212, 0.24)', 1, canvasBounds),
    graphLine('axis-bottom', plot.x, plot.y + plot.height, plot.x + plot.width, plot.y + plot.height, 'rgba(140, 174, 212, 0.24)', 1, canvasBounds)
  ];

  if (scale?.isFocusRelative === true) {
    const referenceY = plot.y + plot.height * 0.56;
    lines.push(graphLine('focus-reference', plot.x, referenceY, plot.x + plot.width, referenceY, 'rgba(98, 255, 159, 0.43)', 1.2, canvasBounds));
    const aheadStep = niceGridStep(numberOr(scale?.aheadSeconds, 1) / 2);
    for (let value = aheadStep; value < numberOr(scale?.aheadSeconds, 0); value += aheadStep) {
      const y = gapDeltaToY(-value, scale, plot);
      lines.push(graphLine('gap-grid-ahead', plot.x, y, plot.x + plot.width, y, 'rgba(140, 174, 212, 0.18)', 1, canvasBounds));
    }

    const behindStep = niceGridStep(numberOr(scale?.behindSeconds, 1) / 2);
    for (let value = behindStep; value < numberOr(scale?.behindSeconds, 0); value += behindStep) {
      const y = gapDeltaToY(value, scale, plot);
      lines.push(graphLine('gap-grid-behind', plot.x, y, plot.x + plot.width, y, 'rgba(140, 174, 212, 0.18)', 1, canvasBounds));
    }

    return lines;
  }

  const step = niceGridStep(maxGapSeconds / 4);
  for (let value = step; value < maxGapSeconds; value += step) {
    const y = gapToY(value, maxGapSeconds, plot);
    lines.push(graphLine('gap-grid', plot.x, y, plot.x + plot.width, y, 'rgba(140, 174, 212, 0.18)', 1, canvasBounds));
  }

  const lapSeconds = Number(graph?.lapReferenceSeconds);
  if (Number.isFinite(lapSeconds) && lapSeconds >= 20 && maxGapSeconds >= lapSeconds * 0.85) {
    for (let lap = 1; lap * lapSeconds < maxGapSeconds; lap += 1) {
      const y = gapToY(lap * lapSeconds, maxGapSeconds, plot);
      lines.push(graphLine('gap-lap-reference', plot.x, y, plot.x + plot.width, y, 'rgba(255, 247, 255, 0.58)', 1.2, canvasBounds));
    }
  }

  return lines;
}

function graphLine(kind, x1, y1, x2, y2, color, strokeWidth, canvasBounds) {
  return {
    kind,
    start: offsetPoint(canvasBounds, { x: x1, y: y1 }),
    end: offsetPoint(canvasBounds, { x: x2, y: y2 }),
    color,
    strokeWidth
  };
}

function graphPoint(graph, scale, plot, maxGapSeconds, axisSeconds, gapSeconds) {
  const x = axisToX(graph, plot, axisSeconds);
  const y = scale?.isFocusRelative === true
    ? gapDeltaToY(gapSeconds - referenceGapAt(scale.referencePoints || [], axisSeconds), scale, plot)
    : gapToY(gapSeconds, maxGapSeconds, plot);
  return { x, y };
}

function axisToX(graph, plot, axisSeconds) {
  const domain = graphDomain(graph);
  const ratio = (axisSeconds - domain.start) / Math.max(1, domain.end - domain.start);
  return plot.x + Math.max(0, Math.min(1, ratio)) * plot.width;
}

function gapToY(gapSeconds, maxGapSeconds, plot) {
  return plot.y + Math.max(0, Math.min(1, gapSeconds / Math.max(1, maxGapSeconds))) * plot.height;
}

function niceGridStep(value) {
  if (!Number.isFinite(value) || value <= 0.25) return 0.25;
  const magnitude = 10 ** Math.floor(Math.log10(value));
  const normalized = value / magnitude;
  for (const step of [1, 2, 2.5, 5, 10]) {
    if (normalized <= step) return step * magnitude;
  }
  return 10 * magnitude;
}

function gapDeltaToY(deltaSeconds, scale, plot) {
  const referenceY = plot.y + plot.height * 0.56;
  if (deltaSeconds < 0) {
    const ratio = Math.max(0, Math.min(1, Math.abs(deltaSeconds) / Math.max(1, numberOr(scale?.aheadSeconds, 1))));
    return referenceY - ratio * Math.max(1, referenceY - (plot.y + 18));
  }
  const ratio = Math.max(0, Math.min(1, deltaSeconds / Math.max(1, numberOr(scale?.behindSeconds, 1))));
  return referenceY + ratio * Math.max(1, plot.y + plot.height - 8 - referenceY);
}

function referenceGapAt(points, axisSeconds) {
  const ordered = (Array.isArray(points) ? points : [])
    .filter((point) => Number.isFinite(point?.axisSeconds) && Number.isFinite(point?.gapSeconds))
    .sort((a, b) => a.axisSeconds - b.axisSeconds);
  if (!ordered.length) return 0;
  if (axisSeconds <= ordered[0].axisSeconds) return ordered[0].gapSeconds;
  const last = ordered[ordered.length - 1];
  if (axisSeconds >= last.axisSeconds) return last.gapSeconds;
  const afterIndex = ordered.findIndex((point) => point.axisSeconds >= axisSeconds);
  const after = ordered[Math.max(0, afterIndex)];
  const before = ordered[Math.max(0, afterIndex - 1)];
  const span = after.axisSeconds - before.axisSeconds;
  if (span <= 0.001) return before.gapSeconds;
  const ratio = Math.max(0, Math.min(1, (axisSeconds - before.axisSeconds) / span));
  return before.gapSeconds + (after.gapSeconds - before.gapSeconds) * ratio;
}

function graphDomain(graph) {
  const start = numberOr(graph?.startSeconds, 0);
  const end = Math.max(start + 1, numberOr(graph?.endSeconds, start + 1));
  return { start, end };
}

function graphSeriesColor(series, index, threatCarIdx) {
  if (Number.isFinite(Number(threatCarIdx)) && Number(series?.carIdx) === Number(threatCarIdx)) return '#ff6274';
  if (series?.isReference) return '#00e8ff';
  if (series?.isClassLeader) return '#fff7ff';
  return ['#ffd15b', '#62ff9f', '#ff2aa7'][index % 3];
}

function graphSeriesAlphaMultiplier(series, threatCarIdx) {
  return series?.isClassLeader || series?.isReference || (Number.isFinite(Number(threatCarIdx)) && Number(series?.carIdx) === Number(threatCarIdx)) ? 1 : 0.48;
}

function weatherColor(condition) {
  if (condition === 2 || String(condition).toLowerCase() === 'damp') return 'rgba(75, 170, 205, 0.08)';
  if (condition === 3 || String(condition).toLowerCase() === 'wet') return 'rgba(70, 135, 230, 0.13)';
  if (condition === 4 || String(condition).toLowerCase() === 'declaredwet' || String(condition).toLowerCase() === 'declared-wet') return 'rgba(78, 142, 238, 0.17)';
  return null;
}

function inputTracePoint(value, index, count, bounds) {
  return {
    x: round(bounds.x + index / Math.max(1, count - 1) * bounds.width),
    y: round(bounds.y + bounds.height - clamp01(Number(value)) * bounds.height)
  };
}

function smoothTraceCurves(points) {
  const curves = [];
  for (let index = 0; index < points.length - 1; index += 1) {
    const p0 = index === 0 ? points[index] : points[index - 1];
    const p1 = points[index];
    const p2 = points[index + 1];
    const p3 = index + 2 < points.length ? points[index + 2] : p2;
    const control1 = {
      x: round(p1.x + (p2.x - p0.x) / 6),
      y: round(clampSmoothTraceControlY(p1.y + (p2.y - p0.y) / 6, p1.y, p2.y))
    };
    const control2 = {
      x: round(p2.x - (p3.x - p1.x) / 6),
      y: round(clampSmoothTraceControlY(p2.y - (p3.y - p1.y) / 6, p1.y, p2.y))
    };
    curves.push({ start: p1, control1, control2, end: p2 });
  }
  return curves;
}

function clampSmoothTraceControlY(value, segmentStartY, segmentEndY) {
  return Math.max(Math.min(segmentStartY, segmentEndY), Math.min(Math.max(segmentStartY, segmentEndY), value));
}

function graphMetricValueText(metric) {
  const state = String(metric?.state || '').toLowerCase();
  if (state === 'pit') return pitSecondsText(metric?.comparisonPit);
  if (state === 'pitlap') return pitLapText(metric?.comparisonPit);
  if (state === 'tire') return tireText(metric?.comparisonTire);
  if (state === 'stint' || state === 'last' || state === 'status') return stringOrNull(metric?.comparisonText) || '--';
  if (state === 'ready' && Number.isFinite(Number(metric?.focusGapChangeSeconds))) return formatChangeSeconds(-Number(metric.focusGapChangeSeconds));
  if (state === 'ready' && stringOrNull(metric?.stateLabel)) return stringOrNull(metric.stateLabel);
  if (state === 'warming') return stringOrNull(metric?.stateLabel) || '--';
  if (state === 'leaderchanged') return 'leader';
  return '--';
}

function graphMetricChaserText(metric) {
  const state = String(metric?.state || '').toLowerCase();
  if (state === 'pit') return pitSecondsText(metric?.threatPit);
  if (state === 'pitlap') return pitLapText(metric?.threatPit);
  if (state === 'tire') return tireText(metric?.threatTire);
  if (state === 'stint' || state === 'last' || state === 'status') return stringOrNull(metric?.threatText) || '--';
  if (state === 'ready' && metric?.chaser) {
    return `${metric.chaser.label || `#${metric.chaser.carIdx ?? '--'}`} ${formatChangeSeconds(-Number(metric.chaser.gainSeconds))}`;
  }
  if (state === 'leaderchanged') return 'reset';
  return '--';
}

function pitSecondsText(pit) {
  const seconds = Number(pit?.seconds);
  if (!Number.isFinite(seconds)) return '--';
  return seconds >= 60 ? `${Math.floor(seconds / 60)}:${Math.round(seconds % 60).toString().padStart(2, '0')}` : `${Math.round(seconds)}s`;
}

function pitLapText(pit) {
  const lap = Number(pit?.lap);
  return Number.isFinite(lap) && lap > 0 ? `L${lap}` : '--';
}

function tireText(tire) {
  return stringOrNull(tire?.shortLabel) || stringOrNull(tire?.label) || '--';
}

function formatChangeSeconds(value) {
  if (!Number.isFinite(value)) return '--';
  if (Math.abs(value) < 0.05) return '0.0';
  return `${value > 0 ? '+' : ''}${value.toFixed(1)}`;
}

function findElementBounds(layout, role, classToken) {
  const elements = Array.isArray(layout?.elements) ? layout.elements : [];
  const element = elements.find((item) =>
    item?.role === role
    && (!classToken || String(item?.className || '').split(/\s+/).includes(classToken)));
  return element?.bounds || null;
}

function offsetRect(offset, rect) {
  return {
    x: round(offset.x + rect.x),
    y: round(offset.y + rect.y),
    width: round(rect.width),
    height: round(rect.height)
  };
}

function offsetPoint(offset, point) {
  return {
    x: round(offset.x + point.x),
    y: round(offset.y + point.y)
  };
}

function colorWithAlpha(hex, alpha) {
  const value = String(hex || '').replace('#', '');
  if (!/^[0-9a-f]{6}$/i.test(value)) return hex;
  const red = Number.parseInt(value.slice(0, 2), 16);
  const green = Number.parseInt(value.slice(2, 4), 16);
  const blue = Number.parseInt(value.slice(4, 6), 16);
  return `rgba(${red}, ${green}, ${blue}, ${round(alpha)})`;
}

function stringOrNull(value) {
  const text = typeof value === 'string' ? value.trim() : '';
  return text || null;
}

function round(value) {
  return Math.round(Number(value || 0) * 1000) / 1000;
}

function numberOr(...values) {
  for (const value of values) {
    const number = Number(value);
    if (Number.isFinite(number)) return number;
  }
  return 0;
}

function clamp01(value) {
  return Number.isFinite(value) ? Math.max(0, Math.min(1, value)) : 0;
}

function booleanOrNull(value) {
  return typeof value === 'boolean' ? value : null;
}

function numberOrNull(value) {
  if (value === null || value === undefined || value === '') {
    return null;
  }
  const number = Number(value);
  return Number.isFinite(number) ? number : null;
}

function arrayLength(value) {
  return Array.isArray(value) ? value.length : 0;
}

function modelRowCount(model, layout = null) {
  if (!model || typeof model !== 'object') {
    return 0;
  }
  if (stringOrNull(model.bodyKind) === 'stream-chat') {
    return elementsForRole(layout, 'chat-line').length;
  }
  if (Array.isArray(model.rows)) {
    return model.rows.length;
  }
  if (Array.isArray(model.streamChat?.rows)) {
    return model.streamChat.rows.length;
  }
  return 0;
}

function inspectPng(path, minBytes) {
  if (!existsSync(path)) {
    throw new Error(`Screenshot missing: ${path}`);
  }
  const stats = statSync(path);
  if (stats.size < minBytes) {
    throw new Error(`Screenshot too small: ${path} (${stats.size} bytes)`);
  }

  const data = readFileSync(path);
  const pngSignature = '89504e470d0a1a0a';
  if (data.subarray(0, 8).toString('hex') !== pngSignature) {
    throw new Error(`Screenshot is not a PNG: ${path}`);
  }

  const width = data.readUInt32BE(16);
  const height = data.readUInt32BE(20);
  if (width <= 0 || height <= 0) {
    throw new Error(`Screenshot has invalid dimensions: ${path} (${width}x${height})`);
  }

  return {
    width,
    height,
    bytes: stats.size
  };
}

function startReviewServer(port) {
  const child = spawn(
    process.execPath,
    [resolve(repoRoot, 'tools/browser-review/server.mjs')],
    {
      cwd: repoRoot,
      env: {
        ...process.env,
        TMR_BROWSER_REVIEW_PORT: String(port)
      },
      stdio: ['ignore', 'pipe', 'pipe']
    });

  child.stdout.on('data', (chunk) => {
    if (args.verbose) {
      process.stderr.write(chunk);
    }
  });
  child.stderr.on('data', (chunk) => process.stderr.write(chunk));
  child.on('exit', (code, signal) => {
    if (code !== null && code !== 0) {
      process.stderr.write(`Browser review server exited with code ${code}\n`);
    } else if (signal && signal !== 'SIGTERM') {
      process.stderr.write(`Browser review server exited with signal ${signal}\n`);
    }
  });

  return child;
}

async function waitForServer(url) {
  const deadline = Date.now() + 10_000;
  let lastError = null;

  while (Date.now() < deadline) {
    try {
      const response = await fetch(url);
      if (response.ok) {
        return;
      }
      lastError = new Error(`${response.status} ${response.statusText}`);
    } catch (error) {
      lastError = error;
    }

    await new Promise((resolveTimer) => setTimeout(resolveTimer, 150));
  }

  throw new Error(`Timed out waiting for browser review server at ${url}: ${formatError(lastError)}`);
}

function parseArgs(values) {
  const parsed = {
    baseUrl: '',
    output: '',
    port: 0,
    settleMilliseconds: null,
    surface: 'browser-review',
    verbose: false
  };

  for (let index = 0; index < values.length; index++) {
    const value = values[index];
    if (value === '--base-url') {
      parsed.baseUrl = requiredValue(values, ++index, value);
    } else if (value === '--output') {
      parsed.output = requiredValue(values, ++index, value);
    } else if (value === '--port') {
      parsed.port = Number.parseInt(requiredValue(values, ++index, value), 10);
    } else if (value === '--settle-ms') {
      parsed.settleMilliseconds = Number.parseInt(requiredValue(values, ++index, value), 10);
    } else if (value === '--surface') {
      parsed.surface = requiredValue(values, ++index, value);
    } else if (value === '--verbose') {
      parsed.verbose = true;
    } else {
      throw new Error(`Unknown argument: ${value}`);
    }
  }

  if (parsed.port && !Number.isFinite(parsed.port)) {
    throw new Error('Invalid --port value.');
  }
  if (parsed.settleMilliseconds !== null && !Number.isFinite(parsed.settleMilliseconds)) {
    throw new Error('Invalid --settle-ms value.');
  }
  if (!['browser-review', 'localhost', 'all'].includes(parsed.surface)) {
    throw new Error('Invalid --surface value. Expected browser-review, localhost, or all.');
  }

  return parsed;
}

function defaultOutputFor(surface) {
  if (surface === 'localhost') {
    return 'artifacts/localhost-screenshots';
  }
  if (surface === 'all') {
    return 'artifacts/browser-localhost-screenshots';
  }
  return 'artifacts/browser-review-screenshots';
}

function serverProbePath(surface) {
  return surface === 'localhost'
    ? '/overlays/standings?preview=race'
    : '/review/app';
}

function requiredValue(values, index, flag) {
  const value = values[index];
  if (!value || value.startsWith('--')) {
    throw new Error(`${flag} requires a value.`);
  }
  return value;
}

function stripTrailingSlash(value) {
  return String(value || '').replace(/\/+$/, '');
}

function normalizeUnitSystem(value) {
  return String(value || '').trim().toLowerCase() === 'imperial'
    ? 'Imperial'
    : 'Metric';
}

function formatError(error) {
  return error instanceof Error ? error.message : String(error || 'unknown error');
}
