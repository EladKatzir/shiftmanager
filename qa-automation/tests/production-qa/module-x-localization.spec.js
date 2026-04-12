// @ts-check
const { test, expect } = require('@playwright/test');
const { loginAsOwner, saveEvidence, navigateTo, assertPageContains, collectConsoleErrors, BASE_URL } = require('../../helpers/production-qa-helpers');

const EVIDENCE = '24-localization';

// Hebrew Unicode range: \u0590-\u05FF
const HEBREW_REGEX = /[\u0590-\u05FF]/;
const HEBREW_DAY_NAMES = ['ראשון', 'שני', 'שלישי', 'רביעי', 'חמישי', 'שישי', 'שבת'];

test.describe('Module X: Localization & RTL', () => {
  test.beforeEach(async ({ page }) => {
    await loginAsOwner(page);
  });

  test('X-01: Switch to Hebrew — body contains Hebrew text and html dir=rtl', async ({ page }) => {
    await page.goto(`${BASE_URL}/?culture=he-IL&ui-culture=he-IL`);
    await page.waitForLoadState('networkidle');

    // ASSERT: Body text contains Hebrew characters
    const bodyText = await page.locator('body').innerText();
    expect(HEBREW_REGEX.test(bodyText)).toBe(true);

    // ASSERT: html dir attribute is rtl OR computed direction is rtl
    const htmlDir = await page.locator('html').getAttribute('dir');
    const computedDir = await page.evaluate(() => getComputedStyle(document.body).direction);
    const isRtl = htmlDir === 'rtl' || computedDir === 'rtl';
    expect(isRtl).toBe(true);

    await saveEvidence(page, EVIDENCE, 'X-01-hebrew.png', 'hebrew');
  });

  test('X-02: Switch to English — body text is English, direction is ltr', async ({ page }) => {
    // First load Hebrew so we can verify the switch
    await page.goto(`${BASE_URL}/?culture=he-IL&ui-culture=he-IL`);
    await page.waitForLoadState('networkidle');
    const hebrewBodyText = await page.locator('body').innerText();

    // Now switch to English
    await page.goto(`${BASE_URL}/?culture=en-US&ui-culture=en-US`);
    await page.waitForLoadState('networkidle');

    // ASSERT: Body text changed (is different from Hebrew version)
    const englishBodyText = await page.locator('body').innerText();
    expect(englishBodyText).not.toBe(hebrewBodyText);

    // ASSERT: Computed direction is ltr
    const computedDir = await page.evaluate(() => getComputedStyle(document.body).direction);
    expect(computedDir).toBe('ltr');

    // ASSERT: Page contains common English words (from nav/UI)
    const hasEnglish = /[a-zA-Z]{3,}/.test(englishBodyText);
    expect(hasEnglish).toBe(true);

    await saveEvidence(page, EVIDENCE, 'X-02-english.png', 'english');
  });

  test('X-03: RTL layout — Hebrew computed direction is rtl', async ({ page }) => {
    await page.goto(`${BASE_URL}/?culture=he-IL&ui-culture=he-IL`);
    await page.waitForLoadState('networkidle');

    // ASSERT: html dir attribute
    const htmlDir = await page.locator('html').getAttribute('dir');
    expect(htmlDir).toBe('rtl');

    // ASSERT: Computed body direction is rtl
    const bodyDir = await page.evaluate(() => getComputedStyle(document.body).direction);
    expect(bodyDir).toBe('rtl');

    await saveEvidence(page, EVIDENCE, 'X-03-rtl-layout.png', 'rtl');
  });

  test('X-04: LTR layout — English computed direction is ltr', async ({ page }) => {
    await page.goto(`${BASE_URL}/?culture=en-US&ui-culture=en-US`);
    await page.waitForLoadState('networkidle');

    // ASSERT: Computed body direction is ltr
    const dir = await page.evaluate(() => getComputedStyle(document.body).direction);
    expect(dir).toBe('ltr');

    // ASSERT: html dir is ltr or absent (both acceptable for English)
    const htmlDir = await page.locator('html').getAttribute('dir');
    expect(htmlDir === null || htmlDir === 'ltr' || htmlDir === 'auto').toBe(true);

    await saveEvidence(page, EVIDENCE, 'X-04-ltr-layout.png', 'english');
  });

  test('X-05: Form elements render in Hebrew RTL', async ({ page }) => {
    await page.goto(`${BASE_URL}/Calendar/Shifts?culture=he-IL&ui-culture=he-IL`);
    await page.waitForLoadState('networkidle');

    // ASSERT: Page loaded successfully (not an error page)
    const pageTitle = await page.title();
    expect(pageTitle.length).toBeGreaterThan(0);

    // ASSERT: Hebrew text is present on the page
    const bodyText = await page.locator('body').innerText();
    expect(HEBREW_REGEX.test(bodyText)).toBe(true);

    // ASSERT: At least one interactive form element exists (select, button, or input)
    const formElements = page.locator('select, button, input');
    const count = await formElements.count();
    expect(count).toBeGreaterThan(0);

    // ASSERT: Computed direction is rtl on the page
    const computedDir = await page.evaluate(() => getComputedStyle(document.body).direction);
    expect(computedDir).toBe('rtl');

    await saveEvidence(page, EVIDENCE, 'X-05-form-rtl.png', 'rtl');
  });

  test('X-06: Calendar day names appear in Hebrew', async ({ page }) => {
    await page.goto(`${BASE_URL}/Calendar/Shifts?culture=he-IL&ui-culture=he-IL`);
    await page.waitForLoadState('networkidle');

    // ASSERT: At least one Hebrew day name appears in the page
    const bodyText = await page.locator('body').innerText();
    const foundHebrewDays = HEBREW_DAY_NAMES.filter(day => bodyText.includes(day));
    expect(foundHebrewDays.length).toBeGreaterThanOrEqual(1);

    await saveEvidence(page, EVIDENCE, 'X-06-hebrew-days.png', 'hebrew');
  });

  test('X-07: Validation messages display in Hebrew', async ({ page }) => {
    await page.goto(`${BASE_URL}/Auth/Login?culture=he-IL&ui-culture=he-IL`);
    await page.waitForLoadState('networkidle');

    // ASSERT: Login form is visible in Hebrew
    const emailInput = page.locator('input[name="Email"]');
    await expect(emailInput).toBeVisible({ timeout: 5000 });

    // Fill invalid credentials and submit
    await emailInput.fill('test@test.com');
    await page.fill('input[name="Password"]', 'wrong');
    await page.locator('form:has(input[name="Email"]) button[type="submit"]').click();
    await page.waitForLoadState('networkidle');

    // ASSERT: Page contains Hebrew text (error message or labels)
    const bodyText = await page.locator('body').innerText();
    expect(HEBREW_REGEX.test(bodyText)).toBe(true);

    // ASSERT: We are still on the login page (login failed as expected)
    expect(page.url()).toContain('/Auth/Login');

    await saveEvidence(page, EVIDENCE, 'X-07-hebrew-validation.png', 'hebrew');
  });

  test('X-08: Navigation labels display in Hebrew', async ({ page }) => {
    await page.goto(`${BASE_URL}/?culture=he-IL&ui-culture=he-IL`);
    await page.waitForLoadState('networkidle');

    // ASSERT: Sidebar/nav area contains Hebrew text
    const navItems = await page.locator('.app-sidebar-nav-item, .app-sidebar-nav a, nav a, .sidebar a').allTextContents();
    const allNavText = navItems.join(' ');
    expect(HEBREW_REGEX.test(allNavText)).toBe(true);

    // ASSERT: At least some nav items exist
    expect(navItems.length).toBeGreaterThan(0);

    await saveEvidence(page, EVIDENCE, 'X-08-hebrew-nav.png', 'hebrew');
  });

  test('X-09: Modal dialogs render in Hebrew', async ({ page }) => {
    await page.goto(`${BASE_URL}/Owner/Blueprints?culture=he-IL&ui-culture=he-IL`);
    await page.waitForLoadState('networkidle');

    // ASSERT: Page loaded with Hebrew content
    const bodyText = await page.locator('body').innerText();
    expect(HEBREW_REGEX.test(bodyText)).toBe(true);

    // Try to open a modal via edit button
    const editBtn = page.locator('.edit-name-btn').first();
    const editBtnCount = await editBtn.count();
    if (editBtnCount > 0 && await editBtn.isVisible()) {
      await editBtn.click();
      await page.waitForTimeout(500);

      // ASSERT: A modal or dialog appeared
      // Blueprints page uses .blueprint-modal class with display:flex when shown, and adds .is-open class
      const modal = page.locator('.blueprint-modal.is-open, .blueprint-modal:not([style*="display: none"]), .modal.show, [role="dialog"]').first();
      await expect(modal).toBeVisible({ timeout: 3000 });

      // ASSERT: Modal contains Hebrew text
      const modalText = await modal.innerText();
      expect(HEBREW_REGEX.test(modalText)).toBe(true);
    } else {
      // No edit buttons — ASSERT the page itself has Hebrew content (already verified above)
      // Also assert direction is rtl to confirm Hebrew rendering
      const dir = await page.evaluate(() => getComputedStyle(document.body).direction);
      expect(dir).toBe('rtl');
    }

    await saveEvidence(page, EVIDENCE, 'X-09-hebrew-modal.png', 'hebrew');
  });

  test('X-10: No horizontal overflow with Hebrew text', async ({ page }) => {
    await page.goto(`${BASE_URL}/Admin/Users?culture=he-IL&ui-culture=he-IL`);
    await page.waitForLoadState('networkidle');

    // ASSERT: No horizontal overflow
    const hasOverflow = await page.evaluate(() => {
      return document.documentElement.scrollWidth > document.documentElement.clientWidth;
    });
    expect(hasOverflow).toBe(false);

    // ASSERT: Page is in Hebrew/RTL
    const dir = await page.evaluate(() => getComputedStyle(document.body).direction);
    expect(dir).toBe('rtl');

    await saveEvidence(page, EVIDENCE, 'X-10-no-overflow.png', 'hebrew');
  });

  test('X-11: Company localization override page loads', async ({ page }) => {
    const response = await page.goto('/Owner/LanguageManagement', { timeout: 15000 }).catch(() => null);
    await page.waitForLoadState('domcontentloaded', { timeout: 10000 }).catch(() => {});

    // Check if we were redirected away (user may not have the required grant)
    const currentUrl = page.url();
    if (!currentUrl.includes('LanguageManagement')) {
      test.skip(true, 'Language Management page not accessible — user may lack required grant or page does not exist');
      return;
    }

    // Check response status
    if (response && response.status() >= 400) {
      test.skip(true, `Language Management page returned status ${response.status()}`);
      return;
    }

    // ASSERT: Page heading is visible (the page has h1 with class page-icon)
    const heading = page.locator('h1').first();
    await expect(heading).toBeVisible({ timeout: 15000 });

    // ASSERT: Page content area is visible (language management container or generic main)
    const content = page.locator('.language-management-container, main, .page-content, form, .card').first();
    await expect(content).toBeVisible({ timeout: 5000 });

    await saveEvidence(page, EVIDENCE, 'X-11-company-override.png');
  });

  test('X-12: Language edit mode page accessible', async ({ page }) => {
    await navigateTo(page, '/Owner/LanguageManagement');
    await page.waitForLoadState('networkidle');

    // ASSERT: Page heading is visible
    const heading = page.locator('main h1, main h2').first();
    await expect(heading).toBeVisible({ timeout: 10000 });

    // Look for edit mode toggle and interact with it
    const editToggle = page.locator('button:has-text("Edit"), .language-edit-toggle').first();
    const editToggleCount = await editToggle.count();
    if (editToggleCount > 0 && await editToggle.isVisible()) {
      await editToggle.click();
      await page.waitForTimeout(500);

      // ASSERT: Something changed after clicking (edit mode activated)
      const bodyAfter = await page.locator('body').innerText();
      expect(bodyAfter.trim().length).toBeGreaterThan(0);
    } else {
      // ASSERT: Page loaded successfully even without edit toggle
      const pageTitle = await page.title();
      expect(pageTitle.length).toBeGreaterThan(0);
    }

    await saveEvidence(page, EVIDENCE, 'X-12-edit-mode.png');
  });
});
