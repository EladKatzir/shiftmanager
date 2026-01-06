using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using ShiftManager.Resources;
using ShiftManager.Services;
using System.Globalization;

namespace ShiftManager.ViewComponents;

public class LanguageToggleViewComponent : ViewComponent
{
    private readonly IStringLocalizer<SharedResources> _localizer;
    private readonly ILanguageManagementService _languageManagementService;
    private readonly ITenantResolver _tenantResolver;

    public LanguageToggleViewComponent(
        IStringLocalizer<SharedResources> localizer,
        ILanguageManagementService languageManagementService,
        ITenantResolver tenantResolver)
    {
        _localizer = localizer;
        _languageManagementService = languageManagementService;
        _tenantResolver = tenantResolver;
    }

    public async Task<IViewComponentResult> InvokeAsync()
    {
        var currentCulture = CultureInfo.CurrentUICulture.Name;
        var isHebrew = currentCulture.StartsWith("he");

        // Get company language settings
        var companyId = _tenantResolver.GetCurrentTenantId();
        string defaultCulture = "en-US";
        string alternateCulture = "he-IL";

        if (companyId > 0)
        {
            try
            {
                var settings = await _languageManagementService.GetLanguageSettingsAsync(companyId);
                defaultCulture = settings.DefaultCulture;
                alternateCulture = settings.AlternateCulture;
            }
            catch
            {
                // Fallback to defaults if settings not found or error occurs
            }
        }

        var model = new LanguageToggleViewModel
        {
            CurrentLanguage = currentCulture,
            DefaultCulture = defaultCulture,
            AlternateCulture = alternateCulture,
            IsHebrew = isHebrew
        };

        return View(model);
    }
}

public class LanguageToggleViewModel
{
    public string CurrentLanguage { get; set; } = "en-US";
    public string DefaultCulture { get; set; } = "en-US";
    public string AlternateCulture { get; set; } = "he-IL";
    public bool IsHebrew { get; set; }
}