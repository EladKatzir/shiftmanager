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
/// Regression guard for the <c>BackfillShiftCategoriesFromPrimaryShiftType</c> migration. Runs the
/// EXACT SQL the migration ships (<see cref="ShiftCategoryBackfillSql.Forward"/>) against a real
/// SQLite engine with the production schema, over a seed that exercises every branch:
///   * a molecule-scoped primary shift type,
///   * a company-scoped primary shift type (molecule resolved via the company),
///   * a company-scoped type whose company has NO molecule (unresolvable → skipped),
///   * two users sharing one primary type (shared category, two members),
///   * a user with no primary type at all.
/// </summary>
public sealed class ShiftCategoryBackfillTests : IAsyncLifetime
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

        // PrimaryShiftTypeId was DROPPED from the model in the contract phase, but the historical backfill
        // migration still operates on that legacy column. Re-add it via raw SQL so this test exercises the
        // shipped SQL exactly as it ran against a pre-contract database.
        await _db.Database.ExecuteSqlRawAsync("ALTER TABLE Users ADD COLUMN PrimaryShiftTypeId INTEGER NULL;");

        // Hierarchy: Project → Area → Molecule(1). Company(1) is in molecule 1; Company(2) has NO molecule.
        _db.Projects.Add(new Project { Id = 1, Name = "P", DisplayName = "P" });
        _db.Areas.Add(new Area { Id = 1, ProjectId = 1, Name = "A", DisplayName = "A" });
        _db.Molecules.Add(new Molecule { Id = 1, AreaId = 1, Name = "M", DisplayName = "M" });
        _db.Companies.AddRange(
            new Company { Id = 1, Name = "Co1", Slug = "co1", MoleculeId = 1 },
            new Company { Id = 2, Name = "Co2", Slug = "co2", MoleculeId = null }); // unresolvable molecule

        _db.ShiftTypes.AddRange(
            // molecule-scoped → resolves molecule directly
            new ShiftType { Id = 100, Scope = ShiftScope.Molecule, MoleculeId = 1, Key = "HANAVA" },
            // company-scoped → resolves molecule via Company(1).MoleculeId; NameEn drives DisplayName
            new ShiftType { Id = 101, Scope = ShiftScope.Company, CompanyId = 1, Key = "MORNING", NameEn = "Morning" },
            // company-scoped on a company with no molecule → unresolvable, must be skipped
            new ShiftType { Id = 103, Scope = ShiftScope.Company, CompanyId = 2, Key = "NIGHT" });

        _db.Users.AddRange(
            new AppUser { Id = 10, CompanyId = 1, Email = "a@x.mil", DisplayName = "A" },
            new AppUser { Id = 11, CompanyId = 1, Email = "b@x.mil", DisplayName = "B" },
            new AppUser { Id = 12, CompanyId = 1, Email = "c@x.mil", DisplayName = "C" }, // no primary type
            new AppUser { Id = 13, CompanyId = 1, Email = "d@x.mil", DisplayName = "D" }, // shares cat w/ user 10
            new AppUser { Id = 14, CompanyId = 2, Email = "e@x.mil", DisplayName = "E" }); // unresolvable
        await _db.SaveChangesAsync();

        // Set the legacy PrimaryShiftTypeId via raw SQL (the property no longer exists): 10,13→100; 11→101; 14→103.
        await _db.Database.ExecuteSqlRawAsync(
            "UPDATE Users SET PrimaryShiftTypeId = CASE Id WHEN 10 THEN 100 WHEN 13 THEN 100 WHEN 11 THEN 101 WHEN 14 THEN 103 ELSE NULL END;");

        // Run the exact backfill SQL the migration ships.
        foreach (var sql in ShiftCategoryBackfillSql.Forward)
            await _db.Database.ExecuteSqlRawAsync(sql);

        // Raw SQL bypasses the change tracker — clear it so assertions re-read fresh DB state
        // instead of the stale tracked AppUser instances seeded above (DoesShifts still cached false).
        _db.ChangeTracker.Clear();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task Creates_One_Category_Per_Resolvable_PrimaryShiftType()
    {
        var cats = await _db.ShiftCategories.IgnoreQueryFilters().OrderBy(c => c.Name).ToListAsync();

        cats.Should().HaveCount(2, "types 100 and 101 resolve a molecule; type 103 (company w/o molecule) is skipped");
        cats.Should().OnlyContain(c => c.MoleculeId == 1);
        cats.Select(c => c.DisplayName).Should().BeEquivalentTo(new[] { "HANAVA", "Morning" });
        // Unique-name safeguard: Name embeds the source shift-type id so the (MoleculeId, Name) index never collides.
        cats.Select(c => c.Name).Should().BeEquivalentTo(new[] { "HANAVA [100]", "Morning [101]" });
    }

    [Fact]
    public async Task Sets_DoesShifts_Only_For_Users_With_A_Resolvable_Category()
    {
        var users = await _db.Users.IgnoreQueryFilters().ToDictionaryAsync(u => u.Id);

        users[10].DoesShifts.Should().BeTrue();
        users[11].DoesShifts.Should().BeTrue();
        users[13].DoesShifts.Should().BeTrue();
        users[12].DoesShifts.Should().BeFalse("no primary shift type");
        users[14].DoesShifts.Should().BeFalse("primary type's company has no molecule → invariant clears DoesShifts");
    }

    [Fact]
    public async Task Maps_Users_To_Correct_Categories_Including_A_Shared_One()
    {
        var memberships = await _db.UserShiftCategories.IgnoreQueryFilters()
            .Include(m => m.ShiftCategory).ToListAsync();

        memberships.Should().HaveCount(3);
        memberships.Where(m => m.ShiftCategory.Name == "HANAVA [100]").Select(m => m.UserId)
            .Should().BeEquivalentTo(new[] { 10, 13 }, "users 10 and 13 share the same primary shift type");
        memberships.Single(m => m.ShiftCategory.Name == "Morning [101]").UserId.Should().Be(11);
    }

    [Fact]
    public async Task Holds_Invariant_DoesShifts_Implies_At_Least_One_Category()
    {
        var orphanCount = await _db.Users.IgnoreQueryFilters()
            .Where(u => u.DoesShifts && !_db.UserShiftCategories.Any(m => m.UserId == u.Id))
            .CountAsync();

        orphanCount.Should().Be(0);
    }
}
