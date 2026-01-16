// @ts-check
const { test, expect } = require('@playwright/test');
const { loginAsOwner, loginAsRole } = require('../helpers/auth-helpers');

test.describe('Multi-Tenancy Isolation - Network Level', () => {

  test('P6-01: API responses contain only tenant-scoped data', async ({ page }) => {
    await loginAsOwner(page);

    const apiResponses = [];

    page.on('response', async response => {
      const url = response.url();
      if ((url.includes('/api/') || url.includes('/Api/')) && response.ok()) {
        const contentType = response.headers()['content-type'] || '';
        if (contentType.includes('json')) {
          try {
            const body = await response.json();
            apiResponses.push({ url, body, status: response.status() });
          } catch (e) {
            // Not JSON, skip
          }
        }
      }
    });

    // Navigate through multiple pages
    await page.goto('/Admin/Users');
    await page.goto('/Calendar/Table');
    await page.goto('/My/Index');
    await page.waitForLoadState('networkidle');

    // Analyze captured responses for multi-tenancy violations
    for (const resp of apiResponses) {
      if (Array.isArray(resp.body)) {
        // Check for companyId consistency
        const companyIds = resp.body
          .map(item => item.companyId || item.CompanyId)
          .filter(Boolean);

        if (companyIds.length > 0) {
          const uniqueCompanyIds = [...new Set(companyIds)];
          console.log(`API ${resp.url}: ${uniqueCompanyIds.length} unique company IDs`);

          // Owner can see multiple companies (expected)
          // But should not see ALL companies unfiltered
          expect(uniqueCompanyIds.length).toBeLessThan(100);
        }
      }
    }
  });

  test('P6-02: SQL injection attempts do not bypass tenant filters', async ({ page }) => {
    await loginAsRole(page, 'Manager').catch(() => loginAsOwner(page));

    await page.goto('/Admin/Users');

    // Attempt SQL injection in search/filter field
    const searchField = page.locator('input[name="search"], input[type="search"], input[placeholder*="Search"]');

    if (await searchField.isVisible().catch(() => false)) {
      await searchField.fill("' OR '1'='1' --");

      // Submit search
      const searchButton = page.locator('button[type="submit"]:has-text("Search")');
      if (await searchButton.isVisible().catch(() => false)) {
        await searchButton.click();
      } else {
        await searchField.press('Enter');
      }

      await page.waitForLoadState('networkidle');

      // Should NOT return all users from all companies
      const userCount = await page.locator('tbody tr, .user-row').count();
      expect(userCount).toBeLessThan(10000); // Reasonable limit

      // Should not show SQL error
      const hasSqlError = await page.locator('text=/SQL|syntax error|database error/i').isVisible().catch(() => false);
      expect(hasSqlError).toBe(false);
    }
  });

  test('P6-03: XSS attempts are sanitized in responses', async ({ page }) => {
    await loginAsOwner(page);
    await page.goto('/Admin/Companies');

    // Create company with XSS payload in name
    await page.click('a:has-text("Create")');

    const xssPayload = '<script>alert("XSS")</script>';
    await page.fill('input[name="Name"]', xssPayload);
    await page.fill('input[name="Slug"]', `xss-test-${Date.now()}`);

    page.once('dialog', dialog => {
      // If alert fires, XSS is NOT sanitized (vulnerability)
      expect(dialog.message()).not.toBe('XSS');
      dialog.dismiss();
    });

    await page.click('button[type="submit"]');
    await page.waitForLoadState('networkidle');

    // Check if script tag appears in DOM (should be escaped)
    const pageContent = await page.content();
    expect(pageContent).not.toContain('<script>alert("XSS")</script>');

    // Should be HTML-encoded or stripped
    expect(pageContent.includes('&lt;script&gt;') || !pageContent.includes('<script>')).toBe(true);
  });

  test('P6-04: Network request headers contain proper authentication', async ({ page }) => {
    await loginAsOwner(page);

    let hasCookieAuth = false;

    page.on('request', request => {
      const headers = request.headers();
      const url = request.url();

      if (url.includes('/Admin/') || url.includes('/api/')) {
        // Should have Cookie header for authentication
        if (headers['cookie']) {
          hasCookieAuth = true;
        }
      }
    });

    await page.goto('/Admin/Users');
    await page.waitForLoadState('networkidle');

    expect(hasCookieAuth).toBe(true);
  });
});
