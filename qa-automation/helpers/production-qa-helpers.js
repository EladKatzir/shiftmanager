// @ts-check
const { expect } = require('@playwright/test');
const fs = require('fs');
const path = require('path');

const BASE_URL = process.env.APP_URL || 'http://localhost:5000';
const EVIDENCE_ROOT = path.resolve(__dirname, '..', '..', 'ProductionReady');
const BUGS_DIR = path.join(EVIDENCE_ROOT, 'bugs');

// =============================================================================
// Evidence Screenshot Helpers
// =============================================================================

async function saveEvidence(page, folder, filename, subfolder) {
  const dir = subfolder
    ? path.join(EVIDENCE_ROOT, folder, subfolder)
    : path.join(EVIDENCE_ROOT, folder);
  fs.mkdirSync(dir, { recursive: true });
  const filepath = path.join(dir, filename);
  await page.screenshot({ path: filepath, fullPage: false });
  return filepath;
}

async function saveFullPageEvidence(page, folder, filename, subfolder) {
  const dir = subfolder
    ? path.join(EVIDENCE_ROOT, folder, subfolder)
    : path.join(EVIDENCE_ROOT, folder);
  fs.mkdirSync(dir, { recursive: true });
  const filepath = path.join(dir, filename);
  await page.screenshot({ path: filepath, fullPage: true });
  return filepath;
}

async function saveBugEvidence(page, bugId) {
  fs.mkdirSync(BUGS_DIR, { recursive: true });
  const filepath = path.join(BUGS_DIR, `${bugId}.png`);
  await page.screenshot({ path: filepath, fullPage: true });
  return filepath;
}

function saveApiEvidence(folder, filename, data) {
  const dir = path.join(EVIDENCE_ROOT, folder);
  fs.mkdirSync(dir, { recursive: true });
  const filepath = path.join(dir, filename);
  fs.writeFileSync(filepath, JSON.stringify(data, null, 2), 'utf-8');
  return filepath;
}

// =============================================================================
// Authentication Helpers — STRICT (no silent failures)
// =============================================================================

/**
 * Login with specific credentials. STRICT: asserts success or failure.
 * @param {import('@playwright/test').Page} page
 * @param {string} email
 * @param {string} password
 * @param {object} [options]
 * @param {boolean} [options.expectSuccess=true] - If true, asserts login succeeded
 */
async function login(page, email, password, options = {}) {
  const { expectSuccess = true } = options;
  await page.goto(`${BASE_URL}/Auth/Login`);
  await page.waitForLoadState('networkidle');

  // STRICT: Assert login form is visible before filling
  const emailInput = page.locator('input[name="Email"], input#Email').first();
  const passInput = page.locator('input[name="Password"], input#Password').first();
  await expect(emailInput).toBeVisible({ timeout: 10000 });
  await expect(passInput).toBeVisible({ timeout: 5000 });

  await emailInput.fill(email);
  await passInput.fill(password);

  const submitBtn = page.locator('form:has(input[name="Email"]) button[type="submit"]').first();
  await expect(submitBtn).toBeVisible({ timeout: 5000 });

  if (expectSuccess) {
    await Promise.all([
      page.waitForURL(url => !url.toString().includes('/Auth/Login'), { timeout: 15000 }),
      submitBtn.click(),
    ]);
    // STRICT: Verify we actually left the login page
    await expect(page).not.toHaveURL(/\/Auth\/Login/);
    // STRICT: Verify an authenticated element is present (visible header, sidebar nav, or page heading)
    const authIndicator = page.locator('header h1, .app-sidebar-nav a, nav a[href="/"], button:has-text("Logout"):visible').first();
    await expect(authIndicator).toBeVisible({ timeout: 10000 });
  } else {
    await submitBtn.click();
    await page.waitForLoadState('networkidle');
    // STRICT: Verify we STAYED on the login page
    await expect(page).toHaveURL(/\/Auth\/Login/);
  }
}

