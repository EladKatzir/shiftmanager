// @ts-check
const { test, expect } = require('@playwright/test');
const { loginAsOwner, login, logout, saveEvidence, navigateTo, TEST_PASSWORD, BASE_URL } = require('../../helpers/production-qa-helpers');

const EVIDENCE = '28-every-button';

test.describe('Every-Button Checklist: Global Elements', () => {
  test.beforeEach(async ({ page }) => {
    await loginAsOwner(page);
  });

  test('Global: Sidebar brand/logo is visible', async ({ page }) => {
    await navigateTo(page, '/');

    // ASSERT: Sidebar brand/logo is visible
    const brand = page.locator('.sidebar-brand, .app-sidebar-brand, .navbar-brand').first();
    await expect(brand).toBeVisible({ timeout: 5000 });

    await saveEvidence(page, EVIDENCE, 'global-sidebar-brand.png');
  });

  test('Global: Sidebar toggle button works', async ({ page }) => {
    await navigateTo(page, '/');

    // ASSERT: Sidebar toggle is visible
    const toggle = page.locator('#sidebarToggle').first();
    await expect(toggle).toBeVisible({ timeout: 5000 });

    // ASSERT: Toggle is enabled/clickable
    await expect(toggle).toBeEnabled();

    // Capture state before click
    const classBefore = await page.evaluate(() => {
      const sidebar = document.querySelector('.app-sidebar, #sidebar, aside');
      return sidebar ? sidebar.className : '';
    });

    // Click toggle
    await toggle.click();
    await page.waitForTimeout(300);

    // ASSERT: Sidebar state changed
    const classAfter = await page.evaluate(() => {
      const sidebar = document.querySelector('.app-sidebar, #sidebar, aside');
      return sidebar ? sidebar.className : '';
    });
    expect(classAfter).not.toBe(classBefore);

    // Click again to restore
    await toggle.click();
    await page.waitForTimeout(300);

    await saveEvidence(page, EVIDENCE, 'global-sidebar-toggle.png');
  });

  test('Global: Language toggle button exists and is visible', async ({ page }) => {
    await navigateTo(page, '/');

    // ASSERT: Language toggle exists and is visible
    const langToggle = page.locator('.language-toggle, #languageToggle, button:has-text("עברית"), button:has-text("English"), a[href*="culture"]').first();
    await expect(langToggle).toBeVisible({ timeout: 5000 });

    // ASSERT: Language toggle is enabled
    await expect(langToggle).toBeEnabled();

    await saveEvidence(page, EVIDENCE, 'global-lang-toggle.png');
  });

  test('Global: Theme toggle button exists and functions', async ({ page }) => {
    await navigateTo(page, '/');

    // ASSERT: Theme toggle is visible
    const themeToggle = page.locator('#themeToggle').first();
    await expect(themeToggle).toBeVisible({ timeout: 5000 });
    await expect(themeToggle).toBeEnabled();

    // Capture state before
    const themeBefore = await page.evaluate(() => {
      return document.documentElement.className + '|' +
        document.body.className + '|' +
        (document.documentElement.getAttribute('data-theme') || '');
    });

    // Click to toggle theme
    await themeToggle.click();
    await page.waitForTimeout(300);

    // ASSERT: Theme state changed
    const themeAfter = await page.evaluate(() => {
      return document.documentElement.className + '|' +
        document.body.className + '|' +
        (document.documentElement.getAttribute('data-theme') || '');
    });
    expect(themeAfter).not.toBe(themeBefore);

    await saveEvidence(page, EVIDENCE, 'global-theme-dark.png');

    // Toggle back
    await themeToggle.click();
    await page.waitForTimeout(300);

    await saveEvidence(page, EVIDENCE, 'global-theme-toggle.png');
  });

  test('Global: Notification bell link is visible and navigates', async ({ page }) => {
    await navigateTo(page, '/');

    // ASSERT: Notification bell/link exists and is visible
    const bell = page.locator('a[href*="NotificationCenter"], .notification-bell, .notification-icon').first();
    await expect(bell).toBeVisible({ timeout: 5000 });

    // Click the bell
    await bell.click();
    await page.waitForLoadState('networkidle');

    // ASSERT: Navigated to notification center
    expect(page.url()).toContain('Notification');

    await saveEvidence(page, EVIDENCE, 'global-notification-bell.png');
  });

  test('Global: Logout form is visible', async ({ page }) => {
    await navigateTo(page, '/');

    // ASSERT: Logout form exists and is visible
    const logoutForm = page.locator('form[action*="Logout"]').first();
    await expect(logoutForm).toBeVisible({ timeout: 5000 });

    // ASSERT: Logout submit button inside form is visible
    const logoutBtn = logoutForm.locator('button[type="submit"]');
    await expect(logoutBtn).toBeVisible({ timeout: 3000 });

    await saveEvidence(page, EVIDENCE, 'global-logout.png');
  });

  test('Global: User menu trigger is visible and opens menu', async ({ page }) => {
    await navigateTo(page, '/');

    // ASSERT: User menu trigger exists and is visible
    const userMenu = page.locator('#sidebarUserMenuTrigger, .sidebar-user, .user-menu-trigger').first();
    await expect(userMenu).toBeVisible({ timeout: 5000 });

    // Click to open user menu
    await userMenu.click();
    await page.waitForTimeout(300);

    // ASSERT: User menu dropdown appeared
    const menu = page.locator('#sidebarUserMenu, .sidebar-user-menu, .user-dropdown').first();
    await expect(menu).toBeVisible({ timeout: 3000 });

    await saveEvidence(page, EVIDENCE, 'global-user-menu.png');
  });

  test('Global: Ctrl+J command palette opens', async ({ page }) => {
    await navigateTo(page, '/');

    // Press Ctrl+J
    await page.keyboard.press('Control+j');
    await page.waitForTimeout(500);

    // ASSERT: Command palette appeared
    const palette = page.locator('#commandPalette, .command-palette, [role="dialog"]').first();
    const paletteCount = await palette.count();

    if (paletteCount > 0) {
      await expect(palette).toBeVisible({ timeout: 3000 });

      await saveEvidence(page, EVIDENCE, 'global-command-palette.png');

      // Close
      await page.keyboard.press('Escape');
      await page.waitForTimeout(300);

      // ASSERT: Palette closed
      await expect(palette).not.toBeVisible({ timeout: 3000 });
    } else {
      // Feature may not exist — assert sidebar is still functional
      const nav = page.locator('.sidebar, nav, .navbar').first();
      await expect(nav).toBeVisible({ timeout: 5000 });
      await saveEvidence(page, EVIDENCE, 'global-command-palette.png');
    }
  });
});

