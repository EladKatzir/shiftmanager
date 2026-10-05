# Day notes: per-calendar scope, many per day — design

**Date:** 2026-10-05
**Branch target:** `dev`
**Status:** approved by the user; ready for implementation planning
**Supersedes the day-note portion of:** `docs/superpowers/specs/` (none — the 2026-09-17 molecule-keying work has no spec file; see the memory note `four_task_update_2026_09_17`)

---

## 1. Problem

A `CalendarDayNote` is a free-text note attached to a whole DAY on the `/Calendar/Shifts`
calendar, created from Quick Entry. Today it is keyed to `(MoleculeId, Date)` with **one note per
molecule per day**, enforced by a filtered unique index and by upsert semantics.

The user's report: *"we want to make daynotes at the /shifts viewable to everyone who can see the
calendar, regardless. so even if owner enter oren alhut calendar he will see all day notes."*

### What investigation actually found

Reading the code showed that **the read path already has no permission filter**: the Shifts page
calls the service unconditionally (`Pages/Calendar/Shifts.cshtml.cs:350`), the service deliberately
calls `IgnoreQueryFilters()` (`Services/CalendarDayNoteService.cs:69`), and the render has no grant
check (`Pages/Shared/Components/ExcelCalendarTable/Default.cshtml:81`). So "viewable to everyone who
can see the calendar" was already true *within one molecule*.

Two things were actually wrong, both confirmed by live testing against the dev database (app running
on `127.0.0.1:5123`, `Environment=Development`, logged in as `owner2@test`, home desk SystemAdmins in
molecule **System**):

1. **Scope mismatch.** The only note in the database (`id=1, Date=2026-09-18, MoleculeId=9 (System),
   CompanyId=15 (SystemAdmins)`) does not appear on the Oren/Alhut calendar, because that is a
   different molecule. Verified: `?MoleculeId=1&JobTypeId=1&Start=2026-09-13` rendered 8 rows and 7
   day headers including `2026-09-18`, with `noteChipCount: 0`.
2. **That note is unreachable from *every* calendar, including its own.** Molecule System has no
   shift rows, and `Pages/Calendar/Shifts.cshtml:337` gates the entire `ExcelCalendarTable` on
   `CalendarData.Rows.Any()`. Day notes are rendered inside that table's `<th>`, so a row-less
   calendar silently swallows its notes. Verified: `?MoleculeId=9&Start=2026-09-13` produced
   `dayHeaders: []`, no `.excel-calendar` element, and the empty state "לא נמצאו משמרות".

Finding 2 is **explicitly out of scope** (see §9), by the user's decision: a calendar with no shift
rows has nothing to annotate.

### What was NOT wrong

An earlier draft of this design claimed that a cross-molecule viewer would be rejected by the
SignalR hub's group-join check, and proposed fixing it. **That claim was false and is withdrawn.**
`Hubs/CalendarHub.cs:150-157` is written `if (MoleculeBelongsToCompanyAsync(...)) return true;
return await DirectorHasMoleculeAccessAsync(...);` — and the second rung resolves the
`DirectorHubAccess` grant, which every Owner holds at `ProjectId: 1`. Live proof:

```
05:01:01 [DBG] CalendarHub  Connection ak2K87DmS8aIG_arE8nzHw connected (User: 14)
05:01:01 [DBG] CalendarHub  Connection ak2K87DmS8aIG_arE8nzHw joined group shifts-1-1
```

No `denied access to group` line was emitted. The grant scopes mirror the view scopes at every tier
(Owner project/project, AreaAdmin area/area, Director and MoleculeAdmin molecule/molecule,
BRDirector company/company; Lead/Assigner/Employee/Trainee have no hub grant and only
company-scoped `ViewShifts`), so **"can see this calendar" and "can join its live group" are already
the same answer for every template-derived grant.** The hub requires no change.

---

## 2. Requirements (final, user-decided)

| # | Requirement |
|---|---|
| **R1** | A day note belongs to a CALENDAR, identified by the triple `(MoleculeId, JobTypeId?, TabId?)` exactly as `Pages/Calendar/Shifts.cshtml.cs` resolves it for the request. |
| **R2** | Visibility has no permission filter beyond being able to open that calendar. Anyone who reaches the page — Owner, AreaAdmin, a user from another molecule holding `ViewShifts` — sees every note the calendar shows. This is already true and must stay true. |
| **R3** | No limit of one note per day. A day may hold many notes. |
| **R4** | Hovering a note says who wrote it. |
| **R5** | Tab scoping: viewing a REAL tab shows only that tab's notes. Viewing **All** (`TabId == null`) shows EVERY note of that `(molecule, jobType)` calendar, each badged with its tab name; notes typed on All render unbadged. A note typed on All does NOT appear on a real tab. |
| **R6** | Header overflow: render up to 2 note chips inline, then a `+N` affordance opening a panel that lists every note in full with its author. |
| **R7** | Quick Entry always ADDS a new note (never upserts). Each note's `×` deletes that one note by id. No in-place editing. |
| **R8** | Deleting a note requires `note.CreatedByUserId == callerId` **OR** `AssignShifts` scoped to `(note.MoleculeId, note.JobTypeId)`. Enforced on the server; the `×` renders under the same rule. |

### Requirements notes

- **R1 narrows scope relative to today.** Currently a molecule note appears on every job-type
  calendar of that molecule (the read ignores job type entirely). After this change it appears on
  one `(jobType, tab)` calendar. This is deliberate and is what "per calendar" means.
- **R5's asymmetry is intentional.** A tab is "purely a VIEW/relevance partition"
  (`Models/ShiftTab.cs:11-12`) and `TabId == null` is the synthetic **All** pseudo-tab, which has no
  DB row (`Models/ShiftTab.cs:7-8`). All already shows every row, so it shows every note.
- **R8 revises an earlier draft** which said "delete rights unchanged". That was wrong on two
  counts: the `×` is gated on `IsReadOnly = !CanEdit` (`Shifts.cshtml.cs:619,771`) which is the
  `AssignShifts` grant, while the *server* gate is the broad `HasCalendarNotePermissionAsync`
  (`WriteOverviewNotes` OR any calendar-edit grant). The mismatch means a `WriteOverviewNotes`
  holder can create a note whose `×` never renders for them, yet can delete anyone's note by direct
  POST. That is open audit finding `docs/superpowers/audit/cluster-1-calendars.md:644-647`.

### Audit finding closed by this design

`cluster-1-calendars.md:644-647` — *"/Api/Calendar/QuickAddDayNote lets any employee silently
overwrite or delete the desk-wide day note"*. Its proposed fix has three clauses, all satisfied:

- *"require an explicit delete flag rather than treating empty text as delete"* → R7 + a separate
  `DeleteDayNote` endpoint; the empty-text-deletes branch is removed.
- *"record/display the last author"* → R4, already implemented and extended per-note.
- *"gate behind an assign-tier grant"* → R8 (author OR assign-tier).

Mark that checkbox when this ships.

---

## 3. Data model

### 3.1 Entity (`Models/CalendarDayNote.cs`)

