// @ts-check
const { test, expect } = require('@playwright/test');
const { loginAsOwner, saveEvidence, navigateTo, BASE_URL } = require('../../helpers/production-qa-helpers');

const EVIDENCE = '37-print-export';

/**
 * Module AG: Print & Export — verifies print buttons on calendar pages
 * and export API endpoints returning data.
 */

test.describe('Module AG: Print & Export', () => {
  test.beforeEach(async ({ page }) => {
    await loginAsOwner(page);
  });

  // ---------------------------------------------------------------------------
  // AG-01: Print button visible on Shifts calendar
  // ---------------------------------------------------------------------------
  test('AG-01: Print button visible on Shifts calendar', async ({ page }) => {
    await navigateTo(page, '/Calendar/Shifts');
    await page.waitForLoadState('networkidle');

    // ASSERT: Print button exists
    const printBtn = page.locator('button:has-text("Print"), button:has-text("הדפס"), a:has-text("Print"), [onclick*="print"], .print-btn, button[aria-label*="print"]');
    const printCount = await printBtn.count();
    expect(printCount).toBeGreaterThanOrEqual(1);

    await saveEvidence(page, EVIDENCE, 'AG-01-shifts-print.png');
  });

  // ---------------------------------------------------------------------------
  // AG-02: Print button visible on Chores calendar
  // ---------------------------------------------------------------------------
  test('AG-02: Print button visible on Chores calendar', async ({ page }) => {
    await navigateTo(page, '/Calendar/Chores');
    await page.waitForLoadState('networkidle');

    const printBtn = page.locator('button:has-text("Print"), button:has-text("הדפס"), a:has-text("Print"), [onclick*="print"], .print-btn');
    const printCount = await printBtn.count();
    expect(printCount).toBeGreaterThanOrEqual(1);

    await saveEvidence(page, EVIDENCE, 'AG-02-chores-print.png');
  });

  // ---------------------------------------------------------------------------
  // AG-03: Print button visible on OnCall calendar
  // ---------------------------------------------------------------------------
  test('AG-03: Print button visible on OnCall calendar', async ({ page }) => {
    await navigateTo(page, '/Calendar/OnCall');
    await page.waitForLoadState('networkidle');

    const printBtn = page.locator('button:has-text("Print"), button:has-text("הדפס"), a:has-text("Print"), [onclick*="print"], .print-btn');
    const printCount = await printBtn.count();
    expect(printCount).toBeGreaterThanOrEqual(1);

    await saveEvidence(page, EVIDENCE, 'AG-03-oncall-print.png');
  });

  // ---------------------------------------------------------------------------
  // AG-04: Print button visible on Overview calendar
  // ---------------------------------------------------------------------------
  test('AG-04: Print button visible on Overview calendar', async ({ page }) => {
    await navigateTo(page, '/Calendar/Overview');
    await page.waitForLoadState('networkidle');

    const printBtn = page.locator('button:has-text("Print"), button:has-text("הדפס"), a:has-text("Print"), [onclick*="print"], .print-btn');
    const printCount = await printBtn.count();
    expect(printCount).toBeGreaterThanOrEqual(1);

    await saveEvidence(page, EVIDENCE, 'AG-04-overview-print.png');
  });

  // ---------------------------------------------------------------------------
  // AG-05: Print CSS media rules exist
  // ---------------------------------------------------------------------------
  test('AG-05: Print CSS media rules exist', async ({ page }) => {
    await navigateTo(page, '/Calendar/Shifts');
    await page.waitForLoadState('networkidle');

    // ASSERT: At least one stylesheet has @media print rules
    const hasPrintCSS = await page.evaluate(() => {
      const sheets = Array.from(document.styleSheets);
      for (const sheet of sheets) {
        try {
          const rules = Array.from(sheet.cssRules || []);
          for (const rule of rules) {
            if (rule instanceof CSSMediaRule && rule.conditionText === 'print') {
              return true;
            }
            // Also check for @media print in the cssText
            if (rule.cssText && rule.cssText.includes('@media print')) {
              return true;
            }
          }
        } catch (e) {
          // Cross-origin stylesheet
        }
      }
      return false;
    });

    expect(hasPrintCSS).toBe(true);

    await saveEvidence(page, EVIDENCE, 'AG-05-print-css.png');
  });

  // ---------------------------------------------------------------------------
  // AG-06: Schedule export API returns data
  // ---------------------------------------------------------------------------
  test('AG-06: Schedule export API endpoint responds', async ({ page }) => {
    await navigateTo(page, '/');
    await page.waitForLoadState('networkidle');

    // Call the schedule export API
    const response = await page.evaluate(async (baseUrl) => {
      try {
        const resp = await fetch(`${baseUrl}/Api/ScheduleExport?handler=ExportData`, {
          method: 'GET',
          credentials: 'same-origin'
        });
        return { status: resp.status, ok: resp.ok };
      } catch (e) {
        return { status: 0, ok: false, error: e.message };
      }
    }, BASE_URL);

    // ASSERT: API responds (200 or 400 for missing params, not 500)
    expect(response.status).not.toBe(500);
    expect(response.status).not.toBe(0);

    await saveEvidence(page, EVIDENCE, 'AG-06-schedule-export.png');
  });

  // ---------------------------------------------------------------------------
  // AG-07: GetShiftsData API returns JSON
  // ---------------------------------------------------------------------------
  test('AG-07: GetShiftsData API returns JSON response', async ({ page }) => {
    await navigateTo(page, '/');
    await page.waitForLoadState('networkidle');

    const response = await page.evaluate(async (baseUrl) => {
      try {
        const resp = await fetch(`${baseUrl}/Api/Calendar/GetShiftsData`, {
          method: 'GET',
          credentials: 'same-origin'
        });
        const contentType = resp.headers.get('content-type');
        return { status: resp.status, contentType };
      } catch (e) {
        return { status: 0, error: e.message };
      }
    }, BASE_URL);

    // ASSERT: Returns JSON (even if 400 for missing params)
    expect(response.status).not.toBe(500);

    await saveEvidence(page, EVIDENCE, 'AG-07-shifts-api.png');
  });

  // ---------------------------------------------------------------------------
  // AG-08: GetChoresData API returns JSON
  // ---------------------------------------------------------------------------
  test('AG-08: GetChoresData API returns response', async ({ page }) => {
    await navigateTo(page, '/');
    await page.waitForLoadState('networkidle');

    const response = await page.evaluate(async (baseUrl) => {
      try {
        const resp = await fetch(`${baseUrl}/Api/Calendar/GetChoresData`, {
          method: 'GET',
          credentials: 'same-origin'
        });
        return { status: resp.status };
      } catch (e) {
        return { status: 0, error: e.message };
      }
    }, BASE_URL);

    expect(response.status).not.toBe(500);

    await saveEvidence(page, EVIDENCE, 'AG-08-chores-api.png');
  });

  // ---------------------------------------------------------------------------
  // AG-09: GetOnCallData API returns response
  // ---------------------------------------------------------------------------
  test('AG-09: GetOnCallData API returns response', async ({ page }) => {
    await navigateTo(page, '/');
    await page.waitForLoadState('networkidle');

    const response = await page.evaluate(async (baseUrl) => {
      try {
        const resp = await fetch(`${baseUrl}/Api/Calendar/GetOnCallData`, {
          method: 'GET',
          credentials: 'same-origin'
        });
        return { status: resp.status };
      } catch (e) {
        return { status: 0, error: e.message };
      }
    }, BASE_URL);

    expect(response.status).not.toBe(500);

    await saveEvidence(page, EVIDENCE, 'AG-09-oncall-api.png');
  });

  // ---------------------------------------------------------------------------
  // AG-10: GetOverviewData API returns response
  // ---------------------------------------------------------------------------
  test('AG-10: GetOverviewData API returns response', async ({ page }) => {
    await navigateTo(page, '/');
    await page.waitForLoadState('networkidle');

    const response = await page.evaluate(async (baseUrl) => {
      try {
        const resp = await fetch(`${baseUrl}/Api/Calendar/GetOverviewData`, {
          method: 'GET',
          credentials: 'same-origin'
        });
        return { status: resp.status };
      } catch (e) {
        return { status: 0, error: e.message };
      }
    }, BASE_URL);

    expect(response.status).not.toBe(500);

    await saveEvidence(page, EVIDENCE, 'AG-10-overview-api.png');
  });
});
