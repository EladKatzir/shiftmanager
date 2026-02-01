using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using ShiftManager.Resources;

namespace ShiftManager.Pages;

/// <summary>
/// Base class for all Razor Pages that provides localization support.
/// Inheriting pages get access to _localizer for translating strings.
/// </summary>
public class LocalizedPageModel : PageModel
{
    protected readonly IStringLocalizer<SharedResources> _localizer;

    /// <summary>
    /// Error message to display to the user (localized).
    /// </summary>
    public string? Error { get; set; }

    /// <summary>
    /// Success message to display to the user (localized).
    /// </summary>
    public string? Success { get; set; }

    public LocalizedPageModel(IStringLocalizer<SharedResources> localizer)
    {
        _localizer = localizer;
    }
}
