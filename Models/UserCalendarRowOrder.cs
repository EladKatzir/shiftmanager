namespace ShiftManager.Models;

/// <summary>
/// A single user's personal ordering of a calendar's categories or rows.
/// NOT IBelongsToCompany — it is a per-user UI preference, read only for its owner,
/// so there is no cross-tenant read path. Two axes share this table, distinguished by GroupId:
///   - GroupId == ""            → category order; RowId holds a group id (e.g. "category-5","dl-3").
///   - GroupId == "category-5"  → row order inside that category; RowId holds a row id ("user-14").
/// </summary>
public class UserCalendarRowOrder
{
    public int Id { get; set; }

    /// <summary>Owner of this personal ordering.</summary>
    public int UserId { get; set; }

    /// <summary>Identifies the view, e.g. "shifts:9:2:user", "chores:9", "oncall:4", "overview:5".</summary>
    public string ContextKey { get; set; } = string.Empty;

    /// <summary>"" = the category-ordering namespace; otherwise the category/list this row lives in.</summary>
    public string GroupId { get; set; } = string.Empty;

    /// <summary>A row id ("user-14"/"shift-7") for row order, or a group id ("category-5") for category order.</summary>
    public string RowId { get; set; } = string.Empty;

    /// <summary>0-based position within (UserId, ContextKey, GroupId).</summary>
    public int SortOrder { get; set; }
}
