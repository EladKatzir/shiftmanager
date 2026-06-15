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
/// At-a-glance eligibility hint for a (user × choreType) pair, for the assignment picker.
/// Returns hard-block reasons (officer/exempt) and warnings (gender) as localized strings.
/// This is a DISPLAY hint only — the authoritative gate is BusyService.ValidateChoreAsync.
/// Mirrors the auth/scope pattern of GetEligibleUsersForChore.
/// </summary>
[Authorize(Policy = "Grant:AssignChores")]
[IgnoreAntiforgeryToken]
public class GetChoreEligibilityForCandidateModel : PageModel
{
    private readonly IChoreService _choreService;
    private readonly IScopeFilterService _scopeFilterService;
    private readonly AppDbContext _db;
    private readonly IStringLocalizer<SharedResources> _localizer;
    private readonly ILogger<GetChoreEligibilityForCandidateModel> _logger;

    public GetChoreEligibilityForCandidateModel(
        IChoreService choreService,
        IScopeFilterService scopeFilterService,
        AppDbContext db,
        IStringLocalizer<SharedResources> localizer,
        ILogger<GetChoreEligibilityForCandidateModel> logger)
    {
        _choreService = choreService;
        _scopeFilterService = scopeFilterService;
        _db = db;
        _localizer = localizer;
        _logger = logger;
    }

    public async Task<IActionResult> OnGetAsync([FromQuery] int moleculeId, [FromQuery] int userId, [FromQuery] int choreTypeId)
    {
        try
        {
            var idClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(idClaim) || !int.TryParse(idClaim, out var currentUserId))
                return new JsonResult(new { success = false, message = "Not authenticated" }) { StatusCode = 401 };
            if (moleculeId <= 0 || userId <= 0 || choreTypeId <= 0)
                return new JsonResult(new { success = false, message = "Invalid parameters" }) { StatusCode = 400 };

            // Scope check mirrors GetEligibleUsersForChore: caller must have chores access to this molecule.
            var hasAccess = await _scopeFilterService.ValidateScopeAccessAsync("molecule", moleculeId, "chores");
            if (!hasAccess)
            {
                var caller = await _db.Users.FirstOrDefaultAsync(u => u.Id == currentUserId);
                var callerCompany = caller != null ? await _db.Companies.FindAsync(caller.CompanyId) : null;
                if (callerCompany?.MoleculeId != moleculeId)
                {
                    _logger.LogWarning("SECURITY: User {UserId} probed chore eligibility for molecule {MoleculeId} outside scope", currentUserId, moleculeId);
                    return new JsonResult(new { success = false, message = "Access denied" }) { StatusCode = 403 };
                }
            }

            // Defense-in-depth: the target user AND the chore type must both belong to this molecule.
            // SECURITY-AUDITED: SAFE — molecule scope verified above; IgnoreQueryFilters surfaces
            // cross-company users within the molecule (chores are molecule-scoped, not tenant-scoped).
            var userInMolecule = await _db.Users.IgnoreQueryFilters().AnyAsync(u => u.Id == userId
                && _db.Companies.IgnoreQueryFilters().Any(c => c.Id == u.CompanyId && c.MoleculeId == moleculeId));
            var typeInMolecule = await _db.ChoreTypes.AnyAsync(t => t.Id == choreTypeId && t.MoleculeId == moleculeId);
            if (!userInMolecule || !typeInMolecule)
                return new JsonResult(new { success = false, message = "Out of scope" }) { StatusCode = 400 };

            var result = await _choreService.GetEligibilityForCandidateAsync(userId, choreTypeId);

            // Severity mapping (spec §5): officer + exempt are HARD; gender is a WARNING.
            var hardReasons = new List<string>();
            var warnings = new List<string>();
            foreach (var v in result.Violations)
            {
                switch (v)
                {
                    case EligibilityViolation.RequiresOfficerRank:
                        hardReasons.Add(_localizer["Elig_RequiresOfficerRank"].Value); break;
                    case EligibilityViolation.Exempt:
                        hardReasons.Add(_localizer["Elig_Exempt"].Value); break;
                    case EligibilityViolation.RequiresGender:
                        warnings.Add(_localizer["Elig_RequiresGender"].Value); break;
                }
            }

            return new JsonResult(new
            {
                success = true,
                userId,
                choreTypeId,
                isHardBlocked = hardReasons.Count > 0,
                hardReasons,
                warnings
            });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error in GetChoreEligibilityForCandidate");
            return new JsonResult(new { success = false, message = "Internal error" }) { StatusCode = 500 };
        }
    }
}
