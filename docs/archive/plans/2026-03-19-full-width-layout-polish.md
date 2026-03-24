# Full-Width Layout Polish Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Polish 12 UI items across the app to optimize for full-width layout after sidebar collapse, improving data density, visual hierarchy, and usability.

**Architecture:** CSS-first approach — 7 of 12 items are pure CSS. C# changes limited to view model properties and data computation. JS changes limited to search filter and collapse-all functions. No new pages, no DB changes, no migrations.

**Tech Stack:** CSS (site.css, calendar.css, components.css), Razor (cshtml), C# (Shifts.cshtml.cs, TimeHelpers.cs, ViewComponents), JS (excel-calendar-groups.js)

**Spec:** `docs/superpowers/specs/2026-03-19-full-width-layout-polish-design.md`

---

## Task 1: CSS-Only Global Polish (#1 sticky headers, #2 breadcrumb, #8 empty cells)

**Files:**
- Modify: `wwwroot/css/site.css:2562-2585` (breadcrumb rules)
- Modify: `wwwroot/css/calendar.css:2171-2176` (toolbar padding)
- Modify: `wwwroot/css/calendar.css` (add empty cell rule near line 3580)
- Create or modify: `wwwroot/css/components.css` or `wwwroot/css/site.css` (add sticky utility classes)
- Modify: `Pages/Admin/Users.cshtml` (add sticky classes to table)
- Modify: `Pages/Admin/Organization/Grants/Index.cshtml` (add sticky classes)
- Modify: `Pages/Admin/Organization/Roles/Index.cshtml` (add sticky classes)
- Modify: `Pages/Admin/AuditLog.cshtml` (add sticky-header class)
- Modify: `Pages/Owner/DatabaseConsole.cshtml` (add sticky-header class)
- Modify: `Pages/Owner/Telemetry.cshtml` (add sticky-header class)

- [ ] **Step 1: Add sticky table utility classes to site.css**

Add at end of `wwwroot/css/site.css`:
```css
/* Sticky table utilities (tiered — see spec #1) */
.data-table--sticky-header thead th {
  position: sticky;
  top: 0;
  z-index: 2;
  background: var(--surface);
  box-shadow: 0 1px 0 var(--border);
}

.data-table--sticky-col td:first-child,
.data-table--sticky-col th:first-child {
  position: sticky;
  inset-inline-start: 0;
  z-index: 3;
  background: var(--surface);
  box-shadow: 1px 0 0 var(--border);
}

.data-table--sticky-col thead th:first-child {
  z-index: 5;
}
```

- [ ] **Step 2: Reduce breadcrumb vertical space in site.css**

Find `.breadcrumb-nav` at line 2562 and change `margin-bottom: 1.5rem` to `margin-bottom: var(--space-2)`.

Find `.breadcrumb` padding at line 2583 and change `padding: 0.875rem 1.25rem` to `padding: var(--space-1) 1.25rem`.

- [ ] **Step 3: Reduce calendar toolbar padding in calendar.css**

Find `.cal-toolbar` at line 2171. Change:
- `padding` from `var(--space-3) var(--space-4)` to `var(--space-2) var(--space-3)`
- Add `gap: var(--space-1);` if not present

- [ ] **Step 4: Add empty cell dotted border in calendar.css**

Add near the existing `:empty` rules (line ~3580):
```css
/* Empty cell visual hint — dashed border for unassigned slots */
.excel-calendar__cell-content:not(:has(> *)) {
  border: 1px dashed var(--border);
  min-height: 28px;
  border-radius: var(--radius-sm);
}
```

- [ ] **Step 5: Add sticky classes to 6 page templates**

Tier 1 (sticky header + sticky col) — add both classes to the SPECIFIC `<table>` element:
- `Pages/Admin/Users.cshtml` — find the main users `<table class="data-table"` (the one with Name/Email/Company columns) and change to `class="data-table data-table--sticky-header data-table--sticky-col"`
- `Pages/Admin/Organization/Grants/Index.cshtml` — this page has TWO `data-table` elements. Apply ONLY to the grants list table (~line 91, the one with User/GrantType/Scope/Capabilities columns), NOT to the user summary table (~line 154)
- `Pages/Admin/Organization/Roles/Index.cshtml` — this page also has TWO `data-table` elements. Apply ONLY to the assignments table (~line 92, with User/Role/Scope columns), NOT to the role templates list (~line 148)

