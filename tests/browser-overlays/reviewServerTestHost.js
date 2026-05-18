import { spawn } from 'node:child_process';
import { createServer } from 'node:net';
import { setTimeout as delay } from 'node:timers/promises';
import { repoRoot } from './browserOverlayAssets.js';

export async function startReviewServer() {
  const port = await reservePort();
  const baseUrl = `http://127.0.0.1:${port}`;
  let output = '';
  const processHandle = spawn(process.execPath, ['tools/browser-review/server.mjs'], {
    cwd: repoRoot,
    env: {
      ...process.env,
      TMR_BROWSER_REVIEW_PORT: String(port)
    },
    stdio: ['ignore', 'pipe', 'pipe']
  });

  processHandle.stdout.on('data', (chunk) => {
    output += chunk.toString();
  });
  processHandle.stderr.on('data', (chunk) => {
    output += chunk.toString();
  });

  await waitForServer({ baseUrl, processHandle, output: () => output });

  return {
    baseUrl,
    get output() {
      return output;
    },
    getText: (path) => getText(baseUrl, path),
    getJson: (path) => getJson(baseUrl, path),
    postReviewPatch: (patch) => postReviewPatch(baseUrl, patch),
    stop: () => stopServer(processHandle)
  };
}

async function reservePort() {
  const server = createServer();
  await new Promise((resolve, reject) => {
    server.once('error', reject);
    server.listen(0, '127.0.0.1', resolve);
  });
  const address = server.address();
  await new Promise((resolve, reject) => {
    server.close((error) => error ? reject(error) : resolve());
  });
  if (!address || typeof address === 'string') {
    throw new Error('Unable to reserve review server port');
  }
  return address.port;
}

async function waitForServer({ baseUrl, processHandle, output }) {
  const deadline = Date.now() + 8000;
  let lastError;
  while (Date.now() < deadline) {
    try {
      const response = await fetch(`${baseUrl}/review`);
      if (response.ok) {
        return;
      }
    } catch (error) {
      lastError = error;
    }

    if (processHandle.exitCode !== null) {
      throw new Error(`browser review server exited early: ${output()}`);
    }

    await delay(100);
  }

  throw new Error(`Timed out waiting for browser review server: ${lastError?.message || output()}`);
}

async function getJson(baseUrl, path) {
  const response = await fetch(`${baseUrl}${path}`);
  if (!response.ok) {
    throw new Error(`GET ${path} failed ${response.status}: ${await response.text()}`);
  }
  return response.json();
}

async function getText(baseUrl, path) {
  const response = await fetch(`${baseUrl}${path}`);
  if (!response.ok) {
    throw new Error(`GET ${path} failed ${response.status}: ${await response.text()}`);
  }
  return response.text();
}

async function postReviewPatch(baseUrl, patch) {
  const response = await fetch(`${baseUrl}/api/review/settings`, {
    method: 'POST',
    headers: {
      'content-type': 'application/json'
    },
    body: JSON.stringify(patch)
  });
  if (!response.ok) {
    throw new Error(`Review patch failed ${response.status}: ${await response.text()}`);
  }
  return response.json();
}

async function stopServer(processHandle) {
  if (!processHandle || processHandle.exitCode !== null || processHandle.killed) {
    return;
  }

  const exited = new Promise((resolve) => {
    processHandle.once('exit', resolve);
  });
  processHandle.kill('SIGTERM');
  await Promise.race([exited, delay(1000)]);
}
