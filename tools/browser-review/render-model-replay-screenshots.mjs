#!/usr/bin/env node
import { chromium } from '@playwright/test';
import { createHash } from 'node:crypto';
import { createServer } from 'node:http';
import {
  existsSync,
  mkdirSync,
  readFileSync,
  writeFileSync
} from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import {
  browserOverlayPage,
  browserOverlayPages,
  renderOverlayHtml,
  repoRoot
} from '../../tests/browser-overlays/browserOverlayAssets.js';

const args = parseArgs(process.argv.slice(2));
const browserChannel = process.env.TMR_PLAYWRIGHT_CHANNEL || undefined;
const forensicsOutput = resolve(requiredArg(args, 'forensics-output'));
const renderer = String(args.renderer || 'browser').trim().toLowerCase();
const overlays = csv(args.overlays).length
  ? csv(args.overlays)
  : browserOverlayPages().map((page) => page.page.id);
const port = Number.parseInt(args.port || process.env.TMR_MODEL_REPLAY_SCREENSHOT_PORT || '5198', 10);
const limitPerOverlay = Math.max(1, Number.parseInt(args.limit || '40', 10));
const settleMs = Math.max(0, Number.parseInt(args.settleMs || args['settle-ms'] || '450', 10));
const modelRows = loadModelRows(forensicsOutput, overlays);
const baseUrl = `http://127.0.0.1:${port}`;
const server = createReplayServer(modelRows);

await new Promise((resolveListen) => server.listen(port, '127.0.0.1', resolveListen));

const runManifest = {
  schemaVersion: 1,
  tool: 'tools/browser-review/render-model-replay-screenshots.mjs',
  renderer,
  forensicsOutput,
  baseUrl,
  generatedAtUtc: new Date().toISOString(),
  overlays: {}
};

try {
  const browser = await chromium.launch(browserChannel ? { channel: browserChannel } : {});
  const context = await browser.newContext({
    viewport: { width: 1280, height: 760 },
    deviceScaleFactor: 1
  });
  const page = await context.newPage();

  for (const overlayId of overlays) {
    runManifest.overlays[overlayId] = await captureOverlay(page, overlayId, modelRows.get(overlayId) || []);
  }

  await browser.close();
} finally {
  await new Promise((resolveClose) => server.close(resolveClose));
}

writeJson(
  join(forensicsOutput, `renderer-replay-${renderer}-result.json`),
  runManifest);

function createReplayServer(rowsByOverlay) {
  return createServer((request, response) => {
    const url = new URL(request.url || '/', `http://${request.headers.host || `127.0.0.1:${port}`}`);
    const path = normalizePath(url.pathname);
    try {
      const overlayId = overlayIdFromPath(path);
      if (overlayId) {
        browserOverlayPage(overlayId);
        serveHtml(response, renderOverlayHtml(overlayId));
        return;
      }

      if (path.startsWith('/api/overlay-model/')) {
        const requestedOverlay = decodeURIComponent(path.slice('/api/overlay-model/'.length)).trim().toLowerCase();
        const row = selectModelRow(rowsByOverlay.get(requestedOverlay) || [], url.searchParams);
        if (!row) {
          serveJson(response, 404, { error: 'model_replay_row_not_found', overlayId: requestedOverlay });
          return;
        }

        serveJson(response, 200, row.response || {
          generatedAtUtc: row.generatedAtUtc || new Date().toISOString(),
          model: row.model
        });
        return;
      }

      if (path === '/api/browser-source-event') {
        serveJson(response, 200, { ok: true });
        return;
      }

      if (path === '/api/garage-cover/default-image' || path === '/api/garage-cover/image') {
        serveBinary(
          response,
          200,
          'image/png',
          readFileSync(resolve(repoRoot, 'assets/brand/Team_Logo_4k_TMRBRANDING.png')));
        return;
      }

      serveText(response, 404, 'Not found');
    } catch (error) {
      serveText(response, 500, error instanceof Error ? error.stack || error.message : String(error));
    }
  });
}

