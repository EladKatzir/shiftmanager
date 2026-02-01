# ShiftManager UI Overhaul Release Notes

**Version:** 3.0.0 (UI Overhaul Release)
**Release Date:** February 2026
**Branch:** `Ui` (merged from `release`)

---

## Overview

This major release represents a comprehensive UI overhaul of the ShiftManager application, codenamed "Shifty." The redesign introduces a modern, accessible, and consistent user interface built on a robust design token system. Key improvements include a new navigation shell, enhanced calendar views, improved accessibility (WCAG AA compliance), full RTL/Hebrew support, and offline capabilities.

### Highlights

- **44+ work items completed** across Phases 0-2 of the UI Overhaul
- **New "Warm Command" design theme** - Professional command center aesthetic with human warmth
- **Design Token System** - Centralized color, typography, and spacing definitions
- **Dark Mode Support** - Automatic detection via `prefers-color-scheme` plus manual toggle
- **Offline Support** - Form queueing and automatic retry when connection resumes
- **Enhanced Accessibility** - WCAG AA compliance with screen reader support and keyboard navigation
- **Comprehensive QA Suite** - Playwright tests covering all major user flows

---

## New Features

### Navigation System

- **Redesigned Sidebar Navigation**
  - Collapsible sidebar with 260px expanded width, 64px collapsed
  - Category-based grouping with expand/collapse state persistence (localStorage)
  - Smooth 200ms animations on state transitions
  - Mobile-responsive: hamburger menu on viewports < 768px
  - Full keyboard navigation support

- **Context Switcher Component**
  - Hierarchical company/molecule selection
  - Searchable dropdown for users with multiple companies
  - Keyboard navigation (Arrow keys, Enter, Escape)
  - Localized for English and Hebrew

- **Skip Link for Accessibility**
  - "Skip to main content" link visible on keyboard focus
  - Allows screen reader users to bypass navigation

### Scope Switcher

- **Three-Tier Scope Selection**
  - "Mine Only" - Shows only current user's shifts and assignments
  - "My Company" - Shows company-level data
  - "Full Molecule" - Shows molecule-level data (grant-dependent)
- **Integrated Above Calendar Views** - Consistent placement in Month, Week, Day, and Table views
- **Persistent Scope Selection** - Remembers choice via localStorage
- **Real-time Data Filtering** - Calendar updates immediately on scope change

### Calendar View Enhancements

- **Unified Design Language** - All calendar views (Month, Week, Day, Table) share consistent styling
- **Shift Badge Color System** - Time-of-day color metaphor with design tokens:
  - Morning: Golden Yellow (`--shift-morning`)
  - Afternoon: Olive Green (`--shift-afternoon`)
  - Noon: Bright Yellow (`--shift-noon`)
  - Night: Deep Navy (`--shift-night`)
  - Hakam: Brown (`--shift-hakam`)
  - Lead: Coral (`--shift-lead`)
- **JobType and ShiftGrouping Filters** - New filtering options on Month calendar
- **Responsive Grid Layout** - Adapts to mobile (375px), tablet (768px), and desktop viewports
- **Skeleton Loading States** - Visual feedback during data loading
- **Empty States** - Context-aware messages for no-data scenarios

### Admin Dashboard Redesign

- **Organization Structure Page** - Hierarchical tree view of Projects, Areas, Molecules, Departments
- **Grant Management Pages** - Create, edit, and assign grants to users
- **Role Assignment Pages** - Manage role templates and assign to users
- **Job Type Management** - Configure job types across the organization
- **Shift Grouping Management** - Organize shifts into logical groups
- **Setup Tasks Dashboard** - Onboarding task tracking for new deployments
- **Hierarchy Settings Page** - Configure organizational hierarchy behavior

### Offline Support (B-050)

- **Offline Detection Banner** - Prominent notification when connection is lost
- **Form Submission Queue**
  - Actions queued automatically when offline
  - Persisted to localStorage across sessions
  - Badge shows pending action count
- **Automatic Retry on Reconnection** - Queued actions submitted when online
- **Conflict Detection** - 409 handling for data edited while offline
- **Timeout Error Enhancement** - Suggests checking connection on slow requests
- **Full Localization** - English and Hebrew support

### Keyboard Navigation Improvements (B-037)

