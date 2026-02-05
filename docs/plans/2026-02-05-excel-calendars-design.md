# Excel-Like Table Calendars - UI/UX Design Document

**Date:** 2026-02-05
**Status:** Design Complete - Ready for Implementation
**Author:** Brainstorming Session

---

## Executive Summary

Replace all existing calendar pages with 4 new Excel-like table calendars:
- **Chores Calendar** - Molecule scoped, ChoreType-based rows
- **Shifts Calendar** - JobType + Molecule scoped (PRIMARY)
- **On-Call Calendar** - Area scoped, with primary + backup
- **Company Overview** - Company scoped, view-only with free-text notes

A beautiful landing page at `/Calendar` will serve as the entry point with illustrated cards for each calendar type.

---

## 1. Navigation & Landing Page

### 1.1 Landing Page (`/Calendar`)

**Purpose:** Visually impressive entry point explaining each calendar type.

**Layout:**
```
┌─────────────────────────────────────────────────────────────┐
│                      📅 לוחות שנה                           │
│              ניהול משמרות, תורנויות וכוננויות                │
├─────────────────────────────────────────────────────────────┤
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐
│  │ [Gradient]  │  │ [Gradient]  │  │ [Gradient]  │  │ [Gradient]  │
│  │ [Illustr.]  │  │ [Illustr.]  │  │ [Illustr.]  │  │ [Illustr.]  │
│  │   משמרות    │  │  תורנויות   │  │  כוננויות   │  │   סקירה    │
│  │ Description │  │ Description │  │ Description │  │ Description │
│  │ [צפה בלוח→] │  │ [צפה בלוח→] │  │ [צפה בלוח→] │  │ [צפה בלוח→] │
│  └─────────────┘  └─────────────┘  └─────────────┘  └─────────────┘
└─────────────────────────────────────────────────────────────┘
```

**Card Design (Premium):**
- **Background:** Unique gradient per card using token palette
  - Shifts: `--shift-night` → `--accent` (navy → sky)
  - Chores: `--camo-olive` → `--camo-sand` (olive → sand)
  - On-Call: `--shift-hakam` → `--camo-sand` (brown → sand)
  - Overview: `--camo-slate` → `--surface-soft` (slate → light)
- **Illustration:** Custom SVG military-style illustration per card
- **Hover Effect:** Subtle lift (`translateY(-4px)`) + shadow increase
- **Responsive:** 2x2 grid on desktop, stacked on smaller screens

### 1.2 Inter-Calendar Navigation

- No tabs between calendars - separate pages
- Each calendar has breadcrumb back to landing page
- Preserves date range when navigating (via query params)

---

## 2. Shared UI Components

### 2.1 Excel Table Structure

```
┌────────┬──────────┬──────────┬──────────┬──────────┐
│        │ ראשון   │ שני     │ שלישי   │ רביעי   │  ← Sticky header
│        │ 05/01   │ 06/01   │ 07/01   │ 08/01   │
├────────┼──────────┼──────────┼──────────┼──────────┤
│ Row 1  │  Cell    │  Cell    │  Cell    │  Cell    │  ← Sticky first col
├────────┼──────────┼──────────┼──────────┼──────────┤
│ Row 2  │  Cell    │  Cell    │  Cell    │  Cell    │
└────────┴──────────┴──────────┴──────────┴──────────┘
```

**Sticky Behavior:**
- Header row: `position: sticky; top: 0; z-index: 20;`
- First column: `position: sticky; left: 0; z-index: 10;`
- Corner cell: `z-index: 30;`

**Date Header Format:**
| View | Format |
|------|--------|
| Week | Full day name (ראשון) + DD/MM |
| 2 Weeks | Full day name + DD/MM |
| Month | Abbreviated (א׳) + DD/MM |

**Day Highlighting:**
| View | Today | Weekend |
|------|-------|---------|
| Week | Left border `--primary` | None |
| 2 Weeks | Left border `--primary` | None |
| Month | Left border `--primary` | Shabbat tint `--surface-soft`, Friday lighter |

