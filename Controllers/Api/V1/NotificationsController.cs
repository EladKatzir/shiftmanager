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
/// API controller for notification management.
/// All endpoints require API key authentication via X-API-Key header.
/// </summary>
[Authorize]
[ApiController]
[Route("api/v1/notifications")]
[Produces("application/json")]
public class NotificationsController : ControllerBase
{
    private readonly NotificationApiService _notificationService;
    private readonly IFeatureFlagService _featureFlagService;
    private readonly ILogger<NotificationsController> _logger;

    public NotificationsController(
        NotificationApiService notificationService,
        IFeatureFlagService featureFlagService,
        ILogger<NotificationsController> logger)
    {
        _notificationService = notificationService;
        _featureFlagService = featureFlagService;
        _logger = logger;
    }

    /// <summary>
    /// Lists notifications with pagination and filtering.
    /// Requires scope: notification:read
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(PaginatedResponse<NotificationDto>), 200)]
    [ProducesResponseType(typeof(ApiErrorResponse), 401)]
    [ProducesResponseType(typeof(ApiErrorResponse), 403)]
    [ProducesResponseType(typeof(ApiErrorResponse), 429)]
    public async Task<IActionResult> ListNotifications(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] int? userId = null,
        [FromQuery] bool? isRead = null,
        [FromQuery] string? type = null)
    {
        try
        {
            // Check feature flag
            if (!await _featureFlagService.IsEnabledAsync(FeatureFlagSeed.Flags.ApiNotificationsList))
            {
                _logger.LogWarning("API endpoint not enabled. Endpoint={Endpoint}, Path={Path}",
                    nameof(ListNotifications), HttpContext.Request.Path);
                return NotFound(ApiErrorResponse.Create("ENDPOINT_DISABLED", "This API endpoint is not enabled"));
            }

            // Get CompanyId from claims
            var companyIdClaim = User.FindFirst("CompanyId")?.Value;
            if (companyIdClaim == null || !int.TryParse(companyIdClaim, out var companyId))
            {
                // ERR-003: Log authorization failure
                _logger.LogWarning("Unauthorized API access attempt. Endpoint={Endpoint}, Path={Path}, HasCompanyClaim={HasClaim}",
                    nameof(ListNotifications), HttpContext.Request.Path, companyIdClaim != null);
                return Unauthorized(ApiErrorResponse.Unauthorized("Invalid authentication"));
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
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Database error in {Endpoint}. CompanyId={CompanyId}, Path={Path}",
                nameof(ListNotifications), User.FindFirst("CompanyId")?.Value, HttpContext.Request.Path);
            return StatusCode(500, ApiErrorResponse.ServerError("An error occurred while processing your request"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in {Endpoint}. CompanyId={CompanyId}, Path={Path}",
                nameof(ListNotifications), User.FindFirst("CompanyId")?.Value, HttpContext.Request.Path);
            return StatusCode(500, ApiErrorResponse.ServerError("An unexpected error occurred"));
        }
    }

    /// <summary>
    /// Gets a single notification by ID.
    /// Requires scope: notification:read
    /// </summary>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(NotificationDto), 200)]
    [ProducesResponseType(typeof(ApiErrorResponse), 401)]
    [ProducesResponseType(typeof(ApiErrorResponse), 403)]
    [ProducesResponseType(typeof(ApiErrorResponse), 404)]
    [ProducesResponseType(typeof(ApiErrorResponse), 429)]
    public async Task<IActionResult> GetNotification(int id)
    {
        try
        {
            // Check feature flag
            if (!await _featureFlagService.IsEnabledAsync(FeatureFlagSeed.Flags.ApiNotificationsGet))
            {
                _logger.LogWarning("API endpoint not enabled. Endpoint={Endpoint}, Path={Path}",
                    nameof(GetNotification), HttpContext.Request.Path);
                return NotFound(ApiErrorResponse.Create("ENDPOINT_DISABLED", "This API endpoint is not enabled"));
            }

            // Get CompanyId from claims
            var companyIdClaim = User.FindFirst("CompanyId")?.Value;
            if (companyIdClaim == null || !int.TryParse(companyIdClaim, out var companyId))
            {
                // ERR-003: Log authorization failure
                _logger.LogWarning("Unauthorized API access attempt. Endpoint={Endpoint}, Path={Path}, HasCompanyClaim={HasClaim}",
                    nameof(GetNotification), HttpContext.Request.Path, companyIdClaim != null);
                return Unauthorized(ApiErrorResponse.Unauthorized("Invalid authentication"));
            }

            var notification = await _notificationService.GetNotificationAsync(companyId, id);

            if (notification == null)
            {
                _logger.LogWarning("Notification not found. Endpoint={Endpoint}, CompanyId={CompanyId}, NotificationId={NotificationId}",
                    nameof(GetNotification), companyId, id);
                return NotFound(ApiErrorResponse.NotFound("Notification", id.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            }

            return Ok(notification);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Database error in {Endpoint}. CompanyId={CompanyId}, NotificationId={NotificationId}, Path={Path}",
                nameof(GetNotification), User.FindFirst("CompanyId")?.Value, id, HttpContext.Request.Path);
            return StatusCode(500, ApiErrorResponse.ServerError("An error occurred while processing your request"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in {Endpoint}. CompanyId={CompanyId}, NotificationId={NotificationId}, Path={Path}",
                nameof(GetNotification), User.FindFirst("CompanyId")?.Value, id, HttpContext.Request.Path);
            return StatusCode(500, ApiErrorResponse.ServerError("An unexpected error occurred"));
        }
    }

    /// <summary>
    /// Marks a notification as read.
    /// Requires scope: notification:write
    /// </summary>
    [HttpPost("{id}/mark-read")]
    [ProducesResponseType(typeof(NotificationDto), 200)]
    [ProducesResponseType(typeof(ApiErrorResponse), 400)]
    [ProducesResponseType(typeof(ApiErrorResponse), 401)]
    [ProducesResponseType(typeof(ApiErrorResponse), 403)]
    [ProducesResponseType(typeof(ApiErrorResponse), 404)]
    [ProducesResponseType(typeof(ApiErrorResponse), 429)]
    public async Task<IActionResult> MarkAsRead(int id, [FromBody] MarkAsReadRequest? request = null)
    {
        try
        {
            // Check feature flag
            if (!await _featureFlagService.IsEnabledAsync(FeatureFlagSeed.Flags.ApiNotificationsMarkRead))
            {
                _logger.LogWarning("API endpoint not enabled. Endpoint={Endpoint}, Path={Path}",
                    nameof(MarkAsRead), HttpContext.Request.Path);
                return NotFound(ApiErrorResponse.Create("ENDPOINT_DISABLED", "This API endpoint is not enabled"));
            }

            // Get CompanyId from claims
            var companyIdClaim = User.FindFirst("CompanyId")?.Value;
            if (companyIdClaim == null || !int.TryParse(companyIdClaim, out var companyId))
            {
                // ERR-003: Log authorization failure
                _logger.LogWarning("Unauthorized API access attempt. Endpoint={Endpoint}, Path={Path}, HasCompanyClaim={HasClaim}",
                    nameof(MarkAsRead), HttpContext.Request.Path, companyIdClaim != null);
                return Unauthorized(ApiErrorResponse.Unauthorized("Invalid authentication"));
            }

            var (notification, error) = await _notificationService.MarkAsReadAsync(
                companyId, id, request?.UserId);

            if (error != null)
            {
                if (error.Contains("not found"))
                {
                    _logger.LogWarning("Notification not found for mark as read. Endpoint={Endpoint}, CompanyId={CompanyId}, NotificationId={NotificationId}",
                        nameof(MarkAsRead), companyId, id);
                    return NotFound(ApiErrorResponse.Create("NOT_FOUND", error));
                }
                _logger.LogWarning("Mark as read validation error. Endpoint={Endpoint}, CompanyId={CompanyId}, NotificationId={NotificationId}, Error={Error}",
                    nameof(MarkAsRead), companyId, id, error);
                return BadRequest(ApiErrorResponse.BadRequest(error));
            }

            _logger.LogInformation("Notification marked as read. Endpoint={Endpoint}, CompanyId={CompanyId}, NotificationId={NotificationId}",
                nameof(MarkAsRead), companyId, id);

            return Ok(notification);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Database error in {Endpoint}. CompanyId={CompanyId}, NotificationId={NotificationId}, Path={Path}",
                nameof(MarkAsRead), User.FindFirst("CompanyId")?.Value, id, HttpContext.Request.Path);
            return StatusCode(500, ApiErrorResponse.ServerError("An error occurred while processing your request"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in {Endpoint}. CompanyId={CompanyId}, NotificationId={NotificationId}, Path={Path}",
                nameof(MarkAsRead), User.FindFirst("CompanyId")?.Value, id, HttpContext.Request.Path);
            return StatusCode(500, ApiErrorResponse.ServerError("An unexpected error occurred"));
        }
    }

    /// <summary>
    /// Marks all notifications for a user as read.
    /// Requires scope: notification:write
    /// </summary>
    [HttpPost("mark-all-read")]
    [ProducesResponseType(typeof(MarkAllReadResponse), 200)]
    [ProducesResponseType(typeof(ApiErrorResponse), 400)]
    [ProducesResponseType(typeof(ApiErrorResponse), 401)]
    [ProducesResponseType(typeof(ApiErrorResponse), 403)]
    [ProducesResponseType(typeof(ApiErrorResponse), 429)]
    public async Task<IActionResult> MarkAllAsRead([FromBody] MarkAllAsReadRequest request)
    {
        try
        {
            // Check feature flag
            if (!await _featureFlagService.IsEnabledAsync(FeatureFlagSeed.Flags.ApiNotificationsMarkAllRead))
            {
                _logger.LogWarning("API endpoint not enabled. Endpoint={Endpoint}, Path={Path}",
                    nameof(MarkAllAsRead), HttpContext.Request.Path);
                return NotFound(ApiErrorResponse.Create("ENDPOINT_DISABLED", "This API endpoint is not enabled"));
            }

            // Get CompanyId from claims
            var companyIdClaim = User.FindFirst("CompanyId")?.Value;
            if (companyIdClaim == null || !int.TryParse(companyIdClaim, out var companyId))
            {
                // ERR-003: Log authorization failure
                _logger.LogWarning("Unauthorized API access attempt. Endpoint={Endpoint}, Path={Path}, HasCompanyClaim={HasClaim}",
                    nameof(MarkAllAsRead), HttpContext.Request.Path, companyIdClaim != null);
                return Unauthorized(ApiErrorResponse.Unauthorized("Invalid authentication"));
            }

            if (request.UserId <= 0)
            {
                _logger.LogWarning("Validation error: UserId required. Endpoint={Endpoint}, CompanyId={CompanyId}",
                    nameof(MarkAllAsRead), companyId);
                return BadRequest(ApiErrorResponse.BadRequest("UserId is required"));
            }

            var count = await _notificationService.MarkAllAsReadAsync(companyId, request.UserId);

            _logger.LogInformation("All notifications marked as read. Endpoint={Endpoint}, CompanyId={CompanyId}, UserId={UserId}, MarkedCount={MarkedCount}",
                nameof(MarkAllAsRead), companyId, request.UserId, count);

            return Ok(new MarkAllReadResponse { MarkedCount = count });
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Database error in {Endpoint}. CompanyId={CompanyId}, UserId={UserId}, Path={Path}",
                nameof(MarkAllAsRead), User.FindFirst("CompanyId")?.Value, request?.UserId, HttpContext.Request.Path);
            return StatusCode(500, ApiErrorResponse.ServerError("An error occurred while processing your request"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in {Endpoint}. CompanyId={CompanyId}, UserId={UserId}, Path={Path}",
                nameof(MarkAllAsRead), User.FindFirst("CompanyId")?.Value, request?.UserId, HttpContext.Request.Path);
            return StatusCode(500, ApiErrorResponse.ServerError("An unexpected error occurred"));
        }
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
