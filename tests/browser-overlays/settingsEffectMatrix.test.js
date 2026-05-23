import { describe, expect, it } from 'vitest';
import { startReviewServer } from './reviewServerTestHost.js';

const previewMode = 'race';

describe('settings effect matrix', () => {
  it.each(happyPathCases())('$id', async (testCase) => {
    await withReviewServer(async (server) => {
      for (const patch of visibleOverlayPatches(testCase.overlayId)) {
        await server.postReviewPatch(patch);
      }
      for (const patch of testCase.patches) {
        await server.postReviewPatch(patch);
      }

      const config = await settingsConfig(server, testCase.overlayId);
      testCase.assertSettings?.(overlayConfig(config, testCase.overlayId), config);

      const model = (await server.getJson(modelPath(testCase.overlayId, testCase.query))).model;
      testCase.assertModel?.(model);
      expectEffectiveSettingsEvidence(model, {
        caseId: testCase.id,
        overlayId: testCase.overlayId,
        preview: testCase.query?.preview || previewMode,
        settingKey: testCase.settingKey,
        expectedValue: testCase.expectedValue,
        session: testCase.session || null
      });
    });
  }, 15000);

  it('keeps settings OBS size and effective browser-source evidence scale-aware', async () => {
    await withReviewServer(async (server) => {
      for (const patch of visibleOverlayPatches('standings')) {
        await server.postReviewPatch(patch);
      }
      await server.postReviewPatch(numberPatch('standings', 'scalePercent', 125));
      await server.postReviewPatch(numberPatch('standings', 'opacityPercent', 80));

      const config = await settingsConfig(server, 'standings');
      const overlay = overlayConfig(config, 'standings');
      expect.soft(overlay.scalePercent).toBe(125);
      expect.soft(overlay.opacityPercent).toBe(80);
      expect.soft(overlay.browserSize).toBe('846 x 1030');

      const model = (await server.getJson(modelPath('standings'))).model;
      expect.soft(model.effectiveSettings.rendered.browserSource).toMatchObject({
        baseWidth: 677,
        baseHeight: 824,
        width: 846,
        height: 1030,
        scalePercent: 125,
        opacityPercent: 80
      });
      expect.soft(model.effectiveSettings.settings).toEqual(expect.arrayContaining([
        expect.objectContaining({ key: 'scalePercent', value: 125 }),
        expect.objectContaining({ key: 'opacityPercent', value: 80 })
      ]));
    });
  }, 15000);

  it('requires effective-settings source evidence for every exposed content toggle', async () => {
    await withReviewServer(async (server) => {
      const initialConfig = await settingsConfig(server);
      const contentCases = initialConfig.overlays
        .flatMap((overlay) => (overlay.contentRows || []).map((row) => ({
          id: `${overlay.id}.${row.key || row.label}`,
          overlayId: overlay.id,
          row
        })));

      expect.soft(contentCases.length).toBeGreaterThan(40);

      for (const overlayId of new Set(contentCases.map((testCase) => testCase.overlayId))) {
        for (const patch of visibleOverlayPatches(overlayId)) {
          await server.postReviewPatch(patch);
        }
      }

      for (const testCase of contentCases) {
        await server.postReviewPatch({
          kind: 'content',
          overlayId: testCase.overlayId,
          key: testCase.row.key,
          label: testCase.row.label,
          enabled: false
        });

        const config = await settingsConfig(server, testCase.overlayId);
        expect.soft(
          contentRow(overlayConfig(config, testCase.overlayId), testCase.row.label).enabled,
          `${testCase.id}: settings app did not reflect disabled content toggle`
        ).toBe(false);

        const model = (await server.getJson(modelPath(testCase.overlayId))).model;
        expectEffectiveSettingsEvidence(model, {
          caseId: testCase.id,
          overlayId: testCase.overlayId,
          settingKey: testCase.row.key,
          expectedValue: false
        });

        await server.postReviewPatch({
          kind: 'content',
          overlayId: testCase.overlayId,
          key: testCase.row.key,
          label: testCase.row.label,
          enabled: testCase.row.defaultEnabled !== false
        });
      }
    });
  }, 30000);
});

