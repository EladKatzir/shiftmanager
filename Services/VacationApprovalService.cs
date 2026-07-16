using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using ShiftManager.Data;
using ShiftManager.Data.SeedData;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Resources;

namespace ShiftManager.Services;

// SECURITY-AUDITED: All IgnoreQueryFilters() in this class are SAFE — vacation approval requires cross-company visibility
// for Directors managing multiple companies; all queries scoped by explicit requestId/userId/companyId parameters
public class VacationApprovalService : IVacationApprovalService
{
    private static readonly HashSet<int> AlhutTextJobTypeIds = new() { 1, 3 };

    private readonly AppDbContext _context;
    private readonly IGrantService _grantService;
    private readonly ILogger<VacationApprovalService> _logger;
    private readonly INotificationService _notificationService;
    private readonly ITraineeService _traineeService;
    private readonly IHomeMaterialiserService _materialiser;
    private readonly IAuditLogService _auditLogService;
    private readonly IStringLocalizer<SharedResources> _localizer;
    private readonly IFeatureFlagService _featureFlagService;
    private readonly ICompanyMembershipService _membershipService;

    public VacationApprovalService(
        AppDbContext context,
        IGrantService grantService,
        ILogger<VacationApprovalService> logger,
        INotificationService notificationService,
        ITraineeService traineeService,
        IHomeMaterialiserService materialiser,
        IAuditLogService auditLogService,
        IStringLocalizer<SharedResources> localizer,
        IFeatureFlagService featureFlagService,
        ICompanyMembershipService membershipService)
    {
        _context = context;
        _grantService = grantService;
        _logger = logger;
        _notificationService = notificationService;
        _traineeService = traineeService;
        _materialiser = materialiser;
        _auditLogService = auditLogService;
        _localizer = localizer;
        _featureFlagService = featureFlagService;
        _membershipService = membershipService;
    }

    /// <summary>
    /// Determines the approval route for a given time-off request.
    /// Finds matching VacationApprovalRule by JobTypeId and CompanyId,
    /// handles auto-approve for short leaves, and flags extended leave for second approval.
    /// </summary>
    public async Task<(int? ApproverId, string ApproverGrantKey, bool RequiresSecondApproval)> GetApprovalRouteAsync(int requestId)
    {
        // 1. Load the TimeOffRequest with user info
        // IgnoreQueryFilters: request/user/rules may be in a different company than current tenant
        var request = await _context.TimeOffRequests
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.Id == requestId);

        if (request == null)
        {
            _logger.LogWarning("GetApprovalRouteAsync: TimeOffRequest {RequestId} not found", requestId);
            return (null, "ApproveVacations", false);
        }

