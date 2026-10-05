# Day Notes: Per-Calendar Scope, Many Per Day — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Re-key `CalendarDayNote` from `(MoleculeId, Date)` with one note per day to the calendar triple `(MoleculeId, JobTypeId?, TabId?)` with unlimited notes per day, each attributed and individually deletable.

**Architecture:** The note's scope becomes the calendar identity exactly as `Pages/Calendar/Shifts.cshtml.cs` resolves it per request. Reads apply a union rule for the synthetic "All" tab. Writes always insert; deletes move to a sibling endpoint keyed by note id and gated on author-or-assign-tier. The header renders up to 2 preview chips plus a trigger opening a body-portaled modal.

**Tech Stack:** ASP.NET Core 8 (EF provider reports **9.0.9**), SQLite, Razor Pages, SignalR, xUnit + FluentAssertions, bilingual EN/he-IL with RTL.

**Spec:** `docs/superpowers/specs/2026-10-05-daynotes-per-calendar-design.md` — read it before Task 1. Every "why" lives there; this plan is the "how".

## Global Constraints

- Connection string key is `"Default"`, never `"DefaultConnection"`.
- Every `IgnoreQueryFilters()` call needs a `// SECURITY-AUDITED:` comment saying why it is safe.
- New entity fields need all three: model property, EF config in `AppDbContext`, migration.
- Every user-visible string needs a key in **both** `Resources/SharedResources.resx` and `Resources/SharedResources.he-IL.resx`. **Always `grep -c 'name="KEY"'` both files before adding** — a duplicate key has broken localization tests before.
- No native `alert()`; use `window.FeedbackModal.show()` / `Toast.*()`.
- CSS under the `.excel-calendar`, `.cal-toolbar`, `.cal-page` prefixes must use logical properties (`inset-inline-start`, never `left`). Enforced by `ShiftManager.Tests/UnitTests/Css/CalendarStickyRtlSweepTests.cs:26-31`.
- Test command: `dotnet test -- xUnit.ParallelizeTestCollections=false`. **A PASS can come from a STALE assembly — always check the reported test COUNT.** Record the baseline count in Task 0 and compare after every task.
- CSS/JS are minified at build time with a `?v=` hash, and the dev app serves `bin/Debug` assets — source edits are not served until a rebuild. `curl` the served file for your marker before browser-testing a frontend change.
- **Never rebuild while the app is running** — a locked executable produces a stale binary that reports false success. Stop the app (`TaskStop`, then verify the port is free and no `ShiftManager` process remains) before any build.
- `FinalProductPublish/` is generated. Never hand-edit it; regenerate with `scripts/Update-FinalProductPublish.ps1 -Version`.
- Do not commit `.serena/project.yml` or `packages.lock.json` — tooling rewrites them incidentally. `git checkout --` them if they appear dirty.

---

## File Structure

**Modified**
| File | Responsibility after this change |
|---|---|
| `Models/CalendarDayNote.cs` | Entity gains `JobTypeId`, `TabId`, nullable `CreatedByUserId`, explicit `JobType`/`Tab` navs. Doc comment rewritten — current text asserts one-note-per-day, which becomes false. |
| `Data/AppDbContext.cs:746-764` | Drop unique index, add `(MoleculeId, JobTypeId, Date, TabId)`, add two FKs, change `CreatedByUser` to `SetNull`. |
| `Services/ICalendarDayNoteService.cs` | `CalendarScope` record, reshaped `DayNoteView`, four methods. |
| `Services/CalendarDayNoteService.cs` | Always-insert, delete-by-id, scope read with the All-union branch. |
| `Pages/Calendar/Shifts.cshtml.cs` | Builds `CalendarScope`; passes `CanWriteNote` to the view model. |
| `Pages/Calendar/Shifts.cshtml:546-552` | `CalendarPageConfig` gains `jobTypeId`. |
| `ViewComponents/ExcelCalendarTableViewComponent.cs:71-77` | `DayNotes` becomes `Dictionary<DateOnly, List<DayNoteView>>`; new `CanWriteNote`, `DayNotesScopeIsAllTab`. |
| `Pages/Shared/Components/ExcelCalendarTable/Default.cshtml:81-106` | Chip stack + trigger + inert `<template>` + panel shell. |
| `Pages/Api/Calendar/QuickAddDayNote.cshtml.cs` | Accepts the triple, validates it, always inserts, broadcasts. |
| `wwwroot/js/calendar-inline-edit.js` | `quickAddDayNote` sends the triple; `deleteDayNote(noteId)`; in-flight guards. |
| `wwwroot/js/calendar-sticky-shadows.js:70-77` | Publishes the real header height. |
| `wwwroot/css/calendar.css` | Chip stack, trigger, badge, panel list styling. |
| `Hubs/CalendarHub.cs:310-318` | `NotifyDayNoteChangedAsync` + event record. |
| `Pages/Admin/Organization/JobTypes/Index.cshtml.cs:275-291` | Pre-checks tabs and day notes before delete. |
| `Services/ShiftTabService.cs:105-110` | `GetUsageAsync` returns a day-note count. |
| `Pages/Admin/Organization/Tabs/Index.cshtml.cs:246-261` | Delete-confirm message includes it. |
| `Pages/Admin/Users.cshtml.cs` | Belt-and-braces day-note cleanup on user delete. |
| `Resources/SharedResources{,.he-IL}.resx` | Nine new keys; `Tabs_DeleteConfirm` gains `{4}`. |

**Created**
| File | Responsibility |
|---|---|
| `Pages/Api/Calendar/DeleteDayNote.cshtml` + `.cshtml.cs` | Delete one note by id, author-or-assign-tier gated. |
| `wwwroot/js/calendar-day-notes-panel.js` | Portal, open/close, focus trap, refresh survival. Kept separate from `calendar-inline-edit.js`, which is already large and owns transport, not UI. |
| `Migrations/<ts>_ScopeCalendarDayNotesToCalendar.cs` | Schema. |
| `Migrations/<ts>_FanOutCalendarDayNotesByJobType.cs` | Data. |
| `Migrations/CalendarDayNoteJobTypeFanOutSql.cs` | Shared SQL so a test can run it. |
| `ShiftManager.Tests/UnitTests/Pages/DeleteDayNoteTests.cs` | Gate-ladder tests. |
| `ShiftManager.Tests/UnitTests/Migrations/CalendarDayNoteJobTypeFanOutTests.cs` | Fan-out correctness + idempotency. |

---

## Task 0: Baseline

**Files:** none (measurement only)

**Interfaces:**
- Consumes: nothing
- Produces: a recorded test count and a clean tree, used by every later task's verification step

- [ ] **Step 1: Confirm nothing is running and the tree is clean**

```bash
git -C /c/Users/katzi/Downloads/ShiftManager status --porcelain   # expect empty
```

```powershell
Get-Process -Name ShiftManager -ErrorAction SilentlyContinue   # expect nothing
```

If either is dirty, resolve before continuing. `.serena/project.yml` and `packages.lock.json` get rewritten by tooling — `git checkout --` them.

- [ ] **Step 2: Record the baseline test count**

Run: `dotnet test -- xUnit.ParallelizeTestCollections=false`
Expected: all green. **Write the exact total down** (memory says ~2062; trust the number you see, not the memory). Every later task compares against this.

- [ ] **Step 3: Back up the dev database for backfill verification**

```bash
cd /c/Users/katzi/Downloads/ShiftManager
cp app.db "$TMPDIR/app.db.prefanout"
```

Task 1 needs a copy of **real** data to verify the fan-out. The dev DB holds one note (`id=1, Date=2026-09-18, MoleculeId=9`), and molecule 9 is the one molecule where the Tech bug is invisible — so also hand-insert a Tech-molecule note into the copy (molecule 6, Shikma) before testing, per Task 1 Step 11.

---

## Task 1: Data model, migrations, service, and minimal working UI

This is deliberately one task and one commit. **Dropping the unique index without the service rewrite 500s `/Calendar/Shifts`**: `CalendarDayNoteService.cs:83` is `notes.ToDictionary(n => n.Date, …)` justified by the now-false comment at `:67`, and `Program.cs:615` runs `MigrateAsync()` at startup — so any binary booting against the migrated DB crashes for every viewer of a molecule with two notes on one day. The view/page are updated minimally here (plain stacked chips, no panel) so every commit is green and shippable; Task 3 upgrades the UI.

**Files:**
- Modify: `Models/CalendarDayNote.cs`, `Data/AppDbContext.cs:746-764`, `Services/ICalendarDayNoteService.cs`, `Services/CalendarDayNoteService.cs`, `Pages/Calendar/Shifts.cshtml.cs:350`, `ViewComponents/ExcelCalendarTableViewComponent.cs:77`, `Pages/Shared/Components/ExcelCalendarTable/Default.cshtml:81-106`, `Pages/Api/Calendar/QuickAddDayNote.cshtml.cs` (compile-only: call the new add method)
- Create: `Migrations/CalendarDayNoteJobTypeFanOutSql.cs`, two migrations, `ShiftManager.Tests/UnitTests/Migrations/CalendarDayNoteJobTypeFanOutTests.cs`
- Test: `ShiftManager.Tests/UnitTests/Services/CalendarDayNoteServiceTests.cs` (rewrite)

