namespace ShiftManager.Pages.Shared;

public class CalendarReadonlyBannerModel
{
    public bool IsReadOnly { get; set; }
    public List<string> RequiredGrantNameKeys { get; set; } = new();
}