Tier 2 (sticky header only) — add `data-table--sticky-header` to the EXISTING class:
- `Pages/Admin/AuditLog.cshtml` — table has `class="audit-table"`, change to `class="audit-table data-table--sticky-header"`
- `Pages/Owner/DatabaseConsole.cshtml` — table has `class="results-table"`, change to `class="results-table data-table--sticky-header"` (this is the dynamic query results table)
- `Pages/Owner/Telemetry.cshtml` — find the main data table and add `data-table--sticky-header`

Note: The CSS `.data-table--sticky-header` targets the class name as a modifier — it works regardless of what other classes are on the element.

- [ ] **Step 6: Build and verify**

Run: `dotnet build --no-restore 2>&1 | tail -5`
Expected: 0 errors, 0 warnings

- [ ] **Step 7: Commit**

```bash
git add wwwroot/css/site.css wwwroot/css/calendar.css Pages/Admin/Users.cshtml Pages/Admin/Organization/Grants/Index.cshtml Pages/Admin/Organization/Roles/Index.cshtml Pages/Admin/AuditLog.cshtml Pages/Owner/DatabaseConsole.cshtml Pages/Owner/Telemetry.cshtml
git commit -m "feat: sticky table headers (tiered), reduce breadcrumb spacing, empty cell borders

Tier 1 (sticky header+col): Users, Grants, Roles
Tier 2 (sticky header): AuditLog, DatabaseConsole, Telemetry
Breadcrumb margin reduced from 1.5rem to var(--space-2)
Empty calendar cells show dashed border via :not(:has(> *))"
```

---

## Task 2: Calendar Toolbar Condensing + Separator (#11)

**Files:**
- Modify: `wwwroot/css/calendar.css` (add media query for single-row layout)

- [ ] **Step 1: Add single-row desktop layout for calendar toolbar**

Add to `wwwroot/css/calendar.css` after the existing `.cal-toolbar` rules:
```css
/* Desktop: condense toolbar to single row */
@media (min-width: 1024px) {
  .cal-toolbar {
    flex-direction: row;
    flex-wrap: wrap;
    align-items: center;
  }
  .cal-toolbar__row {
    display: contents;
  }
  /* Visual separator between selector group and action group */
  .cal-toolbar__row:first-child::after {
    content: '';
    width: 1px;
    height: 24px;
    background: var(--border);
    align-self: center;
    margin: 0 var(--space-2);
  }
}
```

- [ ] **Step 2: Commit**

```bash
git add wwwroot/css/calendar.css
git commit -m "feat: condense calendar toolbar to single row on desktop

Flattens toolbar rows via display:contents at >1024px.
Adds thin vertical separator between selector and action groups."
```

---

## Task 3: Color-Coded Shift Chips (#3)

**Files:**
- Modify: `wwwroot/css/calendar.css` (add color rules for overview chips)
- Modify: `Pages/Calendar/Shifts.cshtml.cs:680-691` (add RowColor to assignment Role)

- [ ] **Step 1: Add color-coded chip CSS for Overview page**

Add to `wwwroot/css/calendar.css`:
```css
/* Color-coded assignment chips for Overview calendar */
.overview-calendar .excel-calendar__assignment[data-role-color="shift"] {
  background: rgba(59, 130, 246, 0.15);
  border-inline-start: 3px solid #3B82F6;
}
.overview-calendar .excel-calendar__assignment[data-role-color="chore"] {
  background: rgba(245, 158, 11, 0.15);
  border-inline-start: 3px solid #F59E0B;
}
.overview-calendar .excel-calendar__assignment[data-role-color="duty"] {
  background: rgba(139, 92, 246, 0.15);
  border-inline-start: 3px solid #8B5CF6;
}

/* Dark mode: increase opacity for readability */
[data-theme="dark"] .overview-calendar .excel-calendar__assignment[data-role-color="shift"] {
  background: rgba(59, 130, 246, 0.25);
}
[data-theme="dark"] .overview-calendar .excel-calendar__assignment[data-role-color="chore"] {
  background: rgba(245, 158, 11, 0.25);
}
[data-theme="dark"] .overview-calendar .excel-calendar__assignment[data-role-color="duty"] {
  background: rgba(139, 92, 246, 0.25);
}
```

