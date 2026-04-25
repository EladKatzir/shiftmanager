using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

[AllowAnonymous]
public class ErrorModel : PageModel
{
    /// <summary>
    /// Request correlation ID surfaced to the user. Prefers W3C Trace Context Activity.Id
    /// (which CorrelationIdMiddleware seeds from X-Correlation-Id) and falls back to
    /// HttpContext.TraceIdentifier so the page always has something to display.
    /// </summary>
    public string RequestId { get; private set; } = string.Empty;

    public void OnGet()
    {
        RequestId = HttpContext.Items["CorrelationId"]?.ToString()
            ?? Activity.Current?.Id
            ?? HttpContext.TraceIdentifier;
    }
}
