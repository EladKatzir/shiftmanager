using ShiftManager.Models.Support;

namespace ShiftManager.Models;

/// <summary>
/// A chore-scoped eligibility requirement attached to one ChoreType (gender or officer-rank).
/// A chore type may carry several rules. Global config table — no tenant filter. Evaluated by
/// IEligibilityEvaluator as gates at assignment time (gender → overrideable warning; officer → hard).
/// Chore-specific by design — shifts do NOT use this table.
/// </summary>
public class EligibilityRule
{
    public int Id { get; set; }
    public int ChoreTypeId { get; set; }
    public EligibilityRuleKind RuleKind { get; set; }   // RequiresGender | RequiresOfficerRank
    public Gender? GenderValue { get; set; }            // set iff RuleKind == RequiresGender
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int CreatedBy { get; set; }

    public ChoreType ChoreType { get; set; } = null!;
    public AppUser? Creator { get; set; }
}
