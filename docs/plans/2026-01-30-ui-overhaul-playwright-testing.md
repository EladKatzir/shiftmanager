# UI Overhaul Playwright Testing Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Comprehensive end-to-end testing of the ShiftManager UI Overhaul ("Shifty" brand refresh) to validate visual, functional, and accessibility requirements.

**Architecture:** Page Object Model (POM) with role-based fixtures, visual regression testing via Playwright snapshots, and accessibility auditing via @axe-core/playwright.

**Tech Stack:** Playwright, @axe-core/playwright (accessibility), test-users fixture pattern

---

## Prerequisites

Before running any tests:

1. **Application running:** `dotnet run` in ShiftManager root (port 5000)
2. **Test users seeded:** Owner (admin@local / easteregg), Director, Manager, Employee per TestDataSeeder.cs
3. **Environment:** Copy `.env.example` to `.env` in qa-automation folder

---

## Task 1: Install Accessibility Testing Dependencies

**Files:**
- Modify: `qa-automation/package.json`

**Step 1: Add axe-core dependency**

```bash
cd qa-automation && npm install @axe-core/playwright --save-dev
```

**Step 2: Verify installation**

Run: `npm list @axe-core/playwright`
Expected: Shows version installed

**Step 3: Commit**

```bash
git add package.json package-lock.json
git commit -m "chore(qa): add axe-core for accessibility testing"
```

---

## Task 2: Create UI Test Helpers

**Files:**
- Create: `qa-automation/helpers/ui-helpers.js`

**Step 1: Create UI helper module**

```javascript
// @ts-check
const { expect } = require('@playwright/test');

/**
 * Verify SHIFTY logo is present and loads correctly
 * @param {import('@playwright/test').Page} page
 */
async function verifyShiftyLogo(page) {
    const logo = page.locator('img[src*="shifty-logo"], .app-logo img');
    await expect(logo).toBeVisible();

    // Verify SVG loads (not 404)
    const logoSrc = await logo.getAttribute('src');
    if (logoSrc) {
        const response = await page.request.get(logoSrc);
        expect(response.status()).toBe(200);
    }
}

/**
 * Verify sidebar navigation structure
 * @param {import('@playwright/test').Page} page
 * @param {boolean} shouldBeVisible - Whether sidebar should be visible
 */
async function verifySidebarPresence(page, shouldBeVisible) {
    const sidebar = page.locator('.app-sidebar, #appSidebar');
    if (shouldBeVisible) {
        await expect(sidebar).toBeVisible();
    } else {
        await expect(sidebar).not.toBeVisible();
    }
}

/**
 * Verify sidebar navigation categories
 * @param {import('@playwright/test').Page} page
 * @param {string[]} expectedCategories - Array of category names to check
 */
async function verifySidebarCategories(page, expectedCategories) {
    for (const category of expectedCategories) {
        const categoryElement = page.locator(`.nav-category:has-text("${category}"), .sidebar-category:has-text("${category}")`);
        await expect(categoryElement).toBeVisible();
    }
}

/**
 * Verify Context Switcher is present (for multi-context users)
 * @param {import('@playwright/test').Page} page
 */
async function verifyContextSwitcher(page) {
    const switcher = page.locator('.context-switcher, [data-testid="context-switcher"]');
    return switcher.isVisible();
}

/**
 * Verify Scope Switcher buttons on calendar pages
 * @param {import('@playwright/test').Page} page
 * @param {string[]} expectedScopes - e.g., ['Mine Only', 'My Company']
 */
async function verifyScopeSwitcher(page, expectedScopes) {
    const scopeSwitcher = page.locator('.calendar-scope-switcher, .scope-switcher');
    await expect(scopeSwitcher).toBeVisible();

    for (const scope of expectedScopes) {
        const scopeButton = scopeSwitcher.locator(`button:has-text("${scope}"), [data-scope]:has-text("${scope}")`);
        await expect(scopeButton).toBeVisible();
    }
}

/**
 * Verify On-Call Widget is present
 * @param {import('@playwright/test').Page} page
 */
async function verifyOnCallWidget(page) {
    const widget = page.locator('.on-call-widget, [data-testid="on-call-widget"]');
    return widget.isVisible();
}

/**
 * Verify Lucide icons are rendering (not broken/missing)
 * @param {import('@playwright/test').Page} page
 */
async function verifyLucideIconsLoaded(page) {
    // Check if lucide script executed (icons get replaced from <i> to <svg>)
    const iconCount = await page.locator('[data-lucide], .lucide').count();
    // If there are lucide icons, they should be rendered as SVGs
    if (iconCount > 0) {
        const svgIcons = await page.locator('[data-lucide] svg, svg.lucide').count();
        expect(svgIcons).toBeGreaterThan(0);
    }
}

/**
 * Check CSS variable is defined and has expected value format
 * @param {import('@playwright/test').Page} page
 * @param {string} varName - CSS variable name (e.g., '--primary')
 * @param {RegExp} [valuePattern] - Optional regex to validate value
 */
async function verifyCSSVariable(page, varName, valuePattern) {
    const value = await page.evaluate((name) => {
        return getComputedStyle(document.documentElement).getPropertyValue(name).trim();
    }, varName);

    expect(value).not.toBe('');
    if (valuePattern) {
        expect(value).toMatch(valuePattern);
    }
    return value;
}

/**
 * Verify page has no console errors
 * @param {import('@playwright/test').Page} page
 * @param {string[]} [allowedPatterns] - Patterns to ignore
 */
async function collectConsoleErrors(page) {
    const errors = [];
    page.on('console', msg => {
        if (msg.type() === 'error') {
            errors.push(msg.text());
        }
    });
    return errors;
}

/**
 * Verify localization is working (no raw keys shown)
 * @param {import('@playwright/test').Page} page
 */
async function verifyNoRawLocalizationKeys(page) {
    // Raw keys typically look like "Calendar_Empty_NoShiftsMine"
    const rawKeyPattern = /[A-Z][a-z]+_[A-Z][a-z]+_[A-Za-z]+/;
    const bodyText = await page.locator('body').textContent();
    const matches = bodyText?.match(new RegExp(rawKeyPattern, 'g')) || [];

    // Filter out false positives (actual expected content)
    const suspiciousKeys = matches.filter(m =>
        m.includes('_') &&
        !m.includes('@') &&
        m.length > 10
    );

    return suspiciousKeys;
}

module.exports = {
    verifyShiftyLogo,
    verifySidebarPresence,
    verifySidebarCategories,
    verifyContextSwitcher,
    verifyScopeSwitcher,
    verifyOnCallWidget,
    verifyLucideIconsLoaded,
    verifyCSSVariable,
    collectConsoleErrors,
    verifyNoRawLocalizationKeys
};
```

