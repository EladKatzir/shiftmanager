using System.Text.Json;
using System.Text.Json.Serialization;

namespace ShiftManager.Models.DTOs;

/// <summary>
/// Represents user claims returned from Griffin ADFS /authorization/getClaims endpoint.
/// Field names match the actual Griffin API response (not standard ADFS claim names).
/// </summary>
public class GriffinClaimsDto
{
    // Business-critical scalar fields: defensively typed with JsonNumberOrStringConverter so that
    // a Griffin deployment which sends UniqueID as a JSON number (e.g. `7108` rather than `"7108"`)
    // doesn't blow up the entire deserialisation — the same RFC 7519 NumericDate trap that bit us
    // on iat/nbf/exp could trivially recur on these fields if Griffin's contract drifts.

    /// <summary>Unique identifier for the user (e.g., "7108user@8200"). Coerced from numbers if needed.</summary>
    [JsonPropertyName("UniqueID")]
    [JsonConverter(typeof(JsonNumberOrStringConverter))]
    public string UniqueID { get; set; } = string.Empty;

    /// <summary>User's email address (used for user lookup in ShiftManager).</summary>
    [JsonPropertyName("EmailAddress")]
    [JsonConverter(typeof(JsonNumberOrStringConverter))]
    public string EmailAddress { get; set; } = string.Empty;

    /// <summary>Full display name (may be OU path format, e.g., "blah/blah/user").</summary>
    [JsonPropertyName("DisplayName")]
    [JsonConverter(typeof(JsonNumberOrStringConverter))]
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>First name / given name.</summary>
    [JsonPropertyName("GivenName")]
    [JsonConverter(typeof(JsonNumberOrStringConverter))]
    public string GivenName { get; set; } = string.Empty;

    /// <summary>
    /// Family name / surname. Surfaced to the GriffinSignup form as a pre-filled
    /// editable field. Empty when Griffin's getClaims response omits the field.
    /// </summary>
    [JsonPropertyName("Surname")]
    [JsonConverter(typeof(JsonNumberOrStringConverter))]
    public string Surname { get; set; } = string.Empty;

    // --- JWT standard fields (for reference/logging only — not used by app logic).
    // Typed as JsonElement? so System.Text.Json never throws on shape variations:
    // Griffin returns numeric (Unix epoch seconds) iat/nbf/exp per RFC 7519 NumericDate,
    // but the contract has drifted across versions and may also return strings or arrays.
    // Capture whatever arrives; stringify lazily at log time via FormatClaim().

    /// <summary>Audience claim — string OR array of strings per RFC 7519.</summary>
    [JsonPropertyName("aud")]
    public JsonElement? Audience { get; set; }

    /// <summary>Issuer claim using Griffin's historical non-standard key "iis".</summary>
    [JsonPropertyName("iis")]
    public JsonElement? IssuerLegacy { get; set; }

    /// <summary>Issuer claim using the RFC 7519 standard key "iss". Some Griffin deployments emit this instead of/alongside "iis".</summary>
    [JsonPropertyName("iss")]
    public JsonElement? IssuerStandard { get; set; }

    /// <summary>Issued-at — typically a JSON number (Unix epoch seconds).</summary>
    [JsonPropertyName("iat")]
    public JsonElement? IssuedAt { get; set; }

    /// <summary>Not-before — typically a JSON number (Unix epoch seconds).</summary>
    [JsonPropertyName("nbf")]
    public JsonElement? NotBefore { get; set; }

    /// <summary>Expiration — typically a JSON number (Unix epoch seconds).</summary>
    [JsonPropertyName("exp")]
    public JsonElement? Expiration { get; set; }

    /// <summary>Pick whichever issuer field arrived (standard "iss" wins over legacy "iis").</summary>
    [JsonIgnore]
    public JsonElement? Issuer => IssuerStandard ?? IssuerLegacy;
}
