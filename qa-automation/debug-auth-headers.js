const { chromium } = require('@playwright/test');
const { loginAsOwner } = require('./helpers/auth-helpers');

(async () => {
  const browser = await chromium.launch({ headless: false });
  const context = await browser.newContext({
    baseURL: 'http://localhost:5000'
  });
  const page = await context.newPage();

  console.log('=== Debug Authentication Headers ===\n');

  await loginAsOwner(page);
  console.log('✅ Logged in successfully\n');

  const cookies = await context.cookies();
  console.log(`Cookies after login: ${cookies.length}`);
  cookies.forEach(c => console.log(`  - ${c.name}: ${c.value.substring(0, 30)}...`));

  console.log('\n--- Tracking requests to /Admin/Users ---\n');

  page.on('request', request => {
    const headers = request.headers();
    const url = request.url();

    if (url.includes('/Admin/') || url.includes('/api/') || url.includes('/Api/')) {
      console.log(`\nRequest: ${url}`);
      console.log(`  Has 'cookie' header: ${!!headers['cookie']}`);
      console.log(`  Has 'Cookie' header: ${!!headers['Cookie']}`);
      if (headers['cookie']) {
        console.log(`  Cookie value: ${headers['cookie'].substring(0, 50)}...`);
      }
      console.log(`  All headers: ${Object.keys(headers).join(', ')}`);
    }
  });

  await page.goto('http://localhost:5000/Admin/Users');
  await page.waitForLoadState('networkidle');

  console.log('\n✅ Done');

  await page.waitForTimeout(2000);
  await browser.close();
})();
