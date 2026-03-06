# Batch 4: Calendar & Schedule Pages Audit

---

## 1. Calendar/Index

### 1) Identity & Routing
- **Route**: `/Calendar` (`@page` — default route)
- **Purpose**: Calendar landing page — entry point to all 4 calendar types (Shifts, Chores, On-Call, Overview)
- **Redirects**: None inbound/outbound; purely navigational
- **Query Params**: None

### 2) Access Control & Scope
- **Auth**: `[Authorize]` — any authenticated user
- **Tenant Scope**: No data loaded; individual calendars handle their own access control
- **Gaps**: None — this is a simple navigation page

### 3) Localization
- Uses `IStringLocalizer<SharedResources>` via `@inject`
- Keys: `Calendars`, `CalendarsSubtitle`, `ShiftsCalendar`, `ShiftsCalendarDesc`, `ChoresCalendar`, `ChoresCalendarDesc`, `OnCallCalendar`, `OnCallCalendarDesc`, `OverviewCalendar`, `OverviewCalendarDesc`, `ViewCalendar`
- No RTL-specific handling (CSS handles via design tokens)

### 4) UI & Design Inventory
- **Layout**: `_Layout`; card grid with 4 large clickable cards
- **Interactive Elements**:
  - `<a href="/Calendar/Shifts">` — Shifts card
  - `<a href="/Calendar/Chores">` — Chores card
  - `<a href="/Calendar/OnCall">` — On-Call card
  - `<a href="/Calendar/Overview">` — Overview card
- **UI States**: None (no data loading, no empty/error states)
- **Shared Components**: None (pure HTML/CSS)
- **CSS**: `calendar-landing.css` (external)

### 5) Navigation Map
- **Nav Targets**: `/Calendar/Shifts`, `/Calendar/Chores`, `/Calendar/OnCall`, `/Calendar/Overview`
- **Reached Via**: Main sidebar nav, likely "Calendars" link
- **Breadcrumbs**: None

### 6) Data Dependencies & Side Effects
- **Reads**: Nothing — `OnGet()` is empty
- **Writes**: Nothing

### 7) Forms & Submissions
- None

### 8) Interesting Behaviors
- Uses emoji icons (📋🧹📞👁️) for visual distinction
- `data-ui-version="v2"` attribute marks this as redesigned UI

### 9) Traceability
- Code-behind: `Pages/Calendar/Index.cshtml.cs` — trivially empty `OnGet()`
- No services injected

---

## 2. Calendar/Shifts

### 1) Identity & Routing
- **Route**: `/Calendar/Shifts` (`@page`)
- **Purpose**: Excel-style shifts calendar — primary deliverable for "Excel Calendars" feature. Shows shift types as rows, dates as columns, with assignment data in cells
- **Redirects**: Redirects to `/Error` if user ID invalid, no company context, or company has no molecule
- **Query Params**: `MoleculeId` (int?), `JobTypeId` (int?), `Start` (string, yyyy-MM-dd), `ViewMode` (week|2weeks|month), `Mode` (shift|user), `CapacityMode` (bool), `JustMine` (bool)

### 2) Access Control & Scope
- **Auth**: `[Authorize]` — any authenticated user
- **Scope**: Molecule-scoped via `MoleculeId`+`JobTypeId` params. Validates molecule access against user's grants via `LoadAvailableMoleculesAsync`. Falls back to user's own molecule if param invalid
- **Edit Permission**: Checked via `HasGrantAsync("AssignAlhutShifts")` or `HasGrantAsync("AssignTextShifts")` — controls `CanEdit` flag and Capacity Mode toggle visibility
- **IgnoreQueryFilters**: Used on `ShiftTypes`, `ShiftInstances`; SECURITY-AUDITED comments present
- **Gaps**: None identified — molecule access validated, edit permission checked

### 3) Localization
- `IStringLocalizer<SharedResources>` + `ILocalizationService` for date formatting
- Keys: `ShiftsCalendar`, `Molecule`, `JobType`, `ViewMode`, `Week`, `TwoWeeks`, `Month`, `Previous`, `Next`, `Print`, `ByShift`, `ByUser`, `CapacityMode`, `Filter`, `JustMine`, `ShowFriends`, `FilterComingSoon`, `Calendar_Empty_NoShifts`, `Calendar_Empty_SelectJobType`, `Calendar_Empty_NoJobTypes`, `Unassigned`
- Mix of `@Localizer["key"]` and `<loc key="..." />` tag helper patterns
- `FormatMediumDate()` for locale-aware date display

