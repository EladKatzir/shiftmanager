# QA Test Failures Remediation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Fix all 52 failing QA automation tests and underlying application issues

**Architecture:** Multi-layered approach addressing test infrastructure (missing test data), application bugs (500 errors, modal issues, validation gaps), and test implementation issues (selectors, timeouts)

**Tech Stack:** ASP.NET Core, Playwright, SQL Server/SQLite, C#, JavaScript

---

## Problem Summary

After running the Playwright test suite, 52 out of 92 tests failed. The failures break down into these categories:

### 1. **Test Data Infrastructure Issues (35 failures)**
**Root Cause:** Test users (Director, Manager, Employee, etc.) don't exist in database
- All `users-crud-rbac.spec.js` tests fail with login timeout
- `shift-assignment-workflow.spec.js` tests fail
- `session-resilience.spec.js` tests fail
- Error: `TimeoutError: page.waitForURL: Timeout 10000ms exceeded` at `role-helper.js:92`

**Why:** The tests assume test users exist with credentials:
- `director@test.com` / `123456`
- `manager@test.com` / `123456`
- `employee@test.com` / `123456`
- `assigner@test.com` / `123456`
- `trainee@test.com` / `123456`

### 2. **Application Bugs (11 failures)**
**Root Cause:** Actual bugs in the application code

**Bug 2.1: HTTP 500 Error on /Admin/Users**
- Detected by: `network-performance.spec.js:234` (P9-06)
- Log: `500 http://localhost:5000/Admin/Users`
- Impact: Admin page crashes under certain conditions

**Bug 2.2: Duplicate API Requests**
- Detected by: `network-performance.spec.js:114` (P9-03)
- Finding: 6 duplicate requests detected on Calendar/Table page
- Impact: Unnecessary network overhead, poor performance

**Bug 2.3: Modal Not Opening**
- Detected by: `companies-crud.spec.js:282` (P2-09), `companies-crud.spec.js:339` (P2-11), `companies-crud.spec.js:401` (P2-13)
- Selector: `#renameModal.active` not found
- Impact: Rename/delete functionality may be broken

**Bug 2.4: Validation Gaps**
- Invalid slug format not rejected: `companies-crud.spec.js:206` (P2-05)
- Path traversal not blocked: `companies-crud.spec.js:510` (P2-17)
- Impact: Security vulnerabilities

**Bug 2.5: XSS Sanitization Failure**
- Detected by: `multi-tenancy-isolation-network.spec.js:84` (P6-03)
- Impact: XSS vulnerability

**Bug 2.6: Slow Endpoints**
- Detected by: `network-performance.spec.js:200` (P9-05)
- Impact: Poor user experience

**Bug 2.7: Missing Authentication Headers**
- Detected by: `multi-tenancy-isolation-network.spec.js:112` (P6-04)
- Impact: Security concern

### 3. **Font Loading Issue (1 failure)**
- Test: `air-gapped-simulation.spec.js:92` (P8-04)
- Expected: Graceful degradation when fonts fail to load
- Impact: Minor UX issue

### 4. **Multi-Tenancy UI Issues (3 failures)**
- Tests fail due to login timeout (relates to missing test data)

### 5. **Session Management Issues (2 failures)**
- Session timeout test fails
- Browser back button test fails

---

## Task 1: Create Test Data Seeding Infrastructure

**Files:**
- Create: `Data/TestDataSeeder.cs`
- Modify: `Program.cs` (add seeding call)

**Step 1: Write TestDataSeeder class**

Create `Data/TestDataSeeder.cs`:

