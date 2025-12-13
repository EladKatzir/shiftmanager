using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Services;
using System.Security.Claims;
using Microsoft.Extensions.Logging;

namespace ShiftManager.Pages.Auth;

public class LoginModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly ILogger<LoginModel> _logger;
    private readonly IRateLimitingService _rateLimiting;
    private readonly IValidationService _validation;
    private readonly IGriffinConfigService _griffinConfigService;
    private readonly IGriffinService _griffinService;

    public LoginModel(
        AppDbContext db,
        ILogger<LoginModel> logger,
        IRateLimitingService rateLimiting,
        IValidationService validation,
        IGriffinConfigService griffinConfigService,
        IGriffinService griffinService)
    {
        _db = db;
        _logger = logger;
        _rateLimiting = rateLimiting;
        _validation = validation;
        _griffinConfigService = griffinConfigService;
        _griffinService = griffinService;
    }

    [BindProperty] public string Email { get; set; } = string.Empty;
    [BindProperty] public string Password { get; set; } = string.Empty;
    public string? Error { get; set; }
    public bool ShowAuthPrompt { get; set; }
    public bool ShowGriffinButton { get; set; }
    public bool ShowGriffinUnavailableMessage { get; set; }
    public string? ReturnUrl { get; set; }

    public async Task OnGetAsync(string? reason = null, string? returnUrl = null)
    {
        ReturnUrl = returnUrl ?? "/Home/Index";

        // ✅ PHASE 18: Show auth required prompt if user was redirected due to unauthorized access
        ShowAuthPrompt = reason == "authRequired";

        // Always show Griffin ADFS button
        ShowGriffinButton = true;

        // Check Griffin availability
        var griffinConfig = await _griffinConfigService.GetGriffinConfigAsync();

        if (griffinConfig?.Enabled != true)
        {
            ShowGriffinUnavailableMessage = true;
        }
        else
        {
            var isAvailable = await _griffinConfigService.TestConnectionAsync(
                griffinConfig.BaseUrl!, griffinConfig.TimeoutSeconds);

            if (!isAvailable)
            {
                ShowGriffinUnavailableMessage = true;
                _logger.LogWarning("Griffin ADFS unavailable");
            }
        }

        // ✅ SUB-PHASE 18.14: Prevent browser caching to ensure link renders correctly
        Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate, max-age=0";
        Response.Headers["Pragma"] = "no-cache";
        Response.Headers["Expires"] = "0";
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        try
        {
            // ✅ SECURITY FIX: Rate limiting (10 attempts per 15 minutes per IP)
            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var rateLimitKey = $"login:{ipAddress}";

            if (!_rateLimiting.IsAllowed(rateLimitKey, 10, 15))
            {
                _logger.LogWarning("Rate limit exceeded for login from IP: {IP}", ipAddress);
                Error = "Too many login attempts. Please try again in 15 minutes.";
                return Page();
            }

            // ✅ SECURITY FIX: Input validation
            if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(Password))
            {
                Error = "Email and password are required.";
                return Page();
            }

            if (Email.Length > 255 || Password.Length > 500)
            {
                _logger.LogWarning("Login attempt with oversized input from IP {IP}", ipAddress);
                Error = "Invalid input.";
                return Page();
            }

            // ✅ SECURITY FIX: Proper email format validation with regex
            if (!_validation.IsValidEmail(Email))
            {
                Error = "Invalid email format.";
                return Page();
            }

            var user = await _db.Users
                .IgnoreQueryFilters() // Allow login across all companies
                .FirstOrDefaultAsync(u => u.Email == Email && u.IsActive);

            // ✅ SECURITY FIX: Account lockout protection
            if (user != null)
            {
                // Check if account is locked out
                if (user.LockoutEnd.HasValue && user.LockoutEnd.Value > DateTime.UtcNow)
                {
                    var remainingMinutes = (int)(user.LockoutEnd.Value - DateTime.UtcNow).TotalMinutes + 1;
                    _logger.LogWarning("Login attempt for locked account: {Email}. Lockout ends in {Minutes} minutes", Email, remainingMinutes);
                    Error = $"Account is locked due to multiple failed login attempts. Please try again in {remainingMinutes} minute(s).";
                    return Page();
                }

                // Record login attempt time
                user.LastLoginAttempt = DateTime.UtcNow;
            }

            // Verify password
            if (user == null || !PasswordHasher.Verify(Password, user.PasswordHash, user.PasswordSalt))
            {
                // ✅ SECURITY FIX: Track failed login attempts
                if (user != null)
                {
                    user.FailedLoginAttempts++;

                    // Lock account after 10 failed attempts
                    if (user.FailedLoginAttempts >= 10)
                    {
                        user.LockoutEnd = DateTime.UtcNow.AddMinutes(3);
                        await _db.SaveChangesAsync();

                        _logger.LogWarning("Account locked for {Email} after {Attempts} failed attempts", Email, user.FailedLoginAttempts);
                        Error = "Account has been locked due to multiple failed login attempts. Please try again in 3 minutes.";
                        return Page();
                    }

                    await _db.SaveChangesAsync();
                    _logger.LogWarning("Login failed for {Email} (attempt {Attempt}/10)", Email, user.FailedLoginAttempts);
                }
                else
                {
                    _logger.LogWarning("Login failed for {Email} (user not found)", Email);
                }

                Error = "Invalid credentials.";
                return Page();
            }

            // ✅ SECURITY FIX: Reset failed attempts and rate limit on successful login
            user.FailedLoginAttempts = 0;
            user.LockoutEnd = null;
            await _db.SaveChangesAsync();

            // Reset rate limit for this IP after successful login
            _rateLimiting.Reset(rateLimitKey);

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.DisplayName),
                new Claim(ClaimTypes.Role, user.Role.ToString()),
                new Claim("CompanyId", user.CompanyId.ToString())
            };
            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));

            _logger.LogInformation("User {UserId} ({Email}) signed in successfully. Role={Role}", user.Id, user.Email, user.Role);

            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                return Redirect(returnUrl);

            // ✅ PHASE 18: Redirect to Home/Index dashboard for all users after login
            return RedirectToPage("/Home/Index");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception during login for {Email}", Email);
            Error = "Unexpected error. Please try again.";
            return Page();
        }
    }

    public async Task<IActionResult> OnPostGriffinAsync(string? returnUrl = null)
    {
        var griffinConfig = await _griffinConfigService.GetGriffinConfigAsync();

        if (griffinConfig?.Enabled != true)
        {
            Error = "ADFS authentication is not configured. Please use local login or contact your administrator.";
            ReturnUrl = returnUrl ?? "/Home/Index";
            ShowGriffinButton = true;
            ShowGriffinUnavailableMessage = true;
            await OnGetAsync(returnUrl: returnUrl);
            return Page();
        }

        // Build callback URL
        var callbackUrl = $"{Request.Scheme}://{Request.Host}/Auth/GriffinCallback";
        if (!string.IsNullOrEmpty(returnUrl))
        {
            callbackUrl += $"?returnUrl={Uri.EscapeDataString(returnUrl)}";
        }

        // Build authentication URL
        var authUrl = _griffinService.BuildAuthenticationUrl(griffinConfig.BaseUrl!, callbackUrl);

        // Redirect to Griffin
        return Redirect(authUrl);
    }
}
