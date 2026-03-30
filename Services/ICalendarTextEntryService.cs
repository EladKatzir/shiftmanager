using ShiftManager.Models;

namespace ShiftManager.Services;

public interface ICalendarTextEntryService
{
    // --- Quick Entry (existing) ---
    Task<CalendarTextEntry> AddAsync(int userId, DateOnly date, string text, int createdByUserId);
    Task<CalendarTextEntry?> GetByIdAsync(int id);
    Task<Dictionary<(int UserId, DateOnly Date), List<(int Id, string Text)>>> GetForDateRangeAsync(DateOnly start, DateOnly end);
    Task<Dictionary<(int UserId, DateOnly Date), List<(int Id, string Text)>>> GetForUsersAndDateRangeAsync(IEnumerable<int> userIds, DateOnly start, DateOnly end);
    Task<bool> DeleteAsync(int id);

    // --- Overview Notes (unified) ---
    Task<CalendarTextEntry> SetOverviewNoteAsync(int userId, DateOnly date, int companyId, string text, int createdByUserId);
    Task<bool> DeleteOverviewNoteAsync(int userId, DateOnly date, int companyId);
    Task<Dictionary<(int UserId, DateOnly Date), string>> GetOverviewNotesForCompanyAsync(int companyId, DateOnly start, DateOnly end);

    // --- Typed reads (returns EntryType + CompanyId so callers can distinguish and filter) ---
    Task<Dictionary<(int UserId, DateOnly Date), List<(int Id, string Text, CalendarTextEntryType EntryType, int CompanyId)>>> GetForUsersAndDateRangeWithTypeAsync(IEnumerable<int> userIds, DateOnly start, DateOnly end);
}
