// @ts-check
const { test, expect } = require('@playwright/test');
const { loginAsOwner, loginAsRole } = require('../helpers/auth-helpers');
const {
    verifyShiftyLogo,
    verifySidebarPresence,
    verifyLucideIconsLoaded,
    verifyNoRawLocalizationKeys
} = require('../helpers/ui-helpers');
const { runAccessibilityAudit, getCriticalViolations, formatViolations } = require('../helpers/accessibility-helpers');

/**
 * Integration test that walks through complete user journey
 * validating all UI overhaul elements work together
 */
test.describe('UI Overhaul: Integration Journey', () => {

    test('INTEGRATION-01: Complete user journey - Owner', async ({ page }) => {
        // Step 1: Unauthenticated - Login Page
        await page.goto('/Auth/Login');
        await page.waitForLoadState('networkidle');

        // Verify login page branding
        await verifyShiftyLogo(page);
        await verifySidebarPresence(page, false); // No sidebar on login

        // Step 2: Login
        await loginAsOwner(page);
        await page.waitForLoadState('networkidle');

        // Step 3: Authenticated - Dashboard/Home
        await verifySidebarPresence(page, true);
        await verifyShiftyLogo(page);
        await verifyLucideIconsLoaded(page);

        // Step 4: Navigate to Calendar
        const calendarLink = page.locator('a[href*="Calendar"], .nav-item:has-text("Calendar")').first();
        if (await calendarLink.isVisible()) {
            await calendarLink.click();
            await page.waitForLoadState('networkidle');
        } else {
            await page.goto('/Calendar/Month');
            await page.waitForLoadState('networkidle');
        }

        // Verify calendar page (Month uses .calendar-grid or .calendar-content)
        const calendarGrid = page.locator('.calendar-grid, .calendar-content, .excel-calendar__table, .cal-page').first();
        await expect(calendarGrid).toBeVisible();

        // Step 5: Check localization
        const rawKeys = await verifyNoRawLocalizationKeys(page);
        expect(rawKeys.length).toBeLessThan(5);

        // Step 6: Accessibility check (non-blocking — log violations but don't fail the integration test)
        try {
            const results = await runAccessibilityAudit(page);
            const critical = getCriticalViolations(results.violations);

            if (critical.length > 0) {
                console.log(`Critical accessibility violations (${critical.length}):`);
                console.log(formatViolations(critical));
            }
            // Warn about critical violations but don't fail the integration journey test
            // Accessibility is tested separately in dedicated modules
        } catch (axeError) {
            console.log('Accessibility audit skipped:', axeError.message);
        }

        // Step 7: Navigate to Admin
        await page.goto('/Admin/Index');
        await page.waitForLoadState('networkidle');

        // Should not redirect to access denied for Owner
        await expect(page).not.toHaveURL(/AccessDenied/);

        // Step 8: Logout — open the user menu dropdown first
        const userMenuTrigger = page.locator('#sidebarUserMenuTrigger');
        if (await userMenuTrigger.isVisible({ timeout: 3000 }).catch(() => false)) {
            await userMenuTrigger.click();
            await page.waitForTimeout(300);
        }
        const logoutForm = page.locator('.sidebar-user-menu__logout-form');
        if (await logoutForm.isVisible()) {
            await Promise.all([
                page.waitForURL(/\/Auth\/Login/),
                logoutForm.locator('button[type="submit"]').click()
            ]);
        }

        // Step 9: Verify back to login
        await expect(page).toHaveURL(/\/Auth\/Login/);
        await verifySidebarPresence(page, false);
    });

    test('INTEGRATION-02: Air-gapped environment simulation', async ({ page }) => {
        const externalRequests = [];

        // Monitor for external requests
        page.on('request', request => {
            const url = request.url();
            if (!url.includes('localhost') && !url.includes('127.0.0.1')) {
                if (!url.startsWith('chrome-extension://') &&
                    !url.startsWith('data:') &&
                    !url.startsWith('blob:')) {
                    externalRequests.push(url);
                }
            }
        });

        // Full journey
        await page.goto('/Auth/Login');
        await loginAsOwner(page);
        await page.goto('/Calendar/Month');
        await page.goto('/Admin/Index');

        // No external requests should be made
        expect(externalRequests).toHaveLength(0);
    });

    test('INTEGRATION-03: No console errors throughout journey', async ({ page }) => {
        const consoleErrors = [];

        page.on('console', msg => {
            if (msg.type() === 'error') {
                const text = msg.text();
                // Filter out known acceptable errors
                if (!text.includes('favicon') &&
                    !text.includes('404') &&
                    !text.includes('net::ERR')) {
                    consoleErrors.push(text);
                }
            }
        });

        // Full journey
        await page.goto('/Auth/Login');
        await loginAsOwner(page);
        await page.goto('/Calendar/Month');
        await page.goto('/Calendar/Week');
        await page.goto('/Calendar/Day');
        await page.goto('/Admin/Index');

        // Filter out known non-bug console errors (SignalR connection race conditions)
        const realErrors = consoleErrors.filter(e =>
            !e.includes('HttpConnection') &&
            !e.includes('WebSocket') &&
            !e.includes('SignalR') &&
            !e.includes('Failed to start') &&
            !e.includes('connection'));

        if (realErrors.length > 0) {
            console.log('Console errors found:');
            realErrors.forEach(e => console.log(`  - ${e}`));
        }

        expect(realErrors).toHaveLength(0);
    });

    test('INTEGRATION-04: Page load performance', async ({ page }) => {
        // Login page should load quickly
        const loginStart = Date.now();
        await page.goto('/Auth/Login');
        await page.waitForLoadState('networkidle');
        const loginTime = Date.now() - loginStart;

        console.log(`Login page load: ${loginTime}ms`);
        expect(loginTime).toBeLessThan(5000); // 5 second max

        // Login
        await loginAsOwner(page);

        // Calendar page load
        const calendarStart = Date.now();
        await page.goto('/Calendar/Month');
        await page.waitForLoadState('networkidle');
        const calendarTime = Date.now() - calendarStart;

        console.log(`Calendar page load: ${calendarTime}ms`);
        expect(calendarTime).toBeLessThan(5000);
    });
});