async function captureOverlay(page, overlayId, rows) {
  const selectedRows = rows
    .filter((row) => Number.isInteger(row.frameIndex) && row.response?.model)
    .slice(0, limitPerOverlay);
  const manifest = {
    schemaVersion: 1,
    overlayId,
    renderer,
    status: selectedRows.length ? 'produced' : 'skipped',
    reason: selectedRows.length ? null : 'no production model rows found',
    screenshotCount: 0,
    screenshots: []
  };
  const overlayRoot = join(forensicsOutput, 'overlays', overlayId);
  const screenshotRoot = join(overlayRoot, 'screenshots', renderer);
  mkdirSync(screenshotRoot, { recursive: true });

  for (const row of selectedRows) {
    const fileName = `frame-${String(row.frameIndex).padStart(6, '0')}.png`;
    const relativePath = `screenshots/${renderer}/${fileName}`;
    const absolutePath = join(overlayRoot, relativePath);
    const url = `${baseUrl}/overlays/${overlayId}?frame=${encodeURIComponent(String(row.frameIndex))}`;
    await page.goto(url, { waitUntil: 'networkidle' });
    await page.waitForTimeout(settleMs);

    const overlay = page.locator('.overlay');
    const overlayCount = await overlay.count();
    let visibleText = '';
    let imageHash = null;
    let captureStatus = 'missing-overlay-element';
    const shouldRender = row.response?.model?.shouldRender ?? null;
    if (overlayCount > 0 && shouldRender !== false) {
      visibleText = normalizeVisibleText(await overlay.first().innerText().catch(() => ''));
      await overlay.first().screenshot({ path: absolutePath }).catch(async () => {
        await page.screenshot({ path: absolutePath, fullPage: true });
        captureStatus = 'page-fallback-captured';
      });
      imageHash = sha256(readFileSync(absolutePath));
      if (captureStatus !== 'page-fallback-captured') {
        captureStatus = 'captured';
      }
      manifest.screenshotCount++;
    } else if (shouldRender === false) {
      await page.screenshot({ path: absolutePath, fullPage: true });
      imageHash = sha256(readFileSync(absolutePath));
      captureStatus = 'model-hidden-page-captured';
      manifest.screenshotCount++;
    }

    manifest.screenshots.push({
      status: captureStatus,
      captureId: row.captureId ?? null,
      source: row.source ?? null,
      modelSource: row.modelSource ?? null,
      cadence: row.cadence ?? null,
      replayProvenance: replayProvenanceEvidence(row),
      frameIndex: row.frameIndex,
      capturedAtUtc: row.capturedAtUtc,
      capturedUnixMs: row.capturedUnixMs ?? null,
      sessionTimeSeconds: row.sessionTimeSeconds,
      sessionTick: row.sessionTick ?? null,
      sessionInfoUpdate: row.sessionInfoUpdate,
      samplePlan: samplePlanEvidence(row.samplePlan),
      modelHash: sha256(JSON.stringify(row.response)),
      imageHash,
      path: relativePath,
      shouldRender,
      modelStatus: row.response?.model?.status ?? null,
      bodyKind: row.response?.model?.bodyKind ?? null,
      visibleText
    });
  }

  writeJson(join(overlayRoot, 'screenshot-manifest.json'), manifest);
  return {
    status: manifest.status,
    screenshotCount: manifest.screenshotCount,
    modelRowCount: rows.length,
    selectedRowCount: selectedRows.length,
    manifestPath: `overlays/${overlayId}/screenshot-manifest.json`
  };
}

function replayProvenanceEvidence(row) {
  if (row?.replayProvenance && typeof row.replayProvenance === 'object') {
    return row.replayProvenance;
  }

  return {
    schemaVersion: 1,
    sourceKind: 'production-model-replay',
    modelSource: row?.modelSource ?? null,
    captureId: row?.captureId ?? null,
    overlayId: row?.overlayId ?? null,
    frameIndex: row?.frameIndex ?? null,
    capturedAtUtc: row?.capturedAtUtc ?? null,
    capturedUnixMs: row?.capturedUnixMs ?? null,
    sessionTimeSeconds: row?.sessionTimeSeconds ?? null,
    sessionTick: row?.sessionTick ?? null,
    sessionInfoUpdate: row?.sessionInfoUpdate ?? null,
    cadence: row?.cadence ?? null,
    sampleReasons: Array.isArray(row?.samplePlan?.reasons) ? row.samplePlan.reasons : [],
    sampleEventIds: Array.isArray(row?.samplePlan?.eventIds) ? row.samplePlan.eventIds : [],
    sampleOverlayIds: Array.isArray(row?.samplePlan?.overlayIds) ? row.samplePlan.overlayIds : []
  };
}

