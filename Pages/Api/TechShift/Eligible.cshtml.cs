using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ShiftManager.Services;

namespace ShiftManager.Pages.Api.TechShift;

/// <summary>
/// API endpoint to get users eligible for a specific tech shift type.
/// Returns users with the corresponding grant (e.g., HANAVA -> CanBeAssignedHanava).
/// </summary>
[Authorize(Policy = "Grant:ManageShifts")]
[IgnoreAntiforgeryToken]
public class EligibleModel : PageModel
{
    private readonly ITechShiftService _techShiftService;
    private readonly ILogger<EligibleModel> _logger;

    public EligibleModel(
        ITechShiftService techShiftService,
        ILogger<EligibleModel> logger)
    {
        _techShiftService = techShiftService;
        _logger = logger;
    }

    public async Task<IActionResult> OnGetAsync(string? type, int? companyId)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(type))
            {
                return new JsonResult(new { success = false, message = "Missing required parameter: type" })
                {
                    StatusCode = 400
                };
            }

            var eligible = await _techShiftService.GetEligibleUsersForTechShiftAsync(type, companyId);

            // Project to DTO — NEVER return raw AppUser (exposes PasswordHash, etc.)
            var result = eligible.Select(u => new { u.Id, u.DisplayName }).ToList();

            return new JsonResult(new
            {
                success = true,
                techShiftType = type,
                companyId = companyId,
                users = result
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching eligible users for tech shift type {Type}, company {CompanyId}", type, companyId);
            return new JsonResult(new { success = false, message = "An error occurred" })
            {
                StatusCode = 500
            };
        }
    }
}
