using ShiftManager.Models.Support;

namespace ShiftManager.Helpers;

/// <summary>
/// Centralized mapping from UserRole enum to RoleTemplate key.
/// Used during login-time backfill, admin user CRUD, and grant repair.
/// The canonical source of truth for role-to-template mapping.
/// IMPORTANT: When new job types are added, the Manager/Director switch cases
/// must be updated to include the new job type → template key mappings.
/// </summary>
public static class RoleTemplateMapper
{
    /// <summary>
    /// Maps a UserRole enum value to the corresponding RoleTemplate key.
    /// For Manager and Director roles, uses the jobTypeName to disambiguate
    /// between job-type-specific templates (e.g., Lead vs BRDirector).
    /// </summary>
    public static string MapUserRoleToRoleTemplateKey(UserRole role, string? jobTypeName = null)
    {
        return role switch
        {
            UserRole.Owner => "Owner",
            UserRole.Assigner => "Assigner",
            UserRole.AreaAdmin => "AreaAdmin",
            UserRole.Employee => "Employee",
            UserRole.Trainee => "Trainee",
            UserRole.Manager => jobTypeName switch
            {
                "Alhut" or "Text" => "Lead",
                _ => "BRDirector"
            },
            UserRole.Director => jobTypeName switch
            {
                "Alhut" or "Text" => "Director",
                _ => "MoleculeAdmin"
            },
            _ => "Employee"
        };
    }
}