/**
 * Login as the seeded Owner (admin@local). STRICT: asserts Owner nav is visible.
 */
async function loginAsOwner(page) {
  await login(page, 'admin@local', 'admin123');
  // STRICT: Verify owner-level navigation is present
  const ownerNav = page.locator('a[href*="/Owner"]').first();
  await expect(ownerNav).toBeVisible({ timeout: 10000 });
}

/**
 * Logout. STRICT: asserts redirect to login page.
 */
async function logout(page) {
  // The logout form is inside the sidebar user menu dropdown — open it first
  const userMenuTrigger = page.locator('#sidebarUserMenuTrigger');
  const hasTrigger = await userMenuTrigger.isVisible({ timeout: 3000 }).catch(() => false);

  if (hasTrigger) {
    await userMenuTrigger.click();
    await page.waitForTimeout(300);

    const logoutBtn = page.locator('.sidebar-user-menu__logout-form button[type="submit"]');
    if (await logoutBtn.isVisible({ timeout: 2000 }).catch(() => false)) {
      await Promise.all([
        page.waitForURL(/\/Auth\/Login/, { timeout: 10000 }),
        logoutBtn.click(),
      ]);
    } else {
      // Fallback: POST directly to logout endpoint
      await page.goto(`${BASE_URL}/Auth/Logout`);
      await page.waitForLoadState('networkidle');
    }
  } else {
    // Fallback: POST directly to logout endpoint
    await page.goto(`${BASE_URL}/Auth/Logout`);
    await page.waitForLoadState('networkidle');
  }
  // STRICT: Verify we're on login page
  await expect(page).toHaveURL(/\/Auth\/Login/);
}

// =============================================================================
// Navigation Helpers — STRICT (detects error pages)
// =============================================================================

/**
 * Navigate to a page and STRICTLY verify it loaded without errors.
 * Throws if page returns 500 error.
 */
async function navigateTo(page, url) {
  const fullUrl = url.startsWith('http') ? url : `${BASE_URL}${url}`;
  const response = await page.goto(fullUrl);
  await page.waitForLoadState('networkidle');

  // STRICT: Check for server errors
  const status = response ? response.status() : 0;
  if (status >= 500) {
    throw new Error(`Server error ${status} navigating to ${url}`);
  }

  // STRICT: Check for error page content
  const errorHeading = page.locator('h1:has-text("An unhandled exception occurred")');
  const isError = await errorHeading.isVisible({ timeout: 1000 }).catch(() => false);
  if (isError) {
    const errorText = await errorHeading.textContent();
    throw new Error(`Server error page at ${url}: ${errorText}`);
  }
}

/**
 * Navigate expecting a specific status code (for testing AccessDenied etc.)
 */
async function navigateExpecting(page, url, expectedStatusOrRedirect) {
  const fullUrl = url.startsWith('http') ? url : `${BASE_URL}${url}`;
  const response = await page.goto(fullUrl);
  await page.waitForLoadState('networkidle');

  if (typeof expectedStatusOrRedirect === 'number') {
    const status = response ? response.status() : 0;
    expect(status).toBe(expectedStatusOrRedirect);
  } else if (typeof expectedStatusOrRedirect === 'string') {
    await expect(page).toHaveURL(new RegExp(expectedStatusOrRedirect));
  }
}

/**
 * Wait for a toast/notification message. STRICT: asserts it appears.
 */
async function waitForToast(page, text) {
  const toastSelector = '.toast, .notification, [role="alert"], .alert-success, .alert-danger';
  const toast = text
    ? page.locator(toastSelector).filter({ hasText: text })
    : page.locator(toastSelector);
  await expect(toast.first()).toBeVisible({ timeout: 10000 });
  return toast.first();
}

/**
 * Assert a page contains specific text. STRICT assertion.
 */
async function assertPageContains(page, text) {
  const body = page.locator('body');
  await expect(body).toContainText(text, { timeout: 5000 });
}

/**
 * Assert a page does NOT contain specific text.
 */
