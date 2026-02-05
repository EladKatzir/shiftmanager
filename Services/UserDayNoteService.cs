using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;

namespace ShiftManager.Services;

public class UserDayNoteService : IUserDayNoteService
{
    private readonly AppDbContext _db;

    public UserDayNoteService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<UserDayNote?> GetNoteAsync(int userId, DateOnly date, int companyId)
    {
        return await _db.UserDayNotes
            .FirstOrDefaultAsync(n => n.UserId == userId && n.Date == date && n.CompanyId == companyId);
    }

    public async Task<Dictionary<(int UserId, DateOnly Date), string>> GetNotesForCompanyAsync(int companyId, DateOnly start, DateOnly end)
    {
        var notes = await _db.UserDayNotes
            .Where(n => n.CompanyId == companyId && n.Date >= start && n.Date <= end)
            .ToListAsync();

        return notes.ToDictionary(n => (n.UserId, n.Date), n => n.Note);
    }

    public async Task<UserDayNote> SetNoteAsync(int userId, DateOnly date, int companyId, string note, int createdByUserId)
    {
        var existing = await GetNoteAsync(userId, date, companyId);

        if (existing != null)
        {
            existing.Note = note;
            existing.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            existing = new UserDayNote
            {
                UserId = userId,
                Date = date,
                CompanyId = companyId,
                Note = note,
                CreatedByUserId = createdByUserId
            };
            _db.UserDayNotes.Add(existing);
        }

        await _db.SaveChangesAsync();
        return existing;
    }

    public async Task<bool> DeleteNoteAsync(int userId, DateOnly date, int companyId)
    {
        var note = await GetNoteAsync(userId, date, companyId);
        if (note == null)
            return false;

        _db.UserDayNotes.Remove(note);
        await _db.SaveChangesAsync();
        return true;
    }
}
