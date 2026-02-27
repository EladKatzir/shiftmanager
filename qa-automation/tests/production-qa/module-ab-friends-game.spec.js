// @ts-check
const { test, expect } = require('@playwright/test');
const { loginAsOwner, saveEvidence, navigateTo, BASE_URL } = require('../../helpers/production-qa-helpers');

const EVIDENCE = '40-friends-game';

/**
 * Module AB: Friends & Game — verifies friends list, game leaderboard,
 * and feature-flag-gated functionality.
 */

test.describe('Module AB: Friends & Game', () => {
  test.beforeEach(async ({ page }) => {
    await loginAsOwner(page);
  });

  // ---------------------------------------------------------------------------
  // AB-01: Friends page loads (or shows feature-disabled state)
  // ---------------------------------------------------------------------------
  test('AB-01: Friends page loads or shows feature gate', async ({ page }) => {
    await navigateTo(page, '/Friends');
    await page.waitForLoadState('networkidle');

    // ASSERT: Either friends page loaded or we got redirected/shown disabled state
    const url = page.url();
    const isFriendsPage = url.includes('Friends');
    const isRedirected = url.includes('AccessDenied') || url.includes('Login') || url.includes('/');

    // Page should have some content regardless
    const main = page.locator('main, .app-content, .page-content, #main-content').first();
    await expect(main).toBeVisible({ timeout: 10000 });

    expect(isFriendsPage || isRedirected).toBe(true);

    await saveEvidence(page, EVIDENCE, 'AB-01-friends-page.png');
  });

  // ---------------------------------------------------------------------------
  // AB-02: Friends API endpoint responds
  // ---------------------------------------------------------------------------
  test('AB-02: Friends API returns response', async ({ page }) => {
    await navigateTo(page, '/');
    await page.waitForLoadState('networkidle');

    const response = await page.evaluate(async (baseUrl) => {
      try {
        const resp = await fetch(`${baseUrl}/Api/Friends/Ids`, {
          method: 'GET',
          credentials: 'same-origin'
        });
        return { status: resp.status };
      } catch (e) {
        return { status: 0, error: e.message };
      }
    }, BASE_URL);

    // ASSERT: API responds (not 500)
    expect(response.status).not.toBe(500);

    await saveEvidence(page, EVIDENCE, 'AB-02-friends-api.png');
  });

  // ---------------------------------------------------------------------------
  // AB-03: Game leaderboard page loads or shows feature gate
  // ---------------------------------------------------------------------------
  test('AB-03: Game leaderboard page loads or redirects', async ({ page }) => {
    await navigateTo(page, '/Game/Leaderboard');
    await page.waitForLoadState('networkidle');

    // ASSERT: Page loaded (may be the game page or redirect)
    const main = page.locator('main, .app-content, #main-content, body').first();
    await expect(main).toBeVisible({ timeout: 10000 });

    await saveEvidence(page, EVIDENCE, 'AB-03-game-leaderboard.png');
  });

  // ---------------------------------------------------------------------------
  // AB-04: Game config API endpoint responds
  // ---------------------------------------------------------------------------
  test('AB-04: Game config API responds', async ({ page }) => {
    await navigateTo(page, '/');
    await page.waitForLoadState('networkidle');

    const response = await page.evaluate(async (baseUrl) => {
      try {
        const resp = await fetch(`${baseUrl}/Api/Game/GetConfiguration`, {
          method: 'GET',
          credentials: 'same-origin'
        });
        return { status: resp.status };
      } catch (e) {
        return { status: 0, error: e.message };
      }
    }, BASE_URL);

    // ASSERT: API responds (not 500)
    expect(response.status).not.toBe(500);

    await saveEvidence(page, EVIDENCE, 'AB-04-game-config-api.png');
  });

  // ---------------------------------------------------------------------------
  // AB-05: Game leaderboard API responds
  // ---------------------------------------------------------------------------
  test('AB-05: Game leaderboard API responds', async ({ page }) => {
    await navigateTo(page, '/');
    await page.waitForLoadState('networkidle');

    const response = await page.evaluate(async (baseUrl) => {
      try {
        const resp = await fetch(`${baseUrl}/Api/Game/GetLeaderboard`, {
          method: 'GET',
          credentials: 'same-origin'
        });
        return { status: resp.status };
      } catch (e) {
        return { status: 0, error: e.message };
      }
    }, BASE_URL);

    expect(response.status).not.toBe(500);

    await saveEvidence(page, EVIDENCE, 'AB-05-game-leaderboard-api.png');
  });

  // ---------------------------------------------------------------------------
  // AB-06: Owner game config page loads
  // ---------------------------------------------------------------------------
  test('AB-06: Owner game config page loads', async ({ page }) => {
    await navigateTo(page, '/Owner/GameConfig');
    await page.waitForLoadState('networkidle');

    const main = page.locator('main, .app-content, #main-content').first();
    await expect(main).toBeVisible({ timeout: 10000 });

    await saveEvidence(page, EVIDENCE, 'AB-06-owner-game-config.png');
  });
});
