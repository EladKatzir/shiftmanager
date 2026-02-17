namespace ShiftManager.Models;

/// <summary>
/// Optional per-job-type display name overrides for role templates.
/// Example: Manager template shows as "Mapotz" for Alhut job type.
/// Cross-company — does NOT implement IBelongsToCompany.
/// </summary>
public class RoleTemplateJobTypeLabel
{
    public int Id { get; set; }
    public int RoleTemplateId { get; set; }
    public int JobTypeId { get; set; }
    public string DisplayNameEN { get; set; } = string.Empty;
    public string DisplayNameHE { get; set; } = string.Empty;

    // Navigation
    public RoleTemplate? RoleTemplate { get; set; }
    public JobType? JobType { get; set; }
}
