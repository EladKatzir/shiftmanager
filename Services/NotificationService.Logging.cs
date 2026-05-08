using Microsoft.Extensions.Logging;
using ShiftManager.Models.Support;

namespace ShiftManager.Services;

// Source-generated LoggerMessage delegates for NotificationService (closes 39 sites of F-C-013).
// EventId range 2000-2099 reserved for NotificationService. Range allocations:
//   2000-2009: CreateNotificationAsync core (4 used)
//   2010-2019: Email fan-out errors per notification type (9 used)
//   2020-2029: Access request notifications (4 used)
//   2030-2039: Daily digest (8 used)
//   2040-2049: Day-before reminders (6 used)
//   2050-2059: Ops Console scheduler notifications (3 used)
//   2060-2069: TryCreate diagnostic helpers (5 used)
public partial class NotificationService
{
    // ── CreateNotificationAsync core ───────────────────────────────────────

    [LoggerMessage(EventId = 2000, Level = LogLevel.Warning,
        Message = "CreateNotificationAsync: recipient {UserId} not found")]
    private static partial void LogCreateRecipientNotFound(ILogger logger, int userId);

    [LoggerMessage(EventId = 2001, Level = LogLevel.Information,
        Message = "Created notification {Type} for user {UserId}: {Title}")]
    private static partial void LogNotificationCreated(ILogger logger, NotificationType type, int userId, string title);

    [LoggerMessage(EventId = 2002, Level = LogLevel.Error,
        Message = "Database error creating notification. CompanyId={CompanyId}, Type={Type}, UserId={UserId}, Title={Title}")]
    private static partial void LogCreateDbError(ILogger logger, System.Exception ex, int? companyId, NotificationType type, int userId, string title);

    [LoggerMessage(EventId = 2003, Level = LogLevel.Error,
        Message = "Unexpected error creating notification. CompanyId={CompanyId}, Type={Type}, UserId={UserId}, Title={Title}")]
    private static partial void LogCreateUnexpectedError(ILogger logger, System.Exception ex, int? companyId, NotificationType type, int userId, string title);

    // ── Email fan-out errors (per notification type) ───────────────────────

    [LoggerMessage(EventId = 2010, Level = LogLevel.Error,
        Message = "Error sending shift assigned email to user {UserId}")]
    private static partial void LogEmailErrorShiftAssigned(ILogger logger, System.Exception ex, int userId);

    [LoggerMessage(EventId = 2011, Level = LogLevel.Error,
        Message = "Error sending shift removed email to user {UserId}")]
    private static partial void LogEmailErrorShiftRemoved(ILogger logger, System.Exception ex, int userId);

    [LoggerMessage(EventId = 2012, Level = LogLevel.Error,
        Message = "Error sending time-off {Status} email to user {UserId}")]
    private static partial void LogEmailErrorTimeOff(ILogger logger, System.Exception ex, RequestStatus status, int userId);

    [LoggerMessage(EventId = 2013, Level = LogLevel.Error,
        Message = "Error sending swap request {Status} email to user {UserId}")]
    private static partial void LogEmailErrorSwapRequest(ILogger logger, System.Exception ex, RequestStatus status, int userId);

    [LoggerMessage(EventId = 2014, Level = LogLevel.Error,
        Message = "Error sending chore assigned email to user {UserId}")]
    private static partial void LogEmailErrorChoreAssigned(ILogger logger, System.Exception ex, int userId);

    [LoggerMessage(EventId = 2015, Level = LogLevel.Error,
        Message = "Error sending chore canceled email to user {UserId}")]
    private static partial void LogEmailErrorChoreCanceled(ILogger logger, System.Exception ex, int userId);

    [LoggerMessage(EventId = 2016, Level = LogLevel.Error,
        Message = "Error sending on-duty assigned email to user {UserId}")]
    private static partial void LogEmailErrorOnDutyAssigned(ILogger logger, System.Exception ex, int userId);

    [LoggerMessage(EventId = 2017, Level = LogLevel.Error,
        Message = "Error sending on-duty canceled email to user {UserId}")]
    private static partial void LogEmailErrorOnDutyCanceled(ILogger logger, System.Exception ex, int userId);

    [LoggerMessage(EventId = 2018, Level = LogLevel.Error,
        Message = "Error sending time-off deleted email to user {UserId}")]
    private static partial void LogEmailErrorTimeOffDeleted(ILogger logger, System.Exception ex, int userId);

    // ── Access request notifications ───────────────────────────────────────

    [LoggerMessage(EventId = 2020, Level = LogLevel.Warning,
        Message = "No owner users found to notify about access request {RequestId}")]
    private static partial void LogAccessRequestNoOwners(ILogger logger, int requestId);

    [LoggerMessage(EventId = 2021, Level = LogLevel.Error,
        Message = "Error sending access request email to owner {UserId}")]
    private static partial void LogEmailErrorAccessRequest(ILogger logger, System.Exception ex, int userId);

    [LoggerMessage(EventId = 2022, Level = LogLevel.Information,
        Message = "Notified {OwnerCount} owners about access request {RequestId} from {RequesterEmail}")]
    private static partial void LogAccessRequestNotified(ILogger logger, int ownerCount, int requestId, string requesterEmail);

    [LoggerMessage(EventId = 2023, Level = LogLevel.Error,
        Message = "Error notifying owners about access request {RequestId}")]
    private static partial void LogAccessRequestNotifyError(ILogger logger, System.Exception ex, int requestId);

    // ── Daily digest ───────────────────────────────────────────────────────