function happyPathCases() {
  return [
    {
      id: 'standings class separators off hides class header rows',
      overlayId: 'standings',
      settingKey: 'standings.class-separators.enabled',
      expectedValue: false,
      patches: [contentPatch('standings', 'standings.class-separators.enabled', 'Class separators', false)],
      assertSettings: (overlay) => {
        expect.soft(overlay.classSeparatorsEnabled).toBe(false);
      },
      assertModel: (model) => {
        expect.soft((model.rows || []).some((row) => row.isClassHeader)).toBe(false);
      }
    },
    {
      id: 'standings cars in class caps the reference class window',
      overlayId: 'standings',
      settingKey: 'carsInClass',
      expectedValue: 1,
      patches: [numberPatch('standings', 'carsInClass', 1)],
      assertSettings: (overlay) => {
        expect.soft(overlay.carsInClass).toBe(1);
      },
      assertModel: (model) => {
        expect.soft((model.rows || []).filter((row) => row.isReference)).toHaveLength(1);
        expect.soft(rowText(model)).not.toContain('Kauan Vigliazzi Teixeira Lemos');
      }
    },
    {
      id: 'standings other class rows zero hides non-reference classes',
      overlayId: 'standings',
      settingKey: 'otherClassRows',
      expectedValue: 0,
      patches: [numberPatch('standings', 'otherClassRows', 0)],
      assertSettings: (overlay) => {
        expect.soft(overlay.otherClassRows).toBe(0);
      },
      assertModel: (model) => {
        expect.soft(rowText(model)).not.toContain('Kousuke Konishi');
        expect.soft((model.rows || []).some((row) => row.isReference)).toBe(true);
      }
    },
    {
      id: 'relative cars each side controls stable row count',
      overlayId: 'relative',
      settingKey: 'carsEachSide',
      expectedValue: 2,
      patches: [numberPatch('relative', 'carsEachSide', 2)],
      assertSettings: (overlay) => {
        expect.soft(overlay.carsEachSide).toBe(2);
      },
      assertModel: (model) => {
        expect.soft(model.rows || []).toHaveLength(5);
        expect.soft((model.rows || []).findIndex((row) => row.isReference)).toBe(2);
      }
    },
    {
      id: 'relative pit content on exposes pit column',
      overlayId: 'relative',
      settingKey: 'relative.content.relative.pit.enabled',
      expectedValue: true,
      patches: [contentPatch('relative', 'relative.content.relative.pit.enabled', 'Pit status', true)],
      assertSettings: (overlay) => {
        expect.soft(contentRow(overlay, 'Pit status').enabled).toBe(true);
      },
      assertModel: (model) => {
        expect.soft((model.columns || []).some((column) => column.dataKey === 'pit')).toBe(true);
        expect.soft(rowText(model)).toContain('IN');
        expect.soft(model.effectiveSettings?.rendered?.browserSource?.baseWidth).toBe(440);
      }
    },
    {
      id: 'shared time header off removes race header time',
      overlayId: 'relative',
      settingKey: 'chrome.header.time-remaining.race',
      expectedValue: false,
      session: 'race',
      patches: [chromePatch('relative', 'header', 'Time remaining', false)],
      assertModel: (model) => {
        expect.soft((model.headerItems || []).some((item) => item.key === 'timeRemaining')).toBe(false);
      }
    },
    {
      id: 'gap ahead and behind zero hides graph',
      overlayId: 'gap-to-leader',
      settingKey: 'gap.cars-window',
      expectedValue: { carsAhead: 0, carsBehind: 0 },
      patches: [
        numberPatch('gap-to-leader', 'carsAhead', 0),
        numberPatch('gap-to-leader', 'carsBehind', 0)
      ],
      assertSettings: (overlay) => {
        expect.soft(overlay.carsAhead).toBe(0);
        expect.soft(overlay.carsBehind).toBe(0);
      },
      assertModel: (model) => {
        expect.soft(model.shouldRender).toBe(false);
        expect.soft(model.points || []).toEqual([]);
      }
    },
    {
      id: 'fuel unit system imperial converts fuel values',
      overlayId: 'fuel-calculator',
      settingKey: 'general.unitSystem',
      expectedValue: 'Imperial',
      patches: [{ kind: 'unitSystem', value: 'Imperial' }],
      assertModel: (model) => {
        expect.soft(JSON.stringify(model.metricSections || model.metrics || [])).toContain('gal');
      }
    },
    {
      id: 'session weather total time off removes total clock segment',
      overlayId: 'session-weather',
      settingKey: 'session-weather.clock.total.enabled',
      expectedValue: false,
      patches: [contentPatch('session-weather', 'session-weather.clock.total.enabled', 'Total time', false)],
      assertSettings: (overlay) => {
        expect.soft(contentRow(overlay, 'Total time').enabled).toBe(false);
      },
      assertModel: (model) => {
        expect.soft(metricSegmentLabels(model, 'Clock')).not.toContain('Total');
      }
    },
    {
      id: 'pit service pressure off removes pressure grid row',
      overlayId: 'pit-service',
      settingKey: 'pit-service.tire-analysis.pressure',
      expectedValue: false,
      patches: [contentPatch('pit-service', 'pit-service.tire-analysis.pressure', 'Pressure', false)],
      assertSettings: (overlay) => {
        expect.soft(contentRow(overlay, 'Pressure').enabled).toBe(false);
      },
      assertModel: (model) => {
        expect.soft(gridRowLabels(model)).not.toContain('Pressure');
      }
    },
    {
      id: 'input traces off removes graph while preserving current rail',
      overlayId: 'input-state',
      settingKey: 'input-state.trace.*',
      expectedValue: false,
      patches: [
        contentPatch('input-state', 'input-state.trace.throttle', 'Throttle trace', false),
        contentPatch('input-state', 'input-state.trace.brake', 'Brake trace', false),
        contentPatch('input-state', 'input-state.trace.clutch', 'Clutch trace', false)
      ],
      assertSettings: (overlay) => {
        expect.soft(contentRow(overlay, 'Throttle trace').enabled).toBe(false);
        expect.soft(contentRow(overlay, 'Brake trace').enabled).toBe(false);
        expect.soft(contentRow(overlay, 'Clutch trace').enabled).toBe(false);
      },
      assertModel: (model) => {
        expect.soft(model.inputs?.hasGraph).toBe(false);
        expect.soft(model.inputs?.hasRail).toBe(true);
      }
    },
    {
      id: 'car radar faster-class warning off removes warning arc',
      overlayId: 'car-radar',
      settingKey: 'radar.multiclass-warning',
      expectedValue: false,
      patches: [contentPatch('car-radar', 'radar.multiclass-warning', 'Faster-class warning', false)],
      assertSettings: (overlay) => {
        expect.soft(overlay.contentRows).toEqual([]);
        expect.soft(overlay.showMulticlassWarning).toBe(false);
      },
      assertModel: (model) => {
        expect.soft(model.carRadar?.showMulticlassWarning).toBe(false);
        expect.soft(model.carRadar?.strongestMulticlassApproach).toBeNull();
      }
    },
    {
      id: 'car radar multiclass window controls multiclass approach range',
      overlayId: 'car-radar',
      settingKey: 'radar.multiclass-warning-seconds',
      expectedValue: 3,
      patches: [numberPatch('car-radar', 'multiclassWarningSeconds', 3)],
      assertSettings: (overlay) => {
        expect.soft(overlay.multiclassWarningSeconds).toBe(3);
      },
      assertModel: (model) => {
        expect.soft(model.carRadar?.multiclassWarningRangeSeconds).toBe(3);
      }
    },
    {
      id: 'car radar range controls timing-aware visibility window',
      overlayId: 'car-radar',
      settingKey: 'radar.visibility-seconds',
      expectedValue: 5,
      patches: [numberPatch('car-radar', 'radarVisibilitySeconds', 5)],
      assertSettings: (overlay) => {
        expect.soft(overlay.radarVisibilitySeconds).toBe(5);
      },
      assertModel: (model) => {
        expect.soft(model.carRadar?.radarVisibilitySeconds).toBe(5);
      }
    },
    {
      id: 'flags blue off removes blue flag category',
      overlayId: 'flags',
      settingKey: 'flags.show-blue',
      expectedValue: false,
      patches: [contentPatch('flags', 'flags.show-blue', 'Blue', false)],
      assertSettings: (overlay) => {
        expect.soft(contentRow(overlay, 'Blue').enabled).toBe(false);
      },
      assertModel: (model) => {
        expect.soft((model.flags?.flags || []).map((flag) => flag.category)).not.toContain('blue');
      }
    },
    {
      id: 'track map sector boundaries off reaches render model',
      overlayId: 'track-map',
      settingKey: 'track-map.sector-boundaries.enabled',
      expectedValue: false,
      patches: [contentPatch('track-map', 'track-map.sector-boundaries.enabled', 'Sector boundaries', false)],
      assertSettings: (overlay) => {
        expect.soft(contentRow(overlay, 'Sector boundaries').enabled).toBe(false);
      },
      assertModel: (model) => {
        expect.soft(model.trackMap?.showSectorBoundaries).toBe(false);
      }
    },
    {
      id: 'stream chat emotes off reaches content options',
      overlayId: 'stream-chat',
      settingKey: 'stream-chat.twitch.emotes',
      expectedValue: false,
      query: { fixture: 'stream-chat-twitch-rich' },
      patches: [contentPatch('stream-chat', 'stream-chat.twitch.emotes', 'Emotes', false)],
      assertSettings: (overlay) => {
        expect.soft(contentRow(overlay, 'Emotes').enabled).toBe(false);
      },
      assertModel: (model) => {
        expect.soft(model.streamChat?.settings?.contentOptions?.showEmotes).toBe(false);
      }
    },
    {
      id: 'stream chat provider twitch configures chat source',
      overlayId: 'stream-chat',
      settingKey: 'stream-chat.provider',
      expectedValue: 'twitch',
      patches: [{ kind: 'streamChatProvider', overlayId: 'stream-chat', providerLabel: 'Twitch' }],
      assertSettings: (overlay) => {
        expect.soft(overlay.provider).toBe('twitch');
      },
      assertModel: (model) => {
        expect.soft(model.streamChat?.settings).toMatchObject({
          provider: 'twitch',
          isConfigured: true,
          twitchChannel: 'techmatesracing'
        });
      }
    },
    {
      id: 'stream chat opacity controls root opacity',
      overlayId: 'stream-chat',
      settingKey: 'opacityPercent',
      expectedValue: 70,
      query: { fixture: 'stream-chat-twitch-rich' },
      patches: [numberPatch('stream-chat', 'opacityPercent', 70)],
      assertSettings: (overlay) => {
        expect.soft(overlay.opacityPercent).toBe(70);
      },
      assertModel: (model) => {
        expect.soft(model.rootOpacity).toBeCloseTo(0.7, 5);
        expect.soft(model.effectiveSettings?.rendered?.browserSource).toMatchObject({
          opacity: 0.7,
          opacityPercent: 70
        });
      }
    },
    {
      id: 'garage cover preview stays settings-only',
      overlayId: 'garage-cover',
      settingKey: 'garage-cover.previewVisible',
      expectedValue: true,
      query: { preview: 'off' },
      patches: [
        { kind: 'garageCover', overlayId: 'garage-cover', action: 'import' },
        { kind: 'garageCover', overlayId: 'garage-cover', action: 'preview' }
      ],
      assertSettings: (overlay) => {
        expect.soft(overlay.garageHasImage).toBe(true);
        expect.soft(overlay.garagePreviewVisible).toBe(true);
      },
      assertModel: (model) => {
        expect.soft(model.garageCover?.shouldCover).toBe(false);
      }
    },
    {
      id: 'localhost obs hidden product overlay returns empty model',
      overlayId: 'standings',
      settingKey: 'overlayEnabled',
      expectedValue: false,
      patches: [
        { kind: 'overlayEnabled', overlayId: 'standings', enabled: false },
        { kind: 'session', overlayId: 'standings', session: 'Race', enabled: false }
      ],
      assertSettings: (overlay) => {
        expect.soft(overlay.enabled).toBe(false);
        expect.soft(overlay.sessions.race).toBe(false);
      },
      assertModel: (model) => {
        expect.soft(model.shouldRender).toBe(false);
        expect.soft(model.rows || []).toEqual([]);
      }
    }
  ];
}

