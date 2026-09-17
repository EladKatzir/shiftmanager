namespace ShiftManager.Models;

/// <summary>
/// A single user's personal column widths for a calendar grid (Excel-style drag-to-resize).
/// NOT IBelongsToCompany — it is a per-user UI preference, read only for its owner,
/// so there is no cross-tenant read path. Mirrors <see cref="UserCalendarRowOrder"/>.
/// </summary>
public class UserCalendarColumnWidth
{
    public int Id { get; set; }

    /// <summary>Owner of this personal sizing.</summary>
    public int UserId { get; set; }

    /// <summary>Identifies the view, e.g. "shifts:9:2:user", "chores:9", "oncall:4", "overview:5".
    /// Same context keys the row-ordering feature uses, so sizing follows the same scoping.</summary>
    public string ContextKey { get; set; } = string.Empty;

    /// <summary>Which column: "label" (the sticky row-label column), "total", or "day:yyyy-MM-dd".</summary>
    public string ColumnKey { get; set; } = string.Empty;

    /// <summary>Column width in CSS pixels.</summary>
    public int Width { get; set; }
}
