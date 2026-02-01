// @ts-check
const { expect } = require('@playwright/test');
const path = require('path');

// Load environment variables
try {
    require('dotenv').config({ path: path.join(__dirname, '..', '.env') });
} catch (e) {
    // dotenv not installed, rely on system environment variables
}

// =============================================================================
// B-041: Test Data Strategy - E2E Test Users
// These are the primary test users created by TestDataSeed.cs
// =============================================================================

// E2E Test Users (from TestDataSeed.cs - B-041)
// These are the recommended users for new E2E tests
const E2E_TEST_USERS = {
    Owner: { email: 'test.owner@shifty.test', password: 'TestOwner123!' },
    Director: { email: 'test.director@shifty.test', password: 'TestDirector123!' },
    Manager: { email: 'test.manager@shifty.test', password: 'TestManager123!' },
    Member: { email: 'test.member@shifty.test', password: 'TestMember123!' },
    Assigner: { email: 'test.assigner@shifty.test', password: 'TestAssigner123!' },
    NoGrants: { email: 'test.nogrants@shifty.test', password: 'TestNoGrants123!' }
};

// Credentials from environment variables
// Default to test seeded Owner account (legacy - use E2E_TEST_USERS for new tests)
const OWNER_EMAIL = process.env.OWNER_EMAIL || 'admin@local';
const OWNER_PASSWORD = process.env.OWNER_PASSWORD || 'admin123';

// Role-based credentials (from TestDataSeeder.cs - legacy)
// All test users use password '123456'
// NOTE: For new tests, prefer E2E_TEST_USERS above (B-041)
const ROLE_CREDENTIALS = {
    Owner: { email: process.env.OWNER_EMAIL || 'admin@local', password: process.env.OWNER_PASSWORD || 'admin123' },
    Director: { email: process.env.DIRECTOR_EMAIL || 'director@test.com', password: process.env.DIRECTOR_PASSWORD || '123456' },
    Manager: { email: 'manager@test.com', password: '123456' },
    Assigner: { email: 'assigner@test.com', password: '123456' },
    Employee: { email: 'employee@test.com', password: '123456' },
    Trainee: { email: 'trainee@test.com', password: '123456' }
};

/**
 * Helper function to login as Owner
 * @param {import('@playwright/test').Page} page - Playwright page object
 * @returns {Promise<void>}
 */
async function loginAsOwner(page) {
    await page.goto('/Auth/Login');

    // Fill login form - the form uses Email field
    await page.fill('input[name="Email"], input#Email', OWNER_EMAIL);
    await page.fill('input[name="Password"], input#Password', OWNER_PASSWORD);

    // Submit the LOCAL login form (not the Griffin ADFS form)
    // Click the button within the form that has the Email/Password fields
    await Promise.all([
        page.waitForURL(url => !url.toString().includes('/Auth/Login'), { timeout: 10000 }),
        page.locator('form:has(input[name="Email"]) button[type="submit"]').click(),
    ]);

    // Verify login succeeded
    await expect(page).not.toHaveURL(/\/Auth\/Login/);
}

/**
 * Check if owner password is configured
 * @returns {boolean}
 */
function isOwnerPasswordConfigured() {
    return !!OWNER_PASSWORD;
}

/**
 * Get owner credentials (for tests that need direct access)
 * @returns {{ email: string, password: string }}
 */
function getOwnerCredentials() {
    return {
        email: OWNER_EMAIL,
        password: OWNER_PASSWORD
    };
}

/**
 * Login as a specific role
 * @param {import('@playwright/test').Page} page - Playwright page object
 * @param {string} role - Role name (Owner, Director, Manager, Assigner, Employee, Trainee)
 * @returns {Promise<void>}
 */
async function loginAsRole(page, role) {
    const creds = ROLE_CREDENTIALS[role];
    if (!creds) throw new Error(`Unknown role: ${role}`);

    await page.goto('/Auth/Login');
    await page.fill('input[name="Email"], input#Email', creds.email);
    await page.fill('input[name="Password"], input#Password', creds.password);

    // Submit the LOCAL login form (not the Griffin ADFS form)
    await Promise.all([
        page.waitForURL(url => !url.toString().includes('/Auth/Login'), { timeout: 10000 }),
        page.locator('form:has(input[name="Email"]) button[type="submit"]').click()
    ]);
}

// =============================================================================
// B-041: E2E Test User Helpers
// =============================================================================

/**
 * Login as a specific E2E test user (B-041 strategy)
 * These are the recommended test users for new Playwright tests
 * @param {import('@playwright/test').Page} page - Playwright page object
 * @param {string} role - Role name (Owner, Director, Manager, Member, Assigner, NoGrants)
 * @returns {Promise<void>}
 */
async function loginAsE2EUser(page, role) {
    const creds = E2E_TEST_USERS[role];
    if (!creds) throw new Error(`Unknown E2E test user role: ${role}. Valid roles: ${Object.keys(E2E_TEST_USERS).join(', ')}`);

    await page.goto('/Auth/Login');
    await page.fill('input[name="Email"], input#Email', creds.email);
    await page.fill('input[name="Password"], input#Password', creds.password);

    // Submit the LOCAL login form
    await Promise.all([
        page.waitForURL(url => !url.toString().includes('/Auth/Login'), { timeout: 10000 }),
        page.locator('form:has(input[name="Email"]) button[type="submit"]').click()
    ]);

    // Verify login succeeded
    await expect(page).not.toHaveURL(/\/Auth\/Login/);
}

/**
 * Get E2E test user credentials
 * @param {string} role - Role name (Owner, Director, Manager, Member, Assigner, NoGrants)
 * @returns {{ email: string, password: string }}
 */
function getE2EUserCredentials(role) {
    const creds = E2E_TEST_USERS[role];
    if (!creds) throw new Error(`Unknown E2E test user role: ${role}`);
    return { email: creds.email, password: creds.password };
}

module.exports = {
    // Legacy helpers (still work with existing test data)
    loginAsOwner,
    loginAsRole,
    isOwnerPasswordConfigured,
    getOwnerCredentials,
    OWNER_EMAIL,
    OWNER_PASSWORD,
    ROLE_CREDENTIALS,

    // B-041: New E2E test user helpers (recommended for new tests)
    loginAsE2EUser,
    getE2EUserCredentials,
    E2E_TEST_USERS
};
