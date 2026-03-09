using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;

namespace ShiftManager.Services;

/// <summary>
/// Implementation of Owner company selector service.
/// Manages cookie-based company selection for Owner role with 12-hour expiry.
/// </summary>
public class OwnerCompanySelectorService : IOwnerCompanySelectorService
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly AppDbContext _db;
    private readonly ILogger<OwnerCompanySelectorService> _logger;
    private readonly ICompanyCacheService _companyCacheService;
    private const string CookieName = "owner_selected_company";

    public OwnerCompanySelectorService(
        IHttpContextAccessor httpContextAccessor,
        AppDbContext db,
        ILogger<OwnerCompanySelectorService> logger,
        ICompanyCacheService companyCacheService)
    {
        _httpContextAccessor = httpContextAccessor;
        _db = db;
        _logger = logger;
        _companyCacheService = companyCacheService;
    }

    public bool IsOwner()
    {
        // Identity check via claims — no database I/O needed.
        // This checks organizational identity (who is this user?), not authorization (can they do X?).
        // Per design doc Section 11, identity checks stay on AppUser.Role / claims.
        var user = _httpContextAccessor.HttpContext?.User;
        return user?.IsInRole("Owner") == true;
    }

    public int? GetSelectedCompanyId()
    {
        if (!IsOwner())
        {
            return null;
        }

        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext?.Request.Cookies.TryGetValue(CookieName, out var value) == true
            && int.TryParse(value, out var companyId))
        {
            _logger.LogDebug("Owner has selected company {CompanyId} from cookie", companyId);
            return companyId;
        }

        return null;
    }

    public async Task<bool> SelectCompanyAsync(int companyId)
    {
        if (!IsOwner())
        {
            _logger.LogWarning("Non-Owner user attempted to select company {CompanyId}", companyId);
            return false;
        }

        // Verify company exists
        var company = await _companyCacheService.GetCompanyAsync(companyId);
        if (company == null)
        {
            _logger.LogWarning("Owner attempted to select non-existent company {CompanyId}", companyId);
            return false;
        }

        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext != null)
        {
            httpContext.Response.Cookies.Append(CookieName, companyId.ToString(), new CookieOptions
            {
                MaxAge = TimeSpan.FromHours(12), // Longer than Director's 8 hours
                HttpOnly = true,                 // Prevent JavaScript access (XSS protection)
                SameSite = SameSiteMode.Strict,  // CSRF protection
                Secure = httpContext.Request.IsHttps, // HTTPS only in production
                Path = "/"                       // Available site-wide
            });

            _logger.LogInformation(
                "Owner user {UserId} selected company {CompanyId} ({CompanyName})",
                GetCurrentUserId(), companyId, company.Name);
        }

        return true;
    }

    public Task ClearSelectionAsync()
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext != null)
        {
            var hadSelection = httpContext.Request.Cookies.ContainsKey(CookieName);

            httpContext.Response.Cookies.Delete(CookieName);

            if (hadSelection)
            {
                _logger.LogInformation(
                    "Owner user {UserId} cleared company selection",
                    GetCurrentUserId());
            }
        }

        return Task.CompletedTask;
    }

    public async Task<string?> GetSelectedCompanyNameAsync()
    {
        var companyId = GetSelectedCompanyId();
        if (companyId == null)
        {
            return null;
        }

        var company = await _companyCacheService.GetCompanyAsync(companyId.Value);
        return company?.LocalizedName;
    }

    public int? GetHomeCompanyId()
    {
        var user = _httpContextAccessor.HttpContext?.User;
        if (user == null)
        {
            return null;
        }

        var companyIdClaim = user.FindFirst("CompanyId")?.Value;
        if (companyIdClaim != null && int.TryParse(companyIdClaim, out var companyId))
        {
            _logger.LogDebug("Owner home company ID: {CompanyId}", companyId);
            return companyId;
        }

        return null;
    }

    private int GetCurrentUserId()
    {
        var userIdClaim = _httpContextAccessor.HttpContext?.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(userIdClaim, out var userId) ? userId : 0;
    }
}
