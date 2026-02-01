/**
 * ShiftManager Application Crawler
 * Phase 0: Application Discovery via Playwright
 *
 * This script discovers all routes in the ShiftManager application by:
 * 1. Logging in as each role (Owner, Director, Manager, etc.)
 * 2. Crawling navigation menus and links
 * 3. Building a comprehensive route map with role access information
 * 4. Outputting results to navigation-map.json
 *
 * Usage:
 *   npm install -D @playwright/test playwright
 *   node qa-automation/discovery/app-crawler.js
 *
 * Prerequisites:
 *   - Application running at http://localhost:5000
 *   - Environment variables configured (see .env.example)
 *   - Test users seeded in database
 */

const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');

// Load environment variables from .env file if present
try {
    require('dotenv').config({ path: path.join(__dirname, '..', '.env') });
} catch (e) {
    // dotenv not installed, rely on system environment variables
}

// Configuration - credentials loaded from environment variables
const CONFIG = {
    baseUrl: process.env.APP_URL || 'http://localhost:5000',
    timeout: 30000,
    credentials: {
        Owner: {
            email: process.env.OWNER_EMAIL || 'owner@test.com',
            password: process.env.OWNER_PASSWORD || ''
        },
        Director: {
            email: process.env.DIRECTOR_EMAIL || 'director@local',
            password: process.env.DIRECTOR_PASSWORD || ''
        },
        Manager: {
            email: process.env.MANAGER_EMAIL || 'manager@test.local',
            password: process.env.MANAGER_PASSWORD || ''
        },
        Assigner: {
            email: process.env.ASSIGNER_EMAIL || 'assigner@test.local',
            password: process.env.ASSIGNER_PASSWORD || ''
        },
        Employee: {
            email: process.env.EMPLOYEE_EMAIL || 'employee@test.local',
            password: process.env.EMPLOYEE_PASSWORD || ''
        },
        Trainee: {
            email: process.env.TRAINEE_EMAIL || 'trainee@test.local',
            password: process.env.TRAINEE_PASSWORD || ''
        },
    },
    outputDir: path.join(__dirname, '.'),
    reportsDir: path.join(__dirname, '..', 'reports'),
};

