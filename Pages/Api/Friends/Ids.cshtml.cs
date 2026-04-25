using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using ShiftManager.Resources;
using ShiftManager.Services;

namespace ShiftManager.Pages.Api.Friends;

/// <summary>
/// API endpoint to get the current user's friend IDs.
/// Used by the calendar friend-highlighting toggle to identify friend assignments.
/// </summary>
[Authorize]
[IgnoreAntiforgeryToken]
public class IdsModel : PageModel
{
    private readonly IFriendshipService _friendshipService;
    private readonly ILogger<IdsModel> _logger;
    private readonly IStringLocalizer<SharedResources> _localizer;

    public IdsModel(
        IFriendshipService friendshipService,
        ILogger<IdsModel> logger,
        IStringLocalizer<SharedResources> localizer)
    {
        _friendshipService = friendshipService;
        _logger = logger;
        _localizer = localizer;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        try
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
            {
                return new JsonResult(new { success = false, message = "Invalid user" })
                {
                    StatusCode = 401
                };
            }

            var friendIds = await _friendshipService.GetFriendIdsAsync(userId);

            return new JsonResult(new
            {
                success = true,
                friendIds = friendIds
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching friend IDs");
            return new JsonResult(
                ShiftManager.Models.ApiErrorResponse.Create(
                    "ERROR_FRIENDS_GET_IDS_FAILED",
                    _localizer["Error_FriendsApi_GetIdsFailed"].Value)
                .WithCorrelationId(HttpContext.TraceIdentifier))
            {
                StatusCode = 500
            };
        }
    }
}
