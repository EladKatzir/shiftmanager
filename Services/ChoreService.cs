using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;

namespace ShiftManager.Services;

public interface IChoreService
{
    Task<(bool Success, string Message, Chore? Chore)> CreateChoreAsync(int assigneeId, DateOnly date, string title, string? notes = null, bool forceAssign = false, int? moleculeId = null, int? choreTypeId = null);
    Task<(bool Success, string Message)> CancelChoreAsync(int choreId, string? reason = null);
    Task<(bool Success, string Message)> RestoreChoreAsync(int choreId);
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

// SECURITY-AUDITED: IgnoreQueryFilters() in this class is used in three distinct, intentional ways:
//   1) Chore queries scoped by explicit moleculeId/userId parameters (molecule-scoped by design;
//      tenant filter would hide chores in sibling companies under the same molecule).
//   2) Cross-tenant user lookups (assignee, current user) followed by HasGrantForCompanyAsync —
//      the grant check is the access boundary, not the tenant filter.
//   3) Cross-tenant chore lookups (CancelChoreAsync, RestoreChoreAsync, ReplaceChoreWithShiftAsync,
//      GetChoreByIdAsync) where each call site re-authorizes via HasGrantForCompanyAsync before
//      mutating state.
public class ChoreService : IChoreService
{
    private readonly AppDbContext _db;
    private readonly ITenantResolver _tenantResolver;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IDirectorService _directorService;
    private readonly IGrantService _grantService;
    private readonly ILogger<ChoreService> _logger;

    private readonly ICompanyCacheService _companyCacheService;

    public ChoreService(
        AppDbContext db,
        ITenantResolver tenantResolver,
        IHttpContextAccessor httpContextAccessor,
        IDirectorService directorService,
        IGrantService grantService,
        ILogger<ChoreService> logger,
        ICompanyCacheService companyCacheService)
    {
        _db = db;
        _tenantResolver = tenantResolver;
        _httpContextAccessor = httpContextAccessor;
        _directorService = directorService;
        _grantService = grantService;
        _logger = logger;
        _companyCacheService = companyCacheService;
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
        // SECURITY-AUDITED: SAFE — looks up the caller themselves; bypassing the tenant filter
        // ensures a momentarily mismatched tenant context (e.g., during context switching for
        // Directors) cannot make the caller invisible to themselves.
        return await _db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == userId);
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
    /// Grant-based: Uses AssignChores grant with company scope. Authorization is the only gate;
    /// any user role (including Director) can be the recipient if the caller has the grant.
    /// </summary>
    public async Task<bool> CanUserManageChoreForAssigneeAsync(int managerId, int assigneeId)
    {
        // SECURITY-AUDITED: SAFE — grant-based authorization is the real boundary; the tenant
        // query filter would block cross-tenant assignees before the grant check runs, locking
        // out Owners/Directors who legitimately have project- or area-wide AssignChores grants.
        var assignee = await _db.Users.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == assigneeId);

        if (assignee == null) return false;

        // Check if manager has AssignChores grant for the assignee's company
        return await _grantService.HasGrantForCompanyAsync(managerId, "AssignChores", assignee.CompanyId);
    }

