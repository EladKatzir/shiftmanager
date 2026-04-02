# Session Handoff — Notes System Cleanup (Phase 8 of Notes Unification)

---

## Goal
Complete the remaining cleanup after the CalendarTextEntry/UserDayNote unification. Data is fully migrated and all pages use `ICalendarTextEntryService`. What remains: drop the dead table, delete dead service files.

---

## Current Status (updated 2026-04-02)
- Migration `20260329152015_UnifyNoteSystems` ran — UserDayNotes rows copied to CalendarTextEntries with `EntryType=1`
- All calendar pages (Shifts, Chores, OnCall, Overview) confirmed using `ICalendarTextEntryService` — no page calls `IUserDayNoteService`
- ✅ **GDPR export fixed** — `UserDataExportService` now queries `CalendarTextEntries` (OverviewNote type)
- ✅ **Dead delete removed** — `Users.cshtml.cs` UserDayNotes `ExecuteDeleteAsync` removed
- ✅ **SignalR wired** — `QuickAddTextEntry` and `DeleteTextEntry` now broadcast `TextEntryChanged` events
- `UserDayNotes` table still exists in DB schema (`AppDbContext` still has the DbSet)
- `IUserDayNoteService` / `UserDayNoteService` still registered in DI (`Program.cs:360`)
- **Audit logging for Overview note save/delete is ALREADY DONE** (`Overview.cshtml.cs:514-540`) — do not re-implement

---

## Steps (in order)

### ~~Step 1 — Fix `UserDataExportService.cs:250`~~ ✅ DONE (2026-04-02)
Replaced `_db.UserDayNotes` with `CalendarTextEntries` filtered to `OverviewNote`. Field `n.Note` → `e.Text`.
GDPR scope note: QuickEntry items are intentionally excluded — documented in code comment.

### ~~Step 2 — Remove dead UserDayNotes delete from `Pages/Admin/Users.cshtml.cs`~~ ✅ DONE (2026-04-02)
Dead `ExecuteDeleteAsync` line removed. CalendarTextEntries delete on the following line already covers it.

### Step 3 — Delete dead service files
- Delete `Services/IUserDayNoteService.cs`
- Delete `Services/UserDayNoteService.cs`
- Remove `Program.cs:360`: `builder.Services.AddScoped<IUserDayNoteService, UserDayNoteService>();`

Verify first: `grep -rn "UserDayNote" . --include="*.cs" --include="*.cshtml"` — only migration files should remain after steps 1+2. ✅ Steps 1+2 are done, so only migration files and the service files themselves should show up.

### Step 4 — Remove UserDayNote from AppDbContext
In `Data/AppDbContext.cs`:
- Delete line 40: `public DbSet<UserDayNote> UserDayNotes => Set<UserDayNote>();`
- Delete entity config block starting at ~line 499
- Delete query filter block at ~lines 696-697

### Step 5 — Delete `Models/UserDayNote.cs`
Verify no remaining non-migration references before deleting.

### Step 6 — Create EF migration
Run `dotnet ef migrations add DropUserDayNotesTable`. Review the generated migration — it should drop the `UserDayNotes` table. Ensure `Down()` recreates the table for rollback safety. Apply with `dotnet ef database update`.

### ~~Step 7 — Wire SignalR in `QuickAddTextEntry.cshtml.cs`~~ ✅ DONE (2026-04-02)
Injected `ICalendarNotificationService` + `AppDbContext`. Uses `entry.CompanyId` (already populated by `AddAsync`) → companies query → `NotifyTextEntryChangedAsync("created")`. LogWarning added for null-molecule case.

### ~~Step 8 — Wire SignalR in `DeleteTextEntry.cshtml.cs`~~ ✅ DONE (2026-04-02)
Same pattern. Uses `entry.CompanyId` from already-loaded entry → `NotifyTextEntryChangedAsync("deleted")`. LogWarning added for null-molecule case.

---

## Important Notes

- `DeleteTextEntry.cshtml.cs:86-94` intentionally blocks deletion of OverviewNote entries — do NOT remove this guard.
- `calendar-realtime.js:182` — `TextEntryChanged` listener confirmed present. SignalR wiring is live.
- GDPR scope: QuickEntry items (`EntryType=0`) are excluded from export by design. Comment in `UserDataExportService.cs` explains the rationale.

---

## Key Files (remaining work only)

- `Services/IUserDayNoteService.cs` + `Services/UserDayNoteService.cs` — **DELETE**
- `Data/AppDbContext.cs:40,499,696` — three spots to remove UserDayNote refs
- `Models/UserDayNote.cs` — **DELETE last**
- `Program.cs:360` — DI registration to remove
