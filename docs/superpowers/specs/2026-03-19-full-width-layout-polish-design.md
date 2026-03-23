# Full-Width Layout Polish — Design Spec

**Date:** 2026-03-19
**Status:** Design finalized (Creative Director + Senior Developer reviewed)
**Context:** With the collapsible sidebar giving full viewport width, audit identified 14 improvements. Cross-page analysis refined scope to 12 actionable items (2 resolved).

---

## 1. Sticky Table Headers (Tiered — 6 pages)

### Problem
Admin tables lose their column headers when scrolling through many rows. But not all tables need this — small config tables (5-15 rows) never scroll enough to warrant sticky headers.

### Design: Three Tiers

**Tier 1: Sticky header + sticky first column** (data-dense, identity-critical)
- `Admin/Users` (50+ rows, 9 cols, Name is the anchor for all other columns)
- `Admin/Organization/Grants` (100+ rows, 7 cols, User name gives meaning to grant rows)
- `Admin/Organization/Roles` Assignments view (30-50 rows, 7 cols, User/Role name is context)

Rationale: These tables have a "who" column (first column) that gives meaning to every other column. Scrolling right without the name makes the data meaningless. Scrolling down without headers makes columns ambiguous.

**Tier 2: Sticky header only** (many rows, header gives context)
- `Admin/AuditLog` (50+ paginated rows, Timestamp is col 1 but User+Action matter more)
- `Owner/DatabaseConsole` (variable row count, dynamic columns — headers are the only way to know what data means)
- `Owner/Telemetry` (20-100 rows per tab, timestamp-heavy)

Rationale: These tables have enough rows to lose headers, but no single "identity" column worth freezing.

**Tier 3: No sticky** (small tables, never scroll)
- Companies (5-20 rows), Directors (3-20), Analytics (summary), all Organization config tables (ChoreTypes, DutyTypes, Departments, JobTypes, Molecules, Areas, Projects, ShiftGroupings)

Rationale: These tables never have enough rows to push headers off-screen. Adding sticky is unnecessary complexity.

### Implementation

**Two opt-in CSS modifier classes** (not a blanket rule):

```css
/* Tier 1+2: Sticky header row */
.data-table--sticky-header thead th {
  position: sticky;
  top: 0;
  z-index: 2;
  background: var(--surface);
  box-shadow: 0 1px 0 var(--border);
}

/* Tier 1 only: Sticky first column (identity column) */
.data-table--sticky-col td:first-child,
.data-table--sticky-col th:first-child {
  position: sticky;
  inset-inline-start: 0;
  z-index: 3;
  background: var(--surface);
  box-shadow: 1px 0 0 var(--border);
}

/* Corner cell (header + first-col intersection) needs highest z */
.data-table--sticky-col thead th:first-child {
  z-index: 5;
}
```

Tables opt in by adding classes to their `<table>` element:
- Tier 1: `class="data-table data-table--sticky-header data-table--sticky-col"`
- Tier 2: `class="data-table data-table--sticky-header"`
- Tier 3: `class="data-table"` (unchanged)

**Edge case:** Tables inside `overflow-x: auto` wrappers — `position: sticky` works relative to the nearest scrollable ancestor, so this works correctly within scroll containers.

**Reference:** Calendar/Shifts already implements this pattern with `position: sticky` on both thead and first-column cells. The calendar's z-index hierarchy (5/10/15) is the proven model.

**Scope:** CSS utility classes (1 file), then add classes to 6 specific page templates.

---

## 2. Reduce Vertical Overhead on Calendar Pages (GLOBAL — all authenticated pages)

### Problem
~350px consumed before calendar data: disk warning banner (30px) + header bar (50px) + breadcrumb area (80px) + filter toolbar (120px) + mode tabs (50px). Managers on 768px-900px screens see only 2-3 data rows above the fold.

### Design
**Breadcrumb area** — reduce margin on `.breadcrumb-nav` and padding on `.breadcrumb`:
```css
.breadcrumb-nav {
  margin-bottom: var(--space-2);  /* down from current 1.5rem */
}
.breadcrumb {
  padding: var(--space-1) 0;  /* reduce vertical padding, use token not hardcoded value */
}
```

