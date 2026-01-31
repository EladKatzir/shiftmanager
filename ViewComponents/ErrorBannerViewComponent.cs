using Microsoft.AspNetCore.Mvc;

namespace ShiftManager.ViewComponents;

/// <summary>
/// ViewComponent for displaying error banners with localization support.
/// Supports error, warning, and info levels with dismissible and retry options.
/// </summary>
public class ErrorBannerViewComponent : ViewComponent
{
    public IViewComponentResult Invoke(
        string level = "error",
        string messageKey = "",
        string fallbackMessage = "",
        bool dismissible = true,
        bool showRetry = false,
        string? retryAction = null,
        string? titleKey = null,
        string? fallbackTitle = null)
    {
        return View(new ErrorBannerModel
        {
            Level = level,
            MessageKey = messageKey,
            FallbackMessage = fallbackMessage,
            Dismissible = dismissible,
            ShowRetry = showRetry,
            RetryAction = retryAction,
            TitleKey = titleKey,
            FallbackTitle = fallbackTitle
        });
    }
}

public class ErrorBannerModel
{
    /// <summary>
    /// Severity level: "error", "warning", or "info"
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
    /// Whether the banner can be dismissed by the user
    /// </summary>
    public bool Dismissible { get; set; } = true;

    /// <summary>
    /// Whether to show a retry button
    /// </summary>
    public bool ShowRetry { get; set; }

    /// <summary>
    /// JavaScript action to execute on retry (e.g., "location.reload()" or custom function)
    /// </summary>
    public string? RetryAction { get; set; }

    /// <summary>
    /// Optional localization key for title
    /// </summary>
    public string? TitleKey { get; set; }

    /// <summary>
    /// Optional fallback title
    /// </summary>
    public string? FallbackTitle { get; set; }
}
