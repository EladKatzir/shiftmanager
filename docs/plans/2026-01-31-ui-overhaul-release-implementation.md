# UI Overhaul Release Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Implement ALL 75 work items (A-001 through A-020, B-001 through B-052, plus extensions) from the UI-OVERHAUL-PRODUCTION-COMPLETE.md document to achieve production release readiness.

**Architecture:** Multi-agent delivery with 12 specialized agents (hats), each owning their domain end-to-end. Work proceeds in 8 phases with explicit gates between phases. Every item requires evidence of completion.

**Tech Stack:** ASP.NET Core Razor Pages, C#, CSS custom properties (tokens.css), JavaScript, Playwright for E2E testing, SharedResources.resx for localization.

**Environment:** Air-gapped military deployment - NO external service dependencies. All observability (analytics, error tracking, RUM) implemented as LOCAL database storage with admin UI.

---

# RESOLVED BLOCKERS

| Blocker ID | Decision | Implementation |
|------------|----------|----------------|
| KU-001 | Local analytics | Database table for events, admin page to view. No external service. |
| KU-002 | Local error tracking | Log errors to database with structured format. Admin page to view. |
| KU-003 | Database feature flags | FeatureFlags table with per-user/per-company support. |
| KU-005 | Modern browsers | Chrome 90+, Firefox 90+, Safari 14+, Edge 90+. IE11 NOT supported. |
| KU-006 | Local RUM | Performance API metrics stored in database. Admin page for dashboards. |

---

# MASTER WORK LEDGER

## Agent Assignments (12 Hats)

| Agent ID | Role | Primary Responsibilities |
|----------|------|-------------------------|
| PM | Program Manager (Orchestrator) | Master ledger, sequencing, gates, status tracking |
| TL | Tech Lead | Architecture, dependencies, PR structure, code standards |
| FE | Frontend/UI Engineer | UI changes, components, states, RTL/localization |
| DS | Design System Engineer | Tokens, component library, theming, variants |
| BE | Backend Engineer | API/service changes, authz/grants, error shapes |
| QA | QA/E2E Engineer | Test plan, Playwright, test data, flake policy |
| A11Y | Accessibility Specialist | Keyboard nav, focus, ARIA, contrast, screen reader |
| PERF | Performance Engineer | Bundle profiling, lazy loading, caching, perf budgets |
| SEC | Security Engineer | XSS/CSRF review, permission UI, PII handling |
| OBS | Observability Engineer | Analytics, error tracking, dashboards, logging |
| RM | Release Manager | Feature flags, staged rollout, migration, rollback |
| DOC | Documentation Owner | Dev docs, migration notes, changelog, runbook |

---

## Complete Item Ledger (75 Items)

### Section A: Plan Completion Items (A-001 to A-020)

| ID | Title | Owner | Dependencies | Status | Evidence |
|----|-------|-------|--------------|--------|----------|
| A-001 | Context Switcher Search Enhancement | FE | - | Not Started | - |
| A-002 | Sidebar Collapse State Persistence | FE | - | Not Started | - |
| A-003 | Scope Switcher Integration | FE+BE | A-003-EXT | Not Started | - |
| A-003-EXT | Scope Switcher Backend Contract | BE | - | Not Started | - |
| A-004 | Calendar Responsive Grid | FE | - | Not Started | - |
| A-005 | Calendar Empty States | FE | A-003 | Not Started | - |
| A-006 | Shift Badge Time-of-Day Colors | DS | - | Not Started | - |
| A-007 | OnCallWidget Empty States | FE | - | Not Started | - |
| A-008 | Widget Collapse Persistence | FE | - | Not Started | - |
| A-009 | Admin Pages Tokenization | DS | A-009-EXT | Not Started | - |
| A-009-EXT | Page Tokenization Verification Script | DS | - | Not Started | - |
| A-010 | Owner Pages Tokenization | DS | A-009-EXT | Not Started | - |
| A-011 | Calendar Pages Polish | DS | A-006, A-009-EXT | Not Started | - |
| A-012 | Remaining Pages Tokenization | DS | A-009-EXT | Not Started | - |
| A-013 | WCAG AA Accessibility Audit | A11Y | A-009 thru A-012 | Not Started | - |
| A-014 | RTL Layout Polish | A11Y+FE | A-009 thru A-012 | Not Started | - |
| A-015 | Missing Localization Strings | FE | A-009 thru A-012 | Not Started | - |
| A-016 | Dark Mode Polish | FE | A-009 thru A-012 | Not Started | - |
| A-017 | Playwright Test Suite Pass | QA | All A-items | Not Started | - |
| A-018 | Scope Switcher Data Correctness | BE+QA | A-003 | Not Started | - |
| A-019 | Calendar Pagination Correctness | BE | - | Not Started | - |
| A-020 | Grant-Based UI Visibility Correctness | FE+QA | A-003, A-001 | Not Started | - |

