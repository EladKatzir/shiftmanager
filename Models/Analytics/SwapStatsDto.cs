namespace ShiftManager.Models.Analytics;

/// <summary>
/// Swap request statistics
/// </summary>
public class SwapStatsDto
{
    public int TotalRequests { get; set; }
    public int ApprovedCount { get; set; }
    public int DeclinedCount { get; set; }
    public int PendingCount { get; set; }
    public decimal ApprovalRate { get; set; } // Percentage (0-100)
}
