const { chromium } = require('@playwright/test');

(async () => {
  const browser = await chromium.launch({ headless: false });
  const page = await browser.newPage();

  console.log('=== Debug Login Failure ===\n');

  await page.goto('http://localhost:5000/Auth/Login');

  // Fill form
  await page.fill('input[name="Email"]', 'owner@local');
  await page.fill('input[name="Password"]', '123456');

  console.log('Submitting...');
  await page.locator('form:has(input[name="Email"]) button[type="submit"]').click();
  await page.waitForLoadState('networkidle');
  await page.waitForTimeout(1000);

  // Check for error messages
  const errorText = await page.locator('.alert-danger, .error, [class*="error"]').allTextContents();
  if (errorText.length > 0) {
    console.log('\n❌ Error messages found:');
    errorText.forEach(text => {
      if (text.trim()) console.log(`   ${text.trim()}`);
    });
  }

  // Check if validation errors
  const validationErrors = await page.locator('.field-validation-error, .validation-summary-errors').allTextContents();
  if (validationErrors.length > 0) {
    console.log('\n❌ Validation errors:');
    validationErrors.forEach(text => {
      if (text.trim()) console.log(`   ${text.trim()}`);
    });
  }

  // Check page content for clues
  const pageText = await page.textContent('body');
  if (pageText.toLowerCase().includes('invalid') || pageText.toLowerCase().includes('incorrect')) {
    console.log('\n❌ Page mentions "invalid" or "incorrect"');
    const lines = pageText.split('\n').filter(l =>
      l.toLowerCase().includes('invalid') || l.toLowerCase().includes('incorrect')
    );
    lines.forEach(l => console.log(`   ${l.trim().substring(0, 100)}`));
  }

  // Take screenshot
  await page.screenshot({ path: 'reports/login-debug.png', fullPage: true });
  console.log('\n📸 Screenshot saved to reports/login-debug.png');

  console.log(`\nCurrent URL: ${page.url()}`);

  await page.waitForTimeout(3000);
  await browser.close();
})();
