namespace ShiftManager.ViewComponents;

public class ExcelCalendarRowViewModel
{
    public ExcelCalendarRow Row { get; set; } = new();
    public List<DateOnly> Days { get; set; } = new();
    public DateOnly Today { get; set; }
    public bool IsReadOnly { get; set; }
    public bool IsCollapsed { get; set; }
    public bool HasWeeklyHours { get; set; }
    public int RowIndex { get; set; } = -1;   // -1 sentinel: forces the partial to omit aria-rowindex if not set by caller

    /// <summary>Active Draft Mode session id (Epic 4); non-null → render shift-chip × as a draft-clear.</summary>
    public int? DraftSessionId { get; set; }
}
