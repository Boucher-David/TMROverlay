import { afterAll, beforeAll, describe, expect, it } from 'vitest';
import { existsSync, readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { browserOverlayPage, repoRoot } from './browserOverlayAssets.js';
import { startReviewServer } from './reviewServerTestHost.js';
import {
  buildOverlayEvidenceContract,
  compareOverlayEvidenceContracts,
  failureMessages,
  supportedOverlayIds,
  validateOverlayEvidenceContract
} from '../../tools/validation/overlay-evidence-contract.mjs';

const comparisonPath = resolve(repoRoot, 'tools/validation/overlay-evidence-framework-comparison.json');
const v103CoveragePath = resolve(repoRoot, 'tools/validation/v103-forensics-coverage.json');

let reviewServer;

beforeAll(async () => {
  reviewServer = await startReviewServer();
}, 10000);

afterAll(async () => {
  await reviewServer?.stop();
});

describe('overlay evidence contract lane', () => {
  it('keeps the side-by-side framework comparison tied to V1.0.3 coverage', () => {
    const comparison = JSON.parse(readFileSync(comparisonPath, 'utf8'));
    const v103Coverage = JSON.parse(readFileSync(v103CoveragePath, 'utf8'));

    expect.soft(comparison.contract).toBe('overlay-validation-framework-comparison/v1');
    expect.soft(comparison.lane.command).toBe('npm run test:evidence-contract');
    expect.soft(comparison.currentFramework.blindSpots.length, 'current framework blind spots').toBeGreaterThanOrEqual(5);
    expect.soft(comparison.evidenceContractFramework.primaryAssertions.length, 'evidence-contract assertions').toBeGreaterThanOrEqual(8);
    expect.soft(comparison.evidenceContractFramework.integratedSources || [], 'app-integrated evidence sources').toEqual(
      expect.arrayContaining([
        'tools/browser-review/server.mjs',
        'src/TmrOverlay.App/Overlays/BrowserSources/BrowserOverlayModelFactory.cs',
        'tests/TmrOverlay.App.Tests/Overlays/BrowserOverlayModelFactoryTests.cs'
      ])
    );

    for (const source of comparison.evidenceContractFramework.integratedSources || []) {
      expect.soft(existsSync(resolve(repoRoot, source)), `integrated evidence source exists: ${source}`).toBe(true);
    }

    const coveredIds = new Set((comparison.detectionMatrix || []).flatMap((item) => item.v103Ids || []));
    for (const item of v103Coverage.items || []) {
      expect.soft(coveredIds.has(item.id), `${item.id}: comparison matrix must map the new lane to this finding`).toBe(true);
    }
  });

  it('normalizes every supported race overlay into the contract before screenshot generation', async () => {
    for (const overlayId of supportedOverlayIds) {
      const model = (await reviewServer.getJson(`/api/overlay-model/${overlayId}?preview=race`)).model;
      const contract = buildOverlayEvidenceContract(model, { overlayId, previewMode: 'race' });
      const page = browserOverlayPage(overlayId);

      expect.soft(contract.contract, `${overlayId}: contract version`).toBe('overlay-evidence-contract/v1');
      expect.soft(contract.overlayId, `${overlayId}: overlay id`).toBe(overlayId);
      expect.soft(contract.previewMode, `${overlayId}: preview mode`).toBe('race');
      expect.soft(contract.semanticModel.bodyKind || contract.bodyKind, `${overlayId}: body kind`).toEqual(expect.any(String));
      expect.soft(contract.runtimeSurfaces, `${overlayId}: runtime surfaces`).toMatchObject({
        browserReview: { expectedRoutePath: `/review/overlays/${overlayId}` },
        localhostObs: { routePath: page.route, expectedRoutePath: page.route },
        windowsNative: { expectedRoutePath: `native://${overlayId}` }
      });
    }
  });

  it('keeps localhost OBS route evidence tied to the served overlay catalogue', async () => {
    for (const overlayId of supportedOverlayIds) {
      const page = browserOverlayPage(overlayId);
      const model = (await reviewServer.getJson(`${page.modelRoute}?preview=race`)).model;
      const contract = buildOverlayEvidenceContract(model, { overlayId, previewMode: 'race' });
      const localhostSurface = contract.runtimeSurfaces.localhostObs;

      expect.soft(localhostSurface.routePath, `${overlayId}: contract localhost route`).toBe(page.route);
      expect.soft(localhostSurface.expectedRoutePath, `${overlayId}: expected localhost route`).toBe(page.route);

      const html = await reviewServer.getText(page.route);
      expect.soft(html, `${overlayId}: served route fetches its model`).toContain(`fetchOverlayModel('${overlayId}')`);

      const staleSingularRoute = page.route.replace('/overlays/', '/overlay/');
      const staleResponse = await fetch(`${reviewServer.baseUrl}${staleSingularRoute}`);
      expect.soft(staleResponse.status, `${overlayId}: stale singular route should not be accepted`).toBe(404);
    }
  });

  it('separates complete evidence plumbing from unresolved product contract failures', async () => {
    const allowedProductFamilies = new Set(['chrome-contract', 'input-contract', 'unavailable-content']);
    const unexpectedFailures = [];
    const scenarios = [
      ...supportedOverlayIds.map((overlayId) => [overlayId, null]),
      ['gap-to-leader', 'gap-no-cars'],
      ['input-state', 'input-state-mock-data'],
      ['input-state', 'input-waiting'],
      ['car-radar', 'car-radar-right'],
      ['flags', 'flags-all-kinds'],
      ['stream-chat', 'stream-chat-twitch-rich']
    ];

    for (const [overlayId, fixtureVariant] of scenarios) {
      const fixtureQuery = fixtureVariant ? `&fixture=${fixtureVariant}` : '';
      const model = (await reviewServer.getJson(`/api/overlay-model/${overlayId}?preview=race${fixtureQuery}`)).model;
      const contract = buildOverlayEvidenceContract(model, { overlayId, previewMode: 'race', fixtureVariant });
      for (const failure of validateOverlayEvidenceContract(contract, { requireSyntheticStateKind: Boolean(fixtureVariant) })) {
        if (!allowedProductFamilies.has(failure.family)) {
          unexpectedFailures.push(failure);
        }
      }
    }

    expect.soft(failureMessages(unexpectedFailures)).toEqual([]);
  });

  it('validates semantic product contracts independently of screenshot pixels', async () => {
    const allFailures = [];
    for (const overlayId of supportedOverlayIds) {
      const model = (await reviewServer.getJson(`/api/overlay-model/${overlayId}?preview=race`)).model;
      const contract = buildOverlayEvidenceContract(model, { overlayId, previewMode: 'race' });
      const failures = validateOverlayEvidenceContract(contract);
      allFailures.push(...failures);
      expect.soft(failureMessages(failures), `${overlayId}: evidence contract failures`).toEqual([]);
    }

    expect.soft(
      new Set(allFailures.map((item) => item.family)),
      'the new lane should fail by family, not by pixel artifact'
    ).toEqual(new Set());
  });

  it('validates explicit unavailable, fallback, and synthetic fixture provenance', async () => {
    const scenarios = [
      ['gap-to-leader', 'gap-no-cars'],
      ['input-state', 'input-state-mock-data'],
      ['input-state', 'input-waiting'],
      ['car-radar', 'car-radar-right'],
      ['flags', 'flags-all-kinds'],
      ['stream-chat', 'stream-chat-twitch-rich']
    ];

    for (const [overlayId, fixtureVariant] of scenarios) {
      const model = (await reviewServer.getJson(`/api/overlay-model/${overlayId}?preview=race&fixture=${fixtureVariant}`)).model;
      const contract = buildOverlayEvidenceContract(model, { overlayId, previewMode: 'race', fixtureVariant });
      const failures = validateOverlayEvidenceContract(contract, { requireSyntheticStateKind: true });

      expect.soft(failureMessages(failures), `${overlayId}/${fixtureVariant}: evidence contract failures`).toEqual([]);
    }
  });

  it('can compare normalized contracts without waiting for manifest parity artifacts', async () => {
    const contracts = [];
    for (const overlayId of ['standings', 'relative']) {
      const model = (await reviewServer.getJson(`/api/overlay-model/${overlayId}?preview=race`)).model;
      contracts.push(buildOverlayEvidenceContract(model, { overlayId, previewMode: 'race' }));
    }

    expect.soft(failureMessages(compareOverlayEvidenceContracts(contracts))).toEqual([]);
  });
});