### Section B: Gap Items (B-001 to B-052)

| ID | Title | Owner | Dependencies | Status | Evidence |
|----|-------|-------|--------------|--------|----------|
| B-001 | prefers-reduced-motion CSS | A11Y | - | Not Started | - |
| B-001-EXT | prefers-reduced-motion JavaScript | A11Y | B-001 | Not Started | - |
| B-002 | Inline Styles Calendar Conversion | DS | - | Not Started | - |
| B-003 | Loading States Implementation | FE | - | Not Started | - |
| B-004 | Error States Implementation | FE | - | Not Started | - |
| B-005 | Icon System Migration | DS | - | Not Started | - |
| B-006 | Print Styles Verification | FE | - | Not Started | - |
| B-007 | Skeleton Loading for Calendars | FE | B-003 | Not Started | - |
| B-008 | Form Validation Error States | FE | - | Not Started | - |
| B-009 | Table Pagination Controls | FE | - | Not Started | - |
| B-010 | Date/Time Format Localization | FE | - | Not Started | - |
| B-011 | Mobile Navigation Collapse | FE | - | Not Started | - |
| B-012 | Context Switcher Edge Cases | FE | A-001 | Not Started | - |
| B-013 | Modal Focus Management | A11Y | - | Not Started | - |
| B-014 | Favicon and App Icons | DS | - | Not Started | - |
| B-015 | Brand Loading Animation | DS | B-001 | Not Started | - |
| B-016 | Stale Cache Handling | BE | - | Not Started | - |
| B-017 | Partial Data / Loading Failures | FE | B-004 | Not Started | - |
| B-018 | Concurrent Edit Conflict Detection | BE+FE | - | Not Started | - |
| B-019 | Analytics Event Taxonomy (LOCAL) | OBS+FE | - | Not Started | - |
| B-020 | Client-Side Error Tracking (LOCAL) | OBS | - | Not Started | - |
| B-021 | Real User Monitoring (LOCAL) | OBS | - | Not Started | - |
| B-022 | Structured Logging | BE+OBS | - | Not Started | - |
| B-023 | Feature Flag Infrastructure | RM+BE | - | Not Started | - |
| B-024 | Phased Rollout Strategy Document | RM | B-023 | Not Started | - |
| B-025 | Rollback Procedure Documentation | RM | B-023 | Not Started | - |
| B-026 | API Contract Documentation | BE | - | Not Started | - |
| B-027 | Rate Limiting Review | BE | - | Not Started | - |
| B-028 | Error Response Standardization | BE | - | Not Started | - |
| B-029 | CSS/JS Bundle Analysis | PERF | A-009 thru A-012 | Not Started | - |
| B-030 | Code Splitting Strategy | PERF | - | Not Started | - |
| B-031 | Image Optimization Audit | PERF | - | Not Started | - |
| B-032 | Render Performance Profiling | PERF | A-009 thru A-012 | Not Started | - |
| B-033 | XSS Vulnerability Audit | SEC | - | Not Started | - |
| B-034 | CSRF Protection Verification | SEC | - | Not Started | - |
| B-035 | UI Permission Alignment Audit | SEC+QA | A-020 | Not Started | - |
| B-036 | Sensitive Data Exposure Audit | SEC | - | Not Started | - |
| B-037 | Keyboard Navigation Audit | A11Y | B-013 | Not Started | - |
| B-038 | Screen Reader Flow Testing | A11Y | A-013 | Not Started | - |
| B-039 | Browser Compatibility Matrix | QA | A-009 thru A-012 | Not Started | - |
| B-040 | Visual Regression Test Suite | QA | A-017 | Not Started | - |
| B-041 | Test Data Strategy | QA | - | Not Started | - |
| B-042 | Flaky Test Policy | QA | B-040, A-017 | Not Started | - |
| B-043 | Component Usage Guidelines | DOC | A-009 thru A-012 | Not Started | - |
| B-044 | Migration Notes Documentation | DOC | A-009 thru A-012 | Not Started | - |
| B-045 | UI Overhaul Changelog | DOC | All work complete | Not Started | - |
| B-046 | Operational Runbook | DOC | B-023, B-021 | Not Started | - |
| B-047 | Disabled State Styling | DS | - | Not Started | - |
| B-048 | Hover State Consistency | DS | - | Not Started | - |
| B-049 | Active/Pressed State Styling | DS | - | Not Started | - |
| B-050 | Offline/Flaky Network Handling | FE | B-004 | Not Started | - |
| B-051 | JavaScript Error Boundary | FE | B-020 | Not Started | - |
| B-052 | Toast/Alert Pattern Standardization | FE | B-004 | Not Started | - |

