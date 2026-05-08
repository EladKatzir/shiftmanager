using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace ShiftManager.Controllers.Api.V1;

// Source-generated LoggerMessage delegates for SwapRequestsController (closes 42 sites of F-C-013).
// EventId range 3000-3099 reserved. 21 unique methods cover 42 call sites — many events
// recur across endpoints (Endpoint parameter discriminates the action). Range allocations:
//   3000-3009: Common API auth/access boilerplate (3 methods used 16 times)
//   3010-3019: Date-parsing validation (List endpoint only, 2 methods)
//   3020-3029: Catch-block diagnostics (4 methods used 12 times — half claim-string, half int companyId)
//   3030-3039: Get endpoint
//   3040-3049: Create endpoint (3 methods)
//   3050-3059: Approve endpoint (3 methods)
//   3060-3069: Decline endpoint (3 methods)
//   3070-3079: Delete endpoint (2 methods)
public partial class SwapRequestsController
{
    // ── Common API boilerplate (reused across endpoints) ───────────────────

    [LoggerMessage(EventId = 3000, Level = LogLevel.Warning,
        Message = "API endpoint not enabled. Endpoint={Endpoint}, Path={Path}")]
    private static partial void LogApiEndpointNotEnabled(ILogger logger, string endpoint, PathString path);

    [LoggerMessage(EventId = 3001, Level = LogLevel.Warning,
        Message = "Unauthorized API access attempt. Endpoint={Endpoint}, Path={Path}, HasCompanyClaim={HasClaim}")]
    private static partial void LogApiUnauthorized(ILogger logger, string endpoint, PathString path, bool hasClaim);

    [LoggerMessage(EventId = 3002, Level = LogLevel.Warning,
        Message = "UserId claim missing. Endpoint={Endpoint}, CompanyId={CompanyId}")]
    private static partial void LogApiUserIdClaimMissing(ILogger logger, string endpoint, int companyId);

    // ── Date-parsing validation (List endpoint) ────────────────────────────

    [LoggerMessage(EventId = 3010, Level = LogLevel.Warning,
        Message = "Invalid startDate format. Endpoint={Endpoint}, CompanyId={CompanyId}, StartDate={StartDate}")]
    private static partial void LogInvalidStartDate(ILogger logger, string endpoint, int companyId, string? startDate);

    [LoggerMessage(EventId = 3011, Level = LogLevel.Warning,
        Message = "Invalid endDate format. Endpoint={Endpoint}, CompanyId={CompanyId}, EndDate={EndDate}")]
    private static partial void LogInvalidEndDate(ILogger logger, string endpoint, int companyId, string? endDate);

    // ── Catch-block diagnostics (CompanyId is raw claim string) ────────────

    [LoggerMessage(EventId = 3020, Level = LogLevel.Error,
        Message = "Database error in {Endpoint}. CompanyId={CompanyId}, Path={Path}")]
    private static partial void LogDbErrorNoRequestId(ILogger logger, System.Exception ex, string endpoint, string? companyId, PathString path);

    [LoggerMessage(EventId = 3021, Level = LogLevel.Error,
        Message = "Unexpected error in {Endpoint}. CompanyId={CompanyId}, Path={Path}")]
    private static partial void LogUnexpectedErrorNoRequestId(ILogger logger, System.Exception ex, string endpoint, string? companyId, PathString path);

    [LoggerMessage(EventId = 3022, Level = LogLevel.Error,
        Message = "Database error in {Endpoint}. CompanyId={CompanyId}, RequestId={RequestId}, Path={Path}")]
    private static partial void LogDbErrorWithRequestId(ILogger logger, System.Exception ex, string endpoint, string? companyId, int requestId, PathString path);

    [LoggerMessage(EventId = 3023, Level = LogLevel.Error,
        Message = "Unexpected error in {Endpoint}. CompanyId={CompanyId}, RequestId={RequestId}, Path={Path}")]
    private static partial void LogUnexpectedErrorWithRequestId(ILogger logger, System.Exception ex, string endpoint, string? companyId, int requestId, PathString path);

    // ── Get endpoint ───────────────────────────────────────────────────────

