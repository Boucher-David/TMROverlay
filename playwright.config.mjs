import { defineConfig } from '@playwright/test';

const browserChannel = process.env.TMR_PLAYWRIGHT_CHANNEL || undefined;

export default defineConfig({
  testDir: 'tests/browser-overlays',
  testMatch: '**/*.pw.spec.js',
  // Browser/context startup occasionally exceeds the normal test budget on
  // shared CI runners. Keep behavioral assertions short in their specs, but
  // give fixture setup enough time and retry once in a fresh context on CI.
  timeout: 30000,
  retries: process.env.CI ? 1 : 0,
  reporter: 'list',
  use: {
    browserName: 'chromium',
    headless: true,
    viewport: { width: 800, height: 600 },
    ...(browserChannel ? { channel: browserChannel } : {})
  }
});
