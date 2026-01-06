# ShiftManager UI Redesign - Testing Checklist

**Purpose**: Comprehensive testing checklist to verify all functionality, localization, RTL support, dark mode, and role-based access control remain intact throughout the redesign.

**Testing Principle**: Test each phase individually before moving to the next.

---

## Phase-by-Phase Testing

### Phase 0: Setup & Foundation
- [ ] Progress tracking files created and accessible
- [ ] Localization keys documented
- [ ] No build errors introduced

---

### Phase 1: Design System & CSS Foundation

#### Visual Testing
- [ ] New CSS variables load correctly in light mode
- [ ] New CSS variables load correctly in dark mode
- [ ] Typography scale renders correctly (all sizes)
- [ ] Card system renders with proper shadows and borders
- [ ] Card variants display correctly (success, warning, danger, metric)

#### Dark Mode
- [ ] Light theme colors match design spec
- [ ] Dark theme colors match design spec
- [ ] No color contrast issues in light mode
- [ ] No color contrast issues in dark mode
- [ ] Smooth transitions when toggling theme

#### RTL Support
- [ ] All new CSS works in RTL (dir="rtl")
- [ ] Card layouts mirror correctly in Hebrew
- [ ] Typography alignment correct in Hebrew
- [ ] No layout breaks in rtl.css

#### Localization
- [ ] No hardcoded strings in CSS comments
- [ ] All new CSS classes work with localized content

---

### Phase 2: Core Layout & Navigation

#### Layout Structure
- [ ] Sidebar renders correctly
- [ ] Header renders correctly
- [ ] Main content area positioned properly
- [ ] App shell structure is responsive

#### Sidebar Navigation
- [ ] Employee sees correct menu items (Home, Schedule, My Requests, My Team, Notifications)
- [ ] Trainee sees correct menu items (same as Employee)
- [ ] Manager sees correct menu items (Home, Schedule, Requests, Analytics, People, Settings)
- [ ] Director sees correct menu items (includes Director tools)
- [ ] Owner sees all menu items (includes Companies, Diagnostics)
- [ ] Icons display correctly (no emojis in nav)
- [ ] Active page highlighted correctly

#### Header
- [ ] Page title displays correctly
- [ ] Breadcrumb area positioned correctly
- [ ] Right-side actions (notifications, language, theme, user menu) work
- [ ] Theme toggle button works
- [ ] Language toggle button works
- [ ] User name and role display correctly
- [ ] Logout button works

#### Dark Mode Enhancement
- [ ] Theme persists across page reloads
- [ ] System preference detected on first load (if no saved theme)
- [ ] Smooth transitions active (0.15s)
- [ ] All layout elements support dark mode

#### RTL Support
- [ ] Sidebar mirrors correctly in Hebrew
- [ ] Header elements reverse in Hebrew
- [ ] Navigation items align right in Hebrew
- [ ] Breadcrumb works in RTL (existing component, verify placement)

#### Localization
- [ ] All sidebar labels use @Localizer
- [ ] All header text uses @Localizer
- [ ] Page titles use @Localizer or ViewData
- [ ] No hardcoded English strings visible
- [ ] Test complete layout in Hebrew (he-IL culture)

#### Functional Preservation
- [ ] Authentication still works (login/logout)
- [ ] Role-based page access still enforced
- [ ] Existing routes still accessible
- [ ] No JavaScript errors in console

---

### Phase 3: Employee Journey - Home Page

#### Home Page Functionality
- [ ] Route /Home/Index accessible
- [ ] Login redirects to /Home/Index
- [ ] Page loads for all roles (Employee, Trainee, Manager, Director, Owner)

#### Card Display (Employee/Trainee)
- [ ] Today/Next Shift card shows correct data
- [ ] Notifications card shows correct unread count
- [ ] My Shifts Summary card shows hours/days
- [ ] My Requests card shows correct counts
- [ ] All CTAs link correctly

#### Card Display (Manager)
- [ ] Staffing Overview card shows unassigned shifts
- [ ] Approvals card shows pending counts
- [ ] CTAs link to correct pages

