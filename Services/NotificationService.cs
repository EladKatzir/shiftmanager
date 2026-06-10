using System.Globalization;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Configuration;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Results;
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
    Task NotifyOwnersOfAccessRequestAsync(string requesterName, string requesterEmail, string companyName, int requestId, int companyId);
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

    // ========================================
    // Diagnostic / Admin helpers (additive — return OperationResult so callers can
    // surface structured failure reasons in admin/test UIs).
    //
    // The 30+ existing CreateXxxNotificationAsync methods intentionally swallow failures
    // (a bad notification must not block the underlying business action). These wrappers
    // exist for diagnostic surfaces ("send me a test notification" buttons, admin tools)
    // that DO need to know whether dispatch succeeded and why it failed.
    // ========================================

    /// <summary>
    /// Diagnostic variant of <see cref="CreateNotificationAsync(int, NotificationType, string, string, int?, string?)"/>
    /// that returns a structured <see cref="OperationResult"/> so callers can render the
    /// failure reason (recipient missing, DB error, etc.) instead of silently returning
    /// <c>false</c>. Suitable for admin diagnostic pages — NOT a replacement for the
    /// fire-and-forget creators used by business code.
    /// </summary>
    Task<Models.Results.OperationResult> TryCreateNotificationWithDiagnosticsAsync(
        int recipientUserId, string title, string body, string? linkUrl = null);

    /// <summary>
    /// Send a test notification to <paramref name="recipientUserId"/> so an admin can verify
    /// that the notification pipeline reaches a user. Returns a structured
    /// <see cref="OperationResult"/> with a localized error message on failure.
    /// </summary>
    Task<Models.Results.OperationResult> TrySendTestNotificationAsync(int recipientUserId);
}

// SECURITY-AUDITED: IgnoreQueryFilters() in this class is SAFE on two paths:
//   1) NotifyOwnersOfAccessRequestAsync — finds Owners across companies during anonymous signup
//      flow (no tenant context); scoped by Role filter.
//   2) GetRecipientAcrossTenantsAsync — looks up the recipient of a notification/email so cross-
//      tenant assignments (Owner/Director/AreaAdmin acting outside their own company) actually
//      reach the assignee. Authorization for the action that triggered the notification has
//      already been enforced upstream by the caller.
public partial class NotificationService : INotificationService
{
    private readonly AppDbContext _db;
    private readonly ILogger<NotificationService> _logger;
    private readonly ITenantResolver _tenantResolver;
    private readonly IMailService _mailService;
    private readonly IStringLocalizer<SharedResources> _localizer;
    private readonly IConfiguration _configuration;
    private readonly ILocalizationService _localization;
    private readonly ICompanyLocalizationService _companyLocalizationService;
    // Optional so existing unit tests can construct NotificationService without it; production DI
    // always injects it. Drives the email gate (engagement/Quiet/mutes) and the 20-unread throttle.
    private readonly INotificationPreferenceService? _preferenceService;

