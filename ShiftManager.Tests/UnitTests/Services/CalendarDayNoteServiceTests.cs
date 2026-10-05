using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Services;
using ShiftManager.Tests.Helpers;

namespace ShiftManager.Tests.UnitTests.Services;

/// <summary>
/// A day note is keyed to a CALENDAR — the triple (MoleculeId, JobTypeId, TabId) exactly as
/// Pages/Calendar/Shifts.cshtml.cs resolves it per request — and a day may hold many notes.
///
/// History of this entity's scope, because each move fixed a real invisibility bug:
///   company  → a note written from desk A was invisible to desk B on the very same calendar
///   molecule → a note showed on every job-type calendar of the molecule, which is too wide
///   calendar → where we are now
///
/// The subtle rule these tests exist to pin is the TAB one. `TabId == null` is the synthetic "All"
/// pseudo-tab (ShiftTab has no row for it), and All already shows every ROW in the calendar — so it
/// shows every NOTE too, badged with the tab each belongs to. A real tab shows only its own notes,
/// and a note typed on All does not leak onto a real tab.
///
/// Seed: molecule 1 (Workforce) with desks 1 and 2 and job types Alhut/BR; molecule 6 (Tech), whose
/// calendar legitimately runs with JobTypeId == null. Tabs Geo and Tacti are active on
/// (molecule 1, Alhut); tab "Old" is deactivated.
/// </summary>
public class CalendarDayNoteServiceTests
{
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
            new JobType { Id = JtBr, AreaId = 1, Name = "BR", DisplayName = "ב\"ר", IsActive = true, SortOrder = 2 });
        db.ShiftTabs.AddRange(
            new ShiftTab { Id = TabGeo, MoleculeId = Mol1, JobTypeId = JtAlhut, NameEn = "Geo", NameHe = "גאו", IsActive = true },
            new ShiftTab { Id = TabTacti, MoleculeId = Mol1, JobTypeId = JtAlhut, NameEn = "Tacti", NameHe = "טקטי", IsActive = true },
            new ShiftTab { Id = TabDead, MoleculeId = Mol1, JobTypeId = JtAlhut, NameEn = "Old", NameHe = "ישן", IsActive = false });
        await db.SaveChangesAsync();
        return f;
    }

    /// <summary>The "All" pseudo-tab view of a (molecule, jobType) calendar.</summary>
    private static CalendarScope All(int mol = Mol1, int? jt = JtAlhut) => new(mol, jt, null);

    /// <summary>A real tab's view of the same calendar.</summary>
    private static CalendarScope Tab(int tabId, int mol = Mol1, int? jt = JtAlhut) => new(mol, jt, tabId);

    // --- R3: many notes per day ---

    [Fact]
    public async Task TwoWritersOnTheSameDay_ProduceTwoNotes_NotOneEdited()
    {
        await using var f = await SeededAsync();
        var svc = new CalendarDayNoteService(f.Db);

        await svc.AddDayNoteAsync(D, All(), authorCompanyId: DeskA, "first", userId: Avi);
        await svc.AddDayNoteAsync(D, All(), authorCompanyId: DeskB, "second", userId: Bat);

        // The previous design upserted by (molecule, date), so the second write silently REPLACED
        // the first and the original author lost their note with no warning.
        var notes = await svc.GetDayNotesForCalendarAsync(All(), D, D);
        notes[D].Select(n => n.Text).Should().Equal("first", "second");
        notes[D].Select(n => n.AuthorName).Should().Equal("Avi", "Bat");
    }

    // --- R5: the tab rule ---

    [Fact]
    public async Task ViewingARealTab_ShowsOnlyThatTabsNotes()
    {
        await using var f = await SeededAsync();
        var svc = new CalendarDayNoteService(f.Db);

        await svc.AddDayNoteAsync(D, All(), DeskA, "all-note", Avi);
        await svc.AddDayNoteAsync(D, Tab(TabGeo), DeskA, "geo-note", Avi);
        await svc.AddDayNoteAsync(D, Tab(TabTacti), DeskA, "tacti-note", Avi);

        var geo = await svc.GetDayNotesForCalendarAsync(Tab(TabGeo), D, D);
        geo[D].Select(n => n.Text).Should().Equal("geo-note");
    }

    [Fact]
    public async Task ViewingAll_UnionsEveryNote_AndBadgesTheTabScopedOnes()
    {
        await using var f = await SeededAsync();
        var svc = new CalendarDayNoteService(f.Db);

        await svc.AddDayNoteAsync(D, All(), DeskA, "all-note", Avi);
        await svc.AddDayNoteAsync(D, Tab(TabGeo), DeskA, "geo-note", Avi);

        // This is the test that catches the silent-failure shape: writing the read as a single
        // `n.TabId == scope.TabId` expression translates to `TabId IS NULL` on the All branch, which
        // returns ONLY "all-note" while compiling, translating and looking correct.
        var all = await svc.GetDayNotesForCalendarAsync(All(), D, D);
        all[D].Should().HaveCount(2);

        all[D].Single(n => n.Text == "all-note").TabNameEn.Should().BeNull();

        var geo = all[D].Single(n => n.Text == "geo-note");
        geo.TabId.Should().Be(TabGeo);
        // BOTH names travel: the service has no localizer, so returning one resolved string would
        // give the Hebrew UI English badges.
        geo.TabNameEn.Should().Be("Geo");
        geo.TabNameHe.Should().Be("גאו");
        geo.TabIsActive.Should().BeTrue();
    }

    [Fact]
    public async Task ANoteTypedOnAll_DoesNotAppearOnARealTab()
    {
        await using var f = await SeededAsync();
        var svc = new CalendarDayNoteService(f.Db);

        await svc.AddDayNoteAsync(D, All(), DeskA, "all-note", Avi);

        (await svc.GetDayNotesForCalendarAsync(Tab(TabGeo), D, D)).Should().BeEmpty();
    }

    [Fact]
    public async Task NoteOnADeactivatedTab_IsStillReturnedFromAll_MarkedInactive()
    {
        await using var f = await SeededAsync();
        var svc = new CalendarDayNoteService(f.Db);

        await svc.AddDayNoteAsync(D, Tab(TabDead), DeskA, "old-note", Avi);

        // A deactivated tab drops out of the strip (ShiftTabService excludes IsActive == false), so
        // its notes are reachable only from All. Keep them visible and say why they cannot be
        // navigated to, rather than hiding them and looking like data loss.
        var all = await svc.GetDayNotesForCalendarAsync(All(), D, D);
        all[D].Single().TabIsActive.Should().BeFalse();
        all[D].Single().TabNameEn.Should().Be("Old");
    }

    // --- R1: the rest of the key ---

    [Fact]
    public async Task NotesAreIsolatedPerJobType()
    {
        await using var f = await SeededAsync();
        var svc = new CalendarDayNoteService(f.Db);

        await svc.AddDayNoteAsync(D, All(jt: JtAlhut), DeskA, "alhut", Avi);
        await svc.AddDayNoteAsync(D, All(jt: JtBr), DeskA, "br", Avi);

        (await svc.GetDayNotesForCalendarAsync(All(jt: JtAlhut), D, D))[D].Single().Text.Should().Be("alhut");
        (await svc.GetDayNotesForCalendarAsync(All(jt: JtBr), D, D))[D].Single().Text.Should().Be("br");
    }

    [Fact]
    public async Task TechCalendarNote_IsDistinctFromAJobTypedAllNote()
    {
        await using var f = await SeededAsync();
        var svc = new CalendarDayNoteService(f.Db);

        // A Tech molecule's calendar runs with JobTypeId == null permanently (Shifts.cshtml.cs forces
        // it), so (M, NULL, NULL) is a legitimate calendar identity — NOT an "unkeyed" marker. That
        // distinction is why the job-type fan-out migration must skip Tech molecules.
        await svc.AddDayNoteAsync(D, new CalendarScope(MolTech, null, null), DeskA, "tech", Avi);
        await svc.AddDayNoteAsync(D, All(), DeskA, "workforce", Avi);

        (await svc.GetDayNotesForCalendarAsync(new CalendarScope(MolTech, null, null), D, D))[D]
            .Single().Text.Should().Be("tech");
        (await svc.GetDayNotesForCalendarAsync(All(), D, D))[D]
            .Single().Text.Should().Be("workforce");
    }

    [Fact]
    public async Task NotesAreBoundedByTheDateRange_AndIsolatedPerMolecule()
    {
        await using var f = await SeededAsync();
        var svc = new CalendarDayNoteService(f.Db);

        await svc.AddDayNoteAsync(D, All(), DeskA, "in", Avi);
        await svc.AddDayNoteAsync(D.AddDays(10), All(), DeskA, "out", Avi);
        await svc.AddDayNoteAsync(D, new CalendarScope(MolTech, null, null), DeskA, "other-molecule", Avi);

        var got = await svc.GetDayNotesForCalendarAsync(All(), D, D.AddDays(1));
        got.Should().ContainKey(D);
        got.Should().NotContainKey(D.AddDays(10));
        got[D].Single().Text.Should().Be("in");
    }

    [Fact]
    public async Task LegacyNotesWithoutAMolecule_AreNeverReturned()
    {
        await using var f = await SeededAsync();
        var svc = new CalendarDayNoteService(f.Db);

        // Rows the molecule backfill could not key are preserved, never deleted — but they belong to
        // no calendar, so no calendar shows them.
        f.Db.CalendarDayNotes.Add(new CalendarDayNote
        {
            Date = D, Text = "legacy", CompanyId = DeskA, MoleculeId = null, CreatedByUserId = Avi
        });
        await f.Db.SaveChangesAsync();

        (await svc.GetDayNotesForCalendarAsync(All(), D, D)).Should().BeEmpty();
    }

    // --- R4 / A1: attribution survives a deleted author ---

    [Fact]
    public async Task ReturnsTheAuthorsDisplayName()
    {
        await using var f = await SeededAsync();
        var svc = new CalendarDayNoteService(f.Db);

        await svc.AddDayNoteAsync(D, All(), DeskA, "note", Avi);

        var got = await svc.GetDayNotesForCalendarAsync(All(), D, D);
        got[D].Single().AuthorName.Should().Be("Avi");
        got[D].Single().CreatedByUserId.Should().Be(Avi);
    }

    [Fact]
    public async Task NoteWithANullAuthor_ReportsTheUnknownAuthorKey()
    {
        await using var f = await SeededAsync();
        var svc = new CalendarDayNoteService(f.Db);

        var note = await svc.AddDayNoteAsync(D, All(), DeskA, "orphan", Avi);
        note.CreatedByUserId = null;   // what the SetNull FK does when the author is deleted
        await f.Db.SaveChangesAsync();

        var got = await svc.GetDayNotesForCalendarAsync(All(), D, D);
        // The service has no localizer, so it returns the resx KEY and the view localizes it. That
        // keeps AuthorName non-null, so no view has to branch on a deleted author.
        got[D].Single().AuthorName.Should().Be("Calendar_DayNote_UnknownAuthor");
        got[D].Single().CreatedByUserId.Should().BeNull();
    }

    // --- R7: delete by id ---

    [Fact]
    public async Task Delete_RemovesOnlyThatNote()
    {
        await using var f = await SeededAsync();
        var svc = new CalendarDayNoteService(f.Db);

        var keep = await svc.AddDayNoteAsync(D, All(), DeskA, "keep", Avi);
        var drop = await svc.AddDayNoteAsync(D, All(), DeskB, "drop", Bat);

        (await svc.DeleteDayNoteAsync(drop.Id)).Should().BeTrue();

        var left = await svc.GetDayNotesForCalendarAsync(All(), D, D);
        left[D].Single().Id.Should().Be(keep.Id);
    }

    [Fact]
    public async Task Delete_WhenNoteDoesNotExist_ReturnsFalse()
    {
        await using var f = await SeededAsync();
        var svc = new CalendarDayNoteService(f.Db);

        // The delete endpoint turns this into a 404, and the client treats 404 as success so a
        // double-click does not raise an error toast.
        (await svc.DeleteDayNoteAsync(424242)).Should().BeFalse();
    }

    [Fact]
    public async Task GetById_ReturnsTheNote_SoTheDeleteEndpointCanAuthorizeIt()
    {
        await using var f = await SeededAsync();
        var svc = new CalendarDayNoteService(f.Db);

        var note = await svc.AddDayNoteAsync(D, Tab(TabGeo), DeskA, "note", Avi);

        var loaded = await svc.GetByIdAsync(note.Id);
        loaded.Should().NotBeNull();
        loaded!.MoleculeId.Should().Be(Mol1);
        loaded.JobTypeId.Should().Be(JtAlhut);
        loaded.TabId.Should().Be(TabGeo);
        loaded.CreatedByUserId.Should().Be(Avi);

        (await svc.GetByIdAsync(424242)).Should().BeNull();
    }

    // --- double-submit guard ---

    [Fact]
    public async Task IdenticalNoteFromTheSameAuthorWithinTheWindow_IsNotDuplicated()
    {
        await using var f = await SeededAsync();
        var svc = new CalendarDayNoteService(f.Db);

        // Quick Entry's input stays open and focused for the whole round-trip (closeInput runs in the
        // promise's .then), so two Enters send two POSTs. Under the old upsert that was harmless —
        // the second overwrote the same row. Now it would create a visible twin and could push a day
        // into "+1", which reads as a bug.
        var first = await svc.AddDayNoteAsync(D, All(), DeskA, "same text", Avi);
        var second = await svc.AddDayNoteAsync(D, All(), DeskA, "same text", Avi);

        second.Id.Should().Be(first.Id, "the duplicate submit returns the existing note rather than inserting");
        (await svc.GetDayNotesForCalendarAsync(All(), D, D))[D].Should().HaveCount(1);
    }

    [Fact]
    public async Task SameTextFromADifferentAuthor_IsAllowed()
    {
        await using var f = await SeededAsync();
        var svc = new CalendarDayNoteService(f.Db);

        // Two people independently writing the same short text ("חצי משמרת") is legitimate — the
        // guard is about one person double-submitting, not about de-duplicating content.
        await svc.AddDayNoteAsync(D, All(), DeskA, "same text", Avi);
        await svc.AddDayNoteAsync(D, All(), DeskB, "same text", Bat);

        (await svc.GetDayNotesForCalendarAsync(All(), D, D))[D].Should().HaveCount(2);
    }

    [Fact]
    public async Task SameTextOnADifferentTab_IsAllowed()
    {
        await using var f = await SeededAsync();
        var svc = new CalendarDayNoteService(f.Db);

        await svc.AddDayNoteAsync(D, All(), DeskA, "same text", Avi);
        await svc.AddDayNoteAsync(D, Tab(TabGeo), DeskA, "same text", Avi);

        // Different calendars, so not a double-submit.
        (await svc.GetDayNotesForCalendarAsync(All(), D, D))[D].Should().HaveCount(2);
        (await svc.GetDayNotesForCalendarAsync(Tab(TabGeo), D, D))[D].Should().HaveCount(1);
    }

    [Fact]
    public async Task AnOlderIdenticalNote_DoesNotBlockWritingTheSameTextAgain()
    {
        await using var f = await SeededAsync();
        var svc = new CalendarDayNoteService(f.Db);

        // Deliberately re-stating yesterday's instruction today must work. The guard is a narrow
        // double-click window, not a permanent uniqueness rule.
        f.Db.CalendarDayNotes.Add(new CalendarDayNote
        {
            Date = D, Text = "same text", CompanyId = DeskA, MoleculeId = Mol1, JobTypeId = JtAlhut,
            CreatedByUserId = Avi, CreatedAt = DateTime.UtcNow.AddMinutes(-30)
        });
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();

        await svc.AddDayNoteAsync(D, All(), DeskA, "same text", Avi);

        (await svc.GetDayNotesForCalendarAsync(All(), D, D))[D].Should().HaveCount(2);
    }

    // --- provenance ---

    [Fact]
    public async Task AddRecordsTheWritersDesk_AsProvenanceOnly()
    {
        await using var f = await SeededAsync();
        var svc = new CalendarDayNoteService(f.Db);

        // CompanyId is set explicitly so CompanyIdInterceptor (which only fires at CompanyId == 0)
        // leaves it alone. It records WHERE the note was written, and no longer scopes who sees it.
        var note = await svc.AddDayNoteAsync(D, All(), authorCompanyId: DeskB, "from desk B", userId: Bat);

        note.CompanyId.Should().Be(DeskB);
        (await svc.GetDayNotesForCalendarAsync(All(), D, D))[D].Single().Text.Should().Be("from desk B");
    }
}
