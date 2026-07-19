export const overlayEvidenceContractVersion = 'overlay-evidence-contract/v1';

export const supportedOverlayIds = Object.freeze([
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
]);

const runtimeSurfaceNames = Object.freeze(['browserReview', 'localhostObs', 'windowsNative']);
const ordinaryOverlayIds = new Set(supportedOverlayIds.filter((overlayId) => overlayId !== 'garage-cover'));
const tableOverlayIds = new Set(['standings', 'relative']);
const metricDensityOverlayIds = new Set(['fuel-calculator', 'pit-service']);

export function buildOverlayEvidenceContract(model, options = {}) {
  const overlayId = options.overlayId || model?.overlayId || 'unknown';
  const previewMode = options.previewMode || 'race';
  const fixtureVariant = options.fixtureVariant || model?.fixtureVariant || null;
  const effectiveSettings = model?.effectiveSettings || {};
  const rendered = effectiveSettings.rendered || {};

  return {
    contract: overlayEvidenceContractVersion,
    overlayId,
    previewMode,
    fixtureVariant,
    bodyKind: model?.bodyKind || rendered.bodyKind || null,
    runtimeSurfaces: runtimeSurfaceEvidence(overlayId, effectiveSettings.sources || {}),
    settingsFingerprint: settingsFingerprintEvidence(effectiveSettings.sources || {}),
    provenance: {
      evidenceClass: rendered.provenance?.evidenceClass || null,
      captureSpecific: rendered.provenance?.captureSpecific,
      sourceContract: rendered.provenance?.sourceContract || null,
      syntheticStateKind: rendered.provenance?.syntheticStateKind || null,
      unavailableContentPolicy: rendered.unavailableContentPolicy || null
    },
    semanticModel: {
      title: stringOrNull(model?.title),
      renderedTitle: stringOrNull(model?.renderedTitle),
      status: stringOrNull(model?.status),
      source: stringOrNull(model?.source),
      shouldRender: model?.shouldRender !== false,
      visibleText: visibleModelText(model),
      hasRenderedContent: hasRenderedContent(model),
      columns: tableColumnKeys(model),
      rows: tableRowIdentities(model),
      placeholderRowCount: placeholderRowCount(model),
      renderedColumns: asStringArray(rendered.columnKeys),
      renderedRows: asStringArray(rendered.rowIdentities),
      renderedPlaceholderRowCount: rendered.placeholderRowCount,
      tableStatus: rendered.tableStatus || null,
      timingSanity: rendered.timingSanity || null,
      fuelStrategy: rendered.fuelStrategy || null,
      layout: rendered.layout || null,
      inputAvailability: rendered.inputAvailability || null,
      mapFallback: rendered.mapFallback || null,
      roleContext: roleContextEvidence(rendered.roleContext),
      metricText: metricText(model),
      inputs: inputEvidence(model),
      trackMap: trackMapEvidence(model),
      graph: graphEvidence(model)
    }
  };
}

export function validateOverlayEvidenceContract(contract, options = {}) {
  const failures = [];
  const overlayId = contract?.overlayId || 'unknown';
  if (contract?.contract !== overlayEvidenceContractVersion) {
    failures.push(failure(overlayId, 'schema', `expected contract ${overlayEvidenceContractVersion}`));
  }
  if (!supportedOverlayIds.includes(overlayId)) {
    failures.push(failure(overlayId, 'schema', `unsupported overlay id ${overlayId}`));
  }
  if (!contract?.previewMode) {
    failures.push(failure(overlayId, 'schema', 'missing previewMode'));
  }

  validateNoRedundantTitle(contract, failures);
  validateRuntimeSurfaces(contract, failures);
  validateProvenance(contract, failures);
  validateTableEvidence(contract, failures);
  validateUnavailableContent(contract, failures);
  validateRoleContext(contract, failures);
  validateOverlaySpecificEvidence(contract, failures, options);
  return failures;
}

