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
                Key = "Lead",
                NameKey = "Role_Lead",
                DescriptionKey = "Role_Lead_Desc",
                DisplayNameEN = "Squad Leader",
                DisplayNameHE = "מפ\"צ",
                ScopeLevel = RoleScopeLevel.CompanyJobType,
                IsSystem = true,
                SortOrder = 20,
                DerivedUserRole = UserRole.Manager,
                IsVisibleInSignup = true,
                CanBeAssignedByDefault = true
            },
            new RoleTemplate
            {
                Id = 5,
                Key = "Director",
                NameKey = "Role_PlatoonLeader",
                DescriptionKey = "Role_PlatoonLeader_Desc",
                DisplayNameEN = "Platoon Leader",
                DisplayNameHE = "מ\"מ",
                ScopeLevel = RoleScopeLevel.MoleculeJobType,
                IsSystem = true,
                SortOrder = 5,
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
                IsVisibleInSignup = false,
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
                DisplayNameHE = "נחפף",
                ScopeLevel = RoleScopeLevel.Implicit,
                IsSystem = true,
                SortOrder = 101,
                DerivedUserRole = UserRole.Trainee,
                IsVisibleInSignup = false,
                CanBeAssignedByDefault = true
            }
        };
    }

    /// <summary>
    /// Sentinel TargetJobTypeId values for deployment-specific JobTypes.
    /// Resolved to actual DB IDs in Program.cs after ShiftyOrganizationSeed runs.
    /// Negative IDs ensure they never collide with real auto-generated IDs.
    /// </summary>
    public const int JT_SENTINEL_BR = -1;
    public const int JT_SENTINEL_HAKAM = -2;

    /// <summary>
    /// Maps sentinel values to JobType names for resolution in Program.cs.
    /// </summary>
    public static readonly Dictionary<int, string> JobTypeSentinelMap = new()
    {
        { JT_SENTINEL_BR, "BR" },
        { JT_SENTINEL_HAKAM, "Hakam" }
    };

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
        // EMPLOYEE (Template 1) — 21 grants
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
        // TRAINEE (Template 12) — 20 grants
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
        // ASSIGNER (Template 8) — 22 grants
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
        // Assigner extra: AREA-wide chore assignment (chore-only; other Assigner grants stay SAR).
        // Per product policy: Assigner needs to assign chores across the whole area, but no other
        // mutating grant — kept at SAR to keep blast radius limited to chores.
        grants.Add(G(8, 17, ETA));   // AssignChores (ETA — area-wide chore assignment)
        grants.Add(G(8, 34, ETA));   // ViewAllUsers (ETA — area-wide visibility for the assignees they can now reach)

        // ============================================
        // LEAD (Template 3) — 48 grants
        // Employee base (SAR) + lead-specific grants. Assign shifts at ETM.
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
        grants.Add(G(3, 4, ETM, useOwnJobType: true));    // AssignTextShifts (ETM, OWN)
        grants.Add(G(3, 2, ETM));    // ViewAllShifts (ETM — Lead/מפ"צ sees shifts across all companies in their molecule, matches AssignAlhut/AssignText molecule scope)
        grants.Add(G(3, 7, SAR, useOwnJobType: true));    // EditShiftPrograms (OWN)
        grants.Add(G(3, 8, SAR, useOwnJobType: true));    // CreateShiftPrograms (OWN)
        grants.Add(G(3, 9, SAR, useOwnJobType: true));    // DeleteShiftPrograms (OWN)
        grants.Add(G(3, 10, ETM, useOwnJobType: true));   // EditShiftTypes (MOLECULE, own JobType + null/global shifts — Lead/מפ"צ edits within their JobType)
        grants.Add(G(3, 11, ETM, useOwnJobType: true));   // CreateShiftTypes (MOLECULE, own JobType — Lead/מפ"צ creates within their JobType)
        grants.Add(G(3, 69, SAR, useOwnJobType: true));   // ManageAlhutBlueprints (OWN)
        grants.Add(G(3, 70, SAR, useOwnJobType: true));   // ManageAlhutPrograms (OWN)
        grants.Add(G(3, 71, SAR, useOwnJobType: true));   // ManageTextBlueprints (OWN)
        grants.Add(G(3, 72, SAR, useOwnJobType: true));   // ManageTextPrograms (OWN)
        grants.Add(G(3, 109, SAR, useOwnJobType: true));  // ManageShiftCapacity (OWN)
        grants.Add(G(3, 17, ETM));   // AssignChores (ETM — Lead/מפ"צ assigns chores across all companies in their molecule; cross-molecule still blocked)
        grants.Add(G(3, 22, SAR, useOwnJobType: true));   // ApproveVacations (OWN)
        grants.Add(G(3, 26, SAR, useOwnJobType: true));   // ApproveSwaps (OWN)
        grants.Add(G(3, 28, SAR));   // ViewUsers
        grants.Add(G(3, 36, SAR, useOwnJobType: true));   // AssignGrants (OWN)
        grants.Add(G(3, 37, SAR, useOwnJobType: true));   // RevokeGrants (OWN)
        grants.Add(G(3, 112, SAR));  // AccessAdminNavigation
        grants.Add(G(3, 114, SAR));  // ManagerHomeAccess
        grants.Add(G(3, 116, SAR, useOwnJobType: true));  // ManageJoinRequests (OWN)
        grants.Add(G(3, 38, SAR, useOwnJobType: true));   // AssignRoles (OWN — BUG 4 fix)
        grants.Add(G(3, 120, SAR));  // ViewSystemAlerts
        grants.Add(G(3, 118, SAR));  // EditCompanyUsers (GAP fix: Lead/מפ״צ must create soldiers in own company — Pages/Admin/Users.cshtml.cs:754)
        // Sidebar redesign: monitoring grants (molecule-scoped, own jobtype)
        grants.Add(G(3, 52, ETM, useOwnJobType: true));   // ViewAnalytics
        grants.Add(G(3, 53, ETM, useOwnJobType: true));   // ViewReports
        grants.Add(G(3, 59, ETM, useOwnJobType: true));   // ViewAuditLog
        // GAP fix: Lead peer-consistency with BRDirector/Director (both have these; Lead was missing)
        grants.Add(G(3, 32, SAR, useOwnJobType: true));   // ResetPasswords — Lead resets passwords for own-jobtype squad
        grants.Add(G(3, 33, SAR, useOwnJobType: true));   // AssignJobTypes — mirrors approval/assignment pattern
        grants.Add(G(3, 24, SAR, useOwnJobType: true));   // ApproveExtendedLeave — Lead approves extended leave for own jobtype

        // ============================================
        // BR DIRECTOR (Template 2) — 56 grants
        // Employee base (SAR) + BR management. AssignBRShifts + ViewAllShifts at ETM.
        // Dual ApproveVacations: BR + Hakam via TargetJobTypeId sentinels (resolved in Program.cs).
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
        grants.Add(G(2, 13, ETM, canGive: true));  // AssignHakamDuties (ETM — קב"ר assigns Hakam duties across their molecule)
        grants.Add(G(2, 2, ETM));                   // ViewAllShifts (ETM)
        grants.Add(G(2, 7, SAR, useOwnJobType: true));
        grants.Add(G(2, 8, SAR, useOwnJobType: true));
        grants.Add(G(2, 9, SAR, useOwnJobType: true));
        grants.Add(G(2, 10, ETM));   // EditShiftTypes (MOLECULE — קב"ר edits all shifts in molecule, all JobTypes, per hierarchy rule)
        grants.Add(G(2, 11, ETM));   // CreateShiftTypes (MOLECULE — קב"ר creates shifts across molecule, all JobTypes)
        grants.Add(G(2, 73, SAR));   // ManageBRBlueprints (ALL)
        grants.Add(G(2, 74, SAR));   // ManageBRPrograms (ALL)
        grants.Add(G(2, 75, SAR));   // ManageHakamBlueprints (ALL — BR manages Hakam)
        grants.Add(G(2, 76, SAR));   // ManageHakamPrograms (ALL)
        grants.Add(G(2, 109, SAR, useOwnJobType: true));
        grants.Add(G(2, 17, ETM));   // AssignChores (ETM — Kabar/קב"ר assigns chores across all companies in their molecule; cross-molecule still blocked)
        grants.Add(G(2, 22, SAR, targetJobTypeId: JT_SENTINEL_BR));     // ApproveVacations (BR)
        grants.Add(G(2, 22, SAR, targetJobTypeId: JT_SENTINEL_HAKAM));  // ApproveVacations (Hakam)
        grants.Add(G(2, 23, SAR));   // OverrideVacationLimits
        grants.Add(G(2, 26, SAR));   // ApproveSwaps (ALL)
        grants.Add(G(2, 27, SAR));   // InitiateSwap
        grants.Add(G(2, 28, SAR));   // ViewUsers
        grants.Add(G(2, 29, SAR));   // EditUsers
        grants.Add(G(2, 40, SAR));   // EditCompany (S-01)
        grants.Add(G(2, 116, SAR));  // ManageJoinRequests
        grants.Add(G(2, 38, SAR));   // AssignRoles
        grants.Add(G(2, 118, SAR));  // EditCompanyUsers
        grants.Add(G(2, 32, SAR));    // ResetPasswords (BUG 1 fix)
        grants.Add(G(2, 33, SAR));    // AssignJobTypes (BUG 2 fix)
        grants.Add(G(2, 24, SAR, targetJobTypeId: JT_SENTINEL_BR));   // ApproveExtendedLeave (BR — BUG 3 fix)
        grants.Add(G(2, 24, SAR, targetJobTypeId: JT_SENTINEL_HAKAM)); // ApproveExtendedLeave (Hakam — BUG 3 fix)
        grants.Add(G(2, 15, SAR, targetJobTypeId: JT_SENTINEL_HAKAM)); // GAP fix: EditDutyPrograms (Hakam — BR manages Hakam duty programs)
        grants.Add(G(2, 112, SAR));  // AccessAdminNavigation
        grants.Add(G(2, 114, SAR));  // ManagerHomeAccess
        grants.Add(G(2, 120, SAR));  // ViewSystemAlerts
        // Sidebar redesign: monitoring + people visibility
        grants.Add(G(2, 34, SAR));   // ViewAllUsers
        grants.Add(G(2, 52, SAR));   // ViewAnalytics
        grants.Add(G(2, 53, SAR));   // ViewReports
        grants.Add(G(2, 59, SAR));   // ViewAuditLog
        // GAP fix: BRDirector peer-to-Lead consistency (Lead has 36/37 at SAR useOwnJobType; BR owns all jobtypes in company)
        grants.Add(G(2, 36, SAR));   // AssignGrants — BR manages grants for their BR+Hakam staff
        grants.Add(G(2, 37, SAR));   // RevokeGrants — mirror of AssignGrants
        grants.Add(G(2, 119, SAR));  // ManageAnnouncements — BR communicates to company troops

        // ============================================
        // DIRECTOR (Template 5) — 58 grants
        // All Lead grants widened to ETM + director extras. Self-scoped stay SAR.
        // ============================================
        // Self-scoped (stay SAR)
        grants.Add(G(5, 21, SAR));
        grants.Add(G(5, 25, SAR));
        grants.Add(G(5, 65, SAR, useOwnJobType: true));
        grants.Add(G(5, 66, SAR, useOwnJobType: true));
        grants.Add(G(5, 67, SAR, useOwnJobType: true));
        grants.Add(G(5, 68, SAR, useOwnJobType: true));
        // Lead grants widened to ETM
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
        grants.Add(G(5, 4, ETM, canGive: true, useOwnJobType: true));  // AssignTextShifts (ETM, OWN, CanGive)
        grants.Add(G(5, 2, ETM));
        grants.Add(G(5, 7, ETM, useOwnJobType: true));
        grants.Add(G(5, 8, ETM, useOwnJobType: true));
        grants.Add(G(5, 9, ETM, useOwnJobType: true));
        grants.Add(G(5, 10, ETM, useOwnJobType: true));   // EditShiftTypes — Director/מ"מ limited to own JobType + null/global shifts
        grants.Add(G(5, 11, ETM, useOwnJobType: true));   // CreateShiftTypes — Director/מ"מ limited to own JobType
        grants.Add(G(5, 69, ETM, useOwnJobType: true));   // ManageAlhutBlueprints (OWN)
        grants.Add(G(5, 70, ETM, useOwnJobType: true));   // ManageAlhutPrograms (OWN)
        grants.Add(G(5, 71, ETM, useOwnJobType: true));   // ManageTextBlueprints (OWN)
        grants.Add(G(5, 72, ETM, useOwnJobType: true));   // ManageTextPrograms (OWN)
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
        grants.Add(G(5, 40, ETM));   // EditCompany (S-01)
        grants.Add(G(5, 113, ETM));  // DirectorHubAccess
        grants.Add(G(5, 116, ETM));  // ManageJoinRequests
        grants.Add(G(5, 118, ETM));  // EditCompanyUsers
        grants.Add(G(5, 119, ETM));  // ManageAnnouncements
        grants.Add(G(5, 121, ETM));  // ViewAllAreas
        grants.Add(G(5, 32, ETM));   // ResetPasswords (BUG 1 fix)
        grants.Add(G(5, 33, ETM));   // AssignJobTypes (BUG 2 fix)
        grants.Add(G(5, 24, ETM, useOwnJobType: true));  // ApproveExtendedLeave (OWN — BUG 3 fix)
        // Sidebar redesign: monitoring grants (molecule-scoped, own jobtype)
        grants.Add(G(5, 52, ETM, useOwnJobType: true));   // ViewAnalytics
        grants.Add(G(5, 53, ETM, useOwnJobType: true));   // ViewReports
        grants.Add(G(5, 59, ETM, useOwnJobType: true));   // ViewAuditLog
        grants.Add(G(5, 122, ETM));  // ManageOnDuty (GAP fix: Director/מ״מ assigns on-duty at molecule scope)
        // GAP fix: Director manager-level grants (BRDirector has these; Director was missing)
        grants.Add(G(5, 23, ETM, useOwnJobType: true));  // OverrideVacationLimits — Director overrides for own jobtype at molecule
        grants.Add(G(5, 27, ETM, useOwnJobType: true));  // InitiateSwap — Director initiates swaps on behalf of own-jobtype soldiers
        grants.Add(G(5, 31, ETM, useOwnJobType: true));  // DeactivateUsers — scoped to own jobtype (security review: prevents cross-jobtype deactivation)

        // ============================================
        // MOLECULE ADMIN (Template 7) — 94 grants
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
        grants.Add(G(7, 40, ETM));   // EditCompany (S-01)
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
        grants.Add(G(7, 32, ETM));   // ResetPasswords (BUG 1 fix)
        grants.Add(G(7, 33, ETM));   // AssignJobTypes (BUG 2 fix)
        grants.Add(G(7, 24, ETM));   // ApproveExtendedLeave (ALL — BUG 3 fix)
        // Tech grants (GAP 1 fix)
        grants.Add(G(7, 6, ETM));    // AssignTechShifts
        grants.Add(G(7, 77, ETM));   // ViewHanavaCalendar
        grants.Add(G(7, 78, ETM));   // ViewDeltaCalendar
        grants.Add(G(7, 79, ETM));   // ViewYekevCalendar
        grants.Add(G(7, 80, ETM));   // ViewMoviltechCalendar
        grants.Add(G(7, 81, ETM));   // AssignHanavaShifts
        grants.Add(G(7, 82, ETM));   // AssignDeltaShifts
        grants.Add(G(7, 83, ETM));   // AssignYekevShifts
        grants.Add(G(7, 84, ETM));   // AssignMoviltechShifts
        grants.Add(G(7, 85, ETM));   // ManageHanavaBlueprints
        grants.Add(G(7, 86, ETM));   // ManageHanavaPrograms
        grants.Add(G(7, 87, ETM));   // ManageDeltaBlueprints
        grants.Add(G(7, 88, ETM));   // ManageDeltaPrograms
        grants.Add(G(7, 89, ETM));   // ManageYekevBlueprints
        grants.Add(G(7, 90, ETM));   // ManageYekevPrograms
        grants.Add(G(7, 91, ETM));   // ManageMoviltechBlueprints
        grants.Add(G(7, 92, ETM));   // ManageMoviltechPrograms
        grants.Add(G(7, 124, ETM));  // ManageHierarchy (add/rename/delete companies & departments)
        // Sidebar redesign: duty rotation management
        grants.Add(G(7, 122, ETM));  // ManageOnDuty
        // GAP fix: MoleculeAdmin Duty bundle + admin completeness (mirrors AreaAdmin's coherent bundle)
        grants.Add(G(7, 13, ETM, canGive: true));  // AssignHakamDuties — Hakam duty assignment across molecule
        grants.Add(G(7, 14, ETM, canGive: true));  // AssignKatzinDuties — Katzin duty assignment across molecule
        grants.Add(G(7, 15, ETM));                 // EditDutyPrograms — edit duty programs (ALL duty types at molecule)
        grants.Add(G(7, 107, ETM));                // ManageKatzinBlueprints — parallel to Hakam blueprints
        grants.Add(G(7, 108, ETM));                // ManageKatzinPrograms — parallel to Hakam programs
        grants.Add(G(7, 113, ETM));                // DirectorHubAccess — MoleculeAdmin is a hub-level admin
        grants.Add(G(7, 47, ETM));                 // ManageDepartments — departments are molecule-scoped (page gates on 47 specifically)
        grants.Add(G(7, 121, ETM));                // ViewAllAreas — parity with Director (below) which has 121 at ETM

        // ============================================
        // DEPARTMENT LEAD (Template 9) — 48 grants
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
        grants.Add(G(9, 112, SAR));  // AccessAdminNavigation (GAP 3 fix)
        grants.Add(G(9, 114, SAR));  // ManagerHomeAccess (GAP 3 fix)
        // Tech grants (GAP 1 fix)
        grants.Add(G(9, 6, SAR));    // AssignTechShifts
        grants.Add(G(9, 77, SAR));   // ViewHanavaCalendar
        grants.Add(G(9, 78, SAR));   // ViewDeltaCalendar
        grants.Add(G(9, 79, SAR));   // ViewYekevCalendar
        grants.Add(G(9, 80, SAR));   // ViewMoviltechCalendar
        grants.Add(G(9, 81, SAR));   // AssignHanavaShifts
        grants.Add(G(9, 82, SAR));   // AssignDeltaShifts
        grants.Add(G(9, 83, SAR));   // AssignYekevShifts
        grants.Add(G(9, 84, SAR));   // AssignMoviltechShifts
        grants.Add(G(9, 85, SAR));   // ManageHanavaBlueprints
        grants.Add(G(9, 86, SAR));   // ManageHanavaPrograms
        grants.Add(G(9, 87, SAR));   // ManageDeltaBlueprints
        grants.Add(G(9, 88, SAR));   // ManageDeltaPrograms
        grants.Add(G(9, 89, SAR));   // ManageYekevBlueprints
        grants.Add(G(9, 90, SAR));   // ManageYekevPrograms
        grants.Add(G(9, 91, SAR));   // ManageMoviltechBlueprints
        grants.Add(G(9, 92, SAR));   // ManageMoviltechPrograms
        // Monitoring grants (GAP fix: Lead has these, DepartmentLead should too)
        grants.Add(G(9, 59, SAR));   // ViewAuditLog
        grants.Add(G(9, 120, SAR));  // ViewSystemAlerts
        grants.Add(G(9, 118, SAR));  // EditCompanyUsers (GAP fix: DeptLead needs 118, not just 29 — Pages/Admin/Users.cshtml.cs:754)
        grants.Add(G(9, 122, SAR));  // ManageOnDuty (GAP fix: tech department duty rotation)
        // GAP fix: DepartmentLead operational authority (tech team had no local approver/admin)
        grants.Add(G(9, 22, SAR));   // ApproveVacations — dept head approves tech team vacations
        grants.Add(G(9, 26, SAR));   // ApproveSwaps — dept head approves swaps within department
        grants.Add(G(9, 24, SAR));   // ApproveExtendedLeave — dept head approves extended leave
        grants.Add(G(9, 32, SAR));   // ResetPasswords — dept head resets tech passwords
        grants.Add(G(9, 38, SAR));   // AssignRoles — dept head assigns roles within department
        grants.Add(G(9, 17, SAR));   // AssignChores — techs also do chores; dept head assigns
        grants.Add(G(9, 116, SAR));  // ManageJoinRequests — dept head reviews new tech applicants

        // ============================================
        // AREA ADMIN (Template 10) — 119 grants
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
        grants.Add(G(10, 40, ETA));   // EditCompany (S-01)
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
        grants.Add(G(10, 32, ETA));   // ResetPasswords (BUG 1 fix)
        grants.Add(G(10, 33, ETA));   // AssignJobTypes (BUG 2 fix)
        grants.Add(G(10, 24, ETA));   // ApproveExtendedLeave (ALL — BUG 3 fix)
        grants.Add(G(10, 47, ETA));   // ManageDepartments
        grants.Add(G(10, 124, ETA));  // ManageHierarchy (add/rename/delete companies & departments)
        // Tech grants (GAP 1 fix)
        grants.Add(G(10, 6, ETA));    // AssignTechShifts
        grants.Add(G(10, 77, ETA));   // ViewHanavaCalendar
        grants.Add(G(10, 78, ETA));   // ViewDeltaCalendar
        grants.Add(G(10, 79, ETA));   // ViewYekevCalendar
        grants.Add(G(10, 80, ETA));   // ViewMoviltechCalendar
        grants.Add(G(10, 81, ETA));   // AssignHanavaShifts
        grants.Add(G(10, 82, ETA));   // AssignDeltaShifts
        grants.Add(G(10, 83, ETA));   // AssignYekevShifts
        grants.Add(G(10, 84, ETA));   // AssignMoviltechShifts
        grants.Add(G(10, 85, ETA));   // ManageHanavaBlueprints
        grants.Add(G(10, 86, ETA));   // ManageHanavaPrograms
        grants.Add(G(10, 87, ETA));   // ManageDeltaBlueprints
        grants.Add(G(10, 88, ETA));   // ManageDeltaPrograms
        grants.Add(G(10, 89, ETA));   // ManageYekevBlueprints
        grants.Add(G(10, 90, ETA));   // ManageYekevPrograms
        grants.Add(G(10, 91, ETA));   // ManageMoviltechBlueprints
        grants.Add(G(10, 92, ETA));   // ManageMoviltechPrograms
        // Helper molecule — Shiklut (GAP fix: AreaAdmin manages all molecules in area)
        grants.Add(G(10, 97, ETA));   // ViewShiklutCalendar
        grants.Add(G(10, 98, ETA));   // AssignShiklutChores
        grants.Add(G(10, 99, ETA));   // ManageShiklutBlueprints
        grants.Add(G(10, 100, ETA));  // ManageShiklutPrograms
        grants.Add(G(10, 101, ETA));  // CanBeAssignedShiklut
        // Helper molecule — NOC (GAP fix: AreaAdmin manages all molecules in area)
        grants.Add(G(10, 102, ETA));  // ViewNOCCalendar
        grants.Add(G(10, 103, ETA));  // AssignNOCChores
        grants.Add(G(10, 104, ETA));  // ManageNOCBlueprints
        grants.Add(G(10, 105, ETA));  // ManageNOCPrograms
        grants.Add(G(10, 106, ETA));  // CanBeAssignedNOC

        // ============================================
        // OWNER (Template 11) — 134 grants at ETP (ALL grant types, ALL canGive)
        // Self-scoped grants stay SAR. Every other grant at ETP with canGive:true.
        // ============================================
        // Self-scoped grants (SAR)
        grants.Add(G(11, 21, SAR, canGive: true));   // RequestVacation
        grants.Add(G(11, 25, SAR, canGive: true));   // RequestSwap
        grants.Add(G(11, 65, SAR, canGive: true, useOwnJobType: true));  // CanBeAssignedAlhutShifts
        grants.Add(G(11, 66, SAR, canGive: true, useOwnJobType: true));  // CanBeAssignedTextShifts
        grants.Add(G(11, 67, SAR, canGive: true, useOwnJobType: true));  // CanBeAssignedBRShifts
        grants.Add(G(11, 68, SAR, canGive: true, useOwnJobType: true));  // CanBeAssignedHakamShifts
        // View grants (ETP)
        grants.Add(G(11, 1, ETP, canGive: true));    // ViewShifts
        grants.Add(G(11, 2, ETP, canGive: true));    // ViewAllShifts
        grants.Add(G(11, 12, ETP, canGive: true));   // ViewDuties
        grants.Add(G(11, 16, ETP, canGive: true));   // ViewChores
        grants.Add(G(11, 20, ETP, canGive: true));   // ViewVacations
        grants.Add(G(11, 28, ETP, canGive: true));   // ViewUsers
        grants.Add(G(11, 34, ETP, canGive: true));   // ViewAllUsers
        grants.Add(G(11, 35, ETP, canGive: true));   // ViewGrants
        grants.Add(G(11, 39, ETP, canGive: true));   // ViewHierarchy
        grants.Add(G(11, 61, ETP, canGive: true));   // ViewAlhutShiftCalendar
        grants.Add(G(11, 62, ETP, canGive: true));   // ViewTextShiftCalendar
        grants.Add(G(11, 63, ETP, canGive: true));   // ViewBRShiftCalendar
        grants.Add(G(11, 64, ETP, canGive: true));   // ViewHakamShiftCalendar
        grants.Add(G(11, 115, ETP, canGive: true));  // ViewCompanyCalendar
        grants.Add(G(11, 117, ETP, canGive: true));  // ViewCompanyUsers
        // Shift assignment + management (ETP)
        grants.Add(G(11, 3, ETP, canGive: true));    // AssignAlhutShifts
        grants.Add(G(11, 4, ETP, canGive: true));    // AssignTextShifts
        grants.Add(G(11, 5, ETP, canGive: true));    // AssignBRShifts
        grants.Add(G(11, 6, ETP, canGive: true));    // AssignTechShifts
        grants.Add(G(11, 7, ETP, canGive: true));    // EditShiftPrograms
        grants.Add(G(11, 8, ETP, canGive: true));    // CreateShiftPrograms
        grants.Add(G(11, 9, ETP, canGive: true));    // DeleteShiftPrograms
        grants.Add(G(11, 10, ETP, canGive: true));   // EditShiftTypes
        grants.Add(G(11, 11, ETP, canGive: true));   // CreateShiftTypes
        grants.Add(G(11, 109, ETP, canGive: true));  // ManageShiftCapacity
        grants.Add(G(11, 110, ETP, canGive: true));  // WriteOverviewNotes
        // Blueprint/program management by type (ETP)
        grants.Add(G(11, 69, ETP, canGive: true));   // ManageAlhutBlueprints
        grants.Add(G(11, 70, ETP, canGive: true));   // ManageAlhutPrograms
        grants.Add(G(11, 71, ETP, canGive: true));   // ManageTextBlueprints
        grants.Add(G(11, 72, ETP, canGive: true));   // ManageTextPrograms
        grants.Add(G(11, 73, ETP, canGive: true));   // ManageBRBlueprints
        grants.Add(G(11, 74, ETP, canGive: true));   // ManageBRPrograms
        grants.Add(G(11, 75, ETP, canGive: true));   // ManageHakamBlueprints
        grants.Add(G(11, 76, ETP, canGive: true));   // ManageHakamPrograms
        // Duty grants (ETP)
        grants.Add(G(11, 13, ETP, canGive: true));   // AssignHakamDuties
        grants.Add(G(11, 14, ETP, canGive: true));   // AssignKatzinDuties
        grants.Add(G(11, 15, ETP, canGive: true));   // EditDutyPrograms
        grants.Add(G(11, 111, ETP, canGive: true));  // ManageOnDutyTypes
        grants.Add(G(11, 122, ETP, canGive: true));  // ManageOnDuty
        // Chore grants (ETP)
        grants.Add(G(11, 17, ETP, canGive: true));   // AssignChores
        grants.Add(G(11, 18, ETP, canGive: true));   // EditChoreTypes
        grants.Add(G(11, 19, ETP, canGive: true));   // CreateChoreTypes
        // Vacation grants (ETP)
        grants.Add(G(11, 22, ETP, canGive: true));   // ApproveVacations
        grants.Add(G(11, 23, ETP, canGive: true));   // OverrideVacationLimits
        grants.Add(G(11, 24, ETP, canGive: true));   // ApproveExtendedLeave
        // Swap grants (ETP)
        grants.Add(G(11, 26, ETP, canGive: true));   // ApproveSwaps
        grants.Add(G(11, 27, ETP, canGive: true));   // InitiateSwap
        // User management (ETP)
        grants.Add(G(11, 29, ETP, canGive: true));   // EditUsers
        grants.Add(G(11, 30, ETP, canGive: true));   // CreateUsers
        grants.Add(G(11, 31, ETP, canGive: true));   // DeactivateUsers
        grants.Add(G(11, 32, ETP, canGive: true));   // ResetPasswords
        grants.Add(G(11, 33, ETP, canGive: true));   // AssignJobTypes
        grants.Add(G(11, 116, ETP, canGive: true));  // ManageJoinRequests
        grants.Add(G(11, 118, ETP, canGive: true));  // EditCompanyUsers
        // Grant management (ETP)
        grants.Add(G(11, 36, ETP, canGive: true));   // AssignGrants
        grants.Add(G(11, 37, ETP, canGive: true));   // RevokeGrants
        grants.Add(G(11, 38, ETP, canGive: true));   // AssignRoles
        // Hierarchy (ETP)
        grants.Add(G(11, 40, ETP, canGive: true));   // EditCompany
        grants.Add(G(11, 41, ETP, canGive: true));   // EditMolecule
        grants.Add(G(11, 42, ETP, canGive: true));   // EditArea
        grants.Add(G(11, 43, ETP, canGive: true));   // CreateCompany
        grants.Add(G(11, 44, ETP, canGive: true));   // CreateMolecule
        grants.Add(G(11, 45, ETP, canGive: true));   // ManageShiftGroupings
        grants.Add(G(11, 46, ETP, canGive: true));   // ManageJobTypes
        grants.Add(G(11, 47, ETP, canGive: true));   // ManageDepartments
        grants.Add(G(11, 123, ETP, canGive: true));  // ReorderHierarchy
        grants.Add(G(11, 124, ETP, canGive: true));  // ManageHierarchy
        // Settings (ETP)
        grants.Add(G(11, 48, ETP, canGive: true));   // ViewSettings
        grants.Add(G(11, 49, ETP, canGive: true));   // EditCompanySettings
        grants.Add(G(11, 50, ETP, canGive: true));   // EditMoleculeSettings
        grants.Add(G(11, 51, ETP, canGive: true));   // EditAreaSettings
        // Analytics (ETP)
        grants.Add(G(11, 52, ETP, canGive: true));   // ViewAnalytics
        grants.Add(G(11, 53, ETP, canGive: true));   // ViewReports
        grants.Add(G(11, 54, ETP, canGive: true));   // ExportData
        // Email (ETP)
        grants.Add(G(11, 55, ETP, canGive: true));   // SendNotifications
        grants.Add(G(11, 56, ETP, canGive: true));   // ConfigureEmailSettings
        // System (ETP)
        grants.Add(G(11, 57, ETP, canGive: true));   // AdminAccess
        grants.Add(G(11, 58, ETP, canGive: true));   // SystemConfiguration
        grants.Add(G(11, 59, ETP, canGive: true));   // ViewAuditLog
        grants.Add(G(11, 60, ETP, canGive: true));   // ManageApiKeys
        // Navigation (ETP)
        grants.Add(G(11, 112, ETP, canGive: true));  // AccessAdminNavigation
        grants.Add(G(11, 113, ETP, canGive: true));  // DirectorHubAccess
        grants.Add(G(11, 114, ETP, canGive: true));  // ManagerHomeAccess
        grants.Add(G(11, 119, ETP, canGive: true));  // ManageAnnouncements
        grants.Add(G(11, 120, ETP, canGive: true));  // ViewSystemAlerts
        grants.Add(G(11, 121, ETP, canGive: true));  // ViewAllAreas
        // Tech calendars + assignment + management (ETP)
        grants.Add(G(11, 77, ETP, canGive: true));   // ViewHanavaCalendar
        grants.Add(G(11, 78, ETP, canGive: true));   // ViewDeltaCalendar
        grants.Add(G(11, 79, ETP, canGive: true));   // ViewYekevCalendar
        grants.Add(G(11, 80, ETP, canGive: true));   // ViewMoviltechCalendar
        grants.Add(G(11, 81, ETP, canGive: true));   // AssignHanavaShifts
        grants.Add(G(11, 82, ETP, canGive: true));   // AssignDeltaShifts
        grants.Add(G(11, 83, ETP, canGive: true));   // AssignYekevShifts
        grants.Add(G(11, 84, ETP, canGive: true));   // AssignMoviltechShifts
        grants.Add(G(11, 85, ETP, canGive: true));   // ManageHanavaBlueprints
        grants.Add(G(11, 86, ETP, canGive: true));   // ManageHanavaPrograms
        grants.Add(G(11, 87, ETP, canGive: true));   // ManageDeltaBlueprints
        grants.Add(G(11, 88, ETP, canGive: true));   // ManageDeltaPrograms
        grants.Add(G(11, 89, ETP, canGive: true));   // ManageYekevBlueprints
        grants.Add(G(11, 90, ETP, canGive: true));   // ManageYekevPrograms
        grants.Add(G(11, 91, ETP, canGive: true));   // ManageMoviltechBlueprints
        grants.Add(G(11, 92, ETP, canGive: true));   // ManageMoviltechPrograms
        // Tech eligibility (ETP — Owner can delegate to users)
        grants.Add(G(11, 93, ETP, canGive: true));   // CanBeAssignedHanava
        grants.Add(G(11, 94, ETP, canGive: true));   // CanBeAssignedDelta
        grants.Add(G(11, 95, ETP, canGive: true));   // CanBeAssignedYekev
        grants.Add(G(11, 96, ETP, canGive: true));   // CanBeAssignedMoviltech
        // Helper molecule — Shiklut (ETP)
        grants.Add(G(11, 97, ETP, canGive: true));   // ViewShiklutCalendar
        grants.Add(G(11, 98, ETP, canGive: true));   // AssignShiklutChores
        grants.Add(G(11, 99, ETP, canGive: true));   // ManageShiklutBlueprints
        grants.Add(G(11, 100, ETP, canGive: true));  // ManageShiklutPrograms
        grants.Add(G(11, 101, ETP, canGive: true));  // CanBeAssignedShiklut
        // Helper molecule — NOC (ETP)
        grants.Add(G(11, 102, ETP, canGive: true));  // ViewNOCCalendar
        grants.Add(G(11, 103, ETP, canGive: true));  // AssignNOCChores
        grants.Add(G(11, 104, ETP, canGive: true));  // ManageNOCBlueprints
        grants.Add(G(11, 105, ETP, canGive: true));  // ManageNOCPrograms
        grants.Add(G(11, 106, ETP, canGive: true));  // CanBeAssignedNOC
        // Katzin duty management (ETP)
        grants.Add(G(11, 107, ETP, canGive: true));  // ManageKatzinBlueprints
        grants.Add(G(11, 108, ETP, canGive: true));  // ManageKatzinPrograms
        // Home rotation management (P2-14 fix: Owner must have ManageHomeTypes)
        grants.Add(G(11, 125, ETP, canGive: true));  // ManageHomeTypes

        // ============================================
        // LATE ADDITIONS (appended to preserve sequential IDs)
        // ============================================

        // ManageHomeTypes (ID 125) — Home rotation type management
        grants.Add(G(7, 125, ETM));                     // MoleculeAdmin: ManageHomeTypes (molecule-scoped)
        grants.Add(G(10, 125, ETA));                    // AreaAdmin: ManageHomeTypes (GAP fix: was missing despite being above MoleculeAdmin)

        // ManageStores (ID 126) — Store definitions and opening hours
        grants.Add(G(7, 126, ETM));                     // MoleculeAdmin: ManageStores (molecule → area expansion)
        grants.Add(G(10, 126, ETA));                    // AreaAdmin: ManageStores (area-scoped)
        grants.Add(G(11, 126, ETP, canGive: true));     // Owner: ManageStores (project-wide, can delegate)

        // ViewHakamOnCall (ID 127) — On-Call Widget: Hakam visibility
        // Matches ViewDuties (12) distribution — anyone who can view duties should see widget Hakam data
        grants.Add(G(1, 127, SAR));                      // Employee
        grants.Add(G(12, 127, SAR));                     // Trainee
        grants.Add(G(8, 127, SAR));                      // Assigner
        grants.Add(G(3, 127, SAR));                      // Lead
        grants.Add(G(2, 127, SAR));                      // BRDirector
        grants.Add(G(9, 127, SAR));                      // DepartmentLead
        grants.Add(G(5, 127, ETM));                      // Director
        grants.Add(G(7, 127, ETM));                      // MoleculeAdmin
        grants.Add(G(10, 127, ETA));                     // AreaAdmin
        grants.Add(G(11, 127, ETP, canGive: true));      // Owner

        // ViewCompanyOnCall (ID 128) — On-Call Widget: company-specific on-call contacts
        // Matches ViewDuties (12) distribution — company-scoped on-call data
        grants.Add(G(1, 128, SAR));                      // Employee
        grants.Add(G(12, 128, SAR));                     // Trainee
        grants.Add(G(8, 128, SAR));                      // Assigner
        grants.Add(G(3, 128, SAR));                      // Lead
        grants.Add(G(2, 128, SAR));                      // BRDirector
        grants.Add(G(9, 128, SAR));                      // DepartmentLead
        grants.Add(G(5, 128, ETM));                      // Director
        grants.Add(G(7, 128, ETM));                      // MoleculeAdmin
        grants.Add(G(10, 128, ETA));                     // AreaAdmin
        grants.Add(G(11, 128, ETP, canGive: true));      // Owner

        // ViewShiftsMolecule (ID 129) — Scope Switcher: molecule-level access
        // Molecule-level+ roles only (Director/Owner already bypass via code, but included for consistency)
        grants.Add(G(5, 129, ETM));                      // Director
        grants.Add(G(7, 129, ETM));                      // MoleculeAdmin
        grants.Add(G(10, 129, ETA));                     // AreaAdmin
        grants.Add(G(11, 129, ETP, canGive: true));      // Owner

        // ViewShiftsArea (ID 130) — Scope Switcher: area-level access
        // Area-level+ roles only (Owner already bypasses via code, but included for consistency)
        grants.Add(G(10, 130, ETA));                     // AreaAdmin
        grants.Add(G(11, 130, ETP, canGive: true));      // Owner

        // EditChoreTypes (ID 18) — grant to Lead, BRDirector, MoleculeAdmin
        grants.Add(G(3, 18, SAR));    // Lead — EditChoreTypes at SameAsRole (company+jobtype)
        grants.Add(G(2, 18, SAR));    // BRDirector — EditChoreTypes at SameAsRole (company)
        grants.Add(G(2, 122, ETM));   // ManageOnDuty (ETM — Kabar can assign on-duty across all companies in their molecule)
        grants.Add(G(2, 113, SAR));   // DirectorHubAccess (GAP fix: BRDirector/קב״ר peer to Director — needs hub visibility)
        // NOTE: G(7, 18, ETM) removed here — duplicate of line ~514 (seed dedupe 2026-04-15).

        // ============================================
        // ManageOnDutyTypes (ID 111) — Duty-type admin (GAP fix 2026-04-15: was only on AreaAdmin + Owner)
        // ============================================
        grants.Add(G(2, 111, SAR));   // BRDirector — ManageOnDutyTypes at SameAsRole (company)
        grants.Add(G(3, 111, SAR));   // Lead — ManageOnDutyTypes at SameAsRole (company)
        grants.Add(G(7, 111, ETM));   // MoleculeAdmin — ManageOnDutyTypes at ExpandToMolecule

        // ============================================
        // EditOnCallCalendar (ID 131) — Collaborative on-call editing (2026-04-15)
        // Area-scoped. Granted broadly to hakam-eligible roles. POST handlers still IDOR-check.
        // ============================================
        grants.Add(G(1, 131, ETA));   // Employee
        grants.Add(G(2, 131, ETA));   // BRDirector
        grants.Add(G(3, 131, ETA));   // Lead
        grants.Add(G(5, 131, ETA));   // Director
        grants.Add(G(7, 131, ETA));   // MoleculeAdmin
        grants.Add(G(8, 131, SAR));   // Assigner — SAR per invariant (AssignerRoleTests: inherited grants stay at SameAsRole)
        grants.Add(G(9, 131, ETA));   // DepartmentLead
        grants.Add(G(10, 131, ETA));  // AreaAdmin
        grants.Add(G(11, 131, ETP, canGive: true));  // Owner
        // Trainee intentionally omitted — learning role, no write access.

        // ============================================
        // EditAreaCalendarPalette (ID 132) — Per-area color overrides (2026-04-15)
        // ============================================
        grants.Add(G(2, 132, SAR));   // BRDirector
        grants.Add(G(7, 132, ETM));   // MoleculeAdmin
        grants.Add(G(10, 132, ETA));  // AreaAdmin
        grants.Add(G(11, 132, ETP, canGive: true));  // Owner

        // ============================================
        // ViewJusticeTable (ID 133) — Justice analytics page (2026-05-03)
        // Distributed to assignment-decision-makers across the hierarchy.
        // Each role sees their natural scope (Assigner = molecule, Lead/BRDirector = molecule
        // per the 2026-04-25 cross-tenant scope expansion, AreaAdmin = area, Owner = project).
        // ============================================
        grants.Add(G(2, 133, ETM));   // BRDirector — molecule (per 2026-04-25 scope expansion)
        grants.Add(G(3, 133, ETM));   // Lead — molecule (per 2026-04-25 scope expansion)
        grants.Add(G(5, 133, ETM));   // Director — molecule
        grants.Add(G(7, 133, ETM));   // MoleculeAdmin
        grants.Add(G(8, 133, SAR));   // Assigner — molecule (their RoleTemplate scope is Molecule)
        grants.Add(G(10, 133, ETA));  // AreaAdmin
        grants.Add(G(11, 133, ETP, canGive: true));  // Owner

        // ============================================
        // EditJusticeTargets (ID 134) — Justice analytics targets/overrides (2026-05-03)
        // Tight by default: only Owner + AreaAdmin can edit. The settings modal (Phase 2)
        // adds an IDOR check to verify the user is allowed to edit at the requested scope.
        // ============================================
        grants.Add(G(10, 134, ETA));                  // AreaAdmin — area-scoped overrides
        grants.Add(G(11, 134, ETP, canGive: true));   // Owner — project-wide

        return grants;
    }
}