**Interfaces:**
- Consumes: Task 0's baseline count
- Produces:
  ```csharp
  public sealed record CalendarScope(int MoleculeId, int? JobTypeId, int? TabId);
  public sealed record DayNoteView(
      int Id, string Text, string AuthorName, int? CreatedByUserId,
      int? TabId, string? TabNameEn, string? TabNameHe, bool TabIsActive);

  Task<CalendarDayNote> AddDayNoteAsync(DateOnly date, CalendarScope scope, int authorCompanyId, string text, int userId);
  Task<CalendarDayNote?> GetByIdAsync(int noteId);
  Task<bool> DeleteDayNoteAsync(int noteId);
  Task<Dictionary<DateOnly, List<DayNoteView>>> GetDayNotesForCalendarAsync(CalendarScope scope, DateOnly start, DateOnly end);
  ```
  Plus these view-model additions on `ExcelCalendarTableViewModel`, which Task 3's markup binds to:
  ```csharp
  Dictionary<DateOnly, List<DayNoteView>> DayNotes;   // was Dictionary<DateOnly, DayNoteView>
  bool CanWriteNote;                                   // gates ADDING
  bool DayNotesScopeIsAllTab;                          // true => render tab badges
  int CurrentUserId;                                   // per-note R8 ownership check
  ```
  Tasks 2–5 and 8 depend on these exact names and types. `AuthorName` is never null — the service
  substitutes the literal resx **key** `"Calendar_DayNote_UnknownAuthor"`, which the view localizes;
  no view ever branches on a null author.

- [ ] **Step 1: Write the failing service tests**

Rewrite `CalendarDayNoteServiceTests.cs`. Keep the existing seed helper shape (`SqliteDbContextFixture.CreateAsync()`, `Project`/`Area`/`Molecule`/`Company`/`AppUser` with `Slug` and `DisplayName`) and extend it with job types and tabs:

```csharp
private const int Mol1 = 1, MolTech = 6;
private const int DeskA = 1, DeskB = 2;
private const int Avi = 7, Bat = 8;
private const int JtAlhut = 1, JtBr = 2;
private const int TabGeo = 1, TabTacti = 2, TabDead = 3;
private static readonly DateOnly D = new(2026, 9, 18);

private static async Task<SqliteDbContextFixture> SeededAsync()
{
    var f = await SqliteDbContextFixture.CreateAsync();
    var db = f.Db;
    db.Projects.Add(new Project { Id = 1, Name = "P", DisplayName = "P" });
    db.Areas.Add(new Area { Id = 1, ProjectId = 1, Name = "A", DisplayName = "A" });
    db.Molecules.AddRange(
        new Molecule { Id = Mol1, AreaId = 1, Name = "M1", DisplayName = "M1", Type = MoleculeType.Workforce },
        new Molecule { Id = MolTech, AreaId = 1, Name = "MT", DisplayName = "MT", Type = MoleculeType.Tech });
    db.Companies.AddRange(
        new Company { Id = DeskA, Name = "A", Slug = "a", DisplayName = "A", MoleculeId = Mol1 },
        new Company { Id = DeskB, Name = "B", Slug = "b", DisplayName = "B", MoleculeId = Mol1 });
    db.Users.AddRange(
        new AppUser { Id = Avi, Email = "avi@test", DisplayName = "Avi", CompanyId = DeskA, IsActive = true },
        new AppUser { Id = Bat, Email = "bat@test", DisplayName = "Bat", CompanyId = DeskB, IsActive = true });
    db.JobTypes.AddRange(
        new JobType { Id = JtAlhut, AreaId = 1, Name = "Alhut", DisplayName = "אלחוט", IsActive = true, SortOrder = 1 },
        new JobType { Id = JtBr,    AreaId = 1, Name = "BR",    DisplayName = "ב\"ר",  IsActive = true, SortOrder = 2 });
    db.ShiftTabs.AddRange(
        new ShiftTab { Id = TabGeo,   MoleculeId = Mol1, JobTypeId = JtAlhut, NameEn = "Geo",   NameHe = "גאו",  IsActive = true },
        new ShiftTab { Id = TabTacti, MoleculeId = Mol1, JobTypeId = JtAlhut, NameEn = "Tacti", NameHe = "טקטי", IsActive = true },
        new ShiftTab { Id = TabDead,  MoleculeId = Mol1, JobTypeId = JtAlhut, NameEn = "Old",   NameHe = "ישן",  IsActive = false });
    await db.SaveChangesAsync();
    return f;
}

private static CalendarScope All(int mol = Mol1, int? jt = JtAlhut) => new(mol, jt, null);
private static CalendarScope Tab(int tabId, int mol = Mol1, int? jt = JtAlhut) => new(mol, jt, tabId);
```

Tests to write (each `[Fact]`, `await using var f = await SeededAsync(); var svc = new CalendarDayNoteService(f.Db);`):

```csharp
[Fact] // R3 — this INVERTS the old SecondDeskWritingTheSameDay_EditsTheOneMoleculeNote test
public async Task TwoWritersOnTheSameDay_ProduceTwoNotes_NotOneEdited()
{
    await svc.AddDayNoteAsync(D, All(), DeskA, "first", Avi);
    await svc.AddDayNoteAsync(D, All(), DeskB, "second", Bat);

    var notes = await svc.GetDayNotesForCalendarAsync(All(), D, D);
    notes[D].Select(n => n.Text).Should().Equal("first", "second"); // CreatedAt asc, then Id
    notes[D].Select(n => n.AuthorName).Should().Equal("Avi", "Bat");
}

[Fact] // R5 — a real tab shows ONLY its own notes
public async Task ViewingARealTab_ShowsOnlyThatTabsNotes()
{
    await svc.AddDayNoteAsync(D, All(), DeskA, "all-note", Avi);
    await svc.AddDayNoteAsync(D, Tab(TabGeo), DeskA, "geo-note", Avi);
    await svc.AddDayNoteAsync(D, Tab(TabTacti), DeskA, "tacti-note", Avi);

    var geo = await svc.GetDayNotesForCalendarAsync(Tab(TabGeo), D, D);
    geo[D].Select(n => n.Text).Should().Equal("geo-note");
}

[Fact] // R5 — All unions every note of the (molecule, jobType) calendar, badged
public async Task ViewingAll_UnionsEveryNote_AndBadgesTheTabScopedOnes()
{
    await svc.AddDayNoteAsync(D, All(), DeskA, "all-note", Avi);
    await svc.AddDayNoteAsync(D, Tab(TabGeo), DeskA, "geo-note", Avi);

    var all = await svc.GetDayNotesForCalendarAsync(All(), D, D);
    all[D].Should().HaveCount(2);
    all[D].Single(n => n.Text == "all-note").TabNameEn.Should().BeNull();
    var geo = all[D].Single(n => n.Text == "geo-note");
    geo.TabNameEn.Should().Be("Geo");
    geo.TabNameHe.Should().Be("גאו");   // BOTH names, or Hebrew UI gets English badges
    geo.TabIsActive.Should().BeTrue();
}

[Fact] // F11 — a deactivated tab's note is still returned, marked inactive
public async Task NoteOnADeactivatedTab_IsStillReturnedFromAll_MarkedInactive()
{
    await svc.AddDayNoteAsync(D, Tab(TabDead), DeskA, "old-note", Avi);

    var all = await svc.GetDayNotesForCalendarAsync(All(), D, D);
    all[D].Single().TabIsActive.Should().BeFalse();
    all[D].Single().TabNameEn.Should().Be("Old");
}

[Fact] // R1 — job type is part of the key
public async Task NotesAreIsolatedPerJobType()
{
    await svc.AddDayNoteAsync(D, All(jt: JtAlhut), DeskA, "alhut", Avi);
    await svc.AddDayNoteAsync(D, All(jt: JtBr), DeskA, "br", Avi);

    (await svc.GetDayNotesForCalendarAsync(All(jt: JtAlhut), D, D))[D].Single().Text.Should().Be("alhut");
    (await svc.GetDayNotesForCalendarAsync(All(jt: JtBr), D, D))[D].Single().Text.Should().Be("br");
}

[Fact] // §3.5 — a Tech note (M,NULL,NULL) is distinct from a non-Tech All note (M,J,NULL)
public async Task TechCalendarNote_IsDistinctFromAJobTypedAllNote()
{
    await svc.AddDayNoteAsync(D, new CalendarScope(MolTech, null, null), DeskA, "tech", Avi);
    await svc.AddDayNoteAsync(D, All(), DeskA, "workforce", Avi);

    (await svc.GetDayNotesForCalendarAsync(new CalendarScope(MolTech, null, null), D, D))[D]
        .Single().Text.Should().Be("tech");
}

[Fact] // bounded by range, and molecule-isolated (carried over from the old suite)
public async Task NotesAreBoundedByTheDateRange_AndIsolatedPerMolecule()
{
    await svc.AddDayNoteAsync(D, All(), DeskA, "in", Avi);
    await svc.AddDayNoteAsync(D.AddDays(10), All(), DeskA, "out", Avi);

    var got = await svc.GetDayNotesForCalendarAsync(All(), D, D.AddDays(1));
    got.Should().ContainKey(D);
    got.Should().NotContainKey(D.AddDays(10));
}

[Fact] // legacy rows stay invisible (carried over)
public async Task LegacyNotesWithoutAMolecule_AreNeverReturned()
{
    f.Db.CalendarDayNotes.Add(new CalendarDayNote
    {
        Date = D, Text = "legacy", CompanyId = DeskA, MoleculeId = null, CreatedByUserId = Avi
    });
    await f.Db.SaveChangesAsync();

    (await svc.GetDayNotesForCalendarAsync(All(), D, D)).Should().BeEmpty();
}

[Fact] // A1 — a deleted author leaves the note readable
public async Task NoteWithANullAuthor_ReportsTheUnknownAuthorLabel()
{
    var note = await svc.AddDayNoteAsync(D, All(), DeskA, "orphan", Avi);
    note.CreatedByUserId = null;
    await f.Db.SaveChangesAsync();

    var got = await svc.GetDayNotesForCalendarAsync(All(), D, D);
    got[D].Single().AuthorName.Should().Be("Calendar_DayNote_UnknownAuthor");
    got[D].Single().CreatedByUserId.Should().BeNull();
}

[Fact] // R7 — delete is by id
public async Task Delete_RemovesOnlyThatNote()
{
    var a = await svc.AddDayNoteAsync(D, All(), DeskA, "keep", Avi);
    var b = await svc.AddDayNoteAsync(D, All(), DeskA, "drop", Bat);

    (await svc.DeleteDayNoteAsync(b.Id)).Should().BeTrue();
    (await svc.GetDayNotesForCalendarAsync(All(), D, D))[D].Single().Id.Should().Be(a.Id);
}

[Fact]
public async Task Delete_WhenNoteDoesNotExist_ReturnsFalse()
    => (await svc.DeleteDayNoteAsync(424242)).Should().BeFalse();
```

