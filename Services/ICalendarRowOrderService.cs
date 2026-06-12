namespace ShiftManager.Services;

public interface ICalendarRowOrderService
{
    /// <summary>All saved positions for a user+context as (GroupId, RowId) → SortOrder.</summary>
    Task<Dictionary<(string GroupId, string RowId), int>> GetOrderMapAsync(int userId, string contextKey);

    /// <summary>Replace the ordering of one namespace. groupId "" = category order (itemIds are group ids);
    /// a real groupId = row order in that group (itemIds are row ids).</summary>
    Task SaveOrderAsync(int userId, string contextKey, string groupId, IReadOnlyList<string> itemIds);
}
