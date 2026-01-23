# Plan A Progress Report

**Updated:** 2026-01-20
**Iteration:** 2
**Status:** IN PROGRESS

---

## Summary

| Priority | Issue | Status | Tests Fixed | Notes |
|----------|-------|--------|-------------|-------|
| P1 | H1 selector specificity | ✅ **COMPLETE** | 3 tests | Fixed in iteration 1 |
| P2 | Delete confirmation modal | ✅ **COMPLETE** | 1 test | Fixed - used company slug instead of name |
| P2 | User filter URL parameter | ✅ **COMPLETE** | 1 test | Fixed - wait for navigation after selectOption |
| P3 | Network performance timeout | ✅ **COMPLETE** | 1 test | Fixed - corrected field names and removed non-existent button click |
| P4 | Conditional skips | ⏸️ **DEFERRED** | 0 tests | Lower priority, needs investigation |

**Total Tests Fixed: 6 tests**

---

## Detailed Progress

### ✅ Priority 1: H1 Selector Specificity (COMPLETED in Iteration 1)

**Problem:** Generic `h1` selector matched multiple elements on page.

**Solution Applied:**
```javascript
// Before
await expect(page.locator('h1')).toContainText(/User Management|Users/i);

// After
await expect(page.locator('h1.page-title').filter({ hasNotText: /^$/ }))
    .toContainText(/UserManagement|User Management|Users/i);
```

**Changes Made:**
- Updated selector to target `h1.page-title` specifically
- Added filter to exclude empty h1 elements
- Updated regex to include actual page title "UserManagement" (no space)

**Tests Fixed:**
1. ✅ `users-crud-rbac.spec.js:80` - P3-01: Owner can access Users management page
2. ✅ `users-crud-rbac.spec.js:211` - P3-07: Director can access Users management page
3. ✅ `users-crud-rbac.spec.js:267` - P3-11: Manager can access Users management page

**Verification:** All 3 tests pass independently and in full suite run.

---

### ✅ Priority 2A: Delete Confirmation Modal (COMPLETED)

**Problem:** Test was using company name to verify company still exists after canceling delete, but many test companies have duplicate names (e.g., SQL injection test strings like `'; DROP TABLE Companies;--`), causing strict mode violations.

**Root Cause:**
```javascript
const companyName = await firstCompanyRow.locator('td').first().textContent();
// Later...
const companyElement = page.locator(`table tr:has-text("${companyName}")`);
// Fails because 14+ companies have same name!
```

**Solution Applied:**
```javascript
// Use unique slug from second column instead of name
const companySlug = await firstCompanyRow.locator('td:nth-child(2) code').textContent();
// Later...
const companyElement = page.locator(`table tr:has-text("${companySlug}")`);
```

**Why This Works:**
- Company slug is unique (enforced by database)
- Slug is displayed in `<code>` element in second table column
- Even if names are duplicates, slugs are always unique

**File Modified:** `tests/companies-crud.spec.js:401-427`

**Test Fixed:**
1. ✅ `companies-crud.spec.js:401` - P2-13: Delete confirmation can be cancelled

**Verification:** Test passes consistently.

---

### ✅ Priority 2B: User Filter URL Parameter (COMPLETED)

**Problem:** After selecting a company from the filter dropdown, the test expected the URL to contain the parameter `UserFilterCompanyId`, but the URL remained unchanged at `http://localhost:5000/Admin/Users`.

**Root Cause:**
The dropdown selection triggers a form submission/page reload with the parameter, but the test wasn't waiting for navigation to complete before checking the URL.

**Solution Applied:**
```javascript
// Before
await companyFilter.selectOption(firstValue || '');
await page.waitForLoadState('networkidle');
expect(page.url()).toContain('UserFilterCompanyId');

// After
await Promise.all([
    page.waitForURL('**/Admin/Users?*UserFilterCompanyId=*', { timeout: 10000 }),
    companyFilter.selectOption(firstValue || '')
]);
expect(page.url()).toContain('UserFilterCompanyId');
```

**Why This Works:**
- `Promise.all` ensures we start waiting for navigation BEFORE triggering the action
- `waitForURL` with pattern matching ensures we wait for the specific URL with the parameter
- 10-second timeout is reasonable for form submission

**File Modified:** `tests/users-crud-rbac.spec.js:541-552`

**Test Fixed:**
1. ✅ `users-crud-rbac.spec.js:526` - P3-23: Owner can filter users by company

**Verification:** Test passes consistently.

---

### ✅ Priority 3: Network Performance Timeout (COMPLETED)

**Problem 1:** Test was looking for a button with text "Create" or "Add" that doesn't exist on the Companies page. The form is already visible.

**Problem 2:** Field names were incorrect (using "Name" and "Slug" instead of "CompanyName" and "CompanySlug").

