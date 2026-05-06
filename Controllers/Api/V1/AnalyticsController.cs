using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Data.SeedData;
using ShiftManager.Models;
using ShiftManager.Services;

namespace ShiftManager.Controllers.Api.V1;

/// <summary>
/// API controller for analytics and aggregate metrics.
/// All endpoints require API key authentication via X-API-Key header.
/// </summary>
[Authorize]
[ApiController]
[Route("api/v1/analytics")]
[Produces("application/json")]
public class AnalyticsController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IFeatureFlagService _featureFlagService;
    private readonly ILogger<AnalyticsController> _logger;

    public AnalyticsController(
        AppDbContext context,
        IFeatureFlagService featureFlagService,
        ILogger<AnalyticsController> logger)
    {
        _context = context;
        _featureFlagService = featureFlagService;
        _logger = logger;
    }

    /// <summary>
    /// Gets basic analytics and metrics summary.
    /// Requires scope: analytics:read
    /// </summary>
    [HttpGet("summary")]
    [ProducesResponseType(typeof(AnalyticsSummary), 200)]
    [ProducesResponseType(typeof(ApiErrorResponse), 401)]
    [ProducesResponseType(typeof(ApiErrorResponse), 403)]
    [ProducesResponseType(typeof(ApiErrorResponse), 429)]
    public async Task<IActionResult> GetSummary(
        [FromQuery] string? startDate = null,
        [FromQuery] string? endDate = null)
    {
        try
        {
            // Check feature flag
            if (!await _featureFlagService.IsEnabledAsync(FeatureFlagSeed.Flags.ApiAnalyticsSummary))
            {
                _logger.LogWarning("API endpoint not enabled. Endpoint={Endpoint}, Path={Path}",
                    nameof(GetSummary), HttpContext.Request.Path);
                return NotFound(ApiErrorResponse.Create("ENDPOINT_DISABLED", "This API endpoint is not enabled"));
            }

            // Get CompanyId from claims
            var companyIdClaim = User.FindFirst("CompanyId")?.Value;
            if (companyIdClaim == null || !int.TryParse(companyIdClaim, out var companyId))
            {
                // ERR-003: Log authorization failure
                _logger.LogWarning("Unauthorized API access attempt. Endpoint={Endpoint}, Path={Path}, HasCompanyClaim={HasClaim}",
                    nameof(GetSummary), HttpContext.Request.Path, companyIdClaim != null);
                return Unauthorized(ApiErrorResponse.Unauthorized("Invalid authentication"));
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
                    return BadRequest(ApiErrorResponse.BadRequest("Invalid startDate format. Use yyyy-MM-dd"));
                }
                start = parsed;
            }

            if (!string.IsNullOrEmpty(endDate))
            {
                if (!DateOnly.TryParse(endDate, out var parsed))
                {
                    _logger.LogWarning("Invalid endDate format. Endpoint={Endpoint}, CompanyId={CompanyId}, EndDate={EndDate}",
                        nameof(GetSummary), companyId, endDate);
                    return BadRequest(ApiErrorResponse.BadRequest("Invalid endDate format. Use yyyy-MM-dd"));
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
            return StatusCode(500, ApiErrorResponse.ServerError("An error occurred while processing your request"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in {Endpoint}. CompanyId={CompanyId}, Path={Path}",
                nameof(GetSummary), User.FindFirst("CompanyId")?.Value, HttpContext.Request.Path);
            return StatusCode(500, ApiErrorResponse.ServerError("An unexpected error occurred"));
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
