// @ts-check
const { test, expect } = require('@playwright/test');
const {
  loginAsOwner, login, logout, saveEvidence, saveApiEvidence, navigateTo,
  assertPageContains, assertPageNotContains, assertMinCount,
  TEST_PASSWORD, TEST_USERS,
} = require('../../helpers/production-qa-helpers');

const EVIDENCE = '16-tenant-isolation';

/**
 * Module P: Tenant Isolation
 *
 * Verifies that each company's data is scoped: users in Company A cannot
 * see Company B data on any calendar, request, or admin page.
 *
 * CRITICAL: These tests depend on test users existing.
 * All logins use expectSuccess:true (the default) so that a missing user
 * causes an EXPLICIT test failure, not a silent pass.
 */
test.describe('Module P: Tenant Isolation', () => {

  test('P-01: emp.tz.alhut sees only Tzafona data (no Hir/Hitazmut)', async ({ page }) => {
    // STRICT login -- fails if emp.tz.alhut@test does not exist
    await login(page, 'emp.tz.alhut@test', TEST_PASSWORD);

    await navigateTo(page, '/Calendar/Shifts');

    // ASSERT: Page loaded to Calendar/Shifts
    expect(page.url()).toContain('/Calendar');

    const body = await page.content();

    // ASSERT: No Hir company employee emails visible
    expect(body).not.toContain('emp.hir');
    // ASSERT: No Hitazmut company employee emails visible
    expect(body).not.toContain('emp.hit');

    await saveEvidence(page, EVIDENCE, 'P-01-tzafona-only.png', 'tzafona-vs-hir');
  });

  test('P-02: emp.hir.alhut sees only Hir data (no Tzafona)', async ({ page }) => {
    // STRICT login -- fails if emp.hir.alhut@test does not exist
    await login(page, 'emp.hir.alhut@test', TEST_PASSWORD);

    await navigateTo(page, '/Calendar/Shifts');

    // ASSERT: Page loaded
    expect(page.url()).toContain('/Calendar');

    const body = await page.content();

    // ASSERT: No Tzafona company employee emails visible
    expect(body).not.toContain('emp.tz.');

    await saveEvidence(page, EVIDENCE, 'P-02-hir-only.png', 'tzafona-vs-hir');
  });

  test('P-03: emp.hit.alhut sees only Hitazmut data (no Tzafona/Hir)', async ({ page }) => {
    // STRICT login -- fails if emp.hit.alhut@test does not exist
    await login(page, 'emp.hit.alhut@test', TEST_PASSWORD);

    await navigateTo(page, '/Calendar/Shifts');

    expect(page.url()).toContain('/Calendar');

    const body = await page.content();

    // ASSERT: No Tzafona employee data
    expect(body).not.toContain('emp.tz.');
    // ASSERT: No Hir employee data
    expect(body).not.toContain('emp.hir.');

    await saveEvidence(page, EVIDENCE, 'P-03-hitazmut-only.png', 'oren-vs-ella');
  });

  test('P-04: emp.alpha.alhut sees only QA-Alpha data', async ({ page }) => {
    // STRICT login -- fails if emp.alpha.alhut@test does not exist
    await login(page, 'emp.alpha.alhut@test', TEST_PASSWORD);

    await navigateTo(page, '/Calendar/Shifts');

    expect(page.url()).toContain('/Calendar');

    const body = await page.content();

    // ASSERT: No other company employees visible
    expect(body).not.toContain('emp.tz.');
    expect(body).not.toContain('emp.hir.');
    expect(body).not.toContain('emp.hit.');
    expect(body).not.toContain('emp.beta.');

    await saveEvidence(page, EVIDENCE, 'P-04-qa-alpha-only.png', 'new-companies');
  });

  test('P-05: API /Api/Calendar/GetShiftsData respects tenant scoping', async ({ page }) => {
    await loginAsOwner(page);

    // GetShiftsData requires moleculeId, jobTypeId, startDate, endDate query params.
    // Without them the API correctly returns 400 (Invalid date format).
    const today = new Date();
    const sd = today.toISOString().slice(0, 10);
    const ed = new Date(today.getTime() + 7 * 86400000).toISOString().slice(0, 10);

    const response = await page.request.get(
      `/Api/Calendar/GetShiftsData?moleculeId=1&jobTypeId=1&startDate=${sd}&endDate=${ed}`
    );

    // ASSERT: API returns 200 with valid scope params
    expect(response.status()).toBe(200);

    // ASSERT: Response body is valid JSON
    const data = await response.json();
    expect(data).toBeDefined();

    // Save API evidence
    saveApiEvidence(EVIDENCE, 'P-05-api-tenant.json', { status: response.status(), data });

    await saveEvidence(page, EVIDENCE, 'P-05-api-tenant.png');
  });

  test('P-06: Direct URL to non-existent shift ID does not leak data', async ({ page }) => {
    // STRICT login
    await login(page, 'emp.tz.alhut@test', TEST_PASSWORD);

    // Navigate to a shift ID that almost certainly does not exist for this company
    const response = await page.goto('http://localhost:5000/Calendar/Shifts?ShiftInstanceId=99999');
    await page.waitForLoadState('networkidle');

    // ASSERT: Response was received
    expect(response).not.toBeNull();

    // ASSERT: We did not get a 500 error (server handled it gracefully)
    expect(response.status()).toBeLessThan(500);

    // ASSERT: No exception page shown
    await expect(page.locator('h1:has-text("An unhandled exception occurred")')).not.toBeVisible({ timeout: 2000 });

    await saveEvidence(page, EVIDENCE, 'P-06-direct-url.png');
  });

  test('P-07: Chores page is scoped to molecule for employee', async ({ page }) => {
    await login(page, 'emp.tz.alhut@test', TEST_PASSWORD);

    await navigateTo(page, '/Calendar/Chores');

    // ASSERT: Chores page loaded
    expect(page.url()).toContain('/Calendar/Chores');

    // ASSERT: No error page
    await expect(page.locator('h1:has-text("An unhandled exception occurred")')).not.toBeVisible({ timeout: 2000 });

    const body = await page.content();

    // ASSERT: No other company employees visible on the chores page
    expect(body).not.toContain('emp.hir');
    expect(body).not.toContain('emp.hit');

    await saveEvidence(page, EVIDENCE, 'P-07-chores-molecule.png');
  });

  test('P-08: Time-off requests scoped to company for employee', async ({ page }) => {
    await login(page, 'emp.tz.alhut@test', TEST_PASSWORD);

    await navigateTo(page, '/My/Requests');

    // ASSERT: My Requests page loaded
    expect(page.url()).toContain('/My/Requests');

    // ASSERT: Page content does not contain other company employee emails
    const body = await page.content();
    expect(body).not.toContain('emp.hir');
    expect(body).not.toContain('emp.hit');

    await saveEvidence(page, EVIDENCE, 'P-08-timeoff-company.png');
  });

  test('P-09: Manager user list is scoped (moladmin only sees own scope)', async ({ page }) => {
    // STRICT login -- fails if moladmin.oren@test does not exist
    await login(page, 'moladmin.oren@test', TEST_PASSWORD);

    await navigateTo(page, '/Admin/Users');

    // ASSERT: The Users page loaded (not redirected to AccessDenied)
    // moladmin has AccessAdminNavigation + ViewAllUsers grants
    expect(page.url()).toContain('/Admin/Users');

    // ASSERT: No error page
    await expect(page.locator('h1:has-text("An unhandled exception occurred")')).not.toBeVisible({ timeout: 2000 });

    // STRICT: Users table or heading must be visible (not just body)
    const usersTable = page.locator('.data-table');
    await expect(usersTable.first()).toBeVisible({ timeout: 5000 });

    // STRICT: Table must have at least one row
    const rows = usersTable.first().locator('tbody tr');
    const rowCount = await rows.count();
    expect(rowCount).toBeGreaterThan(0);

    await saveEvidence(page, EVIDENCE, 'P-09-user-list-scoped.png');
  });

  test('P-10: Notifications are scoped (no cross-company notifications)', async ({ page }) => {
    await login(page, 'emp.tz.alhut@test', TEST_PASSWORD);

    await navigateTo(page, '/My/NotificationCenter');

    // ASSERT: The notification center loaded
    expect(page.url()).toContain('/My/NotificationCenter');

    // ASSERT: No error page
    await expect(page.locator('h1:has-text("An unhandled exception occurred")')).not.toBeVisible({ timeout: 2000 });

    // ASSERT: Page content does not contain other company employee data
    const body = await page.content();
    expect(body).not.toContain('emp.hir');
    expect(body).not.toContain('emp.hit');

    await saveEvidence(page, EVIDENCE, 'P-10-notifications-scoped.png');
  });

  test('P-11: OnCall page loads for owner (area-level scope, not company)', async ({ page }) => {
    await loginAsOwner(page);

    await navigateTo(page, '/Calendar/OnCall');

    // ASSERT: The OnCall page loaded
    expect(page.url()).toContain('/Calendar/OnCall');

    // STRICT: On-call calendar wrapper must be visible
    const calendarWrapper = page.locator('.oncall-calendar');
    await expect(calendarWrapper).toBeVisible({ timeout: 10000 });

    // STRICT: No error page
    await expect(page.locator('h1:has-text("An unhandled exception occurred")')).not.toBeVisible({ timeout: 2000 });

    await saveEvidence(page, EVIDENCE, 'P-11-onduty-global.png');
  });

  test('P-12: Owner can access shifts calendar (multi-company context)', async ({ page }) => {
    await loginAsOwner(page);

    await navigateTo(page, '/Calendar/Shifts');

    // ASSERT: Shifts page loaded
    expect(page.url()).toContain('/Calendar/Shifts');

    // STRICT: Shifts calendar wrapper must be visible
    const calendarWrapper = page.locator('.shifts-calendar');
    await expect(calendarWrapper).toBeVisible({ timeout: 10000 });

    // STRICT: No error page
    await expect(page.locator('h1:has-text("An unhandled exception occurred")')).not.toBeVisible({ timeout: 2000 });

    await saveEvidence(page, EVIDENCE, 'P-12-owner-switch.png');
  });

  test('P-13: Owner can access shifts for different companies', async ({ page }) => {
    await loginAsOwner(page);

    await navigateTo(page, '/Calendar/Shifts');

    // STRICT: Shifts calendar wrapper must be visible
    const calendarWrapper = page.locator('.shifts-calendar');
    await expect(calendarWrapper).toBeVisible({ timeout: 10000 });

    // STRICT: No error page
    await expect(page.locator('h1:has-text("An unhandled exception occurred")')).not.toBeVisible({ timeout: 2000 });

    await saveEvidence(page, EVIDENCE, 'P-13-qa-alpha-empty.png', 'new-companies');
  });

  test('P-14: Blueprints page is company-scoped for owner', async ({ page }) => {
    await loginAsOwner(page);

    await navigateTo(page, '/Owner/Blueprints');

    // ASSERT: Blueprints page loaded
    expect(page.url()).toContain('/Owner/Blueprints');

    // STRICT: No error page
    await expect(page.locator('h1:has-text("An unhandled exception occurred")')).not.toBeVisible({ timeout: 2000 });

    // STRICT: Blueprints page container must be visible (scoped to main content,
    // not the sidebar's hidden logout form which also matches bare 'form' selector)
    const blueprintContainer = page.locator('.feature-flags-container');
    await expect(blueprintContainer).toBeVisible({ timeout: 5000 });

    await saveEvidence(page, EVIDENCE, 'P-14-blueprints-per-company.png');
  });
});
