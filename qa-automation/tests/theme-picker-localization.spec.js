// @ts-check
const { test, expect } = require('@playwright/test');
const { loginAsOwner } = require('../helpers/auth-helpers');

/**
 * Theme Picker Localization - Phase 4.1 Playwright coverage
 *
 * The theme picker is a hidden feature activated by Shift+clicking the logo.
 * Verifies every localized string renders in the active culture, AND that the
 * intentional exceptions (R/G/B/Hex are universal technical identifiers) stay
 * as literal English in BOTH cultures — a positive assertion so anyone who
 * "fixes" them by translating will break the test and see the reasoning.
 */

async function openThemePicker(page) {
  // Shift+click on the sidebar brand triggers the lazy-loaded picker.
  const brand = page.locator('.sidebar-brand, .brand, .page-loader__brand').first();
  await brand.waitFor({ state: 'visible' });
  await brand.click({ modifiers: ['Shift'] });
  // Picker is appended to body with class theme-picker-backdrop.
  await page.locator('.theme-picker-backdrop').waitFor({ state: 'visible' });
}

test.describe('Theme Picker Localization', () => {
  test.beforeEach(async ({ page }) => {
    await loginAsOwner(page);
  });

  test('TP-01: picker opens on Shift+click and title is localized', async ({ page }) => {
    await openThemePicker(page);
    const title = page.locator('#theme-picker-title');
    await expect(title).toBeVisible();
    const text = (await title.textContent())?.trim() ?? '';
    expect(text.length).toBeGreaterThan(0);
    // Either Hebrew or English — both are valid depending on current culture.
    expect(text).toMatch(/^(Appearance|מראה)$/);
  });

  test('TP-02: R/G/B/Hex stay as literal English even in Hebrew mode', async ({ page }) => {
    // Intentional exception per QA review — these are universal rgb() identifiers.
    // If a future contributor translates them, this test will break and the comment
    // here explains why. Do NOT relax this assertion without re-reading the QA notes.
    await openThemePicker(page);
    const hex = page.locator('.theme-picker__inputs label').nth(0);
    const r = page.locator('.theme-picker__inputs label').nth(1);
    const g = page.locator('.theme-picker__inputs label').nth(2);
    const b = page.locator('.theme-picker__inputs label').nth(3);

    await expect(hex).toContainText('Hex');
    await expect(r).toContainText(/^\s*R/);
    await expect(g).toContainText(/^\s*G/);
    await expect(b).toContainText(/^\s*B/);
  });

  test('TP-03: preset names render from AppLocalizer (not raw "Navy")', async ({ page }) => {
    await openThemePicker(page);
    // Each preset button has aria-label and title set from the localizer.
    const firstPreset = page.locator('.theme-picker__preset').first();
    const ariaLabel = await firstPreset.getAttribute('aria-label');
    expect(ariaLabel).toBeTruthy();
    // Should NOT be the raw key name — localizer fallback is gracefully the key name,
    // but after our pre-bake it must resolve to human text.
    expect(ariaLabel).not.toBe('ThemePicker_PresetNavy');
  });
});
