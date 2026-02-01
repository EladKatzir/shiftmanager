namespace ShiftManager.Models.Analytics;

/// <summary>
/// Back-to-back shift warning (shifts with insufficient rest)
/// </summary>
public class BackToBackShiftDto
{
    public int UserId { get; set; }
    public string EmployeeName { get; set; } = string.Empty;
    public DateOnly FirstShiftDate { get; set; }
    public string FirstShiftType { get; set; } = string.Empty;
    public TimeOnly FirstShiftEnd { get; set; }
    public DateOnly SecondShiftDate { get; set; }
    public string SecondShiftType { get; set; } = string.Empty;
    public TimeOnly SecondShiftStart { get; set; }
    public decimal RestHours { get; set; }
}
