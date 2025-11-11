using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;

namespace ShiftManager.Services;

public interface IChoreService
{
    Task<(bool Success, string Message, Chore? Chore)> CreateChoreAsync(int assigneeId, DateOnly date, string title, string? notes = null);
    Task<(bool Success, string Message)> CancelChoreAsync(int choreId, string? reason = null);
    Task<(bool Success, string Message, Chore? Chore)> ReplaceShiftWithChoreAsync(int shiftAssignmentId, string title, string? notes = null);
    Task<(bool Success, string Message)> ReplaceChoreWithShiftAsync(int choreId, int shiftInstanceId);
    Task<List<Chore>> GetChoresAsync(DateOnly? startDate = null, DateOnly? endDate = null, int? userId = null, bool? includeCancel = false);
    Task<Chore?> GetChoreByIdAsync(int choreId);
    Task<bool> HasActiveChoreOnDateAsync(int userId, DateOnly date);
    Task<bool> HasShiftOnDateAsync(int userId, DateOnly date);
    Task<ShiftAssignment?> GetShiftOnDateAsync(int userId, DateOnly date);
    Task<bool> CanUserManageChoresAsync(int userId);
    Task<bool> CanUserManageChoreForAssigneeAsync(int managerId, int assigneeId);
    Task<List<AppUser>> GetEligibleAssigneesAsync();
}

public class ChoreService : IChoreService
{
    private readonly AppDbContext _db;
    private readonly ITenantResolver _tenantResolver;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IDirectorService _directorService;
    private readonly ILogger<ChoreService> _logger;

    public ChoreService(
        AppDbContext db,
        ITenantResolver tenantResolver,
        IHttpContextAccessor httpContextAccessor,
        IDirectorService directorService,
        ILogger<ChoreService> logger)
    {
        _db = db;
        _tenantResolver = tenantResolver;
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
        return userId > 0 ? await _db.Users.FindAsync(userId) : null;
    }

    /// <summary>
    /// Check if current user can manage chores (Manager+, Assigner)
    /// </summary>
    public async Task<bool> CanUserManageChoresAsync(int userId)
    {
        var user = await _db.Users.FindAsync(userId);
        if (user == null) return false;

        return user.Role == UserRole.Owner ||
               user.Role == UserRole.Director ||
               user.Role == UserRole.Manager ||
               user.Role == UserRole.Assigner;
    }

    /// <summary>
    /// Check if manager can assign chore to a specific assignee
    /// - Manager can assign to employees and other managers in their company
    /// - Directors cannot be assigned chores
    /// - Directors can assign across companies they manage
    /// - Assigner can only assign within their own company
    /// </summary>
    public async Task<bool> CanUserManageChoreForAssigneeAsync(int managerId, int assigneeId)
    {
        var manager = await _db.Users.FindAsync(managerId);
        var assignee = await _db.Users.FindAsync(assigneeId);

        if (manager == null || assignee == null) return false;

        // Directors cannot be assigned chores
        if (assignee.Role == UserRole.Director) return false;

        // Owner can assign to anyone
        if (manager.Role == UserRole.Owner) return true;

        // Director can assign within companies they manage
        if (manager.Role == UserRole.Director)
        {
            return await _directorService.CanManageCompanyAsync(assignee.CompanyId);
        }

        // Manager and Assigner can only assign within their own company
        if (manager.Role == UserRole.Manager || manager.Role == UserRole.Assigner)
        {
            return manager.CompanyId == assignee.CompanyId;
        }

        return false;
    }