export function compareOverlayEvidenceContracts(contracts) {
  const failures = [];
  const byOverlayMode = new Map();
  for (const contract of contracts || []) {
    const key = `${contract.overlayId}:${contract.previewMode}:${contract.fixtureVariant || 'default'}`;
    const group = byOverlayMode.get(key) || [];
    group.push(contract);
    byOverlayMode.set(key, group);
  }

  for (const [key, group] of byOverlayMode.entries()) {
    const fingerprints = new Set(group.map((contract) => contract.settingsFingerprint?.combined).filter(Boolean));
    if (fingerprints.size > 1) {
      failures.push(failure(key, 'cross-surface-parity', `settings fingerprints differ across evidence contracts: ${[...fingerprints].join(', ')}`));
    }
    const rowSignatures = new Set(group.map((contract) => (contract.semanticModel?.rows || []).join('\n')));
    if (rowSignatures.size > 1) {
      failures.push(failure(key, 'cross-surface-parity', 'row identity evidence differs across evidence contracts'));
    }
  }

  return failures;
}

export function failureMessages(failures) {
  return (failures || []).map((item) => `${item.overlayId} [${item.family}]: ${item.message}`);
}

function runtimeSurfaceEvidence(overlayId, sources) {
  return {
    browserReview: surfaceEvidence(sources.browserReview, `/review/overlays/${overlayId}`),
    localhostObs: surfaceEvidence(sources.localhostObs, `/overlays/${overlayId}`),
    windowsNative: surfaceEvidence(sources.windowsNative, `native://${overlayId}`)
  };
}

function surfaceEvidence(source = {}, expectedRoutePath) {
  return {
    applied: source.applied === true,
    fixtureVariant: source.fixtureVariant || null,
    sharedSettingsHash: stringOrNull(source.sharedSettingsHash),
    overlaySettingsHash: stringOrNull(source.overlaySettingsHash),
    routePath: stringOrNull(source.routePath),
    expectedRoutePath,
    pixelEvidence: source.pixelEvidence || null
  };
}

function settingsFingerprintEvidence(sources) {
  const values = runtimeSurfaceNames.map((name) => {
    const source = sources[name] || {};
    return `${source.sharedSettingsHash || ''}:${source.overlaySettingsHash || ''}`;
  });
  return {
    browserReview: values[0],
    localhostObs: values[1],
    windowsNative: values[2],
    combined: values.every((value) => value && value === values[0]) ? values[0] : null
  };
}

function validateNoRedundantTitle(contract, failures) {
  if (!ordinaryOverlayIds.has(contract.overlayId)) {
    return;
  }
  const title = contract.semanticModel.title || contract.semanticModel.renderedTitle || '';
  if (title.trim()) {
    failures.push(failure(contract.overlayId, 'chrome-contract', `redundant overlay title is still present: ${title}`));
  }
  const expectedTitle = normalizedOverlayTitle(contract.overlayId);
  if (contract.semanticModel.visibleText.includes(expectedTitle)) {
    failures.push(failure(contract.overlayId, 'chrome-contract', `visible model text still includes overlay title ${expectedTitle}`));
  }
}

function validateRuntimeSurfaces(contract, failures) {
  const fingerprints = new Set();
  for (const surfaceName of runtimeSurfaceNames) {
    const surface = contract.runtimeSurfaces?.[surfaceName] || {};
    if (surface.applied !== true) {
      failures.push(failure(contract.overlayId, 'runtime-surface', `${surfaceName} did not report applied=true`));
    }
    if (!surface.sharedSettingsHash) {
      failures.push(failure(contract.overlayId, 'settings-fingerprint', `${surfaceName} missing sharedSettingsHash`));
    }
    if (!surface.overlaySettingsHash) {
      failures.push(failure(contract.overlayId, 'settings-fingerprint', `${surfaceName} missing overlaySettingsHash`));
    }
    if (surface.sharedSettingsHash || surface.overlaySettingsHash) {
      fingerprints.add(`${surface.sharedSettingsHash || ''}:${surface.overlaySettingsHash || ''}`);
    }
    if (surfaceName !== 'windowsNative' && surface.routePath !== surface.expectedRoutePath) {
      failures.push(failure(contract.overlayId, 'runtime-surface', `${surfaceName} routePath expected ${surface.expectedRoutePath}, got ${surface.routePath || 'missing'}`));
    }
    if (surfaceName === 'windowsNative') {
      const pixelEvidence = surface.pixelEvidence || {};
      if (!['captured', 'disabled', 'unsupported', 'not-applicable'].includes(pixelEvidence.status)) {
        failures.push(failure(contract.overlayId, 'runtime-surface', 'windowsNative missing pixelEvidence.status'));
      }
      if (!stringOrNull(pixelEvidence.reason)) {
        failures.push(failure(contract.overlayId, 'runtime-surface', 'windowsNative missing pixelEvidence.reason'));
      }
    }
  }
  if (fingerprints.size > 1) {
    failures.push(failure(contract.overlayId, 'settings-fingerprint', `runtime settings fingerprints differ: ${[...fingerprints].join(', ')}`));
  }
}

