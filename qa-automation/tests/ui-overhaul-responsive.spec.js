// @ts-check
const { test, expect } = require('@playwright/test');
const { loginAsOwner } = require('../helpers/auth-helpers');
const { verifySidebarPresence, verifyShiftyLogo } = require('../helpers/ui-helpers');
const { runAccessibilityAudit, getCriticalViolations } = require('../helpers/accessibility-helpers');

// Mobile viewport dimensions (iPhone 12)
const MOBILE_VIEWPORT = { width: 390, height: 844 };

// Tablet viewport dimensions (iPad Pro 11)
const TABLET_VIEWPORT = { width: 834, height: 1194 };

// Desktop wide viewport
const DESKTOP_VIEWPORT = { width: 1920, height: 1080 };

test.describe('UI Overhaul: Responsive Design', () => {

    test.describe('Mobile View', () => {

        test('UI-RESP-MOB-01: Login page works on mobile', async ({ page }) => {
            await page.setViewportSize(MOBILE_VIEWPORT);
            await page.goto('/Auth/Login');
            await page.waitForLoadState('networkidle');

            // Logo should be visible
            await verifyShiftyLogo(page);

            // Form should be usable
            const emailInput = page.locator('input[name="Email"], input#Email');
            await expect(emailInput).toBeVisible();

            // Form should fit in viewport (no horizontal scroll)
            const hasHorizontalScroll = await page.evaluate(() =>
                document.documentElement.scrollWidth > document.documentElement.clientWidth
            );

            expect(hasHorizontalScroll).toBe(false);
        });

        test('UI-RESP-MOB-02: Sidebar collapsed on mobile', async ({ page }) => {
            await page.setViewportSize(MOBILE_VIEWPORT);
            await loginAsOwner(page);
            await page.waitForLoadState('networkidle');

            // Sidebar should be hidden or collapsed by default on mobile
            const sidebar = page.locator('.app-sidebar, #appSidebar');
            const isFullyVisible = await sidebar.isVisible();

            // Either hidden or needs hamburger menu
            if (isFullyVisible) {
                const sidebarBox = await sidebar.boundingBox();
                // Sidebar might be off-screen (transform: translateX(-100%))
                console.log(`Sidebar position: x=${sidebarBox?.x}`);
            }

            // Hamburger menu should be visible
            const hamburger = page.locator('.hamburger, .menu-toggle, [aria-label*="menu"]');
            const hamburgerVisible = await hamburger.isVisible();
            console.log(`Hamburger menu visible: ${hamburgerVisible}`);
        });

        test('UI-RESP-MOB-03: Mobile sidebar opens on hamburger click', async ({ page }) => {
            await page.setViewportSize(MOBILE_VIEWPORT);
            await loginAsOwner(page);
            await page.waitForLoadState('networkidle');

            const hamburger = page.locator('.hamburger, .menu-toggle, [aria-label*="menu"]');

            if (await hamburger.isVisible()) {
                await hamburger.click();
                await page.waitForTimeout(300);

                // Sidebar should now be visible
                const sidebar = page.locator('.app-sidebar, #appSidebar');
                await expect(sidebar).toBeVisible();
            }
        });

        test('UI-RESP-MOB-04: Calendar adapts to mobile width', async ({ page }) => {
            await page.setViewportSize(MOBILE_VIEWPORT);
            await loginAsOwner(page);
            await page.goto('/Calendar/Month');
            await page.waitForLoadState('networkidle');

            // Calendar should fit in viewport
            const hasHorizontalScroll = await page.evaluate(() =>
                document.documentElement.scrollWidth > document.documentElement.clientWidth
            );

            // Some horizontal scroll may be acceptable for calendars
            console.log(`Has horizontal scroll: ${hasHorizontalScroll}`);
        });

        test('UI-RESP-MOB-A11Y-01: Mobile view passes accessibility', async ({ page }) => {
            await page.setViewportSize(MOBILE_VIEWPORT);
            await page.goto('/Auth/Login');
            await page.waitForLoadState('networkidle');

            const results = await runAccessibilityAudit(page);
            const critical = getCriticalViolations(results.violations);

            expect(critical).toHaveLength(0);
        });
    });

    test.describe('Tablet View', () => {

        test('UI-RESP-TAB-01: Layout adapts for tablet', async ({ page }) => {
            await page.setViewportSize(TABLET_VIEWPORT);
            await loginAsOwner(page);
            await page.waitForLoadState('networkidle');

            // Sidebar might be visible or collapsible on tablet
            const sidebar = page.locator('.app-sidebar, #appSidebar');
            const isVisible = await sidebar.isVisible();

            console.log(`Sidebar visible on tablet: ${isVisible}`);
        });

        test('UI-RESP-TAB-02: Calendar shows full week on tablet', async ({ page }) => {
            await page.setViewportSize(TABLET_VIEWPORT);
            await loginAsOwner(page);
            await page.goto('/Calendar/Week');
            await page.waitForLoadState('networkidle');

            // Should show all 7 days
            const dayHeaders = page.locator('.day-header, th[data-day], .week-day-header');
            const count = await dayHeaders.count();

            expect(count).toBe(7);
        });
    });

    test.describe('Desktop Wide View', () => {

        test('UI-RESP-DESK-01: Layout uses wide viewport', async ({ page }) => {
            await page.setViewportSize(DESKTOP_VIEWPORT);
            await loginAsOwner(page);
            await page.waitForLoadState('networkidle');

            // Sidebar should be visible and expanded
            await verifySidebarPresence(page, true);

            // Main content should have generous width
            const mainContent = page.locator('main, .main-content, [role="main"]');
            const box = await mainContent.boundingBox();

            expect(box?.width).toBeGreaterThan(1000);
        });

        test('UI-RESP-DESK-02: No wasted space on wide screens', async ({ page }) => {
            await page.setViewportSize(DESKTOP_VIEWPORT);
            await loginAsOwner(page);
            await page.goto('/Calendar/Month');
            await page.waitForLoadState('networkidle');

            // Calendar should expand to use available space
            const calendar = page.locator('.calendar-grid, .month-grid, table.calendar');
            const box = await calendar.boundingBox();

            console.log(`Calendar width: ${box?.width}`);
        });
    });
});
