using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ShiftManager.Services;

/// <summary>
/// Service for sending email notifications via company mail API.
/// Sends emails for shift assignments, changes, and deletions.
/// Supports database configuration (per-company) with fallback to appsettings.json.
/// Follows ShiftManager architecture patterns with dependency injection and structured logging.
/// </summary>
public class MailService : IMailService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<MailService> _logger;
    private readonly IConfiguration _configuration;
    private readonly IEmailConfigService _emailConfigService;

    /// <summary>
    /// Constructor with dependency injection for HTTP client factory, logging, and configuration.
    /// </summary>
    public MailService(
        IHttpClientFactory httpClientFactory,
        ILogger<MailService> logger,
        IConfiguration configuration,
        IEmailConfigService emailConfigService)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _emailConfigService = emailConfigService ?? throw new ArgumentNullException(nameof(emailConfigService));
    }

    /// <summary>
    /// Loads email configuration from database (per-company) with fallback to appsettings.json
    /// </summary>
    private async Task<(bool enabled, string? apiKey, string? apiUrl, string? fromAddress, string source)> LoadConfigurationAsync()
    {
        try
        {
            // Try to load from database first (company-specific configuration)
            var dbConfig = await _emailConfigService.GetEmailConfigAsync();
            if (dbConfig != null)
            {
                var decryptedApiKey = await _emailConfigService.GetDecryptedApiKeyAsync();
                _logger.LogDebug("Loaded email configuration from database for company {CompanyId}", dbConfig.CompanyId);
                return (dbConfig.Enabled, decryptedApiKey, dbConfig.ApiUrl, dbConfig.FromAddress, "database");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load email configuration from database, falling back to appsettings.json");
        }

        // Fallback to appsettings.json
        var apiKey = _configuration["Email:ApiKey"];
        var apiUrl = _configuration["Email:ApiUrl"];
        var fromAddress = _configuration["Email:FromAddress"] ?? "noreply@shiftmanager.local";
        var enabled = _configuration.GetValue<bool>("Email:Enabled", false);

        _logger.LogDebug("Loaded email configuration from appsettings.json");
        return (enabled, apiKey, apiUrl, fromAddress, "appsettings.json");
    }

    /// <summary>
    /// Send an email notification asynchronously with retry logic and error handling.
    /// </summary>
    /// <param name="recipient">Email address of the recipient</param>
    /// <param name="subject">Email subject line</param>
    /// <param name="htmlBody">HTML-formatted email body</param>
    /// <returns>True if email sent successfully, false otherwise</returns>
    public async Task<bool> SendMailAsync(string recipient, string subject, string htmlBody)
    {
        // Validate inputs
        if (string.IsNullOrWhiteSpace(recipient))
        {
            _logger.LogWarning("Cannot send email: recipient is null or empty");
            return false;
        }

        if (string.IsNullOrWhiteSpace(subject))
        {
            _logger.LogWarning("Cannot send email to {Recipient}: subject is null or empty", recipient);
            return false;
        }

        // Load configuration (database first, then fallback to appsettings.json)
        var (emailEnabled, apiKey, apiUrl, fromAddress, source) = await LoadConfigurationAsync();

        // Check if email is enabled in configuration
        if (!emailEnabled)
        {
            _logger.LogInformation("Email service disabled (source: {Source}). Skipping email to {Recipient} with subject: {Subject}",
                source, recipient, subject);
            return true; // Return true to avoid blocking workflow
        }

        // Validate configuration
        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(apiUrl))
        {
            _logger.LogError("Email service misconfigured (source: {Source}). ApiKey or ApiUrl is missing. Cannot send email to {Recipient}",
                source, recipient);
            return false;
        }

        try
        {
            // Create HTTP client from factory (best practice for performance and connection pooling)
            using var httpClient = _httpClientFactory.CreateClient();

            // Build email payload matching company API format
            var payload = new
            {
                from = fromAddress,
                to = recipient,
                subject = subject,
                html = htmlBody
            };

            // Serialize to JSON
            string jsonPayload = JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false
            });

            var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            // Add API key header
            httpClient.DefaultRequestHeaders.Clear();
            httpClient.DefaultRequestHeaders.Add("Apikey", apiKey);

            // Set reasonable timeout (30 seconds)
            httpClient.Timeout = TimeSpan.FromSeconds(30);

            _logger.LogInformation("Sending email to {Recipient} with subject: {Subject} (config source: {Source})",
                recipient, subject, source);

            // Send POST request to mail API
            HttpResponseMessage response = await httpClient.PostAsync(apiUrl, content);

            // Read response content
            string responseContent = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Email sent successfully to {Recipient}. Response: {Response}",
                    recipient, responseContent);
                return true;
            }
            else
            {
                _logger.LogError("Failed to send email to {Recipient}. Status: {StatusCode}, Response: {Response}",
                    recipient, response.StatusCode, responseContent);
                return false;
            }
        }
        catch (HttpRequestException httpEx)
        {
            _logger.LogError(httpEx, "HTTP error while sending email to {Recipient}: {Message}",
                recipient, httpEx.Message);
            return false;
        }
        catch (TaskCanceledException tcEx)
        {
            _logger.LogError(tcEx, "Email request to {Recipient} timed out: {Message}",
                recipient, tcEx.Message);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while sending email to {Recipient}: {Message}",
                recipient, ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Send shift assignment notification email with formatted HTML template.
    /// </summary>
    public async Task<bool> SendShiftAssignedEmailAsync(
        string recipientEmail,
        string employeeName,
        string shiftTypeName,
        DateOnly shiftDate,
        TimeOnly startTime,
        TimeOnly endTime)
    {
        if (string.IsNullOrWhiteSpace(recipientEmail))
        {
            _logger.LogWarning("Cannot send shift assigned email: recipient email is null or empty");
            return false;
        }

        string subject = $"New Shift Assignment - {shiftDate:MMM dd, yyyy}";

        string htmlBody = $@"
<!DOCTYPE html>
<html>
<head>
    <style>
        body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; }}
        .container {{ max-width: 600px; margin: 0 auto; padding: 20px; }}
        .header {{ background-color: #4CAF50; color: white; padding: 15px; text-align: center; }}
        .content {{ padding: 20px; background-color: #f9f9f9; }}
        .shift-details {{ background-color: white; padding: 15px; margin: 15px 0; border-left: 4px solid #4CAF50; }}
        .footer {{ text-align: center; padding: 15px; font-size: 12px; color: #666; }}
        .highlight {{ font-weight: bold; color: #4CAF50; }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h2>New Shift Assignment</h2>
        </div>
        <div class='content'>
            <p>Hello <strong>{employeeName}</strong>,</p>
            <p>You have been assigned to work a new shift:</p>

            <div class='shift-details'>
                <p><strong>Shift Type:</strong> <span class='highlight'>{shiftTypeName}</span></p>
                <p><strong>Date:</strong> {shiftDate:dddd, MMMM dd, yyyy}</p>
                <p><strong>Time:</strong> {startTime:HH:mm} - {endTime:HH:mm}</p>
            </div>

            <p>Please log in to the ShiftManager system to view full details.</p>
            <p>If you have any questions or concerns, please contact your manager.</p>
        </div>
        <div class='footer'>
            <p>This is an automated message from ShiftManager. Please do not reply to this email.</p>
        </div>
    </div>
</body>
</html>";

        return await SendMailAsync(recipientEmail, subject, htmlBody);
    }

    /// <summary>
    /// Send shift change notification email with change description.
    /// </summary>
    public async Task<bool> SendShiftChangedEmailAsync(
        string recipientEmail,
        string employeeName,
        string shiftTypeName,
        DateOnly shiftDate,
        TimeOnly startTime,
        TimeOnly endTime,
        string changeDescription)
    {
        if (string.IsNullOrWhiteSpace(recipientEmail))
        {
            _logger.LogWarning("Cannot send shift changed email: recipient email is null or empty");
            return false;
        }

        string subject = $"Shift Change Notification - {shiftDate:MMM dd, yyyy}";

        string htmlBody = $@"
<!DOCTYPE html>
<html>
<head>
    <style>
        body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; }}
        .container {{ max-width: 600px; margin: 0 auto; padding: 20px; }}
        .header {{ background-color: #FF9800; color: white; padding: 15px; text-align: center; }}
        .content {{ padding: 20px; background-color: #f9f9f9; }}
        .shift-details {{ background-color: white; padding: 15px; margin: 15px 0; border-left: 4px solid #FF9800; }}
        .footer {{ text-align: center; padding: 15px; font-size: 12px; color: #666; }}
        .highlight {{ font-weight: bold; color: #FF9800; }}
        .change-notice {{ background-color: #fff3cd; padding: 10px; margin: 10px 0; border-radius: 4px; }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h2>⚠️ Shift Change Notification</h2>
        </div>
        <div class='content'>
            <p>Hello <strong>{employeeName}</strong>,</p>
            <p>Your shift has been modified:</p>

            <div class='shift-details'>
                <p><strong>Shift Type:</strong> <span class='highlight'>{shiftTypeName}</span></p>
                <p><strong>Date:</strong> {shiftDate:dddd, MMMM dd, yyyy}</p>
                <p><strong>Time:</strong> {startTime:HH:mm} - {endTime:HH:mm}</p>
            </div>

            <div class='change-notice'>
                <p><strong>Change Details:</strong> {changeDescription}</p>
            </div>

            <p>Please log in to the ShiftManager system to review the updated shift details.</p>
            <p>If you have any questions or concerns, please contact your manager immediately.</p>
        </div>
        <div class='footer'>
            <p>This is an automated message from ShiftManager. Please do not reply to this email.</p>
        </div>
    </div>
</body>
</html>";

        return await SendMailAsync(recipientEmail, subject, htmlBody);
    }

    /// <summary>
    /// Send shift deletion notification email.
    /// </summary>
    public async Task<bool> SendShiftDeletedEmailAsync(
        string recipientEmail,
        string employeeName,
        string shiftTypeName,
        DateOnly shiftDate,
        TimeOnly startTime,
        TimeOnly endTime)
    {
        if (string.IsNullOrWhiteSpace(recipientEmail))
        {
            _logger.LogWarning("Cannot send shift deleted email: recipient email is null or empty");
            return false;
        }

        string subject = $"Shift Removed - {shiftDate:MMM dd, yyyy}";

        string htmlBody = $@"
<!DOCTYPE html>
<html>
<head>
    <style>
        body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; }}
        .container {{ max-width: 600px; margin: 0 auto; padding: 20px; }}
        .header {{ background-color: #F44336; color: white; padding: 15px; text-align: center; }}
        .content {{ padding: 20px; background-color: #f9f9f9; }}
        .shift-details {{ background-color: white; padding: 15px; margin: 15px 0; border-left: 4px solid #F44336; }}
        .footer {{ text-align: center; padding: 15px; font-size: 12px; color: #666; }}
        .highlight {{ font-weight: bold; color: #F44336; }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h2>Shift Removed</h2>
        </div>
        <div class='content'>
            <p>Hello <strong>{employeeName}</strong>,</p>
            <p>Your assigned shift has been removed from the schedule:</p>

            <div class='shift-details'>
                <p><strong>Shift Type:</strong> <span class='highlight'>{shiftTypeName}</span></p>
                <p><strong>Date:</strong> {shiftDate:dddd, MMMM dd, yyyy}</p>
                <p><strong>Time:</strong> {startTime:HH:mm} - {endTime:HH:mm}</p>
            </div>

            <p>This shift is no longer on your schedule. Please log in to the ShiftManager system to view your updated schedule.</p>
            <p>If you have any questions, please contact your manager.</p>
        </div>
        <div class='footer'>
            <p>This is an automated message from ShiftManager. Please do not reply to this email.</p>
        </div>
    </div>
</body>
</html>";

        return await SendMailAsync(recipientEmail, subject, htmlBody);
    }

    /// <summary>
    /// Send chore assignment notification email with formatted HTML template.
    /// </summary>
    public async Task<bool> SendChoreAssignedEmailAsync(
        string recipientEmail,
        string employeeName,
        string choreTitle,
        DateOnly choreDate)
    {
        if (string.IsNullOrWhiteSpace(recipientEmail))
        {
            _logger.LogWarning("Cannot send chore assigned email: recipient email is null or empty");
            return false;
        }

        string subject = $"New Chore Assignment - {choreDate:MMM dd, yyyy}";

        string htmlBody = $@"
<!DOCTYPE html>
<html>
<head>
    <style>
        body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; }}
        .container {{ max-width: 600px; margin: 0 auto; padding: 20px; }}
        .header {{ background-color: #2196F3; color: white; padding: 15px; text-align: center; }}
        .content {{ padding: 20px; background-color: #f9f9f9; }}
        .chore-details {{ background-color: white; padding: 15px; margin: 15px 0; border-left: 4px solid #2196F3; }}
        .footer {{ text-align: center; padding: 15px; font-size: 12px; color: #666; }}
        .highlight {{ font-weight: bold; color: #2196F3; }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h2>New Chore Assignment</h2>
        </div>
        <div class='content'>
            <p>Hello <strong>{employeeName}</strong>,</p>
            <p>You have been assigned a new chore:</p>

            <div class='chore-details'>
                <p><strong>Chore:</strong> <span class='highlight'>{choreTitle}</span></p>
                <p><strong>Date:</strong> {choreDate:dddd, MMMM dd, yyyy}</p>
            </div>

            <p>Please log in to the ShiftManager system to view full details.</p>
            <p>If you have any questions or concerns, please contact your manager.</p>
        </div>
        <div class='footer'>
            <p>This is an automated message from ShiftManager. Please do not reply to this email.</p>
        </div>
    </div>
</body>
</html>";

        return await SendMailAsync(recipientEmail, subject, htmlBody);
    }

    /// <summary>
    /// Send chore cancellation notification email with formatted HTML template.
    /// </summary>
    public async Task<bool> SendChoreCanceledEmailAsync(
        string recipientEmail,
        string employeeName,
        string choreTitle,
        DateOnly choreDate)
    {
        if (string.IsNullOrWhiteSpace(recipientEmail))
        {
            _logger.LogWarning("Cannot send chore canceled email: recipient email is null or empty");
            return false;
        }

        string subject = $"Chore Canceled - {choreDate:MMM dd, yyyy}";

        string htmlBody = $@"
<!DOCTYPE html>
<html>
<head>
    <style>
        body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; }}
        .container {{ max-width: 600px; margin: 0 auto; padding: 20px; }}
        .header {{ background-color: #FF9800; color: white; padding: 15px; text-align: center; }}
        .content {{ padding: 20px; background-color: #f9f9f9; }}
        .chore-details {{ background-color: white; padding: 15px; margin: 15px 0; border-left: 4px solid #FF9800; }}
        .footer {{ text-align: center; padding: 15px; font-size: 12px; color: #666; }}
        .highlight {{ font-weight: bold; color: #FF9800; }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h2>Chore Canceled</h2>
        </div>
        <div class='content'>
            <p>Hello <strong>{employeeName}</strong>,</p>
            <p>Your assigned chore has been canceled:</p>

            <div class='chore-details'>
                <p><strong>Chore:</strong> <span class='highlight'>{choreTitle}</span></p>
                <p><strong>Date:</strong> {choreDate:dddd, MMMM dd, yyyy}</p>
            </div>

            <p>This chore is no longer on your schedule. Please log in to the ShiftManager system to view your updated schedule.</p>
            <p>If you have any questions, please contact your manager.</p>
        </div>
        <div class='footer'>
            <p>This is an automated message from ShiftManager. Please do not reply to this email.</p>
        </div>
    </div>
</body>
</html>";

        return await SendMailAsync(recipientEmail, subject, htmlBody);
    }
}
