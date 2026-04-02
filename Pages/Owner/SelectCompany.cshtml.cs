using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ShiftManager.Services;

namespace ShiftManager.Pages.Owner;

/// <summary>
/// Page handler for Owner to select which company to manage.
/// Sets cookie with selected company ID for multi-company management.
/// D-03: POST-only (no GET) to prevent CSRF via link. Rate-limited. IP logged.
/// </summary>
[Authorize(Policy = "Grant:AdminAccess")]
public class SelectCompanyModel : PageModel
{
    private readonly IOwnerCompanySelectorService _ownerCompanySelector;
    private readonly IAuditLogService _auditLogService;
    private readonly IRateLimitingService _rateLimiting;
    private readonly ILogger<SelectCompanyModel> _logger;

    public SelectCompanyModel(
        IOwnerCompanySelectorService ownerCompanySelector,
        IAuditLogService auditLogService,
        IRateLimitingService rateLimiting,
        ILogger<SelectCompanyModel> logger)
    {
        _ownerCompanySelector = ownerCompanySelector;
        _auditLogService = auditLogService;
        _rateLimiting = rateLimiting;
        _logger = logger;
    }

    /// <summary>
    /// D-03: GET removed — company switching is POST-only to prevent CSRF via link.
    /// Redirect to Owner hub instead.
    /// </summary>
    public IActionResult OnGet()
    {
        return RedirectToPage("/Owner/Index");
    }

    /// <summary>
    /// POST handler - allows company selection via form submission (anti-forgery token required).
    /// </summary>
    public async Task<IActionResult> OnPostAsync(int companyId, string? returnUrl = null)
    {
        // D-03: Rate limit company switches (10 per 15 minutes per user)
        var userId = GetCurrentUserId();
        var rateLimitKey = $"company-switch:{userId}";
        if (!_rateLimiting.IsAllowed(rateLimitKey, 10, 15))
        {
            _logger.LogWarning("Rate limit exceeded for company switch by user {UserId}", userId);
            TempData["ErrorMessage"] = "Too many company switches. Please wait before trying again.";
            return Url.IsLocalUrl(returnUrl) ? Redirect(returnUrl) : RedirectToPage("/Owner/Index");
        }

        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var success = await _ownerCompanySelector.SelectCompanyAsync(companyId);

        if (success)
        {
            var companyName = await _ownerCompanySelector.GetSelectedCompanyNameAsync();

            // Show context-switch confirmation message (I-02)
            TempData["ContextSwitchMessage"] = $"Switched to: {companyName}";

            // Audit log with IP
            await _auditLogService.LogUserActionAsync(
                userId,
                "OwnerSelectedCompany",
                "CompanySelection",
                companyId,
                $"Owner selected company: {companyName} (IP: {ipAddress})",
                null);

            _logger.LogInformation(
                "Owner user {UserId} selected company {CompanyId} ({CompanyName}) from IP {IP}",
                userId, companyId, companyName, ipAddress);
        }
        else
        {
            _logger.LogWarning(
                "Owner user {UserId} failed to select company {CompanyId} from IP {IP}",
                userId, companyId, ipAddress);
        }

        return Url.IsLocalUrl(returnUrl) ? Redirect(returnUrl) : RedirectToPage("/Owner/Index");
    }

    private int GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(userIdClaim, out var userId) ? userId : 0;
    }
}