        var user = await _context.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == request.UserId);
        int? userJobTypeId = user?.JobTypeId;

        // 2. Find matching VacationApprovalRule
        //    Order by Priority desc, JobTypeId-specific first, then default (null JobTypeId)
        var rules = await _context.VacationApprovalRules
            .IgnoreQueryFilters()
            .Where(r => r.CompanyId == request.CompanyId && r.IsActive)
            .OrderByDescending(r => r.Priority)
            .ThenByDescending(r => r.JobTypeId != null ? 1 : 0)  // JobType-specific rules first
            .ToListAsync();

        // Find the best matching rule: first try JobType-specific, then default
        var matchingRule = rules.FirstOrDefault(r => r.JobTypeId != null && r.JobTypeId == userJobTypeId)
                        ?? rules.FirstOrDefault(r => r.JobTypeId == null);

        // 3. If no rule found, fall back to any user with "ApproveVacations" grant
        if (matchingRule == null)
        {
            _logger.LogInformation(
                "No approval rule found for request {RequestId}, company {CompanyId}. Falling back to default grant.",
                requestId, request.CompanyId);
            return (null, "ApproveVacations", false);
        }

        // 4. Calculate leave days
        int leaveDays = request.EndDate.DayNumber - request.StartDate.DayNumber + 1;

        // 5. Auto-approve if within threshold
        if (matchingRule.MaxAutoApproveDays > 0 && leaveDays <= matchingRule.MaxAutoApproveDays)
        {
            _logger.LogInformation(
                "Request {RequestId} eligible for auto-approve ({LeaveDays} days <= {MaxDays} day limit)",
                requestId, leaveDays, matchingRule.MaxAutoApproveDays);
            return (null, matchingRule.ApproverGrantKey, false);
        }

        // 6. Check if extended leave requires second approval
        bool requiresSecondApproval = matchingRule.RequiresSecondApproval
            && leaveDays > matchingRule.ExtendedLeaveDaysThreshold;

        return (matchingRule.ApproverUserId, matchingRule.ApproverGrantKey, requiresSecondApproval);
    }

    /// <summary>
    /// Submits a time-off request for approval. Handles auto-approve when applicable.
    /// </summary>
    public async Task<(bool Success, string Message)> SubmitForApprovalAsync(int requestId, int submittedBy)
    {
        // IgnoreQueryFilters: request may be in a different company than current tenant
        var request = await _context.TimeOffRequests
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.Id == requestId);

        if (request == null)
        {
            return (false, "VacationApproval_RequestNotFound");
        }

        if (request.Status != RequestStatus.Pending)
        {
            return (false, "VacationApproval_AlreadyProcessed");
        }

        var (approverId, approverGrantKey, requiresSecondApproval) = await GetApprovalRouteAsync(requestId);

        // Wrap in transaction to prevent concurrent submissions
        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            // Re-check status inside transaction
            var freshRequest = await _context.TimeOffRequests
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(r => r.Id == requestId);
            if (freshRequest == null || freshRequest.Status != RequestStatus.Pending)
            {
                await transaction.RollbackAsync();
                return (false, "VacationApproval_AlreadyProcessed");
            }

            // Set specific approver if rule defines one
            if (approverId.HasValue)
            {
                freshRequest.ApproverId = approverId.Value;
            }

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Failed to submit vacation request {RequestId} for approval", requestId);
            return (false, "VacationApproval_Error");
        }

        // Notifications overhaul Phase 3d: alert the approver pool that a request awaits them
        // (previously the approving manager was never told). In-app + gated email per approver.
        try
        {
            var requesterName = await _context.Users.IgnoreQueryFilters()
                .Where(u => u.Id == request.UserId)
                .Select(u => u.DisplayName)
                .FirstOrDefaultAsync() ?? string.Empty;

            var approvers = await GetApproverPoolAsync(requestId);
            var title = _localizer["Notif_RequestSubmittedTitle"].Value;
            var message = string.Format(_localizer["Notif_TimeOffRequestSubmittedMessage"].Value, requesterName);

            foreach (var approver in approvers)
            {
                await _notificationService.NotifyAsync(approver.Id, NotificationType.TimeOffRequestSubmitted,
                    ShiftManager.Services.Notifications.NotificationCategory.TimeOff, title, message,
                    personallyActionable: true, relatedEntityId: requestId, relatedEntityType: "TimeOffRequest");
            }
        }
        catch (Exception ex)
        {
            // Notification failure must never block the submission itself.
            _logger.LogWarning(ex, "Failed to notify approver pool for request {RequestId}", requestId);
        }

        if (requiresSecondApproval)
        {
            return (true, "VacationApproval_SubmittedForSecondApproval");
        }

        return (true, "VacationApproval_Submitted");
    }

    /// <summary>
    /// Approves a time-off request. Implements the dual-approval state machine
    /// per molecule's <see cref="MoleculeApprovalSettings.DualApprovalDayThreshold"/>:
    /// <list type="bullet">
    ///   <item>Vacations with length &gt; threshold require parallel dual approval
    ///         (Lead+Director for Alhut/Text; BRDirector+MoleculeAdmin otherwise).</item>
    ///   <item>Vacations with length ≤ threshold and all After requests use single approval.</item>
    /// </list>
    /// State transitions:
    /// <list type="bullet">
    ///   <item>Pending → first tier action → PendingSecondApproval (dual mode)</item>
    ///   <item>PendingSecondApproval → complementary tier action → Approved</item>
    ///   <item>Pending → tier action → Approved (single mode)</item>
    /// </list>
    /// Side-effects + materialiser fire only on the final Approved transition.
    /// </summary>
    public async Task<(bool Success, string Message)> ApproveAsync(int requestId, int approverId, string? notes = null)
    {
        // IgnoreQueryFilters: request may be in a different company than current tenant
        var request = await _context.TimeOffRequests
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.Id == requestId);

        if (request == null)
        {
            return (false, "VacationApproval_RequestNotFound");
        }

        // SECURITY: Prevent self-approval of vacation requests (fires before tier/grant checks)
        if (request.UserId == approverId)
        {
            _logger.LogWarning(
                "User {UserId} attempted to approve their own vacation request {RequestId}",
                approverId, requestId);
            return (false, "VacationApproval_CannotApproveSelf");
        }

        // Status guard — only Pending and PendingSecondApproval can be acted on.
        if (request.Status != RequestStatus.Pending
            && request.Status != RequestStatus.PendingSecondApproval)
        {
            return (false, "VacationApproval_AlreadyProcessed");
        }

        // Verify approver has the required grant (legacy check — preserved for backward compat
        // with rule-based approval; tier check below is the new dual-approval gating).
        bool canApprove = await CanUserApproveAsync(approverId, requestId);
        if (!canApprove)
        {
            _logger.LogWarning(
                "User {UserId} attempted to approve request {RequestId} without authorization",
                approverId, requestId);
            return (false, "VacationApproval_NotAuthorized");
        }

        // Determine whether dual approval is required for this request.
        // After requests are always single-approval. Vacation requires dual only when
        // length strictly exceeds the molecule's DualApprovalDayThreshold.
        // IgnoreQueryFilters: requester/company/molecule may be cross-tenant.
        var requester = await _context.Users.IgnoreQueryFilters()
            .Include(u => u.RoleTemplate)
            .FirstOrDefaultAsync(u => u.Id == request.UserId);
        if (requester == null)
        {
            return (false, "VacationApproval_RequesterNotFound");
        }

        var moleculeId = await _context.Companies.IgnoreQueryFilters()
            .Where(c => c.Id == request.CompanyId)
            .Select(c => (int?)c.MoleculeId)
            .FirstOrDefaultAsync();
        var settings = moleculeId.HasValue
            ? await _context.MoleculeApprovalSettings.IgnoreQueryFilters()
                .FirstOrDefaultAsync(s => s.MoleculeId == moleculeId.Value)
            : null;
        var threshold = settings?.DualApprovalDayThreshold ?? 7;

        var lengthDays = request.EndDate.DayNumber - request.StartDate.DayNumber + 1;
        // DayAt uses the identical approval flow as Vacation (incl. dual-approval threshold) — Issue 4.
        bool requiresDual = (request.Type == TimeOffType.Vacation || request.Type == TimeOffType.DayAt) && lengthDays > threshold;

        // Determine the approver's tier. Tiers are disjoint by RoleTemplate Key.
        // Alhut/Text (JobTypeId 1 or 3) → first=Lead, second=Director (same JobTypeId)
        // Other → first=BRDirector, second=MoleculeAdmin
        var approver = await _context.Users.IgnoreQueryFilters()
            .Include(u => u.RoleTemplate)
            .FirstOrDefaultAsync(u => u.Id == approverId);
        if (approver == null)
        {
            return (false, "VacationApproval_ApproverNotFound");
        }

        bool isAlhutOrText = requester.JobTypeId.HasValue
            && AlhutTextJobTypeIds.Contains(requester.JobTypeId.Value);
        var approverKey = approver.RoleTemplate?.Key;

        bool approverIsFirstTier;
        bool approverIsSecondTier;
        if (isAlhutOrText)
        {
            approverIsFirstTier = approverKey == "Lead"
                && approver.JobTypeId == requester.JobTypeId;
            approverIsSecondTier = approverKey == "Director"
                && approver.JobTypeId == requester.JobTypeId;
        }
        else
        {
            approverIsFirstTier = approverKey == "BRDirector";
            approverIsSecondTier = approverKey == "MoleculeAdmin";
        }

        // For single-approval (≤ threshold or After), tier eligibility is relaxed —
        // the legacy CanUserApproveAsync grant gate above is sufficient. Only enforce
        // tier membership when dual approval applies.
        if (requiresDual && !approverIsFirstTier && !approverIsSecondTier)
        {
            _logger.LogWarning(
                "User {ApproverId} (key={Key}) is not in the dual-approval tier pool for request {RequestId}",
                approverId, approverKey, requestId);
            return (false, "VacationApproval_NotEligibleTier");
        }

        // Wrap state-machine transition in a transaction for concurrency safety.
        using var transaction = await _context.Database.BeginTransactionAsync();
        bool finalApproved = false;
        try
        {
            // Re-fetch inside transaction.
            var freshRequest = await _context.TimeOffRequests
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(r => r.Id == requestId);
            if (freshRequest == null
                || (freshRequest.Status != RequestStatus.Pending
                    && freshRequest.Status != RequestStatus.PendingSecondApproval))
            {
                await transaction.RollbackAsync();
                return (false, "VacationApproval_AlreadyProcessed");
            }

            // Overlap check applies only to the FINAL approval transition (when status will
            // become Approved). For intermediate Pending → PendingSecondApproval transitions,
            // skip the overlap check — it will be re-evaluated on the second approval.
            bool willFinalApprove = !requiresDual
                || freshRequest.Status == RequestStatus.PendingSecondApproval;

            if (willFinalApprove)
            {
                // Exclude same-group siblings from the overlap check: a fan-out copy shares the
                // SAME logical leave/dates, so an already-Approved sibling must never count as an
                // "overlap" against this copy. Without this, a sibling left stuck (e.g. its cascade
                // failed) could never be approved through the normal path — the acting copy that
                // is already Approved would always trip the overlap guard.
                var hasOverlap = await _context.TimeOffRequests
                    .IgnoreQueryFilters()
                    .AnyAsync(r => r.UserId == freshRequest.UserId
                        && r.Id != requestId
                        && r.Status == RequestStatus.Approved
                        && (freshRequest.LeaveGroupId == null || r.LeaveGroupId != freshRequest.LeaveGroupId)
                        && r.StartDate <= freshRequest.EndDate
                        && r.EndDate >= freshRequest.StartDate);

                if (hasOverlap)
                {
                    await transaction.RollbackAsync();
                    _logger.LogWarning(
                        "Cannot approve request {RequestId}: overlaps with an already-approved vacation for user {UserId}",
                        requestId, freshRequest.UserId);
                    return (false, "VacationApproval_OverlappingApproved");
                }
            }

            var now = DateTime.UtcNow;

            if (!requiresDual)
            {
                // Single-approval flow: any tier-eligible (or grant-holding) approver finishes.
                // Status must be Pending here — PendingSecondApproval is unreachable in single mode.
                if (freshRequest.Status != RequestStatus.Pending)
                {
                    await transaction.RollbackAsync();
                    return (false, "VacationApproval_AlreadyProcessed");
                }
                freshRequest.Status = RequestStatus.Approved;
                freshRequest.ApproverId = approverId;
                freshRequest.FirstApprovalActorId = approverId;
                freshRequest.FirstApprovalActedAt = now;
                finalApproved = true;
            }
            else
            {
                // Dual-approval flow.
                if (freshRequest.Status == RequestStatus.Pending)
                {
                    // First action by either tier → record on the matching slot, advance to
                    // PendingSecondApproval awaiting the complementary tier.
                    if (approverIsFirstTier)
                    {
                        freshRequest.FirstApprovalActorId = approverId;
                        freshRequest.FirstApprovalActedAt = now;
                    }
                    else // approverIsSecondTier (guaranteed by the eligibility check above)
                    {
                        freshRequest.SecondApprovalActorId = approverId;
                        freshRequest.SecondApprovalActedAt = now;
                    }
                    freshRequest.Status = RequestStatus.PendingSecondApproval;
                    finalApproved = false;
                }
                else // PendingSecondApproval
                {
                    if (approverIsFirstTier && freshRequest.FirstApprovalActorId == null)
                    {
                        freshRequest.FirstApprovalActorId = approverId;
                        freshRequest.FirstApprovalActedAt = now;
                        freshRequest.Status = RequestStatus.Approved;
                        freshRequest.ApproverId = approverId;
                        finalApproved = true;
                    }
                    else if (approverIsSecondTier && freshRequest.SecondApprovalActorId == null)
                    {
                        freshRequest.SecondApprovalActorId = approverId;
                        freshRequest.SecondApprovalActedAt = now;
                        freshRequest.Status = RequestStatus.Approved;
                        freshRequest.ApproverId = approverId;
                        finalApproved = true;
                    }
                    else
                    {
                        // Same tier already approved — block the duplicate action.
                        await transaction.RollbackAsync();
                        return (false, "VacationApproval_TierAlreadyApproved");
                    }
                }
            }

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Failed to approve vacation request {RequestId}", requestId);
            return (false, "VacationApproval_Error");
        }

        _logger.LogInformation(
            "Request {RequestId} approval action by user {ApproverId} (finalApproved={FinalApproved})",
            requestId, approverId, finalApproved);

        // Side-effects + materialiser only on the final → Approved transition.
        if (finalApproved)
        {
            try
            {
                await ProcessApprovalSideEffectsAsync(requestId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Side effects failed for approved request {RequestId}. Manual remediation may be needed.", requestId);
            }

            try
            {
                if (await _featureFlagService.IsEnabledAsync(FeatureFlagSeed.Flags.HomeUnification))
                {
                    await _materialiser.SyncMaterialisedHomeRowsAsync(requestId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Materialiser failed for approved request {RequestId}", requestId);
            }

            // Cascade the terminal Approved decision to all sibling copies that share the
            // same LeaveGroupId. Only fires on the FINAL transition (finalApproved == true) —
            // an intermediate PendingSecondApproval state is NOT cascaded; each copy must
            // advance its own dual-approval tiers independently. The first copy that reaches
            // terminal Approved pulls all remaining non-terminal siblings to Approved.
            if (request.LeaveGroupId.HasValue)
            {
                try
                {
                    // Re-read the now-committed acting request so we copy the accurate
                    // post-commit approval-actor field values to siblings.
                    var committed = await _context.TimeOffRequests
                        .IgnoreQueryFilters()
                        .FirstOrDefaultAsync(r => r.Id == requestId);
                    if (committed != null)
                    {
                        await CascadeGroupDecisionAsync(committed, RequestStatus.Approved);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Cascade-approve failed for group {LeaveGroupId} (acting request {RequestId}). Siblings may need manual remediation.",
                        request.LeaveGroupId, requestId);
                }
            }
        }

        return (true, "VacationApproval_Approved");
    }

    /// <summary>
    /// Declines a time-off request. Verifies the decliner has the required grant.
    /// </summary>
    public async Task<(bool Success, string Message)> DeclineAsync(int requestId, int declinerId, string? reason = null)
    {
        // IgnoreQueryFilters: request may be in a different company than current tenant
        var request = await _context.TimeOffRequests
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.Id == requestId);

        if (request == null)
        {
            return (false, "VacationApproval_RequestNotFound");
        }

        // Decline is allowed from either Pending OR PendingSecondApproval — either tier
        // (or any grant-holder) can reject the request and abort the flow.
        if (request.Status != RequestStatus.Pending
            && request.Status != RequestStatus.PendingSecondApproval)
        {
            return (false, "VacationApproval_AlreadyProcessed");
        }

        // Verify decliner has the required grant
        bool canApprove = await CanUserApproveAsync(declinerId, requestId);
        if (!canApprove)
        {
            _logger.LogWarning(
                "User {UserId} attempted to decline request {RequestId} without authorization",
                declinerId, requestId);
            return (false, "VacationApproval_NotAuthorized");
        }

        // Wrap in transaction to prevent concurrent approve/decline race
        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            // Re-check status inside transaction
            var freshRequest = await _context.TimeOffRequests
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(r => r.Id == requestId);
            if (freshRequest == null
                || (freshRequest.Status != RequestStatus.Pending
                    && freshRequest.Status != RequestStatus.PendingSecondApproval))
            {
                await transaction.RollbackAsync();
                return (false, "VacationApproval_AlreadyProcessed");
            }

            freshRequest.Status = RequestStatus.Declined;
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Failed to decline vacation request {RequestId}", requestId);
            return (false, "VacationApproval_Error");
        }

        _logger.LogInformation(
            "Request {RequestId} declined by user {DeclinerId}. Reason: {Reason}",
            requestId, declinerId, reason ?? "(none)");

        // Sync materialised HOME rows for the decline (should clear any existing rows)
        if (await _featureFlagService.IsEnabledAsync(FeatureFlagSeed.Flags.HomeUnification))
        {
            await _materialiser.SyncMaterialisedHomeRowsAsync(requestId);
        }

        // Cascade the terminal Declined decision to all sibling copies that share the same
        // LeaveGroupId. Any eligible manager declining one copy terminates the whole group.
        if (request.LeaveGroupId.HasValue)
        {
            try
            {
                await CascadeGroupDecisionAsync(request, RequestStatus.Declined);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Cascade-decline failed for group {LeaveGroupId} (acting request {RequestId}). Siblings may need manual remediation.",
                    request.LeaveGroupId, requestId);
            }
        }

        return (true, "VacationApproval_Declined");
    }

    /// <summary>
    /// Checks whether a user can approve a specific request.
    /// If the rule defines a specific ApproverUserId, only that user can approve.
    /// Otherwise, any user with the ApproverGrantKey for the request's company can approve.
    /// Phase 2: Now JobType-aware — targeted grants (e.g., BRDirector's ApproveVacations for BR/Hakam)
    /// are only valid when the requesting user's JobTypeId matches.
    /// </summary>
    public async Task<bool> CanUserApproveAsync(int userId, int requestId)
    {
        // IgnoreQueryFilters: request may be in a different company than current tenant
        var request = await _context.TimeOffRequests
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.Id == requestId);

        if (request == null)
            return false;

        var requestingUser = await _context.Users.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == request.UserId);

        return await CanUserApproveInternalAsync(userId, request, requestingUser?.JobTypeId);
    }

    /// <summary>
    /// Internal overload accepting pre-loaded entities to avoid redundant DB queries
    /// when called in a loop (e.g., from GetPendingApprovalsForUserAsync).
    /// </summary>
    private async Task<bool> CanUserApproveInternalAsync(int userId, TimeOffRequest request, int? requestorJobTypeId)
    {
        // Get the approval route
        var (specificApproverId, approverGrantKey, _) = await GetApprovalRouteAsync(request.Id);

        // If a specific approver is set, the designated user is always authorized (short-circuit first).
        if (specificApproverId.HasValue && userId == specificApproverId.Value)
            return true;

        // Build the union of company ids that the approver grant is checked against:
        // always start with the request's own company, then add each of the requester's
        // other membership companies WHERE DoesShifts is true. Leave is fanned out (and the
        // approver pool is built) only across DoesShifts companies, so a manager in a
        // DoesShifts=false membership company was never routed this leave and must NOT be
        // able to approve it. request.CompanyId is always seeded first regardless. For
        // single-company users (no extra memberships) this is exactly the previous behavior.
        var memberships = await _membershipService.GetMembershipsAsync(request.UserId);
        var companyIds = new HashSet<int> { request.CompanyId };
        foreach (var m in memberships.Where(m => m.DoesShifts))
            companyIds.Add(m.CompanyId);

        // OR across all companies: approver qualifies if they hold the grant in ANY of them.
        foreach (var cid in companyIds)
        {
            if (await _grantService.HasGrantWithScopeAsync(
                    userId, approverGrantKey, companyId: cid, jobTypeId: requestorJobTypeId))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Gets all pending time-off requests that a user can approve.
    /// Phase 2: Now JobType-aware — BRDirector only sees requests from users whose
    /// JobTypeId matches their targeted ApproveVacations grants (BR, Hakam).
    /// </summary>
    public async Task<List<TimeOffRequest>> GetPendingApprovalsForUserAsync(int userId)
    {
        // Get the companies where the user has the ApproveVacations grant
        var approveCompanyIds = await _grantService.GetAccessibleCompanyIdsForGrantAsync(userId, "ApproveVacations");

        // Also check for ApproveExtendedLeave grant
        var extendedLeaveCompanyIds = await _grantService.GetAccessibleCompanyIdsForGrantAsync(userId, "ApproveExtendedLeave");

        var allCompanyIds = approveCompanyIds.Union(extendedLeaveCompanyIds).Distinct().ToList();

        if (!allCompanyIds.Any())
            return new List<TimeOffRequest>();

        // Get pending requests in those companies
        // IgnoreQueryFilters: allCompanyIds may span multiple companies
        var pendingRequests = await _context.TimeOffRequests
            .IgnoreQueryFilters()
            .Where(r => r.Status == RequestStatus.Pending
                     && allCompanyIds.Contains(r.CompanyId))
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();

        if (!pendingRequests.Any())
            return new List<TimeOffRequest>();

        // Batch-load requesting users' JobTypeIds to avoid N+1 queries
        var requestorUserIds = pendingRequests.Select(r => r.UserId).Distinct().ToList();
        var requestorJobTypes = await _context.Users.IgnoreQueryFilters()
            .Where(u => requestorUserIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.JobTypeId);

        // Filter: verify the user can actually approve each request
        // Uses internal overload with pre-loaded data to avoid redundant DB queries
        var result = new List<TimeOffRequest>();
        foreach (var req in pendingRequests)
        {
            requestorJobTypes.TryGetValue(req.UserId, out int? jobTypeId);
            if (await CanUserApproveInternalAsync(userId, req, jobTypeId))
            {
                result.Add(req);
            }
        }

        return result;
    }

    /// <summary>
    /// Gets all approval rules for a company.
    /// </summary>
    public async Task<List<VacationApprovalRule>> GetRulesForCompanyAsync(int companyId)
    {
        // IgnoreQueryFilters: rules may be in a different company than current tenant
        return await _context.VacationApprovalRules
            .IgnoreQueryFilters()
            .Where(r => r.CompanyId == companyId)
            .OrderByDescending(r => r.Priority)
            .ThenByDescending(r => r.JobTypeId != null ? 1 : 0)
            .ToListAsync();
    }

    /// <summary>
    /// Creates a new approval rule.
    /// </summary>
    public async Task<VacationApprovalRule> CreateRuleAsync(VacationApprovalRule rule)
    {
        _context.VacationApprovalRules.Add(rule);
        await _context.SaveChangesAsync();

        _logger.LogInformation(
            "Created approval rule {RuleId} for company {CompanyId}, JobTypeId={JobTypeId}, Priority={Priority}",
            rule.Id, rule.CompanyId, rule.JobTypeId, rule.Priority);

        return rule;
    }

    /// <summary>
    /// Updates an existing approval rule.
    /// </summary>
    public async Task<bool> UpdateRuleAsync(VacationApprovalRule rule)
    {
        // IgnoreQueryFilters: rule may be in a different company than current tenant
        var existing = await _context.VacationApprovalRules
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.Id == rule.Id);

        if (existing == null)
            return false;

        existing.JobTypeId = rule.JobTypeId;
        existing.ApproverUserId = rule.ApproverUserId;
        existing.ApproverGrantKey = rule.ApproverGrantKey;
        existing.MaxAutoApproveDays = rule.MaxAutoApproveDays;
        existing.RequiresSecondApproval = rule.RequiresSecondApproval;
        existing.ExtendedLeaveDaysThreshold = rule.ExtendedLeaveDaysThreshold;
        existing.SecondApproverGrantKey = rule.SecondApproverGrantKey;
        existing.Priority = rule.Priority;
        existing.IsActive = rule.IsActive;

        await _context.SaveChangesAsync();

        _logger.LogInformation("Updated approval rule {RuleId}", rule.Id);

        return true;
    }

    /// <summary>
    /// Deletes an approval rule.
    /// </summary>
    public async Task<bool> DeleteRuleAsync(int ruleId)
    {
        // IgnoreQueryFilters: rule may be in a different company than current tenant
        var rule = await _context.VacationApprovalRules
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.Id == ruleId);

        if (rule == null)
            return false;

        _context.VacationApprovalRules.Remove(rule);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Deleted approval rule {RuleId}", ruleId);

        return true;
    }

    /// <summary>
    /// Allows a user to cancel their own pending request.
    /// </summary>
    public async Task<(bool Success, string Message)> CancelRequestAsync(int requestId, int userId)
    {
        // IgnoreQueryFilters: request may be in a different company than current tenant
        var request = await _context.TimeOffRequests
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.Id == requestId);

        if (request == null)
            return (false, "VacationApproval_RequestNotFound");

        if (request.UserId != userId)
            return (false, "VacationApproval_NotYourRequest");

        if (request.Status != RequestStatus.Pending)
            return (false, "VacationApproval_AlreadyProcessed");

        request.Status = RequestStatus.Canceled;
        await _context.SaveChangesAsync();

        _logger.LogInformation("Request {RequestId} canceled by user {UserId}", requestId, userId);

        // Sync materialised HOME rows for the cancellation
        if (await _featureFlagService.IsEnabledAsync(FeatureFlagSeed.Flags.HomeUnification))
        {
            await _materialiser.SyncMaterialisedHomeRowsAsync(requestId);

            // For vacation (and DayAt — same flow, Issue 4) cancellations, restore rotation HOME shifts
            if (request.Type == TimeOffType.Vacation || request.Type == TimeOffType.DayAt)
            {
                await _materialiser.RestoreRotationHomeAsync(request.UserId, request.StartDate, request.EndDate);
            }
        }

        // Cascade the cancellation to all sibling fan-out copies sharing this LeaveGroupId.
        // Without this, sibling copies in other companies stay Pending in those companies'
        // approver queues even though the filer (whose My/Requests only shows the canonical
        // copy) believes the whole leave is canceled.
        if (request.LeaveGroupId.HasValue)
        {
            try
            {
                await CascadeGroupCancellationAsync(request);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Cascade-cancel failed for group {LeaveGroupId} (acting request {RequestId}). Siblings may need manual remediation.",
                    request.LeaveGroupId, requestId);
            }
        }

        return (true, "VacationApproval_Canceled");
    }

    /// <summary>
    /// Shorten an approved Vacation request's date range. Only narrowing is allowed
    /// (newStart >= original StartDate AND newEnd <= original EndDate). Triggers the
    /// materialiser to remove HOME rows for days now outside the range, and restores
    /// rotation HOME on those formerly-covered days.
    /// </summary>
    public async Task<(bool Success, string Message)> UpdateRequestDatesAsync(
        int requestId, DateOnly newStart, DateOnly newEnd, int actorUserId)
    {
        // IgnoreQueryFilters: request may be in a different company than current tenant
        var req = await _context.TimeOffRequests.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == requestId);

        if (req == null)
            return (false, _localizer["VacationApproval_RequestNotFound"]);

        // F6 SECURITY: only the request's OWNER or an authorized approver may re-date it.
        // Mirrors CancelRequestAsync's ownership gate; CanUserApproveAsync is JobType-aware and
        // honors the approval route + DoesShifts membership (so molecule/area-scoped managers
        // qualify for any company within their grant's scope). Without this, [Authorize]-only
        // access let any authenticated user shorten anyone's approved leave cross-tenant.
        if (req.UserId != actorUserId && !await CanUserApproveAsync(actorUserId, req.Id))
            return (false, _localizer["VacationApproval_NotYourRequest"]);

        if (req.Status != RequestStatus.Approved)
            return (false, _localizer["VacationApproval_OnlyApprovedShortenable"]);

        if (req.Type == TimeOffType.After)
            return (false, _localizer["VacationApproval_AfterNotShortenable"]);

        if (newStart < req.StartDate || newEnd > req.EndDate)
            return (false, _localizer["VacationApproval_ShortenOnlyNarrows"]);

        if (newEnd < newStart)
            return (false, _localizer["Error_EndDateBeforeStartDate"]);

        var oldStart = req.StartDate;
        var oldEnd = req.EndDate;

        using var tx = await _context.Database.BeginTransactionAsync();
        try
        {
            req.StartDate = newStart;
            req.EndDate = newEnd;
            await _context.SaveChangesAsync();

            // Re-materialise: this will delete HOME rows now outside the new range
            if (await _featureFlagService.IsEnabledAsync(FeatureFlagSeed.Flags.HomeUnification))
            {
                await _materialiser.SyncMaterialisedHomeRowsAsync(req.Id);

                // Restore rotation HOME on the days that USED to be covered but no longer are
                if (newStart > oldStart)
                    await _materialiser.RestoreRotationHomeAsync(req.UserId, oldStart, newStart.AddDays(-1));
                if (newEnd < oldEnd)
                    await _materialiser.RestoreRotationHomeAsync(req.UserId, newEnd.AddDays(1), oldEnd);
            }

            await tx.CommitAsync();

            await _auditLogService.LogAsync("TimeOffRequestDatesUpdated", "TimeOffRequest", req.Id,
                $"Shortened from {oldStart:yyyy-MM-dd}..{oldEnd:yyyy-MM-dd} to {newStart:yyyy-MM-dd}..{newEnd:yyyy-MM-dd} by user {actorUserId}");

            _logger.LogInformation(
                "Request {RequestId} dates shortened by user {ActorUserId}: {OldStart}..{OldEnd} → {NewStart}..{NewEnd}",
                requestId, actorUserId, oldStart, oldEnd, newStart, newEnd);

            return (true, _localizer["VacationApproval_DatesUpdated"]);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            _logger.LogError(ex, "Failed to update dates for vacation request {RequestId}", requestId);
            return (false, _localizer["VacationApproval_Error"]);
        }
    }

    /// <summary>
    /// Detects approval rules that have no active users who can fulfill them.
    /// Returns warnings for admin display.
    /// </summary>
    public async Task<List<OrphanedApprovalRuleInfo>> DetectOrphanedRulesAsync(int companyId)
    {
        var orphaned = new List<OrphanedApprovalRuleInfo>();
        var rules = await GetRulesForCompanyAsync(companyId);

        foreach (var rule in rules.Where(r => r.IsActive))
        {
            // If rule has specific approver, check if that user is active
            if (rule.ApproverUserId.HasValue)
            {
                // SECURITY-AUDITED: SAFE — scoped by specific ApproverUserId from rule; admin diagnostic
                var approver = await _context.Users
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(u => u.Id == rule.ApproverUserId.Value);

                if (approver == null || !approver.IsActive)
                {
                    orphaned.Add(new OrphanedApprovalRuleInfo(
                        rule.Id,
                        rule.ApproverGrantKey,
                        rule.JobTypeId,
                        $"Specific approver (User #{rule.ApproverUserId}) is deactivated or missing"));
                    continue;
                }
            }

            // Check if any active user has the required grant for this company
            // SECURITY-AUDITED: SAFE — scoped by grantKey + companyId; admin diagnostic returns boolean only
            var hasAnyApprover = await _context.Grants
                .IgnoreQueryFilters()
                .Include(g => g.GrantType)
                .Include(g => g.User)
                .AnyAsync(g => g.GrantType.Key == rule.ApproverGrantKey
                    && g.User != null && g.User.IsActive
                    && g.CompanyId == companyId);

            if (!hasAnyApprover && !rule.ApproverUserId.HasValue)
            {
                orphaned.Add(new OrphanedApprovalRuleInfo(
                    rule.Id,
                    rule.ApproverGrantKey,
                    rule.JobTypeId,
                    $"No active user has the '{rule.ApproverGrantKey}' grant for this company"));
            }
        }

        return orphaned;
    }

    /// <summary>
    /// Gets the current approval pipeline status for a request,
    /// including current stage, approver info, and whether it's orphaned.
    /// </summary>
    public async Task<ApprovalPipelineStatus> GetApprovalStatusAsync(int requestId)
    {
        // IgnoreQueryFilters: request may be in a different company than current tenant
        var request = await _context.TimeOffRequests
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.Id == requestId);

        if (request == null)
        {
            return new ApprovalPipelineStatus(requestId, RequestStatus.Pending, "Unknown", null, null, null, false, false, DateTime.UtcNow);
        }

        var (approverId, approverGrantKey, requiresSecondApproval) = await GetApprovalRouteAsync(requestId);

        string stage = request.Status switch
        {
            RequestStatus.Approved => "Approved",
            RequestStatus.Declined => "Declined",
            _ => requiresSecondApproval ? "PendingSecondApproval" : "PendingApproval"
        };

        string? approverName = null;
        bool isOrphaned = false;

        if (approverId.HasValue)
        {
            // SECURITY-AUDITED: SAFE — scoped by specific approverId from approval route; returns display name only
            var approver = await _context.Users
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(u => u.Id == approverId.Value);
            approverName = approver?.DisplayName;
            if (approver == null || !approver.IsActive)
            {
                isOrphaned = true;
                stage = "Orphaned";
            }
        }
        else if (request.Status == RequestStatus.Pending)
        {
            // Check if anyone can actually approve this
            // SECURITY-AUDITED: SAFE — scoped by grantKey + request's companyId; returns boolean only
            var hasAnyApprover = await _context.Grants
                .IgnoreQueryFilters()
                .Include(g => g.GrantType)
                .Include(g => g.User)
                .AnyAsync(g => g.GrantType.Key == approverGrantKey
                    && g.User != null && g.User.IsActive
                    && g.CompanyId == request.CompanyId);

            if (!hasAnyApprover)
            {
                isOrphaned = true;
                stage = "Orphaned";
            }
        }

        return new ApprovalPipelineStatus(
            requestId,
            request.Status,
            stage,
            approverGrantKey,
            approverId,
            approverName,
            requiresSecondApproval,
            isOrphaned,
            request.CreatedAt);
    }

    /// <summary>
    /// Processes all post-approval side effects for an approved time-off request.
    /// </summary>
    public async Task ProcessApprovalSideEffectsAsync(int requestId)
    {
        // SECURITY-AUDITED: IgnoreQueryFilters SAFE — scoped by specific requestId
        var request = await _context.TimeOffRequests
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.Id == requestId);

        if (request == null)
        {
            _logger.LogWarning("ProcessApprovalSideEffectsAsync: Request {RequestId} not found", requestId);
            return;
        }

        // 1. Remove overlapping shift assignments
        // SECURITY-AUDITED: IgnoreQueryFilters SAFE — scoped by request.UserId + date range
        var assignments = await (from a in _context.ShiftAssignments.IgnoreQueryFilters()
                                 join si in _context.ShiftInstances.IgnoreQueryFilters()
                                    on a.ShiftInstanceId equals si.Id
                                 where a.UserId == request.UserId
                                    && si.WorkDate >= request.StartDate
                                    && si.WorkDate <= request.EndDate
                                 select a).ToListAsync();

        if (assignments.Any())
        {
            _context.ShiftAssignments.RemoveRange(assignments);
            _logger.LogInformation(
                "Removed {Count} overlapping shift assignments for user {UserId} during approved time-off {RequestId}",
                assignments.Count, request.UserId, requestId);
        }

        // 2. Cancel trainee shadowing if user is a trainee
        // SECURITY-AUDITED: IgnoreQueryFilters SAFE — scoped by request.UserId
        var user = await _context.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == request.UserId);

        if (user?.Role == UserRole.Trainee)
        {
            var startDate = request.StartDate.ToDateTime(TimeOnly.MinValue);
            var endDate = request.EndDate.ToDateTime(TimeOnly.MaxValue);
            await _traineeService.CancelShadowingForTimeOffAsync(request.UserId, startDate, endDate);
        }

        await _context.SaveChangesAsync();

        // 3. Send approval notification (after save so DB state is consistent)
        await _notificationService.CreateTimeOffNotificationAsync(
            request.UserId, RequestStatus.Approved,
            request.StartDate, request.EndDate, request.Id);
    }

    public async Task<(bool Success, int? RequestId, string? ErrorKey)> CreateApprovedManualTimeOffAsync(
        int targetUserId, TimeOffType type,
        DateOnly startDate, DateOnly endDate, string? label, int actorUserId)
    {
        // 1. No self-approval: manual entry creates an ALREADY-Approved leave that frees the
        //    subject's shifts, so — exactly like ApproveAsync's self-approval block — the actor may
        //    not be the subject. Users request their own leave via /My/Requests (routes to an approver).
        if (actorUserId == targetUserId)
            return (false, null, "Error_TimeOff_CannotSelfApprove");

        // 2. The leave belongs to the TARGET USER's own company — NOT the actor's active tenant.
        //    On a molecule-scoped Shifts board (or a switched-company Team view) the users shown
        //    span multiple companies, so the viewed/tenant company would be the wrong home for the
        //    record. Resolve it from the user; it also anchors the authorization scope below.
        var targetCompanyId = await _context.Users.IgnoreQueryFilters()
            .Where(u => u.Id == targetUserId && u.IsActive)
            .Select(u => (int?)u.CompanyId)
            .FirstOrDefaultAsync();
        if (targetCompanyId == null)
            return (false, null, "Error_TimeOff_UserNotInCompany");
        var companyId = targetCompanyId.Value;

        // 3. Authorization: creating an already-Approved leave that removes shifts is an APPROVAL-level
        //    action, so require real calendar-EDIT authority (assign/admin) SCOPED to the target's
        //    company — NOT the universal WriteOverviewNotes note tier that every employee holds. This
        //    both enforces the manager-tier bar and is the anti-IDOR guard (the actor must manage the
        //    target's company).
        if (!await _grantService.HasCalendarAssignPermissionForCompanyAsync(actorUserId, companyId))
            return (false, null, "Error_TimeOff_NoPermission");

        // 4. Normalize per type (mirrors Pages/My/Requests OnPostTimeOffAsync):
        //    After and DayAt are single-day; DayAt requires a free-text location label.
        if (type == TimeOffType.After || type == TimeOffType.DayAt)
            endDate = startDate;
        if (type == TimeOffType.DayAt && string.IsNullOrWhiteSpace(label))
            return (false, null, "Error_TimeOff_DayAtLabelRequired");
        if (endDate < startDate)
            return (false, null, "Error_EndDateBeforeStartDate");

        // 5. Create the already-Approved record inside a transaction, re-checking the overlap guard
        //    under the transaction (mirrors ApproveAsync's concurrency guard) so two near-simultaneous
        //    entries can't both pass the guard and double-book the same day.
        var now = DateTime.UtcNow;
        TimeOffRequest request;
        await using (var tx = await _context.Database.BeginTransactionAsync())
        {
            // SECURITY-AUDITED: IgnoreQueryFilters SAFE — scoped by targetUserId + date range.
            var hasOverlap = await _context.TimeOffRequests.IgnoreQueryFilters()
                .AnyAsync(r => r.UserId == targetUserId
                            && r.Status == RequestStatus.Approved
                            && r.StartDate <= endDate && r.EndDate >= startDate);
            if (hasOverlap)
            {
                await tx.RollbackAsync();
                return (false, null, "Error_TimeOff_OverlapExists");
            }

            request = new TimeOffRequest
            {
                CompanyId = companyId,
                UserId = targetUserId,
                StartDate = startDate,
                EndDate = endDate,
                Type = type,
                Label = type == TimeOffType.DayAt ? label!.Trim() : null,
                Reason = "Entered on calendar",
                Status = RequestStatus.Approved,
                ApproverId = actorUserId,
                FirstApprovalActorId = actorUserId,
                FirstApprovalActedAt = now,
                CreatedAt = now
            };
            _context.TimeOffRequests.Add(request);
            await _context.SaveChangesAsync();
            await tx.CommitAsync();
        }

        // 6. Same side-effects a real approval runs — best-effort (mirrors ApproveAsync): the
        //    Approved record is the source of truth; the idempotent materialiser reconciles HOME rows.
        try
        {
            await ProcessApprovalSideEffectsAsync(request.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Side effects failed for manual time-off {RequestId}. Manual remediation may be needed.", request.Id);
        }

        try
        {
            if (await _featureFlagService.IsEnabledAsync(FeatureFlagSeed.Flags.HomeUnification))
                await _materialiser.SyncMaterialisedHomeRowsAsync(request.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Materialiser failed for manual time-off {RequestId}", request.Id);
        }

        await _auditLogService.LogAsync("TimeOffEnteredOnCalendar", "TimeOffRequest", request.Id,
            $"Manual {type} for user {targetUserId} on {startDate:yyyy-MM-dd}..{endDate:yyyy-MM-dd} by user {actorUserId}");

        return (true, request.Id, null);
    }

    /// <summary>
    /// Cascades a terminal approval decision (Approved or Declined) to every non-terminal
    /// sibling in the same LeaveGroup.
    ///
    /// Design contract:
    /// <list type="bullet">
    ///   <item>Called ONLY after the acting request has already been committed to a TERMINAL
    ///         state (<see cref="RequestStatus.Approved"/> or <see cref="RequestStatus.Declined"/>).
    ///         Never called for the intermediate <see cref="RequestStatus.PendingSecondApproval"/>
    ///         state — each copy advances its own dual-approval tiers independently; this method
    ///         fires only when the FIRST copy reaches its final outcome and pulls the rest.</item>
    ///   <item>Sets sibling status DIRECTLY without calling
    ///         <see cref="ApproveAsync"/> / <see cref="DeclineAsync"/> recursively — guards
    ///         against infinite loops.</item>
    ///   <item>For <see cref="RequestStatus.Approved"/> cascades: copies the approval-actor
    ///         fields from the acting request so the audit trail on every sibling shows
    ///         the real approver.</item>
    ///   <item>Each sibling is persisted in its own transaction and its side-effects /
    ///         materialiser are fired independently, so each company's shifts and
    ///         notifications are processed correctly.</item>
    ///   <item>Uses <c>IgnoreQueryFilters()</c> because sibling copies live in different
    ///         companies — the current tenant filter would hide them.</item>
    /// </list>
    /// </summary>
    private async Task CascadeGroupDecisionAsync(TimeOffRequest actingRequest, RequestStatus terminalStatus)
    {
        if (actingRequest.LeaveGroupId is null)
            return; // nothing to cascade for standalone requests (belt-and-suspenders guard)

        // Load all non-terminal siblings that still need a decision.
        // SECURITY-AUDITED: IgnoreQueryFilters SAFE — sibling copies of the SAME leave belong
        // to other companies; scoped by the known LeaveGroupId value.
        var siblings = await _context.TimeOffRequests
            .IgnoreQueryFilters()
            .Where(r => r.LeaveGroupId == actingRequest.LeaveGroupId
                     && r.Id != actingRequest.Id
                     && r.Status != RequestStatus.Approved
                     && r.Status != RequestStatus.Declined
                     && r.Status != RequestStatus.Canceled)
            .ToListAsync();

        if (siblings.Count == 0)
            return;

        var now = DateTime.UtcNow;

        foreach (var sibling in siblings)
        {
            using var tx = await _context.Database.BeginTransactionAsync();
            try
            {
                // Re-fetch inside the sibling's own transaction for concurrency safety.
                var fresh = await _context.TimeOffRequests
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(r => r.Id == sibling.Id);

                if (fresh == null
                    || fresh.Status == RequestStatus.Approved
                    || fresh.Status == RequestStatus.Declined
                    || fresh.Status == RequestStatus.Canceled)
                {
                    // Already resolved by a concurrent action — skip without rolling back.
                    await tx.RollbackAsync();
                    continue;
                }

                fresh.Status = terminalStatus;

                if (terminalStatus == RequestStatus.Approved)
                {
                    // Copy audit fields from the acting request so every sibling's audit
                    // trail reflects the real approver rather than being left null.
                    fresh.ApproverId             = actingRequest.ApproverId;
                    fresh.FirstApprovalActorId   = actingRequest.FirstApprovalActorId;
                    fresh.FirstApprovalActedAt   = actingRequest.FirstApprovalActedAt ?? now;
                    fresh.SecondApprovalActorId  = actingRequest.SecondApprovalActorId;
                    fresh.SecondApprovalActedAt  = actingRequest.SecondApprovalActedAt;
                }

                await _context.SaveChangesAsync();
                await tx.CommitAsync();
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                _logger.LogError(ex,
                    "Cascade transaction failed for sibling request {SiblingId} (group {LeaveGroupId}, status {TerminalStatus})",
                    sibling.Id, actingRequest.LeaveGroupId, terminalStatus);
                continue; // attempt remaining siblings
            }

            // Fire side-effects for this sibling outside its own save-transaction,
            // mirroring exactly how the acting request fires them in ApproveAsync.
            if (terminalStatus == RequestStatus.Approved)
            {
                try
                {
                    await ProcessApprovalSideEffectsAsync(sibling.Id);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Side effects failed for cascaded-approved sibling {SiblingId}. Manual remediation may be needed.",
                        sibling.Id);
                }

                try
                {
                    if (await _featureFlagService.IsEnabledAsync(FeatureFlagSeed.Flags.HomeUnification))
                    {
                        await _materialiser.SyncMaterialisedHomeRowsAsync(sibling.Id);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Materialiser failed for cascaded-approved sibling {SiblingId}", sibling.Id);
                }

                // AUDIT the bypass: this sibling was force-approved by a cross-company cascade,
                // NOT a normal local approval. Critical when the sibling's own company rules
                // required dual approval — an auditor must be able to tell a single cross-company
                // force-approve apart from a locally-completed (possibly dual-tier) approval.
                try
                {
                    var actingApproverId = actingRequest.ApproverId
                        ?? actingRequest.FirstApprovalActorId
                        ?? actingRequest.SecondApprovalActorId
                        ?? 0;
                    await _auditLogService.LogUserActionAsync(
                        actingApproverId,
                        "TimeOffCrossCompanyForceApprove",
                        "TimeOffRequest",
                        sibling.Id,
                        $"Cross-company cascade force-approve: request {sibling.Id} (company {sibling.CompanyId}) " +
                        $"approved via the group decision on source request {actingRequest.Id} " +
                        $"(company {actingRequest.CompanyId}) by approver {actingApproverId}. " +
                        $"LeaveGroupId {actingRequest.LeaveGroupId}.",
                        $"sourceRequestId={actingRequest.Id};sourceCompanyId={actingRequest.CompanyId};" +
                        $"siblingRequestId={sibling.Id};siblingCompanyId={sibling.CompanyId};" +
                        $"approverId={actingApproverId};leaveGroupId={actingRequest.LeaveGroupId}");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Audit log failed for cross-company force-approve of sibling {SiblingId}", sibling.Id);
                }
            }
            else // Declined
            {
                try
                {
                    if (await _featureFlagService.IsEnabledAsync(FeatureFlagSeed.Flags.HomeUnification))
                    {
                        await _materialiser.SyncMaterialisedHomeRowsAsync(sibling.Id);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Materialiser failed for cascaded-declined sibling {SiblingId}", sibling.Id);
                }
            }

            _logger.LogInformation(
                "Cascade {TerminalStatus}: sibling request {SiblingId} (group {LeaveGroupId}) resolved by acting request {ActingRequestId}",
                terminalStatus, sibling.Id, actingRequest.LeaveGroupId, actingRequest.Id);
        }
    }

    /// <summary>
    /// Cascades a cancellation to every still-active sibling in the same LeaveGroup.
    ///
    /// Unlike <see cref="CascadeGroupDecisionAsync"/>, cancellation is NOT an approval decision:
    /// it must NOT fire <see cref="ProcessApprovalSideEffectsAsync"/> (no shift-removal /
    /// trainee-cancel / approval-notification). Instead it mirrors <see cref="CancelRequestAsync"/>'s
    /// own cleanup:
    /// <list type="bullet">
    ///   <item>A <see cref="RequestStatus.Pending"/> (or <see cref="RequestStatus.PendingSecondApproval"/>)
    ///         sibling simply transitions to <see cref="RequestStatus.Canceled"/> — nothing was
    ///         materialised yet, so <c>SyncMaterialisedHomeRowsAsync</c> is a no-op clear.</item>
    ///   <item>An already-<see cref="RequestStatus.Approved"/> sibling is also canceled and then
    ///         properly UN-materialised: <c>SyncMaterialisedHomeRowsAsync</c> removes the HOME rows
    ///         it created (status is now Canceled) and, for Vacation/DayAt, <c>RestoreRotationHomeAsync</c>
    ///         puts the rotation HOME shifts back.</item>
    /// </list>
    /// Already-<see cref="RequestStatus.Canceled"/> and already-<see cref="RequestStatus.Declined"/>
    /// siblings are left untouched. Sets status DIRECTLY (no recursive <see cref="CancelRequestAsync"/>
    /// call) — guards against loops. Uses <c>IgnoreQueryFilters()</c> because siblings live in
    /// other companies.
    /// </summary>
    private async Task CascadeGroupCancellationAsync(TimeOffRequest actingRequest)
    {
        if (actingRequest.LeaveGroupId is null)
            return; // belt-and-suspenders guard

        // Load all siblings that have not already reached Canceled/Declined.
        // An Approved sibling IS included — canceling it must un-materialise its HOME rows.
        // SECURITY-AUDITED: IgnoreQueryFilters SAFE — sibling copies of the SAME leave belong
        // to other companies; scoped by the known LeaveGroupId value.
        var siblings = await _context.TimeOffRequests
            .IgnoreQueryFilters()
            .Where(r => r.LeaveGroupId == actingRequest.LeaveGroupId
                     && r.Id != actingRequest.Id
                     && r.Status != RequestStatus.Canceled
                     && r.Status != RequestStatus.Declined)
            .ToListAsync();

        if (siblings.Count == 0)
            return;

        bool homeUnification =
            await _featureFlagService.IsEnabledAsync(FeatureFlagSeed.Flags.HomeUnification);

        foreach (var sibling in siblings)
        {
            using var tx = await _context.Database.BeginTransactionAsync();
            try
            {
                // Re-fetch inside the sibling's own transaction for concurrency safety.
                var fresh = await _context.TimeOffRequests
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(r => r.Id == sibling.Id);

                if (fresh == null
                    || fresh.Status == RequestStatus.Canceled
                    || fresh.Status == RequestStatus.Declined)
                {
                    // Already resolved by a concurrent action — skip without rolling back.
                    await tx.RollbackAsync();
                    continue;
                }

                fresh.Status = RequestStatus.Canceled;
                await _context.SaveChangesAsync();
                await tx.CommitAsync();
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                _logger.LogError(ex,
                    "Cascade-cancel transaction failed for sibling request {SiblingId} (group {LeaveGroupId})",
                    sibling.Id, actingRequest.LeaveGroupId);
                continue; // attempt remaining siblings
            }

            // Mirror CancelRequestAsync's cleanup (NOT approval side-effects):
            // un-materialise any HOME rows this sibling created (status is now Canceled, so the
            // sync clears them) and restore rotation HOME for Vacation/DayAt.
            if (homeUnification)
            {
                try
                {
                    await _materialiser.SyncMaterialisedHomeRowsAsync(sibling.Id);

                    if (sibling.Type == TimeOffType.Vacation || sibling.Type == TimeOffType.DayAt)
                    {
                        await _materialiser.RestoreRotationHomeAsync(
                            sibling.UserId, sibling.StartDate, sibling.EndDate);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Materialiser cleanup failed for cascaded-canceled sibling {SiblingId}", sibling.Id);
                }
            }

            _logger.LogInformation(
                "Cascade Canceled: sibling request {SiblingId} (group {LeaveGroupId}) canceled by acting request {ActingRequestId}",
                sibling.Id, actingRequest.LeaveGroupId, actingRequest.Id);
        }
    }

    /// <summary>
    /// Gets the matching approval rule for a request (helper method).
    /// </summary>
    private async Task<VacationApprovalRule?> GetMatchingRuleAsync(TimeOffRequest request)
    {
        // IgnoreQueryFilters: user and rules may be in a different company than current tenant
        var user = await _context.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == request.UserId);
        int? userJobTypeId = user?.JobTypeId;

        var rules = await _context.VacationApprovalRules
            .IgnoreQueryFilters()
            .Where(r => r.CompanyId == request.CompanyId && r.IsActive)
            .OrderByDescending(r => r.Priority)
            .ThenByDescending(r => r.JobTypeId != null ? 1 : 0)
            .ToListAsync();

        return rules.FirstOrDefault(r => r.JobTypeId != null && r.JobTypeId == userJobTypeId)
            ?? rules.FirstOrDefault(r => r.JobTypeId == null);
    }

    /// <summary>
    /// Compute the eligible approver pool for a TimeOffRequest. Per-jobtype-vertical:
    /// - Alhut/Text requesters: Lead or Director with same JobTypeId in molecule
    /// - Hakam/BR/Other requesters: BRDirector or MoleculeAdmin in molecule
    /// Empty-pool fallback: MoleculeAdmin users in the molecule (regardless of JobType).
    /// Self-exclusion: requester is never in their own pool.
    /// </summary>
    public async Task<List<AppUser>> GetApproverPoolAsync(int requestId)
    {
        // SECURITY-AUDITED: IgnoreQueryFilters SAFE — pool resolution requires cross-tenant
        // visibility (requester's molecule may span multiple companies); scoped by molecule.
        var req = await _context.TimeOffRequests.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == requestId);
        if (req == null) return new List<AppUser>();

        var requester = await _context.Users.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == req.UserId);
        if (requester == null) return new List<AppUser>();

        var moleculeId = await _context.Companies.IgnoreQueryFilters()
            .Where(c => c.Id == requester.CompanyId)
            .Select(c => (int?)c.MoleculeId)
            .FirstOrDefaultAsync();
        if (moleculeId == null) return new List<AppUser>();

        bool isAlhutOrText = requester.JobTypeId.HasValue
            && AlhutTextJobTypeIds.Contains(requester.JobTypeId.Value);

        // Load all active users in the same molecule (with their RoleTemplate)
        var moleculeUsers = await _context.Users.IgnoreQueryFilters()
            .Include(u => u.RoleTemplate)
            .Join(_context.Companies.IgnoreQueryFilters(),
                  u => u.CompanyId, c => c.Id, (u, c) => new { User = u, c.MoleculeId })
            .Where(x => x.MoleculeId == moleculeId.Value
                     && x.User.IsActive
                     && x.User.Id != requester.Id)
            .Select(x => x.User)
            .ToListAsync();

        var strictPool = moleculeUsers.Where(u =>
        {
            var key = u.RoleTemplate?.Key;
            if (isAlhutOrText)
            {
                return (key == "Lead" || key == "Director")
                    && u.JobTypeId == requester.JobTypeId;
            }
            else
            {
                return key == "BRDirector" || key == "MoleculeAdmin";
            }
        }).ToList();

        if (strictPool.Count > 0) return strictPool;

        // Empty-pool fallback: any MoleculeAdmin in molecule
        return moleculeUsers
            .Where(u => u.RoleTemplate?.Key == "MoleculeAdmin")
            .ToList();
    }

    public async Task<List<ApproverOption>> GetGrantBasedApproverOptionsAsync(int requesterUserId)
    {
        // SECURITY-AUDITED: IgnoreQueryFilters SAFE — the pool is explicitly scoped below to the
        // requester's own member companies; multi-company users may have approvers in another tenant.
        var requester = await _context.Users.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == requesterUserId);
        if (requester == null) return new List<ApproverOption>();

        // Widen the approver pool to the union of all shift-active membership companies.
        // For a single-company user the set contains only requester.CompanyId — same as before.
        var memberships = await _membershipService.GetMembershipsAsync(requesterUserId);
        var memberCompanyIds = new HashSet<int> { requester.CompanyId };
        foreach (var m in memberships.Where(m => m.DoesShifts))
            memberCompanyIds.Add(m.CompanyId);

        // Resolve the molecule ids for all member companies (needed for hierarchy scope-matching).
        var memberCompanies = await _context.Companies.IgnoreQueryFilters()
            .Where(c => memberCompanyIds.Contains(c.Id))
            .Select(c => new { c.Id, c.MoleculeId })
            .ToListAsync();
        var memberMoleculeIds = memberCompanies
            .Where(c => c.MoleculeId.HasValue)
            .Select(c => c.MoleculeId!.Value)
            .ToHashSet();
        var memberCompanyIdList = memberCompanyIds.ToList();
        var memberMoleculeIdList = memberMoleculeIds.ToList();

        var approvalGrantKeys = new[] { "ApproveVacations", "ApproveExtendedLeave" };

        var approverUserIds = await _context.Grants.IgnoreQueryFilters()
            .Where(g => _context.GrantTypes
                .Where(gt => approvalGrantKeys.Contains(gt.Key))
                .Select(gt => gt.Id)
                .Contains(g.GrantTypeId))
            .Where(g => g.CanOwn)
            // #4: align the offered pool with the job-type-aware approval gate (CanUserApproveAsync).
            // Exclude a grant pinned to a job type that does not cover the requester's — mirrors
            // HasGrantWithScopeAsync's jobTypeMismatch (both sides non-null and differing => excluded),
            // so an approver who is OFFERED is never rejected at approval time for a job-type reason.
            .Where(g => g.JobTypeId == null || requester.JobTypeId == null || g.JobTypeId == requester.JobTypeId)
            // Match grants whose scope actually covers any of the requester's member companies
            .Where(g => memberCompanyIdList.Contains(g.CompanyId ?? -1)
                     // Molecule-scoped: grant's molecule must be one of the member molecules
                     || (g.MoleculeId != null && memberMoleculeIdList.Contains(g.MoleculeId.Value))
                     // Area-scoped: grant's area must contain any of the member molecules
                     || (g.AreaId != null && memberMoleculeIdList.Any()
                         && _context.Molecules.Any(m => memberMoleculeIdList.Contains(m.Id) && m.AreaId == g.AreaId))
                     // Project-scoped: grant's project must contain any of the member molecules' areas
                     || (g.ProjectId != null && memberMoleculeIdList.Any()
                         && _context.Molecules.Any(m => memberMoleculeIdList.Contains(m.Id)
                                && _context.Areas.Any(a => a.Id == m.AreaId && a.ProjectId == g.ProjectId)))
                     // Self-scoped (all nulls): approver must be in any of the member companies
                     || (!g.CompanyId.HasValue && !g.MoleculeId.HasValue
                         && !g.AreaId.HasValue && !g.ProjectId.HasValue
                         && _context.Users.Any(u => u.Id == g.UserId && memberCompanyIdList.Contains(u.CompanyId))))
            .Select(g => g.UserId)
            .Distinct()
            .ToListAsync();

        return await _context.Users.IgnoreQueryFilters()
            .Where(u => approverUserIds.Contains(u.Id) && u.IsActive
                        && u.AccountType == AccountType.Standard)
            .OrderBy(u => u.DisplayName)
            .Select(u => new ApproverOption(u.Id, u.DisplayName, u.Role.ToString()))
            .ToListAsync();
    }

    public async Task<bool> IsEligibleApproverAsync(int requesterUserId, int approverUserId)
    {
        // Reuse the dropdown pool as the single source of truth: an approver is accepted on
        // submit IFF they were offered in the form's dropdown. Guarantees the two never drift.
        var pool = await GetGrantBasedApproverOptionsAsync(requesterUserId);
        return pool.Any(o => o.Id == approverUserId);
    }
}
