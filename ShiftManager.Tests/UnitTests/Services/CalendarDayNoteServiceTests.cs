using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Services;
using ShiftManager.Tests.Helpers;

namespace ShiftManager.Tests.UnitTests.Services;

/// <summary>
/// Day notes are keyed to the MOLECULE whose Shifts calendar they annotate.
///
/// They used to be keyed to the writer's active company. The Shifts calendar is deliberately
/// cross-company (its rows span every desk in the molecule), so a note written from desk A was
/// invisible to a colleague from desk B looking at the very same calendar — which read as "day notes
/// only appear for the user who created them". These tests pin the molecule keying, the author
/// attribution the hover tooltip needs, and that legacy un-keyed rows stay out of view.
///
/// Seed: molecule 1 holds desks 1 and 2; molecule 2 holds desk 3.
/// User 7 "Avi" sits in desk 1, user 8 "Bat" in desk 2.
/// </summary>
public class CalendarDayNoteServiceTests
{
    private const int Mol1 = 1, Mol2 = 2;
    private const int DeskA = 1, DeskB = 2, DeskC = 3;
    private const int Avi = 7, Bat = 8;

    private static readonly DateOnly D = new(2026, 9, 18);

    private static async Task<SqliteDbContextFixture> SeededAsync()
    {
        var f = await SqliteDbContextFixture.CreateAsync();
        var db = f.Db;
        db.Projects.Add(new Project { Id = 1, Name = "P", DisplayName = "P" });
        db.Areas.Add(new Area { Id = 1, ProjectId = 1, Name = "A", DisplayName = "A" });
        db.Molecules.AddRange(
            new Molecule { Id = Mol1, AreaId = 1, Name = "M1", DisplayName = "M1" },
            new Molecule { Id = Mol2, AreaId = 1, Name = "M2", DisplayName = "M2" });
        db.Companies.AddRange(
            new Company { Id = DeskA, Name = "A", Slug = "a", DisplayName = "A", MoleculeId = Mol1 },
            new Company { Id = DeskB, Name = "B", Slug = "b", DisplayName = "B", MoleculeId = Mol1 },
            new Company { Id = DeskC, Name = "C", Slug = "c", DisplayName = "C", MoleculeId = Mol2 });
        db.Users.AddRange(
            new AppUser { Id = Avi, Email = "avi@test", DisplayName = "Avi", CompanyId = DeskA, IsActive = true },
            new AppUser { Id = Bat, Email = "bat@test", DisplayName = "Bat", CompanyId = DeskB, IsActive = true });
        await db.SaveChangesAsync();
        return f;
    }

    // --- Visibility: the reported bug ---

    [Fact]
    public async Task NoteWrittenFromOneDesk_IsReturnedForTheWholeMolecule()
    {
        await using var f = await SeededAsync();
        var svc = new CalendarDayNoteService(f.Db);

        await svc.SetDayNoteAsync(D, Mol1, authorCompanyId: DeskA, "Exercise day", userId: Avi);

        // The read takes no company at all — anyone viewing molecule 1's calendar gets the note,
        // whichever desk they belong to.
        var notes = await svc.GetDayNotesForMoleculeAsync(Mol1, D, D);
        notes.Should().ContainKey(D);
        notes[D].Text.Should().Be("Exercise day");
    }

    [Fact]
    public async Task SecondDeskWritingTheSameDay_EditsTheOneMoleculeNote_RatherThanCreatingAHiddenTwin()
    {
        await using var f = await SeededAsync();
        var svc = new CalendarDayNoteService(f.Db);

        var first = await svc.SetDayNoteAsync(D, Mol1, DeskA, "Exercise day", Avi);
        var second = await svc.SetDayNoteAsync(D, Mol1, DeskB, "Exercise moved to 14:00", Bat);

        second.Id.Should().Be(first.Id);
        (await f.Db.CalendarDayNotes.IgnoreQueryFilters().CountAsync()).Should().Be(1);
        (await svc.GetDayNotesForMoleculeAsync(Mol1, D, D))[D].Text.Should().Be("Exercise moved to 14:00");
    }

