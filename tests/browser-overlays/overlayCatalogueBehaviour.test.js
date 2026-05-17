import { afterEach, describe, expect, it } from 'vitest';
import {
  browserOverlayApiResponse,
  browserOverlayPages,
  carRadarRenderModelFromState,
  freshLiveSnapshot
} from './browserOverlayAssets.js';
import {
  renderBrowserOverlay
} from './browserOverlayTestHost.js';

let currentOverlay;

afterEach(() => {
  currentOverlay?.close();
  currentOverlay = null;
});

function localPlayerModels({ isOnTrack = true, isInGarage = false } = {}) {
  return {
    reference: {
      hasData: true,
      playerCarIdx: 10,
      focusCarIdx: 10,
      focusIsPlayer: true,
      isOnTrack,
      isInGarage
    },
    driverDirectory: {
      hasData: true,
      playerCarIdx: 10,
      focusCarIdx: 10
    }
  };
}

describe('browser overlay catalogue behaviour', () => {
  it('covers every supported browser overlay route', () => {
    expect(browserOverlayPages().map((page) => page.page.id).sort()).toEqual([
      'car-radar',
      'flags',
      'fuel-calculator',
      'gap-to-leader',
      'garage-cover',
      'input-state',
      'pit-service',
      'relative',
      'session-weather',
      'standings',
      'stream-chat',
      'track-map'
    ]);
  });

  it('exposes only production-backed overlay model routes in unit API fixtures', () => {
    const live = freshLiveSnapshot({});
    const modeledOverlayIds = [
      'standings',
      'relative',
      'fuel-calculator',
      'session-weather',
      'pit-service',
      'input-state',
      'flags',
      'gap-to-leader',
      'car-radar',
      'track-map',
      'garage-cover',
      'stream-chat'
    ];
    const noModelOverlayIds = [];

    for (const overlayId of modeledOverlayIds) {
      const response = browserOverlayApiResponse(overlayId, `/api/overlay-model/${overlayId}`, { live });
      expect(response?.model?.overlayId).toBe(overlayId);
    }

    for (const overlayId of noModelOverlayIds) {
      expect(browserOverlayApiResponse(overlayId, `/api/overlay-model/${overlayId}`, { live })).toBeNull();
    }

    expect(browserOverlayPages().find((page) => page.page.id === 'input-state')?.title).toBe('Inputs');
  });

  it('shows practice and qualifying fuel usage from measured completed laps only', () => {
    const live = freshLiveSnapshot({
      ...localPlayerModels(),
      session: {
        hasData: true,
        sessionType: 'Practice',
        sessionName: 'Practice'
      },
      raceEvents: {
        hasData: true,
        isOnTrack: true,
        isInGarage: false
      },
      fuelPit: {
        hasData: true,
        fuel: {
          fuelLevelLiters: 42,
          fuelLevelPercent: 0.4,
          fuelPerLapLiters: 5.2,
          confidence: 'measured-green-lap',
          measuredFuelPerLapMinimumLiters: 5.0,
          measuredFuelPerLapAverageLiters: 5.2,
          measuredFuelPerLapMaximumLiters: 5.4,
          measuredFuelPerLapSampleCount: 3
        }
      }
    });

    const practice = browserOverlayApiResponse('fuel-calculator', '/api/overlay-model/fuel-calculator', { live }).model;
    const qualifying = browserOverlayApiResponse('fuel-calculator', '/api/overlay-model/fuel-calculator', {
      live: {
        ...live,
        models: {
          ...live.models,
          session: {
            ...live.models.session,
            sessionType: 'Qualify',
            sessionName: 'Qualify'
          }
        }
      }
    }).model;
    const waiting = browserOverlayApiResponse('fuel-calculator', '/api/overlay-model/fuel-calculator', {
      live: {
        ...live,
        models: {
          ...live.models,
          fuelPit: {
            ...live.models.fuelPit,
            fuel: {
              ...live.models.fuelPit.fuel,
              measuredFuelPerLapAverageLiters: null,
              measuredFuelPerLapSampleCount: 0
            }
          }
        }
      }
    }).model;

    expect(practice.metricSections.find((section) => section.title === 'Fuel Usage').rows[0].label).toBe('Practice Usage');
    expect(practice.metricSections.find((section) => section.title === 'Fuel Usage').rows[0].value).toContain('avg 5.2 L/lap');
    expect(practice.metricSections.find((section) => section.title === 'Fuel Usage').rows[0].segments)
      .toContainEqual(expect.objectContaining({ label: 'Laps', value: '3 laps' }));
    expect(qualifying.metricSections.find((section) => section.title === 'Fuel Usage').rows[0].label).toBe('Quali Usage');
    expect(waiting.metricSections.find((section) => section.title === 'Fuel Usage').rows[0].value).toBe('waiting for completed lap');
  });

  for (const scenario of browserScenarios()) {
    it(`renders ${scenario.id} catalogue behaviour`, async () => {
      currentOverlay = await renderBrowserOverlay(scenario.id, scenario.fixture());

      await scenario.assert(currentOverlay);
    });
  }

  it('renders car radar for a multiclass approach even when no cars are inside the radar', () => {
    const render = carRadarRenderModelFromState({
      isAvailable: true,
      hasCarLeft: false,
      hasCarRight: false,
      cars: [],
      strongestMulticlassApproach: { relativeSeconds: -4.2, urgency: 0.5 },
      showMulticlassWarning: true,
      previewVisible: false,
      hasCurrentSignal: true,
      referenceCarClassColorHex: '#FFDA59'
    });

    expect(render.shouldRender).toBe(true);
    expect(render.multiclassArc?.label?.text).toBe('Faster class approaching 4.2s');
    expect(render.cars.filter((car) => car.kind === 'nearby')).toHaveLength(0);
    expect(render.cars.find((car) => car.kind === 'focus')).toBeTruthy();
  });

  it('renders spoofed stream chat replay rows without configuring an external provider', () => {
    const response = browserOverlayApiResponse('stream-chat', '/api/overlay-model/stream-chat', {
      live: freshLiveSnapshot({}),
      settings: {
        provider: 'none',
        isConfigured: false,
        streamlabsWidgetUrl: null,
        twitchChannel: null,
        status: 'replay_static',
        replayStatus: 'replay chat | spoofed',
        replaySource: 'source: spoofed stream replay',
        replayRows: [
          { name: 'TMR', text: 'Replay chat fixture.', kind: 'system' },
          { name: 'viewer42', text: 'Fuel window is open.', kind: 'message' }
        ]
      }
    });

    expect(response?.model?.status).toBe('replay chat | spoofed');
    expect(response?.model?.source).toBe('');
    expect(response?.model?.streamChat?.settings).toEqual({
      provider: 'none',
      isConfigured: false,
      streamlabsWidgetUrl: null,
      twitchChannel: null,
      status: 'replay_static',
      contentOptions: defaultStreamChatContentOptions()
    });
    expect(response?.model?.streamChat?.rows).toEqual([
      { name: 'TMR', text: 'Replay chat fixture.', kind: 'system', source: '', authorColorHex: null, metadata: [], badges: [], segments: [{ kind: 'text', text: 'Replay chat fixture.', imageUrl: null }] },
      { name: 'viewer42', text: 'Fuel window is open.', kind: 'message', source: '', authorColorHex: null, metadata: [], badges: [], segments: [{ kind: 'text', text: 'Fuel window is open.', imageUrl: null }] }
    ]);
  });

  it('keeps stream chat footer off while rendering the shared title shell', () => {
    const response = browserOverlayApiResponse('stream-chat', '/api/overlay-model/stream-chat', {
      live: freshLiveSnapshot({
        session: { sessionTimeRemainSeconds: 3672 }
      }),
      settings: {
        provider: 'none',
        isConfigured: false,
        streamlabsWidgetUrl: null,
        twitchChannel: null,
        status: 'replay_static',
        replayStatus: 'replay chat | spoofed',
        replaySource: 'source: spoofed stream replay',
        replayRows: [{ name: 'viewer42', text: 'Fuel window is open.', kind: 'message' }],
        showHeaderStatus: false,
        showHeaderTimeRemaining: true,
        showFooterSource: true
      }
    });

    expect(response?.model?.status).toBe('replay chat | spoofed');
    expect(response?.model?.headerItems).toEqual([{ key: 'status', value: 'replay chat | spoofed' }]);
    expect(response?.model?.source).toBe('');
  });

  it('preserves rich Twitch replay payloads for stream chat review fixtures', () => {
    const response = browserOverlayApiResponse('stream-chat', '/api/overlay-model/stream-chat', {
      live: freshLiveSnapshot({}),
      settings: {
        provider: 'twitch',
        isConfigured: true,
        twitchChannel: 'techmatesracing',
        status: 'configured_twitch',
        replayStatus: 'fixture chat | all twitch features',
        replayRows: [{
          name: 'reply_viewer',
          text: 'that pit call was perfect',
          kind: 'message',
          source: 'twitch',
          twitch: {
            command: 'PRIVMSG',
            tags: {
              id: 'message-1',
              'reply-parent-msg-id': 'parent-1',
              'reply-parent-msg-body': 'Box this lap.'
            },
            reply: {
              parentMessageId: 'parent-1',
              parentMessageBody: 'Box this lap.'
            }
          }
        }]
      }
    });

    expect(response?.model?.streamChat?.rows[0]?.twitch).toEqual({
      command: 'PRIVMSG',
      tags: {
        id: 'message-1',
        'reply-parent-msg-id': 'parent-1',
        'reply-parent-msg-body': 'Box this lap.'
      },
      reply: {
        parentMessageId: 'parent-1',
        parentMessageBody: 'Box this lap.'
      }
    });
  });

  it('keeps distinct pre-grid distance rows from overlapping vertically', () => {
    const render = carRadarRenderModelFromState({
      isAvailable: true,
      hasCarLeft: false,
      hasCarRight: false,
      cars: [
        { carIdx: 45, relativeMeters: -24, relativeSeconds: -4.6, carClassColorHex: '#FFDA59' },
        { carIdx: 42, relativeMeters: -16, relativeSeconds: -0.2, carClassColorHex: '#FFDA59' },
        { carIdx: 10, relativeMeters: -8, relativeSeconds: -2.3, carClassColorHex: '#FFDA59' },
        { carIdx: 22, relativeMeters: 0, relativeSeconds: -4.2, carClassColorHex: '#FFDA59' },
        { carIdx: 11, relativeMeters: 8, relativeSeconds: 1.1, carClassColorHex: '#FFDA59' }
      ],
      strongestMulticlassApproach: null,
      showMulticlassWarning: true,
      previewVisible: false,
      hasCurrentSignal: true,
      referenceCarClassColorHex: '#FFDA59'
    });

    const nearby = render.cars.filter((car) => car.kind === 'nearby').sort((left, right) => left.y - right.y);
    const distinctRowGaps = nearby.slice(1).map((car, index) => Math.abs(car.y - nearby[index].y));
    expect(Math.min(...distinctRowGaps)).toBeGreaterThanOrEqual(47.9);
  });
});

