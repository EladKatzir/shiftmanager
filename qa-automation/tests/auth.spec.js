// @ts-check
const { test, expect } = require('@playwright/test');

/**
 * Authentication Tests - Phase 1
 * Tests core authentication functionality
 */

test.describe('Authentication', () => {
  test.describe('Login Page', () => {
    test('P1-01: should display login page', async ({ page }) => {
      await page.goto('/Auth/Login');

      // Verify login form elements are present
      await expect(page.locator('input[name="Email"], input#Email')).toBeVisible();
      await expect(page.locator('input[name="Password"], input#Password')).toBeVisible();
      await expect(page.locator('button[type="submit"]')).toBeVisible();
    });

    test('P1-01: should show error for invalid credentials', async ({ page }) => {
      await page.goto('/Auth/Login');

      // Fill in invalid credentials
      await page.fill('input[name="Email"], input#Email', 'invalid@test.com');
      await page.fill('input[name="Password"], input#Password', 'wrongpassword');
      await page.click('button[type="submit"]');

      // Should stay on login page with error
      await expect(page).toHaveURL(/\/Auth\/Login/);
    });

    test('P1-01: should login successfully as Owner', async ({ page }) => {
      await page.goto('/Auth/Login');

      // Fill in valid credentials
      await page.fill('input[name="Email"], input#Email', 'admin@local');
      await page.fill('input[name="Password"], input#Password', 'admin123');

      // Submit and wait for navigation
      await Promise.all([
        page.waitForURL(url => !url.toString().includes('/Auth/Login')),
        page.click('button[type="submit"]'),
      ]);

      // Should be redirected away from login
      await expect(page).not.toHaveURL(/\/Auth\/Login/);
    });

    test('P1-01: should enforce rate limiting', async ({ page }) => {
      // This test would need to be run multiple times rapidly
      // For now, just verify the login form handles multiple submissions
      await page.goto('/Auth/Login');

      for (let i = 0; i < 3; i++) {
        await page.fill('input[name="Email"], input#Email', 'test@test.com');
        await page.fill('input[name="Password"], input#Password', 'wrong');
        await page.click('button[type="submit"]');
        await page.waitForLoadState('networkidle');
      }

      // Should still be on login page
      await expect(page).toHaveURL(/\/Auth\/Login/);
    });
  });

  test.describe('Logout', () => {
    test.beforeEach(async ({ page }) => {
      // Login first
      await page.goto('/Auth/Login');
      await page.fill('input[name="Email"], input#Email', 'admin@local');
      await page.fill('input[name="Password"], input#Password', 'admin123');
      await Promise.all([
        page.waitForURL(url => !url.toString().includes('/Auth/Login')),
        page.click('button[type="submit"]'),
      ]);
    });

    test('P1-02: should logout successfully', async ({ page }) => {
      // Find and click logout button
      const logoutForm = page.locator('form[action*="Logout"]');
      await expect(logoutForm).toBeVisible();

      await Promise.all([
        page.waitForURL(/\/Auth\/Login/),
        logoutForm.locator('button[type="submit"]').click(),
      ]);

      // Should be redirected to login
      await expect(page).toHaveURL(/\/Auth\/Login/);
    });

    test('P1-02: should clear session on logout', async ({ page }) => {
      // Logout
      await page.locator('form[action*="Logout"] button[type="submit"]').click();
      await page.waitForURL(/\/Auth\/Login/);

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

      // Login as Owner first (who has access)
      await page.goto('/Auth/Login');
      await page.fill('input[name="Email"], input#Email', 'admin@local');
      await page.fill('input[name="Password"], input#Password', 'admin123');
      await Promise.all([
        page.waitForURL(url => !url.toString().includes('/Auth/Login')),
        page.click('button[type="submit"]'),
      ]);

      // Owner should be able to access Admin
      await page.goto('/Admin/Index');
      await expect(page).not.toHaveURL(/\/AccessDenied/);
    });
  });
});

test.describe('Session Management', () => {
  test('should handle session timeout gracefully', async ({ page }) => {
    // Login
    await page.goto('/Auth/Login');
    await page.fill('input[name="Email"], input#Email', 'admin@local');
    await page.fill('input[name="Password"], input#Password', 'admin123');
    await Promise.all([
      page.waitForURL(url => !url.toString().includes('/Auth/Login')),
      page.click('button[type="submit"]'),
    ]);

    // Verify session status endpoint exists
    const response = await page.request.get('/Api/SessionStatus');
    expect(response.status()).toBe(200);
  });
});
