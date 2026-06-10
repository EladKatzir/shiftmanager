using System;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using ShiftManager.Data;
using ShiftManager.Models.Results;
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
    private readonly IFeatureFlagService? _featureFlagService;
    private readonly ShiftManager.Services.Notifications.INotificationLinkTokenService? _linkTokens;
    private readonly AppDbContext? _db;

    /// <summary>
    /// Constructor with dependency injection for HTTP client factory, logging, configuration, and localization.
    /// ITenantResolver and IFeatureFlagService are optional — available during HTTP requests but not in background processor scope.
    /// INotificationLinkTokenService is optional so existing unit tests can construct MailService without it;
    /// when present, the one-click opt-out footer is injected into emails that carry a recipientUserId.
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
        ITenantResolver? tenantResolver = null,
        IFeatureFlagService? featureFlagService = null,
        ShiftManager.Services.Notifications.INotificationLinkTokenService? linkTokens = null,
        AppDbContext? db = null)
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
        _featureFlagService = featureFlagService;
        _linkTokens = linkTokens;
        _db = db;
    }

    /// <summary>
    /// Injects the one-click "switch to Quiet mode" opt-out footer into an email body, just before
    /// &lt;/body&gt; (or appended if absent). No-op when the token service is unavailable or the
    /// recipient/company is unknown. The link is a signed, login-free token handled by /N/Quiet.
    /// </summary>
    /// <summary>
    /// Resolve a recipient's userId from (email, companyId) for the opt-out footer token. The pair
    /// is unique within a tenant. Read-only; failures degrade to 0 (no footer). Uses .ToLower() on
    /// the column against a client-pre-lowered constant (EF-translatable; see case-folding contract).
    /// </summary>
    private async Task<int> ResolveRecipientUserIdAsync(string email, int companyId)
    {
        try
        {
            var emailLower = email.Trim().ToLowerInvariant();
            return await _db!.Users
                .IgnoreQueryFilters()
                .Where(u => u.Email.ToLower() == emailLower && u.CompanyId == companyId)
                .Select(u => u.Id)
                .FirstOrDefaultAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Opt-out footer user resolution failed for recipient in company {CompanyId}", companyId);
            return 0;
        }
    }

    /// <summary>
    /// True if the user has fetched their .ics subscription feed within the freshness window (14 days)
    /// — i.e. they're an active subscriber, so a per-event Felix calendar push would duplicate the
    /// feed entry. Read-only; failures degrade to false (send the calendar invite).
    /// </summary>
    private async Task<bool> IsActiveFeedSubscriberAsync(int recipientUserId)
    {
        try
        {
            var lastPolled = await _db!.CalendarFeedTokens
                .IgnoreQueryFilters()
                .Where(t => t.UserId == recipientUserId)
                .Select(t => t.LastPolledAt)
                .FirstOrDefaultAsync();
            return lastPolled.HasValue && (DateTime.UtcNow - lastPolled.Value) <= TimeSpan.FromDays(14);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Feed-subscription check failed for user {UserId}; sending calendar invite", recipientUserId);
            return false;
        }
    }

    private string InjectOptOutFooter(string html, int recipientUserId, int companyId)
    {
        if (_linkTokens == null || recipientUserId <= 0)
            return html;

        try
        {
            var token = _linkTokens.CreateQuietToken(recipientUserId);
            var baseUrl = (_configuration["BaseUrl"] ?? string.Empty).TrimEnd('/');
            var url = $"{baseUrl}/N/Quiet?token={Uri.EscapeDataString(token)}";
            var label = WebUtility.HtmlEncode(_localizer["Email_ManagePreferences"].Value);
            var footer = $"<div style='text-align:center;padding:12px;font-size:11px;color:#9ca3af;'>" +
                         $"<a href='{url}' style='color:#9ca3af;'>{label}</a></div>";

            return html.Contains("</body>", StringComparison.OrdinalIgnoreCase)
                ? html.Replace("</body>", footer + "</body>")
                : html + footer;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to inject opt-out footer; sending email without it");
            return html;
        }
    }

    /// <summary>
    /// Builds the (targetUrl, payload) for a Felix send. Plain mail goes to the configured
    /// <paramref name="baseApiUrl"/> (already the /mail/send endpoint); calendar-eligible mail is
    /// routed to the <c>/calendar</c> or <c>/allDayEvent</c> sub-path with the mail object wrapped
    /// alongside the event. Pure (no I/O) so the payload shape + routing is unit-testable.
    /// Times are formatted as supplied — callers MUST pass UTC for timed events (the trailing 'Z'
    /// asserts UTC); all-day events use date-only.
    /// </summary>
    internal static (string url, object payload) BuildSendTarget(
        string baseApiUrl, string from, string to, string subject, string html,
        CalendarEventKind kind, DateTime? startUtc, DateTime? endUtc, string? location)
    {
        var mail = new { from, to, subject, html };
        var trimmed = (baseApiUrl ?? string.Empty).TrimEnd('/');

        switch (kind)
        {
            case CalendarEventKind.Timed:
                return ($"{trimmed}/calendar", new
                {
                    mail,
                    calendarEvent = new
                    {
                        startTime = startUtc?.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
                        endTime = endUtc?.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
                        location = location ?? string.Empty
                    }
                });

            case CalendarEventKind.AllDay:
                return ($"{trimmed}/allDayEvent", new
                {
                    mail,
                    calendarAllDayEvent = new
                    {
                        startTime = startUtc?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        endTime = endUtc?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        location = location ?? string.Empty
                    }
                });

            default:
                return (baseApiUrl ?? string.Empty, mail);
        }
    }

    /// <summary>
    /// Enqueue a calendar-eligible email — delivered through Felix's /calendar (Timed) or
    /// /allDayEvent (AllDay) endpoint so the recipient's Outlook receives a native calendar event
    /// ("summon"). Timed events require UTC start/end; all-day events use the date component only.
    /// </summary>
    public async Task<OperationResult> SendCalendarMailAsync(string recipient, string subject, string htmlBody,
        CalendarEventKind kind, DateTime? startUtc, DateTime? endUtc, string? location, int recipientUserId = 0)
    {
        if (string.IsNullOrWhiteSpace(recipient) || string.IsNullOrWhiteSpace(subject))
        {
            return OperationResult.Fail("Error_MailService_InvalidRecipient", _localizer["Error_MailService_InvalidRecipient"].Value);
        }

        var companyId = _tenantResolver != null && _tenantResolver.HasTenant()
            ? _tenantResolver.GetCurrentTenantId()
            : 0;

        var queued = await _emailQueue.EnqueueAsync(new QueuedEmail(
            recipient, subject, htmlBody, companyId, RecipientUserId: recipientUserId,
            CalendarKind: kind, EventStartUtc: startUtc, EventEndUtc: endUtc, EventLocation: location));

        return queued
            ? OperationResult.Ok()
            : OperationResult.Fail("Error_MailService_SendFailed", _localizer["Error_MailService_SendFailed"].Value);
    }

    /// <summary>
    /// Loads email configuration from database (per-company) with fallback to appsettings.json
    /// </summary>
    private async Task<(bool enabled, string? apiKey, string? apiUrl, string? fromAddress, string source)> LoadConfigurationAsync()
    {
        // Feature flag kill switch: Owner can disable via FeatureFlags UI
        if (_featureFlagService != null)
        {
            try
            {
                if (!await _featureFlagService.IsEnabledAsync(Data.SeedData.FeatureFlagSeed.Flags.EmailServiceEnabled))
                {
                    _logger.LogInformation("Email service disabled via FF_EMAIL_SERVICE_ENABLED feature flag");
                    return (false, null, null, null, "feature-flag-disabled");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to check email feature flag, continuing with other checks");
            }
        }

        // Secondary kill switch: Email:GlobalEnabled in appsettings.json (works even when DB is down)
        var globalEnabled = _configuration.GetValue<bool>("Email:GlobalEnabled", true);
        if (!globalEnabled)
        {
            _logger.LogInformation("Email service globally disabled via Email:GlobalEnabled=false");
            return (false, null, null, null, "global-disabled");
        }

        try
        {
            // Try database: company-specific → global fallback (handled by GetEmailConfigAsync)
            var dbConfig = await _emailConfigService.GetEmailConfigAsync();
            if (dbConfig != null)
            {
                var decryptedApiKey = await _emailConfigService.GetDecryptedApiKeyAsync();
                var source = dbConfig.CompanyId.HasValue ? $"company-{dbConfig.CompanyId}" : "global";
                _logger.LogDebug("Loaded email configuration from {Source}", source);
                return (dbConfig.Enabled, decryptedApiKey, dbConfig.ApiUrl, dbConfig.FromAddress, source);
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
    public async Task<OperationResult> SendMailAsync(string recipient, string subject, string htmlBody, int recipientUserId = 0)
    {
        if (string.IsNullOrWhiteSpace(recipient))
        {
            _logger.LogWarning("Cannot queue email: recipient is null or empty");
            return OperationResult.Fail("Error_MailService_InvalidRecipient", _localizer["Error_MailService_InvalidRecipient"].Value);
        }

        if (string.IsNullOrWhiteSpace(subject))
        {
            _logger.LogWarning("Cannot queue email to {Recipient}: subject is null or empty", recipient);
            return OperationResult.Fail("Error_MailService_InvalidRecipient", _localizer["Error_MailService_InvalidRecipient"].Value);
        }

        // Capture CompanyId from tenant context at enqueue time (HTTP request context is available here).
        // This allows the background processor to set CompanyId on EmailApiLog entities,
        // bypassing the CompanyIdInterceptor which would otherwise throw when no HTTP context exists.
        var companyId = _tenantResolver != null && _tenantResolver.HasTenant()
            ? _tenantResolver.GetCurrentTenantId()
            : 0;

        var queued = await _emailQueue.EnqueueAsync(new QueuedEmail(recipient, subject, htmlBody, companyId, RecipientUserId: recipientUserId));
        if (queued)
        {
            _logger.LogDebug("Email queued for background delivery to {Recipient}", recipient);
            return OperationResult.Ok();
        }

        // Queue full / unavailable — surface as a generic send failure
        _logger.LogWarning("Failed to enqueue email to {Recipient}: queue full or unavailable", recipient);
        return OperationResult.Fail("Error_MailService_SendFailed", _localizer["Error_MailService_SendFailed"].Value);
    }

    /// <summary>
    /// Send an email directly via HTTP call. Called by EmailBackgroundProcessor.
    /// Do not call from HTTP request handlers — use SendMailAsync instead.
    /// </summary>
    public async Task<OperationResult> SendMailDirectAsync(string recipient, string subject, string htmlBody, int companyId = 0, int recipientUserId = 0,
        CalendarEventKind calendarKind = CalendarEventKind.None, DateTime? eventStartUtc = null, DateTime? eventEndUtc = null, string? eventLocation = null)
    {
        // Resolve the recipient user (for the opt-out footer token) when not supplied explicitly.
        // The (email, companyId) pair is unique, so this finds the right user for same-tenant sends.
        if (recipientUserId <= 0 && companyId > 0 && _db != null && !string.IsNullOrWhiteSpace(recipient))
        {
            recipientUserId = await ResolveRecipientUserIdAsync(recipient, companyId);
        }

        // Subscription-aware routing: if this is a calendar push AND the recipient actively polls
        // their .ics feed, the feed already owns their calendar — downgrade to a plain email so they
        // don't get a duplicate calendar entry. The feed (self-healing) remains the source of truth.
        if (calendarKind != CalendarEventKind.None && recipientUserId > 0 && _db != null
            && await IsActiveFeedSubscriberAsync(recipientUserId))
        {
            calendarKind = CalendarEventKind.None;
        }

        // Inject the one-click opt-out footer (no-op if token service / id unavailable).
        htmlBody = InjectOptOutFooter(htmlBody, recipientUserId, companyId);

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

        // Tracks the structured failure key so the finally-block diagnostic log and the returned
        // OperationResult stay in sync. Null = no failure yet (or success).
        string? failureKey = null;

        try
        {
            // Validate inputs
            if (string.IsNullOrWhiteSpace(recipient))
            {
                _logger.LogWarning("Cannot send email: recipient is null or empty");
                failureKey = "Error_MailService_InvalidRecipient";
                errorMessage = _localizer["Error_MailService_InvalidRecipient"].Value;
                return OperationResult.Fail(failureKey, errorMessage);
            }

            if (string.IsNullOrWhiteSpace(subject))
            {
                _logger.LogWarning("Cannot send email to {Recipient}: subject is null or empty", recipient);
                failureKey = "Error_MailService_InvalidRecipient";
                errorMessage = _localizer["Error_MailService_InvalidRecipient"].Value;
                return OperationResult.Fail(failureKey, errorMessage);
            }

            // Load configuration (database first, then fallback to appsettings.json)
            var (emailEnabled, apiKey, apiUrl, fromAddress, source) = await LoadConfigurationAsync();
            requestUrl = apiUrl ?? "not-configured";

            // Check if email is enabled in configuration
            if (!emailEnabled)
            {
                _logger.LogInformation("Email service disabled (source: {Source}). Skipping email to {Recipient} with subject: {Subject}",
                    source, recipient, subject);
                // Distinguish kill-switch (feature flag / Email:GlobalEnabled=false) from "no config record at all"
                // via the source string returned by LoadConfigurationAsync.
                if (source == "feature-flag-disabled" || source == "global-disabled")
                {
                    return OperationResult.Fail("Error_MailService_DisabledByFlag", _localizer["Error_MailService_DisabledByFlag"].Value);
                }
                return OperationResult.Fail("Error_MailService_NotConfigured", _localizer["Error_MailService_NotConfigured"].Value);
            }

            // Validate configuration BEFORE attempting to send
            validationErrors = ValidateEmailConfiguration(apiKey, apiUrl, recipient);
            if (validationErrors.Any())
            {
                errorMessage = string.Join("; ", validationErrors);
                _logger.LogError("Email configuration validation failed: {Errors}", errorMessage);

                // Pick the most-specific structured key based on which validation entry tripped.
                // Order: missing API URL > missing API key > invalid recipient (anything else falls back to SendFailed).
                if (string.IsNullOrWhiteSpace(apiUrl))
                {
                    failureKey = "Error_MailService_MissingApiUrl";
                }
                else if (string.IsNullOrWhiteSpace(apiKey))
                {
                    failureKey = "Error_MailService_MissingApiKey";
                }
                else if (validationErrors.Any(e => e.Contains("Recipient", StringComparison.OrdinalIgnoreCase)))
                {
                    failureKey = "Error_MailService_InvalidRecipient";
                }
                else
                {
                    failureKey = "Error_MailService_SendFailed";
                }

                // Log validation failure to database
                stopwatch.Stop();
                await _emailApiLogService.LogEmailApiCallAsync(
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

                // Surface localized friendly message but keep the diagnostic detail in errorMessage for the log.
                return OperationResult.Fail(failureKey, _localizer[failureKey].Value);
            }

            // Create HTTP client from factory (best practice for performance and connection pooling)
            using var httpClient = _httpClientFactory.CreateClient();

            // Build email payload + target endpoint. Calendar-eligible sends are routed to Felix's
            // /calendar or /allDayEvent sub-paths with the mail object wrapped alongside the event.
            var (targetUrl, payload) = BuildSendTarget(apiUrl!, fromAddress!, recipient, subject, htmlBody,
                calendarKind, eventStartUtc, eventEndUtc, eventLocation);
            requestUrl = targetUrl;

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

            // Send POST request to mail API (plain or calendar sub-path)
            HttpResponseMessage response = await httpClient.PostAsync(targetUrl, content);

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
                failureKey = "Error_MailService_SendFailed";
                _logger.LogError("Failed to send email to {Recipient}. Status: {StatusCode}, Error: {Error}",
                    recipient, responseStatusCode, errorMessage);
            }
        }
        catch (HttpRequestException httpEx)
        {
            success = false;
            errorMessage = $"Network error: {httpEx.Message}";
            failureKey = "Error_MailService_SendFailed";
            _logger.LogError(httpEx, "HTTP error while sending email to {Recipient}: {Message}",
                recipient, httpEx.Message);
        }
        catch (TaskCanceledException tcEx)
        {
            success = false;
            errorMessage = "Request timeout (30s exceeded)";
            failureKey = "Error_MailService_SendFailed";
            _logger.LogError(tcEx, "Email request to {Recipient} timed out: {Message}",
                recipient, tcEx.Message);
        }
        catch (Exception ex)
        {
            success = false;
            errorMessage = $"Unexpected error: {ex.Message}";
            failureKey = "Error_MailService_SendFailed";
            _logger.LogError(ex, "Unexpected error while sending email to {Recipient}: {Message}",
                recipient, ex.Message);
        }
        finally
        {
            // Always log to database for diagnostics (awaited so callers can read the log immediately)
            stopwatch.Stop();
            await _emailApiLogService.LogEmailApiCallAsync(
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

        if (success)
        {
            return OperationResult.Ok();
        }

        // Surface the underlying HTTP/network error verbatim so admins see what actually went wrong
        // (the localized SendFailed key is a fallback when the diagnostic message is unavailable).
        var key = failureKey ?? "Error_MailService_SendFailed";
        return OperationResult.Fail(key, errorMessage ?? _localizer[key].Value);
    }

    /// <summary>
    /// Send shift assignment notification email with formatted HTML template.
    /// </summary>
    public async Task<OperationResult> SendShiftAssignedEmailAsync(
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
            return OperationResult.Fail("Error_MailService_InvalidRecipient", _localizer["Error_MailService_InvalidRecipient"].Value);
        }

        var emailDir = _localizer["Dir"] == "rtl" ? "rtl" : "ltr";
        string subject = string.Format(CultureInfo.CurrentCulture, _localizer["Email_ShiftAssignedSubject"], _localization.FormatMediumDate(shiftDate));

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
            <p>{string.Format(CultureInfo.CurrentCulture, _localizer["Email_Hello"], $"<strong>{WebUtility.HtmlEncode(employeeName)}</strong>")},</p>
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

        // "Summon" the employee to the shift: deliver as a native Outlook calendar invite via Felix.
        var (startUtc, endUtc) = Helpers.IsraelTime.ShiftWindowUtc(shiftDate, startTime, endTime);
        return await SendCalendarMailAsync(recipientEmail, subject, htmlBody, CalendarEventKind.Timed, startUtc, endUtc, location: null);
    }

    /// <summary>
    /// Send shift change notification email with change description.
    /// </summary>
    public async Task<OperationResult> SendShiftChangedEmailAsync(
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
            return OperationResult.Fail("Error_MailService_InvalidRecipient", _localizer["Error_MailService_InvalidRecipient"].Value);
        }

        var emailDir = _localizer["Dir"] == "rtl" ? "rtl" : "ltr";
        string subject = string.Format(CultureInfo.CurrentCulture, _localizer["Email_ShiftChangedSubject"], _localization.FormatMediumDate(shiftDate));

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
            <p>{string.Format(CultureInfo.CurrentCulture, _localizer["Email_Hello"], $"<strong>{WebUtility.HtmlEncode(employeeName)}</strong>")},</p>
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
    public async Task<OperationResult> SendShiftDeletedEmailAsync(
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
            return OperationResult.Fail("Error_MailService_InvalidRecipient", _localizer["Error_MailService_InvalidRecipient"].Value);
        }

        var emailDir = _localizer["Dir"] == "rtl" ? "rtl" : "ltr";
        string subject = string.Format(CultureInfo.CurrentCulture, _localizer["Email_ShiftDeletedSubject"], _localization.FormatMediumDate(shiftDate));

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
            <p>{string.Format(CultureInfo.CurrentCulture, _localizer["Email_Hello"], $"<strong>{WebUtility.HtmlEncode(employeeName)}</strong>")},</p>
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
    public async Task<OperationResult> SendChoreAssignedEmailAsync(
        string recipientEmail,
        string employeeName,
        string choreTitle,
        DateOnly choreDate)
    {
        if (string.IsNullOrWhiteSpace(recipientEmail))
        {
            _logger.LogWarning("Cannot send chore assigned email: recipient email is null or empty");
            return OperationResult.Fail("Error_MailService_InvalidRecipient", _localizer["Error_MailService_InvalidRecipient"].Value);
        }

        var emailDir = _localizer["Dir"] == "rtl" ? "rtl" : "ltr";
        string subject = string.Format(CultureInfo.CurrentCulture, _localizer["Email_ChoreAssignedSubject"], _localization.FormatMediumDate(choreDate));

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
            <p>{string.Format(CultureInfo.CurrentCulture, _localizer["Email_Hello"], $"<strong>{WebUtility.HtmlEncode(employeeName)}</strong>")},</p>
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

        // "Summon" to the chore as an all-day calendar event (chores carry a date, no time window).
        var choreDt = choreDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        return await SendCalendarMailAsync(recipientEmail, subject, htmlBody, CalendarEventKind.AllDay, choreDt, choreDt, location: null);
    }

    /// <summary>
    /// Send chore cancellation notification email with formatted HTML template.
    /// </summary>
    public async Task<OperationResult> SendChoreCanceledEmailAsync(
        string recipientEmail,
        string employeeName,
        string choreTitle,
        DateOnly choreDate)
    {
        if (string.IsNullOrWhiteSpace(recipientEmail))
        {
            _logger.LogWarning("Cannot send chore canceled email: recipient email is null or empty");
            return OperationResult.Fail("Error_MailService_InvalidRecipient", _localizer["Error_MailService_InvalidRecipient"].Value);
        }

        var emailDir = _localizer["Dir"] == "rtl" ? "rtl" : "ltr";
        string subject = string.Format(CultureInfo.CurrentCulture, _localizer["Email_ChoreCanceledSubject"], _localization.FormatMediumDate(choreDate));

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
            <p>{string.Format(CultureInfo.CurrentCulture, _localizer["Email_Hello"], $"<strong>{WebUtility.HtmlEncode(employeeName)}</strong>")},</p>
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
    public async Task<OperationResult> SendAccountApprovedEmailAsync(
        string recipientEmail,
        string userName,
        string assignedRole,
        string companyName)
    {
        if (string.IsNullOrWhiteSpace(recipientEmail))
        {
            _logger.LogWarning("Cannot send account approved email: recipient email is null or empty");
            return OperationResult.Fail("Error_MailService_InvalidRecipient", _localizer["Error_MailService_InvalidRecipient"].Value);
        }

        var emailDir = _localizer["Dir"] == "rtl" ? "rtl" : "ltr";
        string subject = string.Format(CultureInfo.CurrentCulture, _localizer["Email_AccountApprovedSubject"], companyName);

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
                <h3>{string.Format(CultureInfo.CurrentCulture, _localizer["Email_WelcomeToCompany"], WebUtility.HtmlEncode(companyName))}</h3>
            </div>

            <p>{string.Format(CultureInfo.CurrentCulture, _localizer["Email_Hello"], $"<strong>{WebUtility.HtmlEncode(userName)}</strong>")},</p>
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
    public async Task<OperationResult> SendTimeOffApprovedEmailAsync(
        string recipientEmail,
        string employeeName,
        DateOnly startDate,
        DateOnly endDate)
    {
        if (string.IsNullOrWhiteSpace(recipientEmail))
        {
            _logger.LogWarning("Cannot send time-off approved email: recipient email is null or empty");
            return OperationResult.Fail("Error_MailService_InvalidRecipient", _localizer["Error_MailService_InvalidRecipient"].Value);
        }

        var emailDir = _localizer["Dir"] == "rtl" ? "rtl" : "ltr";
        var dateRange = startDate == endDate
            ? _localization.FormatMediumDate(startDate)
            : $"{_localization.FormatMediumDate(startDate)} - {_localization.FormatMediumDate(endDate)}";

        string subject = string.Format(CultureInfo.CurrentCulture, _localizer["Email_TimeOffApprovedSubject"], dateRange);

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
            <p>{string.Format(CultureInfo.CurrentCulture, _localizer["Email_Hello"], $"<strong>{WebUtility.HtmlEncode(employeeName)}</strong>")},</p>
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

        // Approved time-off becomes an all-day calendar event (vacation / Day-At-X spans dates).
        var toStart = startDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var toEnd = endDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        return await SendCalendarMailAsync(recipientEmail, subject, htmlBody, CalendarEventKind.AllDay, toStart, toEnd, location: null);
    }

    /// <summary>
    /// Send time-off request declined notification email with formatted HTML template.
    /// </summary>
    public async Task<OperationResult> SendTimeOffDeclinedEmailAsync(
        string recipientEmail,
        string employeeName,
        DateOnly startDate,
        DateOnly endDate)
    {
        if (string.IsNullOrWhiteSpace(recipientEmail))
        {
            _logger.LogWarning("Cannot send time-off declined email: recipient email is null or empty");
            return OperationResult.Fail("Error_MailService_InvalidRecipient", _localizer["Error_MailService_InvalidRecipient"].Value);
        }

        var emailDir = _localizer["Dir"] == "rtl" ? "rtl" : "ltr";
        var dateRange = startDate == endDate
            ? _localization.FormatMediumDate(startDate)
            : $"{_localization.FormatMediumDate(startDate)} - {_localization.FormatMediumDate(endDate)}";

        string subject = string.Format(CultureInfo.CurrentCulture, _localizer["Email_TimeOffDeclinedSubject"], dateRange);

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
            <p>{string.Format(CultureInfo.CurrentCulture, _localizer["Email_Hello"], $"<strong>{WebUtility.HtmlEncode(employeeName)}</strong>")},</p>
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
    public async Task<OperationResult> SendTimeOffDeletedEmailAsync(
        string recipientEmail,
        string employeeName,
        DateOnly startDate,
        DateOnly endDate)
    {
        if (string.IsNullOrWhiteSpace(recipientEmail))
        {
            _logger.LogWarning("Cannot send time-off deleted email: recipient email is null or empty");
            return OperationResult.Fail("Error_MailService_InvalidRecipient", _localizer["Error_MailService_InvalidRecipient"].Value);
        }

        var emailDir = _localizer["Dir"] == "rtl" ? "rtl" : "ltr";
        var dateRange = startDate == endDate
            ? _localization.FormatMediumDate(startDate)
            : $"{_localization.FormatMediumDate(startDate)} - {_localization.FormatMediumDate(endDate)}";

        string subject = string.Format(CultureInfo.CurrentCulture, _localizer["Email_TimeOffDeletedSubject"], dateRange);

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
            <p>{string.Format(CultureInfo.CurrentCulture, _localizer["Email_Hello"], $"<strong>{WebUtility.HtmlEncode(employeeName)}</strong>")},</p>
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
    public async Task<OperationResult> SendSwapRequestApprovedEmailAsync(
        string recipientEmail,
        string employeeName,
        string shiftInfo)
    {
        if (string.IsNullOrWhiteSpace(recipientEmail))
        {
            _logger.LogWarning("Cannot send swap request approved email: recipient email is null or empty");
            return OperationResult.Fail("Error_MailService_InvalidRecipient", _localizer["Error_MailService_InvalidRecipient"].Value);
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
            <p>{string.Format(CultureInfo.CurrentCulture, _localizer["Email_Hello"], $"<strong>{WebUtility.HtmlEncode(employeeName)}</strong>")},</p>
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
    public async Task<OperationResult> SendSwapRequestDeclinedEmailAsync(
        string recipientEmail,
        string employeeName,
        string shiftInfo)
    {
        if (string.IsNullOrWhiteSpace(recipientEmail))
        {
            _logger.LogWarning("Cannot send swap request declined email: recipient email is null or empty");
            return OperationResult.Fail("Error_MailService_InvalidRecipient", _localizer["Error_MailService_InvalidRecipient"].Value);
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
            <p>{string.Format(CultureInfo.CurrentCulture, _localizer["Email_Hello"], $"<strong>{WebUtility.HtmlEncode(employeeName)}</strong>")},</p>
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
    public async Task<OperationResult> SendOnDutyAssignedEmailAsync(
        string recipientEmail,
        string employeeName,
        string onDutyTypeName,
        DateOnly onDutyDate)
    {
        if (string.IsNullOrWhiteSpace(recipientEmail))
        {
            _logger.LogWarning("Cannot send on-duty assigned email: recipient email is null or empty");
            return OperationResult.Fail("Error_MailService_InvalidRecipient", _localizer["Error_MailService_InvalidRecipient"].Value);
        }

        var emailDir = _localizer["Dir"] == "rtl" ? "rtl" : "ltr";
        string subject = string.Format(CultureInfo.CurrentCulture, _localizer["Email_OnDutyAssignedSubject"], _localization.FormatMediumDate(onDutyDate));

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
            <p>{string.Format(CultureInfo.CurrentCulture, _localizer["Email_Hello"], $"<strong>{WebUtility.HtmlEncode(employeeName)}</strong>")},</p>
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

        // "Summon" to the on-duty assignment as an all-day calendar event.
        var onDutyDt = onDutyDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        return await SendCalendarMailAsync(recipientEmail, subject, htmlBody, CalendarEventKind.AllDay, onDutyDt, onDutyDt, location: null);
    }

    /// <summary>
    /// Send on-duty cancellation notification email with formatted HTML template.
    /// </summary>
    public async Task<OperationResult> SendOnDutyCanceledEmailAsync(
        string recipientEmail,
        string employeeName,
        string onDutyTypeName,
        DateOnly onDutyDate)
    {
        if (string.IsNullOrWhiteSpace(recipientEmail))
        {
            _logger.LogWarning("Cannot send on-duty canceled email: recipient email is null or empty");
            return OperationResult.Fail("Error_MailService_InvalidRecipient", _localizer["Error_MailService_InvalidRecipient"].Value);
        }

        var emailDir = _localizer["Dir"] == "rtl" ? "rtl" : "ltr";
        string subject = string.Format(CultureInfo.CurrentCulture, _localizer["Email_OnDutyCanceledSubject"], _localization.FormatMediumDate(onDutyDate));

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
            <p>{string.Format(CultureInfo.CurrentCulture, _localizer["Email_Hello"], $"<strong>{WebUtility.HtmlEncode(employeeName)}</strong>")},</p>
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
    public async Task<OperationResult> SendAccessRequestSubmittedEmailAsync(
        string recipientEmail,
        string ownerName,
        string requesterName,
        string requesterEmail,
        string companyName)
    {
        if (string.IsNullOrWhiteSpace(recipientEmail))
        {
            _logger.LogWarning("Cannot send access request submitted email: recipient email is null or empty");
            return OperationResult.Fail("Error_MailService_InvalidRecipient", _localizer["Error_MailService_InvalidRecipient"].Value);
        }

        var emailDir = _localizer["Dir"] == "rtl" ? "rtl" : "ltr";
        string subject = string.Format(CultureInfo.CurrentCulture, _localizer["Email_AccessRequestSubmittedSubject"], requesterName);

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
            <p>{string.Format(CultureInfo.CurrentCulture, _localizer["Email_Hello"], $"<strong>{WebUtility.HtmlEncode(ownerName)}</strong>")},</p>
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

    public async Task<OperationResult> SendTraineeAddedEmailAsync(string recipientEmail, string employeeName,
        string traineeName, string shiftTypeName, DateOnly shiftDate, TimeOnly startTime, TimeOnly endTime)
    {
        var subject = string.Format(CultureInfo.CurrentCulture, _localizer["Email_TraineeAdded_Subject"], shiftTypeName, _localization.FormatMediumDate(shiftDate));

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
            <h2>{_localizer["Email_TraineeAdded_Title"]}</h2>
        </div>
        <div class='content'>
            <p>{string.Format(CultureInfo.CurrentCulture, _localizer["Email_Hello"], $"<strong>{WebUtility.HtmlEncode(employeeName)}</strong>")},</p>
            <p>{_localizer["Email_TraineeAdded_Body"]}</p>

            <div class='highlight'>
                {string.Format(CultureInfo.CurrentCulture, _localizer["Email_TraineeAdded_Highlight"], $"<strong>{WebUtility.HtmlEncode(traineeName)}</strong>")}
            </div>

            <div class='details'>
                <p><strong>{_localizer["Email_ShiftType"]}:</strong> {WebUtility.HtmlEncode(shiftTypeName)}</p>
                <p><strong>{_localizer["Date"]}:</strong> {_localization.FormatMediumDate(shiftDate)}</p>
                <p><strong>{_localizer["Time"]}:</strong> {_localization.FormatTime(startTime)} - {_localization.FormatTime(endTime)}</p>
            </div>

            <p>{_localizer["Email_TraineeAdded_Guidance"]}</p>
        </div>
        <div class='footer'>
            <p>{_localizer["Email_AutomatedMessage"]}</p>
        </div>
    </div>
</body>
</html>";

        return await SendMailAsync(recipientEmail, subject, htmlBody);
    }

    public async Task<OperationResult> SendSlotRemovedEmailAsync(string recipientEmail, string employeeName,
        string shiftTypeName, DateOnly shiftDate, TimeOnly startTime, TimeOnly endTime, string reason)
    {
        var subject = string.Format(CultureInfo.CurrentCulture, _localizer["Email_SlotRemoved_Subject"], shiftTypeName, _localization.FormatMediumDate(shiftDate));

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
            <h2>{_localizer["Email_SlotRemoved_Title"]}</h2>
        </div>
        <div class='content'>
            <p>{string.Format(CultureInfo.CurrentCulture, _localizer["Email_Hello"], $"<strong>{WebUtility.HtmlEncode(employeeName)}</strong>")},</p>
            <p>{_localizer["Email_SlotRemoved_Body"]}</p>

            <div class='details'>
                <p><strong>{_localizer["Email_ShiftType"]}:</strong> {WebUtility.HtmlEncode(shiftTypeName)}</p>
                <p><strong>{_localizer["Date"]}:</strong> {_localization.FormatMediumDate(shiftDate)}</p>
                <p><strong>{_localizer["Time"]}:</strong> {_localization.FormatTime(startTime)} - {_localization.FormatTime(endTime)}</p>
            </div>

            <div class='alert-box'>
                <p><strong>{_localizer["Email_SlotRemoved_Reason"]}:</strong> {WebUtility.HtmlEncode(reason)}</p>
            </div>

            <p>{_localizer["Email_SlotRemoved_ContactManager"]}</p>
        </div>
        <div class='footer'>
            <p>{_localizer["Email_AutomatedMessage"]}</p>
        </div>
    </div>
</body>
</html>";

        return await SendMailAsync(recipientEmail, subject, htmlBody);
    }

    public async Task<OperationResult> SendShiftModifiedEmailAsync(string recipientEmail, string employeeName,
        string shiftTypeName, DateOnly shiftDate, string changeDescription)
    {
        var subject = string.Format(CultureInfo.CurrentCulture, _localizer["Email_ShiftModified_Subject"], shiftTypeName, _localization.FormatMediumDate(shiftDate));

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
            <h2>{_localizer["Email_ShiftModified_Title"]}</h2>
        </div>
        <div class='content'>
            <p>{string.Format(CultureInfo.CurrentCulture, _localizer["Email_Hello"], $"<strong>{WebUtility.HtmlEncode(employeeName)}</strong>")},</p>
            <p>{_localizer["Email_ShiftModified_Body"]}</p>

            <div class='details'>
                <p><strong>{_localizer["Email_ShiftType"]}:</strong> {WebUtility.HtmlEncode(shiftTypeName)}</p>
                <p><strong>{_localizer["Date"]}:</strong> {_localization.FormatMediumDate(shiftDate)}</p>
            </div>

            <div class='change-box'>
                <p><strong>{_localizer["Email_ShiftModified_Changes"]}:</strong> {WebUtility.HtmlEncode(changeDescription)}</p>
            </div>

            <p>{_localizer["Email_ShiftModified_ReviewPrompt"]}</p>
        </div>
        <div class='footer'>
            <p>{_localizer["Email_AutomatedMessage"]}</p>
        </div>
    </div>
</body>
</html>";

        return await SendMailAsync(recipientEmail, subject, htmlBody);
    }
}
