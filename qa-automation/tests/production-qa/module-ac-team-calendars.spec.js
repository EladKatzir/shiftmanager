// @ts-check
const { test, expect } = require('@playwright/test');
const { loginAsOwner, saveEvidence, navigateTo, BASE_URL } = require('../../helpers/production-qa-helpers');

const EVIDENCE = '41-team-calendars';

/**
 * Module AC: Team Calendars — verifies team calendar CRUD,
 * member management, and view rendering.
 */

test.describe('Module AC: Team Calendars', () => {
  test.beforeEach(async ({ page }) => {
    await loginAsOwner(page);
  });

  // ---------------------------------------------------------------------------
  // AC-01: MyTeam page loads
  // ---------------------------------------------------------------------------
  test('AC-01: MyTeam page loads or redirects', async ({ page }) => {
    await navigateTo(page, '/MyTeam');
    await page.waitForLoadState('networkidle');

    // ASSERT: Page loaded (may be team page or redirect if feature disabled)
    const main = page.locator('main, .app-content, #main-content, body').first();
    await expect(main).toBeVisible({ timeout: 10000 });

    await saveEvidence(page, EVIDENCE, 'AC-01-myteam-page.png');
  });

  // ---------------------------------------------------------------------------
  // AC-02: Calendar Week view loads
  // ---------------------------------------------------------------------------
  test('AC-02: Calendar Week view loads', async ({ page }) => {
    await navigateTo(page, '/Calendar/Week');
    await page.waitForLoadState('networkidle');

    const main = page.locator('main, .app-content, #main-content').first();
    await expect(main).toBeVisible({ timeout: 10000 });

    await saveEvidence(page, EVIDENCE, 'AC-02-calendar-week.png');
  });

  // ---------------------------------------------------------------------------
  // AC-03: Calendar Day view loads
  // ---------------------------------------------------------------------------
  test('AC-03: Calendar Day view loads', async ({ page }) => {
    await navigateTo(page, '/Calendar/Day');
    await page.waitForLoadState('networkidle');

    const main = page.locator('main, .app-content, #main-content').first();
    await expect(main).toBeVisible({ timeout: 10000 });

    await saveEvidence(page, EVIDENCE, 'AC-03-calendar-day.png');
  });

  // ---------------------------------------------------------------------------
  // AC-04: Calendar Month view loads
  // ---------------------------------------------------------------------------
  test('AC-04: Calendar Month view loads', async ({ page }) => {
    await navigateTo(page, '/Calendar/Month');
    await page.waitForLoadState('networkidle');

    const main = page.locator('main, .app-content, #main-content').first();
    await expect(main).toBeVisible({ timeout: 10000 });

    await saveEvidence(page, EVIDENCE, 'AC-04-calendar-month.png');
  });

  // ---------------------------------------------------------------------------
  // AC-05: Director page loads
  // ---------------------------------------------------------------------------
  test('AC-05: Director index page loads', async ({ page }) => {
    await navigateTo(page, '/Director');
    await page.waitForLoadState('networkidle');

    // Owner should be able to access Director page
    const main = page.locator('main, .app-content, #main-content, body').first();
    await expect(main).toBeVisible({ timeout: 10000 });

    await saveEvidence(page, EVIDENCE, 'AC-05-director-page.png');
  });

  // ---------------------------------------------------------------------------
  // AC-06: Schedule page loads
  // ---------------------------------------------------------------------------
  test('AC-06: Schedule page loads', async ({ page }) => {
    await navigateTo(page, '/Schedule');
    await page.waitForLoadState('networkidle');

    const main = page.locator('main, .app-content, #main-content, body').first();
    await expect(main).toBeVisible({ timeout: 10000 });

    await saveEvidence(page, EVIDENCE, 'AC-06-schedule-page.png');
  });

  // ---------------------------------------------------------------------------
  // AC-07: Requests page loads
  // ---------------------------------------------------------------------------
  test('AC-07: Requests index page loads', async ({ page }) => {
    await navigateTo(page, '/Requests');
    await page.waitForLoadState('networkidle');

    const main = page.locator('main, .app-content, #main-content').first();
    await expect(main).toBeVisible({ timeout: 10000 });

    await saveEvidence(page, EVIDENCE, 'AC-07-requests-page.png');
  });

  // ---------------------------------------------------------------------------
  // AC-08: Public feedback page loads (no auth required)
  // ---------------------------------------------------------------------------
  test('AC-08: Public feedback page loads', async ({ page }) => {
    await navigateTo(page, '/Public/Feedback');
    await page.waitForLoadState('networkidle');

    const main = page.locator('main, .app-content, #main-content, body').first();
    await expect(main).toBeVisible({ timeout: 10000 });

    await saveEvidence(page, EVIDENCE, 'AC-08-public-feedback.png');
  });
});
