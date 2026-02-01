namespace ShiftManager.Models.DTOs;

/// <summary>
/// Represents user claims returned from Griffin ADFS /authorization/getClaims endpoint
/// </summary>
public class GriffinClaimsDto
{
    /// <summary>
    /// Windows/AD username (short form, no domain)
    /// </summary>
    public string sAMAccountName { get; set; } = string.Empty;

    /// <summary>
    /// User Principal Name (email-like identifier)
    /// </summary>
    public string UPN { get; set; } = string.Empty;

    /// <summary>
    /// Full display name
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// First name
    /// </summary>
    public string GivenName { get; set; } = string.Empty;

    /// <summary>
    /// Last name
    /// </summary>
    public string Surname { get; set; } = string.Empty;

    /// <summary>
    /// Timestamp when user authenticated with ADFS
    /// </summary>
    public string auth_time { get; set; } = string.Empty;
}
