namespace ShiftManager.Models.Analytics;

/// <summary>
/// Understaffing or overstaffing issue
/// </summary>
public class StaffingIssueDto
{
    public DateOnly WorkDate { get; set; }
    public string ShiftType { get; set; } = string.Empty;
    public int AssignedCount { get; set; }
    public int RequiredCount { get; set; }
    public int Difference { get; set; } // Negative = understaffed, Positive = overstaffed
}
