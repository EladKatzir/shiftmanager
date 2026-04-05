using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Models;
using ShiftManager.Services;
using ShiftManager.Tests.MasterTests.Infrastructure;

namespace ShiftManager.Tests.MasterTests.UserManagement;

/// <summary>
/// Tests for user creation, role assignment, and deactivation lifecycle.
/// Uses RoleService backed by real GrantService with hierarchy mocks.
/// </summary>
public class UserLifecycleTests : MasterTestBase
{
    public UserLifecycleTests(MasterTestFixture fixture) : base(fixture) { }

    // ================================================================
    // HELPERS
    // ================================================================

    private AppUser CreateTestAppUser(int companyId, string emailSuffix, int? jobTypeId = null)
    {
        return new AppUser
        {
            CompanyId = companyId,
            Email = $"lifecycle.test.{emailSuffix}@test.com",
            DisplayName = $"Lifecycle Test {emailSuffix}",
            PasswordHash = Array.Empty<byte>(),
            PasswordSalt = Array.Empty<byte>(),
            IsActive = true,
            JobTypeId = jobTypeId
        };
    }

    // ================================================================
    // USER CREATION
    // ================================================================

    [Fact]
    public async Task CreateUser_WithRequiredFields_Succeeds()
    {
        var tzafona = Fixture.CompanyByName["Tzafona"];
        var user = CreateTestAppUser(tzafona.Id, "create1");

        Db.Users.Add(user);
        await Db.SaveChangesAsync();
        TrackEntity(user);

        var persisted = await Db.Users.FindAsync(user.Id);

        persisted.Should().NotBeNull();
        persisted!.Email.Should().Be(user.Email);
        persisted.CompanyId.Should().Be(tzafona.Id);
        persisted.IsActive.Should().BeTrue();
        persisted.DisplayName.Should().Be(user.DisplayName);
    }

    // ================================================================
    // ROLE ASSIGNMENT + GRANTS
    // ================================================================

    [Fact]
    public async Task AssignRole_PropagatesGrants()
    {
        var tzafona = Fixture.CompanyByName["Tzafona"];
        var user = CreateTestAppUser(tzafona.Id, "assignrole1");
        Db.Users.Add(user);
        await Db.SaveChangesAsync();
        TrackEntity(user);

        var employeeTemplate = Fixture.RoleTemplateByKey["Employee"];
        var alhut = Fixture.JobTypeByName["Alhut"];
        var scope = new GrantScope(CompanyId: tzafona.Id, JobTypeId: alhut.Id);

        var roleService = CreateRoleService();
        var assignment = await roleService.AssignRoleAsync(
            user.Id, employeeTemplate.Id, scope, user.Id);

        assignment.Should().NotBeNull("AssignRoleAsync should return a role assignment");
        TrackEntity(assignment!);

        // Verify grants were propagated
        var grants = await Db.Grants
            .Where(g => g.UserId == user.Id && g.IsAutoGrant)
            .ToListAsync();

        grants.Should().NotBeEmpty(
            "Assigning Employee role should create auto-grants");
        TrackEntities(grants.Cast<object>().ToArray());
    }

    [Fact]
    public async Task RemoveRole_CleansUpGrants()
    {
        var tzafona = Fixture.CompanyByName["Tzafona"];
        var user = CreateTestAppUser(tzafona.Id, "removerole1");
        Db.Users.Add(user);
        await Db.SaveChangesAsync();
        TrackEntity(user);

        var employeeTemplate = Fixture.RoleTemplateByKey["Employee"];
        var alhut = Fixture.JobTypeByName["Alhut"];
        var scope = new GrantScope(CompanyId: tzafona.Id, JobTypeId: alhut.Id);

        var roleService = CreateRoleService();
        var assignment = await roleService.AssignRoleAsync(
            user.Id, employeeTemplate.Id, scope, user.Id);
        assignment.Should().NotBeNull();
        TrackEntity(assignment!);

        // Verify grants exist before removal
        var grantsBefore = await Db.Grants
            .Where(g => g.UserId == user.Id && g.IsAutoGrant)
            .CountAsync();
        grantsBefore.Should().BeGreaterThan(0);

        // Remove the role
        var removed = await roleService.RemoveRoleAsync(assignment!.Id, user.Id);
        removed.Should().BeTrue();

        // Verify auto-grants were cleaned up
        var grantsAfter = await Db.Grants
            .Where(g => g.UserId == user.Id && g.IsAutoGrant)
            .CountAsync();
        grantsAfter.Should().Be(0,
            "Removing the role should clean up all auto-grants from that template");
    }

