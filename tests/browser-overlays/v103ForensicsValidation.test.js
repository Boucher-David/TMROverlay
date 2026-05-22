import { afterAll, beforeAll, describe, expect, it } from 'vitest';
import { existsSync, readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { repoRoot } from './browserOverlayAssets.js';
import { startReviewServer } from './reviewServerTestHost.js';

const coveragePath = resolve(repoRoot, 'tools/validation/v103-forensics-coverage.json');
const trackerPath = resolve(repoRoot, 'docs/v1.0.3.md');

let reviewServer;

beforeAll(async () => {
  reviewServer = await startReviewServer();
}, 10000);

afterAll(async () => {
  await reviewServer?.stop();
});

describe('v1.0.3 forensic validation coverage', () => {
  it('keeps every forensic finding tied to a validation source', () => {
    const coverage = JSON.parse(readFileSync(coveragePath, 'utf8'));
    const tracker = readFileSync(trackerPath, 'utf8');

    expect.soft(coverage.contract).toBe('v103-forensics-validation-coverage/v1');
    expect.soft(coverage.tracker).toBe('docs/v1.0.3.md');
    expect.soft(coverage.items.length, 'v1.0.3 forensic coverage item count').toBeGreaterThanOrEqual(15);

    const classifications = new Set(coverage.items.map((item) => item.classification));
    for (const classification of ['product-bug', 'diagnostics-evidence-gap', 'capture-data-limitation', 'data-contract-parity']) {
      expect.soft(classifications.has(classification), `missing classification ${classification}`).toBe(true);
    }

    for (const item of coverage.items) {
      expect.soft(item.id, 'coverage item id').toMatch(/^V103-\d{3}$/);
      expect.soft(String(item.finding || '').length, `${item.id}: finding`).toBeGreaterThan(24);
      expect.soft(String(item.validation || '').length, `${item.id}: validation`).toBeGreaterThan(24);
      expect.soft(
        docMentionsSurface(tracker, item.surface),
        `${item.id}: docs/v1.0.3.md should mention the surface ${item.surface}`
      ).toBe(true);
      expect.soft(Array.isArray(item.sources) && item.sources.length > 0, `${item.id}: sources`).toBe(true);

      for (const source of item.sources || []) {
        expect.soft(existsSync(resolve(repoRoot, source)), `${item.id}: source exists: ${source}`).toBe(true);
      }
    }
  });

  it('rejects redundant overlay titles from ordinary localhost/browser models', async () => {
    const overlayIds = [
      'standings',
      'relative',
      'fuel-calculator',
      'session-weather',
      'pit-service',
      'input-state',
      'car-radar',
      'gap-to-leader',
      'track-map',
      'flags',
      'stream-chat'
    ];

    for (const overlayId of overlayIds) {
      const model = (await reviewServer.getJson(`/api/overlay-model/${overlayId}?preview=race`)).model;

      expect.soft(
        renderedTitle(model),
        `${overlayId}: localhost/browser data contract must not carry a redundant rendered overlay title`
      ).toBe('');
      expect.soft(
        allVisibleModelText(model),
        `${overlayId}: visible model text must not include the overlay display title as chrome`
      ).not.toContain(normalizedOverlayTitle(overlayId));
    }
  });

  it('requires shared settings fingerprints for every runtime surface', async () => {
    for (const overlayId of ['standings', 'relative']) {
      const model = (await reviewServer.getJson(`/api/overlay-model/${overlayId}?preview=race`)).model;
      const sources = model.effectiveSettings?.sources || {};

      for (const sourceName of ['browserReview', 'localhostObs', 'windowsNative']) {
        expect.soft(
          sources[sourceName],
          `${overlayId}: missing ${sourceName} effective-settings source`
        ).toMatchObject({
          applied: true,
          sharedSettingsHash: expect.any(String),
          overlaySettingsHash: expect.any(String)
        });
      }

      const sourceFingerprints = ['browserReview', 'localhostObs', 'windowsNative']
        .map((sourceName) => `${sources[sourceName]?.sharedSettingsHash}:${sources[sourceName]?.overlaySettingsHash}`);
      expect.soft(
        new Set(sourceFingerprints).size,
        `${overlayId}: browser, localhost, and native must prove the same settings fingerprint`
      ).toBe(1);
    }
  });

  it('requires table row and column identity evidence for parity, not just counts', async () => {
    for (const overlayId of ['standings', 'relative']) {
      const model = (await reviewServer.getJson(`/api/overlay-model/${overlayId}?preview=race`)).model;
      const rendered = model.effectiveSettings?.rendered || {};
      const columnKeys = (model.columns || []).map((column) => column.dataKey);
      const rowKeys = (model.rows || []).map(rowIdentity);

      expect.soft(rendered.columnKeys, `${overlayId}: rendered column key evidence`).toEqual(columnKeys);
      expect.soft(rendered.rowIdentities, `${overlayId}: rendered row identity evidence`).toEqual(rowKeys);
      expect.soft(rendered.placeholderRowCount, `${overlayId}: placeholder row count evidence`).toEqual(
        (model.rows || []).filter((row) => row.isPlaceholder || !row.cells?.some((cell) => String(cell || '').trim())).length
      );
    }
  });

  it('requires preview provenance and route evidence for every supported overlay surface', async () => {
    const overlayIds = [
      'standings',
      'relative',
      'fuel-calculator',
      'session-weather',
      'pit-service',
      'input-state',
      'car-radar',
      'gap-to-leader',
      'track-map',
      'flags',
      'garage-cover',
      'stream-chat'
    ];

    for (const overlayId of overlayIds) {
      const model = (await reviewServer.getJson(`/api/overlay-model/${overlayId}?preview=race`)).model;
      const rendered = model.effectiveSettings?.rendered || {};
      const sources = model.effectiveSettings?.sources || {};

      expect.soft(rendered.provenance, `${overlayId}: missing rendered provenance`).toMatchObject({
        evidenceClass: expect.stringMatching(/^(live-capture|synthetic-preview|stale-history|unavailable)$/),
        captureSpecific: expect.any(Boolean),
        sourceContract: expect.any(String)
      });
      expect.soft(sources.browserReview?.routePath, `${overlayId}: browser-review route evidence`).toBe(`/review/overlays/${overlayId}`);
      expect.soft(sources.localhostObs?.routePath, `${overlayId}: localhost route evidence`).toBe(`/overlays/${overlayId}`);
      expect.soft(
        sources.windowsNative?.pixelEvidence,
        `${overlayId}: native surface must declare current pixel evidence or an explicit unsupported reason`
      ).toMatchObject({
        status: expect.stringMatching(/^(captured|disabled|unsupported|not-applicable)$/),
        reason: expect.any(String)
      });
    }
  });

  it('requires local role context with spectator and future spotting slots', async () => {
    for (const overlayId of ['standings', 'relative', 'fuel-calculator', 'input-state', 'car-radar']) {
      const model = (await reviewServer.getJson(`/api/overlay-model/${overlayId}?preview=race`)).model;
      expect.soft(model.effectiveSettings?.rendered?.roleContext, `${overlayId}: missing role context`).toMatchObject({
        localRole: expect.stringMatching(/^(driver|spectator|unknown)$/),
        roleSource: expect.any(String),
        playerIsSpectator: expect.any(Boolean),
        focusIsSpectator: expect.any(Boolean),
        spottingSignalStatus: expect.any(String)
      });
      expect.soft(
        Object.prototype.hasOwnProperty.call(model.effectiveSettings?.rendered?.roleContext || {}, 'isSpotting'),
        `${overlayId}: role context should reserve nullable isSpotting`
      ).toBe(true);
    }
  });

  it('requires table diagnostics to catch clipped rows and impossible timing values', async () => {
    const standings = (await reviewServer.getJson('/api/overlay-model/standings?preview=race')).model;
    const rendered = standings.effectiveSettings?.rendered || {};

    expect.soft(rendered.tableStatus, 'standings: rendered table status evidence').toMatchObject({
      dataRowCount: expect.any(Number),
      classHeaderCount: expect.any(Number),
      placeholderRowCount: expect.any(Number),
      clippedRowCount: expect.any(Number),
      statusCarCount: expect.any(Number)
    });
    expect.soft(
      rendered.tableStatus?.clippedRowCount ?? Number.POSITIVE_INFINITY,
      'standings: status must not claim rows clipped out of the visible table'
    ).toBe(0);
    expect.soft(rendered.timingSanity, 'standings: timing sanity evidence').toMatchObject({
      maxIntervalGapRatio: expect.any(Number),
      absurdIntervalCount: 0
    });
  });

  it('requires unavailable and collapsed metric previews to expose semantic layout evidence', async () => {
    const fuel = (await reviewServer.getJson('/api/overlay-model/fuel-calculator?preview=race&fixture=fuel-calculating')).model;
    const pitService = (await reviewServer.getJson('/api/overlay-model/pit-service?preview=race')).model;

    expect.soft(
      fuel.effectiveSettings?.rendered?.fuelStrategy,
      'fuel-calculator: fuel strategy availability evidence'
    ).toMatchObject({
      additionalFuelNeedState: expect.stringMatching(/^(measured|unavailable|not-needed)$/),
      successCopyRequiresMeasuredNeed: true
    });
    expect.soft(
      metricText(fuel),
      'fuel-calculator: null/unavailable need must not render as success copy'
    ).not.toMatch(/\bNeed\s+Covered\b|\bCovered\b/i);

    for (const [overlayId, model] of [['fuel-calculator', fuel], ['pit-service', pitService]]) {
      expect.soft(model.effectiveSettings?.rendered?.layout, `${overlayId}: rendered layout density evidence`).toMatchObject({
        contentRowCount: expect.any(Number),
        unusedHeightRatio: expect.any(Number)
      });
      expect.soft(
        Number.isFinite(model.effectiveSettings?.rendered?.layout?.unusedHeightRatio)
          ? model.effectiveSettings.rendered.layout.unusedHeightRatio
          : Number.POSITIVE_INFINITY,
        `${overlayId}: collapsed content shell leaves too much empty vertical space`
      ).toBeLessThanOrEqual(0.35);
    }
  });

  it('requires overlay-specific provenance for fallback and synthetic states', async () => {
    const defaultInput = (await reviewServer.getJson('/api/overlay-model/input-state?preview=race')).model;
    expect.soft(defaultInput.inputs?.isAvailable, 'input-state: default preview inputs should be available').toBe(true);
    expect.soft(defaultInput.inputs?.tracePointCount ?? 0, 'input-state: default preview trace evidence').toBeGreaterThan(0);

    const input = (await reviewServer.getJson('/api/overlay-model/input-state?preview=race&fixture=input-state-mock-data')).model;
    expect.soft(input.inputs?.isAvailable, 'input-state: mock-data fixture inputs should be available').toBe(true);
    expect.soft(input.inputs?.tracePointCount ?? 0, 'input-state: mock-data fixture trace evidence').toBe(180);
    expect.soft(
      input.effectiveSettings?.rendered?.provenance?.syntheticStateKind,
      'input-state: mock-data fixture provenance'
    ).toBe('input-state-mock-data');

    for (const source of [
      'src/TmrOverlay.App/Overlays/BrowserSources/BrowserOverlayModelFactory.cs',
      'src/TmrOverlay.App/Overlays/InputState/InputStateRenderModel.cs',
      'src/TmrOverlay.App/Overlays/BrowserSources/Assets/modules/input-state.js'
    ]) {
      expect.soft(
        readFileSync(resolve(repoRoot, source), 'utf8'),
        `input-state: mock fixture must not leak into production source ${source}`
      ).not.toContain('input-state-mock-data');
    }

    const trackMap = (await reviewServer.getJson('/api/overlay-model/track-map?preview=race')).model;
    const mapKind = trackMap.trackMap?.mapKind || trackMap.trackMap?.renderModel?.mapKind;
    if (mapKind === 'circle') {
      expect.soft(trackMap.status, 'track-map: circle fallback must not be labelled live').not.toMatch(/\blive\b/i);
      expect.soft(trackMap.effectiveSettings?.rendered?.mapFallback, 'track-map: circle fallback provenance').toMatchObject({
        kind: 'circle',
        reason: expect.any(String),
        currentTrackKey: expect.any(String)
      });
    }

    for (const [overlayId, fixture] of [
      ['input-state', 'input-state-mock-data'],
      ['car-radar', 'car-radar-right'],
      ['flags', 'flags-all-kinds'],
      ['stream-chat', 'stream-chat-twitch-rich']
    ]) {
      const model = (await reviewServer.getJson(`/api/overlay-model/${overlayId}?preview=race&fixture=${fixture}`)).model;
      expect.soft(model.effectiveSettings?.rendered?.provenance?.syntheticStateKind, `${overlayId}: synthetic state classification`).toEqual(expect.any(String));
    }
  });

  it('requires unavailable previews to suppress stale-looking rendered content', async () => {
    const gap = (await reviewServer.getJson('/api/overlay-model/gap-to-leader?preview=race&fixture=gap-no-cars')).model;
    const input = (await reviewServer.getJson('/api/overlay-model/input-state?preview=race&fixture=input-waiting')).model;

    for (const [overlayId, model] of [['gap-to-leader', gap], ['input-state', input]]) {
      expect.soft(model.status, `${overlayId}: fixture should be an unavailable/waiting/hidden state`).toMatch(/waiting|unavailable|hidden/i);
      expect.soft(
        hasRenderedContent(model),
        `${overlayId}: unavailable previews must not carry stale graph/table/input content without explicit stale-history provenance`
      ).toBe(false);
      expect.soft(
        model.effectiveSettings?.rendered?.unavailableContentPolicy,
        `${overlayId}: unavailable content policy evidence`
      ).toBe('suppress-rendered-content');
    }
  });
});

function renderedTitle(model) {
  return String(model?.renderedTitle ?? model?.title ?? '').trim();
}

function docMentionsSurface(markdown, surface) {
  const normalizedMarkdown = markdown.toLowerCase();
  return String(surface || '')
    .split(/[|/]/)
    .map((term) => term.trim())
    .filter(Boolean)
    .some((term) => normalizedMarkdown.includes(term.toLowerCase()));
}

function normalizedOverlayTitle(overlayId) {
  return overlayId.replace(/-/g, ' ').replace(/\b\w/g, (match) => match.toUpperCase());
}

function allVisibleModelText(model) {
  return [
    model.title,
    model.status,
    model.source,
    ...(model.headerItems || []).flatMap((item) => [item.key, item.value, item.tone]),
    ...(model.columns || []).map((column) => column.label),
    ...(model.rows || []).flatMap((row) => [row.headerTitle, row.headerDetail, ...(row.cells || [])]),
    ...(model.metricSections || []).flatMap((section) => [
      section.title,
      ...(section.rows || []).flatMap((row) => [
        row.label,
        row.value,
        ...(row.segments || []).flatMap((segment) => [segment.label, segment.value])
      ])
    ])
  ].filter(Boolean).join(' ');
}

function rowIdentity(row) {
  const kind = row.isClassHeader ? 'class-header' : row.isPlaceholder ? 'placeholder' : 'row';
  const primary = row.headerTitle || (row.cells || []).slice(0, 2).join('/');
  return [
    kind,
    primary,
    visibleRowDetail(row),
    row.isReference ? 'reference' : ''
  ].join('|');
}

function visibleRowDetail(row) {
  const detail = row?.headerDetail || '';
  return row?.isClassHeader ? String(detail).toUpperCase() : detail;
}

function metricText(model) {
  return [
    ...(model.metrics || []).flatMap((row) => metricRowText(row)),
    ...(model.metricSections || []).flatMap((section) => [
      section.title,
      ...(section.rows || []).flatMap((row) => metricRowText(row))
    ]),
    ...(model.gridSections || []).flatMap((section) => [
      section.title,
      ...(section.rows || []).flatMap((row) => [
        row.label,
        ...(row.cells || []).map((cell) => typeof cell === 'object' ? cell.value : cell)
      ])
    ])
  ].filter(Boolean).join(' ');
}

function metricRowText(row) {
  return [
    row.label,
    row.value,
    ...(row.segments || []).flatMap((segment) => [segment.label, segment.value])
  ];
}

function hasRenderedContent(model) {
  return Boolean(
    (model.rows || []).length
    || (model.points || []).length
    || (model.graph?.series || []).length
    || (model.graph?.trendMetrics || []).some((metric) => String(metric.valueText || '').trim() && metric.valueText !== '--')
    || model.inputs?.hasGraph
    || model.inputs?.hasRail
    || (model.inputs?.trace || []).length
  );
}