---

# DEPENDENCY GRAPH & EXECUTION ORDER

## Phase 0: Foundation & Infrastructure (No Dependencies)

**Objective:** Establish base infrastructure that everything else depends on.

```
Parallel Work Streams:
├── Stream 1 (Design System):
│   ├── A-006: Shift Badge Colors
│   ├── A-009-EXT: Tokenization Verification Script
│   ├── B-002: Inline Styles Conversion
│   ├── B-005: Icon System Migration
│   ├── B-014: Favicon/App Icons
│   ├── B-047: Disabled State Styling
│   ├── B-048: Hover State Consistency
│   └── B-049: Active/Pressed State Styling
│
├── Stream 2 (Accessibility Foundation):
│   ├── B-001: prefers-reduced-motion CSS
│   └── B-013: Modal Focus Management
│
├── Stream 3 (Backend Foundation):
│   ├── A-003-EXT: Scope Switcher Backend Contract
│   ├── A-019: Calendar Pagination Correctness
│   ├── B-016: Stale Cache Handling
│   ├── B-022: Structured Logging
│   ├── B-026: API Contract Documentation
│   ├── B-027: Rate Limiting Review
│   └── B-028: Error Response Standardization
│
├── Stream 4 (Observability):
│   ├── B-019: Analytics Event Taxonomy
│   ├── B-020: Error Tracking Integration
│   └── B-021: RUM Performance Monitoring
│
├── Stream 5 (Security Baseline):
│   ├── B-033: XSS Audit
│   ├── B-034: CSRF Verification
│   └── B-036: Sensitive Data Audit
│
├── Stream 6 (UI States):
│   ├── B-003: Loading States
│   ├── B-004: Error States
│   ├── B-008: Form Validation Errors
│   ├── B-009: Table Pagination
│   └── B-010: Date/Time Localization
│
├── Stream 7 (Release Infrastructure):
│   ├── B-023: Feature Flag Infrastructure
│   └── B-041: Test Data Strategy
│
└── Stream 8 (Performance Baseline):
    ├── B-030: Code Splitting Strategy
    └── B-031: Image Optimization
```

**Items in Phase 0:** 32 items
**Gate:** All foundation items complete before Phase 1

---

## Phase 1: Navigation & Context (Depends on Phase 0)

**Objective:** Complete navigation components and context switching.

```
Sequential Order:
1. A-001: Context Switcher Search Enhancement
2. A-002: Sidebar Collapse State Persistence
3. B-011: Mobile Navigation Collapse
4. B-012: Context Switcher Edge Cases (depends on A-001)
```

**Items in Phase 1:** 4 items
**Gate:** Navigation fully functional before Phase 2

---

## Phase 2: Core Components (Depends on Phase 0, 1)

**Objective:** Complete scope switcher and widget system.

