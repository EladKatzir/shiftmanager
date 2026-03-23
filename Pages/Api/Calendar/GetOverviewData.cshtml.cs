using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models.Support;
using ShiftManager.Services;
using System.Security.Claims;

namespace ShiftManager.Pages.Api.Calendar;

/// <summary>
/// API endpoint for shadow refresh of Overview calendar data.
/// </summary>
[Authorize]
[IgnoreAntiforgeryToken]
public class GetOverviewDataModel : PageModel
{
    private readonly IUserDayNoteService _noteService;
    private readonly ICompanyContext _companyContext;
    private readonly IScopeFilterService _scopeFilterService;
    private readonly AppDbContext _db;
    private readonly ILogger<GetOverviewDataModel> _logger;

    public GetOverviewDataModel(
        IUserDayNoteService noteService,
        ICompanyContext companyContext,
        IScopeFilterService scopeFilterService,
        AppDbContext db,
        ILogger<GetOverviewDataModel> logger)
    {
        _noteService = noteService;
        _companyContext = companyContext;
        _scopeFilterService = scopeFilterService;
        _db = db;
        _logger = logger;
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

            // Use current company if not specified
            var effectiveCompanyId = companyId ?? _companyContext.CompanyId;
            if (effectiveCompanyId <= 0)
            {
                return new JsonResult(new { success = false, message = "Invalid company" }) { StatusCode = 400 };
            }

            // SECURITY: Validate user has access to the requested company
            // User can view their own company; cross-company requires molecule or area grants
            var currentUser = await _db.Users.FindAsync(currentUserId);
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

            // Get users in company (uses tenant filter, capped for memory safety)
            var users = await _db.Users
                .Where(u => u.IsActive)
                .OrderBy(u => u.DisplayName)
                .Take(2000)
                .Select(u => new { id = u.Id, name = u.DisplayName })
                .ToListAsync();

            // Get notes for date range
            var notesDict = await _noteService.GetNotesForCompanyAsync(effectiveCompanyId!.Value, start, end);
            var notes = notesDict.Select(kvp => new
            {
                userId = kvp.Key.UserId,
                date = kvp.Key.Date.ToString("yyyy-MM-dd"),
                note = kvp.Value
            }).ToList();

            // Get vacations (approved only)
            var vacations = await _db.TimeOffRequests
                .Where(t => t.Status == RequestStatus.Approved
                    && t.StartDate <= end
                    && t.EndDate >= start)
                .Select(t => new
                {
                    userId = t.UserId,
                    startDate = t.StartDate.ToString("yyyy-MM-dd"),
                    endDate = t.EndDate.ToString("yyyy-MM-dd"),
                    type = t.Type.ToString()
                })
                .ToListAsync();

            // Get chores (active only)
            var chores = await _db.Chores
                .Where(c => c.Date >= start && c.Date <= end && c.CanceledAt == null)
                .Select(c => new
                {
                    userId = c.UserId,
                    date = c.Date.ToString("yyyy-MM-dd"),
                    title = c.Title,
                    isActive = c.CanceledAt == null
                })
                .ToListAsync();

            // Get on-duties (active only, filter by company users)
            var userIds = users.Select(u => u.id).ToList();
            var onDuties = await _db.OnDuties
                .Where(o => o.Date >= start && o.Date <= end && o.CanceledAt == null && userIds.Contains(o.UserId))
                .Select(o => new
                {
                    userId = o.UserId,
                    date = o.Date.ToString("yyyy-MM-dd"),
                    typeName = o.Type.ToString()
                })
                .ToListAsync();

            // Get shift assignments
            var shifts = await _db.ShiftAssignments
                .Include(sa => sa.ShiftInstance)
                    .ThenInclude(si => si.ShiftType)
                .Where(sa => sa.ShiftInstance.WorkDate >= start
                    && sa.ShiftInstance.WorkDate <= end
                    && sa.UserId.HasValue)
                .Select(sa => new
                {
                    userId = sa.UserId,
                    date = sa.ShiftInstance.WorkDate.ToString("yyyy-MM-dd"),
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
                    vacations,
                    chores,
                    onDuties,
                    shifts,
                    timestamp = DateTime.UtcNow.ToString("o")
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in GetOverviewData");
            return new JsonResult(new { success = false, message = "An error occurred" }) { StatusCode = 500 };
        }
    }
}
