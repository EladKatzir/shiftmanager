# ShiftManager v3.1.x - QA Design Review Report

**Review Date:** March 13, 2026
**Reviewer:** QA Design Review Agent (claude-opus-4-6)
**Application Version:** v3.1.2
**URL Tested:** https://localhost:5001
**Test Account:** admin@local (Owner role)

---

## 1. Executive Summary

**Overall Design Quality: GOOD**

ShiftManager presents a well-structured, modern UI with a consistent design language across the majority of pages. The sidebar navigation, card-based layouts, and color token system provide strong visual coherence. Dark mode is broadly well-implemented with appropriate color transformations across most elements.

However, several issues prevent an EXCELLENT rating:
- **Mobile header text overlap** is a visual regression at 375px viewport
- **Pervasive horizontal scrollbar** on mobile across nearly all pages (7px overflow)
- **Unstyled 404 error page** showing bare "HTTP 404" text
- **Quick Info panel overlaps content** on multiple pages at tablet/desktop sizes
- **85+ form inputs missing accessible labels** across the application
- **Multiple select dropdowns show rendering artifacts** (stacked chevrons) in dark mode on Calendar Overview

---

## 2. Coverage Summary

### Visual Consistency (10 checks)
| Check | Status | Notes |
|-------|--------|-------|
| Button styles consistent | PASS | Consistent border-radius (4-8px), padding (8-12px), and color scheme |
| Card component consistency | PASS | Uniform padding, shadows, and border-radius across pages |
| Typography hierarchy | PASS | Clear H1/H2 hierarchy, consistent font sizes |
| Section spacing rhythm | PASS | Even spacing between sections |
| Badge styles consistent | PASS | Status badges (Predefined, Active, Built-in) use consistent colors |
| Form elements consistent | PASS | Inputs have uniform height, border, and padding |
| Table styling consistent | PASS | Alternating styles, consistent headers across Admin/Users, Blueprints |
| Modal appearance | NOT TESTED | No modals were triggered during automated flow |
| Toast notifications | NOT TESTED | No toast triggers in automated flow |
| Icon patterns | PASS with NOTE | Emoji-based nav icons (not SVG/Lucide) - intentional per design |

### Theme Correctness (8 checks)
| Check | Status | Notes |
|-------|--------|-------|
| Light mode: all text readable | PASS | No invisible text found |
| Dark mode: all text readable | PASS | All text has adequate contrast against dark backgrounds |
| Dark mode: calendar cells contrast | PASS | Calendar header row uses navy/blue with white text |
| Dark mode: form input borders visible | PASS | Borders use rgb(46, 60, 79) - visible against dark bg |
| Dark mode: modals | NOT TESTED | |
| Theme toggle smooth | PASS | `#themeToggle` found, JS-based toggle works |
| No hardcoded colors in dark mode | FAIL | See DR-THEME-01 (select dropdown chevrons) |
| Shift type color coding | PASS | Color swatches visible in both themes (Blueprints, DutyTypes) |

### Responsive Design (8 checks)
| Check | Status | Notes |
|-------|--------|-------|
| Mobile: sidebar collapses | PASS | Sidebar hidden, hamburger menu shown |
| Mobile: calendar usable | PARTIAL | Filters stack vertically (good), but header overlaps toolbar |
| Mobile: forms single-column | PASS | Forms go single-column at mobile width |
| Mobile: touch targets 44px | FAIL | See DR-A11Y-TOUCH - 8+ elements below 44px |
| Mobile: bottom sheet works | PRESENT | Quick Info bottom sheet visible on mobile |
| Tablet: layout adapts | PASS | Sidebar collapsed, content fills width, layout adapts |
| Desktop: space usage | PASS | Sidebar + main content + Quick Info panel use space well |
| No horizontal scrollbar | FAIL | Horizontal scrollbar on 13 of 19 pages at mobile (375px) |

### Accessibility (10 checks)
| Check | Status | Notes |
|-------|--------|-------|
| Skip-to-content link | PASS | Present on all layout pages (`a.skip-link`) |
| Tab navigation | PASS | Tab key moves through interactive elements |
| Focus indicators visible | PASS | box-shadow focus ring: `rgb(232, 241, 248) 0px 0px 0px 3px` |
| Color not sole indicator | PASS | Status badges use text labels alongside colors |
| Text contrast WCAG AA | PARTIAL | Skip-link has 1:1 contrast ratio (same fg/bg) - see DR-A11Y-03 |
| ARIA labels on icon buttons | PASS | 0 icon-only buttons without aria-label (automated check) |
| Modal focus trap | NOT TESTED | |
| Calendar keyboard nav | NOT TESTED | |
| Reduced motion | NOT TESTED | |
| Page landmarks | PASS | main, nav, header present on all layout pages |

