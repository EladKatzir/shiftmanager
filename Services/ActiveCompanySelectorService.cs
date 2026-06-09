using System.Security.Claims;

namespace ShiftManager.Services;

public sealed class ActiveCompanySelectorService : IActiveCompanySelectorService
{
    public const string CookieName = "member_selected_company";
    private readonly IHttpContextAccessor _http;
    private readonly ICompanyMembershipService _memberships;

    public ActiveCompanySelectorService(IHttpContextAccessor http, ICompanyMembershipService memberships)
    {
        _http = http;
        _memberships = memberships;
    }

    public int? GetSelectedCompanyId()
    {
        var ctx = _http.HttpContext;
        if (ctx?.Request.Cookies.TryGetValue(CookieName, out var v) == true && int.TryParse(v, out var id))
            return id;
        return null;
    }

    public async Task<bool> SelectCompanyAsync(int companyId)
    {
        var ctx = _http.HttpContext;
        if (ctx == null) return false;
        var userId = GetUserId(ctx);
        if (userId == null) return false;

        if (!await _memberships.IsMemberAsync(userId.Value, companyId))
            return false; // authoritative gate — never trust the client

        ctx.Response.Cookies.Append(CookieName, companyId.ToString(), new CookieOptions
        {
            MaxAge = TimeSpan.FromHours(12),
            HttpOnly = true,
            SameSite = SameSiteMode.Strict,
            Secure = ctx.Request.IsHttps,
            Path = "/"
        });
        return true;
    }

    public Task ClearSelectionAsync()
    {
        _http.HttpContext?.Response.Cookies.Delete(CookieName);
        return Task.CompletedTask;
    }

    private static int? GetUserId(HttpContext ctx)
    {
        var raw = ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(raw, out var id) ? id : (int?)null;
    }
}
