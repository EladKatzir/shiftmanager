using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ShiftManager.Models.DTOs;

/// <summary>
/// Represents user claims returned from Griffin ADFS /authorization/getClaims endpoint.
///
/// PARSING POLICY: do NOT use <c>JsonSerializer.Deserialize&lt;GriffinClaimsDto&gt;</c> directly.
/// Use the static <see cref="Parse"/> factory below instead — it implements aggressive
/// property-name normalization (case + separator agnostic) and surfaces the list of keys
/// actually present in the response when a business-critical field is missing. The default
/// deserialization path (case-insensitive only) silently produces empty fields on any
/// non-casing variation (snake_case, kebab-case, LDAP-style names), which we observed
/// during the 2026-05-11 hardening pass.
///
/// Field type policy:
///   - Business-critical fields (UniqueID, EmailAddress, DisplayName, GivenName, Surname):
///     plain <c>string</c>. Coerced from JSON numbers/booleans/null via Parse.
///   - JWT-spec fields (aud, iss/iis, iat, nbf, exp): <c>JsonElement?</c> so any shape
///     (number, string, array, null) survives parsing. These are reference/logging only.
/// </summary>
public class GriffinClaimsDto
{
    // ---- Business-critical scalar fields ----
    public string UniqueID { get; set; } = string.Empty;
    public string EmailAddress { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string GivenName { get; set; } = string.Empty;
    public string Surname { get; set; } = string.Empty;

    // ---- JWT-spec reference/logging fields (any JSON shape OK) ----
    public JsonElement? Audience { get; set; }
    public JsonElement? IssuerLegacy { get; set; }   // Griffin's historical "iis"
    public JsonElement? IssuerStandard { get; set; } // RFC 7519 "iss"
    public JsonElement? IssuedAt { get; set; }
    public JsonElement? NotBefore { get; set; }
    public JsonElement? Expiration { get; set; }

    /// <summary>Whichever issuer field arrived (RFC 7519 "iss" wins over legacy "iis").</summary>
    public JsonElement? Issuer => IssuerStandard ?? IssuerLegacy;

    // ============================================================================================
    // Candidate name lists. Each entry is the canonical form; the matcher normalizes by stripping
    // separators (underscore, hyphen, space, dot) and lowercasing, so an entry like "EmailAddress"
    // also matches "email_address", "EMAIL-ADDRESS", "email.address", "emailaddress", "Email_Address".
    //
    // Add new aliases ONLY after confirming with the Griffin team — adding too many candidates
    // increases the surface for collisions if a future Griffin response happens to use one of
    // the alias names for a different concept.
    // ============================================================================================
    private static readonly string[] EmailAliases =
    {
        "EmailAddress", "Email", "Mail", "Upn", "UserPrincipalName"
    };
    private static readonly string[] UniqueIdAliases =
    {
        "UniqueID", "Uid", "Sub", "UserId", "EmployeeId", "SubjectId"
    };
    private static readonly string[] DisplayNameAliases =
    {
        "DisplayName", "Name", "FullName", "Cn", "CommonName"
    };
    private static readonly string[] GivenNameAliases =
    {
        "GivenName", "FirstName", "First", "Forename"
    };
    private static readonly string[] SurnameAliases =
    {
        "Surname", "LastName", "Last", "FamilyName", "Sn"
    };

    /// <summary>
    /// Parse a Griffin getClaims response body into a DTO using aggressive name normalization.
    /// Returns null if the body isn't a JSON object. The <paramref name="presentKeys"/> out
    /// parameter is always populated with the top-level keys actually seen — even on a successful
    /// parse — so the caller can include them in the error message when a critical field is empty.
    /// </summary>
    public static GriffinClaimsDto? Parse(string body, out IReadOnlyList<string> presentKeys)
    {
        presentKeys = Array.Empty<string>();
        if (string.IsNullOrWhiteSpace(body)) return null;

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            return null;
        }

        using (doc)
        {
            // Try to unwrap a common API envelope. If the root is an object with exactly one
            // property whose value is itself an object AND none of our business-critical aliases
            // appear at the root, descend into that single child. Handles `{"data": {...}}`,
            // `{"claims": {...}}`, `{"result": {...}}` patterns without enumerating envelope
            // names. Only descends ONE level — deeper wrapping would need explicit config.
            var rootElement = doc.RootElement;
            if (rootElement.ValueKind == JsonValueKind.Object)
            {
                var hasBusinessKeyAtRoot = false;
                JsonElement? singleChild = null;
                var propCount = 0;
                foreach (var prop in rootElement.EnumerateObject())
                {
                    propCount++;
                    var normalizedName = Normalize(prop.Name);
                    if (Normalize("EmailAddress") == normalizedName
                        || Normalize("Email") == normalizedName
                        || Normalize("UniqueID") == normalizedName
                        || Normalize("Sub") == normalizedName)
                    {
                        hasBusinessKeyAtRoot = true;
                        break;
                    }
                    if (propCount == 1 && prop.Value.ValueKind == JsonValueKind.Object)
                        singleChild = prop.Value;
                    else
                        singleChild = null; // multiple props — don't unwrap
                }
                if (!hasBusinessKeyAtRoot && singleChild.HasValue && propCount == 1)
                    rootElement = singleChild.Value;
            }

            if (rootElement.ValueKind != JsonValueKind.Object) return null;

            var keys = new List<string>();
            foreach (var prop in rootElement.EnumerateObject())
                keys.Add(prop.Name);
            presentKeys = keys;

            return new GriffinClaimsDto
            {
                UniqueID = ExtractScalar(rootElement, UniqueIdAliases),
                EmailAddress = ExtractScalar(rootElement, EmailAliases),
                DisplayName = ExtractScalar(rootElement, DisplayNameAliases),
                GivenName = ExtractScalar(rootElement, GivenNameAliases),
                Surname = ExtractScalar(rootElement, SurnameAliases),
                Audience = ExtractElement(rootElement, "aud"),
                IssuerLegacy = ExtractElement(rootElement, "iis"),
                IssuerStandard = ExtractElement(rootElement, "iss"),
                IssuedAt = ExtractElement(rootElement, "iat"),
                NotBefore = ExtractElement(rootElement, "nbf"),
                Expiration = ExtractElement(rootElement, "exp")
            };
        }
    }