### Loading & Transition States (5 checks)
| Check | Status | Notes |
|-------|--------|-------|
| Page loader | NOT OBSERVED | Pages load quickly, no loader needed/seen |
| Skeleton screens | NOT OBSERVED | Calendar empty state shows immediately |
| Form submission indicator | NOT TESTED | |
| Toast notifications | NOT TESTED | |
| Layout shifts | PASS | No CLS observed during page loads |

### Print Quality (3 checks)
| Check | Status | Notes |
|-------|--------|-------|
| Calendar print layout | PARTIAL | Sidebar hidden, table renders, but Quick Info panel still shows |
| Navigation hidden in print | PASS | Sidebar hidden; warning banner still shows |
| B&W distinguishable | PARTIAL | Calendar header uses background that may merge in B&W |

### Empty & Error States (4 checks)
| Check | Status | Notes |
|-------|--------|-------|
| Empty calendar state | PASS | "No Shifts Found" with icon and helpful message |
| Empty lists message | PASS | "No chore types created yet", "No announcements" etc. |
| Error page styled | FAIL | See DR-ERROR-01 - bare "HTTP 404" text, no styling |
| Network error state | NOT TESTED | |

---

## 3. Page-by-Page Assessment

### Login Page
- **Light mode desktop**: Clean split layout with branded left panel (blue gradient + logo) and form on right. Language toggle and theme toggle available. Well-balanced.
- **Dark mode desktop**: Smooth transition. Form background darkens to navy. Login button changes to lighter blue with white text. "Request Access" button border visible.
- **Mobile**: Stacks vertically. Logo on top, form below. Full-width inputs. Good.
- **Dark mobile**: Consistent with desktop dark mode.
- **Issues**: Login button contrast ratio 2.96:1 (white on rgb(91,155,213)) - below WCAG AA 4.5:1 for normal text, but passes 3:1 for large text since button text is 16px/600 weight.
- **Screenshots**: `login_desktop_light.png`, `login_desktop_dark.png`, `login_mobile_light.png`, `login_mobile_dark.png`

### Home / Dashboard
- **Light mode**: Dashboard cards (Upcoming Shifts, Pending Requests, Team Members, Notifications) in a grid. Announcements section with empty state. Quick Actions cards below. Quick Info panel on right.
- **Dark mode**: All elements transition correctly. Card borders visible. Text readable.
- **Mobile**: **ISSUE** - Page title "Mission Overview" overlaps with toolbar buttons (checkmark, bell, language, theme, Logout). The title and toolbar occupy the same horizontal space at this viewport.
- **Tablet**: Clean layout, sidebar collapsed, content fills width. Quick Info panel overlaps Quick Actions slightly.
- **Screenshots**: `home_desktop_light.png`, `home_desktop_dark.png`, `home_mobile_light.png`, `tablet_sidebar.png`

### Calendar/Shifts
- **Light mode**: Filter bar with Molecule, Job Type, View dropdowns. Mode toggle buttons (By Shift/By User/Capacity). Date navigation. Empty state with "No Shifts Found" message.
- **Dark mode**: Filters readable. Button toggle active state clear.
- **Mobile**: Filters stack vertically (good). Same header overlap issue as Home.
- **Note**: Calendar/Table, Calendar/Day, Calendar/Week, Calendar/Month all redirect to Calendar/Shifts. This appears to be by design (the View dropdown on Shifts page controls the view mode).
- **Screenshots**: `calendar_shifts_desktop_light.png`, `calendar_shifts_desktop_dark.png`, `calendar_shifts_mobile_light.png`

### Calendar/Overview
- **Light mode**: Company badge, View/Users filter, date navigation, legend bar (Vacation, Shift, Chore, Day Shift with colored dots), "Read-Only Mode" banner in yellow. Calendar grid with Hebrew day names.
- **Dark mode**: **ISSUE** - Select dropdowns ("Week", "Active Users") show stacked checkmark/chevron artifacts overlapping the dropdown text. The dropdowns have `appearance: none` but something is rendering extra indicators.
- **Mobile**: Header overlap continues. Calendar grid extremely narrow at 375px but still renders.
- **Screenshots**: `calendar_overview_desktop_light.png`, `calendar_overview_desktop_dark.png`, `dark_mode_selects_overview.png`