```
Parallel Work:
├── A-003: Scope Switcher Integration (depends on A-003-EXT)
├── A-007: OnCallWidget Empty States
├── A-008: Widget Collapse Persistence
├── B-001-EXT: prefers-reduced-motion JavaScript (depends on B-001)
└── B-015: Brand Loading Animation (depends on B-001)

Sequential after A-003:
├── A-005: Calendar Empty States
├── A-018: Scope Switcher Data Correctness
└── A-020: Grant-Based UI Visibility (also depends on A-001)
```

**Items in Phase 2:** 8 items
**Gate:** Scope/context fully working before Page Rollout

---

## Phase 3: Page Tokenization (Depends on Phase 0, DS items)

**Objective:** Apply design tokens to all pages.

```
Parallel Work (can run concurrently):
├── A-009: Admin Pages Tokenization (15 pages)
├── A-010: Owner Pages Tokenization (14 pages)
├── A-011: Calendar Pages Polish (4 pages)
└── A-012: Remaining Pages Tokenization (30+ pages)

Sequential Dependencies:
└── A-004: Calendar Responsive Grid (can run parallel with tokenization)
```

**Items in Phase 3:** 5 items (but 60+ pages)
**Gate:** Zero hardcoded colors before Phase 4

---

## Phase 4: Quality & Polish (Depends on Phase 3)

**Objective:** Accessibility, RTL, localization, dark mode.

```
Parallel Work:
├── A-013: WCAG AA Audit
├── A-014: RTL Layout Polish
├── A-015: Missing Localization Strings
├── A-016: Dark Mode Polish
├── B-006: Print Styles Verification
├── B-007: Skeleton Loading (depends on B-003)
├── B-017: Partial Data Handling (depends on B-004)
├── B-018: Concurrent Edit Handling
├── B-037: Keyboard Navigation Audit (depends on B-013)
├── B-050: Offline/Flaky Network (depends on B-004)
├── B-051: Error Boundary (depends on B-020)
└── B-052: Toast/Alert Patterns (depends on B-004)
```

**Items in Phase 4:** 12 items
**Gate:** All quality items pass before Phase 5

---

## Phase 5: Testing (Depends on Phase 4)

**Objective:** Complete test suite and verification.

```
Sequential Order:
1. A-017: Playwright Test Suite Pass
2. B-038: Screen Reader Testing (depends on A-013)
3. B-039: Browser Compatibility Matrix
4. B-040: Visual Regression Tests
5. B-042: Flaky Test Policy

Parallel with Testing:
├── B-029: Bundle Analysis (depends on tokenization)
├── B-032: Render Performance (depends on tokenization)
└── B-035: Permission Alignment Audit (depends on A-020)
```

**Items in Phase 5:** 8 items
**Gate:** 100% test pass before Phase 6

---

## Phase 6: Documentation (Depends on Phase 5)

**Objective:** Complete all documentation.

```
Parallel Work:
├── B-024: Phased Rollout Strategy (depends on B-023)
├── B-025: Rollback Procedure (depends on B-023)
├── B-043: Component Usage Guidelines
├── B-044: Migration Notes
├── B-045: Changelog (depends on all work)
└── B-046: Operational Runbook (depends on B-023, B-021)
```

**Items in Phase 6:** 6 items
**Gate:** All docs complete before Phase 7

---

## Phase 7: Release Preparation (Final)

**Objective:** Final verification and release gate.

```
Sequential:
1. Final Release Readiness Checklist verification
2. All 86 criteria reviewed with evidence
3. All 62 blockers confirmed PASS
4. Discovery Playbook Phase 0 (internal testing)
```

**Gate:** Production release approved

---

# BLOCKERS GATE

## Known Unknowns Requiring Resolution

| Blocker ID | Question | Owner to Resolve | Default Assumption |
|------------|----------|------------------|-------------------|
| KU-001 | Analytics platform choice (B-019, B-021) | Product/DevOps | Use built-in browser Performance API + custom events to console.log; defer external platform |
| KU-002 | Error tracking platform choice (B-020) | Product/DevOps | Implement try-catch with structured logging; defer Sentry integration |
| KU-003 | Feature flag infrastructure (B-023) | Tech Lead | Use appsettings.json + database table for flags; no external service |
| KU-004 | Logging infrastructure (B-022) | DevOps | Use existing ASP.NET Core ILogger with JSON formatter |
| KU-005 | Browser support matrix (B-039) | Product | Chrome 90+, Firefox 90+, Safari 14+, Edge 90+; IE11 NOT supported |
| KU-006 | RUM platform choice (B-021) | Product | Defer external RUM; use Navigation Timing API + manual tracking |
| KU-007 | Visual regression tool (B-040) | QA | Use Playwright's built-in screenshot comparison |
| KU-008 | API versioning strategy (B-026) | Tech Lead | No breaking changes; new fields additive only |