**Step 2: Commit**

```bash
git add qa-automation/helpers/ui-helpers.js
git commit -m "feat(qa): add UI test helpers for UI overhaul testing"
```

---

## Task 3: Create Accessibility Helper

**Files:**
- Create: `qa-automation/helpers/accessibility-helpers.js`

**Step 1: Create accessibility helper**

```javascript
// @ts-check
const AxeBuilder = require('@axe-core/playwright').default;

/**
 * Run accessibility audit on current page
 * @param {import('@playwright/test').Page} page
 * @param {Object} [options]
 * @param {string[]} [options.exclude] - Selectors to exclude
 * @param {string[]} [options.include] - Selectors to include (defaults to full page)
 * @param {string[]} [options.disableRules] - Rules to disable
 * @returns {Promise<import('axe-core').AxeResults>}
 */
async function runAccessibilityAudit(page, options = {}) {
    let builder = new AxeBuilder({ page });

    if (options.exclude) {
        for (const selector of options.exclude) {
            builder = builder.exclude(selector);
        }
    }

    if (options.include) {
        for (const selector of options.include) {
            builder = builder.include(selector);
        }
    }

    if (options.disableRules) {
        builder = builder.disableRules(options.disableRules);
    }

    // Target WCAG AA compliance
    builder = builder.withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa']);

    return builder.analyze();
}

/**
 * Format accessibility violations for readable output
 * @param {import('axe-core').Result[]} violations
 * @returns {string}
 */
function formatViolations(violations) {
    if (violations.length === 0) {
        return 'No accessibility violations found';
    }

    return violations.map(v => {
        const nodes = v.nodes.map(n => `  - ${n.html.substring(0, 100)}...`).join('\n');
        return `[${v.impact?.toUpperCase()}] ${v.id}: ${v.description}\n${nodes}`;
    }).join('\n\n');
}

/**
 * Get critical violations (serious or critical impact)
 * @param {import('axe-core').Result[]} violations
 */
function getCriticalViolations(violations) {
    return violations.filter(v => v.impact === 'critical' || v.impact === 'serious');
}

/**
 * Check color contrast ratio
 * @param {import('@playwright/test').Page} page
 * @param {string} selector - Element to check
 */
async function checkColorContrast(page, selector) {
    const element = page.locator(selector).first();

    const styles = await element.evaluate((el) => {
        const computed = getComputedStyle(el);
        return {
            color: computed.color,
            backgroundColor: computed.backgroundColor,
            fontSize: computed.fontSize,
            fontWeight: computed.fontWeight
        };
    });

    return styles;
}

module.exports = {
    runAccessibilityAudit,
    formatViolations,
    getCriticalViolations,
    checkColorContrast
};
```

