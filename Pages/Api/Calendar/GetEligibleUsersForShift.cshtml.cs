using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Services;
using System.Security.Claims;

namespace ShiftManager.Pages.Api.Calendar;

/// <summary>
/// API endpoint for fetching eligible users for a Tech molecule shift type.
/// Filters by EligibleCompanyIds and RequiresOfficerRank from the ShiftType.
/// Used by calendar-bottom-sheet.js to populate the user picker in Tech shift-mode.
/// </summary>
[Authorize]
[IgnoreAntiforgeryToken]
public class GetEligibleUsersForShiftModel : PageModel
{
    private readonly IShiftCalendarService _shiftCalendarService;
    private readonly IScopeFilterService _scopeFilterService;
    private readonly AppDbContext _db;
    private readonly ILogger<GetEligibleUsersForShiftModel> _logger;

    public GetEligibleUsersForShiftModel(
        IShiftCalendarService shiftCalendarService,
        IScopeFilterService scopeFilterService,
        AppDbContext db,
        ILogger<GetEligibleUsersForShiftModel> logger)
    {
        _shiftCalendarService = shiftCalendarService;
        _scopeFilterService = scopeFilterService;
        _db = db;
        _logger = logger;
    }

    public async Task<IActionResult> OnGetAsync(
        [FromQuery] int moleculeId,
        [FromQuery] int shiftTypeId)
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

            var users = await _shiftCalendarService.GetEligibleUsersForShiftTypeAsync(moleculeId, shiftTypeId);

            return new JsonResult(new
            {
                success = true,
                users = users.Select(u => new { id = u.Id, name = u.DisplayName }).ToList()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in GetEligibleUsersForShift");
            return new JsonResult(new { success = false, message = "An error occurred" }) { StatusCode = 500 };
        }
    }
}
