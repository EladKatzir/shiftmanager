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

    // Message / Error inline-alert properties removed - migrated to the unified feedback surface.
    // Handlers now set TempData["SuccessMessage"] / TempData["ErrorMessage"] (+ TempData["ErrorId"])
    // directly; the _Layout.cshtml bridge opens a FeedbackModal on the next page load.

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
            // Defense-in-depth: [Authorize] passed but the claims are malformed/missing.
            // Bounce to Home with a feedback toast rather than rendering an empty page.
            TempData["ErrorMessage"] = _localizer["Error_AuthenticationError"].Value;
            TempData["ErrorId"] = HttpContext.TraceIdentifier;
            return RedirectToPage("/Home/Index");
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
            TempData["ErrorMessage"] = _localizer["ApiKey_Error_SelectScope"].Value;
            TempData["ErrorId"] = HttpContext.TraceIdentifier;
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
            TempData["ErrorMessage"] = error;
            TempData["ErrorId"] = HttpContext.TraceIdentifier;
        }
        else
        {
            TempData["SuccessMessage"] = _localizer["ApiKey_Success_Submitted"].Value;
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
            TempData["ErrorMessage"] = error;
            TempData["ErrorId"] = HttpContext.TraceIdentifier;
        }
        else
        {
            TempData["SuccessMessage"] = _localizer["ApiKey_Success_Revoked", key!.Name].Value;
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
            TempData["ErrorMessage"] = error;
            TempData["ErrorId"] = HttpContext.TraceIdentifier;
        }
        else
        {
            GeneratedApiKey = newApiKey;
            TempData["SuccessMessage"] = _localizer["ApiKey_Success_Refreshed"].Value;
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
            TempData["ErrorMessage"] = _localizer["ApiKey_Error_Unauthorized"].Value;
            TempData["ErrorId"] = HttpContext.TraceIdentifier;
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
            TempData["ErrorMessage"] = error;
            TempData["ErrorId"] = HttpContext.TraceIdentifier;
        }
        else
        {
            GeneratedApiKey = apiKey;
            TempData["SuccessMessage"] = _localizer["ApiKey_Success_Approved", request!.RequestedByUser?.DisplayName ?? ""].Value;
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRejectAsync(int requestId, string reviewNotes)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var reviewerId))
            return RedirectToPage();

        if (!await CheckIsAdminAsync(reviewerId))
        {
            TempData["ErrorMessage"] = _localizer["ApiKey_Error_Unauthorized"].Value;
            TempData["ErrorId"] = HttpContext.TraceIdentifier;
            return RedirectToPage();
        }

        var (request, error) = await _apiKeyService.RejectRequestAsync(requestId, reviewerId, reviewNotes);

        if (error != null)
        {
            TempData["ErrorMessage"] = error;
            TempData["ErrorId"] = HttpContext.TraceIdentifier;
        }
        else
        {
            TempData["SuccessMessage"] = _localizer["ApiKey_Success_Rejected", request!.RequestedByUser?.DisplayName ?? ""].Value;
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRevokeAdminAsync(int keyId, string? reason)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var reviewerId))
            return RedirectToPage();

        if (!await CheckIsAdminAsync(reviewerId))
        {
            TempData["ErrorMessage"] = _localizer["ApiKey_Error_Unauthorized"].Value;
            TempData["ErrorId"] = HttpContext.TraceIdentifier;
            return RedirectToPage();
        }

        // callerIsAdmin: CheckIsAdminAsync(reviewerId) already ran above, so the ownership gate in
        // the service is deliberately bypassed here — an administrator must be able to revoke any
        // key. The self-service handler (OnPostRevokeAsync) leaves it at its fail-closed default.
        var (key, error) = await _apiKeyService.RevokeApiKeyAsync(keyId, reviewerId, reason ?? "Revoked by administrator", callerIsAdmin: true);

        if (error != null)
        {
            TempData["ErrorMessage"] = error;
            TempData["ErrorId"] = HttpContext.TraceIdentifier;
        }
        else
        {
            TempData["SuccessMessage"] = _localizer["ApiKey_Success_AdminRevoked", key!.Name].Value;
        }

        return RedirectToPage();
    }
}