#### Card Display (Director/Owner)
- [ ] Companies Overview card displays
- [ ] Company metrics shown correctly

#### Dark Mode
- [ ] All cards render correctly in dark mode
- [ ] Card shadows visible but not harsh

#### RTL Support
- [ ] Card grid layouts correctly in Hebrew
- [ ] Card content aligns right in Hebrew
- [ ] CTAs work in Hebrew

#### Localization
- [ ] All card titles use @Localizer
- [ ] All labels use @Localizer (Hours, Days, Pending, etc.)
- [ ] All CTAs use @Localizer
- [ ] Dates formatted according to culture
- [ ] Numbers formatted according to culture
- [ ] Test complete page in Hebrew

#### Functional Preservation
- [ ] Clicking CTAs navigates correctly
- [ ] Data fetching works (no backend errors)
- [ ] Page loads fast (no performance regression)

---

### Phase 4: Employee Journey - Schedule Workspace

#### Schedule Page Functionality
- [ ] Route /Schedule accessible to all authenticated users
- [ ] Query params work (calendar, view, mode)
- [ ] Old routes (/Calendar/Month, etc.) still work

#### Calendar Rail
- [ ] Calendar selector displays
- [ ] Company Shifts option visible to all
- [ ] My Shifts option visible
- [ ] Chores option visible (if permission)
- [ ] On Duty option visible (if permission)
- [ ] Clicking changes calendar context

#### View Controls
- [ ] Calendar/Table toggle works
- [ ] Month/Week/Day toggle works (when view=calendar)
- [ ] Date navigation (Previous/Next) works
- [ ] Today button works
- [ ] Keyboard shortcuts work (arrow keys, 't' for today)

#### Calendar Display
- [ ] Month view renders shifts correctly
- [ ] Week view renders shifts correctly
- [ ] Day view renders shifts correctly
- [ ] Table view renders for assignments
- [ ] Shift data accurate (times, names, counts)
- [ ] Staffing requirements displayed
- [ ] Trainee counts shown

#### Assignments
- [ ] Clicking shift opens assignment management
- [ ] Assignment panel/modal displays correctly
- [ ] Can assign employees to shifts
- [ ] Conflict detection works
- [ ] Busy status indicators work
- [ ] Concurrency detection works

#### Header Actions
- [ ] Employee: "Request Time Off" button visible and works
- [ ] Manager: "Assign Shifts" button visible and works

#### Dark Mode
- [ ] Calendar rail renders in dark mode
- [ ] View controls render in dark mode
- [ ] Calendar grid readable in dark mode
- [ ] Shift cells have proper contrast

#### RTL Support
- [ ] Calendar rail mirrors in Hebrew
- [ ] Calendar grid mirrors in Hebrew (days reverse)
- [ ] View controls work in Hebrew
- [ ] Date navigation correct in Hebrew

#### Localization
- [ ] "Calendars" uses @Localizer
- [ ] Calendar names use @Localizer
- [ ] View toggle labels use @Localizer
- [ ] Mode toggle labels use @Localizer
- [ ] Date navigation buttons use @Localizer
- [ ] Shift types localized
- [ ] Employee names display correctly
- [ ] Test complete Schedule workspace in Hebrew

#### Functional Preservation
- [ ] All existing calendar features work
- [ ] Filtering by shift type works
- [ ] Creating shift instances works (Manager+)
- [ ] Deleting shifts works (Manager+)
- [ ] Adjusting staffing counts works (Manager+)
- [ ] No regression in assignment logic

---

### Phase 5-7: Employee Journey - Requests, Team, Profile

_(Similar structure - add detailed checklists as phases begin)_

#### Phase 5: My Requests
- [ ] Page renders with new card layout
- [ ] Request list displays correctly
- [ ] Request details visible
- [ ] Status labels localized
- [ ] Works in Hebrew
- [ ] Works in dark mode

#### Phase 6: My Team
- [ ] Directory layout displays
- [ ] Team member cards/rows show correctly
- [ ] Details panel works
- [ ] Localized labels
- [ ] Works in Hebrew
- [ ] Works in dark mode