async function withReviewServer(callback) {
  const server = await startReviewServer();
  try {
    await callback(server);
  } finally {
    await server.stop();
  }
}

function visibleOverlayPatches(overlayId) {
  return [
    { kind: 'overlayEnabled', overlayId, enabled: true },
    { kind: 'session', overlayId, session: 'Race', enabled: true }
  ];
}

function contentPatch(overlayId, key, label, enabled) {
  return { kind: 'content', overlayId, key, label, enabled };
}

function chromePatch(overlayId, area, label, enabled) {
  return { kind: 'chrome', overlayId, area, label, session: 'Race', enabled };
}

function numberPatch(overlayId, key, value) {
  return { kind: 'number', overlayId, key, value };
}

async function settingsConfig(server, overlayId = 'general') {
  const html = await server.getText(`/review/app?preview=${previewMode}&tab=${encodeURIComponent(overlayId)}`);
  const match = /<script[^>]+id="settings-app-config"[^>]*>([\s\S]*?)<\/script>/.exec(html);
  if (!match) {
    throw new Error('Missing settings-app-config script in review app HTML');
  }
  return JSON.parse(match[1]);
}

function modelPath(overlayId, query = {}) {
  const params = new URLSearchParams({
    preview: query.preview || previewMode
  });
  if (query.fixture) {
    params.set('fixture', query.fixture);
  }
  return `/api/overlay-model/${encodeURIComponent(overlayId)}?${params}`;
}