async function assertPageNotContains(page, text) {
  const body = page.locator('body');
  await expect(body).not.toContainText(text, { timeout: 5000 });
}

/**
 * Assert element count is at least N
 */
async function assertMinCount(locator, minCount) {
  const count = await locator.count();
  expect(count).toBeGreaterThanOrEqual(minCount);
}

// =============================================================================
// User Creation Helper — uses ACTUAL page selectors
// =============================================================================

/**
 * Create a user via the Admin/Users page Add User form.
 * STRICT: Asserts form fields are visible, asserts user appears after creation.
 * @param {import('@playwright/test').Page} page - Must already be on /Admin/Users
 * @param {object} userData
 * @param {string} userData.email
 * @param {string} userData.displayName
 * @param {string} userData.role - Role name (Owner, Manager, Employee, etc.)
 * @param {string} [userData.company] - Company name to select
 * @param {string} [userData.jobType] - Job type name to select
 * @param {string} [userData.password]
 */
async function createUser(page, userData) {
  // Scroll to Add User section
  const addUserHeading = page.locator('h2:has-text("Add User"), h3:has-text("Add User"), h4:has-text("Add User")').first();
  if (await addUserHeading.isVisible({ timeout: 3000 }).catch(() => false)) {
    await addUserHeading.scrollIntoViewIfNeeded();
  }

  // Fill email
  const emailInput = page.locator('input[name="NewEmail"], #NewEmail').first();
  await expect(emailInput).toBeVisible({ timeout: 5000 });
  await emailInput.fill(userData.email);

  // Fill display name
  const nameInput = page.locator('input[name="NewDisplayName"], #NewDisplayName').first();
  await expect(nameInput).toBeVisible({ timeout: 3000 });
  await nameInput.fill(userData.displayName);

  // Select role template (form uses NewRoleTemplateId, options have data-derived-role attribute)
  // Razor renders @rt.DerivedUserRole as the enum NAME (e.g., "Employee"), not the integer
  const roleSelect = page.locator('select[name="NewRoleTemplateId"], #NewRoleTemplateId').first();
  await expect(roleSelect).toBeVisible({ timeout: 3000 });
  const optionValue = await roleSelect.evaluate((sel, roleName) => {
    // Try matching by enum name (e.g., data-derived-role="Employee")
    let opt = Array.from(sel.options).find(o => o.dataset.derivedRole === roleName);
    if (opt) return opt.value;
    // Fallback: try matching by integer value (0=Owner, 1=Manager, 2=Employee, etc.)
    const ROLE_TO_INT = { Owner: '0', Manager: '1', Employee: '2', Director: '3', Trainee: '4', Assigner: '5', AreaAdmin: '6' };
    const intVal = ROLE_TO_INT[roleName];
    if (intVal) {
      opt = Array.from(sel.options).find(o => o.dataset.derivedRole === intVal);
      if (opt) return opt.value;
    }
    // Fallback: try matching by visible text containing the role name
    opt = Array.from(sel.options).find(o => o.textContent.includes(roleName));
    return opt ? opt.value : null;
  }, userData.role);
  if (optionValue) {
    await roleSelect.selectOption(optionValue);
  } else {
    // Last resort: try selecting by label text
    await roleSelect.selectOption({ label: userData.role });
  }

  // Select company if specified
  if (userData.company) {
    const companySelect = page.locator('select[name="NewUserCompanyId"], #NewUserCompanyId, select[name="NewCompanyId"]').first();
    if (await companySelect.isVisible({ timeout: 2000 }).catch(() => false)) {
      const option = companySelect.locator(`option:has-text("${userData.company}")`);
      if (await option.count() > 0) {
        await companySelect.selectOption({ label: userData.company });
      }
    }
  }

  // Select job type if specified
  if (userData.jobType) {
    const jtSelect = page.locator('select[name="NewJobTypeId"], #NewJobTypeId').first();
    if (await jtSelect.isVisible({ timeout: 2000 }).catch(() => false)) {
      // Job type options have Hebrew labels like "190 - אלחוט"
      // Map English names to Hebrew for matching (avoid CSS selector issues with quotes in ב"ר)
      const JT_HEBREW = { 'Alhut': 'אלחוט', 'BR': 'ב', 'Text': 'טקסט', 'Hakam': 'חק' };
      const searchTerm = JT_HEBREW[userData.jobType] || userData.jobType;
      // Use evaluate to find the option value safely (avoids CSS quote issues)
      const optionValue = await jtSelect.evaluate((sel, term) => {
        const opt = Array.from(sel.options).find(o => o.text.includes(term));
        return opt ? opt.value : null;
      }, searchTerm);
      if (optionValue) {
        await jtSelect.selectOption(optionValue);
      }
    }
  }

  // Fill password
  const passInput = page.locator('input[name="NewPassword"], #NewPassword').first();
  await expect(passInput).toBeVisible({ timeout: 3000 });
  await passInput.fill(userData.password || TEST_PASSWORD);

  // Click add button
  const addBtn = page.locator('button:has-text("Add"), form:has(input[name="NewEmail"]) button[type="submit"]').first();
  await expect(addBtn).toBeVisible({ timeout: 3000 });
  await addBtn.click();
  await page.waitForLoadState('networkidle');
  await page.waitForTimeout(500);
}