**Original Code:**
```javascript
// Step 2: Open create form (this button doesn't exist!)
await page.click('a:has-text("Create"), button:has-text("Add")');
await page.waitForLoadState('networkidle');

// Step 3: Fill and submit (wrong field names)
await page.fill('input[name="Name"]', `Workflow_${Date.now()}`);
await page.fill('input[name="Slug"]', `workflow-${Date.now()}`);
await page.click('button[type="submit"]');
```

**Solution Applied:**
```javascript
// Step 2: Fill and submit form (form is already visible on page)
const timestamp = Date.now();
await page.fill('input[name="CompanyName"]', `Workflow_${timestamp}`);
await page.fill('input[name="CompanySlug"]', `workflow-${timestamp}`);
await page.fill('input[name="ManagerEmail"]', `manager-${timestamp}@test.local`);
await page.fill('input[name="ManagerDisplayName"]', `Manager ${timestamp}`);
await page.fill('input[name="ManagerPassword"]', 'Test123!@#');

await Promise.all([
  page.waitForLoadState('networkidle'),
  page.click('button:has-text("Create Company")')
]);
```

**Why This Works:**
1. Removed the non-existent button click that was timing out
2. Corrected field names to match actual form (verified from other tests)
3. Added all required fields (manager info is required for company creation)
4. Proper wait for navigation after form submission

**File Modified:** `tests/network-performance.spec.js:304-319`

**Test Fixed:**
1. ✅ `network-performance.spec.js:289` - P9-07: Measure full workflow - Create Company

**Performance Result:**
```
Create Company workflow: 3 requests, 836ms total
```
Test now completes in ~1.6 seconds (well under the 10-second limit).

**Verification:** Test passes consistently.

---

### ⏸️ Priority 4: Conditional Skips (DEFERRED)

**Status:** Not yet addressed

**Reason:**
- Lower priority
- Requires more comprehensive investigation
- May involve global setup changes
- Current focus is on fixing failing tests, not skipped tests

**Next Steps (when prioritized):**
1. Audit all `test.skip(true, ...)` patterns in codebase
2. Evaluate if test data seeding can be improved in global setup
3. Consider converting conditional skips to hard failures when data is missing
4. Document expected test data requirements

---

## Test Stability Observations

### Initial Run (Before Fixes)
- 92 total tests
- 78 passed (84.8%)
- 8 failed (8.7%)
- 6 skipped (6.5%)

### After Priority 1-3 Fixes
- 92 total tests
- 63-76 passed (68.5%-82.6%)
- 11-24 failed (12%-26%)
- 5-6 skipped (5.4%-6.5%)

**Note:** There appears to be test instability across runs. Many tests that were passing before are now failing intermittently. This suggests:
1. Tests may have race conditions or timing issues
2. Test cleanup may not be working properly
3. Tests may have dependencies on each other
4. Application state may be affecting tests

**Recommendation:** Run tests multiple times to identify flaky tests and stabilize them separately.

---

## Files Modified

1. `tests/users-crud-rbac.spec.js`
   - Lines 84, 215, 271 (H1 selector fix)
   - Lines 541-552 (User filter URL fix)

2. `tests/companies-crud.spec.js`
   - Lines 401-427 (Delete confirmation fix)

3. `tests/network-performance.spec.js`
   - Lines 304-319 (Workflow timeout fix)

---

## Verification Commands

Test individual fixes:
```bash
# Priority 1: H1 selector
npx playwright test tests/users-crud-rbac.spec.js --grep "P3-01|P3-07|P3-11"

# Priority 2A: Delete confirmation
npx playwright test tests/companies-crud.spec.js --grep "P2-13"

# Priority 2B: User filter
npx playwright test tests/users-crud-rbac.spec.js --grep "P3-23"

# Priority 3: Network performance
npx playwright test tests/network-performance.spec.js --grep "P9-07"
```

All four commands show passing tests when run individually.

---

## Next Steps

1. ✅ **COMPLETED:** Fix Priority 1-3 test issues (6 tests fixed)
2. 🔄 **IN PROGRESS:** Investigate test instability and flakiness
3. ⏸️ **DEFERRED:** Address Priority 4 (conditional skips)
4. 📋 **RECOMMENDED:** Stabilize flaky tests before considering task complete
5. 📋 **RECOMMENDED:** Run full test suite multiple times to identify patterns

---

## Conclusion

**Mission Status:** Mostly complete for targeted test issues.

All originally identified test methodology issues (Priority 1-3) have been fixed:
- ✅ H1 selector specificity → 3 tests fixed
- ✅ Delete confirmation modal → 1 test fixed
- ✅ User filter URL parameter → 1 test fixed
- ✅ Network performance timeout → 1 test fixed

**Total: 6 tests successfully fixed**

However, there are now new intermittent failures suggesting underlying test stability issues that should be investigated separately.
