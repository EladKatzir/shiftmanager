using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Security.Claims;

namespace ShiftManager.Pages.Api;

/// <summary>
/// API endpoint to check session authentication status
/// Returns 200 with session state if authenticated, 401 if not
/// State can be: "ok" (>30 min remaining), "warning" (≤30 min remaining), or 401 (expired)
/// </summary>
[AllowAnonymous]
public class SessionStatusModel : PageModel
{
    private readonly ILogger<SessionStatusModel> _logger;
    private static readonly TimeSpan WarningThreshold = TimeSpan.FromMinutes(30);

    public SessionStatusModel(ILogger<SessionStatusModel> logger)
    {
        _logger = logger;
    }

    public async Task<IActionResult> OnGet()
    {
        try
        {
            // Prevent caching - we need real-time expiration info
            Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate";
            Response.Headers["Pragma"] = "no-cache";
            Response.Headers["Expires"] = "0";

            // Check if this is an AJAX request
            var isAjax = Request.Headers["X-Requested-With"] == "XMLHttpRequest";
            var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            // Log request details for debugging
            _logger.LogInformation("SessionStatus: Request from {IP}, AJAX: {IsAjax}, Authenticated: {IsAuth}, Cookie present: {HasCookie}",
                clientIp,
                isAjax,
                User.Identity?.IsAuthenticated ?? false,
                Request.Cookies.ContainsKey("shiftmgr.auth"));

            // Check if user is authenticated
            if (!User.Identity?.IsAuthenticated ?? true)
            {
                _logger.LogWarning("SessionStatus: User not authenticated. Cookie count: {CookieCount}, Path: {Path}",
                    Request.Cookies.Count,
                    Request.Path);

                // For non-AJAX requests (direct browser access), redirect to login
                if (!isAjax)
                {
                    _logger.LogInformation("SessionStatus: Non-AJAX unauthenticated request, redirecting to login");
                    return RedirectToPage("/Auth/Login", new { reason = "authRequired" });
                }

                return new JsonResult(new
                {
                    authenticated = false,
                    state = "expired",
                    message = "Session expired"
                })
                {
                    StatusCode = 401
                };
            }

            // Get authentication ticket to access expiration information
            var authResult = await HttpContext.AuthenticateAsync(
                CookieAuthenticationDefaults.AuthenticationScheme);

            if (!authResult.Succeeded || authResult.Properties == null)
            {
                _logger.LogWarning("SessionStatus: AuthenticateAsync failed. Succeeded: {Succeeded}, Failure: {Failure}",
                    authResult.Succeeded,
                    authResult.Failure?.Message ?? "No failure message");
                return new JsonResult(new
                {
                    authenticated = false,
                    state = "expired",
                    message = "Invalid session"
                })
                {
                    StatusCode = 401
                };
            }

            // Get expiration time from authentication properties
            var expiresUtc = authResult.Properties.ExpiresUtc;
            var issuedUtc = authResult.Properties.IssuedUtc;

            // Log cookie expiration info to verify sliding expiration
            _logger.LogInformation("SessionStatus: Auth ticket - IssuedAt: {IssuedAt}, ExpiresAt: {ExpiresAt}, AllowRefresh: {AllowRefresh}",
                issuedUtc?.ToString("yyyy-MM-dd HH:mm:ss") ?? "N/A",
                expiresUtc?.ToString("yyyy-MM-dd HH:mm:ss") ?? "N/A",
                authResult.Properties.AllowRefresh ?? false);

            // Calculate time remaining
            var now = DateTimeOffset.UtcNow;
            var timeRemaining = expiresUtc.HasValue
                ? (expiresUtc.Value - now)
                : TimeSpan.FromDays(7); // Default to 7 days if not set

            // Get user ID
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
            {
                _logger.LogWarning("Session status check: Authenticated but invalid user ID claim");
                return new JsonResult(new
                {
                    authenticated = false,
                    state = "expired",
                    message = "Invalid session"
                })
                {
                    StatusCode = 401
                };
            }

            // Determine state based on time remaining
            string state;
            if (timeRemaining <= TimeSpan.Zero)
            {
                state = "expired";
                _logger.LogWarning("SessionStatus: Session expired for user {UserId}", userId);

                return new JsonResult(new
                {
                    authenticated = false,
                    state = "expired",
                    message = "Session expired"
                })
                {
                    StatusCode = 401
                };
            }
            else if (timeRemaining <= WarningThreshold)
            {
                state = "warning";
                _logger.LogInformation("SessionStatus: Session approaching expiration for user {UserId}: {Minutes} minutes remaining",
                    userId, (int)timeRemaining.TotalMinutes);
            }
            else
            {
                state = "ok";
                _logger.LogDebug("SessionStatus: Session OK for user {UserId}, {Days} days remaining",
                    userId, (int)timeRemaining.TotalDays);
            }

            // Return session information
            // Note: Calling this endpoint triggers sliding expiration automatically
            _logger.LogInformation("SessionStatus: Returning state '{State}' for user {UserId} ({Username}), {Minutes} minutes remaining",
                state, userId, User.Identity?.Name ?? "", (int)timeRemaining.TotalMinutes);

            return new JsonResult(new
            {
                authenticated = true,
                state = state,
                secondsRemaining = (int)timeRemaining.TotalSeconds,
                minutesRemaining = (int)timeRemaining.TotalMinutes,
                userId = userId,
                username = User.Identity?.Name ?? "",
                issuedAt = issuedUtc?.ToUnixTimeSeconds(),
                expiresAt = expiresUtc?.ToUnixTimeSeconds(),
                slidingExpirationTriggered = true // This request extends the session
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SessionStatus: Unexpected error checking session status");
            return new JsonResult(new
            {
                authenticated = false,
                state = "error",
                message = "Error checking session"
            })
            {
                StatusCode = 500
            };
        }
    }
}
