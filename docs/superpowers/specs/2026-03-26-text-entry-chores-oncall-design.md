# Text Entry: Chores & OnCall Extension + Cross-Cutting Fixes

**Date:** 2026-03-26
**Status:** Approved (reviewed by QA architect + feature architect)
**Depends on:** Calendar Text Entry feature (implemented same day on Shifts page)

## Context

The Text Entry feature lets users save free-text annotations via Quick Entry when typed text doesn't match any defined item. It's currently live on the Shifts page only. This spec extends it to Chores (full support) and OnCall (read-only overlay badge), while also fixing two cross-cutting issues identified during architectural review.

## Scope Summary

| Calendar | Create | Display as Chips | Overlay Badge |
|---|---|---|---|
| Shifts (user-mode) | Done | Done | n/a |
| Shifts (shift-mode) | No | n/a | Done |
| **Chores** | **New** | **New** | n/a |
| **OnCall** | No | No | **New (read-only)** |

Text entries are **shared across calendars** — same `CalendarTextEntry` records appear on both Shifts and Chores.

## Changes

### 1. Fix: Cross-Company Tenant Scoping (affects Shifts + Chores)

**Problem:** `GetForDateRangeAsync` uses the tenant query filter (`CompanyId = caller's company`). But Chores and Shifts pages show users from ALL companies in the molecule via `IgnoreQueryFilters()`. Text entries created for cross-company users become invisible.

**Fix:**
- Add new service method `GetForUsersAndDateRangeAsync(IEnumerable<int> userIds, DateOnly start, DateOnly end)` that uses `IgnoreQueryFilters()` with explicit `WHERE UserId IN (...)` filtering
- Fix `AddAsync`: look up target user's CompanyId and set it explicitly. The `CompanyIdInterceptor` only fires when `CompanyId == 0`, so explicit assignment prevents it from overwriting with the caller's company.
- Update callers in both Shifts and Chores pages to use the new method with appropriate user ID lists

**Files:** `ICalendarTextEntryService.cs`, `CalendarTextEntryService.cs`, `Shifts.cshtml.cs`

### 2. Chores Page — Full Text Entry (Create + Display)

Chores rows use `user-{userId}` format — identical to Shifts user-mode. The existing JS `selectItem` handler for `text-entry` type already works for `user-*` rowIds. Both JS files are already loaded on the Chores page.

**Backend (`Chores.cshtml.cs`):**
- Inject `ICalendarTextEntryService`
- Load text entries via new molecule-aware method (pass all user IDs from the molecule)
- In `BuildCellsForUser()`, add text entries as `ExcelCalendarAssignment` chips with `Role = "text-entry"` (same pattern as Shifts)
- Update method signature to accept the text entries dictionary

**Frontend:** No JS or CSS changes needed for Chores.

### 3. OnCall Page — Read-Only Overlay Badge

OnCall rows use `dutytype-{typeValue}` — not user-based. Text entries appear only as overlay badges.

**Backend (`OnCall.cshtml.cs`):**
- Inject `ICalendarTextEntryService`
- Load text entries for all assigned on-duty user IDs
- In `BuildCellsForDutyType()`, check if any assigned user has text entries for that date → set `HasTextEntry = true` + `TextEntryTexts` on overlay
- Use `cell.Overlay ??= new ExcelCalendarOverlay()` since OnCall cells don't currently have overlays

**Frontend:** Badge already renders from shared `_CalendarRow.cshtml`.

### 4. JS Guard: Prevent "Save as Text" on Non-User Rows

**Problem:** OnCall reports `getCurrentMode() === 'user'` but has `dutytype-*` rows. The "Save as text" option appears but clicking it does nothing.

**Fix:** Add `activeInput._cellData.rowId.indexOf('user-') === 0` check to the dropdown rendering condition in `calendar-quick-entry.js` (~line 355).

### 5. Fix: Bottom-Sheet Text Entry Deletion Routing

**Problem (pre-existing):** On mobile, tapping x on a text-entry chip in the bottom sheet calls `deleteItem('chore', id)` instead of `deleteTextEntry(id)`. Wrong endpoint, wrong entity.

**Fix (two changes required):**
1. In `extractCellData` (~line 882): read `data-entry-type` from the assignment DOM element and include `entryType` in the parsed assignment object
2. In `handleRemoveAssignment` (~line 783): check `assignment.entryType === 'text'` BEFORE the calendar-type branching and route to `window.deleteTextEntry(assignment.id)`

## Files to Modify

| File | Change |
|---|---|
| `Services/ICalendarTextEntryService.cs` | Add `GetForUsersAndDateRangeAsync` method |
| `Services/CalendarTextEntryService.cs` | Implement new method + fix `AddAsync` CompanyId |
| `Pages/Calendar/Shifts.cshtml.cs` | Switch both call sites to new method |
| `Pages/Calendar/Chores.cshtml.cs` | Inject service, load text entries, add chips |
| `Pages/Calendar/OnCall.cshtml.cs` | Inject service, load text entries, set overlay badges |
| `wwwroot/js/calendar-quick-entry.js` | Add rowId prefix guard (~line 355) |
| `wwwroot/js/calendar-bottom-sheet.js` | Read entryType in extractCellData + route in handleRemoveAssignment |

## Implementation Order

1. **Service layer** (Items 1) — no UI impact, prerequisite for all others
2. **Shifts update** (Item 1 callers) — regression fix, use new method
3. **Chores page** (Item 2) — depends on service layer
4. **OnCall page** (Item 3) — depends on service layer
5. **JS fixes** (Items 4 + 5) — independent of backend, can be done in parallel

## Verification

1. **Cross-company:** Manager from Company A creates text entry for user in Company B (same molecule) → visible to both companies
2. **Chores create:** Type "Team Outing" on Chores page → "Save as text" appears → chip with dotted border
3. **Chores shared:** Text entry created on Shifts appears on Chores, and vice versa
4. **Chores delete:** Click x on text entry chip → deleted from both calendars
5. **OnCall badge:** User with text entries → 📝 badge on OnCall cells → hover shows text
6. **OnCall no-create:** Type unmatched text on OnCall → "Save as text" does NOT appear
7. **Bottom-sheet delete:** On mobile, tap x on text entry chip → calls correct `deleteTextEntry` endpoint
8. **Regression:** Shifts page behavior unchanged
