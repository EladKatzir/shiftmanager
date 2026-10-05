using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Migrations;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Tests.Helpers;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Migrations;

/// <summary>
/// Runs <see cref="CalendarDayNoteJobTypeFanOutSql.Forward"/> against real SQLite.
///
/// Day notes move from one-per-(molecule, day) to one-per-CALENDAR. Because the old read ignored job
/// type entirely, a molecule note was visible on EVERY job-type calendar of its molecule — so the
/// backfill copies it to each one, and the original row becomes the copy for the job type the
/// calendar defaults to.
///
/// The case these tests exist for is TECH MOLECULES. For them, JobTypeId IS NULL is the permanent
/// correct value, not an "unkeyed" marker — their calendar never resolves a job type. Fanning those
/// notes out makes every copy unreachable, silently, with no error and no audit row. The dev database
/// holds exactly one day note, in a molecule where that bug is invisible, so only a test like this
/// one catches it.
///
/// Seed mirrors the real deployment's shape: area 1 holds Alhut and Text (workforce-only) plus BR and
/// Hakam (not), so a Workforce molecule resolves all four while a System molecule resolves only two.
/// Area 2 has no job types at all, giving a molecule with nothing to fan out to.
/// </summary>
public sealed class CalendarDayNoteJobTypeFanOutTests
{
    private const int MolWorkforce = 1, MolTech = 6, MolSystem = 9, MolNoJobTypes = 11;
    private const int JtAlhut = 1, JtBr = 2, JtText = 3, JtHakam = 4;
    private const int Author = 7;

    private static readonly DateOnly D = new(2026, 9, 18);
    private static DateTime T(int hour) => new(2026, 9, 1, hour, 0, 0, DateTimeKind.Utc);

    private static async Task<SqliteDbContextFixture> SeededAsync()
    {
        var f = await SqliteDbContextFixture.CreateAsync();
        var db = f.Db;
        db.Projects.Add(new Project { Id = 1, Name = "P", DisplayName = "P" });
        db.Areas.AddRange(
            new Area { Id = 1, ProjectId = 1, Name = "A1", DisplayName = "A1" },
            new Area { Id = 2, ProjectId = 1, Name = "A2", DisplayName = "A2" });
        db.Molecules.AddRange(
            new Molecule { Id = MolWorkforce, AreaId = 1, Name = "W", DisplayName = "W", Type = MoleculeType.Workforce },
            new Molecule { Id = MolTech, AreaId = 1, Name = "T", DisplayName = "T", Type = MoleculeType.Tech },
            new Molecule { Id = MolSystem, AreaId = 1, Name = "S", DisplayName = "S", Type = MoleculeType.System },
            new Molecule { Id = MolNoJobTypes, AreaId = 2, Name = "N", DisplayName = "N", Type = MoleculeType.Workforce });
        db.Companies.AddRange(
            new Company { Id = 1, Name = "cw", Slug = "cw", DisplayName = "cw", MoleculeId = MolWorkforce },
            new Company { Id = 2, Name = "ct", Slug = "ct", DisplayName = "ct", MoleculeId = MolTech },
            new Company { Id = 3, Name = "cs", Slug = "cs", DisplayName = "cs", MoleculeId = MolSystem },
            new Company { Id = 4, Name = "cn", Slug = "cn", DisplayName = "cn", MoleculeId = MolNoJobTypes });
        db.Users.Add(new AppUser { Id = Author, Email = "u@test", DisplayName = "U", CompanyId = 1, IsActive = true });
        // Area 1's job types. SortOrder decides the anchor; IsWorkforceOnly decides which molecules
        // can resolve them at all.
        db.JobTypes.AddRange(
            new JobType { Id = JtAlhut, AreaId = 1, Name = "Alhut", DisplayName = "Alhut", IsActive = true, SortOrder = 1, IsWorkforceOnly = true },
            new JobType { Id = JtBr, AreaId = 1, Name = "BR", DisplayName = "BR", IsActive = true, SortOrder = 2, IsWorkforceOnly = false },
            new JobType { Id = JtText, AreaId = 1, Name = "Text", DisplayName = "Text", IsActive = true, SortOrder = 3, IsWorkforceOnly = true },
            new JobType { Id = JtHakam, AreaId = 1, Name = "Hakam", DisplayName = "Hakam", IsActive = true, SortOrder = 4, IsWorkforceOnly = false });
        await db.SaveChangesAsync();

        // Pre-migration shape: molecule-keyed, no job type, no tab.
        db.CalendarDayNotes.AddRange(
            new CalendarDayNote { Id = 1, Date = D, CompanyId = 1, MoleculeId = MolWorkforce, Text = "workforce", CreatedByUserId = Author, CreatedAt = T(9) },
            new CalendarDayNote { Id = 2, Date = D, CompanyId = 3, MoleculeId = MolSystem, Text = "system", CreatedByUserId = Author, CreatedAt = T(9) },
            new CalendarDayNote { Id = 3, Date = D, CompanyId = 2, MoleculeId = MolTech, Text = "tech", CreatedByUserId = Author, CreatedAt = T(9) },
            new CalendarDayNote { Id = 4, Date = D, CompanyId = 4, MoleculeId = MolNoJobTypes, Text = "no job types", CreatedByUserId = Author, CreatedAt = T(9) },
            new CalendarDayNote { Id = 5, Date = D, CompanyId = 1, MoleculeId = null, Text = "legacy unkeyed", CreatedByUserId = Author, CreatedAt = T(9) });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return f;
    }

