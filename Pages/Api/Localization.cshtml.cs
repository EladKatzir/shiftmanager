using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using ShiftManager.Resources;
using ShiftManager.Services;
using System.Security.Claims;

namespace ShiftManager.Pages.Api;

/// <summary>
/// API endpoint for client-side JavaScript localization
/// Fetches localized strings for given keys, respecting current culture and company overrides
/// </summary>
[AllowAnonymous]
[IgnoreAntiforgeryToken]
public class LocalizationModel : PageModel
{
    private readonly IStringLocalizer<SharedResources> _localizer;
    private readonly ICompanyLocalizationService _companyLocalizationService;
    private readonly ILogger<LocalizationModel> _logger;

    public LocalizationModel(
        IStringLocalizer<SharedResources> localizer,
        ICompanyLocalizationService companyLocalizationService,
        ILogger<LocalizationModel> logger)
    {
        _localizer = localizer;
        _companyLocalizationService = companyLocalizationService;
        _logger = logger;
    }

    public async Task<IActionResult> OnGetAsync([FromQuery] string? keys)
    {
        try
        {
            // Validate input
            if (string.IsNullOrWhiteSpace(keys))
            {
                return new JsonResult(new Dictionary<string, string>());
            }

            // Parse comma-separated keys
            var keyList = keys.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(k => k.Trim())
                .Where(k => !string.IsNullOrEmpty(k))
                .Distinct()
                .ToList();

            if (keyList.Count == 0)
            {
                return new JsonResult(new Dictionary<string, string>());
            }

            // Get current culture from request
            var currentCulture = System.Globalization.CultureInfo.CurrentUICulture.Name;

            // Get current company ID from claims (if available)
            var companyIdClaim = User.FindFirst("SelectedCompanyId")?.Value
                ?? User.FindFirst("CompanyId")?.Value;

            int? companyId = null;
            if (!string.IsNullOrEmpty(companyIdClaim) && int.TryParse(companyIdClaim, out var parsedCompanyId))
            {
                companyId = parsedCompanyId;
            }

            // Build result dictionary
            var result = new Dictionary<string, string>();

            foreach (var key in keyList)
            {
                string localizedValue;

                // Check if company has override for this key
                if (companyId.HasValue)
                {
                    var overrideValue = await _companyLocalizationService.GetOverrideValueAsync(
                        companyId.Value, currentCulture, key);

                    if (!string.IsNullOrEmpty(overrideValue))
                    {
                        // Use company override
                        localizedValue = overrideValue;
                    }
                    else
                    {
                        // Fallback to base localization
                        localizedValue = _localizer[key].Value;
                    }
                }
                else
                {
                    // No company context, use base localization
                    localizedValue = _localizer[key].Value;
                }

                result[key] = localizedValue;
            }

            _logger.LogDebug("Localization API: Returned {Count} keys for culture {Culture}, CompanyId {CompanyId}",
                result.Count, currentCulture, companyId?.ToString() ?? "none");

            return new JsonResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch localizations for keys: {Keys}", keys);

            // Return empty result on error (JavaScript will use keys as fallback)
            return new JsonResult(new Dictionary<string, string>())
            {
                StatusCode = 500
            };
        }
    }
}