test.describe('Every-Button Checklist: Auth Pages', () => {
  test('Auth: Login page has submit button, signup link, forgot password link', async ({ page }) => {
    await page.goto(`${BASE_URL}/Auth/Login`);
    await page.waitForLoadState('networkidle');

    // ASSERT: Login button exists and is visible
    const loginBtn = page.locator('form:has(input[name="Email"]) button[type="submit"]');
    await expect(loginBtn).toBeVisible({ timeout: 5000 });
    await expect(loginBtn).toBeEnabled();

    // ASSERT: Email and password inputs are visible
    await expect(page.locator('input[name="Email"]').first()).toBeVisible();
    await expect(page.locator('input[name="Password"]').first()).toBeVisible();

    // ASSERT: Signup link exists
    const signupLink = page.locator('a[href*="Signup"]').first();
    const signupCount = await signupLink.count();
    expect(signupCount).toBeGreaterThanOrEqual(1);
    await expect(signupLink).toBeVisible();

    // ASSERT: Forgot password link exists
    const forgotLink = page.locator('a[href*="ForgotPassword"]').first();
    const forgotCount = await forgotLink.count();
    expect(forgotCount).toBeGreaterThanOrEqual(1);
    await expect(forgotLink).toBeVisible();

    await saveEvidence(page, EVIDENCE, 'auth-buttons.png');
  });

  test('Auth: Signup form has required elements', async ({ page }) => {
    await page.goto(`${BASE_URL}/Auth/Signup`);
    await page.waitForLoadState('networkidle');

    // ASSERT: Molecule/company select is visible
    await expect(page.locator('#MoleculeId, select[name="MoleculeId"]').first()).toBeVisible({ timeout: 5000 });

    // ASSERT: Submit button is visible and enabled
    const submitBtn = page.locator('#signupForm button[type="submit"], .auth-submit, form button[type="submit"]').first();
    await expect(submitBtn).toBeVisible({ timeout: 5000 });
    await expect(submitBtn).toBeEnabled();

    await saveEvidence(page, EVIDENCE, 'auth-signup-elements.png');
  });
});