## Critical Dependencies from External Systems

| Dependency | Impact | Mitigation |
|------------|--------|------------|
| Database schema for feature flags | Blocks B-023 | Create migration for FeatureFlags table |
| API contract for scope filtering | Blocks A-003 | BE must define contract before FE integration |
| Grant service methods | Blocks A-018, A-020 | IGrantService interface already exists |

## Environment Requirements

| Requirement | Status | Needed For |
|-------------|--------|------------|
| Playwright installed | Verify with `npx playwright --version` | All QA tests |
| Node.js for frontend tooling | Verify with `node --version` | Bundle analysis |
| Access to SharedResources.resx | Exists | Localization work |
| Test users for each role | Need to verify seed data | Permission testing |

---

# PHASE EXECUTION DETAILS

## Phase 0: Foundation & Infrastructure

### Task 0.1: Design Token Verification Script (A-009-EXT)

**Files:**
- Create: `scripts/verify-tokenization.sh`

**Step 1: Create verification script**

```bash
#!/bin/bash
# Tokenization Verification Script
# Usage: ./scripts/verify-tokenization.sh path/to/file.cshtml

FILE=$1
if [ -z "$FILE" ]; then
    echo "Usage: $0 <file-path>"
    exit 1
fi

echo "=== Tokenization Verification for $FILE ==="

# Count hardcoded hex colors
COLORS=$(grep -cE "#[0-9A-Fa-f]{3,8}" "$FILE" 2>/dev/null || echo 0)
echo "Hardcoded colors: $COLORS"

# Count rgba()
RGBA=$(grep -c "rgba\?" "$FILE" 2>/dev/null || echo 0)
echo "RGBA values: $RGBA"

# Count inline styles
INLINE=$(grep -c 'style="' "$FILE" 2>/dev/null || echo 0)
echo "Inline styles: $INLINE"

# Summary
if [ "$COLORS" -eq 0 ] && [ "$RGBA" -eq 0 ]; then
    echo "✅ PASS: No hardcoded colors"
    exit 0
else
    echo "❌ FAIL: Found hardcoded colors"
    grep -nE "#[0-9A-Fa-f]{3,8}" "$FILE" | head -20
    exit 1
fi
```

**Step 2: Make executable**

Run: `chmod +x scripts/verify-tokenization.sh` (on Unix) or create PowerShell equivalent for Windows

**Step 3: Verify script works**

Run: `bash scripts/verify-tokenization.sh Pages/Calendar/Month.cshtml`
Expected: FAIL with list of hardcoded colors

---

### Task 0.2: prefers-reduced-motion CSS (B-001)

**Files:**
- Modify: `wwwroot/css/tokens.css` (add at end)

**Step 1: Add reduced motion media query**

Add to end of `tokens.css`:

```css
/* Reduced Motion Support */
@media (prefers-reduced-motion: reduce) {
  *,
  *::before,
  *::after {
    animation-duration: 0.01ms !important;
    animation-iteration-count: 1 !important;
    transition-duration: 0.01ms !important;
    scroll-behavior: auto !important;
  }
}
```

**Step 2: Verify in browser**

1. Enable "Reduce motion" in OS settings (Windows: Settings > Accessibility > Visual effects)
2. Load any page with animations
3. Verify animations are instant

**Step 3: Commit**

```bash
git add wwwroot/css/tokens.css
git commit -m "feat(a11y): add prefers-reduced-motion support (B-001)

Co-Authored-By: Claude Opus 4.5 <noreply@anthropic.com>"
```

---

### Task 0.3: Shift Badge Colors (A-006)

