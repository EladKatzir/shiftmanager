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
/// API controller for on-duty assignment management.
/// All endpoints require API key authentication via X-API-Key header.
/// NOTE: On-duty assignments are global (not company-scoped).
/// </summary>
[Authorize]
[ApiController]
[Route("api/v1/on-duty")]
[Produces("application/json")]
public partial class OnDutyController : ControllerBase
{
    private readonly OnDutyApiService _onDutyService;
    private readonly IFeatureFlagService _featureFlagService;
    private readonly ILogger<OnDutyController> _logger;

    public OnDutyController(
        OnDutyApiService onDutyService,
        IFeatureFlagService featureFlagService,
        ILogger<OnDutyController> logger)
    {
        _onDutyService = onDutyService;
        _featureFlagService = featureFlagService;
        _logger = logger;
    }

    /// <summary>
    /// Lists on-duty assignments with pagination and filtering.
    /// Requires scope: onduty:read
    /// NOTE: Results are global (not filtered by company).
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(PaginatedResponse<OnDutyDto>), 200)]
    [ProducesResponseType(typeof(ApiErrorResponse), 401)]
    [ProducesResponseType(typeof(ApiErrorResponse), 403)]
    [ProducesResponseType(typeof(ApiErrorResponse), 429)]
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
            if (!await _featureFlagService.IsEnabledAsync(FeatureFlagSeed.Flags.ApiOnDutyList))
            {
                LogApiEndpointNotEnabled(_logger, nameof(ListOnDuties), HttpContext.Request.Path);
                return NotFound(ApiErrorResponse.Create("ENDPOINT_DISABLED", "This API endpoint is not enabled"));
            }

            // Get CompanyId from claims (for authentication, even though on-duty is global)
            var companyIdClaim = User.FindFirst("CompanyId")?.Value;
            if (companyIdClaim == null || !int.TryParse(companyIdClaim, out var _))
            {
                LogApiUnauthorized(_logger, nameof(ListOnDuties), HttpContext.Request.Path, companyIdClaim != null);
                return Unauthorized(ApiErrorResponse.Unauthorized("Invalid authentication"));
            }

            // Parse date filters
            DateOnly? startDateParsed = null;
            DateOnly? endDateParsed = null;

            if (!string.IsNullOrEmpty(startDate))
            {
                if (!DateOnly.TryParse(startDate, out var parsed))
                {
                    LogInvalidStartDate(_logger, nameof(ListOnDuties), startDate);
                    return BadRequest(ApiErrorResponse.BadRequest("Invalid startDate format. Use yyyy-MM-dd"));
                }
                startDateParsed = parsed;
            }

            if (!string.IsNullOrEmpty(endDate))
            {
                if (!DateOnly.TryParse(endDate, out var parsed))
                {
                    LogInvalidEndDate(_logger, nameof(ListOnDuties), endDate);
                    return BadRequest(ApiErrorResponse.BadRequest("Invalid endDate format. Use yyyy-MM-dd"));
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
            LogDbErrorPath(_logger, ex, nameof(ListOnDuties), HttpContext.Request.Path);
            return StatusCode(500, ApiErrorResponse.ServerError("An error occurred while processing your request"));
        }
        catch (Exception ex)
        {
            LogUnexpectedErrorPath(_logger, ex, nameof(ListOnDuties), HttpContext.Request.Path);
            return StatusCode(500, ApiErrorResponse.ServerError("An unexpected error occurred"));
        }
    }

    /// <summary>
    /// Gets a single on-duty assignment by ID.
    /// Requires scope: onduty:read
    /// </summary>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(OnDutyDto), 200)]
    [ProducesResponseType(typeof(ApiErrorResponse), 401)]
    [ProducesResponseType(typeof(ApiErrorResponse), 403)]
    [ProducesResponseType(typeof(ApiErrorResponse), 404)]
    [ProducesResponseType(typeof(ApiErrorResponse), 429)]
    public async Task<IActionResult> GetOnDuty(int id, [FromQuery] bool includeRelated = true)
    {
        try
        {
            // Check feature flag
            if (!await _featureFlagService.IsEnabledAsync(FeatureFlagSeed.Flags.ApiOnDutyGet))
            {
                LogApiEndpointNotEnabled(_logger, nameof(GetOnDuty), HttpContext.Request.Path);
                return NotFound(ApiErrorResponse.Create("ENDPOINT_DISABLED", "This API endpoint is not enabled"));
            }

            // Get CompanyId from claims (for authentication)
            var companyIdClaim = User.FindFirst("CompanyId")?.Value;
            if (companyIdClaim == null || !int.TryParse(companyIdClaim, out var _))
            {
                LogApiUnauthorized(_logger, nameof(GetOnDuty), HttpContext.Request.Path, companyIdClaim != null);
                return Unauthorized(ApiErrorResponse.Unauthorized("Invalid authentication"));
            }

            var onDuty = await _onDutyService.GetOnDutyAsync(id, includeRelated);

            if (onDuty == null)
            {
                LogOnDutyNotFound(_logger, nameof(GetOnDuty), id);
                return NotFound(ApiErrorResponse.NotFound("OnDuty", id.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            }

            return Ok(onDuty);
        }
        catch (DbUpdateException ex)
        {
            LogDbErrorWithOnDutyId(_logger, ex, nameof(GetOnDuty), id, HttpContext.Request.Path);
            return StatusCode(500, ApiErrorResponse.ServerError("An error occurred while processing your request"));
        }
        catch (Exception ex)
        {
            LogUnexpectedErrorWithOnDutyId(_logger, ex, nameof(GetOnDuty), id, HttpContext.Request.Path);
            return StatusCode(500, ApiErrorResponse.ServerError("An unexpected error occurred"));
        }
    }

    /// <summary>
    /// Creates a new on-duty assignment.
    /// Requires scope: onduty:write
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(OnDutyDto), 201)]
    [ProducesResponseType(typeof(ApiErrorResponse), 400)]
    [ProducesResponseType(typeof(ApiErrorResponse), 401)]
    [ProducesResponseType(typeof(ApiErrorResponse), 403)]
    [ProducesResponseType(typeof(ApiErrorResponse), 429)]
    public async Task<IActionResult> CreateOnDuty([FromBody] CreateOnDutyDto request)
    {
        try
        {
            // Check feature flag
            if (!await _featureFlagService.IsEnabledAsync(FeatureFlagSeed.Flags.ApiOnDutyCreate))
            {
                LogApiEndpointNotEnabled(_logger, nameof(CreateOnDuty), HttpContext.Request.Path);
                return NotFound(ApiErrorResponse.Create("ENDPOINT_DISABLED", "This API endpoint is not enabled"));
            }

            // Get CompanyId from claims (for authentication)
            var companyIdClaim = User.FindFirst("CompanyId")?.Value;
            if (companyIdClaim == null || !int.TryParse(companyIdClaim, out var _))
            {
                LogApiUnauthorized(_logger, nameof(CreateOnDuty), HttpContext.Request.Path, companyIdClaim != null);
                return Unauthorized(ApiErrorResponse.Unauthorized("Invalid authentication"));
            }

            // Get UserId from claims (the creator)
            var userIdClaim = User.FindFirst("UserId")?.Value;
            if (userIdClaim == null || !int.TryParse(userIdClaim, out var creatorId))
            {
                LogUserIdClaimMissing(_logger, nameof(CreateOnDuty));
                return Unauthorized(ApiErrorResponse.Unauthorized("Invalid authentication"));
            }

            // Validate request
            if (request.UserId <= 0)
            {
                LogValidationUserIdRequired(_logger, nameof(CreateOnDuty));
                return BadRequest(ApiErrorResponse.BadRequest("UserId is required"));
            }

            if (string.IsNullOrWhiteSpace(request.Date))
            {
                LogValidationDateRequired(_logger, nameof(CreateOnDuty));
                return BadRequest(ApiErrorResponse.BadRequest("Date is required"));
            }

            if (string.IsNullOrWhiteSpace(request.Type))
            {
                LogValidationTypeRequired(_logger, nameof(CreateOnDuty));
                return BadRequest(ApiErrorResponse.BadRequest("Type is required"));
            }

            var (onDuty, error) = await _onDutyService.CreateOnDutyAsync(creatorId, request);

            if (error != null)
            {
                LogValidationCreateError(_logger, nameof(CreateOnDuty), error);
                return BadRequest(ApiErrorResponse.BadRequest(error));
            }

            LogOnDutyCreated(_logger, nameof(CreateOnDuty), onDuty!.Id);

            return CreatedAtAction(nameof(GetOnDuty), new { id = onDuty!.Id }, onDuty);
        }
        catch (DbUpdateException ex)
        {
            LogDbErrorPath(_logger, ex, nameof(CreateOnDuty), HttpContext.Request.Path);
            return StatusCode(500, ApiErrorResponse.ServerError("An error occurred while processing your request"));
        }
        catch (Exception ex)
        {
            LogUnexpectedErrorPath(_logger, ex, nameof(CreateOnDuty), HttpContext.Request.Path);
            return StatusCode(500, ApiErrorResponse.ServerError("An unexpected error occurred"));
        }
    }

    /// <summary>
    /// Updates an existing on-duty assignment.
    /// Requires scope: onduty:write
    /// </summary>
    [HttpPatch("{id}")]
    [ProducesResponseType(typeof(OnDutyDto), 200)]
    [ProducesResponseType(typeof(ApiErrorResponse), 400)]
    [ProducesResponseType(typeof(ApiErrorResponse), 401)]
    [ProducesResponseType(typeof(ApiErrorResponse), 403)]
    [ProducesResponseType(typeof(ApiErrorResponse), 404)]
    [ProducesResponseType(typeof(ApiErrorResponse), 429)]
    public async Task<IActionResult> UpdateOnDuty(int id, [FromBody] UpdateOnDutyDto request)
    {
        try
        {
            // Check feature flag
            if (!await _featureFlagService.IsEnabledAsync(FeatureFlagSeed.Flags.ApiOnDutyUpdate))
            {
                LogApiEndpointNotEnabled(_logger, nameof(UpdateOnDuty), HttpContext.Request.Path);
                return NotFound(ApiErrorResponse.Create("ENDPOINT_DISABLED", "This API endpoint is not enabled"));
            }

            // Get CompanyId from claims (for authentication)
            var companyIdClaim = User.FindFirst("CompanyId")?.Value;
            if (companyIdClaim == null || !int.TryParse(companyIdClaim, out var _))
            {
                LogApiUnauthorized(_logger, nameof(UpdateOnDuty), HttpContext.Request.Path, companyIdClaim != null);
                return Unauthorized(ApiErrorResponse.Unauthorized("Invalid authentication"));
            }

            var (onDuty, error) = await _onDutyService.UpdateOnDutyAsync(id, request);

            if (error != null)
            {
                if (error.Contains("not found"))
                {
                    LogOnDutyNotFoundForUpdate(_logger, nameof(UpdateOnDuty), id);
                    return NotFound(ApiErrorResponse.Create("NOT_FOUND", error));
                }
                LogValidationUpdateError(_logger, nameof(UpdateOnDuty), id, error);
                return BadRequest(ApiErrorResponse.BadRequest(error));
            }

            LogOnDutyUpdated(_logger, nameof(UpdateOnDuty), id);

            return Ok(onDuty);
        }
        catch (DbUpdateException ex)
        {
            LogDbErrorWithOnDutyId(_logger, ex, nameof(UpdateOnDuty), id, HttpContext.Request.Path);
            return StatusCode(500, ApiErrorResponse.ServerError("An error occurred while processing your request"));
        }
        catch (Exception ex)
        {
            LogUnexpectedErrorWithOnDutyId(_logger, ex, nameof(UpdateOnDuty), id, HttpContext.Request.Path);
            return StatusCode(500, ApiErrorResponse.ServerError("An unexpected error occurred"));
        }
    }

    /// <summary>
    /// Deletes (cancels) an on-duty assignment.
    /// Requires scope: onduty:write
    /// </summary>
    [HttpDelete("{id}")]
    [ProducesResponseType(204)]
    [ProducesResponseType(typeof(ApiErrorResponse), 400)]
    [ProducesResponseType(typeof(ApiErrorResponse), 401)]
    [ProducesResponseType(typeof(ApiErrorResponse), 403)]
    [ProducesResponseType(typeof(ApiErrorResponse), 404)]
    [ProducesResponseType(typeof(ApiErrorResponse), 429)]
    public async Task<IActionResult> DeleteOnDuty(int id)
    {
        try
        {
            // Check feature flag
            if (!await _featureFlagService.IsEnabledAsync(FeatureFlagSeed.Flags.ApiOnDutyDelete))
            {
                LogApiEndpointNotEnabled(_logger, nameof(DeleteOnDuty), HttpContext.Request.Path);
                return NotFound(ApiErrorResponse.Create("ENDPOINT_DISABLED", "This API endpoint is not enabled"));
            }

            // Get CompanyId from claims (for authentication)
            var companyIdClaim = User.FindFirst("CompanyId")?.Value;
            if (companyIdClaim == null || !int.TryParse(companyIdClaim, out var _))
            {
                LogApiUnauthorized(_logger, nameof(DeleteOnDuty), HttpContext.Request.Path, companyIdClaim != null);
                return Unauthorized(ApiErrorResponse.Unauthorized("Invalid authentication"));
            }

            // Get UserId from claims
            var userIdClaim = User.FindFirst("UserId")?.Value;
            if (userIdClaim == null || !int.TryParse(userIdClaim, out var canceledBy))
            {
                LogUserIdClaimMissing(_logger, nameof(DeleteOnDuty));
                return Unauthorized(ApiErrorResponse.Unauthorized("Invalid authentication"));
            }

            var success = await _onDutyService.DeleteOnDutyAsync(id, canceledBy);

            if (!success)
            {
                LogOnDutyDeletionFailed(_logger, nameof(DeleteOnDuty), id);
                return NotFound(ApiErrorResponse.Create("ONDUTY_NOT_FOUND", "On-duty assignment not found or already canceled"));
            }

            LogOnDutyDeleted(_logger, nameof(DeleteOnDuty), id, canceledBy);

            return NoContent();
        }
        catch (DbUpdateException ex)
        {
            LogDbErrorWithOnDutyId(_logger, ex, nameof(DeleteOnDuty), id, HttpContext.Request.Path);
            return StatusCode(500, ApiErrorResponse.ServerError("An error occurred while processing your request"));
        }
        catch (Exception ex)
        {
            LogUnexpectedErrorWithOnDutyId(_logger, ex, nameof(DeleteOnDuty), id, HttpContext.Request.Path);
            return StatusCode(500, ApiErrorResponse.ServerError("An unexpected error occurred"));
        }
    }
}