function validateProvenance(contract, failures) {
  const provenance = contract.provenance || {};
  if (!['live-capture', 'synthetic-preview', 'stale-history', 'unavailable'].includes(provenance.evidenceClass)) {
    failures.push(failure(contract.overlayId, 'provenance', 'missing explicit evidenceClass'));
  }
  if (typeof provenance.captureSpecific !== 'boolean') {
    failures.push(failure(contract.overlayId, 'provenance', 'missing captureSpecific boolean'));
  }
  if (!provenance.sourceContract) {
    failures.push(failure(contract.overlayId, 'provenance', 'missing sourceContract'));
  }
}

function validateTableEvidence(contract, failures) {
  if (!tableOverlayIds.has(contract.overlayId)) {
    return;
  }
  const semantic = contract.semanticModel || {};
  if (!semantic.renderedColumns?.length) {
    failures.push(failure(contract.overlayId, 'table-contract', 'rendered evidence missing columnKeys'));
  } else if (!sameArray(semantic.renderedColumns, semantic.columns)) {
    failures.push(failure(contract.overlayId, 'table-contract', `rendered columnKeys differ from model columns: ${semantic.renderedColumns.join(', ')}`));
  }
  if (!semantic.renderedRows?.length) {
    failures.push(failure(contract.overlayId, 'table-contract', 'rendered evidence missing rowIdentities'));
  } else if (!sameArray(semantic.renderedRows, semantic.rows)) {
    failures.push(failure(contract.overlayId, 'table-contract', 'rendered rowIdentities differ from model rows'));
  }
  if (semantic.renderedPlaceholderRowCount !== semantic.placeholderRowCount) {
    failures.push(failure(contract.overlayId, 'table-contract', `placeholderRowCount expected ${semantic.placeholderRowCount}, got ${semantic.renderedPlaceholderRowCount ?? 'missing'}`));
  }
}

function validateUnavailableContent(contract, failures) {
  const status = contract.semanticModel.status || '';
  if (!/waiting|unavailable/i.test(status)) {
    return;
  }
  if (!contract.semanticModel.hasRenderedContent) {
    return;
  }
  const policy = contract.provenance.unavailableContentPolicy;
  if (policy === 'section-aware-placeholders' && contract.overlayId !== 'session-weather') {
    failures.push(failure(contract.overlayId, 'unavailable-content', 'section-aware unavailable placeholders are only defined for Session / Weather'));
    return;
  }
  if (!['suppress-rendered-content', 'section-aware-placeholders'].includes(policy) && contract.provenance.evidenceClass !== 'stale-history') {
    failures.push(failure(contract.overlayId, 'unavailable-content', 'unavailable/waiting state carries rendered content without stale-history or explicit unavailable-content evidence'));
  }
}

function validateRoleContext(contract, failures) {
  const role = contract.semanticModel?.roleContext || {};
  if (!['driver', 'spectator', 'unknown'].includes(role.localRole)) {
    failures.push(failure(contract.overlayId, 'role-context', `localRole must be driver, spectator, or unknown; got ${role.localRole || 'missing'}`));
  }
  if (!role.roleSource) {
    failures.push(failure(contract.overlayId, 'role-context', 'missing roleSource'));
  }
  if (!Object.prototype.hasOwnProperty.call(role, 'isSpotting')) {
    failures.push(failure(contract.overlayId, 'role-context', 'missing nullable isSpotting future role slot'));
  } else if (role.isSpotting !== null && typeof role.isSpotting !== 'boolean') {
    failures.push(failure(contract.overlayId, 'role-context', 'isSpotting must be boolean or null'));
  }
  for (const key of ['playerIsSpectator', 'focusIsSpectator']) {
    if (role[key] !== null && typeof role[key] !== 'boolean') {
      failures.push(failure(contract.overlayId, 'role-context', `${key} must be boolean or null`));
    }
  }
}

