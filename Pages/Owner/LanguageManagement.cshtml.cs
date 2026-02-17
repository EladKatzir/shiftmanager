using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using ShiftManager.Models;
using ShiftManager.Resources;
using ShiftManager.Services;
using System.Security.Claims;

namespace ShiftManager.Pages.Owner;

/// <summary>
/// Language Management - Configure company languages and manage translation overrides
/// </summary>
[Authorize(Policy = "Grant:AdminAccess")]
[IgnoreAntiforgeryToken] // SECURITY-AUDITED: JSON POST from JS fetch; protected by SameSite=Lax cookies + Authorize policy
public class LanguageManagementModel : PageModel
{
    private readonly ILanguageManagementService _languageManagementService;
    private readonly ICompanyLocalizationService _companyLocalizationService;
    private readonly IOwnerCompanySelectorService _ownerCompanySelector;
    private readonly IStringLocalizer<SharedResources> _localizer;
    private readonly ILogger<LanguageManagementModel> _logger;
    private readonly RequestLocalizationOptions _localizationOptions;

    public LanguageManagementModel(
        ILanguageManagementService languageManagementService,
        ICompanyLocalizationService companyLocalizationService,
        IOwnerCompanySelectorService ownerCompanySelector,
        IStringLocalizer<SharedResources> localizer,
        ILogger<LanguageManagementModel> logger,
        IOptions<RequestLocalizationOptions> localizationOptions)
    {
        _languageManagementService = languageManagementService;
        _companyLocalizationService = companyLocalizationService;
        _ownerCompanySelector = ownerCompanySelector;
        _localizer = localizer;
        _logger = logger;
        _localizationOptions = localizationOptions.Value;
    }

    // Language Settings
    [BindProperty]
    public string DefaultCulture { get; set; } = "en-US";

    [BindProperty]
    public string AlternateCulture { get; set; } = "he-IL";

    // Override Management
    [BindProperty]
    public string? OverrideKey { get; set; }

    [BindProperty]
    public string? OverrideValue { get; set; }

    [BindProperty]
    public string? OverrideCulture { get; set; }

    // Search/Filter
    public string? SearchTerm { get; set; }
    public string? FilterCulture { get; set; }

    // Data
    public List<CompanyLocalizationOverride> Overrides { get; set; } = new();
    public int? SelectedCompanyId { get; set; }
    public List<string> SupportedCultures { get; set; } = new();

    // Messages
    [TempData]
    public string? Success { get; set; }
    public string? Error { get; set; }

