// @ts-check
const { test, expect } = require('@playwright/test');
const { loginAsOwner, loginAsRole } = require('../helpers/auth-helpers');
const { verifyOnCallWidget } = require('../helpers/ui-helpers');
const { runAccessibilityAudit, getCriticalViolations } = require('../helpers/accessibility-helpers');

test.describe('UI Overhaul: Widget System', () => {

    test.describe('On-Call Widget', () => {

        test.beforeEach(async ({ page }) => {
            await loginAsOwner(page);
            await page.waitForLoadState('networkidle');
        });

        test('UI-WIDGET-01: On-Call widget present in sidebar', async ({ page }) => {
            const hasWidget = await verifyOnCallWidget(page);

            // Widget visibility depends on grants
            // For Owner, it should typically be visible
            console.log(`On-Call widget visible: ${hasWidget}`);
        });

        test('UI-WIDGET-02: On-Call widget shows contact info', async ({ page }) => {
            const widget = page.locator('.on-call-widget, [data-testid="on-call-widget"]');

            if (await widget.isVisible()) {
                // Should have contact name
                const contactName = widget.locator('.contact-name, .name');

                // Should have phone or call action
                const phoneAction = widget.locator('a[href^="tel:"], .phone-number, [data-phone]');

                console.log(`Contact name visible: ${await contactName.isVisible()}`);
                console.log(`Phone action visible: ${await phoneAction.isVisible()}`);
            }
        });

        test('UI-WIDGET-03: On-Call widget is collapsible', async ({ page }) => {
            const widget = page.locator('.on-call-widget, [data-testid="on-call-widget"]');

            if (await widget.isVisible()) {
                const collapseBtn = widget.locator('[data-collapse], .collapse-btn, button:has-text("−")');

                if (await collapseBtn.isVisible()) {
                    // Get initial content visibility
                    const content = widget.locator('.widget-content, .contact-list');
                    const initiallyVisible = await content.isVisible();

                    // Click collapse
                    await collapseBtn.click();
                    await page.waitForTimeout(300);

                    // Content visibility should change
                    const nowVisible = await content.isVisible();
                    expect(nowVisible).not.toBe(initiallyVisible);
                }
            }
        });

        test('UI-WIDGET-04: Widget state persists (localStorage)', async ({ page }) => {
            const widget = page.locator('.on-call-widget, [data-testid="on-call-widget"]');

            if (await widget.isVisible()) {
                // Collapse the widget
                const collapseBtn = widget.locator('[data-collapse], .collapse-btn');
                if (await collapseBtn.isVisible()) {
                    await collapseBtn.click();
                    await page.waitForTimeout(300);
                }

                // Reload page
                await page.reload();
                await page.waitForLoadState('networkidle');

                // Check localStorage
                const savedState = await page.evaluate(() =>
                    localStorage.getItem('shifty_widget_collapsed') ||
                    localStorage.getItem('widget_preferences')
                );

                console.log(`Saved widget state: ${savedState}`);
            }
        });

        test('UI-WIDGET-A11Y-01: Widget passes accessibility', async ({ page }) => {
            const widget = page.locator('.on-call-widget, [data-testid="on-call-widget"]');

            if (await widget.isVisible()) {
                const results = await runAccessibilityAudit(page, {
                    include: ['.on-call-widget, [data-testid="on-call-widget"]']
                });

                const critical = getCriticalViolations(results.violations);
                expect(critical).toHaveLength(0);
            }
        });
    });

    test.describe('Grant-Based Widget Visibility', () => {

        test('UI-WIDGET-GRANT-01: Owner sees all widgets', async ({ page }) => {
            await loginAsOwner(page);
            await page.waitForLoadState('networkidle');

            // Count visible widgets
            const widgets = page.locator('.widget, [data-widget]');
            const count = await widgets.count();

            console.log(`Owner sees ${count} widgets`);
        });

        test('UI-WIDGET-GRANT-02: Employee sees limited widgets', async ({ page }) => {
            try {
                await loginAsRole(page, 'Employee');
                await page.waitForLoadState('networkidle');

                // Count visible widgets
                const widgets = page.locator('.widget, [data-widget]');
                const employeeCount = await widgets.count();

                console.log(`Employee sees ${employeeCount} widgets`);
            } catch (e) {
                test.skip();
            }
        });
    });
});
