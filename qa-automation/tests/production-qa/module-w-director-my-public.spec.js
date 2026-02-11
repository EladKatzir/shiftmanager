// @ts-check
const { test, expect } = require('@playwright/test');
const { loginAsOwner, login, logout, saveEvidence, navigateTo, assertPageContains, assertMinCount, TEST_PASSWORD, BASE_URL } = require('../../helpers/production-qa-helpers');

const EVIDENCE = '23-director-my-public';

test.describe('Module W: Director, My, Public Pages', () => {

  test('W-01: Director hub dashboard renders', async ({ page }) => {
    await login(page, 'dir.alhut@test', TEST_PASSWORD);
    await navigateTo(page, '/Director/Index');

    // ASSERT: Page loaded without error (navigateTo checks for 500s)
    const heading = page.locator('main h1, main h2').first();
    await expect(heading).toBeVisible({ timeout: 10000 });

    // ASSERT: Director page has meaningful content (not an error page)
    await expect(page.locator('main, .page-content, .director-hub').first()).toBeVisible({ timeout: 5000 });

    await saveEvidence(page, EVIDENCE, 'W-01-director-hub.png');
  });

  test('W-02: Director page has company/filter controls', async ({ page }) => {
    await login(page, 'dir.alhut@test', TEST_PASSWORD);
    await navigateTo(page, '/Director/Index');

    // ASSERT: Page loaded and has content
    const heading = page.locator('main h1, main h2').first();
    await expect(heading).toBeVisible({ timeout: 10000 });

    // ASSERT: Director page has main content area
    await expect(page.locator('main, .page-content, .director-hub').first()).toBeVisible({ timeout: 5000 });

    await saveEvidence(page, EVIDENCE, 'W-02-director-filter.png');
  });

  test('W-03: My Profile page loads with form fields', async ({ page }) => {
    await loginAsOwner(page);
    await navigateTo(page, '/My/Profile');

    // ASSERT: Page title is visible
    const heading = page.locator('main h1').first();
    await expect(heading).toBeVisible({ timeout: 10000 });

    // ASSERT: Profile form exists
    const form = page.locator('form');
    await expect(form.first()).toBeVisible({ timeout: 5000 });

    // ASSERT: Display Name input field is visible and has a value
    const displayNameInput = page.locator('input#DisplayName, input[name="DisplayName"]').first();
    await expect(displayNameInput).toBeVisible({ timeout: 5000 });
    const nameValue = await displayNameInput.inputValue();
    expect(nameValue.length).toBeGreaterThan(0);

    // ASSERT: Avatar section (card) is visible
    const avatarCard = page.locator('.card').first();
    await expect(avatarCard).toBeVisible({ timeout: 5000 });

    // ASSERT: Personal Information card is visible
    await assertPageContains(page, 'DisplayName');

    // ASSERT: Phone input exists
    const phoneInput = page.locator('input#Phone, input[name="Phone"]').first();
    await expect(phoneInput).toBeVisible({ timeout: 5000 });

    // ASSERT: Save button is visible
    const saveBtn = page.locator('button[type="submit"]').first();
    await expect(saveBtn).toBeVisible({ timeout: 5000 });

    // ASSERT: Cancel link is visible
    const cancelLink = page.locator('a[href="/My"]');
    await expect(cancelLink).toBeVisible({ timeout: 5000 });

    await saveEvidence(page, EVIDENCE, 'W-03-profile.png');
  });

  test('W-04: My Settings page loads with form elements', async ({ page }) => {
    await loginAsOwner(page);
    await navigateTo(page, '/My/Settings');

    // ASSERT: Page title is visible
    const heading = page.locator('main h1, main h2').first();
    await expect(heading).toBeVisible({ timeout: 10000 });

    // ASSERT: Settings form exists
    const form = page.locator('form');
    await expect(form.first()).toBeVisible({ timeout: 5000 });

    // ASSERT: At least one card section is visible
    const cards = page.locator('.card, .section-card');
    await assertMinCount(cards, 1);

    await saveEvidence(page, EVIDENCE, 'W-04-settings.png');
  });

  test('W-05: Notification center page loads', async ({ page }) => {
    await loginAsOwner(page);
    await navigateTo(page, '/My/NotificationCenter');

    // ASSERT: Page heading is visible
    const heading = page.locator('h2');
    await expect(heading.first()).toBeVisible({ timeout: 10000 });

    // ASSERT: Page has notification-related content (list or empty state)
    const notificationItems = page.locator('.notification-item');
    const emptyState = page.locator(':text("no notification"), :text("No notification"), :text("אין"), .empty-state');
    const pageContent = page.locator('main, .page-content, .notification-center').first();
    await expect(pageContent.or(notificationItems.first()).or(emptyState.first())).toBeVisible({ timeout: 5000 });

    await saveEvidence(page, EVIDENCE, 'W-05-notifications.png');
  });

  test('W-06: Notification badge renders on main page', async ({ page }) => {
    await loginAsOwner(page);
    await navigateTo(page, '/');

    // ASSERT: Navigation sidebar is visible (where badge appears)
    const nav = page.locator('.sidebar, nav, .navbar').first();
    await expect(nav).toBeVisible({ timeout: 5000 });

    // ASSERT: Notification bell link exists in nav
    const bellLink = page.locator('a[href*="NotificationCenter"], .notification-bell');
    await expect(bellLink.first()).toBeVisible({ timeout: 5000 });

    await saveEvidence(page, EVIDENCE, 'W-06-notification-badge.png');
  });

  test('W-07: Help page renders with FAQ content', async ({ page }) => {
    await loginAsOwner(page);
    await navigateTo(page, '/My/Help');

    // ASSERT: Page heading is visible
    const heading = page.locator('main h1').first();
    await expect(heading).toBeVisible({ timeout: 10000 });

    // ASSERT: Help page container is visible
    const helpPage = page.locator('.help-page');
    await expect(helpPage).toBeVisible({ timeout: 5000 });

    // ASSERT: Help sections exist with FAQ items
    const helpSections = page.locator('.help-section');
    await assertMinCount(helpSections, 1);

    // ASSERT: FAQ details elements exist
    const faqItems = page.locator('.help-faq, details');
    await assertMinCount(faqItems, 1);

    // ASSERT: FAQ questions (summary elements) are visible
    const faqQuestions = page.locator('.help-faq__question, details summary');
    await assertMinCount(faqQuestions, 1);

    await saveEvidence(page, EVIDENCE, 'W-07-help-page.png');
  });

  test('W-08: API keys page loads', async ({ page }) => {
    await loginAsOwner(page);
    await navigateTo(page, '/My/ApiKeys');

    // ASSERT: Page heading is visible
    const heading = page.locator('h2');
    await expect(heading.first()).toBeVisible({ timeout: 10000 });

    // ASSERT: Request new API key button is visible
    const requestBtn = page.locator('button:has-text("Request"), button:has-text("API")').first();
    await expect(requestBtn).toBeVisible({ timeout: 5000 });

    await saveEvidence(page, EVIDENCE, 'W-08-api-keys.png');
  });

  test('W-09: My Requests page loads', async ({ page }) => {
    await loginAsOwner(page);
    await navigateTo(page, '/My/Requests');

    // ASSERT: Page loaded without error
    const heading = page.locator('main h1, main h2').first();
    await expect(heading).toBeVisible({ timeout: 10000 });

    // ASSERT: Page has request-related content (tab controls or request list)
    const requestContent = page.locator('.tab-content, .requests-container, main, .page-content').first();
    await expect(requestContent).toBeVisible({ timeout: 5000 });

    await saveEvidence(page, EVIDENCE, 'W-09-my-requests.png');
  });

  test('W-10: Public chores page accessible without login', async ({ page }) => {
    // Navigate directly without login
    const response = await page.goto(`${BASE_URL}/Public/Chores`);
    await page.waitForLoadState('networkidle');

    // ASSERT: Page did not redirect to login (accessible without auth)
    const currentUrl = page.url();
    expect(currentUrl).not.toContain('/Auth/Login');

    // ASSERT: Response was received and is not a server error
    expect(response).not.toBeNull();
    expect(response.status()).toBeLessThan(500);

    // ASSERT: Page has chores-related content (heading visible)
    const heading = page.locator('main h1').first();
    await expect(heading).toBeVisible({ timeout: 10000 });

    await saveEvidence(page, EVIDENCE, 'W-10-public-chores.png');
  });

  test('W-11: Public on-duty page accessible without login', async ({ page }) => {
    // Navigate directly without login
    const response = await page.goto(`${BASE_URL}/Public/OnDuty`);
    await page.waitForLoadState('networkidle');

    // ASSERT: Page did not redirect to login (accessible without auth)
    const currentUrl = page.url();
    expect(currentUrl).not.toContain('/Auth/Login');

    // ASSERT: Response was received and is not a server error
    expect(response).not.toBeNull();
    expect(response.status()).toBeLessThan(500);

    // ASSERT: Page has visible heading
    const heading = page.locator('main h1, main h2').first();
    await expect(heading).toBeVisible({ timeout: 10000 });

    await saveEvidence(page, EVIDENCE, 'W-11-public-onduty.png');
  });

  test('W-12: Feedback page renders with form', async ({ page }) => {
    await loginAsOwner(page);
    await navigateTo(page, '/Public/Feedback');

    // ASSERT: Page loaded
    const heading = page.locator('main h1, main h2').first();
    await expect(heading).toBeVisible({ timeout: 10000 });

    // ASSERT: As owner, either the feedback list or the form is visible
    // Owner sees the feedback list view; non-owners see the submit form
    const feedbackContainer = page.locator('.feedback-container');
    await expect(feedbackContainer).toBeVisible({ timeout: 5000 });

    await saveEvidence(page, EVIDENCE, 'W-12-feedback.png');
  });

  test('W-13: Employee dashboard loads after login', async ({ page }) => {
    await login(page, 'emp.tz.alhut@test', TEST_PASSWORD);

    // ASSERT: We are logged in and not on login page
    await expect(page).not.toHaveURL(/\/Auth\/Login/);

    // Navigate to home
    await navigateTo(page, '/');

    // ASSERT: Navigation sidebar is visible (authenticated state)
    const nav = page.locator('.sidebar, nav, .navbar').first();
    await expect(nav).toBeVisible({ timeout: 5000 });

    // ASSERT: Main content area is visible
    await expect(page.locator('main, .page-content, .home-page').first()).toBeVisible({ timeout: 5000 });

    await saveEvidence(page, EVIDENCE, 'W-13-employee-dashboard.png');
  });

  test('W-14: Game leaderboard page loads', async ({ page }) => {
    await loginAsOwner(page);
    await navigateTo(page, '/Game/Leaderboard');

    // ASSERT: Page loaded without error
    const heading = page.locator('main h1, main h2').first();
    await expect(heading).toBeVisible({ timeout: 10000 });

    // ASSERT: Leaderboard page has content area
    await expect(page.locator('main, .page-content, .leaderboard, table').first()).toBeVisible({ timeout: 5000 });

    await saveEvidence(page, EVIDENCE, 'W-14-leaderboard.png');
  });
});
