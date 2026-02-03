using ShiftManager.Models;

namespace ShiftManager.Services;

public interface IAnnouncementService
{
    Task<List<Announcement>> GetActiveAnnouncementsAsync(int userId);
    Task<List<Announcement>> GetAllAnnouncementsAsync(bool includeExpired = false);
    Task<Announcement?> GetByIdAsync(int id);
    Task<Announcement> CreateAsync(Announcement announcement, int createdBy);
    Task UpdateAsync(Announcement announcement);
    Task DeleteAsync(int id);
    Task<int> GetUnreadCountAsync(int userId);
}