// Known routes from codebase analysis (supplementary to crawled data)
const KNOWN_ROUTES = {
    // Authentication (Anonymous)
    anonymous: [
        { path: '/Auth/Login', name: 'Login', method: 'GET' },
        { path: '/Auth/Signup', name: 'Signup', method: 'GET' },
        { path: '/Auth/ForgotPassword', name: 'Forgot Password', method: 'GET' },
        { path: '/Auth/GriffinCallback', name: 'Griffin ADFS Callback', method: 'GET' },
    ],

    // Owner-only routes (Policy: IsAdmin - Owner role only)
    Owner: [
        { path: '/Owner/Index', name: 'Owner Admin Panel', policy: 'IsAdmin' },
        { path: '/Owner/FeatureFlags', name: 'Feature Flags', policy: 'IsAdmin' },
        { path: '/Owner/DatabaseConsole', name: 'Database Console', policy: 'IsAdmin' },
        { path: '/Owner/SystemHealth', name: 'System Health', policy: 'IsAdmin' },
        { path: '/Owner/Backup', name: 'Database Backup', policy: 'IsAdmin' },
        { path: '/Owner/GameConfig', name: 'Game Configuration', policy: 'IsAdmin' },
        { path: '/Owner/GriffinConfig', name: 'Griffin ADFS Config', policy: 'IsAdmin' },
        { path: '/Owner/EmailConfig', name: 'Email Configuration', policy: 'IsAdmin' },
        { path: '/Owner/EmailTemplates', name: 'Email Templates', policy: 'IsAdmin' },
        { path: '/Owner/LanguageEditMode', name: 'Language Edit Mode', policy: 'IsAdmin' },
        { path: '/Owner/LanguageManagement', name: 'Language Management', policy: 'IsAdmin' },
        { path: '/Owner/DataLifecycle', name: 'Data Lifecycle', policy: 'IsAdmin' },
        { path: '/Owner/SelectCompany', name: 'Select Company (Multi-tenant)', policy: 'IsAdmin' },
        { path: '/Owner/ClearCompanySelection', name: 'Clear Company Selection', policy: 'IsAdmin' },
        { path: '/Admin/Companies', name: 'Companies Management', policy: 'IsAdmin' },
        { path: '/Admin/Directors', name: 'Directors Management', policy: 'IsAdmin' },
        { path: '/Diagnostic', name: 'System Diagnostic', policy: 'IsAdmin' },
        { path: '/GriffinDiagnostic', name: 'Griffin Diagnostic', policy: 'IsAdmin' },
    ],

    // Director routes (Policy: IsDirector - Owner + Director)
    Director: [
        { path: '/Director/Index', name: 'Director Hub', policy: 'IsDirector' },
        { path: '/Director/CompanyFilter', name: 'Company Filter', policy: 'IsDirector' },
        { path: '/Director/NotificationHub', name: 'Notification Hub', policy: 'IsDirector' },
        { path: '/Director/ViewAsMode', name: 'View As Manager Mode', policy: 'IsDirector' },
    ],

    // Manager/Admin routes (Policy: IsManagerOrAdmin - Owner + Director + Manager)
    Manager: [
        { path: '/Admin/Index', name: 'Admin Hub', policy: 'IsManagerOrAdmin' },
        { path: '/Admin/Config', name: 'Configuration', policy: 'IsManagerOrAdmin' },
        { path: '/Admin/Users', name: 'User Management', policy: 'IsManagerOrAdmin' },
        { path: '/Admin/ShiftTypes', name: 'Shift Types', policy: 'IsManagerOrAdmin' },
        { path: '/Admin/Analytics', name: 'Analytics Dashboard', policy: 'IsManagerOrAdmin' },
        { path: '/Admin/AuditLog', name: 'Audit Log', policy: 'IsManagerOrAdmin' },
        { path: '/Admin/EditProfile', name: 'Edit User Profile', policy: 'IsManagerOrAdmin' },
        { path: '/Calendar/Table', name: 'Shifts Management Table', policy: 'IsManagerOrAdmin' },
        { path: '/Chores/Calendar', name: 'Chores Calendar (Admin)', policy: 'IsManagerOrAdmin' },
        { path: '/Assignments/Manage', name: 'Assignments Management', policy: 'IsManagerOrAdmin' },
        { path: '/Requests/Index', name: 'Requests Management', policy: 'IsManagerOrAdmin' },
        { path: '/Owner/Programs', name: 'Programs', policy: 'IsManagerOrAdmin' },
        { path: '/Owner/MasterPrograms', name: 'Master Programs', policy: 'IsManagerOrAdmin' },
        { path: '/Owner/Blueprints', name: 'Blueprints (Shift Templates)', policy: 'IsManagerOrAdmin' },
    ],

    // Assigner routes (Policy: CanEditChores, CanEditOnDuty)
    Assigner: [
        // Assigners can edit chores and on-duty
        { path: '/Api/Calendar/QuickAddChore', name: 'Quick Add Chore API', policy: 'CanEditChores', method: 'POST' },
        { path: '/Api/Calendar/DeleteChore', name: 'Delete Chore API', policy: 'CanEditChores', method: 'POST' },
    ],

    // Employee routes (authenticated users)
    Employee: [
        { path: '/', name: 'Home (Redesigned)', policy: 'Authenticated' },
        { path: '/Home/Index', name: 'Home (Legacy)', policy: 'Authenticated' },
        { path: '/My/Index', name: 'My Dashboard', policy: 'Authenticated' },
        { path: '/My/Profile', name: 'My Profile', policy: 'Authenticated' },
        { path: '/My/Settings', name: 'My Settings', policy: 'Authenticated' },
        { path: '/My/Requests', name: 'My Requests', policy: 'Authenticated' },
        { path: '/My/NotificationCenter', name: 'Notification Center', policy: 'Authenticated' },
        { path: '/My/ApiKeys', name: 'API Keys', policy: 'Authenticated' },
        { path: '/MyTeam/Index', name: 'My Team', policy: 'Authenticated' },
        { path: '/Calendar/Month', name: 'Calendar (Month View)', policy: 'Authenticated' },
        { path: '/Calendar/Week', name: 'Calendar (Week View)', policy: 'Authenticated' },
        { path: '/Calendar/Day', name: 'Calendar (Day View)', policy: 'Authenticated' },
        { path: '/Schedule/Index', name: 'Schedule', policy: 'Authenticated' },
        { path: '/Public/Chores', name: 'Chores (Public View)', policy: 'CanViewChores' },
        { path: '/Public/OnDuty', name: 'On-Duty (Public View)', policy: 'CanViewOnDuty' },
        { path: '/Public/Feedback', name: 'Feedback', policy: 'Authenticated' },
        { path: '/Requests/Swaps/Create', name: 'Create Shift Swap', policy: 'Authenticated' },
        { path: '/Requests/TimeOff/Create', name: 'Create Time Off Request', policy: 'Authenticated' },
        { path: '/Game/Leaderboard', name: 'Game Leaderboard', policy: 'Authenticated' },
    ],

    // Trainee routes (same as Employee but limited write access)
    Trainee: [
        // Trainees have read-only access to most features
        // They can view shifts but cannot manage them
    ],

    // API Endpoints
    api: [
        { path: '/Api/SessionStatus', name: 'Session Status API', method: 'GET' },
        { path: '/Api/Localization', name: 'Localization API', method: 'GET' },
        { path: '/Api/Game/GetLeaderboard', name: 'Game Leaderboard API', method: 'GET' },
        { path: '/Api/Game/GetLocalization', name: 'Game Localization API', method: 'GET' },
        { path: '/Api/Game/GetConfiguration', name: 'Game Configuration API', method: 'GET' },
        { path: '/Api/Game/SaveScore', name: 'Save Game Score API', method: 'POST' },
        { path: '/Api/Calendar/QuickAddChore', name: 'Quick Add Chore API', method: 'POST' },
        { path: '/Api/Calendar/QuickAddOnDuty', name: 'Quick Add On-Duty API', method: 'POST' },
        { path: '/Api/Calendar/DeleteChore', name: 'Delete Chore API', method: 'POST' },
        { path: '/Api/Calendar/DeleteOnDuty', name: 'Delete On-Duty API', method: 'POST' },
    ],

    // System pages
    system: [
        { path: '/Error', name: 'Error Page', policy: 'Anonymous' },
        { path: '/AccessDenied', name: 'Access Denied', policy: 'Anonymous' },
        { path: '/Auth/Logout', name: 'Logout', method: 'POST', policy: 'Authenticated' },
    ],
};

