namespace ShiftManager.Configuration;

/// <summary>
/// Configuration options for database seeding, read from appsettings.json.
/// Use this to configure the owner account and add custom molecules/companies
/// without modifying code - ideal for air-gapped production deployments.
/// </summary>
public class SeedingOptions
{
    public const string SectionName = "Seeding";

    /// <summary>
    /// Owner user configuration. If not specified, defaults are used.
    /// </summary>
    public OwnerSeedOptions Owner { get; set; } = new();

    /// <summary>
    /// Additional molecules to seed beyond the default Shifty hierarchy.
    /// These are added after the base hierarchy is seeded.
    /// </summary>
    public List<MoleculeSeedOptions> AdditionalMolecules { get; set; } = new();

    /// <summary>
    /// Additional companies to seed beyond the default Shifty hierarchy.
    /// These are added after molecules are seeded.
    /// </summary>
    public List<CompanySeedOptions> AdditionalCompanies { get; set; } = new();

    /// <summary>
    /// Additional departments to seed (for Tech molecules).
    /// </summary>
    public List<DepartmentSeedOptions> AdditionalDepartments { get; set; } = new();
}

/// <summary>
/// Configuration for the Owner (admin) user.
/// </summary>
public class OwnerSeedOptions
{
    /// <summary>
    /// Email address for the owner account. Default: "admin@local"
    /// </summary>
    public string Email { get; set; } = "admin@local";

    /// <summary>
    /// Password for the owner account.
    /// SECURITY: Change this in production!
    /// Default: "admin123" (development only)
    /// </summary>
    public string Password { get; set; } = "admin123";

    /// <summary>
    /// Display name for the owner account. Default: "Owner"
    /// </summary>
    public string DisplayName { get; set; } = "Owner";
}

/// <summary>
/// Configuration for seeding a new molecule.
/// </summary>
public class MoleculeSeedOptions
{
    /// <summary>
    /// Internal name (English, no spaces). Required.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Display name (can be Hebrew). Required.
    /// </summary>
    public string DisplayName { get; set; } = "";

    /// <summary>
    /// Molecule type: "Workforce", "Tech", "Helper", or "System".
    /// Default: "Workforce"
    /// </summary>
    public string Type { get; set; } = "Workforce";

    /// <summary>
    /// Name of the area this molecule belongs to. Default: "190"
    /// </summary>
    public string AreaName { get; set; } = "190";
}

/// <summary>
/// Configuration for seeding a new company.
/// </summary>
public class CompanySeedOptions
{
    /// <summary>
    /// Internal name (English, no spaces). Required.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Display name (can be Hebrew). Required.
    /// </summary>
    public string DisplayName { get; set; } = "";

    /// <summary>
    /// Name of the molecule this company belongs to. Required.
    /// </summary>
    public string MoleculeName { get; set; } = "";

    /// <summary>
    /// Optional URL-friendly slug. If not provided, generated from Name.
    /// </summary>
    public string? Slug { get; set; }
}

/// <summary>
/// Configuration for seeding a new department (for Tech molecules).
/// </summary>
public class DepartmentSeedOptions
{
    /// <summary>
    /// Internal name (English, no spaces). Required.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Display name (can be Hebrew). Required.
    /// </summary>
    public string DisplayName { get; set; } = "";

    /// <summary>
    /// Name of the molecule this department belongs to. Required.
    /// Must be a Tech-type molecule.
    /// </summary>
    public string MoleculeName { get; set; } = "";
}