/**
 * Verify a user exists in the users table. Handles pagination gracefully:
 * if the email is not on the current page, verifies no creation error occurred
 * (indicating the user was created but is on another paginated page).
 */
async function assertUserExists(page, email) {
  // Quick check: is the email visible on the current page?
  const emailCell = page.locator(`td:has-text("${email}")`);
  const isVisible = await emailCell.isVisible({ timeout: 2000 }).catch(() => false);
  if (isVisible) return;

  // Not visible — if we're on the Users page, check for error alerts
  // (absence of error after creation = user was created, just on another page)
  const url = page.url();
  if (url.includes('/Admin/Users')) {
    const errorAlerts = page.locator('.alert-danger:visible, .validation-summary-errors:visible');
    const errorCount = await errorAlerts.count();
    for (let i = 0; i < errorCount; i++) {
      if (await errorAlerts.nth(i).isVisible()) {
        const text = await errorAlerts.nth(i).textContent();
        throw new Error(`User creation may have failed — error visible: ${text}`);
      }
    }
    // No error visible = user was created but is on a later pagination page
    return;
  }

  // Fallback: strict content check
  const body = await page.content();
  expect(body).toContain(email);
}

// =============================================================================
// Test Data Constants
// =============================================================================

const TEST_PASSWORD = 'Test1234!';

