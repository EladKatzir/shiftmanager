using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Pages.Home;
using ShiftManager.Services;
using System.Security.Claims;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Pages;

/// <summary>
/// Verifies the Home "This Week" summary card classifies each of the 7 week days into
/// exactly ONE disjoint bucket (real shift / vacation / offline) by precedence, instead of
/// computing the offline count via subtraction (7 - shifts - vacation). Subtraction is unsound
/// because a day can carry BOTH an approved vacation AND a HOME shift at once (HOME shifts are
/// materialized from approved TimeOffRequests — see ShiftAssignment.SourceTimeOffRequestId) —
/// that would double-count the day and could drive the offline count negative.
///
/// Approach: LoadCommonDataAsync is internal (InternalsVisibleTo already configured in
/// ShiftManager.csproj — see OverviewRosterAccountTypeTests.cs for the established pattern).
/// The test instantiates the real IndexModel with a real SQLite (:memory:) DbContext, seeds
/// shift/vacation rows with a deliberate overlap day, and calls LoadCommonDataAsync directly
/// so the real production classification logic is exercised end-to-end.
/// </summary>
public sealed class HomeWeekSummaryTests : IAsyncLifetime
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

    /// <summary>Builds an IndexModel with a real SQLite Db. Services unused by LoadCommonDataAsync are no-op stubs.</summary>
    private IndexModel BuildModel()
    {
        var model = new IndexModel(
            context: _db,
            companyContext: Mock.Of<ICompanyContext>(),
            grantService: Mock.Of<IGrantService>(),
            logger: NullLogger<IndexModel>.Instance,
            whoIsOnShiftService: Mock.Of<IWhoIsOnShiftService>(),
            announcementService: Mock.Of<IAnnouncementService>());

        // Wire up a minimal HttpContext (not used by LoadCommonDataAsync, but PageContext must
        // be non-null because PageModel checks it) — mirrors OverviewRosterAccountTypeTests.cs.
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.NameIdentifier, "42") }, "test"))
        };
        model.PageContext = new PageContext { HttpContext = httpContext };

        return model;
    }

    /// <summary>
    /// A week where one day has BOTH an approved vacation AND a HOME shift must not double-count:
    /// that day must land in exactly one bucket (vacation — a HOME shift is not a "real" shift, so
    /// it does not win precedence), and the four resulting numbers must be disjoint and sum to 7.
    /// </summary>
    [Fact]
    public async Task WeekSummary_VacationOverlappingHomeShift_CountsAreDisjointAndSumTo7()
    {
        const int UserId = 42;
        const int CompanyId = 1;
        var startOfWeek = new DateOnly(2026, 7, 5); // arbitrary fixed Sunday — deterministic regardless of run date
        var endOfWeek = startOfWeek.AddDays(7);

        // Shift types: one "real" workforce shift, one HOME presence shift (rotation day off).
        // Scope = Company (+ CompanyId set) satisfies CK_ShiftType_Company_Scope while trivially
        // satisfying CK_ShiftType_Molecule_Scope/CK_ShiftType_Area_Scope (Scope != 1 and != 2) —
        // avoids needing to fabricate an unrelated Molecule/Area parent row for this test.
        var realShiftType = new ShiftType
        {
            Id = 1,
            CompanyId = CompanyId,
            Scope = ShiftScope.Company,
            Key = ShiftType.KEY_MORNING,
            Start = new TimeOnly(7, 0),
            End = new TimeOnly(15, 0)
        };
        var homeShiftType = new ShiftType
        {
            Id = 2,
            CompanyId = CompanyId,
            Scope = ShiftScope.Company,
            Key = ShiftType.KEY_HOME,
            Start = new TimeOnly(0, 0),
            End = new TimeOnly(0, 0)
        };
        _db.ShiftTypes.AddRange(realShiftType, homeShiftType);

        // Day 0, Day 1: real (non-home/non-offline) shifts -> shiftDay.
        // Day 2: a HOME shift AND an approved vacation covering the same day -> must land in the
        //        vacation bucket only (HOME is not a "real" shift, so it doesn't claim the day).
        // Days 3-6: nothing seeded -> offlineDay.
        var day0 = startOfWeek;
        var day1 = startOfWeek.AddDays(1);
        var day2 = startOfWeek.AddDays(2);

        var instance0 = new ShiftInstance { Id = 1, CompanyId = CompanyId, ShiftTypeId = realShiftType.Id, WorkDate = day0 };
        var instance1 = new ShiftInstance { Id = 2, CompanyId = CompanyId, ShiftTypeId = realShiftType.Id, WorkDate = day1 };
        var instance2 = new ShiftInstance { Id = 3, CompanyId = CompanyId, ShiftTypeId = homeShiftType.Id, WorkDate = day2 };
        _db.ShiftInstances.AddRange(instance0, instance1, instance2);

        _db.ShiftAssignments.AddRange(
            new ShiftAssignment { Id = 1, CompanyId = CompanyId, ShiftInstanceId = instance0.Id, UserId = UserId },
            new ShiftAssignment { Id = 2, CompanyId = CompanyId, ShiftInstanceId = instance1.Id, UserId = UserId },
            new ShiftAssignment { Id = 3, CompanyId = CompanyId, ShiftInstanceId = instance2.Id, UserId = UserId });

        _db.TimeOffRequests.Add(new TimeOffRequest
        {
            Id = 1,
            CompanyId = CompanyId,
            UserId = UserId,
            StartDate = day2,
            EndDate = day2,
            Status = RequestStatus.Approved,
            Type = TimeOffType.Vacation
        });

        await _db.SaveChangesAsync();

        var model = BuildModel();

        // Act: run the real production week-summary computation.
        await model.LoadCommonDataAsync(UserId, CompanyId, startOfWeek, startOfWeek, endOfWeek);

        model.ShiftsThisWeek.Should().Be(2, "only the two non-home/offline assignments are real shifts");
        model.DaysWithShiftsThisWeek.Should().Be(2, "the HOME-shift day must not count as a worked day");
        model.VacationDaysThisWeek.Should().Be(1, "the overlap day is vacation, not a shift day");
        model.OfflineDaysThisWeek.Should().Be(4);
        (model.DaysWithShiftsThisWeek + model.VacationDaysThisWeek + model.OfflineDaysThisWeek).Should().Be(7,
            "the three buckets must be disjoint and cover the full week");
        model.OfflineDaysThisWeek.Should().BeGreaterThanOrEqualTo(0, "offline must never go negative");
    }
}
