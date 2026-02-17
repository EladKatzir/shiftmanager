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
        _service = new GrantService(_db, _hierarchyServiceMock.Object, new Mock<IAuditLogService>().Object);
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

        // Act - System grant (no granter) to bypass CanGive enforcement
        var grant = await _service.GrantAsync(
            entities.User.Id,
            entities.GrantTypes["Shift.View"].Id,
            GrantScope.Company(entities.Companies[0].Id),
            grantedByUserId: null,
            notes: "Test grant"
        );

        // Assert
        grant.Should().NotBeNull();
        grant!.UserId.Should().Be(entities.User.Id);
        grant.GrantTypeId.Should().Be(entities.GrantTypes["Shift.View"].Id);
        grant.CompanyId.Should().Be(entities.Companies[0].Id);
        grant.GrantedByUserId.Should().BeNull("System grants have no granter");
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
        _grantService = new GrantService(_db, _hierarchyServiceMock.Object, new Mock<IAuditLogService>().Object);
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

/// <summary>
/// Tests for V3 Audit - CanGive Delegation Enforcement
/// Verifies that CanGive permission is required to grant permissions to others.
/// </summary>
public class CanGiveDelegationTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly GrantService _grantService;
    private readonly Mock<IHierarchyService> _hierarchyServiceMock;

    public CanGiveDelegationTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _db = new AppDbContext(options);
        _hierarchyServiceMock = new Mock<IHierarchyService>();
        _grantService = new GrantService(_db, _hierarchyServiceMock.Object, new Mock<IAuditLogService>().Object);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    private async Task<CanGiveTestEntities> SetupCanGiveTestEntitiesAsync()
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

        var company = new Company { MoleculeId = molecule.Id, Name = "TestCompany", DisplayName = "Test Company" };
        _db.Companies.Add(company);
        await _db.SaveChangesAsync();

        // Create grant types
        var viewShiftGrantType = new GrantType { Key = "ViewShifts", NameKey = "Grant_ViewShifts", Category = GrantCategory.Shift };
        var assignShiftGrantType = new GrantType { Key = "AssignAlhutShifts", NameKey = "Grant_AssignAlhutShifts", Category = GrantCategory.Shift };
        _db.GrantTypes.AddRange(viewShiftGrantType, assignShiftGrantType);
        await _db.SaveChangesAsync();

        // Create users - one with CanGive, one without
        var granterWithCanGive = new AppUser
        {
            CompanyId = company.Id,
            Email = "granter-cangive@test.com",
            DisplayName = "Granter With CanGive",
            PasswordHash = Array.Empty<byte>(),
            PasswordSalt = Array.Empty<byte>()
        };
        var granterWithoutCanGive = new AppUser
        {
            CompanyId = company.Id,
            Email = "granter-nocangive@test.com",
            DisplayName = "Granter Without CanGive",
            PasswordHash = Array.Empty<byte>(),
            PasswordSalt = Array.Empty<byte>()
        };
        var targetUser = new AppUser
        {
            CompanyId = company.Id,
            Email = "target@test.com",
            DisplayName = "Target User",
            PasswordHash = Array.Empty<byte>(),
            PasswordSalt = Array.Empty<byte>()
        };
        _db.Users.AddRange(granterWithCanGive, granterWithoutCanGive, targetUser);
        await _db.SaveChangesAsync();

        // Give granterWithCanGive a grant with CanGive=true
        var grantWithCanGive = new Grant
        {
            UserId = granterWithCanGive.Id,
            GrantTypeId = assignShiftGrantType.Id,
            MoleculeId = molecule.Id,
            CanOwn = true,
            CanGive = true,
            GrantedAt = DateTime.UtcNow,
            IsAutoGrant = false
        };
        _db.Grants.Add(grantWithCanGive);

        // Give granterWithoutCanGive a grant with CanGive=false
        var grantWithoutCanGive = new Grant
        {
            UserId = granterWithoutCanGive.Id,
            GrantTypeId = assignShiftGrantType.Id,
            MoleculeId = molecule.Id,
            CanOwn = true,
            CanGive = false,
            GrantedAt = DateTime.UtcNow,
            IsAutoGrant = false
        };
        _db.Grants.Add(grantWithoutCanGive);
        await _db.SaveChangesAsync();

        return new CanGiveTestEntities(
            Project: project,
            Area: area,
            Molecule: molecule,
            Company: company,
            GrantTypes: new Dictionary<string, GrantType>
            {
                ["ViewShifts"] = viewShiftGrantType,
                ["AssignAlhutShifts"] = assignShiftGrantType
            },
            GranterWithCanGive: granterWithCanGive,
            GranterWithoutCanGive: granterWithoutCanGive,
            TargetUser: targetUser
        );
    }

    [Fact]
    public async Task CanUserGrantAsync_WithCanGiveTrue_ReturnsTrue()
    {
        // Arrange
        var entities = await SetupCanGiveTestEntitiesAsync();

        // Act
        var canGrant = await _grantService.CanUserGrantAsync(
            entities.GranterWithCanGive.Id,
            entities.GrantTypes["AssignAlhutShifts"].Id,
            GrantScope.Molecule(entities.Molecule.Id)
        );

        // Assert
        canGrant.Should().BeTrue("User with CanGive=true should be able to grant");
    }

    [Fact]
    public async Task CanUserGrantAsync_WithCanGiveFalse_ReturnsFalse()
    {
        // Arrange
        var entities = await SetupCanGiveTestEntitiesAsync();

        // Act
        var canGrant = await _grantService.CanUserGrantAsync(
            entities.GranterWithoutCanGive.Id,
            entities.GrantTypes["AssignAlhutShifts"].Id,
            GrantScope.Molecule(entities.Molecule.Id)
        );

        // Assert
        canGrant.Should().BeFalse("User with CanGive=false should not be able to grant");
    }

    [Fact]
    public async Task CanUserGrantAsync_WithNoGrant_ReturnsFalse()
    {
        // Arrange
        var entities = await SetupCanGiveTestEntitiesAsync();

        // Act - Target user has no grants at all
        var canGrant = await _grantService.CanUserGrantAsync(
            entities.TargetUser.Id,
            entities.GrantTypes["AssignAlhutShifts"].Id,
            GrantScope.Molecule(entities.Molecule.Id)
        );

        // Assert
        canGrant.Should().BeFalse("User without any grants should not be able to grant");
    }

    [Fact]
    public async Task GrantAsync_WithCanGivePermission_Succeeds()
    {
        // Arrange
        var entities = await SetupCanGiveTestEntitiesAsync();

        // Act
        var grant = await _grantService.GrantAsync(
            entities.TargetUser.Id,
            entities.GrantTypes["AssignAlhutShifts"].Id,
            GrantScope.Company(entities.Company.Id),
            grantedByUserId: entities.GranterWithCanGive.Id
        );

        // Assert
        grant.Should().NotBeNull("Granting with CanGive permission should succeed");
        grant!.UserId.Should().Be(entities.TargetUser.Id);
    }

    [Fact]
    public async Task GrantAsync_WithoutCanGivePermission_ThrowsUnauthorized()
    {
        // Arrange
        var entities = await SetupCanGiveTestEntitiesAsync();

        // Act & Assert
        await FluentActions.Invoking(() => _grantService.GrantAsync(
            entities.TargetUser.Id,
            entities.GrantTypes["AssignAlhutShifts"].Id,
            GrantScope.Company(entities.Company.Id),
            grantedByUserId: entities.GranterWithoutCanGive.Id
        )).Should().ThrowAsync<UnauthorizedAccessException>("Granting without CanGive permission should throw");
    }

    [Fact]
    public async Task GrantAsync_WithNarrowerScope_Succeeds()
    {
        // Arrange
        var entities = await SetupCanGiveTestEntitiesAsync();

        // GranterWithCanGive has molecule-level grant, try to grant company-level (narrower)
        var grant = await _grantService.GrantAsync(
            entities.TargetUser.Id,
            entities.GrantTypes["AssignAlhutShifts"].Id,
            GrantScope.Company(entities.Company.Id), // Company is narrower than Molecule
            grantedByUserId: entities.GranterWithCanGive.Id
        );

        // Assert
        grant.Should().NotBeNull("Granting at narrower scope should succeed");
    }

    [Fact]
    public async Task GrantAsync_WithBroaderScope_ThrowsUnauthorized()
    {
        // Arrange
        var entities = await SetupCanGiveTestEntitiesAsync();

        // GranterWithCanGive has molecule-level grant, try to grant area-level (broader)
        await FluentActions.Invoking(() => _grantService.GrantAsync(
            entities.TargetUser.Id,
            entities.GrantTypes["AssignAlhutShifts"].Id,
            GrantScope.Area(entities.Area.Id), // Area is broader than Molecule
            grantedByUserId: entities.GranterWithCanGive.Id
        )).Should().ThrowAsync<UnauthorizedAccessException>("Granting at broader scope should fail");
    }

    [Fact]
    public async Task GrantAsync_WithNoGranter_Succeeds()
    {
        // Arrange - System/admin grants without a granter
        var entities = await SetupCanGiveTestEntitiesAsync();

        // Act - No grantedByUserId (system grant)
        var grant = await _grantService.GrantAsync(
            entities.TargetUser.Id,
            entities.GrantTypes["AssignAlhutShifts"].Id,
            GrantScope.Company(entities.Company.Id),
            grantedByUserId: null
        );

        // Assert
        grant.Should().NotBeNull("System grants without granter should succeed");
    }

    private record CanGiveTestEntities(
        Project Project,
        Area Area,
        Molecule Molecule,
        Company Company,
        Dictionary<string, GrantType> GrantTypes,
        AppUser GranterWithCanGive,
        AppUser GranterWithoutCanGive,
        AppUser TargetUser
    );
}

