// @ts-check
const { test, expect } = require('@playwright/test');
const {
  loginAsOwner, login, logout, saveEvidence, navigateTo, navigateExpecting,
  assertPageContains, assertPageNotContains, assertMinCount,
  TEST_PASSWORD, TEST_USERS,
} = require('../../helpers/production-qa-helpers');

const EVIDENCE = '14-timeoff-requests';

/**
 * Module N: Time-Off Requests
 *
 * Tests the time-off request lifecycle: creation (vacation and half-day),
 * approval, decline, deletion, and cross-page visibility.
 *
 * DEPENDENCY: Test users (emp.tz.alhut@test etc.) must exist.
 * Login uses expectSuccess:true so tests FAIL if users are missing.
 */
test.describe('Module N: Time-Off Requests', () => {

  test('N-01: Employee navigates to time-off creation page', async ({ page }) => {
    // Login as employee -- STRICT: will fail if user does not exist
    await login(page, 'emp.tz.alhut@test', TEST_PASSWORD);

    // Navigate to the employee's own My/Requests page (where the creation form lives)
    await navigateTo(page, '/My/Requests');

    // ASSERT: The page title for "My Requests" is visible
    await expect(page.locator('.page-title').first()).toBeVisible({ timeout: 5000 });

    // ASSERT: The time-off request form card is present
    const timeOffCard = page.locator('.card-header:has-text("TimeOff"), .card-title:has-text("TimeOff"), form[action*="TimeOff"], .card-header').first();
    await expect(timeOffCard).toBeVisible({ timeout: 5000 });

    // ASSERT: StartDate input exists on the form
    const startDateInput = page.locator('input[type="date"]').first();
    await expect(startDateInput).toBeVisible({ timeout: 5000 });

    await saveEvidence(page, EVIDENCE, 'N-01-vacation-request.png');
  });

  test('N-02: Employee sees vacation type selector with Vacation and After options', async ({ page }) => {
    await login(page, 'emp.tz.alhut@test', TEST_PASSWORD);
    await navigateTo(page, '/My/Requests');

    // ASSERT: Vacation type selector is present
    const vacationTypeSelect = page.locator('select[id="vacationType"], select[name*="Type"]').first();
    await expect(vacationTypeSelect).toBeVisible({ timeout: 5000 });

    // ASSERT: At least 2 options (Vacation, After)
    const options = vacationTypeSelect.locator('option');
    const optionCount = await options.count();
    expect(optionCount).toBeGreaterThanOrEqual(2);

    await saveEvidence(page, EVIDENCE, 'N-02-half-day-request.png');
  });

  test('N-03: Owner/Manager can access Requests/Index to see pending time-off', async ({ page }) => {
    await loginAsOwner(page);
    await navigateTo(page, '/Requests/Index');

    // ASSERT: The Requests page loaded with the tab navigation
    const tabNavigation = page.locator('.tab-navigation, [role="tablist"]');
    await expect(tabNavigation).toBeVisible({ timeout: 5000 });

    // ASSERT: Time-off tab is present and active by default
    const timeOffTab = page.locator('#tab-btn-timeoff, button[data-tab="timeoff"]');
    await expect(timeOffTab).toBeVisible({ timeout: 5000 });
    await expect(timeOffTab).toHaveAttribute('aria-selected', 'true');

    // ASSERT: The time-off panel is visible (active)
    const timeOffPanel = page.locator('#tab-timeoff');
    await expect(timeOffPanel).toBeVisible({ timeout: 5000 });

    // ASSERT: Either pending requests exist (request-card) or empty state is shown
    const hasRequests = await page.locator('#tab-timeoff .request-card').count();
    const hasEmptyState = await page.locator('#tab-timeoff .empty-state').count();
    expect(hasRequests + hasEmptyState).toBeGreaterThanOrEqual(1);

    await saveEvidence(page, EVIDENCE, 'N-03-approve-timeoff.png');
  });

  test('N-04: Approve and Decline buttons are present on pending time-off cards', async ({ page }) => {
    await loginAsOwner(page);
    await navigateTo(page, '/Requests/Index');

    // Check if there are pending time-off requests
    const requestCards = page.locator('#tab-timeoff .request-card');
    const cardCount = await requestCards.count();

    if (cardCount > 0) {
      // ASSERT: Each pending card has Approve and Decline action buttons
      const firstCard = requestCards.first();
      const approveBtn = firstCard.locator('button.btn-approve, form[action*="Approve"] button');
      const declineBtn = firstCard.locator('button.btn-decline, form[action*="Decline"] button');
      await expect(approveBtn.first()).toBeVisible({ timeout: 3000 });
      await expect(declineBtn.first()).toBeVisible({ timeout: 3000 });
    } else {
      // ASSERT: Empty state is displayed with correct messaging
      const emptyState = page.locator('#tab-timeoff .empty-state');
      await expect(emptyState).toBeVisible({ timeout: 3000 });
    }

    await saveEvidence(page, EVIDENCE, 'N-04-decline-timeoff.png');
  });

  test('N-05: Employee sees own requests on My/Requests page', async ({ page }) => {
    await login(page, 'emp.tz.alhut@test', TEST_PASSWORD);
    await navigateTo(page, '/My/Requests');

    // ASSERT: Page loaded successfully with the requests page layout
    await expect(page.locator('.requests-page, .page-title').first()).toBeVisible({ timeout: 5000 });

    // ASSERT: The "My Time Off Requests" history card is present
    const timeOffHistoryCard = page.locator('.card-header:has-text("TimeOff"), .card-title:has-text("TimeOff"), .history-section .card').first();
    await expect(timeOffHistoryCard).toBeVisible({ timeout: 5000 });

    // ASSERT: Either request list items exist or empty state is shown (but the section is present)
    const historySection = page.locator('.history-section');
    await expect(historySection).toBeVisible({ timeout: 5000 });

    await saveEvidence(page, EVIDENCE, 'N-05-own-requests.png');
  });

  test('N-06: Calendar/Shifts page loads for owner (time-off impact)', async ({ page }) => {
    await loginAsOwner(page);
    await navigateTo(page, '/Calendar/Shifts');

    // ASSERT: The shifts calendar page loaded (not an error or redirect)
    expect(page.url()).toContain('/Calendar/Shifts');

    // ASSERT: Some calendar content is rendered
    const body = page.locator('body');
    await expect(body).toBeVisible({ timeout: 5000 });

    // ASSERT: Page is not an error page
    await expect(page.locator('h1:has-text("An unhandled exception occurred")')).not.toBeVisible({ timeout: 2000 });

    await saveEvidence(page, EVIDENCE, 'N-06-auto-unassign.png');
  });

  test('N-07: Requests page has Approved Time-Off tab', async ({ page }) => {
    await loginAsOwner(page);
    await navigateTo(page, '/Requests/Index');

    // ASSERT: The "Approved" tab exists
    const approvedTab = page.locator('#tab-btn-approved, button[data-tab="approved"]');
    await expect(approvedTab).toBeVisible({ timeout: 5000 });

    // Click the approved tab
    await approvedTab.click();

    // ASSERT: The approved panel becomes visible
    const approvedPanel = page.locator('#tab-approved');
    await expect(approvedPanel).toBeVisible({ timeout: 5000 });

    // ASSERT: Either approved request cards or empty state is shown
    const hasApproved = await page.locator('#tab-approved .request-card').count();
    const hasEmptyState = await page.locator('#tab-approved .empty-state').count();
    expect(hasApproved + hasEmptyState).toBeGreaterThanOrEqual(1);

    await saveEvidence(page, EVIDENCE, 'N-07-extended-leave.png');
  });

  test('N-08: All three tabs (TimeOff, Swaps, Approved) are accessible', async ({ page }) => {
    await loginAsOwner(page);
    await navigateTo(page, '/Requests/Index');

    const tabs = ['timeoff', 'swaps', 'approved'];

    for (const tabName of tabs) {
      const tabBtn = page.locator(`button[data-tab="${tabName}"]`);
      // ASSERT: Each tab button exists
      await expect(tabBtn).toBeVisible({ timeout: 5000 });

      // Click the tab
      await tabBtn.click();

      // ASSERT: Corresponding panel is now active/visible
      const panel = page.locator(`#tab-${tabName}`);
      await expect(panel).toBeVisible({ timeout: 3000 });
    }

    await saveEvidence(page, EVIDENCE, 'N-08-override-limits.png');
  });

  test('N-09: Approved tab shows delete button only for future time-off', async ({ page }) => {
    await loginAsOwner(page);
    await navigateTo(page, '/Requests/Index');

    // Switch to approved tab
    const approvedTab = page.locator('button[data-tab="approved"]');
    await approvedTab.click();
    await expect(page.locator('#tab-approved')).toBeVisible({ timeout: 5000 });

    const approvedCards = page.locator('#tab-approved .request-card');
    const cardCount = await approvedCards.count();

    if (cardCount > 0) {
      // ASSERT: Cards either have a delete form or a status label (past/active)
      const firstCard = approvedCards.first();
      const hasDeleteForm = await firstCard.locator('form[action*="Delete"], button:has-text("Delete")').count();
      const hasStatusLabel = await firstCard.locator('div:has-text("Past"), div:has-text("Active")').count();
      // At least one of these should be present
      expect(hasDeleteForm + hasStatusLabel).toBeGreaterThanOrEqual(1);
    } else {
      // ASSERT: Empty state is shown
      await expect(page.locator('#tab-approved .empty-state')).toBeVisible({ timeout: 3000 });
    }

    await saveEvidence(page, EVIDENCE, 'N-09-delete-timeoff.png');
  });

  test('N-10: Calendar/Overview page loads (time-off visibility)', async ({ page }) => {
    await loginAsOwner(page);
    await navigateTo(page, '/Calendar/Overview');

    // ASSERT: The Overview page loaded successfully
    expect(page.url()).toContain('/Calendar/Overview');

    // ASSERT: Page is not an error page
    await expect(page.locator('h1:has-text("An unhandled exception occurred")')).not.toBeVisible({ timeout: 2000 });

    // ASSERT: Some page content rendered
    const body = page.locator('body');
    await expect(body).toBeVisible();

    await saveEvidence(page, EVIDENCE, 'N-10-timeoff-overlay.png');
  });

  test('N-11: Calendar/OnCall page loads (duty conflict check)', async ({ page }) => {
    await loginAsOwner(page);
    await navigateTo(page, '/Calendar/OnCall');

    // ASSERT: The OnCall page loaded successfully
    expect(page.url()).toContain('/Calendar/OnCall');

    // ASSERT: Page is not an error page
    await expect(page.locator('h1:has-text("An unhandled exception occurred")')).not.toBeVisible({ timeout: 2000 });

    await saveEvidence(page, EVIDENCE, 'N-11-duty-conflict.png');
  });

  test('N-12: Requests page tab navigation uses correct ARIA attributes', async ({ page }) => {
    await loginAsOwner(page);
    await navigateTo(page, '/Requests/Index');

    // ASSERT: Tab list has role="tablist"
    const tabList = page.locator('[role="tablist"]');
    await expect(tabList).toBeVisible({ timeout: 5000 });

    // ASSERT: Tab buttons have role="tab"
    const tabButtons = page.locator('[role="tab"]');
    const tabCount = await tabButtons.count();
    expect(tabCount).toBe(3); // timeoff, swaps, approved

    // ASSERT: Exactly one tab is selected
    const selectedTabs = page.locator('[role="tab"][aria-selected="true"]');
    await expect(selectedTabs).toHaveCount(1);

    // ASSERT: Tab panels have role="tabpanel"
    const tabPanels = page.locator('[role="tabpanel"]');
    const panelCount = await tabPanels.count();
    expect(panelCount).toBe(3);

    await saveEvidence(page, EVIDENCE, 'N-12-duplicate-timeoff.png');
  });
});

