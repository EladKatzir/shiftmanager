using System.Collections.Concurrent;

namespace ShiftManager.Services;

/// <summary>
/// In-memory rate limiting service to prevent brute force attacks
/// Note: For multi-server deployments, use Redis or similar distributed cache
/// </summary>
public class RateLimitingService : IRateLimitingService
{
    private readonly ConcurrentDictionary<string, RateLimitEntry> _attempts = new();
    private readonly ILogger<RateLimitingService> _logger;

    public RateLimitingService(ILogger<RateLimitingService> logger)
    {
        _logger = logger;
    }

    public bool IsAllowed(string key, int maxAttempts, int windowMinutes)
    {
        CleanupExpiredEntries();

        var now = DateTime.UtcNow;
        var windowStart = now.AddMinutes(-windowMinutes);

        var entry = _attempts.GetOrAdd(key, _ => new RateLimitEntry());

        lock (entry)
        {
            // Remove attempts outside the time window
            entry.Attempts.RemoveAll(timestamp => timestamp < windowStart);

            // Check if limit exceeded
            if (entry.Attempts.Count >= maxAttempts)
            {
                _logger.LogWarning("Rate limit exceeded for key: {Key}. Attempts: {Count}/{Max}",
                    key, entry.Attempts.Count, maxAttempts);
                return false;
            }

            // Record this attempt
            entry.Attempts.Add(now);
            return true;
        }
    }

    public void Reset(string key)
    {
        _attempts.TryRemove(key, out _);
    }

    private void CleanupExpiredEntries()
    {
        // Periodically clean up old entries to prevent memory leak
        var cutoff = DateTime.UtcNow.AddHours(-1);
        foreach (var kvp in _attempts)
        {
            lock (kvp.Value)
            {
                if (kvp.Value.Attempts.All(t => t < cutoff))
                {
                    _attempts.TryRemove(kvp.Key, out _);
                }
            }
        }
    }

    private class RateLimitEntry
    {
        public List<DateTime> Attempts { get; } = new();
    }
}
