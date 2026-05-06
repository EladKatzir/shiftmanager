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
public class ChoresController : ControllerBase
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
                _logger.LogWarning("API endpoint not enabled. Endpoint={Endpoint}, Path={Path}",
                    nameof(ListChores), HttpContext.Request.Path);
                return NotFound(ApiErrorResponse.Create("ENDPOINT_DISABLED", "This API endpoint is not enabled"));
            }

            // Get CompanyId from claims
            var companyIdClaim = User.FindFirst("CompanyId")?.Value;
            if (companyIdClaim == null || !int.TryParse(companyIdClaim, out var companyId))
            {
                _logger.LogWarning("Unauthorized API access attempt. Endpoint={Endpoint}, Path={Path}, HasCompanyClaim={HasClaim}",
                    nameof(ListChores), HttpContext.Request.Path, companyIdClaim != null);
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
                        nameof(ListChores), companyId, startDate);
                    return BadRequest(ApiErrorResponse.BadRequest("Invalid startDate format. Use yyyy-MM-dd"));
                }
                startDateParsed = parsed;
            }

            if (!string.IsNullOrEmpty(endDate))
            {
                if (!DateOnly.TryParse(endDate, out var parsed))
                {
                    _logger.LogWarning("Invalid endDate format. Endpoint={Endpoint}, CompanyId={CompanyId}, EndDate={EndDate}",
                        nameof(ListChores), companyId, endDate);
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
            _logger.LogError(ex, "Database error in {Endpoint}. CompanyId={CompanyId}, Path={Path}",
                nameof(ListChores), User.FindFirst("CompanyId")?.Value, HttpContext.Request.Path);
            return StatusCode(500, ApiErrorResponse.ServerError("An error occurred while processing your request"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in {Endpoint}. CompanyId={CompanyId}, Path={Path}",
                nameof(ListChores), User.FindFirst("CompanyId")?.Value, HttpContext.Request.Path);
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
                _logger.LogWarning("API endpoint not enabled. Endpoint={Endpoint}, Path={Path}",
                    nameof(GetChore), HttpContext.Request.Path);
                return NotFound(ApiErrorResponse.Create("ENDPOINT_DISABLED", "This API endpoint is not enabled"));
            }

            // Get CompanyId from claims
            var companyIdClaim = User.FindFirst("CompanyId")?.Value;
            if (companyIdClaim == null || !int.TryParse(companyIdClaim, out var companyId))
            {
                _logger.LogWarning("Unauthorized API access attempt. Endpoint={Endpoint}, Path={Path}, HasCompanyClaim={HasClaim}",
                    nameof(GetChore), HttpContext.Request.Path, companyIdClaim != null);
                return Unauthorized(ApiErrorResponse.Unauthorized("Invalid authentication"));
            }

            var chore = await _choreService.GetChoreAsync(companyId, id, includeRelated);

            if (chore == null)
            {
                _logger.LogWarning("Chore not found. Endpoint={Endpoint}, CompanyId={CompanyId}, ChoreId={ChoreId}",
                    nameof(GetChore), companyId, id);
                return NotFound(ApiErrorResponse.NotFound("Chore", id.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            }

            return Ok(chore);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Database error in {Endpoint}. CompanyId={CompanyId}, ChoreId={ChoreId}, Path={Path}",
                nameof(GetChore), User.FindFirst("CompanyId")?.Value, id, HttpContext.Request.Path);
            return StatusCode(500, ApiErrorResponse.ServerError("An error occurred while processing your request"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in {Endpoint}. CompanyId={CompanyId}, ChoreId={ChoreId}, Path={Path}",
                nameof(GetChore), User.FindFirst("CompanyId")?.Value, id, HttpContext.Request.Path);
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
                _logger.LogWarning("API endpoint not enabled. Endpoint={Endpoint}, Path={Path}",
                    nameof(CreateChore), HttpContext.Request.Path);
                return NotFound(ApiErrorResponse.Create("ENDPOINT_DISABLED", "This API endpoint is not enabled"));
            }

            // Get CompanyId from claims
            var companyIdClaim = User.FindFirst("CompanyId")?.Value;
            if (companyIdClaim == null || !int.TryParse(companyIdClaim, out var companyId))
            {
                _logger.LogWarning("Unauthorized API access attempt. Endpoint={Endpoint}, Path={Path}, HasCompanyClaim={HasClaim}",
                    nameof(CreateChore), HttpContext.Request.Path, companyIdClaim != null);
                return Unauthorized(ApiErrorResponse.Unauthorized("Invalid authentication"));
            }

            // Get UserId from claims (the creator)
            var userIdClaim = User.FindFirst("UserId")?.Value;
            if (userIdClaim == null || !int.TryParse(userIdClaim, out var creatorId))
            {
                _logger.LogWarning("UserId claim missing. Endpoint={Endpoint}, CompanyId={CompanyId}",
                    nameof(CreateChore), companyId);
                return Unauthorized(ApiErrorResponse.Unauthorized("Invalid authentication"));
            }

            // Validate request
            if (request.UserId <= 0)
            {
                _logger.LogWarning("Validation error: UserId required. Endpoint={Endpoint}, CompanyId={CompanyId}",
                    nameof(CreateChore), companyId);
                return BadRequest(ApiErrorResponse.BadRequest("UserId is required"));
            }

            if (string.IsNullOrWhiteSpace(request.Date))
            {
                _logger.LogWarning("Validation error: Date required. Endpoint={Endpoint}, CompanyId={CompanyId}",
                    nameof(CreateChore), companyId);
                return BadRequest(ApiErrorResponse.BadRequest("Date is required"));
            }

            if (string.IsNullOrWhiteSpace(request.Title))
            {
                _logger.LogWarning("Validation error: Title required. Endpoint={Endpoint}, CompanyId={CompanyId}",
                    nameof(CreateChore), companyId);
                return BadRequest(ApiErrorResponse.BadRequest("Title is required"));
            }

            var (chore, error) = await _choreService.CreateChoreAsync(companyId, creatorId, request);

            if (error != null)
            {
                _logger.LogWarning("Chore creation validation error. Endpoint={Endpoint}, CompanyId={CompanyId}, Error={Error}",
                    nameof(CreateChore), companyId, error);
                return BadRequest(ApiErrorResponse.BadRequest(error));
            }

            _logger.LogInformation("Chore created. Endpoint={Endpoint}, CompanyId={CompanyId}, ChoreId={ChoreId}",
                nameof(CreateChore), companyId, chore!.Id);

            return CreatedAtAction(nameof(GetChore), new { id = chore!.Id }, chore);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Database error in {Endpoint}. CompanyId={CompanyId}, Path={Path}",
                nameof(CreateChore), User.FindFirst("CompanyId")?.Value, HttpContext.Request.Path);
            return StatusCode(500, ApiErrorResponse.ServerError("An error occurred while processing your request"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in {Endpoint}. CompanyId={CompanyId}, Path={Path}",
                nameof(CreateChore), User.FindFirst("CompanyId")?.Value, HttpContext.Request.Path);
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
                _logger.LogWarning("API endpoint not enabled. Endpoint={Endpoint}, Path={Path}",
                    nameof(UpdateChore), HttpContext.Request.Path);
                return NotFound(ApiErrorResponse.Create("ENDPOINT_DISABLED", "This API endpoint is not enabled"));
            }

            // Get CompanyId from claims
            var companyIdClaim = User.FindFirst("CompanyId")?.Value;
            if (companyIdClaim == null || !int.TryParse(companyIdClaim, out var companyId))
            {
                _logger.LogWarning("Unauthorized API access attempt. Endpoint={Endpoint}, Path={Path}, HasCompanyClaim={HasClaim}",
                    nameof(UpdateChore), HttpContext.Request.Path, companyIdClaim != null);
                return Unauthorized(ApiErrorResponse.Unauthorized("Invalid authentication"));
            }

            var (chore, error) = await _choreService.UpdateChoreAsync(companyId, id, request);

            if (error != null)
            {
                if (error.Contains("not found"))
                {
                    _logger.LogWarning("Chore not found for update. Endpoint={Endpoint}, CompanyId={CompanyId}, ChoreId={ChoreId}",
                        nameof(UpdateChore), companyId, id);
                    return NotFound(ApiErrorResponse.Create("NOT_FOUND", error));
                }
                _logger.LogWarning("Chore update validation error. Endpoint={Endpoint}, CompanyId={CompanyId}, ChoreId={ChoreId}, Error={Error}",
                    nameof(UpdateChore), companyId, id, error);
                return BadRequest(ApiErrorResponse.BadRequest(error));
            }

            _logger.LogInformation("Chore updated. Endpoint={Endpoint}, CompanyId={CompanyId}, ChoreId={ChoreId}",
                nameof(UpdateChore), companyId, id);

            return Ok(chore);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Database error in {Endpoint}. CompanyId={CompanyId}, ChoreId={ChoreId}, Path={Path}",
                nameof(UpdateChore), User.FindFirst("CompanyId")?.Value, id, HttpContext.Request.Path);
            return StatusCode(500, ApiErrorResponse.ServerError("An error occurred while processing your request"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in {Endpoint}. CompanyId={CompanyId}, ChoreId={ChoreId}, Path={Path}",
                nameof(UpdateChore), User.FindFirst("CompanyId")?.Value, id, HttpContext.Request.Path);
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
                _logger.LogWarning("API endpoint not enabled. Endpoint={Endpoint}, Path={Path}",
                    nameof(DeleteChore), HttpContext.Request.Path);
                return NotFound(ApiErrorResponse.Create("ENDPOINT_DISABLED", "This API endpoint is not enabled"));
            }

            // Get CompanyId from claims
            var companyIdClaim = User.FindFirst("CompanyId")?.Value;
            if (companyIdClaim == null || !int.TryParse(companyIdClaim, out var companyId))
            {
                _logger.LogWarning("Unauthorized API access attempt. Endpoint={Endpoint}, Path={Path}, HasCompanyClaim={HasClaim}",
                    nameof(DeleteChore), HttpContext.Request.Path, companyIdClaim != null);
                return Unauthorized(ApiErrorResponse.Unauthorized("Invalid authentication"));
            }

            // Get UserId from claims
            var userIdClaim = User.FindFirst("UserId")?.Value;
            if (userIdClaim == null || !int.TryParse(userIdClaim, out var canceledBy))
            {
                _logger.LogWarning("UserId claim missing. Endpoint={Endpoint}, CompanyId={CompanyId}",
                    nameof(DeleteChore), companyId);
                return Unauthorized(ApiErrorResponse.Unauthorized("Invalid authentication"));
            }

            var success = await _choreService.DeleteChoreAsync(companyId, id, canceledBy);

            if (!success)
            {
                _logger.LogWarning("Chore deletion failed. Endpoint={Endpoint}, CompanyId={CompanyId}, ChoreId={ChoreId}",
                    nameof(DeleteChore), companyId, id);
                return NotFound(ApiErrorResponse.Create("CHORE_NOT_FOUND", "Chore not found or already canceled"));
            }

            _logger.LogInformation("Chore deleted. Endpoint={Endpoint}, CompanyId={CompanyId}, ChoreId={ChoreId}, CanceledBy={CanceledBy}",
                nameof(DeleteChore), companyId, id, canceledBy);

            return NoContent();
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Database error in {Endpoint}. CompanyId={CompanyId}, ChoreId={ChoreId}, Path={Path}",
                nameof(DeleteChore), User.FindFirst("CompanyId")?.Value, id, HttpContext.Request.Path);
            return StatusCode(500, ApiErrorResponse.ServerError("An error occurred while processing your request"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in {Endpoint}. CompanyId={CompanyId}, ChoreId={ChoreId}, Path={Path}",
                nameof(DeleteChore), User.FindFirst("CompanyId")?.Value, id, HttpContext.Request.Path);
            return StatusCode(500, ApiErrorResponse.ServerError("An unexpected error occurred"));
        }
    }
}
