namespace ShiftManager.Models;

/// <summary>
/// Represents a feature flag for controlling feature availability.
/// Flags can be scoped globally, per-company, or per-user.
///
/// Priority resolution (highest to lowest):
/// 1. User-specific flag (UserId + CompanyId set)
/// 2. Company-specific flag (CompanyId set, UserId null)
/// 3. Global flag (both CompanyId and UserId null)
/// </summary>
public class FeatureFlag
{
    public int Id { get; set; }

    /// <summary>
    /// Unique identifier for the flag (e.g., "FF_WIDGETS_ENABLED")
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Whether this flag is enabled
    /// </summary>
    public bool IsEnabled { get; set; }

    /// <summary>
    /// Human-readable description of what this flag controls
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Company ID for company-scoped flags. Null = global flag.
    /// </summary>
    public int? CompanyId { get; set; }

    /// <summary>
    /// User ID for user-scoped flags. Null = applies to all users in scope.
    /// </summary>
    public int? UserId { get; set; }

    /// <summary>
    /// When this flag was created
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When this flag was last modified
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties (optional - for reference integrity)
    public Company? Company { get; set; }
    public AppUser? User { get; set; }
}
