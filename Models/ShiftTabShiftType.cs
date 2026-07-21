namespace ShiftManager.Models;

/// <summary>
/// Assigns a <see cref="ShiftType"/> to a <see cref="ShiftTab"/> — the tab's by-shift view membership.
/// Replaces the old <c>ShiftType.TabId</c> single-tab column: a shift type may now appear in multiple tabs'
/// views, and "no rows for a tab" means "no restriction" (all job-type shift types). Composite key
/// <c>(ShiftTabId, ShiftTypeId)</c>; both FKs cascade-delete.
/// </summary>
public class ShiftTabShiftType
{
    public int ShiftTabId { get; set; }
    public int ShiftTypeId { get; set; }

    // Navigation
    public ShiftTab ShiftTab { get; set; } = null!;
    public ShiftType ShiftType { get; set; } = null!;
}
