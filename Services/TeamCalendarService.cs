using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;

namespace ShiftManager.Services;

/// <summary>
/// Service for managing team calendars and their members.
/// Handles calendar CRUD operations, member management, and ownership validation.
/// </summary>
public class TeamCalendarService
{
    private readonly AppDbContext _context;
    private readonly ITenantResolver _tenantResolver;

    public TeamCalendarService(AppDbContext context, ITenantResolver tenantResolver)
    {
        _context = context;
        _tenantResolver = tenantResolver;
    }

    #region Calendar Management

    /// <summary>
    /// Gets all non-deleted calendars for a specific owner.
    /// </summary>
    public async Task<List<TeamCalendar>> GetCalendarsForOwnerAsync(int ownerId)
    {
        return await _context.TeamCalendars
            .Where(tc => tc.OwnerId == ownerId && !tc.IsDeleted)
            .OrderBy(tc => tc.Name)
            .Include(tc => tc.Members)
            .ToListAsync();
    }

    /// <summary>
    /// Gets a specific calendar by ID if the user is the owner.
    /// </summary>
    public async Task<TeamCalendar?> GetCalendarByIdAsync(int calendarId, int ownerId)
    {
        return await _context.TeamCalendars
            .Include(tc => tc.Members)
            .ThenInclude(m => m.Member)
            .FirstOrDefaultAsync(tc => tc.Id == calendarId && tc.OwnerId == ownerId && !tc.IsDeleted);
    }

    /// <summary>
    /// Creates a new team calendar for the current user.
    /// </summary>
    public async Task<(bool Success, TeamCalendar? Calendar, string? ErrorMessage)> CreateCalendarAsync(
        int ownerId,
        string name)
    {
        var companyId = _tenantResolver.GetCurrentTenantId();

        // Validate name
        if (string.IsNullOrWhiteSpace(name) || name.Length > 60)
        {
            return (false, null, "Calendar name must be 1-60 characters");
        }

        // Check for duplicate name (only active calendars)
        var existingCalendar = await _context.TeamCalendars
            .FirstOrDefaultAsync(tc =>
                tc.CompanyId == companyId &&
                tc.OwnerId == ownerId &&
                tc.Name == name &&
                !tc.IsDeleted);

        if (existingCalendar != null)
        {
            return (false, null, "A calendar with this name already exists");
        }

        var calendar = new TeamCalendar
        {
            CompanyId = companyId,
            OwnerId = ownerId,
            Name = name,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            IsDeleted = false
        };

        _context.TeamCalendars.Add(calendar);
        await _context.SaveChangesAsync();

        return (true, calendar, null);
    }

    /// <summary>
    /// Renames a calendar if the user is the owner.
    /// </summary>
    public async Task<(bool Success, string? ErrorMessage)> RenameCalendarAsync(
        int calendarId,
        int ownerId,
        string newName)
    {
        var companyId = _tenantResolver.GetCurrentTenantId();

        // Validate name
        if (string.IsNullOrWhiteSpace(newName) || newName.Length > 60)
        {
            return (false, "Calendar name must be 1-60 characters");
        }

        var calendar = await _context.TeamCalendars
            .FirstOrDefaultAsync(tc => tc.Id == calendarId && tc.OwnerId == ownerId && !tc.IsDeleted);

        if (calendar == null)
        {
            return (false, "Calendar not found");
        }

        // Check for duplicate name (only active calendars, excluding current calendar)
        var existingCalendar = await _context.TeamCalendars
            .FirstOrDefaultAsync(tc =>
                tc.CompanyId == companyId &&
                tc.OwnerId == ownerId &&
                tc.Name == newName &&
                !tc.IsDeleted &&
                tc.Id != calendarId);

        if (existingCalendar != null)
        {
            return (false, "A calendar with this name already exists");
        }

        calendar.Name = newName;
        calendar.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return (true, null);
    }

    /// <summary>
    /// Soft deletes a calendar if the user is the owner.
    /// </summary>
    public async Task<(bool Success, string? ErrorMessage)> DeleteCalendarAsync(
        int calendarId,
        int ownerId)
    {
        var calendar = await _context.TeamCalendars
            .FirstOrDefaultAsync(tc => tc.Id == calendarId && tc.OwnerId == ownerId && !tc.IsDeleted);

        if (calendar == null)
        {
            return (false, "Calendar not found");
        }

        calendar.IsDeleted = true;
        calendar.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return (true, null);
    }

    #endregion

    #region Member Management

    /// <summary>
    /// Gets all members for a specific calendar.
    /// </summary>
    public async Task<List<AppUser>> GetCalendarMembersAsync(int calendarId, int ownerId)
    {
        var calendar = await _context.TeamCalendars
            .Include(tc => tc.Members)
            .ThenInclude(m => m.Member)
            .FirstOrDefaultAsync(tc => tc.Id == calendarId && tc.OwnerId == ownerId && !tc.IsDeleted);

        if (calendar == null)
        {
            return new List<AppUser>();
        }

        return calendar.Members
            .Select(m => m.Member)
            .OrderBy(u => u.DisplayName)
            .ToList();
    }

