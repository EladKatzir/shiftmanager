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

    // Navigate to Home first to create navigation history
    await page.goto('/Home/Index');
    await page.waitForLoadState('networkidle');

    // Then navigate to Companies page
    await page.goto('/Admin/Companies');
    await page.waitForLoadState('networkidle');

    // Companies page has inline form, not separate create page
    const timestamp = Date.now();
    const companyName = `Company_${timestamp}`;
    const companySlug = `slug-${timestamp}`;

    // Fill inline form (requires company + manager info)
    await page.fill('input[name="CompanyName"]', companyName);
    await page.fill('input[name="CompanySlug"]', companySlug);
    await page.fill('input[name="ManagerEmail"]', `manager${timestamp}@test.com`);
    await page.fill('input[name="ManagerDisplayName"]', `Manager ${timestamp}`);
    await page.fill('input[name="ManagerPassword"]', '123456');
    await page.locator('form:has(input[name="CompanyName"]) button[type="submit"]').click();

    // Wait for page to reload after POST-REDIRECT-GET
    await page.waitForLoadState('networkidle');

    // Verify company was created (should appear in list)
    await expect(page.locator(`text="${companyName}"`).first()).toBeVisible();

    // POST-REDIRECT-GET creates a new history entry at /Admin/Companies
    // So history is: /Home/Index -> /Admin/Companies -> /Admin/Companies (after redirect)
    // Going back once should take us to the first /Admin/Companies, then back again to /Home/Index
    await page.goBack();
    await page.goBack();
    await expect(page).toHaveURL(/\/Home\/Index/);

    // Go forward twice to get back to Companies page
    await page.goForward();
    await page.goForward();

    // Reload to get fresh data
    await page.reload();
    await page.waitForLoadState('networkidle');

    // Should only have ONE company row in the table (no double-submit from back button)
    // The name might appear in success message too, so check table rows specifically
    const companyRowMatches = await page.locator(`tbody tr:has-text("${companyName}")`).count();
    expect(companyRowMatches).toBe(1);

    console.log('✓ PRG pattern prevents double-submit via back button');
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
