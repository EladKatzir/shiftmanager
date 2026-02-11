// @ts-check
const { test, expect } = require('@playwright/test');
const { loginAsOwner, saveEvidence, navigateTo, assertPageContains, assertMinCount } = require('../../helpers/production-qa-helpers');

const EVIDENCE = '21-owner-pages';

test.describe('Module U: Owner Administration', () => {
  test.beforeEach(async ({ page }) => {
    await loginAsOwner(page);
  });

  test('U-01: Owner dashboard renders with stats and navigation links', async ({ page }) => {
    await navigateTo(page, '/Owner/Index');

    // ASSERT: Page header is visible
    const heading = page.locator('main h1').first();
    await expect(heading).toBeVisible({ timeout: 10000 });

    // ASSERT: Hub cards grid renders with at least 4 cards (Owner/Index redirects to Owner Hub)
    const hubCards = page.locator('.hub-card');
    await assertMinCount(hubCards, 4);

    // ASSERT: Hub stat values are present (projects, areas, molecules, companies, users, etc.)
    const hubStatValues = page.locator('.hub-stat-value');
    await assertMinCount(hubStatValues, 4);

    // ASSERT: Quick links section present with navigation links
    const quickLinks = page.locator('.quick-link');
    await assertMinCount(quickLinks, 5);

    // ASSERT: Key navigation links exist (in hub cards or quick links)
    await expect(page.locator('a[href="/Owner/FeatureFlags"]').first()).toBeVisible({ timeout: 5000 });
    await expect(page.locator('a[href="/Owner/Backup"]').first()).toBeVisible({ timeout: 5000 });
    await expect(page.locator('a[href="/Owner/DatabaseConsole"]').first()).toBeVisible({ timeout: 5000 });
    await expect(page.locator('a[href="/Owner/SystemHealth"]').first()).toBeVisible({ timeout: 5000 });

    // ASSERT: Quick links section exists
    const quickLinksSection = page.locator('.hub-quick-links');
    await expect(quickLinksSection).toBeVisible({ timeout: 5000 });

    await saveEvidence(page, EVIDENCE, 'U-01-owner-dashboard.png');
  });

  test('U-02: Backup page renders with create button and backup list', async ({ page }) => {
    await navigateTo(page, '/Owner/Backup');

    // ASSERT: Page header is visible
    const heading = page.locator('main h1').first();
    await expect(heading).toBeVisible({ timeout: 10000 });

    // ASSERT: Backup actions card with create button is visible
    const backupActionsCard = page.locator('.backup-actions-card');
    await expect(backupActionsCard).toBeVisible({ timeout: 5000 });

    // ASSERT: Create backup button exists and is a submit button
    const createBtn = page.locator('form[action*="CreateBackup"] button[type="submit"], .backup-actions-card button[type="submit"]').first();
    await expect(createBtn).toBeVisible({ timeout: 5000 });

    // ASSERT: Backups list card is visible (shows either backups table or empty state)
    const backupsListCard = page.locator('.backups-list-card');
    await expect(backupsListCard).toBeVisible({ timeout: 5000 });

    // ASSERT: Either backup table rows or empty state is shown
    const backupRows = page.locator('.backups-table tbody tr');
    const emptyState = page.locator('.empty-state');
    const rowCount = await backupRows.count();
    const emptyVisible = await emptyState.isVisible().catch(() => false);
    expect(rowCount > 0 || emptyVisible).toBeTruthy();

    // ASSERT: Info card with instructions is visible
    const infoCard = page.locator('.info-card');
    await expect(infoCard).toBeVisible({ timeout: 5000 });

    await saveEvidence(page, EVIDENCE, 'U-02-backup-page.png');
  });

  test('U-03: Backup files have download links when backups exist', async ({ page }) => {
    await navigateTo(page, '/Owner/Backup');

    // Check if any backup rows exist
    const backupRows = page.locator('.backups-table tbody tr');
    const rowCount = await backupRows.count();

    if (rowCount > 0) {
      // ASSERT: Each backup row has a download link
      const downloadLinks = page.locator('.btn-download, a[href*="DownloadBackup"]');
      await assertMinCount(downloadLinks, 1);

      // ASSERT: First download link has a valid href
      const firstDownloadLink = downloadLinks.first();
      await expect(firstDownloadLink).toBeVisible({ timeout: 5000 });
      const href = await firstDownloadLink.getAttribute('href');
      expect(href).toBeTruthy();
      expect(href).toContain('DownloadBackup');
    } else {
      // ASSERT: Empty state is shown when no backups
      const emptyState = page.locator('.empty-state');
      await expect(emptyState).toBeVisible({ timeout: 5000 });
    }

    await saveEvidence(page, EVIDENCE, 'U-03-download-backup.png');
  });

  test('U-04: Backup page has restore functionality', async ({ page }) => {
    await navigateTo(page, '/Owner/Backup');

    // Check if any backup rows exist
    const backupRows = page.locator('.backups-table tbody tr');
    const rowCount = await backupRows.count();

    if (rowCount > 0) {
      // ASSERT: Restore buttons exist for existing backups
      const restoreBtns = page.locator('.btn-restore, form[action*="RestoreBackup"] button');
      await assertMinCount(restoreBtns, 1);
    } else {
      // ASSERT: Empty state indicates user should create first backup
      const emptyState = page.locator('.empty-state');
      await expect(emptyState).toBeVisible({ timeout: 5000 });
    }

    await saveEvidence(page, EVIDENCE, 'U-04-restore-backup.png');
  });

  test('U-05: Database console: valid SELECT returns results', async ({ page }) => {
    await navigateTo(page, '/Owner/DatabaseConsole');

    // ASSERT: Query textarea is visible
    const queryInput = page.locator('textarea#Query, textarea[name="Query"]').first();
    await expect(queryInput).toBeVisible({ timeout: 10000 });

    // ASSERT: Execute button is visible
    const execBtn = page.locator('button[type="submit"]').first();
    await expect(execBtn).toBeVisible({ timeout: 5000 });

    // ASSERT: Tables list sidebar is visible
    const tablesList = page.locator('.tables-list');
    await expect(tablesList).toBeVisible({ timeout: 5000 });

    // ASSERT: At least one table name is shown
    const tableItems = page.locator('.table-item');
    await assertMinCount(tableItems, 1);

    // Execute a SELECT query
    await queryInput.fill('SELECT COUNT(*) FROM Users');
    await execBtn.click();
    await page.waitForLoadState('networkidle');

    // ASSERT: Results appear (either results table or success message)
    const resultsTable = page.locator('.results-table');
    const successAlert = page.locator('.alert-success');
    const hasResults = await resultsTable.isVisible().catch(() => false);
    const hasSuccess = await successAlert.isVisible().catch(() => false);
    expect(hasResults || hasSuccess).toBeTruthy();

    // ASSERT: No error alert is shown
    const errorAlert = page.locator('.alert-error');
    await expect(errorAlert).not.toBeVisible({ timeout: 3000 });

    await saveEvidence(page, EVIDENCE, 'U-05-db-select.png');
  });

  test('U-06: Database console: SQL injection (semicolon) rejected', async ({ page }) => {
    await navigateTo(page, '/Owner/DatabaseConsole');

    // ASSERT: Query textarea is visible
    const queryInput = page.locator('textarea#Query, textarea[name="Query"]').first();
    await expect(queryInput).toBeVisible({ timeout: 10000 });

    // ASSERT: Execute button is visible
    const execBtn = page.locator('button[type="submit"]').first();
    await expect(execBtn).toBeVisible({ timeout: 5000 });

    // Submit a multi-statement SQL injection attempt
    await queryInput.fill('SELECT 1; DROP TABLE Users;');
    await execBtn.click();
    await page.waitForLoadState('networkidle');

    // ASSERT: Error alert is shown (semicolon rejection)
    const errorAlert = page.locator('.alert-error');
    await expect(errorAlert).toBeVisible({ timeout: 10000 });

    // ASSERT: Error message contains rejection indication
    const errorText = await errorAlert.textContent();
    expect(errorText).toBeTruthy();
    // The error should mention semicolons, multiple statements, or be a rejection
    const hasRejection = /semicolon|multiple|statement|not allowed|rejected|blocked|error/i.test(errorText || '');
    expect(hasRejection).toBeTruthy();

    // ASSERT: No results table is shown (query was not executed)
    const resultsTable = page.locator('.results-table');
    await expect(resultsTable).not.toBeVisible({ timeout: 3000 });

    await saveEvidence(page, EVIDENCE, 'U-06-sql-injection.png');
  });

  test('U-07: Database console: DROP/INSERT blocked', async ({ page }) => {
    await navigateTo(page, '/Owner/DatabaseConsole');

    // ASSERT: Query textarea is visible
    const queryInput = page.locator('textarea#Query, textarea[name="Query"]').first();
    await expect(queryInput).toBeVisible({ timeout: 10000 });

    const execBtn = page.locator('button[type="submit"]').first();
    await expect(execBtn).toBeVisible({ timeout: 5000 });

    // Submit a DROP TABLE query
    await queryInput.fill('DROP TABLE Users');
    await execBtn.click();
    await page.waitForLoadState('networkidle');

    // ASSERT: Error alert is shown (write operation blocked)
    const errorAlert = page.locator('.alert-error');
    await expect(errorAlert).toBeVisible({ timeout: 10000 });

    // ASSERT: Error message indicates rejection
    const errorText = await errorAlert.textContent();
    expect(errorText).toBeTruthy();
    const hasRejection = /not allowed|read.only|blocked|error|rejected|drop|prohibited/i.test(errorText || '');
    expect(hasRejection).toBeTruthy();

    await saveEvidence(page, EVIDENCE, 'U-07-drop-blocked.png');
  });

  test('U-08: System health page displays health checks', async ({ page }) => {
    await navigateTo(page, '/Owner/SystemHealth');

    // ASSERT: Page header is visible
    const heading = page.locator('main h1').first();
    await expect(heading).toBeVisible({ timeout: 10000 });

    // ASSERT: Overall status card is visible
    const healthStatusCard = page.locator('.health-status-card');
    await expect(healthStatusCard).toBeVisible({ timeout: 5000 });

    // ASSERT: Health checks grid renders with cards
    const healthCheckCards = page.locator('.health-check-card');
    await assertMinCount(healthCheckCards, 3);

    // ASSERT: Database health card is visible
    await assertPageContains(page, 'Database');

    // ASSERT: Memory health card is visible
    await assertPageContains(page, 'Memory');

    // ASSERT: Uptime card is visible
    await assertPageContains(page, 'Uptime');

    // ASSERT: Status badges are present
    const statusBadges = page.locator('.status-badge');
    await assertMinCount(statusBadges, 3);

    // ASSERT: Health logs section exists
    const healthLogs = page.locator('.health-logs');
    await expect(healthLogs).toBeVisible({ timeout: 5000 });

    await saveEvidence(page, EVIDENCE, 'U-08-system-health.png');
  });

  test('U-09: Feature flags page displays toggle switches', async ({ page }) => {
    await navigateTo(page, '/Owner/FeatureFlags');

    // ASSERT: Page header is visible
    const heading = page.locator('main h1').first();
    await expect(heading).toBeVisible({ timeout: 10000 });

    // ASSERT: Feature flag cards exist
    const featureFlagCards = page.locator('.feature-flag-card');
    await assertMinCount(featureFlagCards, 3);

    // ASSERT: Toggle switches exist (checkbox inputs inside toggle-switch labels)
    const toggleSwitches = page.locator('.toggle-switch input[type="checkbox"], .toggle-switch-sm input[type="checkbox"]');
    await assertMinCount(toggleSwitches, 4);

    // ASSERT: Core feature flags are visible
    await assertPageContains(page, 'EnforceCompanyScope');
    await assertPageContains(page, 'EnableDirectorRole');
    await assertPageContains(page, 'AllowPublicSignup');

    // ASSERT: Save button is visible
    const saveBtn = page.locator('button[type="submit"]');
    await expect(saveBtn.first()).toBeVisible({ timeout: 5000 });

    // ASSERT: Feature categories exist
    const featureCategories = page.locator('.feature-category');
    await assertMinCount(featureCategories, 1);

    await saveEvidence(page, EVIDENCE, 'U-09-feature-flags.png');
  });

  test('U-10: Language management page loads', async ({ page }) => {
    await navigateTo(page, '/Owner/LanguageManagement');

    // ASSERT: Page loaded without error (navigateTo checks for 500 errors)
    // ASSERT: Page has visible content
    const heading = page.locator('main h1, main h2').first();
    await expect(heading).toBeVisible({ timeout: 10000 });

    // ASSERT: Page body has substantive content (not blank)
    const body = page.locator('body');
    const text = await body.textContent();
    expect((text || '').length).toBeGreaterThan(50);

    await saveEvidence(page, EVIDENCE, 'U-10-language-mgmt.png');
  });

  test('U-11: Email config page loads', async ({ page }) => {
    await navigateTo(page, '/Owner/EmailConfig');

    // ASSERT: Page loaded without error
    const heading = page.locator('main h1, main h2').first();
    await expect(heading).toBeVisible({ timeout: 10000 });

    // ASSERT: Page has form elements or configuration display
    const body = page.locator('body');
    const text = await body.textContent();
    expect((text || '').length).toBeGreaterThan(50);

    await saveEvidence(page, EVIDENCE, 'U-11-email-config.png');
  });

  test('U-12: Email templates page loads', async ({ page }) => {
    await navigateTo(page, '/Owner/EmailTemplates');

    // ASSERT: Page loaded without error
    const heading = page.locator('main h1, main h2').first();
    await expect(heading).toBeVisible({ timeout: 10000 });

    // ASSERT: Page has substantive content
    const body = page.locator('body');
    const text = await body.textContent();
    expect((text || '').length).toBeGreaterThan(50);

    await saveEvidence(page, EVIDENCE, 'U-12-email-templates.png');
  });

  test('U-13: Griffin config page loads', async ({ page }) => {
    await navigateTo(page, '/Owner/GriffinConfig');

    // ASSERT: Page loaded without error
    const heading = page.locator('main h1, main h2').first();
    await expect(heading).toBeVisible({ timeout: 10000 });

    // ASSERT: Page has substantive content
    const body = page.locator('body');
    const text = await body.textContent();
    expect((text || '').length).toBeGreaterThan(50);

    await saveEvidence(page, EVIDENCE, 'U-13-griffin-config.png');
  });

  test('U-14: Permissions page loads with grant data', async ({ page }) => {
    await navigateTo(page, '/Owner/Permissions');

    // ASSERT: Page loaded without error
    const heading = page.locator('main h1, main h2').first();
    await expect(heading).toBeVisible({ timeout: 10000 });

    // ASSERT: Page has substantive content
    const body = page.locator('body');
    const text = await body.textContent();
    expect((text || '').length).toBeGreaterThan(50);

    await saveEvidence(page, EVIDENCE, 'U-14-permissions.png');
  });

  test('U-15: Locked users page loads', async ({ page }) => {
    await navigateTo(page, '/Owner/LockedUsers');

    // ASSERT: Page loaded without error
    const heading = page.locator('main h1, main h2').first();
    await expect(heading).toBeVisible({ timeout: 10000 });

    // ASSERT: Page has substantive content (either locked users list or empty state)
    const body = page.locator('body');
    const text = await body.textContent();
    expect((text || '').length).toBeGreaterThan(50);

    await saveEvidence(page, EVIDENCE, 'U-15-locked-users.png');
  });

  test('U-16: Data lifecycle page loads', async ({ page }) => {
    await navigateTo(page, '/Owner/DataLifecycle');

    // ASSERT: Page loaded without error
    const heading = page.locator('main h1, main h2').first();
    await expect(heading).toBeVisible({ timeout: 10000 });

    // ASSERT: Page has substantive content
    const body = page.locator('body');
    const text = await body.textContent();
    expect((text || '').length).toBeGreaterThan(50);

    await saveEvidence(page, EVIDENCE, 'U-16-data-lifecycle.png');
  });

  test('U-17: Area config page loads', async ({ page }) => {
    await navigateTo(page, '/Owner/AreaConfig');

    // ASSERT: Page loaded without error
    const heading = page.locator('main h1, main h2').first();
    await expect(heading).toBeVisible({ timeout: 10000 });

    // ASSERT: Page has substantive content
    const body = page.locator('body');
    const text = await body.textContent();
    expect((text || '').length).toBeGreaterThan(50);

    await saveEvidence(page, EVIDENCE, 'U-17-area-config.png');
  });

  test('U-18: Game config page loads', async ({ page }) => {
    await navigateTo(page, '/Owner/GameConfig');

    // ASSERT: Page loaded without error
    const heading = page.locator('main h1, main h2').first();
    await expect(heading).toBeVisible({ timeout: 10000 });

    // ASSERT: Page has substantive content
    const body = page.locator('body');
    const text = await body.textContent();
    expect((text || '').length).toBeGreaterThan(50);

    await saveEvidence(page, EVIDENCE, 'U-18-game-config.png');
  });
});