```csharp
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;

namespace ShiftManager.Data
{
    public class TestDataSeeder
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ILogger<TestDataSeeder> _logger;

        public TestDataSeeder(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            ILogger<TestDataSeeder> logger)
        {
            _context = context;
            _userManager = userManager;
            _logger = logger;
        }

        public async Task SeedTestUsersAsync()
        {
            // Only seed in development environment
            var isDevelopment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development";
            if (!isDevelopment)
            {
                _logger.LogInformation("Skipping test data seeding - not in Development environment");
                return;
            }

            _logger.LogInformation("Starting test data seeding...");

            // Create test company
            var testCompany = await EnsureTestCompanyExists();

            // Create test users with different roles
            await CreateTestUser("director@test.com", "123456", "Director", "Test Director", testCompany.CompanyId);
            await CreateTestUser("manager@test.com", "123456", "Manager", "Test Manager", testCompany.CompanyId);
            await CreateTestUser("employee@test.com", "123456", "Employee", "Test Employee", testCompany.CompanyId);
            await CreateTestUser("assigner@test.com", "123456", "Assigner", "Test Assigner", testCompany.CompanyId);
            await CreateTestUser("trainee@test.com", "123456", "Trainee", "Test Trainee", testCompany.CompanyId);

            _logger.LogInformation("Test data seeding completed");
        }

        private async Task<Company> EnsureTestCompanyExists()
        {
            var testCompany = await _context.Companies
                .FirstOrDefaultAsync(c => c.CompanySlug == "test-company");

            if (testCompany == null)
            {
                testCompany = new Company
                {
                    CompanyName = "Test Company",
                    CompanySlug = "test-company",
                    IsActive = true
                };
                _context.Companies.Add(testCompany);
                await _context.SaveChangesAsync();
                _logger.LogInformation("Created test company: {CompanySlug}", testCompany.CompanySlug);
            }

            return testCompany;
        }

        private async Task CreateTestUser(string email, string password, string role, string displayName, int companyId)
        {
            var existingUser = await _userManager.FindByEmailAsync(email);
            if (existingUser != null)
            {
                _logger.LogInformation("Test user already exists: {Email}", email);
                return;
            }

            var user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                DisplayName = displayName,
                CompanyId = companyId,
                IsActive = true
            };

            var result = await _userManager.CreateAsync(user, password);
            if (result.Succeeded)
            {
                // Assign role based on RoleType enum
                user.Role = Enum.Parse<RoleType>(role);
                await _context.SaveChangesAsync();
                _logger.LogInformation("Created test user: {Email} with role {Role}", email, role);
            }
            else
            {
                _logger.LogError("Failed to create test user {Email}: {Errors}",
                    email, string.Join(", ", result.Errors.Select(e => e.Description)));
            }
        }
    }
}
```

**Step 2: Add seeding call to Program.cs**

Modify `Program.cs` to call seeding after application starts:

```csharp
// After app.MapControllers() and before app.Run()

// Seed test data in development
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<ApplicationDbContext>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var logger = services.GetRequiredService<ILogger<TestDataSeeder>>();

        var seeder = new TestDataSeeder(context, userManager, logger);
        await seeder.SeedTestUsersAsync();
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred while seeding test data");
    }
}
```

**Step 3: Restart application and verify**

Run:
```bash
cd ..
dotnet run
```

Expected: Log messages showing test users created

**Step 4: Run failing RBAC tests to verify**

Run:
```bash
cd qa-automation
npx playwright test tests/users-crud-rbac.spec.js --headed
```

Expected: Tests should now pass login phase

**Step 5: Commit**

```bash
git add Data/TestDataSeeder.cs Program.cs
git commit -m "feat(testing): Add test data seeder for QA automation

- Create TestDataSeeder class to populate test users
- Add test users: director, manager, employee, assigner, trainee
- Only seeds in Development environment
- Fixes 35 failing tests that require role-based authentication

Co-Authored-By: Claude Sonnet 4.5 <noreply@anthropic.com>"
```

---

## Task 2: Fix HTTP 500 Error on /Admin/Users

**Files:**
- Investigate: `Controllers/AdminController.cs` (Users action)
- Investigate: `Views/Admin/Users.cshtml`
- Fix: Based on findings

**Step 1: Run application and reproduce error**

Run:
```bash
dotnet run
```

Open browser to `http://localhost:5000/Admin/Users` (login as admin@local first)

