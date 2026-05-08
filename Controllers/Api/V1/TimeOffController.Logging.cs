using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace ShiftManager.Controllers.Api.V1;

// Source-generated LoggerMessage delegates for TimeOffController (closes 37 sites of F-C-013).
// EventId range 6000-6099 reserved (mirrors SwapRequestsController at 3000-3099). 25 unique
// methods cover 37 call sites — boilerplate (endpoint disabled / unauthorized) recurs across
// 5 endpoints, distinguished by Endpoint parameter. Range allocations:
//   6000-6004: Common API auth/access boilerplate (2 methods, 10 sites)
//   6005-6009: Catch-block diagnostics (5 methods, 11 sites)
//   6010-6014: List endpoint (4 methods)
//   6015-6019: Create endpoint (8 methods)
//   6020-6024: Approve+Decline endpoints (6 methods)
//   (Get-specific: 1 method shared with general 'not found')
public partial class TimeOffController
{
    // ── Common API boilerplate (reused) ────────────────────────────────────

    [LoggerMessage(EventId = 6000, Level = LogLevel.Warning,
        Message = "API endpoint not enabled. Endpoint={Endpoint}, Path={Path}")]
    private static partial void LogApiEndpointNotEnabled(ILogger logger, string endpoint, PathString path);

    [LoggerMessage(EventId = 6001, Level = LogLevel.Warning,
        Message = "Unauthorized API access attempt. Endpoint={Endpoint}, Path={Path}, HasCompanyClaim={HasClaim}")]
    private static partial void LogApiUnauthorized(ILogger logger, string endpoint, PathString path, bool hasClaim);

    // ── Catch-block diagnostics ────────────────────────────────────────────

    [LoggerMessage(EventId = 6005, Level = LogLevel.Error,
        Message = "Database error in {Endpoint}. CompanyId={CompanyId}, Path={Path}")]
    private static partial void LogDbErrorCompanyPath(ILogger logger, System.Exception ex, string endpoint, string? companyId, PathString path);

    [LoggerMessage(EventId = 6006, Level = LogLevel.Error,
        Message = "Unexpected error in {Endpoint}. CompanyId={CompanyId}, Path={Path}")]
    private static partial void LogUnexpectedErrorCompanyPath(ILogger logger, System.Exception ex, string endpoint, string? companyId, PathString path);

    [LoggerMessage(EventId = 6007, Level = LogLevel.Error,
        Message = "Database error in {Endpoint}. CompanyId={CompanyId}, RequestId={RequestId}, Path={Path}")]
    private static partial void LogDbErrorWithRequestId(ILogger logger, System.Exception ex, string endpoint, string? companyId, int requestId, PathString path);

    [LoggerMessage(EventId = 6008, Level = LogLevel.Error,
        Message = "Unexpected error in {Endpoint}. CompanyId={CompanyId}, RequestId={RequestId}, Path={Path}")]
    private static partial void LogUnexpectedErrorWithRequestId(ILogger logger, System.Exception ex, string endpoint, string? companyId, int requestId, PathString path);

    [LoggerMessage(EventId = 6009, Level = LogLevel.Error,
        Message = "Database error in {Endpoint}. CompanyId={CompanyId}, UserId={UserId}, Path={Path}")]
    private static partial void LogDbErrorWithUserId(ILogger logger, System.Exception ex, string endpoint, string? companyId, int? userId, PathString path);

    [LoggerMessage(EventId = 6010, Level = LogLevel.Error,
        Message = "Unexpected error in {Endpoint}. CompanyId={CompanyId}, UserId={UserId}, Path={Path}")]
    private static partial void LogUnexpectedErrorWithUserId(ILogger logger, System.Exception ex, string endpoint, string? companyId, int? userId, PathString path);

    // ── List endpoint ──────────────────────────────────────────────────────

    [LoggerMessage(EventId = 6011, Level = LogLevel.Warning,
        Message = "Invalid startDate format. Endpoint={Endpoint}, CompanyId={CompanyId}, StartDate={StartDate}")]
    private static partial void LogInvalidStartDateLower(ILogger logger, string endpoint, int companyId, string? startDate);

    [LoggerMessage(EventId = 6012, Level = LogLevel.Warning,
        Message = "Invalid endDate format. Endpoint={Endpoint}, CompanyId={CompanyId}, EndDate={EndDate}")]
    private static partial void LogInvalidEndDateLower(ILogger logger, string endpoint, int companyId, string? endDate);

    // ── Get endpoint ───────────────────────────────────────────────────────

    [LoggerMessage(EventId = 6013, Level = LogLevel.Warning,
        Message = "Time-off request not found. Endpoint={Endpoint}, CompanyId={CompanyId}, RequestId={RequestId}")]
    private static partial void LogTimeOffNotFound(ILogger logger, string endpoint, int companyId, int requestId);

