using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Services;
using System.Security.Cryptography;
using System.Text;

namespace ShiftManager.Pages.Auth;

[AllowAnonymous]
public class ForgotPasswordModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IMailService _mailService;
    private readonly ILogger<ForgotPasswordModel> _logger;
    private readonly IRateLimitingService _rateLimiting;
    private readonly IValidationService _validation;

    public ForgotPasswordModel(
        AppDbContext db,
        IMailService mailService,
        ILogger<ForgotPasswordModel> logger,
        IRateLimitingService rateLimiting,
        IValidationService validation)
    {
        _db = db;
        _mailService = mailService;
        _logger = logger;
        _rateLimiting = rateLimiting;
        _validation = validation;
    }

    [BindProperty]
    public string Email { get; set; } = string.Empty;

    [BindProperty]
    public string Phone { get; set; } = string.Empty;

    public string? SuccessMessage { get; set; }
    public string? ErrorMessage { get; set; }

    // ✅ PHASE 18: Store generated temp password for one-time display (not persisted)
    public string? GeneratedTempPassword { get; set; }

    // ✅ SUB-PHASE 18.14: Password change properties
    [BindProperty]
    public string ChangeEmail { get; set; } = string.Empty;

    [BindProperty]
    public string OldPassword { get; set; } = string.Empty;

    [BindProperty]
    public string NewPassword { get; set; } = string.Empty;

    public string? PasswordChangeSuccess { get; set; }
    public string? PasswordChangeError { get; set; }

    public void OnGet()
    {
        // ✅ PHASE 18: Set no-cache headers to prevent password from being cached
        Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate, max-age=0";
        Response.Headers["Pragma"] = "no-cache";
        Response.Headers["Expires"] = "0";
    }

    public async Task<IActionResult> OnPostAsync()
    {
        // ✅ SECURITY FIX: Rate limiting (3 attempts per 15 minutes per IP)
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var rateLimitKey = $"forgot-password:{ipAddress}";

        if (!_rateLimiting.IsAllowed(rateLimitKey, 3, 15))
        {
            _logger.LogWarning("Rate limit exceeded for password reset from IP: {IP}", ipAddress);
            ErrorMessage = "Too many password reset attempts. Please try again in 15 minutes.";
            return Page();
        }

        // ✅ SECURITY FIX: Input validation
        if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(Phone))
        {
            ErrorMessage = "Please provide both email and phone number.";
            return Page();
        }

        if (Email.Length > 255 || Phone.Length > 50)
        {
            _logger.LogWarning("Password reset attempt with oversized input from IP: {IP}", ipAddress);
            ErrorMessage = "Invalid input.";
            return Page();
        }

        // ✅ SECURITY FIX: Proper email format validation with regex
        if (!_validation.IsValidEmail(Email))
        {
            ErrorMessage = "Invalid email format.";
            return Page();
        }

        // ✅ SECURITY FIX: Proper phone format validation with regex
        if (!_validation.IsValidPhone(Phone))
        {
            ErrorMessage = "Invalid phone format.";
            return Page();
        }

        try
        {
            // Find user by email and phone number match (case-insensitive for email)
            var user = await _db.Users
                .IgnoreQueryFilters() // Allow searching across all companies
                .FirstOrDefaultAsync(u =>
                    u.Email.ToLower() == Email.ToLower() &&
                    u.Phone != null &&
                    u.Phone == Phone);

            if (user == null)
            {
                // No match found - show error but don't reveal which field is wrong (security)
                ErrorMessage = "Email and phone number do not match our records.";
                _logger.LogWarning("Failed password recovery attempt for email: {Email}", Email);

                return new JsonResult(new
                {
                    status = "error",
                    message = "Email and phone number do not match our records."
                });
            }

            // User found - generate temporary password and update hash
            var temporaryPassword = GenerateTemporaryPassword();
            var (newHash, newSalt) = PasswordHasher.CreateHash(temporaryPassword);

            user.PasswordHash = newHash;
            user.PasswordSalt = newSalt;
            await _db.SaveChangesAsync();

            // Send temporary password via email
            var emailBody = $@"
<h2>Password Recovery Request</h2>
<p>Hello {user.DisplayName},</p>
<p>You requested to recover your password for the Shift Manager system.</p>
<p><strong>Your temporary password is:</strong> <code style='background: #f4f4f4; padding: 4px 8px; border-radius: 4px;'>{temporaryPassword}</code></p>
<p><strong>IMPORTANT:</strong> This is a temporary password. Please log in and change it immediately in your profile settings.</p>
<p>If you did not request this, please contact your system administrator immediately.</p>
<br/>
<p>Best regards,<br/>Shift Manager Team</p>
";

            var emailSent = await _mailService.SendMailAsync(
                recipient: user.Email,
                subject: "Password Recovery - Shift Manager",
                htmlBody: emailBody
            );

            if (emailSent)
            {
                SuccessMessage = "A temporary password has been sent to your registered email address.";
                _logger.LogInformation("Password recovery email sent to user: {Email}", user.Email);

                // ✅ PHASE 18: Store temp password for one-time display (not logged, not persisted)
                GeneratedTempPassword = temporaryPassword;

                // ✅ PHASE 18: Set no-cache headers to prevent password from being cached
                Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate, max-age=0";
                Response.Headers["Pragma"] = "no-cache";
                Response.Headers["Expires"] = "0";

                // Clear sensitive data from page
                Email = string.Empty;
                Phone = string.Empty;

                return Page();
            }
            else
            {
                ErrorMessage = "Failed to send recovery email. Please try again later or contact support.";
                _logger.LogError("Failed to send password recovery email for user: {Email}", user.Email);
                return Page();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during password recovery for email: {Email}", Email);
            ErrorMessage = "An error occurred. Please try again later.";
            return Page();
        }
    }

    // ✅ SUB-PHASE 18.14: Password change handler (requires email, old password, new password)
    public async Task<IActionResult> OnPostChangePasswordAsync()
    {
        // ✅ SECURITY: Rate limiting (5 attempts per 15 minutes per IP)
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var rateLimitKey = $"change-password:{ipAddress}";

        if (!_rateLimiting.IsAllowed(rateLimitKey, 5, 15))
        {
            _logger.LogWarning("Rate limit exceeded for password change from IP: {IP}", ipAddress);
            PasswordChangeError = "Too many password change attempts. Please try again in 15 minutes.";
            return Page();
        }

        // ✅ VALIDATION: Input validation
        if (string.IsNullOrWhiteSpace(ChangeEmail) || string.IsNullOrWhiteSpace(OldPassword) || string.IsNullOrWhiteSpace(NewPassword))
        {
            PasswordChangeError = "Please provide email, current password, and new password.";
            return Page();
        }

        // ✅ VALIDATION: Email format validation
        if (!_validation.IsValidEmail(ChangeEmail))
        {
            PasswordChangeError = "Invalid email format.";
            return Page();
        }

        // ✅ VALIDATION: New password length check
        if (NewPassword.Length < 6)
        {
            PasswordChangeError = "New password must be at least 6 characters long.";
            return Page();
        }

        try
        {
            // Find user by email (case-insensitive)
            var user = await _db.Users
                .IgnoreQueryFilters() // Allow searching across all companies
                .FirstOrDefaultAsync(u => u.Email.ToLower() == ChangeEmail.ToLower());

            if (user == null)
            {
                // Don't reveal if user exists or not (security)
                PasswordChangeError = "Failed to change password. Please check your current password and try again.";
                _logger.LogWarning("Password change attempt for non-existent email: {Email}", ChangeEmail);
                return Page();
            }

            // ✅ SECURITY: Verify old password using PasswordHasher
            if (!PasswordHasher.Verify(OldPassword, user.PasswordHash, user.PasswordSalt))
            {
                PasswordChangeError = "Current password is incorrect.";
                _logger.LogWarning("Password change attempt with incorrect old password for user: {Email}", user.Email);
                return Page();
            }

            // ✅ UPDATE: Create hash for new password and update user
            var (newHash, newSalt) = PasswordHasher.CreateHash(NewPassword);
            user.PasswordHash = newHash;
            user.PasswordSalt = newSalt;
            await _db.SaveChangesAsync();

            PasswordChangeSuccess = "Password changed successfully! You can now log in with your new password.";
            _logger.LogInformation("Password changed successfully for user: {Email}", user.Email);

            // Clear sensitive data from page
            ChangeEmail = string.Empty;
            OldPassword = string.Empty;
            NewPassword = string.Empty;

            return Page();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during password change for email: {Email}", ChangeEmail);
            PasswordChangeError = "An error occurred. Please try again later.";
            return Page();
        }
    }

    private string GenerateTemporaryPassword()
    {
        // SECURITY FIX: Use cryptographically secure random number generator
        // Previously used System.Random which is NOT cryptographically secure
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghjkmnpqrstuvwxyz23456789!@#$%";
        var password = new StringBuilder();

        // Generate 12-character password with mix of uppercase, lowercase, numbers, and symbols
        using (var rng = RandomNumberGenerator.Create())
        {
            byte[] randomBytes = new byte[12];
            rng.GetBytes(randomBytes);

            for (int i = 0; i < 12; i++)
            {
                password.Append(chars[randomBytes[i] % chars.Length]);
            }
        }

        return password.ToString();
    }
}