function defaultStreamChatContentOptions() {
  return {
    showAuthorColor: true,
    showBadges: true,
    showBits: true,
    showFirstMessage: true,
    showReplies: true,
    showTimestamps: true,
    showEmotes: true,
    showAlerts: true,
    showMessageIds: false
  };
}

function browserScenarios() {
  return [
    {
      id: 'standings',
      fixture: () => ({
        live: freshLiveSnapshot({}),
        model: tableModel('standings', 'Standings', 'P2 | 3 shown', standingsColumns(), [
          row(['1', '#11', 'Class Leader', 'Lap 13', '0.0', '1:22.123', '1:23.400', '']),
          row(['2', '#10', 'Reference Driver', '--', '--', '1:23.456', '1:23.456', ''], { isReference: true, cellTones: [null, null, null, null, null, 'personal-best', 'personal-best', null] }),
          row(['3', '#12', 'Chase Driver', '--', '--', '--', '--', ''])
        ])
      }),
      assert: ({ document }) => {
        expect(rowText(document)).toEqual([
          '1 #11 Class Leader Lap 13 0.0 1:22.123 1:23.400',
          '2 #10 Reference Driver -- -- 1:23.456 1:23.456',
          '3 #12 Chase Driver -- -- -- --'
        ]);
        expect(contentText(document)).not.toMatch(/\d(?:\.\d+)?L\b/i);
      }
    },
    {
      id: 'relative',
      fixture: () => ({
        live: freshLiveSnapshot({}),
        model: tableModel('relative', 'Relative', '5 - 2/4 cars', relativeColumns(), [
          row(['3', '#34 Near Ahead', '-2.350']),
          row(['5', '#55 Focus Driver', '0.000'], { isReference: true }),
          row(['6', '#61 Near Behind', '+1.200', 'IN'], { isPit: true })
        ])
      }),
      assert: ({ document }) => {
        expect(rowText(document)).toEqual([
          '3 #34 Near Ahead -2.350',
          '5 #55 Focus Driver 0.000',
          '6 #61 Near Behind +1.200 IN'
        ]);
        expect(contentText(document)).not.toMatch(/\d(?:\.\d+)?L\b/i);
      }
    },
    {
      id: 'fuel-calculator',
      fixture: () => {
        const raceRows = [
          metric('Plan', '31 laps | 3 stints | 2 stops', 'info', [
            segment('Race', '31 laps', 'info'),
            segment('Remain', '30.4 laps', 'info'),
            segment('Stints', '3', 'info'),
            segment('Stops', '2', 'info'),
            segment('Save', '0.2 L/lap', 'warning')
          ]),
          metric('Fuel', '74.0 L | 3.1 L/lap | Covered', 'success', [
            segment('Current', '74.0 L', 'info'),
            segment('Burn', '3.1 L/lap', 'info'),
            segment('Tank', '34.2 laps', 'info'),
            segment('Need', 'Covered', 'success')
          ])
        ];
        const stintRows = [
          metric('Stint 1', '12 laps | target 3.1 L/lap', 'info', [
            segment('Laps', '12 laps', 'info'),
            segment('Target', '3.1 L/lap', 'info'),
            segment('Save', '0.2 L/lap', 'warning')
          ])
        ];
        return {
          live: freshLiveSnapshot({}),
          model: metricsModel(
            'fuel-calculator',
            'Fuel Calculator',
            '3 stints / 2 stops',
            [...raceRows, ...stintRows],
            'burn 3.1 L/lap (measured green lap) | 34.2 laps/tank | history user',
            [],
            [
              { title: 'Race Information', rows: raceRows },
              { title: 'Stint Targets', rows: stintRows }
            ]),
          waitForSelector: '.metric.segmented'
        };
      },
      assert: ({ document }) => {
        const metrics = metricText(document).join(' ');
        expect(metrics).toContain('Plan Race 31 laps Remain 30.4 laps');
        expect(metrics).toContain('Stint 1 Laps 12 laps Target 3.1 L/lap');
        expect(contentText(document)).toContain('Race Information');
        expect(contentText(document)).toContain('Stint Targets');
        expect(contentText(document)).not.toContain('Laps Left');
        expect(document.querySelector('#content .metric.modeled, #content .value-segment.modeled')).toBeNull();
        expect(document.getElementById('status').textContent).toBe('3 stints / 2 stops');
      }
    },
    {
      id: 'session-weather',
      fixture: () => ({
        live: freshLiveSnapshot({}),
        model: (() => {
          const sessionRows = [
            metric('Session', 'Race | team', 'info', [
              segment('Type', 'Race', 'normal'),
              segment('Name', '--', 'normal'),
              segment('Mode', 'Team', 'normal')
            ]),
            metric('Event', 'Race | Mercedes-AMG GT3', 'normal', [
              segment('Event', 'Race', 'normal'),
              segment('Car', 'Mercedes-AMG GT3', 'normal')
            ]),
            metric('Clock', '17:22 elapsed | 6:37:08 left | 6:54:30 total', 'normal', [
              segment('Elapsed', '17:22', 'normal'),
              segment('Left', '6:37:08', 'normal'),
              segment('Total', '6:54:30', 'normal')
            ]),
            metric('Laps', '-- left | 179 total', 'normal', [
              segment('Remaining', '--', 'normal'),
              segment('Total', '179', 'normal')
            ]),
            metric('Track', 'Gesamtstrecke 24h | 25.38 km', 'normal', [
              segment('Name', 'Gesamtstrecke 24h', 'normal'),
              segment('Length', '25.38 km', 'normal')
            ])
          ];
          const weatherRows = [
            metric('Temps', 'air 19 C | track 44 C', 'warning', [
              segment('Air', '19 C', 'info', { accentHex: '#33CEFF' }),
              segment('Track', '44 C', 'warning', { accentHex: '#FF7D49' })
            ]),
            metric('Surface', 'Dry | Rubber Moderate Usage', 'normal', [
              segment('Wetness', 'Dry', 'normal'),
              segment('Declared', 'Dry', 'normal'),
              segment('Rubber', 'Moderate Usage', 'normal')
            ]),
            metric('Sky', 'Partly Cloudy | constant | rain:0%', 'normal', [
              segment('Skies', 'Partly Cloudy', 'normal'),
              segment('Weather', 'constant', 'normal'),
              segment('Rain', '0%', 'normal')
            ]),
            metric('Wind', 'S | 15 km/h', 'normal', [
              segment('Dir', 'S', 'normal'),
              segment('Speed', '15 km/h', 'normal'),
              segment('Facing', 'Head', 'normal', { rotationDegrees: 0 })
            ]),
            metric('Atmosphere', 'hum 48% | fog 0% | 1013 hPa', 'normal', [
              segment('Hum', '48%', 'normal'),
              segment('Fog', '0%', 'normal'),
              segment('Pressure', '1013 hPa', 'normal')
            ])
          ];
          return metricsModel('session-weather', 'Session / Weather', 'Race', [
            ...sessionRows,
            ...weatherRows
          ], '', [], [
            { title: 'Session', rows: sessionRows },
            { title: 'Weather', rows: weatherRows }
          ]);
        })(),
        waitForSelector: '.metric'
      }),
      assert: ({ document }) => {
        expect(contentText(document)).toContain('Session');
        expect(contentText(document)).toContain('Weather');
        expect(metricText(document)).toContain('Temps Air 19 C Track 44 C');
        expect(metricText(document)).toContain('Surface Wetness Dry Declared Dry Rubber Moderate Usage');
        expect(metricText(document)).toContain('Sky Skies Partly Cloudy Weather constant Rain 0%');
        expect(document.querySelectorAll('.metric .value-segment.custom-color')).toHaveLength(2);
        expect(metricText(document).join(' ')).toContain('Wind Dir S Speed 15 km/h Facing');
        expect(metricText(document).join(' ')).toContain('Head');
        expect(document.querySelector('.wind-arrow')?.textContent).toBe('');
        expect(metricText(document)).toContain('Atmosphere Hum 48% Fog 0% Pressure 1013 hPa');
        expect(metricText(document).join(' ')).not.toContain('State state 4');
        expect(metricText(document).join(' ')).not.toContain('Sun Alt');
        expect(document.getElementById('status').textContent).toBe('Race');
      }
    },
    {
      id: 'pit-service',
      fixture: () => ({
        live: freshLiveSnapshot({}),
        model: metricsModel('pit-service', 'Pit Service', '', [
          metric('Time / Laps', '03:58 | 148/179 laps', 'normal'),
          metric('Release', 'RED - service active', 'error'),
          metric('Pit status', 'in progress', 'error'),
          metric('Fuel request', 'requested | 31.6 L', 'normal', [
            segment('Requested', 'Yes', 'success'),
            segment('Selected', '31.6 L', 'info')
          ]),
          metric('Tearoff', 'requested', 'normal', [
            segment('Requested', 'Yes', 'success')
          ]),
          metric('Repair', '12s required', 'error', [
            segment('Required', '12s', 'error'),
            segment('Optional', '18s', 'warning')
          ]),
          metric('Fast repair', 'selected | available 1', 'normal', [
            segment('Selected', 'Yes', 'success'),
            segment('Available', '1', 'success')
          ])
        ], 'source: player/team pit service telemetry', [
          {
            title: 'Tire Analysis',
            headers: ['Info', 'FL', 'FR', 'RL', 'RR'],
            rows: [
              gridRow('Compound', [gridCell('S', 'success'), gridCell('S', 'success'), gridCell('S', 'success'), gridCell('S', 'success')]),
              gridRow('Change', [gridCell('Change', 'success'), gridCell('Change', 'success'), gridCell('Keep', 'info'), gridCell('Change', 'success')]),
              gridRow('Set limit', ['4 sets', '4 sets', '4 sets', '4 sets']),
              gridRow('Available', ['2', '2', gridCell('0', 'error'), '2']),
              gridRow('Wear', ['92/91/90%', '93/92/91%', '96/95/94%', '97/96/95%'])
            ]
          }
        ], [
          {
            title: 'Session',
            rows: [
              metric('Time / Laps', '03:58 | 148/179 laps', 'normal')
            ]
          },
          {
            title: 'Pit Signal',
            rows: [
              metric('Release', 'RED - service active', 'error', undefined, { rowColorHex: '#FF6274' }),
              metric('Pit status', 'in progress', 'error', undefined, { rowColorHex: '#FF6274' })
            ]
          },
          {
            title: 'Service Request',
            rows: [
              metric('Fuel request', 'requested | 31.6 L', 'normal', [
                segment('Requested', 'Yes', 'success'),
                segment('Selected', '31.6 L', 'info')
              ]),
              metric('Tearoff', 'requested', 'normal', [
                segment('Requested', 'Yes', 'success')
              ]),
              metric('Repair', '12s required', 'error', [
                segment('Required', '12s', 'error'),
                segment('Optional', '18s', 'warning')
              ]),
              metric('Fast repair', 'selected | available 1', 'normal', [
                segment('Selected', 'Yes', 'success'),
                segment('Available', '1', 'success')
              ])
            ]
          }
        ], [
          { key: 'status', value: '' },
          { key: 'timeRemaining', value: '00:03:58' }
        ]),
        waitForSelector: '.metric.segmented'
      }),
      assert: ({ document }) => {
        expect(metricText(document)).toContain('Release RED - service active');
        expect(metricText(document)).toContain('Pit status in progress');
        expect(document.querySelectorAll('.metric.segmented').length).toBeGreaterThanOrEqual(4);
        expect(contentText(document)).not.toContain('Estimated');
        expect([...document.querySelectorAll('.tire-grid-cell.info')].some((cellElement) => cellElement.textContent.includes('Keep'))).toBe(true);
        expect([...document.querySelectorAll('.tire-grid-cell.error')].some((cellElement) => cellElement.textContent.includes('0'))).toBe(true);
        expect([...document.querySelectorAll('.metric-section')].map((section) => section.textContent).join(' ')).toContain('Pit Signal');
        expect([...document.querySelectorAll('.metric-section')].map((section) => section.textContent).join(' ')).toContain('Tire Analysis');
        expect(contentText(document)).not.toContain('fARB');
        expect(contentText(document)).not.toContain('player on pit road');
        expect(document.querySelector('.tire-grid').textContent).toContain('Set limit');
        expect(document.querySelector('.tire-grid').textContent).toContain('92/91/90%');
        expect(document.getElementById('status').textContent).toBe('');
        expect(document.getElementById('time-remaining').textContent).toBe('00:03:58');
      }
    },
    {
      id: 'gap-to-leader',
      fixture: () => ({
        live: freshLiveSnapshot({}),
        model: {
          overlayId: 'gap-to-leader',
          title: 'Gap To Leader',
          status: 'live | race gap',
          source: 'source: live gap telemetry | cars 4',
          bodyKind: 'graph',
          columns: [],
          rows: [],
          metrics: [],
          points: [8, 6, 4.2],
          graph: {
            series: [
              {
                carIdx: 11,
                isReference: false,
                isClassLeader: true,
                classPosition: 1,
                alpha: 1,
                isStickyExit: false,
                isStale: false,
                points: [
                  { axisSeconds: 100, gapSeconds: 0, startsSegment: true },
                  { axisSeconds: 104, gapSeconds: 0, startsSegment: false },
                  { axisSeconds: 108, gapSeconds: 0, startsSegment: false }
                ]
              },
              {
                carIdx: 12,
                isReference: true,
                isClassLeader: false,
                classPosition: 2,
                alpha: 1,
                isStickyExit: false,
                isStale: false,
                points: [
                  { axisSeconds: 100, gapSeconds: 8, startsSegment: true },
                  { axisSeconds: 104, gapSeconds: 6, startsSegment: false },
                  { axisSeconds: 108, gapSeconds: 4.2, startsSegment: false }
                ]
              }
            ],
            weather: [
              { axisSeconds: 100, condition: 'Dry' }
            ],
            leaderChanges: [],
            driverChanges: [],
            startSeconds: 100,
            endSeconds: 120,
            maxGapSeconds: 10,
            lapReferenceSeconds: 80,
            selectedSeriesCount: 2,
            trendMetrics: [
              { label: '5L', focusGapChangeSeconds: -1.4, chaser: { carIdx: 14, label: '#14', gainSeconds: 0.8 }, state: 'ready', stateLabel: null },
              { label: '10L', focusGapChangeSeconds: null, chaser: null, state: 'warming', stateLabel: '0.7L' },
              { label: 'Pit', focusGapChangeSeconds: null, chaser: null, state: 'unavailable', stateLabel: null },
              { label: 'PLap', focusGapChangeSeconds: null, chaser: null, state: 'unavailable', stateLabel: null },
              { label: 'Stint', focusGapChangeSeconds: null, chaser: null, state: 'stint', stateLabel: null, primaryText: '5L', threatText: '6L', comparisonText: '5L' },
              { label: 'Tire', focusGapChangeSeconds: null, chaser: null, state: 'unavailable', stateLabel: null },
              { label: 'Last', focusGapChangeSeconds: null, chaser: null, state: 'last', stateLabel: null, primaryText: '1:31.842', threatText: '1:30.913', comparisonText: '1:32.104' },
              { label: 'Status', focusGapChangeSeconds: null, chaser: null, state: 'status', stateLabel: null, primaryText: 'Track', threatText: 'Track', comparisonText: 'Pit' }
            ],
            activeThreat: { label: 'Threat', focusGapChangeSeconds: null, chaser: { carIdx: 14, label: '#14', gainSeconds: 0.8 }, state: 'ready', stateLabel: null },
            threatCarIdx: 14,
            metricDeadbandSeconds: 0.25,
            scale: {
              maxGapSeconds: 10,
              isFocusRelative: false,
              aheadSeconds: 0,
              behindSeconds: 0,
              referencePoints: [],
              latestReferenceGapSeconds: 0
            }
          }
        },
        waitForSelector: '.model-graph'
      }),
      assert: ({ document }) => {
        expect(document.querySelector('.model-graph')).not.toBeNull();
        expect(metricText(document)).not.toContain('Class leader +4.2');
        expect(document.getElementById('status').textContent).toBe('live | race gap');
        expect(document.getElementById('source').textContent).toBe('source: live gap telemetry | cars 4');
      }
    },
    {
      id: 'input-state',
      fixture: () => ({
        live: freshLiveSnapshot({
          ...localPlayerModels(),
          raceEvents: { hasData: true, isOnTrack: true, isInGarage: false },
          inputs: {
            hasData: true,
            quality: 'raw',
            throttle: 0.72,
            brake: 1,
            clutch: 0.4,
            steeringWheelAngle: 0.25,
            gear: 3,
            speedMetersPerSecond: 64,
            brakeAbsActive: true
          }
        }),
        settings: {
          showThrottle: true,
          showBrake: true,
          showClutch: true,
          showSteering: true,
          showGear: true,
          showSpeed: true
        },
        waitForSelector: '.input-layout'
      }),
      assert: ({ document }) => {
        expect(document.querySelector('.input-graph')).not.toBeNull();
        expect(document.body.textContent).toContain('ABS');
        expect(document.getElementById('status').textContent).toBe('');
      }
    },
    {
      id: 'input-state',
      fixture: () => ({
        live: freshLiveSnapshot({
          ...localPlayerModels(),
          raceEvents: { hasData: true, isOnTrack: true, isInGarage: false },
          inputs: {
            hasData: true,
            quality: 'raw',
            throttle: 0.72,
            brake: 1,
            clutch: 0.4,
            steeringWheelAngle: 0.25,
            gear: 3,
            speedMetersPerSecond: 64,
            brakeAbsActive: true
          }
        }),
        settings: {
          showThrottleTrace: true,
          showBrakeTrace: false,
          showClutchTrace: true,
          showThrottle: false,
          showBrake: false,
          showClutch: false,
          showSteering: true,
          showGear: false,
          showSpeed: true
        },
        waitForSelector: '.input-layout'
      }),
      assert: ({ document }) => {
        expect(document.querySelector('.input-graph')).not.toBeNull();
        expect(document.querySelector('.input-rail')).not.toBeNull();
        expect(document.querySelectorAll('.input-bar')).toHaveLength(0);
        expect(contentText(document)).toContain('Wheel');
        expect(contentText(document)).toContain('SPD');
        expect(contentText(document)).not.toContain('ABS');
      }
    },
    {
      id: 'car-radar',
      fixture: () => ({
        live: freshLiveSnapshot({
          ...localPlayerModels(),
          raceEvents: { hasData: true, isOnTrack: true, isInGarage: false },
          spatial: {
            hasData: true,
            sideStatus: 'left',
            hasCarLeft: true,
            hasCarRight: false,
            strongestMulticlassApproach: { relativeSeconds: -2.8 },
            cars: [
              { carIdx: 12, relativeSeconds: -1.2, relativeMeters: -8, carClassColorHex: '#FFDA59' },
              { carIdx: 14, relativeSeconds: 1.7, relativeMeters: 12, carClassColorHex: '#33CEFF' }
            ]
          }
        }),
        waitForSelector: '.radar-v2'
      }),
      assert: ({ document }) => {
        expect(document.querySelector('.radar-v2')).not.toBeNull();
        expect(document.body.textContent).toContain('Faster class approaching 2.8s');
        expect(document.querySelector('.radar-car-side-left')).not.toBeNull();
      }
    },
    {
      id: 'car-radar',
      fixture: () => ({
        live: freshLiveSnapshot({
          ...localPlayerModels(),
          raceEvents: { hasData: true, isOnTrack: true, isInGarage: false },
          spatial: {
            hasData: false,
            sideStatus: 'waiting',
            hasCarLeft: false,
            hasCarRight: false,
            strongestMulticlassApproach: null,
            cars: []
          }
        }),
        waitForSelector: null
      }),
      assert: ({ document }) => {
        expect(document.querySelector('.radar-v2')).toBeNull();
        expect(document.getElementById('content').textContent.trim()).toBe('');
        expect(document.getElementById('status').textContent).toBe('waiting for radar');
      }
    },
    {
      id: 'car-radar',
      fixture: () => ({
        live: freshLiveSnapshot({
          ...localPlayerModels({ isOnTrack: false }),
          raceEvents: { hasData: true, isOnTrack: false, isInGarage: false },
          spatial: {
            hasData: true,
            sideStatus: 'left',
            hasCarLeft: true,
            hasCarRight: false,
            strongestMulticlassApproach: { relativeSeconds: -3.4 },
            cars: [
              { carIdx: 12, relativeSeconds: -1.2, relativeMeters: -8, carClassColorHex: '#FFDA59' }
            ]
          }
        }),
        waitForSelector: null
      }),
      assert: ({ document }) => {
        expect(document.querySelector('.radar-v2')).toBeNull();
        expect(document.getElementById('content').textContent.trim()).toBe('');
        expect(document.getElementById('status').textContent).toMatch(/waiting( for player in car)?/);
      }
    },
    {
      id: 'track-map',
      fixture: () => ({
        live: freshLiveSnapshot({
          reference: {
            hasData: true,
            playerCarIdx: 10,
            focusCarIdx: 10,
            focusIsPlayer: true,
            isOnTrack: true,
            lapDistPct: 0.42,
            playerTrackSurface: 3
          },
          driverDirectory: { hasData: true, playerCarIdx: 10, focusCarIdx: 10 },
          timing: {
            focusCarIdx: 10,
            focusRow: { carIdx: 10, isFocus: true, lapDistPct: 0.42, hasSpatialProgress: true, hasTakenGrid: false, classPosition: 5 },
            overallRows: [
              { carIdx: 10, isFocus: true, lapDistPct: 0.42, hasSpatialProgress: true, hasTakenGrid: false, classPosition: 5 },
              { carIdx: 11, lapDistPct: 0.28, hasSpatialProgress: true, hasTakenGrid: false, carClassColorHex: '#33CEFF' },
              { carIdx: 12, lapDistPct: 0.58, hasSpatialProgress: true, hasTakenGrid: true, carClassColorHex: '#FFDA59' }
            ],
            classRows: []
          },
          spatial: {
            hasData: true,
            referenceCarIdx: 10,
            cars: [{ carIdx: 11, lapDistPct: 0.28, carClassColorHex: '#FFDA59' }]
          },
          raceEvents: { hasData: true, lapDistPct: 0.42 },
          trackMap: {
            hasData: true,
            sectors: [
              { startPct: 0, endPct: 0.33, highlight: 'personal-best' },
              { startPct: 0.33, endPct: 0.66, highlight: 'best-lap' }
            ]
          }
        }),
        settings: {
          trackMap: trackMapAsset(),
          trackMapSettings: { internalOpacity: 1, showSectorBoundaries: true }
        },
        waitForSelector: '.track svg'
      }),
      assert: ({ document }) => {
        expect(document.querySelector('.track svg')).not.toBeNull();
        expect(document.querySelectorAll('.track ellipse, .track path, .track line').length).toBeGreaterThan(2);
        expect(document.querySelectorAll('.track circle[fill="rgba(51,206,255,0.961)"]').length).toBe(0);
        expect(document.querySelectorAll('.track circle[fill="rgba(255,218,89,0.961)"]').length).toBe(1);
        expect(document.querySelectorAll('.track circle[fill="rgba(0,232,255,1.000)"]').length).toBe(1);
        expect(document.querySelector('.track text')?.textContent).toBe('5');
        expect(document.getElementById('status').textContent).toBe('live');
      }
    },
    {
      id: 'flags',
      fixture: () => ({
        live: freshLiveSnapshot({}),
        model: {
          overlayId: 'flags',
          title: 'Flags',
          status: 'yellow + repair + checkered',
          source: 'source: session flags telemetry',
          bodyKind: 'flags',
          columns: [],
          rows: [],
          metrics: [],
          points: [],
          headerItems: [],
          flags: {
            isWaiting: false,
            flags: [
              { kind: 'yellow', category: 'yellow', label: 'Yellow', detail: 'waving', tone: 'warning' },
              { kind: 'meatball', category: 'critical', label: 'Repair', detail: null, tone: 'error' },
              { kind: 'checkered', category: 'finish', label: 'Checkered', detail: null, tone: 'info' }
            ]
          },
          shouldRender: true
        },
        waitForSelector: '.flags-v2'
      }),
      assert: ({ document }) => {
        expect(document.body.classList.contains('flags-page')).toBe(true);
        expect(document.querySelector('.flags-v2')).not.toBeNull();
        expect(document.querySelectorAll('.flag-cell')).toHaveLength(3);
        expect(document.querySelector('.flag-yellow title')?.textContent).toContain('Yellow | waving');
        expect(document.querySelector('.flag-meatball circle')?.getAttribute('fill')).toBe('rgb(245,124,38)');
        expect(document.getElementById('content').textContent).toContain('Repair');
        expect(document.getElementById('status').textContent).toBe('');
      }
    },
    {
      id: 'garage-cover',
      fixture: () => ({
        live: freshLiveSnapshot({
          raceEvents: { hasData: true, isGarageVisible: true }
        }),
        settings: { hasImage: false, imageVersion: null, fallbackReason: 'test', previewVisible: false },
        waitForSelector: '.garage-cover'
      }),
      assert: ({ document }) => {
        expect(document.querySelector('.garage-cover')).not.toBeNull();
        expect(document.querySelector('.garage-cover img')?.getAttribute('src')).toContain('/api/garage-cover/default-image');
        expect(document.getElementById('status').textContent).toBe('garage visible');
      }
    },
    {
      id: 'stream-chat',
      fixture: () => ({
        live: freshLiveSnapshot({}),
        settings: {
          provider: 'none',
          isConfigured: false,
          streamlabsWidgetUrl: null,
          twitchChannel: null,
          status: 'not_configured'
        },
        waitForSelector: '.chat-line'
      }),
      assert: ({ document }) => {
        expect(document.body.classList.contains('stream-chat-page')).toBe(true);
        expect(document.querySelector('.chat-line')).not.toBeNull();
        expect(document.querySelector('.chat-name').textContent).toBe('TMR');
        expect(document.querySelector('.title').textContent).toBe('Stream Chat');
        expect(document.getElementById('status').textContent).toBe('waiting for chat source');
      }
    }
  ];
}