### 4) UI & Design Inventory
- **Layout**: `_Layout`; toolbar + calendar table
- **Toolbar Row 1**: Molecule selector dropdown, JobType selector dropdown, ViewMode selector dropdown
- **Toolbar Row 2**: Date navigation (←/→ arrows + date picker), Print button
- **Toolbar Row 3**: Shift/User mode toggle, Capacity mode toggle (grant-gated), Filter button (placeholder — TODO), JustMine toggle, ShowFriends toggle
- **Calendar Table**: `ExcelCalendarTable` ViewComponent renders the grid
- **Empty State**: SVG calendar icon + contextual message (no shifts / select job type / no job types)
- **Shared Components**: `ExcelCalendarTable`, `Breadcrumb`
- **CSS**: Inline `<style>` block with responsive breakpoints (768px, 480px) + print styles

### 5) Navigation Map
- **Nav Targets**: Previous/Next period links (self-referencing with different `Start`), mode toggles (self-referencing), `/Calendar/Month` (via breadcrumb)
- **Reached Via**: `/Calendar` landing page card, sidebar nav
- **Breadcrumb**: "Calendars" → "Shifts Calendar"

### 6) Data Dependencies & Side Effects
- **Reads**:
  - `Companies` (with Molecule include) — user's company context
  - `Users` (current user with JobType) — default JobTypeId
  - `Molecules` — available molecules filtered by grants
  - `JobTypes` via `IJobTypeService.GetJobTypesForMoleculeAsync`
  - `ShiftTypes` via `IgnoreQueryFilters` — molecule+jobType scoped
  - `ShiftInstances` + `ShiftAssignments` via `IShiftCalendarService`
  - `Companies` (for cross-company name lookup in molecule mode)
  - Overlay data (vacations, chores, on-duty) in user mode via `IShiftCalendarService.GetOverlaysAsync`
- **Writes**: None (read-only page)
- **Real-time**: SignalR via `calendar-realtime.js` — joins `shifts-{moleculeId}-{jobTypeId}` group, handles `onAssignmentChanged` and `onCapacityChanged` events, triggers `refreshCalendarData()` AJAX call to `/Api/Calendar/GetShiftsData`

### 7) Forms & Submissions
- None (read-only; shift management is on Calendar/Table)

### 8) Interesting Behaviors
- **SignalR Real-time Updates**: Subscribes to assignment/capacity change events; full refresh on change
- **XSS Protection**: Custom `escapeHtml()` function for safe innerHTML rendering
- **Keyboard Navigation**: Alt+Left/Right for period navigation, Ctrl+P for print
- **Friends Highlighting**: Toggle to highlight friend assignments (uses `/Api/Friends/Ids`)
- **Filter Modal**: Currently a placeholder (TODO — shows alert)
- **Print Styles**: Toolbar hidden when printing
- **Lazy Loading**: Uses `calendar-lazy-rows.js` for performance
- **Bottom Sheet**: Uses `calendar-bottom-sheet.js` for mobile cell details

### 9) Traceability
- **Services**: `IShiftCalendarService`, `IGrantService`, `ICompanyContext`, `IJobTypeService`
- **ViewComponents**: `ExcelCalendarTable`, `Breadcrumb`
- **JS**: `calendar-lazy-rows.js`, `calendar-bottom-sheet.js`, `calendar-realtime.js`, `signalr.min.js`
- **Models**: `ExcelCalendarTableViewModel`, `ExcelCalendarRow`, `ExcelCalendarCell`, `ExcelCalendarAssignment`, `ExcelCalendarOverlay`, `FyiOverlayData`

---

## 3. Calendar/Table

### 1) Identity & Routing
- **Route**: `/Calendar/Table` (`@page`)
- **Purpose**: Shift management table — the primary CRUD interface for shift assignments. Supports both company mode and molecule mode
- **Redirects**: None (errors returned as JSON from POST handlers)
- **Query Params**: `start` (string), `view` (week|2weeks|month), `MoleculeId` (int?), `JobTypeId` (int?)

### 2) Access Control & Scope
- **Auth**: `[Authorize(Policy = "Grant:ManagerHomeAccess")]` — requires manager grant
- **Scope**: Dual-mode:
  - **Company Mode** (default): Standard tenant-filtered queries
  - **Molecule Mode** (when `MoleculeId`+`JobTypeId` set): Cross-company via `IgnoreQueryFilters`, validated against user's grant-derived accessible molecules
- **POST Authorization**: `OnPostAssignEmployeeAsync` explicitly checks `AssignAlhutShifts`, `AssignTextShifts`, `AssignBRShifts`, `AssignTechShifts` grants (FINDING-002 FIX)
- **IgnoreQueryFilters**: Extensively used with SECURITY-AUDITED comments; justified by molecule-scoped re-filtering
- **Gaps**: POST handlers (EnsureShiftInstance, CreateShiftInstance, AssignUserToSlot) don't have explicit grant checks — they rely on page-level `Grant:ManagerHomeAccess` policy