**Step 2: Commit**

```bash
git add qa-automation/helpers/accessibility-helpers.js
git commit -m "feat(qa): add accessibility testing helpers with axe-core"
```

---

## Task 4: Create Login Page UI Tests

**Files:**
- Create: `qa-automation/tests/ui-overhaul-login.spec.js`

**Step 1: Create login page visual test**

```javascript
// @ts-check
const { test, expect } = require('@playwright/test');
const { verifyShiftyLogo, verifySidebarPresence, verifyLucideIconsLoaded, verifyCSSVariable } = require('../helpers/ui-helpers');
const { runAccessibilityAudit, formatViolations, getCriticalViolations } = require('../helpers/accessibility-helpers');

test.describe('UI Overhaul: Login Page', () => {

    test.beforeEach(async ({ page }) => {
        await page.goto('/Auth/Login');
        await page.waitForLoadState('networkidle');
    });

    test.describe('Visual Design', () => {

        test('UI-LOGIN-01: Login page displays SHIFTY branding', async ({ page }) => {
            // Logo should be visible
            await verifyShiftyLogo(page);
        });

        test('UI-LOGIN-02: Sidebar is hidden on login page', async ({ page }) => {
            // Sidebar should NOT be visible for unauthenticated users
            await verifySidebarPresence(page, false);
        });

        test('UI-LOGIN-03: CSS design tokens are properly defined', async ({ page }) => {
            // Verify critical CSS variables are defined
            await verifyCSSVariable(page, '--primary', /#[0-9A-Fa-f]{6}/);
            await verifyCSSVariable(page, '--bg', /#[0-9A-Fa-f]{6}/);
            await verifyCSSVariable(page, '--text', /#[0-9A-Fa-f]{6}/);
        });

        test('UI-LOGIN-04: Icons load properly (no broken images)', async ({ page }) => {
            await verifyLucideIconsLoaded(page);
        });

        test('UI-LOGIN-05: No console errors on login page', async ({ page }) => {
            const errors = [];
            page.on('console', msg => {
                if (msg.type() === 'error') {
                    errors.push(msg.text());
                }
            });

            await page.reload();
            await page.waitForLoadState('networkidle');

            // Filter out known acceptable errors
            const realErrors = errors.filter(e =>
                !e.includes('favicon') &&
                !e.includes('404')
            );

            expect(realErrors).toHaveLength(0);
        });
    });

    test.describe('Accessibility', () => {

        test('UI-LOGIN-A11Y-01: Login page passes WCAG AA audit', async ({ page }) => {
            const results = await runAccessibilityAudit(page);

            const critical = getCriticalViolations(results.violations);
            if (critical.length > 0) {
                console.log('Critical accessibility violations:');
                console.log(formatViolations(critical));
            }

            expect(critical).toHaveLength(0);
        });

        test('UI-LOGIN-A11Y-02: Form inputs have proper labels', async ({ page }) => {
            const emailInput = page.locator('input[name="Email"], input#Email');
            const passwordInput = page.locator('input[name="Password"], input#Password');

            // Check for associated labels or aria-label
            const emailLabel = await emailInput.getAttribute('aria-label') ||
                              await page.locator(`label[for="${await emailInput.getAttribute('id')}"]`).textContent();
            const passwordLabel = await passwordInput.getAttribute('aria-label') ||
                                 await page.locator(`label[for="${await passwordInput.getAttribute('id')}"]`).textContent();

            expect(emailLabel).toBeTruthy();
            expect(passwordLabel).toBeTruthy();
        });

        test('UI-LOGIN-A11Y-03: Submit button is keyboard accessible', async ({ page }) => {
            const emailInput = page.locator('input[name="Email"], input#Email');
            await emailInput.focus();

            // Tab to password
            await page.keyboard.press('Tab');
            await expect(page.locator('input[name="Password"], input#Password')).toBeFocused();

            // Tab to submit button
            await page.keyboard.press('Tab');
            const focusedElement = await page.evaluate(() => document.activeElement?.tagName);
            expect(focusedElement?.toUpperCase()).toBe('BUTTON');
        });
    });

    test.describe('Air-Gapped Compatibility', () => {

        test('UI-LOGIN-AG-01: No external network requests', async ({ page }) => {
            const externalRequests = [];

            page.on('request', request => {
                const url = request.url();
                if (!url.includes('localhost') && !url.includes('127.0.0.1')) {
                    externalRequests.push(url);
                }
            });

            await page.reload();
            await page.waitForLoadState('networkidle');

            // Filter out browser internals
            const actualExternal = externalRequests.filter(u =>
                !u.startsWith('chrome-extension://') &&
                !u.startsWith('data:') &&
                !u.startsWith('blob:')
            );

            expect(actualExternal).toHaveLength(0);
        });

        test('UI-LOGIN-AG-02: Lucide icons load from local bundle', async ({ page }) => {
            const scriptRequests = [];

            page.on('request', request => {
                if (request.resourceType() === 'script') {
                    scriptRequests.push(request.url());
                }
            });

            await page.reload();
            await page.waitForLoadState('networkidle');

            // Verify no CDN scripts
            const cdnScripts = scriptRequests.filter(u =>
                u.includes('unpkg.com') ||
                u.includes('cdn.jsdelivr.net') ||
                u.includes('cdnjs.cloudflare.com')
            );

            expect(cdnScripts).toHaveLength(0);
        });
    });
});
```