    /// <summary>
    /// Look up a notification recipient across tenants. The tenant query filter on AppUser would
    /// otherwise hide users in other companies from a manager acting on a cross-tenant grant
    /// (Owner/Director/AreaAdmin), silently dropping notification emails. Read-only by design.
    /// </summary>
    private Task<AppUser?> GetRecipientAcrossTenantsAsync(int userId)
        => _db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == userId);

    public NotificationService(AppDbContext db, ILogger<NotificationService> logger, ITenantResolver tenantResolver, IMailService mailService, IStringLocalizer<SharedResources> localizer, IConfiguration configuration, ILocalizationService localization, ICompanyLocalizationService companyLocalizationService, INotificationPreferenceService? preferenceService = null)
    {
        _db = db;
        _logger = logger;
        _tenantResolver = tenantResolver;
        _mailService = mailService;
        _localizer = localizer;
        _configuration = configuration;
        _localization = localization;
        _companyLocalizationService = companyLocalizationService;
        _preferenceService = preferenceService;
    }

    /// <summary>
    /// Email gate: whether the email channel should fire for this event/recipient given the user's
    /// engagement mode + mutes. When the preference service is unavailable (unit tests), defaults to
    /// true (preserves legacy "always email" behavior).
    /// </summary>
    private async Task<bool> ShouldEmailAsync(int userId, int companyId, Notifications.NotificationCategory category, bool personallyActionable, bool securityCritical = false)
        => _preferenceService == null || await _preferenceService.ShouldSendEmailAsync(userId, companyId, category, personallyActionable, securityCritical);

    /// <summary>
    /// 20-unread "catch up" throttle. Call after persisting an in-app notification. In Quiet mode,
    /// when the recipient's unread count crosses the threshold, sends one localized catch-up email.
    /// No-op when the preference service is unavailable.
    /// </summary>
    private async Task MaybeSendCatchUpAsync(int userId, int companyId)
    {
        if (_preferenceService == null)
            return;

        try
        {
            var unread = await _db.UserNotifications
                .IgnoreQueryFilters()
                .CountAsync(n => n.UserId == userId && !n.IsRead);

            if (await _preferenceService.TryBeginCatchUpAsync(userId, companyId, unread))
            {
                var subject = _localizer["Email_CatchUpSubject"].Value;
                var body = WebUtility.HtmlEncode(_localizer["Email_CatchUpBody"].Value);
                var html = $"<!DOCTYPE html><html><body><div style='font-family:Arial,sans-serif;padding:16px;'>" +
                           $"<p>{body}</p></div></body></html>";

                var recipient = await GetRecipientAcrossTenantsAsync(userId);
                if (recipient != null && !string.IsNullOrWhiteSpace(recipient.Email))
                {
                    await _mailService.SendMailAsync(recipient.Email, subject, html, recipientUserId: userId);
                }
            }
        }
        catch (Exception ex)
        {
            // A throttle failure must never block the underlying notification.
            _logger.LogWarning(ex, "Catch-up throttle evaluation failed for user {UserId}", userId);
        }
    }

    public async Task<bool> CreateNotificationAsync(int userId, NotificationType type, string title, string message, int? relatedEntityId = null, string? relatedEntityType = null)
    {
        int? companyId = null;
        try
        {
            // Look up the recipient first so the notification is stored under THEIR tenant.
            // Using the caller's tenant here would persist notifications under the acting manager's
            // CompanyId for cross-tenant actions (Owner/Director with project/area-wide grants),
            // and the UserNotifications query filter would then hide them from the assignee.
            var recipient = await GetRecipientAcrossTenantsAsync(userId);
            if (recipient == null)
            {
                LogCreateRecipientNotFound(_logger, userId);
                return false;
            }
            companyId = recipient.CompanyId;

            var notification = new UserNotification
            {
                CompanyId = companyId.Value,
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

            LogNotificationCreated(_logger, type, userId, title);

            // 20-unread "catch up" throttle (Quiet mode). Centralized here so every in-app
            // notification participates regardless of which creator produced it.
            await MaybeSendCatchUpAsync(userId, companyId.Value);

            return true;
        }
        catch (DbUpdateException ex)
        {
            LogCreateDbError(_logger, ex, companyId, type, userId, title);
            return false;
        }
        catch (Exception ex)
        {
            LogCreateUnexpectedError(_logger, ex, companyId, type, userId, title);
            return false;
        }
    }

    public async Task CreateShiftAddedNotificationAsync(int userId, string shiftTypeName, DateOnly shiftDate, TimeOnly startTime, TimeOnly endTime)
    {
        var title = _localizer["NotificationShiftAddedTitle"];
        var message = string.Format(CultureInfo.CurrentCulture, _localizer["NotificationShiftAddedMessage"],
            shiftTypeName,
            _localization.FormatMediumDate(shiftDate),
            _localization.FormatTime(startTime),
            _localization.FormatTime(endTime));

        await CreateNotificationAsync(userId, NotificationType.ShiftAdded, title, message, null, "ShiftAssignment");

        // Send email notification
        try
        {
            var user = await GetRecipientAcrossTenantsAsync(userId);
            if (user != null && !string.IsNullOrWhiteSpace(user.Email)
                && await ShouldEmailAsync(userId, user.CompanyId, Notifications.NotificationCategory.ShiftAssignment, personallyActionable: true))
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
            LogEmailErrorShiftAssigned(_logger, ex, userId);
            // Don't throw - email failure should not block notification creation
        }
    }

    public async Task CreateShiftRemovedNotificationAsync(int userId, string shiftTypeName, DateOnly shiftDate, TimeOnly startTime, TimeOnly endTime)
    {
        var title = _localizer["NotificationShiftRemovedTitle"];
        var message = string.Format(CultureInfo.CurrentCulture, _localizer["NotificationShiftRemovedMessage"],
            shiftTypeName,
            _localization.FormatMediumDate(shiftDate),
            _localization.FormatTime(startTime),
            _localization.FormatTime(endTime));

        await CreateNotificationAsync(userId, NotificationType.ShiftRemoved, title, message, null, "ShiftAssignment");

        // Send email notification
        try
        {
            var user = await GetRecipientAcrossTenantsAsync(userId);
            if (user != null && !string.IsNullOrWhiteSpace(user.Email)
                && await ShouldEmailAsync(userId, user.CompanyId, Notifications.NotificationCategory.ShiftAssignment, personallyActionable: true))
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
            LogEmailErrorShiftRemoved(_logger, ex, userId);
            // Don't throw - email failure should not block notification creation
        }
    }

    public async Task CreateTimeOffNotificationAsync(int userId, RequestStatus status, DateOnly startDate, DateOnly endDate, int requestId)
    {
        var title = status == RequestStatus.Approved
            ? _localizer["NotificationTimeOffApprovedTitle"]
            : _localizer["NotificationTimeOffDeclinedTitle"];

        var dateRange = startDate == endDate
            ? _localization.FormatMediumDate(startDate)
            : $"{_localization.FormatMediumDate(startDate)} - {_localization.FormatMediumDate(endDate)}";

        var message = status == RequestStatus.Approved
            ? string.Format(CultureInfo.CurrentCulture, _localizer["NotificationTimeOffApprovedMessage"], dateRange)
            : string.Format(CultureInfo.CurrentCulture, _localizer["NotificationTimeOffDeclinedMessage"], dateRange);

        var notificationType = status == RequestStatus.Approved ? NotificationType.TimeOffApproved : NotificationType.TimeOffDeclined;

        await CreateNotificationAsync(userId, notificationType, title, message, requestId, "TimeOffRequest");

        // Send email notification
        try
        {
            var user = await GetRecipientAcrossTenantsAsync(userId);
            if (user != null && !string.IsNullOrWhiteSpace(user.Email)
                && await ShouldEmailAsync(userId, user.CompanyId, Notifications.NotificationCategory.TimeOff, personallyActionable: true))
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
            LogEmailErrorTimeOff(_logger, ex, status, userId);
            // Don't throw - email failure should not block notification creation
        }
    }

    public async Task CreateSwapRequestNotificationAsync(int userId, RequestStatus status, string shiftInfo, int requestId)
    {
        var title = status == RequestStatus.Approved
            ? _localizer["NotificationSwapRequestApprovedTitle"]
            : _localizer["NotificationSwapRequestDeclinedTitle"];

        var message = status == RequestStatus.Approved
            ? string.Format(CultureInfo.CurrentCulture, _localizer["NotificationSwapRequestApprovedMessage"], shiftInfo)
            : string.Format(CultureInfo.CurrentCulture, _localizer["NotificationSwapRequestDeclinedMessage"], shiftInfo);

        var notificationType = status == RequestStatus.Approved ? NotificationType.SwapRequestApproved : NotificationType.SwapRequestDeclined;

        await CreateNotificationAsync(userId, notificationType, title, message, requestId, "SwapRequest");

        // Send email notification
        try
        {
            var user = await GetRecipientAcrossTenantsAsync(userId);
            if (user != null && !string.IsNullOrWhiteSpace(user.Email)
                && await ShouldEmailAsync(userId, user.CompanyId, Notifications.NotificationCategory.Swap, personallyActionable: true))
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
            LogEmailErrorSwapRequest(_logger, ex, status, userId);
            // Don't throw - email failure should not block notification creation
        }
    }

    public async Task CreateChoreAssignedNotificationAsync(int userId, string choreTitle, DateOnly choreDate, int choreId)
    {
        var title = _localizer["NotificationChoreAssignedTitle"];
        var message = string.Format(CultureInfo.CurrentCulture, _localizer["NotificationChoreAssignedMessage"],
            choreTitle,
            _localization.FormatMediumDate(choreDate));

        await CreateNotificationAsync(userId, NotificationType.ChoreAssigned, title, message, choreId, "Chore");

        // Send email notification
        try
        {
            var user = await GetRecipientAcrossTenantsAsync(userId);
            if (user != null && !string.IsNullOrWhiteSpace(user.Email)
                && await ShouldEmailAsync(userId, user.CompanyId, Notifications.NotificationCategory.Chore, personallyActionable: true))
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
            LogEmailErrorChoreAssigned(_logger, ex, userId);
            // Don't throw - email failure should not block notification creation
        }
    }

    public async Task CreateChoreCanceledNotificationAsync(int userId, string choreTitle, DateOnly choreDate, int choreId)
    {
        var title = _localizer["NotificationChoreCanceledTitle"];
        var message = string.Format(CultureInfo.CurrentCulture, _localizer["NotificationChoreCanceledMessage"],
            choreTitle,
            _localization.FormatMediumDate(choreDate));

        await CreateNotificationAsync(userId, NotificationType.ChoreCanceled, title, message, choreId, "Chore");

        // Send email notification
        try
        {
            var user = await GetRecipientAcrossTenantsAsync(userId);
            if (user != null && !string.IsNullOrWhiteSpace(user.Email)
                && await ShouldEmailAsync(userId, user.CompanyId, Notifications.NotificationCategory.Chore, personallyActionable: true))
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
            LogEmailErrorChoreCanceled(_logger, ex, userId);
            // Don't throw - email failure should not block notification creation
        }
    }

    public async Task CreateOnDutyAssignedNotificationAsync(int userId, OnDutyType onDutyType, DateOnly onDutyDate, int onDutyId)
    {
        var onDutyTypeName = onDutyType == OnDutyType.Hakam
            ? _localizer["OnDutyTypeHakam"]
            : _localizer["OnDutyTypeLead"];

        var title = _localizer["NotificationOnDutyAssignedTitle"];
        var message = string.Format(CultureInfo.CurrentCulture, _localizer["NotificationOnDutyAssignedMessage"],
            onDutyTypeName,
            _localization.FormatMediumDate(onDutyDate));

        await CreateNotificationAsync(userId, NotificationType.OnDutyAssigned, title, message, onDutyId, "OnDuty");

        // Send email notification
        try
        {
            var user = await GetRecipientAcrossTenantsAsync(userId);
            if (user != null && !string.IsNullOrWhiteSpace(user.Email)
                && await ShouldEmailAsync(userId, user.CompanyId, Notifications.NotificationCategory.OnDuty, personallyActionable: true))
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
            LogEmailErrorOnDutyAssigned(_logger, ex, userId);
            // Don't throw - email failure should not block notification creation
        }
    }

    public async Task CreateOnDutyCanceledNotificationAsync(int userId, OnDutyType onDutyType, DateOnly onDutyDate, int onDutyId)
    {
        var onDutyTypeName = onDutyType == OnDutyType.Hakam
            ? _localizer["OnDutyTypeHakam"]
            : _localizer["OnDutyTypeLead"];

        var title = _localizer["NotificationOnDutyCanceledTitle"];
        var message = string.Format(CultureInfo.CurrentCulture, _localizer["NotificationOnDutyCanceledMessage"],
            onDutyTypeName,
            _localization.FormatMediumDate(onDutyDate));

        await CreateNotificationAsync(userId, NotificationType.OnDutyCanceled, title, message, onDutyId, "OnDuty");

        // Send email notification
        try
        {
            var user = await GetRecipientAcrossTenantsAsync(userId);
            if (user != null && !string.IsNullOrWhiteSpace(user.Email)
                && await ShouldEmailAsync(userId, user.CompanyId, Notifications.NotificationCategory.OnDuty, personallyActionable: true))
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
            LogEmailErrorOnDutyCanceled(_logger, ex, userId);
            // Don't throw - email failure should not block notification creation
        }
    }

    public async Task CreateTimeOffDeletedNotificationAsync(int userId, DateOnly startDate, DateOnly endDate)
    {
        var title = _localizer["NotificationTimeOffDeletedTitle"];
        var dateRange = startDate == endDate
            ? _localization.FormatMediumDate(startDate)
            : $"{_localization.FormatMediumDate(startDate)} - {_localization.FormatMediumDate(endDate)}";
        var message = string.Format(CultureInfo.CurrentCulture, _localizer["NotificationTimeOffDeletedMessage"], dateRange);

        await CreateNotificationAsync(userId, NotificationType.TimeOffDeleted, title, message, null, "TimeOffRequest");

        // Send email notification
        try
        {
            var user = await GetRecipientAcrossTenantsAsync(userId);
            if (user != null && !string.IsNullOrWhiteSpace(user.Email)
                && await ShouldEmailAsync(userId, user.CompanyId, Notifications.NotificationCategory.TimeOff, personallyActionable: true))
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
            LogEmailErrorTimeOffDeleted(_logger, ex, userId);
            // Don't throw - email failure should not block notification creation
        }
    }

    // ========================================
    // Access Request Notification Methods
    // ========================================

    /// <summary>
    /// Notify all owner users about a new access request and send them emails.
    /// </summary>
    public async Task NotifyOwnersOfAccessRequestAsync(string requesterName, string requesterEmail, string companyName, int requestId, int companyId)
    {
        try
        {
            // Set tenant context for this operation — this method is called from fire-and-forget
            // Task.Run in Signup where there's no HttpContext, so tenant resolver returns 0.
            // Without this, notifications are created with CompanyId=0 and become invisible.
            _tenantResolver.SetCurrentTenantId(companyId);

            // Get owner users scoped to the target company (capped for safety — typically 1-5 per company)
            // IgnoreQueryFilters: called from anonymous signup context where tenant=0,
            // which would filter out ALL users. Scoped by companyId to prevent cross-company notification leak.
            var owners = await _db.Users
                .IgnoreQueryFilters()
                .Where(u => u.CompanyId == companyId && u.Role == UserRole.Owner && u.IsActive)
                .Take(100)
                .ToListAsync();

            if (!owners.Any())
            {
                LogAccessRequestNoOwners(_logger, requestId);
                return;
            }

            var title = _localizer["NotificationAccessRequestSubmittedTitle"];
            var message = string.Format(CultureInfo.CurrentCulture, _localizer["NotificationAccessRequestSubmittedMessage"],
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
                    LogEmailErrorAccessRequest(_logger, ex, owner.Id);
                    // Don't throw - continue notifying other owners
                }
            }

            LogAccessRequestNotified(_logger, owners.Count, requestId, requesterEmail);
        }
        catch (Exception ex)
        {
            LogAccessRequestNotifyError(_logger, ex, requestId);
            // Don't throw - access request was still created successfully
        }
    }

    /// <summary>
    /// Create notification for user when their access request is approved.
    /// </summary>
    public async Task CreateAccessRequestApprovedNotificationAsync(int userId, string companyName, string assignedRole)
    {
        var title = _localizer["NotificationAccessRequestApprovedTitle"];
        var message = string.Format(CultureInfo.CurrentCulture, _localizer["NotificationAccessRequestApprovedMessage"],
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

            // Handle midnight wrap-around (e.g., 00:05 - 15min = 23:50)
            // When lowerBound > upperBound, the window crosses midnight
            var crossesMidnight = lowerBound > upperBound;

            var userIds = await _db.DailyNotificationPreferences
                .Where(p => p.CompanyId == companyId
                         && p.IsActive
                         && p.ReceiveDailyDigest
                         && (crossesMidnight
                             ? (p.PreferredTime >= lowerBound || p.PreferredTime <= upperBound)
                             : (p.PreferredTime >= lowerBound && p.PreferredTime <= upperBound)))
                .Select(p => p.UserId)
                .ToListAsync();

            LogDigestUsersFound(_logger, userIds.Count, currentTime, companyId);

            return userIds;
        }
        catch (Exception ex)
        {
            LogDigestUsersError(_logger, ex, currentTime, companyId);
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
            var user = await GetRecipientAcrossTenantsAsync(userId);
            if (user == null || string.IsNullOrWhiteSpace(user.Email))
            {
                LogDigestUserMissing(_logger, userId);
                return false;
            }

            var preference = await _db.DailyNotificationPreferences
                .FirstOrDefaultAsync(p => p.UserId == userId && p.CompanyId == companyId && p.IsActive);

            if (preference == null || !preference.ReceiveDailyDigest)
            {
                LogDigestNoPreference(_logger, userId);
                return false;
            }

            // Render this digest under the recipient's learned language (background jobs have no
            // request culture). Null/unknown preference → inherits the job's default culture.
            using var _digestCulture = new Helpers.CultureScope(user.PreferredLanguage);

            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var nextWeek = today.AddDays(7);

            var digestParts = new List<string>();

            // Execute digest data queries sequentially (DbContext is NOT thread-safe)
            List<ShiftAssignment>? upcomingShifts = null;
            List<TimeOffRequest>? pendingTimeOff = null;
            List<SwapRequest>? pendingSwaps = null;
            List<Chore>? upcomingChores = null;
            List<OnDuty>? upcomingOnDuty = null;

            // Upcoming Shifts (next 7 days)
            if (preference.IncludeUpcomingShifts)
            {
                upcomingShifts = await _db.ShiftAssignments
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
                pendingTimeOff = await _db.TimeOffRequests
                    .Where(r => r.UserId == userId
                             && r.CompanyId == companyId
                             && r.Status == RequestStatus.Pending)
                    .OrderBy(r => r.StartDate)
                    .Take(5)
                    .ToListAsync();

                pendingSwaps = await _db.SwapRequests
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
                upcomingChores = await _db.Chores
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
                upcomingOnDuty = await _db.OnDuties
                    .Where(od => od.UserId == userId
                              && od.Date >= today
                              && od.Date <= nextWeek
                              && od.CanceledAt == null)
                    .OrderBy(od => od.Date)
                    .Take(10)
                    .ToListAsync();
            }

            // Build digest content from results
            if (upcomingShifts != null)
            {
                if (upcomingShifts.Any())
                {
                    var shiftsHtml = $"<h3>{_localizer["Email_UpcomingShifts"]}</h3><ul>";
                    foreach (var shift in upcomingShifts)
                    {
                        var shiftTypeName = await _companyLocalizationService.ResolveShiftTypeNameAsync(shift.ShiftInstance.ShiftType, companyId, "he-IL");
                        shiftsHtml += $"<li><strong>{_localization.FormatMediumDate(shift.ShiftInstance.WorkDate)}</strong> - " +
                                    $"{WebUtility.HtmlEncode(shiftTypeName)} " +
                                    $"({_localization.FormatTime(shift.ShiftInstance.ShiftType.Start)} - {_localization.FormatTime(shift.ShiftInstance.ShiftType.End)})</li>";
                    }
                    shiftsHtml += "</ul>";
                    digestParts.Add(shiftsHtml);
                }
            }

            if (pendingTimeOff != null || pendingSwaps != null)
            {
                var pendingTimeOffList = pendingTimeOff ?? new List<TimeOffRequest>();
                var pendingSwapsList = pendingSwaps ?? new List<SwapRequest>();

                if (pendingTimeOffList.Any() || pendingSwapsList.Any())
                {
                    var requestsHtml = $"<h3>{_localizer["Email_PendingRequests"]}</h3>";

                    if (pendingTimeOffList.Any())
                    {
                        requestsHtml += $"<h4>{_localizer["Email_TimeOffRequests"]}</h4><ul>";
                        foreach (var request in pendingTimeOffList)
                        {
                            var dateRange = request.StartDate == request.EndDate
                                ? _localization.FormatMediumDate(request.StartDate)
                                : $"{_localization.FormatMediumDate(request.StartDate)} - {_localization.FormatMediumDate(request.EndDate)}";
                            requestsHtml += $"<li>{dateRange} - <em>{_localizer["Email_PendingApproval"]}</em></li>";
                        }
                        requestsHtml += "</ul>";
                    }

                    if (pendingSwapsList.Any())
                    {
                        requestsHtml += $"<h4>{_localizer["Email_SwapRequests"]}</h4><ul>";
                        foreach (var swap in pendingSwapsList)
                        {
                            var shiftInstance = swap.FromAssignment?.ShiftInstance;
                            string shiftInfo;
                            if (shiftInstance != null)
                            {
                                var swapShiftTypeName = shiftInstance.ShiftType != null
                                    ? await _companyLocalizationService.ResolveShiftTypeNameAsync(shiftInstance.ShiftType, companyId, "he-IL")
                                    : _localizer["Email_Shift"].Value;
                                shiftInfo = $"{_localization.FormatMediumDate(shiftInstance.WorkDate)} - {WebUtility.HtmlEncode(swapShiftTypeName)}";
                            }
                            else
                            {
                                shiftInfo = "Unknown shift";
                            }
                            requestsHtml += $"<li>{shiftInfo} - <em>{_localizer["Email_PendingApproval"]}</em></li>";
                        }
                        requestsHtml += "</ul>";
                    }

                    digestParts.Add(requestsHtml);
                }
            }

            if (upcomingChores != null)
            {
                if (upcomingChores.Any())
                {
                    var choresHtml = $"<h3>{_localizer["Email_AssignedChores"]}</h3><ul>";
                    foreach (var chore in upcomingChores)
                    {
                        choresHtml += $"<li><strong>{_localization.FormatMediumDate(chore.Date)}</strong> - {WebUtility.HtmlEncode(chore.Title)}</li>";
                    }
                    choresHtml += "</ul>";
                    digestParts.Add(choresHtml);
                }
            }

            if (upcomingOnDuty != null)
            {
                if (upcomingOnDuty.Any())
                {
                    var onDutyHtml = $"<h3>{_localizer["Email_OnDutyAssignments"]}</h3><ul>";
                    foreach (var od in upcomingOnDuty)
                    {
                        var typeName = od.Type == OnDutyType.Hakam
                            ? _localizer["OnDutyTypeHakam"]
                            : _localizer["OnDutyTypeLead"];
                        onDutyHtml += $"<li><strong>{_localization.FormatMediumDate(od.Date)}</strong> - {typeName}</li>";
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
                        var todaysRole = string.Format(CultureInfo.CurrentCulture, _localizer["Email_TodaysRole"],
                                        $"<strong>{WebUtility.HtmlEncode(roleName)}</strong>",
                                        $"<a href='{WebUtility.HtmlEncode(baseUrl)}/My/Profile?userId={od.UserId}'>{WebUtility.HtmlEncode(od.User?.DisplayName ?? "Unknown")}</a>");
                        todayHtml += $"<li>{todaysRole}</li>";
                    }
                    todayHtml += "</ul>";
                    digestParts.Add(todayHtml);
                }
            }

            // If no content, don't send email
            if (!digestParts.Any())
            {
                LogDigestNoContent(_logger, userId);
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
    <h2>{string.Format(CultureInfo.CurrentCulture, _localizer["Email_DailyDigestTitle"], WebUtility.HtmlEncode(user.DisplayName))}</h2>
    <p>{_localizer["Email_DailyDigestIntro"]}</p>
    {string.Join("\n", digestParts)}
    <div class='footer'>
        <p>{_localizer["Email_DailyDigestFooter"]}</p>
        <p><em>{string.Format(CultureInfo.CurrentCulture, _localizer["Email_SentAt"], _localization.FormatDateTime(DateTime.UtcNow))}</em></p>
    </div>
</body>
</html>";

            var subject = string.Format(CultureInfo.CurrentCulture, _localizer["Email_DailyDigestSubject"], _localization.FormatMediumDate(today));

            var sendResult = await _mailService.SendMailAsync(user.Email, subject, emailBody);

            if (sendResult.Success)
            {
                LogDigestSent(_logger, userId, user.Email);
            }
            else
            {
                LogDigestSendFailed(_logger, userId, user.Email, sendResult.ErrorMessage);
            }

            return sendResult.Success;
        }
        catch (Exception ex)
        {
            LogDigestError(_logger, ex, userId, companyId);
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
            var user = await GetRecipientAcrossTenantsAsync(userId);
            if (user == null || string.IsNullOrWhiteSpace(user.Email))
            {
                LogDayBeforeUserMissing(_logger, userId);
                return;
            }

            // Get reminder preferences
            var prefs = await _db.DailyNotificationPreferences
                .FirstOrDefaultAsync(p => p.UserId == userId && p.CompanyId == companyId && p.IsActive);

            if (prefs == null || (!prefs.RemindBeforeShifts && !prefs.RemindBeforeChores && !prefs.RemindBeforeOnDuty))
            {
                LogDayBeforeNotEnabled(_logger, userId);
                return;
            }

            // Render reminders under the recipient's learned language (background jobs have no
            // request culture). Null/unknown preference → inherits the job's default culture.
            using var _reminderCulture = new Helpers.CultureScope(user.PreferredLanguage);

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
                        var shiftTypeName = shiftType != null
                            ? await _companyLocalizationService.ResolveShiftTypeNameAsync(shiftType, companyId, "he-IL")
                            : _localizer["Email_Shift"].Value;
                        var startTime = shiftType != null ? _localization.FormatTime(shiftType.Start) : "--:--";
                        var endTime = shiftType != null ? _localization.FormatTime(shiftType.End) : "--:--";
                        shiftsHtml += $"<li><strong>{WebUtility.HtmlEncode(shiftTypeName)}</strong> - {startTime} to {endTime}</li>";
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
                        choresHtml += $"<li>{WebUtility.HtmlEncode(chore.Title)}</li>";
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

                        onDutyHtml += $"<li><strong>{WebUtility.HtmlEncode(roleName)}</strong></li>";
                    }
                    onDutyHtml += "</ul>";
                    reminderParts.Add(onDutyHtml);
                }
            }

            // If no reminders, don't send email
            if (!reminderParts.Any())
            {
                LogDayBeforeNoneToSend(_logger, userId);
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
    <p>{string.Format(CultureInfo.CurrentCulture, _localizer["Email_ReminderIntro"], WebUtility.HtmlEncode(user.DisplayName), _localization.FormatMediumDate(tomorrow))}</p>
    {string.Join("\n", reminderParts)}
    <div class='footer'>
        <p>{_localizer["Email_ReminderFooter"]}</p>
        <p><em>{string.Format(CultureInfo.CurrentCulture, _localizer["Email_SentAt"], _localization.FormatDateTime(DateTime.UtcNow))}</em></p>
    </div>
</body>
</html>";

            var subject = string.Format(CultureInfo.CurrentCulture, _localizer["Email_ReminderSubject"], _localization.FormatMediumDate(tomorrow));

            var sendResult = await _mailService.SendMailAsync(user.Email, subject, emailBody);

            if (sendResult.Success)
            {
                LogDayBeforeSent(_logger, userId, user.Email);
            }
            else
            {
                LogDayBeforeSendFailed(_logger, userId, user.Email, sendResult.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            LogDayBeforeError(_logger, ex, userId, companyId);
        }
    }

    // ============= Ops Console Scheduler: Trainee Notifications =============

    public async Task CreateTraineeAddedNotificationAsync(int primaryUserId, int traineeUserId, string traineeName, string shiftTypeName, DateOnly date, TimeOnly start, TimeOnly end)
    {
        var title = _localizer["NotificationTraineeAddedTitle"];
        var message = string.Format(CultureInfo.CurrentCulture, _localizer["NotificationTraineeAddedMessage"],
            traineeName, shiftTypeName, _localization.FormatMediumDate(date), _localization.FormatTime(start), _localization.FormatTime(end));

        await CreateNotificationAsync(primaryUserId, NotificationType.EmployeeTraineeAdded, title, message, null, "ShiftAssignment");

        // Send email notification
        try
        {
            var user = await GetRecipientAcrossTenantsAsync(primaryUserId);
            if (user != null && !string.IsNullOrWhiteSpace(user.Email)
                && await ShouldEmailAsync(primaryUserId, user.CompanyId, Notifications.NotificationCategory.Trainee, personallyActionable: true))
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
            LogEmailErrorTraineeAdded(_logger, ex, primaryUserId);
            // Don't throw - email failure should not block notification creation
        }
    }

    public async Task CreateTraineeChangedNotificationAsync(int primaryUserId, int oldTraineeUserId, int newTraineeUserId, string oldTraineeName, string newTraineeName, string shiftTypeName, DateOnly date)
    {
        var title = _localizer["NotificationTraineeChangedTitle"];
        var message = string.Format(CultureInfo.CurrentCulture, _localizer["NotificationTraineeChangedMessage"],
            shiftTypeName, _localization.FormatMediumDate(date), oldTraineeName, newTraineeName);

        await CreateNotificationAsync(primaryUserId, NotificationType.EmployeeTraineeAdded, title, message, null, "ShiftAssignment");

        // Notify old and new trainees
        var removedTitle = _localizer["NotificationTraineeAssignmentRemovedTitle"];
        var removedMessage = string.Format(CultureInfo.CurrentCulture, _localizer["NotificationTraineeAssignmentRemovedMessage"], shiftTypeName, _localization.FormatMediumDate(date));
        await CreateNotificationAsync(oldTraineeUserId, NotificationType.TraineeShadowingRemoved, removedTitle, removedMessage, null, "ShiftAssignment");

        var addedTitle = _localizer["NotificationNewTraineeAssignmentTitle"];
        var addedMessage = string.Format(CultureInfo.CurrentCulture, _localizer["NotificationNewTraineeAssignmentMessage"], shiftTypeName, _localization.FormatMediumDate(date));
        await CreateNotificationAsync(newTraineeUserId, NotificationType.TraineeShadowingAdded, addedTitle, addedMessage, null, "ShiftAssignment");
    }

    public async Task CreateTraineeRemovedNotificationAsync(int primaryUserId, int traineeUserId, string traineeName, string shiftTypeName, DateOnly date)
    {
        var title = _localizer["NotificationTraineeRemovedTitle"];
        var message = string.Format(CultureInfo.CurrentCulture, _localizer["NotificationTraineeRemovedMessage"],
            traineeName, shiftTypeName, _localization.FormatMediumDate(date));

        await CreateNotificationAsync(primaryUserId, NotificationType.EmployeeTraineeRemoved, title, message, null, "ShiftAssignment");

        // Notify trainee
        var traineeTitle = _localizer["NotificationTraineeAssignmentRemovedTitle"];
        var traineeMessage = string.Format(CultureInfo.CurrentCulture, _localizer["NotificationTraineeAssignmentRemovedMessage"], shiftTypeName, _localization.FormatMediumDate(date));
        await CreateNotificationAsync(traineeUserId, NotificationType.TraineeShadowingRemoved, traineeTitle, traineeMessage, null, "ShiftAssignment");
    }

    // ============= Ops Console Scheduler: Staffing Change Notifications =============

    public async Task CreateSlotRemovedNotificationAsync(int affectedUserId, string shiftTypeName, DateOnly date, TimeOnly start, TimeOnly end, string reason)
    {
        var title = _localizer["NotificationSlotRemovedTitle"];
        var message = string.Format(CultureInfo.CurrentCulture, _localizer["NotificationSlotRemovedMessage"],
            shiftTypeName, _localization.FormatMediumDate(date), _localization.FormatTime(start), _localization.FormatTime(end), reason);

        await CreateNotificationAsync(affectedUserId, NotificationType.ShiftRemoved, title, message, null, "ShiftAssignment");

        // Send email notification
        try
        {
            var user = await GetRecipientAcrossTenantsAsync(affectedUserId);
            if (user != null && !string.IsNullOrWhiteSpace(user.Email)
                && await ShouldEmailAsync(affectedUserId, user.CompanyId, Notifications.NotificationCategory.ShiftChange, personallyActionable: true))
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
            LogEmailErrorSlotRemoved(_logger, ex, affectedUserId);
            // Don't throw - email failure should not block notification creation
        }
    }

    // ============= Ops Console Scheduler: Shift Modification Notifications =============

    public async Task CreateShiftModifiedNotificationAsync(List<int> assignedUserIds, string shiftTypeName, DateOnly date, string changeDescription)
    {
        var title = _localizer["NotificationShiftModifiedTitle"];
        var message = string.Format(CultureInfo.CurrentCulture, _localizer["NotificationShiftModifiedMessage"],
            shiftTypeName, _localization.FormatMediumDate(date), changeDescription);

        foreach (var userId in assignedUserIds)
        {
            await CreateNotificationAsync(userId, NotificationType.ShiftAdded, title, message, null, "ShiftAssignment");

            // Send email notification
            try
            {
                var user = await GetRecipientAcrossTenantsAsync(userId);
                if (user != null && !string.IsNullOrWhiteSpace(user.Email)
                    && await ShouldEmailAsync(userId, user.CompanyId, Notifications.NotificationCategory.ShiftChange, personallyActionable: true))
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
                LogEmailErrorShiftModified(_logger, ex, userId);
                // Don't throw - email failure should not block notification creation
            }
        }
    }

    // ========================================
    // Diagnostic / Admin helpers (additive — see interface XML docs).
    //
    // These methods reuse the same persistence path as CreateNotificationAsync but
    // map known failure modes (recipient missing, DB error) to localized
    // OperationResult.Fail outcomes. They MUST NOT be wired into the 30+ existing
    // fire-and-forget callers — those still want bool/void semantics so a notification
    // failure cannot block a shift assignment / chore / on-duty action.
    // ========================================

    /// <inheritdoc />
    public async Task<OperationResult> TryCreateNotificationWithDiagnosticsAsync(
        int recipientUserId, string title, string body, string? linkUrl = null)
    {
        // Treat blank title/body as a "template missing" condition — these are the
        // two fields that MUST be populated for a notification row to be meaningful.
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(body))
        {
            LogTryCreateMissingContent(_logger, recipientUserId);
            return OperationResult.Fail(
                "Error_NotificationService_TemplateMissing",
                _localizer["Error_NotificationService_TemplateMissing"].Value);
        }

        int? companyId = null;
        try
        {
            // Same cross-tenant lookup as CreateNotificationAsync — see the SECURITY-AUDITED
            // note above the class for why IgnoreQueryFilters is safe here.
            var recipient = await GetRecipientAcrossTenantsAsync(recipientUserId);
            if (recipient == null)
            {
                LogTryCreateRecipientNotFound(_logger, recipientUserId);
                return OperationResult.Fail(
                    "Error_NotificationService_RecipientNotFound",
                    _localizer["Error_NotificationService_RecipientNotFound"].Value);
            }
            companyId = recipient.CompanyId;

            // linkUrl is stored in RelatedEntityType so the in-app notification UI can
            // surface a click-through target without an entity lookup. This mirrors the
            // free-form usage of RelatedEntityType in the existing creators (e.g. "Chore",
            // "ShiftAssignment") — diagnostic notifications are not tied to a domain entity.
            var notification = new UserNotification
            {
                CompanyId = companyId.Value,
                UserId = recipientUserId,
                Type = NotificationType.ShiftAdded, // closest neutral type; see TrySendTest below
                Title = title,
                Message = body,
                IsRead = false,
                CreatedAt = DateTime.UtcNow,
                RelatedEntityId = null,
                RelatedEntityType = string.IsNullOrWhiteSpace(linkUrl) ? null : linkUrl
            };

            _db.UserNotifications.Add(notification);
            await _db.SaveChangesAsync();

            LogTryCreateSucceeded(_logger, recipientUserId, companyId, title);
            return OperationResult.Ok();
        }
        catch (DbUpdateException ex)
        {
            LogTryCreateDbError(_logger, ex, recipientUserId, companyId);
            return OperationResult.Fail(
                "Error_NotificationService_DispatchFailed",
                _localizer["Error_NotificationService_DispatchFailed"].Value);
        }
        catch (Exception ex)
        {
            LogTryCreateUnexpectedError(_logger, ex, recipientUserId, companyId);
            return OperationResult.Fail(
                "Error_NotificationService_DispatchFailed",
                _localizer["Error_NotificationService_DispatchFailed"].Value);
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult> TrySendTestNotificationAsync(int recipientUserId)
    {
        // Reuse the localized "Notification_Test*" pair if present, otherwise fall back to
        // hard-coded English strings — these are admin-tool labels, not end-user content.
        // The localizer returns the key name itself when a key is missing, so we explicitly
        // check ResourceNotFound to provide a sensible default.
        var titleEntry = _localizer["Notification_Test_Title"];
        var messageEntry = _localizer["Notification_Test_Message"];
        var title = titleEntry.ResourceNotFound ? "Test notification" : titleEntry.Value;
        var body = messageEntry.ResourceNotFound
            ? "This is a test notification confirming that your account can receive notifications from ShiftManager."
            : messageEntry.Value;

        return await TryCreateNotificationWithDiagnosticsAsync(recipientUserId, title, body);
    }
}