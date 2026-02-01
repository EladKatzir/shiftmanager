using System.Security.Claims;

namespace ShiftManager.Services;

/// <summary>
/// ✅ PHASE 20: Cookie-based user preference management
/// </summary>
public class UserPreferenceService : IUserPreferenceService
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private const string ShowMyItemsOnlyCookieName = "user_show_my_items_only";

    public UserPreferenceService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public bool GetShowMyItemsOnly()
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext == null)
            return GetDefaultValue(null);

        // Check if cookie exists
        if (httpContext.Request.Cookies.TryGetValue(ShowMyItemsOnlyCookieName, out var value))
        {
            if (bool.TryParse(value, out var showMyItemsOnly))
            {
                return showMyItemsOnly;
            }
        }

        // No cookie found - return role-based default
        var role = httpContext.User.FindFirst(ClaimTypes.Role)?.Value;
        return GetDefaultValue(role);
    }

    public void SetShowMyItemsOnly(bool showMyItemsOnly)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext != null)
        {
            httpContext.Response.Cookies.Append(ShowMyItemsOnlyCookieName, showMyItemsOnly.ToString(), new CookieOptions
            {
                MaxAge = TimeSpan.FromDays(30), // Persist for 30 days
                HttpOnly = true,
                SameSite = SameSiteMode.Strict
            });
        }
    }

    /// <summary>
    /// Get default value based on user role
    /// Employees/Trainees: true (show only their items)
    /// Managers/Directors/Owners: false (show all items)
    /// </summary>
    private bool GetDefaultValue(string? role)
    {
        if (string.IsNullOrEmpty(role))
            return true; // Default to "my items only" for unknown roles

        // Managers and above see all items by default
        if (role == "Owner" || role == "Director" || role == "Manager")
            return false;

        // Employees and Trainees see only their items by default
        return true;
    }
}
