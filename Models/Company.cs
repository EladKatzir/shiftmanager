namespace ShiftManager.Models;

public class Company
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    // Multitenancy Phase 1: URL routing and branding
    public string? Slug { get; set; }
    public string? DisplayName { get; set; }

    // JSON column for company-specific settings overrides
    public string? SettingsJson { get; set; }

    // HQ company flag — auto-created per molecule for Director assignments
    public bool IsHeadquarters { get; set; }

    // Organizational hierarchy - Molecule FK (nullable during migration, required after)
    public int? MoleculeId { get; set; }
    public Molecule? Molecule { get; set; }
}