/// <summary>
/// Tests for V3 Audit - Assigner Role Configuration
/// Verifies that Assigner role has only chore grants, not shift grants.
/// </summary>
public class AssignerRoleTests : IDisposable
{
    private readonly AppDbContext _db;

    public AssignerRoleTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _db = new AppDbContext(options);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    [Fact]
    public void RoleTemplateSeed_AssignerRole_HasOnlyChoreGrants()
    {
        // Arrange
        var roleTemplates = Data.SeedData.RoleTemplateSeed.GetRoleTemplates();
        var roleGrants = Data.SeedData.RoleTemplateSeed.GetRoleTemplateGrants();
        var grantTypes = Data.SeedData.GrantTypeSeed.GetGrantTypes();

        // Find Assigner role (ID 8)
        var assignerRole = roleTemplates.FirstOrDefault(rt => rt.Key == "Assigner");
        assignerRole.Should().NotBeNull("Assigner role should exist");

        // Get grants for Assigner role
        var assignerGrants = roleGrants.Where(rg => rg.RoleTemplateId == assignerRole!.Id).ToList();

        // Get the grant types for those grants
        var assignerGrantTypes = assignerGrants
            .Select(ag => grantTypes.FirstOrDefault(gt => gt.Id == ag.GrantTypeId))
            .Where(gt => gt != null)
            .ToList();

        // Assert - Should only have chore grants
        assignerGrantTypes.Should().NotBeEmpty("Assigner should have some grants");
        assignerGrantTypes.Should().AllSatisfy(gt =>
        {
            gt!.Category.Should().Be(GrantCategory.Chore,
                $"Assigner grant '{gt.Key}' should be a chore grant, not {gt.Category}");
        });

        // Verify specific grants
        var grantKeys = assignerGrantTypes.Select(gt => gt!.Key).ToList();
        grantKeys.Should().Contain("AssignChores", "Assigner should have AssignChores grant");
        grantKeys.Should().Contain("ViewChores", "Assigner should have ViewChores grant");

        // Verify NO shift grants
        grantKeys.Should().NotContain(k => k.Contains("Shift"), "Assigner should NOT have any shift grants");
    }
}

