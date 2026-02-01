// @ts-check
const { test, expect } = require('@playwright/test');
const { loginAsOwner, loginAsRole } = require('../helpers/auth-helpers');

/**
 * Visual Regression Test Suite
 * 
 * This suite captures baseline screenshots of key pages and compares them
 * against future runs to detect unintended visual changes.
 * 
 * Tagged with @visual for separate CI runs.
 * 
 * Baseline screenshots are stored in:
 * qa-automation/tests/visual-regression.spec.js-snapshots/
 * 
 * To update baselines:
 * npx playwright test visual-regression.spec.js --update-snapshots
 */

test.describe('Visual Regression Tests @visual', () => {

    // ===========================================================================
    // Login Page (Unauthenticated)
    // ===========================================================================
    test.describe('Login Page', () => {

        test('VR-LOGIN-01: Login page visual baseline', async ({ page }) => {
            await page.goto('/Auth/Login');
            await page.waitForLoadState('networkidle');
            
            // Wait for any animations to complete
            await page.waitForTimeout(500);

            await expect(page).toHaveScreenshot('login-page.png', {
                fullPage: true,
                mask: [
                    // Mask any dynamic elements like timestamps
                    page.locator('[data-testid="timestamp"]'),
                    page.locator('.timestamp'),
                ],
            });
        });

        test('VR-LOGIN-02: Login form focused state', async ({ page }) => {
            await page.goto('/Auth/Login');
            await page.waitForLoadState('networkidle');

            // Focus on email input to show focus styles
            const emailInput = page.locator('input[name="Email"], input#Email');
            await emailInput.focus();
            await page.waitForTimeout(200);

            await expect(page).toHaveScreenshot('login-page-focused.png', {
                fullPage: true,
            });
        });

        test('VR-LOGIN-03: Login form with validation error', async ({ page }) => {
            await page.goto('/Auth/Login');
            await page.waitForLoadState('networkidle');

            // Submit empty form to trigger validation
            const submitButton = page.locator('form:has(input[name="Email"]) button[type="submit"]');
            await submitButton.click();
            await page.waitForTimeout(500);

            await expect(page).toHaveScreenshot('login-page-validation-error.png', {
                fullPage: true,
            });
        });
    });

    // ===========================================================================
    // Calendar Views (Authenticated)
    // ===========================================================================
    test.describe('Calendar Views', () => {

        test.beforeEach(async ({ page }) => {
            await loginAsOwner(page);
        });

        test('VR-CAL-01: Month calendar visual baseline', async ({ page }) => {
            await page.goto('/Calendar/Month');
            await page.waitForLoadState('networkidle');
            await page.waitForTimeout(500);

            await expect(page).toHaveScreenshot('calendar-month.png', {
                fullPage: true,
                mask: [
                    // Mask date-specific content that changes
                    page.locator('[data-testid="current-date"]'),
                    page.locator('.today-indicator'),
                ],
            });
        });

        test('VR-CAL-02: Week calendar visual baseline', async ({ page }) => {
            await page.goto('/Calendar/Week');
            await page.waitForLoadState('networkidle');
            await page.waitForTimeout(500);

            await expect(page).toHaveScreenshot('calendar-week.png', {
                fullPage: true,
                mask: [
                    page.locator('[data-testid="current-date"]'),
                    page.locator('.today-indicator'),
                    page.locator('.current-time-indicator'),
                ],
            });
        });

        test('VR-CAL-03: Day calendar visual baseline', async ({ page }) => {
            await page.goto('/Calendar/Day');
            await page.waitForLoadState('networkidle');
            await page.waitForTimeout(500);

            await expect(page).toHaveScreenshot('calendar-day.png', {
                fullPage: true,
                mask: [
                    page.locator('[data-testid="current-date"]'),
                    page.locator('.today-indicator'),
                    page.locator('.current-time-indicator'),
                ],
            });
        });

        test('VR-CAL-04: Table calendar visual baseline', async ({ page }) => {
            await page.goto('/Calendar/Table');
            await page.waitForLoadState('networkidle');
            await page.waitForTimeout(500);

            await expect(page).toHaveScreenshot('calendar-table.png', {
                fullPage: true,
                mask: [
                    page.locator('[data-testid="current-date"]'),
                    page.locator('.today-indicator'),
                ],
            });
        });
    });

    // ===========================================================================
    // Admin Dashboard (Authenticated - Owner)
    // ===========================================================================
    test.describe('Admin Dashboard', () => {

        test.beforeEach(async ({ page }) => {
            await loginAsOwner(page);
        });

        test('VR-ADMIN-01: Admin index page visual baseline', async ({ page }) => {
            await page.goto('/Admin/Index');
            await page.waitForLoadState('networkidle');
            await page.waitForTimeout(500);

            await expect(page).toHaveScreenshot('admin-index.png', {
                fullPage: true,
                mask: [
                    // Mask any dynamic stats/counters
                    page.locator('[data-testid="stats-counter"]'),
                    page.locator('.stat-value'),
                    page.locator('.counter'),
                ],
            });
        });

        test('VR-ADMIN-02: Users management page visual baseline', async ({ page }) => {
            await page.goto('/Admin/Users');
            await page.waitForLoadState('networkidle');
            await page.waitForTimeout(500);

            await expect(page).toHaveScreenshot('admin-users.png', {
                fullPage: true,
                mask: [
                    // Mask user-specific data that may change
                    page.locator('td[data-field="email"]'),
                    page.locator('td[data-field="lastLogin"]'),
                    page.locator('.user-avatar'),
                ],
            });
        });

        test('VR-ADMIN-03: Companies management page visual baseline', async ({ page }) => {
            await page.goto('/Admin/Companies');
            await page.waitForLoadState('networkidle');
            await page.waitForTimeout(500);

            await expect(page).toHaveScreenshot('admin-companies.png', {
                fullPage: true,
            });
        });

        test('VR-ADMIN-04: Shift types configuration page visual baseline', async ({ page }) => {
            await page.goto('/Admin/ShiftTypes');
            await page.waitForLoadState('networkidle');
            await page.waitForTimeout(500);

            await expect(page).toHaveScreenshot('admin-shift-types.png', {
                fullPage: true,
            });
        });

        test('VR-ADMIN-05: Analytics page visual baseline', async ({ page }) => {
            await page.goto('/Admin/Analytics');
            await page.waitForLoadState('networkidle');
            // Extra wait for charts/graphs to render
            await page.waitForTimeout(1000);

            await expect(page).toHaveScreenshot('admin-analytics.png', {
                fullPage: true,
                mask: [
                    // Mask dynamic chart data
                    page.locator('.chart-container'),
                    page.locator('canvas'),
                    page.locator('[data-testid="chart"]'),
                ],
            });
        });

        test('VR-ADMIN-06: Config page visual baseline', async ({ page }) => {
            await page.goto('/Admin/Config');
            await page.waitForLoadState('networkidle');
            await page.waitForTimeout(500);

            await expect(page).toHaveScreenshot('admin-config.png', {
                fullPage: true,
            });
        });
    });

    // ===========================================================================
    // Navigation Components
    // ===========================================================================
    test.describe('Navigation Components', () => {

        test.beforeEach(async ({ page }) => {
            await loginAsOwner(page);
        });

        test('VR-NAV-01: Sidebar expanded state', async ({ page }) => {
            await page.goto('/Calendar/Month');
            await page.waitForLoadState('networkidle');
            await page.waitForTimeout(500);

            // Ensure sidebar is expanded
            const sidebar = page.locator('.sidebar, [data-sidebar], nav[role="navigation"]').first();
            
            await expect(sidebar).toHaveScreenshot('sidebar-expanded.png');
        });

        test('VR-NAV-02: Sidebar collapsed state', async ({ page }) => {
            await page.goto('/Calendar/Month');
            await page.waitForLoadState('networkidle');

            // Try to collapse sidebar if collapse button exists
            const collapseBtn = page.locator('[data-sidebar-toggle], .sidebar-toggle, [aria-label*="collapse"]');
            if (await collapseBtn.isVisible()) {
                await collapseBtn.click();
                await page.waitForTimeout(500);
            }

            const sidebar = page.locator('.sidebar, [data-sidebar], nav[role="navigation"]').first();
            await expect(sidebar).toHaveScreenshot('sidebar-collapsed.png');
        });
    });

    // ===========================================================================
    // Responsive Breakpoints
    // ===========================================================================
    test.describe('Responsive Breakpoints', () => {

        test.beforeEach(async ({ page }) => {
            await loginAsOwner(page);
        });

        test('VR-RESP-01: Calendar at tablet breakpoint (768px)', async ({ page }) => {
            await page.setViewportSize({ width: 768, height: 1024 });
            await page.goto('/Calendar/Month');
            await page.waitForLoadState('networkidle');
            await page.waitForTimeout(500);

            await expect(page).toHaveScreenshot('calendar-month-tablet.png', {
                fullPage: true,
            });
        });

        test('VR-RESP-02: Calendar at mobile breakpoint (375px)', async ({ page }) => {
            await page.setViewportSize({ width: 375, height: 812 });
            await page.goto('/Calendar/Month');
            await page.waitForLoadState('networkidle');
            await page.waitForTimeout(500);

            await expect(page).toHaveScreenshot('calendar-month-mobile.png', {
                fullPage: true,
            });
        });

        test('VR-RESP-03: Login at mobile breakpoint (375px)', async ({ page }) => {
            await page.setViewportSize({ width: 375, height: 812 });
            await page.goto('/Auth/Login');
            await page.waitForLoadState('networkidle');
            await page.waitForTimeout(500);

            await expect(page).toHaveScreenshot('login-page-mobile.png', {
                fullPage: true,
            });
        });
    });

    // ===========================================================================
    // Theme Variations (if dark mode exists)
    // ===========================================================================
    test.describe('Theme Variations', () => {

        test('VR-THEME-01: Login page in dark mode preference', async ({ page }) => {
            // Set dark mode preference before navigation
            await page.emulateMedia({ colorScheme: 'dark' });
            await page.goto('/Auth/Login');
            await page.waitForLoadState('networkidle');
            await page.waitForTimeout(500);

            await expect(page).toHaveScreenshot('login-page-dark.png', {
                fullPage: true,
            });
        });

        test('VR-THEME-02: Calendar in dark mode preference', async ({ page }) => {
            await page.emulateMedia({ colorScheme: 'dark' });
            await loginAsOwner(page);
            await page.goto('/Calendar/Month');
            await page.waitForLoadState('networkidle');
            await page.waitForTimeout(500);

            await expect(page).toHaveScreenshot('calendar-month-dark.png', {
                fullPage: true,
            });
        });
    });
});