**Delete outright** (they test in-place-edit semantics R7 removes): `ReturnsTheOriginalAuthorsName_AndNoEditor_WhenOnlyTheAuthorWrote`, `EditBySomeoneElse_KeepsTheOriginalAuthor_AndNamesTheEditor`, `SecondDeskWritingTheSameDay_EditsTheOneMoleculeNote_RatherThanCreatingAHiddenTwin`.

> `AuthorName` returns the raw resx **key** `"Calendar_DayNote_UnknownAuthor"`, not localized text — the service has no localizer. The view localizes it. Assert on the key.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --filter FullyQualifiedName~CalendarDayNoteServiceTests -- xUnit.ParallelizeTestCollections=false`
Expected: **compile errors** — `CalendarScope`, `AddDayNoteAsync`, `JobTypeId` etc. do not exist.

- [ ] **Step 3: Update the entity**

`Models/CalendarDayNote.cs` — add the two scope columns, make the author nullable, add explicit navs. Replace the class doc comment: the current one asserts one-note-per-day, which is now false. The new comment must state (a) the note is keyed to the calendar triple, (b) `JobTypeId == null` means a Tech calendar **and** is therefore unavailable as an "all job types" sentinel, (c) `TabId == null` means the synthetic All view, (d) `(M, NULL, NULL)` is ambiguous between Tech and a job-type-less non-Tech molecule and must not be "fixed" (spec §3.5).

```csharp
public int? JobTypeId { get; set; }
public int? TabId { get; set; }
public int? CreatedByUserId { get; set; }     // was int

public AppUser? CreatedByUser { get; set; }   // was AppUser (non-null)
public JobType? JobType { get; set; }         // nav name MUST match JobTypeId
public ShiftTab? Tab { get; set; }            // nav name MUST match TabId
```

> Nav naming is load-bearing: this project shipped a shadow-FK bug where a nav called `Trainee` pointed at `TraineeId` while the column was `TraineeUserId`. Name each nav after its FK and configure both explicitly.

- [ ] **Step 4: Update the EF configuration**

`Data/AppDbContext.cs:746-764` — replace the index and add the FKs:

```csharp
// Many notes per day per calendar (R3), so NO unique index. Column order is measured, not guessed:
// the All view (TabId unfiltered) is the DEFAULT view, and with TabId third the Date range becomes
// unusable and SQLite scans every tab/date entry in the (molecule, jobType) group. TabId is both the
// least selective column and the one the hot branch does not filter on, so it goes last.
entity.HasIndex(e => new { e.MoleculeId, e.JobTypeId, e.Date, e.TabId });
entity.HasIndex(e => e.CompanyId);   // backs the inherited company query filter below

// SetNull, mirroring UpdatedByUser: a Restrict author FK plus no cleanup in Admin/Users made it
// impossible to delete any user who had ever written a day note. A null author fails R8's ownership
// branch and falls through to the AssignShifts branch — nobody owns an orphaned note.
entity.HasOne(e => e.CreatedByUser).WithMany().HasForeignKey(e => e.CreatedByUserId)
      .OnDelete(DeleteBehavior.SetNull);

// Restrict, NOT SetNull. JobTypeId == null already MEANS "Tech calendar", so nulling a note would
// not hide it — it would RELOCATE it onto a calendar it was never written for. Matches every other
// JobType FK in this model. The companion pre-check lives in Admin/Organization/JobTypes.
entity.HasOne(e => e.JobType).WithMany().HasForeignKey(e => e.JobTypeId)
      .OnDelete(DeleteBehavior.Restrict);

// SetNull: tab deletion is a HARD delete (ShiftTabService.Remove). Cascade would destroy user text;
// SetNull drops the note into the All bucket, which is safe now that uniqueness is gone and strictly
// NARROWS visibility (before: All + tab T; after: All only). Mirrors UserShiftTabPreference.TabId.
entity.HasOne(e => e.Tab).WithMany().HasForeignKey(e => e.TabId)
      .OnDelete(DeleteBehavior.SetNull);
```

Keep `Company` → `Restrict`, `Molecule` → `Cascade`, `UpdatedByUser` → `SetNull`. Remove the nav-less `HasOne<Molecule>()` only if you replace it with an equivalent explicit configuration; otherwise leave it exactly as is.

- [ ] **Step 5: Rewrite the service interface**

`Services/ICalendarDayNoteService.cs` — the records and four methods from **Interfaces** above. Document on `GetDayNotesForCalendarAsync` that `TabId == null` means the All union, not "notes with no tab".

- [ ] **Step 6: Rewrite the service implementation**

`Services/CalendarDayNoteService.cs`. The read is the one place a mistake is invisible:

```csharp
public async Task<Dictionary<DateOnly, List<DayNoteView>>> GetDayNotesForCalendarAsync(
    CalendarScope scope, DateOnly start, DateOnly end)
{
    // SECURITY-AUDITED: IgnoreQueryFilters is required, not incidental. A note is keyed to a CALENDAR
    // and must be visible to viewers from every desk in the molecule, while the entity's inherited
    // company filter would restrict it to the writer's desk. Callers (the Shifts page and both
    // day-note endpoints) authorise the molecule via ShiftCalendarAccess before calling.
    var q = _db.CalendarDayNotes
        .IgnoreQueryFilters()
        .Where(n => n.MoleculeId == scope.MoleculeId
                 && n.JobTypeId == scope.JobTypeId
                 && n.Date >= start && n.Date <= end);

    // A real tab narrows. The All view (TabId == null) applies NO TabId predicate at all — that is
    // the union R5 requires.
    //
    // DO NOT collapse these branches into one expression. `n.TabId == scope.TabId` translates to
    // `TabId IS NULL` on the All branch, which returns only the unbadged notes and SILENTLY DROPS
    // every tab-scoped one — failing the requirement while compiling, translating and looking
    // correct. And `(n.TabId ?? 0) == (scope.TabId ?? 0)` translates but defeats the index.
    if (scope.TabId.HasValue)
        q = q.Where(n => n.TabId == scope.TabId);

    var rows = await q
        .OrderBy(n => n.CreatedAt).ThenBy(n => n.Id)   // stable: the 2 inline chips and the "+N" split
        .Select(n => new
        {
            n.Id, n.Date, n.Text, n.CreatedByUserId, n.TabId,
            AuthorName = n.CreatedByUser != null ? n.CreatedByUser.DisplayName : null,
            TabNameEn = n.Tab != null ? n.Tab.NameEn : null,
            TabNameHe = n.Tab != null ? n.Tab.NameHe : null,
            TabIsActive = n.Tab == null || n.Tab.IsActive
        })
        .ToListAsync();

    return rows
        .GroupBy(r => r.Date)
        .ToDictionary(g => g.Key, g => g.Select(r => new DayNoteView(
            r.Id, r.Text,
            // The view localizes this key. The service has no localizer, so it returns the key.
            r.AuthorName ?? "Calendar_DayNote_UnknownAuthor",
            r.CreatedByUserId, r.TabId, r.TabNameEn, r.TabNameHe, r.TabIsActive)).ToList());
}
```

> `GroupBy(...).ToDictionary(g => g.Key, ...)` — **never** `ToDictionary(n => n.Date, ...)`. That is the exact call that would throw `ArgumentException: An item with the same key has already been added` once a day holds two notes.

`AddDayNoteAsync` always inserts, setting `CompanyId = authorCompanyId` explicitly so `CompanyIdInterceptor` (which only fires at `CompanyId == 0`) leaves it alone. `DeleteDayNoteAsync(int noteId)` loads by id with `IgnoreQueryFilters()` (same audited reason), removes, returns whether a row existed. `GetByIdAsync` likewise — Task 2's delete endpoint needs the row to authorize it.

Rewrite the stale comment at the old `:67` ("the filtered unique index guarantees at most one row per day here"). It is now false and it was the justification for the crash.

- [ ] **Step 7: Run the service tests**

Run: `dotnet test --filter FullyQualifiedName~CalendarDayNoteServiceTests -- xUnit.ParallelizeTestCollections=false`
Expected: PASS, 11 tests.

- [ ] **Step 8: Write the fan-out SQL and its tests**

Create `Migrations/CalendarDayNoteJobTypeFanOutSql.cs` holding the two statements from spec §4 verbatim as `public const string InsertCopies` and `public const string UpdateAnchor`, with the four traps documented as comments. Then `ShiftManager.Tests/UnitTests/Migrations/CalendarDayNoteJobTypeFanOutTests.cs`:

```csharp
[Fact] // F1 — the bug that would have destroyed real production notes
public async Task TechMoleculeNotes_AreLeftCompletelyUntouched()
{
    // molecule Type == 1 (Tech) runs with JobTypeId == null permanently, so a fan-out would make
    // its notes unreachable with no error and no audit row.
    await RunFanOutAsync(db);
    var techNote = await db.CalendarDayNotes.IgnoreQueryFilters()
        .SingleAsync(n => n.MoleculeId == MolTech);
    techNote.JobTypeId.Should().BeNull();
    (await db.CalendarDayNotes.IgnoreQueryFilters().CountAsync(n => n.MoleculeId == MolTech))
        .Should().Be(1);
}

