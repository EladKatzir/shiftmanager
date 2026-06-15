// @ts-check
/**
 * SECURITY + FUNCTIONAL — Shorten approved vacation (/Api/TimeOffRequest/{id}?handler=UpdateDates)
 *
 * Two confirmed defects in the in-flight VacationApprovalService.UpdateRequestDatesAsync
 * (uncommitted at the 2026-06-14 sweep). Both assert correct behavior → red until fixed.
 *
 *   F5 (P1, functional): The handler ALWAYS throws and returns 400 "VacationApproval_Error",
 *       for every actor including the request's owner. Root cause: UpdateRequestDatesAsync
 *       opens a DB transaction (VacationApprovalService.cs:872) then calls
 *       HomeMaterialiserService.SyncMaterialisedHomeRowsAsync which opens ANOTHER transaction
 *       on the same connection (HomeMaterialiserService.cs:42) → InvalidOperationException
 *       "The connection is already in a transaction". The shorten feature is fully broken.
 *
 *   F6 (latent P0, security): The handler is [Authorize]-only with NO ownership check and NO
 *       ApproveVacations grant check (contrast the sibling Cancel handler which enforces
 *       request.UserId == userId). A non-owner reaches the mutation; only F5's crash prevents
 *       a persisted cross-tenant write today. Fixing F5 without adding this check turns it into
 *       a live cross-tenant data-tamper vulnerability.
 *
 * Helper note: UpdateDates requires the 'X-Requested-With: XMLHttpRequest' header (CSRF guard)
 * and a JSON body; that header is NOT an authorization control.
 */
const { test, expect } = require('@playwright/test');
const { loginAsPersona, formPost, extractCsrfToken } = require('../helpers/persona-helpers');
const { createPendingTimeOffRequest, dateOffset } = require('../helpers/test-data-helpers');

async function approvedRequest(page, mgrPage) {
    const reason = `QA-shorten ${Date.now()}`;
    const { requestId } = await createPendingTimeOffRequest(page, {
        startDate: dateOffset(70), endDate: dateOffset(77), reason,
    });
    const appr = await formPost(mgrPage, '/Requests?handler=ApproveTimeOff', { id: String(requestId) });
    expect([200, 302, 303]).toContain(appr.status);
    return requestId;
}

async function updateDates(page, id, newStart, newEnd) {
    const token = await extractCsrfToken(page);
    return page.request.post(`/Api/TimeOffRequest/${id}?handler=UpdateDates`, {
        headers: {
            'RequestVerificationToken': token || '',
            'X-Requested-With': 'XMLHttpRequest',
            'Content-Type': 'application/json',
        },
        data: JSON.stringify({ NewStart: newStart, NewEnd: newEnd }),
        maxRedirects: 0,
    });
}

test.describe('Vacation shorten — UpdateDates handler (F5/F6)', () => {

    test('F5 — the OWNER can successfully shorten their own approved vacation', async ({ page, context }) => {
        await loginAsPersona(page, 'OrenAlhutEmp');
        const mgr = await context.browser().newContext();
        const mgrPage = await mgr.newPage();
        await loginAsPersona(mgrPage, 'OrenAlhutLead');
        const id = await approvedRequest(page, mgrPage);
        await mgr.close();

        // Owner shortens by one day — should succeed (200 + success:true).
        const resp = await updateDates(page, id, dateOffset(70), dateOffset(76));
        const body = await resp.text().catch(() => '');
        expect(
            resp.status() === 200 && /"success"\s*:\s*true/.test(body),
            `Owner shorten should succeed. status=${resp.status()} body=${body.slice(0,160)} ` +
            `(F5: nested-transaction bug VacationApprovalService.cs:872 + HomeMaterialiserService.cs:42).`
        ).toBe(true);
    });

    test('F6 — a non-owner base user must be DENIED the UpdateDates handler', async ({ page, context }) => {
        // Victim creates + gets approved
        const victimCtx = await context.browser().newContext();
        const victim = await victimCtx.newPage();
        await loginAsPersona(victim, 'OrenAlhutEmp');
        const mgr = await context.browser().newContext();
        const mgrPage = await mgr.newPage();
        await loginAsPersona(mgrPage, 'OrenAlhutLead');
        const id = await approvedRequest(victim, mgrPage);
        await mgr.close();
        await victimCtx.close();

        // Attacker: a different base employee tries to re-date the victim's request.
        await loginAsPersona(page, 'OrenHitazmutEmp');
        const resp = await updateDates(page, id, dateOffset(70), dateOffset(71));
        const body = await resp.text().catch(() => '');
        // The fix denies non-owners via the app's NotYourRequest path (HTTP 400 success:false),
        // consistent with the sibling Cancel handler. A 403/redirect is also acceptable. Since F5 is
        // fixed (an authorized owner now gets success:true), a non-owner's success:false IS the denial.
        const denied = resp.status() === 403
            || (resp.status() >= 300 && resp.status() < 400 && /AccessDenied|Login/i.test(resp.headers()['location'] || ''))
            || (resp.status() === 400 && /"success"\s*:\s*false/.test(body));
        expect(
            denied,
            `Non-owner must be denied. Got status=${resp.status()} body=${body.slice(0,160)}. ` +
            `A success:true here would mean the non-owner re-dated someone else's leave (F6 P0).`
        ).toBe(true);
    });
});
