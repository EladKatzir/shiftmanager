using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Services;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Services;

/// <summary>
/// Unit tests for <see cref="CompanyMembershipService"/>.
/// Uses the FK-off SQLite in-memory harness (same as CompanyMembershipBackfillTests) so we
/// don't need to seed Companies/RoleTemplates parent rows.
/// </summary>
public sealed class CompanyMembershipServiceTests : IAsyncLifetime
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

    /// <summary>Seeds a user with their primary membership (mirrors backfill output).</summary>
    private async Task SeedUserAsync(int userId, int companyId)
    {
        _db.Users.Add(new AppUser { Id = userId, CompanyId = companyId, Email = $"u{userId}@x", DisplayName = $"U{userId}" });
        _db.CompanyMemberships.Add(new CompanyMembership { UserId = userId, CompanyId = companyId, IsPrimary = true });
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task GetMembershipsAsync_ReturnsActiveMembershipsForUser()
    {
        // Arrange
        await SeedUserAsync(1, 10);
        var sut = new CompanyMembershipService(_db);
        await sut.AddMembershipAsync(1, 20, null, null, null, doesShifts: true, null, actingAdminId: 99);

        // Act
        var result = await sut.GetMembershipsAsync(1);

        // Assert
        result.Should().HaveCount(2, "user has primary in company 10 and an added membership in company 20");
        result.Should().Contain(m => m.CompanyId == 10 && m.IsPrimary);
        result.Should().Contain(m => m.CompanyId == 20 && !m.IsPrimary);
    }

    [Fact]
    public async Task AddMembershipAsync_DuplicateActiveCompany_Throws()
    {
        // Arrange
        await SeedUserAsync(1, 10);
        var sut = new CompanyMembershipService(_db);

        // Act & Assert
        var act = async () => await sut.AddMembershipAsync(1, 10, null, null, null, doesShifts: false, null, actingAdminId: 99);
        await act.Should().ThrowAsync<InvalidOperationException>(
            "adding a second active membership to the same company must be rejected");
    }

    [Fact]
    public async Task IsMemberAsync_TrueOnlyForActiveMembershipCompanies()
    {
        // Arrange
        await SeedUserAsync(1, 10);
        var sut = new CompanyMembershipService(_db);

        // Act & Assert
        (await sut.IsMemberAsync(1, 10)).Should().BeTrue("user has an active primary membership in company 10");
        (await sut.IsMemberAsync(1, 999)).Should().BeFalse("user has no membership in company 999");
    }

    [Fact]
    public async Task SetPrimaryAsync_PromotesTargetAndSyncsAppUserCompanyId()
    {
        // Arrange
        await SeedUserAsync(1, 10);
        var sut = new CompanyMembershipService(_db);
        await sut.AddMembershipAsync(1, 20, null, null, null, doesShifts: false, null, actingAdminId: 99);

        // Act
        await sut.SetPrimaryAsync(1, 20);

        // Assert — membership state
        var memberships = await _db.CompanyMemberships
            .IgnoreQueryFilters()
            .Where(m => m.UserId == 1 && !m.IsDeleted)
            .ToListAsync();

        memberships.Count(m => m.IsPrimary).Should().Be(1, "exactly one primary must exist after promotion");
        memberships.Single(m => m.IsPrimary).CompanyId.Should().Be(20, "company 20 is now primary");
        memberships.Single(m => m.CompanyId == 10).IsPrimary.Should().BeFalse("company 10 must be demoted");

        // Assert — AppUser.CompanyId synced
        var user = await _db.Users.IgnoreQueryFilters().SingleAsync(u => u.Id == 1);
        user.CompanyId.Should().Be(20, "AppUser.CompanyId must mirror the new primary");
    }

    [Fact]
    public async Task SetPrimaryAsync_NonMemberCompany_Throws()
    {
        // Arrange
        await SeedUserAsync(1, 10);
        var sut = new CompanyMembershipService(_db);

        // Act & Assert
        var act = async () => await sut.SetPrimaryAsync(1, 777);
        await act.Should().ThrowAsync<InvalidOperationException>(
            "promoting a company the user has no active membership in must be rejected");
    }

    [Fact]
    public async Task RemoveMembershipAsync_SoftDeletesAndRemovesFromActiveSet()
    {
        // Arrange
        await SeedUserAsync(1, 10);
        var svc = new CompanyMembershipService(_db);
        var m = await svc.AddMembershipAsync(1, 20, null, null, null, false, null, 99);

        // Act
        await svc.RemoveMembershipAsync(m.Id, 99);

        // Assert — active set no longer contains company 20
        var active = await svc.GetMembershipsAsync(1);
        active.Should().ContainSingle(x => x.CompanyId == 10, "only the company-10 primary remains active");
        active.Should().NotContain(x => x.CompanyId == 20, "the removed membership is no longer active");
        (await svc.IsMemberAsync(1, 20)).Should().BeFalse("removed membership is not an active membership");

        // Assert — the row is soft-deleted, not hard-deleted
        var removed = _db.CompanyMemberships.IgnoreQueryFilters().First(x => x.Id == m.Id);
        removed.IsDeleted.Should().BeTrue("removal is a soft-delete");
        removed.DeletedAt.Should().NotBeNull("soft-delete stamps DeletedAt");
    }

    [Fact]
    public async Task RemoveMembershipAsync_PrimaryMembership_Throws()
    {
        // Arrange
        await SeedUserAsync(1, 10);
        var svc = new CompanyMembershipService(_db);
        var primaryId = (await svc.GetMembershipsAsync(1)).Single(x => x.IsPrimary).Id;

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.RemoveMembershipAsync(primaryId, 99));
    }
}
