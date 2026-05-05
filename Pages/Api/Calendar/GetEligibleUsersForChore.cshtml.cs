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
/// Eligible users for chore assignment within a molecule. Cross-company within
/// the molecule is intentional — the chore service uses molecule scope, not tenant.
/// Mirrors auth pattern of GetEligibleUsersForShift.
/// </summary>
[Authorize(Policy = "Grant:AssignChores")]
[IgnoreAntiforgeryToken]
public class GetEligibleUsersForChoreModel : PageModel
{
    private readonly IScopeFilterService _scopeFilterService;
    private readonly AppDbContext _db;
    private readonly ILogger<GetEligibleUsersForChoreModel> _logger;
    private readonly IStringLocalizer<SharedResources> _localizer;

    public GetEligibleUsersForChoreModel(
        IScopeFilterService scopeFilterService,
        AppDbContext db,
        ILogger<GetEligibleUsersForChoreModel> logger,
        IStringLocalizer<SharedResources> localizer)
    {
        _scopeFilterService = scopeFilterService;
        _db = db;
        _logger = logger;
        _localizer = localizer;
    }

    public async Task<IActionResult> OnGetAsync([FromQuery] int moleculeId)
    {
        try
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var currentUserId))
                return new JsonResult(new { success = false, message = "Not authenticated" }) { StatusCode = 401 };

            if (moleculeId <= 0)
                return new JsonResult(new { success = false, message = "Invalid moleculeId" }) { StatusCode = 400 };

            var hasAccess = await _scopeFilterService.ValidateScopeAccessAsync("molecule", moleculeId, "chores");
            if (!hasAccess)
            {
                var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == currentUserId);
                var userCompany = user != null ? await _db.Companies.FindAsync(user.CompanyId) : null;
                if (userCompany?.MoleculeId != moleculeId)
                {
                    _logger.LogWarning("SECURITY: User {UserId} attempted to access chore-eligible users for molecule {MoleculeId} outside scope",
                        currentUserId, moleculeId);
                    return new JsonResult(new { success = false, message = "Access denied" }) { StatusCode = 403 };
                }
            }

            // SECURITY-AUDITED: SAFE — molecule scope verified above; IgnoreQueryFilters needed
            // to surface cross-company users within the molecule
            var users = await _db.Users.IgnoreQueryFilters()
                .Where(u => u.IsActive
                    && _db.Companies.IgnoreQueryFilters()
                        .Any(c => c.Id == u.CompanyId && c.MoleculeId == moleculeId))
                .OrderBy(u => u.DisplayName)
                .Select(u => new { id = u.Id, name = u.DisplayName })
                .ToListAsync();

            return new JsonResult(new { success = true, users });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in GetEligibleUsersForChore");
            return new JsonResult(new { success = false, message = "Internal error" }) { StatusCode = 500 };
        }
    }
}
