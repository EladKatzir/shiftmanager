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

    public async Task<IActionResult> OnPostAsync(int companyId, string? returnUrl = null)
    {
        var success = await _ownerCompanySelector.SelectCompanyAsync(companyId);

        if (success)
        {
            var companyName = await _ownerCompanySelector.GetSelectedCompanyNameAsync();

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
