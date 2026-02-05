using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Services;
using System.Security.Claims;

namespace ShiftManager.Pages.Api.Calendar;

/// <summary>
/// API endpoint for shadow refresh of Shifts calendar data.
/// Returns cell-level data for the current view without requiring full page reload.
/// </summary>
[Authorize]
[IgnoreAntiforgeryToken]
public class GetShiftsDataModel : PageModel
{
    private readonly IShiftCalendarService _shiftCalendarService;
    private readonly IScopeFilterService _scopeFilterService;
    private readonly AppDbContext _db;
    private readonly ILogger<GetShiftsDataModel> _logger;

    public GetShiftsDataModel(
        IShiftCalendarService shiftCalendarService,
        IScopeFilterService scopeFilterService,
        AppDbContext db,
        ILogger<GetShiftsDataModel> logger)
    {
        _shiftCalendarService = shiftCalendarService;
        _scopeFilterService = scopeFilterService;
        _db = db;
        _logger = logger;
    }

    public async Task<IActionResult> OnGetAsync(
        [FromQuery] int moleculeId,
        [FromQuery] int jobTypeId,
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

            // Validate scope parameters
            if (moleculeId <= 0 || jobTypeId <= 0)
            {
                return new JsonResult(new { success = false, message = "Invalid scope parameters" }) { StatusCode = 400 };
            }

            // Get company IDs for molecule scope
            var companyIds = await _scopeFilterService.ResolveCompanyIdsForScopeAsync("molecule", moleculeId);

            // Fetch users and instances
            var users = await _shiftCalendarService.GetUsersForCalendarAsync(moleculeId, jobTypeId);
            var instances = await _shiftCalendarService.GetShiftInstancesAsync(moleculeId, jobTypeId, start, end);
            var overlays = await _shiftCalendarService.GetOverlaysAsync(moleculeId, start, end);

            // Get assignments for these instances
            var instanceIds = instances.Select(i => i.Id).ToList();
            var assignments = await _db.ShiftAssignments
                .IgnoreQueryFilters()
                .Include(sa => sa.User)
                .Where(sa => instanceIds.Contains(sa.ShiftInstanceId))
                .ToListAsync();

            var assignmentsByInstance = assignments
                .GroupBy(a => a.ShiftInstanceId)
                .ToDictionary(g => g.Key, g => g.ToList());

            // Transform to cell data
            var cells = new List<object>();
            foreach (var instance in instances)
            {
                var capacity = await _shiftCalendarService.GetCapacityAsync(
                    instance.ShiftTypeId, moleculeId, jobTypeId, instance.WorkDate);

                var instanceAssignments = assignmentsByInstance.GetValueOrDefault(instance.Id) ?? new();

                cells.Add(new
                {
                    shiftInstanceId = instance.Id,
                    shiftTypeId = instance.ShiftTypeId,
                    shiftTypeName = instance.ShiftType?.Key ?? "Unknown",
                    date = instance.WorkDate.ToString("yyyy-MM-dd"),
                    capacity,
                    assignedCount = instanceAssignments.Count(a => a.UserId.HasValue),
                    assignments = instanceAssignments
                        .Where(a => a.UserId.HasValue)
                        .Select(a => new
                        {
                            userId = a.UserId,
                            userName = a.User?.DisplayName ?? "Unknown"
                        })
                        .ToList()
                });
            }

            // Transform overlays
            var userOverlays = overlays.Select(kvp => new
            {
                userId = kvp.Key.UserId,
                date = kvp.Key.Date.ToString("yyyy-MM-dd"),
                hasVacation = kvp.Value.HasVacation,
                hasChore = kvp.Value.HasChore,
                hasOnDuty = kvp.Value.HasOnDuty,
                otherShifts = kvp.Value.OtherShifts
            }).ToList();

            _logger.LogDebug("GetShiftsData: Returned {CellCount} cells for molecule {MoleculeId}, job {JobTypeId}",
                cells.Count, moleculeId, jobTypeId);

            return new JsonResult(new
            {
                success = true,
                data = new
                {
                    cells,
                    overlays = userOverlays,
                    users = users.Select(u => new { id = u.Id, name = u.DisplayName }).ToList(),
                    timestamp = DateTime.UtcNow.ToString("o")
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in GetShiftsData");
            return new JsonResult(new { success = false, message = "An error occurred" }) { StatusCode = 500 };
        }
    }
}
