using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Services;

namespace ShiftManager.Tests.UnitTests.Services.V3Hierarchy;

/// <summary>
/// Tests for Task 13.1: Grant Authorization
/// Verifies that grant checking respects scope and auto-grants from roles.
/// </summary>
public class GrantServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly GrantService _service;
    private readonly Mock<IHierarchyService> _hierarchyServiceMock;

    public GrantServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _db = new AppDbContext(options);
        _hierarchyServiceMock = new Mock<IHierarchyService>();
        _service = new GrantService(_db, _hierarchyServiceMock.Object);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    private async Task<TestEntities> SetupTestEntitiesAsync()
    {
        // Create hierarchy
        var project = new Project { Name = "TestProject", DisplayName = "Test Project" };
        _db.Projects.Add(project);
        await _db.SaveChangesAsync();

        var area = new Area { ProjectId = project.Id, Name = "TestArea", DisplayName = "Test Area" };
        _db.Areas.Add(area);
        await _db.SaveChangesAsync();

        var molecule = new Molecule { AreaId = area.Id, Name = "TestMolecule", Type = MoleculeType.Workforce };
        _db.Molecules.Add(molecule);
        await _db.SaveChangesAsync();

        var company1 = new Company { MoleculeId = molecule.Id, Name = "Company1", DisplayName = "Company 1" };
        var company2 = new Company { MoleculeId = molecule.Id, Name = "Company2", DisplayName = "Company 2" };
        _db.Companies.AddRange(company1, company2);
        await _db.SaveChangesAsync();

        // Create grant types
        var viewShiftGrantType = new GrantType { Key = "Shift.View", NameKey = "Grant_Shift_View", Category = GrantCategory.Shift };
        var editShiftGrantType = new GrantType { Key = "Shift.Edit", NameKey = "Grant_Shift_Edit", Category = GrantCategory.Shift };
        var adminGrantType = new GrantType { Key = "Admin.Full", NameKey = "Grant_Admin_Full", Category = GrantCategory.System };
        _db.GrantTypes.AddRange(viewShiftGrantType, editShiftGrantType, adminGrantType);
        await _db.SaveChangesAsync();

        // Create user
        var user = new AppUser
        {
            CompanyId = company1.Id,
            Email = "test@test.com",
            DisplayName = "Test User",
            PasswordHash = Array.Empty<byte>(),
            PasswordSalt = Array.Empty<byte>()
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        // Setup hierarchy service mock
        var hierarchyPath = new HierarchyPath(project, area, molecule, company1, null);
        _hierarchyServiceMock.Setup(h => h.GetUserHierarchyContextAsync(user.Id))
            .ReturnsAsync(new UserHierarchyContext(
                UserId: user.Id,
                Path: hierarchyPath,
                JobType: null,
                IsWorkforce: true,
                IsTech: false
            ));

        return new TestEntities(
            Project: project,
            Area: area,
            Molecule: molecule,
            Companies: new[] { company1, company2 },
            GrantTypes: new Dictionary<string, GrantType>
            {
                ["Shift.View"] = viewShiftGrantType,
                ["Shift.Edit"] = editShiftGrantType,
                ["Admin.Full"] = adminGrantType
            },
            User: user
        );
    }

    [Fact]
    public async Task HasGrant_WithNoGrants_ReturnsFalse()
    {
        // Arrange
        var entities = await SetupTestEntitiesAsync();

        // Act
        var hasGrant = await _service.HasGrantAsync(entities.User.Id, "Shift.View");

        // Assert
        hasGrant.Should().BeFalse("User without grants should not have access");
    }

    [Fact]
    public async Task HasGrant_WithMatchingGrant_ReturnsTrue()
    {
        // Arrange
        var entities = await SetupTestEntitiesAsync();

        // Give user the grant
        await _service.GrantAsync(
            entities.User.Id,
            entities.GrantTypes["Shift.View"].Id,
            GrantScope.Self()
        );

        // Act
        var hasGrant = await _service.HasGrantAsync(entities.User.Id, "Shift.View");

        // Assert
        hasGrant.Should().BeTrue("User with matching grant should have access");
    }

    [Fact]
    public async Task HasGrant_WithDifferentGrant_ReturnsFalse()
    {
        // Arrange
        var entities = await SetupTestEntitiesAsync();

        // Give user a different grant
        await _service.GrantAsync(
            entities.User.Id,
            entities.GrantTypes["Shift.View"].Id,
            GrantScope.Self()
        );

        // Act
        var hasGrant = await _service.HasGrantAsync(entities.User.Id, "Shift.Edit");

        // Assert
        hasGrant.Should().BeFalse("User should not have access to grants they don't have");
    }

    [Fact]
    public async Task HasGrantWithScope_CompanyScope_MatchesUserCompany()
    {
        // Arrange
        var entities = await SetupTestEntitiesAsync();

        // Give user grant scoped to their company
        await _service.GrantAsync(
            entities.User.Id,
            entities.GrantTypes["Shift.View"].Id,
            GrantScope.Company(entities.Companies[0].Id)
        );

        // Act - Check against same company
        var hasGrantSameCompany = await _service.HasGrantWithScopeAsync(
            entities.User.Id,
            "Shift.View",
            companyId: entities.Companies[0].Id
        );

        // Act - Check against different company
        var hasGrantDifferentCompany = await _service.HasGrantWithScopeAsync(
            entities.User.Id,
            "Shift.View",
            companyId: entities.Companies[1].Id
        );

        // Assert
        hasGrantSameCompany.Should().BeTrue("Grant should match user's company");
        hasGrantDifferentCompany.Should().BeFalse("Grant should not match different company");
    }

    [Fact]
    public async Task HasGrantWithScope_MoleculeScope_CoversAllCompanies()
    {
        // Arrange
        var entities = await SetupTestEntitiesAsync();

        // Give user grant scoped to molecule
        await _service.GrantAsync(
            entities.User.Id,
            entities.GrantTypes["Shift.View"].Id,
            GrantScope.Molecule(entities.Molecule.Id)
        );

        // Act - Check against molecule
        var hasGrant = await _service.HasGrantWithScopeAsync(
            entities.User.Id,
            "Shift.View",
            moleculeId: entities.Molecule.Id
        );

        // Assert
        hasGrant.Should().BeTrue("Molecule-scoped grant should cover molecules");
    }

    [Fact]
    public async Task HasGrantWithScope_AreaScope_CoversMolecules()
    {
        // Arrange
        var entities = await SetupTestEntitiesAsync();

        // Give user grant scoped to area
        await _service.GrantAsync(
            entities.User.Id,
            entities.GrantTypes["Shift.View"].Id,
            GrantScope.Area(entities.Area.Id)
        );

        // Act
        var hasGrant = await _service.HasGrantWithScopeAsync(
            entities.User.Id,
            "Shift.View",
            areaId: entities.Area.Id
        );

        // Assert
        hasGrant.Should().BeTrue("Area-scoped grant should provide access to area");
    }

    [Fact]
    public async Task GrantAsync_CreatesNewGrant_WithCorrectScope()
    {
        // Arrange
        var entities = await SetupTestEntitiesAsync();

        // Act
        var grant = await _service.GrantAsync(
            entities.User.Id,
            entities.GrantTypes["Shift.View"].Id,
            GrantScope.Company(entities.Companies[0].Id),
            grantedByUserId: 999,
            notes: "Test grant"
        );

        // Assert
        grant.Should().NotBeNull();
        grant!.UserId.Should().Be(entities.User.Id);
        grant.GrantTypeId.Should().Be(entities.GrantTypes["Shift.View"].Id);
        grant.CompanyId.Should().Be(entities.Companies[0].Id);
        grant.GrantedByUserId.Should().Be(999);
        grant.Notes.Should().Be("Test grant");
    }

    [Fact]
    public async Task RevokeAsync_RemovesGrant()
    {
        // Arrange
        var entities = await SetupTestEntitiesAsync();

        var grant = await _service.GrantAsync(
            entities.User.Id,
            entities.GrantTypes["Shift.View"].Id,
            GrantScope.Self()
        );

        // Verify grant exists
        var hasGrantBefore = await _service.HasGrantAsync(entities.User.Id, "Shift.View");
        hasGrantBefore.Should().BeTrue();

        // Act
        var revoked = await _service.RevokeAsync(grant!.Id);

        // Assert
        revoked.Should().BeTrue();

        var hasGrantAfter = await _service.HasGrantAsync(entities.User.Id, "Shift.View");
        hasGrantAfter.Should().BeFalse("Revoked grant should no longer provide access");
    }

    [Fact]
    public async Task GetUserGrantsAsync_ReturnsAllUserGrants()
    {
        // Arrange
        var entities = await SetupTestEntitiesAsync();

        // Give user multiple grants
        await _service.GrantAsync(entities.User.Id, entities.GrantTypes["Shift.View"].Id, GrantScope.Self());
        await _service.GrantAsync(entities.User.Id, entities.GrantTypes["Shift.Edit"].Id, GrantScope.Self());

        // Act
        var grants = await _service.GetUserGrantsAsync(entities.User.Id);

        // Assert
        grants.Should().HaveCount(2);
    }

    private record TestEntities(
        Project Project,
        Area Area,
        Molecule Molecule,
        Company[] Companies,
        Dictionary<string, GrantType> GrantTypes,
        AppUser User
    );
}

