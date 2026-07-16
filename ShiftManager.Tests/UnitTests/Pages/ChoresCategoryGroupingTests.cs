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
using ShiftManager.ViewComponents;
using System.Security.Claims;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Pages;

/// <summary>
/// Verifies the category-accordion + mirrored-row grouping of the by-user Chores calendar
/// (BuildChoreCategoryGroupedRowsAsync — parity with the shift by-user calendar). A participant
/// belonging to N categories appears as N mirrored rows (one per category); a participant with no
/// category falls under a company-header fallback group so nobody is lost.
///
/// Approach mirrors ChoresRosterAccountTypeTests: real SQLite (:memory:) + real ChoreCategoryService
/// so GetCategoriesForMoleculeAsync reads the seeded categories. The grouping method is internal
/// (InternalsVisibleTo configured) and called directly.
/// </summary>
public sealed class ChoresCategoryGroupingTests : IAsyncLifetime
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

    private ChoresModel BuildModel()
    {
        var localizer = new Mock<IStringLocalizer<SharedResources>>();
        localizer.Setup(l => l[It.IsAny<string>()]).Returns<string>(k => new LocalizedString(k, k));

        var model = new ChoresModel(
            db: _db,
            choreService: Mock.Of<IChoreService>(),
            choreTypeService: Mock.Of<IChoreTypeService>(),
            choreCategoryService: new ChoreCategoryService(_db),  // real — reads seeded categories
            grantService: Mock.Of<IGrantService>(),
            companyContext: Mock.Of<ICompanyContext>(),
            localizer: localizer.Object,
            logger: NullLogger<ChoresModel>.Instance,
            textEntryService: Mock.Of<ICalendarTextEntryService>(),
            calendarService: Mock.Of<IShiftCalendarService>(),
            justiceService: Mock.Of<IJusticeService>(),
            draftChoreService: Mock.Of<IDraftChoreService>(),
            draftLifecycle: Mock.Of<IDraftLifecycle>());

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.NameIdentifier, "99") }, "test"))
        };
        model.PageContext = new PageContext { HttpContext = httpContext };
        return model;
    }

    /// <summary>
    /// Seed: molecule M, company C. Categories Physical(1, color #A1B2C3), Computer(2). User A (id 1) in
    /// BOTH categories; user B (id 2) in NONE. Expect groups [chorecategory-1, chorecategory-2, company-1];
    /// A mirrored under both categories; B once under company-1; A NOT in the company fallback; category
    /// color carried to the header.
    /// </summary>
    [Fact]
    public async Task BuildChoreCategoryGroupedRows_MirrorsUserPerMembership_AndFallsBackToCompany()
    {
        var area = new Area { Id = 1, ProjectId = 1, Name = "Area", DisplayName = "Area" };
        _db.Areas.Add(area);
        _db.Molecules.Add(new Molecule { Id = MolId, AreaId = 1, Name = "Mol", Type = MoleculeType.Workforce });
        _db.Companies.Add(new Company { Id = 1, MoleculeId = MolId, Name = "Co", DisplayName = "Co" });
        await _db.SaveChangesAsync();

        _db.ChoreCategories.AddRange(
            new ChoreCategory { Id = 1, MoleculeId = MolId, Name = "Physical", DisplayName = "Physical", Color = "#A1B2C3", SortOrder = 0, IsActive = true },
            new ChoreCategory { Id = 2, MoleculeId = MolId, Name = "Computer", DisplayName = "Computer", SortOrder = 1, IsActive = true });
        await _db.SaveChangesAsync();

        _db.Users.AddRange(
            new AppUser { Id = 1, Email = "a@test.com", DisplayName = "UserA", CompanyId = 1, IsActive = true,
                          AccountType = AccountType.Standard, Role = UserRole.Employee, DoesChores = true },
            new AppUser { Id = 2, Email = "b@test.com", DisplayName = "UserB", CompanyId = 1, IsActive = true,
                          AccountType = AccountType.Standard, Role = UserRole.Employee, DoesChores = true });
        await _db.SaveChangesAsync();

        // User A is a member of both categories; user B is a member of none.
        _db.UserChoreCategories.AddRange(
            new UserChoreCategory { UserId = 1, ChoreCategoryId = 1 },
            new UserChoreCategory { UserId = 1, ChoreCategoryId = 2 });
        await _db.SaveChangesAsync();

        var model = BuildModel();
        model.StartDate = new DateOnly(2026, 6, 15);
        model.EndDate = new DateOnly(2026, 6, 21);

        // The projected-AppUser shape the page builds (Id, DisplayName, CompanyId).
        var users = new List<AppUser>
        {
            new AppUser { Id = 1, DisplayName = "UserA", CompanyId = 1 },
            new AppUser { Id = 2, DisplayName = "UserB", CompanyId = 1 }
        };

        var overlays = new Dictionary<(int UserId, DateOnly Date), FyiOverlayData>();
        var textEntries = new Dictionary<(int UserId, DateOnly Date), List<(int Id, string Text)>>();
        var overviewNotes = new Dictionary<(int UserId, DateOnly Date), string>();
        var homeShifts = new Dictionary<(int UserId, DateOnly Date), List<ChoresModel.HomeShiftItem>>();

        var (rows, groups) = await model.BuildChoreCategoryGroupedRowsAsync(
            users, MolId, chores: new List<Chore>(), overlays, textEntries, overviewNotes, homeShifts, isHebrew: false);

        groups.Select(g => g.Id).Should().ContainInOrder("chorecategory-1", "chorecategory-2", "company-1");
        rows.Count(r => r.Id == "user-1").Should().Be(2, "user A is mirrored under both categories");
        rows.Where(r => r.GroupId == "chorecategory-1").Select(r => r.Id).Should().Contain("user-1");
        rows.Where(r => r.GroupId == "chorecategory-2").Select(r => r.Id).Should().Contain("user-1");
        rows.Where(r => r.GroupId == "company-1").Select(r => r.Id).Should().BeEquivalentTo(new[] { "user-2" });
        rows.Where(r => r.GroupId == "company-1").Select(r => r.Id).Should().NotContain("user-1");
        groups.Single(g => g.Id == "chorecategory-1").Color.Should().Be("#A1B2C3"); // category color carried to header
    }

    /// <summary>All participants categorized → no company fallback group is emitted.</summary>
    [Fact]
    public async Task BuildChoreCategoryGroupedRows_AllCategorized_NoCompanyFallback()
    {
        var area = new Area { Id = 1, ProjectId = 1, Name = "Area", DisplayName = "Area" };
        _db.Areas.Add(area);
        _db.Molecules.Add(new Molecule { Id = MolId, AreaId = 1, Name = "Mol", Type = MoleculeType.Workforce });
        _db.Companies.Add(new Company { Id = 1, MoleculeId = MolId, Name = "Co", DisplayName = "Co" });
        await _db.SaveChangesAsync();

        _db.ChoreCategories.Add(new ChoreCategory { Id = 1, MoleculeId = MolId, Name = "Physical", DisplayName = "Physical", SortOrder = 0, IsActive = true });
        await _db.SaveChangesAsync();

        _db.Users.Add(new AppUser { Id = 1, Email = "a@test.com", DisplayName = "UserA", CompanyId = 1, IsActive = true,
                                    AccountType = AccountType.Standard, Role = UserRole.Employee, DoesChores = true });
        await _db.SaveChangesAsync();
        _db.UserChoreCategories.Add(new UserChoreCategory { UserId = 1, ChoreCategoryId = 1 });
        await _db.SaveChangesAsync();

        var model = BuildModel();
        model.StartDate = new DateOnly(2026, 6, 15);
        model.EndDate = new DateOnly(2026, 6, 21);

        var users = new List<AppUser> { new AppUser { Id = 1, DisplayName = "UserA", CompanyId = 1 } };

        var (rows, groups) = await model.BuildChoreCategoryGroupedRowsAsync(
            users, MolId, new List<Chore>(),
            new Dictionary<(int UserId, DateOnly Date), FyiOverlayData>(),
            new Dictionary<(int UserId, DateOnly Date), List<(int Id, string Text)>>(),
            new Dictionary<(int UserId, DateOnly Date), string>(),
            new Dictionary<(int UserId, DateOnly Date), List<ChoresModel.HomeShiftItem>>(),
            isHebrew: false);

        groups.Select(g => g.Id).Should().BeEquivalentTo(new[] { "chorecategory-1" });
        groups.Should().NotContain(g => g.Id == "company-1", "every participant is categorized");
        rows.Should().ContainSingle().Which.GroupId.Should().Be("chorecategory-1");
    }
}