### 2.2 Toolbar Layout

```
┌─────────────────────────────────────────────────────────────────┐
│ [שבוע ▼] [01/01/26-07/01/26] [📅] [🖨️]  |  [🔍 סינון ▼] [👤 רק שלי] │
└─────────────────────────────────────────────────────────────────┘
```

**Components:**
- **Time Range Dropdown:** שבוע / שבועיים / חודש
- **Date Range Display:** Compact DD/MM/YY format, clickable
- **Date Picker Button:** Opens compact calendar picker
- **Print Button:** Uses existing print.css logic
- **Filter Button:** Opens advanced filter panel, shows count badge when active
- **"Just Mine" Toggle:** Quick filter to current user's rows

**Shifts Calendar Additional:**
```
[👥 לפי משתמש ⟳] ← Cycle toggle between shift-based and user-based view
```

**Capacity Mode (Shifts only):**
```
[📊 מצב קיבולת] ← Toggle to enter capacity editing mode
```

### 2.3 Cell Density

| Calendar | Week View | Month View |
|----------|-----------|------------|
| Shifts | Expanded (name, role, badges) | Compact (name + badge dots) |
| Chores | Expanded | Compact |
| On-Call | Expanded | Semi-compact (name, role) |
| Overview | Expanded | Expanded |

**Common Elements:**
- **Hover:** Always reveals full details tooltip
- **⊕ Button:** Appears on cell hover (top-left corner), opens detail popover

### 2.4 Cell Editor Popover

**Positioning:**
- Opens to the **left** of cell in RTL mode
- Smart edge detection - flips when near viewport boundary
- Stays anchored during scroll

**Structure:**
```
┌─────────────────────────────────┐
│ ⚠️ מנוחה < 8 שעות מול "לילה"   │  ← Warning banner (if applicable)
│    אתמול                        │
├─────────────────────────────────┤
│ 🔍 חיפוש...                     │  ← User search
├─────────────────────────────────┤
│ 👤 יוסי כהן ⚠️🏖️                │  ← User list with badges
│ 👤 דנה לוי ✓                    │
│ 👤 משה ישראלי 🔧📅              │
├─────────────────────────────────┤
│ [ ביטול ]        [ שמור ]       │  ← Actions
│                  [ שמור בכל זאת ]│  ← When warnings exist
└─────────────────────────────────┘
```

**User Picker:**
- Compact spacing (badges directly after name)
- Badge meanings:
  - ⚠️ Conflict (rest violation, already assigned)
  - 🏖️ Vacation
  - 🧹 Chore
  - 📞 On-duty
  - 📅 Other shift
  - ✓ Available
- Future-ready: Structure supports shift count for analytics

### 2.5 FYI Badge Colors

| Status | Token | Badge |
|--------|-------|-------|
| Vacation | `--info` (blue) | 🏖️ |
| Chore | `--warning` (yellow) | 🧹 |
| On-Duty | `--camo-hakam` (brown) | 📞 |
| Other Shift | `--text-muted` (gray) | 📅 |

### 2.6 Row Groupings (Shifts Calendar)

**Visual Design:**
```
┌─────────────────────────────────────────────────────────┐
│ ▼ צפון                                            ⋮⋮   │  ← Group header
├─────────────────────────────────────────────────────────┤
│ בוקר  │ יוסי כהן │ דנה לוי │ ...                       │
│ לילה  │ משה י.   │ רונית א.│ ...                       │
├─────────────────────────────────────────────────────────┤
│ ▶ דרום (מכווץ)                                    ⋮⋮   │  ← Collapsed
├─────────────────────────────────────────────────────────┤
│ ▼ טקטי                                            ⋮⋮   │
```

**Interactions:**
- **Chevron (▼/▶):** Click to collapse/expand
- **Group title:** Double-click to rename inline
- **Grip handle (⋮⋮):** Drag to reorder groups
- **"+" button:** At bottom, links to admin page for new group

