// @ts-check
const path = require('path');

// Load environment variables
try {
    require('dotenv').config({ path: path.join(__dirname, '..', '.env') });
} catch (e) {
    // dotenv not installed, rely on system environment variables
}

const BASE_URL = process.env.APP_URL || 'http://localhost:5000';

/**
 * Clean up test data by calling application endpoint
 * This should be called in global teardown or between test suites
 * @param {import('@playwright/test').Page} page - Authenticated page
 */
async function cleanupTestData(page) {
    console.log('🧹 Cleaning up test data...');

    try {
        // Navigate to a cleanup endpoint if available, or manually delete test data
        // For now, we'll delete companies created during tests
        await page.goto(`${BASE_URL}/Admin/Companies`);
        await page.waitForLoadState('networkidle');

        // Delete companies with test prefixes and patterns
        const testPrefixes = ['TenantA_', 'TenantB_', 'Company_', 'TestCompany_', 'E2E_', 'DEL_',
                              'DROP TABLE', 'testcompany-', 'TestCo_'];

        for (const prefix of testPrefixes) {
            let deleted = 0;
            const rows = await page.locator(`tbody tr:has-text("${prefix}")`).count();

            if (rows > 0) {
                console.log(`  Found ${rows} test companies with prefix "${prefix}"`);

                // Delete all companies with this prefix (increased limit to 50)
                for (let i = 0; i < Math.min(rows, 50); i++) {
                    const row = page.locator(`tbody tr:has-text("${prefix}")`).first();
                    const deleteButton = row.locator('button:has-text("Delete"), form[action*="Delete"] button').first();

                    if (await deleteButton.isVisible({ timeout: 1000 }).catch(() => false)) {
                        // Set up dialog handler before clicking
                        page.once('dialog', async dialog => await dialog.accept());

                        await deleteButton.click();
                        await page.waitForLoadState('networkidle');
                        deleted++;
                    }
                }

                if (deleted > 0) {
                    console.log(`  ✓ Deleted ${deleted} test companies with prefix "${prefix}"`);
                }
            }
        }

        console.log('✓ Test data cleanup complete');
    } catch (error) {
        console.error('⚠️  Error during cleanup:', error.message);
        // Don't throw - cleanup is best-effort
    }
}

/**
 * Reset database to clean state for testing
 * This reseeds test users and removes test data
 * @param {import('@playwright/test').Page} page - Authenticated page
 */
async function resetTestDatabase(page) {
    console.log('🔄 Resetting test database...');

    // Clean up test data first
    await cleanupTestData(page);

    // The TestDataSeeder runs on app startup in Development mode
    // So test users should already exist
    console.log('✓ Database reset complete (test users available via seeder)');
}

/**
 * Create isolated test data with unique timestamp
 * Prevents conflicts between parallel tests
 * @returns {Object} Isolated test data with unique identifiers
 */
function createIsolatedTestData() {
    const timestamp = Date.now();
    const random = Math.floor(Math.random() * 10000);
    const uniqueId = `${timestamp}_${random}`;

    return {
        company: {
            name: `TestCo_${uniqueId}`,
            slug: `test-${uniqueId}`,
            displayName: `Test Company ${uniqueId}`,
            managerEmail: `manager_${uniqueId}@test.com`,
            managerName: `Manager ${uniqueId}`,
            managerPassword: '123456'
        },
        user: {
            email: `user_${uniqueId}@test.com`,
            displayName: `Test User ${uniqueId}`,
            password: '123456'
        },
        uniqueId
    };
}

/**
 * Wait for application to be ready
 * @param {import('@playwright/test').Page} page
 */
async function waitForAppReady(page) {
    try {
        await page.goto(BASE_URL, { timeout: 10000 });
        await page.waitForLoadState('networkidle', { timeout: 10000 });
        return true;
    } catch (error) {
        console.error('⚠️  Application not ready:', error.message);
        return false;
    }
}

module.exports = {
    cleanupTestData,
    resetTestDatabase,
    createIsolatedTestData,
    waitForAppReady,
    BASE_URL
};
