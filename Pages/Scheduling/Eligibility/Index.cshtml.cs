using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
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
/// </summary>
[Authorize(Policy = "Grant:ManageShiftCategories")]
public class IndexModel : PageModel
{
    private readonly IEligibilityQueryService _elig;
    private readonly IShiftCategoryService _shiftCats;
    private readonly IChoreCategoryService _choreCats;
    private readonly ICurrentUserService _currentUser;
    private readonly IStringLocalizer<SharedResources> _loc;

    public IndexModel(
        IEligibilityQueryService elig,
        IShiftCategoryService shiftCats,
        IChoreCategoryService choreCats,
        ICurrentUserService currentUser,
        IStringLocalizer<SharedResources> loc)
    {
        _elig = elig;
        _shiftCats = shiftCats;
        _choreCats = choreCats;
        _currentUser = currentUser;
        _loc = loc;
    }

    public record CategoryOption(int Id, string Name);

    public List<CategoryOption> ShiftCategoryOptions { get; private set; } = new();
    public List<CategoryOption> ChoreCategoryOptions { get; private set; } = new();
    public List<UserOption> UserOptions { get; private set; } = new();

    public async Task OnGetAsync()
    {
        var molecule = _currentUser.MoleculeId;
        if (molecule is null) return;

        string Disp(string name, string display) => string.IsNullOrWhiteSpace(display) ? name : display;

        ShiftCategoryOptions = (await _shiftCats.GetCategoriesForMoleculeAsync(molecule.Value))
            .Select(c => new CategoryOption(c.Id, Disp(c.Name, c.DisplayName))).ToList();
        ChoreCategoryOptions = (await _choreCats.GetCategoriesForMoleculeAsync(molecule.Value))
            .Select(c => new CategoryOption(c.Id, Disp(c.Name, c.DisplayName))).ToList();
        UserOptions = (await _elig.GetMoleculeUsersAsync(molecule.Value)).ToList();
    }

    public async Task<IActionResult> OnGetCategoryAsync(int id, bool chore)
    {
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