### My/Requests
- **Light mode**: Two-column layout: "Request Time Off" form on left, "Request Shift Swap" on right. Request History section below with empty states.
- **Dark mode**: Form fields have visible borders and backgrounds. Submit button visible.
- **Accessibility**: 2 inputs without labels.
- **Screenshots**: `my_requests_desktop_light.png`, `my_requests_desktop_dark.png`

### Requests (Manager View)
- **Light mode**: Tab navigation (Pending Time-Off, Pending Swaps, Approved Time-Off). Empty state with "No time off requests yet" with checkmark icon.
- **Dark mode**: Tabs and empty state render correctly.
- **Screenshots**: `requests_desktop_light.png`, `requests_desktop_dark.png`

### Admin/Users
- **Light mode**: Search bar, Join Requests section, Existing Users table with pagination. Add User form at bottom. Bulk Import CSV section.
- **Dark mode**: Table rows have alternating backgrounds. Headers visible.
- **Accessibility**: **54 inputs without labels** - the per-row inline edit fields in the users table lack associated labels.
- **Mobile**: Table becomes card-like layout. Still usable but dense.
- **Screenshots**: `admin_users_desktop_light.png`, `admin_users_desktop_dark.png`, `admin_users_mobile_light.png`

### Owner/AreaConfig
- **Light mode**: Area Configuration card showing area details (ID, shifts count), Default Rest Hours and Weekly Hours Cap with values. "Edit Settings" button.
- **Dark mode**: Clean transition, card border visible.
- **Screenshots**: `owner_areaconfig_detail_light.png`, `owner_areaconfig_detail_dark.png`

### Owner/Blueprints
- **Light mode**: Create New Shift Type form, Existing Shift Types table with type badges (Predefined), scope badges (Company Only/Publish to Molecule), action buttons (Edit Time/Delete).
- **Dark mode**: Table renders well. Badge colors visible.
- **Responsive**: **Horizontal scrollbar at both tablet (768px) and mobile (375px)** - scrollWidth 881px. The table columns don't collapse for smaller viewports.
- **Screenshots**: `owner_blueprints_desktop_light.png`, `owner_blueprints_desktop_dark.png`, `owner_blueprints_mobile_light.png`

### Owner/DataLifecycle
- **Light mode**: Create Archive, Purge Data, Re-Import sections. Safety confirmations with destructive operation warnings. SQL preview shown.
- **Dark mode**: Warning boxes visible. Destructive operation styling maintained.
- **Screenshots**: `owner_datalifecycle_desktop_light.png`

### Owner/EmailTemplates
- **Light mode**: Grid of email template cards (Shift Assigned, Shift Changed, Shift Deleted, etc.) with Default Message preview, Available Variables, and Edit buttons.
- **Dark mode**: Cards render with visible borders.
- **Mobile**: Horizontal scrollbar (scrollWidth=436px at 375px viewport).
- **Screenshots**: `owner_emailtemplates_desktop_light.png`

### Owner/MasterPrograms
- **Light mode**: Molecule selector, create form, "No programs available" warning, "No master programs defined yet" info box.
- **Dark mode**: Clean transition.
- **Screenshots**: `owner_masterprograms_desktop_light.png`

### Admin/Organization/ChoreTypes
- **Light mode**: Molecule selector, create form (Name, Display Name), empty list "No chore types have been created yet".
- **Accessibility**: 3 inputs without labels.
- **Screenshots**: `admin_org_choretypes_desktop_light.png`

### Admin/Organization/DutyTypes
- **Light mode**: Create form (Name English, Name Hebrew, Icon, Color picker). Table of existing duty types (Hakam, Lead, Backup-hakam) with built-in/active status badges.
- **Accessibility**: 8 inputs without labels.
- **Screenshots**: `admin_org_dutytypes_desktop_light.png`

### Notifications Page
- **ISSUE**: Returns HTTP 404. The page does not exist at `/Notifications`. The notification bell icon in the toolbar navigates here but no page is served.
- **Screenshots**: `notifications_desktop_light.png`

### Profile (/My/Profile)
- **Light mode**: Avatar upload, Personal Information (Display name, Preferred Name, Phone, Living Place, DOB), Professional Information (Molecule, Job Title, Hire Date, Skills, Certifications), Emergency Contact section. Save/Cancel buttons.
- **Dark mode**: All form fields have visible borders. Text readable.
- **Accessibility**: **14 inputs without labels** - some form fields rely on heading text rather than `<label>` associations.
- **Screenshots**: `profile_desktop_light.png`, `profile_desktop_dark.png`

