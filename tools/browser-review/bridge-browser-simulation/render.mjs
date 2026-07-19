import {
  bridgeBrowserSimulationCaseIds,
  bridgeBrowserSimulationChannelName,
  bridgeBrowserSimulationEnvelope,
  bridgeBrowserSimulationFixture,
  bridgeBrowserSimulationFixtureTruth,
  isBridgeBrowserSimulationCase
} from './fixtures.mjs';

export function renderBridgeBrowserSimulationHtml({ view, caseId }) {
  const resolvedCase = isBridgeBrowserSimulationCase(caseId) ? caseId : 'current-remote';
  const fixture = bridgeBrowserSimulationFixture(resolvedCase);

  if (view === 'producer') {
    return renderDocument({
      title: 'Overlay Bridge browser-only fixture producer',
      body: renderProducer(fixture, resolvedCase),
      script: renderSimulationScript({ role: 'producer', caseId: resolvedCase })
    });
  }

  if (view === 'receiver') {
    return renderDocument({
      title: 'Overlay Bridge browser-only fixture receiver',
      body: renderReceiver(),
      script: renderSimulationScript({ role: 'receiver', caseId: resolvedCase })
    });
  }

  return renderDocument({
    title: 'Overlay Bridge browser-only local simulation',
    body: renderLauncher(resolvedCase),
    script: ''
  });
}

function renderLauncher(caseId) {
  const producerUrl = `/review/bridge/local/producer?case=${encodeURIComponent(caseId)}`;
  const receiverUrl = `/review/bridge/local/receiver?case=${encodeURIComponent(caseId)}`;
  const caseLinks = bridgeBrowserSimulationCaseIds.map((candidate) => {
    const selected = candidate === caseId ? ' aria-current="page"' : '';
    return `<a class="case-link" href="/review/bridge/local/workbench?case=${encodeURIComponent(candidate)}"${selected}>${escapeHtml(candidate)}</a>`;
  }).join('');

  return `
    <main class="simulation-shell" data-bridge-browser-simulation="launcher">
      ${renderBoundaryBanner()}
      <header>
        <p class="eyebrow">Developer-only local validation</p>
        <h1>Browser-to-browser Bridge simulation</h1>
        <p class="lede">Open the receiver first, then the producer in a second browser tab or window. Publishing sends one deterministic synthetic fixture event through a browser-only channel on this machine.</p>
      </header>
      <nav class="case-nav" aria-label="Browser simulation fixture cases">${caseLinks}</nav>
      <section class="launch-grid" aria-label="Simulation launch links">
        <article>
          <p class="eyebrow">Step 1</p>
          <h2>Receiver</h2>
          <p>Shows expected Core priority metadata after a synthetic browser event is received. It does not execute Core.</p>
          <a class="launch-link" href="${receiverUrl}" target="_blank" rel="noopener">Open receiver</a>
        </article>
        <article>
          <p class="eyebrow">Step 2</p>
          <h2>Producer</h2>
          <p>Publishes the selected safe fixture only when you press its local test button.</p>
          <a class="launch-link" href="${producerUrl}" target="_blank" rel="noopener">Open producer</a>
        </article>
      </section>
      <section class="boundary-card" aria-label="Simulation boundaries">
        <h2>What this proves</h2>
        <ul>
          <li>Two browser documents can pass a named, deterministic fixture event locally.</li>
          <li>The receiver can visibly show the expected source-priority outcome for review.</li>
        </ul>
        <h2>What this does not prove</h2>
        <ul>
          <li>No app integration, Core execution, decoder, encrypted transport, pairing, relay, socket, telemetry, capture, history, or cloud service is involved.</li>
          <li>Fixture decisions are display-only expectations; Windows/Core tests remain the contract authority.</li>
        </ul>
      </section>
    </main>`;
}

function renderProducer(fixture, caseId) {
  const envelope = bridgeBrowserSimulationEnvelope(caseId);
  return `
    <main class="simulation-shell" data-bridge-browser-simulation="producer" data-fixture-case="${escapeHtml(caseId)}">
      ${renderBoundaryBanner()}
      <header>
        <p class="eyebrow">Synthetic producer document</p>
        <h1>Local fixture producer</h1>
        <p class="lede">This page has no production telemetry source. The button below sends one safe fixture event to another local browser document only.</p>
      </header>
      <section class="metadata-card" aria-label="Selected producer fixture">
        <dl class="detail-list">
          <div><dt>Fixture case</dt><dd>${escapeHtml(caseId)}</dd></div>
          <div><dt>Fixture ID</dt><dd><code>${escapeHtml(fixture.id)}</code></dd></div>
          <div><dt>Event kind</dt><dd>${escapeHtml(fixture.eventKind)}</dd></div>
          <div><dt>Sequence label</dt><dd>${escapeHtml(fixture.sequence)}</dd></div>
          <div><dt>Safe summary</dt><dd>${escapeHtml(fixture.producerSummary)}</dd></div>
        </dl>
      </section>
      <button id="publish-fixture" type="button">Publish selected synthetic fixture</button>
      <p id="producer-status" class="runtime-status" role="status">Waiting for a local test action.</p>
      <p class="panel-note">No persistence. Closing this document closes its browser-only channel.</p>
      <script id="bridge-browser-simulation-envelope" type="application/json">${safeJson(envelope)}</script>
    </main>`;
}