**Step 2: Check application logs**

Check console output for exception details

**Step 3: Investigate AdminController.Users action**

Read the controller to identify the bug:
```bash
# Use Read tool on Controllers/AdminController.cs
```

**Step 4: Fix the identified bug**

Common causes of 500 errors:
- Null reference exception
- Missing data in eager loading
- Query that fails due to missing relationship

Fix the code based on findings.

**Step 5: Test the fix**

Run:
```bash
dotnet run
```

Navigate to /Admin/Users and verify no 500 error

**Step 6: Run performance test**

Run:
```bash
cd qa-automation
npx playwright test tests/network-performance.spec.js:234
```

Expected: No 500 errors detected

**Step 7: Commit**

```bash
git add Controllers/AdminController.cs
git commit -m "fix(admin): Resolve 500 error on /Admin/Users endpoint

- Fix null reference/query issue in Users action
- Add proper null checks and eager loading
- Resolves P9-06 test failure

Co-Authored-By: Claude Sonnet 4.5 <noreply@anthropic.com>"
```

---

## Task 3: Fix Modal Issues (Rename/Delete)

**Files:**
- Investigate: `Views/Admin/Index.cshtml` (Companies view)
- Investigate: `wwwroot/js/site.js` (modal JavaScript)

**Step 1: Check modal implementation**

Read the Companies view to see how modals are triggered:
```bash
# Use Read tool on Views/Admin/Index.cshtml
```

**Step 2: Check if modal uses custom CSS classes**

Search for modal activation:
```bash
# Use Grep tool to search for "renameModal" in Views and wwwroot
```

**Step 3: Identify the issue**

Common problems:
- Modal uses custom class not `.active` (maybe `.show` or `.open`)
- Modal ID is different
- JavaScript not properly adding active class

**Step 4: Update test selectors OR fix application code**

If application uses `.show` instead of `.active`:

Option A: Fix tests (update selector in `companies-crud.spec.js`)
Option B: Fix app (ensure modal adds `.active` class)

**Step 5: Test manually**

Run app and click Rename button, verify modal opens

**Step 6: Run failing tests**

Run:
```bash
cd qa-automation
npx playwright test tests/companies-crud.spec.js:282 tests/companies-crud.spec.js:339 tests/companies-crud.spec.js:401
```

Expected: Tests pass

**Step 7: Commit**

```bash
git add Views/Admin/Index.cshtml wwwroot/js/site.js
git commit -m "fix(ui): Fix modal activation for rename/delete operations

- Ensure modal adds .active class when shown
- Fix JavaScript modal trigger logic
- Resolves P2-09, P2-11, P2-13 test failures

Co-Authored-By: Claude Sonnet 4.5 <noreply@anthropic.com>"
```

---

## Task 4: Fix Slug Validation

**Files:**
- Modify: `Views/Admin/Index.cshtml` (company form)
- Modify: `Controllers/AdminController.cs` (company creation action)

**Step 1: Read current validation**

Check the form input for CompanySlug:
```bash
# Use Read tool on Views/Admin/Index.cshtml, search for CompanySlug
```

**Step 2: Ensure HTML5 pattern validation exists**

The input should have:
```html
<input name="CompanySlug" pattern="[a-z0-9-]+" required />
```

**Step 3: Add server-side validation**

In the controller POST action, add validation:
```csharp
if (!Regex.IsMatch(model.CompanySlug, @"^[a-z0-9-]+$"))
{
    ModelState.AddModelError("CompanySlug", "Slug must contain only lowercase letters, numbers, and hyphens");
    return View(model);
}
```

**Step 4: Test validation**

Run:
```bash
cd qa-automation
npx playwright test tests/companies-crud.spec.js:206
```

Expected: Test passes (invalid slugs rejected)

**Step 5: Commit**

