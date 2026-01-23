const { chromium } = require('@playwright/test');

(async () => {
  const browser = await chromium.launch({ headless: false });
  const context = await browser.newContext();
  const page = await context.newPage();

  console.log('=== Testing owner@test.com Login ===\n');

  await page.goto('http://localhost:5000/Auth/Login');
  await page.fill('input[name="Email"]', 'owner@test.com');
  await page.fill('input[name="Password"]', '123456');

  console.log('Submitting login...');
  await page.locator('form:has(input[name="Email"]) button[type="submit"]').click();
  await page.waitForLoadState('networkidle');

  const url = page.url();
  console.log(`URL after login: ${url}`);

  // Check cookies
  const cookies = await context.cookies();
  console.log(`\nCookies: ${cookies.length}`);
  cookies.forEach(c => {
    console.log(`  - ${c.name}: ${c.value.substring(0, 50)}...`);
  });

  // Test protected page access
  console.log('\nNavigating to /Admin/Users...');
  await page.goto('http://localhost:5000/Admin/Users');
  await page.waitForLoadState('networkidle');

  const finalUrl = page.url();
  console.log(`Final URL: ${finalUrl}`);

  if (finalUrl.includes('/Admin/Users') && !finalUrl.includes('Login')) {
    console.log('\n✅ SUCCESS: Login working! Can access protected pages.');
  } else {
    console.log('\n❌ FAILED: Still redirected to login');
  }

  await page.waitForTimeout(2000);
  await browser.close();
})();
