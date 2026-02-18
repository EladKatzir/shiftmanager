using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using ShiftManager.Models.Support;
using ShiftManager.Resources;

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
    private readonly IEmailApiLogService _emailApiLogService;
    private readonly IStringLocalizer<SharedResources> _localizer;
    private readonly IEmailTemplateService _emailTemplateService;
    private readonly EmailBackgroundQueue _emailQueue;
    private readonly ILocalizationService _localization;
    private readonly ITenantResolver? _tenantResolver;

    /// <summary>
    /// Constructor with dependency injection for HTTP client factory, logging, configuration, and localization.
    /// ITenantResolver is optional — it's available during HTTP requests (for enqueue) but not in background processor scope.
    /// </summary>
    public MailService(
        IHttpClientFactory httpClientFactory,
        ILogger<MailService> logger,
        IConfiguration configuration,
        IEmailConfigService emailConfigService,
        IEmailApiLogService emailApiLogService,
        IStringLocalizer<SharedResources> localizer,
        IEmailTemplateService emailTemplateService,
        EmailBackgroundQueue emailQueue,
        ILocalizationService localization,
        ITenantResolver? tenantResolver = null)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _emailConfigService = emailConfigService ?? throw new ArgumentNullException(nameof(emailConfigService));
        _emailApiLogService = emailApiLogService ?? throw new ArgumentNullException(nameof(emailApiLogService));
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _emailTemplateService = emailTemplateService ?? throw new ArgumentNullException(nameof(emailTemplateService));
        _emailQueue = emailQueue ?? throw new ArgumentNullException(nameof(emailQueue));
        _localization = localization ?? throw new ArgumentNullException(nameof(localization));
        _tenantResolver = tenantResolver;
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
    /// Validates email configuration and returns list of validation errors.
    /// </summary>
    private List<string> ValidateEmailConfiguration(string? apiKey, string? apiUrl, string recipient)
    {
        var errors = new List<string>();

        // URL validation
        if (string.IsNullOrWhiteSpace(apiUrl))
        {
            errors.Add("API URL is not configured");
        }
        else if (!Uri.TryCreate(apiUrl, UriKind.Absolute, out Uri? uri))
        {
            errors.Add("API URL is invalid (not a valid URI)");
        }
        else if (uri.Scheme != "http" && uri.Scheme != "https")
        {
            errors.Add("API URL must use HTTP or HTTPS protocol");
        }

        // API key validation
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            errors.Add("API key is not configured");
        }
        else if (apiKey.Length < 8)
        {
            errors.Add("API key appears too short (expected at least 8 characters)");
        }

        // Recipient validation
        if (string.IsNullOrWhiteSpace(recipient))
        {
            errors.Add("Recipient email is empty");
        }
        else if (!recipient.Contains("@"))
        {
            errors.Add("Recipient email is invalid (missing @ symbol)");
        }

        return errors;
    }

    /// <summary>
    /// Returns a user-friendly error message based on HTTP status code.
    /// </summary>
    private string GetUserFriendlyHttpError(int statusCode, string? responseBody)
    {
        var friendlyMessage = statusCode switch
        {
            401 => "API key appears invalid or expired",
            403 => "Access forbidden - check API permissions",
            404 => "API endpoint not found - check URL",
            429 => "Rate limit exceeded - wait before retrying",
            500 => "Email service server error",
            502 => "Bad gateway - email service may be temporarily unavailable",
            503 => "Service unavailable - email service may be under maintenance",
            504 => "Gateway timeout - email service took too long to respond",
            _ => $"HTTP {statusCode} error"
        };

        // Include response body if it's short and might be helpful
        if (!string.IsNullOrWhiteSpace(responseBody) && responseBody.Length < 200)
        {
            return $"{friendlyMessage}. Response: {responseBody}";
        }

        return friendlyMessage;
    }

    /// <summary>
    /// Enqueue an email for background delivery. Returns immediately without blocking the HTTP request.
    /// Uses backpressure-aware EnqueueAsync that waits briefly if the queue is full.
    /// </summary>
    public async Task<bool> SendMailAsync(string recipient, string subject, string htmlBody)
    {
        if (string.IsNullOrWhiteSpace(recipient))
        {
            _logger.LogWarning("Cannot queue email: recipient is null or empty");
            return false;
        }

        if (string.IsNullOrWhiteSpace(subject))
        {
            _logger.LogWarning("Cannot queue email to {Recipient}: subject is null or empty", recipient);
            return false;
        }

        // Capture CompanyId from tenant context at enqueue time (HTTP request context is available here).
        // This allows the background processor to set CompanyId on EmailApiLog entities,
        // bypassing the CompanyIdInterceptor which would otherwise throw when no HTTP context exists.
        var companyId = _tenantResolver != null && _tenantResolver.HasTenant()
            ? _tenantResolver.GetCurrentTenantId()
            : 0;

        var queued = await _emailQueue.EnqueueAsync(new QueuedEmail(recipient, subject, htmlBody, companyId));
        if (queued)
        {
            _logger.LogDebug("Email queued for background delivery to {Recipient}", recipient);
        }

        return queued;
    }

    /// <summary>
    /// Send an email directly via HTTP call. Called by EmailBackgroundProcessor.
    /// Do not call from HTTP request handlers — use SendMailAsync instead.
    /// </summary>
    public async Task<bool> SendMailDirectAsync(string recipient, string subject, string htmlBody, int companyId = 0)
    {
        // Start timing for diagnostics
        var stopwatch = Stopwatch.StartNew();

        // Variables for diagnostic logging
        string? requestUrl = null;
        string? requestBody = null;
        Dictionary<string, string>? requestHeaders = null;
        int? responseStatusCode = null;
        Dictionary<string, string>? responseHeaders = null;
        string? responseBody = null;
        bool success = false;
        string? errorMessage = null;
        List<string>? validationErrors = null;

        try
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
            requestUrl = apiUrl ?? "not-configured";

            // Check if email is enabled in configuration
            if (!emailEnabled)
            {
                _logger.LogInformation("Email service disabled (source: {Source}). Skipping email to {Recipient} with subject: {Subject}",
                    source, recipient, subject);
                return true; // Return true to avoid blocking workflow
            }

            // Validate configuration BEFORE attempting to send
            validationErrors = ValidateEmailConfiguration(apiKey, apiUrl, recipient);
            if (validationErrors.Any())
            {
                errorMessage = string.Join("; ", validationErrors);
                _logger.LogError("Email configuration validation failed: {Errors}", errorMessage);

                // Log validation failure to database (fire-and-forget)
                stopwatch.Stop();
                _ = _emailApiLogService.LogEmailApiCallAsync(
                    requestUrl: requestUrl,
                    requestMethod: "POST",
                    requestHeaders: new Dictionary<string, string>(),
                    requestBody: "",
                    responseStatusCode: null,
                    responseHeaders: null,
                    responseBody: null,
                    recipientEmail: recipient,
                    emailSubject: subject,
                    success: false,
                    errorMessage: errorMessage,
                    durationMs: (int)stopwatch.ElapsedMilliseconds,
                    validationErrors: validationErrors,
                    companyId: companyId);

                return false;
            }

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
            requestBody = JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false
            });

            var content = new StringContent(requestBody, Encoding.UTF8, "application/json");

            // Add API key header
            httpClient.DefaultRequestHeaders.Clear();
            httpClient.DefaultRequestHeaders.Add("Apikey", apiKey!);

            // Capture request headers for diagnostics (mask API key to prevent PII leak in DB logs)
            var maskedKey = apiKey != null && apiKey.Length > 8
                ? apiKey[..4] + "****" + apiKey[^4..]
                : "****";
            requestHeaders = new Dictionary<string, string>
            {
                { "Apikey", maskedKey },
                { "Content-Type", "application/json" }
            };

            // Set reasonable timeout (30 seconds)
            httpClient.Timeout = TimeSpan.FromSeconds(30);

            _logger.LogInformation("Sending email to {Recipient} with subject: {Subject} (config source: {Source})",
                recipient, subject, source);

            // Send POST request to mail API
            HttpResponseMessage response = await httpClient.PostAsync(apiUrl, content);

            // Capture response details
            responseStatusCode = (int)response.StatusCode;
            responseBody = await response.Content.ReadAsStringAsync();

            // Capture response headers
            responseHeaders = new Dictionary<string, string>();
            foreach (var header in response.Headers)
            {
                responseHeaders[header.Key] = string.Join(", ", header.Value);
            }

            if (response.IsSuccessStatusCode)
            {
                success = true;
                _logger.LogInformation("Email sent successfully to {Recipient}. Status: {StatusCode}",
                    recipient, responseStatusCode);
            }
            else
            {
                success = false;
                errorMessage = GetUserFriendlyHttpError(responseStatusCode.Value, responseBody);
                _logger.LogError("Failed to send email to {Recipient}. Status: {StatusCode}, Error: {Error}",
                    recipient, responseStatusCode, errorMessage);
            }
        }
        catch (HttpRequestException httpEx)
        {
            success = false;
            errorMessage = $"Network error: {httpEx.Message}";
            _logger.LogError(httpEx, "HTTP error while sending email to {Recipient}: {Message}",
                recipient, httpEx.Message);
        }
        catch (TaskCanceledException tcEx)
        {
            success = false;
            errorMessage = "Request timeout (30s exceeded)";
            _logger.LogError(tcEx, "Email request to {Recipient} timed out: {Message}",
                recipient, tcEx.Message);
        }
        catch (Exception ex)
        {
            success = false;
            errorMessage = $"Unexpected error: {ex.Message}";
            _logger.LogError(ex, "Unexpected error while sending email to {Recipient}: {Message}",
                recipient, ex.Message);
        }
        finally
        {
            // Always log to database for diagnostics (fire-and-forget)
            stopwatch.Stop();
            _ = _emailApiLogService.LogEmailApiCallAsync(
                requestUrl: requestUrl ?? "unknown",
                requestMethod: "POST",
                requestHeaders: requestHeaders ?? new Dictionary<string, string>(),
                requestBody: requestBody ?? "",
                responseStatusCode: responseStatusCode,
                responseHeaders: responseHeaders,
                responseBody: responseBody,
                recipientEmail: recipient,
                emailSubject: subject,
                success: success,
                errorMessage: errorMessage,
                durationMs: (int)stopwatch.ElapsedMilliseconds,
                validationErrors: validationErrors,
                companyId: companyId);
        }

        return success;
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

        var emailDir = _localizer["Dir"] == "rtl" ? "rtl" : "ltr";
        string subject = string.Format(_localizer["Email_ShiftAssignedSubject"], _localization.FormatMediumDate(shiftDate));

        string htmlBody = $@"
