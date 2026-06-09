using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Migrations;
using ShiftManager.Models;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Migrations;

/// <summary>
/// Regression guard for the <c>BackfillCompanyMembershipsFromPrimary</c> migration.
/// Runs the EXACT SQL the migration ships (<see cref="CompanyMembershipBackfillSql.Forward"/>)
/// against a real SQLite engine with the production schema.
///
/// Uses Foreign Keys=False so the test does NOT need to seed Companies/RoleTemplates — the
/// same pattern used by ~53 fixtures in this repo (per MEMORY.md migration history).
/// </summary>
public sealed class CompanyMembershipBackfillTests : IAsyncLifetime
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

    [Fact]
    public async Task Forward_CreatesExactlyOnePrimaryMembershipPerUser()
    {
        // Seed two users with different company/role/dept data.
        // User 1: company 10, RoleTemplateId=2, JobTypeId=3, DoesShifts=true, HomeTypeId=5.
        // User 2: company 20, DepartmentId=7, DoesShifts=false (all others null).
        _db.Users.AddRange(
            new AppUser { Id = 1, CompanyId = 10, Email = "user1@test.mil", DisplayName = "User One",
                          RoleTemplateId = 2, JobTypeId = 3, DoesShifts = true, HomeTypeId = 5 },
            new AppUser { Id = 2, CompanyId = 20, Email = "user2@test.mil", DisplayName = "User Two",
                          DepartmentId = 7, DoesShifts = false });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        // Run the exact backfill SQL the migration ships.
        foreach (var sql in CompanyMembershipBackfillSql.Forward)
            await _db.Database.ExecuteSqlRawAsync(sql);

        var memberships = await _db.CompanyMemberships
            .IgnoreQueryFilters()
            .OrderBy(m => m.UserId)
            .ToListAsync();

        // Exactly one primary membership per user.
        memberships.Should().HaveCount(2, "one primary membership per seeded user");
        memberships.Should().OnlyContain(m => m.IsPrimary, "backfill only creates primary rows");
        memberships.Should().OnlyContain(m => !m.IsDeleted, "backfill rows are not soft-deleted");

        // User 1 assertions.
        var m1 = memberships.Single(m => m.UserId == 1);
        m1.CompanyId.Should().Be(10);
        m1.RoleTemplateId.Should().Be(2);
        m1.JobTypeId.Should().Be(3);
        m1.DoesShifts.Should().BeTrue();
        m1.HomeTypeId.Should().Be(5);
        m1.DepartmentId.Should().BeNull();
        Assert.Equal(0, m1.GrantedBy);

        // User 2 assertions.
        var m2 = memberships.Single(m => m.UserId == 2);
        m2.CompanyId.Should().Be(20);
        m2.DepartmentId.Should().Be(7);
        m2.DoesShifts.Should().BeFalse();
        m2.RoleTemplateId.Should().BeNull();
        m2.JobTypeId.Should().BeNull();
        m2.HomeTypeId.Should().BeNull();
        Assert.Equal(0, m2.GrantedBy);
    }

    [Fact]
    public async Task Forward_IsIdempotent_NoDuplicatePrimaries()
    {
        // Seed one user.
        _db.Users.Add(new AppUser { Id = 1, CompanyId = 10, Email = "user@test.mil", DisplayName = "User" });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        // Run the forward SQL TWICE.
        foreach (var sql in CompanyMembershipBackfillSql.Forward)
            await _db.Database.ExecuteSqlRawAsync(sql);
        foreach (var sql in CompanyMembershipBackfillSql.Forward)
            await _db.Database.ExecuteSqlRawAsync(sql);

        var primaryCount = await _db.CompanyMemberships
            .IgnoreQueryFilters()
            .CountAsync(m => m.UserId == 1 && m.IsPrimary && !m.IsDeleted);

        primaryCount.Should().Be(1, "the NOT EXISTS guard makes a re-run a no-op");
    }
}
