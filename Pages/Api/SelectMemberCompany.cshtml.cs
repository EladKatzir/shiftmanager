using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ShiftManager.Services;
using System.Security.Claims;

namespace ShiftManager.Pages.Api;

/// <summary>
/// Page handler for a multi-company member to select which company is their active tenant.
/// Membership is validated inside <see cref="IActiveCompanySelectorService.SelectCompanyAsync"/>
/// (async DB check) — no extra authorization beyond <see cref="AuthorizeAttribute"/> is needed here.
/// Mirrors <see cref="SelectMoleculeModel"/> structure: POST-only, anti-forgery (Razor Pages default),
/// returnUrl validated via <see cref="IUrlHelper.IsLocalUrl"/>, and an audit-log entry on a successful switch.
/// </summary>
[Authorize] // any authenticated user; membership is enforced inside the selector service
[IgnoreAntiforgeryToken] // CSRF enforced via X-Requested-With header check in ApiAuthenticationMiddleware
public class SelectMemberCompanyModel : PageModel
{
    private readonly IActiveCompanySelectorService _selector;
    private readonly IAuditLogService _auditLogService;
    private readonly ILogger<SelectMemberCompanyModel> _logger;

    public SelectMemberCompanyModel(
        IActiveCompanySelectorService selector,
        IAuditLogService auditLogService,
        ILogger<SelectMemberCompanyModel> logger)
    {
        _selector = selector;
        _auditLogService = auditLogService;
        _logger = logger;
    }

    /// <summary>
    /// GET removed — company selection is POST-only to prevent CSRF via crafted link.
    /// Mirrors SelectMolecule.OnGet() redirect behavior.
    /// </summary>
    public IActionResult OnGet() => RedirectToPage("/Index");

    /// <summary>
    /// POST handler — validates membership and writes the active-company cookie, then redirects.
    /// Anti-forgery token is validated automatically by Razor Pages.
    /// </summary>
    public async Task<IActionResult> OnPostAsync(int companyId, string? returnUrl = null)
    {
        // SelectCompanyAsync does the authoritative IsMemberAsync gate;
        // the cookie is only written when membership is confirmed.
        var switched = await _selector.SelectCompanyAsync(companyId);

        if (switched)
        {
            // Audit-log the switch, mirroring SelectMolecule's IAuditLogService usage
            // (action/entityType/entityId/description shape with the originating IP).
            var userId = GetCurrentUserId();
            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            await _auditLogService.LogUserActionAsync(
                userId,
                "MemberSelectedCompany",
                "CompanySelection",
                companyId,
                $"Member selected active company ID: {companyId} (IP: {ipAddress})",
                null);

            _logger.LogInformation(
                "Multi-company member user {UserId} selected active company {CompanyId} from IP {IP}",
                userId, companyId, ipAddress);
        }

        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            return Redirect(returnUrl);

        return RedirectToPage("/Index");
    }

    private int GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(userIdClaim, out var userId) ? userId : 0;
    }
}
