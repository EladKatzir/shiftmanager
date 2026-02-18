using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Data.SeedData;
using ShiftManager.Models.Api;
using ShiftManager.Models.Api.Dto;
using ShiftManager.Services;

namespace ShiftManager.Controllers.Api.V1;

/// <summary>
/// API controller for audit log access.
/// All endpoints require API key authentication via X-API-Key header.
/// </summary>
[Authorize]
[ApiController]
[Route("api/v1/audit-logs")]
[Produces("application/json")]
public class AuditLogsController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IFeatureFlagService _featureFlagService;
    private readonly ILogger<AuditLogsController> _logger;

    public AuditLogsController(
        AppDbContext context,
        IFeatureFlagService featureFlagService,
        ILogger<AuditLogsController> logger)
    {
        _context = context;
        _featureFlagService = featureFlagService;
        _logger = logger;
    }

    /// <summary>
    /// Lists audit logs with pagination and filtering.
    /// Requires scope: audit:read
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(PaginatedResponse<AuditLogDto>), 200)]
    [ProducesResponseType(typeof(ApiProblemDetails), 401)]
    [ProducesResponseType(typeof(ApiProblemDetails), 403)]
    [ProducesResponseType(typeof(ApiProblemDetails), 429)]
    public async Task<IActionResult> ListAuditLogs(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] int? userId = null,
        [FromQuery] string? action = null,
        [FromQuery] string? startDate = null,
        [FromQuery] string? endDate = null)
    {
        try
        {
            // Check feature flag
            if (!await _featureFlagService.IsEnabledAsync(FeatureFlagSeed.Flags.ApiAuditLogsList))
            {
                _logger.LogWarning("API endpoint not enabled. Endpoint={Endpoint}, Path={Path}",
                    nameof(ListAuditLogs), HttpContext.Request.Path);
                return NotFound(ApiProblemDetails.NotFound("This API endpoint is not enabled", HttpContext.Request.Path));
            }

            // Get CompanyId from claims
            var companyIdClaim = User.FindFirst("CompanyId")?.Value;
            if (companyIdClaim == null || !int.TryParse(companyIdClaim, out var companyId))
            {
                // ERR-003: Log authorization failure
                _logger.LogWarning("Unauthorized API access attempt. Endpoint={Endpoint}, Path={Path}, HasCompanyClaim={HasClaim}",
                    nameof(ListAuditLogs), HttpContext.Request.Path, companyIdClaim != null);
                return Unauthorized(ApiProblemDetails.Unauthorized("Invalid authentication", HttpContext.Request.Path));
            }

            // Validate pagination
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 50;
            if (pageSize > 100) pageSize = 100;

            var query = _context.AuditLogs.AsQueryable();
            query = query.Where(a => a.CompanyId == companyId);

            // Apply filters
            if (userId.HasValue)
            {
                query = query.Where(a => a.UserId == userId.Value);
            }

            if (!string.IsNullOrEmpty(action))
            {
                query = query.Where(a => a.Action.Contains(action));
            }

            // Parse date filters
            if (!string.IsNullOrEmpty(startDate))
            {
                if (DateTime.TryParse(startDate, out var start))
                {
                    query = query.Where(a => a.Timestamp >= start);
                }
                else
                {
                    _logger.LogWarning("Invalid startDate format. Endpoint={Endpoint}, CompanyId={CompanyId}, StartDate={StartDate}",
                        nameof(ListAuditLogs), companyId, startDate);
                }
            }

            if (!string.IsNullOrEmpty(endDate))
            {
                if (DateTime.TryParse(endDate, out var end))
                {
                    query = query.Where(a => a.Timestamp <= end);
                }
                else
                {
                    _logger.LogWarning("Invalid endDate format. Endpoint={Endpoint}, CompanyId={CompanyId}, EndDate={EndDate}",
                        nameof(ListAuditLogs), companyId, endDate);
                }
            }

            // Get total count
            var totalCount = await query.CountAsync();

            // Apply pagination
            var logs = await query
                .OrderByDescending(a => a.Timestamp)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            // Map to DTOs
            var dtos = logs.Select(l => new AuditLogDto
            {
                Id = l.Id,
                UserId = l.UserId,
                UserEmail = l.UserEmail,
                UserDisplayName = l.UserDisplayName,
                Action = l.Action,
                EntityType = l.EntityType,
                EntityId = l.EntityId,
                Description = l.Description,
                Details = l.Details,
                IpAddress = l.IpAddress,
                UserAgent = l.UserAgent,
                Timestamp = l.Timestamp.ToString("O")
            }).ToList();

            var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
            var response = new PaginatedResponse<AuditLogDto>
            {
                Data = dtos,
                Pagination = new PaginationInfo
                {
                    Page = page,
                    PageSize = pageSize,
                    TotalCount = totalCount,
                    TotalPages = totalPages
                }
            };

            return Ok(response);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Database error in {Endpoint}. CompanyId={CompanyId}, Path={Path}",
                nameof(ListAuditLogs), User.FindFirst("CompanyId")?.Value, HttpContext.Request.Path);
            return StatusCode(500, ApiProblemDetails.InternalError(
                "An error occurred while processing your request", HttpContext.Request.Path));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in {Endpoint}. CompanyId={CompanyId}, Path={Path}",
                nameof(ListAuditLogs), User.FindFirst("CompanyId")?.Value, HttpContext.Request.Path);
            return StatusCode(500, ApiProblemDetails.InternalError(
                "An unexpected error occurred", HttpContext.Request.Path));
        }
    }
}

/// <summary>
/// Audit log DTO
/// </summary>
public class AuditLogDto
{
    public int Id { get; set; }
    public int? UserId { get; set; }
    public string UserEmail { get; set; } = string.Empty;
    public string UserDisplayName { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string? EntityType { get; set; }
    public int? EntityId { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? Details { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string Timestamp { get; set; } = string.Empty;
}