- [ ] **Step 2: Add RowColor to shift assignments in Shifts user-mode**

In `Pages/Calendar/Shifts.cshtml.cs`, find `BuildCellsForUser` at line ~680 where assignments are created. Add `Role = a.ShiftInstance.ShiftType.RowColor` to the `ExcelCalendarAssignment` initializer:

```csharp
cell.Assignments = userAssignments.Select(a => new ExcelCalendarAssignment
{
    Id = a.Id,
    Name = localizedShiftNames.GetValueOrDefault(a.ShiftInstance.ShiftTypeId,
        a.ShiftInstance.ShiftType?.Name ?? _localizer["Shift"].Value),
    SubLabel = a.Note,
    Role = a.ShiftInstance.ShiftType?.RowColor,  // ← ADD THIS LINE
    IsTrainee = a.TraineeUserId == userId,
    IsTraineeShift = a.IsTraineeShift,
    UserId = a.UserId,
    TraineeUserId = a.TraineeUserId,
    TraineeName = a.Trainee?.DisplayName
}).ToList();
```

- [ ] **Step 3: Build and verify**

Run: `dotnet build --no-restore 2>&1 | tail -5`

- [ ] **Step 4: Commit**

```bash
git add wwwroot/css/calendar.css Pages/Calendar/Shifts.cshtml.cs
git commit -m "feat: color-coded shift chips in Overview and Shifts user-mode

Overview chips colored by type (shift=blue, chore=amber, duty=purple).
Shifts user-mode chips colored by ShiftType.RowColor.
Dark mode uses higher opacity (0.25) for readability."
```

---

## Task 4: Weekly Hours Per User (#4) — Extract TimeHelpers + Add Column

**Files:**
- Modify: `Services/TimeHelpers.cs` (add MergeAndSumHours)
- Modify: `Services/ShiftAssignmentService.cs:1019-1046` (call TimeHelpers instead of private copy)
- Modify: `ViewComponents/ExcelCalendarTableViewComponent.cs:16-25` (add WeeklyHours to ExcelCalendarRow)
- Modify: `Pages/Calendar/Shifts.cshtml.cs:358-438` (compute hours in BuildUserBasedCalendarAsync)
- Modify: `Pages/Shared/Components/ExcelCalendarTable/Default.cshtml:31-51` (add Total header column)
- Modify: `Pages/Shared/Components/ExcelCalendarTable/_CalendarRow.cshtml` (add hours cell)
- Modify: `wwwroot/css/calendar.css` (summary column styling)

- [ ] **Step 1: Extract MergeAndSumHours to TimeHelpers**

In `Services/TimeHelpers.cs`, add the method (copied from `ShiftAssignmentService.cs` line 1019):
```csharp
/// <summary>
/// Merges overlapping time windows and sums total unique hours.
/// Prevents double-counting when shifts overlap.
/// </summary>
public static double MergeAndSumHours(List<(DateTime start, DateTime end)> windows)
{
    if (windows.Count == 0) return 0;

    double total = 0;
    var currentStart = windows[0].start;
    var currentEnd = windows[0].end;

    for (int i = 1; i < windows.Count; i++)
    {
        if (windows[i].start <= currentEnd)
        {
            if (windows[i].end > currentEnd)
                currentEnd = windows[i].end;
        }
        else
        {
            total += (currentEnd - currentStart).TotalHours;
            currentStart = windows[i].start;
            currentEnd = windows[i].end;
        }
    }
    total += (currentEnd - currentStart).TotalHours;
    return total;
}
```

- [ ] **Step 2: Update ShiftAssignmentService to call TimeHelpers**

In `Services/ShiftAssignmentService.cs`, change the `MergeAndSumHours` method at line 1019 from `private static` to calling `TimeHelpers.MergeAndSumHours`. Replace the method body with:
```csharp
private static double MergeAndSumHours(List<(DateTime start, DateTime end)> windows)
    => TimeHelpers.MergeAndSumHours(windows);
```

- [ ] **Step 3: Add WeeklyHours to ExcelCalendarRow**

In `ViewComponents/ExcelCalendarTableViewComponent.cs`, add to the `ExcelCalendarRow` class (line ~24):
```csharp
public double? WeeklyHours { get; set; }
```

- [ ] **Step 4: Compute weekly hours in BuildUserBasedCalendarAsync**

