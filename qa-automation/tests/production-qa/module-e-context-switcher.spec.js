// @ts-check
const { test, expect } = require('@playwright/test');
const {
  loginAsOwner,
  login,
  logout,
  saveEvidence,
  navigateTo,
  createUser,
  TEST_PASSWORD,
  ROLE_ENUM,
} = require('../../helpers/production-qa-helpers');

const EVIDENCE = '05-context-switcher';

test.describe('Module E: Context Switcher', () => {
  test('E-01: Owner sees context switcher with company list', async ({ page }) => {
    await loginAsOwner(page);
    await navigateTo(page, '/');

    // ASSERT: the context switcher component is visible
    const switcher = page.locator('#contextSwitcher');
    await expect(switcher).toBeVisible({ timeout: 10000 });

    // ASSERT: the trigger button is visible (shows current company name)
    const trigger = page.locator('#contextSwitcherTrigger');
    await expect(trigger).toBeVisible({ timeout: 5000 });

    // ASSERT: the trigger shows a company name (non-empty text)
    const triggerName = page.locator('#contextSwitcherTrigger .context-switcher__name');
    await expect(triggerName).toBeVisible({ timeout: 5000 });
    const nameText = await triggerName.textContent();
    expect(nameText.trim().length).toBeGreaterThan(0);

    // Click the trigger to open the dropdown
    await trigger.click();
    await page.waitForTimeout(300);

    // ASSERT: dropdown is open (the switcher has the 'is-open' class)
    await expect(switcher).toHaveClass(/is-open/);

    // ASSERT: the dropdown list contains company options
    const dropdown = page.locator('#contextSwitcherDropdown');
    await expect(dropdown).toBeVisible({ timeout: 5000 });

    const options = page.locator('.context-switcher__option');
    const optionCount = await options.count();
    expect(optionCount).toBeGreaterThan(0);

    // ASSERT: the search input is visible inside the dropdown
    const searchInput = page.locator('#contextSwitcherSearch');
    await expect(searchInput).toBeVisible({ timeout: 3000 });

    await saveEvidence(page, EVIDENCE, 'E-01-owner-switcher.png');
  });

  test('E-02: Switch to Tzafona', async ({ page }) => {
    await loginAsOwner(page);
    await navigateTo(page, '/');

    // ASSERT: context switcher is visible
    const trigger = page.locator('#contextSwitcherTrigger');
    await expect(trigger).toBeVisible({ timeout: 10000 });

    // Open the dropdown
    await trigger.click();
    await page.waitForTimeout(300);

    // ASSERT: dropdown is open
    const switcher = page.locator('#contextSwitcher');
    await expect(switcher).toHaveClass(/is-open/);

    // Find and click the Tzafona option
    const tzafonaOption = page.locator('.context-switcher__option[data-context-name="Tzafona"]');
    const tzafonaExists = await tzafonaOption.count();

    if (tzafonaExists === 0) {
      test.skip(true, 'Tzafona company not found in context switcher options');
    }

    await expect(tzafonaOption).toBeVisible({ timeout: 3000 });

    // Clicking triggers a form POST and page navigation
    await Promise.all([
      page.waitForLoadState('networkidle'),
      tzafonaOption.click(),
    ]);

    // ASSERT: after switching, the trigger now shows "Tzafona"
    const newTrigger = page.locator('#contextSwitcherTrigger .context-switcher__name');
    await expect(newTrigger).toBeVisible({ timeout: 10000 });
    await expect(newTrigger).toContainText('Tzafona');

    await saveEvidence(page, EVIDENCE, 'E-02-switch-tzafona.png');
  });

  test('E-03: Switch to Hir', async ({ page }) => {
    await loginAsOwner(page);
    await navigateTo(page, '/');

    // ASSERT: context switcher is visible
    const trigger = page.locator('#contextSwitcherTrigger');
    await expect(trigger).toBeVisible({ timeout: 10000 });

    // Open the dropdown
    await trigger.click();
    await page.waitForTimeout(300);

    // Find and click the Hir option
    const hirOption = page.locator('.context-switcher__option[data-context-name="Hir"]');
    const hirExists = await hirOption.count();

    if (hirExists === 0) {
      test.skip(true, 'Hir company not found in context switcher options');
    }

    await expect(hirOption).toBeVisible({ timeout: 3000 });

    await Promise.all([
      page.waitForLoadState('networkidle'),
      hirOption.click(),
    ]);

    // ASSERT: trigger now shows "Hir"
    const newTrigger = page.locator('#contextSwitcherTrigger .context-switcher__name');
    await expect(newTrigger).toBeVisible({ timeout: 10000 });
    await expect(newTrigger).toContainText('Hir');

    await saveEvidence(page, EVIDENCE, 'E-03-switch-hir.png');
  });

  test('E-04: Switch to another company (e.g. Hitazmut or any available)', async ({ page }) => {
    await loginAsOwner(page);
    await navigateTo(page, '/');

    // ASSERT: context switcher is visible
    const trigger = page.locator('#contextSwitcherTrigger');
    await expect(trigger).toBeVisible({ timeout: 10000 });

    // Get current company name before switching
    const currentName = await page.locator('#contextSwitcherTrigger .context-switcher__name').textContent();

    // Open the dropdown
    await trigger.click();
    await page.waitForTimeout(300);

    // ASSERT: dropdown is open
    await expect(page.locator('#contextSwitcher')).toHaveClass(/is-open/);

    // Find an option that is NOT the currently active one
    const allOptions = page.locator('.context-switcher__option:not(.is-active)');
    const optionCount = await allOptions.count();
    expect(optionCount).toBeGreaterThan(0);

    // Get the name of the option we're about to select
    const targetOption = allOptions.first();
    const targetName = await targetOption.getAttribute('data-context-name');
    expect(targetName).toBeTruthy();

    // Click to switch
    await Promise.all([
      page.waitForLoadState('networkidle'),
      targetOption.click(),
    ]);

    // ASSERT: trigger now shows the new company name
    const newTrigger = page.locator('#contextSwitcherTrigger .context-switcher__name');
    await expect(newTrigger).toBeVisible({ timeout: 10000 });
    await expect(newTrigger).toContainText(targetName);

    await saveEvidence(page, EVIDENCE, 'E-04-switch-other-company.png');
  });

  test('E-05: Search/filter companies in dropdown', async ({ page }) => {
    await loginAsOwner(page);
    await navigateTo(page, '/');

    // ASSERT: context switcher is visible
    const trigger = page.locator('#contextSwitcherTrigger');
    await expect(trigger).toBeVisible({ timeout: 10000 });

    // Open the dropdown
    await trigger.click();
    await page.waitForTimeout(300);

    // ASSERT: search input is visible
    const searchInput = page.locator('#contextSwitcherSearch');
    await expect(searchInput).toBeVisible({ timeout: 3000 });

    // Count all options before filtering
    const allOptionsBefore = page.locator('.context-switcher__option');
    const totalBefore = await allOptionsBefore.count();
    expect(totalBefore).toBeGreaterThan(0);

    // Type a search term that should match at least one company
    // Use a partial name that likely matches something (e.g., first 3 chars of first option)
    const firstOptionName = await allOptionsBefore.first().getAttribute('data-context-name');
    const searchTerm = firstOptionName.substring(0, 3);

    await searchInput.fill(searchTerm);
    // Wait for debounce (150ms) + buffer
    await page.waitForTimeout(300);

    // ASSERT: the visible options are filtered (some may be hidden)
    const visibleAfterFilter = page.locator('.context-switcher__option:not([style*="display: none"])');
    const countAfterFilter = await visibleAfterFilter.count();
    expect(countAfterFilter).toBeGreaterThan(0);
    expect(countAfterFilter).toBeLessThanOrEqual(totalBefore);

    // ASSERT: all visible options contain the search term
    for (let i = 0; i < countAfterFilter; i++) {
      const optionName = await visibleAfterFilter.nth(i).getAttribute('data-context-name');
      expect(optionName.toLowerCase()).toContain(searchTerm.toLowerCase());
    }

    // Clear search and verify all options return
    await searchInput.fill('');
    await page.waitForTimeout(300);

    const visibleAfterClear = page.locator('.context-switcher__option:not([style*="display: none"])');
    const countAfterClear = await visibleAfterClear.count();
    expect(countAfterClear).toBe(totalBefore);

    // ASSERT: typing a nonsense string shows "no results"
    await searchInput.fill('zzzzzzxyznonexistent');
    await page.waitForTimeout(300);
    const noResults = page.locator('#contextSwitcherNoResults');
    await expect(noResults).toBeVisible({ timeout: 3000 });

    await saveEvidence(page, EVIDENCE, 'E-05-search-filter.png');
  });

  test('E-06: Close dropdown with Escape key', async ({ page }) => {
    await loginAsOwner(page);
    await navigateTo(page, '/');

    // ASSERT: context switcher is visible
    const trigger = page.locator('#contextSwitcherTrigger');
    await expect(trigger).toBeVisible({ timeout: 10000 });

    // Open the dropdown
    await trigger.click();
    await page.waitForTimeout(300);

    // ASSERT: dropdown is open
    const switcher = page.locator('#contextSwitcher');
    await expect(switcher).toHaveClass(/is-open/);

    // Press Escape to close
    await page.keyboard.press('Escape');
    await page.waitForTimeout(300);

    // ASSERT: dropdown is closed
    const classAfterEscape = await switcher.getAttribute('class');
    expect(classAfterEscape).not.toContain('is-open');

    // ASSERT: trigger button aria-expanded is false
    await expect(trigger).toHaveAttribute('aria-expanded', 'false');

    await saveEvidence(page, EVIDENCE, 'E-06-close-dropdown.png');
  });

  test('E-07: Employee does NOT see context switcher', async ({ page }) => {
    // Create an employee user to test with
    await loginAsOwner(page);
    await navigateTo(page, '/Admin/Users');

    const empEmail = `e07.emp.${Date.now()}@test`;
    await createUser(page, {
      email: empEmail,
      displayName: 'E07 Employee NoSwitcher',
      role: 'Employee',
      password: TEST_PASSWORD,
    });

    // Logout owner and login as employee
    await logout(page);
    await login(page, empEmail, TEST_PASSWORD);

    // Navigate to home
    await navigateTo(page, '/');

    // ASSERT: the context switcher trigger is NOT visible for employees
    // The context switcher component may not render at all, or it may show
    // the single-context (non-interactive) variant.
    const multiSwitcherTrigger = page.locator('#contextSwitcherTrigger');
    const multiSwitcherVisible = await multiSwitcherTrigger.isVisible({ timeout: 3000 }).catch(() => false);

    // The multi-company interactive switcher should NOT be visible for employees
    expect(multiSwitcherVisible).toBe(false);

    await saveEvidence(page, EVIDENCE, 'E-07-employee-no-switcher.png');
  });

  test('E-08: Keyboard navigation in context switcher', async ({ page }) => {
    await loginAsOwner(page);
    await navigateTo(page, '/');

    // ASSERT: context switcher trigger is visible
    const trigger = page.locator('#contextSwitcherTrigger');
    await expect(trigger).toBeVisible({ timeout: 10000 });

    // Open the dropdown
    await trigger.click();
    await page.waitForTimeout(300);

    // ASSERT: dropdown is open
    const switcher = page.locator('#contextSwitcher');
    await expect(switcher).toHaveClass(/is-open/);

    // ASSERT: search input is focused (it should auto-focus on open)
    const searchInput = page.locator('#contextSwitcherSearch');
    await expect(searchInput).toBeFocused({ timeout: 3000 });

    // Press ArrowDown to focus first option
    await page.keyboard.press('ArrowDown');
    await page.waitForTimeout(200);

    // ASSERT: first visible option has the 'is-focused' class
    const firstOption = page.locator('.context-switcher__option').first();
    await expect(firstOption).toHaveClass(/is-focused/);

    // ASSERT: aria-activedescendant is updated on the search input
    const activeDescendant = await searchInput.getAttribute('aria-activedescendant');
    expect(activeDescendant).toBeTruthy();
    expect(activeDescendant.length).toBeGreaterThan(0);

    // Press ArrowDown again to move to second option
    await page.keyboard.press('ArrowDown');
    await page.waitForTimeout(200);

    // ASSERT: first option no longer focused, second is focused
    const secondOption = page.locator('.context-switcher__option').nth(1);
    const secondOptionExists = await secondOption.count();
    if (secondOptionExists > 0) {
      await expect(secondOption).toHaveClass(/is-focused/);
      await expect(firstOption).not.toHaveClass(/is-focused/);
    }

    // Press ArrowUp to go back
    await page.keyboard.press('ArrowUp');
    await page.waitForTimeout(200);
    await expect(firstOption).toHaveClass(/is-focused/);

    // Press Escape to close
    await page.keyboard.press('Escape');
    await page.waitForTimeout(300);

    // ASSERT: dropdown is closed after Escape
    const classAfterEscape = await switcher.getAttribute('class');
    expect(classAfterEscape).not.toContain('is-open');

    // ASSERT: trigger button should regain focus after Escape
    await expect(trigger).toBeFocused({ timeout: 3000 });

    await saveEvidence(page, EVIDENCE, 'E-08-keyboard-nav.png');
  });
});
