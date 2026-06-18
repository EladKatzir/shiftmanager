using ShiftManager.Models;

namespace ShiftManager.Services;

/// <summary>
/// Manages day-scoped free-text notes (<see cref="CalendarDayNote"/>) — one editable note per
/// (Date, CompanyId). All methods take an explicit companyId and bypass the ambient tenant query
/// filter (same security model as <see cref="ICalendarTextEntryService"/>): authorization is
/// enforced at the API endpoint, and the companyId is the caller's resolved tenant.
/// </summary>
public interface ICalendarDayNoteService
{
    /// <summary>Upsert the day note for (date, companyId). Creates if absent, updates text if present.</summary>
    Task<CalendarDayNote> SetDayNoteAsync(DateOnly date, int companyId, string text, int createdByUserId);

    /// <summary>Delete the day note for (date, companyId). Returns true if a note existed and was removed.</summary>
    Task<bool> DeleteDayNoteAsync(DateOnly date, int companyId);

    /// <summary>Bulk-load day notes for a company within an inclusive date range, keyed by date.</summary>
    Task<Dictionary<DateOnly, string>> GetDayNotesForCompanyAsync(int companyId, DateOnly start, DateOnly end);
}