/// <summary>
/// Tests for V3 Audit - Grant Type Seed Count
/// Verifies that all grant types are properly seeded.
/// </summary>
public class GrantTypeSeedTests
{
    [Fact]
    public void GrantTypeSeed_Creates_ExpectedNumberOfGrants()
    {
        // Arrange & Act
        var grantTypes = Data.SeedData.GrantTypeSeed.GetGrantTypes();

        // Assert - 122 grants: 107 original + 3 calendar grants + 4 navigation grants + 3 join request grants + 1 ExportUserData grant + 4 dynamic role grants (ManageAnnouncements, ViewSystemAlerts, ViewAllAreas, ManageOnDuty)
        grantTypes.Should().HaveCount(122, "Should have exactly 122 grant types including calendar, navigation, join request, export, and dynamic role grants");
    }

    [Fact]
    public void GrantTypeSeed_HasUniqueIds()
    {
        // Arrange & Act
        var grantTypes = Data.SeedData.GrantTypeSeed.GetGrantTypes();
        var ids = grantTypes.Select(gt => gt.Id).ToList();

        // Assert
        ids.Should().OnlyHaveUniqueItems("All grant type IDs should be unique");
    }

    [Fact]
    public void GrantTypeSeed_HasUniqueKeys()
    {
        // Arrange & Act
        var grantTypes = Data.SeedData.GrantTypeSeed.GetGrantTypes();
        var keys = grantTypes.Select(gt => gt.Key).ToList();

        // Assert
        keys.Should().OnlyHaveUniqueItems("All grant type keys should be unique");
    }

