import { afterAll, beforeAll, describe, expect, it } from 'vitest';
import { startReviewServer } from './reviewServerTestHost.js';

let reviewServer;

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
        headerItems: []
      },
      settings: expect.arrayContaining([
        expect.objectContaining({ key: 'carsEachSide', value: 2 }),
        expect.objectContaining({ key: 'relative.content.relative.pit.enabled', session: 'race', value: false }),
        expect.objectContaining({ key: 'chrome.header.time-remaining.race', value: false })
      ])
    });
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
