using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Resources;
using ShiftManager.Services;
using System.Globalization;
using System.Security.Claims;

namespace ShiftManager.Pages.Api.Calendar;

/// <summary>
/// API endpoint for shadow refresh of Overview calendar data.
/// </summary>
[Authorize]
[IgnoreAntiforgeryToken]
public class GetOverviewDataModel : PageModel
{
    private readonly ICalendarTextEntryService _textEntryService;
    private readonly ITenantResolver _tenantResolver;
    private readonly IScopeFilterService _scopeFilterService;
    private readonly AppDbContext _db;
    private readonly ILogger<GetOverviewDataModel> _logger;
    private readonly IStringLocalizer<SharedResources> _localizer;

    public GetOverviewDataModel(
        ICalendarTextEntryService textEntryService,
        ITenantResolver tenantResolver,
        IScopeFilterService scopeFilterService,
        AppDbContext db,
        ILogger<GetOverviewDataModel> logger,
        IStringLocalizer<SharedResources> localizer)
    {
        _textEntryService = textEntryService;
        _tenantResolver = tenantResolver;
        _scopeFilterService = scopeFilterService;
        _db = db;
        _logger = logger;
        _localizer = localizer;
    }

    public async Task<IActionResult> OnGetAsync(
        [FromQuery] int? companyId,
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

            // Use the switcher-aware active company if not specified (the shipped Overview JS always
            // passes companyId; this fallback previously used the home-company claim, returning the
            // wrong company's data for a switched Owner/member when the param was omitted).
            int effectiveCompanyId = companyId ?? _tenantResolver.GetCurrentTenantId();
            if (effectiveCompanyId <= 0)
            {
                return new JsonResult(new { success = false, message = "Invalid company" }) { StatusCode = 400 };
            }

            // SECURITY: Validate user has access to the requested company
            // User can view their own company; cross-company requires molecule or area grants.
            // IgnoreQueryFilters: the caller's OWN record must always be findable — the tenant filter
            // is scoped to the (possibly switched) active company, which would exclude an Owner whose
            // home company differs from the switched tenant, wrongly yielding a 401 "User not found".
            var currentUser = await _db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == currentUserId);
            if (currentUser == null)
            {
                return new JsonResult(new { success = false, message = "User not found" }) { StatusCode = 401 };
            }
            if (currentUser.CompanyId != effectiveCompanyId)
            {
                // Not their own company — check if target company is in the user's molecule or area
                var userCompany = await _db.Companies.FindAsync(currentUser.CompanyId);
                var targetCompany = await _db.Companies.FindAsync(effectiveCompanyId);
                var inSameMolecule = userCompany?.MoleculeId != null && targetCompany?.MoleculeId != null
                    && userCompany.MoleculeId == targetCompany.MoleculeId;

                if (!inSameMolecule)
                {
                    // Check broader scope grants (molecule/area level)
                    var hasMoleculeAccess = await _scopeFilterService.ValidateScopeAccessAsync("molecule", null, "overview");
                    var hasAreaAccess = await _scopeFilterService.ValidateScopeAccessAsync("area", null, "overview");
                    if (!hasMoleculeAccess && !hasAreaAccess)
                    {
                        _logger.LogWarning("SECURITY: User {UserId} attempted to access overview for company {CompanyId} outside their scope",
                            currentUserId, effectiveCompanyId);
                        return new JsonResult(new { success = false, message = "Access denied" }) { StatusCode = 403 };
                    }
                }
            }

            // Get users in the effective company, capped for memory safety
            // SECURITY-AUDITED: IgnoreQueryFilters is SAFE — effectiveCompanyId validated above via scope/grant checks
            var users = await _db.Users
                .IgnoreQueryFilters()
                .Where(u => u.CompanyId == effectiveCompanyId && u.IsActive)
                .OrderBy(u => u.DisplayName)
                .Take(2000)
                .Select(u => new { id = u.Id, name = u.DisplayName })
                .ToListAsync();