function renderReceiver() {
  return `
    <main class="simulation-shell" data-bridge-browser-simulation="receiver">
      ${renderBoundaryBanner()}
      <header>
        <p class="eyebrow">Synthetic receiver document</p>
        <h1>Local fixture receiver</h1>
        <p class="lede">Waiting for a safe fixture event from a separate browser document. Received metadata is display-only: this page does not execute the Core model.</p>
      </header>
      <p id="receiver-status" class="runtime-status" role="status">Waiting for a browser-only fixture event.</p>
      <section id="receiver-decision" class="decision-card" aria-live="polite" aria-label="Expected Core priority decision">
        <p class="eyebrow">Expected Core priority decision</p>
        <h2>Awaiting fixture event</h2>
        <p>Open the producer page and publish one selected synthetic fixture.</p>
      </section>
      <section class="metadata-card" aria-label="Received fixture metadata">
        <h2>Received fixture metadata</h2>
        <dl id="receiver-metadata" class="detail-list">
          <div><dt>Fixture truth</dt><dd>None received</dd></div>
          <div><dt>Browser event</dt><dd>None received</dd></div>
        </dl>
      </section>
      <p class="panel-note">No state is retained when this document closes or reloads.</p>
    </main>`;
}

function renderBoundaryBanner() {
  return `<aside class="boundary-banner" role="note"><strong>Browser-only local simulation.</strong> Synthetic fixture data • no connection to the app • no telemetry • no capture • no cloud relay.</aside>`;
}

function renderSimulationScript({ role, caseId }) {
  const bootstrap = safeJson({
    role,
    caseId,
    channel: bridgeBrowserSimulationChannelName,
    fixtureTruth: bridgeBrowserSimulationFixtureTruth,
    canonicalEnvelopes: Object.fromEntries(
      bridgeBrowserSimulationCaseIds.map((candidate) =>
        [candidate, bridgeBrowserSimulationEnvelope(candidate)]))
  });

  return `<script>
(() => {
  'use strict';
  const bootstrap = ${bootstrap};
  const channelName = bootstrap.channel;
  const status = document.querySelector(bootstrap.role === 'producer' ? '#producer-status' : '#receiver-status');
  const hasBroadcastChannel = typeof BroadcastChannel === 'function';

  if (!hasBroadcastChannel) {
    if (status) status.textContent = 'Browser-only simulation unavailable: this browser does not support BroadcastChannel.';
    return;
  }

  const channel = new BroadcastChannel(channelName);
  window.addEventListener('pagehide', () => channel.close(), { once: true });

  if (bootstrap.role === 'producer') {
    const button = document.querySelector('#publish-fixture');
    const source = document.querySelector('#bridge-browser-simulation-envelope');
    const envelope = source ? JSON.parse(source.textContent || '{}') : null;
    if (!button || !envelope || envelope.fixtureTruth !== bootstrap.fixtureTruth) {
      if (status) status.textContent = 'Browser-only simulation unavailable: fixture envelope is invalid.';
      return;
    }

    button.addEventListener('click', () => {
      channel.postMessage(envelope);
      if (status) status.textContent = 'Published one synthetic fixture event to local browser receivers.';
    });
    if (status) status.textContent = 'Ready to publish one synthetic fixture event to local browser receivers.';
    return;
  }

  channel.addEventListener('message', (event) => {
    const message = event.data;
    if (!isSafeFixtureEnvelope(message, bootstrap)) return;
    renderExpectedDecision(message);
  });
  if (status) status.textContent = 'Ready: waiting for a synthetic fixture event on this local browser channel.';
})();

function isSafeFixtureEnvelope(message, bootstrap) {
  if (!message
    || typeof message !== 'object'
    || message.kind !== 'tmr-overlay-bridge-browser-simulation/v1'
    || message.fixtureTruth !== bootstrap.fixtureTruth
    || message.channel !== bootstrap.channel
    || typeof message.caseId !== 'string') {
    return false;
  }

  const canonical = bootstrap.canonicalEnvelopes?.[message.caseId];
  return canonical && JSON.stringify(message) === JSON.stringify(canonical);
}

function renderExpectedDecision(message) {
  const status = document.querySelector('#receiver-status');
  const decision = message.expectedDecision;
  const decisionPanel = document.querySelector('#receiver-decision');
  const metadata = document.querySelector('#receiver-metadata');
  if (!decisionPanel || !metadata) return;

  if (status) status.textContent = 'Received one synthetic fixture event from a local browser producer.';
  decisionPanel.replaceChildren(
    node('p', 'eyebrow', 'Expected Core priority decision — display only'),
    node('h2', '', decision.availability + ' • ' + decision.source),
    node('p', '', decision.calculation),
    detailList([
      ['Merge policy', decision.mergePolicy],
      ['Expected reason', decision.reason]
    ])
  );
  metadata.replaceChildren(...detailList([
    ['Fixture truth', message.fixtureTruth],
    ['Fixture case', message.caseId],
    ['Fixture ID', message.fixtureId],
    ['Browser event kind', message.eventKind],
    ['Synthetic sequence label', message.sequence]
  ]).childNodes);
}

function node(tagName, className, text) {
  const element = document.createElement(tagName);
  if (className) element.className = className;
  element.textContent = text;
  return element;
}

function detailList(entries) {
  const list = document.createElement('dl');
  list.className = 'detail-list';
  for (const [term, value] of entries) {
    const row = document.createElement('div');
    row.append(node('dt', '', term), node('dd', '', value));
    list.append(row);
  }
  return list;
}
</script>`;
}

