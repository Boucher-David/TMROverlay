import { afterAll, beforeAll, describe, expect, it } from 'vitest';
import { readFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import {
  bridgeWorkbenchCaseIds,
  bridgeWorkbenchFixture,
  isBridgeWorkbenchCase
} from '../../tools/browser-review/bridge-workbench/fixtures.mjs';
import { startReviewServer } from './reviewServerTestHost.js';

let reviewServer;
const repoRoot = resolve(dirname(fileURLToPath(import.meta.url)), '../..');

beforeAll(async () => {
  reviewServer = await startReviewServer();
}, 10000);

afterAll(async () => {
  await reviewServer?.stop();
});

describe('Overlay Bridge local workbench', () => {
  it('keeps the safe contract fixture cases immutable and explicitly synthetic', () => {
    expect(bridgeWorkbenchCaseIds).toEqual([
      'active-team-live',
      'publisher-no-publication',
      'group-unavailable',
      'capability-not-negotiated',
      'malformed-cbor',
      'stale-publication',
      'sequence-regression',
      'session-epoch-change'
    ]);
    expect(isBridgeWorkbenchCase('active-team-live')).toBe(true);
    expect(isBridgeWorkbenchCase('not-a-fixture')).toBe(false);
    expect(Object.isFrozen(bridgeWorkbenchFixture('active-team-live'))).toBe(true);
    expect(Object.isFrozen(bridgeWorkbenchFixture('active-team-live').producer.header)).toBe(true);
  });

  it('serves independent fixture-only producer and consumer documents', async () => {
    const producer = await reviewServer.getText('/review/bridge/producer?case=active-team-live');
    const consumer = await reviewServer.getText('/review/bridge/consumer?case=active-team-live');

    expect(producer).toContain('Producer publication summary');
    expect(producer).toContain('fixture-publisher-a');
    expect(producer).toContain('Safe header summary');
    expect(producer).toContain('Negotiated capabilities');
    expect(producer).toContain('synthetic-contract-fixture');
    expect(consumer).toContain('Consumer Core-boundary outcome');
    expect(consumer).toContain('Accepted complete publication');
    expect(consumer).toContain('Group availability');
    expect(consumer).toContain('Provenance summary');
    expect(consumer).toContain('synthetic-contract-fixture');
    expect(producer).not.toContain('Consumer Core-boundary outcome');
    expect(consumer).not.toContain('Producer publication summary');
  });

  it('presents each deterministic outcome without a live runtime dependency', async () => {
    for (const caseId of bridgeWorkbenchCaseIds) {
      const workbench = await reviewServer.getText(`/review/bridge/workbench?case=${caseId}`);
      const producer = await reviewServer.getText(`/review/bridge/producer?case=${caseId}`);
      const consumer = await reviewServer.getText(`/review/bridge/consumer?case=${caseId}`);
      const fixture = bridgeWorkbenchFixture(caseId);

      expect.soft(workbench, caseId).toContain('Offline fixture evidence.');
      expect.soft(workbench, caseId).toContain('No connection');
      expect.soft(workbench, caseId).toContain('Oracle not configured');
      expect.soft(workbench, caseId).toContain(`/review/bridge/producer?case=${caseId}`);
      expect.soft(workbench, caseId).toContain(`/review/bridge/consumer?case=${caseId}`);
      expect.soft(workbench, caseId).toContain('sandbox');
      expect.soft(producer, caseId).toContain(fixture.producer.publication);
      expect.soft(consumer, caseId).toContain(fixture.consumer.outcome);
      expect.soft(consumer, caseId).toContain(fixture.consumer.coreBoundary);
    }
  });

  it('does not overstate decoder, freshness, group-availability, or session-rollover behavior', () => {
    const malformed = bridgeWorkbenchFixture('malformed-cbor').consumer;
    const aged = bridgeWorkbenchFixture('stale-publication').consumer;
    const unavailable = bridgeWorkbenchFixture('group-unavailable').consumer;
    const rollover = bridgeWorkbenchFixture('session-epoch-change').consumer;

    expect(malformed.coreBoundary).toContain('does not exercise receiver-store invalidation');
    expect(malformed.provenance).toContainEqual(['Stored facts', 'Not asserted by this fixture']);
    expect(aged.outcome).toContain('overlay freshness decision pending');
    expect(aged.provenance).toContainEqual(['Overlay decision', 'Not made by this workbench']);
    expect(unavailable.provenance).toContainEqual(['Receiver state', 'Current publication; active group unavailable']);
    expect(rollover.coreBoundary).toContain('after local expected-session confirmation');
  });

  it('does not ship live-state, socket, event, message, or review API runtime hooks', async () => {
    const pages = await Promise.all([
      reviewServer.getText('/review/bridge/workbench?case=active-team-live'),
      reviewServer.getText('/review/bridge/producer?case=active-team-live'),
      reviewServer.getText('/review/bridge/consumer?case=active-team-live')
    ]);
    const forbiddenRuntimeTokens = [
      'fetch(',
      'EventSource',
      'WebSocket',
      'postMessage',
      'reviewAppState',
      '/api/bridge',
      '/api/snapshot',
      '/api/overlay-model',
      '/api/review/settings'
    ];

    for (const page of pages) {
      for (const token of forbiddenRuntimeTokens) {
        expect(page).not.toContain(token);
      }
      expect(page).not.toMatch(/<script\b/i);
    }
  });

  it('keeps the server route and fixture modules inside the offline-only import boundary', () => {
    const fixturesSource = readFileSync(
      resolve(repoRoot, 'tools/browser-review/bridge-workbench/fixtures.mjs'),
      'utf8');
    const renderSource = readFileSync(
      resolve(repoRoot, 'tools/browser-review/bridge-workbench/render.mjs'),
      'utf8');
    const serverSource = readFileSync(
      resolve(repoRoot, 'tools/browser-review/server.mjs'),
      'utf8');
    const routeBranch = serverSource.slice(
      serverSource.indexOf('const bridgeWorkbenchRoute = bridgeWorkbenchRouteFromPath(path);'),
      serverSource.indexOf('const overlayId = overlayIdFromPath(path);'));
    const forbiddenTokens = [
      'fetch(',
      'EventSource',
      'WebSocket',
      'reviewAppState',
      'browserOverlayApiResponse',
      '/api/'
    ];

    expect(renderSource).toContain("from './fixtures.mjs'");
    expect(routeBranch).toContain('renderBridgeWorkbenchHtml');
    expect(routeBranch).toContain("url.searchParams.get('case')");
    for (const source of [fixturesSource, renderSource, routeBranch]) {
      for (const token of forbiddenTokens) {
        expect(source).not.toContain(token);
      }
    }
  });

  it('uses restrictive document headers and enough embedded-page height for the complete fixture evidence', async () => {
    const response = await fetch(`${reviewServer.baseUrl}/review/bridge/workbench?case=active-team-live`);
    const workbench = await response.text();
    const renderSource = readFileSync(
      resolve(repoRoot, 'tools/browser-review/bridge-workbench/render.mjs'),
      'utf8');

    expect(response.headers.get('content-security-policy')).toBe(
      "default-src 'none'; style-src 'unsafe-inline'; frame-src 'self'; base-uri 'none'; form-action 'none'; frame-ancestors 'self'");
    expect(response.headers.get('referrer-policy')).toBe('no-referrer');
    expect(workbench).toContain('title="Producer fixture document"');
    expect(workbench).toContain('title="Consumer fixture document"');
    expect(renderSource).toContain('height: 840px');
    expect(await reviewServer.getText('/review/bridge/consumer?case=active-team-live')).toContain('Receiver state');
  });
});
