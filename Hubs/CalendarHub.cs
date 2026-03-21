using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Services;

namespace ShiftManager.Hubs;

/// <summary>
/// SignalR hub for real-time calendar updates.
/// Clients join groups based on calendar scope (molecule+jobtype, area, company).
/// </summary>
[Authorize]
public class CalendarHub : Hub
{
    private readonly ILogger<CalendarHub> _logger;
    private readonly AppDbContext _db;
    private readonly IRateLimitingService _rateLimiter;
    private readonly IGrantService _grantService;

    private const int HubRateLimitMaxAttempts = 30;
    private const int HubRateLimitWindowMinutes = 1;

    public CalendarHub(ILogger<CalendarHub> logger, AppDbContext db, IRateLimitingService rateLimiter, IGrantService grantService)
    {
        _logger = logger;
        _db = db;
        _rateLimiter = rateLimiter;
        _grantService = grantService;
    }

    /// <summary>
    /// Join a calendar group to receive updates for that scope.
    /// Group names follow pattern: "{calendarType}-{scope}"
    /// Examples: "shifts-5-3" (moleculeId=5, jobTypeId=3), "oncall-2" (areaId=2), "overview-7" (companyId=7)
    /// </summary>
    public async Task JoinCalendarGroup(string groupName)
    {
        var userId = GetUserId();
        if (userId == "unknown")
        {
            _logger.LogError("JoinCalendarGroup called without NameIdentifier claim (Connection: {ConnectionId})", Context.ConnectionId);
            return;
        }

        var rateLimitKey = $"hub:{userId}:join";
        if (!_rateLimiter.IsAllowed(rateLimitKey, HubRateLimitMaxAttempts, HubRateLimitWindowMinutes))
        {
            _logger.LogWarning("Rate limit exceeded for JoinCalendarGroup by user {UserId} (Connection: {ConnectionId})", userId, Context.ConnectionId);
            return;
        }

        if (!await ValidateGroupAccessAsync(groupName))
        {
            _logger.LogWarning("Connection {ConnectionId} denied access to group {GroupName}", Context.ConnectionId, groupName);
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
        _logger.LogDebug("Connection {ConnectionId} joined group {GroupName}", Context.ConnectionId, groupName);
    }

    /// <summary>
    /// Leave a calendar group (e.g., when switching filters or leaving page).
    /// </summary>
    public async Task LeaveCalendarGroup(string groupName)
    {
        var userId = GetUserId();
        if (userId == "unknown")
        {
            _logger.LogError("LeaveCalendarGroup called without NameIdentifier claim (Connection: {ConnectionId})", Context.ConnectionId);
            return;
        }

        var rateLimitKey = $"hub:{userId}:leave";
        if (!_rateLimiter.IsAllowed(rateLimitKey, HubRateLimitMaxAttempts, HubRateLimitWindowMinutes))
        {
            _logger.LogWarning("Rate limit exceeded for LeaveCalendarGroup by user {UserId} (Connection: {ConnectionId})", userId, Context.ConnectionId);
            return;
        }

        await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
        _logger.LogDebug("Connection {ConnectionId} left group {GroupName}", Context.ConnectionId, groupName);
    }

    /// <summary>
    /// Called when client disconnects - cleanup is automatic.
    /// </summary>
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (exception != null)
        {
            _logger.LogWarning(exception, "Connection {ConnectionId} disconnected with error", Context.ConnectionId);
        }
        else
        {
            _logger.LogDebug("Connection {ConnectionId} disconnected", Context.ConnectionId);
        }

        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Called when client connects - log for debugging.
    /// </summary>
    public override async Task OnConnectedAsync()
    {
        var userId = GetUserId();
        _logger.LogDebug("Connection {ConnectionId} connected (User: {UserId})", Context.ConnectionId, userId);
        await base.OnConnectedAsync();
    }

    /// <summary>
    /// Extracts the user ID from claims for rate limiting keying.
    /// Returns "unknown" if the claim is not present (should not happen for [Authorize] hub).
    /// </summary>
    private string GetUserId()
    {
        return Context.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "unknown";
    }

    /// <summary>
    /// Validates that the current user is authorized to join the requested group.
    /// Prevents cross-tenant real-time data leaks by checking group scope against user's company.
    /// </summary>
    private async Task<bool> ValidateGroupAccessAsync(string groupName)
    {
        if (string.IsNullOrWhiteSpace(groupName))
            return false;

        var companyIdClaim = Context.User?.FindFirst("CompanyId")?.Value;
        if (string.IsNullOrEmpty(companyIdClaim) || !int.TryParse(companyIdClaim, out var userCompanyId))
            return false;

        var parts = groupName.Split('-');
        if (parts.Length < 2)
            return false;

        var groupType = parts[0];

        switch (groupType)
        {
            case "overview":
                // overview-{companyId} — direct company match
                if (int.TryParse(parts[1], out var overviewCompanyId))
                    return overviewCompanyId == userCompanyId;
                return false;

            case "shifts":
                // shifts-{moleculeId}-{jobTypeId} — validate molecule belongs to user's company
                if (parts.Length >= 3 && int.TryParse(parts[1], out var shiftsMoleculeId))
                {
                    if (await MoleculeBelongsToCompanyAsync(shiftsMoleculeId, userCompanyId))
                        return true;
                    return await DirectorHasMoleculeAccessAsync(shiftsMoleculeId);
                }
                return false;

            case "chores":
                // chores-{moleculeId} — validate molecule belongs to user's company
                if (int.TryParse(parts[1], out var choresMoleculeId))
                {
                    if (await MoleculeBelongsToCompanyAsync(choresMoleculeId, userCompanyId))
                        return true;
                    return await DirectorHasMoleculeAccessAsync(choresMoleculeId);
                }
                return false;

            case "oncall":
                // oncall-{areaId} — validate area is accessible to user's company
                if (int.TryParse(parts[1], out var oncallAreaId))
                {
                    if (await AreaBelongsToCompanyAsync(oncallAreaId, userCompanyId))
                        return true;
                    return await DirectorHasAreaAccessAsync(oncallAreaId);
                }
                return false;

            default:
                _logger.LogWarning("Unknown group type '{GroupType}' in group name '{GroupName}'", groupType, groupName);
                return false;
        }
    }

    /// <summary>
    /// Checks if the given molecule is associated with the user's company.
    /// </summary>
    private async Task<bool> MoleculeBelongsToCompanyAsync(int moleculeId, int companyId)
    {
        // SECURITY-AUDITED: SAFE — validates tenant scope; IgnoreQueryFilters needed to check cross-tenant molecule ownership
        return await _db.Companies.IgnoreQueryFilters()
            .AnyAsync(c => c.Id == companyId && c.MoleculeId == moleculeId);
    }

    /// <summary>
    /// Checks if the given area is associated with the user's company (via molecule).
    /// </summary>
    private async Task<bool> AreaBelongsToCompanyAsync(int areaId, int companyId)
    {
        // SECURITY-AUDITED: SAFE — validates tenant scope; IgnoreQueryFilters needed to check cross-tenant area ownership
        return await _db.Companies.IgnoreQueryFilters()
            .AnyAsync(c => c.Id == companyId && c.Molecule != null && c.Molecule.AreaId == areaId);
    }

    /// <summary>
    /// Fallback: checks if the current user has DirectorHubAccess grant for any company in the given molecule.
    /// Only called when the direct company check fails (most users pass the direct check).
    /// </summary>
    private async Task<bool> DirectorHasMoleculeAccessAsync(int moleculeId)
    {
        var userIdStr = GetUserId();
        if (!int.TryParse(userIdStr, out var userId))
            return false;

        var accessibleCompanyIds = await _grantService.GetAccessibleCompanyIdsForGrantAsync(userId, "DirectorHubAccess");
        if (accessibleCompanyIds.Count == 0)
            return false;

        // SECURITY-AUDITED: SAFE — Directors need cross-company molecule lookup to validate SignalR group access
        var moleculeCompanyIds = await _db.Companies.IgnoreQueryFilters()
            .Where(c => c.MoleculeId == moleculeId)
            .Select(c => c.Id)
            .ToListAsync();

        return accessibleCompanyIds.Intersect(moleculeCompanyIds).Any();
    }

    /// <summary>
    /// Fallback: checks if the current user has DirectorHubAccess grant for any company in the given area.
    /// Only called when the direct company check fails.
    /// </summary>
    private async Task<bool> DirectorHasAreaAccessAsync(int areaId)
    {
        var userIdStr = GetUserId();
        if (!int.TryParse(userIdStr, out var userId))
            return false;

        var accessibleCompanyIds = await _grantService.GetAccessibleCompanyIdsForGrantAsync(userId, "DirectorHubAccess");
        if (accessibleCompanyIds.Count == 0)
            return false;

        // SECURITY-AUDITED: SAFE — Directors need cross-company area lookup to validate SignalR group access
        var areaCompanyIds = await _db.Companies.IgnoreQueryFilters()
            .Where(c => c.Molecule != null && c.Molecule.AreaId == areaId)
            .Select(c => c.Id)
            .ToListAsync();

        return accessibleCompanyIds.Intersect(areaCompanyIds).Any();
    }
}

/// <summary>
/// DTOs for calendar events sent via SignalR.
/// </summary>
public record CalendarAssignmentChangedEvent(
    int ShiftInstanceId,
    int? UserId,
    string? UserDisplayName,
    DateOnly Date,
    int ShiftTypeId,
    string ShiftTypeName,
    string ChangeType // "assigned" | "unassigned"
);

public record CalendarCapacityChangedEvent(
    int ShiftTypeId,
    DateOnly Date,
    int NewCapacity,
    int AssignedCount
);

public record CalendarNoteChangedEvent(
    int UserId,
    DateOnly Date,
    string? Note,
    string ChangeType // "created" | "updated" | "deleted"
);

public record CalendarChoreChangedEvent(
    int ChoreId,
    int? UserId,
    DateOnly Date,
    int ChoreTypeId,
    string ChoreTypeName,
    string ChangeType // "assigned" | "unassigned" | "updated"
);

public record CalendarOnCallChangedEvent(
    int OnDutyId,
    int? PrimaryUserId,
    int? BackupUserId,
    DateOnly Date,
    int OnDutyTypeId,
    string OnDutyTypeName,
    string ChangeType // "assigned" | "unassigned" | "updated"
);

/// <summary>
/// Service for sending calendar updates to connected clients.
/// Inject this into services/controllers that modify calendar data.
/// </summary>
public interface ICalendarNotificationService
{
    Task NotifyAssignmentChangedAsync(string groupName, CalendarAssignmentChangedEvent evt);
    Task NotifyCapacityChangedAsync(string groupName, CalendarCapacityChangedEvent evt);
    Task NotifyNoteChangedAsync(string groupName, CalendarNoteChangedEvent evt);
    Task NotifyChoreChangedAsync(string groupName, CalendarChoreChangedEvent evt);
    Task NotifyOnCallChangedAsync(string groupName, CalendarOnCallChangedEvent evt);
}

public class CalendarNotificationService : ICalendarNotificationService
{
    private readonly IHubContext<CalendarHub> _hubContext;
    private readonly ILogger<CalendarNotificationService> _logger;