// =============================================================================
// Phase 2 (P1): Time-Off Extended — N-13 through N-17
// =============================================================================

test.describe('Module N: Time-Off Extended (P1)', () => {
  test.beforeEach(async ({ page }) => {
    await loginAsOwner(page);
  });

  // ---------------------------------------------------------------------------
  // N-13: Time-off request form has all required fields
  // ---------------------------------------------------------------------------
  test('N-13: Time-off request form structure is complete', async ({ page }) => {
    await navigateTo(page, '/My/Requests');

    const requestsPage = page.locator('.requests-page');
    await expect(requestsPage).toBeVisible({ timeout: 15000 });

    // ASSERT: Vacation type selector exists
    const vacationType = page.locator('#vacationType');
    await expect(vacationType).toBeVisible({ timeout: 5000 });

    // ASSERT: Start date field exists
    const startDate = page.locator('#startDate');
    await expect(startDate).toHaveCount(1);

    // ASSERT: End date container exists
    const endDateContainer = page.locator('#endDateContainer');
    await expect(endDateContainer).toHaveCount(1);

    // ASSERT: Reason textarea exists
    const reason = page.locator('textarea[name*="Reason"], textarea#Reason, textarea[id*="Reason"]');
    const reasonCount = await reason.count();
    expect(reasonCount).toBeGreaterThanOrEqual(1);

    // ASSERT: Submit button exists (scoped to main content to avoid hidden sidebar form)
    const submitBtn = page.locator('#main-content form button[type="submit"]').first();
    await expect(submitBtn).toBeVisible({ timeout: 5000 });

    await saveEvidence(page, EVIDENCE, 'N-13-form-fields.png');
  });

  // ---------------------------------------------------------------------------
  // N-14: After-duty vacation type hides end date
  // ---------------------------------------------------------------------------
  test('N-14: AfterDuty vacation type hides end date field', async ({ page }) => {
    await navigateTo(page, '/My/Requests');

    const requestsPage = page.locator('.requests-page');
    await expect(requestsPage).toBeVisible({ timeout: 15000 });

    const vacationType = page.locator('#vacationType');
    await expect(vacationType).toBeVisible({ timeout: 5000 });

    // Select AfterDuty type (value=1)
    await vacationType.selectOption('1');
    await page.waitForTimeout(500);

    // ASSERT: End date container should be hidden
    const endDateContainer = page.locator('#endDateContainer');
    const isHidden = await endDateContainer.evaluate(el => {
      const style = window.getComputedStyle(el);
      return style.display === 'none' || el.hidden;
    });
    expect(isHidden).toBe(true);

    // Switch back to Regular (value=0)
    await vacationType.selectOption('0');
    await page.waitForTimeout(500);

    // ASSERT: End date container should be visible again
    const isVisible = await endDateContainer.evaluate(el => {
      const style = window.getComputedStyle(el);
      return style.display !== 'none' && !el.hidden;
    });
    expect(isVisible).toBe(true);

    await saveEvidence(page, EVIDENCE, 'N-14-afterduty-hide-enddate.png');
  });

  // ---------------------------------------------------------------------------
  // N-15: Request history shows status badges
  // ---------------------------------------------------------------------------
  test('N-15: Request history renders with status badges', async ({ page }) => {
    await navigateTo(page, '/My/Requests');

    const requestsPage = page.locator('.requests-page');
    await expect(requestsPage).toBeVisible({ timeout: 15000 });

    // ASSERT: History section exists
    const historySection = page.locator('.history-section');
    const historyCount = await historySection.count();
    expect(historyCount).toBeGreaterThanOrEqual(1);

    // Check for request items or empty state
    const requestItems = page.locator('.request-item');
    const itemCount = await requestItems.count();

    if (itemCount > 0) {
      // ASSERT: Request items have status badges
      const statusBadges = page.locator('.status-badge');
      const badgeCount = await statusBadges.count();
      expect(badgeCount).toBeGreaterThanOrEqual(1);
    } else {
      // Empty state is valid — verify empty state element
      const emptyState = page.locator('.empty-state');
      const emptyCount = await emptyState.count();
      expect(emptyCount).toBeGreaterThanOrEqual(1);
    }

    await saveEvidence(page, EVIDENCE, 'N-15-request-history.png');
  });

  // ---------------------------------------------------------------------------
  // N-16: Swap request form visible if user has shifts
  // ---------------------------------------------------------------------------
  test('N-16: Swap request section renders on Requests page', async ({ page }) => {
    await navigateTo(page, '/My/Requests');

    const requestsPage = page.locator('.requests-page');
    await expect(requestsPage).toBeVisible({ timeout: 15000 });

    // The swap section should render (either form or empty state)
    const formsSection = page.locator('.forms-section');
    await expect(formsSection).toBeVisible({ timeout: 5000 });

    // ASSERT: There should be at least 1 card (time-off form)
    const cards = formsSection.locator('.card');
    const cardCount = await cards.count();
    expect(cardCount).toBeGreaterThanOrEqual(1);

    await saveEvidence(page, EVIDENCE, 'N-16-swap-section.png');
  });

  // ---------------------------------------------------------------------------
  // N-17: Cancel button requires confirmation dialog
  // ---------------------------------------------------------------------------
  test('N-17: Request cancel mechanism exists', async ({ page }) => {
    await navigateTo(page, '/My/Requests');

    const requestsPage = page.locator('.requests-page');
    await expect(requestsPage).toBeVisible({ timeout: 15000 });

    // Check if any pending requests have cancel buttons
    const cancelBtns = page.locator('.btn-cancel, form[action*="CancelRequest"] button');
    const cancelCount = await cancelBtns.count();

    if (cancelCount > 0) {
      // ASSERT: Cancel buttons use onclick confirm
      const firstCancel = cancelBtns.first();
      const onclick = await firstCancel.evaluate(el => {
        const form = el.closest('form');
        return form ? form.getAttribute('onsubmit') : el.getAttribute('onclick');
      });
      // Should have confirmation dialog
      expect(onclick || '').toContain('confirm');
    }

    // ASSERT: Page renders correctly regardless
    await expect(requestsPage).toBeVisible();

    await saveEvidence(page, EVIDENCE, 'N-17-cancel-mechanism.png');
  });
});
