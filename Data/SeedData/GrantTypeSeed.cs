using ShiftManager.Models;
using ShiftManager.Models.Support;

namespace ShiftManager.Data.SeedData;

public static class GrantTypeSeed
{
    // WARNING: GrantType IDs are assigned sequentially via id++. NEVER insert new entries in the middle —
    // always append at the end. RoleTemplateSeed references grants by numeric ID.
    public static List<GrantType> GetGrantTypes()
    {
        var grants = new List<GrantType>();
        int id = 1;

        // ============================================
        // SHIFT GRANTS (Category.Shift)
        // ============================================

        // View shifts
        grants.Add(new GrantType { Id = id++, Key = "ViewShifts", NameKey = "Grant_ViewShifts", DescriptionKey = "Grant_ViewShifts_Desc", Category = GrantCategory.Shift, DefaultScope = GrantScopeLevel.Company, IsSystem = true });
        grants.Add(new GrantType { Id = id++, Key = "ViewAllShifts", NameKey = "Grant_ViewAllShifts", DescriptionKey = "Grant_ViewAllShifts_Desc", Category = GrantCategory.Shift, DefaultScope = GrantScopeLevel.Molecule, IsSystem = true });

        // Assign shifts by type
        grants.Add(new GrantType { Id = id++, Key = "AssignAlhutShifts", NameKey = "Grant_AssignAlhutShifts", DescriptionKey = "Grant_AssignAlhutShifts_Desc", Category = GrantCategory.Shift, DefaultScope = GrantScopeLevel.Company, IsSystem = true });
        grants.Add(new GrantType { Id = id++, Key = "AssignTextShifts", NameKey = "Grant_AssignTextShifts", DescriptionKey = "Grant_AssignTextShifts_Desc", Category = GrantCategory.Shift, DefaultScope = GrantScopeLevel.Company, IsSystem = true });
        grants.Add(new GrantType { Id = id++, Key = "AssignBRShifts", NameKey = "Grant_AssignBRShifts", DescriptionKey = "Grant_AssignBRShifts_Desc", Category = GrantCategory.Shift, DefaultScope = GrantScopeLevel.Molecule, IsSystem = true });
        grants.Add(new GrantType { Id = id++, Key = "AssignTechShifts", NameKey = "Grant_AssignTechShifts", DescriptionKey = "Grant_AssignTechShifts_Desc", Category = GrantCategory.Shift, DefaultScope = GrantScopeLevel.Department, IsSystem = true });

        // Edit shift programs
        grants.Add(new GrantType { Id = id++, Key = "EditShiftPrograms", NameKey = "Grant_EditShiftPrograms", DescriptionKey = "Grant_EditShiftPrograms_Desc", Category = GrantCategory.Shift, DefaultScope = GrantScopeLevel.Company, IsSystem = true });
        grants.Add(new GrantType { Id = id++, Key = "CreateShiftPrograms", NameKey = "Grant_CreateShiftPrograms", DescriptionKey = "Grant_CreateShiftPrograms_Desc", Category = GrantCategory.Shift, DefaultScope = GrantScopeLevel.Company, IsSystem = true });
        grants.Add(new GrantType { Id = id++, Key = "DeleteShiftPrograms", NameKey = "Grant_DeleteShiftPrograms", DescriptionKey = "Grant_DeleteShiftPrograms_Desc", Category = GrantCategory.Shift, DefaultScope = GrantScopeLevel.Company, IsSystem = true });

        // Edit shift types/blueprints
        grants.Add(new GrantType { Id = id++, Key = "EditShiftTypes", NameKey = "Grant_EditShiftTypes", DescriptionKey = "Grant_EditShiftTypes_Desc", Category = GrantCategory.Shift, DefaultScope = GrantScopeLevel.Company, IsSystem = true });
        grants.Add(new GrantType { Id = id++, Key = "CreateShiftTypes", NameKey = "Grant_CreateShiftTypes", DescriptionKey = "Grant_CreateShiftTypes_Desc", Category = GrantCategory.Shift, DefaultScope = GrantScopeLevel.Company, IsSystem = true });

        // ============================================
        // DUTY GRANTS (Category.Duty)
        // ============================================

        grants.Add(new GrantType { Id = id++, Key = "ViewDuties", NameKey = "Grant_ViewDuties", DescriptionKey = "Grant_ViewDuties_Desc", Category = GrantCategory.Duty, DefaultScope = GrantScopeLevel.Area, IsSystem = true });
        grants.Add(new GrantType { Id = id++, Key = "AssignHakamDuties", NameKey = "Grant_AssignHakamDuties", DescriptionKey = "Grant_AssignHakamDuties_Desc", Category = GrantCategory.Duty, DefaultScope = GrantScopeLevel.Area, IsSystem = true });
        grants.Add(new GrantType { Id = id++, Key = "AssignKatzinDuties", NameKey = "Grant_AssignKatzinDuties", DescriptionKey = "Grant_AssignKatzinDuties_Desc", Category = GrantCategory.Duty, DefaultScope = GrantScopeLevel.Area, IsSystem = true });
        grants.Add(new GrantType { Id = id++, Key = "EditDutyPrograms", NameKey = "Grant_EditDutyPrograms", DescriptionKey = "Grant_EditDutyPrograms_Desc", Category = GrantCategory.Duty, DefaultScope = GrantScopeLevel.Area, IsSystem = true });

        // ============================================
        // CHORE GRANTS (Category.Chore)
        // ============================================

        grants.Add(new GrantType { Id = id++, Key = "ViewChores", NameKey = "Grant_ViewChores", DescriptionKey = "Grant_ViewChores_Desc", Category = GrantCategory.Chore, DefaultScope = GrantScopeLevel.Molecule, IsSystem = true });
        grants.Add(new GrantType { Id = id++, Key = "AssignChores", NameKey = "Grant_AssignChores", DescriptionKey = "Grant_AssignChores_Desc", Category = GrantCategory.Chore, DefaultScope = GrantScopeLevel.Molecule, IsSystem = true });
        grants.Add(new GrantType { Id = id++, Key = "EditChoreTypes", NameKey = "Grant_EditChoreTypes", DescriptionKey = "Grant_EditChoreTypes_Desc", Category = GrantCategory.Chore, DefaultScope = GrantScopeLevel.Molecule, IsSystem = true });
        grants.Add(new GrantType { Id = id++, Key = "CreateChoreTypes", NameKey = "Grant_CreateChoreTypes", DescriptionKey = "Grant_CreateChoreTypes_Desc", Category = GrantCategory.Chore, DefaultScope = GrantScopeLevel.Molecule, IsSystem = true });

        // ============================================
        // VACATION GRANTS (Category.Vacation)
        // ============================================

        grants.Add(new GrantType { Id = id++, Key = "ViewVacations", NameKey = "Grant_ViewVacations", DescriptionKey = "Grant_ViewVacations_Desc", Category = GrantCategory.Vacation, DefaultScope = GrantScopeLevel.Company, IsSystem = true });
        grants.Add(new GrantType { Id = id++, Key = "RequestVacation", NameKey = "Grant_RequestVacation", DescriptionKey = "Grant_RequestVacation_Desc", Category = GrantCategory.Vacation, DefaultScope = GrantScopeLevel.Self, IsSystem = true });
        grants.Add(new GrantType { Id = id++, Key = "ApproveVacations", NameKey = "Grant_ApproveVacations", DescriptionKey = "Grant_ApproveVacations_Desc", Category = GrantCategory.Vacation, DefaultScope = GrantScopeLevel.Company, IsSystem = true });
        grants.Add(new GrantType { Id = id++, Key = "OverrideVacationLimits", NameKey = "Grant_OverrideVacationLimits", DescriptionKey = "Grant_OverrideVacationLimits_Desc", Category = GrantCategory.Vacation, DefaultScope = GrantScopeLevel.Company, IsSystem = true });
        grants.Add(new GrantType { Id = id++, Key = "ApproveExtendedLeave", NameKey = "Grant_ApproveExtendedLeave", DescriptionKey = "Grant_ApproveExtendedLeave_Desc", Category = GrantCategory.Vacation, DefaultScope = GrantScopeLevel.Company, IsSystem = true });

        // ============================================
        // SWAP GRANTS (Category.Swap)
        // ============================================

        grants.Add(new GrantType { Id = id++, Key = "RequestSwap", NameKey = "Grant_RequestSwap", DescriptionKey = "Grant_RequestSwap_Desc", Category = GrantCategory.Swap, DefaultScope = GrantScopeLevel.Self, IsSystem = true });
        grants.Add(new GrantType { Id = id++, Key = "ApproveSwaps", NameKey = "Grant_ApproveSwaps", DescriptionKey = "Grant_ApproveSwaps_Desc", Category = GrantCategory.Swap, DefaultScope = GrantScopeLevel.Company, IsSystem = true });
        grants.Add(new GrantType { Id = id++, Key = "InitiateSwap", NameKey = "Grant_InitiateSwap", DescriptionKey = "Grant_InitiateSwap_Desc", Category = GrantCategory.Swap, DefaultScope = GrantScopeLevel.Company, IsSystem = true });

        // ============================================
        // USER MANAGEMENT GRANTS (Category.UserManagement)
        // ============================================

        grants.Add(new GrantType { Id = id++, Key = "ViewUsers", NameKey = "Grant_ViewUsers", DescriptionKey = "Grant_ViewUsers_Desc", Category = GrantCategory.UserManagement, DefaultScope = GrantScopeLevel.Company, IsSystem = true });
        grants.Add(new GrantType { Id = id++, Key = "EditUsers", NameKey = "Grant_EditUsers", DescriptionKey = "Grant_EditUsers_Desc", Category = GrantCategory.UserManagement, DefaultScope = GrantScopeLevel.Company, IsSystem = true });
        grants.Add(new GrantType { Id = id++, Key = "CreateUsers", NameKey = "Grant_CreateUsers", DescriptionKey = "Grant_CreateUsers_Desc", Category = GrantCategory.UserManagement, DefaultScope = GrantScopeLevel.Company, IsSystem = true });
        grants.Add(new GrantType { Id = id++, Key = "DeactivateUsers", NameKey = "Grant_DeactivateUsers", DescriptionKey = "Grant_DeactivateUsers_Desc", Category = GrantCategory.UserManagement, DefaultScope = GrantScopeLevel.Company, IsSystem = true });
        grants.Add(new GrantType { Id = id++, Key = "ResetPasswords", NameKey = "Grant_ResetPasswords", DescriptionKey = "Grant_ResetPasswords_Desc", Category = GrantCategory.UserManagement, DefaultScope = GrantScopeLevel.Company, IsSystem = true });
        grants.Add(new GrantType { Id = id++, Key = "AssignJobTypes", NameKey = "Grant_AssignJobTypes", DescriptionKey = "Grant_AssignJobTypes_Desc", Category = GrantCategory.UserManagement, DefaultScope = GrantScopeLevel.Company, IsSystem = true });
        grants.Add(new GrantType { Id = id++, Key = "ViewAllUsers", NameKey = "Grant_ViewAllUsers", DescriptionKey = "Grant_ViewAllUsers_Desc", Category = GrantCategory.UserManagement, DefaultScope = GrantScopeLevel.Molecule, IsSystem = true });

        // ============================================
        // GRANT MANAGEMENT GRANTS (Category.GrantManagement)
        // ============================================

        grants.Add(new GrantType { Id = id++, Key = "ViewGrants", NameKey = "Grant_ViewGrants", DescriptionKey = "Grant_ViewGrants_Desc", Category = GrantCategory.GrantManagement, DefaultScope = GrantScopeLevel.Company, IsSystem = true });
        grants.Add(new GrantType { Id = id++, Key = "AssignGrants", NameKey = "Grant_AssignGrants", DescriptionKey = "Grant_AssignGrants_Desc", Category = GrantCategory.GrantManagement, DefaultScope = GrantScopeLevel.Company, IsSystem = true });
        grants.Add(new GrantType { Id = id++, Key = "RevokeGrants", NameKey = "Grant_RevokeGrants", DescriptionKey = "Grant_RevokeGrants_Desc", Category = GrantCategory.GrantManagement, DefaultScope = GrantScopeLevel.Company, IsSystem = true });
        grants.Add(new GrantType { Id = id++, Key = "AssignRoles", NameKey = "Grant_AssignRoles", DescriptionKey = "Grant_AssignRoles_Desc", Category = GrantCategory.GrantManagement, DefaultScope = GrantScopeLevel.Company, IsSystem = true });

        // ============================================
        // HIERARCHY GRANTS (Category.Hierarchy)
        // ============================================

        grants.Add(new GrantType { Id = id++, Key = "ViewHierarchy", NameKey = "Grant_ViewHierarchy", DescriptionKey = "Grant_ViewHierarchy_Desc", Category = GrantCategory.Hierarchy, DefaultScope = GrantScopeLevel.Company, IsSystem = true });
        grants.Add(new GrantType { Id = id++, Key = "EditCompany", NameKey = "Grant_EditCompany", DescriptionKey = "Grant_EditCompany_Desc", Category = GrantCategory.Hierarchy, DefaultScope = GrantScopeLevel.Company, IsSystem = true });
        grants.Add(new GrantType { Id = id++, Key = "EditMolecule", NameKey = "Grant_EditMolecule", DescriptionKey = "Grant_EditMolecule_Desc", Category = GrantCategory.Hierarchy, DefaultScope = GrantScopeLevel.Molecule, IsSystem = true });
        grants.Add(new GrantType { Id = id++, Key = "EditArea", NameKey = "Grant_EditArea", DescriptionKey = "Grant_EditArea_Desc", Category = GrantCategory.Hierarchy, DefaultScope = GrantScopeLevel.Area, IsSystem = true });
        grants.Add(new GrantType { Id = id++, Key = "CreateCompany", NameKey = "Grant_CreateCompany", DescriptionKey = "Grant_CreateCompany_Desc", Category = GrantCategory.Hierarchy, DefaultScope = GrantScopeLevel.Molecule, IsSystem = true });
        grants.Add(new GrantType { Id = id++, Key = "CreateMolecule", NameKey = "Grant_CreateMolecule", DescriptionKey = "Grant_CreateMolecule_Desc", Category = GrantCategory.Hierarchy, DefaultScope = GrantScopeLevel.Area, IsSystem = true });
        grants.Add(new GrantType { Id = id++, Key = "ManageShiftGroupings", NameKey = "Grant_ManageShiftGroupings", DescriptionKey = "Grant_ManageShiftGroupings_Desc", Category = GrantCategory.Hierarchy, DefaultScope = GrantScopeLevel.Molecule, IsSystem = true });
        grants.Add(new GrantType { Id = id++, Key = "ManageJobTypes", NameKey = "Grant_ManageJobTypes", DescriptionKey = "Grant_ManageJobTypes_Desc", Category = GrantCategory.Hierarchy, DefaultScope = GrantScopeLevel.Area, IsSystem = true });
        grants.Add(new GrantType { Id = id++, Key = "ManageDepartments", NameKey = "Grant_ManageDepartments", DescriptionKey = "Grant_ManageDepartments_Desc", Category = GrantCategory.Hierarchy, DefaultScope = GrantScopeLevel.Molecule, IsSystem = true });

        // ============================================
        // SETTINGS GRANTS (Category.Settings)
        // ============================================

        grants.Add(new GrantType { Id = id++, Key = "ViewSettings", NameKey = "Grant_ViewSettings", DescriptionKey = "Grant_ViewSettings_Desc", Category = GrantCategory.Settings, DefaultScope = GrantScopeLevel.Company, IsSystem = true });
        grants.Add(new GrantType { Id = id++, Key = "EditCompanySettings", NameKey = "Grant_EditCompanySettings", DescriptionKey = "Grant_EditCompanySettings_Desc", Category = GrantCategory.Settings, DefaultScope = GrantScopeLevel.Company, IsSystem = true });
        grants.Add(new GrantType { Id = id++, Key = "EditMoleculeSettings", NameKey = "Grant_EditMoleculeSettings", DescriptionKey = "Grant_EditMoleculeSettings_Desc", Category = GrantCategory.Settings, DefaultScope = GrantScopeLevel.Molecule, IsSystem = true });
        grants.Add(new GrantType { Id = id++, Key = "EditAreaSettings", NameKey = "Grant_EditAreaSettings", DescriptionKey = "Grant_EditAreaSettings_Desc", Category = GrantCategory.Settings, DefaultScope = GrantScopeLevel.Area, IsSystem = true });

        // ============================================
        // ANALYTICS GRANTS (Category.Analytics)
        // ============================================

        grants.Add(new GrantType { Id = id++, Key = "ViewAnalytics", NameKey = "Grant_ViewAnalytics", DescriptionKey = "Grant_ViewAnalytics_Desc", Category = GrantCategory.Analytics, DefaultScope = GrantScopeLevel.Company, IsSystem = true });
        grants.Add(new GrantType { Id = id++, Key = "ViewReports", NameKey = "Grant_ViewReports", DescriptionKey = "Grant_ViewReports_Desc", Category = GrantCategory.Analytics, DefaultScope = GrantScopeLevel.Company, IsSystem = true });
        grants.Add(new GrantType { Id = id++, Key = "ExportData", NameKey = "Grant_ExportData", DescriptionKey = "Grant_ExportData_Desc", Category = GrantCategory.Analytics, DefaultScope = GrantScopeLevel.Company, IsSystem = true });

        // ============================================
        // EMAIL GRANTS (Category.Email)
        // ============================================

        grants.Add(new GrantType { Id = id++, Key = "SendNotifications", NameKey = "Grant_SendNotifications", DescriptionKey = "Grant_SendNotifications_Desc", Category = GrantCategory.Email, DefaultScope = GrantScopeLevel.Company, IsSystem = true });
        grants.Add(new GrantType { Id = id++, Key = "ConfigureEmailSettings", NameKey = "Grant_ConfigureEmailSettings", DescriptionKey = "Grant_ConfigureEmailSettings_Desc", Category = GrantCategory.Email, DefaultScope = GrantScopeLevel.Company, IsSystem = true });

        // ============================================
        // SYSTEM GRANTS (Category.System)
        // ============================================

        grants.Add(new GrantType { Id = id++, Key = "AdminAccess", NameKey = "Grant_AdminAccess", DescriptionKey = "Grant_AdminAccess_Desc", Category = GrantCategory.System, DefaultScope = GrantScopeLevel.Project, IsSystem = true });
        grants.Add(new GrantType { Id = id++, Key = "SystemConfiguration", NameKey = "Grant_SystemConfiguration", DescriptionKey = "Grant_SystemConfiguration_Desc", Category = GrantCategory.System, DefaultScope = GrantScopeLevel.Project, IsSystem = true });
        grants.Add(new GrantType { Id = id++, Key = "ViewAuditLog", NameKey = "Grant_ViewAuditLog", DescriptionKey = "Grant_ViewAuditLog_Desc", Category = GrantCategory.System, DefaultScope = GrantScopeLevel.Company, IsSystem = true });
        grants.Add(new GrantType { Id = id++, Key = "ManageApiKeys", NameKey = "Grant_ManageApiKeys", DescriptionKey = "Grant_ManageApiKeys_Desc", Category = GrantCategory.System, DefaultScope = GrantScopeLevel.Company, IsSystem = true });

        // ============================================
        // SHIFT CALENDARS (Category.Shift) - View specific shift type calendars
        // ============================================

        grants.Add(new GrantType { Id = id++, Key = "ViewAlhutShiftCalendar", NameKey = "Grant_ViewAlhutShiftCalendar", DescriptionKey = "Grant_ViewAlhutShiftCalendar_Desc", Category = GrantCategory.Shift, DefaultScope = GrantScopeLevel.Company, IsSystem = true });
        grants.Add(new GrantType { Id = id++, Key = "ViewTextShiftCalendar", NameKey = "Grant_ViewTextShiftCalendar", DescriptionKey = "Grant_ViewTextShiftCalendar_Desc", Category = GrantCategory.Shift, DefaultScope = GrantScopeLevel.Company, IsSystem = true });
        grants.Add(new GrantType { Id = id++, Key = "ViewBRShiftCalendar", NameKey = "Grant_ViewBRShiftCalendar", DescriptionKey = "Grant_ViewBRShiftCalendar_Desc", Category = GrantCategory.Shift, DefaultScope = GrantScopeLevel.Molecule, IsSystem = true });
        grants.Add(new GrantType { Id = id++, Key = "ViewHakamShiftCalendar", NameKey = "Grant_ViewHakamShiftCalendar", DescriptionKey = "Grant_ViewHakamShiftCalendar_Desc", Category = GrantCategory.Shift, DefaultScope = GrantScopeLevel.Area, IsSystem = true });

        // ============================================
        // SHIFT ELIGIBILITY (Category.Shift) - Controls who can be assigned to shifts
        // ============================================

        grants.Add(new GrantType { Id = id++, Key = "CanBeAssignedAlhutShifts", NameKey = "Grant_CanBeAssignedAlhutShifts", DescriptionKey = "Grant_CanBeAssignedAlhutShifts_Desc", Category = GrantCategory.Shift, DefaultScope = GrantScopeLevel.Self, IsSystem = true });
        grants.Add(new GrantType { Id = id++, Key = "CanBeAssignedTextShifts", NameKey = "Grant_CanBeAssignedTextShifts", DescriptionKey = "Grant_CanBeAssignedTextShifts_Desc", Category = GrantCategory.Shift, DefaultScope = GrantScopeLevel.Self, IsSystem = true });
        grants.Add(new GrantType { Id = id++, Key = "CanBeAssignedBRShifts", NameKey = "Grant_CanBeAssignedBRShifts", DescriptionKey = "Grant_CanBeAssignedBRShifts_Desc", Category = GrantCategory.Shift, DefaultScope = GrantScopeLevel.Self, IsSystem = true });
        grants.Add(new GrantType { Id = id++, Key = "CanBeAssignedHakamShifts", NameKey = "Grant_CanBeAssignedHakamShifts", DescriptionKey = "Grant_CanBeAssignedHakamShifts_Desc", Category = GrantCategory.Shift, DefaultScope = GrantScopeLevel.Self, IsSystem = true });

        // ============================================
        // BLUEPRINT/PROGRAM GRANTS BY TYPE (Category.Shift)
        // ============================================

        grants.Add(new GrantType { Id = id++, Key = "ManageAlhutBlueprints", NameKey = "Grant_ManageAlhutBlueprints", DescriptionKey = "Grant_ManageAlhutBlueprints_Desc", Category = GrantCategory.Shift, DefaultScope = GrantScopeLevel.Molecule, IsSystem = true });
        grants.Add(new GrantType { Id = id++, Key = "ManageAlhutPrograms", NameKey = "Grant_ManageAlhutPrograms", DescriptionKey = "Grant_ManageAlhutPrograms_Desc", Category = GrantCategory.Shift, DefaultScope = GrantScopeLevel.Molecule, IsSystem = true });
        grants.Add(new GrantType { Id = id++, Key = "ManageTextBlueprints", NameKey = "Grant_ManageTextBlueprints", DescriptionKey = "Grant_ManageTextBlueprints_Desc", Category = GrantCategory.Shift, DefaultScope = GrantScopeLevel.Molecule, IsSystem = true });
        grants.Add(new GrantType { Id = id++, Key = "ManageTextPrograms", NameKey = "Grant_ManageTextPrograms", DescriptionKey = "Grant_ManageTextPrograms_Desc", Category = GrantCategory.Shift, DefaultScope = GrantScopeLevel.Molecule, IsSystem = true });
        grants.Add(new GrantType { Id = id++, Key = "ManageBRBlueprints", NameKey = "Grant_ManageBRBlueprints", DescriptionKey = "Grant_ManageBRBlueprints_Desc", Category = GrantCategory.Shift, DefaultScope = GrantScopeLevel.Molecule, IsSystem = true });
        grants.Add(new GrantType { Id = id++, Key = "ManageBRPrograms", NameKey = "Grant_ManageBRPrograms", DescriptionKey = "Grant_ManageBRPrograms_Desc", Category = GrantCategory.Shift, DefaultScope = GrantScopeLevel.Molecule, IsSystem = true });
        grants.Add(new GrantType { Id = id++, Key = "ManageHakamBlueprints", NameKey = "Grant_ManageHakamBlueprints", DescriptionKey = "Grant_ManageHakamBlueprints_Desc", Category = GrantCategory.Shift, DefaultScope = GrantScopeLevel.Area, IsSystem = true });
        grants.Add(new GrantType { Id = id++, Key = "ManageHakamPrograms", NameKey = "Grant_ManageHakamPrograms", DescriptionKey = "Grant_ManageHakamPrograms_Desc", Category = GrantCategory.Shift, DefaultScope = GrantScopeLevel.Area, IsSystem = true });

        // ============================================
        // TECH CALENDARS (Category.Shift) - Department-specific tech shift calendars
        // ============================================

        grants.Add(new GrantType { Id = id++, Key = "ViewHanavaCalendar", NameKey = "Grant_ViewHanavaCalendar", DescriptionKey = "Grant_ViewHanavaCalendar_Desc", Category = GrantCategory.Shift, DefaultScope = GrantScopeLevel.Department, IsSystem = true });
        grants.Add(new GrantType { Id = id++, Key = "ViewDeltaCalendar", NameKey = "Grant_ViewDeltaCalendar", DescriptionKey = "Grant_ViewDeltaCalendar_Desc", Category = GrantCategory.Shift, DefaultScope = GrantScopeLevel.Department, IsSystem = true });
        grants.Add(new GrantType { Id = id++, Key = "ViewYekevCalendar", NameKey = "Grant_ViewYekevCalendar", DescriptionKey = "Grant_ViewYekevCalendar_Desc", Category = GrantCategory.Shift, DefaultScope = GrantScopeLevel.Department, IsSystem = true });
        grants.Add(new GrantType { Id = id++, Key = "ViewMoviltechCalendar", NameKey = "Grant_ViewMoviltechCalendar", DescriptionKey = "Grant_ViewMoviltechCalendar_Desc", Category = GrantCategory.Shift, DefaultScope = GrantScopeLevel.Department, IsSystem = true });

        // ============================================
        // TECH ASSIGNMENT (Category.Shift) - Department-specific tech shift assignment
        // ============================================

        grants.Add(new GrantType { Id = id++, Key = "AssignHanavaShifts", NameKey = "Grant_AssignHanavaShifts", DescriptionKey = "Grant_AssignHanavaShifts_Desc", Category = GrantCategory.Shift, DefaultScope = GrantScopeLevel.Department, IsSystem = true });
        grants.Add(new GrantType { Id = id++, Key = "AssignDeltaShifts", NameKey = "Grant_AssignDeltaShifts", DescriptionKey = "Grant_AssignDeltaShifts_Desc", Category = GrantCategory.Shift, DefaultScope = GrantScopeLevel.Department, IsSystem = true });
        grants.Add(new GrantType { Id = id++, Key = "AssignYekevShifts", NameKey = "Grant_AssignYekevShifts", DescriptionKey = "Grant_AssignYekevShifts_Desc", Category = GrantCategory.Shift, DefaultScope = GrantScopeLevel.Department, IsSystem = true });
        grants.Add(new GrantType { Id = id++, Key = "AssignMoviltechShifts", NameKey = "Grant_AssignMoviltechShifts", DescriptionKey = "Grant_AssignMoviltechShifts_Desc", Category = GrantCategory.Shift, DefaultScope = GrantScopeLevel.Department, IsSystem = true });

        // ============================================
        // TECH BLUEPRINTS/PROGRAMS (Category.Shift) - Department-specific management
        // ============================================

        grants.Add(new GrantType { Id = id++, Key = "ManageHanavaBlueprints", NameKey = "Grant_ManageHanavaBlueprints", DescriptionKey = "Grant_ManageHanavaBlueprints_Desc", Category = GrantCategory.Shift, DefaultScope = GrantScopeLevel.Department, IsSystem = true });
        grants.Add(new GrantType { Id = id++, Key = "ManageHanavaPrograms", NameKey = "Grant_ManageHanavaPrograms", DescriptionKey = "Grant_ManageHanavaPrograms_Desc", Category = GrantCategory.Shift, DefaultScope = GrantScopeLevel.Department, IsSystem = true });
        grants.Add(new GrantType { Id = id++, Key = "ManageDeltaBlueprints", NameKey = "Grant_ManageDeltaBlueprints", DescriptionKey = "Grant_ManageDeltaBlueprints_Desc", Category = GrantCategory.Shift, DefaultScope = GrantScopeLevel.Department, IsSystem = true });
        grants.Add(new GrantType { Id = id++, Key = "ManageDeltaPrograms", NameKey = "Grant_ManageDeltaPrograms", DescriptionKey = "Grant_ManageDeltaPrograms_Desc", Category = GrantCategory.Shift, DefaultScope = GrantScopeLevel.Department, IsSystem = true });
        grants.Add(new GrantType { Id = id++, Key = "ManageYekevBlueprints", NameKey = "Grant_ManageYekevBlueprints", DescriptionKey = "Grant_ManageYekevBlueprints_Desc", Category = GrantCategory.Shift, DefaultScope = GrantScopeLevel.Department, IsSystem = true });
        grants.Add(new GrantType { Id = id++, Key = "ManageYekevPrograms", NameKey = "Grant_ManageYekevPrograms", DescriptionKey = "Grant_ManageYekevPrograms_Desc", Category = GrantCategory.Shift, DefaultScope = GrantScopeLevel.Department, IsSystem = true });
        grants.Add(new GrantType { Id = id++, Key = "ManageMoviltechBlueprints", NameKey = "Grant_ManageMoviltechBlueprints", DescriptionKey = "Grant_ManageMoviltechBlueprints_Desc", Category = GrantCategory.Shift, DefaultScope = GrantScopeLevel.Department, IsSystem = true });
        grants.Add(new GrantType { Id = id++, Key = "ManageMoviltechPrograms", NameKey = "Grant_ManageMoviltechPrograms", DescriptionKey = "Grant_ManageMoviltechPrograms_Desc", Category = GrantCategory.Shift, DefaultScope = GrantScopeLevel.Department, IsSystem = true });

        // ============================================
        // TECH ELIGIBILITY (Category.Shift) - Controls who can be assigned tech shifts
        // ============================================

        grants.Add(new GrantType { Id = id++, Key = "CanBeAssignedHanava", NameKey = "Grant_CanBeAssignedHanava", DescriptionKey = "Grant_CanBeAssignedHanava_Desc", Category = GrantCategory.Shift, DefaultScope = GrantScopeLevel.Self, IsSystem = true });
        grants.Add(new GrantType { Id = id++, Key = "CanBeAssignedDelta", NameKey = "Grant_CanBeAssignedDelta", DescriptionKey = "Grant_CanBeAssignedDelta_Desc", Category = GrantCategory.Shift, DefaultScope = GrantScopeLevel.Self, IsSystem = true });
        grants.Add(new GrantType { Id = id++, Key = "CanBeAssignedYekev", NameKey = "Grant_CanBeAssignedYekev", DescriptionKey = "Grant_CanBeAssignedYekev_Desc", Category = GrantCategory.Shift, DefaultScope = GrantScopeLevel.Self, IsSystem = true });
        grants.Add(new GrantType { Id = id++, Key = "CanBeAssignedMoviltech", NameKey = "Grant_CanBeAssignedMoviltech", DescriptionKey = "Grant_CanBeAssignedMoviltech_Desc", Category = GrantCategory.Shift, DefaultScope = GrantScopeLevel.Self, IsSystem = true });

        // ============================================
        // HELPER MOLECULE GRANTS - Shiklut (Category.Chore)
        // ============================================

        grants.Add(new GrantType { Id = id++, Key = "ViewShiklutCalendar", NameKey = "Grant_ViewShiklutCalendar", DescriptionKey = "Grant_ViewShiklutCalendar_Desc", Category = GrantCategory.Chore, DefaultScope = GrantScopeLevel.Molecule, IsSystem = true });
        grants.Add(new GrantType { Id = id++, Key = "AssignShiklutChores", NameKey = "Grant_AssignShiklutChores", DescriptionKey = "Grant_AssignShiklutChores_Desc", Category = GrantCategory.Chore, DefaultScope = GrantScopeLevel.Molecule, IsSystem = true });
        grants.Add(new GrantType { Id = id++, Key = "ManageShiklutBlueprints", NameKey = "Grant_ManageShiklutBlueprints", DescriptionKey = "Grant_ManageShiklutBlueprints_Desc", Category = GrantCategory.Chore, DefaultScope = GrantScopeLevel.Molecule, IsSystem = true });
        grants.Add(new GrantType { Id = id++, Key = "ManageShiklutPrograms", NameKey = "Grant_ManageShiklutPrograms", DescriptionKey = "Grant_ManageShiklutPrograms_Desc", Category = GrantCategory.Chore, DefaultScope = GrantScopeLevel.Molecule, IsSystem = true });
        grants.Add(new GrantType { Id = id++, Key = "CanBeAssignedShiklut", NameKey = "Grant_CanBeAssignedShiklut", DescriptionKey = "Grant_CanBeAssignedShiklut_Desc", Category = GrantCategory.Chore, DefaultScope = GrantScopeLevel.Self, IsSystem = true });

        // ============================================
        // HELPER MOLECULE GRANTS - NOC (Category.Chore)
        // ============================================

        grants.Add(new GrantType { Id = id++, Key = "ViewNOCCalendar", NameKey = "Grant_ViewNOCCalendar", DescriptionKey = "Grant_ViewNOCCalendar_Desc", Category = GrantCategory.Chore, DefaultScope = GrantScopeLevel.Molecule, IsSystem = true });
        grants.Add(new GrantType { Id = id++, Key = "AssignNOCChores", NameKey = "Grant_AssignNOCChores", DescriptionKey = "Grant_AssignNOCChores_Desc", Category = GrantCategory.Chore, DefaultScope = GrantScopeLevel.Molecule, IsSystem = true });
        grants.Add(new GrantType { Id = id++, Key = "ManageNOCBlueprints", NameKey = "Grant_ManageNOCBlueprints", DescriptionKey = "Grant_ManageNOCBlueprints_Desc", Category = GrantCategory.Chore, DefaultScope = GrantScopeLevel.Molecule, IsSystem = true });
        grants.Add(new GrantType { Id = id++, Key = "ManageNOCPrograms", NameKey = "Grant_ManageNOCPrograms", DescriptionKey = "Grant_ManageNOCPrograms_Desc", Category = GrantCategory.Chore, DefaultScope = GrantScopeLevel.Molecule, IsSystem = true });
        grants.Add(new GrantType { Id = id++, Key = "CanBeAssignedNOC", NameKey = "Grant_CanBeAssignedNOC", DescriptionKey = "Grant_CanBeAssignedNOC_Desc", Category = GrantCategory.Chore, DefaultScope = GrantScopeLevel.Self, IsSystem = true });

        // ============================================
        // KATZIN DUTY GRANTS (Category.Duty)
        // ============================================

        grants.Add(new GrantType { Id = id++, Key = "ManageKatzinBlueprints", NameKey = "Grant_ManageKatzinBlueprints", DescriptionKey = "Grant_ManageKatzinBlueprints_Desc", Category = GrantCategory.Duty, DefaultScope = GrantScopeLevel.Area, IsSystem = true });
        grants.Add(new GrantType { Id = id++, Key = "ManageKatzinPrograms", NameKey = "Grant_ManageKatzinPrograms", DescriptionKey = "Grant_ManageKatzinPrograms_Desc", Category = GrantCategory.Duty, DefaultScope = GrantScopeLevel.Area, IsSystem = true });

        // ============================================
        // CALENDAR GRANTS
        // ============================================

        grants.Add(new GrantType { Id = id++, Key = "ManageShiftCapacity", NameKey = "Grant_ManageShiftCapacity", DescriptionKey = "Grant_ManageShiftCapacity_Desc", Category = GrantCategory.Shift, DefaultScope = GrantScopeLevel.Molecule, IsSystem = true });
        grants.Add(new GrantType { Id = id++, Key = "WriteOverviewNotes", NameKey = "Grant_WriteOverviewNotes", DescriptionKey = "Grant_WriteOverviewNotes_Desc", Category = GrantCategory.Shift, DefaultScope = GrantScopeLevel.Company, IsSystem = true });
        grants.Add(new GrantType { Id = id++, Key = "ManageOnDutyTypes", NameKey = "Grant_ManageOnDutyTypes", DescriptionKey = "Grant_ManageOnDutyTypes_Desc", Category = GrantCategory.Duty, DefaultScope = GrantScopeLevel.Area, IsSystem = true });

        // ============================================
        // NAVIGATION GRANTS (Category.System) - Controls navigation visibility
        // ============================================

        grants.Add(new GrantType { Id = id++, Key = "AccessAdminNavigation", NameKey = "Grant_AccessAdminNavigation", DescriptionKey = "Grant_AccessAdminNavigation_Desc", Category = GrantCategory.System, DefaultScope = GrantScopeLevel.Company, IsSystem = true });
        grants.Add(new GrantType { Id = id++, Key = "DirectorHubAccess", NameKey = "Grant_DirectorHubAccess", DescriptionKey = "Grant_DirectorHubAccess_Desc", Category = GrantCategory.System, DefaultScope = GrantScopeLevel.Area, IsSystem = true });
        grants.Add(new GrantType { Id = id++, Key = "ManagerHomeAccess", NameKey = "Grant_ManagerHomeAccess", DescriptionKey = "Grant_ManagerHomeAccess_Desc", Category = GrantCategory.System, DefaultScope = GrantScopeLevel.Company, IsSystem = true });
        grants.Add(new GrantType { Id = id++, Key = "ViewCompanyCalendar", NameKey = "Grant_ViewCompanyCalendar", DescriptionKey = "Grant_ViewCompanyCalendar_Desc", Category = GrantCategory.Shift, DefaultScope = GrantScopeLevel.Company, IsSystem = true });

        // ============================================
        // JOIN REQUEST GRANTS (Category.UserManagement)
        // ============================================

        grants.Add(new GrantType { Id = id++, Key = "ManageJoinRequests", NameKey = "Grant_ManageJoinRequests", DescriptionKey = "Grant_ManageJoinRequests_Desc", Category = GrantCategory.UserManagement, DefaultScope = GrantScopeLevel.Company, IsSystem = true });
        grants.Add(new GrantType { Id = id++, Key = "ViewCompanyUsers", NameKey = "Grant_ViewCompanyUsers", DescriptionKey = "Grant_ViewCompanyUsers_Desc", Category = GrantCategory.UserManagement, DefaultScope = GrantScopeLevel.Company, IsSystem = true });
        grants.Add(new GrantType { Id = id++, Key = "EditCompanyUsers", NameKey = "Grant_EditCompanyUsers", DescriptionKey = "Grant_EditCompanyUsers_Desc", Category = GrantCategory.UserManagement, DefaultScope = GrantScopeLevel.Company, IsSystem = true });

        // ============================================
        // DYNAMIC ROLE TEMPLATE GRANTS (Category.System / Category.Duty)
        // ============================================

        grants.Add(new GrantType { Id = id++, Key = "ManageAnnouncements", NameKey = "Grant_ManageAnnouncements", DescriptionKey = "Grant_ManageAnnouncements_Desc", Category = GrantCategory.System, DefaultScope = GrantScopeLevel.Company, IsSystem = true });
        grants.Add(new GrantType { Id = id++, Key = "ViewSystemAlerts", NameKey = "Grant_ViewSystemAlerts", DescriptionKey = "Grant_ViewSystemAlerts_Desc", Category = GrantCategory.System, DefaultScope = GrantScopeLevel.Company, IsSystem = true });
        grants.Add(new GrantType { Id = id++, Key = "ViewAllAreas", NameKey = "Grant_ViewAllAreas", DescriptionKey = "Grant_ViewAllAreas_Desc", Category = GrantCategory.System, DefaultScope = GrantScopeLevel.Area, IsSystem = true });
        grants.Add(new GrantType { Id = id++, Key = "ManageOnDuty", NameKey = "Grant_ManageOnDuty", DescriptionKey = "Grant_ManageOnDuty_Desc", Category = GrantCategory.Duty, DefaultScope = GrantScopeLevel.Area, IsSystem = true });

        // ============================================
        // HIERARCHY REORDER (Section 8 — was missing, needed by Api/Hierarchy/Reorder endpoint)
        // ============================================
        grants.Add(new GrantType { Id = id++, Key = "ReorderHierarchy", NameKey = "Grant_ReorderHierarchy", DescriptionKey = "Grant_ReorderHierarchy_Desc", Category = GrantCategory.Hierarchy, DefaultScope = GrantScopeLevel.Project, IsSystem = true });

        // ============================================
        // HIERARCHY MANAGE (write access: add/rename/delete companies and departments)
        // ============================================
        grants.Add(new GrantType { Id = id++, Key = "ManageHierarchy", NameKey = "Grant_ManageHierarchy", DescriptionKey = "Grant_ManageHierarchy_Desc", Category = GrantCategory.Hierarchy, DefaultScope = GrantScopeLevel.Molecule, IsSystem = true });

        // ============================================
        // HOME ROTATION (SP4 — Shikma pilot)
        // ============================================
        grants.Add(new GrantType { Id = id++, Key = "ManageHomeTypes", NameKey = "Grant_ManageHomeTypes", DescriptionKey = "Grant_ManageHomeTypes_Desc", Category = GrantCategory.Shift, DefaultScope = GrantScopeLevel.Molecule, IsSystem = true });

        // ============================================
        // STORE HOURS & QUICK INFO WIDGET (Category.Hierarchy — area-scoped organization admin)
        // ============================================

        grants.Add(new GrantType { Id = id++, Key = "ManageStores", NameKey = "Grant_ManageStores", DescriptionKey = "Grant_ManageStores_Desc", Category = GrantCategory.Hierarchy, DefaultScope = GrantScopeLevel.Area, IsSystem = true });

        // ============================================
        // ON-CALL WIDGET GRANTS (Category.Duty) - Controls widget visibility
        // ============================================

        grants.Add(new GrantType { Id = id++, Key = "ViewHakamOnCall", NameKey = "Grant_ViewHakamOnCall", DescriptionKey = "Grant_ViewHakamOnCall_Desc", Category = GrantCategory.Duty, DefaultScope = GrantScopeLevel.Area, IsSystem = true });
        grants.Add(new GrantType { Id = id++, Key = "ViewCompanyOnCall", NameKey = "Grant_ViewCompanyOnCall", DescriptionKey = "Grant_ViewCompanyOnCall_Desc", Category = GrantCategory.Duty, DefaultScope = GrantScopeLevel.Company, IsSystem = true });

        // ============================================
        // SCOPE SWITCHER GRANTS - Molecule and Area level scope access
        // Used by ScopeSwitcherViewComponent and /Api/ScopeSwitcher endpoint.
        // Dynamic pattern: View{CalendarType}Molecule, View{CalendarType}Area
        // ============================================

        grants.Add(new GrantType { Id = id++, Key = "ViewShiftsMolecule", NameKey = "Grant_ViewShiftsMolecule", DescriptionKey = "Grant_ViewShiftsMolecule_Desc", Category = GrantCategory.Shift, DefaultScope = GrantScopeLevel.Molecule, IsSystem = true });
        grants.Add(new GrantType { Id = id++, Key = "ViewShiftsArea", NameKey = "Grant_ViewShiftsArea", DescriptionKey = "Grant_ViewShiftsArea_Desc", Category = GrantCategory.Shift, DefaultScope = GrantScopeLevel.Area, IsSystem = true });

        // ============================================
        // Collaborative On-Call Editing (2026-04-15)
        // Grants any hakam-eligible user the right to assign/unassign users and add text notes
        // on the OnCall calendar (/Calendar/OnCall). POST handlers still perform IDOR scope check.
        // ============================================
        grants.Add(new GrantType { Id = id++, Key = "EditOnCallCalendar", NameKey = "Grant_EditOnCallCalendar", DescriptionKey = "Grant_EditOnCallCalendar_Desc", Category = GrantCategory.Duty, DefaultScope = GrantScopeLevel.Area, IsSystem = true });

        // ============================================
        // Per-Area Calendar Palette override (2026-04-15)
        // Grants Area Admins the right to customize the shift/chore/on-duty/vacation colors per area.
        // ============================================
        grants.Add(new GrantType { Id = id++, Key = "EditAreaCalendarPalette", NameKey = "Grant_EditAreaCalendarPalette", DescriptionKey = "Grant_EditAreaCalendarPalette_Desc", Category = GrantCategory.Shift, DefaultScope = GrantScopeLevel.Area, IsSystem = true });

        return grants;
    }
}
