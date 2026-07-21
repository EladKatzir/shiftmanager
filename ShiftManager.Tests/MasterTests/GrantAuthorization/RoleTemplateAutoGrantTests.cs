using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Tests.MasterTests.Infrastructure;

namespace ShiftManager.Tests.MasterTests.GrantAuthorization;

/// <summary>
/// Verifies that role template grants were correctly propagated to users during seeding.
/// Tests both positive grant existence and expected grant counts per template.
/// </summary>
public class RoleTemplateAutoGrantTests : MasterTestBase
{
    public RoleTemplateAutoGrantTests(MasterTestFixture fixture) : base(fixture) { }

    // ================================================================
    // POSITIVE GRANT CHECKS (Theory-driven)
    // ================================================================

    [Theory]
    [MemberData(nameof(TestUserMatrix.ExpectedGrantCombinations), MemberType = typeof(TestUserMatrix))]
    public async Task User_HasGrant_ForExpectedTemplateGrant(string company, string template, string grantKey)
    {
        var user = GetTestUser(company, template);
        var grantType = Fixture.GrantTypeByKey[grantKey];

        var hasGrant = await Db.Grants
            .AnyAsync(g => g.UserId == user.Id && g.GrantTypeId == grantType.Id && g.CanOwn);

        hasGrant.Should().BeTrue(
            $"{template} at {company} should have grant '{grantKey}'");
    }

    // ================================================================
    // OWNER GRANTS
    // ================================================================

    [Fact]
    public async Task Owner_Has_AdminAccess()
    {
        var owner = GetTestUser("SystemAdmins", "Owner");
        var grantType = Fixture.GrantTypeByKey["AdminAccess"];

        var hasGrant = await Db.Grants
            .AnyAsync(g => g.UserId == owner.Id && g.GrantTypeId == grantType.Id && g.CanOwn);

        hasGrant.Should().BeTrue("Owner should have AdminAccess");
    }

    [Fact]
    public async Task Owner_Has_SystemConfiguration()
    {
        var owner = GetTestUser("SystemAdmins", "Owner");
        var grantType = Fixture.GrantTypeByKey["SystemConfiguration"];

        var hasGrant = await Db.Grants
            .AnyAsync(g => g.UserId == owner.Id && g.GrantTypeId == grantType.Id && g.CanOwn);

        hasGrant.Should().BeTrue("Owner should have SystemConfiguration");
    }

    // ================================================================
    // EMPLOYEE GRANTS
    // ================================================================

    [Theory]
    [InlineData("ViewShifts")]
    [InlineData("ViewChores")]
    [InlineData("ViewDuties")]
    [InlineData("ViewVacations")]
    [InlineData("RequestVacation")]
    [InlineData("RequestSwap")]
    public async Task Employee_Has_BasicViewAndRequestGrants(string grantKey)
    {
        var employee = GetTestUser("Tzafona", "Employee");
        var grantType = Fixture.GrantTypeByKey[grantKey];

        var hasGrant = await Db.Grants
            .AnyAsync(g => g.UserId == employee.Id && g.GrantTypeId == grantType.Id && g.CanOwn);

        hasGrant.Should().BeTrue($"Employee should have '{grantKey}'");
    }

    [Fact]
    public async Task Employee_DoesNotHave_AssignAlhutShifts()
    {
        var employee = GetTestUser("Tzafona", "Employee");
        var grantType = Fixture.GrantTypeByKey["AssignAlhutShifts"];

        var hasGrant = await Db.Grants
            .AnyAsync(g => g.UserId == employee.Id && g.GrantTypeId == grantType.Id && g.CanOwn);

        hasGrant.Should().BeFalse("Employee should NOT have AssignAlhutShifts");
    }

    // ================================================================
    // TRAINEE vs EMPLOYEE DIFFERENCE
    // ================================================================

    // Note: No Trainee user is seeded in the fixture. This test documents the expected
    // difference: Trainee has 19 grants (Employee's 20 minus RequestSwap). (F2: ViewGrants removed from both.)
    // The denial is tested indirectly through the DeniedGrantCombinations in AuthorizationDenialTests.

    // ================================================================
    // ROLE-SPECIFIC GRANT CHECKS
    // ================================================================

    [Fact]
    public async Task Lead_Has_AssignChores()
    {
        var lead = GetTestUser("Tzafona", "Lead");
        var grantType = Fixture.GrantTypeByKey["AssignChores"];

        var hasGrant = await Db.Grants
            .AnyAsync(g => g.UserId == lead.Id && g.GrantTypeId == grantType.Id && g.CanOwn);

        hasGrant.Should().BeTrue("Lead should have AssignChores");
    }

    [Fact]
    public async Task Lead_Has_ApproveVacations()
    {
        var lead = GetTestUser("Tzafona", "Lead");
        var grantType = Fixture.GrantTypeByKey["ApproveVacations"];

        var hasGrant = await Db.Grants
            .AnyAsync(g => g.UserId == lead.Id && g.GrantTypeId == grantType.Id && g.CanOwn);

        hasGrant.Should().BeTrue("Lead should have ApproveVacations");
    }

