using ShiftManager.Models.Support;

namespace ShiftManager.Models;

public class GrantType
{
    public int Id { get; set; }
    public string Key { get; set; } = string.Empty;  // "AssignAlhutShifts"
    public string NameKey { get; set; } = string.Empty;  // Localization key
    public string DescriptionKey { get; set; } = string.Empty;
    public GrantCategory Category { get; set; }
    public GrantScopeLevel DefaultScope { get; set; }
    public bool IsSystem { get; set; }  // true = built-in, false = custom
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// True for grant types retained for ID-stability/audit history but no longer assignable
    /// (e.g. the 8 per-type Assign*Shifts grants superseded by AssignShifts on 2026-06-16).
    /// Hidden from the grant-admin catalog; never re-applied to templates or users.
    /// </summary>
    public bool IsDeprecated { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int? CreatedByUserId { get; set; }

    // Navigation
    public AppUser? CreatedByUser { get; set; }
    public List<Grant> Grants { get; set; } = new();
    public List<RoleTemplateGrant> RoleTemplateGrants { get; set; } = new();
}
