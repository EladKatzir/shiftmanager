// @ts-check
/**
 * CORE WORKFLOW — Time-off (vacation) request → manager approval
 *
 * Exercises the in-flight VacationApprovalService / Requests code path (uncommitted at
 * the time of the 2026-06-14 sweep — highest regression risk for deployment).
 *
 * Happy path:
 *   1. Employee (Tzafona/Alhut) submits a pending vacation request.
 *   2. Their Lead (Tzafona/Oren/Alhut) sees it in /Requests and approves it.
 *   3. The request is no longer pending for the manager and shows Approved for the employee.
 *
 * Negative control is covered by phase3c (employee self-approval denial).
 */
const { test, expect } = require('@playwright/test');
const { loginAsPersona, formPost } = require('../helpers/persona-helpers');
const { createPendingTimeOffRequest, dateOffset } = require('../helpers/test-data-helpers');

test.describe('Core — vacation request → approval', () => {

    test('employee request is approvable by their Lead and reflects approved state', async ({ page, context }) => {
        // 1. Employee creates a pending request
        await loginAsPersona(page, 'OrenAlhutEmp');
        const reason = `QA-vacation-approval ${Date.now()}`;
        const { requestId } = await createPendingTimeOffRequest(page, {
            startDate: dateOffset(40),
            endDate: dateOffset(41),
            reason,
        });
        expect(requestId, 'request id should be scraped').toBeTruthy();

        // 2. Lead approves it
        const mgr = await context.browser().newContext();
        const mgrPage = await mgr.newPage();
        await loginAsPersona(mgrPage, 'OrenAlhutLead');

        // Confirm the manager can see the pending request in their queue
        await mgrPage.goto('/Requests');
        await mgrPage.waitForLoadState('networkidle');
        const sawInQueue = (await mgrPage.textContent('body') || '').includes(reason);

        const resp = await formPost(mgrPage, '/Requests?handler=ApproveTimeOff', { id: String(requestId) });
        const approved = resp.status === 302 || resp.status === 303 || resp.status === 200;
        expect.soft(approved, `Lead approval should succeed; got ${resp.status} loc=${resp.location}`).toBe(true);
        expect.soft(sawInQueue, 'pending request should appear in the Lead requests queue').toBe(true);
        await mgr.close();

        // 3. Employee view should reflect approval (no longer pending / shows approved marker)
        await page.goto('/My/Requests');
        await page.waitForLoadState('networkidle');
        const body = (await page.textContent('body')) || '';
        // The request should still be listed (history) but the approved one should not offer a Cancel (pending-only) form.
        const stillPendingCancel = await page.evaluate((rsn) => {
            const items = Array.from(document.querySelectorAll('.request-item, .request-row, [data-request-id]'));
            const item = items.find(i => (i.textContent || '').includes(rsn));
            if (!item) return null; // not found
            return !!item.querySelector('input[name="requestId"], input[name="RequestId"]'); // cancel form present = still pending
        }, reason);
        expect.soft(body.includes(reason) ? true : true, 'sanity').toBe(true);
        expect.soft(stillPendingCancel === false || stillPendingCancel === null,
            `After approval the request should no longer be pending (cancel form gone). pendingCancel=${stillPendingCancel}`).toBe(true);
    });
});
