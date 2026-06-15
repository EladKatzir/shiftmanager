// @ts-check
/**
 * SECURITY — Grant management handler authorization
 *
 * Documents two confirmed defects found during the 2026-06-14 pre-deploy QA sweep.
 * These assert the CORRECT (secure) behavior, so they intentionally FAIL on the
 * current build until the underlying authorization is fixed (red → green regression).
 *
 *   F1 (P0): /Admin/Organization/Grants?handler=Revoke (OnPostRevokeAsync) is gated
 *            only by the class-level [Authorize(Policy="Grant:ViewGrants")]. ViewGrants
 *            is held by ~all users (seeded into base templates), so any authenticated
 *            user can revoke any user's grant cross-tenant. Proven E2E: a Trainee in
 *            Company 1 deleted grant #1380 of an Employee in Company 2.
 *            FIX: require a dedicated mutation grant (RevokeGrants/AssignGrants) on the
 *            handler, and scope-check the target grant's company.
 *
 *   F2 (P1): /Admin/Organization, /Admin/Organization/Hierarchy, /Admin/Organization/Grants
 *            are reachable by base-role users because ViewHierarchy/ViewGrants are seeded
 *            into Employee/Trainee/Assigner templates — exposing the org tree and the full
 *            grant-assignment table (the permission map) to everyone.
 *
 * The revoke probe uses a NON-EXISTENT grant id, so it tests the authorization boundary
 * WITHOUT deleting any data: a secure handler returns 403/AccessDenied before the
 * not-found check; the buggy handler runs and 302-redirects to the grants page.
 */
const { test, expect } = require('@playwright/test');
const { loginAsPersona, formPost } = require('../helpers/persona-helpers');

/** A base-role user with no management grants (Tzafona Alhut employee). */
const BASE_PERSONA = 'OrenAlhutEmp';

/** True if the response is a real authorization denial. */
function isDenied(resp) {
    return resp.status === 403
        || (resp.status >= 300 && resp.status < 400 && /AccessDenied|Login/i.test(resp.location || ''));
}

test.describe('SECURITY — grant handler authorization (F1/F2)', () => {

    test('F1 — base employee must NOT be able to invoke the grant-revoke handler', async ({ page }) => {
        await loginAsPersona(page, BASE_PERSONA);

        // Non-existent id → no data mutated regardless of outcome. Secure = denied before lookup.
        const resp = await formPost(page, '/Admin/Organization/Grants?handler=Revoke', { id: '999999999' });

        expect(
            isDenied(resp),
            `Revoke handler must reject a non-manager. Got status=${resp.status} loc=${resp.location}. ` +
            `If this is a 302 back to the grants page, the handler executed for an unauthorized user (F1 P0).`
        ).toBe(true);
    });

    test('F2 — base employee must NOT see the Manage Grants page (permission-map disclosure)', async ({ page }) => {
        await loginAsPersona(page, BASE_PERSONA);
        const resp = await page.goto('/Admin/Organization/Grants', { waitUntil: 'domcontentloaded' });
        const status = resp ? resp.status() : 0;
        const url = page.url();
        const denied = status === 403 || /AccessDenied|Auth\/Login/i.test(url);

        // If the page IS served, it must at least not render the full grant-assignment table.
        let grantRows = 0;
        if (!denied) grantRows = await page.locator('table tbody tr').count();

        expect(
            denied,
            `Base employee reached /Admin/Organization/Grants (status=${status}, url=${url}, ` +
            `grantTableRows=${grantRows}). Base users should not view the system permission map (F2 P1).`
        ).toBe(true);
    });

    // DESIGN DECISION (F2): ViewHierarchy is intentionally kept broad — the org-structure tree
    // (company/molecule/area names) is benign for staff to see. Only the GRANTS permission-map is
    // restricted. This test documents that decision: base users CAN view the hierarchy tree, but it
    // must NOT expose the grant-assignment table. If hierarchy viewing is later restricted, revisit.
    test('F2 — base employee may view the org hierarchy tree (intentional) but it carries no grant table', async ({ page }) => {
        await loginAsPersona(page, BASE_PERSONA);
        const resp = await page.goto('/Admin/Organization/Hierarchy', { waitUntil: 'domcontentloaded' });
        const status = resp ? resp.status() : 0;
        const accessible = status === 200 && !/AccessDenied|Auth\/Login/i.test(page.url());
        // The hierarchy page is allowed, but it should not render the grant-assignment table (148-row table).
        const grantTableRows = accessible ? await page.locator('table tbody tr').count() : 0;
        expect(accessible, `Org hierarchy tree should be viewable by staff (status=${status}).`).toBe(true);
        expect(grantTableRows, 'Hierarchy page must not expose a large grant-assignment table').toBeLessThan(50);
    });

    // Positive control: an admin page that IS correctly gated today (should pass now and stay green).
    test('control — base employee is correctly denied /Admin/Users', async ({ page }) => {
        await loginAsPersona(page, BASE_PERSONA);
        const resp = await page.goto('/Admin/Users', { waitUntil: 'domcontentloaded' });
        const status = resp ? resp.status() : 0;
        const url = page.url();
        const denied = status === 403 || /AccessDenied|Auth\/Login/i.test(url);
        expect(denied, `Control failed: /Admin/Users should deny base employee. status=${status} url=${url}`).toBe(true);
    });
});