<!DOCTYPE html>
<html dir='{emailDir}'>
<head>
    <meta charset='utf-8'>
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
            <h2>{_localizer["Email_ShiftAssignedTitle"]}</h2>
        </div>
        <div class='content'>
            <p>{string.Format(_localizer["Email_Hello"], $"<strong>{WebUtility.HtmlEncode(employeeName)}</strong>")},</p>
            <p>{_localizer["Email_ShiftAssignedBody"]}</p>

            <div class='shift-details'>
                <p><strong>{_localizer["Email_ShiftType"]}:</strong> <span class='highlight'>{WebUtility.HtmlEncode(shiftTypeName)}</span></p>
                <p><strong>{_localizer["Date"]}:</strong> {_localization.FormatLongDate(shiftDate)}</p>
                <p><strong>{_localizer["Time"]}:</strong> {_localization.FormatTime(startTime)} - {_localization.FormatTime(endTime)}</p>
            </div>

            <p>{_localizer["Email_ShiftAssignedLoginPrompt"]}</p>
            <p>{_localizer["Email_ShiftAssignedContactManager"]}</p>
        </div>
        <div class='footer'>
            <p>{_localizer["Email_AutomatedMessage"]}</p>
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

        var emailDir = _localizer["Dir"] == "rtl" ? "rtl" : "ltr";
        string subject = string.Format(_localizer["Email_ShiftChangedSubject"], _localization.FormatMediumDate(shiftDate));

        string htmlBody = $@"
<!DOCTYPE html>
<html dir='{emailDir}'>
<head>
    <meta charset='utf-8'>
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
            <h2>⚠️ {_localizer["Email_ShiftChangedTitle"]}</h2>
        </div>
        <div class='content'>
            <p>{string.Format(_localizer["Email_Hello"], $"<strong>{WebUtility.HtmlEncode(employeeName)}</strong>")},</p>
            <p>{_localizer["Email_ShiftChangedBody"]}</p>

            <div class='shift-details'>
                <p><strong>{_localizer["Email_ShiftType"]}:</strong> <span class='highlight'>{WebUtility.HtmlEncode(shiftTypeName)}</span></p>
                <p><strong>{_localizer["Date"]}:</strong> {_localization.FormatLongDate(shiftDate)}</p>
                <p><strong>{_localizer["Time"]}:</strong> {_localization.FormatTime(startTime)} - {_localization.FormatTime(endTime)}</p>
            </div>

            <div class='change-notice'>
                <p><strong>{_localizer["Email_ChangeDetails"]}:</strong> {WebUtility.HtmlEncode(changeDescription)}</p>
            </div>

            <p>{_localizer["Email_ShiftChangedReviewPrompt"]}</p>
            <p>{_localizer["Email_ShiftChangedContactManager"]}</p>
        </div>
        <div class='footer'>
            <p>{_localizer["Email_AutomatedMessage"]}</p>
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

        var emailDir = _localizer["Dir"] == "rtl" ? "rtl" : "ltr";
        string subject = string.Format(_localizer["Email_ShiftDeletedSubject"], _localization.FormatMediumDate(shiftDate));

        string htmlBody = $@"
