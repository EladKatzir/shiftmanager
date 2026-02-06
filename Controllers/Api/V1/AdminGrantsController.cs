using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShiftManager.Services;

namespace ShiftManager.Controllers.Api.V1;

/// <summary>
/// API controller for admin grant verification and management operations.
/// Allows checking and repairing user grants against expected role templates.
/// </summary>
[ApiController]
[Route("api/admin/verify-grants")]
[Produces("application/json")]
[Authorize(Policy = "IsOwner")] // Only owners can use this endpoint
public class AdminGrantsController : ControllerBase
{
    private readonly IGrantService _grantService;
    private readonly ILogger<AdminGrantsController> _logger;

    public AdminGrantsController(IGrantService grantService, ILogger<AdminGrantsController> logger)
    {
        _grantService = grantService;
        _logger = logger;
    }

    /// <summary>
    /// Verifies all users have expected grants based on their role templates.
    /// Returns a list of users with missing or extra grants.
    /// </summary>
    /// <returns>List of grant verification results</returns>
    [HttpGet]
    [ProducesResponseType(typeof(GrantVerificationResponse), 200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    public async Task<IActionResult> VerifyAllGrants()
    {
        try
        {
            _logger.LogInformation("Admin grant verification requested");

            var results = await _grantService.VerifyAllUserGrantsAsync();

            var response = new GrantVerificationResponse
            {
                TotalUsers = results.Count,
                CompliantUsers = results.Count(r => r.IsCompliant),
                NonCompliantUsers = results.Count(r => !r.IsCompliant),
                Details = results.Where(r => !r.IsCompliant).ToList()
            };

            _logger.LogInformation("Grant verification completed: {Total} users, {Compliant} compliant, {NonCompliant} non-compliant",
                response.TotalUsers, response.CompliantUsers, response.NonCompliantUsers);

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during grant verification");
            return StatusCode(500, new { error = "An error occurred during grant verification" });
        }
    }

    /// <summary>
    /// Verifies a specific user's grants against their role template.
    /// </summary>
    /// <param name="userId">User ID to verify</param>
    /// <returns>Grant verification result for the user</returns>
    [HttpGet("{userId}")]
    [ProducesResponseType(typeof(GrantVerificationResult), 200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> VerifyUserGrants(int userId)
    {
        try
        {
            var result = await _grantService.VerifyUserGrantsAsync(userId);

            if (result.UserDisplayName == "Not Found")
            {
                return NotFound(new { error = $"User {userId} not found" });
            }

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during grant verification for user {UserId}", userId);
            return StatusCode(500, new { error = "An error occurred during grant verification" });
        }
    }

    /// <summary>
    /// Repairs grants for all non-compliant users by adding missing grants.
    /// </summary>
    /// <returns>Repair summary</returns>
    [HttpPost("repair")]
    [ProducesResponseType(typeof(GrantRepairResponse), 200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    public async Task<IActionResult> RepairAllGrants()
    {
        try
        {
            var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            int? currentUserId = int.TryParse(userIdClaim, out var uid) ? uid : null;

            _logger.LogInformation("Admin grant repair requested by user {UserId}", currentUserId ?? 0);

            var grantsAdded = await _grantService.RepairAllUserGrantsAsync(currentUserId);

            var response = new GrantRepairResponse
            {
                Success = true,
                GrantsAdded = grantsAdded,
                Message = grantsAdded > 0
                    ? $"Successfully added {grantsAdded} missing grants"
                    : "All users already have correct grants"
            };

            _logger.LogInformation("Grant repair completed: {GrantsAdded} grants added", grantsAdded);

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during grant repair");
            return StatusCode(500, new { error = "An error occurred during grant repair" });
        }
    }

    /// <summary>
    /// Repairs grants for a specific user by adding missing grants.
    /// </summary>
    /// <param name="userId">User ID to repair</param>
    /// <returns>Repair summary</returns>
    [HttpPost("{userId}/repair")]
    [ProducesResponseType(typeof(GrantRepairResponse), 200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> RepairUserGrants(int userId)
    {
        try
        {
            var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            int? currentUserId = int.TryParse(userIdClaim, out var uid) ? uid : null;

            _logger.LogInformation("Grant repair for user {TargetUserId} requested by user {UserId}", userId, currentUserId ?? 0);

            var grantsAdded = await _grantService.RepairUserGrantsAsync(userId, currentUserId);

            var response = new GrantRepairResponse
            {
                Success = true,
                GrantsAdded = grantsAdded,
                Message = grantsAdded > 0
                    ? $"Successfully added {grantsAdded} missing grants to user {userId}"
                    : $"User {userId} already has correct grants"
            };

            _logger.LogInformation("Grant repair for user {UserId} completed: {GrantsAdded} grants added", userId, grantsAdded);

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during grant repair for user {UserId}", userId);
            return StatusCode(500, new { error = "An error occurred during grant repair" });
        }
    }
}

/// <summary>
/// Response model for grant verification endpoint.
/// </summary>
public class GrantVerificationResponse
{
    public int TotalUsers { get; set; }
    public int CompliantUsers { get; set; }
    public int NonCompliantUsers { get; set; }
    public List<GrantVerificationResult> Details { get; set; } = new();
}

/// <summary>
/// Response model for grant repair endpoint.
/// </summary>
public class GrantRepairResponse
{
    public bool Success { get; set; }
    public int GrantsAdded { get; set; }
    public string Message { get; set; } = string.Empty;
}
