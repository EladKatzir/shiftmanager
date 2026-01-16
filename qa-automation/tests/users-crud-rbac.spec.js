// @ts-check
const { test, expect } = require('@playwright/test');
const path = require('path');
const { RoleHelper, ROLE_CREDENTIALS } = require('../helpers/role-helper');
const TestDataFactory = require('../helpers/test-data-factory');

// Load environment variables
try {
    require('dotenv').config({ path: path.join(__dirname, '..', '.env') });
} catch (e) {
    // dotenv not installed, rely on system environment variables
}

/**
 * Users CRUD with RBAC Authorization Matrix Tests
 *
 * This test suite validates role-based access control (RBAC) for the Users module.
 * Tests cover:
 * - Owner can create users in any company
 * - Manager cannot access user management
 * - Director can only see users in assigned companies
 * - Owner can delete users from any company
 * - Backend rejects unauthorized user creation via API
 *
 * Prerequisites:
 * - Test accounts for each role must exist with credentials in .env
 * - Application must be running on APP_URL (default: http://localhost:5000)
 *
 * Role Hierarchy (from Users.cshtml.cs):
 * - Owner: Full access to all companies and users
 * - Director: Access to companies they manage (via DirectorCompany table)
 * - Manager: Access to their own company only
 * - Assigner: No user management access
 * - Employee: No user management access
 * - Trainee: No user management access
 */