// Role hierarchy for access calculation
const ROLE_HIERARCHY = {
    Owner: ['Owner', 'Director', 'Manager', 'Assigner', 'Employee', 'Trainee'],
    Director: ['Director', 'Manager', 'Assigner', 'Employee', 'Trainee'],
    Manager: ['Manager', 'Assigner', 'Employee', 'Trainee'],
    Assigner: ['Assigner', 'Employee', 'Trainee'],
    Employee: ['Employee', 'Trainee'],
    Trainee: ['Trainee'],
};

// Policy to role mapping
const POLICY_ROLES = {
    'IsAdmin': ['Owner'],
    'IsDirector': ['Owner', 'Director'],
    'IsOwnerOrDirector': ['Owner', 'Director'],
    'IsManagerOrAdmin': ['Owner', 'Director', 'Manager'],
    'CanEditChores': ['Owner', 'Director', 'Manager', 'Assigner'],
    'CanEditOnDuty': ['Owner', 'Director', 'Manager'],
    'CanViewChores': ['Owner', 'Director', 'Manager', 'Assigner', 'Employee', 'Trainee'],
    'CanViewOnDuty': ['Owner', 'Director', 'Manager', 'Assigner', 'Employee', 'Trainee'],
    'Authenticated': ['Owner', 'Director', 'Manager', 'Assigner', 'Employee', 'Trainee'],
    'Anonymous': [],
};

/**
 * Main crawler class
 */