    /// <summary>
    /// Get list of users eligible for chore assignment.
    /// Grant-based: Uses AssignChores grant scope to determine visible users.
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
        // SECURITY-AUDITED: SAFE — re-scoped by grant-derived accessibleCompanyIds
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
        // Get user to find their company (needed for vacation query).
        // SECURITY-AUDITED: SAFE — caller is already authorized for this assigneeId via grant
        // check; tenant filter would silently skip vacation conflicts for cross-tenant assignees.
        var user = await _db.Users.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == userId);
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
        // SECURITY-AUDITED: SAFE — see HasVacationConflictAsync; cross-tenant lookup needed so
        // vacation conflicts are surfaced to authorized cross-company chore assigners.
        var user = await _db.Users.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == userId);
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
        int? moleculeId = null,
        int? choreTypeId = null)
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

            // Get assignee — bypass tenant filter; per-assignee authorization happens below.
            // SECURITY-AUDITED: SAFE — CanUserManageChoreForAssigneeAsync is the access gate.
            var assignee = await _db.Users.IgnoreQueryFilters()
                .FirstOrDefaultAsync(u => u.Id == assigneeId);
            if (assignee == null)
            {
                return (false, "Assignee not found.", null);
            }

            // Check if assignee is eligible (not a Director, etc.)
            if (!await CanUserManageChoreForAssigneeAsync(currentUserId, assigneeId))
            {
                return (false, "You cannot assign chores to this user.", null);
            }

            // Validate title early (before transaction)
            if (string.IsNullOrWhiteSpace(title))
            {
                return (false, "Chore title is required.", null);
            }

            // COLLISION RULE: Check for vacation conflict (unless force-assigning)
            if (!forceAssign)
            {
                var (hasConflict, vacationStart, vacationEnd, vacationType) = await GetVacationConflictDetailsAsync(assigneeId, date);
                if (hasConflict)
                {
                    return (false, $"VACATION_CONFLICT|{vacationStart}|{vacationEnd}|{vacationType}", null);
                }
            }

            // Get MoleculeId from the assignee's company if not explicitly provided
            var effectiveMoleculeId = moleculeId;
            if (!effectiveMoleculeId.HasValue)
            {
                var company = await _companyCacheService.GetCompanyAsync(assignee.CompanyId);
                effectiveMoleculeId = company?.MoleculeId;
            }

            // Atomic mutual exclusion: wrap chore+shift checks and insert in single transaction (fixes A-03)
            // SQLite serializes writes, so this ensures the check-then-insert is atomic
            using var transaction = await _db.Database.BeginTransactionAsync();
            Chore chore;
            try
            {
                // Check if assignee already has an active chore on this date (inside transaction)
                if (await HasActiveChoreOnDateAsync(assigneeId, date))
                {
                    await transaction.RollbackAsync();
                    return (false, "This user already has an active chore on this date.", null);
                }

                // Check if assignee has a shift on this date (mutual exclusion with shifts)
                if (await HasShiftOnDateAsync(assigneeId, date))
                {
                    await transaction.RollbackAsync();
                    return (false, "SHIFT_CONFLICT", null);
                }

                chore = new Chore
                {
                    CompanyId = assignee.CompanyId,
                    MoleculeId = effectiveMoleculeId,
                    ChoreTypeId = choreTypeId,
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
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Failed to atomically create chore for user {UserId} on {Date}", assigneeId, date);
                return (false, "Failed to create chore - please try again.", null);
            }

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
            // SECURITY-AUDITED: SAFE — scoped by specific choreId; grant check for chore's company follows below
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

    public async Task<(bool Success, string Message)> RestoreChoreAsync(int choreId)
    {
        var currentUserId = GetCurrentUserId();
        try
        {
            var chore = await _db.Chores
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(c => c.Id == choreId);

            if (chore == null)
                return (false, "Chore not found.");

            if (chore.CanceledAt == null)
                return (false, "Chore is not canceled.");

            // Only allow restore within 60 seconds of cancellation
            if ((DateTime.UtcNow - chore.CanceledAt.Value).TotalSeconds > 60)
                return (false, "Undo window has expired.");

            var hasGrant = await _grantService.HasGrantForCompanyAsync(currentUserId, "AssignChores", chore.CompanyId);
            if (!hasGrant)
                return (false, "You do not have permission to restore this chore.");

            chore.CanceledAt = null;
            chore.CanceledBy = null;
            await _db.SaveChangesAsync();

            _logger.LogInformation("Chore {ChoreId} restored (undo) by user {UserId}", choreId, currentUserId);
            return (true, "Chore restored successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error restoring chore {ChoreId}", choreId);
            return (false, "An error occurred while restoring the chore.");
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

            if (shiftAssignment.UserId == null)
            {
                return (false, "Shift assignment has no assigned user.", null);
            }
            var assigneeId = shiftAssignment.UserId.Value;
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

            // Create the chore — bypass tenant filter; the assignee may legitimately belong to a
            // different company than the caller. Authorization above (CanUserManageChoreForAssigneeAsync)
            // already validated the caller has AssignChores for the assignee's company.
            // SECURITY-AUDITED: SAFE — looked up after the grant gate runs.
            var assignee = await _db.Users.IgnoreQueryFilters()
                .FirstOrDefaultAsync(u => u.Id == assigneeId);
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

            // Get the chore — bypass tenant filter so cross-tenant chores are reachable.
            // SECURITY-AUDITED: SAFE — grant check below is the access gate (mirrors CancelChoreAsync).
            var chore = await _db.Chores.IgnoreQueryFilters()
                .FirstOrDefaultAsync(c => c.Id == choreId);
            if (chore == null)
            {
                return (false, "Chore not found.");
            }

            // ✅ Grant-based: Check if user has AssignChores grant for the chore's company.
            // Without this, any caller with a chore id could replace any chore in any tenant (IDOR).
            var hasGrant = await _grantService.HasGrantForCompanyAsync(currentUserId, "AssignChores", chore.CompanyId);
            if (!hasGrant)
            {
                return (false, "You do not have permission to replace this chore.");
            }

            // Verify the target shift instance belongs to the chore's company so we don't
            // smuggle a cross-tenant FK reference (a ShiftAssignment row whose CompanyId
            // disagrees with its ShiftInstance.CompanyId would be a tenancy invariant break).
            // SECURITY-AUDITED: SAFE — read-only validation before persistence.
            var shiftInstance = await _db.ShiftInstances.IgnoreQueryFilters()
                .FirstOrDefaultAsync(si => si.Id == shiftInstanceId);
            if (shiftInstance == null)
            {
                return (false, "Shift instance not found.");
            }
            if (shiftInstance.CompanyId != chore.CompanyId)
            {
                return (false, "Cannot replace a chore with a shift in a different company.");
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
            .Include(c => c.ChoreType)
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
    /// Get a chore by ID. Bypasses the tenant query filter so cross-tenant chores are visible
    /// to authorized callers (Owner/Director/AreaAdmin acting on grants that span companies).
    /// </summary>
    public async Task<Chore?> GetChoreByIdAsync(int choreId)
    {
        // SECURITY-AUDITED: SAFE — read-only lookup. Callers that act on the result must
        // re-authorize via HasGrantForCompanyAsync (CancelChoreAsync / RestoreChoreAsync /
        // ReplaceChoreWithShiftAsync all do this internally before mutating state).
        return await _db.Chores.IgnoreQueryFilters()
            .Include(c => c.User)
            .Include(c => c.Creator)
            .Include(c => c.Canceler)
            .FirstOrDefaultAsync(c => c.Id == choreId);
    }
}
