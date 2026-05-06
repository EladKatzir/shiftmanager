using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ShiftManager.Helpers;

/// <summary>
/// B-016: Cache helper utilities for stale cache handling
/// Provides ETag generation and cache validation support
/// </summary>
public static class CacheHelper
{
    /// <summary>
    /// Generates an ETag from data object by computing a hash of its JSON representation
    /// </summary>
    /// <param name="data">The data object to generate an ETag for</param>
    /// <returns>A quoted ETag string suitable for HTTP headers</returns>
    public static string GenerateETag(object data)
    {
        var json = JsonSerializer.Serialize(data);
        // Batch M (F-C-012): SHA256 instead of MD5. ETags don't need crypto strength,
        // but CA5351 flags MD5 as broken-crypto category. SHA256 is in the BCL, no
        // additional package needed. Existing ETag-comparison logic doesn't care about
        // hash length, so the switch is transparent to clients.
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return $"\"{Convert.ToBase64String(hash)}\"";
    }

    /// <summary>
    /// Generates an ETag from a string representation
    /// </summary>
    /// <param name="content">The string content to generate an ETag for</param>
    /// <returns>A quoted ETag string suitable for HTTP headers</returns>
    public static string GenerateETag(string content)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return $"\"{Convert.ToBase64String(hash)}\"";
    }

    /// <summary>
    /// Generates an ETag from a timestamp (useful for time-based cache validation)
    /// </summary>
    /// <param name="timestamp">The timestamp to generate an ETag for</param>
    /// <returns>A quoted ETag string suitable for HTTP headers</returns>
    public static string GenerateETag(DateTime timestamp)
    {
        return $"\"{timestamp.Ticks}\"";
    }

    /// <summary>
    /// Checks if the client's If-None-Match header matches the current ETag
    /// </summary>
    /// <param name="ifNoneMatch">The If-None-Match header value from the request</param>
    /// <param name="currentETag">The current ETag of the resource</param>
    /// <returns>True if the client's cached version is still valid</returns>
    public static bool IsNotModified(string? ifNoneMatch, string currentETag)
    {
        if (string.IsNullOrEmpty(ifNoneMatch))
            return false;

        // Handle multiple ETags in If-None-Match
        var clientETags = ifNoneMatch.Split(',').Select(e => e.Trim());
        return clientETags.Any(e => e == currentETag || e == "*");
    }

    /// <summary>
    /// Sets appropriate no-cache headers on the response
    /// Use for dynamic API data that should not be cached
    /// </summary>
    /// <param name="response">The HTTP response</param>
    public static void SetNoCacheHeaders(HttpResponse response)
    {
        response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
        response.Headers["Pragma"] = "no-cache";
        response.Headers["Expires"] = "0";
    }

    /// <summary>
    /// Sets ETag-based caching headers on the response
    /// Allows caching but requires revalidation with ETag
    /// </summary>
    /// <param name="response">The HTTP response</param>
    /// <param name="etag">The ETag value for the resource</param>
    /// <param name="maxAgeSeconds">Maximum age in seconds before revalidation (default: 0)</param>
    public static void SetETagHeaders(HttpResponse response, string etag, int maxAgeSeconds = 0)
    {
        response.Headers["ETag"] = etag;
        response.Headers["Cache-Control"] = $"private, must-revalidate, max-age={maxAgeSeconds}";
    }

    /// <summary>
    /// Sets headers for short-term caching with automatic refresh
    /// Good for data that changes occasionally (within 30 seconds for other users)
    /// </summary>
    /// <param name="response">The HTTP response</param>
    /// <param name="etag">The ETag value for the resource</param>
    public static void SetShortCacheHeaders(HttpResponse response, string etag)
    {
        response.Headers["ETag"] = etag;
        // Cache for up to 30 seconds, but revalidate afterward
        response.Headers["Cache-Control"] = "private, max-age=30, must-revalidate";
    }
}
