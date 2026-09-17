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
/// Pins the company-header fallback on the by-user SHIFTS calendar — the shift-side twin of
/// <see cref="ChoresCategoryGroupingTests"/>.
///
/// **Why this matters now**: new users are created with <c>DoesShifts = true</c> by default, and no
/// creation path assigns shift categories. A migration comment
/// (<c>Migrations/ShiftCategoryBackfillSql.cs</c>) documents the invariant
/// "DoesShifts = true ⇒ user has ≥ 1 category", so a participant with zero categories is exactly
/// the shape that default now produces on every new account. If such a user were dropped from the
/// grouping, they would be invisible on the calendar the day they are created. They must land under
/// their Company header instead.
/// </summary>
public sealed class ShiftsCategoryFallbackTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private AppDbContext _db = null!;
    private const int MolId = 1;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        _db = new AppDbContext(options);
        await _db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private ShiftsModel BuildModel()
    {
        var localizer = new Mock<IStringLocalizer<SharedResources>>();
        localizer.Setup(l => l[It.IsAny<string>()]).Returns<string>(k => new LocalizedString(k, k));

        var model = new ShiftsModel(
            db: _db,
            calendarService: Mock.Of<IShiftCalendarService>(),
            grantService: Mock.Of<IGrantService>(),
            localizer: localizer.Object,
            companyLocalizationService: Mock.Of<ICompanyLocalizationService>(),
            tenantResolver: Mock.Of<ITenantResolver>(),
            logger: NullLogger<ShiftsModel>.Instance,
            jobTypeService: Mock.Of<IJobTypeService>(),
            traineeService: Mock.Of<ITraineeService>(),
            choreTypeService: Mock.Of<IChoreTypeService>(),
            textEntryService: Mock.Of<ICalendarTextEntryService>(),
            dayNoteService: Mock.Of<ICalendarDayNoteService>(),
            justiceService: Mock.Of<IJusticeService>(),
            distributionListService: Mock.Of<IDistributionListService>(),
            categoryService: new ShiftCategoryService(_db),   // real — reads the seeded categories
            tabService: Mock.Of<IShiftTabService>(),
            draftService: Mock.Of<IDraftModeService>(),
            featureFlags: Mock.Of<IFeatureFlagService>());

        model.PageContext = new PageContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    new[] { new Claim(ClaimTypes.NameIdentifier, "99") }, "test"))
            }
        };
        return model;
    }

    [Fact]
    public async Task ParticipantWithNoCategory_FallsUnderTheCompanyHeader_AndIsNotLost()
    {
        _db.Projects.Add(new Project { Id = 1, Name = "P", DisplayName = "P" });
        _db.Areas.Add(new Area { Id = 1, ProjectId = 1, Name = "Area", DisplayName = "Area" });
        _db.Molecules.Add(new Molecule { Id = MolId, AreaId = 1, Name = "Mol", Type = MoleculeType.Workforce });
        _db.Companies.Add(new Company { Id = 1, MoleculeId = MolId, Name = "Co", Slug = "co", DisplayName = "Co" });
        await _db.SaveChangesAsync();

        _db.ShiftCategories.Add(new ShiftCategory
        {
            Id = 1, MoleculeId = MolId, Name = "Guard", DisplayName = "Guard", SortOrder = 0, IsActive = true
        });
        await _db.SaveChangesAsync();

        // A is a categorised participant; B is exactly the shape a NEWLY CREATED user now has:
        // DoesShifts = true, zero category memberships.
        _db.Users.AddRange(
            new AppUser { Id = 1, Email = "a@test.com", DisplayName = "UserA", CompanyId = 1, IsActive = true,
                          AccountType = AccountType.Standard, Role = UserRole.Employee, DoesShifts = true },
            new AppUser { Id = 2, Email = "b@test.com", DisplayName = "UserB", CompanyId = 1, IsActive = true,
                          AccountType = AccountType.Standard, Role = UserRole.Employee, DoesShifts = true });
        await _db.SaveChangesAsync();

        _db.UserShiftCategories.Add(new UserShiftCategory { UserId = 1, ShiftCategoryId = 1 });
        await _db.SaveChangesAsync();

        var model = BuildModel();
        model.StartDate = new DateOnly(2026, 6, 15);
        model.EndDate = new DateOnly(2026, 6, 21);

        var users = new List<AppUser>
        {
            new AppUser { Id = 1, DisplayName = "UserA", CompanyId = 1, DoesShifts = true },
            new AppUser { Id = 2, DisplayName = "UserB", CompanyId = 1, DoesShifts = true }
        };

        var (rows, groups) = await model.BuildCategoryGroupedRowsAsync(
            users,
            MolId,
            instances: new List<ShiftInstance>(),
            assignments: new List<ShiftAssignment>(),
            overlays: new Dictionary<(int UserId, DateOnly Date), FyiOverlayData>(),
            localizedShiftNames: new Dictionary<int, string>(),
            textEntries: new Dictionary<(int UserId, DateOnly Date), List<(int Id, string Text)>>(),
            overviewNotes: new Dictionary<(int UserId, DateOnly Date), string>());

        rows.Select(r => r.Id).Should().Contain("user-2",
            "a participant with no category must still be rendered — otherwise every newly created " +
            "user is invisible on the calendar until an admin assigns them a category");
        rows.Where(r => r.GroupId == "company-1").Select(r => r.Id)
            .Should().BeEquivalentTo(new[] { "user-2" });
        rows.Where(r => r.GroupId == "category-1").Select(r => r.Id).Should().Contain("user-1");
        groups.Select(g => g.Id).Should().ContainInOrder("category-1", "company-1");
    }
}