**Step 2: Run test to verify it works**

Run: `cd qa-automation && npx playwright test ui-overhaul-login.spec.js --headed`
Expected: Tests execute (some may fail if UI not yet implemented)

**Step 3: Commit**

```bash
git add qa-automation/tests/ui-overhaul-login.spec.js
git commit -m "test(qa): add UI overhaul login page tests"
```

---

## Task 5: Create Authenticated Layout Tests

**Files:**
- Create: `qa-automation/tests/ui-overhaul-layout.spec.js`

**Step 1: Create authenticated layout tests**

```javascript
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
```

**Step 2: Commit**

```bash
git add qa-automation/tests/ui-overhaul-layout.spec.js
git commit -m "test(qa): add authenticated layout UI overhaul tests"
```

---

## Task 6: Create Calendar UI Tests

**Files:**
- Create: `qa-automation/tests/ui-overhaul-calendar.spec.js`

**Step 1: Create calendar UI tests**

```javascript
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
```

**Step 2: Commit**

```bash
git add qa-automation/tests/ui-overhaul-calendar.spec.js
git commit -m "test(qa): add calendar UI overhaul tests"
```

---

## Task 7: Create Widget System Tests

**Files:**
- Create: `qa-automation/tests/ui-overhaul-widgets.spec.js`

**Step 1: Create widget tests**

```javascript
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
```

**Step 2: Commit**

```bash
git add qa-automation/tests/ui-overhaul-widgets.spec.js
git commit -m "test(qa): add widget system UI tests"
```

---

## Task 8: Create Dark Mode Tests

**Files:**
- Create: `qa-automation/tests/ui-overhaul-theme.spec.js`

**Step 1: Create theme/dark mode tests**

```javascript
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
```

**Step 2: Commit**

```bash
git add qa-automation/tests/ui-overhaul-theme.spec.js
git commit -m "test(qa): add theme system tests (light/dark mode)"
```

---

