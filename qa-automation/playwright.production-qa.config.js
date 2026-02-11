// @ts-check
const { defineConfig, devices } = require('@playwright/test');
const path = require('path');

/**
 * ShiftManager Production QA Configuration
 * Separate from regular test config — runs the comprehensive release QA suite
 * with evidence screenshots saved to ProductionReady/
 */
module.exports = defineConfig({
  testDir: './tests/production-qa',

  /* No global setup/teardown — production QA manages its own state */
  /* Tests run sequentially in defined order (not parallel) */
  fullyParallel: false,

  /* No retries — we want to see actual failures */
  retries: 0,

  /* Single worker — tests depend on prior state (user creation, etc.) */
  workers: 1,

  /* Reporter */
  reporter: [
    ['html', { outputFolder: '../ProductionReady/playwright-report' }],
    ['json', { outputFile: '../ProductionReady/test-results.json' }],
    ['list'],
  ],

  /* Shared settings */
  use: {
    baseURL: process.env.APP_URL || 'http://localhost:5000',

    /* Always capture screenshots for evidence */
    screenshot: 'on',

    /* Always capture trace for debugging */
    trace: 'on',

    /* Video on failure for debugging */
    video: 'retain-on-failure',

    /* Longer timeouts for complex operations */
    actionTimeout: 15000,
    navigationTimeout: 30000,
  },

  /* Single browser — Chromium desktop */
  projects: [
    {
      name: 'production-qa',
      use: {
        ...devices['Desktop Chrome'],
        viewport: { width: 1920, height: 1080 },
      },
    },
  ],

  /* Global timeout — longer for complex flows */
  timeout: 60000,

  /* Expect timeout */
  expect: {
    timeout: 10000,
  },

  /* Output folder for test artifacts */
  outputDir: '../ProductionReady/test-artifacts',
});
