import { bridgeWorkbenchCaseIds, bridgeWorkbenchFixture, isBridgeWorkbenchCase } from './fixtures.mjs';

export function renderBridgeWorkbenchHtml({ view, caseId }) {
  const resolvedCase = isBridgeWorkbenchCase(caseId) ? caseId : 'active-team-live';
  const fixture = bridgeWorkbenchFixture(resolvedCase);

  if (view === 'producer') {
    return renderDocument({
      title: 'Overlay Bridge producer fixture',
      body: renderProducerPanel(fixture),
      caseId: resolvedCase,
      fixtureLabel: fixture.label
    });
  }

  if (view === 'consumer') {
    return renderDocument({
      title: 'Overlay Bridge consumer fixture',
      body: renderConsumerPanel(fixture),
      caseId: resolvedCase,
      fixtureLabel: fixture.label
    });
  }

  return renderDocument({
    title: 'Overlay Bridge local workbench',
    body: renderWorkbench(fixture, resolvedCase),
    caseId: resolvedCase,
    fixtureLabel: fixture.label
  });
}

function renderWorkbench(fixture, caseId) {
  const links = bridgeWorkbenchCaseIds.map((candidate) => {
    const selected = candidate === caseId ? ' aria-current="page"' : '';
    return `<a class="case-link" href="/review/bridge/workbench?case=${encodeURIComponent(candidate)}"${selected}>${escapeHtml(candidate)}</a>`;
  }).join('');

  const producerUrl = `/review/bridge/producer?case=${encodeURIComponent(caseId)}`;
  const consumerUrl = `/review/bridge/consumer?case=${encodeURIComponent(caseId)}`;

  return `
    <main class="workbench" data-workbench-surface="offline-fixture-evidence">
      ${renderBoundaryBanner()}
      <header class="workbench-heading">
        <div>
          <p class="eyebrow">Developer-only review surface</p>
          <h1>Overlay Bridge local workbench</h1>
          <p class="lede">Two independent, immutable contract-fixture documents for one producer-to-consumer outcome. This is not a connected Bridge.</p>
        </div>
        <dl class="fixture-summary">
          <div><dt>Fixture case</dt><dd>${escapeHtml(caseId)}</dd></div>
          <div><dt>Contract truth</dt><dd>synthetic-contract-fixture</dd></div>
          <div><dt>Selected outcome</dt><dd>${escapeHtml(fixture.label)}</dd></div>
        </dl>
      </header>
      <nav class="case-nav" aria-label="Fixture cases">${links}</nav>
      <section class="pane-grid" aria-label="Independent fixture documents">
        <article class="pane-card">
          <header><p class="eyebrow">Document A</p><h2>Producer</h2><a href="${producerUrl}">Open independently</a></header>
          <iframe title="Producer fixture document" src="${producerUrl}" loading="eager" sandbox></iframe>
        </article>
        <article class="pane-card">
          <header><p class="eyebrow">Document B</p><h2>Consumer</h2><a href="${consumerUrl}">Open independently</a></header>
          <iframe title="Consumer fixture document" src="${consumerUrl}" loading="eager" sandbox></iframe>
        </article>
      </section>
      <footer class="boundary-footer">No live app state, telemetry, transport, pairing, Oracle service, or relay is used by this surface.</footer>
    </main>`;
}

function renderProducerPanel(fixture) {
  const producer = fixture.producer;
  return `
    <main class="single-panel" data-workbench-role="producer">
      ${renderBoundaryBanner()}
      <header>
        <p class="eyebrow">Independent fixture document</p>
        <h1>Producer publication summary</h1>
        <p class="lede">Safe contract-shaped summary only. It does not display raw telemetry or encoded payload data.</p>
      </header>
      <section aria-labelledby="producer-publication">
        <h2 id="producer-publication">Publication</h2>
        <dl class="metadata">
          <div><dt>Fixture publisher</dt><dd>${escapeHtml(producer.identity)}</dd></div>
          <div><dt>Publication state</dt><dd>${escapeHtml(producer.publication)}</dd></div>
          <div><dt>Synthetic fixture ID</dt><dd><code>${escapeHtml(producer.fixtureId)}</code></dd></div>
        </dl>
      </section>
      ${renderDetailList('Safe header summary', producer.header)}
      ${renderChipList('Negotiated capabilities', producer.capabilities)}
      ${renderBulletList('Safe payload summary', producer.payload)}
      <p class="panel-note">Fixture truth: <strong>synthetic-contract-fixture</strong>. No connection. No Oracle.</p>
    </main>`;
}

function renderConsumerPanel(fixture) {
  const consumer = fixture.consumer;
  return `
    <main class="single-panel" data-workbench-role="consumer">
      ${renderBoundaryBanner()}
      <header>
        <p class="eyebrow">Independent fixture document</p>
        <h1>Consumer Core-boundary outcome</h1>
        <p class="lede">This shows an expected admission and fact-store boundary outcome. It makes no overlay decision.</p>
      </header>
      <section class="outcome outcome-${escapeHtml(consumer.outcomeTone)}" aria-labelledby="consumer-outcome">
        <p class="eyebrow">Admission outcome</p>
        <h2 id="consumer-outcome">${escapeHtml(consumer.outcome)}</h2>
        <p>${escapeHtml(consumer.coreBoundary)}</p>
      </section>
      ${renderDetailList('Group availability', consumer.availability)}
      ${renderDetailList('Provenance summary', consumer.provenance)}
      <p class="panel-note">Fixture truth: <strong>synthetic-contract-fixture</strong>. No connection. No Oracle.</p>
    </main>`;
}

