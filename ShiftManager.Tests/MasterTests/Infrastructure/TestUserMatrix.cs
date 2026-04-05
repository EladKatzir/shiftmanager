namespace ShiftManager.Tests.MasterTests.Infrastructure;

/// <summary>
/// Provides [MemberData] generators for parameterized tests across role templates,
/// companies, and grant combinations.
/// </summary>
public static class TestUserMatrix
{
    /// <summary>
    /// All 10 role template keys.
    /// </summary>
    public static readonly string[] AllTemplateKeys =
    {
        "Employee", "Trainee", "Assigner", "Lead", "BRDirector",
        "Director", "MoleculeAdmin", "DepartmentLead", "AreaAdmin", "Owner"
    };

    /// <summary>
    /// Representative companies from different molecules.
    /// </summary>
    public static readonly string[] RepresentativeCompanies =
    {
        "Tzafona",    // Oren (workforce)
        "Hitazmut",   // Ella (workforce)
        "Element",    // Harava (workforce)
        "Hamasa",     // Gefen (workforce)
        "Yekev"       // Shikma (tech)
    };

    /// <summary>
    /// Yields all 10 role template keys as [MemberData] rows.
    /// </summary>
    public static IEnumerable<object[]> AllRoleTemplates()
    {
        foreach (var key in AllTemplateKeys)
            yield return new object[] { key };
    }

    /// <summary>
    /// Yields representative companies as [MemberData] rows.
    /// </summary>
    public static IEnumerable<object[]> AllRepresentativeCompanies()
    {
        foreach (var name in RepresentativeCompanies)
            yield return new object[] { name };
    }

    /// <summary>
    /// Yields (company, template) combos that exist in the test fixture user set.
    /// </summary>
    public static IEnumerable<object[]> SeededUserCombinations()
    {
        yield return new object[] { "Tzafona", "Employee" };
        yield return new object[] { "Tzafona", "Lead" };
        yield return new object[] { "Tzafona", "BRDirector" };
        yield return new object[] { "Tzafona", "Director" };
        yield return new object[] { "Tzafona", "Assigner" };
        yield return new object[] { "Tzafona", "AreaAdmin" };
        yield return new object[] { "Hitazmut", "Employee" };
        yield return new object[] { "Hitazmut", "Lead" };
        yield return new object[] { "Hitazmut", "MoleculeAdmin" };
        yield return new object[] { "Element", "Employee" };
        yield return new object[] { "Element", "BRDirector" };
        yield return new object[] { "Hamasa", "Employee" };
        yield return new object[] { "Hamasa", "Lead" };
        yield return new object[] { "Yekev", "Employee" };
        yield return new object[] { "Yekev", "DepartmentLead" };
        yield return new object[] { "SystemAdmins", "Owner" };
    }

    /// <summary>
    /// Yields (templateKey, grantKey) pairs where the template should NOT have the grant.
    /// Derived by inverting the known grant assignments from RoleTemplateSeed.
    /// </summary>
    public static IEnumerable<object[]> DeniedGrantCombinations()
    {
        // Employee should NOT have assignment or admin grants
        yield return new object[] { "Tzafona", "Employee", "AssignAlhutShifts" };
        yield return new object[] { "Tzafona", "Employee", "AssignBRShifts" };
        yield return new object[] { "Tzafona", "Employee", "ApproveVacations" };
        yield return new object[] { "Tzafona", "Employee", "EditUsers" };
        yield return new object[] { "Tzafona", "Employee", "AdminAccess" };
        yield return new object[] { "Tzafona", "Employee", "SystemConfiguration" };
        yield return new object[] { "Tzafona", "Employee", "AssignChores" };
        yield return new object[] { "Tzafona", "Employee", "ManageOnDutyTypes" };

        // Trainee should NOT have RequestSwap (key difference from Employee)
        yield return new object[] { "Hitazmut", "Employee", "SystemConfiguration" };

        // Assigner should NOT have shift assignment grants
        yield return new object[] { "Tzafona", "Assigner", "AssignAlhutShifts" };
        yield return new object[] { "Tzafona", "Assigner", "AssignBRShifts" };
        yield return new object[] { "Tzafona", "Assigner", "ApproveVacations" };
        yield return new object[] { "Tzafona", "Assigner", "AdminAccess" };

        // BRDirector should NOT have SystemConfiguration
        yield return new object[] { "Tzafona", "BRDirector", "SystemConfiguration" };
        yield return new object[] { "Element", "BRDirector", "SystemConfiguration" };

        // DepartmentLead should NOT have area-level hierarchy edits
        yield return new object[] { "Yekev", "DepartmentLead", "EditArea" };
        yield return new object[] { "Yekev", "DepartmentLead", "SystemConfiguration" };
    }

    /// <summary>
    /// Yields (company, template, grantKey) where the grant SHOULD exist.
    /// Used for positive grant verification tests.
    /// </summary>
    public static IEnumerable<object[]> ExpectedGrantCombinations()
    {
        // Employee basic view grants
        yield return new object[] { "Tzafona", "Employee", "ViewShifts" };
        yield return new object[] { "Tzafona", "Employee", "ViewChores" };
        yield return new object[] { "Tzafona", "Employee", "ViewDuties" };
        yield return new object[] { "Tzafona", "Employee", "ViewVacations" };
        yield return new object[] { "Tzafona", "Employee", "RequestVacation" };
        yield return new object[] { "Tzafona", "Employee", "RequestSwap" };

        // Lead has assignment grants
        yield return new object[] { "Tzafona", "Lead", "AssignChores" };
        yield return new object[] { "Tzafona", "Lead", "ApproveVacations" };
        yield return new object[] { "Tzafona", "Lead", "ViewAllShifts" };

        // BRDirector has BR assignment
        yield return new object[] { "Tzafona", "BRDirector", "AssignBRShifts" };
        yield return new object[] { "Tzafona", "BRDirector", "ApproveSwaps" };

        // MoleculeAdmin has broad grants
        yield return new object[] { "Hitazmut", "MoleculeAdmin", "EditCompany" };
        yield return new object[] { "Hitazmut", "MoleculeAdmin", "ManageShiftGroupings" };
        yield return new object[] { "Hitazmut", "MoleculeAdmin", "AssignGrants" };

        // Owner has everything
        yield return new object[] { "SystemAdmins", "Owner", "AdminAccess" };
        yield return new object[] { "SystemAdmins", "Owner", "SystemConfiguration" };

        // AreaAdmin has area-wide grants
        yield return new object[] { "Tzafona", "AreaAdmin", "EditArea" };
        yield return new object[] { "Tzafona", "AreaAdmin", "ManageJobTypes" };
    }
}
