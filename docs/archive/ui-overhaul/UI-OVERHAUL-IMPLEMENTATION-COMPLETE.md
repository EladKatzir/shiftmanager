# ShiftManager UI Overhaul - Complete Implementation Document

**Document Version:** 1.0
**Date:** 2026-01-31
**Status:** Production Release Preparation
**Author:** Claude (Opus 4.5)

---

## Executive Summary

This document provides a **complete, production-ready implementation checklist** for the ShiftManager UI Overhaul project. After comprehensive codebase analysis, the following critical gaps have been identified:

### Key Metrics

| Category | Current State | Target | Gap |
|----------|--------------|--------|-----|
| **Hardcoded Colors** | 267 occurrences in 40 files | 0 | -267 |
| **Pages with Inline Styles** | 45+ files | 0 critical | High |
| **prefers-reduced-motion** | 0 CSS rules | Full coverage | Missing |
| **Total CSHTML Pages** | ~96 files | All tokenized | ~70% remaining |
| **Design Tokens Coverage** | tokens.css complete | Applied everywhere | ~30% applied |

### Biggest Risks

1. **Calendar Pages (4 files)** - Each has 7+ hardcoded shift colors, 50+ inline styles
2. **Accessibility Motion** - Zero prefers-reduced-motion support = WCAG 2.1 AAA gap
3. **Table.cshtml** - 47 hardcoded colors, most complex page in codebase
4. **Localization** - Multiple pages with hardcoded English strings unverified

---

# SECTION A: Remaining Work from the Plan

This section covers all incomplete items from the approved UI Overhaul plan.

---

## A.1 - Phase 2: Navigation Shell Completion (5% remaining)

### A.1.1 - Context Switcher Search Enhancement

| Field | Value |
|-------|-------|
| **Description** | Add searchable filtering to the Context Switcher dropdown for users with multiple companies/molecules |
| **Rationale** | Plan Section 5.2 specifies "Searchable dropdown: Click opens searchable list" |
| **Acceptance Criteria** | 1. Search input appears when dropdown opens<br>2. Typing filters company/molecule list in real-time<br>3. Arrow key navigation works within filtered results<br>4. Enter key selects highlighted option<br>5. Works in both EN and HE locales |
| **Dependencies** | None |
| **Owner** | UI Designer Agent |
| **Complexity** | M |
| **Risk Level** | Low |
| **Test Approach** | Manual + Playwright: test search filtering, keyboard navigation, locale switching |
| **Files** | `Pages/Shared/Components/ContextSwitcher/Default.cshtml`, SharedResources |

### A.1.2 - Sidebar Collapse State Persistence

| Field | Value |
|-------|-------|
| **Description** | Persist sidebar category expand/collapse states across page navigation and browser sessions |
| **Rationale** | Plan Section 5.3: "Remember state: LocalStorage persists preferences" |
| **Acceptance Criteria** | 1. Collapsing a category saves state to localStorage<br>2. On page load, previously collapsed categories remain collapsed<br>3. Works across browser sessions<br>4. Transition animation (200ms) on expand/collapse |
| **Dependencies** | None |
| **Owner** | UI Designer Agent |
| **Complexity** | S |
| **Risk Level** | Low |
| **Test Approach** | Manual: collapse category, navigate away, return - verify state preserved |
| **Files** | `Pages/Shared/_Layout.cshtml`, new `wwwroot/js/sidebar.js` |

---

## A.2 - Phase 3: Calendar Redesign (50% remaining)

### A.2.1 - Scope Switcher Integration (Critical)

| Field | Value |
|-------|-------|
| **Description** | Integrate the ScopeSwitcher ViewComponent above all calendar views; wire to data filtering |
| **Rationale** | Plan Section 5.4: "Location: Directly above every calendar view" - Critical for navigation pain point |
| **Acceptance Criteria** | 1. ScopeSwitcher appears above Month/Week/Day/Table calendars<br>2. "Mine Only" scope shows only current user's assignments<br>3. "My Company" scope shows company-level data<br>4. "Full Molecule" scope (if grant) shows molecule-level<br>5. Scope changes trigger page refresh with new data<br>6. Selected scope persists via localStorage |
| **Dependencies** | ScopeSwitcher ViewComponent exists |
| **Owner** | UI Designer Agent + Backend integration |
| **Complexity** | L |
| **Risk Level** | Medium - data filtering logic must be correct |
| **Test Approach** | Playwright: verify data changes with scope; test with different user grant levels |
| **Files** | `Pages/Calendar/Month.cshtml`, `Week.cshtml`, `Day.cshtml`, `Table.cshtml`, backend services |

### A.2.2 - Calendar Responsive Grid

| Field | Value |
|-------|-------|
| **Description** | Make calendar grids responsive for mobile devices (<768px) |
| **Rationale** | Plan Section 1.1: "Calendar grid doesn't adapt; mobile sidebar broken" - Severity: High |
| **Acceptance Criteria** | 1. Calendar displays properly at 375px width (iPhone SE)<br>2. Calendar displays properly at 768px width (tablet)<br>3. Touch targets are at least 44x44px<br>4. Horizontal scroll available for month view if needed<br>5. Week view stacks to single column on mobile |
| **Dependencies** | None |
| **Owner** | UI Designer Agent |
| **Complexity** | M |
| **Risk Level** | Medium - layout changes may break existing functionality |
| **Test Approach** | Playwright responsive tests at 375px, 768px, 1024px viewports |
| **Files** | `wwwroot/css/calendar.css` |

