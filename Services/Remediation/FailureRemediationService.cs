using Microsoft.Extensions.Localization;
using ShiftManager.Resources;

namespace ShiftManager.Services.Remediation;

/// <summary>A "go fix it" action attached to a failure: a localized label + the URL of the exact place to change it.</summary>
public sealed record Remediation(string Label, string Url);

/// <summary>
/// Maps a validation/eligibility failure code to the concrete place to go fix it (the "why + button"
/// pattern). Centralised so every assign/change endpoint produces the same remediation for the same
/// reason. The fix URL is context-aware (e.g. officer-rank → that user's profile).
/// </summary>
public interface IFailureRemediationService
{
    /// <summary>The remediation for a failure key, or null if there's no self-service fix.</summary>
    Remediation? For(string? key, int? userId = null, int? choreTypeId = null);
}

public class FailureRemediationService : IFailureRemediationService
{
    private readonly IStringLocalizer<SharedResources> _loc;

    public FailureRemediationService(IStringLocalizer<SharedResources> loc) => _loc = loc;

    public Remediation? For(string? key, int? userId = null, int? choreTypeId = null)
    {
        if (string.IsNullOrEmpty(key)) return null;

        string profile = userId is int u ? $"/Admin/EditProfile?UserId={u}" : "/Admin/Users";

        return key switch
        {
            // rank — promote the user (or, for chores, edit the rule / membership)
            "OFFICER_RANK_REQUIRED" or "ELIG_OFFICER_RANK" => new(_loc["Fix_ChangeRank"], profile),
            // gender mismatch — fix the user's recorded gender
            "ELIG_GENDER" => new(_loc["Fix_EditProfile"], profile),
            // chore exemption / officer-or-gender RULE — edit the chore type's rules
            "ELIG_EXEMPT" => new(_loc["Fix_ChoreRules"], "/Admin/Organization/ChoreTypes"),
            // participation / account type — manage the user
            "ACCOUNT_CANNOT_DO_CHORES" or "GROUPUSER_CANNOT_BE_ASSIGNED" or "USER_INACTIVE" => new(_loc["Fix_ManageUser"], "/Admin/Users"),
            // who-can-do-what (category membership / company eligibility) — the Eligibility editor
            "COMPANY_INELIGIBLE" or "NOT_IN_SHIFT_GROUPING" or "USER_NOT_IN_MOLECULE" => new(_loc["Fix_Eligibility"], "/Scheduling/Eligibility"),
            _ => null
        };
    }
}
