using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Migrations;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Migrations;

/// <summary>
/// Regression guard for the BackfillChoreFoundation migration — runs the EXACT SQL it ships
/// (ChoreFoundationBackfillSql.Forward) against real SQLite over a seed that exercises:
///   * a molecule WITH chore types (gets a General category) and one WITHOUT (none created),
///   * a timed chore (weight from times), a differently-timed chore, an untimed chore (keeps 480),
///   * active Standard / active Mil / active GroupUser / inactive Standard users.
/// </summary>
public sealed class ChoreFoundationBackfillTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private AppDbContext _db = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        _db = new AppDbContext(options);
        await _db.Database.EnsureCreatedAsync();

        // Phase 1 — hierarchy + users (FK-safe ordering: parents before children).
        _db.Projects.Add(new Project { Id = 1, Name = "P", DisplayName = "P" });
        _db.Areas.Add(new Area { Id = 1, ProjectId = 1, Name = "A", DisplayName = "A" });
        _db.Molecules.AddRange(
            new Molecule { Id = 1, AreaId = 1, Name = "M1", DisplayName = "M1" },
            new Molecule { Id = 2, AreaId = 1, Name = "M2", DisplayName = "M2" });
        _db.Companies.Add(new Company { Id = 1, Name = "Co", Slug = "co", MoleculeId = 1 });
        _db.Users.AddRange(
            new AppUser { Id = 1, CompanyId = 1, Email = "s@x.mil", DisplayName = "Std",    IsActive = true,  AccountType = AccountType.Standard },
            new AppUser { Id = 2, CompanyId = 1, Email = "m@x.mil", DisplayName = "Mil",    IsActive = true,  AccountType = AccountType.Mil },
            new AppUser { Id = 3, CompanyId = 1, Email = "g@x.mil", DisplayName = "Grp",    IsActive = true,  AccountType = AccountType.GroupUser },
            new AppUser { Id = 4, CompanyId = 1, Email = "i@x.mil", DisplayName = "StdOff", IsActive = false, AccountType = AccountType.Standard });
        await _db.SaveChangesAsync();

        // Phase 2 — two chore types in molecule 1, both uncategorized; molecule 2 has none.
        _db.ChoreTypes.AddRange(
            new ChoreType { Id = 100, MoleculeId = 1, Name = "Kitchen", DisplayName = "Kitchen", CreatedByUserId = 1 },
            new ChoreType { Id = 101, MoleculeId = 1, Name = "Guard",   DisplayName = "Guard",   CreatedByUserId = 1 });
        await _db.SaveChangesAsync();

        // Phase 3 — chores: timed 10:00-14:00 (=240), timed 08:00-12:30 (=270), untimed (keeps 480).
        // All seeded at the 480 production default; backfill recomputes the timed ones.
        _db.Chores.AddRange(
            new Chore { Id = 500, CompanyId = 1, MoleculeId = 1, UserId = 1, Date = new DateOnly(2026, 6, 20),
                        Title = "T1", StartTime = new TimeOnly(10, 0), EndTime = new TimeOnly(14, 0),
                        CreatedBy = 1, CreatedAt = DateTime.UtcNow, WeightMinutes = 480 },
            new Chore { Id = 501, CompanyId = 1, MoleculeId = 1, UserId = 1, Date = new DateOnly(2026, 6, 21),
                        Title = "T2", StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(12, 30),
                        CreatedBy = 1, CreatedAt = DateTime.UtcNow, WeightMinutes = 480 },
            new Chore { Id = 502, CompanyId = 1, MoleculeId = 1, UserId = 1, Date = new DateOnly(2026, 6, 22),
                        Title = "T3", CreatedBy = 1, CreatedAt = DateTime.UtcNow, WeightMinutes = 480 });
        await _db.SaveChangesAsync();

        foreach (var sql in ChoreFoundationBackfillSql.Forward)
            await _db.Database.ExecuteSqlRawAsync(sql);

        _db.ChangeTracker.Clear(); // raw SQL bypasses the tracker; re-read fresh DB state
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task Creates_General_Category_Only_For_Molecules_With_Types()
    {
        var cats = await _db.ChoreCategories.IgnoreQueryFilters().ToListAsync();
        cats.Should().ContainSingle("only molecule 1 has chore types");
        cats[0].MoleculeId.Should().Be(1);
        cats[0].Name.Should().Be("General");
        cats[0].NameHe.Should().Be("כללי");
    }

    [Fact]
    public async Task Assigns_All_Existing_Types_To_General()
    {
        var general = await _db.ChoreCategories.IgnoreQueryFilters().SingleAsync();
        var types = await _db.ChoreTypes.IgnoreQueryFilters().ToListAsync();
        types.Should().OnlyContain(t => t.ChoreCategoryId == general.Id);
    }

    [Fact]
    public async Task Freezes_Weight_From_Times_Else_Keeps_Default()
    {
        var chores = await _db.Chores.IgnoreQueryFilters().ToDictionaryAsync(c => c.Id);
        chores[500].WeightMinutes.Should().Be(240, "10:00-14:00 = 240m");
        chores[501].WeightMinutes.Should().Be(270, "08:00-12:30 = 270m");
        chores[502].WeightMinutes.Should().Be(480, "untimed keeps the 480 default");
    }

    [Fact]
    public async Task Marks_Only_Active_Standard_Users_As_Chore_Participants()
    {
        var users = await _db.Users.IgnoreQueryFilters().ToDictionaryAsync(u => u.Id);
        users[1].DoesChores.Should().BeTrue("active Standard");
        users[2].DoesChores.Should().BeFalse("Mil");
        users[3].DoesChores.Should().BeFalse("GroupUser");
        users[4].DoesChores.Should().BeFalse("inactive");
    }

    [Fact]
    public async Task Backfill_Is_Idempotent()
    {
        foreach (var sql in ChoreFoundationBackfillSql.Forward)
            await _db.Database.ExecuteSqlRawAsync(sql);
        _db.ChangeTracker.Clear();
        (await _db.ChoreCategories.IgnoreQueryFilters().CountAsync())
            .Should().Be(1, "re-running must not create a second General category");
    }
}
