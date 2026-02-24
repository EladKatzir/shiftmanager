using ShiftManager.Models;
using ShiftManager.Models.Support;

namespace ShiftManager.Data.SeedData;

public static class RoleTemplateSeed
{
    public static List<RoleTemplate> GetRoleTemplates()
    {
        return new List<RoleTemplate>
        {
            new RoleTemplate
            {
                Id = 1,
                Key = "Employee",
                NameKey = "Role_Employee",
                DescriptionKey = "Role_Employee_Desc",
                DisplayNameEN = "Soldier",
                DisplayNameHE = "חייל",
                ScopeLevel = RoleScopeLevel.Implicit,
                IsSystem = true,
                SortOrder = 100,
                DerivedUserRole = UserRole.Employee,
                IsVisibleInSignup = true,
                CanBeAssignedByDefault = true
            },
            new RoleTemplate
            {
                Id = 2,
                Key = "BRDirector",
                NameKey = "Role_BRDirector",
                DescriptionKey = "Role_BRDirector_Desc",
                DisplayNameEN = "Kabar",
                DisplayNameHE = "קב\"ר",
                ScopeLevel = RoleScopeLevel.Company,
                IsSystem = true,
                SortOrder = 10,
                DerivedUserRole = UserRole.Manager,
                IsVisibleInSignup = true,
                CanBeAssignedByDefault = true
            },
            new RoleTemplate
            {
                Id = 3,
                Key = "AlhutLead",
                NameKey = "Role_AlhutLead",
                DescriptionKey = "Role_AlhutLead_Desc",
                DisplayNameEN = "Alhut SL",
                DisplayNameHE = "מפ\"צ אלחוט",
                ScopeLevel = RoleScopeLevel.CompanyJobType,
                IsSystem = true,
                SortOrder = 20,
                DerivedUserRole = UserRole.Manager,
                IsVisibleInSignup = true,
                CanBeAssignedByDefault = true
            },
            new RoleTemplate
            {
                Id = 4,
                Key = "TextLead",
                NameKey = "Role_TextLead",
                DescriptionKey = "Role_TextLead_Desc",
                DisplayNameEN = "Text SL",
                DisplayNameHE = "מפ\"צ טקסט",
                ScopeLevel = RoleScopeLevel.CompanyJobType,
                IsSystem = true,
                SortOrder = 21,
                DerivedUserRole = UserRole.Manager,
                IsVisibleInSignup = true,
                CanBeAssignedByDefault = true
            },
            new RoleTemplate
            {
                Id = 5,
                Key = "AlhutDirector",
                NameKey = "Role_AlhutDirector",
                DescriptionKey = "Role_AlhutDirector_Desc",
                DisplayNameEN = "Alhut PL",
                DisplayNameHE = "מ\"מ אלחוט",
                ScopeLevel = RoleScopeLevel.MoleculeJobType,
                IsSystem = true,
                SortOrder = 5,
                DerivedUserRole = UserRole.Director,
                IsVisibleInSignup = true,
                CanBeAssignedByDefault = true
            },
            new RoleTemplate
            {
                Id = 6,
                Key = "TextDirector",
                NameKey = "Role_TextDirector",
                DescriptionKey = "Role_TextDirector_Desc",
                DisplayNameEN = "Text PL",
                DisplayNameHE = "מ\"מ טקסט",
                ScopeLevel = RoleScopeLevel.MoleculeJobType,
                IsSystem = true,
                SortOrder = 6,
                DerivedUserRole = UserRole.Director,
                IsVisibleInSignup = true,
                CanBeAssignedByDefault = true
            },
            new RoleTemplate
            {
                Id = 7,
                Key = "MoleculeAdmin",
                NameKey = "Role_MoleculeAdmin",
                DescriptionKey = "Role_MoleculeAdmin_Desc",
                DisplayNameEN = "CO",
                DisplayNameHE = "מפק\"מ",
                ScopeLevel = RoleScopeLevel.Molecule,
                IsSystem = true,
                SortOrder = 3,
                DerivedUserRole = UserRole.Manager,
                IsVisibleInSignup = true,
                CanBeAssignedByDefault = true
            },
            new RoleTemplate
            {
                Id = 8,
                Key = "Assigner",
                NameKey = "Role_Assigner",
                DescriptionKey = "Role_Assigner_Desc",
                DisplayNameEN = "Assigner",
                DisplayNameHE = "משבץ",
                ScopeLevel = RoleScopeLevel.Molecule,
                IsSystem = true,
                SortOrder = 30,
                DerivedUserRole = UserRole.Assigner,
                IsVisibleInSignup = false,
                CanBeAssignedByDefault = true
            },
            new RoleTemplate
            {
                Id = 9,
                Key = "DepartmentLead",
                NameKey = "Role_DepartmentLead",
                DescriptionKey = "Role_DepartmentLead_Desc",
                DisplayNameEN = "Department Lead",
                DisplayNameHE = "מפקד מחלקה טכנית",
                ScopeLevel = RoleScopeLevel.Department,
                IsSystem = true,
                SortOrder = 25,
                DerivedUserRole = UserRole.Manager,
                IsVisibleInSignup = true,
                CanBeAssignedByDefault = true
            },
            new RoleTemplate
            {
                Id = 10,
                Key = "AreaAdmin",
                NameKey = "Role_AreaAdmin",
                DescriptionKey = "Role_AreaAdmin_Desc",
                DisplayNameEN = "BC",
                DisplayNameHE = "קב\"ב",
                ScopeLevel = RoleScopeLevel.Area,
                IsSystem = true,
                SortOrder = 2,
                DerivedUserRole = UserRole.AreaAdmin,
                IsVisibleInSignup = true,
                CanBeAssignedByDefault = true
            },
            new RoleTemplate
            {
                Id = 11,
                Key = "Owner",
                NameKey = "Role_Owner",
                DescriptionKey = "Role_Owner_Desc",
                DisplayNameEN = "Owner",
                DisplayNameHE = "בעלים",
                ScopeLevel = RoleScopeLevel.Project,
                IsSystem = true,
                SortOrder = 1,
                DerivedUserRole = UserRole.Owner,
                IsVisibleInSignup = false,
                CanBeAssignedByDefault = true
            },
            new RoleTemplate
            {
                Id = 12,
                Key = "Trainee",
                NameKey = "Role_Trainee",
                DescriptionKey = "Role_Trainee_Desc",
                DisplayNameEN = "Trainee",
                DisplayNameHE = "חניך",
                ScopeLevel = RoleScopeLevel.Implicit,
                IsSystem = true,
                SortOrder = 101,
                DerivedUserRole = UserRole.Trainee,
                IsVisibleInSignup = false,
                CanBeAssignedByDefault = true
            }
        };
    }

