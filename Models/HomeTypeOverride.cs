namespace ShiftManager.Models;

/// <summary>
/// Per-user pattern deviation from a HomeType template.
/// Created when admin explicitly overrides a specific user's home pattern.
/// Most users have no override — they follow the template directly.
/// </summary>
public class HomeTypeOverride : IBelongsToCompany
{
    public int Id { get; set; }

    /// <summary>Tenant scoping (IBelongsToCompany).</summary>
    public int CompanyId { get; set; }

    /// <summary>The HomeType template being overridden.</summary>
    public int HomeTypeId { get; set; }

    /// <summary>The user whose pattern differs from the template.</summary>
    public int UserId { get; set; }

    /// <summary>
    /// User-specific painted pattern (different from template).
    /// JSON array of date strings (yyyy-MM-dd).
    /// </summary>
    public string OverridePatternJson { get; set; } = "[]";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int CreatedBy { get; set; }

    // Navigation
    public HomeType HomeType { get; set; } = null!;
    public AppUser User { get; set; } = null!;
    public AppUser Creator { get; set; } = null!;
}
