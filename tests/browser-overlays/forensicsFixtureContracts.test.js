import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { freshLiveSnapshot, renderBrowserOverlay, waitFor } from './browserOverlayTestHost.js';
import { repoRoot } from './browserOverlayAssets.js';
import { startReviewServer } from './reviewServerTestHost.js';

const fixture = JSON.parse(readFileSync(
  resolve(repoRoot, 'fixtures/telemetry-analysis/garage-cover-navarra-obs-policy.json'),
  'utf8'));

let reviewServer;
let currentOverlay;

beforeAll(async () => {
  reviewServer = await startReviewServer();
}, 10000);

afterEach(() => {
  currentOverlay?.close();
  currentOverlay = null;
});

afterAll(async () => {
  currentOverlay?.close();
  await reviewServer?.stop();
});

describe('forensics fixture renderer contracts', () => {
  it('replays the compact Navarra Garage Cover fixture through browser-review and localhost semantics', async () => {
    expect(fixture.id).toBe('garage-cover-navarra-obs-policy');
    expect(fixture.source.redaction).toContain('raw telemetry');

    for (const testCase of fixture.cases) {
      await applyReviewCase(testCase);

      const query = reviewQuery(testCase);
      const settings = (await reviewServer.getJson(`/api/garage-cover?preview=practice&${query}`)).garageCover;
      const model = (await reviewServer.getJson(`/api/overlay-model/garage-cover?preview=practice&${query}`)).model;

      assertReviewModelContract(testCase, settings, model);

      currentOverlay = await renderBrowserOverlay('garage-cover', {
        live: liveSnapshot(testCase),
        settings,
        model,
        query: `?${query}`,
        userAgent: 'Mozilla/5.0 OBS Studio/32.1.2',
        waitForSelector: testCase.expected.shouldRender ? '.garage-cover' : null
      });

      if (testCase.expected.shouldRender) {
        await waitFor(() => currentOverlay.document.querySelector('.garage-cover'));
        expect.soft(currentOverlay.document.querySelector('.overlay').style.opacity, testCase.id).toBe('1');
        expect.soft(currentOverlay.browserSourceEvents, testCase.id).toEqual(expect.arrayContaining([
          expect.objectContaining({ event: 'model-render', overlayId: 'garage-cover' })
        ]));

        const image = currentOverlay.document.querySelector('.garage-cover img');
        expect.soft(image, testCase.id).not.toBeNull();
        if (testCase.imageMode === 'ready') {
          expect.soft(image?.getAttribute('src'), testCase.id).toContain('/api/garage-cover/image');
        } else {
          expect.soft(image?.getAttribute('src'), testCase.id).toContain('/api/garage-cover/default-image');
        }
      } else {
        await waitFor(() => currentOverlay.document.querySelector('.overlay').style.opacity === '0');
        expect.soft(currentOverlay.document.querySelector('.garage-cover'), testCase.id).toBeNull();
        expect.soft(currentOverlay.document.getElementById('content').textContent, testCase.id).toBe('');
        expect.soft(currentOverlay.browserSourceEvents, testCase.id).toEqual(expect.arrayContaining([
          expect.objectContaining({ event: 'model-hidden', overlayId: 'garage-cover' })
        ]));
      }

      expect.soft(currentOverlay.fetchCalls, testCase.id).toContain('/api/overlay-model/garage-cover');
      expect.soft(currentOverlay.fetchCalls, testCase.id).toContain('/api/browser-source-event');

      currentOverlay.close();
      currentOverlay = null;
    }
  });
});

async function applyReviewCase(testCase) {
  await reviewServer.postReviewPatch({
    kind: 'overlayEnabled',
    overlayId: 'garage-cover',
    enabled: testCase.overlayEnabled
  });
  await reviewServer.postReviewPatch({
    kind: 'garageCover',
    overlayId: 'garage-cover',
    action: 'clear'
  });
  if (testCase.previewActive === true) {
    await reviewServer.postReviewPatch({
      kind: 'garageCover',
      overlayId: 'garage-cover',
      action: 'preview'
    });
  }
}

function assertReviewModelContract(testCase, settings, model) {
  const expected = testCase.expected;
  expect.soft(settings.previewVisible, testCase.id).toBe(expected.previewVisible ?? false);
  expect.soft(settings.fallbackReason ?? null, testCase.id).toBe(expected.fallbackReason ?? null);
  expect.soft(settings.hasImage, testCase.id).toBe(testCase.imageMode === 'ready');

  expect.soft(model.overlayId, testCase.id).toBe('garage-cover');
  expect.soft(model.shouldRender, testCase.id).toBe(expected.shouldRender);
  expect.soft(model.status, testCase.id).toBe(expected.status);
  expect.soft(model.effectiveSettings.rendered.shouldRender, testCase.id).toBe(expected.shouldRender);
  expect.soft(model.effectiveSettings.sources.browserReview.routePath, testCase.id).toBe('/review/overlays/garage-cover');
  expect.soft(model.effectiveSettings.sources.localhostObs.routePath, testCase.id).toBe('/overlays/garage-cover');
  expect.soft(model.effectiveSettings.sources.browserReview.sharedSettingsHash, testCase.id).toBe(
    model.effectiveSettings.sources.localhostObs.sharedSettingsHash);
  expect.soft(model.effectiveSettings.sources.browserReview.overlaySettingsHash, testCase.id).toBe(
    model.effectiveSettings.sources.localhostObs.overlaySettingsHash);
  expect.soft(model.effectiveSettings.rendered.browserSource, testCase.id).toMatchObject({
    baseWidth: 1280,
    baseHeight: 720,
    width: 1280,
    height: 720
  });

  if (expected.modelPresent === false) {
    expect.soft(model.garageCover, testCase.id).toBeUndefined();
    return;
  }

  expect.soft(model.garageCover.shouldCover, testCase.id).toBe(expected.shouldCover);
  expect.soft(model.garageCover.detection.state, testCase.id).toBe(expected.detectionState);
  expect.soft(model.garageCover.browserSettings.previewVisible, testCase.id).toBe(expected.previewVisible);
  expect.soft(model.garageCover.browserSettings.fallbackReason ?? null, testCase.id).toBe(expected.fallbackReason ?? null);
}

function reviewQuery(testCase) {
  const params = new URLSearchParams();
  params.set('fixture', reviewFixtureVariant(testCase));
  params.set('garageImageMode', testCase.imageMode);
  return params.toString();
}

function reviewFixtureVariant(testCase) {
  if (testCase.telemetry.isConnected === false) {
    return 'garage-disconnected';
  }
  if (Number(testCase.telemetry.updatedAgeSeconds) > 2.5) {
    return 'garage-stale';
  }
  return testCase.telemetry.isGarageVisible ? 'garage-visible' : 'garage-hidden';
}

function liveSnapshot(testCase) {
  const telemetry = testCase.telemetry;
  const updatedAgeSeconds = telemetry.updatedAgeSeconds;
  const lastUpdatedAtUtc = updatedAgeSeconds === null || updatedAgeSeconds === undefined
    ? null
    : new Date(Date.now() - updatedAgeSeconds * 1000).toISOString();
  return {
    ...freshLiveSnapshot({
      raceEvents: {
        hasData: true,
        isGarageVisible: telemetry.isGarageVisible,
        isInGarage: telemetry.isInGarage,
        isOnTrack: !telemetry.isInGarage
      }
    }),
    isConnected: telemetry.isConnected,
    isCollecting: telemetry.isCollecting,
    lastUpdatedAtUtc
  };
}
