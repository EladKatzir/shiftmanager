using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using ShiftManager.Models;
using ShiftManager.Resources;
using ShiftManager.Services;

namespace ShiftManager.Pages.Owner;

/// <summary>
/// Feature Flags Management — view and toggle all DB-backed feature flags.
/// Uses IFeatureFlagService for full CRUD. Changes take effect immediately
/// (SetFlagAsync calls InvalidateCache which clears both per-flag and warm cache).
/// </summary>
[Authorize(Policy = "Grant:SystemConfiguration")]
public class FeatureFlagsModel : LocalizedPageModel
{
    private readonly IFeatureFlagService _featureFlagService;
    private readonly ILogger<FeatureFlagsModel> _logger;
    private readonly IAuditLogService _auditLogService;

    public FeatureFlagsModel(
        IStringLocalizer<SharedResources> localizer,
        IFeatureFlagService featureFlagService,
        ILogger<FeatureFlagsModel> logger,
        IAuditLogService auditLogService)
        : base(localizer)
    {
        _featureFlagService = featureFlagService;
        _logger = logger;
        _auditLogService = auditLogService;
    }

    /// <summary>
    /// Flags grouped by category for display. Key = category name, Value = list of flags.
    /// </summary>
    public Dictionary<string, List<FeatureFlag>> FlagsByCategory { get; set; } = new();

    // Message property removed — feedback now flows through TempData → _Layout FeedbackModal bridge.

    public async Task OnGetAsync()
    {
        await LoadFlagsAsync();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        try
        {
            // Get all current global flags from DB
            var allFlags = (await _featureFlagService.GetAllFlagsAsync())
                .Where(f => f.CompanyId == null && f.UserId == null) // Only global flags
                .ToList();

            var enabledFlags = Request.Form.Keys
                .Where(k => k.StartsWith("flag_", StringComparison.Ordinal))
                .Select(k => k.Substring(5)) // Remove "flag_" prefix
                .ToHashSet();

            int changed = 0;
            var changedFlags = new List<string>();
            foreach (var flag in allFlags)
            {
                var shouldBeEnabled = enabledFlags.Contains(flag.Name);
                if (flag.IsEnabled != shouldBeEnabled)
                {
                    await _featureFlagService.SetFlagAsync(flag.Name, shouldBeEnabled);
                    _logger.LogInformation("Feature flag {FlagName} changed to {IsEnabled}", flag.Name, shouldBeEnabled);
                    changedFlags.Add($"{flag.Name}={shouldBeEnabled}");
                    changed++;
                }
            }

            if (changed > 0)
            {
                await _auditLogService.LogAsync("FeatureFlagToggled", "FeatureFlag", null,
                    $"Toggled {changed} feature flag(s)",
                    string.Join(", ", changedFlags));

                TempData["SuccessMessage"] = $"{changed} flag(s) updated. Changes take effect immediately.";
            }
            else
            {
                TempData["InfoMessage"] = "No changes detected.";
            }

            await LoadFlagsAsync();
            return Page();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving feature flags");
            TempData["ErrorMessage"] = _localizer["Error_SavingFeatureFlags"].Value;
            TempData["ErrorId"] = HttpContext.TraceIdentifier;
            await LoadFlagsAsync();
            return Page();
        }
    }

    private async Task LoadFlagsAsync()
    {
        var allFlags = (await _featureFlagService.GetAllFlagsAsync())
            .Where(f => f.CompanyId == null && f.UserId == null) // Only global flags
            .ToList();

        FlagsByCategory = allFlags
            .GroupBy(f => GetCategory(f.Name))
            .OrderBy(g => GetCategorySortOrder(g.Key))
            .ToDictionary(g => g.Key, g => g.OrderBy(f => f.Name).ToList());
    }

    /// <summary>
    /// Maps flag names to display categories based on prefix convention.
    /// </summary>
    private static string GetCategory(string flagName)
    {
        if (flagName.StartsWith("FF_API_", StringComparison.Ordinal))
            return "API Endpoints";
        if (flagName.StartsWith("FF_EXCEL_", StringComparison.Ordinal))
            return "Excel Calendars";
        if (flagName.StartsWith("FF_NEW_", StringComparison.Ordinal) || flagName.StartsWith("FF_WIDGETS_", StringComparison.Ordinal) || flagName.StartsWith("FF_SCOPE_", StringComparison.Ordinal))
            return "UI Features";
        if (flagName.StartsWith("FF_ENFORCE_", StringComparison.Ordinal) || flagName.StartsWith("FF_ALLOW_", StringComparison.Ordinal) || flagName.StartsWith("FF_ENABLE_", StringComparison.Ordinal))
            return "Operations";
        // Remaining: feature-specific flags (friendships, duty rotation, setup tasks)
        return "Features";
    }

    /// <summary>
    /// Sort order for categories in the UI.
    /// </summary>
    private static int GetCategorySortOrder(string category) => category switch
    {
        "Operations" => 0,
        "UI Features" => 1,
        "Excel Calendars" => 2,
        "Features" => 3,
        "API Endpoints" => 4,
        _ => 99
    };
}
