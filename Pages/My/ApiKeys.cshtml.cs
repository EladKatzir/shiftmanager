using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ShiftManager.Models.Api;
using ShiftManager.Services;
using System.Security.Claims;

namespace ShiftManager.Pages.My;

[Authorize]
public class ApiKeysModel : PageModel
{
    private readonly IApiKeyService _apiKeyService;
    private readonly ILogger<ApiKeysModel> _logger;

    public ApiKeysModel(IApiKeyService apiKeyService, ILogger<ApiKeysModel> logger)
    {
        _apiKeyService = apiKeyService;
        _logger = logger;
    }

    // User data
    public List<ApiKey> ActiveKeys { get; set; } = new();
    public List<ApiKeyRequest> PendingRequests { get; set; } = new();
    public List<ApiKeyRequest> AllRequests { get; set; } = new();

    // Admin data (only populated for Owner/Manager/Director)
    public List<ApiKeyRequest> AllPendingRequests { get; set; } = new();
    public List<ApiKey> AllKeys { get; set; } = new();
    public List<ApiKeyRequest> AllCompanyRequests { get; set; } = new();

    public bool IsAdmin => User.IsInRole("Owner") || User.IsInRole("Manager") || User.IsInRole("Director");
    public bool IsOwner => User.IsInRole("Owner");

    [TempData]
    public string? Message { get; set; }

    [TempData]
    public string? Error { get; set; }

    [TempData]
    public string? GeneratedApiKey { get; set; }

    public async Task OnGetAsync()
    {
        var companyId = int.Parse(User.FindFirstValue("CompanyId")!);
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        // Load user's own keys and requests
        ActiveKeys = await _apiKeyService.ListUserKeysAsync(companyId, userId);
        var allUserRequests = await _apiKeyService.ListUserRequestsAsync(companyId, userId);

        PendingRequests = allUserRequests.Where(r => r.Status == ApiKeyRequestStatus.Pending).ToList();
        AllRequests = allUserRequests;

        // Load admin data if user is admin
        if (IsAdmin)
        {
            AllPendingRequests = await _apiKeyService.ListPendingRequestsAsync(companyId);
            AllKeys = await _apiKeyService.ListAllKeysAsync(companyId, includeInactive: true);
            AllCompanyRequests = await _apiKeyService.ListAllRequestsAsync(companyId);
        }
    }

    public async Task<IActionResult> OnPostRequestAsync(string name, string description, string[] scopes)
    {
        var companyId = int.Parse(User.FindFirstValue("CompanyId")!);
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        // Validate scopes
        if (scopes == null || scopes.Length == 0)
        {
            Error = "Please select at least one scope";
            return RedirectToPage();
        }

        var requestedScopes = string.Join(",", scopes);

        var (request, error) = await _apiKeyService.RequestApiKeyAsync(
            companyId,
            userId,
            name,
            description,
            requestedScopes);

        if (error != null)
        {
            Error = error;
        }
        else
        {
            Message = "API key request submitted successfully. An administrator will review your request.";
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRevokeAsync(int keyId)
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        var (key, error) = await _apiKeyService.RevokeApiKeyAsync(keyId, userId, "Revoked by user");

        if (error != null)
        {
            Error = error;
        }
        else
        {
            Message = $"API key '{key!.Name}' has been revoked successfully";
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRefreshAsync(int keyId)
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        var (newApiKey, error) = await _apiKeyService.RegenerateApiKeyAsync(keyId, userId);

        if (error != null)
        {
            Error = error;
        }
        else
        {
            GeneratedApiKey = newApiKey;
            Message = "API key refreshed successfully. Save the new key - it won't be shown again!";
        }

        return RedirectToPage();
    }

    // Admin handlers
    public async Task<IActionResult> OnPostApproveAsync(
        int requestId,
        string? reviewNotes,
        string? approvedScopes,
        int rateLimitPerMinute = 100,
        int? expiresInDays = null)
    {
        if (!IsAdmin)
        {
            Error = "Unauthorized";
            return RedirectToPage();
        }

        var reviewerId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        DateTime? expiresAt = expiresInDays.HasValue
            ? DateTime.UtcNow.AddDays(expiresInDays.Value)
            : null;

        var (apiKey, request, error) = await _apiKeyService.ApproveRequestAsync(
            requestId,
            reviewerId,
            reviewNotes,
            approvedScopes,
            rateLimitPerMinute,
            expiresAt);

        if (error != null)
        {
            Error = error;
        }
        else
        {
            GeneratedApiKey = apiKey;
            Message = $"API key request approved for '{request!.RequestedByUser?.DisplayName}'. Make sure to save the key - it won't be shown again!";
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRejectAsync(int requestId, string reviewNotes)
    {
        if (!IsAdmin)
        {
            Error = "Unauthorized";
            return RedirectToPage();
        }

        var reviewerId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        var (request, error) = await _apiKeyService.RejectRequestAsync(requestId, reviewerId, reviewNotes);

        if (error != null)
        {
            Error = error;
        }
        else
        {
            Message = $"API key request rejected for '{request!.RequestedByUser?.DisplayName}'";
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRevokeAdminAsync(int keyId, string? reason)
    {
        if (!IsAdmin)
        {
            Error = "Unauthorized";
            return RedirectToPage();
        }

        var reviewerId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        var (key, error) = await _apiKeyService.RevokeApiKeyAsync(keyId, reviewerId, reason ?? "Revoked by administrator");

        if (error != null)
        {
            Error = error;
        }
        else
        {
            Message = $"API key '{key!.Name}' has been revoked";
        }

        return RedirectToPage();
    }
}
