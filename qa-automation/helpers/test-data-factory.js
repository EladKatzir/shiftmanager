// @ts-check
/**
 * Test Data Factory for ShiftManager QA Automation
 *
 * Provides methods for generating consistent, unique test data
 * for various entities in the ShiftManager application.
 */

/**
 * Generates unique identifiers for test data
 * @returns {string} A unique identifier based on timestamp and random string
 */
function generateUniqueId() {
    const timestamp = Date.now();
    const random = Math.random().toString(36).substring(2, 8);
    return `${timestamp}_${random}`;
}

/**
 * Generates a valid slug from a string
 * @param {string} name - The name to convert to a slug
 * @returns {string} A lowercase slug with only letters, numbers, and hyphens
 */
function generateSlug(name) {
    return name
        .toLowerCase()
        .replace(/[^a-z0-9]+/g, '-')
        .replace(/^-|-$/g, '')
        .substring(0, 50);
}

class TestDataFactory {
    /**
     * Generate company test data
     * @param {Object} overrides - Optional field overrides
     * @returns {Object} Company test data object
     */
    static generateCompany(overrides = {}) {
        const uniqueId = generateUniqueId();
        const baseName = overrides.name || `TestCompany_${uniqueId}`;

        return {
            name: baseName,
            slug: generateSlug(baseName),
            displayName: `Test Company ${uniqueId}`,
            // Default configuration values based on AppConfig defaults from Companies.cshtml.cs
            weeklyHoursLimit: 40,
            restHoursRequired: 8,
            ...overrides
        };
    }

    /**
     * Generate user test data
     * @param {string} role - User role (Owner, Director, Manager, Assigner, Employee, Trainee)
     * @param {Object} overrides - Optional field overrides
     * @returns {Object} User test data object with both spec properties (username, fullName)
     *                   and application properties (email, displayName)
     */
    static generateUser(role = 'Employee', overrides = {}) {
        const uniqueId = generateUniqueId();
        const validRoles = ['Owner', 'Director', 'Manager', 'Assigner', 'Employee', 'Trainee'];

        if (!validRoles.includes(role)) {
            throw new Error(`Invalid role: ${role}. Valid roles are: ${validRoles.join(', ')}`);
        }

        const emailValue = `user_${uniqueId}@test.local`;
        const nameValue = `Test ${role} ${uniqueId}`;

        return {
            // Spec-required properties:
            username: emailValue,
            fullName: nameValue,
            // Application-specific properties:
            email: emailValue,
            displayName: nameValue,
            password: 'TestPassword123!',
            role: role,
            isActive: true,
            ...overrides
        };
    }

    /**
     * Generate manager account data for company creation
     * @param {Object} overrides - Optional field overrides
     * @returns {Object} Manager account data
     */
    static generateManager(overrides = {}) {
        const uniqueId = generateUniqueId();

        return {
            email: `manager_${uniqueId}@test.local`,
            displayName: `Test Manager ${uniqueId}`,
            password: 'Manager123!',
            ...overrides
        };
    }

    /**
     * Generate shift type test data
     * @param {Object} overrides - Optional field overrides
     * @returns {Object} Shift type test data
     */
    static generateShiftType(overrides = {}) {
        const uniqueId = generateUniqueId();

        return {
            key: `SHIFT_${uniqueId}`.substring(0, 20).toUpperCase(),
            start: '08:00',
            end: '16:00',
            ...overrides
        };
    }

    /**
     * Generate test data for input validation testing
     * @returns {Object} Object containing various malicious/edge-case inputs
     */
    static getSecurityTestInputs() {
        return {
            xssAttempts: [
                '<script>alert("xss")</script>',
                '<img src=x onerror=alert("xss")>',
                '"><script>alert(document.cookie)</script>',
                "javascript:alert('xss')",
                '<svg onload=alert("xss")>',
            ],
            sqlInjectionAttempts: [
                "'; DROP TABLE Companies;--",
                "\" OR 1=1;--",
                "'; DELETE FROM Users WHERE '1'='1",
                "UNION SELECT * FROM Users--",
                "1'; EXEC xp_cmdshell('dir');--",
            ],
            pathTraversalAttempts: [
                '../../../etc/passwd',
                '..\\..\\..\\windows\\system32\\config\\sam',
                '%2e%2e%2f%2e%2e%2f%2e%2e%2fetc%2fpasswd',
                '....//....//....//etc/passwd',
            ],
            boundaryInputs: {
                empty: '',
                whitespace: '   ',
                veryLong: 'A'.repeat(1000),
                unicodeCharacters: 'Test\u0000\u0001\u0002Company',
                emojiInput: 'Test Company 123',
                rtlText: 'Company\u202E\u202Dtest',
            },
            specialCharacters: [
                '!@#$%^&*()_+-=[]{}|;:\'",.<>?/\\`~',
                'Company\nWith\nNewlines',
                'Company\tWith\tTabs',
                'Company\rWith\rCarriageReturns',
            ],
        };
    }

    /**
     * Generate complete company creation form data
     * @param {Object} options - Options for data generation
     * @param {boolean} options.withDirector - Whether to include director selection
     * @param {Object} options.companyOverrides - Company field overrides
     * @param {Object} options.managerOverrides - Manager field overrides
     * @returns {Object} Complete form data for company creation
     */
    static generateCompanyCreationData(options = {}) {
        const {
            withDirector = false,
            companyOverrides = {},
            managerOverrides = {},
        } = options;

        const company = this.generateCompany(companyOverrides);
        const result = {
            companyName: company.name,
            companySlug: company.slug,
            companyDisplayName: company.displayName,
        };

        if (!withDirector) {
            const manager = this.generateManager(managerOverrides);
            result.managerEmail = manager.email;
            result.managerDisplayName = manager.displayName;
            result.managerPassword = manager.password;
        }

        return result;
    }

    /**
     * Generate test data for rename company operation
     * @param {number} companyId - The company ID to rename
     * @param {Object} overrides - Optional field overrides
     * @returns {Object} Rename company form data
     */
    static generateRenameData(companyId, overrides = {}) {
        const uniqueId = generateUniqueId();

        return {
            companyId: companyId,
            newName: `RenamedCompany_${uniqueId}`,
            ...overrides
        };
    }

    /**
     * Clean up test data - generate names for entities created during tests
     * that should be identifiable for cleanup
     * @param {string} prefix - Prefix for test entity names
     * @returns {Object} Test data with cleanup-friendly naming
     */
    static generateCleanupFriendlyData(prefix = 'CLEANUP') {
        const uniqueId = generateUniqueId();
        const cleanupPrefix = `${prefix}_${uniqueId}`;

        return {
            company: this.generateCompany({ name: `${cleanupPrefix}_Company` }),
            user: this.generateUser('Employee', {
                email: `${cleanupPrefix.toLowerCase()}@cleanup.test`,
                displayName: `${cleanupPrefix} User`
            }),
            shiftType: this.generateShiftType({ key: cleanupPrefix.substring(0, 20).toUpperCase() }),
        };
    }
}

module.exports = TestDataFactory;