In `Pages/Calendar/Shifts.cshtml.cs`, in `BuildUserBasedCalendarAsync` after the rows are built (around line 415-426 for non-tech, or in `BuildTechGroupedRowsAsync` for tech), add computation for each row's WeeklyHours.

Add weekly hours computation INSIDE each `foreach (var user in ...)` loop, immediately after the `row.Cells = BuildCellsForUser(...)` line and BEFORE `rows.Add(row)`:

```csharp
// Compute weekly hours for this user
var userShiftWindows = assignments
    .Where(a => a.UserId == user.Id && a.ShiftInstance.WorkDate >= StartDate && a.ShiftInstance.WorkDate <= EndDate)
    .Select(a => TimeHelpers.GetShiftWindow(a.ShiftInstance.ShiftType, a.ShiftInstance.WorkDate))
    .OrderBy(w => w.start)
    .ToList();
row.WeeklyHours = TimeHelpers.MergeAndSumHours(userShiftWindows);
```

This must be added in **4 separate foreach loops** — INSIDE each loop, not after it:

1. **Non-tech path** (~line 422): inside `foreach (var user in users)` in `BuildUserBasedCalendarAsync`
2. **Tech regulars** (~line 568): inside `foreach (var user in regulars)` in `BuildTechGroupedRowsAsync`
3. **Tech trainees** (~line 587): inside `foreach (var user in trainees)` in `BuildTechGroupedRowsAsync`
4. **Company users** (~line 608): inside `foreach (var user in companyUsers)` in `BuildTechGroupedRowsAsync`

**Critical:** Each insertion must be INSIDE the foreach body (after `row.Cells = ...`, before `rows.Add(row)`), not after the foreach closing brace. Placing it after the loop would give ALL rows the last user's hours.

- [ ] **Step 5: Add Total column header in Default.cshtml**

In `Pages/Shared/Components/ExcelCalendarTable/Default.cshtml`, after the day column headers in `<thead>` (~line 49), before `</tr>`:
```html
@if (Model.Rows.Any(r => r.WeeklyHours.HasValue))
{
    <th class="excel-calendar__header-total" scope="col">
        <span class="excel-calendar__day-name">@Localizer["Total"]</span>
    </th>
}
```

- [ ] **Step 6: Add hours cell in _CalendarRow.cshtml**

In `Pages/Shared/Components/ExcelCalendarTable/_CalendarRow.cshtml`, after the `}` that closes the `@foreach (var day in days)` loop, before `</tr>`:
```html
@if (row.WeeklyHours.HasValue)
{
    <td class="excel-calendar__cell excel-calendar__cell--total">
        <span class="excel-calendar__hours">@($"{row.WeeklyHours:0.#}h")</span>
    </td>
}
```

- [ ] **Step 7: Add summary column CSS**

Add to `wwwroot/css/calendar.css`:
```css
/* Weekly hours summary column */
.excel-calendar__cell--total {
  background: var(--surface-soft);
  text-align: center;
  font-variant-numeric: tabular-nums;
  font-weight: 600;
  font-size: var(--text-tiny);
  max-width: 50px;
  min-width: 40px;
}

.excel-calendar__header-total {
  background: var(--surface-soft) !important;
  text-align: center;
  min-width: 40px;
  max-width: 50px;
}
```

- [ ] **Step 8: Add "Total" resource key**

Add to `Resources/SharedResources.resx`:
```xml
<data name="Total" xml:space="preserve"><value>Total</value></data>
```
Add to `Resources/SharedResources.he-IL.resx`:
```xml
<data name="Total" xml:space="preserve"><value>סה"כ</value></data>
```

- [ ] **Step 9: Build + test**

Run: `dotnet build --no-restore 2>&1 | tail -5`
Run: `dotnet test --no-build --filter "FullyQualifiedName!~IntegrationTests" 2>&1 | tail -5`

- [ ] **Step 10: Commit**

```bash
git add Services/TimeHelpers.cs Services/ShiftAssignmentService.cs ViewComponents/ExcelCalendarTableViewComponent.cs Pages/Calendar/Shifts.cshtml.cs Pages/Shared/Components/ExcelCalendarTable/Default.cshtml Pages/Shared/Components/ExcelCalendarTable/_CalendarRow.cshtml wwwroot/css/calendar.css Resources/SharedResources.resx Resources/SharedResources.he-IL.resx
git commit -m "feat: weekly hours summary column in Shifts user-mode calendar

Extracts MergeAndSumHours to TimeHelpers (shared, DRY).
Adds WeeklyHours property to ExcelCalendarRow.
Renders 'Total' column with tabular-nums alignment and soft background."
```