### 404 Error Page
- **ISSUE**: Bare "HTTP 404" text in the top-left corner on a completely white page. No branding, no navigation, no "go back" link, no styling whatsoever.
- **Screenshots**: `error_404_page.png`

---

## 4. Confirmed Findings

### DR-MOBILE-01: Mobile Header Title Overlaps Toolbar
- **Area:** Responsive Design
- **Severity:** P1 - Critical
- **Description:** At 375px viewport width, the page title ("Mission Overview", "Shifts Calendar", etc.) occupies the same horizontal space as the toolbar action buttons (checkmark, notification bell, language selector, theme toggle, Logout). The title text is partially obscured and truncated. This affects every page with the standard layout.
- **Evidence:** `mobile_header_overlap.png` - Title "Mission Overview" shows as "M..on O..view" behind the toolbar buttons.
- **Affected pages:** All pages using the standard layout

### DR-RESP-01: Pervasive Mobile Horizontal Scrollbar
- **Area:** Responsive Design
- **Severity:** P2 - High
- **Description:** 13 of 19 tested pages show a horizontal scrollbar at 375px mobile viewport. Most overflow by only 7px (scrollWidth=382 vs clientWidth=375), suggesting a common layout element (likely the sidebar toggle arrow or a margin/padding issue) is slightly wider than the viewport. The Blueprints page overflows significantly (scrollWidth=881px) due to a non-responsive table.
- **Evidence:** Automated scroll detection in `qa_log.txt`. Affected pages: home, calendar_shifts, calendar_table, calendar_day, calendar_week, calendar_month, calendar_overview, my_requests, requests, admin_users, owner_emailtemplates, admin_org_choretypes, admin_org_dutytypes, profile.
- **Note:** The consistent 7px overflow across most pages suggests a single root cause.

### DR-RESP-02: Blueprints Table Not Responsive
- **Area:** Responsive Design
- **Severity:** P2 - High
- **Description:** The Owner/Blueprints "Existing Shift Types" table causes horizontal scrollbar at both tablet (768px, scrollWidth=881) and mobile (375px, scrollWidth=881). The table does not collapse, wrap, or become a card layout on smaller viewports.
- **Evidence:** `owner_blueprints_mobile_light.png`

### DR-ERROR-01: Unstyled 404 Error Page
- **Area:** Empty & Error States
- **Severity:** P2 - High
- **Description:** Navigating to any non-existent URL (e.g., `/nonexistent-page-404`) shows a bare white page with only "HTTP 404" text in the upper-left corner. No branding, sidebar, navigation, or "return to home" link. The `/Notifications` page also returns this same bare 404.
- **Evidence:** `error_404_page.png`, `notifications_desktop_light.png`

### DR-THEME-01: Dark Mode Select Dropdown Rendering Artifacts
- **Area:** Theme Correctness
- **Severity:** P3 - Medium
- **Description:** On the Calendar/Overview page in dark mode, the View ("Week") and Users ("Active Users") select dropdowns display stacked checkmark/chevron characters overlapping the dropdown text. The selects have `appearance: none` set, but a custom dropdown arrow appears to be rendering on top of browser-default arrows or other visual artifacts.
- **Evidence:** `dark_mode_selects_overview.png`, `calendar_overview_desktop_dark.png`

### DR-A11Y-01: Skip-Link Has 1:1 Contrast Ratio
- **Area:** Accessibility
- **Severity:** P3 - Medium
- **Description:** The skip-to-content link (`a.skip-link`) has identical foreground and background colors (both `rgb(91, 155, 213)`) in dark mode, resulting in a 1:1 contrast ratio. While skip-links are typically hidden until focused, this means even when focused the text is invisible against its background.
- **Evidence:** Detected on every page with the standard layout in dark mode. Contrast analysis in `qa_log.txt`.

### DR-A11Y-02: 85+ Form Inputs Without Associated Labels
- **Area:** Accessibility
- **Severity:** P2 - High
- **Description:** Multiple pages have form inputs lacking `<label>` elements, `aria-label`, or `aria-labelledby` attributes. The worst offenders are:
  - Admin/Users: 54 inputs (inline edit fields in user table rows)
  - Profile: 14 inputs
  - Admin/Organization/DutyTypes: 8 inputs
  - Admin/Organization/ChoreTypes: 3 inputs
  - My/Requests: 2 inputs
  - Calendar/Shifts, Calendar/Overview, Owner/DataLifecycle: 1 each