<!DOCTYPE html>
<html dir='{emailDir}'>
<head>
    <meta charset='utf-8'>
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
            <h2>{_localizer["Email_ShiftDeletedTitle"]}</h2>
        </div>
        <div class='content'>
            <p>{string.Format(_localizer["Email_Hello"], $"<strong>{WebUtility.HtmlEncode(employeeName)}</strong>")},</p>
            <p>{_localizer["Email_ShiftDeletedBody"]}</p>

            <div class='shift-details'>
                <p><strong>{_localizer["Email_ShiftType"]}:</strong> <span class='highlight'>{WebUtility.HtmlEncode(shiftTypeName)}</span></p>
                <p><strong>{_localizer["Date"]}:</strong> {_localization.FormatLongDate(shiftDate)}</p>
                <p><strong>{_localizer["Time"]}:</strong> {_localization.FormatTime(startTime)} - {_localization.FormatTime(endTime)}</p>
            </div>

            <p>{_localizer["Email_ShiftDeletedSchedulePrompt"]}</p>
            <p>{_localizer["Email_ShiftDeletedContactManager"]}</p>
        </div>
        <div class='footer'>
            <p>{_localizer["Email_AutomatedMessage"]}</p>
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

        var emailDir = _localizer["Dir"] == "rtl" ? "rtl" : "ltr";
        string subject = string.Format(_localizer["Email_ChoreAssignedSubject"], _localization.FormatMediumDate(choreDate));

        string htmlBody = $@"
<!DOCTYPE html>
<html dir='{emailDir}'>
<head>
    <meta charset='utf-8'>
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
            <h2>{_localizer["Email_ChoreAssignedTitle"]}</h2>
        </div>
        <div class='content'>
            <p>{string.Format(_localizer["Email_Hello"], $"<strong>{WebUtility.HtmlEncode(employeeName)}</strong>")},</p>
            <p>{_localizer["Email_ChoreAssignedBody"]}</p>

            <div class='chore-details'>
                <p><strong>{_localizer["Email_Chore"]}:</strong> <span class='highlight'>{WebUtility.HtmlEncode(choreTitle)}</span></p>
                <p><strong>{_localizer["Date"]}:</strong> {_localization.FormatLongDate(choreDate)}</p>
            </div>

            <p>{_localizer["Email_ChoreAssignedLoginPrompt"]}</p>
            <p>{_localizer["Email_ChoreAssignedContactManager"]}</p>
        </div>
        <div class='footer'>
            <p>{_localizer["Email_AutomatedMessage"]}</p>
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

        var emailDir = _localizer["Dir"] == "rtl" ? "rtl" : "ltr";
        string subject = string.Format(_localizer["Email_ChoreCanceledSubject"], _localization.FormatMediumDate(choreDate));

        string htmlBody = $@"
<!DOCTYPE html>
<html dir='{emailDir}'>
<head>
    <meta charset='utf-8'>
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
            <h2>{_localizer["Email_ChoreCanceledTitle"]}</h2>
        </div>
        <div class='content'>
            <p>{string.Format(_localizer["Email_Hello"], $"<strong>{WebUtility.HtmlEncode(employeeName)}</strong>")},</p>
            <p>{_localizer["Email_ChoreCanceledBody"]}</p>

            <div class='chore-details'>
                <p><strong>{_localizer["Email_Chore"]}:</strong> <span class='highlight'>{WebUtility.HtmlEncode(choreTitle)}</span></p>
                <p><strong>{_localizer["Date"]}:</strong> {_localization.FormatLongDate(choreDate)}</p>
            </div>

            <p>{_localizer["Email_ChoreCanceledSchedulePrompt"]}</p>
            <p>{_localizer["Email_ChoreCanceledContactManager"]}</p>
        </div>
        <div class='footer'>
            <p>{_localizer["Email_AutomatedMessage"]}</p>
        </div>
    </div>
