# ShiftManager - Redesigned Pages Reference

This document provides a quick reference for all pages that were redesigned, including their locations, key features, and what was changed.

---

## Employee Journey Pages

### 1. Home Dashboard
**File**: `Pages/Home/Index.cshtml` + `Pages/Home/Index.cshtml.cs`
**Route**: `/Home/Index`
**Status**: ✅ Complete

**What Changed**:
- Created new PageModel with role-aware data loading
- Implemented metrics cards (Next Shift, Weekly Summary, Notifications)
- Role-specific cards (Employee vs Manager vs Owner)
- Time-based greetings
- Modern card layout

**Key Features**:
- Shows upcoming shifts for employees
- Shows staffing needs for managers
- Shows company overview for owners
- Weekly statistics (hours, days with shifts, days off)
- Notification counts
- Request approval counts

**Localization Keys Added**:
- ViewMyUpcomingShifts, NextShift, WeeklySummary, TotalHours
- DaysWithShifts, DaysOff, StaffingNeeded, OpenShifts
- FullyStaffed, PendingApprovals, TimeOffRequests, ShiftSwaps
- YourCompanies, TotalCompanies, ActiveCompanies

---

### 2. Schedule Workspace
**File**: `Pages/Schedule/Index.cshtml` + `Pages/Schedule/Index.cshtml.cs`
**Route**: `/Schedule/Index`
**Status**: ✅ Complete

**What Changed**:
- Created new PageModel with role detection
- Implemented calendar rail sidebar with calendar toggles
- Added view controls (Calendar/Table, Month/Week/Day)
- Role-aware action buttons
- Calendar iframe integration
- Responsive design

**Key Features**:
- Calendar rail with multiple calendars
- View switcher (Calendar vs Table)
- Time period switcher (Month/Week/Day)
- "Request Time Off" button for employees
- "Assign Shifts" button for managers
- JavaScript for view switching

**Note**: This is a **new page** that provides a unified workspace. The original calendar pages (Month, Week, Day, Table) still exist and work through iframe integration.

---

### 3. My Requests
**File**: `Pages/My/Requests.cshtml`
**Route**: `/My/Requests`
**Status**: ✅ Complete

**What Changed**:
- Added modern page header
- Redesigned forms section with card layout
- Updated Time Off form with dynamic end date toggle
- Redesigned Shift Swap form
- Updated request history with side-by-side layout
- Status badges with color coding
- Empty states
- Modern form components

**Key Features**:
- Submit new time-off requests
- Submit shift swap requests
- View request history (approved, pending, rejected)
- Color-coded status badges
- Dynamic form validation
- Responsive grid layout

**Localization Keys Added**:
- NewRequest
- ManageYourTimeOffAndShiftSwaps

---

### 4. My Team
**File**: `Pages/MyTeam/Index.cshtml`
**Route**: `/MyTeam/Index`
**Status**: ✅ Complete

**What Changed**:
- Added modern page header
- Updated topbar and action buttons
- Enhanced week navigation controls
- Refined member cards and status badges
- Modernized all modals (switcher, form, configure, delete)
- Updated form components
- Responsive design enhancements

**Key Features**:
- Team calendar view for selected week
- Member cards with avatars
- Status indicators (shift, vacation, on-duty, free)
- Calendar management (create, rename, delete, configure members)
- Week navigation (previous, next, this week)
- Responsive member grid

**Localization Keys Added**:
- ViewYourTeamScheduleAndAvailability

---

### 5. My Profile
**File**: `Pages/My/Profile.cshtml`
**Route**: `/My/Profile`
**Status**: ✅ Complete

**What Changed**:
- Added modern page header
- Redesigned avatar section with upload interface
- Redesigned Personal Information card
- Redesigned Professional Information card (role-aware locking)
- Redesigned Emergency Contact card
- Modern form components
- Action buttons
- Responsive design

