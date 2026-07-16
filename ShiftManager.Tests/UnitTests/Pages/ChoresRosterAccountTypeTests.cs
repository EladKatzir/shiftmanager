using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Pages.Calendar;
using ShiftManager.Resources;
using ShiftManager.Services;
using System.Security.Claims;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Pages;

/// <summary>
/// Verifies that the Chores roster (GetUsersForMoleculeAsync) only returns Standard-account
/// users, excluding Mil and GroupUser accounts.
///
/// Approach: GetUsersForMoleculeAsync is changed to <c>internal</c> (InternalsVisibleTo
/// already configured in ShiftManager.csproj). The test instantiates the real ChoresModel
/// with a real SQLite (:memory:) DbContext, seeds the molecule/company hierarchy plus
/// Standard + Mil + GroupUser users, and calls GetUsersForMoleculeAsync() directly — so any
/// future removal of the AccountType predicate in production code will immediately fail.
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

    private const int MolId = 1;

    private async Task SeedAsync()
    {
        var area = new Area { Id = 1, ProjectId = 1, Name = "Area", DisplayName = "Area" };
        _db.Areas.Add(area);
        var molecule = new Molecule { Id = MolId, AreaId = 1, Name = "Mol", Type = MoleculeType.Workforce };
        _db.Molecules.Add(molecule);
        var company = new Company { Id = 1, MoleculeId = MolId, Name = "Co", DisplayName = "Co" };
        _db.Companies.Add(company);
        await _db.SaveChangesAsync();

        _db.Users.AddRange(
            new AppUser
            {
                Id = 1, Email = "std@test.com", DisplayName = "Standard",
                CompanyId = 1, IsActive = true, AccountType = AccountType.Standard,
                Role = UserRole.Employee, DoesChores = true   // D9: opted-in so the account-type gate is what's under test
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
    }

    /// <summary>Builds a ChoresModel with real SQLite Db. All services unused by GetUsersForMoleculeAsync are mocked with no-op stubs.</summary>
    private ChoresModel BuildModel()
    {
        var localizer = new Mock<IStringLocalizer<SharedResources>>();
        localizer.Setup(l => l[It.IsAny<string>()]).Returns<string>(k => new LocalizedString(k, k));

        var model = new ChoresModel(
            db: _db,
            choreService: Mock.Of<IChoreService>(),
            choreTypeService: Mock.Of<IChoreTypeService>(),
            choreCategoryService: new ChoreCategoryService(_db),
            grantService: Mock.Of<IGrantService>(),
            companyContext: Mock.Of<ICompanyContext>(),
            localizer: localizer.Object,
            logger: NullLogger<ChoresModel>.Instance,
            textEntryService: Mock.Of<ICalendarTextEntryService>(),
            calendarService: Mock.Of<IShiftCalendarService>(),
            justiceService: Mock.Of<IJusticeService>(),
            draftChoreService: Mock.Of<IDraftChoreService>(),
            draftLifecycle: Mock.Of<IDraftLifecycle>());

        // Wire up a minimal HttpContext (PageModel requires a non-null PageContext).
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.NameIdentifier, "99") }, "test"))
        };
        model.PageContext = new PageContext { HttpContext = httpContext };

        return model;
    }

    /// <summary>
    /// GetUsersForMoleculeAsync (the real production method) must exclude Mil and GroupUser
    /// accounts and return only Standard users. If the AccountType predicate is removed from
    /// production code, this test fails because result would have 3 entries instead of 1.
    /// </summary>
    [Fact]
    public async Task GetUsersForMoleculeAsync_OnlyReturnsStandardAccounts()
    {
        await SeedAsync();

        var model = BuildModel();

        // Call the real production method (internal visibility).
        var users = await model.GetUsersForMoleculeAsync(MolId);

        // ContainSingle proves Mil and GroupUser were excluded; DisplayName confirms the right user survived.
        // AccountType is not projected in the select (performance optimisation), so we assert identity
        // via DisplayName rather than the enum value.
        users.Should().ContainSingle("only the Standard user should pass the AccountType filter");
        users[0].DisplayName.Should().Be("Standard");
    }

    /// <summary>
    /// D9 roster predicate: a Standard user appears only if they participate in chores —
    /// either <c>DoesChores = true</c> OR they belong to ≥1 ChoreCategory. A Standard user
    /// with neither is excluded. Mil/GroupUser are excluded regardless (the account-type gate
    /// dominates the participation clause).
    /// </summary>
    [Fact]
    public async Task GetUsersForMoleculeAsync_D9_IncludesDoesChoresAndCategoryMembers_ExcludesNeitherAndNonStandard()
    {
        // Hierarchy
        var area = new Area { Id = 1, ProjectId = 1, Name = "Area", DisplayName = "Area" };
        _db.Areas.Add(area);
        _db.Molecules.Add(new Molecule { Id = MolId, AreaId = 1, Name = "Mol", Type = MoleculeType.Workforce });
        _db.Companies.Add(new Company { Id = 1, MoleculeId = MolId, Name = "Co", DisplayName = "Co" });
        await _db.SaveChangesAsync();

        var category = new ChoreCategory { Id = 1, MoleculeId = MolId, Name = "Physical", DisplayName = "Physical", IsActive = true };
        _db.ChoreCategories.Add(category);
        await _db.SaveChangesAsync();

        _db.Users.AddRange(
            // (A) Standard + DoesChores → INCLUDED
            new AppUser { Id = 1, Email = "does@test.com", DisplayName = "DoesChores", CompanyId = 1, IsActive = true,
                          AccountType = AccountType.Standard, Role = UserRole.Employee, DoesChores = true },
            // (B) Standard + NOT DoesChores but category member → INCLUDED
            new AppUser { Id = 2, Email = "cat@test.com", DisplayName = "CategoryMember", CompanyId = 1, IsActive = true,
                          AccountType = AccountType.Standard, Role = UserRole.Employee, DoesChores = false },
            // (C) Standard + neither → EXCLUDED
            new AppUser { Id = 3, Email = "neither@test.com", DisplayName = "Neither", CompanyId = 1, IsActive = true,
                          AccountType = AccountType.Standard, Role = UserRole.Employee, DoesChores = false },
            // (D) Mil + DoesChores → EXCLUDED (account-type gate dominates)
            new AppUser { Id = 4, Email = "mil@test.com", DisplayName = "Mil", CompanyId = 1, IsActive = true,
                          AccountType = AccountType.Mil, Role = UserRole.Employee, DoesChores = true },
            // (E) GroupUser + category member → EXCLUDED
            new AppUser { Id = 5, Email = "grp@test.com", DisplayName = "GroupUser", CompanyId = 1, IsActive = true,
                          AccountType = AccountType.GroupUser, Role = UserRole.Employee, DoesChores = false });
        await _db.SaveChangesAsync();
        _db.UserChoreCategories.Add(new UserChoreCategory { UserId = 2, ChoreCategoryId = 1 }); // makes (B) a member
        _db.UserChoreCategories.Add(new UserChoreCategory { UserId = 5, ChoreCategoryId = 1 }); // (E) member but GroupUser
        await _db.SaveChangesAsync();

        var model = BuildModel();
        var users = await model.GetUsersForMoleculeAsync(MolId);

        users.Select(u => u.DisplayName).Should().BeEquivalentTo(new[] { "DoesChores", "CategoryMember" },
            "D9 includes Standard DoesChores + Standard category-members, excludes Standard-with-neither and all Mil/GroupUser");
    }

    /// <summary>
    /// Baseline: without the AccountType filter all three active users would be returned.
    /// This proves the filter is doing meaningful work (not filtering an already-empty set).
    /// </summary>
    [Fact]
    public async Task GetUsersForMoleculeAsync_WithoutAccountTypeFilter_BaselineVerification_AllThreeUsersExist()
    {
        await SeedAsync();

        // Raw query — no AccountType predicate — must return all three seeded users.
        var companyIds = await _db.Companies
            .Where(c => c.MoleculeId == MolId)
            .Select(c => c.Id)
            .ToListAsync();

        var allUsers = await _db.Users
            .IgnoreQueryFilters()
            .Where(u => companyIds.Contains(u.CompanyId) && u.IsActive)
            .ToListAsync();

        allUsers.Should().HaveCount(3,
            "all three seeded account types (Standard, Mil, GroupUser) are active in the molecule");
    }
}
