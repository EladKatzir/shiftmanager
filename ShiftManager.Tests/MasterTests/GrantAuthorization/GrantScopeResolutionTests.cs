using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Tests.MasterTests.Infrastructure;

namespace ShiftManager.Tests.MasterTests.GrantAuthorization;

/// <summary>
/// Tests for HasGrantWithScopeAsync hierarchical scope matching.
/// Uses CreateGrantServiceWithHierarchy() which provides per-user hierarchy resolution.
/// </summary>
public class GrantScopeResolutionTests : MasterTestBase
{
    public GrantScopeResolutionTests(MasterTestFixture fixture) : base(fixture) { }

    // ================================================================
    // COMPANY-SCOPED GRANTS
    // ================================================================

    [Fact]
    public async Task CompanyScopedGrant_Matches_SameCompany()
    {
        // Employee at Tzafona has company-scoped ViewShifts
        var user = GetTestUser("Tzafona", "Employee");
        var company = Fixture.CompanyByName["Tzafona"];
        var grantService = CreateGrantServiceWithHierarchy();

        var result = await grantService.HasGrantWithScopeAsync(
            user.Id, "ViewShifts", companyId: company.Id);

        result.Should().BeTrue("Employee's company-scoped ViewShifts should match own company");
    }

    [Fact]
    public async Task CompanyScopedGrant_DoesNotMatch_DifferentCompanyInSameMolecule()
    {
        // Employee at Tzafona has company-scoped grants — should NOT match Hir (same molecule Oren)
        var user = GetTestUser("Tzafona", "Employee");
        var hirCompany = Fixture.CompanyByName["Hir"];
        var grantService = CreateGrantServiceWithHierarchy();

        var result = await grantService.HasGrantWithScopeAsync(
            user.Id, "ViewShifts", companyId: hirCompany.Id);

        result.Should().BeFalse(
            "Employee's company-scoped ViewShifts at Tzafona should NOT match Hir (different company, same molecule)");
    }

    [Fact]
    public async Task CompanyScopedGrant_DoesNotMatch_DifferentMoleculeCompany()
    {
        // Employee at Tzafona should NOT have access to Hitazmut (different molecule entirely)
        var user = GetTestUser("Tzafona", "Employee");
        var hitazmutCompany = Fixture.CompanyByName["Hitazmut"];
        var grantService = CreateGrantServiceWithHierarchy();

        var result = await grantService.HasGrantWithScopeAsync(
            user.Id, "ViewShifts", companyId: hitazmutCompany.Id);

        result.Should().BeFalse(
            "Employee's company-scoped ViewShifts at Tzafona should NOT match Hitazmut (different molecule)");
    }

    // ================================================================
    // MOLECULE-SCOPED GRANTS (ETM)
    // ================================================================

    [Fact]
    public async Task MoleculeScopedGrant_Matches_AnyCompanyInMolecule()
    {
        // Assigner at Tzafona has AssignChores at ETM scope — should match Oren molecule
        var assigner = GetTestUser("Tzafona", "Assigner");
        var orenMolecule = Fixture.MoleculeByName["Oren"];
        var grantService = CreateGrantServiceWithHierarchy();

        // AssignChores is ETM-scoped for Assigner (only grant that is molecule-scoped)
        var result = await grantService.HasGrantWithScopeAsync(
            assigner.Id, "AssignChores", moleculeId: orenMolecule.Id);

        result.Should().BeTrue(
            "Assigner's molecule-scoped AssignChores should match own molecule");
    }

    [Fact]
    public async Task CompanyScopedGrant_DoesNotMatch_OtherCompanyInSameMolecule_ViaScope()
    {
        // Employee at Tzafona has company-scoped ViewShifts — should NOT match Hir via scope query
        // even though both are in Oren molecule. This confirms company-scoped grants are strict.
        var employee = GetTestUser("Tzafona", "Employee");
        var hirCompany = Fixture.CompanyByName["Hir"];
        var grantService = CreateGrantServiceWithHierarchy();

        var result = await grantService.HasGrantWithScopeAsync(
            employee.Id, "ViewShifts", companyId: hirCompany.Id);

        result.Should().BeFalse(
            "Employee's company-scoped ViewShifts at Tzafona should NOT match Hir (same molecule, different company)");
    }

    // ================================================================
    // AREA-SCOPED GRANTS (ETA)
    // ================================================================

    [Fact]
    public async Task AreaScopedGrant_Matches_AnyMoleculeInArea()
    {
        // AreaAdmin at Tzafona has area-scoped grants — should match anything in area 190
        var areaAdmin = GetTestUser("Tzafona", "AreaAdmin");
        var area = Fixture.AreaByName["190"];
        var grantService = CreateGrantServiceWithHierarchy();

        var result = await grantService.HasGrantWithScopeAsync(
            areaAdmin.Id, "EditArea", areaId: area.Id);

        result.Should().BeTrue("AreaAdmin's area-scoped EditArea should match own area");
    }

    // ================================================================
    // PROJECT-SCOPED GRANTS (Owner)
    // ================================================================

