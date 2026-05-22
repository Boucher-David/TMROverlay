import { describe, expect, it } from 'vitest';
import {
  buildOverlayEvidenceContract,
  compareOverlayEvidenceContracts,
  failureMessages,
  validateOverlayEvidenceContract
} from '../../tools/validation/overlay-evidence-contract.mjs';

describe('overlay evidence contract unit failures', () => {
  it('reports schema, unsupported overlay, and formatted failure messages', () => {
    const contract = validContract('relative');
    contract.contract = 'overlay-evidence-contract/old';
    contract.overlayId = 'unsupported-overlay';
    contract.previewMode = '';

    const messages = failureMessages(validateOverlayEvidenceContract(contract));

    expect(messages).toEqual(expect.arrayContaining([
      'unsupported-overlay [schema]: expected contract overlay-evidence-contract/v1',
      'unsupported-overlay [schema]: unsupported overlay id unsupported-overlay',
      'unsupported-overlay [schema]: missing previewMode'
    ]));
  });

  it('reports runtime route, hash, pixel evidence, and fingerprint failures', () => {
    const contract = validContract('relative');
    contract.runtimeSurfaces.browserReview.routePath = '/wrong/review-route';
    contract.runtimeSurfaces.browserReview.sharedSettingsHash = '';
    contract.runtimeSurfaces.localhostObs.overlaySettingsHash = 'different-overlay-hash';
    contract.runtimeSurfaces.windowsNative.pixelEvidence = {};

    const messages = failureMessages(validateOverlayEvidenceContract(contract));

    expect(messages).toEqual(expect.arrayContaining([
      'relative [settings-fingerprint]: browserReview missing sharedSettingsHash',
      'relative [runtime-surface]: browserReview routePath expected /review/overlays/relative, got /wrong/review-route',
      'relative [runtime-surface]: windowsNative missing pixelEvidence.status',
      'relative [runtime-surface]: windowsNative missing pixelEvidence.reason'
    ]));
    expect(messages).toContainEqual(expect.stringMatching(
      /^relative \[settings-fingerprint\]: runtime settings fingerprints differ:/
    ));
  });

  it('reports table column, row, and placeholder mismatches', () => {
    const contract = validContract('relative', {
      columns: [{ label: 'Pos', dataKey: 'position' }],
      rows: [{ cells: ['1', 'Reference Driver'], isReference: true }],
      effectiveSettings: {
        rendered: {
          columnKeys: ['driver'],
          rowIdentities: ['row|Other Driver||'],
          placeholderRowCount: 2
        }
      }
    });

    const messages = failureMessages(validateOverlayEvidenceContract(contract));

    expect(messages).toEqual(expect.arrayContaining([
      'relative [table-contract]: rendered columnKeys differ from model columns: driver',
      'relative [table-contract]: rendered rowIdentities differ from model rows',
      'relative [table-contract]: placeholderRowCount expected 0, got 2'
    ]));
  });

  it('reports unavailable content policy and invalid role context failures', () => {
    const contract = validContract('relative', {
      status: 'waiting for telemetry',
      rows: [{ cells: ['stale content'] }],
      effectiveSettings: {
        rendered: {
          columnKeys: [],
          rowIdentities: [],
          placeholderRowCount: 0,
          unavailableContentPolicy: 'section-aware-placeholders'
        }
      }
    });
    contract.semanticModel.roleContext = {
      localRole: 'crew-chief',
      roleSource: '',
      isSpotting: 'sometimes',
      playerIsSpectator: 'no',
      focusIsSpectator: 1
    };

    const messages = failureMessages(validateOverlayEvidenceContract(contract));

    expect(messages).toEqual(expect.arrayContaining([
      'relative [unavailable-content]: section-aware unavailable placeholders are only defined for Session / Weather',
      'relative [role-context]: localRole must be driver, spectator, or unknown; got crew-chief',
      'relative [role-context]: missing roleSource',
      'relative [role-context]: isSpotting must be boolean or null',
      'relative [role-context]: playerIsSpectator must be boolean or null',
      'relative [role-context]: focusIsSpectator must be boolean or null'
    ]));
  });

  it('reports standings-specific evidence failures', () => {
    const contract = validContract('standings', {
      columns: [{ label: 'Pos', dataKey: 'position' }],
      rows: [{ cells: ['1', 'Leader'] }],
      effectiveSettings: {
        rendered: {
          columnKeys: ['position'],
          rowIdentities: ['row|1/Leader||'],
          placeholderRowCount: 0,
          tableStatus: {
            dataRowCount: 1,
            classHeaderCount: 0,
            placeholderRowCount: 0,
            clippedRowCount: 2,
            statusCarCount: 1
          },
          timingSanity: { absurdIntervalCount: 1 }
        }
      }
    });

    const messages = failureMessages(validateOverlayEvidenceContract(contract));

    expect(messages).toEqual(expect.arrayContaining([
      'standings [standings-contract]: tableStatus reports 2 clipped rows',
      'standings [standings-contract]: timingSanity missing maxIntervalGapRatio',
      'standings [standings-contract]: timingSanity absurdIntervalCount expected 0, got 1'
    ]));
  });

  it('reports fuel evidence and metric-density failures', () => {
    const contract = validContract('fuel-calculator', {
      metrics: [{ label: 'Need Covered', value: 'Covered' }],
      effectiveSettings: {
        rendered: {
          fuelStrategy: {
            additionalFuelNeedState: 'estimated',
            successCopyRequiresMeasuredNeed: false
          },
          layout: {
            contentRowCount: 1,
            unusedHeightRatio: 0.5
          }
        }
      }
    });

    const messages = failureMessages(validateOverlayEvidenceContract(contract));

    expect(messages).toEqual(expect.arrayContaining([
      'fuel-calculator [fuel-contract]: fuelStrategy missing additionalFuelNeedState',
      'fuel-calculator [fuel-contract]: fuelStrategy missing successCopyRequiresMeasuredNeed=true',
      'fuel-calculator [fuel-contract]: renders Covered without measured additional-fuel-need evidence',
      'fuel-calculator [layout-density]: unusedHeightRatio 0.5 exceeds 0.35'
    ]));
  });

  it('reports input, track-map, and synthetic-state fixture failures', () => {
    const inputContract = validContract('input-state', {
      inputs: { isAvailable: false, tracePointCount: 0 },
      effectiveSettings: { rendered: { inputAvailability: { fixtureControlsAvailable: false } } }
    }, { fixtureVariant: 'input-state-mock-data' });
    const trackMapContract = validContract('track-map', {
      status: 'live telemetry',
      trackMap: { mapKind: 'circle', markerCount: 1 },
      effectiveSettings: { rendered: { mapFallback: { kind: 'rectangle', reason: '' } } }
    });

    const messages = failureMessages([
      ...validateOverlayEvidenceContract(inputContract, { requireSyntheticStateKind: true }),
      ...validateOverlayEvidenceContract(trackMapContract)
    ]);

    expect(messages).toEqual(expect.arrayContaining([
      'input-state [input-contract]: input-state-mock-data fixture missing explicit mock-data provenance',
      'input-state [input-contract]: missing fixtureControlsAvailable evidence',
      'input-state [input-contract]: input preview is unavailable despite fixture controls requirement',
      'input-state [input-contract]: input preview missing non-empty trace evidence',
      'input-state [synthetic-state]: fixture input-state-mock-data missing syntheticStateKind provenance',
      'track-map [track-map-contract]: circle map missing mapFallback provenance',
      'track-map [track-map-contract]: circle map missing fallback reason',
      'track-map [track-map-contract]: circle fallback is labelled live'
    ]));
  });

  it('reports cross-contract fingerprint and row mismatches', () => {
    const left = validContract('relative', {
      columns: [{ label: 'Driver', dataKey: 'driver' }],
      rows: [{ cells: ['Leader'] }],
      effectiveSettings: {
        rendered: {
          columnKeys: ['driver'],
          rowIdentities: ['row|Leader||'],
          placeholderRowCount: 0
        }
      }
    });
    const right = validContract('relative', {
      columns: [{ label: 'Driver', dataKey: 'driver' }],
      rows: [{ cells: ['Chaser'] }],
      effectiveSettings: {
        sources: runtimeSources('relative', { sharedSettingsHash: 'shared-b', overlaySettingsHash: 'overlay-b' }),
        rendered: {
          columnKeys: ['driver'],
          rowIdentities: ['row|Chaser||'],
          placeholderRowCount: 0
        }
      }
    });

    const messages = failureMessages(compareOverlayEvidenceContracts([left, right]));

    expect(messages).toEqual(expect.arrayContaining([
      'relative:race:default [cross-surface-parity]: settings fingerprints differ across evidence contracts: shared-a:overlay-a, shared-b:overlay-b',
      'relative:race:default [cross-surface-parity]: row identity evidence differs across evidence contracts'
    ]));
  });
});

