using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using ShiftManager.Data;
using ShiftManager.Resources;
using ShiftManager.Services;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace ShiftManager.ViewComponents;

/// <summary>
/// ViewComponent for the Scope Switcher UI that allows users to switch between organizational scopes.
/// Integrates with /Api/ScopeSwitcher endpoint for scope data.
/// </summary>
public class ScopeSwitcherViewComponent : ViewComponent
{
    private readonly IStringLocalizer<SharedResources> _localizer;
    private readonly AppDbContext _context;
    private readonly IGrantService _grantService;

    public ScopeSwitcherViewComponent(
        IStringLocalizer<SharedResources> localizer,
        AppDbContext context,
        IGrantService grantService)
    {
        _localizer = localizer;
        _context = context;
        _grantService = grantService;
    }

    /// <summary>
    /// Invokes the Scope Switcher component.
    /// </summary>
    /// <param name="currentScope">The current scope (defaults to "company" or reads from URL/cookie)</param>
    /// <param name="calendarType">The calendar type context (e.g., "shifts", "chores")</param>
    public async Task<IViewComponentResult> InvokeAsync(string? currentScope = null, string calendarType = "shifts")
    {
        var user = HttpContext.User;
        if (!user.Identity?.IsAuthenticated ?? true)
        {
            return Content(string.Empty);
        }

        var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
        {
            return Content(string.Empty);
        }

        // Determine the current scope from URL query string, cookie, or default
        var resolvedScope = ResolveCurrentScope(currentScope, calendarType);

        // Determine which scopes are available based on grants
        var scopes = new List<ScopeOption>
        {
            // Everyone can see their own shifts
            new ScopeOption
            {
                Key = "mine",
                Label = _localizer["ScopeSwitcher_MineOnly"],
                Icon = "👤",
                IsAvailable = true,
                IsActive = resolvedScope == "mine"
            },
            // Company view is default for most users
            new ScopeOption
            {
                Key = "company",
                Label = _localizer["ScopeSwitcher_MyCompany"],
                Icon = "🏢",
                IsAvailable = true,
                IsActive = resolvedScope == "company"
            }
        };

        // Check for Molecule-level view grant
        // Note: Grant-based only; role fallbacks removed for proper grant-based authorization
        var hasMoleculeGrant = await _grantService.HasGrantAsync(userId, $"View{calendarType.ToTitleCase()}Molecule");
        if (hasMoleculeGrant)
        {
            scopes.Add(new ScopeOption
            {
                Key = "molecule",
                Label = _localizer["ScopeSwitcher_FullMolecule"],
                Icon = "👥",
                IsAvailable = true,
                IsActive = resolvedScope == "molecule"
            });
        }

        // Check for Area-level view grant
        // Note: Grant-based only; role fallbacks removed for proper grant-based authorization
        var hasAreaGrant = await _grantService.HasGrantAsync(userId, $"View{calendarType.ToTitleCase()}Area");
        if (hasAreaGrant)
        {
            scopes.Add(new ScopeOption
            {
                Key = "area",
                Label = _localizer["ScopeSwitcher_FullArea"],
                Icon = "🌍",
                IsAvailable = true,
                IsActive = resolvedScope == "area"
            });
        }

        // Validate that the resolved scope is available; if not, default to first available
        if (!scopes.Any(s => s.Key == resolvedScope && s.IsAvailable))
        {
            resolvedScope = scopes.FirstOrDefault(s => s.IsAvailable)?.Key ?? "company";
            foreach (var scope in scopes)
            {
                scope.IsActive = scope.Key == resolvedScope;
            }
        }

        var model = new ScopeSwitcherViewModel
        {
            Scopes = scopes,
            CurrentScope = resolvedScope,
            CalendarType = calendarType
        };

        return View(model);
    }

    /// <summary>
    /// Resolves the current scope from URL query string, cookie, or defaults.
    /// Priority: URL query > Cookie > Parameter > Default ("company")
    /// </summary>
    private string ResolveCurrentScope(string? parameterScope, string calendarType)
    {
        // 1. Check URL query string first (highest priority)
        if (HttpContext.Request.Query.TryGetValue("scope", out var queryScope) && !string.IsNullOrEmpty(queryScope))
        {
            return queryScope.ToString().ToLowerInvariant();
        }

        // 2. Check cookie for saved preference
        var cookieKey = $"ShiftyScope_{calendarType}";
        if (HttpContext.Request.Cookies.TryGetValue(cookieKey, out var cookieScope) && !string.IsNullOrEmpty(cookieScope))
        {
            return cookieScope.ToLowerInvariant();
        }

        // 3. Use parameter if provided
        if (!string.IsNullOrEmpty(parameterScope))
        {
            return parameterScope.ToLowerInvariant();
        }

        // 4. Default to company view
        return "company";
    }
}

public class ScopeSwitcherViewModel
{
    public List<ScopeOption> Scopes { get; set; } = new();
    public string CurrentScope { get; set; } = "company";
    public string CalendarType { get; set; } = "shifts";
}

public class ScopeOption
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public string Icon { get; set; } = "";
    public bool IsAvailable { get; set; }
    public bool IsActive { get; set; }
}

public static class StringExtensions
{
    public static string ToTitleCase(this string str)
    {
        if (string.IsNullOrEmpty(str))
            return str;

        return char.ToUpper(str[0]) + str.Substring(1).ToLower();
    }
}
