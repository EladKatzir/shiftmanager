using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using ShiftManager.Data;
using ShiftManager.Resources;
using ShiftManager.Services;
using ShiftManager.Services.Eligibility;

namespace ShiftManager.Pages.Scheduling.Eligibility;

/// <summary>
/// The single discoverable Eligibility editor (Issue 3). By-category: pick a category, see every
/// candidate classified eligible/why-not, toggle membership inline. By-person: pick a person, see
/// every category they can/can't do and why. Replaces the old three-page scavenger hunt.
///
/// Policy MUST match the NavRegistry node policy for this route (P2: visibility == access).
///
/// Issue #3 (2026-07): the page used to pin all data to the caller's login home-molecule claim
/// with no selector, so a project-scoped Owner (or area-scoped AreaAdmin) could never reach users
/// outside their own home molecule. It also had a latent cross-molecule IDOR — the mutating/detail
/// handlers performed no caller-scope check against the target's molecule at all. Both are fixed
/// here: a scope-aware molecule selector (mirrors Pages/Owner/Blueprints.cshtml.cs, minus its
/// area filter — that would wrongly confine a project-scoped Owner to one area) plus an explicit
/// ManageShiftCategories-for-the-target's-molecule re-check in every handler that reads or writes
/// a specific user/category. The MoleculeId query parameter is never trusted as proof of
/// authorization by itself — every handler re-derives the TARGET's molecule server-side and
/// checks the caller's grant against that resolved value.
/// </summary>
[Authorize(Policy = "Grant:ManageShiftCategories")]
public class IndexModel : PageModel
{
    private const string GrantKey = "ManageShiftCategories";

    private readonly IEligibilityQueryService _elig;
    private readonly IShiftCategoryService _shiftCats;
    private readonly IChoreCategoryService _choreCats;
    private readonly ICurrentUserService _currentUser;
    private readonly IGrantService _grantService;
    private readonly AppDbContext _db;
    private readonly IStringLocalizer<SharedResources> _loc;

    public IndexModel(
        IEligibilityQueryService elig,
        IShiftCategoryService shiftCats,
        IChoreCategoryService choreCats,
        ICurrentUserService currentUser,
        IGrantService grantService,
        AppDbContext db,
        IStringLocalizer<SharedResources> loc)
    {
        _elig = elig;
        _shiftCats = shiftCats;
        _choreCats = choreCats;
        _currentUser = currentUser;
        _grantService = grantService;
        _db = db;
        _loc = loc;
    }

    public record CategoryOption(int Id, string Name);

    /// <summary>A molecule the caller may select in the scope picker.</summary>
    public record MoleculeOption(int Id, string Name);

    /// <summary>
    /// The molecule whose categories/users are displayed. Bound from the querystring so the
    /// selector can auto-submit a GET — but NEVER trusted as-is: <see cref="OnGetAsync"/> clamps
    /// it to the caller's own <c>GetAccessibleMoleculeIdsForGrantAsync</c> set below.
    /// </summary>
    [BindProperty(SupportsGet = true)]
    public int? MoleculeId { get; set; }

    /// <summary>Molecules the caller may pick from (their ManageShiftCategories scope, cascaded: project -> every molecule in it, area -> the area's, molecule -> just their own).</summary>
    public List<MoleculeOption> AvailableMolecules { get; private set; } = new();

    public List<CategoryOption> ShiftCategoryOptions { get; private set; } = new();
    public List<CategoryOption> ChoreCategoryOptions { get; private set; } = new();
    public List<UserOption> UserOptions { get; private set; } = new();