**Files:**
- Modify: `wwwroot/css/components.css`
- Modify: `Pages/Calendar/Month.cshtml`
- Modify: `Pages/Calendar/Week.cshtml`
- Modify: `Pages/Calendar/Day.cshtml`

**Step 1: Define shift badge classes in components.css**

Add to `wwwroot/css/components.css`:

```css
/* Shift Badge Colors - Time-of-day metaphor */
.shift-morning {
  border-left: 4px solid var(--shift-morning);
  background-color: var(--shift-morning-bg, rgba(240, 193, 75, 0.1));
}

.shift-middle {
  border-left: 4px solid var(--shift-afternoon);
  background-color: var(--shift-afternoon-bg, rgba(107, 127, 89, 0.1));
}

.shift-noon {
  border-left: 4px solid var(--shift-noon);
  background-color: var(--shift-noon-bg, rgba(255, 230, 109, 0.1));
}

.shift-night {
  border-left: 4px solid var(--shift-night);
  background-color: var(--shift-night-bg, rgba(30, 58, 95, 0.1));
}

.chore-green {
  border-left: 4px solid var(--success);
  background-color: var(--success-bg, rgba(81, 207, 102, 0.1));
}

.onduty-hakam {
  border-left: 4px solid var(--shift-hakam);
  background-color: var(--shift-hakam-bg, rgba(139, 69, 19, 0.1));
}

.onduty-lead {
  border-left: 4px solid var(--shift-lead);
  background-color: var(--shift-lead-bg, rgba(255, 150, 113, 0.1));
}
```

**Step 2: Remove inline shift colors from Month.cshtml**

Find and remove the `<style>` block at lines ~416-422 that defines hardcoded shift colors.

**Step 3: Repeat for Week.cshtml and Day.cshtml**

**Step 4: Verify**

Run: `bash scripts/verify-tokenization.sh Pages/Calendar/Month.cshtml`
Check: Shift-related colors should not appear in grep results

---

### Task 0.4: Feature Flag Infrastructure (B-023)

**Files:**
- Create: `Migrations/YYYYMMDDHHMMSS_AddFeatureFlags.cs`
- Create: `Models/FeatureFlag.cs`
- Create: `Services/IFeatureFlagService.cs`
- Create: `Services/FeatureFlagService.cs`
- Modify: `Data/AppDbContext.cs`

**Step 1: Create FeatureFlag model**

```csharp
// Models/FeatureFlag.cs
namespace ShiftManager.Models;

public class FeatureFlag
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public string? Description { get; set; }
    public int? CompanyId { get; set; }  // null = global
    public int? UserId { get; set; }     // null = all users in scope
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
```

**Step 2: Create service interface**

```csharp
// Services/IFeatureFlagService.cs
namespace ShiftManager.Services;

public interface IFeatureFlagService
{
    Task<bool> IsEnabledAsync(string flagName, int? userId = null, int? companyId = null);
    Task SetFlagAsync(string flagName, bool isEnabled, int? companyId = null, int? userId = null);
    Task<IEnumerable<FeatureFlag>> GetAllFlagsAsync();
}
```

**Step 3: Implement service**

```csharp
// Services/FeatureFlagService.cs
namespace ShiftManager.Services;

public class FeatureFlagService : IFeatureFlagService
{
    private readonly AppDbContext _context;
    private readonly IMemoryCache _cache;

    public FeatureFlagService(AppDbContext context, IMemoryCache cache)
    {
        _context = context;
        _cache = cache;
    }

    public async Task<bool> IsEnabledAsync(string flagName, int? userId = null, int? companyId = null)
    {
        var cacheKey = $"ff:{flagName}:{userId}:{companyId}";

        if (_cache.TryGetValue(cacheKey, out bool cached))
            return cached;

        // Check user-specific, then company-specific, then global
        var flag = await _context.FeatureFlags
            .Where(f => f.Name == flagName)
            .Where(f =>
                (f.UserId == userId && f.CompanyId == companyId) ||
                (f.UserId == null && f.CompanyId == companyId) ||
                (f.UserId == null && f.CompanyId == null))
            .OrderByDescending(f => f.UserId != null)
            .ThenByDescending(f => f.CompanyId != null)
            .FirstOrDefaultAsync();

        var result = flag?.IsEnabled ?? false;
        _cache.Set(cacheKey, result, TimeSpan.FromMinutes(1));
        return result;
    }

    public async Task SetFlagAsync(string flagName, bool isEnabled, int? companyId = null, int? userId = null)
    {
        var existing = await _context.FeatureFlags
            .FirstOrDefaultAsync(f => f.Name == flagName && f.CompanyId == companyId && f.UserId == userId);

        if (existing != null)
        {
            existing.IsEnabled = isEnabled;
            existing.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            _context.FeatureFlags.Add(new FeatureFlag
            {
                Name = flagName,
                IsEnabled = isEnabled,
                CompanyId = companyId,
                UserId = userId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }

        await _context.SaveChangesAsync();

        // Invalidate cache
        var cacheKey = $"ff:{flagName}:{userId}:{companyId}";
        _cache.Remove(cacheKey);
    }

    public async Task<IEnumerable<FeatureFlag>> GetAllFlagsAsync()
    {
        return await _context.FeatureFlags.ToListAsync();
    }
}
```

