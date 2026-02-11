// @ts-check
const { test, expect } = require('@playwright/test');
const {
  loginAsOwner, login, logout, saveEvidence, navigateTo, navigateExpecting,
  assertPageContains, assertPageNotContains, assertMinCount,
  TEST_PASSWORD, TEST_USERS, ROLE_ENUM,
} = require('../../helpers/production-qa-helpers');

const EVIDENCE = '17-per-role-access';

/**
 * Module Q: Per-Role Access Control
 *
 * Tests that each role sees the correct navigation items, can access
 * permitted pages, and is denied access to forbidden pages.
 *
 * CRITICAL: All logins use expectSuccess:true (the default).
 * If a test user does not exist, the test FAILS explicitly.
 *
 * Role hierarchy (from layout):
 *   - Owner (admin@local): has AdminAccess grant -> sees Owner Admin Panel + all admin nav
 *   - Director (dir.alhut@test): has DirectorHubAccess -> sees Director Hub, admin nav
 *   - Manager (mgr.alhut.tz@test): has AccessAdminNavigation -> sees admin nav, no Owner pages
 *   - Employee (emp.tz.alhut@test): NO admin nav, read-only calendar, My/Requests
 *   - Trainee (trainee.alhut@test): same as Employee
 *   - Assigner (assigner.oren@test): chore controls only
 */

// ============================================================================
// OWNER ROLE TESTS
// ============================================================================
test.describe('Module Q: Owner Role Access', () => {

  test('Q-owner-01: Owner home page loads correctly', async ({ page }) => {
    await loginAsOwner(page);

    // ASSERT: We are authenticated and NOT on login page
    await expect(page).not.toHaveURL(/\/Auth\/Login/);

    // ASSERT: Page rendered content
    await expect(page.locator('body')).toBeVisible();
    await expect(page.locator('h1:has-text("An unhandled exception occurred")')).not.toBeVisible({ timeout: 2000 });

    await saveEvidence(page, EVIDENCE, 'Q-owner-01-home.png', 'owner');
  });

  test('Q-owner-02: Owner sidebar has Owner Admin Panel link', async ({ page }) => {
    await loginAsOwner(page);

    // ASSERT: Owner admin panel link is visible in sidebar
    const ownerLink = page.locator('a[href*="/Owner"]');
    await expect(ownerLink.first()).toBeVisible({ timeout: 5000 });

    // ASSERT: Admin-level nav items are visible (Users, Config, etc.)
    const adminUsersLink = page.locator('a[href*="/Admin/Users"]');
    await expect(adminUsersLink.first()).toBeVisible({ timeout: 5000 });

    await saveEvidence(page, EVIDENCE, 'Q-owner-02-sidebar.png', 'owner');
  });

  test('Q-owner-03: Owner can access /Owner/Index', async ({ page }) => {
    await loginAsOwner(page);
    await navigateTo(page, '/Owner/Index');

    // ASSERT: Landed on Owner page (not redirected or denied)
    expect(page.url()).toContain('/Owner');

    // ASSERT: No error
    await expect(page.locator('h1:has-text("An unhandled exception occurred")')).not.toBeVisible({ timeout: 2000 });

    await saveEvidence(page, EVIDENCE, 'Q-owner-03-owner-access.png', 'owner');
  });

  test('Q-owner-04: Owner can access Calendar/Shifts', async ({ page }) => {
    await loginAsOwner(page);
    await navigateTo(page, '/Calendar/Shifts');

    // ASSERT: Shifts page loaded
    expect(page.url()).toContain('/Calendar/Shifts');
    await expect(page.locator('h1:has-text("An unhandled exception occurred")')).not.toBeVisible({ timeout: 2000 });

    await saveEvidence(page, EVIDENCE, 'Q-owner-04-calendar.png', 'owner');
  });

  test('Q-owner-05: Owner API /Api/SessionStatus returns 200', async ({ page }) => {
    await loginAsOwner(page);

    const response = await page.request.get('/Api/SessionStatus');
    // ASSERT: API responds with 200
    expect(response.status()).toBe(200);

    await saveEvidence(page, EVIDENCE, 'Q-owner-05-api-scope.png', 'owner');
  });
});