    public static List<RoleTemplateGrant> GetRoleTemplateGrants()
    {
        var grants = new List<RoleTemplateGrant>();
        int id = 1;

        // Helper for concise grant creation
        RoleTemplateGrant G(int templateId, int grantTypeId, GrantScopeMode scope,
            bool canGive = false, int? targetJobTypeId = null, bool useOwnJobType = false)
            => new RoleTemplateGrant
            {
                Id = id++,
                RoleTemplateId = templateId,
                GrantTypeId = grantTypeId,
                CanOwn = true,
                CanGive = canGive,
                ScopeMode = scope,
                TargetJobTypeId = targetJobTypeId,
                UseOwnJobType = useOwnJobType
            };

        // Shorthand constants
        const GrantScopeMode SAR = GrantScopeMode.SameAsRole;
        const GrantScopeMode ETM = GrantScopeMode.ExpandToMolecule;
        const GrantScopeMode ETA = GrantScopeMode.ExpandToArea;
        const GrantScopeMode ETP = GrantScopeMode.ExpandToProject;

        // ============================================
        // EMPLOYEE (Template 1) — 19 grants
        // All SAR (company-scoped). Base for all workforce roles.
        // ============================================
        grants.Add(G(1, 1, SAR));    // ViewShifts
        grants.Add(G(1, 16, SAR));   // ViewChores
        grants.Add(G(1, 12, SAR));   // ViewDuties
        grants.Add(G(1, 20, SAR));   // ViewVacations
        grants.Add(G(1, 21, SAR));   // RequestVacation (NOT 22 — old seed was off-by-one!)
        grants.Add(G(1, 25, SAR));   // RequestSwap (NOT 26 — old seed was off-by-one!)
        grants.Add(G(1, 115, SAR));  // ViewCompanyCalendar
        grants.Add(G(1, 117, SAR));  // ViewCompanyUsers
        grants.Add(G(1, 35, SAR));   // ViewGrants
        grants.Add(G(1, 39, SAR));   // ViewHierarchy
        grants.Add(G(1, 110, SAR));  // WriteOverviewNotes
        grants.Add(G(1, 61, SAR));   // ViewAlhutShiftCalendar
        grants.Add(G(1, 62, SAR));   // ViewTextShiftCalendar
        grants.Add(G(1, 63, SAR));   // ViewBRShiftCalendar
        grants.Add(G(1, 64, SAR));   // ViewHakamShiftCalendar
        grants.Add(G(1, 65, SAR, useOwnJobType: true));   // CanBeAssignedAlhutShifts (OWN)
        grants.Add(G(1, 66, SAR, useOwnJobType: true));   // CanBeAssignedTextShifts (OWN)
        grants.Add(G(1, 67, SAR, useOwnJobType: true));   // CanBeAssignedBRShifts (OWN)
        grants.Add(G(1, 68, SAR, useOwnJobType: true));   // CanBeAssignedHakamShifts (OWN)

        // ============================================
        // TRAINEE (Template 12) — 18 grants
        // Same as Employee minus RequestSwap
        // ============================================
        grants.Add(G(12, 1, SAR));    // ViewShifts
        grants.Add(G(12, 16, SAR));   // ViewChores
        grants.Add(G(12, 12, SAR));   // ViewDuties
        grants.Add(G(12, 20, SAR));   // ViewVacations
        grants.Add(G(12, 21, SAR));   // RequestVacation (FIXED: was 22)
        // NO RequestSwap for Trainee
        grants.Add(G(12, 115, SAR));  // ViewCompanyCalendar
        grants.Add(G(12, 117, SAR));  // ViewCompanyUsers
        grants.Add(G(12, 35, SAR));   // ViewGrants
        grants.Add(G(12, 39, SAR));   // ViewHierarchy
        grants.Add(G(12, 110, SAR));  // WriteOverviewNotes
        grants.Add(G(12, 61, SAR));   // ViewAlhutShiftCalendar
        grants.Add(G(12, 62, SAR));   // ViewTextShiftCalendar
        grants.Add(G(12, 63, SAR));   // ViewBRShiftCalendar
        grants.Add(G(12, 64, SAR));   // ViewHakamShiftCalendar
        grants.Add(G(12, 65, SAR, useOwnJobType: true));   // CanBeAssignedAlhutShifts (OWN)
        grants.Add(G(12, 66, SAR, useOwnJobType: true));   // CanBeAssignedTextShifts (OWN)
        grants.Add(G(12, 67, SAR, useOwnJobType: true));   // CanBeAssignedBRShifts (OWN)
        grants.Add(G(12, 68, SAR, useOwnJobType: true));   // CanBeAssignedHakamShifts (OWN)

        // ============================================
        // ASSIGNER (Template 8) — 20 grants
        // All Employee grants at SAR + AssignChores at ETM
        // ============================================
        grants.Add(G(8, 1, SAR));    // ViewShifts
        grants.Add(G(8, 16, SAR));   // ViewChores
        grants.Add(G(8, 12, SAR));   // ViewDuties
        grants.Add(G(8, 20, SAR));   // ViewVacations
        grants.Add(G(8, 21, SAR));   // RequestVacation
        grants.Add(G(8, 25, SAR));   // RequestSwap
        grants.Add(G(8, 115, SAR));  // ViewCompanyCalendar
        grants.Add(G(8, 117, SAR));  // ViewCompanyUsers
        grants.Add(G(8, 35, SAR));   // ViewGrants
        grants.Add(G(8, 39, SAR));   // ViewHierarchy
        grants.Add(G(8, 110, SAR));  // WriteOverviewNotes
        grants.Add(G(8, 61, SAR));   // ViewAlhutShiftCalendar
        grants.Add(G(8, 62, SAR));   // ViewTextShiftCalendar
        grants.Add(G(8, 63, SAR));   // ViewBRShiftCalendar
        grants.Add(G(8, 64, SAR));   // ViewHakamShiftCalendar
        grants.Add(G(8, 65, SAR, useOwnJobType: true));
        grants.Add(G(8, 66, SAR, useOwnJobType: true));
        grants.Add(G(8, 67, SAR, useOwnJobType: true));
        grants.Add(G(8, 68, SAR, useOwnJobType: true));
        // Assigner extra: molecule-wide chore assignment
        grants.Add(G(8, 17, ETM));   // AssignChores (ETM — only this grant is molecule-scoped)

        // ============================================
        // ALHUT LEAD (Template 3) — 38 grants
        // Employee base (SAR) + lead-specific grants. AssignAlhutShifts at ETM.
        // ============================================
        grants.Add(G(3, 1, SAR));    // ViewShifts
        grants.Add(G(3, 16, SAR));   // ViewChores
        grants.Add(G(3, 12, SAR));   // ViewDuties
        grants.Add(G(3, 20, SAR));   // ViewVacations
        grants.Add(G(3, 21, SAR));   // RequestVacation
        grants.Add(G(3, 25, SAR));   // RequestSwap
        grants.Add(G(3, 115, SAR));  // ViewCompanyCalendar
        grants.Add(G(3, 117, SAR));  // ViewCompanyUsers
        grants.Add(G(3, 35, SAR));   // ViewGrants
        grants.Add(G(3, 39, SAR));   // ViewHierarchy
        grants.Add(G(3, 110, SAR));  // WriteOverviewNotes
        grants.Add(G(3, 61, SAR));   // ViewAlhutShiftCalendar
        grants.Add(G(3, 62, SAR));   // ViewTextShiftCalendar
        grants.Add(G(3, 63, SAR));   // ViewBRShiftCalendar
        grants.Add(G(3, 64, SAR));   // ViewHakamShiftCalendar
        grants.Add(G(3, 65, SAR, useOwnJobType: true));
        grants.Add(G(3, 66, SAR, useOwnJobType: true));
        grants.Add(G(3, 67, SAR, useOwnJobType: true));
        grants.Add(G(3, 68, SAR, useOwnJobType: true));
        // Lead-specific grants
        grants.Add(G(3, 3, ETM, useOwnJobType: true));    // AssignAlhutShifts (ETM, OWN)
        grants.Add(G(3, 2, SAR));    // ViewAllShifts
        grants.Add(G(3, 7, SAR, useOwnJobType: true));    // EditShiftPrograms (OWN)
        grants.Add(G(3, 8, SAR, useOwnJobType: true));    // CreateShiftPrograms (OWN)
        grants.Add(G(3, 9, SAR, useOwnJobType: true));    // DeleteShiftPrograms (OWN)
        grants.Add(G(3, 10, SAR, useOwnJobType: true));   // EditShiftTypes (OWN)
        grants.Add(G(3, 11, SAR, useOwnJobType: true));   // CreateShiftTypes (OWN)
        grants.Add(G(3, 69, SAR, useOwnJobType: true));   // ManageAlhutBlueprints (OWN)
        grants.Add(G(3, 70, SAR, useOwnJobType: true));   // ManageAlhutPrograms (OWN)
        grants.Add(G(3, 109, SAR, useOwnJobType: true));  // ManageShiftCapacity (OWN)
        grants.Add(G(3, 17, SAR));   // AssignChores
        grants.Add(G(3, 22, SAR, useOwnJobType: true));   // ApproveVacations (OWN)
        grants.Add(G(3, 26, SAR, useOwnJobType: true));   // ApproveSwaps (OWN)
        grants.Add(G(3, 28, SAR));   // ViewUsers
        grants.Add(G(3, 36, SAR, useOwnJobType: true));   // AssignGrants (OWN)
        grants.Add(G(3, 37, SAR, useOwnJobType: true));   // RevokeGrants (OWN)
        grants.Add(G(3, 112, SAR));  // AccessAdminNavigation
        grants.Add(G(3, 114, SAR));  // ManagerHomeAccess
        grants.Add(G(3, 120, SAR));  // ViewSystemAlerts

        // ============================================
        // TEXT LEAD (Template 4) — 38 grants
        // Same as AlhutLead but Text-specific
        // ============================================
        grants.Add(G(4, 1, SAR));
        grants.Add(G(4, 16, SAR));
        grants.Add(G(4, 12, SAR));
        grants.Add(G(4, 20, SAR));
        grants.Add(G(4, 21, SAR));
        grants.Add(G(4, 25, SAR));
        grants.Add(G(4, 115, SAR));
        grants.Add(G(4, 117, SAR));
        grants.Add(G(4, 35, SAR));
        grants.Add(G(4, 39, SAR));
        grants.Add(G(4, 110, SAR));
        grants.Add(G(4, 61, SAR));
        grants.Add(G(4, 62, SAR));
        grants.Add(G(4, 63, SAR));
        grants.Add(G(4, 64, SAR));
        grants.Add(G(4, 65, SAR, useOwnJobType: true));
        grants.Add(G(4, 66, SAR, useOwnJobType: true));
        grants.Add(G(4, 67, SAR, useOwnJobType: true));
        grants.Add(G(4, 68, SAR, useOwnJobType: true));
        // Lead-specific (Text variant)
        grants.Add(G(4, 4, ETM, useOwnJobType: true));    // AssignTextShifts (ETM, OWN)
        grants.Add(G(4, 2, SAR));
        grants.Add(G(4, 7, SAR, useOwnJobType: true));
        grants.Add(G(4, 8, SAR, useOwnJobType: true));
        grants.Add(G(4, 9, SAR, useOwnJobType: true));
        grants.Add(G(4, 10, SAR, useOwnJobType: true));
        grants.Add(G(4, 11, SAR, useOwnJobType: true));
        grants.Add(G(4, 71, SAR, useOwnJobType: true));   // ManageTextBlueprints (OWN)
        grants.Add(G(4, 72, SAR, useOwnJobType: true));   // ManageTextPrograms (OWN)
        grants.Add(G(4, 109, SAR, useOwnJobType: true));
        grants.Add(G(4, 17, SAR));
        grants.Add(G(4, 22, SAR, useOwnJobType: true));
        grants.Add(G(4, 26, SAR, useOwnJobType: true));
        grants.Add(G(4, 28, SAR));
        grants.Add(G(4, 36, SAR, useOwnJobType: true));
        grants.Add(G(4, 37, SAR, useOwnJobType: true));
        grants.Add(G(4, 112, SAR));
        grants.Add(G(4, 114, SAR));
        grants.Add(G(4, 120, SAR));

        // ============================================
        // BR DIRECTOR (Template 2) — 42 grants
        // Employee base (SAR) + BR management. AssignBRShifts + ViewAllShifts at ETM.
        // ============================================
        grants.Add(G(2, 1, SAR));
        grants.Add(G(2, 16, SAR));
        grants.Add(G(2, 12, SAR));
        grants.Add(G(2, 20, SAR));
        grants.Add(G(2, 21, SAR));
        grants.Add(G(2, 25, SAR));
        grants.Add(G(2, 115, SAR));
        grants.Add(G(2, 117, SAR));
        grants.Add(G(2, 35, SAR));
        grants.Add(G(2, 39, SAR));
        grants.Add(G(2, 110, SAR));
        grants.Add(G(2, 61, SAR));
        grants.Add(G(2, 62, SAR));
        grants.Add(G(2, 63, SAR));
        grants.Add(G(2, 64, SAR));
        grants.Add(G(2, 65, SAR, useOwnJobType: true));
        grants.Add(G(2, 66, SAR, useOwnJobType: true));
        grants.Add(G(2, 67, SAR, useOwnJobType: true));
        grants.Add(G(2, 68, SAR, useOwnJobType: true));
        // BRDirector-specific
        grants.Add(G(2, 5, ETM, canGive: true));   // AssignBRShifts (ETM, ALL, CanGive)
        grants.Add(G(2, 2, ETM));                   // ViewAllShifts (ETM)
        grants.Add(G(2, 7, SAR, useOwnJobType: true));
        grants.Add(G(2, 8, SAR, useOwnJobType: true));
        grants.Add(G(2, 9, SAR, useOwnJobType: true));
        grants.Add(G(2, 10, SAR, useOwnJobType: true));
        grants.Add(G(2, 11, SAR, useOwnJobType: true));
        grants.Add(G(2, 73, SAR));   // ManageBRBlueprints (ALL)
        grants.Add(G(2, 74, SAR));   // ManageBRPrograms (ALL)
        grants.Add(G(2, 75, SAR));   // ManageHakamBlueprints (ALL — BR manages Hakam)
        grants.Add(G(2, 76, SAR));   // ManageHakamPrograms (ALL)
        grants.Add(G(2, 109, SAR, useOwnJobType: true));
        grants.Add(G(2, 17, SAR));   // AssignChores
        grants.Add(G(2, 22, SAR));   // ApproveVacations (ALL)
        grants.Add(G(2, 23, SAR));   // OverrideVacationLimits
        grants.Add(G(2, 26, SAR));   // ApproveSwaps (ALL)
        grants.Add(G(2, 27, SAR));   // InitiateSwap
        grants.Add(G(2, 28, SAR));   // ViewUsers
        grants.Add(G(2, 29, SAR));   // EditUsers
        grants.Add(G(2, 116, SAR));  // ManageJoinRequests
        grants.Add(G(2, 118, SAR));  // EditCompanyUsers
        grants.Add(G(2, 112, SAR));  // AccessAdminNavigation
        grants.Add(G(2, 114, SAR));  // ManagerHomeAccess
        grants.Add(G(2, 120, SAR));  // ViewSystemAlerts

        // ============================================
        // ALHUT DIRECTOR (Template 5) — 45 grants
        // All AlhutLead grants widened to ETM + director extras. Self-scoped stay SAR.
        // ============================================
        // Self-scoped (stay SAR)
        grants.Add(G(5, 21, SAR));
        grants.Add(G(5, 25, SAR));
        grants.Add(G(5, 65, SAR, useOwnJobType: true));
        grants.Add(G(5, 66, SAR, useOwnJobType: true));
        grants.Add(G(5, 67, SAR, useOwnJobType: true));
        grants.Add(G(5, 68, SAR, useOwnJobType: true));
        // AlhutLead grants widened to ETM
        grants.Add(G(5, 1, ETM));
        grants.Add(G(5, 16, ETM));
        grants.Add(G(5, 12, ETM));
        grants.Add(G(5, 20, ETM));
        grants.Add(G(5, 115, ETM));
        grants.Add(G(5, 117, ETM));
        grants.Add(G(5, 35, ETM));
        grants.Add(G(5, 39, ETM));
        grants.Add(G(5, 110, ETM));
        grants.Add(G(5, 61, ETM));
        grants.Add(G(5, 62, ETM));
        grants.Add(G(5, 63, ETM));
        grants.Add(G(5, 64, ETM));
        grants.Add(G(5, 3, ETM, canGive: true, useOwnJobType: true));  // AssignAlhutShifts (ETM, OWN, CanGive)
        grants.Add(G(5, 2, ETM));
        grants.Add(G(5, 7, ETM, useOwnJobType: true));
        grants.Add(G(5, 8, ETM, useOwnJobType: true));
        grants.Add(G(5, 9, ETM, useOwnJobType: true));
        grants.Add(G(5, 10, ETM, useOwnJobType: true));
        grants.Add(G(5, 11, ETM, useOwnJobType: true));
        grants.Add(G(5, 69, ETM, useOwnJobType: true));
        grants.Add(G(5, 70, ETM, useOwnJobType: true));
        grants.Add(G(5, 109, ETM, useOwnJobType: true));
        grants.Add(G(5, 17, ETM));
        grants.Add(G(5, 22, ETM, useOwnJobType: true));
        grants.Add(G(5, 26, ETM, useOwnJobType: true));
        grants.Add(G(5, 28, ETM));
        grants.Add(G(5, 36, ETM, useOwnJobType: true));
        grants.Add(G(5, 37, ETM, useOwnJobType: true));
        grants.Add(G(5, 112, ETM));
        grants.Add(G(5, 114, ETM));
        grants.Add(G(5, 120, ETM));
        // Director extras
        grants.Add(G(5, 34, ETM));   // ViewAllUsers
        grants.Add(G(5, 38, ETM));   // AssignRoles
        grants.Add(G(5, 113, ETM));  // DirectorHubAccess
        grants.Add(G(5, 116, ETM));  // ManageJoinRequests
        grants.Add(G(5, 118, ETM));  // EditCompanyUsers
        grants.Add(G(5, 119, ETM));  // ManageAnnouncements
        grants.Add(G(5, 121, ETM));  // ViewAllAreas

        // ============================================
        // TEXT DIRECTOR (Template 6) — 45 grants
        // Same as AlhutDirector but Text-specific
        // ============================================
        grants.Add(G(6, 21, SAR));
        grants.Add(G(6, 25, SAR));
        grants.Add(G(6, 65, SAR, useOwnJobType: true));
        grants.Add(G(6, 66, SAR, useOwnJobType: true));
        grants.Add(G(6, 67, SAR, useOwnJobType: true));
        grants.Add(G(6, 68, SAR, useOwnJobType: true));
        grants.Add(G(6, 1, ETM));
        grants.Add(G(6, 16, ETM));
        grants.Add(G(6, 12, ETM));
        grants.Add(G(6, 20, ETM));
        grants.Add(G(6, 115, ETM));
        grants.Add(G(6, 117, ETM));
        grants.Add(G(6, 35, ETM));
        grants.Add(G(6, 39, ETM));
        grants.Add(G(6, 110, ETM));
        grants.Add(G(6, 61, ETM));
        grants.Add(G(6, 62, ETM));
        grants.Add(G(6, 63, ETM));
        grants.Add(G(6, 64, ETM));
        grants.Add(G(6, 4, ETM, canGive: true, useOwnJobType: true));  // AssignTextShifts
        grants.Add(G(6, 2, ETM));
        grants.Add(G(6, 7, ETM, useOwnJobType: true));
        grants.Add(G(6, 8, ETM, useOwnJobType: true));
        grants.Add(G(6, 9, ETM, useOwnJobType: true));
        grants.Add(G(6, 10, ETM, useOwnJobType: true));
        grants.Add(G(6, 11, ETM, useOwnJobType: true));
        grants.Add(G(6, 71, ETM, useOwnJobType: true));   // ManageTextBlueprints
        grants.Add(G(6, 72, ETM, useOwnJobType: true));   // ManageTextPrograms
        grants.Add(G(6, 109, ETM, useOwnJobType: true));
        grants.Add(G(6, 17, ETM));
        grants.Add(G(6, 22, ETM, useOwnJobType: true));
        grants.Add(G(6, 26, ETM, useOwnJobType: true));
        grants.Add(G(6, 28, ETM));
        grants.Add(G(6, 36, ETM, useOwnJobType: true));
        grants.Add(G(6, 37, ETM, useOwnJobType: true));
        grants.Add(G(6, 112, ETM));
        grants.Add(G(6, 114, ETM));
        grants.Add(G(6, 120, ETM));
        grants.Add(G(6, 34, ETM));
        grants.Add(G(6, 38, ETM));
        grants.Add(G(6, 113, ETM));
        grants.Add(G(6, 116, ETM));
        grants.Add(G(6, 118, ETM));
        grants.Add(G(6, 119, ETM));
        grants.Add(G(6, 121, ETM));

        // ============================================
        // MOLECULE ADMIN (Template 7) — 56 grants
        // All BRDirector grants widened to ETM + admin extras.
        // ApproveVacations ALL (supersedes BR+HAKAM). Added AssignAlhut+Text.
        // ============================================
        grants.Add(G(7, 21, SAR));
        grants.Add(G(7, 25, SAR));
        grants.Add(G(7, 65, SAR, useOwnJobType: true));
        grants.Add(G(7, 66, SAR, useOwnJobType: true));
        grants.Add(G(7, 67, SAR, useOwnJobType: true));
        grants.Add(G(7, 68, SAR, useOwnJobType: true));
        grants.Add(G(7, 1, ETM));
        grants.Add(G(7, 16, ETM));
        grants.Add(G(7, 12, ETM));
        grants.Add(G(7, 20, ETM));
        grants.Add(G(7, 115, ETM));
        grants.Add(G(7, 117, ETM));
        grants.Add(G(7, 35, ETM));
        grants.Add(G(7, 39, ETM));
        grants.Add(G(7, 110, ETM));
        grants.Add(G(7, 61, ETM));
        grants.Add(G(7, 62, ETM));
        grants.Add(G(7, 63, ETM));
        grants.Add(G(7, 64, ETM));
        grants.Add(G(7, 5, ETM, canGive: true));   // AssignBRShifts
        grants.Add(G(7, 2, ETM));                   // ViewAllShifts
        grants.Add(G(7, 7, ETM));                   // EditShiftPrograms (ALL)
        grants.Add(G(7, 8, ETM));
        grants.Add(G(7, 9, ETM));
        grants.Add(G(7, 10, ETM));
        grants.Add(G(7, 11, ETM));
        grants.Add(G(7, 73, ETM));
        grants.Add(G(7, 74, ETM));
        grants.Add(G(7, 75, ETM));
        grants.Add(G(7, 76, ETM));
        grants.Add(G(7, 109, ETM));
        grants.Add(G(7, 17, ETM));
        grants.Add(G(7, 22, ETM));   // ApproveVacations — ALL (supersedes BR+HAKAM)
        grants.Add(G(7, 23, ETM));
        grants.Add(G(7, 26, ETM));
        grants.Add(G(7, 27, ETM));
        grants.Add(G(7, 28, ETM));
        grants.Add(G(7, 29, ETM));
        grants.Add(G(7, 116, ETM));
        grants.Add(G(7, 118, ETM));
        grants.Add(G(7, 112, ETM));
        grants.Add(G(7, 114, ETM));
        grants.Add(G(7, 120, ETM));
        // MoleculeAdmin extras
        grants.Add(G(7, 3, ETM, canGive: true));    // AssignAlhutShifts (ALL, CanGive)
        grants.Add(G(7, 4, ETM, canGive: true));    // AssignTextShifts (ALL, CanGive)
        grants.Add(G(7, 18, ETM));
        grants.Add(G(7, 19, ETM));
        grants.Add(G(7, 30, ETM));
        grants.Add(G(7, 31, ETM));
        grants.Add(G(7, 34, ETM));
        grants.Add(G(7, 36, ETM, canGive: true));
        grants.Add(G(7, 37, ETM));
        grants.Add(G(7, 38, ETM, canGive: true));
        grants.Add(G(7, 41, ETM));
        grants.Add(G(7, 45, ETM));
        grants.Add(G(7, 48, ETM));
        grants.Add(G(7, 49, ETM));
        grants.Add(G(7, 50, ETM));
        grants.Add(G(7, 52, ETM));
        grants.Add(G(7, 53, ETM));
        grants.Add(G(7, 59, ETM));
        grants.Add(G(7, 119, ETM));
        grants.Add(G(7, 69, ETM));
        grants.Add(G(7, 70, ETM));
        grants.Add(G(7, 71, ETM));
        grants.Add(G(7, 72, ETM));

        // ============================================
        // DEPARTMENT LEAD (Template 9) — 25 grants
        // Employee base (SAR) + department management grants
        // ============================================
        grants.Add(G(9, 1, SAR));
        grants.Add(G(9, 16, SAR));
        grants.Add(G(9, 12, SAR));
        grants.Add(G(9, 20, SAR));
        grants.Add(G(9, 21, SAR));
        grants.Add(G(9, 25, SAR));
        grants.Add(G(9, 115, SAR));
        grants.Add(G(9, 117, SAR));
        grants.Add(G(9, 35, SAR));
        grants.Add(G(9, 39, SAR));
        grants.Add(G(9, 110, SAR));
        grants.Add(G(9, 61, SAR));
        grants.Add(G(9, 62, SAR));
        grants.Add(G(9, 63, SAR));
        grants.Add(G(9, 64, SAR));
        grants.Add(G(9, 65, SAR, useOwnJobType: true));
        grants.Add(G(9, 66, SAR, useOwnJobType: true));
        grants.Add(G(9, 67, SAR, useOwnJobType: true));
        grants.Add(G(9, 68, SAR, useOwnJobType: true));
        // Department extras
        grants.Add(G(9, 28, SAR));
        grants.Add(G(9, 29, SAR));
        grants.Add(G(9, 33, SAR));
        grants.Add(G(9, 47, SAR));
        grants.Add(G(9, 52, SAR));
        grants.Add(G(9, 53, SAR));

        // ============================================
        // AREA ADMIN (Template 10) — 70+ grants
        // Merges Directors + MoleculeAdmin at ETA. ALL jobtype wins.
        // ============================================
        grants.Add(G(10, 21, SAR));
        grants.Add(G(10, 25, SAR));
        grants.Add(G(10, 65, SAR, useOwnJobType: true));
        grants.Add(G(10, 66, SAR, useOwnJobType: true));
        grants.Add(G(10, 67, SAR, useOwnJobType: true));
        grants.Add(G(10, 68, SAR, useOwnJobType: true));
        grants.Add(G(10, 1, ETA));
        grants.Add(G(10, 16, ETA));
        grants.Add(G(10, 12, ETA));
        grants.Add(G(10, 20, ETA));
        grants.Add(G(10, 115, ETA));
        grants.Add(G(10, 117, ETA));
        grants.Add(G(10, 35, ETA));
        grants.Add(G(10, 39, ETA));
        grants.Add(G(10, 110, ETA));
        grants.Add(G(10, 61, ETA));
        grants.Add(G(10, 62, ETA));
        grants.Add(G(10, 63, ETA));
        grants.Add(G(10, 64, ETA));
        grants.Add(G(10, 3, ETA, canGive: true));
        grants.Add(G(10, 4, ETA, canGive: true));
        grants.Add(G(10, 5, ETA, canGive: true));
        grants.Add(G(10, 2, ETA));
        grants.Add(G(10, 7, ETA));
        grants.Add(G(10, 8, ETA));
        grants.Add(G(10, 9, ETA));
        grants.Add(G(10, 10, ETA));
        grants.Add(G(10, 11, ETA));
        grants.Add(G(10, 69, ETA));
        grants.Add(G(10, 70, ETA));
        grants.Add(G(10, 71, ETA));
        grants.Add(G(10, 72, ETA));
        grants.Add(G(10, 73, ETA));
        grants.Add(G(10, 74, ETA));
        grants.Add(G(10, 75, ETA));
        grants.Add(G(10, 76, ETA));
        grants.Add(G(10, 109, ETA));
        grants.Add(G(10, 17, ETA));
        grants.Add(G(10, 18, ETA));
        grants.Add(G(10, 19, ETA));
        grants.Add(G(10, 22, ETA));
        grants.Add(G(10, 23, ETA));
        grants.Add(G(10, 26, ETA));
        grants.Add(G(10, 27, ETA));
        grants.Add(G(10, 28, ETA));
        grants.Add(G(10, 29, ETA));
        grants.Add(G(10, 30, ETA));
        grants.Add(G(10, 31, ETA));
        grants.Add(G(10, 34, ETA));
        grants.Add(G(10, 36, ETA, canGive: true));
        grants.Add(G(10, 37, ETA));
        grants.Add(G(10, 38, ETA, canGive: true));
        grants.Add(G(10, 41, ETA));
        grants.Add(G(10, 45, ETA));
        grants.Add(G(10, 48, ETA));
        grants.Add(G(10, 49, ETA));
        grants.Add(G(10, 50, ETA));
        grants.Add(G(10, 52, ETA));
        grants.Add(G(10, 53, ETA));
        grants.Add(G(10, 59, ETA));
        grants.Add(G(10, 116, ETA));
        grants.Add(G(10, 118, ETA));
        grants.Add(G(10, 112, ETA));
        grants.Add(G(10, 113, ETA));
        grants.Add(G(10, 114, ETA));
        grants.Add(G(10, 119, ETA));
        grants.Add(G(10, 120, ETA));
        grants.Add(G(10, 121, ETA));
        // AreaAdmin extras
        grants.Add(G(10, 13, ETA, canGive: true));
        grants.Add(G(10, 14, ETA, canGive: true));
        grants.Add(G(10, 15, ETA));
        grants.Add(G(10, 42, ETA));
        grants.Add(G(10, 43, ETA));
        grants.Add(G(10, 44, ETA));
        grants.Add(G(10, 46, ETA));
        grants.Add(G(10, 51, ETA));
        grants.Add(G(10, 122, ETA));
        grants.Add(G(10, 111, ETA));
        grants.Add(G(10, 107, ETA));
        grants.Add(G(10, 108, ETA));

        // ============================================
        // OWNER (Template 11) — All grants at ETP
        // All AreaAdmin grants at ETP + system grants. Self-scoped stay SAR.
        // ============================================
        grants.Add(G(11, 21, SAR));
        grants.Add(G(11, 25, SAR));
        grants.Add(G(11, 65, SAR, useOwnJobType: true));
        grants.Add(G(11, 66, SAR, useOwnJobType: true));
        grants.Add(G(11, 67, SAR, useOwnJobType: true));
        grants.Add(G(11, 68, SAR, useOwnJobType: true));
        grants.Add(G(11, 1, ETP));
        grants.Add(G(11, 16, ETP));
        grants.Add(G(11, 12, ETP));
        grants.Add(G(11, 20, ETP));
        grants.Add(G(11, 115, ETP));
        grants.Add(G(11, 117, ETP));
        grants.Add(G(11, 35, ETP));
        grants.Add(G(11, 39, ETP));
        grants.Add(G(11, 110, ETP));
        grants.Add(G(11, 61, ETP));
        grants.Add(G(11, 62, ETP));
        grants.Add(G(11, 63, ETP));
        grants.Add(G(11, 64, ETP));
        grants.Add(G(11, 3, ETP, canGive: true));
        grants.Add(G(11, 4, ETP, canGive: true));
        grants.Add(G(11, 5, ETP, canGive: true));
        grants.Add(G(11, 2, ETP));
        grants.Add(G(11, 7, ETP));
        grants.Add(G(11, 8, ETP));
        grants.Add(G(11, 9, ETP));
        grants.Add(G(11, 10, ETP));
        grants.Add(G(11, 11, ETP));
        grants.Add(G(11, 69, ETP));
        grants.Add(G(11, 70, ETP));
        grants.Add(G(11, 71, ETP));
        grants.Add(G(11, 72, ETP));
        grants.Add(G(11, 73, ETP));
        grants.Add(G(11, 74, ETP));
        grants.Add(G(11, 75, ETP));
        grants.Add(G(11, 76, ETP));
        grants.Add(G(11, 109, ETP));
        grants.Add(G(11, 17, ETP));
        grants.Add(G(11, 18, ETP));
        grants.Add(G(11, 19, ETP));
        grants.Add(G(11, 22, ETP));
        grants.Add(G(11, 23, ETP));
        grants.Add(G(11, 26, ETP));
        grants.Add(G(11, 27, ETP));
        grants.Add(G(11, 28, ETP));
        grants.Add(G(11, 29, ETP));
        grants.Add(G(11, 30, ETP));
        grants.Add(G(11, 31, ETP));
        grants.Add(G(11, 34, ETP));
        grants.Add(G(11, 36, ETP, canGive: true));
        grants.Add(G(11, 37, ETP));
        grants.Add(G(11, 38, ETP, canGive: true));
        grants.Add(G(11, 41, ETP));
        grants.Add(G(11, 45, ETP));
        grants.Add(G(11, 48, ETP));
        grants.Add(G(11, 49, ETP));
        grants.Add(G(11, 50, ETP));
        grants.Add(G(11, 52, ETP));
        grants.Add(G(11, 53, ETP));
        grants.Add(G(11, 59, ETP));
        grants.Add(G(11, 116, ETP));
        grants.Add(G(11, 118, ETP));
        grants.Add(G(11, 112, ETP));
        grants.Add(G(11, 113, ETP));
        grants.Add(G(11, 114, ETP));
        grants.Add(G(11, 119, ETP));
        grants.Add(G(11, 120, ETP));
        grants.Add(G(11, 121, ETP));
        // AreaAdmin extras at ETP
        grants.Add(G(11, 13, ETP, canGive: true));
        grants.Add(G(11, 14, ETP, canGive: true));
        grants.Add(G(11, 15, ETP));
        grants.Add(G(11, 42, ETP));
        grants.Add(G(11, 43, ETP));
        grants.Add(G(11, 44, ETP));
        grants.Add(G(11, 46, ETP));
        grants.Add(G(11, 51, ETP));
        grants.Add(G(11, 122, ETP));
        grants.Add(G(11, 111, ETP));
        grants.Add(G(11, 107, ETP));
        grants.Add(G(11, 108, ETP));
        // Owner system-level extras
        grants.Add(G(11, 57, ETP, canGive: true));   // AdminAccess
        grants.Add(G(11, 58, ETP));                  // SystemConfiguration
        grants.Add(G(11, 60, ETP));                  // ManageApiKeys
        grants.Add(G(11, 54, ETP));                  // ExportData
        grants.Add(G(11, 55, ETP));                  // SendNotifications
        grants.Add(G(11, 56, ETP));                  // ConfigureEmailSettings
        grants.Add(G(11, 123, ETP));                 // ReorderHierarchy

        return grants;
    }
}