function validateOverlaySpecificEvidence(contract, failures, options) {
  if (contract.overlayId === 'standings') {
    validateStandingsEvidence(contract, failures);
  }
  if (contract.overlayId === 'fuel-calculator') {
    validateFuelEvidence(contract, failures);
  }
  if (metricDensityOverlayIds.has(contract.overlayId)) {
    validateMetricDensityEvidence(contract, failures);
  }
  if (contract.overlayId === 'input-state') {
    validateInputEvidence(contract, failures);
  }
  if (contract.overlayId === 'track-map') {
    validateTrackMapEvidence(contract, failures);
  }
  if (options.requireSyntheticStateKind || contract.fixtureVariant) {
    validateSyntheticStateEvidence(contract, failures);
  }
}

function validateStandingsEvidence(contract, failures) {
  const tableStatus = contract.semanticModel.tableStatus || {};
  for (const key of ['dataRowCount', 'classHeaderCount', 'placeholderRowCount', 'clippedRowCount', 'statusCarCount']) {
    if (!Number.isFinite(tableStatus[key])) {
      failures.push(failure(contract.overlayId, 'standings-contract', `tableStatus missing ${key}`));
    }
  }
  if (Number.isFinite(tableStatus.clippedRowCount) && tableStatus.clippedRowCount !== 0) {
    failures.push(failure(contract.overlayId, 'standings-contract', `tableStatus reports ${tableStatus.clippedRowCount} clipped rows`));
  }
  const timingSanity = contract.semanticModel.timingSanity || {};
  if (!Number.isFinite(timingSanity.maxIntervalGapRatio)) {
    failures.push(failure(contract.overlayId, 'standings-contract', 'timingSanity missing maxIntervalGapRatio'));
  }
  if (timingSanity.absurdIntervalCount !== 0) {
    failures.push(failure(contract.overlayId, 'standings-contract', `timingSanity absurdIntervalCount expected 0, got ${timingSanity.absurdIntervalCount ?? 'missing'}`));
  }
}

function validateFuelEvidence(contract, failures) {
  const strategy = contract.semanticModel.fuelStrategy || {};
  if (!['measured', 'unavailable', 'not-needed'].includes(strategy.additionalFuelNeedState)) {
    failures.push(failure(contract.overlayId, 'fuel-contract', 'fuelStrategy missing additionalFuelNeedState'));
  }
  if (strategy.successCopyRequiresMeasuredNeed !== true) {
    failures.push(failure(contract.overlayId, 'fuel-contract', 'fuelStrategy missing successCopyRequiresMeasuredNeed=true'));
  }
  if (/\bNeed\s+Covered\b|\bCovered\b/i.test(contract.semanticModel.metricText)
    && !['measured', 'not-needed'].includes(strategy.additionalFuelNeedState)) {
    failures.push(failure(contract.overlayId, 'fuel-contract', 'renders Covered without measured additional-fuel-need evidence'));
  }

  const readiness = strategy.modelReadiness;
  if (readiness == null) {
    return;
  }

  if (!Array.isArray(readiness.evidenceSources)) {
    failures.push(failure(contract.overlayId, 'fuel-contract', 'modelReadiness missing evidenceSources'));
    return;
  }

  const hasCurrentSessionEvidence = readiness.evidenceSources.includes('current-session');
  if (hasCurrentSessionEvidence && typeof readiness.currentSessionEvidenceUpdatedAtUtc !== 'string') {
    failures.push(failure(contract.overlayId, 'fuel-contract', 'current-session modelReadiness evidence missing completion timestamp'));
  }
  if (!hasCurrentSessionEvidence && readiness.currentSessionEvidenceUpdatedAtUtc != null) {
    failures.push(failure(contract.overlayId, 'fuel-contract', 'durable-only modelReadiness evidence carries a current-session timestamp'));
  }
  if (hasCurrentSessionEvidence
    && readiness.evidenceSources.some((source) => !['current-session', 'durable-history'].includes(source))) {
    failures.push(failure(contract.overlayId, 'fuel-contract', 'modelReadiness has an unknown evidence source'));
  }
}

