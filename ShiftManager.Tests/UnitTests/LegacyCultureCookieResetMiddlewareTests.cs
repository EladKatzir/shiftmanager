using FluentAssertions;
using Microsoft.AspNetCore.Http;
using ShiftManager.Middleware;

namespace ShiftManager.Tests.UnitTests;

/// <summary>
/// Tests for the one-shot migration middleware that clears stale <c>en-US</c> culture
/// cookies when <c>FF_HEBREW_DEFAULT</c> is enabled. Each test exercises exactly one
/// branch of the decision logic documented on <see cref="LegacyCultureCookieResetMiddleware"/>.
/// </summary>
public class LegacyCultureCookieResetMiddlewareTests
{
    private const string CultureCookie = ".AspNetCore.Culture";
    private const string MarkerCookie = ".culture_explicit";
    private const string LegacyValue = "c=en-US|uic=en-US";
    private const string HebrewValue = "c=he-IL|uic=he-IL";

    private static async Task<(HttpContext ctx, bool nextCalled)> RunMiddlewareAsync(
        IDictionary<string, string>? cookies)
    {
        var ctx = new DefaultHttpContext();
        if (cookies != null)
        {
            var header = string.Join("; ", cookies.Select(kv => $"{kv.Key}={kv.Value}"));
            ctx.Request.Headers["Cookie"] = header;
        }

        bool nextCalled = false;
        var middleware = new LegacyCultureCookieResetMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(ctx);
        return (ctx, nextCalled);
    }

    /// <summary>
    /// Headers produced by <c>Response.Cookies.Append/Delete</c>. Each produces a
    /// <c>Set-Cookie</c> header; this helper makes assertions readable.
    /// </summary>
    private static string[] SetCookieHeaders(HttpContext ctx)
        => ctx.Response.Headers["Set-Cookie"].ToArray()!;

    [Fact]
    public async Task NoCookies_PassesThrough_NoCookieWrites()
    {
        var (ctx, nextCalled) = await RunMiddlewareAsync(cookies: null);

        nextCalled.Should().BeTrue();
        SetCookieHeaders(ctx).Should().BeEmpty("no cookies means the default kicks in for free");
    }

    [Fact]
    public async Task MarkerV2_Present_DoesNotDeleteLegacyCultureCookie()
    {
        var (ctx, nextCalled) = await RunMiddlewareAsync(new Dictionary<string, string>
        {
            [CultureCookie] = LegacyValue,
            [MarkerCookie] = "v2"
        });

        nextCalled.Should().BeTrue();
        SetCookieHeaders(ctx).Should().BeEmpty(
            "explicit v2 marker means the user deliberately chose English — respect it");
    }

    [Fact]
    public async Task MarkerAutoReset_Present_DoesNotReEvaluate()
    {
        var (ctx, nextCalled) = await RunMiddlewareAsync(new Dictionary<string, string>
        {
            [CultureCookie] = LegacyValue,
            [MarkerCookie] = "auto-reset"
        });

        nextCalled.Should().BeTrue();
        SetCookieHeaders(ctx).Should().BeEmpty(
            "any non-empty marker value wins — fixes the v2 vs auto-reset ambiguity QA flagged");
    }

    [Fact]
    public async Task LegacyEnUsCookie_NoMarker_DeletesAndWritesMarker()
    {
        var (ctx, nextCalled) = await RunMiddlewareAsync(new Dictionary<string, string>
        {
            [CultureCookie] = LegacyValue
        });

        nextCalled.Should().BeTrue();

        var setCookies = SetCookieHeaders(ctx);
        setCookies.Should().Contain(h => h.StartsWith($"{CultureCookie}="),
            "legacy cookie must be deleted via a Set-Cookie expiry");
        setCookies.Should().Contain(h => h.StartsWith($"{MarkerCookie}=auto-reset"),
            "marker cookie must be written so we never re-evaluate this user");

        // Request.Cookies should also be stripped for this same request so
        // UseRequestLocalization() — which runs AFTER us — sees no culture cookie.
        ctx.Request.Cookies.ContainsKey(CultureCookie).Should().BeFalse();
    }

    [Fact]
    public async Task HebrewCookie_NoMarker_DoesNotDelete()
    {
        var (ctx, nextCalled) = await RunMiddlewareAsync(new Dictionary<string, string>
        {
            [CultureCookie] = HebrewValue
        });

        nextCalled.Should().BeTrue();
        SetCookieHeaders(ctx).Should().BeEmpty(
            "Hebrew cookies are never legacy-reset — only strict en-US matches trigger the wipe");
    }

    [Fact]
    public async Task MalformedCookie_PassesThrough_NoThrow()
    {
        var (ctx, nextCalled) = await RunMiddlewareAsync(new Dictionary<string, string>
        {
            [CultureCookie] = "not=a|valid cookie value"
        });

        nextCalled.Should().BeTrue();
        SetCookieHeaders(ctx).Should().BeEmpty("malformed cookies fall through without error");
    }

    [Fact]
    public async Task LegacyEnUsCookie_WrittenMarkerCookie_HasCorrectAttributes()
    {
        // Verifies the cookie attributes chosen per QA review: SameSite=Lax, Path=/,
        // HttpOnly=true (server-only), Secure=false (air-gapped HTTP intranet).
        var (ctx, _) = await RunMiddlewareAsync(new Dictionary<string, string>
        {
            [CultureCookie] = LegacyValue
        });

        var markerHeader = SetCookieHeaders(ctx)
            .FirstOrDefault(h => h.StartsWith($"{MarkerCookie}=auto-reset"));
        markerHeader.Should().NotBeNull();
        markerHeader!.Should().Contain("path=/", because: "cookie must apply site-wide");
        markerHeader.Should().Contain("samesite=lax", because: "matches the culture cookie's SameSite policy");
        markerHeader.Should().Contain("httponly", because: "only the server reads this marker");
        markerHeader.Should().NotContain("secure",
            because: "air-gapped intranet deployments may serve over plain HTTP");
    }
}