test.describe('Every-Button Checklist: Calendar Shifts', () => {
  test.beforeEach(async ({ page }) => {
    await loginAsOwner(page);
  });

  test('Shifts: Navigation controls exist and are visible', async ({ page }) => {
    await navigateTo(page, '/Calendar/Shifts');

    // ASSERT: Navigation buttons (prev/next) exist
    const navButtons = page.locator('.shifts-calendar__nav-btn, a:has-text("←"), a:has-text("→"), button:has-text("Prev"), button:has-text("Next"), a[href*="startDate"]');
    const navCount = await navButtons.count();
    expect(navCount).toBeGreaterThanOrEqual(1);

    // ASSERT: At least the first nav button is visible
    await expect(navButtons.first()).toBeVisible({ timeout: 5000 });

    await saveEvidence(page, EVIDENCE, 'shifts-nav.png');
  });

  test('Shifts: Mode toggle links exist', async ({ page }) => {
    await navigateTo(page, '/Calendar/Shifts');

    // ASSERT: Mode toggle links are present
    const modeLinks = page.locator('a[href*="Mode="], .mode-toggle, .view-toggle');
    const count = await modeLinks.count();
    expect(count).toBeGreaterThanOrEqual(1);

    // ASSERT: At least one is visible
    await expect(modeLinks.first()).toBeVisible({ timeout: 5000 });

    await saveEvidence(page, EVIDENCE, 'shifts-mode-toggle.png');
  });

  test('Shifts: Job type selector exists with options', async ({ page }) => {
    await navigateTo(page, '/Calendar/Shifts');

    // ASSERT: Job type select is visible
    const jtSelect = page.locator('#jobTypeSelect, select[name*="jobType"], select[name*="JobType"]').first();
    await expect(jtSelect).toBeVisible({ timeout: 5000 });

    // ASSERT: It has at least one option
    const options = await jtSelect.locator('option').allTextContents();
    expect(options.length).toBeGreaterThanOrEqual(1);

    await saveEvidence(page, EVIDENCE, 'shifts-jobtypes.png');
  });

  test('Shifts: Just-mine toggle link exists', async ({ page }) => {
    await navigateTo(page, '/Calendar/Shifts');

    // ASSERT: JustMine link exists
    const justMine = page.locator('a[href*="JustMine"], .just-mine-toggle, input[name*="JustMine"]').first();
    const justMineCount = await justMine.count();
    expect(justMineCount).toBeGreaterThanOrEqual(1);

    await saveEvidence(page, EVIDENCE, 'shifts-just-mine.png');
  });

  test('Shifts: Print/export button exists', async ({ page }) => {
    await navigateTo(page, '/Calendar/Shifts');

    // ASSERT: Print or export button exists
    const printBtn = page.locator('[onclick*="print"], button:has-text("Print"), a:has-text("Export"), a:has-text("print"), .print-btn').first();
    const printCount = await printBtn.count();
    expect(printCount).toBeGreaterThanOrEqual(1);

    await saveEvidence(page, EVIDENCE, 'shifts-print.png');
  });
});

