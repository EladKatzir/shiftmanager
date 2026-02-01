// @ts-check

/**
 * Wait for element and scroll into view if needed
 * @param {import('@playwright/test').Page} page - Playwright page
 * @param {string} selector - Element selector
 * @param {object} options - Wait options
 * @param {number} [options.timeout=10000] - Timeout in milliseconds
 * @returns {Promise<import('@playwright/test').Locator>} The element locator
 */
async function waitAndScrollToElement(page, selector, options = {}) {
    const element = page.locator(selector);

    // Wait for element to be attached to DOM
    await element.waitFor({ state: 'attached', timeout: options.timeout || 10000 });

    // Scroll element into view
    await element.scrollIntoViewIfNeeded();

    // Wait for element to be visible
    await element.waitFor({ state: 'visible', timeout: 2000 });

    return element;
}

/**
 * Scroll page to find element (try multiple scroll positions)
 * @param {import('@playwright/test').Page} page - Playwright page
 * @param {string} selector - Element selector
 * @param {number} [maxAttempts=3] - Maximum scroll attempts
 * @returns {Promise<boolean>} Whether element was found
 */
async function scrollToFindElement(page, selector, maxAttempts = 3) {
    for (let i = 0; i < maxAttempts; i++) {
        if (await page.locator(selector).isVisible().catch(() => false)) {
            return true;
        }

        // Scroll down in increments
        await page.evaluate((increment) => {
            window.scrollBy(0, increment);
        }, 300);

        await page.waitForTimeout(200);
    }

    return false;
}

/**
 * Navigate to a page and wait for full load with loading indicators
 * @param {import('@playwright/test').Page} page - Playwright page
 * @param {string} url - URL to navigate to
 * @param {object} options - Navigation options
 * @param {string} [options.pageTitle] - Expected page title text to verify
 * @param {number} [options.timeout=10000] - Timeout in milliseconds
 */
async function navigateAndWaitForLoad(page, url, options = {}) {
    await page.goto(url);
    await page.waitForLoadState('networkidle');

    // Wait for any loading indicators to disappear
    const loader = page.locator('.loading, .spinner, [data-loading="true"]');
    if (await loader.isVisible().catch(() => false)) {
        await loader.waitFor({ state: 'hidden', timeout: options.timeout || 10000 });
    }

    // Verify we're on the expected page if title provided
    if (options.pageTitle) {
        await page.locator('h1').filter({ hasText: new RegExp(options.pageTitle, 'i') }).waitFor({
            state: 'visible',
            timeout: 5000
        });
    }

    // Scroll to ensure full page is loaded/visible
    await page.evaluate(() => window.scrollTo(0, document.body.scrollHeight));
    await page.waitForTimeout(500); // Brief pause for any lazy-loaded elements
    await page.evaluate(() => window.scrollTo(0, 0));
}

/**
 * Create a company with isolated test data
 * Prevents conflicts between parallel tests
 * @param {import('@playwright/test').Page} page
 * @returns {Promise<Object>} Created company data
 */
async function createTestCompany(page) {
    const { createIsolatedTestData } = require('./database-helper');
    const testData = createIsolatedTestData();
    const company = testData.company;

    await page.goto('/Admin/Companies');
    await page.waitForLoadState('networkidle');

    // Fill company form with isolated data
    await page.fill('input[name="CompanyName"]', company.name);
    await page.fill('input[name="CompanySlug"]', company.slug);
    await page.fill('input[name="CompanyDisplayName"]', company.displayName || company.name);

    // Fill manager info
    await page.fill('input[name="ManagerEmail"]', company.managerEmail);
    await page.fill('input[name="ManagerDisplayName"]', company.managerName);
    await page.fill('input[name="ManagerPassword"]', company.managerPassword);

    // Submit form
    await page.locator('form:has(input[name="CompanyName"]) button[type="submit"]').click();
    await page.waitForLoadState('networkidle');

    // Wait for company to appear in list
    await page.locator(`text="${company.name}"`).first().waitFor({ timeout: 5000 }).catch(() => {});

    return {
        ...company,
        uniqueId: testData.uniqueId
    };
}

/**
 * Delete a company by name
 * @param {import('@playwright/test').Page} page
 * @param {string} companyName
 */
async function deleteTestCompany(page, companyName) {
    await page.goto('/Admin/Companies');
    await page.waitForLoadState('networkidle');

    const row = page.locator(`tbody tr:has-text("${companyName}")`).first();
    const deleteButton = row.locator('button:has-text("Delete"), form[action*="Delete"] button').first();

    if (await deleteButton.isVisible({ timeout: 2000 }).catch(() => false)) {
        // Set up dialog handler
        page.once('dialog', async dialog => await dialog.accept());

        await deleteButton.click();
        await page.waitForLoadState('networkidle');
    }
}

module.exports = {
    waitAndScrollToElement,
    scrollToFindElement,
    navigateAndWaitForLoad,
    createTestCompany,
    deleteTestCompany
};
