using ShiftManager.Models;
using ShiftManager.Models.Support;

namespace ShiftManager.Services;

public record OrphanedApprovalRuleInfo(int RuleId, string ApproverGrantKey, int? JobTypeId, string Reason);

/// <summary>One selectable entry in the optional "specific approver" dropdown on the time-off form.</summary>
public record ApproverOption(int Id, string Name, string Role);

public record ApprovalPipelineStatus(
    int RequestId,
    RequestStatus Status,
    string CurrentStage,
    string? ApproverGrantKey,
    int? SpecificApproverId,
    string? SpecificApproverName,
    bool RequiresSecondApproval,
    bool IsOrphaned,
    DateTime CreatedAt);

public interface IVacationApprovalService
{
    Task<(int? ApproverId, string ApproverGrantKey, bool RequiresSecondApproval)> GetApprovalRouteAsync(int requestId);
    Task<(bool Success, string Message)> SubmitForApprovalAsync(int requestId, int submittedBy);
    Task<(bool Success, string Message)> ApproveAsync(int requestId, int approverId, string? notes = null);
    Task<(bool Success, string Message)> DeclineAsync(int requestId, int declinerId, string? reason = null);
    Task<bool> CanUserApproveAsync(int userId, int requestId);
    Task<List<TimeOffRequest>> GetPendingApprovalsForUserAsync(int userId);
    Task<List<VacationApprovalRule>> GetRulesForCompanyAsync(int companyId);
    Task<VacationApprovalRule> CreateRuleAsync(VacationApprovalRule rule);
    Task<bool> UpdateRuleAsync(VacationApprovalRule rule);
    Task<bool> DeleteRuleAsync(int ruleId);
    Task<(bool Success, string Message)> CancelRequestAsync(int requestId, int userId);
    Task<List<OrphanedApprovalRuleInfo>> DetectOrphanedRulesAsync(int companyId);
    Task<ApprovalPipelineStatus> GetApprovalStatusAsync(int requestId);

    /// <summary>
    /// Processes post-approval side effects: removes overlapping shifts, cancels trainee shadowing, sends notification.
    /// Call this after setting request status to Approved.
    /// </summary>
    Task ProcessApprovalSideEffectsAsync(int requestId);

    /// <summary>
    /// Creates an already-Approved time-off record directly from a calendar (manual entry by an
    /// editor), bypassing the request/approval queue. Authorizes the actor (calendar-note permission
    /// + reach to the target user), ties the record to the TARGET USER's own company (which — on a
    /// molecule/cross-company board — differs from the actor's active tenant), normalizes dates per
    /// type (After/DayAt = single day; DayAt requires a label), guards against overlapping approved
    /// leave, then runs the same side-effects a real approval runs (removes conflicting shift
    /// assignments, cancels trainee shadowing, notifies) and the HOME materialiser (flag-gated).
    /// Returns an ErrorKey (a SharedResources key) on failure so the caller can localize.
    /// </summary>
    Task<(bool Success, int? RequestId, string? ErrorKey)> CreateApprovedManualTimeOffAsync(
        int targetUserId, TimeOffType type,
        DateOnly startDate, DateOnly endDate, string? label, int actorUserId);

    /// <summary>
    /// Shorten an approved Vacation request's date range. Only narrowing is allowed
    /// (newStart >= original StartDate AND newEnd <= original EndDate). Triggers the
    /// materialiser to remove HOME rows for days now outside the range, and restores
    /// rotation HOME on those formerly-covered days.
    /// </summary>
    Task<(bool Success, string Message)> UpdateRequestDatesAsync(
        int requestId, DateOnly newStart, DateOnly newEnd, int actorUserId);

    /// <summary>
    /// Compute the eligible approver pool for a TimeOffRequest. Per-jobtype-vertical:
    /// - Alhut/Text requesters: Lead or Director with same JobTypeId in molecule
    /// - Hakam/BR/Other requesters: BRDirector or MoleculeAdmin in molecule
    /// Empty-pool fallback: MoleculeAdmin users in the molecule (regardless of JobType).
    /// Self-exclusion: requester is never in their own pool.
    /// </summary>
    Task<List<AppUser>> GetApproverPoolAsync(int requestId);

    /// <summary>
    /// Grant-based pool backing the optional "specific approver" dropdown on the time-off
    /// request form: active users holding ApproveVacations or ApproveExtendedLeave (CanOwn)
    /// scoped to ANY of the requester's shift-active companies (multi-company union — a
    /// single-company requester gets only approvers scoped to their own company).
    /// Distinct from <see cref="GetApproverPoolAsync"/> (job-type-vertical routing for an
    /// existing request). The requester is NOT self-excluded here; self-approval is blocked
    /// separately at approval time.
    /// </summary>
    Task<List<ApproverOption>> GetGrantBasedApproverOptionsAsync(int requesterUserId);

    /// <summary>
    /// True if <paramref name="approverUserId"/> is an eligible "specific approver" for
    /// <paramref name="requesterUserId"/> — i.e. is present in
    /// <see cref="GetGrantBasedApproverOptionsAsync"/> for that requester. This is the
    /// single source of truth shared with the request-form dropdown, so an approver that
    /// is OFFERED in the dropdown is always ACCEPTED on submit (and vice-versa). Multi-company
    /// requesters: an approver scoped to ANY of their member companies is eligible.
    /// </summary>
    Task<bool> IsEligibleApproverAsync(int requesterUserId, int approverUserId);
}
