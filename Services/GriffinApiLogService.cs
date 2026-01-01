using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;

namespace ShiftManager.Services;

/// <summary>
/// Service for managing Griffin ADFS connection test diagnostic logs.
/// Provides defensive logging - never throws exceptions.
/// </summary>
public class GriffinApiLogService : IGriffinApiLogService
{
    private readonly AppDbContext _context;
    private readonly ILogger<GriffinApiLogService> _logger;

    public GriffinApiLogService(AppDbContext context, ILogger<GriffinApiLogService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<GriffinApiLog> LogConnectionTestAsync(
        string requestUrl,
        string requestMethod,
        Dictionary<string, string> requestHeaders,
        int? responseStatusCode,
        Dictionary<string, string>? responseHeaders,
        string? responseBody,
        string? redirectUrl,
        bool success,
        string? errorMessage,
        int durationMs,
        List<string>? validationErrors = null)
    {
        try
        {
            // Obfuscate sensitive headers for security
            var sanitizedHeaders = ObfuscateSensitiveHeaders(requestHeaders);

            var log = new GriffinApiLog
            {
                RequestUrl = requestUrl,
                RequestMethod = requestMethod,
                RequestHeaders = SerializeToJson(sanitizedHeaders),
                ResponseStatusCode = responseStatusCode,
                ResponseHeaders = responseHeaders != null ? SerializeToJson(responseHeaders) : null,
                ResponseBody = responseBody,
                RedirectUrl = redirectUrl,
                Success = success,
                ErrorMessage = errorMessage,
                DurationMs = durationMs,
                Timestamp = DateTime.UtcNow,
                ValidationErrors = validationErrors != null && validationErrors.Any()
                    ? SerializeToJson(validationErrors)
                    : null
            };

            _context.GriffinApiLogs.Add(log);
            await _context.SaveChangesAsync();

            _logger.LogInformation(
                "Griffin connection test log created: {Success}, Status: {StatusCode}, Duration: {Duration}ms, RedirectUrl: {RedirectUrl}",
                success ? "SUCCESS" : "FAILURE",
                responseStatusCode?.ToString() ?? "N/A",
                durationMs,
                redirectUrl ?? "N/A");

            return log;
        }
        catch (Exception ex)
        {
            // Defensive: Never throw from logging code
            _logger.LogError(ex, "Failed to create Griffin connection test log entry");

            // Return a minimal log object (not persisted)
            return new GriffinApiLog
            {
                Success = success,
                ErrorMessage = errorMessage,
                Timestamp = DateTime.UtcNow
            };
        }
    }

    public async Task<List<GriffinApiLog>> GetRecentLogsAsync(int count = 100)
    {
        try
        {
            return await _context.GriffinApiLogs
                .OrderByDescending(l => l.Timestamp)
                .Take(count)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve recent Griffin connection test logs");
            return new List<GriffinApiLog>();
        }
    }

    public async Task<List<GriffinApiLog>> GetFailedLogsAsync(int count = 50)
    {
        try
        {
            return await _context.GriffinApiLogs
                .Where(l => !l.Success)
                .OrderByDescending(l => l.Timestamp)
                .Take(count)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve failed Griffin connection test logs");
            return new List<GriffinApiLog>();
        }
    }

    public async Task<GriffinApiLog?> GetLogByIdAsync(int id)
    {
        try
        {
            return await _context.GriffinApiLogs.FindAsync(id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve Griffin connection test log by ID: {Id}", id);
            return null;
        }
    }

    public async Task<int> CleanupOldLogsAsync(int retentionDays = 90)
    {
        try
        {
            var cutoffDate = DateTime.UtcNow.AddDays(-retentionDays);
            var oldLogs = await _context.GriffinApiLogs
                .Where(l => l.Timestamp < cutoffDate)
                .ToListAsync();

            if (oldLogs.Any())
            {
                _context.GriffinApiLogs.RemoveRange(oldLogs);
                await _context.SaveChangesAsync();

                _logger.LogInformation(
                    "Cleaned up {Count} old Griffin connection test logs (older than {Days} days)",
                    oldLogs.Count,
                    retentionDays);

                return oldLogs.Count;
            }

            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cleanup old Griffin connection test logs");
            return 0;
        }
    }

    /// <summary>
    /// Obfuscates sensitive headers for security.
    /// Shows only first 4 and last 4 characters for sensitive values.
    /// </summary>
    private Dictionary<string, string> ObfuscateSensitiveHeaders(Dictionary<string, string> headers)
    {
        var sanitized = new Dictionary<string, string>(headers);

        // Common header names that might contain sensitive data
        var sensitiveHeaders = new[] { "Authorization", "Cookie", "X-Auth-Token", "X-API-Key" };

        foreach (var headerName in sensitiveHeaders)
        {
            if (sanitized.ContainsKey(headerName))
            {
                sanitized[headerName] = ObfuscateSensitiveValue(sanitized[headerName]);
            }
        }

        return sanitized;
    }

    /// <summary>
    /// Obfuscates a sensitive value by showing only first 4 and last 4 characters.
    /// </summary>
    private string ObfuscateSensitiveValue(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= 8)
        {
            return "****";
        }

        return $"{value.Substring(0, 4)}...{value.Substring(value.Length - 4)}";
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
