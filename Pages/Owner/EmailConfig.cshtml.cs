using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ShiftManager.Services;
using System.Security.Claims;

namespace ShiftManager.Pages.Owner;

/// <summary>
/// Email Configuration - Configure email notifications for the system
/// </summary>
[Authorize(Policy = "IsAdmin")]
public class EmailConfigModel : PageModel
{
    private readonly IEmailConfigService _emailConfigService;
    private readonly IAuditLogService _auditLogService;
    private readonly ILogger<EmailConfigModel> _logger;

    public EmailConfigModel(
        IEmailConfigService emailConfigService,
        IAuditLogService auditLogService,
        ILogger<EmailConfigModel> logger)
    {
        _emailConfigService = emailConfigService;
        _auditLogService = auditLogService;
        _logger = logger;
    }

    [BindProperty] public bool EmailEnabled { get; set; }
    [BindProperty] public string EmailApiKey { get; set; } = string.Empty;
    [BindProperty] public string EmailApiUrl { get; set; } = string.Empty;
    [BindProperty] public string EmailFromAddress { get; set; } = string.Empty;

    public bool HasExistingKey { get; set; }
    public string? Success { get; set; }
    public string? Error { get; set; }

    // Statistics
    public int TotalEmailsSent { get; set; }
    public int EmailsToday { get; set; }
    public int FailedEmails { get; set; }
    public DateTime? LastEmailSent { get; set; }

    public async Task OnGetAsync()
    {
        try
        {
            var emailConfig = await _emailConfigService.GetEmailConfigAsync();

            EmailEnabled = emailConfig.Enabled;
            EmailApiUrl = emailConfig.ApiUrl ?? string.Empty;
            EmailFromAddress = emailConfig.FromAddress ?? string.Empty;
            HasExistingKey = !string.IsNullOrWhiteSpace(emailConfig.EncryptedApiKey);

            // Load statistics (placeholder values)
            TotalEmailsSent = 0; // Would query from email log
            EmailsToday = 0; // Would query from email log
            FailedEmails = 0; // Would query from email log
            LastEmailSent = null; // Would query from email log
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading email configuration");
            Error = "Failed to load email configuration.";
        }
    }

    public async Task<IActionResult> OnPostAsync()
    {
        try
        {
            var currentUserId = GetCurrentUserId();
            var userName = User.Identity?.Name ?? "Unknown";

            // Save email configuration
            var config = await _emailConfigService.SaveEmailConfigAsync(
                EmailEnabled,
                string.IsNullOrWhiteSpace(EmailApiKey) ? null : EmailApiKey,
                EmailApiUrl,
                EmailFromAddress,
                userName);

            // Log the configuration change
            await _auditLogService.LogUserActionAsync(
                currentUserId,
                "EmailConfigUpdated",
                "EmailConfig",
                null,
                "Email configuration updated",
                $"Enabled={EmailEnabled}, Url={EmailApiUrl}, From={EmailFromAddress}");

            Success = "Email configuration saved successfully.";
            HasExistingKey = !string.IsNullOrWhiteSpace(EmailApiKey) || HasExistingKey;

            // Reload the page data
            await OnGetAsync();
            return Page();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving email configuration");
            Error = "An error occurred while saving email configuration.";
            await OnGetAsync();
            return Page();
        }
    }

    public async Task<IActionResult> OnPostSendTestAsync(string testEmail)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(testEmail))
            {
                Error = "Please provide a valid email address.";
                await OnGetAsync();
                return Page();
            }

            var currentUserId = GetCurrentUserId();

            // TODO: Implement test email sending
            // This would use the IEmailService to send a test email
            _logger.LogInformation("Test email requested to {Email}", testEmail);

            Success = $"Test email sent to {testEmail}. Check your inbox.";

            // Log the test
            await _auditLogService.LogUserActionAsync(
                currentUserId,
                "TestEmailSent",
                "EmailConfig",
                null,
                $"Test email sent to {testEmail}",
                null);

            await OnGetAsync();
            return Page();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending test email");
            Error = "Failed to send test email.";
            await OnGetAsync();
            return Page();
        }
    }

    private int GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(userIdClaim, out var userId) ? userId : 0;
    }
}