**Step 4: Register in DI**

Add to `Program.cs`:
```csharp
builder.Services.AddScoped<IFeatureFlagService, FeatureFlagService>();
```

**Step 5: Create and run migration**

Run: `dotnet ef migrations add AddFeatureFlags`
Run: `dotnet ef database update`

**Step 6: Seed initial flags**

Create seed data for:
- `FF_NEW_NAV_ENABLED` (default: false)
- `FF_SCOPE_SWITCHER_ENABLED` (default: false)
- `FF_NEW_CALENDAR_STYLES` (default: false)
- `FF_WIDGETS_ENABLED` (default: false)

---

### Task 0.5: Error States Base Component (B-004)

**Files:**
- Create: `Pages/Shared/Components/ErrorBanner/Default.cshtml`
- Create: `Pages/Shared/Components/ErrorToast/Default.cshtml`
- Modify: `wwwroot/css/components.css`
- Add: Resource keys to `SharedResources.resx`

**Step 1: Create ErrorBanner component**

```html
@* Pages/Shared/Components/ErrorBanner/Default.cshtml *@
@model ErrorBannerViewModel

<div class="error-banner error-banner--@Model.Level" role="alert" aria-live="polite">
    <span class="error-banner__icon" aria-hidden="true">
        @switch (Model.Level)
        {
            case "error":
                @Html.Raw("⚠️")
                break;
            case "warning":
                @Html.Raw("⚡")
                break;
            case "info":
                @Html.Raw("ℹ️")
                break;
        }
    </span>
    <span class="error-banner__message">
        <loc key="@Model.MessageKey">@Model.FallbackMessage</loc>
    </span>
    @if (Model.ShowRetry)
    {
        <button class="error-banner__retry btn btn--secondary btn--small" onclick="@Model.RetryAction">
            <loc key="Error_Retry">Retry</loc>
        </button>
    }
    @if (Model.Dismissible)
    {
        <button class="error-banner__dismiss" aria-label="@Localizer["Dismiss"]" onclick="this.parentElement.remove()">
            ×
        </button>
    }
</div>
```

**Step 2: Add CSS for error states**

```css
/* Error Banner */
.error-banner {
  display: flex;
  align-items: center;
  gap: var(--spacing-3);
  padding: var(--spacing-3) var(--spacing-4);
  border-radius: var(--radius-md);
  margin-bottom: var(--spacing-4);
}

.error-banner--error {
  background-color: var(--error-bg);
  border: 1px solid var(--error);
  color: var(--error);
}

.error-banner--warning {
  background-color: var(--warning-bg);
  border: 1px solid var(--warning);
  color: var(--warning-text);
}

.error-banner--info {
  background-color: var(--info-bg);
  border: 1px solid var(--info);
  color: var(--info);
}

.error-banner__message {
  flex: 1;
}

.error-banner__dismiss {
  background: none;
  border: none;
  font-size: 1.5rem;
  cursor: pointer;
  color: inherit;
  opacity: 0.7;
}

.error-banner__dismiss:hover {
  opacity: 1;
}
```

**Step 3: Add resource keys**