**Key Features**:
- Avatar upload
- Personal info (name, email, phone, birthday)
- Professional info (role, join date) - managers can edit
- Emergency contact
- Form validation
- Success/error alerts

**Localization Keys Added**:
- ManageYourPersonalAndProfessionalInformation

---

## Manager Journey Pages

### 6. Requests Inbox
**File**: `Pages/Requests/Index.cshtml`
**Route**: `/Requests/Index`
**Status**: ✅ Complete

**What Changed**:
- Added modern page header
- Redesigned error alerts
- Created request card components with user avatars
- Added Time Off section with duration calculation
- Added Swap requests section
- Implemented action buttons (approve/decline)
- Empty states for both sections
- Responsive design

**Key Features**:
- Approve/decline time-off requests
- Approve/decline shift swap requests
- Duration calculation for time-off
- User avatars on cards
- Reason display
- Empty states when no pending requests
- Responsive card grid

**Localization Keys Added**:
- ReviewAndApproveTeamRequests

---

### 7. People & Users
**File**: `Pages/Admin/Users.cshtml`
**Route**: `/Admin/Users`
**Status**: ✅ Complete

**What Changed**:
- Added modern page header
- Redesigned Join Requests section with filters
- Added batch approval functionality
- Redesigned Existing Users section with inline editing
- Added Add User form section
- Implemented JavaScript for batch operations
- Responsive design

**Key Features**:
- Batch approve/decline join requests
- Filter join requests (All/Pending/Approved/Declined)
- Inline edit user roles
- Add new users with form
- Delete users
- User count display
- Responsive table/grid

**Localization Keys Added**:
- ManageUsersJoinRequestsAndPermissions

---

### 8. Analytics Dashboard
**File**: `Pages/Admin/Analytics.cshtml`
**Route**: `/Admin/Analytics`
**Status**: ✅ Complete

**What Changed**:
- Added modern page header
- Redesigned date range filter card with export
- Created summary stats grid with 4 themed metric cards
- Redesigned Employee Hours table
- Redesigned Understaffing Report with color-coded deficits
- Redesigned Top Swappers table
- Added Swap Statistics mini stats grid
- Added Time-Off Statistics mini stats grid
- Responsive design for all sections

**Key Features**:
- Date range filter (start/end date picker)
- Export to CSV
- Summary metrics (total hours, average per employee, etc.)
- Employee hours table with sorting
- Understaffing report with visual deficit indicators
- Top swappers leaderboard
- Swap statistics (total, approved, pending, rejected)
- Time-off statistics
- Responsive grid layouts

**Localization Keys Added**:
- ViewInsightsMetricsAndReports

---

### 9. Configuration (Config)
**File**: `Pages/Admin/Config.cshtml`
**Route**: `/Admin/Config`
**Status**: ✅ Complete

**What Changed**:
- Added modern page header
- Redesigned Company Settings section with form grid
- Redesigned Email Configuration section
- Redesigned OnDuty Types section with modal
- Added modal for adding new OnDuty types
- Implemented JavaScript for modal control
- Responsive design

**Key Features**:
- Company settings (RestHours, WeeklyCap)
- Email configuration (enable, API key, URL, from address)
- OnDuty types management (default + custom)
- Add new OnDuty types with modal
- Delete custom OnDuty types
- Form validation
- Modal interactions

**Localization Keys Added**:
- ConfigureSystemSettings

---

### 10. Shift Types
**File**: `Pages/Admin/ShiftTypes.cshtml`
**Route**: `/Admin/ShiftTypes`
**Status**: ✅ Complete

**What Changed**:
- Added modern page header
- Redesigned shift types table with inline editing
- Added delete buttons with confirmation
- Save button to update all shift types
- Modern table styling
- Responsive design

**Key Features**:
- View all shift types (default + custom)
- Inline edit start/end times
- Delete custom shift types
- Save all changes at once
- Confirmation dialog for deletions
- Display shift key and name

