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
        // Whole-namespace replace. ExecuteDeleteAsync commits immediately on its own, so without a
        // transaction a failed insert would leave the user's order WIPED (#6). Wrap delete+insert in a
        // transaction so they are atomic; participate in an ambient transaction if the caller opened one.
        var ownsTransaction = _db.Database.CurrentTransaction == null;
        await using var tx = ownsTransaction ? await _db.Database.BeginTransactionAsync() : null;

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
        if (ownsTransaction) await tx!.CommitAsync();
    }
}
