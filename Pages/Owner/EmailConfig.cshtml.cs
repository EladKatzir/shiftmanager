using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using ShiftManager.Models;
using ShiftManager.Pages;
using ShiftManager.Resources;
using ShiftManager.Services;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace ShiftManager.Pages.Owner;

/// <summary>
/// Email Configuration - Configure email notifications for the system
/// </summary>
[Authorize(Policy = "Grant:ConfigureEmailSettings")]
public class EmailConfigModel : LocalizedPageModel
{
    private readonly IEmailConfigService _emailConfigService;
    private readonly IAuditLogService _auditLogService;
    private readonly ILogger<EmailConfigModel> _logger;
    private readonly IMailService _mailService;
    private readonly IEmailApiLogService _emailApiLogService;

    public EmailConfigModel(
        IStringLocalizer<SharedResources> localizer,
        IEmailConfigService emailConfigService,
        IAuditLogService auditLogService,
        ILogger<EmailConfigModel> logger,
        IMailService mailService,
        IEmailApiLogService emailApiLogService) : base(localizer)
    {
        _emailConfigService = emailConfigService;
        _auditLogService = auditLogService;
        _logger = logger;
        _mailService = mailService;
        _emailApiLogService = emailApiLogService;
    }

    [BindProperty] public bool EmailEnabled { get; set; }
    [BindProperty] public string EmailApiKey { get; set; } = string.Empty;
    [BindProperty] public string EmailApiUrl { get; set; } = string.Empty;
    [BindProperty] public string EmailFromAddress { get; set; } = string.Empty;

    // Global config properties
    [BindProperty] public bool GlobalEmailEnabled { get; set; }
    [BindProperty] public string GlobalEmailApiKey { get; set; } = string.Empty;
    [BindProperty] public string GlobalEmailApiUrl { get; set; } = string.Empty;
    [BindProperty] public string GlobalEmailFromAddress { get; set; } = string.Empty;
    public bool GlobalHasExistingKey { get; set; }
    public bool HasCompanyOverride { get; set; }
    public bool IsOwner { get; set; }

    public bool HasExistingKey { get; set; }

    // Statistics
    public int TotalEmailsSent { get; set; }
    public int EmailsToday { get; set; }
    public int FailedEmails { get; set; }
    public DateTime? LastEmailSent { get; set; }

    // Diagnostics
    public EmailApiLog? LastTestResult { get; set; }
    public string? TestDiagnostics { get; set; }
    public List<EmailApiLog> RecentLogs { get; set; } = new();
    public List<EmailApiLog> RecentFailures { get; set; } = new();

    // Status Widget Properties
    public DateTime? LastTestTimestamp { get; set; }
    public bool? LastTestSuccess { get; set; }
    public string? LastTestError { get; set; }

    public async Task OnGetAsync()
    {
        try
        {
            // Check Owner status for showing global config section
            var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (int.TryParse(userIdClaim, out var uid))
            {
                var grantService = HttpContext.RequestServices.GetService<IGrantService>();
                IsOwner = grantService != null && await grantService.HasGrantAsync(uid, "AdminAccess");
            }

            // Load global config
            var globalConfig = await _emailConfigService.GetGlobalEmailConfigAsync();
            GlobalEmailEnabled = globalConfig?.Enabled ?? false;
            GlobalEmailApiUrl = globalConfig?.ApiUrl ?? string.Empty;
            GlobalEmailFromAddress = globalConfig?.FromAddress ?? string.Empty;
            GlobalHasExistingKey = !string.IsNullOrWhiteSpace(globalConfig?.EncryptedApiKey);

            // Load company-specific override (if any)
            HasCompanyOverride = await _emailConfigService.HasCompanyOverrideAsync();

            // Load effective config for current company (company override or global fallback)
            var emailConfig = await _emailConfigService.GetEmailConfigAsync();

            EmailEnabled = emailConfig?.Enabled ?? false;
            EmailApiUrl = emailConfig?.ApiUrl ?? string.Empty;
            EmailFromAddress = emailConfig?.FromAddress ?? string.Empty;
            HasExistingKey = !string.IsNullOrWhiteSpace(emailConfig?.EncryptedApiKey);

            // Load recent logs and failures for diagnostics
            RecentLogs = await _emailApiLogService.GetRecentLogsAsync(10);
            RecentFailures = await _emailApiLogService.GetFailedLogsAsync(5);

            // Populate status widget properties from most recent log
            var lastLog = RecentLogs.FirstOrDefault();
            if (lastLog != null)
            {
                LastTestTimestamp = lastLog.Timestamp;
                LastTestSuccess = lastLog.Success;
                LastTestError = lastLog.ErrorMessage;
            }

            // Load real statistics from email logs
            var allLogs = await _emailApiLogService.GetRecentLogsAsync(1000);
            TotalEmailsSent = allLogs.Count(l => l.Success);
            EmailsToday = allLogs.Count(l => l.Success && l.Timestamp.Date == DateTime.UtcNow.Date);
            FailedEmails = allLogs.Count(l => !l.Success);
            LastEmailSent = allLogs.OrderByDescending(l => l.Timestamp).FirstOrDefault()?.Timestamp;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading email configuration");
            Error = _localizer["Error_FailedToLoadEmailConfig"];
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

            Success = _localizer["Success_EmailConfigSaved"];
            HasExistingKey = !string.IsNullOrWhiteSpace(EmailApiKey) || HasExistingKey;

            // Reload the page data
            await OnGetAsync();
            return Page();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving email configuration");
            Error = _localizer["Error_SavingEmailConfigFailed"];
            await OnGetAsync();
            return Page();
        }
    }

