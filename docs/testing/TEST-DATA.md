# Test Data Strategy (B-041)

## Overview

This document describes the test data strategy for ShiftManager E2E testing with Playwright.
The strategy ensures:

1. **Consistent test scenarios** - Every test run starts with the same known state
2. **Role-based test users** - Users for each permission level (Owner, Director, Manager, etc.)
3. **Scenario-specific data** - Empty calendar, full calendar, multi-company scenarios
4. **Environment isolation** - Test data NEVER appears in production
5. **Idempotent seeding** - Safe to run multiple times

## Test Users

All test users follow the pattern `test.<role>@shifty.test` and use secure passwords.

| Role | Email | Password | Company | Grants |
|------|-------|----------|---------|--------|
| **Owner** | test.owner@shifty.test | TestOwner123! | Test Company Full | Full system access |
| **Director** | test.director@shifty.test | TestDirector123! | Multi Test A | Multi-company access (A + B) |
| **Manager** | test.manager@shifty.test | TestManager123! | Test Company Full | Single company management |
| **Member** | test.member@shifty.test | TestMember123! | Test Company Full | Personal calendar only |
| **Assigner** | test.assigner@shifty.test | TestAssigner123! | Test Company Full | Shift/chore assignment |
| **NoGrants** | test.nogrants@shifty.test | TestNoGrants123! | Test Company Empty | Minimal permissions (edge case) |

## Test Companies / Scenarios

### Empty Calendar (`test-company-empty`)
- **Purpose:** Test empty states and edge cases
- **Data:** No shifts, no assignments
- **Users:** NoGrants user
- **Use cases:**
  - Empty calendar views
  - "No data" messaging
  - First-time user experience

### Full Calendar (`test-company-full`)
- **Purpose:** Test typical workflows with data
- **Data:** 30 days of shifts (15 days past, 15 days future), multiple shift types
- **Users:** Owner, Manager, Member, Assigner
- **Use cases:**
  - Calendar pagination
  - Shift filtering
  - Assignment workflows
  - Date navigation

### Multi-Company A & B (`multi-test-a`, `multi-test-b`)
- **Purpose:** Test context switching for directors
- **Data:** Basic company structure
- **Users:** Director (has access to both)
- **Use cases:**
  - Company switcher component
  - Cross-company data isolation
  - Director workflows

## Environment Isolation

Test data is **ONLY** seeded when:

```csharp
ASPNETCORE_ENVIRONMENT = "Development" OR "Test"
```

The seeder explicitly checks the environment and refuses to run in production:

```csharp
private static bool IsTestEnvironment()
{
    var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
    return env == "Development" || env == "Test";
}
```

## Using Test Data in Playwright Tests

### JavaScript/TypeScript Fixtures

Import the test users fixture:

```javascript
const { testUsers, getCredentials, testCompanies } = require('./fixtures/test-users');

// Get credentials for a specific user
const { email, password } = getCredentials('owner');

// Or access directly
const ownerEmail = testUsers.owner.email;
const ownerPassword = testUsers.owner.password;
```

### Environment Variable Overrides

You can override test credentials via environment variables:

```bash
# Override owner credentials
TEST_OWNER_EMAIL=custom.owner@test.com
TEST_OWNER_PASSWORD=CustomPassword123!

# Then in tests:
const { email, password } = getTestUserFromEnv('owner');
```

### Example Test Setup

```javascript
const { test, expect } = require('@playwright/test');
const { testUsers, getCredentials } = require('./fixtures/test-users');

test.describe('Manager Workflow', () => {
  test.beforeEach(async ({ page }) => {
    const { email, password } = getCredentials('manager');

    await page.goto('/Auth/Login');
    await page.fill('input[name="Email"]', email);
    await page.fill('input[name="Password"]', password);
    await page.click('button[type="submit"]');
    await page.waitForURL(url => !url.toString().includes('/Auth/Login'));
  });

  test('should see admin hub', async ({ page }) => {
    await page.goto('/Admin/Index');
    await expect(page.locator('h1')).toContainText('Admin');
  });
});
```

## Reset Strategy

Before each test run, the system ensures a known state:

1. **Database migrations** are applied
2. **Seed data** is re-applied (idempotent - checks for existing data)
3. **Test-specific data** is created fresh if missing

For a complete reset (clearing and recreating test data):

```csharp
await TestDataSeed.ResetTestDataAsync(context, logger);
```

## Shift Types Created

The full calendar company includes these shift types:

| Shift Type | Time | Color |
|------------|------|-------|
| Morning | 06:00 - 14:00 | Green (#4CAF50) |
| Afternoon | 14:00 - 22:00 | Blue (#2196F3) |
| Night | 22:00 - 06:00 | Purple (#9C27B0) |

## Files and Locations

| File | Purpose |
|------|---------|
| `Data/SeedData/TestDataSeed.cs` | C# seeder class |
| `qa-automation/tests/fixtures/test-users.js` | Playwright fixtures (JS) |
| `qa-automation/tests/fixtures/test-users.ts` | Playwright fixtures (TS) |
| `docs/TEST-DATA.md` | This documentation |

## Security Considerations

1. **Test passwords** are intentionally different from production patterns
2. **Test email domain** (`@shifty.test`) is not a real domain
3. **Environment check** prevents accidental seeding in production
4. **Credentials stored in fixtures** - never commit `.env` files with real credentials

## CI/CD Integration

For CI/CD pipelines, set up the test environment:

```yaml
# Example GitHub Actions step
- name: Seed test data
  env:
    ASPNETCORE_ENVIRONMENT: Test
  run: dotnet run --project ShiftManager -- --seed-test-data

- name: Run E2E tests
  run: npx playwright test
```

## Troubleshooting

### Tests fail to log in

1. Ensure `ASPNETCORE_ENVIRONMENT=Development` or `Test`
2. Verify seed data ran (check logs for "E2E test data seeding completed")
3. Check password matches exactly (case-sensitive)

### Missing calendar data

1. Run the application once in Development mode to trigger seeding
2. Check `ShiftInstances` table for test company data

### Director can't switch companies

1. Verify `DirectorCompany` mappings exist for both test companies
2. Check the director user has correct multi-company grants

## Future Enhancements

- [ ] Add API endpoints for test data reset
- [ ] Create Playwright fixtures for authenticated state persistence
- [ ] Add more complex calendar scenarios (conflicts, overlaps)
- [ ] Support for custom test data via JSON files
