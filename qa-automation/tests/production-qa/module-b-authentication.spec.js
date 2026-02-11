// @ts-check
const { test, expect } = require('@playwright/test');
const {
  login,
  loginAsOwner,
  logout,
  saveEvidence,
  navigateTo,
  assertPageContains,
  BASE_URL,
} = require('../../helpers/production-qa-helpers');

const EVIDENCE = '02-auth';

/**
 * Module B: Authentication
 *
 * Covers login success/failure, lockout, logout, session management,
 * ADFS rendering, change-password page, anti-forgery tokens, and
 * language/RTL toggling.
 *
 * HARDENING NOTES
 *  - Every test makes at least one meaningful assertion.
 *  - No `.catch(() => false)` to silently skip test logic.
 *  - No `if (await el.isVisible()) { ... }` conditional skips.
 *  - Post-action state is always verified.
 */

test.describe('Module B: Authentication', () => {

  // -----------------------------------------------------------------------
  // B-01  Owner login success
  // -----------------------------------------------------------------------
  test('B-01: Owner login success', async ({ page }) => {
    await login(page, 'admin@local', 'admin123');

    // STRICT: must have left the login page
    await expect(page).not.toHaveURL(/\/Auth\/Login/);

    // STRICT: Owner nav link must be visible
    const ownerNav = page.locator('a[href*="/Owner"]');
    await expect(ownerNav.first()).toBeVisible({ timeout: 10000 });

    await saveEvidence(page, EVIDENCE, 'B-01-owner-login.png');
  });

  // -----------------------------------------------------------------------
  // B-02  Wrong password shows error alert
  // -----------------------------------------------------------------------
  test('B-02: Wrong password shows error', async ({ page }) => {
    await login(page, 'admin@local', 'wrongpassword', { expectSuccess: false });

    // STRICT: stayed on login page
    await expect(page).toHaveURL(/\/Auth\/Login/);

    // STRICT: an error alert must be visible
    const errorAlert = page.locator('.auth-alert--error, .alert-danger, .validation-summary-errors');
    await expect(errorAlert.first()).toBeVisible({ timeout: 5000 });

    await saveEvidence(page, EVIDENCE, 'B-02-wrong-password.png');
  });

  // -----------------------------------------------------------------------
  // B-03  Non-existent email shows error
  // -----------------------------------------------------------------------
  test('B-03: Non-existent email shows error', async ({ page }) => {
    await login(page, 'nonexistent@test.com', 'anypassword', { expectSuccess: false });

    // STRICT: stayed on login page
    await expect(page).toHaveURL(/\/Auth\/Login/);

    // STRICT: error alert visible
    const errorAlert = page.locator('.auth-alert--error, .alert-danger, .validation-summary-errors');
    await expect(errorAlert.first()).toBeVisible({ timeout: 5000 });

    await saveEvidence(page, EVIDENCE, 'B-03-nonexistent-email.png');
  });

  // -----------------------------------------------------------------------
  // B-04  Empty field submission triggers HTML5 validation
  // -----------------------------------------------------------------------
  test('B-04: Empty fields submit shows validation errors', async ({ page }) => {
    await page.goto(`${BASE_URL}/Auth/Login`);
    await page.waitForLoadState('networkidle');

    // STRICT: the login form and submit button must be present
    const emailInput = page.locator('input[name="Email"]');
    const submitBtn = page.locator('form.auth-form button[type="submit"]');
    await expect(emailInput).toBeVisible({ timeout: 10000 });
    await expect(submitBtn).toBeVisible({ timeout: 5000 });

    // Click submit without filling anything
    await submitBtn.click();
    await page.waitForTimeout(300);

    // STRICT: HTML5 required-field validation should mark the email invalid
    const isInvalid = await emailInput.evaluate(
      /** @param {HTMLInputElement} el */ (el) => !el.checkValidity()
    );
    expect(isInvalid).toBe(true);

    // STRICT: we must still be on the login page (form did not submit)
    await expect(page).toHaveURL(/\/Auth\/Login/);

    await saveEvidence(page, EVIDENCE, 'B-04-empty-fields.png');
  });

  // -----------------------------------------------------------------------
  // B-05  Account lockout after repeated failures
  // -----------------------------------------------------------------------
  test('B-05: Account lockout (5+ failed attempts)', async ({ page }) => {
    // This test depends on "locked@test" existing from Setup-04.
    // Attempt 6 wrong-password logins to trigger lockout.
    for (let i = 0; i < 6; i++) {
      await login(page, 'locked@test', 'wrongpassword', { expectSuccess: false });
      await page.waitForTimeout(300);
    }

    // Now try with the correct password — should still fail due to lockout
    await login(page, 'locked@test', 'Test1234!', { expectSuccess: false });

    // STRICT: we must remain on the login page
    await expect(page).toHaveURL(/\/Auth\/Login/);

    // STRICT: a lockout or error message must be visible
    const lockoutOrError = page.locator('#lockoutMessage, .auth-alert--error, .auth-alert--warning');
    await expect(lockoutOrError.first()).toBeVisible({ timeout: 5000 });

    await saveEvidence(page, EVIDENCE, 'B-05-account-lockout.png');
  });

  // -----------------------------------------------------------------------
  // B-06  Logout clears session and protects routes
  // -----------------------------------------------------------------------
  test('B-06: Logout clears session', async ({ page }) => {
    await loginAsOwner(page);
    await logout(page);

    // STRICT: must be on login page after logout
    await expect(page).toHaveURL(/\/Auth\/Login/);

    // STRICT: accessing a protected page should redirect back to login
    await page.goto(`${BASE_URL}/Admin/Users`);
    await page.waitForLoadState('networkidle');
    await expect(page).toHaveURL(/\/Auth\/Login/);

    await saveEvidence(page, EVIDENCE, 'B-06-logout-session.png');
  });

  // -----------------------------------------------------------------------
  // B-07  Session status endpoint returns 200 when authenticated
  // -----------------------------------------------------------------------
  test('B-07: Session status endpoint works', async ({ page }) => {
    await loginAsOwner(page);

    // STRICT: the SessionStatus API must return 200
    const response = await page.request.get(`${BASE_URL}/Api/SessionStatus`);
    expect(response.status()).toBe(200);

    // STRICT: response must contain JSON body (not empty)
    const body = await response.text();
    expect(body.length).toBeGreaterThan(0);

    await saveEvidence(page, EVIDENCE, 'B-07-session-status.png');
  });

  // -----------------------------------------------------------------------
  // B-08  ADFS / Griffin button visible on login page
  // -----------------------------------------------------------------------
  test('B-08: ADFS button exists on login page', async ({ page }) => {
    await page.goto(`${BASE_URL}/Auth/Login`);
    await page.waitForLoadState('networkidle');

    // STRICT: the ADFS login button must be visible
    const adfsBtn = page.locator('.auth-adfs__btn');
    await expect(adfsBtn).toBeVisible({ timeout: 5000 });

    // STRICT: the ADFS section container must exist
    const adfsSection = page.locator('.auth-adfs');
    await expect(adfsSection).toBeVisible({ timeout: 5000 });

    await saveEvidence(page, EVIDENCE, 'B-08-griffin-adfs.png');
  });

  // -----------------------------------------------------------------------
  // B-09  Change-password / ForgotPassword page renders
  // -----------------------------------------------------------------------
  test('B-09: ForgotPassword page has password inputs', async ({ page }) => {
    // The MustChangePassword flow redirects to /Auth/ForgotPassword
    await page.goto(`${BASE_URL}/Auth/ForgotPassword`);
    await page.waitForLoadState('networkidle');

    // STRICT: the page must load (not 500)
    // STRICT: at least one password-related input or section must be visible
    const heading = page.locator('main h1, main h2, main h3').first();
    await expect(heading).toBeVisible({ timeout: 10000 });

    // The page has an email input for requesting a reset, or password fields
    // for changing the password — either way, an input must be visible
    const formInput = page.locator('input[type="email"], input[type="password"], input[name="Email"]').first();
    await expect(formInput).toBeVisible({ timeout: 5000 });

    await saveEvidence(page, EVIDENCE, 'B-09-forgot-password.png');
  });

  // -----------------------------------------------------------------------
  // B-10  Anti-forgery token is present and non-empty
  // -----------------------------------------------------------------------
  test('B-10: Anti-forgery token present', async ({ page }) => {
    await page.goto(`${BASE_URL}/Auth/Login`);
    await page.waitForLoadState('networkidle');

    // STRICT: the hidden CSRF token must be in the DOM
    const token = page.locator('input[name="__RequestVerificationToken"]');
    await expect(token.first()).toBeAttached({ timeout: 5000 });

    // STRICT: its value must be a non-trivial string
    const value = await token.first().getAttribute('value');
    expect(value).toBeTruthy();
    expect(value.length).toBeGreaterThan(10);

    await saveEvidence(page, EVIDENCE, 'B-10-antiforgery.png');
  });

  // -----------------------------------------------------------------------
  // B-11  Language toggle switches between English and Hebrew
  // -----------------------------------------------------------------------
  test('B-11: Language toggle on login page', async ({ page }) => {
    await page.goto(`${BASE_URL}/Auth/Login`);
    await page.waitForLoadState('networkidle');

    // STRICT: the language toggle button must be visible
    const langToggle = page.locator('#authLanguageToggle');
    await expect(langToggle).toBeVisible({ timeout: 5000 });

    // Capture the current lang attribute before toggle
    const langBefore = await page.locator('html').getAttribute('lang');
    expect(langBefore).toBeTruthy();

    // Click the language toggle — this sets a cookie and reloads
    await langToggle.click();
    await page.waitForLoadState('networkidle');

    // STRICT: the html lang attribute must have changed
    const langAfter = await page.locator('html').getAttribute('lang');
    expect(langAfter).toBeTruthy();
    expect(langAfter).not.toBe(langBefore);

    await saveEvidence(page, EVIDENCE, 'B-11-language-toggle.png');
  });

  // -----------------------------------------------------------------------
  // B-12  RTL layout on Hebrew login page
  // -----------------------------------------------------------------------
  test('B-12: RTL layout on Hebrew login', async ({ page }) => {
    // Set the culture cookie to Hebrew before navigating
    await page.context().addCookies([{
      name: '.AspNetCore.Culture',
      value: 'c=he-IL|uic=he-IL',
      url: BASE_URL,
      path: '/',
    }]);

    await page.goto(`${BASE_URL}/Auth/Login`);
    await page.waitForLoadState('networkidle');

    // STRICT: the html dir attribute must be "rtl"
    const htmlDir = await page.locator('html').getAttribute('dir');
    expect(htmlDir).toBe('rtl');

    // STRICT: the html lang attribute should start with "he"
    const htmlLang = await page.locator('html').getAttribute('lang');
    expect(htmlLang).toBeTruthy();
    expect(htmlLang.startsWith('he')).toBe(true);

    // STRICT: verify computed direction on body as a double-check
    const computedDir = await page.evaluate(
      () => getComputedStyle(document.body).direction
    );
    expect(computedDir).toBe('rtl');

    await saveEvidence(page, EVIDENCE, 'B-12-rtl-layout.png');

    // Clean up: reset to English so subsequent tests are not affected
    await page.context().addCookies([{
      name: '.AspNetCore.Culture',
      value: 'c=en-US|uic=en-US',
      url: BASE_URL,
      path: '/',
    }]);
  });
});
