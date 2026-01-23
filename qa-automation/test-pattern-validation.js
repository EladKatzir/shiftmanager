const { test } = require('@playwright/test');

test('pattern validation test', async ({ page }) => {
    await page.goto('file:///C:/Users/katzi/Downloads/ShiftManager/test-pattern.html');
    await page.waitForLoadState('networkidle');

    const results = await page.evaluate(() => {
        const values = ['UPPERCASE', 'with spaces', 'with.dots', 'with_underscores', 'valid-slug-123'];
        let results = [];

        results.push('Testing pattern="^[a-z0-9-]+$":');
        values.forEach(val => {
            const input = document.getElementById('slug1');
            input.value = val;
            const pattern = input.getAttribute('pattern');
            results.push(`  "${val}" (pattern="${pattern}"): checkValidity()=${input.checkValidity()}, validationMessage="${input.validationMessage}"`);
        });

        results.push('\nTesting pattern="[a-z0-9-]+":');
        values.forEach(val => {
            const input = document.getElementById('slug2');
            input.value = val;
            const pattern = input.getAttribute('pattern');
            results.push(`  "${val}" (pattern="${pattern}"): checkValidity()=${input.checkValidity()}, validationMessage="${input.validationMessage}"`);
        });

        return results.join('\n');
    });

    console.log(results);
});
