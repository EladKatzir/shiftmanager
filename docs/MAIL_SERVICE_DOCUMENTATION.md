# ShiftManager Email Service - Technical Documentation

**Version**: 1.0
**Date**: December 15, 2025
**File**: `Services/MailService.cs` (651 lines)

---

## Executive Summary

ShiftManager's email notification system is a production-ready service that sends automated emails for shift assignments, changes, chores, and user account events.

**Key Features**:
- ✅ **6 Email Functions** (1 core + 5 specialized)
- ✅ **Dual Configuration** (Database per-company + appsettings.json fallback)
- ✅ **Full Localization** (English + Hebrew RTL)
- ✅ **Comprehensive Logging** (Every API call logged to database)
- ✅ **Production-Ready** (30s timeouts, validation, error handling)

---

## System Architecture

```
┌──────────────────┐
│   Application    │  (Pages/Admin/Users, Services, etc.)
└────────┬─────────┘
         │
         ▼
┌─────────────────────────────────────────────┐
│           IMailService Implementation       │
├─────────────────────────────────────────────┤
│  • SendShiftAssignedEmailAsync()            │
│  • SendShiftChangedEmailAsync()             │
│  • SendShiftDeletedEmailAsync()             │
│  • SendChoreAssignedEmailAsync()            │
│  • SendChoreCanceledEmailAsync()            │
│  • SendMailAsync() [CORE FUNCTION]          │
└────────┬─────────────────────────────────── │
         │
    ┌────┴────┐
    │         │
    ▼         ▼
┌─────────┐ ┌──────────────┐
│Database │ │appsettings   │
│ Config  │ │  (fallback)  │
└────┬────┘ └─────┬────────┘
     │            │
     └────┬───────┘
          │
          ▼
  ┌───────────────┐
  │   Validate    │
  │Configuration  │
  └───────┬───────┘
          │
          ▼
  ┌───────────────┐
  │HTTP POST to   │
  │  Mail API     │
  │ (30s timeout) │
  └───────┬───────┘
          │
     ┌────┴────┐
     │         │
     ▼         ▼
┌─────────┐ ┌─────────┐
│Success  │ │ Error   │
│  200    │ │4xx/5xx  │
└────┬────┘ └────┬────┘
     │           │
     └─────┬─────┘
           │
           ▼
   ┌───────────────┐
   │EmailApiLog DB │
   │+ Struct Logs  │
   └───────────────┘
```

---

## Configuration

### Database Configuration (Priority 1)

**Table**: `EmailConfigs`

```sql
Id              INTEGER PRIMARY KEY
CompanyId       INTEGER NOT NULL
Enabled         INTEGER NOT NULL DEFAULT 0
ApiUrl          TEXT NOT NULL
EncryptedApiKey TEXT NOT NULL  -- AES-256 encrypted
FromAddress     TEXT NOT NULL
```

**Example**:
```sql
INSERT INTO EmailConfigs (CompanyId, Enabled, ApiUrl, EncryptedApiKey, FromAddress)
VALUES (1, 1, 'https://mail-api.company.com/send', '[encrypted-key]', 'noreply@company.com');
```

### appsettings.json Fallback (Priority 2)

```json
{
  "Email": {
    "Enabled": false,
    "ApiKey": "your-api-key-here",
    "ApiUrl": "https://mail-api.example.com/send",
    "FromAddress": "noreply@shiftmanager.local"
  }
}
```

---

## API Integration

### HTTP Request Format

```http
POST {ApiUrl}
Content-Type: application/json
Apikey: {ApiKey}

Request Body (JSON):
{
  "from": "noreply@company.com",
  "to": "employee@example.com",
  "subject": "New Shift Assignment - Dec 15, 2025",
  "html": "<html>...</html>"
}
```

### Expected API Responses

**Success (200 OK)**:
```json
{
  "status": "sent",
  "messageId": "abc123..."
}
```

**Error (401 Unauthorized)**:
```json
{
  "error": "Invalid API key"
}
```

---

## Full Function Implementations

### CORE FUNCTION: SendMailAsync

**Purpose**: Foundation for all email sending. All specialized functions call this.

```csharp
public async Task<bool> SendMailAsync(string recipient, string subject, string htmlBody)
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
                validationErrors: validationErrors);

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

        // Capture request headers for diagnostics
        requestHeaders = new Dictionary<string, string>
        {
            { "Apikey", apiKey! },
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
            validationErrors: validationErrors);
    }

    return success;
}
```