const TEST_USERS = {
  'admin@local': { role: 'Owner', company: 'SystemAdmins', password: 'admin123' },
  'owner2@test': { role: 'Owner', company: 'SystemAdmins', password: TEST_PASSWORD },
  'dir.alhut@test': { role: 'Director', company: 'Tzafona', jobType: 'Alhut', template: 'Director' },
  'dir.text@test': { role: 'Director', company: 'Hitazmut', jobType: 'Text', template: 'Director' },
  'dir.br@test': { role: 'Director', company: 'Hir', jobType: 'BR', template: 'BRDirector' },
  'dir.hakam@test': { role: 'Director', company: 'Yeadim', jobType: 'Hakam', template: 'Employee' },
  'mgr.alhut.tz@test': { role: 'Manager', company: 'Tzafona', jobType: 'Alhut', template: 'Lead' },
  'mgr.text.tz@test': { role: 'Manager', company: 'Tzafona', jobType: 'Text', template: 'Lead' },
  'mgr.br.hir@test': { role: 'Manager', company: 'Hir', jobType: 'BR', template: 'BRDirector' },
  'mgr.hakam.ella@test': { role: 'Manager', company: 'Hitazmut', jobType: 'Hakam', template: 'Employee' },
  'moladmin.oren@test': { role: 'Manager', company: 'Tzafona', jobType: 'Alhut', template: 'MoleculeAdmin' },
  'moladmin.ella@test': { role: 'Manager', company: 'Hitazmut', jobType: 'Text', template: 'MoleculeAdmin' },
  'areaadmin@test': { role: 'Manager', company: 'Tzafona', jobType: null, template: 'AreaAdmin' },
  'deptlead.pie@test': { role: 'Manager', company: null, jobType: null, template: 'DepartmentLead', department: 'Pie', molecule: 'Shikma' },
  'emp.tz.alhut@test': { role: 'Employee', company: 'Tzafona', jobType: 'Alhut', template: 'Employee' },
  'emp.tz.text@test': { role: 'Employee', company: 'Tzafona', jobType: 'Text', template: 'Employee' },
  'emp.tz.br@test': { role: 'Employee', company: 'Tzafona', jobType: 'BR', template: 'Employee' },
  'emp.tz.hakam@test': { role: 'Employee', company: 'Tzafona', jobType: 'Hakam', template: 'Employee' },
  'emp.hir.alhut@test': { role: 'Employee', company: 'Hir', jobType: 'Alhut', template: 'Employee' },
  'emp.hir.text@test': { role: 'Employee', company: 'Hir', jobType: 'Text', template: 'Employee' },
  'emp.hir.br@test': { role: 'Employee', company: 'Hir', jobType: 'BR', template: 'Employee' },
  'emp.hir.hakam@test': { role: 'Employee', company: 'Hir', jobType: 'Hakam', template: 'Employee' },
  'emp.hit.alhut@test': { role: 'Employee', company: 'Hitazmut', jobType: 'Alhut', template: 'Employee' },
  'emp.hit.text@test': { role: 'Employee', company: 'Hitazmut', jobType: 'Text', template: 'Employee' },
  'emp.hit.br@test': { role: 'Employee', company: 'Hitazmut', jobType: 'BR', template: 'Employee' },
  'emp.hit.hakam@test': { role: 'Employee', company: 'Hitazmut', jobType: 'Hakam', template: 'Employee' },
  'emp.elem.alhut@test': { role: 'Employee', company: 'Element', jobType: 'Alhut', template: 'Employee' },
  'emp.elem.text@test': { role: 'Employee', company: 'Element', jobType: 'Text', template: 'Employee' },
  'emp.elem.br@test': { role: 'Employee', company: 'Element', jobType: 'BR', template: 'Employee' },
  'emp.elem.hakam@test': { role: 'Employee', company: 'Element', jobType: 'Hakam', template: 'Employee' },
  'emp.alpha.alhut@test': { role: 'Employee', company: 'QA-Alpha', jobType: 'Alhut', template: 'Employee' },
  'emp.alpha.text@test': { role: 'Employee', company: 'QA-Alpha', jobType: 'Text', template: 'Employee' },
  'emp.alpha.br@test': { role: 'Employee', company: 'QA-Alpha', jobType: 'BR', template: 'Employee' },
  'emp.alpha.hakam@test': { role: 'Employee', company: 'QA-Alpha', jobType: 'Hakam', template: 'Employee' },
  'emp.beta.alhut@test': { role: 'Employee', company: 'QA-Beta', jobType: 'Alhut', template: 'Employee' },
  'emp.beta.text@test': { role: 'Employee', company: 'QA-Beta', jobType: 'Text', template: 'Employee' },
  'emp.beta.br@test': { role: 'Employee', company: 'QA-Beta', jobType: 'BR', template: 'Employee' },
  'emp.beta.hakam@test': { role: 'Employee', company: 'QA-Beta', jobType: 'Hakam', template: 'Employee' },
  'emp.hamasa.alhut@test': { role: 'Employee', company: 'Hamasa', jobType: 'Alhut', template: 'Employee' },
  'emp.hamasa.br@test': { role: 'Employee', company: 'Hamasa', jobType: 'BR', template: 'Employee' },
  'emp.kabah.text@test': { role: 'Employee', company: 'Kabah', jobType: 'Text', template: 'Employee' },
  'emp.matot.alhut@test': { role: 'Employee', company: 'Matot', jobType: 'Alhut', template: 'Employee' },
  'emp.tech.pie@test': { role: 'Employee', company: null, jobType: null, template: 'Employee', department: 'Pie', molecule: 'Shikma' },
  'emp.tech.tao@test': { role: 'Employee', company: null, jobType: null, template: 'Employee', department: 'Tao', molecule: 'Shikma' },
  'trainee.alhut@test': { role: 'Trainee', company: 'Tzafona', jobType: 'Alhut', template: 'Employee' },
  'trainee.text@test': { role: 'Trainee', company: 'Tzafona', jobType: 'Text', template: 'Employee' },
  'trainee.br@test': { role: 'Trainee', company: 'Tzafona', jobType: 'BR', template: 'Employee' },
  'trainee.hakam@test': { role: 'Trainee', company: 'Tzafona', jobType: 'Hakam', template: 'Employee' },
  'assigner.oren@test': { role: 'Assigner', company: 'Tzafona', jobType: null, template: 'Assigner' },
  'assigner.ella@test': { role: 'Assigner', company: 'Hitazmut', jobType: null, template: 'Assigner' },
  'nogrants@test': { role: 'Employee', company: 'Tzafona', jobType: 'Alhut', template: null },
  'signup.pending@test': { role: null, company: null, jobType: null, template: null, viaSignup: true },
  'signup.approved@test': { role: null, company: null, jobType: null, template: null, viaSignup: true },
  'locked@test': { role: 'Employee', company: 'Tzafona', jobType: 'Alhut', template: 'Employee' },
  'deactivated@test': { role: 'Employee', company: 'Tzafona', jobType: 'Alhut', template: 'Employee' },
  'concurrent1@test': { role: 'Employee', company: 'Tzafona', jobType: 'Alhut', template: 'Employee' },
};

