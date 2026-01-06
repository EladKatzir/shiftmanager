using ShiftManager.Models.Support;
using System.ComponentModel.DataAnnotations;

namespace ShiftManager.Models;

/// <summary>
/// Stores language configuration for a company (default + alternate language).
/// Each company can define which two languages are available for users to toggle between.
/// </summary>
public class CompanyLanguageSettings : IBelongsToCompany
{
    public int Id { get; set; }

    /// <summary>
    /// Company this language configuration belongs to
    /// </summary>
    public int CompanyId { get; set; }

    /// <summary>
    /// Default language for the company (shown to new users or users without a preference)
    /// Must be "en-US" or "he-IL"
    /// </summary>
    [Required]
    [StringLength(10)]
    public string DefaultCulture { get; set; } = "en-US";

    /// <summary>
    /// Alternate language available via language toggle
    /// Must be "en-US" or "he-IL" and must differ from DefaultCulture
    /// </summary>
    [Required]
    [StringLength(10)]
    public string AlternateCulture { get; set; } = "he-IL";

    /// <summary>
    /// When this configuration was created
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// User who created this configuration
    /// </summary>
    public int CreatedBy { get; set; }

    /// <summary>
    /// When this configuration was last updated
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// User who last updated this configuration
    /// </summary>
    public int UpdatedBy { get; set; }

    // Navigation properties
    public Company? Company { get; set; }
    public AppUser? CreatedByUser { get; set; }
    public AppUser? UpdatedByUser { get; set; }
}