**Data:**
- Stored in `ShiftGrouping.SortOrder`
- Global per (Molecule, JobType) - all users see same order

### 2.7 Filter Panel

```
┌─────────────────────────────┐
│ סינון                   [✕] │
├─────────────────────────────┤
│ חיפוש: [ __________ ]       │
│                             │
│ צוות:    [ כל הצוותים ▼ ]   │
│ דרגה:    [ כל הדרגות ▼ ]    │
│ תפקיד:   [ כל התפקידים ▼ ]  │
│                             │
│ ☐ הצג רק משובצים            │
│ ☐ הצג רק עם התנגשויות       │
│                             │
│ [ נקה הכל ]    [ החל ]      │
└─────────────────────────────┘
```

- Opens via filter button in toolbar
- Active filter count shown as badge: `[🔍 סינון (2) ▼]`

### 2.8 Loading & Empty States

**Loading:** Extend existing `CalendarSkeleton` for Excel table layout
- Pulsing gray cells matching table grid

**Empty:** Centered message with action
- "אין משמרות בטווח זה"
- Link to relevant admin page if applicable

---

## 3. Shifts Calendar (PRIMARY)

### 3.1 Route & Parameters

**Route:** `/Calendar/Shifts`

**Query Parameters:**
- `moleculeId` - Required or derived from user context
- `jobTypeId` - Required or derived
- `start` - Start date (DateOnly)
- `view` - week | 2weeks | month
- `mode` - shift | user

### 3.2 Views

**Shift-Based (Default):**
- Rows = Shift types from ShiftProgram blueprint
- Columns = Days
- Cells = Assigned users

**User-Based:**
- Rows = Users in scope
- Columns = Days
- Cells = Assigned shifts + FYI overlays

**Toggle:** Cycle button `[👥 לפי משתמש ⟳]`

### 3.3 Capacity Mode

Separate mode toggled via `[📊 מצב קיבולת]`

**Display in Capacity Mode:**
```
┌─────┬─────┬─────┬─────┐
│ בוקר │  2  │  3  │  2  │  ← Click to change
│ צהריים│  1  │  1  │  1  │
│ לילה │  2  │  2  │ ⚠️1 │  ← Warning if below assignments
└─────┴─────┴─────┴─────┘
```

**Override Flow:**
1. Click cell → number picker appears
2. If reducing below current assignments → warning dialog

### 3.4 Auto-Unassign Dialog

```
┌─────────────────────────────────────┐
│ ⚠️ הקטנת קיבולת                      │
├─────────────────────────────────────┤
│ הקיבולת החדשה (1) נמוכה מהשיבוצים   │
│ הנוכחיים (3). יש להסיר 2 משתמשים:   │
│                                     │
│ ☑ בחר הכל                           │
│ ─────────────────────────────────── │
│ ☑ יוסי כהן (שובץ אחרון)             │
│ ☑ דנה לוי                           │
│ ☐ משה ישראלי (שובץ ראשון)           │
│                                     │
│ [ ביטול ]          [ הסר נבחרים ]   │
└─────────────────────────────────────┘
```

- "בחר הכל" checkbox
- LIFO pre-selected (most recent first)
- Button disabled if selection < required removals

### 3.5 8-Hour Rest Warning

**Detection:** Check end-time of previous shift vs start-time of new shift

**Display:**
- Badge on user in dropdown: ⚠️
- Banner in popover: "⚠️ מנוחה < 8 שעות מול 'לילה' אתמול"
- **Never blocks** - warning only
- Save button changes to "שמור בכל זאת"
- Logged to audit when user confirms

### 3.6 Permissions (Grants)

| Action | Grant | Auto-Assigned Roles |
|--------|-------|---------------------|
| View | (all authenticated) | - |
| Assign users | `AssignShifts` | Lead, Admin |
| Change capacity | `ManageShiftCapacity` | Lead, Admin |
| Edit groupings | `ManageShiftGroupings` | Lead, Admin |

