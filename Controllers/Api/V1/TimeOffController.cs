using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data.SeedData;
using ShiftManager.Models;
using ShiftManager.Models.Api.Dto;
using ShiftManager.Services;
using ShiftManager.Services.Api;

namespace ShiftManager.Controllers.Api.V1;

/// <summary>
/// API controller for time-off request management.
/// All endpoints require API key authentication via X-API-Key header.
/// </summary>
[Authorize]
[ApiController]
[Route("api/v1/time-off-requests")]
[Produces("application/json")]
public class TimeOffController : ControllerBase
{
    private readonly TimeOffApiService _timeOffService;
    private readonly IFeatureFlagService _featureFlagService;
    private readonly ILogger<TimeOffController> _logger;

    public TimeOffController(
        TimeOffApiService timeOffService,
        IFeatureFlagService featureFlagService,
        ILogger<TimeOffController> logger)
    {
        _timeOffService = timeOffService;
        _featureFlagService = featureFlagService;
        _logger = logger;
    }

    /// <summary>
    /// Lists time-off requests with pagination and filtering.
    /// Requires scope: timeoff:read
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(PaginatedResponse<TimeOffDto>), 200)]
    [ProducesResponseType(typeof(ApiErrorResponse), 401)]
    [ProducesResponseType(typeof(ApiErrorResponse), 403)]
    [ProducesResponseType(typeof(ApiErrorResponse), 429)]
    public async Task<IActionResult> ListTimeOffRequests(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] int? userId = null,
        [FromQuery] string? status = null,
        [FromQuery] string? startDate = null,
        [FromQuery] string? endDate = null)
    {
        try
        {
            // Check feature flag
            if (!await _featureFlagService.IsEnabledAsync(FeatureFlagSeed.Flags.ApiTimeOffList))
            {
                _logger.LogWarning("API endpoint not enabled. Endpoint={Endpoint}, Path={Path}",
                    nameof(ListTimeOffRequests), HttpContext.Request.Path);
                return NotFound(ApiErrorResponse.Create("ENDPOINT_DISABLED", "This API endpoint is not enabled"));
            }

            // Get CompanyId from claims
            var companyIdClaim = User.FindFirst("CompanyId")?.Value;
            if (companyIdClaim == null || !int.TryParse(companyIdClaim, out var companyId))
            {
                // ERR-003: Log authorization failure
                _logger.LogWarning("Unauthorized API access attempt. Endpoint={Endpoint}, Path={Path}, HasCompanyClaim={HasClaim}",
                    nameof(ListTimeOffRequests), HttpContext.Request.Path, companyIdClaim != null);
                return Unauthorized(ApiErrorResponse.Unauthorized("Invalid authentication"));
            }

            // Parse date filters
            DateOnly? startDateParsed = null;
            DateOnly? endDateParsed = null;

            if (!string.IsNullOrEmpty(startDate))
            {
                if (!DateOnly.TryParse(startDate, out var parsed))
                {
                    _logger.LogWarning("Invalid startDate format. Endpoint={Endpoint}, CompanyId={CompanyId}, StartDate={StartDate}",
                        nameof(ListTimeOffRequests), companyId, startDate);
                    return BadRequest(ApiErrorResponse.BadRequest("Invalid startDate format. Use yyyy-MM-dd"));
                }
                startDateParsed = parsed;
            }

            if (!string.IsNullOrEmpty(endDate))
            {
                if (!DateOnly.TryParse(endDate, out var parsed))
                {
                    _logger.LogWarning("Invalid endDate format. Endpoint={Endpoint}, CompanyId={CompanyId}, EndDate={EndDate}",
                        nameof(ListTimeOffRequests), companyId, endDate);
                    return BadRequest(ApiErrorResponse.BadRequest("Invalid endDate format. Use yyyy-MM-dd"));
                }
                endDateParsed = parsed;
            }

            var (requests, totalCount) = await _timeOffService.ListTimeOffRequestsAsync(
                companyId, page, pageSize, userId, status, startDateParsed, endDateParsed);

            var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
            var response = new PaginatedResponse<TimeOffDto>
            {
                Data = requests,
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
                nameof(ListTimeOffRequests), User.FindFirst("CompanyId")?.Value, HttpContext.Request.Path);
            return StatusCode(500, ApiErrorResponse.ServerError("An error occurred while processing your request"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in {Endpoint}. CompanyId={CompanyId}, Path={Path}",
                nameof(ListTimeOffRequests), User.FindFirst("CompanyId")?.Value, HttpContext.Request.Path);
            return StatusCode(500, ApiErrorResponse.ServerError("An unexpected error occurred"));
        }
    }

    /// <summary>
    /// Gets a single time-off request by ID.
    /// Requires scope: timeoff:read
    /// </summary>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(TimeOffDto), 200)]
    [ProducesResponseType(typeof(ApiErrorResponse), 401)]
    [ProducesResponseType(typeof(ApiErrorResponse), 403)]
    [ProducesResponseType(typeof(ApiErrorResponse), 404)]
    [ProducesResponseType(typeof(ApiErrorResponse), 429)]
    public async Task<IActionResult> GetTimeOffRequest(int id)
    {
        try
        {
            // Check feature flag
            if (!await _featureFlagService.IsEnabledAsync(FeatureFlagSeed.Flags.ApiTimeOffGet))
            {
                _logger.LogWarning("API endpoint not enabled. Endpoint={Endpoint}, Path={Path}",
                    nameof(GetTimeOffRequest), HttpContext.Request.Path);
                return NotFound(ApiErrorResponse.Create("ENDPOINT_DISABLED", "This API endpoint is not enabled"));
            }

            // Get CompanyId from claims
            var companyIdClaim = User.FindFirst("CompanyId")?.Value;
            if (companyIdClaim == null || !int.TryParse(companyIdClaim, out var companyId))
            {
                // ERR-003: Log authorization failure
                _logger.LogWarning("Unauthorized API access attempt. Endpoint={Endpoint}, Path={Path}, HasCompanyClaim={HasClaim}",
                    nameof(GetTimeOffRequest), HttpContext.Request.Path, companyIdClaim != null);
                return Unauthorized(ApiErrorResponse.Unauthorized("Invalid authentication"));
            }

            var request = await _timeOffService.GetTimeOffRequestAsync(companyId, id);

            if (request == null)
            {
                _logger.LogWarning("Time-off request not found. Endpoint={Endpoint}, CompanyId={CompanyId}, RequestId={RequestId}",
                    nameof(GetTimeOffRequest), companyId, id);
                return NotFound(ApiErrorResponse.NotFound("TimeOff", id.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            }

            return Ok(request);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Database error in {Endpoint}. CompanyId={CompanyId}, RequestId={RequestId}, Path={Path}",
                nameof(GetTimeOffRequest), User.FindFirst("CompanyId")?.Value, id, HttpContext.Request.Path);
            return StatusCode(500, ApiErrorResponse.ServerError("An error occurred while processing your request"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in {Endpoint}. CompanyId={CompanyId}, RequestId={RequestId}, Path={Path}",
                nameof(GetTimeOffRequest), User.FindFirst("CompanyId")?.Value, id, HttpContext.Request.Path);
            return StatusCode(500, ApiErrorResponse.ServerError("An unexpected error occurred"));
        }
    }

    /// <summary>
    /// Creates a new time-off request.
    /// Requires scope: timeoff:write
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(TimeOffDto), 201)]
    [ProducesResponseType(typeof(ApiErrorResponse), 400)]
    [ProducesResponseType(typeof(ApiErrorResponse), 401)]
    [ProducesResponseType(typeof(ApiErrorResponse), 403)]
    [ProducesResponseType(typeof(ApiErrorResponse), 409)]
    [ProducesResponseType(typeof(ApiErrorResponse), 429)]
    public async Task<IActionResult> CreateTimeOffRequest([FromBody] CreateTimeOffRequest request)
    {
        try
        {
            // Check feature flag
            if (!await _featureFlagService.IsEnabledAsync(FeatureFlagSeed.Flags.ApiTimeOffCreate))
            {
                _logger.LogWarning("API endpoint not enabled. Endpoint={Endpoint}, Path={Path}",
                    nameof(CreateTimeOffRequest), HttpContext.Request.Path);
                return NotFound(ApiErrorResponse.Create("ENDPOINT_DISABLED", "This API endpoint is not enabled"));
            }

            // Get CompanyId from claims
            var companyIdClaim = User.FindFirst("CompanyId")?.Value;
            if (companyIdClaim == null || !int.TryParse(companyIdClaim, out var companyId))
            {
                // ERR-003: Log authorization failure
                _logger.LogWarning("Unauthorized API access attempt. Endpoint={Endpoint}, Path={Path}, HasCompanyClaim={HasClaim}",
                    nameof(CreateTimeOffRequest), HttpContext.Request.Path, companyIdClaim != null);
                return Unauthorized(ApiErrorResponse.Unauthorized("Invalid authentication"));
            }

            // Validate request
            if (request.UserId <= 0)
            {
                _logger.LogWarning("Validation error: UserId required. Endpoint={Endpoint}, CompanyId={CompanyId}",
                    nameof(CreateTimeOffRequest), companyId);
                return BadRequest(ApiErrorResponse.BadRequest("UserId is required"));
            }

            if (string.IsNullOrEmpty(request.StartDate))
            {
                _logger.LogWarning("Validation error: StartDate required. Endpoint={Endpoint}, CompanyId={CompanyId}, UserId={UserId}",
                    nameof(CreateTimeOffRequest), companyId, request.UserId);
                return BadRequest(ApiErrorResponse.BadRequest("StartDate is required"));
            }

            if (string.IsNullOrEmpty(request.EndDate))
            {
                _logger.LogWarning("Validation error: EndDate required. Endpoint={Endpoint}, CompanyId={CompanyId}, UserId={UserId}",
                    nameof(CreateTimeOffRequest), companyId, request.UserId);
                return BadRequest(ApiErrorResponse.BadRequest("EndDate is required"));
            }

            // Parse dates
            if (!DateOnly.TryParse(request.StartDate, out var startDate))
            {
                _logger.LogWarning("Invalid StartDate format. Endpoint={Endpoint}, CompanyId={CompanyId}, StartDate={StartDate}",
                    nameof(CreateTimeOffRequest), companyId, request.StartDate);
                return BadRequest(ApiErrorResponse.BadRequest("Invalid StartDate format. Use yyyy-MM-dd"));
            }

            if (!DateOnly.TryParse(request.EndDate, out var endDate))
            {
                _logger.LogWarning("Invalid EndDate format. Endpoint={Endpoint}, CompanyId={CompanyId}, EndDate={EndDate}",
                    nameof(CreateTimeOffRequest), companyId, request.EndDate);
                return BadRequest(ApiErrorResponse.BadRequest("Invalid EndDate format. Use yyyy-MM-dd"));
            }

            var (timeOffRequest, error) = await _timeOffService.CreateTimeOffRequestAsync(
                companyId, request.UserId, startDate, endDate, request.Reason);

            if (error != null)
            {
                if (error.Contains("overlaps"))
                {
                    _logger.LogWarning("Time-off request conflict. Endpoint={Endpoint}, CompanyId={CompanyId}, UserId={UserId}, Error={Error}",
                        nameof(CreateTimeOffRequest), companyId, request.UserId, error);
                    return Conflict(ApiErrorResponse.Conflict(error));
                }
                _logger.LogWarning("Time-off request validation error. Endpoint={Endpoint}, CompanyId={CompanyId}, UserId={UserId}, Error={Error}",
                    nameof(CreateTimeOffRequest), companyId, request.UserId, error);
                return BadRequest(ApiErrorResponse.BadRequest(error));
            }

            _logger.LogInformation("Time-off request created. Endpoint={Endpoint}, CompanyId={CompanyId}, UserId={UserId}, RequestId={RequestId}",
                nameof(CreateTimeOffRequest), companyId, request.UserId, timeOffRequest!.Id);

            return CreatedAtAction(nameof(GetTimeOffRequest), new { id = timeOffRequest!.Id }, timeOffRequest);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Database error in {Endpoint}. CompanyId={CompanyId}, UserId={UserId}, Path={Path}",
                nameof(CreateTimeOffRequest), User.FindFirst("CompanyId")?.Value, request?.UserId, HttpContext.Request.Path);
            return StatusCode(500, ApiErrorResponse.ServerError("An error occurred while processing your request"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in {Endpoint}. CompanyId={CompanyId}, UserId={UserId}, Path={Path}",
                nameof(CreateTimeOffRequest), User.FindFirst("CompanyId")?.Value, request?.UserId, HttpContext.Request.Path);
            return StatusCode(500, ApiErrorResponse.ServerError("An unexpected error occurred"));
        }
    }

    /// <summary>
    /// Approves a time-off request.
    /// Requires scope: timeoff:approve
    /// </summary>
    [HttpPost("{id}/approve")]
    [ProducesResponseType(typeof(TimeOffDto), 200)]
    [ProducesResponseType(typeof(ApiErrorResponse), 400)]
    [ProducesResponseType(typeof(ApiErrorResponse), 401)]
    [ProducesResponseType(typeof(ApiErrorResponse), 403)]
    [ProducesResponseType(typeof(ApiErrorResponse), 404)]
    [ProducesResponseType(typeof(ApiErrorResponse), 429)]
    public async Task<IActionResult> ApproveTimeOffRequest(int id)
    {
        try
        {
            // Check feature flag
            if (!await _featureFlagService.IsEnabledAsync(FeatureFlagSeed.Flags.ApiTimeOffApprove))
            {
                _logger.LogWarning("API endpoint not enabled. Endpoint={Endpoint}, Path={Path}",
                    nameof(ApproveTimeOffRequest), HttpContext.Request.Path);
                return NotFound(ApiErrorResponse.Create("ENDPOINT_DISABLED", "This API endpoint is not enabled"));
            }

            // Get CompanyId from claims
            var companyIdClaim = User.FindFirst("CompanyId")?.Value;
            if (companyIdClaim == null || !int.TryParse(companyIdClaim, out var companyId))
            {
                // ERR-003: Log authorization failure
                _logger.LogWarning("Unauthorized API access attempt. Endpoint={Endpoint}, Path={Path}, HasCompanyClaim={HasClaim}",
                    nameof(ApproveTimeOffRequest), HttpContext.Request.Path, companyIdClaim != null);
                return Unauthorized(ApiErrorResponse.Unauthorized("Invalid authentication"));
            }

            var (timeOffRequest, error) = await _timeOffService.ApproveTimeOffRequestAsync(companyId, id);

            if (error != null)
            {
                if (error.Contains("not found"))
                {
                    _logger.LogWarning("Time-off request not found for approval. Endpoint={Endpoint}, CompanyId={CompanyId}, RequestId={RequestId}",
                        nameof(ApproveTimeOffRequest), companyId, id);
                    return NotFound(ApiErrorResponse.Create("NOT_FOUND", error));
                }
                _logger.LogWarning("Time-off request approval validation error. Endpoint={Endpoint}, CompanyId={CompanyId}, RequestId={RequestId}, Error={Error}",
                    nameof(ApproveTimeOffRequest), companyId, id, error);
                return BadRequest(ApiErrorResponse.BadRequest(error));
            }

            _logger.LogInformation("Time-off request approved. Endpoint={Endpoint}, CompanyId={CompanyId}, RequestId={RequestId}",
                nameof(ApproveTimeOffRequest), companyId, id);

            return Ok(timeOffRequest);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Database error in {Endpoint}. CompanyId={CompanyId}, RequestId={RequestId}, Path={Path}",
                nameof(ApproveTimeOffRequest), User.FindFirst("CompanyId")?.Value, id, HttpContext.Request.Path);
            return StatusCode(500, ApiErrorResponse.ServerError("An error occurred while processing your request"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in {Endpoint}. CompanyId={CompanyId}, RequestId={RequestId}, Path={Path}",
                nameof(ApproveTimeOffRequest), User.FindFirst("CompanyId")?.Value, id, HttpContext.Request.Path);
            return StatusCode(500, ApiErrorResponse.ServerError("An unexpected error occurred"));
        }
    }

    /// <summary>
    /// Declines a time-off request.
    /// Requires scope: timeoff:approve
    /// </summary>
    [HttpPost("{id}/decline")]
    [ProducesResponseType(typeof(TimeOffDto), 200)]
    [ProducesResponseType(typeof(ApiErrorResponse), 400)]
    [ProducesResponseType(typeof(ApiErrorResponse), 401)]
    [ProducesResponseType(typeof(ApiErrorResponse), 403)]
    [ProducesResponseType(typeof(ApiErrorResponse), 404)]
    [ProducesResponseType(typeof(ApiErrorResponse), 429)]
    public async Task<IActionResult> DeclineTimeOffRequest(int id)
    {
        try
        {
            // Check feature flag
            if (!await _featureFlagService.IsEnabledAsync(FeatureFlagSeed.Flags.ApiTimeOffDecline))
            {
                _logger.LogWarning("API endpoint not enabled. Endpoint={Endpoint}, Path={Path}",
                    nameof(DeclineTimeOffRequest), HttpContext.Request.Path);
                return NotFound(ApiErrorResponse.Create("ENDPOINT_DISABLED", "This API endpoint is not enabled"));
            }

            // Get CompanyId from claims
            var companyIdClaim = User.FindFirst("CompanyId")?.Value;
            if (companyIdClaim == null || !int.TryParse(companyIdClaim, out var companyId))
            {
                // ERR-003: Log authorization failure
                _logger.LogWarning("Unauthorized API access attempt. Endpoint={Endpoint}, Path={Path}, HasCompanyClaim={HasClaim}",
                    nameof(DeclineTimeOffRequest), HttpContext.Request.Path, companyIdClaim != null);
                return Unauthorized(ApiErrorResponse.Unauthorized("Invalid authentication"));
            }

            var (timeOffRequest, error) = await _timeOffService.DeclineTimeOffRequestAsync(companyId, id);

            if (error != null)
            {
                if (error.Contains("not found"))
                {
                    _logger.LogWarning("Time-off request not found for decline. Endpoint={Endpoint}, CompanyId={CompanyId}, RequestId={RequestId}",
                        nameof(DeclineTimeOffRequest), companyId, id);
                    return NotFound(ApiErrorResponse.Create("NOT_FOUND", error));
                }
                _logger.LogWarning("Time-off request decline validation error. Endpoint={Endpoint}, CompanyId={CompanyId}, RequestId={RequestId}, Error={Error}",
                    nameof(DeclineTimeOffRequest), companyId, id, error);
                return BadRequest(ApiErrorResponse.BadRequest(error));
            }

            _logger.LogInformation("Time-off request declined. Endpoint={Endpoint}, CompanyId={CompanyId}, RequestId={RequestId}",
                nameof(DeclineTimeOffRequest), companyId, id);

            return Ok(timeOffRequest);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Database error in {Endpoint}. CompanyId={CompanyId}, RequestId={RequestId}, Path={Path}",
                nameof(DeclineTimeOffRequest), User.FindFirst("CompanyId")?.Value, id, HttpContext.Request.Path);
            return StatusCode(500, ApiErrorResponse.ServerError("An error occurred while processing your request"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in {Endpoint}. CompanyId={CompanyId}, RequestId={RequestId}, Path={Path}",
                nameof(DeclineTimeOffRequest), User.FindFirst("CompanyId")?.Value, id, HttpContext.Request.Path);
            return StatusCode(500, ApiErrorResponse.ServerError("An unexpected error occurred"));
        }
    }
}

/// <summary>
/// Request model for creating a time-off request
/// </summary>
public class CreateTimeOffRequest
{
    public int UserId { get; set; }
    public string StartDate { get; set; } = string.Empty;
    public string EndDate { get; set; } = string.Empty;
    public string? Reason { get; set; }
}