    [Fact]
    public async Task ChangeRoleTemplate_UpdatesGrants()
    {
        var tzafona = Fixture.CompanyByName["Tzafona"];
        var user = CreateTestAppUser(tzafona.Id, "changerole1");
        Db.Users.Add(user);
        await Db.SaveChangesAsync();
        TrackEntity(user);

        var employeeTemplate = Fixture.RoleTemplateByKey["Employee"];
        var leadTemplate = Fixture.RoleTemplateByKey["Lead"];
        var alhut = Fixture.JobTypeByName["Alhut"];
        var scope = new GrantScope(CompanyId: tzafona.Id, JobTypeId: alhut.Id);

        var roleService = CreateRoleService();

        // Assign Employee first
        var empAssignment = await roleService.AssignRoleAsync(
            user.Id, employeeTemplate.Id, scope, user.Id);
        empAssignment.Should().NotBeNull();
        TrackEntity(empAssignment!);

        var empGrantKeys = await Db.Grants
            .Where(g => g.UserId == user.Id && g.IsAutoGrant)
            .Select(g => g.GrantTypeId)
            .ToListAsync();

        // Remove Employee role
        await roleService.RemoveRoleAsync(empAssignment!.Id, user.Id);

        // Assign Lead role
        var leadAssignment = await roleService.AssignRoleAsync(
            user.Id, leadTemplate.Id, scope, user.Id);
        leadAssignment.Should().NotBeNull();
        TrackEntity(leadAssignment!);

        var leadGrantKeys = await Db.Grants
            .Where(g => g.UserId == user.Id && g.IsAutoGrant)
            .Select(g => g.GrantTypeId)
            .ToListAsync();

        leadGrantKeys.Should().NotBeEmpty("Lead role should have auto-grants");

        // Clean up any remaining grants
        var remainingGrants = await Db.Grants
            .Where(g => g.UserId == user.Id)
            .ToListAsync();
        TrackEntities(remainingGrants.Cast<object>().ToArray());
    }

    // ================================================================
    // DEACTIVATION
    // ================================================================

    [Fact]
    public async Task DeactivatedUser_ExcludedFromActiveQueries()
    {
        var tzafona = Fixture.CompanyByName["Tzafona"];
        var user = CreateTestAppUser(tzafona.Id, "deactivate1");
        Db.Users.Add(user);
        await Db.SaveChangesAsync();
        TrackEntity(user);

        // Verify user is active initially
        var activeCount = await Db.Users
            .Where(u => u.Id == user.Id && u.IsActive)
            .CountAsync();
        activeCount.Should().Be(1);

        // Deactivate
        user.IsActive = false;
        await Db.SaveChangesAsync();

        // Verify excluded from active queries
        var activeAfter = await Db.Users
            .Where(u => u.Id == user.Id && u.IsActive)
            .CountAsync();
        activeAfter.Should().Be(0,
            "Deactivated user should not appear in active user queries");

        // But still exists in DB
        var exists = await Db.Users
            .Where(u => u.Id == user.Id)
            .AnyAsync();
        exists.Should().BeTrue("Deactivated user should still exist in DB (soft delete)");
    }

    // ================================================================
    // JOB TYPE ASSOCIATION
    // ================================================================

    [Fact]
    public async Task UserWithJobType_CorrectlyAssociatedToShiftEligibility()
    {
        var alhut = Fixture.JobTypeByName["Alhut"];

        // Use seeded user with known JobTypeId
        var user = GetTestUser("Tzafona", "Employee");

        user.JobTypeId.Should().Be(alhut.Id,
            "Tzafona Employee was seeded with Alhut job type");

        // Query users by job type
        var usersWithAlhut = await Db.Users
            .Where(u => u.JobTypeId == alhut.Id && u.IsActive)
            .ToListAsync();

        usersWithAlhut.Should().Contain(u => u.Id == user.Id,
            "User with Alhut job type should appear in Alhut job type query");
    }

    // ================================================================
    // MULTIPLE ROLE ASSIGNMENTS
    // ================================================================

    [Fact]
    public async Task MultipleRoleAssignments_AccumulateGrants()
    {
        var tzafona = Fixture.CompanyByName["Tzafona"];
        var user = CreateTestAppUser(tzafona.Id, "multirole1");
        Db.Users.Add(user);
        await Db.SaveChangesAsync();
        TrackEntity(user);

        var employeeTemplate = Fixture.RoleTemplateByKey["Employee"];
        var assignerTemplate = Fixture.RoleTemplateByKey["Assigner"];
        var alhut = Fixture.JobTypeByName["Alhut"];

        var roleService = CreateRoleService();

        // Assign Employee role
        var empAssignment = await roleService.AssignRoleAsync(
            user.Id, employeeTemplate.Id,
            new GrantScope(CompanyId: tzafona.Id, JobTypeId: alhut.Id),
            user.Id);
        empAssignment.Should().NotBeNull();
        TrackEntity(empAssignment!);

        var grantsAfterEmployee = await Db.Grants
            .Where(g => g.UserId == user.Id && g.IsAutoGrant)
            .CountAsync();

        // Assign Assigner role (no job type scope)
        var assignerAssignment = await roleService.AssignRoleAsync(
            user.Id, assignerTemplate.Id,
            new GrantScope(CompanyId: tzafona.Id),
            user.Id);
        assignerAssignment.Should().NotBeNull();
        TrackEntity(assignerAssignment!);

        var grantsAfterBoth = await Db.Grants
            .Where(g => g.UserId == user.Id && g.IsAutoGrant)
            .CountAsync();

        grantsAfterBoth.Should().BeGreaterThan(grantsAfterEmployee,
            "Adding a second role should accumulate more grants");

        // Clean up grants
        var allGrants = await Db.Grants.Where(g => g.UserId == user.Id).ToListAsync();
        TrackEntities(allGrants.Cast<object>().ToArray());
    }