    [Fact]
    public void GrantTypeSeed_HasAllRequiredShiftCalendarGrants()
    {
        // Arrange & Act
        var grantTypes = Data.SeedData.GrantTypeSeed.GetGrantTypes();
        var keys = grantTypes.Select(gt => gt.Key).ToList();

        // Assert - Shift calendars
        keys.Should().Contain("ViewAlhutShiftCalendar");
        keys.Should().Contain("ViewTextShiftCalendar");
        keys.Should().Contain("ViewBRShiftCalendar");
        keys.Should().Contain("ViewHakamShiftCalendar");
    }

    [Fact]
    public void GrantTypeSeed_HasAllRequiredTechGrants()
    {
        // Arrange & Act
        var grantTypes = Data.SeedData.GrantTypeSeed.GetGrantTypes();
        var keys = grantTypes.Select(gt => gt.Key).ToList();

        // Assert - Tech calendars
        keys.Should().Contain("ViewHanavaCalendar");
        keys.Should().Contain("ViewDeltaCalendar");
        keys.Should().Contain("ViewYekevCalendar");
        keys.Should().Contain("ViewMoviltechCalendar");

        // Assert - Tech assignment
        keys.Should().Contain("AssignHanavaShifts");
        keys.Should().Contain("AssignDeltaShifts");
        keys.Should().Contain("AssignYekevShifts");
        keys.Should().Contain("AssignMoviltechShifts");

        // Assert - Tech eligibility
        keys.Should().Contain("CanBeAssignedHanava");
        keys.Should().Contain("CanBeAssignedDelta");
        keys.Should().Contain("CanBeAssignedYekev");
        keys.Should().Contain("CanBeAssignedMoviltech");
    }

    [Fact]
    public void GrantTypeSeed_HasAllRequiredHelperMoleculeGrants()
    {
        // Arrange & Act
        var grantTypes = Data.SeedData.GrantTypeSeed.GetGrantTypes();
        var keys = grantTypes.Select(gt => gt.Key).ToList();

        // Assert - Shiklut grants
        keys.Should().Contain("ViewShiklutCalendar");
        keys.Should().Contain("AssignShiklutChores");
        keys.Should().Contain("ManageShiklutBlueprints");
        keys.Should().Contain("ManageShiklutPrograms");
        keys.Should().Contain("CanBeAssignedShiklut");

        // Assert - NOC grants
        keys.Should().Contain("ViewNOCCalendar");
        keys.Should().Contain("AssignNOCChores");
        keys.Should().Contain("ManageNOCBlueprints");
        keys.Should().Contain("ManageNOCPrograms");
        keys.Should().Contain("CanBeAssignedNOC");
    }

    [Fact]
    public void GrantTypeSeed_HasKatzinDutyGrants()
    {
        // Arrange & Act
        var grantTypes = Data.SeedData.GrantTypeSeed.GetGrantTypes();
        var keys = grantTypes.Select(gt => gt.Key).ToList();

        // Assert - Katzin duty grants
        keys.Should().Contain("ManageKatzinBlueprints");
        keys.Should().Contain("ManageKatzinPrograms");
    }

