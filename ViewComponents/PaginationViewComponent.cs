using Microsoft.AspNetCore.Mvc;

namespace ShiftManager.ViewComponents;

/// <summary>
/// Table pagination component with page numbers, navigation, and per-page selector.
/// Implements B-009 pagination controls requirements.
///
/// Features:
/// - "Showing X-Y of Z" info always visible
/// - Page numbers: first, last, current ±1 with ellipsis
/// - Prev/Next disabled at boundaries
/// - Per-page dropdown
/// - Localized text
/// - RTL support
/// </summary>
public class PaginationViewComponent : ViewComponent
{
    public IViewComponentResult Invoke(
        int currentPage,
        int totalPages,
        int totalItems,
        int pageSize,
        string? baseUrl = null,
        int[]? pageSizeOptions = null,
        string? queryStringKey = null)
    {
        var model = new PaginationModel
        {
            CurrentPage = Math.Max(1, currentPage),
            TotalPages = Math.Max(1, totalPages),
            TotalItems = Math.Max(0, totalItems),
            PageSize = pageSize,
            BaseUrl = baseUrl ?? HttpContext.Request.Path.Value ?? "",
            PageSizeOptions = pageSizeOptions ?? new[] { 10, 25, 50, 100 },
            QueryStringKey = queryStringKey ?? "page"
        };

        // Preserve existing query string parameters (except page/pageSize)
        var existingQuery = HttpContext.Request.Query
            .Where(q => q.Key != model.QueryStringKey && q.Key != "pageSize")
            .ToDictionary(q => q.Key, q => q.Value.ToString());
        model.ExistingQueryParams = existingQuery;

        return View(model);
    }
}

public class PaginationModel
{
    /// <summary>
    /// Current page number (1-based)
    /// </summary>
    public int CurrentPage { get; set; }

    /// <summary>
    /// Total number of pages
    /// </summary>
    public int TotalPages { get; set; }

    /// <summary>
    /// Total number of items across all pages
    /// </summary>
    public int TotalItems { get; set; }

    /// <summary>
    /// Number of items per page
    /// </summary>
    public int PageSize { get; set; }

    /// <summary>
    /// Base URL for pagination links (without query string)
    /// </summary>
    public string BaseUrl { get; set; } = "";

    /// <summary>
    /// Available page size options for the dropdown
    /// </summary>
    public int[] PageSizeOptions { get; set; } = { 10, 25, 50, 100 };

    /// <summary>
    /// Query string parameter name for the page number
    /// </summary>
    public string QueryStringKey { get; set; } = "page";

    /// <summary>
    /// Existing query parameters to preserve
    /// </summary>
    public Dictionary<string, string> ExistingQueryParams { get; set; } = new();

    /// <summary>
    /// First item number on current page (1-based)
    /// </summary>
    public int StartItem => TotalItems == 0 ? 0 : (CurrentPage - 1) * PageSize + 1;

    /// <summary>
    /// Last item number on current page
    /// </summary>
    public int EndItem => Math.Min(CurrentPage * PageSize, TotalItems);

    /// <summary>
    /// Whether there is a previous page
    /// </summary>
    public bool HasPreviousPage => CurrentPage > 1;

    /// <summary>
    /// Whether there is a next page
    /// </summary>
    public bool HasNextPage => CurrentPage < TotalPages;

    /// <summary>
    /// Gets the page numbers to display with ellipsis logic.
    /// Shows: first, last, current ±1
    /// </summary>
    public IEnumerable<PageItem> GetPageItems()
    {
        var items = new List<PageItem>();
        var pages = new HashSet<int>();

        // Always show first page
        pages.Add(1);

        // Show pages around current (current - 1, current, current + 1)
        for (int i = Math.Max(2, CurrentPage - 1); i <= Math.Min(TotalPages - 1, CurrentPage + 1); i++)
        {
            pages.Add(i);
        }

        // Always show last page if more than 1 page
        if (TotalPages > 1)
        {
            pages.Add(TotalPages);
        }

        var sortedPages = pages.OrderBy(p => p).ToList();
        int? lastPage = null;

        foreach (var page in sortedPages)
        {
            // Add ellipsis if there's a gap
            if (lastPage.HasValue && page - lastPage > 1)
            {
                items.Add(new PageItem { IsEllipsis = true });
            }

            items.Add(new PageItem
            {
                PageNumber = page,
                IsActive = page == CurrentPage,
                IsEllipsis = false
            });

            lastPage = page;
        }

        return items;
    }

    /// <summary>
    /// Builds a URL for a specific page
    /// </summary>
    public string GetPageUrl(int pageNumber)
    {
        var queryParams = new List<string>();

        // Add existing query params
        foreach (var param in ExistingQueryParams)
        {
            queryParams.Add($"{Uri.EscapeDataString(param.Key)}={Uri.EscapeDataString(param.Value)}");
        }

        // Add page parameter
        queryParams.Add($"{Uri.EscapeDataString(QueryStringKey)}={pageNumber}");

        // Add pageSize if not default
        if (PageSize != 10)
        {
            queryParams.Add($"pageSize={PageSize}");
        }

        var queryString = string.Join("&", queryParams);
        return string.IsNullOrEmpty(queryString) ? BaseUrl : $"{BaseUrl}?{queryString}";
    }

    /// <summary>
    /// Builds a URL for changing page size (resets to page 1)
    /// </summary>
    public string GetPageSizeUrl(int newPageSize)
    {
        var queryParams = new List<string>();

        // Add existing query params
        foreach (var param in ExistingQueryParams)
        {
            queryParams.Add($"{Uri.EscapeDataString(param.Key)}={Uri.EscapeDataString(param.Value)}");
        }

        // Reset to page 1 when changing page size
        queryParams.Add($"{Uri.EscapeDataString(QueryStringKey)}=1");
        queryParams.Add($"pageSize={newPageSize}");

        var queryString = string.Join("&", queryParams);
        return string.IsNullOrEmpty(queryString) ? BaseUrl : $"{BaseUrl}?{queryString}";
    }
}

/// <summary>
/// Represents a single page item (either a page number or ellipsis)
/// </summary>
public class PageItem
{
    public int PageNumber { get; set; }
    public bool IsActive { get; set; }
    public bool IsEllipsis { get; set; }
}
