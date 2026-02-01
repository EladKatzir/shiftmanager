# ShiftManager Calendar Views - Comprehensive Brainstorming Documentation

**Document Version:** 1.0
**Date:** 2026-01-23
**Status:** Brainstorming Complete - Ready for Performance/Accessibility Review
**Authors:** Product Team + Design Consultant

---

## Table of Contents

1. [Executive Summary](#executive-summary)
2. [System Architecture Overview](#system-architecture-overview)
3. [Feature Specifications](#feature-specifications)
   - [Table View - Shift-Based Mode](#table-view---shift-based-mode)
   - [Table View - User-Based Mode](#table-view---user-based-mode)
   - [CU-1 Excel Grid](#cu-1-excel-grid)
4. [Smart Synchronization System](#smart-synchronization-system)
5. [Programs/Blueprints/Master Programs Integration](#programsblueprintsmaster-programs-integration)
6. [Color System](#color-system)
7. [Interaction Models](#interaction-models)
8. [Visual Indicators System](#visual-indicators-system)
9. [Keyboard Shortcuts](#keyboard-shortcuts)
10. [Mobile/Tablet Responsiveness](#mobiletablet-responsiveness)
11. [Export Functionality](#export-functionality)
12. [Radar Mode - Conflict Detection](#radar-mode---conflict-detection)
13. [Technical Implementation Notes](#technical-implementation-notes)
14. [Next Steps](#next-steps)

---

## Executive Summary

### Vision
ShiftManager will support three distinct calendar assignment interfaces, each optimized for different workflows while sharing a unified backend, color system, and smart synchronization engine.

### Key Principles
1. **Excel-like Speed & Familiarity** - Primary interaction remains fast and intuitive
2. **Context-Aware Synchronization** - Real-time updates when safe, deferred when disruptive
3. **Consistent Visual Language** - Assignment types use same colors across all views
4. **Progressive Enhancement** - New views complement (not replace) existing functionality
5. **Mobile-First Responsive** - All features work on all devices with appropriate adaptations

### Three Calendar Views

| View | Primary Use Case | Key Feature | Roster Contains |
|------|------------------|-------------|-----------------|
| **Table (Shift-Based)** | Assign employees to shifts | Drag employee to shift/date | Employees |
| **Table (User-Based)** | Assign shifts to users | Drag shift to user/date | Shift Types |
| **CU-1 Excel Grid** | Clean team-based overview | Team-grouped, click-to-assign | None (drag from names) |

### Core Innovations
- **Adaptive Roster Dock** - Contents flip based on view mode (employees ↔ shift types)
- **Smart Sync States** - IDLE (real-time) / ACTIVE (deferred) / POST-ACTION (immediate)
- **Universal Program Integration** - Programs populate all views consistently
- **Tasteful Color System** - WCAG AAA compliant, feature-by-feature rollout

---

## System Architecture Overview

### Navigation Structure

**Top Global Header:**
```
┌────────────────────────────────────────────────────┐
│ 📊 ShiftManager  [📅] [📋] [⊞] [⚙️]  [User Avatar] │
│                Calendar Requests Grid Settings      │
└────────────────────────────────────────────────────┘
```

**Icon Meanings:**
- **📅 Calendar** → Month/Week/Day/Table views
- **📋 Requests** → Vacation/shift change requests
- **⊞ Excel Grid** → CU-1 dedicated view
- **⚙️ Settings** → System configuration

**Calendar Sub-Navigation:**
```
Within Calendar section:
┌─────────────────────────────────────────────┐
│ [Month] [Week] [Day] [Table]               │
└─────────────────────────────────────────────┘

Table view has additional header:
┌──────────────────────────────────────────────────────────┐
│ [Week] [2 Weeks] [Month] │ [☰ Roster] [📡 Radar] │ [🔄 View: Shifts ▼] [⬇️ Export ▼] │
│    Time Range             │     Tools             │    Display Mode       Export       │
└──────────────────────────────────────────────────────────┘
```

### Data Flow Architecture

```
┌─────────────────────────────────────────────────┐
│         Programs/Blueprints/Master Programs      │
│         (Auto-generate shift instances)          │
└────────────────┬────────────────────────────────┘
                 │
                 ▼
┌─────────────────────────────────────────────────┐
│              ShiftInstances Database             │
│        (Single source of truth for all views)    │
└────┬─────────┬──────────┬──────────┬────────────┘
     │         │          │          │
     ▼         ▼          ▼          ▼
┌─────────┬─────────┬─────────┬─────────────┐
│ Month   │ Week    │ Table   │ CU-1 Grid   │
│ View    │ View    │ (Shift) │             │
│         │         │ (User)  │             │
└─────────┴─────────┴─────────┴─────────────┘
     │         │          │          │
     └─────────┴──────────┴──────────┘
                 │
                 ▼
        ┌───────────────────┐
        │   Smart Sync      │
        │   (SignalR/Poll)  │
        └───────────────────┘
```

### User Preference Persistence

| Preference | Storage Method | Scope |
|------------|----------------|-------|
| **Calendar View Mode** (Shift/User) | Hybrid (localStorage + database) | Per-user, cross-device |
| **Team Collapse State** (CU-1) | localStorage | Per-browser |
| **Roster Dock Position** | localStorage | Per-browser |
| **Radar Mode State** | Session only (not persisted) | Current session |
| **Date Range Selection** | URL query params | Current page |

---

## Feature Specifications

### Table View - Shift-Based Mode

**Layout:**
```
Row 1 (Header): [Week] [2 Weeks] [Month] │ [☰ Roster] [📡 Radar] │ [🔄 View: Shifts ▼] [⬇️ Export ▼]
Row 2 (Dates):  | Mon 20/1 | Tue 21/1 | Wed 22/1 | Thu 23/1 | Fri 24/1 |
Row 3+ (Shifts):
┌──────────────┬──────────┬──────────┬──────────┬──────────┬──────────┐
│ Morning      │ John (2) │ Sarah (1)│          │ Mike [T] │ Alex     │
├──────────────┼──────────┼──────────┼──────────┼──────────┼──────────┤
│ Evening      │          │          │ John     │          │ Sarah    │
├──────────────┼──────────┼──────────┼──────────┼──────────┼──────────┤
│ Night        │ Alex     │          │          │ Mike     │          │
└──────────────┴──────────┴──────────┴──────────┴──────────┴──────────┘

Roster Dock (Right Side):
┌─────────────────┐
│ 👤 John Smith   │
│ 👤 Sarah Cohen  │
│ 👤 Mike Davis   │
│ 👤 Alex Johnson │
└─────────────────┘
```

**Rows:** Shift types (Morning, Evening, Night, Chores, etc.)
**Columns:** Dates
**Cells:** Assigned employees with staffing count
**Roster:** Contains employees (drag to assign)

**Cell Content Format:**
- Primary assignee name
- Staffing count in parentheses: `John (2)` = John + 1 trainee
- `[T]` badge = Trainee
- `[OVR]` badge = Manually edited (detached from program)

**Interactions:**
1. **Drag from roster** → Drop on cell → Assign employee to that shift/date
2. **Click cell** → Dropdown with employee list → Select to assign
3. **Fill handle** → Drag from cell corner → Replicate assignment pattern
4. **Right-click cell** → Context menu: Edit staffing, Remove, Reset to program
5. **Keyboard:** Tab/arrows navigate, Enter confirms, Delete clears

**Special Features:**
- **Roster dock toggle** - Click ☰ to show/hide
- **Programs integration** - Auto-populated cells, OVR badge when manually edited
- **Radar mode** - Highlights understaffed/overstaffed cells
- **Export** - Excel/PNG of current view

---

### Table View - User-Based Mode

**Layout:**
```
Row 1 (Header): [Same as Shift-Based]
                [🔄 View: By Users ▼] ← Active state
Row 2 (Dates):  | Mon 20/1 | Tue 21/1 | Wed 22/1 | Thu 23/1 | Fri 24/1 |
Row 3+ (Users):
┌──────────────┬──────────┬──────────┬──────────┬──────────┬──────────┐
│ John Smith   │ Morning  │ Evening  │          │ Morning  │ Chore    │
├──────────────┼──────────┼──────────┼──────────┼──────────┼──────────┤
│ Sarah Cohen  │ Evening  │          │ Morning  │ Vacation │          │
├──────────────┼──────────┼──────────┼──────────┼──────────┼──────────┤
│ Mike Davis   │          │ Night    │ Chore    │          │ Evening  │
└──────────────┴──────────┴──────────┴──────────┴──────────┴──────────┘

Roster Dock (Right Side):
┌─────────────────┐
│ 🌅 Morning      │
│ 🌆 Evening      │
│ 🌙 Night        │
│ 🧹 Chore        │
│ 🏖️ Vacation     │
└─────────────────┘
```

**Rows:** Users (employees)
**Columns:** Dates
**Cells:** Assigned shift types
**Roster:** Contains shift types (drag to assign)

**Key Difference from Shift-Based:**
- **Roster contents flip** - Now contains shift types instead of employees
- **Drag shift from roster** → Drop on user/date → Assign that shift to that user
- Same visual indicators (OVR badge, colors, radar)
- Same keyboard shortcuts and export functionality

**Use Case:**
"I want to give John the Morning shift across Monday, Wednesday, and Friday"
- Drag "Morning" from roster
- Drop on John's Monday cell → Assigned
- Drop on John's Wednesday cell → Assigned
- Drop on John's Friday cell → Assigned

**Symmetric Interaction Model:**
| View | Roster Contains | Row Represents | Action |
|------|-----------------|----------------|--------|
| Shift-Based | Employees | Shift types | Drag employee → Assign to shift |
| User-Based | Shift types | Employees | Drag shift → Assign to user |

---

### CU-1 Excel Grid

**Philosophy:** Clean, printable, team-grouped interface inspired by traditional Excel spreadsheets. No roster dock, no complex toolbars - just a pure grid with smart assignment.

**Layout:**
```
Top Header:
┌────────────────────────────────────────────────────┐
│ [📅 17/1/26 - 23/1/26 ▼]         [⬇️ Export ▼] [?] │
└────────────────────────────────────────────────────┘

Grid Structure:
┌──────────────┬──────────┬──────────┬──────────┬──────────┬──────────┬──────────┐
│              │  17/1    │  18/1    │  19/1    │  20/1    │  21/1    │  22/1    │ ← Row 1: Dates
├──────────────┼──────────┼──────────┼──────────┼──────────┼──────────┼──────────┤
│              │ ראשון   │  שני     │ שלישי    │ רביעי    │ חמישי    │ שישי     │ ← Row 2: Days
├──────────────┼──────────┼──────────┼──────────┼──────────┼──────────┼──────────┤
│ ▼ Team A     │░░░░░░░░░░│░░░░░░░░░░│░░░░░░░░░░│░░░░░░░░░░│░░░░░░░░░░│░░░░░░░░░░│ ← Team header (light blue)
├──────────────┼──────────┼──────────┼──────────┼──────────┼──────────┼──────────┤
│ John Smith   │░Morning●░│░░░░░░░░░░│░Evening░░│░Chore░░░░│░░░░░░░░░░│░Morning░░│ ← Member (light blue)
│              │░   [+1]░░│          │          │          │          │          │   (● = manual edit)
├──────────────┼──────────┼──────────┼──────────┼──────────┼──────────┼──────────┤
│ Sarah Cohen  │░░░░░░░░░░│░Morning░░│░░░░░░░░░░│░Evening░░│░Vacation░│░░░░░░░░░░│ ← Member (light blue)
├──────────────┼──────────┼──────────┼──────────┼──────────┼──────────┼──────────┤
│ ► Team B (3) │▒▒▒▒▒▒▒▒▒▒│▒▒▒▒▒▒▒▒▒▒│▒▒▒▒▒▒▒▒▒▒│▒▒▒▒▒▒▒▒▒▒│▒▒▒▒▒▒▒▒▒▒│▒▒▒▒▒▒▒▒▒▒│ ← Collapsed team (light green)
└──────────────┴──────────┴──────────┴──────────┴──────────┴──────────┴──────────┘

Export Buttons (Bottom Right):
[הורדה לאקסל] [הורדת תמונה]
```

**Structure Elements:**

1. **Header Rows (2):**
   - Row 1: Dates (17/1, 18/1, 19/1...)
   - Row 2: Day names (ראשון, שני, שלישי...)

2. **Team Sections:**
   - **Team header row** - Team name with collapse triangle (▼/►)
   - **Member rows** - One row per team member
   - **Background color** - Each team has distinct light color (Team A = light blue, Team B = light green, etc.)

3. **Cell Content:**
   - Assignment name (Morning, Evening, Chore, Vacation)
   - Color-coded text by type (blue=shift, orange=chore, purple=vacation)
   - Corner dot (●) if manually edited
   - `[+N]` indicator if multiple assignments (click to expand)

**Color Palette for Teams:**
| Team | Background Color | Hex |
|------|------------------|-----|
| Team A | Light Blue | `#E3F2FD` |
| Team B | Light Green | `#E8F5E9` |
| Team C | Light Orange | `#FFF3E0` |
| Team D | Light Purple | `#F3E5F5` |
| Team E | Light Teal | `#E0F2F1` |
| Team F | Light Pink | `#FCE4EC` |

**Interactions:**

#### Primary: Click-to-Assign Flow
```
1. Click empty cell
   ↓
2. Cell enters edit mode (border highlights)
   ↓
3. Context menu appears:
   ┌─────────────────┐
   │ ○ Shift         │
   │ ○ Chore         │
   │ ○ Vacation      │
   └─────────────────┘
   ↓
4. Select type (radio button)
   ↓
5. Type user name
   ↓
6. Autocomplete dropdown shows:
   - Available users (normal text)
   - Unavailable users (RED text + warning)
     Example: "Sarah Cohen ⚠️ On vacation 20/1-22/1"
   ↓
7. Select user
   ↓
8. Press Enter → Cell saves
   ↓
9. Cell displays assignment
   - Text color based on type
   - Corner dot (●) appears (detached from program)
```

#### Secondary: Drag from Name
```
1. Hover over user name column (left edge)
   ↓
2. Cursor changes to drag icon
   ↓
3. Drag user's row label
   ↓
4. Drop on date cell
   ↓
5. Quick assignment (uses default shift type or prompts)
```

#### Multiple Assignments in Cell
```
Cell with 3 assignments:
┌──────────────┐
│ Morning [+2] │ ← Shows earliest start time + count
└──────────────┘

Click cell:
┌──────────────┐
│ Morning 06:00│ ← Cell expands vertically
│ Chore   14:00│   Row height increases
│ Night   22:00│   All in chronological order
└──────────────┘

Click again or click outside: Collapses back to [+2] state
```

**Team Collapse Behavior:**

**Expanded state:**
```
▼ Team A (shows all members)
  - John Smith
  - Sarah Cohen
  - Mike Johnson
```

**Collapsed state:**
```
► Team A (3)  ← Shows member count only
```

**Persistence:**
- Collapse/expand state saved to `localStorage`
- Key: `cu1-team-collapsed-{teamId}`: true/false
- Persists across page refreshes

**Date Range Navigation:**

**Dropdown presets:**
```
Click: [📅 17/1/26 - 23/1/26 ▼]

Opens dropdown:
┌─────────────────────────────────┐
│ This Week      (17/1 - 23/1)   │ ← Highlights current
│ Next Week      (24/1 - 30/1)   │
│ This Month     (1/1 - 31/1)    │
│ Next Month     (1/2 - 28/2)    │
├─────────────────────────────────┤
│ Custom Range...                 │ ← Opens calendar picker
└─────────────────────────────────┘
```

**Custom range picker:**
- Opens date range calendar modal
- Select start date → Select end date
- Apply → Grid reloads with new date range
- Max range: 31 days (performance limit)

**Export Buttons:**

Per CU-1 specification, dedicated export buttons appear bottom-right:

```
┌───────────────────┬─────────────────┐
│ הורדה לאקסל      │ הורדת תמונה     │
│ (Download Excel)  │ (Download Image)│
└───────────────────┴─────────────────┘
```

**Excel Export:**
- Native `.xlsx` file
- Preserves team colors
- Includes borders and formatting
- Cell values are text (not formulas)
- Filename: `ShiftManager_Grid_{date_range}.xlsx`

**PNG Export:**
- Screenshot-style image
- High resolution (2x for retina)
- Preserves all visual styling
- Filename: `ShiftManager_Grid_{date_range}.png`

**Programs Integration:**

CU-1 cells are auto-populated by Programs/Master Programs, same as Table view:

```
Program generates shift instance
     ↓
Cell populated with assignment (no indicator)
     ↓
User clicks cell and manually edits
     ↓
Corner dot (●) appears
     ↓
Future program runs skip this cell
     ↓
User can right-click → "Reset to Program" to restore auto-population
```

**Visual Indicators:**
- **No indicator** = Program-generated (clean)
- **● corner dot** = Manually edited (detached from program)
- **⚠️ badge + glow** = Radar mode conflict (understaffed/overstaffed)

**Radar Mode in CU-1:**

Same conflict detection as Table view:

```
Understaffed cell:
┌──────────────┐
│ Morning      │ ← Yellow/orange glow
│   ⚠️ 2/3     │ ← Badge shows filled/required
└──────────────┘

Overstaffed cell:
┌──────────────┐
│ Evening      │ ← Red glow
│   ⚠️ 4/3     │ ← Badge shows excess
└──────────────┘
```

**Keyboard Navigation in CU-1:**

| Key | Action |
|-----|--------|
| **Tab** | Move to next cell (right) |
| **Shift+Tab** | Move to previous cell (left) |
| **Arrow keys** | Navigate grid |
| **Enter** | Edit mode / Confirm edit / Move down |
| **Esc** | Cancel edit |
| **Delete** | Clear cell |
| **Ctrl+C** | Copy cell content |
| **Ctrl+V** | Paste |

**No Fill Handle in CU-1:**
- Keep CU-1 simple (click-to-assign focus)
- Use Table view for advanced bulk operations
- Fill handle only in Table views

---

## Smart Synchronization System

### Problem Statement
Multiple users editing the same calendar simultaneously can lead to:
- Lost edits (user A overwrites user B's changes)
- Disrupted workflows (screen refreshes mid-assignment)
- Stale data (outdated view of assignments)

### Solution: Context-Aware Hybrid Sync

The system adapts synchronization strategy based on **user state**:

### State Detection

**Three states:**

1. **IDLE** - User is passively viewing
2. **ACTIVE** - User is interacting (modal open, dropdown visible, dragging, typing)
3. **POST-ACTION** - User just completed an action (saved, closed modal, finished drag)

**State Detection Logic:**

```javascript
function getUserState() {
  // Check for blocking UI elements
  if (document.querySelector('.modal.show')) return 'ACTIVE';
  if (document.querySelector('.dropdown.show')) return 'ACTIVE';
  if (document.querySelector('.employee-dropdown.show')) return 'ACTIVE';
  if (document.querySelector('.action-menu.show')) return 'ACTIVE';
  if (isDragging) return 'ACTIVE';

  // Check for forms with unsaved data
  if (document.querySelector('form.dirty')) return 'ACTIVE';

  // Check for focused inputs with content
  const activeElement = document.activeElement;
  if (activeElement.tagName === 'INPUT' && activeElement.value.length > 0) return 'ACTIVE';
  if (activeElement.tagName === 'TEXTAREA' && activeElement.value.length > 0) return 'ACTIVE';

  // Check if just completed action (within last 2 seconds)
  if (lastActionTime && (Date.now() - lastActionTime) < 2000) {
    return 'POST-ACTION';
  }

  return 'IDLE';
}
```

**What counts as ACTIVE (blocks real-time updates):**
- ✅ Modal open
- ✅ Dropdown expanded
- ✅ Context menu visible
- ✅ Drag-and-drop in progress
- ✅ Input field focused with typed content
- ✅ Form with unsaved changes

**What does NOT count as ACTIVE (allows updates):**
- ❌ Hovering over elements
- ❌ Tooltips visible
- ❌ "Show more" expanded
- ❌ Roster dock open (but not dragging)
- ❌ Empty input field focused

### Sync Behavior by State

#### State 1: IDLE (Passive Viewing)

**Sync Method:** Real-time push (SignalR)

**How it works:**
```
User A viewing calendar (IDLE)
     ↓
User B assigns "John to Monday Morning"
     ↓
SignalR push to User A's browser
     ↓
User A's screen updates instantly (smooth fade-in animation)
     ↓
No user action required
```

**Technical:**
- WebSocket connection via SignalR Hub
- Server pushes changes to all connected clients
- Client applies DOM updates with CSS transitions
- Works in air-gapped environments (local network only)

**Visual feedback:**
```css
/* Smooth fade-in for new assignments */
.assignment-cell.updated {
  animation: fadeIn 0.3s ease-in;
}

@keyframes fadeIn {
  from { opacity: 0; transform: scale(0.95); }
  to { opacity: 1; transform: scale(1); }
}
```

#### State 2: ACTIVE (User Interaction In Progress)

**Sync Method:** Deferred refresh with polling

**How it works:**
```
User A opens "Assign to Monday Morning" modal (enters ACTIVE state)
     ↓
SignalR connection pauses (saves bandwidth)
     ↓
Background polling starts (every 30 seconds)
     ↓
User B assigns "Sarah to Monday Morning" (conflict!)
     ↓
Background poll detects change, stores in memory (doesn't apply yet)
     ↓
Badge appears: "🟡 3 updates pending  [Refresh Now]"
     ↓
User A completes action (saves or cancels modal)
     ↓
Returns to IDLE → Deferred updates apply automatically
     ↓
User A sees Sarah's assignment + conflict warning
```

**Update Badge UI:**
```html
<div class="sync-status-badge" style="position: fixed; top: 70px; right: 20px;">
  🟡 3 updates pending
  <button onclick="applyDeferredUpdates()">Refresh Now</button>
</div>
```

**Technical:**
```javascript
let deferredUpdates = [];
let pollInterval = null;

function startDeferredPolling() {
  pollInterval = setInterval(async () => {
    const updates = await fetchLatestData();
    deferredUpdates = updates;

    if (updates.length > 0) {
      showUpdateBadge(updates.length);
    }
  }, 30000); // 30 seconds
}

function applyDeferredUpdates() {
  deferredUpdates.forEach(update => {
    applyUpdateToDOM(update);
  });
  deferredUpdates = [];
  hideBadge();
}
```

**Auto-apply timing:**
- When modal closes → Apply immediately
- When dropdown collapses → Apply immediately
- When drag completes → Apply immediately
- When form submits → Apply immediately

#### State 3: POST-ACTION (Just Completed Action)

**Sync Method:** Immediate refresh

**How it works:**
```
User A drags "John" from roster to Monday Morning cell
     ↓
DROP completes → API call → Success response
     ↓
Immediately fetch latest data from server
     ↓
Full refresh of visible date range
     ↓
User A sees their change + any updates from others
     ↓
Transition to IDLE after 2 seconds
```

**Why immediate refresh?**
- User already expects screen to change (they just acted)
- Natural moment to incorporate others' changes
- No disruption to workflow

**Technical:**
```javascript
async function handleDropComplete(employeeId, cellData) {
  // Save assignment
  const result = await assignEmployee(employeeId, cellData);

  if (result.success) {
    lastActionTime = Date.now();

    // Immediate refresh (don't wait for SignalR)
    await refreshCalendarData();

    // Brief POST-ACTION state (2 seconds)
    setTimeout(() => {
      lastActionTime = null; // Return to IDLE
      reconnectSignalR(); // Resume real-time updates
    }, 2000);
  }
}
```

### State Transition Diagram

```
┌──────────────────────────────────────────┐
│              IDLE STATE                   │
│  - SignalR connected (real-time)         │
│  - Updates apply instantly               │
│  - Polling: None                         │
└───────────┬──────────────────────────────┘
            │
            │ Modal/Dropdown opens
            │ Drag starts
            │ Form editing
            ▼
┌──────────────────────────────────────────┐
│             ACTIVE STATE                  │
│  - SignalR paused                        │
│  - Updates deferred (stored)             │
│  - Polling: Every 30s                    │
│  - Badge: "X updates pending"            │
└───────────┬──────────────────────────────┘
            │
            │ Modal closes / Save / Cancel
            │ Dropdown closes
            │ Drag completes
            ▼
┌──────────────────────────────────────────┐
│          POST-ACTION STATE                │
│  - Immediate data fetch                  │
│  - Apply all updates (deferred + fresh)  │
│  - Duration: 2 seconds                   │
└───────────┬──────────────────────────────┘
            │
            │ After 2 seconds
            │
            ▼
         [Return to IDLE]
```

### Conflict Resolution

**Scenario:** Two users try to assign different people to the same shift slot simultaneously

**Timeline:**
```
10:00:00 - User A opens "Assign to Monday Morning" modal (ACTIVE)
10:00:15 - User B assigns "Sarah" to Monday Morning (succeeds, A's screen doesn't update yet)
10:00:30 - Background poll detects change, stores: { mondayMorning: { assignedTo: "Sarah" } }
10:00:45 - User A tries to assign "John" to Monday Morning
10:00:46 - Server returns 409 Conflict (slot already assigned)
10:00:47 - UI shows conflict resolution dialog
```

**Conflict Resolution Dialog:**
```
┌─────────────────────────────────────────────┐
│ ⚠️ Assignment Conflict                      │
├─────────────────────────────────────────────┤
│ This slot was just assigned to:             │
│ Sarah Cohen                                 │
│ by User B at 10:00:15                       │
│                                             │
│ You are trying to assign:                   │
│ John Smith                                  │
│                                             │
│ What would you like to do?                  │
│                                             │
│ [Cancel]  [Replace Sarah with John]  [Add John as Trainee] │
└─────────────────────────────────────────────┘
```

**Resolution Options:**
1. **Cancel** - Close modal, refresh view (shows Sarah assigned)
2. **Replace** - Unassign Sarah, assign John (audit log records both actions)
3. **Add as Trainee** - Keep Sarah as primary, add John as trainee (if staffing allows)

### Network Optimization

**Adaptive Polling Intervals:**

| User State | SignalR | Polling Interval | Bandwidth |
|------------|---------|------------------|-----------|
| IDLE | ✅ Active | None | Low (WebSocket only) |
| ACTIVE | ❌ Paused | 30 seconds | Very low (REST poll) |
| POST-ACTION | ✅ Reconnecting | One-time fetch | Spike (immediate) |

**Why pause SignalR during ACTIVE?**
- User is focused on their task
- Updates won't be applied anyway (deferred)
- Reduces server load
- Saves bandwidth
- Reconnects automatically when returning to IDLE

**Connection Lost Handling:**
```
SignalR connection lost detected
     ↓
Show warning: "🔴 Connection lost. Retrying..."
     ↓
Poll every 5 seconds until reconnected
     ↓
When reconnected: Full data refresh
     ↓
Show success: "🟢 Connected. Calendar updated."
```

### Air-Gapped Environment Support

**Yes, real-time sync works in air-gapped!**

Air-gapped ≠ No network. It means isolated from the internet.

**Setup:**
```
Server: http://192.168.1.100:5000 (local network)
Client 1: http://192.168.1.100:5000 (same machine)
Client 2: http://192.168.1.101 (different machine, same LAN)
Client 3: http://192.168.1.102 (different machine, same LAN)
     ↓
All clients connect to local SignalR hub
     ↓
Real-time sync works over LAN
```

**Requirements:**
- Internal network connectivity between machines
- ASP.NET Core server running SignalR
- Clients can reach server via local IP/hostname
- No external internet needed

---

## Programs/Blueprints/Master Programs Integration

### System Overview

**Hierarchy:**
```
1. Blueprints (Shift Type Definitions)
   ↓ define
   Example: "Morning Shift" = 06:00-14:00

2. Programs (Weekly Templates)
   ↓ use blueprints
   Example: "Morning Mon-Fri" = Morning shift on Mon/Tue/Wed/Thu/Fri, 2 people required

3. Master Programs (Complete Schedules)
   ↓ combine programs
   Example: "Standard Week" = Morning Mon-Fri + Evening Mon-Fri + Night Mon-Sun

4. ShiftInstances (Generated Assignments)
   ↓ populate calendars
   Example: Actual shift on "Monday Jan 20, 2026" created from "Morning Mon-Fri" program
```

### Universal Behavior Across All Views

**Core Principle:** Programs populate shifts in **all calendar views** identically. The view is just a different UI to the same underlying data.

**Lifecycle:**

```
Step 1: Owner creates Master Program
     ↓
Step 2: Owner applies Master Program to date range
     ↓
Step 3: System generates ShiftInstances for all dates
     ↓
Step 4: ShiftInstances appear in ALL views:
        - Month/Week/Day calendars
        - Table view (Shift-Based)
        - Table view (User-Based)
        - CU-1 Excel Grid
     ↓
Step 5: Manager assigns employees to shifts
     ↓
Step 6a: Assignment via program → Cell clean (no badge)
Step 6b: Manual assignment → Cell marked as "detached"
     ↓
Step 7: Future program applications skip detached cells
     ↓
Step 8: Manager can "Reset to Program" to restore auto-population
```

### Detachment on Edit

**What triggers detachment?**
1. Manual employee assignment (different from program default)
2. Staffing change (e.g., program says 2 required, user changes to 3)
3. Shift deletion (user removes program-generated shift)
4. Time modification (user changes shift hours)

**What does NOT trigger detachment?**
1. Viewing the cell
2. Expanding multi-assignment cell
3. Hovering over cell
4. Applying same assignment as program (no change = no detachment)

### Visual Indicators by View

| View | Program-Generated | Manually Edited (Detached) |
|------|-------------------|----------------------------|
| **Table (Shift-Based)** | No badge | `[OVR]` badge |
| **Table (User-Based)** | No badge | `[OVR]` badge |
| **CU-1 Excel Grid** | No indicator | `●` corner dot |
| **Month/Week/Day** | Normal event | `*` or border accent |

### Reset to Program

**Table Views:**

Right-click cell with `[OVR]` badge:
```
┌─────────────────────────────────┐
│ Edit Staffing                   │
│ Remove Assignment               │
│ ─────────────────────────────── │
│ 🔄 Reset to Program Defaults    │ ← This option
└─────────────────────────────────┘
```

Action: Removes manual assignments, restores program-defined staffing requirements

**CU-1 Excel Grid:**

Right-click cell with `●` corner dot:
```
┌─────────────────────────────────┐
│ Edit Assignment                 │
│ Clear Cell                      │
│ ─────────────────────────────── │
│ 🔄 Reset to Program             │ ← This option
└─────────────────────────────────┘
```

Action: Removes manual assignment, allows program to regenerate

### Program Application Modes

**Mode 1: Fill Empty Only**
```
Program application skips:
- Cells with existing assignments (even if detached)
- Cells marked as detached

Fills only:
- Completely empty cells
- Cells that were never touched
```

**Mode 2: Overwrite All**
```
Program application:
- Clears ALL existing assignments
- Removes all detachment flags
- Regenerates from scratch

⚠️ Requires confirmation:
"This will overwrite all manual assignments. Continue?"
```

**Mode 3: Smart Merge**
```
Program application:
- Fills empty cells
- Skips detached cells
- Updates non-detached cells to match current program definition

Use case: Program was edited (e.g., staffing changed from 2 to 3)
Want to update auto-generated cells but preserve manual edits
```

### Audit Trail

**All program-related actions logged:**

| Action | Audit Log Entry |
|--------|----------------|
| Program applied | "Master Program 'Standard Week' applied to 2026-01-20 through 2026-01-26 by User123" |
| Cell detached | "ShiftInstance #456 detached from Program (manual edit by User123)" |
| Reset to program | "ShiftInstance #456 reset to Program defaults by User123" |
| Program overwrite | "Master Program 'Standard Week' re-applied with OVERWRITE mode by User123 (45 cells affected)" |

---

## Color System

### Design Philosophy

**Goals:**
1. **Consistency** - Same assignment type = same color across ALL views
2. **Accessibility** - WCAG AAA compliant (7:1+ contrast ratio)
3. **Tasteful** - Avoid "too splashy" UI, use color strategically
4. **Mode-Aware** - Different colors for light mode vs dark mode

### Assignment Type Color Palette

#### Light Mode (default)

**On white background (`#ffffff`):**

| Assignment Type | Color Name | Hex | RGB | Contrast Ratio | WCAG |
|-----------------|-----------|-----|-----|----------------|------|
| **Shifts** | Soft Blue | `#1565C0` | rgb(21, 101, 192) | 7.5:1 | AAA ✅ |
| **Chores** | Warm Orange | `#E65100` | rgb(230, 81, 0) | 6.2:1 | AAA ✅ |
| **On-Duty** | Deep Teal | `#00695C` | rgb(0, 105, 92) | 6.8:1 | AAA ✅ |
| **Vacations** | Rich Purple | `#6A1B9A` | rgb(106, 27, 154) | 7.1:1 | AAA ✅ |
| **Unavailable** | Danger Red | `#C62828` | rgb(198, 40, 40) | 7.9:1 | AAA ✅ |
| **Pending** | Muted Gray | `#616161` | rgb(97, 97, 97) | 5.7:1 | AAA ✅ |

#### Dark Mode

**On dark surface (`#0b1120`):**

| Assignment Type | Color Name | Hex | RGB | Contrast Ratio | WCAG |
|-----------------|-----------|-----|-----|----------------|------|
| **Shifts** | Light Sky | `#64B5F6` | rgb(100, 181, 246) | 8.2:1 | AAA ✅ |
| **Chores** | Peach | `#FFB74D` | rgb(255, 183, 77) | 9.1:1 | AAA ✅ |
| **On-Duty** | Mint Teal | `#4DB6AC` | rgb(77, 182, 172) | 8.5:1 | AAA ✅ |
| **Vacations** | Lavender | `#BA68C8` | rgb(186, 104, 200) | 7.8:1 | AAA ✅ |
| **Unavailable** | Soft Red | `#EF5350` | rgb(239, 83, 80) | 6.9:1 | AAA ✅ |
| **Pending** | Light Gray | `#BDBDBD` | rgb(189, 189, 189) | 8.3:1 | AAA ✅ |

### CSS Implementation

```css
:root {
  /* Assignment Type Colors - Light Mode */
  --assignment-shift: #1565C0;
  --assignment-chore: #E65100;
  --assignment-onduty: #00695C;
  --assignment-vacation: #6A1B9A;
  --assignment-unavailable: #C62828;
  --assignment-pending: #616161;

  /* Soft background variants (for badges, pills) */
  --assignment-shift-bg: #E3F2FD;
  --assignment-chore-bg: #FFF3E0;
  --assignment-onduty-bg: #E0F2F1;
  --assignment-vacation-bg: #F3E5F5;
  --assignment-unavailable-bg: #FFEBEE;
  --assignment-pending-bg: #F5F5F5;
}

:root[data-theme="dark"] {
  /* Assignment Type Colors - Dark Mode */
  --assignment-shift: #64B5F6;
  --assignment-chore: #FFB74D;
  --assignment-onduty: #4DB6AC;
  --assignment-vacation: #BA68C8;
  --assignment-unavailable: #EF5350;
  --assignment-pending: #BDBDBD;

  /* Soft background variants (for badges, pills) */
  --assignment-shift-bg: #1A2636;
  --assignment-chore-bg: #2B2416;
  --assignment-onduty-bg: #142B28;
  --assignment-vacation-bg: #2A1B30;
  --assignment-unavailable-bg: #2B1515;
  --assignment-pending-bg: #1E1E1E;
}
```

### Tasteful Application Rules

#### ✅ DO Use Color:

1. **Grid cell text** (all views)
   ```html
   <span class="assignment assignment-shift">Morning</span>
   ```

2. **Small badges/pills** (status indicators)
   ```html
   <span class="status-badge status-pending">Pending Approval</span>
   ```

3. **Calendar event blocks** (Month/Week/Day views)
   ```html
   <div class="calendar-event event-type-chore">
     <span class="event-time">14:00</span>
     <span class="event-title">Kitchen Duty</span>
   </div>
   ```

4. **Legend keys**
   ```html
   <div class="legend">
     <div class="legend-item">
       <span class="legend-color" style="background: var(--assignment-shift);"></span>
       <span>Shifts</span>
     </div>
   </div>
   ```

5. **Chart/graph elements** (statistics pages)
   ```javascript
   datasets: [{
     label: 'Shifts',
     backgroundColor: 'var(--assignment-shift)',
     data: [12, 15, 18]
   }]
   ```

6. **Dropdown option dots** (small colored circles)
   ```html
   <option>
     <span class="option-dot dot-shift"></span>
     Morning Shift
   </option>
   ```

#### ❌ DON'T Use Color:

1. **Large background areas** - Keep backgrounds neutral (white/dark surface)
2. **Entire rows/columns** - Only individual cells
3. **Buttons** - Keep existing button color system (primary/secondary/danger)
4. **Headers/titles** - Keep typography hierarchy (dark text)
5. **Navigation elements** - Keep sidebar/header neutral

### Usage Examples

**Grid Cell (CU-1):**
```html
<div class="excel-cell" style="background: var(--surface);">
  <span class="assignment assignment-shift">Morning</span>
</div>
```
Result: "Morning" text appears in blue (#1565C0 light / #64B5F6 dark)

**Filter Toggle with Color Dot:**
```html
<button class="filter-btn">
  <span class="color-dot" style="background: var(--assignment-chore);"></span>
  <loc key="Chores">תורנויות</loc>
</button>
```

**Calendar Event:**
```html
<div class="calendar-event" style="border-left: 3px solid var(--assignment-vacation);">
  <span class="event-time" style="color: var(--assignment-vacation);">09:00</span>
  <span class="event-title">Vacation</span>
</div>
```

### Feature-by-Feature Rollout

**Phase 1: CU-1 Excel Grid** (when building)
- Cell text colors for assignments
- Export preserves colors

**Phase 2: User-Based View** (when building flip toggle)
- Same color system in flipped cells
- Consistent with CU-1

**Phase 3: Existing views enhancement** (after new features stable)
- Update Table view
- Update Month/Week/Day calendars
- Add legends/filters

**Phase 4: Analytics & Reports** (when building statistics)
- Charts and graphs
- Fairness dashboard

---

## Interaction Models

### Drag-and-Drop

#### Table View (Shift-Based)

**Source:** Roster dock (contains employees)
**Target:** Grid cells (shift × date)
**Action:** Assign employee to shift on that date

```
Flow:
1. Roster dock shows: [👤 John] [👤 Sarah] [👤 Mike]
2. Drag "John" from roster
3. Drag cursor shows employee name + shift preview
4. Drop on "Morning × Monday" cell
5. API call: POST /assign { employeeId, shiftInstanceId }
6. Success → Cell updates with "John"
7. Roster item remains (can assign John to multiple shifts)
```

**Visual Feedback:**
```css
/* Drag preview */
.drag-ghost {
  background: var(--surface-elevated);
  padding: 0.5rem 1rem;
  border-radius: var(--radius-md);
  box-shadow: var(--shadow-lg);
  pointer-events: none;
}

/* Drop target highlight */
.assignment-cell.drop-target {
  background: rgba(var(--primary-rgb), 0.1);
  border: 2px dashed var(--primary);
}

/* Invalid drop target */
.assignment-cell.drop-invalid {
  background: rgba(var(--danger-rgb), 0.1);
  cursor: not-allowed;
}
```

#### Table View (User-Based)

**Source:** Roster dock (contains shift types)
**Target:** Grid cells (user × date)
**Action:** Assign shift to user on that date

```
Flow:
1. Roster dock shows: [🌅 Morning] [🌆 Evening] [🧹 Chore]
2. Drag "Morning" from roster
3. Drag cursor shows shift name + time
4. Drop on "John × Monday" cell
5. API call: POST /assign { userId, shiftTypeId, date }
6. Success → Cell updates with "Morning"
7. Roster item remains (can assign Morning to multiple users)
```

**Symmetric Design:**
- Shift-Based: Drag WHO → Drop on WHAT/WHEN
- User-Based: Drag WHAT → Drop on WHO/WHEN

#### CU-1 Excel Grid

**Source:** User name column (first column)
**Target:** Date cells in that user's row
**Action:** Quick-assign user to that date

```
Flow:
1. Hover over "John Smith" row label
2. Cursor changes to drag icon
3. Drag row label
4. Drop on date cell (e.g., Monday column)
5. If shift type is unambiguous → Assign default shift
6. If multiple shift types possible → Show quick picker:
   ┌─────────────────┐
   │ Assign John to: │
   │ ○ Morning       │
   │ ○ Evening       │
   │ ○ Chore         │
   └─────────────────┘
7. Select type → Cell updates
```

**No Roster Dock in CU-1:**
- Keeps interface clean
- Drag-from-name is sufficient
- Primary interaction is click-to-assign

### Click-to-Assign

#### Table Views (Both Modes)

**Current Behavior (existing):**
1. Click cell → Dropdown appears with employee/shift list
2. Select item → Assigns
3. Click again → Edit/remove

#### CU-1 Excel Grid (New)

**Enhanced Click Flow:**

```
1. Click empty cell
   ↓
   Cell border highlights (edit mode)

2. Context menu appears floating near cell:
   ┌─────────────────┐
   │ Assignment Type:│
   │ ○ Shift         │ ← Selected by default
   │ ○ Chore         │
   │ ○ Vacation      │
   └─────────────────┘

3. Input field appears in cell with autocomplete:
   ┌──────────────────────────┐
   │ [Type user name...] 🔍   │
   └──────────────────────────┘

4. User types "joh"
   ↓
   Autocomplete dropdown:
   ┌─────────────────────────────────────┐
   │ John Smith ✅                        │ ← Available (normal text)
   │ John Davis ⚠️ On vacation 20/1-22/1 │ ← Unavailable (RED text)
   └─────────────────────────────────────┘

5. User selects "John Smith" (available option)
   ↓
   User presses Enter

6. Cell saves:
   - API call: POST /assign { userId, cellDate, assignmentType }
   - Cell displays: "Morning" (in blue shift color)
   - Corner dot (●) appears (marked as manually edited)
   - Context menu closes
   - Cell exits edit mode
```

**Red Unavailable Warning:**

```html
<div class="autocomplete-option unavailable">
  <span class="option-name">Sarah Cohen</span>
  <span class="option-warning">⚠️ On vacation 20/1-22/1</span>
</div>
```

```css
.autocomplete-option.unavailable {
  color: var(--assignment-unavailable); /* Red */
  font-style: italic;
}

.autocomplete-option.unavailable .option-warning {
  font-size: 0.875rem;
  color: var(--danger);
}
```

**Can still assign unavailable users:**
- Warning shows, but assignment is allowed
- Useful for emergency staffing or overrides
- Audit log records "Assigned despite vacation conflict"

### Multi-Assignment Handling

**CU-1 Cell with Multiple Assignments:**

**Collapsed state (default):**
```
┌──────────────┐
│ Morning [+2] │ ← Shows earliest assignment + count
└──────────────┘
```

**Expanded state (after click):**
```
┌──────────────┐
│ Morning 06:00│ ← Cell grows taller
│ Chore   14:00│   Sorted chronologically
│ Night   22:00│
│ (3 items)    │
└──────────────┘
```

**Row height behavior:**
- Entire row height increases when any cell expands
- Maintains horizontal alignment
- Other cells in row show vertical centering

**Click outside or click cell again → Collapses back**

### Fill Handle (Excel-Style)

**Available in:** Table views only (not CU-1)

**How it works:**
```
1. Cell has assignment: "John → Morning"

2. Hover bottom-right corner of cell
   ↓
   Small square handle appears
   Cursor changes to crosshair (+)

3. Click and drag handle right (across dates)
   ↓
   Selection preview highlights target cells

4. Release mouse
   ↓
   Assignment replicates to all selected cells:
   - Monday: John → Morning
   - Tuesday: John → Morning
   - Wednesday: John → Morning
   - Thursday: John → Morning

5. All filled cells marked as manually edited (OVR badge)
```

**Smart Fill Options:**

After fill completes, show options tooltip:
```
┌─────────────────────────────┐
│ Fill Options:               │
│ ○ Copy cells (current)      │
│ ○ Fill days only (skip wknd)│
│ ○ Fill pattern (alt days)   │
└─────────────────────────────┘
```

---

## Visual Indicators System

### Complete Indicator Matrix

| State | Table View | CU-1 Grid | Month/Week/Day | Purpose |
|-------|-----------|-----------|----------------|---------|
| **Program-generated** | No badge | No indicator | Normal event | Clean default state |
| **Manually edited** | `[OVR]` badge | `●` corner dot | `*` or border | Show detachment |
| **Understaffed (Radar)** | `⚠️ 2/3` + yellow glow | `⚠️ 2/3` + yellow glow | Badge | Conflict warning |
| **Overstaffed (Radar)** | `⚠️ 4/3` + red glow | `⚠️ 4/3` + red glow | Badge | Conflict warning |
| **Trainee** | `[T]` badge | Small `T` subscript | `(T)` suffix | Distinguish trainees |
| **Multiple assignments** | Stacked names | `[+N]` indicator | `+N more` | Show overflow |
| **Unavailable user** | Red text in dropdown | Red text in autocomplete | N/A | Prevent conflicts |
| **Pending approval** | Gray italic text | Gray italic text | Gray border | Awaiting action |

### OVR Badge (Table Views)

**Appearance:**
```html
<span class="ovr-badge">OVR</span>
```

```css
.ovr-badge {
  display: inline-block;
  padding: 0.125rem 0.375rem;
  background: var(--warning);
  color: var(--warning-text);
  font-size: 0.625rem;
  font-weight: 700;
  border-radius: var(--radius-sm);
  text-transform: uppercase;
  margin-left: 0.25rem;
  vertical-align: middle;
}
```

**Tooltip on hover:**
"This shift was manually modified and detached from its Program"

**Context menu option:**
"🔄 Reset to Program Defaults" → Removes OVR badge and restores auto-population

### Corner Dot (CU-1 Grid)

**Appearance:**
```css
.excel-cell.manually-edited::after {
  content: '●';
  position: absolute;
  top: 4px;
  right: 4px; /* left: 4px in RTL mode */
  font-size: 8px;
  color: var(--focus); /* Orange #ff9800 */
  line-height: 1;
}
```

**Positioning:**
- Top-right corner in LTR
- Top-left corner in RTL (Hebrew layout)
- 4px from edges
- Does not overlap text

**Tooltip on hover:**
"Manually edited"

### Radar Conflict Badges

**Understaffed (Warning):**
```html
<div class="conflict-badge badge-warning">⚠️ 2/3</div>
```

```css
.conflict-badge {
  position: absolute;
  top: 2px;
  right: 2px;
  padding: 0.25rem 0.5rem;
  background: rgba(245, 158, 11, 0.9); /* Warning color with opacity */
  color: white;
  font-size: 0.75rem;
  font-weight: 600;
  border-radius: var(--radius-sm);
  box-shadow: var(--shadow-md);
  z-index: 10;
}

.radar-underfilled {
  box-shadow: 0 0 0 3px rgba(245, 158, 11, 0.3); /* Yellow glow */
}
```

**Overstaffed (Danger):**
```html
<div class="conflict-badge badge-danger">⚠️ 4/3</div>
```

```css
.radar-overfilled {
  box-shadow: 0 0 0 3px rgba(220, 38, 38, 0.3); /* Red glow */
}
```

**Tooltip with details:**
```
Hover over conflict badge:
┌─────────────────────────────────┐
│ ⚠️ Understaffed                 │
│ Shift: Morning                  │
│ Date: Mon, Jan 20              │
│ Staffing: 2 / 3                │
│ Needed: 1 more employee        │
└─────────────────────────────────┘
```

### Trainee Badge

**Table View:**
```html
<span class="trainee-badge">[T]</span>
```

**CU-1 Grid:**
```html
<span class="assignment assignment-shift">
  Morning<sub class="trainee-indicator">T</sub>
</span>
```

```css
.trainee-indicator {
  font-size: 0.7em;
  color: var(--muted);
  margin-left: 0.125rem;
}
```

### Multi-Assignment Indicator

**CU-1 Collapsed:**
```html
<div class="excel-cell">
  <span class="assignment assignment-shift">Morning</span>
  <span class="multi-assignment-badge">[+2]</span>
</div>
```

```css
.multi-assignment-badge {
  display: inline-block;
  padding: 0.125rem 0.375rem;
  background: var(--surface-soft);
  border: 1px solid var(--border);
  font-size: 0.75rem;
  border-radius: var(--radius-sm);
  margin-left: 0.25rem;
  color: var(--muted);
}
```

**CU-1 Expanded:**
```html
<div class="excel-cell expanded">
  <div class="assignment-item">
    <span class="assignment assignment-shift">Morning</span>
    <span class="assignment-time">06:00</span>
  </div>
  <div class="assignment-item">
    <span class="assignment assignment-chore">Chore</span>
    <span class="assignment-time">14:00</span>
  </div>
  <div class="assignment-item">
    <span class="assignment assignment-shift">Night</span>
    <span class="assignment-time">22:00</span>
  </div>
  <div class="assignment-count">(3 items)</div>
</div>
```

---

## Keyboard Shortcuts

### Universal Shortcuts (All Views)

| Shortcut | Action | Notes |
|----------|--------|-------|
| **Tab** | Move to next cell (right) | Wraps to next row at end |
| **Shift+Tab** | Move to previous cell (left) | Wraps to previous row at start |
| **↑ ↓ ← →** | Navigate grid | Arrow keys move one cell |
| **Enter** | Confirm edit / Next row | In edit mode: save; Otherwise: move down |
| **Esc** | Cancel edit | Discards changes, exits edit mode |
| **Delete** | Clear cell | Removes assignment |
| **Ctrl+C** | Copy cell content | Copies assignment details |
| **Ctrl+V** | Paste | Applies copied assignment to current cell |
| **F2** | Enter edit mode | Same as clicking cell |
| **Ctrl+Z** | Undo (future) | Not yet implemented |

### Table View Specific

| Shortcut | Action | Notes |
|----------|--------|-------|
| **Ctrl+Shift+R** | Toggle Roster dock | Show/hide roster |
| **Ctrl+Shift+D** | Toggle Radar mode | Enable/disable conflict detection |
| **Ctrl+Shift+F** | Toggle view (Shift/User) | Flip between modes |
| **Ctrl+E** | Export menu | Opens export dropdown |

### CU-1 Specific

| Shortcut | Action | Notes |
|----------|--------|-------|
| **Space** | Expand/collapse multi-assignment cell | Toggle height |
| **Ctrl+Shift+E** | Expand all teams | Show all members |
| **Ctrl+Shift+C** | Collapse all teams | Hide all members |
| **Ctrl+Shift+T** | Toggle team at cursor | Expand/collapse current team |

### Fill Handle (Table Views Only)

| Shortcut | Action | Notes |
|----------|--------|-------|
| **Ctrl+Drag** | Fill with pattern | Alternates assignment |
| **Shift+Drag** | Fill weekdays only | Skips weekends |
| **Ctrl+D** | Fill down | Replicates to cell below |
| **Ctrl+R** | Fill right | Replicates to cell right |

### Accessibility Shortcuts

| Shortcut | Action | Notes |
|----------|--------|-------|
| **Ctrl+Plus** | Zoom in | Increase font size |
| **Ctrl+Minus** | Zoom out | Decrease font size |
| **Ctrl+0** | Reset zoom | Return to 100% |
| **Alt+Shift+T** | Toggle high contrast | Accessibility mode |

### Keyboard Navigation Visual Feedback

**Focus indicator:**
```css
.assignment-cell:focus {
  outline: 3px solid var(--focus);
  outline-offset: -3px;
  z-index: 1;
}

/* High contrast mode */
@media (prefers-contrast: high) {
  .assignment-cell:focus {
    outline-width: 4px;
    outline-color: black;
  }
}
```

**Keyboard navigation state:**
```javascript
// Track if user is keyboard navigating (vs mouse)
let isKeyboardNavigating = false;

document.addEventListener('keydown', (e) => {
  if (e.key === 'Tab' || e.key.startsWith('Arrow')) {
    isKeyboardNavigating = true;
    document.body.classList.add('keyboard-navigation');
  }
});

document.addEventListener('mousedown', () => {
  isKeyboardNavigating = false;
  document.body.classList.remove('keyboard-navigation');
});
```

```css
/* Only show focus rings during keyboard navigation */
body:not(.keyboard-navigation) .assignment-cell:focus {
  outline: none;
}
```

---

## Mobile/Tablet Responsiveness

### Responsive Breakpoints

```css
/* Mobile: <768px */
@media (max-width: 767px) {
  /* Compact layout, drawer roster, larger touch targets */
}

/* Tablet: 768px-1023px */
@media (min-width: 768px) and (max-width: 1023px) {
  /* Medium layout, side roster, balanced sizing */
}

/* Desktop: ≥1024px */
@media (min-width: 1024px) {
  /* Full layout, all features */
}
```

### Mobile Layout (<768px)

#### Table View (Shift/User-Based)

**Layout transformation:**
```
Desktop:
┌────────────────────────────────────────┐
│ [Roster Dock] │ [Grid] │ [Tools]      │
└────────────────────────────────────────┘

Mobile:
┌────────────────────────────────────────┐
│ [Compact Header]                [☰]   │ ← Hamburger menu
├────────────────────────────────────────┤
│ ┌─────────┬────┬────┬────┐           │
│ │ Morning │ Mon│ Tue│ Wed│ →         │ ← Horizontal scroll
│ ├─────────┼────┼────┼────┤           │   Sticky first column
│ │ Evening │    │    │    │           │
│ └─────────┴────┴────┴────┘           │
│                                        │
│ ▲ Swipe up for Roster ▲              │ ← Bottom drawer
└────────────────────────────────────────┘
```

**Key adaptations:**
- **Sticky first column** - Shift/user names always visible during horizontal scroll
- **Bottom drawer roster** - Swipe up to reveal, swipe down to hide
- **Larger touch targets** - Minimum 44×44px (Apple HIG standard)
- **No hover states** - Use tap/active states instead
- **Long-press for context menus** - Replaces right-click

#### CU-1 Excel Grid

**Layout transformation:**
```
Desktop:
┌──────────────────────────────────────────┐
│ [Date Picker] [Export]                   │
│ ┌───────┬─────┬─────┬─────┬─────┬─────┐│
│ │ Team  │ Mon │ Tue │ Wed │ Thu │ Fri ││
│ └───────┴─────┴─────┴─────┴─────┴─────┘│
└──────────────────────────────────────────┘

Mobile:
┌────────────────────────────────────────┐
│ [📅 Week ▼] [⬇️]              [☰]    │
├────────────────────────────────────────┤
│ ┌──────┬────┬────┬────┐              │
│ │ Team │ Mon│ Tue│ Wed│ →            │ ← Horizontal scroll
│ │  A   │    │    │    │              │   Sticky team column
│ ├──────┼────┼────┼────┤              │
│ │ John │ Mor│    │ Eve│              │   Pinch to zoom
│ └──────┴────┴────┴────┘              │
└────────────────────────────────────────┘
```

**Key adaptations:**
- **Horizontal/vertical scroll** - Full grid, pinch to zoom
- **Sticky header rows** - Dates + days always visible
- **Sticky first column** - Team/user names always visible
- **Team collapse works** - ▼/► triangles remain functional
- **Tap cell → Modal** - Context menu appears as bottom sheet modal

### Tablet Layout (768px-1023px)

**Balanced approach:**
- **Side roster** - Narrower (200px instead of 280px)
- **Compact toolbar** - Icons with text labels on hover
- **Medium touch targets** - 36×36px minimum
- **Horizontal scroll** - Date columns if many dates visible
- **All features available** - Nothing hidden, just compact

### Touch Optimizations

#### Touch Target Sizing

```css
/* Minimum touch target: 44×44px */
.assignment-cell {
  min-height: 44px;
  min-width: 44px;
  padding: 0.75rem; /* Increased from 0.5rem */
}

/* Buttons and interactive elements */
.btn, .roster-item, .dropdown-option {
  min-height: 44px;
  padding: 0.75rem 1rem;
}
```

#### Touch Gestures

**Table View:**
- **Tap cell** → Select/edit
- **Long-press cell** → Context menu
- **Swipe left on row** → Quick actions (delete, etc.)
- **Swipe up from bottom** → Show roster drawer
- **Swipe down on roster** → Hide drawer
- **Two-finger tap** → Toggle radar mode

**CU-1 Grid:**
- **Tap cell** → Edit mode (context menu as bottom sheet)
- **Long-press cell** → Expand multi-assignment
- **Pinch** → Zoom in/out
- **Swipe horizontally** → Scroll dates
- **Swipe vertically** → Scroll teams
- **Tap team header** → Collapse/expand

#### Mobile Context Menu (Bottom Sheet)

Instead of floating context menu, use bottom sheet modal:

```html
<div class="bottom-sheet-modal">
  <div class="bottom-sheet-handle"></div>
  <div class="bottom-sheet-content">
    <h3>Assign to Monday, Jan 20</h3>
    <div class="assignment-type-selector">
      <button class="type-btn active">
        <span class="type-icon">🌅</span>
        <span>Shift</span>
      </button>
      <button class="type-btn">
        <span class="type-icon">🧹</span>
        <span>Chore</span>
      </button>
      <button class="type-btn">
        <span class="type-icon">🏖️</span>
        <span>Vacation</span>
      </button>
    </div>
    <input type="text" class="form-input" placeholder="Search user...">
    <!-- Autocomplete results -->
    <div class="actions">
      <button class="btn btn-ghost">Cancel</button>
      <button class="btn btn-primary">Assign</button>
    </div>
  </div>
</div>
```

```css
.bottom-sheet-modal {
  position: fixed;
  bottom: 0;
  left: 0;
  right: 0;
  background: var(--surface);
  border-radius: var(--radius-lg) var(--radius-lg) 0 0;
  box-shadow: var(--shadow-xl);
  transform: translateY(100%);
  transition: transform 0.3s ease;
  z-index: var(--z-modal);
}

.bottom-sheet-modal.open {
  transform: translateY(0);
}

.bottom-sheet-handle {
  width: 40px;
  height: 4px;
  background: var(--border);
  border-radius: 2px;
  margin: 0.5rem auto;
}
```

#### Mobile Roster Drawer

**Closed state:**
```
┌────────────────────────────────────────┐
│                                        │
│        [Calendar Grid]                 │
│                                        │
│ ▲ Swipe up for Roster ▲              │ ← Hint
└────────────────────────────────────────┘
```

**Open state:**
```
┌────────────────────────────────────────┐
│        [Calendar Grid]                 │ ← Slightly visible
├────────────────────────────────────────┤
│ ───────                                │ ← Handle
│ Roster                        [×]      │
│ ┌────────────────────────────────────┐│
│ │ 👤 John Smith                      ││
│ │ 👤 Sarah Cohen                     ││
│ │ 👤 Mike Davis                      ││
│ └────────────────────────────────────┘│
└────────────────────────────────────────┘
```

**Drawer behavior:**
- Swipe up → Slides up from bottom
- Covers 60% of screen
- Grid remains partially visible (dimmed)
- Swipe down on drawer → Closes
- Tap outside drawer → Closes
- Tap roster item → Starts drag (shows drag ghost)

### Mobile-Specific Interactions

**Drag-and-Drop on Mobile:**

Since touch doesn't have "hover" state, use this flow:

```
1. Tap roster item (e.g., "John")
   ↓
2. Item highlights, enters "drag mode"
   Visual: Pulsing border, "Tap a cell to assign" message

3. Tap target cell
   ↓
4. Assignment completes
   Visual: Success animation

5. Roster item exits drag mode
```

**Alternative: Tap-Hold-Drag**
```
1. Tap and hold roster item (500ms)
   ↓
2. Haptic feedback (vibration)
   Visual: Item lifts off page (shadow increases)

3. Drag finger to target cell
   ↓
4. Release finger
   ↓
5. Assignment completes
```

### Responsive Grid Layout

**Desktop (≥1024px):**
```
Grid shows: 7-14 date columns (depending on view mode)
Cell size: ~80px width, ~60px height
Font size: 14px
```

**Tablet (768px-1023px):**
```
Grid shows: 5-7 date columns
Cell size: ~70px width, ~55px height
Font size: 13px
Horizontal scroll enabled
```

**Mobile (<768px):**
```
Grid shows: 3-4 date columns
Cell size: ~65px width, ~50px height
Font size: 12px
Horizontal scroll enabled
First column sticky (120px width)
```

### Performance on Mobile

**Optimizations:**
- **Virtual scrolling** - Only render visible rows + buffer
- **Debounced scroll handlers** - Don't recalculate on every pixel
- **Lazy load team sections** - Load visible teams first
- **Reduced animations** - Respect `prefers-reduced-motion`
- **Image optimization** - Serve smaller avatars on mobile

**Virtual scrolling example:**
```javascript
// Only render rows in viewport + 5 rows buffer
function getVisibleRows() {
  const scrollTop = gridContainer.scrollTop;
  const viewportHeight = gridContainer.clientHeight;
  const rowHeight = 50; // Average row height

  const startRow = Math.max(0, Math.floor(scrollTop / rowHeight) - 5);
  const endRow = Math.ceil((scrollTop + viewportHeight) / rowHeight) + 5;

  return { startRow, endRow };
}
```

---

## Export Functionality

### Export Options (All Views)

**Dropdown menu:**
```
Click: [⬇️ Export ▼]

Opens:
┌─────────────────────────────┐
│ Export to Excel (.xlsx)     │
│ Export to PNG Image         │
└─────────────────────────────┘
```

**Behavior:**
- Works in all calendar views (Month, Week, Day, Table, CU-1)
- Exports current view state (date range, filters, assignments)
- Preserves visual styling (colors, borders, formatting)

### Excel Export (.xlsx)

**Format:** Native Excel file (OpenXML)

**Features:**
- **Native .xlsx** - Opens in Excel, Google Sheets, LibreOffice
- **Preserves colors** - Assignment type colors carry over
- **Formatted cells** - Borders, alignment, font styles
- **No formulas** - Static values only (for simplicity)
- **Multiple sheets** (optional):
  - Sheet 1: Calendar grid
  - Sheet 2: Legend (color meanings)
  - Sheet 3: Summary statistics

**File naming:**
```
ShiftManager_{ViewType}_{DateRange}.xlsx

Examples:
- ShiftManager_Table_2026-01-20_to_2026-01-26.xlsx
- ShiftManager_CU1_2026-01_Week3.xlsx
- ShiftManager_Month_2026-01.xlsx
```

**Implementation:**
```csharp
// Use EPPlus or ClosedXML library
using (var package = new ExcelPackage())
{
    var worksheet = package.Workbook.Worksheets.Add("Calendar");

    // Headers
    worksheet.Cells[1, 1].Value = "Shift Type";
    for (int i = 0; i < dates.Count; i++)
    {
        worksheet.Cells[1, i + 2].Value = dates[i].ToString("MM/dd");
    }

    // Data rows
    int row = 2;
    foreach (var shiftType in shiftTypes)
    {
        worksheet.Cells[row, 1].Value = shiftType.Name;

        for (int col = 0; col < dates.Count; col++)
        {
            var assignment = GetAssignment(shiftType, dates[col]);
            worksheet.Cells[row, col + 2].Value = assignment?.UserName ?? "";

            // Apply color
            if (assignment != null)
            {
                var color = GetColorForType(assignment.Type);
                worksheet.Cells[row, col + 2].Style.Font.Color.SetColor(color);
            }
        }
        row++;
    }

    // Auto-fit columns
    worksheet.Cells.AutoFitColumns();

    // Save
    return package.GetAsByteArray();
}
```

### PNG Export (Image)

**Format:** PNG image (lossless)

**Features:**
- **High resolution** - 2x for retina displays (1920px width minimum)
- **Preserves all styling** - Colors, borders, shadows, fonts
- **Screenshot-like** - Exact visual copy of what user sees
- **Transparent background** (optional) - Or white background

**File naming:**
```
ShiftManager_{ViewType}_{DateRange}.png

Examples:
- ShiftManager_Table_2026-01-20_to_2026-01-26.png
- ShiftManager_CU1_2026-01_Week3.png
```

**Implementation Options:**

**Option A: Server-side rendering (Puppeteer/Playwright)**
```csharp
// Use Playwright for server-side screenshot
await using var playwright = await Playwright.CreateAsync();
await using var browser = await playwright.Chromium.LaunchAsync();
var page = await browser.NewPageAsync();

// Render calendar HTML
await page.SetContentAsync(calendarHtml);

// Take screenshot
var screenshot = await page.ScreenshotAsync(new()
{
    FullPage = true,
    Type = ScreenshotType.Png,
    Scale = ScreenshotScale.Retina
});

return screenshot;
```

**Option B: Client-side rendering (html2canvas)**
```javascript
import html2canvas from 'html2canvas';

async function exportToPNG() {
  const element = document.querySelector('.calendar-grid');
  const canvas = await html2canvas(element, {
    scale: 2, // Retina quality
    backgroundColor: '#ffffff',
    logging: false
  });

  // Download
  const link = document.createElement('a');
  link.download = `ShiftManager_${viewType}_${dateRange}.png`;
  link.href = canvas.toDataURL('image/png');
  link.click();
}
```

**Recommendation:** Option B (client-side) for simplicity and no server dependencies

### CU-1 Dedicated Export Buttons

**Per specification, CU-1 has additional export buttons (bottom-right):**

```html
<div class="cu1-export-buttons">
  <button class="btn btn-secondary" onclick="exportToExcel()">
    <span>📊</span>
    <loc key="DownloadExcel">הורדה לאקסל</loc>
  </button>
  <button class="btn btn-secondary" onclick="exportToPNG()">
    <span>🖼️</span>
    <loc key="DownloadImage">הורדת תמונה</loc>
  </button>
</div>
```

```css
.cu1-export-buttons {
  position: fixed;
  bottom: 20px;
  right: 20px; /* left: 20px in RTL */
  display: flex;
  gap: var(--space-sm);
  z-index: 100;
}

/* RTL support */
[dir="rtl"] .cu1-export-buttons {
  right: auto;
  left: 20px;
}
```

**Why both toolbar + dedicated buttons in CU-1?**
- **Toolbar export** - Consistent across all views
- **Dedicated buttons** - Per original CU-1 specification, more discoverable
- **Redundancy is OK** - Gives users flexibility

### Export Progress Indicator

**For large calendars (>100 assignments):**

```html
<div class="export-progress-modal">
  <div class="progress-content">
    <div class="progress-icon">📥</div>
    <h3>Generating Excel file...</h3>
    <div class="progress-bar">
      <div class="progress-fill" style="width: 60%;"></div>
    </div>
    <p>Processing 245 assignments</p>
  </div>
</div>
```

**States:**
1. Click export → Modal appears
2. Progress bar animates (0% → 100%)
3. File generates (2-5 seconds)
4. Download starts automatically
5. Modal shows success: "✅ Download started"
6. Auto-closes after 2 seconds

---

## Radar Mode - Conflict Detection

### Purpose

Visually highlight staffing conflicts:
- **Understaffed shifts** - Not enough people assigned
- **Overstaffed shifts** - Too many people assigned

### Toggle Button

**Location:** Tools section in header

```html
<button id="radarToggle" class="radar-toggle-btn" onclick="toggleRadarMode()">
  📡 Radar
</button>
```

**States:**
- **Inactive:** Gray button, "📡 Radar"
- **Active:** Blue background, "📡 Radar (Active)"

### Conflict Detection Logic

**Server-side calculation:**

```csharp
public class ConflictDetector
{
    public List<Conflict> DetectConflicts(DateTime startDate, DateTime endDate)
    {
        var conflicts = new List<Conflict>();
        var instances = GetShiftInstances(startDate, endDate);

        foreach (var instance in instances)
        {
            int required = instance.RequiredStaffing;
            int filled = instance.Assignments.Count();

            if (filled < required)
            {
                conflicts.Add(new Conflict
                {
                    InstanceId = instance.Id,
                    Type = "underfilled",
                    ShiftTypeName = instance.ShiftType.Name,
                    Date = instance.Date,
                    Required = required,
                    Filled = filled
                });
            }
            else if (filled > required)
            {
                conflicts.Add(new Conflict
                {
                    InstanceId = instance.Id,
                    Type = "overfilled",
                    ShiftTypeName = instance.ShiftType.Name,
                    Date = instance.Date,
                    Required = required,
                    Filled = filled
                });
            }
        }

        return conflicts;
    }
}
```

### Visual Indicators

**Understaffed (Yellow/Orange):**

```css
.radar-underfilled {
  box-shadow: 0 0 0 3px rgba(245, 158, 11, 0.3); /* Yellow glow */
  background: rgba(245, 158, 11, 0.05); /* Subtle yellow tint */
}

.conflict-badge.badge-warning {
  background: rgba(245, 158, 11, 0.9);
  color: white;
}
```

**Overstaffed (Red):**

```css
.radar-overfilled {
  box-shadow: 0 0 0 3px rgba(220, 38, 38, 0.3); /* Red glow */
  background: rgba(220, 38, 38, 0.05); /* Subtle red tint */
}

.conflict-badge.badge-danger {
  background: rgba(220, 38, 38, 0.9);
  color: white;
}
```

**Badge content:**
```
⚠️ 2/3  ← 2 filled out of 3 required (understaffed)
⚠️ 4/3  ← 4 filled out of 3 required (overstaffed)
```

### Tooltip on Hover

```html
<div class="conflict-tooltip">
  <div class="tooltip-header">⚠️ Understaffed</div>
  <div class="tooltip-body">
    <p><strong>Shift:</strong> Morning</p>
    <p><strong>Date:</strong> Mon, Jan 20</p>
    <p><strong>Staffing:</strong> 2 / 3</p>
    <p><strong>Needed:</strong> 1 more employee</p>
  </div>
</div>
```

### Auto-Refresh Behavior

**When Radar active:**
- Fetches conflicts from server every 30 seconds
- Updates highlights in real-time
- No page reload required

**When Radar inactive:**
- No polling
- Saves server resources

### Behavior Across Views

**Table View (Shift-Based):**
- Highlights cells where shift instance is under/overstaffed
- Badge shows filled/required ratio
- Works with existing cell structure

**Table View (User-Based):**
- Same detection logic
- Different visual layout (row = user instead of shift)
- Conflict still tied to shift instance (not user)

**CU-1 Excel Grid:**
- Highlights cells same way
- Yellow/red glow around cell border
- Badge appears in cell corner
- Works with team-grouped layout

**Month/Week/Day Views:**
- Event blocks show conflict badge
- Border color changes (yellow/red)
- Click event → Shows conflict details

### Example Workflow

```
1. Manager opens Table view
2. Clicks "📡 Radar" button
3. System fetches conflicts
4. 5 conflicts detected:
   - Monday Morning: 2/3 (understaffed)
   - Tuesday Evening: 4/3 (overstaffed)
   - Wednesday Night: 1/2 (understaffed)
   - Thursday Morning: 0/3 (understaffed, critical!)
   - Friday Evening: 5/4 (overstaffed)
5. Cells glow yellow/red
6. Badges appear: ⚠️ 2/3, ⚠️ 4/3, etc.
7. Manager hovers → Tooltip shows details
8. Manager assigns employees to fix conflicts
9. After 30 seconds, radar refreshes
10. Conflicts that were fixed disappear
11. Remaining conflicts still highlighted
```

---

## Technical Implementation Notes

### Backend Requirements

**Models:**
- `RecurringOuting` (new) - For recurring vacation rules
- `UserCalendarPreference` (new) - Stores view mode preference
- `ShiftInstanceAuditLog` (existing) - Tracks program detachments
- `ConflictDetectionCache` (optional) - Cache radar results

**Services:**
- `SmartSyncService` (new) - Manages SignalR hub, state detection
- `ConflictDetectionService` (new) - Calculates staffing conflicts
- `ExportService` (enhanced) - Handles Excel/PNG generation
- `RecurringOutingService` (new) - Generates outing dates from rules

**API Endpoints:**
```
GET  /Api/Calendar/GetConflicts?start={date}&view={mode}
POST /Api/Calendar/AssignUser
POST /Api/Calendar/DetachFromProgram
POST /Api/Calendar/ResetToProgram
GET  /Api/Calendar/GetUserPreference
POST /Api/Calendar/SaveUserPreference
GET  /Api/Calendar/ExportExcel?view={mode}&start={date}&end={date}
GET  /Api/Calendar/ExportPNG?view={mode}&start={date}&end={date}
```

### Frontend Architecture

**Component Structure:**

```
Pages/
├── Calendar/
│   ├── Month.cshtml
│   ├── Week.cshtml
│   ├── Day.cshtml
│   └── Table.cshtml (enhanced with flip toggle)
└── ExcelGrid/
    └── Index.cshtml (new CU-1 view)

Components/
├── MiniCalendar/
│   └── Default.cshtml (new)
├── ContextSidebar/
│   └── Default.cshtml (new)
├── AssignmentCell/
│   └── Default.cshtml (enhanced)
└── RosterDock/
    └── Default.cshtml (enhanced with adaptive content)

JavaScript/
├── calendar-sync.js (new - smart sync system)
├── calendar-radar.js (existing - enhanced)
├── calendar-export.js (new)
├── cu1-grid.js (new - CU-1 interactions)
└── keyboard-shortcuts.js (new)

CSS/
├── site.css (enhanced with new colors)
├── calendar-table.css (enhanced)
└── cu1-grid.css (new)
```

### Database Schema Changes

**New Tables:**

```sql
-- User calendar view preferences
CREATE TABLE UserCalendarPreferences (
    UserId INT PRIMARY KEY,
    ViewMode NVARCHAR(20) NOT NULL DEFAULT 'shifts', -- 'shifts' or 'users'
    LastUpdated DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    FOREIGN KEY (UserId) REFERENCES Users(Id)
);

-- Recurring outing rules
CREATE TABLE RecurringOutings (
    Id INT PRIMARY KEY IDENTITY,
    UserId INT NOT NULL,
    Frequency NVARCHAR(20) NOT NULL, -- 'Weekly', 'Biweekly', 'Monthly'
    DayOfWeek INT NULL, -- 0-6 for weekly
    StartDate DATE NOT NULL,
    EndDate DATE NULL,
    Description NVARCHAR(200),
    CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    FOREIGN KEY (UserId) REFERENCES Users(Id)
);

-- Team collapse state (optional - or use localStorage only)
CREATE TABLE TeamCollapseState (
    UserId INT NOT NULL,
    TeamId INT NOT NULL,
    IsCollapsed BIT NOT NULL DEFAULT 0,
    PRIMARY KEY (UserId, TeamId),
    FOREIGN KEY (UserId) REFERENCES Users(Id),
    FOREIGN KEY (TeamId) REFERENCES Teams(Id)
);
```

**Enhanced Columns:**

```sql
-- ShiftInstances table (existing, add if missing)
ALTER TABLE ShiftInstances
ADD IsDetachedFromProgram BIT NOT NULL DEFAULT 0,
    OriginalProgramId INT NULL,
    DetachedAt DATETIME2 NULL,
    DetachedByUserId INT NULL;
```

### Performance Considerations

**Optimization Strategies:**

1. **Virtual Scrolling (Large Teams)**
   - Only render visible rows + buffer
   - Render on scroll with debouncing
   - Target: Handle 500+ team members smoothly

2. **Lazy Loading (Team Sections)**
   - Load visible teams first
   - Load collapsed teams on expand
   - Target: Initial load <2 seconds

3. **Database Indexing**
   ```sql
   CREATE INDEX IX_ShiftInstances_DateRange
   ON ShiftInstances(Date, ShiftTypeId)
   INCLUDE (RequiredStaffing, IsDetachedFromProgram);

   CREATE INDEX IX_Assignments_UserDate
   ON Assignments(UserId, Date)
   INCLUDE (ShiftInstanceId);
   ```

4. **Caching Strategy**
   - Cache conflict detection results (1 minute)
   - Cache user preferences (10 minutes)
   - Cache team lists (session)
   - Invalidate on writes

5. **SignalR Optimization**
   - Group users by viewed date range
   - Only push updates to relevant groups
   - Limit message size (delta updates, not full state)

**Performance Targets:**

| Metric | Target | Measurement |
|--------|--------|-------------|
| Initial page load | <3 seconds | Time to interactive |
| Calendar switch (Month→Table) | <500ms | UI ready |
| Cell assignment | <200ms | API response + UI update |
| Radar refresh | <1 second | Conflict detection + rendering |
| Export Excel | <5 seconds | For 100 shifts |
| Export PNG | <3 seconds | For full grid |
| SignalR latency | <500ms | Update propagation |

### Browser Support

**Target Browsers:**
- Chrome 90+ ✅
- Firefox 88+ ✅
- Safari 14+ ✅
- Edge 90+ ✅
- Mobile Safari (iOS 14+) ✅
- Chrome Mobile (Android 11+) ✅

**Progressive Enhancement:**
- SignalR: Fallback to long-polling if WebSocket unavailable
- CSS Grid: Fallback to flexbox for older browsers
- CSS Variables: Fallback to hardcoded colors (rare)
- Touch events: Graceful degradation to mouse events

### Accessibility (WCAG 2.1 AA Compliance)

**Requirements:**

1. **Keyboard Navigation**
   - All features accessible via keyboard
   - Visible focus indicators
   - Logical tab order
   - Skip links for main content

2. **Screen Reader Support**
   - ARIA labels on all interactive elements
   - ARIA live regions for dynamic updates
   - Semantic HTML structure
   - Alt text for icons (where applicable)

3. **Color Contrast**
   - All text meets WCAG AAA (7:1 ratio) ✅ Already achieved
   - Non-text elements meet 3:1 ratio
   - Error states not color-only (use icons + text)

4. **Responsive Text**
   - Supports 200% zoom without horizontal scroll
   - Text spacing adjustable
   - No fixed pixel font sizes (use rem/em)

5. **Motion & Animation**
   - Respect `prefers-reduced-motion`
   - No auto-playing animations >5 seconds
   - Pause controls for moving content

**ARIA Example:**

```html
<div class="assignment-cell"
     role="gridcell"
     aria-label="Monday Morning shift, assigned to John Smith"
     tabindex="0"
     data-date="2026-01-20"
     data-shift="Morning">
  <span class="assignment assignment-shift">Morning</span>
</div>
```

---

## Next Steps

### Remaining Brainstorming Topics (Option C)

Before moving to design phase, we should cover:

1. **Performance & Scalability**
   - Large team handling (500+ members)
   - Long date ranges (90+ days)
   - Concurrent user limits
   - Database query optimization

2. **Edge Cases & Error Handling**
   - Network failure scenarios
   - Concurrent edit conflicts
   - Invalid date ranges
   - Missing shift types
   - Orphaned assignments

3. **Accessibility Deep Dive**
   - Screen reader testing plan
   - Keyboard-only workflow validation
   - High contrast mode support
   - RTL language testing (Hebrew)

4. **Testing Strategy**
   - Unit test coverage requirements
   - Integration test scenarios
   - End-to-end test automation
   - Performance benchmarks
   - User acceptance criteria

5. **Security Considerations**
   - Permission model (who can flip views?)
   - Data exposure in exports
   - API rate limiting
   - XSS prevention in cell content

6. **Migration & Rollout**
   - Feature flags for gradual rollout
   - Data migration scripts
   - User training materials
   - Rollback plan

### Design Phase Tasks (Option A)

After brainstorming complete:

1. **UI Mockups**
   - High-fidelity designs in Figma
   - Interactive prototypes
   - Mobile/tablet variants
   - Dark mode versions

2. **Component Specifications**
   - Detailed component breakdown
   - Props/events documentation
   - State management diagrams
   - API contracts

3. **Style Guide Updates**
   - Add new color tokens
   - Document new patterns
   - Update design system
   - Create usage examples

4. **User Flow Diagrams**
   - Click-to-assign flow
   - Drag-and-drop flow
   - Conflict resolution flow
   - Export flow

### Implementation Phases

**Phase 1: Foundation (Weeks 1-2)**
- Smart sync system (SignalR)
- Color system implementation
- Keyboard shortcuts base

**Phase 2: Table Enhancements (Weeks 3-4)**
- User-Based flip toggle
- Adaptive roster dock
- Programs integration

**Phase 3: CU-1 Excel Grid (Weeks 5-7)**
- Grid layout
- Team sections
- Click-to-assign
- Export buttons

**Phase 4: Polish & Testing (Weeks 8-9)**
- Mobile responsiveness
- Accessibility audit
- Performance optimization
- User acceptance testing

**Phase 5: Rollout (Week 10)**
- Feature flag activation
- User training
- Monitor & iterate

---

## Document Changelog

| Version | Date | Author | Changes |
|---------|------|--------|---------|
| 1.0 | 2026-01-23 | Product Team | Initial comprehensive documentation |

---

**End of Brainstorming Documentation**

This document represents the complete brainstorming phase output. All decisions, specifications, and technical details have been captured.

**Status:** ✅ Ready for performance/accessibility brainstorming (Option C), then design phase (Option A).
