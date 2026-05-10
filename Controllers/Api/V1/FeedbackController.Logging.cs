using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace ShiftManager.Controllers.Api.V1;

// Source-generated LoggerMessage delegates for FeedbackController (Batch K Phase 13 — closes
// 32 sites of CA1848). EventId range 16000-16099 reserved (mirrors TimeOffController at 6000-6099).
// 17 unique methods cover 32 call sites — boilerplate (endpoint disabled / unauthorized / catch
// diagnostics) recurs across 5 endpoints, distinguished by Endpoint parameter. Range allocations:
//   16000-16004: Common API auth/access boilerplate (3 methods, 12 sites)
//   16005-16009: Catch-block diagnostics (4 methods, 10 sites)
//   16010-16014: Get + Delete endpoints distinct sites (3 methods)
//   16015-16019: Create endpoint distinct sites (4 methods)
//   16020-16024: UpdateStatus endpoint distinct sites (3 methods)
public partial class FeedbackController
{
    // ── Common API boilerplate (reused) ────────────────────────────────────

    [LoggerMessage(EventId = 16000, Level = LogLevel.Warning,
        Message = "API endpoint not enabled. Endpoint={Endpoint}, Path={Path}")]
    private static partial void LogApiEndpointNotEnabled(ILogger logger, string endpoint, PathString path);

    [LoggerMessage(EventId = 16001, Level = LogLevel.Warning,
        Message = "Unauthorized API access attempt. Endpoint={Endpoint}, Path={Path}, HasCompanyClaim={HasClaim}")]
    private static partial void LogApiUnauthorized(ILogger logger, string endpoint, PathString path, bool hasClaim);

    [LoggerMessage(EventId = 16002, Level = LogLevel.Warning,
        Message = "UserId claim missing. Endpoint={Endpoint}, CompanyId={CompanyId}")]
    private static partial void LogUserIdClaimMissing(ILogger logger, string endpoint, int companyId);

    // ── Catch-block diagnostics ────────────────────────────────────────────

    [LoggerMessage(EventId = 16005, Level = LogLevel.Error,
        Message = "Database error in {Endpoint}. CompanyId={CompanyId}, Path={Path}")]
    private static partial void LogDbErrorCompanyPath(ILogger logger, System.Exception ex, string endpoint, string? companyId, PathString path);

    [LoggerMessage(EventId = 16006, Level = LogLevel.Error,
        Message = "Unexpected error in {Endpoint}. CompanyId={CompanyId}, Path={Path}")]
    private static partial void LogUnexpectedErrorCompanyPath(ILogger logger, System.Exception ex, string endpoint, string? companyId, PathString path);

    [LoggerMessage(EventId = 16007, Level = LogLevel.Error,
        Message = "Database error in {Endpoint}. CompanyId={CompanyId}, FeedbackId={FeedbackId}, Path={Path}")]
    private static partial void LogDbErrorWithFeedbackId(ILogger logger, System.Exception ex, string endpoint, string? companyId, int feedbackId, PathString path);

    [LoggerMessage(EventId = 16008, Level = LogLevel.Error,
        Message = "Unexpected error in {Endpoint}. CompanyId={CompanyId}, FeedbackId={FeedbackId}, Path={Path}")]
    private static partial void LogUnexpectedErrorWithFeedbackId(ILogger logger, System.Exception ex, string endpoint, string? companyId, int feedbackId, PathString path);

    // ── Get + Delete endpoints ─────────────────────────────────────────────

    [LoggerMessage(EventId = 16010, Level = LogLevel.Warning,
        Message = "Feedback not found. Endpoint={Endpoint}, CompanyId={CompanyId}, FeedbackId={FeedbackId}")]
    private static partial void LogFeedbackNotFound(ILogger logger, string endpoint, int companyId, int feedbackId);

    [LoggerMessage(EventId = 16011, Level = LogLevel.Warning,
        Message = "Feedback deletion failed. Endpoint={Endpoint}, CompanyId={CompanyId}, FeedbackId={FeedbackId}")]
    private static partial void LogFeedbackDeletionFailed(ILogger logger, string endpoint, int companyId, int feedbackId);

    [LoggerMessage(EventId = 16012, Level = LogLevel.Information,
        Message = "Feedback deleted. Endpoint={Endpoint}, CompanyId={CompanyId}, FeedbackId={FeedbackId}")]
    private static partial void LogFeedbackDeleted(ILogger logger, string endpoint, int companyId, int feedbackId);

    // ── Create endpoint ────────────────────────────────────────────────────

    [LoggerMessage(EventId = 16015, Level = LogLevel.Warning,
        Message = "Validation error: Type required. Endpoint={Endpoint}, CompanyId={CompanyId}")]
    private static partial void LogValidationTypeRequired(ILogger logger, string endpoint, int companyId);

    [LoggerMessage(EventId = 16016, Level = LogLevel.Warning,
        Message = "Validation error: Content required. Endpoint={Endpoint}, CompanyId={CompanyId}")]
    private static partial void LogValidationContentRequired(ILogger logger, string endpoint, int companyId);

    [LoggerMessage(EventId = 16017, Level = LogLevel.Warning,
        Message = "Feedback creation validation error. Endpoint={Endpoint}, CompanyId={CompanyId}, Error={Error}")]
    private static partial void LogFeedbackCreationValidationError(ILogger logger, string endpoint, int companyId, string? error);

    [LoggerMessage(EventId = 16018, Level = LogLevel.Information,
        Message = "Feedback created. Endpoint={Endpoint}, CompanyId={CompanyId}, FeedbackId={FeedbackId}")]
    private static partial void LogFeedbackCreated(ILogger logger, string endpoint, int companyId, int feedbackId);

    // ── UpdateStatus endpoint ──────────────────────────────────────────────

    [LoggerMessage(EventId = 16020, Level = LogLevel.Warning,
        Message = "Feedback not found for update. Endpoint={Endpoint}, CompanyId={CompanyId}, FeedbackId={FeedbackId}")]
    private static partial void LogFeedbackNotFoundForUpdate(ILogger logger, string endpoint, int companyId, int feedbackId);

    [LoggerMessage(EventId = 16021, Level = LogLevel.Warning,
        Message = "Feedback status update validation error. Endpoint={Endpoint}, CompanyId={CompanyId}, FeedbackId={FeedbackId}, Error={Error}")]
    private static partial void LogFeedbackStatusUpdateValidationError(ILogger logger, string endpoint, int companyId, int feedbackId, string? error);

    [LoggerMessage(EventId = 16022, Level = LogLevel.Information,
        Message = "Feedback status updated. Endpoint={Endpoint}, CompanyId={CompanyId}, FeedbackId={FeedbackId}")]
    private static partial void LogFeedbackStatusUpdated(ILogger logger, string endpoint, int companyId, int feedbackId);
}
