using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Configuration;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Resources;

namespace ShiftManager.Services;

public interface INotificationService
{
    Task<bool> CreateNotificationAsync(int userId, NotificationType type, string title, string message, int? relatedEntityId = null, string? relatedEntityType = null);
    Task CreateShiftAddedNotificationAsync(int userId, string shiftTypeName, DateOnly shiftDate, TimeOnly startTime, TimeOnly endTime);
    Task CreateShiftRemovedNotificationAsync(int userId, string shiftTypeName, DateOnly shiftDate, TimeOnly startTime, TimeOnly endTime);
    Task CreateTimeOffNotificationAsync(int userId, RequestStatus status, DateOnly startDate, DateOnly endDate, int requestId);
    Task CreateSwapRequestNotificationAsync(int userId, RequestStatus status, string shiftInfo, int requestId);
    Task CreateChoreAssignedNotificationAsync(int userId, string choreTitle, DateOnly choreDate, int choreId);
    Task CreateChoreCanceledNotificationAsync(int userId, string choreTitle, DateOnly choreDate, int choreId);
    Task CreateOnDutyAssignedNotificationAsync(int userId, OnDutyType onDutyType, DateOnly onDutyDate, int onDutyId);
    Task CreateOnDutyCanceledNotificationAsync(int userId, OnDutyType onDutyType, DateOnly onDutyDate, int onDutyId);
    Task CreateTimeOffDeletedNotificationAsync(int userId, DateOnly startDate, DateOnly endDate);

    // Access Request Notifications
    Task NotifyOwnersOfAccessRequestAsync(string requesterName, string requesterEmail, string companyName, int requestId);
    Task CreateAccessRequestApprovedNotificationAsync(int userId, string companyName, string assignedRole);

    // Phase 6: Daily Digest Methods
    Task<List<int>> GetUsersForDailyDigestAsync(TimeOnly currentTime, int companyId);
    Task<bool> SendDailyDigestAsync(int userId, int companyId);

    // Phase 6 Extension: Day-Before Reminders
    Task SendDayBeforeRemindersAsync(int userId, int companyId);

    // Ops Console Scheduler: Trainee Notifications
    Task CreateTraineeAddedNotificationAsync(int primaryUserId, int traineeUserId, string traineeName, string shiftTypeName, DateOnly date, TimeOnly start, TimeOnly end);
    Task CreateTraineeChangedNotificationAsync(int primaryUserId, int oldTraineeUserId, int newTraineeUserId, string oldTraineeName, string newTraineeName, string shiftTypeName, DateOnly date);
    Task CreateTraineeRemovedNotificationAsync(int primaryUserId, int traineeUserId, string traineeName, string shiftTypeName, DateOnly date);

    // Ops Console Scheduler: Staffing Change Notifications
    Task CreateSlotRemovedNotificationAsync(int affectedUserId, string shiftTypeName, DateOnly date, TimeOnly start, TimeOnly end, string reason);

    // Ops Console Scheduler: Shift Modification Notifications
    Task CreateShiftModifiedNotificationAsync(List<int> assignedUserIds, string shiftTypeName, DateOnly date, string changeDescription);
}

public class NotificationService : INotificationService
{
    private readonly AppDbContext _db;
    private readonly ILogger<NotificationService> _logger;
    private readonly ITenantResolver _tenantResolver;
    private readonly IMailService _mailService;
    private readonly IStringLocalizer<SharedResources> _localizer;
    private readonly IConfiguration _configuration;

    public NotificationService(AppDbContext db, ILogger<NotificationService> logger, ITenantResolver tenantResolver, IMailService mailService, IStringLocalizer<SharedResources> localizer, IConfiguration configuration)
    {
        _db = db;
        _logger = logger;
        _tenantResolver = tenantResolver;
        _mailService = mailService;
        _localizer = localizer;
        _configuration = configuration;
    }

