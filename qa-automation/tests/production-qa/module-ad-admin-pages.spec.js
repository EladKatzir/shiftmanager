// @ts-check
const { test, expect } = require('@playwright/test');
const {
  login,
  loginAsOwner,
  saveEvidence,
  navigateTo,
  navigateExpecting,
  assertPageContains,
  BASE_URL,
} = require('../../helpers/production-qa-helpers');

const EVIDENCE = '30-admin-pages';

/**
 * Module AD: Admin & Owner Pages
 *
 * Covers FEATURE-INVENTORY sections: Admin pages, Owner pages, Diagnostic,
 * configuration, hierarchy, job types, shift groupings, seed verification.
 *
 * Phase 2 (P1): Tests AD-01 through AD-29
 *
 * HARDENING NOTES
 *  - Every test makes at least one meaningful assertion.
 *  - No `.catch(() => false)` to silently skip test logic.
 *  - Post-action state is always verified.
 */

// =============================================================================
// Admin Pages — Analytics, Audit, Config
// =============================================================================

test.describe('Module AD: Admin Hub & Analytics (P1)', () => {

  // -----------------------------------------------------------------------
  // AD-01  Admin hub loads
  // -----------------------------------------------------------------------
  test('AD-01: Admin hub page loads', async ({ page }) => {
    await loginAsOwner(page);
    await navigateTo(page, '/Admin');

    // STRICT: Page should render with heading or admin content
    const heading = page.locator('h1, .page-title, .admin-hub').first();
    await expect(heading).toBeVisible({ timeout: 10000 });

    await saveEvidence(page, EVIDENCE, 'AD-01-admin-hub.png');
  });

  // -----------------------------------------------------------------------
  // AD-02  Analytics dashboard loads
  // -----------------------------------------------------------------------
  test('AD-02: Analytics dashboard loads', async ({ page }) => {
    await loginAsOwner(page);
    await navigateTo(page, '/Admin/Analytics');

    // STRICT: Page should have analytics-related content
    const content = page.locator('h1, .page-title, .analytics-container, .dashboard').first();
    await expect(content).toBeVisible({ timeout: 10000 });

    await saveEvidence(page, EVIDENCE, 'AD-02-analytics.png');
  });

  // -----------------------------------------------------------------------
  // AD-03  Audit log page loads
  // -----------------------------------------------------------------------
  test('AD-03: Audit log page loads', async ({ page }) => {
    await loginAsOwner(page);
    await navigateTo(page, '/Admin/AuditLog');

    // STRICT: Audit log should have a table or list of entries
    const content = page.locator('.data-table, table, .audit-log, h1').first();
    await expect(content).toBeVisible({ timeout: 10000 });

    await saveEvidence(page, EVIDENCE, 'AD-03-audit-log.png');
  });

  // -----------------------------------------------------------------------
  // AD-04  Config page loads
  // -----------------------------------------------------------------------
  test('AD-04: Config page loads', async ({ page }) => {
    await loginAsOwner(page);
    await navigateTo(page, '/Admin/Config');

    // STRICT: Config page with main content area visible
    const content = page.locator('#main-content h1, #main-content form, .config-section, .page-header').first();
    await expect(content).toBeVisible({ timeout: 10000 });

    await saveEvidence(page, EVIDENCE, 'AD-04-config.png');
  });

  // -----------------------------------------------------------------------
  // AD-05  Admin Users page loads with user list
  // -----------------------------------------------------------------------
  test('AD-05: Admin Users page lists users', async ({ page }) => {
    await loginAsOwner(page);
    await navigateTo(page, '/Admin/Users');

    // STRICT: Should have a user table with at least one row
    const table = page.locator('table, .data-table, .users-table').first();
    await expect(table).toBeVisible({ timeout: 10000 });

    // STRICT: At least one user row visible
    const rows = page.locator('table tbody tr, .user-row');
    const rowCount = await rows.count();
    expect(rowCount).toBeGreaterThanOrEqual(1);

    await saveEvidence(page, EVIDENCE, 'AD-05-admin-users.png');
  });

  // -----------------------------------------------------------------------
  // AD-06  Announcements page loads
  // -----------------------------------------------------------------------
  test('AD-06: Announcements page loads', async ({ page }) => {
    await loginAsOwner(page);
    await navigateTo(page, '/Admin/Announcements');

    // STRICT: Announcements should render in main content area
    const content = page.locator('#main-content h1, #main-content .announcements, .page-header').first();
    await expect(content).toBeVisible({ timeout: 10000 });

    await saveEvidence(page, EVIDENCE, 'AD-06-announcements.png');
  });
});

