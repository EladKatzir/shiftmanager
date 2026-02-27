namespace ShiftManager.Services;

/// <summary>
/// Information about a rate-limited entry for admin display.
/// </summary>
public class RateLimitInfo
{
    public string Key { get; set; } = "";
    public int AttemptCount { get; set; }
    public DateTime? LastAttempt { get; set; }
}

/// <summary>
/// Service for rate limiting requests by IP address to prevent brute force attacks
/// </summary>
public interface IRateLimitingService
{
    /// <summary>
    /// Checks if a request from the given IP is allowed based on rate limits
    /// </summary>
    /// <param name="key">Unique key (e.g., IP address + endpoint)</param>
    /// <param name="maxAttempts">Maximum number of attempts allowed</param>
    /// <param name="windowMinutes">Time window in minutes</param>
    /// <returns>True if request is allowed, false if rate limited</returns>
    bool IsAllowed(string key, int maxAttempts, int windowMinutes);

    /// <summary>
    /// Resets the rate limit counter for a given key (e.g., after successful action)
    /// </summary>
    void Reset(string key);

    /// <summary>
    /// Returns active rate-limited entries matching a key prefix that are at or over the limit.
    /// </summary>
    IReadOnlyList<RateLimitInfo> GetActiveEntries(string keyPrefix, int maxAttempts, int windowMinutes);

    /// <summary>
    /// Resets all entries matching a key prefix. Returns the number of entries removed.
    /// </summary>
    int ResetByPrefix(string prefix);
}
