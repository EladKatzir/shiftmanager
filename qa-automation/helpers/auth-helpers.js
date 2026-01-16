// @ts-check
const { expect } = require('@playwright/test');
const path = require('path');

// Load environment variables
try {
    require('dotenv').config({ path: path.join(__dirname, '..', '.env') });
} catch (e) {
    // dotenv not installed, rely on system environment variables
}

// Credentials from environment variables
const OWNER_EMAIL = process.env.OWNER_EMAIL || 'owner@test.com';
const OWNER_PASSWORD = process.env.OWNER_PASSWORD || '';

// Role-based credentials (from discovery report)
const ROLE_CREDENTIALS = {
    Owner: { email: process.env.OWNER_EMAIL || 'admin@local', password: process.env.OWNER_PASSWORD || 'admin123' },
    Director: { email: process.env.DIRECTOR_EMAIL || 'director@local', password: process.env.DIRECTOR_PASSWORD || 'director123' },
    Manager: { email: 'manager@test.com', password: 'Manager123!' },
    Assigner: { email: 'assigner@test.com', password: 'Assigner123!' },
    Employee: { email: 'employee@test.com', password: 'Employee123!' },
    Trainee: { email: 'trainee@test.com', password: 'Trainee123!' }
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

    // Submit and wait for navigation
    await Promise.all([
        page.waitForURL(url => !url.toString().includes('/Auth/Login'), { timeout: 10000 }),
        page.click('button[type="submit"]'),
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
    await Promise.all([
        page.waitForURL(url => !url.toString().includes('/Auth/Login')),
        page.click('button[type="submit"]')
    ]);
}

module.exports = {
    loginAsOwner,
    loginAsRole,
    isOwnerPasswordConfigured,
    getOwnerCredentials,
    OWNER_EMAIL,
    OWNER_PASSWORD
};