function samplePlanEvidence(samplePlan) {
  if (!samplePlan || typeof samplePlan !== 'object') {
    return null;
  }

  return {
    reasons: Array.isArray(samplePlan.reasons) ? samplePlan.reasons : [],
    eventIds: Array.isArray(samplePlan.eventIds) ? samplePlan.eventIds : [],
    overlayIds: Array.isArray(samplePlan.overlayIds) ? samplePlan.overlayIds : []
  };
}

function selectModelRow(rows, searchParams) {
  if (!rows.length) {
    return null;
  }

  const requestedFrame = Number.parseInt(searchParams.get('frame') || searchParams.get('frameIndex') || '', 10);
  if (Number.isInteger(requestedFrame)) {
    return rows.find((row) => row.frameIndex === requestedFrame) || rows[0];
  }

  return rows[0];
}

function loadModelRows(root, overlayIds) {
  const byOverlay = new Map();
  for (const overlayId of overlayIds) {
    const path = join(root, 'overlays', overlayId, 'models.jsonl');
    byOverlay.set(overlayId, existsSync(path)
      ? readJsonLines(path).filter((row) => row && row.modelSource === 'production-live-store-browser-overlay-model-factory')
      : []);
  }

  return byOverlay;
}

function readJsonLines(path) {
  return readFileSync(path, 'utf8')
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter(Boolean)
    .map((line) => JSON.parse(line));
}

function overlayIdFromPath(path) {
  const page = browserOverlayPages().find((candidate) =>
    candidate.route === path || candidate.aliases.includes(path));
  return page?.page.id || null;
}

function normalizePath(path) {
  const normalized = String(path || '/').trim().toLowerCase().replace(/\/+$/, '');
  return normalized || '/';
}

function serveHtml(response, body) {
  serveBuffer(response, 200, 'text/html; charset=utf-8', Buffer.from(body, 'utf8'));
}

function serveText(response, statusCode, body) {
  serveBuffer(response, statusCode, 'text/plain; charset=utf-8', Buffer.from(body, 'utf8'));
}

function serveJson(response, statusCode, body) {
  serveBuffer(response, statusCode, 'application/json; charset=utf-8', Buffer.from(JSON.stringify(body), 'utf8'));
}

function serveBinary(response, statusCode, contentType, body) {
  serveBuffer(response, statusCode, contentType, body);
}

function serveBuffer(response, statusCode, contentType, body) {
  response.writeHead(statusCode, {
    'content-type': contentType,
    'cache-control': 'no-store',
    'access-control-allow-origin': '*'
  });
  response.end(body);
}

function writeJson(path, document) {
  mkdirSync(dirname(path), { recursive: true });
  writeFileSync(path, `${JSON.stringify(document, null, 2)}\n`);
}

function normalizeVisibleText(value) {
  return String(value || '').replace(/\s+/g, ' ').trim();
}

function sha256(value) {
  return createHash('sha256').update(value).digest('hex');
}

function csv(value) {
  return String(value || '')
    .split(',')
    .map((item) => item.trim())
    .filter(Boolean);
}

function requiredArg(parsedArgs, name) {
  const value = parsedArgs[name];
  if (!value) {
    throw new Error(`Missing required --${name}`);
  }

  return value;
}

function parseArgs(rawArgs) {
  const parsed = {};
  for (let index = 0; index < rawArgs.length; index++) {
    const arg = rawArgs[index];
    if (!arg.startsWith('--')) {
      throw new Error(`Unexpected argument: ${arg}`);
    }

    const key = arg.slice(2);
    const next = rawArgs[index + 1];
    if (!next || next.startsWith('--')) {
      parsed[key] = 'true';
      continue;
    }

    parsed[key] = next;
    index++;
  }

  return parsed;
}
