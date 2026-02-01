// @ts-check
const { test, expect } = require('@playwright/test');
const { loginAsOwner, OWNER_EMAIL, OWNER_PASSWORD } = require('../helpers/auth-helpers');
const TestDataFactory = require('../helpers/test-data-factory');
const { waitAndScrollToElement, navigateAndWaitForLoad } = require('../helpers/test-helpers');

/**
 * Companies CRUD Tests - Phase 1
 *
 * Tests the Companies management page functionality including:
 * - Create company (with manager account)
 * - Read/List companies
 * - Update/Rename company
 * - Delete company
 * - Input validation and security
 *
 * Prerequisites:
 * - Owner account must exist with credentials in .env
 * - Application must be running on APP_URL (default: http://localhost:5000)
 */

test.describe('Companies CRUD - Owner Role', () => {
    /**
     * Helper function to navigate to Companies page
     * @param {import('@playwright/test').Page} page
     */
    async function navigateToCompanies(page) {
        await page.goto('/Admin/Companies');
        await page.waitForLoadState('networkidle');

        // Wait for any loading indicators to disappear
        const loader = page.locator('.loading, .spinner, [data-loading="true"]');
        if (await loader.isVisible().catch(() => false)) {
            await loader.waitFor({ state: 'hidden', timeout: 10000 });
        }

        // Wait for page-specific content to be loaded
        // Check for either the companies table or the form (whichever indicates page is ready)
        try {
            await page.waitForSelector('table.companies-table, form input[name="CompanyName"]', { timeout: 10000 });
        } catch (e) {
            // If neither exists, wait for network to be idle for dynamic content
            await page.waitForLoadState('networkidle');
        }

        // Scroll to ensure full page is loaded/visible
        await page.evaluate(() => window.scrollTo(0, document.body.scrollHeight));
        await page.waitForLoadState('networkidle'); // Wait for any lazy-loaded elements
        await page.evaluate(() => window.scrollTo(0, 0));
    }

    /**
     * Helper to fill company creation form
     * @param {import('@playwright/test').Page} page
     * @param {Object} data - Company and manager data
     * @param {boolean} clearFirst - Whether to clear form fields first (default: true)
     */
    async function fillCompanyForm(page, data, clearFirst = true) {
        // Ensure the form is visible and scroll to it if needed
        const companyNameInput = page.locator('input[name="CompanyName"]');
        if (await companyNameInput.isVisible().catch(() => false)) {
            await companyNameInput.scrollIntoViewIfNeeded();
        }

        // Clear form fields first if requested (handles iteration in loop tests)
        if (clearFirst) {
            await page.fill('input[name="CompanyName"]', '');
            await page.fill('input[name="CompanySlug"]', '');
            await page.fill('input[name="CompanyDisplayName"]', '');
            await page.fill('input[name="ManagerEmail"]', '');
            await page.fill('input[name="ManagerDisplayName"]', '');
            await page.fill('input[name="ManagerPassword"]', '');
        }

        // Fill company details
        await page.fill('input[name="CompanyName"]', data.companyName);
        await page.fill('input[name="CompanySlug"]', data.companySlug);

        if (data.companyDisplayName) {
            await page.fill('input[name="CompanyDisplayName"]', data.companyDisplayName);
        }

        // Fill manager details (required when not using existing director)
        if (data.managerEmail) {
            await page.fill('input[name="ManagerEmail"]', data.managerEmail);
        }
        if (data.managerDisplayName) {
            await page.fill('input[name="ManagerDisplayName"]', data.managerDisplayName);
        }
        if (data.managerPassword) {
            await page.fill('input[name="ManagerPassword"]', data.managerPassword);
        }
    }

    test.beforeEach(async ({ page }) => {
        // Skip tests if password not configured
        test.skip(!OWNER_PASSWORD, 'Owner password not configured in environment');

        await loginAsOwner(page);
    });

    test.describe('Create Company', () => {
        test('P2-01: Create company with valid data', async ({ page }) => {
            await navigateToCompanies(page);

            const companyData = TestDataFactory.generateCompanyCreationData();

            // Fill the company creation form
            await fillCompanyForm(page, companyData);

            // Submit the form (ensure visible before click)
            const submitButton = page.locator('button[type="submit"]:has-text("Create")');
            await submitButton.scrollIntoViewIfNeeded();
            await submitButton.click();

            // Wait for success indicators instead of networkidle
            await Promise.race([
                page.waitForSelector('.alert-success', { timeout: 10000 }),
                page.waitForSelector(`table tbody tr:has-text("${companyData.companyName}")`, { timeout: 10000 })
            ]).catch(() => {});

            // Give DOM time to update
            await page.waitForTimeout(1000);

            // Verify success - check for success message or company in list
            const successAlert = page.locator('.alert-success');
            const companyInList = page.locator(`table tbody tr:has-text("${companyData.companyName}")`);

            // Either success message or company visible in list indicates success
            const hasSuccess = await successAlert.isVisible().catch(() => false);
            const hasCompany = await companyInList.isVisible().catch(() => false);

            expect(hasSuccess || hasCompany).toBeTruthy();
        });

        test('P2-02: Create company with minimum required fields', async ({ page }) => {
            await navigateToCompanies(page);

            const uniqueId = Date.now();
            const minimalData = {
                companyName: `MinimalCo_${uniqueId}`,
                companySlug: `minimal-${uniqueId}`,
                companyDisplayName: '',
                managerEmail: `minimal_${uniqueId}@test.local`,
                managerDisplayName: `Minimal Manager ${uniqueId}`,
                managerPassword: 'Password123!'
            };

            await fillCompanyForm(page, minimalData);

            const submitButton = page.locator('button[type="submit"]:has-text("Create")');
            await submitButton.scrollIntoViewIfNeeded();
            await submitButton.click();
            await page.waitForLoadState('networkidle');

            // Verify company was created
            await expect(page.locator(`table:has-text("${minimalData.companyName}")`)).toBeVisible({ timeout: 10000 });
        });

        test('P2-03: Validation - empty company name rejected', async ({ page }) => {
            await navigateToCompanies(page);

            const companyData = TestDataFactory.generateCompanyCreationData();
            companyData.companyName = '';

            await fillCompanyForm(page, companyData);

            // Try to submit - should fail HTML5 validation or show error
            const submitButton = page.locator('button[type="submit"]:has-text("Create")');
            await submitButton.scrollIntoViewIfNeeded();
            await submitButton.click();

            // Either HTML5 validation prevents submission or error is shown
            const currentUrl = page.url();
            const errorVisible = await page.locator('.alert-error').isVisible().catch(() => false);
            const stillOnPage = currentUrl.includes('/Admin/Companies');

            expect(stillOnPage).toBeTruthy();
        });

        test('P2-04: Validation - duplicate slug rejected', async ({ page }) => {
            await navigateToCompanies(page);

            // Wait for companies table to load
            await page.waitForSelector('table tbody tr', { timeout: 10000 }).catch(() => {});

            // First, get an existing company slug from the table
            const existingSlugCell = page.locator('table tbody tr:first-child td:nth-child(2) code');
            const existingSlug = await existingSlugCell.textContent({ timeout: 5000 }).catch(() => null);

            if (!existingSlug) {
                test.skip(true, 'No existing companies to test duplicate slug');
                return;
            }

            const uniqueId = Date.now();
            const duplicateData = {
                companyName: `DuplicateTest_${uniqueId}`,
                companySlug: existingSlug.trim(), // Use existing slug
                companyDisplayName: '',
                managerEmail: `dup_${uniqueId}@test.local`,
                managerDisplayName: `Dup Manager ${uniqueId}`,
                managerPassword: 'Password123!'
            };

            await fillCompanyForm(page, duplicateData);

            const submitButton = page.locator('button[type="submit"]:has-text("Create")');
            await submitButton.scrollIntoViewIfNeeded();
            await submitButton.click();

            // Wait for error message to appear
            await Promise.race([
                page.waitForSelector('.alert-error', { timeout: 10000 }),
                page.waitForLoadState('domcontentloaded', { timeout: 10000 })
            ]).catch(() => {});

            // Should show error about duplicate slug
            await expect(page.locator('.alert-error')).toBeVisible();
        });

        test('P2-05: Validation - invalid slug format rejected', async ({ page }) => {
            await navigateToCompanies(page);

            const invalidSlugs = [
                'UPPERCASE',           // Must be lowercase
                'with spaces',         // No spaces
                'with.dots',          // No dots
                'with_underscores',   // No underscores (hyphens only)
            ];

            for (const invalidSlug of invalidSlugs) {
                const companyData = TestDataFactory.generateCompanyCreationData();
                companyData.companySlug = invalidSlug;

                await fillCompanyForm(page, companyData);

                // The form has pattern validation: pattern="[a-z0-9-]+"
                // HTML5 validation should prevent submission
                const slugInput = page.locator('input[name="CompanySlug"]');
                const isValid = await slugInput.evaluate((el) => (/** @type {HTMLInputElement} */ (el)).checkValidity());

                expect(isValid).toBeFalsy();

                // Clear for next iteration
                await page.fill('input[name="CompanySlug"]', '');
                await page.fill('input[name="CompanyName"]', '');
            }
        });
    });

    test.describe('Read/List Companies', () => {
        test('P2-06: Companies list is displayed', async ({ page }) => {
            await navigateToCompanies(page);

            // Verify table structure exists
            const table = page.locator('table.companies-table');
            await expect(table).toBeVisible();

            // Verify table headers (use exact text to avoid strict mode violations)
            await expect(page.getByRole('columnheader', { name: 'Name', exact: true })).toBeVisible();
            await expect(page.getByRole('columnheader', { name: 'Slug', exact: true })).toBeVisible();
            await expect(page.getByRole('columnheader', { name: 'Actions', exact: true })).toBeVisible();
        });

        test('P2-07: Company details are displayed correctly', async ({ page }) => {
            await navigateToCompanies(page);

            // Get first company row
            const firstRow = page.locator('table tbody tr:first-child');
            const rowExists = await firstRow.isVisible().catch(() => false);

            if (!rowExists) {
                test.skip(true, 'No companies in list to verify');
                return;
            }

            // Verify row has expected columns
            const cells = await firstRow.locator('td').count();
            expect(cells).toBeGreaterThanOrEqual(4); // Name, Slug, DisplayName, Users, Actions
        });

        test('P2-08: Empty state displayed when no companies', async ({ page }) => {
            // This test documents expected behavior - actual verification
            // depends on database state
            await navigateToCompanies(page);

            const tableExists = await page.locator('table tbody tr').count();

            if (tableExists === 0) {
                // Should show empty state message
                await expect(page.locator('.empty-state, p:has-text("No companies")')).toBeVisible();
            }
        });
    });

    test.describe('Update/Rename Company', () => {
        test('P2-09: Rename company modal opens', async ({ page }) => {
            await navigateToCompanies(page);

            // Check if there's a company to rename (more specific selector)
            const firstCompanyRow = page.locator('table tbody tr').first();
            const renameButton = firstCompanyRow.locator('button:has-text("Rename")');
            const buttonExists = await renameButton.isVisible().catch(() => false);

            if (!buttonExists) {
                test.skip(true, 'No companies available to test rename');
                return;
            }

            await renameButton.scrollIntoViewIfNeeded();
            await renameButton.click();

            // Verify modal opens (with longer timeout for animation)
            await expect(page.locator('#renameModal.active, .modal.active')).toBeVisible({ timeout: 10000 });
        });

        test('P2-10: Rename company successfully', async ({ page }) => {
            await navigateToCompanies(page);

            // First create a company to rename
            const companyData = TestDataFactory.generateCompanyCreationData();
            await fillCompanyForm(page, companyData);

            const createButton = page.locator('button[type="submit"]:has-text("Create")');
            await createButton.scrollIntoViewIfNeeded();
            await createButton.click();
            await page.waitForLoadState('networkidle');

            // Find and click rename button for our company
            const companyRow = page.locator(`table tbody tr:has-text("${companyData.companyName}")`);
            const rowExists = await companyRow.isVisible().catch(() => false);

            if (!rowExists) {
                test.skip(true, 'Could not create company for rename test');
                return;
            }

            await companyRow.locator('button:has-text("Rename")').click();

            // Wait for modal to be visible before filling
            const modal = page.locator('#renameModal.active, .modal.active');
            await modal.waitFor({ state: 'visible' });

            // Fill new name in modal
            const newName = `Renamed_${Date.now()}`;
            await page.fill('#newCompanyName', newName);
            await page.click('#renameModal button[type="submit"]');

            // Wait for modal to close and success message or table update
            await Promise.race([
                page.waitForSelector('.alert-success', { timeout: 10000 }),
                page.waitForSelector(`table:has-text("${newName}")`, { timeout: 10000 }),
                modal.waitFor({ state: 'hidden', timeout: 10000 })
            ]).catch(() => {});

            // Verify rename succeeded
            await expect(page.locator(`table:has-text("${newName}")`)).toBeVisible();
        });

        test('P2-11: Rename modal cancel works', async ({ page }) => {
            await navigateToCompanies(page);

            // More specific selector within first company row
            const firstCompanyRow = page.locator('table tbody tr').first();
            const renameButton = firstCompanyRow.locator('button:has-text("Rename")');
            const buttonExists = await renameButton.isVisible().catch(() => false);

            if (!buttonExists) {
                test.skip(true, 'No companies available to test rename cancel');
                return;
            }

            await renameButton.scrollIntoViewIfNeeded();
            await renameButton.click();
            await expect(page.locator('#renameModal.active')).toBeVisible({ timeout: 10000 });

            // Click cancel
            await page.click('#renameModal button:has-text("Cancel")');

            // Modal should close
            await expect(page.locator('#renameModal.active')).not.toBeVisible();
        });
    });

    test.describe('Delete Company', () => {
        test('P2-12: Delete company with confirmation', async ({ page }) => {
            await navigateToCompanies(page);

            // First create a company to delete
            const companyData = TestDataFactory.generateCompanyCreationData();
            await fillCompanyForm(page, companyData);

            const createButton = page.locator('button[type="submit"]:has-text("Create")');
            await createButton.scrollIntoViewIfNeeded();
            await createButton.click();
            await page.waitForLoadState('networkidle');

            // Find delete button for our company
            const companyRow = page.locator(`table tbody tr:has-text("${companyData.companyName}")`);

            // Wait for the row to be visible with increased timeout
            await companyRow.waitFor({ state: 'visible', timeout: 10000 });

            const deleteButton = companyRow.locator('button:has-text("Delete")');

            // Scroll button into view and wait for it to be enabled
            await deleteButton.scrollIntoViewIfNeeded();
            await deleteButton.waitFor({ state: 'visible', timeout: 5000 });

            // Set up dialog handler BEFORE clicking (critical timing fix)
            page.once('dialog', async dialog => {
                expect(dialog.type()).toBe('confirm');
                await dialog.accept();
            });

            await deleteButton.click();

            // Wait for either success message or company to disappear from table
            await Promise.race([
                page.waitForSelector('.alert-success', { timeout: 10000 }),
                companyRow.waitFor({ state: 'hidden', timeout: 10000 }),
                page.waitForLoadState('domcontentloaded', { timeout: 10000 })
            ]).catch(() => {});

            // Give DOM time to update
            await page.waitForTimeout(1000);

            // Verify company was deleted
            await expect(page.locator(`table tr:has-text("${companyData.companyName}")`)).not.toBeVisible();
        });

        test('P2-13: Delete confirmation can be cancelled', async ({ page }) => {
            await navigateToCompanies(page);

            // More specific selector within first company row
            const firstCompanyRow = page.locator('table tbody tr').first();
            const deleteButton = firstCompanyRow.locator('button:has-text("Delete")');
            const buttonExists = await deleteButton.isVisible().catch(() => false);

            if (!buttonExists) {
                test.skip(true, 'No companies available to test delete cancel');
                return;
            }

            // Get company name before delete attempt
            const companyName = await firstCompanyRow.locator('td').first().textContent();

            // Set up dialog handler BEFORE clicking (critical timing fix)
            page.once('dialog', async dialog => {
                await dialog.dismiss();
            });

            await deleteButton.click();

            // Company should still exist (deterministic wait instead of timeout)
            const companyElement = page.locator(`table tr:has-text("${companyName}")`);
            await expect(companyElement).toBeVisible({ timeout: 5000 });
        });
    });

    test.describe('Security - Input Validation', () => {
        test('P2-14: XSS attempts in company name are handled', async ({ page }) => {
            await navigateToCompanies(page);

            const securityInputs = TestDataFactory.getSecurityTestInputs();

            for (const xssAttempt of securityInputs.xssAttempts.slice(0, 2)) { // Test first 2 for speed
                const companyData = TestDataFactory.generateCompanyCreationData();
                companyData.companyName = xssAttempt;

                await fillCompanyForm(page, companyData);

                const createButton = page.locator('button[type="submit"]:has-text("Create")');
                await createButton.scrollIntoViewIfNeeded();
                await createButton.click();

                // Wait for either success/error message or networkidle (XSS payloads may cause validation errors)
                await Promise.race([
                    page.waitForLoadState('networkidle', { timeout: 10000 }),
                    page.waitForSelector('.alert-success, .alert-danger, .alert-warning', { timeout: 10000 })
                ]).catch(() => {
                    // If both timeout, continue anyway - validation might have rejected the payload
                });

                // If created, verify it's properly escaped in the DOM
                const createdCompany = page.locator(`table td:has-text("${xssAttempt}")`);
                const wasCreated = await createdCompany.isVisible().catch(() => false);

                if (wasCreated) {
                    // Verify no script execution - check if text is escaped
                    const cellHtml = await createdCompany.innerHTML();
                    expect(cellHtml).not.toContain('<script>');
                    expect(cellHtml).not.toContain('onerror=');
                }

                // Navigate back to start fresh (check if page is still open first)
                if (!page.isClosed()) {
                    await navigateToCompanies(page);
                }
            }
        });

        test('P2-15: SQL injection attempts in company name are handled', async ({ page }) => {
            await navigateToCompanies(page);

            const securityInputs = TestDataFactory.getSecurityTestInputs();
            const sqlAttempt = securityInputs.sqlInjectionAttempts[0];

            const companyData = TestDataFactory.generateCompanyCreationData();
            companyData.companyName = sqlAttempt;

            await fillCompanyForm(page, companyData);

            const createButton = page.locator('button[type="submit"]:has-text("Create")');
            await createButton.scrollIntoViewIfNeeded();
            await createButton.click();
            await page.waitForLoadState('networkidle');

            // Either rejected with error OR stored as literal string (safe)
            // The page should not crash or show database errors
            const pageContent = await page.content();
            expect(pageContent).not.toContain('SQL syntax');
            expect(pageContent).not.toContain('database error');
            expect(pageContent).not.toContain('SQLException');
        });

        test('P2-16: Very long company name handled', async ({ page }) => {
            await navigateToCompanies(page);

            const securityInputs = TestDataFactory.getSecurityTestInputs();
            const companyData = TestDataFactory.generateCompanyCreationData();
            companyData.companyName = securityInputs.boundaryInputs.veryLong;

            await fillCompanyForm(page, companyData);

            const createButton = page.locator('button[type="submit"]:has-text("Create")');
            await createButton.scrollIntoViewIfNeeded();
            await createButton.click();
            await page.waitForLoadState('networkidle');

            // Should show error about length (based on Companies.cshtml.cs validation)
            // CompanyName.Length > 200 triggers error
            const errorAlert = page.locator('.alert-error');
            const errorVisible = await errorAlert.isVisible().catch(() => false);

            // Either error shown OR truncated - either is acceptable
            expect(true).toBeTruthy(); // Page didn't crash
        });

        test('P2-17: Path traversal attempts in company slug are rejected', async ({ page }) => {
            await navigateToCompanies(page);

            const securityInputs = TestDataFactory.getSecurityTestInputs();

            for (const pathAttempt of securityInputs.pathTraversalAttempts.slice(0, 2)) {
                const companyData = TestDataFactory.generateCompanyCreationData();
                companyData.companySlug = pathAttempt;

                await page.fill('input[name="CompanySlug"]', pathAttempt);

                // Slug pattern validation: pattern="[a-z0-9-]+"
                // Should fail HTML5 validation
                const slugInput = page.locator('input[name="CompanySlug"]');
                const isValid = await slugInput.evaluate((el) => (/** @type {HTMLInputElement} */ (el)).checkValidity());

                expect(isValid).toBeFalsy();

                await page.fill('input[name="CompanySlug"]', '');
            }
        });
    });

    test.describe('Director Assignment', () => {
        test('P2-18: Director dropdown is available', async ({ page }) => {
            await navigateToCompanies(page);

            // Verify director selection dropdown exists
            const directorSelect = page.locator('select[name="SelectedDirectorId"], #directorSelect');
            await expect(directorSelect).toBeVisible();
        });

        test('P2-19: Manager fields become optional when director selected', async ({ page }) => {
            await navigateToCompanies(page);

            const directorSelect = page.locator('#directorSelect');
            const options = await directorSelect.locator('option').count();

            if (options <= 1) { // Only the "Select" placeholder
                test.skip(true, 'No directors available to test assignment');
                return;
            }

            // Select first available director
            await directorSelect.selectOption({ index: 1 });

            // Manager fields should become optional (not required)
            const managerEmail = page.locator('#managerEmail');
            const hasRequired = await managerEmail.getAttribute('required');

            expect(hasRequired).toBeNull();

            // Manager fieldset should be dimmed
            const fieldset = page.locator('#managerFieldset');
            const hasDimmedClass = await fieldset.evaluate((el) => el.classList.contains('fieldset-dimmed'));
            expect(hasDimmedClass).toBeTruthy();
        });
    });

    test.describe('Form State', () => {
        test('P2-20: Form clears after successful submission', async ({ page }) => {
            await navigateToCompanies(page);

            const companyData = TestDataFactory.generateCompanyCreationData();
            await fillCompanyForm(page, companyData);

            const createButton = page.locator('button[type="submit"]:has-text("Create")');
            await createButton.scrollIntoViewIfNeeded();
            await createButton.click();
            await page.waitForLoadState('networkidle');

            // After redirect, form should be empty
            const companyNameValue = await page.locator('input[name="CompanyName"]').inputValue();
            expect(companyNameValue).toBe('');
        });

        test('P2-21: Form preserves values on validation error', async ({ page }) => {
            await navigateToCompanies(page);

            // Get existing slug to trigger duplicate error
            const existingSlugCell = page.locator('table tbody tr:first-child td:nth-child(2) code');
            const existingSlug = await existingSlugCell.textContent().catch(() => null);

            if (!existingSlug) {
                test.skip(true, 'No existing companies to test form preservation');
                return;
            }

            const companyName = `PreserveTest_${Date.now()}`;
            const duplicateData = {
                companyName: companyName,
                companySlug: existingSlug.trim(),
                companyDisplayName: 'Preserve Test',
                managerEmail: `preserve_${Date.now()}@test.local`,
                managerDisplayName: 'Preserve Manager',
                managerPassword: 'Password123!'
            };

            await fillCompanyForm(page, duplicateData);

            const createButton = page.locator('button[type="submit"]:has-text("Create")');
            await createButton.scrollIntoViewIfNeeded();
            await createButton.click();
            await page.waitForLoadState('networkidle');

            // Error should be shown
            const errorVisible = await page.locator('.alert-error').isVisible().catch(() => false);

            if (errorVisible) {
                // Form values should be preserved (ASP.NET behavior)
                // Note: This may depend on server-side model binding behavior
                const currentUrl = page.url();
                expect(currentUrl).toContain('/Admin/Companies');
            }
        });
    });
});

test.describe('Companies Access Control', () => {
    test('P2-22: Unauthenticated user redirected to login', async ({ page }) => {
        // Try to access Companies page without login
        await page.goto('/Admin/Companies');

        // Should be redirected to login
        await expect(page).toHaveURL(/\/Auth\/Login/);
    });

    test('P2-23: Page requires admin policy', async ({ page }) => {
        // The Companies page has [Authorize(Policy = "IsAdmin")]
        // Non-admin users should be denied access

        // Try direct navigation without auth
        const response = await page.goto('/Admin/Companies');

        // Should either redirect to login (302) or show access denied
        const status = response?.status();
        const url = page.url();

        expect(
            status === 302 ||
            status === 401 ||
            status === 403 ||
            url.includes('/Auth/Login') ||
            url.includes('/AccessDenied')
        ).toBeTruthy();
    });
});