**Localization Keys Added**:
- ManageShiftTypesAndSchedules

---

### 11. Time Off Management
**File**: `Pages/Admin/TimeOff.cshtml`
**Route**: `/Admin/TimeOff`
**Status**: ✅ Complete

**What Changed**:
- Added modern page header
- Redesigned time-off table with date info
- Added duration calculation
- Status badges (Active Today / Past)
- Conditional delete button (future only)
- Info box explaining deletion rules
- Empty state with link to pending requests
- Responsive design

**Key Features**:
- View all approved time-off
- See duration (X days)
- Status indicators (active, past)
- Delete future time-off requests
- Cannot delete active/past requests
- Link to pending requests
- Empty state

**Localization Keys Added**:
- ManageApprovedTimeOffRequests

---

## Director/Owner Journey Pages

### 12. Companies Management
**File**: `Pages/Admin/Companies.cshtml`
**Route**: `/Admin/Companies`
**Status**: ✅ Complete

**What Changed**:
- Added modern page header
- Redesigned Add Company form with three fieldsets
- Implemented JavaScript for dynamic field toggling
- Redesigned companies table
- Added rename modal with backdrop
- Info box explaining company creation
- Responsive design

**Key Features**:
- Add new companies
- Assign existing director OR create new manager
- Dynamic form (manager fields optional if director selected)
- Rename companies
- Delete companies
- Company slug display
- Modal for renaming

**Localization Keys Added**:
- ManageCompaniesAndOrganizations

**JavaScript**:
- Toggles manager field requirements based on director selection
- Dims manager fieldset when director selected

---

### 13. Directors Management
**File**: `Pages/Admin/Directors.cshtml`
**Route**: `/Admin/Directors`
**Status**: ✅ Complete

**What Changed**:
- Added modern page header
- Redesigned Assign Director form with 3-column grid
- Redesigned Current Assignments table
- Info box explaining director permissions
- Empty state for no assignments
- Responsive design

**Key Features**:
- Assign directors to companies
- View all director-company assignments
- Unassign directors
- Dropdown selectors for director and company
- Assignment count display
- Empty state

**Localization Keys Added**:
- ManageDirectorAssignments

---

### 14. Audit Log
**File**: `Pages/Admin/AuditLog.cshtml`
**Route**: `/Admin/AuditLog`
**Status**: ✅ Complete

**What Changed**:
- Added modern page header
- Redesigned filter grid (6 fields)
- Added Export CSV button
- Redesigned audit log table with expandable rows
- Purple gradient action badges
- Expandable detail rows with additional info
- Pagination controls
- Responsive design

**Key Features**:
- Filter by date range, user, action, entity type, search term
- Export filtered results to CSV
- Expandable rows (click to see details)
- Shows timestamp, action, user, entity, description
- Shows additional details and user agent on expand
- Pagination (first, previous, page numbers, next, last)
- Empty state

**Localization Keys Added**:
- ViewSystemAuditLog

**JavaScript**:
- Toggle detail rows (toggleDetailsX functions)
- Separate function per row for expansion

---

## Public Pages

### 15. Chores Calendar
**File**: `Pages/Public/Chores.cshtml`
**Route**: `/Public/Chores`
**Status**: ✅ Updated (header only)

**What Changed**:
- Added modern page header with title and subtitle

**Note**: This page already had modern inline styling. Only the page header was added for consistency.

**Localization Keys Added**:
- ManageChoresAndTaskAssignments

---

### 16. On Duty Calendar
**File**: `Pages/Public/OnDuty.cshtml`
**Route**: `/Public/OnDuty`
**Status**: ✅ Updated (header only)

**What Changed**:
- Added modern page header with title and subtitle

**Note**: This page already had modern inline styling. Only the page header was added for consistency.

**Localization Keys Added**:
- ManageOnDutyRotationsAndAssignments

---

## Pages NOT Changed

The following pages were **not modified** as they already had modern styling:

### Calendar Pages (Already Modern)
- **Pages/Calendar/Month.cshtml** - Monthly calendar view ✅
- **Pages/Calendar/Week.cshtml** - Weekly calendar view ✅
- **Pages/Calendar/Day.cshtml** - Daily calendar view ✅
- **Pages/Calendar/Table.cshtml** - Table view ✅

These pages already use:
- CSS variables
- Modern card/grid layouts
- Responsive design
- Breadcrumb navigation
- Proper header structure

### Other Existing Pages (Not in Redesign Scope)
- Assignments pages
- Authentication pages (Login, Register, etc.)
- Director-specific pages (CompanyFilter, ViewAsMode, etc.)
- API documentation pages
- Error pages
- Other utility pages

---

## Common Patterns Used

### Page Structure
All redesigned pages follow this structure:

```
1. @page directive
2. @model directive
3. Using statements
4. Breadcrumb component
5. Page header (title + subtitle)
6. Page content (sections, cards, tables)
7. Modals (if needed)
8. JavaScript (if needed)
```

### Page Header Pattern
```html
<div class="page-header">
    <div class="page-header-content">
        <h1 class="page-title">Title</h1>
        <p class="page-subtitle">Subtitle</p>
    </div>
</div>
```

### Section Card Pattern
```html
<div class="section-card">
    <h3 class="section-header">Section Title</h3>
    <!-- Content -->
</div>
```

### Form Pattern
```html
<div class="form-group">
    <label class="form-label">Label</label>
    <input type="text" class="form-input" />
</div>
```

### Button Pattern
```html
<button class="btn btn-primary">Primary Action</button>
<button class="btn btn-ghost">Secondary Action</button>
<button class="btn btn-danger">Delete Action</button>
```

### Table Pattern
```html
<table class="data-table">
    <thead>
        <tr><th>Column</th></tr>
    </thead>
    <tbody>
        <tr><td>Data</td></tr>
    </tbody>
</table>
```

---

## Quick Reference: File Locations

### New Pages Created
- Pages/Home/Index.cshtml.cs
- Pages/Home/Index.cshtml
- Pages/Schedule/Index.cshtml.cs
- Pages/Schedule/Index.cshtml

### Redesigned Existing Pages
- Pages/My/Requests.cshtml
- Pages/My/Profile.cshtml
- Pages/MyTeam/Index.cshtml
- Pages/Requests/Index.cshtml
- Pages/Admin/Users.cshtml
- Pages/Admin/Analytics.cshtml
- Pages/Admin/Config.cshtml
- Pages/Admin/ShiftTypes.cshtml
- Pages/Admin/TimeOff.cshtml
- Pages/Admin/Companies.cshtml
- Pages/Admin/Directors.cshtml
- Pages/Admin/AuditLog.cshtml
- Pages/Public/Chores.cshtml (header only)
- Pages/Public/OnDuty.cshtml (header only)

### Core Files Modified
- Pages/Shared/_Layout.cshtml (sidebar, header, command palette)
- wwwroot/css/site.css (design system)
- wwwroot/css/rtl.css (RTL support)
- wwwroot/js/site.js (theme, command palette, features)
- Resources/SharedResources.resx (English localization)
- Resources/SharedResources.he-IL.resx (Hebrew localization)

---

## Summary

**Total Pages Redesigned**: 16 pages
- 5 Employee journey pages
- 6 Manager journey pages
- 3 Director/Owner journey pages
- 2 Public pages (headers only)

**Total Localization Keys Added**: 18 keys (English + Hebrew)

**Common Design Elements**:
- Modern page headers (title + subtitle)
- Section cards for content organization
- Consistent form components
- Status badges with color coding
- Empty states with helpful messages
- Responsive grid layouts
- Action buttons (primary, ghost, danger)
- Modal dialogs where appropriate
- Data tables with inline actions

All pages follow the same design language and use the shared CSS design system.
