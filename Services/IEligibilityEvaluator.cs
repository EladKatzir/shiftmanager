using ShiftManager.Models;

namespace ShiftManager.Services;

/// <summary>
/// One distinct eligibility failure. Severity is intentionally NOT encoded here — the caller
/// (BusyService) maps kind → severity per the spec: RequiresGender → overrideable WARNING;
/// RequiresOfficerRank and Exempt → HARD errors.
/// </summary>
public enum EligibilityViolation
{
    RequiresGender = 0,
    RequiresOfficerRank = 1,
    Exempt = 2
}

/// <summary>
/// Outcome of an eligibility evaluation for one (user, subject) pair. <see cref="IsEligible"/> is
/// just <c>Violations.Count == 0</c>; <see cref="Violations"/> lists every distinct failure so the
/// caller can render and severity-map each one.
/// </summary>
public sealed record EligibilityResult(IReadOnlyList<EligibilityViolation> Violations)
{
    public bool IsEligible => Violations.Count == 0;

    public static readonly EligibilityResult Eligible = new(Array.Empty<EligibilityViolation>());
}

/// <summary>
/// Pure, stateless evaluator of <see cref="EligibilityRule"/> rows + a per-user exemption flag
/// against a materialized <see cref="AppUser"/>. No DB access, no DI dependencies — fully unit
/// testable. Reused by BusyService (assignment gate) and ChoreService (picker hints).
/// </summary>
public interface IEligibilityEvaluator
{
    /// <param name="user">The candidate. Only <c>Gender</c> and <c>Rank</c> are read.</param>
    /// <param name="rulesForSubject">All eligibility rules already loaded for the one subject
    /// (e.g. the ChoreType). The evaluator does NOT filter by subject — the caller batch-loads
    /// the correct subject's rows.</param>
    /// <param name="userHasExemptionForSubject">True iff a UserChoreExemption row exists for this
    /// (user, subject). Modeled as a flag (not a rule) because exemptions are chore-specific.</param>
    EligibilityResult Evaluate(
        AppUser user,
        IReadOnlyList<EligibilityRule> rulesForSubject,
        bool userHasExemptionForSubject);
}
