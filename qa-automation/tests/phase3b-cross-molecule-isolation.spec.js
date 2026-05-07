// @ts-check
/**
 * PHASE 3b — Cross-Molecule Isolation Within The Same Area
 *
 * Attacker: Oren Alhut Lead (mgr.alhut.tz@test, Tzafona/Oren, Lead role, Alhut JobType)
 * Victims:
 *   - Oren Element Employee (emp.elem.alhut@test) — different molecule, same area
 *   - Oren Hitazmut Employee (emp.hit.alhut@test) — different molecule, same area
 *
 * The Lead role is scoped to CompanyJobType. Its `AssignAlhutShifts` grant has
 * `ExpandToMolecule + UseOwnJobType` — meaning the Lead can assign shifts to
 * Alhut-jobtype users WITHIN their own molecule (Oren). Element and Hitazmut
 * are *different molecules*, so attacks targeting them must fail.
 *
 * The Lead's `AssignChores` grant is `ExpandToMolecule` — same molecule scope.
 *
 * Cross-molecule success here = P0 security breach.
 */
const { test, expect } = require('@playwright/test');
const {
    loginAsPersona,
    jsonPost,
    lookupUserIds,
} = require('../helpers/persona-helpers');
const { dateOffset } = require('../helpers/test-data-helpers');

test.describe('Phase 3b — Cross-molecule isolation (Oren-Alhut Lead → Element/Hitazmut)', () => {

    /** @type {{ [email: string]: number }} */
    let userIds;

    test.beforeAll(async ({ browser }) => {
        const ctx = await browser.newContext();
        userIds = await lookupUserIds(ctx, [
            'mgr.alhut.tz@test',
            'emp.elem.alhut@test',
            'emp.hit.alhut@test',
            'emp.tz.alhut@test',
            'emp.tz.text@test',
        ]);
        await ctx.close();
    });

    // ──────────────────────────────────────────────────────────────────────
    test('3b.1  AssignAlhutShifts to an Element-molecule user is denied', async ({ page }) => {
        await loginAsPersona(page, 'OrenAlhutLead');

        const resp = await jsonPost(page, '/Calendar/Table?handler=AssignEmployee', {
            shiftTypeId: 1,                            // any shiftTypeId — auth check fires first
            date: dateOffset(15),
            userId: userIds['emp.elem.alhut@test'],    // VICTIM in Element molecule
        });

        // Either page-policy gate or in-handler scope check rejects.
        // BUSY validation can also reject with 200+success=false — that's also a denial.
        const blocked =
            (resp.status === 403 || resp.status === 401)
            || (resp.status === 200 && resp.body?.success === false);

        expect.soft(blocked, `cross-molecule shift POST must be denied — got ${resp.status} body=${JSON.stringify(resp.body).slice(0, 300)}`).toBe(true);
        expect.soft(resp.body?.success, 'must NOT report success').not.toBe(true);
    });

    // ──────────────────────────────────────────────────────────────────────
    test('3b.2  AssignChores to a Hitazmut-molecule user is denied', async ({ page }) => {
        await loginAsPersona(page, 'OrenAlhutLead');

        const resp = await jsonPost(page, '/Api/Calendar/QuickAddChore', {
            assigneeId: userIds['emp.hit.alhut@test'],  // VICTIM in Hitazmut molecule
            date: dateOffset(16),
            title: 'P3b.2 cross-molecule chore attack',
        });

        expect.soft(resp.status, `cross-molecule chore POST status — body=${JSON.stringify(resp.body).slice(0, 300)}`).toBe(403);
        expect.soft(resp.body?.success, 'must NOT report success').toBe(false);
        expect.soft(typeof resp.body?.message, 'must surface localized error').toBe('string');
    });

    // ──────────────────────────────────────────────────────────────────────
    test('3b.3  Cross-JobType within own molecule is now ENFORCED (Finding #3 fix)', async ({ page }) => {
        // After the 2026-05-07 fix in GrantService.HasGrantWithScopeAsync (jobTypeMismatch
        // filter on molecule/area branches) + Calendar/Table.cshtml.cs OnPostAssignEmployeeAsync
        // (passes shift's JobTypeId), an Alhut Lead's seeded AssignTextShifts grant
        // (stored with JobTypeId=Alhut due to useOwnJobType:true) is no longer matched for a
        // Text-jobtype shift. The handler returns 403.
        await loginAsPersona(page, 'OrenAlhutLead');

        const resp = await jsonPost(page, '/Calendar/Table?handler=AssignEmployee', {
            shiftTypeId: 5,  // Text-jobtype shift in seed data
            date: dateOffset(450 + Math.floor(Math.random() * 60)),
            userId: userIds['emp.tz.text@test'],
        });

        expect.soft(resp.status, `Alhut Lead → Text shift must be 403 after fix — got ${resp.status} body=${JSON.stringify(resp.body).slice(0, 300)}`).toBe(403);
        expect.soft(resp.body?.success, 'must NOT report success').not.toBe(true);
        expect.soft(resp.body?.assignmentId, 'must NOT return an assignmentId').toBeUndefined();
    });

    // ──────────────────────────────────────────────────────────────────────
    test('3b.4  Sanity: same Lead can assign to in-molecule Alhut user (proves prior denials are scope-based, not blanket)', async ({ page }) => {
        await loginAsPersona(page, 'OrenAlhutLead');

        const resp = await jsonPost(page, '/Calendar/Table?handler=AssignEmployee', {
            shiftTypeId: 1,
            date: dateOffset(18),
            userId: userIds['emp.tz.alhut@test'],   // SAME molecule, SAME jobtype
        });

        // Auth check should pass; assignment may succeed or hit a busy warning — both are fine.
        const okOrWarned =
            (resp.status === 200) && (resp.body?.success === true || resp.body?.requiresOverride === true || (resp.body?.success === false && /BUSY|already|conflict/i.test(JSON.stringify(resp.body))));

        expect.soft(okOrWarned, `in-scope shift should reach service layer — got ${resp.status} body=${JSON.stringify(resp.body).slice(0, 300)}`).toBe(true);
        expect.soft([401, 403], 'in-scope shift must not be auth-denied').not.toContain(resp.status);
    });
});
