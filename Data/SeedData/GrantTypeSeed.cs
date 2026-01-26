using ShiftManager.Models;
using ShiftManager.Models.Support;

namespace ShiftManager.Data.SeedData;

public static class GrantTypeSeed
{
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

        return grants;
    }
}
