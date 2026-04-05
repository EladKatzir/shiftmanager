using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Tests.MasterTests.Infrastructure;

namespace ShiftManager.Tests.MasterTests.GrantAuthorization;

/// <summary>
/// Tests verifying that HasGrantAsync returns false for grants that users should NOT have.
/// Ensures privilege boundaries are maintained across role templates.
/// </summary>
public class AuthorizationDenialTests : MasterTestBase
{
    public AuthorizationDenialTests(MasterTestFixture fixture) : base(fixture) { }

    // ================================================================
    // THEORY-DRIVEN DENIAL CHECKS
    // ================================================================

    [Theory]
    [MemberData(nameof(TestUserMatrix.DeniedGrantCombinations), MemberType = typeof(TestUserMatrix))]
    public async Task User_DoesNotHaveGrant_ForDeniedCombination(string company, string template, string grantKey)
    {
        var user = GetTestUser(company, template);
        var grantType = Fixture.GrantTypeByKey[grantKey];

        var hasGrant = await Db.Grants
            .AnyAsync(g => g.UserId == user.Id && g.GrantTypeId == grantType.Id && g.CanOwn);

        hasGrant.Should().BeFalse(
            $"{template} at {company} should NOT have grant '{grantKey}'");
    }

    // ================================================================
    // EMPLOYEE DENIALS
    // ================================================================

    [Fact]
    public async Task Employee_Cannot_AssignAlhutShifts()
    {
        var employee = GetTestUser("Tzafona", "Employee");
        var grantType = Fixture.GrantTypeByKey["AssignAlhutShifts"];

        var hasGrant = await Db.Grants
            .AnyAsync(g => g.UserId == employee.Id && g.GrantTypeId == grantType.Id && g.CanOwn);

        hasGrant.Should().BeFalse("Employee should NOT be able to assign Alhut shifts");
    }

    [Fact]
    public async Task Employee_Cannot_SystemConfiguration()
    {
        var employee = GetTestUser("Tzafona", "Employee");
        var grantType = Fixture.GrantTypeByKey["SystemConfiguration"];

        var hasGrant = await Db.Grants
            .AnyAsync(g => g.UserId == employee.Id && g.GrantTypeId == grantType.Id && g.CanOwn);

        hasGrant.Should().BeFalse("Employee should NOT have SystemConfiguration");
    }

    [Fact]
    public async Task Employee_Cannot_EditUsers()
    {
        var employee = GetTestUser("Tzafona", "Employee");
        var grantType = Fixture.GrantTypeByKey["EditUsers"];

        var hasGrant = await Db.Grants
            .AnyAsync(g => g.UserId == employee.Id && g.GrantTypeId == grantType.Id && g.CanOwn);

        hasGrant.Should().BeFalse("Employee should NOT have EditUsers");
    }

    [Fact]
    public async Task Employee_Cannot_AdminAccess()
    {
        var employee = GetTestUser("Tzafona", "Employee");
        var grantType = Fixture.GrantTypeByKey["AdminAccess"];

        var hasGrant = await Db.Grants
            .AnyAsync(g => g.UserId == employee.Id && g.GrantTypeId == grantType.Id && g.CanOwn);

        hasGrant.Should().BeFalse("Employee should NOT have AdminAccess");
    }

    [Fact]
    public async Task Employee_Cannot_ApproveVacations()
    {
        var employee = GetTestUser("Tzafona", "Employee");
        var grantType = Fixture.GrantTypeByKey["ApproveVacations"];

        var hasGrant = await Db.Grants
            .AnyAsync(g => g.UserId == employee.Id && g.GrantTypeId == grantType.Id && g.CanOwn);

        hasGrant.Should().BeFalse("Employee should NOT have ApproveVacations");
    }

    [Fact]
    public async Task Employee_Cannot_ManageOnDutyTypes()
    {
        var employee = GetTestUser("Tzafona", "Employee");
        var grantType = Fixture.GrantTypeByKey["ManageOnDutyTypes"];

        var hasGrant = await Db.Grants
            .AnyAsync(g => g.UserId == employee.Id && g.GrantTypeId == grantType.Id && g.CanOwn);

        hasGrant.Should().BeFalse("Employee should NOT have ManageOnDutyTypes");
    }

    // ================================================================
    // ASSIGNER DENIALS
    // ================================================================

    [Fact]
    public async Task Assigner_Cannot_AssignAlhutShifts()
    {
        var assigner = GetTestUser("Tzafona", "Assigner");
        var grantType = Fixture.GrantTypeByKey["AssignAlhutShifts"];

        var hasGrant = await Db.Grants
            .AnyAsync(g => g.UserId == assigner.Id && g.GrantTypeId == grantType.Id && g.CanOwn);

        hasGrant.Should().BeFalse("Assigner should NOT have AssignAlhutShifts");
    }

    [Fact]
    public async Task Assigner_Cannot_ApproveVacations()
    {
        var assigner = GetTestUser("Tzafona", "Assigner");
        var grantType = Fixture.GrantTypeByKey["ApproveVacations"];

        var hasGrant = await Db.Grants
            .AnyAsync(g => g.UserId == assigner.Id && g.GrantTypeId == grantType.Id && g.CanOwn);

        hasGrant.Should().BeFalse("Assigner should NOT have ApproveVacations");
    }

