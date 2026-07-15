using System.Globalization;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;

namespace ShiftManager.Middleware;

/// <summary>
/// Passively "learns" each user's preferred language. After the request is handled, for an
/// authenticated cookie user it compares the request's resolved UI culture
/// (<see cref="CultureInfo.CurrentUICulture"/>, set by UseRequestLocalization) against the
/// stored <c>AppUser.PreferredLanguage</c> and persists it when it changed.
///
/// Why this exists: background email composition (daily digest, day-before reminders, the
/// 20-unread catch-up) runs with no HTTP request, so there is no culture to inherit. Learning
/// the user's language here gives those jobs a per-user culture to render under.
///
/// Design notes:
///  - Registered AFTER UseAuthentication + UseRequestLocalization so both the user and the
///    resolved culture are available; runs AFTER _next so it never adds latency to the response.
///  - Uses ExecuteUpdateAsync filtered on "value differs" → at most one UPDATE, and only when
///    the language actually changed (steady state = zero writes).
///  - Never throws into the pipeline: any failure is swallowed and logged. A language-learning
///    miss must never break a page load.
/// </summary>
public sealed class PreferredLanguageLearningMiddleware
{
    private readonly RequestDelegate _next;

    public PreferredLanguageLearningMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(
        HttpContext context,
        AppDbContext db,
        ILogger<PreferredLanguageLearningMiddleware> logger)
    {
        await _next(context);

        // An endpoint further down the pipeline (e.g. /Api/My/Language) may have already written
        // PreferredLanguage explicitly on THIS request. LearnAsync persists
        // CultureInfo.CurrentUICulture.Name, which is the REQUEST's resolved culture — still the
        // OLD value on a request that just changed it (a new culture cookie only takes effect on
        // the NEXT request). Running LearnAsync anyway would silently clobber the endpoint's
        // explicit write with that stale culture, so the endpoint owns the preference this request.
        if (context.Items.ContainsKey("LanguageExplicitlySet"))
            return;

        try
        {
            await LearnAsync(context, db, context.RequestAborted);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Never surface a learning failure to the user — log and move on.
            logger.LogWarning(ex, "PreferredLanguage learning failed for the current request");
        }
    }

    /// <summary>
    /// Core decision, separated for testability. Returns true if a row was updated.
    /// </summary>
    public static async Task<bool> LearnAsync(HttpContext context, AppDbContext db, CancellationToken ct)
    {
        var user = context.User;
        if (user?.Identity is not { IsAuthenticated: true })
            return false;

        var idValue = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(idValue, out var userId) || userId <= 0)
            return false;

        var culture = CultureInfo.CurrentUICulture.Name;
        if (string.IsNullOrWhiteSpace(culture))
            return false;

        // SECURITY-AUDITED: SAFE — self-scoped to the authenticated user's own Id (from
        // ClaimTypes.NameIdentifier), only their own PreferredLanguage is updated; IgnoreQueryFilters
        // needed so a switched-company user still updates their own (home-company) row.
        //
        // Single statement: update only the rows where the language differs. Steady state writes
        // nothing. IgnoreQueryFilters is required because db.Users otherwise carries the tenant
        // query filter (CompanyId == GetCurrentTenantId()) — for an owner/multi-company user
        // currently VIEWING a company other than their home one, that filter would scope this
        // query to the viewed company and filter out the caller's own row entirely, silently
        // matching 0 rows.
        var updated = await db.Users
            .IgnoreQueryFilters()
            .Where(u => u.Id == userId && u.PreferredLanguage != culture)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.PreferredLanguage, culture), ct);

        return updated > 0;
    }
}
