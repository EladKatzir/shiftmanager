namespace ShiftManager.Models.Export;

public class ScheduleExportData
{
    public string CompanyName { get; set; } = string.Empty;
    public DateTime GeneratedAt { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public string? DepartmentName { get; set; }
    public string? JobTypeName { get; set; }

    public List<ExportDayData> Days { get; set; } = new();
}

public class ExportDayData
{
    public DateTime Date { get; set; }
    public string DayName { get; set; } = string.Empty;
    public List<ExportShiftData> Shifts { get; set; } = new();
    public List<ExportChoreData> Chores { get; set; } = new();
    public List<ExportOnDutyData> OnDuties { get; set; } = new();
}

public class ExportShiftData
{
    public string ShiftName { get; set; } = string.Empty;
    public string TimeRange { get; set; } = string.Empty;
    public int RequiredStaff { get; set; }
    public int AssignedStaff { get; set; }
    public List<string> AssignedEmployees { get; set; } = new();
}

public class ExportChoreData
{
    public string ChoreName { get; set; } = string.Empty;
    public string? AssignedTo { get; set; }
    public bool IsCompleted { get; set; }
}

public class ExportOnDutyData
{
    public string DutyName { get; set; } = string.Empty;
    public string? AssignedTo { get; set; }
}