```bash
git add Views/Admin/Index.cshtml Controllers/AdminController.cs
git commit -m "fix(validation): Enforce slug format validation

- Add pattern validation for company slug
- Ensure only lowercase letters, numbers, hyphens allowed
- Add server-side validation as defense in depth
- Resolves P2-05 test failure

Co-Authored-By: Claude Sonnet 4.5 <noreply@anthropic.com>"
```

---

## Task 5: Fix Path Traversal Validation

**Files:**
- Modify: `Controllers/AdminController.cs` (company creation)

**Step 1: Add path traversal detection**

In company creation action:
```csharp
// Reject path traversal attempts
if (model.CompanySlug.Contains("..") ||
    model.CompanySlug.Contains("/") ||
    model.CompanySlug.Contains("\\"))
{
    ModelState.AddModelError("CompanySlug", "Invalid slug format");
    return View(model);
}
```

**Step 2: Run test**

Run:
```bash
cd qa-automation
npx playwright test tests/companies-crud.spec.js:510
```

Expected: Test passes

**Step 3: Commit**

```bash
git add Controllers/AdminController.cs
git commit -m "fix(security): Block path traversal in company slug

- Reject slugs containing ../, /, or \
- Prevent directory traversal attacks
- Resolves P2-17 test failure

Co-Authored-By: Claude Sonnet 4.5 <noreply@anthropic.com>"
```

---

## Task 6: Fix XSS Sanitization

**Files:**
- Investigate: `Controllers/AdminController.cs`
- Investigate: Response encoding in views

**Step 1: Read the failing test**

```bash
# Use Read tool on qa-automation/tests/multi-tenancy-isolation-network.spec.js:84
```

**Step 2: Identify what XSS attack is tested**

The test likely sends `<script>alert('xss')</script>` in a form field

**Step 3: Ensure proper output encoding**

In Razor views, use `@` prefix (not `@Html.Raw`) for user input:
```razor
<td>@Model.CompanyName</td>  <!-- Safe, encoded -->
<!-- NOT: @Html.Raw(Model.CompanyName) -->
```

**Step 4: Add input sanitization**

Use `HtmlEncoder` for additional safety:
```csharp
using System.Text.Encodings.Web;

// In controller
var sanitized = HtmlEncoder.Default.Encode(model.CompanyName);
```

**Step 5: Run test**

Run:
```bash
cd qa-automation
npx playwright test tests/multi-tenancy-isolation-network.spec.js:84
```

Expected: XSS payload is sanitized

**Step 6: Commit**

```bash
git add Controllers/AdminController.cs Views/Admin/Index.cshtml
git commit -m "fix(security): Ensure XSS sanitization in responses

- Add HTML encoding for user input
- Remove any @Html.Raw usage for untrusted data
- Resolves P6-03 test failure

Co-Authored-By: Claude Sonnet 4.5 <noreply@anthropic.com>"
```

---

## Task 7: Fix Duplicate API Requests

**Files:**
- Investigate: `wwwroot/js/site.js`
- Investigate: `Views/Calendar/Table.cshtml`

**Step 1: Enable network logging in test**

Already done - test detected 6 duplicate requests

**Step 2: Identify duplicate requests**

Check test output or performance report:
```bash
# Use Read tool on qa-automation/reports/efficiency-report.json
```

**Step 3: Find duplicate API call sources**

Common causes:
- Multiple event listeners attached
- Data loaded twice (once on page load, once via AJAX)
- Polling that overlaps

**Step 4: Fix the duplicate calls**

Remove redundant API calls or add deduplication logic

**Step 5: Run test**

Run:
```bash
cd qa-automation
npx playwright test tests/network-performance.spec.js:114
```

Expected: Fewer than 5 duplicates

**Step 6: Commit**

```bash
git add wwwroot/js/site.js Views/Calendar/Table.cshtml
git commit -m "perf(network): Eliminate duplicate API requests

- Remove redundant data fetching
- Add request deduplication
- Resolves P9-03 test failure

Co-Authored-By: Claude Sonnet 4.5 <noreply@anthropic.com>"
```

---

## Task 8: Fix Slow Endpoints

