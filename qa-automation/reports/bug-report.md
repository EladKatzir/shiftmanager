# Bug Report

**Date:** 2026-01-16
**Testing Session:** Comprehensive QA Validation
**Tester:** Automated Playwright Suite

---

## Template

### BUG-XXX: [Title]
**Severity:** Critical | High | Medium | Low
**Module:** [Module name]
**Status:** Open | In Progress | Fixed | Won't Fix

**Steps to Reproduce:**
1. Step 1
2. Step 2
3. Step 3

**Expected Result:** [What should happen]
**Actual Result:** [What actually happens]

**Evidence:**
- Screenshot: [path/to/screenshot.png]
- Network Log: [relevant API calls with responses]
- Console Errors: [JavaScript errors]
- Test File: [test-file.spec.js:line]

**Impact:** [Business/user impact]
**Workaround:** [If any]

---

## Known Issues from Previous Testing

### BUG-001: Dialog Handler Memory Leak
**Severity:** Medium
**Module:** Companies CRUD
**Status:** Open

**Location:** `companies-crud.spec.js:361-364, 394-396`

**Issue:** Using `page.on('dialog')` instead of `page.once('dialog')` causes memory leaks when multiple dialogs occur.

**Fix:**
```javascript
// Bad:
page.on('dialog', dialog => dialog.accept());

// Good:
page.once('dialog', dialog => dialog.accept());
```

### BUG-002: Hardcoded Timeout Anti-Pattern
**Severity:** Low
**Module:** Companies CRUD
**Status:** Open

**Location:** `companies-crud.spec.js:402`

**Issue:** `await page.waitForTimeout(500)` is a code smell. Should use deterministic waits.

**Fix:**
```javascript
// Instead of:
await page.waitForTimeout(500);

// Use:
await page.waitForLoadState('networkidle');
// or
await expect(element).toBeVisible();
```

---

## Bugs Found During Continuation

(To be populated as tests run)

---

**Total Bugs:** TBD
- Critical: TBD
- High: TBD
- Medium: 2 (known)
- Low: 0
