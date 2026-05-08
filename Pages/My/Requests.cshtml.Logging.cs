using Microsoft.Extensions.Logging;
using ShiftManager.Models.Support;

namespace ShiftManager.Pages.My;

// Source-generated LoggerMessage delegates for RequestsModel (closes 38 sites of F-C-013).
// EventId range 8000-8099 reserved (companion to Pages/Requests/Index.cshtml.cs at 5000-5099 —
// admin view of the same request domain). Range allocations:
//   8000-8019: OnGetAsync lifecycle (14 unique methods, 14 sites)
//   8020-8029: Common (claim-missing, shared across all 4 handlers; 1 method, 4 sites)
//   8030-8039: OnPostTimeOffAsync (7 methods, 7 sites)
//   8050-8069: OnPostSwapAsync (8 methods, 8 sites)
//   8070-8079: OnPostCancelTimeOffAsync (5 methods, 5 sites)
public partial class RequestsModel
{
    // ── OnGetAsync lifecycle ───────────────────────────────────────────────

    [LoggerMessage(EventId = 8000, Level = LogLevel.Information,
        Message = "Starting OnGetAsync for requests page")]
    private static partial void LogStartingOnGet(ILogger logger);

    [LoggerMessage(EventId = 8002, Level = LogLevel.Information,
        Message = "User ID: {UserId}")]
    private static partial void LogUserId(ILogger logger, int userId);

    [LoggerMessage(EventId = 8003, Level = LogLevel.Information,
        Message = "Loading time off requests for user {UserId}")]
    private static partial void LogLoadingTimeOff(ILogger logger, int userId);

    [LoggerMessage(EventId = 8004, Level = LogLevel.Information,
        Message = "Loaded {Count} time off requests for user {UserId}")]
    private static partial void LogLoadedTimeOff(ILogger logger, int count, int userId);

    [LoggerMessage(EventId = 8005, Level = LogLevel.Information,
        Message = "Loading shift assignments for user {UserId}")]
    private static partial void LogLoadingAssignments(ILogger logger, int userId);

    [LoggerMessage(EventId = 8006, Level = LogLevel.Information,
        Message = "Found {Count} shift assignments for user {UserId}")]
    private static partial void LogFoundAssignments(ILogger logger, int count, int userId);

    [LoggerMessage(EventId = 8007, Level = LogLevel.Information,
        Message = "Loading swap requests for user assignments")]
    private static partial void LogLoadingSwapRequests(ILogger logger);

    [LoggerMessage(EventId = 8008, Level = LogLevel.Information,
        Message = "Loaded {Count} swap requests for user {UserId}")]
    private static partial void LogLoadedSwapRequests(ILogger logger, int count, int userId);

    [LoggerMessage(EventId = 8009, Level = LogLevel.Information,
        Message = "Loading available shifts for swapping for user {UserId}")]
    private static partial void LogLoadingAvailableShifts(ILogger logger, int userId);

    [LoggerMessage(EventId = 8010, Level = LogLevel.Information,
        Message = "Loaded {Count} available shifts for user {UserId}")]
    private static partial void LogLoadedAvailableShifts(ILogger logger, int count, int userId);

    [LoggerMessage(EventId = 8011, Level = LogLevel.Information,
        Message = "Loading available approvers for user {UserId}")]
    private static partial void LogLoadingApprovers(ILogger logger, int userId);

    [LoggerMessage(EventId = 8012, Level = LogLevel.Information,
        Message = "Loaded {Count} available approvers for user {UserId}")]
    private static partial void LogLoadedApprovers(ILogger logger, int count, int userId);

    [LoggerMessage(EventId = 8013, Level = LogLevel.Information,
        Message = "OnGetAsync completed successfully for user {UserId}")]
    private static partial void LogOnGetCompleted(ILogger logger, int userId);

    [LoggerMessage(EventId = 8014, Level = LogLevel.Error,
        Message = "Error in OnGetAsync for requests page")]
    private static partial void LogErrorOnGet(ILogger logger, System.Exception ex);

    // ── Common (used across all 4 handlers) ────────────────────────────────

    [LoggerMessage(EventId = 8020, Level = LogLevel.Error,
        Message = "Invalid or missing NameIdentifier claim")]
    private static partial void LogInvalidNameIdentifierClaim(ILogger logger);

    // ── OnPostTimeOffAsync ─────────────────────────────────────────────────

    [LoggerMessage(EventId = 8030, Level = LogLevel.Information,
        Message = "Starting time off request submission")]
    private static partial void LogStartingTimeOffSubmit(ILogger logger);

    [LoggerMessage(EventId = 8031, Level = LogLevel.Warning,
        Message = "Time off request validation failed: End date {EndDate} is before start date {StartDate}")]
    private static partial void LogValidationEndBeforeStart(ILogger logger, System.DateOnly endDate, System.DateOnly startDate);