function validContract(overlayId, modelOverrides = {}, options = {}) {
  return buildOverlayEvidenceContract(validModel(overlayId, modelOverrides), {
    overlayId,
    previewMode: 'race',
    ...options
  });
}

function validModel(overlayId, overrides = {}) {
  const effectiveSettings = mergeEffectiveSettings(overlayId, overrides.effectiveSettings);
  const model = {
    overlayId,
    bodyKind: 'metrics',
    status: 'ready',
    effectiveSettings,
    ...overrides
  };
  model.effectiveSettings = effectiveSettings;
  return model;
}

function mergeEffectiveSettings(overlayId, overrides = {}) {
  return {
    ...overrides,
    sources: {
      ...runtimeSources(overlayId),
      ...(overrides.sources || {})
    },
    rendered: {
      provenance: {
        evidenceClass: 'synthetic-preview',
        captureSpecific: false,
        sourceContract: 'unit-test',
        ...(overrides.rendered?.provenance || {})
      },
      roleContext: {
        localRole: 'driver',
        roleSource: 'unit-test',
        isSpotting: null,
        ...(overrides.rendered?.roleContext || {})
      },
      ...(overrides.rendered || {})
    }
  };
}

function runtimeSources(overlayId = 'unit', hashOverrides = {}) {
  const hashes = {
    sharedSettingsHash: 'shared-a',
    overlaySettingsHash: 'overlay-a',
    ...hashOverrides
  };
  return {
    browserReview: {
      applied: true,
      routePath: `/review/overlays/${overlayId}`,
      ...hashes
    },
    localhostObs: {
      applied: true,
      routePath: `/overlays/${overlayId}`,
      ...hashes
    },
    windowsNative: {
      applied: true,
      routePath: `native://${overlayId}`,
      pixelEvidence: { status: 'captured', reason: 'unit fixture' },
      ...hashes
    }
  };
}