function validateMetricDensityEvidence(contract, failures) {
  if (contract.semanticModel.shouldRender === false) {
    return;
  }

  const layout = contract.semanticModel.layout || {};
  if (!Number.isFinite(layout.contentRowCount)) {
    failures.push(failure(contract.overlayId, 'layout-density', 'layout missing contentRowCount'));
  }
  if (!Number.isFinite(layout.unusedHeightRatio)) {
    failures.push(failure(contract.overlayId, 'layout-density', 'layout missing unusedHeightRatio'));
  } else if (layout.unusedHeightRatio > 0.35) {
    failures.push(failure(contract.overlayId, 'layout-density', `unusedHeightRatio ${layout.unusedHeightRatio} exceeds 0.35`));
  }
}

function validateInputEvidence(contract, failures) {
  if (contract.provenance.syntheticStateKind === 'forced-unavailable' || contract.semanticModel.shouldRender === false) {
    return;
  }

  if (contract.fixtureVariant === 'input-state-mock-data'
    && contract.provenance.syntheticStateKind !== 'input-state-mock-data') {
    failures.push(failure(contract.overlayId, 'input-contract', 'input-state-mock-data fixture missing explicit mock-data provenance'));
  }

  const inputAvailability = contract.semanticModel.inputAvailability || {};
  if (inputAvailability.fixtureControlsAvailable !== true) {
    failures.push(failure(contract.overlayId, 'input-contract', 'missing fixtureControlsAvailable evidence'));
  }
  if (contract.semanticModel.inputs.isAvailable !== true) {
    failures.push(failure(contract.overlayId, 'input-contract', 'input preview is unavailable despite fixture controls requirement'));
  }
  if (!Number.isFinite(contract.semanticModel.inputs.tracePointCount) || contract.semanticModel.inputs.tracePointCount <= 0) {
    failures.push(failure(contract.overlayId, 'input-contract', 'input preview missing non-empty trace evidence'));
  }
}

function validateTrackMapEvidence(contract, failures) {
  if (contract.semanticModel.trackMap.mapKind !== 'circle') {
    return;
  }
  const fallback = contract.semanticModel.mapFallback || {};
  if (fallback.kind !== 'circle') {
    failures.push(failure(contract.overlayId, 'track-map-contract', 'circle map missing mapFallback provenance'));
  }
  if (!fallback.reason) {
    failures.push(failure(contract.overlayId, 'track-map-contract', 'circle map missing fallback reason'));
  }
  if (/\blive\b/i.test(contract.semanticModel.status || '')) {
    failures.push(failure(contract.overlayId, 'track-map-contract', 'circle fallback is labelled live'));
  }
}

function validateSyntheticStateEvidence(contract, failures) {
  if (!contract.provenance.syntheticStateKind) {
    failures.push(failure(contract.overlayId, 'synthetic-state', `fixture ${contract.fixtureVariant || 'default'} missing syntheticStateKind provenance`));
  }
}

function visibleModelText(model) {
  return [
    model?.title,
    model?.renderedTitle,
    model?.status,
    model?.source,
    ...(model?.headerItems || []).flatMap((item) => [item.key, item.value, item.tone]),
    ...(model?.columns || []).flatMap((column) => [column.label, column.dataKey]),
    ...(model?.rows || []).flatMap((row) => [row.headerTitle, row.headerDetail, ...(row.cells || [])]),
    ...(model?.metrics || []).flatMap(metricRowText),
    ...(model?.metricSections || []).flatMap((section) => [
      section.title,
      ...(section.rows || []).flatMap(metricRowText)
    ]),
    ...(model?.gridSections || []).flatMap((section) => [
      section.title,
      ...(section.rows || []).flatMap((row) => [
        row.label,
        ...(row.cells || []).map((cell) => typeof cell === 'object' && cell !== null ? cell.value : cell)
      ])
    ])
  ].filter(Boolean).join(' ');
}

function metricText(model) {
  return [
    ...(model?.metrics || []).flatMap(metricRowText),
    ...(model?.metricSections || []).flatMap((section) => [
      section.title,
      ...(section.rows || []).flatMap(metricRowText)
    ]),
    ...(model?.gridSections || []).flatMap((section) => [
      section.title,
      ...(section.rows || []).flatMap((row) => [
        row.label,
        ...(row.cells || []).map((cell) => typeof cell === 'object' && cell !== null ? cell.value : cell)
      ])
    ])
  ].filter(Boolean).join(' ');
}

function metricRowText(row) {
  return [
    row?.label,
    row?.value,
    ...(row?.segments || []).flatMap((segment) => [segment.label, segment.value])
  ];
}

