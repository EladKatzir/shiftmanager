using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace ShiftManager.Middleware;

/// <summary>
/// UI-route counterpart to <see cref="ApiExceptionMiddleware"/>.
///
/// <para>
/// Catches unhandled exceptions on non-/api routes, logs them with the request correlation ID,
/// stores a user-facing message + correlation ID in TempData (consumed by the layout's
/// <c>FeedbackModal</c> bridge on the next request), and 302s back to the same-origin Referer
/// (or <c>/Error</c> if no safe Referer exists).
/// </para>
/// <para>
/// In Development, the raw exception type + message + stack trace is also written to
/// <c>TempData["ErrorDetail"]</c> so the FeedbackModal can render it in its collapsible
/// detail panel. Production never exposes raw exception text.
/// </para>
/// <para>
/// <b>Pipeline ordering matters:</b> this middleware MUST be registered <i>before</i>
/// <see cref="ApiExceptionMiddleware"/>. ASP.NET invokes middleware in registration order
/// (so PageException is the OUTER), but exceptions propagate the other direction (innermost
/// first). This means ApiExc catches API requests and emits JSON; for non-API requests its
/// filter passes through, and the re-thrown exception lands here.
/// </para>
/// </summary>
public class PageExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<PageExceptionMiddleware> _logger;
    private readonly IWebHostEnvironment _env;

    public PageExceptionMiddleware(
        RequestDelegate next,
        ILogger<PageExceptionMiddleware> logger,
        IWebHostEnvironment env)
    {
        _next = next;
        _logger = logger;
        _env = env;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex) when (!IsApiRequest(context.Request.Path))
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception ex)
    {
        var correlationId = context.Items["CorrelationId"]?.ToString() ?? context.TraceIdentifier;

        _logger.LogError(ex,
            "Unhandled exception in UI request. Path={Path}, Method={Method}, CorrelationId={CorrelationId}",
            context.Request.Path,
            context.Request.Method,
            correlationId);

        if (context.Response.HasStarted)
        {
            // Cannot redirect once the response stream has begun. Re-throw so the framework's
            // configured exception handler (UseExceptionHandler / UseDeveloperExceptionPage) runs.
            _logger.LogWarning(
                "Response already started for {Path}; cannot redirect to safe URL. CorrelationId={CorrelationId}",
                context.Request.Path,
                correlationId);
            throw ex;
        }

        try
        {
            var tempDataFactory = context.RequestServices.GetService<ITempDataDictionaryFactory>();
            if (tempDataFactory != null)
            {
                var tempData = tempDataFactory.GetTempData(context);
                // English fallback message; the FeedbackModal layer renders this directly.
                // Phase 2 service-layer migrations populate TempData["ErrorMessage"] with
                // localized strings BEFORE the request would reach this middleware (handled
                // exceptions are converted to OperationResult.Fail at the service boundary).
                // This middleware fires only for genuinely unhandled exceptions.
                tempData["ErrorMessage"] = "An unexpected error occurred. If the problem persists, quote the error ID to support.";
                tempData["ErrorId"] = correlationId;

                if (_env.IsDevelopment())
                {
                    tempData["ErrorDetail"] = $"{ex.GetType().FullName}: {ex.Message}\n\n{ex.StackTrace}";
                }

                tempData.Save();
            }

            // Same-origin Referer is the safest redirect target — it preserves the user's context.
            // If absent or cross-origin, fall back to /Error (the framework's generic page).
            var referer = context.Request.Headers.Referer.ToString();
            string safeRedirect = "/Error";
            if (!string.IsNullOrEmpty(referer)
                && Uri.TryCreate(referer, UriKind.Absolute, out var refererUri)
                && string.Equals(refererUri.Host, context.Request.Host.Host, StringComparison.OrdinalIgnoreCase))
            {
                safeRedirect = referer;
            }

            context.Response.Redirect(safeRedirect);
        }
        catch (Exception inner)
        {
            // If the redirect-with-TempData path itself failed, surface BOTH errors and let the
            // framework default exception handler take over. Re-throwing the original exception
            // preserves its stack for the outer handler.
            _logger.LogError(inner,
                "PageExceptionMiddleware failed to compose redirect for original exception. CorrelationId={CorrelationId}",
                correlationId);
            throw ex;
        }
    }

    private static bool IsApiRequest(PathString path)
    {
        return path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments("/Api", StringComparison.OrdinalIgnoreCase);
    }
}