    [Fact]
    public async Task Assigner_Cannot_AdminAccess()
    {
        var assigner = GetTestUser("Tzafona", "Assigner");
        var grantType = Fixture.GrantTypeByKey["AdminAccess"];

        var hasGrant = await Db.Grants
            .AnyAsync(g => g.UserId == assigner.Id && g.GrantTypeId == grantType.Id && g.CanOwn);

        hasGrant.Should().BeFalse("Assigner should NOT have AdminAccess");
    }

    [Fact]
    public async Task Assigner_Cannot_AssignBRShifts()
    {
        var assigner = GetTestUser("Tzafona", "Assigner");
        var grantType = Fixture.GrantTypeByKey["AssignBRShifts"];

        var hasGrant = await Db.Grants
            .AnyAsync(g => g.UserId == assigner.Id && g.GrantTypeId == grantType.Id && g.CanOwn);

        hasGrant.Should().BeFalse("Assigner should NOT have AssignBRShifts");
    }

    // ================================================================
    // BRDIRECTOR DENIALS
    // ================================================================

    [Fact]
    public async Task BRDirector_Cannot_SystemConfiguration()
    {
        var brDirector = GetTestUser("Tzafona", "BRDirector");
        var grantType = Fixture.GrantTypeByKey["SystemConfiguration"];

        var hasGrant = await Db.Grants
            .AnyAsync(g => g.UserId == brDirector.Id && g.GrantTypeId == grantType.Id && g.CanOwn);

        hasGrant.Should().BeFalse("BRDirector should NOT have SystemConfiguration");
    }

    [Fact]
    public async Task BRDirector_CrossCompany_Cannot_SystemConfiguration()
    {
        // Verify for BRDirector at Element too (different molecule)
        var brDirector = GetTestUser("Element", "BRDirector");
        var grantType = Fixture.GrantTypeByKey["SystemConfiguration"];

        var hasGrant = await Db.Grants
            .AnyAsync(g => g.UserId == brDirector.Id && g.GrantTypeId == grantType.Id && g.CanOwn);

        hasGrant.Should().BeFalse("BRDirector at Element should NOT have SystemConfiguration");
    }

    // ================================================================
    // DEPARTMENTLEAD DENIALS
    // ================================================================

    [Fact]
    public async Task DepartmentLead_Cannot_EditArea()
    {
        var deptLead = GetTestUser("Yekev", "DepartmentLead");
        var grantType = Fixture.GrantTypeByKey["EditArea"];

        var hasGrant = await Db.Grants
            .AnyAsync(g => g.UserId == deptLead.Id && g.GrantTypeId == grantType.Id && g.CanOwn);

        hasGrant.Should().BeFalse("DepartmentLead should NOT have EditArea");
    }

    [Fact]
    public async Task DepartmentLead_Cannot_SystemConfiguration()
    {
        var deptLead = GetTestUser("Yekev", "DepartmentLead");
        var grantType = Fixture.GrantTypeByKey["SystemConfiguration"];

        var hasGrant = await Db.Grants
            .AnyAsync(g => g.UserId == deptLead.Id && g.GrantTypeId == grantType.Id && g.CanOwn);

        hasGrant.Should().BeFalse("DepartmentLead should NOT have SystemConfiguration");
    }

    // ================================================================
    // LEAD DENIALS
    // ================================================================

    [Fact]
    public async Task Lead_Cannot_SystemConfiguration()
    {
        var lead = GetTestUser("Tzafona", "Lead");
        var grantType = Fixture.GrantTypeByKey["SystemConfiguration"];

        var hasGrant = await Db.Grants
            .AnyAsync(g => g.UserId == lead.Id && g.GrantTypeId == grantType.Id && g.CanOwn);

        hasGrant.Should().BeFalse("Lead should NOT have SystemConfiguration");
    }

    // ================================================================
    // CROSS-COMPANY DENIALS (using HasGrantWithScopeAsync)
    // ================================================================

    [Fact]
    public async Task Employee_ScopeDenial_CannotAccess_DifferentCompany()
    {
        // Even though Employee has ViewShifts, it should not resolve for a different company
        var employee = GetTestUser("Tzafona", "Employee");
        var elementCompany = Fixture.CompanyByName["Element"];
        var grantService = CreateGrantServiceWithHierarchy();

        var result = await grantService.HasGrantWithScopeAsync(
            employee.Id, "ViewShifts", companyId: elementCompany.Id);

        result.Should().BeFalse(
            "Employee at Tzafona should NOT have ViewShifts scoped to Element (different molecule)");
    }

    [Fact]
    public async Task Lead_ScopeDenial_CannotAccess_DifferentMolecule()
    {
        // Lead at Tzafona (Oren molecule) should not have AssignChores in Ella molecule
        var lead = GetTestUser("Tzafona", "Lead");
        var ellaMolecule = Fixture.MoleculeByName["Ella"];
        var grantService = CreateGrantServiceWithHierarchy();

        var result = await grantService.HasGrantWithScopeAsync(
            lead.Id, "AssignChores", moleculeId: ellaMolecule.Id);

        result.Should().BeFalse(
            "Lead at Oren should NOT have AssignChores in Ella molecule");
    }

    [Fact]
    public async Task NonExistentGrant_ReturnsFalse()
    {
        // Querying a grant key that does not exist should return false, not throw
        var employee = GetTestUser("Tzafona", "Employee");
        var grantService = CreateGrantServiceWithHierarchy();

        var result = await grantService.HasGrantWithScopeAsync(
            employee.Id, "CompletelyFakeGrantThatDoesNotExist");

        result.Should().BeFalse("Non-existent grant key should return false");
    }
}