| Field | Change | Notes |
|---|---|---|
| `Id` | — | |
| `Date` | — | `DateOnly` |
| `Text` | — | `MaxLength(500)` |
| `CompanyId` | — | Writer's desk, provenance only. Not the visibility boundary. |
| `MoleculeId int?` | — | NULL only for legacy un-keyable rows, preserved and invisible. |
| **`JobTypeId int?`** | **ADD** | NULL is legitimate: Tech molecules run with no job type (`Shifts.cshtml.cs:260-264`). |
| **`TabId int?`** | **ADD** | NULL = the synthetic **All** view. |
| `CreatedByUserId` | **int → int?** | See A1 in §8. Becomes nullable with `SetNull`. |
| `CreatedAt` | — | |
| `UpdatedAt` | **KEEP** | Vestigial under R7 but must not be dropped — see §3.4. |
| `UpdatedByUserId` | **KEEP** | Same. |
| `CreatedByUser` nav | `AppUser` → `AppUser?` | Consequence of the nullable FK. |
| **`JobType` nav** | **ADD** | Declared explicitly as `JobType`/`JobTypeId`. |
| **`Tab` nav** | **ADD** | Declared explicitly as `Tab`/`TabId`. |

> **Navigation naming is load-bearing.** This project has already shipped a shadow-FK bug where a
> nav named `Trainee` pointed at `TraineeId` while the real column was `TraineeUserId`. Declare each
> nav so its name matches its FK, and configure both explicitly in `AppDbContext`.

### 3.2 Indexes (`Data/AppDbContext.cs:746-764`)

- **DROP** `HasIndex(MoleculeId, Date).IsUnique().HasFilter("MoleculeId IS NOT NULL")` — required by R3.
- **ADD** non-unique `HasIndex(MoleculeId, JobTypeId, Date, TabId)`.
- **KEEP** `HasIndex(CompanyId)` — backs the inherited query filter at `:767-768`.

**The column order is measured, not guessed.** `EXPLAIN QUERY PLAN` on a real-schema copy:

| Index order | All-view branch (`MoleculeId=? AND JobTypeId=? AND Date BETWEEN ?`) |
|---|---|
| `(Molecule, JobType, TabId, Date)` | `SEARCH` on two equality columns only — **Date range unusable**, scans every tab/date entry in the group |
| `(Molecule, JobType, Date, TabId)` | `SEARCH` using `MoleculeId=? AND JobTypeId=? AND Date>? AND Date<?` |

All is the **default** view, so it is the hot path. `TabId` is both the least selective column and
the one the common branch does not filter on, so it goes last; the tab branch loses nothing because
`TabId` stays index-resident.

### 3.3 Foreign keys

| FK | OnDelete | Why |
|---|---|---|
| `Company` | `Restrict` (unchanged) | |
| `Molecule` | `Cascade` (unchanged) | In practice unreachable while `ShiftTab.Molecule` stays `Restrict` (`AppDbContext.cs:1482-1486`) — tabs block the molecule delete first. |
| `CreatedByUser` | **`Restrict` → `SetNull`** | See A1 in §8. |
| `UpdatedByUser` | `SetNull` (unchanged) | |
| **`Tab` (new)** | **`SetNull`** | Tab deletion is a HARD delete (`Services/ShiftTabService.cs:92-103`, reached from `Pages/Admin/Organization/Tabs/Index.cshtml.cs:233-243`). `Cascade` would destroy user text. `SetNull` drops the note into the All bucket — safe now that uniqueness is gone, and it strictly NARROWS visibility under R5 (before: All + tab T; after: All only). Matches the precedent at `AppDbContext.cs:1556-1559` (`UserShiftTabPreference.TabId`). |
| **`JobType` (new)** | **`Restrict`** | `Cascade` would silently delete user text. **`SetNull` is actively wrong**: `JobTypeId == null` already MEANS "Tech calendar", so a nulled note would not disappear — it would RELOCATE onto a calendar it was never written for. Consistent with every other JobType FK in the model (`:717`, `:1490`, `:2003`). Requires the companion pre-check in A2. |

### 3.4 Why `UpdatedAt` / `UpdatedByUserId` stay

R7 removes in-place editing, so both go vestigial. They must **not** be dropped anyway:

`Migrations/CalendarDayNoteMoleculeBackfillSql.cs:29` uses `COALESCE(n.UpdatedAt, n.CreatedAt)`, and
`CalendarDayNoteMoleculeBackfillTests` builds its schema from the **current model** via
`EnsureCreatedAsync` (`Helpers/SqliteDbContextFixture.cs:68`). Dropping the column makes that test
fail with `no such column: n.UpdatedAt` plus a compile error on its seed line — and its entire
purpose is pinning FROZEN historical migration behaviour, which it must not be rewritten to track.

Instead: keep both columns, and remove only the *surface*:
- Delete `LastEditorName` from `DayNoteView`.
- Delete the `Calendar_DayNote_LastEditedBy` render branch at `Default.cshtml:88-89`.
- **Do NOT delete the resx key.** `GetOverrideValueAsync(companyId, culture, key)` resolves by key,
  so removing it would orphan `CompanyLocalization` rows. Leave the key in both files.

### 3.5 The one ambiguous triple

`(M, NULL, NULL)` means "M's calendar as rendered when it has no job type". That is true both for a
Tech molecule and for a non-Tech molecule whose area has zero resolvable job types
(`Shifts.cshtml.cs:271-273` sets `JobTypeId = null` when `AvailableJobTypes` is empty). The encoding
is self-consistent and both read back correctly, so this is not a defect — but its MEANING is
anchored to mutable data outside the row:

- Flipping a molecule's `Type` between Workforce and Tech changes which notes are reachable.
- Adding the first job type to a previously job-type-less non-Tech molecule's area makes its
  existing `(M, NULL, NULL)` notes invisible; deleting the last one makes them reappear.

**Decision:** keep the encoding (it is the only one available), comment it on the entity so nobody
"fixes" it, and have the write endpoint **400** when a NON-Tech calendar resolves `JobTypeId = null`.
Refusing to write into a degenerate calendar state removes the ambiguous triple from the system
rather than relying on it reading back correctly. No molecule in the dev data hits this.

---

## 4. Migrations

**Two migrations, schema then data.** Reversed, migration 2 fails with `no such column: JobTypeId`.

> **Correcting a widely-repeated reason.** The previous day-note change used two migrations
> "because SQL behind a pending rebuild runs pre-rebuild". That is **false**, measured with
> `dotnet ef migrations script`: EF Core's SQLite generator **hoists** every rebuild-triggering
> operation (`AddForeignKey`, `AlterColumn`, `DropColumn`) to the END of the migration regardless of
> where it is written (proved on `20260716065316_DraftAllCalendarsFoundation`, whose `AlterColumn`
> at body line 30 lands after all seven `CreateIndex` statements). `ALTER TABLE ADD COLUMN` and
> `DROP INDEX` are native and run first, so `migrationBuilder.Sql` DOES see newly added columns.
>
> The real reason to split: in one migration, correctness rests on an **invisible intra-body
> ordering rule** — the `Sql()` call must sit after the `DropIndex()` call. Written before it, you
> get `SqliteException: UNIQUE constraint failed: CalendarDayNotes.MoleculeId, CalendarDayNotes.Date`
> (reproduced), because the old unique index is still live and fan-out copies share
> `(MoleculeId, Date)` by construction. Two migrations make the ordering a property of the migration
> SEQUENCE, which EF enforces, instead of statement order inside one method, which nothing enforces
> and no test would catch.
>
> Also for the record: the EF provider here reports **9.0.9**, not 8.x, and
> `dotnet ef migrations script --idempotent` is **unsupported on SQLite** — so re-runnability must
> live in the SQL itself.

