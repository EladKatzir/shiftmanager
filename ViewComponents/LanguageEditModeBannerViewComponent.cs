using Microsoft.AspNetCore.Mvc;

namespace ShiftManager.ViewComponents;

/// <summary>
/// ViewComponent for displaying the language edit mode banner.
/// Only renders when edit mode cookies are present.
/// </summary>
public class LanguageEditModeBannerViewComponent : ViewComponent
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public LanguageEditModeBannerViewComponent(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public IViewComponentResult Invoke()
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext == null)
            return Content(string.Empty);

        // Check if edit mode is active
        var editModeCookie = httpContext.Request.Cookies["language_edit_mode"];
        if (editModeCookie != "true")
            return Content(string.Empty);

        // Get edit mode parameters
        var companyIdCookie = httpContext.Request.Cookies["language_edit_companyId"];
        var cultureCookie = httpContext.Request.Cookies["language_edit_culture"];

        if (string.IsNullOrWhiteSpace(companyIdCookie) || string.IsNullOrWhiteSpace(cultureCookie))
        {
            // Invalid edit mode state, don't show banner
            return Content(string.Empty);
        }

        // Parse company ID
        if (!int.TryParse(companyIdCookie, out var companyId))
            return Content(string.Empty);

        // Create model
        var model = new LanguageEditModeBannerViewModel
        {
            CompanyId = companyId,
            Culture = cultureCookie
        };

        return View(model);
    }
}

/// <summary>
/// Model for Language Edit Mode Banner
/// </summary>
public class LanguageEditModeBannerViewModel
{
    public int CompanyId { get; set; }
    public string Culture { get; set; } = string.Empty;
}