    [Fact]
    public async Task BRDirector_Has_AssignShifts()
    {
        // 2026-06-16: the 8 Assign*Shifts grants collapsed into the unified AssignShifts (137).
        var brDirector = GetTestUser("Tzafona", "BRDirector");
        var grantType = Fixture.GrantTypeByKey["AssignShifts"];

        var hasGrant = await Db.Grants
            .AnyAsync(g => g.UserId == brDirector.Id && g.GrantTypeId == grantType.Id && g.CanOwn);

        hasGrant.Should().BeTrue("BRDirector should have AssignShifts");
    }

    [Fact]
    public async Task AreaAdmin_Has_EditArea()
    {
        var areaAdmin = GetTestUser("Tzafona", "AreaAdmin");
        var grantType = Fixture.GrantTypeByKey["EditArea"];

        var hasGrant = await Db.Grants
            .AnyAsync(g => g.UserId == areaAdmin.Id && g.GrantTypeId == grantType.Id && g.CanOwn);

        hasGrant.Should().BeTrue("AreaAdmin should have EditArea");
    }

    [Fact]
    public async Task MoleculeAdmin_Has_BroadNonOwnerGrants()
    {
        var molAdmin = GetTestUser("Hitazmut", "MoleculeAdmin");

        // MoleculeAdmin should have grants that non-admin roles lack
        var editCompanyType = Fixture.GrantTypeByKey["EditCompany"];
        var manageShiftGroupingsType = Fixture.GrantTypeByKey["ManageShiftGroupings"];
        var assignGrantsType = Fixture.GrantTypeByKey["AssignGrants"];

        var hasEditCompany = await Db.Grants
            .AnyAsync(g => g.UserId == molAdmin.Id && g.GrantTypeId == editCompanyType.Id && g.CanOwn);
        var hasManageGroupings = await Db.Grants
            .AnyAsync(g => g.UserId == molAdmin.Id && g.GrantTypeId == manageShiftGroupingsType.Id && g.CanOwn);
        var hasAssignGrants = await Db.Grants
            .AnyAsync(g => g.UserId == molAdmin.Id && g.GrantTypeId == assignGrantsType.Id && g.CanOwn);

        hasEditCompany.Should().BeTrue("MoleculeAdmin should have EditCompany");
        hasManageGroupings.Should().BeTrue("MoleculeAdmin should have ManageShiftGroupings");
        hasAssignGrants.Should().BeTrue("MoleculeAdmin should have AssignGrants");
    }

    // ================================================================
    // GRANT COUNTS PER TEMPLATE
    // ================================================================

    [Theory]
    // Counts reflect the 2026-04-17 audit fills + 2026-05-03 Justice analytics additions +
    // 2026-05-23 ManageDistributionLists (#135) + 2026-06-09 ManageShiftCategories (#136), both
    // granted to Lead/BRDirector/Director/Assigner/DepartmentLead/MoleculeAdmin/AreaAdmin/Owner.
    // ViewJusticeTable (#133) granted to Lead/BRDirector/Director/Assigner/MoleculeAdmin/AreaAdmin/Owner;
    // EditJusticeTargets (#134) granted to AreaAdmin + Owner. Total grants: 136.
    // 2026-06-16: AssignShifts collapse (8 Assign*Shifts → 1 AssignShifts) reduces each affected
    // template by (K-1) where K = the number of Assign*Shifts grants it held.
    // 2026-07-21: ManageCalendarTabs (#138) granted to Lead/BRDirector/Director/MoleculeAdmin/Assigner/
    // DepartmentLead/AreaAdmin/Owner → +1 each (Employee unchanged).
    [InlineData("Tzafona", "Employee", 21)]      // unchanged (not a Lead-and-above template)
    [InlineData("Tzafona", "Lead", 58)]          // +1 ManageCalendarTabs (138)
    [InlineData("Tzafona", "BRDirector", 71)]    // +1 ManageCalendarTabs
    [InlineData("Tzafona", "Director", 66)]      // +1 ManageCalendarTabs
    [InlineData("Tzafona", "Assigner", 27)]      // +1 ManageCalendarTabs
    [InlineData("Hitazmut", "MoleculeAdmin", 102)]// +1 ManageCalendarTabs
    [InlineData("Yekev", "DepartmentLead", 57)]  // +1 ManageCalendarTabs
    [InlineData("Tzafona", "AreaAdmin", 119)]    // +1 ManageCalendarTabs
    [InlineData("SystemAdmins", "Owner", 130)]   // +1 ManageCalendarTabs
    public async Task User_Has_ExpectedAutoGrantCount(string company, string template, int expectedCount)
    {
        var user = GetTestUser(company, template);

        var actualCount = await Db.Grants
            .CountAsync(g => g.UserId == user.Id && g.IsAutoGrant);

        actualCount.Should().Be(expectedCount,
            $"{template} at {company} should have {expectedCount} auto-grants");
    }

    // ================================================================
    // ALL AUTO-GRANTS ARE MARKED CORRECTLY
    // ================================================================

    [Theory]
    [MemberData(nameof(TestUserMatrix.SeededUserCombinations), MemberType = typeof(TestUserMatrix))]
    public async Task AllGrants_AreMarkedAsAutoGrant(string company, string template)
    {
        var user = GetTestUser(company, template);

        var nonAutoGrants = await Db.Grants
            .CountAsync(g => g.UserId == user.Id && !g.IsAutoGrant);

        nonAutoGrants.Should().Be(0,
            $"all grants for {template} at {company} should be auto-grants (seeded from template)");
    }
}
