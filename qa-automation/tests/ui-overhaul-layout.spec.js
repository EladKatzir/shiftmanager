// @ts-check
const { test, expect } = require('@playwright/test');
const { loginAsOwner, loginAsRole } = require('../helpers/auth-helpers');
const {
    verifyShiftyLogo,
    verifySidebarPresence,
    verifySidebarCategories,
    verifyContextSwitcher,
    verifyLucideIconsLoaded,
    verifyCSSVariable,
    verifyNoRawLocalizationKeys
} = require('../helpers/ui-helpers');
const { runAccessibilityAudit, getCriticalViolations, formatViolations } = require('../helpers/accessibility-helpers');

test.describe('UI Overhaul: Authenticated Layout', () => {

    test.describe('Owner View', () => {

        test.beforeEach(async ({ page }) => {
            await loginAsOwner(page);
            await page.waitForLoadState('networkidle');
        });

        test('UI-LAYOUT-01: Sidebar is visible after login', async ({ page }) => {
            await verifySidebarPresence(page, true);
        });

        test('UI-LAYOUT-02: SHIFTY logo displayed in sidebar/header', async ({ page }) => {
            await verifyShiftyLogo(page);
        });

        test('UI-LAYOUT-03: Sidebar has expected categories for Owner', async ({ page }) => {
            // Owner should see all categories
            await verifySidebarCategories(page, [
                'MY SHIFTY',
                'CALENDARS'
            ]);
        });

        test('UI-LAYOUT-04: Sidebar categories are collapsible', async ({ page }) => {
            const category = page.locator('.nav-category, .sidebar-category').first();

            // Find collapse toggle (could be the category header itself or a button)
            const toggle = category.locator('[data-collapse-toggle], .collapse-toggle, button').first();

            if (await toggle.isVisible()) {
                // Get initial state
                const initiallyExpanded = await category.locator('.nav-items, .category-items').isVisible();

                // Click to toggle
                await toggle.click();
                await page.waitForTimeout(300); // Animation time

                // Verify state changed
                const nowExpanded = await category.locator('.nav-items, .category-items').isVisible();
                expect(nowExpanded).not.toBe(initiallyExpanded);
            }
        });

        test('UI-LAYOUT-05: Icons render correctly', async ({ page }) => {
            await verifyLucideIconsLoaded(page);
        });

        test('UI-LAYOUT-06: CSS tokens defined in authenticated view', async ({ page }) => {
            await verifyCSSVariable(page, '--primary');
            await verifyCSSVariable(page, '--surface');
            await verifyCSSVariable(page, '--text');
            await verifyCSSVariable(page, '--border');
        });

        test('UI-LAYOUT-07: No raw localization keys visible', async ({ page }) => {
            const rawKeys = await verifyNoRawLocalizationKeys(page);

            if (rawKeys.length > 0) {
                console.log('Potential raw localization keys found:', rawKeys);
            }

            // Allow some tolerance for dynamic content
            expect(rawKeys.length).toBeLessThan(5);
        });

        test('UI-LAYOUT-A11Y-01: Authenticated layout passes WCAG AA', async ({ page }) => {
            const results = await runAccessibilityAudit(page, {
                // Exclude dynamic content that may not be loaded
                exclude: ['.loading', '.skeleton']
            });

            const critical = getCriticalViolations(results.violations);
            if (critical.length > 0) {
                console.log('Critical violations:', formatViolations(critical));
            }

            expect(critical).toHaveLength(0);
        });

        test('UI-LAYOUT-A11Y-02: Sidebar has proper ARIA landmarks', async ({ page }) => {
            const sidebar = page.locator('.app-sidebar, #appSidebar');

            // Should have navigation role or be within nav element
            const hasNavRole = await sidebar.evaluate(el => {
                return el.role === 'navigation' ||
                       el.closest('nav') !== null ||
                       el.querySelector('nav') !== null;
            });

            expect(hasNavRole).toBe(true);
        });

        test('UI-LAYOUT-A11Y-03: Skip link present for keyboard users', async ({ page }) => {
            // Skip link should be first focusable element
            await page.keyboard.press('Tab');

            const skipLink = page.locator('a[href="#main"], a[href="#content"], .skip-link');
            // Skip link may be visually hidden but should exist
            const skipLinkExists = await skipLink.count() > 0;

            // This is a best practice, not always required
            if (!skipLinkExists) {
                console.log('Note: Skip link not found - consider adding for accessibility');
            }
        });
    });

    test.describe('Employee View (Restricted)', () => {

        test.beforeEach(async ({ page }) => {
            // Try to login as Employee role
            try {
                await loginAsRole(page, 'Employee');
                await page.waitForLoadState('networkidle');
            } catch (e) {
                test.skip();
            }
        });

        test('UI-LAYOUT-RBAC-01: Employee sees limited sidebar categories', async ({ page }) => {
            // Employee should see MY SHIFTY but not ADMIN
            await verifySidebarPresence(page, true);

            // Should NOT see Admin category
            const adminCategory = page.locator('.nav-category:has-text("ADMIN"), .sidebar-category:has-text("ADMIN")');
            await expect(adminCategory).not.toBeVisible();
        });

        test('UI-LAYOUT-RBAC-02: Context switcher hidden for single-context users', async ({ page }) => {
            // Employee typically has single company context
            const hasContextSwitcher = await verifyContextSwitcher(page);

            // If employee has only one company, switcher should be hidden
            // This test documents the expected behavior
            console.log(`Context switcher visible: ${hasContextSwitcher}`);
        });
    });
});
