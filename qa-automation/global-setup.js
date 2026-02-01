// @ts-check
const { chromium } = require('@playwright/test');
const { loginAsOwner } = require('./helpers/auth-helpers');
const { waitForAppReady, resetTestDatabase, BASE_URL } = require('./helpers/database-helper');

/**
 * Global setup runs once before all tests
 * - Verifies application is running
 * - Resets test database to clean state
 * - Creates authentication state for reuse
 */
async function globalSetup() {
    console.log('\n🚀 Global Setup Starting...\n');

    const browser = await chromium.launch();
    const context = await browser.newContext({
        baseURL: BASE_URL
    });
    const page = await context.newPage();

    try {
        // 1. Verify application is ready
        console.log('📡 Checking application availability...');
        const isReady = await waitForAppReady(page);
        if (!isReady) {
            throw new Error('Application is not responding. Please start the application before running tests.');
        }
        console.log('✓ Application is ready\n');

        // 2. Login as Owner to perform cleanup
        console.log('🔐 Logging in as Owner for cleanup...');
        await loginAsOwner(page);
        console.log('✓ Authenticated as Owner\n');

        // 3. Reset test database (cleanup old test data)
        await resetTestDatabase(page);

        // 4. Save authentication state for reuse (speeds up tests)
        console.log('\n💾 Saving authentication state for test reuse...');
        await context.storageState({ path: 'playwright/.auth/owner.json' });
        console.log('✓ Authentication state saved\n');

    } catch (error) {
        console.error('\n❌ Global setup failed:', error.message);
        throw error;
    } finally {
        await browser.close();
    }

    console.log('✅ Global Setup Complete\n');
}

module.exports = globalSetup;
