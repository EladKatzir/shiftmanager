# Batch 7 - Shared Components & Layout Audit

Audited: 2026-03-03
Files reviewed: 26 view files + 13 ViewComponent code-behind classes

---

## Table of Contents

1. [_Layout.cshtml](#1-_layoutcshtml)
2. [_ViewImports.cshtml](#2-_viewimportscshtml)
3. [_LocalizationScript.cshtml](#3-_localizationscriptcshtml)
4. [_ValidationMessage.cshtml](#4-_validationmessagecshtml)
5. [CalendarSkeleton](#5-calendarskeleton)
6. [ContextSwitcher](#6-contextswitcher)
7. [ErrorBanner](#7-errorbanner)
8. [ErrorToast](#8-errortoast)
9. [ExcelCalendarTable](#9-excelcalendartable)
10. [HierarchyTree](#10-hierarchytree)
11. [LanguageToggle](#11-languagetoggle)
12. [LoadingSkeleton](#12-loadingskeleton)
13. [LoadingSpinner](#13-loadingspinner)
14. [OnCallWidget](#14-oncallwidget)
15. [Pagination](#15-pagination)
16. [ScopeSwitcher](#16-scopeswitcher)
17. [ShowMyItemsToggle](#17-showmyitemstoggle)
18. [UnreadNotificationCount](#18-unreadnotificationcount)
19. [Layout Navigation Summary](#19-layout-navigation-summary)

---

## 1. _Layout.cshtml

**File:** `Pages/Shared/_Layout.cshtml` (1042 lines)

### Purpose
Master layout file for the entire application. Defines the HTML shell, sidebar navigation, header bar, bottom dock panel, command palette, and all global script/CSS loading.

### Services Injected
- `IHttpContextAccessor` - Access to HttpContext/User claims
- `IStringLocalizer<SharedResources>` - Localization
- `IViewAsModeService` - Manager "View As" mode detection
- `IFeatureFlagService` - Feature flag resolution for conditional navigation items

### Head Section
- Dynamic `<title>` based on user's first name and locale (Hebrew: "Ha'shifty shel {name}", English: "{name}'s shifty")
- `<html>` attributes: `lang`, `dir` (rtl/ltr), `data-theme="light"`, class `hebrew`/`english`
- CSS files: `site.css`, `print.css` (media=print), `rtl.css` (conditional on Hebrew), `shift-swap-game.css`, `language-edit-mode.css` (conditional on cookie)
- Lucide icons: `lucide.min.js` loaded synchronously
- `calendar-skeleton.js` loaded synchronously (early to show skeleton before data loads)
- Anti-forgery token script (inline, authenticated only): patches `window.fetch` to inject CSRF header on POST/PUT/DELETE/PATCH
- `_LocalizationScript` partial included via `@await Html.PartialAsync`
- Approximately 20+ deferred JS files loaded via `<script defer>`: `reduced-motion.js`, `toast-notifications.js`, `site.js`, `localization-api.js`, `localization-attributes.js`, `date-format.js`, `cache-management.js`, `session-check.js` (auth only), `hebrew-audit.js` (Hebrew only), `language-edit-mode.js` (conditional), `modal-focus.js`, `keyboard-nav.js`, `a11y-enhancements.js`, `mobile-nav.js`, `form-validation.js`, `api-client.js`, `error-states.js`, `partial-data.js`, `offline-handler.js`, `telemetry.js`, `error-boundary.js`, `lazy-loader.js`, `modal-loader.js`, `widget-persistence.js`, `calendar-print.js`, `friends-highlight.js`
- Inline scripts (synchronous, in `<head>`): sidebar collapsed state (FOUC prevention), widget collapsed state (FOUC prevention), sidebar CSS diagnostic, browser compatibility check (ES2020+ feature detection)

### Body Structure
1. **noscript banner** - Bilingual (Hebrew + English) warning for JS-disabled browsers
2. **Context-switch toast** - Shows TempData["ContextSwitchMessage"] as a temporary overlay
3. **Skip link** - `<a href="#main-content">` for keyboard navigation (WCAG 2.4.1)
4. **Page loader** - Brand loading animation with 150ms delay before showing; auto-dismisses on DOMContentLoaded; 5s fallback timeout
5. **SystemAlerts** ViewComponent - Admin-only critical alerts
6. **LanguageEditModeBanner** ViewComponent - Translation edit mode banner
7. **`<div class="app-shell">`** wrapping:
   - **Sidebar** (`<aside class="app-sidebar">`) - authenticated only
   - **Mobile nav overlay** + toggle (hamburger button)
   - **Main content area** (`<div class="app-main">`)

### Sidebar Structure
- **Brand section** - Logo image with SVG fallback + "SHIFTY" name + "ShiftManager" tagline
- **ContextSwitcher** ViewComponent (conditional on `EnableCompanySwitcher` feature flag)
- **Navigation** (`<nav class="app-sidebar-nav">`) - two major branches:
  - Admin navigation (users with `AccessAdminNavigation` grant)
  - Employee navigation (users WITHOUT `AccessAdminNavigation` grant)
- **Sidebar footer** - User avatar, name, role; user popup menu (Profile, Settings, Logout); Ctrl+K shortcut hint
- **Sidebar toggle button** (A-002) with `aria-expanded`, `aria-controls`

### Header Bar
- Page title from `ViewData["Title"]`
- **DecisionRibbon** ViewComponent (excluded from `/Owner` routes)
- Notifications link with **UnreadNotificationCount** ViewComponent
- **LanguageToggle** ViewComponent
- Theme toggle button
- Logout form (authenticated only)

### After Main Content
- "View As Manager" warning bar (conditional)
- Employee welcome section (conditional on `/Home` path + non-admin)
- **Bottom Dock Panel** - Contains **OnCallWidget** ViewComponent; gated by `WidgetsEnabled` feature flag; collapsible with localStorage persistence
- **Command Palette** - Hidden by default; triggered by Ctrl+K; search input + dynamic results

### Render Sections
- `@RenderSectionAsync("Styles", required: false)` - in `<head>`
- `@RenderSectionAsync("Scripts", required: false)` - before closing `</body>`
- `@RenderSectionAsync("PageScripts", required: false)` - after Scripts section

### Post-Body Scripts
- Lucide icon initialization (`lucide.createIcons()`) + htmx re-initialization
- Sidebar & mobile navigation scripts (A-002): collapse persistence via localStorage (`shifty_sidebar_collapsed`), Ctrl+B toggle shortcut, mobile overlay close on Escape, collapsible nav category headers with localStorage persistence
- Bottom dock toggle script: collapse persistence via localStorage (`shifty_bottom_dock_collapsed`), Escape key close

---

## 2. _ViewImports.cshtml

**File:** `Pages/_ViewImports.cshtml` (5 lines)

### Contents
```csharp
@addTagHelper *, Microsoft.AspNetCore.Mvc.TagHelpers
@addTagHelper *, ShiftManager
@using ShiftManager.Models
@using ShiftManager.Models.Support
@namespace ShiftManager.Pages
```

### Summary
- Registers **Microsoft built-in tag helpers** (`asp-for`, `asp-page`, `asp-action`, `asp-append-version`, etc.)
- Registers **ShiftManager custom tag helpers** (from the `ShiftManager` assembly): includes `<require-grant>`, `<loc>`, `<icon>`, `loc-aria-label`, `loc-title`, `loc-placeholder`, and other custom tag helpers
- Makes `ShiftManager.Models` and `ShiftManager.Models.Support` namespaces globally available to all Razor Pages (eliminates per-page `@using` statements for model types and enums like `MoleculeType`, `UserRole`, `MilitaryRank`)
- Sets the base namespace for generated Razor Page classes to `ShiftManager.Pages`

---

## 3. _LocalizationScript.cshtml

**File:** `Pages/Shared/_LocalizationScript.cshtml` (176 lines)

### Purpose
Generates a `window.AppLocalizer` JavaScript object containing all localization strings needed by client-side JavaScript code. This is the bridge between server-side `IStringLocalizer` and client-side JS.

### Injection
- `IStringLocalizer<SharedResources>` - Server-side localization service

### How It Works
Each key-value pair serializes the localized string via `@Html.Raw(System.Text.Json.JsonSerializer.Serialize(Localizer["Key"].Value))` to produce properly JSON-escaped strings.

### Localization Categories (173 keys total)
| Category | Keys | Consumers |
|----------|------|-----------|
| Common UI | Cancel, Save, Delete, Edit, Close, Confirm | Multiple JS files |
| Shift Management | CreateNewShift, Date, ShiftType, RequiredStaff, NumberOfPeopleNeeded, ShiftNameOptional, ShiftNamePlaceholder, CreateShift, PleaseSelectShiftType, InvalidDateData, FailedToCreateShift, ShiftCreatedSuccessfully, Person, People, ConfirmDeleteShift, AccessDenied, AccessDeniedMessage, Understood, NoShiftTypesAvailable, NoResultsFound, Recent, Error, Company, Required | `site.js` |
| Calendar Inline Edit | ChoreCreatedSuccessfully, ErrorCreatingChore, ConflictDetected, OnDutyCreatedSuccessfully, ErrorCreatingOnDuty, ItemDeletedSuccessfully, ErrorDeletingItem, PleaseSelectAssignee, PleaseEnterTitle, TitleMaxLengthExceeded | `calendar-inline-edit.js` |
| MyGroups | Time, ClickToView, ViewOnlyNoNavigation, AnErrorOccurred | `myteam.js` |
| Command Palette Navigation | Home, DashboardOverview, Calendar, MonthlySchedule, WeeklySchedule, DailySchedule, ManageRequests, ViewReports, ManageEmployees, ManageOrganizations, AssignDirectors, SystemSettings, ManageShiftDefinitions, ApprovedTimeOff, SystemActivityLog, TaskManagement, CurrentDutyRoster, ViewMyRequests, ViewTeamMembers, ViewGroupMembers, UpdateMyInformation, ViewNotifications | `site.js` |
| Error/Status Messages | Error_OperationFailed, Error_NetworkError, Error_Prefix, Error_FailedToDeleteShift | `site.js` |
| Nav Page Titles | Nav_Requests through Nav_Notifications (~30 keys) | `site.js` (command palette) |
| Roster Dock | Roster_Vacation, Roster_OnShift, Roster_HasChore, Roster_Available, Roster_FailedToAssign, Roster_AssignedSuccess, Roster_AssignmentFailed, Roster_Loading, Roster_FailedToLoad, Roster_NoEmployees | `roster-dock.js` |
| Modal Loader | FailedToLoadContent, LoadingFailedTitle | `modal-loader.js` |
| DateTime Formatting | DateTime_JustNow, DateTime_MinutesAgo, DateTime_HoursAgo, DateTime_Yesterday, DateTime_DaysAgo, DateTime_WeeksAgo, DateTime_Today, DateTime_Tomorrow | `date-format.js` |
| Calendar Fill Handle | FillHandle_DragToCopy, FillHandle_InvalidSource, FillHandle_Applying | `calendar-fill-handle.js` |
| Calendar Inline Edit Undo | InlineEdit_CouldNotUndo | `calendar-inline-edit.js` |
| Calendar Radar | Radar_Active, Radar, Radar_FailedToLoad, Radar_ConflictDetected, Radar_Understaffed, Radar_Overstaffed, Radar_Conflict, Radar_Needed, Radar_Extra, Radar_Shift, Radar_Date, Radar_Staffing | `calendar-radar.js` |
| Offline Handler | Offline_SavedOffline, Offline_Retry, Offline_NoPendingActions | `offline-handler.js` |

---

## 4. _ValidationMessage.cshtml

**File:** `Pages/Shared/_ValidationMessage.cshtml` (17 lines)

### 1) Identity
- **Name:** _ValidationMessage (Partial View, not a ViewComponent)
- **Invocation:** `@await Html.PartialAsync("_ValidationMessage", "PropertyName")` or `<partial name="_ValidationMessage" model="@("PropertyName")" />`
- **Implements:** B-008 (Accessible Validation Messages)

### 2) Purpose
Renders an accessible validation error message span for a given model property.

### 3) Parameters/Model
- `@model string` - The property name to validate (e.g., `"Username"`, `"Password"`)

### 4) UI Structure
```html
<span asp-validation-for="@Model" class="field-validation-error" aria-live="polite"></span>
```
- Uses `asp-validation-for` tag helper for server-side validation integration
- `aria-live="polite"` for screen reader announcements when validation state changes
- CSS class `field-validation-error` (styled via `components.css` with `::before` pseudo-element for warning icon)

### 5) Dependencies
- Microsoft built-in tag helpers (`asp-validation-for`)

### 6) Usage
- Currently only its own definition file references it. No other `.cshtml` files were found using it, suggesting it may be newly added or underutilized.

### 7) Interesting Behaviors
- Accessibility: `aria-live="polite"` ensures screen readers announce validation errors without interrupting current speech
- Warning icon applied via CSS `::before` pseudo-element (not inline)

---

## 5. CalendarSkeleton

### 1) Identity
- **Name:** CalendarSkeletonViewComponent
- **Code-behind:** `ViewComponents/CalendarSkeletonViewComponent.cs`
- **Views:** `Pages/Shared/Components/CalendarSkeleton/Default.cshtml` + 4 sub-partials (`_DaySkeleton`, `_MonthSkeleton`, `_WeekSkeleton`, `_TableSkeleton`)
- **Invocation:** `@await Component.InvokeAsync("CalendarSkeleton", new { viewType = "month", itemCount = 5, cellCount = 35 })`
- **Implements:** B-007 (Skeleton Loading for Calendars)

### 2) Purpose
Renders a shimmer-animation skeleton placeholder while calendar data loads. Matches the layout of each calendar view type so the transition from skeleton to real content feels seamless.

### 3) Parameters/Model
`CalendarSkeletonModel`:
| Property | Type | Default | Description |
|----------|------|---------|-------------|
| ViewType | string | "month" | Calendar view type: "month", "week", "day", "table" |
| ItemCount | int | 5 | Number of item placeholders (primarily for day view) |
| CellCount | int | 35 | Number of cells (for month view: 5 weeks x 7 days) |
| WeekColumns | int (computed) | 7 | Always 7 for week view |
| TableRows | int (computed) | 4 | Number of table rows for table skeleton |
| TableDateColumns | int (computed) | 7 | Number of date columns for table skeleton |

### 4) UI Structure
**Default.cshtml (Router):**
- Container `<div class="calendar-skeleton">` with `role="status"`, `aria-busy="true"`, `aria-live="polite"`, `aria-label="Loading"`
- Switches on `Model.ViewType.ToLower()` to render the appropriate sub-partial
- Hidden `<span class="visually-hidden">` with "Loading" text for screen readers

**_MonthSkeleton.cshtml:**
- Skeleton header (title + navigation arrows)
- 4 skeleton metric items (icon + value + label)
- 3 skeleton view-switcher buttons
- Quick actions (label + 2 buttons)
- 7-column weekday header
- Grid of `Model.CellCount` cells with 1-3 shimmer items each (varying by cell index)

**_WeekSkeleton.cshtml:**
- Same header/metrics/view-switcher/quick-actions pattern
- 7-column grid, each with day name, date, and 1-3 shimmer items (varying by column index)

**_DaySkeleton.cshtml:**
- Same header/metrics/view-switcher/quick-actions pattern
- `Model.ItemCount` day items, each with icon, title, 2-3 detail rows, and an action button
- Alternating items show an extra detail row for visual variety

**_TableSkeleton.cshtml:**
- Same header/metrics/view-switcher pattern
- HTML `<table>` with header row (shift name column + `Model.TableDateColumns` date headers)
- `Model.TableRows` body rows, each with shift name + time in first column, then date columns with 1-2 assignment slot placeholders

### 5) Dependencies
- `IStringLocalizer<SharedResources>` (injected in Default.cshtml for "Loading" text)
- CSS class `calendar-skeleton-shimmer` for animation effect

### 6) Usage
- `Pages/Calendar/Table.cshtml`
- `Pages/Calendar/Week.cshtml`
- `Pages/Calendar/Month.cshtml`
- `Pages/Calendar/Day.cshtml`

### 7) Interesting Behaviors
- Accessibility: Full ARIA status region (`role="status"`, `aria-busy`, `aria-live="polite"`) + visually hidden loading text
- Visual variety: item counts within cells/columns vary based on modular arithmetic to avoid a monotonous repeating pattern
- Defaults to month skeleton if an unknown `ViewType` is passed

---

## 6. ContextSwitcher

### 1) Identity
- **Name:** ContextSwitcherViewComponent
- **Code-behind:** `ViewComponents/ContextSwitcherViewComponent.cs`
- **View:** `Pages/Shared/Components/ContextSwitcher/Default.cshtml`
- **Invocation:** `@await Component.InvokeAsync("ContextSwitcher")`

### 2) Purpose
Allows users to switch between organizational contexts (companies). Adapts its display based on the number of available contexts: error state, empty state, single context (non-interactive), or full multi-context switcher with search, keyboard navigation, and grouped options.

### 3) Parameters/Model
`ContextSwitcherViewModel`:
| Property | Type | Description |
|----------|------|-------------|
| CurrentContext | ContextOption? | Currently selected context |
| ContextGroups | List\<ContextGroup\> | Grouped context options (by molecule) |
| ShowSwitcher | bool | Whether to show the full interactive switcher |
| HasSingleContext | bool | Only one context available (non-interactive display) |
| HasNoContexts | bool | No contexts available (empty state) |
| HasManyContexts | bool | 50+ contexts (shows count badge) |
| TotalContextCount | int | Total number of contexts |
| CurrentContextUnavailable | bool | Current context was deleted/unavailable |
| HasError | bool | Error loading data |
| ErrorMessage | string? | Localized error message |

`ContextOption`: Id, Name, DisplayName, FullName, Type, ParentName, MoleculeId, NeedsTruncation

`ContextGroup`: GroupName, GroupType, Options

### 4) UI Structure
**4 display states:**
1. **Error state** - Error icon + message + reload retry button
2. **No contexts** - Building emoji + "no contexts" message
3. **Single context** - Static display with pin emoji + context name
4. **Multi-context** - Full interactive:
   - "Currently viewing" label
   - Warning banner if current context became unavailable (auto-dismiss after 10s)
   - Trigger button showing current context name + chevron
   - Dropdown with: search input (combobox with `aria-autocomplete`), loading state (hidden by default), grouped option buttons, "no results" message

**Localization keys used:** `ContextSwitcher_LoadError`, `ContextSwitcher_Retry`, `ContextSwitcher_NoContextsAvailable`, `ContextSwitcher_CurrentlyViewing`, `ContextSwitcher_SelectContext`, `ContextSwitcher_Loading`, `ContextSwitcher_SearchPlaceholder`, `ContextSwitcher_SearchLabel`, `ContextSwitcher_NoResults`, `ContextSwitcher_ContextUnavailable`

### 5) Dependencies
**Code-behind injections:**
- `IStringLocalizer<SharedResources>`
- `AppDbContext` - Direct DB access for molecules/companies
- `ITenantResolver` - Get current tenant (company) ID
- `IGrantService` - `GetAccessibleCompanyIdsForGrantAsync` for Director scope resolution
- `ILogger<ContextSwitcherViewComponent>`

**Security note:** Uses `IgnoreQueryFilters()` for Director company resolution (intentional cross-tenant query with explicit `Where` clause filtering by `directorCompanyIds`).

### 6) Usage
- `Pages/Shared/_Layout.cshtml` (conditionally, behind `EnableCompanySwitcher` feature flag)

### 7) Interesting Behaviors
- **Context selection submits a POST form** to `/Owner/SelectCompany` with anti-forgery token (D-03 compliance: company switching is POST-only)
- Saves last context to `localStorage` (`shifty_last_context`)
- **Keyboard navigation:** Arrow keys, Enter to select, Escape to close, Home/End keys, Tab closes dropdown
- **Debounced search** (150ms) with group-level visibility toggling
- **Name truncation** at 30 characters with tooltip for full name
- **Role-based context determination:** Owner sees all molecules/companies, Director sees grant-scoped companies, Employee sees own company only
- IIFE-wrapped JavaScript prevents global namespace pollution

---

## 7. ErrorBanner

### 1) Identity
- **Name:** ErrorBannerViewComponent
- **Code-behind:** `ViewComponents/ErrorBannerViewComponent.cs`
- **View:** `Pages/Shared/Components/ErrorBanner/Default.cshtml`
- **Invocation:** `@await Component.InvokeAsync("ErrorBanner", new { level = "error", messageKey = "...", dismissible = true, showRetry = false })`

### 2) Purpose
Displays a persistent, page-level error/warning/info banner with optional dismiss and retry buttons.

### 3) Parameters/Model
`ErrorBannerModel`:
| Property | Type | Default | Description |
|----------|------|---------|-------------|
| Level | string | "error" | Severity: "error", "warning", "info" |
| MessageKey | string | "" | Localization key for message |
| FallbackMessage | string | "" | Fallback if localization key not provided |
| Dismissible | bool | true | Can be dismissed by user |
| ShowRetry | bool | false | Show retry button |
| RetryAction | string? | null | JS action for retry (defaults to `location.reload()`) |
| TitleKey | string? | null | Optional localization key for title |
| FallbackTitle | string? | null | Optional fallback title |

### 4) UI Structure
- Container `<div class="error-banner">` with `role="alert"`, `aria-live="polite"`, level-specific CSS class
- Icon via `<icon>` tag helper (error/warning/info)
- Title (optional, localized or fallback)
- Message (localized or fallback)
- Actions: retry button (optional), close/dismiss button (optional, removes element via DOM)
- Unique `id` per banner instance (GUID-based)

### 5) Dependencies
- `IStringLocalizer<SharedResources>`
- `<icon>` custom tag helper
- `<loc>` custom tag helper

### 6) Usage
- Only its own view definition file. No pages currently invoke this component, suggesting it is available as a reusable utility but not yet actively called. (Pages likely use the toast system or TempData for errors.)

### 7) Interesting Behaviors
- Dismiss button uses `document.getElementById('@bannerId').remove()` for instant DOM removal
- Retry button executes arbitrary JS via `Model.RetryAction` (potential XSS vector if untrusted input reaches this parameter -- mitigated by server-side-only invocation)

---

## 8. ErrorToast

### 1) Identity
- **Name:** ErrorToastViewComponent
- **Code-behind:** `ViewComponents/ErrorToastViewComponent.cs`
- **View:** `Pages/Shared/Components/ErrorToast/Default.cshtml`
- **Invocation:** `@await Component.InvokeAsync("ErrorToast", new { level = "error", messageKey = "...", autoDismissMs = 5000 })`

### 2) Purpose
Displays a transient toast notification that auto-dismisses after a configurable delay. Supports error, warning, info, and success levels.

### 3) Parameters/Model
`ErrorToastModel`:
| Property | Type | Default | Description |
|----------|------|---------|-------------|
| Level | string | "error" | Severity: "error", "warning", "info", "success" |
| MessageKey | string | "" | Localization key |
| FallbackMessage | string | "" | Fallback text |
| TitleKey | string? | null | Optional title localization key |
| FallbackTitle | string? | null | Optional fallback title |
| AutoDismissMs | int | 5000 | Auto-dismiss delay (0 = never) |
| ShowCloseButton | bool | true | Show manual close button |

### 4) UI Structure
- `<div class="toast">` with `role="alert"`, `aria-live="assertive"`, level-specific CSS class
- Icon via `<icon>` tag helper (error/warning/info/check_circle)
- Title (optional) + message
- Close button (optional)
- Auto-dismiss via IIFE inline script: adds `toast--dismissing` class, removes element after 300ms animation

### 5) Dependencies
- `IStringLocalizer<SharedResources>`
- `<icon>` and `<loc>` custom tag helpers

### 6) Usage
- Only its own view definition file. Similar to ErrorBanner, this is a reusable utility not currently directly invoked by pages (the JS-based `showToast()` function in `toast-notifications.js` likely handles most toasts).

### 7) Interesting Behaviors
- Uses `aria-live="assertive"` (interrupts current screen reader speech) vs. ErrorBanner's `aria-live="polite"`
- Auto-dismiss animation: `toast--dismissing` CSS class triggers fade-out, then element is removed after 300ms
- Each toast gets a unique GUID-based ID

---

## 9. ExcelCalendarTable

### 1) Identity
- **Name:** ExcelCalendarTableViewComponent
- **Code-behind:** `ViewComponents/ExcelCalendarTableViewComponent.cs`
- **View:** `Pages/Shared/Components/ExcelCalendarTable/Default.cshtml` + `_CalendarRow.cshtml`
- **Invocation:** `@await Component.InvokeAsync("ExcelCalendarTable", new { model = viewModel })`

### 2) Purpose
Renders an Excel-style grid calendar table with date columns, employee/shift rows, and assignment cells. Supports grouping, collapsible groups, read-only mode, and multiple calendar types.

### 3) Parameters/Model
`ExcelCalendarTableViewModel`:
| Property | Type | Default | Description |
|----------|------|---------|-------------|
| StartDate | DateOnly | - | First date column |
| EndDate | DateOnly | - | Last date column |
| ViewMode | string | "week" | Display density: "week", "2weeks", "month" |
| IsReadOnly | bool | false | Disable editing |
| CalendarType | string | "shifts" | Type: "shifts", "chores", "oncall", "overview" |
| Rows | List\<ExcelCalendarRow\> | [] | Row data |
| Groups | List\<ExcelCalendarGroup\>? | null | Optional grouping |

`ExcelCalendarRow`: Id, Label, Color, GroupId, CompanyName, Cells (Dictionary\<DateOnly, ExcelCalendarCell\>)

`ExcelCalendarCell`: Assignments, Overlay, Note, Capacity, DefaultCapacity

`ExcelCalendarAssignment`: Id, Name, Role, IsTrainee, UserId

`ExcelCalendarOverlay`: HasVacation, HasChore, HasOnDuty, OtherItems

`ExcelCalendarGroup`: Id, Name, IsCollapsed, SortOrder

### 4) UI Structure
**Default.cshtml:**
- Read-only banner (conditional)
- `<div class="excel-calendar">` with data attributes for type, dates, readonly
- `<table>` with `role="grid"`:
  - Header row: corner cell + date columns (Hebrew day names hardcoded: "ראשון", "שני", etc.; short forms: "א׳", "ב׳", etc.)
  - Today highlighting on header cells
  - Weekend highlighting (Friday/Saturday) in month view
  - Groups: collapsible group headers with chevron toggle, drag-to-reorder grip, editable group name
  - Rows rendered via `_CalendarRow.cshtml` partial

**_CalendarRow.cshtml:**
- `<tr>` with row-level color via CSS custom property `--row-color`
- Row label cell with optional company badge
- Per-day cells: add button (non-readonly), assignments list, trainee badge, notes, overlay badges (vacation, chore, on-duty)
- Each cell has `role="gridcell"`, `tabindex="0"`, data attributes for row-id and date

### 5) Dependencies
- `IStringLocalizer<SharedResources>`
- Localization keys: `Calendar_ReadOnlyMode`, `DragToReorder`, `AddAssignment`, `Trainee`, `Vacation`, `Chore`, `OnDuty`

### 6) Usage
- `Pages/Calendar/Shifts.cshtml`
- `Pages/Calendar/Chores.cshtml`
- `Pages/Calendar/OnCall.cshtml`
- `Pages/Calendar/Overview.cshtml`

### 7) Interesting Behaviors
- **Hebrew day names are hardcoded** in the view (not pulled from localization) -- both full and abbreviated forms
- Uses `@functions` block to define `ExcelCalendarRowViewModel` inline in the view file
- Row model uses `dynamic` for the partial view model in `_CalendarRow.cshtml`
- Group collapse state propagated to row visibility via inline `display: none` style
- Compact mode (`--compact` CSS class) applied when `ViewMode == "month"`

---

## 10. HierarchyTree

### 1) Identity
- **Name:** HierarchyTreeViewComponent
- **Code-behind:** `ViewComponents/HierarchyTreeViewComponent.cs`
- **View:** `Pages/Shared/Components/HierarchyTree/Default.cshtml` + `_ProjectNode.cshtml` + `_AreaNode.cshtml` + `_MoleculeNode.cshtml`
- **Invocation:** `@await Component.InvokeAsync("HierarchyTree", new { mode = "full", expandedByDefault = true })`

### 2) Purpose
Interactive tree visualization of the organizational hierarchy: Project > Area > Molecule > Company/Department. Supports expand/collapse, inline editing, context menu, drag-and-drop reordering via SortableJS, and node CRUD operations via `/Api/Hierarchy/*` endpoints.

### 3) Parameters/Model
`HierarchyTreeViewModel`:
| Property | Type | Default | Description |
|----------|------|---------|-------------|
| Tree | List\<HierarchyProjectNode\> | [] | Root-level project nodes |
| Mode | string | "full" | "full" or "readonly" |
| ExpandedByDefault | bool | true | Initial expand state |
| CanEdit | bool | false | Grant-based: EditHierarchy |
| CanReorder | bool | false | Grant-based: ReorderHierarchy |
| CanDelete | bool | false | Grant-based: DeleteHierarchy |
| CanAddChild | bool | false | Grant-based: CreateHierarchy |

Node types: `HierarchyProjectNode`, `HierarchyAreaNode`, `HierarchyMoleculeNode`, `HierarchyCompanyNode`, `HierarchyDepartmentNode`

`HierarchyNodeViewModel` - Used for partials, carries one node plus capability flags.

### 4) UI Structure
**Default.cshtml:**
- Toolbar: Expand All, Collapse All, Add Project (grant-controlled)
- Tree container (`role="tree"`)
- Context menu (hidden, `role="menu"`): Edit, Add Child, Move To, Delete
- Inline edit input (hidden): text input + save/cancel buttons
- Move modal (hidden): destination selector with target list

**_ProjectNode / _AreaNode / _MoleculeNode:**
- Tree nodes (`role="treeitem"`) with badges (P/A/M/C/D) color-coded by type
- Toggle button for expand/collapse (triangle icon rotates 90deg)
- Drag handle (visible on hover)
- Label, child count, type badge (Molecule: Workforce/Tech/Helper), inactive status, menu button
- Children container (`role="group"`) with recursive partial rendering

**Molecule node inlines Company and Department children directly** (not separate partials).

### 5) Dependencies
**Code-behind:**
- `IStringLocalizer<SharedResources>`
- `AppDbContext` - Loads all Projects, Areas, Molecules, Companies, Departments with `IgnoreQueryFilters()` (SECURITY-AUDITED comment present)
- `IGrantService` - Checks 4 grants: EditHierarchy, ReorderHierarchy, DeleteHierarchy, CreateHierarchy
- `ILogger`

**Client-side:**
- `SortableJS` library (`~/lib/sortablejs/Sortable.min.js`)
- API endpoints: `/Api/Hierarchy/Rename`, `/Api/Hierarchy/Reorder`, `/Api/Hierarchy/Create`, `/Api/Hierarchy/Delete`, `/Api/Hierarchy/Move`, `/Api/Hierarchy/MoveTargets`

**Localization keys used:** ExpandAll, CollapseAll, AddProject, HierarchyTree, NoProjectsYet, CreateFirstProject, Edit, AddChild, MoveTo, Delete, Save, Cancel, NameRequired, ErrorSaving, ErrorReordering, EnterName, ErrorCreating, EnterProjectName, ConfirmDelete, ErrorDeleting, NoMoveTargets, ErrorMoving, SelectDestination, Areas, Molecules, Companies, Departments, Inactive, Workforce, Tech, Helper, Project

### 6) Usage
- `Pages/Admin/Organization/Index.cshtml`
- `Pages/Admin/Organization/Hierarchy/Index.cshtml`

### 7) Interesting Behaviors
- **Inline styles block** (~500 lines of CSS) embedded at the bottom of `Default.cshtml` -- not in an external CSS file
- Uses `@@media` (escaped `@` for Razor) for responsive breakpoints
- `IgnoreQueryFilters()` used to load full cross-tenant hierarchy (audited as safe since component only appears on grant-protected admin pages)
- Double-click on node triggers inline edit
- Context menu positions itself to stay within viewport bounds
- All API calls include anti-forgery token via `RequestVerificationToken` header
- SortableJS configured with `ghostClass`, `chosenClass`, `dragClass` for drag-drop visual feedback
- `window.HierarchyTree` namespace exposed globally for toolbar button onclick handlers
- Reorder failure reverts DOM changes automatically

---

## 11. LanguageToggle

### 1) Identity
- **Name:** LanguageToggleViewComponent
- **Code-behind:** `ViewComponents/LanguageToggleViewComponent.cs`
- **View:** `Pages/Shared/Components/LanguageToggle/Default.cshtml`
- **Invocation:** `@await Component.InvokeAsync("LanguageToggle")`

### 2) Purpose
Provides a button to switch between the default and alternate languages (typically English and Hebrew). Uses company-specific language settings.

### 3) Parameters/Model
`LanguageToggleViewModel`:
| Property | Type | Default | Description |
|----------|------|---------|-------------|
| CurrentLanguage | string | "en-US" | Current UI culture |
| DefaultCulture | string | "en-US" | Company's default language |
| AlternateCulture | string | "he-IL" | Company's alternate language |
| IsHebrew | bool | false | Whether current language is Hebrew |

### 4) UI Structure
- Container `<div class="language-toggle">`
- Button with globe emoji icon + language text ("En" or "עב")
- `loc-aria-label="Language"` and `loc-title="Language"` for accessibility
- Inline `<script>` defining `toggleLanguage()` function

### 5) Dependencies
**Code-behind:**
- `IStringLocalizer<SharedResources>`
- `ILanguageManagementService` - Gets company language settings
- `ITenantResolver` - Gets current company ID

### 6) Usage
- `Pages/Shared/_Layout.cshtml` (in header bar)
- `Pages/Auth/Login.cshtml` (on login page)

### 7) Interesting Behaviors
- Language switch works by setting the `.AspNetCore.Culture` cookie and reloading the page
- Company-specific language settings: fetches `DefaultCulture`/`AlternateCulture` from `ILanguageManagementService`; falls back to en-US / he-IL on error
- `toggleLanguage()` is a **global function** (not IIFE-wrapped) -- potential namespace collision risk

---

## 12. LoadingSkeleton

### 1) Identity
- **Name:** LoadingSkeletonViewComponent
- **Code-behind:** `ViewComponents/LoadingSkeletonViewComponent.cs`
- **View:** `Pages/Shared/Components/LoadingSkeleton/Default.cshtml`
- **Invocation:** `@await Component.InvokeAsync("LoadingSkeleton", new { variant = "text", count = 3, width = "80%" })`
- **Implements:** B-003 (Loading States)

### 2) Purpose
Renders generic skeleton loading placeholders for non-calendar content (text lines, headings, avatars, buttons, cards, rows).

### 3) Parameters/Model
`LoadingSkeletonModel`:
| Property | Type | Default | Description |
|----------|------|---------|-------------|
| Variant | string | "text" | Shape: "text", "text-sm", "heading", "avatar", "btn", "card", "row" |
| Count | int | 1 | Number of skeleton items |
| Width | string? | null | Custom CSS width |
| Height | string? | null | Custom CSS height |

### 4) UI Structure
- Container `<div class="skeleton-container">` with `role="status"`, `aria-busy="true"`, `aria-label="Loading"`
- Renders `Model.Count` skeleton divs with variant-specific CSS class
- Custom width/height applied as inline style when provided
- Hidden `<span class="visually-hidden">Loading</span>` for screen readers

### 5) Dependencies
- `IStringLocalizer<SharedResources>` (for "Loading" text)

### 6) Usage
- Only its own definition file. Available as a utility but no pages currently invoke it directly.

### 7) Interesting Behaviors
- Uses `@Html.Raw(customStyle)` to inject inline styles from model properties -- safe since width/height come from server-side code, not user input
- Each skeleton item is `aria-hidden="true"` (decorative); the container provides the accessibility semantics

---

## 13. LoadingSpinner

### 1) Identity
- **Name:** LoadingSpinnerViewComponent
- **Code-behind:** `ViewComponents/LoadingSpinnerViewComponent.cs`
- **View:** `Pages/Shared/Components/LoadingSpinner/Default.cshtml`
- **Invocation:** `@await Component.InvokeAsync("LoadingSpinner", new { size = "large", label = "Loading data...", showLabel = true })`
- **Implements:** B-003 (Loading States)

### 2) Purpose
Renders an animated loading spinner with an optional text label.

### 3) Parameters/Model
`LoadingSpinnerModel`:
| Property | Type | Default | Description |
|----------|------|---------|-------------|
| Size | string | "medium" | Size: "small", "medium", "large" |
| Label | string? | null | Custom label text (uses localized "Loading" if null) |
| ShowLabel | bool | true | Whether to show visible label |

### 4) UI Structure
- Container `<div class="loading-spinner">` with `role="status"`, `aria-live="polite"`, size-specific CSS class
- Animated spinner div (`aria-hidden="true"`)
- Visible label `<span>` (conditional on `ShowLabel`)
- Hidden `<span class="visually-hidden">` always present for screen readers

### 5) Dependencies
- `IStringLocalizer<SharedResources>`
- `<loc>` custom tag helper

### 6) Usage
- Only its own definition file. Available as a utility.

### 7) Interesting Behaviors
- Always provides screen reader text via `visually-hidden` span, even when `ShowLabel` is false
- Uses `<loc key="Loading" />` tag helper for the localized loading text

---

## 14. OnCallWidget

### 1) Identity
- **Name:** OnCallWidgetViewComponent
- **Code-behind:** `ViewComponents/OnCallWidgetViewComponent.cs`
- **View:** `Pages/Shared/Components/OnCallWidget/Default.cshtml`
- **Invocation:** `@await Component.InvokeAsync("OnCallWidget", new { showInSidebar = false })`

### 2) Purpose
Displays quick-info contacts (on-call officers, friends, company contacts) and office phone numbers. Collapsible widget with call-to-action links.

### 3) Parameters/Model
**Invoke parameter:** `showInSidebar` (bool, default true)

`OnCallWidgetViewModel`:
| Property | Type | Description |
|----------|------|-------------|
| Contacts | List\<OnCallContact\> | Contact list |
| OfficeNumbers | List\<OfficeNumber\> | Office phone numbers |
| IsCollapsed | bool | Initial collapse state |
| ShowOfficeNumbers | bool | Whether to show office section |
| ShowInSidebar | bool | Sidebar vs. bottom dock display mode |
| HasContacts | bool | Whether contacts exist |
| HasOfficeNumbers | bool | Whether office numbers exist |

`OnCallContact`: UserId, Name, Role, PhoneNumber, AvatarInitial, ContactType (Hakam/CompanyOnCall/Friend), CompanyName, Rank (MilitaryRank)

### 4) UI Structure
- Widget container with collapse toggle header (`role="button"`, `aria-expanded`)
- Contact badge count in header
- Contact list: avatar initial, role, name with military rank badge, phone number with `dir="ltr"`, call action link (`tel:` href)
- Empty state: icon circle + empty message + hint text
- Nested "Office Numbers" sub-widget (also collapsible): label + phone link for each number
- Comment notes widget persistence handled by `widget-persistence.js`

**Localization keys:** Widget_QuickInfo, Widget_OnCallCall, Widget_OnCallEmpty, Widget_OnCallEmptyHint, Widget_OfficeNumbers

### 5) Dependencies
**Code-behind:**
- `IStringLocalizer<SharedResources>`
- `IWidgetService` - `BuildOnCallWidgetAsync()`, `GetOfficeNumbersAsync()`
- `ITenantResolver` - Current company ID

**View:**
- `<icon>` tag helper (phone, chevron-down, user-x)
- `<loc>` tag helper
- Military rank extension methods: `GetAbbreviation()`, `GetBadgeClass()`, `GetDisplayName()`

### 6) Usage
- `Pages/Shared/_Layout.cshtml` (in bottom dock, `showInSidebar = false`, gated by `WidgetsEnabled` feature flag)

### 7) Interesting Behaviors
- Phone numbers rendered with `dir="ltr"` to prevent RTL reversal of digits
- Military rank displayed as a compact badge with abbreviation
- Maps `WidgetService.ContactType` enum to ViewComponent's own `OnCallContactType` enum (decoupling layer)
- Returns `Content(string.Empty)` for unauthenticated users (graceful no-op)

---

## 15. Pagination

### 1) Identity
- **Name:** PaginationViewComponent
- **Code-behind:** `ViewComponents/PaginationViewComponent.cs`
- **View:** `Pages/Shared/Components/Pagination/Default.cshtml`
- **Invocation:** `@await Component.InvokeAsync("Pagination", new { currentPage = 1, totalPages = 10, totalItems = 100, pageSize = 10 })`
- **Implements:** B-009 (Table Pagination Controls)

### 2) Purpose
Renders pagination controls with page numbers, prev/next navigation, items-per-page selector, and "Showing X-Y of Z" info.

### 3) Parameters/Model
`PaginationModel`:
| Property | Type | Default | Description |
|----------|------|---------|-------------|
| CurrentPage | int | - | Current page (1-based) |
| TotalPages | int | - | Total number of pages |
| TotalItems | int | - | Total item count |
| PageSize | int | - | Items per page |
| BaseUrl | string? | current path | URL base for pagination links |
| PageSizeOptions | int[]? | [10,25,50,100] | Page size dropdown options |
| QueryStringKey | string? | "page" | Query param name for page number |

Computed properties: `StartItem`, `EndItem`, `HasPreviousPage`, `HasNextPage`

Methods: `GetPageItems()` (generates page numbers with ellipsis), `GetPageUrl(int)`, `GetPageSizeUrl(int)`

### 4) UI Structure
- `<nav class="pagination">` with `role="navigation"`, localized `aria-label`
- Info section: "Showing X-Y of Z" with `aria-live="polite"`
- Page controls: Prev/Next buttons (SVG chevron icons, disabled at boundaries), page number links with ellipsis, `aria-current="page"` on active page
- Per-page selector: `<select>` with `onchange` navigation, visually-hidden label, "per page" text
- Prev/Next buttons include `visually-hidden` text and are disabled with `aria-disabled="true"`, `tabindex="-1"`

**Localization keys:** Pagination_Label, Pagination_Showing, Pagination_NoItems, Pagination_Previous, Pagination_Next, Pagination_GoToPage, Pagination_ItemsPerPage, Pagination_PerPage

### 5) Dependencies
- `IStringLocalizer<SharedResources>`
- Preserves existing query string parameters (excluding page/pageSize)

### 6) Usage
- `Pages/Calendar/Table.cshtml`
- `Pages/Owner/Hub/AuditSearch.cshtml`
- `Pages/Admin/AuditLog.cshtml`
- `Pages/Admin/Users.cshtml`

### 7) Interesting Behaviors
- **Ellipsis logic:** Always shows first page, last page, and current +/- 1; gaps > 1 get ellipsis
- Page size change resets to page 1
- SVG icons inline (no external icon dependency for pagination arrows)
- Only renders when `TotalPages > 0`

---

## 16. ScopeSwitcher

### 1) Identity
- **Name:** ScopeSwitcherViewComponent
- **Code-behind:** `ViewComponents/ScopeSwitcherViewComponent.cs`
- **View:** `Pages/Shared/Components/ScopeSwitcher/Default.cshtml`
- **Invocation:** `@await Component.InvokeAsync("ScopeSwitcher", new { currentScope = "company", calendarType = "shifts" })`

### 2) Purpose
Allows users to switch the scope of calendar views between "My Items", "My Company", "Full Molecule", and "Full Area" based on their granted permissions.

### 3) Parameters/Model
**Invoke parameters:** `currentScope` (string?), `calendarType` (string, default "shifts")

`ScopeSwitcherViewModel`:
| Property | Type | Description |
|----------|------|-------------|
| Scopes | List\<ScopeOption\> | Available scopes |
| CurrentScope | string | Currently active scope key |
| CalendarType | string | Calendar context (shifts/chores/etc.) |

`ScopeOption`: Key, Label, Icon, IsAvailable, IsActive

### 4) UI Structure
- Container `<div class="scope-switcher">` with `role="region"`, `aria-label`
- Label: "View:" text
- Button group (`role="tablist"`) with scope buttons (`role="tab"`) using roving tabindex pattern
- Screen reader announcer (`aria-live="polite"`, visually hidden)
- Inline `<script>` (IIFE) for:
  - Scope preference persistence (localStorage + cookie, 30 days)
  - Auto-restore saved scope on page load
  - Keyboard navigation (Arrow keys, Home, End, Enter, Space)
  - URL-based scope switching via query parameter

**Localization keys:** ScopeSwitcher_AriaLabel, ScopeSwitcher_ViewLabel, ScopeSwitcher_MineOnly, ScopeSwitcher_MyCompany, ScopeSwitcher_FullMolecule, ScopeSwitcher_FullArea

### 5) Dependencies
**Code-behind:**
- `IStringLocalizer<SharedResources>`
- `AppDbContext`
- `IGrantService` - Checks `View{CalendarType}Molecule` and `View{CalendarType}Area` grants

**Defines:** `StringExtensions.ToTitleCase()` extension method (in same file)

### 6) Usage
- `Pages/Calendar/Month.cshtml`
- `Pages/Calendar/Week.cshtml`
- `Pages/Calendar/Day.cshtml`
- `Pages/Api/ScopeSwitcher.cshtml`

### 7) Interesting Behaviors
- **Scope resolution priority:** URL query string > Cookie > Parameter > Default ("company")
- **Grant-based scope availability:** Molecule/Area scopes only shown if user has the corresponding view grant (e.g., `ViewShiftsMolecule`, `ViewChoresArea`)
- Scope preference stored in both localStorage (key: `shifty_scope_{calendarType}`) and cookie (key: `ShiftyScope_{calendarType}`, 30 days, SameSite=Lax)
- Falls back to first available scope if the saved/requested scope is not available for the user
- Loading state class added to button during scope change navigation

---

## 17. ShowMyItemsToggle

### 1) Identity
- **Name:** ShowMyItemsToggleViewComponent
- **Code-behind:** `ViewComponents/ShowMyItemsToggleViewComponent.cs`
- **View:** `Pages/Shared/Components/ShowMyItemsToggle/Default.cshtml`
- **Invocation:** `@await Component.InvokeAsync("ShowMyItemsToggle")`
- **Implements:** Phase 20 + 3B

### 2) Purpose
Toggle button for "Show My Items Only" filtering on calendar views.

### 3) Parameters/Model
`ShowMyItemsToggleViewModel`:
| Property | Type | Description |
|----------|------|-------------|
| ShowMyItemsOnly | bool | Current filter state |

### 4) UI Structure
- Container `<div class="show-my-items-toggle">`
- Button with `active` class when filtering is on
- Icon: person emoji when filtering, group emoji when showing all
- Two text labels: primary ("My Items Only" / "Showing All") + secondary hint ("Click to show all" / "Click to filter mine")
- Dynamic `aria-label` and `title` change based on state

**Localization keys:** Calendar_ToggleShowAll_Tooltip, Calendar_ToggleMyItems_Tooltip, Calendar_MyItemsOnly, Calendar_ShowingAll, Calendar_ClickToShowAll, Calendar_ClickToFilterMine

### 5) Dependencies
**Code-behind:**
- `IStringLocalizer<SharedResources>`
- `IUserPreferenceService` - `GetShowMyItemsOnly()` to read current preference

**View:**
- `toggleShowMyItems()` global function (not IIFE-wrapped)

### 6) Usage
- `Pages/Calendar/Month.cshtml`
- `Pages/Calendar/Week.cshtml`
- `Pages/Calendar/Day.cshtml`

### 7) Interesting Behaviors
- State persisted via cookie (`user_show_my_items_only`, 30 days, SameSite=Strict)
- Page reload triggers server-side filtering based on cookie value
- `toggleShowMyItems()` is a **global function** -- potential namespace collision

---

## 18. UnreadNotificationCount

### 1) Identity
- **Name:** UnreadNotificationCountViewComponent
- **Code-behind:** `ViewComponents/UnreadNotificationCountViewComponent.cs`
- **View:** `Pages/Shared/Components/UnreadNotificationCount/Default.cshtml`
- **Invocation:** `@await Component.InvokeAsync("UnreadNotificationCount")`

### 2) Purpose
Renders a notification badge showing the count of unread notifications.

### 3) Parameters/Model
- `@model int` - The unread notification count

### 4) UI Structure
- Only renders when count > 0
- `<span class="notification-badge">` with inline styles: absolute positioning, danger background, round badge, minimum 18px width
- Shows count, or "99+" if count exceeds 99

### 5) Dependencies
**Code-behind:**
- `AppDbContext` - Direct query on `UserNotifications` table

### 6) Usage
- `Pages/Shared/_Layout.cshtml` (in header bar, next to notifications bell)

### 7) Interesting Behaviors
- **Uses `int.Parse()` on NameIdentifier claim** (line 27 of code-behind) -- this violates the project convention of using `int.TryParse()` on claim values. If the claim is missing, `FindFirst(...).Value` will throw a NullReferenceException, and `int.Parse` would throw on malformed data. The `try/catch` block mitigates this but it is still a code smell.
- Inline styles on the badge span (not using CSS classes) -- may cause maintenance issues
- Returns `Content("")` on any exception (silent failure)

---

## 19. Layout Navigation Summary

### Admin Navigation (requires `AccessAdminNavigation` grant)

#### "My Calendar" Section
| Item | URL | Grant Required | Feature Flag |
|------|-----|----------------|--------------|
| Home (Owner) | /Home/Index | AdminAccess | - |
| Director Hub | /Director/Index | DirectorHubAccess (+ NOT AdminAccess) | - |
| Home (Manager) | / | NOT AdminAccess, NOT DirectorHubAccess | - |
| Schedule | /Calendar/Shifts or /Calendar/Month | - | ExcelCalendars+ExcelCalendarShifts |
| Company Overview | /Calendar/Overview | - | - |
| Requests | /Requests/Index | - | - |
| Analytics | /Admin/Analytics | ViewAnalytics | - |
| Audit Log | /Admin/AuditLog | ViewAuditLog | - |
| People | /Admin/Users | ViewAllUsers | - |
| My Groups | /MyTeam/Index | NOT ViewAllUsers | - |
| Companies | /Admin/Companies | EditCompany | - |
| Admin Hub | /Admin/Index | NOT AdminAccess | - |
| Settings | /Admin/Config | ViewSettings | - |
| Duty Rotation | /Admin/DutyRotation | ManageOnDuty | DutyRotationEnabled |
| Setup Tasks | /Admin/SetupTasks | SystemConfiguration | SetupTasksEnabled |
| Approval Rules | /Admin/Settings/ApprovalRules | SystemConfiguration | VacationApprovalEnabled |

#### "Owner Admin Panel" Section (requires `AdminAccess` grant)
| Item | URL |
|------|-----|
| Admin Panel | /Owner/Index |
| Telemetry | /Owner/Telemetry |

#### Friends (all authenticated, feature flag gated)
| Item | URL | Feature Flag |
|------|-----|--------------|
| My Friends | /Friends | FriendshipsEnabled |

#### "Calendar Management" Section (requires any of ViewShifts/ViewChores/ViewDuties)
| Item | URL | Grant |
|------|-----|-------|
| Shifts Management | /Calendar/Table or /Calendar/Shifts | ViewShifts |
| Chores Management | /Public/Chores or /Calendar/Chores | ViewChores |
| On-Duty Management | /Public/OnDuty or /Calendar/OnCall | ViewDuties |

### Employee Navigation (requires NOT `AccessAdminNavigation`)
| Item | URL |
|------|-----|
| Home | / |
| Schedule | /Calendar/Shifts or /Calendar/Month |
| Company Overview | /Calendar/Overview |
| Requests | /My/Requests |
| My Groups | /MyTeam/Index |
| My Friends | /Friends (feature-flagged) |
| Chores | /Public/Chores or /Calendar/Chores |
| On-Duty | /Public/OnDuty or /Calendar/OnCall |
| My Profile | /My/Profile |
| Help | /My/Help |
| My Settings | /My/Settings |

### Header Bar (all authenticated users)
| Element | Description |
|---------|-------------|
| Page title | `ViewData["Title"]` |
| DecisionRibbon | ViewComponent (excluded from /Owner routes) |
| Notifications | Bell icon link to /My/NotificationCenter + UnreadNotificationCount badge |
| Language Toggle | LanguageToggle ViewComponent |
| Theme Toggle | Dark mode toggle button |
| Logout | POST form to /Auth/Logout |

### Sidebar Footer
| Element | Description |
|---------|-------------|
| User avatar | First letter of first name |
| User name | First name |
| User role | Role display name (via RoleDisplayHelper) |
| User menu | Profile, Settings, Logout |
| Ctrl+K hint | Command palette shortcut |

### Bottom Dock (feature-flagged: WidgetsEnabled)
- OnCallWidget ViewComponent (showInSidebar = false)
- Collapsible with localStorage persistence

### Components Invoked by _Layout
1. `SystemAlerts`
2. `LanguageEditModeBanner`
3. `ContextSwitcher` (behind EnableCompanySwitcher flag)
4. `DecisionRibbon` (excluded from /Owner routes)
5. `UnreadNotificationCount`
6. `LanguageToggle`
7. `OnCallWidget` (behind WidgetsEnabled flag)

---

## ViewComponent Code-Behind Summary

All ViewComponent code-behind files are located in `ViewComponents/*.cs`:

| File | Component | Async | Injected Services |
|------|-----------|-------|-------------------|
| `CalendarSkeletonViewComponent.cs` | CalendarSkeleton | No | None |
| `ContextSwitcherViewComponent.cs` | ContextSwitcher | Yes | Localizer, AppDbContext, ITenantResolver, IGrantService, ILogger |
| `ErrorBannerViewComponent.cs` | ErrorBanner | No | None |
| `ErrorToastViewComponent.cs` | ErrorToast | No | None |
| `ExcelCalendarTableViewComponent.cs` | ExcelCalendarTable | No | None (pass-through) |
| `HierarchyTreeViewComponent.cs` | HierarchyTree | Yes | Localizer, AppDbContext, IGrantService, ILogger |
| `LanguageToggleViewComponent.cs` | LanguageToggle | Yes | Localizer, ILanguageManagementService, ITenantResolver |
| `LoadingSkeletonViewComponent.cs` | LoadingSkeleton | No | None |
| `LoadingSpinnerViewComponent.cs` | LoadingSpinner | No | None |
| `OnCallWidgetViewComponent.cs` | OnCallWidget | Yes | Localizer, IWidgetService, ITenantResolver |
| `PaginationViewComponent.cs` | Pagination | No | None (reads HttpContext directly) |
| `ScopeSwitcherViewComponent.cs` | ScopeSwitcher | Yes | Localizer, AppDbContext, IGrantService |
| `ShowMyItemsToggleViewComponent.cs` | ShowMyItemsToggle | No | Localizer, IUserPreferenceService |
| `UnreadNotificationCountViewComponent.cs` | UnreadNotificationCount | Yes | AppDbContext |

### Additional ViewComponents (discovered but not in audit scope)
- `BreadcrumbViewComponent.cs`
- `DecisionRibbonViewComponent.cs`
- `LanguageEditModeBannerViewComponent.cs`
- `OwnerCompanySelectorViewComponent.cs`
- `SystemAlertsViewComponent.cs`

---

## Cross-Cutting Observations

### Accessibility (A11y)
- Consistent use of ARIA attributes: `role`, `aria-live`, `aria-expanded`, `aria-label`, `aria-busy`, `aria-hidden`
- Skip link for keyboard navigation
- Visually-hidden text for screen readers on loading states
- Roving tabindex pattern in ScopeSwitcher
- Keyboard navigation (Arrow keys, Escape, Enter, Home, End) in ContextSwitcher and ScopeSwitcher

### RTL Support
- Layout: `dir="rtl"` on `<html>` element when Hebrew
- Phone numbers use `dir="ltr"` to prevent digit reversal
- ExcelCalendarTable uses hardcoded Hebrew day names
- Conditional `rtl.css` stylesheet loaded

### Localization
- All components use `IStringLocalizer<SharedResources>` or `<loc>` tag helper
- Client-side strings bridged via `_LocalizationScript.cshtml` (173 keys in `window.AppLocalizer`)
- `loc-aria-label`, `loc-title`, `loc-placeholder` tag helper attributes for attribute-level localization

### Feature Flags Gating Navigation
- `EnableCompanySwitcher` - ContextSwitcher visibility
- `ExcelCalendars` + `ExcelCalendarShifts/Chores/OnCall` - Calendar URL routing
- `FriendshipsEnabled` - Friends nav link
- `DutyRotationEnabled` - Duty Rotation nav link
- `SetupTasksEnabled` - Setup Tasks nav link
- `VacationApprovalEnabled` - Approval Rules nav link
- `WidgetsEnabled` - Bottom dock panel

### Potential Issues Found
1. **UnreadNotificationCountViewComponent** uses `int.Parse()` on claim value instead of `int.TryParse()` (convention violation)
2. **LanguageToggle** and **ShowMyItemsToggle** expose global functions (`toggleLanguage`, `toggleShowMyItems`) without IIFE wrapping
3. **ErrorBanner** and **ErrorToast** are defined but appear to have no current callers in `.cshtml` files
4. **_ValidationMessage** partial is defined but appears to have no current callers
5. **HierarchyTree** embeds ~500 lines of CSS inline in the view rather than in an external stylesheet
6. **ExcelCalendarTable** hardcodes Hebrew day names instead of using localization
7. **UnreadNotificationCount** uses inline styles on the badge instead of CSS classes
