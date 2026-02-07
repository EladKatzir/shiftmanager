using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ShiftManager.Services;

namespace ShiftManager.Pages.Owner;

/// <summary>
/// Page handler for Owner to select which company to manage.
/// Sets cookie with selected company ID for multi-company management.
/// </summary>
[Authorize(Policy = "Grant:AdminAccess")]
public class SelectCompanyModel : PageModel
{
    private readonly IOwnerCompanySelectorService _ownerCompanySelector;
    private readonly IAuditLogService _auditLogService;
    private readonly ILogger<SelectCompanyModel> _logger;

    public SelectCompanyModel(
        IOwnerCompanySelectorService ownerCompanySelector,
        IAuditLogService auditLogService,
        ILogger<SelectCompanyModel> logger)
    {
        _ownerCompanySelector = ownerCompanySelector;
        _auditLogService = auditLogService;
        _logger = logger;
    }

    /// <summary>
    /// GET handler - allows company selection via URL (e.g., from context switcher).
    /// </summary>
    public async Task<IActionResult> OnGetAsync(int companyId, string? returnUrl = null)
    {
        return await SelectCompanyAsync(companyId, returnUrl);
    }

    /// <summary>
    /// POST handler - allows company selection via form submission.
    /// </summary>
    public async Task<IActionResult> OnPostAsync(int companyId, string? returnUrl = null)
    {
        return await SelectCompanyAsync(companyId, returnUrl);
    }

    private async Task<IActionResult> SelectCompanyAsync(int companyId, string? returnUrl)
    {
        var success = await _ownerCompanySelector.SelectCompanyAsync(companyId);

        if (success)
        {
            var companyName = await _ownerCompanySelector.GetSelectedCompanyNameAsync();

            // Show context-switch confirmation message (I-02)
            TempData["ContextSwitchMessage"] = $"Switched to: {companyName}";

            // Audit log
            await _auditLogService.LogUserActionAsync(
                GetCurrentUserId(),
                "OwnerSelectedCompany",
                "CompanySelection",
                companyId,
                $"Owner selected company: {companyName}",
                null);

            _logger.LogInformation(
                "Owner user {UserId} selected company {CompanyId} ({CompanyName})",
                GetCurrentUserId(), companyId, companyName);
        }
        else
        {
            _logger.LogWarning(
                "Owner user {UserId} failed to select company {CompanyId}",
                GetCurrentUserId(), companyId);
        }

        return Redirect(returnUrl ?? "/Owner/Index");
    }

    private int GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(userIdClaim, out var userId) ? userId : 0;
    }
}