    public async Task OnGetAsync()
    {
        var callerId = _currentUser.UserId;

        // Scope-aware molecule set (Issue #3): project -> every molecule in the project,
        // area -> the area's molecules, molecule-scoped role -> just their own. Deliberately NOT
        // area-filtered like Blueprints.cshtml.cs — that would wrongly confine a project-scoped
        // Owner to a single area.
        var accessibleMoleculeIds = await _grantService.GetAccessibleMoleculeIdsForGrantAsync(callerId, GrantKey);
        if (accessibleMoleculeIds.Count == 0)
            return;

        AvailableMolecules = await _db.Molecules
            .Where(m => accessibleMoleculeIds.Contains(m.Id) && m.IsActive)
            .OrderBy(m => m.DisplayName)
            .Select(m => new MoleculeOption(m.Id, m.DisplayName))
            .ToListAsync();

        // The querystring value is untrusted input: clamp to the caller's accessible set rather
        // than loading whatever molecule a crafted request asks for. Default to the caller's home
        // molecule (back-compat with the pre-fix behavior) when unset or out of scope.
        if (!MoleculeId.HasValue || !accessibleMoleculeIds.Contains(MoleculeId.Value))
        {
            MoleculeId = _currentUser.MoleculeId.HasValue && accessibleMoleculeIds.Contains(_currentUser.MoleculeId.Value)
                ? _currentUser.MoleculeId
                : accessibleMoleculeIds[0];
        }

        if (!MoleculeId.HasValue) return;

        string Disp(string name, string display) => string.IsNullOrWhiteSpace(display) ? name : display;

        ShiftCategoryOptions = (await _shiftCats.GetCategoriesForMoleculeAsync(MoleculeId.Value))
            .Select(c => new CategoryOption(c.Id, Disp(c.Name, c.DisplayName))).ToList();
        ChoreCategoryOptions = (await _choreCats.GetCategoriesForMoleculeAsync(MoleculeId.Value))
            .Select(c => new CategoryOption(c.Id, Disp(c.Name, c.DisplayName))).ToList();
        UserOptions = (await _elig.GetMoleculeUsersAsync(MoleculeId.Value)).ToList();
    }

    public async Task<IActionResult> OnGetCategoryAsync(int id, bool chore)
    {
        // SECURITY (IDOR close): resolve the category's own molecule first — never the caller's
        // selected MoleculeId — and require the caller's grant actually covers it.
        var targetMoleculeId = chore
            ? (await _choreCats.GetCategoryAsync(id))?.MoleculeId
            : (await _shiftCats.GetCategoryAsync(id))?.MoleculeId;
        if (targetMoleculeId is null) return NotFound();
        if (!await _grantService.HasGrantWithScopeAsync(_currentUser.UserId, GrantKey, moleculeId: targetMoleculeId.Value))
            return Forbidden();

        var view = chore ? await _elig.GetChoreCategoryCandidatesAsync(id) : await _elig.GetShiftCategoryCandidatesAsync(id);
        if (view is null) return NotFound();
        return new JsonResult(new
        {
            name = view.Name,
            isChore = view.IsChore,
            candidates = view.Candidates.Select(c => new
            {
                userId = c.UserId,
                name = c.Name,
                eligible = c.Eligible,
                member = !c.Reasons.Contains(EligibilityReason.NotCategoryMember),
                reasons = c.Reasons.Select(r => ReasonText(r, view.IsChore)).ToList()
            })
        });
    }

    public async Task<IActionResult> OnGetPersonAsync(int userId)
    {
        // SECURITY (IDOR close): resolve the TARGET user's own molecule (via their company), not
        // the caller's selected MoleculeId, and require the caller's grant covers it.
        var targetMoleculeId = await GetUserMoleculeIdAsync(userId);
        if (targetMoleculeId is null) return NotFound();
        if (!await _grantService.HasGrantWithScopeAsync(_currentUser.UserId, GrantKey, moleculeId: targetMoleculeId.Value))
            return Forbidden();

        var view = await _elig.GetUserEligibilityAsync(userId);
        if (view is null) return NotFound();
        object Map(CategoryEligibility c) => new
        {
            categoryId = c.CategoryId,
            name = c.Name,
            isChore = c.IsChore,
            eligible = c.Eligible,
            member = c.IsMember,
            reasons = c.Reasons.Select(r => ReasonText(r, c.IsChore)).ToList()
        };
        return new JsonResult(new
        {
            name = view.Name,
            shift = view.ShiftCategories.Select(Map),
            chore = view.ChoreCategories.Select(Map)
        });
    }

    public class ToggleRequest
    {
        public int UserId { get; set; }
        public int CategoryId { get; set; }
        public bool IsChore { get; set; }
        public bool Add { get; set; }
    }

