import fs from 'node:fs';
import path from 'node:path';
import { afterAll, beforeAll, describe, expect, it } from 'vitest';
import { startReviewServer } from './reviewServerTestHost.js';

let reviewServer;
const overlayGeometry = JSON.parse(fs.readFileSync(
  path.join(process.cwd(), 'src/TmrOverlay.App/Overlays/BrowserSources/Assets/contracts/overlay-geometry.json'),
  'utf8'
));

beforeAll(async () => {
  reviewServer = await startReviewServer();
}, 10000);

afterAll(async () => {
  await reviewServer?.stop();
});

describe('browser review server validation contracts', () => {
  it('exposes effective settings evidence beside model output', async () => {
    await reviewServer.postReviewPatch({
      kind: 'number',
      overlayId: 'relative',
      key: 'carsEachSide',
      value: 2
    });
    await reviewServer.postReviewPatch({
      kind: 'content',
      overlayId: 'relative',
      key: 'relative.content.relative.pit.enabled',
      label: 'Pit status',
      session: 'Race',
      enabled: false
    });
    await reviewServer.postReviewPatch({
      kind: 'chrome',
      overlayId: 'relative',
      area: 'header',
      label: 'Time remaining',
      session: 'Race',
      enabled: false
    });

    const settings = await reviewServer.getJson('/api/relative?preview=race');
    const model = (await reviewServer.getJson('/api/overlay-model/relative?preview=race')).model;

    expect.soft(settings.relativeSettings.reviewOverlayState.carsEachSide).toBe(2);
    expect.soft(settings.relativeSettings.reviewOverlayState.content['Pit status.race']).toBe(false);
    expect.soft(model.rows).toHaveLength(5);
    expect.soft(relativeRowKinds(model)).toEqual(['placeholder', 'car', 'reference', 'car', 'placeholder']);
    expect.soft(model.effectiveSettings.rendered.placeholderRowCount).toBe(2);
    expect.soft(model.effectiveSettings.rendered.rowIdentities).toEqual([
      'placeholder|/||',
      'row|3/#34 Near Ahead||',
      'row|5/#55 Focus Driver||reference',
      'row|6/#61 Near Behind||',
      'placeholder|/||'
    ]);

    expect.soft(model.effectiveSettings).toMatchObject({
      overlayId: 'relative',
      previewMode: 'race',
      sources: {
        browserReview: { applied: true },
        localhostObs: { applied: true },
        windowsNative: { applied: true }
      },
      rendered: {
        rowCount: 5,
        headerItems: [],
        browserSource: {
          baseWidth: 392,
          baseHeight: 212,
          width: 392,
          height: 212,
          scalePercent: 100,
          opacityPercent: 100
        }
      },
      settings: expect.arrayContaining([
        expect.objectContaining({ key: 'carsEachSide', value: 2 }),
        expect.objectContaining({ key: 'scalePercent', value: 100 }),
        expect.objectContaining({ key: 'relative.content.relative.pit.enabled', session: 'race', value: false }),
        expect.objectContaining({ key: 'chrome.header.time-remaining.race', value: false })
      ])
    });
  });

  it('exposes header item tone evidence for localhost model chrome', async () => {
    const model = (await reviewServer.getJson('/api/overlay-model/fuel-calculator?preview=race')).model;

    expect.soft(model.headerItems).toEqual(expect.arrayContaining([
      expect.objectContaining({ key: 'timeRemaining', tone: 'normal' })
    ]));
    expect.soft(model.effectiveSettings.rendered.headerItems).toEqual(expect.arrayContaining([
      expect.objectContaining({ key: 'timeRemaining', tone: 'normal' })
    ]));
  });

  it('exposes fuel calculating and content-off review fixtures', async () => {
    const calculating = (await reviewServer.getJson('/api/overlay-model/fuel-calculator?preview=race&fixture=fuel-calculating')).model;
    expect.soft(calculating.status).toBe('calculating strategy');
    expect.soft(metricSectionTitles(calculating)).toEqual(['Race Information']);
    expect.soft(metricRowLabels(calculating, 'Race Information')).toEqual(['Plan', 'Fuel']);
    expect.soft(allMetricText(calculating)).toMatch(/\bCalculating\b/);
    expect.soft(allMetricText(calculating)).not.toMatch(/\bCovered\b|\bNone\b/);
    expect.soft(calculating.effectiveSettings.rendered.fuelStrategy).toMatchObject({
      additionalFuelNeedState: 'unavailable',
      successCopyRequiresMeasuredNeed: true
    });
    expect.soft(fuelRenderedRowCount(calculating)).toBe(2);
    expect.soft(calculating.effectiveSettings.rendered.layout).toMatchObject({
      contentRowCount: 4,
      unusedHeightRatio: 0
    });
    expect.soft(calculating.effectiveSettings.rendered.browserSource).toMatchObject({
      baseWidth: 503,
      baseHeight: expectedFuelContentHeight(2, 1),
      width: 503,
      height: expectedFuelContentHeight(2, 1),
      scalePercent: 100
    });

    const stintsOff = (await reviewServer.getJson('/api/overlay-model/fuel-calculator?preview=race&fixture=fuel-stint-targets-off')).model;
    expect.soft(metricSectionTitles(stintsOff)).toEqual(['Race Information']);
    expect.soft(metricRowLabels(stintsOff, 'Race Information')).toEqual(['Plan', 'Fuel']);
    expect.soft(allMetricText(stintsOff)).not.toMatch(/\bStint Targets\b|\bStint 1\b/);
    expect.soft(fuelRenderedRowCount(stintsOff)).toBe(2);
    expect.soft(stintsOff.effectiveSettings.rendered.browserSource.baseHeight).toBe(expectedFuelContentHeight(2, 1));
    expect.soft(stintsOff.effectiveSettings.rendered.browserSource.height).toBe(expectedFuelContentHeight(2, 1));

    const raceInfoOff = (await reviewServer.getJson('/api/overlay-model/fuel-calculator?preview=race&fixture=fuel-race-information-off')).model;
    expect.soft(metricSectionTitles(raceInfoOff)).toEqual(['Stint Targets']);
    expect.soft(metricRowLabels(raceInfoOff, 'Stint Targets')).toEqual(['Stint 1', 'Stint 2', 'Stint 3']);
    expect.soft(allMetricText(raceInfoOff)).not.toMatch(/\bRace Information\b|\bPlan\b|\bFuel\b/);
    expect.soft(fuelRenderedRowCount(raceInfoOff)).toBe(3);
    expect.soft(raceInfoOff.effectiveSettings.rendered.browserSource.baseHeight).toBe(expectedFuelContentHeight(3, 1));
    expect.soft(raceInfoOff.effectiveSettings.rendered.browserSource.height).toBe(expectedFuelContentHeight(3, 1));
  });

  it('proves v1.0.2 non-race display contracts in practice and qualifying previews', async () => {
    for (const preview of ['practice', 'qualifying']) {
      const standings = (await reviewServer.getJson(`/api/overlay-model/standings?preview=${preview}`)).model;
      const standingsColumns = columnLabels(standings);
      const standingsText = tableText(standings);

      expect.soft(
        standingsColumns,
        `V102-004 ${preview}: non-race Standings must not expose race GAP header`
      ).not.toContain('GAP');
      expect.soft(
        standingsColumns,
        `V102-004 ${preview}: non-race Standings must not expose race INT header`
      ).not.toContain('INT');
      expect.soft(
        standingsText,
        `V102-004 ${preview}: non-race Standings must not render race leader gap wording`
      ).not.toMatch(/\bLeader\b/);

      const relative = (await reviewServer.getJson(`/api/overlay-model/relative?preview=${preview}`)).model;
      const relativeColumns = columnLabels(relative);
      const relativeText = tableText(relative);
      if (preview === 'practice') {
        expect.soft(relativeColumns, 'Relative practice should match race timing column semantics').toContain('Delta');
        expect.soft(
          (relative.rows || []).map((row) => row.relativeLapDelta).filter((value) => value !== null && value !== undefined),
          'Relative practice should preserve whole-lap relationship evidence like race'
        ).toEqual([1, 0, -2]);
      } else {
        expect.soft(relative.shouldRender, 'Qualifying Relative should not render because it lacks useful proximity semantics').toBe(false);
        expect.soft(relative.status).toContain('qualifying unsupported');
        expect.soft(relativeColumns, 'Qualifying Relative should not expose table columns').toEqual([]);
        expect.soft(relative.rows || [], 'Qualifying Relative should not expose stale rows').toEqual([]);
        expect.soft(relative.headerItems || [], 'Qualifying Relative should not render header-only chrome').toEqual([]);
        expect.soft(
          relativeText,
          `V102-005 ${preview}: qualifying Relative must not render F2/estimated timing as physical proximity without evidence`
        ).not.toMatch(/[+-]?\d+(?:\.\d+)?s\b/);
        expect.soft(relative.effectiveSettings?.rendered?.shouldRender).toBe(false);
        expect.soft(relative.effectiveSettings?.rendered?.rowCount).toBe(0);
        expect.soft(relative.effectiveSettings?.rendered?.columnKeys).toEqual([]);
        expect.soft(
          relative.effectiveSettings?.rendered?.relativeTimingEvidence,
          `V102-005 ${preview}: qualifying Relative needs source evidence for any displayed timing/proximity column`
        ).toMatchObject({
          sessionKind: preview,
          physicalProximityAvailable: false
        });
      }

      const fuel = (await reviewServer.getJson(`/api/overlay-model/fuel-calculator?preview=${preview}`)).model;
      expect.soft(
        metricSectionTitles(fuel),
        `V102-006 ${preview}: non-race Fuel must use range/usage sections, not race strategy sections`
      ).toEqual(['Fuel Range', 'Fuel Usage']);
      expect.soft(
        allMetricText(fuel),
        `V102-006 ${preview}: non-race Fuel must not expose race plan/stint language`
      ).not.toMatch(/\bRace Information\b|\bStint Targets\b|\bstops?\b/i);

      const sessionWeather = (await reviewServer.getJson(`/api/overlay-model/session-weather?preview=${preview}`)).model;
      expect.soft(
        metricRowLabels(sessionWeather, 'Session'),
        `V102-007 ${preview}: Session/Weather must not show estimated race lap rows outside race sessions`
      ).not.toContain('Laps');
      expect.soft(
        allMetricText(sessionWeather),
        `V102-007 ${preview}: Session/Weather must not render estimated lap text outside race sessions`
      ).not.toMatch(/\b\d+(?:\.\d+)?\s+est\b/i);

      const pitService = (await reviewServer.getJson(`/api/overlay-model/pit-service?preview=${preview}`)).model;
      expect.soft(
        metricRowLabels(pitService, 'Session'),
        `V102-009 ${preview}: Pit Service must not show race lap context outside race sessions`
      ).not.toContain('Time / Laps');
      expect.soft(
        allMetricText(pitService),
        `V102-009 ${preview}: Pit Service must not render race lap counters outside race sessions`
      ).not.toMatch(/\b\d+\s*\/\s*\d+\s*laps\b/i);
    }
  });

  it('proves v1.0.2 row parity and fastest-lap precedence contracts in race previews', async () => {
    const standings = (await reviewServer.getJson('/api/overlay-model/standings?preview=race')).model;
    expect.soft(
      standings.rows || [],
      'V102-016 Standings rowCount must describe the actual deterministic rendered row set'
    ).toHaveLength(standings.effectiveSettings?.rendered?.rowCount);

    const rowWithFastestLast = (standings.rows || []).find((row) => {
      const fastest = row.cells?.[5];
      const last = row.cells?.[6];
      return fastest && fastest === last;
    });
    expect.soft(rowWithFastestLast, 'V102-023 fixture must include a row where last lap is also fastest lap').toBeTruthy();
    expect.soft(
      rowWithFastestLast?.cellTones?.[5],
      'V102-023 FAST cell must use fastest-lap tone'
    ).toBe('best-lap');
    expect.soft(
      rowWithFastestLast?.cellTones?.[6],
      'V102-023 LAST cell must preserve fastest-lap precedence over personal-best green'
    ).toBe('best-lap');

    const relative = (await reviewServer.getJson('/api/overlay-model/relative?preview=race')).model;
    expect.soft(
      relative.rows || [],
      'V102-014/V102-016 Relative rowCount must describe the actual deterministic rendered row set'
    ).toHaveLength(relative.effectiveSettings?.rendered?.rowCount);
    expect.soft(
      (relative.rows || []).filter((row) => row.isReference),
      'V102-014/V102-016 Relative must expose exactly one reference row across browser and localhost contracts'
    ).toHaveLength(1);
  });

  it('exposes deterministic minimum-scale fixtures for wide table and input overlays', async () => {
    const standings = (await reviewServer.getJson('/api/overlay-model/standings?preview=race&fixture=standings-min-scale')).model;
    expect.soft(columnLabels(standings)).toEqual(['Pos', 'CAR', 'Driver', 'GAP', 'INT', 'FAST', 'LAST', 'PIT']);
    expect.soft(standings.rows || []).toHaveLength(6);
    expect.soft(tableText(standings)).toContain('Tech Mates Racing');
    expect.soft(tableText(standings)).toContain('IN');
    expect.soft(standings.effectiveSettings.rendered.browserSource).toMatchObject({
      baseWidth: 677,
      baseHeight: 313,
      width: 406,
      height: 188,
      scale: 0.6,
      scalePercent: 60
    });
    expect.soft(standings.effectiveSettings.settings).toContainEqual(expect.objectContaining({
      key: 'scalePercent',
      value: 60
    }));

    const input = (await reviewServer.getJson('/api/overlay-model/input-state?preview=race&fixture=input-min-scale')).model;
    expect.soft(input.bodyKind).toBe('inputs');
    expect.soft(input.inputs).toMatchObject({ hasGraph: true, hasRail: true, hasContent: true });
    expect.soft(input.effectiveSettings.rendered.browserSource).toMatchObject({
      baseWidth: 520,
      baseHeight: 260,
      width: 312,
      height: 156,
      scale: 0.6,
      scalePercent: 60
    });
  });

  it('distinguishes standings all-chrome-off no-content from chrome-only no-content', async () => {
    const hidden = (await reviewServer.getJson('/api/overlay-model/standings?preview=race&fixture=standings-no-content')).model;
    expect.soft(hidden.shouldRender).toBe(false);
    expect.soft(hidden.status).toBe('hidden | no enabled content');
    expect.soft(hidden.columns || []).toEqual([]);
    expect.soft(hidden.rows || []).toEqual([]);
    expect.soft(hidden.headerItems || []).toEqual([]);
    expect.soft(hidden.effectiveSettings.settings).toContainEqual(expect.objectContaining({
      key: 'chrome.header.time-remaining.race',
      value: false
    }));

    const chromeOnly = (await reviewServer.getJson('/api/overlay-model/standings?preview=race&fixture=standings-content-off-chrome-on')).model;
    expect.soft(chromeOnly.shouldRender).toBe(true);
    expect.soft(chromeOnly.status).toBe('chrome only | content disabled');
    expect.soft(chromeOnly.columns || []).toEqual([]);
    expect.soft(chromeOnly.rows || []).toEqual([]);
    expect.soft(chromeOnly.headerItems || []).toEqual([
      expect.objectContaining({ key: 'timeRemaining', value: '06:37:08' })
    ]);
    expect.soft(chromeOnly.effectiveSettings.rendered).toMatchObject({
      shouldRender: true,
      rowCount: 0,
      browserSource: expect.objectContaining({
        baseHeight: 40,
        height: 40
      })
    });
    expect.soft(chromeOnly.effectiveSettings.settings).toContainEqual(expect.objectContaining({
      key: 'chrome.header.time-remaining.race',
      value: true
    }));

    const noResultsChrome = (await reviewServer.getJson('/api/overlay-model/standings?preview=race&fixture=standings-no-results-chrome-on')).model;
    expect.soft(noResultsChrome.shouldRender).toBe(true);
    expect.soft(noResultsChrome.status).toBe('waiting for standings');
    expect.soft(noResultsChrome.columns || []).toEqual([]);
    expect.soft(noResultsChrome.rows || []).toEqual([]);
    expect.soft(noResultsChrome.headerItems || []).toEqual([
      expect.objectContaining({ key: 'timeRemaining', value: '06:37:08' })
    ]);
    expect.soft(noResultsChrome.effectiveSettings.rendered).toMatchObject({
      shouldRender: true,
      rowCount: 0,
      browserSource: expect.objectContaining({
        baseHeight: 40,
        height: 40
      }),
      unavailableContentPolicy: 'chrome-only-placeholder'
    });
  });

  it('keeps Session Weather missing data distinct from user-disabled weather content', async () => {
    const missing = (await reviewServer.getJson('/api/overlay-model/session-weather?preview=race&fixture=session-weather-missing')).model;
    const weatherOff = (await reviewServer.getJson('/api/overlay-model/session-weather?preview=race&fixture=session-weather-weather-off')).model;

    expect.soft(missing.status).toBe('weather unavailable');
    expect.soft(metricSectionTitles(missing)).toEqual(['Session', 'Weather']);
    expect.soft(metricRowLabels(missing, 'Session')).toEqual(['Session', 'Clock', 'Event', 'Track', 'Laps']);
    expect.soft(metricRowLabels(missing, 'Weather')).toEqual(['Surface', 'Sky', 'Wind', 'Temps', 'Atmosphere']);
    expect.soft(missing.source).toMatch(/weather source unavailable/i);
    expect.soft(missing.effectiveSettings.rendered.unavailableContentPolicy).toBe('section-aware-placeholders');
    expect.soft(missing.effectiveSettings.rendered.browserSource.baseHeight).toBe(496);

    const weatherRows = (missing.metricSections || []).find((section) => section.title === 'Weather')?.rows || [];
    expect.soft(weatherRows).toHaveLength(5);
    for (const row of weatherRows) {
      expect.soft(row.segments?.map((segment) => segment.value), `${row.label}: missing weather placeholders`).toEqual(
        row.segments?.map(() => '--')
      );
      expect.soft(['waiting', 'unavailable'], `${row.label}: missing weather tone`).toContain(row.tone);
    }

    expect.soft(missing.effectiveSettings.settings).toEqual(expect.arrayContaining([
      expect.objectContaining({ key: 'session-weather.surface.wetness.enabled', value: true }),
      expect.objectContaining({ key: 'session-weather.sky.weather.enabled', value: true }),
      expect.objectContaining({ key: 'session-weather.wind.speed.enabled', value: true }),
      expect.objectContaining({ key: 'session-weather.temps.track.enabled', value: true }),
      expect.objectContaining({ key: 'session-weather.atmosphere.pressure.enabled', value: true })
    ]));

    expect.soft(metricSectionTitles(weatherOff)).toEqual(['Session']);
    expect.soft(weatherOff.effectiveSettings.settings).toEqual(expect.arrayContaining([
      expect.objectContaining({ key: 'session-weather.sky.weather.enabled', value: false })
    ]));
  });

  it('proves v1.0.2 Gap To Leader trend, threat, color, and focus-window evidence', async () => {
    const model = (await reviewServer.getJson('/api/overlay-model/gap-to-leader?preview=race')).model;
    const graph = model.graph || {};
    const metricsByLabel = new Map((graph.trendMetrics || []).map((metric) => [String(metric.label || '').toUpperCase(), metric]));
    expect.soft(
      (graph.trendMetrics || []).map((metric) => metric?.label),
      'Gap trend rows should put Last above 5L so the latest lap delta is the first trend signal'
    ).toEqual(['Last', '5L', '10L', 'Pit', 'PLap', 'Stint', 'Tire', 'Status']);

    const last = metricsByLabel.get('LAST');
    expect.soft(last?.comparisonText, 'Gap Last comparison should be a signed last-lap delta, not a raw lap time').toMatch(/^[+-]\d+(?:\.\d+)?$/);
    expect.soft(last?.threatText, 'Gap Last threat should be a signed last-lap delta, not a raw lap time').toMatch(/^[+-]\d+(?:\.\d+)?$/);
    expect.soft(`${last?.comparisonText || ''} ${last?.threatText || ''}`, 'Gap Last cells should not expose m:ss raw last-lap values').not.toMatch(/\d+:\d{2}\.\d{3}/);

    for (const label of ['5L', '10L']) {
      const metric = metricsByLabel.get(label);
      expect.soft(metric, `V102-018 Gap ${label} metric missing`).toBeTruthy();
      expect.soft(
        metric?.focusGapChangeSeconds,
        `V102-018 Gap ${label} must expose time-delta semantics`
      ).toEqual(expect.any(Number));
      expect.soft(
        metric?.completedReferenceLaps ?? -1,
        `V102-018 Gap ${label} must prove completed reference-lap readiness before showing a value`
      ).toBeGreaterThanOrEqual(label === '5L' ? 5 : 10);
      expect.soft(
        [metric?.stateLabel, metric?.valueText, metric?.chaserText].filter(Boolean).join(' '),
        `V102-018 Gap ${label} must not expose lap-count text as a trend value`
      ).not.toMatch(/[+-]?\d+(?:\.\d+)?L\b/i);
    }

    expect.soft(graph.scale?.isFocusRelative, 'V102-017/V102-021 Gap scale must be focus-relative').toBe(true);
    expect.soft(
      Number.isFinite(graph.scale?.maxGapSeconds) ? graph.scale.maxGapSeconds : Number.POSITIVE_INFINITY,
      'V102-017/V102-021 Gap focus-window scale should stay bounded'
    ).toBeLessThanOrEqual(30);
    expect.soft(graph.comparisonLabel, 'V102-024 Gap Last comparison must not point at class leader for a P24 reference').not.toMatch(/^(P1|Leader)$/i);
    expect.soft(graph.activeThreat?.chaser?.label, 'V102-025 Gap threat label must use position').toMatch(/^P\d+$/);

    const threatCarIdx = graph.threatCarIdx;
    const series = graph.series || [];
    expect.soft(series.length, 'V102-026 Gap color exclusivity needs rendered series evidence').toBeGreaterThan(0);
    for (const item of series) {
      const renderedColor = item.renderedColor || item.baseColor || '';
      expect.soft(
        renderedColor.length,
        `V102-026 Gap series ${item.carIdx ?? '?'} missing color evidence`
      ).toBeGreaterThan(0);
      if (item.carIdx !== threatCarIdx) {
        expect.soft(
          renderedColor,
          `V102-026 Gap non-threat series ${item.carIdx ?? '?'} must not use red`
        ).not.toMatch(/#(?:ff|ec|e[0-9a-f])[0-9a-f]{4}|rgb\(\s*(?:18\d|19\d|2[0-5]\d)\s*,\s*(?:[0-9]|[1-9]\d|1[0-2]\d)\s*,/i);
      }
    }
  });

  it('carries Stream Chat opacity into settings, model, and browser-source evidence', async () => {
    await reviewServer.postReviewPatch({
      kind: 'number',
      overlayId: 'stream-chat',
      key: 'opacityPercent',
      value: 70
    });

    const settings = await reviewSettingsConfig('race');
    const streamChatSettings = settings.overlays.find((overlay) => overlay.id === 'stream-chat');
    const model = (await reviewServer.getJson('/api/overlay-model/stream-chat?preview=race&fixture=stream-chat-twitch-rich')).model;

    expect.soft(streamChatSettings?.opacityPercent, 'V102-050 settings must expose Stream Chat opacity').toBe(70);
    expect.soft(model.rootOpacity, 'V102-050 Stream Chat model root opacity').toBeCloseTo(0.7, 5);
    expect.soft(model.effectiveSettings?.rendered?.browserSource, 'V102-050 Stream Chat browser-source opacity evidence').toMatchObject({
      opacity: 0.7,
      opacityPercent: 70
    });
    expect.soft(model.effectiveSettings?.settings, 'V102-050 Stream Chat effective settings opacity entry').toContainEqual(expect.objectContaining({
      key: 'opacityPercent',
      value: 70
    }));
  });

  it('keeps ordinary previews settings-faithful and moves rightmost proof into an explicit fixture', async () => {
    await reviewServer.postReviewPatch({
      kind: 'number',
      overlayId: 'relative',
      key: 'carsEachSide',
      value: 3
    });
    await reviewServer.postReviewPatch({
      kind: 'chrome',
      overlayId: 'relative',
      area: 'header',
      label: 'Time remaining',
      session: 'Race',
      enabled: true
    });
    await reviewServer.postReviewPatch({
      kind: 'content',
      overlayId: 'relative',
      key: 'relative.content.relative.pit.enabled',
      label: 'Pit status',
      session: 'Race',
      enabled: false
    });

    const normal = (await reviewServer.getJson('/api/overlay-model/relative?preview=race')).model;
    const fixture = (await reviewServer.getJson('/api/overlay-model/relative?preview=race&fixture=rightmost-evidence')).model;

    expect.soft((normal.columns || []).map((column) => column.dataKey)).not.toContain('pit');
    expect.soft(normal.effectiveSettings.settings).toContainEqual(expect.objectContaining({
      key: 'relative.content.relative.pit.enabled',
      session: 'race',
      value: false
    }));

    expect.soft((fixture.columns || []).map((column) => column.dataKey)).toContain('pit');
    expect.soft(fixture.effectiveSettings.sources.browserReview.fixtureVariant).toBe('rightmost-evidence');
    expect.soft(fixture.effectiveSettings.rendered.browserSource).toMatchObject({
      baseWidth: 440,
      baseHeight: 308,
      width: 440,
      height: 308
    });
    expect.soft(fixture.effectiveSettings.settings).toContainEqual(expect.objectContaining({
      key: 'relative.content.relative.pit.enabled',
      session: 'race',
      value: true
    }));
  });

  it('exposes Relative column-off and no-content fixtures for screenshot validation', async () => {
    await reviewServer.postReviewPatch({
      kind: 'number',
      overlayId: 'relative',
      key: 'carsEachSide',
      value: 3
    });
    await reviewServer.postReviewPatch({
      kind: 'chrome',
      overlayId: 'relative',
      area: 'header',
      label: 'Time remaining',
      session: 'Race',
      enabled: true
    });

    const driverOnly = (await reviewServer.getJson('/api/overlay-model/relative?preview=race&fixture=relative-driver-only')).model;
    expect.soft((driverOnly.columns || []).map((column) => column.dataKey)).toEqual(['driver']);
    expect.soft(driverOnly.rows || []).toHaveLength(7);
    expect.soft((driverOnly.rows || []).find((row) => row.isReference)?.cells).toEqual(['#55 Focus Driver']);
    expect.soft(driverOnly.effectiveSettings.rendered.browserSource).toMatchObject({
      baseWidth: 274,
      baseHeight: 308
    });

    const positionDriver = (await reviewServer.getJson('/api/overlay-model/relative?preview=race&fixture=relative-position-driver')).model;
    expect.soft((positionDriver.columns || []).map((column) => column.dataKey)).toEqual(['relative-position', 'driver']);
    expect.soft((positionDriver.rows || []).find((row) => row.isReference)?.cells).toEqual(['5', '#55 Focus Driver']);
    expect.soft(positionDriver.effectiveSettings.rendered.browserSource).toMatchObject({
      baseWidth: 322,
      baseHeight: 308
    });

    const noContent = (await reviewServer.getJson('/api/overlay-model/relative?preview=race&fixture=relative-no-content')).model;
    expect.soft(noContent.shouldRender).toBe(false);
    expect.soft(noContent.status).toContain('no enabled content');
    expect.soft(noContent.columns || []).toEqual([]);
    expect.soft(noContent.rows || []).toEqual([]);
    expect.soft(noContent.headerItems || []).toEqual([]);
    expect.soft(noContent.effectiveSettings.settings).toContainEqual(expect.objectContaining({
      key: 'chrome.header.time-remaining.race',
      session: 'race',
      value: false
    }));
    for (const key of [
      'relative.content.relative.position.enabled',
      'relative.content.relative.driver.enabled',
      'relative.content.relative.gap.enabled',
      'relative.content.relative.pit.enabled'
    ]) {
      expect.soft(noContent.effectiveSettings.settings).toContainEqual(expect.objectContaining({
        key,
        session: 'race',
        value: false
      }));
    }
  });

  it('does not make global session preview force Garage Cover preview visibility', async () => {
    await reviewServer.postReviewPatch({
      kind: 'garageCover',
      overlayId: 'garage-cover',
      action: 'clear'
    });
    await reviewServer.postReviewPatch({
      kind: 'garageCover',
      overlayId: 'garage-cover',
      action: 'import'
    });

    const model = (await reviewServer.getJson('/api/overlay-model/garage-cover?preview=race')).model;

    expect.soft(model.shouldRender).toBe(false);
    expect.soft(model.garageCover?.shouldCover).toBe(false);
    expect.soft(model.garageCover?.browserSettings?.previewVisible).toBe(false);
    expect.soft(model.effectiveSettings.rendered.shouldRender).toBe(false);
    expect.soft(model.effectiveSettings.settings).toContainEqual(expect.objectContaining({
      key: 'garage-cover.previewVisible',
      value: false
    }));
  });

  it('hides localhost OBS models when product visibility is disabled', async () => {
    await reviewServer.postReviewPatch({
      kind: 'overlayEnabled',
      overlayId: 'standings',
      enabled: false
    });
    await reviewServer.postReviewPatch({
      kind: 'session',
      overlayId: 'standings',
      session: 'Race',
      enabled: false
    });

    const model = (await reviewServer.getJson('/api/overlay-model/standings?preview=race')).model;

    expect.soft(model.shouldRender).toBe(false);
    expect.soft(model.rows ?? []).toEqual([]);
    expect.soft(model.status).toMatch(/hidden|disabled/i);
    expect.soft(model.effectiveSettings).toMatchObject({
      overlayId: 'standings',
      previewMode: 'race',
      sources: {
        browserReview: { applied: true },
        localhostObs: { applied: true },
        windowsNative: { applied: true }
      },
      settings: expect.arrayContaining([
        expect.objectContaining({ key: 'overlayEnabled', value: false }),
        expect.objectContaining({ key: 'session.race.enabled', value: false })
      ])
    });
  });

  it('serves bounded gap-to-leader models under concurrent localhost polling', async () => {
    await reviewServer.postReviewPatch({
      kind: 'overlayEnabled',
      overlayId: 'gap-to-leader',
      enabled: true
    });
    await reviewServer.postReviewPatch({
      kind: 'session',
      overlayId: 'gap-to-leader',
      session: 'Race',
      enabled: true
    });

    const startedAt = performance.now();
    const responses = await Promise.all(Array.from({ length: 32 }, () =>
      reviewServer.getJson('/api/overlay-model/gap-to-leader?preview=race')
    ));
    const elapsedMs = performance.now() - startedAt;

    expect.soft(elapsedMs).toBeLessThan(2000);
    for (const response of responses) {
      const model = response.model;
      const encodedBytes = Buffer.byteLength(JSON.stringify(model), 'utf8');
      const seriesPointCount = (model.graph?.series || [])
        .reduce((count, series) => count + (series.points || []).length, 0);

      expect.soft(model.overlayId).toBe('gap-to-leader');
      expect.soft(model.effectiveSettings).toMatchObject({
        overlayId: 'gap-to-leader',
        previewMode: 'race'
      });
      expect.soft(encodedBytes).toBeLessThan(250_000);
      expect.soft(seriesPointCount).toBeLessThanOrEqual(2_000);
    }
  });

  it('hides every exposed localhost OBS model when product visibility is disabled', async () => {
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
      await reviewServer.postReviewPatch({
        kind: 'overlayEnabled',
        overlayId,
        enabled: false
      });
      await reviewServer.postReviewPatch({
        kind: 'session',
        overlayId,
        session: 'Race',
        enabled: false
      });

      const model = (await reviewServer.getJson(`/api/overlay-model/${overlayId}?preview=race`)).model;

      expectHiddenOverlayModel(model, overlayId);
      expect.soft(model.effectiveSettings, `${overlayId}: missing effective-settings evidence`).toMatchObject({
        overlayId,
        previewMode: 'race',
        sources: {
          browserReview: { applied: true },
          localhostObs: { applied: true },
          windowsNative: { applied: true }
        },
        settings: expect.arrayContaining([
          expect.objectContaining({ key: 'overlayEnabled', value: false }),
          expect.objectContaining({ key: 'session.race.enabled', value: false })
        ])
      });
    }
  });

  it('hides content-driven localhost OBS models when every renderable content row is disabled', async () => {
    const config = await reviewSettingsConfig('race');
    const overlayIds = ['standings', 'relative', 'fuel-calculator', 'session-weather', 'pit-service', 'input-state', 'flags'];

    for (const overlayId of overlayIds) {
      const overlay = config.overlays.find((candidate) => candidate.id === overlayId);
      expect.soft(overlay?.contentRows?.length, `${overlayId}: missing content rows`).toBeGreaterThan(0);

      await reviewServer.postReviewPatch({
        kind: 'overlayEnabled',
        overlayId,
        enabled: true
      });
      await reviewServer.postReviewPatch({
        kind: 'session',
        overlayId,
        session: 'Race',
        enabled: true
      });

      for (const row of overlay?.contentRows || []) {
        await reviewServer.postReviewPatch({
          kind: 'content',
          overlayId,
          key: row.key,
          label: row.label,
          session: 'Race',
          enabled: false
        });
      }

      const model = (await reviewServer.getJson(`/api/overlay-model/${overlayId}?preview=race`)).model;

      if (overlayId === 'standings') {
        expect.soft(model.shouldRender, 'standings: chrome-only no-content should render chrome').toBe(true);
        expect.soft(model.status, 'standings: chrome-only status').toBe('chrome only | content disabled');
        expect.soft(model.rows ?? [], 'standings: chrome-only rows').toEqual([]);
        expect.soft(model.headerItems ?? [], 'standings: chrome-only header').toEqual([
          expect.objectContaining({ key: 'timeRemaining', value: '06:37:08' })
        ]);
        expect.soft(model.effectiveSettings.rendered.shouldRender, 'standings: effective rendered chrome-only').toBe(true);
      } else {
        expectHiddenOverlayModel(model, overlayId);
        expect.soft(model.status, `${overlayId}: no-content hidden status`).toMatch(/no enabled content/i);
        expect.soft(model.effectiveSettings.rendered.shouldRender, `${overlayId}: effective rendered hidden`).toBe(false);
      }
    }
  });
});

