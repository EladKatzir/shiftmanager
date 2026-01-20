// @ts-check
const { chromium } = require('@playwright/test');
const { cleanupTestData, BASE_URL } = require('./helpers/database-helper');
const fs = require('fs');
const path = require('path');

/**
 * Global teardown runs once after all tests
 * - Cleans up test data created during test run
 * - Removes saved authentication states
 */
async function globalTeardown() {
    console.log('\n🧹 Global Teardown Starting...\n');

    const browser = await chromium.launch();

    try {
        // 1. Load saved authentication state to perform cleanup
        const authFile = 'playwright/.auth/owner.json';
        if (fs.existsSync(authFile)) {
            console.log('🔐 Using saved authentication for cleanup...');

            const context = await browser.newContext({
                baseURL: BASE_URL,
                storageState: authFile
            });
            const page = await context.newPage();

            // 2. Clean up test data
            await cleanupTestData(page);

            await context.close();
        } else {
            console.log('⚠️  No authentication state found, skipping cleanup');
        }

        // 3. Remove saved authentication states
        console.log('\n🗑️  Removing saved authentication states...');
        const authDir = 'playwright/.auth';
        if (fs.existsSync(authDir)) {
            fs.rmSync(authDir, { recursive: true, force: true });
            console.log('✓ Authentication states removed');
        }

    } catch (error) {
        console.error('\n⚠️  Teardown error (non-fatal):', error.message);
        // Don't throw - teardown failures shouldn't fail the test run
    } finally {
        await browser.close();
    }

    console.log('\n✅ Global Teardown Complete\n');
}

module.exports = globalTeardown;