---

## 4. Chores Calendar

### 4.1 Route & Scope

**Route:** `/Calendar/Chores`
**Scope:** Molecule (cross-company)

### 4.2 Primary View: By People

- Rows = Users in molecule
- Columns = Days
- Cells = Assigned ChoreType(s)

### 4.3 ChoreType Model (New)

```csharp
public class ChoreType
{
    public int Id { get; set; }
    public int MoleculeId { get; set; }
    public string Name { get; set; }
    public string DisplayName { get; set; }
    public string? Color { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public int CreatedBy { get; set; }
}
```

**Migration:** Extract distinct chore names → seed ChoreTypes

### 4.4 Permissions (Grants)

| Action | Grant | Auto-Assigned Roles |
|--------|-------|---------------------|
| View | (all in molecule) | - |
| Assign chores | `AssignChores` | Lead, Admin |
| Manage ChoreTypes | `ManageChoreTypes` | Admin |

---

## 5. On-Call Calendar

### 5.1 Route & Scope

**Route:** `/Calendar/OnCall`
**Scope:** Area (cross-company)

### 5.2 Row Structure

```
│          │ א׳ 05/01        │ ב׳ 06/01        │
│ חקם      │ יוסי (ר׳) דנה   │ משה (ר׳) רונית  │
│ מוביל    │ אבי (ר׳) שרה    │ יעל (ר׳) דני    │
```

- One row per OnDutyType
- Each cell shows: Primary `(ר׳)` + Backup
- Admin can add new duty types

### 5.3 Future-Ready: Sub-Areas

**Current:** One חקם/מוביל per area

**Data Model Preparation:**
- Add optional `SubAreaId` to `OnDuty` / `OnDutyTypeConfig`
- When null → area-wide (current)
- When set → grouped view appears automatically

### 5.4 Permissions (Grants)

| Action | Grant | Auto-Assigned Roles |
|--------|-------|---------------------|
| View | (all in area) | - |
| Assign on-call | `AssignOnCall` | Lead, Admin |
| Manage OnDutyTypes | `ManageOnDutyTypes` | Admin |

---

## 6. Company Overview Calendar

### 6.1 Route & Scope

**Route:** `/Calendar/Overview`
**Scope:** Company (standard tenant filter)

### 6.2 Display

**View:** User-based only (rows = company users)

**Cell Content:**
```
│ יוסי כהן │ 🏖️   │ בוקר +1 │ לילה +2 │
│ דנה לוי  │ 🧹   │ צהריים  │ פנוי    │
│ משה י.   │ נסיעה│ פנוי    │ פנוי    │
```

| Cell State | Display |
|------------|---------|
| Has activities | Primary activity + "+N" for additional |
| Empty, no note | "פנוי" (muted text) |
| Empty, with note | Note text only (italic) |

**Hover:** Shows full list of activities

### 6.3 Free-Text Notes

**Who Can Edit:** Users with `WriteOverviewNotes` grant (auto: Lead, Admin)

**Flow:**
1. Click on "פנוי" cell
2. Small text input popover appears
3. Enter any free-form text
4. Save → replaces "פנוי" with note

**Data Model:**
```csharp
public class UserDayNote
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public DateOnly Date { get; set; }
    public int CompanyId { get; set; }
    public string Note { get; set; }
    public int CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
}
```

### 6.4 Permissions (Grants)

| Action | Grant | Auto-Assigned Roles |
|--------|-------|---------------------|
| View | (all in company) | - |
| Write notes | `WriteOverviewNotes` | Lead, Admin |

---

## 7. Interaction Patterns

### 7.1 Keyboard Navigation

| Key | Action |
|-----|--------|
| Arrow keys | Move between cells |
| Enter | Open cell editor popover |
| Escape | Close popover |
| Tab | Navigate popover fields |
| Delete | Clear assignment (with confirmation) |

