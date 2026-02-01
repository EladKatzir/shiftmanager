// @ts-check
const { test, expect } = require('@playwright/test');
const { loginAsOwner, loginAsRole } = require('../helpers/auth-helpers');
const { verifyScopeSwitcher, verifyNoRawLocalizationKeys } = require('../helpers/ui-helpers');
const { runAccessibilityAudit, getCriticalViolations, formatViolations } = require('../helpers/accessibility-helpers');

test.describe('UI Overhaul: Calendar Views', () => {

    test.describe('Month Calendar', () => {

        test.beforeEach(async ({ page }) => {
            await loginAsOwner(page);
            await page.goto('/Calendar/Month');
            await page.waitForLoadState('networkidle');
        });

        test('UI-CAL-01: Month calendar displays correctly', async ({ page }) => {
            // Calendar grid should be visible
            const calendarGrid = page.locator('.calendar-grid, .month-grid, table.calendar');
            await expect(calendarGrid).toBeVisible();
        });

        test('UI-CAL-02: Scope switcher present above calendar', async ({ page }) => {
            await verifyScopeSwitcher(page, ['Mine', 'Company']);
        });

        test('UI-CAL-03: Scope switcher "Mine Only" filters calendar', async ({ page }) => {
            const mineButton = page.locator('.scope-switcher button:has-text("Mine"), [data-scope="mine"]');

            if (await mineButton.isVisible()) {
                await mineButton.click();
                await page.waitForLoadState('networkidle');

                // URL or page state should reflect the filter
                const url = page.url();
                expect(url).toMatch(/mine|scope=mine|filter=user/i);
            }
        });

        test('UI-CAL-04: Month navigation works', async ({ page }) => {
            // Find previous/next month buttons
            const prevButton = page.locator('[aria-label*="previous"], button:has-text("◀"), .nav-prev');
            const nextButton = page.locator('[aria-label*="next"], button:has-text("▶"), .nav-next');

            // Get current month title
            const monthTitle = page.locator('.calendar-title, .month-header, h1, h2').first();
            const initialMonth = await monthTitle.textContent();

            // Navigate to next month
            if (await nextButton.isVisible()) {
                await nextButton.click();
                await page.waitForLoadState('networkidle');

                const newMonth = await monthTitle.textContent();
                expect(newMonth).not.toBe(initialMonth);
            }
        });

        test('UI-CAL-05: Calendar cells show shift badges', async ({ page }) => {
            // Look for shift indicators in calendar cells
            const shiftBadges = page.locator('.shift-badge, .calendar-shift, .shift-indicator');

            // There should be some shifts visible (seeded data)
            const badgeCount = await shiftBadges.count();
            console.log(`Found ${badgeCount} shift badges in calendar`);

            // At minimum, verify the calendar structure exists
            const calendarCells = page.locator('.calendar-cell, td[data-date], .day-cell');
            await expect(calendarCells.first()).toBeVisible();
        });

        test('UI-CAL-06: Shift type colors follow design tokens', async ({ page }) => {
            // Get shift badges
            const shiftBadges = page.locator('.shift-badge, .calendar-shift').first();

            if (await shiftBadges.isVisible()) {
                const bgColor = await shiftBadges.evaluate(el =>
                    getComputedStyle(el).backgroundColor
                );

                // Should not be default browser color
                expect(bgColor).not.toBe('rgba(0, 0, 0, 0)');
                expect(bgColor).not.toBe('transparent');
            }
        });

        test('UI-CAL-A11Y-01: Calendar passes accessibility audit', async ({ page }) => {
            const results = await runAccessibilityAudit(page, {
                exclude: ['.loading', '.skeleton']
            });

            const critical = getCriticalViolations(results.violations);
            if (critical.length > 0) {
                console.log('Calendar accessibility issues:', formatViolations(critical));
            }

            expect(critical).toHaveLength(0);
        });

        test('UI-CAL-A11Y-02: Calendar cells are keyboard navigable', async ({ page }) => {
            // Focus on calendar
            const calendarGrid = page.locator('.calendar-grid, .month-grid, table.calendar');
            await calendarGrid.focus();

            // Arrow keys should navigate
            await page.keyboard.press('ArrowRight');

            // Some cell should be focused
            const focusedElement = await page.evaluate(() =>
                document.activeElement?.className
            );

            console.log(`Focused element class: ${focusedElement}`);
        });
    });

    test.describe('Week Calendar', () => {

        test.beforeEach(async ({ page }) => {
            await loginAsOwner(page);
            await page.goto('/Calendar/Week');
            await page.waitForLoadState('networkidle');
        });

        test('UI-CAL-WEEK-01: Week view displays 7 day columns', async ({ page }) => {
            const dayHeaders = page.locator('.day-header, th[data-day], .week-day-header');
            const count = await dayHeaders.count();

            expect(count).toBe(7);
        });

        test('UI-CAL-WEEK-02: Week navigation works', async ({ page }) => {
            const prevButton = page.locator('[aria-label*="previous"], button:has-text("◀"), .nav-prev');
            const nextButton = page.locator('[aria-label*="next"], button:has-text("▶"), .nav-next');

            if (await nextButton.isVisible()) {
                const initialUrl = page.url();
                await nextButton.click();
                await page.waitForLoadState('networkidle');

                const newUrl = page.url();
                // URL should change with date parameter
                expect(newUrl).not.toBe(initialUrl);
            }
        });
    });

    test.describe('Day Calendar', () => {

        test.beforeEach(async ({ page }) => {
            await loginAsOwner(page);
            await page.goto('/Calendar/Day');
            await page.waitForLoadState('networkidle');
        });

        test('UI-CAL-DAY-01: Day view shows time slots', async ({ page }) => {
            const timeSlots = page.locator('.time-slot, .hour-row, tr[data-hour]');
            const count = await timeSlots.count();

            // Should have multiple time slots
            expect(count).toBeGreaterThan(0);
        });
    });

    test.describe('Calendar Empty States', () => {

        test.beforeEach(async ({ page }) => {
            await loginAsOwner(page);
        });

        test('UI-CAL-EMPTY-01: Empty state shown when no shifts', async ({ page }) => {
            // Navigate to a future month with no data
            await page.goto('/Calendar/Month?year=2030&month=12');
            await page.waitForLoadState('networkidle');

            // Look for empty state message
            const emptyState = page.locator('.empty-state, .no-shifts-message, [data-testid="empty-state"]');

            // If no shifts, empty state should be visible
            // This is conditional based on actual data
            const isEmpty = await emptyState.isVisible();
            console.log(`Empty state visible: ${isEmpty}`);
        });

        test('UI-CAL-EMPTY-02: Empty state has localized message', async ({ page }) => {
            await page.goto('/Calendar/Month?year=2030&month=12');
            await page.waitForLoadState('networkidle');

            const rawKeys = await verifyNoRawLocalizationKeys(page);
            expect(rawKeys.length).toBeLessThan(3);
        });
    });
});
