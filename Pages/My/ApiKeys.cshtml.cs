using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using ShiftManager.Data.SeedData;
using ShiftManager.Models.Api;
using ShiftManager.Pages;
using ShiftManager.Resources;
using ShiftManager.Services;
using System.Security.Claims;

namespace ShiftManager.Pages.My;

[Authorize]
public class ApiKeysModel : LocalizedPageModel
{
    private readonly IApiKeyService _apiKeyService;
    private readonly IGrantService _grantService;
    private readonly IFeatureFlagService _featureFlagService;
    private readonly ILogger<ApiKeysModel> _logger;

    public ApiKeysModel(IApiKeyService apiKeyService, IGrantService grantService, IFeatureFlagService featureFlagService, ILogger<ApiKeysModel> logger, IStringLocalizer<SharedResources> localizer) : base(localizer)
    {
        _apiKeyService = apiKeyService;
        _grantService = grantService;
        _featureFlagService = featureFlagService;
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

    public bool IsAdmin { get; set; }
    public bool IsOwner { get; set; }

    [TempData]
    public string? Message { get; set; }

    [TempData]
    public new string? Error { get; set; }

    [TempData]
    public string? GeneratedApiKey { get; set; }

    private async Task<bool> CheckIsAdminAsync(int userId)
    {
        return await _grantService.HasGrantAsync(userId, "AccessAdminNavigation");
    }

    private async Task<bool> CheckIsOwnerAsync(int userId)
    {
        return await _grantService.HasGrantAsync(userId, "AdminAccess");
    }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!_featureFlagService.IsEnabled(FeatureFlagSeed.Flags.EnableApiKeyManagement))
            return RedirectToPage("/Home/Index");

        if (!int.TryParse(User.FindFirstValue("CompanyId"), out var companyId) ||
            !int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            Error = _localizer["Error_AuthenticationError"];
            return Page();
        }

        // Grant-based access checks
        IsAdmin = await CheckIsAdminAsync(userId);
        IsOwner = await CheckIsOwnerAsync(userId);

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

        return Page();
    }

    public async Task<IActionResult> OnPostRequestAsync(string name, string description, string[] scopes)
    {
        if (!int.TryParse(User.FindFirstValue("CompanyId"), out var companyId) ||
            !int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return RedirectToPage();

        // Validate scopes
        if (scopes == null || scopes.Length == 0)
        {
            Error = _localizer["ApiKey_Error_SelectScope"];
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
            Message = _localizer["ApiKey_Success_Submitted"];
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRevokeAsync(int keyId)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return RedirectToPage();

        var (key, error) = await _apiKeyService.RevokeApiKeyAsync(keyId, userId, "Revoked by user");

        if (error != null)
        {
            Error = error;
        }
        else
        {
            Message = _localizer["ApiKey_Success_Revoked", key!.Name];
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRefreshAsync(int keyId)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return RedirectToPage();

        var (newApiKey, error) = await _apiKeyService.RegenerateApiKeyAsync(keyId, userId);

        if (error != null)
        {
            Error = error;
        }
        else
        {
            GeneratedApiKey = newApiKey;
            Message = _localizer["ApiKey_Success_Refreshed"];
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
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var reviewerId))
            return RedirectToPage();

        if (!await CheckIsAdminAsync(reviewerId))
        {
            Error = _localizer["ApiKey_Error_Unauthorized"];
            return RedirectToPage();
        }

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
            Message = _localizer["ApiKey_Success_Approved", request!.RequestedByUser?.DisplayName ?? ""];
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRejectAsync(int requestId, string reviewNotes)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var reviewerId))
            return RedirectToPage();

        if (!await CheckIsAdminAsync(reviewerId))
        {
            Error = _localizer["ApiKey_Error_Unauthorized"];
            return RedirectToPage();
        }

        var (request, error) = await _apiKeyService.RejectRequestAsync(requestId, reviewerId, reviewNotes);

        if (error != null)
        {
            Error = error;
        }
        else
        {
            Message = _localizer["ApiKey_Success_Rejected", request!.RequestedByUser?.DisplayName ?? ""];
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRevokeAdminAsync(int keyId, string? reason)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var reviewerId))
            return RedirectToPage();

        if (!await CheckIsAdminAsync(reviewerId))
        {
            Error = _localizer["ApiKey_Error_Unauthorized"];
            return RedirectToPage();
        }

        var (key, error) = await _apiKeyService.RevokeApiKeyAsync(keyId, reviewerId, reason ?? "Revoked by administrator");

        if (error != null)
        {
            Error = error;
        }
        else
        {
            Message = _localizer["ApiKey_Success_AdminRevoked", key!.Name];
        }

        return RedirectToPage();
    }
}
