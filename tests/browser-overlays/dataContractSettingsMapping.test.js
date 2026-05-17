import { describe, expect, it } from 'vitest';
import { JSDOM } from 'jsdom';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import {
  repoRoot,
  renderAppValidatorReviewHtml
} from './browserOverlayAssets.js';

const snapshotSettings = JSON.parse(readFileSync(
  resolve(repoRoot, 'fixtures/data-contracts/v0.19.0/settings/settings.json'),
  'utf8'));

describe('data contract settings mapping', () => {
  it('maps the latest release snapshot into browser review overlay controls', () => {
    const reviewState = reviewStateFromSnapshot(snapshotSettings);
    const dom = new JSDOM(renderAppValidatorReviewHtml({
      selectedTab: 'standings',
      selectedRegion: 'content',
      reviewState
    }), {
      pretendToBeVisual: true,
      runScripts: 'dangerously',
      url: 'http://localhost:8765/review/app?tab=standings&region=content'
    });

    const config = JSON.parse(dom.window.document.getElementById('settings-app-config').textContent);
    expect(config.unitSystem).toBe('Imperial');
    expect(dom.window.document.querySelector('h1')?.textContent).toBe('Standings');
    expect(dom.window.document.querySelector('.sidebar-tab.active')?.textContent).toBe('Standings');

    const standings = overlay(config, 'standings');
    expect(standings.enabled).toBe(true);
    expect(standings.scalePercent).toBe(115);
    expect(standings.opacityPercent).toBe(88);
    expect(standings.classSeparatorsEnabled).toBe(true);
    expect(standings.otherClassRows).toBe(0);
    expect(standings.chrome.header.Status.race).toBe(false);
    expect(standings.chrome.footer.Source.race).toBe(false);
    expect(contentRow(standings, 'Class gap').enabled).toBe(false);
    expect(contentRow(standings, 'Driver').enabled).toBe(true);

    const relative = overlay(config, 'relative');
    expect(relative.carsEachSide).toBe(3);
    expect(contentRow(relative, 'Pit status').enabled).toBe(false);

    const fuel = overlay(config, 'fuel-calculator');
    expect(fuel.contentRows).toEqual([]);
    expect(fuel.chrome.footer.Source.race).toBe(false);

    const sessionWeather = overlay(config, 'session-weather');
    expect(contentRow(sessionWeather, 'Total time').enabled).toBe(false);
    expect(contentRow(sessionWeather, 'Event type').enabled).toBe(false);
    expect(contentRow(sessionWeather, 'Car').enabled).toBe(true);
    expect(contentRow(sessionWeather, 'Laps total').enabled).toBe(true);
    expect(contentRow(sessionWeather, 'Facing wind').enabled).toBe(true);
    expect(sessionWeather.footerRows).toEqual([]);

    const pitService = overlay(config, 'pit-service');
    expect(contentRow(pitService, 'Pressure').enabled).toBe(true);
    expect(contentRow(pitService, 'Fast repairs available').enabled).toBe(true);
    expect(pitService.chrome.footer.Source.race).toBe(false);

    const inputState = overlay(config, 'input-state');
    expect(contentRow(inputState, 'Brake trace').enabled).toBe(true);
    expect(contentRow(inputState, 'Speed').enabled).toBe(true);

    const carRadar = overlay(config, 'car-radar');
    expect(contentRow(carRadar, 'Faster-class warning').enabled).toBe(true);

    const gap = overlay(config, 'gap-to-leader');
    expect(gap.carsAhead).toBe(4);
    expect(gap.carsBehind).toBe(4);

    const trackMap = overlay(config, 'track-map');
    expect(trackMap.content['track-map.build-from-telemetry']).toBe(true);
    expect(contentRow(trackMap, 'Sector boundaries').enabled).toBe(true);

    const streamChat = overlay(config, 'stream-chat');
    expect(streamChat.provider).toBe('twitch');
    expect(streamChat.twitchChannel).toBe('techmatesracing');
    expect(contentRow(streamChat, 'Emotes').enabled).toBe(true);
    expect(contentRow(streamChat, 'Message IDs').enabled).toBe(false);

    const garageCover = overlay(config, 'garage-cover');
    expect(garageCover.garageHasImage).toBe(true);
    expect(garageCover.garagePreviewVisible).toBe(true);

    const flags = overlay(config, 'flags');
    expect(contentRow(flags, 'White / checkered').enabled).toBe(true);

    dom.window.close();
  });
});

function reviewStateFromSnapshot(settings) {
  return {
    unitSystem: settings.general?.unitSystem || 'Metric',
    overlays: Object.fromEntries((settings.overlays || []).map((overlay) => [
      overlay.id,
      overlayReviewState(overlay)
    ]))
  };
}

function overlayReviewState(overlay) {
  const options = overlay.options || {};
  return {
    enabled: overlay.enabled === true,
    scalePercent: Math.round(Number(overlay.scale || 1) * 100),
    opacityPercent: Math.round(Number(overlay.opacity || 1) * 100),
    sessions: {
      test: overlay.showInTest !== false,
      practice: overlay.showInPractice !== false,
      qualifying: overlay.showInQualifying !== false,
      race: overlay.showInRace !== false
    },
    content: contentState(options),
    chrome: chromeState(options),
    provider: options['stream-chat.provider'] || 'twitch',
    twitchChannel: options['stream-chat.twitch-channel'] || 'techmatesracing',
    streamlabsWidgetUrl: options['stream-chat.streamlabs-url'] || '',
    otherClassRows: integerOption(options['standings.other-class-rows'], 2),
    carsEachSide: integerOption(options['relative.cars-each-side'], 5),
    carsAhead: integerOption(options['gap.cars-ahead'], 5),
    carsBehind: integerOption(options['gap.cars-behind'], 5),
    garageHasImage: Boolean(options['garage-cover.image-path']),
    garagePreviewVisible: Boolean(options['garage-cover.preview-until-utc'])
  };
}

function contentState(options) {
  return Object.fromEntries(Object.entries(options).map(([key, value]) => [
    key,
    value !== 'false'
  ]));
}

function chromeState(options) {
  const chrome = {
    header: {
      Status: {},
      'Time remaining': {}
    },
    footer: {
      Source: {}
    }
  };
  for (const [key, value] of Object.entries(options)) {
    const enabled = value !== 'false';
    const match = /^chrome\.(header|footer)\.(status|time-remaining|source)\.(test|practice|qualifying|race)$/.exec(key);
    if (!match) continue;

    const [, area, item, session] = match;
    const label = item === 'status'
      ? 'Status'
      : item === 'time-remaining'
        ? 'Time remaining'
        : 'Source';
    chrome[area][label][session === 'test' ? 'practice' : session] = enabled;
  }

  return chrome;
}

function integerOption(value, fallback) {
  const parsed = Number.parseInt(value, 10);
  return Number.isFinite(parsed) ? parsed : fallback;
}

function overlay(config, id) {
  const match = config.overlays.find((candidate) => candidate.id === id);
  if (!match) {
    throw new Error(`Missing overlay config for ${id}`);
  }

  return match;
}

function contentRow(overlay, label) {
  const match = overlay.contentRows.find((row) => row.label === label);
  if (!match) {
    throw new Error(`Missing ${overlay.id} content row ${label}`);
  }

  return match;
}
