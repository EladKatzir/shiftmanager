using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace ShiftManager.Hubs;

/// <summary>
/// SignalR hub for real-time calendar updates.
/// Clients join groups based on calendar scope (molecule+jobtype, area, company).
/// </summary>
[Authorize]
public class CalendarHub : Hub
{
    private readonly ILogger<CalendarHub> _logger;

    public CalendarHub(ILogger<CalendarHub> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Join a calendar group to receive updates for that scope.
    /// Group names follow pattern: "{calendarType}-{scope}"
    /// Examples: "shifts-5-3" (moleculeId=5, jobTypeId=3), "oncall-2" (areaId=2), "overview-7" (companyId=7)
    /// </summary>
    public async Task JoinCalendarGroup(string groupName)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
        _logger.LogDebug("Connection {ConnectionId} joined group {GroupName}", Context.ConnectionId, groupName);
    }

    /// <summary>
    /// Leave a calendar group (e.g., when switching filters or leaving page).
    /// </summary>
    public async Task LeaveCalendarGroup(string groupName)
    {
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
        var userId = Context.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        _logger.LogDebug("Connection {ConnectionId} connected (User: {UserId})", Context.ConnectionId, userId ?? "unknown");
        await base.OnConnectedAsync();
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
    public static string Shifts(int moleculeId, int jobTypeId) => $"shifts-{moleculeId}-{jobTypeId}";
    public static string Chores(int moleculeId) => $"chores-{moleculeId}";
    public static string OnCall(int areaId) => $"oncall-{areaId}";
    public static string Overview(int companyId) => $"overview-{companyId}";
}