    [Fact]
    public async Task Notes_AreIsolatedPerMolecule_AndBoundedByTheDateRange()
    {
        await using var f = await SeededAsync();
        var svc = new CalendarDayNoteService(f.Db);

        await svc.SetDayNoteAsync(D, Mol1, DeskA, "Mol1 in range", Avi);
        await svc.SetDayNoteAsync(D.AddDays(30), Mol1, DeskA, "Mol1 out of range", Avi);
        await svc.SetDayNoteAsync(D, Mol2, DeskC, "Mol2 same day", Avi);

        var notes = await svc.GetDayNotesForMoleculeAsync(Mol1, D, D.AddDays(6));

        notes.Should().HaveCount(1);
        notes[D].Text.Should().Be("Mol1 in range");
    }

    [Fact]
    public async Task LegacyNotesWithoutAMolecule_AreNeverReturned()
    {
        // Rows the molecule backfill could not key (their desk had no molecule, or they lost a
        // same-day collision) keep MoleculeId = NULL. They are preserved, not deleted, but must not
        // surface on any calendar.
        await using var f = await SeededAsync();
        f.Db.CalendarDayNotes.Add(new CalendarDayNote
        {
            Date = D, Text = "legacy", CompanyId = DeskA, MoleculeId = null, CreatedByUserId = Avi
        });
        await f.Db.SaveChangesAsync();

        var notes = await new CalendarDayNoteService(f.Db).GetDayNotesForMoleculeAsync(Mol1, D, D);

        notes.Should().BeEmpty();
    }

    // --- Attribution: the hover tooltip ---

    [Fact]
    public async Task ReturnsTheOriginalAuthorsName_AndNoEditor_WhenOnlyTheAuthorWrote()
    {
        await using var f = await SeededAsync();
        var svc = new CalendarDayNoteService(f.Db);

        await svc.SetDayNoteAsync(D, Mol1, DeskA, "Exercise day", Avi);
        await svc.SetDayNoteAsync(D, Mol1, DeskA, "Exercise day (confirmed)", Avi); // author edits own note

        var note = (await svc.GetDayNotesForMoleculeAsync(Mol1, D, D))[D];
        note.AuthorName.Should().Be("Avi");
        note.LastEditorName.Should().BeNull("the author editing their own note is not a second contributor");
    }

    [Fact]
    public async Task EditBySomeoneElse_KeepsTheOriginalAuthor_AndNamesTheEditor()
    {
        // The tooltip must show who ORIGINALLY wrote the note. Silently re-attributing the note to
        // whoever touched it last would be wrong; silently keeping only the original author while
        // showing someone else's words would be misleading. So both are carried.
        await using var f = await SeededAsync();
        var svc = new CalendarDayNoteService(f.Db);

        await svc.SetDayNoteAsync(D, Mol1, DeskA, "Exercise day", Avi);
        var edited = await svc.SetDayNoteAsync(D, Mol1, DeskB, "Exercise moved to 14:00", Bat);

        edited.CreatedByUserId.Should().Be(Avi);
        edited.CompanyId.Should().Be(DeskA, "the note's origin desk does not change on edit");
        edited.UpdatedByUserId.Should().Be(Bat);

        var note = (await svc.GetDayNotesForMoleculeAsync(Mol1, D, D))[D];
        note.AuthorName.Should().Be("Avi");
        note.LastEditorName.Should().Be("Bat");
    }

    // --- Delete ---

    [Fact]
    public async Task Delete_RemovesThatMoleculesNote_AndReturnsTrue()
    {
        await using var f = await SeededAsync();
        var svc = new CalendarDayNoteService(f.Db);
        await svc.SetDayNoteAsync(D, Mol1, DeskA, "Mol1", Avi);
        await svc.SetDayNoteAsync(D, Mol2, DeskC, "Mol2", Avi);

        var removed = await svc.DeleteDayNoteAsync(D, Mol1);

        removed.Should().BeTrue();
        (await svc.GetDayNotesForMoleculeAsync(Mol1, D, D)).Should().BeEmpty();
        (await svc.GetDayNotesForMoleculeAsync(Mol2, D, D)).Should().ContainKey(D, "other molecules are untouched");
    }

    [Fact]
    public async Task Delete_WhenNoNoteExists_ReturnsFalse()
    {
        await using var f = await SeededAsync();

        (await new CalendarDayNoteService(f.Db).DeleteDayNoteAsync(D, Mol1)).Should().BeFalse();
    }
}
