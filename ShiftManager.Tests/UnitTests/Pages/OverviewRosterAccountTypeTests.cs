using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Pages;

/// <summary>
/// Verifies that the Overview roster query (LoadUsersAsync predicate) only returns
/// Standard-account users, excluding Mil and GroupUser accounts.
///
/// <see cref="ShiftManager.Pages.Calendar.OverviewModel"/> calls LoadUsersAsync which
/// is a private PageModel method — not directly callable from tests. The test
/// replicates the exact query predicate applied in that method against a real SQLite
/// context, so SQL translation is exercised (unlike UseInMemoryDatabase).
/// Task-16 browser sweep provides end-to-end coverage on the live page.
/// </summary>
public sealed class OverviewRosterAccountTypeTests : IAsyncLifetime
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

    private async Task SeedUsersAsync()
    {
        const int CompanyId = 1;

        _db.Users.AddRange(
            new AppUser
            {
                Id = 1, Email = "std@test.com", DisplayName = "Standard User",
                CompanyId = CompanyId, IsActive = true, AccountType = AccountType.Standard,
                Role = UserRole.Employee
            },
            new AppUser
            {
                Id = 2, Email = "mil@test.com", DisplayName = "Mil User",
                CompanyId = CompanyId, IsActive = true, AccountType = AccountType.Mil,
                Role = UserRole.Employee
            },
            new AppUser
            {
                Id = 3, Email = "grp@test.com", DisplayName = "Group User",
                CompanyId = CompanyId, IsActive = true, AccountType = AccountType.GroupUser,
                Role = UserRole.Employee
            });

        await _db.SaveChangesAsync();
    }

    /// <summary>
    /// The Overview LoadUsersAsync query applies:
    ///   .Where(u => u.CompanyId == CompanyId)
    ///   .Where(u => u.AccountType == AccountType.Standard)
    ///   .Where(u => u.IsActive)
    /// This test exercises the same predicate shape via real SQLite.
    /// </summary>
    [Fact]
    public async Task OverviewRosterQuery_OnlyReturnsStandardAccounts()
    {
        await SeedUsersAsync();
        const int CompanyId = 1;

        // Replicate the predicate from Overview.cshtml.cs LoadUsersAsync
        var result = await _db.Users
            .IgnoreQueryFilters()
            .Where(u => u.CompanyId == CompanyId
                     && u.AccountType == AccountType.Standard
                     && u.IsActive)
            .OrderBy(u => u.DisplayName)
            .ToListAsync();

        result.Should().ContainSingle();
        result[0].DisplayName.Should().Be("Standard User");
        result[0].AccountType.Should().Be(AccountType.Standard);
    }

    [Fact]
    public async Task OverviewRosterQuery_WithoutAccountTypeFilter_ReturnsAllThree()
    {
        await SeedUsersAsync();
        const int CompanyId = 1;

        // Confirm the base query (without the new predicate) returns all 3,
        // proving the filter is actually doing work.
        var result = await _db.Users
            .IgnoreQueryFilters()
            .Where(u => u.CompanyId == CompanyId && u.IsActive)
            .ToListAsync();

        result.Should().HaveCount(3);
    }
}
