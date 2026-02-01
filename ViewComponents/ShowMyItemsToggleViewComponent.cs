using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using ShiftManager.Resources;
using ShiftManager.Services;

namespace ShiftManager.ViewComponents;

/// <summary>
/// ✅ PHASE 20: ViewComponent for toggling "Show My Items Only" preference
/// </summary>
public class ShowMyItemsToggleViewComponent : ViewComponent
{
    private readonly IStringLocalizer<SharedResources> _localizer;
    private readonly IUserPreferenceService _userPreferenceService;

    public ShowMyItemsToggleViewComponent(
        IStringLocalizer<SharedResources> localizer,
        IUserPreferenceService userPreferenceService)
    {
        _localizer = localizer;
        _userPreferenceService = userPreferenceService;
    }

    public IViewComponentResult Invoke()
    {
        var showMyItemsOnly = _userPreferenceService.GetShowMyItemsOnly();

        var model = new ShowMyItemsToggleViewModel
        {
            ShowMyItemsOnly = showMyItemsOnly
        };

        return View(model);
    }
}

public class ShowMyItemsToggleViewModel
{
    public bool ShowMyItemsOnly { get; set; }
}
