using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;

namespace ShiftManager.Services;

public interface INotificationService
{
    Task CreateNotificationAsync(int userId, NotificationType type, string title, string message, int? relatedEntityId = null, string? relatedEntityType = null);
    Task CreateShiftAddedNotificationAsync(int userId, string shiftTypeName, DateOnly shiftDate, TimeOnly startTime, TimeOnly endTime);
    Task CreateShiftRemovedNotificationAsync(int userId, string shiftTypeName, DateOnly shiftDate, TimeOnly startTime, TimeOnly endTime);
    Task CreateTimeOffNotificationAsync(int userId, RequestStatus status, DateOnly startDate, DateOnly endDate, int requestId);
    Task CreateSwapRequestNotificationAsync(int userId, RequestStatus status, string shiftInfo, int requestId);
    Task CreateChoreAssignedNotificationAsync(int userId, string choreTitle, DateOnly choreDate, int choreId);
    Task CreateChoreCanceledNotificationAsync(int userId, string choreTitle, DateOnly choreDate, int choreId);
}

public class NotificationService : INotificationService
{
    private readonly AppDbContext _db;
    private readonly ILogger<NotificationService> _logger;
    private readonly ITenantResolver _tenantResolver;
    private readonly IMailService _mailService;

    public NotificationService(AppDbContext db, ILogger<NotificationService> logger, ITenantResolver tenantResolver, IMailService mailService)
    {
        _db = db;
        _logger = logger;
        _tenantResolver = tenantResolver;
        _mailService = mailService;
    }

    public async Task CreateNotificationAsync(int userId, NotificationType type, string title, string message, int? relatedEntityId = null, string? relatedEntityType = null)
    {
        try
        {
            var notification = new UserNotification
            {
                CompanyId = _tenantResolver.GetCurrentTenantId(), // Multitenancy Phase 2
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
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating notification {Type} for user {UserId}", type, userId);
        }
    }

    public async Task CreateShiftAddedNotificationAsync(int userId, string shiftTypeName, DateOnly shiftDate, TimeOnly startTime, TimeOnly endTime)
    {
        var title = "New Shift Assignment";
        var message = $"You have been assigned to work {shiftTypeName} on {shiftDate:MMM dd, yyyy} from {startTime:HH:mm} to {endTime:HH:mm}.";

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
        var title = "Shift Assignment Removed";
        var message = $"Your {shiftTypeName} shift on {shiftDate:MMM dd, yyyy} from {startTime:HH:mm} to {endTime:HH:mm} has been removed.";

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
        var statusText = status == RequestStatus.Approved ? "Approved" : "Declined";
        var title = $"Time-Off Request {statusText}";
        var dateRange = startDate == endDate ? startDate.ToString("MMM dd, yyyy") : $"{startDate:MMM dd} - {endDate:MMM dd, yyyy}";
        var message = $"Your time-off request for {dateRange} has been {statusText.ToLower()}.";

        var notificationType = status == RequestStatus.Approved ? NotificationType.TimeOffApproved : NotificationType.TimeOffDeclined;

        await CreateNotificationAsync(userId, notificationType, title, message, requestId, "TimeOffRequest");
    }

    public async Task CreateSwapRequestNotificationAsync(int userId, RequestStatus status, string shiftInfo, int requestId)
    {
        var statusText = status == RequestStatus.Approved ? "Approved" : "Declined";
        var title = $"Shift Swap Request {statusText}";
        var message = $"Your shift swap request for {shiftInfo} has been {statusText.ToLower()}.";

        var notificationType = status == RequestStatus.Approved ? NotificationType.SwapRequestApproved : NotificationType.SwapRequestDeclined;

        await CreateNotificationAsync(userId, notificationType, title, message, requestId, "SwapRequest");
    }

    public async Task CreateChoreAssignedNotificationAsync(int userId, string choreTitle, DateOnly choreDate, int choreId)
    {
        var title = "New Chore Assignment";
        var message = $"You have been assigned a chore: \"{choreTitle}\" on {choreDate:MMM dd, yyyy}.";

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
        var title = "Chore Canceled";
        var message = $"Your chore \"{choreTitle}\" on {choreDate:MMM dd, yyyy} has been canceled.";

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
}