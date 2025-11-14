using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Models.Api;
using ShiftManager.Models.Api.Dto;
using ShiftManager.Services.Api;

namespace ShiftManager.Controllers.Api.V1;

/// <summary>
/// API controller for feedback management.
/// All endpoints require API key authentication via X-API-Key header.
/// </summary>
[ApiController]
[Route("api/v1/feedback")]
[Produces("application/json")]
public class FeedbackController : ControllerBase
{
    private readonly FeedbackApiService _feedbackService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<FeedbackController> _logger;

    public FeedbackController(
        FeedbackApiService feedbackService,
        IConfiguration configuration,
        ILogger<FeedbackController> logger)
    {
        _feedbackService = feedbackService;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Lists feedback with pagination and filtering.
    /// Requires scope: feedback:read
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(PaginatedResponse<FeedbackDto>), 200)]
    [ProducesResponseType(typeof(ApiProblemDetails), 401)]
    [ProducesResponseType(typeof(ApiProblemDetails), 403)]
    [ProducesResponseType(typeof(ApiProblemDetails), 429)]
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
            if (!_configuration.GetValue<bool>("Features:Api:Feedback:ListEnabled", false))
            {
                _logger.LogWarning("API endpoint not enabled. Endpoint={Endpoint}, Path={Path}",
                    nameof(ListFeedback), HttpContext.Request.Path);
                return NotFound(ApiProblemDetails.NotFound("This API endpoint is not enabled", HttpContext.Request.Path));
            }

            // Get CompanyId from claims
            var companyIdClaim = User.FindFirst("CompanyId")?.Value;
            if (companyIdClaim == null || !int.TryParse(companyIdClaim, out var companyId))
            {
                _logger.LogWarning("Unauthorized API access attempt. Endpoint={Endpoint}, Path={Path}, HasCompanyClaim={HasClaim}",
                    nameof(ListFeedback), HttpContext.Request.Path, companyIdClaim != null);
                return Unauthorized(ApiProblemDetails.Unauthorized("Invalid authentication", HttpContext.Request.Path));
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
            _logger.LogError(ex, "Database error in {Endpoint}. CompanyId={CompanyId}, Path={Path}",
                nameof(ListFeedback), User.FindFirst("CompanyId")?.Value, HttpContext.Request.Path);
            return StatusCode(500, ApiProblemDetails.InternalError(
                "An error occurred while processing your request", HttpContext.Request.Path));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in {Endpoint}. CompanyId={CompanyId}, Path={Path}",
                nameof(ListFeedback), User.FindFirst("CompanyId")?.Value, HttpContext.Request.Path);
            return StatusCode(500, ApiProblemDetails.InternalError(
                "An unexpected error occurred", HttpContext.Request.Path));
        }
    }

    /// <summary>
    /// Gets a single feedback by ID.
    /// Requires scope: feedback:read
    /// </summary>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(FeedbackDto), 200)]
    [ProducesResponseType(typeof(ApiProblemDetails), 401)]
    [ProducesResponseType(typeof(ApiProblemDetails), 403)]
    [ProducesResponseType(typeof(ApiProblemDetails), 404)]
    [ProducesResponseType(typeof(ApiProblemDetails), 429)]
    public async Task<IActionResult> GetFeedback(int id, [FromQuery] bool includeRelated = true)
    {
        try
        {
            // Check feature flag
            if (!_configuration.GetValue<bool>("Features:Api:Feedback:GetEnabled", false))
            {
                _logger.LogWarning("API endpoint not enabled. Endpoint={Endpoint}, Path={Path}",
                    nameof(GetFeedback), HttpContext.Request.Path);
                return NotFound(ApiProblemDetails.NotFound("This API endpoint is not enabled", HttpContext.Request.Path));
            }

            // Get CompanyId from claims
            var companyIdClaim = User.FindFirst("CompanyId")?.Value;
            if (companyIdClaim == null || !int.TryParse(companyIdClaim, out var companyId))
            {
                _logger.LogWarning("Unauthorized API access attempt. Endpoint={Endpoint}, Path={Path}, HasCompanyClaim={HasClaim}",
                    nameof(GetFeedback), HttpContext.Request.Path, companyIdClaim != null);
                return Unauthorized(ApiProblemDetails.Unauthorized("Invalid authentication", HttpContext.Request.Path));
            }

            var feedback = await _feedbackService.GetFeedbackAsync(companyId, id, includeRelated);

            if (feedback == null)
            {
                _logger.LogWarning("Feedback not found. Endpoint={Endpoint}, CompanyId={CompanyId}, FeedbackId={FeedbackId}",
                    nameof(GetFeedback), companyId, id);
                return NotFound(ApiProblemDetails.NotFound($"Feedback {id} not found", HttpContext.Request.Path));
            }

            return Ok(feedback);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Database error in {Endpoint}. CompanyId={CompanyId}, FeedbackId={FeedbackId}, Path={Path}",
                nameof(GetFeedback), User.FindFirst("CompanyId")?.Value, id, HttpContext.Request.Path);
            return StatusCode(500, ApiProblemDetails.InternalError(
                "An error occurred while processing your request", HttpContext.Request.Path));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in {Endpoint}. CompanyId={CompanyId}, FeedbackId={FeedbackId}, Path={Path}",
                nameof(GetFeedback), User.FindFirst("CompanyId")?.Value, id, HttpContext.Request.Path);
            return StatusCode(500, ApiProblemDetails.InternalError(
                "An unexpected error occurred", HttpContext.Request.Path));
        }
    }

    /// <summary>
    /// Creates new feedback.
    /// Requires scope: feedback:write
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(FeedbackDto), 201)]
    [ProducesResponseType(typeof(ApiProblemDetails), 400)]
    [ProducesResponseType(typeof(ApiProblemDetails), 401)]
    [ProducesResponseType(typeof(ApiProblemDetails), 403)]
    [ProducesResponseType(typeof(ApiProblemDetails), 429)]
    public async Task<IActionResult> CreateFeedback([FromBody] CreateFeedbackDto request)
    {
        try
        {
            // Check feature flag
            if (!_configuration.GetValue<bool>("Features:Api:Feedback:CreateEnabled", false))
            {
                _logger.LogWarning("API endpoint not enabled. Endpoint={Endpoint}, Path={Path}",
                    nameof(CreateFeedback), HttpContext.Request.Path);
                return NotFound(ApiProblemDetails.NotFound("This API endpoint is not enabled", HttpContext.Request.Path));
            }

            // Get CompanyId from claims
            var companyIdClaim = User.FindFirst("CompanyId")?.Value;
            if (companyIdClaim == null || !int.TryParse(companyIdClaim, out var companyId))
            {
                _logger.LogWarning("Unauthorized API access attempt. Endpoint={Endpoint}, Path={Path}, HasCompanyClaim={HasClaim}",
                    nameof(CreateFeedback), HttpContext.Request.Path, companyIdClaim != null);
                return Unauthorized(ApiProblemDetails.Unauthorized("Invalid authentication", HttpContext.Request.Path));
            }

            // Get UserId from claims (the submitter)
            var userIdClaim = User.FindFirst("UserId")?.Value;
            if (userIdClaim == null || !int.TryParse(userIdClaim, out var submitterId))
            {
                _logger.LogWarning("UserId claim missing. Endpoint={Endpoint}, CompanyId={CompanyId}",
                    nameof(CreateFeedback), companyId);
                return Unauthorized(ApiProblemDetails.Unauthorized("Invalid authentication", HttpContext.Request.Path));
            }

            // Validate request
            if (string.IsNullOrWhiteSpace(request.Type))
            {
                _logger.LogWarning("Validation error: Type required. Endpoint={Endpoint}, CompanyId={CompanyId}",
                    nameof(CreateFeedback), companyId);
                return BadRequest(ApiProblemDetails.ValidationError("Type is required", HttpContext.Request.Path));
            }

            if (string.IsNullOrWhiteSpace(request.Content))
            {
                _logger.LogWarning("Validation error: Content required. Endpoint={Endpoint}, CompanyId={CompanyId}",
                    nameof(CreateFeedback), companyId);
                return BadRequest(ApiProblemDetails.ValidationError("Content is required", HttpContext.Request.Path));
            }

            var (feedback, error) = await _feedbackService.CreateFeedbackAsync(companyId, submitterId, request);

            if (error != null)
            {
                _logger.LogWarning("Feedback creation validation error. Endpoint={Endpoint}, CompanyId={CompanyId}, Error={Error}",
                    nameof(CreateFeedback), companyId, error);
                return BadRequest(ApiProblemDetails.ValidationError(error, HttpContext.Request.Path));
            }

            _logger.LogInformation("Feedback created. Endpoint={Endpoint}, CompanyId={CompanyId}, FeedbackId={FeedbackId}",
                nameof(CreateFeedback), companyId, feedback!.Id);

            return CreatedAtAction(nameof(GetFeedback), new { id = feedback!.Id }, feedback);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Database error in {Endpoint}. CompanyId={CompanyId}, Path={Path}",
                nameof(CreateFeedback), User.FindFirst("CompanyId")?.Value, HttpContext.Request.Path);
            return StatusCode(500, ApiProblemDetails.InternalError(
                "An error occurred while processing your request", HttpContext.Request.Path));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in {Endpoint}. CompanyId={CompanyId}, Path={Path}",
                nameof(CreateFeedback), User.FindFirst("CompanyId")?.Value, HttpContext.Request.Path);
            return StatusCode(500, ApiProblemDetails.InternalError(
                "An unexpected error occurred", HttpContext.Request.Path));
        }
    }

    /// <summary>
    /// Updates feedback status.
    /// Requires scope: feedback:write
    /// </summary>
    [HttpPatch("{id}/status")]
    [ProducesResponseType(typeof(FeedbackDto), 200)]
    [ProducesResponseType(typeof(ApiProblemDetails), 400)]
    [ProducesResponseType(typeof(ApiProblemDetails), 401)]
    [ProducesResponseType(typeof(ApiProblemDetails), 403)]
    [ProducesResponseType(typeof(ApiProblemDetails), 404)]
    [ProducesResponseType(typeof(ApiProblemDetails), 429)]
    public async Task<IActionResult> UpdateFeedbackStatus(int id, [FromBody] UpdateFeedbackStatusDto request)
    {
        try
        {
            // Check feature flag
            if (!_configuration.GetValue<bool>("Features:Api:Feedback:UpdateStatusEnabled", false))
            {
                _logger.LogWarning("API endpoint not enabled. Endpoint={Endpoint}, Path={Path}",
                    nameof(UpdateFeedbackStatus), HttpContext.Request.Path);
                return NotFound(ApiProblemDetails.NotFound("This API endpoint is not enabled", HttpContext.Request.Path));
            }

            // Get CompanyId from claims
            var companyIdClaim = User.FindFirst("CompanyId")?.Value;
            if (companyIdClaim == null || !int.TryParse(companyIdClaim, out var companyId))
            {
                _logger.LogWarning("Unauthorized API access attempt. Endpoint={Endpoint}, Path={Path}, HasCompanyClaim={HasClaim}",
                    nameof(UpdateFeedbackStatus), HttpContext.Request.Path, companyIdClaim != null);
                return Unauthorized(ApiProblemDetails.Unauthorized("Invalid authentication", HttpContext.Request.Path));
            }

            // Get UserId from claims
            var userIdClaim = User.FindFirst("UserId")?.Value;
            if (userIdClaim == null || !int.TryParse(userIdClaim, out var updaterId))
            {
                _logger.LogWarning("UserId claim missing. Endpoint={Endpoint}, CompanyId={CompanyId}",
                    nameof(UpdateFeedbackStatus), companyId);
                return Unauthorized(ApiProblemDetails.Unauthorized("Invalid authentication", HttpContext.Request.Path));
            }

            var (feedback, error) = await _feedbackService.UpdateFeedbackStatusAsync(companyId, id, updaterId, request);

            if (error != null)
            {
                if (error.Contains("not found"))
                {
                    _logger.LogWarning("Feedback not found for update. Endpoint={Endpoint}, CompanyId={CompanyId}, FeedbackId={FeedbackId}",
                        nameof(UpdateFeedbackStatus), companyId, id);
                    return NotFound(ApiProblemDetails.NotFound(error, HttpContext.Request.Path));
                }
                _logger.LogWarning("Feedback status update validation error. Endpoint={Endpoint}, CompanyId={CompanyId}, FeedbackId={FeedbackId}, Error={Error}",
                    nameof(UpdateFeedbackStatus), companyId, id, error);
                return BadRequest(ApiProblemDetails.ValidationError(error, HttpContext.Request.Path));
            }

            _logger.LogInformation("Feedback status updated. Endpoint={Endpoint}, CompanyId={CompanyId}, FeedbackId={FeedbackId}",
                nameof(UpdateFeedbackStatus), companyId, id);

            return Ok(feedback);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Database error in {Endpoint}. CompanyId={CompanyId}, FeedbackId={FeedbackId}, Path={Path}",
                nameof(UpdateFeedbackStatus), User.FindFirst("CompanyId")?.Value, id, HttpContext.Request.Path);
            return StatusCode(500, ApiProblemDetails.InternalError(
                "An error occurred while processing your request", HttpContext.Request.Path));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in {Endpoint}. CompanyId={CompanyId}, FeedbackId={FeedbackId}, Path={Path}",
                nameof(UpdateFeedbackStatus), User.FindFirst("CompanyId")?.Value, id, HttpContext.Request.Path);
            return StatusCode(500, ApiProblemDetails.InternalError(
                "An unexpected error occurred", HttpContext.Request.Path));
        }
    }

    /// <summary>
    /// Deletes feedback.
    /// Requires scope: feedback:write
    /// </summary>
    [HttpDelete("{id}")]
    [ProducesResponseType(204)]
    [ProducesResponseType(typeof(ApiProblemDetails), 400)]
    [ProducesResponseType(typeof(ApiProblemDetails), 401)]
    [ProducesResponseType(typeof(ApiProblemDetails), 403)]
    [ProducesResponseType(typeof(ApiProblemDetails), 404)]
    [ProducesResponseType(typeof(ApiProblemDetails), 429)]
    public async Task<IActionResult> DeleteFeedback(int id)
    {
        try
        {
            // Check feature flag
            if (!_configuration.GetValue<bool>("Features:Api:Feedback:DeleteEnabled", false))
            {
                _logger.LogWarning("API endpoint not enabled. Endpoint={Endpoint}, Path={Path}",
                    nameof(DeleteFeedback), HttpContext.Request.Path);
                return NotFound(ApiProblemDetails.NotFound("This API endpoint is not enabled", HttpContext.Request.Path));
            }

            // Get CompanyId from claims
            var companyIdClaim = User.FindFirst("CompanyId")?.Value;
            if (companyIdClaim == null || !int.TryParse(companyIdClaim, out var companyId))
            {
                _logger.LogWarning("Unauthorized API access attempt. Endpoint={Endpoint}, Path={Path}, HasCompanyClaim={HasClaim}",
                    nameof(DeleteFeedback), HttpContext.Request.Path, companyIdClaim != null);
                return Unauthorized(ApiProblemDetails.Unauthorized("Invalid authentication", HttpContext.Request.Path));
            }

            var success = await _feedbackService.DeleteFeedbackAsync(companyId, id);

            if (!success)
            {
                _logger.LogWarning("Feedback deletion failed. Endpoint={Endpoint}, CompanyId={CompanyId}, FeedbackId={FeedbackId}",
                    nameof(DeleteFeedback), companyId, id);
                return NotFound(ApiProblemDetails.NotFound(
                    "Feedback not found", HttpContext.Request.Path));
            }

            _logger.LogInformation("Feedback deleted. Endpoint={Endpoint}, CompanyId={CompanyId}, FeedbackId={FeedbackId}",
                nameof(DeleteFeedback), companyId, id);

            return NoContent();
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Database error in {Endpoint}. CompanyId={CompanyId}, FeedbackId={FeedbackId}, Path={Path}",
                nameof(DeleteFeedback), User.FindFirst("CompanyId")?.Value, id, HttpContext.Request.Path);
            return StatusCode(500, ApiProblemDetails.InternalError(
                "An error occurred while processing your request", HttpContext.Request.Path));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in {Endpoint}. CompanyId={CompanyId}, FeedbackId={FeedbackId}, Path={Path}",
                nameof(DeleteFeedback), User.FindFirst("CompanyId")?.Value, id, HttpContext.Request.Path);
            return StatusCode(500, ApiProblemDetails.InternalError(
                "An unexpected error occurred", HttpContext.Request.Path));
        }
    }
}