### 3) Localization
- `IStringLocalizer<SharedResources>` via `@inject`
- Keys: `TableView`, `Calendar`, `Previous`, `Next`, and many more in the HTML template

### 4) UI & Design Inventory
- **Layout**: `_Layout`; navigation bar + shift table with interactive cells
- **Interactive Elements**:
  - Date navigation (Previous/Next/Today links)
  - View mode toggle (week/2weeks/month)
  - Molecule+JobType selectors (when in molecule mode)
  - **Per-cell**: Click to open action menu/employee dropdown
  - **Assignment slots**: Click assigned user → action menu (unassign/clear/swap trainee); Click unassigned → employee dropdown
  - **Add assignment button** per cell
  - **Adjust headcount buttons** (+/−)
  - **Add shift row button** (creates new shift instances)
  - **Shift delete button** per row
  - **OVR badge** for overridden/detached instances
  - **Reset program button** for detached instances
- **Employee dropdown**: Color-coded busy indicators (vacation=info, shift=danger, chore=warning)
- **Modals**: Amount picker modal for headcount adjustment
- **Empty States**: Empty cells shown with dashed border and "unassigned" styling
- **Shared Components**: `Breadcrumb`

### 5) Navigation Map
- **Nav Targets**: Previous/Next period (self), `/Calendar/Month` (breadcrumb)
- **Reached Via**: Calendar/Shifts "manage" links, Calendar/Day quick actions, admin navigation
- **Breadcrumb**: "Calendar" → "Table View"

### 6) Data Dependencies & Side Effects
- **Reads (OnGet)**:
  - `Companies` (with Molecule) — context
  - `ShiftInstances` + `ShiftAssignments` — grid data
  - `ShiftTypes` via `IShiftTypeCacheService`
  - `Users` — employee list for dropdowns
  - `BusyUsersByDate` via `IBusyUserService` — conflict indicators
  - `ShiftPrograms` via `IShiftProgramService` — active programs
- **Writes (POST handlers)**:
  - `OnPostEnsureShiftInstanceAsync` — Creates/updates ShiftInstance + empty ShiftAssignment slots (transactional)
  - `OnPostCreateShiftInstanceAsync` — Creates new ShiftInstance (fails if exists)
  - `OnPostAssignUserToSlotAsync` — Assigns user to specific slot; validates via `IShiftAssignmentService.ValidateShiftAssignmentAsync`; checks time overlap
  - `OnPostAssignEmployeeAsync` — Full assignment flow with grant checks, creates instance if needed, validation with override tokens
  - `OnPostUnassignEmployeeAsync` — Removes assignment
  - `OnPostClearAssignmentAsync` — Clears user/trainee from slot without deleting it
  - Plus additional handlers (not fully read due to file size)
- **SignalR**: Sends `CalendarAssignmentChangedEvent` via `ICalendarNotificationService` on assign/unassign
- **Error Handling**: All POST handlers return `{ success: bool, error?: string }` JSON; use try/catch with logging; concurrency handled via `IConcurrencyService.SaveWithConcurrencyHandlingAsync` (returns 409 on conflict)

### 7) Forms & Submissions
- All submissions are AJAX (JSON body → JSON response)
- **EnsureShiftInstance**: `{ ShiftTypeId, Date, StaffingRequired }` — StaffingRequired validated 1-30
- **AssignUserToSlot**: `{ AssignmentId, UserId, OverrideToken? }` — Full validation (job type, grouping, weekly cap, rest hours, time overlap)
- **AssignEmployee**: `{ ShiftTypeId, Date, UserId, OverrideToken? }` — Creates instance if needed, same validation + grant check
- **UnassignEmployee**: `{ AssignmentId }` — Removes assignment record
- **Override Token Pattern**: When validation produces warnings (not errors), returns `requiresOverride=true` + `overrideToken` + `warnings[]`. Client confirms and re-submits with token

### 8) Interesting Behaviors
- **Override Token System**: Sophisticated validation flow — hard errors block, soft warnings require user confirmation via cryptographic token
- **Concurrency Handling**: All saves go through `SaveWithConcurrencyHandlingAsync` with entity-specific concurrency
- **Molecule Mode**: Dynamically switches between company-scoped and cross-company molecule-scoped queries
- **Detached Instances**: Shows OVR badge for manually overridden instances; offers "reset to program" action
- **Busy User Service**: Pre-loads all busy statuses for the date range in a single batch query (avoids N+1)
- **Dense Mode**: CSS class for multi-week/month views (smaller fonts, tighter spacing)
- **Performance**: Batch query for BusyUsersByDate, ShiftTypeCache for shift types