    // ================================================================
    // DUPLICATE ROLE ASSIGNMENT
    // ================================================================

    [Fact]
    public async Task DuplicateRoleAssignment_ReturnsExisting()
    {
        var tzafona = Fixture.CompanyByName["Tzafona"];
        var user = CreateTestAppUser(tzafona.Id, "duprole1");
        Db.Users.Add(user);
        await Db.SaveChangesAsync();
        TrackEntity(user);

        var employeeTemplate = Fixture.RoleTemplateByKey["Employee"];
        var alhut = Fixture.JobTypeByName["Alhut"];
        var scope = new GrantScope(CompanyId: tzafona.Id, JobTypeId: alhut.Id);

        var roleService = CreateRoleService();

        var first = await roleService.AssignRoleAsync(
            user.Id, employeeTemplate.Id, scope, user.Id);
        first.Should().NotBeNull();
        TrackEntity(first!);

        // Assign same role again
        var second = await roleService.AssignRoleAsync(
            user.Id, employeeTemplate.Id, scope, user.Id);

        second.Should().NotBeNull();
        second!.Id.Should().Be(first!.Id,
            "Duplicate role assignment should return the existing assignment, not create a new one");

        // Clean up grants
        var allGrants = await Db.Grants.Where(g => g.UserId == user.Id).ToListAsync();
        TrackEntities(allGrants.Cast<object>().ToArray());
    }

    // ================================================================
    // EMAIL UNIQUENESS
    // ================================================================

    [Fact]
    public async Task UserEmail_MustBeUnique()
    {
        var tzafona = Fixture.CompanyByName["Tzafona"];
        var user1 = CreateTestAppUser(tzafona.Id, "unique_email");
        Db.Users.Add(user1);
        await Db.SaveChangesAsync();
        TrackEntity(user1);

        var user2 = new AppUser
        {
            CompanyId = tzafona.Id,
            Email = user1.Email, // Same email
            DisplayName = "Duplicate Email User",
            PasswordHash = Array.Empty<byte>(),
            PasswordSalt = Array.Empty<byte>(),
            IsActive = true
        };

        Db.Users.Add(user2);

        // InMemory provider enforces unique index
        var act = () => Db.SaveChangesAsync();
        await act.Should().ThrowAsync<Exception>(
            "DB should enforce email uniqueness via unique index");

        // Detach the failed entity to avoid polluting the context
        Db.Entry(user2).State = EntityState.Detached;
    }

    // ================================================================
    // AUDIT FIELDS
    // ================================================================

    [Fact]
    public async Task RoleAssignment_SetsAuditFields()
    {
        var tzafona = Fixture.CompanyByName["Tzafona"];
        var assigner = GetTestUser("Tzafona", "Lead");
        var user = CreateTestAppUser(tzafona.Id, "audit1");
        Db.Users.Add(user);
        await Db.SaveChangesAsync();
        TrackEntity(user);

        var employeeTemplate = Fixture.RoleTemplateByKey["Employee"];
        var alhut = Fixture.JobTypeByName["Alhut"];
        var scope = new GrantScope(CompanyId: tzafona.Id, JobTypeId: alhut.Id);

        var beforeAssign = DateTime.UtcNow;
        var roleService = CreateRoleService();
        var assignment = await roleService.AssignRoleAsync(
            user.Id, employeeTemplate.Id, scope, assigner.Id);

        assignment.Should().NotBeNull();
        TrackEntity(assignment!);

        assignment!.AssignedByUserId.Should().Be(assigner.Id,
            "AssignedByUserId should be set to the assigner's ID");
        assignment.AssignedAt.Should().BeOnOrAfter(beforeAssign,
            "AssignedAt should be set to a time at or after the operation started");
        assignment.IsActive.Should().BeTrue();

        // Clean up grants
        var allGrants = await Db.Grants.Where(g => g.UserId == user.Id).ToListAsync();
        TrackEntities(allGrants.Cast<object>().ToArray());
    }
}