    [LoggerMessage(EventId = 2030, Level = LogLevel.Information,
        Message = "Found {Count} users for daily digest at {Time} for company {CompanyId}")]
    private static partial void LogDigestUsersFound(ILogger logger, int count, System.TimeOnly time, int companyId);

    [LoggerMessage(EventId = 2031, Level = LogLevel.Error,
        Message = "Error getting users for daily digest at {Time} for company {CompanyId}")]
    private static partial void LogDigestUsersError(ILogger logger, System.Exception ex, System.TimeOnly time, int companyId);

    [LoggerMessage(EventId = 2032, Level = LogLevel.Warning,
        Message = "User {UserId} not found or has no email address")]
    private static partial void LogDigestUserMissing(ILogger logger, int userId);

    [LoggerMessage(EventId = 2033, Level = LogLevel.Information,
        Message = "User {UserId} has no active daily digest preference")]
    private static partial void LogDigestNoPreference(ILogger logger, int userId);

    [LoggerMessage(EventId = 2034, Level = LogLevel.Information,
        Message = "No digest content for user {UserId}, skipping email")]
    private static partial void LogDigestNoContent(ILogger logger, int userId);

    [LoggerMessage(EventId = 2035, Level = LogLevel.Information,
        Message = "Successfully sent daily digest to user {UserId} ({Email})")]
    private static partial void LogDigestSent(ILogger logger, int userId, string email);

    [LoggerMessage(EventId = 2036, Level = LogLevel.Warning,
        Message = "Failed to send daily digest to user {UserId} ({Email}): {Reason}")]
    private static partial void LogDigestSendFailed(ILogger logger, int userId, string email, string? reason);

    [LoggerMessage(EventId = 2037, Level = LogLevel.Error,
        Message = "Error sending daily digest to user {UserId} in company {CompanyId}")]
    private static partial void LogDigestError(ILogger logger, System.Exception ex, int userId, int companyId);

    // ── Day-before reminders ───────────────────────────────────────────────

    [LoggerMessage(EventId = 2040, Level = LogLevel.Warning,
        Message = "User {UserId} not found or has no email")]
    private static partial void LogDayBeforeUserMissing(ILogger logger, int userId);

    [LoggerMessage(EventId = 2041, Level = LogLevel.Debug,
        Message = "User {UserId} has no day-before reminders enabled")]
    private static partial void LogDayBeforeNotEnabled(ILogger logger, int userId);

    [LoggerMessage(EventId = 2042, Level = LogLevel.Information,
        Message = "No day-before reminders for user {UserId}")]
    private static partial void LogDayBeforeNoneToSend(ILogger logger, int userId);

    [LoggerMessage(EventId = 2043, Level = LogLevel.Information,
        Message = "Successfully sent day-before reminders to user {UserId} ({Email})")]
    private static partial void LogDayBeforeSent(ILogger logger, int userId, string email);

    [LoggerMessage(EventId = 2044, Level = LogLevel.Warning,
        Message = "Failed to send day-before reminders to user {UserId} ({Email}): {Reason}")]
    private static partial void LogDayBeforeSendFailed(ILogger logger, int userId, string email, string? reason);

    [LoggerMessage(EventId = 2045, Level = LogLevel.Error,
        Message = "Error sending day-before reminders to user {UserId} in company {CompanyId}")]
    private static partial void LogDayBeforeError(ILogger logger, System.Exception ex, int userId, int companyId);

    // ── Ops Console scheduler notifications ────────────────────────────────

    [LoggerMessage(EventId = 2050, Level = LogLevel.Error,
        Message = "Error sending trainee added email to user {UserId}")]
    private static partial void LogEmailErrorTraineeAdded(ILogger logger, System.Exception ex, int userId);

    [LoggerMessage(EventId = 2051, Level = LogLevel.Error,
        Message = "Error sending slot removed email to user {UserId}")]
    private static partial void LogEmailErrorSlotRemoved(ILogger logger, System.Exception ex, int userId);

    [LoggerMessage(EventId = 2052, Level = LogLevel.Error,
        Message = "Error sending shift modified email to user {UserId}")]
    private static partial void LogEmailErrorShiftModified(ILogger logger, System.Exception ex, int userId);

    // ── TryCreate diagnostic helpers ───────────────────────────────────────

    [LoggerMessage(EventId = 2060, Level = LogLevel.Warning,
        Message = "TryCreateNotificationWithDiagnosticsAsync: missing title/body for recipient {UserId}")]
    private static partial void LogTryCreateMissingContent(ILogger logger, int userId);

    [LoggerMessage(EventId = 2061, Level = LogLevel.Warning,
        Message = "TryCreateNotificationWithDiagnosticsAsync: recipient {UserId} not found")]
    private static partial void LogTryCreateRecipientNotFound(ILogger logger, int userId);

    [LoggerMessage(EventId = 2062, Level = LogLevel.Information,
        Message = "Diagnostic notification created for user {UserId} (CompanyId={CompanyId}): {Title}")]
    private static partial void LogTryCreateSucceeded(ILogger logger, int userId, int? companyId, string title);

    [LoggerMessage(EventId = 2063, Level = LogLevel.Error,
        Message = "TryCreateNotificationWithDiagnosticsAsync: DB error for recipient {UserId} (CompanyId={CompanyId})")]
    private static partial void LogTryCreateDbError(ILogger logger, System.Exception ex, int userId, int? companyId);

    [LoggerMessage(EventId = 2064, Level = LogLevel.Error,
        Message = "TryCreateNotificationWithDiagnosticsAsync: unexpected error for recipient {UserId} (CompanyId={CompanyId})")]
    private static partial void LogTryCreateUnexpectedError(ILogger logger, System.Exception ex, int userId, int? companyId);
}