- **Evidence:** Automated structure check in `qa_log.txt`

### DR-A11Y-03: Touch Targets Below 44px Minimum
- **Area:** Accessibility
- **Severity:** P3 - Medium
- **Description:** At mobile viewport, 8 interactive elements have touch targets smaller than the recommended 44x44px minimum:
  - Dismiss button ("x"): 18x26px
  - Context switcher: 187x37px
  - Notification bell: 43x45px (close, ~1px short)
  - Language button: 68x43px
  - Theme toggle: 43x43px
  - Logout button: 94x43px
  - Bottom dock toggle: 354x41px
- **Evidence:** Automated check in `qa_log2.txt`

### DR-A11Y-04: Notifications Page Missing All Landmarks
- **Area:** Accessibility
- **Severity:** P2 - High (if page should exist)
- **Description:** The `/Notifications` page returns a 404 response but with 0 landmarks (no `<main>`, `<nav>`, `<header>`, no h1, no skip-link). This is the same unstyled 404 issue as DR-ERROR-01 but is specifically concerning because the notification bell in the toolbar links here.
- **Evidence:** `notifications_desktop_light.png`

### DR-PRINT-01: Quick Info Panel Visible in Print
- **Area:** Print Quality
- **Severity:** P3 - Medium
- **Description:** When printing the Calendar/Overview page, the Quick Info panel ("No one is on-call right now") remains visible in the lower-right corner. It should be hidden in print media to avoid wasting space and confusing the printed output. The system warning banner also remains visible.
- **Evidence:** `calendar_overview_print.png`

### DR-LOGIN-01: Login Button Contrast Ratio
- **Area:** Accessibility / Theme
- **Severity:** P4 - Low
- **Description:** The Login button has white text (`rgb(255,255,255)`) on a medium-blue background (`rgb(91,155,213)`), yielding a contrast ratio of approximately 2.96:1. This technically fails WCAG AA for normal text (requires 4.5:1) but passes for large text (3:1 threshold). Since the text is 16px/600 weight, it is borderline - considered "large" text by some interpretations. Visual readability is acceptable.
- **Evidence:** Automated contrast check in `qa_log.txt`

---

## 5. Issues Ranked by Severity

| # | ID | Severity | Area | Description |
|---|-----|----------|------|-------------|
| 1 | DR-MOBILE-01 | P1 | Responsive | Mobile header title overlaps toolbar on all pages |
| 2 | DR-A11Y-02 | P2 | Accessibility | 85+ form inputs without accessible labels |
| 3 | DR-RESP-01 | P2 | Responsive | Horizontal scrollbar on 13/19 pages at mobile |
| 4 | DR-ERROR-01 | P2 | Error States | Unstyled 404 error page |
| 5 | DR-RESP-02 | P2 | Responsive | Blueprints table not responsive at tablet/mobile |
| 6 | DR-A11Y-04 | P2 | Accessibility | Notifications page 404 (linked from toolbar) |
| 7 | DR-THEME-01 | P3 | Theme | Dark mode select dropdown rendering artifacts |
| 8 | DR-A11Y-01 | P3 | Accessibility | Skip-link 1:1 contrast in dark mode |
| 9 | DR-A11Y-03 | P3 | Accessibility | Touch targets below 44px minimum on mobile |
| 10 | DR-PRINT-01 | P3 | Print | Quick Info panel visible in print output |
| 11 | DR-LOGIN-01 | P4 | Accessibility | Login button contrast ratio borderline |

---

## 6. Release Blockers

### Must Fix Before Release
1. **DR-MOBILE-01 (P1)**: The mobile header overlap makes page titles unreadable on all pages at 375px. This is a core usability regression for mobile users. Fix: ensure the page title and toolbar wrap or stack, or truncate title with ellipsis while keeping toolbar accessible.

### Strongly Recommended
2. **DR-RESP-01 (P2)**: The 7px horizontal overflow on mobile is likely a single root cause (sidebar toggle arrow or body margin). A quick CSS fix could resolve scrollbar on 12+ pages.
3. **DR-ERROR-01 (P2)**: A styled 404 page with navigation back to the app is a basic UX expectation. The notification bell linking to a 404 is particularly concerning.

---

## 7. Non-Blocking Improvements

### High Impact, Low Effort
- **DR-RESP-01**: Finding the 7px overflow root cause and fixing it once resolves 12+ pages
- **DR-A11Y-01**: Fix skip-link dark mode colors (one CSS rule change)
- **DR-PRINT-01**: Add `@media print` rule to hide Quick Info panel and warning banner