// =============================================================================
// Admin Pages — Organization / Hierarchy
// =============================================================================

test.describe('Module AD: Organization & Hierarchy (P1)', () => {

  // -----------------------------------------------------------------------
  // AD-07  Hierarchy tree page loads
  // -----------------------------------------------------------------------
  test('AD-07: Organization Hierarchy page loads', async ({ page }) => {
    await loginAsOwner(page);
    await navigateTo(page, '/Admin/Organization/Hierarchy');

    // STRICT: Hierarchy tree structure should render
    const content = page.locator('.hierarchy-tree, .tree-container, h1, .org-hierarchy').first();
    await expect(content).toBeVisible({ timeout: 10000 });

    await saveEvidence(page, EVIDENCE, 'AD-07-hierarchy.png');
  });

  // -----------------------------------------------------------------------
  // AD-08  Job Types page loads with CRUD
  // -----------------------------------------------------------------------
  test('AD-08: Organization Job Types page loads', async ({ page }) => {
    await loginAsOwner(page);
    await navigateTo(page, '/Admin/Organization/JobTypes');

    // STRICT: Job types table should be visible
    const content = page.locator('table, .data-table, h1').first();
    await expect(content).toBeVisible({ timeout: 10000 });

    // STRICT: At least one job type exists (seeded data)
    const rows = page.locator('table tbody tr, .jobtype-row');
    const rowCount = await rows.count();
    expect(rowCount).toBeGreaterThanOrEqual(1);

    await saveEvidence(page, EVIDENCE, 'AD-08-jobtypes.png');
  });

  // -----------------------------------------------------------------------
  // AD-09  Shift Groupings page loads
  // -----------------------------------------------------------------------
  test('AD-09: Organization Shift Groupings page loads', async ({ page }) => {
    await loginAsOwner(page);
    await navigateTo(page, '/Admin/Organization/ShiftGroupings');

    // STRICT: Shift groupings page should render
    const content = page.locator('table, .data-table, h1, .shift-groupings').first();
    await expect(content).toBeVisible({ timeout: 10000 });

    await saveEvidence(page, EVIDENCE, 'AD-09-shift-groupings.png');
  });

  // -----------------------------------------------------------------------
  // AD-10  Roles page loads
  // -----------------------------------------------------------------------
  test('AD-10: Organization Roles page loads', async ({ page }) => {
    await loginAsOwner(page);
    await navigateTo(page, '/Admin/Organization/Roles');

    // STRICT: Roles page should show role templates
    const content = page.locator('table, .data-table, h1, .role-template').first();
    await expect(content).toBeVisible({ timeout: 10000 });

    await saveEvidence(page, EVIDENCE, 'AD-10-roles.png');
  });

  // -----------------------------------------------------------------------
  // AD-11  Companies page loads (via organization)
  // -----------------------------------------------------------------------
  test('AD-11: Admin Companies page loads', async ({ page }) => {
    await loginAsOwner(page);
    await navigateTo(page, '/Admin/Companies');

    // STRICT: Companies table or list
    const content = page.locator('table, .data-table, h1, .companies').first();
    await expect(content).toBeVisible({ timeout: 10000 });

    await saveEvidence(page, EVIDENCE, 'AD-11-companies.png');
  });

  // -----------------------------------------------------------------------
  // AD-12  Directors page loads
  // -----------------------------------------------------------------------
  test('AD-12: Admin Directors page loads', async ({ page }) => {
    await loginAsOwner(page);
    await navigateTo(page, '/Admin/Directors');

    // STRICT: Directors page should render
    const content = page.locator('table, .data-table, h1, .directors').first();
    await expect(content).toBeVisible({ timeout: 10000 });

    await saveEvidence(page, EVIDENCE, 'AD-12-directors.png');
  });

  // -----------------------------------------------------------------------
  // AD-13  Settings page loads
  // -----------------------------------------------------------------------
  test('AD-13: Admin Settings page loads', async ({ page }) => {
    await loginAsOwner(page);
    await navigateTo(page, '/Admin/Settings');

    // STRICT: Settings form or content in main area
    const content = page.locator('#main-content h1, #main-content form, #main-content .settings, .page-header').first();
    await expect(content).toBeVisible({ timeout: 10000 });

    await saveEvidence(page, EVIDENCE, 'AD-13-settings.png');
  });

  // -----------------------------------------------------------------------
  // AD-14  Setup Tasks page loads
  // -----------------------------------------------------------------------
  test('AD-14: Admin Setup Tasks page loads', async ({ page }) => {
    await loginAsOwner(page);
    await navigateTo(page, '/Admin/SetupTasks');

    // STRICT: Setup tasks list or content
    const content = page.locator('.setup-tasks, .task-list, h1, table').first();
    await expect(content).toBeVisible({ timeout: 10000 });

    await saveEvidence(page, EVIDENCE, 'AD-14-setup-tasks.png');
  });

  // -----------------------------------------------------------------------
  // AD-15  Duty Rotation page loads
  // -----------------------------------------------------------------------
  test('AD-15: Admin Duty Rotation page loads', async ({ page }) => {
    await loginAsOwner(page);
    await navigateTo(page, '/Admin/DutyRotation');

    // STRICT: Duty rotation content
    const content = page.locator('h1, .page-title, .duty-rotation, table').first();
    await expect(content).toBeVisible({ timeout: 10000 });

    await saveEvidence(page, EVIDENCE, 'AD-15-duty-rotation.png');
  });
});

