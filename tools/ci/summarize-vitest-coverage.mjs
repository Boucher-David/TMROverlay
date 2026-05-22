import { appendFileSync, readFileSync } from 'node:fs';
import { resolve } from 'node:path';

const summaryPath = resolve('coverage/js/coverage-summary.json');
const summary = JSON.parse(readFileSync(summaryPath, 'utf8'));
const metrics = ['lines', 'branches', 'functions', 'statements'];

const rows = metrics.map((metric) => {
  const value = summary.total?.[metric];
  if (!value) {
    throw new Error(`Missing Vitest coverage metric: ${metric}`);
  }

  return `| ${titleCase(metric)} | ${value.covered} | ${value.total} | ${formatPercent(value.pct)} |`;
});

const markdown = [
  '## JavaScript Coverage',
  '',
  '| Metric | Covered | Total | Percent |',
  '| --- | ---: | ---: | ---: |',
  ...rows,
  ''
].join('\n');

if (process.env.GITHUB_STEP_SUMMARY) {
  appendFileSync(process.env.GITHUB_STEP_SUMMARY, markdown);
} else {
  console.log(markdown);
}

function titleCase(value) {
  return value.slice(0, 1).toUpperCase() + value.slice(1);
}

function formatPercent(value) {
  return `${Number(value).toFixed(2)}%`;
}
