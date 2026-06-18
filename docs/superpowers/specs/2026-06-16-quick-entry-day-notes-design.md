# Spec — Quick-Entry Day-Notes (Issue 1)

**Date:** 2026-06-16 · **Branch:** `dev` · **Status:** design approved ("continue working on issue 1")

## Problem

In the calendar quick-entry, typing free text that matches no option should offer to save it as a note.
Today the "save as text" escape-hatch **only exists in user-mode** (rows = workers), because a note row is
`CalendarTextEntry{ UserId, Date }` and must attach to one person. The user works in the **default shift-mode**
(rows = shift types), where `getCurrentMode()` returns `'shift'`, the save-as-text branch
(`calendar-quick-entry.js:658`) is skipped, and typing non-matching text lands on the "No matches" line (690).
Root cause **confirmed by static proof**: the "No matches" branch is logically incompatible with true
user-mode, so the feature was simply never reachable from the view the user uses.

## Decision

Add a **day-level note** lane to quick-entry, available in **every** calendar view. Chosen over keeping notes
user-mode-only (Option A) and over per-cell shift notes (Option C). Day-level fits "jot a note about this day"
and works regardless of row orientation.

### Storage — new isolated entity (NOT reusing `CalendarTextEntry`)

`CalendarTextEntry.UserId` is non-nullable and every existing query keys on it; making it nullable would push
null-handling into all per-user note queries (regression risk). Instead, add:

```
CalendarDayNote : IBelongsToCompany
  int Id
  DateOnly Date
  string Text            (MaxLength 500)
  int CompanyId          (tenant filter via IBelongsToCompany)
  int CreatedByUserId
  DateTime CreatedAt
  DateTime? UpdatedAt
  // nav: Company, CreatedByUser
```

- **One editable note per (Date, CompanyId)** (mirrors `OverviewNote`'s one-per-key rule). Upsert semantics.
- Auto-CompanyId via `CompanyIdInterceptor`; query filter in `AppDbContext` like other `IBelongsToCompany`.
- EF migration `AddCalendarDayNote` (+ index on `(CompanyId, Date)`).

### Service — `ICalendarDayNoteService` (or extend `ICalendarTextEntryService`)

- `Task<CalendarDayNote> SetDayNoteAsync(DateOnly date, int companyId, string text, int createdByUserId)` — upsert.
- `Task<bool> DeleteDayNoteAsync(DateOnly date, int companyId)`.
- `Task<Dictionary<DateOnly,string>> GetDayNotesForCompanyAsync(int companyId, DateOnly start, DateOnly end)`.

### API — `Pages/Api/Calendar/QuickAddDayNote.cshtml(.cs)`

Mirror `QuickAddTextEntry`: `[Authorize]`, `[IgnoreAntiforgeryToken]`, POST JSON `{ date, text }`.
- Validate: text non-empty, ≤500, date parses, within 2y future / not past (match existing rules — confirm
  past-date rule desirable for notes; relax if user wants notes on past days).
- Authz: reuse `HasCalendarNotePermissionAsync(currentUserId)` (no target-user scope check needed — day-scoped,
  company-bound). Empty text ⇒ delete (clear note).
- Audit log + SignalR broadcast (reuse `NotifyTextEntryChangedAsync` group pattern or add a day-note event).

### Frontend — `wwwroot/js/calendar-quick-entry.js`

- New **always-present** trailing dropdown option **"📝 {QuickEntry_AddDayNote}: {date}"**, shown whenever
  `query.trim().length > 0` (in any mode/calendar), *in addition to* matches — NOT gated on `totalItems === 0`.
  Keep it visually separated (reuse `.quick-entry-item--text-entry` styling + a divider).
- New `item.type === 'day-note'` handled in `selectItem()` → calls a `window.quickAddDayNote(date, text)` wrapper
  (add to `calendar-inline-edit.js` mirroring `quickAddTextEntry`). On success: toast + `triggerCalendarRefresh()`.
- Leave the existing user-mode `text-entry` path intact (still valid in worker-view); day-note is the new
  unified lane. (If the user later wants ONLY day-notes, we can retire `text-entry`.)

### Rendering — day-column header of the Excel calendar

- Surface the day-note in the **date column header** (thead) so it shows in all calendars (Shifts/Chores/OnCall).
  Add a note glyph + tooltip/peek; clicking re-opens to edit. Exact hook: `ExcelCalendarTableViewComponent` +
  the header partial. Bulk-load via `GetDayNotesForCompanyAsync` in each calendar page model and pass into the
  view model.
- CSS in `calendar.css` — light/dark/RTL safe (pair any colored background with explicit contrast color).

### Localization

Add to BOTH `SharedResources.resx` + `SharedResources.he-IL.resx` (grep first to avoid dup-key test breaks):
`QuickEntry_AddDayNote`, `QuickEntry_DayNoteSaved`, `DayNote_EditTitle`, `DayNote_Delete` (+ any toast keys).

## Testing

- **TDD backend:** service upsert/delete/get tests using real SQLite (`UseSqlite(":memory:")`, not InMemory) +
  tenant-isolation test (company A cannot read company B's day-note). API endpoint validation + authz tests.
- **Frontend:** browser verification (app running, flag default) — type in shift-view, confirm the day-note
  option appears, saves, renders in the header, edits, deletes. Check light/dark/RTL.
- Full suite sequential (`xUnit.MaxParallelThreads=1`).

## Out of scope / deferred

- Multiple notes per day (chose one editable). Revisit if requested.
- Retiring the legacy user-mode `text-entry` path (kept for now).
- Past-date note rule (inherit existing restriction unless user wants past-day notes).

## Open confirmations (non-blocking; sensible defaults chosen)

- One-editable-per-day (assumed) vs multiple.
- Whether notes may be added on past dates (assumed: follow existing future-only rule).