class ApplicationCrawler {
    constructor() {
        this.browser = null;
        this.context = null;
        this.page = null;
        this.discoveredRoutes = new Map();
        this.crawlErrors = [];
        this.crawlStats = {
            startTime: null,
            endTime: null,
            pagesVisited: 0,
            linksDiscovered: 0,
            errorsEncountered: 0,
        };
    }

    async initialize() {
        console.log('Initializing Playwright browser...');
        try {
            this.browser = await chromium.launch({
                headless: true,
                args: ['--no-sandbox', '--disable-setuid-sandbox'],
            });
            this.context = await this.browser.newContext({
                viewport: { width: 1920, height: 1080 },
                userAgent: 'ShiftManager-QA-Crawler/1.0',
            });
            this.page = await this.context.newPage();
            this.page.setDefaultTimeout(CONFIG.timeout);
            console.log('Browser initialized successfully');
            return true;
        } catch (error) {
            console.error('Failed to initialize browser:', error.message);
            return false;
        }
    }

    async close() {
        if (this.browser) {
            await this.browser.close();
        }
    }

    async login(role) {
        const creds = CONFIG.credentials[role];
        if (!creds) {
            console.log(`No credentials for role: ${role}, skipping login`);
            return false;
        }

        console.log(`Attempting login as ${role} (${creds.email})...`);

        try {
            await this.page.goto(`${CONFIG.baseUrl}/Auth/Login`, { waitUntil: 'networkidle' });

            // Fill login form
            await this.page.fill('input[name="Email"], input#Email', creds.email);
            await this.page.fill('input[name="Password"], input#Password', creds.password);

            // Submit form
            await Promise.all([
                this.page.waitForNavigation({ waitUntil: 'networkidle' }),
                this.page.click('button[type="submit"]'),
            ]);

            // Check if login was successful by looking for auth indicators
            const currentUrl = this.page.url();
            if (currentUrl.includes('/Auth/Login')) {
                console.log(`Login failed for ${role} - still on login page`);
                return false;
            }

            console.log(`Successfully logged in as ${role}`);
            return true;
        } catch (error) {
            console.error(`Login error for ${role}:`, error.message);
            this.crawlErrors.push({ type: 'login', role, error: error.message });
            return false;
        }
    }

    async logout() {
        try {
            // Try to find and click logout button
            const logoutButton = await this.page.$('form[action*="Logout"] button, a[href*="Logout"]');
            if (logoutButton) {
                await logoutButton.click();
                await this.page.waitForLoadState('networkidle');
            }
        } catch (error) {
            // Navigate to login page to ensure clean state
            await this.page.goto(`${CONFIG.baseUrl}/Auth/Login`);
        }
    }

    async crawlNavigation() {
        console.log('Crawling navigation menus...');
        const routes = [];

        try {
            // Get all navigation links from sidebar
            const sidebarLinks = await this.page.$$eval(
                '.app-sidebar-nav a, nav a',
                (links) => links.map((link) => ({
                    text: link.textContent?.trim() || '',
                    href: link.href,
                    isActive: link.classList.contains('active'),
                }))
            );
            routes.push(...sidebarLinks);

            // Get admin tool cards if present
            const adminCards = await this.page.$$eval(
                '.admin-tool-card a, .admin-card a, .tool-card a',
                (links) => links.map((link) => ({
                    text: link.textContent?.trim() || '',
                    href: link.href,
                    type: 'admin-card',
                }))
            );
            routes.push(...adminCards);

            // Get command palette items if accessible
            try {
                await this.page.keyboard.press('Control+K');
                await this.page.waitForTimeout(500);
                const paletteItems = await this.page.$$eval(
                    '#commandPaletteResults a, .command-palette-results a',
                    (links) => links.map((link) => ({
                        text: link.textContent?.trim() || '',
                        href: link.href,
                        type: 'command-palette',
                    }))
                );
                routes.push(...paletteItems);
                await this.page.keyboard.press('Escape');
            } catch {
                // Command palette may not be available
            }

            console.log(`Found ${routes.length} navigation links`);
        } catch (error) {
            console.error('Error crawling navigation:', error.message);
            this.crawlErrors.push({ type: 'navigation', error: error.message });
        }

        return routes;
    }