- **Global Focus Indicators** - Consistent 2px solid primary outline with offset
- **Dropdown/Combobox Navigation** - Arrow keys cycle options, Enter selects
- **Calendar Cell Navigation** - Tab/Arrow keys navigate date cells
- **Modal Focus Trapping** - Tab cycles within modal only
- **Action Menu Navigation** - Full keyboard support for context menus

### Widget System

- **OnCallWidget** - Displays current on-call contacts in sidebar
  - Collapsible with state persistence
  - Empty state handling (no contacts, no grants)
  - OfficeNumbers nested widget
- **LoadingSkeleton Component** - Reusable skeleton placeholder
- **LoadingSpinner Component** - Branded spinner with motion preference respect

---

## Design Changes

### New Design Tokens System

A comprehensive CSS custom properties (variables) system has been implemented in `tokens.css`:

```css
/* Primary Palette */
--primary: #1E3A5F;      /* Deep Navy */
--accent: #5B9BD5;       /* Sky Blue */

/* Semantic Colors */
--success: #2D6A4F;
--warning: #D4A017;
--danger: #9B2C2C;
--info: #1E3A5F;

/* Camo Accent Palette (Military-Inspired) */
--camo-sand: #C9B896;
--camo-olive: #6B7F59;
--camo-slate: #4A5568;
--camo-sky: #87CEEB;
```

### Updated Color Palette

- **Light Mode Default** - Clean `#F8F9FB` background with `#FFFFFF` surfaces
- **Dark Mode** - Deep `#0F1419` background with `#1A2332` surfaces
- **WCAG AA Verified Contrast Ratios**
  - Primary text: 14.2:1 (light), 13.8:1 (dark)
  - Muted text: 4.8:1+ on all backgrounds
  - Updated `--text-subtle` from #94A3B8 (2.8:1) to #6B7280 (5.0:1)

### Typography Improvements

- **Font Stack**: Inter (primary), Heebo/Rubik (Hebrew), JetBrains Mono (code)
- **Modular Type Scale** (based on 16px):
  - Display: 32px
  - Title: 24px
  - Heading: 20px
  - Body: 16px
  - Small: 14px
  - Tiny: 12px
- **Line Heights**: Tight (1.2), Snug (1.3), Normal (1.5), Relaxed (1.6)

### RTL Improvements

- **Dedicated `rtl.css`** - 15+ KB of RTL-specific overrides
- **Automatic Detection** - Applied when Hebrew locale selected
- **Mirrored Layout Elements**:
  - Sidebar position
  - Icon directions
  - Form button order (primary on left in RTL)
  - Calendar grid flow
- **Text Alignment** - Correct alignment for Hebrew content

### Component Library (`components.css`)

70+ KB of reusable component styles:

- **Buttons**: Primary, Secondary, Outline, Ghost, Danger variants
- **Cards**: Elevated surfaces with consistent padding and shadows
- **Badges**: Status indicators with semantic colors
- **Forms**: Inputs, selects, checkboxes, toggles with validation states
- **Tables**: Sortable headers, hover states, pagination controls
- **Modals**: Focus-trapped overlays with backdrop blur
- **Toasts**: Non-blocking notifications with auto-dismiss

---

## Accessibility Improvements

### WCAG AA Compliance Updates (A-013)

- **Color Contrast Verification** - All text/background combinations meet 4.5:1 minimum
- **Focus Visible States** - Clear focus indicators on all interactive elements
- **Form Labels** - All inputs have associated labels
- **Heading Hierarchy** - Logical heading structure on all pages
- **Alt Text** - All images have descriptive alt attributes

### Screen Reader Improvements (B-038)

- **ARIA Landmarks** - Proper use of `role`, `aria-label`, `aria-describedby`
- **Live Regions** - `aria-live="polite"` for dynamic content updates
- **Screen Reader Only Text** - `.sr-only` class for context
- **Testing Guide** - Documentation for NVDA/VoiceOver testing procedures

### Keyboard Navigation (B-037)

- **Tab Order** - Logical focus flow following visual order
- **Skip Links** - Bypass navigation for content access
- **Modal Focus Management** (B-013):
  - Focus moves to first focusable element on open
  - Tab cycles within modal only (focus trap)
  - Focus returns to trigger on close
  - Escape key closes modal

### Reduced Motion Support (B-001)