### 9) Traceability
- **Services**: `IShiftAssignmentService`, `IBusyUserService`, `IShiftTypeCacheService`, `IShiftProgramService`, `ICalendarNotificationService`, `IConcurrencyService`, `IGrantService`, `ICompanyContext`, `IJobTypeService`
- **Hubs**: `CalendarGroups.Shifts()` for SignalR group naming
- **Models**: `AssignmentInfo` (inner class), `EnsureShiftInstanceRequest`, `CreateShiftInstanceRequest`, `AssignUserToSlotRequest`, `AssignEmployeeRequest`, `UnassignEmployeeRequest`, `ClearAssignmentRequest`

---

## 4. Calendar/Day

### 1) Identity & Routing
- **Route**: `/Calendar/Day` (`@page`)
- **Purpose**: Daily calendar view — read-only unified view of shifts, chores, and on-duty for a single day
- **Redirects**: None (silently returns empty if user ID invalid)
- **Query Params**: `year` (int?), `month` (int?), `day` (int?), `showMyItems` (bool, via URL)

### 2) Access Control & Scope
- **Auth**: `[Authorize]` — any authenticated user
- **Scope**: Scope-switcher integrated (`IScopeFilterService`) — supports mine/company/molecule/area scopes. Falls back to company scope if user lacks access to requested scope
- **Admin Features**: Quick-add chore/on-duty forms gated by `HasGrantAsync("AccessAdminNavigation")`
- **Grant-gated UI**: Coverage metrics shown only with `ViewAnalytics` grant (via `<require-grant>` tag helper)

### 3) Localization
- Mix of `@Localizer["key"]` and `<loc key="..." />` tag helper
- Keys: `Calendar_MyCalendar`, `Day`, `Today`, `Calendar_Shifts`, `Calendar_Chores`, `Calendar_OnDuty`, `Calendar_Pending`, `Calendar_Coverage`, `Month`, `Week`, `Calendar_QuickActions`, `Calendar_ManageShifts`, `Calendar_ManageChores`, `Calendar_ManageOnDuty`, `Calendar_SelectAssignee`, `Trainee`, `AssignedTo`, `Time`, `Staffing`, `Details`, `Delete`, `Manage`, `Add`, `Cancel`, `Calendar_ChoreTitle`, `OnDuty_Hakam`, `OnDuty_Lead`, `Calendar_Empty_NoShiftsDay`, `Calendar_Empty_Subtitle_Mine`, `Calendar_Empty_Subtitle_All`, `Calendar_Empty_Action_ViewAll`, `Calendar_Empty_Action_CreateShift`, `Calendar_Empty_Action_GoToToday`, `Unassigned`

### 4) UI & Design Inventory
- **Layout**: `_Layout`; header with metrics → view switcher → scope switcher → quick actions → items list
- **Header**: Date display, ShowMyItemsToggle component, navigation arrows (←/Today/→)
- **Metrics Bar**: Shift count, Chore count, OnDuty count, Pending count, Coverage % (admin only)
- **View Switcher**: Month/Week/Day toggle links
- **Scope Switcher**: `ScopeSwitcher` ViewComponent
- **Quick Actions** (admin only): Links to Table, Chores, OnDuty management
- **Day Items**: Vertical list of cards — each shows icon, title, assignee, time range, staffing info, details
  - Personal items: highlighted border (primary color)
  - Other items: muted opacity
  - Admin actions: Delete (chores/on-duty) or Manage (shifts)
- **Quick Add** (admin only): Inline form for adding chores/on-duty with assignee dropdown
- **Empty State**: SVG icon + contextual message + action buttons (View All, Create Shift, Go to Today)
- **Shared Components**: `Breadcrumb`, `CalendarSkeleton` (loading), `ShowMyItemsToggle`, `ScopeSwitcher`
- **CSS**: Extensive inline styles with responsive breakpoints (1920+, 768, 480, 360) + touch device enhancements

### 5) Navigation Map
- **Nav Targets**: Previous day, Today, Next day (self-links), `/Calendar/Month`, `/Calendar/Week`, `/Calendar/Table`, `/Public/Chores`, `/Public/OnDuty`
- **Reached Via**: Calendar view switcher, direct URL, sidebar nav
- **Breadcrumb**: "My Calendar" → "Day"

### 6) Data Dependencies & Side Effects
- **Reads**:
  - User claims (NameIdentifier)
  - `IScopeFilterService` — scope resolution
  - `ShiftInstances` + `ShiftAssignments` (IgnoreQueryFilters, scoped by companyIds)
  - `Chores` (IgnoreQueryFilters, scoped by companyIds)
  - `OnDuties` (IgnoreQueryFilters — global by design)
  - `OnDutyTypeConfigs` — custom duty type names/icons
  - `EligibleAssignees` via `IChoreService` (admin only)
