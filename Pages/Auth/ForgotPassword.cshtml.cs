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

    public ForgotPasswordModel(
        AppDbContext db,
        IMailService mailService,
        ILogger<ForgotPasswordModel> logger)
    {
        _db = db;
        _mailService = mailService;
        _logger = logger;
    }

    [BindProperty]
    public string Email { get; set; } = string.Empty;

    [BindProperty]
    public string Phone { get; set; } = string.Empty;

    public string? SuccessMessage { get; set; }
    public string? ErrorMessage { get; set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(Phone))
        {
            ErrorMessage = "Please provide both email and phone number.";
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