```css
@media (prefers-reduced-motion: reduce) {
  *, *::before, *::after {
    animation-duration: 0.01ms !important;
    animation-iteration-count: 1 !important;
    transition-duration: 0.01ms !important;
    scroll-behavior: auto !important;
  }
}
```

---

## Performance Improvements

### Bundle Optimization (B-029)

| Metric | Value | Target | Status |
|--------|-------|--------|--------|
| CSS (gzipped) | ~68 KB | <100 KB | PASS |
| JS (gzipped) | ~105 KB | - | ACCEPTABLE |

### CSS Architecture

- **Modular Loading** - Conditional CSS loading (RTL, print, game)
- **Design Tokens** - Single source of truth reduces redundancy
- **Component Library** - Shared styles reduce duplication

### Lazy Loading

- **Conditional Scripts** - Feature-specific JS loaded on demand
- **RTL Stylesheet** - Only loaded for Hebrew locale
- **Print Styles** - Deferred via `media="print"`

### Skeleton Loading (B-007)

- **Calendar Skeleton** - 12+ KB of loading placeholder styles
- **Perceived Performance** - Content shape shown immediately
- **Smooth Transitions** - 200ms fade-in when data arrives

---

## Bug Fixes

### Calendar Fixes

- **fix(calendar): add missing shift-noon and shift-lead tokens (A-006)** - Added design tokens for noon and lead shifts
- **fix: Add fallback UI and logging for empty shift table** - Graceful handling when no shifts exist

### UI Fixes

- **fix(ui): Fix rename modal activation with data attributes** - Modal triggers work correctly
- **fix: Prevent metric-item text cutoff with overflow visible** - Dashboard metrics display fully
- **fix: Add missing CSS styles to Admin/Index for design parity** - Consistent Admin page styling
- **fix: Add missing Hebrew translations for Admin, Calendar, Users pages** - Complete localization

### Security Fixes

- **security(csrf): add AJAX anti-forgery token handling (B-034)** - CSRF protection for AJAX calls
- **security(ui): add grant-based UI permission alignment (B-035)** - UI reflects actual permissions
- **security(privacy): add PII masking helpers and audit docs (B-036)** - Personal data protection
- **fix(security): Block path traversal in company slug** - Prevent directory traversal attacks
- **fix(security,ux): Fix XSS vulnerability and enhance blueprint deletion modal** - XSS prevention

### QA and Testing Fixes

- **fix(qa): correct owner password and fix responsive tests** - Test reliability improvements
- **fix(qa): Fix test methodology issues in Tasks 8-14** - Test accuracy improvements
- Multiple authentication and credential fixes for test automation

---

## Known Issues

### Partial Implementation

1. **Context Switcher Search** - Search filtering partially implemented; full keyboard navigation pending
2. **Sidebar State Persistence** - Category collapse state may reset on session timeout
3. **Calendar Print Styles** - Some calendar views may not print optimally on all browsers

### Browser-Specific

1. **Safari < 15**: `aspect-ratio` CSS property not supported; fallback styles applied
2. **Firefox < 103**: `backdrop-filter` limited support; modals use solid backgrounds
3. **IE11**: Not supported - no polyfills provided

### Accessibility

1. **Complex Tables**: Some data tables may require additional screen reader optimization
2. **Dynamic Content**: Some AJAX-loaded content may not announce to screen readers immediately

---

## Breaking Changes

### CSS Class Renames

The following CSS classes have been renamed for consistency:

| Old Class | New Class | Notes |
|-----------|-----------|-------|
| `.btn-primary` | `.btn--primary` | BEM naming convention |
| `.btn-secondary` | `.btn--secondary` | BEM naming convention |
| `.card-header` | `.card__header` | BEM naming convention |
| `.nav-item` | `.nav__item` | BEM naming convention |

### Removed Legacy Styles

- Inline `style=""` attributes removed from calendar pages - use CSS classes
- Hardcoded hex colors removed - use design token variables
- Legacy Bootstrap utility classes deprecated - use new component classes

### API/Behavior Changes

1. **Scope Switcher Required** - Calendar pages now require scope selection; defaults to "Mine Only"
2. **Theme Persistence** - Dark mode preference stored in localStorage; clears on explicit toggle
3. **Offline Queue** - Form submissions may be delayed when offline; check queue badge

