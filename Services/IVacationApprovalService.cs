using ShiftManager.Models;

namespace ShiftManager.Services;

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
}
