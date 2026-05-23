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
        const categoryElement = page.locator(`.nav-category:has-text("${category}"), .nav-section-header:has-text("${category}"), .sidebar-category:has-text("${category}")`);
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
    const scopeSwitcher = page.locator('.calendar-scope-switcher, .scope-switcher, .calendar-view-switcher');
    await expect(scopeSwitcher).toBeVisible();

    for (const scope of expectedScopes) {
        const scopeButton = scopeSwitcher.locator(`button:has-text("${scope}"), a:has-text("${scope}"), [data-scope]:has-text("${scope}")`);
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
    // Use innerText (rendered, VISIBLE text) rather than textContent: textContent also includes
    // inline <script> content, e.g. the window.AppLocalizer i18n dictionary emitted by
    // _LocalizationScript.cshtml whose object KEYS (Justice_Panel_*, Conflict_*) are localized
    // correctly but are not user-visible. We only want to flag raw keys actually shown to the user.
    const bodyText = await page.locator('body').innerText();
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