    /// <summary>
    /// Gets all company users who are NOT members of the specified calendar.
    /// </summary>
    public async Task<List<AppUser>> GetAvailableUsersAsync(int calendarId, int ownerId)
    {
        var companyId = _tenantResolver.GetCurrentTenantId();

        // Verify ownership
        var calendar = await _context.TeamCalendars
            .FirstOrDefaultAsync(tc => tc.Id == calendarId && tc.OwnerId == ownerId && !tc.IsDeleted);

        if (calendar == null)
        {
            return new List<AppUser>();
        }

        var memberUserIds = await _context.TeamCalendarMembers
            .Where(m => m.TeamCalendarId == calendarId)
            .Select(m => m.MemberUserId)
            .ToListAsync();

        return await _context.Users
            .Where(u => u.CompanyId == companyId && !memberUserIds.Contains(u.Id))
            .OrderBy(u => u.DisplayName)
            .ToListAsync();
    }

    /// <summary>
    /// Adds multiple members to a calendar.
    /// </summary>
    public async Task<(bool Success, string? ErrorMessage)> AddMembersAsync(
        int calendarId,
        int ownerId,
        List<int> memberUserIds)
    {
        var companyId = _tenantResolver.GetCurrentTenantId();

        // Verify ownership
        var calendar = await _context.TeamCalendars
            .Include(tc => tc.Members)
            .FirstOrDefaultAsync(tc => tc.Id == calendarId && tc.OwnerId == ownerId && !tc.IsDeleted);

        if (calendar == null)
        {
            return (false, "Calendar not found");
        }

        // Verify all users belong to the same company
        var validUsers = await _context.Users
            .Where(u => memberUserIds.Contains(u.Id) && u.CompanyId == companyId)
            .Select(u => u.Id)
            .ToListAsync();

        if (validUsers.Count != memberUserIds.Count)
        {
            return (false, "Some users are invalid or don't belong to your company");
        }

        // Get existing member IDs to avoid duplicates
        var existingMemberIds = calendar.Members.Select(m => m.MemberUserId).ToHashSet();

        // Add new members
        var now = DateTime.UtcNow;
        foreach (var userId in memberUserIds.Where(id => !existingMemberIds.Contains(id)))
        {
            calendar.Members.Add(new TeamCalendarMember
            {
                TeamCalendarId = calendarId,
                MemberUserId = userId,
                AddedAt = now
            });
        }

        calendar.UpdatedAt = now;
        await _context.SaveChangesAsync();

        return (true, null);
    }

    /// <summary>
    /// Removes multiple members from a calendar.
    /// </summary>
    public async Task<(bool Success, string? ErrorMessage)> RemoveMembersAsync(
        int calendarId,
        int ownerId,
        List<int> memberUserIds)
    {
        // Verify ownership
        var calendar = await _context.TeamCalendars
            .Include(tc => tc.Members)
            .FirstOrDefaultAsync(tc => tc.Id == calendarId && tc.OwnerId == ownerId && !tc.IsDeleted);

        if (calendar == null)
        {
            return (false, "Calendar not found");
        }

        var membersToRemove = calendar.Members
            .Where(m => memberUserIds.Contains(m.MemberUserId))
            .ToList();

        foreach (var member in membersToRemove)
        {
            calendar.Members.Remove(member);
        }

        calendar.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return (true, null);
    }

    /// <summary>
    /// Replaces all members of a calendar with a new set.
    /// Efficient for the "Save Changes" operation in the member manager.
    /// </summary>
    public async Task<(bool Success, string? ErrorMessage)> SetMembersAsync(
        int calendarId,
        int ownerId,
        List<int> memberUserIds)
    {
        var companyId = _tenantResolver.GetCurrentTenantId();

        // Verify ownership
        var calendar = await _context.TeamCalendars
            .Include(tc => tc.Members)
            .FirstOrDefaultAsync(tc => tc.Id == calendarId && tc.OwnerId == ownerId && !tc.IsDeleted);

        if (calendar == null)
        {
            return (false, "Calendar not found");
        }

        // Verify all users belong to the same company
        var validUsers = await _context.Users
            .Where(u => memberUserIds.Contains(u.Id) && u.CompanyId == companyId)
            .Select(u => u.Id)
            .ToListAsync();

        if (validUsers.Count != memberUserIds.Count)
        {
            return (false, "Some users are invalid or don't belong to your company");
        }

        // Remove all existing members
        calendar.Members.Clear();

        // Add new members
        var now = DateTime.UtcNow;
        foreach (var userId in memberUserIds.Distinct())
        {
            calendar.Members.Add(new TeamCalendarMember
            {
                TeamCalendarId = calendarId,
                MemberUserId = userId,
                AddedAt = now
            });
        }

        calendar.UpdatedAt = now;
        await _context.SaveChangesAsync();

        return (true, null);
    }

    #endregion
}
