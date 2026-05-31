namespace ShiftManager.Pages.Shared;

public class CalendarReadonlyBannerModel
{
    public bool IsReadOnly { get; set; }
    public IReadOnlyList<string> RequiredGrantNameKeys { get; set; } = new List<string>();
}
