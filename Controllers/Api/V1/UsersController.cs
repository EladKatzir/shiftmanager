using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Models.Api;
using ShiftManager.Models.Api.Dto;
using ShiftManager.Services.Api;

namespace ShiftManager.Controllers.Api.V1;

/// <summary>
/// API controller for user management operations.
/// All endpoints require API key authentication via X-API-Key header.
/// </summary>
[ApiController]
[Route("api/v1/users")]
[Produces("application/json")]
public class UsersController : ControllerBase
{
    private readonly UserApiService _userService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<UsersController> _logger;

    public UsersController(
        UserApiService userService,
        IConfiguration configuration,
        ILogger<UsersController> logger)
    {
        _userService = userService;
        _configuration = configuration;
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
    [ProducesResponseType(typeof(ApiProblemDetails), 401)]
    [ProducesResponseType(typeof(ApiProblemDetails), 403)]
    [ProducesResponseType(typeof(ApiProblemDetails), 429)]
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
            if (!_configuration.GetValue<bool>("Features:Api:Users:ListEnabled", false))
            {
                _logger.LogWarning("API endpoint not enabled. Endpoint={Endpoint}, Path={Path}",
                    nameof(ListUsers), HttpContext.Request.Path);
                return NotFound(ApiProblemDetails.NotFound("This API endpoint is not enabled", HttpContext.Request.Path));
            }

            // Get CompanyId from claims (set by ApiAuthenticationMiddleware)
            var companyIdClaim = User.FindFirst("CompanyId")?.Value;
            if (companyIdClaim == null || !int.TryParse(companyIdClaim, out var companyId))
            {
                // ERR-003: Log authorization failure
                _logger.LogWarning("Unauthorized API access attempt. Endpoint={Endpoint}, Path={Path}, HasCompanyClaim={HasClaim}",
                    nameof(ListUsers), HttpContext.Request.Path, companyIdClaim != null);
                return Unauthorized(ApiProblemDetails.Unauthorized("Invalid authentication", HttpContext.Request.Path));
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
            _logger.LogError(ex, "Database error in {Endpoint}. CompanyId={CompanyId}, Path={Path}",
                nameof(ListUsers), User.FindFirst("CompanyId")?.Value, HttpContext.Request.Path);
            return StatusCode(500, ApiProblemDetails.InternalError(
                "An error occurred while processing your request", HttpContext.Request.Path));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in {Endpoint}. CompanyId={CompanyId}, Path={Path}",
                nameof(ListUsers), User.FindFirst("CompanyId")?.Value, HttpContext.Request.Path);
            return StatusCode(500, ApiProblemDetails.InternalError(
                "An unexpected error occurred", HttpContext.Request.Path));
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
    [ProducesResponseType(typeof(ApiProblemDetails), 401)]
    [ProducesResponseType(typeof(ApiProblemDetails), 403)]
    [ProducesResponseType(typeof(ApiProblemDetails), 404)]
    [ProducesResponseType(typeof(ApiProblemDetails), 429)]
    public async Task<IActionResult> GetUser(int id)
    {
        try
        {
            // Check feature flag
            if (!_configuration.GetValue<bool>("Features:Api:Users:GetEnabled", false))
            {
                _logger.LogWarning("API endpoint not enabled. Endpoint={Endpoint}, Path={Path}",
                    nameof(GetUser), HttpContext.Request.Path);
                return NotFound(ApiProblemDetails.NotFound("This API endpoint is not enabled", HttpContext.Request.Path));
            }

            // Get CompanyId from claims
            var companyIdClaim = User.FindFirst("CompanyId")?.Value;
            if (companyIdClaim == null || !int.TryParse(companyIdClaim, out var companyId))
            {
                // ERR-003: Log authorization failure
                _logger.LogWarning("Unauthorized API access attempt. Endpoint={Endpoint}, Path={Path}, UserId={UserId}, HasCompanyClaim={HasClaim}",
                    nameof(GetUser), HttpContext.Request.Path, id, companyIdClaim != null);
                return Unauthorized(ApiProblemDetails.Unauthorized("Invalid authentication", HttpContext.Request.Path));
            }

            // Call service
            var user = await _userService.GetUserAsync(companyId, id);

            if (user == null)
            {
                _logger.LogWarning("User not found. Endpoint={Endpoint}, CompanyId={CompanyId}, UserId={UserId}",
                    nameof(GetUser), companyId, id);
                return NotFound(ApiProblemDetails.NotFound($"User {id} not found", HttpContext.Request.Path));
            }

            return Ok(user);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Database error in {Endpoint}. CompanyId={CompanyId}, UserId={UserId}, Path={Path}",
                nameof(GetUser), User.FindFirst("CompanyId")?.Value, id, HttpContext.Request.Path);
            return StatusCode(500, ApiProblemDetails.InternalError(
                "An error occurred while processing your request", HttpContext.Request.Path));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in {Endpoint}. CompanyId={CompanyId}, UserId={UserId}, Path={Path}",
                nameof(GetUser), User.FindFirst("CompanyId")?.Value, id, HttpContext.Request.Path);
            return StatusCode(500, ApiProblemDetails.InternalError(
                "An unexpected error occurred", HttpContext.Request.Path));
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
    [ProducesResponseType(typeof(ApiProblemDetails), 400)]
    [ProducesResponseType(typeof(ApiProblemDetails), 401)]
    [ProducesResponseType(typeof(ApiProblemDetails), 403)]
    [ProducesResponseType(typeof(ApiProblemDetails), 409)]
    [ProducesResponseType(typeof(ApiProblemDetails), 429)]
    public async Task<IActionResult> CreateUser([FromBody] CreateUserRequest request)
    {
        try
        {
            // Check feature flag
            if (!_configuration.GetValue<bool>("Features:Api:Users:CreateEnabled", false))
            {
                _logger.LogWarning("API endpoint not enabled. Endpoint={Endpoint}, Path={Path}",
                    nameof(CreateUser), HttpContext.Request.Path);
                return NotFound(ApiProblemDetails.NotFound("This API endpoint is not enabled", HttpContext.Request.Path));
            }

            // Get CompanyId from claims
            var companyIdClaim = User.FindFirst("CompanyId")?.Value;
            if (companyIdClaim == null || !int.TryParse(companyIdClaim, out var companyId))
            {
                // ERR-003: Log authorization failure
                _logger.LogWarning("Unauthorized API access attempt. Endpoint={Endpoint}, Path={Path}, HasCompanyClaim={HasClaim}",
                    nameof(CreateUser), HttpContext.Request.Path, companyIdClaim != null);
                return Unauthorized(ApiProblemDetails.Unauthorized("Invalid authentication", HttpContext.Request.Path));
            }

            // Validate request
            if (string.IsNullOrEmpty(request.Email))
            {
                _logger.LogWarning("Validation error in {Endpoint}. CompanyId={CompanyId}, Error={Error}",
                    nameof(CreateUser), companyId, "Email is required");
                return BadRequest(ApiProblemDetails.ValidationError("Email is required", HttpContext.Request.Path));
            }

            if (string.IsNullOrEmpty(request.DisplayName))
            {
                _logger.LogWarning("Validation error in {Endpoint}. CompanyId={CompanyId}, Error={Error}",
                    nameof(CreateUser), companyId, "DisplayName is required");
                return BadRequest(ApiProblemDetails.ValidationError("DisplayName is required", HttpContext.Request.Path));
            }

            if (string.IsNullOrEmpty(request.Role))
            {
                _logger.LogWarning("Validation error in {Endpoint}. CompanyId={CompanyId}, Error={Error}",
                    nameof(CreateUser), companyId, "Role is required");
                return BadRequest(ApiProblemDetails.ValidationError("Role is required", HttpContext.Request.Path));
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
                    _logger.LogWarning("User creation conflict in {Endpoint}. CompanyId={CompanyId}, Error={Error}",
                        nameof(CreateUser), companyId, error);
                    return Conflict(ApiProblemDetails.Conflict(error, HttpContext.Request.Path));
                }
                _logger.LogWarning("User creation validation error in {Endpoint}. CompanyId={CompanyId}, Error={Error}",
                    nameof(CreateUser), companyId, error);
                return BadRequest(ApiProblemDetails.ValidationError(error, HttpContext.Request.Path));
            }

            _logger.LogInformation("User created successfully. Endpoint={Endpoint}, CompanyId={CompanyId}, UserId={UserId}",
                nameof(CreateUser), companyId, user!.Id);
            return CreatedAtAction(nameof(GetUser), new { id = user!.Id }, user);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Database error in {Endpoint}. CompanyId={CompanyId}, Path={Path}",
                nameof(CreateUser), User.FindFirst("CompanyId")?.Value, HttpContext.Request.Path);
            return StatusCode(500, ApiProblemDetails.InternalError(
                "An error occurred while processing your request", HttpContext.Request.Path));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in {Endpoint}. CompanyId={CompanyId}, Path={Path}",
                nameof(CreateUser), User.FindFirst("CompanyId")?.Value, HttpContext.Request.Path);
            return StatusCode(500, ApiProblemDetails.InternalError(
                "An unexpected error occurred", HttpContext.Request.Path));
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
    [ProducesResponseType(typeof(ApiProblemDetails), 400)]
    [ProducesResponseType(typeof(ApiProblemDetails), 401)]
    [ProducesResponseType(typeof(ApiProblemDetails), 403)]
    [ProducesResponseType(typeof(ApiProblemDetails), 404)]
    [ProducesResponseType(typeof(ApiProblemDetails), 429)]
    public async Task<IActionResult> UpdateUser(int id, [FromBody] UpdateUserRequest request)
    {
        try
        {
            // Check feature flag
            if (!_configuration.GetValue<bool>("Features:Api:Users:UpdateEnabled", false))
            {
                _logger.LogWarning("API endpoint not enabled. Endpoint={Endpoint}, Path={Path}",
                    nameof(UpdateUser), HttpContext.Request.Path);
                return NotFound(ApiProblemDetails.NotFound("This API endpoint is not enabled", HttpContext.Request.Path));
            }

            // Get CompanyId from claims
            var companyIdClaim = User.FindFirst("CompanyId")?.Value;
            if (companyIdClaim == null || !int.TryParse(companyIdClaim, out var companyId))
            {
                // ERR-003: Log authorization failure
                _logger.LogWarning("Unauthorized API access attempt. Endpoint={Endpoint}, Path={Path}, UserId={UserId}, HasCompanyClaim={HasClaim}",
                    nameof(UpdateUser), HttpContext.Request.Path, id, companyIdClaim != null);
                return Unauthorized(ApiProblemDetails.Unauthorized("Invalid authentication", HttpContext.Request.Path));
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
                    _logger.LogWarning("User not found for update. Endpoint={Endpoint}, CompanyId={CompanyId}, UserId={UserId}",
                        nameof(UpdateUser), companyId, id);
                    return NotFound(ApiProblemDetails.NotFound(error, HttpContext.Request.Path));
                }
                _logger.LogWarning("User update validation error. Endpoint={Endpoint}, CompanyId={CompanyId}, UserId={UserId}, Error={Error}",
                    nameof(UpdateUser), companyId, id, error);
                return BadRequest(ApiProblemDetails.ValidationError(error, HttpContext.Request.Path));
            }

            _logger.LogInformation("User updated successfully. Endpoint={Endpoint}, CompanyId={CompanyId}, UserId={UserId}",
                nameof(UpdateUser), companyId, id);
            return Ok(user);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Database error in {Endpoint}. CompanyId={CompanyId}, UserId={UserId}, Path={Path}",
                nameof(UpdateUser), User.FindFirst("CompanyId")?.Value, id, HttpContext.Request.Path);
            return StatusCode(500, ApiProblemDetails.InternalError(
                "An error occurred while processing your request", HttpContext.Request.Path));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in {Endpoint}. CompanyId={CompanyId}, UserId={UserId}, Path={Path}",
                nameof(UpdateUser), User.FindFirst("CompanyId")?.Value, id, HttpContext.Request.Path);
            return StatusCode(500, ApiProblemDetails.InternalError(
                "An unexpected error occurred", HttpContext.Request.Path));
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
