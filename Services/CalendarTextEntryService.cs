using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;

namespace ShiftManager.Services;

// SECURITY-AUDITED: This service uses IgnoreQueryFilters() in several methods because
// CalendarTextEntry.CompanyId is set to the TARGET user's company (not the caller's tenant).
// Cross-company text entries are valid within a molecule scope (e.g., a director creating
// entries for users in another company within the same molecule).
// Authorization is enforced at the API endpoint level (QuickAddTextEntry, DeleteTextEntry)
// via grant checks — NOT in this service. Callers of read methods must validate that
// the provided userIds are within the caller's authorized scope.
public class CalendarTextEntryService : ICalendarTextEntryService
{
    private readonly AppDbContext _db;

    public CalendarTextEntryService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<CalendarTextEntry> AddAsync(int userId, DateOnly date, string text, int createdByUserId)
    {
        // Use the target user's CompanyId so cross-company entries (manager creating
        // entry for user in another company within the same molecule) are visible
        // in the correct tenant context. CompanyIdInterceptor only fires when CompanyId == 0,
        // so explicit assignment prevents it from overwriting with the caller's company.
        // SECURITY-AUDITED: SAFE — IgnoreQueryFilters used only to resolve target user's CompanyId
        // by exact userId match. Authorization enforced at API endpoint level.
        var targetUser = await _db.Users
            .IgnoreQueryFilters()
            .Where(u => u.Id == userId)
            .Select(u => new { u.CompanyId })
            .FirstOrDefaultAsync();

        if (targetUser == null)
            throw new ArgumentException($"Target user {userId} not found", nameof(userId));

        var entry = new CalendarTextEntry
        {
            UserId = userId,
            Date = date,
            Text = text,
            CreatedByUserId = createdByUserId,
            CompanyId = targetUser.CompanyId
        };
        _db.CalendarTextEntries.Add(entry);
        await _db.SaveChangesAsync();
        return entry;
    }

    /// <summary>
    /// Loads a single text entry by ID. Uses IgnoreQueryFilters because the entry's
    /// CompanyId may differ from the caller's tenant (cross-company within molecule).
    /// </summary>
    public async Task<CalendarTextEntry?> GetByIdAsync(int id)
    {
        // SECURITY-AUDITED: SAFE — entity lookup by unique ID.
        // Authorization enforced at API endpoint level (grant checks).
        return await _db.CalendarTextEntries
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == id);
    }

    public async Task<Dictionary<(int UserId, DateOnly Date), List<(int Id, string Text)>>> GetForDateRangeAsync(DateOnly start, DateOnly end)
    {
        var entries = await _db.CalendarTextEntries
            .Where(e => e.Date >= start && e.Date <= end)
            .Select(e => new { e.Id, e.UserId, e.Date, e.Text })
            .ToListAsync();

        return entries
            .GroupBy(e => (e.UserId, e.Date))
            .ToDictionary(
                g => g.Key,
                g => g.Select(e => (e.Id, e.Text)).ToList());
    }

    public async Task<Dictionary<(int UserId, DateOnly Date), List<(int Id, string Text)>>> GetForUsersAndDateRangeAsync(
        IEnumerable<int> userIds, DateOnly start, DateOnly end)
    {
        // SECURITY-AUDITED: SAFE — IgnoreQueryFilters is required because text entries have
        // CompanyId set to the target user's company, not the caller's company. Cross-company
        // entries within a molecule are valid. The provided userIds MUST be pre-validated by
        // the caller (page handler) against the caller's authorized molecule/area scope.
        // Callers: Shifts.cshtml.cs, Chores.cshtml.cs, OnCall.cshtml.cs — all validate via
        // grant-scoped molecule/area user lists before calling this method.
        var userIdList = userIds.ToList();
        var entries = await _db.CalendarTextEntries
            .IgnoreQueryFilters()
            .Where(e => userIdList.Contains(e.UserId) && e.Date >= start && e.Date <= end)
            .Select(e => new { e.Id, e.UserId, e.Date, e.Text })
            .ToListAsync();

        return entries
            .GroupBy(e => (e.UserId, e.Date))
            .ToDictionary(
                g => g.Key,
                g => g.Select(e => (e.Id, e.Text)).ToList());
    }

    public async Task<bool> DeleteAsync(int id)
    {
        // SECURITY-AUDITED: SAFE — IgnoreQueryFilters needed because the entry's CompanyId
        // may differ from the caller's tenant (cross-company entries within the same molecule).
        // Authorization enforced at API endpoint level (grant checks in DeleteTextEntry).
        var entry = await _db.CalendarTextEntries
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(e => e.Id == id);
        if (entry == null)
            return false;

        _db.CalendarTextEntries.Remove(entry);
        await _db.SaveChangesAsync();
        return true;
    }
}
