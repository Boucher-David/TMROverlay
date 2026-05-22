import { defineConfig } from 'vitest/config';

export default defineConfig({
  test: {
    environment: 'jsdom',
    include: ['tests/browser-overlays/**/*.test.js'],
    testTimeout: 5000,
    coverage: {
      provider: 'v8',
      all: true,
      reportsDirectory: 'coverage/js',
      reporter: ['text', 'html', 'lcov', 'json-summary'],
      include: [
        'src/TmrOverlay.App/Overlays/BrowserSources/Assets/modules/**/*.js',
        'src/TmrOverlay.App/Overlays/BrowserSources/Assets/scripts/**/*.js',
        'tools/validation/**/*.mjs'
      ],
      exclude: [
        '**/*.test.js',
        '**/*.pw.spec.js',
        'tests/**',
        'node_modules/**',
        'artifacts/**',
        'coverage/**',
        'src/TmrOverlay.App/Overlays/BrowserSources/Assets/contracts/**',
        'src/TmrOverlay.App/Overlays/BrowserSources/Assets/styles/**',
        'src/TmrOverlay.App/Overlays/BrowserSources/Assets/templates/**',
        'tools/browser-review/**'
      ]
    }
  }
});