---

## Task 5: Admin/Users Improvements (#5 move Add User, #6 search)

**Files:**
- Modify: `Pages/Admin/Users.cshtml:987-1067` (reorder Add User form)
- Modify: `Pages/Admin/Users.cshtml` (add search input above table)

- [ ] **Step 1: Move Add User form above the Existing Users table**

In `Pages/Admin/Users.cshtml`, cut the entire Add User section (lines 987-1067) and paste it ABOVE the `<table>` that renders existing users. Find the `<h2>` for "Existing Users" and place the Add User form between the section header and the table.

- [ ] **Step 2: Add search input above the Existing Users table**

Add between the section header and the table (after filters, before `<table>`):
```html
<div style="margin-bottom: var(--space-3);">
    <label class="sr-only" for="userSearch">@Localizer["SearchUser"]</label>
    <input type="text" id="userSearch" class="form-input"
           placeholder="@Localizer["SearchUserPlaceholder"]"
           oninput="filterUserTable(this.value)"
           style="max-width: 300px;" />
</div>

<script>
function filterUserTable(query) {
    var rows = document.querySelectorAll('.users-table tbody tr');
    var q = query.toLowerCase();
    rows.forEach(function(row) {
        var text = row.textContent.toLowerCase();
        row.style.display = text.includes(q) ? '' : 'none';
    });
}
</script>
```

- [ ] **Step 3: Add resource keys**

Add to `SharedResources.resx`: `SearchUser` = "Search user", `SearchUserPlaceholder` = "Search by name or email..."
Add to `SharedResources.he-IL.resx`: `SearchUser` = "חפש משתמש", `SearchUserPlaceholder` = "חפש לפי שם או אימייל..."

- [ ] **Step 4: Commit**

```bash
git add Pages/Admin/Users.cshtml Resources/SharedResources.resx Resources/SharedResources.he-IL.resx
git commit -m "feat: move Add User form to top, add client-side user search

Add User form now appears above the table, not below 40 rows.
Search input filters table rows by name/email in real-time."
```

---

## Task 6: Dashboard Improvements (#9 card consistency, #10 quick actions)

**Files:**
- Modify: `Pages/Home/Index.cshtml:44-152` (add quick actions, normalize cards)
- Modify: `wwwroot/css/site.css` or page-specific CSS (card min-height)
- Modify: `Resources/SharedResources.resx` + `.he-IL.resx` (new keys)

- [ ] **Step 1: Add quick action buttons above stat cards**

In `Pages/Home/Index.cshtml`, find the first stat cards section (~line 44) and add before it:
```html
<div style="display: flex; gap: var(--space-3); margin-bottom: var(--space-4);">
    <a href="/Calendar/Shifts" class="btn btn--primary btn--md">
        📅 @Localizer["GoToCalendar"]
    </a>
    <a href="/Calendar/Shifts?Mode=user" class="btn btn--outline btn--md">
        👥 @Localizer["ViewByPeople"]
    </a>
</div>
```

- [ ] **Step 2: Add dashboard card normalization CSS**

First check the actual card class names in `Pages/Home/Index.cshtml` — cards use `.card` and `.card--metric` classes inside a `.dashboard-grid` container. Add to `wwwroot/css/site.css`:
```css
.dashboard-grid .card,
.home-dashboard .card {
  min-height: 120px;
  display: flex;
  flex-direction: column;
  justify-content: center;
}
```

Note: Verify the exact class names by reading `Pages/Home/Index.cshtml` before applying — the dashboard may use `.card--metric`, `.card--status-warning`, etc.

- [ ] **Step 2b: Merge redundant company metrics**

In `Pages/Home/Index.cshtml`, find where "Total Companies" and "Active Companies" are rendered as separate cards. When values are equal, merge into a single card. Add a Razor conditional:
```html
@if (totalCompanies == activeCompanies)
{
    <!-- Single merged card: "37 חברות (הכל פעילות)" -->
}
else
{
    <!-- Show both cards separately -->
}
```

Read the actual page model properties first to determine the correct variable names.