**Note:** Use `var(--space-2)` (not `var(--space-1)`) for the breadcrumb margin as a safer middle ground — `var(--space-1)` makes non-calendar pages (Settings, empty states) feel too cramped. Verify on Admin/Config and Requests pages during implementation.

**Calendar-specific** — reduce gap between filter toolbar rows and between toolbar and table:
```css
.cal-toolbar {
  padding: var(--space-2) var(--space-3);  /* down from var(--space-3) var(--space-4) */
  gap: var(--space-1);  /* down from var(--space-2) */
}
```

**Target:** Reduce total overhead from ~350px to ~220px (save ~130px).

**Scope:** Breadcrumb change is global. Toolbar change is calendar-specific (4 pages).

---

## 3. Color-Coded Shift Chips (Calendar/Overview + Calendar/Shifts user-mode)

### Problem
Overview page has a color legend (Vacation=green, Shift=blue, Chore=amber, Duty=purple) but the actual assignment chips in cells all look the same — light blue background regardless of type.

Calendar/Shifts user-mode shows shift type names (Morning/Afternoon/Night) as text chips without color coding, making it hard to visually scan shift distribution.

### Design

**Overview page:** Apply the `Role` property (already set to "shift", "chore", "duty" in Overview.cshtml.cs) as a CSS color:
```css
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
```

**Shifts user-mode:** Add `RowColor` from the ShiftType to the assignment chip's `Role` property in `BuildCellsForUser`. The existing `data-role-color` + `--_role-color` CSS infrastructure already handles inline hex colors.

**Dark mode:** Increase opacity to 0.25 for dark theme readability:
```css
[data-theme="dark"] .overview-calendar .excel-calendar__assignment[data-role-color="shift"] {
  background: rgba(59, 130, 246, 0.25);
}
/* Same for chore and duty */
```

**Scope:** CSS + minor C# change in `Shifts.cshtml.cs` `BuildCellsForUser`.

---

## 4. Weekly Hours Per User (Calendar/Shifts user-mode)

### Problem
In user-mode, managers see shift names per cell but no weekly total. They can't quickly assess workload balance across users.

### Design
Add a **summary column** at the end of each user row showing total hours for the displayed week.

**Data:** Extract `MergeAndSumHours` from `ShiftAssignmentService` (currently `private static`) to `TimeHelpers.MergeAndSumHours` (shared static method). Then call it from `BuildUserBasedCalendarAsync` using the already-loaded `assignments` list. Do NOT duplicate the logic — DRY principle.

**View model:** Add `WeeklyHours` property to `ExcelCalendarRow`:
```csharp
public double? WeeklyHours { get; set; }
```

**Rendering:** In `Default.cshtml`, after the last day column in the header, add a "סה\"כ" (Total) column. In each row, render `row.WeeklyHours` formatted as "Xh". Style the summary column with:
- `font-variant-numeric: tabular-nums` — numbers align vertically across rows
- Subtle background: `background: var(--surface-soft)` — visually separates summary from data cells
- Column width: `max-width: 50px` — keep narrow to not crowd the calendar

**Scope:** Shifts user-mode only (Chores and OnCall are date-based, not hour-based — no weekly hours).
- `Models/Support/TimeHelpers.cs` (extract `MergeAndSumHours` from `ShiftAssignmentService`)
- `Services/ShiftAssignmentService.cs` (call `TimeHelpers.MergeAndSumHours` instead of private copy)
- `ViewComponents/ExcelCalendarTableViewComponent.cs` (add `WeeklyHours` to model)
- `Default.cshtml` + `_CalendarRow.cshtml` (render column)
- `Shifts.cshtml.cs` (compute hours per user)

---

## 5. Move "Add User" to Top of Admin/Users

### Problem
The "Add User" form and "Import CSV" section are buried below 40+ user rows. Admins must scroll past the entire table to find them.

