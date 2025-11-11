using Microsoft.AspNetCore.Mvc;
using ShiftManager.Models.Api;
using ShiftManager.Models.Api.Dto;
using ShiftManager.Services.Api;

namespace ShiftManager.Controllers.Api.V1;

/// <summary>
/// API controller for notification management.
/// All endpoints require API key authentication via X-API-Key header.
/// </summary>
[ApiController]
[Route("api/v1/notifications")]
[Produces("application/json")]
public class NotificationsController : ControllerBase
{
    private readonly NotificationApiService _notificationService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<NotificationsController> _logger;

    public NotificationsController(
        NotificationApiService notificationService,
        IConfiguration configuration,
        ILogger<NotificationsController> logger)
    {
        _notificationService = notificationService;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Lists notifications with pagination and filtering.
    /// Requires scope: notification:read
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(PaginatedResponse<NotificationDto>), 200)]
    [ProducesResponseType(typeof(ApiProblemDetails), 401)]
    [ProducesResponseType(typeof(ApiProblemDetails), 403)]
    [ProducesResponseType(typeof(ApiProblemDetails), 429)]
    public async Task<IActionResult> ListNotifications(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] int? userId = null,
        [FromQuery] bool? isRead = null,
        [FromQuery] string? type = null)
    {
        if (!_configuration.GetValue<bool>("Features:Api:Notifications:ListEnabled", false))
        {
            return NotFound(ApiProblemDetails.NotFound("This API endpoint is not enabled", HttpContext.Request.Path));
        }

        var companyIdClaim = User.FindFirst("CompanyId")?.Value;
        if (companyIdClaim == null || !int.TryParse(companyIdClaim, out var companyId))
        {
            return Unauthorized(ApiProblemDetails.Unauthorized("Invalid authentication", HttpContext.Request.Path));
        }

        var (notifications, totalCount) = await _notificationService.ListNotificationsAsync(
            companyId, page, pageSize, userId, isRead, type);

        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
        var response = new PaginatedResponse<NotificationDto>
        {
            Data = notifications,
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
    /// Gets a single notification by ID.
    /// Requires scope: notification:read
    /// </summary>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(NotificationDto), 200)]
    [ProducesResponseType(typeof(ApiProblemDetails), 401)]
    [ProducesResponseType(typeof(ApiProblemDetails), 403)]
    [ProducesResponseType(typeof(ApiProblemDetails), 404)]
    [ProducesResponseType(typeof(ApiProblemDetails), 429)]
    public async Task<IActionResult> GetNotification(int id)
    {
        if (!_configuration.GetValue<bool>("Features:Api:Notifications:GetEnabled", false))
        {
            return NotFound(ApiProblemDetails.NotFound("This API endpoint is not enabled", HttpContext.Request.Path));
        }

        var companyIdClaim = User.FindFirst("CompanyId")?.Value;
        if (companyIdClaim == null || !int.TryParse(companyIdClaim, out var companyId))
        {
            return Unauthorized(ApiProblemDetails.Unauthorized("Invalid authentication", HttpContext.Request.Path));
        }

        var notification = await _notificationService.GetNotificationAsync(companyId, id);

        if (notification == null)
        {
            return NotFound(ApiProblemDetails.NotFound($"Notification {id} not found", HttpContext.Request.Path));
        }

        return Ok(notification);
    }

    /// <summary>
    /// Marks a notification as read.
    /// Requires scope: notification:write
    /// </summary>
    [HttpPost("{id}/mark-read")]
    [ProducesResponseType(typeof(NotificationDto), 200)]
    [ProducesResponseType(typeof(ApiProblemDetails), 400)]
    [ProducesResponseType(typeof(ApiProblemDetails), 401)]
    [ProducesResponseType(typeof(ApiProblemDetails), 403)]
    [ProducesResponseType(typeof(ApiProblemDetails), 404)]
    [ProducesResponseType(typeof(ApiProblemDetails), 429)]
    public async Task<IActionResult> MarkAsRead(int id, [FromBody] MarkAsReadRequest? request = null)
    {
        if (!_configuration.GetValue<bool>("Features:Api:Notifications:MarkReadEnabled", false))
        {
            return NotFound(ApiProblemDetails.NotFound("This API endpoint is not enabled", HttpContext.Request.Path));
        }

        var companyIdClaim = User.FindFirst("CompanyId")?.Value;
        if (companyIdClaim == null || !int.TryParse(companyIdClaim, out var companyId))
        {
            return Unauthorized(ApiProblemDetails.Unauthorized("Invalid authentication", HttpContext.Request.Path));
        }

        var (notification, error) = await _notificationService.MarkAsReadAsync(
            companyId, id, request?.UserId);

        if (error != null)
        {
            if (error.Contains("not found"))
            {
                return NotFound(ApiProblemDetails.NotFound(error, HttpContext.Request.Path));
            }
            return BadRequest(ApiProblemDetails.ValidationError(error, HttpContext.Request.Path));
        }

        return Ok(notification);
    }

    /// <summary>
    /// Marks all notifications for a user as read.
    /// Requires scope: notification:write
    /// </summary>
    [HttpPost("mark-all-read")]
    [ProducesResponseType(typeof(MarkAllReadResponse), 200)]
    [ProducesResponseType(typeof(ApiProblemDetails), 400)]
    [ProducesResponseType(typeof(ApiProblemDetails), 401)]
    [ProducesResponseType(typeof(ApiProblemDetails), 403)]
    [ProducesResponseType(typeof(ApiProblemDetails), 429)]
    public async Task<IActionResult> MarkAllAsRead([FromBody] MarkAllAsReadRequest request)
    {
        if (!_configuration.GetValue<bool>("Features:Api:Notifications:MarkAllReadEnabled", false))
        {
            return NotFound(ApiProblemDetails.NotFound("This API endpoint is not enabled", HttpContext.Request.Path));
        }

        var companyIdClaim = User.FindFirst("CompanyId")?.Value;
        if (companyIdClaim == null || !int.TryParse(companyIdClaim, out var companyId))
        {
            return Unauthorized(ApiProblemDetails.Unauthorized("Invalid authentication", HttpContext.Request.Path));
        }

        if (request.UserId <= 0)
        {
            return BadRequest(ApiProblemDetails.ValidationError("UserId is required", HttpContext.Request.Path));
        }

        var count = await _notificationService.MarkAllAsReadAsync(companyId, request.UserId);

        return Ok(new MarkAllReadResponse { MarkedCount = count });
    }
}

/// <summary>
/// Optional request model for mark-read endpoint
/// </summary>
public class MarkAsReadRequest
{
    public int? UserId { get; set; }
}

/// <summary>
/// Request model for mark-all-read endpoint
/// </summary>
public class MarkAllAsReadRequest
{
    public int UserId { get; set; }
}

/// <summary>
/// Response model for mark-all-read endpoint
/// </summary>
public class MarkAllReadResponse
{
    public int MarkedCount { get; set; }
}