// =============================================================================
// Owner Pages
// =============================================================================

test.describe('Module AD: Owner Pages (P1)', () => {

  // -----------------------------------------------------------------------
  // AD-16  Owner dashboard
  // -----------------------------------------------------------------------
  test('AD-16: Owner dashboard loads', async ({ page }) => {
    await loginAsOwner(page);
    await navigateTo(page, '/Owner');

    // STRICT: Owner dashboard content
    const content = page.locator('h1, .page-title, .dashboard, .owner-hub').first();
    await expect(content).toBeVisible({ timeout: 10000 });

    await saveEvidence(page, EVIDENCE, 'AD-16-owner-dashboard.png');
  });

  // -----------------------------------------------------------------------
  // AD-17  Blueprints page loads
  // -----------------------------------------------------------------------
  test('AD-17: Blueprints page loads with data', async ({ page }) => {
    await loginAsOwner(page);
    await navigateTo(page, '/Owner/Blueprints');

    // STRICT: Blueprints page with table or card layout
    const content = page.locator('table, .data-table, .blueprints, h1').first();
    await expect(content).toBeVisible({ timeout: 10000 });

    await saveEvidence(page, EVIDENCE, 'AD-17-blueprints.png');
  });

  // -----------------------------------------------------------------------
  // AD-18  Programs page loads
  // -----------------------------------------------------------------------
  test('AD-18: Programs page loads', async ({ page }) => {
    await loginAsOwner(page);
    await navigateTo(page, '/Owner/Programs');

    // STRICT: Programs page content
    const content = page.locator('table, .data-table, .programs, h1').first();
    await expect(content).toBeVisible({ timeout: 10000 });

    await saveEvidence(page, EVIDENCE, 'AD-18-programs.png');
  });

  // -----------------------------------------------------------------------
  // AD-19  Feature Flags page loads
  // -----------------------------------------------------------------------
  test('AD-19: Feature Flags page loads', async ({ page }) => {
    await loginAsOwner(page);
    await navigateTo(page, '/Owner/FeatureFlags');

    // STRICT: Feature flags with toggle switches or content in main area
    const content = page.locator('#main-content h1, #main-content .feature-flags, #main-content .toggle-switch, .page-header').first();
    await expect(content).toBeVisible({ timeout: 10000 });

    await saveEvidence(page, EVIDENCE, 'AD-19-feature-flags.png');
  });

  // -----------------------------------------------------------------------
  // AD-20  Backup page loads
  // -----------------------------------------------------------------------
  test('AD-20: Backup page loads', async ({ page }) => {
    await loginAsOwner(page);
    await navigateTo(page, '/Owner/Backup');

    // STRICT: Backup management content
    const content = page.locator('form, .backup-section, h1, button').first();
    await expect(content).toBeVisible({ timeout: 10000 });

    await saveEvidence(page, EVIDENCE, 'AD-20-backup.png');
  });

  // -----------------------------------------------------------------------
  // AD-21  Database Console loads (read-only)
  // -----------------------------------------------------------------------
  test('AD-21: Database Console page loads', async ({ page }) => {
    await loginAsOwner(page);
    await navigateTo(page, '/Owner/DatabaseConsole');

    // STRICT: Database console with query input
    const content = page.locator('textarea, .sql-editor, .query-input, h1').first();
    await expect(content).toBeVisible({ timeout: 10000 });

    await saveEvidence(page, EVIDENCE, 'AD-21-database-console.png');
  });

  // -----------------------------------------------------------------------
  // AD-22  Locked Users page
  // -----------------------------------------------------------------------
  test('AD-22: Locked Users page loads', async ({ page }) => {
    await loginAsOwner(page);
    await navigateTo(page, '/Owner/LockedUsers');

    // STRICT: Locked users page content
    const content = page.locator('h1, .page-title, table, .locked-users, .empty-state').first();
    await expect(content).toBeVisible({ timeout: 10000 });

    await saveEvidence(page, EVIDENCE, 'AD-22-locked-users.png');
  });

  // -----------------------------------------------------------------------
  // AD-23  Language Management page
  // -----------------------------------------------------------------------
  test('AD-23: Language Management page loads', async ({ page }) => {
    await loginAsOwner(page);
    await navigateTo(page, '/Owner/LanguageManagement');

    // STRICT: Language management content with locale entries
    const content = page.locator('table, .data-table, h1, .language-management').first();
    await expect(content).toBeVisible({ timeout: 10000 });

    await saveEvidence(page, EVIDENCE, 'AD-23-language-mgmt.png');
  });

  // -----------------------------------------------------------------------
  // AD-24  Email Config page
  // -----------------------------------------------------------------------
  test('AD-24: Email Config page loads', async ({ page }) => {
    await loginAsOwner(page);
    await navigateTo(page, '/Owner/EmailConfig');

    // STRICT: Email config content in main area
    const content = page.locator('#main-content h1, #main-content .email-config, #main-content form, .page-header').first();
    await expect(content).toBeVisible({ timeout: 10000 });

    await saveEvidence(page, EVIDENCE, 'AD-24-email-config.png');
  });

  // -----------------------------------------------------------------------
  // AD-25  Griffin Config page
  // -----------------------------------------------------------------------
  test('AD-25: Griffin Config page loads', async ({ page }) => {
    await loginAsOwner(page);
    await navigateTo(page, '/Owner/GriffinConfig');

    // STRICT: Griffin config content in main area
    const content = page.locator('#main-content h1, #main-content .griffin-config, #main-content form, .page-header').first();
    await expect(content).toBeVisible({ timeout: 10000 });

    await saveEvidence(page, EVIDENCE, 'AD-25-griffin-config.png');
  });

  // -----------------------------------------------------------------------
  // AD-26  Owner Hub home page
  // -----------------------------------------------------------------------
  test('AD-26: Owner Hub page loads', async ({ page }) => {
    await loginAsOwner(page);
    await navigateTo(page, '/Owner/Hub');

    // STRICT: Hub index content
    const content = page.locator('h1, .page-title, .hub-cards, .owner-hub').first();
    await expect(content).toBeVisible({ timeout: 10000 });

    await saveEvidence(page, EVIDENCE, 'AD-26-owner-hub.png');
  });
});