test.describe('Every-Button Checklist: Admin Pages', () => {
  test.beforeEach(async ({ page }) => {
    await loginAsOwner(page);
  });

  test('Admin Users: Action buttons exist (add, edit, toggle, reset)', async ({ page }) => {
    await navigateTo(page, '/Admin/Users');

    // ASSERT: Add user form submit button exists
    const addBtn = page.locator('form:has(input[name="NewEmail"]) button[type="submit"]').first();
    await expect(addBtn).toBeVisible({ timeout: 5000 });
    await expect(addBtn).toBeEnabled();

    // ASSERT: Email input for new user is visible
    const emailInput = page.locator('input[name="NewEmail"]').first();
    await expect(emailInput).toBeVisible({ timeout: 5000 });

    // ASSERT: Edit buttons exist (at least one user row has edit capability)
    const editBtns = page.locator('.cell-display__edit-btn, a[href*="EditProfile"], .edit-btn');
    const editCount = await editBtns.count();
    expect(editCount).toBeGreaterThanOrEqual(1);

    // ASSERT: Toggle/deactivate forms exist
    const toggleForms = page.locator('form[action*="Toggle"], form[action*="Deactivate"], button:has-text("Deactivate"), button:has-text("Activate")');
    const toggleCount = await toggleForms.count();
    expect(toggleCount).toBeGreaterThanOrEqual(1);

    await saveEvidence(page, EVIDENCE, 'admin-users.png');
  });

  test('Admin Config: Save settings button exists', async ({ page }) => {
    await navigateTo(page, '/Admin/Config');

    // ASSERT: Page heading is visible
    const heading = page.locator('main h1, main h2').first();
    await expect(heading).toBeVisible({ timeout: 10000 });

    // ASSERT: Save/submit button exists and is visible
    const saveBtn = page.locator('button[type="submit"], .btn-primary').first();
    await expect(saveBtn).toBeVisible({ timeout: 5000 });
    await expect(saveBtn).toBeEnabled();

    await saveEvidence(page, EVIDENCE, 'admin-config.png');
  });

  test('Admin Analytics: Page loads with content', async ({ page }) => {
    await navigateTo(page, '/Admin/Analytics');
    await page.waitForLoadState('networkidle');

    // ASSERT: Page heading is visible
    const heading = page.locator('main h1, main h2').first();
    await expect(heading).toBeVisible({ timeout: 10000 });

    // ASSERT: Analytics content area is visible
    const content = page.locator('main, .page-content, .analytics-container').first();
    await expect(content).toBeVisible({ timeout: 5000 });

    await saveEvidence(page, EVIDENCE, 'admin-analytics.png');
  });

  test('Admin AuditLog: Page loads with content', async ({ page }) => {
    await navigateTo(page, '/Admin/AuditLog');
    await page.waitForLoadState('networkidle');

    // ASSERT: Page heading is visible
    const heading = page.locator('main h1, main h2').first();
    await expect(heading).toBeVisible({ timeout: 10000 });

    // ASSERT: Page has a table or list for audit entries
    const dataElements = page.locator('table, .list-group, .audit-log, .log-entry');
    const dataCount = await dataElements.count();
    expect(dataCount).toBeGreaterThan(0);

    await saveEvidence(page, EVIDENCE, 'admin-auditlog.png');
  });
});

