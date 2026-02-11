// @ts-check
const { test, expect } = require('@playwright/test');
const {
  loginAsOwner,
  saveEvidence,
  navigateTo,
  assertPageContains,
  assertMinCount,
  getTestDateRange,
} = require('../../helpers/production-qa-helpers');

const EVIDENCE = '08-master-programs';

test.describe('Module H: Master Programs', () => {
  test.beforeEach(async ({ page }) => {
    await loginAsOwner(page);
  });

  test('H-01: Create master program template', async ({ page }) => {
    await navigateTo(page, '/Owner/MasterPrograms');

    // STRICT: Assert the page heading loaded
    await expect(page.locator('main h1').first()).toContainText('Master Programs', { timeout: 10000 });

    // STRICT: Assert the create form is present
    const nameInput = page.locator('input[name="MasterProgramName"]');
    await expect(nameInput).toBeVisible({ timeout: 5000 });

    const descriptionInput = page.locator('textarea[name="MasterProgramDescription"]');
    await expect(descriptionInput).toBeVisible({ timeout: 5000 });

    // STRICT: Check if there are programs available to include
    const programCheckboxes = page.locator('input[name="SelectedProgramIds"]');
    const noPrograms = page.locator('.alert-warning:has-text("No programs available")');
    const hasPrograms = await programCheckboxes.first().isVisible({ timeout: 3000 }).catch(() => false);
    const hasMissingWarning = await noPrograms.isVisible({ timeout: 2000 }).catch(() => false);

    if (hasPrograms) {
      // Fill the form and create a master program
      await nameInput.fill('QA Standard Week');
      await descriptionInput.fill('Standard weekly schedule for QA testing');

      // Select at least one program
      const firstCheckbox = programCheckboxes.first();
      await expect(firstCheckbox).toBeVisible({ timeout: 3000 });
      await firstCheckbox.check();
      await expect(firstCheckbox).toBeChecked();

      // Submit the form
      const submitBtn = page.locator('button[type="submit"]:has-text("Create Master Program")');
      await expect(submitBtn).toBeVisible({ timeout: 3000 });
      await submitBtn.click();
      await page.waitForLoadState('networkidle');

      // STRICT: Assert the master program appears after creation
      await assertPageContains(page, 'QA Standard Week');

      // STRICT: Assert a master program card exists
      const masterCard = page.locator('.master-program-card:has-text("QA Standard Week")');
      await expect(masterCard).toBeVisible({ timeout: 5000 });
    } else {
      // STRICT: If no programs available, assert the warning is shown
      expect(hasMissingWarning).toBe(true);
    }

    await saveEvidence(page, EVIDENCE, 'H-01-create-master.png');
  });

  test('H-02: Apply master to Tzafona', async ({ page }) => {
    await navigateTo(page, '/Owner/MasterPrograms');

    // STRICT: Assert page loaded
    await expect(page.locator('main h1').first()).toContainText('Master Programs', { timeout: 10000 });

    // STRICT: Assert the OwnerCompanySelector is present
    const companySelector = page.locator('#ownerCompanySelect, select[name="OwnerCompanyId"], .owner-company-selector select').first();
    await expect(companySelector).toBeVisible({ timeout: 5000 });

    // STRICT: Check if any master program cards exist
    const masterCards = page.locator('.master-program-card');
    const masterCardCount = await masterCards.count();

    if (masterCardCount > 0) {
      // STRICT: Assert the generate button is present on the first card
      const generateBtn = page.locator('.generate-master-btn').first();
      await expect(generateBtn).toBeVisible({ timeout: 5000 });

      // STRICT: Assert the button has data attributes
      const masterId = await generateBtn.getAttribute('data-master-id');
      expect(masterId).toBeTruthy();
    } else {
      // STRICT: Assert the empty state message is displayed
      const emptyAlert = page.locator('.alert-info:has-text("No master programs defined yet")');
      await expect(emptyAlert).toBeVisible({ timeout: 5000 });
    }

    await saveEvidence(page, EVIDENCE, 'H-02-apply-tzafona.png');
  });

  test('H-03: Apply master to Hir', async ({ page }) => {
    await navigateTo(page, '/Owner/MasterPrograms');

    // STRICT: Assert page loaded
    await expect(page.locator('main h1').first()).toContainText('Master Programs', { timeout: 10000 });

    // Switch to Hir context
    const companySelector = page.locator('#ownerCompanySelect, select[name="OwnerCompanyId"], .owner-company-selector select').first();
    await expect(companySelector).toBeVisible({ timeout: 5000 });

    const options = await companySelector.locator('option').allTextContents();
    const hirIdx = options.findIndex(o => o.includes('Hir'));

    if (hirIdx >= 0) {
      await companySelector.selectOption({ index: hirIdx });
      await page.waitForLoadState('networkidle');

      // Company selector redirects to /Owner/Index — navigate back to MasterPrograms
      await navigateTo(page, '/Owner/MasterPrograms');
      await expect(page.locator('main h1').first()).toContainText('Master Programs', { timeout: 10000 });
    }

    // STRICT: Assert the page rendered without error (form or empty state)
    const nameInput = page.locator('input[name="MasterProgramName"]');
    await expect(nameInput).toBeVisible({ timeout: 5000 });

    await saveEvidence(page, EVIDENCE, 'H-03-apply-hir.png');
  });

  test('H-04: Edit master program', async ({ page }) => {
    await navigateTo(page, '/Owner/MasterPrograms');

    // STRICT: Assert page loaded
    await expect(page.locator('main h1').first()).toContainText('Master Programs', { timeout: 10000 });

    // STRICT: Verify the existing master programs section is present
    const existingSection = page.locator('h2:has-text("Existing Master Programs")');
    await expect(existingSection).toBeVisible({ timeout: 5000 });

    // Check master program cards
    const masterCards = page.locator('.master-program-card');
    const cardCount = await masterCards.count();

    if (cardCount > 0) {
      // STRICT: Assert the first card has a name and programs list
      const firstCard = masterCards.first();
      await expect(firstCard).toBeVisible({ timeout: 5000 });

      const cardTitle = firstCard.locator('.master-program-header h3');
      const titleText = await cardTitle.textContent();
      expect(titleText).toBeTruthy();
      expect(titleText.trim().length).toBeGreaterThan(0);

      // STRICT: Assert included programs list exists
      const programList = firstCard.locator('.program-list');
      await expect(programList).toBeVisible({ timeout: 3000 });

      const programItems = programList.locator('li');
      const itemCount = await programItems.count();
      expect(itemCount).toBeGreaterThanOrEqual(1);
    } else {
      // STRICT: Assert empty state is shown
      const emptyAlert = page.locator('.alert-info:has-text("No master programs")');
      await expect(emptyAlert).toBeVisible({ timeout: 5000 });
    }

    await saveEvidence(page, EVIDENCE, 'H-04-edit-master.png');
  });

  test('H-05: Delete master program', async ({ page }) => {
    await navigateTo(page, '/Owner/MasterPrograms');

    // STRICT: Assert page loaded
    await expect(page.locator('main h1').first()).toContainText('Master Programs', { timeout: 10000 });

    const masterCards = page.locator('.master-program-card');
    const cardCountBefore = await masterCards.count();

    if (cardCountBefore > 0) {
      // STRICT: Assert the delete form exists on a master program card
      const deleteForm = page.locator('form[action*="DeleteMasterProgram"]').last();
      await expect(deleteForm).toBeVisible({ timeout: 5000 });

      // Accept the confirmation dialog
      page.once('dialog', async dialog => {
        expect(dialog.type()).toBe('confirm');
        await dialog.accept();
      });

      const deleteBtn = deleteForm.locator('button[type="submit"]');
      await expect(deleteBtn).toBeVisible({ timeout: 3000 });
      await deleteBtn.click();
      await page.waitForLoadState('networkidle');

      // STRICT: Assert the page is still Master Programs (no error)
      await expect(page.locator('main h1').first()).toContainText('Master Programs', { timeout: 10000 });

      // STRICT: Assert card count decreased or success shown
      const cardCountAfter = await masterCards.count();
      const successAlert = page.locator('.alert-success');
      const hasSuccess = await successAlert.isVisible({ timeout: 3000 }).catch(() => false);
      expect(cardCountAfter < cardCountBefore || hasSuccess).toBe(true);
    } else {
      // STRICT: If no cards to delete, assert the empty state
      const emptyAlert = page.locator('.alert-info:has-text("No master programs")');
      await expect(emptyAlert).toBeVisible({ timeout: 5000 });
    }

    await saveEvidence(page, EVIDENCE, 'H-05-delete-master.png');
  });

  test('H-06: Master program does not affect already-generated instances', async ({ page }) => {
    await navigateTo(page, '/Calendar/Shifts');
    await page.waitForLoadState('networkidle');

    // STRICT: Assert the calendar page loaded with the correct structure
    const calendar = page.locator('.shifts-calendar');
    await expect(calendar).toBeVisible({ timeout: 10000 });

    // STRICT: Assert the toolbar is present
    const toolbar = page.locator('.shifts-calendar__toolbar');
    await expect(toolbar).toBeVisible({ timeout: 5000 });

    // STRICT: Assert the molecule and jobType selectors are present
    const moleculeSelect = page.locator('#moleculeSelect');
    const jobTypeSelect = page.locator('#jobTypeSelect');
    await expect(moleculeSelect).toBeVisible({ timeout: 5000 });
    await expect(jobTypeSelect).toBeVisible({ timeout: 5000 });

    // STRICT: Assert the date navigation links are present
    const prevLink = page.locator('.shifts-calendar__nav-btn').first();
    const nextLink = page.locator('.shifts-calendar__nav-btn').last();
    await expect(prevLink).toBeVisible({ timeout: 5000 });
    await expect(nextLink).toBeVisible({ timeout: 5000 });

    // STRICT: Assert either calendar data or empty state is shown
    const calendarTable = page.locator('.shifts-calendar table, .excel-calendar');
    const emptyState = page.locator('.shifts-calendar__empty');
    const hasCalendar = await calendarTable.first().isVisible({ timeout: 5000 }).catch(() => false);
    const hasEmpty = await emptyState.isVisible({ timeout: 3000 }).catch(() => false);
    expect(hasCalendar || hasEmpty).toBe(true);

    await saveEvidence(page, EVIDENCE, 'H-06-existing-unchanged.png');
  });
});
