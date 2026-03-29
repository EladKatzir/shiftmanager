using ShiftManager.Models;

namespace ShiftManager.Services;

public interface ICalendarTextEntryService
{
    Task<CalendarTextEntry> AddAsync(int userId, DateOnly date, string text, int createdByUserId);
    Task<CalendarTextEntry?> GetByIdAsync(int id);
    Task<Dictionary<(int UserId, DateOnly Date), List<(int Id, string Text)>>> GetForDateRangeAsync(DateOnly start, DateOnly end);
    Task<Dictionary<(int UserId, DateOnly Date), List<(int Id, string Text)>>> GetForUsersAndDateRangeAsync(IEnumerable<int> userIds, DateOnly start, DateOnly end);
    Task<bool> DeleteAsync(int id);
}