### A.2.3 - Calendar Empty States

| Field | Value |
|-------|-------|
| **Description** | Implement context-aware empty states for calendars when no data exists |
| **Rationale** | Plan Appendix C.2: Empty states are guidance opportunities, not dead ends |
| **Acceptance Criteria** | 1. "Mine Only" empty: "No shifts assigned to you this month"<br>2. Company empty: "No shifts scheduled for [Company]"<br>3. Future month: "Schedule not yet published"<br>4. No access: "You don't have access to this calendar"<br>5. All messages use `<loc>` tags<br>6. Hebrew translations provided |
| **Dependencies** | A.2.1 (Scope Switcher) |
| **Owner** | UI Designer Agent + Localization Specialist Agent |
| **Complexity** | M |
| **Risk Level** | Low |
| **Test Approach** | Manual: test each empty state scenario in both locales |
| **Files** | Calendar pages, SharedResources.resx, SharedResources.he-IL.resx |

### A.2.4 - Shift Badge Time-of-Day Colors (Critical)

| Field | Value |
|-------|-------|
| **Description** | Replace 7 hardcoded shift colors with design token CSS classes in ALL calendar pages |
| **Rationale** | Plan Section 7.4 defines shift colors; current Calendar pages have hardcoded `#FF6B6B`, `#4ECDC4`, etc. |
| **Acceptance Criteria** | 1. `.shift-morning` uses `var(--shift-morning)` (#F0C14B)<br>2. `.shift-middle` uses appropriate token<br>3. `.shift-noon` uses appropriate token<br>4. `.shift-night` uses `var(--shift-night)` (#1E3A5F)<br>5. `.chore-green` uses `var(--success)`<br>6. `.onduty-hakam` uses `var(--shift-hakam)`<br>7. `.onduty-lead` uses appropriate token<br>8. Dark mode variants work correctly |
| **Dependencies** | None |
| **Owner** | Design Systems Lead Agent |
| **Complexity** | M |
| **Risk Level** | Low - straightforward color replacement |
| **Test Approach** | Visual inspection in light/dark modes; contrast verification |
| **Files** | `Month.cshtml`, `Week.cshtml`, `Day.cshtml`, `Table.cshtml`, `calendar.css`, `components.css` |

**Current Hardcoded Colors in Calendar Files:**

```
Month.cshtml:416:  .shift-morning { border-left: 4px solid #FF6B6B; }
Month.cshtml:417:  .shift-middle { border-left: 4px solid #4ECDC4; }
Month.cshtml:418:  .shift-noon { border-left: 4px solid #FFE66D; }
Month.cshtml:419:  .shift-night { border-left: 4px solid #95E1D3; }
Month.cshtml:420:  .chore-green { border-left: 4px solid #51CF66; }
Month.cshtml:421:  .onduty-hakam { border-left: 4px solid #845EC2; }
Month.cshtml:422:  .onduty-lead { border-left: 4px solid #FF9671; }
(Same pattern in Week.cshtml, Day.cshtml)
```

---

## A.3 - Phase 4: Widget System Completion (5% remaining)

### A.3.1 - OnCallWidget Empty States

| Field | Value |
|-------|-------|
| **Description** | Implement empty states for OnCallWidget when no contacts or no grants |
| **Rationale** | Plan Appendix C.3: Different empty states for different scenarios |
| **Acceptance Criteria** | 1. "No one is on-call right now" message with alert tone<br>2. "Add contacts to see who's on-call" for no-grants scenario<br>3. Proper icon and styling per design spec<br>4. All text localized |
| **Dependencies** | None |
| **Owner** | UI Designer Agent |
| **Complexity** | S |
| **Risk Level** | Low |
| **Test Approach** | Manual: test with no on-call data, no grants |
| **Files** | `Pages/Shared/Components/OnCallWidget/Default.cshtml`, SharedResources |

### A.3.2 - Widget Collapse Persistence

| Field | Value |
|-------|-------|
| **Description** | Persist OnCallWidget collapse state across sessions |
| **Rationale** | Widget already has collapse functionality; needs persistence |
| **Acceptance Criteria** | 1. Collapse state saved to localStorage<br>2. Restores on page load<br>3. Works for both main widget and nested OfficeNumbers widget |
| **Dependencies** | None |
| **Owner** | UI Designer Agent |
| **Complexity** | S |
| **Risk Level** | Low |
| **Test Approach** | Manual: collapse, refresh, verify state |
| **Files** | `OnCallWidget/Default.cshtml` (JavaScript section) |

---

## A.4 - Phase 5: Page Rollout (0% complete)

### A.4.1 - Admin Pages Tokenization (15 pages)

| Field | Value |
|-------|-------|
| **Description** | Apply design tokens to all Admin pages, replacing hardcoded colors and inline styles |
| **Rationale** | Plan Phase 5 Task 5.1: Systematic rollout |
| **Acceptance Criteria** | For EACH page:<br>1. Zero hardcoded hex colors<br>2. Uses `.card`, `.btn`, `.badge` component classes<br>3. All visible text uses `<loc>` tags<br>4. ARIA labels present and localized<br>5. Works in light and dark modes |
| **Dependencies** | None |
| **Owner** | Design Systems Lead Agent (parallel execution) |
| **Complexity** | L (aggregate) |
| **Risk Level** | Medium - many files to update |
| **Test Approach** | Playwright visual tests; locale switch tests |

**Files to Update:**

| File | Hardcoded Colors | Priority |
|------|------------------|----------|
| `Admin/Analytics.cshtml` | 4 colors | High |
| `Admin/AuditLog.cshtml` | 1 color | Medium |
| `Admin/Companies.cshtml` | 4+ colors | High |
| `Admin/Config.cshtml` | 3 colors | Medium |
| `Admin/Directors.cshtml` | Unknown | Medium |
| `Admin/EditProfile.cshtml` | Unknown | Low |
| `Admin/Index.cshtml` | Unknown | Medium |
| `Admin/Settings/Index.cshtml` | 4 colors | High |
| `Admin/SetupTasks/Index.cshtml` | 4 colors | High |
| `Admin/ShiftTypes.cshtml` | Unknown | Medium |
| `Admin/Users.cshtml` | Unknown | Medium |
| `Admin/Organization/Index.cshtml` | 1 color | Medium |
| `Admin/Organization/Areas/Index.cshtml` | Unknown | Medium |
| `Admin/Organization/Departments/Index.cshtml` | Unknown | Medium |
| `Admin/Organization/Grants/Index.cshtml` | 4 colors | High |
| `Admin/Organization/Grants/Assign.cshtml` | Unknown | Medium |
| `Admin/Organization/JobTypes/Index.cshtml` | 3 colors | Medium |
| `Admin/Organization/Molecules/Index.cshtml` | 5 colors | High |
| `Admin/Organization/Projects/Index.cshtml` | 2 colors | Medium |
| `Admin/Organization/Roles/Index.cshtml` | 4 colors | High |
| `Admin/Organization/Roles/Assign.cshtml` | 1 color | Low |
| `Admin/Organization/ShiftGroupings/Index.cshtml` | 3 colors | Medium |

### A.4.2 - Owner Pages Tokenization (14 pages)

| Field | Value |
|-------|-------|
| **Description** | Apply design tokens to all Owner pages |
| **Rationale** | Plan Phase 5 Task 5.2 |
| **Acceptance Criteria** | Same as A.4.1 |
| **Dependencies** | None |
| **Owner** | Design Systems Lead Agent (parallel execution) |
| **Complexity** | L (aggregate) |
| **Risk Level** | Medium |
| **Test Approach** | Same as A.4.1 |

**Files to Update:**

| File | Hardcoded Colors | Priority |
|------|------------------|----------|
| `Owner/Index.cshtml` | Unknown | High |
| `Owner/Backup.cshtml` | Unknown | Medium |
| `Owner/Blueprints.cshtml` | 6 colors | High |
| `Owner/ClearCompanySelection.cshtml` | Unknown | Low |
| `Owner/DataLifecycle.cshtml` | Unknown | Medium |
| `Owner/DatabaseConsole.cshtml` | Unknown | High |
| `Owner/EmailConfig.cshtml` | 4 colors | High |
| `Owner/EmailTemplates.cshtml` | 9 colors | **Critical** |
| `Owner/FeatureFlags.cshtml` | 7 colors | **Critical** |
| `Owner/GameConfig.cshtml` | 1 color | Low |
| `Owner/GriffinConfig.cshtml` | 22 colors | **Critical** |
| `Owner/LanguageEditMode.cshtml` | Unknown | Low |
| `Owner/LanguageManagement.cshtml` | 6 colors | High |
| `Owner/MasterPrograms.cshtml` | Unknown | Medium |
| `Owner/Programs.cshtml` | Unknown | Medium |
| `Owner/SelectCompany.cshtml` | Unknown | Low |
| `Owner/SystemHealth.cshtml` | 11 colors | **Critical** |

### A.4.3 - Calendar Pages Polish (4 pages)

| Field | Value |
|-------|-------|
| **Description** | Complete tokenization and polish for all Calendar pages |
| **Rationale** | These pages have the MOST hardcoded colors and are core to the application |
| **Acceptance Criteria** | 1. All 267+ hardcoded colors replaced<br>2. All inline styles converted to classes<br>3. Responsive grid working<br>4. Scope switcher integrated<br>5. Empty states implemented<br>6. Print styles verified |
| **Dependencies** | A.2.1, A.2.2, A.2.3, A.2.4 |
| **Owner** | Design Systems Lead Agent |
| **Complexity** | L |
| **Risk Level** | High - complex pages |
| **Test Approach** | Full Playwright test suite |

**Detailed Analysis - Table.cshtml (worst case):**

```
Total hardcoded colors: 47+
Critical inline styles: 40+
Examples:
- Line 115: background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
- Line 159: border: 2px dashed #007bff;
- Line 289-316: Multiple status colors (#cce5ff, #f8d7da, #fff3cd, etc.)
- Line 499-542: Fill option colors (#f97316, #10b981, etc.)
```

### A.4.4 - Remaining Pages (30+ pages)

| Field | Value |
|-------|-------|
| **Description** | Apply design tokens to all remaining pages |
| **Rationale** | Complete coverage required |
| **Acceptance Criteria** | Same as A.4.1 |
| **Dependencies** | None |
| **Owner** | Design Systems Lead Agent |
| **Complexity** | L |
| **Risk Level** | Medium |

**Pages requiring work:**

- `Auth/Login.cshtml` - 5 colors
- `Auth/Signup.cshtml` - 2 colors
- `Auth/ForgotPassword.cshtml` - 5 colors
- `My/Index.cshtml` - 16 colors (**Critical**)
- `MyTeam/Index.cshtml` - 36 colors (**Critical**)
- `Assignments/Manage.cshtml` - 4 colors
- `Chores/Calendar.cshtml` - Multiple inline styles
- `Director/Index.cshtml` - 5 colors
- `Friends/Index.cshtml` - Unknown
- `Home/Index.cshtml` - Unknown
- `Public/Chores.cshtml` - Unknown
- `Public/Feedback.cshtml` - 6 colors
- `Public/OnDuty.cshtml` - 4 colors
- `Requests/Index.cshtml` - 1 color
- `Schedule/Index.cshtml` - 3 colors
- `AccessDenied.cshtml` - Uses inline styles
- `Error.cshtml` - Unknown

---

## A.5 - Phase 6: Polish, RTL, Accessibility (40% remaining)

### A.5.1 - WCAG AA Accessibility Audit

| Field | Value |
|-------|-------|
| **Description** | Run formal WCAG AA audit and fix all violations |
| **Rationale** | Plan Section 2.2: "Accessible by Default - WCAG AA compliance built-in" |
| **Acceptance Criteria** | 1. Zero critical violations<br>2. Zero serious violations<br>3. Color contrast ratios verified (4.5:1 minimum)<br>4. All images have alt text<br>5. All form inputs have labels<br>6. Focus management correct<br>7. Heading hierarchy correct |
| **Dependencies** | A.4 (all pages updated) |
| **Owner** | Accessibility Specialist Agent |
| **Complexity** | L |
| **Risk Level** | Medium |
| **Test Approach** | axe DevTools automated scan; manual keyboard testing |
| **Files** | All pages |

### A.5.2 - RTL Layout Polish

| Field | Value |
|-------|-------|
| **Description** | Verify and fix RTL layout for Hebrew locale |
| **Rationale** | Hebrew is a supported language; RTL must work correctly |
| **Acceptance Criteria** | 1. Sidebar position correct in RTL<br>2. Icon directions correct<br>3. Text alignment correct<br>4. Form button order correct (primary on left)<br>5. Calendar grid flows correctly<br>6. No text overlap or clipping |
| **Dependencies** | A.4 (all pages updated) |
| **Owner** | Accessibility Specialist Agent |
| **Complexity** | M |
| **Risk Level** | Medium |
| **Test Approach** | Full site walkthrough in Hebrew locale |
| **Files** | All CSS files |

### A.5.3 - Missing Localization Strings

| Field | Value |
|-------|-------|
| **Description** | Identify and add all missing localization strings |
| **Rationale** | 100% localization coverage required |
| **Acceptance Criteria** | 1. Zero hardcoded English text in CSHTML<br>2. All strings in SharedResources.resx<br>3. All strings translated in he-IL.resx<br>4. ARIA labels localized<br>5. Placeholder text localized |
| **Dependencies** | None |
| **Owner** | Localization Specialist Agent |
| **Complexity** | M |
| **Risk Level** | Low |
| **Test Approach** | grep for hardcoded text; locale switch test |
| **Files** | All CSHTML, SharedResources.resx, SharedResources.he-IL.resx |

### A.5.4 - Dark Mode Polish

| Field | Value |
|-------|-------|
| **Description** | Verify dark mode works correctly on all pages |
| **Rationale** | Dark mode is specified in tokens.css |
| **Acceptance Criteria** | 1. All pages render correctly in dark mode<br>2. No missing variable overrides<br>3. Sufficient contrast maintained<br>4. Form inputs visible<br>5. Focus states visible |
| **Dependencies** | A.4 (all pages updated) |
| **Owner** | UI Designer Agent |
| **Complexity** | M |
| **Risk Level** | Low |
| **Test Approach** | Full site walkthrough with `data-theme="dark"` |
| **Files** | All CSS files |

### A.5.5 - Final Playwright Test Suite Pass

| Field | Value |
|-------|-------|
| **Description** | Run full Playwright test suite and fix all failures |
| **Rationale** | Must have automated verification before release |
| **Acceptance Criteria** | 1. 100% test pass rate (or documented exceptions)<br>2. Tests run in both EN and HE locales<br>3. Tests run at multiple viewports<br>4. No flaky tests |
| **Dependencies** | All other tasks complete |
| **Owner** | QA Agent |
| **Complexity** | M |
| **Risk Level** | Medium |
| **Test Approach** | Run `npx playwright test` in qa-automation folder |
| **Files** | qa-automation test files |

---

# SECTION B: Gaps Outside the Plan

This section covers gaps that were discovered during implementation but not formally specified in the original plan.

---

## B.1 - Accessibility: prefers-reduced-motion (CRITICAL GAP)

| Field | Value |
|-------|-------|
| **Description** | Add `@media (prefers-reduced-motion: reduce)` queries to disable animations for users who prefer reduced motion |
| **Rationale** | WCAG 2.1 AAA criterion 2.3.3 (Animation from Interactions); important for vestibular disorders |
| **Acceptance Criteria** | 1. All CSS animations respect prefers-reduced-motion<br>2. All CSS transitions respect prefers-reduced-motion<br>3. JavaScript animations respect prefers-reduced-motion |
| **Dependencies** | None |
| **Owner** | Accessibility Specialist Agent |
| **Complexity** | S |
| **Risk Level** | Low |
| **Test Approach** | Enable "Reduce motion" in OS settings; verify no animations |

**Current State:** ZERO prefers-reduced-motion rules found in codebase (only 2 mentions in documentation planning file).

**Implementation Required:**

```css
/* Add to tokens.css or components.css */
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

**Files to Update:**
- `wwwroot/css/tokens.css`
- `wwwroot/css/components.css`
- `wwwroot/css/calendar.css`
- `wwwroot/css/widgets.css`
- `wwwroot/css/navigation.css`
- Any JavaScript with animations

---

## B.2 - Inline Styles in Calendar Pages (CRITICAL GAP)

| Field | Value |
|-------|-------|
| **Description** | Convert all inline `style=""` attributes to CSS classes in Calendar pages |
| **Rationale** | Inline styles bypass dark mode, theming, and are maintenance nightmares |
| **Acceptance Criteria** | 1. Zero `style=""` attributes for layout/colors<br>2. Only acceptable inline styles: `display: none` for JS toggle states |
| **Dependencies** | Design token classes exist |
| **Owner** | Design Systems Lead Agent |
| **Complexity** | L |
| **Risk Level** | Medium - may break layout |
| **Test Approach** | Visual comparison before/after |

**Current State:** Calendar/Table.cshtml alone has 40+ inline style occurrences:

```
Line 1292: <div class="view-mode-toggle" style="margin-bottom: 1rem;">
Line 1329: style="margin: 1rem 0; padding: 1rem; border-radius: 0.5rem; background: #fff3cd; border: 1px solid #ffc107;"
Line 1368: style="color: #666;"
Line 1524-1536: Multiple form styling inline
Line 2510-2519: Modal styling inline
```

---

## B.3 - Missing Loading States

| Field | Value |
|-------|-------|
| **Description** | Implement loading spinners/skeletons for async operations |
| **Rationale** | Plan Appendix C.5 specifies loading states but they are not implemented |
| **Acceptance Criteria** | 1. Page loading shows branded spinner<br>2. Calendar loading shows skeleton<br>3. Form submission shows button loading state<br>4. 300ms delay before showing spinner (avoid flash) |
| **Dependencies** | None |
| **Owner** | UI Designer Agent |
| **Complexity** | M |
| **Risk Level** | Low |
| **Test Approach** | Throttle network in DevTools; verify loading states appear |
| **Files** | New CSS for loading states, JavaScript integration |

---

## B.4 - Missing Error States

| Field | Value |
|-------|-------|
| **Description** | Implement error toast, network disconnect banner, session timeout modal |
| **Rationale** | Plan Appendix D specifies these but they may not be implemented |
| **Acceptance Criteria** | 1. Error toast appears on API failure<br>2. Network disconnect banner shows when offline<br>3. Session timeout modal shows 2 minutes before expiry<br>4. All messages localized |
| **Dependencies** | None |
| **Owner** | UI Designer Agent |
| **Complexity** | M |
| **Risk Level** | Low |
| **Test Approach** | Simulate network errors; verify error states |
| **Files** | New components, JavaScript integration |

---

## B.5 - Icon System Migration (Deferred)

| Field | Value |
|-------|-------|
| **Description** | Replace emoji icons with SVG icon library (Lucide Icons recommended) |
| **Rationale** | Plan Section 3.7: "Replace emoji with SVG icon library" - currently using emojis throughout |
| **Acceptance Criteria** | 1. All interactive icons use SVG<br>2. Icons support `currentColor`<br>3. Icons scale properly<br>4. Consistent sizes (16px, 20px, 24px) |
| **Dependencies** | None |
| **Owner** | Design Systems Lead Agent |
| **Complexity** | L |
| **Risk Level** | Medium |
| **Test Approach** | Visual comparison; verify icons display at all sizes |
| **Status** | **DEFERRED** - Can be done post-launch |

---

## B.6 - Print Styles Verification

| Field | Value |
|-------|-------|
| **Description** | Verify print.css works correctly for all printable pages |
| **Rationale** | Plan Appendix G specifies print requirements; print.css exists but may not cover new components |
| **Acceptance Criteria** | 1. Calendar prints cleanly<br>2. No UI chrome in print<br>3. Colors are B&W friendly<br>4. Page breaks avoid splitting shifts |
| **Dependencies** | None |
| **Owner** | UI Designer Agent |
| **Complexity** | S |
| **Risk Level** | Low |
| **Test Approach** | Print preview in browser |
| **Files** | `wwwroot/css/print.css`, calendar pages |

---

## B.7 - Skeleton Loading for Calendars

| Field | Value |
|-------|-------|
| **Description** | Implement skeleton loading animation for calendar grids |
| **Rationale** | Plan Appendix C.5 specifies skeleton loading for predictable layouts |
| **Acceptance Criteria** | 1. Skeleton shows calendar grid shape<br>2. Pulse animation on skeleton bars<br>3. Content fades in over skeleton (200ms) |
| **Dependencies** | None |
| **Owner** | UI Designer Agent |
| **Complexity** | M |
| **Risk Level** | Low |
| **Test Approach** | Throttle network; verify skeleton appears |
| **Files** | New CSS, calendar pages |

---

## B.8 - Form Validation Error States

| Field | Value |
|-------|-------|
| **Description** | Ensure all forms show inline validation errors per Plan Appendix F |
| **Rationale** | Plan Appendix F.4 specifies error message placement and styling |
| **Acceptance Criteria** | 1. Error messages appear below fields<br>2. Red text with warning icon<br>3. `aria-live="polite"` for screen readers<br>4. Messages localized |
| **Dependencies** | None |
| **Owner** | UI Designer Agent |
| **Complexity** | M |
| **Risk Level** | Low |
| **Test Approach** | Submit forms with invalid data; verify error display |
| **Files** | Form pages, `components.css` |

---

## B.9 - Table Pagination Controls

| Field | Value |
|-------|-------|
| **Description** | Verify pagination controls match Plan Appendix H.3 specification |
| **Rationale** | Tables must have consistent pagination UX |
| **Acceptance Criteria** | 1. "Showing X-Y of Z" always visible<br>2. Page numbers show first, last, current ±1<br>3. Prev/Next disabled at boundaries<br>4. Per-page dropdown works |
| **Dependencies** | None |
| **Owner** | UI Designer Agent |
| **Complexity** | S |
| **Risk Level** | Low |
| **Test Approach** | Navigate through table pages; verify controls |
| **Files** | Table components, pagination CSS |

---

## B.10 - Date/Time Format Localization

| Field | Value |
|-------|-------|
| **Description** | Verify date/time formatting uses culture-aware patterns per Plan Appendix I |
| **Rationale** | Hebrew dates should show day before month |
| **Acceptance Criteria** | 1. English: "January 30, 2026"<br>2. Hebrew: "30 בינואר 2026"<br>3. Calendar headers use correct format<br>4. Relative times localized ("5 minutes ago" / "לפני 5 דקות") |
| **Dependencies** | None |
| **Owner** | Localization Specialist Agent |
| **Complexity** | S |
| **Risk Level** | Low |
| **Test Approach** | Switch locales; verify date formats |
| **Files** | C# date formatting code, Razor pages |

---

## B.11 - Mobile Navigation Collapse

| Field | Value |
|-------|-------|
| **Description** | Ensure sidebar collapses/hides properly on mobile |
| **Rationale** | Plan Section 1.1: "mobile sidebar broken" |
| **Acceptance Criteria** | 1. Sidebar hidden by default on mobile (<768px)<br>2. Hamburger menu opens sidebar as overlay<br>3. Tap outside closes sidebar<br>4. Smooth animation on open/close |
| **Dependencies** | None |
| **Owner** | UI Designer Agent |
| **Complexity** | M |
| **Risk Level** | Medium |
| **Test Approach** | Test on mobile viewport; verify sidebar behavior |
| **Files** | `_Layout.cshtml`, `navigation.css`, JavaScript |

---

## B.12 - Context Switcher Edge Cases

| Field | Value |
|-------|-------|
| **Description** | Handle edge cases for Context Switcher |
| **Rationale** | Plan Section 5.2 lists edge cases |
| **Acceptance Criteria** | 1. Single context user: switcher hidden entirely<br>2. Owner/Director: full hierarchy tree shown<br>3. New user: defaults to assigned company<br>4. No grants: graceful empty state |
| **Dependencies** | A.1.1 |
| **Owner** | UI Designer Agent |
| **Complexity** | S |
| **Risk Level** | Low |
| **Test Approach** | Test with users of different grant levels |
| **Files** | ContextSwitcher component |

---

## B.13 - Modal Focus Management

| Field | Value |
|-------|-------|
| **Description** | Ensure modals trap focus and manage focus on open/close |
| **Rationale** | Plan Appendix E.5 specifies modal accessibility requirements |
| **Acceptance Criteria** | 1. Focus moves to first focusable element on open<br>2. Tab cycles within modal only<br>3. Focus returns to trigger on close<br>4. ESC key closes modal<br>5. Backdrop click closes modal |
| **Dependencies** | None |
| **Owner** | Accessibility Specialist Agent |
| **Complexity** | M |
| **Risk Level** | Low |
| **Test Approach** | Keyboard-only navigation through modal workflows |
| **Files** | Modal components, JavaScript |

---

## B.14 - Favicon and App Icons

| Field | Value |
|-------|-------|
| **Description** | Create and implement SHIFTY brand icons for favicon and app icons |
| **Rationale** | Plan Section 3.1 specifies "Compact" logo variant for app icon/favicon |
| **Acceptance Criteria** | 1. favicon.ico uses SHIFTY compact "S"<br>2. Apple touch icon provided<br>3. Android manifest icons provided<br>4. PWA icons if applicable |
| **Dependencies** | Brand assets finalized |
| **Owner** | Brand Designer Agent |
| **Complexity** | S |
| **Risk Level** | Low |
| **Test Approach** | Verify icons display in browser tab, bookmark |
| **Files** | `wwwroot/favicon.ico`, manifest files |

---

## B.15 - Brand Loading Animation

| Field | Value |
|-------|-------|
| **Description** | Create SHIFTY swoosh loading animation |
| **Rationale** | Plan Section 3.1 specifies "Animated" logo variant; Appendix C.5 specifies loading spinner design |
| **Acceptance Criteria** | 1. Swoosh "draws in" animation<br>2. Navy blue in light mode, sky blue in dark mode<br>3. 1.5s animation cycle<br>4. 300ms delay before showing |
| **Dependencies** | Brand assets, prefers-reduced-motion support |
| **Owner** | Brand Designer Agent |
| **Complexity** | M |
| **Risk Level** | Low |
| **Test Approach** | Throttle network; verify loading animation |
| **Files** | New CSS/SVG animation |

---

# SECTION C: Rumsfeld Matrix

## Known Knowns (What we know we need to do)

| Item | Status | Confidence |
|------|--------|------------|
| 267 hardcoded hex colors in 40 files | Identified | 100% |
| 7 shift colors hardcoded in each Calendar page | Identified | 100% |
| Table.cshtml has 47+ hardcoded colors | Identified | 100% |
| Zero prefers-reduced-motion CSS rules | Identified | 100% |
| Scope Switcher not integrated with calendars | Identified | 100% |
| Design tokens file is complete | Verified | 100% |
| RTL support exists (83 occurrences) | Verified | 100% |
| print.css exists (506 lines) | Verified | 100% |
| OnCallWidget exists and functions | Verified | 100% |
| ScopeSwitcher component exists | Verified | 100% |
| ContextSwitcher component exists | Verified | 100% |

## Known Unknowns (What we know we don't know)

| Item | Risk | Mitigation |
|------|------|------------|
| Exact count of missing localization strings | Medium | Run comprehensive string audit |
| How many pages have inline styles that break dark mode | Medium | Grep for `style=` + dark mode test |
| Whether all modals have proper focus management | Low | Manual audit |
| Whether all forms have proper validation display | Low | Manual audit |
| Performance impact of tokenization | Low | Lighthouse before/after |
| Whether existing Playwright tests cover all new components | Medium | Review test coverage |
| How backend services filter by scope | Medium | Code review |

## Unknown Unknowns (What we don't know we don't know)

| Category | Potential Issues | Detection Strategy |
|----------|------------------|-------------------|
| **Browser Compatibility** | CSS variables in older browsers | Test on Edge, Firefox, Safari, Chrome |
| **Screen Reader Behavior** | ARIA implementation nuances | Test with NVDA, VoiceOver |
| **RTL Edge Cases** | Complex layouts in Hebrew | Manual walkthrough with native speaker |
| **Print Variations** | Browser-specific print rendering | Print preview in multiple browsers |
| **Mobile Gestures** | Touch interactions on widgets | Test on real mobile devices |
| **Data Combinations** | Empty + filtered + scoped states together | Test edge case combinations |
| **Third-party CSS Conflicts** | Bootstrap or other library conflicts | Check for `!important` overrides |
| **Performance Regressions** | Tokenization impact on initial load | Monitor Core Web Vitals |
| **Session State Interactions** | localStorage + server state sync | Test with expired sessions |
| **Memory Leaks** | JavaScript event listeners in widgets | Chrome DevTools memory profiling |

---

# SECTION D: Release Readiness Checklist

## D.1 - Visual Design (Pass/Fail)

| Criterion | Test Method | Pass Criteria | Status |
|-----------|-------------|---------------|--------|
| Zero hardcoded colors in CSHTML | `grep -r "#[0-9A-Fa-f]{6}"` | 0 matches | ⬜ FAIL |
| Zero hardcoded rgba() in CSHTML | `grep -r "rgba("` | 0 matches (except tokens.css) | ⬜ FAIL |
| All pages use component classes | Manual review | `.card`, `.btn`, `.badge` present | ⬜ PENDING |
| Dark mode renders correctly | Toggle `data-theme="dark"` | No visual issues | ⬜ PENDING |
| Light mode renders correctly | Default state | No visual issues | ⬜ PENDING |
| Brand colors consistent | Visual comparison | Matches tokens.css | ⬜ PENDING |
| Typography consistent | Visual comparison | Font scale correct | ⬜ PENDING |
| Spacing consistent | Visual comparison | 8px grid alignment | ⬜ PENDING |
| Shift badges use correct colors | Visual check | Time-of-day metaphor | ⬜ FAIL |

## D.2 - Components (Pass/Fail)

| Criterion | Test Method | Pass Criteria | Status |
|-----------|-------------|---------------|--------|
| Context Switcher works | Click dropdown | Shows companies, filters work | ⬜ PENDING |
| Scope Switcher works | Click buttons | Calendars filter by scope | ⬜ PENDING |
| OnCallWidget displays | Load sidebar | Shows contacts or empty state | ⬜ PENDING |
| Sidebar collapses | Click categories | Expand/collapse with animation | ⬜ PENDING |
| Modals open/close | Click modal triggers | Focus management correct | ⬜ PENDING |
| Toasts display | Trigger notification | Appears with correct styling | ⬜ PENDING |
| Forms validate | Submit invalid data | Error messages display | ⬜ PENDING |
| Tables paginate | Navigate pages | Pagination controls work | ⬜ PENDING |

## D.3 - Responsiveness (Pass/Fail)

| Criterion | Test Method | Pass Criteria | Status |
|-----------|-------------|---------------|--------|
| 375px (mobile) | Chrome DevTools | Layout intact, no overflow | ⬜ PENDING |
| 768px (tablet) | Chrome DevTools | Layout adapts correctly | ⬜ PENDING |
| 1024px (desktop) | Chrome DevTools | Full layout visible | ⬜ PENDING |
| 1920px (large) | Browser window | Content centered, not stretched | ⬜ PENDING |
| Touch targets 44x44px | Measure in DevTools | All interactive elements | ⬜ PENDING |
| Sidebar mobile behavior | Mobile viewport | Collapses to hamburger | ⬜ PENDING |
| Calendar mobile layout | Mobile viewport | Readable without h-scroll | ⬜ PENDING |

## D.4 - Accessibility (Pass/Fail)

| Criterion | Test Method | Pass Criteria | Status |
|-----------|-------------|---------------|--------|
| WCAG AA contrast (4.5:1) | axe DevTools | 0 contrast violations | ⬜ PENDING |
| Focus visible | Keyboard navigation | All elements show focus ring | ⬜ PENDING |
| Tab order logical | Tab through page | Follows visual order | ⬜ PENDING |
| Skip link present | Press Tab on load | "Skip to main content" | ⬜ PENDING |
| ARIA labels present | Screen reader test | All interactive elements labeled | ⬜ PENDING |
| Heading hierarchy | axe DevTools | No skipped heading levels | ⬜ PENDING |
| Alt text on images | axe DevTools | All images have alt | ⬜ PENDING |
| Form labels present | axe DevTools | All inputs labeled | ⬜ PENDING |
| prefers-reduced-motion | Enable OS setting | Animations disabled | ⬜ FAIL |
| Screen reader compatible | NVDA/VoiceOver test | Content readable | ⬜ PENDING |

## D.5 - Localization (Pass/Fail)

| Criterion | Test Method | Pass Criteria | Status |
|-----------|-------------|---------------|--------|
| Zero hardcoded English | grep for common words | 0 matches | ⬜ PENDING |
| All strings in resx | Verify SharedResources | Keys present | ⬜ PENDING |
| Hebrew translations present | Check he-IL.resx | All keys translated | ⬜ PENDING |
| RTL layout correct | Switch to Hebrew | Mirrored correctly | ⬜ PENDING |
| Date formats localized | View calendar in HE | Day before month | ⬜ PENDING |
| ARIA labels localized | View source in HE | Hebrew values | ⬜ PENDING |
| Placeholders localized | View forms in HE | Hebrew text | ⬜ PENDING |
| Error messages localized | Trigger errors in HE | Hebrew messages | ⬜ PENDING |

## D.6 - Performance (Pass/Fail)

| Criterion | Test Method | Pass Criteria | Status |
|-----------|-------------|---------------|--------|
| LCP < 2.5s | Lighthouse | Green score | ⬜ PENDING |
| FID < 100ms | Lighthouse | Green score | ⬜ PENDING |
| CLS < 0.1 | Lighthouse | Green score | ⬜ PENDING |
| CSS bundle < 100KB | Check file size | Under limit | ⬜ PENDING |
| No layout thrashing | Performance profiler | No forced reflows | ⬜ PENDING |

## D.7 - QA/Testing (Pass/Fail)

| Criterion | Test Method | Pass Criteria | Status |
|-----------|-------------|---------------|--------|
| Playwright tests pass | `npx playwright test` | 100% (or documented) | ⬜ PENDING |
| Tests run in EN locale | Configure locale | All pass | ⬜ PENDING |
| Tests run in HE locale | Configure locale | All pass | ⬜ PENDING |
| Tests run at 375px | Configure viewport | All pass | ⬜ PENDING |
| Tests run at 1024px | Configure viewport | All pass | ⬜ PENDING |
| No flaky tests | Run 3x | Consistent results | ⬜ PENDING |

## D.8 - Security (Pass/Fail)

| Criterion | Test Method | Pass Criteria | Status |
|-----------|-------------|---------------|--------|
| No inline event handlers | grep for `onclick=` etc | Minimal matches | ⬜ PENDING |
| CSP compatible | Check console errors | No CSP violations | ⬜ PENDING |
| No sensitive data exposed | Code review | No API keys, passwords | ⬜ PASS |
| localStorage keys prefixed | Code review | All use `shifty_` prefix | ⬜ PENDING |

---

## Summary

### Work Items by Priority

| Priority | Count | Examples |
|----------|-------|----------|
| **Critical** | 8 | Calendar shift colors, Table.cshtml, prefers-reduced-motion, Scope Switcher |
| **High** | 15 | Admin pages, Owner pages, Calendar responsive, RTL polish |
| **Medium** | 12 | Context Switcher search, Widget states, Error states |
| **Low** | 5 | Icon system migration, Print verification, Brand loading animation |
| **Deferred** | 1 | Icon system (post-launch) |

### Estimated Total Work Items

| Section | Count |
|---------|-------|
| Section A (Plan Items) | 25 |
| Section B (Gap Items) | 15 |
| **Total** | **40** |

### Files Requiring Modification

| Category | Count |
|---------|-------|
| Calendar pages | 4 |
| Admin pages | 22 |
| Owner pages | 17 |
| Other pages | 15+ |
| CSS files | 7 |
| SharedResources | 2 |
| **Total** | **65+** |

---

**Document Complete.**
**Next Step:** Execute work items in priority order, starting with Critical items.
