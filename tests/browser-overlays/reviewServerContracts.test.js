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
});
