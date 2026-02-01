using Microsoft.AspNetCore.Mvc;

namespace ShiftManager.ViewComponents;

/// <summary>
/// Loading skeleton component for content placeholders.
/// Implements B-003 loading states requirements.
/// </summary>
public class LoadingSkeletonViewComponent : ViewComponent
{
    public IViewComponentResult Invoke(string variant = "text", int count = 1, string? width = null, string? height = null)
    {
        return View(new LoadingSkeletonModel
        {
            Variant = variant,
            Count = count,
            Width = width,
            Height = height
        });
    }
}

public class LoadingSkeletonModel
{
    /// <summary>
    /// Skeleton variant: "text", "text-sm", "heading", "avatar", "btn", "card", "row"
    /// </summary>
    public string Variant { get; set; } = "text";

    /// <summary>
    /// Number of skeleton items to render.
    /// </summary>
    public int Count { get; set; } = 1;

    /// <summary>
    /// Optional custom width (CSS value, e.g., "80%", "200px").
    /// </summary>
    public string? Width { get; set; }

    /// <summary>
    /// Optional custom height (CSS value, e.g., "2em", "100px").
    /// </summary>
    public string? Height { get; set; }
}
