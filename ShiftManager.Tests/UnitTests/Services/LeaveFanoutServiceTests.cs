using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Services;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Services;

/// <summary>
/// Unit tests for <see cref="LeaveFanoutService"/>.
/// Uses the FK-off real SQLite harness so EF query translation is exercised exactly as
/// in production, without needing to seed parent Company/Department rows.
/// </summary>
public sealed class LeaveFanoutServiceTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private AppDbContext _db = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        await _connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;
        _db = new AppDbContext(options);
        await _db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    // ─── helpers ────────────────────────────────────────────────────────────

    private LeaveFanoutService CreateSut() =>
        new LeaveFanoutService(_db, new CompanyMembershipService(_db, NullLogger<CompanyMembershipService>.Instance));

    /// <summary>
    /// Seeds a user and one or more memberships. The first entry is treated as the primary.
    /// Each tuple is (companyId, doesShifts).
    /// </summary>
    private async Task SeedUserWithMembershipsAsync(int userId, params (int CompanyId, bool DoesShifts)[] memberships)
    {
        var primaryCompanyId = memberships[0].CompanyId;
        _db.Users.Add(new AppUser
        {
            Id = userId,
            CompanyId = primaryCompanyId,
            Email = $"user{userId}@test.local",
            DisplayName = $"User {userId}"
        });

        for (var i = 0; i < memberships.Length; i++)
        {
            var (companyId, doesShifts) = memberships[i];
            _db.CompanyMemberships.Add(new CompanyMembership
            {
                UserId = userId,
                CompanyId = companyId,
                IsPrimary = i == 0,
                DoesShifts = doesShifts,
                GrantedBy = 0
            });
        }

        await _db.SaveChangesAsync();
    }

    private static TimeOffRequest BuildPrimaryRequest(int userId, int companyId) => new TimeOffRequest
    {
        UserId = userId,
        CompanyId = companyId,
        StartDate = new DateOnly(2026, 7, 1),
        EndDate = new DateOnly(2026, 7, 5),
        Type = TimeOffType.Vacation,
        Reason = "summer",
        Status = RequestStatus.Pending,
        CreatedAt = DateTime.UtcNow
    };

    // ─── tests ──────────────────────────────────────────────────────────────

    /// <summary>
    /// User with shift-memberships in companies 10 (primary), 20, 30.
    /// Fan-out should produce 2 clones (one per additional company).
    /// All 3 rows share one non-null LeaveGroupId, each has the correct CompanyId and same dates.
    /// </summary>
    [Fact]
    public async Task FanOutAsync_MultiShiftCompanies_CreatesClones_WithSharedGroupId()
    {
        // Arrange
        const int userId = 1;
        await SeedUserWithMembershipsAsync(userId,
            (CompanyId: 10, DoesShifts: true),
            (CompanyId: 20, DoesShifts: true),
            (CompanyId: 30, DoesShifts: true));

        var primary = BuildPrimaryRequest(userId, companyId: 10);
        _db.TimeOffRequests.Add(primary);
        await _db.SaveChangesAsync();

        var sut = CreateSut();

        // Act
        var (groupId, clones) = await sut.FanOutAsync(primary, userId);

        // Assert — return values
        groupId.Should().NotBeNull("fan-out must assign a LeaveGroupId");
        clones.Should().HaveCount(2, "one clone per additional shift-company (20 and 30)");

        // Assert — primary was stamped
        primary.LeaveGroupId.Should().Be(groupId,
            "the primary must share the same LeaveGroupId as the clones");

        // Assert — clones have distinct, correct company ids
        var cloneCompanyIds = clones.Select(c => c.CompanyId).OrderBy(x => x).ToList();
        cloneCompanyIds.Should().BeEquivalentTo(new[] { 20, 30 },
            "one clone for each additional shift-company");

        // Assert — all three rows exist in the database and share the group id
        var allRows = await _db.TimeOffRequests
            .IgnoreQueryFilters()
            .Where(r => r.UserId == userId)
            .ToListAsync();
        allRows.Should().HaveCount(3);
        allRows.Should().AllSatisfy(r =>
            r.LeaveGroupId.Should().Be(groupId, "every row must carry the shared LeaveGroupId"));

        // Assert — dates and type are faithfully copied
        foreach (var clone in clones)
        {
            clone.StartDate.Should().Be(primary.StartDate);
            clone.EndDate.Should().Be(primary.EndDate);
            clone.Type.Should().Be(primary.Type);
            clone.Reason.Should().Be(primary.Reason);
            clone.Status.Should().Be(primary.Status);
            clone.ApproverId.Should().BeNull("each company routes its own approver");
        }
    }

    /// <summary>
    /// User who does shifts in only ONE company → FanOutAsync returns (null, empty);
    /// primary.LeaveGroupId stays null; no extra rows are created.
    /// </summary>
    [Fact]
    public async Task FanOutAsync_SingleShiftCompany_NoFanOut_LeaveGroupIdNull()
    {
        // Arrange
        const int userId = 2;
        await SeedUserWithMembershipsAsync(userId,
            (CompanyId: 10, DoesShifts: true));  // only one shift-company

        var primary = BuildPrimaryRequest(userId, companyId: 10);
        _db.TimeOffRequests.Add(primary);
        await _db.SaveChangesAsync();

        var sut = CreateSut();

        // Act
        var (groupId, clones) = await sut.FanOutAsync(primary, userId);

        // Assert
        groupId.Should().BeNull("single-company user must not get a LeaveGroupId");
        clones.Should().BeEmpty("no clones should be created");
        primary.LeaveGroupId.Should().BeNull("primary must be untouched for single-company users");

        var rowCount = await _db.TimeOffRequests
            .IgnoreQueryFilters()
            .CountAsync(r => r.UserId == userId);
        rowCount.Should().Be(1, "only the original primary row should exist");
    }

    /// <summary>
    /// A membership with DoesShifts == false in another company must NOT receive a clone.
    /// Only DoesShifts == true memberships are fanned into.
    /// </summary>
    [Fact]
    public async Task FanOutAsync_NonShiftMembership_IsNotFannedInto()
    {
        // Arrange
        const int userId = 3;
        await SeedUserWithMembershipsAsync(userId,
            (CompanyId: 10, DoesShifts: true),   // primary, does shifts
            (CompanyId: 20, DoesShifts: false),  // member but does NOT do shifts
            (CompanyId: 30, DoesShifts: true));  // does shifts → should get a clone

        var primary = BuildPrimaryRequest(userId, companyId: 10);
        _db.TimeOffRequests.Add(primary);
        await _db.SaveChangesAsync();

        var sut = CreateSut();

        // Act
        var (groupId, clones) = await sut.FanOutAsync(primary, userId);

        // Assert — only one clone (for company 30); company 20 is excluded
        groupId.Should().NotBeNull();
        clones.Should().HaveCount(1, "only DoesShifts companies get a copy");
        clones[0].CompanyId.Should().Be(30, "company 30 is the only additional shift-company");

        var allRows = await _db.TimeOffRequests
            .IgnoreQueryFilters()
            .Where(r => r.UserId == userId)
            .ToListAsync();
        allRows.Should().HaveCount(2, "primary + one clone (company 20 excluded)");
        allRows.Should().NotContain(r => r.CompanyId == 20, "non-shift membership must not receive a copy");
    }
}
