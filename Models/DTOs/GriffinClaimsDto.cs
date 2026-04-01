using System.Text.Json.Serialization;

namespace ShiftManager.Models.DTOs;

/// <summary>
/// Represents user claims returned from Griffin ADFS /authorization/getClaims endpoint.
/// Field names match the actual Griffin API response (not standard ADFS claim names).
/// </summary>
public class GriffinClaimsDto
{
    /// <summary>
    /// Unique identifier for the user (e.g., "7108user@8200")
    /// </summary>
    [JsonPropertyName("UniqueID")]
    public string UniqueID { get; set; } = string.Empty;

    /// <summary>
    /// User's email address (used for user lookup in ShiftManager)
    /// </summary>
    [JsonPropertyName("EmailAddress")]
    public string EmailAddress { get; set; } = string.Empty;

    /// <summary>
    /// Full display name (may be OU path format, e.g., "blah/blah/user")
    /// </summary>
    [JsonPropertyName("DisplayName")]
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// First name / given name
    /// </summary>
    [JsonPropertyName("GivenName")]
    public string GivenName { get; set; } = string.Empty;

    // --- JWT standard fields (for reference/logging) ---

    /// <summary>
    /// Audience
    /// </summary>
    [JsonPropertyName("aud")]
    public string Audience { get; set; } = string.Empty;

    /// <summary>
    /// Issuer (Griffin uses "iis" instead of standard "iss")
    /// </summary>
    [JsonPropertyName("iis")]
    public string Issuer { get; set; } = string.Empty;

    /// <summary>
    /// Issued at timestamp
    /// </summary>
    [JsonPropertyName("iat")]
    public string IssuedAt { get; set; } = string.Empty;

    /// <summary>
    /// Not before timestamp
    /// </summary>
    [JsonPropertyName("nbf")]
    public string NotBefore { get; set; } = string.Empty;

    /// <summary>
    /// Expiration timestamp
    /// </summary>
    [JsonPropertyName("exp")]
    public string Expiration { get; set; } = string.Empty;
}
