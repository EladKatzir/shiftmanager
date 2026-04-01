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
        catch (Exception ex)
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
            var principal = await griffinService.AuthenticateUserAsync(token, griffinConfig, ipAddress);

            if (principal != null)
            {
                context.User = principal;
                _logger.LogDebug("Griffin authentication successful for user {UserId}",
                    principal.FindFirst(ClaimTypes.NameIdentifier)?.Value);
            }
            else
            {
                // Invalid token - clear cookie
                context.Response.Cookies.Delete("griffin.token");
                _logger.LogWarning("Invalid Griffin token, cookie cleared");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Griffin authentication failed");
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