- **Writes**: None (read-only; quick-add submits go to API endpoints)
- **Error Handling**: Safe date parsing with `TryCreateValidDate()` — clamps invalid dates to nearest valid

### 7) Forms & Submissions
- Quick-add form rendered inline but submissions are client-side JS calls to API endpoints (`/Api/Calendar/QuickAddChore`, `/Api/Calendar/QuickAddOnDuty`)

### 8) Interesting Behaviors
- **Skeleton Loading**: Uses `CalendarSkeleton` ViewComponent with `data-calendar-skeleton-container` pattern — JS signals when content is ready
- **Date Boundary Handling**: `TryCreateValidDate()` clamps day to valid range for month (handles Feb 29 edge case)
- **Scope Filter**: Full mine/company/molecule/area filtering via `IScopeFilterService`
- **Consistent Calendar Pattern**: Day/Week/Month share nearly identical code structure for loading shifts/chores/on-duties
- **Inline Edit JS**: Loads `calendar-inline-edit.js` for potential inline editing support

### 9) Traceability
- **Services**: `IScopeFilterService`, `ICompanyContext`, `IChoreService`, `IGrantService`, `ILocalizationService`, `IUserPreferenceService`
- **ViewComponents**: `Breadcrumb`, `CalendarSkeleton`, `ShowMyItemsToggle`, `ScopeSwitcher`
- **Models**: `CalendarItemViewModel`, `CalendarItemType` enum, `OnDutyTypeConfig`

---

## 5. Calendar/Week

### 1) Identity & Routing
- **Route**: `/Calendar/Week` (`@page`)
- **Purpose**: Weekly calendar view — read-only unified 7-day grid of shifts, chores, on-duty
- **Query Params**: `year`, `month`, `day` (for target week)

### 2) Access Control & Scope
- **Auth**: `[Authorize]`
- **Scope**: Same scope-switcher integration as Day (mine/company/molecule/area via `IScopeFilterService`)
- **Admin Features**: Quick-add forms gated by `AccessAdminNavigation` grant

### 3) Localization
- Same pattern as Day — `Localizer` + `<loc>` tags
- Week-specific: week start/end date labels

### 4) UI & Design Inventory
- Same header/metrics/view-switcher/scope-switcher pattern as Day
- **Grid**: 7 columns (Sunday-Saturday), each with list of `CalendarItemViewModel` items
- **Empty/admin/quick-add**: Same patterns as Day

### 5) Navigation Map
- Previous/Next week links, view switcher to Month/Week/Day
- Breadcrumb: "My Calendar" → "Week"

### 6) Data Dependencies & Side Effects
- Identical to Day but for 7-day date range
- Same 3-way load: `LoadShiftsAsync` + `LoadChoresAsync` + `LoadOnDutiesAsync`
- All scope-filtered with `IgnoreQueryFilters` + company ID filtering

### 7) Forms & Submissions
- Quick-add forms (admin only) — same as Day

### 8) Interesting Behaviors
- **Code Duplication**: `LoadShiftsAsync`, `LoadChoresAsync`, `LoadOnDutiesAsync`, `TryCreateValidDate`, `GetShiftIcon`, `GetOnDutyTypeInfo`, `CalculateHeaderMetrics` are all duplicated across Day/Week/Month models (significant DRY violation)
- Week starts on Sunday (US convention)

### 9) Traceability
- Same services as Day + `IDirectorService` (injected but not obviously used in Week)

---

## 6. Calendar/Month

### 1) Identity & Routing
- **Route**: `/Calendar/Month` (`@page`)
- **Purpose**: Monthly calendar view — 6-week grid showing shifts, chores, on-duty
- **Query Params**: `year`, `month`, `jobTypeId` (int?), `shiftGroupingId` (int?)

### 2) Access Control & Scope
- **Auth**: `[Authorize]`
- **Scope**: Same scope-switcher integration. Additional v3.0 hierarchy filters: `FilterJobTypeId` and `FilterShiftGroupingId`

### 3) Localization
- Same pattern. Month-specific: month name display

### 4) UI & Design Inventory
- Same header/metrics pattern
- **Additional Filters**: JobType dropdown and ShiftGrouping dropdown (v3.0 hierarchy filters)
- **Grid**: 6 weeks × 7 days grid; items condensed in small cells
- **Schedule Export**: Exposes `StartDate`/`EndDate` properties for export functionality

### 5) Navigation Map
- Previous/Next month links, view switcher
- Breadcrumb: "My Calendar" → "Month"

### 6) Data Dependencies & Side Effects
- Same 3-way load pattern, expanded to 42-day range (6 weeks)
- **Additional**: Loads `JobTypes` via `IJobTypeService.GetJobTypesForMoleculeAsync` and `ShiftGroupings` for filter dropdowns
- Shift query adds `.Where(si => si.ShiftType.JobTypeId == FilterJobTypeId)` and `.Where(si => si.ShiftType.ShiftGroupingId == FilterShiftGroupingId)` when filters active

