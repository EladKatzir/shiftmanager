using ShiftManager.Models.Support;
using System.ComponentModel.DataAnnotations;

namespace ShiftManager.Models;

/// <summary>
/// Stores custom translation overrides for a company.
/// Allows companies to customize localized strings per culture (en-US or he-IL).
/// </summary>
public class CompanyLocalizationOverride : IBelongsToCompany
{
    public int Id { get; set; }

    /// <summary>
    /// Company this override belongs to
    /// </summary>
    public int CompanyId { get; set; }

    /// <summary>
    /// Culture code for this override ("en-US" or "he-IL")
    /// </summary>
    [Required]
    [StringLength(10)]
    public string Culture { get; set; } = string.Empty;

    /// <summary>
    /// Resource key from SharedResources (e.g., "Button_Save", "Dashboard_Title")
    /// </summary>
    [Required]
    [StringLength(200)]
    public string ResourceKey { get; set; } = string.Empty;

    /// <summary>
    /// Custom translation value (HTML-encoded for security)
    /// </summary>
    [Required]
    [StringLength(2000)]
    public string OverrideValue { get; set; } = string.Empty;

    /// <summary>
    /// Whether this override is active (for soft delete capability)
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// When this override was created
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// User who created this override
    /// </summary>
    public int CreatedBy { get; set; }

    /// <summary>
    /// When this override was last updated
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// User who last updated this override
    /// </summary>
    public int UpdatedBy { get; set; }

    // Navigation properties
    public Company? Company { get; set; }
    public AppUser? CreatedByUser { get; set; }
    public AppUser? UpdatedByUser { get; set; }
}