### Migration 1 — schema

Body order (only the two `AddForeignKey` calls trigger the rebuild; EF hoists them):

1. `DropIndex("IX_CalendarDayNotes_MoleculeId_Date")`
2. `AddColumn JobTypeId` (nullable INTEGER)
3. `AddColumn TabId` (nullable INTEGER)
4. `AlterColumn CreatedByUserId` → nullable (A1 — folds into the same `ef_temp` rebuild, so this is
   free now and costs a standalone rebuild later)
5. `CreateIndex` on `(MoleculeId, JobTypeId, Date, TabId)`, non-unique
6. `AddForeignKey` → `JobTypes` (`Restrict`), `AddForeignKey` → `ShiftTabs` (`SetNull`)

### Migration 2 — data fan-out

A molecule note is visible TODAY on every job-type calendar of its molecule (verified: the read at
`CalendarDayNoteService.cs:70` ignores job type entirely). Fan-out is the only shape that preserves
that, because `JobTypeId == null` is unavailable as an "all job types" sentinel — Tech already owns
it. The SQL lives in a shared constants class beside
`Migrations/CalendarDayNoteMoleculeBackfillSql.cs` so a regression test can run it.

**INSERT first, then UPDATE.** Reversed, the UPDATE consumes the `JobTypeId IS NULL` marker, the
INSERT matches nothing, no copies are made, and the migration reports success — a silent no-op.

```sql
-- 1. one copy per job type EXCEPT the anchor (the one the calendar defaults to)
INSERT INTO CalendarDayNotes
      (Date, Text, CompanyId, MoleculeId, JobTypeId, TabId, CreatedByUserId, CreatedAt, UpdatedAt, UpdatedByUserId)
SELECT n.Date, n.Text, n.CompanyId, n.MoleculeId, jt.Id, NULL,
       n.CreatedByUserId, n.CreatedAt, n.UpdatedAt, n.UpdatedByUserId
FROM CalendarDayNotes n
JOIN Molecules m ON m.Id = n.MoleculeId AND m.Type <> 1          -- NOT Tech
JOIN JobTypes jt ON jt.IsActive = 1
                AND jt.AreaId = m.AreaId
                AND (jt.MoleculeId IS NULL OR jt.MoleculeId = n.MoleculeId)
                AND (m.Type IN (0,2) OR jt.IsWorkforceOnly = 0)
WHERE n.MoleculeId IS NOT NULL AND n.JobTypeId IS NULL
  AND jt.Id <> (SELECT jt2.Id FROM JobTypes jt2
                WHERE jt2.IsActive = 1 AND jt2.AreaId = m.AreaId
                  AND (jt2.MoleculeId IS NULL OR jt2.MoleculeId = n.MoleculeId)
                  AND (m.Type IN (0,2) OR jt2.IsWorkforceOnly = 0)
                ORDER BY jt2.SortOrder, jt2.Name LIMIT 1);

-- 2. the original becomes the anchor copy
UPDATE CalendarDayNotes SET JobTypeId = (
  SELECT jt2.Id FROM JobTypes jt2 JOIN Molecules m ON m.Id = CalendarDayNotes.MoleculeId
  WHERE jt2.IsActive = 1 AND jt2.AreaId = m.AreaId AND m.Type <> 1
    AND (jt2.MoleculeId IS NULL OR jt2.MoleculeId = CalendarDayNotes.MoleculeId)
    AND (m.Type IN (0,2) OR jt2.IsWorkforceOnly = 0)
  ORDER BY jt2.SortOrder, jt2.Name LIMIT 1)
WHERE MoleculeId IS NOT NULL AND JobTypeId IS NULL
  AND EXISTS (SELECT 1 FROM Molecules m WHERE m.Id = CalendarDayNotes.MoleculeId AND m.Type <> 1);
```

Verified against a copy of the real `app.db`, byte-identical after runs 1, 2 and 3
(`PRAGMA foreign_key_check` clean):

```
mol 1 (Workforce), NULL -> jt 1,2,3,4
mol 9 (System),    NULL -> jt 2,4
mol 6 (Tech),      NULL -> NULL, untouched
molecule with zero job types -> NULL, MoleculeId preserved (invisible, not destroyed)
```

**Four dependencies, each a real trap:**

1. **`m.Type <> 1` (exclude Tech) is a correctness requirement, not a nicety.** Without it the
   fan-out **destroys** every Tech-molecule note: `JobTypeId IS NULL` is both the "not yet keyed"
   marker and the legitimate permanent Tech value, so a Tech note is fanned out to job types its
   calendar never resolves, becoming permanently unreachable with no error and no audit row.
   Reproduced on molecule 6 (**Shikma**, Tech) against real data:
   `(3,'tech',mol=6,jt=NULL)` → `(3,mol=6,jt=2) (5,mol=6,jt=4) (6,mol=6,jt=5) (7,mol=6,jt=6)`.
   **The dev DB cannot catch this** — it holds only the molecule-9 note. Shikma is the exposure.
   The same marker ambiguity also breaks idempotency.
2. **INSERT must precede UPDATE** (above).
3. **`CompanyId` / `CreatedByUserId` / `CreatedAt` are carried forward explicitly.** Raw SQL bypasses
   `CompanyIdInterceptor`, and `CompanyId` is NOT NULL with no default — omit it and you get
   `NOT NULL constraint failed`. Carrying both forward is also what satisfies the `Company`
   `Restrict` FK (`:756`) and the `CreatedByUser` FK (`:760`): copies reference existing rows.
4. **The anchor is `ORDER BY SortOrder, Name LIMIT 1`** and MUST match
   `Services/JobTypeService.cs:58-59` plus `Shifts.cshtml.cs:273`
   (`AvailableJobTypes.FirstOrDefault()`), or the copy the calendar opens on is not the one the user
   had been seeing.

**Enum literals are frozen.** `0/1/2` are `MoleculeType.Workforce/Tech/Helper`. Migration SQL cannot
be re-derived if the enum is reordered, so add an assertion that
`(int)Workforce == 0 && (int)Tech == 1 && (int)Helper == 2` alongside the existing backfill tests.

**Rollback.** `Migrations/20260917094723_...cs:94-99` already carries a caveat about restoring a
unique index over duplicates. After fan-out that caveat becomes a guarantee: `Down()` **cannot**
recreate `IX_CalendarDayNotes_MoleculeId_Date`. Either delete the non-anchor copies in `Down()`
first, or declare the migration one-way in the file. **Decision: declare one-way**, with a comment
explaining why, matching the existing precedent.

---

## 5. Service layer

`Services/ICalendarDayNoteService.cs` / `CalendarDayNoteService.cs`:

```csharp
/// The identity of one Shifts calendar, exactly as Shifts.cshtml.cs resolves it per request.
public sealed record CalendarScope(int MoleculeId, int? JobTypeId, int? TabId);

public sealed record DayNoteView(
    int Id,
    string Text,
    string AuthorName,        // never null; the service substitutes Calendar_DayNote_UnknownAuthor
    int? CreatedByUserId,      // for R8's ownership check in the view
    int? TabId,
    string? TabNameEn,         // null when the note is All-scoped
    string? TabNameHe,
    bool TabIsActive);         // false => badge marked inactive (F11 decision)

Task<CalendarDayNote> AddDayNoteAsync(DateOnly date, CalendarScope scope, int authorCompanyId, string text, int userId);
Task<CalendarDayNote?> GetByIdAsync(int noteId);
Task<bool> DeleteDayNoteAsync(int noteId);
Task<Dictionary<DateOnly, List<DayNoteView>>> GetDayNotesForCalendarAsync(CalendarScope scope, DateOnly start, DateOnly end);
```

### 5.1 The read query — and the trap that silently fails R5

```csharp
// SECURITY-AUDITED: IgnoreQueryFilters is required, not incidental — a note is keyed to a calendar
// and must be visible to viewers from every desk, while the inherited company filter would restrict
// it to the writer's desk. Callers authorise the molecule via ShiftCalendarAccess first.
var q = _db.CalendarDayNotes.IgnoreQueryFilters()
    .Where(n => n.MoleculeId == scope.MoleculeId
             && n.JobTypeId == scope.JobTypeId
             && n.Date >= start && n.Date <= end);

if (scope.TabId.HasValue)                     // real tab: narrow to it
    q = q.Where(n => n.TabId == scope.TabId);
// All view: NO TabId predicate AT ALL — this is the union R5 requires.
```

> **The branch must be a C# `if`, not a composed expression.** Writing
> `n.TabId == scope.TabId` for both branches translates to `TabId IS NULL` on the All branch, which
> shows only the unbadged notes and **silently drops every tab-scoped note** — failing R5 while
> compiling, translating and looking correct. Also avoid `(n.TabId ?? 0) == (scope.TabId ?? 0)`: it
> translates, but it defeats the index in §3.2.

Nullable equality is safe. EF Core's parameter-aware null-semantics rewriting inspects the parameter
value at translation time and emits `"JobTypeId" IS NULL` when null and `"JobTypeId" = @p` when not,
with a separate query-cache entry per null-ness — so a null-vs-null comparison does not degenerate to
SQL `NULL = NULL`. Direct real-SQLite precedent in this repo: `Services/ShiftTabService.cs:22,246-248`,
pinned by `ShiftTabServiceTests.cs:252,255` under `SqliteDbContextFixture`.

### 5.2 Other service rules

- `AddDayNoteAsync` **always inserts** (R7). Set `CompanyId` explicitly so `CompanyIdInterceptor`
  (which only fires at `CompanyId == 0`) leaves it alone.
- The result is grouped `ToDictionary(g => g.Key, g => g.ToList())` — **never** `ToDictionary(n => n.Date, …)`.
- Ordering within a day: `CreatedAt` ascending, then `Id`, so the 2 inline chips are stable and the
  "+N" overflow is deterministic.
- `TabNameEn`/`TabNameHe` must BOTH be carried; the service has no localizer and the view picks by
  culture the way `Shifts.cshtml:320` does. Returning one resolved string would give the Hebrew UI
  English badges.
- `AuthorName` null-substitution happens **in the service**, so no view has to branch.

### 5.3 The stale-uniqueness comments must die with the index

`Models/CalendarDayNote.cs:12-13` and `CalendarDayNoteService.cs:67` both assert "one note per day"
as live justification. Once the unique index is gone those comments are false, and the `:67` one
specifically justifies the `ToDictionary` that would then throw. Rewrite both.

> **Ship-together constraint.** The service change MUST land in the same commit as the migration.
> `Program.cs:615` runs `MigrateAsync()` at startup, so booting any binary against the migrated DB
> runs the new schema against old code: molecule 9 would have two rows for 2026-09-18, and
> `ToDictionary` throws `ArgumentException: An item with the same key has already been added`,
> **500ing `/Calendar/Shifts` for every viewer of that molecule in both modes.**

---

## 6. Endpoints

### 6.1 `Pages/Api/Calendar/QuickAddDayNote.cshtml.cs` (modified)

Request gains `jobTypeId` and `tabId`. The `text.Length == 0` → delete branch (`:136-148`) is
**removed outright** — which also fixes a latent audit bug: that branch logs `entityId: 0`, so today
every day-note deletion is unattributable.

Validation order:

1. parse/bind; 400 on bad date or `> today + 2 years` (unchanged)
2. `HasCalendarNotePermissionAsync(userId)` → 403 (unchanged)
3. `moleculeId` present and `> 0` → 400 (unchanged)
4. active desk via `_tenantResolver.GetCurrentTenantId()` → 400 if `<= 0` (unchanged)
5. `moleculeId ∈ ShiftCalendarAccess.GetViewableMoleculeIdsAsync(...)` → 403 (unchanged)
6. **NEW** `jobTypeId`, when present, must be in `GetJobTypesForMoleculeAsync(moleculeId)` → 403.
   That predicate pins `jt.AreaId == molecule.AreaId` and `(jt.MoleculeId == null || == moleculeId)`
   (`JobTypeService.cs:54-57`), so a job type from another area is unreachable.
7. **NEW** `jobTypeId` absent while the molecule is **not** Tech → 400 (§3.5).
8. **NEW** `tabId`, when present, must be a `ShiftTab` whose `(MoleculeId, JobTypeId)` equals the
   request pair → 403. Closes the cross-molecule tab vector.
9. insert; audit `DayNoteSaved` with the real note id.

### 6.2 `Pages/Api/Calendar/DeleteDayNote.cshtml` + `.cshtml.cs` (NEW)

A **separate sibling page**, following the established convention — `QuickAddChore`/`DeleteChore`,
`QuickAddOnDuty`/`DeleteOnDuty`, `QuickAddTextEntry`/`DeleteTextEntry` are all separate pairs, never
a second handler on the add page. Modelled on `DeleteTextEntry.cshtml.cs:69-104`. Covered
automatically by `ApiAuthenticationMiddleware.cs:306`'s `/Api/Calendar` prefix; needs no
`Program.cs` `AllowAnonymousToPage` entry (it is `[Authorize]`). **Verify both anyway** — this
project has been bitten by `/Api/` endpoints needing registration in both places.

Gate ladder, **in this order**:

1. 401 if no `NameIdentifier` claim
2. `HasCalendarNotePermissionAsync(userId)` → 403
3. load note by id → **404** if missing
4. **`note.MoleculeId == null` → 404** ("this note is not on any calendar")
5. `!viewable.Contains(note.MoleculeId ?? 0)` → 403
6. **R8**: `note.CreatedByUserId == currentUserId || HasGrantWithScopeAsync(currentUserId, "AssignShifts", moleculeId: note.MoleculeId, jobTypeId: note.JobTypeId)` → 403
7. delete; audit `DayNoteDeleted` with the real note id
8. SignalR broadcast (A4)

