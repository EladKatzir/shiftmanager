using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.Localization;
using ShiftManager.Resources;
using ShiftManager.Services;
using System.Globalization;
using System.Text;

namespace ShiftManager.TagHelpers;

/// <summary>
/// Tag helper for rendering localized text with company-scoped overrides and edit mode support.
/// Usage: &lt;loc key="Button_Save" /&gt; or &lt;loc key="Welcome_Message" params='new object[] { userName }' /&gt;
/// </summary>
[HtmlTargetElement("loc", TagStructure = TagStructure.NormalOrSelfClosing)]
public class LocalizationTagHelper : TagHelper
{
    private readonly IStringLocalizer<SharedResources> _localizer;
    private readonly ICompanyLocalizationService _companyLocalizationService;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ITenantResolver _tenantResolver;

    public LocalizationTagHelper(
        IStringLocalizer<SharedResources> localizer,
        ICompanyLocalizationService companyLocalizationService,
        IHttpContextAccessor httpContextAccessor,
        ITenantResolver tenantResolver)
    {
        _localizer = localizer;
        _companyLocalizationService = companyLocalizationService;
        _httpContextAccessor = httpContextAccessor;
        _tenantResolver = tenantResolver;
    }

    /// <summary>
    /// The resource key (e.g., "Button_Save", "Dashboard_Title")
    /// </summary>
    [HtmlAttributeName("key")]
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// Optional parameters for parameterized strings (e.g., new object[] { userName, date })
    /// </summary>
    [HtmlAttributeName("params")]
    public object[]? Params { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        // Change tag to span
        output.TagName = "span";
        output.TagMode = TagMode.StartTagAndEndTag;

        // Add data-loc-key attribute
        output.Attributes.Add("data-loc-key", Key);

        // Get current culture from request
        var currentCulture = CultureInfo.CurrentUICulture.Name;

        // Get company ID from tenant resolver
        var companyId = _tenantResolver.GetCurrentTenantId();

        // Resolve the localized value
        string localizedValue;

        if (companyId > 0)
        {
            // Try to get company override
            var overrideValue = await _companyLocalizationService.GetOverrideValueAsync(
                companyId, currentCulture, Key);

            if (!string.IsNullOrEmpty(overrideValue))
            {
                // Use company override
                localizedValue = overrideValue;
            }
            else
            {
                // Fallback to base localization
                localizedValue = _localizer[Key].Value;
            }
        }
        else
        {
            // No company context, use base localization
            localizedValue = _localizer[Key].Value;
        }

        // Apply parameters if provided
        if (Params != null && Params.Length > 0)
        {
            try
            {
                localizedValue = string.Format(localizedValue, Params);

                // Store parameters as JSON for edit mode
                var paramsJson = System.Text.Json.JsonSerializer.Serialize(Params);
                output.Attributes.Add("data-loc-params", paramsJson);
            }
            catch (FormatException)
            {
                // If formatting fails, use the raw value
                // This can happen if override doesn't preserve placeholders correctly
            }
        }

        // Check if edit mode is active
        if (IsEditModeActive())
        {
            output.Attributes.Add("class", "loc-editable");
        }

        // Set the content
        output.Content.SetContent(localizedValue);
    }

    /// <summary>
    /// Check if language edit mode is active by looking for edit mode cookies
    /// </summary>
    private bool IsEditModeActive()
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext == null)
            return false;

        // Check for edit mode cookie
        var editModeCookie = httpContext.Request.Cookies["language_edit_mode"];
        return editModeCookie == "true";
    }
}
