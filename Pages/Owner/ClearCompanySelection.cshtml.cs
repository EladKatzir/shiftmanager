using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ShiftManager.Services;

namespace ShiftManager.Pages.Owner;

/// <summary>
/// Page handler for Owner to clear company selection and return to home company.
/// Deletes the owner_selected_company cookie.
/// </summary>
[Authorize(Policy = "Grant:AdminAccess")]
public class ClearCompanySelectionModel : PageModel
{
    private readonly IOwnerCompanySelectorService _ownerCompanySelector;
    private readonly IAuditLogService _auditLogService;
    private readonly ILogger<ClearCompanySelectionModel> _logger;

    public ClearCompanySelectionModel(
        IOwnerCompanySelectorService ownerCompanySelector,
        IAuditLogService auditLogService,
        ILogger<ClearCompanySelectionModel> logger)
    {
        _ownerCompanySelector = ownerCompanySelector;
        _auditLogService = auditLogService;
        _logger = logger;
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        await _ownerCompanySelector.ClearSelectionAsync();

        // Audit log
        await _auditLogService.LogUserActionAsync(
            GetCurrentUserId(),
            "OwnerClearedCompanySelection",
            "CompanySelection",
            null,
            "Owner cleared company selection (returned to home company)",
            null);

        _logger.LogInformation(
            "Owner user {UserId} cleared company selection",
            GetCurrentUserId());

        return Url.IsLocalUrl(returnUrl) ? Redirect(returnUrl) : RedirectToPage("/Owner/Index");
    }

    private int GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(userIdClaim, out var userId) ? userId : 0;
    }
}
