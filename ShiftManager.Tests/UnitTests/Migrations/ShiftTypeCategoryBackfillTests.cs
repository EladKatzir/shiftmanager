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
/// Regression guard for the <c>BackfillShiftTypeCategoryId</c> migration. Runs the EXACT SQL it ships
/// (<see cref="ShiftTypeCategoryBackfillSql.Forward"/>) against real SQLite over a seed that exercises:
///   * a molecule-scoped type WITH a matching "[id]"-tagged category      -> CategoryId stamped,
///   * a company-scoped type WITH a matching category (molecule via company) -> stamped,
///   * a HOME type with no matching category                              -> stays NULL,
///   * an assignable type whose category was never synthesized            -> stays NULL,
///   * idempotency (re-run is a no-op) + a pre-set CategoryId is never clobbered.
/// </summary>
public sealed class ShiftTypeCategoryBackfillTests : IAsyncLifetime
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

        _db.Projects.Add(new Project { Id = 1, Name = "P", DisplayName = "P" });
        _db.Areas.Add(new Area { Id = 1, ProjectId = 1, Name = "A", DisplayName = "A" });
        _db.Molecules.Add(new Molecule { Id = 1, AreaId = 1, Name = "M", DisplayName = "M", Type = MoleculeType.Workforce });
        _db.Companies.Add(new Company { Id = 1, Name = "Co1", Slug = "co1", MoleculeId = 1 });

        // Categories the user-backfill would have synthesized (note the "[id]" tag). Add BEFORE the
        // shift types so type 104's pre-set CategoryId = 9001 satisfies the FK.
        _db.ShiftCategories.AddRange(
            new ShiftCategory { Id = 900, MoleculeId = 1, Name = "Hanava [100]", DisplayName = "Hanava" },
            new ShiftCategory { Id = 901, MoleculeId = 1, Name = "Morning [101]", DisplayName = "Morning" },
            new ShiftCategory { Id = 9001, MoleculeId = 1, Name = "Manual Cat", DisplayName = "Manual Cat" });

        _db.ShiftTypes.AddRange(
            // 100: molecule-scoped, NameEn "Hanava" -> category "Hanava [100]" exists
            new ShiftType { Id = 100, Scope = ShiftScope.Molecule, MoleculeId = 1, Key = "HANAVA", NameEn = "Hanava" },
            // 101: company-scoped, NameEn "Morning" -> category "Morning [101]" exists (molecule via Co1)
            new ShiftType { Id = 101, Scope = ShiftScope.Company, CompanyId = 1, Key = "MORNING", NameEn = "Morning" },
            // 102: HOME shared type -> no category -> stays null
            new ShiftType { Id = 102, Scope = ShiftScope.Molecule, MoleculeId = 1, Key = "HOME", NameEn = "Home" },
            // 103: assignable type whose category was never synthesized -> stays null
            new ShiftType { Id = 103, Scope = ShiftScope.Molecule, MoleculeId = 1, Key = "NIGHT", NameEn = "Night" },
            // 104: a type with a PRE-SET CategoryId (admin choice) -> never clobbered
            new ShiftType { Id = 104, Scope = ShiftScope.Molecule, MoleculeId = 1, Key = "EVENING", NameEn = "Evening", CategoryId = 9001 });
        await _db.SaveChangesAsync();

        foreach (var sql in ShiftTypeCategoryBackfillSql.Forward)
            await _db.Database.ExecuteSqlRawAsync(sql);

        _db.ChangeTracker.Clear();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task Stamps_CategoryId_For_Types_With_A_Matching_Tagged_Category()
    {
        var types = await _db.ShiftTypes.IgnoreQueryFilters().ToDictionaryAsync(t => t.Id);
        types[100].CategoryId.Should().Be(900);
        types[101].CategoryId.Should().Be(901, "molecule resolved via Company(1).MoleculeId");
    }

    [Fact]
    public async Task Leaves_Shared_And_NeverPrimary_Types_Null()
    {
        var types = await _db.ShiftTypes.IgnoreQueryFilters().ToDictionaryAsync(t => t.Id);
        types[102].CategoryId.Should().BeNull("HOME has no synthesized category -> runtime fallback");
        types[103].CategoryId.Should().BeNull("never anyone's primary -> no category -> runtime fallback");
    }

    [Fact]
    public async Task Never_Clobbers_A_PreSet_CategoryId()
    {
        var types = await _db.ShiftTypes.IgnoreQueryFilters().ToDictionaryAsync(t => t.Id);
        types[104].CategoryId.Should().Be(9001, "WHERE CategoryId IS NULL guards the admin's manual choice");
    }

    [Fact]
    public async Task Is_Idempotent_On_Re_Run()
    {
        foreach (var sql in ShiftTypeCategoryBackfillSql.Forward)
            await _db.Database.ExecuteSqlRawAsync(sql);
        _db.ChangeTracker.Clear();

        var types = await _db.ShiftTypes.IgnoreQueryFilters().ToDictionaryAsync(t => t.Id);
        types[100].CategoryId.Should().Be(900);
        types[101].CategoryId.Should().Be(901);
        types[102].CategoryId.Should().BeNull();
        types[104].CategoryId.Should().Be(9001);
    }
}
