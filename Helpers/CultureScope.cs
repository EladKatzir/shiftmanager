using System.Globalization;

namespace ShiftManager.Helpers;

/// <summary>
/// Temporarily switches the ambient culture (both <see cref="CultureInfo.CurrentCulture"/> and
/// <see cref="CultureInfo.CurrentUICulture"/>) for the lifetime of the scope, restoring the
/// previous values on dispose. Used by background email composition (which has no request
/// culture) to render each recipient's email under their learned <c>PreferredLanguage</c>.
///
/// A null/blank/unrecognized culture name is a no-op — the ambient culture is left untouched,
/// so callers can pass a possibly-null preference and fall back to whatever default the caller
/// (e.g. the background job) already established.
/// </summary>
public sealed class CultureScope : IDisposable
{
    private readonly CultureInfo _previousCulture;
    private readonly CultureInfo _previousUiCulture;

    public CultureScope(string? cultureName)
    {
        _previousCulture = CultureInfo.CurrentCulture;
        _previousUiCulture = CultureInfo.CurrentUICulture;

        if (string.IsNullOrWhiteSpace(cultureName))
            return;

        try
        {
            var culture = CultureInfo.GetCultureInfo(cultureName);

            // ICU-based .NET does NOT throw for many malformed names — it leniently parses the
            // primary subtag (e.g. "not-a-culture-xyz" → a culture named "not"). Guard by
            // requiring the resolved culture's Name to match the requested name (case-insensitive);
            // otherwise treat it as unknown and leave the ambient culture untouched.
            if (!string.Equals(culture.Name, cultureName, StringComparison.OrdinalIgnoreCase))
                return;

            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
        }
        catch (CultureNotFoundException)
        {
            // Unknown culture name → keep the ambient culture (no-op).
        }
    }

    public void Dispose()
    {
        CultureInfo.CurrentCulture = _previousCulture;
        CultureInfo.CurrentUICulture = _previousUiCulture;
    }
}
