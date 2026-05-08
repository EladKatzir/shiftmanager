using Microsoft.Extensions.Logging;
using ShiftManager.Models.Support;

namespace ShiftManager.Pages.Requests;

// Source-generated LoggerMessage delegates for IndexModel (closes 42 sites of F-C-013).
// EventId range 5000-5099 reserved (reallocated from MailService — that file is Batch O scope
// and will use a future range when its god-class decomposition starts). Range allocations:
//   5000-5019: Page lifecycle (11 methods, 11 sites — each unique)
//   5020-5029: Common validation/auth errors (5 methods, 14 sites — most repeated 2-5x)
//   5030-5039: Authorization + concurrency + manager-grant (4 methods, 9 sites — uses
//              Action+EntityType parameterization to collapse 4 SECURITY + 4 CONCURRENCY
//              literals into 2 methods each)
//   5040-5049: Delete approved time-off flow (8 methods, 8 sites — each unique)
public partial class IndexModel
{
    // ── Page lifecycle ─────────────────────────────────────────────────────

    [LoggerMessage(EventId = 5000, Level = LogLevel.Information,
        Message = "Loading requests page")]
    private static partial void LogLoadingRequestsPage(ILogger logger);

    [LoggerMessage(EventId = 5001, Level = LogLevel.Information,
        Message = "Loading own requests for employee {UserId}")]
    private static partial void LogLoadingOwnRequests(ILogger logger, int userId);

    [LoggerMessage(EventId = 5002, Level = LogLevel.Information,
        Message = "Loaded {TimeOffCount} pending, {SwapCount} swaps, {ApprovedCount} approved for employee")]
    private static partial void LogLoadedEmployeeSummary(ILogger logger, int timeOffCount, int swapCount, int approvedCount);

    [LoggerMessage(EventId = 5003, Level = LogLevel.Information,
        Message = "Loading pending time off requests")]
    private static partial void LogLoadingPendingTimeOff(ILogger logger);

    [LoggerMessage(EventId = 5004, Level = LogLevel.Information,
        Message = "Loaded {Count} pending time off requests")]
    private static partial void LogLoadedPendingTimeOff(ILogger logger, int count);

    [LoggerMessage(EventId = 5005, Level = LogLevel.Information,
        Message = "Loading pending swap requests")]
    private static partial void LogLoadingPendingSwaps(ILogger logger);

    [LoggerMessage(EventId = 5006, Level = LogLevel.Information,
        Message = "Loaded {Count} pending swap requests")]
    private static partial void LogLoadedPendingSwaps(ILogger logger, int count);

    [LoggerMessage(EventId = 5007, Level = LogLevel.Information,
        Message = "Loading approved time off requests")]
    private static partial void LogLoadingApprovedTimeOff(ILogger logger);

    [LoggerMessage(EventId = 5008, Level = LogLevel.Information,
        Message = "Loaded {Count} approved time off requests")]
    private static partial void LogLoadedApprovedTimeOff(ILogger logger, int count);

    [LoggerMessage(EventId = 5009, Level = LogLevel.Information,
        Message = "Admin requests page loaded successfully")]
    private static partial void LogAdminPageLoaded(ILogger logger);

    [LoggerMessage(EventId = 5010, Level = LogLevel.Error,
        Message = "Error loading admin requests page")]
    private static partial void LogErrorLoadingAdminPage(ILogger logger, System.Exception ex);

    // ── Common validation / auth errors (multi-callsite) ───────────────────

    [LoggerMessage(EventId = 5020, Level = LogLevel.Warning,
        Message = "Invalid time off request ID: {Id}")]
    private static partial void LogInvalidTimeOffRequestId(ILogger logger, int id);

    [LoggerMessage(EventId = 5021, Level = LogLevel.Warning,
        Message = "Time off request {RequestId} not found")]
    private static partial void LogTimeOffRequestNotFound(ILogger logger, int requestId);

    [LoggerMessage(EventId = 5022, Level = LogLevel.Warning,
        Message = "Invalid swap request ID: {Id}")]
    private static partial void LogInvalidSwapRequestId(ILogger logger, int id);

    [LoggerMessage(EventId = 5023, Level = LogLevel.Warning,
        Message = "Swap request {RequestId} not found")]
    private static partial void LogSwapRequestNotFound(ILogger logger, int requestId);

