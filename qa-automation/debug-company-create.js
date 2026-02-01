const { chromium } = require('@playwright/test');
const { loginAsOwner } = require('./helpers/auth-helpers');

(async () => {
  const browser = await chromium.launch({ headless: false });
  const context = await browser.newContext({ baseURL: 'http://localhost:5000' });
  const page = await context.newPage();

  console.log('=== Debug Company Creation ===\n');

  await loginAsOwner(page);
  await page.goto('/Admin/Companies');

  console.log('✅ Navigated to Companies page\n');

  const companyName = `TestCompany_${Date.now()}`;
  const companySlug = `test-${Date.now()}`;

  console.log(`Creating company: ${companyName} / ${companySlug}\n`);

  // Fill form
  await page.fill('input[name="CompanyName"]', companyName);
  await page.fill('input[name="CompanySlug"]', companySlug);

  console.log('✅ Filled form fields\n');

  // Check for error messages or required fields
  page.on('response', response => {
    if (response.url().includes('/Admin/Companies')) {
      console.log(`Response: ${response.status()} ${response.url()}`);
    }
  });

  // Submit
  console.log('Submitting form...\n');
  await page.locator('form:has(input[name="CompanyName"]) button[type="submit"]').click();

  await page.waitForLoadState('networkidle');
  await page.waitForTimeout(1000);

  const url = page.url();
  console.log(`Current URL: ${url}\n`);

  // Check for error messages
  const errors = await page.locator('.alert-danger, .error, .field-validation-error').allTextContents();
  if (errors.length > 0) {
    console.log('❌ Errors found:');
    errors.forEach(e => {
      if (e.trim()) console.log(`  - ${e.trim()}`);
    });
  }

  // Check if company appears in list
  const found = await page.locator(`text="${companyName}"`).count();
  console.log(`\nCompany "${companyName}" in list: ${found > 0 ? '✅ YES' : '❌ NO'} (count: ${found})\n`);

  // List all companies
  const companies = await page.locator('tbody tr td:first-child, .company-list-item').allTextContents();
  console.log(`All companies (${companies.length}):`);
  companies.slice(0, 5).forEach(c => console.log(`  - ${c.trim()}`));

  await page.waitForTimeout(3000);
  await browser.close();
})();
