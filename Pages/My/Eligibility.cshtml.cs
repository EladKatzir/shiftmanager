using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using ShiftManager.Resources;
using ShiftManager.Services;
using ShiftManager.Services.Eligibility;

namespace ShiftManager.Pages.My;

/// <summary>
/// Employee-facing, READ-ONLY "why am I (in)eligible?" surface. For the signed-in user only, it
/// lists which shift/chore categories they can be assigned to and — where they can't — the plain
/// reason (account type, shifts/chores turned off, or not a member of the group). These reasons are
/// manager-managed, so this page EXPLAINS ("ask your manager") rather than offering a self-service
/// fix; the manager-facing "why + fix button" lives on the assignment failure instead. This is the
/// self-scoped mirror of the By-person Eligibility editor view (Issue 3, Phase 3).
///
/// SECURITY: only ever queries the caller's OWN id (<see cref="ICurrentUserService.UserId"/>) —
/// never a bound parameter — so plain <c>[Authorize]</c> is safe (no info disclosure), even though
/// <see cref="IEligibilityQueryService"/> reads with IgnoreQueryFilters.
/// </summary>
[Authorize]
public class EligibilityModel : PageModel
{
    private readonly IEligibilityQueryService _elig;
    private readonly ICurrentUserService _currentUser;
    private readonly IStringLocalizer<SharedResources> _loc;

    public EligibilityModel(
        IEligibilityQueryService elig,
        ICurrentUserService currentUser,
        IStringLocalizer<SharedResources> loc)
    {
        _elig = elig;
        _currentUser = currentUser;
        _loc = loc;
    }

    /// <summary>One category line: its name, whether the user is eligible, and (if not) the reason.</summary>
    public sealed record Row(string Name, bool Eligible, string? Reason);

    public List<Row> Shifts { get; private set; } = new();
    public List<Row> Chores { get; private set; } = new();

    public bool HasAnyCategories => Shifts.Count > 0 || Chores.Count > 0;

    public async Task OnGetAsync()
    {
        // Self only — never trust a parameter for the id.
        var view = await _elig.GetUserEligibilityAsync(_currentUser.UserId);
        if (view is null) return;

        Shifts = view.ShiftCategories.Select(ToRow).ToList();
        Chores = view.ChoreCategories.Select(ToRow).ToList();
    }

    private Row ToRow(CategoryEligibility c)
        => new(c.Name, c.Eligible, c.Eligible ? null : FirstReason(c.Reasons, c.IsChore));

    /// <summary>
    /// The first (most fundamental) blocking reason, framed for the employee in the first person.
    /// <see cref="EligibilityClassifier.ClassifyMembership"/> orders reasons account → participation
    /// → membership, so reasons[0] is the deepest block to surface.
    /// </summary>
    private string FirstReason(IReadOnlyList<EligibilityReason> reasons, bool isChore)
    {
        if (reasons.Count == 0) return string.Empty;
        return reasons[0] switch
        {
            EligibilityReason.AccountTypeIneligible => _loc["MyElig_Reason_AccountType"],
            EligibilityReason.NotParticipating => isChore ? _loc["MyElig_Reason_NoChores"] : _loc["MyElig_Reason_NoShifts"],
            EligibilityReason.NotCategoryMember => _loc["MyElig_Reason_NotMember"],
            _ => _loc["MyElig_Reason_NotMember"]
        };
    }
}