- [ ] **Step 3: Add resource keys**

Add `GoToCalendar` = "Go to Calendar" / "לוח זמנים"
Add `ViewByPeople` = "View by People" / "לפי אנשים"

- [ ] **Step 4: Commit**

```bash
git add Pages/Home/Index.cshtml wwwroot/css/site.css Resources/SharedResources.resx Resources/SharedResources.he-IL.resx
git commit -m "feat: dashboard quick actions + card height normalization

Adds 'Go to Calendar' and 'View by People' buttons above stat cards.
Normalizes dashboard card heights to min-height: 120px."
```

---

## Task 7: Group Header Enhancement + Collapse All/Expand All (#13, #14)

**Files:**
- Modify: `ViewComponents/ExcelCalendarTableViewComponent.cs:27-33` (add Color, MemberCount to ExcelCalendarGroup)
- Modify: `Pages/Calendar/Shifts.cshtml.cs:556-614` (pass color + count when creating groups)
- Modify: `Pages/Shared/Components/ExcelCalendarTable/Default.cshtml:57-63` (render color border + count + collapse buttons)
- Modify: `wwwroot/css/calendar.css` (group header color border)
- Modify: `wwwroot/js/excel-calendar-groups.js` (add collapseAll/expandAll)
- Modify: `Pages/Calendar/Shifts.cshtml:91-102` (add collapse/expand buttons near mode tabs)
- Modify: `Resources/SharedResources.resx` + `.he-IL.resx` (CollapseAll, ExpandAll)

- [ ] **Step 1: Add Color and MemberCount to ExcelCalendarGroup model**

In `ViewComponents/ExcelCalendarTableViewComponent.cs`, update the `ExcelCalendarGroup` class:
```csharp
public class ExcelCalendarGroup
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsCollapsed { get; set; }
    public int SortOrder { get; set; }
    public string? Color { get; set; }       // ← ADD
    public int MemberCount { get; set; }     // ← ADD
}
```

- [ ] **Step 2: Pass color and count when creating groups in Shifts.cshtml.cs**

In `BuildTechGroupedRowsAsync`, update all 3 `new ExcelCalendarGroup` calls:

For regular groups (~line 557):
```csharp
groups.Add(new ExcelCalendarGroup {
    Id = groupId,
    Name = $"{_localizer["Regulars"]} {displayName}",
    SortOrder = sortOrder++,
    Color = displaySt?.RowColor,
    MemberCount = regulars.Count
});
```

For trainee groups (~line 576):
```csharp
groups.Add(new ExcelCalendarGroup {
    Id = groupId,
    Name = $"{_localizer["Trainees"]} {displayName}",
    SortOrder = sortOrder++,
    Color = displaySt?.RowColor,
    MemberCount = trainees.Count
});
```

For company groups (~line 597):
```csharp
groups.Add(new ExcelCalendarGroup {
    Id = groupId,
    Name = companyName,
    SortOrder = sortOrder++,
    MemberCount = companyUsers.Count
});
```

- [ ] **Step 3: Render color border + count in Default.cshtml**

In `Pages/Shared/Components/ExcelCalendarTable/Default.cshtml`, update the group header rendering (~line 57-63):

Replace the group header `<tr>`:
```html
<tr class="excel-calendar__group-header" data-group-id="@group.Id">
    <td colspan="@(days.Count + 1)" style="@(group.Color != null ? $"--group-color: {group.Color}" : "")">
        <div class="excel-calendar__group-toggle">
            <span class="excel-calendar__group-chevron @(group.IsCollapsed ? "excel-calendar__group-chevron--collapsed" : "")">▼</span>
            <span class="excel-calendar__group-name" data-editable="true">@group.Name</span>
            @if (group.MemberCount > 0)
            {
                <span class="excel-calendar__group-count">(@group.MemberCount)</span>
            }
            <span class="excel-calendar__group-grip" title="@Localizer["DragToReorder"]">⋮⋮</span>
        </div>
    </td>
</tr>
```

- [ ] **Step 4: Add group header color CSS**

Add to `wwwroot/css/calendar.css`:
```css
/* Group header colored border */
.excel-calendar__group-header td {
  border-inline-start: 4px solid var(--group-color, var(--primary));
}

.excel-calendar__group-count {
  font-size: var(--text-tiny);
  color: var(--text-muted);
  font-weight: normal;
  margin-inline-start: var(--space-1);
}
```

