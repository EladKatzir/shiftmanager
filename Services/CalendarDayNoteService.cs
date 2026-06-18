using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;

namespace ShiftManager.Services;

// SECURITY-AUDITED: Mirrors CalendarTextEntryService. Methods take an explicit companyId and use
// IgnoreQueryFilters() so they never invoke the ambient tenant query filter (which dereferences a
// possibly-null ITenantResolver). The companyId passed in is the caller's resolved tenant; the API
// endpoint (QuickAddDayNote) enforces the note-write grant before calling these methods.
public class CalendarDayNoteService : ICalendarDayNoteService
{
    private readonly AppDbContext _db;

    public CalendarDayNoteService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<CalendarDayNote> SetDayNoteAsync(DateOnly date, int companyId, string text, int createdByUserId)
    {
        // Upsert: one note per (date, companyId). Explicit companyId set so CompanyIdInterceptor
        // (which only fires when CompanyId == 0) does not overwrite it.
        var existing = await _db.CalendarDayNotes
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(n => n.Date == date && n.CompanyId == companyId);

        if (existing != null)
        {
            existing.Text = text;
            existing.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            existing = new CalendarDayNote
            {
                Date = date,
                Text = text,
                CompanyId = companyId,
                CreatedByUserId = createdByUserId
            };
            _db.CalendarDayNotes.Add(existing);
        }

        await _db.SaveChangesAsync();
        return existing;
    }

    public async Task<bool> DeleteDayNoteAsync(DateOnly date, int companyId)
    {
        var note = await _db.CalendarDayNotes
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(n => n.Date == date && n.CompanyId == companyId);

        if (note == null)
            return false;

        _db.CalendarDayNotes.Remove(note);
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<Dictionary<DateOnly, string>> GetDayNotesForCompanyAsync(int companyId, DateOnly start, DateOnly end)
    {
        var notes = await _db.CalendarDayNotes
            .IgnoreQueryFilters()
            .Where(n => n.CompanyId == companyId && n.Date >= start && n.Date <= end)
            .Select(n => new { n.Date, n.Text })
            .ToListAsync();

        // GroupBy is defensive — the DB unique index on (CompanyId, Date) already guarantees one per day.
        return notes
            .GroupBy(n => n.Date)
            .ToDictionary(g => g.Key, g => g.First().Text);
    }
}
