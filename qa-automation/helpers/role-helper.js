// @ts-check
const { expect } = require('@playwright/test');
const path = require('path');

// Load environment variables
try {
    require('dotenv').config({ path: path.join(__dirname, '..', '.env') });
} catch (e) {
    // dotenv not installed, rely on system environment variables
}

/**
 * Role credentials from environment variables
 * Falls back to default test credentials if not set
 */
const ROLE_CREDENTIALS = {
    Owner: {
        email: process.env.OWNER_EMAIL || 'owner@test.com',
        password: process.env.OWNER_PASSWORD || ''
    },
    Director: {
        email: process.env.DIRECTOR_EMAIL || 'director@test.com',
        password: process.env.DIRECTOR_PASSWORD || ''
    },
    Manager: {
        email: process.env.MANAGER_EMAIL || 'manager@test.com',
        password: process.env.MANAGER_PASSWORD || ''
    },
    Assigner: {
        email: process.env.ASSIGNER_EMAIL || 'assigner@test.com',
        password: process.env.ASSIGNER_PASSWORD || ''
    },
    Employee: {
        email: process.env.EMPLOYEE_EMAIL || 'employee@test.com',
        password: process.env.EMPLOYEE_PASSWORD || ''
    },
    Trainee: {
        email: process.env.TRAINEE_EMAIL || 'trainee@test.com',
        password: process.env.TRAINEE_PASSWORD || ''
    }
};

/**
 * Role hierarchy levels for permission comparison
 * Higher number = more permissions
 */
const ROLE_HIERARCHY = {
    Owner: 6,
    Director: 5,
    Manager: 4,
    Assigner: 3,
    Employee: 2,
    Trainee: 1
};

/**
 * RoleHelper - Helper class for role-based access control testing
 * Provides methods to login as different roles and test authorization
 */
class RoleHelper {
    /**
     * Login as a specific role
     * @param {import('@playwright/test').Page} page - Playwright page object
     * @param {keyof typeof ROLE_CREDENTIALS} role - The role to login as
     * @returns {Promise<void>}
     */
    static async loginAs(page, role) {
        const credentials = ROLE_CREDENTIALS[role];

        if (!credentials) {
            throw new Error(`Unknown role: ${role}. Valid roles are: ${Object.keys(ROLE_CREDENTIALS).join(', ')}`);
        }

        if (!credentials.password) {
            throw new Error(`Password not configured for role: ${role}. Set ${role.toUpperCase()}_PASSWORD in environment variables.`);
        }

        await page.goto('/Auth/Login');

        // Fill login form - uses Email field based on app structure
        await page.fill('input[name="Email"], input#Email', credentials.email);
        await page.fill('input[name="Password"], input#Password', credentials.password);

        // Submit and wait for navigation
        await Promise.all([
            page.waitForURL(url => !url.toString().includes('/Auth/Login'), { timeout: 10000 }),
            page.click('button[type="submit"]'),
        ]);

        // Verify login succeeded
        await expect(page).not.toHaveURL(/\/Auth\/Login/);
    }

    /**
     * Check if credentials are configured for a specific role
     * @param {keyof typeof ROLE_CREDENTIALS} role - The role to check
     * @returns {boolean}
     */
    static isRoleConfigured(role) {
        const credentials = ROLE_CREDENTIALS[role];
        return !!(credentials && credentials.password);
    }

    /**
     * Get configured role credentials (for tests that need direct access)
     * @param {keyof typeof ROLE_CREDENTIALS} role - The role to get credentials for
     * @returns {{ email: string, password: string }}
     */
    static getCredentials(role) {
        const credentials = ROLE_CREDENTIALS[role];
        if (!credentials) {
            throw new Error(`Unknown role: ${role}`);
        }
        return credentials;
    }

    /**
     * Attempt to access a URL and return the HTTP status code
     * Useful for testing unauthorized access scenarios
     * @param {import('@playwright/test').Page} page - Playwright page object
     * @param {string} url - The URL to access
     * @returns {Promise<number>} The HTTP status code
     */
    static async attemptUnauthorizedAction(page, url) {
        const response = await page.goto(url);
        return response?.status() ?? 0;
    }

    /**
     * Check if current page is Access Denied or Login redirect
     * @param {import('@playwright/test').Page} page - Playwright page object
     * @returns {Promise<boolean>}
     */
    static async isAccessDenied(page) {
        const currentUrl = page.url();
        return currentUrl.includes('/AccessDenied') ||
               currentUrl.includes('/Auth/Login') ||
               currentUrl.includes('/Account/AccessDenied');
    }

    /**
     * Verify that a role can access a specific page
     * @param {import('@playwright/test').Page} page - Playwright page object
     * @param {string} url - The URL to access
     * @returns {Promise<boolean>} True if access was granted
     */
    static async canAccessPage(page, url) {
        const response = await page.goto(url);
        const status = response?.status() ?? 0;
        const isRedirectedToLogin = page.url().includes('/Auth/Login');
        const isAccessDenied = await this.isAccessDenied(page);

        return status === 200 && !isRedirectedToLogin && !isAccessDenied;
    }

    /**
     * Get all available roles
     * @returns {string[]} Array of role names
     */
    static getAllRoles() {
        return Object.keys(ROLE_CREDENTIALS);
    }

    /**
     * Get roles that have lower or equal permission level than specified role
     * @param {keyof typeof ROLE_HIERARCHY} role - The reference role
     * @returns {string[]} Array of role names with lower or equal permissions
     */
    static getRolesAtOrBelowLevel(role) {
        const level = ROLE_HIERARCHY[role];
        return Object.entries(ROLE_HIERARCHY)
            .filter(([_, lvl]) => lvl <= level)
            .map(([r]) => r);
    }

    /**
     * Get roles that have higher permission level than specified role
     * @param {keyof typeof ROLE_HIERARCHY} role - The reference role
     * @returns {string[]} Array of role names with higher permissions
     */
    static getRolesAboveLevel(role) {
        const level = ROLE_HIERARCHY[role];
        return Object.entries(ROLE_HIERARCHY)
            .filter(([_, lvl]) => lvl > level)
            .map(([r]) => r);
    }

    /**
     * Logout from the current session
     * @param {import('@playwright/test').Page} page - Playwright page object
     * @returns {Promise<void>}
     */
    static async logout(page) {
        // Find and click logout form/button
        const logoutForm = page.locator('form[action*="Logout"]');
        const logoutExists = await logoutForm.isVisible().catch(() => false);

        if (logoutExists) {
            await Promise.all([
                page.waitForURL(/\/Auth\/Login/),
                logoutForm.locator('button[type="submit"]').click(),
            ]);
        } else {
            // Fallback: navigate directly to login page
            await page.goto('/Auth/Login');
        }
    }

    /**
     * Submit a form via API with CSRF token
     * Useful for testing backend authorization
     * @param {import('@playwright/test').Page} page - Playwright page object
     * @param {string} url - The form action URL
     * @param {Object} formData - The form data to submit
     * @returns {Promise<import('@playwright/test').APIResponse>}
     */
    static async submitFormWithCsrf(page, url, formData) {
        // Get CSRF token from page
        const csrfToken = await page.locator('input[name="__RequestVerificationToken"]')
            .getAttribute('value');

        // Submit form via API
        return page.request.post(url, {
            form: {
                __RequestVerificationToken: csrfToken || '',
                ...formData
            }
        });
    }
}

module.exports = {
    RoleHelper,
    ROLE_CREDENTIALS,
    ROLE_HIERARCHY
};
