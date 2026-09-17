using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Migrations;
using ShiftManager.Models;
using ShiftManager.Tests.Helpers;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Migrations;

/// <summary>
/// Runs <see cref="CalendarDayNoteMoleculeBackfillSql.Forward"/> against real SQLite.
///
/// Day notes move from one-per-(desk, day) to one-per-(molecule, day). Two desks in the same
/// molecule may each already hold a note for the same day, and the new filtered unique index on
/// (MoleculeId, Date) allows only one of them to be keyed. The backfill therefore:
///   • keys the most recently written note of each (molecule, day) — COALESCE(UpdatedAt, CreatedAt),
///     newest first, highest Id breaking ties;
///   • leaves every other note with MoleculeId = NULL — invisible on calendars, but NOT DELETED, so
///     no text a user typed is destroyed by the migration;
///   • leaves notes from desks with no molecule un-keyed for the same reason;
///   • is idempotent, and never re-keys a day that already has a molecule note.
/// </summary>
public sealed class CalendarDayNoteMoleculeBackfillTests
{
    private static readonly DateOnly D = new(2026, 9, 18);
    private static DateTime T(int hour) => new(2026, 9, 1, hour, 0, 0, DateTimeKind.Utc);

    private static async Task<SqliteDbContextFixture> SeededAsync()
    {
        var f = await SqliteDbContextFixture.CreateAsync();
        var db = f.Db;
        db.Projects.Add(new Project { Id = 1, Name = "P", DisplayName = "P" });
        db.Areas.Add(new Area { Id = 1, ProjectId = 1, Name = "A", DisplayName = "A" });
        db.Molecules.AddRange(
            new Molecule { Id = 1, AreaId = 1, Name = "M1", DisplayName = "M1" },
            new Molecule { Id = 2, AreaId = 1, Name = "M2", DisplayName = "M2" });
        db.Companies.AddRange(
            new Company { Id = 1, Name = "A", Slug = "a", DisplayName = "A", MoleculeId = 1 },
            new Company { Id = 2, Name = "B", Slug = "b", DisplayName = "B", MoleculeId = 1 },
            new Company { Id = 3, Name = "C", Slug = "c", DisplayName = "C", MoleculeId = 2 },
            new Company { Id = 4, Name = "HQ", Slug = "hq", DisplayName = "HQ", MoleculeId = null });
        db.Users.Add(new AppUser { Id = 7, Email = "u@test", DisplayName = "U", CompanyId = 1, IsActive = true });
        await db.SaveChangesAsync();

        // Pre-migration shape: every note un-keyed (MoleculeId = NULL).
        db.CalendarDayNotes.AddRange(
            // Same (molecule 1, D): desk 2's note is newer → it wins.
            new CalendarDayNote { Id = 1, Date = D, CompanyId = 1, Text = "desk A, older", CreatedByUserId = 7, CreatedAt = T(9) },
            new CalendarDayNote { Id = 2, Date = D, CompanyId = 2, Text = "desk B, newer", CreatedByUserId = 7, CreatedAt = T(10) },
            // Molecule 2, no collision.
            new CalendarDayNote { Id = 3, Date = D, CompanyId = 3, Text = "desk C", CreatedByUserId = 7, CreatedAt = T(9) },
            // Desk with no molecule.
            new CalendarDayNote { Id = 4, Date = D, CompanyId = 4, Text = "HQ orphan", CreatedByUserId = 7, CreatedAt = T(9) },
            // Same (molecule 1, D+1): desk A was CREATED earlier but EDITED later → the edit time counts.
            new CalendarDayNote { Id = 5, Date = D.AddDays(1), CompanyId = 1, Text = "created early, edited late", CreatedByUserId = 7, CreatedAt = T(8), UpdatedAt = T(20) },
            new CalendarDayNote { Id = 6, Date = D.AddDays(1), CompanyId = 2, Text = "created mid", CreatedByUserId = 7, CreatedAt = T(12) });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return f;
    }

    private static async Task RunBackfillAsync(AppDbContext db)
    {
        foreach (var sql in CalendarDayNoteMoleculeBackfillSql.Forward)
            await db.Database.ExecuteSqlRawAsync(sql);
    }

    private static async Task<Dictionary<int, CalendarDayNote>> NotesAsync(AppDbContext db) =>
        await db.CalendarDayNotes.IgnoreQueryFilters().AsNoTracking().ToDictionaryAsync(n => n.Id);

    [Fact]
    public async Task KeysTheNewestNotePerMoleculeDay_AndLeavesTheLoserUnkeyed()
    {
        await using var f = await SeededAsync();
        await RunBackfillAsync(f.Db);
        var n = await NotesAsync(f.Db);

        n[2].MoleculeId.Should().Be(1, "desk B's note is the newest for (molecule 1, D)");
        n[1].MoleculeId.Should().BeNull("the older twin cannot share the unique (MoleculeId, Date) slot");
        n[3].MoleculeId.Should().Be(2);
    }

    [Fact]
    public async Task UsesTheLastEditTime_NotJustCreationTime_ToPickTheWinner()
    {
        await using var f = await SeededAsync();
        await RunBackfillAsync(f.Db);
        var n = await NotesAsync(f.Db);

        n[5].MoleculeId.Should().Be(1, "edited at 20:00 beats created at 12:00");
        n[6].MoleculeId.Should().BeNull();
    }

    [Fact]
    public async Task NotesFromADeskWithNoMolecule_StayUnkeyed()
    {
        await using var f = await SeededAsync();
        await RunBackfillAsync(f.Db);

        (await NotesAsync(f.Db))[4].MoleculeId.Should().BeNull();
    }

    [Fact]
    public async Task DeletesNothing_EveryTypedNoteSurvivesWithItsText()
    {
        await using var f = await SeededAsync();
        await RunBackfillAsync(f.Db);
        var n = await NotesAsync(f.Db);

        n.Should().HaveCount(6);
        n[1].Text.Should().Be("desk A, older");
        n[4].Text.Should().Be("HQ orphan");
    }

    [Fact]
    public async Task IsIdempotent()
    {
        await using var f = await SeededAsync();
        await RunBackfillAsync(f.Db);
        var once = (await NotesAsync(f.Db)).ToDictionary(kv => kv.Key, kv => kv.Value.MoleculeId);

        await RunBackfillAsync(f.Db);
        var twice = (await NotesAsync(f.Db)).ToDictionary(kv => kv.Key, kv => kv.Value.MoleculeId);

        twice.Should().BeEquivalentTo(once);
    }

    [Fact]
    public async Task NeverReKeysADayThatAlreadyHasAMoleculeNote()
    {
        // A note written through the new molecule-keyed endpoint already occupies (molecule 1, D+2).
        // A newer legacy note for that day must not be keyed on top of it — that would violate the
        // unique index and abort the migration.
        await using var f = await SeededAsync();
        f.Db.CalendarDayNotes.AddRange(
            new CalendarDayNote { Id = 7, Date = D.AddDays(2), CompanyId = 1, MoleculeId = 1, Text = "already keyed", CreatedByUserId = 7, CreatedAt = T(9) },
            new CalendarDayNote { Id = 8, Date = D.AddDays(2), CompanyId = 2, MoleculeId = null, Text = "newer legacy", CreatedByUserId = 7, CreatedAt = T(23) });
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();

        await RunBackfillAsync(f.Db);
        var n = await NotesAsync(f.Db);

        n[7].MoleculeId.Should().Be(1);
        n[8].MoleculeId.Should().BeNull();
    }
}