### Design
Move the "+ Add" button to a **prominent position in the Existing Users section header** (already partially done — there's an "+ Add" button). When clicked, it should expand an inline form ABOVE the table, not below it.

**Current state:** The "+ Add" button already exists and expands a form. The issue is that on page load, the form may be at the bottom. Ensure the expanded form appears at the TOP of the Existing Users section.

**Scope:** `Pages/Admin/Users.cshtml` — reorder HTML to put the add form above the table.

---

## 6. Text Search for Admin/Users

### Problem
With 40+ users, finding a specific person requires scanning rows or using browser Ctrl+F.

### Design
Add a **client-side search input** above the Existing Users table that filters rows by name/email:

```html
<label class="sr-only" for="userSearch">חפש משתמש</label>
<input type="text" id="userSearch" class="form-input" placeholder="חפש לפי שם או אימייל..."
       oninput="filterUserTable(this.value)" />
```

```js
function filterUserTable(query) {
  const rows = document.querySelectorAll('.users-table tbody tr');
  const q = query.toLowerCase();
  rows.forEach(row => {
    const text = row.textContent.toLowerCase();
    row.style.display = text.includes(q) ? '' : 'none';
  });
}
```

**Scope:** `Pages/Admin/Users.cshtml` — HTML + inline JS. No server changes.

---

## 7. Tab Counts on Requests Page

**RESOLVED** — Already implemented. The Requests page already uses `<span class="count-badge">` on all three tabs with live counts from the page model. No changes needed.

---

## 8. Empty Cell Visual Differentiation (All Excel Calendar Pages)

### Problem
Empty cells look identical to cells that simply haven't loaded. Managers scanning for gaps can't quickly spot unassigned slots.

### Design
Add a subtle visual hint to empty cells — a light dotted border or a faint diagonal hash pattern:

```css
.excel-calendar__cell-content:empty {
  background: repeating-linear-gradient(
    -45deg,
    transparent,
    transparent 4px,
    var(--border) 4px,
    var(--border) 5px
  );
  opacity: 0.3;
  min-height: 28px;
}
```

Alternative (simpler): just a dotted inner border:
```css
.excel-calendar__cell-content:not(:has(> *)) {
  border: 1px dashed var(--border);
  min-height: 28px;
  border-radius: var(--radius-sm);
}
```

**Note:** Use `:not(:has(> *))` instead of `:empty` because Razor may emit whitespace text nodes inside the div, which breaks `:empty`. The `:has()` selector is supported in Chrome 105+, Safari 15.4+, Firefox 121+ — well within the app's browser targets.

**Recommendation:** The dotted border approach. Clean, minimal, obvious.

**Density test required:** With 7 days × 20 users = 140 cells, if 80% are empty, 112 dotted rectangles appear. Use a very light color (`var(--border)` with low opacity or a muted variant) to keep it subtle. Verify on a fully populated Shikma calendar (55 users) that the dotted borders don't create visual noise.

**Conflict check:** The existing `.excel-calendar__cell-content:empty::after` rule (line ~3580 in calendar.css) renders a "+" hover affordance. Verify that both the dotted border and the "+" don't create double visual noise — they serve different purposes (border = "this is empty", "+" = "click to add") and should coexist.

**Scope:** `wwwroot/css/calendar.css` — single CSS rule. Affects all 4 Excel calendar pages automatically.

---

## 9. Dashboard Card Consistency

### Problem
Dashboard stat cards have inconsistent heights. The "Next Shift" card is taller than "Notifications" which just shows "No notifications." When all values are zero, the dashboard looks hollow.

### Design
**Normalize card heights:**
```css
.dashboard-stat-card {
  min-height: 120px;
  display: flex;
  flex-direction: column;
  justify-content: center;
}
```

**Merge redundant metrics:** When "Total Companies" = "Active Companies" (e.g., 37/37), show a single card: "37 חברות (הכל פעילות)" instead of two separate cards.

**Scope:** `Pages/Home/Index.cshtml` or Dashboard page + CSS.

---

## 10. Dashboard Quick Actions

### Problem
The dashboard shows metrics but no primary actions. A manager landing here must navigate to the calendar via the sidebar.

### Design
Add a **hero action bar** at the top of the dashboard:

```html
<div class="dashboard-actions">
  <a href="/Calendar/Shifts" class="btn btn--primary btn--md">
    📅 <loc key="GoToCalendar" />
  </a>
  <a href="/Calendar/Shifts?Mode=user" class="btn btn--outline btn--md">
    👥 <loc key="ViewByPeople" />
  </a>
</div>
```

Position it above the stat cards. Use `btn--md` (not `btn--lg`) to stay proportionate with the dashboard's card-based layout — oversized hero buttons would feel disproportionate next to the compact stat cards.

**Scope:** Dashboard page HTML + CSS.

---

## 11. Condense Calendar Filter Toolbars

### Problem
Calendar filter toolbar uses 2 rows (~120px). On smaller screens with the sidebar open, this pushes the table far down.

### Design
**Single-row layout** for desktop (>1024px): all selectors, date nav, mode tabs, and buttons in one horizontal row using flexbox with `flex-wrap: wrap` and `gap: var(--space-2)`.

```css
@media (min-width: 1024px) {
  .cal-toolbar {
    flex-direction: row;
    flex-wrap: wrap;
    align-items: center;
  }
  .cal-toolbar__row {
    display: contents;  /* flatten nested rows into single flex container */
  }
}
```

On smaller screens, keep the current 2-row layout.

**Visual grouping in single-row mode:** When flattened to one row, selectors (molecule, job type) and actions (mode tabs, filter, print) merge into one line. Add a thin vertical separator between the two groups to preserve visual structure:
```css
@media (min-width: 1024px) {
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

**Scope:** `wwwroot/css/calendar.css` — CSS only. Affects all 4 calendar pages.

---

## 12. Remove Duplicate Print Button

**RESOLVED** — Investigation shows only 1 print button per page in the toolbar. The floating print button at bottom-right was from the `calendar-print.js` which adds it dynamically. It's a supplementary affordance, not a true duplicate.

**Decision:** Keep both — the floating one is useful on long-scrolled pages where the toolbar is off-screen. No change needed.

---

## 13. Group Header Enhancement (Shikma Calendar)

### Problem
Group headers (תקני משמרת דלתא, פאי) use the same dark navy background as the table header. They're functional but visually indistinct.

### Design
Add a **colored left border** to group headers using the first ShiftType's `RowColor` in that group:

```css
.excel-calendar__group-header td {
  border-inline-start: 4px solid var(--group-color, var(--primary));
}
```

Pass `--group-color` via inline style from the `ExcelCalendarGroup` model (add a `Color` property).

Also add **member count** to the group name: "תקני משמרת דלתא (1)".

Add `Color` and `MemberCount` properties to `ExcelCalendarGroup` model. Compute `MemberCount` in C# (in `BuildTechGroupedRowsAsync`) — do NOT compute in Razor template (would be O(n*m) per render).

**Scope:** `ExcelCalendarTableViewComponent.cs` (Color + MemberCount properties), `Default.cshtml` (inline style + count display), `Shifts.cshtml.cs` (pass color from ShiftType.RowColor + count from group size).

---

## 14. Collapse All / Expand All + Drawer Buttons

### Problem
Calendar pages with groups (Shikma user-mode, Chores, OnCall) have per-group collapse/expand via ▼ chevrons, but no global "collapse all" / "expand all" buttons.

### Design
Add a **button group** in the calendar toolbar area that looks like a drawer icon:

```
[▲ Collapse All] [▼ Expand All]
```

**HTML:**
```html
<div class="group-toggle-buttons">
  <button type="button" class="btn btn--sm btn--outline" onclick="collapseAllGroups()">
    <span>▲</span> <loc key="CollapseAll" />
  </button>
  <button type="button" class="btn btn--sm btn--outline" onclick="expandAllGroups()">
    <span>▼</span> <loc key="ExpandAll" />
  </button>
</div>
```

**JS** (add INSIDE the existing IIFE in `excel-calendar-groups.js` — `toggleGroup` is IIFE-scoped and must be captured in closure):
```js
window.collapseAllGroups = function() {
  const groups = document.querySelectorAll('.excel-calendar__group-header');
  groups.forEach(header => {
    const groupId = header.dataset.groupId;
    if (groupId) toggleGroup(groupId, true);  // force collapse
  });
};

window.expandAllGroups = function() {
  const groups = document.querySelectorAll('.excel-calendar__group-header');
  groups.forEach(header => {
    const groupId = header.dataset.groupId;
    if (groupId) toggleGroup(groupId, false);  // force expand
  });
};
```

**Visibility:** Only show the buttons when `CalendarData.Groups != null` (groups exist).

**Placement:** Position inline with the mode tabs (By Shift / By User) since they're view-manipulation controls, not filters. This avoids adding a new toolbar row and keeps related controls together. On the condensed single-row toolbar (#11), they flow naturally after the mode tabs.

**Scope:** `excel-calendar-groups.js` (functions), `Default.cshtml` or individual calendar pages (buttons), `calendar.css` (button styling), `SharedResources.resx` + `.he-IL.resx` (localized labels).

---

## Files Summary

| File | Items |
|------|-------|
| `wwwroot/css/components.css` or `site.css` | #1 (sticky header/col utility classes) |
| `Pages/Admin/Users.cshtml` | #1 (Tier 1 classes), #5 (reorder form), #6 (search input) |
| `Pages/Admin/Organization/Grants/Index.cshtml` | #1 (Tier 1 classes) |
| `Pages/Admin/Organization/Roles/Index.cshtml` | #1 (Tier 1 classes) |
| `Pages/Admin/AuditLog.cshtml` | #1 (Tier 2 class) |
| `Pages/Owner/DatabaseConsole.cshtml` | #1 (Tier 2 class) |
| `Pages/Owner/Telemetry.cshtml` | #1 (Tier 2 class) |
| `wwwroot/css/calendar.css` | #2 (toolbar), #8 (empty cells), #11 (condense toolbar), #13 (group color) |
| `_Layout.cshtml` or breadcrumb CSS | #2 (breadcrumb padding) |
| `Pages/Calendar/Shifts.cshtml.cs` | #3 (shift color), #4 (weekly hours), #13 (group color+count) |
| `ViewComponents/ExcelCalendarTableViewComponent.cs` | #4 (WeeklyHours), #13 (Group.Color) |
| `ExcelCalendarTable/Default.cshtml` | #4 (total column), #14 (buttons) |
| `ExcelCalendarTable/_CalendarRow.cshtml` | #4 (hours cell) |
| `Pages/Admin/Users.cshtml` | #5 (reorder form), #6 (search input) |
| `Pages/Requests/Index.cshtml` + `.cs` | #7 (tab counts) |
| `Pages/Home/Index.cshtml` | #9 (cards), #10 (actions) |
| `wwwroot/js/excel-calendar-groups.js` | #14 (collapse/expand all) |
| `Resources/SharedResources.resx` + `.he-IL.resx` | #7, #10, #14 (labels) |

---

## Verification Plan

1. **Sticky headers:** Scroll Grants page (100+ rows) → header + first column stay visible. Verify corner cell renders correctly in dark mode.
2. **Breadcrumb:** Measure vertical space above calendar table (target: <220px). Also verify Admin/Config and Requests pages don't feel too cramped.
3. **Color chips:** Overview shows colored chips matching legend. Verify dark mode chips are readable (opacity 0.25).
4. **Weekly hours:** Shifts user-mode shows "Xh" in last column with tabular-nums alignment. Verify `TimeHelpers.MergeAndSumHours` gives same results as the old private copy.
5. **Add User:** "+ Add" form expands above table, not below.
6. **Search:** Type name → table filters instantly. Verify Add User form stays visible during search.
7. ~~Tab counts~~ RESOLVED — already implemented.
8. **Empty cells:** Dotted border on empty cells. Test with 7×20 grid — verify not visually noisy. Verify coexistence with "+" hover affordance.
9. **Dashboard cards:** Uniform 120px height, redundant company metrics merged.
10. **Quick actions:** "Go to Calendar" btn--md above stat cards.
11. **Toolbar:** Single row on desktop with vertical separator between selector/action groups. 2 rows on mobile.
12. (Resolved — no change)
13. **Group headers:** Colored left border matching ShiftType.RowColor + "(N)" member count.
14. **Collapse/Expand:** ▲/▼ buttons inline with mode tabs, only visible when groups exist. Click collapses/expands all with localStorage persistence.

### Cross-cutting verification
- All items: test in Hebrew RTL + English LTR
- All items: test in light + dark mode
- All items: test with sidebar open + collapsed
- Calendar items: test with 0, 3, and 20+ users
