namespace ShiftManager.Models;

public class Company
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Hebrew display name for the company. When the UI culture is Hebrew, this is used instead of Name.
    /// </summary>
    public string? NameHe { get; set; }

    // Multitenancy Phase 1: URL routing and branding
    public string? Slug { get; set; }
    public string? DisplayName { get; set; }

    // JSON column for company-specific settings overrides
    public string? SettingsJson { get; set; }

    // HQ company flag — auto-created per molecule for Director assignments
    public bool IsHeadquarters { get; set; }

    public int SortOrder { get; set; }

    /// <summary>
    /// Returns the culture-appropriate display name: NameHe when Hebrew, otherwise DisplayName ?? Name.
    /// </summary>
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public string LocalizedName => ResolveLocalizedName(Name, DisplayName, NameHe);

    /// <summary>
    /// Culture-aware company name resolver for use after EF projections (where the computed
    /// <see cref="LocalizedName"/> is not available because only raw fields were projected into a DTO).
    /// Mirrors the fallback chain of <see cref="LocalizedName"/> exactly.
    /// </summary>
    public static string ResolveLocalizedName(string? name, string? displayName, string? nameHe) =>
        System.Globalization.CultureInfo.CurrentUICulture.Name.StartsWith("he", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(nameHe)
            ? nameHe!
            : (displayName ?? name ?? string.Empty);

    // Organizational hierarchy - Molecule FK (nullable during migration, required after)
    public int? MoleculeId { get; set; }
    public Molecule? Molecule { get; set; }
}