function tableColumnKeys(model) {
  return (model?.columns || []).map((column) => stringOrNull(column.dataKey) || stringOrNull(column.label) || '');
}

function tableRowIdentities(model) {
  return (model?.rows || []).map((row) => {
    const cells = (row.cells || []).map((cell) => String(cell ?? ''));
    const kind = row.isClassHeader ? 'class-header' : row.isPlaceholder ? 'placeholder' : 'row';
    const primary = row.headerTitle || cells.slice(0, 2).join('/');
    return [kind, primary, visibleRowDetail(row), row.isReference ? 'reference' : ''].join('|');
  });
}

function visibleRowDetail(row) {
  const detail = row?.headerDetail || '';
  return row?.isClassHeader ? String(detail).toUpperCase() : detail;
}

function placeholderRowCount(model) {
  return (model?.rows || []).filter((row) => row.isPlaceholder || !(row.cells || []).some((cell) => String(cell || '').trim())).length;
}

function inputEvidence(model) {
  const inputs = model?.inputs || {};
  return {
    isAvailable: inputs.isAvailable,
    tracePointCount: inputs.tracePointCount ?? (Array.isArray(inputs.trace) ? inputs.trace.length : undefined),
    hasGraph: inputs.hasGraph,
    hasRail: inputs.hasRail
  };
}

function trackMapEvidence(model) {
  const trackMap = model?.trackMap || {};
  return {
    mapKind: trackMap.mapKind || trackMap.renderModel?.mapKind || null,
    markerCount: trackMap.markerCount ?? trackMap.renderModel?.markerCount ?? null
  };
}

function graphEvidence(model) {
  const graph = model?.graph || {};
  return {
    seriesCount: (graph.series || []).length,
    trendMetricCount: (graph.trendMetrics || []).length,
    selectedSeriesCount: graph.selectedSeriesCount ?? null
  };
}

function roleContextEvidence(role = {}) {
  return {
    playerCarIdx: finiteNumberOrNull(role.playerCarIdx),
    focusCarIdx: finiteNumberOrNull(role.focusCarIdx),
    focusIsPlayer: typeof role.focusIsPlayer === 'boolean' ? role.focusIsPlayer : false,
    hasExplicitNonPlayerFocus: typeof role.hasExplicitNonPlayerFocus === 'boolean' ? role.hasExplicitNonPlayerFocus : false,
    playerIsSpectator: typeof role.playerIsSpectator === 'boolean' ? role.playerIsSpectator : null,
    focusIsSpectator: typeof role.focusIsSpectator === 'boolean' ? role.focusIsSpectator : null,
    localRole: ['driver', 'spectator', 'unknown'].includes(role.localRole) ? role.localRole : 'unknown',
    roleSource: stringOrNull(role.roleSource) || 'unavailable',
    isSpotting: typeof role.isSpotting === 'boolean' ? role.isSpotting : null,
    spottingSignalStatus: stringOrNull(role.spottingSignalStatus) || 'not-observed'
  };
}

function hasRenderedContent(model) {
  return Boolean(
    (model?.rows || []).length
    || (model?.points || []).length
    || (model?.metrics || []).length
    || (model?.metricSections || []).some((section) => (section.rows || []).length)
    || (model?.gridSections || []).some((section) => (section.rows || []).length)
    || (model?.graph?.series || []).length
    || (model?.graph?.trendMetrics || []).length
    || model?.inputs?.hasGraph
    || model?.inputs?.hasRail
    || (model?.inputs?.series || []).length
    || (model?.flags?.flags || []).length
    || (model?.streamChat?.rows || []).length
  );
}

function asStringArray(value) {
  return Array.isArray(value) ? value.map((item) => String(item ?? '')) : [];
}

function sameArray(left, right) {
  return Array.isArray(left)
    && Array.isArray(right)
    && left.length === right.length
    && left.every((value, index) => value === right[index]);
}

function normalizedOverlayTitle(overlayId) {
  return String(overlayId || '').replace(/-/g, ' ').replace(/\b\w/g, (match) => match.toUpperCase());
}

function stringOrNull(value) {
  if (value === null || value === undefined) {
    return null;
  }
  const text = String(value).trim();
  return text ? text : null;
}

function finiteNumberOrNull(value) {
  return Number.isFinite(value) ? value : null;
}

function failure(overlayId, family, message) {
  return { overlayId, family, message };
}
