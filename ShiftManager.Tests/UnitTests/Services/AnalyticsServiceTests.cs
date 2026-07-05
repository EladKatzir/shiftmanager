using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Moq;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Services;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Services;

/// <summary>
/// Tests for <see cref="AnalyticsService"/> time-off analytics methods.
///
/// Verifies that fanned-out leave (multiple <see cref="TimeOffRequest"/> rows sharing a
/// non-null <see cref="TimeOffRequest.LeaveGroupId"/>) is counted ONCE per logical leave,
/// while ordinary single-company (null <see cref="TimeOffRequest.LeaveGroupId"/>) rows each
/// count individually.
///
/// AS-TF-01: GetTimeOffStatsAsync deduplicates fanned-out leaves — counts 2 logical requests
///            not 3 rows (two fan-out copies + one ordinary leave).
/// AS-TF-02: GetAverageDaysOffPerEmployeeAsync counts fanned-out leave's days once (3 days
///            for the fanned user) not twice (6 days), so the per-employee average is correct.
/// AS-TF-03: GetTimeOffByMonthAsync counts fanned-out leave's days once in the relevant month.
/// AS-TF-04: GetTimeOffStatsAsync with all-null LeaveGroupId rows behaves identically to today
///            (no regression for single-company users).
/// </summary>
public class AnalyticsServiceTests : IDisposable
{
    private readonly SqliteConnection _sqliteConnection;
    private readonly AppDbContext _db;
    private readonly AnalyticsService _sut;