**Flow**:
1. ✅ Validate inputs (recipient, subject)
2. ✅ Load configuration (database first, fallback to appsettings.json)
3. ✅ Check if email is enabled (graceful skip if disabled)
4. ✅ Validate configuration (URL, API key, recipient format)
5. ✅ Build HTTP POST request with JSON payload
6. ✅ Add Apikey header
7. ✅ Send request with 30-second timeout
8. ✅ Parse response and determine success/failure
9. ✅ **Always** log to database (even on exceptions)

---

### FUNCTION 1: SendShiftAssignedEmailAsync

**Purpose**: Notify employee of new shift assignment
**Theme**: Green (#4CAF50)

```csharp
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
    string subject = string.Format(_localizer["Email_ShiftAssignedSubject"], shiftDate.ToString("MMM dd, yyyy"));

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
            <p>{string.Format(_localizer["Email_Hello"], $"<strong>{employeeName}</strong>")},</p>
            <p>{_localizer["Email_ShiftAssignedBody"]}</p>

            <div class='shift-details'>
                <p><strong>{_localizer["Email_ShiftType"]}:</strong> <span class='highlight'>{shiftTypeName}</span></p>
                <p><strong>{_localizer["Date"]}:</strong> {shiftDate:dddd, MMMM dd, yyyy}</p>
                <p><strong>{_localizer["Time"]}:</strong> {startTime:HH:mm} - {endTime:HH:mm}</p>
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
```

---

### FUNCTION 2: SendShiftChangedEmailAsync

**Purpose**: Notify employee of shift modification
**Theme**: Orange (#FF9800)

```csharp
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
    string subject = string.Format(_localizer["Email_ShiftChangedSubject"], shiftDate.ToString("MMM dd, yyyy"));

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
            <p>{string.Format(_localizer["Email_Hello"], $"<strong>{employeeName}</strong>")},</p>
            <p>{_localizer["Email_ShiftChangedBody"]}</p>

            <div class='shift-details'>
                <p><strong>{_localizer["Email_ShiftType"]}:</strong> <span class='highlight'>{shiftTypeName}</span></p>
                <p><strong>{_localizer["Date"]}:</strong> {shiftDate:dddd, MMMM dd, yyyy}</p>
                <p><strong>{_localizer["Time"]}:</strong> {startTime:HH:mm} - {endTime:HH:mm}</p>

                <div class='change-notice'>
                    <p><strong>{_localizer["Email_ChangeDetails"]}:</strong> {changeDescription}</p>
                </div>
            </div>

            <p>{_localizer["Email_ShiftChangedSchedulePrompt"]}</p>
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
```

---

### FUNCTION 3: SendShiftDeletedEmailAsync

**Purpose**: Notify employee of shift cancellation
**Theme**: Red (#F44336)

```csharp
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
    string subject = string.Format(_localizer["Email_ShiftDeletedSubject"], shiftDate.ToString("MMM dd, yyyy"));

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
            <p>{string.Format(_localizer["Email_Hello"], $"<strong>{employeeName}</strong>")},</p>
            <p>{_localizer["Email_ShiftDeletedBody"]}</p>

            <div class='shift-details'>
                <p><strong>{_localizer["Email_ShiftType"]}:</strong> <span class='highlight'>{shiftTypeName}</span></p>
                <p><strong>{_localizer["Date"]}:</strong> {shiftDate:dddd, MMMM dd, yyyy}</p>
                <p><strong>{_localizer["Time"]}:</strong> {startTime:HH:mm} - {endTime:HH:mm}</p>
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
```

---

### FUNCTION 4: SendChoreAssignedEmailAsync

**Purpose**: Notify employee of chore assignment
**Theme**: Blue (#2196F3)

```csharp
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
    string subject = string.Format(_localizer["Email_ChoreAssignedSubject"], choreDate.ToString("MMM dd, yyyy"));

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
            <p>{string.Format(_localizer["Email_Hello"], $"<strong>{employeeName}</strong>")},</p>
            <p>{_localizer["Email_ChoreAssignedBody"]}</p>

            <div class='chore-details'>
                <p><strong>{_localizer["Email_Chore"]}:</strong> <span class='highlight'>{choreTitle}</span></p>
                <p><strong>{_localizer["Date"]}:</strong> {choreDate:dddd, MMMM dd, yyyy}</p>
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
```

---

### FUNCTION 5: SendChoreCanceledEmailAsync

**Purpose**: Notify employee of chore cancellation
**Theme**: Orange (#FF9800)

```csharp
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
    string subject = string.Format(_localizer["Email_ChoreCanceledSubject"], choreDate.ToString("MMM dd, yyyy"));

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
            <p>{string.Format(_localizer["Email_Hello"], $"<strong>{employeeName}</strong>")},</p>
            <p>{_localizer["Email_ChoreCanceledBody"]}</p>

            <div class='chore-details'>
                <p><strong>{_localizer["Email_Chore"]}:</strong> <span class='highlight'>{choreTitle}</span></p>
                <p><strong>{_localizer["Date"]}:</strong> {choreDate:dddd, MMMM dd, yyyy}</p>
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
```

---

## Logging & Diagnostics

### Database Table: EmailApiLogs

Every email attempt is logged with full diagnostics:

| Column | Description |
|--------|-------------|
| `RequestUrl` | API endpoint URL |
| `RequestMethod` | Always "POST" |
| `RequestHeaders` | JSON dictionary of headers |
| `RequestBody` | Full JSON payload sent |
| `ResponseStatusCode` | HTTP status code (200, 401, etc.) |
| `ResponseBody` | API response text |
| `RecipientEmail` | Recipient address |
| `EmailSubject` | Email subject line |
| `Success` | 1=success, 0=failure |
| `ErrorMessage` | User-friendly error (if failed) |
| `DurationMs` | Milliseconds elapsed |
| `ValidationErrors` | List of validation failures (if any) |
| `Timestamp` | UTC timestamp |
| `CompanyId` | Company identifier |

**Example Log**:
```json
{
  "requestUrl": "https://mail-api.company.com/send",
  "responseStatusCode": 200,
  "recipientEmail": "employee@company.com",
  "emailSubject": "New Shift Assignment - Dec 15, 2025",
  "success": true,
  "durationMs": 487,
  "timestamp": "2025-12-15T10:30:00Z",
  "companyId": 1
}
```

### Structured Application Logs

**Success**:
```
[Info] Sending email to employee@company.com (config source: database)
[Info] Email sent successfully. Status: 200
```

**Failure**:
```
[Error] Email configuration validation failed: API key is not configured
[Error] Failed to send email. Status: 401, Error: API key appears invalid
```

---

## Error Handling

### Pre-Flight Validation

Checked **before** sending:
- ✅ API URL configured
- ✅ API URL valid format (http/https)
- ✅ API key configured
- ✅ API key length >= 8 characters
- ✅ Recipient email not empty
- ✅ Recipient email contains @

### HTTP Status Codes

| Code | Meaning | User-Friendly Message |
|------|---------|----------------------|
| 200 | Success | Email sent successfully |
| 401 | Unauthorized | API key appears invalid or expired |
| 403 | Forbidden | Access forbidden - check API permissions |
| 404 | Not Found | API endpoint not found - check URL |
| 429 | Rate Limit | Rate limit exceeded - wait before retrying |
| 500 | Server Error | Email service server error |
| 502 | Bad Gateway | Email service may be temporarily unavailable |
| 503 | Unavailable | Service unavailable - may be under maintenance |
| 504 | Timeout | Gateway timeout - service took too long |

### Exception Handling

- `HttpRequestException`: Network errors (DNS, connection refused, SSL)
- `TaskCanceledException`: 30-second timeout exceeded
- `Exception`: Unexpected errors

All exceptions are caught, logged, and never crash the application.

---

## Summary

**Production-Ready Features**:
- ✅ HTTP client factory (connection pooling)
- ✅ AES-256 encrypted API keys
- ✅ 30-second timeout protection
- ✅ Pre-flight validation (7 checks)
- ✅ Comprehensive logging (database + structured logs)
- ✅ Localization support (English + Hebrew RTL)
- ✅ Graceful degradation (returns true if disabled)
- ✅ Fire-and-forget logging (doesn't block on log failures)

**Statistics**:
- **Total Functions**: 6 (1 core + 5 specialized)
- **Configuration Sources**: 2 (database + appsettings.json)
- **Supported Languages**: 2 (English + Hebrew)
- **Email Themes**: 5 (Green, Orange, Red, Blue, Orange)
- **Total Code**: 651 lines
- **Validation Checks**: 7 pre-flight
- **Timeout**: 30 seconds
- **Logging Destinations**: 2 (database + logs)

---

**End of Documentation**
