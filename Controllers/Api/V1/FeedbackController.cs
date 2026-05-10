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
/// API controller for feedback management.
/// All endpoints require API key authentication via X-API-Key header.
/// </summary>
[Authorize]
[ApiController]
[Route("api/v1/feedback")]
[Produces("application/json")]
public partial class FeedbackController : ControllerBase
{
    private readonly FeedbackApiService _feedbackService;
    private readonly IFeatureFlagService _featureFlagService;
    private readonly ILogger<FeedbackController> _logger;

    public FeedbackController(
        FeedbackApiService feedbackService,
        IFeatureFlagService featureFlagService,
        ILogger<FeedbackController> logger)
    {
        _feedbackService = feedbackService;
        _featureFlagService = featureFlagService;
        _logger = logger;
    }

    /// <summary>
    /// Lists feedback with pagination and filtering.
    /// Requires scope: feedback:read
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(PaginatedResponse<FeedbackDto>), 200)]
    [ProducesResponseType(typeof(ApiErrorResponse), 401)]
    [ProducesResponseType(typeof(ApiErrorResponse), 403)]
    [ProducesResponseType(typeof(ApiErrorResponse), 429)]
    public async Task<IActionResult> ListFeedback(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] int? submittedBy = null,
        [FromQuery] string? type = null,
        [FromQuery] string? status = null,
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null,
        [FromQuery] bool includeRelated = false)
    {
        try
        {
            // Check feature flag
            if (!await _featureFlagService.IsEnabledAsync(FeatureFlagSeed.Flags.ApiFeedbackList))
            {
                LogApiEndpointNotEnabled(_logger, nameof(ListFeedback), HttpContext.Request.Path);
                return NotFound(ApiErrorResponse.Create("ENDPOINT_DISABLED", "This API endpoint is not enabled"));
            }

            // Get CompanyId from claims
            var companyIdClaim = User.FindFirst("CompanyId")?.Value;
            if (companyIdClaim == null || !int.TryParse(companyIdClaim, out var companyId))
            {
                LogApiUnauthorized(_logger, nameof(ListFeedback), HttpContext.Request.Path, companyIdClaim != null);
                return Unauthorized(ApiErrorResponse.Unauthorized("Invalid authentication"));
            }

            var (feedbacks, totalCount) = await _feedbackService.ListFeedbackAsync(
                companyId, page, pageSize, submittedBy, type, status, startDate, endDate, includeRelated);

            var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
            var response = new PaginatedResponse<FeedbackDto>
            {
                Data = feedbacks,
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
            LogDbErrorCompanyPath(_logger, ex, nameof(ListFeedback), User.FindFirst("CompanyId")?.Value, HttpContext.Request.Path);
            return StatusCode(500, ApiErrorResponse.ServerError("An error occurred while processing your request"));
        }
        catch (Exception ex)
        {
            LogUnexpectedErrorCompanyPath(_logger, ex, nameof(ListFeedback), User.FindFirst("CompanyId")?.Value, HttpContext.Request.Path);
            return StatusCode(500, ApiErrorResponse.ServerError("An unexpected error occurred"));
        }
    }

    /// <summary>
    /// Gets a single feedback by ID.
    /// Requires scope: feedback:read
    /// </summary>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(FeedbackDto), 200)]
    [ProducesResponseType(typeof(ApiErrorResponse), 401)]
    [ProducesResponseType(typeof(ApiErrorResponse), 403)]
    [ProducesResponseType(typeof(ApiErrorResponse), 404)]
    [ProducesResponseType(typeof(ApiErrorResponse), 429)]
    public async Task<IActionResult> GetFeedback(int id, [FromQuery] bool includeRelated = true)
    {
        try
        {
            // Check feature flag
            if (!await _featureFlagService.IsEnabledAsync(FeatureFlagSeed.Flags.ApiFeedbackGet))
            {
                LogApiEndpointNotEnabled(_logger, nameof(GetFeedback), HttpContext.Request.Path);
                return NotFound(ApiErrorResponse.Create("ENDPOINT_DISABLED", "This API endpoint is not enabled"));
            }

            // Get CompanyId from claims
            var companyIdClaim = User.FindFirst("CompanyId")?.Value;
            if (companyIdClaim == null || !int.TryParse(companyIdClaim, out var companyId))
            {
                LogApiUnauthorized(_logger, nameof(GetFeedback), HttpContext.Request.Path, companyIdClaim != null);
                return Unauthorized(ApiErrorResponse.Unauthorized("Invalid authentication"));
            }

            var feedback = await _feedbackService.GetFeedbackAsync(companyId, id, includeRelated);

            if (feedback == null)
            {
                LogFeedbackNotFound(_logger, nameof(GetFeedback), companyId, id);
                return NotFound(ApiErrorResponse.NotFound("Feedback", id.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            }

            return Ok(feedback);
        }
        catch (DbUpdateException ex)
        {
            LogDbErrorWithFeedbackId(_logger, ex, nameof(GetFeedback), User.FindFirst("CompanyId")?.Value, id, HttpContext.Request.Path);
            return StatusCode(500, ApiErrorResponse.ServerError("An error occurred while processing your request"));
        }
        catch (Exception ex)
        {
            LogUnexpectedErrorWithFeedbackId(_logger, ex, nameof(GetFeedback), User.FindFirst("CompanyId")?.Value, id, HttpContext.Request.Path);
            return StatusCode(500, ApiErrorResponse.ServerError("An unexpected error occurred"));
        }
    }

    /// <summary>
    /// Creates new feedback.
    /// Requires scope: feedback:write
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(FeedbackDto), 201)]
    [ProducesResponseType(typeof(ApiErrorResponse), 400)]
    [ProducesResponseType(typeof(ApiErrorResponse), 401)]
    [ProducesResponseType(typeof(ApiErrorResponse), 403)]
    [ProducesResponseType(typeof(ApiErrorResponse), 429)]
    public async Task<IActionResult> CreateFeedback([FromBody] CreateFeedbackDto request)
    {
        try
        {
            // Check feature flag
            if (!await _featureFlagService.IsEnabledAsync(FeatureFlagSeed.Flags.ApiFeedbackCreate))
            {
                LogApiEndpointNotEnabled(_logger, nameof(CreateFeedback), HttpContext.Request.Path);
                return NotFound(ApiErrorResponse.Create("ENDPOINT_DISABLED", "This API endpoint is not enabled"));
            }

            // Get CompanyId from claims
            var companyIdClaim = User.FindFirst("CompanyId")?.Value;
            if (companyIdClaim == null || !int.TryParse(companyIdClaim, out var companyId))
            {
                LogApiUnauthorized(_logger, nameof(CreateFeedback), HttpContext.Request.Path, companyIdClaim != null);
                return Unauthorized(ApiErrorResponse.Unauthorized("Invalid authentication"));
            }

            // Get UserId from claims (the submitter)
            var userIdClaim = User.FindFirst("UserId")?.Value;
            if (userIdClaim == null || !int.TryParse(userIdClaim, out var submitterId))
            {
                LogUserIdClaimMissing(_logger, nameof(CreateFeedback), companyId);
                return Unauthorized(ApiErrorResponse.Unauthorized("Invalid authentication"));
            }

            // Validate request
            if (string.IsNullOrWhiteSpace(request.Type))
            {
                LogValidationTypeRequired(_logger, nameof(CreateFeedback), companyId);
                return BadRequest(ApiErrorResponse.BadRequest("Type is required"));
            }

            if (string.IsNullOrWhiteSpace(request.Content))
            {
                LogValidationContentRequired(_logger, nameof(CreateFeedback), companyId);
                return BadRequest(ApiErrorResponse.BadRequest("Content is required"));
            }

            var (feedback, error) = await _feedbackService.CreateFeedbackAsync(companyId, submitterId, request);

            if (error != null)
            {
                LogFeedbackCreationValidationError(_logger, nameof(CreateFeedback), companyId, error);
                return BadRequest(ApiErrorResponse.BadRequest(error));
            }

            LogFeedbackCreated(_logger, nameof(CreateFeedback), companyId, feedback!.Id);

            return CreatedAtAction(nameof(GetFeedback), new { id = feedback!.Id }, feedback);
        }
        catch (DbUpdateException ex)
        {
            LogDbErrorCompanyPath(_logger, ex, nameof(CreateFeedback), User.FindFirst("CompanyId")?.Value, HttpContext.Request.Path);
            return StatusCode(500, ApiErrorResponse.ServerError("An error occurred while processing your request"));
        }
        catch (Exception ex)
        {
            LogUnexpectedErrorCompanyPath(_logger, ex, nameof(CreateFeedback), User.FindFirst("CompanyId")?.Value, HttpContext.Request.Path);
            return StatusCode(500, ApiErrorResponse.ServerError("An unexpected error occurred"));
        }
    }

    /// <summary>
    /// Updates feedback status.
    /// Requires scope: feedback:write
    /// </summary>
    [HttpPatch("{id}/status")]
    [ProducesResponseType(typeof(FeedbackDto), 200)]
    [ProducesResponseType(typeof(ApiErrorResponse), 400)]
    [ProducesResponseType(typeof(ApiErrorResponse), 401)]
    [ProducesResponseType(typeof(ApiErrorResponse), 403)]
    [ProducesResponseType(typeof(ApiErrorResponse), 404)]
    [ProducesResponseType(typeof(ApiErrorResponse), 429)]
    public async Task<IActionResult> UpdateFeedbackStatus(int id, [FromBody] UpdateFeedbackStatusDto request)
    {
        try
        {
            // Check feature flag
            if (!await _featureFlagService.IsEnabledAsync(FeatureFlagSeed.Flags.ApiFeedbackUpdateStatus))
            {
                LogApiEndpointNotEnabled(_logger, nameof(UpdateFeedbackStatus), HttpContext.Request.Path);
                return NotFound(ApiErrorResponse.Create("ENDPOINT_DISABLED", "This API endpoint is not enabled"));
            }

            // Get CompanyId from claims
            var companyIdClaim = User.FindFirst("CompanyId")?.Value;
            if (companyIdClaim == null || !int.TryParse(companyIdClaim, out var companyId))
            {
                LogApiUnauthorized(_logger, nameof(UpdateFeedbackStatus), HttpContext.Request.Path, companyIdClaim != null);
                return Unauthorized(ApiErrorResponse.Unauthorized("Invalid authentication"));
            }

            // Get UserId from claims
            var userIdClaim = User.FindFirst("UserId")?.Value;
            if (userIdClaim == null || !int.TryParse(userIdClaim, out var updaterId))
            {
                LogUserIdClaimMissing(_logger, nameof(UpdateFeedbackStatus), companyId);
                return Unauthorized(ApiErrorResponse.Unauthorized("Invalid authentication"));
            }

            var (feedback, error) = await _feedbackService.UpdateFeedbackStatusAsync(companyId, id, updaterId, request);

            if (error != null)
            {
                if (error.Contains("not found"))
                {
                    LogFeedbackNotFoundForUpdate(_logger, nameof(UpdateFeedbackStatus), companyId, id);
                    return NotFound(ApiErrorResponse.Create("NOT_FOUND", error));
                }
                LogFeedbackStatusUpdateValidationError(_logger, nameof(UpdateFeedbackStatus), companyId, id, error);
                return BadRequest(ApiErrorResponse.BadRequest(error));
            }

            LogFeedbackStatusUpdated(_logger, nameof(UpdateFeedbackStatus), companyId, id);

            return Ok(feedback);
        }
        catch (DbUpdateException ex)
        {
            LogDbErrorWithFeedbackId(_logger, ex, nameof(UpdateFeedbackStatus), User.FindFirst("CompanyId")?.Value, id, HttpContext.Request.Path);
            return StatusCode(500, ApiErrorResponse.ServerError("An error occurred while processing your request"));
        }
        catch (Exception ex)
        {
            LogUnexpectedErrorWithFeedbackId(_logger, ex, nameof(UpdateFeedbackStatus), User.FindFirst("CompanyId")?.Value, id, HttpContext.Request.Path);
            return StatusCode(500, ApiErrorResponse.ServerError("An unexpected error occurred"));
        }
    }

    /// <summary>
    /// Deletes feedback.
    /// Requires scope: feedback:write
    /// </summary>
    [HttpDelete("{id}")]
    [ProducesResponseType(204)]
    [ProducesResponseType(typeof(ApiErrorResponse), 400)]
    [ProducesResponseType(typeof(ApiErrorResponse), 401)]
    [ProducesResponseType(typeof(ApiErrorResponse), 403)]
    [ProducesResponseType(typeof(ApiErrorResponse), 404)]
    [ProducesResponseType(typeof(ApiErrorResponse), 429)]
    public async Task<IActionResult> DeleteFeedback(int id)
    {
        try
        {
            // Check feature flag
            if (!await _featureFlagService.IsEnabledAsync(FeatureFlagSeed.Flags.ApiFeedbackDelete))
            {
                LogApiEndpointNotEnabled(_logger, nameof(DeleteFeedback), HttpContext.Request.Path);
                return NotFound(ApiErrorResponse.Create("ENDPOINT_DISABLED", "This API endpoint is not enabled"));
            }

            // Get CompanyId from claims
            var companyIdClaim = User.FindFirst("CompanyId")?.Value;
            if (companyIdClaim == null || !int.TryParse(companyIdClaim, out var companyId))
            {
                LogApiUnauthorized(_logger, nameof(DeleteFeedback), HttpContext.Request.Path, companyIdClaim != null);
                return Unauthorized(ApiErrorResponse.Unauthorized("Invalid authentication"));
            }

            var success = await _feedbackService.DeleteFeedbackAsync(companyId, id);

            if (!success)
            {
                LogFeedbackDeletionFailed(_logger, nameof(DeleteFeedback), companyId, id);
                return NotFound(ApiErrorResponse.Create("FEEDBACK_NOT_FOUND", "Feedback not found"));
            }

            LogFeedbackDeleted(_logger, nameof(DeleteFeedback), companyId, id);

            return NoContent();
        }
        catch (DbUpdateException ex)
        {
            LogDbErrorWithFeedbackId(_logger, ex, nameof(DeleteFeedback), User.FindFirst("CompanyId")?.Value, id, HttpContext.Request.Path);
            return StatusCode(500, ApiErrorResponse.ServerError("An error occurred while processing your request"));
        }
        catch (Exception ex)
        {
            LogUnexpectedErrorWithFeedbackId(_logger, ex, nameof(DeleteFeedback), User.FindFirst("CompanyId")?.Value, id, HttpContext.Request.Path);
            return StatusCode(500, ApiErrorResponse.ServerError("An unexpected error occurred"));
        }
    }
}
