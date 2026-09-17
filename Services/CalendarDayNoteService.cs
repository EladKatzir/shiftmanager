using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;

namespace ShiftManager.Services;

// SECURITY-AUDITED: Methods take an explicit moleculeId and use IgnoreQueryFilters(). That is required,
// not incidental: a day note is keyed to a molecule and must be visible to viewers from every desk in
// it, while the entity's inherited query filter would restrict it to the writer's desk. Callers
// (Shifts page, QuickAddDayNote endpoint) authorise the molecule via ShiftCalendarAccess first.
public class CalendarDayNoteService : ICalendarDayNoteService
{
    private readonly AppDbContext _db;

    public CalendarDayNoteService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<CalendarDayNote> SetDayNoteAsync(DateOnly date, int moleculeId, int authorCompanyId, string text, int userId)
    {
        var existing = await _db.CalendarDayNotes
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(n => n.MoleculeId == moleculeId && n.Date == date);

        if (existing != null)
        {
            // An edit keeps the original author and origin desk; only the text and the editor change.
            existing.Text = text;
            existing.UpdatedAt = DateTime.UtcNow;
            existing.UpdatedByUserId = userId;
        }
        else
        {
            existing = new CalendarDayNote
            {
                Date = date,
                Text = text,
                MoleculeId = moleculeId,
                // Explicit so CompanyIdInterceptor (which only fires when CompanyId == 0) leaves it alone.
                CompanyId = authorCompanyId,
                CreatedByUserId = userId
            };
            _db.CalendarDayNotes.Add(existing);
        }

        await _db.SaveChangesAsync();
        return existing;
    }

    public async Task<bool> DeleteDayNoteAsync(DateOnly date, int moleculeId)
    {
        var note = await _db.CalendarDayNotes
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(n => n.MoleculeId == moleculeId && n.Date == date);

        if (note == null)
            return false;

        _db.CalendarDayNotes.Remove(note);
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<Dictionary<DateOnly, DayNoteView>> GetDayNotesForMoleculeAsync(int moleculeId, DateOnly start, DateOnly end)
    {
        // The filtered unique index on (MoleculeId, Date) guarantees at most one row per day here, and
        // legacy un-keyed rows (MoleculeId NULL) can never match a concrete moleculeId.
        var notes = await _db.CalendarDayNotes
            .IgnoreQueryFilters()
            .Where(n => n.MoleculeId == moleculeId && n.Date >= start && n.Date <= end)
            .Select(n => new
            {
                n.Date,
                n.Text,
                n.CreatedByUserId,
                n.UpdatedByUserId,
                AuthorName = n.CreatedByUser.DisplayName,
                EditorName = n.UpdatedByUser != null ? n.UpdatedByUser.DisplayName : null
            })
            .ToListAsync();

        return notes.ToDictionary(
            n => n.Date,
            n => new DayNoteView(
                n.Text,
                n.AuthorName,
                // The author editing their own note is not a second contributor.
                n.UpdatedByUserId.HasValue && n.UpdatedByUserId != n.CreatedByUserId ? n.EditorName : null));
    }
}
