using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Localization;

namespace ShiftManager.Services;

/// <summary>
/// Centralises request-localization (culture) configuration so the provider ordering can be unit
/// tested. See <see cref="RequestLocalizationSetupTests"/> for the behaviour this guarantees.
/// </summary>
public static class RequestLocalizationSetup
{
    /// <summary>Cultures the application ships translations for.</summary>
    public static readonly string[] SupportedCultures = { "en-US", "he-IL" };

    /// <summary>
    /// Configures <paramref name="options"/> for the given Hebrew-default state.
    /// </summary>
    /// <param name="options">The options to mutate.</param>
    /// <param name="hebrewDefaultEnabled">
    /// When true (FF_HEBREW_DEFAULT on) the default culture is he-IL and the browser's
    /// Accept-Language header is intentionally NOT consulted, so a user without an explicit
    /// query-string/cookie choice falls through to the Hebrew default instead of resolving en-US
    /// from an English browser. When false the default is en-US and the Accept-Language provider is
    /// registered as before.
    /// </param>
    public static void Configure(RequestLocalizationOptions options, bool hebrewDefaultEnabled)
    {
        options.DefaultRequestCulture = new RequestCulture(hebrewDefaultEnabled ? "he-IL" : "en-US");
        options.SupportedCultures = SupportedCultures.Select(c => new CultureInfo(c)).ToList();
        options.SupportedUICultures = SupportedCultures.Select(c => new CultureInfo(c)).ToList();

        options.RequestCultureProviders.Clear();
        // 1. Explicit ?culture= override. 2. The .AspNetCore.Culture cookie (the manual toggle).
        options.RequestCultureProviders.Add(new QueryStringRequestCultureProvider());
        options.RequestCultureProviders.Add(new CookieRequestCultureProvider());
        // 3. Browser Accept-Language — only when there is NO Hebrew org default. Otherwise it would
        //    resolve en-US for English browsers and pre-empt the he-IL DefaultRequestCulture below,
        //    which is exactly why the Hebrew default never took effect for fresh (cookieless) users.
        if (!hebrewDefaultEnabled)
            options.RequestCultureProviders.Add(new AcceptLanguageHeaderRequestCultureProvider());
    }
}