function expectHiddenOverlayModel(model, overlayId) {
  expect.soft(model.shouldRender, `${overlayId}: product-hidden model should not render`).toBe(false);
  expect.soft(model.rows ?? [], `${overlayId}: product-hidden rows`).toEqual([]);
  expect.soft(model.metrics ?? [], `${overlayId}: product-hidden metrics`).toEqual([]);
  expect.soft(model.points ?? [], `${overlayId}: product-hidden trend points`).toEqual([]);

  if (model.graph) {
    expect.soft(model.graph.series ?? [], `${overlayId}: product-hidden graph series`).toEqual([]);
    expect.soft(model.graph.selectedSeriesCount ?? 0, `${overlayId}: product-hidden graph selection`).toBe(0);
  }

  if (model.carRadar?.renderModel) {
    expect.soft(model.carRadar.renderModel.shouldRender, `${overlayId}: product-hidden radar render model`).toBe(false);
  }

  if (model.trackMap?.renderModel) {
    expect.soft(model.trackMap.renderModel.shouldRender, `${overlayId}: product-hidden track map render model`).toBe(false);
  }

  if (model.garageCover) {
    expect.soft(model.garageCover.shouldCover, `${overlayId}: product-hidden garage cover`).toBe(false);
  }

  if (model.flags) {
    expect.soft(model.flags.flags ?? [], `${overlayId}: product-hidden flags`).toEqual([]);
  }

  if (model.streamChat) {
    expect.soft(model.streamChat.rows ?? [], `${overlayId}: product-hidden stream chat rows`).toEqual([]);
  }
}

