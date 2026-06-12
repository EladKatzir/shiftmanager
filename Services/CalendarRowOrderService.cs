using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;

namespace ShiftManager.Services;

public class CalendarRowOrderService : ICalendarRowOrderService
{
    private readonly AppDbContext _db;
    public CalendarRowOrderService(AppDbContext db) => _db = db;

    public async Task<Dictionary<(string, string), int>> GetOrderMapAsync(int userId, string contextKey)
    {
        var rows = await _db.UserCalendarRowOrders
            .Where(o => o.UserId == userId && o.ContextKey == contextKey)
            .Select(o => new { o.GroupId, o.RowId, o.SortOrder })
            .ToListAsync();
        return rows.ToDictionary(o => (o.GroupId, o.RowId), o => o.SortOrder);
    }

    public async Task SaveOrderAsync(int userId, string contextKey, string groupId, IReadOnlyList<string> itemIds)
    {
        // Whole-namespace replace = simplest correct semantics.
        await _db.UserCalendarRowOrders
            .Where(o => o.UserId == userId && o.ContextKey == contextKey && o.GroupId == groupId)
            .ExecuteDeleteAsync();

        for (var i = 0; i < itemIds.Count; i++)
        {
            _db.UserCalendarRowOrders.Add(new UserCalendarRowOrder
            {
                UserId = userId,
                ContextKey = contextKey,
                GroupId = groupId,
                RowId = itemIds[i],
                SortOrder = i
            });
        }
        await _db.SaveChangesAsync();
    }
}