    [Fact]
    public void GrantTypeSeed_HasJoinRequestGrants()
    {
        // Arrange & Act
        var grantTypes = Data.SeedData.GrantTypeSeed.GetGrantTypes();
        var keys = grantTypes.Select(gt => gt.Key).ToList();

        // Assert - Join request grants
        keys.Should().Contain("ManageJoinRequests");
        keys.Should().Contain("ViewCompanyUsers");
        keys.Should().Contain("EditCompanyUsers");
    }
}

/// <summary>
/// Tests for Scope Resolution in GrantService
/// Verifies GetAccessibleCompanyIdsForGrantAsync and HasGrantForCompanyAsync methods.
/// </summary>
public class GrantScopeResolutionTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly GrantService _grantService;
    private readonly Mock<IHierarchyService> _hierarchyServiceMock;

    // Test data
    private Project _project = null!;
    private Area _area = null!;
    private Molecule _molecule = null!;
    private Company _company1 = null!;
    private Company _company2 = null!;
    private AppUser _testUser = null!;
    private GrantType _manageJoinRequestsGrant = null!;

    public GrantScopeResolutionTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _db = new AppDbContext(options);
        _hierarchyServiceMock = new Mock<IHierarchyService>();
        _grantService = new GrantService(_db, _hierarchyServiceMock.Object, new Mock<IAuditLogService>().Object);

        SetupTestData().GetAwaiter().GetResult();
    }

    private async Task SetupTestData()
    {
        // Create hierarchy: Project > Area > Molecule > Companies
        _project = new Project { Name = "Test Project" };
        _db.Projects.Add(_project);
        await _db.SaveChangesAsync();

        _area = new Area { ProjectId = _project.Id, Name = "Test Area" };
        _db.Areas.Add(_area);
        await _db.SaveChangesAsync();

        _molecule = new Molecule { AreaId = _area.Id, Name = "Test Molecule", Type = MoleculeType.Workforce };
        _db.Molecules.Add(_molecule);
        await _db.SaveChangesAsync();

        _company1 = new Company { MoleculeId = _molecule.Id, Name = "Company 1" };
        _company2 = new Company { MoleculeId = _molecule.Id, Name = "Company 2" };
        _db.Companies.AddRange(_company1, _company2);
        await _db.SaveChangesAsync();

        // Create test user
        _testUser = new AppUser
        {
            CompanyId = _company1.Id,
            Email = "test@test.com",
            DisplayName = "Test User",
            PasswordHash = Array.Empty<byte>(),
            PasswordSalt = Array.Empty<byte>()
        };
        _db.Users.Add(_testUser);
        await _db.SaveChangesAsync();

        // Create ManageJoinRequests grant type
        _manageJoinRequestsGrant = new GrantType
        {
            Key = "ManageJoinRequests",
            NameKey = "Grant_ManageJoinRequests",
            DescriptionKey = "Grant_ManageJoinRequests_Desc",
            Category = GrantCategory.UserManagement,
            DefaultScope = GrantScopeLevel.Company,
            IsSystem = true,
            IsActive = true
        };
        _db.GrantTypes.Add(_manageJoinRequestsGrant);
        await _db.SaveChangesAsync();

        // Setup hierarchy service mock
        _hierarchyServiceMock.Setup(h => h.GetUserHierarchyContextAsync(_testUser.Id))
            .ReturnsAsync(new UserHierarchyContext(
                _testUser.Id,
                new HierarchyPath(_project, _area, _molecule, _company1, null),
                JobType: null,
                IsWorkforce: true,
                IsTech: false
            ));
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    [Fact]
    public async Task GetAccessibleCompanyIdsForGrantAsync_WithCompanyScope_ReturnsOnlyThatCompany()
    {
        // Arrange - Grant with Company scope
        var grant = new Grant
        {
            UserId = _testUser.Id,
            GrantTypeId = _manageJoinRequestsGrant.Id,
            CompanyId = _company1.Id,
            CanOwn = true
        };
        _db.Grants.Add(grant);
        await _db.SaveChangesAsync();

        // Act
        var result = await _grantService.GetAccessibleCompanyIdsForGrantAsync(_testUser.Id, "ManageJoinRequests");

        // Assert
        result.Should().HaveCount(1);
        result.Should().Contain(_company1.Id);
        result.Should().NotContain(_company2.Id);
    }

    [Fact]
    public async Task GetAccessibleCompanyIdsForGrantAsync_WithMoleculeScope_ReturnsAllCompaniesInMolecule()
    {
        // Arrange - Grant with Molecule scope
        var grant = new Grant
        {
            UserId = _testUser.Id,
            GrantTypeId = _manageJoinRequestsGrant.Id,
            MoleculeId = _molecule.Id,
            CanOwn = true
        };
        _db.Grants.Add(grant);
        await _db.SaveChangesAsync();

        // Act
        var result = await _grantService.GetAccessibleCompanyIdsForGrantAsync(_testUser.Id, "ManageJoinRequests");

        // Assert
        result.Should().HaveCount(2);
        result.Should().Contain(_company1.Id);
        result.Should().Contain(_company2.Id);
    }

    [Fact]
    public async Task GetAccessibleCompanyIdsForGrantAsync_WithSelfScope_ReturnsUserCompany()
    {
        // Arrange - Grant with Self scope (no scope fields set)
        var grant = new Grant
        {
            UserId = _testUser.Id,
            GrantTypeId = _manageJoinRequestsGrant.Id,
            CanOwn = true
            // No CompanyId, MoleculeId, etc. = Self scope
        };
        _db.Grants.Add(grant);
        await _db.SaveChangesAsync();

        // Act
        var result = await _grantService.GetAccessibleCompanyIdsForGrantAsync(_testUser.Id, "ManageJoinRequests");

        // Assert
        result.Should().HaveCount(1);
        result.Should().Contain(_company1.Id);
    }

    [Fact]
    public async Task GetAccessibleCompanyIdsForGrantAsync_WithNoGrant_ReturnsEmptyList()
    {
        // Arrange - No grant

        // Act
        var result = await _grantService.GetAccessibleCompanyIdsForGrantAsync(_testUser.Id, "ManageJoinRequests");

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task HasGrantForCompanyAsync_WithMatchingScope_ReturnsTrue()
    {
        // Arrange - Grant with Company scope
        var grant = new Grant
        {
            UserId = _testUser.Id,
            GrantTypeId = _manageJoinRequestsGrant.Id,
            CompanyId = _company1.Id,
            CanOwn = true
        };
        _db.Grants.Add(grant);
        await _db.SaveChangesAsync();

        // Act
        var result = await _grantService.HasGrantForCompanyAsync(_testUser.Id, "ManageJoinRequests", _company1.Id);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task HasGrantForCompanyAsync_WithNonMatchingScope_ReturnsFalse()
    {
        // Arrange - Grant only for Company1
        var grant = new Grant
        {
            UserId = _testUser.Id,
            GrantTypeId = _manageJoinRequestsGrant.Id,
            CompanyId = _company1.Id,
            CanOwn = true
        };
        _db.Grants.Add(grant);
        await _db.SaveChangesAsync();

        // Act - Check for Company2
        var result = await _grantService.HasGrantForCompanyAsync(_testUser.Id, "ManageJoinRequests", _company2.Id);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task HasGrantForCompanyAsync_WithMoleculeScope_ReturnsTrueForAnyCompanyInMolecule()
    {
        // Arrange - Grant with Molecule scope
        var grant = new Grant
        {
            UserId = _testUser.Id,
            GrantTypeId = _manageJoinRequestsGrant.Id,
            MoleculeId = _molecule.Id,
            CanOwn = true
        };
        _db.Grants.Add(grant);
        await _db.SaveChangesAsync();

        // Act - Check for both companies
        var result1 = await _grantService.HasGrantForCompanyAsync(_testUser.Id, "ManageJoinRequests", _company1.Id);
        var result2 = await _grantService.HasGrantForCompanyAsync(_testUser.Id, "ManageJoinRequests", _company2.Id);

        // Assert
        result1.Should().BeTrue();
        result2.Should().BeTrue();
    }
}
