using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using ShiftManager.Models.Support;
using ShiftManager.Resources;
using ShiftManager.Services;

namespace ShiftManager.Pages.Api.OnDuty;

/// <summary>
/// API endpoint to get eligible users for a specific on-duty type.
/// Returns users with their rank information for dropdown population.
/// </summary>
[Authorize(Policy = "Grant:ManageOnDuty")]
[IgnoreAntiforgeryToken]
public class GetEligibleUsersModel : PageModel
{
    private readonly IOnDutyService _onDutyService;
    private readonly ILogger<GetEligibleUsersModel> _logger;
    private readonly IStringLocalizer<SharedResources> _localizer;

    public GetEligibleUsersModel(
        IOnDutyService onDutyService,
        ILogger<GetEligibleUsersModel> logger,
        IStringLocalizer<SharedResources> localizer)
    {
        _onDutyService = onDutyService;
        _logger = logger;
        _localizer = localizer;
    }

    public async Task<IActionResult> OnGetAsync(int? dutyType)
    {
        try
        {
            // If no duty type specified, return all eligible users
            if (!dutyType.HasValue)
            {
                var allUsers = await _onDutyService.GetEligibleAssigneesAsync();
                return new JsonResult(new
                {
                    success = true,
                    requiresOfficer = false,
                    users = allUsers.Select(u => new
                    {
                        id = u.Id,
                        displayName = u.DisplayName,
                        email = u.Email,
                        rank = (int)u.Rank,
                        rankAbbreviation = u.Rank.GetAbbreviation(),
                        rankDisplayName = u.Rank.GetDisplayName(),
                        rankBadgeClass = u.Rank.GetBadgeClass(),
                        isOfficer = u.Rank.IsOfficer()
                    })
                });
            }

            // Validate duty type
            if (!Enum.IsDefined(typeof(OnDutyType), dutyType.Value))
            {
                return new JsonResult(new { success = false, message = "Invalid duty type" })
                {
                    StatusCode = 400
                };
            }

            var onDutyType = (OnDutyType)dutyType.Value;

            // Check if this duty type requires officer rank
            var requiresOfficer = await _onDutyService.RequiresOfficerForDutyTypeAsync(onDutyType);

            // Get eligible users (filtered by rank if required)
            var eligibleUsers = await _onDutyService.GetEligibleUsersForDutyAsync(onDutyType, requiresOfficer);

            return new JsonResult(new
            {
                success = true,
                requiresOfficer = requiresOfficer,
                dutyType = dutyType.Value,
                users = eligibleUsers.Select(u => new
                {
                    id = u.Id,
                    displayName = u.DisplayName,
                    email = u.Email,
                    rank = (int)u.Rank,
                    rankAbbreviation = u.Rank.GetAbbreviation(),
                    rankDisplayName = u.Rank.GetDisplayName(),
                    rankBadgeClass = u.Rank.GetBadgeClass(),
                    isOfficer = u.Rank.IsOfficer()
                })
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching eligible users for duty type {DutyType}", dutyType);
            return new JsonResult(
                ShiftManager.Models.ApiErrorResponse.Create(
                    "ERROR_ONDUTY_GET_ELIGIBLE_USERS_FAILED",
                    _localizer["Error_OnDutyApi_GetEligibleUsersFailed"].Value)
                .WithCorrelationId(HttpContext.TraceIdentifier))
            {
                StatusCode = 500
            };
        }
    }
}