Add to `SharedResources.resx`:
- `Error_Retry` = "Retry"
- `Error_NetworkError` = "Unable to connect. Please check your connection."
- `Error_ServerError` = "Something went wrong. Please try again."
- `Error_SessionExpired` = "Your session has expired. Please log in again."
- `Error_AccessDenied` = "You don't have access to this resource."

Add Hebrew translations to `SharedResources.he-IL.resx`.

---

(Continuing with remaining Phase 0 tasks...)

---

# VERIFICATION COMMANDS

## Build & Test Commands

```bash
# Build project
dotnet build

# Run all tests
dotnet test

# Run Playwright tests
cd qa-automation && npx playwright test

# Verify no hardcoded colors
grep -r "#[0-9A-Fa-f]{3,8}" Pages/**/*.cshtml --include="*.cshtml" | wc -l

# Verify reduced motion
# (Manual: enable OS reduced motion, test animations)

# Check bundle size
# (After build, check wwwroot/css file sizes)
```

## Per-Phase Verification

| Phase | Command | Expected Result |
|-------|---------|-----------------|
| 0 | `dotnet build` | Success, no errors |
| 0 | `dotnet test` | All pass |
| 0 | Feature flag check | Database has 4 flags |
| 1 | Context switcher works | Dropdown functional |
| 2 | Scope switcher works | Calendars filter by scope |
| 3 | Color grep | 0 hardcoded colors |
| 4 | axe DevTools | 0 critical/serious |
| 5 | `npx playwright test` | 100% pass |
| 6 | Docs exist | All 4 docs present |
| 7 | Checklist | 86/86 criteria pass |

---

# STATUS TRACKING

## Phase Status

| Phase | Started | Items Done | Items Total | Blocked | Status |
|-------|---------|------------|-------------|---------|--------|
| 0 | - | 0 | 32 | 0 | Not Started |
| 1 | - | 0 | 4 | 0 | Not Started |
| 2 | - | 0 | 8 | 0 | Not Started |
| 3 | - | 0 | 5 | 0 | Not Started |
| 4 | - | 0 | 12 | 0 | Not Started |
| 5 | - | 0 | 8 | 0 | Not Started |
| 6 | - | 0 | 6 | 0 | Not Started |
| 7 | - | 0 | 0 | 0 | Not Started |

## Release Readiness Progress

| Gate | Blockers Pass | Non-Blockers Pass | Total |
|------|---------------|-------------------|-------|
| Gate 0 | 3/5 | 0/0 | 3/5 |
| Gate 1 | 8/8 | 0/0 | 8/8 |
| Gate 2 | 1/4 | 0/2 | 1/6 |
| Gate 3 | 1/5 | 0/3 | 1/8 |
| Gate 4 | 1/3 | 0/2 | 1/5 |
| Gate 5 | 0/5 | 0/1 | 0/6 |
| Gate 6 | 0/5 | 0/3 | 0/8 |
| Gate 7 | 0/5 | 0/2 | 0/7 |
| Gate 8 | 0/1 | 0/4 | 0/5 |
| Gate 9 | 0/4 | 0/1 | 0/5 |
| Gate 10 | 0/2 | 0/3 | 0/5 |
| Gate 11 | 0/5 | 0/3 | 0/8 |
| **Total** | **14/62** | **0/24** | **14/86** |

---

# NEXT STEPS

1. **User Decision Required:**
   - Confirm analytics platform choice (or accept default)
   - Confirm error tracking platform choice (or accept default)
   - Confirm browser support matrix

2. **Begin Phase 0:**
   - Start with parallel work streams
   - Design System Engineer: A-009-EXT, A-006, B-047, B-048, B-049
   - Accessibility Specialist: B-001, B-013
   - Backend Engineer: A-003-EXT, B-023, B-026, B-028
   - Frontend Engineer: B-003, B-004, B-008

3. **First Milestone:**
   - Phase 0 complete = Foundation ready
   - All 32 items done with evidence
   - Proceed to Phase 1 (Navigation)

---

**Document Created:** 2026-01-31
**Last Updated:** 2026-01-31
**Total Items:** 75
**Phases:** 8
**Estimated Duration:** 8 weeks (per original document)
