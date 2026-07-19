const FIRST_REMOTE_RELEASE_CAPABILITY = 'Active team car';

/**
 * This is deliberately a small, public-safe description of contract outcomes.
 * It is not a serialized CBOR fixture, an application export, or a live Bridge
 * packet. Keeping it here makes the browser workbench useful without granting
 * it access to the app's review state, captures, or a relay.
 */
const fixtureCases = deepFreeze({
  'active-team-live': {
    label: 'Active team car — current sector publication',
    producer: {
      identity: 'fixture-publisher-a',
      publication: 'Complete sector publication',
      fixtureId: 'active-team-live-v1',
      header: [
        ['Contract', 'Bridge fact schema 1.0'],
        ['Room', 'fixture-room-01'],
        ['Team-car stream', 'fixture-team-car-01'],
        ['Session epoch / sequence', '4 / 84'],
        ['Sector / published', '3 / 10:14:02 UTC']
      ],
      capabilities: [FIRST_REMOTE_RELEASE_CAPABILITY],
      payload: [
        'Active team-car fact group',
        'Fuel envelope and bounded clean-burn evidence',
        'Pit/service/repair state and team-car progress'
      ]
    },
    consumer: {
      outcome: 'Accepted complete publication',
      outcomeTone: 'success',
      coreBoundary: 'Synthetic contract fixture would enter the remote fact store.',
      availability: [
        ['Active team car', 'Available'],
        ['Race context', 'Not granted'],
        ['Environment', 'Not granted'],
        ['Spatial traffic', 'Not granted']
      ],
      provenance: [
        ['Fixture truth', 'synthetic-contract-fixture'],
        ['Source / cadence', 'Fixture publisher / sector 3'],
        ['Publication age', '00:00:04'],
        ['Receiver state', 'Current']
      ]
    }
  },
  'publisher-no-publication': {
    label: 'Publisher has no eligible publication',
    producer: {
      identity: 'fixture-publisher-a',
      publication: 'No publication emitted',
      fixtureId: 'publisher-no-publication-v1',
      header: [
        ['Contract', 'Bridge fact schema 1.0'],
        ['Room', 'fixture-room-01'],
        ['Team-car stream', 'fixture-team-car-01'],
        ['Publisher eligibility', 'Pending direct in-car confirmation'],
        ['Publication', 'No complete sector snapshot']
      ],
      capabilities: [FIRST_REMOTE_RELEASE_CAPABILITY],
      payload: [
        'No payload summary: a complete source snapshot is required',
        'No held fact is replaced by this fixture'
      ]
    },
    consumer: {
      outcome: 'No remote fact accepted',
      outcomeTone: 'neutral',
      coreBoundary: 'Synthetic contract fixture leaves the remote fact store unchanged.',
      availability: [
        ['Active team car', 'Awaiting a complete publication'],
        ['Race context', 'Not granted'],
        ['Environment', 'Not granted'],
        ['Spatial traffic', 'Not granted']
      ],
      provenance: [
        ['Fixture truth', 'synthetic-contract-fixture'],
        ['Source state', 'No eligible publisher'],
        ['Receiver state', 'Waiting for a complete snapshot'],
        ['Stored facts', 'Unchanged']
      ]
    }
  },
  'group-unavailable': {
    label: 'Known group unavailable in a complete publication',
    producer: {
      identity: 'fixture-publisher-a',
      publication: 'Complete sector publication',
      fixtureId: 'group-unavailable-v1',
      header: [
        ['Contract', 'Bridge fact schema 1.0'],
        ['Room', 'fixture-room-01'],
        ['Team-car stream', 'fixture-team-car-01'],
        ['Session epoch / sequence', '4 / 85'],
        ['Declared unavailable group', 'Active team car']
      ],
      capabilities: [FIRST_REMOTE_RELEASE_CAPABILITY],
      payload: [
        'Active team-car group declared unavailable',
        'No active team-car fields are supplied',
        'All ungranted groups remain absent'
      ]
    },
    consumer: {
      outcome: 'Accepted with declared group unavailable',
      outcomeTone: 'success',
      coreBoundary: 'Synthetic contract fixture records group availability without inventing missing facts.',
      availability: [
        ['Active team car', 'Declared unavailable'],
        ['Environment', 'Not granted'],
        ['Race context', 'Not granted'],
        ['Spatial traffic', 'Not granted']
      ],
      provenance: [
        ['Fixture truth', 'synthetic-contract-fixture'],
        ['Source / cadence', 'Fixture publisher / sector 3'],
        ['Publication age', '00:00:04'],
        ['Receiver state', 'Current publication; active group unavailable']
      ]
    }
  },
  'capability-not-negotiated': {
    label: 'Capability requested but not negotiated',
    producer: {
      identity: 'fixture-publisher-a',
      publication: 'Complete sector publication',
      fixtureId: 'capability-not-negotiated-v1',
      header: [
        ['Contract', 'Bridge fact schema 1.0'],
        ['Room', 'fixture-room-01'],
        ['Team-car stream', 'fixture-team-car-01'],
        ['Session epoch / sequence', '4 / 86'],
        ['Requested capability', 'Spatial traffic']
      ],
      capabilities: [FIRST_REMOTE_RELEASE_CAPABILITY],
      payload: [
        'Active team-car fact group',
        'Spatial traffic is omitted because it was not negotiated',
        'No wider-field facts are represented'
      ]
    },
    consumer: {
      outcome: 'Accepted negotiated facts only',
      outcomeTone: 'success',
      coreBoundary: 'Synthetic contract fixture stores only the negotiated active team-car group.',
      availability: [
        ['Active team car', 'Available'],
        ['Spatial traffic', 'Not negotiated'],
        ['Race context', 'Not granted'],
        ['Environment', 'Not granted']
      ],
      provenance: [
        ['Fixture truth', 'synthetic-contract-fixture'],
        ['Negotiated scope', FIRST_REMOTE_RELEASE_CAPABILITY],
        ['Receiver state', 'Current'],
        ['Wider field', 'Not represented']
      ]
    }
  },
  'malformed-cbor': {
    label: 'Malformed canonical payload classification',
    producer: {
      identity: 'fixture-publisher-a',
      publication: 'Rejected fixture input',
      fixtureId: 'malformed-cbor-v1',
      header: [
        ['Contract', 'Bridge fact schema 1.0'],
        ['Room', 'fixture-room-01'],
        ['Team-car stream', 'fixture-team-car-01'],
        ['Input classification', 'Malformed canonical payload'],
        ['Decoded field values', 'Not displayed']
      ],
      capabilities: [FIRST_REMOTE_RELEASE_CAPABILITY],
      payload: [
        'No decoded payload is displayed',
        'Fixture carries only the safe rejection classification'
      ]
    },
    consumer: {
      outcome: 'Rejected: malformed payload',
      outcomeTone: 'error',
      coreBoundary: 'Synthetic contract fixture classifies a decoder rejection. It does not exercise receiver-store invalidation.',
      availability: [
        ['Active team car', 'Decoder rejection; store policy not exercised'],
        ['Race context', 'Not granted'],
        ['Environment', 'Not granted'],
        ['Spatial traffic', 'Not granted']
      ],
      provenance: [
        ['Fixture truth', 'synthetic-contract-fixture'],
        ['Safe error class', 'Malformed canonical payload'],
        ['Decoder state', 'Rejected before admission'],
        ['Stored facts', 'Not asserted by this fixture']
      ]
    }
  },
  'stale-publication': {
    label: 'Complete publication with an aged receiver receipt',
    producer: {
      identity: 'fixture-publisher-a',
      publication: 'Previously complete sector publication',
      fixtureId: 'stale-publication-v1',
      header: [
        ['Contract', 'Bridge fact schema 1.0'],
        ['Room', 'fixture-room-01'],
        ['Team-car stream', 'fixture-team-car-01'],
        ['Session epoch / sequence', '4 / 81'],
        ['Publication age', '00:01:36']
      ],
      capabilities: [FIRST_REMOTE_RELEASE_CAPABILITY],
      payload: [
        'Last complete active team-car fact group',
        'No extrapolated updates',
        'Freshness decision belongs to the consuming overlay'
      ]
    },
    consumer: {
      outcome: 'Accepted history; overlay freshness decision pending',
      outcomeTone: 'warning',
      coreBoundary: 'Synthetic contract fixture shows aged receipt metadata only. Each consuming overlay independently decides presentation and calculation influence.',
      availability: [
        ['Active team car', 'Aged receipt; policy decision pending'],
        ['Race context', 'Not granted'],
        ['Environment', 'Not granted'],
        ['Spatial traffic', 'Not granted']
      ],
      provenance: [
        ['Fixture truth', 'synthetic-contract-fixture'],
        ['Publication age', '00:01:36'],
        ['Receiver state', 'Accepted history retained'],
        ['Overlay decision', 'Not made by this workbench']
      ]
    }
  },
  'sequence-regression': {
    label: 'Sequence regression',
    producer: {
      identity: 'fixture-publisher-a',
      publication: 'Rejected fixture input',
      fixtureId: 'sequence-regression-v1',
      header: [
        ['Contract', 'Bridge fact schema 1.0'],
        ['Room', 'fixture-room-01'],
        ['Team-car stream', 'fixture-team-car-01'],
        ['Session epoch / sequence', '4 / 83 after 84'],
        ['Input classification', 'Sequence regression']
      ],
      capabilities: [FIRST_REMOTE_RELEASE_CAPABILITY],
      payload: [
        'No newer fact replaces the accepted sequence',
        'Fixture carries only the safe rejection classification'
      ]
    },
    consumer: {
      outcome: 'Rejected: sequence regression',
      outcomeTone: 'error',
      coreBoundary: 'Synthetic contract fixture retains the prior accepted facts only for explanation and marks them unusable for calculation.',
      availability: [
        ['Active team car', 'Prior fact retained; calculation unavailable'],
        ['Race context', 'Not granted'],
        ['Environment', 'Not granted'],
        ['Spatial traffic', 'Not granted']
      ],
      provenance: [
        ['Fixture truth', 'synthetic-contract-fixture'],
        ['Safe error class', 'Sequence regression'],
        ['Receiver state', 'Rejected'],
        ['Stored facts', 'Retained only; sequence rejected']
      ]
    }
  },
  'session-epoch-change': {
    label: 'New session epoch',
    producer: {
      identity: 'fixture-publisher-b',
      publication: 'Complete epoch-start publication',
      fixtureId: 'session-epoch-change-v1',
      header: [
        ['Contract', 'Bridge fact schema 1.0'],
        ['Room', 'fixture-room-01'],
        ['Team-car stream', 'fixture-team-car-01'],
        ['Session epoch / sequence', '5 / 1'],
        ['Publication', 'First complete snapshot for new epoch']
      ],
      capabilities: [FIRST_REMOTE_RELEASE_CAPABILITY],
      payload: [
        'Active team-car fact group',
        'Full snapshot required at epoch change',
        'Previous epoch is not blended'
      ]
    },
    consumer: {
      outcome: 'Accepted new epoch snapshot',
      outcomeTone: 'success',
      coreBoundary: 'Synthetic contract fixture shows the accepted rollover path after local expected-session confirmation; the prior epoch is then cleared before acceptance.',
      availability: [
        ['Active team car', 'Available in epoch 5'],
        ['Race context', 'Not granted'],
        ['Environment', 'Not granted'],
        ['Spatial traffic', 'Not granted']
      ],
      provenance: [
        ['Fixture truth', 'synthetic-contract-fixture'],
        ['Source / cadence', 'Fixture publisher / initial snapshot'],
        ['Prior epoch', 'Cleared only after local expected-session confirmation'],
        ['Receiver state', 'Current']
      ]
    }
  }
});

export const bridgeWorkbenchCaseIds = Object.freeze(Object.keys(fixtureCases));

export function bridgeWorkbenchFixture(caseId) {
  return fixtureCases[caseId] || fixtureCases['active-team-live'];
}

export function isBridgeWorkbenchCase(caseId) {
  return Object.hasOwn(fixtureCases, caseId);
}

function deepFreeze(value) {
  if (!value || typeof value !== 'object' || Object.isFrozen(value)) return value;
  Object.freeze(value);
  for (const nested of Object.values(value)) {
    deepFreeze(nested);
  }
  return value;
}
