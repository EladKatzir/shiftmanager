using Microsoft.AspNetCore.Mvc;

namespace ShiftManager.ViewComponents;

/// <summary>
/// Loading spinner component with size variants and localized labels.
/// Implements B-003 loading states requirements.
/// </summary>
public class LoadingSpinnerViewComponent : ViewComponent
{
    public IViewComponentResult Invoke(string size = "medium", string? label = null, bool showLabel = true)
    {
        return View(new LoadingSpinnerModel
        {
            Size = size,
            Label = label,
            ShowLabel = showLabel
        });
    }
}

public class LoadingSpinnerModel
{
    /// <summary>
    /// Size variant: "small", "medium", or "large"
    /// </summary>
    public string Size { get; set; } = "medium";

    /// <summary>
    /// Optional custom label. If null, uses localized "Loading..." text.
    /// </summary>
    public string? Label { get; set; }

    /// <summary>
    /// Whether to show the label text below the spinner.
    /// </summary>
    public bool ShowLabel { get; set; } = true;
}