function tableModel(overlayId, title, status, columns, rows) {
  return {
    overlayId,
    title,
    status,
    source: 'source: catalogue behaviour',
    bodyKind: 'table',
    columns,
    rows,
    metrics: []
  };
}

function metricsModel(overlayId, title, status, metrics, source = 'source: catalogue behaviour', gridSections = [], metricSections = [], headerItems = [{ key: 'status', value: status }]) {
  return {
    overlayId,
    title,
    status,
    source,
    bodyKind: 'metrics',
    columns: [],
    rows: [],
    metrics,
    gridSections,
    metricSections,
    headerItems
  };
}

function standingsColumns() {
  return [
    column('standings.class-position', 'CLS', 'class-position', 35, 'right'),
    column('standings.car-number', 'CAR', 'car-number', 50, 'right'),
    column('standings.driver', 'Driver', 'driver', 250, 'left'),
    column('standings.gap', 'GAP', 'gap', 60, 'right'),
    column('standings.interval', 'INT', 'interval', 60, 'right'),
    column('standings.fastest-lap', 'FAST', 'fastest-lap', 70, 'right'),
    column('standings.last-lap', 'LAST', 'last-lap', 70, 'right'),
    column('standings.pit', 'PIT', 'pit', 30, 'right')
  ];
}

