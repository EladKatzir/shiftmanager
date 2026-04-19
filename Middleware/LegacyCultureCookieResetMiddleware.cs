namespace ShiftManager.Middleware;

/// <summary>
/// One-shot migration middleware that removes stale <c>.AspNetCore.Culture=en-US</c> cookies
/// left over from the era when <c>en-US</c> was the app's default culture. Runs BEFORE
/// <c>UseRequestLocalization()</c> so removing the cookie on the same request causes the
/// configured <c>DefaultRequestCulture</c> (now <c>he-IL</c>) to take effect immediately.
///
/// Gate logic (any branch 1 wins — do nothing):
/// 1. If the marker cookie <c>.culture_explicit</c> exists with ANY non-empty value, the
///    user's language intent has already been recorded (either via a deliberate toggle or
///    a prior auto-reset). Leave everything alone.
/// 2. Else if <c>.AspNetCore.Culture</c> parses to <c>en-US</c> for both <c>c=</c> and
///    <c>uic=</c>, treat it as a legacy auto-placed cookie: delete it and write
///    <c>.culture_explicit=auto-reset</c> so future requests skip branch 2.
/// 3. Otherwise do nothing (no cookie, or Hebrew cookie — both already resolve correctly).
///
/// Only wired up when <c>FeatureFlags:HebrewDefault</c> is <c>true</c>.
/// </summary>
public class LegacyCultureCookieResetMiddleware
{
    private const string CultureCookieName = ".AspNetCore.Culture";
    private const string MarkerCookieName = ".culture_explicit";
    private const string LegacyCookieValue = "c=en-US|uic=en-US";
    private const string AutoResetMarkerValue = "auto-reset";

    private readonly RequestDelegate _next;

    public LegacyCultureCookieResetMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public Task InvokeAsync(HttpContext context)
    {
        // Branch 1 — explicit marker wins. Any non-empty value respects recorded intent.
        var marker = context.Request.Cookies[MarkerCookieName];
        if (!string.IsNullOrEmpty(marker))
        {
            return _next(context);
        }

        // Branch 2 — strict match on the legacy en-US value.
        var culture = context.Request.Cookies[CultureCookieName];
        if (string.Equals(culture, LegacyCookieValue, StringComparison.Ordinal))
        {
            // Delete the legacy cookie (must match the options the provider used to set it,
            // otherwise browsers keep the original cookie and add a stray deletion cookie).
            context.Response.Cookies.Delete(CultureCookieName, new CookieOptions { Path = "/" });

            // Remove it from the CURRENT request too — UseRequestLocalization() runs after us
            // and re-reads Request.Cookies. Without this the legacy value would still win
            // for this one request even though we deleted it for the next one.
            context.Request.Cookies = new RequestCookieCollection(
                context.Request.Cookies
                    .Where(kv => !string.Equals(kv.Key, CultureCookieName, StringComparison.Ordinal))
                    .ToDictionary(kv => kv.Key, kv => kv.Value));

            // Record intent so we don't re-evaluate this user on every request.
            context.Response.Cookies.Append(MarkerCookieName, AutoResetMarkerValue, new CookieOptions
            {
                Path = "/",
                HttpOnly = true,
                SameSite = SameSiteMode.Lax,
                Secure = false, // air-gapped intranet may serve over HTTP
                Expires = DateTimeOffset.UtcNow.AddYears(1)
            });
        }

        // Branch 3 — no cookie or Hebrew cookie: default handles it.
        return _next(context);
    }

    /// <summary>
    /// Minimal <see cref="IRequestCookieCollection"/> wrapper so the middleware can rewrite
    /// <c>Request.Cookies</c> in-place. ASP.NET's default impl is internal and sealed.
    /// </summary>
    private sealed class RequestCookieCollection : IRequestCookieCollection
    {
        private readonly IDictionary<string, string> _inner;
        public RequestCookieCollection(IDictionary<string, string> inner) { _inner = inner; }
        public string? this[string key] => _inner.TryGetValue(key, out var v) ? v : null;
        public int Count => _inner.Count;
        public ICollection<string> Keys => _inner.Keys;
        public bool ContainsKey(string key) => _inner.ContainsKey(key);
        public bool TryGetValue(string key, [System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out string value)
        {
            var ok = _inner.TryGetValue(key, out var v);
            value = v!;
            return ok;
        }
        public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => _inner.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _inner.GetEnumerator();
    }
}