#### Phase 7: Profile & Settings
- [ ] /My/Profile updated to card layout
- [ ] All form fields work
- [ ] /My/NotificationCenter updated
- [ ] /My/ApiKeys updated
- [ ] All pages localized
- [ ] Works in Hebrew
- [ ] Works in dark mode

---

### Phase 8-11: Manager Journey

#### Phase 8: Requests Inbox
- [ ] Three-column layout renders
- [ ] Filters work
- [ ] Request list loads
- [ ] Details panel displays
- [ ] Approve/Decline actions work
- [ ] Conflict removal still automatic
- [ ] Notifications sent correctly
- [ ] Localized
- [ ] Works in Hebrew
- [ ] Works in dark mode

#### Phase 9: People & Users
- [ ] /Admin/Users modernized
- [ ] Join Requests section works
- [ ] User directory displays
- [ ] Create/edit user works
- [ ] Activate/deactivate works
- [ ] Filters work
- [ ] Localized
- [ ] Works in Hebrew
- [ ] Works in dark mode

#### Phase 10: Analytics
- [ ] Metric cards display with correct data
- [ ] Charts render correctly
- [ ] Tables display correctly
- [ ] All metrics accurate
- [ ] Localized (labels, units)
- [ ] Works in Hebrew
- [ ] Works in dark mode

#### Phase 11: Configuration
- [ ] /Admin/Config uses card layout
- [ ] /Admin/ShiftTypes updated
- [ ] /Admin/TimeOff updated
- [ ] All settings functional
- [ ] Validation works
- [ ] Localized
- [ ] Works in Hebrew
- [ ] Works in dark mode

---

### Phase 12: Director/Owner Journey

#### Director Pages
- [ ] /Director/NotificationHub updated
- [ ] /Director/CompanyFilter updated
- [ ] /Director/ViewAsMode works
- [ ] Multi-company context switching works
- [ ] Localized
- [ ] Works in Hebrew
- [ ] Works in dark mode

#### Owner Pages
- [ ] /Admin/Companies updated
- [ ] /Admin/Directors updated
- [ ] /Diagnostic updated
- [ ] All owner-only features accessible
- [ ] Localized
- [ ] Works in Hebrew
- [ ] Works in dark mode

---

### Phase 13: Command Palette

#### Functionality
- [ ] Ctrl/Cmd+K opens palette
- [ ] Modal displays centered
- [ ] Static page list shows
- [ ] Recent history shows (from localStorage)
- [ ] Clicking item navigates correctly
- [ ] Escape closes palette

#### Visual
- [ ] Modal styled correctly
- [ ] Works in dark mode
- [ ] Readable contrast

#### RTL Support
- [ ] Modal works in Hebrew
- [ ] Input field mirrors correctly
- [ ] Results list aligns correctly

#### Localization
- [ ] Placeholder text uses @Localizer
- [ ] Page labels use @Localizer
- [ ] Test in Hebrew

---

### Phase 14: Public Pages & Remaining Updates

#### Public Pages
- [ ] /Public/Chores updated
- [ ] /Public/OnDuty updated
- [ ] /Public/Feedback updated
- [ ] All localized
- [ ] Work in Hebrew
- [ ] Work in dark mode

#### Auth Pages
- [ ] /Auth/Login styled with modern card
- [ ] /Auth/Signup styled with modern card
- [ ] /Auth/ForgotPassword styled with modern card
- [ ] All auth flows still work (lockouts, validation)
- [ ] Localized
- [ ] Work in Hebrew
- [ ] Work in dark mode

---

### Phase 15: Breadcrumb Updates

#### Placement
- [ ] Breadcrumbs placed in app-header-left on all pages
- [ ] Component code UNCHANGED (verify)
- [ ] Breadcrumbs added to new pages (Home, Schedule)
- [ ] Breadcrumbs visible and correct on all existing pages

#### RTL Support
- [ ] Breadcrumbs mirror correctly in Hebrew (existing feature)
- [ ] Placement looks correct in new header