    private static async Task RunFanOutAsync(AppDbContext db)
    {
        foreach (var sql in CalendarDayNoteJobTypeFanOutSql.Forward)
            await db.Database.ExecuteSqlRawAsync(sql);
    }

    private static async Task<List<CalendarDayNote>> NotesAsync(AppDbContext db) =>
        await db.CalendarDayNotes.IgnoreQueryFilters().AsNoTracking().OrderBy(n => n.Id).ToListAsync();

    /// <summary>A comparable shape for idempotency: every row's full identity.</summary>
    private static async Task<List<string>> SnapshotAsync(AppDbContext db) =>
        (await NotesAsync(db))
            .Select(n => $"{n.MoleculeId}|{n.JobTypeId}|{n.TabId}|{n.Date:yyyy-MM-dd}|{n.Text}|{n.CompanyId}|{n.CreatedByUserId}")
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

    [Fact]
    public async Task TechMoleculeNotes_AreLeftCompletelyUntouched()
    {
        await using var f = await SeededAsync();
        await RunFanOutAsync(f.Db);

        // A Tech calendar only ever asks for JobTypeId = NULL, so any fanned-out copy would be
        // permanently unreachable. Reproduced against the real database on molecule 6 (Shikma), where
        // one note became four invisible ones.
        var tech = (await NotesAsync(f.Db)).Where(n => n.MoleculeId == MolTech).ToList();
        tech.Should().HaveCount(1, "a Tech molecule's note must not be duplicated");
        tech.Single().JobTypeId.Should().BeNull("NULL is the correct permanent value for a Tech calendar");
        tech.Single().Id.Should().Be(3);
        tech.Single().Text.Should().Be("tech");
    }

    [Fact]
    public async Task WorkforceNote_IsFannedOutToEveryResolvableJobType_WithTheAnchorKeepingTheOriginalId()
    {
        await using var f = await SeededAsync();
        await RunFanOutAsync(f.Db);

        var rows = (await NotesAsync(f.Db)).Where(n => n.MoleculeId == MolWorkforce).ToList();
        rows.Select(r => r.JobTypeId).Should().BeEquivalentTo(
            new int?[] { JtAlhut, JtBr, JtText, JtHakam },
            "a Workforce molecule resolves all four of its area's job types");

        // The anchor keeps the original Id and takes the job type the calendar DEFAULTS to
        // (ORDER BY SortOrder, Name), so the calendar opens on the row the user already knows rather
        // than on a copy.
        rows.Single(r => r.Id == 1).JobTypeId.Should().Be(JtAlhut);
        rows.Should().AllSatisfy(r => r.TabId.Should().BeNull("copies land on the All view"));
        rows.Should().AllSatisfy(r => r.Text.Should().Be("workforce"));
        rows.Should().AllSatisfy(r => r.CompanyId.Should().Be(1, "provenance is carried forward"));
        rows.Should().AllSatisfy(r => r.CreatedByUserId.Should().Be(Author));
    }

