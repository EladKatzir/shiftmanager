# SHELVED FEATURE: Calendar Views (Shift-based, User-based, CU-1 Excel Grid)
**Date Shelved:** 2026-01-24
**Reason:** Discovered critical architectural flaw - organizational hierarchy must be redesigned first
**Status:** SHELVED pending completion of Organizational Hierarchy Redesign (Project → Area → Molecule → Department → Company)

---

## Why This Was Shelved

During brainstorming for calendar view features (Questions 1-40), we discovered the current single-level "Company" organizational model is insufficient for the real-world operational structure. The proposed features assumed:

- Multi-level hierarchy (Project → Area → Molecule → Department → Company)
- Job-based permissions (JobType + Scope + Actions)
- Grant system for explicit permission management
- Duty system (Responsibility Duties + Coverage Duties)
- Molecule-scoped chores
- Department-scoped JobType calendars

**Current model has:**
- Single level: Company only
- Role-based permissions (Owner/Manager/Director/Employee enum)
- No JobType entity (free text JobTitle field)
- No Department entity (free text Department field)
- OnDuty is global (confuses Responsibility vs Coverage)

**Decision:** Rebuild organizational foundation first, THEN implement calendar views on correct architecture.

---

## Complete Brainstorming Record (Q1-Q39)

### Executive Summary of Decisions

All 39 brainstorming questions were answered and documented in:
- `docs/planner/2026-01-23-calendar-views-brainstorming-complete.md` (Questions 1-30)
- Extended brainstorming (Questions 31-39) documented in task tracker

**Key decisions made:**

#### **View Selection & Toggle (Q1-Q3)**
- ✅ Flip icon (↔️ or 🔄) in header to toggle between Shift-based and User-based views
- ✅ User preference saved (LocalStorage + database)
- ✅ Icon size: 24px square matching existing toolbar icons
- ✅ CU-1 Excel Grid as separate top navigation item (not view toggle)

#### **Data Consistency (Q4-Q7)**
- ✅ Single source of truth: ShiftInstances database
- ✅ Views are UI representations only (same data, different layouts)
- ✅ Air-gapped environment support via local sync (no external dependencies)
- ✅ Real-time updates via SignalR in connected environments

#### **Programs/Blueprints Integration (Q16-Q17)**
- ✅ Programs populate shifts across all calendar views
- ✅ User-based view: Roster dock drops shifts (adaptive behavior)
- ✅ CU-1: Can overwrite program shifts (manual edit takes precedence)
- ✅ Re-apply program: Repopulate cell if edited

