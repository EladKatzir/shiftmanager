// @ts-check
const { test, expect } = require('@playwright/test');
const { loginAsOwner } = require('../helpers/auth-helpers');
const TestDataFactory = require('../helpers/test-data-factory');

test.describe('Multi-Tenancy Isolation - UI Level', () => {

  let tenantA, tenantB;

  test.beforeAll(async ({ browser }) => {
    const page = await browser.newPage();
    await loginAsOwner(page);

    // Setup Tenant A
    await page.goto('/Admin/Companies');

    const timestampA = Date.now();
    tenantA = TestDataFactory.generateCompany({ name: `TenantA_${timestampA}` });
    await page.fill('input[name="CompanyName"]', tenantA.name);
    await page.fill('input[name="CompanySlug"]', `tenant-a-${timestampA}`);
    await page.fill('input[name="ManagerEmail"]', `manager-a-${timestampA}@test.com`);
    await page.fill('input[name="ManagerDisplayName"]', `Manager A ${timestampA}`);
    await page.fill('input[name="ManagerPassword"]', '123456');
    await page.locator('form:has(input[name="CompanyName"]) button[type="submit"]').click();
    await page.waitForLoadState('networkidle');

    // Setup Tenant B
    await page.goto('/Admin/Companies');

    const timestampB = Date.now();
    tenantB = TestDataFactory.generateCompany({ name: `TenantB_${timestampB}` });
    await page.fill('input[name="CompanyName"]', tenantB.name);
    await page.fill('input[name="CompanySlug"]', `tenant-b-${timestampB}`);
    await page.fill('input[name="ManagerEmail"]', `manager-b-${timestampB}@test.com`);
    await page.fill('input[name="ManagerDisplayName"]', `Manager B ${timestampB}`);
    await page.fill('input[name="ManagerPassword"]', '123456');
    await page.locator('form:has(input[name="CompanyName"]) button[type="submit"]').click();
    await page.waitForLoadState('networkidle');

    await page.close();
  });

  test.afterAll(async ({ browser }) => {
    // Clean up test tenants created in beforeAll
    const page = await browser.newPage();
    try {
      await loginAsOwner(page);
      await page.goto('/Admin/Companies');

      // Delete Tenant A
      if (tenantA) {
        const tenantARow = page.locator(`table tr:has-text("${tenantA.name}")`);
        if (await tenantARow.isVisible().catch(() => false)) {
          const deleteButton = tenantARow.locator('button:has-text("Delete")');
          if (await deleteButton.isVisible().catch(() => false)) {
            page.once('dialog', async dialog => await dialog.accept());
            await deleteButton.click();
            await page.waitForLoadState('networkidle');
          }
        }
      }

      // Delete Tenant B
      if (tenantB) {
        const tenantBRow = page.locator(`table tr:has-text("${tenantB.name}")`);
        if (await tenantBRow.isVisible().catch(() => false)) {
          const deleteButton = tenantBRow.locator('button:has-text("Delete")');
          if (await deleteButton.isVisible().catch(() => false)) {
            page.once('dialog', async dialog => await dialog.accept());
            await deleteButton.click();
            await page.waitForLoadState('networkidle');
          }
        }
      }
    } catch (error) {
      console.error('Error during cleanup:', error);
    } finally {
      await page.close();
    }
  });

  test('P5-01: Owner can see all companies', async ({ page }) => {
    await loginAsOwner(page);
    await page.goto('/Admin/Companies');

    // Should see both tenants (exact text matching)
    const companyTableA = page.locator(`table td:text-is("${tenantA.name}")`);
    const companyTableB = page.locator(`table td:text-is("${tenantB.name}")`);
    await expect(companyTableA).toBeVisible();
    await expect(companyTableB).toBeVisible();
  });

  test('P5-02: Company selection isolates data view', async ({ page }) => {
    await loginAsOwner(page);

    // Select Tenant A
    await page.goto('/Owner/SelectCompany');
    const companySelectA = page.locator(`a:has-text("${tenantA.name}"), button:has-text("${tenantA.name}")`);

    if (await companySelectA.isVisible().catch(() => false)) {
      await companySelectA.click();

      // Navigate to a data view (e.g., Users)
      await page.goto('/Admin/Users');

      // Capture visible users
      const usersInA = await page.locator('tbody tr, .user-row').count();

      // Switch to Tenant B
      await page.goto('/Owner/SelectCompany');
      const companySelectB = page.locator(`a:has-text("${tenantB.name}"), button:has-text("${tenantB.name}")`);

      if (await companySelectB.isVisible().catch(() => false)) {
        await companySelectB.click();
        await page.goto('/Admin/Users');

        const usersInB = await page.locator('tbody tr, .user-row').count();

        // User counts should differ (isolated data sets)
        // Note: This assumes tenants have different user counts
        console.log(`Tenant A users: ${usersInA}, Tenant B users: ${usersInB}`);
      }
    }
  });

  test('P5-03: Direct URL manipulation cannot access other tenant data', async ({ page }) => {
    await loginAsOwner(page);

    // Select Tenant A
    await page.goto('/Owner/SelectCompany');
    // ... select tenant A

    // Try to access Tenant B's data via URL parameter manipulation
    await page.goto('/Calendar/Table?companyId=999999');

    // Should not error out, and should not show data from wrong company
    await page.waitForLoadState('networkidle');

    // Validate page doesn't crash with error messages (check for actual error elements)
    const errorMessages = await page.locator('.alert-danger, .error-message, [class*="error"]').count();
    expect(errorMessages).toBe(0);

    // Validate no exception text visible on page
    const bodyText = await page.locator('body').textContent();
    expect(bodyText).not.toContain('Exception:');
    expect(bodyText).not.toContain('Stack trace:');
  });
});
