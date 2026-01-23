const { chromium } = require('@playwright/test');

(async () => {
    const browser = await chromium.launch({ headless: false });
    const page = await browser.newPage();

    // Login as owner
    await page.goto('http://localhost:5000/Auth/Login');
    await page.fill('input[name="Email"]', 'owner@local');
    await page.fill('input[name="Password"]', '123456');
    await page.click('button[type="submit"]');
    await page.waitForLoadState('networkidle');
    await page.waitForTimeout(2000);

    // Navigate to companies page
    await page.goto('http://localhost:5000/Admin/Companies');
    await page.waitForLoadState('networkidle');

    // Get the actual pattern attribute value
    const pattern = await page.getAttribute('input[name="CompanySlug"]', 'pattern');
    console.log('Pattern attribute value:', pattern);

    // Test validation
    await page.fill('input[name="CompanySlug"]', 'UPPERCASE');
    const isValid = await page.evaluate(() => {
        const input = document.querySelector('input[name="CompanySlug"]');
        return {
            value: input.value,
            pattern: input.getAttribute('pattern'),
            checkValidity: input.checkValidity(),
            validationMessage: input.validationMessage
        };
    });

    console.log('Validation result:', JSON.stringify(isValid, null, 2));

    await browser.close();
})();
