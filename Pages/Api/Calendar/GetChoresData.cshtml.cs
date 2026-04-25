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
/// API endpoint for shadow refresh of Chores calendar data.
/// </summary>
// SECURITY-AUDITED: All IgnoreQueryFilters() in this class are SAFE — requires [Authorize];
// chores and users are filtered by molecule-scoped companyIds; no cross-tenant data leak
[Authorize]
[IgnoreAntiforgeryToken]
public class GetChoresDataModel : PageModel
{
    private readonly IChoreTypeService _choreTypeService;
    private readonly IScopeFilterService _scopeFilterService;
    private readonly AppDbContext _db;
    private readonly ILogger<GetChoresDataModel> _logger;
    private readonly IStringLocalizer<SharedResources> _localizer;

    public GetChoresDataModel(
        IChoreTypeService choreTypeService,
        IScopeFilterService scopeFilterService,
        AppDbContext db,
        ILogger<GetChoresDataModel> logger,
        IStringLocalizer<SharedResources> localizer)
    {
        _choreTypeService = choreTypeService;
        _scopeFilterService = scopeFilterService;
        _db = db;
        _logger = logger;
        _localizer = localizer;
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

            // SECURITY: Validate user has access to the requested molecule scope
            var hasAccess = await _scopeFilterService.ValidateScopeAccessAsync("molecule", moleculeId, "chores");
            if (!hasAccess)
            {
                // Also check if the user's own company belongs to this molecule
                var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == currentUserId);
                var userCompany = user != null ? await _db.Companies.FindAsync(user.CompanyId) : null;
                if (userCompany?.MoleculeId != moleculeId)
                {
                    _logger.LogWarning("SECURITY: User {UserId} attempted to access chores for molecule {MoleculeId} outside their scope",
                        currentUserId, moleculeId);
                    return new JsonResult(new { success = false, message = "Access denied" }) { StatusCode = 403 };
                }
            }

            // Get company IDs for molecule
            var companyIds = await _scopeFilterService.ResolveCompanyIdsForScopeAsync("molecule", moleculeId);

            // Get chore types for this molecule
            var choreTypes = await _choreTypeService.GetChoreTypesForMoleculeAsync(moleculeId);

            // Get chores for the date range (only active chores)
            // Materialize first so we can call LocalizeChoreTypeName in-memory (EF can't translate it)
            // Culture is set by request localization middleware from the .AspNetCore.Culture cookie —
            // same session that rendered the calling page, so this reliably matches the page locale.
            var isHebrew = System.Globalization.CultureInfo.CurrentUICulture.Name.StartsWith("he");
            var choresRaw = await _db.Chores
                .IgnoreQueryFilters()
                .Include(c => c.User)
                .Include(c => c.ChoreType)
                .Where(c => companyIds.Contains(c.CompanyId)
                    && c.Date >= start
                    && c.Date <= end
                    && c.CanceledAt == null) // Only active chores
                .ToListAsync();
            var chores = choresRaw.Select(c => new
            {
                c.Id,
                c.UserId,
                userName = c.User != null ? c.User.DisplayName : null,
                date = c.Date.ToString("yyyy-MM-dd"),
                c.Title,
                c.Notes,
                c.ChoreTypeId,
                choreTypeName = c.ChoreType != null ? LocalizeChoreTypeName(c.ChoreType, isHebrew) : null,
                choreTypeColor = c.ChoreType != null ? c.ChoreType.Color : null,
                isActive = c.CanceledAt == null
            }).ToList();

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
                            name = LocalizeChoreTypeName(ct, isHebrew),
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
            return new JsonResult(
                ShiftManager.Models.ApiErrorResponse.Create(
                    "ERROR_CALENDAR_GET_CHORES_FAILED",
                    _localizer["Error_CalendarApi_GetChoresFailed"].Value)
                .WithCorrelationId(HttpContext.TraceIdentifier))
            { StatusCode = 500 };
        }
    }

    private static string LocalizeChoreTypeName(ShiftManager.Models.ChoreType ct, bool isHebrew) =>
        isHebrew && !string.IsNullOrWhiteSpace(ct.NameHe) ? ct.NameHe : ct.NameEn ?? ct.DisplayName;
}
