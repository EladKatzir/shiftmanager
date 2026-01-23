# Plan A: Test Fixes (TEST ISSUES)

**Iteration:** 1
**Date:** 2026-01-20
**Status:** ACTIONABLE

---

## Overview

This plan addresses test methodology issues, selector problems, timing issues, and test assumptions that need fixing. These are problems with HOW we test, not the application itself.

---

## Issue #1: H1 Selector Specificity (PRIORITY 1)

### Affected Tests (3 tests)
1. `users-crud-rbac.spec.js:80` - P3-01: Owner can access Users management page
2. `users-crud-rbac.spec.js:211` - P3-07: Director can access Users management page
3. `users-crud-rbac.spec.js:267` - P3-11: Manager can access Users management page

### Problem
```javascript
await expect(page.locator('h1')).toContainText(/User Management|Users/i);
```
**Error:** `strict mode violation: locator('h1') resolved to 2 elements`

The selector matches TWO h1 elements:
1. Empty h1: `<h1 class="page-title"></h1>`
2. Actual h1: `<h1 class="page-title">UserManagement</h1>`

### Root Cause
Non-specific selector that matches multiple elements on the page.

### Fix Strategy
Replace the generic `h1` selector with a more specific selector that targets only the h1 with content.

### Implementation

**Option 1: Filter out empty h1 (RECOMMENDED)**
```javascript
await expect(page.locator('h1.page-title').filter({ hasNotText: /^$/ }))
    .toContainText(/User Management|Users/i);
```

**Option 2: Use last() if order is consistent**
```javascript
await expect(page.locator('h1.page-title').last())
    .toContainText(/User Management|Users/i);
```

**Option 3: Match by specific text pattern**
```javascript
await expect(page.locator('h1:has-text("UserManagement")'))
    .toBeVisible();
```

### Files to Edit
- `qa-automation/tests/users-crud-rbac.spec.js`
  - Line 84
  - Line 215
  - Line 271

### Expected Outcome
All 3 affected tests should pass after applying the fix.

### Testing
Run the affected tests:
```bash
cd qa-automation
npx playwright test tests/users-crud-rbac.spec.js --grep "P3-01|P3-07|P3-11"
```

---

## Issue #2: Delete Confirmation Modal Interaction (PRIORITY 2)

### Affected Test
- `companies-crud.spec.js:401` - P2-13: Delete confirmation can be cancelled

### Problem
Test clicked the delete button (button shows `[active]` state in snapshot), but something went wrong after that point. The test is supposed to:
1. Click delete button
2. Confirm dialog appears
3. Cancel the dialog
4. Verify company still exists

### Investigation Needed
The error details weren't fully captured. Need to determine:
- Does the confirmation dialog appear?
- Is the cancel button being found/clicked correctly?
- Is there a timing issue?

### Fix Strategy
1. Read the full test code (lines 401-430)
2. Check if proper waits are in place for the dialog
3. Verify dialog selectors are correct
4. Add explicit waits if needed

### Next Steps
1. Read test code at `companies-crud.spec.js:401-430`
2. Check if there's a modal/dialog that appears after clicking delete
3. Verify the cancel button selector
4. Test manually in the UI to understand the flow

---

## Issue #3: User Filter URL Parameter Expectation (PRIORITY 2)

### Affected Test
- `users-crud-rbac.spec.js:526` - P3-23: Owner can filter users by company

### Problem
```javascript
// Test code at line 533-549
const companyFilter = page.locator('select[name="UserFilterCompanyId"]');
await companyFilter.selectOption(firstValue || '');
await page.waitForLoadState('networkidle');

// URL should contain the filter parameter
expect(page.url()).toContain('UserFilterCompanyId');
```

**Error:** Expected "UserFilterCompanyId" in URL, got "http://localhost:5000/Admin/Users"

### Root Cause Analysis
The test selects a filter option but the URL doesn't update with the parameter. This could be because:
1. The filter works via AJAX/fetch without page reload
2. The form needs to be submitted explicitly
3. There's a separate "Apply Filter" button that needs clicking
4. The filter updates immediately via JavaScript without changing the URL

### Evidence from Snapshot
In the error context (lines 109-112), I can see:
```yaml
- combobox [ref=e109] [cursor=pointer]:
  - option "All Companies"
  - option "'; DROP TABLE Companies;--" [selected]
```

The filter shows a selected option, and there's also an "Export CSV" link with a URL parameter visible:
```
/url: /Admin/Users?UserFilterCompanyId=12&handler=ExportCsv
```

This suggests the parameter name is correct, but the filter might work differently than expected.

### Fix Strategy

**Investigate and choose ONE approach:**

**Approach A: Check if form submission is needed**
```javascript
// After selecting option, trigger form submission
await companyFilter.selectOption(firstValue || '');
await page.locator('form').press('Enter'); // or find submit button
await page.waitForLoadState('networkidle');
expect(page.url()).toContain('UserFilterCompanyId');
```

