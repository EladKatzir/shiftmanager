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
/// Verifies the week-view by-user calendar row (<c>BuildUserRow</c> — the builder used by
/// <c>Mode=user</c>) computes <c>ExcelCalendarRow.ShiftCount</c> as a count of real shifts,
/// applying the same IsHome/IsOffline exclusion already used for <c>WeeklyHours</c> (#8).
///
/// Approach mirrors ChoresCategoryGroupingTests / HomeWeekSummaryTests: real SQLite (:memory:)
/// with Foreign Keys=False (ShiftAssignment.UserId does not need a matching AppUsers row —
/// BuildUserRow takes the AppUser object directly, not a DB lookup). ShiftType rows use
/// Scope = ShiftScope.Company + CompanyId to satisfy CK_ShiftType_Company_Scope.
/// BuildUserRow is internal (InternalsVisibleTo configured) and called directly, matching the
/// precedent set by BuildChoreCategoryGroupedRowsAsync in Chores.cshtml.cs.
/// </summary>
public sealed class ShiftsUserRowShiftCountTests : IAsyncLifetime
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

    private const int CompanyId = 1;

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
            categoryService: Mock.Of<IShiftCategoryService>(),
            draftService: Mock.Of<IDraftModeService>(),
            featureFlags: Mock.Of<IFeatureFlagService>());

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.NameIdentifier, "99") }, "test"))
        };
        model.PageContext = new PageContext { HttpContext = httpContext };
        return model;
    }

    /// <summary>
    /// A week with 3 real (non-home/offline) assignments + 1 HOME + 1 OFFLINE assignment for the
    /// same user must yield ShiftCount == 3 — the HOME/OFFLINE presence assignments are excluded,
    /// same exclusion rule already applied to WeeklyHours.
    /// </summary>
    [Fact]
    public async Task BuildUserRow_ShiftCount_ExcludesHomeAndOffline()
    {
        const int UserId = 1;
        var startOfWeek = new DateOnly(2026, 7, 5); // arbitrary fixed Sunday — deterministic regardless of run date
        var endOfWeek = startOfWeek.AddDays(6);

        // Scope = Company (+ CompanyId set) satisfies CK_ShiftType_Company_Scope while trivially
        // satisfying CK_ShiftType_Molecule_Scope/CK_ShiftType_Area_Scope (Scope != 1 and != 2).
        var realType = new ShiftType { Id = 1, CompanyId = CompanyId, Scope = ShiftScope.Company, Key = ShiftType.KEY_MORNING, Start = new TimeOnly(7, 0), End = new TimeOnly(15, 0) };
        var homeType = new ShiftType { Id = 2, CompanyId = CompanyId, Scope = ShiftScope.Company, Key = ShiftType.KEY_HOME, Start = new TimeOnly(0, 0), End = new TimeOnly(0, 0) };
        var offlineType = new ShiftType { Id = 3, CompanyId = CompanyId, Scope = ShiftScope.Company, Key = ShiftType.KEY_OFFLINE, Start = new TimeOnly(0, 0), End = new TimeOnly(0, 0) };
        _db.ShiftTypes.AddRange(realType, homeType, offlineType);

        // Days 0-2: real shifts. Day 3: HOME. Day 4: OFFLINE. All within [startOfWeek, endOfWeek].
        var instances = new List<ShiftInstance>
        {
            new ShiftInstance { Id = 1, CompanyId = CompanyId, ShiftTypeId = realType.Id, WorkDate = startOfWeek.AddDays(0) },
            new ShiftInstance { Id = 2, CompanyId = CompanyId, ShiftTypeId = realType.Id, WorkDate = startOfWeek.AddDays(1) },
            new ShiftInstance { Id = 3, CompanyId = CompanyId, ShiftTypeId = realType.Id, WorkDate = startOfWeek.AddDays(2) },
            new ShiftInstance { Id = 4, CompanyId = CompanyId, ShiftTypeId = homeType.Id, WorkDate = startOfWeek.AddDays(3) },
            new ShiftInstance { Id = 5, CompanyId = CompanyId, ShiftTypeId = offlineType.Id, WorkDate = startOfWeek.AddDays(4) },
        };
        _db.ShiftInstances.AddRange(instances);

        _db.ShiftAssignments.AddRange(
            instances.Select((inst, idx) => new ShiftAssignment { Id = idx + 1, CompanyId = CompanyId, ShiftInstanceId = inst.Id, UserId = UserId }));

        await _db.SaveChangesAsync();

        // Re-query with the same Include shape GetAssignmentsAsync uses, so BuildUserRow sees
        // properly hydrated ShiftInstance/ShiftType navigation properties (production shape).
        var assignments = await _db.ShiftAssignments
            .Include(a => a.ShiftInstance).ThenInclude(si => si.ShiftType)
            .ToListAsync();

        var model = BuildModel();
        model.StartDate = startOfWeek;
        model.EndDate = endOfWeek;

        var user = new AppUser { Id = UserId, DisplayName = "Test User", CompanyId = CompanyId };

        var row = model.BuildUserRow(
            user,
            groupId: null,
            companyName: null,
            subLabel: null,
            instances: new List<ShiftInstance>(),
            assignments: assignments,
            overlays: new Dictionary<(int UserId, DateOnly Date), FyiOverlayData>(),
            localizedShiftNames: new Dictionary<int, string>(),
            textEntries: new Dictionary<(int UserId, DateOnly Date), List<(int Id, string Text)>>(),
            overviewNotes: new Dictionary<(int UserId, DateOnly Date), string>());

        row.ShiftCount.Should().Be(3, "only the 3 non-home/offline assignments count as shifts");
    }
}
