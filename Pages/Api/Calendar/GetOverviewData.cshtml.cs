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
    private readonly AppDbContext _db;
    private readonly ILogger<GetOverviewDataModel> _logger;

    public GetOverviewDataModel(
        IUserDayNoteService noteService,
        ICompanyContext companyContext,
        AppDbContext db,
        ILogger<GetOverviewDataModel> logger)
    {
        _noteService = noteService;
        _companyContext = companyContext;
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

            // Get users in company (uses tenant filter)
            var users = await _db.Users
                .Where(u => u.IsActive)
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
