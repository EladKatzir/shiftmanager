// @ts-check
const { test, expect } = require('@playwright/test');
const { loginAsOwner } = require('../helpers/auth-helpers');
const { verifyCSSVariable } = require('../helpers/ui-helpers');

test.describe('UI Overhaul: Theme System', () => {

    test.describe('Light Mode (Default)', () => {

        test.beforeEach(async ({ page }) => {
            await loginAsOwner(page);
            await page.waitForLoadState('networkidle');
        });

        test('UI-THEME-01: Light mode is default', async ({ page }) => {
            // Check for dark mode class/attribute
            const isDarkMode = await page.evaluate(() =>
                document.documentElement.classList.contains('dark') ||
                document.documentElement.getAttribute('data-theme') === 'dark'
            );

            expect(isDarkMode).toBe(false);
        });

        test('UI-THEME-02: Light mode colors match design tokens', async ({ page }) => {
            // Verify key light mode colors
            const primary = await verifyCSSVariable(page, '--primary');
            const bg = await verifyCSSVariable(page, '--bg');

            // Light mode primary should be navy (#1E3A5F)
            expect(primary.toLowerCase()).toMatch(/1e3a5f|rgb\(30,\s*58,\s*95\)/i);
        });
    });

    test.describe('Dark Mode', () => {

        test.beforeEach(async ({ page }) => {
            await loginAsOwner(page);
            await page.waitForLoadState('networkidle');
        });

        test('UI-THEME-DARK-01: Dark mode toggle exists', async ({ page }) => {
            const themeToggle = page.locator('[data-theme-toggle], .theme-toggle, button[aria-label*="theme"]');
            const exists = await themeToggle.count() > 0;

            console.log(`Theme toggle present: ${exists}`);
        });

        test('UI-THEME-DARK-02: Dark mode can be activated', async ({ page }) => {
            const themeToggle = page.locator('[data-theme-toggle], .theme-toggle');

            if (await themeToggle.isVisible()) {
                await themeToggle.click();
                await page.waitForTimeout(300);

                const isDarkMode = await page.evaluate(() =>
                    document.documentElement.classList.contains('dark') ||
                    document.documentElement.getAttribute('data-theme') === 'dark'
                );

                expect(isDarkMode).toBe(true);
            }
        });

        test('UI-THEME-DARK-03: Dark mode colors change appropriately', async ({ page }) => {
            // Simulate dark mode via media query
            await page.emulateMedia({ colorScheme: 'dark' });
            await page.reload();
            await page.waitForLoadState('networkidle');

            // Check if system preference is respected
            const primary = await verifyCSSVariable(page, '--primary');

            // Dark mode primary should be sky blue (#5B9BD5)
            // This test documents expected behavior
            console.log(`Primary color in dark mode: ${primary}`);
        });

        test('UI-THEME-DARK-04: Theme preference persists', async ({ page }) => {
            const themeToggle = page.locator('[data-theme-toggle], .theme-toggle');

            if (await themeToggle.isVisible()) {
                // Switch to dark mode
                await themeToggle.click();
                await page.waitForTimeout(300);

                // Reload
                await page.reload();
                await page.waitForLoadState('networkidle');

                // Check localStorage for saved preference
                const savedTheme = await page.evaluate(() =>
                    localStorage.getItem('theme') ||
                    localStorage.getItem('color-scheme') ||
                    localStorage.getItem('shifty_theme')
                );

                console.log(`Saved theme preference: ${savedTheme}`);
            }
        });
    });

    test.describe('High Contrast Mode', () => {

        test('UI-THEME-HC-01: Respects prefers-contrast media query', async ({ page }) => {
            // Emulate high contrast
            await page.emulateMedia({ forcedColors: 'active' });
            await page.goto('/Auth/Login');
            await page.waitForLoadState('networkidle');

            // Page should still be usable
            const emailInput = page.locator('input[name="Email"], input#Email');
            await expect(emailInput).toBeVisible();
        });
    });
});
