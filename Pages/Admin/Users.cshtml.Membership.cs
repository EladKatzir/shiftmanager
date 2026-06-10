using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ShiftManager.Pages.Admin;

// Handlers for managing a user's company memberships (add, remove-with-impact, set-primary).
// Authorization mirrors Move handlers: EditCompanyUsers on the target company OR AdminAccess.
public partial class UsersModel
{
    // ── Add membership ────────────────────────────────────────────────────────

    /// <summary>
    /// Adds a non-primary membership for the user in the given company.
    /// Epic 5 comment: grant application for the role template is deferred to Epic 5.
    /// </summary>
    public async Task<IActionResult> OnPostAddMembershipAsync(
        int userId, int companyId, int? roleTemplateId, int? jobTypeId, bool doesShifts)
    {
        if (!int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var adminId))
            return Forbid();

        var authError = await AuthorizeMembershipActionAsync(adminId, userId, companyId);
        if (authError != null)
        {
            TempData["ErrorMessage"] = _localizer[authError].Value;
            return RedirectToPage();
        }

        try
        {
            await _companyMembershipService.AddMembershipAsync(
                userId, companyId, roleTemplateId, jobTypeId,
                departmentId: null, doesShifts, homeTypeId: null, actingAdminId: adminId);

            // Epic 5: apply role-template grants scoped to the membership's company.
            // Only when a role template is assigned; a template-free membership confers no auto-grants.
            if (roleTemplateId.HasValue)
            {
                var roleTemplate = await _roleService.GetRoleTemplateAsync(roleTemplateId.Value);
                if (roleTemplate != null)
                {
                    var grantScope = await BuildGrantScopeForTemplateAsync(roleTemplate.Key, companyId, jobTypeId);
                    await _grantService.AssignRoleTemplateGrantsAsync(userId, roleTemplate.Key, grantScope, adminId);
                }
            }

            await _auditLogService.LogUserActionAsync(
                userId: adminId,
                action: "MembershipAdded",
                entityType: "CompanyMembership",
                entityId: userId,
                description: $"Added membership for user #{userId} in company #{companyId} (roleTemplate={roleTemplateId}, jobType={jobTypeId}, doesShifts={doesShifts})");

            TempData["SuccessMessage"] = _localizer["Users_Membership_Added"].Value;
        }
        catch (InvalidOperationException)
        {
            // Service throws when an active membership already exists (incl. the concurrent-add race).
            TempData["ErrorMessage"] = _localizer["Error_MembershipAlreadyExists"].Value;
        }

