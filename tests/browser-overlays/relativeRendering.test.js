import { afterEach, describe, expect, it } from 'vitest';
import { freshLiveSnapshot, renderBrowserOverlay } from './browserOverlayTestHost.js';

let currentOverlay;

afterEach(() => {
  currentOverlay?.close();
  currentOverlay = null;
});

describe('relative browser rendering', () => {
  it('renders stable configured row slots around the reference row with class-colour accents', async () => {
    currentOverlay = await renderBrowserOverlay('relative', {
      live: freshLiveSnapshot({}),
      model: relativeDisplayModel()
    });

    const rows = [...currentOverlay.document.querySelectorAll('tbody tr')];
    const rowText = rows.map(rowCells);

    expect(rowText).toEqual([
      '',
      '',
      '3 #34 Near Ahead -2.350',
      '5 #55 Focus Driver 0.000',
      '6 #61 Near Behind +1.200 IN',
      '',
      ''
    ]);
    expect(rows[0].classList.contains('placeholder')).toBe(true);
    expect(rows[2].classList.contains('class-colored')).toBe(true);
    expect(rows[2].classList.contains('lap-ahead-1')).toBe(true);
    expect(rows[3].classList.contains('focus')).toBe(true);
    expect(rows[3].classList.contains('class-colored')).toBe(true);
    expect(rows[4].classList.contains('lap-behind-2')).toBe(true);
    expect(currentOverlay.document.getElementById('status')).toBeNull();
    expect(currentOverlay.document.body.textContent).not.toContain('Far Ahead');
    expect(currentOverlay.document.body.textContent).not.toContain('Far Behind');
  });

  it('does not render lap relationship classes when the model omits race-only lap deltas', async () => {
    currentOverlay = await renderBrowserOverlay('relative', {
      live: freshLiveSnapshot({}),
      model: relativeDisplayModel({ includeLapDeltas: false })
    });

    const rows = [...currentOverlay.document.querySelectorAll('tbody tr')];

    expect(rows.some((row) => row.classList.contains('lap-ahead-1'))).toBe(false);
    expect(rows.some((row) => row.classList.contains('lap-behind-2'))).toBe(false);
  });
});

function relativeDisplayModel({ includeLapDeltas = true } = {}) {
  return {
    overlayId: 'relative',
    title: 'Relative',
    status: '5 - 2/4 cars',
    source: 'source: live proximity telemetry',
    bodyKind: 'table',
    columns: [
      { id: 'relative.position', label: 'Pos', dataKey: 'relative-position', width: 38, alignment: 'right' },
      { id: 'relative.driver', label: 'Driver', dataKey: 'driver', width: 250, alignment: 'left' },
      { id: 'relative.gap', label: 'Delta', dataKey: 'gap', width: 70, alignment: 'right' },
      { id: 'relative.pit', label: 'Pit', dataKey: 'pit', width: 30, alignment: 'right' }
    ],
    rows: [
      row(['', '', '', ''], { isPlaceholder: true }),
      row(['', '', '', ''], { isPlaceholder: true }),
      row(['3', '#34 Near Ahead', '-2.350'], { carClassColorHex: '#FFDA59', relativeLapDelta: includeLapDeltas ? 1 : null }),
      row(['5', '#55 Focus Driver', '0.000'], { isReference: true, carClassColorHex: '#33CEFF' }),
      row(['6', '#61 Near Behind', '+1.200', 'IN'], { isPit: true, carClassColorHex: '#FF4FD8', relativeLapDelta: includeLapDeltas ? -2 : null }),
      row(['', '', '', ''], { isPlaceholder: true }),
      row(['', '', '', ''], { isPlaceholder: true })
    ],
    metrics: []
  };
}

function rowCells(row) {
  return [...row.querySelectorAll('td')].map((cell) => cell.textContent.trim()).join(' ').trim();
}

function row(cells, extra = {}) {
  return {
    cells,
    isReference: false,
    isClassHeader: false,
    isPit: false,
    isPartial: false,
    isPlaceholder: false,
    relativeLapDelta: null,
    carClassColorHex: null,
    headerTitle: null,
    headerDetail: null,
    ...extra
  };
}