### 7) Forms & Submissions
- Quick-add forms (admin only)

### 8) Interesting Behaviors
- **Localization Key Inconsistency**: Uses `OnDutyHakam`/`OnDutyLead` (no underscore) vs Day/Week which use `OnDuty_Hakam`/`OnDuty_Lead` (with underscore) — likely results in missing translations in one variant
- **42-day Grid**: Always renders 6 weeks regardless of month length
- **Schedule Export Integration**: `StartDate`/`EndDate` exposed for `/Api/ScheduleExport` consumption

### 9) Traceability
- Same services + `IJobTypeService`, `IDirectorService`

---

## 7. Calendar/Chores

### 1) Identity & Routing
- **Route**: `/Calendar/Chores` (`@page`)
- **Purpose**: Excel-style chores calendar — shows users as rows, dates as columns, chore assignments in cells
- **Redirects**: `/Error` if no company context, no molecule
- **Query Params**: `MoleculeId`, `Start`, `ViewMode`, `ChoreTypeFilter` (int?), `JustMine`

### 2) Access Control & Scope
- **Auth**: `[Authorize]`
- **Scope**: Molecule-scoped (cross-company within molecule). Validates molecule access against grants with `AssignChores`/`ViewChores` grant keys
- **Edit Permission**: `HasGrantAsync("AssignChores")` controls `CanEdit` flag

### 3) Localization
- Standard `IStringLocalizer<SharedResources>` pattern

### 4) UI & Design Inventory
- Same toolbar pattern as Shifts calendar: molecule selector, date nav, view mode, chore type filter, JustMine toggle
- **Calendar Grid**: `ExcelCalendarTable` ViewComponent — users as rows, dates as columns
- **Groups**: Chore types used as optional group headers

### 5) Navigation Map
- Self-referencing navigation links
- Reached via `/Calendar` landing page

### 6) Data Dependencies & Side Effects
- **Reads**: Companies (molecule context), Molecules (via grants), ChoreTypes via `IChoreTypeService`, Users in molecule's companies (IgnoreQueryFilters), Chores via `IChoreService.GetChoresAsync`
- **Writes**: None (read-only)

### 7) Forms & Submissions
- None

### 8) Interesting Behaviors
- Users loaded cross-company within molecule via `IgnoreQueryFilters`
- ChoreType filter allows viewing specific chore categories
- Similar structure to Shifts calendar but simpler (no SignalR, no real-time)

### 9) Traceability
- **Services**: `IChoreService`, `IChoreTypeService`, `IGrantService`, `ICompanyContext`

---

## 8. Calendar/OnCall

### 1) Identity & Routing
- **Route**: `/Calendar/OnCall` (`@page`)
- **Purpose**: Excel-style on-call/duty calendar — shows duty types as rows, dates as columns
- **Redirects**: `/Error` if no company context
- **Query Params**: `AreaId`, `Start`, `ViewMode`, `DutyTypeFilter`, `JustMine`

### 2) Access Control & Scope
- **Auth**: `[Authorize]`
- **Scope**: On-duty is global by design; optionally filtered by area. Area access: users with `ViewAllAreas` grant see all areas; others see their company's area + grant-derived areas
- **Edit Permission**: `HasGrantAsync("ManageOnDutyTypes")` controls `CanEdit`

### 3) Localization
- Standard pattern. Uses `Hakam`, `Lead` keys for built-in types
- Custom types: bilingual `NameEn`/`NameHe` with culture-based selection

### 4) UI & Design Inventory
- Area selector dropdown, date nav, view mode, duty type filter, JustMine toggle
- **Calendar Grid**: `ExcelCalendarTable` — duty types as rows (with icons + colors)
- **Duty Type Rows**: Built-in (Hakam 👮, Lead ⭐) + custom types from `OnDutyTypeConfig`

### 5) Navigation Map
- Self-referencing navigation
- Reached via `/Calendar` landing page

### 6) Data Dependencies & Side Effects
- **Reads**: Companies/Molecules/Areas (hierarchy context), `OnDuties` via `IOnDutyService`, `OnDutyTypeConfigs`, Users via grants
- **Writes**: None (read-only)

### 7) Forms & Submissions
- None

### 8) Interesting Behaviors
- **Backup Type Detection**: Heuristic — checks if custom type name contains "Backup" (en) or "רזרבה" (he) to flag as backup type
- **Primary/Backup Linking**: Backup types linked to primary `Hakam` type via `PrimaryTypeValue`
- **Area Filtering**: On-duty is global but filtered by area (users in companies in molecules in the selected area)

