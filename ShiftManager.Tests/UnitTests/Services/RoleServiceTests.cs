using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Services;

namespace ShiftManager.Tests.UnitTests.Services;

public class RoleServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly RoleService _service;
    private readonly Mock<IGrantService> _grantServiceMock;

    public RoleServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _db = new AppDbContext(options);
        _grantServiceMock = new Mock<IGrantService>();

        _service = new RoleService(_db, _grantServiceMock.Object);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    private async Task<(AppUser User, RoleTemplate Template)> SeedUserAndTemplateAsync(
        int userId = 1,
        string templateKey = "TestRole",
        RoleScopeLevel scopeLevel = RoleScopeLevel.Company)
    {
        var user = new AppUser
        {
            Id = userId, Email = $"user{userId}@test.com", DisplayName = $"User {userId}",
            CompanyId = 1, IsActive = true, Role = UserRole.Employee
        };
        _db.Users.Add(user);

        var template = new RoleTemplate
        {
            Id = 100 + userId, // Avoid ID conflicts
            Key = templateKey,
            NameKey = $"Role_{templateKey}",
            DescriptionKey = $"Desc_{templateKey}",
            ScopeLevel = scopeLevel,
            IsActive = true,
            IsSystem = true,
            SortOrder = 1,
            CanBeAssignedByDefault = true,
            IsVisibleInSignup = true
        };
        _db.RoleTemplates.Add(template);
        await _db.SaveChangesAsync();

        return (user, template);
    }

    // --- GetRoleTemplateAsync ---

    [Fact]
    public async Task GetRoleTemplateAsync_ReturnsTemplate_WhenExistsAndActive()
    {
        var (_, template) = await SeedUserAndTemplateAsync();

        var result = await _service.GetRoleTemplateAsync(template.Id);

        result.Should().NotBeNull();
        result!.Key.Should().Be("TestRole");
    }

    [Fact]
    public async Task GetRoleTemplateAsync_ReturnsNull_WhenNotFound()
    {
        var result = await _service.GetRoleTemplateAsync(999);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetRoleTemplateAsync_ReturnsNull_WhenInactive()
    {
        var (_, template) = await SeedUserAndTemplateAsync();
        template.IsActive = false;
        await _db.SaveChangesAsync();

        var result = await _service.GetRoleTemplateAsync(template.Id);

        result.Should().BeNull();
    }

    // --- GetRoleTemplateByKeyAsync ---

    [Fact]
    public async Task GetRoleTemplateByKeyAsync_ReturnsTemplate_WhenExists()
    {
        await SeedUserAndTemplateAsync(templateKey: "ManagerRole");

        var result = await _service.GetRoleTemplateByKeyAsync("ManagerRole");

        result.Should().NotBeNull();
        result!.Key.Should().Be("ManagerRole");
    }

    [Fact]
    public async Task GetRoleTemplateByKeyAsync_ReturnsNull_WhenKeyNotFound()
    {
        var result = await _service.GetRoleTemplateByKeyAsync("NonexistentKey");

        result.Should().BeNull();
    }

    // --- GetRoleTemplatesAsync ---

    [Fact]
    public async Task GetRoleTemplatesAsync_ReturnsOnlyActiveTemplates()
    {
        await SeedUserAndTemplateAsync(userId: 1, templateKey: "Active1");
        await SeedUserAndTemplateAsync(userId: 2, templateKey: "Active2");

        // Add inactive template
        _db.RoleTemplates.Add(new RoleTemplate
        {
            Id = 999, Key = "Inactive", NameKey = "N", DescriptionKey = "D",
            IsActive = false, SortOrder = 0
        });
        await _db.SaveChangesAsync();

        var result = await _service.GetRoleTemplatesAsync();

        result.Should().HaveCount(2);
        result.Should().OnlyContain(rt => rt.IsActive);
    }

    [Fact]
    public async Task GetRoleTemplatesAsync_ReturnsSortedBySortOrderThenKey()
    {
        _db.RoleTemplates.AddRange(
            new RoleTemplate { Id = 1, Key = "Bravo", NameKey = "N", DescriptionKey = "D", IsActive = true, SortOrder = 2 },
            new RoleTemplate { Id = 2, Key = "Alpha", NameKey = "N", DescriptionKey = "D", IsActive = true, SortOrder = 1 },
            new RoleTemplate { Id = 3, Key = "Charlie", NameKey = "N", DescriptionKey = "D", IsActive = true, SortOrder = 2 }
        );
        await _db.SaveChangesAsync();

        var result = await _service.GetRoleTemplatesAsync();

        result.Should().HaveCount(3);
        result[0].Key.Should().Be("Alpha");    // SortOrder 1
        result[1].Key.Should().Be("Bravo");    // SortOrder 2, Key "B" < "C"
        result[2].Key.Should().Be("Charlie");   // SortOrder 2, Key "C"
    }

    // --- GetRoleTemplatesByScopeLevelAsync ---

    [Fact]
    public async Task GetRoleTemplatesByScopeLevelAsync_FiltersCorrectly()
    {
        _db.RoleTemplates.AddRange(
            new RoleTemplate { Id = 1, Key = "CompanyRole", NameKey = "N", DescriptionKey = "D", IsActive = true, ScopeLevel = RoleScopeLevel.Company, SortOrder = 1 },
            new RoleTemplate { Id = 2, Key = "MoleculeRole", NameKey = "N", DescriptionKey = "D", IsActive = true, ScopeLevel = RoleScopeLevel.Molecule, SortOrder = 1 },
            new RoleTemplate { Id = 3, Key = "AreaRole", NameKey = "N", DescriptionKey = "D", IsActive = true, ScopeLevel = RoleScopeLevel.Area, SortOrder = 1 }
        );
        await _db.SaveChangesAsync();

        var result = await _service.GetRoleTemplatesByScopeLevelAsync(RoleScopeLevel.Company);

        result.Should().HaveCount(1);
        result.First().Key.Should().Be("CompanyRole");
    }

    // --- AssignRoleAsync ---

    [Fact]
    public async Task AssignRoleAsync_HappyPath_CreatesAssignmentAndAppliesAutoGrants()
    {
        var (user, template) = await SeedUserAndTemplateAsync();
        var scope = GrantScope.Company(1);

        var result = await _service.AssignRoleAsync(
            userId: user.Id, roleTemplateId: template.Id, scope: scope, assignedByUserId: 99);

        result.Should().NotBeNull();
        result!.UserId.Should().Be(user.Id);
        result.RoleTemplateId.Should().Be(template.Id);
        result.CompanyId.Should().Be(1);
        result.IsActive.Should().BeTrue();
        result.AssignedByUserId.Should().Be(99);

        // Verify auto-grants were applied
        _grantServiceMock.Verify(g => g.ApplyAutoGrantsAsync(user.Id, template.Id, scope), Times.Once);
    }

    [Fact]
    public async Task AssignRoleAsync_ReturnsNull_WhenUserNotFound()
    {
        var (_, template) = await SeedUserAndTemplateAsync();

        var result = await _service.AssignRoleAsync(
            userId: 999, roleTemplateId: template.Id, scope: GrantScope.Company(1), assignedByUserId: 99);

        result.Should().BeNull();
    }

    [Fact]
    public async Task AssignRoleAsync_ReturnsNull_WhenTemplateNotFound()
    {
        var (user, _) = await SeedUserAndTemplateAsync();

        var result = await _service.AssignRoleAsync(
            userId: user.Id, roleTemplateId: 999, scope: GrantScope.Company(1), assignedByUserId: 99);

        result.Should().BeNull();
    }

    [Fact]
    public async Task AssignRoleAsync_ReturnsExisting_WhenDuplicateRoleAndScope()
    {
        var (user, template) = await SeedUserAndTemplateAsync();
        var scope = GrantScope.Company(1);

        // Assign first time
        var first = await _service.AssignRoleAsync(user.Id, template.Id, scope, 99);
        first.Should().NotBeNull();

        // Assign again with same scope
        var second = await _service.AssignRoleAsync(user.Id, template.Id, scope, 99);

        second.Should().NotBeNull();
        second!.Id.Should().Be(first!.Id); // Returns existing, not a new one

        // Auto-grants should only be applied once (the first call)
        _grantServiceMock.Verify(g => g.ApplyAutoGrantsAsync(user.Id, template.Id, scope), Times.Once);
    }

    // --- RemoveRoleAsync ---

    [Fact]
    public async Task RemoveRoleAsync_HappyPath_DeactivatesAndRemovesAutoGrants()
    {
        var (user, template) = await SeedUserAndTemplateAsync();
        var scope = GrantScope.Company(1);

        var assignment = await _service.AssignRoleAsync(user.Id, template.Id, scope, 99);
        assignment.Should().NotBeNull();

        var result = await _service.RemoveRoleAsync(assignment!.Id, removedByUserId: 99);

        result.Should().BeTrue();

        // Verify it's deactivated in the database
        var savedAssignment = await _db.UserRoleAssignments.FindAsync(assignment.Id);
        savedAssignment!.IsActive.Should().BeFalse();

        // Verify auto-grants cleanup was called
        _grantServiceMock.Verify(g => g.RemoveAutoGrantsAsync(user.Id, template.Id), Times.Once);
    }

    [Fact]
    public async Task RemoveRoleAsync_ReturnsFalse_WhenNotFound()
    {
        var result = await _service.RemoveRoleAsync(999);

        result.Should().BeFalse();
    }

    // --- RemoveAllUserRolesAsync ---

    [Fact]
    public async Task RemoveAllUserRolesAsync_DeactivatesAllActiveRoles()
    {
        var (user, template1) = await SeedUserAndTemplateAsync(userId: 1, templateKey: "Role1");

        // Add a second template
        var template2 = new RoleTemplate
        {
            Id = 200, Key = "Role2", NameKey = "N", DescriptionKey = "D",
            IsActive = true, SortOrder = 2
        };
        _db.RoleTemplates.Add(template2);
        await _db.SaveChangesAsync();

        // Assign both roles
        await _service.AssignRoleAsync(user.Id, template1.Id, GrantScope.Company(1), 99);
        await _service.AssignRoleAsync(user.Id, template2.Id, GrantScope.Company(1), 99);

        var result = await _service.RemoveAllUserRolesAsync(user.Id);

        result.Should().BeTrue();

        // Verify all roles are deactivated
        var activeRoles = await _db.UserRoleAssignments
            .Where(ura => ura.UserId == user.Id && ura.IsActive)
            .CountAsync();
        activeRoles.Should().Be(0);

        // Verify auto-grants removed for both
        _grantServiceMock.Verify(g => g.RemoveAutoGrantsAsync(user.Id, template1.Id), Times.Once);
        _grantServiceMock.Verify(g => g.RemoveAutoGrantsAsync(user.Id, template2.Id), Times.Once);
    }

    // --- GetUserRolesAsync ---

    [Fact]
    public async Task GetUserRolesAsync_ReturnsActiveRolesOnly()
    {
        var (user, template) = await SeedUserAndTemplateAsync();

        // Create active assignment
        _db.UserRoleAssignments.Add(new UserRoleAssignment
        {
            UserId = user.Id, RoleTemplateId = template.Id,
            CompanyId = 1, AssignedByUserId = 99, IsActive = true
        });
        // Create inactive assignment
        _db.UserRoleAssignments.Add(new UserRoleAssignment
        {
            UserId = user.Id, RoleTemplateId = template.Id,
            CompanyId = 2, AssignedByUserId = 99, IsActive = false
        });
        await _db.SaveChangesAsync();

        var result = await _service.GetUserRolesAsync(user.Id);

        result.Should().HaveCount(1);
        result.First().IsActive.Should().BeTrue();
    }

    // --- UserHasRoleAsync ---

    [Fact]
    public async Task UserHasRoleAsync_ByKey_ReturnsTrue_WhenHasActiveRole()
    {
        var (user, template) = await SeedUserAndTemplateAsync(templateKey: "ManagerRole");
        _db.UserRoleAssignments.Add(new UserRoleAssignment
        {
            UserId = user.Id, RoleTemplateId = template.Id,
            CompanyId = 1, AssignedByUserId = 99, IsActive = true
        });
        await _db.SaveChangesAsync();

        var result = await _service.UserHasRoleAsync(user.Id, "ManagerRole");

        result.Should().BeTrue();
    }

    [Fact]
    public async Task UserHasRoleAsync_ByKey_ReturnsFalse_WhenRoleInactive()
    {
        var (user, template) = await SeedUserAndTemplateAsync(templateKey: "ManagerRole");
        _db.UserRoleAssignments.Add(new UserRoleAssignment
        {
            UserId = user.Id, RoleTemplateId = template.Id,
            CompanyId = 1, AssignedByUserId = 99, IsActive = false
        });
        await _db.SaveChangesAsync();

        var result = await _service.UserHasRoleAsync(user.Id, "ManagerRole");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task UserHasRoleAsync_ById_ReturnsTrue_WhenHasActiveRole()
    {
        var (user, template) = await SeedUserAndTemplateAsync();
        _db.UserRoleAssignments.Add(new UserRoleAssignment
        {
            UserId = user.Id, RoleTemplateId = template.Id,
            CompanyId = 1, AssignedByUserId = 99, IsActive = true
        });
        await _db.SaveChangesAsync();

        var result = await _service.UserHasRoleAsync(user.Id, template.Id);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task UserHasRoleAsync_ReturnsFalse_WhenNoRoleAssignment()
    {
        await SeedUserAndTemplateAsync();

        var result = await _service.UserHasRoleAsync(1, "NonexistentRole");

        result.Should().BeFalse();
    }

    // --- GetUsersWithRoleAsync ---

    [Fact]
    public async Task GetUsersWithRoleAsync_ReturnsActiveUsersWithRole()
    {
        var (user1, template) = await SeedUserAndTemplateAsync(userId: 1, templateKey: "Lead");
        var user2 = new AppUser
        {
            Id = 2, Email = "user2@test.com", DisplayName = "User 2",
            CompanyId = 1, IsActive = true, Role = UserRole.Employee
        };
        var user3 = new AppUser
        {
            Id = 3, Email = "user3@test.com", DisplayName = "User 3",
            CompanyId = 1, IsActive = false, Role = UserRole.Employee // Inactive
        };
        _db.Users.AddRange(user2, user3);
        await _db.SaveChangesAsync();

        _db.UserRoleAssignments.AddRange(
            new UserRoleAssignment { UserId = 1, RoleTemplateId = template.Id, AssignedByUserId = 99, IsActive = true },
            new UserRoleAssignment { UserId = 2, RoleTemplateId = template.Id, AssignedByUserId = 99, IsActive = true },
            new UserRoleAssignment { UserId = 3, RoleTemplateId = template.Id, AssignedByUserId = 99, IsActive = true } // User inactive
        );
        await _db.SaveChangesAsync();

        var result = await _service.GetUsersWithRoleAsync(template.Id);

        result.Should().HaveCount(2);
        result.Should().OnlyContain(u => u.IsActive);
    }

    // --- GetAssignableRoleTemplatesAsync ---

    [Fact]
    public async Task GetAssignableRoleTemplatesAsync_ReturnsOnlyAssignable()
    {
        _db.RoleTemplates.AddRange(
            new RoleTemplate { Id = 1, Key = "Assignable", NameKey = "N", DescriptionKey = "D", IsActive = true, CanBeAssignedByDefault = true, SortOrder = 1 },
            new RoleTemplate { Id = 2, Key = "NotAssignable", NameKey = "N", DescriptionKey = "D", IsActive = true, CanBeAssignedByDefault = false, SortOrder = 2 }
        );
        await _db.SaveChangesAsync();

        var result = await _service.GetAssignableRoleTemplatesAsync();

        result.Should().HaveCount(1);
        result.First().Key.Should().Be("Assignable");
    }

    // --- GetSignupRoleTemplatesAsync ---

    [Fact]
    public async Task GetSignupRoleTemplatesAsync_ReturnsOnlyVisibleInSignup()
    {
        _db.RoleTemplates.AddRange(
            new RoleTemplate { Id = 1, Key = "Visible", NameKey = "N", DescriptionKey = "D", IsActive = true, IsVisibleInSignup = true, SortOrder = 1 },
            new RoleTemplate { Id = 2, Key = "Hidden", NameKey = "N", DescriptionKey = "D", IsActive = true, IsVisibleInSignup = false, SortOrder = 2 }
        );
        await _db.SaveChangesAsync();

        var result = await _service.GetSignupRoleTemplatesAsync();

        result.Should().HaveCount(1);
        result.First().Key.Should().Be("Visible");
    }

    // --- Count methods ---

    [Fact]
    public async Task GetActiveRoleTemplateCountAsync_CountsOnlyActive()
    {
        _db.RoleTemplates.AddRange(
            new RoleTemplate { Id = 1, Key = "A", NameKey = "N", DescriptionKey = "D", IsActive = true, SortOrder = 1 },
            new RoleTemplate { Id = 2, Key = "B", NameKey = "N", DescriptionKey = "D", IsActive = true, SortOrder = 2 },
            new RoleTemplate { Id = 3, Key = "C", NameKey = "N", DescriptionKey = "D", IsActive = false, SortOrder = 3 }
        );
        await _db.SaveChangesAsync();

        var result = await _service.GetActiveRoleTemplateCountAsync();

        result.Should().Be(2);
    }

    [Fact]
    public async Task GetRoleTemplateCountAsync_CountsAll()
    {
        _db.RoleTemplates.AddRange(
            new RoleTemplate { Id = 1, Key = "A", NameKey = "N", DescriptionKey = "D", IsActive = true, SortOrder = 1 },
            new RoleTemplate { Id = 2, Key = "B", NameKey = "N", DescriptionKey = "D", IsActive = false, SortOrder = 2 }
        );
        await _db.SaveChangesAsync();

        var result = await _service.GetRoleTemplateCountAsync();

        result.Should().Be(2);
    }
}
