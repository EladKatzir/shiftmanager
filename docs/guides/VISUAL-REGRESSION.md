# Visual Regression Testing Guide

This document explains how to use the visual regression testing infrastructure for ShiftManager.

## Overview

Visual regression tests capture baseline screenshots of key pages and compare them against future runs to detect unintended visual changes. This helps catch:

- CSS regressions
- Layout shifts
- Missing or broken images
- Unintended style changes
- Responsive design breakages

## Test Location

**Test File:** `qa-automation/tests/visual-regression.spec.js`

**Baseline Screenshots:** `qa-automation/tests/visual-regression.spec.js-snapshots/`

## Configuration

Visual comparison settings are configured in `qa-automation/playwright.config.js`:

```javascript
expect: {
  toHaveScreenshot: {
    // Maximum allowed pixel difference ratio (0.2% = 0.002)
    maxDiffPixelRatio: 0.002,
    // Threshold for color differences (0-1, lower = stricter)
    threshold: 0.2,
    // Disable animations during screenshots
    animations: 'disabled',
  },
}
```

### Why These Thresholds?

- **maxDiffPixelRatio: 0.002 (0.2%)** - Allows minor anti-aliasing differences while catching real visual changes. Industry standard for visual regression.
  
- **threshold: 0.2** - Balanced tolerance for slight color variations due to font rendering differences across platforms.

- **animations: 'disabled'** - Ensures consistent screenshots by stopping CSS animations and transitions.

## Running Visual Tests

### Prerequisites

1. The application must be running:
   ```bash
   cd C:\Users\katzi\Downloads\ShiftManager
   dotnet run
   ```

2. Navigate to the qa-automation directory:
   ```bash
   cd qa-automation
   ```

### Run All Visual Tests

```bash
npx playwright test visual-regression.spec.js
```

### Run Specific Visual Test

```bash
npx playwright test visual-regression.spec.js -g "Login page visual baseline"
```

### Run with Tag Filter

Visual tests are tagged with `@visual` for separate CI runs:

```bash
npx playwright test --grep "@visual"
```

## Updating Baselines

When you intentionally change the UI, you need to update the baseline screenshots:

### Update All Baselines

```bash
npx playwright test visual-regression.spec.js --update-snapshots
```

### Update Specific Page Baseline

```bash
npx playwright test visual-regression.spec.js -g "Login page visual baseline" --update-snapshots
```

### Review Changes Before Committing

After updating baselines:

1. Review the new screenshots visually
2. Use `git diff --stat` to see changed files
3. Open the HTML report to compare: `npx playwright show-report`

## Test Coverage

The visual regression suite covers:

### Unauthenticated Pages
- Login page (default state)
- Login page (focused input state)
- Login page (validation error state)

### Calendar Views (Authenticated)
- Month calendar
- Week calendar
- Day calendar
- Table calendar

### Admin Dashboard (Owner)
- Admin index page
- Users management
- Companies management
- Shift types configuration
- Analytics page
- Config page

### Navigation Components
- Sidebar expanded state
- Sidebar collapsed state

### Responsive Breakpoints
- Tablet (768px width)
- Mobile (375px width)

### Theme Variations
- Dark mode preference

## Masking Dynamic Content

Some elements are masked in screenshots because they change between runs:

- Timestamps and dates
- Current date indicators
- User avatars
- Stats counters
- Chart data

Masks are defined using Playwright locators in the test file.

## CI Integration

For CI pipelines, visual tests should run separately from functional tests:

```yaml
# Example GitHub Actions step
- name: Run Visual Regression Tests
  run: |
    cd qa-automation
    npx playwright test visual-regression.spec.js
  env:
    CI: true
```

### CI Considerations

1. **Browser consistency**: Playwright uses exact browser versions, ensuring CI matches local results
2. **Font rendering**: May differ slightly between Windows/Linux - adjust threshold if needed
3. **Docker**: Use Playwright's official Docker images for consistent CI environments

## Troubleshooting

### Test Fails with Pixel Differences

1. Open the HTML report: `npx playwright show-report`
2. Compare actual vs expected screenshots
3. Decide if the change is intentional:
   - **Yes**: Update baselines with `--update-snapshots`
   - **No**: Fix the CSS/layout regression

### Screenshots Look Different Locally vs CI

1. Check if fonts are installed consistently
2. Use Playwright's Docker image for CI
3. Consider increasing `threshold` slightly for cross-platform tolerance

### Tests Time Out

1. Ensure the app is running on the configured `baseURL`
2. Check network connectivity
3. Increase timeout in config if needed for slow environments

### Missing Baseline

On first run, Playwright creates baseline screenshots automatically. If baselines are missing:

```bash
npx playwright test visual-regression.spec.js --update-snapshots
```

Then commit the generated snapshots to source control.

## Best Practices

1. **Commit baselines to source control** - Enables team-wide comparison
2. **Review baseline changes carefully** - Don't blindly update
3. **Keep tests focused** - One visual aspect per test
4. **Use meaningful masks** - Only mask truly dynamic content
5. **Document intentional changes** - In PR descriptions, explain visual changes
6. **Run before merging** - Catch regressions before they reach main branch

## Adding New Visual Tests

When adding new pages to the application:

1. Add a new test in `visual-regression.spec.js`:
   ```javascript
   test('VR-NEW-01: New page visual baseline', async ({ page }) => {
       await page.goto('/New/Page');
       await page.waitForLoadState('networkidle');
       await page.waitForTimeout(500);

       await expect(page).toHaveScreenshot('new-page.png', {
           fullPage: true,
       });
   });
   ```

2. Run with `--update-snapshots` to create baseline
3. Commit the new snapshot file