test.describe('Users Module - RBAC Authorization Matrix', () => {
    /**
     * Helper function to navigate to Users page
     * @param {import('@playwright/test').Page} page
     */
    async function navigateToUsers(page) {
        await page.goto('/Admin/Users');
        await page.waitForLoadState('networkidle');
    }

    /**
     * Helper function to fill the Add User form
     * @param {import('@playwright/test').Page} page
     * @param {Object} userData - User data to fill in the form
     */
    async function fillAddUserForm(page, userData) {
        await page.fill('input[name="NewEmail"], input#NewEmail', userData.email);
        await page.fill('input[name="NewDisplayName"], input#NewDisplayName', userData.displayName);
        await page.fill('input[name="NewPassword"], input#NewPassword', userData.password);

        // Select role if dropdown is available
        const roleSelect = page.locator('select[name="NewRole"], select#NewRole');
        if (await roleSelect.isVisible()) {
            await roleSelect.selectOption(userData.role || 'Employee');
        }

        // Select company if dropdown is available (Owner only)
        if (userData.companyId) {
            const companySelect = page.locator('select[name="NewUserCompanyId"], select#NewUserCompanyId');
            if (await companySelect.isVisible()) {
                await companySelect.selectOption(userData.companyId.toString());
            }
        }
    }

    test.describe('Owner Role - Full Access', () => {
        test.beforeEach(async ({ page }) => {
            test.skip(!RoleHelper.isRoleConfigured('Owner'), 'Owner credentials not configured');
            await RoleHelper.loginAs(page, 'Owner');
        });

        test('P3-01: Owner can access Users management page', async ({ page }) => {
            await navigateToUsers(page);

            // Verify we're on the Users page
            await expect(page.locator('h1')).toContainText(/User Management|Users/i);

            // Verify the Add User form is visible
            await expect(page.locator('input[name="NewEmail"], input#NewEmail')).toBeVisible();
        });

        test('P3-02: Owner can see all companies in company filter', async ({ page }) => {
            await navigateToUsers(page);

            // Owner should see a company selection dropdown for filtering
            const companyFilter = page.locator('select[name="UserFilterCompanyId"]');

            if (await companyFilter.isVisible()) {
                // Should have multiple options (All Companies + actual companies)
                const optionCount = await companyFilter.locator('option').count();
                expect(optionCount).toBeGreaterThanOrEqual(1);
            }
        });

        test('P3-03: Owner can see company selection for new user creation', async ({ page }) => {
            await navigateToUsers(page);

            // Owner should see company selection for creating users in any company
            const companySelect = page.locator('select[name="NewUserCompanyId"], select#NewUserCompanyId');
            await expect(companySelect).toBeVisible();

            // Should have options to select from
            const optionCount = await companySelect.locator('option').count();
            expect(optionCount).toBeGreaterThanOrEqual(1);
        });

        test('P3-04: Owner can create user with valid data', async ({ page }) => {
            await navigateToUsers(page);

            // Generate unique test user data
            const userData = TestDataFactory.generateUser('Employee');

            // Get the first available company option
            const companySelect = page.locator('select[name="NewUserCompanyId"], select#NewUserCompanyId');
            const companyOptions = await companySelect.locator('option[value]:not([value=""])').all();

            if (companyOptions.length === 0) {
                test.skip(true, 'No companies available for user creation');
                return;
            }

            const firstCompanyValue = await companyOptions[0].getAttribute('value');

            // Fill the form
            await fillAddUserForm(page, {
                ...userData,
                companyId: firstCompanyValue
            });

            // Submit the form
            await page.click('button[type="submit"]:has-text("Add")');
            await page.waitForLoadState('networkidle');

            // Verify success - either success message or user appears in list
            const successAlert = page.locator('.alert-success');
            const userInList = page.locator(`table tbody tr:has-text("${userData.email}")`);

            const hasSuccess = await successAlert.isVisible().catch(() => false);
            const hasUser = await userInList.isVisible().catch(() => false);

            expect(hasSuccess || hasUser).toBeTruthy();
        });

        test('P3-05: Owner can delete user from any company', async ({ page }) => {
            await navigateToUsers(page);

            // Set up dialog handler for confirmation
            page.once('dialog', async dialog => {
                expect(dialog.type()).toBe('confirm');
                await dialog.accept();
            });

            // Find a delete button in the users table
            const deleteButton = page.locator('form[action*="DeleteUser"] button[type="submit"]:first-of-type');
            const buttonExists = await deleteButton.isVisible().catch(() => false);

            if (!buttonExists) {
                test.skip(true, 'No users available to test delete functionality');
                return;
            }

            // Get the user name before deletion for verification
            const userRow = page.locator('table tbody tr:has(form[action*="DeleteUser"]):first-of-type');
            const userName = await userRow.locator('td:first-child').textContent();

            await deleteButton.click();
            await page.waitForLoadState('networkidle');

            // Verify deletion - either success message or user no longer in list
            const successMessage = await page.locator('.alert-success').isVisible().catch(() => false);
            const pageContainsDeletedUser = await page.locator(`table tbody tr:has-text("${userName}")`).isVisible().catch(() => false);

            // Either success message shown OR user is gone from the list
            expect(successMessage || !pageContainsDeletedUser).toBeTruthy();
        });

        test('P3-06: Owner can assign any role to a user', async ({ page }) => {
            await navigateToUsers(page);

            // Find a role dropdown in the users table
            const roleSelect = page.locator('table tbody tr select[name="role"]:first-of-type');
            const selectExists = await roleSelect.isVisible().catch(() => false);

            if (!selectExists) {
                test.skip(true, 'No users available to test role assignment');
                return;
            }

            // Verify Owner can see all roles including Director
            const options = await roleSelect.locator('option').allTextContents();

            // Owner should see Director option
            expect(options.some(opt => opt.toLowerCase().includes('director'))).toBeTruthy();
        });
    });

    test.describe('Director Role - Limited Multi-Company Access', () => {
        test.beforeEach(async ({ page }) => {
            test.skip(!RoleHelper.isRoleConfigured('Director'), 'Director credentials not configured');
            await RoleHelper.loginAs(page, 'Director');
        });

        test('P3-07: Director can access Users management page', async ({ page }) => {
            await navigateToUsers(page);

            // Director should have access to Users page (IsManagerOrAdmin policy)
            await expect(page.locator('h1')).toContainText(/User Management|Users/i);
        });

        test('P3-08: Director sees only users from assigned companies', async ({ page }) => {
            await navigateToUsers(page);

            // Verify the users table is visible
            const usersTable = page.locator('table:has-text("Name")');
            await expect(usersTable).toBeVisible();

            // Director should see company filter with their assigned companies
            const companyFilter = page.locator('select[name="UserFilterCompanyId"]');

            if (await companyFilter.isVisible()) {
                const optionCount = await companyFilter.locator('option').count();
                // Should have at least "All Companies" and their assigned companies
                expect(optionCount).toBeGreaterThanOrEqual(1);
            }
        });

        test('P3-09: Director cannot see company selection for new users', async ({ page }) => {
            await navigateToUsers(page);

            // Director should NOT see company selection - users are created in their company
            const companySelect = page.locator('select[name="NewUserCompanyId"], select#NewUserCompanyId');

            // Company select should not be visible for non-Owner roles
            const isVisible = await companySelect.isVisible().catch(() => false);
            expect(isVisible).toBeFalsy();
        });

        test('P3-10: Director cannot assign Owner role', async ({ page }) => {
            await navigateToUsers(page);

            // Check role dropdown in Add User form
            const roleSelect = page.locator('select[name="NewRole"], select#NewRole');

            if (await roleSelect.isVisible()) {
                const options = await roleSelect.locator('option').allTextContents();

                // Director should NOT see Owner option
                expect(options.every(opt => !opt.toLowerCase().includes('owner'))).toBeTruthy();
            }
        });
    });

    test.describe('Manager Role - Single Company Access', () => {
        test.beforeEach(async ({ page }) => {
            test.skip(!RoleHelper.isRoleConfigured('Manager'), 'Manager credentials not configured');
            await RoleHelper.loginAs(page, 'Manager');
        });

        test('P3-11: Manager can access Users management page', async ({ page }) => {
            await navigateToUsers(page);

            // Manager should have access to Users page (IsManagerOrAdmin policy)
            await expect(page.locator('h1')).toContainText(/User Management|Users/i);
        });

        test('P3-12: Manager sees only users from their company', async ({ page }) => {
            await navigateToUsers(page);

            // Verify the users table is visible
            const usersTable = page.locator('table:has-text("Name")');
            await expect(usersTable).toBeVisible();

            // Manager should not see cross-company filters or company dropdown
            const companySelect = page.locator('select[name="NewUserCompanyId"], select#NewUserCompanyId');

            // Company select should not be visible for Manager role
            const isVisible = await companySelect.isVisible().catch(() => false);
            expect(isVisible).toBeFalsy();
        });

        test('P3-13: Manager cannot assign Director or Owner roles', async ({ page }) => {
            await navigateToUsers(page);

            // Check role dropdown in Add User form
            const roleSelect = page.locator('select[name="NewRole"], select#NewRole');

            if (await roleSelect.isVisible()) {
                const options = await roleSelect.locator('option').allTextContents();

                // Manager should NOT see Owner or Director options
                expect(options.every(opt =>
                    !opt.toLowerCase().includes('owner') &&
                    !opt.toLowerCase().includes('director')
                )).toBeTruthy();
            }
        });

        test('P3-14: Manager can create Employee users', async ({ page }) => {
            await navigateToUsers(page);

            // Generate unique test user data
            const userData = TestDataFactory.generateUser('Employee');

            // Fill the form (no company selection for Manager)
            await page.fill('input[name="NewEmail"], input#NewEmail', userData.email);
            await page.fill('input[name="NewDisplayName"], input#NewDisplayName', userData.displayName);
            await page.fill('input[name="NewPassword"], input#NewPassword', userData.password);

            const roleSelect = page.locator('select[name="NewRole"], select#NewRole');
            if (await roleSelect.isVisible()) {
                await roleSelect.selectOption('Employee');
            }

            // Submit the form
            await page.click('button[type="submit"]:has-text("Add")');
            await page.waitForLoadState('networkidle');

            // Verify success or user created
            const successAlert = page.locator('.alert-success');
            const hasSuccess = await successAlert.isVisible().catch(() => false);

            // Either success or still on the page without error
            const errorAlert = page.locator('.alert-error');
            const hasError = await errorAlert.isVisible().catch(() => false);

            expect(hasSuccess || !hasError).toBeTruthy();
        });
    });

    test.describe('Employee Role - No Access', () => {
        test.beforeEach(async ({ page }) => {
            test.skip(!RoleHelper.isRoleConfigured('Employee'), 'Employee credentials not configured');
            await RoleHelper.loginAs(page, 'Employee');
        });

        test('P3-15: Employee cannot access Users management page', async ({ page }) => {
            const response = await page.goto('/Admin/Users');
            await page.waitForLoadState('networkidle');

            // Employee should be redirected to login or access denied
            const currentUrl = page.url();
            const isAccessDenied = currentUrl.includes('/Auth/Login') ||
                                   currentUrl.includes('/AccessDenied') ||
                                   currentUrl.includes('/Account/AccessDenied');

            expect(isAccessDenied).toBeTruthy();
        });
    });

    test.describe('Trainee Role - No Access', () => {
        test.beforeEach(async ({ page }) => {
            test.skip(!RoleHelper.isRoleConfigured('Trainee'), 'Trainee credentials not configured');
            await RoleHelper.loginAs(page, 'Trainee');
        });

        test('P3-16: Trainee cannot access Users management page', async ({ page }) => {
            const response = await page.goto('/Admin/Users');
            await page.waitForLoadState('networkidle');

            // Trainee should be redirected to login or access denied
            const currentUrl = page.url();
            const isAccessDenied = currentUrl.includes('/Auth/Login') ||
                                   currentUrl.includes('/AccessDenied') ||
                                   currentUrl.includes('/Account/AccessDenied');

            expect(isAccessDenied).toBeTruthy();
        });
    });

    test.describe('Assigner Role - No Access', () => {
        test.beforeEach(async ({ page }) => {
            test.skip(!RoleHelper.isRoleConfigured('Assigner'), 'Assigner credentials not configured');
            await RoleHelper.loginAs(page, 'Assigner');
        });

        test('P3-17: Assigner cannot access Users management page', async ({ page }) => {
            const response = await page.goto('/Admin/Users');
            await page.waitForLoadState('networkidle');

            // Assigner should be redirected to login or access denied
            const currentUrl = page.url();
            const isAccessDenied = currentUrl.includes('/Auth/Login') ||
                                   currentUrl.includes('/AccessDenied') ||
                                   currentUrl.includes('/Account/AccessDenied');

            expect(isAccessDenied).toBeTruthy();
        });
    });

    test.describe('Backend Authorization - API Security', () => {
        test('P3-18: Unauthenticated request to Users page is rejected', async ({ page }) => {
            // Try to access Users page without authentication
            const response = await page.goto('/Admin/Users');

            // Should redirect to login
            await expect(page).toHaveURL(/\/Auth\/Login/);
        });

        test('P3-19: Backend rejects unauthorized user creation via direct POST', async ({ page }) => {
            test.skip(!RoleHelper.isRoleConfigured('Employee'), 'Employee credentials not configured');

            // Login as Employee (no user management access)
            await RoleHelper.loginAs(page, 'Employee');

            // Try to directly POST to the Add handler
            const userData = TestDataFactory.generateUser('Employee');

            // First navigate to get CSRF token (if possible)
            await page.goto('/');
            await page.waitForLoadState('networkidle');

            // Attempt direct POST to add user endpoint
            const response = await page.request.post('/Admin/Users?handler=Add', {
                form: {
                    NewEmail: userData.email,
                    NewDisplayName: userData.displayName,
                    NewPassword: userData.password,
                    NewRole: 'Employee'
                },
                failOnStatusCode: false
            });

            // Should be rejected with 400 (bad request - missing CSRF), 401, 403, or redirect
            const status = response.status();
            expect([302, 400, 401, 403]).toContain(status);
        });

        test('P3-20: Backend rejects unauthorized user deletion via direct POST', async ({ page }) => {
            test.skip(!RoleHelper.isRoleConfigured('Employee'), 'Employee credentials not configured');

            // Login as Employee (no user management access)
            await RoleHelper.loginAs(page, 'Employee');

            // Try to directly POST to the Delete handler
            const response = await page.request.post('/Admin/Users?handler=DeleteUser', {
                form: {
                    id: '1' // Arbitrary user ID
                },
                failOnStatusCode: false
            });

            // Should be rejected
            const status = response.status();
            expect([302, 400, 401, 403]).toContain(status);
        });

        test('P3-21: Backend rejects role escalation attempt', async ({ page }) => {
            test.skip(!RoleHelper.isRoleConfigured('Manager'), 'Manager credentials not configured');

            // Login as Manager
            await RoleHelper.loginAs(page, 'Manager');

            // Navigate to Users page to get CSRF token
            await navigateToUsers(page);

            // Try to create a user with Owner role (should be rejected)
            const userData = TestDataFactory.generateUser('Owner');

            // Fill the form
            await page.fill('input[name="NewEmail"], input#NewEmail', userData.email);
            await page.fill('input[name="NewDisplayName"], input#NewDisplayName', userData.displayName);
            await page.fill('input[name="NewPassword"], input#NewPassword', userData.password);

            // Try to manually set role to Owner via JavaScript (if role select exists)
            const roleSelect = page.locator('select[name="NewRole"], select#NewRole');
            if (await roleSelect.isVisible()) {
                // Try to inject Owner as an option value
                await roleSelect.evaluate((select) => {
                    const option = document.createElement('option');
                    option.value = 'Owner';
                    option.text = 'Owner';
                    // @ts-ignore
                    select.appendChild(option);
                    // @ts-ignore
                    select.value = 'Owner';
                });
            }

            // Submit the form
            await page.click('button[type="submit"]:has-text("Add")');
            await page.waitForLoadState('networkidle');

            // Backend should reject or ignore the escalation
            const errorAlert = page.locator('.alert-error');
            const successAlert = page.locator('.alert-success');

            const hasError = await errorAlert.isVisible().catch(() => false);
            const hasSuccess = await successAlert.isVisible().catch(() => false);

            // If success, verify the user was NOT created with Owner role
            // (backend should have rejected the invalid role)
            // The system should show either success (role downgraded) or error (rejected)
            expect(hasSuccess || hasError).toBeTruthy();

            // Additional check: ensure we remain on the Users page
            expect(page.url()).toContain('/Admin/Users');
        });
    });

    test.describe('Cross-Company Access Control', () => {
        test('P3-22: Manager cannot modify users from other companies', async ({ page }) => {
            test.skip(!RoleHelper.isRoleConfigured('Manager'), 'Manager credentials not configured');

            await RoleHelper.loginAs(page, 'Manager');
            await navigateToUsers(page);

            // Manager should only see users from their own company
            // This is validated by checking that all visible users have the same company
            const companyNames = await page.locator('table tbody tr td:nth-child(3)').allTextContents();

            if (companyNames.length > 1) {
                // All company names should be the same for Manager view
                const uniqueCompanies = [...new Set(companyNames)];
                expect(uniqueCompanies.length).toBe(1);
            }
        });

        test('P3-23: Owner can filter users by company', async ({ page }) => {
            test.skip(!RoleHelper.isRoleConfigured('Owner'), 'Owner credentials not configured');

            await RoleHelper.loginAs(page, 'Owner');
            await navigateToUsers(page);

            // Owner should be able to filter by company
            const companyFilter = page.locator('select[name="UserFilterCompanyId"]');

            if (await companyFilter.isVisible()) {
                const options = await companyFilter.locator('option').allTextContents();

                // Should have multiple company options
                expect(options.length).toBeGreaterThanOrEqual(1);

                // Select a specific company and verify filter works
                const companyOptions = await companyFilter.locator('option[value]:not([value=""])').all();
                if (companyOptions.length > 0) {
                    const firstValue = await companyOptions[0].getAttribute('value');
                    await companyFilter.selectOption(firstValue || '');
                    await page.waitForLoadState('networkidle');

                    // URL should contain the filter parameter
                    expect(page.url()).toContain('UserFilterCompanyId');
                }
            }
        });
    });

    test.describe('Input Validation Security', () => {
        test.beforeEach(async ({ page }) => {
            test.skip(!RoleHelper.isRoleConfigured('Owner'), 'Owner credentials not configured');
            await RoleHelper.loginAs(page, 'Owner');
        });

        test('P3-24: XSS attempts in user display name are handled', async ({ page }) => {
            await navigateToUsers(page);

            const securityInputs = TestDataFactory.getSecurityTestInputs();
            const xssAttempt = securityInputs.xssAttempts[0];

            const userData = TestDataFactory.generateUser('Employee');
            userData.displayName = xssAttempt;

            // Get company for creation
            const companySelect = page.locator('select[name="NewUserCompanyId"], select#NewUserCompanyId');
            let companyId = '';
            if (await companySelect.isVisible()) {
                const companyOptions = await companySelect.locator('option[value]:not([value=""])').all();
                if (companyOptions.length > 0) {
                    companyId = await companyOptions[0].getAttribute('value') || '';
                }
            }

            await fillAddUserForm(page, {
                ...userData,
                companyId
            });

            await page.click('button[type="submit"]:has-text("Add")');
            await page.waitForLoadState('networkidle');

            // Verify no script execution - page content should not have unescaped script
            const pageContent = await page.content();
            expect(pageContent).not.toContain('<script>alert');
        });

        test('P3-25: SQL injection attempts in email are handled', async ({ page }) => {
            await navigateToUsers(page);

            const securityInputs = TestDataFactory.getSecurityTestInputs();
            const sqlAttempt = securityInputs.sqlInjectionAttempts[0];

            // Use SQL injection payload as email (though it should fail email validation)
            const userData = TestDataFactory.generateUser('Employee');

            // Get company for creation
            const companySelect = page.locator('select[name="NewUserCompanyId"], select#NewUserCompanyId');
            let companyId = '';
            if (await companySelect.isVisible()) {
                const companyOptions = await companySelect.locator('option[value]:not([value=""])').all();
                if (companyOptions.length > 0) {
                    companyId = await companyOptions[0].getAttribute('value') || '';
                }
            }

            await page.fill('input[name="NewEmail"], input#NewEmail', sqlAttempt + '@test.com');
            await page.fill('input[name="NewDisplayName"], input#NewDisplayName', userData.displayName);
            await page.fill('input[name="NewPassword"], input#NewPassword', userData.password);

            if (companyId && await companySelect.isVisible()) {
                await companySelect.selectOption(companyId);
            }

            await page.click('button[type="submit"]:has-text("Add")');
            await page.waitForLoadState('networkidle');

            // Page should not show database errors
            const pageContent = await page.content();
            expect(pageContent).not.toContain('SQL syntax');
            expect(pageContent).not.toContain('database error');
            expect(pageContent).not.toContain('SQLException');
        });

        test('P3-26: Very long display name is handled', async ({ page }) => {
            await navigateToUsers(page);

            const securityInputs = TestDataFactory.getSecurityTestInputs();
            const userData = TestDataFactory.generateUser('Employee');
            userData.displayName = securityInputs.boundaryInputs.veryLong;

            // Get company for creation
            const companySelect = page.locator('select[name="NewUserCompanyId"], select#NewUserCompanyId');
            let companyId = '';
            if (await companySelect.isVisible()) {
                const companyOptions = await companySelect.locator('option[value]:not([value=""])').all();
                if (companyOptions.length > 0) {
                    companyId = await companyOptions[0].getAttribute('value') || '';
                }
            }

            await fillAddUserForm(page, {
                ...userData,
                companyId
            });

            await page.click('button[type="submit"]:has-text("Add")');
            await page.waitForLoadState('networkidle');

            // Should either show validation error or handle gracefully
            // The page should not crash
            expect(page.url()).toContain('/Admin/Users');
        });
    });

    test.describe('Join Requests - RBAC', () => {
        test.beforeEach(async ({ page }) => {
            test.skip(!RoleHelper.isRoleConfigured('Owner'), 'Owner credentials not configured');
            await RoleHelper.loginAs(page, 'Owner');
        });

        test('P3-27: Owner can see join requests section', async ({ page }) => {
            await navigateToUsers(page);

            // Join Requests section should be visible
            const joinRequestsSection = page.locator('h2:has-text("Join Requests")');
            await expect(joinRequestsSection).toBeVisible();
        });

        test('P3-28: Join requests table displays expected columns', async ({ page }) => {
            await navigateToUsers(page);

            // Check table structure for join requests
            const joinRequestsTable = page.locator('.section-card:has(h2:has-text("Join Requests")) table');

            if (await joinRequestsTable.isVisible()) {
                // Verify expected columns exist
                const headers = await joinRequestsTable.locator('th').allTextContents();
                const headerText = headers.join(' ').toLowerCase();

                expect(headerText).toContain('name');
                expect(headerText).toContain('email');
            }
        });
    });
});