**Files:**
- Investigate: Application logs and performance report
- Optimize: Controllers and database queries

**Step 1: Read performance report**

```bash
# Use Read tool on qa-automation/reports/efficiency-report.json
```

**Step 2: Identify slow endpoints**

Look for endpoints taking > 1000ms

**Step 3: Add database query optimization**

Common fixes:
- Add eager loading: `.Include(x => x.RelatedEntity)`
- Add indexes to frequently queried columns
- Use projection: `.Select(x => new { x.Id, x.Name })`

**Step 4: Run test**

Run:
```bash
cd qa-automation
npx playwright test tests/network-performance.spec.js:200
```

Expected: All endpoints respond < 1000ms

**Step 5: Commit**

```bash
git add Controllers/* Data/ApplicationDbContext.cs
git commit -m "perf(database): Optimize slow endpoints

- Add eager loading for related entities
- Add database indexes
- Use query projection to reduce payload
- Resolves P9-05 test failure

Co-Authored-By: Claude Sonnet 4.5 <noreply@anthropic.com>"
```

---

## Task 9: Fix Authentication Header Issues

**Files:**
- Investigate: `Controllers/*Controller.cs`
- Investigate: Middleware configuration

**Step 1: Read failing test**

```bash
# Use Read tool on qa-automation/tests/multi-tenancy-isolation-network.spec.js:112
```

**Step 2: Determine what header is missing**

Test likely checks for:
- `Authorization` header
- Custom tenant header
- Anti-forgery token

**Step 3: Add missing header**

Ensure API responses include proper authentication context

**Step 4: Run test**

Run:
```bash
cd qa-automation
npx playwright test tests/multi-tenancy-isolation-network.spec.js:112
```

Expected: Test passes

**Step 5: Commit**

```bash
git add Controllers/* Program.cs
git commit -m "fix(auth): Add proper authentication headers to requests

- Ensure Authorization header is set
- Add tenant context to headers
- Resolves P6-04 test failure

Co-Authored-By: Claude Sonnet 4.5 <noreply@anthropic.com>"
```

---

## Task 10: Fix Font Loading Graceful Degradation

**Files:**
- Modify: `wwwroot/css/site.css`

**Step 1: Add fallback fonts**

Ensure all font-family declarations have fallbacks:
```css
body {
    font-family: 'CustomFont', Arial, sans-serif;
}
```

**Step 2: Add @font-face error handling**

```css
@font-face {
    font-family: 'CustomFont';
    src: url('/fonts/custom.woff2') format('woff2');
    font-display: swap; /* Shows fallback while loading */
}
```

**Step 3: Run test**

Run:
```bash
cd qa-automation
npx playwright test tests/air-gapped-simulation.spec.js:92
```

Expected: Test passes

**Step 4: Commit**

```bash
git add wwwroot/css/site.css
git commit -m "fix(ui): Add graceful font loading degradation

- Add fallback fonts to all font-family declarations
- Use font-display: swap for @font-face
- Resolves P8-04 test failure

Co-Authored-By: Claude Sonnet 4.5 <noreply@anthropic.com>"
```

---

## Task 11: Fix Session Timeout Handling

**Files:**
- Investigate: `Middleware/*` or session configuration
- Investigate: `wwwroot/js/session-check.js`

**Step 1: Read failing test**

```bash
# Use Read tool on qa-automation/tests/session-resilience.spec.js:7
```

**Step 2: Check session timeout configuration**

Ensure session timeout is configured and enforced

**Step 3: Add session timeout redirect**

Ensure expired sessions redirect to login page

**Step 4: Run test**

Run:
```bash
cd qa-automation
npx playwright test tests/session-resilience.spec.js:7
```

Expected: Test passes

**Step 5: Commit**

```bash
git add Program.cs Middleware/* wwwroot/js/session-check.js
git commit -m "fix(auth): Implement proper session timeout handling

- Add session timeout detection
- Redirect to login on session expiry
- Resolves P7-01 test failure

Co-Authored-By: Claude Sonnet 4.5 <noreply@anthropic.com>"
```

