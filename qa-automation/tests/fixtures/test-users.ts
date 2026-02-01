/**
 * Test Users and Scenarios for E2E Testing (B-041)
 *
 * This file defines the test users and scenarios created by TestDataSeed.cs
 * These credentials MUST match the C# TestDataSeed constants exactly.
 *
 * IMPORTANT:
 * - These credentials are for Development/Test environments ONLY
 * - Production databases NEVER receive test data
 * - See docs/TEST-DATA.md for the full test data strategy
 */

// =============================================================================
// Test User Credentials
// =============================================================================

export interface TestUser {
  email: string;
  password: string;
  role: string;
  displayName: string;
  grants: string[];
  description: string;
}

/**
 * Test users for E2E testing.
 * Each user has specific grants and is designed for testing specific scenarios.
 */
export const testUsers: Record<string, TestUser> = {
  owner: {
    email: 'test.owner@shifty.test',
    password: 'TestOwner123!',
    role: 'Owner',
    displayName: 'Test Owner',
    grants: ['system:*', 'admin:*', 'all'],
    description: 'Full system access - can do anything'
  },

  director: {
    email: 'test.director@shifty.test',
    password: 'TestDirector123!',
    role: 'Director',
    displayName: 'Test Director',
    grants: ['company:multi-test-a', 'company:multi-test-b', 'cross-company'],
    description: 'Multi-company access - can switch between companies'
  },

  manager: {
    email: 'test.manager@shifty.test',
    password: 'TestManager123!',
    role: 'Manager',
    displayName: 'Test Manager',
    grants: ['company:test-company-full', 'admin-hub', 'user-management', 'shift-management'],
    description: 'Single company management access'
  },

  member: {
    email: 'test.member@shifty.test',
    password: 'TestMember123!',
    role: 'Employee',
    displayName: 'Test Member',
    grants: ['personal-calendar', 'view-own-shifts'],
    description: 'Personal calendar access only'
  },

  assigner: {
    email: 'test.assigner@shifty.test',
    password: 'TestAssigner123!',
    role: 'Assigner',
    displayName: 'Test Assigner',
    grants: ['assign-shifts', 'assign-chores', 'view-schedules'],
    description: 'Can assign shifts and chores'
  },

  noGrants: {
    email: 'test.nogrants@shifty.test',
    password: 'TestNoGrants123!',
    role: 'Trainee',
    displayName: 'Test NoGrants',
    grants: [],
    description: 'Edge case - user with minimal permissions'
  }
};

// =============================================================================
// Test Company Scenarios
// =============================================================================

export interface TestCompany {
  name: string;
  slug: string;
  description: string;
  hasCalendarData: boolean;
}

/**
 * Test companies for different testing scenarios.
 */
export const testCompanies: Record<string, TestCompany> = {
  emptyCalendar: {
    name: 'Test Company Empty',
    slug: 'test-company-empty',
    description: 'No shifts, no assignments - for testing empty states',
    hasCalendarData: false
  },

  fullCalendar: {
    name: 'Test Company Full',
    slug: 'test-company-full',
    description: '30 days of shifts with various types - for testing pagination, filtering',
    hasCalendarData: true
  },

  multiCompanyA: {
    name: 'Multi Test A',
    slug: 'multi-test-a',
    description: 'First company for multi-company testing',
    hasCalendarData: false
  },

  multiCompanyB: {
    name: 'Multi Test B',
    slug: 'multi-test-b',
    description: 'Second company for multi-company testing',
    hasCalendarData: false
  }
};

// =============================================================================
// Test Scenarios
// =============================================================================

export interface TestScenario {
  name: string;
  users: string[];
  companies: string[];
  description: string;
}

/**
 * Pre-defined test scenarios combining users and companies.
 */
export const testScenarios: Record<string, TestScenario> = {
  singleCompanyWorkflow: {
    name: 'Single Company Workflow',
    users: ['manager', 'member', 'assigner'],
    companies: ['fullCalendar'],
    description: 'Tests typical workflows within a single company'
  },

  multiCompanyDirector: {
    name: 'Multi-Company Director',
    users: ['director'],
    companies: ['multiCompanyA', 'multiCompanyB'],
    description: 'Tests context switching between companies'
  },

  emptyStateHandling: {
    name: 'Empty State Handling',
    users: ['noGrants'],
    companies: ['emptyCalendar'],
    description: 'Tests empty calendar and minimal permissions'
  },

  adminWorkflow: {
    name: 'Admin Workflow',
    users: ['owner'],
    companies: ['fullCalendar', 'emptyCalendar'],
    description: 'Tests admin operations across companies'
  }
};

// =============================================================================
// Helper Functions
// =============================================================================

/**
 * Gets credentials for a specific test user.
 * @param userKey - Key from testUsers object
 * @returns User credentials { email, password }
 */
export function getCredentials(userKey: keyof typeof testUsers): { email: string; password: string } {
  const user = testUsers[userKey];
  if (!user) {
    throw new Error(`Unknown test user: ${userKey}`);
  }
  return { email: user.email, password: user.password };
}

/**
 * Gets the company slug for a test scenario.
 * @param scenarioKey - Key from testCompanies object
 * @returns Company slug
 */
export function getCompanySlug(scenarioKey: keyof typeof testCompanies): string {
  const company = testCompanies[scenarioKey];
  if (!company) {
    throw new Error(`Unknown test company: ${scenarioKey}`);
  }
  return company.slug;
}

/**
 * Checks if a user has a specific grant.
 * @param userKey - Key from testUsers object
 * @param grant - Grant string to check
 * @returns true if user has the grant
 */
export function userHasGrant(userKey: keyof typeof testUsers, grant: string): boolean {
  const user = testUsers[userKey];
  if (!user) return false;

  // Check for wildcard grants
  if (user.grants.includes('all') || user.grants.includes('system:*')) {
    return true;
  }

  return user.grants.includes(grant);
}

// =============================================================================
// Environment Variable Overrides
// =============================================================================

/**
 * Gets test user credentials, with environment variable override support.
 * Useful when running tests against different environments.
 */
export function getTestUserFromEnv(userKey: keyof typeof testUsers): { email: string; password: string } {
  const envPrefix = userKey.toUpperCase();
  const emailEnv = `TEST_${envPrefix}_EMAIL`;
  const passwordEnv = `TEST_${envPrefix}_PASSWORD`;

  const user = testUsers[userKey];

  return {
    email: process.env[emailEnv] || user.email,
    password: process.env[passwordEnv] || user.password
  };
}