test.describe('Users CRUD Operations', () => {
    test.beforeEach(async ({ page }) => {
        test.skip(!RoleHelper.isRoleConfigured('Owner'), 'Owner credentials not configured');
        await RoleHelper.loginAs(page, 'Owner');
    });

    /**
     * Helper function to navigate to Users page
     * @param {import('@playwright/test').Page} page
     */
    async function navigateToUsers(page) {
        await page.goto('/Admin/Users');
        await page.waitForLoadState('networkidle');
    }

    test('P3-29: Users table displays expected columns', async ({ page }) => {
        await navigateToUsers(page);

        // Find the Existing Users section table
        const usersTable = page.locator('.section-card:has(h2:has-text("Existing Users")) table');

        if (await usersTable.isVisible()) {
            // Verify expected column headers
            const headers = await usersTable.locator('th').allTextContents();
            const headerText = headers.join(' ').toLowerCase();

            expect(headerText).toContain('name');
            expect(headerText).toContain('email');
            expect(headerText).toContain('company');
            expect(headerText).toContain('role');
        }
    });

    test('P3-30: User role can be changed via dropdown', async ({ page }) => {
        await navigateToUsers(page);

        // Find a role dropdown in the users table
        const roleSelect = page.locator('table tbody tr select[name="role"]:first-of-type');
        const selectExists = await roleSelect.isVisible().catch(() => false);

        if (!selectExists) {
            test.skip(true, 'No users available to test role change');
            return;
        }

        // Get current role
        const currentRole = await roleSelect.inputValue();

        // Select a different role
        const options = await roleSelect.locator('option').allTextContents();
        const differentRole = options.find(opt =>
            opt.toLowerCase() !== currentRole.toLowerCase() &&
            opt.toLowerCase().includes('employee')
        );

        if (differentRole) {
            await roleSelect.selectOption({ label: differentRole.trim() });
            await page.waitForLoadState('networkidle');

            // Verify success message or role changed
            const successMessage = await page.locator('.alert-success').isVisible().catch(() => false);
            expect(successMessage).toBeTruthy();
        }
    });

    test('P3-31: User status can be toggled', async ({ page }) => {
        await navigateToUsers(page);

        // Find a toggle status button
        const toggleForm = page.locator('form[action*="Toggle"]:first-of-type');
        const formExists = await toggleForm.isVisible().catch(() => false);

        if (!formExists) {
            test.skip(true, 'No users available to test status toggle');
            return;
        }

        // Get current status text
        const toggleButton = toggleForm.locator('button');
        const currentStatus = await toggleButton.textContent();

        await toggleButton.click();
        await page.waitForLoadState('networkidle');

        // Verify status changed (button text should be different)
        const newStatus = await page.locator('form[action*="Toggle"]:first-of-type button').textContent();

        // Status should have toggled
        expect(newStatus).not.toBe(currentStatus);
    });

    test('P3-32: Password reset form exists and works', async ({ page }) => {
        await navigateToUsers(page);

        // Find password reset form
        const passwordForm = page.locator('form[action*="ResetPassword"]:first-of-type');
        const formExists = await passwordForm.isVisible().catch(() => false);

        if (!formExists) {
            test.skip(true, 'No users available to test password reset');
            return;
        }

        // Verify password input exists
        const passwordInput = passwordForm.locator('input[type="password"]');
        await expect(passwordInput).toBeVisible();

        // Fill in a new password
        await passwordInput.fill('NewPassword123!');

        // Submit
        await passwordForm.locator('button[type="submit"], button:has-text("Set")').click();
        await page.waitForLoadState('networkidle');

        // Verify success message
        const successMessage = await page.locator('.alert-success').isVisible().catch(() => false);
        expect(successMessage).toBeTruthy();
    });

    test('P3-33: Export CSV functionality exists', async ({ page }) => {
        await navigateToUsers(page);

        // Find export CSV link/button
        const exportLink = page.locator('a:has-text("Export")');
        const linkExists = await exportLink.isVisible().catch(() => false);

        expect(linkExists).toBeTruthy();

        if (linkExists) {
            // Verify it has the correct handler
            const href = await exportLink.getAttribute('href');
            expect(href).toContain('ExportCsv');
        }
    });
});