function renderBoundaryBanner() {
  return `<aside class="boundary-banner" role="note"><strong>Offline fixture evidence.</strong> No connection • Oracle not configured • no live telemetry.</aside>`;
}

function renderDetailList(title, entries) {
  return `<section aria-label="${escapeHtml(title)}"><h2>${escapeHtml(title)}</h2><dl class="detail-list">${entries.map(([term, value]) => `<div><dt>${escapeHtml(term)}</dt><dd>${escapeHtml(value)}</dd></div>`).join('')}</dl></section>`;
}

function renderChipList(title, values) {
  return `<section aria-label="${escapeHtml(title)}"><h2>${escapeHtml(title)}</h2><ul class="chips">${values.map((value) => `<li>${escapeHtml(value)}</li>`).join('')}</ul></section>`;
}

function renderBulletList(title, values) {
  return `<section aria-label="${escapeHtml(title)}"><h2>${escapeHtml(title)}</h2><ul class="bullet-list">${values.map((value) => `<li>${escapeHtml(value)}</li>`).join('')}</ul></section>`;
}

function renderDocument({ title, body, caseId, fixtureLabel }) {
  return `<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>${escapeHtml(title)}</title>
  <style>${styles}</style>
</head>
<body data-fixture-case="${escapeHtml(caseId)}" data-fixture-label="${escapeHtml(fixtureLabel)}">
${body}
</body>
</html>`;
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
  body { margin: 0; min-width: 300px; background: radial-gradient(circle at 20% 0%, #142337 0, #091018 43rem); }
  a { color: #70d5ff; }
  .workbench, .single-panel { max-width: 1280px; margin: 0 auto; padding: 24px; }
  .single-panel { max-width: 780px; }
  .boundary-banner { border: 1px solid #ba8c2e; background: #382a0f; color: #ffdf9d; padding: 11px 13px; border-radius: 7px; font-size: 13px; letter-spacing: .01em; }
  .workbench-heading { display: flex; justify-content: space-between; gap: 24px; align-items: start; margin: 30px 0 18px; }
  .eyebrow { color: #8cb0c9; font-size: 11px; text-transform: uppercase; letter-spacing: .12em; margin: 0 0 6px; font-weight: 700; }
  h1 { font-size: clamp(25px, 4vw, 38px); letter-spacing: -.025em; margin: 0; }
  h2 { font-size: 17px; margin: 25px 0 11px; }
  .lede { color: #b9c9d5; max-width: 680px; line-height: 1.45; margin: 10px 0 0; }
  .fixture-summary { min-width: 260px; margin: 0; display: grid; gap: 8px; }
  .fixture-summary div, .metadata div, .detail-list div { display: grid; grid-template-columns: minmax(110px, .9fr) minmax(0, 1.4fr); gap: 14px; padding: 9px 0; border-bottom: 1px solid #243647; }
  dt { color: #97acbd; font-size: 13px; } dd { margin: 0; font-size: 14px; overflow-wrap: anywhere; }
  .case-nav { display: flex; flex-wrap: wrap; gap: 7px; margin: 20px 0; }
  .case-link { border: 1px solid #36556d; border-radius: 999px; padding: 6px 10px; font-size: 12px; text-decoration: none; color: #cbe6f5; }
  .case-link[aria-current="page"] { background: #0e6f99; border-color: #6dd3ff; color: white; }
  .pane-grid { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 18px; }
  .pane-card { border: 1px solid #294354; background: #0d1822; border-radius: 10px; overflow: hidden; }
  .pane-card > header { display: grid; grid-template-columns: 1fr auto; column-gap: 10px; align-items: center; padding: 16px 16px 12px; }
  .pane-card > header .eyebrow { grid-column: 1 / -1; } .pane-card h2 { margin: 0; }
  iframe { display: block; width: 100%; height: 840px; border: 0; background: #091018; }
  .boundary-footer, .panel-note { color: #9ab0c0; font-size: 12px; line-height: 1.45; margin: 24px 0 0; }
  .metadata, .detail-list { margin: 0; }
  code { color: #dceeff; font-family: ui-monospace, SFMono-Regular, Menlo, monospace; }
  .chips, .bullet-list { margin: 0; padding: 0; list-style: none; display: grid; gap: 8px; }
  .chips { display: flex; flex-wrap: wrap; } .chips li { padding: 6px 10px; border-radius: 999px; background: #10384c; color: #bcecff; font-size: 13px; }
  .bullet-list li { border-left: 3px solid #4f8ba7; background: #0e1c27; padding: 9px 11px; color: #d2dde5; font-size: 14px; }
  .outcome { margin-top: 24px; border: 1px solid #385263; border-left-width: 4px; padding: 15px; border-radius: 6px; background: #0d1822; }
  .outcome h2 { margin: 0; font-size: 20px; } .outcome p:last-child { color: #c4d2dc; line-height: 1.45; margin: 9px 0 0; }
  .outcome-success { border-left-color: #4bc989; } .outcome-warning { border-left-color: #e4aa48; } .outcome-error { border-left-color: #e96575; } .outcome-neutral { border-left-color: #7f9caf; }
  @media (max-width: 820px) { .workbench-heading { display: block; } .fixture-summary { margin-top: 20px; } .pane-grid { grid-template-columns: 1fr; } iframe { height: 840px; } }
`;
