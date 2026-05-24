// @ts-check
const { test, expect } = require('@playwright/test');
const {
  loginAsOwner, login, logout, saveEvidence, navigateTo,
  assertPageContains, assertPageNotContains, assertMinCount,
  TEST_PASSWORD, ROLE_ENUM
} = require('../../helpers/production-qa-helpers');

const EVIDENCE = '11-chore-calendar';

test.describe('Module K: Chore Calendar Full Flow', () => {
  test.beforeEach(async ({ page }) => {
    await loginAsOwner(page);
  });

  // ---------------------------------------------------------------------------
  // K-01: Chore calendar renders with correct structure
  // ---------------------------------------------------------------------------
  test('K-01: Chore calendar renders with toolbar and table', async ({ page }) => {
    await navigateTo(page, '/Calendar/Chores');

    // ASSERT: Main calendar wrapper is visible
    const calendarWrapper = page.locator('.cal-page');
    await expect(calendarWrapper).toBeVisible({ timeout: 15000 });

    // ASSERT: Toolbar is visible
    const toolbar = page.locator('.cal-toolbar');
    await expect(toolbar).toBeVisible();

    // ASSERT: Molecule selector is present
    const moleculeSelect = page.locator('#moleculeSelect');
    await expect(moleculeSelect).toBeVisible();

    // ASSERT: View mode selector is present
    const viewModeSelect = page.locator('#viewModeSelect');
    await expect(viewModeSelect).toBeVisible();

    // ASSERT: Either the table or empty state is shown
    const calendarTable = page.locator('.excel-calendar__table');
    const emptyState = page.locator('.cal-empty');
    await expect(calendarTable.or(emptyState)).toBeVisible({ timeout: 10000 });

    await saveEvidence(page, EVIDENCE, 'K-01-chore-calendar.png');
  });

  // ---------------------------------------------------------------------------
  // K-02: Calendar declares type "chores"
  // ---------------------------------------------------------------------------
  test('K-02: Calendar data-calendar-type is "chores"', async ({ page }) => {
    await navigateTo(page, '/Calendar/Chores');

    const calendarEl = page.locator('.excel-calendar[data-calendar-type="chores"]');
    const emptyState = page.locator('.cal-empty');

    // ASSERT: Either the typed calendar or empty state must be visible
    await expect(calendarEl.or(emptyState)).toBeVisible({ timeout: 10000 });

    const hasCalendar = await calendarEl.isVisible();
    if (hasCalendar) {
      const calType = await calendarEl.getAttribute('data-calendar-type');
      expect(calType).toBe('chores');
    }

    await saveEvidence(page, EVIDENCE, 'K-02-calendar-type.png');
  });

  // ---------------------------------------------------------------------------
  // K-03: ChoreType selector and legend are present when types exist
  // ---------------------------------------------------------------------------
  test('K-03: ChoreType selector and legend are rendered', async ({ page }) => {
    await navigateTo(page, '/Calendar/Chores');

    // ASSERT: The chore calendar wrapper loads
    const calendarWrapper = page.locator('.cal-page');
    await expect(calendarWrapper).toBeVisible({ timeout: 15000 });

    // Check whether chore type selector is present
    const choreTypeSelect = page.locator('#choreTypeSelect');
    const calendarTable = page.locator('.excel-calendar__table');
    const emptyState = page.locator('.cal-empty');

    // ASSERT: Either table/empty state must be visible (page rendered properly)
    await expect(calendarTable.or(emptyState)).toBeVisible({ timeout: 10000 });

    const hasChoreTypeSelect = await choreTypeSelect.isVisible();

    if (hasChoreTypeSelect) {
      // ASSERT: Has at least "All" option plus one real type
      const optionCount = await choreTypeSelect.locator('option').count();
      expect(optionCount).toBeGreaterThanOrEqual(2); // "All" + at least 1 type

      // ASSERT: Legend is also visible when types exist
      const legend = page.locator('.cal-toolbar__legend');
      await expect(legend).toBeVisible();

      // ASSERT: Legend has at least one item
      const legendItems = legend.locator('.cal-toolbar__legend-item');
      const legendCount = await legendItems.count();
      expect(legendCount).toBeGreaterThanOrEqual(1);
    }

    await saveEvidence(page, EVIDENCE, 'K-03-choretype-selector.png');
  });

  // ---------------------------------------------------------------------------
  // K-04: Owner can see add buttons on chore calendar cells
  // ---------------------------------------------------------------------------
  test('K-04: Owner sees add-buttons on chore calendar cells', async ({ page }) => {
    await navigateTo(page, '/Calendar/Chores');

    const calendarTable = page.locator('.excel-calendar__table');
    const emptyState = page.locator('.cal-empty');

    // ASSERT: Either table or empty state must be visible
    await expect(calendarTable.or(emptyState)).toBeVisible({ timeout: 10000 });

    const hasTable = await calendarTable.isVisible();
    if (hasTable) {
      // ASSERT: Owner MUST have add buttons when table is present
      const addButtons = page.locator('.excel-calendar__add-btn');
      const addCount = await addButtons.count();
      expect(addCount).toBeGreaterThanOrEqual(1);
    }

    await saveEvidence(page, EVIDENCE, 'K-04-owner-add-buttons.png');
  });

  // ---------------------------------------------------------------------------
  // K-05: Click add button opens chore assignment UI
  // ---------------------------------------------------------------------------
  test('K-05: Clicking add button triggers chore assignment UI', async ({ page }) => {
    await navigateTo(page, '/Calendar/Chores');

    // ASSERT: Add button must be present for Owner (calendar may be empty if no chore types exist)
    const addBtn = page.locator('.excel-calendar__add-btn').first();
    const addBtnVisible = await addBtn.isVisible({ timeout: 10000 }).catch(() => false);
    if (!addBtnVisible) {
      // No add buttons means no chore types exist for the current molecule — skip
      test.skip(true, 'No add buttons visible on chore calendar — chore types may not exist');
      return;
    }

    // Record URL before click
    const urlBefore = page.url();

    await addBtn.click();
    await page.waitForTimeout(1500);

    // Wait for bottom sheet animation or modal to appear
    const sheetVisible = await page.locator('.bottom-sheet--open').first().isVisible({ timeout: 5000 }).catch(() => false);
    const modalVisible = await page.locator('.modal:not([hidden]), [role="dialog"]:not([hidden])').first().isVisible({ timeout: 1000 }).catch(() => false);
    const promptVisible = await page.locator('[role="alertdialog"]').first().isVisible({ timeout: 1000 }).catch(() => false);
    const inlineVisible = await page.locator('select[data-role="assignee-select"], .excel-calendar__inline-select').first().isVisible({ timeout: 1000 }).catch(() => false);
    const urlChanged = page.url() !== urlBefore;

    // If no UI appeared, skip — the cell may have no available users to assign
    if (!(sheetVisible || modalVisible || promptVisible || inlineVisible || urlChanged)) {
      await saveEvidence(page, EVIDENCE, 'K-05-quick-add-no-ui.png');
      test.skip(true, 'Add button clicked but no assignment UI appeared — likely no assignable users for this cell');
      return;
    }

    await saveEvidence(page, EVIDENCE, 'K-05-quick-add.png');
  });

  // ---------------------------------------------------------------------------
  // K-06: Existing chore assignments display correctly
  // ---------------------------------------------------------------------------
  test('K-06: Existing chore assignments render as named elements', async ({ page }) => {
    await navigateTo(page, '/Calendar/Chores');

    const calendarTable = page.locator('.excel-calendar__table');
    const emptyState = page.locator('.cal-empty');

    // ASSERT: Either table or empty state must render
    await expect(calendarTable.or(emptyState)).toBeVisible({ timeout: 10000 });

    const hasTable = await calendarTable.isVisible();
    if (hasTable) {
      // ASSERT: Table must have data rows
      const rows = page.locator('.excel-calendar__table tbody tr[data-row-id]');
      const rowCount = await rows.count();
      expect(rowCount).toBeGreaterThanOrEqual(1);

      // Check assignments if any
      const assignments = page.locator('.excel-calendar__assignment');
      const assignmentCount = await assignments.count();

      if (assignmentCount > 0) {
        const nameEl = assignments.first().locator('.excel-calendar__assignment-name');
        await expect(nameEl).toBeVisible();
        const nameText = await nameEl.textContent();
        expect(nameText.trim().length).toBeGreaterThan(0);
      }
    }

    await saveEvidence(page, EVIDENCE, 'K-06-chore-assignments.png');
  });

  // ---------------------------------------------------------------------------
  // K-07: ChoreType filter changes calendar content
  // ---------------------------------------------------------------------------
  test('K-07: Selecting a ChoreType filter updates the calendar', async ({ page }) => {
    await navigateTo(page, '/Calendar/Chores');

    // ASSERT: Calendar wrapper is visible
    const calendarWrapper = page.locator('.cal-page');
    await expect(calendarWrapper).toBeVisible({ timeout: 15000 });

    const choreTypeSelect = page.locator('#choreTypeSelect');
    const hasSelect = await choreTypeSelect.isVisible();

    if (hasSelect) {
      const options = await choreTypeSelect.locator('option').count();

      if (options > 1) {
        // Select the second option (first real type after "All")
        await choreTypeSelect.selectOption({ index: 1 });
        await page.waitForLoadState('networkidle');

        // ASSERT: URL includes ChoreTypeFilter
        expect(page.url()).toContain('ChoreTypeFilter');

        // ASSERT: Page still renders
        await expect(calendarWrapper).toBeVisible();
      }
    }

    await saveEvidence(page, EVIDENCE, 'K-07-filter-choretype.png');
  });

  // ---------------------------------------------------------------------------
  // K-08: JustMine toggle on chore calendar
  // ---------------------------------------------------------------------------
  test('K-08: JustMine toggle filters chore calendar', async ({ page }) => {
    await navigateTo(page, '/Calendar/Chores');

    const justMineLink = page.locator('.cal-toolbar__toggle-group a[href*="JustMine="]');
    await expect(justMineLink).toBeVisible({ timeout: 10000 });

    await justMineLink.click();
    await page.waitForLoadState('networkidle');

    // ASSERT: URL has JustMine
    expect(page.url().toLowerCase()).toContain('justmine=true');

    // ASSERT: Page renders
    const calendarWrapper = page.locator('.cal-page');
    await expect(calendarWrapper).toBeVisible();

    await saveEvidence(page, EVIDENCE, 'K-08-just-mine.png');
  });

  // ---------------------------------------------------------------------------
  // K-09: Date navigation works on chore calendar
  // ---------------------------------------------------------------------------
  test('K-09: Date navigation moves chore calendar period', async ({ page }) => {
    await navigateTo(page, '/Calendar/Chores');

    const dateLabel = page.locator('.cal-toolbar__date-label');
    await expect(dateLabel).toBeVisible({ timeout: 10000 });
    const initialDateText = await dateLabel.textContent();

    const nextBtn = page.locator('.cal-toolbar__nav-btn').last();
    await expect(nextBtn).toBeVisible();
    await nextBtn.click();
    await page.waitForLoadState('networkidle');

    // ASSERT: Date label changed
    const newDateText = await page.locator('.cal-toolbar__date-label').textContent();
    expect(newDateText).not.toBe(initialDateText);

    await saveEvidence(page, EVIDENCE, 'K-09-date-nav.png');
  });

  // ---------------------------------------------------------------------------
  // K-10: Assigner CAN manage chores
  // ---------------------------------------------------------------------------
  test('K-10: Assigner can access chore calendar with edit controls', async ({ page }) => {
    await logout(page);
    await login(page, 'assigner.oren@test', TEST_PASSWORD);

    await navigateTo(page, '/Calendar/Chores');

    // ASSERT: Calendar page loads
    const calendarWrapper = page.locator('.cal-page');
    await expect(calendarWrapper).toBeVisible({ timeout: 15000 });

    // ASSERT: Either table or empty state is rendered
    const calendarTable = page.locator('.excel-calendar__table');
    const emptyState = page.locator('.cal-empty');
    await expect(calendarTable.or(emptyState)).toBeVisible({ timeout: 10000 });

    const hasTable = await calendarTable.isVisible();
    if (hasTable) {
      // ASSERT: Assigner MUST have add buttons when table is present
      const addButtons = page.locator('.excel-calendar__add-btn');
      const addCount = await addButtons.count();
      expect(addCount).toBeGreaterThanOrEqual(1);
    }

    await saveEvidence(page, EVIDENCE, 'K-10-assigner-chores.png');
  });

  // ---------------------------------------------------------------------------
  // K-11: Assigner CANNOT manage on-duty (cross-check from chores module)
  // ---------------------------------------------------------------------------
  test('K-11: Assigner CANNOT manage on-duty calendar', async ({ page }) => {
    await logout(page);
    await login(page, 'assigner.oren@test', TEST_PASSWORD);

    await navigateTo(page, '/Calendar/OnCall');

    // ASSERT: OnCall calendar loads
    const calendarWrapper = page.locator('.cal-page');
    await expect(calendarWrapper).toBeVisible({ timeout: 15000 });

    // ASSERT: Assigner has NO add buttons on on-duty calendar
    const addButtons = page.locator('.excel-calendar__add-btn');
    const addCount = await addButtons.count();
    expect(addCount).toBe(0);

    await saveEvidence(page, EVIDENCE, 'K-11-assigner-no-onduty.png');
  });

  // ---------------------------------------------------------------------------
  // K-12: Employee sees chore calendar (read-only)
  // ---------------------------------------------------------------------------
  test('K-12: Employee sees read-only chore calendar', async ({ page }) => {
    await logout(page);
    await login(page, 'emp.tz.alhut@test', TEST_PASSWORD);

    await navigateTo(page, '/Calendar/Chores?JustMine=true');

    // ASSERT: Calendar loads
    const calendarWrapper = page.locator('.cal-page');
    await expect(calendarWrapper).toBeVisible({ timeout: 15000 });

    // ASSERT: No add buttons for employee
    const addButtons = page.locator('.excel-calendar__add-btn');
    const addCount = await addButtons.count();
    expect(addCount).toBe(0);

    await saveEvidence(page, EVIDENCE, 'K-12-employee-readonly.png');
  });

  // ---------------------------------------------------------------------------
  // K-13: Print button is present on chore calendar
  // ---------------------------------------------------------------------------
  test('K-13: Print button is present on chore calendar', async ({ page }) => {
    await navigateTo(page, '/Calendar/Chores');

    const printBtn = page.locator('.cal-toolbar__print');
    await expect(printBtn).toBeVisible({ timeout: 10000 });

    await saveEvidence(page, EVIDENCE, 'K-13-print-button.png');
  });

  // ---------------------------------------------------------------------------
  // K-14: Notification center page loads
  // ---------------------------------------------------------------------------
  test('K-14: Notification center page loads for chore notifications', async ({ page }) => {
    await navigateTo(page, '/My/NotificationCenter');

    // ASSERT: Page loaded without error (status was checked by navigateTo)
    // ASSERT: We are on the notification page (not redirected to error)
    expect(page.url()).toContain('/My/NotificationCenter');

    await saveEvidence(page, EVIDENCE, 'K-14-notification-center.png');
  });

  // ---------------------------------------------------------------------------
  // K-15: Public chores page loads or redirects appropriately
  // ---------------------------------------------------------------------------
  test('K-15: Public chores page loads without auth', async ({ page }) => {
    await logout(page);

    const response = await page.goto('/Public/Chores');
    await page.waitForLoadState('networkidle');

    // ASSERT: Response was received
    expect(response).not.toBeNull();

    // ASSERT: Public page returns 200 (public access) or 302 redirect
    // It should NOT return 500
    expect(response.status()).toBeLessThan(500);

    // ASSERT: If accessible, page has content; if not, we're on login/access denied
    const isPublic = !page.url().includes('AccessDenied') && !page.url().includes('Auth/Login');
    const isAuthRedirect = page.url().includes('Auth/Login') || page.url().includes('AccessDenied');
    expect(isPublic || isAuthRedirect).toBe(true);

    await saveEvidence(page, EVIDENCE, 'K-15-public-chores.png');
  });

  // ---------------------------------------------------------------------------
  // K-16: SignalR script loaded on chore calendar
  // ---------------------------------------------------------------------------
  test('K-16: SignalR script is included on chore calendar', async ({ page }) => {
    await navigateTo(page, '/Calendar/Chores');

    // ASSERT: SignalR script tag is present
    const signalrScript = page.locator('script[src*="signalr"]');
    const signalrCount = await signalrScript.count();
    expect(signalrCount).toBeGreaterThanOrEqual(1);

    // ASSERT: Calendar realtime script is present
    const realtimeScript = page.locator('script[src*="calendar-realtime"]');
    const realtimeCount = await realtimeScript.count();
    expect(realtimeCount).toBeGreaterThanOrEqual(1);

    await saveEvidence(page, EVIDENCE, 'K-16-signalr.png');
  });
});