[Fact]
public async Task NonTechNote_IsFannedOutToEveryResolvableJobType_WithTheAnchorKeepingTheOriginalId()
{
    var seeded = new CalendarDayNote
    {
        Date = D, Text = "molecule note", CompanyId = DeskA, MoleculeId = Mol1,
        JobTypeId = null, CreatedByUserId = Avi, CreatedAt = new DateTime(2026, 9, 18, 8, 0, 0)
    };
    db.CalendarDayNotes.Add(seeded);
    await db.SaveChangesAsync();
    var originalId = seeded.Id;

    await RunFanOutAsync(db);
    var rows = await db.CalendarDayNotes.IgnoreQueryFilters()
        .Where(n => n.MoleculeId == Mol1).ToListAsync();
    rows.Select(r => r.JobTypeId).Should().BeEquivalentTo(new int?[] { JtAlhut, JtBr });
    // The anchor MUST match JobTypeService's ordering (SortOrder, Name) and Shifts.cshtml.cs's
    // AvailableJobTypes.FirstOrDefault(), or the calendar opens on a copy the user never saw.
    rows.Single(r => r.Id == originalId).JobTypeId.Should().Be(JtAlhut);
}

[Fact]
public async Task FanOut_IsIdempotent_AcrossThreeRuns()
{
    await RunFanOutAsync(db);
    var after1 = await SnapshotAsync(db);
    await RunFanOutAsync(db);
    await RunFanOutAsync(db);
    (await SnapshotAsync(db)).Should().BeEquivalentTo(after1);
}

[Fact]
public async Task MoleculeWithNoResolvableJobTypes_KeepsItsNote_WithMoleculeIdPreserved()
{
    await RunFanOutAsync(db);
    var n = await db.CalendarDayNotes.IgnoreQueryFilters().SingleAsync(x => x.MoleculeId == MolNoJt);
    n.JobTypeId.Should().BeNull();
    n.MoleculeId.Should().Be(MolNoJt);   // preserved, not destroyed
}

[Fact] // the SQL hard-codes 0/1/2; migration SQL cannot be re-derived if the enum is reordered
public void MoleculeTypeEnumLiterals_AreFrozen()
{
    ((int)MoleculeType.Workforce).Should().Be(0);
    ((int)MoleculeType.Tech).Should().Be(1);
    ((int)MoleculeType.Helper).Should().Be(2);
}
```

`RunFanOutAsync` executes `InsertCopies` then `UpdateAnchor` via `db.Database.ExecuteSqlRawAsync`. **INSERT first.** Reversed, the UPDATE consumes the `JobTypeId IS NULL` marker, the INSERT matches nothing, no copies are made, and the migration reports success — a silent no-op. Add a test asserting that order if you can express it.

- [ ] **Step 9: Run the fan-out tests to verify they fail**

Run: `dotnet test --filter FullyQualifiedName~CalendarDayNoteJobTypeFanOut -- xUnit.ParallelizeTestCollections=false`
Expected: FAIL — `CalendarDayNoteJobTypeFanOutSql` does not exist yet, or the SQL is wrong.

- [ ] **Step 10: Create both migrations**

```bash
dotnet ef migrations add ScopeCalendarDayNotesToCalendar
dotnet ef migrations add FanOutCalendarDayNotesByJobType
```

Migration 1 body order (only the two `AddForeignKey` calls trigger the SQLite rebuild; EF hoists them to the end wherever you write them): `DropIndex` → `AddColumn JobTypeId` → `AddColumn TabId` → `AlterColumn CreatedByUserId` nullable → `CreateIndex` → two `AddForeignKey`.

> Putting `AlterColumn CreatedByUserId` here is what makes A1 **free**: migration 1 is already rebuilding the table for the FKs, so EF folds this into the same `ef_temp` table. Deferred, it costs a full standalone rebuild later.

Migration 2 is only `migrationBuilder.Sql(InsertCopies);` then `migrationBuilder.Sql(UpdateAnchor);`.

Declare migration 2 **one-way**: after fan-out, `Down()` cannot recreate `IX_CalendarDayNotes_MoleculeId_Date` because the copies share `(MoleculeId, Date)` by construction. Add a comment saying so, matching the existing caveat at `Migrations/20260917094723_...cs:94-99`.

> Two migrations, not one — **and not for the reason previously recorded in this repo.** EF's SQLite generator hoists rebuild-triggering ops to the end, so raw SQL *does* see newly added columns; the old "SQL behind a pending rebuild runs pre-rebuild" explanation is false (measured with `dotnet ef migrations script`). The real reason: in one migration the correctness depends on an invisible intra-body rule — `Sql()` must follow `DropIndex()` — and writing it first fails at runtime with `UNIQUE constraint failed: CalendarDayNotes.MoleculeId, CalendarDayNotes.Date`. Two migrations make the order a property of the sequence, which EF enforces. Also: `dotnet ef migrations script --idempotent` is unsupported on SQLite, so re-runnability must live in the SQL.

- [ ] **Step 11: Verify the fan-out against a copy of the REAL database**

Tests are not sufficient evidence here: the dev DB's single note sits in molecule 9, the one molecule where the Tech bug is invisible.

```bash
cd /c/Users/katzi/Downloads/ShiftManager
cp "$TMPDIR/app.db.prefanout" /tmp/fanout-probe.db
# insert a Tech-molecule note (molecule 6 = Shikma) so the F1 case is actually exercised
PYTHONIOENCODING=utf-8 python - <<'PY'
import sqlite3
c = sqlite3.connect("/tmp/fanout-probe.db")
c.execute("""INSERT INTO CalendarDayNotes (Date, Text, CompanyId, MoleculeId, CreatedByUserId, CreatedAt)
             VALUES ('2026-09-18','tech note',15,6,1,'2026-09-18 00:00:00')""")
c.commit(); c.close()
PY
```

Then point the app at the probe DB, run the migrations, and confirm:
- molecule 1 → job types 1,2,3,4; molecule 9 → 2,4; **molecule 6 → still one row, `JobTypeId` NULL**
- run the SQL twice more: byte-identical
- `PRAGMA foreign_key_check` clean

- [ ] **Step 12: Update the page, view model and view minimally**

`Pages/Calendar/Shifts.cshtml.cs:350` →

```csharp
CalendarData.DayNotes = await _dayNoteService.GetDayNotesForCalendarAsync(
    new CalendarScope(MoleculeId.Value, JobTypeId, Tab), StartDate, EndDate);
CalendarData.CanWriteNote = CanWriteNote;
CalendarData.DayNotesScopeIsAllTab = !Tab.HasValue;
```

`Tab` is already the **effective** tab after `:278-298` (null = All), so no extra resolution is needed.

`ViewComponents/ExcelCalendarTableViewComponent.cs:77` →

```csharp
public Dictionary<DateOnly, List<DayNoteView>> DayNotes { get; set; } = new();
public bool CanWriteNote { get; set; }         // WriteOverviewNotes OR CanEdit — gates ADDING
public bool DayNotesScopeIsAllTab { get; set; } // true => render tab badges (R5)
public int CurrentUserId { get; set; }          // needed for R8's per-note ownership check
```

`Default.cshtml:81-106` → a plain `@foreach` over the list, keeping the existing chip markup and `title` attribution per note, with `data-note-id` on each `×`. Drop the `Calendar_DayNote_LastEditedBy` branch. **Do not delete that resx key** — `GetOverrideValueAsync` resolves by key, so removing it orphans `CompanyLocalization` rows.

The per-note `×` condition, which must match the server's 403 exactly:

```csharp
// R8: your own note, or assign-tier on this calendar. IsReadOnly is already !CanEdit — i.e. the
// scoped AssignShifts grant — so it IS the assign-tier signal; no new property is needed.
var canDeleteThisNote = Model.CanWriteNote
    && (note.CreatedByUserId == Model.CurrentUserId || !Model.IsReadOnly);
