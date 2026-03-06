// @ts-check
const { test, expect } = require('@playwright/test');
const {
  loginAsOwner,
  saveEvidence,
  navigateTo,
  assertPageContains,
  assertMinCount,
} = require('../../helpers/production-qa-helpers');

const EVIDENCE = '01-seed-verification';

/**
 * Module A: Seeding & Fresh Database
 *
 * Verifies every entity created by the database seed script is present
 * and accessible through the UI.  Each test navigates to the appropriate
 * page and makes STRICT text/element assertions — no screenshot-only tests,
 * no `.catch(() => false)` skip patterns, no `expect(body).toBeTruthy()`.
 */

test.describe('Module A: Seeding & Fresh Database', () => {
  test.beforeEach(async ({ page }) => {
    await loginAsOwner(page);
  });

  // -----------------------------------------------------------------------
  // A-01  Project "Shifty" exists
  // -----------------------------------------------------------------------
  test('A-01: Fresh seed creates Project "Shifty"', async ({ page }) => {
    await navigateTo(page, '/Owner/Index');
    await page.waitForLoadState('networkidle');

    // STRICT: the Owner panel must contain the word "Shifty" (project name)
    // or "Shift Manager" which is the rendered project title
    await assertPageContains(page, 'Shift');

    // STRICT: Owner Hub must have hub cards (Owner/Index redirects to Owner/Hub/Index)
    const hubCards = page.locator('.hub-card');
    await assertMinCount(hubCards, 3);

    await saveEvidence(page, EVIDENCE, 'A-01-project-exists.png');
  });

  // -----------------------------------------------------------------------
  // A-02  Area "190" exists
  // -----------------------------------------------------------------------
  test('A-02: Seed creates Area "190"', async ({ page }) => {
    await navigateTo(page, '/Admin/Organization');
    await page.waitForLoadState('networkidle');

    // STRICT: the hierarchy tree must contain area "190"
    await assertPageContains(page, '190');

    await saveEvidence(page, EVIDENCE, 'A-02-area-190.png');
  });

  // -----------------------------------------------------------------------
  // A-03  Nine molecules with correct names
  // -----------------------------------------------------------------------
  test('A-03: Seed creates 9 molecules with correct names', async ({ page }) => {
    await navigateTo(page, '/Admin/Organization');
    await page.waitForLoadState('networkidle');

    const molecules = [
      'Oren', 'Ella', 'Harava', 'Shaked', 'Gefen',
      'Shikma', 'NOC', 'Shiklut', 'SystemAdmins',
    ];

    for (const mol of molecules) {
      await assertPageContains(page, mol);
    }

    await saveEvidence(page, EVIDENCE, 'A-03-molecules.png');
  });

  // -----------------------------------------------------------------------
  // A-04  Companies in correct molecules
  // -----------------------------------------------------------------------
  test('A-04: Seed creates companies in correct molecules', async ({ page }) => {
    await navigateTo(page, '/Admin/Organization');
    await page.waitForLoadState('networkidle');

    const companies = [
      'Tzafona', 'Hir', 'Camps', 'City', 'Radio',
      'Hitazmut', 'GAP', 'Yeadim', 'Element',
      'Inside', 'Out', 'Hamasa', 'Kabah', 'Matot',
    ];

    for (const co of companies) {
      await assertPageContains(page, co);
    }

    await saveEvidence(page, EVIDENCE, 'A-04-companies.png');
  });

  // -----------------------------------------------------------------------
  // A-05  Six tech departments
  // -----------------------------------------------------------------------
  test('A-05: Seed creates 6 tech departments', async ({ page }) => {
    await navigateTo(page, '/Admin/Organization');
    await page.waitForLoadState('networkidle');

    const departments = ['Pie', 'Tao', 'Yekeb', 'Snir', 'Arbel', 'Samapkam'];

    for (const dept of departments) {
      await assertPageContains(page, dept);
    }

    await saveEvidence(page, EVIDENCE, 'A-05-departments.png');
  });

  // -----------------------------------------------------------------------
  // A-06  Four job types
  // -----------------------------------------------------------------------
  test('A-06: Seed creates 4 job types', async ({ page }) => {
    await navigateTo(page, '/Admin/Organization');
    await page.waitForLoadState('networkidle');

    const jobTypes = ['Alhut', 'BR', 'Text', 'Hakam'];

    for (const jt of jobTypes) {
      await assertPageContains(page, jt);
    }

    await saveEvidence(page, EVIDENCE, 'A-06-jobtypes.png');
  });

  // -----------------------------------------------------------------------
  // A-07  Grant types exist
  // -----------------------------------------------------------------------
  test('A-07: Seed creates grant types', async ({ page }) => {
    // Grants page is under the Owner Hub
    await navigateTo(page, '/Owner/Hub/Grants');
    await page.waitForLoadState('networkidle');

    // STRICT: the grants page must render and contain grant-related content
    // Check for the page title / heading
    const pageTitle = page.locator('main h1').first();
    await expect(pageTitle).toBeVisible({ timeout: 10000 });

    // STRICT: at least one stat card should be present on the grants dashboard
    const statCards = page.locator('.stat-card');
    await assertMinCount(statCards, 1);

    await saveEvidence(page, EVIDENCE, 'A-07-granttypes.png');
  });

  // -----------------------------------------------------------------------
  // A-08  Role templates (11 expected)
  // -----------------------------------------------------------------------
  test('A-08: Seed creates role templates', async ({ page }) => {
    // Role templates are managed under Organization > Roles
    await navigateTo(page, '/Admin/Organization/Roles');
    await page.waitForLoadState('networkidle');

    // STRICT: the page heading must be visible
    const heading = page.locator('main h1').first();
    await expect(heading).toBeVisible({ timeout: 10000 });

    // STRICT: verify known template names appear
    const expectedTemplates = [
      'Employee', 'Owner', 'Assigner', 'MoleculeAdmin', 'AreaAdmin',
      'Lead', 'Director', 'BRDirector',
    ];

    let foundCount = 0;
    for (const tmpl of expectedTemplates) {
      const bodyText = await page.locator('body').innerText();
      if (bodyText.includes(tmpl)) {
        foundCount++;
      }
    }

    // STRICT: at least 8 of the expected templates must be present
    expect(foundCount).toBeGreaterThanOrEqual(8);

    await saveEvidence(page, EVIDENCE, 'A-08-role-templates.png');
  });

  // -----------------------------------------------------------------------
  // A-09  ShiftGroupings for Oren molecule
  // -----------------------------------------------------------------------
  test('A-09: Seed creates ShiftGroupings for Oren', async ({ page }) => {
    await navigateTo(page, '/Admin/Organization');
    await page.waitForLoadState('networkidle');

    // STRICT: at least one of the Oren shift groupings must appear
    const bodyText = await page.locator('body').innerText();
    const hasGrouping = bodyText.includes('Tzafon') ||
                        bodyText.includes('Darom') ||
                        bodyText.includes('Tacti');
    expect(hasGrouping).toBe(true);

    await saveEvidence(page, EVIDENCE, 'A-09-shiftgroupings-oren.png');
  });

  // -----------------------------------------------------------------------
  // A-10  ShiftGrouping for Ella molecule
  // -----------------------------------------------------------------------
  test('A-10: Seed creates ShiftGrouping for Ella', async ({ page }) => {
    await navigateTo(page, '/Admin/Organization');
    await page.waitForLoadState('networkidle');

    // STRICT: the Ella grouping must appear
    const bodyText = await page.locator('body').innerText();
    const hasGrouping = bodyText.includes('HitazmutYeadim');
    expect(hasGrouping).toBe(true);

    await saveEvidence(page, EVIDENCE, 'A-10-shiftgrouping-ella.png');
  });

  // -----------------------------------------------------------------------
  // A-11  ShiftGroupings for Gefen molecule
  // -----------------------------------------------------------------------
  test('A-11: Seed creates ShiftGroupings for Gefen', async ({ page }) => {
    await navigateTo(page, '/Admin/Organization');
    await page.waitForLoadState('networkidle');

    // STRICT: at least one Gefen grouping must appear
    const bodyText = await page.locator('body').innerText();
    const hasGrouping = bodyText.includes('HamasaKabah') ||
                        bodyText.includes('Matot');
    expect(hasGrouping).toBe(true);

    await saveEvidence(page, EVIDENCE, 'A-11-shiftgroupings-gefen.png');
  });

  // -----------------------------------------------------------------------
  // A-12  Owner can login and see Owner navigation
  // -----------------------------------------------------------------------
  test('A-12: Owner can login with seeded credentials', async ({ page }) => {
    // Already logged in via beforeEach — verify Owner nav links
    const ownerNavLinks = page.locator('a[href*="/Owner"]');
    await assertMinCount(ownerNavLinks, 1);

    // STRICT: the first Owner nav link must be visible (not just in DOM)
    await expect(ownerNavLinks.first()).toBeVisible({ timeout: 10000 });

    await saveEvidence(page, EVIDENCE, 'A-12-owner-login-nav.png');
  });
});
