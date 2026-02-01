using Microsoft.AspNetCore.Razor.TagHelpers;

namespace ShiftManager.TagHelpers;

/// <summary>
/// Tag helper for rendering localized HTML attributes.
/// Converts loc-* attributes to data-loc-attr-* attributes that are processed by JavaScript.
///
/// Usage:
///   &lt;button loc-title="Button_Delete" loc-aria-label="Button_Delete_Description"&gt;
///       &lt;loc key="Delete" /&gt;
///   &lt;/button&gt;
///
/// Renders as:
///   &lt;button data-loc-attr-title="Button_Delete" data-loc-attr-aria-label="Button_Delete_Description"&gt;
///       &lt;span data-loc-key="Delete"&gt;Delete&lt;/span&gt;
///   &lt;/button&gt;
///
/// The localization-attributes.js script will then fetch localized values and apply them
/// to the actual HTML attributes (title, placeholder, aria-label, aria-description).
/// </summary>
[HtmlTargetElement("*", Attributes = "loc-title")]
[HtmlTargetElement("*", Attributes = "loc-placeholder")]
[HtmlTargetElement("*", Attributes = "loc-aria-label")]
[HtmlTargetElement("*", Attributes = "loc-aria-description")]
public class LocalizationAttributeTagHelper : TagHelper
{
    /// <summary>
    /// Localization key for the title attribute
    /// </summary>
    [HtmlAttributeName("loc-title")]
    public string? LocTitle { get; set; }

    /// <summary>
    /// Localization key for the placeholder attribute
    /// </summary>
    [HtmlAttributeName("loc-placeholder")]
    public string? LocPlaceholder { get; set; }

    /// <summary>
    /// Localization key for the aria-label attribute
    /// </summary>
    [HtmlAttributeName("loc-aria-label")]
    public string? LocAriaLabel { get; set; }

    /// <summary>
    /// Localization key for the aria-description attribute
    /// </summary>
    [HtmlAttributeName("loc-aria-description")]
    public string? LocAriaDescription { get; set; }

    /// <summary>
    /// Lower order than LocalizationTagHelper to ensure attributes are processed first
    /// </summary>
    public override int Order => -1000;

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        // Convert loc-* attributes to data-loc-attr-* attributes

        if (!string.IsNullOrEmpty(LocTitle))
        {
            output.Attributes.RemoveAll("loc-title");
            output.Attributes.Add("data-loc-attr-title", LocTitle);
        }

        if (!string.IsNullOrEmpty(LocPlaceholder))
        {
            output.Attributes.RemoveAll("loc-placeholder");
            output.Attributes.Add("data-loc-attr-placeholder", LocPlaceholder);
        }

        if (!string.IsNullOrEmpty(LocAriaLabel))
        {
            output.Attributes.RemoveAll("loc-aria-label");
            output.Attributes.Add("data-loc-attr-aria-label", LocAriaLabel);
        }

        if (!string.IsNullOrEmpty(LocAriaDescription))
        {
            output.Attributes.RemoveAll("loc-aria-description");
            output.Attributes.Add("data-loc-attr-aria-description", LocAriaDescription);
        }

        // Note: The actual localized values will be applied by localization-attributes.js
        // This tag helper just converts the markup from loc-* to data-loc-attr-* format
    }
}