```

> The gate changes from a bare `!Model.IsReadOnly`. `IsReadOnly = !CanEdit` is the `AssignShifts` grant, but *adding* a note only needs `CanWriteNote` — so today a notes-only writer can create a note whose `×` never renders for them, while the server would still let them delete anyone's note by direct POST.

`QuickAddDayNote.cshtml.cs` — compile-only change here: call `AddDayNoteAsync` with a `CalendarScope`. Its validation and the removal of the empty-text branch belong to Task 2.

- [ ] **Step 13: Build, run the full suite, and browser-check**

```powershell
# app must NOT be running — a locked exe yields a stale binary that reports false success
Get-Process -Name ShiftManager -ErrorAction SilentlyContinue
```

Run: `dotnet build` then `dotnet test -- xUnit.ParallelizeTestCollections=false`
Expected: green, and the **count** = Task 0 baseline − 3 (deleted tests) + 11 (new service) + 5 (fan-out). Compute the expected number and check it.

Then start the app (`ASPNETCORE_ENVIRONMENT=Development`, `--urls http://127.0.0.1:5123`), log in as `owner2@test` / `Test1234!`, open `/Calendar/Shifts?MoleculeId=1&JobTypeId=1`, add two notes to one day via Quick Entry, and confirm both render. Stop the app afterwards and verify the port is free.

- [ ] **Step 14: Commit**

```bash
git add Models/CalendarDayNote.cs Data/AppDbContext.cs Services/ICalendarDayNoteService.cs \
        Services/CalendarDayNoteService.cs Pages/Calendar/Shifts.cshtml.cs \
        ViewComponents/ExcelCalendarTableViewComponent.cs \
        Pages/Shared/Components/ExcelCalendarTable/Default.cshtml \
        Pages/Api/Calendar/QuickAddDayNote.cshtml.cs \
        Migrations/ ShiftManager.Tests/
git commit -m "feat(calendar): key day notes to the calendar triple, many per day

Re-keys CalendarDayNote from (MoleculeId, Date) with one note per day to
(MoleculeId, JobTypeId, TabId) with unlimited notes per day. The All view
unions every note of the (molecule, jobType) calendar; a real tab shows only
its own.

The service rewrite ships with the migration deliberately: dropping the unique
index while the old ToDictionary(n => n.Date) is live throws ArgumentException
and 500s /Calendar/Shifts for every viewer of an affected molecule.

The fan-out excludes Tech molecules. JobTypeId IS NULL is both the unkeyed
marker and the permanent legitimate Tech value, so fanning those out makes
their notes unreachable with no error — reproduced against real data on
molecule 6."
```

---

## Task 2: Endpoints — validated add, and delete by id

**Files:**
- Modify: `Pages/Api/Calendar/QuickAddDayNote.cshtml.cs`, `Pages/Calendar/Shifts.cshtml:546-552`, `wwwroot/js/calendar-inline-edit.js:717-772,826-833`
- Create: `Pages/Api/Calendar/DeleteDayNote.cshtml`, `Pages/Api/Calendar/DeleteDayNote.cshtml.cs`
- Test: `ShiftManager.Tests/UnitTests/Pages/QuickAddDayNoteTests.cs`, `ShiftManager.Tests/UnitTests/Pages/DeleteDayNoteTests.cs`

Add and delete ship together: removing the empty-text-deletes branch breaks `deleteDayNote(date)` until the new endpoint and the JS exist.

**Interfaces:**
- Consumes: `CalendarScope`, `AddDayNoteAsync`, `GetByIdAsync`, `DeleteDayNoteAsync` (Task 1)
- Produces: `POST /Api/Calendar/DeleteDayNote` with body `{ noteId: int }`; `window.deleteDayNote(noteId)`; `CalendarPageConfig.jobTypeId`

- [ ] **Step 1: Write the failing endpoint tests**

Extend `QuickAddDayNoteTests` — keep the 5 surviving tests (`RequestWithoutAMolecule_IsRejected`, `MoleculeTheCallerCannotView_IsForbidden_AndNothingIsWritten`, `CallersOwnMolecule_IsWritable_WithoutAnyViewShiftsGrant`, `AnotherMoleculeReachedByTheGrant_IsWritable_AndRecordsTheWritersDesk`, `WithoutNotePermission_IsForbidden`), **replace** `EmptyText_DeletesTheMoleculesNote` (old signature *and* removed premise), and add:

```csharp
[Fact] public async Task JobTypeFromAnotherArea_IsForbidden()                  // 403
[Fact] public async Task TabBelongingToADifferentMoleculeOrJobType_IsForbidden() // 403
[Fact] public async Task NonTechMoleculeWithNoJobType_IsRejected()             // 400, spec §3.5
[Fact] public async Task EmptyText_IsRejected_RatherThanDeleting()             // 400
[Fact] public async Task SecondIdenticalSubmitWithinTheWindow_IsNotDuplicated() // double-submit guard
```

New `DeleteDayNoteTests` — the gate ladder, each rung independently:

```csharp
[Fact] public async Task WithoutNotePermission_IsForbidden()                    // 403
[Fact] public async Task UnknownNoteId_IsNotFound()                             // 404
[Fact] public async Task LegacyNoteWithNullMolecule_IsNotFound_AndNoGrantIsConsulted()
[Fact] public async Task NoteOnAMoleculeTheCallerCannotView_IsForbidden()       // 403
[Fact] public async Task Author_CanDeleteTheirOwnNote_WithoutAssignShifts()     // 200
[Fact] public async Task NonAuthorWithoutAssignShifts_IsForbidden()             // 403
[Fact] public async Task NonAuthorWithAssignShiftsOnThatCalendar_CanDelete()    // 200
[Fact] public async Task NoteWithANullAuthor_CannotBeDeletedByANonAssigner()    // 403 (A1 interaction)
[Fact] public async Task DeletingAnAlreadyDeletedNote_IsNotFound()              // 404, double-click
```

`LegacyNoteWithNullMolecule_IsNotFound_AndNoGrantIsConsulted` is the security test. Assert with a strict mock that `HasGrantWithScopeAsync` is **never** called:

```csharp
grantService.Verify(g => g.HasGrantWithScopeAsync(
    It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<int?>(),
    It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<int?>()), Times.Never);
```

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~QuickAddDayNoteTests|FullyQualifiedName~DeleteDayNoteTests" -- xUnit.ParallelizeTestCollections=false`
Expected: FAIL / compile error — `DeleteDayNoteModel` does not exist.

- [ ] **Step 3: Update the add endpoint**

`QuickAddDayNote.cshtml.cs` — `DayNoteRequest` gains `public int? JobTypeId { get; set; }` and `public int? TabId { get; set; }`. **Delete the `text.Length == 0` branch entirely** (old `:136-148`) and 400 on empty text instead. That branch also logged `entityId: 0`, so today every day-note deletion is unattributable in the audit log; the new endpoint logs the real id.

Insert after the existing molecule check, in this order. `Forbid403(...)` / `BadRequest400(...)`
below are shorthand — write them in this file's existing style, i.e.
`return new JsonResult(new { success = false, message = "…" }) { StatusCode = 403 };` preceded by a
`_logger.LogWarning("SECURITY: …")` line, matching `QuickAddDayNote.cshtml.cs:102-107,124-130`:

```csharp
// SECURITY: the request names a job type, so prove it is one THIS molecule's calendar can show.
// GetJobTypesForMoleculeAsync pins jt.AreaId == molecule.AreaId and (jt.MoleculeId == null ||
// == moleculeId), so a job type from another area is unreachable.
var available = await _jobTypeService.GetJobTypesForMoleculeAsync(moleculeId);
if (data.JobTypeId.HasValue && available.All(jt => jt.Id != data.JobTypeId.Value))
    return Forbid403("You do not have access to this calendar");

// A NULL job type is legitimate ONLY for a Tech molecule. For any other molecule it is the
// degenerate "no resolvable job types" state, and writing into it creates a row encoded identically
// to a Tech note. Refuse, so the ambiguous triple never enters the system.
if (!data.JobTypeId.HasValue && molecule.Type != MoleculeType.Tech)
    return BadRequest400("A job type is required for this calendar");

// SECURITY: a tab is only valid on the calendar that owns it. Closes the cross-molecule tab vector.
if (data.TabId.HasValue)
{
    var tabOk = await _db.ShiftTabs.AnyAsync(t =>
        t.Id == data.TabId.Value && t.MoleculeId == moleculeId && t.JobTypeId == data.JobTypeId);
    if (!tabOk) return Forbid403("You do not have access to this calendar");
}
```

Then `AddDayNoteAsync(noteDate, new CalendarScope(moleculeId, data.JobTypeId, data.TabId), companyId, text, currentUserId)`, audit `DayNoteSaved` with `note.Id`, and the double-submit rejection (same scope + date + text + author within a short window).

- [ ] **Step 4: Create the delete endpoint**

New sibling page, following the convention — `QuickAddChore`/`DeleteChore`, `QuickAddOnDuty`/`DeleteOnDuty`, `QuickAddTextEntry`/`DeleteTextEntry` are all separate pairs, never a second handler on the add page. Model it on `DeleteTextEntry.cshtml.cs:69-104`: `[Authorize]`, `[IgnoreAntiforgeryToken]`, each rejection logged with a `SECURITY:`-prefixed warning.

