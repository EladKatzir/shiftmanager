namespace ShiftManager.Models.Analytics;

/// <summary>
/// Employee upcoming shift count
/// </summary>
public class EmployeeShiftCountDto
{
    public int UserId { get; set; }
    public string EmployeeName { get; set; } = string.Empty;
    public int UpcomingShiftCount { get; set; }
}