function renderDocument({ title, body, script }) {
  return `<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>${escapeHtml(title)}</title>
  <style>${styles}</style>
</head>
<body>
${body}
${script}
</body>
</html>`;
}

function safeJson(value) {
  return JSON.stringify(value)
    .replaceAll('<', '\\u003c')
    .replaceAll('>', '\\u003e')
    .replaceAll('&', '\\u0026')
    .replaceAll('\u2028', '\\u2028')
    .replaceAll('\u2029', '\\u2029');
}

function escapeHtml(value) {
  return String(value)
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
    .replaceAll("'", '&#039;');
}

const styles = `
  :root { color-scheme: dark; font-family: "Segoe UI", system-ui, sans-serif; background: #091018; color: #edf4fa; }
  * { box-sizing: border-box; }
  body { min-width: 320px; min-height: 100vh; margin: 0; background: radial-gradient(circle at 20% 0%, #142337 0, #091018 43rem); }
  .simulation-shell { max-width: 880px; margin: 0 auto; padding: 24px; }
  .boundary-banner { border: 1px solid #ba8c2e; background: #382a0f; color: #ffdf9d; padding: 11px 13px; border-radius: 7px; font-size: 13px; line-height: 1.45; }
  header { margin: 30px 0 20px; } .eyebrow { color: #8cb0c9; font-size: 11px; font-weight: 700; letter-spacing: .12em; margin: 0 0 6px; text-transform: uppercase; }
  h1 { font-size: clamp(26px, 5vw, 42px); letter-spacing: -.025em; margin: 0; } h2 { font-size: 19px; margin: 0 0 12px; }
  .lede { color: #b9c9d5; line-height: 1.5; margin: 10px 0 0; max-width: 720px; }
  .case-nav { display: flex; flex-wrap: wrap; gap: 7px; margin: 22px 0; } .case-link { border: 1px solid #36556d; border-radius: 999px; color: #cbe6f5; font-size: 12px; padding: 6px 10px; text-decoration: none; } .case-link[aria-current="page"] { background: #0e6f99; border-color: #6dd3ff; color: white; }
  .launch-grid { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 16px; } .launch-grid article, .metadata-card, .decision-card, .boundary-card { background: #0d1822; border: 1px solid #294354; border-radius: 10px; padding: 18px; }
  .launch-grid p { color: #c4d2dc; line-height: 1.45; } .launch-link, button { background: #0e6f99; border: 1px solid #6dd3ff; border-radius: 7px; color: white; display: inline-block; font: inherit; padding: 9px 12px; text-decoration: none; } button { cursor: pointer; font-weight: 700; } button:focus-visible, a:focus-visible { outline: 3px solid #ffdf9d; outline-offset: 3px; }
  .boundary-card { margin-top: 18px; } .boundary-card h2 { margin-top: 12px; } .boundary-card ul { color: #c4d2dc; line-height: 1.55; margin: 0; padding-left: 22px; }
  .metadata-card, .decision-card { margin-top: 20px; } .detail-list { margin: 0; } .detail-list > div { border-bottom: 1px solid #243647; display: grid; gap: 14px; grid-template-columns: minmax(130px, .8fr) minmax(0, 1.7fr); padding: 9px 0; } .detail-list > div:last-child { border-bottom: 0; } dt { color: #97acbd; font-size: 13px; } dd { font-size: 14px; margin: 0; overflow-wrap: anywhere; } code { color: #dceeff; font-family: ui-monospace, SFMono-Regular, Menlo, monospace; }
  .runtime-status { background: #0e1c27; border-left: 3px solid #4f8ba7; color: #d2dde5; line-height: 1.45; margin: 16px 0 0; padding: 10px 12px; } .panel-note { color: #9ab0c0; font-size: 12px; line-height: 1.45; margin-top: 20px; }
  .decision-card h2 { font-size: 22px; } .decision-card > p:not(.eyebrow) { color: #c4d2dc; line-height: 1.45; }
  @media (max-width: 640px) { .simulation-shell { padding: 16px; } .launch-grid { grid-template-columns: 1fr; } .detail-list > div { grid-template-columns: 1fr; gap: 3px; } }
`;
