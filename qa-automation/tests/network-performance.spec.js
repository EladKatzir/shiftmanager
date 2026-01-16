// @ts-check
const { test, expect } = require('@playwright/test');
const { loginAsOwner } = require('../helpers/auth-helpers');
const fs = require('fs');
const path = require('path');

let performanceData = {
  screens: {},
  workflows: {},
  slowEndpoints: [],
  errors: [],
  polling: {}
};

test.describe('Network Performance & Efficiency', () => {

  test.afterAll(() => {
    // Write performance report
    const reportsDir = path.join(__dirname, '..', 'reports');
    if (!fs.existsSync(reportsDir)) {
      fs.mkdirSync(reportsDir, { recursive: true });
    }
    fs.writeFileSync(
      path.join(reportsDir, 'efficiency-report.json'),
      JSON.stringify(performanceData, null, 2)
    );
    console.log('\n📊 Performance report saved to qa-automation/reports/efficiency-report.json');
  });

  test('P9-01: Measure request count - Admin/Index', async ({ page }) => {
    const requests = [];

    page.on('request', request => {
      requests.push({
        url: request.url(),
        method: request.method(),
        resourceType: request.resourceType(),
        timestamp: Date.now()
      });
    });

    await loginAsOwner(page);

    const startTime = Date.now();
    await page.goto('/Admin/Index');
    await page.waitForLoadState('networkidle');
    const loadTime = Date.now() - startTime;

    const apiRequests = requests.filter(r =>
      r.url.includes('/api/') || r.url.includes('/Api/') || r.url.includes('/Admin/')
    );

    performanceData.screens['Admin/Index'] = {
      totalRequests: requests.length,
      apiRequests: apiRequests.length,
      loadTime: loadTime,
      requests: apiRequests.map(r => ({ url: r.url, method: r.method }))
    };

    console.log(`Admin/Index: ${apiRequests.length} API requests, ${loadTime}ms load time`);

    // Efficiency budgets
    expect(apiRequests.length).toBeLessThan(20); // Max 20 API calls
    expect(loadTime).toBeLessThan(5000); // Max 5 seconds
  });

  test('P9-02: Measure request count - Calendar/Table', async ({ page }) => {
    const requests = [];

    page.on('request', request => {
      requests.push({
        url: request.url(),
        method: request.method(),
        resourceType: request.resourceType()
      });
    });

    await loginAsOwner(page);

    const startTime = Date.now();
    await page.goto('/Calendar/Table');
    await page.waitForLoadState('networkidle');
    const loadTime = Date.now() - startTime;

    const apiRequests = requests.filter(r =>
      r.url.includes('/api/') || r.url.includes('/Api/') || r.url.includes('/Calendar/')
    );

    performanceData.screens['Calendar/Table'] = {
      totalRequests: requests.length,
      apiRequests: apiRequests.length,
      loadTime: loadTime
    };

    console.log(`Calendar/Table: ${apiRequests.length} API requests, ${loadTime}ms load time`);

    // Calendar is complex, allow more requests
    expect(apiRequests.length).toBeLessThan(30);
    expect(loadTime).toBeLessThan(8000);
  });

  test('P9-03: Detect duplicate requests', async ({ page }) => {
    const requests = [];

    page.on('request', request => {
      requests.push({
        url: request.url(),
        method: request.method()
      });
    });

    await loginAsOwner(page);
    await page.goto('/Admin/Users');
    await page.waitForLoadState('networkidle');

    // Find duplicates
    const seen = new Map();
    const duplicates = [];

    for (const req of requests) {
      const key = `${req.method}:${req.url}`;
      if (seen.has(key)) {
        duplicates.push(req);
      } else {
        seen.set(key, true);
      }
    }

    performanceData.screens['Admin/Users'] = {
      totalRequests: requests.length,
      duplicateRequests: duplicates.length,
      duplicates: duplicates.map(d => d.url)
    };

    console.log(`Duplicate requests: ${duplicates.length}`);
    expect(duplicates.length).toBeLessThan(5); // Allow some cache misses
  });

  test('P9-04: Detect unexpected polling', async ({ page }) => {
    const requests = [];

    page.on('request', request => {
      requests.push({
        url: request.url(),
        timestamp: Date.now()
      });
    });

    await loginAsOwner(page);
    await page.goto('/My/Index');

    // Wait 5 seconds to detect polling
    await page.waitForTimeout(5000);

    // Check for repeated requests to same URL
    const urlCounts = new Map();
    for (const req of requests) {
      urlCounts.set(req.url, (urlCounts.get(req.url) || 0) + 1);
    }

    const pollingUrls = [];
    for (const [url, count] of urlCounts.entries()) {
      if (count > 2 && url.includes('/api/')) {
        pollingUrls.push({ url, count });
      }
    }

    performanceData.polling = {
      detected: pollingUrls.length > 0,
      urls: pollingUrls
    };

    console.log(`Polling URLs detected: ${pollingUrls.length}`);
  });

  test('P9-05: Identify slow endpoints', async ({ page }) => {
    const slowEndpoints = [];

    page.on('response', async response => {
      const request = response.request();
      const timing = response.timing();
      const responseTime = timing.responseEnd - timing.requestStart;

      if (responseTime > 1000 && request.url().includes('/')) {
        slowEndpoints.push({
          url: request.url(),
          status: response.status(),
          time: Math.round(responseTime),
          size: parseInt(response.headers()['content-length'] || '0')
        });
      }
    });

    await loginAsOwner(page);
    await page.goto('/Admin/Analytics').catch(() => page.goto('/Admin/Index'));
    await page.waitForLoadState('networkidle');

    performanceData.slowEndpoints = slowEndpoints;

    console.log(`Slow endpoints (>1s): ${slowEndpoints.length}`);
    slowEndpoints.forEach(e => console.log(`  ${e.url}: ${e.time}ms`));
  });

  test('P9-06: Detect 4xx/5xx errors', async ({ page }) => {
    const errors = [];

    page.on('response', response => {
      const status = response.status();
      if (status >= 400) {
        errors.push({
          url: response.url(),
          status: status,
          statusText: response.statusText(),
          timestamp: new Date().toISOString()
        });
      }
    });

    await loginAsOwner(page);

    const screens = [
      '/Admin/Index',
      '/Calendar/Table',
      '/Admin/Users',
      '/My/Index'
    ];

    for (const screen of screens) {
      await page.goto(`http://localhost:5000${screen}`);
      await page.waitForLoadState('networkidle');
    }

    performanceData.errors = errors;

    console.log(`HTTP errors detected: ${errors.length}`);
    errors.forEach(e => console.log(`  ${e.status} ${e.url}`));

    // Should have minimal errors during normal navigation
    expect(errors.filter(e => e.status >= 500).length).toBe(0); // No 5xx errors
  });

  test('P9-07: Measure full workflow - Create Company', async ({ page }) => {
    const requests = [];

    page.on('request', request => {
      if (request.url().includes('/api/') || request.url().includes('/Admin/')) {
        requests.push({
          url: request.url(),
          method: request.method(),
          timestamp: Date.now()
        });
      }
    });

    await loginAsOwner(page);

    const workflowStart = Date.now();

    // Step 1: Navigate to Companies
    await page.goto('/Admin/Companies');
    await page.waitForLoadState('networkidle');

    // Step 2: Open create form
    await page.click('a:has-text("Create"), button:has-text("Add")');
    await page.waitForLoadState('networkidle');

    // Step 3: Fill and submit
    await page.fill('input[name="Name"]', `Workflow_${Date.now()}`);
    await page.fill('input[name="Slug"]', `workflow-${Date.now()}`);
    await page.click('button[type="submit"]');
    await page.waitForLoadState('networkidle');

    const workflowTime = Date.now() - workflowStart;

    performanceData.workflows['CreateCompany'] = {
      totalRequests: requests.length,
      totalTime: workflowTime,
      requests: requests
    };

    console.log(`Create Company workflow: ${requests.length} requests, ${workflowTime}ms total`);
    expect(workflowTime).toBeLessThan(10000); // Max 10 seconds for full workflow
  });
});