```csharp
// 1. 401 if no NameIdentifier claim
// 2. 403 if !await _grantService.HasCalendarNotePermissionAsync(currentUserId)

var note = await _dayNoteService.GetByIdAsync(data.NoteId);
if (note == null) return NotFound404();

// SECURITY — THIS GUARD IS LOAD-BEARING, NOT TIDINESS.
// MoleculeId and JobTypeId are both nullable. For a legacy un-keyed row the R8 grant call below
// would reach HasGrantWithScopeAsync with ALL-NULL scope params, and GrantService's project, area
// and molecule branches each return true for any holder in their own hierarchy — degenerating R8
// into "any assigner anywhere may delete it". Reject before the grant call, never after.
if (note.MoleculeId == null) return NotFound404();

// Resolve the caller's own molecule exactly as QuickAddDayNote.cshtml.cs:118-124 does — from the
// SWITCHER-AWARE active desk, not the raw CompanyId claim.
// SECURITY-AUDITED: SAFE — reads only the caller's own active desk's MoleculeId.
var companyId = _tenantResolver.GetCurrentTenantId();
if (companyId <= 0)
    return new JsonResult(new { success = false, message = "No active company context" }) { StatusCode = 400 };

var ownMoleculeId = await _db.Companies
    .IgnoreQueryFilters()
    .Where(c => c.Id == companyId)
    .Select(c => c.MoleculeId)
    .FirstOrDefaultAsync();

var viewable = await ShiftCalendarAccess.GetViewableMoleculeIdsAsync(
    _db, _grantService, currentUserId, ownMoleculeId);

// Spelling matters: `note.MoleculeId.HasValue && !viewable.Contains(note.MoleculeId.Value)` leaves
// the hole open. `?? 0` is safe because 0 is never a molecule id.
if (!viewable.Contains(note.MoleculeId ?? 0)) return Forbid403();

// R8: your own note, or assign-tier on THIS calendar. Closes audit finding
// docs/superpowers/audit/cluster-1-calendars.md:644-647, whose other two clauses are satisfied by
// the explicit delete endpoint (no more empty-text-deletes) and by the visible author.
//
// KNOWN, ACCEPTED WIDENING: for a Tech-molecule note JobTypeId is legitimately null, and
// GrantService computes jobTypeMismatch only when BOTH sides have a value (see the SECURITY-AUDITED
// comment at GrantService.cs:213-223, which documents this as deliberate legacy behaviour). So a
// user whose AssignShifts is scoped to one job type can delete Tech-molecule notes. Tech molecules
// have no job types, so this is defensible — but it is a choice, not an accident.
var isAuthor = note.CreatedByUserId == currentUserId;
var canAssign = await _grantService.HasGrantWithScopeAsync(
    currentUserId, "AssignShifts", moleculeId: note.MoleculeId, jobTypeId: note.JobTypeId);
if (!isAuthor && !canAssign) return Forbid403();

await _dayNoteService.DeleteDayNoteAsync(note.Id);
// audit DayNoteDeleted with note.Id — the real id, unlike the old entityId: 0
```

- [ ] **Step 5: Verify endpoint registration**

The `/Api/Calendar` prefix in `Middleware/ApiAuthenticationMiddleware.cs:306` covers the new page, and no `Program.cs` `AllowAnonymousToPage` entry is needed because it is `[Authorize]`. **Check both anyway** — this project has been bitten by `/Api/` endpoints needing registration in both places.

- [ ] **Step 6: Rewire the JS**

`Pages/Calendar/Shifts.cshtml:546-552` — add `jobTypeId: @(Model.JobTypeId?.ToString() ?? "null"),`. Additive and safe: `calendar-tab-prioritization.js:17` reads the object defensively and `calendar-bottom-sheet.js:444,496-498,972-974` reads only the existing keys.

`calendar-inline-edit.js` —
- `quickAddDayNote` sends `jobTypeId` and `tabId` read from `CalendarPageConfig` (`activeTabId`). Because they are read internally, `calendar-quick-entry.js:811,1363-1367` needs **no change**.
- Add an in-flight boolean. `closeInput()` fires only inside the promise's `.then()`, so the Quick Entry input stays open and focused for the whole round-trip and two Enters send two POSTs — harmless under the old upsert, two identical notes under R7.
- Replace `deleteDayNote(date)` with `deleteDayNote(noteId)` posting `{noteId}` to `/Api/Calendar/DeleteDayNote`; **treat 404 as success** so a double-click does not raise an error toast.
- `:826-833` reads `btn.dataset.noteId`.

- [ ] **Step 7: Run the endpoint tests**

Run: `dotnet test --filter "FullyQualifiedName~QuickAddDayNoteTests|FullyQualifiedName~DeleteDayNoteTests" -- xUnit.ParallelizeTestCollections=false`
Expected: PASS.

- [ ] **Step 8: Full suite, then commit**

Run: `dotnet test -- xUnit.ParallelizeTestCollections=false` — green, count increased by the new tests.

```bash
git commit -m "feat(calendar): validated day-note add, and delete by note id

Adds jobTypeId/tabId to the add endpoint with anti-IDOR validation, and moves
delete to a sibling DeleteDayNote endpoint keyed by note id, gated on
author-or-AssignShifts (closes cluster-1-calendars.md:644-647).

The delete endpoint rejects a null MoleculeId BEFORE consulting the grant
service: all-null scope params make HasGrantWithScopeAsync return true for any
assigner in their own hierarchy, which would let anyone delete a legacy note."
```

---

## Task 3: Header UI — chip cap, trigger, panel markup, CSS, localization

**Files:**
- Modify: `Pages/Shared/Components/ExcelCalendarTable/Default.cshtml`, `wwwroot/css/calendar.css`, `Resources/SharedResources.resx`, `Resources/SharedResources.he-IL.resx`
- Test: `ShiftManager.Tests/UnitTests/Css/` (existing guards), resx parity

**Interfaces:**
- Consumes: `DayNoteView`, `CanWriteNote`, `DayNotesScopeIsAllTab` (Task 1)
- Produces: DOM contract for Task 4 — `.excel-calendar__day-notes[data-date]`, `.excel-calendar__day-notes-trigger[data-date]`, `template.excel-calendar__day-notes-template[data-date]`, `.cal-day-notes` panel shell with `.cal-day-notes__item[data-note-id]`

- [ ] **Step 1: Add the nine resx keys**

Grep both files first (`grep -c 'name="KEY"'` must return 0 for each), then add all nine from spec §7.9 to **both** files, and extend `Tabs_DeleteConfirm` with a `{4}` clause in both. Reuse `QuickEntry_MoreItems` = `"+{0} more..."` for the trigger — do not mint a near-duplicate. Leave `Calendar_DayNote_LastEditedBy` in place.

- [ ] **Step 2: Run the localization parity tests**

Run: `dotnet test --filter FullyQualifiedName~Localization -- xUnit.ParallelizeTestCollections=false`
Expected: PASS. A failure here almost always means a duplicate key.

- [ ] **Step 3: Compute the chip cap server-side and render**

In `Default.cshtml`'s `@{ }` block, derive the cap from `columnPlan` — CSS cannot read a `<col>` width, `@container` does not apply to internal table elements, and `contain` on the calendar chain is forbidden (`calendar.css:2944-2947`). The constants already exist (`CalendarColumnPlanner.DefaultDayWidth = 100`, `DefaultCompactDayWidth = 60`, `MinWidth = 40`, `MaxWidth = 600`), and all day columns share the key `"day"`.

| Resolved width | Chips | Trigger |
|---|---|---|
| `< 90` | 0 | `📝` + count |
| `< 140` | 1 | `+N` |
| `>= 140` | 2 | `+N` |

Then replace `:81-106` with the chip stack, the trigger, and an inert `<template>` per day carrying the full list (Razor-localized, cloned by JS — `<template>` content is not rendered, not focusable and not in the accessibility tree).

**This DOM contract is what Task 4 binds to. Keep these exact class and attribute names:**

```razor
@if (Model.DayNotes.TryGetValue(day, out var dayNotes) && dayNotes.Count > 0)
{
    var noteDate = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    var shown = dayNotes.Take(inlineNoteCap).ToList();
    var hiddenCount = dayNotes.Count - shown.Count;

    <div class="excel-calendar__day-notes" data-date="@noteDate">
        @foreach (var note in shown)
        {
            @* aria-hidden: a truncated PREVIEW. A <th> is re-announced on every grid cell the user
               traverses, so note prose here would make the calendar unusable with a screen reader.
               The full copy lives in the panel, which is the keyboard/AT path. *@
            <span class="excel-calendar__day-note" title="@BuildTooltip(note)" aria-hidden="true">
                <span class="excel-calendar__day-note-icon">📝</span>
                @if (Model.DayNotesScopeIsAllTab && note.TabId.HasValue)
                {
                    <span class="excel-calendar__day-note-tab-dot"
                          style="--day-note-tab-color: @TabColor(note)"></span>
                }
                <bdi class="excel-calendar__day-note-text">@note.Text</bdi>
                @if (CanDelete(note))
                {
                    <button type="button" class="excel-calendar__day-note-delete"
                            data-note-id="@note.Id" tabindex="-1"
                            aria-label="@DeleteLabel(note)" title="@DeleteLabel(note)">×</button>
                }
            </span>
        }

        @* Rendered whenever the day has >= 1 note, NOT only when notes are hidden: under the literal
           "+N only" rule a 1- or 2-note day has no focusable element at all, so its text would be
           unreachable by keyboard and screen reader (title is mouse-only, the chip is not focusable). *@
        <button type="button" class="excel-calendar__day-notes-trigger"
                data-date="@noteDate"
                aria-haspopup="dialog" aria-expanded="false"
                aria-label="@TriggerLabel(day, dayNotes.Count)" title="@TriggerLabel(day, dayNotes.Count)">
            @(hiddenCount > 0
                ? string.Format(culture, Localizer["QuickEntry_MoreItems"].Value, hiddenCount)
                : $"📝 {dayNotes.Count}")
        </button>

        @* Inert source of truth for the panel. Not rendered, not focusable, not in the a11y tree. *@
        <template class="excel-calendar__day-notes-template" data-date="@noteDate">
            @foreach (var note in dayNotes)
            {
                <li class="cal-day-notes__item" data-note-id="@note.Id">
                    @if (Model.DayNotesScopeIsAllTab && note.TabId.HasValue)
                    {
                        <span class="visually-hidden">@TabAriaLabel(note)</span>
                        <bdi class="cal-day-notes__tab" aria-hidden="true">@TabDisplayName(note)@(note.TabIsActive ? "" : " · " + Localizer["Calendar_DayNote_TabInactive"].Value)</bdi>
                    }
                    <bdi class="cal-day-notes__text">@note.Text</bdi>
                    <span class="cal-day-notes__meta">@string.Format(culture, Localizer["Calendar_DayNote_WrittenBy"].Value, AuthorDisplay(note))</span>
                    @if (CanDelete(note))
                    {
                        <button type="button" class="cal-day-notes__delete" data-note-id="@note.Id"
                                aria-label="@DeleteLabel(note)">×</button>
                    }
                </li>
            }
        </template>
    </div>
}
```

