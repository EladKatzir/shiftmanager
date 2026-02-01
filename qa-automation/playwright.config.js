// @ts-check
const { defineConfig, devices } = require('@playwright/test');

/**
 * ShiftManager Playwright Configuration
 * @see https://playwright.dev/docs/test-configuration
 */
module.exports = defineConfig({
  testDir: './tests',

  /* Global setup and teardown */
  globalSetup: require.resolve('./global-setup'),
  globalTeardown: require.resolve('./global-teardown'),

  /* Run tests in files in parallel */
  fullyParallel: true,

  /* Fail the build on CI if you accidentally left test.only in the source code. */
  forbidOnly: !!process.env.CI,

  /* Retry on CI only */
  retries: process.env.CI ? 2 : 0,

  /* Opt out of parallel tests on CI. Limit to 2 workers locally for stability - 4 workers causes race conditions. */
  workers: process.env.CI ? 1 : 2,

  /* Reporter to use. See https://playwright.dev/docs/test-reporters */
  reporter: [
    ['html', { outputFolder: 'reports/playwright-report' }],
    ['json', { outputFile: 'reports/test-results.json' }],
    ['list'],
  ],

  /* Shared settings for all the projects below. */
  use: {
    /* Base URL for navigation */
    baseURL: process.env.APP_URL || 'http://localhost:5000',

    /* Collect trace when retrying the failed test. */
    trace: 'on-first-retry',

    /* Screenshot on failure */
    screenshot: 'only-on-failure',

    /* Video on failure */
    video: 'retain-on-failure',
  },

  /* Configure projects for major browsers */
  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
    },

    // Uncomment to test on additional browsers (requires: npx playwright install)
    // {
    //   name: 'firefox',
    //   use: { ...devices['Desktop Firefox'] },
    // },
    //
    // {
    //   name: 'webkit',
    //   use: { ...devices['Desktop Safari'] },
    // },
    //
    // /* Test against mobile viewports. */
    // {
    //   name: 'Mobile Chrome',
    //   use: { ...devices['Pixel 5'] },
    // },
    // {
    //   name: 'Mobile Safari',
    //   use: { ...devices['iPhone 12'] },
    // },
  ],

  /* Run your local dev server before starting the tests */
  // webServer: {
  //   command: 'dotnet run',
  //   url: 'http://localhost:5000',
  //   reuseExistingServer: !process.env.CI,
  //   cwd: '../',
  // },

  /* Global timeout for each test */
  timeout: 30000,

  /* Expect timeout */
  expect: {
    timeout: 5000,
    /* Visual comparison configuration */
    toHaveScreenshot: {
      /* Maximum allowed pixel difference ratio (0.2% = 0.002) */
      maxDiffPixelRatio: 0.002,
      /* Threshold for color differences (0-1, lower = stricter) */
      threshold: 0.2,
      /* Animation tolerance settings */
      animations: 'disabled',
    },
    toMatchSnapshot: {
      maxDiffPixelRatio: 0.002,
    },
  },

  /* Output folder for test artifacts */
  outputDir: 'reports/test-artifacts',
});
