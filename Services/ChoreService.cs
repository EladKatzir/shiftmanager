using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;

namespace ShiftManager.Services;

public interface IChoreService
{
    Task<(bool Success, string Message, Chore? Chore)> CreateChoreAsync(int assigneeId, DateOnly date, string title, string? notes = null, bool forceAssign = false, int? moleculeId = null);
    Task<(bool Success, string Message)> CancelChoreAsync(int choreId, string? reason = null);
    Task<(bool Success, string Message, Chore? Chore)> ReplaceShiftWithChoreAsync(int shiftAssignmentId, string title, string? notes = null);
    Task<(bool Success, string Message)> ReplaceChoreWithShiftAsync(int choreId, int shiftInstanceId);
    Task<List<Chore>> GetChoresAsync(DateOnly? startDate = null, DateOnly? endDate = null, int? userId = null, bool? includeCancel = false, int? moleculeId = null);
    Task<Chore?> GetChoreByIdAsync(int choreId);
    Task<bool> HasActiveChoreOnDateAsync(int userId, DateOnly date);
    Task<bool> HasShiftOnDateAsync(int userId, DateOnly date);
    Task<ShiftAssignment?> GetShiftOnDateAsync(int userId, DateOnly date);
    Task<bool> HasVacationConflictAsync(int userId, DateOnly date);
    Task<(bool HasConflict, DateOnly? StartDate, DateOnly? EndDate, TimeOffType? Type)> GetVacationConflictDetailsAsync(int userId, DateOnly date);
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
    private readonly IGrantService _grantService;
    private readonly ILogger<ChoreService> _logger;

