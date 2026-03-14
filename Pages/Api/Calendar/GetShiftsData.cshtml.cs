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
// SECURITY-AUDITED: All IgnoreQueryFilters() in this class are SAFE — requires [Authorize];
// assignments are filtered by molecule/jobType-scoped instanceIds; no cross-tenant data leak
[Authorize]
[IgnoreAntiforgeryToken]
public class GetShiftsDataModel : PageModel
{
    private readonly IShiftCalendarService _shiftCalendarService;
    private readonly IScopeFilterService _scopeFilterService;
    private readonly AppDbContext _db;
    private readonly ILogger<GetShiftsDataModel> _logger;
    private readonly ICompanyLocalizationService _companyLocalizationService;
    private readonly ITenantResolver _tenantResolver;

    public GetShiftsDataModel(
        IShiftCalendarService shiftCalendarService,
        IScopeFilterService scopeFilterService,
        AppDbContext db,
        ILogger<GetShiftsDataModel> logger,
        ICompanyLocalizationService companyLocalizationService,
        ITenantResolver tenantResolver)
    {
        _shiftCalendarService = shiftCalendarService;
        _scopeFilterService = scopeFilterService;
        _db = db;
        _logger = logger;
        _companyLocalizationService = companyLocalizationService;
        _tenantResolver = tenantResolver;
    }

    public async Task<IActionResult> OnGetAsync(
        [FromQuery] int moleculeId,
        [FromQuery] int? jobTypeId,
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

            // Validate scope parameters (jobTypeId is nullable for Tech molecules)
            if (moleculeId <= 0 || (jobTypeId.HasValue && jobTypeId.Value <= 0))
            {
                return new JsonResult(new { success = false, message = "Invalid scope parameters" }) { StatusCode = 400 };
            }

            // Get company IDs for molecule scope
            var companyIds = await _scopeFilterService.ResolveCompanyIdsForScopeAsync("molecule", moleculeId);

            // Fetch users, instances, and overlays
            var users = await _shiftCalendarService.GetUsersForCalendarAsync(moleculeId, jobTypeId);
            var instances = await _shiftCalendarService.GetShiftInstancesAsync(moleculeId, jobTypeId, start, end);
            var overlays = await _shiftCalendarService.GetOverlaysAsync(moleculeId, start, end);

            // C-07 OPTIMIZED: Batch-load all capacities in 2 queries instead of N+1 per instance
            var capacities = await _shiftCalendarService.GetCapacitiesBatchAsync(moleculeId, jobTypeId, start, end);

            // Get assignments for these instances using projection (avoid loading full User entity)
            var instanceIds = instances.Select(i => i.Id).ToList();
            var assignments = await _db.ShiftAssignments
                .IgnoreQueryFilters()
                .Where(sa => instanceIds.Contains(sa.ShiftInstanceId))
                .Select(sa => new
                {
                    sa.ShiftInstanceId,
                    sa.UserId,
                    UserDisplayName = sa.User != null ? sa.User.DisplayName : null
                })
                .ToListAsync();

            var assignmentsByInstance = assignments
                .GroupBy(a => a.ShiftInstanceId)
                .ToDictionary(g => g.Key, g => g.ToList());

            // Pre-compute localized shift type names
            var companyId = _tenantResolver.GetCurrentTenantId();
            var culture = System.Globalization.CultureInfo.CurrentUICulture.Name;
            var shiftTypeNames = new Dictionary<int, string>();
            foreach (var st in instances.Select(i => i.ShiftType).Where(st => st != null).DistinctBy(st => st.Id))
            {
                shiftTypeNames[st.Id] = await _companyLocalizationService.ResolveShiftTypeNameAsync(st, companyId, culture);
            }

            // Transform to cell data — no per-instance queries needed
            var cells = new List<object>();
            foreach (var instance in instances)
            {
                // Use batch-loaded capacity; fall back to instance's StaffingRequired
                var capacity = capacities.GetValueOrDefault(
                    (instance.ShiftTypeId, instance.WorkDate),
                    instance.StaffingRequired);

                var instanceAssignments = assignmentsByInstance.GetValueOrDefault(instance.Id) ?? new();

                cells.Add(new
                {
                    shiftInstanceId = instance.Id,
                    shiftTypeId = instance.ShiftTypeId,
                    shiftTypeName = shiftTypeNames.GetValueOrDefault(instance.ShiftTypeId, instance.ShiftType?.Name ?? "Unknown"),
                    date = instance.WorkDate.ToString("yyyy-MM-dd"),
                    capacity,
                    assignedCount = instanceAssignments.Count(a => a.UserId.HasValue),
                    assignments = instanceAssignments
                        .Where(a => a.UserId.HasValue)
                        .Select(a => new
                        {
                            userId = a.UserId,
                            userName = a.UserDisplayName ?? "Unknown"
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
