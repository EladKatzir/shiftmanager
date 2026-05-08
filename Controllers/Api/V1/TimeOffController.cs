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
public partial class TimeOffController : ControllerBase
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
                LogApiEndpointNotEnabled(_logger, nameof(ListTimeOffRequests), HttpContext.Request.Path);
                return NotFound(ApiErrorResponse.Create("ENDPOINT_DISABLED", "This API endpoint is not enabled"));
            }

            // Get CompanyId from claims
            var companyIdClaim = User.FindFirst("CompanyId")?.Value;
            if (companyIdClaim == null || !int.TryParse(companyIdClaim, out var companyId))
            {
                // ERR-003: Log authorization failure
                LogApiUnauthorized(_logger, nameof(ListTimeOffRequests), HttpContext.Request.Path, companyIdClaim != null);
                return Unauthorized(ApiErrorResponse.Unauthorized("Invalid authentication"));
            }

            // Parse date filters
            DateOnly? startDateParsed = null;
            DateOnly? endDateParsed = null;

            if (!string.IsNullOrEmpty(startDate))
            {
                if (!DateOnly.TryParse(startDate, out var parsed))
                {
                    LogInvalidStartDateLower(_logger, nameof(ListTimeOffRequests), companyId, startDate);
                    return BadRequest(ApiErrorResponse.BadRequest("Invalid startDate format. Use yyyy-MM-dd"));
                }
                startDateParsed = parsed;
            }

            if (!string.IsNullOrEmpty(endDate))
            {
                if (!DateOnly.TryParse(endDate, out var parsed))
                {
                    LogInvalidEndDateLower(_logger, nameof(ListTimeOffRequests), companyId, endDate);
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
            LogDbErrorCompanyPath(_logger, ex, nameof(ListTimeOffRequests), User.FindFirst("CompanyId")?.Value, HttpContext.Request.Path);
            return StatusCode(500, ApiErrorResponse.ServerError("An error occurred while processing your request"));
        }
        catch (Exception ex)
        {
            LogUnexpectedErrorCompanyPath(_logger, ex, nameof(ListTimeOffRequests), User.FindFirst("CompanyId")?.Value, HttpContext.Request.Path);
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
                LogApiEndpointNotEnabled(_logger, nameof(GetTimeOffRequest), HttpContext.Request.Path);
                return NotFound(ApiErrorResponse.Create("ENDPOINT_DISABLED", "This API endpoint is not enabled"));
            }

            // Get CompanyId from claims
            var companyIdClaim = User.FindFirst("CompanyId")?.Value;
            if (companyIdClaim == null || !int.TryParse(companyIdClaim, out var companyId))
            {
                // ERR-003: Log authorization failure
                LogApiUnauthorized(_logger, nameof(GetTimeOffRequest), HttpContext.Request.Path, companyIdClaim != null);
                return Unauthorized(ApiErrorResponse.Unauthorized("Invalid authentication"));
            }

            var request = await _timeOffService.GetTimeOffRequestAsync(companyId, id);

            if (request == null)
            {
                LogTimeOffNotFound(_logger, nameof(GetTimeOffRequest), companyId, id);
                return NotFound(ApiErrorResponse.NotFound("TimeOff", id.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            }

            return Ok(request);
        }
        catch (DbUpdateException ex)
        {
            LogDbErrorWithRequestId(_logger, ex, nameof(GetTimeOffRequest), User.FindFirst("CompanyId")?.Value, id, HttpContext.Request.Path);
            return StatusCode(500, ApiErrorResponse.ServerError("An error occurred while processing your request"));
        }
        catch (Exception ex)
        {
            LogUnexpectedErrorWithRequestId(_logger, ex, nameof(GetTimeOffRequest), User.FindFirst("CompanyId")?.Value, id, HttpContext.Request.Path);
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
                LogApiEndpointNotEnabled(_logger, nameof(CreateTimeOffRequest), HttpContext.Request.Path);
                return NotFound(ApiErrorResponse.Create("ENDPOINT_DISABLED", "This API endpoint is not enabled"));
            }

            // Get CompanyId from claims
            var companyIdClaim = User.FindFirst("CompanyId")?.Value;
            if (companyIdClaim == null || !int.TryParse(companyIdClaim, out var companyId))
            {
                // ERR-003: Log authorization failure
                LogApiUnauthorized(_logger, nameof(CreateTimeOffRequest), HttpContext.Request.Path, companyIdClaim != null);
                return Unauthorized(ApiErrorResponse.Unauthorized("Invalid authentication"));
            }

            // Validate request
            if (request.UserId <= 0)
            {
                LogValidationUserIdRequired(_logger, nameof(CreateTimeOffRequest), companyId);
                return BadRequest(ApiErrorResponse.BadRequest("UserId is required"));
            }

            if (string.IsNullOrEmpty(request.StartDate))
            {
                LogValidationStartDateRequired(_logger, nameof(CreateTimeOffRequest), companyId, request.UserId);
                return BadRequest(ApiErrorResponse.BadRequest("StartDate is required"));
            }

            if (string.IsNullOrEmpty(request.EndDate))
            {
                LogValidationEndDateRequired(_logger, nameof(CreateTimeOffRequest), companyId, request.UserId);
                return BadRequest(ApiErrorResponse.BadRequest("EndDate is required"));
            }

            // Parse dates
            if (!DateOnly.TryParse(request.StartDate, out var startDate))
            {
                LogInvalidStartDateUpper(_logger, nameof(CreateTimeOffRequest), companyId, request.StartDate);
                return BadRequest(ApiErrorResponse.BadRequest("Invalid StartDate format. Use yyyy-MM-dd"));
            }

            if (!DateOnly.TryParse(request.EndDate, out var endDate))
            {
                LogInvalidEndDateUpper(_logger, nameof(CreateTimeOffRequest), companyId, request.EndDate);
                return BadRequest(ApiErrorResponse.BadRequest("Invalid EndDate format. Use yyyy-MM-dd"));
            }

            var (timeOffRequest, error) = await _timeOffService.CreateTimeOffRequestAsync(
                companyId, request.UserId, startDate, endDate, request.Reason);

            if (error != null)
            {
                if (error.Contains("overlaps"))
                {
                    LogTimeOffConflict(_logger, nameof(CreateTimeOffRequest), companyId, request.UserId, error);
                    return Conflict(ApiErrorResponse.Conflict(error));
                }
                LogValidationCreateError(_logger, nameof(CreateTimeOffRequest), companyId, request.UserId, error);
                return BadRequest(ApiErrorResponse.BadRequest(error));
            }

            LogTimeOffCreated(_logger, nameof(CreateTimeOffRequest), companyId, request.UserId, timeOffRequest!.Id);

            return CreatedAtAction(nameof(GetTimeOffRequest), new { id = timeOffRequest!.Id }, timeOffRequest);
        }
        catch (DbUpdateException ex)
        {
            LogDbErrorWithUserId(_logger, ex, nameof(CreateTimeOffRequest), User.FindFirst("CompanyId")?.Value, request?.UserId, HttpContext.Request.Path);
            return StatusCode(500, ApiErrorResponse.ServerError("An error occurred while processing your request"));
        }
        catch (Exception ex)
        {
            LogUnexpectedErrorWithUserId(_logger, ex, nameof(CreateTimeOffRequest), User.FindFirst("CompanyId")?.Value, request?.UserId, HttpContext.Request.Path);
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
                LogApiEndpointNotEnabled(_logger, nameof(ApproveTimeOffRequest), HttpContext.Request.Path);
                return NotFound(ApiErrorResponse.Create("ENDPOINT_DISABLED", "This API endpoint is not enabled"));
            }

            // Get CompanyId from claims
            var companyIdClaim = User.FindFirst("CompanyId")?.Value;
            if (companyIdClaim == null || !int.TryParse(companyIdClaim, out var companyId))
            {
                // ERR-003: Log authorization failure
                LogApiUnauthorized(_logger, nameof(ApproveTimeOffRequest), HttpContext.Request.Path, companyIdClaim != null);
                return Unauthorized(ApiErrorResponse.Unauthorized("Invalid authentication"));
            }

            var (timeOffRequest, error) = await _timeOffService.ApproveTimeOffRequestAsync(companyId, id);

            if (error != null)
            {
                if (error.Contains("not found"))
                {
                    LogTimeOffNotFoundForApproval(_logger, nameof(ApproveTimeOffRequest), companyId, id);
                    return NotFound(ApiErrorResponse.Create("NOT_FOUND", error));
                }
                LogValidationApproveError(_logger, nameof(ApproveTimeOffRequest), companyId, id, error);
                return BadRequest(ApiErrorResponse.BadRequest(error));
            }

            LogTimeOffApproved(_logger, nameof(ApproveTimeOffRequest), companyId, id);

            return Ok(timeOffRequest);
        }
        catch (DbUpdateException ex)
        {
            LogDbErrorWithRequestId(_logger, ex, nameof(ApproveTimeOffRequest), User.FindFirst("CompanyId")?.Value, id, HttpContext.Request.Path);
            return StatusCode(500, ApiErrorResponse.ServerError("An error occurred while processing your request"));
        }
        catch (Exception ex)
        {
            LogUnexpectedErrorWithRequestId(_logger, ex, nameof(ApproveTimeOffRequest), User.FindFirst("CompanyId")?.Value, id, HttpContext.Request.Path);
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
                LogApiEndpointNotEnabled(_logger, nameof(DeclineTimeOffRequest), HttpContext.Request.Path);
                return NotFound(ApiErrorResponse.Create("ENDPOINT_DISABLED", "This API endpoint is not enabled"));
            }

            // Get CompanyId from claims
            var companyIdClaim = User.FindFirst("CompanyId")?.Value;
            if (companyIdClaim == null || !int.TryParse(companyIdClaim, out var companyId))
            {
                // ERR-003: Log authorization failure
                LogApiUnauthorized(_logger, nameof(DeclineTimeOffRequest), HttpContext.Request.Path, companyIdClaim != null);
                return Unauthorized(ApiErrorResponse.Unauthorized("Invalid authentication"));
            }

            var (timeOffRequest, error) = await _timeOffService.DeclineTimeOffRequestAsync(companyId, id);

            if (error != null)
            {
                if (error.Contains("not found"))
                {
                    LogTimeOffNotFoundForDecline(_logger, nameof(DeclineTimeOffRequest), companyId, id);
                    return NotFound(ApiErrorResponse.Create("NOT_FOUND", error));
                }
                LogValidationDeclineError(_logger, nameof(DeclineTimeOffRequest), companyId, id, error);
                return BadRequest(ApiErrorResponse.BadRequest(error));
            }

            LogTimeOffDeclined(_logger, nameof(DeclineTimeOffRequest), companyId, id);

            return Ok(timeOffRequest);
        }
        catch (DbUpdateException ex)
        {
            LogDbErrorWithRequestId(_logger, ex, nameof(DeclineTimeOffRequest), User.FindFirst("CompanyId")?.Value, id, HttpContext.Request.Path);
            return StatusCode(500, ApiErrorResponse.ServerError("An error occurred while processing your request"));
        }
        catch (Exception ex)
        {
            LogUnexpectedErrorWithRequestId(_logger, ex, nameof(DeclineTimeOffRequest), User.FindFirst("CompanyId")?.Value, id, HttpContext.Request.Path);
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