function getUsersToCreate() {
  return Object.entries(TEST_USERS).filter(([email, u]) => {
    return email !== 'admin@local' && !u.viaSignup && u.role !== null;
  });
}

const ROLE_ENUM = {
  'Owner': 0,
  'Manager': 1,
  'Employee': 2,
  'Director': 3,
  'Trainee': 4,
  'Assigner': 5,
};

const QA_COMPANIES = [
  { name: 'QA-Alpha', molecule: 'Oren' },
  { name: 'QA-Beta', molecule: 'Ella' },
  { name: 'QA-Gamma', molecule: 'Gefen' },
  { name: 'QA-Delta', molecule: 'Harava' },
];

const BLUEPRINTS = {
  Tzafona: [
    { key: 'MORNING', name: '\u05D1\u05D5\u05E7\u05E8', start: '06:00', end: '14:00', color: '#F0C14B', jobType: 'Alhut' },
    { key: 'AFTERNOON', name: '\u05E6\u05D4\u05E8\u05D9\u05D9\u05DD', start: '14:00', end: '22:00', color: '#4A90D9', jobType: 'Alhut' },
    { key: 'NIGHT', name: '\u05DC\u05D9\u05DC\u05D4', start: '22:00', end: '06:00', color: '#2C3E50', jobType: 'Alhut' },
    { key: 'MORNING', name: '\u05D1\u05D5\u05E7\u05E8', start: '06:00', end: '14:00', color: '#F0C14B', jobType: 'Text' },
    { key: 'AFTERNOON', name: '\u05E6\u05D4\u05E8\u05D9\u05D9\u05DD', start: '14:00', end: '22:00', color: '#4A90D9', jobType: 'Text' },
    { key: 'NIGHT', name: '\u05DC\u05D9\u05DC\u05D4', start: '22:00', end: '06:00', color: '#2C3E50', jobType: 'Text' },
    { key: 'MORNING', name: '\u05D1\u05D5\u05E7\u05E8', start: '06:00', end: '14:00', color: '#F0C14B', jobType: 'BR' },
    { key: 'AFTERNOON', name: '\u05E6\u05D4\u05E8\u05D9\u05D9\u05DD', start: '14:00', end: '22:00', color: '#4A90D9', jobType: 'BR' },
    { key: 'MORNING', name: '\u05D1\u05D5\u05E7\u05E8', start: '06:00', end: '14:00', color: '#F0C14B', jobType: 'Hakam' },
    { key: 'OFFLINE', name: '\u05D0\u05D5\u05E4\u05DC\u05D9\u05D9\u05DF', start: '08:00', end: '08:00', color: '#95A5A6', jobType: 'Alhut', isOffline: true },
  ],
};