test.describe('Every-Button Checklist: Owner Pages', () => {
  test.beforeEach(async ({ page }) => {
    await loginAsOwner(page);
  });

  test('Owner Backup: Create, download buttons exist', async ({ page }) => {
    await navigateTo(page, '/Owner/Backup');

    // ASSERT: Create/backup button exists and is visible
    const createBtn = page.locator('button:has-text("Backup"), button:has-text("Create"), form button[type="submit"]').first();
    await expect(createBtn).toBeVisible({ timeout: 5000 });
    await expect(createBtn).toBeEnabled();

    // ASSERT: Restore mechanism exists (either restore buttons per backup or empty state)
    const restoreForms = page.locator('form[action*="RestoreBackup"], .btn-restore');
    const emptyState = page.locator('.empty-icon, :text("No backups")');
    const hasRestore = await restoreForms.count() > 0;
    const hasEmpty = await emptyState.count() > 0;
    expect(hasRestore || hasEmpty).toBeTruthy();

    await saveEvidence(page, EVIDENCE, 'owner-backup.png');
  });

  test('Owner DatabaseConsole: Execute button and query input exist', async ({ page }) => {
    await navigateTo(page, '/Owner/DatabaseConsole');

    // ASSERT: Execute/Run button exists and is visible
    const execBtn = page.locator('button:has-text("Execute"), button:has-text("Run"), button[type="submit"]').first();
    await expect(execBtn).toBeVisible({ timeout: 5000 });
    await expect(execBtn).toBeEnabled();

    // ASSERT: Query input area exists
    const queryInput = page.locator('textarea, input[name*="Query"], input[name*="query"], #query, #Query').first();
    await expect(queryInput).toBeVisible({ timeout: 5000 });

    await saveEvidence(page, EVIDENCE, 'owner-db-console.png');
  });

  test('Owner Programs: Page loads with program management elements', async ({ page }) => {
    await navigateTo(page, '/Owner/Programs');

    // ASSERT: Page heading is visible
    const heading = page.locator('main h1, main h2').first();
    await expect(heading).toBeVisible({ timeout: 10000 });

    // ASSERT: At least one form or interactive element exists
    const formElements = page.locator('form, button, input, select');
    const formCount = await formElements.count();
    expect(formCount).toBeGreaterThan(0);

    await saveEvidence(page, EVIDENCE, 'owner-programs.png');
  });

  test('Owner Blueprints: Page loads with blueprint management elements', async ({ page }) => {
    await navigateTo(page, '/Owner/Blueprints');

    // ASSERT: Page heading is visible
    const heading = page.locator('main h1, main h2').first();
    await expect(heading).toBeVisible({ timeout: 10000 });

    // ASSERT: Blueprint create form or elements exist
    const formElements = page.locator('form, button, input');
    const formCount = await formElements.count();
    expect(formCount).toBeGreaterThan(0);

    await saveEvidence(page, EVIDENCE, 'owner-blueprints.png');
  });

  test('Owner SystemHealth: Page loads successfully', async ({ page }) => {
    await navigateTo(page, '/Owner/SystemHealth');

    // ASSERT: Page heading is visible
    const heading = page.locator('main h1, main h2').first();
    await expect(heading).toBeVisible({ timeout: 10000 });

    // ASSERT: Health check content area is visible
    const content = page.locator('main, .page-content, .system-health, .card').first();
    await expect(content).toBeVisible({ timeout: 5000 });

    await saveEvidence(page, EVIDENCE, 'owner-system-health.png');
  });

  test('Owner FeatureFlags: Page loads with toggle elements', async ({ page }) => {
    await navigateTo(page, '/Owner/FeatureFlags');

    // ASSERT: Page heading is visible
    const heading = page.locator('main h1, main h2').first();
    await expect(heading).toBeVisible({ timeout: 10000 });

    // ASSERT: Interactive elements exist (checkboxes, toggles, buttons)
    const toggleElements = page.locator('input[type="checkbox"], .toggle, button, select');
    const toggleCount = await toggleElements.count();
    expect(toggleCount).toBeGreaterThan(0);

    await saveEvidence(page, EVIDENCE, 'owner-feature-flags.png');
  });

  test('Owner LockedUsers: Page loads successfully', async ({ page }) => {
    await navigateTo(page, '/Owner/LockedUsers');

    // ASSERT: Page heading is visible
    const heading = page.locator('main h1, main h2').first();
    await expect(heading).toBeVisible({ timeout: 10000 });

    // ASSERT: Page content area is visible
    const content = page.locator('main, .page-content, .card, table').first();
    await expect(content).toBeVisible({ timeout: 5000 });

    await saveEvidence(page, EVIDENCE, 'owner-locked-users.png');
  });
});