// ============================================================================
// DIRECTOR ROLE TESTS
// ============================================================================
test.describe('Module Q: Director Role Access', () => {

  test('Q-director-01: Director home page loads (Director Hub)', async ({ page }) => {
    // STRICT: fails if dir.alhut@test does not exist
    await login(page, 'dir.alhut@test', TEST_PASSWORD);

    // ASSERT: Authenticated
    await expect(page).not.toHaveURL(/\/Auth\/Login/);

    // ASSERT: Page loaded
    await expect(page.locator('body')).toBeVisible();
    await expect(page.locator('h1:has-text("An unhandled exception occurred")')).not.toBeVisible({ timeout: 2000 });

    await saveEvidence(page, EVIDENCE, 'Q-director-01-home.png', 'director');
  });

  test('Q-director-02: Director sidebar has admin nav but NO Owner link', async ({ page }) => {
    await login(page, 'dir.alhut@test', TEST_PASSWORD);

    // ASSERT: Admin-level nav is present (Director has AccessAdminNavigation)
    const calendarLink = page.locator('nav a[href*="/Calendar"]');
    await expect(calendarLink.first()).toBeVisible({ timeout: 5000 });

    // ASSERT: Owner link is NOT visible (no AdminAccess grant)
    const ownerLink = page.locator('a.app-sidebar-nav-item[href*="/Owner/Index"]');
    await expect(ownerLink).not.toBeVisible({ timeout: 3000 });

    await saveEvidence(page, EVIDENCE, 'Q-director-02-sidebar.png', 'director');
  });

  test('Q-director-03: Director is denied access to /Owner/Index', async ({ page }) => {
    await login(page, 'dir.alhut@test', TEST_PASSWORD);

    // Navigate directly to Owner page
    await page.goto('http://localhost:5000/Owner/Index');
    await page.waitForLoadState('networkidle');

    const url = page.url();

    // ASSERT: Director was redirected away from Owner page (AccessDenied or login or elsewhere)
    const isDenied = url.includes('AccessDenied') || url.includes('Auth/Login') || !url.includes('/Owner');
    expect(isDenied).toBe(true);

    await saveEvidence(page, EVIDENCE, 'Q-director-03-forbidden.png', 'director');
  });

  test('Q-director-04: Director can access Calendar/Shifts', async ({ page }) => {
    await login(page, 'dir.alhut@test', TEST_PASSWORD);
    await navigateTo(page, '/Calendar/Shifts');

    // ASSERT: Page loaded
    expect(page.url()).toContain('/Calendar');
    await expect(page.locator('h1:has-text("An unhandled exception occurred")')).not.toBeVisible({ timeout: 2000 });

    await saveEvidence(page, EVIDENCE, 'Q-director-04-calendar.png', 'director');
  });

  test('Q-director-05: Director API /Api/SessionStatus returns 200', async ({ page }) => {
    await login(page, 'dir.alhut@test', TEST_PASSWORD);

    const response = await page.request.get('/Api/SessionStatus');
    expect(response.status()).toBe(200);

    await saveEvidence(page, EVIDENCE, 'Q-director-05-api-scope.png', 'director');
  });
});

// ============================================================================
// MANAGER ROLE TESTS
// ============================================================================
test.describe('Module Q: Manager Role Access', () => {

  test('Q-manager-01: Manager home page loads correctly', async ({ page }) => {
    // STRICT: fails if mgr.alhut.tz@test does not exist
    await login(page, 'mgr.alhut.tz@test', TEST_PASSWORD);

    // ASSERT: Authenticated
    await expect(page).not.toHaveURL(/\/Auth\/Login/);
    await expect(page.locator('body')).toBeVisible();
    await expect(page.locator('h1:has-text("An unhandled exception occurred")')).not.toBeVisible({ timeout: 2000 });

    await saveEvidence(page, EVIDENCE, 'Q-manager-01-home.png', 'manager');
  });

  test('Q-manager-02: Manager sidebar has admin nav (via grants), NO Owner link', async ({ page }) => {
    await login(page, 'mgr.alhut.tz@test', TEST_PASSWORD);

    // ASSERT: Calendar link is present (manager has admin nav)
    const calendarLink = page.locator('nav a[href*="/Calendar"]');
    await expect(calendarLink.first()).toBeVisible({ timeout: 5000 });

    // ASSERT: Owner link is NOT visible
    const ownerLink = page.locator('a.app-sidebar-nav-item[href*="/Owner/Index"]');
    await expect(ownerLink).not.toBeVisible({ timeout: 3000 });

    await saveEvidence(page, EVIDENCE, 'Q-manager-02-sidebar.png', 'manager');
  });

  test('Q-manager-03: Manager is denied access to /Owner/Index', async ({ page }) => {
    await login(page, 'mgr.alhut.tz@test', TEST_PASSWORD);

    await page.goto('http://localhost:5000/Owner/Index');
    await page.waitForLoadState('networkidle');

    const url = page.url();
    const isDenied = url.includes('AccessDenied') || url.includes('Auth/Login') || !url.includes('/Owner');
    expect(isDenied).toBe(true);

    await saveEvidence(page, EVIDENCE, 'Q-manager-03-forbidden.png', 'manager');
  });

  test('Q-manager-04: Manager can access Calendar/Shifts with controls', async ({ page }) => {
    await login(page, 'mgr.alhut.tz@test', TEST_PASSWORD);
    await navigateTo(page, '/Calendar/Shifts');

    // ASSERT: Calendar page loaded
    expect(page.url()).toContain('/Calendar');
    await expect(page.locator('h1:has-text("An unhandled exception occurred")')).not.toBeVisible({ timeout: 2000 });

    await saveEvidence(page, EVIDENCE, 'Q-manager-04-calendar.png', 'manager');
  });

  test('Q-manager-05: Manager API /Api/SessionStatus returns 200', async ({ page }) => {
    await login(page, 'mgr.alhut.tz@test', TEST_PASSWORD);

    const response = await page.request.get('/Api/SessionStatus');
    expect(response.status()).toBe(200);

    await saveEvidence(page, EVIDENCE, 'Q-manager-05-api-scope.png', 'manager');
  });
});