    [Fact]
    public async Task SystemMolecule_OnlyResolvesJobTypesThatAreNotWorkforceOnly()
    {
        await using var f = await SeededAsync();
        await RunFanOutAsync(f.Db);

        var rows = (await NotesAsync(f.Db)).Where(n => n.MoleculeId == MolSystem).ToList();
        rows.Select(r => r.JobTypeId).Should().BeEquivalentTo(
            new int?[] { JtBr, JtHakam },
            "Alhut and Text are IsWorkforceOnly and a System molecule is not a workforce molecule");
        rows.Single(r => r.Id == 2).JobTypeId.Should().Be(JtBr, "BR has the lowest SortOrder of the two");
    }

    [Fact]
    public async Task MoleculeWithNoResolvableJobTypes_KeepsItsNote_WithMoleculeIdPreserved()
    {
        await using var f = await SeededAsync();
        await RunFanOutAsync(f.Db);

        var rows = (await NotesAsync(f.Db)).Where(n => n.MoleculeId == MolNoJobTypes).ToList();
        rows.Should().HaveCount(1);
        rows.Single().JobTypeId.Should().BeNull();
        rows.Single().MoleculeId.Should().Be(MolNoJobTypes, "nothing a user typed is destroyed");
    }

    [Fact]
    public async Task LegacyUnkeyedNote_IsNotTouched()
    {
        await using var f = await SeededAsync();
        await RunFanOutAsync(f.Db);

        var legacy = (await NotesAsync(f.Db)).Single(n => n.Id == 5);
        legacy.MoleculeId.Should().BeNull();
        legacy.JobTypeId.Should().BeNull();
    }

    [Fact]
    public async Task FanOut_IsIdempotent_AcrossThreeRuns()
    {
        await using var f = await SeededAsync();

        await RunFanOutAsync(f.Db);
        var afterFirst = await SnapshotAsync(f.Db);

        await RunFanOutAsync(f.Db);
        await RunFanOutAsync(f.Db);

        (await SnapshotAsync(f.Db)).Should().BeEquivalentTo(afterFirst,
            "re-running must not multiply rows — the molecule backfill before this one is run twice " +
            "by design, once by the migration and once by its regression test");
    }

    [Fact]
    public async Task RunningTheUpdateBeforeTheInsert_SilentlyMakesNoCopies()
    {
        await using var f = await SeededAsync();

        // Both statements select on `JobTypeId IS NULL`. Reversed, the UPDATE consumes that marker
        // first, so the INSERT matches nothing — no copies, no error, migration reports success. This
        // test documents why Forward's order is load-bearing rather than stylistic.
        await f.Db.Database.ExecuteSqlRawAsync(CalendarDayNoteJobTypeFanOutSql.KeyOriginalToAnchorJobType);
        await f.Db.Database.ExecuteSqlRawAsync(CalendarDayNoteJobTypeFanOutSql.InsertCopiesPerJobType);

        (await NotesAsync(f.Db)).Where(n => n.MoleculeId == MolWorkforce).Should().HaveCount(1,
            "the wrong order loses three of the four job-type calendars");
    }

    [Fact]
    public void MoleculeTypeEnumLiterals_AreFrozen()
    {
        // The SQL hard-codes `m.Type <> 1` and `m.Type IN (0,2)`. Migration SQL cannot be re-derived
        // from the enum later, so reordering MoleculeType must break here rather than in production.
        ((int)MoleculeType.Workforce).Should().Be(0);
        ((int)MoleculeType.Tech).Should().Be(1);
        ((int)MoleculeType.Helper).Should().Be(2);
        ((int)MoleculeType.System).Should().Be(3);
    }
}
