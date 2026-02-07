using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;

namespace ShiftManager.Services;

/// <summary>
/// Exports all data related to a specific user in JSON format.
/// GDPR-style data portability (QA Item 65 / E-06).
/// Accessible only to Owner via OwnerHub.
/// </summary>
// SECURITY-AUDITED: All IgnoreQueryFilters() in this class are SAFE — Owner-only service called via Grant:AdminAccess-protected endpoint;
// each query is scoped by specific userId parameter; returns read-only data (AsNoTracking)
public class UserDataExportService
{
    private readonly AppDbContext _db;
    private readonly ILogger<UserDataExportService> _logger;

    public UserDataExportService(AppDbContext db, ILogger<UserDataExportService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<object?> ExportUserDataAsync(int userId)
    {
        var user = await _db.Users
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user == null) return null;

        _logger.LogInformation("Exporting data for user {UserId} ({DisplayName})", userId, user.DisplayName);

        // Profile (exclude password hash/salt)
        var profile = new
        {
            user.Id,
            user.CompanyId,
            user.Email,
            user.DisplayName,
            user.PreferredName,
            user.Role,
            user.IsActive,
            user.Phone,
            user.City,
            user.DateOfBirth,
            user.JobTitle,
            user.HireDate,
            user.Skills,
            user.Certifications,
            user.Rank,
            user.EmergencyContactName,
            user.EmergencyContactPhone,
            user.EmergencyContactRelation,
            user.AvatarFileName,
            user.ProfileLastUpdated,
            user.JobTypeId,
            user.DepartmentId,
            user.FailedLoginAttempts,
            user.LockoutEnd,
            user.LastLoginAttempt
        };

        // Shift assignments
        var shiftAssignments = await _db.ShiftAssignments
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(sa => sa.UserId == userId)
            .Include(sa => sa.ShiftInstance)
                .ThenInclude(si => si!.ShiftType)
            .Select(sa => new
            {
                sa.Id,
                sa.ShiftInstanceId,
                ShiftDate = sa.ShiftInstance.WorkDate,
                ShiftType = sa.ShiftInstance.ShiftType != null ? sa.ShiftInstance.ShiftType.Name : "Unknown",
                sa.CreatedAt
            })
            .OrderByDescending(sa => sa.ShiftDate)
            .ToListAsync();

        // Chores
        var chores = await _db.Chores
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(c => c.UserId == userId)
            .Include(c => c.ChoreType)
            .Select(c => new
            {
                c.Id,
                c.Date,
                ChoreType = c.ChoreType != null ? c.ChoreType.Name : "Unknown",
                c.CanceledAt,
                c.CanceledBy,
                c.CreatedAt
            })
            .OrderByDescending(c => c.Date)
            .ToListAsync();

        // On-Duty entries
        var onDuties = await _db.OnDuties
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(od => od.UserId == userId)
            .Select(od => new
            {
                od.Id,
                od.Date,
                od.Type,
                od.Notes,
                od.CreatedAt
            })
            .OrderByDescending(od => od.Date)
            .ToListAsync();

        // Time-off requests
        var timeOffRequests = await _db.TimeOffRequests
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(t => t.UserId == userId)
            .Select(t => new
            {
                t.Id,
                t.StartDate,
                t.EndDate,
                t.Type,
                t.Reason,
                t.Status,
                t.CreatedAt,
                t.ApproverId
            })
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync();

        // Swap requests (from or to this user)
        var swapRequests = await _db.SwapRequests
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(sr => sr.FromUserId == userId || sr.ToUserId == userId)
            .Select(sr => new
            {
                sr.Id,
                sr.FromUserId,
                sr.ToUserId,
                sr.FromAssignmentId,
                sr.ToAssignmentId,
                sr.Status,
                sr.Reason,
                sr.DeclineReason,
                sr.CreatedAt,
                sr.ReviewedAt,
                sr.ReviewedBy
            })
            .OrderByDescending(sr => sr.CreatedAt)
            .ToListAsync();

        // Notifications
        var notifications = await _db.UserNotifications
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(n => n.UserId == userId)
            .Select(n => new
            {
                n.Id,
                n.Type,
                n.Message,
                n.CreatedAt,
                n.ReadAt
            })
            .OrderByDescending(n => n.CreatedAt)
            .Take(500)
            .ToListAsync();

        // Game scores
        var gameScores = await _db.GameScores
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(gs => gs.UserId == userId)
            .Select(gs => new
            {
                gs.Id,
                gs.Score,
                gs.PlayedAt
            })
            .OrderByDescending(gs => gs.PlayedAt)
            .ToListAsync();

        // Audit log (actions BY this user)
        var auditLogs = await _db.AuditLogs
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(al => al.UserId == userId)
            .Select(al => new
            {
                al.Id,
                al.Action,
                al.EntityType,
                al.EntityId,
                al.Description,
                al.Timestamp
            })
            .OrderByDescending(al => al.Timestamp)
            .Take(1000)
            .ToListAsync();

        // Grants
        var grants = await _db.Grants
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(g => g.UserId == userId)
            .Include(g => g.GrantType)
            .Select(g => new
            {
                g.Id,
                GrantTypeKey = g.GrantType != null ? g.GrantType.Key : "Unknown",
                g.ProjectId,
                g.AreaId,
                g.MoleculeId,
                g.CompanyId,
                g.JobTypeId,
                g.CanOwn,
                g.CanGive,
                g.GrantedAt,
                g.GrantedByUserId,
                g.IsAutoGrant
            })
            .ToListAsync();

        // Role assignments
        var roleAssignments = await _db.UserRoleAssignments
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(ra => ra.UserId == userId)
            .Include(ra => ra.RoleTemplate)
            .Select(ra => new
            {
                ra.Id,
                RoleTemplateKey = ra.RoleTemplate != null ? ra.RoleTemplate.Key : "Unknown",
                ra.CompanyId,
                ra.MoleculeId,
                ra.AreaId,
                ra.JobTypeId,
                ra.DepartmentId,
                ra.AssignedAt,
                ra.AssignedByUserId,
                ra.IsActive
            })
            .ToListAsync();

        // Day notes
        var dayNotes = await _db.UserDayNotes
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(n => n.UserId == userId)
            .Select(n => new
            {
                n.Id,
                n.Date,
                n.Note,
                n.CreatedAt
            })
            .OrderByDescending(n => n.Date)
            .ToListAsync();

        // Friendships
        var friendships = await _db.UserFriendships
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(f => f.UserId == userId || f.FriendId == userId)
            .Select(f => new
            {
                f.Id,
                f.UserId,
                f.FriendId,
                f.Status,
                f.RequestedAt
            })
            .ToListAsync();

        return new
        {
            ExportedAt = DateTime.UtcNow,
            ExportVersion = "1.0",
            Profile = profile,
            ShiftAssignments = shiftAssignments,
            Chores = chores,
            OnDutyEntries = onDuties,
            TimeOffRequests = timeOffRequests,
            SwapRequests = swapRequests,
            Notifications = notifications,
            GameScores = gameScores,
            AuditLogEntries = auditLogs,
            Grants = grants,
            RoleAssignments = roleAssignments,
            DayNotes = dayNotes,
            Friendships = friendships
        };
    }
}