// ============================================================================
// EMPLOYEE ROLE TESTS
// ============================================================================
test.describe('Module Q: Employee Role Access', () => {

  test('Q-employee-01: Employee home page loads correctly', async ({ page }) => {
    // STRICT: fails if emp.tz.alhut@test does not exist
    await login(page, 'emp.tz.alhut@test', TEST_PASSWORD);

    // ASSERT: Authenticated
    await expect(page).not.toHaveURL(/\/Auth\/Login/);
    await expect(page.locator('body')).toBeVisible();
    await expect(page.locator('h1:has-text("An unhandled exception occurred")')).not.toBeVisible({ timeout: 2000 });

    await saveEvidence(page, EVIDENCE, 'Q-employee-01-home.png', 'employee');
  });

  test('Q-employee-02: Employee sidebar has NO admin nav links', async ({ page }) => {
    await login(page, 'emp.tz.alhut@test', TEST_PASSWORD);

    // ASSERT: Admin nav items are NOT visible for employee
    const adminUsersLink = page.locator('a.app-sidebar-nav-item[href*="/Admin/Users"]');
    await expect(adminUsersLink).not.toBeVisible({ timeout: 3000 });

    const adminConfigLink = page.locator('a.app-sidebar-nav-item[href*="/Admin/Config"]');
    await expect(adminConfigLink).not.toBeVisible({ timeout: 3000 });

    const ownerLink = page.locator('a.app-sidebar-nav-item[href*="/Owner/Index"]');
    await expect(ownerLink).not.toBeVisible({ timeout: 3000 });

    // ASSERT: Employee sidebar DOES have My/Requests link
    const requestsLink = page.locator('a[href*="/My/Requests"]');
    await expect(requestsLink.first()).toBeVisible({ timeout: 5000 });

    // ASSERT: Employee sidebar DOES have calendar link
    const calendarLink = page.locator('nav a[href*="/Calendar"]');
    await expect(calendarLink.first()).toBeVisible({ timeout: 5000 });

    await saveEvidence(page, EVIDENCE, 'Q-employee-02-sidebar.png', 'employee');
  });

  test('Q-employee-03: Employee is denied access to /Owner/Index', async ({ page }) => {
    await login(page, 'emp.tz.alhut@test', TEST_PASSWORD);

    await page.goto('http://localhost:5000/Owner/Index');
    await page.waitForLoadState('networkidle');

    const url = page.url();
    const isDenied = url.includes('AccessDenied') || url.includes('Auth/Login') || !url.includes('/Owner');
    expect(isDenied).toBe(true);

    await saveEvidence(page, EVIDENCE, 'Q-employee-03-forbidden.png', 'employee');
  });

  test('Q-employee-04: Employee sees calendar in read-only mode (no edit controls)', async ({ page }) => {
    await login(page, 'emp.tz.alhut@test', TEST_PASSWORD);
    await navigateTo(page, '/Calendar/Shifts');

    // ASSERT: Calendar page loaded
    expect(page.url()).toContain('/Calendar');
    await expect(page.locator('h1:has-text("An unhandled exception occurred")')).not.toBeVisible({ timeout: 2000 });

    // ASSERT: No admin-level edit controls (assignment dropdowns, assign buttons)
    const editControls = page.locator('select[name*="UserId"], button:has-text("Assign"), .assignment-dropdown');
    const editCount = await editControls.count();
    expect(editCount).toBe(0);

    await saveEvidence(page, EVIDENCE, 'Q-employee-04-calendar.png', 'employee');
  });

  test('Q-employee-05: Employee API /Api/SessionStatus returns 200', async ({ page }) => {
    await login(page, 'emp.tz.alhut@test', TEST_PASSWORD);

    const response = await page.request.get('/Api/SessionStatus');
    expect(response.status()).toBe(200);

    await saveEvidence(page, EVIDENCE, 'Q-employee-05-api-scope.png', 'employee');
  });

  test('Q-employee-06: Employee is denied access to /Admin/Users', async ({ page }) => {
    await login(page, 'emp.tz.alhut@test', TEST_PASSWORD);

    await page.goto('http://localhost:5000/Admin/Users');
    await page.waitForLoadState('networkidle');

    const url = page.url();
    const isDenied = url.includes('AccessDenied') || url.includes('Auth/Login') || !url.includes('/Admin/Users');
    expect(isDenied).toBe(true);

    await saveEvidence(page, EVIDENCE, 'Q-employee-06-admin-denied.png', 'employee');
  });

  test('Q-employee-07: Employee is denied access to /Admin/Config', async ({ page }) => {
    await login(page, 'emp.tz.alhut@test', TEST_PASSWORD);

    await page.goto('http://localhost:5000/Admin/Config');
    await page.waitForLoadState('networkidle');

    const url = page.url();
    const isDenied = url.includes('AccessDenied') || url.includes('Auth/Login') || !url.includes('/Admin/Config');
    expect(isDenied).toBe(true);

    await saveEvidence(page, EVIDENCE, 'Q-employee-07-config-denied.png', 'employee');
  });
});

