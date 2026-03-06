using ShiftManager.Models.Support;

namespace ShiftManager.Models;

public class RoleTemplate
{
    public int Id { get; set; }
    public string Key { get; set; } = string.Empty;  // "BRDirector", "Lead", "Director"
    public string NameKey { get; set; } = string.Empty;
    public string DescriptionKey { get; set; } = string.Empty;
    public RoleScopeLevel ScopeLevel { get; set; }
    public bool IsSystem { get; set; }  // Built-in vs custom
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Dynamic Role Template fields
    /// <summary>
    /// Maps template to business tier (Employee, Manager, Director, Trainee, Owner, Assigner, AreaAdmin).
    /// Used to auto-populate AppUser.Role. Custom roles MUST set this.
    /// </summary>
    public UserRole? DerivedUserRole { get; set; }

    /// <summary>Free-text English display name (for custom roles; system roles use NameKey → resx)</summary>
    public string? DisplayNameEN { get; set; }

    /// <summary>Free-text Hebrew display name (for custom roles; system roles use NameKey → resx)</summary>
    public string? DisplayNameHE { get; set; }

    /// <summary>Owner can restrict assignment of specific roles. Default: true.</summary>
    public bool CanBeAssignedByDefault { get; set; } = true;

    /// <summary>Controls whether role appears in public signup dropdown. Default: true.</summary>
    public bool IsVisibleInSignup { get; set; } = true;

    // Navigation
    public List<RoleTemplateGrant> AutoGrants { get; set; } = new();
    public List<UserRoleAssignment> UserRoles { get; set; } = new();
    public List<RoleTemplateJobTypeLabel> JobTypeLabels { get; set; } = new();
}
