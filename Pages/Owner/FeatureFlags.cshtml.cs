using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Localization;
using ShiftManager.Resources;
using System.Text.Json;

namespace ShiftManager.Pages.Owner;

/// <summary>
/// Feature Flags Management - Toggle system features on/off
/// </summary>
[Authorize(Policy = "Grant:SystemConfiguration")]
public class FeatureFlagsModel : LocalizedPageModel
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<FeatureFlagsModel> _logger;

    public FeatureFlagsModel(
        IStringLocalizer<SharedResources> localizer,
        IConfiguration configuration,
        ILogger<FeatureFlagsModel> logger)
        : base(localizer)
    {
        _configuration = configuration;
        _logger = logger;
    }

    // Core Feature Flags
    [BindProperty] public bool EnforceCompanyScope { get; set; }
    [BindProperty] public bool EnableDirectorRole { get; set; }
    [BindProperty] public bool AllowPublicSignup { get; set; }
    [BindProperty] public bool EnableApiKeyManagement { get; set; }

    // API Categories
    public Dictionary<string, Dictionary<string, bool>> ApiCategories { get; set; } = new();

    public string? Message { get; set; }

    public void OnGet()
    {
        LoadFeatureFlags();
    }

    public IActionResult OnPost()
    {
        try
        {
            // Note: Modifying appsettings.json at runtime is not recommended for production
            // In production, use environment variables, Azure App Configuration, or similar
            Message = _localizer["Info_FeatureFlagChangesRequireRestart"].Value;

            _logger.LogInformation("Feature flags updated: EnforceCompanyScope={EnforceCompanyScope}, " +
                "EnableDirectorRole={EnableDirectorRole}, AllowPublicSignup={AllowPublicSignup}, " +
                "EnableApiKeyManagement={EnableApiKeyManagement}",
                EnforceCompanyScope, EnableDirectorRole, AllowPublicSignup, EnableApiKeyManagement);

            // In a real implementation, you would:
            // 1. Write to appsettings.json or external config
            // 2. Trigger configuration reload
            // 3. Or use a database-backed feature flag system

            LoadFeatureFlags();
            return Page();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving feature flags");
            Message = _localizer["Error_SavingFeatureFlags"].Value;
            LoadFeatureFlags();
            return Page();
        }
    }

    private void LoadFeatureFlags()
    {
        try
        {
            // Load core feature flags
            EnforceCompanyScope = _configuration.GetValue<bool>("Features:EnforceCompanyScope", true);
            EnableDirectorRole = _configuration.GetValue<bool>("Features:EnableDirectorRole", true);
            AllowPublicSignup = _configuration.GetValue<bool>("Features:AllowPublicSignup", true);
            EnableApiKeyManagement = _configuration.GetValue<bool>("Features:EnableApiKeyManagement", true);

            // Load API feature flags
            ApiCategories = new Dictionary<string, Dictionary<string, bool>>
            {
                ["Users"] = new()
                {
                    ["List"] = _configuration.GetValue<bool>("Features:Api:Users:ListEnabled", true),
                    ["Get"] = _configuration.GetValue<bool>("Features:Api:Users:GetEnabled", true),
                    ["Create"] = _configuration.GetValue<bool>("Features:Api:Users:CreateEnabled", true),
                    ["Update"] = _configuration.GetValue<bool>("Features:Api:Users:UpdateEnabled", true)
                },
                ["Shifts"] = new()
                {
                    ["List"] = _configuration.GetValue<bool>("Features:Api:Shifts:ListEnabled", true),
                    ["Get"] = _configuration.GetValue<bool>("Features:Api:Shifts:GetEnabled", true)
                },
                ["TimeOff"] = new()
                {
                    ["List"] = _configuration.GetValue<bool>("Features:Api:TimeOff:ListEnabled", true),
                    ["Get"] = _configuration.GetValue<bool>("Features:Api:TimeOff:GetEnabled", true),
                    ["Create"] = _configuration.GetValue<bool>("Features:Api:TimeOff:CreateEnabled", true),
                    ["Approve"] = _configuration.GetValue<bool>("Features:Api:TimeOff:ApproveEnabled", true)
                },
                ["Notifications"] = new()
                {
                    ["List"] = _configuration.GetValue<bool>("Features:Api:Notifications:ListEnabled", true),
                    ["Get"] = _configuration.GetValue<bool>("Features:Api:Notifications:GetEnabled", true),
                    ["MarkRead"] = _configuration.GetValue<bool>("Features:Api:Notifications:MarkReadEnabled", true)
                },
                ["Chores"] = new()
                {
                    ["List"] = _configuration.GetValue<bool>("Features:Api:Chores:ListEnabled", true),
                    ["Get"] = _configuration.GetValue<bool>("Features:Api:Chores:GetEnabled", true),
                    ["Create"] = _configuration.GetValue<bool>("Features:Api:Chores:CreateEnabled", true),
                    ["Update"] = _configuration.GetValue<bool>("Features:Api:Chores:UpdateEnabled", true),
                    ["Delete"] = _configuration.GetValue<bool>("Features:Api:Chores:DeleteEnabled", true)
                },
                ["OnDuty"] = new()
                {
                    ["List"] = _configuration.GetValue<bool>("Features:Api:OnDuty:ListEnabled", true),
                    ["Get"] = _configuration.GetValue<bool>("Features:Api:OnDuty:GetEnabled", true),
                    ["Create"] = _configuration.GetValue<bool>("Features:Api:OnDuty:CreateEnabled", true),
                    ["Update"] = _configuration.GetValue<bool>("Features:Api:OnDuty:UpdateEnabled", true),
                    ["Delete"] = _configuration.GetValue<bool>("Features:Api:OnDuty:DeleteEnabled", true)
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading feature flags");
        }
    }
}
