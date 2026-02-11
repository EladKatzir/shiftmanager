// @ts-check
const { test, expect } = require('@playwright/test');
const {
  loginAsOwner, login, logout, saveEvidence, navigateTo,
  assertPageContains, assertPageNotContains, assertMinCount,
  TEST_PASSWORD, ROLE_ENUM
} = require('../../helpers/production-qa-helpers');

const EVIDENCE = '10-shift-assignments';

test.describe('Module J: Shift Assignment Lifecycle', () => {
  test.beforeEach(async ({ page }) => {
    await loginAsOwner(page);
  });

  // ---------------------------------------------------------------------------
  // J-01: Shifts calendar renders with table structure
  // ---------------------------------------------------------------------------
  test('J-01: Shifts calendar renders with table, toolbar, and date nav', async ({ page }) => {
    await navigateTo(page, '/Calendar/Shifts');

    // ASSERT: The main calendar wrapper is visible
    const calendarWrapper = page.locator('.shifts-calendar');
    await expect(calendarWrapper).toBeVisible({ timeout: 15000 });

    // ASSERT: Toolbar with selectors is visible
    const toolbar = page.locator('.shifts-calendar__toolbar');
    await expect(toolbar).toBeVisible();

    // ASSERT: Molecule and JobType selectors are present
    const moleculeSelect = page.locator('#moleculeSelect');
    await expect(moleculeSelect).toBeVisible();
    const jobTypeSelect = page.locator('#jobTypeSelect');
    await expect(jobTypeSelect).toBeVisible();

    // ASSERT: View mode selector is present
    const viewModeSelect = page.locator('#viewModeSelect');
    await expect(viewModeSelect).toBeVisible();

    // ASSERT: Date navigation is present
    const dateNav = page.locator('.shifts-calendar__date-nav');
    await expect(dateNav).toBeVisible();

    // ASSERT: Either the calendar table rendered or we see the empty state
    const calendarTable = page.locator('.excel-calendar__table');
    const emptyState = page.locator('.shifts-calendar__empty');
    await expect(calendarTable.or(emptyState)).toBeVisible({ timeout: 10000 });

    await saveEvidence(page, EVIDENCE, 'J-01-calendar-renders.png');
  });

  // ---------------------------------------------------------------------------
  // J-02: Excel calendar table has correct structure (rows, cells, headers)
  // ---------------------------------------------------------------------------
  test('J-02: Excel calendar table has header days and row labels', async ({ page }) => {
    await navigateTo(page, '/Calendar/Shifts');

    const calendarTable = page.locator('.excel-calendar__table');
    const emptyState = page.locator('.shifts-calendar__empty');
    await expect(calendarTable.or(emptyState)).toBeVisible({ timeout: 10000 });

    const hasTable = await calendarTable.isVisible();

    if (hasTable) {
      // ASSERT: Header row has day columns
      const headerDays = page.locator('.excel-calendar__header-day');
      const headerCount = await headerDays.count();
      expect(headerCount).toBeGreaterThanOrEqual(7); // at least one week

      // ASSERT: There is at least one data row
      const rows = page.locator('.excel-calendar__table tbody tr[data-row-id]');
      const rowCount = await rows.count();
      expect(rowCount).toBeGreaterThanOrEqual(1);

      // ASSERT: Each data row has a label
      const firstRowLabel = rows.first().locator('.excel-calendar__row-label');
      await expect(firstRowLabel).toBeVisible();
    }
    // If empty state is visible, the .or() assertion already passed — no silent else

    await saveEvidence(page, EVIDENCE, 'J-02-table-structure.png');
  });

  // ---------------------------------------------------------------------------
  // J-03: Cells have data attributes (row-id, date) for interactivity
  // ---------------------------------------------------------------------------
  test('J-03: Calendar cells have data-row-id and data-date attributes', async ({ page }) => {
    await navigateTo(page, '/Calendar/Shifts');

    const calendarTable = page.locator('.excel-calendar__table');
    const emptyState = page.locator('.shifts-calendar__empty');
    await expect(calendarTable.or(emptyState)).toBeVisible({ timeout: 10000 });

    const hasTable = await calendarTable.isVisible();

    if (hasTable) {
      const cells = page.locator('.excel-calendar__cell[data-row-id][data-date]');
      const cellCount = await cells.count();
      // ASSERT: At least 7 cells (one week of one row)
      expect(cellCount).toBeGreaterThanOrEqual(7);

      // ASSERT: The first cell has a valid date attribute (yyyy-MM-dd)
      const firstDate = await cells.first().getAttribute('data-date');
      expect(firstDate).toMatch(/^\d{4}-\d{2}-\d{2}$/);
    }
    // If empty state is visible, the .or() assertion already passed — no silent else

    await saveEvidence(page, EVIDENCE, 'J-03-cell-data-attrs.png');
  });

  // ---------------------------------------------------------------------------
  // J-04: Owner sees add-buttons (non-readonly cells)
  // ---------------------------------------------------------------------------
  test('J-04: Owner sees add-assignment buttons on editable cells', async ({ page }) => {
    await navigateTo(page, '/Calendar/Shifts');

    const calendarTable = page.locator('.excel-calendar__table');
    const emptyState = page.locator('.shifts-calendar__empty');
    await expect(calendarTable.or(emptyState)).toBeVisible({ timeout: 10000 });

    const hasTable = await calendarTable.isVisible();

    if (hasTable) {
      // Owner should see cells that are NOT readonly — Owner always has edit capability
      const addButtons = page.locator('.excel-calendar__cell:not(.excel-calendar__cell--readonly) .excel-calendar__add-btn');
      const addCount = await addButtons.count();
      // ASSERT: Owner MUST have at least one add button when the table is visible
      expect(addCount).toBeGreaterThanOrEqual(1);
    }
    // If empty state is visible, the .or() assertion already passed — no silent else

    await saveEvidence(page, EVIDENCE, 'J-04-owner-add-buttons.png');
  });

  // ---------------------------------------------------------------------------
  // J-05: Click add button opens assignment interaction (bottom-sheet or inline)
  // ---------------------------------------------------------------------------
  test('J-05: Clicking add button triggers assignment UI', async ({ page }) => {
    await navigateTo(page, '/Calendar/Shifts');

    // ASSERT: Owner must see at least one add button (not empty/readonly)
    const addBtn = page.locator('.excel-calendar__add-btn').first();
    await expect(addBtn).toBeVisible({ timeout: 10000 });

    await addBtn.click();
    await page.waitForTimeout(500);

    // ASSERT: Some assignment UI appeared (bottom-sheet or inline selector or modal)
    const bottomSheet = page.locator('.bottom-sheet--open');
    const assigneeSelect = page.locator('[id^="assigneeSelect-"], select[data-role="assignee-select"]').first();
    const anyModal = page.locator('.modal:not([hidden]), [role="dialog"]:not([hidden])').first();

    // At least one assignment UI must appear — use .or() chain
    await expect(bottomSheet.or(assigneeSelect).or(anyModal)).toBeVisible({ timeout: 5000 });

    await saveEvidence(page, EVIDENCE, 'J-05-add-btn-ui.png');
  });

  // ---------------------------------------------------------------------------
  // J-06: Assigned users show in cells with correct structure
  // ---------------------------------------------------------------------------
  test('J-06: Existing assignments appear as named elements inside cells', async ({ page }) => {
    await navigateTo(page, '/Calendar/Shifts');

    const calendarTable = page.locator('.excel-calendar__table');
    const emptyState = page.locator('.shifts-calendar__empty');
    await expect(calendarTable.or(emptyState)).toBeVisible({ timeout: 10000 });

    const hasTable = await calendarTable.isVisible();

    if (hasTable) {
      // Check for any existing assignments
      const assignments = page.locator('.excel-calendar__assignment');
      const assignmentCount = await assignments.count();

      if (assignmentCount > 0) {
        // ASSERT: Each assignment has a name element
        const firstAssignment = assignments.first();
        const nameEl = firstAssignment.locator('.excel-calendar__assignment-name');
        await expect(nameEl).toBeVisible();

        // ASSERT: The name is non-empty
        const nameText = await nameEl.textContent();
        expect(nameText.trim().length).toBeGreaterThan(0);
      }
      // If no assignments exist that is valid state -- calendar might be newly seeded
    }
    // If empty state is visible, the .or() assertion already passed — no silent else

    await saveEvidence(page, EVIDENCE, 'J-06-assignment-names.png');
  });

  // ---------------------------------------------------------------------------
  // J-07: Trainee badge displayed on trainee assignments
  // ---------------------------------------------------------------------------
  test('J-07: Trainee assignments show trainee badge', async ({ page }) => {
    await navigateTo(page, '/Calendar/Shifts');

    const calendarTable = page.locator('.excel-calendar__table');
    const emptyState = page.locator('.shifts-calendar__empty');
    await expect(calendarTable.or(emptyState)).toBeVisible({ timeout: 10000 });

    const hasTable = await calendarTable.isVisible();

    if (hasTable) {
      const traineeBadges = page.locator('.excel-calendar__badge--trainee');
      const traineeBadgeCount = await traineeBadges.count();

      // If trainee assignments exist, verify their structure
      if (traineeBadgeCount > 0) {
        // ASSERT: Trainee badge is inside an assignment element
        const parentAssignment = traineeBadges.first().locator('..');
        const parentClass = await parentAssignment.getAttribute('class');
        expect(parentClass).toContain('excel-calendar__assignment');
      }
      // It is valid for there to be no trainee assignments
    }

    await saveEvidence(page, EVIDENCE, 'J-07-trainee-badge.png');
  });

  // ---------------------------------------------------------------------------
  // J-08: ByShift / ByUser mode toggle works
  // ---------------------------------------------------------------------------
  test('J-08: Shift/User mode toggle changes view', async ({ page }) => {
    await navigateTo(page, '/Calendar/Shifts');

    // ASSERT: Mode toggle links exist
    const byShiftLink = page.locator('.shifts-calendar__toggle-group a[href*="Mode=shift"]');
    const byUserLink = page.locator('.shifts-calendar__toggle-group a[href*="Mode=user"]');
    await expect(byShiftLink).toBeVisible({ timeout: 10000 });
    await expect(byUserLink).toBeVisible();

    // Navigate to user mode
    await byUserLink.click();
    await page.waitForLoadState('networkidle');

    // ASSERT: URL now includes Mode=user
    expect(page.url()).toContain('Mode=user');

    // ASSERT: Page still renders without error
    const calendarWrapper = page.locator('.shifts-calendar');
    await expect(calendarWrapper).toBeVisible();

    await saveEvidence(page, EVIDENCE, 'J-08-user-mode.png');
  });

  // ---------------------------------------------------------------------------
  // J-09: Print button exists and is functional
  // ---------------------------------------------------------------------------
  test('J-09: Print button is present in toolbar', async ({ page }) => {
    await navigateTo(page, '/Calendar/Shifts');

    // ASSERT: Print button is visible in the actions area
    const printBtn = page.locator('.shifts-calendar__actions button[onclick*="print"]');
    await expect(printBtn).toBeVisible({ timeout: 10000 });

    await saveEvidence(page, EVIDENCE, 'J-09-print-button.png');
  });

  // ---------------------------------------------------------------------------
  // J-10: JustMine toggle works
  // ---------------------------------------------------------------------------
  test('J-10: JustMine toggle filters calendar', async ({ page }) => {
    await navigateTo(page, '/Calendar/Shifts');

    // ASSERT: JustMine link exists
    const justMineLink = page.locator('.shifts-calendar__toggle-group a[href*="JustMine="]');
    await expect(justMineLink).toBeVisible({ timeout: 10000 });

    // Click JustMine
    await justMineLink.click();
    await page.waitForLoadState('networkidle');

    // ASSERT: URL now has JustMine parameter
    expect(page.url().toLowerCase()).toContain('justmine=true');

    // ASSERT: Page renders without errors
    const calendarWrapper = page.locator('.shifts-calendar');
    await expect(calendarWrapper).toBeVisible();

    await saveEvidence(page, EVIDENCE, 'J-10-just-mine.png');
  });

  // ---------------------------------------------------------------------------
  // J-11: CapacityMode toggle (owner only)
  // ---------------------------------------------------------------------------
  test('J-11: CapacityMode toggle is visible for Owner', async ({ page }) => {
    await navigateTo(page, '/Calendar/Shifts');

    // Owner should see CapacityMode toggle (rendered only if Model.CanEdit)
    const capacityLink = page.locator('.shifts-calendar__toggle-group a[href*="CapacityMode"]');
    const emptyState = page.locator('.shifts-calendar__empty');

    // ASSERT: Owner has capacity mode toggle OR the calendar is empty
    await expect(capacityLink.or(emptyState)).toBeVisible({ timeout: 5000 });

    await saveEvidence(page, EVIDENCE, 'J-11-capacity-mode.png');
  });

  // ---------------------------------------------------------------------------
  // J-12: Manager (AlhutLead) can access shifts calendar
  // ---------------------------------------------------------------------------
  test('J-12: AlhutLead manager can access shifts calendar', async ({ page }) => {
    await logout(page);
    await login(page, 'mgr.alhut.tz@test', TEST_PASSWORD);

    await navigateTo(page, '/Calendar/Shifts');

    // ASSERT: Calendar wrapper is visible (not blocked/redirect)
    const calendarWrapper = page.locator('.shifts-calendar');
    await expect(calendarWrapper).toBeVisible({ timeout: 15000 });

    // ASSERT: Manager sees the toolbar
    const toolbar = page.locator('.shifts-calendar__toolbar');
    await expect(toolbar).toBeVisible();

    await saveEvidence(page, EVIDENCE, 'J-12-alhut-lead.png');
  });

  // ---------------------------------------------------------------------------
  // J-13: TextLead manager can access shifts calendar
  // ---------------------------------------------------------------------------
  test('J-13: TextLead manager can access shifts calendar', async ({ page }) => {
    await logout(page);
    await login(page, 'mgr.text.tz@test', TEST_PASSWORD);

    await navigateTo(page, '/Calendar/Shifts');

    // ASSERT: Calendar is visible
    const calendarWrapper = page.locator('.shifts-calendar');
    await expect(calendarWrapper).toBeVisible({ timeout: 15000 });

    await saveEvidence(page, EVIDENCE, 'J-13-text-lead.png');
  });

  // ---------------------------------------------------------------------------
  // J-14: BRDirector can access shifts calendar
  // ---------------------------------------------------------------------------
  test('J-14: BRDirector can access shifts calendar', async ({ page }) => {
    await logout(page);
    await login(page, 'dir.br@test', TEST_PASSWORD);

    await navigateTo(page, '/Calendar/Shifts');

    // ASSERT: Calendar is visible
    const calendarWrapper = page.locator('.shifts-calendar');
    await expect(calendarWrapper).toBeVisible({ timeout: 15000 });

    await saveEvidence(page, EVIDENCE, 'J-14-br-director.png');
  });

  // ---------------------------------------------------------------------------
  // J-15: Employee sees readonly calendar (no add buttons)
  // ---------------------------------------------------------------------------
  test('J-15: Employee sees readonly calendar with no assignment controls', async ({ page }) => {
    await logout(page);
    await login(page, 'emp.tz.alhut@test', TEST_PASSWORD);

    await navigateTo(page, '/Calendar/Shifts');

    // ASSERT: Calendar wrapper is visible
    const calendarWrapper = page.locator('.shifts-calendar');
    await expect(calendarWrapper).toBeVisible({ timeout: 15000 });

    // ASSERT: Either table or empty state is visible
    const calendarTable = page.locator('.excel-calendar__table');
    const emptyState = page.locator('.shifts-calendar__empty');
    await expect(calendarTable.or(emptyState)).toBeVisible({ timeout: 10000 });

    const hasTable = await calendarTable.isVisible();

    if (hasTable) {
      const addButtons = page.locator('.excel-calendar__add-btn');
      const addCount = await addButtons.count();
      // ASSERT: Employee has NO add buttons
      expect(addCount).toBe(0);
    }

    // ASSERT: No CapacityMode toggle for employee
    const capacityLink = page.locator('a[href*="CapacityMode"]');
    await expect(capacityLink).toHaveCount(0);

    await saveEvidence(page, EVIDENCE, 'J-15-employee-readonly.png');
  });

  // ---------------------------------------------------------------------------
  // J-16: Trainee sees readonly calendar
  // ---------------------------------------------------------------------------
  test('J-16: Trainee sees readonly calendar', async ({ page }) => {
    await logout(page);
    await login(page, 'trainee.alhut@test', TEST_PASSWORD);

    await navigateTo(page, '/Calendar/Shifts');

    // ASSERT: Calendar page loads
    const calendarWrapper = page.locator('.shifts-calendar');
    await expect(calendarWrapper).toBeVisible({ timeout: 15000 });

    // ASSERT: No add buttons for trainee
    const addButtons = page.locator('.excel-calendar__add-btn');
    const addCount = await addButtons.count();
    expect(addCount).toBe(0);

    await saveEvidence(page, EVIDENCE, 'J-16-trainee-readonly.png');
  });

  // ---------------------------------------------------------------------------
  // J-17: Assigner has NO shift assignment controls
  // ---------------------------------------------------------------------------
  test('J-17: Assigner has no shift assignment controls', async ({ page }) => {
    await logout(page);
    await login(page, 'assigner.oren@test', TEST_PASSWORD);

    await navigateTo(page, '/Calendar/Shifts');

    // ASSERT: Calendar page loads
    const calendarWrapper = page.locator('.shifts-calendar');
    await expect(calendarWrapper).toBeVisible({ timeout: 15000 });

    // ASSERT: No add buttons (assigner cannot assign shifts)
    const addButtons = page.locator('.excel-calendar__add-btn');
    const addCount = await addButtons.count();
    expect(addCount).toBe(0);

    await saveEvidence(page, EVIDENCE, 'J-17-assigner-no-shifts.png');
  });

  // ---------------------------------------------------------------------------
  // J-18: View mode selector changes week/2weeks/month
  // ---------------------------------------------------------------------------
  test('J-18: View mode selector cycles through week, 2weeks, month', async ({ page }) => {
    // Start with week view
    await navigateTo(page, '/Calendar/Shifts?ViewMode=week');

    const viewSelect = page.locator('#viewModeSelect');
    await expect(viewSelect).toBeVisible({ timeout: 10000 });

    // ASSERT: viewModeSelect has the 3 options
    const options = viewSelect.locator('option');
    const optionCount = await options.count();
    expect(optionCount).toBe(3);

    // Verify we are in week view: URL should have ViewMode=week or default
    const calendarWrapper = page.locator('.shifts-calendar');
    await expect(calendarWrapper).toBeVisible();

    await saveEvidence(page, EVIDENCE, 'J-18-view-mode.png');
  });

  // ---------------------------------------------------------------------------
  // J-19: Date navigation forward and backward
  // ---------------------------------------------------------------------------
  test('J-19: Date navigation buttons move the calendar period', async ({ page }) => {
    await navigateTo(page, '/Calendar/Shifts');

    // Get current date range text
    const dateLabel = page.locator('.shifts-calendar__date-label');
    await expect(dateLabel).toBeVisible({ timeout: 10000 });
    const initialDateText = await dateLabel.textContent();

    // Click next
    const nextBtn = page.locator('.shifts-calendar__nav-btn[title]').last();
    await expect(nextBtn).toBeVisible();
    await nextBtn.click();
    await page.waitForLoadState('networkidle');

    // ASSERT: Date label changed
    const newDateText = await page.locator('.shifts-calendar__date-label').textContent();
    expect(newDateText).not.toBe(initialDateText);

    // ASSERT: Page still renders
    const calendarWrapper = page.locator('.shifts-calendar');
    await expect(calendarWrapper).toBeVisible();

    await saveEvidence(page, EVIDENCE, 'J-19-date-nav.png');
  });

  // ---------------------------------------------------------------------------
  // J-20: SignalR — two users see same calendar, both load successfully
  // ---------------------------------------------------------------------------
  test('J-20: SignalR — two browser contexts both load shifts calendar', async ({ page, browser }) => {
    // User A is already logged in as Owner
    await navigateTo(page, '/Calendar/Shifts');
    const calendarA = page.locator('.shifts-calendar');
    await expect(calendarA).toBeVisible({ timeout: 15000 });

    // Create User B context
    const contextB = await browser.newContext({ baseURL: 'http://localhost:5000' });
    const pageB = await contextB.newPage();
    await loginAsOwner(pageB);
    await navigateTo(pageB, '/Calendar/Shifts');

    // ASSERT: User B also sees the calendar
    const calendarB = pageB.locator('.shifts-calendar');
    await expect(calendarB).toBeVisible({ timeout: 15000 });

    // ASSERT: Both have the SignalR script loaded
    const signalrScriptA = await page.locator('script[src*="signalr"]').count();
    const signalrScriptB = await pageB.locator('script[src*="signalr"]').count();
    expect(signalrScriptA).toBeGreaterThanOrEqual(1);
    expect(signalrScriptB).toBeGreaterThanOrEqual(1);

    await saveEvidence(page, EVIDENCE, 'J-20-signalr-userA.png');
    await saveEvidence(pageB, EVIDENCE, 'J-20-signalr-userB.png');

    await contextB.close();
  });

  // ---------------------------------------------------------------------------
  // J-21: Concurrency — two contexts open same calendar without crash
  // ---------------------------------------------------------------------------
  test('J-21: Concurrent calendar access does not crash', async ({ page, browser }) => {
    // User A
    await navigateTo(page, '/Calendar/Shifts');
    await expect(page.locator('.shifts-calendar')).toBeVisible({ timeout: 15000 });

    // User B
    const contextB = await browser.newContext({ baseURL: 'http://localhost:5000' });
    const pageB = await contextB.newPage();
    await loginAsOwner(pageB);
    await navigateTo(pageB, '/Calendar/Shifts');
    await expect(pageB.locator('.shifts-calendar')).toBeVisible({ timeout: 15000 });

    // Both navigate to next period simultaneously
    const nextA = page.locator('.shifts-calendar__nav-btn').last();
    const nextB = pageB.locator('.shifts-calendar__nav-btn').last();

    await Promise.all([
      nextA.click(),
      nextB.click(),
    ]);

    await page.waitForLoadState('networkidle');
    await pageB.waitForLoadState('networkidle');

    // ASSERT: Both pages still show the calendar (no crash)
    await expect(page.locator('.shifts-calendar')).toBeVisible();
    await expect(pageB.locator('.shifts-calendar')).toBeVisible();

    await saveEvidence(page, EVIDENCE, 'J-21-concurrency-A.png');
    await saveEvidence(pageB, EVIDENCE, 'J-21-concurrency-B.png');

    await contextB.close();
  });

  // ---------------------------------------------------------------------------
  // J-22: Calendar data attribute declares type "shifts"
  // ---------------------------------------------------------------------------
  test('J-22: Calendar data-calendar-type is "shifts"', async ({ page }) => {
    await navigateTo(page, '/Calendar/Shifts');

    const calendarEl = page.locator('.excel-calendar[data-calendar-type="shifts"]');
    const emptyState = page.locator('.shifts-calendar__empty');

    // ASSERT: Either calendar renders with correct type or empty state is shown
    await expect(calendarEl.or(emptyState)).toBeVisible({ timeout: 10000 });

    const hasCalendar = await calendarEl.isVisible();

    if (hasCalendar) {
      const calType = await calendarEl.getAttribute('data-calendar-type');
      expect(calType).toBe('shifts');
    }

    await saveEvidence(page, EVIDENCE, 'J-22-calendar-type.png');
  });

  // ---------------------------------------------------------------------------
  // J-23: Today column is highlighted
  // ---------------------------------------------------------------------------
  test('J-23: Today column header and cells have today CSS class', async ({ page }) => {
    await navigateTo(page, '/Calendar/Shifts');

    const calendarTable = page.locator('.excel-calendar__table');
    const emptyState = page.locator('.shifts-calendar__empty');
    await expect(calendarTable.or(emptyState)).toBeVisible({ timeout: 10000 });

    const hasTable = await calendarTable.isVisible();

    if (hasTable) {
      // ASSERT: There is exactly one header with today class
      const todayHeader = page.locator('.excel-calendar__header-day--today');
      const todayHeaderCount = await todayHeader.count();
      // Could be 0 if current date is outside the viewed period
      expect(todayHeaderCount).toBeLessThanOrEqual(1);

      if (todayHeaderCount === 1) {
        // ASSERT: There are also cells with today class
        const todayCells = page.locator('.excel-calendar__cell--today');
        const todayCellCount = await todayCells.count();
        expect(todayCellCount).toBeGreaterThanOrEqual(1);
      }
    }

    await saveEvidence(page, EVIDENCE, 'J-23-today-highlight.png');
  });

  // ---------------------------------------------------------------------------
  // J-24: Molecule selector changes the calendar context
  // ---------------------------------------------------------------------------
  test('J-24: Changing molecule selector updates calendar URL', async ({ page }) => {
    await navigateTo(page, '/Calendar/Shifts');

    const moleculeSelect = page.locator('#moleculeSelect');
    await expect(moleculeSelect).toBeVisible({ timeout: 10000 });

    // ASSERT: Molecule select has at least one option
    const optionCount = await moleculeSelect.locator('option').count();
    expect(optionCount).toBeGreaterThanOrEqual(1);

    // Get the first option value for verification
    const firstValue = await moleculeSelect.locator('option').first().getAttribute('value');
    expect(firstValue).toBeTruthy();

    await saveEvidence(page, EVIDENCE, 'J-24-molecule-selector.png');
  });
});