            // Get overview notes for date range (unified from CalendarTextEntry)
            var notesDict = await _textEntryService.GetOverviewNotesForCompanyAsync(effectiveCompanyId, start, end);
            var notes = notesDict.Select(kvp => new
            {
                userId = kvp.Key.UserId,
                date = kvp.Key.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                note = kvp.Value
            }).ToList();

            // Get vacations (approved only)
            // SECURITY-AUDITED: IgnoreQueryFilters is SAFE — effectiveCompanyId validated above via scope/grant checks
            var vacations = await _db.TimeOffRequests
                .IgnoreQueryFilters()
                .Where(t => t.CompanyId == effectiveCompanyId
                    && t.Status == RequestStatus.Approved
                    && t.StartDate <= end
                    && t.EndDate >= start)
                .Select(t => new
                {
                    userId = t.UserId,
                    startDate = t.StartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    endDate = t.EndDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    type = t.Type.ToString()
                })
                .ToListAsync();

            // Get chores (active only)
            // SECURITY-AUDITED: IgnoreQueryFilters is SAFE — effectiveCompanyId validated above via scope/grant checks
            var chores = await _db.Chores
                .IgnoreQueryFilters()
                .Where(c => c.CompanyId == effectiveCompanyId && c.Date >= start && c.Date <= end && c.CanceledAt == null)
                .Select(c => new
                {
                    userId = c.UserId,
                    date = c.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    title = c.Title,
                    isActive = c.CanceledAt == null
                })
                .ToListAsync();

            // Get on-duties (active only, filter by company users)
            var userIds = users.Select(u => u.id).ToList();

            // Get quick-entry text entries for cross-visibility (📝 badge on Overview)
            var textEntriesWithType = await _textEntryService.GetForUsersAndDateRangeWithTypeAsync(
                userIds, start, end);
            var textEntries = textEntriesWithType
                .SelectMany(kvp => kvp.Value
                    .Where(e => e.EntryType == CalendarTextEntryType.QuickEntry)
                    .Select(e => new
                    {
                        userId = kvp.Key.UserId,
                        date = kvp.Key.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        text = e.Text
                    }))
                .ToList();

            var onDuties = await _db.OnDuties
                .Where(o => o.Date >= start && o.Date <= end && o.CanceledAt == null && userIds.Contains(o.UserId))
                .Select(o => new
                {
                    userId = o.UserId,
                    date = o.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    typeName = o.Type.ToString()
                })
                .ToListAsync();

            // Get shift assignments
            // SECURITY-AUDITED: IgnoreQueryFilters is SAFE — effectiveCompanyId validated above via scope/grant checks
            var shifts = await _db.ShiftAssignments
                .IgnoreQueryFilters()
                .Include(sa => sa.ShiftInstance)
                    .ThenInclude(si => si.ShiftType)
                .Where(sa => sa.CompanyId == effectiveCompanyId
                    && sa.ShiftInstance.WorkDate >= start
                    && sa.ShiftInstance.WorkDate <= end
                    && sa.UserId.HasValue)
                .Select(sa => new
                {
                    userId = sa.UserId,
                    date = sa.ShiftInstance.WorkDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    shiftName = sa.ShiftInstance.ShiftType != null ? sa.ShiftInstance.ShiftType.Key : "Shift"
                })
                .ToListAsync();

            _logger.LogDebug("GetOverviewData: Returned data for company {CompanyId}", effectiveCompanyId);

            return new JsonResult(new
            {
                success = true,
                data = new
                {
                    users,
                    notes,
                    textEntries,
                    vacations,
                    chores,
                    onDuties,
                    shifts,
                    timestamp = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in GetOverviewData");
            return new JsonResult(
                ApiErrorResponse.Create(
                    "ERROR_CALENDAR_GET_OVERVIEW_FAILED",
                    _localizer["Error_CalendarApi_GetOverviewFailed"].Value)
                .WithCorrelationId(HttpContext.TraceIdentifier))
            { StatusCode = 500 };
        }
    }
}