function columnLabels(model) {
  return (model.columns || []).map((column) => column.label).filter(Boolean);
}

function tableText(model) {
  return (model.rows || [])
    .flatMap((row) => [
      row.headerTitle,
      row.headerDetail,
      ...(row.cells || [])
    ])
    .filter(Boolean)
    .join(' ');
}

function relativeRowKinds(model) {
  return (model.rows || []).map((row) => {
    if (row.isPlaceholder) {
      return 'placeholder';
    }

    return row.isReference ? 'reference' : 'car';
  });
}

function metricSectionTitles(model) {
  return (model.metricSections || []).map((section) => section.title);
}

function metricRowLabels(model, sectionTitle) {
  return (model.metricSections || [])
    .filter((section) => section.title === sectionTitle)
    .flatMap((section) => section.rows || [])
    .map((row) => row.label);
}

function fuelRenderedRowCount(model) {
  return (model.metricSections || [])
    .filter((section) => (section.rows || []).length > 0)
    .reduce((total, section) => total + (section.rows || []).length, 0);
}

function expectedFuelContentHeight(rowCount, sectionCount) {
  const metricRows = overlayGeometry.metricRows;
  if (rowCount <= 0 || sectionCount <= 0) {
    return metricRows.minimumFuelCalculatorHeight;
  }

  const rowGaps = Math.round(Math.max(0, rowCount - sectionCount) * metricRows.rowGap);
  const sectionGaps = Math.round(Math.max(0, sectionCount - 1) * metricRows.sectionGap);
  const height = metricRows.headerChromeHeight
    + metricRows.fuelContentVerticalPadding
    + sectionCount * metricRows.fuelSectionTitleReserveHeight
    + Math.round(rowCount * metricRows.segmentedRowHeight)
    + rowGaps
    + sectionGaps
    + metricRows.collapsedFooterReserveHeight;
  return Math.max(metricRows.minimumFuelCalculatorHeight, Math.min(height, 298));
}

function allMetricText(model) {
  return (model.metricSections || [])
    .flatMap((section) => [
      section.title,
      ...(section.rows || []).flatMap((row) => [
        row.label,
        row.value,
        ...(row.segments || []).flatMap((segment) => [segment.label, segment.value])
      ])
    ])
    .filter(Boolean)
    .join(' ');
}

async function reviewSettingsConfig(preview = 'race') {
  const html = await reviewServer.getText(`/review/app?preview=${encodeURIComponent(preview)}&tab=general`);
  const match = /<script[^>]+id="settings-app-config"[^>]*>([\s\S]*?)<\/script>/.exec(html);
  if (!match) {
    throw new Error('Missing settings-app-config script in review app HTML');
  }

  return JSON.parse(match[1]);
}