    [LoggerMessage(EventId = 8032, Level = LogLevel.Warning,
        Message = "Time off request model state is invalid: {Errors}")]
    private static partial void LogTimeOffModelInvalid(ILogger logger, string errors);

    [LoggerMessage(EventId = 8033, Level = LogLevel.Information,
        Message = "Time off request for user {UserId}, dates {StartDate} to {EndDate}")]
    private static partial void LogTimeOffSubmitContext(ILogger logger, int userId, System.DateOnly startDate, System.DateOnly endDate);

    [LoggerMessage(EventId = 8034, Level = LogLevel.Information,
        Message = "Time off request {RequestId} submitted successfully for user {UserId}, Type: {Type}, Approver: {ApproverId}")]
    private static partial void LogTimeOffSubmitted(ILogger logger, int requestId, int userId, TimeOffType type, int? approverId);

    [LoggerMessage(EventId = 8035, Level = LogLevel.Information,
        Message = "Vacation approval result for request {RequestId}: Success={Success}, Message={Message}")]
    private static partial void LogVacationApprovalResult(ILogger logger, int requestId, bool success, string? message);

    [LoggerMessage(EventId = 8036, Level = LogLevel.Error,
        Message = "Error submitting time off request")]
    private static partial void LogErrorSubmittingTimeOff(ILogger logger, System.Exception ex);

    // ── OnPostSwapAsync ────────────────────────────────────────────────────

    [LoggerMessage(EventId = 8050, Level = LogLevel.Information,
        Message = "Starting swap request submission")]
    private static partial void LogStartingSwapSubmit(ILogger logger);

    [LoggerMessage(EventId = 8051, Level = LogLevel.Warning,
        Message = "Swap request model state is invalid: {Errors}")]
    private static partial void LogSwapModelInvalid(ILogger logger, string errors);

    [LoggerMessage(EventId = 8052, Level = LogLevel.Information,
        Message = "Swap request for user {UserId}, ShiftId {ShiftId}")]
    private static partial void LogSwapSubmitContext(ILogger logger, int userId, int shiftId);

    [LoggerMessage(EventId = 8053, Level = LogLevel.Warning,
        Message = "Assignment {ShiftId} not found for user {UserId}")]
    private static partial void LogAssignmentNotFound(ILogger logger, int shiftId, int userId);

    [LoggerMessage(EventId = 8054, Level = LogLevel.Information,
        Message = "Found assignment {AssignmentId} for user {UserId}")]
    private static partial void LogFoundAssignment(ILogger logger, int assignmentId, int userId);

    [LoggerMessage(EventId = 8055, Level = LogLevel.Error,
        Message = "User {UserId} not found")]
    private static partial void LogUserNotFound(ILogger logger, int userId);

    [LoggerMessage(EventId = 8056, Level = LogLevel.Information,
        Message = "Swap request {RequestId} submitted successfully for assignment {AssignmentId}")]
    private static partial void LogSwapSubmitted(ILogger logger, int requestId, int assignmentId);

    [LoggerMessage(EventId = 8057, Level = LogLevel.Error,
        Message = "Error submitting swap request")]
    private static partial void LogErrorSubmittingSwap(ILogger logger, System.Exception ex);

    // ── OnPostCancelTimeOffAsync ───────────────────────────────────────────

    [LoggerMessage(EventId = 8070, Level = LogLevel.Warning,
        Message = "Cancel request: TimeOffRequest {RequestId} not found for user {UserId}")]
    private static partial void LogCancelTimeOffNotFound(ILogger logger, int requestId, int userId);

    [LoggerMessage(EventId = 8071, Level = LogLevel.Warning,
        Message = "Cancel request: TimeOffRequest {RequestId} is not pending (status={Status})")]
    private static partial void LogCancelTimeOffNotPending(ILogger logger, int requestId, RequestStatus status);

    [LoggerMessage(EventId = 8072, Level = LogLevel.Information,
        Message = "TimeOffRequest {RequestId} canceled by user {UserId}")]
    private static partial void LogTimeOffCanceled(ILogger logger, int requestId, int userId);

    [LoggerMessage(EventId = 8073, Level = LogLevel.Warning,
        Message = "Failed to cancel TimeOffRequest {RequestId}: {Message}")]
    private static partial void LogFailedCancelTimeOff(ILogger logger, int requestId, string? message);

    [LoggerMessage(EventId = 8074, Level = LogLevel.Error,
        Message = "Error canceling time off request {RequestId}")]
    private static partial void LogErrorCancelTimeOff(ILogger logger, System.Exception ex, int requestId);
}
