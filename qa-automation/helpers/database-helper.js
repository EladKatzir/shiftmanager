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
        await page.goto(`${BASE_URL}/Admin/Companies`);
        await page.waitForLoadState('networkidle');

        const testPrefixes = ['TenantA_', 'TenantB_', 'Company_', 'TestCompany_', 'E2E_', 'DEL_',
                              'DROP TABLE', 'testcompany-', 'TestCo_'];

        // Pre-fix: a leftover #js-confirm-modal-backdrop element from a prior dialog can intercept
        // pointer events on every subsequent click. Strip any open modal/backdrop before starting,
        // and after each delete cycle.
        const dismissAnyConfirmModal = async () => {
            await page.evaluate(() => {
                document.querySelectorAll('#js-confirm-modal, #js-confirm-modal-backdrop, .modal.is-open, .modal-backdrop.is-open')
                    .forEach(el => el.remove());
                document.body.style.overflow = '';
                document.body.classList.remove('modal-open');
            });
        };
        await dismissAnyConfirmModal();

        for (const prefix of testPrefixes) {
            let deleted = 0;
            const rows = await page.locator(`tbody tr:has-text("${prefix}")`).count();
            if (rows === 0) continue;
            console.log(`  Found ${rows} test companies with prefix "${prefix}"`);

            for (let i = 0; i < Math.min(rows, 50); i++) {
                const row = page.locator(`tbody tr:has-text("${prefix}")`).first();
                if (!(await row.count())) break;

                // The delete UI is a form whose submit button is gated by a JS confirm modal
                // (data-confirm-modal). The modal-backdrop intercepts the click. Bypass the UI
                // confirmation entirely by *submitting the form directly* via page.evaluate.
                const submitted = await row.evaluate((tr) => {
                    const form = tr.querySelector('form[action*="Delete" i], form[asp-page-handler*="Delete" i], form button[type="submit"]')?.closest('form');
                    if (!form) return false;
                    // Detach any data-confirm-modal so the form submits without the dialog
                    form.removeAttribute('data-confirm-modal');
                    form.submit();
                    return true;
                }).catch(() => false);

                if (submitted) {
                    // form.submit() triggers navigation — wait for it cleanly so our next
                    // page.evaluate doesn't fire mid-navigation ("Execution context destroyed").
                    await page.waitForURL(/.*/, { timeout: 5000 }).catch(() => { });
                    await page.waitForLoadState('networkidle').catch(() => { });
                    await dismissAnyConfirmModal().catch(() => { });
                    deleted++;
                } else {
                    // Row had no recognizable delete form — skip
                    break;
                }
            }
            if (deleted > 0) console.log(`  ✓ Deleted ${deleted} companies with prefix "${prefix}"`);
        }

        console.log('✓ Test data cleanup complete');
    } catch (error) {
        console.error('⚠️  Error during cleanup:', error.message);
        // Cleanup is best-effort — never let it fail a run.
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
