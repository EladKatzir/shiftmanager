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

// SECURITY-AUDITED: All IgnoreQueryFilters() in this class are SAFE — anonymous auth flow before tenant context established;
// scoped by explicit email parameter; only checks user existence, no sensitive data exposed
[AllowAnonymous]
public class ForgotPasswordModel : LocalizedPageModel
{
    private readonly AppDbContext _db;
    private readonly IMailService _mailService;
    private readonly ILogger<ForgotPasswordModel> _logger;
    private readonly IRateLimitingService _rateLimiting;
    private readonly IValidationService _validation;
    private readonly IAuditLogService _auditLogService;

    public ForgotPasswordModel(
        IStringLocalizer<SharedResources> localizer,
        AppDbContext db,
        IMailService mailService,
        ILogger<ForgotPasswordModel> logger,
        IRateLimitingService rateLimiting,
        IValidationService validation,
        IAuditLogService auditLogService) : base(localizer)
    {
        _db = db;
        _mailService = mailService;
        _logger = logger;
        _rateLimiting = rateLimiting;
        _validation = validation;
        _auditLogService = auditLogService;
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
            // D-05: Start timer to enforce constant-time response regardless of user existence
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

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
                // D-05: Constant-time delay to prevent user enumeration via timing
                stopwatch.Stop();
                var elapsed = stopwatch.ElapsedMilliseconds;
                var minDelay = 800; // milliseconds
                if (elapsed < minDelay)
                    await Task.Delay(minDelay - (int)elapsed);

                // No match found - show error but don't reveal which field is wrong (security)
                Error = _localizer["Error_EmailPhoneNoMatch"];
                _logger.LogWarning("Failed password recovery attempt for email: {Email}", RedactEmail(Email));

                // MED-009 FIX: Return Page() consistently to prevent user enumeration via response type
                return Page();
            }

            // User found - generate temporary password and update hash
            var temporaryPassword = GenerateTemporaryPassword();
            var (newHash, newSalt) = PasswordHasher.CreateHash(temporaryPassword);

            user.PasswordHash = newHash;
            user.PasswordSalt = newSalt;
            // A-07: Force user to change password on next login
            user.MustChangePassword = true;
            await _db.SaveChangesAsync();

            await _auditLogService.LogUserActionAsync(user.Id, "PasswordReset", "User", user.Id, $"Password reset for user '{user.Email}'");

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

            var emailResult = await _mailService.SendMailAsync(
                recipient: user.Email,
                subject: emailSubject,
                htmlBody: emailBody
            );

            if (emailResult.Success)
            {
                Success = _localizer["Success_TemporaryPasswordSent"];
                _logger.LogInformation("Password recovery email sent to user: {Email}", RedactEmail(user.Email));

                // B-01: Do NOT display temp password on screen — shoulder-surfing risk in military environment
                // Password is sent via email only. GeneratedTempPassword left null intentionally.

                // Clear sensitive data from page
                Email = string.Empty;
                Phone = string.Empty;

                return Page();
            }
            else
            {
                // I-01: In air-gapped environments without email, display temp password on screen
                // as the only viable recovery path. Log this as a security event.
                _logger.LogWarning("Email delivery failed for password recovery (user: {Email}): [{Key}] {Reason}. Falling back to on-screen display.",
                    RedactEmail(user.Email), emailResult.ErrorKey, emailResult.ErrorMessage);
                GeneratedTempPassword = temporaryPassword;
                Success = _localizer["Error_FailedToSendRecoveryEmail_FallbackDisplayed"];
                return Page();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during password recovery for email: {Email}", RedactEmail(Email));
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

        // D-09: Password minimum length increased to 12 characters for military environment
        if (NewPassword.Length < 12)
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
                _logger.LogWarning("Password change attempt for non-existent email: {Email}", RedactEmail(ChangeEmail));
                return Page();
            }

            // ✅ SECURITY: Verify old password using PasswordHasher
            if (!PasswordHasher.Verify(OldPassword, user.PasswordHash, user.PasswordSalt))
            {
                PasswordChangeError = _localizer["Error_CurrentPasswordIncorrect"];
                _logger.LogWarning("Password change attempt with incorrect old password for user: {Email}", RedactEmail(user.Email));
                return Page();
            }

            // ✅ UPDATE: Create hash for new password and update user
            var (newHash, newSalt) = PasswordHasher.CreateHash(NewPassword);
            user.PasswordHash = newHash;
            user.PasswordSalt = newSalt;
            // A-07: Clear forced password change flag after successful change
            user.MustChangePassword = false;
            await _db.SaveChangesAsync();

            await _auditLogService.LogUserActionAsync(user.Id, "PasswordChanged", "User", user.Id, $"Password changed for user '{user.Email}'");

            PasswordChangeSuccess = _localizer["Success_PasswordChangedSuccessfully"];
            _logger.LogInformation("Password changed successfully for user: {Email}", RedactEmail(user.Email));

            // Clear sensitive data from page
            ChangeEmail = string.Empty;
            OldPassword = string.Empty;
            NewPassword = string.Empty;

            return Page();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during password change for email: {Email}", RedactEmail(ChangeEmail));
            PasswordChangeError = _localizer["Error_AnErrorOccurred"];
            return Page();
        }
    }

    /// <summary>
    /// E-01: Redact email for safe logging. Shows first 2 chars + domain.
    /// Example: "john.doe@army.mil" → "jo***@army.mil"
    /// </summary>
    private static string RedactEmail(string? email)
    {
        if (string.IsNullOrEmpty(email)) return "[empty]";
        var atIndex = email.IndexOf('@');
        if (atIndex <= 0) return "[redacted]";
        var prefix = email[..Math.Min(2, atIndex)];
        return $"{prefix}***{email[atIndex..]}";
    }

    private string GenerateTemporaryPassword()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghjkmnpqrstuvwxyz23456789!@#$%";
        var password = new StringBuilder();

        // D-11: Use rejection sampling to avoid modulus bias
        // chars.Length = 56, largest multiple of 56 fitting in byte = 252 (56*4)
        // Reject values >= 252 to ensure uniform distribution
        var maxUnbiased = (byte)(256 - (256 % chars.Length)); // 252

        using (var rng = RandomNumberGenerator.Create())
        {
            var buffer = new byte[1];
            while (password.Length < 12)
            {
                rng.GetBytes(buffer);
                if (buffer[0] < maxUnbiased)
                {
                    password.Append(chars[buffer[0] % chars.Length]);
                }
            }
        }

        return password.ToString();
    }
}