</body>
</html>";

        return await SendMailAsync(recipientEmail, subject, htmlBody);
    }

    /// <summary>
    /// Send account approval notification email with formatted HTML template.
    /// Notifies users when their join request has been approved by an administrator.
    /// </summary>
    public async Task<bool> SendAccountApprovedEmailAsync(
        string recipientEmail,
        string userName,
        string assignedRole,
        string companyName)
    {
        if (string.IsNullOrWhiteSpace(recipientEmail))
        {
            _logger.LogWarning("Cannot send account approved email: recipient email is null or empty");
            return false;
        }

        var emailDir = _localizer["Dir"] == "rtl" ? "rtl" : "ltr";
        string subject = string.Format(_localizer["Email_AccountApprovedSubject"], companyName);

        // Check for custom template
        var customMessage = await _emailTemplateService.GetCustomMessageAsync(EmailTemplateType.AccountApproved);
        string messageBody;

        if (!string.IsNullOrWhiteSpace(customMessage))
        {
            var variables = new Dictionary<string, string>
            {
                { "UserName", userName },
                { "CompanyName", companyName },
                { "AssignedRole", assignedRole }
            };
            messageBody = _emailTemplateService.ReplaceVariables(customMessage, variables);
        }
        else
        {
            messageBody = _localizer["Email_AccountApprovedBody"];
        }

        string htmlBody = $@"
<!DOCTYPE html>
<html dir='{emailDir}'>
<head>
    <meta charset='utf-8'>
    <style>
        body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; }}
        .container {{ max-width: 600px; margin: 0 auto; padding: 20px; }}
        .header {{ background-color: #4CAF50; color: white; padding: 15px; text-align: center; }}
        .content {{ padding: 20px; background-color: #f9f9f9; }}
        .account-details {{ background-color: white; padding: 15px; margin: 15px 0; border-left: 4px solid #4CAF50; }}
        .footer {{ text-align: center; padding: 15px; font-size: 12px; color: #666; }}
        .highlight {{ font-weight: bold; color: #4CAF50; }}
        .welcome-box {{ background-color: #e8f5e9; padding: 15px; margin: 15px 0; border-radius: 8px; text-align: center; }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h2>✓ {_localizer["Email_AccountApprovedTitle"]}</h2>
        </div>
        <div class='content'>
            <div class='welcome-box'>
                <h3>{string.Format(_localizer["Email_WelcomeToCompany"], WebUtility.HtmlEncode(companyName))}</h3>
            </div>

            <p>{string.Format(_localizer["Email_Hello"], $"<strong>{WebUtility.HtmlEncode(userName)}</strong>")},</p>
            <p>{messageBody}</p>

            <div class='account-details'>
                <p><strong>{_localizer["Email_Company"]}:</strong> <span class='highlight'>{WebUtility.HtmlEncode(companyName)}</span></p>
                <p><strong>{_localizer["Email_AssignedRole"]}:</strong> <span class='highlight'>{WebUtility.HtmlEncode(assignedRole)}</span></p>
                <p><strong>{_localizer["Email_AccountStatus"]}:</strong> <span class='highlight'>{_localizer["Email_Active"]}</span></p>
            </div>

            <p>{_localizer["Email_AccountApprovedLoginPrompt"]}</p>
            <p>{_localizer["Email_AccountApprovedNextSteps"]}</p>
            <p>{_localizer["Email_AccountApprovedContactSupport"]}</p>
        </div>
        <div class='footer'>
            <p>{_localizer["Email_AutomatedMessage"]}</p>
        </div>
    </div>
</body>
</html>";

        return await SendMailAsync(recipientEmail, subject, htmlBody);
    }

    /// <summary>
    /// Send time-off request approved notification email with formatted HTML template.
    /// </summary>
    public async Task<bool> SendTimeOffApprovedEmailAsync(
        string recipientEmail,
        string employeeName,
        DateOnly startDate,
        DateOnly endDate)
    {
        if (string.IsNullOrWhiteSpace(recipientEmail))
        {
            _logger.LogWarning("Cannot send time-off approved email: recipient email is null or empty");
            return false;
        }

        var emailDir = _localizer["Dir"] == "rtl" ? "rtl" : "ltr";
        var dateRange = startDate == endDate
            ? _localization.FormatMediumDate(startDate)
            : $"{_localization.FormatMediumDate(startDate)} - {_localization.FormatMediumDate(endDate)}";

        string subject = string.Format(_localizer["Email_TimeOffApprovedSubject"], dateRange);

        // Check for custom template
        var customMessage = await _emailTemplateService.GetCustomMessageAsync(EmailTemplateType.TimeOffApproved);
        string messageBody;

        if (!string.IsNullOrWhiteSpace(customMessage))
        {
            var variables = new Dictionary<string, string>
            {
                { "EmployeeName", employeeName },
                { "StartDate", _localization.FormatMediumDate(startDate) },
                { "EndDate", _localization.FormatMediumDate(endDate) }
            };
            messageBody = _emailTemplateService.ReplaceVariables(customMessage, variables);
        }
        else
        {
            messageBody = _localizer["Email_TimeOffApprovedBody"];
        }

        string htmlBody = $@"
<!DOCTYPE html>
<html dir='{emailDir}'>
<head>
    <meta charset='utf-8'>
    <style>
        body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; }}
        .container {{ max-width: 600px; margin: 0 auto; padding: 20px; }}
        .header {{ background-color: #4CAF50; color: white; padding: 15px; text-align: center; }}
        .content {{ padding: 20px; background-color: #f9f9f9; }}
        .timeoff-details {{ background-color: white; padding: 15px; margin: 15px 0; border-left: 4px solid #4CAF50; }}
        .footer {{ text-align: center; padding: 15px; font-size: 12px; color: #666; }}
        .highlight {{ font-weight: bold; color: #4CAF50; }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h2>✓ {_localizer["Email_TimeOffApprovedTitle"]}</h2>
        </div>
        <div class='content'>
            <p>{string.Format(_localizer["Email_Hello"], $"<strong>{WebUtility.HtmlEncode(employeeName)}</strong>")},</p>
            <p>{messageBody}</p>

            <div class='timeoff-details'>
                <p><strong>{_localizer["Email_DateRange"]}:</strong> <span class='highlight'>{dateRange}</span></p>
                <p><strong>{_localizer["Email_Status"]}:</strong> <span class='highlight'>{_localizer["Email_Approved"]}</span></p>
            </div>

            <p>{_localizer["Email_TimeOffApprovedEnjoy"]}</p>
        </div>
        <div class='footer'>
            <p>{_localizer["Email_AutomatedMessage"]}</p>
        </div>
    </div>
</body>
</html>";

        return await SendMailAsync(recipientEmail, subject, htmlBody);
    }

    /// <summary>
    /// Send time-off request declined notification email with formatted HTML template.
    /// </summary>
    public async Task<bool> SendTimeOffDeclinedEmailAsync(
        string recipientEmail,
        string employeeName,
        DateOnly startDate,
        DateOnly endDate)
    {
        if (string.IsNullOrWhiteSpace(recipientEmail))
        {
            _logger.LogWarning("Cannot send time-off declined email: recipient email is null or empty");
            return false;
        }

        var emailDir = _localizer["Dir"] == "rtl" ? "rtl" : "ltr";
        var dateRange = startDate == endDate
            ? _localization.FormatMediumDate(startDate)
            : $"{_localization.FormatMediumDate(startDate)} - {_localization.FormatMediumDate(endDate)}";

        string subject = string.Format(_localizer["Email_TimeOffDeclinedSubject"], dateRange);

        // Check for custom template
        var customMessage = await _emailTemplateService.GetCustomMessageAsync(EmailTemplateType.TimeOffDeclined);
        string messageBody;

        if (!string.IsNullOrWhiteSpace(customMessage))
        {
            var variables = new Dictionary<string, string>
            {
                { "EmployeeName", employeeName },
                { "StartDate", _localization.FormatMediumDate(startDate) },
                { "EndDate", _localization.FormatMediumDate(endDate) }
            };
            messageBody = _emailTemplateService.ReplaceVariables(customMessage, variables);
        }
        else
        {
            messageBody = _localizer["Email_TimeOffDeclinedBody"];
        }

        string htmlBody = $@"
<!DOCTYPE html>
<html dir='{emailDir}'>
<head>
    <meta charset='utf-8'>
    <style>
        body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; }}
        .container {{ max-width: 600px; margin: 0 auto; padding: 20px; }}
        .header {{ background-color: #F44336; color: white; padding: 15px; text-align: center; }}
        .content {{ padding: 20px; background-color: #f9f9f9; }}
        .timeoff-details {{ background-color: white; padding: 15px; margin: 15px 0; border-left: 4px solid #F44336; }}
        .footer {{ text-align: center; padding: 15px; font-size: 12px; color: #666; }}
        .highlight {{ font-weight: bold; color: #F44336; }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h2>{_localizer["Email_TimeOffDeclinedTitle"]}</h2>
        </div>
        <div class='content'>
            <p>{string.Format(_localizer["Email_Hello"], $"<strong>{WebUtility.HtmlEncode(employeeName)}</strong>")},</p>
            <p>{messageBody}</p>

            <div class='timeoff-details'>
                <p><strong>{_localizer["Email_DateRange"]}:</strong> <span class='highlight'>{dateRange}</span></p>
                <p><strong>{_localizer["Email_Status"]}:</strong> <span class='highlight'>{_localizer["Email_Declined"]}</span></p>
            </div>

            <p>{_localizer["Email_TimeOffDeclinedContactManager"]}</p>
        </div>
        <div class='footer'>
            <p>{_localizer["Email_AutomatedMessage"]}</p>
        </div>
    </div>
</body>
</html>";

        return await SendMailAsync(recipientEmail, subject, htmlBody);
    }

    /// <summary>
    /// Send time-off request deleted notification email with formatted HTML template.
    /// </summary>
    public async Task<bool> SendTimeOffDeletedEmailAsync(
        string recipientEmail,
        string employeeName,
        DateOnly startDate,
        DateOnly endDate)
    {
        if (string.IsNullOrWhiteSpace(recipientEmail))
        {
            _logger.LogWarning("Cannot send time-off deleted email: recipient email is null or empty");
            return false;
        }

        var emailDir = _localizer["Dir"] == "rtl" ? "rtl" : "ltr";
        var dateRange = startDate == endDate
            ? _localization.FormatMediumDate(startDate)
            : $"{_localization.FormatMediumDate(startDate)} - {_localization.FormatMediumDate(endDate)}";

        string subject = string.Format(_localizer["Email_TimeOffDeletedSubject"], dateRange);

        // Check for custom template
        var customMessage = await _emailTemplateService.GetCustomMessageAsync(EmailTemplateType.TimeOffDeleted);
        string messageBody;

        if (!string.IsNullOrWhiteSpace(customMessage))
        {
            var variables = new Dictionary<string, string>
            {
                { "EmployeeName", employeeName },
                { "StartDate", _localization.FormatMediumDate(startDate) },
                { "EndDate", _localization.FormatMediumDate(endDate) }
            };
            messageBody = _emailTemplateService.ReplaceVariables(customMessage, variables);
        }
        else
        {
            messageBody = _localizer["Email_TimeOffDeletedBody"];
        }

        string htmlBody = $@"
<!DOCTYPE html>
<html dir='{emailDir}'>
<head>
    <meta charset='utf-8'>
    <style>
        body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; }}
        .container {{ max-width: 600px; margin: 0 auto; padding: 20px; }}
        .header {{ background-color: #FF9800; color: white; padding: 15px; text-align: center; }}
        .content {{ padding: 20px; background-color: #f9f9f9; }}
        .timeoff-details {{ background-color: white; padding: 15px; margin: 15px 0; border-left: 4px solid #FF9800; }}
        .footer {{ text-align: center; padding: 15px; font-size: 12px; color: #666; }}
        .highlight {{ font-weight: bold; color: #FF9800; }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h2>⚠️ {_localizer["Email_TimeOffDeletedTitle"]}</h2>
        </div>
        <div class='content'>
            <p>{string.Format(_localizer["Email_Hello"], $"<strong>{WebUtility.HtmlEncode(employeeName)}</strong>")},</p>
            <p>{messageBody}</p>

            <div class='timeoff-details'>
                <p><strong>{_localizer["Email_DateRange"]}:</strong> <span class='highlight'>{dateRange}</span></p>
            </div>

            <p>{_localizer["Email_TimeOffDeletedContactManager"]}</p>
        </div>
        <div class='footer'>
            <p>{_localizer["Email_AutomatedMessage"]}</p>
        </div>
    </div>
</body>
</html>";

        return await SendMailAsync(recipientEmail, subject, htmlBody);
    }

    /// <summary>
    /// Send swap request approved notification email with formatted HTML template.
    /// </summary>
    public async Task<bool> SendSwapRequestApprovedEmailAsync(
        string recipientEmail,
        string employeeName,
        string shiftInfo)
    {
        if (string.IsNullOrWhiteSpace(recipientEmail))
        {
            _logger.LogWarning("Cannot send swap request approved email: recipient email is null or empty");
            return false;
        }

        var emailDir = _localizer["Dir"] == "rtl" ? "rtl" : "ltr";
        string subject = _localizer["Email_SwapRequestApprovedSubject"];

        // Check for custom template
        var customMessage = await _emailTemplateService.GetCustomMessageAsync(EmailTemplateType.SwapRequestApproved);
        string messageBody;

        if (!string.IsNullOrWhiteSpace(customMessage))
        {
            var variables = new Dictionary<string, string>
            {
                { "EmployeeName", employeeName },
                { "ShiftInfo", shiftInfo }
            };
            messageBody = _emailTemplateService.ReplaceVariables(customMessage, variables);
        }
        else
        {
            messageBody = _localizer["Email_SwapRequestApprovedBody"];
        }

        string htmlBody = $@"
<!DOCTYPE html>
<html dir='{emailDir}'>
<head>
    <meta charset='utf-8'>
    <style>
        body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; }}
        .container {{ max-width: 600px; margin: 0 auto; padding: 20px; }}
        .header {{ background-color: #4CAF50; color: white; padding: 15px; text-align: center; }}
        .content {{ padding: 20px; background-color: #f9f9f9; }}
        .swap-details {{ background-color: white; padding: 15px; margin: 15px 0; border-left: 4px solid #4CAF50; }}
        .footer {{ text-align: center; padding: 15px; font-size: 12px; color: #666; }}
        .highlight {{ font-weight: bold; color: #4CAF50; }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h2>✓ {_localizer["Email_SwapRequestApprovedTitle"]}</h2>
        </div>
        <div class='content'>
            <p>{string.Format(_localizer["Email_Hello"], $"<strong>{WebUtility.HtmlEncode(employeeName)}</strong>")},</p>
            <p>{messageBody}</p>

            <div class='swap-details'>
                <p><strong>{_localizer["Email_Shift"]}:</strong> <span class='highlight'>{WebUtility.HtmlEncode(shiftInfo)}</span></p>
                <p><strong>{_localizer["Email_Status"]}:</strong> <span class='highlight'>{_localizer["Email_Approved"]}</span></p>
            </div>

            <p>{_localizer["Email_SwapRequestApprovedScheduleUpdated"]}</p>
        </div>
        <div class='footer'>
            <p>{_localizer["Email_AutomatedMessage"]}</p>
        </div>
    </div>
</body>
</html>";

        return await SendMailAsync(recipientEmail, subject, htmlBody);
    }

    /// <summary>
    /// Send swap request declined notification email with formatted HTML template.
    /// </summary>
    public async Task<bool> SendSwapRequestDeclinedEmailAsync(
        string recipientEmail,
        string employeeName,
        string shiftInfo)
    {
        if (string.IsNullOrWhiteSpace(recipientEmail))
        {
            _logger.LogWarning("Cannot send swap request declined email: recipient email is null or empty");
            return false;
        }

        var emailDir = _localizer["Dir"] == "rtl" ? "rtl" : "ltr";
        string subject = _localizer["Email_SwapRequestDeclinedSubject"];

        // Check for custom template
        var customMessage = await _emailTemplateService.GetCustomMessageAsync(EmailTemplateType.SwapRequestDeclined);
        string messageBody;

        if (!string.IsNullOrWhiteSpace(customMessage))
        {
            var variables = new Dictionary<string, string>
            {
                { "EmployeeName", employeeName },
                { "ShiftInfo", shiftInfo }
            };
            messageBody = _emailTemplateService.ReplaceVariables(customMessage, variables);
        }
        else
        {
            messageBody = _localizer["Email_SwapRequestDeclinedBody"];
        }

        string htmlBody = $@"
<!DOCTYPE html>
<html dir='{emailDir}'>
<head>
    <meta charset='utf-8'>
    <style>
        body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; }}
        .container {{ max-width: 600px; margin: 0 auto; padding: 20px; }}
        .header {{ background-color: #F44336; color: white; padding: 15px; text-align: center; }}
        .content {{ padding: 20px; background-color: #f9f9f9; }}
        .swap-details {{ background-color: white; padding: 15px; margin: 15px 0; border-left: 4px solid #F44336; }}
        .footer {{ text-align: center; padding: 15px; font-size: 12px; color: #666; }}
        .highlight {{ font-weight: bold; color: #F44336; }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h2>{_localizer["Email_SwapRequestDeclinedTitle"]}</h2>
        </div>
        <div class='content'>
            <p>{string.Format(_localizer["Email_Hello"], $"<strong>{WebUtility.HtmlEncode(employeeName)}</strong>")},</p>
            <p>{messageBody}</p>

            <div class='swap-details'>
                <p><strong>{_localizer["Email_Shift"]}:</strong> <span class='highlight'>{WebUtility.HtmlEncode(shiftInfo)}</span></p>
                <p><strong>{_localizer["Email_Status"]}:</strong> <span class='highlight'>{_localizer["Email_Declined"]}</span></p>
            </div>

            <p>{_localizer["Email_SwapRequestDeclinedContactManager"]}</p>
        </div>
        <div class='footer'>
            <p>{_localizer["Email_AutomatedMessage"]}</p>
        </div>
    </div>
</body>
</html>";

        return await SendMailAsync(recipientEmail, subject, htmlBody);
    }

    /// <summary>
    /// Send on-duty assignment notification email with formatted HTML template.
    /// </summary>
    public async Task<bool> SendOnDutyAssignedEmailAsync(
        string recipientEmail,
        string employeeName,
        string onDutyTypeName,
        DateOnly onDutyDate)
    {
        if (string.IsNullOrWhiteSpace(recipientEmail))
        {
            _logger.LogWarning("Cannot send on-duty assigned email: recipient email is null or empty");
            return false;
        }

        var emailDir = _localizer["Dir"] == "rtl" ? "rtl" : "ltr";
        string subject = string.Format(_localizer["Email_OnDutyAssignedSubject"], _localization.FormatMediumDate(onDutyDate));

        // Check for custom template
        var customMessage = await _emailTemplateService.GetCustomMessageAsync(EmailTemplateType.OnDutyAssigned);
        string messageBody;

        if (!string.IsNullOrWhiteSpace(customMessage))
        {
            var variables = new Dictionary<string, string>
            {
                { "EmployeeName", employeeName },
                { "OnDutyType", onDutyTypeName },
                { "Date", _localization.FormatMediumDate(onDutyDate) }
            };
            messageBody = _emailTemplateService.ReplaceVariables(customMessage, variables);
        }
        else
        {
            messageBody = _localizer["Email_OnDutyAssignedBody"];
        }

        string htmlBody = $@"
<!DOCTYPE html>
<html dir='{emailDir}'>
<head>
    <meta charset='utf-8'>
    <style>
        body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; }}
        .container {{ max-width: 600px; margin: 0 auto; padding: 20px; }}
        .header {{ background-color: #9C27B0; color: white; padding: 15px; text-align: center; }}
        .content {{ padding: 20px; background-color: #f9f9f9; }}
        .onduty-details {{ background-color: white; padding: 15px; margin: 15px 0; border-left: 4px solid #9C27B0; }}
        .footer {{ text-align: center; padding: 15px; font-size: 12px; color: #666; }}
        .highlight {{ font-weight: bold; color: #9C27B0; }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h2>{_localizer["Email_OnDutyAssignedTitle"]}</h2>
        </div>
        <div class='content'>
            <p>{string.Format(_localizer["Email_Hello"], $"<strong>{WebUtility.HtmlEncode(employeeName)}</strong>")},</p>
            <p>{messageBody}</p>

            <div class='onduty-details'>
                <p><strong>{_localizer["Email_OnDutyType"]}:</strong> <span class='highlight'>{WebUtility.HtmlEncode(onDutyTypeName)}</span></p>
                <p><strong>{_localizer["Date"]}:</strong> {_localization.FormatLongDate(onDutyDate)}</p>
            </div>

            <p>{_localizer["Email_OnDutyAssignedLoginPrompt"]}</p>
        </div>
        <div class='footer'>
            <p>{_localizer["Email_AutomatedMessage"]}</p>
        </div>
    </div>
</body>
</html>";

        return await SendMailAsync(recipientEmail, subject, htmlBody);
    }

    /// <summary>
    /// Send on-duty cancellation notification email with formatted HTML template.
    /// </summary>
    public async Task<bool> SendOnDutyCanceledEmailAsync(
        string recipientEmail,
        string employeeName,
        string onDutyTypeName,
        DateOnly onDutyDate)
    {
        if (string.IsNullOrWhiteSpace(recipientEmail))
        {
            _logger.LogWarning("Cannot send on-duty canceled email: recipient email is null or empty");
            return false;
        }

        var emailDir = _localizer["Dir"] == "rtl" ? "rtl" : "ltr";
        string subject = string.Format(_localizer["Email_OnDutyCanceledSubject"], _localization.FormatMediumDate(onDutyDate));

        // Check for custom template
        var customMessage = await _emailTemplateService.GetCustomMessageAsync(EmailTemplateType.OnDutyCanceled);
        string messageBody;

        if (!string.IsNullOrWhiteSpace(customMessage))
        {
            var variables = new Dictionary<string, string>
            {
                { "EmployeeName", employeeName },
                { "OnDutyType", onDutyTypeName },
                { "Date", _localization.FormatMediumDate(onDutyDate) }
            };
            messageBody = _emailTemplateService.ReplaceVariables(customMessage, variables);
        }
        else
        {
            messageBody = _localizer["Email_OnDutyCanceledBody"];
        }

        string htmlBody = $@"
<!DOCTYPE html>
<html dir='{emailDir}'>
<head>
    <meta charset='utf-8'>
    <style>
        body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; }}
        .container {{ max-width: 600px; margin: 0 auto; padding: 20px; }}
        .header {{ background-color: #FF9800; color: white; padding: 15px; text-align: center; }}
        .content {{ padding: 20px; background-color: #f9f9f9; }}
        .onduty-details {{ background-color: white; padding: 15px; margin: 15px 0; border-left: 4px solid #FF9800; }}
        .footer {{ text-align: center; padding: 15px; font-size: 12px; color: #666; }}
        .highlight {{ font-weight: bold; color: #FF9800; }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h2>⚠️ {_localizer["Email_OnDutyCanceledTitle"]}</h2>
        </div>
        <div class='content'>
            <p>{string.Format(_localizer["Email_Hello"], $"<strong>{WebUtility.HtmlEncode(employeeName)}</strong>")},</p>
            <p>{messageBody}</p>

            <div class='onduty-details'>
                <p><strong>{_localizer["Email_OnDutyType"]}:</strong> <span class='highlight'>{WebUtility.HtmlEncode(onDutyTypeName)}</span></p>
                <p><strong>{_localizer["Date"]}:</strong> {_localization.FormatLongDate(onDutyDate)}</p>
            </div>

            <p>{_localizer["Email_OnDutyCanceledScheduleUpdated"]}</p>
        </div>
        <div class='footer'>
            <p>{_localizer["Email_AutomatedMessage"]}</p>
        </div>
    </div>
</body>
</html>";

        return await SendMailAsync(recipientEmail, subject, htmlBody);
    }

    /// <summary>
    /// Send access request submitted notification email to owners.
    /// </summary>
    public async Task<bool> SendAccessRequestSubmittedEmailAsync(
        string recipientEmail,
        string ownerName,
        string requesterName,
        string requesterEmail,
        string companyName)
    {
        if (string.IsNullOrWhiteSpace(recipientEmail))
        {
            _logger.LogWarning("Cannot send access request submitted email: recipient email is null or empty");
            return false;
        }

        var emailDir = _localizer["Dir"] == "rtl" ? "rtl" : "ltr";
        string subject = string.Format(_localizer["Email_AccessRequestSubmittedSubject"], requesterName);

        // Check for custom template
        var customMessage = await _emailTemplateService.GetCustomMessageAsync(EmailTemplateType.AccessRequestSubmitted);
        string messageBody;

        if (!string.IsNullOrWhiteSpace(customMessage))
        {
            var variables = new Dictionary<string, string>
            {
                { "OwnerName", ownerName },
                { "RequesterName", requesterName },
                { "RequesterEmail", requesterEmail },
                { "CompanyName", companyName }
            };
            messageBody = _emailTemplateService.ReplaceVariables(customMessage, variables);
        }
        else
        {
            messageBody = _localizer["Email_AccessRequestSubmittedBody"];
        }

        string htmlBody = $@"
<!DOCTYPE html>
<html dir='{emailDir}'>
<head>
    <meta charset='utf-8'>
    <style>
        body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; }}
        .container {{ max-width: 600px; margin: 0 auto; padding: 20px; }}
        .header {{ background-color: #2196F3; color: white; padding: 15px; text-align: center; }}
        .content {{ padding: 20px; background-color: #f9f9f9; }}
        .request-details {{ background-color: white; padding: 15px; margin: 15px 0; border-left: 4px solid #2196F3; }}
        .footer {{ text-align: center; padding: 15px; font-size: 12px; color: #666; }}
        .highlight {{ font-weight: bold; color: #2196F3; }}
        .action-box {{ background-color: #e3f2fd; padding: 15px; margin: 15px 0; border-radius: 8px; text-align: center; }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h2>{_localizer["Email_AccessRequestSubmittedTitle"]}</h2>
        </div>
        <div class='content'>
            <p>{string.Format(_localizer["Email_Hello"], $"<strong>{WebUtility.HtmlEncode(ownerName)}</strong>")},</p>
            <p>{messageBody}</p>

            <div class='request-details'>
                <p><strong>{_localizer["Email_RequesterName"]}:</strong> <span class='highlight'>{WebUtility.HtmlEncode(requesterName)}</span></p>
                <p><strong>{_localizer["Email_RequesterEmail"]}:</strong> {WebUtility.HtmlEncode(requesterEmail)}</p>
                <p><strong>{_localizer["Email_Company"]}:</strong> {WebUtility.HtmlEncode(companyName)}</p>
            </div>

            <div class='action-box'>
                <p><strong>{_localizer["Email_AccessRequestAction"]}</strong></p>
                <p>{_localizer["Email_AccessRequestLoginToReview"]}</p>
            </div>
        </div>
        <div class='footer'>
            <p>{_localizer["Email_AutomatedMessage"]}</p>
        </div>
    </div>
</body>
</html>";

        return await SendMailAsync(recipientEmail, subject, htmlBody);
    }

    // ============= Ops Console Scheduler: New Email Templates =============

    public async Task<bool> SendTraineeAddedEmailAsync(string recipientEmail, string employeeName,
        string traineeName, string shiftTypeName, DateOnly shiftDate, TimeOnly startTime, TimeOnly endTime)
    {
        var subject = $"Trainee Added: {shiftTypeName} on {_localization.FormatMediumDate(shiftDate)}";

        var htmlBody = $@"<!DOCTYPE html>
<html>
<head>
    <style>
        body {{ font-family: Arial, sans-serif; line-height: 1.6; margin: 0; padding: 0; background-color: #f4f4f4; }}
        .container {{ max-width: 600px; margin: 20px auto; background-color: #ffffff; padding: 20px; border-radius: 8px; box-shadow: 0 2px 10px rgba(0,0,0,0.1); }}
        .header {{ background-color: #2196F3; color: white; padding: 20px; text-align: center; border-radius: 6px 6px 0 0; }}
        .content {{ padding: 20px; }}
        .highlight {{ background-color: #E3F2FD; color: #1976D2; padding: 12px; border-radius: 4px; border-left: 4px solid #2196F3; margin: 15px 0; font-weight: 600; }}
        .details {{ background-color: #F5F5F5; padding: 15px; border-radius: 4px; margin: 15px 0; }}
        .footer {{ margin-top: 20px; padding-top: 20px; border-top: 1px solid #ddd; color: #666; font-size: 12px; text-align: center; }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h2>👤 Trainee Added to Your Shift</h2>
        </div>
        <div class='content'>
            <p>Hello <strong>{WebUtility.HtmlEncode(employeeName)}</strong>,</p>
            <p>A trainee has been added to your shift assignment:</p>

            <div class='highlight'>
                <strong>{WebUtility.HtmlEncode(traineeName)}</strong> will be joining you as a trainee
            </div>

            <div class='details'>
                <p><strong>Shift Type:</strong> {WebUtility.HtmlEncode(shiftTypeName)}</p>
                <p><strong>Date:</strong> {_localization.FormatMediumDate(shiftDate)}</p>
                <p><strong>Time:</strong> {_localization.FormatTime(startTime)} - {_localization.FormatTime(endTime)}</p>
            </div>

            <p>Please help guide and mentor your trainee during this shift.</p>
        </div>
        <div class='footer'>
            <p>This is an automated notification from ShiftManager.</p>
        </div>
    </div>
</body>
</html>";

        return await SendMailAsync(recipientEmail, subject, htmlBody);
    }

    public async Task<bool> SendSlotRemovedEmailAsync(string recipientEmail, string employeeName,
        string shiftTypeName, DateOnly shiftDate, TimeOnly startTime, TimeOnly endTime, string reason)
    {
        var subject = $"Shift Removed: {shiftTypeName} on {_localization.FormatMediumDate(shiftDate)}";

        var htmlBody = $@"<!DOCTYPE html>
<html>
<head>
    <style>
        body {{ font-family: Arial, sans-serif; line-height: 1.6; margin: 0; padding: 0; background-color: #f4f4f4; }}
        .container {{ max-width: 600px; margin: 20px auto; background-color: #ffffff; padding: 20px; border-radius: 8px; box-shadow: 0 2px 10px rgba(0,0,0,0.1); }}
        .header {{ background-color: #FF9800; color: white; padding: 20px; text-align: center; border-radius: 6px 6px 0 0; }}
        .content {{ padding: 20px; }}
        .alert-box {{ background-color: #FFF3CD; color: #856404; padding: 12px; border-radius: 4px; border-left: 4px solid #FF9800; margin: 15px 0; }}
        .details {{ background-color: #F5F5F5; padding: 15px; border-radius: 4px; margin: 15px 0; }}
        .footer {{ margin-top: 20px; padding-top: 20px; border-top: 1px solid #ddd; color: #666; font-size: 12px; text-align: center; }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h2>⚠️ Shift Assignment Removed</h2>
        </div>
        <div class='content'>
            <p>Hello <strong>{WebUtility.HtmlEncode(employeeName)}</strong>,</p>
            <p>Your shift assignment has been removed from the schedule:</p>

            <div class='details'>
                <p><strong>Shift Type:</strong> {WebUtility.HtmlEncode(shiftTypeName)}</p>
                <p><strong>Date:</strong> {_localization.FormatMediumDate(shiftDate)}</p>
                <p><strong>Time:</strong> {_localization.FormatTime(startTime)} - {_localization.FormatTime(endTime)}</p>
            </div>

            <div class='alert-box'>
                <p><strong>Reason:</strong> {WebUtility.HtmlEncode(reason)}</p>
            </div>

            <p>If you have any questions about this change, please contact your manager.</p>
        </div>
        <div class='footer'>
            <p>This is an automated notification from ShiftManager.</p>
        </div>
    </div>
</body>
</html>";

        return await SendMailAsync(recipientEmail, subject, htmlBody);
    }

    public async Task<bool> SendShiftModifiedEmailAsync(string recipientEmail, string employeeName,
        string shiftTypeName, DateOnly shiftDate, string changeDescription)
    {
        var subject = $"Shift Modified: {shiftTypeName} on {_localization.FormatMediumDate(shiftDate)}";

        var htmlBody = $@"<!DOCTYPE html>
<html>
<head>
    <style>
        body {{ font-family: Arial, sans-serif; line-height: 1.6; margin: 0; padding: 0; background-color: #f4f4f4; }}
        .container {{ max-width: 600px; margin: 20px auto; background-color: #ffffff; padding: 20px; border-radius: 8px; box-shadow: 0 2px 10px rgba(0,0,0,0.1); }}
        .header {{ background-color: #9C27B0; color: white; padding: 20px; text-align: center; border-radius: 6px 6px 0 0; }}
        .content {{ padding: 20px; }}
        .change-box {{ background-color: #F3E5F5; color: #6A1B9A; padding: 12px; border-radius: 4px; border-left: 4px solid #9C27B0; margin: 15px 0; font-weight: 600; }}
        .details {{ background-color: #F5F5F5; padding: 15px; border-radius: 4px; margin: 15px 0; }}
        .footer {{ margin-top: 20px; padding-top: 20px; border-top: 1px solid #ddd; color: #666; font-size: 12px; text-align: center; }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h2>🔄 Your Shift Has Been Modified</h2>
        </div>
        <div class='content'>
            <p>Hello <strong>{WebUtility.HtmlEncode(employeeName)}</strong>,</p>
            <p>Your shift assignment has been updated:</p>

            <div class='details'>
                <p><strong>Shift Type:</strong> {WebUtility.HtmlEncode(shiftTypeName)}</p>
                <p><strong>Date:</strong> {_localization.FormatMediumDate(shiftDate)}</p>
            </div>

            <div class='change-box'>
                <p><strong>Changes:</strong> {WebUtility.HtmlEncode(changeDescription)}</p>
            </div>

            <p>Please review the updated shift details and contact your manager if you have any questions.</p>
        </div>
        <div class='footer'>
            <p>This is an automated notification from ShiftManager.</p>
        </div>
    </div>
</body>
</html>";

        return await SendMailAsync(recipientEmail, subject, htmlBody);
    }
}
