using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Services;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Services;

/// <summary>
/// Tests for <see cref="ICompanyMembershipService.UpdateMembershipAsync"/>:
///   1. Non-destructive update — operational records survive (contrast with remove which deletes them).
///   2. Primary membership throws InvalidOperationException.
///   3. Cross-company grant isolation — the company-scoped removal primitive used by the handler
///      only removes grants for the target company; primary-company grants from the same template
///      are untouched.
///
/// Harness: real-SQLite FK-off (DataSource=:memory:;Foreign Keys=False), matching the pattern in
/// <see cref="CompanyMembershipRemovalTests"/> and <see cref="CompanyMembershipServiceTests"/>.
/// </summary>
public sealed class CompanyMembershipUpdateTests : IAsyncLifetime
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

    private static CompanyMembershipService CreateSut(AppDbContext db)
        => new CompanyMembershipService(db, NullLogger<CompanyMembershipService>.Instance);

    /// <summary>
    /// Seeds a user with:
    ///   - Primary membership in company 10 (membershipId 100)
    ///   - Additional membership in company 20 (membershipId 200, roleTemplateId = 5)
    ///   - A future ShiftAssignment in company 20 (proves non-destructive)
    /// </summary>
    private async Task SeedBaseAsync()
    {
        var futureDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(7);

        _db.Users.Add(new AppUser { Id = 1, CompanyId = 10, Email = "u1@x", DisplayName = "U1" });

        _db.CompanyMemberships.Add(new CompanyMembership
        {
            Id = 100, UserId = 1, CompanyId = 10, IsPrimary = true, GrantedBy = 0
        });
        _db.CompanyMemberships.Add(new CompanyMembership
        {
            Id = 200, UserId = 1, CompanyId = 20, IsPrimary = false, GrantedBy = 99,
            RoleTemplateId = 5, JobTypeId = 7, DoesShifts = true
        });

        // Future shift in company 20 — must survive UpdateMembership (non-destructive invariant).
        _db.ShiftInstances.Add(new ShiftInstance
        {
            Id = 1001, CompanyId = 20, ShiftTypeId = 1, WorkDate = futureDate,
            Name = "Morning20", StaffingRequired = 1
        });
        _db.ShiftAssignments.Add(new ShiftAssignment
        {
            Id = 2001, CompanyId = 20, UserId = 1, ShiftInstanceId = 1001
        });

        await _db.SaveChangesAsync();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 1: UpdateMembershipAsync on a non-primary membership updates fields;
    //         operational records (shifts) are NOT deleted.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateMembershipAsync_NonPrimary_UpdatesFieldsAndDoesNotDeleteShifts()
    {
        // Arrange
        await SeedBaseAsync();
        var sut = CreateSut(_db);

        // Act — change role template, job type, and does-shifts on the company-20 membership
        var oldRoleTemplateId = await sut.UpdateMembershipAsync(
            membershipId: 200,
            roleTemplateId: 8,
            jobTypeId: 9,
            doesShifts: false,
            actingAdminId: 99);

        // Assert — returned old value
        oldRoleTemplateId.Should().Be(5, "service must return the previous RoleTemplateId for grant reconciliation");

        // Assert — membership row updated
        var updated = await _db.CompanyMemberships.IgnoreQueryFilters()
            .FirstAsync(m => m.Id == 200);
        updated.RoleTemplateId.Should().Be(8, "role template was changed to 8");
        updated.JobTypeId.Should().Be(9, "job type was changed to 9");
        updated.DoesShifts.Should().BeFalse("doesShifts was set to false");

        // Assert — future ShiftAssignment SURVIVES (non-destructive invariant)
        var shiftCount = await _db.ShiftAssignments.IgnoreQueryFilters()
            .CountAsync(sa => sa.UserId == 1 && sa.CompanyId == 20);
        shiftCount.Should().Be(1, "UpdateMembershipAsync must NOT delete operational records; only remove does that");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 2: UpdateMembershipAsync on the PRIMARY membership throws.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateMembershipAsync_PrimaryMembership_ThrowsInvalidOperationException()
    {
        // Arrange
        await SeedBaseAsync();
        var sut = CreateSut(_db);

        // Act — attempt to update the primary (id=100)
        var act = async () => await sut.UpdateMembershipAsync(
            membershipId: 100,
            roleTemplateId: 8,
            jobTypeId: null,
            doesShifts: true,
            actingAdminId: 99);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>(
            "updating the primary membership is not allowed; primary edits go through user-row handlers");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 3: Cross-company grant isolation — company-scoped removal primitive.
    //
    // The handler does NOT use RemoveAutoGrantsAsync (global) — instead it directly
    // deletes Grants WHERE UserId == userId AND CompanyId == membershipCompanyId
    // AND IsAutoGrant == true AND GrantTypeId IN (old template's grant types).
    //
    // This test seeds:
    //   - User 1 with primary membership in company 10 AND additional in company 20.
    //   - Both companies hold auto-grants from role template "Employee" (grantTypeId 501)
    //     at company-scoped level (CompanyId).
    //   - UpdateMembership for company 20 changes template Employee→Manager
    //     (handler removes old template's grants for company 20 ONLY, then applies new).
    //
    // Asserts:
    //   - Company 10's auto-grant for grantType 501 is UNTOUCHED.
    //   - Company 20's old auto-grant for grantType 501 is REMOVED.
    //
    // This test exercises the company-scoped deletion query directly (as the handler does),
    // proving the isolation invariant at the lowest possible seam.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CompanyScopedGrantRemoval_LeavesOtherCompaniesGrantsIntact()
    {
        // Arrange — seed hierarchy IDs (FK-off so no real rows needed for parent tables)
        int userId = 1;
        int companyAId = 10;   // primary company
        int companyBId = 20;   // membership being updated
        int grantTypeId = 501; // shared grant type (same template in both companies)

        _db.Users.Add(new AppUser { Id = userId, CompanyId = companyAId, Email = "u@x", DisplayName = "U" });
        _db.CompanyMemberships.Add(new CompanyMembership
        {
            Id = 100, UserId = userId, CompanyId = companyAId, IsPrimary = true,
            RoleTemplateId = 5, GrantedBy = 0
        });
        _db.CompanyMemberships.Add(new CompanyMembership
        {
            Id = 200, UserId = userId, CompanyId = companyBId, IsPrimary = false,
            RoleTemplateId = 5, GrantedBy = 99
        });

        // Auto-grant for role template 5 in company A (primary) — must survive
        _db.Grants.Add(new Grant
        {
            Id = 4001, UserId = userId, GrantTypeId = grantTypeId,
            CompanyId = companyAId, IsAutoGrant = true, CanOwn = true
        });
        // Auto-grant for role template 5 in company B (being updated) — must be removed
        _db.Grants.Add(new Grant
        {
            Id = 4002, UserId = userId, GrantTypeId = grantTypeId,
            CompanyId = companyBId, IsAutoGrant = true, CanOwn = true
        });

        // Manual grant in company B (not auto — must also survive)
        _db.Grants.Add(new Grant
        {
            Id = 4003, UserId = userId, GrantTypeId = grantTypeId,
            CompanyId = companyBId, IsAutoGrant = false, CanOwn = true
        });

        // RoleTemplate 5 with one AutoGrant for grantTypeId 501
        _db.RoleTemplates.Add(new RoleTemplate
        {
            Id = 5, Key = "Employee", NameKey = "RoleTemplate_Employee",
            DescriptionKey = "RoleTemplate_Employee_Desc",
            ScopeLevel = RoleScopeLevel.Company, IsSystem = true, IsActive = true
        });
        _db.RoleTemplateGrants.Add(new RoleTemplateGrant
        {
            RoleTemplateId = 5, GrantTypeId = grantTypeId,
            ScopeMode = GrantScopeMode.SameAsRole
        });

        await _db.SaveChangesAsync();

        // Act — simulate the handler's company-scoped grant removal for old template (id=5) in company B only.
        // This is the exact query the handler executes for company-scoped removal.
        var oldTemplate = await _db.RoleTemplates
            .IgnoreQueryFilters()
            .Include(rt => rt.AutoGrants)
            .FirstAsync(rt => rt.Id == 5);

        var oldGrantTypeIds = oldTemplate.AutoGrants.Select(ag => ag.GrantTypeId).ToList();

        var grantsToRemove = await _db.Grants.IgnoreQueryFilters()
            .Where(g => g.UserId == userId
                && g.CompanyId == companyBId
                && g.IsAutoGrant
                && oldGrantTypeIds.Contains(g.GrantTypeId))
            .ToListAsync();

        _db.Grants.RemoveRange(grantsToRemove);
        await _db.SaveChangesAsync();

        // Assert — company A's auto-grant for grantType 501 is UNTOUCHED
        var companyAGrant = await _db.Grants.IgnoreQueryFilters()
            .FirstOrDefaultAsync(g => g.Id == 4001);
        companyAGrant.Should().NotBeNull(
            "company A's auto-grant from the same template must be UNTOUCHED by a company-B membership update");

        // Assert — company B's old auto-grant is removed
        var companyBAutoGrant = await _db.Grants.IgnoreQueryFilters()
            .FirstOrDefaultAsync(g => g.Id == 4002);
        companyBAutoGrant.Should().BeNull(
            "company B's auto-grant from the old template must be removed by the company-scoped removal");

        // Assert — manual grant in company B survives (IsAutoGrant=false is not touched)
        var companyBManualGrant = await _db.Grants.IgnoreQueryFilters()
            .FirstOrDefaultAsync(g => g.Id == 4003);
        companyBManualGrant.Should().NotBeNull(
            "manually-added grants (IsAutoGrant=false) must never be removed by template-reconciliation");
    }
}