    public async Task<IActionResult> OnPostToggleAsync([FromBody] ToggleRequest req)
    {
        // SECURITY (IDOR close): this mutates a (user, category) membership pair, so BOTH targets'
        // molecules must be resolved and re-checked against the caller's grant — checking only the
        // category (or only the user) would leave the other half of the pair as an open IDOR (e.g.
        // an in-scope category but a forged UserId belonging to a molecule the caller can't touch).
        // The request is never trusted to self-report its own scope.
        var categoryMoleculeId = req.IsChore
            ? (await _choreCats.GetCategoryAsync(req.CategoryId))?.MoleculeId
            : (await _shiftCats.GetCategoryAsync(req.CategoryId))?.MoleculeId;
        if (categoryMoleculeId is null) return NotFound();

        var userMoleculeId = await GetUserMoleculeIdAsync(req.UserId);
        if (userMoleculeId is null) return NotFound();

        var callerId = _currentUser.UserId;
        var canManageCategory = await _grantService.HasGrantWithScopeAsync(callerId, GrantKey, moleculeId: categoryMoleculeId.Value);
        var canManageUser = await _grantService.HasGrantWithScopeAsync(callerId, GrantKey, moleculeId: userMoleculeId.Value);
        if (!canManageCategory || !canManageUser)
            return Forbidden();

        if (req.IsChore)
        {
            var ids = await _choreCats.GetUserCategoryIdsAsync(req.UserId);
            if (req.Add) { if (!ids.Contains(req.CategoryId)) ids.Add(req.CategoryId); }
            else ids.Remove(req.CategoryId);
            await _choreCats.SetUserCategoriesAsync(req.UserId, ids.Distinct().ToList());
        }
        else
        {
            var ids = await _shiftCats.GetUserCategoryIdsAsync(req.UserId);
            if (req.Add) { if (!ids.Contains(req.CategoryId)) ids.Add(req.CategoryId); }
            else ids.Remove(req.CategoryId);
            await _shiftCats.SetUserCategoriesAsync(req.UserId, ids.Distinct().ToList());
        }
        return new JsonResult(new { ok = true });
    }

    /// <summary>
    /// Resolves a user's molecule via their company (mirrors the molecule resolution in
    /// Pages/Admin/Users.cshtml.cs OnPostUserCategoriesAsync). Null when the user or their
    /// company/molecule can't be resolved — callers must treat that as "deny", never as "no
    /// scope requested" (passing a null moleculeId into HasGrantWithScopeAsync/GetAccessible*
    /// falls back to the CALLER's own ambient hierarchy for some scope shapes, which would wrongly
    /// authorize an unresolvable target instead of rejecting it).
    /// </summary>
    // SECURITY-AUDITED: SAFE — IgnoreQueryFilters is required for cross-company/cross-molecule
    // target resolution; the only thing derived from this query is the numeric MoleculeId fed into
    // the caller's HasGrantWithScopeAsync check immediately after — no user data is returned.
    private async Task<int?> GetUserMoleculeIdAsync(int userId)
        => await (from u in _db.Users.IgnoreQueryFilters()
                  join c in _db.Companies.IgnoreQueryFilters() on u.CompanyId equals c.Id
                  where u.Id == userId
                  select (int?)c.MoleculeId).FirstOrDefaultAsync();

    private static JsonResult Forbidden() =>
        new(new { success = false, ok = false, error = "Forbidden" }) { StatusCode = 403 };

    private string ReasonText(EligibilityReason r, bool isChore) => r switch
    {
        EligibilityReason.AccountTypeIneligible => _loc["Elig_Reason_AccountType"],
        EligibilityReason.NotParticipating => isChore ? _loc["Elig_Reason_NoChores"] : _loc["Elig_Reason_NoShifts"],
        EligibilityReason.NotCategoryMember => _loc["Elig_Reason_NotMember"],
        EligibilityReason.RequiresGender => _loc["Elig_RequiresGender"],
        EligibilityReason.RequiresOfficerRank => _loc["Elig_RequiresOfficerRank"],
        EligibilityReason.Exempt => _loc["Elig_Exempt"],
        _ => r.ToString()
    };
}