    // ── Create endpoint ────────────────────────────────────────────────────

    [LoggerMessage(EventId = 6014, Level = LogLevel.Warning,
        Message = "Validation error: UserId required. Endpoint={Endpoint}, CompanyId={CompanyId}")]
    private static partial void LogValidationUserIdRequired(ILogger logger, string endpoint, int companyId);

    [LoggerMessage(EventId = 6015, Level = LogLevel.Warning,
        Message = "Validation error: StartDate required. Endpoint={Endpoint}, CompanyId={CompanyId}, UserId={UserId}")]
    private static partial void LogValidationStartDateRequired(ILogger logger, string endpoint, int companyId, int userId);

    [LoggerMessage(EventId = 6016, Level = LogLevel.Warning,
        Message = "Validation error: EndDate required. Endpoint={Endpoint}, CompanyId={CompanyId}, UserId={UserId}")]
    private static partial void LogValidationEndDateRequired(ILogger logger, string endpoint, int companyId, int userId);

    [LoggerMessage(EventId = 6017, Level = LogLevel.Warning,
        Message = "Invalid StartDate format. Endpoint={Endpoint}, CompanyId={CompanyId}, StartDate={StartDate}")]
    private static partial void LogInvalidStartDateUpper(ILogger logger, string endpoint, int companyId, string? startDate);

    [LoggerMessage(EventId = 6018, Level = LogLevel.Warning,
        Message = "Invalid EndDate format. Endpoint={Endpoint}, CompanyId={CompanyId}, EndDate={EndDate}")]
    private static partial void LogInvalidEndDateUpper(ILogger logger, string endpoint, int companyId, string? endDate);

    [LoggerMessage(EventId = 6019, Level = LogLevel.Warning,
        Message = "Time-off request conflict. Endpoint={Endpoint}, CompanyId={CompanyId}, UserId={UserId}, Error={Error}")]
    private static partial void LogTimeOffConflict(ILogger logger, string endpoint, int companyId, int userId, string? error);

    [LoggerMessage(EventId = 6020, Level = LogLevel.Warning,
        Message = "Time-off request validation error. Endpoint={Endpoint}, CompanyId={CompanyId}, UserId={UserId}, Error={Error}")]
    private static partial void LogValidationCreateError(ILogger logger, string endpoint, int companyId, int userId, string? error);

    [LoggerMessage(EventId = 6021, Level = LogLevel.Information,
        Message = "Time-off request created. Endpoint={Endpoint}, CompanyId={CompanyId}, UserId={UserId}, RequestId={RequestId}")]
    private static partial void LogTimeOffCreated(ILogger logger, string endpoint, int companyId, int userId, int requestId);

    // ── Approve + Decline endpoints ────────────────────────────────────────

    [LoggerMessage(EventId = 6022, Level = LogLevel.Warning,
        Message = "Time-off request not found for approval. Endpoint={Endpoint}, CompanyId={CompanyId}, RequestId={RequestId}")]
    private static partial void LogTimeOffNotFoundForApproval(ILogger logger, string endpoint, int companyId, int requestId);

    [LoggerMessage(EventId = 6023, Level = LogLevel.Warning,
        Message = "Time-off request approval validation error. Endpoint={Endpoint}, CompanyId={CompanyId}, RequestId={RequestId}, Error={Error}")]
    private static partial void LogValidationApproveError(ILogger logger, string endpoint, int companyId, int requestId, string? error);

    [LoggerMessage(EventId = 6024, Level = LogLevel.Information,
        Message = "Time-off request approved. Endpoint={Endpoint}, CompanyId={CompanyId}, RequestId={RequestId}")]
    private static partial void LogTimeOffApproved(ILogger logger, string endpoint, int companyId, int requestId);

    [LoggerMessage(EventId = 6025, Level = LogLevel.Warning,
        Message = "Time-off request not found for decline. Endpoint={Endpoint}, CompanyId={CompanyId}, RequestId={RequestId}")]
    private static partial void LogTimeOffNotFoundForDecline(ILogger logger, string endpoint, int companyId, int requestId);

    [LoggerMessage(EventId = 6026, Level = LogLevel.Warning,
        Message = "Time-off request decline validation error. Endpoint={Endpoint}, CompanyId={CompanyId}, RequestId={RequestId}, Error={Error}")]
    private static partial void LogValidationDeclineError(ILogger logger, string endpoint, int companyId, int requestId, string? error);

    [LoggerMessage(EventId = 6027, Level = LogLevel.Information,
        Message = "Time-off request declined. Endpoint={Endpoint}, CompanyId={CompanyId}, RequestId={RequestId}")]
    private static partial void LogTimeOffDeclined(ILogger logger, string endpoint, int companyId, int requestId);
}
