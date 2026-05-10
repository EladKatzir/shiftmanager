using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace ShiftManager.Controllers.Api.V1;

// Source-generated LoggerMessage delegates for UsersController (closes 26 sites of CA1848 — Batch K Phase 12).
// EventId range 15000-15099 reserved (mirrors TimeOffController at 6000-6099). 17 unique methods cover
// 26 call sites — boilerplate (endpoint disabled / unauthorized / catch-block diagnostics) recurs across
// 4 endpoints, distinguished by Endpoint parameter. Range allocations:
//   15000-15001: Common API auth/access boilerplate (no UserId) — 2 methods, 6 sites
//   15002-15003: Common API auth/access boilerplate (with UserId) — 2 methods, 2 sites
//   15004-15007: Catch-block diagnostics (4 methods, 8 sites)
//   15008-15009: User not found (2 methods, 2 sites)
//   15010-15014: Validation errors (3 methods, 5 sites — Email/DisplayName/Role + create + update)
//   15015-15016: Conflict + Success (2 methods, 3 sites)
public partial class UsersController
{
    // ── Common API boilerplate (reused) ────────────────────────────────────

    [LoggerMessage(EventId = 15000, Level = LogLevel.Warning,
        Message = "API endpoint not enabled. Endpoint={Endpoint}, Path={Path}")]
    private static partial void LogApiEndpointNotEnabled(ILogger logger, string endpoint, PathString path);

    [LoggerMessage(EventId = 15001, Level = LogLevel.Warning,
        Message = "Unauthorized API access attempt. Endpoint={Endpoint}, Path={Path}, HasCompanyClaim={HasClaim}")]
    private static partial void LogApiUnauthorized(ILogger logger, string endpoint, PathString path, bool hasClaim);

    [LoggerMessage(EventId = 15002, Level = LogLevel.Warning,
        Message = "Unauthorized API access attempt. Endpoint={Endpoint}, Path={Path}, UserId={UserId}, HasCompanyClaim={HasClaim}")]
    private static partial void LogApiUnauthorizedWithUserId(ILogger logger, string endpoint, PathString path, int userId, bool hasClaim);

    // ── Catch-block diagnostics ────────────────────────────────────────────

    [LoggerMessage(EventId = 15004, Level = LogLevel.Error,
        Message = "Database error in {Endpoint}. CompanyId={CompanyId}, Path={Path}")]
    private static partial void LogDbErrorCompanyPath(ILogger logger, System.Exception ex, string endpoint, string? companyId, PathString path);

    [LoggerMessage(EventId = 15005, Level = LogLevel.Error,
        Message = "Unexpected error in {Endpoint}. CompanyId={CompanyId}, Path={Path}")]
    private static partial void LogUnexpectedErrorCompanyPath(ILogger logger, System.Exception ex, string endpoint, string? companyId, PathString path);

    [LoggerMessage(EventId = 15006, Level = LogLevel.Error,
        Message = "Database error in {Endpoint}. CompanyId={CompanyId}, UserId={UserId}, Path={Path}")]
    private static partial void LogDbErrorWithUserId(ILogger logger, System.Exception ex, string endpoint, string? companyId, int userId, PathString path);

    [LoggerMessage(EventId = 15007, Level = LogLevel.Error,
        Message = "Unexpected error in {Endpoint}. CompanyId={CompanyId}, UserId={UserId}, Path={Path}")]
    private static partial void LogUnexpectedErrorWithUserId(ILogger logger, System.Exception ex, string endpoint, string? companyId, int userId, PathString path);

    // ── User not found ─────────────────────────────────────────────────────

    [LoggerMessage(EventId = 15008, Level = LogLevel.Warning,
        Message = "User not found. Endpoint={Endpoint}, CompanyId={CompanyId}, UserId={UserId}")]
    private static partial void LogUserNotFound(ILogger logger, string endpoint, int companyId, int userId);

    [LoggerMessage(EventId = 15009, Level = LogLevel.Warning,
        Message = "User not found for update. Endpoint={Endpoint}, CompanyId={CompanyId}, UserId={UserId}")]
    private static partial void LogUserNotFoundForUpdate(ILogger logger, string endpoint, int companyId, int userId);

    // ── Validation errors ──────────────────────────────────────────────────

    [LoggerMessage(EventId = 15010, Level = LogLevel.Warning,
        Message = "Validation error in {Endpoint}. CompanyId={CompanyId}, Error={Error}")]
    private static partial void LogValidationError(ILogger logger, string endpoint, int companyId, string? error);

    [LoggerMessage(EventId = 15011, Level = LogLevel.Warning,
        Message = "User creation validation error in {Endpoint}. CompanyId={CompanyId}, Error={Error}")]
    private static partial void LogValidationCreateError(ILogger logger, string endpoint, int companyId, string? error);

    [LoggerMessage(EventId = 15012, Level = LogLevel.Warning,
        Message = "User update validation error. Endpoint={Endpoint}, CompanyId={CompanyId}, UserId={UserId}, Error={Error}")]
    private static partial void LogValidationUpdateError(ILogger logger, string endpoint, int companyId, int userId, string? error);

    // ── Conflict + Success ─────────────────────────────────────────────────

    [LoggerMessage(EventId = 15015, Level = LogLevel.Warning,
        Message = "User creation conflict in {Endpoint}. CompanyId={CompanyId}, Error={Error}")]
    private static partial void LogUserCreationConflict(ILogger logger, string endpoint, int companyId, string? error);

    [LoggerMessage(EventId = 15016, Level = LogLevel.Information,
        Message = "User created successfully. Endpoint={Endpoint}, CompanyId={CompanyId}, UserId={UserId}")]
    private static partial void LogUserCreated(ILogger logger, string endpoint, int companyId, int userId);

    [LoggerMessage(EventId = 15017, Level = LogLevel.Information,
        Message = "User updated successfully. Endpoint={Endpoint}, CompanyId={CompanyId}, UserId={UserId}")]
    private static partial void LogUserUpdated(ILogger logger, string endpoint, int companyId, int userId);
}
