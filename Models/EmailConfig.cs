namespace ShiftManager.Models;

/// <summary>
/// Stores email configuration settings for each company.
/// The ApiKey field is encrypted at rest using Data Protection API.
/// </summary>
public class EmailConfig : IBelongsToCompany
{
    public int Id { get; set; }
    public int CompanyId { get; set; }

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
}
