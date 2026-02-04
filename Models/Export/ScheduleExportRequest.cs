namespace ShiftManager.Models.Export;

public enum ExportFormat
{
    Pdf = 0,
    Excel = 1,
    Csv = 2
}

public enum ExportPeriod
{
    Week = 0,
    Month = 1,
    Custom = 2
}

public class ScheduleExportRequest
{
    public ExportFormat Format { get; set; } = ExportFormat.Pdf;
    public ExportPeriod Period { get; set; } = ExportPeriod.Week;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }

    /// <summary>
    /// Optional: Filter by specific department
    /// </summary>
    public int? DepartmentId { get; set; }

    /// <summary>
    /// Optional: Filter by specific job type
    /// </summary>
    public int? JobTypeId { get; set; }

    /// <summary>
    /// Include employee names or just counts
    /// </summary>
    public bool IncludeEmployeeNames { get; set; } = true;

    /// <summary>
    /// Include chores in export
    /// </summary>
    public bool IncludeChores { get; set; } = true;

    /// <summary>
    /// Include on-duty assignments
    /// </summary>
    public bool IncludeOnDuty { get; set; } = true;
}