#### Localization
- [ ] Breadcrumb labels use @Localizer on all pages
- [ ] Test in Hebrew

---

## Comprehensive Testing (Phase 16)

### Functional Testing - All User Roles

#### Employee Flow
- [ ] Login as Employee
- [ ] Navigate to Home
- [ ] View Schedule (My Shifts)
- [ ] Create Time Off Request
- [ ] Create Swap Request (if not Trainee)
- [ ] View My Requests
- [ ] View My Team
- [ ] Edit Profile
- [ ] View Notifications
- [ ] Request API Key
- [ ] Logout

#### Trainee Flow
- [ ] Login as Trainee
- [ ] All Employee features work
- [ ] Cannot create Swap Requests (verify blocked)
- [ ] Can be assigned to shadow shifts

#### Manager Flow
- [ ] Login as Manager
- [ ] Navigate to Home (Manager cards visible)
- [ ] View Schedule (Company Shifts)
- [ ] Assign employees to shifts
- [ ] Handle conflicts correctly
- [ ] Approve Time Off Request (conflicts removed automatically)
- [ ] Decline Time Off Request
- [ ] Approve Swap Request
- [ ] Decline Swap Request
- [ ] View Analytics
- [ ] Manage Users (create, edit, approve join requests)
- [ ] Configure Shift Types
- [ ] Configure Company Settings
- [ ] View Audit Log
- [ ] Manage API Keys
- [ ] Logout

#### Director Flow
- [ ] Login as Director
- [ ] View NotificationHub (multi-company)
- [ ] Switch company context (CompanyFilter)
- [ ] Enter View As Manager mode
- [ ] Perform Manager actions
- [ ] Exit View As Manager mode
- [ ] Verify multi-company access
- [ ] Logout

#### Owner Flow
- [ ] Login as Owner
- [ ] All Manager features work
- [ ] All Director features work
- [ ] Manage Companies
- [ ] Manage Directors
- [ ] Access Diagnostic page
- [ ] Logout

### Security & Authorization Testing

