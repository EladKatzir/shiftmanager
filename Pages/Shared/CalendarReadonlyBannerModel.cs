namespace ShiftManager.Pages.Shared;

public class CalendarReadonlyBannerModel
{
    public bool IsReadOnly { get; set; }
    public IReadOnlyList<string> RequiredGrantNameKeys { get; set; } = new List<string>();

    /// <summary>
    /// When true, the grid is read-only for shift/note edits but the caller may still enter time-off
    /// on people rows (Team page, or any editor-viewer). Drives an accurate "time-off entry only"
    /// banner instead of the stark "Read-Only Mode" — which otherwise hides a working capability.
    /// </summary>
    public bool CanEnterTimeOff { get; set; }
}
