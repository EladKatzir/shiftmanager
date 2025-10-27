namespace ShiftManager.Models.Analytics;

/// <summary>
/// Top swap requester
/// </summary>
public class TopSwapperDto
{
    public int UserId { get; set; }
    public string EmployeeName { get; set; } = string.Empty;
    public int SwapRequestCount { get; set; }
    public int ApprovedCount { get; set; }
    public int DeclinedCount { get; set; }
}
