using ShiftManager.Models;

namespace ShiftManager.Services;

public interface IUserDayNoteService
{
    Task<UserDayNote?> GetNoteAsync(int userId, DateOnly date, int companyId);
    Task<Dictionary<(int UserId, DateOnly Date), string>> GetNotesForCompanyAsync(int companyId, DateOnly start, DateOnly end);
    Task<UserDayNote> SetNoteAsync(int userId, DateOnly date, int companyId, string note, int createdByUserId);
    Task<bool> DeleteNoteAsync(int userId, DateOnly date, int companyId);
}
