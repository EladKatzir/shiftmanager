using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;

namespace ShiftManager.Services;

public class AnnouncementService : IAnnouncementService
{
    private readonly AppDbContext _db;
    private readonly ITenantResolver _tenantResolver;

    public AnnouncementService(AppDbContext db, ITenantResolver tenantResolver)
    {
        _db = db;
        _tenantResolver = tenantResolver;
    }

    public async Task<List<Announcement>> GetActiveAnnouncementsAsync(int userId)
    {
        var user = await _db.Users
            .Include(u => u.Department)
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user == null) return new List<Announcement>();

        var now = DateTime.UtcNow;
        var query = _db.Announcements
            .Include(a => a.Creator)
            .Where(a => a.IsActive)
            .Where(a => a.ExpiresAt == null || a.ExpiresAt > now);

        // Filter by scope
        query = query.Where(a =>
            a.Scope == AnnouncementScope.All ||
            (a.Scope == AnnouncementScope.Department && a.TargetDepartmentId == user.DepartmentId) ||
            (a.Scope == AnnouncementScope.Role && a.TargetRole == user.Role.ToString()));

        return await query
            .OrderByDescending(a => a.IsPinned)
            .ThenByDescending(a => a.CreatedAt)
            .Take(20)
            .ToListAsync();
    }

    public async Task<List<Announcement>> GetAllAnnouncementsAsync(bool includeExpired = false)
    {
        var query = _db.Announcements
            .Include(a => a.Creator)
            .Include(a => a.TargetDepartment)
            .AsQueryable();

        if (!includeExpired)
        {
            var now = DateTime.UtcNow;
            query = query.Where(a => a.ExpiresAt == null || a.ExpiresAt > now);
        }

        return await query
            .OrderByDescending(a => a.IsPinned)
            .ThenByDescending(a => a.CreatedAt)
            .ToListAsync();
    }

    public async Task<Announcement?> GetByIdAsync(int id)
    {
        return await _db.Announcements
            .Include(a => a.Creator)
            .Include(a => a.TargetDepartment)
            .FirstOrDefaultAsync(a => a.Id == id);
    }

    public async Task<Announcement> CreateAsync(Announcement announcement, int createdBy)
    {
        announcement.CreatedBy = createdBy;
        announcement.CreatedAt = DateTime.UtcNow;
        announcement.CompanyId = _tenantResolver.GetCurrentTenantId();

        _db.Announcements.Add(announcement);
        await _db.SaveChangesAsync();
        return announcement;
    }

    public async Task UpdateAsync(Announcement announcement)
    {
        _db.Announcements.Update(announcement);
        await _db.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        var announcement = await _db.Announcements.FindAsync(id);
        if (announcement != null)
        {
            _db.Announcements.Remove(announcement);
            await _db.SaveChangesAsync();
        }
    }

    public async Task<int> GetUnreadCountAsync(int userId)
    {
        var weekAgo = DateTime.UtcNow.AddDays(-7);
        var announcements = await GetActiveAnnouncementsAsync(userId);
        return announcements.Count(a => a.CreatedAt > weekAgo);
    }
}
