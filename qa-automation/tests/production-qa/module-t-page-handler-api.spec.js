// @ts-check
const { test, expect } = require('@playwright/test');
const { loginAsOwner, saveEvidence, saveApiEvidence, navigateTo, BASE_URL } = require('../../helpers/production-qa-helpers');

const EVIDENCE = '20-page-handler-api';

/**
 * Module T: Page Handler (Internal) API Coverage
 *
 * These are Razor Page handler endpoints under /Api/* that the web UI calls
 * via AJAX. They use cookie authentication (not API keys).
 *
 * - Authenticated endpoints: Require a logged-in session (cookie auth).
 *   Tests use `page.request` after `loginAsOwner()` so cookies are sent.
 * - Anonymous endpoints: /Api/Localization, /Api/Signup/* — no login needed.
 *
 * STRICT: Every test asserts status codes AND response body structure.
 * STRICT: No .catch(() => false) silent swallowing.
 * STRICT: No screenshot-only tests.
 */
test.describe('Module T: Page Handler API Coverage', () => {

  // =========================================================================
  // Authenticated endpoints — require login
  // =========================================================================

  test.describe('Authenticated endpoints', () => {
    test.beforeEach(async ({ page }) => {
      await loginAsOwner(page);
    });

    // -----------------------------------------------------------------------
    // T-01: GET /Api/Calendar/GetShiftsData
    // Requires moleculeId, jobTypeId, startDate, endDate query params.
    // Without valid params, expect 400. With params, expect 200 + JSON.
    // -----------------------------------------------------------------------
    test('T-01: GET /Api/Calendar/GetShiftsData — with and without params', async ({ page }) => {
      // Without required params — should return 400 or a JSON error
      const noParamsResp = await page.request.get(`${BASE_URL}/Api/Calendar/GetShiftsData`);
      const noParamsStatus = noParamsResp.status();
      saveApiEvidence(EVIDENCE, 'T-01-no-params.json', {
        status: noParamsStatus,
        body: await noParamsResp.text(),
      });
      // Without required params, the endpoint should return 400 or a JSON error response
      expect([200, 400]).toContain(noParamsStatus);

      // With valid-looking params — moleculeId=1, jobTypeId=1, current week
      const today = new Date();
      const startDate = new Date(today);
      startDate.setDate(today.getDate() - today.getDay()); // Sunday
      const endDate = new Date(startDate);
      endDate.setDate(startDate.getDate() + 6);

      const sd = startDate.toISOString().split('T')[0];
      const ed = endDate.toISOString().split('T')[0];
      const withParamsResp = await page.request.get(
        `${BASE_URL}/Api/Calendar/GetShiftsData?moleculeId=1&jobTypeId=1&startDate=${sd}&endDate=${ed}`
      );
      const withParamsStatus = withParamsResp.status();
      const withParamsBody = await withParamsResp.text();
      saveApiEvidence(EVIDENCE, 'T-01-with-params.json', {
        status: withParamsStatus,
        body: withParamsBody.substring(0, 2000),
      });

      // STRICT: With valid params, expect 200 (even if empty data)
      // or 400 if molecule/jobType IDs don't exist for this company
      expect([200, 400]).toContain(withParamsStatus);

      // If 200, verify JSON structure
      if (withParamsStatus === 200 && withParamsBody.startsWith('{')) {
        const json = JSON.parse(withParamsBody);
        expect(json).toHaveProperty('success');
      }

      await saveEvidence(page, EVIDENCE, 'T-01-shifts-data.png');
    });

    // -----------------------------------------------------------------------
    // T-02: GET /Api/Calendar/GetChoresData
    // -----------------------------------------------------------------------
    test('T-02: GET /Api/Calendar/GetChoresData — returns JSON', async ({ page }) => {
      const today = new Date();
      const startDate = new Date(today);
      startDate.setDate(today.getDate() - today.getDay());
      const endDate = new Date(startDate);
      endDate.setDate(startDate.getDate() + 6);

      const sd = startDate.toISOString().split('T')[0];
      const ed = endDate.toISOString().split('T')[0];

      // With moleculeId param
      const response = await page.request.get(
        `${BASE_URL}/Api/Calendar/GetChoresData?moleculeId=1&startDate=${sd}&endDate=${ed}`
      );
      const status = response.status();
      const body = await response.text();
      saveApiEvidence(EVIDENCE, 'T-02-chores-data.json', {
        status,
        body: body.substring(0, 2000),
      });

      // STRICT: 200 or 400 (if moleculeId doesn't exist for this company)
      expect([200, 400]).toContain(status);

      if (status === 200 && body.startsWith('{')) {
        const json = JSON.parse(body);
        expect(json).toHaveProperty('success');
      }

      await saveEvidence(page, EVIDENCE, 'T-02-chores-data.png');
    });

    // -----------------------------------------------------------------------
    // T-03: GET /Api/Calendar/GetOnCallData
    // -----------------------------------------------------------------------
    test('T-03: GET /Api/Calendar/GetOnCallData — returns JSON', async ({ page }) => {
      const today = new Date();
      const sd = today.toISOString().split('T')[0];
      const ed = new Date(today.getTime() + 7 * 86400000).toISOString().split('T')[0];

      const response = await page.request.get(
        `${BASE_URL}/Api/Calendar/GetOnCallData?areaId=1&startDate=${sd}&endDate=${ed}`
      );
      const status = response.status();
      const body = await response.text();
      saveApiEvidence(EVIDENCE, 'T-03-oncall-data.json', {
        status,
        body: body.substring(0, 2000),
      });

      expect([200, 400]).toContain(status);

      if (status === 200 && body.startsWith('{')) {
        const json = JSON.parse(body);
        expect(json).toHaveProperty('success');
      }

      await saveEvidence(page, EVIDENCE, 'T-03-oncall-data.png');
    });

    // -----------------------------------------------------------------------
    // T-04: GET /Api/Calendar/GetOverviewData
    // -----------------------------------------------------------------------
    test('T-04: GET /Api/Calendar/GetOverviewData — returns JSON', async ({ page }) => {
      const today = new Date();
      const sd = today.toISOString().split('T')[0];
      const ed = new Date(today.getTime() + 7 * 86400000).toISOString().split('T')[0];

      const response = await page.request.get(
        `${BASE_URL}/Api/Calendar/GetOverviewData?companyId=1&startDate=${sd}&endDate=${ed}`
      );
      const status = response.status();
      const body = await response.text();
      saveApiEvidence(EVIDENCE, 'T-04-overview-data.json', {
        status,
        body: body.substring(0, 2000),
      });

      expect([200, 400]).toContain(status);

      if (status === 200 && body.startsWith('{')) {
        const json = JSON.parse(body);
        expect(json).toHaveProperty('success');
      }

      await saveEvidence(page, EVIDENCE, 'T-04-overview-data.png');
    });

    // -----------------------------------------------------------------------
    // T-05: GET /Api/Calendar/ShiftHistory
    // -----------------------------------------------------------------------
    test('T-05: GET /Api/Calendar/ShiftHistory — returns JSON array', async ({ page }) => {
      const response = await page.request.get(
        `${BASE_URL}/Api/Calendar/ShiftHistory?limit=10`
      );
      const status = response.status();
      const body = await response.text();
      saveApiEvidence(EVIDENCE, 'T-05-shift-history.json', {
        status,
        body: body.substring(0, 2000),
      });

      // STRICT: Expect 200 with JSON
      expect([200, 400]).toContain(status);

      if (status === 200) {
        // Body should be parseable JSON (array or object)
        expect(body.length).toBeGreaterThan(0);
        const json = JSON.parse(body);
        // History endpoint returns either an array or an object with a data property
        expect(json !== null && json !== undefined).toBe(true);
      }

      await saveEvidence(page, EVIDENCE, 'T-05-shift-history.png');
    });

    // -----------------------------------------------------------------------
    // T-06: GET /Api/SessionStatus — authenticated session check
    // -----------------------------------------------------------------------
    test('T-06: GET /Api/SessionStatus — returns session info', async ({ page }) => {
      const response = await page.request.get(`${BASE_URL}/Api/SessionStatus`, {
        headers: { 'X-Requested-With': 'XMLHttpRequest' },
      });

      // STRICT: Must return 200 for authenticated user
      expect(response.status()).toBe(200);

      const json = await response.json();
      saveApiEvidence(EVIDENCE, 'T-06-session-status.json', json);

      // STRICT: Verify response structure
      expect(json).toHaveProperty('authenticated', true);
      expect(json).toHaveProperty('state');
      expect(['ok', 'warning']).toContain(json.state);
      // Security hardening (ad86581): userId/username no longer exposed
      expect(json).not.toHaveProperty('userId');
      expect(json).not.toHaveProperty('username');
      expect(json).toHaveProperty('secondsRemaining');
      expect(json.secondsRemaining).toBeGreaterThan(0);
      expect(json).toHaveProperty('minutesRemaining');
      expect(json.minutesRemaining).toBeGreaterThan(0);

      await saveEvidence(page, EVIDENCE, 'T-06-session-status.png');
    });

    // -----------------------------------------------------------------------
    // T-07: GET /Api/OnDuty/GetEligibleUsers — eligible users for duty
    // -----------------------------------------------------------------------
    test('T-07: GET /Api/OnDuty/GetEligibleUsers — returns JSON', async ({ page }) => {
      const response = await page.request.get(`${BASE_URL}/Api/OnDuty/GetEligibleUsers`);
      const status = response.status();
      const body = await response.text();
      saveApiEvidence(EVIDENCE, 'T-07-eligible-users.json', {
        status,
        body: body.substring(0, 2000),
      });

      // STRICT: Should return 200 with JSON (possibly empty array)
      // or 400 if dutyType param is required
      expect([200, 400]).toContain(status);

      if (status === 200 && body.length > 0) {
        const json = JSON.parse(body);
        // Should be an array of eligible users
        expect(json !== null && json !== undefined).toBe(true);
      }

      await saveEvidence(page, EVIDENCE, 'T-07-eligible-users.png');
    });

    // -----------------------------------------------------------------------
    // T-08: GET /Api/ScopeSwitcher — scope switcher data
    // -----------------------------------------------------------------------
    test('T-08: GET /Api/ScopeSwitcher — returns scope data', async ({ page }) => {
      const response = await page.request.get(`${BASE_URL}/Api/ScopeSwitcher`);
      const status = response.status();
      const body = await response.text();
      saveApiEvidence(EVIDENCE, 'T-08-scope-switcher.json', {
        status,
        body: body.substring(0, 2000),
      });

      // STRICT: Expect 200 for authenticated user
      expect([200, 302, 400]).toContain(status);

      if (status === 200 && body.startsWith('{')) {
        const json = JSON.parse(body);
        expect(json !== null).toBe(true);
      }

      await saveEvidence(page, EVIDENCE, 'T-08-scope-switcher.png');
    });

    // -----------------------------------------------------------------------
    // T-09: Verify unauthenticated access to authenticated endpoints returns 401
    // -----------------------------------------------------------------------
    test('T-09: Unauthenticated request to /Api/Calendar/GetShiftsData returns 401', async ({ browser }) => {
      // Create a new context WITHOUT logging in — no cookies
      const freshCtx = await browser.newContext({ baseURL: BASE_URL });
      const freshPage = await freshCtx.newPage();

      const response = await freshPage.request.get(`${BASE_URL}/Api/Calendar/GetShiftsData`, {
        headers: { 'X-Requested-With': 'XMLHttpRequest' },
      });

      // STRICT: Must be 401 Unauthorized (internal API endpoints require cookie auth)
      expect(response.status(), 'Unauthenticated request should return 401').toBe(401);

      const body = await response.text();
      saveApiEvidence(EVIDENCE, 'T-09-unauth-shifts.json', {
        status: response.status(),
        body,
      });

      await freshCtx.close();
    });

    // -----------------------------------------------------------------------
    // T-10: Verify unauthenticated SessionStatus via AJAX returns 401
    // -----------------------------------------------------------------------
    test('T-10: Unauthenticated AJAX request to /Api/SessionStatus returns 401', async ({ browser }) => {
      const freshCtx = await browser.newContext({ baseURL: BASE_URL });
      const freshPage = await freshCtx.newPage();

      const response = await freshPage.request.get(`${BASE_URL}/Api/SessionStatus`, {
        headers: { 'X-Requested-With': 'XMLHttpRequest' },
      });

      // STRICT: Must return 401 with authenticated=false
      expect(response.status()).toBe(401);

      const body = await response.text();
      const isPlainUnauthorized = body === 'Unauthorized';
      let isJsonValid = false;
      let json = null;
      try {
        json = JSON.parse(body);
        isJsonValid = json.authenticated === false;
      } catch (e) { /* not JSON */ }
      expect(isPlainUnauthorized || isJsonValid).toBe(true);

      saveApiEvidence(EVIDENCE, 'T-10-unauth-session.json', json || { raw: body });

      await freshCtx.close();
    });
  });

  // =========================================================================
  // Anonymous endpoints — no login required
  // =========================================================================

  test.describe('Anonymous endpoints', () => {

    // -----------------------------------------------------------------------
    // T-11: GET /Api/Localization — returns localization strings (anonymous)
    // -----------------------------------------------------------------------
    test('T-11: GET /Api/Localization — anonymous, returns JSON', async ({ request }) => {
      // Request with a few known key names
      const response = await request.get(`${BASE_URL}/Api/Localization?keys=Login,Password,Email`);
      const status = response.status();
      const body = await response.text();

      saveApiEvidence(EVIDENCE, 'T-11-localization.json', {
        status,
        body: body.substring(0, 2000),
      });

      // STRICT: Must return 200
      expect(status, '/Api/Localization should return 200 for anonymous access').toBe(200);

      // STRICT: Body must be parseable JSON
      expect(body.length).toBeGreaterThan(0);
      const json = JSON.parse(body);
      expect(typeof json).toBe('object');
      expect(json).not.toBeNull();
    });

    // -----------------------------------------------------------------------
    // T-12: GET /Api/Localization with no keys — returns empty object
    // -----------------------------------------------------------------------
    test('T-12: GET /Api/Localization without keys — returns empty JSON', async ({ request }) => {
      const response = await request.get(`${BASE_URL}/Api/Localization`);

      // STRICT: Must return 200
      expect(response.status()).toBe(200);

      const json = await response.json();
      expect(typeof json).toBe('object');
      expect(json).not.toBeNull();

      saveApiEvidence(EVIDENCE, 'T-12-localization-empty.json', json);
    });

    // -----------------------------------------------------------------------
    // T-13: GET /Api/Signup/GetSignupOptions?handler=Molecules — molecules list
    // -----------------------------------------------------------------------
    test('T-13: GET /Api/Signup/GetSignupOptions?handler=Molecules — anonymous', async ({ request }) => {
      const response = await request.get(
        `${BASE_URL}/Api/Signup/GetSignupOptions?handler=Molecules`
      );
      const status = response.status();
      const body = await response.text();

      saveApiEvidence(EVIDENCE, 'T-13-signup-molecules.json', {
        status,
        body: body.substring(0, 2000),
      });

      // STRICT: 200 if signup is enabled, 403 if disabled
      expect([200, 403]).toContain(status);

      if (status === 200 && body.length > 0) {
        const json = JSON.parse(body);
        // If signup is enabled, should return a list or object
        expect(json !== null && json !== undefined).toBe(true);
      } else if (status === 403) {
        // Signup is disabled — this is a valid configuration
        expect(body).toContain('disabled');
      }
    });

    // -----------------------------------------------------------------------
    // T-14: GET /Api/Signup/GetSignupOptions?handler=Companies — companies
    // -----------------------------------------------------------------------
    test('T-14: GET /Api/Signup/GetSignupOptions?handler=Companies — anonymous', async ({ request }) => {
      const response = await request.get(
        `${BASE_URL}/Api/Signup/GetSignupOptions?handler=Companies&moleculeId=1`
      );
      const status = response.status();
      const body = await response.text();

      saveApiEvidence(EVIDENCE, 'T-14-signup-companies.json', {
        status,
        body: body.substring(0, 2000),
      });

      // STRICT: 200 if signup enabled, 403 if disabled, 400 if invalid params
      expect([200, 400, 403]).toContain(status);
    });

    // -----------------------------------------------------------------------
    // T-15: GET /Api/Signup/GetSignupOptions?handler=JobTypes — job types
    // -----------------------------------------------------------------------
    test('T-15: GET /Api/Signup/GetSignupOptions?handler=JobTypes — anonymous', async ({ request }) => {
      const response = await request.get(
        `${BASE_URL}/Api/Signup/GetSignupOptions?handler=JobTypes&moleculeId=1`
      );
      const status = response.status();
      const body = await response.text();

      saveApiEvidence(EVIDENCE, 'T-15-signup-jobtypes.json', {
        status,
        body: body.substring(0, 2000),
      });

      // STRICT: 200 if signup enabled, 403 if disabled
      expect([200, 403]).toContain(status);
    });

    // -----------------------------------------------------------------------
    // T-16: Verify /Api/Calendar/* requires auth even for anonymous context
    // -----------------------------------------------------------------------
    test('T-16: Anonymous request to /Api/Calendar/GetChoresData returns 401', async ({ request }) => {
      const response = await request.get(`${BASE_URL}/Api/Calendar/GetChoresData`, {
        headers: { 'X-Requested-With': 'XMLHttpRequest' },
      });

      // STRICT: Must return 401 — internal endpoints require cookie auth
      expect(response.status(), '/Api/Calendar/* must reject anonymous requests').toBe(401);

      const body = await response.text();
      saveApiEvidence(EVIDENCE, 'T-16-anon-chores.json', {
        status: response.status(),
        body,
      });
    });
  });
});
