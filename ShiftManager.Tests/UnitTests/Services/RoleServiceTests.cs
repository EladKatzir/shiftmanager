using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Resources;
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

        var localizer = new StringLocalizer<SharedResources>(
            new ResourceManagerStringLocalizerFactory(
                Options.Create(new LocalizationOptions { ResourcesPath = "Resources" }),
                NullLoggerFactory.Instance));
        _service = new RoleService(_db, _grantServiceMock.Object, NullLogger<RoleService>.Instance, localizer);
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

        var opResult = await _service.AssignRoleAsync(
            userId: user.Id, roleTemplateId: template.Id, scope: scope, assignedByUserId: 99);

        opResult.Success.Should().BeTrue();
        var result = opResult.Value;
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
    public async Task AssignRoleAsync_ReturnsFailure_WhenUserNotFound()
    {
        var (_, template) = await SeedUserAndTemplateAsync();

        var result = await _service.AssignRoleAsync(
            userId: 999, roleTemplateId: template.Id, scope: GrantScope.Company(1), assignedByUserId: 99);

        result.Success.Should().BeFalse();
        result.ErrorKey.Should().Be("Error_RoleService_UserNotFound");
        result.Value.Should().BeNull();
    }

    [Fact]
    public async Task AssignRoleAsync_ReturnsFailure_WhenTemplateNotFound()
    {
        var (user, _) = await SeedUserAndTemplateAsync();

        var result = await _service.AssignRoleAsync(
            userId: user.Id, roleTemplateId: 999, scope: GrantScope.Company(1), assignedByUserId: 99);

        result.Success.Should().BeFalse();
        result.ErrorKey.Should().Be("Error_RoleService_TemplateNotFound");
        result.Value.Should().BeNull();
    }

    [Fact]
    public async Task AssignRoleAsync_ReturnsExisting_WhenDuplicateRoleAndScope()
    {
        var (user, template) = await SeedUserAndTemplateAsync();
        var scope = GrantScope.Company(1);

        // Assign first time
        var first = (await _service.AssignRoleAsync(user.Id, template.Id, scope, 99)).Value;
        first.Should().NotBeNull();

        // Assign again with same scope
        var second = (await _service.AssignRoleAsync(user.Id, template.Id, scope, 99)).Value;

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

        var assignment = (await _service.AssignRoleAsync(user.Id, template.Id, scope, 99)).Value;
        assignment.Should().NotBeNull();

        var result = await _service.RemoveRoleAsync(assignment!.Id, removedByUserId: 99);

        result.Success.Should().BeTrue();

        // Verify it's deactivated in the database
        var savedAssignment = await _db.UserRoleAssignments.FindAsync(assignment.Id);
        savedAssignment!.IsActive.Should().BeFalse();

        // Verify auto-grants cleanup was called
        _grantServiceMock.Verify(g => g.RemoveAutoGrantsAsync(user.Id, template.Id), Times.Once);
    }

    [Fact]
    public async Task RemoveRoleAsync_ReturnsFailure_WhenNotFound()
    {
        var result = await _service.RemoveRoleAsync(999);

        result.Success.Should().BeFalse();
        result.ErrorKey.Should().Be("Error_RoleService_AssignmentNotFound");
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

        result.Success.Should().BeTrue();

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

    // --- GetUserRolesInScopeAsync ---

    [Fact]
    public async Task GetUserRolesInScopeAsync_FiltersBy_CompanyId()
    {
        var (user, template) = await SeedUserAndTemplateAsync();

        _db.UserRoleAssignments.AddRange(
            new UserRoleAssignment
            {
                UserId = user.Id, RoleTemplateId = template.Id,
                CompanyId = 1, AssignedByUserId = 99, IsActive = true
            },
            new UserRoleAssignment
            {
                UserId = user.Id, RoleTemplateId = template.Id,
                CompanyId = 2, AssignedByUserId = 99, IsActive = true
            }
        );
        await _db.SaveChangesAsync();

        var result = await _service.GetUserRolesInScopeAsync(user.Id, GrantScope.Company(1));

        result.Should().HaveCount(1);
        result.First().CompanyId.Should().Be(1);
    }

    [Fact]
    public async Task GetUserRolesInScopeAsync_ExcludesInactiveRoles()
    {
        var (user, template) = await SeedUserAndTemplateAsync();

        _db.UserRoleAssignments.AddRange(
            new UserRoleAssignment
            {
                UserId = user.Id, RoleTemplateId = template.Id,
                CompanyId = 1, AssignedByUserId = 99, IsActive = true
            },
            new UserRoleAssignment
            {
                UserId = user.Id, RoleTemplateId = template.Id,
                CompanyId = 1, AssignedByUserId = 99, IsActive = false
            }
        );
        await _db.SaveChangesAsync();

        var result = await _service.GetUserRolesInScopeAsync(user.Id, GrantScope.Company(1));

        result.Should().HaveCount(1);
        result.First().IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task GetUserRolesInScopeAsync_FiltersBy_MoleculeId()
    {
        var (user, template) = await SeedUserAndTemplateAsync();

        _db.UserRoleAssignments.AddRange(
            new UserRoleAssignment
            {
                UserId = user.Id, RoleTemplateId = template.Id,
                MoleculeId = 10, AssignedByUserId = 99, IsActive = true
            },
            new UserRoleAssignment
            {
                UserId = user.Id, RoleTemplateId = template.Id,
                MoleculeId = 20, AssignedByUserId = 99, IsActive = true
            }
        );
        await _db.SaveChangesAsync();

        var result = await _service.GetUserRolesInScopeAsync(user.Id, GrantScope.Molecule(10));

        result.Should().HaveCount(1);
        result.First().MoleculeId.Should().Be(10);
    }

    [Fact]
    public async Task GetUserRolesInScopeAsync_ReturnsEmpty_WhenNoMatchingScope()
    {
        var (user, template) = await SeedUserAndTemplateAsync();

        _db.UserRoleAssignments.Add(new UserRoleAssignment
        {
            UserId = user.Id, RoleTemplateId = template.Id,
            CompanyId = 1, AssignedByUserId = 99, IsActive = true
        });
        await _db.SaveChangesAsync();

        var result = await _service.GetUserRolesInScopeAsync(user.Id, GrantScope.Company(999));

        result.Should().BeEmpty();
    }

    // --- GetUserRoleAssignmentAsync ---

    [Fact]
    public async Task GetUserRoleAssignmentAsync_ReturnsAssignment_WhenExists()
    {
        var (user, template) = await SeedUserAndTemplateAsync();

        var assignment = new UserRoleAssignment
        {
            UserId = user.Id, RoleTemplateId = template.Id,
            CompanyId = 1, AssignedByUserId = 99, IsActive = true
        };
        _db.UserRoleAssignments.Add(assignment);
        await _db.SaveChangesAsync();

        var result = await _service.GetUserRoleAssignmentAsync(assignment.Id);

        result.Should().NotBeNull();
        result!.UserId.Should().Be(user.Id);
        result.RoleTemplateId.Should().Be(template.Id);
        result.RoleTemplate.Should().NotBeNull();
        result.User.Should().NotBeNull();
    }

    [Fact]
    public async Task GetUserRoleAssignmentAsync_ReturnsNull_WhenNotFound()
    {
        var result = await _service.GetUserRoleAssignmentAsync(999);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetUserRoleAssignmentAsync_ReturnsInactiveAssignment_Too()
    {
        // This method does NOT filter by IsActive — it returns any assignment by Id
        var (user, template) = await SeedUserAndTemplateAsync();

        var assignment = new UserRoleAssignment
        {
            UserId = user.Id, RoleTemplateId = template.Id,
            CompanyId = 1, AssignedByUserId = 99, IsActive = false
        };
        _db.UserRoleAssignments.Add(assignment);
        await _db.SaveChangesAsync();

        var result = await _service.GetUserRoleAssignmentAsync(assignment.Id);

        result.Should().NotBeNull();
        result!.IsActive.Should().BeFalse();
    }

    // --- GetUsersWithRoleInScopeAsync ---

    [Fact]
    public async Task GetUsersWithRoleInScopeAsync_ReturnsUsersWithRoleInCompany()
    {
        var (user1, template) = await SeedUserAndTemplateAsync(userId: 1, templateKey: "Lead");

        var user2 = new AppUser
        {
            Id = 2, Email = "user2@test.com", DisplayName = "User 2",
            CompanyId = 1, IsActive = true, Role = UserRole.Employee
        };
        _db.Users.Add(user2);
        await _db.SaveChangesAsync();

        _db.UserRoleAssignments.AddRange(
            new UserRoleAssignment
            {
                UserId = 1, RoleTemplateId = template.Id,
                CompanyId = 1, AssignedByUserId = 99, IsActive = true
            },
            new UserRoleAssignment
            {
                UserId = 2, RoleTemplateId = template.Id,
                CompanyId = 2, AssignedByUserId = 99, IsActive = true
            }
        );
        await _db.SaveChangesAsync();

        var result = await _service.GetUsersWithRoleInScopeAsync(template.Id, GrantScope.Company(1));

        result.Should().HaveCount(1);
        result.First().Id.Should().Be(1);
    }

    [Fact]
    public async Task GetUsersWithRoleInScopeAsync_ExcludesInactiveUsers()
    {
        var (user1, template) = await SeedUserAndTemplateAsync(userId: 1, templateKey: "Lead");

        var inactiveUser = new AppUser
        {
            Id = 2, Email = "inactive@test.com", DisplayName = "Inactive User",
            CompanyId = 1, IsActive = false, Role = UserRole.Employee
        };
        _db.Users.Add(inactiveUser);
        await _db.SaveChangesAsync();

        _db.UserRoleAssignments.AddRange(
            new UserRoleAssignment
            {
                UserId = 1, RoleTemplateId = template.Id,
                CompanyId = 1, AssignedByUserId = 99, IsActive = true
            },
            new UserRoleAssignment
            {
                UserId = 2, RoleTemplateId = template.Id,
                CompanyId = 1, AssignedByUserId = 99, IsActive = true
            }
        );
        await _db.SaveChangesAsync();

        var result = await _service.GetUsersWithRoleInScopeAsync(template.Id, GrantScope.Company(1));

        result.Should().HaveCount(1);
        result.First().Id.Should().Be(1);
    }

    [Fact]
    public async Task GetUsersWithRoleInScopeAsync_ExcludesInactiveRoleAssignments()
    {
        var (user, template) = await SeedUserAndTemplateAsync();

        _db.UserRoleAssignments.Add(new UserRoleAssignment
        {
            UserId = user.Id, RoleTemplateId = template.Id,
            CompanyId = 1, AssignedByUserId = 99, IsActive = false // Inactive assignment
        });
        await _db.SaveChangesAsync();

        var result = await _service.GetUsersWithRoleInScopeAsync(template.Id, GrantScope.Company(1));

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetUsersWithRoleInScopeAsync_ReturnsEmpty_WhenNoMatchingScope()
    {
        var (user, template) = await SeedUserAndTemplateAsync();

        _db.UserRoleAssignments.Add(new UserRoleAssignment
        {
            UserId = user.Id, RoleTemplateId = template.Id,
            CompanyId = 1, AssignedByUserId = 99, IsActive = true
        });
        await _db.SaveChangesAsync();

        var result = await _service.GetUsersWithRoleInScopeAsync(template.Id, GrantScope.Company(999));

        result.Should().BeEmpty();
    }

    // --- GetRoleTemplateWithAutoGrantsAsync ---

    [Fact]
    public async Task GetRoleTemplateWithAutoGrantsAsync_ReturnsTemplateWithGrants()
    {
        var grantType = new GrantType
        {
            Id = 1, Key = "TestGrant", NameKey = "N", DescriptionKey = "D", IsSystem = true, IsActive = true
        };
        _db.GrantTypes.Add(grantType);

        var template = new RoleTemplate
        {
            Id = 1, Key = "TestRole", NameKey = "N", DescriptionKey = "D",
            IsActive = true, SortOrder = 1
        };
        _db.RoleTemplates.Add(template);
        await _db.SaveChangesAsync();

        _db.RoleTemplateGrants.Add(new RoleTemplateGrant
        {
            Id = 1, RoleTemplateId = 1, GrantTypeId = 1, CanOwn = true
        });
        await _db.SaveChangesAsync();

        var result = await _service.GetRoleTemplateWithAutoGrantsAsync(1);

        result.Should().NotBeNull();
        result!.Key.Should().Be("TestRole");
        result.AutoGrants.Should().HaveCount(1);
        result.AutoGrants.First().GrantType.Should().NotBeNull();
        result.AutoGrants.First().GrantType.Key.Should().Be("TestGrant");
    }

    [Fact]
    public async Task GetRoleTemplateWithAutoGrantsAsync_ReturnsNull_WhenNotFound()
    {
        var result = await _service.GetRoleTemplateWithAutoGrantsAsync(999);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetRoleTemplateWithAutoGrantsAsync_ReturnsInactiveTemplate_Too()
    {
        // This method does NOT filter by IsActive
        _db.RoleTemplates.Add(new RoleTemplate
        {
            Id = 1, Key = "Inactive", NameKey = "N", DescriptionKey = "D",
            IsActive = false, SortOrder = 1
        });
        await _db.SaveChangesAsync();

        var result = await _service.GetRoleTemplateWithAutoGrantsAsync(1);

        result.Should().NotBeNull();
        result!.IsActive.Should().BeFalse();
    }

    // --- GetRoleTemplateWithAutoGrantsByKeyAsync ---

    [Fact]
    public async Task GetRoleTemplateWithAutoGrantsByKeyAsync_ReturnsTemplate_WhenActiveAndKeyMatches()
    {
        var grantType = new GrantType
        {
            Id = 1, Key = "SomeGrant", NameKey = "N", DescriptionKey = "D", IsSystem = true, IsActive = true
        };
        _db.GrantTypes.Add(grantType);

        var template = new RoleTemplate
        {
            Id = 1, Key = "LeadRole", NameKey = "N", DescriptionKey = "D",
            IsActive = true, SortOrder = 1
        };
        _db.RoleTemplates.Add(template);
        await _db.SaveChangesAsync();

        _db.RoleTemplateGrants.Add(new RoleTemplateGrant
        {
            Id = 1, RoleTemplateId = 1, GrantTypeId = 1, CanOwn = true
        });
        await _db.SaveChangesAsync();

        var result = await _service.GetRoleTemplateWithAutoGrantsByKeyAsync("LeadRole");

        result.Should().NotBeNull();
        result!.Key.Should().Be("LeadRole");
        result.AutoGrants.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetRoleTemplateWithAutoGrantsByKeyAsync_ReturnsNull_WhenInactive()
    {
        _db.RoleTemplates.Add(new RoleTemplate
        {
            Id = 1, Key = "InactiveRole", NameKey = "N", DescriptionKey = "D",
            IsActive = false, SortOrder = 1
        });
        await _db.SaveChangesAsync();

        var result = await _service.GetRoleTemplateWithAutoGrantsByKeyAsync("InactiveRole");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetRoleTemplateWithAutoGrantsByKeyAsync_ReturnsNull_WhenKeyNotFound()
    {
        var result = await _service.GetRoleTemplateWithAutoGrantsByKeyAsync("NonexistentKey");

        result.Should().BeNull();
    }

    // --- GetAllRoleTemplatesWithDetailsAsync ---

    [Fact]
    public async Task GetAllRoleTemplatesWithDetailsAsync_ReturnsAllTemplates_IncludingInactive()
    {
        _db.RoleTemplates.AddRange(
            new RoleTemplate { Id = 1, Key = "Active1", NameKey = "N", DescriptionKey = "D", IsActive = true, SortOrder = 1 },
            new RoleTemplate { Id = 2, Key = "Inactive1", NameKey = "N", DescriptionKey = "D", IsActive = false, SortOrder = 2 }
        );
        await _db.SaveChangesAsync();

        var result = await _service.GetAllRoleTemplatesWithDetailsAsync();

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetAllRoleTemplatesWithDetailsAsync_IsSortedBySortOrderThenKey()
    {
        _db.RoleTemplates.AddRange(
            new RoleTemplate { Id = 1, Key = "Charlie", NameKey = "N", DescriptionKey = "D", IsActive = true, SortOrder = 2 },
            new RoleTemplate { Id = 2, Key = "Alpha", NameKey = "N", DescriptionKey = "D", IsActive = true, SortOrder = 1 },
            new RoleTemplate { Id = 3, Key = "Bravo", NameKey = "N", DescriptionKey = "D", IsActive = true, SortOrder = 2 }
        );
        await _db.SaveChangesAsync();

        var result = await _service.GetAllRoleTemplatesWithDetailsAsync();

        result.Should().HaveCount(3);
        result[0].Key.Should().Be("Alpha");   // SortOrder 1
        result[1].Key.Should().Be("Bravo");   // SortOrder 2, Key "B" < "C"
        result[2].Key.Should().Be("Charlie");  // SortOrder 2, Key "C"
    }

    [Fact]
    public async Task GetAllRoleTemplatesWithDetailsAsync_IncludesAutoGrantsAndUserRoles()
    {
        var grantType = new GrantType
        {
            Id = 1, Key = "G1", NameKey = "N", DescriptionKey = "D", IsSystem = true, IsActive = true
        };
        _db.GrantTypes.Add(grantType);

        var user = new AppUser
        {
            Id = 1, Email = "u@t.com", DisplayName = "U",
            CompanyId = 1, IsActive = true, Role = UserRole.Employee
        };
        _db.Users.Add(user);

        var template = new RoleTemplate
        {
            Id = 1, Key = "RoleWithDetails", NameKey = "N", DescriptionKey = "D",
            IsActive = true, SortOrder = 1
        };
        _db.RoleTemplates.Add(template);
        await _db.SaveChangesAsync();

        _db.RoleTemplateGrants.Add(new RoleTemplateGrant
        {
            Id = 1, RoleTemplateId = 1, GrantTypeId = 1
        });
        _db.UserRoleAssignments.Add(new UserRoleAssignment
        {
            UserId = 1, RoleTemplateId = 1, AssignedByUserId = 1, IsActive = true
        });
        await _db.SaveChangesAsync();

        var result = await _service.GetAllRoleTemplatesWithDetailsAsync();

        result.Should().HaveCount(1);
        result[0].AutoGrants.Should().HaveCount(1);
        result[0].UserRoles.Should().HaveCount(1);
    }

    // --- GetActiveRoleTemplatesWithAutoGrantsAsync ---

    [Fact]
    public async Task GetActiveRoleTemplatesWithAutoGrantsAsync_ReturnsOnlyActiveTemplates()
    {
        _db.RoleTemplates.AddRange(
            new RoleTemplate { Id = 1, Key = "Active", NameKey = "N", DescriptionKey = "D", IsActive = true, SortOrder = 1 },
            new RoleTemplate { Id = 2, Key = "Inactive", NameKey = "N", DescriptionKey = "D", IsActive = false, SortOrder = 2 }
        );
        await _db.SaveChangesAsync();

        var result = await _service.GetActiveRoleTemplatesWithAutoGrantsAsync();

        result.Should().HaveCount(1);
        result.First().Key.Should().Be("Active");
    }

    [Fact]
    public async Task GetActiveRoleTemplatesWithAutoGrantsAsync_IncludesAutoGrantsWithGrantType()
    {
        var grantType = new GrantType
        {
            Id = 1, Key = "ViewCalendar", NameKey = "N", DescriptionKey = "D", IsSystem = true, IsActive = true
        };
        _db.GrantTypes.Add(grantType);

        var template = new RoleTemplate
        {
            Id = 1, Key = "CalendarViewer", NameKey = "N", DescriptionKey = "D",
            IsActive = true, SortOrder = 1
        };
        _db.RoleTemplates.Add(template);
        await _db.SaveChangesAsync();

        _db.RoleTemplateGrants.Add(new RoleTemplateGrant
        {
            Id = 1, RoleTemplateId = 1, GrantTypeId = 1, CanOwn = false, CanGive = false
        });
        await _db.SaveChangesAsync();

        var result = await _service.GetActiveRoleTemplatesWithAutoGrantsAsync();

        result.Should().HaveCount(1);
        result[0].AutoGrants.Should().HaveCount(1);
        result[0].AutoGrants[0].GrantType.Should().NotBeNull();
        result[0].AutoGrants[0].GrantType.Key.Should().Be("ViewCalendar");
    }

    [Fact]
    public async Task GetActiveRoleTemplatesWithAutoGrantsAsync_IsSortedBySortOrder()
    {
        _db.RoleTemplates.AddRange(
            new RoleTemplate { Id = 1, Key = "Second", NameKey = "N", DescriptionKey = "D", IsActive = true, SortOrder = 2 },
            new RoleTemplate { Id = 2, Key = "First", NameKey = "N", DescriptionKey = "D", IsActive = true, SortOrder = 1 }
        );
        await _db.SaveChangesAsync();

        var result = await _service.GetActiveRoleTemplatesWithAutoGrantsAsync();

        result.Should().HaveCount(2);
        result[0].Key.Should().Be("First");
        result[1].Key.Should().Be("Second");
    }
}