### Configuration Changes

- **New Feature Flags** (B-023) - Feature flag infrastructure added; some features toggle-controlled
- **Seeding Options** - New `SeedingOptions` configuration for test data seeding

---

## Upgrade Instructions

### Prerequisites

- .NET 8.0 SDK or later
- Node.js 18+ (for build tools)
- Modern browser (Chrome/Firefox/Safari/Edge 90+)

### Step 1: Database Migration

Run Entity Framework migrations to update the database schema:

```bash
dotnet ef database update
```

This applies:
- `20260128234955_PendingModelChanges` - Model updates
- `20260131161714_AddClientTelemetry` - Telemetry tables

### Step 2: Clear Browser Cache

Users should clear their browser cache or perform a hard refresh (Ctrl+Shift+R) to load the new CSS and JavaScript files.

### Step 3: Review Custom Styles

If you have custom CSS:
1. Check for hardcoded colors - replace with design tokens
2. Update class names to new BEM naming convention
3. Test in both light and dark modes

### Step 4: Test Critical Flows

After upgrade, verify:
1. Login/authentication works
2. Calendar views load correctly
3. Scope switcher filters data as expected
4. Offline mode behaves correctly (disconnect and reconnect)
5. Dark mode toggle works

### Step 5: Review Feature Flags

Check `/Owner/FeatureFlags` to review and enable/disable new features as needed.

---

## Localization Updates

### New Resource Keys

Over 80+ new localization keys added to `SharedResources.resx` and `SharedResources.he-IL.resx`:

- Offline handling messages (`Offline_Banner`, `Offline_QueuedActions`, etc.)
- Scope switcher labels (`Scope_MineOnly`, `Scope_MyCompany`, `Scope_FullMolecule`)
- Calendar empty states
- Accessibility labels
- Error messages

### Hebrew (he-IL) Complete

Full Hebrew translations provided for all new UI elements.

---

## QA and Testing

### Test Suite

- **Playwright Tests** - Comprehensive E2E test suite in `qa-automation/`
- **Accessibility Tests** - axe-core integration for automated a11y checks
- **Visual Regression** - Baseline screenshots for UI comparison

### Test Coverage

- Login page tests
- Authenticated layout tests
- Calendar UI tests
- Widget system tests
- Theme system tests (light/dark)
- Localization tests (EN/HE)
- Responsive design tests (mobile/tablet/desktop)
- RTL tests

### Running Tests

```bash
cd qa-automation
npm install
npx playwright test
```

---

## Documentation

New documentation added:

| Document | Description |
|----------|-------------|
| `BROWSER-COMPATIBILITY.md` | Supported browsers and feature matrix |
| `BUNDLE-ANALYSIS.md` | CSS/JS bundle size analysis |
| `UI-OVERHAUL-IMPLEMENTATION-COMPLETE.md` | Full implementation checklist |
| `SCREEN-READER-TESTING-GUIDE.md` | Screen reader testing procedures |
| `VISUAL-REGRESSION-TEST-GUIDE.md` | Visual testing documentation |

---

## Contributors

- UI Overhaul implementation by development team
- Accessibility audit and fixes
- QA automation team
- Localization team (Hebrew translations)

---

## Technical Details

### Commit Summary

- **200+ commits** on `Ui` branch since diverging from `release`
- **44 primary work items** (A-001 through B-050)
- Key commits:
  - `7086003` - feat(ui): Complete Phases 0-2 of UI Overhaul (44 items)
  - `a4d47b4` - feat(offline): add offline/flaky network handling (B-050)
  - `0b4f7cd` - feat(a11y): add prefers-reduced-motion support (B-001)
  - `2e405f2` - feat(a11y): add modal focus management (B-013)
  - `897cd61` - feat(calendar): migrate shift badge colors to design tokens (A-006)

### Files Changed

- **40+ CSHTML pages** updated with design tokens
- **7 CSS files** in design system
- **New ViewComponents**: ContextSwitcher, ScopeSwitcher, LoadingSkeleton, LoadingSpinner, OnCallWidget
- **New JavaScript**: offline-handler.js, focus management utilities
- **Resource files**: 80+ new localization keys in both EN and HE

---

**Document Version:** 1.0
**Last Updated:** February 1, 2026