### 7.2 Permission Denied

**Read-Only Mode:**
- Banner: "מצב צפייה בלבד"
- Cells: `cursor: default`, no hover highlight

**Force Edit Attempt:**
```
┌─────────────────────────────────────┐
│ ⛔ אין הרשאה                         │
├─────────────────────────────────────┤
│ אין לך הרשאות עריכה ללוח זה.         │
│ לקבלת הרשאות, פנה למפקדים שלך.       │
│                                     │
│              [ הבנתי ]              │
└─────────────────────────────────────┘
```

### 7.3 Error Handling

| Error Type | Display |
|------------|---------|
| Validation | Inline in popover, red text |
| Network | Toast notification with retry |
| Server (500) | Toast with "נסה שוב" button |
| Permission | Modal dialog |

**Success:** Green toast "✓ נשמר בהצלחה" (auto-dismiss 3s)

### 7.4 Real-Time Updates (SignalR)

- Hub for calendar change notifications
- Lead A saves → Lead B's view updates automatically
- Optimistic UI with rollback on error
- Fallback to polling if WebSocket fails

---

## 8. Technical Requirements

### 8.1 New Database Tables

```sql
-- Capacity overrides for specific days
CREATE TABLE ShiftCapacityOverride (
    Id INT PRIMARY KEY,
    ShiftTypeId INT NOT NULL,
    MoleculeId INT NOT NULL,
    JobTypeId INT NOT NULL,
    Date DATE NOT NULL,
    Capacity INT NOT NULL,
    CreatedBy INT NOT NULL,
    CreatedAt DATETIME NOT NULL
);

-- Chore type definitions
CREATE TABLE ChoreType (
    Id INT PRIMARY KEY,
    MoleculeId INT NOT NULL,
    Name NVARCHAR(100) NOT NULL,
    DisplayName NVARCHAR(100) NOT NULL,
    Color NVARCHAR(7),
    SortOrder INT NOT NULL DEFAULT 0,
    IsActive BIT NOT NULL DEFAULT 1,
    CreatedAt DATETIME NOT NULL,
    CreatedBy INT NOT NULL
);

-- Free-text notes for Overview calendar
CREATE TABLE UserDayNote (
    Id INT PRIMARY KEY,
    UserId INT NOT NULL,
    Date DATE NOT NULL,
    CompanyId INT NOT NULL,
    Note NVARCHAR(500) NOT NULL,
    CreatedBy INT NOT NULL,
    CreatedAt DATETIME NOT NULL
);
```

### 8.2 Table Modifications

```sql
-- Add to ShiftGrouping
ALTER TABLE ShiftGrouping ADD JobTypeId INT NULL;
ALTER TABLE ShiftGrouping ADD SortOrder INT NOT NULL DEFAULT 0;

-- Add to ShiftType
ALTER TABLE ShiftType ADD RowColor NVARCHAR(7) NULL;

-- Add to Chore
ALTER TABLE Chore ADD ChoreTypeId INT NULL;

-- Add to OnDutyTypeConfig (future-ready)
ALTER TABLE OnDutyTypeConfig ADD SubAreaId INT NULL;
```

### 8.3 New Grants

| Grant | Scope | Purpose |
|-------|-------|---------|
| `AssignShifts` | Molecule + JobType | Assign users to shifts |
| `ManageShiftCapacity` | Molecule + JobType | Override capacity |
| `ManageShiftGroupings` | Molecule + JobType | Edit grouping titles/order |
| `AssignChores` | Molecule | Assign chores to users |
| `ManageChoreTypes` | Molecule | CRUD chore types |
| `AssignOnCall` | Area | Assign on-call duties |
| `ManageOnDutyTypes` | Area | CRUD on-duty types |
| `WriteOverviewNotes` | Company | Add free-text notes |

### 8.4 New Services

