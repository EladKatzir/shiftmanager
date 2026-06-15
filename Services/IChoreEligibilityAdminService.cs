using ShiftManager.Models;
using ShiftManager.Models.Support;

namespace ShiftManager.Services;

/// <summary>
/// Admin operations over chore eligibility: the <see cref="EligibilityRule"/> set attached to a
/// chore type (replace semantics) and per-user <see cref="UserChoreExemption"/> waivers.
/// Gated at the PAGE layer by the existing <c>EditChoreTypes</c> grant — this service performs no
/// authorization. Rules are chore-scoped (keyed by <see cref="EligibilityRule.ChoreTypeId"/>); shifts do NOT use this table.
/// </summary>
public interface IChoreEligibilityAdminService
{
    /// <summary>All rules currently attached to a chore type.</summary>
    Task<List<EligibilityRule>> GetRulesForChoreTypeAsync(int choreTypeId);

    /// <summary>
    /// Replaces the chore type's eligibility rules with exactly the supplied requirements
    /// (delete-then-insert). <paramref name="requiredGender"/> null clears the gender rule;
    /// <paramref name="requiresOfficerRank"/> false clears the officer rule. <paramref name="createdBy"/>
    /// stamps the audit FK. Returns false if the chore type does not exist.
    /// </summary>
    Task<bool> SetRulesForChoreTypeAsync(int choreTypeId, Gender? requiredGender, bool requiresOfficerRank, int createdBy);

    /// <summary>Users currently exempt from a chore type.</summary>
    Task<List<UserChoreExemption>> GetExemptionsForChoreTypeAsync(int choreTypeId);

    /// <summary>
    /// Adds a waiver for (user, type). Idempotent: returns the existing row if one is already present
    /// (does not duplicate, does not overwrite the reason). Returns null if the chore type or user is missing.
    /// </summary>
    Task<UserChoreExemption?> AddExemptionAsync(int userId, int choreTypeId, string? reason, int createdBy);

    /// <summary>Removes the (user, type) waiver. Returns false if no such waiver exists.</summary>
    Task<bool> RemoveExemptionAsync(int userId, int choreTypeId);
}
