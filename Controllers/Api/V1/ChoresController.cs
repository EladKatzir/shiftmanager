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
/// API controller for chore management.
/// All endpoints require API key authentication via X-API-Key header.
/// </summary>
[Authorize]
[ApiController]
[Route("api/v1/chores")]
[Produces("application/json")]
public partial class ChoresController : ControllerBase
{
    private readonly ChoreApiService _choreService;
    private readonly IFeatureFlagService _featureFlagService;
    private readonly ILogger<ChoresController> _logger;

    public ChoresController(
        ChoreApiService choreService,
        IFeatureFlagService featureFlagService,
        ILogger<ChoresController> logger)
    {
        _choreService = choreService;
        _featureFlagService = featureFlagService;
        _logger = logger;
    }

    /// <summary>
    /// Lists chores with pagination and filtering.
    /// Requires scope: chores:read
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(PaginatedResponse<ChoreDto>), 200)]
    [ProducesResponseType(typeof(ApiErrorResponse), 401)]
    [ProducesResponseType(typeof(ApiErrorResponse), 403)]
    [ProducesResponseType(typeof(ApiErrorResponse), 429)]
    public async Task<IActionResult> ListChores(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] int? userId = null,
        [FromQuery] string? startDate = null,
        [FromQuery] string? endDate = null,
        [FromQuery] bool includeRelated = false,
        [FromQuery] bool includeCanceled = false)
    {
        try
        {
            // Check feature flag
            if (!await _featureFlagService.IsEnabledAsync(FeatureFlagSeed.Flags.ApiChoresList))
            {
                LogApiEndpointNotEnabled(_logger, nameof(ListChores), HttpContext.Request.Path);
                return NotFound(ApiErrorResponse.Create("ENDPOINT_DISABLED", "This API endpoint is not enabled"));
            }

            // Get CompanyId from claims
            var companyIdClaim = User.FindFirst("CompanyId")?.Value;
            if (companyIdClaim == null || !int.TryParse(companyIdClaim, out var companyId))
            {
                LogApiUnauthorized(_logger, nameof(ListChores), HttpContext.Request.Path, companyIdClaim != null);
                return Unauthorized(ApiErrorResponse.Unauthorized("Invalid authentication"));
            }

            // Parse date filters
            DateOnly? startDateParsed = null;
            DateOnly? endDateParsed = null;

            if (!string.IsNullOrEmpty(startDate))
            {
                if (!DateOnly.TryParse(startDate, out var parsed))
                {
                    LogInvalidStartDate(_logger, nameof(ListChores), companyId, startDate);
                    return BadRequest(ApiErrorResponse.BadRequest("Invalid startDate format. Use yyyy-MM-dd"));
                }
                startDateParsed = parsed;
            }

            if (!string.IsNullOrEmpty(endDate))
            {
                if (!DateOnly.TryParse(endDate, out var parsed))
                {
                    LogInvalidEndDate(_logger, nameof(ListChores), companyId, endDate);
                    return BadRequest(ApiErrorResponse.BadRequest("Invalid endDate format. Use yyyy-MM-dd"));
                }
                endDateParsed = parsed;
            }

            var (chores, totalCount) = await _choreService.ListChoresAsync(
                companyId, page, pageSize, userId, startDateParsed, endDateParsed, includeRelated, includeCanceled);

            var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
            var response = new PaginatedResponse<ChoreDto>
            {
                Data = chores,
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
            LogDbErrorCompanyPath(_logger, ex, nameof(ListChores), User.FindFirst("CompanyId")?.Value, HttpContext.Request.Path);
            return StatusCode(500, ApiErrorResponse.ServerError("An error occurred while processing your request"));
        }
        catch (Exception ex)
        {
            LogUnexpectedErrorCompanyPath(_logger, ex, nameof(ListChores), User.FindFirst("CompanyId")?.Value, HttpContext.Request.Path);
            return StatusCode(500, ApiErrorResponse.ServerError("An unexpected error occurred"));
        }
    }

    /// <summary>
    /// Gets a single chore by ID.
    /// Requires scope: chores:read
    /// </summary>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(ChoreDto), 200)]
    [ProducesResponseType(typeof(ApiErrorResponse), 401)]
    [ProducesResponseType(typeof(ApiErrorResponse), 403)]
    [ProducesResponseType(typeof(ApiErrorResponse), 404)]
    [ProducesResponseType(typeof(ApiErrorResponse), 429)]
    public async Task<IActionResult> GetChore(int id, [FromQuery] bool includeRelated = true)
    {
        try
        {
            // Check feature flag
            if (!await _featureFlagService.IsEnabledAsync(FeatureFlagSeed.Flags.ApiChoresGet))
            {
                LogApiEndpointNotEnabled(_logger, nameof(GetChore), HttpContext.Request.Path);
                return NotFound(ApiErrorResponse.Create("ENDPOINT_DISABLED", "This API endpoint is not enabled"));
            }

            // Get CompanyId from claims
            var companyIdClaim = User.FindFirst("CompanyId")?.Value;
            if (companyIdClaim == null || !int.TryParse(companyIdClaim, out var companyId))
            {
                LogApiUnauthorized(_logger, nameof(GetChore), HttpContext.Request.Path, companyIdClaim != null);
                return Unauthorized(ApiErrorResponse.Unauthorized("Invalid authentication"));
            }

            var chore = await _choreService.GetChoreAsync(companyId, id, includeRelated);

            if (chore == null)
            {
                LogChoreNotFound(_logger, nameof(GetChore), companyId, id);
                return NotFound(ApiErrorResponse.NotFound("Chore", id.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            }

            return Ok(chore);
        }
        catch (DbUpdateException ex)
        {
            LogDbErrorWithChoreId(_logger, ex, nameof(GetChore), User.FindFirst("CompanyId")?.Value, id, HttpContext.Request.Path);
            return StatusCode(500, ApiErrorResponse.ServerError("An error occurred while processing your request"));
        }
        catch (Exception ex)
        {
            LogUnexpectedErrorWithChoreId(_logger, ex, nameof(GetChore), User.FindFirst("CompanyId")?.Value, id, HttpContext.Request.Path);
            return StatusCode(500, ApiErrorResponse.ServerError("An unexpected error occurred"));
        }
    }

    /// <summary>
    /// Creates a new chore.
    /// Requires scope: chores:write
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(ChoreDto), 201)]
    [ProducesResponseType(typeof(ApiErrorResponse), 400)]
    [ProducesResponseType(typeof(ApiErrorResponse), 401)]
    [ProducesResponseType(typeof(ApiErrorResponse), 403)]
    [ProducesResponseType(typeof(ApiErrorResponse), 429)]
    public async Task<IActionResult> CreateChore([FromBody] CreateChoreDto request)
    {
        try
        {
            // Check feature flag
            if (!await _featureFlagService.IsEnabledAsync(FeatureFlagSeed.Flags.ApiChoresCreate))
            {
                LogApiEndpointNotEnabled(_logger, nameof(CreateChore), HttpContext.Request.Path);
                return NotFound(ApiErrorResponse.Create("ENDPOINT_DISABLED", "This API endpoint is not enabled"));
            }

            // Get CompanyId from claims
            var companyIdClaim = User.FindFirst("CompanyId")?.Value;
            if (companyIdClaim == null || !int.TryParse(companyIdClaim, out var companyId))
            {
                LogApiUnauthorized(_logger, nameof(CreateChore), HttpContext.Request.Path, companyIdClaim != null);
                return Unauthorized(ApiErrorResponse.Unauthorized("Invalid authentication"));
            }

            // Get UserId from claims (the creator)
            var userIdClaim = User.FindFirst("UserId")?.Value;
            if (userIdClaim == null || !int.TryParse(userIdClaim, out var creatorId))
            {
                LogUserIdClaimMissing(_logger, nameof(CreateChore), companyId);
                return Unauthorized(ApiErrorResponse.Unauthorized("Invalid authentication"));
            }

            // Validate request
            if (request.UserId <= 0)
            {
                LogValidationUserIdRequired(_logger, nameof(CreateChore), companyId);
                return BadRequest(ApiErrorResponse.BadRequest("UserId is required"));
            }

            if (string.IsNullOrWhiteSpace(request.Date))
            {
                LogValidationDateRequired(_logger, nameof(CreateChore), companyId);
                return BadRequest(ApiErrorResponse.BadRequest("Date is required"));
            }

            if (string.IsNullOrWhiteSpace(request.Title))
            {
                LogValidationTitleRequired(_logger, nameof(CreateChore), companyId);
                return BadRequest(ApiErrorResponse.BadRequest("Title is required"));
            }

            var (chore, error) = await _choreService.CreateChoreAsync(companyId, creatorId, request);

            if (error != null)
            {
                LogChoreCreationValidationError(_logger, nameof(CreateChore), companyId, error);
                return BadRequest(ApiErrorResponse.BadRequest(error));
            }

            LogChoreCreated(_logger, nameof(CreateChore), companyId, chore!.Id);

            return CreatedAtAction(nameof(GetChore), new { id = chore!.Id }, chore);
        }
        catch (DbUpdateException ex)
        {
            LogDbErrorCompanyPath(_logger, ex, nameof(CreateChore), User.FindFirst("CompanyId")?.Value, HttpContext.Request.Path);
            return StatusCode(500, ApiErrorResponse.ServerError("An error occurred while processing your request"));
        }
        catch (Exception ex)
        {
            LogUnexpectedErrorCompanyPath(_logger, ex, nameof(CreateChore), User.FindFirst("CompanyId")?.Value, HttpContext.Request.Path);
            return StatusCode(500, ApiErrorResponse.ServerError("An unexpected error occurred"));
        }
    }

    /// <summary>
    /// Updates an existing chore.
    /// Requires scope: chores:write
    /// </summary>
    [HttpPatch("{id}")]
    [ProducesResponseType(typeof(ChoreDto), 200)]
    [ProducesResponseType(typeof(ApiErrorResponse), 400)]
    [ProducesResponseType(typeof(ApiErrorResponse), 401)]
    [ProducesResponseType(typeof(ApiErrorResponse), 403)]
    [ProducesResponseType(typeof(ApiErrorResponse), 404)]
    [ProducesResponseType(typeof(ApiErrorResponse), 429)]
    public async Task<IActionResult> UpdateChore(int id, [FromBody] UpdateChoreDto request)
    {
        try
        {
            // Check feature flag
            if (!await _featureFlagService.IsEnabledAsync(FeatureFlagSeed.Flags.ApiChoresUpdate))
            {
                LogApiEndpointNotEnabled(_logger, nameof(UpdateChore), HttpContext.Request.Path);
                return NotFound(ApiErrorResponse.Create("ENDPOINT_DISABLED", "This API endpoint is not enabled"));
            }

            // Get CompanyId from claims
            var companyIdClaim = User.FindFirst("CompanyId")?.Value;
            if (companyIdClaim == null || !int.TryParse(companyIdClaim, out var companyId))
            {
                LogApiUnauthorized(_logger, nameof(UpdateChore), HttpContext.Request.Path, companyIdClaim != null);
                return Unauthorized(ApiErrorResponse.Unauthorized("Invalid authentication"));
            }

            var (chore, error) = await _choreService.UpdateChoreAsync(companyId, id, request);

            if (error != null)
            {
                if (error.Contains("not found"))
                {
                    LogChoreNotFoundForUpdate(_logger, nameof(UpdateChore), companyId, id);
                    return NotFound(ApiErrorResponse.Create("NOT_FOUND", error));
                }
                LogChoreUpdateValidationError(_logger, nameof(UpdateChore), companyId, id, error);
                return BadRequest(ApiErrorResponse.BadRequest(error));
            }

            LogChoreUpdated(_logger, nameof(UpdateChore), companyId, id);

            return Ok(chore);
        }
        catch (DbUpdateException ex)
        {
            LogDbErrorWithChoreId(_logger, ex, nameof(UpdateChore), User.FindFirst("CompanyId")?.Value, id, HttpContext.Request.Path);
            return StatusCode(500, ApiErrorResponse.ServerError("An error occurred while processing your request"));
        }
        catch (Exception ex)
        {
            LogUnexpectedErrorWithChoreId(_logger, ex, nameof(UpdateChore), User.FindFirst("CompanyId")?.Value, id, HttpContext.Request.Path);
            return StatusCode(500, ApiErrorResponse.ServerError("An unexpected error occurred"));
        }
    }

    /// <summary>
    /// Deletes (cancels) a chore.
    /// Requires scope: chores:write
    /// </summary>
    [HttpDelete("{id}")]
    [ProducesResponseType(204)]
    [ProducesResponseType(typeof(ApiErrorResponse), 400)]
    [ProducesResponseType(typeof(ApiErrorResponse), 401)]
    [ProducesResponseType(typeof(ApiErrorResponse), 403)]
    [ProducesResponseType(typeof(ApiErrorResponse), 404)]
    [ProducesResponseType(typeof(ApiErrorResponse), 429)]
    public async Task<IActionResult> DeleteChore(int id)
    {
        try
        {
            // Check feature flag
            if (!await _featureFlagService.IsEnabledAsync(FeatureFlagSeed.Flags.ApiChoresDelete))
            {
                LogApiEndpointNotEnabled(_logger, nameof(DeleteChore), HttpContext.Request.Path);
                return NotFound(ApiErrorResponse.Create("ENDPOINT_DISABLED", "This API endpoint is not enabled"));
            }

            // Get CompanyId from claims
            var companyIdClaim = User.FindFirst("CompanyId")?.Value;
            if (companyIdClaim == null || !int.TryParse(companyIdClaim, out var companyId))
            {
                LogApiUnauthorized(_logger, nameof(DeleteChore), HttpContext.Request.Path, companyIdClaim != null);
                return Unauthorized(ApiErrorResponse.Unauthorized("Invalid authentication"));
            }

            // Get UserId from claims
            var userIdClaim = User.FindFirst("UserId")?.Value;
            if (userIdClaim == null || !int.TryParse(userIdClaim, out var canceledBy))
            {
                LogUserIdClaimMissing(_logger, nameof(DeleteChore), companyId);
                return Unauthorized(ApiErrorResponse.Unauthorized("Invalid authentication"));
            }

            var success = await _choreService.DeleteChoreAsync(companyId, id, canceledBy);

            if (!success)
            {
                LogChoreDeletionFailed(_logger, nameof(DeleteChore), companyId, id);
                return NotFound(ApiErrorResponse.Create("CHORE_NOT_FOUND", "Chore not found or already canceled"));
            }

            LogChoreDeleted(_logger, nameof(DeleteChore), companyId, id, canceledBy);

            return NoContent();
        }
        catch (DbUpdateException ex)
        {
            LogDbErrorWithChoreId(_logger, ex, nameof(DeleteChore), User.FindFirst("CompanyId")?.Value, id, HttpContext.Request.Path);
            return StatusCode(500, ApiErrorResponse.ServerError("An error occurred while processing your request"));
        }
        catch (Exception ex)
        {
            LogUnexpectedErrorWithChoreId(_logger, ex, nameof(DeleteChore), User.FindFirst("CompanyId")?.Value, id, HttpContext.Request.Path);
            return StatusCode(500, ApiErrorResponse.ServerError("An unexpected error occurred"));
        }
    }
}
