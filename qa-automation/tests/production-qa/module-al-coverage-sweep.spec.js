// @ts-check
const { test, expect } = require('@playwright/test');
const { loginAsOwner, saveEvidence, navigateTo, BASE_URL } = require('../../helpers/production-qa-helpers');

const EVIDENCE = '34-coverage-sweep';

/**
 * Module AL: Coverage Sweep — verifies that every key page loads without error.
 * Pattern: navigate → assert no error → assert main content visible → save evidence.
 */

// Helper: assert page loaded successfully (no error page, main content visible)
async function assertPageLoads(page, url, evidenceName) {
  await navigateTo(page, url);
  await page.waitForLoadState('networkidle');

  // ASSERT: Not an error page
  const errorPage = page.locator('.error-page, .error-content, [data-error]');
  const errorCount = await errorPage.count();
  if (errorCount > 0) {
    const visible = await errorPage.first().isVisible();
    expect(visible).toBe(false);
  }

  // ASSERT: Main content area exists
  const main = page.locator('main, .app-content, .page-content, #main-content').first();
  await expect(main).toBeVisible({ timeout: 10000 });

  await saveEvidence(page, EVIDENCE, evidenceName);
}

test.describe('Module AL: Coverage Sweep — Owner Pages', () => {
  test.beforeEach(async ({ page }) => {
    await loginAsOwner(page);
  });

  test('AL-01: Owner dashboard loads', async ({ page }) => {
    await assertPageLoads(page, '/Owner', 'AL-01-owner-dashboard.png');
  });

  test('AL-02: Owner Blueprints page loads', async ({ page }) => {
    await assertPageLoads(page, '/Owner/Blueprints', 'AL-02-owner-blueprints.png');
  });

  test('AL-03: Owner Programs page loads', async ({ page }) => {
    await assertPageLoads(page, '/Owner/Programs', 'AL-03-owner-programs.png');
  });

  test('AL-04: Owner Feature Flags page loads', async ({ page }) => {
    await assertPageLoads(page, '/Owner/FeatureFlags', 'AL-04-feature-flags.png');
  });

  test('AL-05: Owner System Health page loads', async ({ page }) => {
    await assertPageLoads(page, '/Owner/SystemHealth', 'AL-05-system-health.png');
  });

  test('AL-06: Owner Backup page loads', async ({ page }) => {
    await assertPageLoads(page, '/Owner/Backup', 'AL-06-backup.png');
  });

  test('AL-07: Owner Locked Users page loads', async ({ page }) => {
    await assertPageLoads(page, '/Owner/LockedUsers', 'AL-07-locked-users.png');
  });
});

test.describe('Module AL: Coverage Sweep — Admin Pages', () => {
  test.beforeEach(async ({ page }) => {
    await loginAsOwner(page);
  });

  test('AL-08: Admin Users page loads', async ({ page }) => {
    await assertPageLoads(page, '/Admin/Users', 'AL-08-admin-users.png');
  });

  test('AL-09: Admin Organization Hierarchy page loads', async ({ page }) => {
    await assertPageLoads(page, '/Admin/Organization/Hierarchy', 'AL-09-hierarchy.png');
  });

  test('AL-10: Admin Announcements page loads', async ({ page }) => {
    await assertPageLoads(page, '/Admin/Announcements', 'AL-10-announcements.png');
  });

  test('AL-11: Admin Config page loads', async ({ page }) => {
    await assertPageLoads(page, '/Admin/Config', 'AL-11-config.png');
  });

  test('AL-12: Admin Audit Log page loads', async ({ page }) => {
    await assertPageLoads(page, '/Admin/AuditLog', 'AL-12-audit-log.png');
  });
});

test.describe('Module AL: Coverage Sweep — My & Calendar Pages', () => {
  test.beforeEach(async ({ page }) => {
    await loginAsOwner(page);
  });

  test('AL-13: My Profile page loads', async ({ page }) => {
    await assertPageLoads(page, '/My/Profile', 'AL-13-my-profile.png');
  });

  test('AL-14: My Settings page loads', async ({ page }) => {
    await assertPageLoads(page, '/My/Settings', 'AL-14-my-settings.png');
  });

  test('AL-15: Calendar Shifts page loads', async ({ page }) => {
    await assertPageLoads(page, '/Calendar/Shifts', 'AL-15-calendar-shifts.png');
  });
});
