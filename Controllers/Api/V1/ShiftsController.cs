using Microsoft.AspNetCore.Mvc;
using ShiftManager.Models.Api;
using ShiftManager.Models.Api.Dto;
using ShiftManager.Services.Api;

namespace ShiftManager.Controllers.Api.V1;

/// <summary>
/// API controller for shift management operations.
/// All endpoints require API key authentication via X-API-Key header.
/// </summary>
[ApiController]
[Route("api/v1/shifts")]
[Produces("application/json")]
public class ShiftsController : ControllerBase
{
    private readonly ShiftApiService _shiftService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ShiftsController> _logger;

    public ShiftsController(
        ShiftApiService shiftService,
        IConfiguration configuration,
        ILogger<ShiftsController> logger)
    {
        _shiftService = shiftService;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Lists shift instances with pagination and filtering.
    /// Requires scope: shift:read
    /// </summary>
    /// <param name="page">Page number (default: 1)</param>
    /// <param name="pageSize">Page size (default: 50, max: 100)</param>
    /// <param name="startDate">Filter by start date (yyyy-MM-dd)</param>
    /// <param name="endDate">Filter by end date (yyyy-MM-dd)</param>
    /// <param name="shiftTypeId">Filter by shift type ID</param>
    /// <param name="userId">Filter by assigned user ID</param>
    /// <param name="hasOpenSlots">Filter shifts with open slots</param>
    /// <returns>Paginated list of shifts</returns>
    [HttpGet]
    [ProducesResponseType(typeof(PaginatedResponse<ShiftDto>), 200)]
    [ProducesResponseType(typeof(ApiProblemDetails), 401)]
    [ProducesResponseType(typeof(ApiProblemDetails), 403)]
    [ProducesResponseType(typeof(ApiProblemDetails), 429)]
    public async Task<IActionResult> ListShifts(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? startDate = null,
        [FromQuery] string? endDate = null,
        [FromQuery] int? shiftTypeId = null,
        [FromQuery] int? userId = null,
        [FromQuery] bool? hasOpenSlots = null)
    {
        // Check feature flag
        if (!_configuration.GetValue<bool>("Features:Api:Shifts:ListEnabled", false))
        {
            return NotFound(ApiProblemDetails.NotFound("This API endpoint is not enabled", HttpContext.Request.Path));
        }

        // Get CompanyId from claims (set by ApiAuthenticationMiddleware)
        var companyIdClaim = User.FindFirst("CompanyId")?.Value;
        if (companyIdClaim == null || !int.TryParse(companyIdClaim, out var companyId))
        {
            return Unauthorized(ApiProblemDetails.Unauthorized("Invalid authentication", HttpContext.Request.Path));
        }

        // Parse date filters
        DateOnly? startDateParsed = null;
        DateOnly? endDateParsed = null;

        if (!string.IsNullOrEmpty(startDate))
        {
            if (!DateOnly.TryParse(startDate, out var parsed))
            {
                return BadRequest(ApiProblemDetails.ValidationError(
                    "Invalid startDate format. Use yyyy-MM-dd",
                    HttpContext.Request.Path));
            }
            startDateParsed = parsed;
        }

        if (!string.IsNullOrEmpty(endDate))
        {
            if (!DateOnly.TryParse(endDate, out var parsed))
            {
                return BadRequest(ApiProblemDetails.ValidationError(
                    "Invalid endDate format. Use yyyy-MM-dd",
                    HttpContext.Request.Path));
            }
            endDateParsed = parsed;
        }

        // Call service
        var (shifts, totalCount) = await _shiftService.ListShiftsAsync(
            companyId, page, pageSize, startDateParsed, endDateParsed,
            shiftTypeId, userId, hasOpenSlots);

        // Build pagination info
        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
        var response = new PaginatedResponse<ShiftDto>
        {
            Data = shifts,
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
    /// Gets a single shift instance by ID with full details.
    /// Requires scope: shift:read
    /// </summary>
    /// <param name="id">Shift instance ID</param>
    /// <returns>Shift details with assignments</returns>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(ShiftDto), 200)]
    [ProducesResponseType(typeof(ApiProblemDetails), 401)]
    [ProducesResponseType(typeof(ApiProblemDetails), 403)]
    [ProducesResponseType(typeof(ApiProblemDetails), 404)]
    [ProducesResponseType(typeof(ApiProblemDetails), 429)]
    public async Task<IActionResult> GetShift(int id)
    {
        // Check feature flag
        if (!_configuration.GetValue<bool>("Features:Api:Shifts:GetEnabled", false))
        {
            return NotFound(ApiProblemDetails.NotFound("This API endpoint is not enabled", HttpContext.Request.Path));
        }

        // Get CompanyId from claims
        var companyIdClaim = User.FindFirst("CompanyId")?.Value;
        if (companyIdClaim == null || !int.TryParse(companyIdClaim, out var companyId))
        {
            return Unauthorized(ApiProblemDetails.Unauthorized("Invalid authentication", HttpContext.Request.Path));
        }

        // Call service
        var shift = await _shiftService.GetShiftAsync(companyId, id);

        if (shift == null)
        {
            return NotFound(ApiProblemDetails.NotFound($"Shift {id} not found", HttpContext.Request.Path));
        }

        return Ok(shift);
    }
}
