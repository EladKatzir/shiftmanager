namespace ShiftManager.Models;

/// <summary>
/// Assigns a <see cref="Company"/> to a <see cref="ShiftTab"/> — the tab's people-view roster base +
/// selector prioritization set. A company may now belong to MULTIPLE tabs (across job types and within a
/// job type's tab set). Composite key <c>(ShiftTabId, CompanyId)</c>; both FKs cascade-delete.
/// </summary>
public class ShiftTabCompany
{
    public int ShiftTabId { get; set; }
    public int CompanyId { get; set; }

    // Navigation
    public ShiftTab ShiftTab { get; set; } = null!;
    public Company Company { get; set; } = null!;
}