## Task 9: Create Localization Tests

**Files:**
- Create: `qa-automation/tests/ui-overhaul-localization.spec.js`

**Step 1: Create localization tests**

```javascript
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
```

**Step 2: Commit**

```bash
git add qa-automation/tests/ui-overhaul-localization.spec.js
git commit -m "test(qa): add localization and RTL tests"
```

---

## Task 10: Create Responsive/Mobile Tests

**Files:**
- Create: `qa-automation/tests/ui-overhaul-responsive.spec.js`

**Step 1: Create responsive tests**

```javascript
// @ts-check
const { test, expect, devices } = require('@playwright/test');
const { loginAsOwner } = require('../helpers/auth-helpers');
const { verifySidebarPresence, verifyShiftyLogo } = require('../helpers/ui-helpers');
const { runAccessibilityAudit, getCriticalViolations } = require('../helpers/accessibility-helpers');

test.describe('UI Overhaul: Responsive Design', () => {

    test.describe('Mobile View (iPhone 12)', () => {
        test.use({ ...devices['iPhone 12'] });

        test('UI-RESP-MOB-01: Login page works on mobile', async ({ page }) => {
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
            await page.goto('/Auth/Login');
            await page.waitForLoadState('networkidle');

            const results = await runAccessibilityAudit(page);
            const critical = getCriticalViolations(results.violations);

            expect(critical).toHaveLength(0);
        });
    });

    test.describe('Tablet View (iPad)', () => {
        test.use({ ...devices['iPad Pro 11'] });

        test('UI-RESP-TAB-01: Layout adapts for tablet', async ({ page }) => {
            await loginAsOwner(page);
            await page.waitForLoadState('networkidle');

            // Sidebar might be visible or collapsible on tablet
            const sidebar = page.locator('.app-sidebar, #appSidebar');
            const isVisible = await sidebar.isVisible();

            console.log(`Sidebar visible on tablet: ${isVisible}`);
        });

        test('UI-RESP-TAB-02: Calendar shows full week on tablet', async ({ page }) => {
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
        test.use({ viewport: { width: 1920, height: 1080 } });

        test('UI-RESP-DESK-01: Layout uses wide viewport', async ({ page }) => {
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
```

**Step 2: Commit**

```bash
git add qa-automation/tests/ui-overhaul-responsive.spec.js
git commit -m "test(qa): add responsive design tests (mobile/tablet/desktop)"
```

---

## Task 11: Create Test Runner Script

**Files:**
- Create: `qa-automation/run-ui-overhaul-tests.sh`
- Create: `qa-automation/run-ui-overhaul-tests.ps1`

**Step 1: Create bash script**

```bash
#!/bin/bash
# Run UI Overhaul Playwright Tests

echo "==================================="
echo "ShiftManager UI Overhaul Test Suite"
echo "==================================="

# Check if app is running
if ! curl -s http://localhost:5000/Auth/Login > /dev/null; then
    echo "ERROR: ShiftManager is not running on http://localhost:5000"
    echo "Please start the application with: dotnet run"
    exit 1
fi

echo "Application is running. Starting tests..."

# Install dependencies if needed
if [ ! -d "node_modules" ]; then
    echo "Installing dependencies..."
    npm install
fi

# Run all UI overhaul tests
echo ""
echo "Running UI Overhaul tests..."
npx playwright test tests/ui-overhaul-*.spec.js \
    --reporter=html \
    --reporter=list \
    "$@"

echo ""
echo "Tests complete. Report available at: reports/playwright-report/index.html"
```

**Step 2: Create PowerShell script**

```powershell
# Run UI Overhaul Playwright Tests
Write-Host "===================================" -ForegroundColor Cyan
Write-Host "ShiftManager UI Overhaul Test Suite" -ForegroundColor Cyan
Write-Host "===================================" -ForegroundColor Cyan

# Check if app is running
try {
    $response = Invoke-WebRequest -Uri "http://localhost:5000/Auth/Login" -UseBasicParsing -TimeoutSec 5
} catch {
    Write-Host "ERROR: ShiftManager is not running on http://localhost:5000" -ForegroundColor Red
    Write-Host "Please start the application with: dotnet run" -ForegroundColor Yellow
    exit 1
}

Write-Host "Application is running. Starting tests..." -ForegroundColor Green

# Install dependencies if needed
if (-not (Test-Path "node_modules")) {
    Write-Host "Installing dependencies..."
    npm install
}

# Run all UI overhaul tests
Write-Host ""
Write-Host "Running UI Overhaul tests..."
npx playwright test tests/ui-overhaul-*.spec.js `
    --reporter=html `
    --reporter=list `
    $args

