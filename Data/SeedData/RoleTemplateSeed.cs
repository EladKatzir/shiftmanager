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

        // Employee (Role 1) - Basic view grants
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 1, GrantTypeId = 1, CanOwn = true, CanGive = false, ScopeMode = GrantScopeMode.SameAsRole }); // ViewShifts
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 1, GrantTypeId = 17, CanOwn = true, CanGive = false, ScopeMode = GrantScopeMode.SameAsRole }); // ViewChores
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 1, GrantTypeId = 21, CanOwn = true, CanGive = false, ScopeMode = GrantScopeMode.SameAsRole }); // ViewVacations
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 1, GrantTypeId = 22, CanOwn = true, CanGive = false, ScopeMode = GrantScopeMode.SameAsRole }); // RequestVacation
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 1, GrantTypeId = 26, CanOwn = true, CanGive = false, ScopeMode = GrantScopeMode.SameAsRole }); // RequestSwap

        // BR Director (Role 2) - Company-wide BR management
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 2, GrantTypeId = 5, CanOwn = true, CanGive = true, ScopeMode = GrantScopeMode.ExpandToMolecule }); // AssignBRShifts
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 2, GrantTypeId = 2, CanOwn = true, CanGive = false, ScopeMode = GrantScopeMode.ExpandToMolecule }); // ViewAllShifts
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 2, GrantTypeId = 23, CanOwn = true, CanGive = false, ScopeMode = GrantScopeMode.SameAsRole }); // ApproveVacations
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 2, GrantTypeId = 27, CanOwn = true, CanGive = false, ScopeMode = GrantScopeMode.SameAsRole }); // ApproveSwaps
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 2, GrantTypeId = 29, CanOwn = true, CanGive = false, ScopeMode = GrantScopeMode.SameAsRole }); // ViewUsers
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 2, GrantTypeId = 30, CanOwn = true, CanGive = false, ScopeMode = GrantScopeMode.SameAsRole }); // EditUsers

        // Alhut Lead (Role 3) - Company+JobType Alhut shift assignment
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 3, GrantTypeId = 3, CanOwn = true, CanGive = false, ScopeMode = GrantScopeMode.SameAsRole }); // AssignAlhutShifts
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 3, GrantTypeId = 1, CanOwn = true, CanGive = false, ScopeMode = GrantScopeMode.SameAsRole }); // ViewShifts
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 3, GrantTypeId = 29, CanOwn = true, CanGive = false, ScopeMode = GrantScopeMode.SameAsRole }); // ViewUsers

        // Text Lead (Role 4) - Company+JobType Text shift assignment
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 4, GrantTypeId = 4, CanOwn = true, CanGive = false, ScopeMode = GrantScopeMode.SameAsRole }); // AssignTextShifts
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 4, GrantTypeId = 1, CanOwn = true, CanGive = false, ScopeMode = GrantScopeMode.SameAsRole }); // ViewShifts
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 4, GrantTypeId = 29, CanOwn = true, CanGive = false, ScopeMode = GrantScopeMode.SameAsRole }); // ViewUsers

        // Alhut Director (Role 5) - Molecule-wide Alhut management
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 5, GrantTypeId = 3, CanOwn = true, CanGive = true, ScopeMode = GrantScopeMode.SameAsRole }); // AssignAlhutShifts
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 5, GrantTypeId = 2, CanOwn = true, CanGive = false, ScopeMode = GrantScopeMode.SameAsRole }); // ViewAllShifts
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 5, GrantTypeId = 7, CanOwn = true, CanGive = false, ScopeMode = GrantScopeMode.SameAsRole }); // EditShiftPrograms
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 5, GrantTypeId = 35, CanOwn = true, CanGive = false, ScopeMode = GrantScopeMode.SameAsRole }); // ViewAllUsers
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 5, GrantTypeId = 40, CanOwn = true, CanGive = false, ScopeMode = GrantScopeMode.SameAsRole }); // AssignRoles (for Leads)

        // Text Director (Role 6) - Molecule-wide Text management
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 6, GrantTypeId = 4, CanOwn = true, CanGive = true, ScopeMode = GrantScopeMode.SameAsRole }); // AssignTextShifts
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 6, GrantTypeId = 2, CanOwn = true, CanGive = false, ScopeMode = GrantScopeMode.SameAsRole }); // ViewAllShifts
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 6, GrantTypeId = 7, CanOwn = true, CanGive = false, ScopeMode = GrantScopeMode.SameAsRole }); // EditShiftPrograms
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 6, GrantTypeId = 35, CanOwn = true, CanGive = false, ScopeMode = GrantScopeMode.SameAsRole }); // ViewAllUsers
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 6, GrantTypeId = 40, CanOwn = true, CanGive = false, ScopeMode = GrantScopeMode.SameAsRole }); // AssignRoles (for Leads)

        // Molecule Admin (Role 7) - Full molecule administration
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 7, GrantTypeId = 2, CanOwn = true, CanGive = true, ScopeMode = GrantScopeMode.SameAsRole }); // ViewAllShifts
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 7, GrantTypeId = 18, CanOwn = true, CanGive = true, ScopeMode = GrantScopeMode.SameAsRole }); // AssignChores
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 7, GrantTypeId = 19, CanOwn = true, CanGive = false, ScopeMode = GrantScopeMode.SameAsRole }); // EditChoreTypes
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 7, GrantTypeId = 35, CanOwn = true, CanGive = false, ScopeMode = GrantScopeMode.SameAsRole }); // ViewAllUsers
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 7, GrantTypeId = 30, CanOwn = true, CanGive = false, ScopeMode = GrantScopeMode.SameAsRole }); // EditUsers
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 7, GrantTypeId = 40, CanOwn = true, CanGive = true, ScopeMode = GrantScopeMode.SameAsRole }); // AssignRoles
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 7, GrantTypeId = 43, CanOwn = true, CanGive = false, ScopeMode = GrantScopeMode.SameAsRole }); // EditMolecule
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 7, GrantTypeId = 47, CanOwn = true, CanGive = false, ScopeMode = GrantScopeMode.SameAsRole }); // ManageShiftGroupings
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 7, GrantTypeId = 51, CanOwn = true, CanGive = false, ScopeMode = GrantScopeMode.SameAsRole }); // EditMoleculeSettings

        // Assigner (Role 8) - Chore assignment only (NOT shift assignment)
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 8, GrantTypeId = 17, CanOwn = true, CanGive = false, ScopeMode = GrantScopeMode.SameAsRole }); // AssignChores (ID 17)
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 8, GrantTypeId = 16, CanOwn = true, CanGive = false, ScopeMode = GrantScopeMode.SameAsRole }); // ViewChores (ID 16)

        // Department Lead (Role 9) - Tech department management
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 9, GrantTypeId = 6, CanOwn = true, CanGive = false, ScopeMode = GrantScopeMode.SameAsRole }); // AssignTechShifts
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 9, GrantTypeId = 1, CanOwn = true, CanGive = false, ScopeMode = GrantScopeMode.SameAsRole }); // ViewShifts
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 9, GrantTypeId = 29, CanOwn = true, CanGive = false, ScopeMode = GrantScopeMode.SameAsRole }); // ViewUsers
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 9, GrantTypeId = 30, CanOwn = true, CanGive = false, ScopeMode = GrantScopeMode.SameAsRole }); // EditUsers

        // Area Admin (Role 10) - Area-wide administration
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 10, GrantTypeId = 13, CanOwn = true, CanGive = true, ScopeMode = GrantScopeMode.SameAsRole }); // ViewDuties
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 10, GrantTypeId = 14, CanOwn = true, CanGive = true, ScopeMode = GrantScopeMode.SameAsRole }); // AssignHakamDuties
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 10, GrantTypeId = 15, CanOwn = true, CanGive = true, ScopeMode = GrantScopeMode.SameAsRole }); // AssignKatzinDuties
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 10, GrantTypeId = 44, CanOwn = true, CanGive = false, ScopeMode = GrantScopeMode.SameAsRole }); // EditArea
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 10, GrantTypeId = 45, CanOwn = true, CanGive = false, ScopeMode = GrantScopeMode.SameAsRole }); // CreateCompany
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 10, GrantTypeId = 46, CanOwn = true, CanGive = false, ScopeMode = GrantScopeMode.SameAsRole }); // CreateMolecule
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 10, GrantTypeId = 48, CanOwn = true, CanGive = false, ScopeMode = GrantScopeMode.SameAsRole }); // ManageJobTypes
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 10, GrantTypeId = 52, CanOwn = true, CanGive = false, ScopeMode = GrantScopeMode.SameAsRole }); // EditAreaSettings

        // Owner (Role 11) - Full system access
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 11, GrantTypeId = 57, CanOwn = true, CanGive = true, ScopeMode = GrantScopeMode.SameAsRole }); // AdminAccess
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 11, GrantTypeId = 58, CanOwn = true, CanGive = false, ScopeMode = GrantScopeMode.SameAsRole }); // SystemConfiguration
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 11, GrantTypeId = 111, CanOwn = true, CanGive = true, ScopeMode = GrantScopeMode.SameAsRole }); // AccessAdminNavigation
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 11, GrantTypeId = 112, CanOwn = true, CanGive = true, ScopeMode = GrantScopeMode.SameAsRole }); // DirectorHubAccess

        // ============================================
        // NAVIGATION GRANTS - Added to roles that need admin navigation
        // AccessAdminNavigation (111), DirectorHubAccess (112), ManagerHomeAccess (113), ViewCompanyCalendar (114)
        // ============================================

        // BRDirector (Role 2) - Admin navigation + Manager home
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 2, GrantTypeId = 111, CanOwn = true, CanGive = false, ScopeMode = GrantScopeMode.SameAsRole }); // AccessAdminNavigation
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 2, GrantTypeId = 113, CanOwn = true, CanGive = false, ScopeMode = GrantScopeMode.SameAsRole }); // ManagerHomeAccess

        // AlhutLead (Role 3) - Admin navigation + Manager home
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 3, GrantTypeId = 111, CanOwn = true, CanGive = false, ScopeMode = GrantScopeMode.SameAsRole }); // AccessAdminNavigation
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 3, GrantTypeId = 113, CanOwn = true, CanGive = false, ScopeMode = GrantScopeMode.SameAsRole }); // ManagerHomeAccess

        // TextLead (Role 4) - Admin navigation + Manager home
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 4, GrantTypeId = 111, CanOwn = true, CanGive = false, ScopeMode = GrantScopeMode.SameAsRole }); // AccessAdminNavigation
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 4, GrantTypeId = 113, CanOwn = true, CanGive = false, ScopeMode = GrantScopeMode.SameAsRole }); // ManagerHomeAccess

        // AlhutDirector (Role 5) - Admin navigation + Director Hub + Manager home
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 5, GrantTypeId = 111, CanOwn = true, CanGive = false, ScopeMode = GrantScopeMode.SameAsRole }); // AccessAdminNavigation
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 5, GrantTypeId = 112, CanOwn = true, CanGive = false, ScopeMode = GrantScopeMode.SameAsRole }); // DirectorHubAccess
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 5, GrantTypeId = 113, CanOwn = true, CanGive = false, ScopeMode = GrantScopeMode.SameAsRole }); // ManagerHomeAccess

        // TextDirector (Role 6) - Admin navigation + Director Hub + Manager home
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 6, GrantTypeId = 111, CanOwn = true, CanGive = false, ScopeMode = GrantScopeMode.SameAsRole }); // AccessAdminNavigation
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 6, GrantTypeId = 112, CanOwn = true, CanGive = false, ScopeMode = GrantScopeMode.SameAsRole }); // DirectorHubAccess
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 6, GrantTypeId = 113, CanOwn = true, CanGive = false, ScopeMode = GrantScopeMode.SameAsRole }); // ManagerHomeAccess

        // MoleculeAdmin (Role 7) - Admin navigation + Manager home
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 7, GrantTypeId = 111, CanOwn = true, CanGive = false, ScopeMode = GrantScopeMode.SameAsRole }); // AccessAdminNavigation
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 7, GrantTypeId = 113, CanOwn = true, CanGive = false, ScopeMode = GrantScopeMode.SameAsRole }); // ManagerHomeAccess

        // Assigner (Role 8) - Chore-only role, uses employee navigation with chore access

        // DepartmentLead (Role 9) - Admin navigation + Manager home
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 9, GrantTypeId = 111, CanOwn = true, CanGive = false, ScopeMode = GrantScopeMode.SameAsRole }); // AccessAdminNavigation
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 9, GrantTypeId = 113, CanOwn = true, CanGive = false, ScopeMode = GrantScopeMode.SameAsRole }); // ManagerHomeAccess

        // AreaAdmin (Role 10) - Admin navigation + Director Hub access
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 10, GrantTypeId = 111, CanOwn = true, CanGive = true, ScopeMode = GrantScopeMode.SameAsRole }); // AccessAdminNavigation
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 10, GrantTypeId = 112, CanOwn = true, CanGive = false, ScopeMode = GrantScopeMode.SameAsRole }); // DirectorHubAccess

        // Note: Assigner (Role 8) does NOT get AccessAdminNavigation - uses employee navigation
        // Note: Employee (Role 1) does NOT get AccessAdminNavigation - uses employee navigation

        // ============================================
        // VIEW COMPANY CALENDAR GRANTS (114)
        // ============================================

        // All roles that can view company-wide calendar
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 11, GrantTypeId = 114, CanOwn = true, CanGive = true, ScopeMode = GrantScopeMode.SameAsRole }); // Owner: ViewCompanyCalendar
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 10, GrantTypeId = 114, CanOwn = true, CanGive = false, ScopeMode = GrantScopeMode.SameAsRole }); // Area Admin: ViewCompanyCalendar
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 7, GrantTypeId = 114, CanOwn = true, CanGive = false, ScopeMode = GrantScopeMode.SameAsRole }); // Molecule Admin: ViewCompanyCalendar
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 5, GrantTypeId = 114, CanOwn = true, CanGive = false, ScopeMode = GrantScopeMode.SameAsRole }); // Alhut Director: ViewCompanyCalendar
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 6, GrantTypeId = 114, CanOwn = true, CanGive = false, ScopeMode = GrantScopeMode.SameAsRole }); // Text Director: ViewCompanyCalendar
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 2, GrantTypeId = 114, CanOwn = true, CanGive = false, ScopeMode = GrantScopeMode.SameAsRole }); // BR Director: ViewCompanyCalendar
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 3, GrantTypeId = 114, CanOwn = true, CanGive = false, ScopeMode = GrantScopeMode.SameAsRole }); // Alhut Lead: ViewCompanyCalendar
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 4, GrantTypeId = 114, CanOwn = true, CanGive = false, ScopeMode = GrantScopeMode.SameAsRole }); // Text Lead: ViewCompanyCalendar
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 9, GrantTypeId = 114, CanOwn = true, CanGive = false, ScopeMode = GrantScopeMode.SameAsRole }); // Department Lead: ViewCompanyCalendar

        // ============================================
        // JOIN REQUEST MANAGEMENT GRANTS
        // ============================================

        // Owner (Role 11) - Global scope for join request management
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 11, GrantTypeId = 115, CanOwn = true, CanGive = true, ScopeMode = GrantScopeMode.SameAsRole }); // ManageJoinRequests
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 11, GrantTypeId = 116, CanOwn = true, CanGive = true, ScopeMode = GrantScopeMode.SameAsRole }); // ViewCompanyUsers
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 11, GrantTypeId = 117, CanOwn = true, CanGive = true, ScopeMode = GrantScopeMode.SameAsRole }); // EditCompanyUsers

        // Area Admin (Role 10) - Area scope for join request management
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 10, GrantTypeId = 115, CanOwn = true, CanGive = false, ScopeMode = GrantScopeMode.SameAsRole }); // ManageJoinRequests
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 10, GrantTypeId = 116, CanOwn = true, CanGive = false, ScopeMode = GrantScopeMode.SameAsRole }); // ViewCompanyUsers
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 10, GrantTypeId = 117, CanOwn = true, CanGive = false, ScopeMode = GrantScopeMode.SameAsRole }); // EditCompanyUsers

        // Molecule Admin (Role 7) - Molecule scope for join request management
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 7, GrantTypeId = 115, CanOwn = true, CanGive = false, ScopeMode = GrantScopeMode.SameAsRole }); // ManageJoinRequests
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 7, GrantTypeId = 116, CanOwn = true, CanGive = false, ScopeMode = GrantScopeMode.SameAsRole }); // ViewCompanyUsers
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 7, GrantTypeId = 117, CanOwn = true, CanGive = false, ScopeMode = GrantScopeMode.SameAsRole }); // EditCompanyUsers

        // BR Director (Role 2) - Company scope for join request management
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 2, GrantTypeId = 115, CanOwn = true, CanGive = false, ScopeMode = GrantScopeMode.SameAsRole }); // ManageJoinRequests

        // ============================================
        // DYNAMIC ROLE TEMPLATE GRANTS (IDs 119-122)
        // ManageAnnouncements (119), ViewSystemAlerts (120), ViewAllAreas (121), ManageOnDuty (122)
        // ============================================

        // Trainee (Role 12) - Basic view grants (subset of Employee — explicitly NO RequestSwap)
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 12, GrantTypeId = 1, CanOwn = true, CanGive = false, ScopeMode = GrantScopeMode.SameAsRole }); // ViewShifts
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 12, GrantTypeId = 17, CanOwn = true, CanGive = false, ScopeMode = GrantScopeMode.SameAsRole }); // ViewChores
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 12, GrantTypeId = 21, CanOwn = true, CanGive = false, ScopeMode = GrantScopeMode.SameAsRole }); // ViewVacations
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 12, GrantTypeId = 22, CanOwn = true, CanGive = false, ScopeMode = GrantScopeMode.SameAsRole }); // RequestVacation

        // Owner (Role 11) - ManageAnnouncements, ViewSystemAlerts, ViewAllAreas, ManageOnDuty
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 11, GrantTypeId = 119, CanOwn = true, CanGive = true, ScopeMode = GrantScopeMode.SameAsRole }); // ManageAnnouncements
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 11, GrantTypeId = 120, CanOwn = true, CanGive = true, ScopeMode = GrantScopeMode.SameAsRole }); // ViewSystemAlerts
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 11, GrantTypeId = 121, CanOwn = true, CanGive = true, ScopeMode = GrantScopeMode.SameAsRole }); // ViewAllAreas
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 11, GrantTypeId = 122, CanOwn = true, CanGive = true, ScopeMode = GrantScopeMode.SameAsRole }); // ManageOnDuty

        // AreaAdmin (Role 10) - ManageAnnouncements, ViewAllAreas, ManageOnDuty
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 10, GrantTypeId = 119, CanOwn = true, CanGive = false, ScopeMode = GrantScopeMode.SameAsRole }); // ManageAnnouncements
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 10, GrantTypeId = 121, CanOwn = true, CanGive = false, ScopeMode = GrantScopeMode.SameAsRole }); // ViewAllAreas
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 10, GrantTypeId = 122, CanOwn = true, CanGive = false, ScopeMode = GrantScopeMode.SameAsRole }); // ManageOnDuty

        // AlhutDirector (Role 5) - ManageAnnouncements, ViewAllAreas
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 5, GrantTypeId = 119, CanOwn = true, CanGive = false, ScopeMode = GrantScopeMode.SameAsRole }); // ManageAnnouncements
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 5, GrantTypeId = 121, CanOwn = true, CanGive = false, ScopeMode = GrantScopeMode.SameAsRole }); // ViewAllAreas

        // TextDirector (Role 6) - ManageAnnouncements, ViewAllAreas
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 6, GrantTypeId = 119, CanOwn = true, CanGive = false, ScopeMode = GrantScopeMode.SameAsRole }); // ManageAnnouncements
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 6, GrantTypeId = 121, CanOwn = true, CanGive = false, ScopeMode = GrantScopeMode.SameAsRole }); // ViewAllAreas

        // MoleculeAdmin (Role 7) - ManageAnnouncements
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 7, GrantTypeId = 119, CanOwn = true, CanGive = false, ScopeMode = GrantScopeMode.SameAsRole }); // ManageAnnouncements

        // BRDirector (Role 2) - ViewSystemAlerts
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 2, GrantTypeId = 120, CanOwn = true, CanGive = false, ScopeMode = GrantScopeMode.SameAsRole }); // ViewSystemAlerts

        // AlhutLead (Role 3) - ViewSystemAlerts
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 3, GrantTypeId = 120, CanOwn = true, CanGive = false, ScopeMode = GrantScopeMode.SameAsRole }); // ViewSystemAlerts

        // TextLead (Role 4) - ViewSystemAlerts
        grants.Add(new RoleTemplateGrant { Id = id++, RoleTemplateId = 4, GrantTypeId = 120, CanOwn = true, CanGive = false, ScopeMode = GrantScopeMode.SameAsRole }); // ViewSystemAlerts

        return grants;
    }
}
