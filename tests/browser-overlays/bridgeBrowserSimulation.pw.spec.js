import { expect, test } from '@playwright/test';
import {
  bridgeBrowserSimulationCaseIds,
  bridgeBrowserSimulationFixture
} from '../../tools/browser-review/bridge-browser-simulation/fixtures.mjs';
import { startReviewServer } from './reviewServerTestHost.js';

let reviewServer;

test.beforeAll(async () => {
  reviewServer = await startReviewServer();
}, 10000);

test.afterAll(async () => {
  await reviewServer?.stop();
});

test.describe('Overlay Bridge browser-only local simulation', () => {
  test('browser-to-browser fixture events retain each expected Core priority decision without persistent shared state', async ({ browser }) => {
    const context = await browser.newContext();
    const receiver = await context.newPage();
    const producer = await context.newPage();

    await receiver.goto(`${reviewServer.baseUrl}/review/bridge/local/receiver`);
    await expect(receiver.locator('#receiver-status')).toContainText('Ready: waiting for a synthetic fixture event');

    for (const caseId of bridgeBrowserSimulationCaseIds) {
      const fixture = bridgeBrowserSimulationFixture(caseId);
      await producer.goto(`${reviewServer.baseUrl}/review/bridge/local/producer?case=${encodeURIComponent(caseId)}`);
      await expect(producer.locator('[data-bridge-browser-simulation="producer"]')).toHaveAttribute('data-fixture-case', caseId);
      await producer.getByRole('button', { name: 'Publish selected synthetic fixture' }).click();

      await expect(receiver.locator('#receiver-status')).toContainText('Received one synthetic fixture event');
      await expect(receiver.locator('#receiver-decision')).toContainText(`${fixture.expectedDecision.availability} • ${fixture.expectedDecision.source}`);
      await expect(receiver.locator('#receiver-decision')).toContainText(fixture.expectedDecision.calculation);
      await expect(receiver.locator('#receiver-decision')).toContainText(fixture.expectedDecision.mergePolicy);
      await expect(receiver.locator('#receiver-decision')).toContainText(fixture.expectedDecision.reason);
      await expect(receiver.locator('#receiver-metadata')).toContainText(caseId);
    }

    expect(await receiver.evaluate(() => ({
      localStorageKeys: Object.keys(localStorage),
      sessionStorageKeys: Object.keys(sessionStorage),
      documentCookie: document.cookie
    }))).toEqual({
      localStorageKeys: [],
      sessionStorageKeys: [],
      documentCookie: ''
    });

    await receiver.reload();
    await expect(receiver.locator('#receiver-status')).toContainText(
      'Ready: waiting for a synthetic fixture event');
    await expect(receiver.locator('#receiver-decision')).toContainText('Awaiting fixture event');

    await context.close();
  });
});
