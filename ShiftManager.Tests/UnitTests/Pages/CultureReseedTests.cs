using FluentAssertions;
using Microsoft.AspNetCore.Http;
using ShiftManager.Pages.Auth;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Pages;

/// <summary>
/// Tests for <see cref="LoginModel.ReseedCultureCookie"/> — the login-time re-seed that restores
/// a user's stored <c>AppUser.PreferredLanguage</c> into the <c>.AspNetCore.Culture</c> cookie
/// (#10: "switched UI to Hebrew, later it reverted to English"). Root cause: nothing re-seeded
/// the cookie from the DB value, so a fresh/cookieless browser never saw it.
///
/// Exercised directly against a <see cref="DefaultHttpContext"/> — the two call sites
/// (Login.cshtml.cs OnPostAsync, GriffinCallback.cshtml.cs OnGetAsync) are covered by browser
/// verification since they require a full authenticated request pipeline.
/// </summary>
public class CultureReseedTests
{
    private const string CultureCookie = ".AspNetCore.Culture";
    private const string MarkerCookie = ".culture_explicit";

    private static string[] SetCookieHeaders(HttpContext ctx)
        => ctx.Response.Headers["Set-Cookie"].ToArray()!;

    /// <summary>
    /// Response.Cookies.Append URL-encodes the cookie value (Uri.EscapeDataString internally),
    /// so the raw Set-Cookie header contains "c%3Dhe-IL%7Cuic%3Dhe-IL", not "c=he-IL|uic=he-IL".
    /// This round-trips correctly through ASP.NET's own Request.Cookies[...] reader (which
    /// decodes on the way in) — exactly how CookieRequestCultureProvider itself always consumes
    /// it — so it is not a bug. Decode here so the test asserts the meaningful, documented
    /// MakeCookieValue format instead of this incidental transport-encoding detail.
    /// </summary>
    private static string DecodedCookieValue(HttpContext ctx, string cookieName)
    {
        var header = SetCookieHeaders(ctx).First(h => h.StartsWith($"{cookieName}="));
        var rawValue = header[(cookieName.Length + 1)..].Split(';')[0];
        return Uri.UnescapeDataString(rawValue);
    }

    [Fact]
    public void ReseedCultureCookie_WritesAspNetCultureAndExplicitMarker()
    {
        var ctx = new DefaultHttpContext();

        LoginModel.ReseedCultureCookie(ctx, "he-IL");

        var setCookies = SetCookieHeaders(ctx);
        setCookies.Should().Contain(h => h.StartsWith($"{CultureCookie}="),
            "the culture cookie must be (re)written from PreferredLanguage");
        DecodedCookieValue(ctx, CultureCookie).Should().Contain("c=he-IL|uic=he-IL",
            "MakeCookieValue must produce the standard c=..|uic=.. format");
        setCookies.Should().Contain(h => h.StartsWith($"{MarkerCookie}="),
            "the explicit marker must accompany the re-seed so LegacyCultureCookieResetMiddleware won't strip it");
    }

    [Fact]
    public void ReseedCultureCookie_EnglishCulture_WritesBothCookiesWithMarker()
    {
        // The en-US case is the one that actually matters for LegacyCultureCookieResetMiddleware:
        // an unmarked en-US cookie is treated as a legacy auto-placed artifact and stripped.
        var ctx = new DefaultHttpContext();

        LoginModel.ReseedCultureCookie(ctx, "en-US");

        DecodedCookieValue(ctx, CultureCookie).Should().Contain("c=en-US|uic=en-US");
        SetCookieHeaders(ctx).Should().Contain(h => h.StartsWith($"{MarkerCookie}="),
            "without the marker, a re-seeded en-US would be wiped again on the very next request");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ReseedCultureCookie_NullOrEmpty_WritesNoCookies(string? culture)
    {
        var ctx = new DefaultHttpContext();

        LoginModel.ReseedCultureCookie(ctx, culture);

        SetCookieHeaders(ctx).Should().BeEmpty("no stored preference means nothing to re-seed");
    }

    [Fact]
    public void ReseedCultureCookie_UnsupportedCulture_WritesNoCookies()
    {
        var ctx = new DefaultHttpContext();

        // A value outside RequestLocalizationSetup.SupportedCultures must never reach
        // RequestCulture(...) — CultureNotFoundException would fail sign-in outright for a
        // user with a stale/corrupted PreferredLanguage value.
        LoginModel.ReseedCultureCookie(ctx, "xx-XX");

        SetCookieHeaders(ctx).Should().BeEmpty("unsupported/garbage culture values must be ignored, not thrown");
    }

    [Fact]
    public void ReseedCultureCookie_CultureCookieOptions_AreHttpOnlyFalseAndPathRoot()
    {
        // HttpOnly=false is load-bearing: localization-api.js and both JS toggles read
        // document.cookie('.AspNetCore.Culture') directly.
        var ctx = new DefaultHttpContext();

        LoginModel.ReseedCultureCookie(ctx, "he-IL");

        var cultureHeader = SetCookieHeaders(ctx).First(h => h.StartsWith($"{CultureCookie}="));
        cultureHeader.Should().Contain("path=/");
        cultureHeader.Should().NotContain("httponly", "JS must be able to read this cookie");
    }

    [Fact]
    public void ReseedCultureCookie_HttpsRequest_SetsSecureFlag()
    {
        // Secure must track the request scheme (air-gapped intranet deployments may be plain
        // HTTP, where Secure would make the browser silently refuse to store the cookie at all).
        var ctx = new DefaultHttpContext();
        ctx.Request.Scheme = "https";

        LoginModel.ReseedCultureCookie(ctx, "he-IL");

        var cultureHeader = SetCookieHeaders(ctx).First(h => h.StartsWith($"{CultureCookie}="));
        cultureHeader.Should().Contain("secure");
    }
}
