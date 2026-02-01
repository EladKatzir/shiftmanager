using Microsoft.AspNetCore.Mvc;

namespace ShiftManager.ViewComponents;

/// <summary>
/// ViewComponent for displaying transient toast notifications.
/// Supports error, warning, info, and success levels with auto-dismiss.
/// </summary>
public class ErrorToastViewComponent : ViewComponent
{
    public IViewComponentResult Invoke(
        string level = "error",
        string messageKey = "",
        string fallbackMessage = "",
        string? titleKey = null,
        string? fallbackTitle = null,
        int autoDismissMs = 5000,
        bool showCloseButton = true)
    {
        return View(new ErrorToastModel
        {
            Level = level,
            MessageKey = messageKey,
            FallbackMessage = fallbackMessage,
            TitleKey = titleKey,
            FallbackTitle = fallbackTitle,
            AutoDismissMs = autoDismissMs,
            ShowCloseButton = showCloseButton
        });
    }
}

public class ErrorToastModel
{
    /// <summary>
    /// Severity level: "error", "warning", "info", or "success"
    /// </summary>
    public string Level { get; set; } = "error";

    /// <summary>
    /// Localization key for the message
    /// </summary>
    public string MessageKey { get; set; } = "";

    /// <summary>
    /// Fallback message if localization key is not found
    /// </summary>
    public string FallbackMessage { get; set; } = "";

    /// <summary>
    /// Optional localization key for title
    /// </summary>
    public string? TitleKey { get; set; }

    /// <summary>
    /// Optional fallback title
    /// </summary>
    public string? FallbackTitle { get; set; }

    /// <summary>
    /// Auto-dismiss time in milliseconds (0 = never auto-dismiss)
    /// </summary>
    public int AutoDismissMs { get; set; } = 5000;

    /// <summary>
    /// Whether to show the close button
    /// </summary>
    public bool ShowCloseButton { get; set; } = true;
}