    public async Task OnGetAsync(string? searchTerm = null, string? filterCulture = null)
    {
        try
        {
            // Get supported cultures from configuration
            SupportedCultures = _localizationOptions.SupportedUICultures?
                .Select(c => c.Name)
                .ToList() ?? new List<string> { "en-US", "he-IL" };

            SelectedCompanyId = _ownerCompanySelector.GetSelectedCompanyId();

            if (SelectedCompanyId == null)
            {
                Error = "Please select a company to manage language settings.";
                return;
            }

            // Load language settings
            var settings = await _languageManagementService.GetLanguageSettingsAsync(SelectedCompanyId.Value);
            DefaultCulture = settings.DefaultCulture;
            AlternateCulture = settings.AlternateCulture;

            // Load overrides with search/filter
            SearchTerm = searchTerm;
            FilterCulture = filterCulture;
            Overrides = await _companyLocalizationService.SearchOverridesAsync(
                SelectedCompanyId.Value, filterCulture, searchTerm);

            _logger.LogInformation("Loaded language management page for CompanyId={CompanyId}, {OverrideCount} overrides, {CultureCount} supported cultures",
                SelectedCompanyId.Value, Overrides.Count, SupportedCultures.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading language management page");
            Error = "Failed to load language settings. Please try again.";
        }
    }

    public async Task<IActionResult> OnPostSaveLanguageSettingsAsync()
    {
        try
        {
            SelectedCompanyId = _ownerCompanySelector.GetSelectedCompanyId();
            if (SelectedCompanyId == null)
            {
                Error = "No company selected.";
                await OnGetAsync();
                return Page();
            }

            // Validate
            if (!_languageManagementService.ValidateLanguageSettings(DefaultCulture, AlternateCulture, out var validationError))
            {
                Error = validationError;
                await OnGetAsync();
                return Page();
            }

            // Save
            var userId = GetCurrentUserId();
            await _languageManagementService.SaveLanguageSettingsAsync(
                SelectedCompanyId.Value, DefaultCulture, AlternateCulture, userId);

            Success = "Language settings saved successfully.";
            return RedirectToPage();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving language settings");
            Error = "Failed to save language settings. Please try again.";
            await OnGetAsync();
            return Page();
        }
    }

    public async Task<IActionResult> OnPostAddOverrideAsync()
    {
        try
        {
            SelectedCompanyId = _ownerCompanySelector.GetSelectedCompanyId();
            if (SelectedCompanyId == null)
            {
                Error = "No company selected.";
                await OnGetAsync();
                return Page();
            }

            if (string.IsNullOrWhiteSpace(OverrideKey) || string.IsNullOrWhiteSpace(OverrideValue) || string.IsNullOrWhiteSpace(OverrideCulture))
            {
                Error = "All fields are required to add an override.";
                await OnGetAsync();
                return Page();
            }

            var userId = GetCurrentUserId();
            await _companyLocalizationService.UpsertOverrideAsync(
                SelectedCompanyId.Value, OverrideCulture, OverrideKey, OverrideValue, userId);

            Success = $"Override for '{OverrideKey}' saved successfully.";
            return RedirectToPage();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save override");
            Error = "Failed to save override. Please try again.";
            await OnGetAsync();
            return Page();
        }
    }

    public async Task<IActionResult> OnPostDeleteOverrideAsync(int id)
    {
        try
        {
            SelectedCompanyId = _ownerCompanySelector.GetSelectedCompanyId();
            if (SelectedCompanyId == null)
            {
                return Forbid();
            }

            // Find the override to get its details
            var allOverrides = await _companyLocalizationService.SearchOverridesAsync(SelectedCompanyId.Value);
            var existingOverride = allOverrides.FirstOrDefault(o => o.Id == id);

            if (existingOverride == null || existingOverride.CompanyId != SelectedCompanyId.Value)
            {
                return Forbid();
            }

            var userId = GetCurrentUserId();
            await _companyLocalizationService.DeleteOverrideAsync(
                SelectedCompanyId.Value, existingOverride.Culture, existingOverride.ResourceKey, userId);

            Success = "Override deleted successfully.";
            return RedirectToPage();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete override");
            Error = "Failed to delete override. Please try again.";
            return RedirectToPage();
        }
    }

    /// <summary>
    /// API handler for saving draft translations from edit mode.
    /// Called by language-edit-mode.js when user clicks "Save and Exit".
    /// </summary>
    public async Task<IActionResult> OnPostApiSaveDraftsAsync([FromBody] SaveDraftsRequest request)
    {
        try
        {
            // Validate request
            if (request == null || request.Drafts == null || request.Drafts.Count == 0)
            {
                return new JsonResult(new { success = false, message = "No drafts provided" });
            }

            // Validate that Owner has selected this company (security check)
            var selectedCompanyId = _ownerCompanySelector.GetSelectedCompanyId();
            if (selectedCompanyId != request.CompanyId)
            {
                _logger.LogWarning("Owner attempted to save drafts for CompanyId={CompanyId} but has CompanyId={SelectedCompanyId} selected",
                    request.CompanyId, selectedCompanyId);
                return new JsonResult(new { success = false, message = "You can only save translations for the currently selected company." });
            }

            // Validate culture
            var allowedCultures = new[] { "en-US", "he-IL" };
            if (!allowedCultures.Contains(request.Culture))
            {
                return new JsonResult(new { success = false, message = $"Invalid culture: {request.Culture}" });
            }

            // Save all drafts using bulk upsert
            var userId = GetCurrentUserId();
            await _companyLocalizationService.UpsertOverridesBulkAsync(
                request.CompanyId, request.Culture, request.Drafts, userId);

            _logger.LogInformation("Owner saved {Count} draft translation(s) for CompanyId={CompanyId}, Culture={Culture}",
                request.Drafts.Count, request.CompanyId, request.Culture);

            return new JsonResult(new { success = true, count = request.Drafts.Count });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save draft translations");
            return new JsonResult(new { success = false, message = "An unexpected error occurred. Please try again." });
        }
    }

    private int GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(userIdClaim, out var id) ? id : 0;
    }
}

/// <summary>
/// Request model for saving draft translations
/// </summary>
public class SaveDraftsRequest
{
    public int CompanyId { get; set; }
    public string Culture { get; set; } = string.Empty;
    public Dictionary<string, string> Drafts { get; set; } = new();
}
