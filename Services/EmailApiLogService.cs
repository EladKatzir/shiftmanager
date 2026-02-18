using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;

namespace ShiftManager.Services;

/// <summary>
/// Service for managing email API diagnostic logs.
/// Provides defensive logging - never throws exceptions.
/// </summary>
public class EmailApiLogService : IEmailApiLogService
{
    private readonly AppDbContext _context;
    private readonly ILogger<EmailApiLogService> _logger;

    public EmailApiLogService(AppDbContext context, ILogger<EmailApiLogService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<EmailApiLog> LogEmailApiCallAsync(
        string requestUrl,
        string requestMethod,
        Dictionary<string, string> requestHeaders,
        string requestBody,
        int? responseStatusCode,
        Dictionary<string, string>? responseHeaders,
        string? responseBody,
        string recipientEmail,
        string emailSubject,
        bool success,
        string? errorMessage,
        int durationMs,
        List<string>? validationErrors = null,
        int companyId = 0)
    {
        try
        {
            // Obfuscate API keys in headers for security
            var sanitizedHeaders = ObfuscateApiKeysInHeaders(requestHeaders);

            var log = new EmailApiLog
            {
                // Pre-set CompanyId when provided (non-zero) so CompanyIdInterceptor skips resolution.
                // This enables persistence from background services that lack HTTP tenant context.
                CompanyId = companyId,
                RequestUrl = requestUrl,
                RequestMethod = requestMethod,
                RequestHeaders = SerializeToJson(sanitizedHeaders),
                RequestBody = requestBody,
                ResponseStatusCode = responseStatusCode,
                ResponseHeaders = responseHeaders != null ? SerializeToJson(responseHeaders) : null,
                ResponseBody = responseBody,
                RecipientEmail = recipientEmail,
                EmailSubject = emailSubject,
                Success = success,
                ErrorMessage = errorMessage,
                DurationMs = durationMs,
                Timestamp = DateTime.UtcNow,
                ValidationErrors = validationErrors != null && validationErrors.Any()
                    ? SerializeToJson(validationErrors)
                    : null
            };

            _context.EmailApiLogs.Add(log);
            await _context.SaveChangesAsync();

            _logger.LogInformation(
                "Email API log created: {Success} to {Recipient}, Status: {StatusCode}, Duration: {Duration}ms",
                success ? "SUCCESS" : "FAILURE",
                recipientEmail,
                responseStatusCode?.ToString() ?? "N/A",
                durationMs);

            return log;
        }
        catch (Exception ex)
        {
            // Defensive: Never throw from logging code
            _logger.LogError(ex, "Failed to create email API log entry");

            // Return a minimal log object (not persisted)
            return new EmailApiLog
            {
                Success = success,
                ErrorMessage = errorMessage,
                Timestamp = DateTime.UtcNow
            };
        }
    }

    public async Task<List<EmailApiLog>> GetRecentLogsAsync(int count = 100)
    {
        try
        {
            return await _context.EmailApiLogs
                .OrderByDescending(l => l.Timestamp)
                .Take(count)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve recent email API logs");
            return new List<EmailApiLog>();
        }
    }

    public async Task<List<EmailApiLog>> GetFailedLogsAsync(int count = 50)
    {
        try
        {
            return await _context.EmailApiLogs
                .Where(l => !l.Success)
                .OrderByDescending(l => l.Timestamp)
                .Take(count)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve failed email API logs");
            return new List<EmailApiLog>();
        }
    }

    public async Task<EmailApiLog?> GetLogByIdAsync(int id)
    {
        try
        {
            return await _context.EmailApiLogs.FindAsync(id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve email API log by ID: {Id}", id);
            return null;
        }
    }

    public async Task<int> CleanupOldLogsAsync(int retentionDays = 90)
    {
        try
        {
            var cutoffDate = DateTime.UtcNow.AddDays(-retentionDays);
            var oldLogs = await _context.EmailApiLogs
                .Where(l => l.Timestamp < cutoffDate)
                .ToListAsync();

            if (oldLogs.Any())
            {
                _context.EmailApiLogs.RemoveRange(oldLogs);
                await _context.SaveChangesAsync();

                _logger.LogInformation(
                    "Cleaned up {Count} old email API logs (older than {Days} days)",
                    oldLogs.Count,
                    retentionDays);

                return oldLogs.Count;
            }

            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cleanup old email API logs");
            return 0;
        }
    }

    /// <summary>
    /// Obfuscates API keys in headers for security.
    /// Shows only first 4 and last 4 characters.
    /// </summary>
    private Dictionary<string, string> ObfuscateApiKeysInHeaders(Dictionary<string, string> headers)
    {
        var sanitized = new Dictionary<string, string>(headers);

        // Common header names that might contain API keys
        var sensitiveHeaders = new[] { "X-API-Key", "Authorization", "X-Auth-Token", "Api-Key" };

        foreach (var headerName in sensitiveHeaders)
        {
            if (sanitized.ContainsKey(headerName))
            {
                sanitized[headerName] = ObfuscateApiKey(sanitized[headerName]);
            }
        }

        return sanitized;
    }

    /// <summary>
    /// Obfuscates an API key by showing only first 4 and last 4 characters.
    /// </summary>
    private string ObfuscateApiKey(string apiKey)
    {
        if (string.IsNullOrEmpty(apiKey) || apiKey.Length <= 8)
        {
            return "****";
        }

        return $"{apiKey.Substring(0, 4)}...{apiKey.Substring(apiKey.Length - 4)}";
    }

    /// <summary>
    /// Serializes an object to JSON string.
    /// Returns null if serialization fails.
    /// </summary>
    private string? SerializeToJson(object obj)
    {
        try
        {
            return JsonSerializer.Serialize(obj, new JsonSerializerOptions
            {
                WriteIndented = false
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to serialize object to JSON");
            return null;
        }
    }
}
