using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace ShiftManager.Controllers.Api.V1;

// Source-generated LoggerMessage delegates for OnDutyController (closes 35 sites of CA1848 — Batch K Phase 11).
// EventId range 14000-14099 reserved; 14000-14019 used. 20 unique methods cover 35 call sites —
// boilerplate (endpoint disabled / unauthorized / userId-missing / DB error / unexpected) recurs
// across the 5 endpoints (List, Get, Create, Update, Delete), distinguished by Endpoint parameter.
// Range allocations:
//   14000-14002: Common API auth/access boilerplate (3 methods, 12 sites)
//   14003-14006: Catch-block diagnostics (4 methods, 10 sites)
//   14007-14008: List endpoint date-parse warnings (2 methods)
//   14009     : Get endpoint not-found (1 method)
//   14010-14014: Create endpoint validations + success (5 methods)
//   14015-14017: Update endpoint not-found / validation / success (3 methods)
//   14018-14019: Delete endpoint not-found / success (2 methods)
public partial class OnDutyController
{
    // ── Common API boilerplate (reused across endpoints) ───────────────────

    [LoggerMessage(EventId = 14000, Level = LogLevel.Warning,
        Message = "API endpoint not enabled. Endpoint={Endpoint}, Path={Path}")]
    private static partial void LogApiEndpointNotEnabled(ILogger logger, string endpoint, PathString path);

    [LoggerMessage(EventId = 14001, Level = LogLevel.Warning,
        Message = "Unauthorized API access attempt. Endpoint={Endpoint}, Path={Path}, HasCompanyClaim={HasClaim}")]
    private static partial void LogApiUnauthorized(ILogger logger, string endpoint, PathString path, bool hasClaim);

    [LoggerMessage(EventId = 14002, Level = LogLevel.Warning,
        Message = "UserId claim missing. Endpoint={Endpoint}")]
    private static partial void LogUserIdClaimMissing(ILogger logger, string endpoint);

    // ── Catch-block diagnostics ────────────────────────────────────────────

    [LoggerMessage(EventId = 14003, Level = LogLevel.Error,
        Message = "Database error in {Endpoint}. Path={Path}")]
    private static partial void LogDbErrorPath(ILogger logger, System.Exception ex, string endpoint, PathString path);

    [LoggerMessage(EventId = 14004, Level = LogLevel.Error,
        Message = "Unexpected error in {Endpoint}. Path={Path}")]
    private static partial void LogUnexpectedErrorPath(ILogger logger, System.Exception ex, string endpoint, PathString path);

    [LoggerMessage(EventId = 14005, Level = LogLevel.Error,
        Message = "Database error in {Endpoint}. OnDutyId={OnDutyId}, Path={Path}")]
    private static partial void LogDbErrorWithOnDutyId(ILogger logger, System.Exception ex, string endpoint, int onDutyId, PathString path);

    [LoggerMessage(EventId = 14006, Level = LogLevel.Error,
        Message = "Unexpected error in {Endpoint}. OnDutyId={OnDutyId}, Path={Path}")]
    private static partial void LogUnexpectedErrorWithOnDutyId(ILogger logger, System.Exception ex, string endpoint, int onDutyId, PathString path);

    // ── List endpoint ──────────────────────────────────────────────────────

    [LoggerMessage(EventId = 14007, Level = LogLevel.Warning,
        Message = "Invalid startDate format. Endpoint={Endpoint}, StartDate={StartDate}")]
    private static partial void LogInvalidStartDate(ILogger logger, string endpoint, string? startDate);

    [LoggerMessage(EventId = 14008, Level = LogLevel.Warning,
        Message = "Invalid endDate format. Endpoint={Endpoint}, EndDate={EndDate}")]
    private static partial void LogInvalidEndDate(ILogger logger, string endpoint, string? endDate);

    // ── Get endpoint ───────────────────────────────────────────────────────

    [LoggerMessage(EventId = 14009, Level = LogLevel.Warning,
        Message = "On-duty assignment not found. Endpoint={Endpoint}, OnDutyId={OnDutyId}")]
    private static partial void LogOnDutyNotFound(ILogger logger, string endpoint, int onDutyId);

    // ── Create endpoint ────────────────────────────────────────────────────

    [LoggerMessage(EventId = 14010, Level = LogLevel.Warning,
        Message = "Validation error: UserId required. Endpoint={Endpoint}")]
    private static partial void LogValidationUserIdRequired(ILogger logger, string endpoint);

    [LoggerMessage(EventId = 14011, Level = LogLevel.Warning,
        Message = "Validation error: Date required. Endpoint={Endpoint}")]
    private static partial void LogValidationDateRequired(ILogger logger, string endpoint);

    [LoggerMessage(EventId = 14012, Level = LogLevel.Warning,
        Message = "Validation error: Type required. Endpoint={Endpoint}")]
    private static partial void LogValidationTypeRequired(ILogger logger, string endpoint);

    [LoggerMessage(EventId = 14013, Level = LogLevel.Warning,
        Message = "On-duty creation validation error. Endpoint={Endpoint}, Error={Error}")]
    private static partial void LogValidationCreateError(ILogger logger, string endpoint, string? error);

    [LoggerMessage(EventId = 14014, Level = LogLevel.Information,
        Message = "On-duty assignment created. Endpoint={Endpoint}, OnDutyId={OnDutyId}")]
    private static partial void LogOnDutyCreated(ILogger logger, string endpoint, int onDutyId);

    // ── Update endpoint ────────────────────────────────────────────────────

    [LoggerMessage(EventId = 14015, Level = LogLevel.Warning,
        Message = "On-duty assignment not found for update. Endpoint={Endpoint}, OnDutyId={OnDutyId}")]
    private static partial void LogOnDutyNotFoundForUpdate(ILogger logger, string endpoint, int onDutyId);

    [LoggerMessage(EventId = 14016, Level = LogLevel.Warning,
        Message = "On-duty update validation error. Endpoint={Endpoint}, OnDutyId={OnDutyId}, Error={Error}")]
    private static partial void LogValidationUpdateError(ILogger logger, string endpoint, int onDutyId, string? error);

    [LoggerMessage(EventId = 14017, Level = LogLevel.Information,
        Message = "On-duty assignment updated. Endpoint={Endpoint}, OnDutyId={OnDutyId}")]
    private static partial void LogOnDutyUpdated(ILogger logger, string endpoint, int onDutyId);

    // ── Delete endpoint ────────────────────────────────────────────────────

    [LoggerMessage(EventId = 14018, Level = LogLevel.Warning,
        Message = "On-duty deletion failed. Endpoint={Endpoint}, OnDutyId={OnDutyId}")]
    private static partial void LogOnDutyDeletionFailed(ILogger logger, string endpoint, int onDutyId);

    [LoggerMessage(EventId = 14019, Level = LogLevel.Information,
        Message = "On-duty assignment deleted. Endpoint={Endpoint}, OnDutyId={OnDutyId}, CanceledBy={CanceledBy}")]
    private static partial void LogOnDutyDeleted(ILogger logger, string endpoint, int onDutyId, int canceledBy);
}