- [ ] **Step 5: Add collapseAll/expandAll to excel-calendar-groups.js**

In `wwwroot/js/excel-calendar-groups.js`, add INSIDE the IIFE (before the `initGroups()` call at line ~74):
```js
    // Global collapse/expand all functions
    window.collapseAllGroups = function() {
        var headers = document.querySelectorAll('.excel-calendar__group-header');
        headers.forEach(function(header) {
            var groupId = header.dataset.groupId;
            if (groupId) toggleGroup(groupId, true);
        });
    };

    window.expandAllGroups = function() {
        var headers = document.querySelectorAll('.excel-calendar__group-header');
        headers.forEach(function(header) {
            var groupId = header.dataset.groupId;
            if (groupId) toggleGroup(groupId, false);
        });
    };
```

- [ ] **Step 6: Add collapse/expand buttons in Shifts.cshtml near mode tabs**

In `Pages/Calendar/Shifts.cshtml`, find the mode tabs area (~line 91-102). After the mode tab `<div>`, add:
```html
@if (Model.CalendarData.Groups != null)
{
    <div class="group-toggle-buttons" style="display: inline-flex; gap: var(--space-1); margin-inline-start: var(--space-2);">
        <button type="button" class="btn btn--sm btn--outline" onclick="collapseAllGroups()">
            <span>▲</span> @Localizer["CollapseAll"]
        </button>
        <button type="button" class="btn btn--sm btn--outline" onclick="expandAllGroups()">
            <span>▼</span> @Localizer["ExpandAll"]
        </button>
    </div>
}
```

- [ ] **Step 7: Add resource keys**

Add to `SharedResources.resx`: `CollapseAll` = "Collapse All", `ExpandAll` = "Expand All"
Add to `SharedResources.he-IL.resx`: `CollapseAll` = "כווץ הכל", `ExpandAll` = "הרחב הכל"

- [ ] **Step 8: Build + test**

Run: `dotnet build --no-restore 2>&1 | tail -5`
Run: `dotnet test --no-build --filter "FullyQualifiedName!~IntegrationTests" 2>&1 | tail -5`

- [ ] **Step 9: Commit**

```bash
git add ViewComponents/ExcelCalendarTableViewComponent.cs Pages/Calendar/Shifts.cshtml.cs Pages/Shared/Components/ExcelCalendarTable/Default.cshtml wwwroot/css/calendar.css wwwroot/js/excel-calendar-groups.js Pages/Calendar/Shifts.cshtml Resources/SharedResources.resx Resources/SharedResources.he-IL.resx
git commit -m "feat: group header colors + member count + collapse all/expand all

Group headers show colored left border from ShiftType.RowColor.
Member count '(N)' shown next to group name.
Collapse All / Expand All buttons inline with mode tabs.
localStorage persistence maintained via existing toggleGroup."
```

---

## Task 8: Final Build + Manual Browser Verification

- [ ] **Step 1: Full build**

Run: `dotnet build 2>&1 | tail -5`
Expected: 0 warnings, 0 errors

- [ ] **Step 2: Run tests**

Run: `dotnet test --filter "FullyQualifiedName!~IntegrationTests" 2>&1 | tail -5`
Expected: 259+ passed, 0 failed

- [ ] **Step 3: Manual verification checklist**

Start the app and verify each item:

1. Sticky headers: Scroll Admin/Users (50+ rows) → header stays, name column sticks
2. Breadcrumb: Calendar page overhead < 220px. Admin/Config doesn't feel cramped.
3. Color chips: Overview shows colored chips. Shifts user-mode shows RowColor borders.
4. Weekly hours: Shifts user-mode → "Xh" column with tabular alignment
5. Add User: Form above table, not below
6. Search: Type in search → rows filter instantly
7. Empty cells: Dotted border on unassigned cells (not noisy with 20+ users)
8. Dashboard cards: Uniform height
9. Quick actions: "Go to Calendar" button visible on dashboard
10. Toolbar: Single row on desktop (>1024px), separator between groups
11. Group headers: Colored left border + "(N)" count
12. Collapse All/Expand All: Buttons visible when groups exist, toggle works

Cross-cutting:
- All items in Hebrew RTL + English LTR
- All items in light + dark mode
- All items with sidebar open + collapsed