    public async Task<bool> CreateNotificationAsync(int userId, NotificationType type, string title, string message, int? relatedEntityId = null, string? relatedEntityType = null)
    {
        try
        {
            var companyId = _tenantResolver.GetCurrentTenantId(); // Multitenancy Phase 2
            var notification = new UserNotification
            {
                CompanyId = companyId,
                UserId = userId,
                Type = type,
                Title = title,
                Message = message,
                IsRead = false,
                CreatedAt = DateTime.UtcNow,
                RelatedEntityId = relatedEntityId,
                RelatedEntityType = relatedEntityType
            };

            _db.UserNotifications.Add(notification);
            await _db.SaveChangesAsync();

            _logger.LogInformation("Created notification {Type} for user {UserId}: {Title}", type, userId, title);
            return true;
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Database error creating notification. CompanyId={CompanyId}, Type={Type}, UserId={UserId}, Title={Title}",
                _tenantResolver.GetCurrentTenantId(), type, userId, title);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error creating notification. CompanyId={CompanyId}, Type={Type}, UserId={UserId}, Title={Title}",
                _tenantResolver.GetCurrentTenantId(), type, userId, title);
            return false;
        }
    }

    public async Task CreateShiftAddedNotificationAsync(int userId, string shiftTypeName, DateOnly shiftDate, TimeOnly startTime, TimeOnly endTime)
    {
        var title = _localizer["NotificationShiftAddedTitle"];
        var message = string.Format(_localizer["NotificationShiftAddedMessage"],
            shiftTypeName,
            shiftDate.ToString("MMM dd, yyyy"),
            startTime.ToString("HH:mm"),
            endTime.ToString("HH:mm"));

        await CreateNotificationAsync(userId, NotificationType.ShiftAdded, title, message, null, "ShiftAssignment");

        // Send email notification
        try
        {
            var user = await _db.Users.FindAsync(userId);
            if (user != null && !string.IsNullOrWhiteSpace(user.Email))
            {
                await _mailService.SendShiftAssignedEmailAsync(
                    user.Email,
                    user.DisplayName,
                    shiftTypeName,
                    shiftDate,
                    startTime,
                    endTime);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending shift assigned email to user {UserId}", userId);
            // Don't throw - email failure should not block notification creation
        }
    }

    public async Task CreateShiftRemovedNotificationAsync(int userId, string shiftTypeName, DateOnly shiftDate, TimeOnly startTime, TimeOnly endTime)
    {
        var title = _localizer["NotificationShiftRemovedTitle"];
        var message = string.Format(_localizer["NotificationShiftRemovedMessage"],
            shiftTypeName,
            shiftDate.ToString("MMM dd, yyyy"),
            startTime.ToString("HH:mm"),
            endTime.ToString("HH:mm"));

        await CreateNotificationAsync(userId, NotificationType.ShiftRemoved, title, message, null, "ShiftAssignment");

        // Send email notification
        try
        {
            var user = await _db.Users.FindAsync(userId);
            if (user != null && !string.IsNullOrWhiteSpace(user.Email))
            {
                await _mailService.SendShiftDeletedEmailAsync(
                    user.Email,
                    user.DisplayName,
                    shiftTypeName,
                    shiftDate,
                    startTime,
                    endTime);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending shift removed email to user {UserId}", userId);
            // Don't throw - email failure should not block notification creation
        }
    }

    public async Task CreateTimeOffNotificationAsync(int userId, RequestStatus status, DateOnly startDate, DateOnly endDate, int requestId)
    {
        var title = status == RequestStatus.Approved
            ? _localizer["NotificationTimeOffApprovedTitle"]
            : _localizer["NotificationTimeOffDeclinedTitle"];

        var dateRange = startDate == endDate
            ? startDate.ToString("MMM dd, yyyy")
            : $"{startDate:MMM dd} - {endDate:MMM dd, yyyy}";

        var message = status == RequestStatus.Approved
            ? string.Format(_localizer["NotificationTimeOffApprovedMessage"], dateRange)
            : string.Format(_localizer["NotificationTimeOffDeclinedMessage"], dateRange);

        var notificationType = status == RequestStatus.Approved ? NotificationType.TimeOffApproved : NotificationType.TimeOffDeclined;

        await CreateNotificationAsync(userId, notificationType, title, message, requestId, "TimeOffRequest");

        // Send email notification
        try
        {
            var user = await _db.Users.FindAsync(userId);
            if (user != null && !string.IsNullOrWhiteSpace(user.Email))
            {
                if (status == RequestStatus.Approved)
                {
                    await _mailService.SendTimeOffApprovedEmailAsync(
                        user.Email,
                        user.DisplayName,
                        startDate,
                        endDate);
                }
                else
                {
                    await _mailService.SendTimeOffDeclinedEmailAsync(
                        user.Email,
                        user.DisplayName,
                        startDate,
                        endDate);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending time-off {Status} email to user {UserId}", status, userId);
            // Don't throw - email failure should not block notification creation
        }
    }

    public async Task CreateSwapRequestNotificationAsync(int userId, RequestStatus status, string shiftInfo, int requestId)
    {
        var title = status == RequestStatus.Approved
            ? _localizer["NotificationSwapRequestApprovedTitle"]
            : _localizer["NotificationSwapRequestDeclinedTitle"];

        var message = status == RequestStatus.Approved
            ? string.Format(_localizer["NotificationSwapRequestApprovedMessage"], shiftInfo)
            : string.Format(_localizer["NotificationSwapRequestDeclinedMessage"], shiftInfo);

        var notificationType = status == RequestStatus.Approved ? NotificationType.SwapRequestApproved : NotificationType.SwapRequestDeclined;

        await CreateNotificationAsync(userId, notificationType, title, message, requestId, "SwapRequest");

        // Send email notification
        try
        {
            var user = await _db.Users.FindAsync(userId);
            if (user != null && !string.IsNullOrWhiteSpace(user.Email))
            {
                if (status == RequestStatus.Approved)
                {
                    await _mailService.SendSwapRequestApprovedEmailAsync(
                        user.Email,
                        user.DisplayName,
                        shiftInfo);
                }
                else
                {
                    await _mailService.SendSwapRequestDeclinedEmailAsync(
                        user.Email,
                        user.DisplayName,
                        shiftInfo);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending swap request {Status} email to user {UserId}", status, userId);
            // Don't throw - email failure should not block notification creation
        }
    }

    public async Task CreateChoreAssignedNotificationAsync(int userId, string choreTitle, DateOnly choreDate, int choreId)
    {
        var title = _localizer["NotificationChoreAssignedTitle"];
        var message = string.Format(_localizer["NotificationChoreAssignedMessage"],
            choreTitle,
            choreDate.ToString("MMM dd, yyyy"));

        await CreateNotificationAsync(userId, NotificationType.ChoreAssigned, title, message, choreId, "Chore");

        // Send email notification
        try
        {
            var user = await _db.Users.FindAsync(userId);
            if (user != null && !string.IsNullOrWhiteSpace(user.Email))
            {
                await _mailService.SendChoreAssignedEmailAsync(
                    user.Email,
                    user.DisplayName,
                    choreTitle,
                    choreDate);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending chore assigned email to user {UserId}", userId);
            // Don't throw - email failure should not block notification creation
        }
    }

    public async Task CreateChoreCanceledNotificationAsync(int userId, string choreTitle, DateOnly choreDate, int choreId)
    {
        var title = _localizer["NotificationChoreCanceledTitle"];
        var message = string.Format(_localizer["NotificationChoreCanceledMessage"],
            choreTitle,
            choreDate.ToString("MMM dd, yyyy"));

        await CreateNotificationAsync(userId, NotificationType.ChoreCanceled, title, message, choreId, "Chore");

        // Send email notification
        try
        {
            var user = await _db.Users.FindAsync(userId);
            if (user != null && !string.IsNullOrWhiteSpace(user.Email))
            {
                await _mailService.SendChoreCanceledEmailAsync(
                    user.Email,
                    user.DisplayName,
                    choreTitle,
                    choreDate);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending chore canceled email to user {UserId}", userId);
            // Don't throw - email failure should not block notification creation
        }
    }

    public async Task CreateOnDutyAssignedNotificationAsync(int userId, OnDutyType onDutyType, DateOnly onDutyDate, int onDutyId)
    {
        var onDutyTypeName = onDutyType == OnDutyType.Hakam
            ? _localizer["OnDutyTypeHakam"]
            : _localizer["OnDutyTypeLead"];

        var title = _localizer["NotificationOnDutyAssignedTitle"];
        var message = string.Format(_localizer["NotificationOnDutyAssignedMessage"],
            onDutyTypeName,
            onDutyDate.ToString("MMM dd, yyyy"));

        await CreateNotificationAsync(userId, NotificationType.OnDutyAssigned, title, message, onDutyId, "OnDuty");

        // Send email notification
        try
        {
            var user = await _db.Users.FindAsync(userId);
            if (user != null && !string.IsNullOrWhiteSpace(user.Email))
            {
                await _mailService.SendOnDutyAssignedEmailAsync(
                    user.Email,
                    user.DisplayName,
                    onDutyTypeName,
                    onDutyDate);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending on-duty assigned email to user {UserId}", userId);
            // Don't throw - email failure should not block notification creation
        }
    }

    public async Task CreateOnDutyCanceledNotificationAsync(int userId, OnDutyType onDutyType, DateOnly onDutyDate, int onDutyId)
    {
        var onDutyTypeName = onDutyType == OnDutyType.Hakam
            ? _localizer["OnDutyTypeHakam"]
            : _localizer["OnDutyTypeLead"];

        var title = _localizer["NotificationOnDutyCanceledTitle"];
        var message = string.Format(_localizer["NotificationOnDutyCanceledMessage"],
            onDutyTypeName,
            onDutyDate.ToString("MMM dd, yyyy"));

        await CreateNotificationAsync(userId, NotificationType.OnDutyCanceled, title, message, onDutyId, "OnDuty");

        // Send email notification
        try
        {
            var user = await _db.Users.FindAsync(userId);
            if (user != null && !string.IsNullOrWhiteSpace(user.Email))
            {
                await _mailService.SendOnDutyCanceledEmailAsync(
                    user.Email,
                    user.DisplayName,
                    onDutyTypeName,
                    onDutyDate);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending on-duty canceled email to user {UserId}", userId);
            // Don't throw - email failure should not block notification creation
        }
    }

    public async Task CreateTimeOffDeletedNotificationAsync(int userId, DateOnly startDate, DateOnly endDate)
    {
        var title = _localizer["NotificationTimeOffDeletedTitle"];
        var dateRange = startDate == endDate
            ? startDate.ToString("MMM dd, yyyy")
            : $"{startDate:MMM dd} - {endDate:MMM dd, yyyy}";
        var message = string.Format(_localizer["NotificationTimeOffDeletedMessage"], dateRange);

        await CreateNotificationAsync(userId, NotificationType.TimeOffDeleted, title, message, null, "TimeOffRequest");

        // Send email notification
        try
        {
            var user = await _db.Users.FindAsync(userId);
            if (user != null && !string.IsNullOrWhiteSpace(user.Email))
            {
                await _mailService.SendTimeOffDeletedEmailAsync(
                    user.Email,
                    user.DisplayName,
                    startDate,
                    endDate);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending time-off deleted email to user {UserId}", userId);
            // Don't throw - email failure should not block notification creation
        }
    }

    // ========================================
    // Access Request Notification Methods
    // ========================================

    /// <summary>
    /// Notify all owner users about a new access request and send them emails.
    /// </summary>
    public async Task NotifyOwnersOfAccessRequestAsync(string requesterName, string requesterEmail, string companyName, int requestId)
    {
        try
        {
            // Get all owner users
            var owners = await _db.Users
                .Where(u => u.Role == UserRole.Owner && u.IsActive)
                .ToListAsync();

            if (!owners.Any())
            {
                _logger.LogWarning("No owner users found to notify about access request {RequestId}", requestId);
                return;
            }

            var title = _localizer["NotificationAccessRequestSubmittedTitle"];
            var message = string.Format(_localizer["NotificationAccessRequestSubmittedMessage"],
                requesterName,
                requesterEmail,
                companyName);

            // Create in-app notification for each owner
            foreach (var owner in owners)
            {
                await CreateNotificationAsync(
                    owner.Id,
                    NotificationType.AccessRequestSubmitted,
                    title,
                    message,
                    requestId,
                    "UserJoinRequest");
            }

            // Send email to each owner
            foreach (var owner in owners)
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(owner.Email))
                    {
                        await _mailService.SendAccessRequestSubmittedEmailAsync(
                            owner.Email,
                            owner.DisplayName,
                            requesterName,
                            requesterEmail,
                            companyName);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error sending access request email to owner {UserId}", owner.Id);
                    // Don't throw - continue notifying other owners
                }
            }

            _logger.LogInformation("Notified {OwnerCount} owners about access request {RequestId} from {RequesterEmail}",
                owners.Count, requestId, requesterEmail);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error notifying owners about access request {RequestId}", requestId);
            // Don't throw - access request was still created successfully
        }
    }

    /// <summary>
    /// Create notification for user when their access request is approved.
    /// </summary>
    public async Task CreateAccessRequestApprovedNotificationAsync(int userId, string companyName, string assignedRole)
    {
        var title = _localizer["NotificationAccessRequestApprovedTitle"];
        var message = string.Format(_localizer["NotificationAccessRequestApprovedMessage"],
            companyName,
            assignedRole);

        await CreateNotificationAsync(userId, NotificationType.AccessRequestApproved, title, message, null, "User");

        // Note: Email is already sent by Admin/Users.cshtml.cs using SendAccountApprovedEmailAsync
    }

    // ========================================
    // Phase 6: Daily Digest Methods
    // ========================================

    /// <summary>
    /// Get list of user IDs who should receive daily digest at the current time.
    /// Matches users whose preferred time is within ±15 minutes of currentTime.
    /// </summary>
    public async Task<List<int>> GetUsersForDailyDigestAsync(TimeOnly currentTime, int companyId)
    {
        try
        {
            // Allow 15-minute window for digest delivery
            var timeWindow = TimeSpan.FromMinutes(15);
            var lowerBound = currentTime.Add(-timeWindow);
            var upperBound = currentTime.Add(timeWindow);

            var userIds = await _db.DailyNotificationPreferences
                .Where(p => p.CompanyId == companyId
                         && p.IsActive
                         && p.ReceiveDailyDigest
                         && p.PreferredTime >= lowerBound
                         && p.PreferredTime <= upperBound)
                .Select(p => p.UserId)
                .ToListAsync();

            _logger.LogInformation("Found {Count} users for daily digest at {Time} for company {CompanyId}",
                userIds.Count, currentTime, companyId);

            return userIds;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting users for daily digest at {Time} for company {CompanyId}",
                currentTime, companyId);
            return new List<int>();
        }
    }

    /// <summary>
    /// Generate and send daily digest email for a specific user
    /// </summary>
    public async Task<bool> SendDailyDigestAsync(int userId, int companyId)
    {
        try
        {
            var user = await _db.Users.FindAsync(userId);
            if (user == null || string.IsNullOrWhiteSpace(user.Email))
            {
                _logger.LogWarning("User {UserId} not found or has no email address", userId);
                return false;
            }

            var preference = await _db.DailyNotificationPreferences
                .FirstOrDefaultAsync(p => p.UserId == userId && p.CompanyId == companyId && p.IsActive);

            if (preference == null || !preference.ReceiveDailyDigest)
            {
                _logger.LogInformation("User {UserId} has no active daily digest preference", userId);
                return false;
            }

            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var nextWeek = today.AddDays(7);

            var digestParts = new List<string>();

            // Phase 2C: Parallelize digest data queries using Task.WhenAll
            Task<List<ShiftAssignment>>? upcomingShiftsTask = null;
            Task<List<TimeOffRequest>>? pendingTimeOffTask = null;
            Task<List<SwapRequest>>? pendingSwapsTask = null;
            Task<List<Chore>>? upcomingChoresTask = null;
            Task<List<OnDuty>>? upcomingOnDutyTask = null;

            // Upcoming Shifts (next 7 days)
            if (preference.IncludeUpcomingShifts)
            {
                upcomingShiftsTask = _db.ShiftAssignments
                    .Include(sa => sa.ShiftInstance)
                    .ThenInclude(si => si.ShiftType)
                    .Where(sa => sa.UserId == userId
                              && sa.CompanyId == companyId
                              && sa.ShiftInstance.WorkDate >= today
                              && sa.ShiftInstance.WorkDate <= nextWeek)
                    .OrderBy(sa => sa.ShiftInstance.WorkDate)
                    .ThenBy(sa => sa.ShiftInstance.ShiftType.Start)
                    .Take(10)
                    .ToListAsync();
            }

            // Pending Time-Off and Swap Requests
            if (preference.IncludePendingRequests)
            {
                pendingTimeOffTask = _db.TimeOffRequests
                    .Where(r => r.UserId == userId
                             && r.CompanyId == companyId
                             && r.Status == RequestStatus.Pending)
                    .OrderBy(r => r.StartDate)
                    .Take(5)
                    .ToListAsync();

                pendingSwapsTask = _db.SwapRequests
                    .Include(sr => sr.FromAssignment)
                    .ThenInclude(sa => sa!.ShiftInstance)
                    .ThenInclude(si => si!.ShiftType)
                    .Where(sr => (sr.FromUserId == userId || sr.ToUserId == userId)
                              && sr.CompanyId == companyId
                              && sr.Status == RequestStatus.Pending)
                    .OrderBy(sr => sr.CreatedAt)
                    .Take(5)
                    .ToListAsync();
            }

            // Assigned Chores (next 7 days)
            if (preference.IncludeChores)
            {
                upcomingChoresTask = _db.Chores
                    .Where(c => c.UserId == userId
                             && c.CompanyId == companyId
                             && c.Date >= today
                             && c.Date <= nextWeek
                             && c.CanceledAt == null)
                    .OrderBy(c => c.Date)
                    .Take(10)
                    .ToListAsync();
            }

            // OnDuty Assignments (next 7 days)
            if (preference.IncludeOnDuty)
            {
                upcomingOnDutyTask = _db.OnDuties
                    .Where(od => od.UserId == userId
                              && od.Date >= today
                              && od.Date <= nextWeek
                              && od.CanceledAt == null)
                    .OrderBy(od => od.Date)
                    .Take(10)
                    .ToListAsync();
            }

            // Wait for all queries to complete in parallel
            var tasks = new List<Task>();
            if (upcomingShiftsTask != null) tasks.Add(upcomingShiftsTask);
            if (pendingTimeOffTask != null) tasks.Add(pendingTimeOffTask);
            if (pendingSwapsTask != null) tasks.Add(pendingSwapsTask);
            if (upcomingChoresTask != null) tasks.Add(upcomingChoresTask);
            if (upcomingOnDutyTask != null) tasks.Add(upcomingOnDutyTask);

            if (tasks.Any())
            {
                await Task.WhenAll(tasks);
            }

            // Build digest content from results
            if (upcomingShiftsTask != null)
            {
                var upcomingShifts = await upcomingShiftsTask;
                if (upcomingShifts.Any())
                {
                    var shiftsHtml = $"<h3>{_localizer["Email_UpcomingShifts"]}</h3><ul>";
                    foreach (var shift in upcomingShifts)
                    {
                        shiftsHtml += $"<li><strong>{shift.ShiftInstance.WorkDate:MMM dd, yyyy}</strong> - " +
                                    $"{shift.ShiftInstance.ShiftType.Name} " +
                                    $"({shift.ShiftInstance.ShiftType.Start:HH:mm} - {shift.ShiftInstance.ShiftType.End:HH:mm})</li>";
                    }
                    shiftsHtml += "</ul>";
                    digestParts.Add(shiftsHtml);
                }
            }

            if (pendingTimeOffTask != null || pendingSwapsTask != null)
            {
                var pendingTimeOff = pendingTimeOffTask != null ? await pendingTimeOffTask : new List<TimeOffRequest>();
                var pendingSwaps = pendingSwapsTask != null ? await pendingSwapsTask : new List<SwapRequest>();

                if (pendingTimeOff.Any() || pendingSwaps.Any())
                {
                    var requestsHtml = $"<h3>{_localizer["Email_PendingRequests"]}</h3>";

                    if (pendingTimeOff.Any())
                    {
                        requestsHtml += $"<h4>{_localizer["Email_TimeOffRequests"]}</h4><ul>";
                        foreach (var request in pendingTimeOff)
                        {
                            var dateRange = request.StartDate == request.EndDate
                                ? request.StartDate.ToString("MMM dd, yyyy")
                                : $"{request.StartDate:MMM dd} - {request.EndDate:MMM dd, yyyy}";
                            requestsHtml += $"<li>{dateRange} - <em>{_localizer["Email_PendingApproval"]}</em></li>";
                        }
                        requestsHtml += "</ul>";
                    }

                    if (pendingSwaps.Any())
                    {
                        requestsHtml += $"<h4>{_localizer["Email_SwapRequests"]}</h4><ul>";
                        foreach (var swap in pendingSwaps)
                        {
                            var shiftInstance = swap.FromAssignment?.ShiftInstance;
                            var shiftInfo = shiftInstance != null
                                ? $"{shiftInstance.WorkDate:MMM dd, yyyy} - {shiftInstance.ShiftType?.Name ?? "Unknown"}"
                                : "Unknown shift";
                            requestsHtml += $"<li>{shiftInfo} - <em>{_localizer["Email_PendingApproval"]}</em></li>";
                        }
                        requestsHtml += "</ul>";
                    }

                    digestParts.Add(requestsHtml);
                }
            }

            if (upcomingChoresTask != null)
            {
                var upcomingChores = await upcomingChoresTask;
                if (upcomingChores.Any())
                {
                    var choresHtml = $"<h3>{_localizer["Email_AssignedChores"]}</h3><ul>";
                    foreach (var chore in upcomingChores)
                    {
                        choresHtml += $"<li><strong>{chore.Date:MMM dd, yyyy}</strong> - {chore.Title}</li>";
                    }
                    choresHtml += "</ul>";
                    digestParts.Add(choresHtml);
                }
            }

            if (upcomingOnDutyTask != null)
            {
                var upcomingOnDuty = await upcomingOnDutyTask;
                if (upcomingOnDuty.Any())
                {
                    var onDutyHtml = $"<h3>{_localizer["Email_OnDutyAssignments"]}</h3><ul>";
                    foreach (var od in upcomingOnDuty)
                    {
                        var typeName = od.Type == OnDutyType.Hakam
                            ? _localizer["OnDutyTypeHakam"]
                            : _localizer["OnDutyTypeLead"];
                        onDutyHtml += $"<li><strong>{od.Date:MMM dd, yyyy}</strong> - {typeName}</li>";
                    }
                    onDutyHtml += "</ul>";
                    digestParts.Add(onDutyHtml);
                }
            }

            // Feature 1: Today's On-Duty Assignments (subscribed roles)
            var subscribedRoles = await _db.OnDutyRoleSubscriptions
                .Where(s => s.UserId == userId && s.CompanyId == companyId && s.IsActive)
                .Select(s => s.OnDutyTypeValue)
                .ToListAsync();

            if (subscribedRoles.Any())
            {
                var todaysOnDuty = await _db.OnDuties
                    .Include(od => od.User)
                    .Where(od => od.Date == today
                              && od.CanceledAt == null
                              && subscribedRoles.Contains((int)od.Type))
                    .ToListAsync();

                if (todaysOnDuty.Any())
                {
                    var todayHtml = $"<h3 style='color: #6366f1; margin-top: 1.5rem;'>{_localizer["Email_TodaysOnDutyAssignments"]}</h3><ul>";

                    // Load custom types for name resolution
                    var customTypes = await _db.OnDutyTypeConfigs
                        .Where(c => c.IsActive)
                        .ToDictionaryAsync(c => c.TypeValue, c => c);

                    foreach (var od in todaysOnDuty)
                    {
                        // Get role name (enum or custom)
                        string roleName;
                        if ((int)od.Type == 0)
                            roleName = _localizer["OnDutyTypeHakam"];
                        else if ((int)od.Type == 1)
                            roleName = _localizer["OnDutyTypeLead"];
                        else if (customTypes.TryGetValue((int)od.Type, out var customType))
                        {
                            var currentCulture = System.Globalization.CultureInfo.CurrentCulture.Name;
                            roleName = currentCulture.StartsWith("he") ? customType.NameHe : customType.NameEn;
                        }
                        else
                            roleName = _localizer["Email_UnknownRole"];

                        // Format: "Today's Hakam is John Doe"
                        var baseUrl = _configuration["App:BaseUrl"] ?? "http://localhost:5000";
                        var todaysRole = string.Format(_localizer["Email_TodaysRole"],
                                        $"<strong>{roleName}</strong>",
                                        $"<a href='{baseUrl}/My/Profile?userId={od.UserId}'>{od.User?.DisplayName ?? "Unknown"}</a>");
                        todayHtml += $"<li>{todaysRole}</li>";
                    }
                    todayHtml += "</ul>";
                    digestParts.Add(todayHtml);
                }
            }

            // If no content, don't send email
            if (!digestParts.Any())
            {
                _logger.LogInformation("No digest content for user {UserId}, skipping email", userId);
                return true; // Not an error, just nothing to send
            }

            // Build email HTML
            var emailDir = _localizer["Dir"] == "rtl" ? "rtl" : "ltr";
            var emailBody = $@"
<!DOCTYPE html>
<html dir='{emailDir}'>
<head>
    <meta charset='utf-8'>
    <style>
        body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; }}
        h2 {{ color: #4F46E5; }}
        h3 {{ color: #6366F1; margin-top: 1.5rem; margin-bottom: 0.5rem; }}
        h4 {{ color: #818CF8; margin-top: 1rem; margin-bottom: 0.5rem; }}
        ul {{ list-style-type: none; padding-left: 0; }}
        li {{ padding: 0.5rem 0; border-bottom: 1px solid #E5E7EB; }}
        .footer {{ margin-top: 2rem; padding-top: 1rem; border-top: 2px solid #E5E7EB; color: #6B7280; font-size: 0.875rem; }}
    </style>
</head>
<body>
    <h2>{string.Format(_localizer["Email_DailyDigestTitle"], user.DisplayName)}</h2>
    <p>{_localizer["Email_DailyDigestIntro"]}</p>
    {string.Join("\n", digestParts)}
    <div class='footer'>
        <p>{_localizer["Email_DailyDigestFooter"]}</p>
        <p><em>{string.Format(_localizer["Email_SentAt"], DateTime.UtcNow.ToString("MMM dd, yyyy HH:mm"))}</em></p>
    </div>
</body>
</html>";

            var subject = string.Format(_localizer["Email_DailyDigestSubject"], today.ToString("MMM dd, yyyy"));

            var success = await _mailService.SendMailAsync(user.Email, subject, emailBody);

            if (success)
            {
                _logger.LogInformation("Successfully sent daily digest to user {UserId} ({Email})", userId, user.Email);
            }
            else
            {
                _logger.LogWarning("Failed to send daily digest to user {UserId} ({Email})", userId, user.Email);
            }

            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending daily digest to user {UserId} in company {CompanyId}", userId, companyId);
            return false;
        }
    }

    /// <summary>
    /// Phase 6 Extension: Send day-before reminders for upcoming shifts, chores, and on-duty assignments
    /// </summary>
    public async Task SendDayBeforeRemindersAsync(int userId, int companyId)
    {
        try
        {
            // Get user
            var user = await _db.Users.FindAsync(userId);
            if (user == null || string.IsNullOrWhiteSpace(user.Email))
            {
                _logger.LogWarning("User {UserId} not found or has no email", userId);
                return;
            }

            // Get reminder preferences
            var prefs = await _db.DailyNotificationPreferences
                .FirstOrDefaultAsync(p => p.UserId == userId && p.CompanyId == companyId && p.IsActive);

            if (prefs == null || (!prefs.RemindBeforeShifts && !prefs.RemindBeforeChores && !prefs.RemindBeforeOnDuty))
            {
                _logger.LogDebug("User {UserId} has no day-before reminders enabled", userId);
                return;
            }

            var tomorrow = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));
            var reminderParts = new List<string>();

            // 1. Upcoming Shifts
            if (prefs.RemindBeforeShifts)
            {
                var tomorrowShifts = await _db.ShiftAssignments
                    .Include(sa => sa.ShiftInstance)
                    .ThenInclude(si => si.ShiftType)
                    .Where(sa => sa.UserId == userId
                              && sa.CompanyId == companyId
                              && sa.ShiftInstance.WorkDate == tomorrow)
                    .OrderBy(sa => sa.ShiftInstance.ShiftType.Start)
                    .ToListAsync();

                if (tomorrowShifts.Any())
                {
                    var shiftsHtml = $"<h3 style='color: #6366f1;'>{_localizer["Email_UpcomingShiftsTomorrow"]}</h3><ul>";
                    foreach (var sa in tomorrowShifts)
                    {
                        var shiftType = sa.ShiftInstance?.ShiftType;
                        var shiftTypeName = shiftType?.Name ?? _localizer["Email_Shift"];
                        var startTime = shiftType?.Start.ToString("HH:mm") ?? "--:--";
                        var endTime = shiftType?.End.ToString("HH:mm") ?? "--:--";
                        shiftsHtml += $"<li><strong>{shiftTypeName}</strong> - {startTime} to {endTime}</li>";
                    }
                    shiftsHtml += "</ul>";
                    reminderParts.Add(shiftsHtml);
                }
            }

            // 2. Upcoming Chores
            if (prefs.RemindBeforeChores)
            {
                var tomorrowChores = await _db.Chores
                    .Where(c => c.UserId == userId
                             && c.CompanyId == companyId
                             && c.Date == tomorrow
                             && c.CanceledAt == null)
                    .OrderBy(c => c.Title)
                    .ToListAsync();

                if (tomorrowChores.Any())
                {
                    var choresHtml = $"<h3 style='color: #10b981;'>{_localizer["Email_ChoresAssignedTomorrow"]}</h3><ul>";
                    foreach (var chore in tomorrowChores)
                    {
                        choresHtml += $"<li>{chore.Title}</li>";
                    }
                    choresHtml += "</ul>";
                    reminderParts.Add(choresHtml);
                }
            }

            // 3. Upcoming On-Duty
            if (prefs.RemindBeforeOnDuty)
            {
                var tomorrowOnDuties = await _db.OnDuties
                    .Where(od => od.UserId == userId
                              && od.Date == tomorrow
                              && od.CanceledAt == null)
                    .ToListAsync();

                if (tomorrowOnDuties.Any())
                {
                    var onDutyHtml = $"<h3 style='color: #f59e0b;'>{_localizer["Email_OnDutyAssignmentsTomorrow"]}</h3><ul>";

                    // Load custom types for name resolution
                    var customTypes = await _db.OnDutyTypeConfigs
                        .Where(c => c.IsActive)
                        .ToDictionaryAsync(c => c.TypeValue, c => c);

                    foreach (var od in tomorrowOnDuties)
                    {
                        // Get role name (enum or custom)
                        string roleName;
                        if ((int)od.Type == 0)
                            roleName = _localizer["OnDutyTypeHakam"];
                        else if ((int)od.Type == 1)
                            roleName = _localizer["OnDutyTypeLead"];
                        else if (customTypes.TryGetValue((int)od.Type, out var customType))
                        {
                            var currentCulture = System.Globalization.CultureInfo.CurrentCulture.Name;
                            roleName = currentCulture.StartsWith("he") ? customType.NameHe : customType.NameEn;
                        }
                        else
                            roleName = _localizer["Email_UnknownRole"];

                        onDutyHtml += $"<li><strong>{roleName}</strong></li>";
                    }
                    onDutyHtml += "</ul>";
                    reminderParts.Add(onDutyHtml);
                }
            }

            // If no reminders, don't send email
            if (!reminderParts.Any())
            {
                _logger.LogInformation("No day-before reminders for user {UserId}", userId);
                return;
            }

            // Build email HTML
            var emailDir = _localizer["Dir"] == "rtl" ? "rtl" : "ltr";
            var emailBody = $@"
<!DOCTYPE html>
<html dir='{emailDir}'>
<head>
    <meta charset='utf-8'>
    <style>
        body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; }}
        h2 {{ color: #4F46E5; }}
        h3 {{ color: #6366F1; margin-top: 1.5rem; margin-bottom: 0.5rem; }}
        ul {{ list-style-type: none; padding-left: 0; }}
        li {{ padding: 0.5rem 0; border-bottom: 1px solid #E5E7EB; }}
        .footer {{ margin-top: 2rem; padding-top: 1rem; border-top: 2px solid #E5E7EB; color: #6B7280; font-size: 0.875rem; }}
    </style>
</head>
<body>
    <h2>{_localizer["Email_ReminderTitle"]}</h2>
    <p>{string.Format(_localizer["Email_ReminderIntro"], user.DisplayName, tomorrow.ToString("MMM dd, yyyy"))}</p>
    {string.Join("\n", reminderParts)}
    <div class='footer'>
        <p>{_localizer["Email_ReminderFooter"]}</p>
        <p><em>{string.Format(_localizer["Email_SentAt"], DateTime.UtcNow.ToString("MMM dd, yyyy HH:mm"))}</em></p>
    </div>
</body>
</html>";

            var subject = string.Format(_localizer["Email_ReminderSubject"], tomorrow.ToString("MMM dd, yyyy"));

            var success = await _mailService.SendMailAsync(user.Email, subject, emailBody);

            if (success)
            {
                _logger.LogInformation("Successfully sent day-before reminders to user {UserId} ({Email})", userId, user.Email);
            }
            else
            {
                _logger.LogWarning("Failed to send day-before reminders to user {UserId} ({Email})", userId, user.Email);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending day-before reminders to user {UserId} in company {CompanyId}", userId, companyId);
        }
    }

    // ============= Ops Console Scheduler: Trainee Notifications =============

    public async Task CreateTraineeAddedNotificationAsync(int primaryUserId, int traineeUserId, string traineeName, string shiftTypeName, DateOnly date, TimeOnly start, TimeOnly end)
    {
        var title = "Trainee Added to Your Shift";
        var message = $"{traineeName} has been added as a trainee to your {shiftTypeName} shift on {date:MMM dd, yyyy} ({start:HH:mm} - {end:HH:mm})";

        await CreateNotificationAsync(primaryUserId, NotificationType.ShiftAdded, title, message, null, "ShiftAssignment");

        // Send email notification
        try
        {
            var user = await _db.Users.FindAsync(primaryUserId);
            if (user != null && !string.IsNullOrWhiteSpace(user.Email))
            {
                await _mailService.SendTraineeAddedEmailAsync(
                    user.Email,
                    user.DisplayName,
                    traineeName,
                    shiftTypeName,
                    date,
                    start,
                    end);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending trainee added email to user {UserId}", primaryUserId);
            // Don't throw - email failure should not block notification creation
        }
    }

    public async Task CreateTraineeChangedNotificationAsync(int primaryUserId, int oldTraineeUserId, int newTraineeUserId, string oldTraineeName, string newTraineeName, string shiftTypeName, DateOnly date)
    {
        var title = "Trainee Changed on Your Shift";
        var message = $"Your trainee for {shiftTypeName} on {date:MMM dd, yyyy} has been changed from {oldTraineeName} to {newTraineeName}";

        await CreateNotificationAsync(primaryUserId, NotificationType.ShiftAdded, title, message, null, "ShiftAssignment");

        // Optionally notify old and new trainees
        await CreateNotificationAsync(oldTraineeUserId, NotificationType.ShiftRemoved, "Trainee Assignment Removed", $"You are no longer a trainee for {shiftTypeName} on {date:MMM dd, yyyy}", null, "ShiftAssignment");
        await CreateNotificationAsync(newTraineeUserId, NotificationType.ShiftAdded, "New Trainee Assignment", $"You have been assigned as a trainee for {shiftTypeName} on {date:MMM dd, yyyy}", null, "ShiftAssignment");
    }

    public async Task CreateTraineeRemovedNotificationAsync(int primaryUserId, int traineeUserId, string traineeName, string shiftTypeName, DateOnly date)
    {
        var title = "Trainee Removed from Your Shift";
        var message = $"{traineeName} has been removed as a trainee from your {shiftTypeName} shift on {date:MMM dd, yyyy}";

        await CreateNotificationAsync(primaryUserId, NotificationType.ShiftAdded, title, message, null, "ShiftAssignment");

        // Notify trainee
        await CreateNotificationAsync(traineeUserId, NotificationType.ShiftRemoved, "Trainee Assignment Removed", $"You are no longer a trainee for {shiftTypeName} on {date:MMM dd, yyyy}", null, "ShiftAssignment");
    }

    // ============= Ops Console Scheduler: Staffing Change Notifications =============

    public async Task CreateSlotRemovedNotificationAsync(int affectedUserId, string shiftTypeName, DateOnly date, TimeOnly start, TimeOnly end, string reason)
    {
        var title = "Shift Assignment Removed";
        var message = $"Your {shiftTypeName} shift on {date:MMM dd, yyyy} ({start:HH:mm} - {end:HH:mm}) has been removed. Reason: {reason}";

        await CreateNotificationAsync(affectedUserId, NotificationType.ShiftRemoved, title, message, null, "ShiftAssignment");

        // Send email notification
        try
        {
            var user = await _db.Users.FindAsync(affectedUserId);
            if (user != null && !string.IsNullOrWhiteSpace(user.Email))
            {
                await _mailService.SendSlotRemovedEmailAsync(
                    user.Email,
                    user.DisplayName,
                    shiftTypeName,
                    date,
                    start,
                    end,
                    reason);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending slot removed email to user {UserId}", affectedUserId);
            // Don't throw - email failure should not block notification creation
        }
    }

    // ============= Ops Console Scheduler: Shift Modification Notifications =============

    public async Task CreateShiftModifiedNotificationAsync(List<int> assignedUserIds, string shiftTypeName, DateOnly date, string changeDescription)
    {
        var title = "Shift Details Modified";
        var message = $"Your {shiftTypeName} shift on {date:MMM dd, yyyy} has been modified: {changeDescription}";

        foreach (var userId in assignedUserIds)
        {
            await CreateNotificationAsync(userId, NotificationType.ShiftAdded, title, message, null, "ShiftAssignment");

            // Send email notification
            try
            {
                var user = await _db.Users.FindAsync(userId);
                if (user != null && !string.IsNullOrWhiteSpace(user.Email))
                {
                    await _mailService.SendShiftModifiedEmailAsync(
                        user.Email,
                        user.DisplayName,
                        shiftTypeName,
                        date,
                        changeDescription);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending shift modified email to user {UserId}", userId);
                // Don't throw - email failure should not block notification creation
            }
        }
    }
}