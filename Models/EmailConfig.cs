namespace ShiftManager.Models;

/// <summary>
/// Stores email configuration settings. Supports global config (CompanyId = null)
/// and per-company overrides (CompanyId = specific company).
/// The ApiKey field is encrypted at rest using Data Protection API.
/// Fallback chain: company-specific → global → appsettings.json.
/// NOTE: Does NOT implement IBelongsToCompany — global config has no company.
/// Query filter includes both global (CompanyId = null) and tenant-scoped configs.
/// </summary>
public class EmailConfig
{
    public int Id { get; set; }

    /// <summary>
    /// Null = global config (applies to all companies by default).
    /// Non-null = per-company override for a specific company.
    /// </summary>
    public int? CompanyId { get; set; }

    /// <summary>
    /// Whether email notifications are enabled for this company
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// The API key for the email service (stored encrypted)
    /// </summary>
    public string? EncryptedApiKey { get; set; }

    /// <summary>
    /// The URL endpoint for the email API service
    /// </summary>
    public string? ApiUrl { get; set; }

    /// <summary>
    /// The from email address for outgoing emails
    /// </summary>
    public string? FromAddress { get; set; }

    /// <summary>
    /// When this configuration was last updated
    /// </summary>
    public DateTime LastUpdated { get; set; }

    /// <summary>
    /// User who last updated this configuration
    /// </summary>
    public string? LastUpdatedBy { get; set; }

    /// <summary>When false, this per-company override is ignored and the global config is used instead
    /// (a reversible "disable override"). Default true. Distinct from Enabled (email on/off).</summary>
    public bool OverrideEnabled { get; set; } = true;
}