> **Step 4 is a security requirement, not tidiness.** `note.MoleculeId` and `note.JobTypeId` are both
> nullable. For a legacy un-keyed row, R8's call reaches `HasGrantWithScopeAsync` with **all-null
> scope params**, and `GrantService.cs:196-205` (project), `:240-243` (area) and `:255-258`
> (molecule) each return `true` for any holder in their own hierarchy. R8 would degenerate into
> "any assigner anywhere may delete it" — verbatim the pitfall already recorded in this project's
> notes ("project-scoped grants with all-null caller params must validate the user's hierarchy
> context, not return true blindly").
>
> Step 5's spelling matters too: `if (note.MoleculeId.HasValue && !viewable.Contains(note.MoleculeId.Value))`
> leaves the hole open; `if (!viewable.Contains(note.MoleculeId ?? 0))` is safe because 0 is never a
> molecule id. One character, an IDOR on either side.

**One widening we are knowingly accepting.** For a Tech-molecule note `JobTypeId` is legitimately
null, so R8's call is `(moleculeId: 6, jobTypeId: null)`. `GrantService` computes
`jobTypeMismatch = grant.JobTypeId.HasValue && jobTypeId.HasValue && grant.JobTypeId != jobTypeId`,
so when the request omits `jobTypeId` a job-type-RESTRICTED grant still matches. The
`SECURITY-AUDITED` comment at `GrantService.cs:213-223` documents this as deliberate legacy
behaviour, with enforcement delegated to callers passing `jobTypeId` explicitly. Consequence: a user
whose `AssignShifts` is scoped to one job type can delete Tech-molecule notes. Defensible (Tech has
no job types) but it must be stated in the endpoint comment, not discovered.

### 6.3 Double-submit guard

`calendar-inline-edit.js:717-758` has no in-flight flag, no button disable and no request token, and
`closeInput()` fires only inside the promise's `.then()` (`calendar-quick-entry.js:1363-1367`) — so
the Quick Entry input stays open and focused for the whole round-trip and two Enters send two POSTs.
Harmless under today's upsert; under R7 it creates two identical notes and can push a day to "+1",
which reads as a bug.

- Client: an in-flight boolean in `quickAddDayNote` and in the delete handler.
- Server: `AddDayNoteAsync` rejects same scope + same date + same text + same author within a short
  window.
- `DeleteDayNoteAsync(noteId)` returns 404 for an already-deleted id, and **the JS treats 404 as
  success** so a double-click does not raise an error toast.

> **Do NOT solve this with a unique index.** A filtered unique index over
> `(MoleculeId, JobTypeId, TabId, Date, Text)` hits the SQLite NULL-distinctness trap this project
> has already worked around three times (`AppDbContext.cs:1502-1512`, `:1547-1555`, `:1571-1588`),
> and because both `JobTypeId` and `TabId` are nullable it would need four filtered variants. Not
> worth it for a double-click.

---

## 7. UI

### 7.1 Header render (`Default.cshtml:81-106`)

Width-derived inline cap, computed server-side from `columnPlan` — CSS cannot read a `<col>` width,
and `@container` does not apply to internal table elements (and `contain` on the calendar chain is
forbidden by `calendar.css:2944-2947`). All day columns share one width key, so one value serves
every column. `CalendarColumnPlanner` already exposes the constants (`DefaultDayWidth = 100`,
`DefaultCompactDayWidth = 60`, `MinWidth = 40`, `MaxWidth = 600`).

| Resolved day width | Inline chips | Trigger |
|---|---|---|
| `< 90px` (month default 60, or hand-narrowed) | 0 | `📝` + total count |
| `< 140px` (week default 100) | 1 | `+N` |
| `>= 140px` | 2 (R6's figure) | `+N` |

Month view and a hand-narrowed column collapse through the same rule, so there is no separate
`isCompact` branch.

**Two deliberate deviations from R6/R5 as literally worded, both approved:**

1. **The trigger renders whenever a day has ≥ 1 note**, not only when `N > 2`. Under literal R6 a
   day with one or two notes has no focusable element at all, so a keyboard or screen-reader user
   could never reach the text (`title` is mouse-only, and the chip is not focusable).
2. **The inline chip shows a tab colour DOT, not the tab name** (name in the `title`); the full
   named badge appears in the panel. At a 100px column a chip reading `📝 Geo …` leaves ~2 glyphs
   for the note itself. The dot mirrors `.cal-tab__dot` (`Shifts.cshtml:526-534`) so a dot and its
   strip pill read as the same object, and R5's "badge naming the tab" is satisfied where the brief
   wanted the names anyway.

Inactive tabs (F11 decision): the badge renders with an "inactive" marker. `ResolveActiveTab`
(`Shifts.cshtml.cs:401-411`) falls back to All for any tab not in `AvailableTabs`, and
`ShiftTabService.cs:22-25` excludes `IsActive == false`, so a deactivated tab's notes are reachable
only from All — the marker explains why they cannot be navigated to.

### 7.2 The "+N" panel

**Reuse `.modal` / `.modal-backdrop` / `.is-open`** from `wwwroot/css/components.css:729-830` — the
primitive `wwwroot/js/feedback-modal.js:126-132` already uses. Classes: `.modal .modal--sm`,
`.modal__header/__title/__close/__body/__footer`.

**Rendered once per grid by the view component, then moved to `document.body` on init.** No new
z-index token: `.modal` already carries `--z-modal` (1050) and `.modal-backdrop` `--z-modal-backdrop`
(1040).

Why not anchored inside the `<th>`, in severity order:

1. **Stacking-context cap.** `.excel-calendar__header` is `position: sticky` **with**
   `z-index: --z-sticky-header` (1022), which makes `<thead>` a stacking context — so anything
   inside it composites at 1022 against page-level siblings, *below* `--z-fixed` (1030),
   `--z-modal` (1050) and `--z-dropdown` (1060). It would paint behind the distribution-list
   dropdown and behind any modal, and it cannot be raised from inside.
2. **Overflow clip.** `position: absolute` in the `<th>` is clipped by
   `.excel-calendar { overflow: auto }` (`calendar.css:2757-2766`).
3. **Anchor drift.** `position: fixed` escapes the clip (the chain is audited free of
   transform/filter/contain) but then does not follow the column when the grid scrolls.

Also do **not** render it as a flow sibling after `.excel-calendar` inside `.cal-page`:
`calendar.css:2181-2182` states the invariant *"do NOT add fixed-height elements below
`.excel-calendar`; the flex math assumes the calendar is the last child."*

**Localization without JS string-building:** the panel shell's chrome is Razor-localized and moved to
`<body>`; the per-day content lives in an inert `<template>` inside the `<th>` that Razor fully
renders and the JS clones. `<template>` content is not rendered, not focusable and not in the
accessibility tree, so it costs the header nothing.

Rejected alternatives, with reasons: `window.FeedbackModal.show()` — its API is
`show(type, message)`, one escaped string plus an OK button, cannot host a list with per-row delete
buttons. `calendar-bottom-sheet.js` — gated on `isTouchDevice()` (`:76-80`) and
`.bottom-sheet__content` uses physical `left/right` (`calendar.css:3914-3916`). `.cal-filter-modal`
(`calendar.css:2455-2475`) — hard-codes `z-index: 9999`, **above `--z-toast` (1080)**, so the success
toast a delete fires would render behind the panel. `.modal--fullscreen-wrapper` — exists for pages
that render the dialog inline, which is exactly what must be avoided.

### 7.3 Surviving the grid refresh

`_doCalendarRefresh()` (`calendar-inline-edit.js:34-96`) re-fetches the current URL and does
`liveGrid.replaceWith(fresh)` — the **entire** `.excel-calendar` subtree is destroyed and rebuilt on
every note add or delete, then scroll and focus are restored and `calendar:grid-refreshed` is
dispatched.

- **In-header rendering fails outright**: the open dialog, its backdrop and the focused element are
  all removed in one shot, the close path never runs so `document.body.style.overflow = 'hidden'` is
  never cleared (**page left unscrollable**), and there is no recovery hook because the node is gone
  before the event fires.
- **Portaled**, the dialog survives but its data source does not — the `<template>` it cloned from
  and the trigger focus must return to are both replaced. So on `calendar:grid-refreshed`:
  1. **Re-portal**: detach the previously portaled nodes *before* appending the new ones, or
     `getElementById` keeps resolving the stale detached node.
  2. **Re-render in place, do not dismiss**: remember `openDate`, re-clone from the fresh
     `<template>`, re-apply `.is-open`. Deleting three notes is one panel open, not three.
  3. **Close only when the day reaches zero notes.** Focus return re-finds the trigger **by date**,
     not by the node captured on open, because that node is detached.

`calendar-column-resize.js:214-223` already re-binds on this event and deliberately does not re-seed
its cache (an in-flight POST is not in the server's render yet) — the same hazard class applies here.

### 7.4 CSS

- `.excel-calendar__header th` sets `color: var(--primary-contrast)` and
  `.excel-calendar__header th *` sets `color: inherit` (`calendar.css:2792-2806`) — a **(0,1,2)**
  specificity descendant rule. `calendar.css:2812-2827` then forces `--primary-contrast !important`
  on today/weekend `<th>`s. That is why the existing chip re-asserts
  `color: var(--text) !important` at `:2862`, `:2882`, `:2901`. **Every new text node** inside the
  chip — the `<bdi>`, the tab badge, and the `<span>` the `<loc>` tag helper emits
  (`TagHelpers/LocalizationTagHelper.cs:49` always renders a `span`) — needs the same treatment.
- **Logical properties only.** `ShiftManager.Tests/UnitTests/Css/CalendarStickyRtlSweepTests.cs:26-31`
  bans `left:`/`right:` under the `.excel-calendar`, `.cal-toolbar`, `.cal-page` prefixes.
- `.modal__body { white-space: pre-wrap }` (`components.css:817`) renders inter-tag indentation as
  blank lines inside a generated list — **must be reset** for the notes list.
- Add `padding-inline-end: 6px` to the chip stack: `.excel-calendar__col-resizer` is
  `position: absolute; inset-inline-end: -4px; width: 8px` (`calendar.css:4620-4631`) and the chip's
  `max-width: 100%` already puts the existing `×` partly under the drag handle (pre-existing).
- No new tokens, so `CalendarStickyTokenTests` stays green by construction.

### 7.5 `--excel-calendar-header-height` must stop lying (required, not optional)

It is a hard-coded **44px** constant consumed by `calendar.css:3377`
(`.excel-calendar__group-header td { top: … }`), `calendar-sticky-shadows.js:70-77` (band-pin
`rootMargin`) and `calendar-keyboard-nav.js:18-22`. **Measured live on a rendered grid with ZERO
notes: the property reads `44px` while `.excel-calendar__header` is `64px`** — already a 20px lie
today, which is why pinned group bands overlap the thead. Two chips plus a trigger makes it a
visible overlap.

One-line fix: `calendar-sticky-shadows.js:74` already reads the property off the **calendar
element** rather than `:root`, so set
`calendar.style.setProperty('--excel-calendar-header-height', thead.offsetHeight + 'px')` before the
`headerHeight` read. Affects all five calendars — in the fixing direction.

### 7.6 Accessibility

**One focusable element per day header: the trigger button.**

- Trigger: real `<button>`, `aria-haspopup="dialog"`, `aria-expanded`, `aria-label` carrying date +
  count, plus a matching `title`. Count-agnostic phrasing — the resx files have no `_One`/`_Plural`
  convention.
- Inline chips: `aria-hidden="true"`. They are truncated previews, and a `<th>` is re-announced on
  **every** grid cell the user traverses; N notes of prose there would make the calendar unusable
  with a screen reader. The untruncated copy is in the panel. R4's hover attribution stays as the
  native `title`.
- Chip `×`: `tabindex="-1"` — a focusable control inside an `aria-hidden` subtree is a violation.
  The chip `×` is a mouse shortcut; the keyboard delete path is the panel.
- Panel: `role="dialog" aria-modal="true" aria-labelledby`, `tabindex="-1"`. Focus to the close
  button on open, Escape closes, Tab cycles within (same loop as `feedback-modal.js:233-245`), focus
  returns to the trigger on close.
- Per-note delete: `aria-label` naming the author, so the list does not read as "× × × ×".
- Panel tab badge: visually-hidden `"Tab: {name}"` prefix plus an `aria-hidden` visible `<bdi>`.

This incidentally **fixes** a latent problem: today's single-note attribution sits in a
`.visually-hidden` span inside a non-focusable element inside a `<th>` — announced on every cell
traversal and reachable in no other way.

### 7.7 RTL

`unicode-bidi: isolate` on the chip (`calendar.css:2868`) isolates the chip from the `<th>`; it does
nothing for the badge *versus* the note text *inside* the chip, which stay one bidi paragraph. Three
measures, all with precedent here:

1. `<bdi>` around both runs (tab name and note text) — the `Shifts.cshtml:330` pattern, whose comment
   reads *"bidi-isolate the (possibly Latin) tab name so it renders unscrambled in the RTL strip"*.
2. `unicode-bidi: isolate` on the badge element too, covering the dot pseudo-element and digits.
3. Badge first in DOM order inside a flex row — flex reads logical order, so Hebrew puts it on the
   right with no `row-reverse` and no `[dir="rtl"]` override.

### 7.8 JS contract

- `window.CalendarPageConfig` gains **`jobTypeId`** (`Shifts.cshtml:546-552` currently has
  `isTechMolecule`, `categoryEligibilityEnabled`, `moleculeId`, `activeTabId`, `tabPrioritize` —
  confirmed live). Additive, so the two other readers are unaffected:
  `calendar-tab-prioritization.js:17` reads the whole object defensively, and
  `calendar-bottom-sheet.js:444,496-498,972-974` reads only the existing keys.
- `quickAddDayNote(date, text)` sends `{date, text, moleculeId, jobTypeId, tabId}` read internally
  from `CalendarPageConfig` — so `calendar-quick-entry.js:811,1363-1367` needs **no change**.
- `deleteDayNote(noteId)` replaces `deleteDayNote(date)` and posts to the new endpoint; the delegated
  handler at `calendar-inline-edit.js:826-833` reads `data-note-id` instead of `data-date`.
- Day notes remain excluded from Draft Mode (`calendar-quick-entry.js:114-123,759`) — unchanged.

### 7.9 Localization

**Nine new keys**, every one verified absent from BOTH `Resources/SharedResources.resx` and
`SharedResources.he-IL.resx` (`grep -c 'name="KEY"'` returned 0/0 for all nine).
**None needs a `Pages/Shared/_LocalizationScript.cshtml` entry**, because the design builds zero
user-visible strings in JS — but note that file is an explicit allowlist, so any later JS-built
string must be added there or `window.AppLocalizer.Key` is `undefined` at runtime.

| Key | EN | HE |
|---|---|---|
| `Calendar_DayNotes_PanelTitle` | Day notes | הערות יום |
| `Calendar_DayNotes_TriggerAriaLabel` | Day notes for {0} ({1}) | הערות יום ל־{0} ({1}) |
| `Calendar_DayNotes_Empty` | No notes for this day. | אין הערות ליום זה. |
| `Calendar_DayNote_TabLabel` | Tab: {0} | לשונית: {0} |
| `Calendar_DayNote_DeleteAriaLabel` | Delete the note written by {0} | מחק את ההערה שנכתבה על ידי {0} |
| `Calendar_DayNote_UnknownAuthor` | a deleted user | משתמש שנמחק |
| `Calendar_DayNote_TabInactive` | inactive | לא פעילה |
| `Error_CannotDeleteJobTypeWithTabs` | Cannot delete this job type — {0} calendar tab(s) still use it. | לא ניתן למחוק סוג עבודה זה — {0} לשוניות לוח עדיין משתמשות בו. |
| `Error_CannotDeleteJobTypeWithDayNotes` | Cannot delete this job type — {0} day note(s) are attached to its calendars. | לא ניתן למחוק סוג עבודה זה — {0} הערות יום משויכות ללוחות שלו. |

The A2 blocker keys follow the prefix already used on that page
(`Error_CannotDeleteJobTypeWithUsers`, `Error_JobTypeNotFound` at
`JobTypes/Index.cshtml.cs:283,294`) rather than inventing a `JobTypes_*` namespace — no
`JobTypes_*` key exists in the file.

**A3 extends an existing key rather than adding one.** `Tabs_DeleteConfirm` is already a format
string — *"Delete '{0}'? It has {1} shift type(s) and {2} company/ies assigned, and is the
remembered tab of {3} user(s)."* — so append a `{4}` clause for the day-note count, in both files,
and widen `ShiftTabService.GetUsageAsync`'s return tuple from
`(int ShiftTypeCount, int CompanyCount)` to include `DayNoteCount`. Note that a company that has
overridden `Tabs_DeleteConfirm` keeps its 4-placeholder text; `string.Format` ignores the extra
argument, so this cannot throw.

**Reused unchanged:** `QuickEntry_MoreItems` = `"+{0} more..."` — already the exact "+N more" pattern
(used by `calendar-quick-entry.js:426,719`); do not mint a near-duplicate. `Close`,
`QuickEntry_DeleteDayNote`, `Calendar_DayNote_WrittenBy`. **Not reusable:** `Calendar_MoreItems` =
`"more"` belongs to the legacy Month/Week calendars; `Calendar_Tab_All` is the strip label only.

**Retired but NOT deleted:** `Calendar_DayNote_LastEditedBy` (§3.4).

---

## 8. Adjacent fixes (user-approved, in scope)

### A1 — `CreatedByUserId` nullable + `SetNull`

Today **you cannot delete any user who has ever written a day note.** `CalendarDayNotes` appears
nowhere in `Pages/Admin/Users.cshtml.cs` (3435 lines) — not in the normal cleanup (`:2310-2324`),
not in the `UserDayNote` block (`:2396-2402`), not in the exhaustive force-delete block (`:2405+`)
that handles every other author FK in the model. With `CreatedByUser` at `Restrict`
(`AppDbContext.cs:760`) that delete fails with `FOREIGN KEY constraint failed`, and force-delete does
not rescue it. The irony is two lines apart: `:761-762` reasons that `UpdatedByUser` must be
`SetNull` so it *"must not add a new reason for a user delete to fail"*, while the line above does
exactly that. R3 and the fan-out both multiply the exposure.

**Reassigning authorship to the deleting admin — the `UserDayNotes` pattern — is the wrong fix
here**, for two independent reasons: under R8 `CreatedByUserId` is a *permission*, so reassignment
silently grants delete rights over a departed colleague's note to whichever admin ran the deletion;
and R4 requires the hover to name the author, so reassignment rewrites displayed provenance.

So: `CreatedByUserId int?` with `OnDelete(SetNull)`, mirroring `UpdatedByUser` at `:763`. Under R8 a
null author fails the ownership branch and falls through to the `AssignShifts` branch — the correct
outcome: nobody owns an orphaned note, so only assigners can remove it. Add `Users.cshtml.cs`
coverage as belt-and-braces only; with `SetNull` the FK no longer blocks. **Migration cost is zero
if done now** — migration 1 already triggers a rebuild for the two `AddForeignKey` calls, so EF folds
the `AlterColumn` into the same `ef_temp` table.

### A2 — job-type delete pre-check (and a crash that already exists)

`Pages/Admin/Organization/JobTypes/Index.cshtml.cs:276-291` pre-checks **only**
`Users.CountAsync(u => u.JobTypeId == id)` and has no try/catch, while `DeleteJobTypeAsync`
(`Services/JobTypeService.cs:290-306`) is a plain `Remove`. `ShiftTab.JobType` is already `Restrict`
(`AppDbContext.cs:1490`), so **deleting a job type that has tabs already throws an unhandled
`DbUpdateException` today**. Making the day-note FK `Restrict` (§3.3) adds a second cause on the same
path. Count **both** tabs and day notes in that handler and return a localized blocker.

### A3 — tab-delete preview says what moves

`ShiftTabService.GetUsageAsync` (`:105-110`) already feeds the `Tabs_DeleteConfirm` preview with
shift-type, company and remembered-preference counts. Add a day-note count so deleting a tab warns
that N day notes will move to the All view, rather than the `SetNull` FK moving them silently.

### A4 — live updates for day notes

Day notes do not broadcast: `QuickAddDayNoteModel` has no `ICalendarNotificationService` dependency,
and success only calls `triggerCalendarRefresh()` in the acting browser. The sibling
`CalendarTextEntry` **does** broadcast (`QuickAddTextEntry.cshtml.cs:158`,
`DeleteTextEntry.cshtml.cs:120-143`).

- Add `NotifyDayNoteChangedAsync(string groupName, CalendarDayNoteChangedEvent evt)` to
  `ICalendarNotificationService` (`Hubs/CalendarHub.cs:310-318`) and
  `CalendarNotificationService`, sending `"DayNoteChanged"`.
- Group name: `CalendarGroups.Shifts(moleculeId, jobTypeId)` (`CalendarHub.cs:375`), which already
  matches the note's `(molecule, jobType)`. Carry `tabId` and `date` in the payload so a client on a
  different tab can ignore it.
- Both endpoints broadcast; the client handler refreshes.
- **No hub authorization change** — §1 proved the join already succeeds for every relevant persona.

---

## 9. Accepted consequences and known limitations

1. **Fan-out creates N-way divergence.** The migration's copies are independent rows with
   independent `×` buttons. A user who deletes "the note" on the BR calendar, switches to Hakam and
   sees it still there will read that as "delete didn't work". This is the unavoidable cost of
   preserving today's visibility while moving to per-calendar keying. The migration must report what
   it did, following the `Tabs/Index.cshtml.cs:246-261` usage-count precedent.
2. **R1 narrows scope.** A note that today shows on every job-type calendar of its molecule will,
   after this change, show on one. That is the requirement.
3. **A note typed on All never appears on a real tab** (R5). By decision.
4. **Row-less calendars still swallow notes.** `Shifts.cshtml:337` gates the table on
   `Rows.Any()`, so a calendar with no shift rows shows no notes — including notes already written
   to it. **Out of scope by the user's decision**: a calendar with no shift rows has nothing to
   annotate. Fixing it would mean rendering the header outside the `Rows.Any()` guard, changing the
   empty state on all five calendars.
5. **Hub desk-switch gap.** `ValidateGroupAccessAsync` reads the raw `CompanyId` claim
   (`CalendarHub.cs:131`), so a user whose access comes *only* from switching into a desk, with no
   grant reach, would not join. Pre-existing audit finding, not reproduced here, out of scope.
6. **The degenerate `(M, NULL, NULL)` triple** (§3.5) — mitigated by the step-7 400, not eliminated
   for existing rows.
7. **Tech-molecule delete widening** (§6.2) — job-type-restricted assigners can delete Tech notes.
8. `CalendarColumn.Key`'s XML doc (`CalendarColumnPlanner.cs:4`) still says `"day:yyyy-MM-dd"` while
   `Build` emits the shared key `"day"` (`:63-71`). Cosmetic; fix opportunistically.

---

## 10. Test plan

### Unit / integration

- **`CalendarDayNoteServiceTests`** — all 8 existing tests call old signatures and will not compile.
  - Reshape and keep: `NoteWrittenFromOneDesk_IsReturnedForTheWholeMolecule`,
    `Notes_AreIsolatedPerMolecule_AndBoundedByTheDateRange`,
    `LegacyNotesWithoutAMolecule_AreNeverReturned`.
  - **Invert**: `SecondDeskWritingTheSameDay_EditsTheOneMoleculeNote_RatherThanCreatingAHiddenTwin`
    now asserts TWO notes exist (R7).
  - **Delete**: `ReturnsTheOriginalAuthorsName_AndNoEditor_WhenOnlyTheAuthorWrote`,
    `EditBySomeoneElse_KeepsTheOriginalAuthor_AndNamesTheEditor` — they test in-place-edit semantics
    that no longer exist.
  - Re-signature: `Delete_RemovesThatMoleculesNote_AndReturnsTrue`, `Delete_WhenNoNoteExists_ReturnsFalse`.
  - **New**: R5 both branches (real tab narrows; All unions and badges); All-typed note absent from a
    real tab; a Tech note `(M,NULL,NULL)` distinct from a non-Tech All note `(M,J,NULL)`; ordering
    within a day; `AuthorName` substitution for a null author; both tab names carried.
- **`QuickAddDayNoteTests`** — 5 of 6 survive largely unchanged.
  `EmptyText_DeletesTheMoleculesNote` breaks on both axes (old signature **and** removed premise) and
  must be replaced. **New**: jobType from another area → 403; tab whose `(molecule, jobType)` differs
  → 403; non-Tech with null jobType → 400.
- **`DeleteDayNoteTests`** (new) — the full gate ladder, each rung independently; **explicitly** a
  legacy `MoleculeId == null` note → 404 and *no* grant call; author-deletes-own → 200;
  non-author-without-assign → 403; non-author-with-assign → 200.
- **Migration tests** — fan-out idempotency across three runs; Tech molecules untouched; a
  zero-job-type molecule left `NULL` with `MoleculeId` preserved; the anchor matching
  `JobTypeService`'s ordering; the `MoleculeType` enum-literal assertion.
- **Unaffected**: `DraftOverlayRenderTests:98`, `ShiftsCategoryFallbackTests:71`,
  `ShiftsUserRowShiftCountTests:72` use `Mock.Of<ICalendarDayNoteService>()` with no method setups.
- **Guards that must stay green**: `CalendarStickyRtlSweepTests`, `CalendarStickyTokenTests`,
  `CalendarStickyKeyParityTests`, and resx EN↔HE parity.

### Verification discipline

- `dotnet test -- xUnit.ParallelizeTestCollections=false`. **A PASS can come from a STALE assembly —
  always check the reported test COUNT**, not just the result. Baseline is ~2062.
- **The backfill must be verified against a copy of the real `app.db`, not only the test fixture.**
  The dev DB's single note sits in the one molecule where the Tech bug is invisible; "tests pass" is
  not evidence for this migration.
- Live browser check on both calendars in **both** languages, at week and month view, and at a
  hand-narrowed 40px day column.
- Confirm `--excel-calendar-header-height` now equals the measured `thead.offsetHeight`.

### Process

A final adversarial review pass against the **final** requirement set, because R8 and the separate
delete endpoint landed mid-review and **inverted** one earlier recommendation (A1's fix direction).
Reviews run against a moving requirement set cannot be treated as closed.

---

## 11. Files touched

**Modify:** `Models/CalendarDayNote.cs`, `Data/AppDbContext.cs`,
`Services/{I,}CalendarDayNoteService.cs`, `Pages/Calendar/Shifts.cshtml{,.cs}`,
`Pages/Api/Calendar/QuickAddDayNote.cshtml.cs`,
`Pages/Shared/Components/ExcelCalendarTable/Default.cshtml`,
`ViewComponents/ExcelCalendarTableViewComponent.cs` (the `DayNotes` property is on
`ExcelCalendarTableViewModel` at `:77`, not on the component class), `Hubs/CalendarHub.cs`,
`wwwroot/js/calendar-inline-edit.js`, `wwwroot/js/calendar-sticky-shadows.js`,
`wwwroot/css/calendar.css`, `Resources/SharedResources.resx`,
`Resources/SharedResources.he-IL.resx`, `Pages/Admin/Organization/JobTypes/Index.cshtml.cs`,
`Services/JobTypeService.cs`, `Services/ShiftTabService.cs`,
`Pages/Admin/Organization/Tabs/Index.cshtml{,.cs}`, `Pages/Admin/Users.cshtml.cs`.

**Create:** `Pages/Api/Calendar/DeleteDayNote.cshtml{,.cs}`, two migrations + a shared backfill-SQL
class, `ShiftManager.Tests/UnitTests/Pages/DeleteDayNoteTests.cs`, migration tests.

**Regenerate after shipping, never hand-edit:** `FinalProductPublish/` — tracked in git (539 files),
holds byte-identical copies of `calendar-inline-edit.js`, `calendar-quick-entry.js`, `calendar.css`,
`AppDbContext.cs`; recreated by `scripts/Update-FinalProductPublish.ps1 -Version`.
`ProjectPublish/` and `Backups/` are untracked local output — nothing to do.

**No change needed:** `Program.cs` DI (same interface name, `:429`),
`ApiAuthenticationMiddleware.cs` (`/Api/Calendar` prefix covers the new endpoint, `:306`), Swagger
(Razor PageModels are not discovered), `Services/UserDataExportService.cs` and the
`Users.cshtml.cs` `UserDayNotes` block (both reference the **different** `UserDayNote` entity),
`calendar-quick-entry.js`.