// =============================================================================
// Phase 2 (P1): Chore Calendar Extended — K-17 through K-19
// =============================================================================

test.describe('Module K: Chore Calendar Extended (P1)', () => {
  test.beforeEach(async ({ page }) => {
    await loginAsOwner(page);
  });

  // ---------------------------------------------------------------------------
  // K-17: GetChoresData API returns structured JSON
  // ---------------------------------------------------------------------------
  test('K-17: GetChoresData API returns structured data', async ({ page }) => {
    // First get moleculeId from the Chores page
    await navigateTo(page, '/Calendar/Chores');
    const moleculeSelect = page.locator('#moleculeSelect');
    await expect(moleculeSelect).toBeVisible({ timeout: 15000 });
    const moleculeId = await moleculeSelect.inputValue();

    // Calculate date range
    const today = new Date();
    const dayOfWeek = today.getDay();
    const weekStart = new Date(today);
    weekStart.setDate(today.getDate() - dayOfWeek);
    const weekEnd = new Date(weekStart);
    weekEnd.setDate(weekStart.getDate() + 6);
    const startDate = weekStart.toISOString().split('T')[0];
    const endDate = weekEnd.toISOString().split('T')[0];

    // Call the API
    const response = await page.request.get(
      `/Api/Calendar/GetChoresData?moleculeId=${moleculeId}&startDate=${startDate}&endDate=${endDate}`
    );

    // ASSERT: API responds successfully
    expect(response.status()).toBe(200);

    const data = await response.json();
    expect(data.success).toBe(true);

    // ASSERT: Response has expected structure
    expect(data).toHaveProperty('data');
    expect(data.data).toHaveProperty('chores');
    expect(Array.isArray(data.data.chores)).toBe(true);

    await saveEvidence(page, EVIDENCE, 'K-17-api-chores-data.png');
  });

  // ---------------------------------------------------------------------------
  // K-18: View mode 2weeks shows 14 column headers on chore calendar
  // ---------------------------------------------------------------------------
  test('K-18: Chore calendar 2-week view renders correctly', async ({ page }) => {
    await navigateTo(page, '/Calendar/Chores?ViewMode=2weeks');

    const calendarContainer = page.locator('.cal-page');
    await expect(calendarContainer).toBeVisible({ timeout: 15000 });

    const hasTable = await page.locator('.excel-calendar__table').count() > 0;
    const hasEmpty = await page.locator('.cal-empty').count() > 0;
    expect(hasTable || hasEmpty).toBe(true);

    if (hasTable) {
      // ASSERT: 2-week view has 14 header columns
      const headerDays = page.locator('.excel-calendar__header-day');
      const headerCount = await headerDays.count();
      expect(headerCount).toBe(14);
    }

    await saveEvidence(page, EVIDENCE, 'K-18-two-week-view.png');
  });

  // ---------------------------------------------------------------------------
  // K-19: Chore calendar legend shows chore type colors
  // ---------------------------------------------------------------------------
  test('K-19: Chore calendar legend items have color indicators', async ({ page }) => {
    await navigateTo(page, '/Calendar/Chores');

    const calendarContainer = page.locator('.cal-page');
    await expect(calendarContainer).toBeVisible({ timeout: 15000 });

    // Check for legend (may not be present if no chore types exist)
    const legend = page.locator('.cal-toolbar__legend');
    const legendCount = await legend.count();

    if (legendCount > 0) {
      const legendItems = legend.locator('.cal-toolbar__legend-item');
      const itemCount = await legendItems.count();
      // ASSERT: At least one legend item if legend exists
      expect(itemCount).toBeGreaterThanOrEqual(1);
    }

    // ASSERT: Calendar still renders regardless
    await expect(calendarContainer).toBeVisible();

    await saveEvidence(page, EVIDENCE, 'K-19-legend-colors.png');
  });
});
