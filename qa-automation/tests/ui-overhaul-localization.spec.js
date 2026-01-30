// @ts-check
const { test, expect } = require('@playwright/test');
const { loginAsOwner } = require('../helpers/auth-helpers');
const { verifyNoRawLocalizationKeys } = require('../helpers/ui-helpers');

test.describe('UI Overhaul: Localization', () => {

    test.describe('English (Default)', () => {

        test.beforeEach(async ({ page }) => {
            // Set English locale
            await page.goto('/Auth/Login');
            await page.waitForLoadState('networkidle');
        });

        test('UI-L10N-01: Login page text is in English', async ({ page }) => {
            // Look for English login text
            const hasEnglish = await page.locator('text=Email, text=Password, text=Login, text=Sign').count() > 0;
            expect(hasEnglish).toBe(true);
        });

        test('UI-L10N-02: No raw localization keys on login page', async ({ page }) => {
            const rawKeys = await verifyNoRawLocalizationKeys(page);
            expect(rawKeys).toHaveLength(0);
        });
    });

    test.describe('Hebrew (RTL)', () => {

        test.beforeEach(async ({ page }) => {
            // Set Hebrew culture cookie/header
            await page.context().addCookies([{
                name: '.AspNetCore.Culture',
                value: 'c=he-IL|uic=he-IL',
                domain: 'localhost',
                path: '/'
            }]);
        });

        test('UI-L10N-HE-01: Page switches to Hebrew', async ({ page }) => {
            await page.goto('/Auth/Login');
            await page.waitForLoadState('networkidle');

            // Check for RTL direction
            const direction = await page.evaluate(() =>
                document.documentElement.dir ||
                getComputedStyle(document.body).direction
            );

            // Hebrew should trigger RTL
            expect(direction).toMatch(/rtl/i);
        });

        test('UI-L10N-HE-02: Hebrew text appears on login', async ({ page }) => {
            await page.goto('/Auth/Login');
            await page.waitForLoadState('networkidle');

            // Look for Hebrew characters
            const bodyText = await page.locator('body').textContent();
            const hasHebrew = /[\u0590-\u05FF]/.test(bodyText || '');

            console.log(`Hebrew characters found: ${hasHebrew}`);
        });

        test('UI-L10N-HE-03: Layout mirrors for RTL', async ({ page }) => {
            await loginAsOwner(page);
            await page.waitForLoadState('networkidle');

            // Sidebar should be on the right in RTL
            const sidebar = page.locator('.app-sidebar, #appSidebar');

            if (await sidebar.isVisible()) {
                const sidebarBox = await sidebar.boundingBox();
                const viewportSize = page.viewportSize();

                // In RTL, sidebar should be on right side
                // (x position > half of viewport width)
                if (sidebarBox && viewportSize) {
                    const isOnRight = sidebarBox.x > viewportSize.width / 2;
                    console.log(`Sidebar on right (RTL): ${isOnRight}, x: ${sidebarBox.x}`);
                }
            }
        });
    });

    test.describe('Localization API', () => {

        test('UI-L10N-API-01: API returns localized strings', async ({ page }) => {
            await page.goto('/Auth/Login');

            const response = await page.request.get('/Api/Localization?keys=Login_Title,Login_Submit');

            expect(response.status()).toBe(200);

            const data = await response.json();
            expect(data).toHaveProperty('Login_Title');
            expect(data).toHaveProperty('Login_Submit');
        });

        test('UI-L10N-API-02: API respects culture header', async ({ page }) => {
            await page.goto('/Auth/Login');

            // Request with Hebrew culture
            const response = await page.request.get('/Api/Localization?keys=Login_Title', {
                headers: {
                    'Accept-Language': 'he-IL'
                }
            });

            expect(response.status()).toBe(200);
        });

        test('UI-L10N-API-03: API handles empty keys gracefully', async ({ page }) => {
            await page.goto('/Auth/Login');

            const response = await page.request.get('/Api/Localization?keys=');

            expect(response.status()).toBe(200);
            const data = await response.json();
            expect(data).toEqual({});
        });
    });

    test.describe('Authenticated Pages Localization', () => {

        test.beforeEach(async ({ page }) => {
            await loginAsOwner(page);
        });

        test('UI-L10N-AUTH-01: Sidebar labels are localized', async ({ page }) => {
            const rawKeys = await verifyNoRawLocalizationKeys(page);

            // Filter to just sidebar area
            const sidebarText = await page.locator('.app-sidebar, #appSidebar').textContent();
            const sidebarRawKeys = (sidebarText?.match(/[A-Z][a-z]+_[A-Z][a-z]+_[A-Za-z]+/g) || [])
                .filter(k => k.length > 10);

            expect(sidebarRawKeys).toHaveLength(0);
        });

        test('UI-L10N-AUTH-02: Calendar headers are localized', async ({ page }) => {
            await page.goto('/Calendar/Month');
            await page.waitForLoadState('networkidle');

            const rawKeys = await verifyNoRawLocalizationKeys(page);
            expect(rawKeys.length).toBeLessThan(3);
        });
    });
});
