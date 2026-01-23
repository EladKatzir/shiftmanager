const { chromium } = require('@playwright/test');

(async () => {
  const browser = await chromium.launch({ headless: false });
  const context = await browser.newContext();
  const page = await context.newPage();

  console.log('=== Auth Session Investigation ===\n');

  // Go to login page
  await page.goto('http://localhost:5000/Auth/Login');
  console.log('1. At login page');

  // Check cookies before login
  let cookies = await context.cookies();
  console.log(`\n2. Cookies BEFORE login: ${cookies.length}`);
  cookies.forEach(c => console.log(`   - ${c.name}: ${c.value.substring(0, 20)}...`));

  // Fill and submit login form
  await page.fill('input[name="Email"]', 'owner@local');
  await page.fill('input[name="Password"]', '123456');

  console.log('\n3. Submitting login form...');
  await page.locator('form:has(input[name="Email"]) button[type="submit"]').click();
  await page.waitForLoadState('networkidle');
  await page.waitForTimeout(2000);

  // Check URL after login
  const currentUrl = page.url();
  console.log(`\n4. URL after login: ${currentUrl}`);

  // Check cookies after login
  cookies = await context.cookies();
  console.log(`\n5. Cookies AFTER login: ${cookies.length}`);
  cookies.forEach(c => console.log(`   - ${c.name}: ${c.value.substring(0, 50)}... (expires: ${new Date(c.expires * 1000).toISOString()})`));

  // Try to navigate to protected page
  console.log('\n6. Navigating to /Admin/Users...');
  const response = await page.goto('http://localhost:5000/Admin/Users');
  console.log(`   Status: ${response.status()}`);
  console.log(`   Final URL: ${page.url()}`);

  // Check if redirected to login
  if (page.url().includes('/Auth/Login')) {
    console.log('\n❌ PROBLEM: Redirected back to login! Session not working.');
    console.log('   This explains the duplicate requests - page loads on login, then redirects');
  } else {
    console.log('\n✅ Successfully accessed protected page');
  }

  await page.waitForTimeout(2000);
  await browser.close();
})();