    [LoggerMessage(EventId = 3030, Level = LogLevel.Warning,
        Message = "Swap request not found. Endpoint={Endpoint}, CompanyId={CompanyId}, RequestId={RequestId}")]
    private static partial void LogSwapRequestNotFound(ILogger logger, string endpoint, int companyId, int requestId);

    // ── Create endpoint ────────────────────────────────────────────────────

    [LoggerMessage(EventId = 3040, Level = LogLevel.Warning,
        Message = "Validation error: FromAssignmentId required. Endpoint={Endpoint}, CompanyId={CompanyId}, UserId={UserId}")]
    private static partial void LogValidationFromAssignmentRequired(ILogger logger, string endpoint, int companyId, int userId);

    [LoggerMessage(EventId = 3041, Level = LogLevel.Warning,
        Message = "Swap request validation error. Endpoint={Endpoint}, CompanyId={CompanyId}, UserId={UserId}, Error={Error}")]
    private static partial void LogValidationCreateError(ILogger logger, string endpoint, int companyId, int userId, string? error);

    [LoggerMessage(EventId = 3042, Level = LogLevel.Information,
        Message = "Swap request created. Endpoint={Endpoint}, CompanyId={CompanyId}, FromUserId={FromUserId}, RequestId={RequestId}")]
    private static partial void LogSwapRequestCreated(ILogger logger, string endpoint, int companyId, int fromUserId, int requestId);

    // ── Approve endpoint ───────────────────────────────────────────────────

    [LoggerMessage(EventId = 3050, Level = LogLevel.Warning,
        Message = "Swap request not found for approval. Endpoint={Endpoint}, CompanyId={CompanyId}, RequestId={RequestId}")]
    private static partial void LogSwapRequestNotFoundForApproval(ILogger logger, string endpoint, int companyId, int requestId);

    [LoggerMessage(EventId = 3051, Level = LogLevel.Warning,
        Message = "Swap request approval validation error. Endpoint={Endpoint}, CompanyId={CompanyId}, RequestId={RequestId}, Error={Error}")]
    private static partial void LogValidationApproveError(ILogger logger, string endpoint, int companyId, int requestId, string? error);

    [LoggerMessage(EventId = 3052, Level = LogLevel.Information,
        Message = "Swap request approved. Endpoint={Endpoint}, CompanyId={CompanyId}, RequestId={RequestId}, ReviewerId={ReviewerId}")]
    private static partial void LogSwapRequestApproved(ILogger logger, string endpoint, int companyId, int requestId, int reviewerId);

    // ── Decline endpoint ───────────────────────────────────────────────────

    [LoggerMessage(EventId = 3060, Level = LogLevel.Warning,
        Message = "Swap request not found for decline. Endpoint={Endpoint}, CompanyId={CompanyId}, RequestId={RequestId}")]
    private static partial void LogSwapRequestNotFoundForDecline(ILogger logger, string endpoint, int companyId, int requestId);

    [LoggerMessage(EventId = 3061, Level = LogLevel.Warning,
        Message = "Swap request decline validation error. Endpoint={Endpoint}, CompanyId={CompanyId}, RequestId={RequestId}, Error={Error}")]
    private static partial void LogValidationDeclineError(ILogger logger, string endpoint, int companyId, int requestId, string? error);

    [LoggerMessage(EventId = 3062, Level = LogLevel.Information,
        Message = "Swap request declined. Endpoint={Endpoint}, CompanyId={CompanyId}, RequestId={RequestId}, ReviewerId={ReviewerId}")]
    private static partial void LogSwapRequestDeclined(ILogger logger, string endpoint, int companyId, int requestId, int reviewerId);

    // ── Delete endpoint ────────────────────────────────────────────────────

    [LoggerMessage(EventId = 3070, Level = LogLevel.Warning,
        Message = "Swap request deletion failed. Endpoint={Endpoint}, CompanyId={CompanyId}, RequestId={RequestId}, UserId={UserId}")]
    private static partial void LogSwapRequestDeletionFailed(ILogger logger, string endpoint, int companyId, int requestId, int userId);

    [LoggerMessage(EventId = 3071, Level = LogLevel.Information,
        Message = "Swap request deleted. Endpoint={Endpoint}, CompanyId={CompanyId}, RequestId={RequestId}, UserId={UserId}")]
    private static partial void LogSwapRequestDeleted(ILogger logger, string endpoint, int companyId, int requestId, int userId);
}
