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

  it('retains the Fuel/Lap V2 cell as isolated populated, degraded, and unavailable workbench states', async () => {
    const populated = (await reviewServer.getJson(
      '/api/overlay-model/fuel-calculator?preview=race&fixture=fuel-laps-workbench-fuel'
    )).model;
    expect.soft(populated.status).toBe('fuel/lap workbench');
    expect.soft(metricSectionTitles(populated)).toEqual(['Fuel/Lap Workbench']);
    expect.soft(metricRowLabels(populated, 'Fuel/Lap Workbench')).toEqual(['Fuel/Lap']);
    expect.soft(metricSegmentDisplay(populated.metricSections[0].rows[0].segments)).toEqual([
      { label: 'Last', value: '13.52 L/lap', tone: 'info' },
      { label: '5L', value: '13.50 L/lap', tone: 'info' },
      { label: '10L', value: '13.36 L/lap', tone: 'info' },
      { label: 'Max', value: '13.65 L/lap', tone: 'info' }
    ]);
    expect.soft(allMetricText(populated)).not.toMatch(/V1 Ref|Fuel Range Workbench|Target Usage|Fuel To Add|Plan V2|Stint Targets V2/);

    const degraded = (await reviewServer.getJson(
      '/api/overlay-model/fuel-calculator?preview=race&fixture=fuel-laps-workbench-fuel-degraded'
    )).model;
    expect.soft(degraded.status).toBe('fuel/lap workbench');
    expect.soft(metricSectionTitles(degraded)).toEqual(['Fuel/Lap Workbench']);
    expect.soft(metricSegmentDisplay(degraded.metricSections[0].rows[0].segments)).toEqual([
      { label: 'Last', value: '13.54 L/lap', tone: 'info' },
      { label: '5L', value: '--', tone: 'waiting' },
      { label: '10L', value: '--', tone: 'waiting' },
      { label: 'Max', value: '13.54 L/lap', tone: 'info' }
    ]);

    const noData = (await reviewServer.getJson(
      '/api/overlay-model/fuel-calculator?preview=race&fixture=fuel-laps-workbench-fuel-no-data'
    )).model;
    expect.soft(noData.status).toBe('fuel/lap workbench');
    expect.soft(noData.shouldRender).toBe(true);
    expect.soft(metricSectionTitles(noData)).toEqual(['Fuel/Lap Workbench']);
    expect.soft(metricRowLabels(noData, 'Fuel/Lap Workbench')).toEqual(['Fuel/Lap']);
    expect.soft(metricSegmentDisplay(noData.metricSections[0].rows[0].segments)).toEqual([
      { label: 'Last', value: '--', tone: 'waiting' },
      { label: '5L', value: '--', tone: 'waiting' },
      { label: '10L', value: '--', tone: 'waiting' },
      { label: 'Max', value: '--', tone: 'waiting' }
    ]);

    const productionNoData = (await reviewServer.getJson(
      '/api/overlay-model/fuel-calculator?preview=race&fixture=fuel-no-data'
    )).model;
    expect.soft(productionNoData.shouldRender).toBe(false);
    expect.soft(metricSectionTitles(productionNoData)).toEqual([]);

    const retainedSibling = (await reviewServer.getJson(
      '/api/overlay-model/fuel-calculator?preview=race&fixture=fuel-laps-workbench-range'
    )).model;
    expect.soft(retainedSibling.status).toBe('fuel/range workbench');
    expect.soft(metricSectionTitles(retainedSibling)).toEqual(['Fuel Range Workbench']);
    expect.soft(metricSectionTitles(retainedSibling)).not.toContain('Fuel/Lap Workbench');
  });

  it('keeps corrected Fuel V2 boundary behavior visible in the workbench mirrors', async () => {
    const laps = (await reviewServer.getJson(
      '/api/overlay-model/fuel-calculator?preview=race&fixture=fuel-laps-workbench-laps'
    )).model;
    expect.soft(metricRow(laps, 'Laps Workbench', 'Dallara 45m / V2')?.segments)
      .toContainEqual({ label: 'Mid S1', value: '6.04', tone: 'success' });
    expect.soft(metricRow(laps, 'Laps Workbench', '24h rejoin / V2')?.segments)
      .toContainEqual({ label: 'Stop 1', value: '166.01 held degraded', tone: 'warning' });

    const range = (await reviewServer.getJson(
      '/api/overlay-model/fuel-calculator?preview=race&fixture=fuel-laps-workbench-range'
    )).model;
    expect.soft(metricSegmentDisplay(metricRow(range, 'Fuel Range Workbench', 'Stress / Known zero fuel')?.segments)).toEqual([
      { label: 'Fuel', value: '0.0 L', tone: 'info' },
      { label: 'V1 Ref', value: '--', tone: 'waiting' },
      { label: 'Last', value: '0.00', tone: 'info' },
      { label: '5L', value: '0.00', tone: 'info' },
      { label: '10L', value: '0.00', tone: 'info' },
      { label: 'Max', value: '0.00', tone: 'warning' }
    ]);
    expect.soft(metricRow(range, 'Fuel Range Workbench', 'Stress / Explicit null fuel')?.segments
      .map((segment) => segment.value)).toEqual(['--', '--', '--', '--', '--', '--']);

    const target = (await reviewServer.getJson(
      '/api/overlay-model/fuel-calculator?preview=race&fixture=fuel-laps-workbench-target'
    )).model;
    expect.soft(metricSegmentDisplay(metricRow(target, 'Target Usage - Current Edges', 'Stress / Round-centered candidates')?.segments)).toEqual([
      { label: 'Fuel', value: '44.0 L', tone: 'info' },
      { label: 'Last', value: '10.00 L', tone: 'info' },
      { label: '3 laps', value: '14.67 L', tone: 'success' },
      { label: '4 laps', value: '11.00 L', tone: 'success' },
      { label: '5 laps', value: '8.80 L', tone: 'error' }
    ]);
    expect.soft(metricSegmentDisplay(metricRow(target, 'Target Usage - Current Edges', 'Stress / Missing reference')?.segments)).toEqual([
      { label: 'Fuel', value: '44.0 L', tone: 'info' },
      { label: 'Last', value: '--', tone: 'waiting' },
      { label: '3 laps', value: '14.67 L', tone: 'info' },
      { label: '4 laps', value: '11.00 L', tone: 'info' },
      { label: '5 laps', value: '8.80 L', tone: 'info' }
    ]);

    const plan = (await reviewServer.getJson(
      '/api/overlay-model/fuel-calculator?preview=race&fixture=fuel-laps-workbench-plan'
    )).model;
    expect.soft(plan.status).toBe('fuel/plan workbench');
    expect.soft(gridRow(plan, 'Stress / Sub-lap full budget')).toMatchObject({
      tone: 'waiting',
      cells: [
        { value: '2 laps' },
        { value: '0 laps' },
        { value: '--' },
        { value: '--' },
        { value: '--' }
      ]
    });
    expect.soft(gridRow(plan, 'Stress / Sub-lap future budget')).toMatchObject({
      tone: 'waiting',
      cells: [
        { value: '2 laps' },
        { value: '2' },
        { value: '1.0 / 0 laps' },
        { value: '1.0 now + --' },
        { value: '--' },
        { value: '--' }
      ]
    });
    expect.soft(gridRow(plan, 'Stress / Known zero full budget')).toMatchObject({
      tone: 'waiting',
      cells: [
        { value: '2 laps' },
        { value: '0 laps' },
        { value: '--' },
        { value: '--' },
        { value: '--' }
      ]
    });
    expect.soft(gridRow(plan, 'Stress / Explicit null full budget')?.cells.map((cell) => cell.value))
      .toEqual(['2 laps', '--', '--', '--', '--']);
    expect.soft(gridRow(plan, 'Stress / Known zero checkpoint fuel')).toMatchObject({
      tone: 'waiting',
      cells: [
        { value: '2 laps' },
        { value: '2' },
        { value: '0 laps' },
        { value: '0 now + --' },
        { value: '--' },
        { value: '--' }
      ]
    });
    expect.soft(gridRow(plan, 'Stress / Explicit null checkpoint fuel')?.cells.map((cell) => cell.value))
      .toEqual(['2 laps', '2', '--', '--', '--', '--']);

    const stint = (await reviewServer.getJson(
      '/api/overlay-model/fuel-calculator?preview=race&fixture=fuel-laps-workbench-stint'
    )).model;
    const zeroFuel = gridRow(stint, 'Stress / Known zero fuel');
    expect.soft(zeroFuel.cells[1]).toMatchObject({ value: '0.00 laps', tone: 'info' });
    expect.soft(zeroFuel.cells[3]).toMatchObject({ value: '2: hide unrealistic', tone: 'error' });
    expect.soft(zeroFuel.cells[7]).toMatchObject({ value: 'zero is factual; not tracking', tone: 'error' });
    expect.soft(gridRow(stint, 'Stress / Race finished')).toMatchObject({
      tone: 'info',
      cells: [
        { value: '0' },
        { value: '5.00 laps' },
        { value: '--' },
        { value: '--' },
        { value: '--' },
        { value: '--' },
        { value: '10.00 L/lap' },
        { value: 'finished', tone: 'info' }
      ]
    });
    expect.soft(gridRow(stint, 'Stress / Save threshold 92')).toMatchObject({
      tone: 'warning',
      cells: expect.arrayContaining([
        expect.objectContaining({ value: '1: 9.20 save 0.80', tone: 'warning' }),
        expect.objectContaining({ value: 'boundary; save 0.80 L/lap', tone: 'warning' })
      ])
    });
    expect.soft(gridRow(stint, 'Stress / Save threshold below 92')).toMatchObject({
      tone: 'warning',
      cells: expect.arrayContaining([
        expect.objectContaining({ value: '1: 9.19 big save', tone: 'warning' }),
        expect.objectContaining({ value: 'worse boundary; large save', tone: 'warning' })
      ])
    });
    expect.soft(gridRow(stint, 'Stress / Held tracking')?.cells[3])
      .toMatchObject({ value: '1: 10.00 ok', tone: 'warning' });
    expect.soft(gridRow(stint, 'Stress / Held tracking')?.cells[7])
      .toMatchObject({ value: 'held context; tracking', tone: 'warning' });
    expect.soft(gridRow(stint, 'Stress / Explicit null telemetry')).toMatchObject({
      tone: 'warning',
      cells: [
        { value: '--' },
        { value: '--' },
        { value: '1: --' },
        { value: '2: --' },
        { value: '3: --' },
        { value: '4: --' },
        { value: '10.00 L/lap' },
        { value: 'null stays unavailable; learning', tone: 'warning' }
      ]
    });
    expect.soft(gridRow(stint, 'Stress / Stretch costs more than stop')?.cells[4])
      .toMatchObject({ value: '4: not worth +13s', tone: 'warning' });

    const pit = (await reviewServer.getJson(
      '/api/overlay-model/fuel-calculator?preview=race&fixture=fuel-laps-workbench-pit'
    )).model;
    const invalidReserve = metricRow(pit, 'Fuel To Add Workbench', 'Stress / Invalid negative reserve');
    expect.soft(invalidReserve.segments.map((segment) => segment.value)).toEqual([
      '--', '--', '--', '--', '--', '--'
    ]);
    const invalidCapacity = metricRow(pit, 'Fuel To Add Workbench', 'Stress / Invalid zero capacity');
    expect.soft(invalidCapacity.segments.map((segment) => segment.value)).toEqual([
      '--', '--', '--', '--', '--', '--'
    ]);
    const zeroPitFuel = metricRow(pit, 'Fuel To Add Workbench', 'Dallara quali seed / 4-lap target');
    expect.soft(zeroPitFuel.segments[3]).toMatchObject({ value: '51.0 L cap quali', tone: 'error' });
    expect.soft(zeroPitFuel.segments[5]).toMatchObject({ value: '51.0 L cap quali', tone: 'error' });
  });

  it('retains stable burn-bucket identity and provenance without synthesizing extrema', async () => {
    const fuel = (await reviewServer.getJson(
      '/api/overlay-model/fuel-calculator?preview=race&fixture=fuel-laps-workbench-fuel'
    )).model;
    expect.soft(fuel.metricSections[0].rows[0].segments).toMatchObject([
      { burnBucketId: 'Last', burnSource: 'LiveLastLap', sampleCount: 1, displayEligible: true, strategyEligible: true },
      { burnBucketId: 'FiveLapAverage', burnSource: 'LiveFiveLapAverage', sampleCount: 5, displayEligible: true, strategyEligible: true },
      { burnBucketId: 'TenLapAverage', burnSource: 'LiveTenLapAverage', sampleCount: 10, displayEligible: true, strategyEligible: true },
      { burnBucketId: 'Maximum', burnSource: 'LiveMaximum', sampleCount: 10, displayEligible: true, strategyEligible: true }
    ]);

    const trustedSeed = (await reviewServer.getJson(
      '/api/overlay-model/fuel-calculator?preview=race&fixture=fuel-laps-workbench-fuel-trusted-seed'
    )).model;
    expect.soft(trustedSeed.metricSections[0].rows[0].segments[3]).toMatchObject({
      label: 'Max',
      value: '14.20 L/lap',
      burnBucketId: 'Maximum',
      burnSource: 'HistoricalSeed',
      sampleCount: 12,
      confidence: 'Seeded',
      displayEligible: true,
      cleanBaselineEligible: false,
      strategyEligible: true
    });

    const range = (await reviewServer.getJson(
      '/api/overlay-model/fuel-calculator?preview=race&fixture=fuel-laps-workbench-range'
    )).model;
    const partialRange = metricRow(range, 'Fuel Range Workbench', 'Dallara 45m / Mid S1')?.segments[3];
    expect.soft(partialRange).toMatchObject({
      label: '5L',
      value: '2.29 3/5',
      burnBucketId: 'FiveLapAverage',
      burnSource: 'LiveFiveLapAverage',
      sampleCount: 3,
      displayEligible: true,
      strategyEligible: false
    });
    expect.soft(partialRange?.provenance).toContain('range from 5L');

    const target = (await reviewServer.getJson(
      '/api/overlay-model/fuel-calculator?preview=race&fixture=fuel-laps-workbench-target'
    )).model;
    const qualifyingTarget = metricRow(target, 'Target Usage - Green Start', 'Dallara quali seed / Green est');
    expect.soft(qualifyingTarget?.segments[1]).toMatchObject({
      label: 'Quali',
      burnBucketId: 'Qualifying',
      burnSource: 'QualifyingSeed',
      sampleCount: 1,
      strategyEligible: false
    });
    expect.soft(qualifyingTarget?.segments[2]).toMatchObject({
      referenceBurnBucketId: 'Qualifying',
      referenceBurnSource: 'QualifyingSeed',
      referenceSampleCount: 1,
      referenceConfidence: 'Seeded',
      referenceCleanBaselineEligible: false,
      referenceStrategyEligible: false
    });
    const missingIdentity = metricRow(target, 'Target Usage - Current Edges', 'Stress / Missing reference identity');
    expect.soft(missingIdentity?.segments[1]).toMatchObject({
      label: 'Reference',
      value: '--',
      burnBucketId: null,
      burnSource: 'Unavailable',
      displayEligible: false,
      strategyEligible: false
    });
    expect.soft(missingIdentity?.segments[2]).toMatchObject({
      referenceBurnBucketId: null,
      referenceBurnSource: 'Unavailable',
      referenceStrategyEligible: false
    });

    const pit = (await reviewServer.getJson(
      '/api/overlay-model/fuel-calculator?preview=race&fixture=fuel-laps-workbench-pit'
    )).model;
    const qualifyingPit = metricRow(pit, 'Fuel To Add Workbench', 'Dallara quali seed / 4-lap target');
    expect.soft(qualifyingPit?.segments.map((segment) => segment.burnBucketId)).toEqual([
      'Last', 'FiveLapAverage', 'TenLapAverage', 'Maximum', 'Minimum', 'Qualifying'
    ]);
    expect.soft(qualifyingPit?.segments[3]).toMatchObject({
      burnBucketId: 'Maximum',
      burnSource: 'QualifyingSeed',
      sampleCount: 1,
      strategyEligible: false
    });
    expect.soft(qualifyingPit?.segments[5]).toMatchObject({
      burnBucketId: 'Qualifying',
      burnSource: 'QualifyingSeed',
      sampleCount: 1,
      strategyEligible: false
    });

    const noExtrema = metricRow(pit, 'Fuel To Add Workbench', 'Stress / No explicit extrema');
    expect.soft(noExtrema?.segments.map((segment) => segment.value)).toEqual([
      '+10.0 L', '+9.0 L', '+8.0 L', '--', '--', '--'
    ]);
    expect.soft(noExtrema?.segments.slice(3).map((segment) => ({
      id: segment.burnBucketId,
      source: segment.burnSource
    }))).toEqual([
      { id: 'Maximum', source: 'Unavailable' },
      { id: 'Minimum', source: 'Unavailable' },
      { id: 'Qualifying', source: 'Unavailable' }
    ]);
  });

  it('keeps effective capacity and ordered fuel checkpoints typed and distinct', async () => {
    const capacity = (await reviewServer.getJson(
      '/api/overlay-model/fuel-calculator?preview=race&fixture=fuel-laps-workbench-capacity'
    )).model;
    expect.soft(capacity.status).toBe('fuel/effective capacity workbench');
    expect.soft(gridRow(capacity, 'Dallara 45m / 80% event cap')).toMatchObject({
      tone: 'info',
      cells: [
        { value: '75.00 L' },
        { value: '80.0%' },
        { value: '80.0%' },
        { value: '58.99 L' },
        { value: '60.00 L' },
        { value: 'driver + class' },
        { value: 'limited; authoritative' }
      ]
    });
    expect.soft(gridRow(capacity, 'Dallara 4L / 68% event cap')?.cells[4])
      .toMatchObject({ value: '51.00 L', tone: 'info' });
    expect.soft(gridRow(capacity, 'Stress / Driver cap only')).toMatchObject({
      tone: 'info',
      cells: expect.arrayContaining([
        expect.objectContaining({ value: '51.00 L' }),
        expect.objectContaining({ value: 'driver cap' }),
        expect.objectContaining({ value: 'limited; high' })
      ])
    });
    expect.soft(gridRow(capacity, 'Stress / Missing cap evidence')).toMatchObject({
      tone: 'warning',
      cells: expect.arrayContaining([
        expect.objectContaining({ value: '--' }),
        expect.objectContaining({ value: 'physical only' }),
        expect.objectContaining({ value: 'missing cap; no advice' })
      ])
    });
    expect.soft(gridRow(capacity, 'Stress / Conflicting caps')).toMatchObject({
      tone: 'error',
      cells: expect.arrayContaining([
        expect.objectContaining({ value: '51.00 L' }),
        expect.objectContaining({ value: 'min reported' }),
        expect.objectContaining({ value: 'cap conflict; no advice' })
      ])
    });
    expect.soft(gridRow(capacity, 'Stress / Observed above cap')?.cells[6])
      .toMatchObject({ value: 'observed above cap; conflict', tone: 'error' });
    expect.soft(gridRow(capacity, 'Stress / Invalid driver cap')?.cells[6])
      .toMatchObject({ value: 'invalid evidence; no advice', tone: 'error' });
    expect.soft(gridRow(capacity, 'Stress / Missing physical tank')?.cells[6])
      .toMatchObject({ value: 'missing physical; no advice', tone: 'warning' });

    const checkpoints = (await reviewServer.getJson(
      '/api/overlay-model/fuel-calculator?preview=race&fixture=fuel-laps-workbench-checkpoints'
    )).model;
    expect.soft(checkpoints.status).toBe('fuel/checkpoint flow workbench');
    expect.soft(gridRow(checkpoints, 'Dallara 45m / Measured green')).toMatchObject({
      tone: 'info',
      cells: [
        { value: '60.00 L resolved' },
        { value: '58.99 L measured' },
        { value: '58.99 L measured' },
        { value: '--' },
        { value: '--' },
        { value: '--' },
        { value: 'service complete' },
        { value: 'ordered facts' }
      ]
    });
    expect.soft(gridRow(checkpoints, 'Dallara 4L / Estimated green')?.cells[1])
      .toMatchObject({ value: '50.10 L est', tone: 'warning' });
    expect.soft(gridRow(checkpoints, 'Stress / Single-cap confidence')?.cells[0])
      .toMatchObject({ value: '60.00 L high', tone: 'info' });
    expect.soft(gridRow(checkpoints, 'Stress / Formation exceeds cap')?.cells[1])
      .toMatchObject({ value: '0.00 L est floor', tone: 'warning' });
    expect.soft(gridRow(checkpoints, 'Dallara 45m / Projected pit cycle')).toMatchObject({
      tone: 'info',
      cells: [
        { value: '60.00 L resolved' },
        { value: '--' },
        { value: '33.29 L measured' },
        { value: '32.29 L est' },
        { value: '52.29 L est' },
        { value: '52.15 L est' },
        { value: 'service complete' },
        { value: 'ordered facts' }
      ]
    });
    expect.soft(gridRow(checkpoints, 'Stress / Measured overrides projections')?.cells.slice(3, 6))
      .toEqual([
        { value: '20.00 L measured', tone: 'info' },
        { value: '50.00 L measured', tone: 'info' },
        { value: '49.80 L measured', tone: 'info' }
      ]);
    expect.soft(gridRow(checkpoints, 'Stress / Known zero flow')?.cells.slice(2, 6).map((cell) => cell.value))
      .toEqual(['0.00 L measured', '0.00 L measured', '0.00 L est', '0.00 L est']);
    expect.soft(gridRow(checkpoints, 'Stress / Missing current telemetry')?.cells.slice(2, 6).map((cell) => cell.value))
      .toEqual(['--', '--', '--', '--']);
    expect.soft(gridRow(checkpoints, 'Stress / Above capacity is not clamped')).toMatchObject({
      tone: 'error',
      cells: expect.arrayContaining([
        expect.objectContaining({ value: '60.00 L conflict', tone: 'error' }),
        expect.objectContaining({ value: '59.50 L conflict', tone: 'error' }),
        expect.objectContaining({ value: 'above cap; not clamped', tone: 'error' })
      ])
    });
    expect.soft(gridRow(checkpoints, 'Stress / Invalid transition input')?.cells[7])
      .toMatchObject({ value: 'invalid transition input', tone: 'error' });
    expect.soft(gridRow(checkpoints, 'Stress / Cannot reach box')).toMatchObject({
      tone: 'error',
      cells: [
        { value: '60.00 L resolved', tone: 'info' },
        { value: '--', tone: 'waiting' },
        { value: '1.00 L measured', tone: 'info' },
        { value: '0.00 L conflict floor', tone: 'error' },
        { value: '--', tone: 'waiting' },
        { value: '--', tone: 'waiting' },
        { value: 'service complete', tone: 'info' },
        { value: 'projection below zero; chain stopped', tone: 'error' }
      ]
    });
  });

  it('keeps exact boundary and service feasibility math separate and full precision', async () => {
    const boundary = (await reviewServer.getJson(
      '/api/overlay-model/fuel-calculator?preview=race&fixture=fuel-laps-workbench-boundary'
    )).model;
    expect.soft(boundary.status).toBe('fuel/boundary feasibility workbench');
    expect.soft(boundary.gridSections[0].headers).toEqual([
      'Scenario', 'Bucket', 'Range', 'Safe', 'Next lap', 'Desired / add', 'Room / clamp', 'Shortfall', 'Max', 'State'
    ]);
    expect.soft(gridRow(boundary, 'Stress / Exact 3-lap edge')).toMatchObject({
      tone: 'info',
      cells: [
        { value: 'Last' },
        { value: '3.000000 laps' },
        { value: '3' },
        { value: '10.00000 L' },
        { value: '50.00 L / 40.00 L' },
        { value: '50.00 L / 40.00 L' },
        { value: '0.00000 L' },
        { value: '6' },
        { value: 'range: available; target: feasible; exact-lap-boundary' }
      ]
    });
    expect.soft(gridRow(boundary, 'Stress / Below displayed 3.00')?.cells.slice(1, 4))
      .toEqual([
        { value: '2.999999 laps', tone: 'info' },
        { value: '2', tone: 'info' },
        { value: '0.00001 L', tone: 'info' }
      ]);
    expect.soft(gridRow(boundary, 'Stress / Above displayed 3.00')?.cells.slice(1, 4))
      .toEqual([
        { value: '3.000001 laps', tone: 'info' },
        { value: '3', tone: 'info' },
        { value: '9.99999 L', tone: 'info' }
      ]);
    expect.soft(gridRow(boundary, 'Stress / Known zero facts')).toMatchObject({
      tone: 'info',
      cells: expect.arrayContaining([
        expect.objectContaining({ value: '0.000000 laps' }),
        expect.objectContaining({ value: '0' }),
        expect.objectContaining({ value: '10.00000 L' }),
        expect.objectContaining({ value: 'range: known-zero; target: feasible; exact-lap-boundary, known-zero-range, known-zero-service' })
      ])
    });
    expect.soft(gridRow(boundary, 'Stress / Tank-limited target')).toMatchObject({
      tone: 'error',
      cells: expect.arrayContaining([
        expect.objectContaining({ value: '60.00 L / 50.00 L' }),
        expect.objectContaining({ value: '40.00 L / 40.00 L' }),
        expect.objectContaining({ value: '10.00000 L' }),
        expect.objectContaining({ value: '5' }),
        expect.objectContaining({ value: 'range: available; target: unachievable; exact-lap-boundary, tank-limited' })
      ])
    });
    expect.soft(gridRow(boundary, 'Stress / Margin flips feasibility')?.cells.slice(6, 9))
      .toEqual([
        { value: '0.00010 L', tone: 'error' },
        { value: '4', tone: 'error' },
        { value: 'range: available; target: unachievable; exact-lap-boundary, tank-limited', tone: 'error' }
      ]);
    expect.soft(gridRow(boundary, 'Stress / Missing current only')?.cells[8])
      .toMatchObject({ value: 'range: unavailable; target: feasible', tone: 'warning' });
    expect.soft(gridRow(boundary, 'Stress / Missing at-box only')?.cells.slice(4, 9).map((cell) => cell.value))
      .toEqual(['20.00 L / --', '-- / --', '--', '6', 'range: available; target: unavailable; exact-lap-boundary']);
    expect.soft(gridRow(boundary, 'Stress / Missing capacity')?.cells.slice(4, 9).map((cell) => cell.value))
      .toEqual(['20.00 L / 10.00 L', '-- / --', '--', '--', 'range: available; target: unavailable; exact-lap-boundary']);
    expect.soft(gridRow(boundary, 'Stress / Conflicting capacity')?.cells[8])
      .toMatchObject({ value: 'range: available; target: capacity-conflicted; exact-lap-boundary', tone: 'error' });
    expect.soft(gridRow(boundary, 'Stress / Invalid reserve')?.cells[8])
      .toMatchObject({ value: 'range: available; target: invalid; exact-lap-boundary', tone: 'error' });
    expect.soft(gridRow(boundary, 'Stress / Invalid current telemetry')?.cells[8])
      .toMatchObject({ value: 'range: invalid; target: feasible', tone: 'error' });
    expect.soft(gridRow(boundary, 'Stress / Invalid at-box telemetry')).toMatchObject({
      tone: 'error',
      cells: expect.arrayContaining([
        expect.objectContaining({ value: '2.000000 laps' }),
        expect.objectContaining({ value: '20.00 L / --' }),
        expect.objectContaining({ value: 'range: available; target: invalid; exact-lap-boundary' })
      ])
    });
    expect.soft(gridRow(boundary, 'Stress / Invalid capacity evidence')?.cells[8])
      .toMatchObject({ value: 'range: available; target: invalid; exact-lap-boundary', tone: 'error' });
    expect.soft(gridRow(boundary, 'Stress / Incomplete burn evidence')).toMatchObject({
      tone: 'error',
      cells: expect.arrayContaining([
        expect.objectContaining({ value: 'Last', tone: 'error' }),
        expect.objectContaining({ value: 'range: invalid; target: invalid' })
      ])
    });
    expect.soft(gridRow(boundary, 'Stress / Finite overflow edge')).toMatchObject({
      tone: 'error',
      cells: expect.arrayContaining([
        expect.objectContaining({ value: '--', tone: 'error' }),
        expect.objectContaining({ value: 'range: invalid; target: unavailable' })
      ])
    });
    expect.soft(gridRow(boundary, 'Stress / Seed-only evidence')).toMatchObject({
      tone: 'info',
      cells: expect.arrayContaining([
        expect.objectContaining({ value: 'Quali seeded', tone: 'warning' }),
        expect.objectContaining({ value: '2.000000 laps' }),
        expect.objectContaining({ value: 'range: available; target: feasible; exact-lap-boundary' })
      ])
    });
  });

  it('composes the shared fuel snapshot only from explicitly named owners', async () => {
    const snapshot = (await reviewServer.getJson(
      '/api/overlay-model/fuel-calculator?preview=race&fixture=fuel-laps-workbench-snapshot'
    )).model;
    expect.soft(snapshot.status).toBe('fuel/shared snapshot workbench');
    expect.soft(snapshot.gridSections[0].headers).toEqual([
      'Scenario', 'Lap budget', 'Capacity', 'Current / box', 'Range', 'Fuel to add', 'Target usage', 'Plan', 'Contract'
    ]);
    expect.soft(gridRow(snapshot, 'Populated / explicit owners')).toMatchObject({
      tone: 'info',
      cells: [
        { value: '12 / 11.50', tone: 'info' },
        { value: '60.00 L', tone: 'info' },
        { value: '40.00 L / 10.00 L', tone: 'info' },
        { value: '4.000000 laps', tone: 'info' },
        { value: '11.50 L', tone: 'info' },
        { value: '58.00 L / 11.60 L @5', tone: 'success' },
        { value: '4 now + 5 x1 + 3 / 2 stops', tone: 'info' },
        { value: 'current→range; at-box→service; first-green/5L target; explicit current+future plan', tone: 'info' }
      ]
    });

    expect.soft(gridRow(snapshot, 'Degraded / no implicit fallback')).toMatchObject({
      tone: 'warning',
      cells: [
        { value: '12 / 11.50' },
        { value: '60.00 L' },
        { value: '40.00 L / --' },
        { value: '4.000000 laps' },
        { value: '--' },
        { value: '-- / -- @4 [unavailable]' },
        { value: '-- / -- stops' },
        { value: 'Current and Last stay visible; missing FirstGreen, 5L, target, and Plan inputs stay missing' }
      ]
    });

    expect.soft(gridRow(snapshot, 'Unavailable / owner shells only')).toMatchObject({
      tone: 'waiting',
      cells: [
        { value: '-- / --' },
        { value: '--' },
        { value: '-- / --' },
        { value: '--' },
        { value: '--' },
        { value: '-- / -- @1 [unavailable]' },
        { value: '--' },
        { value: 'Typed unavailable facts; no fabricated capacity, checkpoint, bucket, request, target, or plan' }
      ]
    });
    expect.soft(gridRow(snapshot, 'Unrequested projections / invalid upstreams stay unavailable')).toMatchObject({
      tone: 'error',
      cells: expect.arrayContaining([
        expect.objectContaining({ value: '-- / -- @4 [unavailable]', tone: 'waiting' }),
        expect.objectContaining({ value: '-- / -- stops [unavailable]', tone: 'waiting' }),
        expect.objectContaining({ value: 'No formation or current-to-box input: FirstGreen and AtBox remain Unavailable' })
      ])
    });
    expect.soft(gridRow(snapshot, 'Invalid capacity / diagnostic boundary only')).toMatchObject({
      tone: 'error',
      cells: expect.arrayContaining([
        expect.objectContaining({ value: '2.000000 laps' }),
        expect.objectContaining({ value: '--', tone: 'error' }),
        expect.objectContaining({ value: 'Boundary retains invalid diagnostic math; Fuel To Add projection is suppressed like Core' })
      ])
    });
    expect.soft(gridRow(snapshot, 'Conflicted cap / blocked selected budget')?.cells.slice(1, 7))
      .toEqual([
        { value: '51.00 L', tone: 'error' },
        { value: '20.00 L / 10.00 L', tone: 'info' },
        { value: '2.000000 laps', tone: 'info' },
        { value: '10.00 L', tone: 'error' },
        { value: '-- / -- @4 [conflicted]', tone: 'waiting' },
        { value: '-- / -- stops [conflicted]', tone: 'waiting' }
      ]);
    expect.soft(gridRow(snapshot, 'Invalid at-box / projected fallback blocked')?.cells.slice(2, 7))
      .toEqual([
        { value: '20.00 L / 19.00 L', tone: 'info' },
        { value: '2.000000 laps', tone: 'info' },
        { value: '--', tone: 'error' },
        { value: '-- / -- @2 [invalid]', tone: 'waiting' },
        { value: '2 now + -- / -- stops [future invalid]', tone: 'waiting' }
      ]);
    expect.soft(gridRow(snapshot, 'Invalid descendant chain / provided projections blocked')).toMatchObject({
      tone: 'error',
      cells: [
        { value: '12 / 11.50', tone: 'info' },
        { value: '60.00 L', tone: 'info' },
        { value: '-- / --', tone: 'waiting' },
        { value: '--', tone: 'error' },
        { value: '--', tone: 'error' },
        { value: '-- / -- @2 [invalid]', tone: 'waiting' },
        { value: '-- / -- stops [invalid]', tone: 'waiting' },
        { value: 'Provided service and pit-exit projections retain the upstream Invalid state', tone: 'error' }
      ]
    });
    expect.soft(gridRow(snapshot, 'Clamped projection / conflicted zero')?.cells.slice(2, 7))
      .toEqual([
        { value: '1.00 L / 0.00 L', tone: 'info' },
        { value: '0.100000 laps', tone: 'info' },
        { value: '20.00 L', tone: 'error' },
        { value: '-- / -- @2 [conflicted]', tone: 'waiting' },
        { value: '-- / -- stops [conflicted]', tone: 'waiting' }
      ]);
    expect.soft(gridRow(snapshot, 'Known zero / factual selection')?.cells.slice(2, 7))
      .toEqual([
        { value: '0.00 L / 0.00 L', tone: 'info' },
        { value: '0.000000 laps', tone: 'info' },
        { value: '20.00 L', tone: 'info' },
        { value: '-- / -- @2', tone: 'waiting' },
        { value: '-- / -- stops', tone: 'waiting' }
      ]);
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
    const chromeOff = (await reviewServer.getJson('/api/overlay-model/session-weather?preview=race&fixture=chrome-off')).model;
    const missing = (await reviewServer.getJson('/api/overlay-model/session-weather?preview=race&fixture=session-weather-missing')).model;
    const weatherOff = (await reviewServer.getJson('/api/overlay-model/session-weather?preview=race&fixture=session-weather-weather-off')).model;

    expect.soft(metricSectionTitles(chromeOff)).toEqual(['Session', 'Weather']);
    expect.soft(metricRowLabels(chromeOff, 'Session')).toHaveLength(5);
    expect.soft(metricRowLabels(chromeOff, 'Weather')).toEqual(['Surface', 'Sky', 'Wind', 'Temps', 'Atmosphere']);
    expect.soft(chromeOff.effectiveSettings.rendered.browserSource.baseHeight).toBe(
      overlayGeometry.overlaySizes.sessionWeatherHeight - overlayGeometry.metricRows.headerChromeHeight
    );
    expect.soft(chromeOff.effectiveSettings.rendered.browserSource.height).toBe(
      overlayGeometry.overlaySizes.sessionWeatherHeight - overlayGeometry.metricRows.headerChromeHeight
    );

    expect.soft(missing.status).toBe('weather unavailable');
    expect.soft(metricSectionTitles(missing)).toEqual(['Session', 'Weather']);
    expect.soft(metricRowLabels(missing, 'Session')).toEqual(['Session', 'Clock', 'Event', 'Track', 'Laps']);
    expect.soft(metricRowLabels(missing, 'Weather')).toEqual(['Surface', 'Sky', 'Wind', 'Temps', 'Atmosphere']);
    expect.soft(missing.source).toMatch(/weather source unavailable/i);
    expect.soft(missing.effectiveSettings.rendered.unavailableContentPolicy).toBe('section-aware-placeholders');
    expect.soft(missing.effectiveSettings.rendered.browserSource.baseHeight).toBe(493);

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

function metricRow(model, sectionTitle, rowLabel) {
  return (model.metricSections || [])
    .find((section) => section.title === sectionTitle)
    ?.rows?.find((row) => row.label === rowLabel);
}

function metricSegmentDisplay(segments = []) {
  return segments.map(({ label, value, tone }) => ({ label, value, tone }));
}

function gridRow(model, rowLabel) {
  return (model.gridSections || [])
    .flatMap((section) => section.rows || [])
    .find((row) => row.label === rowLabel);
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
