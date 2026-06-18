using System.Collections.Generic;

namespace ShiftManager.Services.Eligibility;

/// <summary>
/// Unified, surfaceable reason a user is (in)eligible for a shift/chore category. This is the gap
/// the redesign fills: shift eligibility was a silent set-membership filter with NO per-user reason;
/// chore eligibility had reasons (<see cref="Models.Support.EligibilityViolation"/>) but only per
/// chore-type. The Eligibility editor + the employee "why am I (in)eligible?" surface both need a
/// single reason vocabulary.
/// </summary>
public enum EligibilityReason
{
    /// <summary>GroupUser (shifts) or any non-Standard account (chores) is excluded from candidate sets.</summary>
    AccountTypeIneligible = 0,
    /// <summary>The user's DoesShifts / DoesChores master flag is off.</summary>
    NotParticipating = 1,
    /// <summary>The user is not a member of this shift/chore category.</summary>
    NotCategoryMember = 2,
    /// <summary>A chore rule requires a specific gender the user doesn't match (overrideable warning).</summary>
    RequiresGender = 3,
    /// <summary>A chore rule requires officer rank the user doesn't hold (hard block).</summary>
    RequiresOfficerRank = 4,
    /// <summary>The user has a chore exemption (waiver) for this subject (hard block).</summary>
    Exempt = 5
}

/// <summary>One candidate's eligibility for a category: eligible flag + the ordered reasons they're blocked.</summary>
public sealed record CandidateEligibility(int UserId, string Name, bool Eligible, IReadOnlyList<EligibilityReason> Reasons);

/// <summary>One category's eligibility status for a user (used by the By-person view).</summary>
public sealed record CategoryEligibility(int CategoryId, string Name, bool IsChore, bool Eligible, bool IsMember, IReadOnlyList<EligibilityReason> Reasons);
