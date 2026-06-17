using System.Collections.Generic;
using System.Linq;
using ShiftManager.Models.Support;

namespace ShiftManager.Services.Eligibility;

/// <summary>
/// Pure (no DB), unit-testable classification of WHY a user is (in)eligible for a category.
/// Mirrors the silent set-membership predicates used by the shift candidate leaf methods
/// (<c>ShiftAssignmentService</c>/<c>ShiftCalendarService</c>, categoryFilter:true) and the
/// chore participant query (<c>Chores.cshtml.cs</c>) — but EMITS reasons instead of filtering
/// silently. Chore RULE violations are reused verbatim from <see cref="IEligibilityEvaluator"/>.
/// </summary>
public static class EligibilityClassifier
{
    /// <summary>
    /// The participation + membership layer, shared by both axes.
    /// Shifts exclude GroupUser; chores are Standard-account only.
    /// </summary>
    public static List<EligibilityReason> ClassifyMembership(AccountType accountType, bool participates, bool isMember, bool isChore)
    {
        var reasons = new List<EligibilityReason>();
        var accountOk = isChore ? accountType == AccountType.Standard : accountType != AccountType.GroupUser;
        if (!accountOk) reasons.Add(EligibilityReason.AccountTypeIneligible);
        if (!participates) reasons.Add(EligibilityReason.NotParticipating);
        if (!isMember) reasons.Add(EligibilityReason.NotCategoryMember);
        return reasons;
    }

    /// <summary>Maps the chore evaluator's rule violations into the unified reason vocabulary.</summary>
    public static IEnumerable<EligibilityReason> MapRuleViolations(EligibilityResult ruleResult)
        => ruleResult.Violations.Select(v => v switch
        {
            EligibilityViolation.RequiresGender => EligibilityReason.RequiresGender,
            EligibilityViolation.RequiresOfficerRank => EligibilityReason.RequiresOfficerRank,
            EligibilityViolation.Exempt => EligibilityReason.Exempt,
            _ => EligibilityReason.NotParticipating
        });

    public static bool IsEligible(IReadOnlyList<EligibilityReason> reasons) => reasons.Count == 0;
}
