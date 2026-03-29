namespace ShiftManager.Models;

/// <summary>
/// Store opening hours entry — one per day-of-week per store.
/// </summary>
public class StoreHoursEntry
{
    public int Id { get; set; }
    public int StoreId { get; set; }
    public int DayOfWeek { get; set; } // 0=Sunday..6=Saturday (System.DayOfWeek)
    public TimeOnly OpenTime { get; set; }
    public TimeOnly CloseTime { get; set; }
    public bool IsActive { get; set; } = true;

    // Navigation
    public Store? Store { get; set; }
}