### 9) Traceability
- **Services**: `IOnDutyService`, `IGrantService`, `ICompanyContext`

---

## 9. Calendar/Overview

### 1) Identity & Routing
- **Route**: `/Calendar/Overview` (`@page`)
- **Purpose**: Overview calendar — aggregated view of all user data (vacations, shifts, chores, on-duty, notes) for a single company
- **Redirects**: `/Error` if no company context
- **Query Params**: `Start`, `ViewMode`, `UsersFilter` (all|active|inactive), `JustMine`

### 2) Access Control & Scope
- **Auth**: `[Authorize]`
- **Scope**: Company-scoped (NOT cross-company). Users loaded for current company only
- **Note Editing**: `HasGrantAsync("WriteOverviewNotes")` gates note edit/save
- **Note Save Validation**: POST handler validates target user is in same company

### 3) Localization
- Standard pattern. Uses `Shift`, `Trainee`, `Unknown` keys

### 4) UI & Design Inventory
- Date nav, view mode, users filter dropdown, JustMine toggle
- **Calendar Grid**: `ExcelCalendarTable` — users as rows, dates as columns
- **Cell Content**: Aggregated badges for shifts (with trainee suffix), chores, on-duty, vacation overlay, editable notes

### 5) Navigation Map
- Self-referencing navigation
- Reached via `/Calendar` landing page

### 6) Data Dependencies & Side Effects
- **Reads**: Company, Users (active/inactive filter), TimeOffRequests (approved, expanded to per-day), ShiftAssignments (with type names), Chores (with type names), OnDuties, UserDayNotes via `IUserDayNoteService`
- **Writes**: `OnPostSaveNoteAsync` — saves/deletes notes via `IUserDayNoteService`
  - Request: `{ UserId, Date, Note }`
  - Validates: grant check, company context, target user in same company
  - Success: `{ success: true }`, Failure: `{ success: false, error }` with appropriate status codes

### 7) Forms & Submissions
- Note save: AJAX POST with `SaveNoteRequest` body

### 8) Interesting Behaviors
- **Trainee Support**: Shift assignments shown for both primary user and trainee
- **Vacation Overlay**: Shows visual indicator on cells where user has approved time-off
- **Read-Only Grid**: Calendar assignments are always read-only; only notes are editable
- **Users Filter**: Can toggle between active/inactive users

### 9) Traceability
- **Services**: `IUserDayNoteService`, `IGrantService`, `ICompanyContext`
- **Inner Classes**: `SaveNoteRequest`

---

## 10. Chores/Calendar

### 1) Identity & Routing
- **Route**: `/Chores/Calendar` (`@page`)
- **Purpose**: Manager-facing chore management calendar with CRUD operations (create, cancel, replace shift with chore)
- **Query Params**: `year`, `month`, `message` (success message via redirect)

### 2) Access Control & Scope
- **Auth**: `[Authorize(Policy = "Grant:ManagerHomeAccess")]` — requires manager grant
- **CRUD Permissions**: All POST handlers check `IChoreService.CanUserManageChoresAsync` and `CanUserManageChoreForAssigneeAsync`
- **Scope**: Company-scoped via tenant filter (no IgnoreQueryFilters)

### 3) Localization
- Extends `LocalizedPageModel` base class
- Extensive localization keys for all error/success messages: `Chore_Error_FillRequiredFields`, `Chore_Error_TitleRequired`, `Chore_Error_TitleTooLong`, `Chore_Error_NotesTooLong`, `Chore_Error_InvalidAssignee`, `Chore_Error_DateTooFarFuture`, `Chore_Error_DateInPast`, `Chore_Error_NoPermissionCreate`, `Chore_Error_CannotAssignToUser`, `Chore_Error_ShiftConflictReplace`, `Chore_Error_ShiftConflict`, `Chore_Success_Created`, `Chore_Error_NoPermissionReplaceShifts`, `Chore_Success_ShiftReplaced`, `Chore_Error_InvalidId`, `Chore_Error_NoPermissionCancel`, `Chore_Error_NotFound`, `Chore_Success_Canceled`, etc.

### 4) UI & Design Inventory
- Month navigation, chore list grouped by date
- Create form: assignee dropdown, date picker, title input, notes textarea
- Conflict resolution dialog (TempData-driven) for shift/chore conflicts
- Cancel button per chore

### 5) Navigation Map
- Previous/Next month links
- Reached via admin navigation

