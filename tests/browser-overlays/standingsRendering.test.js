import { afterEach, describe, expect, it } from 'vitest';
import { freshLiveSnapshot, renderBrowserOverlay } from './browserOverlayTestHost.js';

let currentOverlay;

afterEach(() => {
  currentOverlay?.close();
  currentOverlay = null;
});

describe('standings browser rendering', () => {
  it('renders scoring-backed standings with class separators and configured other-class limit', async () => {
    currentOverlay = await renderBrowserOverlay('standings', {
      live: freshLiveSnapshot({}),
      model: standingsDisplayModel()
    });

    const rows = [...currentOverlay.document.querySelectorAll('tbody tr')];
    const rowText = rows.map(rowCells);

    expect(rowText).toEqual([
      'LMP2 2 cars | ~10 laps',
      '1 #8 Proto One Lap 22 -45.0 1:45.884 1:46.210',
      'GT3 3 cars | ~12.4 laps',
      '1 #11 GT3 Leader Lap 21 -2.0 1:53.112 1:53.112',
      '2 #71 Focus Racer +3.4 0.0 1:54.228 1:54.901',
      '3 #91 Chaser +8.9 +5.5 1:55.480 1:56.004 IN'
    ]);
    expect(rows[0].classList.contains('class-header')).toBe(true);
    expect(rows[4].classList.contains('focus')).toBe(true);
    expect(rows[5].classList.contains('pit')).toBe(true);
    expect(currentOverlay.document.getElementById('status').textContent).toBe('scoring | 5/5 live');
    expect(currentOverlay.document.getElementById('source').textContent).toBe('source: scoring snapshot + live timing');
    expect(contentText(currentOverlay.document)).not.toContain('Proto Two');
  });

  it('renders placeholder-F2 race standings without lap-distance gap fallback', async () => {
    currentOverlay = await renderBrowserOverlay('standings', {
      live: freshLiveSnapshot({}),
      model: placeholderF2StandingsDisplayModel()
    });

    const rows = [...currentOverlay.document.querySelectorAll('tbody tr')];
    const rowText = rows.map(rowCells);

    expect(rowText).toEqual([
      '1 #11 Class Leader Leader 0.0 -- --',
      '2 #10 Reference Driver -- -- -- --',
      '3 #12 Chase Driver -- -- -- --'
    ]);
    expect(contentText(currentOverlay.document)).not.toMatch(/\d(?:\.\d+)?L\b/i);
  });

});

function standingsDisplayModel() {
  return {
    overlayId: 'standings',
    title: 'Standings',
    status: 'scoring | 5/5 live',
    source: 'source: scoring snapshot + live timing',
    bodyKind: 'table',
    columns: standingsColumns(),
    rows: [
      headerRow('LMP2', '2 cars | ~10 laps', '#33CEFF'),
      carRow(['1', '#8', 'Proto One', 'Lap 22', '-45.0', '1:45.884', '1:46.210', '']),
      headerRow('GT3', '3 cars | ~12.4 laps', '#FFAA00'),
      carRow(['1', '#11', 'GT3 Leader', 'Lap 21', '-2.0', '1:53.112', '1:53.112', '']),
      carRow(['2', '#71', 'Focus Racer', '+3.4', '0.0', '1:54.228', '1:54.901', ''], { isReference: true }),
      carRow(['3', '#91', 'Chaser', '+8.9', '+5.5', '1:55.480', '1:56.004', 'IN'], { isPit: true })
    ],
    metrics: []
  };
}

function placeholderF2StandingsDisplayModel() {
  return {
    overlayId: 'standings',
    title: 'Standings',
    status: 'P2 | 3 shown',
    source: 'source: live timing telemetry',
    bodyKind: 'table',
    columns: standingsColumns(),
    rows: [
      carRow(['1', '#11', 'Class Leader', 'Leader', '0.0', '--', '--', '']),
      carRow(['2', '#10', 'Reference Driver', '--', '--', '--', '--', ''], { isReference: true }),
      carRow(['3', '#12', 'Chase Driver', '--', '--', '--', '--', ''])
    ],
    metrics: []
  };
}

function standingsColumns() {
  return [
    { id: 'standings.class-position', label: 'CLS', dataKey: 'class-position', width: 35, alignment: 'right' },
    { id: 'standings.car-number', label: 'CAR', dataKey: 'car-number', width: 50, alignment: 'right' },
    { id: 'standings.driver', label: 'Driver', dataKey: 'driver', width: 250, alignment: 'left' },
    { id: 'standings.gap', label: 'GAP', dataKey: 'gap', width: 60, alignment: 'right' },
    { id: 'standings.interval', label: 'INT', dataKey: 'interval', width: 60, alignment: 'right' },
    { id: 'standings.fastest-lap', label: 'FAST', dataKey: 'fastest-lap', width: 70, alignment: 'right' },
    { id: 'standings.last-lap', label: 'LAST', dataKey: 'last-lap', width: 70, alignment: 'right' },
    { id: 'standings.pit', label: 'PIT', dataKey: 'pit', width: 30, alignment: 'right' }
  ];
}

function rowCells(row) {
  return [...row.querySelectorAll('td')].map((cell) => cell.textContent.replace(/\s+/g, ' ').trim()).join(' ').trim();
}

function contentText(document) {
  return document.getElementById('content').textContent;
}

function headerRow(headerTitle, headerDetail, carClassColorHex) {
  return {
    cells: [],
    isClassHeader: true,
    isReference: false,
    isPit: false,
    isPartial: false,
    carClassColorHex,
    headerTitle,
    headerDetail
  };
}

function carRow(cells, extra = {}) {
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