#### **Color System (Q13-Q15)**
- ✅ Assignment type colors WCAG AAA compliant (7:1 contrast)
- ✅ Soft, readable colors validated via Playwright screenshots
- ✅ Consistent across entire project (don't interfere with "unavailable" red)
- ✅ CSS custom properties in `:root`:
  ```css
  --assignment-shift: #1565C0;      /* Deep blue */
  --assignment-chore: #E65100;      /* Deep orange */
  --assignment-onduty: #00695C;     /* Deep teal */
  --assignment-vacation: #6A1B9A;   /* Deep purple */
  --assignment-unavailable: #C62828; /* Deep red */
  ```

#### **Radar Mode (Q22-Q23)**
- ✅ Conflict detection with green tick to mark "already known and intentional"
- ✅ Triple-coded conflicts: Color + Letter prefix + Background pattern (accessibility)
- ✅ Auto-refresh every 30 seconds
- ✅ Overlap warnings with manager confirmation prompt

#### **Concurrent Editing (Q19)**
- ✅ Hybrid approach:
  - Different cells: Smart merge (both changes applied)
  - Same cell: Optimistic locking with conflict dialog
  - Real-time lock indicators (cell highlighted when another user editing)
- ✅ Editing counted from menu selection moment (Shift/Vacation/Chore picker)

#### **Performance (Q31-Q32)**
- ✅ Virtualized rendering for large teams (500+ users)
- ✅ Render visible rows + 20-row buffer
- ✅ User's main team always pinned to top
- ✅ Date range limits: 31 days default, 90 days power user mode

#### **Edge Cases (Q33-Q35)**
- ✅ Network failure: Immediate error with manual retry button + persistent banner
- ✅ Orphaned assignments: Background cleanup job with notifications to original assigners
- ✅ Invalid data: Graceful degradation with "[Deleted User]" / "[Unknown Shift]" placeholders

#### **Accessibility (Q36-Q38)**
- ✅ ARIA Grid Pattern for screen readers
- ✅ Pragmatic keyboard navigation (arrow keys, number shortcuts)
- ✅ Triple-coded assignments: Color + Letter + Pattern (color blindness support)
- ✅ High contrast mode support

#### **Testing Strategy (Q39)**
- ✅ Maximum testing coverage:
  - **Unit tests:** All business logic (conflict detection, validation, smart merge)
  - **Integration tests:** Full workflows including concurrent scenarios
  - **E2E tests:** Expand existing Playwright suite with new view scenarios

#### **Security (Q40 - Partially Complete)**
- ✅ View flipping: Everyone can flip (personal preference)
- ✅ CU-1 access: Everyone view, only managers edit
- ✅ Conflict acknowledgement: Any manager in team
- ✅ Export data: Row-level permissions + watermark
- ✅ Rate limiting: Conservative (120 req/hr Radar) with friendly notification
- ✅ XSS prevention: Server-side encoding
- ✅ CSRF protection: Antiforgery tokens + SameSite cookies
- ✅ Audit logging: Comprehensive (H3) - overhaul existing audit page
- ✅ Conflict tooltips: No restrictions (trust air-gapped users)

---

## What Was Being Built (Feature Specifications)

### **1. View Toggle System**

#### **Shift-based View (Default)**
- Excel-like grid layout
- Columns: Shift types (Morning, Afternoon, Night)
- Rows: Dates
- Cells: Assigned users with avatars
- Drag-and-drop assignment
- Roster dock on right
- Fill handle for quick multi-day assignment

#### **User-based View (Flip)**
- Rows: Users (alphabetically sorted)
- Columns: Dates
- Cells: Assigned shifts with time badges
- Roster dock adaptive (drops shifts when clicked)
- Assignment chips (time + shift name + remove button)

#### **CU-1 Excel Grid (Separate Page)**
- Real Excel-like interface with blocks
- Export to .xlsx (EPPlus)
- Export to .png (html2canvas)
- Virtualized scrolling for 500+ users
- User's main team pinned to top
- Manual edits can override program assignments

### **2. Context Sidebar (Right Side)**
- Mini calendar widget (date navigation)
- Team selector with avatar
- Filter sections (shifts, chores, vacations)
- Empty state messaging with icons

### **3. Assignment Mode Toggle**
- "Assign by Users" mode: Day boxes with search, assignment chips
- "Assign by Shifts" mode: Shift selector, weekly grid per shift type

### **4. Fairness & Statistics Dashboard**
- Tabs: General Summary, Fairness Table, Shift Status
- Per-user workload counts (shifts, chores, vacations, hours)
- Deviation badges (color-coded: red=over, green=under)
- Coverage percentages per shift type
- Date range selector (week/month/quarter)

### **5. Enhanced Configuration UI**
- Stacked category rows (expand-to-edit)
- Day-of-week circular selectors
- Start/end time pickers
- Enable/disable toggle per shift type
- Archive button for unused types

### **6. Recurring Outings**
- User detail page with recurring outing rules
- Frequency: Weekly, Biweekly, Monthly, Custom
- Day-of-week selector
- Date range (start/end)
- Generates automatic vacation entries

---

## Design Mockups (Completed)

### Component Specifications Created:
1. **Mini Calendar Component** (`Pages/Shared/Components/MiniCalendar/`)
   - Month/year header with navigation
   - 7-column grid (Sunday-Saturday)
   - Selected day indicator (blue circle)
   - Today indicator (blue border)

2. **Assignment Chip Component** (CSS in `site.css`)
   - Inline-flex layout
   - Time + shift name text
   - × remove button (red circle)
   - Hover effect

3. **Day-of-Week Selector Component** (`Pages/Shared/Components/DaySelector/`)
   - 7 circular buttons (one per day)
   - Toggle selection with click
   - Selected state: primary color background

4. **Context Sidebar Component** (`Pages/Shared/Components/ContextSidebar/`)
   - Fixed right position (RTL: left)
   - 280px width
   - Sections: Calendar, Team, Filters, Shifts, Vacations
   - Empty states with icons

### CSS Design System Extensions:
```css
/* Weekly Calendar Grid */
.weekly-calendar-grid {
  display: grid;
  grid-template-columns: repeat(7, 1fr);
  gap: var(--space-md);
}

.weekly-day-column {
  background: var(--surface);
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
}

.weekly-day-column.selected {
  background: rgba(37, 99, 235, 0.05);
  border-color: var(--primary);
}

/* Assignment Chips */
.assignment-chip {
  display: inline-flex;
  align-items: center;
  gap: var(--space-xs);
  padding: var(--space-xs) var(--space-sm);
  background: var(--surface-soft);
  border: 1px solid var(--border);
  border-radius: var(--radius-2xl);
}

/* Day Selector */
.day-selector-btn {
  width: 2.5rem;
  height: 2.5rem;
  border: 2px solid var(--border);
  background: var(--surface);
  border-radius: 50%;
}

.day-selector-btn.selected {
  background: var(--primary);
  color: white;
  border-color: var(--primary);
}

/* Context Sidebar */
.app-context-sidebar {
  width: 280px;
  height: 100vh;
  background: var(--surface);
  border-left: 1px solid var(--border); /* RTL */
  padding: var(--space-lg);
}

/* Fairness Stats */
.stats-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
  gap: var(--space-lg);
}

.stat-card {
  background: var(--surface);
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  padding: var(--space-lg);
  text-align: center;
}

.deviation-badge.positive {
  background: rgba(220, 38, 38, 0.1);
  color: var(--danger);
}

.deviation-badge.negative {
  background: rgba(22, 163, 74, 0.1);
  color: var(--success);
}
```

---

## Backend Models Designed (Not Implemented)

### **RecurringOuting Model**
```csharp
public class RecurringOuting
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public RecurringFrequency Frequency { get; set; } // Weekly, Biweekly, Monthly
    public DayOfWeek? DayOfWeek { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public string Description { get; set; }

    public AppUser User { get; set; }
}

public enum RecurringFrequency
{
    Weekly,
    Biweekly,
    Monthly,
    Custom
}
```

### **FairnessStatistics Model**
```csharp
public class FairnessStatistics
{
    public int UserId { get; set; }
    public int ShiftCount { get; set; }
    public int ChoreCount { get; set; }
    public int VacationDays { get; set; }
    public decimal TotalHours { get; set; }
    public decimal Deviation { get; set; } // Percentage deviation from average

    public string Initials { get; set; }
    public string Name { get; set; }
}
```

### **WeekViewData Model**
```csharp
public class WeekViewData
{
    public DateOnly WeekStart { get; set; }
    public string WeekRange { get; set; } // "Jan 20 - Jan 26"
    public List<DayData> Days { get; set; } // 7 days
}

public class DayData
{
    public DateOnly Date { get; set; }
    public string DayName { get; set; }
    public int Number { get; set; }
    public bool IsSelected { get; set; }
    public int AssignedCount { get; set; }
    public decimal TotalHours { get; set; }
    public List<AssignmentSummary> Assignments { get; set; }
}
```

---

## Implementation Plan (Shelved)

### **Phase 1: Foundation & Components** (Weeks 1-2)
- [x] Design mini calendar component
- [x] Design assignment chip component
- [x] Design day-of-week selector
- [ ] Implement mini calendar (SHELVED)
- [ ] Implement context sidebar (SHELVED)

### **Phase 2: Weekly Calendar View** (Weeks 3-4)
- [x] Design weekly grid layout
- [x] Design assignment mode toggle
- [ ] Implement weekly view page (SHELVED)
- [ ] Implement context sidebar integration (SHELVED)

### **Phase 3: Assignment Modes** (Weeks 5-6)
- [x] Design "Assign by Users" view
- [x] Design "Assign by Shifts" view
- [ ] Implement day boxes with search (SHELVED)
- [ ] Implement shift selector grid (SHELVED)

### **Phase 4: Fairness Dashboard** (Weeks 7-8)
- [x] Design statistics cards
- [x] Design fairness table
- [x] Design shift status view
- [ ] Implement FairnessCalculationService (SHELVED)
- [ ] Implement dashboard UI (SHELVED)

### **Phase 5: Enhanced Configuration** (Weeks 9-10)
- [x] Design stacked category UI
- [x] Design expand-to-edit pattern
- [ ] Implement shift configuration page (SHELVED)

### **Phase 6: Recurring Outings** (Weeks 11-12)
- [x] Design recurring outing modal
- [x] Design rule editor UI
- [ ] Implement RecurringOutingService (SHELVED)
- [ ] Implement user detail enhancements (SHELVED)

### **Phase 7: Integration & Localization** (Weeks 13-14)
- [ ] Add all localization keys (SHELVED)
- [ ] Test RTL layout (SHELVED)

### **Phase 8: Testing & Polish** (Weeks 15-16)
- [ ] E2E test scenarios (SHELVED)
- [ ] Performance optimization (SHELVED)
- [ ] Accessibility audit (SHELVED)

---

## Files Created During Brainstorming

### Documentation:
- ✅ `docs/planner/2026-01-23-calendar-views-brainstorming-complete.md` (40,000+ words, Q1-Q30)
- ✅ `docs/planner/SHELVED-2026-01-24-calendar-views-feature.md` (this file)

### Design Mockups (Code Snippets):
- ✅ Mini Calendar Component specification
- ✅ Assignment Chip CSS
- ✅ Day Selector Component specification
- ✅ Context Sidebar Component specification
- ✅ Weekly Calendar Grid CSS
- ✅ Fairness Dashboard CSS

### Backend Models (Designed, Not Implemented):
- ✅ RecurringOuting model
- ✅ FairnessStatistics model
- ✅ WeekViewData model

---

## Why This Was the Right Decision

Implementing calendar views on the current single-level "Company" model would have created:

1. **Technical Debt:** Would need complete rewrite when org hierarchy added
2. **Incorrect Permissions:** Role-based (Manager/Director) doesn't match real-world (Job-based grants)
3. **Confused Calendars:** "Show shifts for my team" has no meaning without JobType entity
4. **OnDuty Ambiguity:** Mixing Responsibility Duties (Lead) with Coverage Duties (Hakam On-Call)
5. **Scalability Issues:** Single-level Company can't handle Molecule/Department/Area hierarchy

**By stopping now:**
- Preserve all design decisions (this document)
- Build on correct foundation
- Avoid throwing away implemented code
- Deliver better product faster (no rewrite cycles)

---

## When to Resume This Feature

**Prerequisites before resuming:**
1. ✅ Organizational hierarchy implemented (Project → Area → Molecule → Department → Company)
2. ✅ JobType entity created and populated
3. ✅ Grant system implemented
4. ✅ Duty System separated (Responsibility Duties + Coverage Duties)
5. ✅ Chore scoping changed to Molecule-level
6. ✅ Calendar model updated to Scope + JobType pattern

**Then adapt this feature:**
- "Shift-based view" becomes **"Company Job Calendar"** (e.g., Defence North — Recorder)
- "User-based view" becomes same calendar, different UI layout
- "CU-1 Excel Grid" becomes **"Department Job Rollup Calendar"** (e.g., Defence Dept — Recorder)
- "Show my items" becomes **"My Shifts"** personal calendar
- Context sidebar filters by JobType + Molecule/Department scope
- Fairness dashboard scoped to Department + JobType

---

## Contact for Questions

If resuming this feature in the future, refer to:
- This document (complete feature specification)
- `docs/planner/2026-01-23-calendar-views-brainstorming-complete.md` (Q1-Q30 decisions)
- Original UI/UX integration plan: `C:\Users\katzi\.claude\plans\fuzzy-jumping-dove.md`

**Date Archived:** 2026-01-24
**Next Step:** Implement Organizational Hierarchy Redesign (Priority 1)