    [LoggerMessage(EventId = 5024, Level = LogLevel.Error,
        Message = "Invalid or missing NameIdentifier claim")]
    private static partial void LogInvalidNameIdentifierClaim(ILogger logger);

    // ── Authorization / concurrency (parameterized Action+EntityType) ─────

    // Covers SECURITY warnings for: approve+TimeOff, decline+TimeOff, approve+Swap, decline+Swap.
    // Old literals embedded the verb and entity name; new template uses {Action} and {EntityType}
    // placeholders. Console output identical; structured-log consumers gain filterable properties.
    [LoggerMessage(EventId = 5030, Level = LogLevel.Warning,
        Message = "SECURITY: User {UserId} ({Role}) attempted to {Action} {EntityType} request {RequestId} for unauthorized company {CompanyId}")]
    private static partial void LogSecurityUnauthorizedAction(ILogger logger, int userId, UserRole role, string action, string entityType, int requestId, int companyId);

    [LoggerMessage(EventId = 5031, Level = LogLevel.Warning,
        Message = "CONCURRENCY: User {UserId} attempted to {Action} {EntityType} request {RequestId} with status {Status} (expected Pending)")]
    private static partial void LogConcurrencyAlreadyProcessed(ILogger logger, int userId, string action, string entityType, int requestId, RequestStatus status);

    [LoggerMessage(EventId = 5032, Level = LogLevel.Error,
        Message = "Side effects failed for approved time-off request {RequestId}. Manual remediation may be needed.")]
    private static partial void LogTimeOffSideEffectsFailed(ILogger logger, System.Exception ex, int requestId);

    [LoggerMessage(EventId = 5033, Level = LogLevel.Warning,
        Message = "SECURITY: User {UserId} attempted manager action on Requests page without ManagerHomeAccess grant")]
    private static partial void LogManagerActionWithoutGrant(ILogger logger, int userId);

    // ── Delete approved time-off flow (each unique, all in OnPostDeleteAsync) ──

    [LoggerMessage(EventId = 5040, Level = LogLevel.Information,
        Message = "Admin attempting to delete approved time-off request {RequestId}")]
    private static partial void LogAdminAttemptDeleteTimeOff(ILogger logger, int requestId);

    [LoggerMessage(EventId = 5041, Level = LogLevel.Warning,
        Message = "Time-off request {RequestId} not found")]
    private static partial void LogTimeOffNotFoundForDelete(ILogger logger, int requestId);

    [LoggerMessage(EventId = 5042, Level = LogLevel.Warning,
        Message = "SECURITY: User {UserId} ({Role}) attempted to delete time-off {RequestId} for unauthorized company")]
    private static partial void LogSecurityUnauthorizedDelete(ILogger logger, int userId, UserRole role, int requestId);

    [LoggerMessage(EventId = 5043, Level = LogLevel.Warning,
        Message = "Attempt to delete non-approved time-off request {RequestId} with status {Status}")]
    private static partial void LogAttemptDeleteNonApproved(ILogger logger, int requestId, RequestStatus status);

    [LoggerMessage(EventId = 5044, Level = LogLevel.Warning,
        Message = "Attempt to delete time-off request {RequestId} that has already started")]
    private static partial void LogAttemptDeleteAlreadyStarted(ILogger logger, int requestId);

    [LoggerMessage(EventId = 5045, Level = LogLevel.Information,
        Message = "Deleting approved time-off request {RequestId} for user {UserName} ({StartDate} to {EndDate})")]
    private static partial void LogDeletingTimeOff(ILogger logger, int requestId, string userName, System.DateOnly startDate, System.DateOnly endDate);

    [LoggerMessage(EventId = 5046, Level = LogLevel.Information,
        Message = "Successfully deleted time-off request {RequestId} for user {UserName}")]
    private static partial void LogSuccessfullyDeletedTimeOff(ILogger logger, int requestId, string userName);

    [LoggerMessage(EventId = 5047, Level = LogLevel.Error,
        Message = "Error deleting time-off request {RequestId}")]
    private static partial void LogErrorDeletingTimeOff(ILogger logger, System.Exception ex, int requestId);
}
