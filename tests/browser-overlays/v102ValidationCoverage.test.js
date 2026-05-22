import { describe, expect, it } from 'vitest';
import { existsSync, readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { repoRoot } from './browserOverlayAssets.js';

const coveragePath = resolve(repoRoot, 'tools/validation/v102-confirmed-fix-coverage.json');
const trackerPath = resolve(repoRoot, 'docs/v1.0.2-feedback.md');

describe('v1.0.2 confirmed fix validation coverage', () => {
  it('keeps every non-deferred V102 tracker item tied to validation evidence', () => {
    const coverage = JSON.parse(readFileSync(coveragePath, 'utf8'));
    const tracker = readFileSync(trackerPath, 'utf8');
    const rows = trackerRows(tracker);
    const deferred = new Set(coverage.deferredIds || []);
    const itemsById = new Map((coverage.items || []).map((item) => [item.id, item]));
    const confirmedRows = rows.filter((row) => !deferred.has(row.id));

    expect.soft(coverage.contract).toBe('v102-confirmed-fix-validation-coverage/v1');
    expect.soft(confirmedRows.length, 'confirmed V102 rows parsed from docs').toBeGreaterThan(40);

    for (const row of confirmedRows) {
      const item = itemsById.get(row.id);
      expect.soft(item, `${row.id}: missing coverage entry`).toBeTruthy();
      if (!item) {
        continue;
      }

      expect.soft(item.status, `${row.id}: coverage status drifted from tracker`).toBe(row.status);
      expect
        .soft(String(item.validation || '').trim().length, `${row.id}: coverage needs a concrete validation claim`)
        .toBeGreaterThanOrEqual(16);
      expect.soft(Array.isArray(item.sources) && item.sources.length > 0, `${row.id}: coverage needs source files`).toBe(true);

      for (const source of item.sources || []) {
        const absolute = resolve(repoRoot, source);
        expect.soft(existsSync(absolute), `${row.id}: coverage source missing: ${source}`).toBe(true);
      }
    }

    for (const item of coverage.items || []) {
      expect.soft(rows.some((row) => row.id === item.id), `${item.id}: coverage entry not found in tracker`).toBe(true);
      expect.soft(deferred.has(item.id), `${item.id}: deferred item must not be listed as confirmed coverage`).toBe(false);
    }
  });
});

function trackerRows(markdown) {
  return markdown
    .split('\n')
    .map((line) => line.trim())
    .filter((line) => /^\| V102-\d{3} \|/.test(line))
    .map((line) => {
      const cells = line
        .split('|')
        .slice(1, -1)
        .map((cell) => cell.trim());
      return {
        id: cells[0],
        feedback: cells[1],
        surface: cells[2],
        decision: cells[3],
        status: cells[4]
      };
    });
}
