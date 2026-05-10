using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace ShiftManager.Controllers.Api.V1;

// Source-generated LoggerMessage delegates for ChoresController (closes 35 sites of CA1848,
// Batch K Phase 10). EventId range 13000-13099 reserved. 21 unique methods cover 35 call
// sites — boilerplate (endpoint disabled / unauthorized / catch-block diagnostics) recurs
// across the 5 endpoints (List/Get/Create/Update/Delete), distinguished by the Endpoint
// parameter (Pattern B). Range allocations:
//   13000-13004: Common API auth/access boilerplate (3 methods, 12 sites)
//   13005-13009: Catch-block diagnostics (4 methods, 10 sites)
//   13010-13011: List endpoint specific (2 methods, 2 sites)
//   13012:       Get endpoint specific (1 method, 1 site)
//   13013-13017: Create endpoint specific (5 methods, 5 sites)
//   13018-13019: Update endpoint specific (2 methods, 2 sites)
//   13020-13021: Delete endpoint specific (2 methods, 2 sites)
public partial class ChoresController
{
    // ── Common API boilerplate (reused across all endpoints) ───────────────

    [LoggerMessage(EventId = 13000, Level = LogLevel.Warning,
        Message = "API endpoint not enabled. Endpoint={Endpoint}, Path={Path}")]
    private static partial void LogApiEndpointNotEnabled(ILogger logger, string endpoint, PathString path);

    [LoggerMessage(EventId = 13001, Level = LogLevel.Warning,
        Message = "Unauthorized API access attempt. Endpoint={Endpoint}, Path={Path}, HasCompanyClaim={HasClaim}")]
    private static partial void LogApiUnauthorized(ILogger logger, string endpoint, PathString path, bool hasClaim);

    [LoggerMessage(EventId = 13002, Level = LogLevel.Warning,
        Message = "UserId claim missing. Endpoint={Endpoint}, CompanyId={CompanyId}")]
    private static partial void LogUserIdClaimMissing(ILogger logger, string endpoint, int companyId);

    // ── Catch-block diagnostics ────────────────────────────────────────────

    [LoggerMessage(EventId = 13005, Level = LogLevel.Error,
        Message = "Database error in {Endpoint}. CompanyId={CompanyId}, Path={Path}")]
    private static partial void LogDbErrorCompanyPath(ILogger logger, System.Exception ex, string endpoint, string? companyId, PathString path);

    [LoggerMessage(EventId = 13006, Level = LogLevel.Error,
        Message = "Unexpected error in {Endpoint}. CompanyId={CompanyId}, Path={Path}")]
    private static partial void LogUnexpectedErrorCompanyPath(ILogger logger, System.Exception ex, string endpoint, string? companyId, PathString path);

    [LoggerMessage(EventId = 13007, Level = LogLevel.Error,
        Message = "Database error in {Endpoint}. CompanyId={CompanyId}, ChoreId={ChoreId}, Path={Path}")]
    private static partial void LogDbErrorWithChoreId(ILogger logger, System.Exception ex, string endpoint, string? companyId, int choreId, PathString path);

    [LoggerMessage(EventId = 13008, Level = LogLevel.Error,
        Message = "Unexpected error in {Endpoint}. CompanyId={CompanyId}, ChoreId={ChoreId}, Path={Path}")]
    private static partial void LogUnexpectedErrorWithChoreId(ILogger logger, System.Exception ex, string endpoint, string? companyId, int choreId, PathString path);

    // ── List endpoint ──────────────────────────────────────────────────────

    [LoggerMessage(EventId = 13010, Level = LogLevel.Warning,
        Message = "Invalid startDate format. Endpoint={Endpoint}, CompanyId={CompanyId}, StartDate={StartDate}")]
    private static partial void LogInvalidStartDate(ILogger logger, string endpoint, int companyId, string? startDate);

