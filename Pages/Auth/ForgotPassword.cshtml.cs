using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Resources;
using ShiftManager.Services;
using System.Security.Cryptography;
using System.Text;

namespace ShiftManager.Pages.Auth;

[AllowAnonymous]
public class ForgotPasswordModel : LocalizedPageModel
{
    private readonly AppDbContext _db;
    private readonly IMailService _mailService;
    private readonly ILogger<ForgotPasswordModel> _logger;
    private readonly IRateLimitingService _rateLimiting;
    private readonly IValidationService _validation;

    public ForgotPasswordModel(
        IStringLocalizer<SharedResources> localizer,
        AppDbContext db,
        IMailService mailService,
        ILogger<ForgotPasswordModel> logger,
        IRateLimitingService rateLimiting,
        IValidationService validation) : base(localizer)
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
            Error = _localizer["Error_TooManyPasswordResetAttempts"];
            return Page();
        }

        // ✅ SECURITY FIX: Input validation
        if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(Phone))
        {
            Error = _localizer["Error_ProvideEmailAndPhone"];
            return Page();
        }

        if (Email.Length > 255 || Phone.Length > 50)
        {
            _logger.LogWarning("Password reset attempt with oversized input from IP: {IP}", ipAddress);
            Error = _localizer["Error_InvalidInput"];
            return Page();
        }

        // ✅ SECURITY FIX: Proper email format validation with regex
        if (!_validation.IsValidEmail(Email))
        {
            Error = _localizer["Error_InvalidEmailFormat"];
            return Page();
        }

        // ✅ SECURITY FIX: Proper phone format validation with regex
        if (!_validation.IsValidPhone(Phone))
        {
            Error = _localizer["Error_InvalidPhoneFormat"];
            return Page();
        }

        try
        {
            // SECURITY-AUDITED: SAFE — password recovery must search across all companies
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
                var errorMsg = _localizer["Error_EmailPhoneNoMatch"];
                Error = errorMsg;
                _logger.LogWarning("Failed password recovery attempt for email: {Email}", Email);

                return new JsonResult(new
                {
                    status = "error",
                    message = errorMsg.ToString()
                });
            }

            // User found - generate temporary password and update hash
            var temporaryPassword = GenerateTemporaryPassword();
            var (newHash, newSalt) = PasswordHasher.CreateHash(temporaryPassword);

            user.PasswordHash = newHash;
            user.PasswordSalt = newSalt;
            await _db.SaveChangesAsync();

            // Send temporary password via email
            var emailSubject = _localizer["Email_PasswordRecoverySubject"];
            var emailDir = _localizer["Dir"] == "rtl" ? "rtl" : "ltr";

            var emailBody = $@"
<!DOCTYPE html>
<html dir='{emailDir}'>
<head>
    <meta charset='utf-8'>
    <style>
        body {{ font-family: Arial, sans-serif; padding: 20px; }}
        .code-box {{ background: #f4f4f4; padding: 4px 8px; border-radius: 4px; display: inline-block; }}
    </style>
</head>
<body>
    <h2>{_localizer["Email_PasswordRecoveryTitle"]}</h2>
    <p>{string.Format(_localizer["Email_Hello"], user.DisplayName)},</p>
    <p>{_localizer["Email_PasswordRecoveryBody"]}</p>
    <p><strong>{_localizer["Email_YourTemporaryPassword"]}:</strong> <code class='code-box'>{temporaryPassword}</code></p>
    <p><strong>{_localizer["Important"]}:</strong> {_localizer["Email_PasswordRecoveryImportant"]}</p>
    <p>{_localizer["Email_PasswordRecoverySecurityNote"]}</p>
    <br/>
    <p>{_localizer["Email_BestRegards"]},<br/>{_localizer["Email_ShiftManagerTeam"]}</p>
</body>
</html>";

            var emailSent = await _mailService.SendMailAsync(
                recipient: user.Email,
                subject: emailSubject,
                htmlBody: emailBody
            );

            if (emailSent)
            {
                Success = _localizer["Success_TemporaryPasswordSent"];
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
                Error = _localizer["Error_FailedToSendRecoveryEmail"];
                _logger.LogError("Failed to send password recovery email for user: {Email}", user.Email);
                return Page();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during password recovery for email: {Email}", Email);
            Error = _localizer["Error_AnErrorOccurred"];
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
            PasswordChangeError = _localizer["Error_TooManyPasswordChangeAttempts"];
            return Page();
        }

        // ✅ VALIDATION: Input validation
        if (string.IsNullOrWhiteSpace(ChangeEmail) || string.IsNullOrWhiteSpace(OldPassword) || string.IsNullOrWhiteSpace(NewPassword))
        {
            PasswordChangeError = _localizer["Error_ProvideEmailCurrentAndNewPassword"];
            return Page();
        }

        // ✅ VALIDATION: Email format validation
        if (!_validation.IsValidEmail(ChangeEmail))
        {
            PasswordChangeError = _localizer["Error_InvalidEmailFormat"];
            return Page();
        }

        // ✅ VALIDATION: New password length check
        if (NewPassword.Length < 6)
        {
            PasswordChangeError = _localizer["Error_NewPasswordTooShort"];
            return Page();
        }

        try
        {
            // SECURITY-AUDITED: SAFE — password change must search across all companies
            // Find user by email (case-insensitive)
            var user = await _db.Users
                .IgnoreQueryFilters() // Allow searching across all companies
                .FirstOrDefaultAsync(u => u.Email.ToLower() == ChangeEmail.ToLower());

            if (user == null)
            {
                // Don't reveal if user exists or not (security)
                PasswordChangeError = _localizer["Error_FailedToChangePassword"];
                _logger.LogWarning("Password change attempt for non-existent email: {Email}", ChangeEmail);
                return Page();
            }

            // ✅ SECURITY: Verify old password using PasswordHasher
            if (!PasswordHasher.Verify(OldPassword, user.PasswordHash, user.PasswordSalt))
            {
                PasswordChangeError = _localizer["Error_CurrentPasswordIncorrect"];
                _logger.LogWarning("Password change attempt with incorrect old password for user: {Email}", user.Email);
                return Page();
            }

            // ✅ UPDATE: Create hash for new password and update user
            var (newHash, newSalt) = PasswordHasher.CreateHash(NewPassword);
            user.PasswordHash = newHash;
            user.PasswordSalt = newSalt;
            await _db.SaveChangesAsync();

            PasswordChangeSuccess = _localizer["Success_PasswordChangedSuccessfully"];
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
            PasswordChangeError = _localizer["Error_AnErrorOccurred"];
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