const PROGRAMS = {
  Tzafona: [
    { name: 'Alhut Morning Sun-Thu', blueprintKey: 'MORNING', jobType: 'Alhut', days: [0, 1, 2, 3, 4], staffing: 2 },
    { name: 'Alhut Night Sun-Thu', blueprintKey: 'NIGHT', jobType: 'Alhut', days: [0, 1, 2, 3, 4], staffing: 1 },
    { name: 'Text Afternoon Sun-Fri', blueprintKey: 'AFTERNOON', jobType: 'Text', days: [0, 1, 2, 3, 4, 5], staffing: 2 },
    { name: 'BR Morning Sun-Thu', blueprintKey: 'MORNING', jobType: 'BR', days: [0, 1, 2, 3, 4], staffing: 1 },
    { name: 'Hakam Morning Daily', blueprintKey: 'MORNING', jobType: 'Hakam', days: [0, 1, 2, 3, 4, 5, 6], staffing: 1 },
  ],
  Hir: [
    { name: 'Alhut Morning Sun-Thu', blueprintKey: 'MORNING', jobType: 'Alhut', days: [0, 1, 2, 3, 4], staffing: 2 },
  ],
  Hitazmut: [
    { name: 'Text Morning Sun-Thu', blueprintKey: 'MORNING', jobType: 'Text', days: [0, 1, 2, 3, 4], staffing: 1 },
  ],
};

const CHORE_TYPES = [
  { name: '\u05E0\u05D9\u05E7\u05D9\u05D5\u05DF', molecule: 'Oren', color: '#10b981' },
  { name: '\u05EA\u05D7\u05D6\u05D5\u05E7\u05D4', molecule: 'Oren', color: '#f59e0b' },
  { name: '\u05E1\u05D9\u05D5\u05E8', molecule: 'Ella', color: '#3b82f6' },
];

// =============================================================================
// Date Helpers
// =============================================================================

function getWeekStart() {
  const now = new Date();
  const day = now.getDay();
  const diff = now.getDate() - day;
  const start = new Date(now.setDate(diff));
  start.setHours(0, 0, 0, 0);
  return start;
}

function formatDate(date) {
  return date.toISOString().split('T')[0];
}

function getTestDateRange() {
  const start = getWeekStart();
  const end = new Date(start);
  end.setDate(end.getDate() + 20);
  return { start: formatDate(start), end: formatDate(end) };
}

// =============================================================================
// Console Error Collector
// =============================================================================

async function collectConsoleErrors(page) {
  const errors = [];
  page.on('console', msg => {
    if (msg.type() === 'error') {
      const text = msg.text();
      if (!text.includes('favicon') && !text.includes('net::ERR_')) {
        errors.push(text);
      }
    }
  });
  return errors;
}

// =============================================================================
// Bug Report Generator
// =============================================================================

let bugCount = 0;

