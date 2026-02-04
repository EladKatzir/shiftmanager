using ShiftManager.Models.Support;

namespace ShiftManager.Models;

/// <summary>
/// Configuration for custom "Day Shift" types (user-facing term: "סוגי משמרות יומיות מותאמות אישית" / "Custom Day Shift Types").
///
/// TERMINOLOGY NOTE: Class named "OnDutyTypeConfig" for historical reasons.
/// In the UI, these define custom types of "Day Shifts" beyond the default Hakam/Lead.
///
/// Characteristics:
/// - Global table (not company-scoped) since Day Shifts themselves are global
/// - Allows organizations to define custom day shift types beyond Hakam/Lead defaults
/// - Supports localized names (English and Hebrew)
///
/// See TERMINOLOGY.md for complete terminology mapping.
/// </summary>
public class OnDutyTypeConfig
{
    public int Id { get; set; }

    /// <summary>
    /// The numeric value for this custom type (should be > 1 to avoid conflicts with Hakam=0, Lead=1)
    /// </summary>
    public int TypeValue { get; set; }

    /// <summary>
    /// Display name in English
    /// </summary>
    public string NameEn { get; set; } = string.Empty;

    /// <summary>
    /// Display name in Hebrew
    /// </summary>
    public string NameHe { get; set; } = string.Empty;

    /// <summary>
    /// Icon/Emoji to display for this type
    /// </summary>
    public string Icon { get; set; } = "📌";

    /// <summary>
    /// CSS color for this type (e.g., "#8b5cf6" or "purple")
    /// </summary>
    public string Color { get; set; } = "#6366f1";

    /// <summary>
    /// Whether this type is active
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// If true, only officers (rank >= SegenMishne) can be assigned this duty type.
    /// Used for Katzin duties.
    /// </summary>
    public bool RequiresOfficerRank { get; set; } = false;

    /// <summary>
    /// Created timestamp
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Who created this configuration
    /// </summary>
    public int CreatedBy { get; set; }
}
