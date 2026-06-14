using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ShiftManager.Data;
using ShiftManager.Hubs;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Pages.Calendar;
using ShiftManager.Resources;
using ShiftManager.Services;
using System.Security.Claims;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Pages;

/// <summary>
/// Verifies that the Overview roster (LoadUsersAsync) only returns Standard-account users,
/// excluding Mil and GroupUser accounts.
///
/// Approach: LoadUsersAsync is changed to <c>internal</c> (InternalsVisibleTo already
/// configured in ShiftManager.csproj). The test instantiates the real OverviewModel with
/// a real SQLite (:memory:) DbContext, seeds Standard + Mil + GroupUser users, sets the
/// PageModel's CompanyId, and calls LoadUsersAsync() directly — so any future removal of
/// the AccountType predicate in production code will immediately fail this test.
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

    /// <summary>Builds an OverviewModel with real SQLite Db. All services unused by LoadUsersAsync are mocked with no-op stubs.</summary>
    private OverviewModel BuildModel()
    {
        var localizer = new Mock<IStringLocalizer<SharedResources>>();
        localizer.Setup(l => l[It.IsAny<string>()]).Returns<string>(k => new LocalizedString(k, k));

        var model = new OverviewModel(
            db: _db,
            textEntryService: Mock.Of<ICalendarTextEntryService>(),
            grantService: Mock.Of<IGrantService>(),
            companyContext: Mock.Of<ICompanyContext>(),
            localizer: localizer.Object,
            companyLocalizationService: Mock.Of<ICompanyLocalizationService>(),
            tenantResolver: Mock.Of<ITenantResolver>(),
            auditLogService: Mock.Of<IAuditLogService>(),
            notificationService: Mock.Of<ICalendarNotificationService>(),
            logger: NullLogger<OverviewModel>.Instance);

        // Wire up a minimal HttpContext with a dummy user (not used by LoadUsersAsync,
        // but PageContext must be non-null because PageModel checks it).
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.NameIdentifier, "99") }, "test"))
        };
        model.PageContext = new PageContext { HttpContext = httpContext };

        return model;
    }

    /// <summary>
    /// LoadUsersAsync (the real production method) must exclude Mil and GroupUser accounts
    /// and include only Standard users. If the AccountType predicate is removed from
    /// production code, this test fails because result would have 3 entries instead of 1.
    /// </summary>
    [Fact]
    public async Task LoadUsersAsync_OnlyReturnsStandardAccounts()
    {
        await SeedUsersAsync();

        var model = BuildModel();
        // Directly set the properties that LoadUsersAsync reads.
        model.CompanyId = 1;
        model.UsersFilter = "active"; // default: active users only

        // Call the real production method (internal visibility).
        await model.LoadUsersAsync();

        model.Users.Should().ContainSingle("only the Standard user should pass the AccountType filter");
        model.Users[0].DisplayName.Should().Be("Standard User");
        model.Users[0].AccountType.Should().Be(AccountType.Standard);
    }

    /// <summary>
    /// Baseline: without the AccountType filter all three active users would be returned.
    /// This proves that the filter is doing meaningful work (not filtering an already-empty set).
    /// </summary>
    [Fact]
    public async Task LoadUsersAsync_WithoutAccountTypeFilter_BaselineVerification_AllThreeUsersExist()
    {
        await SeedUsersAsync();
        const int CompanyId = 1;

        // Raw query — no AccountType predicate — must return all three seeded users.
        var allUsers = await _db.Users
            .IgnoreQueryFilters()
            .Where(u => u.CompanyId == CompanyId && u.IsActive)
            .ToListAsync();

        allUsers.Should().HaveCount(3,
            "all three seeded account types (Standard, Mil, GroupUser) are active in company 1");
    }
}