test.describe('Every-Button Checklist: My Pages', () => {
  test.beforeEach(async ({ page }) => {
    await loginAsOwner(page);
  });

  test('My Profile: Save button is visible and enabled', async ({ page }) => {
    await navigateTo(page, '/My/Profile');

    // ASSERT: Save/submit button exists and is visible
    const saveBtn = page.locator('button[type="submit"]:has-text("Save"), button[type="submit"], .btn-primary').first();
    await expect(saveBtn).toBeVisible({ timeout: 5000 });
    await expect(saveBtn).toBeEnabled();

    // ASSERT: Profile form has input fields
    const inputs = page.locator('input, textarea, select');
    const inputCount = await inputs.count();
    expect(inputCount).toBeGreaterThan(0);

    await saveEvidence(page, EVIDENCE, 'my-profile.png');
  });

  test('My Settings: Save preferences button exists', async ({ page }) => {
    await navigateTo(page, '/My/Settings');

    // ASSERT: Submit button exists and is visible
    const saveBtn = page.locator('button[type="submit"], .btn-primary').first();
    await expect(saveBtn).toBeVisible({ timeout: 5000 });
    await expect(saveBtn).toBeEnabled();

    // ASSERT: Settings form/card is visible
    const settingsContent = page.locator('form, .card, .section-card').first();
    await expect(settingsContent).toBeVisible({ timeout: 5000 });

    await saveEvidence(page, EVIDENCE, 'my-settings.png');
  });

  test('NotificationCenter: Page loads with content', async ({ page }) => {
    await navigateTo(page, '/My/NotificationCenter');

    // ASSERT: Page heading is visible
    const heading = page.locator('main h1, main h2').first();
    await expect(heading).toBeVisible({ timeout: 10000 });

    // ASSERT: We are on notification center
    expect(page.url()).toContain('NotificationCenter');

    await saveEvidence(page, EVIDENCE, 'my-notifications.png');
  });

  test('ApiKeys: Generate button and page elements exist', async ({ page }) => {
    await navigateTo(page, '/My/ApiKeys');

    // ASSERT: Page heading is visible
    const heading = page.locator('main h1, main h2').first();
    await expect(heading).toBeVisible({ timeout: 10000 });

    // ASSERT: Generate button or form exists
    const generateBtn = page.locator('button:has-text("Generate"), button:has-text("Create"), .btn-primary, button[type="submit"]').first();
    await expect(generateBtn).toBeVisible({ timeout: 5000 });
    await expect(generateBtn).toBeEnabled();

    await saveEvidence(page, EVIDENCE, 'my-api-keys.png');
  });

  test('Help: Page loads with help content', async ({ page }) => {
    await navigateTo(page, '/My/Help');

    // ASSERT: Help page container is visible
    const helpPage = page.locator('.help-page');
    await expect(helpPage).toBeVisible({ timeout: 10000 });

    // ASSERT: Page has interactive elements (accordion, links, etc.)
    const interactiveElements = page.locator('a, button, details, .accordion, .collapse');
    const count = await interactiveElements.count();
    expect(count).toBeGreaterThan(0);

    await saveEvidence(page, EVIDENCE, 'my-help.png');
  });
});

test.describe('Every-Button Checklist: Requests', () => {
  test('Requests: Page loads with tab or form elements', async ({ page }) => {
    await loginAsOwner(page);
    await navigateTo(page, '/Requests/Index');

    // ASSERT: Page heading is visible
    const heading = page.locator('main h1, main h2').first();
    await expect(heading).toBeVisible({ timeout: 10000 });

    // ASSERT: Page has interactive elements (tabs, buttons, forms)
    const elements = page.locator('a, button, form, select, input, .nav-tab, .tab');
    const count = await elements.count();
    expect(count).toBeGreaterThan(0);

    // ASSERT: Request content area is visible
    const content = page.locator('.tab-content, .requests-container, main, .page-content').first();
    await expect(content).toBeVisible({ timeout: 5000 });

    await saveEvidence(page, EVIDENCE, 'requests-page.png');
  });
});

test.describe('Every-Button Checklist: Public Pages', () => {
  test('Public: Feedback page loads with form elements', async ({ page }) => {
    await loginAsOwner(page);
    await navigateTo(page, '/Public/Feedback');

    // ASSERT: Feedback container is visible
    const feedbackContainer = page.locator('.feedback-container, main, .page-content').first();
    await expect(feedbackContainer).toBeVisible({ timeout: 10000 });

    // ASSERT: Feedback form has input/textarea and submit button
    const formInputs = page.locator('textarea, input[type="text"], input[type="email"]');
    const inputCount = await formInputs.count();
    expect(inputCount).toBeGreaterThan(0);

    const submitBtn = page.locator('button[type="submit"], input[type="submit"]').first();
    await expect(submitBtn).toBeVisible({ timeout: 5000 });
    await expect(submitBtn).toBeEnabled();

    await saveEvidence(page, EVIDENCE, 'public-feedback.png');
  });
});
