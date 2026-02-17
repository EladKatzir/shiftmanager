using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Models.Api;
using ShiftManager.Models.Api.Dto;
using ShiftManager.Services.Api;

namespace ShiftManager.Controllers.Api.V1;

/// <summary>
/// API controller for on-duty assignment management.
/// All endpoints require API key authentication via X-API-Key header.
/// NOTE: On-duty assignments are global (not company-scoped).
/// </summary>
[Authorize]
[ApiController]
[Route("api/v1/on-duty")]
[Produces("application/json")]
public class OnDutyController : ControllerBase
{
    private readonly OnDutyApiService _onDutyService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<OnDutyController> _logger;

    public OnDutyController(
        OnDutyApiService onDutyService,
        IConfiguration configuration,
        ILogger<OnDutyController> logger)
    {
        _onDutyService = onDutyService;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Lists on-duty assignments with pagination and filtering.
    /// Requires scope: onduty:read
    /// NOTE: Results are global (not filtered by company).
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(PaginatedResponse<OnDutyDto>), 200)]
    [ProducesResponseType(typeof(ApiProblemDetails), 401)]
    [ProducesResponseType(typeof(ApiProblemDetails), 403)]
    [ProducesResponseType(typeof(ApiProblemDetails), 429)]
    public async Task<IActionResult> ListOnDuties(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] int? userId = null,
        [FromQuery] string? startDate = null,
        [FromQuery] string? endDate = null,
        [FromQuery] string? type = null,
        [FromQuery] bool includeRelated = false,
        [FromQuery] bool includeCanceled = false)
    {
        try
        {
            // Check feature flag
            if (!_configuration.GetValue<bool>("Features:Api:OnDuty:ListEnabled", false))
            {
                _logger.LogWarning("API endpoint not enabled. Endpoint={Endpoint}, Path={Path}",
                    nameof(ListOnDuties), HttpContext.Request.Path);
                return NotFound(ApiProblemDetails.NotFound("This API endpoint is not enabled", HttpContext.Request.Path));
            }

            // Get CompanyId from claims (for authentication, even though on-duty is global)
            var companyIdClaim = User.FindFirst("CompanyId")?.Value;
            if (companyIdClaim == null || !int.TryParse(companyIdClaim, out var _))
            {
                _logger.LogWarning("Unauthorized API access attempt. Endpoint={Endpoint}, Path={Path}, HasCompanyClaim={HasClaim}",
                    nameof(ListOnDuties), HttpContext.Request.Path, companyIdClaim != null);
                return Unauthorized(ApiProblemDetails.Unauthorized("Invalid authentication", HttpContext.Request.Path));
            }

            // Parse date filters
            DateOnly? startDateParsed = null;
            DateOnly? endDateParsed = null;

            if (!string.IsNullOrEmpty(startDate))
            {
                if (!DateOnly.TryParse(startDate, out var parsed))
                {
                    _logger.LogWarning("Invalid startDate format. Endpoint={Endpoint}, StartDate={StartDate}",
                        nameof(ListOnDuties), startDate);
                    return BadRequest(ApiProblemDetails.ValidationError(
                        "Invalid startDate format. Use yyyy-MM-dd", HttpContext.Request.Path));
                }
                startDateParsed = parsed;
            }

            if (!string.IsNullOrEmpty(endDate))
            {
                if (!DateOnly.TryParse(endDate, out var parsed))
                {
                    _logger.LogWarning("Invalid endDate format. Endpoint={Endpoint}, EndDate={EndDate}",
                        nameof(ListOnDuties), endDate);
                    return BadRequest(ApiProblemDetails.ValidationError(
                        "Invalid endDate format. Use yyyy-MM-dd", HttpContext.Request.Path));
                }
                endDateParsed = parsed;
            }

            var (onDuties, totalCount) = await _onDutyService.ListOnDutiesAsync(
                page, pageSize, userId, startDateParsed, endDateParsed, type, includeRelated, includeCanceled);

            var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
            var response = new PaginatedResponse<OnDutyDto>
            {
                Data = onDuties,
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
            _logger.LogError(ex, "Database error in {Endpoint}. Path={Path}",
                nameof(ListOnDuties), HttpContext.Request.Path);
            return StatusCode(500, ApiProblemDetails.InternalError(
                "An error occurred while processing your request", HttpContext.Request.Path));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in {Endpoint}. Path={Path}",
                nameof(ListOnDuties), HttpContext.Request.Path);
            return StatusCode(500, ApiProblemDetails.InternalError(
                "An unexpected error occurred", HttpContext.Request.Path));
        }
    }

    /// <summary>
    /// Gets a single on-duty assignment by ID.
    /// Requires scope: onduty:read
    /// </summary>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(OnDutyDto), 200)]
    [ProducesResponseType(typeof(ApiProblemDetails), 401)]
    [ProducesResponseType(typeof(ApiProblemDetails), 403)]
    [ProducesResponseType(typeof(ApiProblemDetails), 404)]
    [ProducesResponseType(typeof(ApiProblemDetails), 429)]
    public async Task<IActionResult> GetOnDuty(int id, [FromQuery] bool includeRelated = true)
    {
        try
        {
            // Check feature flag
            if (!_configuration.GetValue<bool>("Features:Api:OnDuty:GetEnabled", false))
            {
                _logger.LogWarning("API endpoint not enabled. Endpoint={Endpoint}, Path={Path}",
                    nameof(GetOnDuty), HttpContext.Request.Path);
                return NotFound(ApiProblemDetails.NotFound("This API endpoint is not enabled", HttpContext.Request.Path));
            }

            // Get CompanyId from claims (for authentication)
            var companyIdClaim = User.FindFirst("CompanyId")?.Value;
            if (companyIdClaim == null || !int.TryParse(companyIdClaim, out var _))
            {
                _logger.LogWarning("Unauthorized API access attempt. Endpoint={Endpoint}, Path={Path}, HasCompanyClaim={HasClaim}",
                    nameof(GetOnDuty), HttpContext.Request.Path, companyIdClaim != null);
                return Unauthorized(ApiProblemDetails.Unauthorized("Invalid authentication", HttpContext.Request.Path));
            }

            var onDuty = await _onDutyService.GetOnDutyAsync(id, includeRelated);

            if (onDuty == null)
            {
                _logger.LogWarning("On-duty assignment not found. Endpoint={Endpoint}, OnDutyId={OnDutyId}",
                    nameof(GetOnDuty), id);
                return NotFound(ApiProblemDetails.NotFound($"On-duty assignment {id} not found", HttpContext.Request.Path));
            }

            return Ok(onDuty);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Database error in {Endpoint}. OnDutyId={OnDutyId}, Path={Path}",
                nameof(GetOnDuty), id, HttpContext.Request.Path);
            return StatusCode(500, ApiProblemDetails.InternalError(
                "An error occurred while processing your request", HttpContext.Request.Path));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in {Endpoint}. OnDutyId={OnDutyId}, Path={Path}",
                nameof(GetOnDuty), id, HttpContext.Request.Path);
            return StatusCode(500, ApiProblemDetails.InternalError(
                "An unexpected error occurred", HttpContext.Request.Path));
        }
    }

    /// <summary>
    /// Creates a new on-duty assignment.
    /// Requires scope: onduty:write
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(OnDutyDto), 201)]
    [ProducesResponseType(typeof(ApiProblemDetails), 400)]
    [ProducesResponseType(typeof(ApiProblemDetails), 401)]
    [ProducesResponseType(typeof(ApiProblemDetails), 403)]
    [ProducesResponseType(typeof(ApiProblemDetails), 429)]
    public async Task<IActionResult> CreateOnDuty([FromBody] CreateOnDutyDto request)
    {
        try
        {
            // Check feature flag
            if (!_configuration.GetValue<bool>("Features:Api:OnDuty:CreateEnabled", false))
            {
                _logger.LogWarning("API endpoint not enabled. Endpoint={Endpoint}, Path={Path}",
                    nameof(CreateOnDuty), HttpContext.Request.Path);
                return NotFound(ApiProblemDetails.NotFound("This API endpoint is not enabled", HttpContext.Request.Path));
            }

            // Get CompanyId from claims (for authentication)
            var companyIdClaim = User.FindFirst("CompanyId")?.Value;
            if (companyIdClaim == null || !int.TryParse(companyIdClaim, out var _))
            {
                _logger.LogWarning("Unauthorized API access attempt. Endpoint={Endpoint}, Path={Path}, HasCompanyClaim={HasClaim}",
                    nameof(CreateOnDuty), HttpContext.Request.Path, companyIdClaim != null);
                return Unauthorized(ApiProblemDetails.Unauthorized("Invalid authentication", HttpContext.Request.Path));
            }

            // Get UserId from claims (the creator)
            var userIdClaim = User.FindFirst("UserId")?.Value;
            if (userIdClaim == null || !int.TryParse(userIdClaim, out var creatorId))
            {
                _logger.LogWarning("UserId claim missing. Endpoint={Endpoint}",
                    nameof(CreateOnDuty));
                return Unauthorized(ApiProblemDetails.Unauthorized("Invalid authentication", HttpContext.Request.Path));
            }

            // Validate request
            if (request.UserId <= 0)
            {
                _logger.LogWarning("Validation error: UserId required. Endpoint={Endpoint}",
                    nameof(CreateOnDuty));
                return BadRequest(ApiProblemDetails.ValidationError("UserId is required", HttpContext.Request.Path));
            }

            if (string.IsNullOrWhiteSpace(request.Date))
            {
                _logger.LogWarning("Validation error: Date required. Endpoint={Endpoint}",
                    nameof(CreateOnDuty));
                return BadRequest(ApiProblemDetails.ValidationError("Date is required", HttpContext.Request.Path));
            }

            if (string.IsNullOrWhiteSpace(request.Type))
            {
                _logger.LogWarning("Validation error: Type required. Endpoint={Endpoint}",
                    nameof(CreateOnDuty));
                return BadRequest(ApiProblemDetails.ValidationError("Type is required", HttpContext.Request.Path));
            }

            var (onDuty, error) = await _onDutyService.CreateOnDutyAsync(creatorId, request);

            if (error != null)
            {
                _logger.LogWarning("On-duty creation validation error. Endpoint={Endpoint}, Error={Error}",
                    nameof(CreateOnDuty), error);
                return BadRequest(ApiProblemDetails.ValidationError(error, HttpContext.Request.Path));
            }

            _logger.LogInformation("On-duty assignment created. Endpoint={Endpoint}, OnDutyId={OnDutyId}",
                nameof(CreateOnDuty), onDuty!.Id);

            return CreatedAtAction(nameof(GetOnDuty), new { id = onDuty!.Id }, onDuty);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Database error in {Endpoint}. Path={Path}",
                nameof(CreateOnDuty), HttpContext.Request.Path);
            return StatusCode(500, ApiProblemDetails.InternalError(
                "An error occurred while processing your request", HttpContext.Request.Path));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in {Endpoint}. Path={Path}",
                nameof(CreateOnDuty), HttpContext.Request.Path);
            return StatusCode(500, ApiProblemDetails.InternalError(
                "An unexpected error occurred", HttpContext.Request.Path));
        }
    }

    /// <summary>
    /// Updates an existing on-duty assignment.
    /// Requires scope: onduty:write
    /// </summary>
    [HttpPatch("{id}")]
    [ProducesResponseType(typeof(OnDutyDto), 200)]
    [ProducesResponseType(typeof(ApiProblemDetails), 400)]
    [ProducesResponseType(typeof(ApiProblemDetails), 401)]
    [ProducesResponseType(typeof(ApiProblemDetails), 403)]
    [ProducesResponseType(typeof(ApiProblemDetails), 404)]
    [ProducesResponseType(typeof(ApiProblemDetails), 429)]
    public async Task<IActionResult> UpdateOnDuty(int id, [FromBody] UpdateOnDutyDto request)
    {
        try
        {
            // Check feature flag
            if (!_configuration.GetValue<bool>("Features:Api:OnDuty:UpdateEnabled", false))
            {
                _logger.LogWarning("API endpoint not enabled. Endpoint={Endpoint}, Path={Path}",
                    nameof(UpdateOnDuty), HttpContext.Request.Path);
                return NotFound(ApiProblemDetails.NotFound("This API endpoint is not enabled", HttpContext.Request.Path));
            }

            // Get CompanyId from claims (for authentication)
            var companyIdClaim = User.FindFirst("CompanyId")?.Value;
            if (companyIdClaim == null || !int.TryParse(companyIdClaim, out var _))
            {
                _logger.LogWarning("Unauthorized API access attempt. Endpoint={Endpoint}, Path={Path}, HasCompanyClaim={HasClaim}",
                    nameof(UpdateOnDuty), HttpContext.Request.Path, companyIdClaim != null);
                return Unauthorized(ApiProblemDetails.Unauthorized("Invalid authentication", HttpContext.Request.Path));
            }

            var (onDuty, error) = await _onDutyService.UpdateOnDutyAsync(id, request);

            if (error != null)
            {
                if (error.Contains("not found"))
                {
                    _logger.LogWarning("On-duty assignment not found for update. Endpoint={Endpoint}, OnDutyId={OnDutyId}",
                        nameof(UpdateOnDuty), id);
                    return NotFound(ApiProblemDetails.NotFound(error, HttpContext.Request.Path));
                }
                _logger.LogWarning("On-duty update validation error. Endpoint={Endpoint}, OnDutyId={OnDutyId}, Error={Error}",
                    nameof(UpdateOnDuty), id, error);
                return BadRequest(ApiProblemDetails.ValidationError(error, HttpContext.Request.Path));
            }

            _logger.LogInformation("On-duty assignment updated. Endpoint={Endpoint}, OnDutyId={OnDutyId}",
                nameof(UpdateOnDuty), id);

            return Ok(onDuty);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Database error in {Endpoint}. OnDutyId={OnDutyId}, Path={Path}",
                nameof(UpdateOnDuty), id, HttpContext.Request.Path);
            return StatusCode(500, ApiProblemDetails.InternalError(
                "An error occurred while processing your request", HttpContext.Request.Path));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in {Endpoint}. OnDutyId={OnDutyId}, Path={Path}",
                nameof(UpdateOnDuty), id, HttpContext.Request.Path);
            return StatusCode(500, ApiProblemDetails.InternalError(
                "An unexpected error occurred", HttpContext.Request.Path));
        }
    }

    /// <summary>
    /// Deletes (cancels) an on-duty assignment.
    /// Requires scope: onduty:write
    /// </summary>
    [HttpDelete("{id}")]
    [ProducesResponseType(204)]
    [ProducesResponseType(typeof(ApiProblemDetails), 400)]
    [ProducesResponseType(typeof(ApiProblemDetails), 401)]
    [ProducesResponseType(typeof(ApiProblemDetails), 403)]
    [ProducesResponseType(typeof(ApiProblemDetails), 404)]
    [ProducesResponseType(typeof(ApiProblemDetails), 429)]
    public async Task<IActionResult> DeleteOnDuty(int id)
    {
        try
        {
            // Check feature flag
            if (!_configuration.GetValue<bool>("Features:Api:OnDuty:DeleteEnabled", false))
            {
                _logger.LogWarning("API endpoint not enabled. Endpoint={Endpoint}, Path={Path}",
                    nameof(DeleteOnDuty), HttpContext.Request.Path);
                return NotFound(ApiProblemDetails.NotFound("This API endpoint is not enabled", HttpContext.Request.Path));
            }

            // Get CompanyId from claims (for authentication)
            var companyIdClaim = User.FindFirst("CompanyId")?.Value;
            if (companyIdClaim == null || !int.TryParse(companyIdClaim, out var _))
            {
                _logger.LogWarning("Unauthorized API access attempt. Endpoint={Endpoint}, Path={Path}, HasCompanyClaim={HasClaim}",
                    nameof(DeleteOnDuty), HttpContext.Request.Path, companyIdClaim != null);
                return Unauthorized(ApiProblemDetails.Unauthorized("Invalid authentication", HttpContext.Request.Path));
            }

            // Get UserId from claims
            var userIdClaim = User.FindFirst("UserId")?.Value;
            if (userIdClaim == null || !int.TryParse(userIdClaim, out var canceledBy))
            {
                _logger.LogWarning("UserId claim missing. Endpoint={Endpoint}",
                    nameof(DeleteOnDuty));
                return Unauthorized(ApiProblemDetails.Unauthorized("Invalid authentication", HttpContext.Request.Path));
            }

            var success = await _onDutyService.DeleteOnDutyAsync(id, canceledBy);

            if (!success)
            {
                _logger.LogWarning("On-duty deletion failed. Endpoint={Endpoint}, OnDutyId={OnDutyId}",
                    nameof(DeleteOnDuty), id);
                return NotFound(ApiProblemDetails.NotFound(
                    "On-duty assignment not found or already canceled", HttpContext.Request.Path));
            }

            _logger.LogInformation("On-duty assignment deleted. Endpoint={Endpoint}, OnDutyId={OnDutyId}, CanceledBy={CanceledBy}",
                nameof(DeleteOnDuty), id, canceledBy);

            return NoContent();
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Database error in {Endpoint}. OnDutyId={OnDutyId}, Path={Path}",
                nameof(DeleteOnDuty), id, HttpContext.Request.Path);
            return StatusCode(500, ApiProblemDetails.InternalError(
                "An error occurred while processing your request", HttpContext.Request.Path));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in {Endpoint}. OnDutyId={OnDutyId}, Path={Path}",
                nameof(DeleteOnDuty), id, HttpContext.Request.Path);
            return StatusCode(500, ApiProblemDetails.InternalError(
                "An unexpected error occurred", HttpContext.Request.Path));
        }
    }
}
