using ShiftManager.Models.Support;

namespace ShiftManager.Models;

/// <summary>
/// Stores Griffin ADFS configuration per company.
/// Pattern matches EmailConfig for consistency.
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
    /// Auto-provision users on first Griffin login
    /// </summary>
    public bool AutoProvisionUsers { get; set; } = true;

    /// <summary>
    /// Default role for auto-provisioned users
    /// </summary>
    public UserRole DefaultProvisionedRole { get; set; } = UserRole.Employee;

    /// <summary>
    /// Template for auto-provisioned SSO users (replaces DefaultProvisionedRole enum).
    /// </summary>
    public int? DefaultProvisionedRoleTemplateId { get; set; }

    /// <summary>
    /// Timeout for Griffin API calls in seconds
    /// </summary>
    public int TimeoutSeconds { get; set; } = 10;

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
