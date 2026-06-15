// @ts-check
/**
 * SECURITY — per-handler authorization (F7/F8/F9/F10), scoped/cross-tenant cases.
 *
 * These assert the fixes added in this session. They are POSITIVE security regressions:
 * they pass now and will fail if a fix is reverted.
 *
 *   F7 (Calendar/Table shift handlers): a manager may only mutate shifts within the scope of
 *       their Assign*Shifts grant. An Oren-molecule Lead must be DENIED mutating a shift in a
 *       different molecule. (cross-scope / cross-tenant proof)
 *   F9 (Admin/EditProfile DeleteAvatar): requires EditCompanyUsers scoped to the target's company.
 *       An Oren-molecule Lead must be DENIED deleting an avatar of a user in another molecule.
 *   F8 (Owner/Programs): requires CreateShiftPrograms, not just the broad ManagerHomeAccess.
 *   F10 (Admin/Config OnDutyType): requires ManageOnDutyTypes, not just ManagerHomeAccess.
 *
 * F8/F10 use the seeded `concurrent1@test`, which holds ManagerHomeAccess (passes the page class
 * gate) but NOT the dedicated grant — so it directly exercises the new per-handler check.
 *
 * Cross-scope target IDs come from fixtures/cross-scope.json (generated from the seeded org).
 */
const { test, expect } = require('@playwright/test');
const fs = require('fs');
const path = require('path');

const fx = JSON.parse(fs.readFileSync(path.join(__dirname, '..', 'fixtures', 'cross-scope.json'), 'utf8'));

async function login(page, email, password) {
    await page.goto('/Auth/Login', { waitUntil: 'domcontentloaded' });
    await page.fill('input[name="Email"], input#Email', email);
    await page.fill('input[name="Password"], input#Password', password);
    await Promise.all([
        page.waitForURL(u => !u.toString().includes('/Auth/Login'), { timeout: 8000 }).catch(() => {}),
        page.locator('form:has(input[name="Email"]) button[type="submit"]').click(),
    ]);
}

async function antiforgery(page) {
    await page.goto('/My/Requests', { waitUntil: 'domcontentloaded' });
    return page.locator('input[name="__RequestVerificationToken"]').first().getAttribute('value').catch(() => null);
}

/** A denial is a 403, a 404 (cross-tenant target filtered out → not deletable, no existence leak),
 *  or a 3xx→AccessDenied/Login redirect. The point is the mutation does NOT happen. */
function denied(status, location) {
    return status === 403
        || status === 404
        || (status >= 300 && status < 400 && /AccessDenied|Auth\/Login/i.test(location || ''));
}

test.describe('SECURITY — scoped per-handler authorization (F7/F9 cross-molecule, F8/F10 dedicated grant)', () => {

    test('F7 — Oren Lead is DENIED ClearAssignment on a cross-molecule shift', async ({ page }) => {
        test.skip(!fx.assignment, 'no cross-molecule assignment in fixture');
        await login(page, 'mgr.alhut.tz@test', 'Test1234!');
        const token = await antiforgery(page);
        const resp = await page.request.post('/Calendar/Table?handler=ClearAssignment', {
            headers: { 'RequestVerificationToken': token || '', 'X-Requested-With': 'XMLHttpRequest', 'Content-Type': 'application/json' },
            data: JSON.stringify({ AssignmentId: fx.assignment.assignmentId }),
            maxRedirects: 0,
        });
        expect(
            resp.status() === 403,
            `Cross-molecule ClearAssignment must be 403. Got ${resp.status()} (assignment ${fx.assignment.assignmentId} in molecule ${fx.instance.MoleculeId}; Lead is in molecule ${fx.mgrMolecule}).`
        ).toBe(true);
    });

    test('F7 — Oren Lead is DENIED UnassignEmployee on a cross-molecule shift', async ({ page }) => {
        test.skip(!fx.assignment, 'no cross-molecule assignment in fixture');
        await login(page, 'mgr.alhut.tz@test', 'Test1234!');
        const token = await antiforgery(page);
        const resp = await page.request.post('/Calendar/Table?handler=UnassignEmployee', {
            headers: { 'RequestVerificationToken': token || '', 'X-Requested-With': 'XMLHttpRequest', 'Content-Type': 'application/json' },
            data: JSON.stringify({ AssignmentId: fx.assignment.assignmentId }),
            maxRedirects: 0,
        });
        expect(resp.status() === 403, `Cross-molecule UnassignEmployee must be 403. Got ${resp.status()}.`).toBe(true);
    });

    test('F9 — Oren Lead is DENIED deleting a cross-molecule user\'s avatar', async ({ page }) => {
        test.skip(!fx.crossUser, 'no cross-molecule user in fixture');
        await login(page, 'mgr.alhut.tz@test', 'Test1234!');
        const token = await antiforgery(page);
        const resp = await page.request.post('/Admin/EditProfile?handler=DeleteAvatar', {
            headers: { 'RequestVerificationToken': token || '', 'X-Requested-With': 'XMLHttpRequest' },
            form: { UserId: String(fx.crossUser.Id), __RequestVerificationToken: token || '' },
            maxRedirects: 0,
        });
        const loc = resp.headers()['location'] || '';
        expect(
            denied(resp.status(), loc),
            `Cross-molecule avatar delete must be denied. Got ${resp.status()} loc=${loc} (target user ${fx.crossUser.Id} in molecule ${fx.crossUser.MoleculeId}; Lead in molecule ${fx.mgrMolecule}).`
        ).toBe(true);
    });

    test('F8 — ManagerHomeAccess-only user is DENIED creating a Program (needs CreateShiftPrograms)', async ({ page }) => {
        await login(page, 'concurrent1@test', 'Test1234!');
        const token = await antiforgery(page);
        const resp = await page.request.post('/Owner/Programs?handler=CreateProgram', {
            headers: { 'RequestVerificationToken': token || '', 'X-Requested-With': 'XMLHttpRequest' },
            form: { ProgramName: `QA-F8 ${Date.now()}`, ShiftTypeId: '1', DefaultStaffing: '1', 'SelectedDays': 'Monday', __RequestVerificationToken: token || '' },
            maxRedirects: 0,
        });
        const loc = resp.headers()['location'] || '';
        expect(
            denied(resp.status(), loc),
            `Program create by ManagerHomeAccess-only user must be denied (403). Got ${resp.status()} loc=${loc}.`
        ).toBe(true);
    });

    test('F10 — ManagerHomeAccess-only user is DENIED adding a global OnDutyType (needs ManageOnDutyTypes)', async ({ page }) => {
        await login(page, 'concurrent1@test', 'Test1234!');
        const token = await antiforgery(page);
        const resp = await page.request.post('/Admin/Config?handler=AddOnDutyType', {
            headers: { 'RequestVerificationToken': token || '', 'X-Requested-With': 'XMLHttpRequest' },
            form: { NameEn: `QA-F10 ${Date.now()}`, NameHe: 'בדיקה', Icon: '📌', Color: '#6366f1', __RequestVerificationToken: token || '' },
            maxRedirects: 0,
        });
        const loc = resp.headers()['location'] || '';
        expect(
            denied(resp.status(), loc),
            `Global OnDutyType add by ManagerHomeAccess-only user must be denied (403). Got ${resp.status()} loc=${loc}.`
        ).toBe(true);
    });
});