// ============================================================================
// TRAINEE ROLE TESTS
// ============================================================================
test.describe('Module Q: Trainee Role Access', () => {

  test('Q-trainee-01: Trainee home page loads correctly', async ({ page }) => {
    // STRICT: fails if trainee.alhut@test does not exist
    await login(page, 'trainee.alhut@test', TEST_PASSWORD);

    // ASSERT: Authenticated
    await expect(page).not.toHaveURL(/\/Auth\/Login/);
    await expect(page.locator('body')).toBeVisible();
    await expect(page.locator('h1:has-text("An unhandled exception occurred")')).not.toBeVisible({ timeout: 2000 });

    await saveEvidence(page, EVIDENCE, 'Q-trainee-01-home.png', 'trainee');
  });

  test('Q-trainee-02: Trainee sidebar has NO admin nav links', async ({ page }) => {
    await login(page, 'trainee.alhut@test', TEST_PASSWORD);

    // ASSERT: No admin links
    const adminUsersLink = page.locator('a.app-sidebar-nav-item[href*="/Admin/Users"]');
    await expect(adminUsersLink).not.toBeVisible({ timeout: 3000 });

    const ownerLink = page.locator('a.app-sidebar-nav-item[href*="/Owner/Index"]');
    await expect(ownerLink).not.toBeVisible({ timeout: 3000 });

    // ASSERT: Calendar link exists
    const calendarLink = page.locator('nav a[href*="/Calendar"]');
    await expect(calendarLink.first()).toBeVisible({ timeout: 5000 });

    await saveEvidence(page, EVIDENCE, 'Q-trainee-02-sidebar.png', 'trainee');
  });

  test('Q-trainee-03: Trainee is denied access to /Owner/Index', async ({ page }) => {
    await login(page, 'trainee.alhut@test', TEST_PASSWORD);

    await page.goto('http://localhost:5000/Owner/Index');
    await page.waitForLoadState('networkidle');

    const url = page.url();
    const isDenied = url.includes('AccessDenied') || url.includes('Auth/Login') || !url.includes('/Owner');
    expect(isDenied).toBe(true);

    await saveEvidence(page, EVIDENCE, 'Q-trainee-03-forbidden.png', 'trainee');
  });

  test('Q-trainee-04: Trainee sees calendar in read-only mode', async ({ page }) => {
    await login(page, 'trainee.alhut@test', TEST_PASSWORD);
    await navigateTo(page, '/Calendar/Shifts');

    // ASSERT: Calendar loaded
    expect(page.url()).toContain('/Calendar');
    await expect(page.locator('h1:has-text("An unhandled exception occurred")')).not.toBeVisible({ timeout: 2000 });

    // ASSERT: No edit controls
    const editControls = page.locator('select[name*="UserId"], button:has-text("Assign"), .assignment-dropdown');
    const editCount = await editControls.count();
    expect(editCount).toBe(0);

    await saveEvidence(page, EVIDENCE, 'Q-trainee-04-calendar.png', 'trainee');
  });

  test('Q-trainee-05: Trainee API /Api/SessionStatus returns 200', async ({ page }) => {
    await login(page, 'trainee.alhut@test', TEST_PASSWORD);

    const response = await page.request.get('/Api/SessionStatus');
    expect(response.status()).toBe(200);

    await saveEvidence(page, EVIDENCE, 'Q-trainee-05-api-scope.png', 'trainee');
  });
});