function overlayConfig(config, overlayId) {
  const overlay = config.overlays.find((candidate) => candidate.id === overlayId);
  expect.soft(overlay, `Missing overlay config for ${overlayId}`).toBeTruthy();
  return overlay || { id: overlayId, contentRows: [], sessions: {} };
}

function contentRow(overlay, label) {
  const row = (overlay.contentRows || []).find((candidate) => candidate.label === label);
  expect.soft(row, `Missing ${overlay.id} content row ${label}`).toBeTruthy();
  return row || { key: label, label, enabled: undefined, defaultEnabled: undefined };
}

function expectEffectiveSettingsEvidence(model, {
  caseId,
  overlayId,
  preview = previewMode,
  settingKey,
  expectedValue,
  session = null
}) {
  const evidence = model?.effectiveSettings;
  expect.soft(evidence, `${caseId}: missing model.effectiveSettings parity evidence`).toBeTruthy();
  if (!evidence) {
    return;
  }

  expect.soft(evidence.overlayId, `${caseId}: effectiveSettings overlay id`).toBe(overlayId);
  expect.soft(evidence.previewMode, `${caseId}: effectiveSettings preview mode`).toBe(preview);
  expect.soft(evidence.sources?.browserReview?.applied, `${caseId}: browser review source evidence`).toBe(true);
  expect.soft(evidence.sources?.localhostObs?.applied, `${caseId}: localhost OBS source evidence`).toBe(true);
  expect.soft(evidence.sources?.windowsNative?.applied, `${caseId}: Windows native source evidence`).toBe(true);

  const expectedSetting = { key: settingKey, value: expectedValue };
  if (session) {
    expectedSetting.session = session;
  }
  expect.soft(
    evidence.settings || [],
    `${caseId}: effectiveSettings missing ${settingKey}=${JSON.stringify(expectedValue)}`
  ).toContainEqual(expect.objectContaining(expectedSetting));
}

function rowText(model) {
  return (model.rows || [])
    .flatMap((row) => [
      row.headerTitle,
      row.headerDetail,
      ...(row.cells || [])
    ])
    .filter(Boolean)
    .join(' ');
}

function metricSegmentLabels(model, rowLabel = null) {
  return (model.metricSections || [])
    .flatMap((section) => section.rows || [])
    .filter((row) => rowLabel == null || row.label === rowLabel)
    .flatMap((row) => row.segments || [])
    .map((segment) => segment.label);
}

function gridRowLabels(model) {
  return (model.gridSections || [])
    .flatMap((section) => section.rows || [])
    .map((row) => row.label);
}
