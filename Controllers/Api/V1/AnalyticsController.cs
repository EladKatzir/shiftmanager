using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models.Api;

namespace ShiftManager.Controllers.Api.V1;

/// <summary>
/// API controller for analytics and aggregate metrics.
/// All endpoints require API key authentication via X-API-Key header.
/// </summary>
[ApiController]
[Route("api/v1/analytics")]
[Produces("application/json")]
public class AnalyticsController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AnalyticsController> _logger;

    public AnalyticsController(
        AppDbContext context,
        IConfiguration configuration,
        ILogger<AnalyticsController> logger)
    {
        _context = context;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Gets basic analytics and metrics summary.
    /// Requires scope: analytics:read
    /// </summary>
    [HttpGet("summary")]
    [ProducesResponseType(typeof(AnalyticsSummary), 200)]
    [ProducesResponseType(typeof(ApiProblemDetails), 401)]
    [ProducesResponseType(typeof(ApiProblemDetails), 403)]
    [ProducesResponseType(typeof(ApiProblemDetails), 429)]
    public async Task<IActionResult> GetSummary(
        [FromQuery] string? startDate = null,
        [FromQuery] string? endDate = null)
    {
        try
        {
            // Check feature flag
            if (!_configuration.GetValue<bool>("Features:Api:Analytics:SummaryEnabled", false))
            {
                _logger.LogWarning("API endpoint not enabled. Endpoint={Endpoint}, Path={Path}",
                    nameof(GetSummary), HttpContext.Request.Path);
                return NotFound(ApiProblemDetails.NotFound("This API endpoint is not enabled", HttpContext.Request.Path));
            }

            // Get CompanyId from claims
            var companyIdClaim = User.FindFirst("CompanyId")?.Value;
            if (companyIdClaim == null || !int.TryParse(companyIdClaim, out var companyId))
            {
                // ERR-003: Log authorization failure
                _logger.LogWarning("Unauthorized API access attempt. Endpoint={Endpoint}, Path={Path}, HasCompanyClaim={HasClaim}",
                    nameof(GetSummary), HttpContext.Request.Path, companyIdClaim != null);
                return Unauthorized(ApiProblemDetails.Unauthorized("Invalid authentication", HttpContext.Request.Path));
            }

            // Parse date filters
            var start = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)); // Default: last 30 days
            var end = DateOnly.FromDateTime(DateTime.UtcNow);

            if (!string.IsNullOrEmpty(startDate))
            {
                if (!DateOnly.TryParse(startDate, out var parsed))
                {
                    _logger.LogWarning("Invalid startDate format. Endpoint={Endpoint}, CompanyId={CompanyId}, StartDate={StartDate}",
                        nameof(GetSummary), companyId, startDate);
                    return BadRequest(ApiProblemDetails.ValidationError(
                        "Invalid startDate format. Use yyyy-MM-dd", HttpContext.Request.Path));
                }
                start = parsed;
            }

            if (!string.IsNullOrEmpty(endDate))
            {
                if (!DateOnly.TryParse(endDate, out var parsed))
                {
                    _logger.LogWarning("Invalid endDate format. Endpoint={Endpoint}, CompanyId={CompanyId}, EndDate={EndDate}",
                        nameof(GetSummary), companyId, endDate);
                    return BadRequest(ApiProblemDetails.ValidationError(
                        "Invalid endDate format. Use yyyy-MM-dd", HttpContext.Request.Path));
                }
                end = parsed;
            }

            // Calculate metrics
            var totalUsers = await _context.Users
                .Where(u => u.CompanyId == companyId)
                .CountAsync();

            var activeUsers = await _context.Users
                .Where(u => u.CompanyId == companyId && u.IsActive)
                .CountAsync();

            var shiftsInPeriod = await _context.ShiftInstances
                .Where(s => s.CompanyId == companyId &&
                            s.WorkDate >= start &&
                            s.WorkDate <= end)
                .CountAsync();

            var pendingTimeOffRequests = await _context.TimeOffRequests
                .Where(r => r.CompanyId == companyId &&
                            r.Status == Models.Support.RequestStatus.Pending)
                .CountAsync();

            var unreadNotifications = await _context.UserNotifications
                .Where(n => n.CompanyId == companyId && !n.IsRead)
                .CountAsync();

            var summary = new AnalyticsSummary
            {
                CompanyId = companyId,
                PeriodStart = start.ToString("yyyy-MM-dd"),
                PeriodEnd = end.ToString("yyyy-MM-dd"),
                TotalUsers = totalUsers,
                ActiveUsers = activeUsers,
                ShiftsScheduled = shiftsInPeriod,
                PendingTimeOffRequests = pendingTimeOffRequests,
                UnreadNotifications = unreadNotifications,
                GeneratedAt = DateTime.UtcNow.ToString("O")
            };

            return Ok(summary);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Database error in {Endpoint}. CompanyId={CompanyId}, Path={Path}",
                nameof(GetSummary), User.FindFirst("CompanyId")?.Value, HttpContext.Request.Path);
            return StatusCode(500, ApiProblemDetails.InternalError(
                "An error occurred while processing your request", HttpContext.Request.Path));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in {Endpoint}. CompanyId={CompanyId}, Path={Path}",
                nameof(GetSummary), User.FindFirst("CompanyId")?.Value, HttpContext.Request.Path);
            return StatusCode(500, ApiProblemDetails.InternalError(
                "An unexpected error occurred", HttpContext.Request.Path));
        }
    }
}

/// <summary>
/// Analytics summary response
/// </summary>
public class AnalyticsSummary
{
    public int CompanyId { get; set; }
    public string PeriodStart { get; set; } = string.Empty;
    public string PeriodEnd { get; set; } = string.Empty;
    public int TotalUsers { get; set; }
    public int ActiveUsers { get; set; }
    public int ShiftsScheduled { get; set; }
    public int PendingTimeOffRequests { get; set; }
    public int UnreadNotifications { get; set; }
    public string GeneratedAt { get; set; } = string.Empty;
}
