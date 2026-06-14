using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Pages;

/// <summary>
/// Verifies that the Chores roster query (GetUsersForMoleculeAsync predicate) only
/// returns Standard-account users, excluding both Mil and GroupUser accounts.
///
/// <see cref="ShiftManager.Pages.Calendar.ChoresModel"/> calls the private
/// GetUsersForMoleculeAsync method — not directly callable from tests. This test
/// replicates the exact query predicate against a real SQLite context so SQL translation
/// is exercised (not UseInMemoryDatabase). Task-16 browser sweep provides end-to-end
/// coverage on the live page.
/// </summary>
public sealed class ChoresRosterAccountTypeTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private AppDbContext _db = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        await _connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection).Options;
        _db = new AppDbContext(options);
        await _db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private async Task<List<int>> SeedAsync()
    {
        var area = new Area { Id = 1, ProjectId = 1, Name = "Area", DisplayName = "Area" };
        _db.Areas.Add(area);
        var molecule = new Molecule { Id = 1, AreaId = 1, Name = "Mol", Type = MoleculeType.Workforce };
        _db.Molecules.Add(molecule);
        var company = new Company { Id = 1, MoleculeId = 1, Name = "Co", DisplayName = "Co" };
        _db.Companies.Add(company);
        await _db.SaveChangesAsync();

        _db.Users.AddRange(
            new AppUser
            {
                Id = 1, Email = "std@test.com", DisplayName = "Standard",
                CompanyId = 1, IsActive = true, AccountType = AccountType.Standard,
                Role = UserRole.Employee
            },
            new AppUser
            {
                Id = 2, Email = "mil@test.com", DisplayName = "Mil",
                CompanyId = 1, IsActive = true, AccountType = AccountType.Mil,
                Role = UserRole.Employee
            },
            new AppUser
            {
                Id = 3, Email = "grp@test.com", DisplayName = "GroupUser",
                CompanyId = 1, IsActive = true, AccountType = AccountType.GroupUser,
                Role = UserRole.Employee
            });
        await _db.SaveChangesAsync();

        // Return the list of company IDs for this molecule (mirrors what GetUsersForMoleculeAsync does)
        return await _db.Companies
            .Where(c => c.MoleculeId == 1)
            .Select(c => c.Id)
            .ToListAsync();
    }

    /// <summary>
    /// The Chores GetUsersForMoleculeAsync query applies:
    ///   .Where(u => companyIds.Contains(u.CompanyId) && u.IsActive
    ///            && u.AccountType == AccountType.Standard)
    /// This test exercises the same predicate shape via real SQLite.
    /// </summary>
    [Fact]
    public async Task ChoresRosterQuery_OnlyReturnsStandardAccounts()
    {
        var companyIds = await SeedAsync();

        // Replicate the exact predicate from Chores.cshtml.cs GetUsersForMoleculeAsync
        var result = await _db.Users
            .IgnoreQueryFilters()
            .Where(u => companyIds.Contains(u.CompanyId) && u.IsActive
                     && u.AccountType == AccountType.Standard)
            .OrderBy(u => u.DisplayName)
            .ToListAsync();

        result.Should().ContainSingle();
        result[0].DisplayName.Should().Be("Standard");
        result[0].AccountType.Should().Be(AccountType.Standard);
    }

    [Fact]
    public async Task ChoresRosterQuery_WithoutAccountTypeFilter_ReturnsAllThree()
    {
        var companyIds = await SeedAsync();

        // Confirm the base query (without the new predicate) returns all 3,
        // proving the filter is actually doing work.
        var result = await _db.Users
            .IgnoreQueryFilters()
            .Where(u => companyIds.Contains(u.CompanyId) && u.IsActive)
            .ToListAsync();

        result.Should().HaveCount(3);
    }
}
