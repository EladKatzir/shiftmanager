namespace ShiftManager.Models.Support;

/// <summary>The kind of hard requirement an <c>EligibilityRule</c> expresses. Chore-scoped — the
/// rule attaches to one ChoreType via a direct FK; shifts do NOT use this.</summary>
public enum EligibilityRuleKind
{
    RequiresGender = 0,
    RequiresOfficerRank = 1
}