    async discoverRoutesForRole(role) {
        console.log(`\n${'='.repeat(60)}`);
        console.log(`Discovering routes for role: ${role}`);
        console.log('='.repeat(60));

        const roleRoutes = {
            role,
            loginSuccess: false,
            discoveredLinks: [],
            accessiblePages: [],
            deniedPages: [],
        };

        // Try to login
        roleRoutes.loginSuccess = await this.login(role);

        if (roleRoutes.loginSuccess) {
            // Crawl navigation
            roleRoutes.discoveredLinks = await this.crawlNavigation();
            this.crawlStats.linksDiscovered += roleRoutes.discoveredLinks.length;

            // Test access to known routes
            const routesToTest = this.getRoutesForRole(role);
            for (const route of routesToTest) {
                try {
                    const response = await this.page.goto(`${CONFIG.baseUrl}${route.path}`, {
                        waitUntil: 'networkidle',
                        timeout: 10000,
                    });

                    const currentUrl = this.page.url();
                    const status = response?.status() || 0;

                    if (status === 200 && !currentUrl.includes('/AccessDenied') && !currentUrl.includes('/Auth/Login')) {
                        roleRoutes.accessiblePages.push({
                            ...route,
                            status,
                            actualUrl: currentUrl,
                        });
                    } else {
                        roleRoutes.deniedPages.push({
                            ...route,
                            status,
                            redirectedTo: currentUrl,
                        });
                    }

                    this.crawlStats.pagesVisited++;
                } catch (error) {
                    roleRoutes.deniedPages.push({
                        ...route,
                        error: error.message,
                    });
                    this.crawlStats.errorsEncountered++;
                }
            }

            await this.logout();
        }

        return roleRoutes;
    }

    getRoutesForRole(role) {
        const routes = [];

        // Add role-specific routes
        if (KNOWN_ROUTES[role]) {
            routes.push(...KNOWN_ROUTES[role]);
        }

        // Add routes accessible to this role based on hierarchy
        const accessibleRoles = ROLE_HIERARCHY[role] || [];
        for (const accessibleRole of accessibleRoles) {
            if (KNOWN_ROUTES[accessibleRole] && accessibleRole !== role) {
                routes.push(...KNOWN_ROUTES[accessibleRole]);
            }
        }

        return routes;
    }

    buildNavigationMap() {
        console.log('\nBuilding navigation map from codebase analysis...');

        const navigationMap = {
            metadata: {
                generatedAt: new Date().toISOString(),
                baseUrl: CONFIG.baseUrl,
                appVersion: '1.0.0',
                crawlStats: this.crawlStats,
            },
            roleHierarchy: ROLE_HIERARCHY,
            policyRoles: POLICY_ROLES,
            routes: {
                byCategory: {},
                byPolicy: {},
                byRole: {},
            },
            discoveredRoutes: Array.from(this.discoveredRoutes.values()),
            errors: this.crawlErrors,
        };

        // Organize routes by category
        for (const [category, routes] of Object.entries(KNOWN_ROUTES)) {
            navigationMap.routes.byCategory[category] = routes.map((r) => ({
                ...r,
                fullUrl: `${CONFIG.baseUrl}${r.path}`,
                accessibleBy: r.policy ? POLICY_ROLES[r.policy] || [] : [],
            }));
        }

        // Organize routes by policy
        for (const [policy, roles] of Object.entries(POLICY_ROLES)) {
            navigationMap.routes.byPolicy[policy] = {
                roles,
                routes: [],
            };
        }

        for (const routes of Object.values(KNOWN_ROUTES)) {
            for (const route of routes) {
                if (route.policy && navigationMap.routes.byPolicy[route.policy]) {
                    navigationMap.routes.byPolicy[route.policy].routes.push(route);
                }
            }
        }

        // Organize routes by role (what each role can access)
        for (const role of Object.keys(ROLE_HIERARCHY)) {
            navigationMap.routes.byRole[role] = [];

            for (const [policy, roles] of Object.entries(POLICY_ROLES)) {
                if (roles.includes(role)) {
                    const policyRoutes = navigationMap.routes.byPolicy[policy]?.routes || [];
                    navigationMap.routes.byRole[role].push(...policyRoutes);
                }
            }

            // Deduplicate
            const seen = new Set();
            navigationMap.routes.byRole[role] = navigationMap.routes.byRole[role].filter((r) => {
                const key = r.path;
                if (seen.has(key)) return false;
                seen.add(key);
                return true;
            });
        }

        return navigationMap;
    }

