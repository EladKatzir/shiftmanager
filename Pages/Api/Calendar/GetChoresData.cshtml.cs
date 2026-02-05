using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Services;
using System.Security.Claims;

namespace ShiftManager.Pages.Api.Calendar;

/// <summary>
/// API endpoint for shadow refresh of Chores calendar data.
/// </summary>
[Authorize]
[IgnoreAntiforgeryToken]
public class GetChoresDataModel : PageModel
{
    private readonly IChoreTypeService _choreTypeService;
    private readonly IScopeFilterService _scopeFilterService;
    private readonly AppDbContext _db;
    private readonly ILogger<GetChoresDataModel> _logger;

    public GetChoresDataModel(
        IChoreTypeService choreTypeService,
        IScopeFilterService scopeFilterService,
        AppDbContext db,
        ILogger<GetChoresDataModel> logger)
    {
        _choreTypeService = choreTypeService;
        _scopeFilterService = scopeFilterService;
        _db = db;
        _logger = logger;
    }

    public async Task<IActionResult> OnGetAsync(
        [FromQuery] int moleculeId,
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

            if (moleculeId <= 0)
            {
                return new JsonResult(new { success = false, message = "Invalid molecule" }) { StatusCode = 400 };
            }

            // Get company IDs for molecule
            var companyIds = await _scopeFilterService.ResolveCompanyIdsForScopeAsync("molecule", moleculeId);

            // Get chore types for this molecule
            var choreTypes = await _choreTypeService.GetChoreTypesForMoleculeAsync(moleculeId);

            // Get chores for the date range (only active chores)
            var chores = await _db.Chores
                .IgnoreQueryFilters()
                .Include(c => c.User)
                .Include(c => c.ChoreType)
                .Where(c => companyIds.Contains(c.CompanyId)
                    && c.Date >= start
                    && c.Date <= end
                    && c.CanceledAt == null) // Only active chores
                .Select(c => new
                {
                    c.Id,
                    c.UserId,
                    userName = c.User != null ? c.User.DisplayName : null,
                    date = c.Date.ToString("yyyy-MM-dd"),
                    c.Title,
                    c.Notes,
                    c.ChoreTypeId,
                    choreTypeName = c.ChoreType != null ? c.ChoreType.DisplayName : null,
                    choreTypeColor = c.ChoreType != null ? c.ChoreType.Color : null,
                    isActive = c.CanceledAt == null
                })
                .ToListAsync();

            // Get users in molecule
            var users = await _db.Users
                .IgnoreQueryFilters()
                .Where(u => companyIds.Contains(u.CompanyId) && u.IsActive)
                .Select(u => new { id = u.Id, name = u.DisplayName })
                .ToListAsync();

            _logger.LogDebug("GetChoresData: Returned {ChoreCount} chores for molecule {MoleculeId}",
                chores.Count, moleculeId);

            return new JsonResult(new
            {
                success = true,
                data = new
                {
                    chores,
                    choreTypes = choreTypes
                        .Where(ct => ct.IsActive)
                        .Select(ct => new
                        {
                            id = ct.Id,
                            name = ct.DisplayName,
                            color = ct.Color
                        }).ToList(),
                    users,
                    timestamp = DateTime.UtcNow.ToString("o")
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in GetChoresData");
            return new JsonResult(new { success = false, message = "An error occurred" }) { StatusCode = 500 };
        }
    }
}
