using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using ShiftManager.Data;
using ShiftManager.Resources;
using ShiftManager.Services;
using System.Security.Claims;

namespace ShiftManager.Pages.Api.Calendar;

/// <summary>
/// API endpoint for fetching eligible users for ANY molecule kind's shift type (3b). Routes through
/// ShiftCandidateService, which reads the company-level CategoryBasedShiftEligibility flag and either
/// returns the legacy set (flag off) or the DoesShifts + ShiftCategory-filtered set (flag on), with the
/// null-category fork (sharedFallback vs noCategory + the allowFallback escape hatch). Officer-rank is
/// preserved for Tech molecules inside the leaf. Used by calendar-bottom-sheet.js + calendar-quick-entry.js.
/// </summary>
[Authorize]
[IgnoreAntiforgeryToken]
public class GetEligibleUsersForShiftModel : PageModel
{
    private readonly IShiftCandidateService _candidateService;
    private readonly IScopeFilterService _scopeFilterService;
    private readonly AppDbContext _db;
    private readonly ILogger<GetEligibleUsersForShiftModel> _logger;
    private readonly IStringLocalizer<SharedResources> _localizer;

    public GetEligibleUsersForShiftModel(
        IShiftCandidateService candidateService,
        IScopeFilterService scopeFilterService,
        AppDbContext db,
        ILogger<GetEligibleUsersForShiftModel> logger,
        IStringLocalizer<SharedResources> localizer)
    {
        _candidateService = candidateService;
        _scopeFilterService = scopeFilterService;
        _db = db;
        _logger = logger;
        _localizer = localizer;
    }

    public async Task<IActionResult> OnGetAsync(
        [FromQuery] int moleculeId,
        [FromQuery] int shiftTypeId,
        [FromQuery] bool allowFallback = false)
    {
        try
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var currentUserId))
                return new JsonResult(new { success = false, message = "Not authenticated" }) { StatusCode = 401 };

            if (moleculeId <= 0 || shiftTypeId <= 0)
                return new JsonResult(new { success = false, message = "Invalid parameters" }) { StatusCode = 400 };

            // SECURITY: Validate user has access to the requested molecule scope
            var hasAccess = await _scopeFilterService.ValidateScopeAccessAsync("molecule", moleculeId, "shifts");
            if (!hasAccess)
            {
                var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == currentUserId);
                var userCompany = user != null ? await _db.Companies.FindAsync(user.CompanyId) : null;
                if (userCompany?.MoleculeId != moleculeId)
                {
                    _logger.LogWarning("SECURITY: User {UserId} attempted to access eligible users for molecule {MoleculeId} outside their scope",
                        currentUserId, moleculeId);
                    return new JsonResult(new { success = false, message = "Access denied" }) { StatusCode = 403 };
                }
            }

            // SECURITY: Validate shiftType belongs to the requested molecule to prevent cross-molecule eligibility filter injection
            var shiftTypeBelongsToMolecule = await _db.ShiftTypes
                .IgnoreQueryFilters()
                .AnyAsync(st => st.Id == shiftTypeId && st.MoleculeId == moleculeId);
            if (!shiftTypeBelongsToMolecule)
                return new JsonResult(new { success = false, message = "Invalid parameters" }) { StatusCode = 400 };

            // Resolve the caller's current company for the company-level flag check (3b).
            var currentCompanyId = 0;
            int.TryParse(User.FindFirst("CompanyId")?.Value, out currentCompanyId);

            var result = await _candidateService.GetEligibleCandidatesAsync(
                moleculeId, shiftTypeId, currentCompanyId, allowFallback);

            return new JsonResult(new
            {
                success = true,
                reason = result.Reason,
                users = result.Users.Select(u => new { id = u.Id, name = u.Name, companyName = u.CompanyName }).ToList()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in GetEligibleUsersForShift");
            return new JsonResult(
                ShiftManager.Models.ApiErrorResponse.Create(
                    "ERROR_CALENDAR_GET_ELIGIBLE_USERS_FAILED",
                    _localizer["Error_CalendarApi_GetEligibleUsersFailed"].Value)
                .WithCorrelationId(HttpContext.TraceIdentifier))
            { StatusCode = 500 };
        }
    }
}
