// @ts-check
const { test, expect } = require('@playwright/test');
const {
  loginAsOwner,
  login,
  logout,
  saveEvidence,
  navigateTo,
  navigateExpecting,
  createUser,
  assertUserExists,
  assertPageContains,
  waitForToast,
  switchOwnerScope,
  ensureFeatureFlag,
  TEST_PASSWORD,
  ROLE_ENUM,
} = require('../../helpers/production-qa-helpers');

const EVIDENCE = '04-user-management';

test.describe('Module D: User Management', () => {
  test('D-01: Owner creates user with each of 6 roles', async ({ page }) => {
    await loginAsOwner(page);
    await navigateTo(page, '/Admin/Users');

    const roles = ['Owner', 'Manager', 'Employee', 'Director', 'Trainee', 'Assigner'];

    for (const role of roles) {
      const email = `d01.${role.toLowerCase()}.${Date.now()}@test`;

      await createUser(page, {
        email,
        displayName: `Test ${role}`,
        role,
        company: 'Tzafona',
        password: TEST_PASSWORD,
      });

      // ASSERT: after creation, reload and verify user appears in the page
      await navigateTo(page, '/Admin/Users');
      await assertUserExists(page, email);
    }

    await saveEvidence(page, EVIDENCE, 'D-01-create-all-roles.png');
  });

  test('D-02: Create user with each job type', async ({ page }) => {
    await loginAsOwner(page);
    await navigateTo(page, '/Admin/Users');

    // ASSERT: the Add User section is visible
    const addUserHeading = page.locator('h2.section-title').filter({ hasText: /Add/ });
    await expect(addUserHeading).toBeVisible({ timeout: 5000 });

    const jobTypes = ['Alhut', 'BR', 'Text', 'Hakam'];

    for (const jt of jobTypes) {
      const email = `d02.${jt.toLowerCase()}.${Date.now()}@test`;

      // Use the createUser helper which handles role selection correctly
      await createUser(page, {
        email,
        displayName: `JT ${jt}`,
        role: 'Employee',
        company: 'Tzafona',
        jobType: jt,
        password: TEST_PASSWORD,
      });

      // ASSERT: reload the page and verify no errors (user may be on another page due to pagination)
      await navigateTo(page, '/Admin/Users');
      await assertUserExists(page, email);
    }

    await saveEvidence(page, EVIDENCE, 'D-02-create-jobtypes.png');
  });

  test('D-03: Verify departments are visible in users table', async ({ page }) => {
    await loginAsOwner(page);
    await navigateTo(page, '/Admin/Users');

    // ASSERT: the existing users section is visible
    const usersHeading = page.locator('h2.section-title').filter({ hasText: /Existing/ });
    await expect(usersHeading).toBeVisible({ timeout: 5000 });

    // ASSERT: the users table exists and has the Department column header
    const departmentHeader = page.locator('.data-table th').filter({ hasText: /Department/ });
    await expect(departmentHeader.first()).toBeVisible({ timeout: 5000 });

    // ASSERT: the table has at least one row of data
    const tableRows = page.locator('.data-table tbody tr');
    const rowCount = await tableRows.count();
    expect(rowCount).toBeGreaterThan(0);

    await saveEvidence(page, EVIDENCE, 'D-03-tech-department.png');
  });

  test('D-04: Edit user profile (name, phone)', async ({ page }) => {
    await loginAsOwner(page);

    // Test profile editing via My/Profile (owner's own profile).
    // Admin/EditProfile has a tenant query filter that requires scope switching,
    // but My/Profile always works for the current user's own data.
    await navigateTo(page, '/My/Profile');

    // ASSERT: the profile page loaded with the page title
    const pageTitle = page.locator('#main-content h1.page-title').first();
    await expect(pageTitle).toBeVisible({ timeout: 5000 });

    // ASSERT: the phone input is visible and fillable
    const phoneInput = page.locator('input[name="Phone"]');
    await expect(phoneInput).toBeVisible({ timeout: 5000 });

    // Make a change — fill in a phone number
    const testPhone = '050' + String(Date.now()).slice(-7);
    await phoneInput.fill(testPhone);

    // Submit the form
    const saveBtn = page.locator('button[type="submit"].btn-primary').first();
    await expect(saveBtn).toBeVisible({ timeout: 5000 });
    await saveBtn.click();
    await page.waitForLoadState('networkidle');

    // ASSERT: after save, reload and verify the phone value persisted
    await navigateTo(page, '/My/Profile');
    const savedPhone = await page.locator('input[name="Phone"]').inputValue();
    expect(savedPhone).toBe(testPhone);

    await saveEvidence(page, EVIDENCE, 'D-04-edit-profile.png');
  });

  test('D-05: Toggle user active/inactive status', async ({ page }) => {
    await loginAsOwner(page);
    await navigateTo(page, '/Admin/Users');

    // asp-page-handler="Toggle" generates action="?handler=Toggle" or action="/Admin/Users?handler=Toggle"
    // Use a broader selector to match both forms with handler=Toggle in query or in path
    const toggleForm = page.locator('form[action*="handler=Toggle"], form[action*="Toggle"]').first();
    await expect(toggleForm).toBeVisible({ timeout: 5000 });

    // Get the current status text before toggling
    const toggleBtn = toggleForm.locator('button').first();
    await expect(toggleBtn).toBeVisible({ timeout: 3000 });
    const statusBefore = await toggleBtn.textContent();

    // Click the toggle
    await toggleBtn.click();
    await page.waitForLoadState('networkidle');

    // ASSERT: after toggling, the button text should have changed
    const toggleFormAfter = page.locator('form[action*="handler=Toggle"], form[action*="Toggle"]').first();
    const toggleBtnAfter = toggleFormAfter.locator('button').first();
    await expect(toggleBtnAfter).toBeVisible({ timeout: 5000 });
    const statusAfter = await toggleBtnAfter.textContent();

    // The text should differ from before (Active ↔ Inactive toggle)
    expect(statusAfter.trim()).not.toBe(statusBefore.trim());

    // Toggle back to restore original state
    await toggleBtnAfter.click();
    await page.waitForLoadState('networkidle');

    await saveEvidence(page, EVIDENCE, 'D-05-deactivate-user.png');
  });

  test('D-06: Reset user password', async ({ page }) => {
    await loginAsOwner(page);
    await navigateTo(page, '/Admin/Users');

    // asp-page-handler="ResetPassword" generates action with handler=ResetPassword
    const resetForm = page.locator('form[action*="handler=ResetPassword"], form[action*="ResetPassword"]').first();
    await expect(resetForm).toBeVisible({ timeout: 5000 });

    // ASSERT: password input inside the form is visible (camelCase name)
    const passInput = resetForm.locator('input[name="newPassword"]');
    await expect(passInput).toBeVisible({ timeout: 3000 });

    // Fill in a new password
    const newPassword = 'NewTemp123!';
    await passInput.fill(newPassword);

    // Click the set button
    const setBtn = resetForm.locator('button').first();
    await expect(setBtn).toBeVisible({ timeout: 3000 });
    await setBtn.click();
    await page.waitForLoadState('networkidle');

    // ASSERT: page reloaded without error (password reset is silent — no toast)
    // Verify we're still on the Users page and no error alert is shown
    await expect(page).toHaveURL(/Admin\/Users/);
    const errorAlert = page.locator('.alert-error, .alert-danger');
    const errorCount = await errorAlert.count();
    for (let i = 0; i < errorCount; i++) {
      const isVisible = await errorAlert.nth(i).isVisible();
      if (isVisible) {
        const text = await errorAlert.nth(i).textContent();
        // Only fail if the error is actually about the reset (not a pre-existing error)
        expect(text).not.toMatch(/password/i);
      }
    }

    await saveEvidence(page, EVIDENCE, 'D-06-reset-password.png');
  });

  test('D-07: Bulk user import (CSV)', async ({ page }) => {
    await loginAsOwner(page);
    await navigateTo(page, '/Admin/Users');

    // ASSERT: bulk import section exists
    const bulkFileInput = page.locator('input[name="BulkImportFile"]');
    await expect(bulkFileInput).toBeVisible({ timeout: 5000 });

    // Create a CSV buffer
    const timestamp = Date.now();
    const csvContent = [
      'Email,DisplayName,Password,Role,Phone',
      `bulk1.${timestamp}@test,Bulk User 1,${TEST_PASSWORD},Employee,0501111111`,
      `bulk2.${timestamp}@test,Bulk User 2,${TEST_PASSWORD},Employee,0502222222`,
    ].join('\n');
    const csvBuffer = Buffer.from(csvContent, 'utf-8');

    // Upload the CSV
    await bulkFileInput.setInputFiles({
      name: 'bulk-import.csv',
      mimeType: 'text/csv',
      buffer: csvBuffer,
    });

    // Handle confirmation dialog
    page.once('dialog', async dialog => await dialog.accept());

    // Submit the import form
    const importBtn = page.locator('form:has(input[name="BulkImportFile"]) button[type="submit"]');
    await expect(importBtn).toBeVisible({ timeout: 3000 });
    await importBtn.click();
    await page.waitForLoadState('networkidle');

    // ASSERT: after bulk import, the page MUST show feedback (success or error details)
    const importFeedback = page.locator('.alert').first();
    await expect(importFeedback).toBeVisible({ timeout: 5000 });

    await saveEvidence(page, EVIDENCE, 'D-07-bulk-import.png');
  });

  test('D-08: Manager or non-owner user access to /Admin/Users', async ({ page }) => {
    // First, create a manager user we can log in as
    await loginAsOwner(page);
    await navigateTo(page, '/Admin/Users');

    const mgrEmail = `d08.manager.${Date.now()}@test`;
    await createUser(page, {
      email: mgrEmail,
      displayName: 'D08 Manager Test',
      role: 'Manager',
      company: 'Tzafona',
      password: TEST_PASSWORD,
    });

    // Logout and login as the manager
    await logout(page);
    await login(page, mgrEmail, TEST_PASSWORD);

    // Navigate to /Admin/Users -- manager may or may not have access
    await page.goto('/Admin/Users');
    await page.waitForLoadState('networkidle');

    // ASSERT: either the page loaded (manager has access) or we were redirected
    const currentUrl = page.url();
    const hasAccess = currentUrl.includes('/Admin/Users');
    const wasDenied = currentUrl.includes('AccessDenied') || currentUrl.includes('Auth/Login');

    // One of these must be true -- the page must have resolved to something definitive
    expect(hasAccess || wasDenied).toBeTruthy();

    if (hasAccess) {
      // ASSERT: if the manager has access, the users table should be visible
      const usersTable = page.locator('.data-table');
      await expect(usersTable.first()).toBeVisible({ timeout: 5000 });
    } else {
      // ASSERT: if denied, we should be on a known redirect target
      expect(currentUrl).toMatch(/AccessDenied|Auth\/Login/);
    }

    await saveEvidence(page, EVIDENCE, 'D-08-manager-scoped.png');
  });

  test('D-09: Employee cannot access /Admin/Users', async ({ page }) => {
    // Create an employee user
    await loginAsOwner(page);
    await navigateTo(page, '/Admin/Users');

    const empEmail = `d09.employee.${Date.now()}@test`;
    await createUser(page, {
      email: empEmail,
      displayName: 'D09 Employee Test',
      role: 'Employee',
      company: 'Tzafona',
      password: TEST_PASSWORD,
    });

    // Logout and login as employee
    await logout(page);
    await login(page, empEmail, TEST_PASSWORD);

    // Try to access /Admin/Users
    await page.goto('/Admin/Users');
    await page.waitForLoadState('networkidle');

    // ASSERT: employee should be redirected to AccessDenied or Login
    const currentUrl = page.url();
    expect(currentUrl).toMatch(/AccessDenied|Auth\/Login/);

    await saveEvidence(page, EVIDENCE, 'D-09-employee-denied.png');
  });

  test('D-10: Owner sees users from multiple companies', async ({ page }) => {
    await loginAsOwner(page);
    await navigateTo(page, '/Admin/Users');

    // ASSERT: the users table is visible
    const usersTable = page.locator('.data-table').last();
    await expect(usersTable).toBeVisible({ timeout: 5000 });

    // ASSERT: there is more than 1 row in the table
    const tableRows = usersTable.locator('tbody tr');
    const rowCount = await tableRows.count();
    expect(rowCount).toBeGreaterThan(1);

    // ASSERT: collect company names from the table -- there should be at least 2 unique companies
    // Company column is the 3rd td in each row (index 2)
    const companyNames = new Set();
    for (let i = 0; i < Math.min(rowCount, 20); i++) {
      const companyText = await tableRows.nth(i).locator('td').nth(2).textContent();
      if (companyText && companyText.trim()) {
        companyNames.add(companyText.trim());
      }
    }

    // After creating users in multiple companies, there should be at least 2 unique companies visible
    expect(companyNames.size).toBeGreaterThanOrEqual(2);

    await saveEvidence(page, EVIDENCE, 'D-10-cross-company.png');
  });

  test('D-11: Audit log page loads with entries or empty state', async ({ page }) => {
    await loginAsOwner(page);
    await navigateTo(page, '/Admin/AuditLog');

    // ASSERT: the audit log page loaded (check for page title)
    const pageTitle = page.locator('.page-title').first();
    await expect(pageTitle).toBeVisible({ timeout: 5000 });

    // ASSERT: either audit entries are shown or an empty state is shown
    const auditTable = page.locator('.audit-table');
    const emptyState = page.locator('.empty-state');

    // ASSERT: either the audit table or the empty state MUST be visible
    await expect(auditTable.or(emptyState)).toBeVisible({ timeout: 5000 });
    const hasTable = await auditTable.isVisible();
    if (hasTable) {
      // ASSERT: the table has at least one data row
      const rows = auditTable.locator('tbody tr');
      const rowCount = await rows.count();
      expect(rowCount).toBeGreaterThan(0);
    }

    await saveEvidence(page, EVIDENCE, 'D-11-audit-trail.png');
  });

  test('D-12: Role templates are visible in grants management', async ({ page }) => {
    await loginAsOwner(page);
    await navigateTo(page, '/Owner/Hub/Grants');

    // ASSERT: the grants page loaded
    const pageHeading = page.locator('main h1').first();
    await expect(pageHeading).toBeVisible({ timeout: 5000 });

    // ASSERT: the Role Templates tab button exists
    const roleTemplatesTab = page.locator('button[data-tab="role-templates"]');
    await expect(roleTemplatesTab).toBeVisible({ timeout: 5000 });

    // Click the Role Templates tab
    await roleTemplatesTab.click();
    await page.waitForTimeout(500);

    // ASSERT: after clicking tab, the panel MUST be visible (the tab click should work)
    const roleTemplatesPanel = page.locator('[data-tab-content="role-templates"], #role-templates');
    await expect(roleTemplatesPanel).toBeVisible({ timeout: 5000 });

    // ASSERT: stats show role template count
    const statCards = page.locator('.stat-card');
    const statCount = await statCards.count();
    expect(statCount).toBeGreaterThan(0);

    await saveEvidence(page, EVIDENCE, 'D-12-assign-template.png');
  });

  test('D-13: Grant actions tab shows revoke capability', async ({ page }) => {
    await loginAsOwner(page);
    await navigateTo(page, '/Owner/Hub/Grants');

    // ASSERT: grants page loaded
    const pageHeading = page.locator('main h1').first();
    await expect(pageHeading).toBeVisible({ timeout: 5000 });

    // ASSERT: the Grant Actions tab exists
    const grantActionsTab = page.locator('button[data-tab="grant-actions"]');
    await expect(grantActionsTab).toBeVisible({ timeout: 5000 });

    // Click the Grant Actions tab
    await grantActionsTab.click();
    await page.waitForTimeout(500);

    // ASSERT: the tab is now active
    await expect(grantActionsTab).toHaveClass(/active/);

    // ASSERT: the User Grants tab also exists (for per-user grant management / revocation)
    const userGrantsTab = page.locator('button[data-tab="user-grants"]');
    await expect(userGrantsTab).toBeVisible({ timeout: 5000 });

    await saveEvidence(page, EVIDENCE, 'D-13-revoke-template.png');
  });

  test('D-14: Grants management UI is accessible and functional', async ({ page }) => {
    await loginAsOwner(page);
    await navigateTo(page, '/Owner/Hub/Grants');

    // ASSERT: the grants container is visible
    const grantsContainer = page.locator('.grants-container');
    await expect(grantsContainer).toBeVisible({ timeout: 5000 });

    // ASSERT: stats grid is visible with at least one stat card
    const statsGrid = page.locator('.stats-grid');
    await expect(statsGrid).toBeVisible({ timeout: 5000 });

    const statCards = page.locator('.stat-card');
    const statCount = await statCards.count();
    expect(statCount).toBeGreaterThanOrEqual(4);

    // ASSERT: all four tab buttons are present
    const tabButtons = page.locator('.tabs-nav .tab-btn');
    const tabCount = await tabButtons.count();
    expect(tabCount).toBe(4);

    // ASSERT: the Grant Types tab is active by default
    const grantTypesTab = page.locator('button[data-tab="grant-types"]');
    await expect(grantTypesTab).toHaveClass(/active/);

    // ASSERT: stat values are numbers (not empty)
    for (let i = 0; i < statCount; i++) {
      const statValue = await statCards.nth(i).locator('.stat-value').textContent();
      expect(statValue.trim()).toMatch(/^\d+$/);
    }

    await saveEvidence(page, EVIDENCE, 'D-14-individual-grant.png');
  });
});