/// <summary>
/// Tests for Task 13.2: Role Assignments
/// Verifies that role assignments correctly manage auto-grants.
/// </summary>
public class RoleAssignmentTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly GrantService _grantService;
    private readonly Mock<IHierarchyService> _hierarchyServiceMock;

    public RoleAssignmentTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _db = new AppDbContext(options);
        _hierarchyServiceMock = new Mock<IHierarchyService>();
        _grantService = new GrantService(_db, _hierarchyServiceMock.Object);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    private async Task<RoleTestEntities> SetupRoleTestEntitiesAsync()
    {
        // Create hierarchy
        var project = new Project { Name = "TestProject" };
        _db.Projects.Add(project);
        await _db.SaveChangesAsync();

        var area = new Area { ProjectId = project.Id, Name = "TestArea" };
        _db.Areas.Add(area);
        await _db.SaveChangesAsync();

        var molecule = new Molecule { AreaId = area.Id, Name = "TestMolecule", Type = MoleculeType.Workforce };
        _db.Molecules.Add(molecule);
        await _db.SaveChangesAsync();

        var company = new Company { MoleculeId = molecule.Id, Name = "TestCompany" };
        _db.Companies.Add(company);
        await _db.SaveChangesAsync();

        // Create grant types
        var viewShiftGrant = new GrantType { Key = "Shift.View", NameKey = "Grant_Shift_View", Category = GrantCategory.Shift };
        var editShiftGrant = new GrantType { Key = "Shift.Edit", NameKey = "Grant_Shift_Edit", Category = GrantCategory.Shift };
        var viewChoreGrant = new GrantType { Key = "Chore.View", NameKey = "Grant_Chore_View", Category = GrantCategory.Chore };
        _db.GrantTypes.AddRange(viewShiftGrant, editShiftGrant, viewChoreGrant);
        await _db.SaveChangesAsync();

        // Create role template with auto-grants
        var roleTemplate = new RoleTemplate
        {
            Key = "TestRole",
            NameKey = "Role_TestRole",
            DescriptionKey = "Role_TestRole_Desc",
            ScopeLevel = RoleScopeLevel.Company,
            IsSystem = false,
            IsActive = true,
            SortOrder = 1
        };
        _db.RoleTemplates.Add(roleTemplate);
        await _db.SaveChangesAsync();

        // Add auto-grants to the role template
        var autoGrant1 = new RoleTemplateGrant
        {
            RoleTemplateId = roleTemplate.Id,
            GrantTypeId = viewShiftGrant.Id,
            CanOwn = true,
            CanGive = false,
            ScopeMode = GrantScopeMode.SameAsRole
        };
        var autoGrant2 = new RoleTemplateGrant
        {
            RoleTemplateId = roleTemplate.Id,
            GrantTypeId = editShiftGrant.Id,
            CanOwn = true,
            CanGive = false,
            ScopeMode = GrantScopeMode.SameAsRole
        };
        _db.RoleTemplateGrants.AddRange(autoGrant1, autoGrant2);
        await _db.SaveChangesAsync();

        // Create user
        var user = new AppUser
        {
            CompanyId = company.Id,
            Email = "test@test.com",
            DisplayName = "Test User",
            PasswordHash = Array.Empty<byte>(),
            PasswordSalt = Array.Empty<byte>()
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        // Setup hierarchy service mock
        var hierarchyPath = new HierarchyPath(project, area, molecule, company, null);
        _hierarchyServiceMock.Setup(h => h.GetUserHierarchyContextAsync(user.Id))
            .ReturnsAsync(new UserHierarchyContext(
                UserId: user.Id,
                Path: hierarchyPath,
                JobType: null,
                IsWorkforce: true,
                IsTech: false
            ));

        return new RoleTestEntities(
            Area: area,
            Molecule: molecule,
            Company: company,
            GrantTypes: new Dictionary<string, GrantType>
            {
                ["Shift.View"] = viewShiftGrant,
                ["Shift.Edit"] = editShiftGrant,
                ["Chore.View"] = viewChoreGrant
            },
            RoleTemplate: roleTemplate,
            User: user
        );
    }

    [Fact]
    public async Task ApplyAutoGrants_AddsRoleGrants()
    {
        // Arrange
        var entities = await SetupRoleTestEntitiesAsync();

        // Verify no grants initially
        var grantsBefore = await _grantService.GetUserGrantsAsync(entities.User.Id);
        grantsBefore.Should().BeEmpty();

        // Act - Apply auto-grants from role
        await _grantService.ApplyAutoGrantsAsync(
            entities.User.Id,
            entities.RoleTemplate.Id,
            GrantScope.Company(entities.Company.Id)
        );

        // Assert
        var grantsAfter = await _grantService.GetUserGrantsAsync(entities.User.Id);
        grantsAfter.Should().HaveCount(2, "Role has 2 auto-grants");

        var hasViewShift = await _grantService.HasGrantAsync(entities.User.Id, "Shift.View");
        var hasEditShift = await _grantService.HasGrantAsync(entities.User.Id, "Shift.Edit");

        hasViewShift.Should().BeTrue("Auto-grant should provide Shift.View");
        hasEditShift.Should().BeTrue("Auto-grant should provide Shift.Edit");
    }

    [Fact]
    public async Task RemoveAutoGrants_RemovesRoleGrants()
    {
        // Arrange
        var entities = await SetupRoleTestEntitiesAsync();

        // Apply auto-grants first
        await _grantService.ApplyAutoGrantsAsync(
            entities.User.Id,
            entities.RoleTemplate.Id,
            GrantScope.Company(entities.Company.Id)
        );

        // Verify grants were applied
        var grantsBefore = await _grantService.GetUserGrantsAsync(entities.User.Id);
        grantsBefore.Should().HaveCount(2);

        // Act - Remove auto-grants
        await _grantService.RemoveAutoGrantsAsync(entities.User.Id, entities.RoleTemplate.Id);

        // Assert
        var grantsAfter = await _grantService.GetUserGrantsAsync(entities.User.Id);
        grantsAfter.Should().BeEmpty("Removing role should revoke auto-grants");
    }

    [Fact]
    public async Task ApplyAutoGrants_RespectsRoleScope()
    {
        // Arrange
        var entities = await SetupRoleTestEntitiesAsync();

        // Act - Apply auto-grants with company scope
        await _grantService.ApplyAutoGrantsAsync(
            entities.User.Id,
            entities.RoleTemplate.Id,
            GrantScope.Company(entities.Company.Id)
        );

        // Assert - Check grants have correct scope
        var grants = await _grantService.GetUserGrantsAsync(entities.User.Id);
        grants.Should().AllSatisfy(g =>
        {
            g.CompanyId.Should().Be(entities.Company.Id,
                "Grant scope should match role assignment scope");
        });
    }

    [Fact]
    public async Task ApplyAutoGrants_DoesNotDuplicateGrants()
    {
        // Arrange
        var entities = await SetupRoleTestEntitiesAsync();

        // Apply auto-grants twice
        await _grantService.ApplyAutoGrantsAsync(
            entities.User.Id,
            entities.RoleTemplate.Id,
            GrantScope.Company(entities.Company.Id)
        );

        await _grantService.ApplyAutoGrantsAsync(
            entities.User.Id,
            entities.RoleTemplate.Id,
            GrantScope.Company(entities.Company.Id)
        );

        // Assert - Should still only have 2 grants
        var grants = await _grantService.GetUserGrantsAsync(entities.User.Id);
        grants.Should().HaveCount(2, "Auto-grants should not be duplicated");
    }

    private record RoleTestEntities(
        Area Area,
        Molecule Molecule,
        Company Company,
        Dictionary<string, GrantType> GrantTypes,
        RoleTemplate RoleTemplate,
        AppUser User
    );
}