Local helpers in the `@{ }` block: `AuthorDisplay(note)` localizes `AuthorName` when it equals the
sentinel key `"Calendar_DayNote_UnknownAuthor"` and otherwise returns it verbatim;
`TabDisplayName(note)` picks `TabNameHe` or `TabNameEn` by culture the way `Shifts.cshtml:320` does
(`isHe ? NameHe : NameEn`); `CanDelete(note)` is the `canDeleteThisNote` expression from Task 1
Step 12; `BuildTooltip(note)` is text + `\n` + written-by (+ `\n` + tab line when badged).

The panel shell is rendered **once per grid**, outside the `<thead>`, as
`<div class="cal-day-notes modal modal--sm" id="calDayNotesPanel" role="dialog" aria-modal="true"
tabindex="-1" aria-labelledby="calDayNotesTitle">` with an `<ul class="cal-day-notes__list">` body
and a sibling `<div class="modal-backdrop" id="calDayNotesBackdrop">`. Task 4 moves both to
`document.body`.

Required details, each with a reason:
- **Trigger renders whenever the day has ≥ 1 note**, not only when `N > 2`. Under the literal rule a 1- or 2-note day has no focusable element, so keyboard and screen-reader users could never reach the text (`title` is mouse-only and the chip is not focusable).
- Chips get `aria-hidden="true"`; the chip `×` gets `tabindex="-1"`. A `<th>` is re-announced on **every** cell the user traverses, so N notes of prose there makes the grid unusable with a screen reader; and a focusable control inside an `aria-hidden` subtree is a violation.
- Trigger: real `<button>`, `aria-haspopup="dialog"`, `aria-expanded`, `aria-label` from `Calendar_DayNotes_TriggerAriaLabel` (date + count, count-agnostic phrasing — there is no `_One`/`_Plural` convention in these files), plus a matching `title`.
- Tab indicator **inline is a colour dot only**, mirroring `.cal-tab__dot` (`Shifts.cshtml:526-534`), with the name in the `title`; the full named badge appears in the panel. At a 100px column a named badge leaves about two glyphs for the note.
- Badge rendered only when `DayNotesScopeIsAllTab` — a real tab's own notes need no badge.
- Inactive tabs get the `Calendar_DayNote_TabInactive` marker.
- `<bdi>` around both the tab name and the note text (the `Shifts.cshtml:330` pattern), badge first in DOM order inside a flex row.
- Per-note `×` gated on `Model.CanWriteNote && (note.CreatedByUserId == currentUserId || canAssign)`.
- Panel shell rendered **once per grid**, not per day, with `.modal .modal--sm` + `.modal-backdrop` + `.is-open` from `components.css:729-830`.

- [ ] **Step 4: Add the CSS**

