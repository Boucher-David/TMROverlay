import { afterAll, beforeAll, describe, expect, it } from 'vitest';
import { readFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import {
  bridgeBrowserSimulationCaseIds,
  bridgeBrowserSimulationChannelName,
  bridgeBrowserSimulationEnvelope,
  bridgeBrowserSimulationFixture,
  bridgeBrowserSimulationFixtureTruth,
  isBridgeBrowserSimulationCase
} from '../../tools/browser-review/bridge-browser-simulation/fixtures.mjs';
import { startReviewServer } from './reviewServerTestHost.js';

let reviewServer;
const repoRoot = resolve(dirname(fileURLToPath(import.meta.url)), '../..');

beforeAll(async () => {
  reviewServer = await startReviewServer();
}, 10000);

afterAll(async () => {
  await reviewServer?.stop();
});

describe('Overlay Bridge browser-only local simulation', () => {
  it('keeps every synthetic producer/receiver priority source fixture deeply frozen and public-safe', () => {
    expect(bridgeBrowserSimulationCaseIds).toEqual([
      'current-remote',
      'held-remote',
      'expired-remote',
      'direct-local-precedence',
      'tombstone',
      'session-mismatch',
      'sequence-rejection'
    ]);
    expect(bridgeBrowserSimulationChannelName).toBe('tmr-overlay-bridge-browser-only-fixture-v1');
    expect(bridgeBrowserSimulationFixtureTruth).toBe('synthetic-browser-simulation-fixture');
    expect(isBridgeBrowserSimulationCase('current-remote')).toBe(true);
    expect(isBridgeBrowserSimulationCase('not-a-fixture')).toBe(false);

    for (const caseId of bridgeBrowserSimulationCaseIds) {
      const fixture = bridgeBrowserSimulationFixture(caseId);
      const envelope = bridgeBrowserSimulationEnvelope(caseId);
      expect(Object.isFrozen(fixture), caseId).toBe(true);
      expect(Object.isFrozen(fixture.expectedDecision), caseId).toBe(true);
      expect(Object.isFrozen(envelope), caseId).toBe(true);
      expect(envelope).toMatchObject({
        fixtureTruth: bridgeBrowserSimulationFixtureTruth,
        channel: bridgeBrowserSimulationChannelName,
        caseId,
        expectedDecision: fixture.expectedDecision
      });
      expect(fixture.producerSummary, caseId).not.toMatch(/telemetry\.bin|capture-manifest|room invite|secret|token/i);
    }
  });

  it('records the complete expected Core priority decision matrix without implementing Core in JavaScript', () => {
    const expected = Object.fromEntries(bridgeBrowserSimulationCaseIds.map((caseId) => {
      const decision = bridgeBrowserSimulationFixture(caseId).expectedDecision;
      return [caseId, [decision.availability, decision.source, decision.calculation, decision.mergePolicy, decision.reason]];
    }));

    expect(expected).toEqual({
      'current-remote': [
        'Current',
        'Remote Bridge',
        'Eligible: one complete remote fact group',
        'Atomic remote group only; do not scalar-merge with local telemetry',
        'Accepted current publication within the receiver freshness window'
      ],
      'held-remote': [
        'Held',
        'Remote Bridge',
        'Ineligible: held data must not influence calculation',
        'No fact fields exposed; preserve provenance only',
        'Receipt is past current freshness but inside the held display window'
      ],
      'expired-remote': [
        'Unavailable',
        'Remote Bridge',
        'Ineligible: expired remote facts are inaccessible to calculation',
        'Retain remote provenance only; no remote fact fields are exposed or blended',
        'Receiver freshness expired'
      ],
      'direct-local-precedence': [
        'Unavailable',
        'Direct Local Telemetry',
        'Remote input ineligible while direct local telemetry is authoritative',
        'Direct local wins atomically; never blend local and remote scalar fields',
        'Direct local telemetry authoritative'
      ],
      tombstone: [
        'Unavailable',
        'Remote Bridge',
        'Ineligible: terminal remote facts are inaccessible to calculation',
        'Retain remote provenance only; no facts are exposed or scalar-blended after terminal state',
        'Receiver terminal tombstone'
      ],
      'session-mismatch': [
        'Current',
        'Remote Bridge',
        'Eligible: prior complete remote fact group remains current',
        'Rejected candidate contributes no fields or freshness; retain the prior atomic remote group',
        'Expected-session mismatch; prior accepted receipt remains current'
      ],
      'sequence-rejection': [
        'Current',
        'Remote Bridge',
        'Eligible: prior complete remote fact group remains current',
        'Rejected candidate contributes no fields or freshness; retain the prior atomic remote group',
        'Sequence regression; prior accepted receipt remains current'
      ]
    });
  });

  it('serves separate developer-only launcher, producer, and receiver routes with bounded browser script policy', async () => {
    const launcher = await reviewServer.getText('/review/bridge/local/workbench?case=current-remote');
    const producer = await reviewServer.getText('/review/bridge/local/producer?case=current-remote');
    const receiver = await reviewServer.getText('/review/bridge/local/receiver');
    const response = await fetch(`${reviewServer.baseUrl}/review/bridge/local/producer?case=current-remote`);

    expect(launcher).toContain('Browser-to-browser Bridge simulation');
    expect(launcher).toContain('/review/bridge/local/receiver?case=current-remote');
    expect(launcher).toContain('/review/bridge/local/producer?case=current-remote');
    expect(producer).toContain('Local fixture producer');
    expect(producer).toContain('Publish selected synthetic fixture');
    expect(receiver).toContain('Local fixture receiver');
    expect(receiver).toContain('Expected Core priority decision');
    expect(response.headers.get('content-security-policy')).toBe(
      "default-src 'none'; style-src 'unsafe-inline'; script-src 'unsafe-inline'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'");
    expect(response.headers.get('referrer-policy')).toBe('no-referrer');
  });

  it('limits the browser route to BroadcastChannel fixture delivery and never confuses it with the offline workbench or production runtime', async () => {
    const pages = await Promise.all([
      reviewServer.getText('/review/bridge/local/workbench?case=current-remote'),
      reviewServer.getText('/review/bridge/local/producer?case=current-remote'),
      reviewServer.getText('/review/bridge/local/receiver')
    ]);
    const forbiddenRuntimeTokens = [
      'fetch(',
      'EventSource',
      'WebSocket',
      'localStorage',
      'sessionStorage',
      'indexedDB',
      'caches',
      'serviceWorker',
      'XMLHttpRequest',
      'sendBeacon',
      'document.cookie',
      'reviewAppState',
      '/api/',
      '/overlays/'
    ];

    for (const page of pages) {
      expect(page).toContain('Browser-only local simulation');
      expect(page).not.toContain('Oracle');
      for (const token of forbiddenRuntimeTokens) {
        expect(page).not.toContain(token);
      }
    }
    expect(pages[1]).toContain('BroadcastChannel');
    expect(pages[2]).toContain('BroadcastChannel');

    const renderSource = readFileSync(
      resolve(repoRoot, 'tools/browser-review/bridge-browser-simulation/render.mjs'),
      'utf8');
    expect(renderSource).toContain('Expected Core priority decision — display only');
    expect(renderSource).toContain('JSON.stringify(message) === JSON.stringify(canonical)');
    expect(renderSource).not.toContain('WebSocket');
    expect(renderSource).not.toContain('window.postMessage');
    expect(renderSource).not.toContain('localStorage');
  });

  it('preserves the existing static offline workbench script boundary', async () => {
    const offline = await reviewServer.getText('/review/bridge/workbench?case=active-team-live');
    expect(offline).toContain('Offline fixture evidence.');
    expect(offline).not.toMatch(/<script\b/i);
    expect(offline).not.toContain('BroadcastChannel');
  });
});
