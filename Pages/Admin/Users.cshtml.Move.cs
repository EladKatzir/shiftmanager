using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ShiftManager.Pages.Admin;

// Handlers for moving a user between companies. Authorization lives HERE (needs HttpContext for
// CanAssignRoleAsync); the transactional data work lives in IUserCompanyTransferService.
public partial class UsersModel
{
    /// <summary>Read-only impact preview for the move modal (AJAX). Returns JSON.</summary>
    public async Task<IActionResult> OnGetMoveImpactAsync(int userId, int destCompanyId)
    {
        if (!int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var adminId))
            return new JsonResult(new { ok = false, error = "unauthorized" }) { StatusCode = 401 };

        var authError = await AuthorizeMoveAsync(adminId, userId, destCompanyId);
        if (authError != null)
            return new JsonResult(new { ok = false, error = _localizer[authError].Value });

        var impact = await _userCompanyTransferService.GetMoveImpactAsync(userId, destCompanyId);
        return new JsonResult(new { ok = true, impact });
    }

    /// <summary>Performs the move after re-checking authorization server-side.</summary>
    public async Task<IActionResult> OnPostMoveUserAsync(int userId, int destCompanyId)
    {
        if (!int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var adminId))
            return Forbid();

        var authError = await AuthorizeMoveAsync(adminId, userId, destCompanyId);
        if (authError != null)
        {
            TempData["ErrorMessage"] = _localizer[authError].Value;
            return RedirectToPage();
        }

        var result = await _userCompanyTransferService.MoveUserToCompanyAsync(userId, destCompanyId, adminId);
        if (result.Success)
            TempData["SuccessMessage"] = _localizer["Users_MoveSuccess"].Value;
        else
            TempData["ErrorMessage"] = _localizer[result.ErrorKey ?? "Error_MoveFailed"].Value;

        return RedirectToPage();
    }

    /// <summary>
    /// Returns a localization key describing the failure, or null when the move is authorized.
    /// Gate: admin must hold EditCompanyUsers for BOTH source and destination (yielding the
    /// molecule/area/owner matrix), AND be permitted to assign the target user's role in the
    /// destination (CanAssignRoleAsync — same gate role-changes use, blocks downgrading a higher admin).
    /// </summary>
    private async Task<string?> AuthorizeMoveAsync(int adminId, int userId, int destCompanyId)
    {
        if (userId == adminId) return "Error_MoveSelf";

        // SECURITY-AUDITED: load move target by explicit id (cross-tenant admin view).
        var target = await _db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == userId);
        if (target == null) return "Error_UserNotFound";
        if (target.CompanyId == destCompanyId) return "Error_MoveSameCompany";

        // SECURITY-AUDITED: validate destination by explicit id.
        var dest = await _db.Companies.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == destCompanyId);
        if (dest == null) return "Error_CompanyNotFound";
        if (dest.IsHeadquarters) return "Error_MoveDestHq";

        var isAdmin = await _grantService.HasGrantAsync(adminId, "AdminAccess");
        if (!isAdmin)
        {
            if (!await _grantService.HasGrantForCompanyAsync(adminId, "EditCompanyUsers", target.CompanyId))
                return "Error_NoPermissionSourceCompany";
            if (!await _grantService.HasGrantForCompanyAsync(adminId, "EditCompanyUsers", destCompanyId))
                return "Error_NoPermissionDestCompany";
        }

        // Privilege gate: must be allowed to assign the target's role (mirrors the role-change handler).
        if (!await _directorService.CanAssignRoleAsync(target.Role))
            return "Error_NoPermissionMoveRole";

        return null;
    }
}
