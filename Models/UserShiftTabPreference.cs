namespace ShiftManager.Models;

/// <summary>
/// A single user's remembered tab selection per molecule on the Shifts calendar. NOT IBelongsToCompany —
/// a per-user UI preference read only for its owner (like <see cref="UserCalendarRowOrder"/>), so there is
/// no cross-tenant read path. <see cref="TabId"/> null = the user explicitly chose "Main". The FK
/// TabId → ShiftTab is SET NULL, so deleting a tab reverts anyone's remembered preference to Main. Unique
/// per (UserId, MoleculeId).
/// </summary>
public class UserShiftTabPreference
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int MoleculeId { get; set; }
    public int? TabId { get; set; }        // null = Main
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public ShiftTab? Tab { get; set; }
}
