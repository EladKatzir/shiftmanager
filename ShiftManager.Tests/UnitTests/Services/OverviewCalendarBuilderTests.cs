using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Moq;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Resources;
using ShiftManager.Services;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Services;

/// <summary>
/// Tests for <see cref="OverviewCalendarBuilder"/> — the shared "user rows x date columns"
/// calendar-build pipeline extracted from <c>OverviewModel</c> (Task #9.2) so both
/// /Calendar/Overview and the future /Calendar/Team page can render an identical calendar by
/// construction. Moved verbatim from OverviewModel's private
/// BuildOverviewCalendarAsync/LoadVacationsAsync/LoadShiftsAsync/LoadChoresAsync/
/// LoadOnDutiesAsync/BuildCellsForUser pipeline; this is the behavior-preservation gate for
/// that extraction (mirrors the real-SQLite fixture pattern established by
/// OverviewRosterAccountTypeTests / ShiftsUserRowShiftCountTests).
/// </summary>
public sealed class OverviewCalendarBuilderTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private AppDbContext _db = null!;
    private const int CompanyId = 1;

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

    private OverviewCalendarBuilder NewBuilder()
    {
        var localizer = new Mock<IStringLocalizer<SharedResources>>();
        localizer.Setup(l => l[It.IsAny<string>()]).Returns<string>(k => new LocalizedString(k, k));

        // Explicitly configured (not Mock.Of<T>() loose defaults) so BuildAsync's real code path
        // — which dereferences these results (dict.TryGetValue, string interpolation) — never
        // sees an unconfigured-mock null/ambiguous default.
        var companyLocalization = new Mock<ICompanyLocalizationService>();
        companyLocalization
            .Setup(s => s.ResolveShiftTypeNameAsync(It.IsAny<ShiftType>(), It.IsAny<int>(), It.IsAny<string>()))
            .ReturnsAsync((ShiftType st, int _, string __) => st.Key);

        var textEntryService = new Mock<ICalendarTextEntryService>();
        textEntryService
            .Setup(s => s.GetOverviewNotesForCompanyAsync(It.IsAny<int>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>()))
            .ReturnsAsync(new Dictionary<(int UserId, DateOnly Date), string>());
        textEntryService
            .Setup(s => s.GetForUsersAndDateRangeWithTypeAsync(It.IsAny<IEnumerable<int>>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>()))
            .ReturnsAsync(new Dictionary<(int UserId, DateOnly Date), List<(int Id, string Text, CalendarTextEntryType EntryType, int CompanyId)>>());

        return new OverviewCalendarBuilder(
            _db,
            textEntryService.Object,
            companyLocalization.Object,
            localizer.Object);
    }

    private static AppUser MakeUser(int id, string name) => new()
    {
        Id = id,
        CompanyId = CompanyId,
        Email = $"user{id}@test.com",
        DisplayName = name,
        Role = UserRole.Employee,
        IsActive = true,
        AccountType = AccountType.Standard
    };

    /// <summary>
    /// Core shape assertion from the task brief: one row per user, CalendarType == "overview".
    /// Also verifies the other fields BuildOverviewCalendarAsync used to set on CalendarData
    /// (RowMode, RowOrderContextKey, TotalRows, IsReadOnly) survived the extraction unchanged.
    /// </summary>
    [Fact]
    public async Task BuildAsync_ReturnsRowPerUser_WithOverviewShape()
    {
        var u1 = MakeUser(1, "Alice");
        var u2 = MakeUser(2, "Bob");
        var start = new DateOnly(2026, 7, 5); // arbitrary fixed Sunday
        var end = start.AddDays(6);

        var builder = NewBuilder();
        var vm = await builder.BuildAsync(CompanyId, new[] { u1, u2 }, start, end, "week", canEditNotes: false);

        vm.Rows.Should().HaveCount(2);
        vm.CalendarType.Should().Be("overview");
        vm.RowMode.Should().Be("Shifts");
        vm.RowOrderContextKey.Should().Be($"overview:{CompanyId}");
        vm.IsReadOnly.Should().BeTrue("canEditNotes was false");
        vm.StartDate.Should().Be(start);
        vm.EndDate.Should().Be(end);
        vm.ViewMode.Should().Be("week");
        // +1 for the <thead> column-header row (ARIA 1.2 §6.6.4) — same invariant as the
        // original BuildOverviewCalendarAsync.
        vm.TotalRows.Should().Be(vm.Rows.Count + 1);
    }

    [Fact]
    public async Task BuildAsync_CanEditNotesTrue_SetsIsReadOnlyFalse()
    {
        var u1 = MakeUser(1, "Alice");
        var start = new DateOnly(2026, 7, 5);
        var end = start.AddDays(6);

        var builder = NewBuilder();
        var vm = await builder.BuildAsync(CompanyId, new[] { u1 }, start, end, "week", canEditNotes: true);

        vm.IsReadOnly.Should().BeFalse("canEditNotes was true");
    }

    /// <summary>
    /// A real (non-home) shift assigned to a user on a specific day must appear as a "shift"-role
    /// assignment in that user's cell for that date, and no other date.
    /// </summary>
    [Fact]
    public async Task BuildAsync_SeededShift_AppearsInUserCellOnAssignedDate()
    {
        const int UserId = 1;
        var start = new DateOnly(2026, 7, 5);
        var end = start.AddDays(6);
        var shiftDate = start.AddDays(2);

        var shiftType = new ShiftType
        {
            Id = 1, CompanyId = CompanyId, Scope = ShiftScope.Company,
            Key = ShiftType.KEY_MORNING, Start = new TimeOnly(7, 0), End = new TimeOnly(15, 0)
        };
        _db.ShiftTypes.Add(shiftType);

        var instance = new ShiftInstance { Id = 1, CompanyId = CompanyId, ShiftTypeId = shiftType.Id, WorkDate = shiftDate };
        _db.ShiftInstances.Add(instance);

        _db.ShiftAssignments.Add(new ShiftAssignment { Id = 1, CompanyId = CompanyId, ShiftInstanceId = instance.Id, UserId = UserId });

        await _db.SaveChangesAsync();

        var user = MakeUser(UserId, "Alice");
        var builder = NewBuilder();
        var vm = await builder.BuildAsync(CompanyId, new[] { user }, start, end, "week", canEditNotes: false);

        var row = vm.Rows.Single(r => r.Id == $"user-{UserId}");

        row.Cells[shiftDate].Assignments.Should().ContainSingle(a => a.Role == "shift" && a.Name == ShiftType.KEY_MORNING);
        row.Cells[shiftDate].Assignments.Single().IsHome.Should().BeFalse();

        // No shift bled into an adjacent day.
        row.Cells[shiftDate.AddDays(1)].Assignments.Should().BeEmpty();
    }

    /// <summary>
    /// An approved vacation TimeOffRequest overlapping the range must set Overlay.HasVacation
    /// for every day it spans, and must NOT mark days outside its range.
    /// </summary>
    [Fact]
    public async Task BuildAsync_SeededApprovedVacation_SetsOverlayHasVacation()
    {
        const int UserId = 2;
        var start = new DateOnly(2026, 7, 5);
        var end = start.AddDays(6);
        var vacationDate = start.AddDays(4);

        _db.TimeOffRequests.Add(new TimeOffRequest
        {
            Id = 1,
            CompanyId = CompanyId,
            UserId = UserId,
            StartDate = vacationDate,
            EndDate = vacationDate,
            Type = TimeOffType.Vacation,
            Status = RequestStatus.Approved
        });
        await _db.SaveChangesAsync();

        var user = MakeUser(UserId, "Bob");
        var builder = NewBuilder();
        var vm = await builder.BuildAsync(CompanyId, new[] { user }, start, end, "week", canEditNotes: false);

        var row = vm.Rows.Single(r => r.Id == $"user-{UserId}");

        row.Cells[vacationDate].Overlay.Should().NotBeNull();
        row.Cells[vacationDate].Overlay!.HasVacation.Should().BeTrue();

        // An unaffected day must not carry the overlay.
        row.Cells[start].Overlay.Should().BeNull();
    }

    /// <summary>
    /// A PENDING (not yet approved) time-off request must NOT mark the day as vacation —
    /// mirrors the original LoadVacationsAsync's Status == RequestStatus.Approved filter.
    /// </summary>
    [Fact]
    public async Task BuildAsync_PendingVacation_DoesNotSetOverlayHasVacation()
    {
        const int UserId = 3;
        var start = new DateOnly(2026, 7, 5);
        var end = start.AddDays(6);
        var requestedDate = start.AddDays(1);

        _db.TimeOffRequests.Add(new TimeOffRequest
        {
            Id = 1,
            CompanyId = CompanyId,
            UserId = UserId,
            StartDate = requestedDate,
            EndDate = requestedDate,
            Type = TimeOffType.Vacation,
            Status = RequestStatus.Pending
        });
        await _db.SaveChangesAsync();

        var user = MakeUser(UserId, "Cara");
        var builder = NewBuilder();
        var vm = await builder.BuildAsync(CompanyId, new[] { user }, start, end, "week", canEditNotes: false);

        var row = vm.Rows.Single(r => r.Id == $"user-{UserId}");
        row.Cells[requestedDate].Overlay.Should().BeNull("the request is still Pending, not Approved");
    }

    /// <summary>
    /// Regression (cross-company HOME chip): HOME ShiftTypes are molecule-scoped, so ONE
    /// ShiftInstance is shared by every company in the molecule and carries whichever company first
    /// materialised it. A viewed-company user's HOME assignment (its own CompanyId) can point at an
    /// instance stamped with a DIFFERENT company. With the tenant query filter ACTIVE, referencing
    /// the required ShiftInstance nav must NOT drop that row — the per-user tenant axis is the
    /// assignment's CompanyId, not the shared instance's. Before the render-side fix, LoadShiftsAsync
    /// INNER-JOINed on the instance's company and the HOME busy-chip vanished on Overview/Team.
    /// This test constructs the context WITH a tenant resolver (filters on) — the other tests here
    /// use a null resolver, so they never exercised this path.
    /// </summary>
    [Fact]
    public async Task BuildAsync_HomeAssignment_SharedInstanceStampedOtherCompany_StillRenders()
    {
        const int ViewedCompany = 2;   // the board's (validated) company
        const int OtherCompany = 1;    // the company that first stamped the shared molecule instance
        const int UserId = 200;
        var start = new DateOnly(2026, 7, 5);
        var end = start.AddDays(6);
        var homeDate = start.AddDays(2);

        var tenant = new Mock<ITenantResolver>();
        tenant.Setup(t => t.GetCurrentTenantId()).Returns(ViewedCompany);

        using var connection = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        using var db = new AppDbContext(options, tenant.Object);
        await db.Database.EnsureCreatedAsync();

        db.Companies.AddRange(
            new Company { Id = OtherCompany, Name = "C1", Slug = "c1", MoleculeId = 1 },
            new Company { Id = ViewedCompany, Name = "C2", Slug = "c2", MoleculeId = 1 });
        var user = new AppUser
        {
            Id = UserId, CompanyId = ViewedCompany, Email = "u200@test.com",
            DisplayName = "Cross User", Role = UserRole.Employee, IsActive = true, AccountType = AccountType.Standard
        };
        db.Users.Add(user);
        db.ShiftTypes.Add(new ShiftType
        {
            Id = 500, Key = ShiftType.KEY_HOME, MoleculeId = 1, Scope = ShiftScope.Molecule,
            Start = new TimeOnly(0, 0), End = new TimeOnly(23, 59), NameEn = "Home", NameHe = "בית"
        });
        // Shared molecule instance stamped with the OTHER company (as the materialiser/rotation do).
        db.ShiftInstances.Add(new ShiftInstance
        {
            Id = 600, CompanyId = OtherCompany, WorkDate = homeDate, ShiftTypeId = 500, StaffingRequired = 99
        });
        // The viewed-company user's HOME assignment points at that shared instance.
        db.ShiftAssignments.Add(new ShiftAssignment
        {
            Id = 700, CompanyId = ViewedCompany, UserId = UserId, ShiftInstanceId = 600
        });
        await db.SaveChangesAsync();

        var localizer = new Mock<IStringLocalizer<SharedResources>>();
        localizer.Setup(l => l[It.IsAny<string>()]).Returns<string>(k => new LocalizedString(k, k));
        var companyLocalization = new Mock<ICompanyLocalizationService>();
        companyLocalization
            .Setup(s => s.ResolveShiftTypeNameAsync(It.IsAny<ShiftType>(), It.IsAny<int>(), It.IsAny<string>()))
            .ReturnsAsync((ShiftType st, int _, string __) => st.Key);
        var textEntryService = new Mock<ICalendarTextEntryService>();
        textEntryService
            .Setup(s => s.GetOverviewNotesForCompanyAsync(It.IsAny<int>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>()))
            .ReturnsAsync(new Dictionary<(int UserId, DateOnly Date), string>());
        textEntryService
            .Setup(s => s.GetForUsersAndDateRangeWithTypeAsync(It.IsAny<IEnumerable<int>>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>()))
            .ReturnsAsync(new Dictionary<(int UserId, DateOnly Date), List<(int Id, string Text, CalendarTextEntryType EntryType, int CompanyId)>>());

        var builder = new OverviewCalendarBuilder(db, textEntryService.Object, companyLocalization.Object, localizer.Object);

        var vm = await builder.BuildAsync(ViewedCompany, new[] { user }, start, end, "week", canEditNotes: false);

        var row = vm.Rows.Single(r => r.Id == $"user-{UserId}");
        row.Cells[homeDate].Assignments.Should().Contain(a => a.IsHome,
            "the HOME assignment's tenant is its own CompanyId (viewed company), even though the shared molecule instance is stamped with another company");
    }
}
