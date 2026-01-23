const { chromium } = require('@playwright/test');

(async () => {
  const browser = await chromium.launch({ headless: false });
  const page = await browser.newPage();

  const allRequests = [];
  const navigations = [];

  // Track ALL requests with timestamps
  page.on('request', request => {
    allRequests.push({
      timestamp: Date.now(),
      url: request.url(),
      method: request.method(),
      resourceType: request.resourceType()
    });
  });

  // Track navigations
  page.on('framenavigated', frame => {
    if (frame === page.mainFrame()) {
      navigations.push({
        timestamp: Date.now(),
        url: frame.url()
      });
      console.log(`[NAVIGATION ${navigations.length}] ${frame.url()}`);
    }
  });

  // Track response redirects
  page.on('response', async response => {
    const status = response.status();
    if (status >= 300 && status < 400) {
      console.log(`[REDIRECT ${status}] ${response.url()} -> ${response.headers()['location'] || 'unknown'}`);
    }
  });

  console.log('=== Starting Investigation ===\n');

  // Login
  await page.goto('http://localhost:5000/Auth/Login');
  await page.fill('input[name="Email"]', 'owner@local');
  await page.fill('input[name="Password"]', '123456');

  console.log('\n[ACTION] Clicking login button...\n');
  await page.click('button[type="submit"]');
  await page.waitForLoadState('networkidle');

  // Clear tracking for main test
  allRequests.length = 0;
  navigations.length = 0;

  console.log('\n=== Navigating to /Admin/Users ===\n');
  await page.goto('http://localhost:5000/Admin/Users');
  await page.waitForLoadState('networkidle');

  console.log(`\n=== Analysis ===`);
  console.log(`Total navigations: ${navigations.length}`);
  navigations.forEach((nav, i) => {
    console.log(`  ${i + 1}. ${nav.url}`);
  });

  console.log(`\nTotal requests: ${allRequests.length}`);

  // Find duplicates
  const urlCounts = new Map();
  allRequests.forEach(req => {
    const key = `${req.method}:${req.url}`;
    if (!urlCounts.has(key)) {
      urlCounts.set(key, []);
    }
    urlCounts.get(key).push(req.timestamp);
  });

  console.log(`\n=== Duplicates (loaded more than once) ===`);
  let duplicateCount = 0;
  for (const [key, timestamps] of urlCounts.entries()) {
    if (timestamps.length > 1) {
      duplicateCount++;
      const url = key.split(':', 2)[1];
      const shortUrl = url.replace('http://localhost:5000', '').substring(0, 80);
      console.log(`\n${duplicateCount}. ${shortUrl}`);
      console.log(`   Loaded ${timestamps.length} times:`);
      timestamps.forEach((ts, i) => {
        const relativeTime = i === 0 ? '0ms' : `+${ts - timestamps[0]}ms`;
        console.log(`   - Load ${i + 1}: ${relativeTime}`);
      });
    }
  }

  console.log(`\n=== Summary ===`);
  console.log(`Navigations: ${navigations.length}`);
  console.log(`Total requests: ${allRequests.length}`);
  console.log(`Duplicate requests: ${duplicateCount}`);

  await page.waitForTimeout(2000);
  await browser.close();
})();
