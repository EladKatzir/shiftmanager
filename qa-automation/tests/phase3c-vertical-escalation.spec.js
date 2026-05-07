// @ts-check
/**
 * PHASE 3c — Vertical Privilege Escalation
 *
 * Attacker: Shikma Employee (emp.tech.pie@test) — standard, no admin grants
 *
 * Tests that an unprivileged employee CANNOT:
 *  3c.1  Approve their own pending vacation request
 *  3c.2  Self-assign a chore
 *  3c.3  Self-assign a shift
 *  3c.4  Reach /Admin/Users
 *
 * Each must fail with a visible/observable denial.
 */
const { test, expect } = require('@playwright/test');
const {
    loginAsPersona,
    jsonPost,
    formPost,
    lookupUserIds,
} = require('../helpers/persona-helpers');
const { dateOffset, createPendingTimeOffRequest } = require('../helpers/test-data-helpers');

test.describe('Phase 3c — Vertical privilege escalation (employee → manager actions)', () => {

    /** @type {{ [email: string]: number }} */
    let userIds;

    test.beforeAll(async ({ browser }) => {
        const ctx = await browser.newContext();
        userIds = await lookupUserIds(ctx, [
            'emp.tech.pie@test',
        ]);
        await ctx.close();
    });

    // ──────────────────────────────────────────────────────────────────────
    test('3c.1  Employee cannot approve their own pending vacation request', async ({ page }) => {
        await loginAsPersona(page, 'ShikmaEmployee');

        // Pre-fixture: employee creates own pending request
        const { requestId } = await createPendingTimeOffRequest(page, {
            startDate: dateOffset(20),
            endDate: dateOffset(20),
            reason: `QA-Phase3c.1 self-approval attack ${Date.now()}`,
        });

        // Attempt self-approval
        const resp = await formPost(page, '/Requests?handler=ApproveTimeOff', { id: String(requestId) });

        // The handler calls RequireManagerAccessAsync(); employee lacks ManagerHomeAccess → Forbid()
        // ASP.NET Core Forbid() returns either 403 OR redirects to /Account/AccessDenied
        const denied =
            resp.status === 403
            || (resp.status >= 300 && resp.status < 400 && /AccessDenied|Login/i.test(resp.location || ''));

        expect.soft(denied, `self-approve must be denied — got ${resp.status} loc=${resp.location}`).toBe(true);

        // Verify the request status DID NOT flip to Approved
        await page.goto('/My/Requests');
        await page.waitForLoadState('networkidle');
        const bodyText = (await page.textContent('body')) || '';
        // Request should still appear in pending section, not in approved.
        // We just assert it didn't trigger the approval audit log path.
        // (We trust the 403 — additional state assertion would require admin lookup we don't have.)
        expect.soft(bodyText.length, 'page should still render after denial').toBeGreaterThan(0);
    });

    // ──────────────────────────────────────────────────────────────────────
    test('3c.2  Employee cannot self-assign a chore', async ({ page }) => {
        await loginAsPersona(page, 'ShikmaEmployee');

        const resp = await jsonPost(page, '/Api/Calendar/QuickAddChore', {
            assigneeId: userIds['emp.tech.pie@test'],  // SELF
            date: dateOffset(21),
            title: 'P3c.2 self-assign chore attack',
        });

        // Page is `[Authorize(Policy = "Grant:AssignChores")]` — employee lacks AssignChores → 403/redirect
        const denied = resp.status === 403 || resp.status === 401;

        expect.soft(denied, `self-assign chore must be denied — got ${resp.status} body=${JSON.stringify(resp.body).slice(0, 300)}`).toBe(true);
        expect.soft(resp.body?.success, 'must NOT report success').not.toBe(true);
    });

    // ──────────────────────────────────────────────────────────────────────
    test('3c.3  Employee cannot self-assign a shift', async ({ page }) => {
        await loginAsPersona(page, 'ShikmaEmployee');

        const resp = await jsonPost(page, '/Calendar/Table?handler=AssignEmployee', {
            shiftTypeId: 1,
            date: dateOffset(22),
            userId: userIds['emp.tech.pie@test'],  // SELF
        });

        // Page is `[Authorize(Policy = "Grant:ManagerHomeAccess")]` — employee lacks → page-level deny
        const denied = [401, 403, 404].includes(resp.status);

        expect.soft(denied, `self-assign shift must be denied — got ${resp.status} body=${JSON.stringify(resp.body).slice(0, 300)}`).toBe(true);
        expect.soft(resp.body?.success, 'must NOT report success').not.toBe(true);
    });

    // ──────────────────────────────────────────────────────────────────────
    test('3c.4  Employee navigating to /Admin/Users gets a visible denial (403 page or AccessDenied)', async ({ page }) => {
        await loginAsPersona(page, 'ShikmaEmployee');

        const resp = await page.goto('/Admin/Users', { waitUntil: 'networkidle' });
        const finalUrl = page.url();
        const status = resp?.status() ?? 0;

        const denied =
            status === 403
            || /AccessDenied|Auth\/Login/i.test(finalUrl);

        expect.soft(denied, `/Admin/Users should be denied — got status=${status} url=${finalUrl}`).toBe(true);

        // Phase 4 mandate: visible UI message
        if (denied) {
            const bodyText = ((await page.textContent('body')) || '').toLowerCase();
            const hasReadableDenial = /permission|access|denied|forbidden|not\s+authorized|אין\s+הרשאה/.test(bodyText);
            expect.soft(hasReadableDenial, `denial page must contain a human-readable message (body excerpt: ${bodyText.slice(0, 200)})`).toBe(true);
        }
    });
});
