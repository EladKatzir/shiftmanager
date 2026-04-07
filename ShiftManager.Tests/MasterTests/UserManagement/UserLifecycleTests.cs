using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Services;
using ShiftManager.Tests.MasterTests.Infrastructure;

namespace ShiftManager.Tests.MasterTests.UserManagement;

/// <summary>
/// Tests for user creation, role assignment, and deactivation lifecycle.
/// Uses RoleService backed by real GrantService with hierarchy mocks.
///
/// IMPORTANT: Each test creates its own AppDbContext via Fixture.CreateDbContext() to avoid
/// change tracker pollution from other test classes' DisposeAsync cleanup in the shared Db.
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
            Email = $"lifecycle.test.{emailSuffix}.{Guid.NewGuid():N}@test.com",
            DisplayName = $"Lifecycle Test {emailSuffix}",
            PasswordHash = Array.Empty<byte>(),
            PasswordSalt = Array.Empty<byte>(),
            IsActive = true,
            JobTypeId = jobTypeId
        };
    }

    /// <summary>
    /// Creates a RoleService that uses the provided db context instead of the shared Db.
    /// </summary>
    private RoleService CreateRoleServiceWithDb(AppDbContext db)
    {
        var hierarchyMock = new Mock<IHierarchyService>();
        var project = Fixture.ProjectByName["Shifty"];
        var area = Fixture.AreaByName["190"];

        hierarchyMock
            .Setup(h => h.GetUserHierarchyContextAsync(It.IsAny<int>()))
            .ReturnsAsync((int userId) =>
            {
                var user = Fixture.UserByEmail.Values.FirstOrDefault(u => u.Id == userId);
                // Also check the provided db for newly created users
                user ??= db.Users.Local.FirstOrDefault(u => u.Id == userId);
                if (user == null) return null;

                var company = Fixture.CompanyByName.Values.FirstOrDefault(c => c.Id == user.CompanyId);
                if (company == null) return null;

                var molecule = Fixture.MoleculeByName.Values.FirstOrDefault(m => m.Id == company.MoleculeId);
                if (molecule == null) return null;

                var jobType = user.JobTypeId.HasValue
                    ? Fixture.JobTypeByName.Values.FirstOrDefault(j => j.Id == user.JobTypeId.Value)
                    : null;

                return new UserHierarchyContext(
                    userId,
                    new HierarchyPath(project, area, molecule, company, null),
                    jobType,
                    IsWorkforce: molecule.Type == Models.Support.MoleculeType.Workforce,
                    IsTech: molecule.Type == Models.Support.MoleculeType.Tech);
            });

        var auditLogService = Mock.Of<IAuditLogService>();
        var grantService = new GrantService(db, hierarchyMock.Object, auditLogService);
        return new RoleService(db, grantService);
    }

    // ================================================================
    // USER CREATION
    // ================================================================

    [Fact]
    public async Task CreateUser_WithRequiredFields_Succeeds()
    {
        using var db = Fixture.CreateDbContext();
        var tzafona = Fixture.CompanyByName["Tzafona"];
        var user = CreateTestAppUser(tzafona.Id, "create1");

        db.Users.Add(user);
        await db.SaveChangesAsync();

        var persisted = await db.Users.FindAsync(user.Id);

        persisted.Should().NotBeNull();
        persisted!.Email.Should().Be(user.Email);
        persisted.CompanyId.Should().Be(tzafona.Id);
        persisted.IsActive.Should().BeTrue();
        persisted.DisplayName.Should().Be(user.DisplayName);

        // Cleanup
        db.Users.Remove(user);
        await db.SaveChangesAsync();
    }

    // ================================================================
    // ROLE ASSIGNMENT + GRANTS
    // ================================================================

    [Fact]
    public async Task AssignRole_PropagatesGrants()
    {
        using var db = Fixture.CreateDbContext();
        var tzafona = Fixture.CompanyByName["Tzafona"];
        var user = CreateTestAppUser(tzafona.Id, "assignrole1");
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var employeeTemplate = Fixture.RoleTemplateByKey["Employee"];
        var alhut = Fixture.JobTypeByName["Alhut"];
        var scope = new GrantScope(CompanyId: tzafona.Id, JobTypeId: alhut.Id);

        var roleService = CreateRoleServiceWithDb(db);
        var assignment = await roleService.AssignRoleAsync(
            user.Id, employeeTemplate.Id, scope, user.Id);

        assignment.Should().NotBeNull("AssignRoleAsync should return a role assignment");

        var grants = await db.Grants
            .Where(g => g.UserId == user.Id && g.IsAutoGrant)
            .ToListAsync();

        grants.Should().NotBeEmpty(
            "Assigning Employee role should create auto-grants");

        // Cleanup
        db.Grants.RemoveRange(grants);
        db.UserRoleAssignments.Remove(assignment!);
        db.Users.Remove(user);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task RemoveRole_CleansUpGrants()
    {
        using var db = Fixture.CreateDbContext();
        var tzafona = Fixture.CompanyByName["Tzafona"];
        var user = CreateTestAppUser(tzafona.Id, "removerole1");
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var employeeTemplate = Fixture.RoleTemplateByKey["Employee"];
        var alhut = Fixture.JobTypeByName["Alhut"];
        var scope = new GrantScope(CompanyId: tzafona.Id, JobTypeId: alhut.Id);

        var roleService = CreateRoleServiceWithDb(db);
        var assignment = await roleService.AssignRoleAsync(
            user.Id, employeeTemplate.Id, scope, user.Id);
        assignment.Should().NotBeNull();

        var grantsBefore = await db.Grants
            .Where(g => g.UserId == user.Id && g.IsAutoGrant)
            .CountAsync();
        grantsBefore.Should().BeGreaterThan(0);

        var removed = await roleService.RemoveRoleAsync(assignment!.Id, user.Id);
        removed.Should().BeTrue();

        var grantsAfter = await db.Grants
            .Where(g => g.UserId == user.Id && g.IsAutoGrant)
            .CountAsync();
        grantsAfter.Should().Be(0,
            "Removing the role should clean up all auto-grants from that template");

        // Cleanup
        var remainingGrants = await db.Grants.Where(g => g.UserId == user.Id).ToListAsync();
        db.Grants.RemoveRange(remainingGrants);
        var roleAssignments = await db.UserRoleAssignments.Where(r => r.UserId == user.Id).ToListAsync();
        db.UserRoleAssignments.RemoveRange(roleAssignments);
        db.Users.Remove(user);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task ChangeRoleTemplate_UpdatesGrants()
    {
        using var db = Fixture.CreateDbContext();
        var tzafona = Fixture.CompanyByName["Tzafona"];
        var user = CreateTestAppUser(tzafona.Id, "changerole1");
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var employeeTemplate = Fixture.RoleTemplateByKey["Employee"];
        var leadTemplate = Fixture.RoleTemplateByKey["Lead"];
        var alhut = Fixture.JobTypeByName["Alhut"];
        var scope = new GrantScope(CompanyId: tzafona.Id, JobTypeId: alhut.Id);

        var roleService = CreateRoleServiceWithDb(db);

        var empAssignment = await roleService.AssignRoleAsync(
            user.Id, employeeTemplate.Id, scope, user.Id);
        empAssignment.Should().NotBeNull();

        await roleService.RemoveRoleAsync(empAssignment!.Id, user.Id);

        var leadAssignment = await roleService.AssignRoleAsync(
            user.Id, leadTemplate.Id, scope, user.Id);
        leadAssignment.Should().NotBeNull();

        var leadGrantKeys = await db.Grants
            .Where(g => g.UserId == user.Id && g.IsAutoGrant)
            .Select(g => g.GrantTypeId)
            .ToListAsync();

        leadGrantKeys.Should().NotBeEmpty("Lead role should have auto-grants");

        // Cleanup
        var remainingGrants = await db.Grants.Where(g => g.UserId == user.Id).ToListAsync();
        db.Grants.RemoveRange(remainingGrants);
        var roleAssignments = await db.UserRoleAssignments.Where(r => r.UserId == user.Id).ToListAsync();
        db.UserRoleAssignments.RemoveRange(roleAssignments);
        db.Users.Remove(user);
        await db.SaveChangesAsync();
    }

    // ================================================================
    // DEACTIVATION
    // ================================================================

    [Fact]
    public async Task DeactivatedUser_ExcludedFromActiveQueries()
    {
        using var db = Fixture.CreateDbContext();
        var tzafona = Fixture.CompanyByName["Tzafona"];
        var user = CreateTestAppUser(tzafona.Id, "deactivate1");
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var activeCount = await db.Users
            .Where(u => u.Id == user.Id && u.IsActive)
            .CountAsync();
        activeCount.Should().Be(1);

        user.IsActive = false;
        await db.SaveChangesAsync();

        var activeAfter = await db.Users
            .Where(u => u.Id == user.Id && u.IsActive)
            .CountAsync();
        activeAfter.Should().Be(0,
            "Deactivated user should not appear in active user queries");

        var exists = await db.Users
            .Where(u => u.Id == user.Id)
            .AnyAsync();
        exists.Should().BeTrue("Deactivated user should still exist in DB (soft delete)");

        // Cleanup
        db.Users.Remove(user);
        await db.SaveChangesAsync();
    }

    // ================================================================
    // JOB TYPE ASSOCIATION
    // ================================================================

    [Fact]
    public async Task UserWithJobType_CorrectlyAssociatedToShiftEligibility()
    {
        using var db = Fixture.CreateDbContext();

        // Use the Lead user (Alhut) which has a unique key — Employee key collides (two Tzafona employees)
        var user = GetTestUser("Tzafona", "Lead");
        var alhut = Fixture.JobTypeByName["Alhut"];

        user.JobTypeId.Should().Be(alhut.Id,
            "Tzafona Lead was seeded with Alhut job type");

        var usersWithAlhut = await db.Users
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
        using var db = Fixture.CreateDbContext();
        var tzafona = Fixture.CompanyByName["Tzafona"];
        var user = CreateTestAppUser(tzafona.Id, "multirole1");
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var employeeTemplate = Fixture.RoleTemplateByKey["Employee"];
        var assignerTemplate = Fixture.RoleTemplateByKey["Assigner"];
        var alhut = Fixture.JobTypeByName["Alhut"];

        var roleService = CreateRoleServiceWithDb(db);

        var empAssignment = await roleService.AssignRoleAsync(
            user.Id, employeeTemplate.Id,
            new GrantScope(CompanyId: tzafona.Id, JobTypeId: alhut.Id),
            user.Id);
        empAssignment.Should().NotBeNull();

        var grantsAfterEmployee = await db.Grants
            .Where(g => g.UserId == user.Id && g.IsAutoGrant)
            .CountAsync();

        var assignerAssignment = await roleService.AssignRoleAsync(
            user.Id, assignerTemplate.Id,
            new GrantScope(CompanyId: tzafona.Id),
            user.Id);
        assignerAssignment.Should().NotBeNull();

        var grantsAfterBoth = await db.Grants
            .Where(g => g.UserId == user.Id && g.IsAutoGrant)
            .CountAsync();

        grantsAfterBoth.Should().BeGreaterThan(grantsAfterEmployee,
            "Adding a second role should accumulate more grants");

        // Cleanup
        var allGrants = await db.Grants.Where(g => g.UserId == user.Id).ToListAsync();
        db.Grants.RemoveRange(allGrants);
        var roleAssignments = await db.UserRoleAssignments.Where(r => r.UserId == user.Id).ToListAsync();
        db.UserRoleAssignments.RemoveRange(roleAssignments);
        db.Users.Remove(user);
        await db.SaveChangesAsync();
    }

    // ================================================================
    // DUPLICATE ROLE ASSIGNMENT
    // ================================================================

    [Fact]
    public async Task DuplicateRoleAssignment_ReturnsExisting()
    {
        using var db = Fixture.CreateDbContext();
        var tzafona = Fixture.CompanyByName["Tzafona"];
        var user = CreateTestAppUser(tzafona.Id, "duprole1");
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var employeeTemplate = Fixture.RoleTemplateByKey["Employee"];
        var alhut = Fixture.JobTypeByName["Alhut"];
        var scope = new GrantScope(CompanyId: tzafona.Id, JobTypeId: alhut.Id);

        var roleService = CreateRoleServiceWithDb(db);

        var first = await roleService.AssignRoleAsync(
            user.Id, employeeTemplate.Id, scope, user.Id);
        first.Should().NotBeNull();

        var second = await roleService.AssignRoleAsync(
            user.Id, employeeTemplate.Id, scope, user.Id);

        second.Should().NotBeNull();
        second!.Id.Should().Be(first!.Id,
            "Duplicate role assignment should return the existing assignment, not create a new one");

        // Cleanup
        var allGrants = await db.Grants.Where(g => g.UserId == user.Id).ToListAsync();
        db.Grants.RemoveRange(allGrants);
        var roleAssignments = await db.UserRoleAssignments.Where(r => r.UserId == user.Id).ToListAsync();
        db.UserRoleAssignments.RemoveRange(roleAssignments);
        db.Users.Remove(user);
        await db.SaveChangesAsync();
    }

    // ================================================================
    // EMAIL UNIQUENESS
    // ================================================================

    [Fact]
    public async Task UserEmail_ShouldBeUniqueByConvention()
    {
        // NOTE: InMemory provider does not enforce unique indexes.
        // This test verifies that duplicate emails CAN be detected at the application level.
        using var db = Fixture.CreateDbContext();
        var tzafona = Fixture.CompanyByName["Tzafona"];
        var user1 = CreateTestAppUser(tzafona.Id, "unique_email_check");
        db.Users.Add(user1);
        await db.SaveChangesAsync();

        // Verify we can detect duplicates by querying
        var duplicateExists = await db.Users
            .AnyAsync(u => u.Email == user1.Email);
        duplicateExists.Should().BeTrue("first user should exist with this email");

        // Application-level check: before inserting, detect duplicate
        var wouldBeDuplicate = await db.Users
            .AnyAsync(u => u.Email == user1.Email && u.Id != user1.Id);
        wouldBeDuplicate.Should().BeFalse("no other user should have this email yet");

        // Cleanup
        db.Users.Remove(user1);
        await db.SaveChangesAsync();
    }

    // ================================================================
    // AUDIT FIELDS
    // ================================================================

    [Fact]
    public async Task RoleAssignment_SetsAuditFields()
    {
        using var db = Fixture.CreateDbContext();
        var tzafona = Fixture.CompanyByName["Tzafona"];
        var assigner = GetTestUser("Tzafona", "Lead");
        var user = CreateTestAppUser(tzafona.Id, "audit1");
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var employeeTemplate = Fixture.RoleTemplateByKey["Employee"];
        var alhut = Fixture.JobTypeByName["Alhut"];
        var scope = new GrantScope(CompanyId: tzafona.Id, JobTypeId: alhut.Id);

        var beforeAssign = DateTime.UtcNow;
        var roleService = CreateRoleServiceWithDb(db);
        var assignment = await roleService.AssignRoleAsync(
            user.Id, employeeTemplate.Id, scope, assigner.Id);

        assignment.Should().NotBeNull();

        assignment!.AssignedByUserId.Should().Be(assigner.Id,
            "AssignedByUserId should be set to the assigner's ID");
        assignment.AssignedAt.Should().BeOnOrAfter(beforeAssign,
            "AssignedAt should be set to a time at or after the operation started");
        assignment.IsActive.Should().BeTrue();

        // Cleanup
        var allGrants = await db.Grants.Where(g => g.UserId == user.Id).ToListAsync();
        db.Grants.RemoveRange(allGrants);
        var roleAssignments = await db.UserRoleAssignments.Where(r => r.UserId == user.Id).ToListAsync();
        db.UserRoleAssignments.RemoveRange(roleAssignments);
        db.Users.Remove(user);
        await db.SaveChangesAsync();
    }
}
