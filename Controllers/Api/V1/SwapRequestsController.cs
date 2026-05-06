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
/// API controller for swap request management.
/// All endpoints require API key authentication via X-API-Key header.
/// </summary>
[Authorize]
[ApiController]
[Route("api/v1/swap-requests")]
[Produces("application/json")]
public class SwapRequestsController : ControllerBase
{
    private readonly SwapRequestApiService _swapRequestService;
    private readonly IFeatureFlagService _featureFlagService;
    private readonly ILogger<SwapRequestsController> _logger;

    public SwapRequestsController(
        SwapRequestApiService swapRequestService,
        IFeatureFlagService featureFlagService,
        ILogger<SwapRequestsController> logger)
    {
        _swapRequestService = swapRequestService;
        _featureFlagService = featureFlagService;
        _logger = logger;
    }

    /// <summary>
    /// Lists swap requests with pagination and filtering.
    /// Requires scope: swaps:read
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(PaginatedResponse<SwapRequestDto>), 200)]
    [ProducesResponseType(typeof(ApiErrorResponse), 401)]
    [ProducesResponseType(typeof(ApiErrorResponse), 403)]
    [ProducesResponseType(typeof(ApiErrorResponse), 429)]
    public async Task<IActionResult> ListSwapRequests(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] int? userId = null,
        [FromQuery] string? status = null,
        [FromQuery] string? startDate = null,
        [FromQuery] string? endDate = null,
        [FromQuery] bool includeRelated = false)
    {
        try
        {
            // Check feature flag
            if (!await _featureFlagService.IsEnabledAsync(FeatureFlagSeed.Flags.ApiSwapRequestsList))
            {
                _logger.LogWarning("API endpoint not enabled. Endpoint={Endpoint}, Path={Path}",
                    nameof(ListSwapRequests), HttpContext.Request.Path);
                return NotFound(ApiErrorResponse.Create("ENDPOINT_DISABLED", "This API endpoint is not enabled"));
            }

            // Get CompanyId from claims
            var companyIdClaim = User.FindFirst("CompanyId")?.Value;
            if (companyIdClaim == null || !int.TryParse(companyIdClaim, out var companyId))
            {
                _logger.LogWarning("Unauthorized API access attempt. Endpoint={Endpoint}, Path={Path}, HasCompanyClaim={HasClaim}",
                    nameof(ListSwapRequests), HttpContext.Request.Path, companyIdClaim != null);
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
                        nameof(ListSwapRequests), companyId, startDate);
                    return BadRequest(ApiErrorResponse.BadRequest("Invalid startDate format. Use yyyy-MM-dd"));
                }
                startDateParsed = parsed;
            }

            if (!string.IsNullOrEmpty(endDate))
            {
                if (!DateOnly.TryParse(endDate, out var parsed))
                {
                    _logger.LogWarning("Invalid endDate format. Endpoint={Endpoint}, CompanyId={CompanyId}, EndDate={EndDate}",
                        nameof(ListSwapRequests), companyId, endDate);
                    return BadRequest(ApiErrorResponse.BadRequest("Invalid endDate format. Use yyyy-MM-dd"));
                }
                endDateParsed = parsed;
            }

            var (requests, totalCount) = await _swapRequestService.ListSwapRequestsAsync(
                companyId, page, pageSize, userId, status, startDateParsed, endDateParsed, includeRelated);

            var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
            var response = new PaginatedResponse<SwapRequestDto>
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
                nameof(ListSwapRequests), User.FindFirst("CompanyId")?.Value, HttpContext.Request.Path);
            return StatusCode(500, ApiErrorResponse.ServerError("An error occurred while processing your request"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in {Endpoint}. CompanyId={CompanyId}, Path={Path}",
                nameof(ListSwapRequests), User.FindFirst("CompanyId")?.Value, HttpContext.Request.Path);
            return StatusCode(500, ApiErrorResponse.ServerError("An unexpected error occurred"));
        }
    }

    /// <summary>
    /// Gets a single swap request by ID.
    /// Requires scope: swaps:read
    /// </summary>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(SwapRequestDto), 200)]
    [ProducesResponseType(typeof(ApiErrorResponse), 401)]
    [ProducesResponseType(typeof(ApiErrorResponse), 403)]
    [ProducesResponseType(typeof(ApiErrorResponse), 404)]
    [ProducesResponseType(typeof(ApiErrorResponse), 429)]
    public async Task<IActionResult> GetSwapRequest(int id, [FromQuery] bool includeRelated = true)
    {
        try
        {
            // Check feature flag
            if (!await _featureFlagService.IsEnabledAsync(FeatureFlagSeed.Flags.ApiSwapRequestsGet))
            {
                _logger.LogWarning("API endpoint not enabled. Endpoint={Endpoint}, Path={Path}",
                    nameof(GetSwapRequest), HttpContext.Request.Path);
                return NotFound(ApiErrorResponse.Create("ENDPOINT_DISABLED", "This API endpoint is not enabled"));
            }

            // Get CompanyId from claims
            var companyIdClaim = User.FindFirst("CompanyId")?.Value;
            if (companyIdClaim == null || !int.TryParse(companyIdClaim, out var companyId))
            {
                _logger.LogWarning("Unauthorized API access attempt. Endpoint={Endpoint}, Path={Path}, HasCompanyClaim={HasClaim}",
                    nameof(GetSwapRequest), HttpContext.Request.Path, companyIdClaim != null);
                return Unauthorized(ApiErrorResponse.Unauthorized("Invalid authentication"));
            }

            var request = await _swapRequestService.GetSwapRequestAsync(companyId, id, includeRelated);

            if (request == null)
            {
                _logger.LogWarning("Swap request not found. Endpoint={Endpoint}, CompanyId={CompanyId}, RequestId={RequestId}",
                    nameof(GetSwapRequest), companyId, id);
                return NotFound(ApiErrorResponse.NotFound("SwapRequest", id.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            }

            return Ok(request);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Database error in {Endpoint}. CompanyId={CompanyId}, RequestId={RequestId}, Path={Path}",
                nameof(GetSwapRequest), User.FindFirst("CompanyId")?.Value, id, HttpContext.Request.Path);
            return StatusCode(500, ApiErrorResponse.ServerError("An error occurred while processing your request"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in {Endpoint}. CompanyId={CompanyId}, RequestId={RequestId}, Path={Path}",
                nameof(GetSwapRequest), User.FindFirst("CompanyId")?.Value, id, HttpContext.Request.Path);
            return StatusCode(500, ApiErrorResponse.ServerError("An unexpected error occurred"));
        }
    }

    /// <summary>
    /// Creates a new swap request.
    /// Requires scope: swaps:write
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(SwapRequestDto), 201)]
    [ProducesResponseType(typeof(ApiErrorResponse), 400)]
    [ProducesResponseType(typeof(ApiErrorResponse), 401)]
    [ProducesResponseType(typeof(ApiErrorResponse), 403)]
    [ProducesResponseType(typeof(ApiErrorResponse), 429)]
    public async Task<IActionResult> CreateSwapRequest([FromBody] CreateSwapRequestDto request)
    {
        try
        {
            // Check feature flag
            if (!await _featureFlagService.IsEnabledAsync(FeatureFlagSeed.Flags.ApiSwapRequestsCreate))
            {
                _logger.LogWarning("API endpoint not enabled. Endpoint={Endpoint}, Path={Path}",
                    nameof(CreateSwapRequest), HttpContext.Request.Path);
                return NotFound(ApiErrorResponse.Create("ENDPOINT_DISABLED", "This API endpoint is not enabled"));
            }

            // Get CompanyId from claims
            var companyIdClaim = User.FindFirst("CompanyId")?.Value;
            if (companyIdClaim == null || !int.TryParse(companyIdClaim, out var companyId))
            {
                _logger.LogWarning("Unauthorized API access attempt. Endpoint={Endpoint}, Path={Path}, HasCompanyClaim={HasClaim}",
                    nameof(CreateSwapRequest), HttpContext.Request.Path, companyIdClaim != null);
                return Unauthorized(ApiErrorResponse.Unauthorized("Invalid authentication"));
            }

            // Get UserId from claims (the user making the request)
            var userIdClaim = User.FindFirst("UserId")?.Value;
            if (userIdClaim == null || !int.TryParse(userIdClaim, out var fromUserId))
            {
                _logger.LogWarning("UserId claim missing. Endpoint={Endpoint}, CompanyId={CompanyId}",
                    nameof(CreateSwapRequest), companyId);
                return Unauthorized(ApiErrorResponse.Unauthorized("Invalid authentication"));
            }

            // Validate request
            if (request.FromAssignmentId <= 0)
            {
                _logger.LogWarning("Validation error: FromAssignmentId required. Endpoint={Endpoint}, CompanyId={CompanyId}, UserId={UserId}",
                    nameof(CreateSwapRequest), companyId, fromUserId);
                return BadRequest(ApiErrorResponse.BadRequest("FromAssignmentId is required"));
            }

            var (swapRequest, error) = await _swapRequestService.CreateSwapRequestAsync(
                companyId, fromUserId, request);

            if (error != null)
            {
                _logger.LogWarning("Swap request validation error. Endpoint={Endpoint}, CompanyId={CompanyId}, UserId={UserId}, Error={Error}",
                    nameof(CreateSwapRequest), companyId, fromUserId, error);
                return BadRequest(ApiErrorResponse.BadRequest(error));
            }

            _logger.LogInformation("Swap request created. Endpoint={Endpoint}, CompanyId={CompanyId}, FromUserId={FromUserId}, RequestId={RequestId}",
                nameof(CreateSwapRequest), companyId, fromUserId, swapRequest!.Id);

            return CreatedAtAction(nameof(GetSwapRequest), new { id = swapRequest!.Id }, swapRequest);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Database error in {Endpoint}. CompanyId={CompanyId}, Path={Path}",
                nameof(CreateSwapRequest), User.FindFirst("CompanyId")?.Value, HttpContext.Request.Path);
            return StatusCode(500, ApiErrorResponse.ServerError("An error occurred while processing your request"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in {Endpoint}. CompanyId={CompanyId}, Path={Path}",
                nameof(CreateSwapRequest), User.FindFirst("CompanyId")?.Value, HttpContext.Request.Path);
            return StatusCode(500, ApiErrorResponse.ServerError("An unexpected error occurred"));
        }
    }

    /// <summary>
    /// Approves a swap request (admin operation).
    /// Requires scope: swaps:approve
    /// </summary>
    [HttpPost("{id}/approve")]
    [ProducesResponseType(typeof(SwapRequestDto), 200)]
    [ProducesResponseType(typeof(ApiErrorResponse), 400)]
    [ProducesResponseType(typeof(ApiErrorResponse), 401)]
    [ProducesResponseType(typeof(ApiErrorResponse), 403)]
    [ProducesResponseType(typeof(ApiErrorResponse), 404)]
    [ProducesResponseType(typeof(ApiErrorResponse), 429)]
    public async Task<IActionResult> ApproveSwapRequest(int id)
    {
        try
        {
            // Check feature flag
            if (!await _featureFlagService.IsEnabledAsync(FeatureFlagSeed.Flags.ApiSwapRequestsApprove))
            {
                _logger.LogWarning("API endpoint not enabled. Endpoint={Endpoint}, Path={Path}",
                    nameof(ApproveSwapRequest), HttpContext.Request.Path);
                return NotFound(ApiErrorResponse.Create("ENDPOINT_DISABLED", "This API endpoint is not enabled"));
            }

            // Get CompanyId from claims
            var companyIdClaim = User.FindFirst("CompanyId")?.Value;
            if (companyIdClaim == null || !int.TryParse(companyIdClaim, out var companyId))
            {
                _logger.LogWarning("Unauthorized API access attempt. Endpoint={Endpoint}, Path={Path}, HasCompanyClaim={HasClaim}",
                    nameof(ApproveSwapRequest), HttpContext.Request.Path, companyIdClaim != null);
                return Unauthorized(ApiErrorResponse.Unauthorized("Invalid authentication"));
            }

            // Get UserId from claims (the reviewer)
            var userIdClaim = User.FindFirst("UserId")?.Value;
            if (userIdClaim == null || !int.TryParse(userIdClaim, out var reviewerId))
            {
                _logger.LogWarning("UserId claim missing. Endpoint={Endpoint}, CompanyId={CompanyId}",
                    nameof(ApproveSwapRequest), companyId);
                return Unauthorized(ApiErrorResponse.Unauthorized("Invalid authentication"));
            }

            var (swapRequest, error) = await _swapRequestService.ApproveSwapRequestAsync(companyId, id, reviewerId);

            if (error != null)
            {
                if (error.Contains("not found"))
                {
                    _logger.LogWarning("Swap request not found for approval. Endpoint={Endpoint}, CompanyId={CompanyId}, RequestId={RequestId}",
                        nameof(ApproveSwapRequest), companyId, id);
                    return NotFound(ApiErrorResponse.Create("NOT_FOUND", error));
                }
                _logger.LogWarning("Swap request approval validation error. Endpoint={Endpoint}, CompanyId={CompanyId}, RequestId={RequestId}, Error={Error}",
                    nameof(ApproveSwapRequest), companyId, id, error);
                return BadRequest(ApiErrorResponse.BadRequest(error));
            }

            _logger.LogInformation("Swap request approved. Endpoint={Endpoint}, CompanyId={CompanyId}, RequestId={RequestId}, ReviewerId={ReviewerId}",
                nameof(ApproveSwapRequest), companyId, id, reviewerId);

            return Ok(swapRequest);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Database error in {Endpoint}. CompanyId={CompanyId}, RequestId={RequestId}, Path={Path}",
                nameof(ApproveSwapRequest), User.FindFirst("CompanyId")?.Value, id, HttpContext.Request.Path);
            return StatusCode(500, ApiErrorResponse.ServerError("An error occurred while processing your request"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in {Endpoint}. CompanyId={CompanyId}, RequestId={RequestId}, Path={Path}",
                nameof(ApproveSwapRequest), User.FindFirst("CompanyId")?.Value, id, HttpContext.Request.Path);
            return StatusCode(500, ApiErrorResponse.ServerError("An unexpected error occurred"));
        }
    }

    /// <summary>
    /// Declines a swap request (admin operation).
    /// Requires scope: swaps:approve
    /// </summary>
    [HttpPost("{id}/decline")]
    [ProducesResponseType(typeof(SwapRequestDto), 200)]
    [ProducesResponseType(typeof(ApiErrorResponse), 400)]
    [ProducesResponseType(typeof(ApiErrorResponse), 401)]
    [ProducesResponseType(typeof(ApiErrorResponse), 403)]
    [ProducesResponseType(typeof(ApiErrorResponse), 404)]
    [ProducesResponseType(typeof(ApiErrorResponse), 429)]
    public async Task<IActionResult> DeclineSwapRequest(int id, [FromBody] DeclineSwapRequestDto request)
    {
        try
        {
            // Check feature flag
            if (!await _featureFlagService.IsEnabledAsync(FeatureFlagSeed.Flags.ApiSwapRequestsDecline))
            {
                _logger.LogWarning("API endpoint not enabled. Endpoint={Endpoint}, Path={Path}",
                    nameof(DeclineSwapRequest), HttpContext.Request.Path);
                return NotFound(ApiErrorResponse.Create("ENDPOINT_DISABLED", "This API endpoint is not enabled"));
            }

            // Get CompanyId from claims
            var companyIdClaim = User.FindFirst("CompanyId")?.Value;
            if (companyIdClaim == null || !int.TryParse(companyIdClaim, out var companyId))
            {
                _logger.LogWarning("Unauthorized API access attempt. Endpoint={Endpoint}, Path={Path}, HasCompanyClaim={HasClaim}",
                    nameof(DeclineSwapRequest), HttpContext.Request.Path, companyIdClaim != null);
                return Unauthorized(ApiErrorResponse.Unauthorized("Invalid authentication"));
            }

            // Get UserId from claims (the reviewer)
            var userIdClaim = User.FindFirst("UserId")?.Value;
            if (userIdClaim == null || !int.TryParse(userIdClaim, out var reviewerId))
            {
                _logger.LogWarning("UserId claim missing. Endpoint={Endpoint}, CompanyId={CompanyId}",
                    nameof(DeclineSwapRequest), companyId);
                return Unauthorized(ApiErrorResponse.Unauthorized("Invalid authentication"));
            }

            var (swapRequest, error) = await _swapRequestService.DeclineSwapRequestAsync(
                companyId, id, reviewerId, request.DeclineReason);

            if (error != null)
            {
                if (error.Contains("not found"))
                {
                    _logger.LogWarning("Swap request not found for decline. Endpoint={Endpoint}, CompanyId={CompanyId}, RequestId={RequestId}",
                        nameof(DeclineSwapRequest), companyId, id);
                    return NotFound(ApiErrorResponse.Create("NOT_FOUND", error));
                }
                _logger.LogWarning("Swap request decline validation error. Endpoint={Endpoint}, CompanyId={CompanyId}, RequestId={RequestId}, Error={Error}",
                    nameof(DeclineSwapRequest), companyId, id, error);
                return BadRequest(ApiErrorResponse.BadRequest(error));
            }

            _logger.LogInformation("Swap request declined. Endpoint={Endpoint}, CompanyId={CompanyId}, RequestId={RequestId}, ReviewerId={ReviewerId}",
                nameof(DeclineSwapRequest), companyId, id, reviewerId);

            return Ok(swapRequest);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Database error in {Endpoint}. CompanyId={CompanyId}, RequestId={RequestId}, Path={Path}",
                nameof(DeclineSwapRequest), User.FindFirst("CompanyId")?.Value, id, HttpContext.Request.Path);
            return StatusCode(500, ApiErrorResponse.ServerError("An error occurred while processing your request"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in {Endpoint}. CompanyId={CompanyId}, RequestId={RequestId}, Path={Path}",
                nameof(DeclineSwapRequest), User.FindFirst("CompanyId")?.Value, id, HttpContext.Request.Path);
            return StatusCode(500, ApiErrorResponse.ServerError("An unexpected error occurred"));
        }
    }

    /// <summary>
    /// Deletes (cancels) a swap request. Only the requester can cancel their own pending request.
    /// Requires scope: swaps:write
    /// </summary>
    [HttpDelete("{id}")]
    [ProducesResponseType(204)]
    [ProducesResponseType(typeof(ApiErrorResponse), 400)]
    [ProducesResponseType(typeof(ApiErrorResponse), 401)]
    [ProducesResponseType(typeof(ApiErrorResponse), 403)]
    [ProducesResponseType(typeof(ApiErrorResponse), 404)]
    [ProducesResponseType(typeof(ApiErrorResponse), 429)]
    public async Task<IActionResult> DeleteSwapRequest(int id)
    {
        try
        {
            // Check feature flag
            if (!await _featureFlagService.IsEnabledAsync(FeatureFlagSeed.Flags.ApiSwapRequestsDelete))
            {
                _logger.LogWarning("API endpoint not enabled. Endpoint={Endpoint}, Path={Path}",
                    nameof(DeleteSwapRequest), HttpContext.Request.Path);
                return NotFound(ApiErrorResponse.Create("ENDPOINT_DISABLED", "This API endpoint is not enabled"));
            }

            // Get CompanyId from claims
            var companyIdClaim = User.FindFirst("CompanyId")?.Value;
            if (companyIdClaim == null || !int.TryParse(companyIdClaim, out var companyId))
            {
                _logger.LogWarning("Unauthorized API access attempt. Endpoint={Endpoint}, Path={Path}, HasCompanyClaim={HasClaim}",
                    nameof(DeleteSwapRequest), HttpContext.Request.Path, companyIdClaim != null);
                return Unauthorized(ApiErrorResponse.Unauthorized("Invalid authentication"));
            }

            // Get UserId from claims
            var userIdClaim = User.FindFirst("UserId")?.Value;
            if (userIdClaim == null || !int.TryParse(userIdClaim, out var userId))
            {
                _logger.LogWarning("UserId claim missing. Endpoint={Endpoint}, CompanyId={CompanyId}",
                    nameof(DeleteSwapRequest), companyId);
                return Unauthorized(ApiErrorResponse.Unauthorized("Invalid authentication"));
            }

            var success = await _swapRequestService.DeleteSwapRequestAsync(companyId, id, userId);

            if (!success)
            {
                _logger.LogWarning("Swap request deletion failed. Endpoint={Endpoint}, CompanyId={CompanyId}, RequestId={RequestId}, UserId={UserId}",
                    nameof(DeleteSwapRequest), companyId, id, userId);
                return NotFound(ApiErrorResponse.Create("SWAP_REQUEST_NOT_FOUND", "Swap request not found or cannot be deleted (may not be yours or already processed)"));
            }

            _logger.LogInformation("Swap request deleted. Endpoint={Endpoint}, CompanyId={CompanyId}, RequestId={RequestId}, UserId={UserId}",
                nameof(DeleteSwapRequest), companyId, id, userId);

            return NoContent();
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Database error in {Endpoint}. CompanyId={CompanyId}, RequestId={RequestId}, Path={Path}",
                nameof(DeleteSwapRequest), User.FindFirst("CompanyId")?.Value, id, HttpContext.Request.Path);
            return StatusCode(500, ApiErrorResponse.ServerError("An error occurred while processing your request"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in {Endpoint}. CompanyId={CompanyId}, RequestId={RequestId}, Path={Path}",
                nameof(DeleteSwapRequest), User.FindFirst("CompanyId")?.Value, id, HttpContext.Request.Path);
            return StatusCode(500, ApiErrorResponse.ServerError("An unexpected error occurred"));
        }
    }
}