---

## Task 12: Fix Browser Back Button State

**Files:**
- Investigate: Form submission flow
- Add: Cache-Control headers

**Step 1: Read failing test**

```bash
# Use Read tool on qa-automation/tests/session-resilience.spec.js:35
```

**Step 2: Add no-cache headers for POST responses**

Prevent browser from caching POST results:
```csharp
Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
Response.Headers["Pragma"] = "no-cache";
Response.Headers["Expires"] = "0";
```

**Step 3: Implement Post-Redirect-Get pattern**

After POST, redirect to GET page:
```csharp
// Instead of returning View after POST
return RedirectToAction("Index");
```

**Step 4: Run test**

Run:
```bash
cd qa-automation
npx playwright test tests/session-resilience.spec.js:35
```

Expected: Test passes

**Step 5: Commit**

```bash
git add Controllers/*
git commit -m "fix(ui): Fix browser back button after form submit

- Implement Post-Redirect-Get pattern
- Add cache-control headers to prevent caching
- Resolves P7-03 test failure

Co-Authored-By: Claude Sonnet 4.5 <noreply@anthropic.com>"
```

---

## Task 13: Fix Multi-Tenancy UI Issues

**Files:**
- Already fixed by Task 1 (test data seeding)

**Step 1: Run multi-tenancy tests**

Run:
```bash
cd qa-automation
npx playwright test tests/multi-tenancy-isolation-ui.spec.js
```

Expected: Tests pass now that test users exist

**Step 2: If still failing, investigate company assignment**

Ensure test users are assigned to correct companies

---

## Task 14: Fix Shift Assignment Workflow

**Files:**
- Already fixed by Task 1 (test data seeding)
- May need additional investigation

**Step 1: Run workflow tests**

Run:
```bash
cd qa-automation
npx playwright test tests/shift-assignment-workflow.spec.js
```

**Step 2: If still failing, debug specific workflow steps**

Check which phase fails (Blueprint, Program, Shift, or Assignment)

**Step 3: Fix identified issues**

---

## Task 15: Run Full Test Suite and Generate Report

**Step 1: Run all tests**

Run:
```bash
cd qa-automation
npx playwright test
```

**Step 2: Generate HTML report**

Run:
```bash
npx playwright show-report
```

**Step 3: Document results**

Create summary of:
- Tests fixed
- Remaining failures (if any)
- Application bugs resolved
- Performance improvements

**Step 4: Commit final report**

```bash
git add qa-automation/reports/*
git commit -m "test: Update QA test results after bug fixes

- Document test pass rate improvement
- Include performance metrics
- List resolved issues

Co-Authored-By: Claude Sonnet 4.5 <noreply@anthropic.com>"
```

---

## Verification Checklist

After completing all tasks:

- [ ] All 35 RBAC tests pass (test users created)
- [ ] No 500 errors on /Admin/Users
- [ ] Modals open correctly (rename/delete)
- [ ] Slug validation works (rejects invalid formats)
- [ ] Path traversal blocked
- [ ] XSS attempts sanitized
- [ ] Duplicate requests reduced to < 5
- [ ] All endpoints respond < 1000ms
- [ ] Authentication headers present
- [ ] Fonts degrade gracefully
- [ ] Session timeout redirects to login
- [ ] Back button shows correct state
- [ ] Multi-tenancy isolation works
- [ ] Shift assignment workflow completes
- [ ] Test pass rate > 90%

---

## Expected Outcomes

**Before:** 38/92 tests passing (41% pass rate)
**After:** 85+/92 tests passing (92%+ pass rate)

**Security Improvements:**
- XSS vulnerability fixed
- Path traversal blocked
- Input validation enforced

**Performance Improvements:**
- Duplicate requests eliminated
- Slow endpoints optimized
- Network overhead reduced

**UX Improvements:**
- Modals work correctly
- Session timeout handled gracefully
- Font loading doesn't break layout
- Back button behavior correct
