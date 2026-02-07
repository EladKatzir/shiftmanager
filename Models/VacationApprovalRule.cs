namespace ShiftManager.Models;

public class VacationApprovalRule : IBelongsToCompany
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public int? JobTypeId { get; set; }  // null = default rule for company
    public int? ApproverUserId { get; set; }  // Specific approver, null = any with grant
    public string ApproverGrantKey { get; set; } = "ApproveVacations";  // Grant required to approve
    public int MaxAutoApproveDays { get; set; } = 0;  // 0 = no auto-approve
    public bool RequiresSecondApproval { get; set; } = false;  // For extended leave
    public int ExtendedLeaveDaysThreshold { get; set; } = 5;  // Days that trigger second approval
    public string? SecondApproverGrantKey { get; set; }  // Grant for second-level approver
    public int Priority { get; set; } = 0;  // Higher = checked first (for multiple rules)
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int CreatedBy { get; set; }

    // Navigation
    public Company? Company { get; set; }
    public JobType? JobType { get; set; }
    public AppUser? ApproverUser { get; set; }
}