    /// <summary>
    /// Get list of users eligible for chore assignment (excludes Directors, includes current user's scope)
    /// </summary>
    public async Task<List<AppUser>> GetEligibleAssigneesAsync()
    {
        var currentUser = await GetCurrentUserAsync();
        if (currentUser == null)
        {
            return new List<AppUser>();
        }

        IQueryable<AppUser> query;

        if (currentUser.Role == UserRole.Owner)
        {
            // Owner sees all active users across all companies (except Directors)
            // Use IgnoreQueryFilters to bypass multi-tenant scoping
            query = _db.Users.IgnoreQueryFilters()
                .Where(u => u.IsActive && u.Role != UserRole.Director);
        }
        else if (currentUser.Role == UserRole.Director)
        {
            // Directors see users in companies they manage
            var companyIds = await _directorService.GetDirectorCompanyIdsAsync();
            query = _db.Users.IgnoreQueryFilters()
                .Where(u => u.IsActive && u.Role != UserRole.Director && companyIds.Contains(u.CompanyId));
        }
        else if (currentUser.Role == UserRole.Manager || currentUser.Role == UserRole.Assigner)
        {
            // Managers and Assigners see users in their own company only
            // Note: AppUser doesn't have query filter, so we must explicitly filter by CompanyId
            query = _db.Users
                .Where(u => u.IsActive && u.Role != UserRole.Director && u.CompanyId == currentUser.CompanyId);
        }
        else
        {
            // Employees and Trainees cannot create chores
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
    /// Check if user has an active chore on a specific date
    /// </summary>
    public async Task<bool> HasActiveChoreOnDateAsync(int userId, DateOnly date)
    {
        return await _db.Chores
            .AnyAsync(c => c.UserId == userId && c.Date == date && c.CanceledAt == null);
    }

    /// <summary>
    /// Check if user has a shift assignment on a specific date
    /// </summary>
    public async Task<bool> HasShiftOnDateAsync(int userId, DateOnly date)
    {
        return await _db.ShiftAssignments
            .Include(sa => sa.ShiftInstance)
            .AnyAsync(sa => sa.UserId == userId && sa.ShiftInstance!.WorkDate == date);
    }

    /// <summary>
    /// Get shift assignment for user on a specific date
    /// </summary>
    public async Task<ShiftAssignment?> GetShiftOnDateAsync(int userId, DateOnly date)
    {
        return await _db.ShiftAssignments
            .Include(sa => sa.ShiftInstance)
            .FirstOrDefaultAsync(sa => sa.UserId == userId && sa.ShiftInstance!.WorkDate == date);
    }

    /// <summary>
    /// Create a new chore assignment
    /// </summary>
    public async Task<(bool Success, string Message, Chore? Chore)> CreateChoreAsync(
        int assigneeId,
        DateOnly date,
        string title,
        string? notes = null)
    {
        try
        {
            var currentUserId = GetCurrentUserId();
            var currentUser = await GetCurrentUserAsync();

            if (currentUser == null)
            {
                return (false, "User not authenticated.", null);
            }

            // Check if current user can manage chores
            if (!await CanUserManageChoresAsync(currentUserId))
            {
                return (false, "You do not have permission to create chores.", null);
            }

            // Get assignee
            var assignee = await _db.Users.FindAsync(assigneeId);
            if (assignee == null)
            {
                return (false, "Assignee not found.", null);
            }

            // Check if assignee is eligible (not a Director, etc.)
            if (!await CanUserManageChoreForAssigneeAsync(currentUserId, assigneeId))
            {
                return (false, "You cannot assign chores to this user.", null);
            }

            // Check if assignee already has an active chore on this date
            if (await HasActiveChoreOnDateAsync(assigneeId, date))
            {
                return (false, "This user already has an active chore on this date.", null);
            }

            // Check if assignee has a shift on this date (warning, not blocking)
            // This should be handled in the UI with a confirmation dialog
            // For now, we block it here and let the UI call ReplaceShiftWithChoreAsync instead
            if (await HasShiftOnDateAsync(assigneeId, date))
            {
                return (false, "SHIFT_CONFLICT", null); // Special message for UI to handle
            }

            // Validate title
            if (string.IsNullOrWhiteSpace(title))
            {
                return (false, "Chore title is required.", null);
            }

            // Create the chore
            var chore = new Chore
            {
                CompanyId = assignee.CompanyId, // Use assignee's company ID for multi-tenant support
                UserId = assigneeId,
                Date = date,
                Title = title.Trim(),
                Notes = notes?.Trim(),
                CreatedBy = currentUserId,
                CreatedAt = DateTime.UtcNow
            };

            _db.Chores.Add(chore);
            await _db.SaveChangesAsync();

            _logger.LogInformation("Chore {ChoreId} created by user {CreatedBy} for user {UserId} on {Date}",
                chore.Id, currentUserId, assigneeId, date);

            return (true, "Chore created successfully.", chore);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating chore for user {UserId} on {Date}", assigneeId, date);
            return (false, "An error occurred while creating the chore.", null);
        }
    }

    /// <summary>
    /// Cancel (soft delete) a chore
    /// </summary>
    public async Task<(bool Success, string Message)> CancelChoreAsync(int choreId, string? reason = null)
    {
        try
        {
            var currentUserId = GetCurrentUserId();
            var currentUser = await GetCurrentUserAsync();

            if (currentUser == null)
            {
                return (false, "User not authenticated.");
            }

            var chore = await _db.Chores
                .Include(c => c.User)
                .FirstOrDefaultAsync(c => c.Id == choreId);

            if (chore == null)
            {
                return (false, "Chore not found.");
            }

            // Check if already canceled
            if (chore.CanceledAt != null)
            {
                return (false, "Chore is already canceled.");
            }

            // Check permissions
            if (!await CanUserManageChoresAsync(currentUserId))
            {
                return (false, "You do not have permission to cancel chores.");
            }

            // For Directors, check if they can manage the company
            if (currentUser.Role == UserRole.Director)
            {
                if (!await _directorService.CanManageCompanyAsync(chore.CompanyId))
                {
                    return (false, "You do not have permission to cancel this chore.");
                }
            }
            // For Managers, check if chore is in their company
            else if (currentUser.Role == UserRole.Manager)
            {
                if (chore.CompanyId != currentUser.CompanyId)
                {
                    return (false, "You do not have permission to cancel this chore.");
                }
            }

            // Cancel the chore
            chore.CanceledAt = DateTime.UtcNow;
            chore.CanceledBy = currentUserId;

            await _db.SaveChangesAsync();

            _logger.LogInformation("Chore {ChoreId} canceled by user {CanceledBy}. Reason: {Reason}",
                choreId, currentUserId, reason ?? "None");

            return (true, "Chore canceled successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error canceling chore {ChoreId}", choreId);
            return (false, "An error occurred while canceling the chore.");
        }
    }

    /// <summary>
    /// Replace an existing shift with a chore (transactional)
    /// </summary>
    public async Task<(bool Success, string Message, Chore? Chore)> ReplaceShiftWithChoreAsync(
        int shiftAssignmentId,
        string title,
        string? notes = null)
    {
        using var transaction = await _db.Database.BeginTransactionAsync();
        try
        {
            var currentUserId = GetCurrentUserId();
            var currentUser = await GetCurrentUserAsync();

            if (currentUser == null)
            {
                return (false, "User not authenticated.", null);
            }

            // Get the shift assignment
            var shiftAssignment = await _db.ShiftAssignments
                .Include(sa => sa.ShiftInstance)
                .FirstOrDefaultAsync(sa => sa.Id == shiftAssignmentId);

            if (shiftAssignment == null)
            {
                return (false, "Shift assignment not found.", null);
            }

            var assigneeId = shiftAssignment.UserId!.Value;
            var date = shiftAssignment.ShiftInstance!.WorkDate;

            // Validate permissions
            if (!await CanUserManageChoresAsync(currentUserId))
            {
                return (false, "You do not have permission to create chores.", null);
            }

            if (!await CanUserManageChoreForAssigneeAsync(currentUserId, assigneeId))
            {
                return (false, "You cannot assign chores to this user.", null);
            }

            // Delete the shift assignment
            _db.ShiftAssignments.Remove(shiftAssignment);

            // Create the chore
            var assignee = await _db.Users.FindAsync(assigneeId);
            if (assignee == null)
            {
                return (false, "Assignee not found.", null);
            }

            var chore = new Chore
            {
                CompanyId = assignee.CompanyId,
                UserId = assigneeId,
                Date = date,
                Title = title.Trim(),
                Notes = notes?.Trim(),
                CreatedBy = currentUserId,
                CreatedAt = DateTime.UtcNow
            };

            _db.Chores.Add(chore);
            await _db.SaveChangesAsync();
            await transaction.CommitAsync();

            _logger.LogInformation("Shift {ShiftAssignmentId} replaced with chore {ChoreId} by user {UserId}",
                shiftAssignmentId, chore.Id, currentUserId);

            return (true, "Shift replaced with chore successfully.", chore);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Error replacing shift {ShiftAssignmentId} with chore", shiftAssignmentId);
            return (false, "An error occurred while replacing the shift with a chore.", null);
        }
    }

    /// <summary>
    /// Replace an existing chore with a shift assignment (transactional)
    /// </summary>
    public async Task<(bool Success, string Message)> ReplaceChoreWithShiftAsync(int choreId, int shiftInstanceId)
    {
        using var transaction = await _db.Database.BeginTransactionAsync();
        try
        {
            var currentUserId = GetCurrentUserId();
            var currentUser = await GetCurrentUserAsync();

            if (currentUser == null)
            {
                return (false, "User not authenticated.");
            }

            // Get the chore
            var chore = await _db.Chores.FindAsync(choreId);
            if (chore == null)
            {
                return (false, "Chore not found.");
            }

            // Cancel the chore
            chore.CanceledAt = DateTime.UtcNow;
            chore.CanceledBy = currentUserId;

            // Create the shift assignment
            var shiftAssignment = new ShiftAssignment
            {
                CompanyId = chore.CompanyId,
                ShiftInstanceId = shiftInstanceId,
                UserId = chore.UserId,
                CreatedAt = DateTime.UtcNow
            };

            _db.ShiftAssignments.Add(shiftAssignment);
            await _db.SaveChangesAsync();
            await transaction.CommitAsync();

            _logger.LogInformation("Chore {ChoreId} replaced with shift on instance {ShiftInstanceId} by user {UserId}",
                choreId, shiftInstanceId, currentUserId);

            return (true, "Chore replaced with shift successfully.");
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Error replacing chore {ChoreId} with shift", choreId);
            return (false, "An error occurred while replacing the chore with a shift.");
        }
    }

    /// <summary>
    /// Get chores with optional filtering
    /// </summary>
    public async Task<List<Chore>> GetChoresAsync(
        DateOnly? startDate = null,
        DateOnly? endDate = null,
        int? userId = null,
        bool? includeCanceled = false)
    {
        var query = _db.Chores
            .Include(c => c.User)
            .Include(c => c.Creator)
            .Include(c => c.Canceler)
            .AsQueryable();

        if (startDate.HasValue)
        {
            query = query.Where(c => c.Date >= startDate.Value);
        }

        if (endDate.HasValue)
        {
            query = query.Where(c => c.Date <= endDate.Value);
        }

        if (userId.HasValue)
        {
            query = query.Where(c => c.UserId == userId.Value);
        }

        if (includeCanceled == false)
        {
            query = query.Where(c => c.CanceledAt == null);
        }

        return await query
            .OrderBy(c => c.Date)
            .ThenBy(c => c.CreatedAt)
            .ToListAsync();
    }

    /// <summary>
    /// Get a chore by ID
    /// </summary>
    public async Task<Chore?> GetChoreByIdAsync(int choreId)
    {
        return await _db.Chores
            .Include(c => c.User)
            .Include(c => c.Creator)
            .Include(c => c.Canceler)
            .FirstOrDefaultAsync(c => c.Id == choreId);
    }
}
