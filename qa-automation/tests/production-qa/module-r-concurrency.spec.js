// @ts-check
const { test, expect } = require('@playwright/test');
const { loginAsOwner, login, saveEvidence, navigateTo, TEST_PASSWORD, BASE_URL } = require('../../helpers/production-qa-helpers');

const EVIDENCE = '18-concurrency';

/**
 * Module R: Concurrency Tests
 *
 * Uses browser.newContext() for independent sessions (separate cookie jars).
 * Every test asserts real outcomes — no screenshot-only tests.
 * Login failures throw with explicit messages, never silently skip.
 */
test.describe('Module R: Concurrency', () => {

  // -------------------------------------------------------------------------
  // Helper: create a new browser context, open a page, and login as owner.
  // On failure the error message makes it immediately obvious which context
  // failed to login.
  // -------------------------------------------------------------------------
  async function createLoggedInContext(browser, label) {
    const ctx = await browser.newContext({ baseURL: BASE_URL });
    const page = await ctx.newPage();
    try {
      await loginAsOwner(page);
    } catch (err) {
      await ctx.close();
      throw new Error(`Login failed for context "${label}": ${err.message}`);
    }
    return { ctx, page };
  }

  // -------------------------------------------------------------------------
  // R-01: Two contexts both login as owner and navigate to the same calendar.
  // Assert both sessions are authenticated and see the shifts calendar.
  // -------------------------------------------------------------------------
  test('R-01: Two contexts login as owner and view shift calendar simultaneously', async ({ page, browser }) => {
    // Context A — use the default page fixture
    await loginAsOwner(page);
    await navigateTo(page, '/Calendar/Shifts');

    // Context B — separate browser context (independent cookies)
    const { ctx: ctxB, page: pageB } = await createLoggedInContext(browser, 'B');
    await navigateTo(pageB, '/Calendar/Shifts');

    // STRICT: Both contexts must see the shifts calendar wrapper
    const calendarA = page.locator('.shifts-calendar');
    const calendarB = pageB.locator('.shifts-calendar');
    await expect(calendarA).toBeVisible({ timeout: 10000 });
    await expect(calendarB).toBeVisible({ timeout: 10000 });

    // STRICT: Both are authenticated — no redirect back to login
    await expect(page).not.toHaveURL(/\/Auth\/Login/);
    await expect(pageB).not.toHaveURL(/\/Auth\/Login/);

    await saveEvidence(page, EVIDENCE, 'R-01-concurrent-assign-A.png');
    await saveEvidence(pageB, EVIDENCE, 'R-01-concurrent-assign-B.png');
    await ctxB.close();
  });

  // -------------------------------------------------------------------------
  // R-02: Three contexts view the same shifts calendar.
  // All three must remain authenticated and see the page.
  // -------------------------------------------------------------------------
  test('R-02: Three contexts view same shift calendar (SignalR readiness)', async ({ page, browser }) => {
    await loginAsOwner(page);
    await navigateTo(page, '/Calendar/Shifts');

    const { ctx: ctx2, page: page2 } = await createLoggedInContext(browser, 'Ctx2');
    await navigateTo(page2, '/Calendar/Shifts');

    const { ctx: ctx3, page: page3 } = await createLoggedInContext(browser, 'Ctx3');
    await navigateTo(page3, '/Calendar/Shifts');

    // STRICT: All three see the shifts calendar wrapper
    for (const [label, p] of [['ctx1', page], ['ctx2', page2], ['ctx3', page3]]) {
      const calendar = p.locator('.shifts-calendar');
      await expect(calendar).toBeVisible({ timeout: 10000 });
      await expect(p).not.toHaveURL(/\/Auth\/Login/);
    }

    await saveEvidence(page, EVIDENCE, 'R-02-signalr-ctx1.png');
    await saveEvidence(page2, EVIDENCE, 'R-02-signalr-ctx2.png');
    await saveEvidence(page3, EVIDENCE, 'R-02-signalr-ctx3.png');

    await ctx2.close();
    await ctx3.close();
  });

  // -------------------------------------------------------------------------
  // R-03: Two contexts view the chores calendar concurrently.
  // -------------------------------------------------------------------------
  test('R-03: Two contexts view chores calendar concurrently', async ({ page, browser }) => {
    await loginAsOwner(page);
    await navigateTo(page, '/Calendar/Chores');

    const { ctx: ctx2, page: page2 } = await createLoggedInContext(browser, 'ChoreB');
    await navigateTo(page2, '/Calendar/Chores');

    // STRICT: Both contexts loaded the chores page
    await expect(page).not.toHaveURL(/\/Auth\/Login/);
    await expect(page2).not.toHaveURL(/\/Auth\/Login/);
    // STRICT: Both contexts see the chores calendar wrapper
    const choresContentA = page.locator('.chores-calendar');
    const choresContentB = page2.locator('.chores-calendar');
    await expect(choresContentA).toBeVisible({ timeout: 10000 });
    await expect(choresContentB).toBeVisible({ timeout: 10000 });

    await saveEvidence(page, EVIDENCE, 'R-03-concurrent-chore-A.png');
    await saveEvidence(page2, EVIDENCE, 'R-03-concurrent-chore-B.png');
    await ctx2.close();
  });

  // -------------------------------------------------------------------------
  // R-04: Two contexts view the requests page concurrently.
  // -------------------------------------------------------------------------
  test('R-04: Two contexts view requests page concurrently', async ({ page, browser }) => {
    await loginAsOwner(page);
    await navigateTo(page, '/Requests/Index');
    await page.waitForLoadState('networkidle');

    const { ctx: ctx2, page: page2 } = await createLoggedInContext(browser, 'ReqB');
    await navigateTo(page2, '/Requests/Index');
    await page2.waitForLoadState('networkidle');

    // STRICT: Both contexts loaded — not redirected to login
    await expect(page).not.toHaveURL(/\/Auth\/Login/);
    await expect(page2).not.toHaveURL(/\/Auth\/Login/);
    // STRICT: Both are on the requests page with content
    expect(page.url()).toContain('/Requests');
    expect(page2.url()).toContain('/Requests');
    // STRICT: Page title exists (not blank page)
    const titleA = await page.title();
    const titleB = await page2.title();
    expect(titleA.length).toBeGreaterThan(0);
    expect(titleB.length).toBeGreaterThan(0);

    await saveEvidence(page, EVIDENCE, 'R-04-concurrent-timeoff-A.png');
    await saveEvidence(page2, EVIDENCE, 'R-04-concurrent-timeoff-B.png');
    await ctx2.close();
  });

  // -------------------------------------------------------------------------
  // R-05: Two contexts view the users admin page concurrently.
  // -------------------------------------------------------------------------
  test('R-05: Two contexts view admin users page concurrently', async ({ page, browser }) => {
    await loginAsOwner(page);
    await navigateTo(page, '/Admin/Users');
    await page.waitForLoadState('networkidle');

    const { ctx: ctx2, page: page2 } = await createLoggedInContext(browser, 'UsersB');
    await navigateTo(page2, '/Admin/Users');
    await page2.waitForLoadState('networkidle');

    // STRICT: Both contexts loaded the users page
    await expect(page).not.toHaveURL(/\/Auth\/Login/);
    await expect(page2).not.toHaveURL(/\/Auth\/Login/);
    // STRICT: Both contexts see the users data table
    const usersA = page.locator('.data-table').first();
    const usersB = page2.locator('.data-table').first();
    await expect(usersA).toBeVisible({ timeout: 10000 });
    await expect(usersB).toBeVisible({ timeout: 10000 });

    await saveEvidence(page, EVIDENCE, 'R-05-concurrent-users-A.png');
    await saveEvidence(page2, EVIDENCE, 'R-05-concurrent-users-B.png');
    await ctx2.close();
  });

  // -------------------------------------------------------------------------
  // R-06: Two contexts load requests page — verify independent sessions.
  // -------------------------------------------------------------------------
  test('R-06: Two independent sessions on requests page', async ({ page, browser }) => {
    await loginAsOwner(page);
    await navigateTo(page, '/Requests/Index');
    await page.waitForLoadState('networkidle');

    const { ctx: ctx2, page: page2 } = await createLoggedInContext(browser, 'SwapB');
    await navigateTo(page2, '/Requests/Index');
    await page2.waitForLoadState('networkidle');

    // STRICT: Both are on the requests page, not redirected
    await expect(page).not.toHaveURL(/\/Auth\/Login/);
    await expect(page2).not.toHaveURL(/\/Auth\/Login/);

    // STRICT: SessionStatus returns authenticated for both
    const sessionA = await page.request.get(`${BASE_URL}/Api/SessionStatus`, {
      headers: { 'X-Requested-With': 'XMLHttpRequest' },
    });
    expect(sessionA.status()).toBe(200);
    const sessionAData = await sessionA.json();
    expect(sessionAData.authenticated).toBe(true);

    const sessionB = await page2.request.get(`${BASE_URL}/Api/SessionStatus`, {
      headers: { 'X-Requested-With': 'XMLHttpRequest' },
    });
    expect(sessionB.status()).toBe(200);
    const sessionBData = await sessionB.json();
    expect(sessionBData.authenticated).toBe(true);

    await saveEvidence(page, EVIDENCE, 'R-06-concurrent-swap-A.png');
    await saveEvidence(page2, EVIDENCE, 'R-06-concurrent-swap-B.png');
    await ctx2.close();
  });

  // -------------------------------------------------------------------------
  // R-07: Browser back/forward after navigation preserves session.
  // -------------------------------------------------------------------------
  test('R-07: Browser back/forward after page navigation', async ({ page }) => {
    await loginAsOwner(page);
    await navigateTo(page, '/Calendar/Shifts');
    await page.waitForLoadState('networkidle');

    // Capture the shifts URL to verify we return to it
    const shiftsUrl = page.url();

    // Navigate forward to chores
    await navigateTo(page, '/Calendar/Chores');
    await page.waitForLoadState('networkidle');

    // STRICT: We are now on chores, not shifts
    expect(page.url()).toContain('/Calendar/Chores');

    // Go back — should return to shifts
    await page.goBack();
    await page.waitForLoadState('networkidle');

    // STRICT: We returned to the shifts page, not the login page
    await expect(page).not.toHaveURL(/\/Auth\/Login/);
    expect(page.url()).toContain('/Calendar/Shifts');

    // Go forward — should go back to chores
    await page.goForward();
    await page.waitForLoadState('networkidle');
    expect(page.url()).toContain('/Calendar/Chores');

    await saveEvidence(page, EVIDENCE, 'R-07-back-forward.png');
  });

  // -------------------------------------------------------------------------
  // R-08: Session remains valid after navigating between pages.
  // Verify via SessionStatus API that the session is not stale.
  // -------------------------------------------------------------------------
  test('R-08: Session remains valid after multiple navigations', async ({ page }) => {
    await loginAsOwner(page);
    await navigateTo(page, '/Calendar/Shifts');
    await page.waitForLoadState('networkidle');

    // Navigate to several pages to exercise the session
    await navigateTo(page, '/Calendar/Chores');
    await navigateTo(page, '/Admin/Users');
    await navigateTo(page, '/Calendar/Shifts');

    // STRICT: Session is still valid
    const sessionResp = await page.request.get(`${BASE_URL}/Api/SessionStatus`, {
      headers: { 'X-Requested-With': 'XMLHttpRequest' },
    });
    expect(sessionResp.status()).toBe(200);
    const session = await sessionResp.json();
    expect(session.authenticated).toBe(true);
    expect(session.state).toBe('ok');
    expect(session.userId).toBeGreaterThan(0);

    await saveEvidence(page, EVIDENCE, 'R-08-session-valid.png');
  });

  // -------------------------------------------------------------------------
  // R-09: Two fully separate browser contexts both access the same API.
  // Verify independent sessions do not interfere with each other.
  // -------------------------------------------------------------------------
  test('R-09: Two separate contexts both hit SessionStatus API', async ({ page, browser }) => {
    await loginAsOwner(page);
    await navigateTo(page, '/Calendar/Shifts');

    const { ctx: ctx2, page: page2 } = await createLoggedInContext(browser, 'SepB');
    await navigateTo(page2, '/Calendar/Shifts');

    // STRICT: Both contexts can independently query session status
    const respA = await page.request.get(`${BASE_URL}/Api/SessionStatus`, {
      headers: { 'X-Requested-With': 'XMLHttpRequest' },
    });
    expect(respA.status()).toBe(200);
    const dataA = await respA.json();
    expect(dataA.authenticated).toBe(true);

    const respB = await page2.request.get(`${BASE_URL}/Api/SessionStatus`, {
      headers: { 'X-Requested-With': 'XMLHttpRequest' },
    });
    expect(respB.status()).toBe(200);
    const dataB = await respB.json();
    expect(dataB.authenticated).toBe(true);

    // Both should report the same user ID (both logged in as owner)
    expect(dataA.userId).toBe(dataB.userId);

    await saveEvidence(page, EVIDENCE, 'R-09-separate-sessions-A.png');
    await saveEvidence(page2, EVIDENCE, 'R-09-separate-sessions-B.png');
    await ctx2.close();
  });

  // -------------------------------------------------------------------------
  // R-10: Three contexts view chores calendar concurrently.
  // -------------------------------------------------------------------------
  test('R-10: Three contexts view chores calendar simultaneously', async ({ page, browser }) => {
    await loginAsOwner(page);
    await navigateTo(page, '/Calendar/Chores');

    const { ctx: ctx2, page: page2 } = await createLoggedInContext(browser, 'Chore2');
    await navigateTo(page2, '/Calendar/Chores');

    const { ctx: ctx3, page: page3 } = await createLoggedInContext(browser, 'Chore3');
    await navigateTo(page3, '/Calendar/Chores');

    // STRICT: All three contexts see the chores calendar wrapper
    for (const [label, p] of [['ctx1', page], ['ctx2', page2], ['ctx3', page3]]) {
      await expect(p).not.toHaveURL(/\/Auth\/Login/);
      const content = p.locator('.chores-calendar');
      await expect(content).toBeVisible({ timeout: 10000 });
    }

    // STRICT: All three have valid sessions
    for (const [label, p] of [['ctx1', page], ['ctx2', page2], ['ctx3', page3]]) {
      const resp = await p.request.get(`${BASE_URL}/Api/SessionStatus`, {
        headers: { 'X-Requested-With': 'XMLHttpRequest' },
      });
      expect(resp.status()).toBe(200);
      const data = await resp.json();
      expect(data.authenticated).toBe(true);
    }

    await saveEvidence(page, EVIDENCE, 'R-10-three-chores.png');
    await ctx2.close();
    await ctx3.close();
  });
});
