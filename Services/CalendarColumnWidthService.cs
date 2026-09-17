using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;

namespace ShiftManager.Services;

public class CalendarColumnWidthService : ICalendarColumnWidthService
{
    private readonly AppDbContext _db;
    public CalendarColumnWidthService(AppDbContext db) => _db = db;

    public async Task<Dictionary<string, int>> GetWidthMapAsync(int userId, string contextKey)
    {
        var rows = await _db.UserCalendarColumnWidths
            .Where(w => w.UserId == userId && w.ContextKey == contextKey)
            .Select(w => new { w.ColumnKey, w.Width })
            .ToListAsync();
        return rows.ToDictionary(w => w.ColumnKey, w => w.Width);
    }

    public async Task SaveWidthsAsync(int userId, string contextKey, IReadOnlyDictionary<string, int> widths)
    {
        // Whole-context replace. ExecuteDeleteAsync commits immediately on its own, so without a
        // transaction a failed insert would leave the user's widths WIPED — the same defect that was
        // fixed in CalendarRowOrderService.SaveOrderAsync. Wrap delete+insert so they are atomic;
        // participate in an ambient transaction if the caller opened one.
        var ownsTransaction = _db.Database.CurrentTransaction == null;
        await using var tx = ownsTransaction ? await _db.Database.BeginTransactionAsync() : null;

        await _db.UserCalendarColumnWidths
            .Where(w => w.UserId == userId && w.ContextKey == contextKey)
            .ExecuteDeleteAsync();

        foreach (var (columnKey, width) in widths)
        {
            _db.UserCalendarColumnWidths.Add(new UserCalendarColumnWidth
            {
                UserId = userId,
                ContextKey = contextKey,
                ColumnKey = columnKey,
                Width = width
            });
        }
        await _db.SaveChangesAsync();
        if (ownsTransaction) await tx!.CommitAsync();
    }
}