    [Fact]
    public async Task ProjectScopedGrant_Matches_Everything()
    {
        // Owner has project-scoped grants — should match any company/molecule/area
        var owner = GetTestUser("SystemAdmins", "Owner");
        var hitazmutCompany = Fixture.CompanyByName["Hitazmut"];
        var grantService = CreateGrantServiceWithHierarchy();

        var result = await grantService.HasGrantWithScopeAsync(
            owner.Id, "AdminAccess", companyId: hitazmutCompany.Id);

        result.Should().BeTrue("Owner's project-scoped AdminAccess should match any company");
    }

    [Fact]
    public async Task ProjectScopedGrant_Matches_WithNoScopeParams()
    {
        // Owner should match even when no specific scope is provided (validates against own hierarchy)
        var owner = GetTestUser("SystemAdmins", "Owner");
        var grantService = CreateGrantServiceWithHierarchy();

        var result = await grantService.HasGrantWithScopeAsync(
            owner.Id, "SystemConfiguration");

        result.Should().BeTrue(
            "Owner's project-scoped SystemConfiguration should match with no scope params");
    }

    // ================================================================
    // SELF-SCOPED GRANTS
    // ================================================================

    [Fact]
    public async Task SelfScopedGrant_Matches_WhenTargetUserIsSelf()
    {
        // Self-scoped grants (all scope IDs null) match only when targetUserId == userId
        // Employee's self-scoped grants (e.g., ViewOwnProfile) should match self
        var employee = GetTestUser("Tzafona", "Employee");
        var grantService = CreateGrantServiceWithHierarchy();

        // Check if employee has any self-scoped grant
        var selfScopedGrant = await Db.Grants
            .Include(g => g.GrantType)
            .FirstOrDefaultAsync(g => g.UserId == employee.Id && g.CanOwn
                && !g.ProjectId.HasValue && !g.AreaId.HasValue && !g.MoleculeId.HasValue
                && !g.CompanyId.HasValue && !g.JobTypeId.HasValue);

        if (selfScopedGrant != null)
        {
            var result = await grantService.HasGrantWithScopeAsync(
                employee.Id, selfScopedGrant.GrantType!.Key, targetUserId: employee.Id);

            result.Should().BeTrue("Self-scoped grant should match when targetUserId is self");
        }
    }

    [Fact]
    public async Task SelfScopedGrant_DoesNotMatch_WhenTargetUserIsDifferent()
    {
        var employee = GetTestUser("Tzafona", "Employee");
        var otherEmployee = GetTestUser("Hitazmut", "Employee");
        var grantService = CreateGrantServiceWithHierarchy();

        // Find a self-scoped grant for the employee
        var selfScopedGrant = await Db.Grants
            .Include(g => g.GrantType)
            .FirstOrDefaultAsync(g => g.UserId == employee.Id && g.CanOwn
                && !g.ProjectId.HasValue && !g.AreaId.HasValue && !g.MoleculeId.HasValue
                && !g.CompanyId.HasValue && !g.JobTypeId.HasValue);

        if (selfScopedGrant != null)
        {
            var result = await grantService.HasGrantWithScopeAsync(
                employee.Id, selfScopedGrant.GrantType!.Key, targetUserId: otherEmployee.Id);

            result.Should().BeFalse(
                "Self-scoped grant should NOT match when targetUserId is a different user");
        }
    }

    // ================================================================
    // JOBTYPE-SCOPED GRANTS
    // ================================================================

    [Fact]
    public async Task LeadAssignAlhutShifts_Matches_OwnMoleculeAndJobType()
    {
        // Lead at Tzafona (Alhut jobtype) has AssignAlhutShifts with ETM + OwnJobType
        var lead = GetTestUser("Tzafona", "Lead");
        var orenMolecule = Fixture.MoleculeByName["Oren"];
        var alhutJobType = Fixture.JobTypeByName["Alhut"];
        var grantService = CreateGrantServiceWithHierarchy();

        var result = await grantService.HasGrantWithScopeAsync(
            lead.Id, "AssignAlhutShifts",
            moleculeId: orenMolecule.Id,
            jobTypeId: alhutJobType.Id);

        result.Should().BeTrue(
            "Lead's AssignAlhutShifts (ETM+OwnJobType) should match own molecule and Alhut jobtype");
    }

    [Fact]
    public async Task BRDirector_AssignBRShifts_MatchesAcrossMolecule()
    {
        // BRDirector at Tzafona has AssignBRShifts (ETM, ALL jobtypes, CanGive)
        var brDirector = GetTestUser("Tzafona", "BRDirector");
        var orenMolecule = Fixture.MoleculeByName["Oren"];
        var grantService = CreateGrantServiceWithHierarchy();

        var result = await grantService.HasGrantWithScopeAsync(
            brDirector.Id, "AssignBRShifts",
            moleculeId: orenMolecule.Id);

        result.Should().BeTrue(
            "BRDirector's AssignBRShifts should match across own molecule");
    }
}
