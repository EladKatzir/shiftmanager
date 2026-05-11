using System.Security.Claims;
using ShiftManager.Models;
using ShiftManager.Services;

namespace ShiftManager.Middleware;

/// <summary>
/// Middleware that authenticates requests using Griffin ADFS tokens.
/// Runs BEFORE CompanyContextMiddleware and UseAuthentication.
/// </summary>
public class GriffinAuthenticationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GriffinAuthenticationMiddleware> _logger;

    public GriffinAuthenticationMiddleware(
        RequestDelegate next,
        ILogger<GriffinAuthenticationMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(
        HttpContext context,
        IGriffinConfigService griffinConfigService,
        IGriffinService griffinService)
    {
        // 1. Skip anonymous paths
        if (IsAnonymousPath(context.Request.Path))
        {
            await _next(context);
            return;
        }

        // 2. Try to load Griffin config
        // Note: CompanyId may not be available yet at this stage
        // Try to extract from existing auth cookie if present
        GriffinConfig? griffinConfig = null;
        try
        {
            griffinConfig = await TryGetGriffinConfigAsync(context, griffinConfigService);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Unable to load Griffin config at middleware stage");
        }

        // 3. Skip if Griffin disabled or config unavailable
        if (griffinConfig?.Enabled != true)
        {
            await _next(context);
            return;
        }

        // 4. Extract Griffin token from cookie
        var token = context.Request.Cookies["griffin.token"];
        if (string.IsNullOrEmpty(token))
        {
            await _next(context);
            return;
        }

        // 5. Authenticate user
        try
        {
            var ipAddress = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var authResult = await griffinService.AuthenticateUserAsync(token, griffinConfig, ipAddress);

            if (authResult.Success && authResult.Value != null)
            {
                context.User = authResult.Value;
                _logger.LogDebug("Griffin authentication successful for user {UserId}",
                    authResult.Value.FindFirst(ClaimTypes.NameIdentifier)?.Value);
            }
            else
            {
                // Invalid / expired / otherwise unusable token — clear the cookie so the
                // next request falls back to the anonymous flow and the user gets bounced
                // to /Auth/Login instead of looping on a dead token.
                context.Response.Cookies.Delete("griffin.token");
                _logger.LogWarning("Griffin token rejected [{ErrorToken}]: {Detail}. Cookie cleared.",
                    authResult.Error?.ErrorToken ?? "GRIFFIN-UNKNOWN",
                    authResult.Error?.TechnicalDetail ?? "(no error detail)");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Griffin authentication threw unexpectedly");
            context.Response.Cookies.Delete("griffin.token");
        }

        await _next(context);
    }

    private bool IsAnonymousPath(PathString path)
    {
        return path.StartsWithSegments("/Auth/Login") ||
               path.StartsWithSegments("/Auth/Signup") ||
               path.StartsWithSegments("/Auth/GriffinCallback") ||
               path.StartsWithSegments("/Auth/GriffinSignup") ||
               // /GriffinDiagnostic is gated by Grant:AdminAccess separately; running this
               // middleware on every diagnostic page load would trigger a live Griffin API call
               // (AuthenticateUserAsync) per request — wasteful and confusing when an admin is
               // logged in via password/cookie auth. The diagnostic page does its own Griffin
               // testing in OnGetAsync; the middleware shouldn't compete with it.
               path.StartsWithSegments("/GriffinDiagnostic") ||
               path.StartsWithSegments("/health") ||
               path.StartsWithSegments("/ready") ||
               path.StartsWithSegments("/AccessDenied");
    }

    private async Task<GriffinConfig?> TryGetGriffinConfigAsync(
        HttpContext context,
        IGriffinConfigService griffinConfigService)
    {
        // Try to get CompanyId from existing auth cookie
        // If user already authenticated via local auth, this will work
        var companyIdClaim = context.User?.FindFirst("CompanyId")?.Value;
        if (!string.IsNullOrEmpty(companyIdClaim) && int.TryParse(companyIdClaim, out var companyId))
        {
            return await griffinConfigService.GetGriffinConfigByCompanyIdAsync(companyId);
        }

        // Otherwise, try tenant resolver (may fail if no context yet)
        try
        {
            return await griffinConfigService.GetGriffinConfigAsync();
        }
        catch
        {
            // If tenant resolver fails, we can't determine company yet
            // This is expected for unauthenticated requests
            return null;
        }
    }
}