```csharp
public interface IShiftCalendarService
{
    Task<List<AppUser>> GetUsersForCalendarAsync(int moleculeId, int jobTypeId);
    Task<List<ShiftInstance>> GetShiftInstancesAsync(int moleculeId, int jobTypeId, DateOnly start, DateOnly end);
    Task<List<ShiftAssignment>> GetAssignmentsAsync(int moleculeId, int jobTypeId, DateOnly start, DateOnly end);
    Task<int> GetCapacityAsync(int shiftTypeId, int moleculeId, int jobTypeId, DateOnly date);
    Task SetCapacityOverrideAsync(int shiftTypeId, int moleculeId, int jobTypeId, DateOnly date, int capacity);
    Task<AssignmentResult> AssignUserAsync(int shiftInstanceId, int userId, int assignedBy);
    Task<List<RestViolationWarning>> CheckRestViolationsAsync(int userId, DateOnly date, int shiftTypeId);
    Task<Dictionary<(int UserId, DateOnly Date), FyiOverlayData>> GetOverlaysAsync(int moleculeId, DateOnly start, DateOnly end);
}
```

### 8.5 SignalR Hub

```csharp
public class CalendarHub : Hub
{
    Task NotifyAssignmentChanged(int calendarType, int scopeId, DateOnly date);
    Task NotifyCapacityChanged(int shiftTypeId, DateOnly date);
    Task NotifyNoteChanged(int userId, DateOnly date);
}
```

---

## 9. Accessibility (WCAG AA)

- Color contrast verified in tokens.css
- `role="grid"` on table, `role="gridcell"` on cells
- `aria-label` on all interactive elements
- `aria-live="polite"` for dynamic announcements
- Focus trap in popovers
- Visible focus indicators
- Skip navigation links

---

## 10. Mobile Support

Minimal - horizontal scroll only:
- Sticky first column maintained
- Touch targets minimum 48px
- No special mobile layout
- Functional on tablet, basic on phone

---

## 11. Feature Flags

```json
{
  "Features": {
    "ExcelCalendars": false,
    "ExcelCalendarShifts": false,
    "ExcelCalendarChores": false,
    "ExcelCalendarOnCall": false,
    "ExcelCalendarOverview": false
  }
}
```

---

## 12. Redirects

```csharp
app.MapGet("/Calendar/Month", () => Results.Redirect("/Calendar/Shifts"));
app.MapGet("/Calendar/Week", () => Results.Redirect("/Calendar/Shifts"));
app.MapGet("/Calendar/Day", () => Results.Redirect("/Calendar/Shifts"));
app.MapGet("/Calendar/Table", () => Results.Redirect("/Calendar/Shifts"));
app.MapGet("/Chores/Calendar", () => Results.Redirect("/Calendar/Chores"));
app.MapGet("/Public/OnDuty", () => Results.Redirect("/Calendar/OnCall"));
app.MapGet("/Public/Chores", () => Results.Redirect("/Calendar/Chores"));
```

---

## 13. Implementation Order

1. **Database schema + migration**
2. **Shared UI components** (ExcelTable, CellEditor, Toolbar)
3. **Shifts Calendar** (PRIMARY)
4. **Chores Calendar**
5. **On-Call Calendar**
6. **Overview Calendar**
7. **Landing Page**
8. **SignalR real-time updates**
9. **Redirects + cleanup old pages**

---

## Appendix: Design Decisions Summary

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Cell editing | Popover (RTL-aware) | Lightweight, maintains context |
| Cell density | Adaptive per calendar/view | Balance info vs scanability |
| Capacity editing | Separate mode | Clean separation of concerns |
| Group ordering | Global | Consistency for team coordination |
| Time picker | Dropdown + compact date | Space efficient |
| Real-time | SignalR | Works in air-gapped, best UX |
| Permissions | Grant-based always | Flexibility without role changes |
| Mobile | Horizontal scroll only | Low priority for air-gapped desktop |
| Warnings | Never block, always warn | Operational flexibility |
