using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using ShiftManager.Resources;
using ShiftManager.Services;
using System.Security.Claims;

namespace ShiftManager.Pages.Friends;

[Authorize]
public class IndexModel : PageModel
{
    private readonly IFriendshipService _friendshipService;
    private readonly IStringLocalizer<SharedResources> _localizer;
    private readonly ILogger<IndexModel> _logger;

    public IndexModel(
        IFriendshipService friendshipService,
        IStringLocalizer<SharedResources> localizer,
        ILogger<IndexModel> logger)
    {
        _friendshipService = friendshipService;
        _localizer = localizer;
        _logger = logger;
    }

    public List<FriendDto> Friends { get; set; } = new();
    public List<FriendRequestDto> PendingRequests { get; set; } = new();
    public List<FriendRequestDto> OutgoingRequests { get; set; } = new();
    public List<PotentialFriendDto> SearchResults { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? SearchQuery { get; set; }

    public string? Success { get; set; }
    public string? Error { get; set; }

    public int CurrentUserId { get; private set; }

    public async Task OnGetAsync()
    {
        if (TempData["SuccessMessage"] is string successMsg) Success = successMsg;
        if (TempData["ErrorMessage"] is string errorMsg) Error = errorMsg;

        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out var userId))
        {
            Error = _localizer["Error_NotAuthenticated"];
            return;
        }
        CurrentUserId = userId;

        Friends = await _friendshipService.GetFriendsAsync(userId);
        PendingRequests = await _friendshipService.GetPendingRequestsAsync(userId);
        OutgoingRequests = await _friendshipService.GetOutgoingRequestsAsync(userId);

        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            SearchResults = await _friendshipService.SearchUsersAsync(userId, SearchQuery);
        }
    }

    public async Task<IActionResult> OnPostSendRequestAsync(int friendId)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out var userId))
        {
            TempData["ErrorMessage"] = _localizer["Error_NotAuthenticated"];
            return RedirectToPage();
        }

        var result = await _friendshipService.SendRequestAsync(userId, friendId);

        if (result.Success)
        {
            TempData["SuccessMessage"] = _localizer["Success_RequestSent"];
        }
        else
        {
            TempData["ErrorMessage"] = result.ErrorMessage ?? _localizer["Error_Unknown"];
        }

        return RedirectToPage(new { SearchQuery });
    }

    public async Task<IActionResult> OnPostAcceptAsync(int id)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out var userId))
        {
            TempData["ErrorMessage"] = _localizer["Error_NotAuthenticated"];
            return RedirectToPage();
        }

        var result = await _friendshipService.AcceptRequestAsync(id, userId);

        if (result.Success)
        {
            TempData["SuccessMessage"] = _localizer["Success_RequestAccepted"];
        }
        else
        {
            TempData["ErrorMessage"] = result.ErrorMessage ?? _localizer["Error_Unknown"];
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRejectAsync(int id)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out var userId))
        {
            TempData["ErrorMessage"] = _localizer["Error_NotAuthenticated"];
            return RedirectToPage();
        }

        var result = await _friendshipService.RejectRequestAsync(id, userId);

        if (result.Success)
        {
            TempData["SuccessMessage"] = _localizer["Success_RequestRejected"];
        }
        else
        {
            TempData["ErrorMessage"] = result.ErrorMessage ?? _localizer["Error_Unknown"];
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRemoveAsync(int id)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out var userId))
        {
            TempData["ErrorMessage"] = _localizer["Error_NotAuthenticated"];
            return RedirectToPage();
        }

        var result = await _friendshipService.RemoveFriendAsync(id, userId);

        if (result.Success)
        {
            TempData["SuccessMessage"] = _localizer["Success_FriendRemoved"];
        }
        else
        {
            TempData["ErrorMessage"] = result.ErrorMessage ?? _localizer["Error_Unknown"];
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostCancelRequestAsync(int id)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out var userId))
        {
            TempData["ErrorMessage"] = _localizer["Error_NotAuthenticated"];
            return RedirectToPage();
        }

        // Cancel is same as remove for the sender
        var result = await _friendshipService.RemoveFriendAsync(id, userId);

        if (result.Success)
        {
            TempData["SuccessMessage"] = _localizer["Success_RequestCanceled"];
        }
        else
        {
            TempData["ErrorMessage"] = result.ErrorMessage ?? _localizer["Error_Unknown"];
        }

        return RedirectToPage();
    }
}