    // Dates that fall comfortably within the 30-day / 12-month look-back windows
    private static readonly DateTime CreatedAt = DateTime.UtcNow.AddDays(-1);
    private static readonly DateOnly StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-5));
    private static readonly DateOnly EndDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-3));
    // 3 calendar days inclusive: EndDate.DayNumber - StartDate.DayNumber + 1 == 3

    public AnalyticsServiceTests()
    {
        // FK-off real SQLite: exercises the SQL translator (catches LINQ-translation bugs)
        // while not requiring parent Company/User rows for every FK.
        _sqliteConnection = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        _sqliteConnection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_sqliteConnection)
            .Options;

        // No ITenantResolver passed to AppDbContext → no query filter applied.
        // This lets the tests seed rows from different companies and have the service
        // see all of them (matching the scenario of a cross-tenant / molecule-wide analytics
        // view, or simply verifying the dedup logic against a set of mixed rows).
        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();

        var tenantResolverMock = new Mock<ITenantResolver>();
        tenantResolverMock.Setup(t => t.GetCurrentTenantId()).Returns(1);

        var cache = new MemoryCache(new MemoryCacheOptions());
        var logger = new Mock<ILogger<AnalyticsService>>();
        var localizationMock = new Mock<ICompanyLocalizationService>();

        _sut = new AnalyticsService(
            _db,
            tenantResolverMock.Object,
            cache,
            logger.Object,
            localizationMock.Object);
    }

    public void Dispose()
    {
        _db.Dispose();
        _sqliteConnection.Dispose();
    }

    // ─── helpers ────────────────────────────────────────────────────────────

    private void SeedActiveUser(int userId)
    {
        _db.Users.Add(new AppUser
        {
            Id = userId,
            CompanyId = 1,
            Email = $"user{userId}@test.local",
            DisplayName = $"User {userId}",
            IsActive = true
        });
    }

    private void AddLeaveRequest(
        int userId,
        int companyId,
        Guid? leaveGroupId,
        RequestStatus status,
        DateOnly startDate,
        DateOnly endDate)
    {
        _db.TimeOffRequests.Add(new TimeOffRequest
        {
            UserId = userId,
            CompanyId = companyId,
            LeaveGroupId = leaveGroupId,
            Status = status,
            StartDate = startDate,
            EndDate = endDate,
            Type = TimeOffType.Vacation,
            CreatedAt = CreatedAt
        });
    }

    // ─── AS-TF-01: GetTimeOffStatsAsync deduplicates ──────────────────────

    /// <summary>
    /// Seed:
    ///   • User 1: fanned leave — 2 rows, same non-null LeaveGroupId, both Approved, 3 days.
    ///   • User 2: ordinary single-company leave — 1 row, null LeaveGroupId, Approved, 2 days.
    /// Expected: TotalRequests == 2 (one logical fanned leave + one ordinary), ApprovedCount == 2.
    /// Before fix: TotalRequests == 3 (all three rows counted).
    /// </summary>
    [Fact]
    public async Task GetTimeOffStatsAsync_FannedLeave_IsCountedOnce()
    {
        // Arrange
        SeedActiveUser(1);
        SeedActiveUser(2);

        var groupId = Guid.NewGuid();
        // Fan-out: company 10 copy + company 20 copy, same logical leave
        AddLeaveRequest(userId: 1, companyId: 10, leaveGroupId: groupId,
            status: RequestStatus.Approved, startDate: StartDate, endDate: EndDate);
        AddLeaveRequest(userId: 1, companyId: 20, leaveGroupId: groupId,
            status: RequestStatus.Approved, startDate: StartDate, endDate: EndDate);
        // Ordinary single-company leave
        AddLeaveRequest(userId: 2, companyId: 10, leaveGroupId: null,
            status: RequestStatus.Approved,
            startDate: StartDate.AddDays(-2), endDate: StartDate.AddDays(-1));

        await _db.SaveChangesAsync();

        var rangeStart = DateOnly.FromDateTime(CreatedAt.AddDays(-1));
        var rangeEnd = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));

        // Act
        var stats = await _sut.GetTimeOffStatsAsync(rangeStart, rangeEnd);

        // Assert
        stats.TotalRequests.Should().Be(2,
            "the fanned leave (2 rows) counts as 1 logical request; plus 1 ordinary leave = 2 total");
        stats.ApprovedCount.Should().Be(2,
            "both logical leaves are Approved");
    }

    // ─── AS-TF-02: GetAverageDaysOffPerEmployeeAsync deduplicates ─────────

    /// <summary>
    /// Same seed as AS-TF-01 but asserts on average days.
    /// User 1: fanned leave 3 days (must count once, not twice).
    /// User 2: ordinary leave 2 days.
    /// 2 active users.
    /// Expected: totalDays == 3 + 2 == 5; employeeCount == 2; average == 2.50.
    /// Before fix: totalDays == 6 + 2 == 8; average == 4.00 (fanned leave double-counted).
    /// </summary>
    [Fact]
    public async Task GetAverageDaysOffPerEmployeeAsync_FannedLeave_DaysCountedOnce()
    {
        // Arrange
        SeedActiveUser(1);
        SeedActiveUser(2);

        var groupId = Guid.NewGuid();
        AddLeaveRequest(userId: 1, companyId: 10, leaveGroupId: groupId,
            status: RequestStatus.Approved, startDate: StartDate, endDate: EndDate);
        AddLeaveRequest(userId: 1, companyId: 20, leaveGroupId: groupId,
            status: RequestStatus.Approved, startDate: StartDate, endDate: EndDate);

        // Ordinary 2-day leave for user 2
        var singleStart = StartDate.AddDays(1);
        var singleEnd = StartDate.AddDays(2); // 2 calendar days
        AddLeaveRequest(userId: 2, companyId: 10, leaveGroupId: null,
            status: RequestStatus.Approved, startDate: singleStart, endDate: singleEnd);

        await _db.SaveChangesAsync();

        // Act
        var average = await _sut.GetAverageDaysOffPerEmployeeAsync(days: 30);

        // Assert: totalDays = 3 (fanned, once) + 2 (ordinary) = 5; employeeCount = 2; avg = 2.50
        average.Should().Be(2.50m,
            "fanned leave days must be counted once: (3 + 2) / 2 active employees = 2.50");
    }

    // ─── AS-TF-03: GetTimeOffByMonthAsync deduplicates ───────────────────

    /// <summary>
    /// Verifies that GetTimeOffByMonthAsync does not double-count days for a fanned leave.
    /// Seed one fanned leave (2 rows) with 3 days in the current month.
    /// Expected: the month bucket shows 3, not 6.
    /// </summary>
    [Fact]
    public async Task GetTimeOffByMonthAsync_FannedLeave_DaysCountedOnce()
    {
        // Arrange
        SeedActiveUser(1);

        var groupId = Guid.NewGuid();
        AddLeaveRequest(userId: 1, companyId: 10, leaveGroupId: groupId,
            status: RequestStatus.Approved, startDate: StartDate, endDate: EndDate);
        AddLeaveRequest(userId: 1, companyId: 20, leaveGroupId: groupId,
            status: RequestStatus.Approved, startDate: StartDate, endDate: EndDate);

        await _db.SaveChangesAsync();

        // Act
        var byMonth = await _sut.GetTimeOffByMonthAsync(months: 12);

        // Assert: the start month should have 3 days, not 6
        var monthKey = StartDate.ToString("yyyy-MM");
        byMonth.Should().ContainKey(monthKey);
        byMonth[monthKey].Should().Be(3,
            "fanned leave (2 rows) contributes 3 days once, not 6");
    }

    // ─── AS-TF-04: null-LeaveGroupId regression guard ─────────────────────

    /// <summary>
    /// Ensures that ordinary single-company leaves (LeaveGroupId == null) each continue
    /// to count individually — the dedup must not suppress or merge them.
    /// Seed: 3 separate approved leaves, all null LeaveGroupId, different users.
    /// Expected: TotalRequests == 3, ApprovedCount == 3.
    /// </summary>
    [Fact]
    public async Task GetTimeOffStatsAsync_NullLeaveGroupId_EachRowCountsIndividually()
    {
        // Arrange
        SeedActiveUser(1);
        SeedActiveUser(2);
        SeedActiveUser(3);

        var start = StartDate;
        var end = EndDate;

        AddLeaveRequest(userId: 1, companyId: 1, leaveGroupId: null,
            status: RequestStatus.Approved, startDate: start, endDate: end);
        AddLeaveRequest(userId: 2, companyId: 1, leaveGroupId: null,
            status: RequestStatus.Approved, startDate: start, endDate: end);
        AddLeaveRequest(userId: 3, companyId: 1, leaveGroupId: null,
            status: RequestStatus.Pending, startDate: start, endDate: end);

        await _db.SaveChangesAsync();

        var rangeStart = DateOnly.FromDateTime(CreatedAt.AddDays(-1));
        var rangeEnd = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));

        // Act
        var stats = await _sut.GetTimeOffStatsAsync(rangeStart, rangeEnd);

        // Assert
        stats.TotalRequests.Should().Be(3, "each null-LeaveGroupId row counts individually");
        stats.ApprovedCount.Should().Be(2);
        stats.PendingCount.Should().Be(1);
    }

    // ─── Issue 1: non-counting shifts excluded from analytics hours ───────

    private void SeedShiftAssignmentWithType(int userId, DateOnly date, TimeOnly start, TimeOnly end,
        string key, bool counts, int stId)
    {
        _db.ShiftTypes.Add(new ShiftType
        {
            Id = stId, Key = key, Start = start, End = end, MoleculeId = 1, CompanyId = 1,
            IsBlocking = true, CountsTowardHourLimits = counts
        });
        var inst = new ShiftInstance { CompanyId = 1, ShiftTypeId = stId, WorkDate = date, StaffingRequired = 1 };
        _db.ShiftInstances.Add(inst);
        _db.SaveChanges();
        _db.ShiftAssignments.Add(new ShiftAssignment { CompanyId = 1, UserId = userId, ShiftInstanceId = inst.Id, CreatedAt = DateTime.UtcNow });
        _db.SaveChanges();
    }

    /// <summary>
    /// A shift type with CountsTowardHourLimits = false contributes 0 hours to the employee-hours
    /// report, even though its assignment still exists. Owner decision: "counts toward hours" is
    /// ignored everywhere hours are summed.
    /// </summary>
    [Fact]
    public async Task GetEmployeeHoursAsync_NonCountingShift_ExcludedFromTotalHours()
    {
        SeedActiveUser(1);
        SeedShiftAssignmentWithType(1, StartDate, new TimeOnly(8, 0), new TimeOnly(16, 0), "MORNING_A", counts: true, stId: 5001);   // 8h, counts
        SeedShiftAssignmentWithType(1, StartDate.AddDays(1), new TimeOnly(9, 0), new TimeOnly(19, 0), "STATUS_A", counts: false, stId: 5002); // 10h, does NOT count
        await _db.SaveChangesAsync();

        var result = await _sut.GetEmployeeHoursAsync(StartDate.AddDays(-1), EndDate.AddDays(2));

        result.Should().ContainSingle(e => e.UserId == 1);
        result.Single(e => e.UserId == 1).TotalHours.Should().Be(8m, "only the counting shift's hours are summed");
    }
}