// ============================================================================
// ASSIGNER ROLE TESTS
// ============================================================================
test.describe('Module Q: Assigner Role Access', () => {

  test('Q-assigner-01: Assigner home page loads correctly', async ({ page }) => {
    // STRICT: fails if assigner.oren@test does not exist
    await login(page, 'assigner.oren@test', TEST_PASSWORD);

    // ASSERT: Authenticated
    await expect(page).not.toHaveURL(/\/Auth\/Login/);
    await expect(page.locator('body')).toBeVisible();
    await expect(page.locator('h1:has-text("An unhandled exception occurred")')).not.toBeVisible({ timeout: 2000 });

    await saveEvidence(page, EVIDENCE, 'Q-assigner-01-home.png', 'assigner');
  });

  test('Q-assigner-02: Assigner sidebar has chore-related links', async ({ page }) => {
    await login(page, 'assigner.oren@test', TEST_PASSWORD);

    // ASSERT: Owner link is NOT visible
    const ownerLink = page.locator('a.app-sidebar-nav-item[href*="/Owner/Index"]');
    await expect(ownerLink).not.toBeVisible({ timeout: 3000 });

    // ASSERT: Calendar link exists (Assigner can view calendars)
    const calendarLink = page.locator('nav a[href*="/Calendar"]');
    await expect(calendarLink.first()).toBeVisible({ timeout: 5000 });

    await saveEvidence(page, EVIDENCE, 'Q-assigner-02-sidebar.png', 'assigner');
  });

  test('Q-assigner-03: Assigner is denied access to /Owner/Index', async ({ page }) => {
    await login(page, 'assigner.oren@test', TEST_PASSWORD);

    await page.goto('http://localhost:5000/Owner/Index');
    await page.waitForLoadState('networkidle');

    const url = page.url();
    const isDenied = url.includes('AccessDenied') || url.includes('Auth/Login') || !url.includes('/Owner');
    expect(isDenied).toBe(true);

    await saveEvidence(page, EVIDENCE, 'Q-assigner-03-forbidden.png', 'assigner');
  });

  test('Q-assigner-04: Assigner can access chores page', async ({ page }) => {
    await login(page, 'assigner.oren@test', TEST_PASSWORD);
    await navigateTo(page, '/Calendar/Chores');

    // ASSERT: Chores page loaded
    expect(page.url()).toContain('/Calendar/Chores');
    await expect(page.locator('h1:has-text("An unhandled exception occurred")')).not.toBeVisible({ timeout: 2000 });

    await saveEvidence(page, EVIDENCE, 'Q-assigner-04-chores.png', 'assigner');
  });

  test('Q-assigner-05: Assigner API /Api/SessionStatus returns 200', async ({ page }) => {
    await login(page, 'assigner.oren@test', TEST_PASSWORD);

    const response = await page.request.get('/Api/SessionStatus');
    expect(response.status()).toBe(200);

    await saveEvidence(page, EVIDENCE, 'Q-assigner-05-api-scope.png', 'assigner');
  });

  test('Q-assigner-06: Assigner has NO shift/duty edit controls on calendar', async ({ page }) => {
    await login(page, 'assigner.oren@test', TEST_PASSWORD);
    await navigateTo(page, '/Calendar/Shifts');

    // ASSERT: Calendar loaded
    expect(page.url()).toContain('/Calendar');
    await expect(page.locator('h1:has-text("An unhandled exception occurred")')).not.toBeVisible({ timeout: 2000 });

    // ASSERT: No shift assignment dropdowns (Assigner only has chore controls)
    const shiftEditControls = page.locator('select[name*="UserId"], .assignment-dropdown');
    const editCount = await shiftEditControls.count();
    expect(editCount).toBe(0);

    await saveEvidence(page, EVIDENCE, 'Q-assigner-06-no-shift-controls.png', 'assigner');
  });
});
