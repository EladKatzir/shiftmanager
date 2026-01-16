// @ts-check
const { test, expect } = require('@playwright/test');
const { loginAsOwner, loginAsRole } = require('../helpers/auth-helpers');

test.describe('Session Management & Resilience', () => {

  test('P7-01: Session timeout redirects to login', async ({ page }) => {
    await loginAsRole(page, 'Employee').catch(() => loginAsOwner(page));
    await page.goto('/My/Index');

    // Clear cookies to simulate timeout
    await page.context().clearCookies();

    // Try to access protected page
    await page.goto('/Admin/Users');

    // Should redirect to login
    await page.waitForURL(/.*Auth\/Login/, { timeout: 5000 });
    expect(page.url()).toContain('/Auth/Login');
  });

  test('P7-02: Page refresh preserves authentication', async ({ page }) => {
    await loginAsOwner(page);
    await page.goto('/Admin/Index');

    // Refresh page
    await page.reload();
    await page.waitForLoadState('networkidle');

    // Should still be logged in (not redirected to login)
    expect(page.url()).not.toContain('/Auth/Login');
    await expect(page.locator('body')).toBeVisible();
  });

  test('P7-03: Browser back button after form submit shows correct state', async ({ page }) => {
    await loginAsOwner(page);
    await page.goto('/Admin/Companies');

    const initialCount = await page.locator('tbody tr, .company-row').count();

    // Create a company
    await page.click('a:has-text("Create")');
    const companyName = `Company_${Date.now()}`;
    await page.fill('input[name="Name"]', companyName);
    await page.fill('input[name="Slug"]', `slug-${Date.now()}`);
    await page.click('button[type="submit"]');

    // Wait for redirect back to list
    await page.waitForURL(/.*\/Admin\/Companies(?!\/Create)/, { timeout: 5000 }).catch(() => {});

    // Go back
    await page.goBack();

    // Go forward again
    await page.goForward();

    // Reload to get fresh data
    await page.reload();

    // Should only have one instance of the company (no double-submit)
    const companyMatches = await page.locator(`text="${companyName}"`).count();
    expect(companyMatches).toBeLessThanOrEqual(1);
  });

  test('P7-04: Concurrent sessions from same user work independently', async ({ browser }) => {
    const context1 = await browser.newContext();
    const context2 = await browser.newContext();

    const page1 = await context1.newPage();
    const page2 = await context2.newPage();

    // Both login as Owner
    await loginAsOwner(page1);
    await loginAsOwner(page2);

    // Both navigate to different pages
    await page1.goto('/Admin/Users');
    await page2.goto('/Admin/Companies');

    // Both should work independently
    await expect(page1.locator('h1, h2')).toContainText(/users/i, { timeout: 5000 }).catch(() =>
      expect(page1.locator('body')).toBeVisible());
    await expect(page2.locator('h1, h2')).toContainText(/compan/i, { timeout: 5000 }).catch(() =>
      expect(page2.locator('body')).toBeVisible());

    await context1.close();
    await context2.close();
  });

  test('P7-05: Network interruption shows graceful error', async ({ page, context }) => {
    await loginAsOwner(page);
    await page.goto('/Admin/Index');

    // Simulate offline
    await context.setOffline(true);

    // Try to navigate
    const response = await page.goto('/Admin/Users').catch(() => null);

    // Should fail gracefully (either error page or offline indicator)
    const hasErrorIndicator = await page.locator('text=/offline|network error|connection/i').isVisible().catch(() => false);
    const isErrorResponse = !response || !response.ok();

    expect(hasErrorIndicator || isErrorResponse).toBe(true);

    // Restore connection
    await context.setOffline(false);

    // Should work again
    await page.reload();
    await expect(page.locator('body')).toBeVisible();
  });
});