    [LoggerMessage(EventId = 13011, Level = LogLevel.Warning,
        Message = "Invalid endDate format. Endpoint={Endpoint}, CompanyId={CompanyId}, EndDate={EndDate}")]
    private static partial void LogInvalidEndDate(ILogger logger, string endpoint, int companyId, string? endDate);

    // ── Get endpoint ───────────────────────────────────────────────────────

    [LoggerMessage(EventId = 13012, Level = LogLevel.Warning,
        Message = "Chore not found. Endpoint={Endpoint}, CompanyId={CompanyId}, ChoreId={ChoreId}")]
    private static partial void LogChoreNotFound(ILogger logger, string endpoint, int companyId, int choreId);

    // ── Create endpoint ────────────────────────────────────────────────────

    [LoggerMessage(EventId = 13013, Level = LogLevel.Warning,
        Message = "Validation error: UserId required. Endpoint={Endpoint}, CompanyId={CompanyId}")]
    private static partial void LogValidationUserIdRequired(ILogger logger, string endpoint, int companyId);

    [LoggerMessage(EventId = 13014, Level = LogLevel.Warning,
        Message = "Validation error: Date required. Endpoint={Endpoint}, CompanyId={CompanyId}")]
    private static partial void LogValidationDateRequired(ILogger logger, string endpoint, int companyId);

    [LoggerMessage(EventId = 13015, Level = LogLevel.Warning,
        Message = "Validation error: Title required. Endpoint={Endpoint}, CompanyId={CompanyId}")]
    private static partial void LogValidationTitleRequired(ILogger logger, string endpoint, int companyId);

    [LoggerMessage(EventId = 13016, Level = LogLevel.Warning,
        Message = "Chore creation validation error. Endpoint={Endpoint}, CompanyId={CompanyId}, Error={Error}")]
    private static partial void LogChoreCreationValidationError(ILogger logger, string endpoint, int companyId, string? error);

    [LoggerMessage(EventId = 13017, Level = LogLevel.Information,
        Message = "Chore created. Endpoint={Endpoint}, CompanyId={CompanyId}, ChoreId={ChoreId}")]
    private static partial void LogChoreCreated(ILogger logger, string endpoint, int companyId, int choreId);

    // ── Update endpoint ────────────────────────────────────────────────────

    [LoggerMessage(EventId = 13018, Level = LogLevel.Warning,
        Message = "Chore not found for update. Endpoint={Endpoint}, CompanyId={CompanyId}, ChoreId={ChoreId}")]
    private static partial void LogChoreNotFoundForUpdate(ILogger logger, string endpoint, int companyId, int choreId);

    [LoggerMessage(EventId = 13019, Level = LogLevel.Warning,
        Message = "Chore update validation error. Endpoint={Endpoint}, CompanyId={CompanyId}, ChoreId={ChoreId}, Error={Error}")]
    private static partial void LogChoreUpdateValidationError(ILogger logger, string endpoint, int companyId, int choreId, string? error);

    [LoggerMessage(EventId = 13020, Level = LogLevel.Information,
        Message = "Chore updated. Endpoint={Endpoint}, CompanyId={CompanyId}, ChoreId={ChoreId}")]
    private static partial void LogChoreUpdated(ILogger logger, string endpoint, int companyId, int choreId);

    // ── Delete endpoint ────────────────────────────────────────────────────

    [LoggerMessage(EventId = 13021, Level = LogLevel.Warning,
        Message = "Chore deletion failed. Endpoint={Endpoint}, CompanyId={CompanyId}, ChoreId={ChoreId}")]
    private static partial void LogChoreDeletionFailed(ILogger logger, string endpoint, int companyId, int choreId);

    [LoggerMessage(EventId = 13022, Level = LogLevel.Information,
        Message = "Chore deleted. Endpoint={Endpoint}, CompanyId={CompanyId}, ChoreId={ChoreId}, CanceledBy={CanceledBy}")]
    private static partial void LogChoreDeleted(ILogger logger, string endpoint, int companyId, int choreId, int canceledBy);
}