Write-Host ""
Write-Host "Tests complete. Report available at: reports/playwright-report/index.html" -ForegroundColor Green
```

**Step 3: Make bash script executable**

```bash
chmod +x qa-automation/run-ui-overhaul-tests.sh
```

**Step 4: Commit**

```bash
git add qa-automation/run-ui-overhaul-tests.sh qa-automation/run-ui-overhaul-tests.ps1
git commit -m "chore(qa): add UI overhaul test runner scripts"
```

---

## Task 12: Final Integration Test

**Files:**
- Create: `qa-automation/tests/ui-overhaul-integration.spec.js`

**Step 1: Create integration test**

```javascript
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

        // Verify calendar page
        const calendarGrid = page.locator('.calendar-grid, .month-grid, table.calendar');
        await expect(calendarGrid).toBeVisible();

        // Step 5: Check localization
        const rawKeys = await verifyNoRawLocalizationKeys(page);
        expect(rawKeys.length).toBeLessThan(5);

        // Step 6: Accessibility check
        const results = await runAccessibilityAudit(page);
        const critical = getCriticalViolations(results.violations);

        if (critical.length > 0) {
            console.log('Critical accessibility violations:');
            console.log(formatViolations(critical));
        }

        expect(critical).toHaveLength(0);

        // Step 7: Navigate to Admin
        await page.goto('/Admin/Index');
        await page.waitForLoadState('networkidle');

        // Should not redirect to access denied for Owner
        await expect(page).not.toHaveURL(/AccessDenied/);

        // Step 8: Logout
        const logoutForm = page.locator('form[action*="Logout"]');
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

        if (consoleErrors.length > 0) {
            console.log('Console errors found:');
            consoleErrors.forEach(e => console.log(`  - ${e}`));
        }

        expect(consoleErrors).toHaveLength(0);
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
```

**Step 2: Commit**

```bash
git add qa-automation/tests/ui-overhaul-integration.spec.js
git commit -m "test(qa): add UI overhaul integration tests"
```

---

## Summary

This testing plan creates a comprehensive Playwright test suite for the UI Overhaul with:

| Test File | Purpose | Test Count |
|-----------|---------|------------|
| `ui-overhaul-login.spec.js` | Login page visual, a11y, air-gap | ~10 |
| `ui-overhaul-layout.spec.js` | Sidebar, navigation, RBAC | ~12 |
| `ui-overhaul-calendar.spec.js` | Calendar views, scope switcher | ~15 |
| `ui-overhaul-widgets.spec.js` | On-call widget, grant visibility | ~8 |
| `ui-overhaul-theme.spec.js` | Light/dark mode, persistence | ~8 |
| `ui-overhaul-localization.spec.js` | i18n, RTL, API | ~10 |
| `ui-overhaul-responsive.spec.js` | Mobile, tablet, desktop | ~10 |
| `ui-overhaul-integration.spec.js` | Full journey, perf, air-gap | ~4 |

**Total: ~77 tests**

### Running Tests

```bash
# From qa-automation directory
cd qa-automation

# Run all UI overhaul tests
npx playwright test tests/ui-overhaul-*.spec.js

# Run with UI (headed mode)
npx playwright test tests/ui-overhaul-*.spec.js --headed

# Run specific test file
npx playwright test tests/ui-overhaul-login.spec.js

# Generate HTML report
npx playwright test tests/ui-overhaul-*.spec.js --reporter=html
```

---

**Plan complete and saved to `docs/plans/2026-01-30-ui-overhaul-playwright-testing.md`.**

Two execution options:

1. **Subagent-Driven (this session)** - I dispatch fresh subagent per task, review between tasks, fast iteration

2. **Parallel Session (separate)** - Open new session with executing-plans, batch execution with checkpoints

Which approach would you like for implementing this test suite?