### 6) Data Dependencies & Side Effects
- **Reads**: `IChoreService.GetChoresAsync`, `GetEligibleAssigneesAsync`
- **Writes**:
  - `OnPostCreateChoreAsync` — Creates chore with full validation (title length ≤200, notes ≤1000, assignee >0, date not past, not >2yr future). Sends notification + audit log
  - `OnPostReplaceShiftWithChoreAsync` — Replaces shift assignment with chore. Validates ShiftAssignmentId >0, title ≤200, notes ≤1000. Sends notification + audit log
  - `OnPostCancelChoreAsync` — Soft-cancels chore. Reads ChoreId from form manually (avoids binding conflicts). Sends notification + audit log
- **Notifications**: `INotificationService.CreateChoreAssignedNotificationAsync`, `CreateChoreCanceledNotificationAsync`
- **Audit**: `IAuditLogService.LogAsync` for all mutations with JSON details

### 7) Forms & Submissions
- **Create Chore**: `AssigneeId` (int), `ChoreDate` (DateOnly), `ChoreTitle` (string, max 200), `ChoreNotes` (string?, max 1000)
- **Replace Shift**: `ShiftAssignmentId` (int), `ChoreTitle`, `ChoreNotes`
- **Cancel Chore**: `ChoreId` (int, read from form manually)
- **Conflict Flow**: On shift conflict, stores data in TempData, shows dialog for user to confirm replacement

### 8) Interesting Behaviors
- **Shift Conflict Resolution**: If chore creation conflicts with shift, returns `SHIFT_CONFLICT` and enables conflict dialog to replace shift with chore
- **Manual Form Binding**: `OnPostCancelChoreAsync` reads `ChoreId` from `Request.Form` instead of model binding to avoid conflicts
- **Header Metrics**: Calculates total/unassigned/my chore counts
- **Security Logging**: Logs unauthorized cancel attempts with "SECURITY:" prefix

### 9) Traceability
- **Services**: `IChoreService`, `INotificationService`, `IAuditLogService`
- **Base Class**: `LocalizedPageModel`

---

## 11. Schedule/Index

### 1) Identity & Routing
- **Route**: `/Schedule` (`@page`)
- **Purpose**: Schedule landing/routing page — determines default view mode and redirects or renders accordingly
- **Query Params**: `view` (month|week|day), `mode` (calendar|table)

### 2) Access Control & Scope
- **Auth**: `[Authorize]`
- **Scope**: Resolves user context (name, admin status) for UI rendering

### 3) Localization
- None in code-behind; presumably in the cshtml template

### 4) UI & Design Inventory
- Minimal code-behind — sets `IsAdmin`, `UserName`, `DefaultView`, `DefaultMode`
- Likely serves as a dispatcher/container page

### 5) Navigation Map
- Likely provides links to Calendar/Month, Calendar/Week, Calendar/Day, Calendar/Table based on user's role
- Reached via main nav

### 6) Data Dependencies & Side Effects
- **Reads**: User claims (Name, NameIdentifier), `IGrantService.HasGrantAsync("AccessAdminNavigation")`
- **Writes**: None

### 7) Forms & Submissions
- None

### 8) Interesting Behaviors
- Very lightweight — essentially a routing shell that sets display flags
- `DefaultView` and `DefaultMode` parsed from query params with safe fallbacks

### 9) Traceability
- **Services**: `IGrantService`

---

## Cross-Cutting Notes for Calendar Section

### Common Patterns
1. **Scope-Switcher Integration**: Day/Week/Month all use `IScopeFilterService` for mine/company/molecule/area filtering
2. **IgnoreQueryFilters**: Used extensively with SECURITY-AUDITED comments; justified by validated scope filtering
3. **Metric Headers**: All calendar views show shift/chore/on-duty/pending counts + coverage %
4. **Quick-Add Forms**: Admin-only inline forms on Day/Week/Month for adding chores/on-duty
5. **ExcelCalendarTable**: Shared ViewComponent used by Shifts, Chores, OnCall, Overview calendars
6. **Date Validation**: `TryCreateValidDate()` with year bounds (1900-2100) and day clamping

### Inconsistencies
1. **Massive Code Duplication**: `LoadShiftsAsync`, `LoadChoresAsync`, `LoadOnDutiesAsync`, `CalculateHeaderMetrics`, `TryCreateValidDate`, `GetShiftIcon`, `GetOnDutyTypeInfo` are copy-pasted across Day/Week/Month models
2. **Localization Key Mismatch**: Month uses `OnDutyHakam`/`OnDutyLead` (no underscore); Day/Week use `OnDuty_Hakam`/`OnDuty_Lead` (with underscore)
3. **Mixed Localization Patterns**: Some pages use `@Localizer["key"]`, others use `<loc key="..." />` tag helper, sometimes both on the same page
4. **Table POST Auth Gap**: Calendar/Table's `EnsureShiftInstance` and `CreateShiftInstance` POST handlers rely solely on page-level `Grant:ManagerHomeAccess` without specific shift assignment grant checks (unlike `AssignEmployee` which has explicit checks)