    public CalendarNotificationService(
        IHubContext<CalendarHub> hubContext,
        ILogger<CalendarNotificationService> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task NotifyAssignmentChangedAsync(string groupName, CalendarAssignmentChangedEvent evt)
    {
        _logger.LogDebug("Notifying group {GroupName} of assignment change: {Event}", groupName, evt);
        await _hubContext.Clients.Group(groupName).SendAsync("AssignmentChanged", evt);
    }

    public async Task NotifyCapacityChangedAsync(string groupName, CalendarCapacityChangedEvent evt)
    {
        _logger.LogDebug("Notifying group {GroupName} of capacity change: {Event}", groupName, evt);
        await _hubContext.Clients.Group(groupName).SendAsync("CapacityChanged", evt);
    }

    public async Task NotifyNoteChangedAsync(string groupName, CalendarNoteChangedEvent evt)
    {
        _logger.LogDebug("Notifying group {GroupName} of note change: {Event}", groupName, evt);
        await _hubContext.Clients.Group(groupName).SendAsync("NoteChanged", evt);
    }

    public async Task NotifyChoreChangedAsync(string groupName, CalendarChoreChangedEvent evt)
    {
        _logger.LogDebug("Notifying group {GroupName} of chore change: {Event}", groupName, evt);
        await _hubContext.Clients.Group(groupName).SendAsync("ChoreChanged", evt);
    }

    public async Task NotifyOnCallChangedAsync(string groupName, CalendarOnCallChangedEvent evt)
    {
        _logger.LogDebug("Notifying group {GroupName} of on-call change: {Event}", groupName, evt);
        await _hubContext.Clients.Group(groupName).SendAsync("OnCallChanged", evt);
    }
}

/// <summary>
/// Helper class for building calendar group names.
/// </summary>
public static class CalendarGroups
{
    public static string Shifts(int moleculeId, int? jobTypeId) => $"shifts-{moleculeId}-{jobTypeId ?? 0}";
    public static string Chores(int moleculeId) => $"chores-{moleculeId}";
    public static string OnCall(int areaId) => $"oncall-{areaId}";
    public static string Overview(int companyId) => $"overview-{companyId}";
}