In `calendar.css`, next to the existing day-note block (`:2852-2922`):
- `.excel-calendar__day-notes` stack with `padding-inline-end: 6px` (the resizer at `:4620-4631` is `inset-inline-end: -4px; width: 8px` and already overlaps the chip's `×`).
- Trigger and badge styling. **Every new text node** needs `color: var(--text) !important` — `.excel-calendar__header th *` sets `color: inherit` at specificity (0,1,2) (`:2792-2806`) and today/weekend headers force `--primary-contrast !important` (`:2812-2827`). That includes the `<bdi>`, the badge, and the `<span>` the `<loc>` tag helper emits (`TagHelpers/LocalizationTagHelper.cs:49` always renders a `span`).
- `unicode-bidi: isolate` on the badge.
- `.cal-day-notes` panel list, **resetting `white-space`** — `.modal__body { white-space: pre-wrap }` (`components.css:817`) would render inter-tag indentation as blank lines in a generated list.
- Logical properties only. No new tokens, so `CalendarStickyTokenTests` stays green by construction.

- [ ] **Step 5: Run the CSS guard tests**

Run: `dotnet test --filter FullyQualifiedName~Css -- xUnit.ParallelizeTestCollections=false`
Expected: PASS. `CalendarStickyRtlSweepTests` fails on any `left:`/`right:` you introduced.

- [ ] **Step 6: Full suite, then commit**

```bash
git commit -m "feat(calendar): day-note chip stack, overflow trigger and panel markup"
```

---

## Task 4: Panel JS — portal, focus, and surviving the grid refresh

**Files:**
- Create: `wwwroot/js/calendar-day-notes-panel.js`
- Modify: `Pages/Calendar/Shifts.cshtml` (script tag)

**Interfaces:**
- Consumes: Task 3's DOM contract; `window.deleteDayNote(noteId)` (Task 2)
- Produces: nothing other tasks depend on

- [ ] **Step 1: Write the module**

**Portal to `document.body` on init.** This is not a style preference — three independent reasons:
1. `.excel-calendar__header` is `position: sticky` **with** `z-index: --z-sticky-header` (1022), which makes `<thead>` a stacking context. Anything inside composites at 1022, *below* `--z-fixed` (1030), `--z-modal` (1050) and `--z-dropdown` (1060), and cannot be raised from inside — it would paint behind the distribution-list dropdown and behind any modal.
2. `position: absolute` inside the `<th>` is clipped by `.excel-calendar { overflow: auto }` (`:2757-2766`).
3. `position: fixed` escapes the clip but then does not follow the column when the grid scrolls.

Do **not** render it as a flow sibling after `.excel-calendar` either: `calendar.css:2181-2182` states *"do NOT add fixed-height elements below `.excel-calendar`; the flex math assumes the calendar is the last child."*

Use `--z-modal` / `--z-modal-backdrop` via the existing `.modal` classes. No new token.

**Refresh survival.** Every add and delete calls `triggerCalendarRefresh()`, and `_doCalendarRefresh()` (`calendar-inline-edit.js:34-96`) does `liveGrid.replaceWith(fresh)` — the entire `.excel-calendar` subtree is destroyed, then `calendar:grid-refreshed` is dispatched with `detail: { grid: fresh }`. On that event:
1. **Re-portal**: detach the previously portaled nodes *before* appending the new ones, or `getElementById` keeps resolving the stale detached node.
2. **Re-render in place, do not dismiss**: remember `openDate`, re-clone from the fresh `<template>`, re-apply `.is-open`. Deleting three notes is one panel open, not three.
3. **Close only when the day reaches zero notes.** Focus return re-finds the trigger **by date**, not by the node captured on open, because that node is detached. When the deleted note was the day's last, the trigger is gone and focus falls to `<body>` — a known limitation.

> Had the panel been rendered in the header instead, a refresh would destroy the open dialog, its backdrop and the focused element in one shot, and because the close path never runs, `document.body.style.overflow = 'hidden'` is never cleared — leaving the page unscrollable with focus on `<body>`.

`calendar-column-resize.js:214-223` already re-binds on this event and deliberately does not re-seed its cache; the same hazard class applies here.

**Accessibility:** `role="dialog" aria-modal="true" aria-labelledby`, `tabindex="-1"`, focus to the close button on open, Escape closes, Tab cycles within (same loop as `feedback-modal.js:233-245`). Per-note delete `aria-label` names the author via `Calendar_DayNote_DeleteAriaLabel`, so the list does not read as "× × × ×".

**Build no user-visible strings in JS.** All chrome is Razor-localized; the body is cloned from the `<template>`. If that ever changes, the string must be added to `Pages/Shared/_LocalizationScript.cshtml`, which is an explicit allowlist — otherwise `window.AppLocalizer.Key` is `undefined` at runtime.

Also add an in-flight guard to the per-note delete, and do **not** trigger browser dialogs — `confirm()` would block the automation used to verify this.

- [ ] **Step 2: Register the script**

Add `<script src="~/js/calendar-day-notes-panel.js" asp-append-version="true"></script>` to `Shifts.cshtml` after `calendar-inline-edit.js` (it depends on `window.deleteDayNote`).

- [ ] **Step 3: Verify the asset is actually served**

CSS/JS are minified at build time with a `?v=` hash and the dev app serves `bin/Debug` — source edits are not served until a rebuild. Rebuild, start the app, then:

```bash
curl -s "http://127.0.0.1:5123/js/calendar-day-notes-panel.js" | head -5
```

Confirm your marker is present **before** browser-testing.

- [ ] **Step 4: Browser-verify, then commit**

With 5 notes on one day: trigger shows `+3`; the panel opens above the grid (not clipped, not behind the toolbar); deleting one note leaves the panel open with 4; deleting the last closes it; Escape closes; Tab cycles; Hebrew renders right-to-left with the badge on the right. Check at week view, month view, and a hand-narrowed 40px day column.

```bash
git commit -m "feat(calendar): body-portaled day-notes panel that survives grid refresh"
```

---

## Task 5: Make `--excel-calendar-header-height` tell the truth

**Files:** Modify `wwwroot/js/calendar-sticky-shadows.js:70-77`

This is **required**, not cosmetic. The property is a hard-coded 44px constant consumed by `calendar.css:3377` (`.excel-calendar__group-header td { top: … }`), `calendar-sticky-shadows.js:70-77` (band-pin `rootMargin`) and `calendar-keyboard-nav.js:18-22`. **Measured live on a rendered grid with ZERO notes: the property reads `44px` while `.excel-calendar__header` is `64px`** — already a 20px lie, which is why pinned group bands overlap the thead. Two chips plus a trigger makes it a visible overlap.

- [ ] **Step 1: Publish the measured height**

`:74` already reads the property off the **calendar element** rather than `:root`, so set it before the read:

```js
// The 44px default in calendar.css is a stale measurement — the real header is ~64px bare and taller
// with day notes, which is why pinned group bands overlapped the thead. Publish the measured value.
calendar.style.setProperty('--excel-calendar-header-height', thead.offsetHeight + 'px');
```

Recompute on `calendar:grid-refreshed` and on resize, since the height now varies with note count.

- [ ] **Step 2: Verify on all five calendars**

Shifts, Overview, Team, Chores, OnCall. In the console: the property equals `thead.offsetHeight`, and a pinned group band's top edge no longer overlaps the header.

- [ ] **Step 3: Full suite, then commit**

```bash
git commit -m "fix(calendar): publish the measured sticky header height

The 44px constant was a stale measurement; the real header is 64px bare, so
pinned group bands already overlapped the thead before day notes made the
header taller."
```

---

## Task 6: A1 + A2 — user deletion and job-type deletion

**Files:**
- Modify: `Pages/Admin/Users.cshtml.cs`, `Pages/Admin/Organization/JobTypes/Index.cshtml.cs:275-291`, `Services/JobTypeService.cs:290-306`
- Test: new cases in the existing admin test files

**Interfaces:**
- Consumes: the `SetNull` author FK from Task 1
- Produces: nothing other tasks depend on

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public async Task DeletingAUserWhoWroteADayNote_Succeeds_AndLeavesTheNoteWithANullAuthor()
[Fact] public async Task DeletingAJobTypeThatHasDayNotes_IsBlocked_WithALocalizedMessage()
[Fact] public async Task DeletingAJobTypeThatHasTabs_IsBlocked_RatherThanThrowing()
```

The third covers a bug that **already exists**: `JobTypes/Index.cshtml.cs:276-291` pre-checks only `Users.CountAsync(u => u.JobTypeId == id)`, `ShiftTab.JobType` is already `Restrict` (`AppDbContext.cs:1490`), and `DeleteJobTypeAsync` (`JobTypeService.cs:290-306`) is a plain `Remove` with no try/catch — so deleting a job type that has tabs throws an unhandled `DbUpdateException` today.

- [ ] **Step 2: Run them to verify they fail**

Expected: the user-delete test fails only if Task 1's `SetNull` is missing; both job-type tests fail (one with an unhandled exception).

- [ ] **Step 3: Implement**

In `OnPostDeleteAsync`, count tabs **and** day notes alongside users and return `Error_CannotDeleteJobTypeWithTabs` / `Error_CannotDeleteJobTypeWithDayNotes`. Add `Users.cshtml.cs` day-note handling as belt-and-braces only — with `SetNull` the FK no longer blocks.

> **Do not reassign authorship** to the deleting admin (the `UserDayNotes` pattern). Under R8 `CreatedByUserId` is a *permission*, so reassignment would silently grant delete rights over a departed colleague's note; and the hover would start naming the admin as the author.

- [ ] **Step 4: Run tests, full suite, commit**

```bash
git commit -m "fix(admin): unblock user deletion and guard job-type deletion

Deleting any user who had written a day note failed with a FOREIGN KEY
constraint error, and was not handled anywhere in Users.cshtml.cs. Deleting a
job type that has calendar tabs threw an unhandled DbUpdateException."
```

---

## Task 7: A3 — tab deletion says what moves

**Files:** Modify `Services/ShiftTabService.cs:105-110`, `Pages/Admin/Organization/Tabs/Index.cshtml.cs:246-261`

- [ ] **Step 1: Write the failing test**

```csharp
[Fact] public async Task GetUsageAsync_ReportsHowManyDayNotesWouldMoveToTheAllView()
```

- [ ] **Step 2: Run it to verify it fails** — the tuple has no such member.

- [ ] **Step 3: Implement**

Widen the return tuple from `(int ShiftTypeCount, int CompanyCount)` to include `DayNoteCount`, and pass it as `{4}` to the already-extended `Tabs_DeleteConfirm`. The `SetNull` FK moves those notes to the All view silently otherwise.

> A company that has overridden `Tabs_DeleteConfirm` keeps its 4-placeholder text; `string.Format` ignores the extra argument, so this cannot throw.

- [ ] **Step 4: Run tests, full suite, commit**

---

## Task 8: A4 — live updates

**Files:** Modify `Hubs/CalendarHub.cs:310-318` and `CalendarNotificationService`, both day-note endpoints, `wwwroot/js/calendar-realtime.js`

**No hub authorization change.** Live-tested: an Owner whose home desk is in molecule System joined `shifts-1-1` successfully (`[DBG] CalendarHub Connection … joined group shifts-1-1`, no `denied access` line), because `ValidateGroupAccessAsync`'s second rung resolves the `DirectorHubAccess` grant that Owners hold at project scope. The hub grant mirrors the view grant at every role tier, so "can see this calendar" and "can join its live group" are already the same answer.

- [ ] **Step 1: Write the failing test**

```csharp
[Fact] public async Task AddingADayNote_BroadcastsToTheShiftsGroupForThatMoleculeAndJobType()
[Fact] public async Task DeletingADayNote_BroadcastsToo()
```

- [ ] **Step 2: Run them to verify they fail.**

- [ ] **Step 3: Implement**

Add `NotifyDayNoteChangedAsync(string groupName, CalendarDayNoteChangedEvent evt)` to the interface (`:310-318`) and the implementation, sending `"DayNoteChanged"` — exactly the shape of the five existing `Notify*` methods. Group name from `CalendarGroups.Shifts(moleculeId, jobTypeId)` (`:375`), which already matches the note's `(molecule, jobType)`. Carry `tabId` and `date` in the payload so a client on a different tab can ignore it. Call it from both endpoints; handle `"DayNoteChanged"` in `calendar-realtime.js` by refreshing.

- [ ] **Step 4: Browser-verify with two sessions, then commit**

Two browsers on the same calendar; a note added in one appears in the other without a manual refresh. Then switch the second browser to a different tab and confirm it correctly ignores a note scoped to the first tab.

---

## Task 9: Final verification

- [ ] **Step 1: Full sequential suite, with the count checked**

Run: `dotnet test -- xUnit.ParallelizeTestCollections=false`
Expected: green, and the total equals Task 0's baseline plus the net new tests. **A PASS from a stale assembly is a known failure mode — verify the count.**

- [ ] **Step 2: Re-verify the backfill against real data**

Repeat Task 1 Step 11 from a fresh copy of `app.db`, now with all migrations applied in sequence. Confirm Tech-molecule notes are untouched and the SQL is idempotent across three runs.

- [ ] **Step 3: Bilingual browser pass**

Both languages (set the `.AspNetCore.Culture` cookie to `c=he-IL|uic=he-IL` — `?culture=` has no effect, the cookie provider is the only source), week and month view, 40px and 600px day columns, All tab and a real tab, with and without `AssignShifts`.

- [ ] **Step 4: Final adversarial review against the FINAL requirements**

Required, not optional. R8 and the separate delete endpoint landed mid-review and **inverted** an earlier recommendation (A1's fix direction), so neither prior review can be treated as closed. Focus on: the all-null scope-param guard, the All-branch query shape, the fan-out's Tech exclusion, and the `×` visibility rule matching the server's 403.

- [ ] **Step 5: Regenerate the publish artifact**

```powershell
./scripts/Update-FinalProductPublish.ps1 -Version <next>
```

`FinalProductPublish/` is tracked (539 files) and holds byte-identical copies of `calendar-inline-edit.js`, `calendar-quick-entry.js`, `calendar.css` and `AppDbContext.cs`. Never hand-edit it. Requires a clean tree.

- [ ] **Step 6: Tick the audit finding**

Mark `docs/superpowers/audit/cluster-1-calendars.md:644-647` complete — all three clauses of its proposed fix are satisfied (explicit delete rather than empty-text, visible author, assign-tier gate).
