const FIXTURE_TRUTH = 'synthetic-browser-simulation-fixture';
const CHANNEL_NAME = 'tmr-overlay-bridge-browser-only-fixture-v1';

/**
 * Public-safe, deeply frozen source fixtures for exercising browser-to-browser wiring.
 *
 * These are intentionally not a Core fixture export, a CBOR representation,
 * or a telemetry sample. They contain no car identity, numeric telemetry,
 * captures, room invitation, secret, or pairing material. The receiver page
 * displays the expected Core decision written here; it never evaluates Core.
 */
const fixtureCases = deepFreeze({
  'current-remote': fixture({
    caseId: 'current-remote',
    label: 'Current remote Active Team Car publication',
    eventKind: 'complete-sector-publication',
    sequence: '9 / 141',
    producerSummary: 'Complete sector boundary for the active team car.',
    expectedDecision: decision({
      availability: 'Current',
      source: 'Remote Bridge',
      calculation: 'Eligible: one complete remote fact group',
      mergePolicy: 'Atomic remote group only; do not scalar-merge with local telemetry',
      reason: 'Accepted current publication within the receiver freshness window'
    })
  }),
  'held-remote': fixture({
    caseId: 'held-remote',
    label: 'Held remote publication',
    eventKind: 'held-receipt',
    sequence: '9 / 141',
    producerSummary: 'Prior complete sector boundary retained only as receipt provenance.',
    expectedDecision: decision({
      availability: 'Held',
      source: 'Remote Bridge',
      calculation: 'Ineligible: held data must not influence calculation',
      mergePolicy: 'No fact fields exposed; preserve provenance only',
      reason: 'Receipt is past current freshness but inside the held display window'
    })
  }),
  'expired-remote': fixture({
    caseId: 'expired-remote',
    label: 'Expired remote publication',
    eventKind: 'expired-receipt',
    sequence: '9 / 141',
    producerSummary: 'Prior complete sector boundary is beyond its receiver freshness window.',
    expectedDecision: decision({
      availability: 'Unavailable',
      source: 'Remote Bridge',
      calculation: 'Ineligible: expired remote facts are inaccessible to calculation',
      mergePolicy: 'Retain remote provenance only; no remote fact fields are exposed or blended',
      reason: 'Receiver freshness expired'
    })
  }),
  'direct-local-precedence': fixture({
    caseId: 'direct-local-precedence',
    label: 'Direct local telemetry takes priority',
    eventKind: 'complete-sector-publication',
    sequence: '9 / 142',
    producerSummary: 'A current remote publication is available, but the consumer fixture has direct local telemetry.',
    expectedDecision: decision({
      availability: 'Unavailable',
      source: 'Direct Local Telemetry',
      calculation: 'Remote input ineligible while direct local telemetry is authoritative',
      mergePolicy: 'Direct local wins atomically; never blend local and remote scalar fields',
      reason: 'Direct local telemetry authoritative'
    })
  }),
  tombstone: fixture({
    caseId: 'tombstone',
    label: 'Publisher tombstone',
    eventKind: 'terminal-tombstone',
    sequence: '9 / 143',
    producerSummary: 'Publisher ends the active team-car fact group with a terminal lifecycle event.',
    expectedDecision: decision({
      availability: 'Unavailable',
      source: 'Remote Bridge',
      calculation: 'Ineligible: terminal remote facts are inaccessible to calculation',
      mergePolicy: 'Retain remote provenance only; no facts are exposed or scalar-blended after terminal state',
      reason: 'Receiver terminal tombstone'
    })
  }),
  'session-mismatch': fixture({
    caseId: 'session-mismatch',
    label: 'Session mismatch rejection retains prior current group',
    eventKind: 'session-mismatch',
    sequence: '10 / 1',
    producerSummary: 'A complete-looking publication targets a different synthetic session boundary after a current remote group was already accepted.',
    expectedDecision: decision({
      availability: 'Current',
      source: 'Remote Bridge',
      calculation: 'Eligible: prior complete remote fact group remains current',
      mergePolicy: 'Rejected candidate contributes no fields or freshness; retain the prior atomic remote group',
      reason: 'Expected-session mismatch; prior accepted receipt remains current'
    })
  }),
  'sequence-rejection': fixture({
    caseId: 'sequence-rejection',
    label: 'Sequence regression rejection retains prior current group',
    eventKind: 'sequence-regression',
    sequence: '9 / 140 after 141',
    producerSummary: 'An older synthetic sequence arrives after a newer accepted fixture sequence whose remote fact group is still current.',
    expectedDecision: decision({
      availability: 'Current',
      source: 'Remote Bridge',
      calculation: 'Eligible: prior complete remote fact group remains current',
      mergePolicy: 'Rejected candidate contributes no fields or freshness; retain the prior atomic remote group',
      reason: 'Sequence regression; prior accepted receipt remains current'
    })
  })
});

export const bridgeBrowserSimulationCaseIds = Object.freeze(Object.keys(fixtureCases));
export const bridgeBrowserSimulationChannelName = CHANNEL_NAME;
export const bridgeBrowserSimulationFixtureTruth = FIXTURE_TRUTH;

export function bridgeBrowserSimulationFixture(caseId) {
  return fixtureCases[caseId] || fixtureCases['current-remote'];
}

export function isBridgeBrowserSimulationCase(caseId) {
  return Object.hasOwn(fixtureCases, caseId);
}

export function bridgeBrowserSimulationEnvelope(caseId) {
  const fixture = bridgeBrowserSimulationFixture(caseId);
  return deepFreeze({
    kind: 'tmr-overlay-bridge-browser-simulation/v1',
    fixtureTruth: FIXTURE_TRUTH,
    channel: CHANNEL_NAME,
    fixtureId: fixture.id,
    caseId: fixture.caseId,
    eventKind: fixture.eventKind,
    sequence: fixture.sequence,
    producerSummary: fixture.producerSummary,
    expectedDecision: fixture.expectedDecision
  });
}

function fixture({ caseId, label, eventKind, sequence, producerSummary, expectedDecision }) {
  return {
    id: `browser-sim-${caseId}-v1`,
    caseId,
    label,
    eventKind,
    sequence,
    producerSummary,
    expectedDecision
  };
}

function decision({ availability, source, calculation, mergePolicy, reason }) {
  return {
    availability,
    source,
    calculation,
    mergePolicy,
    reason
  };
}

function deepFreeze(value) {
  if (!value || typeof value !== 'object' || Object.isFrozen(value)) return value;
  Object.freeze(value);
  for (const nested of Object.values(value)) {
    deepFreeze(nested);
  }
  return value;
}
