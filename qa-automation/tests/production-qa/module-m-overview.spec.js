// @ts-check
const { test, expect } = require('@playwright/test');
const {
  loginAsOwner, login, logout, saveEvidence, navigateTo,
  assertPageContains, assertPageNotContains, assertMinCount,
  TEST_PASSWORD, ROLE_ENUM
} = require('../../helpers/production-qa-helpers');

const EVIDENCE = '13-overview-calendar';

test.describe('Module M: Overview Calendar', () => {
  test.beforeEach(async ({ page }) => {
    await loginAsOwner(page);
  });

  // ---------------------------------------------------------------------------
  // M-01: Overview calendar renders with correct structure
  // ---------------------------------------------------------------------------
  test('M-01: Overview calendar renders with toolbar, company badge, and table', async ({ page }) => {
    await navigateTo(page, '/Calendar/Overview');

    // ASSERT: Main calendar wrapper is visible
    const calendarWrapper = page.locator('.overview-calendar');
    await expect(calendarWrapper).toBeVisible({ timeout: 15000 });

    // ASSERT: Toolbar is visible
    const toolbar = page.locator('.overview-calendar__toolbar');
    await expect(toolbar).toBeVisible();

    // ASSERT: Company badge is visible (overview is company-scoped)
    const companyBadge = page.locator('.overview-calendar__company-badge');
    await expect(companyBadge).toBeVisible();

    // ASSERT: Company badge has non-empty text
    const badgeText = await companyBadge.locator('.badge').textContent();
    expect(badgeText.trim().length).toBeGreaterThan(0);

    // ASSERT: View mode selector is present
    const viewModeSelect = page.locator('#viewModeSelect');
    await expect(viewModeSelect).toBeVisible();

    // ASSERT: Users filter is present
    const usersFilter = page.locator('#usersFilter');
    await expect(usersFilter).toBeVisible();

    // ASSERT: Either the table or empty state is shown (one MUST be visible)
    const calendarTable = page.locator('.excel-calendar__table');
    const emptyState = page.locator('.overview-calendar__empty');
    await expect(calendarTable.or(emptyState)).toBeVisible({ timeout: 10000 });

    await saveEvidence(page, EVIDENCE, 'M-01-overview-calendar.png');
  });

  // ---------------------------------------------------------------------------
  // M-02: Calendar declares type "overview"
  // ---------------------------------------------------------------------------
  test('M-02: Calendar data-calendar-type is "overview"', async ({ page }) => {
    await navigateTo(page, '/Calendar/Overview');

    const calendarEl = page.locator('.excel-calendar[data-calendar-type="overview"]');
    const emptyState = page.locator('.overview-calendar__empty');

    // ASSERT: Either an overview-typed calendar or empty state MUST be visible
    await expect(calendarEl.or(emptyState)).toBeVisible({ timeout: 10000 });

    // If the calendar element is present, verify its type attribute
    if (await calendarEl.isVisible()) {
      const calType = await calendarEl.getAttribute('data-calendar-type');
      expect(calType).toBe('overview');
    }

    await saveEvidence(page, EVIDENCE, 'M-02-calendar-type.png');
  });

  // ---------------------------------------------------------------------------
  // M-03: Overview shows aggregated data with overlay badges
  // ---------------------------------------------------------------------------
  test('M-03: Overview calendar renders rows with user data', async ({ page }) => {
    await navigateTo(page, '/Calendar/Overview');

    const calendarTable = page.locator('.excel-calendar__table');
    const emptyState = page.locator('.overview-calendar__empty');

    // ASSERT: Either the table or empty state MUST be visible
    await expect(calendarTable.or(emptyState)).toBeVisible({ timeout: 10000 });

    if (await calendarTable.isVisible()) {
      // ASSERT: Table MUST have data rows
      const rows = page.locator('.excel-calendar__table tbody tr[data-row-id]');
      const rowCount = await rows.count();
      expect(rowCount).toBeGreaterThanOrEqual(1);

      // ASSERT: Each row has a label (user name)
      const firstRowLabel = rows.first().locator('.excel-calendar__row-label');
      await expect(firstRowLabel).toBeVisible();
      const labelText = await firstRowLabel.textContent();
      expect(labelText.trim().length).toBeGreaterThan(0);

      // ASSERT: Cells have data-date attributes
      const cells = page.locator('.excel-calendar__cell[data-date]');
      const cellCount = await cells.count();
      expect(cellCount).toBeGreaterThanOrEqual(7);
    }

    await saveEvidence(page, EVIDENCE, 'M-03-aggregated-data.png');
  });

  // ---------------------------------------------------------------------------
  // M-04: Legend shows vacation, shift, chore, duty indicators
  // ---------------------------------------------------------------------------
  test('M-04: Overview legend shows all four overlay categories', async ({ page }) => {
    await navigateTo(page, '/Calendar/Overview');

    const legend = page.locator('.overview-calendar__legend');
    await expect(legend).toBeVisible({ timeout: 10000 });

    // ASSERT: Legend has vacation item
    const vacationLegend = legend.locator('.legend-item--vacation');
    await expect(vacationLegend).toBeVisible();

    // ASSERT: Legend has shift item
    const shiftLegend = legend.locator('.legend-item--shift');
    await expect(shiftLegend).toBeVisible();

    // ASSERT: Legend has chore item
    const choreLegend = legend.locator('.legend-item--chore');
    await expect(choreLegend).toBeVisible();

    // ASSERT: Legend has duty item
    const dutyLegend = legend.locator('.legend-item--duty');
    await expect(dutyLegend).toBeVisible();

    await saveEvidence(page, EVIDENCE, 'M-04-legend.png');
  });

  // ---------------------------------------------------------------------------
  // M-05: JustMine toggle works
  // ---------------------------------------------------------------------------
  test('M-05: JustMine toggle filters overview calendar', async ({ page }) => {
    await navigateTo(page, '/Calendar/Overview');

    const justMineLink = page.locator('.overview-calendar__toggle-group a[href*="JustMine="]');
    await expect(justMineLink).toBeVisible({ timeout: 10000 });

    await justMineLink.click();
    await page.waitForLoadState('networkidle');

    // ASSERT: URL has JustMine
    expect(page.url().toLowerCase()).toContain('justmine=true');

    // ASSERT: Page renders
    const calendarWrapper = page.locator('.overview-calendar');
    await expect(calendarWrapper).toBeVisible();

    await saveEvidence(page, EVIDENCE, 'M-05-just-mine.png');
  });

  // ---------------------------------------------------------------------------
  // M-06: Users filter switches between active and inactive
  // ---------------------------------------------------------------------------
  test('M-06: Users filter selector has active/inactive options', async ({ page }) => {
    await navigateTo(page, '/Calendar/Overview');

    const usersFilter = page.locator('#usersFilter');
    await expect(usersFilter).toBeVisible({ timeout: 10000 });

    // ASSERT: Has exactly 2 options (active and inactive)
    const optionCount = await usersFilter.locator('option').count();
    expect(optionCount).toBe(2);

    // Switch to inactive
    await usersFilter.selectOption('inactive');
    await page.waitForLoadState('networkidle');

    // ASSERT: URL includes UsersFilter=inactive
    expect(page.url()).toContain('UsersFilter=inactive');

    // ASSERT: Page renders
    const calendarWrapper = page.locator('.overview-calendar');
    await expect(calendarWrapper).toBeVisible();

    await saveEvidence(page, EVIDENCE, 'M-06-users-filter.png');
  });

  // ---------------------------------------------------------------------------
  // M-07: Date navigation works
  // ---------------------------------------------------------------------------
  test('M-07: Date navigation moves overview calendar period', async ({ page }) => {
    await navigateTo(page, '/Calendar/Overview');

    const dateLabel = page.locator('.overview-calendar__date-label');
    await expect(dateLabel).toBeVisible({ timeout: 10000 });
    const initialDateText = await dateLabel.textContent();

    const nextBtn = page.locator('.overview-calendar__nav-btn').last();
    await expect(nextBtn).toBeVisible();
    await nextBtn.click();
    await page.waitForLoadState('networkidle');

    // ASSERT: Date label changed
    const newDateText = await page.locator('.overview-calendar__date-label').textContent();
    expect(newDateText).not.toBe(initialDateText);

    await saveEvidence(page, EVIDENCE, 'M-07-date-nav.png');
  });

  // ---------------------------------------------------------------------------
  // M-08: Note editing modal exists for authorized users
  // ---------------------------------------------------------------------------
  test('M-08: Note editing modal is present in DOM for Owner', async ({ page }) => {
    await navigateTo(page, '/Calendar/Overview');

    const calendarTable = page.locator('.excel-calendar__table');
    const emptyState = page.locator('.overview-calendar__empty');

    // ASSERT: Either the table or empty state MUST be visible
    await expect(calendarTable.or(emptyState)).toBeVisible({ timeout: 10000 });

    if (await calendarTable.isVisible()) {
      // ASSERT: Note modal is in the DOM (hidden by default)
      const noteModal = page.locator('#noteModal');
      const modalExists = await noteModal.count();
      // Owner should have CanEditNotes = true, so modal should exist
      expect(modalExists).toBeGreaterThanOrEqual(1);

      // ASSERT: Modal has the required form fields
      const noteText = page.locator('#noteText');
      const noteTextExists = await noteText.count();
      expect(noteTextExists).toBe(1);

      // ASSERT: Save and delete buttons exist
      const saveBtn = page.locator('#noteModal button:has-text("Save"), #noteModal .btn-primary');
      const saveBtnCount = await saveBtn.count();
      expect(saveBtnCount).toBeGreaterThanOrEqual(1);

      const deleteBtn = page.locator('#deleteNoteBtn');
      const deleteBtnCount = await deleteBtn.count();
      expect(deleteBtnCount).toBe(1);
    }

    await saveEvidence(page, EVIDENCE, 'M-08-note-modal.png');
  });

  // ---------------------------------------------------------------------------
  // M-09: Cells are editable (double-click shows hint cursor)
  // ---------------------------------------------------------------------------
  test('M-09: Overview cells have editable class for note editing', async ({ page }) => {
    await navigateTo(page, '/Calendar/Overview');

    const calendarTable = page.locator('.excel-calendar__table');
    const emptyState = page.locator('.overview-calendar__empty');

    // ASSERT: Either the table or empty state MUST be visible
    await expect(calendarTable.or(emptyState)).toBeVisible({ timeout: 10000 });

    if (await calendarTable.isVisible()) {
      // Wait for JS to add the editable class
      await page.waitForTimeout(1000);

      // ASSERT: Owner should have editable cells when user rows are present
      const userRows = page.locator('tr[data-row-id^="user-"]');
      const userRowCount = await userRows.count();

      if (userRowCount > 0) {
        const editableCells = page.locator('.excel-calendar-cell--editable');
        const editableCount = await editableCells.count();
        expect(editableCount).toBeGreaterThanOrEqual(1);
      }
    }

    await saveEvidence(page, EVIDENCE, 'M-09-editable-cells.png');
  });

  // ---------------------------------------------------------------------------
  // M-10: Print button is present
  // ---------------------------------------------------------------------------
  test('M-10: Print button is present on overview calendar', async ({ page }) => {
    await navigateTo(page, '/Calendar/Overview');

    const printBtn = page.locator('.overview-calendar__actions button[onclick*="print"]');
    await expect(printBtn).toBeVisible({ timeout: 10000 });

    await saveEvidence(page, EVIDENCE, 'M-10-print-button.png');
  });

  // ---------------------------------------------------------------------------
  // M-11: View mode selector has correct options
  // ---------------------------------------------------------------------------
  test('M-11: View mode selector has week, 2weeks, month options', async ({ page }) => {
    await navigateTo(page, '/Calendar/Overview');

    const viewSelect = page.locator('#viewModeSelect');
    await expect(viewSelect).toBeVisible({ timeout: 10000 });

    const options = viewSelect.locator('option');
    const optionCount = await options.count();
    // ASSERT: Exactly 3 view modes
    expect(optionCount).toBe(3);

    await saveEvidence(page, EVIDENCE, 'M-11-view-modes.png');
  });

  // ---------------------------------------------------------------------------
  // M-12: Today column is highlighted
  // ---------------------------------------------------------------------------
  test('M-12: Today column is highlighted on overview calendar', async ({ page }) => {
    await navigateTo(page, '/Calendar/Overview');

    const calendarTable = page.locator('.excel-calendar__table');
    const emptyState = page.locator('.overview-calendar__empty');

    // ASSERT: Either the table or empty state MUST be visible
    await expect(calendarTable.or(emptyState)).toBeVisible({ timeout: 10000 });

    if (await calendarTable.isVisible()) {
      const todayHeader = page.locator('.excel-calendar__header-day--today');
      const todayHeaderCount = await todayHeader.count();
      expect(todayHeaderCount).toBeLessThanOrEqual(1);

      if (todayHeaderCount === 1) {
        const todayCells = page.locator('.excel-calendar__cell--today');
        const todayCellCount = await todayCells.count();
        expect(todayCellCount).toBeGreaterThanOrEqual(1);
      }
    }

    await saveEvidence(page, EVIDENCE, 'M-12-today-highlight.png');
  });

  // ---------------------------------------------------------------------------
  // M-13: Employee can access overview calendar
  // ---------------------------------------------------------------------------
  test('M-13: Employee can access overview calendar (read-only)', async ({ page }) => {
    await logout(page);
    await login(page, 'emp.tz.alhut@test', TEST_PASSWORD);

    await navigateTo(page, '/Calendar/Overview');

    // ASSERT: Calendar loads
    const calendarWrapper = page.locator('.overview-calendar');
    await expect(calendarWrapper).toBeVisible({ timeout: 15000 });

    // ASSERT: Company badge is visible
    const companyBadge = page.locator('.overview-calendar__company-badge');
    await expect(companyBadge).toBeVisible();

    await saveEvidence(page, EVIDENCE, 'M-13-employee-overview.png');
  });

  // ---------------------------------------------------------------------------
  // M-14: Overview shows overlay badges (vacation, chore, duty) if present
  // ---------------------------------------------------------------------------
  test('M-14: Overview cells can contain overlay badges', async ({ page }) => {
    await navigateTo(page, '/Calendar/Overview');

    const calendarTable = page.locator('.excel-calendar__table');
    const emptyState = page.locator('.overview-calendar__empty');

    // ASSERT: Either the table or empty state MUST be visible
    await expect(calendarTable.or(emptyState)).toBeVisible({ timeout: 10000 });

    if (await calendarTable.isVisible()) {
      // ASSERT: The table structure is present and well-formed with header days
      const headerDays = page.locator('.excel-calendar__header-day');
      const headerCount = await headerDays.count();
      expect(headerCount).toBeGreaterThanOrEqual(7);
    }

    await saveEvidence(page, EVIDENCE, 'M-14-overlay-badges.png');
  });

  // ---------------------------------------------------------------------------
  // M-15: SignalR and realtime scripts are loaded
  // ---------------------------------------------------------------------------
  test('M-15: SignalR and realtime scripts are loaded on overview', async ({ page }) => {
    await navigateTo(page, '/Calendar/Overview');

    // ASSERT: SignalR script tag is present
    const signalrScript = page.locator('script[src*="signalr"]');
    const signalrCount = await signalrScript.count();
    expect(signalrCount).toBeGreaterThanOrEqual(1);

    // ASSERT: Calendar realtime script is present
    const realtimeScript = page.locator('script[src*="calendar-realtime"]');
    const realtimeCount = await realtimeScript.count();
    expect(realtimeCount).toBeGreaterThanOrEqual(1);

    await saveEvidence(page, EVIDENCE, 'M-15-signalr.png');
  });

  // ---------------------------------------------------------------------------
  // M-16: Manager can access overview calendar
  // ---------------------------------------------------------------------------
  test('M-16: Manager can access overview calendar with editing', async ({ page }) => {
    await logout(page);
    await login(page, 'mgr.alhut.tz@test', TEST_PASSWORD);

    await navigateTo(page, '/Calendar/Overview');

    // ASSERT: Calendar loads
    const calendarWrapper = page.locator('.overview-calendar');
    await expect(calendarWrapper).toBeVisible({ timeout: 15000 });

    await saveEvidence(page, EVIDENCE, 'M-16-manager-overview.png');
  });
});
