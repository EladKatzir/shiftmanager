using System.ComponentModel.DataAnnotations;

namespace ShiftManager.Models;

/// <summary>
/// A named rotation template defining WHEN users are home.
/// Admin paints home days on a monthly calendar; system derives a recurrence rule.
/// Users assigned to a HomeType follow its rotation pattern for HOME shift generation.
/// </summary>
public class HomeType : IBelongsToCompany
{
    public int Id { get; set; }

    /// <summary>Tenant scoping (IBelongsToCompany).</summary>
    public int CompanyId { get; set; }

    /// <summary>Molecule scope for this rotation template.</summary>
    public int MoleculeId { get; set; }

    /// <summary>Display name, e.g., "סבב א", "סבב ב", "רבעונים".</summary>
    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Optional Hebrew name override.</summary>
    public string? NameHe { get; set; }

    /// <summary>
    /// Serialized painted dates within the reference month.
    /// JSON array of date strings (yyyy-MM-dd) that the admin painted as home days.
    /// </summary>
    public string? PatternJson { get; set; }

    /// <summary>
    /// System-derived recurrence rule from the painted pattern.
    /// JSON: { "cycleWeeks": 2, "homeDays": [4,5,6,0], "weekOffsets": [0] }
    /// null = custom (non-repeating) pattern.
    /// </summary>
    public string? DerivedRule { get; set; }

    /// <summary>Whether this home type is active and available for assignment.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Set by the materialiser on every successful Generate. Compared to UpdatedAt to drive
    /// the "needs regenerate" admin banner: when UpdatedAt > LastGeneratedAt (or this is null),
    /// the pattern was edited but not yet propagated to ShiftAssignment rows.
    /// </summary>
    public DateTime? LastGeneratedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int CreatedBy { get; set; }

    // Navigation
    public Molecule Molecule { get; set; } = null!;
    public AppUser Creator { get; set; } = null!;
}