        return RedirectToPage();
    }

    // ── Remove membership — impact preview (AJAX GET) ─────────────────────────

    /// <summary>
    /// Returns a JSON impact summary for removing the user's membership in the given company.
    /// </summary>
    public async Task<IActionResult> OnGetRemoveMembershipImpactAsync(int userId, int companyId)
    {
        if (!int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var adminId))
            return new JsonResult(new { ok = false, error = "unauthorized" }) { StatusCode = 401 };

        var authError = await AuthorizeMembershipActionAsync(adminId, userId, companyId);
        if (authError != null)
            return new JsonResult(new { ok = false, error = _localizer[authError].Value });

        var impact = await _companyMembershipService.GetRemovalImpactAsync(userId, companyId);
        var summary = _localizer["Users_RemoveMembershipImpact_Summary",
            impact.FutureShifts, impact.PendingOrFutureTimeOff, impact.FutureChores,
            impact.OpenSwapRequests, impact.FutureOnDuty, impact.GrantsRemoved].Value;

        return new JsonResult(new { ok = true, summary });
    }

    // ── Remove membership — execute (POST) ────────────────────────────────────

    /// <summary>
    /// Removes the user's membership in the given company, clearing all scoped records.
    /// Refuses to remove the primary membership (promote another first).
    /// </summary>
    public async Task<IActionResult> OnPostRemoveMembershipAsync(int userId, int companyId)
    {
        if (!int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var adminId))
            return Forbid();

        var authError = await AuthorizeMembershipActionAsync(adminId, userId, companyId);
        if (authError != null)
        {
            TempData["ErrorMessage"] = _localizer[authError].Value;
            return RedirectToPage();
        }

        try
        {
            var removed = await _companyMembershipService.RemoveMembershipWithCleanupAsync(userId, companyId, adminId);
            if (removed)
            {
                await _auditLogService.LogUserActionAsync(
                    userId: adminId,
                    action: "MembershipRemoved",
                    entityType: "CompanyMembership",
                    entityId: userId,
                    description: $"Removed membership for user #{userId} from company #{companyId}");

                TempData["SuccessMessage"] = _localizer["Users_Membership_Removed"].Value;
            }
            else
            {
                TempData["ErrorMessage"] = _localizer["Error_UserNotFound"].Value;
            }
        }
        catch (InvalidOperationException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }

        return RedirectToPage();
    }

    // ── Set primary membership (POST) ─────────────────────────────────────────

    /// <summary>
    /// Promotes the given company to primary for the user (demotes all others).
    /// </summary>
    public async Task<IActionResult> OnPostSetPrimaryMembershipAsync(int userId, int companyId)
    {
        if (!int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var adminId))
            return Forbid();

        // Two-sided gate: SetPrimary relocates the home pointer, so it requires authority over the
        // user's current home company in addition to the new primary company.
        var authError = await AuthorizeSetPrimaryAsync(adminId, userId, companyId);
        if (authError != null)
        {
            TempData["ErrorMessage"] = _localizer[authError].Value;
            return RedirectToPage();
        }

        try
        {
            await _companyMembershipService.SetPrimaryAsync(userId, companyId);

            await _auditLogService.LogUserActionAsync(
                userId: adminId,
                action: "MembershipPrimarySet",
                entityType: "CompanyMembership",
                entityId: userId,
                description: $"Set company #{companyId} as primary for user #{userId}");

            TempData["SuccessMessage"] = _localizer["Users_Membership_PrimarySet"].Value;
        }
        catch (InvalidOperationException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }

        return RedirectToPage();
    }

    // ── Authorization helper ──────────────────────────────────────────────────

    // Authorization model (deliberate, asymmetric — see Users.cshtml.Move.cs for the destructive Move):
    //   • Add authorizes on the DESTINATION company only — it is additive and does NOT affect the
    //     user's other companies (unlike Move, which relocates the user and must gate source+dest).
    //   • Remove authorizes on the company being cleared.
    //   • SetPrimary additionally requires authority over the user's CURRENT home company, because it
    //     relocates the home pointer (AppUser.CompanyId), affecting the old home too — see
    //     AuthorizeSetPrimaryAsync.
    //   • ALL actions also require CanAssignRoleAsync(target.Role): a company-admin must not be able to
    //     manipulate a higher-role (e.g. Director) user's memberships.

    /// <summary>
    /// Returns a localization key describing the failure, or null when the membership action is authorized.
    /// Gate: admin must hold AdminAccess OR EditCompanyUsers on the target company, AND (when not
    /// AdminAccess) be permitted to assign the target user's role (blocks manipulating a higher admin).
    /// </summary>
    private async Task<string?> AuthorizeMembershipActionAsync(int adminId, int userId, int companyId)
    {
        if (userId == adminId) return "Error_MoveSelf";

        // SECURITY-AUDITED: load target by explicit id (cross-tenant admin view).
        var target = await _db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == userId);
        if (target == null) return "Error_UserNotFound";

        // SECURITY-AUDITED: validate company by explicit id.
        var company = await _db.Companies.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == companyId);
        if (company == null) return "Error_CompanyNotFound";
        if (company.IsHeadquarters) return "Error_MoveDestHq";

        var isAdmin = await _grantService.HasGrantAsync(adminId, "AdminAccess");
        if (!isAdmin)
        {
            if (!await _grantService.HasGrantForCompanyAsync(adminId, "EditCompanyUsers", companyId))
                return "Error_NoPermissionDestCompany";

            // Privilege gate: must be allowed to assign the target's role (mirrors Move + role-change).
            if (!await _directorService.CanAssignRoleAsync(target.Role))
                return "Error_NoPermissionMembershipRole";
        }

        return null;
    }

    /// <summary>
    /// SetPrimary-specific authorization. Runs the normal check on the NEW primary company, then —
    /// because promoting relocates the user's home pointer (AppUser.CompanyId) — ALSO requires authority
    /// over the user's CURRENT home company (when not AdminAccess). Returns an error key or null.
    /// </summary>
    private async Task<string?> AuthorizeSetPrimaryAsync(int adminId, int userId, int companyId)
    {
        var baseError = await AuthorizeMembershipActionAsync(adminId, userId, companyId);
        if (baseError != null) return baseError;

        var isAdmin = await _grantService.HasGrantAsync(adminId, "AdminAccess");
        if (!isAdmin)
        {
            // SECURITY-AUDITED: load target by explicit id for its current home company.
            var target = await _db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == userId);
            if (target == null) return "Error_UserNotFound";

            // The new company is already authorized above; also require authority over the OLD home.
            if (target.CompanyId != companyId &&
                !await _grantService.HasGrantForCompanyAsync(adminId, "EditCompanyUsers", target.CompanyId))
                return "Error_NoPermissionSourceCompany";
        }

        return null;
    }
}