function relativeColumns() {
  return [
    column('relative.position', 'Pos', 'relative-position', 38, 'right'),
    column('relative.driver', 'Driver', 'driver', 250, 'left'),
    column('relative.gap', 'Delta', 'gap', 70, 'right'),
    column('relative.pit', 'Pit', 'pit', 30, 'right')
  ];
}

function column(id, label, dataKey, width, alignment) {
  return { id, label, dataKey, width, alignment };
}

function row(cells, extra = {}) {
  return {
    cells,
    isClassHeader: false,
    isReference: false,
    isPit: false,
    isPartial: false,
    carClassColorHex: null,
    headerTitle: null,
    headerDetail: null,
    ...extra
  };
}

function metric(label, value, tone, segments = undefined, extra = {}) {
  return segments ? { label, value, tone, segments, ...extra } : { label, value, tone, ...extra };
}

function segment(label, value, tone, extra = {}) {
  return { label, value, tone, ...extra };
}

function gridRow(label, values, tone = 'normal') {
  return {
    label,
    tone,
    cells: values.map((value) => typeof value === 'object' && value !== null
      ? { value: value.value, tone: value.tone || tone }
      : { value, tone })
  };
}

function gridCell(value, tone) {
  return { value, tone };
}

function rowText(document) {
  return [...document.querySelectorAll('tbody tr')]
    .map((rowElement) => [...rowElement.querySelectorAll('td')]
      .map((cell) => cell.textContent.replace(/\s+/g, ' ').trim())
      .join(' ')
      .trim());
}

function metricText(document) {
  return [...document.querySelectorAll('.metric')]
    .map((metricElement) => metricElement.textContent.replace(/\s+/g, ' ').trim());
}

function contentText(document) {
  return document.getElementById('content').textContent;
}

function trackMapAsset() {
  return {
    racingLine: {
      closed: true,
      points: [
        { x: 0, y: 48, lapDistPct: 0 },
        { x: 65, y: 92, lapDistPct: 0.16 },
        { x: 142, y: 74, lapDistPct: 0.34 },
        { x: 176, y: 0, lapDistPct: 0.52 },
        { x: 102, y: -62, lapDistPct: 0.72 },
        { x: 18, y: -28, lapDistPct: 0.88 },
        { x: 0, y: 48, lapDistPct: 1 }
      ]
    },
    pitLane: {
      closed: false,
      points: [
        { x: 22, y: 36, lapDistPct: 0.03 },
        { x: 78, y: 56, lapDistPct: 0.11 }
      ]
    }
  };
}
