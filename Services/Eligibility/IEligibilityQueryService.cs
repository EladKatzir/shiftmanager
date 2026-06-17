using System.Collections.Generic;
using System.Threading.Tasks;

namespace ShiftManager.Services.Eligibility;

/// <summary>
/// The query engine behind the Eligibility editor (Issue 3). Answers the two questions the old
/// three-page scavenger hunt couldn't: "who is eligible for category X (and why not)?" (By-category)
/// and "which categories can person Y do (and why are they blocked)?" (By-person).
///
/// Scope: the membership + participation axis (DoesShifts/DoesChores + category membership +
/// account type) which is the editable part. Chore RULE blocks (gender/officer/exempt) are
/// per chore-TYPE and remain surfaced in the ChoreTypes admin; this engine focuses on the axis
/// the editor edits.
/// </summary>
public interface IEligibilityQueryService
{
    /// <summary>All active users in the category's molecule, each classified eligible/why-not. Null if the category doesn't exist.</summary>
    Task<CategoryCandidatesView?> GetShiftCategoryCandidatesAsync(int shiftCategoryId);

    /// <summary>Chore equivalent (Standard accounts + DoesChores + chore-category membership).</summary>
    Task<CategoryCandidatesView?> GetChoreCategoryCandidatesAsync(int choreCategoryId);

    /// <summary>For one user: every shift + chore category in their molecule, with eligibility + reasons. Null if the user doesn't exist.</summary>
    Task<UserEligibilityView?> GetUserEligibilityAsync(int userId);
}

/// <summary>A category and its classified candidate list.</summary>
public sealed record CategoryCandidatesView(int CategoryId, string Name, bool IsChore, IReadOnlyList<CandidateEligibility> Candidates);

/// <summary>A user and their per-category eligibility across both axes.</summary>
public sealed record UserEligibilityView(
    int UserId,
    string Name,
    IReadOnlyList<CategoryEligibility> ShiftCategories,
    IReadOnlyList<CategoryEligibility> ChoreCategories);