    // Find the first property whose normalized name matches any normalized alias, and return its
    // scalar string representation. Number/Boolean/Null coerced — matches the prior behavior of
    // JsonNumberOrStringConverter, kept here so callers don't need a converter at all.
    private static string ExtractScalar(JsonElement root, string[] aliases)
    {
        var targets = new HashSet<string>(StringComparer.Ordinal);
        foreach (var a in aliases) targets.Add(Normalize(a));

        foreach (var prop in root.EnumerateObject())
        {
            if (!targets.Contains(Normalize(prop.Name))) continue;
            return ScalarToString(prop.Value);
        }
        return string.Empty;
    }

    // Convert a scalar JsonElement to a string, coercing numbers/bools/null.
    // Uses GetDecimal for the non-Int64 path so extreme-magnitude floats don't render in
    // scientific notation (which would produce confusing UniqueID values like "1.7E+18").
    private static string ScalarToString(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => (value.GetString() ?? string.Empty).Trim(),
        JsonValueKind.Number => value.TryGetInt64(out var l)
            ? l.ToString(CultureInfo.InvariantCulture)
            : value.TryGetDecimal(out var d)
                ? d.ToString(CultureInfo.InvariantCulture)
                : value.GetDouble().ToString("0.################", CultureInfo.InvariantCulture),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Null => string.Empty,
        _ => string.Empty // object/array — don't surface complex types as scalars
    };

    // Capture a JsonElement field whose name matches (case-insensitive ordinal — JWT fields
    // are always lower-case in the spec). Cloned so the value outlives the JsonDocument scope.
    private static JsonElement? ExtractElement(JsonElement root, string name)
    {
        foreach (var prop in root.EnumerateObject())
        {
            if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
                return prop.Value.Clone();
        }
        return null;
    }

    // Normalize a property name for matching: lowercase, ASCII only, separators stripped.
    // "Email_Address" → "emailaddress"
    // "email-address" → "emailaddress"
    // "Email.Address" → "emailaddress"
    internal static string Normalize(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }
}
