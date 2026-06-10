using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Services;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Services;

/// <summary>
/// Regression tests for Epic 5: role-template grants are applied scoped to the membership's
/// company (not the user's primary company) when <see cref="OnPostAddMembershipAsync"/> runs.
///
/// Tests exercise <see cref="GrantService.BuildRoleTemplateScopeAsync"/> +
/// <see cref="GrantService.AssignRoleTemplateGrantsAsync"/> — the exact two calls the handler
/// now makes — against a real SQLite DB to prove:
///   1. Grant rows are created for the correct user.
///   2. The grants are scoped to the MEMBERSHIP company (companyId), not the user's primary.
///   3. No grants are written when roleTemplateId is absent (null guard is in the handler).
/// </summary>
public sealed class MembershipGrantApplicationTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly AppDbContext _db;
    private readonly GrantService _grantService;

    // Hierarchy IDs populated in SetupHierarchyAsync()
    private int _projectId;
    private int _areaId;
    private int _moleculeId;
    private int _companyAId;   // user's primary company
    private int _companyBId;   // membership target company (different company, same molecule)
    private int _userId;
    private int _grantTypeId;
    private int _roleTemplateId;

    public MembershipGrantApplicationTests()
    {
        _conn = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        _conn.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conn).Options;
        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();
        _grantService = new GrantService(_db, new Mock<IHierarchyService>().Object, new Mock<IAuditLogService>().Object);
    }

    public void Dispose()
    {
        _db.Dispose();
        _conn.Dispose();
    }

    /// <summary>
    /// Seeds a minimal hierarchy: Project → Area → Molecule → CompanyA + CompanyB.
    /// Also seeds a user whose primary company is CompanyA, a GrantType, and a RoleTemplate
    /// ("Employee") with one AutoGrant using ScopeMode=SameAsRole (company-level scope).
    /// </summary>
    private async Task SetupHierarchyAsync()
    {
        var project = new Project { Name = "P", DisplayName = "P" };
        _db.Projects.Add(project);
        await _db.SaveChangesAsync();
        _projectId = project.Id;

        var area = new Area { ProjectId = project.Id, Name = "A", DisplayName = "A" };
        _db.Areas.Add(area);
        await _db.SaveChangesAsync();
        _areaId = area.Id;

        var mol = new Molecule { AreaId = area.Id, Name = "M", DisplayName = "M", Type = MoleculeType.Workforce };
        _db.Molecules.Add(mol);
        await _db.SaveChangesAsync();
        _moleculeId = mol.Id;

        var companyA = new Company { MoleculeId = mol.Id, Name = "CompanyA", DisplayName = "CompanyA" };
        var companyB = new Company { MoleculeId = mol.Id, Name = "CompanyB", DisplayName = "CompanyB" };
        _db.Companies.AddRange(companyA, companyB);
        await _db.SaveChangesAsync();
        _companyAId = companyA.Id;
        _companyBId = companyB.Id;

        var grantType = new GrantType { Key = "ViewShifts", NameKey = "Grant_ViewShifts", Category = GrantCategory.Shift };
        _db.GrantTypes.Add(grantType);
        await _db.SaveChangesAsync();
        _grantTypeId = grantType.Id;

        // Role template "Employee" with one auto-grant at company scope (SameAsRole)
        var template = new RoleTemplate
        {
            Key = "Employee",
            NameKey = "RoleTemplate_Employee",
            DescriptionKey = "RoleTemplate_Employee_Desc",
            ScopeLevel = RoleScopeLevel.Company,
            IsSystem = true,
            IsActive = true,
            DerivedUserRole = UserRole.Employee
        };
        _db.RoleTemplates.Add(template);
        await _db.SaveChangesAsync();
        _roleTemplateId = template.Id;

        _db.RoleTemplateGrants.Add(new RoleTemplateGrant
        {
            RoleTemplateId = template.Id,
            GrantTypeId = grantType.Id,
            CanOwn = true,
            CanGive = false,
            ScopeMode = GrantScopeMode.SameAsRole,
            UseOwnJobType = false
        });
        await _db.SaveChangesAsync();

        // User whose primary company is CompanyA
        var user = new AppUser
        {
            CompanyId = companyA.Id,
            Email = "u@test.com",
            DisplayName = "TestUser",
            Role = UserRole.Employee,
            RoleTemplateId = template.Id,
            PasswordHash = Array.Empty<byte>(),
            PasswordSalt = Array.Empty<byte>()
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        _userId = user.Id;
    }

    // ─── Test 1: grants land scoped to the MEMBERSHIP company (CompanyB) ──────────────────────

    [Fact]
    public async Task ApplyMembershipGrants_ScopedToMembershipCompany_NotPrimaryCompany()
    {
        // Arrange
        await SetupHierarchyAsync();

        // This mirrors exactly what OnPostAddMembershipAsync now does:
        //   var grantScope = await BuildGrantScopeForTemplateAsync(roleTemplate.Key, companyId, jobTypeId);
        //   await _grantService.AssignRoleTemplateGrantsAsync(userId, roleTemplate.Key, grantScope, adminId);
        var grantScope = await _grantService.BuildRoleTemplateScopeAsync("Employee", _companyBId, jobTypeId: null);
        var count = await _grantService.AssignRoleTemplateGrantsAsync(_userId, "Employee", grantScope, grantedByUserId: 1);

        // Assert: at least one grant was created
        count.Should().BeGreaterThan(0, "the Employee template has an auto-grant");

        // Assert: the grant's CompanyId is CompanyB (the membership company), NOT CompanyA (primary)
        var grants = await _db.Grants
            .Where(g => g.UserId == _userId)
            .ToListAsync();

        grants.Should().NotBeEmpty();
        grants.Should().AllSatisfy(g =>
            g.CompanyId.Should().Be(_companyBId,
                "grants from membership scope must be tied to the membership company, not the user's primary"));

        // Negative: no grant scoped to CompanyA (primary company) was created by this call
        grants.Should().NotContain(g => g.CompanyId == _companyAId,
            "primary company must not receive grants from the membership application");
    }

    // ─── Test 2: idempotency — second call does not create duplicates ──────────────────────────

    [Fact]
    public async Task ApplyMembershipGrants_Idempotent_NoDuplicatesOnSecondCall()
    {
        await SetupHierarchyAsync();

        var grantScope = await _grantService.BuildRoleTemplateScopeAsync("Employee", _companyBId, jobTypeId: null);

        var firstCount = await _grantService.AssignRoleTemplateGrantsAsync(_userId, "Employee", grantScope, grantedByUserId: 1);
        var secondCount = await _grantService.AssignRoleTemplateGrantsAsync(_userId, "Employee", grantScope, grantedByUserId: 1);

        firstCount.Should().BeGreaterThan(0, "first application creates grants");
        secondCount.Should().Be(0, "second application is a no-op (dedup is built into AssignRoleTemplateGrantsAsync)");

        var totalGrants = await _db.Grants.CountAsync(g => g.UserId == _userId);
        totalGrants.Should().Be(firstCount, "no duplicate Grant rows were inserted");
    }

    // ─── Test 3: BuildRoleTemplateScopeAsync uses the MEMBERSHIP company's hierarchy ───────────

    [Fact]
    public async Task BuildRoleTemplateScopeAsync_UsesCorrectCompanyHierarchy_ForMembershipCompany()
    {
        await SetupHierarchyAsync();

        // For "Employee", BuildRoleTemplateScopeAsync returns CompanyId + MoleculeId (see GrantService.cs)
        var scope = await _grantService.BuildRoleTemplateScopeAsync("Employee", _companyBId, jobTypeId: null);

        scope.CompanyId.Should().Be(_companyBId,
            "scope must resolve to the membership company, not the user's primary");
        scope.MoleculeId.Should().Be(_moleculeId,
            "scope must carry the molecule ID from the membership company's hierarchy");
    }

    // ─── Test 4: no-template path — zero grants created ──────────────────────────────────────

    [Fact]
    public async Task ApplyMembershipGrants_WithUnknownTemplateKey_CreatesNoGrants()
    {
        await SetupHierarchyAsync();

        // Simulates the guard inside the handler: if roleTemplate is null, no calls are made.
        // Here we directly test AssignRoleTemplateGrantsAsync with a non-existent key.
        var grantScope = await _grantService.BuildRoleTemplateScopeAsync("NonExistentTemplate", _companyBId, jobTypeId: null);
        var count = await _grantService.AssignRoleTemplateGrantsAsync(_userId, "NonExistentTemplate", grantScope, grantedByUserId: 1);

        count.Should().Be(0, "a missing/inactive template produces no grants");
        var grants = await _db.Grants.Where(g => g.UserId == _userId).ToListAsync();
        grants.Should().BeEmpty("no rows written for an unknown template key");
    }
}
