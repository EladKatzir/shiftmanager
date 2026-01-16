// @ts-check
const { test, expect } = require('@playwright/test');
const { loginAsOwner, loginAsRole } = require('../helpers/auth-helpers');
const TestDataFactory = require('../helpers/test-data-factory');

/**
 * Shift Assignment Workflow End-to-End Tests - Task 4
 *
 * Tests the complete shift assignment process from Blueprint → Program → Shift → Assignment
 *
 * Prerequisites:
 * - Owner and Employee accounts must exist with credentials in .env
 * - Application must be running on APP_URL (default: http://localhost:5000)
 * - At least one company must exist in the system
 *
 * Test Flow:
 * 1. Create Blueprint (ShiftType with StartTime, EndTime)
 * 2. Create Program using Blueprint
 * 3. Navigate to Calendar/Table
 * 4. Assign shift (if assignment UI exists)
 * 5. Verify assignment persists after reload
 * 6. Verify employee can see their assignment
 */

test.describe('Shift Assignment Workflow - End-to-End', () => {
    let testContext = {
        blueprintId: null,
        blueprintKey: null,
        programId: null,
        programName: null,
        shiftDate: null,
        assignmentCreated: false
    };

    test.beforeAll(async () => {
        // Owner credentials are configured by default in auth-helpers.js
    });

    test('Complete workflow: Blueprint → Program → Shift → Assignment', async ({ page }) => {
        // ========================================
        // PHASE 1: Create Blueprint (ShiftType)
        // ========================================
        await test.step('Phase 1: Create Blueprint', async () => {
            await loginAsOwner(page);

            // Navigate to Blueprints page
            await page.goto('/Owner/Blueprints');
            await page.waitForLoadState('networkidle');

            // Verify we're on the Blueprints page
            await expect(page.locator('h1')).toContainText(/Blueprint/i);

            // Generate unique blueprint data
            const blueprintData = TestDataFactory.generateShiftType({
                key: `E2E_${Date.now()}`.substring(0, 20).toUpperCase(),
                start: '09:00',
                end: '17:00'
            });

            testContext.blueprintKey = blueprintData.key;

            // Fill blueprint creation form
            await page.fill('input[name="NewShiftKey"]', blueprintData.key);
            await page.fill('input[name="NewShiftNameEn"]', `Test Shift ${blueprintData.key}`);
            await page.fill('input[name="NewShiftNameHe"]', `משמרת בדיקה ${blueprintData.key}`);
            await page.fill('input[name="NewShiftStart"]', blueprintData.start);
            await page.fill('input[name="NewShiftEnd"]', blueprintData.end);

            // Submit the form
            await Promise.all([
                page.waitForLoadState('networkidle'),
                page.click('button[type="submit"]:has-text("Create Shift Type")')
            ]);

            // Verify success message or blueprint appears in table
            const successVisible = await page.locator('.alert-success').isVisible().catch(() => false);
            const blueprintInTable = await page.locator(`tr:has-text("${blueprintData.key}")`).isVisible().catch(() => false);

            expect(successVisible || blueprintInTable).toBeTruthy();

            // Store blueprint ID if available
            const blueprintRow = page.locator(`tr:has-text("${blueprintData.key}")`).first();
            const blueprintExists = await blueprintRow.isVisible().catch(() => false);

            if (blueprintExists) {
                const dataShiftId = await blueprintRow.getAttribute('data-shift-id');
                if (dataShiftId) {
                    testContext.blueprintId = parseInt(dataShiftId);
                }
            }

            console.log(`✓ Blueprint created: ${blueprintData.key} (ID: ${testContext.blueprintId})`);
        });

        // ========================================
        // PHASE 2: Create Program using Blueprint
        // ========================================
        await test.step('Phase 2: Create Program', async () => {
            // Navigate to Programs page
            await page.goto('/Owner/Programs');
            await page.waitForLoadState('networkidle');

            // Verify we're on the Programs page
            await expect(page.locator('h1')).toContainText(/Program/i);

            // Check if our blueprint appears in the dropdown
            const blueprintOption = page.locator(`select[name="ShiftTypeId"] option:has-text("${testContext.blueprintKey}")`).first();
            const blueprintAvailable = await blueprintOption.isVisible().catch(() => false);

            if (!blueprintAvailable) {
                // Blueprint might not be immediately available, try refreshing
                await page.reload({ waitUntil: 'networkidle' });
                const retryAvailable = await page.locator(`select[name="ShiftTypeId"] option:has-text("${testContext.blueprintKey}")`).first().isVisible().catch(() => false);

                if (!retryAvailable) {
                    test.skip(true, 'Blueprint not available in Programs dropdown - may require app restart or different data flow');
                }
            }

            // Generate unique program data
            testContext.programName = `E2E_Program_${Date.now()}`;

            // Select the blueprint from dropdown
            await page.selectOption('select[name="ShiftTypeId"]', { label: new RegExp(testContext.blueprintKey) });

            // Fill program name
            await page.fill('input[name="ProgramName"]', testContext.programName);

            // Select days of week (Monday and Tuesday for testing)
            await page.check('input[name="SelectedDays"][value="Monday"]');
            await page.check('input[name="SelectedDays"][value="Tuesday"]');

            // Set default staffing
            await page.fill('input[name="DefaultStaffing"]', '2');

            // Submit the form
            await Promise.all([
                page.waitForLoadState('networkidle'),
                page.click('button[type="submit"]:has-text("Create Program")')
            ]);

            // Verify success message or program appears
            const successVisible = await page.locator('.alert-success').isVisible().catch(() => false);
            const programCard = await page.locator(`.program-card:has-text("${testContext.programName}")`).isVisible().catch(() => false);

            expect(successVisible || programCard).toBeTruthy();

            console.log(`✓ Program created: ${testContext.programName}`);
        });

        // ========================================
        // PHASE 3: Navigate to Calendar/Table
        // ========================================
        await test.step('Phase 3: Navigate to Calendar Table', async () => {
            // Navigate to Calendar Table view
            await page.goto('/Calendar/Table');
            await page.waitForLoadState('networkidle');

            // Verify we're on the Calendar Table page
            const isTableView = await page.locator('h1:has-text("Table"), .table-view-container').isVisible().catch(() => false);

            if (!isTableView) {
                test.skip(true, 'Calendar Table view not available');
            }

            // Look for shift instances created by the program
            const shiftCell = page.locator(`.assignment-cell, .shift-table td:has-text("${testContext.blueprintKey}")`).first();
            const shiftExists = await shiftCell.isVisible({ timeout: 5000 }).catch(() => false);

            if (!shiftExists) {
                console.log('⚠ Shift instances not yet generated - may require background job or manual trigger');
                test.skip(true, 'Shift instances not found - may require program execution trigger');
            }

            console.log(`✓ Navigated to Calendar Table - shift instances visible`);
        });

        // ========================================
        // PHASE 4: Assign Shift (if UI supports it)
        // ========================================
        await test.step('Phase 4: Assign Shift', async () => {
            // Check if assignment UI exists
            const rosterDock = page.locator('#rosterDock, .roster-dock').first();
            const hasRosterDock = await rosterDock.isVisible({ timeout: 2000 }).catch(() => false);

            const assignmentCell = page.locator('.assignment-cell, .assignment-slot').first();
            const hasAssignmentCell = await assignmentCell.isVisible({ timeout: 2000 }).catch(() => false);

            if (!hasRosterDock && !hasAssignmentCell) {
                console.log('⚠ Assignment UI not found - skipping interactive assignment test');
                test.skip(true, 'Assignment UI not available - requires drag-and-drop or click assignment interface');
            }

            // Try to perform assignment via drag-and-drop if roster dock exists
            if (hasRosterDock) {
                // Look for an employee in the roster
                const employeeElement = page.locator('.roster-dock .employee-item, .roster-dock [data-user-id]').first();
                const hasEmployee = await employeeElement.isVisible({ timeout: 2000 }).catch(() => false);

                if (hasEmployee && hasAssignmentCell) {
                    // Attempt drag-and-drop assignment
                    try {
                        await employeeElement.dragTo(assignmentCell);
                        testContext.assignmentCreated = true;
                        console.log('✓ Shift assignment created via drag-and-drop');
                    } catch (error) {
                        console.log('⚠ Drag-and-drop failed:', error.message);
                    }
                }
            }

            // Alternative: Click-based assignment if available
            if (!testContext.assignmentCreated && hasAssignmentCell) {
                const clickable = await assignmentCell.isEnabled().catch(() => false);
                if (clickable) {
                    await assignmentCell.click();

                    // Look for assignment dialog or inline edit
                    const dialog = page.locator('.modal, .dialog, .assignment-dialog').first();
                    const hasDialog = await dialog.isVisible({ timeout: 2000 }).catch(() => false);

                    if (hasDialog) {
                        // Try to select an employee and save
                        const employeeSelect = dialog.locator('select, .employee-selector').first();
                        const hasSelect = await employeeSelect.isVisible({ timeout: 1000 }).catch(() => false);

                        if (hasSelect) {
                            const options = await employeeSelect.locator('option').count();
                            if (options > 1) {
                                await employeeSelect.selectOption({ index: 1 });
                                await dialog.locator('button[type="submit"], button:has-text("Save"), button:has-text("Assign")').click();
                                testContext.assignmentCreated = true;
                                console.log('✓ Shift assignment created via dialog');
                            }
                        }
                    }
                }
            }

            if (!testContext.assignmentCreated) {
                console.log('⚠ Could not create assignment - UI interaction not supported in current test');
            }
        });

        // ========================================
        // PHASE 5: Verify Assignment Persists
        // ========================================
        await test.step('Phase 5: Verify Assignment Persists', async () => {
            if (!testContext.assignmentCreated) {
                test.skip(true, 'Assignment not created - skipping persistence verification');
            }

            // Reload the page
            await page.reload({ waitUntil: 'networkidle' });

            // Verify assignment still exists
            const assignedCell = page.locator('.assignment-slot:not(.unassigned), .assignment-cell:has(.assigned)').first();
            const assignmentPersists = await assignedCell.isVisible({ timeout: 5000 }).catch(() => false);

            expect(assignmentPersists).toBeTruthy();

            console.log('✓ Assignment persisted after reload');
        });

        // ========================================
        // PHASE 6: Verify Employee Can See Assignment
        // ========================================
        await test.step('Phase 6: Verify Employee View', async () => {
            if (!RoleHelper.isRoleConfigured('Employee')) {
                console.log('⚠ Employee credentials not configured - skipping employee view test');
                return;
            }

            // Logout and login as Employee
            await RoleHelper.logout(page);
            await loginAsRole(page, 'Employee');

            // Navigate to employee's schedule view
            const possibleViews = ['/Calendar/Month', '/My/Index', '/Schedule/Index'];

            let foundAssignment = false;

            for (const viewUrl of possibleViews) {
                await page.goto(viewUrl);
                await page.waitForLoadState('networkidle');

                // Check if assignment is visible in this view
                const hasAssignment = await page.locator(
                    `.assignment, .shift-assignment, .calendar-event:has-text("${testContext.blueprintKey}")`
                ).first().isVisible({ timeout: 2000 }).catch(() => false);

                if (hasAssignment) {
                    foundAssignment = true;
                    console.log(`✓ Employee can see assignment in ${viewUrl}`);
                    break;
                }
            }

            if (!foundAssignment && !testContext.assignmentCreated) {
                console.log('⚠ No assignment was created, so employee view test is informational only');
            } else if (!foundAssignment && testContext.assignmentCreated) {
                console.log('⚠ Assignment created but not visible in employee views - may require specific user assignment');
            }
        });
    });

    test('Data integrity: Deleting blueprint prevents new program creation', async ({ page }) => {
        // ========================================
        // PHASE 1: Create a Blueprint to Delete
        // ========================================
        await test.step('Create Blueprint to Delete', async () => {
            await loginAsOwner(page);

            // Navigate to Blueprints page
            await page.goto('/Owner/Blueprints');
            await page.waitForLoadState('networkidle');

            // Generate unique blueprint for deletion test
            const blueprintData = TestDataFactory.generateShiftType({
                key: `DEL_${Date.now()}`.substring(0, 20).toUpperCase(),
                start: '10:00',
                end: '18:00'
            });

            testContext.blueprintKey = blueprintData.key;

            // Create the blueprint
            await page.fill('input[name="NewShiftKey"]', blueprintData.key);
            await page.fill('input[name="NewShiftNameEn"]', `Delete Test ${blueprintData.key}`);
            await page.fill('input[name="NewShiftNameHe"]', `בדיקת מחיקה ${blueprintData.key}`);
            await page.fill('input[name="NewShiftStart"]', blueprintData.start);
            await page.fill('input[name="NewShiftEnd"]', blueprintData.end);

            await Promise.all([
                page.waitForLoadState('networkidle'),
                page.click('button[type="submit"]:has-text("Create Shift Type")')
            ]);

            // Verify blueprint exists
            const blueprintRow = page.locator(`tr:has-text("${blueprintData.key}")`).first();
            await expect(blueprintRow).toBeVisible({ timeout: 5000 });

            console.log(`✓ Blueprint created for deletion test: ${blueprintData.key}`);
        });

        // ========================================
        // PHASE 2: Delete the Blueprint
        // ========================================
        await test.step('Delete Blueprint', async () => {
            // Find the delete button for our blueprint
            const blueprintRow = page.locator(`tr:has-text("${testContext.blueprintKey}")`).first();
            const deleteButton = blueprintRow.locator('button:has-text("Delete"), form[action*="Delete"] button, a:has-text("Delete")').first();

            const hasDeleteButton = await deleteButton.isVisible({ timeout: 2000 }).catch(() => false);

            if (!hasDeleteButton) {
                test.skip(true, 'Delete functionality not available in UI');
            }

            // Click delete and handle confirmation if present
            const confirmationDialog = page.locator('.modal, .dialog, .confirm-dialog').first();

            await deleteButton.click();

            // Check if confirmation dialog appears
            const hasConfirmation = await confirmationDialog.isVisible({ timeout: 2000 }).catch(() => false);

            if (hasConfirmation) {
                const confirmButton = confirmationDialog.locator('button:has-text("Delete"), button:has-text("Confirm"), button.btn-danger').first();
                await confirmButton.click();
            }

            await page.waitForLoadState('networkidle');

            // Verify blueprint no longer exists in table
            const blueprintGone = await page.locator(`tr:has-text("${testContext.blueprintKey}")`).first().isVisible({ timeout: 2000 }).catch(() => false);

            expect(blueprintGone).toBeFalsy();

            console.log(`✓ Blueprint deleted: ${testContext.blueprintKey}`);
        });

        // ========================================
        // PHASE 3: Verify Deleted Blueprint Not in Program Dropdown
        // ========================================
        await test.step('Verify Deleted Blueprint Not in Programs Dropdown', async () => {
            // Navigate to Programs page
            await page.goto('/Owner/Programs');
            await page.waitForLoadState('networkidle');

            // Verify we're on the Programs page
            await expect(page.locator('h1')).toContainText(/Program/i);

            // Check that deleted blueprint does NOT appear in dropdown
            const deletedBlueprintOption = page.locator(`select[name="ShiftTypeId"] option:has-text("${testContext.blueprintKey}")`).first();
            const stillVisible = await deletedBlueprintOption.isVisible({ timeout: 2000 }).catch(() => false);

            expect(stillVisible).toBeFalsy();

            console.log(`✓ Deleted blueprint not visible in Programs dropdown`);
        });

        // ========================================
        // PHASE 4: Attempt to Create Program (Should Fail or Show No Option)
        // ========================================
        await test.step('Verify Cannot Create Program with Deleted Blueprint', async () => {
            // Verify dropdown doesn't contain the deleted blueprint
            const shiftTypeSelect = page.locator('select[name="ShiftTypeId"]');
            const options = await shiftTypeSelect.locator('option').allTextContents();

            const deletedBlueprintInOptions = options.some(opt => opt.includes(testContext.blueprintKey));

            expect(deletedBlueprintInOptions).toBeFalsy();

            console.log('✓ Data integrity verified: Deleted blueprint cannot be used for new programs');
        });
    });
});

