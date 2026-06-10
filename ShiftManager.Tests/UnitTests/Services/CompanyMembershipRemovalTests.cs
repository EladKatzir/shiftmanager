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
/// TDD tests for the Epic 4 company-scoped removal methods:
/// <see cref="ICompanyMembershipService.GetRemovalImpactAsync"/> and
/// <see cref="ICompanyMembershipService.RemoveMembershipWithCleanupAsync"/>.
///
/// Harness: real-SQLite FK-off (DataSource=:memory:;Foreign Keys=False),
/// matching the pattern in <see cref="CompanyMembershipServiceTests"/>.
/// </summary>
public sealed class CompanyMembershipRemovalTests : IAsyncLifetime
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

    /// <summary>
    /// Seeds the canonical test fixture:
    ///   - User 1 with primary membership in company 10, additional membership in company 20.
    ///   - Company-20-scoped records: future ShiftAssignment, pending TimeOffRequest, Grant.
    ///   - Company-10-scoped equivalents (to prove they survive).
    /// </summary>
    private async Task SeedFixtureAsync()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var futureDate = today.AddDays(7);

        // Users
        _db.Users.Add(new AppUser { Id = 1, CompanyId = 10, Email = "u1@x", DisplayName = "U1" });

        // Memberships
        _db.CompanyMemberships.Add(new CompanyMembership
        {
            Id = 100, UserId = 1, CompanyId = 10, IsPrimary = true, GrantedBy = 0
        });
        _db.CompanyMemberships.Add(new CompanyMembership
        {
            Id = 200, UserId = 1, CompanyId = 20, IsPrimary = false, GrantedBy = 99
        });

        // --- Company-20 records ---

        // ShiftInstance for company 20 (future)
        _db.ShiftInstances.Add(new ShiftInstance
        {
            Id = 1001, CompanyId = 20, ShiftTypeId = 1, WorkDate = futureDate,
            Name = "Morning20", StaffingRequired = 1
        });
        // ShiftAssignment for company 20, user 1
        _db.ShiftAssignments.Add(new ShiftAssignment
        {
            Id = 2001, CompanyId = 20, UserId = 1, ShiftInstanceId = 1001
        });

        // TimeOffRequest for company 20, user 1 (Pending)
        _db.TimeOffRequests.Add(new TimeOffRequest
        {
            Id = 3001, CompanyId = 20, UserId = 1,
            StartDate = futureDate, EndDate = futureDate.AddDays(1),
            Status = RequestStatus.Pending
        });

        // Grant for company 20, user 1
        _db.Grants.Add(new Grant
        {
            Id = 4001, UserId = 1, GrantTypeId = 1, CompanyId = 20, CanOwn = true
        });

        // --- Company-10 records (must survive) ---

        // ShiftInstance for company 10 (future)
        _db.ShiftInstances.Add(new ShiftInstance
        {
            Id = 1002, CompanyId = 10, ShiftTypeId = 1, WorkDate = futureDate,
            Name = "Morning10", StaffingRequired = 1
        });
        // ShiftAssignment for company 10, user 1
        _db.ShiftAssignments.Add(new ShiftAssignment
        {
            Id = 2002, CompanyId = 10, UserId = 1, ShiftInstanceId = 1002
        });

        // TimeOffRequest for company 10, user 1 (Pending)
        _db.TimeOffRequests.Add(new TimeOffRequest
        {
            Id = 3002, CompanyId = 10, UserId = 1,
            StartDate = futureDate, EndDate = futureDate.AddDays(1),
            Status = RequestStatus.Pending
        });

        // Grant for company 10, user 1
        _db.Grants.Add(new Grant
        {
            Id = 4002, UserId = 1, GrantTypeId = 1, CompanyId = 10, CanOwn = true
        });

        await _db.SaveChangesAsync();
    }

    private static CompanyMembershipService CreateSut(AppDbContext db)
        => new CompanyMembershipService(db, NullLogger<CompanyMembershipService>.Instance);

    // ─────────────────────────────────────────────────────────────────────────
    // Test 1: GetRemovalImpactAsync returns correct counts for company 20
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetRemovalImpactAsync_Company20_ReturnsCountsMatchingSeededRecords()
    {
        // Arrange
        await SeedFixtureAsync();
        var sut = CreateSut(_db);

        // Act
        var impact = await sut.GetRemovalImpactAsync(1, 20);

        // Assert — only non-zero fields we seeded
        impact.FutureShifts.Should().Be(1, "one future ShiftAssignment seeded in company 20");
        impact.PendingOrFutureTimeOff.Should().Be(1, "one pending TimeOffRequest seeded in company 20");
        impact.GrantsRemoved.Should().Be(1, "one Grant seeded in company 20");

        // Chores + SwapRequests were not seeded — counts should be zero
        impact.FutureChores.Should().Be(0);
        impact.OpenSwapRequests.Should().Be(0);

        // OnDuty has no CompanyId — always 0 in company-scoped impact
        impact.FutureOnDuty.Should().Be(0);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 2: GetRemovalImpactAsync does NOT mutate data (read-only)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetRemovalImpactAsync_IsReadOnly_DoesNotMutateData()
    {
        // Arrange
        await SeedFixtureAsync();
        var sut = CreateSut(_db);

        // Act — call the impact method
        await sut.GetRemovalImpactAsync(1, 20);

        // Assert — all company-20 records still exist untouched
        var assignments = await _db.ShiftAssignments.IgnoreQueryFilters()
            .CountAsync(sa => sa.UserId == 1 && sa.CompanyId == 20);
        assignments.Should().Be(1, "GetRemovalImpactAsync must not delete records");

        var timeOff = await _db.TimeOffRequests.IgnoreQueryFilters()
            .CountAsync(t => t.UserId == 1 && t.CompanyId == 20);
        timeOff.Should().Be(1, "GetRemovalImpactAsync must not cancel time-off requests");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 3: RemoveMembershipWithCleanupAsync removes company-20 records
    //         and soft-deletes the membership, leaving company-10 intact
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RemoveMembershipWithCleanupAsync_Company20_CleansUpAndSoftDeletesMembership()
    {
        // Arrange
        await SeedFixtureAsync();
        var sut = CreateSut(_db);

        // Act
        var result = await sut.RemoveMembershipWithCleanupAsync(1, 20, actingAdminId: 99);

        // Assert — method returned true
        result.Should().BeTrue("removing a non-primary membership must succeed");

        // Assert — company-20 membership is soft-deleted
        (await sut.IsMemberAsync(1, 20)).Should().BeFalse("company-20 membership must be soft-deleted");

        // Assert — company-20 ShiftAssignment deleted
        var shiftsCo20 = await _db.ShiftAssignments.IgnoreQueryFilters()
            .CountAsync(sa => sa.UserId == 1 && sa.CompanyId == 20);
        shiftsCo20.Should().Be(0, "future shifts in company 20 must be deleted");

        // Assert — company-20 TimeOffRequest canceled.
        // Use AsNoTracking() to bypass the EF change tracker (ExecuteUpdateAsync writes
        // directly to SQLite, bypassing tracked entity state — same pattern as UserCompanyTransferServiceTests).
        var timeOffCo20 = await _db.TimeOffRequests.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == 3001);
        timeOffCo20.Should().NotBeNull();
        timeOffCo20!.Status.Should().Be(RequestStatus.Canceled,
            "pending time-off in company 20 must be canceled");

        // Assert — company-20 Grant deleted
        var grantsCo20 = await _db.Grants.IgnoreQueryFilters()
            .CountAsync(g => g.UserId == 1 && g.CompanyId == 20);
        grantsCo20.Should().Be(0, "grants in company 20 must be deleted");

        // Assert — company-10 primary membership is INTACT
        (await sut.IsMemberAsync(1, 10)).Should().BeTrue("primary company-10 membership must survive");

        // Assert — company-10 ShiftAssignment is INTACT
        var shiftsCo10 = await _db.ShiftAssignments.IgnoreQueryFilters()
            .CountAsync(sa => sa.UserId == 1 && sa.CompanyId == 10);
        shiftsCo10.Should().Be(1, "shift in company 10 must not be touched");

        // Assert — company-10 TimeOffRequest is INTACT (AsNoTracking for same reason as above)
        var timeOffCo10 = await _db.TimeOffRequests.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == 3002);
        timeOffCo10.Should().NotBeNull();
        timeOffCo10!.Status.Should().NotBe(RequestStatus.Canceled,
            "time-off in company 10 must not be touched");

        // Assert — company-10 Grant is INTACT
        var grantsCo10 = await _db.Grants.IgnoreQueryFilters()
            .CountAsync(g => g.UserId == 1 && g.CompanyId == 10);
        grantsCo10.Should().Be(1, "grants in company 10 must not be touched");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 4: RemoveMembershipWithCleanupAsync refuses to remove primary membership
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RemoveMembershipWithCleanupAsync_PrimaryMembership_ThrowsAndClearsNothing()
    {
        // Arrange
        await SeedFixtureAsync();
        var sut = CreateSut(_db);

        // Act & Assert — must throw
        var act = async () => await sut.RemoveMembershipWithCleanupAsync(1, 10, actingAdminId: 99);
        await act.Should().ThrowAsync<InvalidOperationException>(
            "attempting to remove the primary membership must be rejected");

        // Assert — no cleanup occurred: company-10 records are intact
        (await sut.IsMemberAsync(1, 10)).Should().BeTrue("primary membership must remain active");

        var shiftsCo10 = await _db.ShiftAssignments.IgnoreQueryFilters()
            .CountAsync(sa => sa.UserId == 1 && sa.CompanyId == 10);
        shiftsCo10.Should().Be(1, "company-10 shift must not be deleted when primary removal is attempted");

        var grantsCo10 = await _db.Grants.IgnoreQueryFilters()
            .CountAsync(g => g.UserId == 1 && g.CompanyId == 10);
        grantsCo10.Should().Be(1, "company-10 grants must not be deleted when primary removal is attempted");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 5: RemoveMembershipWithCleanupAsync on non-existent membership returns false
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RemoveMembershipWithCleanupAsync_NonExistentMembership_ReturnsFalse()
    {
        // Arrange
        await SeedFixtureAsync();
        var sut = CreateSut(_db);

        // Act — company 999 has no membership for user 1
        var result = await sut.RemoveMembershipWithCleanupAsync(1, 999, actingAdminId: 99);

        // Assert
        result.Should().BeFalse("removing a membership that does not exist must return false");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 6: GetRemovalImpactAsync on non-existent membership returns zero impact
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetRemovalImpactAsync_NonExistentMembership_ReturnsZeroImpact()
    {
        // Arrange
        await SeedFixtureAsync();
        var sut = CreateSut(_db);

        // Act
        var impact = await sut.GetRemovalImpactAsync(1, 999);

        // Assert — all zeros for a non-existent company
        impact.FutureShifts.Should().Be(0);
        impact.PendingOrFutureTimeOff.Should().Be(0);
        impact.FutureChores.Should().Be(0);
        impact.OpenSwapRequests.Should().Be(0);
        impact.FutureOnDuty.Should().Be(0);
        impact.GrantsRemoved.Should().Be(0);
    }
}
