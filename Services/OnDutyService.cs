using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Data.SeedData;
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

    /// <summary>
    /// Check if a duty type requires officer rank.
    /// </summary>
    Task<bool> RequiresOfficerForDutyTypeAsync(OnDutyType type);

    /// <summary>
    /// Check if a duty type value is valid (either a built-in enum or a custom OnDutyTypeConfig).
    /// </summary>
    Task<bool> IsValidDutyTypeAsync(int typeValue);
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
// SECURITY-AUDITED: All IgnoreQueryFilters() in this class are SAFE — OnDuty is cross-company by design;
// lookups scoped by explicit userId/date/type parameters; called only from authorized endpoints
public class OnDutyService : IOnDutyService
{
    private readonly AppDbContext _db;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IDirectorService _directorService;
    private readonly IGrantService _grantService;
    private readonly ILogger<OnDutyService> _logger;
    private readonly IFeatureFlagService _featureFlagService;

    public OnDutyService(
        AppDbContext db,
        IHttpContextAccessor httpContextAccessor,
        IDirectorService directorService,
        IGrantService grantService,
        ILogger<OnDutyService> logger,
        IFeatureFlagService featureFlagService)
    {
        _db = db;
        _httpContextAccessor = httpContextAccessor;
        _directorService = directorService;
        _grantService = grantService;
        _logger = logger;
        _featureFlagService = featureFlagService;
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
        // SECURITY-AUDITED: SAFE — scoped by authenticated userId from claims; returns only current user's record
        return await _db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == userId);
    }

    /// <summary>
    /// Check if current user can manage on-duty assignments.
    /// ✅ Grant-based: Uses AssignHakamDuties or AssignKatzinDuties grants instead of role checks.
    /// Note: Assigner role does not receive these grants by default.
    /// </summary>
    public async Task<bool> CanUserManageOnDutyAsync(int userId)
    {
        // Any of these grants authorizes on-call editing. ManageOnDuty was previously
        // omitted (bug fix 2026-04-15). EditOnCallCalendar (2026-04-15) opens the on-call
        // calendar to all hakam-eligible users for collaborative edits.
        return await _grantService.HasGrantAsync(userId, "AssignHakamDuties")
            || await _grantService.HasGrantAsync(userId, "AssignKatzinDuties")
            || await _grantService.HasGrantAsync(userId, "ManageOnDuty")
            || await _grantService.HasGrantAsync(userId, "EditOnCallCalendar");
    }

    /// <summary>
    /// Get list of users eligible for on-duty assignment.
    /// ✅ Grant-based: Uses AssignHakamDuties/AssignKatzinDuties grant scopes to determine visible users.
    /// Directors can be assigned OnDuty (unlike Chores).
    /// </summary>
    public async Task<List<AppUser>> GetEligibleAssigneesAsync()
    {
        var currentUserId = GetCurrentUserId();
        if (currentUserId <= 0)
        {
            return new List<AppUser>();
        }

        // Get accessible company IDs based on duty assignment grant scopes
        var hakamCompanyIds = await _grantService.GetAccessibleCompanyIdsForGrantAsync(currentUserId, "AssignHakamDuties");
        var katzinCompanyIds = await _grantService.GetAccessibleCompanyIdsForGrantAsync(currentUserId, "AssignKatzinDuties");

        // Combine accessible company IDs from both grants
        var accessibleCompanyIds = hakamCompanyIds.Union(katzinCompanyIds).Distinct().ToList();

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
    /// Check if user has an active on-duty assignment on a specific date and type
    /// </summary>
    public async Task<bool> HasActiveOnDutyOnDateAsync(int userId, DateOnly date, OnDutyType type)
    {
        // OnDuty is global - must use IgnoreQueryFilters
        // SECURITY-AUDITED: SAFE — OnDuty is global by design; scoped by userId + date + type
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
        // SECURITY-AUDITED: SAFE — scoped by specific userId parameter; no data exposure
        var user = await _db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null) return false;

        // Check for approved time off requests that include this date
        // Vacation logic: StartDate 00:00 to EndDate+1 13:00
        // After logic: StartDate 16:00 to StartDate+1 13:00
        // SECURITY-AUDITED: SAFE — scoped by userId + companyId + date; returns boolean only
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
        // SECURITY-AUDITED: SAFE — scoped by specific userId parameter; no data exposure
        var user = await _db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null) return (false, null, null, null);

        // SECURITY-AUDITED: SAFE — scoped by userId + companyId + date; returns only conflict dates/type
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

            // ✅ Grant-based: Validate user has the SPECIFIC grant for this duty type
            var requiredGrant = type == OnDutyType.Lead ? "AssignKatzinDuties" : "AssignHakamDuties";
            var hasTypeSpecificGrant = await _grantService.HasGrantAsync(currentUserId, requiredGrant);
            if (!hasTypeSpecificGrant)
            {
                var dutyTypeName = type == OnDutyType.Lead ? "Katzin" : "Hakam";
                return (false, $"You do not have permission to assign {dutyTypeName} duties.", null);
            }

            // Check if duty type requires officer rank (only enforce when feature flag is enabled)
            var enforceRankEligibility = await _featureFlagService.IsEnabledAsync(FeatureFlagSeed.Flags.EnforceRankEligibility);
            if (enforceRankEligibility)
            {
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
            }

            // Get assignee (must use IgnoreQueryFilters for cross-company access)
            // SECURITY-AUDITED: SAFE — scoped by specific assigneeId; grant check performed above
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
            // SECURITY-AUDITED: SAFE — OnDuty is global by design; scoped by specific onDutyId; grant check follows
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

            // ✅ Grant-based: Check if user has AssignHakamDuties or AssignKatzinDuties grant for the assignee's company
            if (onDuty.User != null)
            {
                // 2026-04-16: include ManageOnDuty + EditOnCallCalendar in the cancel-auth check.
                // Without these, holders of those grants pass the broad CanUserManageOnDutyAsync gate
                // in DeleteOnDuty.cshtml.cs but get rejected here — contradicting the collaborative
                // editing design.
                var hasHakamGrant   = await _grantService.HasGrantForCompanyAsync(currentUserId, "AssignHakamDuties", onDuty.User.CompanyId);
                var hasKatzinGrant  = await _grantService.HasGrantForCompanyAsync(currentUserId, "AssignKatzinDuties", onDuty.User.CompanyId);
                var hasManageGrant  = await _grantService.HasGrantAsync(currentUserId, "ManageOnDuty");
                var hasEditOnCallGrant = await _grantService.HasGrantAsync(currentUserId, "EditOnCallCalendar");
                if (!hasHakamGrant && !hasKatzinGrant && !hasManageGrant && !hasEditOnCallGrant)
                {
                    return (false, "You do not have permission to cancel this on-duty assignment.");
                }
            }
            else
            {
                // If no user is assigned, just check if they have the general grant
                if (!await CanUserManageOnDutyAsync(currentUserId))
                {
                    return (false, "You do not have permission to cancel on-duty assignments.");
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
        // SECURITY-AUDITED: SAFE — OnDuty is global by design; re-filtered by date/userId/type parameters
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
        // SECURITY-AUDITED: SAFE — OnDuty is global by design; scoped by specific onDutyId
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
        // SECURITY-AUDITED: SAFE — OnDuty is global/cross-company by design; filtered by IsActive and optional rank
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
    public async Task<bool> RequiresOfficerForDutyTypeAsync(OnDutyType type)
    {
        // Built-in Lead type (Katzin) requires officer
        if (type == OnDutyType.Lead)
            return true;

        // Check custom types
        var customConfig = await _db.OnDutyTypeConfigs
            .FirstOrDefaultAsync(c => c.TypeValue == (int)type && c.IsActive);

        return customConfig?.RequiresOfficerRank ?? false;
    }

    public async Task<bool> IsValidDutyTypeAsync(int typeValue)
    {
        // Built-in types are always valid
        if (Enum.IsDefined(typeof(OnDutyType), typeValue))
            return true;

        // Check custom types in OnDutyTypeConfig
        return await _db.OnDutyTypeConfigs.AnyAsync(c => c.TypeValue == typeValue && c.IsActive);
    }

    /// <summary>
    /// Check if a specific user is eligible for a duty type.
    /// </summary>
    public async Task<bool> IsUserEligibleForDutyAsync(int userId, OnDutyType dutyType, bool requireOfficer = false)
    {
        // SECURITY-AUDITED: SAFE — scoped by specific userId; returns boolean eligibility check only
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