#### Page Access Control
- [ ] Employee CANNOT access /Admin/* (except allowed pages)
- [ ] Employee CANNOT access /Director/*
- [ ] Trainee CANNOT create swap requests
- [ ] Manager CAN access /Admin/* (except Owner-only)
- [ ] Manager CANNOT access /Admin/Companies
- [ ] Manager CANNOT access /Admin/Directors
- [ ] Manager CANNOT access /Diagnostic
- [ ] Director CAN access multi-company features
- [ ] Owner CAN access all pages

#### Data Scoping
- [ ] CompanyId filtering still enforced
- [ ] Users only see their company data
- [ ] Directors see assigned companies only
- [ ] Owner sees all companies

### Localization & RTL Testing

#### English (en-US)
- [ ] All pages display in English
- [ ] No missing localization keys
- [ ] Dates formatted correctly (MM/DD/YYYY)
- [ ] Numbers formatted correctly
- [ ] LTR layout correct

#### Hebrew (he-IL)
- [ ] All pages display in Hebrew
- [ ] No missing localization keys
- [ ] All navigation labels in Hebrew
- [ ] All page content in Hebrew
- [ ] All buttons/CTAs in Hebrew
- [ ] Dates formatted correctly (DD/MM/YYYY)
- [ ] Numbers formatted correctly
- [ ] RTL layout correct (sidebar mirrors, tables reverse, forms align right)
- [ ] No layout breaks
- [ ] Breadcrumbs reverse correctly

### Dark Mode Testing

#### Light Mode
- [ ] All pages readable
- [ ] Good contrast throughout
- [ ] No visual glitches
- [ ] Forms readable
- [ ] Tables readable
- [ ] Cards have appropriate shadows

#### Dark Mode
- [ ] All pages readable
- [ ] Good contrast throughout
- [ ] No harsh colors
- [ ] Forms readable
- [ ] Tables readable
- [ ] Cards have appropriate shadows
- [ ] No "light mode bleed" (elements not themed)

#### Theme Toggle
- [ ] Toggle works from any page
- [ ] Transition is smooth
- [ ] Theme persists on reload
- [ ] System preference detected on first visit

### Responsive Testing

#### Desktop (1920x1080)
- [ ] Layout looks correct
- [ ] Sidebar fully visible
- [ ] All content readable

#### Tablet (1024x768)
- [ ] Layout adapts correctly
- [ ] Sidebar may collapse
- [ ] Navigation still accessible

#### Mobile (375x667)
- [ ] Layout adapts correctly
- [ ] Sidebar collapses to icons or hamburger
- [ ] Tables scroll horizontally
- [ ] Forms stack vertically
- [ ] All features accessible

### Browser Testing

#### Chrome
- [ ] All features work
- [ ] Ctrl/Cmd+K works
- [ ] Theme persistence works
- [ ] No console errors

#### Firefox
- [ ] All features work
- [ ] Ctrl/Cmd+K works
- [ ] Theme persistence works
- [ ] No console errors

#### Safari
- [ ] All features work
- [ ] Cmd+K works
- [ ] Theme persistence works
- [ ] No console errors

#### Edge
- [ ] All features work
- [ ] Ctrl+K works
- [ ] Theme persistence works
- [ ] No console errors

### Performance Testing

- [ ] Home page loads < 2s
- [ ] Schedule page loads < 3s
- [ ] Calendar rendering smooth
- [ ] No noticeable lag when toggling theme
- [ ] No noticeable lag when switching language
- [ ] Command palette opens instantly
- [ ] No memory leaks (check DevTools)

---

## Critical Feature Verification

### Assignments & Conflicts
- [ ] Conflict detection works (time-off overlaps)
- [ ] Back-to-back shift warnings work
- [ ] Weekly hour cap enforced
- [ ] Rest hour requirements enforced
- [ ] Busy status indicators accurate
- [ ] Trainee shadowing works

### Request Workflows
- [ ] Time-off request creation works
- [ ] Time-off approval removes conflicting shifts
- [ ] Time-off decline sends notification
- [ ] Swap request creation works
- [ ] Swap approval reassigns shift
- [ ] Swap decline sends notification
- [ ] Trainees CANNOT create swaps

### Concurrency Handling
- [ ] Concurrent shift updates detected
- [ ] User warned about conflicts
- [ ] Last-write-wins or proper merge

### Notifications
- [ ] Notifications created on approvals/declines
- [ ] Unread count accurate
- [ ] Notification badge displays
- [ ] Notification center shows messages

### Multi-Tenancy
- [ ] CompanyId scoping enforced everywhere
- [ ] Director can switch companies
- [ ] Director sees only assigned companies
- [ ] Owner sees all companies

---

## Regression Testing

### Features That Must Not Break
- [ ] Login/Logout (including lockouts)
- [ ] Password recovery (email+phone verification)
- [ ] Signup flow (if enabled)
- [ ] Rate limiting (login attempts)
- [ ] CSRF protection (anti-forgery tokens)
- [ ] Role-based authorization
- [ ] Email sending (time-off approvals, password resets)
- [ ] Audit logging
- [ ] API key management

---

## Sign-Off Criteria

### Before Moving to Next Phase
Each phase must pass:
- [ ] Visual inspection in light mode
- [ ] Visual inspection in dark mode
- [ ] Test in English
- [ ] Test in Hebrew (full RTL verification)
- [ ] No console errors
- [ ] No build warnings
- [ ] Localization complete (no hardcoded strings)

### Before Final Deployment
- [ ] All phases complete
- [ ] All checklists above passed
- [ ] No known bugs
- [ ] Performance acceptable
- [ ] Documentation updated
- [ ] REDESIGN_PROGRESS.md marked complete

---

## Bug Tracking

| Bug ID | Phase | Description | Severity | Status | Resolution |
|--------|-------|-------------|----------|--------|------------|
| - | - | - | - | - | - |

**Severity**: Critical / High / Medium / Low
**Status**: Open / In Progress / Resolved / Won't Fix

---

**Last Updated**: 2025-11-17
**Next Review**: After each phase completion
