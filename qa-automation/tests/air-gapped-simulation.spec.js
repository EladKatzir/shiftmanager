// @ts-check
const { test, expect } = require('@playwright/test');
const { loginAsOwner } = require('../helpers/auth-helpers');

test.describe('Air-Gapped Environment Simulation', () => {

  test('P8-01: Block external requests and verify functionality', async ({ page, context }) => {
    // Block all non-localhost requests
    await context.route('**/*', route => {
      const url = route.request().url();
      const isLocal = url.startsWith('http://localhost') ||
                      url.startsWith('http://127.0.0.1') ||
                      url.startsWith('http://[::1]');

      if (isLocal) {
        route.continue();
      } else {
        console.log(`Blocked external request: ${url}`);
        route.abort();
      }
    });

    await loginAsOwner(page);

    // Test key pages load without external dependencies
    const pagesToTest = [
      '/Admin/Index',
      '/Calendar/Table',
      '/Admin/Users',
      '/My/Index'
    ];

    for (const path of pagesToTest) {
      await page.goto(`http://localhost:5000${path}`);
      await page.waitForLoadState('domcontentloaded');

      // Verify page loads (no infinite spinner)
      const bodyVisible = await page.locator('body').isVisible({ timeout: 5000 });
      expect(bodyVisible).toBe(true);

      // Check for stuck loading states
      await page.waitForTimeout(2000);
      const hasSpinner = await page.locator('.spinner:visible, .loading:visible').count();
      expect(hasSpinner).toBeLessThan(5); // Allow some spinners but not stuck

      console.log(`✓ ${path} loads in air-gapped mode`);
    }
  });

  test('P8-02: CSS loads from local paths only', async ({ page }) => {
    const externalRequests = [];

    page.on('request', request => {
      const url = request.url();
      const resourceType = request.resourceType();

      if (resourceType === 'stylesheet' && !url.includes('localhost')) {
        externalRequests.push(url);
      }
    });

    await loginAsOwner(page);
    await page.goto('/Admin/Index');
    await page.waitForLoadState('networkidle');

    // Should have zero external CSS requests
    expect(externalRequests).toHaveLength(0);
    console.log('✓ All CSS loaded locally');
  });

  test('P8-03: JavaScript loads from local paths only', async ({ page }) => {
    const externalScripts = [];

    page.on('request', request => {
      const url = request.url();
      const resourceType = request.resourceType();

      if (resourceType === 'script' && !url.includes('localhost')) {
        externalScripts.push(url);
      }
    });

    await loginAsOwner(page);
    await page.goto('/Calendar/Table');
    await page.waitForLoadState('networkidle');

    // Should have zero external script requests
    expect(externalScripts).toHaveLength(0);
    console.log('✓ All JavaScript loaded locally');
  });

  test('P8-04: Font loading fails gracefully', async ({ page, context }) => {
    // Block external font requests
    await context.route('**/*.{woff,woff2,ttf,otf,eot}', route => {
      const url = route.request().url();
      if (url.includes('fonts.googleapis.com') ||
          url.includes('cdnjs.cloudflare.com') ||
          url.includes('cdn.')) {
        route.abort();
      } else {
        route.continue();
      }
    });

    await loginAsOwner(page);
    await page.goto('/Admin/Index');

    // Page should still render with fallback fonts
    await expect(page.locator('h1, h2, body')).toBeVisible();

    // Text should be readable (not invisible)
    const bodyText = await page.locator('body').textContent();
    expect(bodyText.length).toBeGreaterThan(0);

    console.log('✓ Page renders with fallback fonts');
  });
});
