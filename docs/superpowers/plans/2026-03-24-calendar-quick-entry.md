# Quick Entry Mode for Calendar

## Context
The calendar's current assignment flow requires 3-4 clicks per assignment (click cell → bottom sheet → select dropdown → click Assign). For managers scheduling 50+ assignments per session, this is slow. The goal is an Excel-like "type-in" mode where clicking a cell opens an inline autocomplete input — type a name, press Enter, Tab to next cell. Zero modals, zero page transitions.

## Design Decisions (User-Confirmed)
- **UX Pattern**: Grouped Autocomplete (Google Sheets-style) — type anything, see ALL matching entities grouped by type. Slash commands as power-user layer.
- **Tab behavior**: Tab moves RIGHT (next day, same row). Matches Excel column navigation.
- **Scope**: Phase 1 + 2 (full feature with slash commands, chore title entry, tooltips).
- **Desktop only**: Touch devices keep the existing bottom sheet.

## Scope

**Phase 1**: Calendar/Shifts page (both shift-mode and user-mode). This is the highest-ROI target.
**Future phases**: Calendar/Chores and Calendar/OnCall also use ExcelCalendarTable + bottom sheet and are natural candidates. Calendar/Month/Week/Day use custom grids and are NOT candidates.

- **Shift-mode** (rows = shift types): Simple — Quick Entry searches users only. Flat list, no grouping.
- **User-mode** (rows = users): Grouped — Quick Entry searches shifts, chores, and duty types. **Note: adding chores and duty types to the Shifts page is NEW functionality** — currently these are only on their dedicated pages (Calendar/Chores, Calendar/OnCall). Quick Entry brings them together for the first time.

### Permission Model
**Quick Entry does NOT pre-check permissions.** This follows the existing pattern — the bottom sheet never checks grants before showing the dropdown. The server POST handlers enforce grants and return 403 on unauthorized actions. Quick Entry handles 403 gracefully with an inline error toast.

### Scope / Hierarchy Context
All data shown in Quick Entry is already scoped by the page's molecule + jobType selectors:
- **Users** (shift-mode): From `GetUsersForCalendarAsync(moleculeId, jobTypeId)` — molecule + jobType scoped, cross-company within molecule
- **Shift types** (user-mode): From hidden `<select>` populated by `Model.ShiftTypes` — molecule + area overlay, jobType filtered
- **Chore types**: Molecule-scoped (from `IChoreTypeService`)
- **Duty types**: Global + area-scoped custom (from `OnDutyTypeConfig`)
- **ShiftGrouping** (Tzafon/Darom): Not filtered in Quick Entry — server-side validation handles eligible users per grouping

### JustMine Filter Interaction
**Known issue**: The hidden `<select>` contains ALL users even when JustMine is active (the filter only applies to calendar rows, not the select data). Quick Entry must handle this:
- When JustMine is active, **disable Quick Entry** (show toggle as grayed out with tooltip "Disable Just Mine to use Quick Entry")
- Alternative: Filter the autocomplete client-side to match JustMine — but this adds complexity for a rare use case

### Trainee Assignment
Quick Entry does NOT handle trainee assignment. Trainees use a separate dropdown (`traineeSelect`) and are a secondary action on an existing assignment. Users who want to add trainees use the bottom sheet (Quick Entry OFF) or click the existing trainee UI on the assignment chip.

## Key Design Details

### Toggle Button
Add `[⚡ Quick Entry]` button in the filter bar (`cal-toolbar__controls`), same row as Filter/JustMine/CapacityMode.
- Active state: `btn-primary` with icon change
- Persisted in `localStorage` (remembers user preference)
- Hidden on touch devices: `@media (pointer: coarse) { .quick-entry-toggle { display: none; } }`

### Inline Autocomplete (Grouped)
Click cell → inline `<input dir="auto">` appears inside the cell → type to search → grouped dropdown:

**Shift mode** (rows = shift types): Only show users. No grouping needed — simple flat list.
**User mode** (rows = users): Show grouped results:
- 📅 SHIFTS — shift types with time range: "Morning (08:00-16:00)". HOME appears here naturally (it's a shift type with key=HOME).
- 🧹 CHORES — chore types from molecule (loaded from new hidden `<select>`)
- 🛡 DAY SHIFTS — duty types from `OnDutyTypeConfig` (includes custom types beyond Hakam/Lead)

**Many-items handling**:
- Max 6 results per group visible (rest hidden with "… and X more")
- Fuzzy match ranking: exact prefix > word-start > substring > fuzzy
- Data source is already molecule+jobType scoped (from hidden `<select>`)
- 2-3 typed characters typically narrows to <5 results

**Scope context in dropdown items**:
- Shift types show time range: "Morning (08:00-16:00)" — parsed from the `<option>` text which already includes `(HH:mm-HH:mm)`
- Chore types show type name only (no time context)
- Users show display name only (company badge data is NOT in the hidden `<select>` — would need `data-company="Tzafona"` added to shift-mode user `<option>` elements for future enhancement)

### Keyboard Navigation
| Key | Action |
|-----|--------|
| Click cell / Enter on focused cell / Start typing | Open inline input |
| Arrow Up/Down | Navigate autocomplete options |
| Enter | Assign selected item |
| Tab | Assign + move to next cell RIGHT (next day) |
| Escape | Close input, restore cell |
| Arrow keys (when input empty) | Navigate between cells |

### Slash Commands (Power-User Layer)
Typing `/` in the input opens a command palette:
```
/shift  📅 Assign shift
/chore  🧹 Add chore
/duty   🛡 Add on-duty
/home   🏠 Home assignment
```
Selecting a command pre-filters the autocomplete to that entity type only. Adds a colored chip in the input: `[🧹 Chore] morning___`

### Chore Special Case
Selecting a chore type from dropdown → input transitions to title entry:
1. Chore type becomes a chip: `[🧹 Morning Inspection]`
2. Input placeholder changes to "Title…"
3. Enter commits both chore type + title
4. This is two steps but never leaves the cell

### Hebrew/RTL Support
- `<input dir="auto">` — auto-detects direction from first strong character
- Dropdown anchored with `inset-inline-start: 0` (logical property)
- Group headers localized (need **new .resx keys** — existing terms verified by Hebrew expert):
  - `QuickEntry_ShiftsGroup` = "Shifts" / "משמרות"
  - `QuickEntry_ChoresGroup` = "Chores" / "תורנויות"
  - `QuickEntry_DutyGroup` = "Day Shifts" / "כונניות" (reuses existing term from `DayShifts` key)
- **Fuzzy search with Hebrew normalization**:
  - Final-form letter equivalence: כ=ך, מ=ם, נ=ן, פ=ף, צ=ץ (normalize before matching)
  - Search `DisplayName` field only — **there is no separate English name field** on `AppUser`. Names are single-language (typically Hebrew in production).
  - No niqqud (diacritics) expected in user/shift names — no stripping needed
  - No existing autocomplete/fuzzy engine in the app — **must build from scratch**
- Slash commands are intentionally **English-only** (`/shift`, `/chore`, `/duty`, `/home`) — this is a power-user feature. Hebrew users use the grouped autocomplete (zero-prefix path) which requires no English.
- **Tab direction**: Tab moves RIGHT = next chronological day. The calendar renders dates left-to-right regardless of RTL page direction. This is correct and consistent with the existing calendar layout.
- **IME composition**: Track `compositionstart`/`compositionend` events. Suppress autocomplete updates while `isComposing` is true. Tab during composition completes the composition, doesn't advance cell. Relevant for Hebrew on some input methods and essential for Arabic.

### Input Element Details
- `autocomplete="off"` (or `autocomplete="new-password"` as backup) to prevent browser autofill interference
- `@media print { .quick-entry-toggle, .quick-entry-dropdown, .quick-entry-input { display: none !important; } }` in CSS
- Dropdown uses `position: fixed` and is clamped to viewport bounds (prevent overflow on narrow screens or cells near screen edges)
- Module pattern: IIFE matching `calendar-bottom-sheet.js` pattern, with `window.CalendarQuickEntry` public API

### Cell State Handling
- **Empty cells**: Input appears with `+` button hidden
- **Cells with existing assignments**: Input appears BELOW existing chips (appended to cell content area), chips remain visible
- **Read-only cells** (`excel-calendar__cell--readonly`): Quick Entry does NOT activate — click passes through to normal handler
- **Past-date cells** (`excel-calendar__cell--past`): Quick Entry does NOT activate
- **Fully-staffed cells**: Quick Entry activates but shows inline "⚠️ Full" toast if assignment fails, with "Expand capacity? Enter/Escape" prompt (NO native `confirm()`)

### Feature Interaction Coordination
**Critical: Existing click handlers must NOT compete with Quick Entry.**

| Feature | When Quick Entry ON | Mechanism |
|---------|-------------------|-----------|
| **Bottom sheet** (`+` button handler) | Disabled | `initAddButtonHandler()` uses **capture phase** (`addEventListener(..., true)`) — `stopPropagation` from Quick Entry (bubble phase) will NOT prevent it. Must add `if (window.quickEntryActive) return;` at line 957 of `calendar-bottom-sheet.js`, inside `initAddButtonHandler`, before the `addBtn` lookup. |
| **Fill handle** (drag handle in cell corner) | Hidden while input is active | Add `.quick-entry-editing` class to active cell → CSS hides `.excel-calendar__fill-handle` |
| **Remove buttons** (× on assignment chips) | Still work | Remove buttons are on child elements, Quick Entry listens on the cell — separate event targets |
| **Shadow refresh** (`calendar-realtime.js`) | Deferred | `triggerShadowRefresh()` is inside the IIFE — cannot be intercepted externally. Add `if (window.quickEntryActive) return;` at **two** locations inside the IIFE: (1) line 369 in the cell click handler, (2) line 435 in the `window focus` handler. |
| **SignalR full DOM replacement** | Deferred + restored | Same flag mechanism. Also covers the page's own `updateSingleCell()` function (distinct DOM mutation source from SignalR). (1) Save input value + cell coordinates before replacement, (2) after DOM update, re-create input in the same cell with saved value. Register `MutationObserver` on the table to catch both SignalR and `updateSingleCell` mutations. |

### Assignment Wrapper (replaces direct `confirm()` calls)
The existing `quickAddShift()`, `quickAddChore()`, and `quickAddOnDuty()` each have **2 `confirm()` calls** (6 total) — interleaved inside async/await chains. These cannot be simply wrapped; they need refactoring.

**Approach: Thread an async `confirmHandler` parameter through all 3 functions:**

```javascript
// Default handler (backward-compatible — bottom sheet still uses confirm())
const defaultConfirm = (msg) => Promise.resolve(confirm(msg));

// Quick Entry handler (inline toast with keyboard resolution)
const quickEntryConfirm = (msg) => new Promise((resolve) => {
    showInlineToast(msg + " Enter=yes, Esc=no", { onEnter: () => resolve(true), onEscape: () => resolve(false) });
});

// Refactored signatures:
window.quickAddShift = async function(shiftTypeId, date, userId, confirmHandler = defaultConfirm) { ... }
window.quickAddChore = async function(date, userId, title, forceAssign, choreTypeId, confirmHandler = defaultConfirm) { ... }
window.quickAddOnDuty = async function(date, userId, onDutyType, forceAssign, confirmHandler = defaultConfirm) { ... }
```

**Specific confirm sites to refactor** (verified by feature architect):
- `quickAddChore`: line 136 (vacation override) + line 172 (manage vacation redirect)
- `quickAddOnDuty`: line 263 (vacation override) + line 295 (manage vacation redirect)
- `quickAddShift`: line 374 (capacity full) + line 382 (warnings/override)

**"Manage vacation" second confirm**: In Quick Entry mode, the `confirmHandler` returns `false` for "navigate to manage vacation" — this action is not available during Quick Entry (user should use bottom sheet for complex vacation management). The inline toast says: "Vacation conflict. Enter=override, Esc=skip."

**Estimated change: ~60 lines across `calendar-inline-edit.js`** (not 20 as previously stated).

### Conflict/Warning Handling
When assignment returns a warning (vacation overlap, weekly cap):
- Toast appears inline below the input: "⚠️ Avi has vacation. Enter to override, Escape to skip."
- Tab-to-advance pauses until resolved
- Override uses existing HMAC token validation flow
- **No native `confirm()` or `alert()` calls** — all prompts are inline

### First-Use Tooltip
On first activation of Quick Entry, show a dismissible tooltip:
> "Type to assign, Tab to move right, Escape to cancel. Use / for chores and more."

Stored in `localStorage`. Shown 3 times, then auto-hides forever.

## Files to Create/Modify

### New Files
- `wwwroot/js/calendar-quick-entry.js` (~650-700 lines)
  - Toggle handler + state management
  - Inline input creation + positioning
  - Autocomplete engine (fuzzy search, grouping, rendering)
  - Keyboard navigation (Tab/Enter/Escape/Arrows)
  - Slash command palette
  - Chore title inline entry
  - First-use tooltip
  - Integration with existing `quickAddShift()`, `quickAddChore()`, `quickAddOnDuty()`

- `wwwroot/css/calendar-quick-entry.css` (~100-150 lines)
  - `.quick-entry-input` — inline input styling (underline, not bordered)
  - `.quick-entry-dropdown` — autocomplete dropdown with groups
  - `.quick-entry-group-header` — colored section headers
  - `.quick-entry-item` — result items with hover/active states
  - `.quick-entry-chip` — entity type chip (for slash commands)
  - `.quick-entry-active` — cell highlight when in quick entry mode
  - `.excel-calendar--quick-entry` — grid-level modifier class
  - RTL overrides using logical properties

### Modified Files
- `Pages/Calendar/Shifts.cshtml.cs` — Load `ChoreTypes` and `DutyTypeConfigs` for selected molecule (add to OnGet, ~10 lines)
- `Pages/Calendar/Shifts.cshtml` — Add toggle button in filter bar, add hidden `<select>` for chore types + duty types, load new JS/CSS
- `wwwroot/js/calendar-bottom-sheet.js` — Add `if (window.quickEntryActive) return;` at line 957 inside `initAddButtonHandler` (capture-phase handler). 1 line.
- `wwwroot/js/calendar-realtime.js` — Add `if (window.quickEntryActive) return;` at line 369 (click handler) AND line 435 (focus handler) inside the IIFE. 2 lines.
- `wwwroot/js/calendar-inline-edit.js` — Thread `async confirmHandler` parameter through `quickAddShift` (line 374, 382), `quickAddChore` (line 136, 172), `quickAddOnDuty` (line 263, 295). Replace 6 `confirm()` calls with `await confirmHandler(msg)`. Add default handler for backward compat. ~60 lines changed.
- `Pages/Calendar/Shifts.cshtml` — Add `data-key="@st.Key"` to shift type `<option>` elements (line 210). Add hidden selects for chore types + duty types. Add toggle button. Load new JS/CSS.
- `Resources/SharedResources.resx` — Add Quick Entry group header keys (EN)
- `Resources/SharedResources.he-IL.resx` — Add Quick Entry group header keys (HE: משמרות, תורנויות, כונניות)

### Existing Code to Reuse
- `wwwroot/js/calendar-inline-edit.js` — `quickAddShift()`, `quickAddChore()`, `quickAddOnDuty()`, `deleteItem()`, `showToast()`
- `wwwroot/js/calendar-bottom-sheet.js` — `extractCellData()`, `getAvailableUsers()` (Strategy 1 reads from hidden `<select>`)
- Hidden `<select data-role="assignee-select">` — pre-populated user/shifttype options
- `ViewComponents/ExcelCalendarTable` — cell structure with `data-row-id`, `data-date`, `tabindex="0"`
- Design tokens: `var(--primary)`, `var(--surface-soft)`, `var(--text-muted)`, etc.

### Data Sources for Autocomplete
- **Users** (shift mode): Hidden `<select data-role="assignee-select" data-item-type="user">` — already on page
- **Shift types** (user mode): Hidden `<select data-role="assignee-select" data-item-type="shifttype">` — already on page. **Note**: HOME shift type is already in this list — do NOT add a separate "Home" group; it appears under SHIFTS group naturally
- **Chore types** (user mode): **New hidden data container required** — add `<select id="choreTypeSelect-quickentry" style="display:none">` to `Shifts.cshtml`, populated server-side from `IChoreTypeService.GetChoreTypesAsync(moleculeId)`. This requires a minor server-side change to the page model.
- **Duty types** (user mode): **Load from `OnDutyTypeConfig` table**, not static enum — custom duty types exist with `TypeValue >= 2`. Add hidden `<select id="dutyTypeSelect-quickentry">` populated server-side. Minor server-side change.
- **Home**: Part of the Shift types hidden `<select>`. **Currently NO `data-key` attribute exists** on `<option>` elements — must add `data-key="@st.Key"` to Shifts.cshtml (1-line Razor fix). This enables the `/home` slash command to identify HOME by key.

### Backend Changes (Minor)
**The "no backend changes" claim was incorrect.** Two minor server-side additions are needed:

1. **`Shifts.cshtml.cs` OnGet**: Load chore types and duty types for the selected molecule, expose as page properties
2. **`Shifts.cshtml`**: Render two new hidden `<select>` elements with chore type and duty type options

These are ~10 lines of C# + ~20 lines of Razor. No new API endpoints, no model changes, no migration.

## Verification Plan

### Core Functionality
1. **Build**: `dotnet build` — passes with minor server-side additions
2. **Toggle**: Quick Entry button appears in filter bar, persists state in localStorage
3. **Shift mode**: Click empty cell → type user name → autocomplete shows matches → Enter assigns → Tab moves right
4. **User mode**: Click empty cell → type → grouped dropdown (shifts/chores/duty) → select assigns
5. **Slash commands**: Type `/` → palette appears → select type → filtered autocomplete
6. **Chore flow**: Select chore type → chip appears → type title → Enter creates chore
7. **Hebrew**: Type Hebrew name → RTL input → matches Hebrew display names → dropdown RTL
8. **Hebrew final forms**: Search "שלו" matches "שלום" (mem-final equivalence)
9. **Tab navigation**: Assign → Tab → next day cell activates with input ready
10. **Escape**: Closes input, restores cell to normal state

### Edge Cases
11. **Populated cells**: Click cell with existing assignment → input appears below chips, chips remain visible
12. **Read-only cells**: Click read-only cell → Quick Entry does NOT activate
13. **Past-date cells**: Click past cell → Quick Entry does NOT activate
14. **Fully-staffed shift**: Assign to full shift → inline toast "Full. Enter=expand" (no `confirm()`)
15. **Empty search results**: Type gibberish → dropdown shows "No matches" message
16. **Many results**: Type 1 char → max 6 per group visible → type more → narrows

### Feature Interactions
17. **Touch devices**: Toggle button hidden, bottom sheet still works normally
18. **Bottom sheet OFF**: Quick Entry ON → click `+` button → Quick Entry handles it, NOT bottom sheet
19. **Bottom sheet ON**: Quick Entry OFF → click `+` button → bottom sheet works normally
20. **Fill handle**: Quick Entry input open → fill handle hidden → close input → fill handle reappears
21. **SignalR update during edit**: Another user makes a change → DOM update deferred until input closes → then refreshes
22. **Shadow refresh**: Click cell → no shadow refresh fires while input is active

### Warnings & Overrides
23. **Vacation conflict**: Assign user with vacation → inline toast with Enter=override / Escape=skip
24. **Weekly cap warning**: Same inline toast pattern, no native dialogs
25. **Existing features preserved**: Bottom sheet, fill handle, remove buttons, real-time updates all work when Quick Entry is OFF

### Scope & Permissions
23. **Unauthorized action**: User without grant tries Quick Entry assign → server 403 → inline toast "Permission denied"
24. **JustMine active**: Quick Entry toggle grayed out with tooltip
25. **Molecule/JobType switch mid-edit**: Changing dropdowns while input is open → close input, page reloads with new data
26. **CapacityMode view**: Quick Entry still works — capacity is display-only, server validates

### Accessibility
27. **ARIA**: Input has `role="combobox"`, dropdown has `role="listbox"`, items have `role="option"`, `aria-activedescendant` tracks selection
28. **First-use tooltip**: Shows 3 times then hides permanently (localStorage counter)

### Calendar Pages NOT Affected (verified)
29. **Calendar/Table** (legacy): No Quick Entry — deprecated, no bottom sheet
30. **Calendar/Overview**: No Quick Entry — view-only (day notes only)
31. **Calendar/Month/Week/Day**: No Quick Entry — custom grid, not ExcelCalendarTable
32. **Home/Index**: No Quick Entry — dashboard only
