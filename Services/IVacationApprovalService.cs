using ShiftManager.Models;
using ShiftManager.Models.Support;

namespace ShiftManager.Services;

public record OrphanedApprovalRuleInfo(int RuleId, string ApproverGrantKey, int? JobTypeId, string Reason);

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
}
