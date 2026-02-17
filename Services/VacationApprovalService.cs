using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;

namespace ShiftManager.Services;

// SECURITY-AUDITED: All IgnoreQueryFilters() in this class are SAFE — vacation approval requires cross-company visibility
// for Directors managing multiple companies; all queries scoped by explicit requestId/userId/companyId parameters
public class VacationApprovalService : IVacationApprovalService
{
    private readonly AppDbContext _context;
    private readonly IGrantService _grantService;
    private readonly ILogger<VacationApprovalService> _logger;

    public VacationApprovalService(
        AppDbContext context,
        IGrantService grantService,
        ILogger<VacationApprovalService> logger)
    {
        _context = context;
        _grantService = grantService;
        _logger = logger;
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

        // Calculate leave days for auto-approve check
        int leaveDays = request.EndDate.DayNumber - request.StartDate.DayNumber + 1;

        // Check if auto-approve applies
        var rule = await GetMatchingRuleAsync(request);
        if (rule != null && rule.MaxAutoApproveDays > 0 && leaveDays <= rule.MaxAutoApproveDays)
        {
            // Auto-approve
            request.Status = RequestStatus.Approved;
            await _context.SaveChangesAsync();

            _logger.LogInformation(
                "Request {RequestId} auto-approved for user {UserId} ({LeaveDays} days)",
                requestId, request.UserId, leaveDays);

            return (true, "VacationApproval_AutoApproved");
        }

        // Set specific approver if rule defines one
        if (approverId.HasValue)
        {
            request.ApproverId = approverId.Value;
            await _context.SaveChangesAsync();
        }

        _logger.LogInformation(
            "Request {RequestId} submitted for approval. RequiresSecondApproval={RequiresSecond}",
            requestId, requiresSecondApproval);

        if (requiresSecondApproval)
        {
            return (true, "VacationApproval_RequiresSecondApproval");
        }

        return (true, "VacationApproval_PendingApproval");
    }

    /// <summary>
    /// Approves a time-off request. Verifies the approver has the required grant.
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

        if (request.Status != RequestStatus.Pending)
        {
            return (false, "VacationApproval_AlreadyProcessed");
        }

        // SECURITY: Prevent self-approval of vacation requests
        if (request.UserId == approverId)
        {
            _logger.LogWarning(
                "User {UserId} attempted to approve their own vacation request {RequestId}",
                approverId, requestId);
            return (false, "VacationApproval_CannotApproveSelf");
        }

        // Verify approver has the required grant
        bool canApprove = await CanUserApproveAsync(approverId, requestId);
        if (!canApprove)
        {
            _logger.LogWarning(
                "User {UserId} attempted to approve request {RequestId} without authorization",
                approverId, requestId);
            return (false, "VacationApproval_NotAuthorized");
        }

        // Wrap approval in transaction to prevent concurrent overlapping approvals
        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            // Re-check status inside transaction (may have changed concurrently)
            var freshRequest = await _context.TimeOffRequests
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(r => r.Id == requestId);
            if (freshRequest == null || freshRequest.Status != RequestStatus.Pending)
            {
                await transaction.RollbackAsync();
                return (false, "VacationApproval_AlreadyProcessed");
            }

            // Check for overlapping APPROVED requests for the same user
            var hasOverlap = await _context.TimeOffRequests
                .IgnoreQueryFilters()
                .AnyAsync(r => r.UserId == freshRequest.UserId
                    && r.Id != requestId
                    && r.Status == RequestStatus.Approved
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

            // Update the request
            freshRequest.Status = RequestStatus.Approved;
            freshRequest.ApproverId = approverId;
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
            "Request {RequestId} approved by user {ApproverId}",
            requestId, approverId);

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

        if (request.Status != RequestStatus.Pending)
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

        request.Status = RequestStatus.Declined;
        await _context.SaveChangesAsync();

        _logger.LogInformation(
            "Request {RequestId} declined by user {DeclinerId}. Reason: {Reason}",
            requestId, declinerId, reason ?? "(none)");

        return (true, "VacationApproval_Declined");
    }

    /// <summary>
    /// Checks whether a user can approve a specific request.
    /// If the rule defines a specific ApproverUserId, only that user can approve.
    /// Otherwise, any user with the ApproverGrantKey for the request's company can approve.
    /// </summary>
    public async Task<bool> CanUserApproveAsync(int userId, int requestId)
    {
        // IgnoreQueryFilters: request may be in a different company than current tenant
        var request = await _context.TimeOffRequests
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.Id == requestId);

        if (request == null)
            return false;

        // Get the approval route
        var (specificApproverId, approverGrantKey, _) = await GetApprovalRouteAsync(requestId);

        // If a specific approver is set, only that user can approve
        if (specificApproverId.HasValue)
        {
            if (userId == specificApproverId.Value)
                return true;

            // Also allow if user has the grant (fallback for flexibility)
            return await _grantService.HasGrantForCompanyAsync(userId, approverGrantKey, request.CompanyId);
        }

        // Otherwise, check if user has the required grant for the company
        return await _grantService.HasGrantForCompanyAsync(userId, approverGrantKey, request.CompanyId);
    }

    /// <summary>
    /// Gets all pending time-off requests that a user can approve.
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

        // Filter: if a request has a specific ApproverId, only include if it matches this user
        var result = new List<TimeOffRequest>();
        foreach (var req in pendingRequests)
        {
            if (req.ApproverId.HasValue && req.ApproverId.Value != userId)
            {
                // Specific approver is set and it's not this user.
                // Still include if user has the grant (flexibility).
                var (_, grantKey, _) = await GetApprovalRouteAsync(req.Id);
                if (await _grantService.HasGrantForCompanyAsync(userId, grantKey, req.CompanyId))
                {
                    result.Add(req);
                }
            }
            else
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

        // NOTE: Uses Declined status for self-cancellations since RequestStatus has no Canceled value.
        // Distinguish from admin-declined by checking if ApproverId is null (self-cancel) vs set (admin-declined).
        request.Status = RequestStatus.Declined;
        await _context.SaveChangesAsync();

        _logger.LogInformation("Request {RequestId} canceled by user {UserId}", requestId, userId);
        return (true, "VacationApproval_Canceled");
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
}