**Approach B: Check if filter works via AJAX (no URL change)**
```javascript
// Instead of checking URL, verify filtered results in table
await companyFilter.selectOption(firstValue || '');
await page.waitForLoadState('networkidle');

// Verify the table shows filtered results
const rows = page.locator('table tbody tr');
await expect(rows).not.toHaveCount(0);
// Additional assertion: verify all visible users belong to selected company
```

**Approach C: Check for "Apply" or "Filter" button**
```javascript
await companyFilter.selectOption(firstValue || '');
await page.locator('button:has-text("Apply"), button:has-text("Filter")').click();
await page.waitForLoadState('networkidle');
expect(page.url()).toContain('UserFilterCompanyId');
```

### Investigation Steps
1. Check the Users page HTML/form structure
2. Test manually: select a company filter and see what happens
3. Check network tab to see if there's an AJAX request
4. Look for any Apply/Submit button for filters

### Files to Edit
- `qa-automation/tests/users-crud-rbac.spec.js` - line 526-552

---

## Issue #4: Network Performance Test Timeout (PRIORITY 3)

### Affected Test
- `network-performance.spec.js:289` - P9-07: Measure full workflow - Create Company

### Problem
```
Error: page.waitForResponse: Request context disposed.
Timeout: 60000ms exceeded.
```

### Root Cause
Test is monitoring network traffic while performing a "Create Company" workflow, but either:
1. The expected response never arrives
2. The page context is closed/disposed prematurely
3. The wait condition is too specific
4. Network interception is interfering with the actual request

### Fix Strategy

**Option 1: Increase timeout for slow operations**
```javascript
await page.waitForResponse(
    response => response.url().includes('/Admin/Companies') && response.status() === 200,
    { timeout: 120000 } // Increase to 2 minutes
);
```

**Option 2: Make wait condition more flexible**
```javascript
// Instead of waiting for specific response, wait for navigation
await Promise.race([
    page.waitForResponse(response => response.url().includes('/Admin/Companies')),
    page.waitForLoadState('networkidle')
]);
```

**Option 3: Add error handling for disposed context**
```javascript
try {
    const response = await page.waitForResponse(
        response => response.url().includes('/Admin/Companies'),
        { timeout: 60000 }
    );
    // measure performance
} catch (error) {
    if (error.message.includes('disposed')) {
        test.skip(true, 'Page context was disposed during navigation');
    }
    throw error;
}
```

### Investigation Steps
1. Read the full test code at `network-performance.spec.js:289`
2. Check what network monitoring is set up
3. Verify the "Create Company" workflow doesn't close the page unexpectedly
4. Test if removing network monitoring makes the test pass (to isolate if monitoring is the issue)

### Files to Edit
- `qa-automation/tests/network-performance.spec.js` - line 289 and surrounding context

---

## Issue #5: Conditional Skips Resulting in False Skips (PRIORITY 4)

### Problem
6 tests were marked as "skipped" in this run. Many tests have conditional `test.skip()` calls that skip based on data availability:

```javascript
test.skip(true, 'No companies available to test delete cancel');
test.skip(true, 'No existing companies to test duplicate slug');
```

### Analysis
These conditional skips can hide test coverage gaps. If data isn't available, the test skips silently, giving false confidence.

### Fix Strategy

**Option 1: Fail tests instead of skipping when data is missing**
```javascript
if (!buttonExists) {
    throw new Error('No companies available - test data setup failed');
}
```

**Option 2: Ensure test data is seeded before tests run**
- Add data setup in global setup
- Create fixture companies/users for tests that need them
- Use beforeEach hooks to ensure data exists

**Option 3: Make data creation part of the test**
```javascript
// Instead of skipping if no data exists, CREATE the data
if (!(await deleteButton.isVisible())) {
    await createTestCompany(page);  // Helper to create company
}
// Now run the actual test
```

### Files to Review
- All test files with `test.skip(true, ...)` patterns
- Global setup to ensure adequate test data

---

## Summary

| Priority | Issue | Affected Tests | Effort | Impact |
|----------|-------|----------------|--------|--------|
| P1 | H1 selector specificity | 3 tests | Low (5 min) | High (immediate 3 tests pass) |
| P2 | Delete confirmation | 1 test | Medium (15 min) | Medium (1 test pass) |
| P2 | User filter URL | 1 test | Medium (20 min) | Medium (1 test pass or investigation) |
| P3 | Network timeout | 1 test | Medium (15 min) | Medium (1 test pass or skip) |
| P4 | Conditional skips | 6 tests | High (1 hour) | Low (better coverage awareness) |

**Total potential test fixes: 4-5 tests → ~9% improvement**

---

## Next Iteration

After applying Priority 1 fix:
1. Run tests again
2. Verify 3 tests now pass
3. Move to Priority 2 fixes
4. Update this document with results