    async run() {
        console.log('Starting ShiftManager Application Crawler');
        console.log(`Target: ${CONFIG.baseUrl}`);
        console.log('='.repeat(60));

        this.crawlStats.startTime = new Date().toISOString();

        // Try to initialize browser
        const browserInitialized = await this.initialize();

        let crawlResults = {
            browserAvailable: browserInitialized,
            applicationReachable: false,
            roleDiscovery: {},
        };

        if (browserInitialized) {
            // Check if application is reachable
            try {
                const response = await this.page.goto(`${CONFIG.baseUrl}/Auth/Login`, {
                    waitUntil: 'networkidle',
                    timeout: 10000,
                });
                crawlResults.applicationReachable = response?.status() === 200;
                console.log(`Application reachable: ${crawlResults.applicationReachable}`);
            } catch (error) {
                console.log(`Application not reachable: ${error.message}`);
                this.crawlErrors.push({
                    type: 'connectivity',
                    error: error.message,
                    suggestion: 'Ensure application is running at ' + CONFIG.baseUrl,
                });
            }

            if (crawlResults.applicationReachable) {
                // Discover routes for each role with credentials
                for (const role of Object.keys(CONFIG.credentials)) {
                    crawlResults.roleDiscovery[role] = await this.discoverRoutesForRole(role);
                }
            }

            await this.close();
        }

        this.crawlStats.endTime = new Date().toISOString();

        // Build navigation map (works even without live crawling)
        const navigationMap = this.buildNavigationMap();
        navigationMap.crawlResults = crawlResults;

        // Save results
        await this.saveResults(navigationMap);

        return navigationMap;
    }

    async saveResults(navigationMap) {
        // Ensure directories exist
        if (!fs.existsSync(CONFIG.outputDir)) {
            fs.mkdirSync(CONFIG.outputDir, { recursive: true });
        }
        if (!fs.existsSync(CONFIG.reportsDir)) {
            fs.mkdirSync(CONFIG.reportsDir, { recursive: true });
        }

        // Save navigation map
        const outputPath = path.join(CONFIG.outputDir, 'navigation-map.json');
        fs.writeFileSync(outputPath, JSON.stringify(navigationMap, null, 2));
        console.log(`\nNavigation map saved to: ${outputPath}`);

        // Generate summary
        console.log('\n' + '='.repeat(60));
        console.log('CRAWL SUMMARY');
        console.log('='.repeat(60));
        console.log(`Total routes catalogued: ${this.countRoutes(navigationMap)}`);
        console.log(`Pages visited: ${this.crawlStats.pagesVisited}`);
        console.log(`Links discovered: ${this.crawlStats.linksDiscovered}`);
        console.log(`Errors encountered: ${this.crawlStats.errorsEncountered}`);
        console.log(`Crawl duration: ${this.calculateDuration()}`);
    }

    countRoutes(navigationMap) {
        let count = 0;
        for (const routes of Object.values(navigationMap.routes.byCategory)) {
            count += routes.length;
        }
        return count;
    }

    calculateDuration() {
        if (!this.crawlStats.startTime || !this.crawlStats.endTime) return 'N/A';
        const start = new Date(this.crawlStats.startTime);
        const end = new Date(this.crawlStats.endTime);
        const durationMs = end - start;
        const seconds = Math.floor(durationMs / 1000);
        return `${seconds} seconds`;
    }
}

// Run crawler
async function main() {
    const crawler = new ApplicationCrawler();
    try {
        await crawler.run();
        process.exit(0);
    } catch (error) {
        console.error('Fatal error:', error);
        process.exit(1);
    }
}

// Export for testing
module.exports = { ApplicationCrawler, KNOWN_ROUTES, POLICY_ROLES, ROLE_HIERARCHY };

// Run if executed directly
if (require.main === module) {
    main();
}