    public async Task<IActionResult> OnPostSaveGlobalAsync()
    {
        try
        {
            var currentUserId = GetCurrentUserId();

            // SECURITY: Global config affects ALL companies — require Owner (AdminAccess) grant
            var grantService = HttpContext.RequestServices.GetRequiredService<IGrantService>();
            if (!await grantService.HasGrantAsync(currentUserId, "AdminAccess"))
            {
                _logger.LogWarning("User {UserId} attempted to save global email config without AdminAccess grant", currentUserId);
                return Forbid();
            }

            var userName = User.Identity?.Name ?? "Unknown";

            await _emailConfigService.SaveGlobalEmailConfigAsync(
                GlobalEmailEnabled,
                string.IsNullOrWhiteSpace(GlobalEmailApiKey) ? null : GlobalEmailApiKey,
                GlobalEmailApiUrl,
                GlobalEmailFromAddress,
                userName);

            await _auditLogService.LogUserActionAsync(
                currentUserId, "GlobalEmailConfigUpdated", "EmailConfig", null,
                "Global email configuration updated",
                $"Enabled={GlobalEmailEnabled}, Url={GlobalEmailApiUrl}, From={GlobalEmailFromAddress}");

            Success = _localizer["Success_EmailConfigSaved"];
            await OnGetAsync();
            return Page();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving global email configuration");
            Error = _localizer["Error_SavingEmailConfigFailed"];
            await OnGetAsync();
            return Page();
        }
    }