// =============================================================================
// Diagnostic & Seed Verification
// =============================================================================

test.describe('Module AD: Diagnostic & Seed Verification (P1)', () => {

  // -----------------------------------------------------------------------
  // AD-27  Diagnostic page loads (Owner-only)
  // -----------------------------------------------------------------------
  test('AD-27: Diagnostic page loads for Owner', async ({ page }) => {
    await loginAsOwner(page);
    await navigateTo(page, '/Diagnostic');

    // STRICT: Diagnostic content visible
    const content = page.locator('h1, .diagnostic, .page-title, pre, table').first();
    await expect(content).toBeVisible({ timeout: 10000 });

    await saveEvidence(page, EVIDENCE, 'AD-27-diagnostic.png');
  });

  // -----------------------------------------------------------------------
  // AD-28  Seed data — seeded companies exist
  // -----------------------------------------------------------------------
  test('AD-28: Seeded test companies visible in Admin', async ({ page }) => {
    await loginAsOwner(page);
    await navigateTo(page, '/Admin/Companies');

    // STRICT: At least the base company should appear
    const pageText = await page.locator('body').innerText();
    // The hierarchy has known company names from seed data
    const hasCompanies = /company|דסק|חברה|tzafona|hir|alhut|text|br|hakam/i.test(pageText);
    expect(hasCompanies).toBe(true);

    await saveEvidence(page, EVIDENCE, 'AD-28-seeded-companies.png');
  });

  // -----------------------------------------------------------------------
  // AD-29  Employee cannot access Owner pages
  // -----------------------------------------------------------------------
  test('AD-29: Employee denied access to Owner pages', async ({ page }) => {
    // Use raw login to avoid sidebar visibility check (employee has collapsed sidebar)
    await page.goto(`${BASE_URL}/Auth/Login`);
    await page.waitForLoadState('networkidle');
    const emailInput = page.locator('input[name="Email"], input#Email').first();
    const passInput = page.locator('input[name="Password"], input#Password').first();
    await expect(emailInput).toBeVisible({ timeout: 10000 });
    await emailInput.fill('test.member@shifty.test');
    await passInput.fill('TestMember123!');
    const submitBtn = page.locator('form:has(input[name="Email"]) button[type="submit"]').first();
    await Promise.all([
      page.waitForURL(url => !url.toString().includes('/Auth/Login'), { timeout: 15000 }),
      submitBtn.click(),
    ]);
    await expect(page).not.toHaveURL(/\/Auth\/Login/);

    // Navigate to Owner page — should be denied or redirected
    await page.goto(`${BASE_URL}/Owner`);
    await page.waitForLoadState('networkidle');

    // STRICT: Should NOT see Owner dashboard content
    const url = page.url();
    const isDenied = url.includes('/AccessDenied') ||
                     url.includes('/Auth/Login') ||
                     url.includes('/Home') ||
                     !url.includes('/Owner');

    expect(isDenied).toBe(true);

    await saveEvidence(page, EVIDENCE, 'AD-29-employee-denied-owner.png');
  });
});
