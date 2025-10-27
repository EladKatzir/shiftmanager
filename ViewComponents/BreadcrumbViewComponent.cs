using Microsoft.AspNetCore.Mvc;

namespace ShiftManager.ViewComponents;

public class BreadcrumbViewComponent : ViewComponent
{
    public IViewComponentResult Invoke(List<BreadcrumbItem> items)
    {
        return View(items);
    }
}

public class BreadcrumbItem
{
    public string Label { get; set; } = string.Empty;
    public string? Url { get; set; }
    public bool IsActive { get; set; }
}
