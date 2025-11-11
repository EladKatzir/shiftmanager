using Microsoft.AspNetCore.Mvc;
using ShiftManager.Models.Api;
using ShiftManager.Models.Api.Dto;
using ShiftManager.Services.Api;

namespace ShiftManager.Controllers.Api.V1;

/// <summary>
/// API controller for time-off request management.
/// All endpoints require API key authentication via X-API-Key header.
/// </summary>
[ApiController]
[Route("api/v1/time-off-requests")]
[Produces("application/json")]
public class TimeOffController : ControllerBase
{
    private readonly TimeOffApiService _timeOffService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<TimeOffController> _logger;

    public TimeOffController(
        TimeOffApiService timeOffService,
        IConfiguration configuration,
        ILogger<TimeOffController> logger)
    {
        _timeOffService = timeOffService;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Lists time-off requests with pagination and filtering.
    /// Requires scope: timeoff:read
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(PaginatedResponse<TimeOffDto>), 200)]
    [ProducesResponseType(typeof(ApiProblemDetails), 401)]
    [ProducesResponseType(typeof(ApiProblemDetails), 403)]
    [ProducesResponseType(typeof(ApiProblemDetails), 429)]
    public async Task<IActionResult> ListTimeOffRequests(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] int? userId = null,
        [FromQuery] string? status = null,
        [FromQuery] string? startDate = null,
        [FromQuery] string? endDate = null)
    {
        if (!_configuration.GetValue<bool>("Features:Api:TimeOff:ListEnabled", false))
        {
            return NotFound(ApiProblemDetails.NotFound("This API endpoint is not enabled", HttpContext.Request.Path));
        }

        var companyIdClaim = User.FindFirst("CompanyId")?.Value;
        if (companyIdClaim == null || !int.TryParse(companyIdClaim, out var companyId))
        {
            return Unauthorized(ApiProblemDetails.Unauthorized("Invalid authentication", HttpContext.Request.Path));
        }

        // Parse date filters
        DateOnly? startDateParsed = null;
        DateOnly? endDateParsed = null;

        if (!string.IsNullOrEmpty(startDate))
        {
            if (!DateOnly.TryParse(startDate, out var parsed))
            {
                return BadRequest(ApiProblemDetails.ValidationError(
                    "Invalid startDate format. Use yyyy-MM-dd", HttpContext.Request.Path));
            }
            startDateParsed = parsed;
        }

        if (!string.IsNullOrEmpty(endDate))
        {
            if (!DateOnly.TryParse(endDate, out var parsed))
            {
                return BadRequest(ApiProblemDetails.ValidationError(
                    "Invalid endDate format. Use yyyy-MM-dd", HttpContext.Request.Path));
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

    /// <summary>
    /// Gets a single time-off request by ID.
    /// Requires scope: timeoff:read
    /// </summary>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(TimeOffDto), 200)]
    [ProducesResponseType(typeof(ApiProblemDetails), 401)]
    [ProducesResponseType(typeof(ApiProblemDetails), 403)]
    [ProducesResponseType(typeof(ApiProblemDetails), 404)]
    [ProducesResponseType(typeof(ApiProblemDetails), 429)]
    public async Task<IActionResult> GetTimeOffRequest(int id)
    {
        if (!_configuration.GetValue<bool>("Features:Api:TimeOff:GetEnabled", false))
        {
            return NotFound(ApiProblemDetails.NotFound("This API endpoint is not enabled", HttpContext.Request.Path));
        }

        var companyIdClaim = User.FindFirst("CompanyId")?.Value;
        if (companyIdClaim == null || !int.TryParse(companyIdClaim, out var companyId))
        {
            return Unauthorized(ApiProblemDetails.Unauthorized("Invalid authentication", HttpContext.Request.Path));
        }

        var request = await _timeOffService.GetTimeOffRequestAsync(companyId, id);

        if (request == null)
        {
            return NotFound(ApiProblemDetails.NotFound($"Time-off request {id} not found", HttpContext.Request.Path));
        }

        return Ok(request);
    }

    /// <summary>
    /// Creates a new time-off request.
    /// Requires scope: timeoff:write
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(TimeOffDto), 201)]
    [ProducesResponseType(typeof(ApiProblemDetails), 400)]
    [ProducesResponseType(typeof(ApiProblemDetails), 401)]
    [ProducesResponseType(typeof(ApiProblemDetails), 403)]
    [ProducesResponseType(typeof(ApiProblemDetails), 409)]
    [ProducesResponseType(typeof(ApiProblemDetails), 429)]
    public async Task<IActionResult> CreateTimeOffRequest([FromBody] CreateTimeOffRequest request)
    {
        if (!_configuration.GetValue<bool>("Features:Api:TimeOff:CreateEnabled", false))
        {
            return NotFound(ApiProblemDetails.NotFound("This API endpoint is not enabled", HttpContext.Request.Path));
        }

        var companyIdClaim = User.FindFirst("CompanyId")?.Value;
        if (companyIdClaim == null || !int.TryParse(companyIdClaim, out var companyId))
        {
            return Unauthorized(ApiProblemDetails.Unauthorized("Invalid authentication", HttpContext.Request.Path));
        }

        // Validate request
        if (request.UserId <= 0)
        {
            return BadRequest(ApiProblemDetails.ValidationError("UserId is required", HttpContext.Request.Path));
        }

        if (string.IsNullOrEmpty(request.StartDate))
        {
            return BadRequest(ApiProblemDetails.ValidationError("StartDate is required", HttpContext.Request.Path));
        }

        if (string.IsNullOrEmpty(request.EndDate))
        {
            return BadRequest(ApiProblemDetails.ValidationError("EndDate is required", HttpContext.Request.Path));
        }

        // Parse dates
        if (!DateOnly.TryParse(request.StartDate, out var startDate))
        {
            return BadRequest(ApiProblemDetails.ValidationError(
                "Invalid StartDate format. Use yyyy-MM-dd", HttpContext.Request.Path));
        }

        if (!DateOnly.TryParse(request.EndDate, out var endDate))
        {
            return BadRequest(ApiProblemDetails.ValidationError(
                "Invalid EndDate format. Use yyyy-MM-dd", HttpContext.Request.Path));
        }

        var (timeOffRequest, error) = await _timeOffService.CreateTimeOffRequestAsync(
            companyId, request.UserId, startDate, endDate, request.Reason);

        if (error != null)
        {
            if (error.Contains("overlaps"))
            {
                return Conflict(ApiProblemDetails.Conflict(error, HttpContext.Request.Path));
            }
            return BadRequest(ApiProblemDetails.ValidationError(error, HttpContext.Request.Path));
        }

        return CreatedAtAction(nameof(GetTimeOffRequest), new { id = timeOffRequest!.Id }, timeOffRequest);
    }

    /// <summary>
    /// Approves a time-off request.
    /// Requires scope: timeoff:approve
    /// </summary>
    [HttpPost("{id}/approve")]
    [ProducesResponseType(typeof(TimeOffDto), 200)]
    [ProducesResponseType(typeof(ApiProblemDetails), 400)]
    [ProducesResponseType(typeof(ApiProblemDetails), 401)]
    [ProducesResponseType(typeof(ApiProblemDetails), 403)]
    [ProducesResponseType(typeof(ApiProblemDetails), 404)]
    [ProducesResponseType(typeof(ApiProblemDetails), 429)]
    public async Task<IActionResult> ApproveTimeOffRequest(int id)
    {
        if (!_configuration.GetValue<bool>("Features:Api:TimeOff:ApproveEnabled", false))
        {
            return NotFound(ApiProblemDetails.NotFound("This API endpoint is not enabled", HttpContext.Request.Path));
        }

        var companyIdClaim = User.FindFirst("CompanyId")?.Value;
        if (companyIdClaim == null || !int.TryParse(companyIdClaim, out var companyId))
        {
            return Unauthorized(ApiProblemDetails.Unauthorized("Invalid authentication", HttpContext.Request.Path));
        }

        var (timeOffRequest, error) = await _timeOffService.ApproveTimeOffRequestAsync(companyId, id);

        if (error != null)
        {
            if (error.Contains("not found"))
            {
                return NotFound(ApiProblemDetails.NotFound(error, HttpContext.Request.Path));
            }
            return BadRequest(ApiProblemDetails.ValidationError(error, HttpContext.Request.Path));
        }

        return Ok(timeOffRequest);
    }

    /// <summary>
    /// Declines a time-off request.
    /// Requires scope: timeoff:approve
    /// </summary>
    [HttpPost("{id}/decline")]
    [ProducesResponseType(typeof(TimeOffDto), 200)]
    [ProducesResponseType(typeof(ApiProblemDetails), 400)]
    [ProducesResponseType(typeof(ApiProblemDetails), 401)]
    [ProducesResponseType(typeof(ApiProblemDetails), 403)]
    [ProducesResponseType(typeof(ApiProblemDetails), 404)]
    [ProducesResponseType(typeof(ApiProblemDetails), 429)]
    public async Task<IActionResult> DeclineTimeOffRequest(int id)
    {
        if (!_configuration.GetValue<bool>("Features:Api:TimeOff:DeclineEnabled", false))
        {
            return NotFound(ApiProblemDetails.NotFound("This API endpoint is not enabled", HttpContext.Request.Path));
        }

        var companyIdClaim = User.FindFirst("CompanyId")?.Value;
        if (companyIdClaim == null || !int.TryParse(companyIdClaim, out var companyId))
        {
            return Unauthorized(ApiProblemDetails.Unauthorized("Invalid authentication", HttpContext.Request.Path));
        }

        var (timeOffRequest, error) = await _timeOffService.DeclineTimeOffRequestAsync(companyId, id);

        if (error != null)
        {
            if (error.Contains("not found"))
            {
                return NotFound(ApiProblemDetails.NotFound(error, HttpContext.Request.Path));
            }
            return BadRequest(ApiProblemDetails.ValidationError(error, HttpContext.Request.Path));
        }

        return Ok(timeOffRequest);
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
