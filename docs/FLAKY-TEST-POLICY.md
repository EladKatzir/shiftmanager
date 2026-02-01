# Flaky Test Policy

**Document Version:** 1.0
**Last Updated:** 2026-02-01
**Applies To:** ShiftManager Playwright E2E Test Suite (`qa-automation/`)

---

## Table of Contents

1. [Definition of Flaky Tests](#1-definition-of-flaky-tests)
2. [Detection](#2-detection)
3. [Retry Strategy](#3-retry-strategy)
4. [Quarantine Procedure](#4-quarantine-procedure)
5. [Resolution Timeline](#5-resolution-timeline)
6. [Prevention Best Practices](#6-prevention-best-practices)
7. [Common Causes and Solutions](#7-common-causes-and-solutions)
8. [Tracking and Metrics](#8-tracking-and-metrics)

---

## 1. Definition of Flaky Tests

### What Constitutes a Flaky Test

A **flaky test** is a test that produces inconsistent results (pass/fail) when run multiple times against the same codebase, without any changes to the code or test.

**Characteristics:**
- Passes on some runs, fails on others
- Failure is not reproducible on demand
- No corresponding code changes explain the failure
- Often related to timing, state, or environmental factors

### Flaky vs. Environment Issues

| Flaky Test | Environment Issue |
|------------|-------------------|
| Same environment, inconsistent results | Consistent failure in specific environment |
| Root cause in test code or app timing | Root cause in infrastructure/setup |
| Affects single or few tests | May affect entire test suite |
| Reproducible intermittently | Reproducible consistently in affected environment |

**Environment issues to rule out first:**
- Application not running (`http://localhost:5000` unavailable)
- Database not seeded with test data
- Missing dependencies (`npx playwright install chromium`)
- Port conflicts
- Insufficient system resources (memory, CPU)

---

## 2. Detection

### How Flaky Tests Are Detected

#### CI Pipeline Detection
- Test passes locally but fails in CI (or vice versa)
- Test fails on retry but passes on re-run
- Test results differ between parallel workers

#### Local Detection
- Running `npx playwright test --repeat-each=5` shows inconsistent results
- Test fails intermittently during development
- Test passes in headed mode but fails in headless (or vice versa)

#### Pattern Recognition
Look for these warning signs in test code:
- Hard-coded `waitForTimeout()` calls
- Missing `await` keywords
- Race conditions with parallel data access
- Time-sensitive assertions (dates, timestamps)

### Detection Commands

```bash
# Run a specific test multiple times to check for flakiness
npx playwright test auth.spec.js --repeat-each=10

# Run with tracing to diagnose failures
npx playwright test --trace on

# Run with verbose output
npx playwright test --reporter=list
```

### Metrics to Track

| Metric | Target | Alert Threshold |
|--------|--------|-----------------|
| Test failure rate (per test) | < 1% | > 5% |
| Retry success rate | > 95% | < 80% |
| Quarantined test count | < 5% of suite | > 10% |
| Time to resolution | < 2 weeks | > 4 weeks |

---

## 3. Retry Strategy

### Current Configuration

The Playwright configuration (`qa-automation/playwright.config.js`) already implements retries:

```javascript
// Current settings
retries: process.env.CI ? 2 : 0,  // 2 retries in CI, none locally
trace: 'on-first-retry',          // Capture trace on retry
screenshot: 'only-on-failure',     // Screenshot failures
video: 'retain-on-failure',        // Video on failures
```

### Retry Policy

| Environment | Retries | Rationale |
|-------------|---------|-----------|
| Local Development | 0 | Immediate feedback; flakiness should be visible |
| CI Pipeline | 2 | Balance between catching real issues and CI stability |
| Nightly/Stress | 3 | Extended retry for comprehensive runs |

### Per-Test Retry Override

For known flaky tests awaiting fix, use per-test retry configuration:

```javascript
// Increase retries for a specific flaky test
test('flaky network test', async ({ page }) => {
  test.info().annotations.push({ type: 'flaky', description: 'FLAKY-001: Network timing issue' });
  // test code
});

// In playwright.config.js, can add project-specific retries
```

### When Retries Mask Real Issues

**Warning Signs:**
- Test consistently passes only after retry
- Retry rate exceeds 20% for a test file
- Same test fails repeatedly over multiple days

**Action Required:**
- Do NOT increase retry count to "fix" the problem
- Investigate root cause immediately
- Quarantine if investigation will take > 2 days

---

## 4. Quarantine Procedure

### Step 1: Identify and Document

When a test is confirmed flaky:

1. Create a tracking issue (if using issue tracker)
2. Add annotation to the test:

```javascript
test('flaky test name', async ({ page }) => {
  test.info().annotations.push({
    type: 'flaky',
    description: 'FLAKY-XXX: Brief description of flaky behavior'
  });
  // test code
});
```

### Step 2: Quarantine the Test

Use Playwright's built-in annotations:

```javascript
// Option A: Skip with reason (test is completely disabled)
test.skip('test name', async ({ page }) => {
  // Test code
});

// With skip reason
test('test name', async ({ page }) => {
  test.skip(true, 'FLAKY-XXX: Quarantined - timing issue with modal animation');
  // Test code
});

// Option B: fixme (marks as known broken, still shows in report)
test.fixme('test name', async ({ page }) => {
  // Test code
});

// With fixme reason
test('test name', async ({ page }) => {
  test.fixme(true, 'FLAKY-XXX: Known flaky - investigating race condition');
  // Test code
});
```

### When to Use Each

| Annotation | Use When | Visibility |
|------------|----------|------------|
| `test.skip()` | Test blocks CI or causes confusion | Hidden from run |
| `test.fixme()` | Test should be tracked but not block CI | Shows as "fixme" |
| `test.slow()` | Test is slow but not flaky | Triples timeout |

### Step 3: Track Quarantined Tests

Maintain a list of quarantined tests (update this table or use issue tracker):

| Test ID | Test Name | File | Quarantine Date | Owner | Issue |
|---------|-----------|------|-----------------|-------|-------|
| FLAKY-001 | Example | file.spec.js | 2026-02-01 | @developer | #123 |

### Step 4: Review Process

- Quarantined tests MUST be reviewed in weekly team standup
- Tests quarantined > 2 weeks require escalation
- Tests quarantined > 4 weeks should be considered for deletion

---

## 5. Resolution Timeline

### Timeline Requirements

| Severity | Resolution Deadline | Escalation Path |
|----------|---------------------|-----------------|
| Critical (blocks release) | 24 hours | Immediate team attention |
| High (affects CI stability) | 1 week | Team lead review |
| Medium (intermittent) | 2 weeks | Sprint planning |
| Low (rare occurrence) | 4 weeks | Backlog |

### Resolution Workflow

```
Day 0: Flaky test identified
  |
  v
Day 1-2: Root cause analysis
  |
  +-- Root cause found --> Fix and verify (run 10x)
  |
  +-- Root cause unclear --> Quarantine + continue investigation
  |
  v
Day 3-7: Investigation continues
  |
  +-- Fix identified --> Implement, verify, un-quarantine
  |
  +-- No progress --> Escalate to team lead
  |
  v
Day 8-14: Escalated investigation
  |
  +-- Fix implemented --> Verify and close
  |
  +-- Unfixable --> Decision: Delete or rewrite test
```

### When to Delete vs Fix

**Delete the test when:**
- Test provides little value (duplicate coverage)
- Test requires architectural changes to fix
- Test cost exceeds value (> 1 week to fix, minor coverage)
- Feature being tested is deprecated

**Fix the test when:**
- Test covers critical business logic
- Fix is straightforward (timing, selectors, state)
- Test is part of required compliance/security suite
- Multiple tests affected by same root cause

---

## 6. Prevention Best Practices

### Waiting Strategies

**DO use explicit waits:**

```javascript
// Wait for specific element
await page.waitForSelector('.dashboard-loaded');

// Wait for load state
await page.waitForLoadState('networkidle');

// Wait for URL change
await page.waitForURL('**/dashboard');

// Wait for response
await page.waitForResponse(resp => resp.url().includes('/api/data'));
```

**DO NOT use arbitrary timeouts:**

```javascript
// BAD - arbitrary wait
await page.waitForTimeout(2000);

// GOOD - wait for specific condition
await expect(page.locator('.loading')).toBeHidden();
```

### Selector Stability

**DO use stable selectors:**

```javascript
// GOOD - data-testid (most stable)
await page.locator('[data-testid="submit-button"]').click();

// GOOD - ARIA role with name
await page.getByRole('button', { name: 'Submit' }).click();

// GOOD - explicit ID
await page.locator('#submit-btn').click();
```

**DO NOT use fragile selectors:**

```javascript
// BAD - relies on DOM structure
await page.locator('div > div > button').click();

// BAD - relies on class names that may change
await page.locator('.btn-primary.mt-4').click();

// BAD - relies on text that may be localized
await page.locator('text=Click Here').click();
```

### State Management

**DO isolate test state:**

```javascript
test.beforeEach(async ({ page }) => {
  // Reset to known state
  await page.goto('/Auth/Login');
  // Login with fresh session
  await login(page, credentials);
});

test.afterEach(async ({ page }) => {
  // Clean up any created data
  await cleanup(page);
});
```

**DO NOT share state between tests:**

```javascript
// BAD - tests depend on execution order
let createdCompanyId;

test('create company', async ({ page }) => {
  createdCompanyId = await createCompany(page);
});

test('edit company', async ({ page }) => {
  // FLAKY - depends on previous test
  await editCompany(page, createdCompanyId);
});
```

### Animation and Transition Handling

```javascript
// Wait for animations to complete
await page.locator('.modal').waitFor({ state: 'visible' });
await expect(page.locator('.modal')).toHaveCSS('opacity', '1');

// Or disable animations in tests
await page.addStyleTag({
  content: '*, *::before, *::after { transition: none !important; animation: none !important; }'
});
```

---

## 7. Common Causes and Solutions

### Timing Issues

| Problem | Solution |
|---------|----------|
| Element not yet visible | `await page.waitForSelector('.element')` |
| Page not loaded | `await page.waitForLoadState('networkidle')` |
| Animation in progress | Wait for animation end or disable animations |
| Async data loading | Wait for loading indicator to disappear |

### State Issues

| Problem | Solution |
|---------|----------|
| Test data not seeded | Run database seed before tests |
| Previous test left dirty state | Use `beforeEach` to reset state |
| Shared mutable state | Isolate tests with unique data |
| Session/cookie issues | Clear context in `beforeEach` |

### Network Issues

| Problem | Solution |
|---------|----------|
| Slow API response | Increase timeout or mock API |
| Network request race | Use `waitForResponse` |
| CORS or auth issues | Ensure proper test environment setup |
| Unstable test server | Use `webServer` config option |

### Environment Issues

| Problem | Solution |
|---------|----------|
| Different local vs CI | Use consistent Docker environment |
| Timezone differences | Use UTC in tests |
| Screen resolution | Set explicit viewport size |
| Font rendering | Use visual comparison thresholds |

---

## 8. Tracking and Metrics

### Weekly Review Checklist

- [ ] Review all quarantined tests
- [ ] Check flaky test failure rate from CI reports
- [ ] Update tracking table with resolution status
- [ ] Escalate overdue items
- [ ] Close resolved items

### Reporting

Generate flaky test report from CI:

```bash
# View test results
npx playwright show-report reports/playwright-report

# Export JSON for analysis
# (Already configured in playwright.config.js)
# Output: reports/test-results.json
```

### Dashboard Metrics (if implemented)

Track these metrics over time:
- Total tests vs quarantined tests ratio
- Average time to resolution
- Most frequently flaky test files
- Retry success rate by test category

---

## Appendix: Quick Reference

### Playwright Annotations

```javascript
test.skip(condition, 'reason');     // Skip test
test.fixme(condition, 'reason');    // Mark as known broken
test.slow();                        // Triple timeout
test.fail();                        // Expect test to fail
test.describe.skip('suite');        // Skip entire suite
test.describe.fixme('suite');       // Mark suite as broken
```

### Useful Debug Commands

```bash
# Run with headed browser
npx playwright test --headed

# Run with debug mode (step through)
npx playwright test --debug

# Run specific test by name
npx playwright test -g "test name"

# Generate trace for debugging
npx playwright test --trace on

# View trace
npx playwright show-trace trace.zip
```

### Configuration Reference

Current `playwright.config.js` settings relevant to flaky tests:

| Setting | Value | Purpose |
|---------|-------|---------|
| `retries` | 2 (CI), 0 (local) | Automatic retry on failure |
| `trace` | 'on-first-retry' | Capture trace for debugging |
| `screenshot` | 'only-on-failure' | Visual debugging |
| `video` | 'retain-on-failure' | Video debugging |
| `timeout` | 30000ms | Per-test timeout |
| `expect.timeout` | 5000ms | Assertion timeout |
| `workers` | 2 (local), 1 (CI) | Parallel execution |

---

## Document History

| Version | Date | Author | Changes |
|---------|------|--------|---------|
| 1.0 | 2026-02-01 | Claude Code | Initial document |

---

*This policy is part of the ShiftManager QA documentation. For questions, contact the QA team.*
