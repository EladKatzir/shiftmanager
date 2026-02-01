namespace ShiftManager.Models.Analytics;

/// <summary>
/// Employee hours worked statistics
/// </summary>
public class EmployeeHoursDto
{
    public int UserId { get; set; }
    public string EmployeeName { get; set; } = string.Empty;
    public decimal TotalHours { get; set; }
    public decimal AverageHoursPerWeek { get; set; }
    public int ShiftCount { get; set; }
}