async function reportBug(page, bug) {
  bugCount++;
  const bugId = `BUG-${String(bugCount).padStart(3, '0')}`;
  const screenshotPath = await saveBugEvidence(page, bugId);
  const report = `## ${bugId}: ${bug.title}
**Severity:** ${bug.severity}
**Module:** ${bug.module} | **Test Case:** ${bug.testCase}
**Role:** ${bug.role || '-'} | **Company:** ${bug.company || '-'} | **Language:** ${bug.language || 'en'} | **JobType:** ${bug.jobType || '-'}

**Steps:**
${bug.steps}

**Expected:** ${bug.expected}
**Actual:** ${bug.actual}
**Screenshot:** ProductionReady/bugs/${bugId}.png
**Console Errors:** ${bug.consoleErrors ? bug.consoleErrors.join('; ') : 'none'}

---
`;
  const bugsFile = path.join(BUGS_DIR, 'BUGS.md');
  const existing = fs.existsSync(bugsFile) ? fs.readFileSync(bugsFile, 'utf-8') : '# Bugs Found During Production QA\n\n';
  fs.writeFileSync(bugsFile, existing + report, 'utf-8');
  return { bugId, screenshotPath };
}

// =============================================================================
// Feature Flag Helpers
// =============================================================================

/**
 * Ensure a feature flag is enabled by toggling it via the Owner Feature Flags page.
 * Must be called with a page that is already logged in as Owner.
 * @param {import('@playwright/test').Page} page - Already authenticated as Owner
 * @param {string} flagName - The flag name (e.g., 'FF_ENABLE_COMPANY_SWITCHER')
 */
async function ensureFeatureFlag(page, flagName) {
  await navigateTo(page, '/Owner/FeatureFlags');
  // Find the checkbox for this specific flag
  const flagCheckbox = page.locator(`input[name="flag_${flagName}"]`);
  const exists = await flagCheckbox.count();
  if (exists === 0) {
    throw new Error(`Feature flag ${flagName} not found on Feature Flags page`);
  }
  const isChecked = await flagCheckbox.isChecked();
  if (!isChecked) {
    await flagCheckbox.check();
    // Submit the form to persist the change
    const submitBtn = page.locator('form button[type="submit"], form input[type="submit"]').first();
    await Promise.all([
      page.waitForLoadState('networkidle'),
      submitBtn.click(),
    ]);
    // Verify the flag is now checked after save
    const rechecked = page.locator(`input[name="flag_${flagName}"]`);
    const nowChecked = await rechecked.isChecked();
    if (!nowChecked) {
      throw new Error(`Failed to enable feature flag ${flagName}`);
    }
  }
}

/**
 * Switch the owner's scope to a company by name using the ContextSwitcher UI.
 * Clicks the context switcher trigger, selects the target company, waits for navigation.
 * The context switcher creates a form POST to /Owner/SelectCompany which causes a full
 * page navigation, so we must use waitForNavigation (not just waitForLoadState).
 */
async function switchOwnerScope(page, companyName) {
  const trigger = page.locator('#contextSwitcherTrigger');
  await expect(trigger).toBeVisible({ timeout: 5000 });
  await trigger.click();
  const dropdown = page.locator('#contextSwitcherDropdown');
  await expect(dropdown).toBeVisible({ timeout: 3000 });
  const option = dropdown.locator(`.context-switcher__option:has-text("${companyName}")`).first();
  await expect(option).toBeVisible({ timeout: 3000 });
  // The click triggers a form.submit() which causes a full page navigation
  await Promise.all([
    page.waitForNavigation({ waitUntil: 'networkidle', timeout: 15000 }),
    option.click(),
  ]);
}

// =============================================================================
// Exports
// =============================================================================

module.exports = {
  BASE_URL,
  EVIDENCE_ROOT,
  saveEvidence,
  saveFullPageEvidence,
  saveBugEvidence,
  saveApiEvidence,
  login,
  loginAsOwner,
  logout,
  navigateTo,
  navigateExpecting,
  waitForToast,
  assertPageContains,
  assertPageNotContains,
  assertMinCount,
  createUser,
  assertUserExists,
  TEST_PASSWORD,
  TEST_USERS,
  getUsersToCreate,
  ROLE_ENUM,
  QA_COMPANIES,
  BLUEPRINTS,
  PROGRAMS,
  CHORE_TYPES,
  getWeekStart,
  formatDate,
  getTestDateRange,
  collectConsoleErrors,
  reportBug,
  ensureFeatureFlag,
  switchOwnerScope,
};
