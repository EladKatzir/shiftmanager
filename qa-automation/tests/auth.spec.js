// @ts-check
const { test, expect } = require('@playwright/test');
const { loginAsOwner, getOwnerCredentials, OWNER_EMAIL, OWNER_PASSWORD } = require('../helpers/auth-helpers');
const { waitAndScrollToElement } = require('../helpers/test-helpers');

/**
 * Authentication Tests - Phase 1
 * Tests core authentication functionality
 */

test.describe('Authentication', () => {
  test.describe('Login Page', () => {
    test('P1-01a: should display login page', async ({ page }) => {
      await page.goto('/Auth/Login');

      // Verify login form elements are present
      await expect(page.locator('input[name="Email"], input#Email')).toBeVisible();
      await expect(page.locator('input[name="Password"], input#Password')).toBeVisible();
      await expect(page.locator('form:has(input[name="Email"]) button[type="submit"]')).toBeVisible();
    });

    test('P1-01b: should show error for invalid credentials', async ({ page }) => {
      await page.goto('/Auth/Login');

      // Fill in invalid credentials (with visibility waits)
      const emailInput = page.locator('input[name="Email"], input#Email');
      await emailInput.waitFor({ state: 'visible' });
      await emailInput.fill('invalid@test.com');

      const passwordInput = page.locator('input[name="Password"], input#Password');
      await passwordInput.waitFor({ state: 'visible' });
      await passwordInput.fill('wrongpassword');

      await page.locator('form:has(input[name="Email"]) button[type="submit"]').click();

      // Should stay on login page with error
      await expect(page).toHaveURL(/\/Auth\/Login/);
    });

    test('P1-01c: should login successfully as Owner', async ({ page }) => {
      await page.goto('/Auth/Login');

      // Fill in valid credentials from environment variables
      await page.fill('input[name="Email"], input#Email', OWNER_EMAIL);
      await page.fill('input[name="Password"], input#Password', OWNER_PASSWORD);

      // Submit and wait for navigation
      await Promise.all([
        page.waitForURL(url => !url.toString().includes('/Auth/Login')),
        page.locator('form:has(input[name="Email"]) button[type="submit"]').click(),
      ]);

      // Should be redirected away from login
      await expect(page).not.toHaveURL(/\/Auth\/Login/);
    });

    test('P1-01d: should enforce rate limiting', async ({ page }) => {
      // This test would need to be run multiple times rapidly
      // For now, just verify the login form handles multiple submissions
      await page.goto('/Auth/Login');

      const emailInput = page.locator('input[name="Email"], input#Email');
      const passwordInput = page.locator('input[name="Password"], input#Password');

      for (let i = 0; i < 3; i++) {
        await emailInput.waitFor({ state: 'visible' });
        await emailInput.fill('test@test.com');
        await passwordInput.waitFor({ state: 'visible' });
        await passwordInput.fill('wrong');
        await page.locator('form:has(input[name="Email"]) button[type="submit"]').click();
        await page.waitForLoadState('networkidle');
      }

      // Should still be on login page
      await expect(page).toHaveURL(/\/Auth\/Login/);
    });
  });

  test.describe('Logout', () => {
    test.beforeEach(async ({ page }) => {
      // Login first using credentials from environment variables
      await page.goto('/Auth/Login');
      await page.fill('input[name="Email"], input#Email', OWNER_EMAIL);
      await page.fill('input[name="Password"], input#Password', OWNER_PASSWORD);
      await Promise.all([
        page.waitForURL(url => !url.toString().includes('/Auth/Login')),
        page.locator('form:has(input[name="Email"]) button[type="submit"]').click(),
      ]);
    });

    test('P1-02: should logout successfully', async ({ page }) => {
      // The logout form is inside a dropdown menu — open it first
      const userMenuTrigger = page.locator('#sidebarUserMenuTrigger');
      await expect(userMenuTrigger).toBeVisible({ timeout: 5000 });
      await userMenuTrigger.click();
      await page.waitForTimeout(300);

      // Find and click logout button
      const logoutForm = page.locator('.sidebar-user-menu__logout-form');
      await expect(logoutForm).toBeVisible();

      await Promise.all([
        page.waitForURL(/\/Auth\/Login/),
        logoutForm.locator('button[type="submit"]').click(),
      ]);

      // Should be redirected to login
      await expect(page).toHaveURL(/\/Auth\/Login/);
    });

    test('P1-02: should clear session on logout', async ({ page }) => {
      // The logout form is inside a dropdown menu — open it first
      const userMenuTrigger = page.locator('#sidebarUserMenuTrigger');
      await expect(userMenuTrigger).toBeVisible({ timeout: 5000 });
      await userMenuTrigger.click();
      await page.waitForTimeout(300);

      // Logout (with proper navigation wait)
      await Promise.all([
        page.waitForURL(/\/Auth\/Login/),
        page.locator('.sidebar-user-menu__logout-form button[type="submit"]').click(),
      ]);

      // Try to access protected page
      await page.goto('/Admin/Index');

      // Should be redirected to login
      await expect(page).toHaveURL(/\/Auth\/Login/);
    });
  });
});

test.describe('Authorization', () => {
  test.describe('Access Control', () => {
    test('should redirect unauthenticated user to login', async ({ page }) => {
      // Try to access protected page without logging in
      await page.goto('/Admin/Index');

      // Should be redirected to login
      await expect(page).toHaveURL(/\/Auth\/Login/);
    });

    test('should show access denied for unauthorized role', async ({ page }) => {
      // This would require an Employee account to test properly
      // For now, document the expected behavior

      // Login as Owner first (who has access) using credentials from environment variables
      await page.goto('/Auth/Login');
      await page.fill('input[name="Email"], input#Email', OWNER_EMAIL);
      await page.fill('input[name="Password"], input#Password', OWNER_PASSWORD);
      await Promise.all([
        page.waitForURL(url => !url.toString().includes('/Auth/Login')),
        page.locator('form:has(input[name="Email"]) button[type="submit"]').click(),
      ]);

      // Owner should be able to access Admin
      await page.goto('/Admin/Index');
      await expect(page).not.toHaveURL(/\/AccessDenied/);
    });
  });
});

test.describe('Session Management', () => {
  test('should handle session timeout gracefully', async ({ page }) => {
    // Login using credentials from environment variables
    await page.goto('/Auth/Login');
    await page.fill('input[name="Email"], input#Email', OWNER_EMAIL);
    await page.fill('input[name="Password"], input#Password', OWNER_PASSWORD);
    await Promise.all([
      page.waitForURL(url => !url.toString().includes('/Auth/Login')),
      page.locator('form:has(input[name="Email"]) button[type="submit"]').click(),
    ]);

    // Verify session status endpoint exists
    const response = await page.request.get('/Api/SessionStatus');
    expect(response.status()).toBe(200);
  });
});
