namespace ShiftManager.Models.ViewModels;

/// <summary>
/// ✅ PHASE 20: Unified calendar item representing shifts, chores, or on-duty assignments
/// </summary>
public class CalendarItemViewModel
{
    /// <summary>
    /// Type of calendar item
    /// </summary>
    public CalendarItemType Type { get; set; }

    /// <summary>
    /// Unique identifier (ShiftInstanceId, ChoreId, or OnDutyId)
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Date of the item
    /// </summary>
    public DateOnly Date { get; set; }

    /// <summary>
    /// Title/Name of the item (e.g., "Morning Shift", "Clean Kitchen", "Hakam")
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Display name of the assigned user
    /// </summary>
    public string AssigneeName { get; set; } = string.Empty;

    /// <summary>
    /// Time range for the item (e.g., "08:00 - 16:00"), empty if not applicable
    /// </summary>
    public string TimeRange { get; set; } = string.Empty;

    /// <summary>
    /// CSS color class for the item (e.g., "shift-blue", "chore-green", "onduty-purple")
    /// </summary>
    public string ColorClass { get; set; } = string.Empty;

    /// <summary>
    /// Emoji icon for the item type
    /// </summary>
    public string Icon { get; set; } = string.Empty;

    /// <summary>
    /// Whether this item is assigned to the current user
    /// </summary>
    public bool IsCurrentUser { get; set; }

    /// <summary>
    /// URL to manage/edit this item (links to Table, Chores, or OnDuty page)
    /// </summary>
    public string ManagementUrl { get; set; } = string.Empty;

    /// <summary>
    /// Additional details for tooltip or expanded view
    /// </summary>
    public string Details { get; set; } = string.Empty;

    /// <summary>
    /// Staffing info for shifts (e.g., "2/3" = 2 assigned, 3 required)
    /// </summary>
    public string? StaffingInfo { get; set; }

    /// <summary>
    /// Whether this is a trainee assignment (for shifts)
    /// </summary>
    public bool IsTrainee { get; set; }

    /// <summary>
    /// Original entity ID for type-specific operations
    /// </summary>
    public int EntityId { get; set; }
}

/// <summary>
/// ✅ PHASE 20: Calendar item types
/// </summary>
public enum CalendarItemType
{
    Shift = 0,
    Chore = 1,
    OnDuty = 2
}
