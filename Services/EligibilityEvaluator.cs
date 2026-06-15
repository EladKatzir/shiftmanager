using ShiftManager.Models;
using ShiftManager.Models.Support;

namespace ShiftManager.Services;

/// <inheritdoc cref="IEligibilityEvaluator"/>
public sealed class EligibilityEvaluator : IEligibilityEvaluator
{
    public EligibilityResult Evaluate(
        AppUser user,
        IReadOnlyList<EligibilityRule> rulesForSubject,
        bool userHasExemptionForSubject)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(rulesForSubject);

        var violations = new List<EligibilityViolation>();

        foreach (var rule in rulesForSubject)
        {
            switch (rule.RuleKind)
            {
                case EligibilityRuleKind.RequiresGender:
                    // A RequiresGender rule with no target value is malformed config — ignore it
                    // rather than block everyone. Unspecified-on-the-user always violates (fail to
                    // a WARNING, per spec — the manager can still override).
                    if (rule.GenderValue.HasValue && user.Gender != rule.GenderValue.Value)
                        AddOnce(violations, EligibilityViolation.RequiresGender);
                    break;

                case EligibilityRuleKind.RequiresOfficerRank:
                    if (!user.Rank.IsOfficer())
                        AddOnce(violations, EligibilityViolation.RequiresOfficerRank);
                    break;
            }
        }

        if (userHasExemptionForSubject)
            AddOnce(violations, EligibilityViolation.Exempt);

        return violations.Count == 0 ? EligibilityResult.Eligible : new EligibilityResult(violations);
    }

    private static void AddOnce(List<EligibilityViolation> list, EligibilityViolation v)
    {
        if (!list.Contains(v)) list.Add(v);
    }
}
