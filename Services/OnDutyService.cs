using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;

namespace ShiftManager.Services;

/// <summary>
/// Interface for "Day Shifts" management (user-facing term: "משמרות יומיות" / "Day Shifts").
///
/// TERMINOLOGY NOTE: Named "IOnDutyService" for historical reasons.
/// See TERMINOLOGY.md for UI vs. code terminology mapping.
/// </summary>
public interface IOnDutyService
{
    Task<(bool Success, string Message, OnDuty? OnDuty)> CreateOnDutyAsync(int assigneeId, DateOnly date, OnDutyType type, string? notes = null, bool forceAssign = false);
    Task<(bool Success, string Message)> CancelOnDutyAsync(int onDutyId, string? reason = null);
    Task<List<OnDuty>> GetOnDutiesAsync(DateOnly? startDate = null, DateOnly? endDate = null, int? userId = null, OnDutyType? type = null, bool? includeCanceled = false);
    Task<OnDuty?> GetOnDutyByIdAsync(int onDutyId);
    Task<bool> HasActiveOnDutyOnDateAsync(int userId, DateOnly date, OnDutyType type);
    Task<bool> HasVacationConflictAsync(int userId, DateOnly date);
    Task<(bool HasConflict, DateOnly? StartDate, DateOnly? EndDate, TimeOffType? Type)> GetVacationConflictDetailsAsync(int userId, DateOnly date);
    Task<bool> CanUserManageOnDutyAsync(int userId);
    Task<List<AppUser>> GetEligibleAssigneesAsync();

    /// <summary>
    /// Get users eligible for a specific duty type, optionally requiring officer rank.
    /// </summary>
    Task<List<AppUser>> GetEligibleUsersForDutyAsync(OnDutyType dutyType, bool requireOfficer = false);

    /// <summary>
    /// Check if a specific user is eligible for a duty type.
    /// </summary>
    Task<bool> IsUserEligibleForDutyAsync(int userId, OnDutyType dutyType, bool requireOfficer = false);
}

/// <summary>
/// Service for managing "Day Shifts" (user-facing term: "משמרות יומיות" / "Day Shifts").
///
/// TERMINOLOGY NOTE: The service is named "OnDutyService" for historical reasons.
/// In the user interface, these are presented as "Day Shifts" to be more inclusive
/// of all workers (not just official "on-duty" roles like Hakam/Lead).
///
/// Day shifts are:
/// - Full day-length assignments (no specific start/end times, just dates)
/// - Global/cross-company (unlike scheduled shifts which are company-scoped)
/// - One person per type per date
/// - Support types: Hakam, Lead, and custom types
///
/// See TERMINOLOGY.md for complete terminology mapping.
/// </summary>
public class OnDutyService : IOnDutyService
{
    private readonly AppDbContext _db;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IDirectorService _directorService;
    private readonly ILogger<OnDutyService> _logger;

    public OnDutyService(
        AppDbContext db,
        IHttpContextAccessor httpContextAccessor,
        IDirectorService directorService,
        ILogger<OnDutyService> logger)
    {
        _db = db;
        _httpContextAccessor = httpContextAccessor;
        _directorService = directorService;
        _logger = logger;
    }

    private int GetCurrentUserId()
    {
        var userIdClaim = _httpContextAccessor.HttpContext?.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(userIdClaim, out var userId) ? userId : 0;
    }