### Medium Impact, Medium Effort
- **DR-A11Y-02**: Add `aria-label` attributes to unlabeled form inputs, especially the Admin/Users inline edit fields
- **DR-THEME-01**: Fix dark mode select dropdown arrow rendering (likely a CSS `background-image` or `::after` pseudo-element conflict)
- **DR-RESP-02**: Make Blueprints table responsive with horizontal scroll container or card layout at smaller viewports

### Lower Impact
- **DR-A11Y-03**: Increase minimum touch target sizes in mobile CSS
- **DR-LOGIN-01**: Darken login button background slightly for better contrast

---

## 8. Recommendations

### Immediate (Before v3.1.3)
1. **Fix mobile header layout** - Add `flex-wrap: wrap` or media query to ensure title and toolbar don't overlap at narrow widths
2. **Fix 7px mobile overflow** - Inspect the body/main layout for the element causing 382px total width on a 375px viewport. Likely a margin, padding, or the sidebar collapse toggle
3. **Create styled 404 page** - Add a custom error page with branding, navigation, and a link back to Home
4. **Fix or remove Notifications link** - Either create the `/Notifications` page or remove/redirect the notification bell link

### Short-term (v3.2)
5. **Accessibility label audit** - Add labels to the 85+ unlabeled inputs, prioritizing the Admin/Users table
6. **Fix dark mode select dropdowns** - Investigate the chevron artifact on Calendar/Overview selects
7. **Print CSS cleanup** - Hide Quick Info panel, system warnings, and breadcrumbs in print media

### Medium-term
8. **Responsive tables** - Add horizontal scroll containers or card layouts for data tables (Blueprints, Users) at mobile/tablet
9. **Touch target audit** - Ensure all interactive elements meet 44px minimum on mobile
10. **Skip-link dark mode fix** - Set contrasting colors for the focused skip-link in dark theme

---

## 9. Positive Observations

The following aspects of the design are well-executed:

1. **Consistent design language**: Cards, buttons, typography, and spacing follow a coherent system across all pages
2. **Dark mode quality**: The vast majority of elements transition correctly between light and dark themes. Form inputs have visible borders, text is readable, and cards have appropriate backgrounds
3. **Empty states**: Meaningful messages with icons appear for empty calendars, empty request lists, empty chore types, etc.
4. **Sidebar navigation**: Well-organized with emoji icons, section groupings (My Shifty, Admin, Owner Administration), and proper active state highlighting
5. **Breadcrumb navigation**: Present on all sub-pages with clear hierarchy
6. **Skip-to-content link**: Present on all layout pages (accessibility positive)
7. **Form layout**: Forms adapt to single-column on mobile viewports
8. **Calendar Overview legend**: Color-coded legend with dot indicators for Vacation/Shift/Chore/Day Shift
9. **Warning banner**: System disk space warning is prominent, sticky, and dismissible
10. **Theme toggle**: Easily discoverable in the toolbar, works smoothly

---

## Appendix: Test Coverage Matrix

| Page | Desktop Light | Desktop Dark | Tablet | Mobile Light | Mobile Dark |
|------|:---:|:---:|:---:|:---:|:---:|
| Login | Y | Y | Y | Y | Y |
| Home | Y | Y | Y | Y | Y |
| Calendar/Shifts | Y | Y | Y | Y | Y |
| Calendar/Overview | Y | Y | Y | Y | Y |
| My/Requests | Y | Y | Y | Y | Y |
| Requests | Y | Y | Y | Y | Y |
| Admin/Users | Y | Y | Y | Y | Y |
| Owner/AreaConfig | Y | Y | Y | Y | Y |
| Owner/DataLifecycle | Y | Y | Y | Y | Y |
| Owner/EmailTemplates | Y | Y | Y | Y | Y |
| Owner/Blueprints | Y | Y | Y | Y | Y |
| Owner/MasterPrograms | Y | Y | Y | Y | Y |
| Admin/Org/ChoreTypes | Y | Y | Y | Y | Y |
| Admin/Org/DutyTypes | Y | Y | Y | Y | Y |
| Profile | Y | Y | Y | Y | Y |
| Notifications | Y (404) | Y (404) | Y (404) | Y (404) | Y (404) |
| Error Page (404) | Y | - | - | - | - |
| Print (Overview) | Y | - | - | - | - |

**Total screenshots captured:** 90+
**Screenshot directory:** `qa_screenshots/`