    public ChoreService(
        AppDbContext db,
        ITenantResolver tenantResolver,
        IHttpContextAccessor httpContextAccessor,
        IDirectorService directorService,
        IGrantService grantService,
        ILogger<ChoreService> logger)
    {
        _db = db;
        _tenantResolver = tenantResolver;
        _httpContextAccessor = httpContextAccessor;
        _directorService = directorService;
        _grantService = grantService;
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
    /// Check if current user can manage chores.
    /// ✅ Grant-based: Uses AssignChores grant instead of role checks.
    /// </summary>
    public async Task<bool> CanUserManageChoresAsync(int userId)
    {
        // Check if user has the AssignChores grant
        return await _grantService.HasGrantAsync(userId, "AssignChores");
    }

    /// <summary>
    /// Check if manager can assign chore to a specific assignee.
    /// ✅ Grant-based: Uses AssignChores grant with company scope instead of role checks.
    /// Business rule: Directors cannot be assigned chores (enforced separately).
    /// </summary>
    public async Task<bool> CanUserManageChoreForAssigneeAsync(int managerId, int assigneeId)
    {
        var assignee = await _db.Users.FindAsync(assigneeId);

        if (assignee == null) return false;

        // Directors cannot be assigned chores (business rule, not grant-based)
        if (assignee.Role == UserRole.Director) return false;

        // Check if manager has AssignChores grant for the assignee's company
        return await _grantService.HasGrantForCompanyAsync(managerId, "AssignChores", assignee.CompanyId);
    }

    /// <summary>
    /// Get list of users eligible for chore assignment.
    /// ✅ Grant-based: Uses AssignChores grant scope to determine visible users.
    /// Business rule "Directors cannot be assigned chores" is enforced at assignment time in CanUserManageChoreForAssigneeAsync.
    /// </summary>
    public async Task<List<AppUser>> GetEligibleAssigneesAsync()
    {
        var currentUserId = GetCurrentUserId();
        if (currentUserId <= 0)
        {
            return new List<AppUser>();
        }

        // Get accessible company IDs based on AssignChores grant scope
        var accessibleCompanyIds = await _grantService.GetAccessibleCompanyIdsForGrantAsync(currentUserId, "AssignChores");

        if (!accessibleCompanyIds.Any())
        {
            // No grant = no access
            return new List<AppUser>();
        }

        // Query users in accessible companies
        var query = _db.Users.IgnoreQueryFilters()
            .Where(u => u.IsActive && accessibleCompanyIds.Contains(u.CompanyId));

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
    /// Check if user has an approved vacation that overlaps with the given date
    /// COLLISION RULE: Chore assignments cannot overlap with approved vacations
    /// </summary>
    public async Task<bool> HasVacationConflictAsync(int userId, DateOnly date)
    {
        // Get user to find their company (needed for vacation query)
        var user = await _db.Users.FindAsync(userId);
        if (user == null) return false;

        // Check for approved time off requests that include this date
        var hasConflict = await _db.TimeOffRequests
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
        var user = await _db.Users.FindAsync(userId);
        if (user == null) return (false, null, null, null);

        var vacation = await _db.TimeOffRequests
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
    /// Create a new chore assignment
    /// </summary>
    public async Task<(bool Success, string Message, Chore? Chore)> CreateChoreAsync(
        int assigneeId,
        DateOnly date,
        string title,
        string? notes = null,
        bool forceAssign = false,
        int? moleculeId = null)
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

            // Validate title
            if (string.IsNullOrWhiteSpace(title))
            {
                return (false, "Chore title is required.", null);
            }

            // Get MoleculeId from the assignee's company if not explicitly provided
            var effectiveMoleculeId = moleculeId;
            if (!effectiveMoleculeId.HasValue)
            {
                var company = await _db.Companies.FindAsync(assignee.CompanyId);
                effectiveMoleculeId = company?.MoleculeId;
            }

            // Create the chore
            var chore = new Chore
            {
                CompanyId = assignee.CompanyId, // Use assignee's company ID for multi-tenant support
                MoleculeId = effectiveMoleculeId, // Nullable - will be null for legacy/unassigned molecules
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

            // Audit log for force-assignments (bypassing vacation conflict)
            if (forceAssign)
            {
                _logger.LogWarning("Chore {ChoreId} was FORCE-ASSIGNED by user {CreatedBy} despite vacation conflict for user {UserId} on {Date}",
                    chore.Id, currentUserId, assigneeId, date);
            }

            return (true, "Chore created successfully.", chore);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Database error creating chore. CompanyId={CompanyId}, CreatedBy={CreatedBy}, AssigneeId={AssigneeId}, Date={Date}, Title={Title}",
                companyId, currentUserId, assigneeId, date, title);
            return (false, "An error occurred while creating the chore.", null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error creating chore. CompanyId={CompanyId}, CreatedBy={CreatedBy}, AssigneeId={AssigneeId}, Date={Date}, Title={Title}",
                companyId, currentUserId, assigneeId, date, title);
            return (false, "An error occurred while creating the chore.", null);
        }
    }

    /// <summary>
    /// Cancel (soft delete) a chore
    /// </summary>
    public async Task<(bool Success, string Message)> CancelChoreAsync(int choreId, string? reason = null)
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

            // IgnoreQueryFilters() allows cross-company chore management for users with appropriate grants
            var chore = await _db.Chores
                .IgnoreQueryFilters()
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

            // ✅ Grant-based: Check if user has AssignChores grant for the chore's company
            var hasGrant = await _grantService.HasGrantForCompanyAsync(currentUserId, "AssignChores", chore.CompanyId);
            if (!hasGrant)
            {
                return (false, "You do not have permission to cancel this chore.");
            }

            // Cancel the chore
            chore.CanceledAt = DateTime.UtcNow;
            chore.CanceledBy = currentUserId;

            await _db.SaveChangesAsync();

            _logger.LogInformation("Chore {ChoreId} canceled by user {CanceledBy}. Reason: {Reason}",
                choreId, currentUserId, reason ?? "None");

            return (true, "Chore canceled successfully.");
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Database error canceling chore. CompanyId={CompanyId}, CanceledBy={CanceledBy}, ChoreId={ChoreId}, Reason={Reason}",
                companyId, currentUserId, choreId, reason ?? "None");
            return (false, "An error occurred while canceling the chore.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error canceling chore. CompanyId={CompanyId}, CanceledBy={CanceledBy}, ChoreId={ChoreId}, Reason={Reason}",
                companyId, currentUserId, choreId, reason ?? "None");
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
        var currentUserId = GetCurrentUserId();
        int? companyId = null;
        using var transaction = await _db.Database.BeginTransactionAsync();
        try
        {
            var currentUser = await GetCurrentUserAsync();
            companyId = currentUser?.CompanyId;

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
        catch (DbUpdateException ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Database error replacing shift with chore. CompanyId={CompanyId}, UserId={UserId}, ShiftAssignmentId={ShiftAssignmentId}, Title={Title}",
                companyId, currentUserId, shiftAssignmentId, title);
            return (false, "An error occurred while replacing the shift with a chore.", null);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Unexpected error replacing shift with chore. CompanyId={CompanyId}, UserId={UserId}, ShiftAssignmentId={ShiftAssignmentId}, Title={Title}",
                companyId, currentUserId, shiftAssignmentId, title);
            return (false, "An error occurred while replacing the shift with a chore.", null);
        }
    }

    /// <summary>
    /// Replace an existing chore with a shift assignment (transactional)
    /// </summary>
    public async Task<(bool Success, string Message)> ReplaceChoreWithShiftAsync(int choreId, int shiftInstanceId)
    {
        var currentUserId = GetCurrentUserId();
        int? companyId = null;
        using var transaction = await _db.Database.BeginTransactionAsync();
        try
        {
            var currentUser = await GetCurrentUserAsync();
            companyId = currentUser?.CompanyId;

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
        catch (DbUpdateException ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Database error replacing chore with shift. CompanyId={CompanyId}, UserId={UserId}, ChoreId={ChoreId}, ShiftInstanceId={ShiftInstanceId}",
                companyId, currentUserId, choreId, shiftInstanceId);
            return (false, "An error occurred while replacing the chore with a shift.");
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Unexpected error replacing chore with shift. CompanyId={CompanyId}, UserId={UserId}, ChoreId={ChoreId}, ShiftInstanceId={ShiftInstanceId}",
                companyId, currentUserId, choreId, shiftInstanceId);
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
        bool? includeCanceled = false,
        int? moleculeId = null)
    {
        var query = _db.Chores
            .Include(c => c.User)
            .Include(c => c.Creator)
            .Include(c => c.Canceler)
            .Include(c => c.Molecule)
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

        if (moleculeId.HasValue)
        {
            query = query.Where(c => c.MoleculeId == moleculeId.Value);
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