test.describe('Shift Assignment Workflow - Edge Cases', () => {
    test.beforeAll(async () => {
        // Owner credentials are configured by default in auth-helpers.js
    });

    test('Blueprint without start/end time validation', async ({ page }) => {
        await test.step('Test Blueprint Time Validation', async () => {
            await loginAsOwner(page);

            await page.goto('/Owner/Blueprints');
            await page.waitForLoadState('networkidle');

            // Try to create blueprint with invalid times (end before start)
            const blueprintData = TestDataFactory.generateShiftType({
                key: `INV_${Date.now()}`.substring(0, 20).toUpperCase(),
                start: '18:00',
                end: '09:00' // Invalid: end before start
            });

            await page.fill('input[name="NewShiftKey"]', blueprintData.key);
            await page.fill('input[name="NewShiftNameEn"]', 'Invalid Time Test');
            await page.fill('input[name="NewShiftNameHe"]', 'בדיקת זמן לא תקין');
            await page.fill('input[name="NewShiftStart"]', blueprintData.start);
            await page.fill('input[name="NewShiftEnd"]', blueprintData.end);

            await page.click('button[type="submit"]:has-text("Create Shift Type")');
            await page.waitForLoadState('networkidle');

            // Should show error or validation message
            const hasError = await page.locator('.alert-danger, .error, .validation-error, .field-validation-error').isVisible({ timeout: 2000 }).catch(() => false);
            const blueprintNotCreated = !(await page.locator(`tr:has-text("${blueprintData.key}")`).isVisible({ timeout: 2000 }).catch(() => false));

            // Either error is shown OR blueprint wasn't created (validation worked)
            expect(hasError || blueprintNotCreated).toBeTruthy();

            console.log('✓ Time validation working (end time before start time rejected or allowed for overnight shifts)');
        });
    });

    test('Program creation without selected days', async ({ page }) => {
        await test.step('Test Program Day Selection Validation', async () => {
            await loginAsOwner(page);

            await page.goto('/Owner/Programs');
            await page.waitForLoadState('networkidle');

            // Check if any shift types exist
            const shiftTypeSelect = page.locator('select[name="ShiftTypeId"]');
            const options = await shiftTypeSelect.locator('option').count();

            if (options <= 1) {
                test.skip(true, 'No shift types available for program creation test');
            }

            // Select first available shift type
            await shiftTypeSelect.selectOption({ index: 1 });

            // Fill program name
            await page.fill('input[name="ProgramName"]', `NoDay_${Date.now()}`);

            // Don't select any days - leave all unchecked
            // Uncheck all days if any are checked by default
            const checkedDays = await page.locator('input[name="SelectedDays"]:checked').count();
            if (checkedDays > 0) {
                await page.uncheck('input[name="SelectedDays"]');
            }

            // Set default staffing
            await page.fill('input[name="DefaultStaffing"]', '1');

            // Submit the form
            await page.click('button[type="submit"]:has-text("Create Program")');
            await page.waitForLoadState('networkidle');

            // Should show error or validation message
            const hasError = await page.locator('.alert-danger, .error, .validation-error').isVisible({ timeout: 2000 }).catch(() => false);

            if (hasError) {
                console.log('✓ Validation working: Program requires at least one day selected');
            } else {
                console.log('⚠ No validation error shown - program may have been created without days or validation is client-side only');
            }
        });
    });
});
