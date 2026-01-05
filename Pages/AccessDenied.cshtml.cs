using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ShiftManager.Pages;

/// <summary>
/// Phase 8.5: Fixed infinite redirect loop by expanding protected paths and removing query parameters
/// </summary>
public class AccessDeniedModel : PageModel
{
    private readonly ILogger<AccessDeniedModel> _logger;

    public AccessDeniedModel(ILogger<AccessDeniedModel> logger)
    {
        _logger = logger;
    }

    public string ReturnUrl { get; set; } = "/Calendar/Month";

    public IActionResult OnGet(string? returnUrl = null)
    {
        // Use the referring page or default to calendar
        var originalUrl = returnUrl ?? Request.Headers["Referer"].FirstOrDefault() ?? "/Calendar/Month";

        _logger.LogInformation("Access Denied - Original URL: {OriginalUrl}", originalUrl);

        // Clean up the URL by removing query parameters first
        var cleanUrl = CleanUrl(originalUrl);

        // Check if the URL is a protected path that should redirect to default
        if (IsProtectedPath(cleanUrl))
        {
            _logger.LogInformation("Protected path detected, redirecting to default: {CleanUrl}", cleanUrl);
            ReturnUrl = "/Calendar/Month";
        }
        else
        {
            ReturnUrl = cleanUrl;
        }

        _logger.LogInformation("Access Denied - Final ReturnUrl: {ReturnUrl}", ReturnUrl);

        // Set HTTP 403 Forbidden status code for proper RESTful semantics
        Response.StatusCode = 403;
        return Page();
    }

    /// <summary>
    /// Cleans URL by removing query parameters and ensuring proper format
    /// </summary>
    private string CleanUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return "/Calendar/Month";

        try
        {
            // Remove query parameters
            var uri = new Uri(url, UriKind.RelativeOrAbsolute);

            if (uri.IsAbsoluteUri)
            {
                // Extract just the path without query string
                return uri.PathAndQuery.Split('?')[0];
            }
            else
            {
                // For relative URLs, just remove everything after ?
                return url.Split('?')[0];
            }
        }
        catch
        {
            // If URI parsing fails, fall back to simple string split
            return url.Split('?')[0];
        }
    }

    /// <summary>
    /// Phase 8.5: Expanded list of protected paths that should not be used as return URLs
    /// </summary>
    private bool IsProtectedPath(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        var protectedPaths = new[]
        {
            "/AccessDenied",
            "/Admin/",
            "/Owner/",
            "/Director/",
            "/Assignments/",
            "/Requests/Index"
        };

        return protectedPaths.Any(path =>
            url.Contains(path, StringComparison.OrdinalIgnoreCase));
    }
}