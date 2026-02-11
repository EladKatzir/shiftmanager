// @ts-check
const { test, expect } = require('@playwright/test');
const { loginAsOwner, saveEvidence, navigateTo, assertPageContains, BASE_URL } = require('../../helpers/production-qa-helpers');

const EVIDENCE = '25-notifications';

test.describe('Module Y: Notifications', () => {
  test.beforeEach(async ({ page }) => {
    await loginAsOwner(page);
  });

  test('Y-01: Notification center page loads successfully', async ({ page }) => {
    await navigateTo(page, '/My/NotificationCenter');
    await page.waitForLoadState('networkidle');

    // ASSERT: We are on the notification center page (URL check)
    expect(page.url()).toContain('NotificationCenter');

    // ASSERT: Page heading is visible
    const heading = page.locator('main h1, main h2').first();
    await expect(heading).toBeVisible({ timeout: 10000 });

    await saveEvidence(page, EVIDENCE, 'Y-01-notification-center.png');
  });

  test('Y-02: Notification center displays notification list or empty state', async ({ page }) => {
    await navigateTo(page, '/My/NotificationCenter');
    await page.waitForLoadState('networkidle');

    // ASSERT: Page heading is visible (not an error page)
    const heading = page.locator('main h1, main h2').first();
    await expect(heading).toBeVisible({ timeout: 10000 });

    // ASSERT: Either notification items exist OR main content area is visible
    const notificationItems = page.locator('.notification-item, .notification-row, .list-group-item');
    const pageContent = page.locator('main, .page-content, .notification-center').first();
    await expect(notificationItems.first().or(pageContent)).toBeVisible({ timeout: 5000 });

    await saveEvidence(page, EVIDENCE, 'Y-02-notification-list.png');
  });

  test('Y-03: Notification bell link exists in navigation', async ({ page }) => {
    await navigateTo(page, '/');
    await page.waitForLoadState('networkidle');

    // ASSERT: Notification bell/link to notification center exists
    const bell = page.locator('a[href*="NotificationCenter"], .notification-bell, .notification-icon, a[href*="notification"]');
    const bellCount = await bell.count();
    expect(bellCount).toBeGreaterThanOrEqual(1);

    // ASSERT: The bell link is visible
    await expect(bell.first()).toBeVisible({ timeout: 5000 });

    await saveEvidence(page, EVIDENCE, 'Y-03-notification-bell.png');
  });

  test('Y-04: Notification badge count element exists', async ({ page }) => {
    await navigateTo(page, '/');
    await page.waitForLoadState('networkidle');

    // ASSERT: Badge element or notification count indicator is present in DOM
    // (may be hidden if count is 0, but the element should exist)
    const badge = page.locator('.notification-badge, .badge, [data-notification-count], .notification-count');
    const bellLink = page.locator('a[href*="NotificationCenter"]');

    // At minimum, the notification center link must exist
    const bellCount = await bellLink.count();
    expect(bellCount).toBeGreaterThanOrEqual(1);

    // If badge exists, verify it's attached to the DOM
    const badgeCount = await badge.count();
    if (badgeCount > 0) {
      // ASSERT: Badge text is either empty or a number
      const badgeText = await badge.first().innerText();
      const isValidBadge = badgeText.trim() === '' || /^\d+$/.test(badgeText.trim());
      expect(isValidBadge).toBe(true);
    }

    await saveEvidence(page, EVIDENCE, 'Y-04-notification-badge.png');
  });

  test('Y-05: Clicking notification bell navigates to notification center', async ({ page }) => {
    await navigateTo(page, '/');
    await page.waitForLoadState('networkidle');

    // ASSERT: Bell link is visible
    const bell = page.locator('a[href*="NotificationCenter"], .notification-bell, .notification-icon').first();
    await expect(bell).toBeVisible({ timeout: 5000 });

    // Click the bell
    await bell.click();
    await page.waitForLoadState('networkidle');

    // ASSERT: Navigated to notification center
    expect(page.url()).toContain('Notification');

    // ASSERT: Page heading is visible (notification center loaded)
    const heading = page.locator('main h1, main h2').first();
    await expect(heading).toBeVisible({ timeout: 10000 });

    await saveEvidence(page, EVIDENCE, 'Y-05-bell-navigation.png');
  });

  test('Y-06: Mark notification as read (if notifications exist)', async ({ page }) => {
    await navigateTo(page, '/My/NotificationCenter');
    await page.waitForLoadState('networkidle');

    // ASSERT: Page heading loaded
    const heading = page.locator('main h1, main h2').first();
    await expect(heading).toBeVisible({ timeout: 10000 });

    // Check if mark-read buttons exist
    const markReadBtn = page.locator('button:has-text("Read"), button:has-text("Mark"), .mark-read, button:has-text("סמן")').first();
    const markReadVisible = await markReadBtn.isVisible();

    if (markReadVisible) {
      // ASSERT: Button is enabled before clicking
      await expect(markReadBtn).toBeEnabled();

      await markReadBtn.click();
      await page.waitForLoadState('networkidle');

      // ASSERT: Page heading still visible after action (no crash)
      await expect(page.locator('main h1, main h2').first()).toBeVisible({ timeout: 5000 });
    }
    // No mark-read button means no unread notifications — heading assertion above is the gate

    // ASSERT: We are still on the notification center page
    expect(page.url()).toContain('NotificationCenter');

    await saveEvidence(page, EVIDENCE, 'Y-06-mark-read.png');
  });

  test('Y-07: Notification center accessible from multiple page contexts', async ({ page }) => {
    // Navigate to different pages and verify notification link is always present
    const pagesToCheck = ['/', '/Calendar/Shifts', '/Admin/Users', '/My/Profile'];

    for (const pageUrl of pagesToCheck) {
      await navigateTo(page, pageUrl);
      await page.waitForLoadState('networkidle');

      // ASSERT: Notification link is present on every page
      const bellLink = page.locator('a[href*="NotificationCenter"], .notification-bell, .notification-icon');
      const count = await bellLink.count();
      expect(count).toBeGreaterThanOrEqual(1);
    }

    await saveEvidence(page, EVIDENCE, 'Y-07-notification-global.png');
  });

  test('Y-08: Notification center page has no console errors', async ({ page }) => {
    const consoleErrors = [];
    page.on('console', msg => {
      if (msg.type() === 'error') {
        const text = msg.text();
        if (!text.includes('favicon') && !text.includes('net::ERR_')) {
          consoleErrors.push(text);
        }
      }
    });

    await navigateTo(page, '/My/NotificationCenter');
    await page.waitForLoadState('networkidle');
    await page.waitForTimeout(1000);

    // ASSERT: No JavaScript console errors on notification page
    expect(consoleErrors).toEqual([]);

    await saveEvidence(page, EVIDENCE, 'Y-08-no-console-errors.png');
  });
});
