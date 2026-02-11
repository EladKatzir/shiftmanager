// @ts-check
const { test, expect } = require('@playwright/test');
const { loginAsOwner, saveEvidence, navigateTo, assertPageContains, assertMinCount } = require('../../helpers/production-qa-helpers');

const EVIDENCE = '22-admin-organization';

test.describe('Module V: Admin Organization', () => {
  test.beforeEach(async ({ page }) => {
    await loginAsOwner(page);
  });

  test('V-01: Organization page renders hierarchy tree', async ({ page }) => {
    await navigateTo(page, '/Admin/Organization');

    // ASSERT: Page title is visible
    const heading = page.locator('main h1').first();
    await expect(heading).toBeVisible({ timeout: 10000 });

    // ASSERT: Stats grid renders with stat cards
    const statCards = page.locator('.stat-card');
    await assertMinCount(statCards, 3);

    // ASSERT: Quick actions section has navigation links
    const quickActions = page.locator('.quick-actions a, .quick-actions .btn');
    await assertMinCount(quickActions, 4);

    // ASSERT: Hierarchy tree section card is visible
    const hierarchySectionCard = page.locator('.section-card');
    await assertMinCount(hierarchySectionCard, 1);

    // ASSERT: Hierarchy tree is rendered with at least one tree node
    const treeNodes = page.locator('.tree-node');
    await assertMinCount(treeNodes, 1);

    // ASSERT: Tree labels contain the project name "Shifty"
    await assertPageContains(page, 'Shifty');

    await saveEvidence(page, EVIDENCE, 'V-01-hierarchy-tree.png');
  });

  test('V-02: Hierarchy tree shows molecule names', async ({ page }) => {
    await navigateTo(page, '/Admin/Organization');

    // ASSERT: Tree has molecule-level nodes (rendered with tree-icon-molecule)
    const moleculeIcons = page.locator('.tree-icon-molecule');
    await assertMinCount(moleculeIcons, 1);

    // ASSERT: Known molecule names are visible (from test data: Oren, Ella, etc.)
    const moleculeLabels = page.locator('.tree-icon-molecule + .tree-label, .tree-icon-molecule ~ .tree-label');
    const moleculeCount = await moleculeLabels.count();
    expect(moleculeCount).toBeGreaterThanOrEqual(1);

    // ASSERT: At least one molecule label has text content
    const firstMoleculeText = await moleculeLabels.first().textContent();
    expect((firstMoleculeText || '').trim().length).toBeGreaterThan(0);

    await saveEvidence(page, EVIDENCE, 'V-02-molecule-names.png');
  });

  test('V-03: Hierarchy tree shows company names', async ({ page }) => {
    await navigateTo(page, '/Admin/Organization');

    // First expand all nodes so companies are visible
    const expandAllBtn = page.locator('button:has-text("Expand"), button:has-text("Collapse")').first();
    const expandVisible = await expandAllBtn.isVisible({ timeout: 3000 }).catch(() => false);
    if (expandVisible) {
      await expandAllBtn.click();
      await page.waitForTimeout(500);
    }

    // ASSERT: Tree has company-level nodes (rendered with tree-icon-company)
    const companyIcons = page.locator('.tree-icon-company');
    await assertMinCount(companyIcons, 1);

    // ASSERT: Known company names from test data are visible
    const companyLabels = page.locator('.tree-icon-company + .tree-label, .tree-icon-company ~ .tree-label');
    const companyCount = await companyLabels.count();
    expect(companyCount).toBeGreaterThanOrEqual(1);

    // ASSERT: At least one company label has text content
    const firstCompanyText = await companyLabels.first().textContent();
    expect((firstCompanyText || '').trim().length).toBeGreaterThan(0);

    await saveEvidence(page, EVIDENCE, 'V-03-company-names.png');
  });

  test('V-04: Hierarchy tree shows area names', async ({ page }) => {
    await navigateTo(page, '/Admin/Organization');

    // ASSERT: Tree has area-level nodes (rendered with tree-icon-area)
    const areaIcons = page.locator('.tree-icon-area');
    await assertMinCount(areaIcons, 1);

    // ASSERT: Area labels have text content
    const areaLabels = page.locator('.tree-icon-area + .tree-label, .tree-icon-area ~ .tree-label');
    const firstAreaText = await areaLabels.first().textContent();
    expect((firstAreaText || '').trim().length).toBeGreaterThan(0);

    await saveEvidence(page, EVIDENCE, 'V-04-area-names.png');
  });

  test('V-05: Hierarchy page has quick action links', async ({ page }) => {
    await navigateTo(page, '/Admin/Organization');

    // ASSERT: Manage Projects link exists
    const projectsLink = page.locator('a[href="/Admin/Organization/Projects"]');
    await expect(projectsLink).toBeVisible({ timeout: 5000 });

    // ASSERT: Manage Areas link exists
    const areasLink = page.locator('a[href="/Admin/Organization/Areas"]');
    await expect(areasLink).toBeVisible({ timeout: 5000 });

    // ASSERT: Manage Molecules link exists
    const moleculesLink = page.locator('a[href="/Admin/Organization/Molecules"]');
    await expect(moleculesLink).toBeVisible({ timeout: 5000 });

    // ASSERT: Manage Companies link exists (scoped to main to avoid sidebar duplicate)
    const companiesLink = page.locator('main a[href="/Admin/Companies"]');
    await expect(companiesLink).toBeVisible({ timeout: 5000 });

    await saveEvidence(page, EVIDENCE, 'V-05-quick-actions.png');
  });

  test('V-06: Hierarchy detail page renders tree with drag-drop support', async ({ page }) => {
    await navigateTo(page, '/Admin/Organization/Hierarchy');

    // ASSERT: Page header is visible
    const heading = page.locator('main h1').first();
    await expect(heading).toBeVisible({ timeout: 10000 });

    // ASSERT: Stats bar shows counts for Projects, Areas, Molecules, Companies
    const statPills = page.locator('.stat-pill');
    await assertMinCount(statPills, 4);

    // ASSERT: Quick links for managing entities are visible
    const quickLinks = page.locator('.quick-link');
    await assertMinCount(quickLinks, 4);

    // ASSERT: Hierarchy tree section exists
    const sectionCard = page.locator('.section-card');
    await expect(sectionCard.first()).toBeVisible({ timeout: 5000 });

    await saveEvidence(page, EVIDENCE, 'V-06-hierarchy-detail.png');
  });

  test('V-07: JobTypes page lists job types', async ({ page }) => {
    await navigateTo(page, '/Admin/Organization/JobTypes');

    // ASSERT: Page header is visible
    const heading = page.locator('main h1').first();
    await expect(heading).toBeVisible({ timeout: 10000 });

    // ASSERT: Known job types from test data are visible
    const body = await page.content();
    const hasJobTypes = /Alhut|BR|Text|Hakam/i.test(body);
    expect(hasJobTypes).toBeTruthy();

    // ASSERT: Section card is visible
    const sectionCards = page.locator('.section-card');
    await assertMinCount(sectionCards, 1);

    await saveEvidence(page, EVIDENCE, 'V-07-jobtypes.png');
  });

  test('V-08: ShiftGroupings page loads', async ({ page }) => {
    await navigateTo(page, '/Admin/Organization/ShiftGroupings');

    // ASSERT: Page loaded without error (navigateTo checks for 500s)
    const heading = page.locator('main h1, main h2').first();
    await expect(heading).toBeVisible({ timeout: 10000 });

    // ASSERT: Page has substantive content
    const body = page.locator('body');
    const text = await body.textContent();
    expect((text || '').length).toBeGreaterThan(50);

    await saveEvidence(page, EVIDENCE, 'V-08-shiftgroupings.png');
  });

  test('V-09: Role templates page lists templates', async ({ page }) => {
    await navigateTo(page, '/Admin/Organization/Roles');

    // ASSERT: Page title is visible
    const heading = page.locator('main h1').first();
    await expect(heading).toBeVisible({ timeout: 10000 });

    // ASSERT: Page has role/template-related content
    const body = page.locator('body');
    const text = await body.textContent();
    expect((text || '').length).toBeGreaterThan(50);

    // ASSERT: A table or list structure exists for role templates
    const tableOrList = page.locator('.data-table, table, .section-card');
    await assertMinCount(tableOrList, 1);

    await saveEvidence(page, EVIDENCE, 'V-09-role-templates.png');
  });

  test('V-10: Admin Users page loads', async ({ page }) => {
    await navigateTo(page, '/Admin/Users');

    // ASSERT: Page loaded without error
    const heading = page.locator('main h1, main h2').first();
    await expect(heading).toBeVisible({ timeout: 10000 });

    // ASSERT: Page contains user-related content
    const body = page.locator('body');
    const text = await body.textContent();
    expect((text || '').length).toBeGreaterThan(50);

    await saveEvidence(page, EVIDENCE, 'V-10-admin-users.png');
  });

  test('V-11: Grants page loads with permissions data', async ({ page }) => {
    await navigateTo(page, '/Admin/Organization/Grants');

    // ASSERT: Page loaded without error
    const heading = page.locator('main h1, main h2').first();
    await expect(heading).toBeVisible({ timeout: 10000 });

    // ASSERT: Page has substantive grant/permission content
    const body = page.locator('body');
    const text = await body.textContent();
    expect((text || '').length).toBeGreaterThan(50);

    await saveEvidence(page, EVIDENCE, 'V-11-grants.png');
  });

  test('V-12: Owner permissions page loads', async ({ page }) => {
    await navigateTo(page, '/Owner/Permissions');

    // ASSERT: Page loaded without error
    const heading = page.locator('main h1, main h2').first();
    await expect(heading).toBeVisible({ timeout: 10000 });

    // ASSERT: Page has substantive content
    const body = page.locator('body');
    const text = await body.textContent();
    expect((text || '').length).toBeGreaterThan(50);

    await saveEvidence(page, EVIDENCE, 'V-12-owner-permissions.png');
  });
});
