using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ShiftManager.Services;
using System.Security.Claims;

namespace ShiftManager.Pages.Api;

/// <summary>
/// Page handler for Director to select which molecule to view.
/// Sets cookie with selected molecule ID for molecule-level context switching.
/// Mirrors the Owner/SelectCompany pattern but operates at molecule level.
/// </summary>
[Authorize(Policy = "Grant:DirectorHubAccess")]
public class SelectMoleculeModel : PageModel
{
    private readonly IGrantService _grantService;
    private readonly IAuditLogService _auditLogService;
    private readonly IRateLimitingService _rateLimiting;
    private readonly ILogger<SelectMoleculeModel> _logger;
    private const string CookieName = "director_selected_molecule";

    public SelectMoleculeModel(
        IGrantService grantService,
        IAuditLogService auditLogService,
        IRateLimitingService rateLimiting,
        ILogger<SelectMoleculeModel> logger)
    {
        _grantService = grantService;
        _auditLogService = auditLogService;
        _rateLimiting = rateLimiting;
        _logger = logger;
    }

    /// <summary>
    /// GET removed — molecule switching is POST-only to prevent CSRF via link.
    /// </summary>
    public IActionResult OnGet()
    {
        return RedirectToPage("/Admin/Users");
    }

    /// <summary>
    /// POST handler - allows molecule selection via form submission (anti-forgery token required).
    /// </summary>
    public async Task<IActionResult> OnPostAsync(int moleculeId, string? returnUrl = null)
    {
        var userId = GetCurrentUserId();

        // Rate limit molecule switches (10 per 15 minutes per user)
        var rateLimitKey = $"molecule-switch:{userId}";
        if (!_rateLimiting.IsAllowed(rateLimitKey, 10, 15))
        {
            _logger.LogWarning("Rate limit exceeded for molecule switch by user {UserId}", userId);
            TempData["Error"] = "Too many molecule switches. Please wait before trying again.";
            return Url.IsLocalUrl(returnUrl) ? Redirect(returnUrl) : RedirectToPage("/Admin/Users");
        }

        // Validate the molecule is in the director's accessible list
        var accessibleMoleculeIds = await _grantService
            .GetAccessibleMoleculeIdsForGrantAsync(userId, "DirectorHubAccess");

        if (!accessibleMoleculeIds.Contains(moleculeId))
        {
            _logger.LogWarning(
                "Director user {UserId} attempted to select unauthorized molecule {MoleculeId}",
                userId, moleculeId);
            TempData["Error"] = "Access denied to the selected molecule.";
            return Url.IsLocalUrl(returnUrl) ? Redirect(returnUrl) : RedirectToPage("/Admin/Users");
        }

        // Set the cookie
        var httpContext = HttpContext;
        httpContext.Response.Cookies.Append(CookieName, moleculeId.ToString(), new CookieOptions
        {
            MaxAge = TimeSpan.FromHours(12),
            HttpOnly = true,
            SameSite = SameSiteMode.Strict,
            Secure = httpContext.Request.IsHttps,
            Path = "/"
        });

        var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        // Audit log
        await _auditLogService.LogUserActionAsync(
            userId,
            "DirectorSelectedMolecule",
            "MoleculeSelection",
            moleculeId,
            $"Director selected molecule ID: {moleculeId} (IP: {ipAddress})",
            null);

        _logger.LogInformation(
            "Director user {UserId} selected molecule {MoleculeId} from IP {IP}",
            userId, moleculeId, ipAddress);

        TempData["ContextSwitchMessage"] = $"Switched molecule context";

        return Url.IsLocalUrl(returnUrl) ? Redirect(returnUrl) : RedirectToPage("/Admin/Users");
    }

    private int GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(userIdClaim, out var userId) ? userId : 0;
    }
}
