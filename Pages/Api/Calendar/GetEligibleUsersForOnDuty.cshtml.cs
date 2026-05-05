using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using ShiftManager.Data;
using ShiftManager.Data.SeedData;
using ShiftManager.Models.Support;
using ShiftManager.Resources;
using ShiftManager.Services;
using System.Security.Claims;

namespace ShiftManager.Pages.Api.Calendar;

/// <summary>
/// Eligible users for on-duty assignment within a molecule. Cross-company within
/// molecule is intentional. Mirrors auth pattern of GetEligibleUsersForShift.
/// Optionally filters by officer rank when EnforceRankEligibility flag is on AND
/// a specific OnDutyType is supplied that requires officer rank.
/// </summary>
[Authorize]
[IgnoreAntiforgeryToken]
public class GetEligibleUsersForOnDutyModel : PageModel
{
    private readonly IScopeFilterService _scopeFilterService;
    private readonly IFeatureFlagService _featureFlagService;
    private readonly AppDbContext _db;
    private readonly ILogger<GetEligibleUsersForOnDutyModel> _logger;
    private readonly IStringLocalizer<SharedResources> _localizer;

    public GetEligibleUsersForOnDutyModel(
        IScopeFilterService scopeFilterService,
        IFeatureFlagService featureFlagService,
        AppDbContext db,
        ILogger<GetEligibleUsersForOnDutyModel> logger,
        IStringLocalizer<SharedResources> localizer)
    {
        _scopeFilterService = scopeFilterService;
        _featureFlagService = featureFlagService;
        _db = db;
        _logger = logger;
        _localizer = localizer;
    }

    public async Task<IActionResult> OnGetAsync(
        [FromQuery] int moleculeId,
        [FromQuery] int? onDutyType = null)
    {
        try
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var currentUserId))
                return new JsonResult(new { success = false, message = "Not authenticated" }) { StatusCode = 401 };

            if (moleculeId <= 0)
                return new JsonResult(new { success = false, message = "Invalid moleculeId" }) { StatusCode = 400 };

            var hasAccess = await _scopeFilterService.ValidateScopeAccessAsync("molecule", moleculeId, "oncall");
            if (!hasAccess)
            {
                var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == currentUserId);
                var userCompany = user != null ? await _db.Companies.FindAsync(user.CompanyId) : null;
                if (userCompany?.MoleculeId != moleculeId)
                {
                    _logger.LogWarning("SECURITY: User {UserId} attempted to access on-duty-eligible users for molecule {MoleculeId} outside scope",
                        currentUserId, moleculeId);
                    return new JsonResult(new { success = false, message = "Access denied" }) { StatusCode = 403 };
                }
            }

            var query = _db.Users.IgnoreQueryFilters()
                .Where(u => u.IsActive
                    && _db.Companies.IgnoreQueryFilters()
                        .Any(c => c.Id == u.CompanyId && c.MoleculeId == moleculeId));

            // Officer-rank gating: if the on-duty type requires officer rank AND the
            // EnforceRankEligibility flag is on, filter the eligible list at query time.
            // The actual hard-error firing still happens server-side in BusyService.
            if (onDutyType.HasValue
                && Enum.IsDefined(typeof(OnDutyType), onDutyType.Value))
            {
                var enforceRank = await _featureFlagService.IsEnabledAsync(FeatureFlagSeed.Flags.EnforceRankEligibility);
                if (enforceRank)
                {
                    var typeConfig = await _db.OnDutyTypeConfigs.IgnoreQueryFilters()
                        .FirstOrDefaultAsync(t => t.TypeValue == onDutyType.Value);
                    if (typeConfig?.RequiresOfficerRank == true)
                    {
                        // Officer ranks are >= some threshold — uses MilitaryRankExtensions.IsOfficer.
                        // Filter post-load (in-memory) since IsOfficer is computed.
                        var allUsers = await query
                            .Select(u => new { u.Id, u.DisplayName, u.Rank })
                            .ToListAsync();
                        var officers = allUsers
                            .Where(u => u.Rank.IsOfficer())
                            .OrderBy(u => u.DisplayName)
                            .Select(u => new { id = u.Id, name = u.DisplayName })
                            .ToList();
                        return new JsonResult(new { success = true, users = officers });
                    }
                }
            }

            var users = await query
                .OrderBy(u => u.DisplayName)
                .Select(u => new { id = u.Id, name = u.DisplayName })
                .ToListAsync();

            return new JsonResult(new { success = true, users });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in GetEligibleUsersForOnDuty");
            return new JsonResult(new { success = false, message = "Internal error" }) { StatusCode = 500 };
        }
    }
}
