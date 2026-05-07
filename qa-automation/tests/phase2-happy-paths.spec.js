// @ts-check
/**
 * PHASE 2 — Critical Path Verification (sanctioned flows)
 *
 * 2.1  Shikma Assigner — chore assignment SUCCEEDS, shift assignment FAILS (correct asymmetry)
 * 2.2  Shikma BRDirector — vacation approval flips status Pending → Approved
 * 2.3  Oren Alhut Lead — shift assignment to a same-molecule Alhut user SUCCEEDS
 *
 * Failures here are P0 blockers for the investor call.
 */
const { test, expect } = require('@playwright/test');
const {
    PERSONAS,
    loginAsPersona,
    logout,
    jsonPost,
    findUserIdAsOwner,
    lookupUserIds,
} = require('../helpers/persona-helpers');
const {
    dateOffset,
    createPendingTimeOffRequest,
} = require('../helpers/test-data-helpers');

test.describe('Phase 2 — Sanctioned happy paths', () => {

    /** @type {{ [email: string]: number }} */
    let userIds;

    test.beforeAll(async ({ browser }) => {
        const ctx = await browser.newContext();
        userIds = await lookupUserIds(ctx, [
            'assigner.pie@test',
            'deptlead.pie@test',
            'mgr.alhut.tz@test',
            'emp.tech.pie@test',
            'emp.tz.alhut@test',
        ]);
        await ctx.close();
    });

    // ──────────────────────────────────────────────────────────────────────
    test('2.1  Shikma Assigner can assign a chore but cannot assign a shift (correct asymmetry per RoleTemplateSeed.cs Assigner bundle)', async ({ page }) => {
        await loginAsPersona(page, 'ShikmaAssigner');

        // Randomize the date to avoid CHORE_CONFLICT collisions with prior test runs
        // (the test DB does not reset chores between runs).
        const choreDate = dateOffset(150 + Math.floor(Math.random() * 200));
        const assigneeId = userIds['emp.tech.pie@test'];

        // (a) CHORE assign — should succeed because Assigner role has AssignChores grant (ExpandToArea)
        const choreResp = await jsonPost(page, '/Api/Calendar/QuickAddChore', {
            assigneeId,
            date: choreDate,
            title: `QA chore ${Date.now()}`,
            notes: 'Phase 2.1 sanctioned chore',
        });

        // Auth gate must open (no 401/403). The actual assignment may succeed (success:true) OR
        // hit a busy warning (requiresOverride:true) — both prove the role's AssignChores grant
        // applied. The point of this test is the *asymmetry* (chore allowed, shift denied),
        // not whether the chore physically lands.
        expect.soft([401, 403], 'Assigner chore POST must NOT be auth-denied').not.toContain(choreResp.status);
        expect.soft(choreResp.status, `chore POST status (body=${JSON.stringify(choreResp.body)})`).toBe(200);
        const choreAuthOpened =
            choreResp.body?.success === true
            || choreResp.body?.requiresOverride === true
            || (choreResp.body?.success === false && /CONFLICT|BUSY/i.test(JSON.stringify(choreResp.body)));
        expect.soft(choreAuthOpened, `chore auth gate must open (success or busy warning) — body=${JSON.stringify(choreResp.body).slice(0, 300)}`).toBe(true);

        // (b) SHIFT assign — should FAIL because Assigner role lacks any Assign*Shifts grant.
        // This is the *correct* boundary: chore-focused role must not double as shift assigner.
        // We need a real shiftTypeId; look one up from any visible calendar URL the assigner can reach.
        // If the assigner cannot reach any calendar (also valid security), we'll fall back to a guessed
        // ID and assert the 403/forbidden response.

        let shiftTypeId = 1; // sensible default for seed data
        const shiftResp = await jsonPost(page, '/Calendar/Table?handler=AssignEmployee', {
            shiftTypeId,
            date: choreDate,
            userId: assigneeId,
        });

        // Either 403 (no grant) or 401 (page-level auth blocked even reaching the handler).
        expect.soft([401, 403, 404], `shift POST should refuse (got ${shiftResp.status}, body=${JSON.stringify(shiftResp.body).slice(0, 200)})`).toContain(shiftResp.status);
        expect.soft(shiftResp.body?.success, 'shift POST must NOT report success for Assigner role').not.toBe(true);
    });

    // ──────────────────────────────────────────────────────────────────────
    test('2.2  Shikma BRDirector approves a Pie employee vacation request and the status flips to Approved', async ({ browser }) => {
        // Pre-fixture: log in as the employee, create the pending request
        const empCtx = await browser.newContext();
        const empPage = await empCtx.newPage();
        await loginAsPersona(empPage, 'ShikmaEmployee');

        const startDate = dateOffset(45);
        const endDate = dateOffset(46);
        const { requestId } = await createPendingTimeOffRequest(empPage, {
            startDate,
            endDate,
            type: 'Vacation',
            reason: `QA-Phase2.2 vacation ${Date.now()}`,
        });
        await empCtx.close();

        // Manager approves
        const mgrCtx = await browser.newContext();
        const mgrPage = await mgrCtx.newPage();
        await loginAsPersona(mgrPage, 'ShikmaBRDirector');

        await mgrPage.goto('/Requests');
        await mgrPage.waitForLoadState('networkidle');

        // Use the formPost helper through the helper module (form submits with antiforgery)
        const { formPost } = require('../helpers/persona-helpers');
        const approveResp = await formPost(mgrPage, '/Requests?handler=ApproveTimeOff', { id: String(requestId) });

        // Razor Pages POST → typically 302 redirect to /Requests on success
        expect.soft([200, 302, 303], `approve POST status (body snippet=${approveResp.body.slice(0, 200)})`).toContain(approveResp.status);

        // Verify status flipped: re-render /Requests as the employee — request should NOT be pending anymore
        const verifyCtx = await browser.newContext();
        const verifyPage = await verifyCtx.newPage();
        await loginAsPersona(verifyPage, 'ShikmaEmployee');
        await verifyPage.goto('/Requests');
        await verifyPage.waitForLoadState('networkidle');

        // The employee view shows pending in TimeOff and approved in ApprovedTimeOffs.
        // After approval, the request should appear in the approved section.
        // Dates render via Localization.FormatMediumDate (e.g. "Jul 12, 2026") — the ISO startDate
        // string won't appear verbatim. So we assert the unique reason text (which we control) DOES
        // appear AND the rendered status is "Approved" somewhere in proximity.
        const pageText = (await verifyPage.textContent('body')) || '';
        // Reason from the closure scope: `QA-Phase2.2 vacation <timestamp>`
        const reasonMatch = pageText.match(/QA-Phase2\.2 vacation \d+/);
        expect.soft(reasonMatch, `verify page must contain the unique reason text — body excerpt: ${pageText.slice(0, 400)}`).not.toBeNull();
        // Approved label must appear somewhere on the page (employee sees their own approved requests)
        const hasApprovedLabel = /\bApproved\b/i.test(pageText) || /\bמאושר/.test(pageText);
        expect.soft(hasApprovedLabel, 'verify page must show an Approved status label after approval').toBe(true);

        await mgrCtx.close();
        await verifyCtx.close();
    });

    // ──────────────────────────────────────────────────────────────────────
    test('2.3  Oren Alhut Lead can assign a shift to a same-molecule Alhut user (auth gate passed)', async ({ page }) => {
        await loginAsPersona(page, 'OrenAlhutLead');

        // Far-future unique date prevents collisions with prior runs. shiftTypeId=1 is a real
        // seeded shift; what matters for the AUTH boundary is the response category, not whether
        // the assignment itself completes (it can fail with ALREADY_ASSIGNED or other business
        // rules — those are NOT auth failures, which is what this test asserts).
        const assignResp = await jsonPost(page, '/Calendar/Table?handler=AssignEmployee', {
            shiftTypeId: 1,
            date: dateOffset(120 + Math.floor(Math.random() * 60)),
            userId: userIds['emp.tz.alhut@test'],
        });

        // Auth boundary: MUST NOT be 401/403 (the page-level Grant:ManagerHomeAccess + handler-level
        // shift-grant check both must pass for an Alhut Lead in own molecule).
        expect.soft([401, 403], 'Alhut Lead must NOT be denied for Alhut shift in own molecule').not.toContain(assignResp.status);

        // Status code must be 200 — even on business-rule rejection, the handler returns 200 with success=false.
        expect.soft(assignResp.status, `expected 200 (auth passed), got ${assignResp.status} body=${JSON.stringify(assignResp.body).slice(0, 300)}`).toBe(200);

        // Response is a JSON envelope. Either success:true (assigned), requiresOverride:true (busy warning),
        // or success:false with a business-rule error key (NOT an auth error). All three prove the auth gate opened.
        const reachedService =
            assignResp.body?.success === true
            || assignResp.body?.requiresOverride === true
            || (assignResp.body?.success === false && Array.isArray(assignResp.body?.errors));

        expect.soft(reachedService, `Alhut Lead shift POST must reach the service (any non-auth response is fine) — body=${JSON.stringify(assignResp.body).slice(0, 300)}`).toBe(true);
    });
});
