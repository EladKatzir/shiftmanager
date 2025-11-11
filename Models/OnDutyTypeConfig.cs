using ShiftManager.Models.Support;

namespace ShiftManager.Models;

/// <summary>
/// Configuration for custom OnDuty types per company.
/// Allows companies to define their own OnDuty types beyond the default Hakam/Lead.
/// Global table (not company-scoped) since OnDuty itself is global.
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
    /// Created timestamp
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Who created this configuration
    /// </summary>
    public int CreatedBy { get; set; }
}
