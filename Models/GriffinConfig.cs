using ShiftManager.Models.Support;

namespace ShiftManager.Models;

/// <summary>
/// Stores Griffin ADFS connectivity configuration per company.
/// Pattern matches EmailConfig for consistency.
///
/// User-provisioning behaviour is NOT stored here — see the feature flag
/// FF_ALLOW_USERS_CREATION_VIA_ADFS in FeatureFlagSeed for that toggle. This entity
/// is purely the connection record (URL, callback, timeout).
/// </summary>
public class GriffinConfig : IBelongsToCompany
{
    public int Id { get; set; }
    public int CompanyId { get; set; }

    /// <summary>
    /// Enable Griffin ADFS authentication for this company
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Griffin service base URL (e.g., "http://7108dev-auth.d8200.mil")
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Our callback URL where Griffin redirects after authentication
    /// </summary>
    public string? TokenConsumerUrl { get; set; }

    /// <summary>
    /// Timeout for Griffin API calls in seconds
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// When this config was last modified
    /// </summary>
    public DateTime LastUpdated { get; set; }

    /// <summary>
    /// User who last modified this config
    /// </summary>
    public string? LastUpdatedBy { get; set; }

    // Navigation
    public Company? Company { get; set; }
}
