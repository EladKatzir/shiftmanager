using ShiftManager.Models.Support;

namespace ShiftManager.Models;

public class RoleTemplate
{
    public int Id { get; set; }
    public string Key { get; set; } = string.Empty;  // "BRDirector", "AlhutLead"
    public string NameKey { get; set; } = string.Empty;
    public string DescriptionKey { get; set; } = string.Empty;
    public RoleScopeLevel ScopeLevel { get; set; }
    public bool IsSystem { get; set; }  // Built-in vs custom
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public List<RoleTemplateGrant> AutoGrants { get; set; } = new();
    public List<UserRoleAssignment> UserRoles { get; set; } = new();
}
