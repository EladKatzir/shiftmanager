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
/// API controller for user management operations.
/// All endpoints require API key authentication via X-API-Key header.
/// </summary>
[Authorize]
[ApiController]
[Route("api/v1/users")]
[Produces("application/json")]
public partial class UsersController : ControllerBase
{
    private readonly UserApiService _userService;
    private readonly IFeatureFlagService _featureFlagService;
    private readonly ILogger<UsersController> _logger;

    public UsersController(
        UserApiService userService,
        IFeatureFlagService featureFlagService,
        ILogger<UsersController> logger)
    {
        _userService = userService;
        _featureFlagService = featureFlagService;
        _logger = logger;
    }

    /// <summary>
    /// Lists users with pagination and filtering.
    /// Requires scope: user:read
    /// </summary>
    /// <param name="page">Page number (default: 1)</param>
    /// <param name="pageSize">Page size (default: 50, max: 100)</param>
    /// <param name="role">Filter by role (Owner, Manager, Employee, Director, Trainee)</param>
    /// <param name="isActive">Filter by active status</param>
    /// <param name="search">Search by email or display name</param>
    /// <returns>Paginated list of users</returns>
    [HttpGet]
    [ProducesResponseType(typeof(PaginatedResponse<UserDto>), 200)]
    [ProducesResponseType(typeof(ApiErrorResponse), 401)]
    [ProducesResponseType(typeof(ApiErrorResponse), 403)]
    [ProducesResponseType(typeof(ApiErrorResponse), 429)]
    public async Task<IActionResult> ListUsers(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? role = null,
        [FromQuery] bool? isActive = null,
        [FromQuery] string? search = null)
    {
        try
        {
            // Check feature flag
            if (!await _featureFlagService.IsEnabledAsync(FeatureFlagSeed.Flags.ApiUsersList))
            {
                LogApiEndpointNotEnabled(_logger, nameof(ListUsers), HttpContext.Request.Path);
                return NotFound(ApiErrorResponse.Create("ENDPOINT_DISABLED", "This API endpoint is not enabled"));
            }

            // Get CompanyId from claims (set by ApiAuthenticationMiddleware)
            var companyIdClaim = User.FindFirst("CompanyId")?.Value;
            if (companyIdClaim == null || !int.TryParse(companyIdClaim, out var companyId))
            {
                // ERR-003: Log authorization failure
                LogApiUnauthorized(_logger, nameof(ListUsers), HttpContext.Request.Path, companyIdClaim != null);
                return Unauthorized(ApiErrorResponse.Unauthorized("Invalid authentication"));
            }

            // Call service
            var (users, totalCount) = await _userService.ListUsersAsync(
                companyId, page, pageSize, role, isActive, search);

            // Build pagination info
            var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
            var response = new PaginatedResponse<UserDto>
            {
                Data = users,
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
            LogDbErrorCompanyPath(_logger, ex, nameof(ListUsers), User.FindFirst("CompanyId")?.Value, HttpContext.Request.Path);
            return StatusCode(500, ApiErrorResponse.ServerError("An error occurred while processing your request"));
        }
        catch (Exception ex)
        {
            LogUnexpectedErrorCompanyPath(_logger, ex, nameof(ListUsers), User.FindFirst("CompanyId")?.Value, HttpContext.Request.Path);
            return StatusCode(500, ApiErrorResponse.ServerError("An unexpected error occurred"));
        }
    }

    /// <summary>
    /// Gets a single user by ID with full details.
    /// Requires scope: user:read
    /// </summary>
    /// <param name="id">User ID</param>
    /// <returns>User details</returns>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(UserDto), 200)]
    [ProducesResponseType(typeof(ApiErrorResponse), 401)]
    [ProducesResponseType(typeof(ApiErrorResponse), 403)]
    [ProducesResponseType(typeof(ApiErrorResponse), 404)]
    [ProducesResponseType(typeof(ApiErrorResponse), 429)]
    public async Task<IActionResult> GetUser(int id)
    {
        try
        {
            // Check feature flag
            if (!await _featureFlagService.IsEnabledAsync(FeatureFlagSeed.Flags.ApiUsersGet))
            {
                LogApiEndpointNotEnabled(_logger, nameof(GetUser), HttpContext.Request.Path);
                return NotFound(ApiErrorResponse.Create("ENDPOINT_DISABLED", "This API endpoint is not enabled"));
            }

            // Get CompanyId from claims
            var companyIdClaim = User.FindFirst("CompanyId")?.Value;
            if (companyIdClaim == null || !int.TryParse(companyIdClaim, out var companyId))
            {
                // ERR-003: Log authorization failure
                LogApiUnauthorizedWithUserId(_logger, nameof(GetUser), HttpContext.Request.Path, id, companyIdClaim != null);
                return Unauthorized(ApiErrorResponse.Unauthorized("Invalid authentication"));
            }

            // Call service
            var user = await _userService.GetUserAsync(companyId, id);

            if (user == null)
            {
                LogUserNotFound(_logger, nameof(GetUser), companyId, id);
                return NotFound(ApiErrorResponse.NotFound("User", id.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            }

            return Ok(user);
        }
        catch (DbUpdateException ex)
        {
            LogDbErrorWithUserId(_logger, ex, nameof(GetUser), User.FindFirst("CompanyId")?.Value, id, HttpContext.Request.Path);
            return StatusCode(500, ApiErrorResponse.ServerError("An error occurred while processing your request"));
        }
        catch (Exception ex)
        {
            LogUnexpectedErrorWithUserId(_logger, ex, nameof(GetUser), User.FindFirst("CompanyId")?.Value, id, HttpContext.Request.Path);
            return StatusCode(500, ApiErrorResponse.ServerError("An unexpected error occurred"));
        }
    }

    /// <summary>
    /// Creates a new user.
    /// Requires scope: user:write
    /// </summary>
    /// <param name="request">User creation request</param>
    /// <returns>Created user</returns>
    [HttpPost]
    [ProducesResponseType(typeof(UserDto), 201)]
    [ProducesResponseType(typeof(ApiErrorResponse), 400)]
    [ProducesResponseType(typeof(ApiErrorResponse), 401)]
    [ProducesResponseType(typeof(ApiErrorResponse), 403)]
    [ProducesResponseType(typeof(ApiErrorResponse), 409)]
    [ProducesResponseType(typeof(ApiErrorResponse), 429)]
    public async Task<IActionResult> CreateUser([FromBody] CreateUserRequest request)
    {
        try
        {
            // Check feature flag
            if (!await _featureFlagService.IsEnabledAsync(FeatureFlagSeed.Flags.ApiUsersCreate))
            {
                LogApiEndpointNotEnabled(_logger, nameof(CreateUser), HttpContext.Request.Path);
                return NotFound(ApiErrorResponse.Create("ENDPOINT_DISABLED", "This API endpoint is not enabled"));
            }

            // Get CompanyId from claims
            var companyIdClaim = User.FindFirst("CompanyId")?.Value;
            if (companyIdClaim == null || !int.TryParse(companyIdClaim, out var companyId))
            {
                // ERR-003: Log authorization failure
                LogApiUnauthorized(_logger, nameof(CreateUser), HttpContext.Request.Path, companyIdClaim != null);
                return Unauthorized(ApiErrorResponse.Unauthorized("Invalid authentication"));
            }

            // Validate request
            if (string.IsNullOrEmpty(request.Email))
            {
                LogValidationError(_logger, nameof(CreateUser), companyId, "Email is required");
                return BadRequest(ApiErrorResponse.BadRequest("Email is required"));
            }

            if (string.IsNullOrEmpty(request.DisplayName))
            {
                LogValidationError(_logger, nameof(CreateUser), companyId, "DisplayName is required");
                return BadRequest(ApiErrorResponse.BadRequest("DisplayName is required"));
            }

            if (string.IsNullOrEmpty(request.Role))
            {
                LogValidationError(_logger, nameof(CreateUser), companyId, "Role is required");
                return BadRequest(ApiErrorResponse.BadRequest("Role is required"));
            }

            // Call service
            var (user, error) = await _userService.CreateUserAsync(
                companyId,
                request.Email,
                request.DisplayName,
                request.Role,
                request.Password,
                request.Department,
                request.JobTitle);

            if (error != null)
            {
                // Check if it's a conflict (duplicate email)
                if (error.Contains("already exists"))
                {
                    LogUserCreationConflict(_logger, nameof(CreateUser), companyId, error);
                    return Conflict(ApiErrorResponse.Conflict(error));
                }
                LogValidationCreateError(_logger, nameof(CreateUser), companyId, error);
                return BadRequest(ApiErrorResponse.BadRequest(error));
            }

            LogUserCreated(_logger, nameof(CreateUser), companyId, user!.Id);
            return CreatedAtAction(nameof(GetUser), new { id = user!.Id }, user);
        }
        catch (DbUpdateException ex)
        {
            LogDbErrorCompanyPath(_logger, ex, nameof(CreateUser), User.FindFirst("CompanyId")?.Value, HttpContext.Request.Path);
            return StatusCode(500, ApiErrorResponse.ServerError("An error occurred while processing your request"));
        }
        catch (Exception ex)
        {
            LogUnexpectedErrorCompanyPath(_logger, ex, nameof(CreateUser), User.FindFirst("CompanyId")?.Value, HttpContext.Request.Path);
            return StatusCode(500, ApiErrorResponse.ServerError("An unexpected error occurred"));
        }
    }

    /// <summary>
    /// Updates an existing user (partial update).
    /// Requires scope: user:write
    /// </summary>
    /// <param name="id">User ID</param>
    /// <param name="request">User update request</param>
    /// <returns>Updated user</returns>
    [HttpPatch("{id}")]
    [ProducesResponseType(typeof(UserDto), 200)]
    [ProducesResponseType(typeof(ApiErrorResponse), 400)]
    [ProducesResponseType(typeof(ApiErrorResponse), 401)]
    [ProducesResponseType(typeof(ApiErrorResponse), 403)]
    [ProducesResponseType(typeof(ApiErrorResponse), 404)]
    [ProducesResponseType(typeof(ApiErrorResponse), 429)]
    public async Task<IActionResult> UpdateUser(int id, [FromBody] UpdateUserRequest request)
    {
        try
        {
            // Check feature flag
            if (!await _featureFlagService.IsEnabledAsync(FeatureFlagSeed.Flags.ApiUsersUpdate))
            {
                LogApiEndpointNotEnabled(_logger, nameof(UpdateUser), HttpContext.Request.Path);
                return NotFound(ApiErrorResponse.Create("ENDPOINT_DISABLED", "This API endpoint is not enabled"));
            }

            // Get CompanyId from claims
            var companyIdClaim = User.FindFirst("CompanyId")?.Value;
            if (companyIdClaim == null || !int.TryParse(companyIdClaim, out var companyId))
            {
                // ERR-003: Log authorization failure
                LogApiUnauthorizedWithUserId(_logger, nameof(UpdateUser), HttpContext.Request.Path, id, companyIdClaim != null);
                return Unauthorized(ApiErrorResponse.Unauthorized("Invalid authentication"));
            }

            // Call service
            var (user, error) = await _userService.UpdateUserAsync(
                companyId,
                id,
                request.DisplayName,
                request.Role,
                request.IsActive,
                request.Department,
                request.JobTitle,
                request.Phone);

            if (error != null)
            {
                if (error.Contains("not found"))
                {
                    LogUserNotFoundForUpdate(_logger, nameof(UpdateUser), companyId, id);
                    return NotFound(ApiErrorResponse.Create("NOT_FOUND", error));
                }
                LogValidationUpdateError(_logger, nameof(UpdateUser), companyId, id, error);
                return BadRequest(ApiErrorResponse.BadRequest(error));
            }

            LogUserUpdated(_logger, nameof(UpdateUser), companyId, id);
            return Ok(user);
        }
        catch (DbUpdateException ex)
        {
            LogDbErrorWithUserId(_logger, ex, nameof(UpdateUser), User.FindFirst("CompanyId")?.Value, id, HttpContext.Request.Path);
            return StatusCode(500, ApiErrorResponse.ServerError("An error occurred while processing your request"));
        }
        catch (Exception ex)
        {
            LogUnexpectedErrorWithUserId(_logger, ex, nameof(UpdateUser), User.FindFirst("CompanyId")?.Value, id, HttpContext.Request.Path);
            return StatusCode(500, ApiErrorResponse.ServerError("An unexpected error occurred"));
        }
    }
}

/// <summary>
/// Request model for creating a new user
/// </summary>
public class CreateUserRequest
{
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string? Password { get; set; }
    public string? Department { get; set; }
    public string? JobTitle { get; set; }
}

/// <summary>
/// Request model for updating a user (all fields optional for partial update)
/// </summary>
public class UpdateUserRequest
{
    public string? DisplayName { get; set; }
    public string? Role { get; set; }
    public bool? IsActive { get; set; }
    public string? Department { get; set; }
    public string? JobTitle { get; set; }
    public string? Phone { get; set; }
}