    private async Task<AppUser?> GetCurrentUserAsync()
    {
        var userId = GetCurrentUserId();
        if (userId <= 0) return null;

        // Must use IgnoreQueryFilters since we need to access users across all companies
        return await _db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == userId);
    }

    /// <summary>
    /// Check if current user can manage on-duty assignments (Manager+, NOT Assigner)
    /// Assigner role can only manage Chores, not OnDuty
    /// </summary>
    public async Task<bool> CanUserManageOnDutyAsync(int userId)
    {
        var user = await _db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null) return false;

        // Only Manager, Director, and Owner can manage OnDuty
        // Assigner role is explicitly excluded
        return user.Role == UserRole.Owner ||
               user.Role == UserRole.Director ||
               user.Role == UserRole.Manager;
    }

    /// <summary>
    /// Get list of users eligible for on-duty assignment (all active users across all companies)
    /// Directors can be assigned OnDuty (unlike Chores)
    /// </summary>
    public async Task<List<AppUser>> GetEligibleAssigneesAsync()
    {
        var currentUser = await GetCurrentUserAsync();
        if (currentUser == null)
        {
            return new List<AppUser>();
        }

        // Check if current user can manage OnDuty
        if (!await CanUserManageOnDutyAsync(currentUser.Id))
        {
            return new List<AppUser>();
        }

        IQueryable<AppUser> query;

        if (currentUser.Role == UserRole.Owner)
        {
            // Owner sees all active users across all companies
            query = _db.Users.IgnoreQueryFilters()
                .Where(u => u.IsActive);
        }
        else if (currentUser.Role == UserRole.Director)
        {
            // Directors see users in companies they manage
            var companyIds = await _directorService.GetDirectorCompanyIdsAsync();
            query = _db.Users.IgnoreQueryFilters()
                .Where(u => u.IsActive && companyIds.Contains(u.CompanyId));
        }
        else if (currentUser.Role == UserRole.Manager)
        {
            // Managers see users in their own company only
            query = _db.Users.IgnoreQueryFilters()
                .Where(u => u.IsActive && u.CompanyId == currentUser.CompanyId);
        }
        else
        {
            // Employees, Trainees, and Assigners cannot create OnDuty
            return new List<AppUser>();
        }

        return await query
            .OrderBy(u => u.DisplayName)
            .Select(u => new AppUser
            {
                Id = u.Id,
                DisplayName = u.DisplayName,
                Email = u.Email,
                Role = u.Role,
                CompanyId = u.CompanyId
            })
            .ToListAsync();
    }

    /// <summary>
    /// Check if user has an active on-duty assignment on a specific date and type
    /// </summary>
    public async Task<bool> HasActiveOnDutyOnDateAsync(int userId, DateOnly date, OnDutyType type)
    {
        // OnDuty is global - must use IgnoreQueryFilters
        return await _db.OnDuties.IgnoreQueryFilters()
            .AnyAsync(o => o.UserId == userId && o.Date == date && o.Type == type && o.CanceledAt == null);
    }

    /// <summary>
    /// Check if user has an approved vacation that overlaps with the given date
    /// COLLISION RULE: OnDuty assignments cannot overlap with approved vacations
    /// </summary>
    public async Task<bool> HasVacationConflictAsync(int userId, DateOnly date)
    {
        // Get user to find their company (needed for vacation query)
        var user = await _db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null) return false;

        // Check for approved time off requests that include this date
        // Vacation logic: StartDate 00:00 to EndDate+1 13:00
        // After logic: StartDate 16:00 to StartDate+1 13:00
        var hasConflict = await _db.TimeOffRequests.IgnoreQueryFilters()
            .AnyAsync(t => t.UserId == userId &&
                          t.CompanyId == user.CompanyId &&
                          t.Status == RequestStatus.Approved &&
                          t.StartDate <= date &&
                          t.EndDate >= date);

        return hasConflict;
    }

    /// <summary>
    /// Get vacation details if user has an approved vacation that overlaps with the given date
    /// Returns (hasConflict, startDate, endDate, type)
    /// </summary>
    public async Task<(bool HasConflict, DateOnly? StartDate, DateOnly? EndDate, TimeOffType? Type)>
        GetVacationConflictDetailsAsync(int userId, DateOnly date)
    {
        var user = await _db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null) return (false, null, null, null);

        var vacation = await _db.TimeOffRequests.IgnoreQueryFilters()
            .Where(t => t.UserId == userId &&
                       t.CompanyId == user.CompanyId &&
                       t.Status == RequestStatus.Approved &&
                       t.StartDate <= date &&
                       t.EndDate >= date)
            .FirstOrDefaultAsync();

        if (vacation == null)
            return (false, null, null, null);

        return (true, vacation.StartDate, vacation.EndDate, vacation.Type);
    }

    /// <summary>
    /// Create a new on-duty assignment
    /// </summary>
    public async Task<(bool Success, string Message, OnDuty? OnDuty)> CreateOnDutyAsync(
        int assigneeId,
        DateOnly date,
        OnDutyType type,
        string? notes = null,
        bool forceAssign = false)
    {
        var currentUserId = GetCurrentUserId();
        int? companyId = null;
        try
        {
            var currentUser = await GetCurrentUserAsync();
            companyId = currentUser?.CompanyId;

            if (currentUser == null)
            {
                return (false, "User not authenticated.", null);
            }

            // Check if current user can manage OnDuty
            if (!await CanUserManageOnDutyAsync(currentUserId))
            {
                return (false, "You do not have permission to create on-duty assignments.", null);
            }

            // Check if duty type requires officer rank
            var requiresOfficer = await RequiresOfficerForDutyTypeAsync(type);
            if (requiresOfficer)
            {
                var isEligible = await IsUserEligibleForDutyAsync(assigneeId, type, requireOfficer: true);
                if (!isEligible)
                {
                    _logger.LogWarning(
                        "User {UserId} is not eligible for duty type {DutyType} - officer rank required",
                        assigneeId, type);
                    return (false, "OFFICER_RANK_REQUIRED", null);
                }
            }

            // Get assignee (must use IgnoreQueryFilters for cross-company access)
            var assignee = await _db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == assigneeId);
            if (assignee == null)
            {
                return (false, "Assignee not found.", null);
            }

            // Check if assignee is active
            if (!assignee.IsActive)
            {
                return (false, "Cannot assign on-duty to inactive user.", null);
            }

            // Check if assignee already has an active on-duty on this date and type
            if (await HasActiveOnDutyOnDateAsync(assigneeId, date, type))
            {
                return (false, $"This user already has an active {type} on-duty assignment on this date.", null);
            }

            // COLLISION RULE: Check for vacation conflict (unless force-assigning)
            if (!forceAssign)
            {
                var (hasConflict, vacationStart, vacationEnd, vacationType) = await GetVacationConflictDetailsAsync(assigneeId, date);
                if (hasConflict)
                {
                    // Return vacation details for UI to display in confirmation dialog
                    return (false, $"VACATION_CONFLICT|{vacationStart}|{vacationEnd}|{vacationType}", null);
                }
            }

            // Create the on-duty assignment
            var onDuty = new OnDuty
            {
                UserId = assigneeId,
                Date = date,
                Type = type,
                Notes = notes?.Trim(),
                CreatedBy = currentUserId,
                CreatedAt = DateTime.UtcNow
            };

            _db.OnDuties.Add(onDuty);
            await _db.SaveChangesAsync();

            _logger.LogInformation("OnDuty {OnDutyId} ({Type}) created by user {CreatedBy} for user {UserId} on {Date}",
                onDuty.Id, type, currentUserId, assigneeId, date);

            // Audit log for force-assignments (bypassing vacation conflict)
            if (forceAssign)
            {
                _logger.LogWarning("OnDuty {OnDutyId} was FORCE-ASSIGNED by user {CreatedBy} despite vacation conflict for user {UserId} on {Date}",
                    onDuty.Id, currentUserId, assigneeId, date);
            }

            return (true, "On-duty assignment created successfully.", onDuty);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Database error creating on-duty. CompanyId={CompanyId}, CreatedBy={CreatedBy}, AssigneeId={AssigneeId}, Date={Date}, Type={Type}",
                companyId, currentUserId, assigneeId, date, type);
            return (false, "An error occurred while creating the on-duty assignment.", null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error creating on-duty. CompanyId={CompanyId}, CreatedBy={CreatedBy}, AssigneeId={AssigneeId}, Date={Date}, Type={Type}",
                companyId, currentUserId, assigneeId, date, type);
            return (false, "An error occurred while creating the on-duty assignment.", null);
        }
    }

    /// <summary>
    /// Cancel (soft delete) an on-duty assignment
    /// </summary>
    public async Task<(bool Success, string Message)> CancelOnDutyAsync(int onDutyId, string? reason = null)
    {
        var currentUserId = GetCurrentUserId();
        int? companyId = null;
        try
        {
            var currentUser = await GetCurrentUserAsync();
            companyId = currentUser?.CompanyId;

            if (currentUser == null)
            {
                return (false, "User not authenticated.");
            }

            // OnDuty is global - must use IgnoreQueryFilters
            var onDuty = await _db.OnDuties.IgnoreQueryFilters()
                .Include(o => o.User)
                .FirstOrDefaultAsync(o => o.Id == onDutyId);

            if (onDuty == null)
            {
                return (false, "On-duty assignment not found.");
            }

            // Check if already canceled
            if (onDuty.CanceledAt != null)
            {
                return (false, "On-duty assignment is already canceled.");
            }

            // Check permissions
            if (!await CanUserManageOnDutyAsync(currentUserId))
            {
                return (false, "You do not have permission to cancel on-duty assignments.");
            }

            // For Directors, check if they can manage the assignee's company
            if (currentUser.Role == UserRole.Director && onDuty.User != null)
            {
                if (!await _directorService.CanManageCompanyAsync(onDuty.User.CompanyId))
                {
                    return (false, "You do not have permission to cancel this on-duty assignment.");
                }
            }
            // For Managers, check if assignee is in their company
            else if (currentUser.Role == UserRole.Manager && onDuty.User != null)
            {
                if (onDuty.User.CompanyId != currentUser.CompanyId)
                {
                    return (false, "You do not have permission to cancel this on-duty assignment.");
                }
            }

            // Cancel the on-duty assignment
            onDuty.CanceledAt = DateTime.UtcNow;
            onDuty.CanceledBy = currentUserId;

            await _db.SaveChangesAsync();

            _logger.LogInformation("OnDuty {OnDutyId} canceled by user {CanceledBy}. Reason: {Reason}",
                onDutyId, currentUserId, reason ?? "None");

            return (true, "On-duty assignment canceled successfully.");
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Database error canceling on-duty. CompanyId={CompanyId}, CanceledBy={CanceledBy}, OnDutyId={OnDutyId}, Reason={Reason}",
                companyId, currentUserId, onDutyId, reason ?? "None");
            return (false, "An error occurred while canceling the on-duty assignment.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error canceling on-duty. CompanyId={CompanyId}, CanceledBy={CanceledBy}, OnDutyId={OnDutyId}, Reason={Reason}",
                companyId, currentUserId, onDutyId, reason ?? "None");
            return (false, "An error occurred while canceling the on-duty assignment.");
        }
    }

    /// <summary>
    /// Get on-duty assignments with optional filtering
    /// </summary>
    public async Task<List<OnDuty>> GetOnDutiesAsync(
        DateOnly? startDate = null,
        DateOnly? endDate = null,
        int? userId = null,
        OnDutyType? type = null,
        bool? includeCanceled = false)
    {
        // OnDuty is global - must use IgnoreQueryFilters
        var query = _db.OnDuties.IgnoreQueryFilters()
            .Include(o => o.User)
            .Include(o => o.Creator)
            .Include(o => o.Canceler)
            .AsQueryable();

        if (startDate.HasValue)
        {
            query = query.Where(o => o.Date >= startDate.Value);
        }

        if (endDate.HasValue)
        {
            query = query.Where(o => o.Date <= endDate.Value);
        }

        if (userId.HasValue)
        {
            query = query.Where(o => o.UserId == userId.Value);
        }

        if (type.HasValue)
        {
            query = query.Where(o => o.Type == type.Value);
        }

        if (includeCanceled == false)
        {
            query = query.Where(o => o.CanceledAt == null);
        }

        return await query
            .OrderBy(o => o.Date)
            .ThenBy(o => o.Type)
            .ThenBy(o => o.CreatedAt)
            .ToListAsync();
    }

    /// <summary>
    /// Get an on-duty assignment by ID
    /// </summary>
    public async Task<OnDuty?> GetOnDutyByIdAsync(int onDutyId)
    {
        // OnDuty is global - must use IgnoreQueryFilters
        return await _db.OnDuties.IgnoreQueryFilters()
            .Include(o => o.User)
            .Include(o => o.Creator)
            .Include(o => o.Canceler)
            .FirstOrDefaultAsync(o => o.Id == onDutyId);
    }

    /// <summary>
    /// Get users eligible for a specific duty type, optionally requiring officer rank.
    /// </summary>
    public async Task<List<AppUser>> GetEligibleUsersForDutyAsync(OnDutyType dutyType, bool requireOfficer = false)
    {
        var query = _db.Users
            .IgnoreQueryFilters() // OnDuty is cross-company
            .Where(u => u.IsActive);

        if (requireOfficer)
        {
            // Officers have rank >= 9 (SegenMishne)
            query = query.Where(u => (int)u.Rank >= 9);
        }

        return await query
            .OrderBy(u => u.DisplayName)
            .ToListAsync();
    }

    /// <summary>
    /// Check if a duty type requires officer rank.
    /// </summary>
    private async Task<bool> RequiresOfficerForDutyTypeAsync(OnDutyType type)
    {
        // Built-in Lead type (Katzin) requires officer
        if (type == OnDutyType.Lead)
            return true;

        // Check custom types
        var customConfig = await _db.OnDutyTypeConfigs
            .FirstOrDefaultAsync(c => c.TypeValue == (int)type && c.IsActive);

        return customConfig?.RequiresOfficerRank ?? false;
    }

    /// <summary>
    /// Check if a specific user is eligible for a duty type.
    /// </summary>
    public async Task<bool> IsUserEligibleForDutyAsync(int userId, OnDutyType dutyType, bool requireOfficer = false)
    {
        var user = await _db.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user == null || !user.IsActive)
            return false;

        if (requireOfficer && !user.Rank.IsOfficer())
            return false;

        return true;
    }
}
