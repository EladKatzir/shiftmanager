namespace ShiftManager.Models;

/// <summary>
/// Assigns a <see cref="Company"/> to a <see cref="ShiftTab"/> — the tab's people-view roster base.
/// A company belongs to AT MOST ONE tab (unique <see cref="CompanyId"/>), so tabs are disjoint and the
/// implicit "Main" tab = the molecule's companies with no assignment. Both FKs cascade-delete: removing a
/// tab (or a company) drops the link automatically.
/// </summary>
public class ShiftTabCompany
{
    public int Id { get; set; }
    public int ShiftTabId { get; set; }
    public int CompanyId { get; set; }

    // Navigation
    public ShiftTab ShiftTab { get; set; } = null!;
    public Company Company { get; set; } = null!;
}
