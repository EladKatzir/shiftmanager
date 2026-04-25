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
/// API endpoint for shadow refresh of On-Call (Day Shifts) calendar data.
/// </summary>
// SECURITY-AUDITED: All IgnoreQueryFilters() in this class are SAFE — requires [Authorize];
// users are filtered by area-scoped companyIds; OnDuty is global by design
[Authorize]
[IgnoreAntiforgeryToken]
public class GetOnCallDataModel : PageModel
{
    private readonly IScopeFilterService _scopeFilterService;
    private readonly AppDbContext _db;
    private readonly ILogger<GetOnCallDataModel> _logger;
    private readonly IStringLocalizer<SharedResources> _localizer;

    public GetOnCallDataModel(
        IScopeFilterService scopeFilterService,
        AppDbContext db,
        ILogger<GetOnCallDataModel> logger,
        IStringLocalizer<SharedResources> localizer)
    {
        _scopeFilterService = scopeFilterService;
        _db = db;
        _logger = logger;
        _localizer = localizer;
    }

    public async Task<IActionResult> OnGetAsync(
        [FromQuery] int areaId,
        [FromQuery] string startDate,
        [FromQuery] string endDate)
    {
        try
        {
            // Validate user
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var currentUserId))
            {
                return new JsonResult(new { success = false, message = "Not authenticated" }) { StatusCode = 401 };
            }

            // Validate dates
            if (!DateOnly.TryParse(startDate, out var start) || !DateOnly.TryParse(endDate, out var end))
            {
                return new JsonResult(new { success = false, message = "Invalid date format" }) { StatusCode = 400 };
            }

            if (areaId <= 0)
            {
                return new JsonResult(new { success = false, message = "Invalid area" }) { StatusCode = 400 };
            }

            // SECURITY: Validate user has access to the requested area scope
            var hasAccess = await _scopeFilterService.ValidateScopeAccessAsync("area", areaId, "oncall");
            if (!hasAccess)
            {
                // Also check if the user's own company belongs to a molecule in this area
                var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == currentUserId);
                var userCompany = user != null ? await _db.Companies.Include(c => c.Molecule).FirstOrDefaultAsync(c => c.Id == user.CompanyId) : null;
                if (userCompany?.Molecule?.AreaId != areaId)
                {
                    _logger.LogWarning("SECURITY: User {UserId} attempted to access on-call data for area {AreaId} outside their scope",
                        currentUserId, areaId);
                    return new JsonResult(new { success = false, message = "Access denied" }) { StatusCode = 403 };
                }
            }

            // Get company IDs for area
            var companyIds = await _scopeFilterService.ResolveCompanyIdsForScopeAsync("area", areaId);

            // Get on-duty types (global table)
            var onDutyTypes = await _db.OnDutyTypeConfigs
                .Where(c => c.IsActive)
                .Select(c => new
                {
                    id = c.Id,
                    typeValue = c.TypeValue,
                    nameEn = c.NameEn,
                    nameHe = c.NameHe,
                    color = c.Color,
                    icon = c.Icon,
                    requiresOfficerRank = c.RequiresOfficerRank
                })
                .ToListAsync();

            // Get on-duties for the date range (global table, filter by user company)
            // OnDuty is global but we filter by users in the area's companies
            // IgnoreQueryFilters: companyIds already scoped — tenant filter would break multi-company views
            var onDuties = await _db.OnDuties
                .IgnoreQueryFilters()
                .Include(o => o.User)
                .Where(o => o.Date >= start
                    && o.Date <= end
                    && o.CanceledAt == null
                    && o.User != null
                    && companyIds.Contains(o.User.CompanyId))
                .Select(o => new
                {
                    o.Id,
                    date = o.Date.ToString("yyyy-MM-dd"),
                    type = (int)o.Type,
                    typeName = o.Type.ToString(),
                    userId = o.UserId,
                    userName = o.User != null ? o.User.DisplayName : null,
                    o.Notes
                })
                .ToListAsync();

            // Get users in area
            var users = await _db.Users
                .IgnoreQueryFilters()
                .Where(u => companyIds.Contains(u.CompanyId) && u.IsActive)
                .Select(u => new
                {
                    id = u.Id,
                    name = u.DisplayName
                })
                .ToListAsync();

            _logger.LogDebug("GetOnCallData: Returned {OnDutyCount} on-duties for area {AreaId}",
                onDuties.Count, areaId);

            return new JsonResult(new
            {
                success = true,
                data = new
                {
                    onDuties,
                    onDutyTypes,
                    users,
                    timestamp = DateTime.UtcNow.ToString("o")
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in GetOnCallData");
            return new JsonResult(
                ShiftManager.Models.ApiErrorResponse.Create(
                    "ERROR_CALENDAR_GET_ONCALL_FAILED",
                    _localizer["Error_CalendarApi_GetOnCallFailed"].Value)
                .WithCorrelationId(HttpContext.TraceIdentifier))
            { StatusCode = 500 };
        }
    }
}