    public async Task<IActionResult> OnPostRemoveOverrideAsync()
    {
        try
        {
            var currentUserId = GetCurrentUserId();
            var deleted = await _emailConfigService.DeleteCompanyOverrideAsync();
            if (deleted)
            {
                await _auditLogService.LogUserActionAsync(
                    currentUserId, "CompanyEmailOverrideRemoved", "EmailConfig", null,
                    "Company email config override removed — now using global config");

                Success = _localizer["Success_EmailConfigSaved"];
            }

            await OnGetAsync();
            return Page();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing company email override");
            Error = _localizer["Error_SavingEmailConfigFailed"];
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
                Error = _localizer["Error_ProvideValidEmail"];
                await OnGetAsync();
                return Page();
            }

            var currentUserId = GetCurrentUserId();

            // Actually send test email
            var testSubject = $"Test Email from ShiftManager - {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
            var testBody = BuildTestEmailHtml();

            _logger.LogInformation("Sending test email to {Email}", testEmail);

            // Use SendMailDirectAsync for test emails — sends synchronously so the
            // log entry is available immediately for diagnostics. SendMailAsync only
            // enqueues for background delivery, causing a race condition where the
            // log isn't written yet when we try to read it.
            var companyId = int.TryParse(User.FindFirst("CompanyId")?.Value, out var cid) ? cid : 0;
            bool success = await _mailService.SendMailDirectAsync(testEmail, testSubject, testBody, companyId);

            // Fetch most recent log — now available immediately because SendMailDirectAsync is synchronous
            var recentLogs = await _emailApiLogService.GetRecentLogsAsync(1);
            LastTestResult = recentLogs.FirstOrDefault();

            if (success && LastTestResult != null)
            {
                TestDiagnostics = FormatDiagnostics(LastTestResult);
                Success = string.Format(_localizer["Success_TestEmailSent"], testEmail);
            }
            else if (LastTestResult != null)
            {
                TestDiagnostics = FormatDiagnostics(LastTestResult);
                Error = string.Format(_localizer["Error_TestEmailFailed"], LastTestResult.ErrorMessage);
            }
            else
            {
                Error = _localizer["Error_TestEmailFailedNoDiagnostics"];
            }

            // Log audit
            await _auditLogService.LogUserActionAsync(
                currentUserId,
                "TestEmailSent",
                "EmailConfig",
                null,
                $"Test email sent to {testEmail}",
                $"Success: {success}");

            await OnGetAsync();
            return Page();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending test email");
            Error = "Failed to send test email. Please check your configuration and try again.";
            await OnGetAsync();
            return Page();
        }
    }

    private string BuildTestEmailHtml()
    {
        return @"
<!DOCTYPE html>
<html>
<head>
    <style>
        body { font-family: Arial, sans-serif; padding: 20px; }
        .test-banner { background: #2196F3; color: white; padding: 15px; border-radius: 8px; }
        .test-info { background: #f5f5f5; padding: 15px; margin-top: 15px; border-radius: 8px; }
    </style>
</head>
<body>
    <div class='test-banner'><h2>✅ Test Email Successful</h2></div>
    <div class='test-info'>
        <p><strong>This is a test email from ShiftManager.</strong></p>
        <p>If you're seeing this message, your email configuration is working correctly!</p>
        <p><strong>Sent:</strong> " + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") + @" UTC</p>
    </div>
</body>
</html>";
    }

    private string FormatDiagnostics(EmailApiLog log)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Timestamp: {log.Timestamp:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine($"Duration: {log.DurationMs}ms");
        sb.AppendLine($"Success: {(log.Success ? "✅ Yes" : "❌ No")}");
        sb.AppendLine();

        if (!string.IsNullOrEmpty(log.ValidationErrors))
        {
            sb.AppendLine("VALIDATION ERRORS:");
            sb.AppendLine(log.ValidationErrors);
            sb.AppendLine();
        }

        sb.AppendLine("REQUEST:");
        sb.AppendLine($"  URL: {log.RequestUrl}");
        sb.AppendLine($"  Method: {log.RequestMethod}");

        if (!string.IsNullOrEmpty(log.RequestHeaders))
        {
            sb.AppendLine("  Headers:");
            try
            {
                var headers = JsonSerializer.Deserialize<Dictionary<string, string>>(log.RequestHeaders);
                foreach (var h in headers ?? new())
                    sb.AppendLine($"    {h.Key}: {h.Value}");
            }
            catch
            {
                sb.AppendLine($"    {log.RequestHeaders}");
            }
        }

        if (!string.IsNullOrEmpty(log.RequestBody))
        {
            var bodyPreview = log.RequestBody.Length > 500 ? log.RequestBody.Substring(0, 500) + "..." : log.RequestBody;
            sb.AppendLine($"  Body: {bodyPreview}");
        }

        sb.AppendLine();
        sb.AppendLine("RESPONSE:");

        if (log.ResponseStatusCode.HasValue)
        {
            sb.AppendLine($"  Status Code: {log.ResponseStatusCode}");
            if (!string.IsNullOrEmpty(log.ResponseBody))
            {
                var responsePreview = log.ResponseBody.Length > 500 ? log.ResponseBody.Substring(0, 500) + "..." : log.ResponseBody;
                sb.AppendLine($"  Body: {responsePreview}");
            }
        }
        else
        {
            sb.AppendLine("  No response (network error or timeout)");
        }

        if (!string.IsNullOrEmpty(log.ErrorMessage))
            sb.AppendLine($"\nERROR: {log.ErrorMessage}");

        return sb.ToString();
    }

    public async Task<IActionResult> OnGetExportLogsJsonAsync(int count = 100)
    {
        var logs = await _emailApiLogService.GetRecentLogsAsync(count);
        var json = JsonSerializer.Serialize(logs, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        var fileName = $"EmailApiLogs_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json";
        return File(Encoding.UTF8.GetBytes(json), "application/json", fileName);
    }

    public async Task<IActionResult> OnGetExportLogsCsvAsync(int count = 100)
    {
        var logs = await _emailApiLogService.GetRecentLogsAsync(count);
        var csv = new StringBuilder();

        csv.AppendLine("Timestamp,Recipient,Subject,Success,Status Code,Duration (ms),Error Message,Request URL");

        foreach (var log in logs)
        {
            csv.AppendLine($"\"{log.Timestamp:yyyy-MM-dd HH:mm:ss}\"," +
                         $"\"{log.RecipientEmail}\"," +
                         $"\"{log.EmailSubject.Replace("\"", "\"\"")}\"," +
                         $"{log.Success}," +
                         $"{log.ResponseStatusCode?.ToString() ?? "N/A"}," +
                         $"{log.DurationMs}," +
                         $"\"{log.ErrorMessage?.Replace("\"", "\"\"") ?? ""}\"," +
                         $"\"{log.RequestUrl}\"");
        }

        var fileName = $"EmailApiLogs_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv";
        // Add UTF-8 BOM for Hebrew Excel compatibility (fixes G-07)
        var preamble = Encoding.UTF8.GetPreamble();
        var csvBytes = Encoding.UTF8.GetBytes(csv.ToString());
        var bomResult = new byte[preamble.Length + csvBytes.Length];
        preamble.CopyTo(bomResult, 0);
        csvBytes.CopyTo(bomResult, preamble.Length);
        return File(bomResult, "text/csv", fileName);
    }

    private int GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(userIdClaim, out var userId) ? userId : 0;
    }
}
